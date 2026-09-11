using AI2P.Core;
using System.Collections.Concurrent;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Connectors;
using AI2P.Core.Events;
using AI2P.Server.Api;
using AI2P.Storage;

namespace AI2P.Server.Org;

/// <summary>
/// РЕПЛИКАЦИЯ БАЗ ДАННЫХ (ТЗ п. 6.1, гл. 6; этап 43).
///
/// Механизм — журнал изменений строк (<see cref="ChangeLog"/>) плюс курсоры: «отдай мне
/// изменения после N». Конфликт (он возможен только при дирижёрской правке чужой строки —
/// у остальных строк ровно один сервер-писатель) разрешается по большему <c>(ts, node_id)</c>.
///
/// Топология — ЗВЕЗДА: все серверы реплицируются только с дирижёром (todo43). Сеанс идёт
/// по ОДНОЙ организации целиком: сначала одна, потом другая.
///
/// Сеанс двусторонний и его ведёт ОДНА сторона — та, у которой раньше истёк отсчёт. Вторая
/// в этот момент получает отказ на «начать сеанс» и просто пропускает свой запуск. Ведущий
/// делает три вещи: забирает изменения партнёра, отдаёт свои и записывает курсоры — свои
/// и (ответами протокола) партнёрские.
///
/// Первичной репликации как отдельного протокола нет: журнал при создании наполняется
/// существующими строками, поэтому «изменения после 0» — это снимок базы целиком.
/// </summary>
public sealed class ReplicationService : IDisposable
{
    /// <summary>Сколько изменений едет в одной пачке: компромисс между числом запросов
    /// и размером тела. При первичной репликации пачек будет много — это и даёт прогресс.</summary>
    public const int BatchSize = 400;

    private readonly OrgRegistry _registry;
    private readonly ClusterClient _client = new();

    /// <summary>Репликация ФАЙЛОВ (этап 44): идёт в том же сеансе, после базы (todo44).</summary>
    private readonly FileReplicationService _files;

    /// <summary>Идущие сеансы: ключ — «организация|партнёр». Он же замок «второй запуск
    /// игнорируется» — и для нашего таймера, и для входящего запроса партнёра (todo43).</summary>
    private readonly ConcurrentDictionary<string, ReplicationProgress> _running = new();

    /// <summary>
    /// НАШИ СОБСТВЕННЫЕ ЗВОНКИ ПАРТНЁРУ (T-284): когда мы звонили ему в прошлый раз и когда
    /// такой звонок в последний раз удался. В базе этого нет и быть не может: с T-20-S1
    /// <c>repl_state.last_run_at</c> отмечает сеанс пары в ЛЮБУЮ сторону, а <c>last_ok_at</c> —
    /// удачное окончание тоже любого, в том числе начатого партнёром. Память живёт в процессе,
    /// как и замки пары (T-174): после перезапуска первая же неудача снова показывается
    /// человеку — пока мы не знаем, кто в этой паре звонит, молчать нельзя.
    /// </summary>
    private readonly ConcurrentDictionary<string, OutboundCall> _outbound = new();

    /// <inheritdoc cref="_outbound"/>
    private sealed class OutboundCall
    {
        public DateTime? TriedAt { get; set; }
        public DateTime? OkAt { get; set; }
    }

    /// <summary>Репликация АРХИВОВ (T-47-S0): идёт в том же сеансе, последним шагом.</summary>
    private readonly ArchiveReplicationService _archives;

    public ReplicationService(OrgRegistry registry)
    {
        _registry = registry;
        _files = new FileReplicationService(registry, _client);
        _archives = new ArchiveReplicationService(registry, _client, _files);
    }

    /// <summary>Файловая часть репликации — ею же пользуется пассивная сторона сеанса
    /// (раздел <c>/api/cluster/repl/files</c>).</summary>
    public FileReplicationService Files => _files;

    /// <summary>Архивная часть репликации (T-47-S0): ею же пользуется скачивание архива
    /// с другого сервера (раздел <c>/api/archives/{id}/download</c>).</summary>
    public ArchiveReplicationService Archives => _archives;

    /// <summary>
    /// База, в которую применяется этот вид обмена (T-47-S0). Видов теперь три: серверная
    /// БД, база организации и база КОНКРЕТНОГО АРХИВА (<c>arc:&lt;id&gt;</c>). Архив, которого
    /// здесь нет, заводится каталогом — так текущий архив и приезжает на сервер, где его
    /// ещё не было; закрытый .zip'ом не трогается вовсе, и тогда возвращается null.
    /// </summary>
    public Database? DbForScope(OrgContext context, string scope)
    {
        if (scope == ReplScopes.Server)
        {
            return _registry.ServerDb;
        }
        var archiveId = ReplScopes.ArchiveOf(scope);
        if (archiveId.Length == 0)
        {
            return context.Db;
        }
        var archive = context.Archives.Get(archiveId);
        return archive is null ? null : context.Archives.DbForReplication(archive);
    }

    public static string Key(string orgId, string serverId) => orgId + "|" + serverId;

    /// <summary>
    /// Почему сеанс с этим партнёром начать НЕЛЬЗЯ; null — можно. Две причины, и обе значат
    /// «звонить некуда, он позвонит сам»:
    /// <list type="bullet">
    /// <item>партнёр за NAT и назвался петлевым адресом (T-141);</item>
    /// <item>партнёр ещё не выдал нам встречный токен доступа (T-146). Токен он присылает
    /// сам, когда узнаёт о подтверждении своей заявки, — то есть между «ПРИНЯТЬ» на дирижёре
    /// и первым обращением подключившегося сервера пары ещё нет. Это нормальное состояние
    /// первых секунд подключения, а не поломка.</item>
    /// </list>
    /// </summary>
    public string? CannotCall(ServerNode target) =>
        ServerAddress.Unreachable(target, _registry.Servers.Local())
        ?? (_registry.Servers.PeerToken(target.Id).Length == 0
            ? Loc.T("msg.replication.1", target.Name)
            : null);

    /// <summary>Ход текущего сеанса; null — сеанса нет. ПРОСРОЧЕННЫЙ замок сеансом
    /// не считается (T-174): иначе в списке серверов вечно висел бы прогресс-бар с 0%.</summary>
    public ReplicationProgress? Progress(string orgId, string serverId) =>
        _running.GetValueOrDefault(Key(orgId, serverId)) is { } held && !held.IsStale(DateTime.UtcNow)
            ? held
            : null;

    /// <summary>Сеанс с этим партнёром уже идёт (в том числе начатый им самим).
    /// Просроченный замок — не сеанс (T-174).</summary>
    public bool IsRunning(string orgId, string serverId) => Progress(orgId, serverId) is not null;

    /// <summary>
    /// «Сеанс жив»: любое движение по паре отодвигает срок протухания замка (T-174). Ведущий
    /// зовёт это сам, двигая <see cref="ReplicationProgress.Done"/>, а ПАССИВНАЯ сторона —
    /// на каждый входящий запрос партнёра (<see cref="Api.ClusterEndpoints"/>).
    /// </summary>
    public void Touch(string orgId, string serverId) =>
        _running.GetValueOrDefault(Key(orgId, serverId))?.Touch();

