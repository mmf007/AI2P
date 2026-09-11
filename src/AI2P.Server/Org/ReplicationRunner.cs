using AI2P.Core.Entities;
using Serilog;

namespace AI2P.Server.Org;

/// <summary>
/// Автоматический запуск репликации (ТЗ гл. 6, этап 43; todo43 «логика запуска»).
///
/// Один таймер на установку. Правила:
/// <list type="bullet">
/// <item>интервал у сервера не задан — автоматически не реплицируем, только по кнопке;</item>
/// <item>время вышло — сеанс начинается сам; если это случилось на обоих концах пары,
/// второй запуск игнорируется (замок держит <see cref="ReplicationService"/>);</item>
/// <item>отсчёт до следующего сеанса начинается ПОСЛЕ окончания текущего, во время
/// репликации он остановлен;</item>
/// <item>репликация идёт ПО ОРГАНИЗАЦИИ: сначала одна организация целиком, потом другая.</item>
/// </list>
/// </summary>
public sealed class ReplicationRunner : IDisposable
{
    /// <summary>Как часто смотрим на часы. Минимальный интервал репликации — 30 с, так что
    /// пятисекундного тика с запасом хватает, а нагрузки он не создаёт.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    /// <summary>Как часто спрашивать у дирижёра решение по своей заявке (T-153): обмен
    /// дешёвый (один GET), но и торопиться некуда — решение принимает человек.</summary>
    private static readonly TimeSpan JoinTick = TimeSpan.FromSeconds(60);

    private readonly OrgRegistry _registry;
    private readonly ReplicationService _replication;
    private readonly AI2P.Connectors.SecretStore _secrets;
    private readonly CancellationTokenSource _stop = new();
    private Timer? _timer;
    private int _busy;
    private DateTime _joinsCheckedAt = DateTime.MinValue;

    /// <summary>Раздача файлов, на которые ссылаются задачи и чат (T-13-S0): владелец
    /// раскладывает их по каталогу данных организации, дальше они едут обычной файловой
    /// репликацией — в ту сторону, в которую звонок и так проходит.</summary>
    private readonly LinkedFileShare _share = new();

    private DateTime _sharedAt = DateTime.MinValue;

    /// <summary>Когда последний раз объявляли о смене дирижёра (T-21-S1).</summary>
    private DateTime _conductorCheckedAt = DateTime.MinValue;

    /// <summary>Когда последний раз спрашивали соседей, в какие организации нас
    /// включили (T-312).</summary>
    private DateTime _invitesCheckedAt = DateTime.MinValue;

