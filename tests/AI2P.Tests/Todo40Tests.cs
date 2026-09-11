using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo40 / этап 40 (ТЗ v1.47): организации на одном сервере — серверная БД, БД и каталог
/// на организацию, коды организаций в URL, участие аккаунта и изоляция данных.
/// </summary>
public sealed class Todo40Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // --- код организации в URL (ТЗ гл. 11) ---

    [Fact]
    public void Code_Is_Transliterated_And_Url_Safe()
    {
        Assert.Equal("moya-firma", Organization.NormalizeCode("Моя Фирма"));
        Assert.Equal("acme", Organization.NormalizeCode("  ACME  "));
        Assert.Equal("test-2", Organization.NormalizeCode("Test #2"));
        // из совсем непригодного названия код всё равно получается
        Assert.Equal("org", Organization.NormalizeCode("!!!"));
    }

    [Fact]
    public void Code_Is_Derived_From_Name_When_Omitted()
    {
        var org = _f.Orgs.Create("Моя Фирма", "", null);

        Assert.Equal("ORG-1", org.DisplayId);
        Assert.Equal("moya-firma", org.Code);
        Assert.True(org.IsActive);
    }

    [Fact]
    public void Code_Is_Unique_And_Case_Insensitive()
    {
        _f.Orgs.Create("Первая", "acme", null);

        Assert.Throws<ArgumentException>(() => _f.Orgs.Create("Вторая", "ACME", null));
    }

    [Fact]
    public void Reserved_Codes_Are_Rejected()
    {
        // организация с таким кодом сделала бы недоступной страницу входа или API (гл. 11)
        Assert.Throws<ArgumentException>(() => _f.Orgs.Create("Апи", "api", null));
        Assert.Throws<ArgumentException>(() => _f.Orgs.Create("Вход", "login", null));
    }

    [Fact]
    public void Empty_Name_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _f.Orgs.Create("   ", "acme", null));
    }

    [Fact]
    public void Org_Is_Found_By_Code_Ignoring_Case()
    {
        var org = _f.Orgs.Create("Моя Фирма", "acme", null);

        Assert.Equal(org.Id, _f.Orgs.ByCode("ACME")?.Id);
        Assert.Null(_f.Orgs.ByCode("нет-такой"));
        Assert.Null(_f.Orgs.ByCode(""));
    }

    // --- локальный сервер организации (ТЗ гл. 6; сами серверы — этап 41, Todo41Tests) ---

    [Fact]
    public void New_Org_Gets_Local_Conductor_Server()
    {
        // связь с локальным сервером заводит реестр организаций: сервис организаций про
        // серверы не знает — с этапа 41 они отдельная сущность уровня установки
        var org = _f.CreateOrgWithLocalServer("Моя Фирма", "acme");

        var server = Assert.Single(_f.Servers.ServersOf(org.Id));
        Assert.Equal("S0", server.Code);
        Assert.True(server.IsLocal);
        // дирижёр обязателен и всегда ровно один; первый сервер им и становится
        Assert.True(server.IsConductor);
        Assert.Equal("http://localhost:5480/ai2p", server.Url);
    }

    // --- правка и «удаление» (ТЗ гл. 6: пока архивации нет — снятие активности) ---

    [Fact]
    public void Update_Changes_Name_Code_And_Activity()
    {
        var org = _f.Orgs.Create("Моя Фирма", "acme", null);

        var updated = _f.Orgs.Update(org.Id, "Другая", "other", isActive: false, null);

        Assert.Equal("Другая", updated.Name);
        Assert.Equal("other", updated.Code);
        Assert.False(updated.IsActive);
        // неактивная организация не открывается и не предлагается в переключателе
        Assert.Empty(_f.Orgs.List(includeInactive: false));
        Assert.Single(_f.Orgs.List());
    }

    // --- разделение баз (ТЗ п. 6.4.1) ---

    [Fact]
    public void Accounts_And_Orgs_Live_Only_In_Server_Database()
    {
        // БД организации не должна принимать сервисы серверного уровня: перепутать их —
        // значит развести аккаунты по организациям и потерять единый логин (п. 2.14)
        Assert.Throws<ArgumentException>(() => new AccountService(_f.Db, _f.Events));
        Assert.Throws<ArgumentException>(() => new OrgService(_f.Db, _f.Events));
    }

    [Fact]
    public void Org_Databases_Number_Entities_Independently()
    {
        // у каждой организации своя БД, поэтому и счётчики display_id свои: задача T-1
        // есть в каждой организации и это разные задачи (ТЗ гл. 6 «Номера задач»)
        var second = new Database(Path.Combine(_f.Dir, "org2"), "ai2p.db");
        second.Init(_f.Db.NodeId);
        var files2 = new FileStore(second.DataDir);
        var events2 = new EventStore(second);
        var tasks2 = new TaskService(second, events2, files2);

        var here = _f.Tasks.Create(new TaskItem { Title = "Первая" }, "", "", null);
        var there = tasks2.Create(new TaskItem { Title = "Первая" }, "", "", null);

        Assert.Equal("T-1", here.DisplayId);
        Assert.Equal("T-1", there.DisplayId);
        Assert.NotEqual(here.Id, there.Id);          // id — UUID, они всё равно различны
        Assert.Single(_f.Tasks.List());              // и списки не пересекаются
        Assert.Single(tasks2.List());
    }

    [Fact]
    public void Org_Databases_Keep_The_Node_Id_Of_The_Server()
    {
        // узел (компьютер) один на установку — иначе журналы разных баз одного сервера
        // выглядели бы как журналы разных узлов и не слились бы при репликации (п. 6.4.3)
        var second = new Database(Path.Combine(_f.Dir, "org2"), "ai2p.db");
        second.Init(_f.ServerDb.NodeId);

        Assert.Equal(_f.ServerDb.NodeId, second.NodeId);
    }

    // --- участие аккаунта в организации (ТЗ п. 2.15) ---

    [Fact]
    public void Membership_Is_An_Executor_With_The_Account()
    {
        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Email = "mike@example.com",
            Name = "Майк",
            Password = "секрет",
        }, null);

        Assert.Null(_f.Executors.ByAccount(account.Id)); // ещё не участник — доступа нет

        var executor = _f.AddMember(account, SystemRole.Owner);

        Assert.Equal(executor.Id, _f.Executors.ByAccount(account.Id)?.Id);
        Assert.Equal(account.Id, executor.AccountId);
    }

    [Fact]
    public void Role_Belongs_To_The_Organization_Not_To_The_Account()
    {
        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Email = "mike@example.com",
            Name = "Майк",
            Password = "",
        }, null);
        _f.AddMember(account, SystemRole.Reader);

        // во второй организации тот же аккаунт может быть владельцем: роль живёт
        // у исполнителя, а исполнитель — в БД организации (п. 2.2)
        var second = new Database(Path.Combine(_f.Dir, "org2"), "ai2p.db");
        second.Init(_f.Db.NodeId);
        var files2 = new FileStore(second.DataDir);
        var executors2 = new ExecutorService(second, new EventStore(second), files2);
        executors2.Create(new Executor
        {
            Nick = "Майк",
            Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Owner,
            AccountId = account.Id,
        }, null);

        Assert.Equal(SystemRole.Reader, _f.Executors.ByAccount(account.Id)!.SystemRole);
        Assert.Equal(SystemRole.Owner, executors2.ByAccount(account.Id)!.SystemRole);
    }

    [Fact]
    public void Unique_Nick_Does_Not_Collide()
    {
        _f.Executors.Create(new Executor { Nick = "Майк", Kind = ExecutorKind.Human }, null);

        Assert.Equal("Майк 2", _f.Executors.UniqueNick("Майк"));
        Assert.Equal("Майк", _f.Executors.UniqueNick("  Майк  ")[..4]);
        Assert.Equal("user", _f.Executors.UniqueNick(""));
    }

    // --- журнал организаций (ТЗ п. 6.4.3): серверный, не организационный ---

    [Fact]
    public void Org_Changes_Are_Logged_To_The_Server_Journal()
    {
        var org = _f.Orgs.Create("Моя Фирма", "acme", null);
        _f.Orgs.Update(org.Id, "Моя Фирма 2", "acme", isActive: true, null);

        var types = _f.ServerEvents.Query(limit: 100).Select(e => e.EventType).ToList();
        Assert.Contains("org.created", types);
        Assert.Contains("org.updated", types);
        // в журнале организации их нет: организация про себя саму ничего не знает
        Assert.DoesNotContain(_f.Events.Query(limit: 100), e => e.EventType.StartsWith("org."));
    }
}
