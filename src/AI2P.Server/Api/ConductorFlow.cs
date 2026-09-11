using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Connectors;
using AI2P.Server.Org;
using AI2P.Storage.Services;

namespace AI2P.Server.Api;

/// <summary>
/// СМЕНА ДИРИЖЁРА ОРГАНИЗАЦИИ (T-21-S1) — правила и действия, общие у HTTP API и у сторожа.
///
/// Дирижёр обязателен и всегда ровно один (ТЗ гл. 6). От него зависит выдача кодов серверам,
/// ведение справочников, расписания и вся топология репликации, поэтому передача дирижёрства
/// сделана ОТДЕЛЬНОЙ ДВУСТОРОННЕЙ операцией, устроенной как первое подключение сервера:
/// заявка → решение человека на другой стороне. Три вещи живут здесь:
///
/// <list type="number">
/// <item>КТО ВПРАВЕ ПОДАТЬ заявку (<see cref="ProblemWithRequest"/>): только назначаемый
/// сервер или сегодняшний дирижёр, и только если у назначаемого есть настоящий адрес —
/// сервер, называющий себя <c>localhost</c>, дирижёром быть не может, к нему никто
/// не дозвонится (ТЗ гл. 6, T-141).</item>
/// <item>ПРИМЕНЕНИЕ решения (<see cref="ApplyAsync"/>): признак дирижёра переставляется
/// в связях организации и уезжает всем обычной репликацией.</item>
/// <item>ОПОВЕЩЕНИЕ остальных серверов (<see cref="AnnounceAsync"/>): репликации для этого
/// мало — топология звезда, и с новым дирижёром рядовой сервер мог ни разу не разговаривать,
/// а при потере прежнего дирижёра посредника нет вовсе.</item>
/// </list>
/// </summary>
public static class ConductorFlow
{
    /// <summary>
    /// Почему заявку «назначить дирижёром» подать нельзя; null — можно.
    ///
    /// Правило «инициировать могут ОБЕ стороны, но не третья» проверяется здесь и только
    /// здесь: и кнопка в форме сервера, и HTTP-эндпойнт спрашивают один и тот же метод —
    /// иначе они разойдутся, и кнопка окажется живой там, где действие запрещено.
    /// </summary>
    /// <param name="target">Сервер, которого назначают дирижёром.</param>
    public static string? ProblemWithRequest(OrgRegistry registry, Organization org, ServerNode target) =>
        ConductorRules.ProblemWithRequest(target, registry.Servers.Link(org.Id, target.Id),
            registry.Servers.LocalId(), ConductorIdOf(registry, org));

    /// <summary>Сегодняшний дирижёр организации; пусто — его нет вовсе.</summary>
    public static string ConductorIdOf(OrgRegistry registry, Organization org) =>
        registry.Servers.Conductor(org.Id)?.ServerId ?? "";

    /// <summary>
    /// Наша ли это заявка (подали здесь) и вправе ли мы по ней решать. Решает ВТОРАЯ сторона:
    /// подал дирижёр — решает назначаемый, подал назначаемый — решает дирижёр.
    /// </summary>
    public static (bool Mine, bool CanDecide) SidesOf(ConductorRequest request, string localServerId)
    {
        var mine = request.InitiatorServerId == localServerId;
        var other = request.ByTarget ? request.FromServerId : request.TargetServerId;
        return (mine, !mine && other == localServerId);
    }