    /// <summary>
    /// Занять пару под сеанс. false — сеанс уже идёт, и наш запуск игнорируется: это ровно
    /// то, что требуется, когда отсчёт истёк на обоих серверах одновременно (todo43).
    ///
    /// ЗАМОК НЕ БЫВАЕТ ВЕЧНЫМ (T-174). Замок держится в памяти, а снимает его окончание
    /// сеанса — своего в <c>finally</c>, чужого запросом <c>/repl/end</c>. Второе приходит
    /// не всегда: ведущий может упасть, перезапуститься или потерять связь посреди сеанса,
    /// и тогда пара оставалась занятой ДО перезапуска этого сервера. Со стороны это выглядело
    /// так, как в T-174: у дирижёра вечный прогресс-бар с 0% (замок есть, движения нет),
    /// у партнёра — вечный обратный отсчёт (на «начать сеанс» ему отвечают «занято»),
    /// ручной пуск молчит, ошибок нет. Поэтому замок без движения дольше
    /// <see cref="ReplicationProgress.StaleAfter"/> перехватывается.
    /// </summary>
    /// <param name="progress">Ход НАШЕГО сеанса, если пара занята нами; при отказе — ход
    /// того сеанса, который уже идёт (его называют человеку при ручном пуске).</param>
    public bool TryHold(string orgId, string serverId, string startedBy, out ReplicationProgress progress)
    {
        var key = Key(orgId, serverId);
        var mine = new ReplicationProgress { StartedBy = startedBy };
        while (true)
        {
            if (_running.TryAdd(key, mine))
            {
                progress = mine;
                return true;
            }
            if (!_running.TryGetValue(key, out var held))
            {
                continue;   // замок сняли между двумя нашими шагами — пробуем занять снова
            }
            if (!held.IsStale(DateTime.UtcNow))
            {
                progress = held;
                return false;
            }
            if (_running.TryUpdate(key, mine, held))
            {
                Serilog.Log.Warning("Репликация: замок пары {Key} висел без движения с {Since} "
                                    + "(начал {Who}) — сеанс считается оборванным, замок снят (T-174)",
                    key, held.TouchedAt, held.StartedBy);
                progress = mine;
                return true;
            }
        }
    }

    /// <summary>
    /// Занять пару под сеанс, который ведёт ПАРТНЁР (запрос <c>/repl/begin</c>).
    ///
    /// Повторное «начать сеанс» от того же партнёра — всегда согласие (T-174): замок пары
    /// заводится на конкретного партнёра, и если он занят ЕГО же прошлым сеансом, значит
    /// тот сеанс кончился, не закрывшись (партнёр перезапустился, оборвалась связь). Ждать
    /// протухания незачем — раз он звонит снова, прошлого сеанса больше нет. Занято НАМИ
    /// (свой ручной или таймерный сеанс) — по-прежнему «занято», и его запуск пропускается.
    /// </summary>
    public bool HoldForPeer(string orgId, string serverId, out ReplicationProgress progress)
    {
        var key = Key(orgId, serverId);
        if (_running.TryGetValue(key, out var held)
            && held.StartedBy == ReplicationStarters.Peer
            && _running.TryRemove(new KeyValuePair<string, ReplicationProgress>(key, held)))
        {
            Serilog.Log.Warning("Репликация: партнёр начинает сеанс {Key} заново, а прошлый его "
                                + "сеанс (с {Since}) не был закрыт — замок снят (T-174)",
                key, held.TouchedAt);
        }
        return TryHold(orgId, serverId, ReplicationStarters.Peer, out progress);
    }

    public void Release(string orgId, string serverId) => _running.TryRemove(Key(orgId, serverId), out _);

    // --- партнёры по репликации ---

    /// <summary>
    /// С кем реплицируется этот сервер в организации (ТЗ гл. 6, todo43): все реплицируются
    /// ТОЛЬКО с дирижёром, поэтому у рядового сервера партнёр один — дирижёр, а у дирижёра
    /// партнёры — все остальные подключённые серверы организации.
    /// </summary>
    public List<OrgServer> Peers(string orgId)
    {
        var localId = _registry.Servers.LocalId();
        var links = _registry.Servers.ServersOf(orgId, includeRequests: false)
            .Where(l => l.Status == OrgServerStatus.Active && l.IsActive && l.ServerId != localId)
            .ToList();
        return _registry.Servers.IsLocalConductor(orgId)
            ? links
            : links.Where(l => l.IsConductor).ToList();
    }

    /// <summary>
    /// Интервалы отсчёта для пары. Принадлежат записи РЯДОВОГО сервера: дирижёр задаёт их
    /// всем, сам сервер — только себе, и оба конца пары считают по одному значению
    /// (todo43 «Если это происходит на обоих серверах, то второй запуск игнорируется»).
    /// </summary>
    public (int? IntervalSec, int? RetrySec) Intervals(OrgServer peer)
    {
        var owner = peer.IsConductor ? _registry.Servers.Local() : _registry.Servers.Get(peer.ServerId);
        return (owner?.ReplIntervalSec, owner?.ReplRetrySec);
    }

    // --- сеанс ---

