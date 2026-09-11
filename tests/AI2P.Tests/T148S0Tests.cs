using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-148-S0, ВТОРОЙ ЗАХОД: создание новой организации на сервере, который в кластере зовут
/// не <c>S0</c>, починка уже сложившихся установок и удаление организации.
///
/// Случай заказчика (жалоба после выпуска 1.117, когда первый заход уже стоял): на дирижёре
/// <c>S1</c> заведена новая организация, и (1) в списке она значится за сервером <c>S0</c>,
/// (2) её номер совпал с номером приехавшей организации (<c>ORG-1</c>), (3) каталог получил
/// приставку (<c>ORG-1-2</c>), (4) в форме смены организации её нет. Первый заход чинил
/// только НОВЫЕ организации, а уже заведённые так и остались с чужим кодом сервера
/// и задвоенным номером — эти проверки сторожат именно починку и удаление.
/// </summary>
public sealed class T148S0Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "ai2p-t148s0-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private OrgRegistry Registry()
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
            IsConductor: _ => true,
            LocalServerId: () => "",
            LocalCode: _ => "",
            ServerCode: (_, _) => "",
            ServerName: _ => "сервер");
        return new OrgRegistry(Path.Combine(_dir, "server"), "server.db", deps);
    }

    private const string ClusterOrgId = "org-cluster";

    /// <summary>Установка «сервера S1»: свой сервер, запись дирижёра кластера и приехавшая
    /// от него организация <c>ORG-1</c>, где этот компьютер зовут <c>S1</c>.</summary>
    private static Account SetUpS1(OrgRegistry registry)
    {
        registry.EnsureLocalServer("s1", "http", "s1.local", 5480, "/ai2p");
        var peer = registry.Servers.Create(new ServerSaveInput
        {
            Name = "s0", Hostname = "s0.local", Port = 5480,
        }, actorId: null, id: "unid-s0");
        registry.AdoptOrg(new ClusterJoinStatusDto
        {
            RequestId = "link-mine",
            Status = OrgServerStatus.Active,
            Code = "S1",
            OrgId = ClusterOrgId,
            OrgDisplayId = "ORG-1",
            OrgName = "Кластерная",
            OrgCode = "cluster",
            ConductorLinkId = "link-cond",
            ConductorCode = "S0",
        }, peer.Id, actorId: null);
        var owner = registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Хозяин", Email = "owner@localhost", Password = "12345678",
        }, actorId: null);
        var org = registry.Orgs.Get(ClusterOrgId)!;
        registry.AddMember(registry.Context(org), owner, SystemRole.Owner, actorId: null);
        return owner;
    }

    // ---------- 1. НОВАЯ организация на сервере, которого зовут S1 ----------

    [Fact]
    public void New_Org_Takes_The_Code_This_Machine_Is_Known_By()
    {
        using var registry = Registry();
        var owner = SetUpS1(registry);

        var (org, _) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);

        var link = Assert.Single(registry.Servers.ServersOf(org.Id));
        Assert.Equal("S1", link.Code);          // не S0: этим кодом зовут соседнюю машину
        Assert.True(link.IsConductor);
        Assert.True(registry.Servers.IsLocalConductor(org.Id));
        Assert.Equal("ORG-2", org.DisplayId);   // номер приехавшей ORG-1 не переиспользуется
        Assert.Equal("ORG-2", registry.Orgs.DirName(org));
        // создавший — участник: без этого организации нет в форме смены (жалоба, п. 4)
        Assert.Contains(registry.OrgsOf(owner.Id), o => o.Id == org.Id);
    }

    /// <summary>Код кластера берётся у организации, где серверов НЕСКОЛЬКО: там его выдал
    /// дирижёр. Своя одиночная организация свидетельствовать не может — иначе однажды
    /// выданный себе <c>S0</c> размножался бы на все следующие.</summary>
    [Fact]
    public void Cluster_Code_Is_Taken_From_The_Org_With_Several_Servers()
    {
        using var registry = Registry();
        var owner = SetUpS1(registry);
        var local = registry.Servers.Local()!;
        var (wrong, _) = registry.CreateOrg("Своя", "svoya", owner, actorId: null);
        registry.Servers.SetCode(wrong.Id, local.Id, "S7", actorId: null);

        Assert.Equal("S1", registry.Servers.ClusterCodeOf(local.Id));
    }

    // ---------- 2. ПОЧИНКА уже сложившейся установки ----------

    /// <summary>Организация, заведённая прежней версией: номер задвоен с приехавшей,
    /// а локальный сервер записан в ней как <c>S0</c> — кодом соседней машины.</summary>
    private void MakeLegacy(OrgRegistry registry, string orgId)
    {
        registry.Servers.SetCode(orgId, registry.Servers.LocalId(), "S0", actorId: null);
        var db = new Database(Path.Combine(_dir, "server"), "server.db", DatabaseKind.Server);
        using var conn = db.Open();
        Sql.Exec(conn, null, "UPDATE orgs SET display_id='ORG-1' WHERE id=@id", ("@id", orgId));
    }

    [Fact]
    public void Startup_Repairs_The_Duplicated_Number_And_The_Foreign_Server_Code()
    {
        string legacyId;
        using (var registry = Registry())
        {
            var owner = SetUpS1(registry);
            var (legacy, _) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);
            legacyId = legacy.Id;
            MakeLegacy(registry, legacyId);
            Assert.Equal("ORG-1", registry.Orgs.Get(legacyId)!.DisplayId);
        }

        // перезапуск приложения
        using var restarted = Registry();
        restarted.EnsureLocalServer("s1", "http", "s1.local", 5480, "/ai2p");
        restarted.StartAll();

        Assert.NotEqual("ORG-1", restarted.Orgs.Get(legacyId)!.DisplayId);
        Assert.Equal("S1", restarted.Servers.Link(legacyId, restarted.Servers.LocalId())!.Code);
        // приехавшую организацию чинить нельзя: и номер, и код сервера в ней общие на кластер
        Assert.Equal("ORG-1", restarted.Orgs.Get(ClusterOrgId)!.DisplayId);
        Assert.Equal("S1", restarted.Servers.Link(ClusterOrgId, restarted.Servers.LocalId())!.Code);
    }

    // ---------- 3. УДАЛЕНИЕ организации ----------

    [Fact]
    public void Empty_Org_Is_Deleted_With_Its_Records_And_Directory()
    {
        using var registry = Registry();
        var owner = SetUpS1(registry);
        var (org, _) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);
        var dir = registry.DirOf(org);
        Assert.True(Directory.Exists(dir));
        Assert.Null(registry.DeleteProblem(org.Id));

        registry.DeleteOrg(org.Id, actorId: null);

        Assert.Null(registry.Orgs.Get(org.Id));
        Assert.False(Directory.Exists(dir));
        Assert.Empty(registry.Servers.ServersOf(org.Id));
        Assert.Equal("", registry.Orgs.KnownDirName(org.Id));
        Assert.DoesNotContain(registry.OrgsOf(owner.Id), o => o.Id == org.Id);
        // имя каталога освободилось не насовсем: номер удалённой организации новой не отдаём
        Assert.NotEqual(org.DisplayId, registry.Orgs.List().Single(o => o.Id == ClusterOrgId).DisplayId);
    }

    [Fact]
    public void Org_With_Tasks_Is_Not_Deleted()
    {
        using var registry = Registry();
        var owner = SetUpS1(registry);
        var (org, context) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);
        var project = context.Projects.Create("Проект", null, null, actorId: null);
        context.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "", "", actorId: null);

        Assert.False(string.IsNullOrEmpty(registry.DeleteProblem(org.Id)));
        Assert.Throws<ArgumentException>(() => registry.DeleteOrg(org.Id, actorId: null));
        Assert.NotNull(registry.Orgs.Get(org.Id));
        Assert.True(Directory.Exists(registry.DirOf(org)));
    }

    /// <summary>Шаблон задачи удалению не мешает: он приходит с дистрибутивом (TemplateSeed)
    /// и лежит в той же таблице, что задачи, — иначе пустую организацию нельзя было бы
    /// удалить никогда.</summary>
    [Fact]
    public void A_Template_Does_Not_Block_The_Deletion()
    {
        using var registry = Registry();
        var owner = SetUpS1(registry);
        var (org, context) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);
        context.Tasks.Create(new TaskItem { Title = "Узел шаблона", IsTemplate = true },
            "", "", actorId: null);

        Assert.Null(registry.DeleteProblem(org.Id));
    }

    [Fact]
    public void The_Only_Org_Is_Not_Deleted()
    {
        using var registry = Registry();
        registry.EnsureLocalServer("s1", "http", "s1.local", 5480, "/ai2p");
        var owner = registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Хозяин", Email = "owner@localhost", Password = "12345678",
        }, actorId: null);
        var (org, _) = registry.CreateOrg("Первая", "pervaya", owner, actorId: null);

        Assert.False(string.IsNullOrEmpty(registry.DeleteProblem(org.Id)));
    }
}