    /// <summary>Заявка для формы и для строки внизу списка серверов: сущность плюс то,
    /// что считается на месте, — стороны и обратный отсчёт.</summary>
    public static ConductorRequestDto ToDto(ConductorRequest request, string localServerId,
        DateTime now)
    {
        var (mine, canDecide) = SidesOf(request, localServerId);
        return new ConductorRequestDto
        {
            Id = request.Id,
            TargetServerId = request.TargetServerId,
            TargetServerCode = request.TargetServerCode,
            TargetServerName = request.TargetServerName,
            FromServerId = request.FromServerId,
            FromServerCode = request.FromServerCode,
            FromServerName = request.FromServerName,
            InitiatorServerId = request.InitiatorServerId,
            ByTarget = request.ByTarget,
            Status = request.Status,
            Note = request.Note,
            RequestedBy = request.RequestedBy,
            DecidedBy = request.DecidedBy,
            CreatedAt = request.CreatedAt,
            DecidedAt = request.DecidedAt,
            Unilateral = request.Unilateral,
            Mine = mine,
            CanDecide = canDecide,
            CanTakeOver = ConductorRequestService.CanTakeUnilaterally(request, localServerId, now),
            CountdownSec = (long)ConductorRequestService.Countdown(request, now).TotalSeconds,
        };
    }

    /// <summary>
    /// ПРИМЕНИТЬ СМЕНУ ДИРИЖЁРА: признак переставляется в связях организации (он же уедет
    /// всем обычной репликацией), а затем новый дирижёр объявляет о себе остальным серверам
    /// напрямую — иначе те, кто с ним не знаком, продолжали бы звонить прежнему.
    /// </summary>
    /// <param name="newConductorId">Внутренний ключ сервера, ставшего дирижёром.</param>
    public static async Task ApplyAsync(OrgRegistry registry, SecretStore secrets, Organization org,
        string newConductorId, string requestId, string? actorId, CancellationToken ct = default)
    {
        registry.Servers.SetConductor(org.Id, newConductorId, actorId);
        // маршрутизация и производные признаки (кто дирижёр) держатся в памяти
        registry.RefreshCodes();
        if (registry.Servers.LocalId() != newConductorId)
        {
            return;   // дирижёром стали не мы — объявлять о себе будет он сам
        }
        await AnnounceAsync(registry, secrets, org, requestId, ct);
    }

    /// <summary>
    /// ОБЪЯВИТЬ О СМЕНЕ ДИРИЖЁРА остальным серверам организации (T-21-S1).
    ///
    /// Зовётся новым дирижёром: сразу после принятия заявки и потом сторожем репликации,
    /// пока остаются серверы, с которыми пары ещё нет. Объявление подписано ключом
    /// организации (<see cref="ConductorProof"/>) — токена у этой пары может не быть вовсе,
    /// а ключ организации есть у каждого её сервера и не реплицируется никогда.
    ///
    /// Тем же обменом устанавливается пара токенов: мы выдаём получателю свой, он в ответе
    /// присылает встречный. Без этого новый дирижёр не смог бы ни позвать сервер, ни быть
    /// позванным им (ТЗ гл. 12).
    /// </summary>
    /// <returns>Скольким серверам объявление доставлено.</returns>
    public static async Task<int> AnnounceAsync(OrgRegistry registry, SecretStore secrets,
        Organization org, string requestId, CancellationToken ct = default)
    {
        var local = registry.Servers.Local();
        if (local is null || !registry.Servers.IsLocalConductor(org.Id))
        {
            return 0;   // объявлять о смене дирижёра вправе только сам новый дирижёр
        }
        var orgKey = new OrgSecretKey(secrets).Read(org.Id);
        if (orgKey is null)
        {
            Serilog.Log.Warning("Смена дирижёра {Org}: ключа организации на этом сервере нет — "
                                + "объявить о себе остальным нечем (T-21-S1)", org.Code);
            return 0;
        }
        var done = 0;
        using var client = new ClusterClient();
        foreach (var link in registry.Servers.ServersOf(org.Id, includeRequests: false))
        {
            if (link.IsConductor || link.Status != OrgServerStatus.Active || !link.IsActive
                || link.ServerId == local.Id || ct.IsCancellationRequested)
            {
                continue;
            }
            var node = registry.Servers.Get(link.ServerId);
            // до сервера за NAT не дозвониться — он позовёт сам, и о смене дирижёра узнает
            // из обычной репликации связей организации (T-141)
            if (node is null || ServerAddress.Unreachable(node, local) is not null)
            {
                continue;
            }
            var token = registry.Servers.IssueToken(node.Id);
            var stamp = DateTime.UtcNow.ToString("o");
            var dto = new ClusterConductorDto
            {
                OrgCode = org.Code,
                ServerId = local.Id,
                RequestId = requestId,
                Token = token,
                Timestamp = stamp,
                Signature = ConductorProof.Sign(orgKey,
                    ConductorProof.Payload(org.Id, local.Id, requestId, token, stamp)),
            };
            try
            {
                var ack = await client.ConductorAnnounceAsync(node, dto, ct);
                if (ack.Token.Length > 0)
                {
                    registry.Servers.SetPeerToken(node.Id, ack.Token);
                }
                registry.Servers.SetLastError(node.Id, "");
                done++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                           or TaskCanceledException)
            {
                // сервер выключен — не беда: объявление повторит сторож репликации, а сам
                // признак дирижёра доедет до него и обычным журналом изменений
                Serilog.Log.Debug(ex, "Смена дирижёра {Org}: серверу {Server} объявить не удалось",
                    org.Code, node.Name);
            }
        }
        return done;
    }

