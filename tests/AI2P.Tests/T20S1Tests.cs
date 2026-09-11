using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-20-S1: ДИАГНОСТИКА РЕПЛИКАЦИИ — «Ждут повтора строк 3».
///
/// Жалоба заказчика: на втором сервере в форме «Диагностика репликации» для дирижёра
/// ПОСТОЯННО висит
///
///     Ждут повтора строк   3
///     org_servers 20084b0f-…: FOREIGN KEY constraint failed (попыток 1597)
///
/// Разбор. Строка связи «организация ↔ сервер» ссылается на запись самого сервера внешним
/// ключом. Партнёру связь отдаётся по организации — ЛЮБАЯ, включая мягко удалённую (об
/// отвязке и об отклонённой заявке он обязан узнать), а запись сервера отбиралась по списку
/// ДЕЙСТВУЮЩИХ связей. Ребёнок уезжал без родителя: у партнёра строка не применялась никогда,
/// а очередь повтора пробовала её каждый сеанс — отсюда и полторы тысячи попыток.
///
/// Убрать это человеку было нечем: <see cref="ReplPendingService.Reset"/> не вызывался ни из
/// одной кнопки, и «первичная репликация» забывала курсоры, но не очередь.
/// </summary>
public sealed class T20S1Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t20s1-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private const string LocalId = "unid-S0";
    private const string PeerId = "unid-S1";
    private const string RejectedId = "unid-S2";

    private OrgRegistry Registry(string folder = "server", bool conductor = true)
    {
        var deps = new OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: new SecretStore(Path.Combine(_dir, "secrets.json")),
            LocalModels: new LocalModelProcessService(),
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => "",
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => conductor,
            LocalServerId: () => conductor ? LocalId : PeerId,
            LocalCode: _ => conductor ? "S0" : "S1",
            ServerCode: (_, _) => "S1",
            ServerName: _ => "сервер");
        return new OrgRegistry(Path.Combine(_dir, folder), "server.db", deps);
    }

    /// <summary>Кластер как у заказчика: дирижёр, подключённый сервер и третий, чью заявку
    /// отклонили (связь остаётся в таблице мягко удалённой). Возвращает организацию и связь
    /// отклонённого сервера — ту самую строку, что у заказчика висит в очереди повтора.</summary>
    private static (Organization Org, string RejectedLinkId) Cluster(OrgRegistry registry)
    {
        var org = registry.Orgs.Create("Проверка", "t20s1", actorId: null);
        registry.Servers.Create(Input("дирижёр", 5541), actorId: null, id: LocalId);
        registry.Servers.Create(Input("второй", 5542), actorId: null, id: PeerId);
        registry.Servers.Create(Input("третий", 5543), actorId: null, id: RejectedId);
        registry.Servers.Attach(org.Id, LocalId, OrgServerStatus.Active, "", null, code: "S0",
            isConductor: true);
        registry.Servers.Attach(org.Id, PeerId, OrgServerStatus.Active, "", null, code: "S1");
        var request = registry.Servers.Attach(org.Id, RejectedId, OrgServerStatus.Pending, "",
            null, code: "S2");
        registry.Servers.RejectRequest(request.Id, actorId: null);
        return (org, request.Id);
    }

    private static ServerSaveInput Input(string name, int port) =>
        new() { Name = name, Protocol = "http", Hostname = "localhost", Port = port, BasePath = "/ai2p" };

    // ---------- 1. родитель и ребёнок расходились ещё в списке серверов ----------

    [Fact]
    public void A_Rejected_Link_Disappears_From_The_Server_List_But_Its_Row_Still_Refers_To_It()
    {
        using var registry = Registry();
        var (org, _) = Cluster(registry);

        // в списке серверов организации отклонённого нет — так и задумано (todo41 п. 9)
        Assert.DoesNotContain(RejectedId, registry.Servers.ServersOf(org.Id).Select(l => l.ServerId));
        // а строка связи никуда не делась: она мягко удалена и по-прежнему ссылается на сервер
        Assert.Contains(RejectedId, registry.Servers.LinkedServerIds(org.Id));
        Assert.Equal(3, registry.Servers.LinkedServerIds(org.Id).Count);
    }

    // ---------- 2. вот как выглядит жалоба заказчика ----------

    [Fact]
    public void A_Link_Without_Its_Server_Row_Is_Refused_With_Foreign_Key()
    {
        using var registry = Registry();
        var (org, linkId) = Cluster(registry);

        // партнёр: та же организация и те же серверы, КРОМЕ отклонённого
        using var partnerRegistry = Partner(org);
        var result = ChangeLog.Apply(partnerRegistry.ServerDb, [Row(registry, "org_servers", linkId)]);

        var failure = Assert.Single(result.Failed);
        Assert.Equal("org_servers", failure.Change.Table);
        Assert.Contains("FOREIGN KEY", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public void The_Same_Link_Applies_As_Soon_As_Its_Server_Row_Arrives()
    {
        using var registry = Registry();
        var (org, linkId) = Cluster(registry);
        using var partnerRegistry = Partner(org);

        // порядок «родитель, потом ребёнок» — ровно то, чего не хватало
        var result = ChangeLog.Apply(partnerRegistry.ServerDb,
            [Row(registry, "servers", RejectedId), Row(registry, "org_servers", linkId)]);

        Assert.Empty(result.Failed);
        Assert.Equal(2, result.Applied);
        Assert.Contains(RejectedId, partnerRegistry.Servers.LinkedServerIds(org.Id));
    }

    // ---------- 3. что именно чинит правку: фильтр видимости ----------

    [Fact]
    public void Visible_Sends_The_Server_Row_Of_A_Rejected_Link_Too()
    {
        using var registry = Registry();
        var (org, _) = Cluster(registry);
        using var repl = new ReplicationService(registry);
        var context = registry.Context(org);

        List<RowChange> visible;
        using (var conn = registry.ServerDb.Open())
        {
            var all = ChangeLog.Read(conn, 0, 500, PeerId);
            visible = repl.Visible(conn, all, org.Id, context);
        }

        // связь отклонённого сервера партнёру отдаётся — иначе он не узнал бы об отказе…
        Assert.Contains(visible, r => r.Table == "org_servers"
                                      && r.PayloadJson.Contains(RejectedId, StringComparison.Ordinal));
        // …и вместе с ней обязана ехать запись самого сервера, иначе это FOREIGN KEY навсегда
        Assert.Contains(visible, r => r.Table == "servers" && r.Pk == RejectedId);
        // чужого при этом по-прежнему не видно: строки другой организации не отдаются
        Assert.DoesNotContain(visible, r => r.Table is not ("orgs" or "servers" or "org_servers"
                                                           or "accounts"));
    }

    [Fact]
    public void An_Unlinked_Server_Is_Sent_Together_With_Its_Link_As_Well()
    {
        using var registry = Registry();
        var (org, _) = Cluster(registry);
        // сервер отвязали (Detach) — связь тоже мягко удалена, и партнёру о ней сообщат
        registry.Servers.Detach(org.Id, PeerId, actorId: null);
        using var repl = new ReplicationService(registry);
        var context = registry.Context(org);

        List<RowChange> visible;
        using (var conn = registry.ServerDb.Open())
        {
            visible = repl.Visible(conn, ChangeLog.Read(conn, 0, 500, "unid-S9"), org.Id, context);
        }

        Assert.Contains(visible, r => r.Table == "servers" && r.Pk == PeerId);
    }

    // ---------- 4. очередь повтора: её видно построчно и её можно забыть ----------

    [Fact]
    public void The_Retry_Queue_Names_Table_Key_Reason_And_Tries()
    {
        using var registry = Registry();
        var (org, _) = Cluster(registry);
        var queue = registry.ReplPending;

        queue.Save(org.Id, PeerId, ReplScopes.Server, [Failure("org_servers", "link-1")]);
        queue.Save(org.Id, PeerId, ReplScopes.Server, [Failure("org_servers", "link-1")]);

        var rows = queue.List(org.Id, PeerId);
        var row = Assert.Single(rows);
        Assert.Equal(ReplScopes.Server, row.Scope);
        Assert.Equal("org_servers", row.Table);
        Assert.Equal("link-1", row.Pk);
        Assert.Contains("FOREIGN KEY", row.Reason, StringComparison.Ordinal);
        // повторный отказ не плодит строк, а копит попытки — ровно «попыток 1597» из жалобы
        Assert.Equal(2, row.Tries);
        Assert.Equal(1, queue.Count(org.Id, PeerId));
    }

    [Fact]
    public void Initial_Replication_Forgets_The_Retry_Queue()
    {
        using var registry = Registry();
        var (org, _) = Cluster(registry);
        var queue = registry.ReplPending;
        queue.Save(org.Id, PeerId, ReplScopes.Server, [Failure("org_servers", "link-1")]);
        queue.Save(org.Id, "unid-S9", ReplScopes.Server, [Failure("org_servers", "link-2")]);

        // «первичная репликация» забывает курсоры и очередь ЭТОЙ пары
        registry.ReplPending.Reset(org.Id, PeerId);

        Assert.Equal(0, queue.Count(org.Id, PeerId));
        Assert.Empty(queue.List(org.Id, PeerId));
        // и не трогает очередь другой пары
        Assert.Equal(1, queue.Count(org.Id, "unid-S9"));
    }

    // ---------- 5. «Последняя попытка» у пары, к которой звонить некуда ----------

    [Fact]
    public void A_Peer_With_A_Loopback_Address_Cannot_Be_Called_And_This_Is_Said_Aloud()
    {
        using var registry = Registry();
        Cluster(registry);
        using var repl = new ReplicationService(registry);

        var local = registry.Servers.Get(LocalId)!;
        var peer = registry.Servers.Get(PeerId)!;
        // сосед на том же компьютере, но на своём порту — к нему звонить можно (T-148)
        Assert.Null(ServerAddress.Unreachable(peer, local));
        // а вот петлевой адрес с НАШИМ портом означает «попадём к себе»: сеансы начинает он
        peer.Port = local.Port;
        var reason = ServerAddress.Unreachable(peer, local);
        Assert.False(string.IsNullOrEmpty(reason));
        // ту же причину экран диагностики берёт у службы репликации
        Assert.Equal(reason, repl.CannotCall(peer));
    }

    // ---------- вспомогательное ----------

    private static RowFailure Failure(string table, string pk) => new()
    {
        Change = new RowChange
        {
            Seq = 1,
            Table = table,
            Pk = pk,
            Op = ChangeOps.Upsert,
            Ts = Sql.ToDb(DateTime.UtcNow),
            NodeId = LocalId,
            PayloadJson = "{}",
        },
        Reason = "FOREIGN KEY constraint failed",
    };

    /// <summary>Последнее изменение строки из журнала серверной БД — как его отдают партнёру.</summary>
    private static RowChange Row(OrgRegistry registry, string table, string pk)
    {
        using var conn = registry.ServerDb.Open();
        return ChangeLog.Read(conn, 0, 500, "unid-S9")
            .Last(r => r.Table == table && r.Pk == pk);
    }

    /// <summary>Второй сервер: та же организация и запись дирижёра, но об отклонённом сервере
    /// он не знает ничего — как и было у заказчика.</summary>
    private OrgRegistry Partner(Organization org)
    {
        var partner = Registry("partner", conductor: false);
        partner.Orgs.Adopt(org.Id, org.DisplayId, org.Name, org.Code, actorId: null);
        partner.Servers.Create(Input("дирижёр", 5541), actorId: null, id: LocalId);
        partner.Servers.Create(Input("второй", 5542), actorId: null, id: PeerId);
        partner.Servers.Attach(org.Id, LocalId, OrgServerStatus.Active, "", null, code: "S0",
            isConductor: true);
        partner.Servers.Attach(org.Id, PeerId, OrgServerStatus.Active, "", null, code: "S1");
        return partner;
    }
}
