using AI2P.Core;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage.Services;

namespace AI2P.Server.Api;

/// <summary>
/// ПОДКЛЮЧЕНИЕ ЭТОГО СЕРВЕРА К ЧУЖОЙ ОРГАНИЗАЦИИ (ТЗ гл. 11, п. 11.2; гл. 6, этап 43) —
/// сторона ПОДАЮЩЕГО заявку. Приёмная сторона живёт в <see cref="ClusterEndpoints"/>.
///
/// Вынесено из <see cref="ApiEndpoints"/> в T-135: тот же обмен нужен и странице
/// <c>{basePath}/join-cluster</c> — на сервере, где организаций ещё нет, вкладка «Серверы»
/// недоступна (весь UI живёт внутри организации), а подключиться к кластеру надо именно там.
/// Дублировать четырёхшаговый обмен (заявка → решение → токены → заведение организации)
/// в двух местах нельзя: разойдутся.
/// </summary>
public static class ClusterJoinFlow
{
    /// <summary>
    /// Подать заявку на подключение к организации чужого дирижёра (todo41 п. 7).
    ///
    /// Заявка АНОНИМНА (T-139, доработка): реквизитов на дирижёре у нас нет и быть не должно —
    /// требовать их значило бы просить человека передать свой пароль чужому серверу. Личность
    /// заявителя заявка называет сама (<paramref name="applicant"/>), а проверяет её человек
    /// на дирижёре, подтверждая заявку руками.
    /// </summary>
    /// <param name="conductorPassword">Пароль заявителя НА ДИРИЖЁРЕ (T-150): непустой только
    /// тогда, когда дирижёр ответил «человек с этой почтой у меня уже есть» и попросил
    /// доказать, что это он. В обычной заявке пусто — она по-прежнему анонимна.</param>
    public static async Task<ClusterJoinStatusDto> RequestAsync(OrgRegistry registry, ServerNode target,
        string orgCode, JoinApplicant applicant, string? actorId, string note = "",
        bool append = false, string conductorPassword = "", CancellationToken ct = default)
    {
        var local = registry.Servers.Local()
                    ?? throw new InvalidOperationException(Loc.T("msg.clusterJoinFlow.1"));
        // ПЕТЛЕВОЕ ИМЯ СВОЕГО СЕРВЕРА БОЛЬШЕ НЕ ПРЕПЯТСТВИЕ (T-141): постоянного адреса
        // у сервера за NAT нет, и это нормальное положение дел — он просто работает
        // «почти клиентом»: сеанс репликации всегда начинает сам (см. ServerAddress)
        if (applicant.IsEmpty)
        {
            throw new ArgumentException(
                Loc.T("msg.clusterJoinFlow.2"));
        }
        // ХЭШ ПАРОЛЯ ЗАЯВИТЕЛЯ едет с заявкой (T-139): по нему дирижёр заведёт того же
        // человека с ТЕМ ЖЕ паролем. Ждать, пока аккаунт приедет репликацией, нельзя:
        // его строку журнала партнёр пропустит как невидимую (человек ещё не участник
        // организации) и второй раз к ней не вернётся — курсор уже ушёл вперёд
        applicant = applicant with { PasswordHash = registry.Accounts.PasswordHashOf(applicant.Id) };
        using var client = new ClusterClient();
        var status = await client.JoinAsync(target, new ClusterJoinDto
        {
            ServerId = local.Id,
            Name = local.Name,
            Protocol = local.Protocol,
            Hostname = local.Hostname,
            Port = local.Port,
            BasePath = local.BasePath,
            OrgCode = orgCode,
            RequestedBy = applicant.Display(),
            ApplicantId = applicant.Id,
            ApplicantName = applicant.Name,
            ApplicantEmail = applicant.Email,
            ApplicantPasswordHash = applicant.PasswordHash,
            // пароль на дирижёре — только по его же просьбе (T-150), см. параметр
            ApplicantPassword = conductorPassword,
            Note = note,
        }, ct);
        // дирижёр представился своим внутренним ключом — запоминаем именно его
        var realId = registry.Servers.AdoptId(target.Id, status.ServerId);
        registry.Servers.SetLastError(realId, "");
        // «ВЫБЕРИТЕ ОРГАНИЗАЦИЮ» (T-141): код не назван, а организаций у дирижёра несколько —
        // заявки НЕТ, есть только перечень. Запоминать нечего: человек отметит нужные
        // (выбор множественный) и заявка уйдёт на каждую отдельно
        if (status.Status == ClusterJoinStatusDto.ChooseOrg)
        {
            return status;
        }
        // «ЧЕЛОВЕК С ЭТОЙ ПОЧТОЙ У МЕНЯ УЖЕ ЕСТЬ» (T-148): заявки тоже НЕТ — запоминать
        // нечего. Подавший берёт себе названный ключ и подаёт заявку заново
        if (status.Status == ClusterJoinStatusDto.SameEmail)
        {
            return status;
        }
        // код организации мог не называться вовсе (единственная организация дирижёра) —
        // тогда запоминаем тот, который назвал в ответе он сам
        var code = status.OrgCode.Length > 0 ? status.OrgCode : orgCode;
        if (append)
        {
            registry.Servers.AddJoin(realId, status.Status, code, status.RequestId);
        }
        else
        {
            registry.Servers.SetJoin(realId, status.Status, code, status.RequestId);
        }
        return status;
    }

