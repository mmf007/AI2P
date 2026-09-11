using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-293 «Настройки → Серверы: отбор заявок».
///
/// Жалоба: решение по заявке на смену дирижёра принято, а строка осталась висеть в списке
/// серверов — до уборки, то есть неделю. Со стороны это выглядит как незаконченное дело,
/// хотя делать по ней уже нечего.
///
/// Правило теперь такое: в списке видны СЕРВЕРЫ и заявки, ЖДУЩИЕ РЕШЕНИЯ, а по флажку
/// «показать все заявки» — вся история обоих видов сразу: решённые и снятые заявки на смену
/// дирижёра плюс отклонённые заявки на подключение серверов к кластеру (их «ОТКЛОНИТЬ»
/// мягко удаляет, и посмотреть, что было, до сих пор было негде).
///
/// Принятая заявка на подключение в список заявок не попадает ни при каком отборе: она и есть
/// строка сервера в том же списке, и второй строкой была бы просто двойником.
/// </summary>
public sealed class T293Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // --- заявки на смену дирижёра (таблица БД организации) ---

    private const string ConductorId = "unid-S0";
    private const string TargetId = "unid-S1";

    private ConductorRequestService Conductor(string me) => new(_f.Db, _f.Events,
        new ServerScope(() => me, () => me == ConductorId ? "S0" : "S1",
            () => me == ConductorId,
            codeOf: id => id == ConductorId ? "S0" : "S1",
            nameOf: id => id == ConductorId ? "главный" : "ноутбук"));

    // --- заявки на подключение сервера (таблица БД установки) ---

    private ServerNode Local() =>
        _f.Servers.Local() ?? _f.Servers.EnsureLocal("local", "http", "localhost", 5480, "/ai2p");

    private ServerNode Remote(string host) =>
        _f.Servers.Create(new ServerSaveInput
        {
            Name = host,
            Protocol = "http",
            Hostname = host,
            Port = 5480,
            BasePath = "/ai2p",
        }, null);

    [Fact]
    public void A_Decided_Conductor_Request_Leaves_The_Default_List()
    {
        var request = Conductor(TargetId).Add(TargetId, ConductorId, "", "миша", null);

        // пока ждёт решения — видна всегда, при любом отборе
        Assert.Single(Conductor(ConductorId).List());
        Assert.Single(Conductor(ConductorId).List(all: true));

        Conductor(ConductorId).Decide(request.Id, ConductorRequestStatus.Rejected, "миша", null);

        // решение принято — из обычного списка строка уходит сразу, а не через неделю
        Assert.Empty(Conductor(ConductorId).List());
        // но история никуда не делась: она видна по флажку «показать все заявки»
        var all = Conductor(ConductorId).List(all: true);
        Assert.Single(all);
        Assert.Equal(ConductorRequestStatus.Rejected, all[0].Status);
    }

    [Fact]
    public void A_Withdrawn_Conductor_Request_Is_Visible_Only_With_The_Box()
    {
        // снятую подавшим заявку не показывал никто и никогда: Decide мягко удаляет её,
        // а прежний список отбирал по deleted_at IS NULL
        var request = Conductor(TargetId).Add(TargetId, ConductorId, "", "миша", null);
        Conductor(TargetId).Decide(request.Id, ConductorRequestStatus.Withdrawn, "миша", null);

        Assert.Empty(Conductor(TargetId).List());

        var all = Conductor(TargetId).List(all: true);
        Assert.Single(all);
        Assert.Equal(ConductorRequestStatus.Withdrawn, all[0].Status);
    }

    [Fact]
    public void With_The_Box_The_Pending_Request_Still_Comes_First()
    {
        // порядок прежний: то, с чем ещё надо что-то делать, — впереди истории
        var old = Conductor(TargetId).Add(TargetId, ConductorId, "", "миша", null);
        Conductor(ConductorId).Decide(old.Id, ConductorRequestStatus.Rejected, "миша", null);
        var open = Conductor(TargetId).Add(TargetId, ConductorId, "", "миша", null);

        var all = Conductor(ConductorId).List(all: true);

        Assert.Equal(2, all.Count);
        Assert.Equal(open.Id, all[0].Id);
        Assert.Equal(ConductorRequestStatus.Pending, all[0].Status);
    }

    [Fact]
    public void The_Box_Adds_Rejected_Join_Requests()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        _f.Servers.Attach(org.Id, Local().Id, OrgServerStatus.Active, "", null);
        var waiting = _f.Servers.Attach(org.Id, Remote("node2").Id, OrgServerStatus.Pending,
            "mike@example.com", null);
        var rejected = _f.Servers.Attach(org.Id, Remote("node3").Id, OrgServerStatus.Pending,
            "kate@example.com", null);
        _f.Servers.RejectRequest(rejected.Id, null);

        // обычный список — только та заявка, что ждёт решения
        var plain = _f.Servers.Requests();
        Assert.Single(plain);
        Assert.Equal(waiting.Id, plain[0].Id);

        // с флажком — и отклонённая, хотя «ОТКЛОНИТЬ» её мягко удалило
        var all = _f.Servers.Requests(all: true);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.Id == rejected.Id && r.Status == OrgServerStatus.Rejected);
    }

    [Fact]
    public void An_Accepted_Join_Request_Never_Doubles_The_Server_Row()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        _f.Servers.Attach(org.Id, Local().Id, OrgServerStatus.Active, "", null);
        var request = _f.Servers.Attach(org.Id, Remote("node2").Id, OrgServerStatus.Pending,
            "mike@example.com", null);
        _f.Servers.AcceptRequest(request.Id, null);

        // принятая заявка — это подключённый сервер, он уже стоит строкой списка серверов
        Assert.Empty(_f.Servers.Requests());
        Assert.Empty(_f.Servers.Requests(all: true));
        Assert.Contains(_f.Servers.ServersOf(org.Id), s => s.Id != Local().Id);
    }

    [Fact]
    public void A_Deferred_Join_Request_Stays_In_Both_Lists()
    {
        // «ОТЛОЖИТЬ» — решение НЕ принято, такая заявка обязана быть видна и без флажка
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var request = _f.Servers.Attach(org.Id, Remote("node2").Id, OrgServerStatus.Pending,
            "mike@example.com", null);
        _f.Servers.DeferRequest(request.Id, null);

        Assert.Single(_f.Servers.Requests());
        Assert.Single(_f.Servers.Requests(all: true));
        Assert.True(OrgServerStatus.IsRequest(_f.Servers.Requests()[0].Status));
    }
}