    public ReplicationRunner(OrgRegistry registry, ReplicationService replication,
        AI2P.Connectors.SecretStore secrets)
    {
        _registry = registry;
        _replication = replication;
        _secrets = secrets;
    }

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }
        // ОБОРВАННЫЙ ПЕРЕЗАПУСКОМ СЕАНС (T-174): в момент старта не идёт ни один сеанс,
        // поэтому оставшийся в базе признак «running» — след прошлого запуска. Пока он там,
        // пара считается занятой и автоматический обход её пропускает навсегда
        var stuck = _registry.ReplState.ClearRunning();
        if (stuck > 0)
        {
            Log.Warning("Репликация: у {Count} пар(ы) осталось состояние «идёт сеанс» "
                        + "от прошлого запуска — снято (T-174)", stuck);
        }
        _timer = new Timer(_ => _ = TickAsync(), null, Tick, Tick);
    }

    /// <summary>Один проход по всем организациям и их партнёрам. Проходы не накладываются:
    /// сеанс может длиться минуты (первичная репликация), а таймер тикает каждые пять секунд.</summary>
    public async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }
        try
        {
            var now = DateTime.UtcNow;
            // ЗАЯВКА НА ПОДКЛЮЧЕНИЕ ДОВОДИТСЯ ДО КОНЦА САМА (T-153): пока подавший сервер
            // не спросил решение, организации у него нет — и реплицировать нечего, сколько
            // ни жди. Прежде это делала только кнопка «Узнать решение», и подключённый
            // сервер стоял мёртвым, хотя на дирижёре заявку давно приняли
            await FinishJoinsAsync(now);
            // СМЕНА ДИРИЖЁРА ДОХОДИТ ДО ВСЕХ САМА (T-21-S1): новый дирижёр объявляет о себе
            // тем серверам, с которыми пары токенов ещё нет. Раньше при подтверждении заявки
            // он их обошёл, но кто-то мог быть выключен — повторяем тем же ритмом, что и
            // опрос решений по заявкам на подключение
            await AnnounceConductorAsync(now);
            // ОРГАНИЗАЦИЯ, В КОТОРУЮ НАС ВКЛЮЧИЛИ ГАЛОЧКОЙ, ЗАБИРАЕТСЯ САМА (T-312): пока
            // её здесь нет, реплицировать нечего, а дирижёр до сервера за NAT не дозвонится
            // и приглашения не пришлёт — тупик держался бы вечно
            await PullInvitesAsync(now);
            // ФАЙЛЫ, НА КОТОРЫЕ ССЫЛАЮТСЯ ЗАДАЧИ И ЧАТ, — В РЕПЛИЦИРУЕМЫЙ КАТАЛОГ (T-13-S0).
            // Делается ДО обхода: разложенное уедет партнёрам этим же сеансом
            ShareLinkedFiles(now);
            // сначала одна организация целиком, потом другая (todo43)
            foreach (var org in _registry.Orgs.List(includeInactive: false))
            {
                foreach (var peer in _replication.Peers(org.Id))
                {
                    if (_stop.IsCancellationRequested)
                    {
                        return;
                    }
                    await RunIfDueAsync(org, peer, now);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Репликация: обход организаций прерван");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// РАЗДАТЬ ФАЙЛЫ, УПОМЯНУТЫЕ В ЗАДАЧАХ И ЧАТЕ (T-13-S0, повторный заход). Картинка,
    /// положенная агентом в папку проекта, лежит только на ЕГО сервере: папка пер-серверная
    /// и не реплицируется. Спросить её у соседа звонком удаётся не всегда — рядовой сервер
    /// сплошь и рядом стоит за NAT и назван петлевым адресом, и дирижёр до него не
    /// дозванивается вовсе (<see cref="ReplicationService.CannotCall"/>). Поэтому владелец
    /// раскладывает такие файлы по каталогу данных организации, а дальше их развозит обычная
    /// файловая репликация — тем сеансом, который и так идёт.
    ///
    /// Организации, где сервер один, проход не касается: раздавать некому.
    /// </summary>
    private void ShareLinkedFiles(DateTime now)
    {
        if (now - _sharedAt < LinkedFileShare.Period)
        {
            return;
        }
        _sharedAt = now;
        foreach (var org in _registry.Orgs.List(includeInactive: false))
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            if (_replication.Peers(org.Id).Count > 0)
            {
                _share.Run(_registry.Context(org));
            }
        }
    }

    /// <summary>
    /// Спросить у дирижёров решение по своим ЖДУЩИМ заявкам (T-153). Подтверждённая заявка
    /// заводит организацию здесь же (<see cref="Api.ClusterJoinFlow.PollAsync"/>), и со
    /// следующего тика начинается обычная репликация.
    ///
    /// Ошибки сюда не пускаются наружу: дирижёр может быть выключен, и обход остальных
    /// пар от этого страдать не должен.
    /// </summary>
    private async Task FinishJoinsAsync(DateTime now)
    {
        if (now - _joinsCheckedAt < JoinTick)
        {
            return;
        }
        _joinsCheckedAt = now;
        foreach (var target in _registry.Servers.List())
        {
            if (target.IsLocal || !OrgServerStatus.IsRequest(target.JoinStatus)
                || _stop.IsCancellationRequested)
            {
                continue;
            }
            try
            {
                var status = await Api.ClusterJoinFlow.PollAsync(_registry, _secrets, target,
                    actorId: null, _stop.Token);
                if (status.Status == OrgServerStatus.Active && status.Token.Length > 0)
                {
                    Log.Information("AI2P: заявка на подключение к серверу {Server} подтверждена — "
                                    + "организация заведена, начинается репликация", target.Name);
                }
            }
            catch (OperationCanceledException)
            {
                return;   // приложение останавливается
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Заявка на подключение к серверу {Server}: решение не спросить",
                    target.Name);
            }
        }
    }

    /// <summary>
    /// ЗАБРАТЬ ОРГАНИЗАЦИИ, В КОТОРЫЕ НАС ВКЛЮЧИЛИ (T-312). Тем же ритмом, что опрос решений
    /// по заявкам: один GET на сервер, у которого есть наш токен, и у сервера, знающего нас
    /// по всем своим организациям, ответ пустой. Правила и заведение — в
    /// <see cref="Api.ClusterJoinFlow.PullInvitesAsync"/>; ошибки туда же и не всплывают.
    /// </summary>
    private async Task PullInvitesAsync(DateTime now)
    {
        if (now - _invitesCheckedAt < JoinTick)
        {
            return;
        }
        _invitesCheckedAt = now;
        try
        {
            await Api.ClusterJoinFlow.PullInvitesAsync(_registry, _secrets, _stop.Token);
        }
        catch (OperationCanceledException)
        {
            // приложение останавливается
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Кластер: не удалось спросить, в какие организации нас включили");
        }
    }

    /// <summary>
    /// ОБЪЯВИТЬ О СЕБЕ ТЕМ, КТО ЕЩЁ НЕ ЗНАЕТ (T-21-S1). Признак дирижёра едет всем обычной
    /// репликацией, но этого мало: топология — звезда, и рядовой сервер, узнав о смене,
    /// не сможет позвать нового дирижёра, пока у пары нет токенов. А в аварийном случае
    /// (прежний дирижёр физически потерян) он и о самой смене не узнал бы — звонить ему
    /// больше некому. Поэтому новый дирижёр обходит таких серверов сам, тем же ритмом,
    /// что и опрос решений по заявкам на подключение.
    ///
    /// Проход дешёвый: <see cref="Api.ConductorFlow.NeedsAnnounce"/> смотрит только записи
    /// серверов, и в обычном кластере (пары давно есть) он сразу отвечает «некому».
    ///
    /// Тем же проходом убираются отжившие решённые заявки на смену дирижёра: убирает их
    /// автор, и удаление доезжает партнёрам обычным журналом изменений.
    /// </summary>
    private async Task AnnounceConductorAsync(DateTime now)
    {
        if (now - _conductorCheckedAt < JoinTick)
        {
            return;
        }
        _conductorCheckedAt = now;
        foreach (var org in _registry.Orgs.List(includeInactive: false))
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            // отжившие решённые заявки убирает их АВТОР (T-21-S1): удаление едет партнёрам
            // журналом изменений и убирает строку и у них
            try
            {
                _registry.Context(org).ConductorRequests.Cleanup();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Кластер {Org}: уборка заявок на смену дирижёра не удалась", org.Code);
            }
            if (!Api.ConductorFlow.NeedsAnnounce(_registry, org))
            {
                continue;
            }
            try
            {
                var done = await Api.ConductorFlow.AnnounceAsync(_registry, _secrets, org,
                    requestId: "", _stop.Token);
                if (done > 0)
                {
                    Log.Information("Кластер {Org}: о смене дирижёра оповещено серверов: {Count} "
                                    + "(T-21-S1)", org.Code, done);
                }
            }
            catch (OperationCanceledException)
            {
                return;   // приложение останавливается
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Кластер {Org}: объявление о смене дирижёра не удалось", org.Code);
            }
        }
    }

    private async Task RunIfDueAsync(Organization org, OrgServer peer, DateTime now)
    {
        var (interval, _) = _replication.Intervals(peer);
        if (interval is null)
        {
            return;   // «ручная»: автоматически не реплицируем (todo43)
        }
        // звонить некуда, и это не ошибка: сервер за NAT назвался петлевым адресом (T-141)
        // либо ещё не отозвался после подтверждения заявки (T-146). В обоих случаях сеанс
        // с такой парой начинает он сам
        if (_registry.Servers.Get(peer.ServerId) is { } node && _replication.CannotCall(node) is not null)
        {
            return;
        }
        var state = _registry.ReplState.Get(org.Id, peer.ServerId);
        // ИДЁТ ЛИ СЕАНС, ЗНАЕТ ТОЛЬКО ЗАМОК ПАРЫ (T-174). Признак «running» в базе — это
        // то, что показывают человеку, и он переживает падение приложения; полагаться
        // на него значит однажды перестать реплицировать вовсе
        if (_replication.IsRunning(org.Id, peer.ServerId))
        {
            return;   // идёт сеанс — отсчёт остановлен
        }
        if (state.NextRunAt is { } next && next > now)
        {
            return;
        }
        if (state.NextRunAt is null && state.LastRunAt is not null && state.LastError.Length == 0)
        {
            // срок не назначен, хотя сеанс был: интервал задали только что — начнём с него
            state.NextRunAt = now.AddSeconds(interval.Value);
            _registry.ReplState.Save(state);
            return;
        }
        try
        {
            await _replication.ReplicateAsync(org, peer, ReplicationStarters.Timer, _stop.Token);
        }
        catch (OperationCanceledException)
        {
            // приложение останавливается
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Репликация {Org} ↔ {Server}: сеанс не удался", org.Code, peer.Code);
        }
        // СРОК СЛЕДУЮЩЕГО СЕАНСА ОБЯЗАН БЫТЬ ВСЕГДА (T-50-S0). Проход, не назначивший срок,
        // повторяется КАЖДЫЙ тик — раз в пять секунд, — и со стороны это выглядит как
        // «репликация запускается постоянно», а строка списка вечно показывает «скоро»
        // (срока нет — показывать нечего). Так бывает, когда сеанс не состоялся вовсе:
        // замок пары занят партнёром, либо заход прервался мимо обычного завершения
        var after = _registry.ReplState.Get(org.Id, peer.ServerId);
        if (after.NextRunAt is null && !_replication.IsRunning(org.Id, peer.ServerId))
        {
            after.NextRunAt = now.AddSeconds(interval.Value);
            _registry.ReplState.Save(after);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _timer?.Dispose();
        _timer = null;
        _stop.Dispose();
    }
}

/// <summary>Кто начал сеанс репликации — попадает в подсказку статуса.</summary>
public static class ReplicationStarters
{
    public const string Timer = "timer";
    public const string Manual = "manual";
    public const string Peer = "peer";
}
