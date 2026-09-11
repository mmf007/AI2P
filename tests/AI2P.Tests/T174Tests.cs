using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-174 «ошибка репликации» — третий заход (после T-153 «не идёт репликация» и T-160
/// «первичная не до конца»).
///
/// Случай заказчика: репликацию сбросили на первоначальную на ОБОИХ серверах, адреса верные,
/// ручной пуск отвечает «репликация запущена» — и не происходит ничего. На дирижёре в колонке
/// статуса вечный прогресс-бар с 0%, на втором сервере — вечный обратный отсчёт. Ошибок нет
/// нигде. Плюс к этому: администратор второго сервера упирался в «нужна роль owner, у вас
/// admin», а пункт меню «вход администратора сервера» вёл на ВНЕШНИЙ адрес, где такой вход
/// запрещён по определению.
///
/// Разбор: сеанс держит замок пары в памяти, и снять его обязано окончание сеанса — своё
/// в <c>finally</c>, чужое запросом <c>/repl/end</c>. Второе приходит не всегда (ведущий упал,
/// перезапустился, потерял связь), и тогда пара оставалась занятой навсегда: у держащей
/// стороны «идёт сеанс» с нулевым прогрессом, у второй — «занято» на каждый запуск и новый
/// отсчёт. Ошибок при этом не бывает: с точки зрения кода всё штатно.
/// </summary>
public sealed class T174Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t174-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string OrgId = "org-1";
    private const string PeerId = "unid-S1";

    /// <summary>Реестр «сервера» с пустой серверной БД: замок пары и состояние репликации
    /// живут именно в нём.</summary>
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
            LocalServerId: () => "unid-S0",
            LocalCode: _ => "S0",
            ServerCode: (_, _) => "S1",
            ServerName: _ => "сервер");
        return new OrgRegistry(Path.Combine(_dir, "server"), "server.db", deps);
    }

    /// <summary>Сделать замок протухшим, не дожидаясь десяти минут: время последнего движения
    /// отматывается назад тем же способом, каким его двигает сам сеанс.</summary>
    private static void Age(ReplicationProgress progress, TimeSpan back) =>
        progress.Touch(DateTime.UtcNow - back);

    // ---------- 1. замок пары не бывает вечным ----------

    [Fact]
    public void Second_Session_Is_Ignored_While_The_First_One_Is_Alive()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Manual, out var first));
        // ровно то, что требуется по todo43: второй запуск игнорируется
        Assert.False(repl.TryHold(OrgId, PeerId, ReplicationStarters.Timer, out var busy));
        // при отказе называется тот сеанс, который уже идёт, — его показывают человеку
        Assert.Same(first, busy);
        Assert.True(repl.IsRunning(OrgId, PeerId));
    }

    [Fact]
    public void Hold_Without_Any_Movement_Is_Taken_Over()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Peer, out var stuck));
        Age(stuck, ReplicationProgress.StaleAfter + TimeSpan.FromMinutes(1));

        // «идёт сеанс» — уже неправда: движения нет дольше предела
        Assert.False(repl.IsRunning(OrgId, PeerId));
        Assert.Null(repl.Progress(OrgId, PeerId));
        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Manual, out var mine));
        Assert.NotSame(stuck, mine);
        Assert.True(repl.IsRunning(OrgId, PeerId));
    }

    [Fact]
    public void Movement_On_The_Pair_Keeps_The_Hold_Alive()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Peer, out var held));
        Age(held, ReplicationProgress.StaleAfter + TimeSpan.FromMinutes(1));
        // запрос партнёра — это и есть признак, что ведущий на месте (ClusterEndpoints.Pair)
        repl.Touch(OrgId, PeerId);

        Assert.True(repl.IsRunning(OrgId, PeerId));
        Assert.False(repl.TryHold(OrgId, PeerId, ReplicationStarters.Timer, out _));

        // движение отмечает и сам ход сеанса: пачка изменений двигает Done
        Age(held, ReplicationProgress.StaleAfter + TimeSpan.FromMinutes(1));
        held.Done += 400;
        Assert.True(repl.IsRunning(OrgId, PeerId));
    }

    [Fact]
    public void Peer_Starting_Over_Releases_Its_Own_Unclosed_Session()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        // сеанс партнёра, который не был закрыт (партнёр перезапустился, оборвалась связь)
        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Peer, out var abandoned));
        // ждать протухания незачем: раз он звонит снова, прошлого сеанса больше нет
        Assert.True(repl.HoldForPeer(OrgId, PeerId, out var fresh));
        Assert.NotSame(abandoned, fresh);
        Assert.Equal(ReplicationStarters.Peer, fresh.StartedBy);
    }

    [Fact]
    public void Peer_Does_Not_Take_Over_Our_Own_Live_Session()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        // свой сеанс (ручной пуск) идёт прямо сейчас — партнёру отвечают «занято»
        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Manual, out var mine));
        Assert.False(repl.HoldForPeer(OrgId, PeerId, out var busy));
        Assert.Same(mine, busy);
    }

    [Fact]
    public void Release_Frees_The_Pair()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Peer, out _));
        // кнопка «первичная репликация» снимает замок — ручной рычаг «отпустить пару»
        repl.Release(OrgId, PeerId);
        Assert.False(repl.IsRunning(OrgId, PeerId));
        Assert.True(repl.TryHold(OrgId, PeerId, ReplicationStarters.Manual, out _));
    }

    /// <summary>Пассивный замок (его ставит <c>/repl/begin</c>) сразу после взятия показывает
    /// 0% — именно он и висел у заказчика на дирижёре. Пока сеанс жив, это законно.</summary>
    [Fact]
    public void Fresh_Peer_Hold_Shows_Zero_Percent()
    {
        using var registry = Registry();
        using var repl = new ReplicationService(registry);

        Assert.True(repl.HoldForPeer(OrgId, PeerId, out var progress));
        Assert.Equal(0, progress.Percent);
        Assert.Equal(ReplicationStarters.Peer, progress.StartedBy);
        Assert.Contains("0%", progress.Describe());
    }

    // ---------- 2. «идёт сеанс» не переживает перезапуск ----------

    [Fact]
    public void Running_Status_Left_By_A_Crash_Is_Cleared_On_Start()
    {
        using var registry = Registry();
        registry.ReplState.Save(new ReplicationState
        {
            OrgId = OrgId, ServerId = PeerId, Status = ReplicationStatuses.Running,
            LastRunAt = DateTime.UtcNow.AddHours(-5),
        });
        registry.ReplState.Save(new ReplicationState
        {
            OrgId = OrgId, ServerId = "unid-S2", Status = ReplicationStatuses.Error,
            LastError = "нет связи",
        });

        Assert.Equal(1, registry.ReplState.ClearRunning());

        Assert.Equal(ReplicationStatuses.Idle, registry.ReplState.Get(OrgId, PeerId).Status);
        // чужие состояния не трогаются: ошибку прошлого сеанса стирать нечем и незачем
        var other = registry.ReplState.Get(OrgId, "unid-S2");
        Assert.Equal(ReplicationStatuses.Error, other.Status);
        Assert.Equal("нет связи", other.LastError);
        // повторный запуск снимать уже нечего
        Assert.Equal(0, registry.ReplState.ClearRunning());
    }

    // ---------- 3. права: admin — хозяин своего компьютера ----------

    [Fact]
    public void Own_Computer_Is_Managed_By_Admin_Not_By_Owner()
    {
        // список серверов, запись своего сервера, ручной пуск и диагностика репликации
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("GET", "/api/servers"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("GET", "/api/servers/replication"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("POST", "/api/servers/unid-S1/replicate"));
        Assert.Equal(SystemRole.Admin,
            ApiPermissions.Required("POST", "/api/servers/unid-S1/replication/reset"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("PUT", "/api/servers/unid-S1"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("POST", "/api/servers/unid-S1/join"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("DELETE", "/api/servers/unid-S1"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("GET", "/api/servers/requests"));
    }

    [Fact]
    public void Cluster_Wide_Decisions_Are_Still_The_Owners()
    {
        // кого пустить в организацию
        Assert.Equal(SystemRole.Owner,
            ApiPermissions.Required("POST", "/api/servers/requests/req-1/accept"));
        Assert.Equal(SystemRole.Owner,
            ApiPermissions.Required("POST", "/api/servers/requests/req-1/reject"));
        // передача задач выбывшего сервера дирижёру
        Assert.Equal(SystemRole.Owner,
            ApiPermissions.Required("POST", "/api/servers/unid-S1/takeover"));
        // пользователи и организации — по-прежнему только владельцу
        Assert.Equal(SystemRole.Owner, ApiPermissions.Required("GET", "/api/accounts"));
        Assert.Equal(SystemRole.Owner, ApiPermissions.Required("PUT", "/api/orgs/org-1"));
        Assert.Equal(SystemRole.Owner, ApiPermissions.Required("PUT", "/api/orgs/org-1/servers"));
    }

    // ---------- 4. вход администратора сервера — всегда по петлевому адресу ----------

    [Fact]
    public void Server_Admin_Link_Is_Always_Local()
    {
        // так строится ссылка пункта меню: UiState.LocalBaseUrl + "/server-admin"
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp/server-admin",
            LinkUrls.ToLocal("http://84.201.0.7:5480/ai2p/mmfgrp") + "/server-admin");
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp/server-admin",
            LinkUrls.ToLocal("http://мойпк:5480/ai2p/mmfgrp") + "/server-admin");
        // адрес и так петлевой — ссылка не меняется
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp/server-admin",
            LinkUrls.ToLocal("http://localhost:5480/ai2p/mmfgrp") + "/server-admin");
    }
}