    /// <summary>
    /// Есть ли кому объявлять (проверка для сторожа, чтобы не звонить каждую минуту зря):
    /// мы дирижёр, и у организации есть активный сервер, с которым пары токенов ещё нет.
    /// </summary>
    public static bool NeedsAnnounce(OrgRegistry registry, Organization org)
    {
        var local = registry.Servers.Local();
        if (local is null || !registry.Servers.IsLocalConductor(org.Id))
        {
            return false;
        }
        return registry.Servers.ServersOf(org.Id, includeRequests: false).Any(link =>
            !link.IsConductor && link.Status == OrgServerStatus.Active && link.IsActive
            && link.ServerId != local.Id
            && registry.Servers.PeerToken(link.ServerId).Length == 0
            && registry.Servers.Get(link.ServerId) is { } node
            && ServerAddress.Unreachable(node, local) is null);
    }

    /// <summary>
    /// ПРИНЯТЬ ОБЪЯВЛЕНИЕ о новом дирижёре (сторона получателя, эндпойнт кластера).
    /// Подпись уже проверена вызывающим: здесь — только запись у себя.
    /// </summary>
    /// <returns>Токен, которым новый дирижёр будет ходить к нам.</returns>
    public static string AcceptAnnounce(OrgRegistry registry, Organization org, string newConductorId,
        string peerToken, string requestId)
    {
        registry.Servers.SetPeerToken(newConductorId, peerToken);
        // объявивший обязан быть АКТИВНОЙ записью: неактивный сервер не опрашивается и
        // в репликации не участвует (ТЗ гл. 6), а звонить нам он теперь будет постоянно
        if (registry.Servers.Get(newConductorId) is { IsActive: false, IsLocal: false } node)
        {
            registry.Servers.Update(node.Id, new ServerSaveInput
            {
                Name = node.Name,
                Protocol = node.Protocol,
                Hostname = node.Hostname,
                Port = node.Port,
                BasePath = node.BasePath,
                IsActive = true,
            }, actorId: null);
        }
        registry.Servers.SetConductor(org.Id, newConductorId, actorId: null);
        registry.RefreshCodes();
        // заявка, если она у нас есть, закрывается тем же решением: человек должен видеть,
        // чем кончилось, а не «ждёт решения» у уже состоявшейся смены
        if (requestId.Length > 0 && registry.ById(org.Id) is { } context
            && context.ConductorRequests.Get(requestId) is { } request
            && ConductorRequestStatus.IsOpen(request.Status))
        {
            context.ConductorRequests.Decide(request.Id, ConductorRequestStatus.Accepted,
                decidedBy: "", actorId: null, unilateral: request.ByTarget);
        }
        registry.LogCluster(EventTypes.ConductorAnnounced, actorId: null, newConductorId,
            new { org = org.Code, requestId });
        return registry.Servers.IssueToken(newConductorId);
    }
}
