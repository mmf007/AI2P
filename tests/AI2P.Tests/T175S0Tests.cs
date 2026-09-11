using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-175-S0: подключение второго сервера к ВТОРОЙ организации.
///
/// Жалоба заказчика (версия 1.120): на дирижёре <c>S1</c> заведена вторая организация,
/// и (1) галочка сервера <c>S0</c> в её списке серверов отвечает 500 и сбрасывается,
/// (2) «подать заявку» с самого <c>S0</c> отвечает «… ответил 500 … на запрос join».
///
/// Причина у обоих одна: отвязка сервера (<c>Detach</c>) и отказ по заявке
/// (<c>RejectRequest</c>) удаляют связь МЯГКО — строка остаётся, чтобы партнёр узнал об
/// этом репликацией, — а <c>UNIQUE(org_id, server_id)</c> считает и её. <c>Attach</c> искал
/// только ЖИВУЮ связь и заводил вторую строку, упираясь в «UNIQUE constraint failed».
/// Снятую однажды галочку было не вернуть, а отклонённый сервер не мог подать заявку
/// уже никогда.
/// </summary>
public sealed class T175S0Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "ai2p-t175s0-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>Установка заказчика: свой сервер (в кластере его зовут <c>S1</c>), сосед
    /// <c>S0</c> и ВТОРАЯ организация, заведённая здесь же.</summary>
    private static (Organization Org, string PeerId) SetUp(OrgRegistry registry)
    {
        registry.EnsureLocalServer("s1", "http", "s1.local", 5480, "/ai2p");
        var peer = registry.Servers.Create(new ServerSaveInput
        {
            Name = "s0", Hostname = "s0.local", Port = 5480,
        }, actorId: null, id: "unid-s0");
        var owner = registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Хозяин", Email = "owner@localhost", Password = "12345678",
        }, actorId: null);
        var (org, _) = registry.CreateOrg("Вторая", "vtoraya", owner, actorId: null);
        return (org, peer.Id);
    }

    /// <summary>Галочку сервера сняли и ставят обратно (жалоба, п. 1): связь оживает,
    /// а не заводится второй строкой — иначе <c>UNIQUE</c> и 500.</summary>
    [Fact]
    public void Unchecked_Server_Can_Be_Checked_Again()
    {
        using var registry = Registry();
        var (org, peerId) = SetUp(registry);
        var first = registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Active,
            requestedBy: "", actorId: null);
        registry.Servers.Detach(org.Id, peerId, actorId: null);
        Assert.Null(registry.Servers.Link(org.Id, peerId));

        var again = registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Active,
            requestedBy: "", actorId: null);

        Assert.Equal(first.Id, again.Id);          // строка та же: её идентификатор общий на кластер
        Assert.Equal(first.Code, again.Code);      // и код в организации прежний
        var live = Assert.Single(registry.Servers.ServersOf(org.Id)
            .Where(l => l.ServerId == peerId));
        Assert.Equal(OrgServerStatus.Active, live.Status);
        Assert.Null(live.DeletedAt);
    }

    /// <summary>Заявку отклонили, и сервер подаёт её заново (жалоба, п. 2): отказ тоже
    /// удаляет связь мягко, поэтому до T-175-S0 подключиться было нельзя уже никогда.</summary>
    [Fact]
    public void Rejected_Server_Can_Apply_Again()
    {
        using var registry = Registry();
        var (org, peerId) = SetUp(registry);
        var applicant = new JoinApplicant("acc-s0", "Сосед", "s0@localhost");
        var first = registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Pending,
            requestedBy: "Сосед", actorId: null, applicant: applicant);
        registry.Servers.RejectRequest(first.Id, actorId: null);
        Assert.Null(registry.Servers.Link(org.Id, peerId));

        var again = registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Pending,
            requestedBy: "Сосед", actorId: null, applicant: applicant);

        Assert.Equal(first.Id, again.Id);
        var live = registry.Servers.Link(org.Id, peerId);
        Assert.NotNull(live);
        Assert.Equal(OrgServerStatus.Pending, live!.Status);
        Assert.Contains(registry.Servers.Requests(), r => r.Id == again.Id);
    }

    /// <summary>Дирижёр организации при этом не меняется: у оживлённой связи признак
    /// дирижёра ставится по тому же правилу, что и у новой — «только если его ещё нет».</summary>
    [Fact]
    public void Reviving_A_Link_Does_Not_Steal_Conductorship()
    {
        using var registry = Registry();
        var (org, peerId) = SetUp(registry);
        var local = registry.Servers.LocalId();
        registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Active, requestedBy: "",
            actorId: null);
        registry.Servers.Detach(org.Id, peerId, actorId: null);

        var again = registry.Servers.Attach(org.Id, peerId, OrgServerStatus.Active,
            requestedBy: "", actorId: null);

        Assert.False(again.IsConductor);
        Assert.Equal(local, registry.Servers.Conductor(org.Id)?.ServerId);
    }
}