    /// <summary>
    /// Заявки СРАЗУ В НЕСКОЛЬКО ОРГАНИЗАЦИЙ одного дирижёра (T-141): выбор в форме
    /// подключения множественный. Пустой список — код не назван вовсе: тогда уходит одна
    /// заявка, и дирижёр либо возьмёт свою единственную организацию, либо ответит перечнем
    /// (<see cref="ClusterJoinStatusDto.ChooseOrg"/>).
    /// </summary>
    /// <returns>Состояние ПОСЛЕДНЕЙ заявки; ответ «выберите организацию» возвращается сразу.</returns>
    /// <inheritdoc cref="RequestAsync"/>
    public static async Task<ClusterJoinStatusDto> RequestManyAsync(OrgRegistry registry,
        ServerNode target, IEnumerable<string> orgCodes, JoinApplicant applicant, string? actorId,
        string note = "", string conductorPassword = "", CancellationToken ct = default)
    {
        var codes = orgCodes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0)
        {
            return await RequestAsync(registry, target, "", applicant, actorId, note,
                conductorPassword: conductorPassword, ct: ct);
        }
        ClusterJoinStatusDto? last = null;
        for (var i = 0; i < codes.Count; i++)
        {
            // первая заявка заменяет прежний список, остальные добавляются к нему: иначе
            // подача во вторую организацию стёрла бы ссылку на решение по первой
            last = await RequestAsync(registry, target, codes[i], applicant, actorId, note,
                append: i > 0, conductorPassword: conductorPassword, ct: ct);
            // «человек с этой почтой уже есть» (T-148) — ответ про заявителя, а не про
            // организацию: в остальные заявка уйдёт с тем же ответом, повторять незачем
            if (last.Status == ClusterJoinStatusDto.SameEmail)
            {
                return last;
            }
            // запись сервера могла переехать на настоящий ключ дирижёра (AdoptId)
            if (last.ServerId.Length > 0 && registry.Servers.Get(last.ServerId) is { } adopted)
            {
                target = adopted;
            }
        }
        return last!;
    }

    /// <summary>
    /// Узнать решение по своей заявке; решение принимает человек на дирижёре. Заявка
    /// подтверждена — здесь же завершается подключение: встречные токены, ключ организации
    /// и заведение организации с идентификаторами дирижёра (дальше её наполнит репликация).
    /// </summary>
    public static async Task<ClusterJoinStatusDto> PollAsync(OrgRegistry registry, SecretStore secrets,
        ServerNode target, string? actorId, CancellationToken ct = default)
    {
        if (target.JoinStatus.Length == 0)
        {
            return new ClusterJoinStatusDto { Status = "" };
        }
        // заявок к одному дирижёру бывает несколько — по одной на организацию (T-141):
        // спрашиваем решение по каждой, а показываем последнее интересное
        var references = AI2P.Storage.Services.ServerService.Split(target.JoinRef);
        ClusterJoinStatusDto? result = null;
        var pending = new List<(string Code, string Ref)>();
        using var client = new ClusterClient();
        foreach (var reference in references.Count > 0 ? references : [target.JoinRef])
        {
            var one = await client.JoinStatusAsync(target, reference, ct);
            result = one.Token.Length > 0 || result is null ? one : result;
            if (one.Token.Length == 0)
            {
                // решения ещё нет (или отказ) — заявку помним, пока она жива
                if (one.Status != OrgServerStatus.Rejected)
                {
                    pending.Add((one.OrgCode, one.RequestId.Length > 0 ? one.RequestId : reference));
                }
                continue;
            }
            await AcceptedAsync(registry, secrets, client, target, one, actorId, ct);
        }
        registry.Servers.SetJoin(target.Id, result!.Status,
            string.Join(",", pending.Select(p => p.Code)),
            string.Join(",", pending.Select(p => p.Ref)));
        return result;
    }

    /// <summary>Заявку подтвердили: встречные токены, ключ организации и заведение
    /// организации у себя — дальше её наполнит репликация.</summary>
    private static async Task AcceptedAsync(OrgRegistry registry, SecretStore secrets,
        ClusterClient client, ServerNode target, ClusterJoinStatusDto status, string? actorId,
        CancellationToken ct)
    {
        // заявку подтвердили: токеном мы теперь ходим к дирижёру
        registry.Servers.SetPeerToken(target.Id, status.Token);
        registry.Servers.Update(target.Id, new ServerSaveInput
        {
            Name = target.Name,
            Protocol = target.Protocol,
            Hostname = target.Hostname,
            Port = target.Port,
            // ВТОРОЙ (ВНЕШНИЙ) АДРЕС ПЕРЕПИСЫВАТЬ НЕЧЕМ (T-312): запись сохраняется целиком,
            // и не названное здесь поле стёрлось бы — а по нему идёт звонок, когда внутренний
            // адрес не отвечает (T-50-S0)
            Hostname2 = target.Hostname2,
            Port2 = target.Port2,
            BasePath = target.BasePath,
            IsActive = true,
        }, actorId);
        // обмен обязан быть двусторонним и для репликации (ТЗ гл. 12, этап 43): выдаём
        // дирижёру встречный токен — иначе сеанс мог бы начинать только подключившийся
        // сервер, а по todo43 отсчёт идёт с обеих сторон
        var ours = registry.Servers.IssueToken(target.Id);
        await client.ReplPeerTokenAsync(target, status.Token, ours, ct);
        // КЛЮЧ ОРГАНИЗАЦИИ (ТЗ гл. 10, этап 45) — до заведения организации: без него её
        // ключи API приедут нерасшифруемыми. Он выдаётся один раз, вместе с подтверждением
        // заявки, и остаётся в secrets.json этого сервера
        new OrgSecretKey(secrets).Import(status.OrgId, status.OrgKey);
        // подключение к существующей организации (ТЗ гл. 11, п. 11.2): заводим её у себя
        // с идентификаторами дирижёра — дальше её наполнит репликация
        registry.AdoptOrg(status, target.Id, actorId);
        // и включаем автоматические сеансы со своей стороны (T-153): интервал принадлежит
        // записи рядового сервера, то есть НАШЕЙ, и до этого был пуст — «только по кнопке».
        // Сервер за NAT дирижёр не зовёт вовсе, сеанс начинаем мы
        if (registry.Servers.Local() is { } local)
        {
            registry.Servers.EnsureReplication(local.Id, actorId);
        }
    }

    /// <summary>
    /// ЗАБРАТЬ ОРГАНИЗАЦИИ, В КОТОРЫЕ НАС ВКЛЮЧИЛИ (T-312).
    ///
    /// Сервер попадает в организацию двумя путями: заявкой со своей стороны и галочкой
    /// в форме организации НА ДИРИЖЁРЕ (todo41 п. 11). Второй путь доводило до конца
    /// приглашение (T-170-S0), но уходит оно только тогда, когда дирижёр САМ звонит
    /// партнёру перед сеансом. До сервера, назвавшегося петлевым адресом, дирижёр не
    /// дозванивается вовсе (T-141), и получался тупик навсегда: дирижёр не звонит, потому
    /// что некуда, а партнёр не звонит, потому что об организации не знает. Человек при
    /// этом видит отмеченный сервер и ждёт репликации, которой не будет (жалоба T-312).
    ///
    /// Поэтому спрашиваем сами: у каждого сервера кластера, чей токен у нас есть, — «где
    /// я у тебя значусь». Незнакомую организацию заводим тем же способом, что и по
    /// подтверждённой заявке. Ошибки наружу не пускаются: сервер может быть выключен или
    /// быть прежней версии (404) — тогда просто идём к следующему.
    /// </summary>
    /// <returns>Сколько организаций завелось.</returns>
    public static async Task<int> PullInvitesAsync(OrgRegistry registry, SecretStore secrets,
        CancellationToken ct = default)
    {
        var local = registry.Servers.Local();
        if (local is null)
        {
            return 0;
        }
        var done = 0;
        using var client = new ClusterClient();
        foreach (var target in registry.Servers.List())
        {
            if (target.IsLocal || !target.IsActive || ct.IsCancellationRequested
                || registry.Servers.PeerToken(target.Id) is not { Length: > 0 } token)
            {
                continue;   // это мы сами либо пары токенов ещё нет — спрашивать нечем
            }
            try
            {
                var mine = await client.OrgsMineAsync(target, token, ct);
                foreach (var status in mine.Orgs)
                {
                    if (status.OrgId.Trim().Length == 0 || status.Token.Trim().Length == 0
                        || status.RequestId.Trim().Length == 0
                        || status.Status != OrgServerStatus.Active
                        || registry.Orgs.Get(status.OrgId) is not null)
                    {
                        continue;   // неполный ответ либо организация уже заведена — обычное дело
                    }
                    await AcceptedAsync(registry, secrets, client, target, status, actorId: null, ct);
                    done++;
                    Serilog.Log.Information("Кластер {Org}: сервер {Server} включил нас "
                        + "в организацию — она заведена, начинается репликация (T-312)",
                        status.OrgCode, target.Name);
                }
            }
            catch (OperationCanceledException)
            {
                return done;   // приложение останавливается
            }
            catch (Exception ex)
            {
                // выключен, прежней версии (404) или не узнал нас — не беда, спросим позже
                Serilog.Log.Debug(ex, "Кластер: у сервера {Server} не спросить, в какие "
                                      + "организации нас включили", target.Name);
            }
        }
        return done;
    }

    /// <summary>
    /// Адрес, который у каждого компьютера означает «я сам». Отказа по петлевому имени
    /// больше нет (T-141): сервер за NAT законно называется <c>localhost</c> и работает
    /// «почти клиентом» — сеанс репликации с ним начинает он сам. Правило и его следствия
    /// живут в <see cref="ServerAddress"/>, здесь — только привычное имя для вызовов.
    /// </summary>
    public static bool IsLoopback(string hostname) => ServerAddress.IsLoopback(hostname);

    /// <summary>
    /// Запись сервера по его адресу: уже заведён — она же, иначе новая. Нужна странице
    /// подключения (T-135): человек вводит адрес дирижёра, а не выбирает из списка, и
    /// повторный заход по тому же адресу не должен упираться в «сервер уже есть в списке».
    /// </summary>
    public static ServerNode EnsureByAddress(OrgRegistry registry, string protocol, string hostname,
        int port, string basePath, string? actorId)
    {
        var normalized = basePath.Trim().Trim('/');
        normalized = normalized.Length == 0 ? "" : "/" + normalized;
        var existing = registry.Servers.List().FirstOrDefault(s =>
            !s.IsLocal
            && string.Equals(s.Hostname, hostname.Trim(), StringComparison.OrdinalIgnoreCase)
            && s.Port == port
            && string.Equals(s.BasePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }
        return registry.Servers.Create(new ServerSaveInput
        {
            Name = hostname.Trim(),
            Protocol = protocol,
            Hostname = hostname.Trim(),
            Port = port,
            BasePath = normalized,
            IsActive = true,
        }, actorId);
    }

    /// <summary>
    /// Разобрать адрес сервера, введённый человеком: <c>http://хост:5480/ai2p</c>, можно без
    /// протокола и без префикса. Возвращает false с текстом ошибки — адрес неразборчив.
    /// </summary>
    public static bool TryParseAddress(string address, out string protocol, out string hostname,
        out int port, out string basePath, out string error)
    {
        protocol = "http";
        hostname = "";
        port = 5480;
        basePath = "/ai2p";
        error = "";
        var value = address.Trim();
        if (value.Length == 0)
        {
            error = Loc.T("msg.clusterJoinFlow.3");
            return false;
        }
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0)
        {
            error = Loc.T("msg.clusterJoinFlow.4", address);
            return false;
        }
        protocol = uri.Scheme;
        hostname = uri.Host;
        // порт не назван — берём умолчание AI2P (5480), а не 80/443 схемы: адрес обычно
        // набирают как «192.168.1.10:5480/ai2p», но и «сервер.local/ai2p» должен работать
        port = HasExplicitPort(value) ? uri.Port : 5480;
        var path = uri.AbsolutePath.Trim('/');
        basePath = path.Length == 0 ? "" : "/" + path;
        return true;
    }

    /// <summary>Порт назван в адресе явно; литерал IPv6 в скобках («[::1]») не в счёт.</summary>
    private static bool HasExplicitPort(string url)
    {
        var afterScheme = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var authority = afterScheme.Split('/', 2)[0];
        var afterLiteral = authority.LastIndexOf(']');
        return authority.IndexOf(':', afterLiteral + 1) >= 0;
    }
}
