using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-139, ДОРАБОТКА (версия 1.69): ЗАЯВКА НА ПОДКЛЮЧЕНИЕ СЕРВЕРА ПОДАЁТСЯ АНОНИМНО.
///
/// До этой версии, чтобы подать заявку, подключающийся сервер требовал ввести почту и пароль
/// аккаунта НА ДИРИЖЁРЕ: ими брался список его организаций. То есть человека просили передать
/// свои реквизиты чужому серверу (да ещё и по открытому HTTP) — а владелец дирижёра, наоборот,
/// должен был кому-то их сообщить. Ни того, ни другого делать нельзя.
///
/// Теперь:
/// 1. вход на удалённый сервер из протокола убран совсем (нет ни <c>ClusterLoginDto</c>,
///    ни <c>POST /api/cluster/orgs</c>, ни списка его организаций);
/// 2. заявка называет ЗАЯВИТЕЛЯ сама — имя, почту и внутренний ключ его аккаунта на
///    подключаемом сервере; они хранятся у связи и показываются принимающему решение;
/// 3. код организации необязателен: у дирижёра с единственной организацией её не набирают;
/// 4. подтверждение заявки делает заявителя участником организации — исполнитель заводится
///    со ссылкой на ЕГО аккаунт, а сам аккаунт с хэшем пароля приезжает репликацией.
///    Поэтому пароли в кластере не выравнивают: аккаунт у человека один.
/// </summary>
public sealed class T139_2Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. вход на чужой сервер убран из протокола ----------

    /// <summary>
    /// Регрессия «нельзя вернуть передачу пароля»: типов логина на удалённый сервер и метода
    /// «показать его организации» в сборках больше нет. Проверяется отражением — так видно
    /// именно ОТСУТСТВИЕ двери, а не то, что ею перестали пользоваться в одном месте.
    /// </summary>
    [Fact]
    public void Remote_Login_Is_Gone_From_The_Cluster_Protocol()
    {
        var core = typeof(AI2P.Core.Api.ClusterJoinDto).Assembly;
        Assert.Null(core.GetType("AI2P.Core.Api.ClusterLoginDto"));
        Assert.Null(core.GetType("AI2P.Core.Api.ClusterOrgDto"));

        var client = typeof(AI2P.Server.Api.ClusterClient);
        Assert.Null(client.GetMethod("ListOrgsAsync"));
    }

    /// <summary>Заявка везёт заявителя: без этих полей человек на дирижёре не знал бы,
    /// кого пускает, — а спросить не у кого, обмен анонимный.</summary>
    [Fact]
    public void Join_Request_Carries_The_Applicant()
    {
        var dto = new AI2P.Core.Api.ClusterJoinDto
        {
            ApplicantId = "acc-1",
            ApplicantName = "Иван Петров",
            ApplicantEmail = "ivan@localhost",
        };

        Assert.Equal("acc-1", dto.ApplicantId);
        Assert.Equal("Иван Петров", dto.ApplicantName);
        Assert.Equal("ivan@localhost", dto.ApplicantEmail);
        // код организации допускается ПУСТЫМ: у дирижёра с одной организацией его негде взять
        Assert.Equal("", dto.OrgCode);
    }

    // ---------- 2. заявитель ----------

    [Fact]
    public void Applicant_Needs_An_Account_And_An_Email()
    {
        Assert.False(new JoinApplicant("acc-1", "Иван", "ivan@localhost").IsEmpty);
        // без почты заявителя не опознать, без ключа аккаунта его нечем сделать участником
        Assert.True(new JoinApplicant("", "Иван", "ivan@localhost").IsEmpty);
        Assert.True(new JoinApplicant("acc-1", "Иван", "   ").IsEmpty);

        Assert.Equal("Иван <ivan@localhost>", new JoinApplicant("acc-1", "Иван", "ivan@localhost").Display());
        // имени человек может не задать — тогда его называет почта
        Assert.Equal("ivan@localhost", new JoinApplicant("acc-1", "", "ivan@localhost").Display());
    }

    /// <summary>Заявитель хранится у связи «организация ↔ сервер»: решение принимают
    /// не сразу, а заявку до тех пор нужно кому-то помнить.</summary>
    [Fact]
    public void Attach_Remembers_The_Applicant()
    {
        var server = _f.Servers.Create(new ServerSaveInput
        {
            Name = "worker-pc", Protocol = "http", Hostname = "worker-pc", Port = 5480,
            BasePath = "/ai2p", IsActive = false,
        }, null);

        var org = _f.Orgs.Create("Фирма", "acme", null);
        var link = _f.Servers.Attach(org.Id, server.Id, OrgServerStatus.Pending,
            "Иван <ivan@localhost>", actorId: null,
            applicant: new JoinApplicant("acc-1", "  Иван  ", " ivan@localhost "));

        var stored = _f.Servers.LinkById(link.Id)!;
        Assert.Equal("acc-1", stored.ApplicantId);
        Assert.Equal("Иван", stored.ApplicantName);
        Assert.Equal("ivan@localhost", stored.ApplicantEmail);
        Assert.Equal("Иван <ivan@localhost>", stored.RequestedBy);
        Assert.Equal(OrgServerStatus.Pending, stored.Status);
    }

    /// <summary>Служебные привязки (локальный сервер, дирижёр при подключении) заявителя
    /// не имеют — старые связи должны остаться законными.</summary>
    [Fact]
    public void Link_Without_An_Applicant_Is_Still_Valid()
    {
        var server = _f.Servers.Create(new ServerSaveInput
        {
            Name = "conductor", Protocol = "http", Hostname = "conductor", Port = 5480,
            BasePath = "/ai2p", IsActive = true,
        }, null);

        var org = _f.Orgs.Create("Фирма", "acme", null);
        var link = _f.Servers.Attach(org.Id, server.Id, OrgServerStatus.Active,
            requestedBy: "", actorId: null);

        var stored = _f.Servers.LinkById(link.Id)!;
        Assert.Equal("", stored.ApplicantId);
        Assert.Equal("", stored.ApplicantEmail);
        Assert.True(new JoinApplicant(stored.ApplicantId, stored.ApplicantName,
            stored.ApplicantEmail).IsEmpty);
    }

    // ---------- 3. схема серверной БД ----------

    /// <summary>Колонки заявителя добавляются идемпотентной миграцией (схема серверной БД v22):
    /// на пустой базе и на обновлении результат одинаков, а журнал изменений строится
    /// по фактическому составу колонок — репликация подхватывает их сама.</summary>
    [Fact]
    public void Applicant_Columns_Are_In_The_Server_Schema()
    {
        using var conn = _f.ServerDb.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(org_servers)", r => r.S("name"));

        Assert.Contains("applicant_id", columns);
        Assert.Contains("applicant_name", columns);
        Assert.Contains("applicant_email", columns);
        // версия серверной схемы растёт дальше: v23 — T-160 (очередь повтора репликации),
        // v24 — T-50-S0 (второй, внешний адрес сервера)
        Assert.Equal("24", Sql.Scalar<string>(conn, null,
            "SELECT value FROM meta WHERE key='schema_version'"));
    }
}