    /// <summary>
    /// Провести сеанс репликации организации с одним партнёром. Возвращает false, если
    /// сеанс не начался: он уже идёт здесь либо на той стороне (второй запуск игнорируется).
    /// </summary>
    public async Task<bool> ReplicateAsync(Organization org, OrgServer peer, string startedBy,
        CancellationToken ct = default)
    {
        if (!TryHold(org.Id, peer.ServerId, startedBy, out var progress))
        {
            return false;
        }
        var state = _registry.ReplState.Get(org.Id, peer.ServerId);
        // ЧТО БЫЛО С ПАРОЙ ДО ЭТОГО ЗАХОДА (T-284): время прошлого удачного сеанса (чьего
        // угодно) и время НАШЕГО прошлого звонка. По ним разбирается случай «звонит он сам»,
        // когда наш заход не удаётся, а обмен при этом идёт (см. PartnerDrives)
        var okBefore = state.LastOkAt;
        var memo = _outbound.GetOrAdd(Key(org.Id, peer.ServerId), _ => new OutboundCall());
        var triedBefore = memo.TriedAt;
        memo.TriedAt = DateTime.UtcNow;
        state.Status = ReplicationStatuses.Running;
        state.LastRunAt = DateTime.UtcNow;
        // «принято» и «отдано» — за ПОСЛЕДНИЙ сеанс: копить их за всё время бессмысленно,
        // на экране диагностики нужен масштаб текущего обмена. Конфликты, наоборот,
        // накапливаются: их считают до тех пор, пока человек не разберётся и не сбросит
        state.Received = 0;
        state.Sent = 0;
        // во время репликации отсчёт до следующей останавливается (todo43)
        state.NextRunAt = null;
        _registry.ReplState.Save(state);
        var target = _registry.Servers.Get(peer.ServerId);
        try
        {
            if (target is null)
            {
                throw new InvalidOperationException(Loc.T("msg.replication.2"));
            }
            // ЗВОНИТЬ ПОКА НЕКУДА: сервер за NAT (T-141) либо ещё не отозвавшийся после
            // подтверждения заявки (T-146). Автоматический обход такие пары пропускает молча
            // (ReplicationRunner), а по кнопке человек получает объяснение
            if (CannotCall(target) is { } silence)
            {
                throw new InvalidOperationException(silence);
            }
            var token = _registry.Servers.PeerToken(target.Id);
            // ПАРТНЁР МОЖЕТ ЕЩЁ НЕ ЗНАТЬ ЭТОЙ ОРГАНИЗАЦИИ (T-170-S0): его включили в неё
            // галочкой в форме организации, а не заявкой с его стороны. Тогда сеанс
            // отвергается словами «организация не найдена», и так навсегда — приглашаем
            await InviteIfNeededAsync(org, peer, target, token, state, ct);
            var session = await _client.ReplBeginAsync(target, token, org.Code, ct);
            if (session.Busy)
            {
                // сеанс уже ведёт партнёр — свой запуск пропускаем целиком (todo43),
                // но отсчёт всё равно перезаводим: иначе мы бы долбились каждую секунду.
                //
                // В ЖУРНАЛ ЭТО ПОПАДАЕТ (T-174): «ничего не происходит и ошибок нет» —
                // ровно то, что видит человек, когда у партнёра завис замок пары. Строчка
                // в журнале называет причину, не выдавая её за ошибку связи
                Serilog.Log.Warning("Репликация {Org} → {Server}: партнёр отвечает «сеанс уже "
                                    + "идёт» — свой запуск пропускаем (ТЗ гл. 6)", org.Code, target.Name);
                // до сервера мы ДОЗВОНИЛИСЬ — прошлая жалоба на связь устарела (T-284)
                memo.OkAt = DateTime.UtcNow;
                ClearServerError(target);
                Reschedule(state, peer);
                return false;
            }
            var context = _registry.Context(org);
            progress.Total = Math.Max(1,
                session.OrgHead - state.PullCursor + session.ServerHead - state.ServerPullCursor
                + Outgoing(context.Db, state.PushCursor, target.Id)
                + Outgoing(_registry.ServerDb, state.ServerPushCursor, target.Id));

            // 1) забираем чужое, 2) отдаём своё — по обеим базам (ТЗ п. 6.4.1).
            // Отвергнутое базой пробуется заново сразу после вычитки (T-160): чаще всего
            // не хватало родительской строки, а она приехала одной из следующих пачек
            await PullAsync(target, token, org, context, state, ReplScopes.Org, progress, ct);
            RetryPending(target, org, context, state, ReplScopes.Org);
            await PullAsync(target, token, org, context, state, ReplScopes.Server, progress, ct);
            RetryPending(target, org, context, state, ReplScopes.Server);
            await PushAsync(target, token, org, context, state, ReplScopes.Org, progress, ct);
            await PushAsync(target, token, org, context, state, ReplScopes.Server, progress, ct);

            // 3) ФАЙЛЫ — в том же сеансе, но ПОСЛЕ базы (todo44): каталог данных организации
            // и папки Common проектов. Пока идёт этот шаг, отсчёт до следующей репликации
            // не запускается — он заводится только в Finish
            state.FilesReceived = 0;
            state.FilesSent = 0;
            state.BytesReceived = 0;
            state.BytesSent = 0;
            var filesFailed = await _files.RunAsync(target, token, org, context, state, progress, ct);

            // 4) АРХИВЫ (T-47-S0) — ПОСЛЕ рабочей среды и тоже в этом сеансе. Реплицируется
            // только ТЕКУЩИЙ архив; если он сменился, первым идёт последний досыл прежнего,
            // и лишь в самом конце прежний перестаёт быть текущим у обеих сторон
            await _archives.RunAsync(target, token, org, context, state, progress, ct);
            // ДОСЫЛ ХВОСТА ЖУРНАЛА (T-47-S0). Шаг архивов сам пишет в базу организации:
            // объявляет соседям, что архив у нас появился или исчез (archive_servers).
            // Пуш базы шёл выше, до архивов, и без этого второго прохода отметка «архив
            // есть и у меня» доезжала бы только следующим сеансом — то есть человек ещё
            // несколько минут видел бы в списке архивов один сервер вместо двух.
            // Проход дешёвый: журнал к этому моменту почти пуст
            await PushAsync(target, token, org, context, state, ReplScopes.Org, progress, ct);
            _registry.ReplState.Save(state);

            await _client.ReplEndAsync(target, token, org.Code, ct);
            // код организации мог приехать изменённым — маршрутизация держит их в памяти
            _registry.RefreshCodes();
            // ФАЙЛЫ, КОТОРЫЕ НЕ ПЕРЕЕХАЛИ (T-50-S0). Сеанс из-за них не рвётся — обмен базой
            // и остальными файлами состоялся, — но выдавать его за полностью удачный нельзя:
            // пара помечается ошибкой с именами файлов, и человек видит их в диагностике.
            // Связь при этом есть, поэтому жалоба на связь с записи сервера всё равно снимается
            Finish(state, peer, ok: filesFailed.Count == 0,
                error: filesFailed.Count == 0 ? "" : FilesFailedText(filesFailed),
                reached: true);
            // СЕАНС УДАЛСЯ — КРАСНОЙ СТРОКИ БОЛЬШЕ НЕТ (T-284). Пассивная сторона снимала
            // жалобу на связь ещё с T-153 (/repl/end), а ведущая — нет: ошибка прошлого
            // захода оставалась в записи сервера навсегда и висела внизу его формы, хотя
            // репликация с ним идёт каждые несколько минут
            memo.OkAt = DateTime.UtcNow;
            ClearServerError(target);
            // приехало ли что-то, требующее действия ЗДЕСЬ (T-196): заявка на запуск нашей
            // задачи с соседнего сервера и ответ человека на вопрос нашего агента. И то и
            // другое — обычные строки, своего канала «репликация → оркестратор» нет; ждать
            // прохода сторожа (минута) незачем, раз сеанс только что кончился
            AfterReplication(context);
            return true;
        }
        // ЛЮБАЯ БЕДА ОБЯЗАНА ЗАКОНЧИТЬ СЕАНС (T-50-S0, повторный заход). Перечень видов
        // исключений был ЗАКРЫТЫМ, и первое же не названное в нём (у заказчика —
        // FileNotFoundException на пустом файле) улетало наружу мимо Finish: пара навсегда
        // оставалась в состоянии «идёт сеанс» с пустым текстом ошибки, а старая жалоба на
        // связь в записи сервера не снималась и не заменялась. На экране это выглядело как
        // «репликация всё время идёт, ошибок нет», хотя обмена не было вовсе. Отмена по
        // нашему признаку (остановка приложения) ошибкой не считается, а вот ТАЙМАУТ считается:
        // HttpClient сообщает о нём тем же TaskCanceledException, и отличить одно от другого
        // можно только по нашему признаку отмены
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Finish(state, peer, ok: false, error: ex.Message);
            if (target is not null)
            {
                // ЗВОНИМ НЕ МЫ — И ЭТО НЕ ПОЛОМКА (T-284). Партнёр, до которого нам не
                // дозвониться (закрытый порт, свой межсетевой экран, NAT с настоящим адресом
                // в записи), сам ведёт сеансы с нами, и пара живёт. Красная строка в форме
                // сервера в этом случае вводит в заблуждение: обмен идёт. Причина остаётся
                // в журнале и в состоянии пары — на экране диагностики видно всё
                if (PartnerDrives(okBefore, triedBefore, memo.OkAt))
                {
                    Serilog.Log.Information("Репликация {Org} → {Server}: наш заход не удался "
                        + "({Error}), но пара реплицируется — сеансы ведёт партнёр, "
                        + "в запись сервера это не пишем (T-284)", org.Code, target.Name, ex.Message);
                }
                else
                {
                    _registry.Servers.SetLastError(target.Id, ex.Message);
                }
            }
            return true;
        }
        finally
        {
            Release(org.Id, peer.ServerId);
        }
    }

    /// <summary>
    /// ПРИГЛАСИТЬ ПАРТНЁРА В ОРГАНИЗАЦИЮ, ЕСЛИ ОН О НЕЙ ЕЩЁ НЕ ЗНАЕТ (T-170-S0).
    ///
    /// Сервер попадает в организацию двумя путями: заявкой со своей стороны (тогда он и
    /// заводит её у себя, <see cref="Api.ClusterJoinFlow"/>) — и галочкой в форме организации
    /// НА ДИРИЖЁРЕ (todo41 п. 11). Второй путь до этой правки не работал вовсе: у партнёра
    /// организации нет, и на «начать сеанс» он отвечает «организация не найдена» — навсегда.
    /// Человек при этом видел в форме отмеченный сервер и ждал репликации, которой не будет.
    ///
    /// Шлёт ДИРИЖЁР и только пока пара НИ РАЗУ не отреплицировалась удачно: это один дешёвый
    /// запрос перед началом сеанса, а у партнёра, который организацию уже завёл, он ничего
    /// не делает. Ошибка приглашения не глушится: если позвать партнёра нельзя, то и сеанса
    /// не будет — человек увидит причину там же, где увидел бы её от самого сеанса.
    /// </summary>
    private async Task InviteIfNeededAsync(Organization org, OrgServer peer, ServerNode target,
        string token, ReplicationState state, CancellationToken ct)
    {
        if (state.LastOkAt is not null || !_registry.Servers.IsLocalConductor(org.Id))
        {
            return;
        }
        // сводка ровно та же, что уезжает подавшему заявку в ответ на её подтверждение:
        // идентификаторы организации, коды обоих серверов, токен для обращения к нам
        // и ключ организации (ТЗ гл. 10)
        var invite = Api.ClusterEndpoints.Status(_registry, peer, _registry.Context(org).OrgKeys);
        var ack = await _client.OrgInviteAsync(target, token, invite, ct);
        if (ack.Applied)
        {
            Serilog.Log.Information("Репликация {Org} → {Server}: партнёр не знал этой "
                                    + "организации — приглашён и завёл её у себя (T-170-S0)",
                org.Code, target.Name);
        }
    }

    /// <summary>
    /// Что делать сразу после удачного сеанса (T-196). Репликация привозит не только данные,
    /// но и ПОРУЧЕНИЯ: заявку «запусти свою задачу», поданную человеком с другого сервера, и
    /// ответ на вопрос нашего агента, написанный там же. Оба разбирает оркестратор — здесь
    /// он вызывается сразу, не дожидаясь минутного прохода сторожа. Работа фоновая: сеанс
    /// репликации не должен ждать запуска агентов.
    /// </summary>
    public static void AfterReplication(OrgContext context) =>
        _ = Task.Run(() =>
        {
            try
            {
                context.Orchestrator.ApplyRunRequestsOnce();
                context.Orchestrator.ResumeAnsweredOnce();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Разбор поручений после сеанса репликации не удался: {Error}",
                    ex.Message);
            }
        });

    /// <summary>Забрать изменения партнёра пачками и применить их у себя.</summary>
    private async Task PullAsync(ServerNode target, string token, Organization org, OrgContext context,
        ReplicationState state, string scope, ReplicationProgress progress, CancellationToken ct)
    {
        var db = scope == ReplScopes.Server ? _registry.ServerDb : context.Db;
        var cursor = scope == ReplScopes.Server ? state.ServerPullCursor : state.PullCursor;
        progress.Phase = scope == ReplScopes.Server ? ReplPhases.PullServer : ReplPhases.Pull;
        // время прошлого УДАЧНОГО сеанса берётся до начала этого: им определяется, менялся ли
        // ключ API у нас с тех пор (см. AcceptKey)
        var lastOkAt = state.LastOkAt;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _client.ReplChangesAsync(target, token, org.Code, scope, cursor, BatchSize, ct);
            if (EndOfChanges(page, cursor))
            {
                break;
            }
            var result = ChangeLog.Apply(db, page.Items,
                Guard(scope, org, context, target, lastOkAt));
            cursor = page.NextCursor;
            state.Received += result.Applied;
            Count(state, scope, result.Conflicts, result.LastConflict);
            // строки, принятые без родителя (T-169-S0), и строки, отвергнутые базой: первые
            // называются в журнале, вторые откладываются и не держат ни пачку, ни сеанс (T-160)
            NoteAdopted(target, org, result);
            Quarantine(target, org, state, scope, result);
            if (scope == ReplScopes.Server)
            {
                state.ServerPullCursor = cursor;
            }
            else
            {
                state.PullCursor = cursor;
            }
            _registry.ReplState.Save(state);
            progress.Done += page.Items.Count;
            if (page.Remaining <= 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// ПУСТАЯ ПАЧКА — НЕ ВСЕГДА КОНЕЦ (T-160). Из СЕРВЕРНОЙ БД партнёр отдаёт не всё подряд,
    /// а только относящееся к организации сеанса, и целая пачка может оказаться отсеянной.
    /// Раньше вычитка на этом кончалась, а курсор партнёра уезжал на голову журнала — то,
    /// что шло дальше, не приезжало никогда. Теперь пустая пачка двигает курсор и вычитка
    /// продолжается; концом считается только «курсор не сдвинулся».
    /// </summary>
    private static bool EndOfChanges(ReplChangesDto page, long cursor) =>
        page.Items.Count == 0 && page.NextCursor <= cursor;

    /// <summary>
    /// СТРОКИ, ПРИНЯТЫЕ БЕЗ РОДИТЕЛЯ (T-169-S0), — назвать в журнале. Зовётся на ОБЕИХ
    /// сторонах и отдельно от <see cref="Quarantine"/>: отложенных строк при этом может
    /// не быть вовсе, а знать, что у исполнителя ссылка на модель повисла в пустоту, надо.
    /// </summary>
    private static void NoteAdopted(ServerNode target, Organization org, ApplyResult result)
    {
        foreach (var adopted in result.Adopted)
        {
            Serilog.Log.Warning("Репликация {Org} ← {Server}: строка {Row} принята без строки, "
                                + "на которую ссылается ({Reason}) — ссылка починится, когда та "
                                + "приедет (T-169-S0)",
                org.Code, target.Name,
                $"{adopted.Change.Table} {adopted.Change.Pk}", adopted.Reason);
        }
    }

    /// <summary>Отложить отвергнутые базой строки и показать это в состоянии пары (T-160).</summary>
    private void Quarantine(ServerNode target, Organization org, ReplicationState state,
        string scope, ApplyResult result)
    {
        if (result.Failed.Count > 0)
        {
            _registry.ReplPending.Save(org.Id, target.Id, scope, result.Failed);
            foreach (var failure in result.Failed)
            {
                Serilog.Log.Warning("Репликация {Org} ← {Server}: строку {Row} принять не удалось "
                                    + "({Reason}) — отложена до следующего сеанса",
                    org.Code, target.Name, $"{failure.Change.Table} {failure.Change.Pk}", failure.Reason);
            }
            state.Pending = _registry.ReplPending.Count(org.Id, target.Id);
            state.LastPending = _registry.ReplPending.Last(org.Id, target.Id);
        }
    }

    /// <summary>
    /// Повторить отложенные строки (T-160): родитель, которого не хватало, мог приехать
    /// следующей пачкой этого же сеанса. Применившиеся уходят из очереди, оставшиеся ждут
    /// следующего сеанса — и так до тех пор, пока не пройдут либо пока человек не разберётся.
    /// </summary>
    private void RetryPending(ServerNode target, Organization org, OrgContext context,
        ReplicationState state, string scope)
    {
        var pending = _registry.ReplPending.Take(org.Id, target.Id, scope);
        if (pending.Count == 0)
        {
            return;
        }
        var db = scope == ReplScopes.Server ? _registry.ServerDb : context.Db;
        var result = ChangeLog.Apply(db, pending, Guard(scope, org, context, target, state.LastOkAt));
        NoteAdopted(target, org, result);
        var stuck = result.Failed.Select(f => f.Change.Table + "|" + f.Change.Pk)
            .ToHashSet(StringComparer.Ordinal);
        _registry.ReplPending.Remove(org.Id, target.Id, scope,
            pending.Where(p => !stuck.Contains(p.Table + "|" + p.Pk)));
        state.Received += result.Applied;
        Count(state, scope, result.Conflicts, result.LastConflict);
        _registry.ReplPending.Save(org.Id, target.Id, scope, result.Failed);
        state.Pending = _registry.ReplPending.Count(org.Id, target.Id);
        state.LastPending = _registry.ReplPending.Last(org.Id, target.Id);
        _registry.ReplState.Save(state);
    }

    /// <summary>Отдать партнёру свои изменения пачками.</summary>
    private async Task PushAsync(ServerNode target, string token, Organization org, OrgContext context,
        ReplicationState state, string scope, ReplicationProgress progress, CancellationToken ct)
    {
        var db = scope == ReplScopes.Server ? _registry.ServerDb : context.Db;
        var cursor = scope == ReplScopes.Server ? state.ServerPushCursor : state.PushCursor;
        progress.Phase = scope == ReplScopes.Server ? ReplPhases.PushServer : ReplPhases.Push;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            List<RowChange> items;
            using (var conn = db.Open())
            {
                items = ChangeLog.Read(conn, cursor, BatchSize, target.Id);
                if (scope == ReplScopes.Server)
                {
                    items = Visible(conn, items, org.Id, context);
                }
            }
            if (items.Count == 0)
            {
                // пусто может быть и потому, что всё отсеяно фильтром видимости, —
                // тогда курсор всё равно надо подвинуть, иначе будем перечитывать вечно
                var head = HeadAfter(db, cursor, target.Id, BatchSize);
                if (head <= cursor)
                {
                    break;
                }
                cursor = head;
                Store(state, scope, cursor);
                continue;
            }
            var last = items[^1].Seq;
            var result = await _client.ReplApplyAsync(target, token, new ReplApplyDto
            {
                OrgCode = org.Code,
                Scope = scope,
                Items = items,
                SourceCursor = last,
            }, ct);
            cursor = last;
            state.Sent += result.Applied;
            Count(state, scope, result.Conflicts, result.LastConflict);
            if (result.Failed > 0)
            {
                // партнёр отложил часть нашей пачки у себя (T-160) — пачку он не отклоняет,
                // но знать об этом надо: разбираться придётся на той стороне
                Serilog.Log.Warning("Репликация {Org} → {Server}: партнёр отложил {Count} строк "
                                    + "({Reason})", org.Code, target.Name, result.Failed, result.LastFailed);
            }
            Store(state, scope, cursor);
            _registry.ReplState.Save(state);
            progress.Done += items.Count;
        }
    }

    /// <summary>
    /// Учесть конфликты — только по базе ОРГАНИЗАЦИИ. В серверной БД проигрыш по LWW
    /// штатен и ни о чём не говорит: при подключении обе стороны заводят записи об одних
    /// и тех же серверах и связях независимо, и одна из версий обязана проиграть. Считать
    /// это конфликтом значило бы встречать человека шестью «конфликтами» на первом же
    /// соединении. Расхождение, в котором надо разбираться, бывает только в данных
    /// организации — там у строки один сервер-писатель (ТЗ гл. 6).
    /// </summary>
    private static void Count(ReplicationState state, string scope, int conflicts, string last)
    {
        if (scope == ReplScopes.Server || conflicts == 0)
        {
            return;
        }
        state.Conflicts += conflicts;
        if (last.Length > 0)
        {
            state.LastConflict = last;
        }
    }

    private static void Store(ReplicationState state, string scope, long cursor)
    {
        if (scope == ReplScopes.Server)
        {
            state.ServerPushCursor = cursor;
        }
        else
        {
            state.PushCursor = cursor;
        }
    }

    /// <summary>Докуда можно подвинуть курсор, если вся пачка отсеяна фильтром видимости.</summary>
    private static long HeadAfter(Database db, long after, string exceptNode, int limit)
    {
        using var conn = db.Open();
        return Sql.Scalar<long?>(conn, null, """
            SELECT MAX(seq) FROM (SELECT seq FROM changes WHERE seq>@a ORDER BY seq LIMIT @l)
            """, ("@a", after), ("@l", limit)) ?? after;
    }

    /// <summary>Сколько наших изменений партнёр ещё не забрал (отставание, экран диагностики).</summary>
    public static long Outgoing(Database db, long cursor, string peerNode)
    {
        using var conn = db.Open();
        return ChangeLog.CountAfter(conn, cursor, peerNode);
    }

    /// <summary>
    /// Снять с записи сервера жалобу на связь (T-284). Зовётся всякий раз, когда мы до него
    /// ДОЗВОНИЛИСЬ: удачный сеанс и «сеанс уже идёт» — оба доказывают, что связь есть.
    /// То же самое делает пассивная сторона в <c>/repl/end</c> (T-153); теперь обе стороны
    /// пары ведут себя одинаково, и текст «Нет связи с сервером …» переживает ровно до
    /// первой удачной репликации, а не до конца времён.
    /// </summary>
    /// <remarks>Запись без надобности не трогается: одинаковое значение
    /// <see cref="AI2P.Storage.Services.ServerService.SetLastError"/> не перезаписывает.</remarks>
    private void ClearServerError(ServerNode target) => _registry.Servers.SetLastError(target.Id, "");

    /// <summary>
    /// СЕАНСЫ ЭТОЙ ПАРЫ ВЕДЁТ ПАРТНЁР (T-284): наш звонок не удался, но пара всё-таки
    /// отреплицировалась ПОСЛЕ нашего прошлого звонка, и удача эта не наша. Значит, звонил
    /// он — обмен идёт, и показывать человеку «Нет связи с сервером …» не за что.
    ///
    /// Правило намеренно требует ОБОИХ условий. Только «пара реплицировалась» мало: удачей
    /// мог быть наш собственный прошлый сеанс, и тогда сегодняшний отказ — настоящая новость.
    /// Только «удача не наша» тоже мало: без сравнения со временем нашего прошлого звонка
    /// давняя чужая отметка молчала бы про свежую поломку вечно.
    /// </summary>
    /// <param name="pairOkAt">Время прошлого удачного сеанса пары — чьего угодно.</param>
    /// <param name="ourPreviousCallAt">Когда мы звонили партнёру в прошлый раз; null — этот
    /// звонок первый с запуска приложения, и о паре мы пока ничего не знаем.</param>
    /// <param name="ourLastOkAt">Когда наш звонок в последний раз удался.</param>
    public static bool PartnerDrives(DateTime? pairOkAt, DateTime? ourPreviousCallAt,
        DateTime? ourLastOkAt) =>
        pairOkAt is { } pair
        && ourPreviousCallAt is { } previous && pair > previous
        && (ourLastOkAt is not { } ours || ours < pair);

    /// <param name="reached">Сеанс ДОШЁЛ ДО КОНЦА, хоть и не всё перенеслось (T-50-S0):
    /// база обменялась, партнёру сказано «сеанс окончен». Такой сеанс считается удачным
    /// по времени (<c>LastOkAt</c>) — иначе один непереносимый файл выглядел бы как
    /// «репликации не было НИКОГДА», — но красную строку с именами файлов человек видит.</param>
    private void Finish(ReplicationState state, OrgServer peer, bool ok, string error,
        bool reached = false)
    {
        state.Status = ok ? ReplicationStatuses.Idle : ReplicationStatuses.Error;
        state.LastError = error;
        if (ok || reached)
        {
            state.LastOkAt = DateTime.UtcNow;
        }
        Schedule(state, peer, ok);
    }

    /// <summary>Сводка о файлах, которые в этом сеансе перенести не удалось (T-50-S0).
    /// Имён показываем пять: список идёт в одну строку экрана диагностики, а полный
    /// перечень с причинами лежит в журнале.</summary>
    private static string FilesFailedText(List<string> failed)
    {
        const int show = 5;
        var names = string.Join(", ", failed.Take(show));
        return failed.Count <= show
            ? Loc.T("msg.replication.4", failed.Count, names)
            : Loc.T("msg.replication.5", failed.Count, names, failed.Count - show);
    }

    /// <summary>Сеанс не состоялся (его ведёт партнёр) — просто заводим отсчёт заново.</summary>
    private void Reschedule(ReplicationState state, OrgServer peer)
    {
        state.Status = ReplicationStatuses.Idle;
        Schedule(state, peer, ok: true);
    }

    /// <summary>
    /// Отсчёт до следующей репликации начинается ПОСЛЕ окончания текущей (todo43): во время
    /// сеанса он остановлен. После ошибки берётся интервал повтора, а если он не задан —
    /// обычный. Интервала нет вовсе — репликация только ручная, срока следующей нет.
    /// </summary>
    private void Schedule(ReplicationState state, OrgServer peer, bool ok)
    {
        var (interval, retry) = Intervals(peer);
        var seconds = ok ? interval : retry ?? interval;
        state.NextRunAt = seconds is { } value ? DateTime.UtcNow.AddSeconds(value) : null;
        _registry.ReplState.Save(state);
    }

    // --- видимость и защита строк серверной БД ---

    /// <summary>
    /// Что из СЕРВЕРНОЙ БД партнёр вправе увидеть в рамках этой организации (ТЗ гл. 12):
    /// сама организация, её серверы (без токенов и настроек — их вырезает
    /// <see cref="ChangeLog"/>), связи и аккаунты её участников. Всё остальное — чужое:
    /// другие организации этой установки партнёра не касаются.
    ///
    /// РОДИТЕЛЬ ЕДЕТ ВМЕСТЕ С РЕБЁНКОМ (T-20-S1). Серверы берутся по ВСЕМ связям организации
    /// (<see cref="ServerService.LinkedServerIds"/>), а не по действующим: связь отдаётся
    /// партнёру и после мягкого удаления — отвязку и отклонённую заявку он обязан увидеть, —
    /// а <c>org_servers.server_id</c> ссылается на <c>servers</c> внешним ключом. Пока список
    /// брался из <c>ServersOf</c>, строка связи отклонённого сервера уезжала без записи самого
    /// сервера и оседала у партнёра в очереди повтора навсегда: «org_servers …: FOREIGN KEY
    /// constraint failed (попыток 1597)».
    /// </summary>
    public List<RowChange> Visible(Microsoft.Data.Sqlite.SqliteConnection conn, List<RowChange> items,
        string orgId, OrgContext context)
    {
        var servers = _registry.Servers.LinkedServerIds(orgId).ToHashSet(StringComparer.Ordinal);
        var accounts = context.Executors.AccountIds().ToHashSet(StringComparer.Ordinal);
        var visible = new List<RowChange>();
        foreach (var item in items)
        {
            var ok = item.Table switch
            {
                "orgs" => item.Pk == orgId,
                "servers" => servers.Contains(item.Pk),
                "accounts" => accounts.Contains(item.Pk),
                "org_servers" => LinkOrg(conn, item) == orgId,
                _ => false,
            };
            if (ok)
            {
                visible.Add(item);
            }
        }
        return visible;
    }

    /// <summary>Применить пришедшую пачку изменений с проверкой прав на строки (ТЗ гл. 6, гл. 12).
    /// Ею пользуется и ведущий сеанса, и пассивная сторона (эндпойнт <c>/repl/apply</c>).</summary>
    public ApplyResult ApplyFrom(Database db, string scope, Organization org, ServerNode peer,
        IEnumerable<RowChange> items)
    {
        var context = _registry.Context(org);
        var state = _registry.ReplState.Get(org.Id, peer.Id);
        var result = ChangeLog.Apply(db, items, Guard(scope, org, context, peer, state.LastOkAt));
        // строки, принятые без родителя (T-169-S0), называются в журнале и здесь: отложенных
        // при этом может не быть вовсе, а Quarantine ниже зовётся только из-за них
        NoteAdopted(peer, org, result);
        // пассивная сторона откладывает отвергнутое так же, как ведущий (T-160): пачку
        // партнёра нельзя ни потерять целиком, ни вернуть ему ошибкой — он её больше
        // не пришлёт, курсор у него уже ушёл вперёд
        if (result.Failed.Count > 0)
        {
            // база АРХИВА в очередь повтора не кладётся (T-47-S0): очередь разбирается по
            // видам org и srv, а строка архива ждала бы там вечно. Архив перевозится целиком
            // с нуля, и отвергнутая строка приедет заново следующей пачкой того же журнала
            if (ReplScopes.ArchiveOf(scope).Length > 0)
            {
                Serilog.Log.Warning("Репликация архива {Scope} ← {Server}: строк не принято {Count} "
                                    + "({Reason})", scope, peer.Name, result.Failed.Count,
                    result.Failed[^1].Text);
                return result;
            }
            Quarantine(peer, org, state, scope, result);
            _registry.ReplState.Save(state);
        }
        return result;
    }

    /// <summary>
    /// Разобрать очередь повтора по обеим базам (T-160). Пассивная сторона сеанса вызывает
    /// это на его закрытии: ведущим она может не стать никогда (сервер за NAT сеансы
    /// начинает сам), а отложенные строки разбирать всё равно надо.
    /// </summary>
    public void RetryPending(Organization org, ServerNode peer)
    {
        var state = _registry.ReplState.Get(org.Id, peer.Id);
        if (state.Pending == 0)
        {
            return;
        }
        var context = _registry.Context(org);
        RetryPending(peer, org, context, state, ReplScopes.Org);
        RetryPending(peer, org, context, state, ReplScopes.Server);
    }

    /// <summary>
    /// Разобраться с приходящим ключом API (ТЗ гл. 10, todo45). Разные ключи — разные строки
    /// и сливаются сами; один и тот же ключ с ОДИНАКОВЫМ значением тоже сливается молча;
    /// остаётся случай «один ключ, разные значения на двух серверах» — он и есть конфликт,
    /// и решает его человек тремя кнопками (принять левый, принять правый, ввести новый).
    /// </summary>
    /// <returns>true — изменение можно применять обычным порядком.</returns>
    private bool AcceptKey(Organization org, OrgContext context, ServerNode peer, string pk,
        Dictionary<string, object?> values, DateTime? lastOkAt)
    {
        var scopeKey = AI2P.Storage.Services.FileSyncStateService.ScopeKey(KeyScope, org.Id);
        var mine = context.KeyStore.GetById(pk);
        var incoming = values.GetValueOrDefault("value_enc") as string ?? "";
        var secretRef = values.GetValueOrDefault("secret_ref") as string ?? mine?.SecretRef ?? pk;
        // «менялось ли у НАС с прошлого удачного сеанса» — критерий, одинаковый на обоих
        // концах пары. Курсор для этого не годится: партнёр забирает наш журнал ДО того, как
        // решит применять, и наша правка выглядела бы доставленной, даже когда он её отверг
        var changedHere = mine is not null && (lastOkAt is null || mine.UpdatedAt > lastOkAt.Value);
        if (!changedHere || mine is null)
        {
            // мы ключ не трогали — обычная репликация; заодно снимаем конфликт, если он был:
            // значение партнёра принято, спорить больше не о чем
            _registry.FileSync.Clear(org.Id, peer.Id, scopeKey, secretRef);
            return true;
        }
        // сравниваем ОТКРЫТЫЕ значения: шифртексты у одного и того же ключа разные (случайный
        // nonce), и сравнение blob'ов объявляло бы конфликтом любое повторное сохранение
        var orgKey = context.OrgKeys.Read(org.Id);
        var ours = orgKey is null ? null : OrgSecretKey.Decrypt(orgKey, mine.ValueEnc);
        var theirs = orgKey is null ? null : OrgSecretKey.Decrypt(orgKey, incoming);
        if (orgKey is not null && ours is not null && ours == theirs)
        {
            _registry.FileSync.Clear(org.Id, peer.Id, scopeKey, secretRef);
            return true;   // «меняли один и тот же ключ, но он одинаковый» — сливаем
        }
        var weAreConductor = context.IsConductor;
        var left = weAreConductor ? mine.ValueEnc : incoming;
        var right = weAreConductor ? incoming : mine.ValueEnc;
        var leftAt = weAreConductor ? Sql.ToDb(mine.UpdatedAt) : values.GetValueOrDefault("updated_at") as string ?? "";
        var rightAt = weAreConductor ? values.GetValueOrDefault("updated_at") as string ?? "" : Sql.ToDb(mine.UpdatedAt);
        _registry.FileSync.Record(org.Id, peer.Id, KeyScope, scopeKey, projectId: "", secretRef,
            left: new FileEntry { Path = secretRef, Mtime = leftAt, Hash = Fingerprint(left) },
            right: new FileEntry { Path = secretRef, Mtime = rightAt, Hash = Fingerprint(right) },
            leftValue: left, rightValue: right);
        return false;   // ключ не применяем, пока человек не решит (todo45)
    }

    /// <summary>Вид конфликта «ключ API организации» — он попадает в тот же список, что и файлы.</summary>
    public const string KeyScope = "key";

    /// <summary>Вид конфликта «запись справочника моделей» (T-227) — там же, рядом с ключами.</summary>
    public const string ModelScope = "model";

    /// <summary>
    /// Разобраться с приходящей записью справочника моделей (T-227). Правки человека — имя,
    /// активность, кастомные записи, удаление — реплицируются как обычно; остаётся случай
    /// «одну и ту же запись правили на ДВУХ серверах» — он и есть конфликт, и решает его
    /// человек двумя кнопками: принять левую (версию дирижёра) или правую. Переименований,
    /// как у файлов, тут быть не может — запись одна, а не две.
    ///
    /// Критерий «правили и у нас» тот же, что у ключей API: <c>updated_at</c> нашей строки
    /// новее прошлого удачного сеанса. Курсор для этого не годится — партнёр забирает наш
    /// журнал ДО того, как решит применять.
    /// </summary>
    /// <returns>true — изменение можно применять обычным порядком.</returns>
    private bool AcceptModel(Organization org, OrgContext context, ServerNode peer, string pk,
        Dictionary<string, object?> values, DateTime? lastOkAt)
    {
        var scopeKey = AI2P.Storage.Services.FileSyncStateService.ScopeKey(ModelScope, org.Id);
        var verdict = JudgeModel(context.Models.Get(pk), lastOkAt, values);
        if (verdict.Accept)
        {
            // запись мы не трогали (или правили одинаково) — обычная репликация; заодно
            // снимаем конфликт, если он был: спорить больше не о чем
            _registry.FileSync.Clear(org.Id, peer.Id, scopeKey, pk);
            return true;
        }
        // левая сторона пары — всегда дирижёр: список конфликтов выглядит одинаково,
        // на каком бы из двух серверов его ни открыли
        var weAreConductor = context.IsConductor;
        var left = weAreConductor ? verdict.Ours : verdict.Theirs;
        var right = weAreConductor ? verdict.Theirs : verdict.Ours;
        var minePart = verdict.OursAt;
        var theirsPart = values.GetValueOrDefault("updated_at") as string ?? "";
        _registry.FileSync.Record(org.Id, peer.Id, ModelScope, scopeKey, projectId: "", pk,
            left: new FileEntry
            {
                Path = pk,
                Mtime = weAreConductor ? minePart : theirsPart,
                Hash = Fingerprint(left),
            },
            right: new FileEntry
            {
                Path = pk,
                Mtime = weAreConductor ? theirsPart : minePart,
                Hash = Fingerprint(right),
            },
            leftValue: left, rightValue: right);
        return false;   // запись не применяем, пока человек не решит
    }

    /// <summary>Приговор приходящей записи справочника (T-227): применять её обычным
    /// порядком или это конфликт, и что тогда показывать сторонами.</summary>
    /// <param name="Accept">Применять обычным порядком (не трогали у нас либо правили одинаково).</param>
    /// <param name="Ours">Спорная часть НАШЕЙ версии — JSON.</param>
    /// <param name="Theirs">Спорная часть версии партнёра — JSON.</param>
    /// <param name="OursAt">Время нашей правки для показа человеку.</param>
    public sealed record ModelVerdict(bool Accept, string Ours, string Theirs, string OursAt);

    /// <summary>
    /// РЕШЕНИЕ по приходящей записи справочника моделей (T-227) — отдельно от записи конфликта,
    /// чтобы правило проверялось само по себе.
    ///
    /// «Правили и у нас» — <c>updated_at</c> нашей строки новее прошлого удачного сеанса
    /// (<paramref name="lastOkAt"/>). Критерий одинаков на обоих концах пары; курсор для этого
    /// не годится — партнёр забирает наш журнал ДО того, как решит применять.
    /// </summary>
    public static ModelVerdict JudgeModel(AiModel? mine, DateTime? lastOkAt,
        Dictionary<string, object?> values)
    {
        var theirs = ModelValue(
            values.GetValueOrDefault("name") as string ?? "",
            values.GetValueOrDefault("is_active") as long? == 1,
            values.GetValueOrDefault("is_local") as long? == 1,
            values.GetValueOrDefault("deleted_at") is string { Length: > 0 });
        // ПЕРВЫЙ сеанс пары (удачных ещё не было) конфликтом не считается вовсе: соглашаться
        // раньше было не о чем, а справочник дистрибутива к этому моменту есть у обеих сторон
        // и различается ровно тем, что одна из них успела посчитать сама (погашенные модели
        // без ключа API). Объявлять это конфликтом значило бы встречать каждого нового
        // партнёра тремя десятками споров на ровном месте
        var changedHere = mine is not null && lastOkAt is not null && mine.UpdatedAt > lastOkAt.Value;
        if (!changedHere || mine is null)
        {
            return new ModelVerdict(true, "", theirs, "");
        }
        var ours = ModelValue(mine.Name, mine.IsActive, mine.IsLocal, mine.DeletedAt is not null);
        // «правили одну запись, но одинаково» — сливаем молча
        return new ModelVerdict(ours == theirs, ours, theirs, Sql.ToDb(mine.UpdatedAt));
    }

    /// <summary>
    /// Спорная часть записи справочника — ровно то, что правит ЧЕЛОВЕК: имя, активность,
    /// размещение и признак удаления. Владелец строки (<c>server_id</c>), порядковый номер и
    /// пути файлов сюда не входят: они не правки, а служебные поля, и объявлять из-за них
    /// конфликт было бы ложной тревогой.
    ///
    /// Хранится это как JSON в сторонах конфликта: им же и применяется решение человека
    /// (<c>ApiEndpoints.ResolveModelConflict</c>), поэтому «отпечатка» тут мало.
    /// </summary>
    public static string ModelValue(string name, bool isActive, bool isLocal, bool deleted) =>
        JsonSerializer.Serialize(new
        {
            name,
            isActive,
            isLocal,
            deleted,
        });

    /// <summary>Отпечаток зашифрованного значения: по нему видно, что версия сменилась,
    /// а само значение не раскрывается (в том числе в списке конфликтов).</summary>
    private static string Fingerprint(string encrypted) =>
        encrypted.Length == 0
            ? ""
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(encrypted))).ToLowerInvariant()[..16];

    /// <summary>Организация связи «организация ↔ сервер»: у удаления в payload только ключ,
    /// поэтому организацию доспрашиваем в таблице.</summary>
    private static string LinkOrg(Microsoft.Data.Sqlite.SqliteConnection conn, RowChange change)
    {
        var values = ChangeLog.Parse(change.PayloadJson);
        if (values.GetValueOrDefault("org_id") is string fromPayload)
        {
            return fromPayload;
        }
        return Sql.Scalar<string>(conn, null, "SELECT org_id FROM org_servers WHERE id=@id",
            ("@id", change.Pk)) ?? "";
    }

    /// <summary>
    /// Страж применения (ТЗ гл. 6, гл. 12). Правило владения действует и в репликации:
    /// <list type="bullet">
    /// <item>СВОЯ запись в списке серверов извне правится только в части интервалов
    /// репликации — адрес, порт и каталоги этого компьютера живут в config.json и менять
    /// их удалённо нельзя;</item>
    /// <item>строки серверной БД принимаются только по организации сеанса: чужие
    /// организации партнёра нас не касаются;</item>
    /// <item>признак «это я» и токены доступа не принимаются никогда.</item>
    /// </list>
    /// </summary>
    private Func<string, string, Dictionary<string, object?>, bool> Guard(string scope,
        Organization org, OrgContext context, ServerNode peer, DateTime? lastOkAt)
    {
        var orgId = org.Id;
        // БАЗА АРХИВА (T-47-S0) — страж пропускающий: ни ключей API, ни справочника моделей
        // в архиве нет, спорить не о чем, а данные архива изменению уже не подлежат
        if (ReplScopes.ArchiveOf(scope).Length > 0)
        {
            return (_, _, _) => true;
        }
        if (scope != ReplScopes.Server)
        {
            // ключи API организации (этап 45): одинаковый ключ с разными значениями на двух
            // серверах — конфликт, а не «выиграл последний»: молча потерять чужой ключ нельзя.
            // Справочник моделей (T-227) — по тому же правилу
            return (table, pk, values) => table switch
            {
                "model_keys" => AcceptKey(org, context, peer, pk, values, lastOkAt),
                "ai_models" => AcceptModel(org, context, peer, pk, values, lastOkAt),
                "archives" => KeepCurrentArchive(values),
                _ => true,
            };
        }
        var localId = _registry.Servers.LocalId();
        return (table, pk, values) =>
        {
            switch (table)
            {
                case "orgs":
                    return pk == orgId;
                case "accounts":
                    return true;
                case "org_servers":
                    KeepRequestOfLink(pk, values);
                    return values.GetValueOrDefault("org_id") is not string org || org == orgId;
                case "servers":
                    values.Remove("is_local");
                    values.Remove("token");
                    values.Remove("peer_token");
                    if (pk != localId)
                    {
                        KeepAddressOfLoopbackPeer(pk, values);
                        return true;
                    }
                    // своя запись: оставляем ключ и только интервалы репликации
                    foreach (var column in values.Keys.ToList())
                    {
                        if (column != "id" && !ChangeLog.ReplicationColumns.Contains(column))
                        {
                            values.Remove(column);
                        }
                    }
                    return values.Count > 1;
                default:
                    return false;
            }
        };
    }

    /// <summary>
    /// ПРИЗНАК «ТЕКУЩИЙ» НЕ ПРИЕЗЖАЕТ КОЛОНКОЙ (T-47-S0).
    ///
    /// Сам архив реплицируется, а вот МОМЕНТ, когда он становится текущим, определяется здесь,
    /// а не у создателя: по ТЗ прежний текущий архив обязан СНАЧАЛА дослать свои данные
    /// в последний раз, и только потом перестать быть текущим. Приезжай <c>is_current</c>
    /// обычной колонкой — он переключился бы прямо в разгар сеанса, вместе со строкой реестра,
    /// то есть ДО досыла, и последние данные прежнего архива не приехали бы никогда.
    ///
    /// Поэтому колонка вырезается из пришедшей строки (у новой строки останется умолчание 0),
    /// а переключение делает ПОРУЧЕНИЕ <c>archive_handovers</c> — в самом конце сеанса,
    /// когда оба архива уже обменялись данными (<see cref="ArchiveReplicationService"/>).
    /// </summary>
    public static bool KeepCurrentArchive(Dictionary<string, object?> values)
    {
        values.Remove("is_current");
        return true;
    }

    /// <summary>
    /// ЗАЯВКА НЕ ЗАТИРАЕТСЯ ПУСТОТОЙ (T-146).
    ///
    /// Строку связи «организация ↔ сервер» пишут ОБЕ стороны: дирижёр заводит её по заявке,
    /// а подключающийся сервер — у себя, когда узнаёт о подтверждении. Но кто просил
    /// подключить, с какой запиской и какой человек за этим стоял, знает только дирижёр:
    /// у второй стороны этих колонок нет, и она шлёт их пустыми. По правилу «выиграл
    /// последний» первая же репликация стирала их у дирижёра — из списка серверов пропадало
    /// «кто просил подключить», а вместе с ним и след заявителя.
    ///
    /// Правило простое: ПУСТОЕ НЕ ЗАТИРАЕТ НЕПУСТОЕ. Всё остальное в строке (статус, код,
    /// признак дирижёра) применяется как обычно.
    /// </summary>
    private void KeepRequestOfLink(string linkId, Dictionary<string, object?> values)
    {
        if (_registry.Servers.LinkById(linkId) is not { } ours)
        {
            return;   // связи у нас ещё нет — принимаем как прислали
        }
        Keep("requested_by", ours.RequestedBy);
        Keep("note", ours.Note);
        Keep("applicant_id", ours.ApplicantId);
        Keep("applicant_name", ours.ApplicantName);
        Keep("applicant_email", ours.ApplicantEmail);
        Keep("applicant_hash", ours.ApplicantHash);

        void Keep(string column, string mine)
        {
            if (values.ContainsKey(column)
                && (values[column] as string ?? "").Length == 0 && mine.Length > 0)
            {
                values[column] = mine;
            }
        }
    }

    /// <summary>
    /// ПЕТЛЕВОЙ АДРЕС ЧУЖОГО СЕРВЕРА НЕ ПРИНИМАЕТСЯ (T-141).
    ///
    /// Каждый сервер пишет свою запись сам, и она едет репликацией всем — вместе с адресом.
    /// Но у сервера за NAT постоянного адреса нет, и себя он называет «localhost»: приняв
    /// такой адрес, мы потеряли бы тот, по которому реально до него дозваниваемся (человек
    /// набрал его руками при подключении), и начали бы ходить к самим себе. Поэтому адрес
    /// в пришедшей строке заменяется НАШИМ — остальные колонки применяются как обычно.
    ///
    /// Записи, которой у нас ещё нет, это не касается: адрес пишется как прислали — знать
    /// о таком сервере что-то лучшее нам неоткуда, а звонить ему мы всё равно не станем.
    /// </summary>
    private void KeepAddressOfLoopbackPeer(string serverId, Dictionary<string, object?> values)
    {
        if (values.GetValueOrDefault("hostname") is not string incoming
            || !ServerAddress.IsLoopback(incoming))
        {
            return;
        }
        if (_registry.Servers.Get(serverId) is not { } ours || ServerAddress.IsLoopback(ours.Hostname))
        {
            return;   // своего адреса для него у нас тоже нет — принимаем как есть
        }
        values["hostname"] = ours.Hostname;
        values["protocol"] = ours.Protocol;
        values["port"] = (long)ours.Port;
        values["base_path"] = ours.BasePath;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Что делает сеанс прямо сейчас — попадает в подсказку колонки «статус репликации».</summary>
public static class ReplPhases
{
    public const string Pull = "pull";
    public const string PullServer = "pull_srv";
    public const string Push = "push";
    public const string PushServer = "push_srv";

    /// <summary>Файлы каталога данных организации (этап 44).</summary>
    public const string FilesOrg = "files_org";

    /// <summary>Файлы папки <c>Common</c> проектов (этап 44).</summary>
    public const string FilesCommon = "files_common";

    /// <summary>База ТЕКУЩЕГО архива (T-47-S0).</summary>
    public const string Archive = "arc";

    /// <summary>Файлы проектов текущего архива (T-47-S0).</summary>
    public const string ArchiveFiles = "arc_files";
}

/// <summary>Ход сеанса репликации: прогресс-бар и процент в списке серверов (todo43).</summary>
public sealed class ReplicationProgress
{
    /// <summary>
    /// СКОЛЬКО СЕАНС МОЖЕТ МОЛЧАТЬ (T-174). Замок пары держится в памяти, и снять его
    /// обязано окончание сеанса; если оно не пришло, пара не должна оставаться занятой
    /// навсегда. Предел взят с запасом к таймауту запроса к партнёру (5 минут,
    /// <see cref="Api.ClusterClient"/>): дольше него без единого движения не бывает даже
    /// самый медленный шаг сеанса, а значит, молчание сверх этого — оборванный сеанс.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Кто начал сеанс: «таймер», почта пользователя или имя партнёра.</summary>
    public string StartedBy { get; set; } = "";

    /// <summary>Когда сеанс начался.</summary>
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    /// <summary>Когда по паре последний раз было движение (T-174).</summary>
    public DateTime TouchedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Оценка общего числа изменений в сеансе (не меньше 1: делим на него).</summary>
    public long Total
    {
        get => _total;
        set { _total = value; Touch(); }
    }

    /// <summary>Сколько изменений уже прошло.</summary>
    public long Done
    {
        get => _done;
        set { _done = value; Touch(); }
    }

    public string Phase
    {
        get => _phase;
        set { _phase = value; Touch(); }
    }

    public int Percent => Total <= 0 ? 0 : (int)Math.Clamp(Done * 100 / Total, 0, 100);

    /// <summary>Отметить движение по сеансу: срок протухания замка отсчитывается отсюда.</summary>
    /// <param name="at">Когда было движение; null — сейчас (обычный вызов). Явное время
    /// нужно проверкам: ждать десять минут ради «замок протух» они не могут.</param>
    public void Touch(DateTime? at = null) => TouchedAt = at ?? DateTime.UtcNow;

    /// <summary>Сеанс молчит дольше <see cref="StaleAfter"/> — считаем его оборванным (T-174).</summary>
    public bool IsStale(DateTime now) => now - TouchedAt > StaleAfter;

    /// <summary>Чем занят сеанс — одной строкой для человека (ручной пуск, журнал).</summary>
    public string Describe() => Loc.T("msg.replication.3", (StartedBy.Length > 0 ? StartedBy : "?"), Phase, Percent, StartedAt.ToLocalTime());

    private long _total = 1;
    private long _done;
    private string _phase = ReplPhases.Pull;
}
