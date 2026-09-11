using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo41 / этап 41 (ТЗ v1.48): сеть и серверы — список серверов кластера, связь
/// «организация ↔ сервер» многие-ко-многим, коды S0/S1, дирижёр, двустороннее подключение
/// (заявка → ПРИНЯТЬ/ОТЛОЖИТЬ/ОТКЛОНИТЬ) и токены доступа сервер-сервер.
/// </summary>
public sealed class Todo41Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ServerNode Local() =>
        _f.Servers.Local() ?? _f.Servers.EnsureLocal("local", "http", "localhost", 5480, "/ai2p");

    private ServerNode Remote(string host = "node2", int port = 5480) =>
        _f.Servers.Create(new ServerSaveInput
        {
            Name = host,
            Protocol = "http",
            Hostname = host,
            Port = port,
            BasePath = "/ai2p",
        }, null);

    // --- серверы как сущность уровня установки (ТЗ гл. 6) ---

    [Fact]
    public void Local_Server_Is_Created_Once_And_Keeps_Its_Key()
    {
        var first = _f.Servers.EnsureLocal("this", "http", "localhost", 5480, "/ai2p");
        // адрес сменился (правка config.json) — запись та же: внутренний ключ репликации
        // менять нельзя, иначе кластер перестал бы узнавать сервер (todo41 п. 6)
        var again = _f.Servers.EnsureLocal("this", "http", "ai2p.example.com", 5491, "/ai2p");

        Assert.Equal(first.Id, again.Id);
        Assert.Equal("http://ai2p.example.com:5491/ai2p", again.Url);
        Assert.True(again.IsLocal);
        Assert.Single(_f.Servers.List());
    }

    [Fact]
    public void Local_Server_Is_Always_First_In_The_List()
    {
        // список серверов начинается с локального — он «свой» и его форма особенная (п. 2)
        Remote("node2");
        Local();
        Remote("node3");

        var servers = _f.Servers.List();

        Assert.Equal(3, servers.Count);
        Assert.True(servers[0].IsLocal);
    }

    /// <summary>Адрес проверяется на осмысленность; УНИКАЛЬНЫМ он больше не обязан быть
    /// (T-141): серверы различаются внутренним ключом, а за NAT половина кластера законно
    /// называется «localhost» — см. T141Tests.</summary>
    [Fact]
    public void Remote_Server_Address_Is_Validated()
    {
        Remote("node2");
        Remote("node2");   // тот же адрес — законно, это разные серверы за одним NAT

        Assert.Equal(2, _f.Servers.List().Count(s => !s.IsLocal));
        Assert.Throws<ArgumentException>(() => _f.Servers.Create(new ServerSaveInput
        {
            Hostname = "",
            Port = 5480,
        }, null));
        Assert.Throws<ArgumentException>(() => _f.Servers.Create(new ServerSaveInput
        {
            Hostname = "node9",
            Port = 0,
        }, null));
    }

    [Fact]
    public void Local_Server_Address_Is_Not_Edited_Through_The_Server_List()
    {
        // адрес локального сервера лежит в config.json: от него зависит запуск приложения,
        // и править его нужно уметь снаружи системы (todo41 п. 3)
        var local = Local();

        var error = Assert.Throws<ArgumentException>(() => _f.Servers.Update(local.Id,
            new ServerSaveInput { Hostname = "other", Port = 5480 }, null));

        Assert.Contains("config.json", error.Message);
    }

    // --- связь «организация ↔ сервер» многие-ко-многим (todo41 п. 11) ---

    [Fact]
    public void Server_And_Org_Are_Many_To_Many()
    {
        var local = Local();
        var remote = Remote();
        var first = _f.Orgs.Create("Первая", "one", null);
        var second = _f.Orgs.Create("Вторая", "two", null);

        _f.Servers.Attach(first.Id, local.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(second.Id, local.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(first.Id, remote.Id, OrgServerStatus.Active, "", null);

        // у сервера — список организаций, у организации — список серверов
        Assert.Equal(2, _f.Servers.OrgsOf(local.Id).Count);
        Assert.Equal(2, _f.Servers.ServersOf(first.Id).Count);
        Assert.Single(_f.Servers.ServersOf(second.Id));
    }

    [Fact]
    public void Codes_Start_At_S0_And_Are_Never_Reused()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var local = Local();
        var second = Remote("node2");

        Assert.Equal("S0", _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null).Code);
        Assert.Equal("S1", _f.Servers.Attach(org.Id, second.Id, OrgServerStatus.Active, "", null).Code);

        // сервер вывели из кластера — его код НЕ достаётся следующему: код входит в номера
        // задач (T-18-S1), и повторное использование сделало бы их неоднозначными (гл. 6)
        _f.Servers.Detach(org.Id, second.Id, null);
        var third = _f.Servers.Attach(org.Id, Remote("node3").Id, OrgServerStatus.Active, "", null);

        Assert.Equal("S2", third.Code);
    }

    [Fact]
    public void Codes_Are_Counted_Inside_One_Organization()
    {
        // нумерация серверов своя в каждой организации: один и тот же сервер бывает S0
        // в одной и S1 в другой
        var first = _f.Orgs.Create("Первая", "one", null);
        var second = _f.Orgs.Create("Вторая", "two", null);
        var local = Local();
        var remote = Remote();

        _f.Servers.Attach(first.Id, local.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(second.Id, remote.Id, OrgServerStatus.Active, "", null);

        Assert.Equal("S0", _f.Servers.Link(first.Id, local.Id)!.Code);
        Assert.Equal("S0", _f.Servers.Link(second.Id, remote.Id)!.Code);
    }

    // --- дирижёр: обязателен и ровно один (ТЗ гл. 6) ---

    [Fact]
    public void First_Server_Becomes_The_Conductor()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var local = Local();

        var link = _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null);
        var second = _f.Servers.Attach(org.Id, Remote().Id, OrgServerStatus.Active, "", null);

        Assert.True(link.IsConductor);
        Assert.False(second.IsConductor); // дирижёр ровно один
        Assert.Equal(local.Id, _f.Servers.Conductor(org.Id)!.ServerId);
        Assert.True(_f.Servers.IsLocalConductor(org.Id));
    }

    [Fact]
    public void Conductor_Moves_And_Stays_Single()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var local = Local();
        var remote = Remote();
        _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(org.Id, remote.Id, OrgServerStatus.Active, "", null);

        _f.Servers.SetConductor(org.Id, remote.Id, null);

        Assert.Single(_f.Servers.ServersOf(org.Id), s => s.IsConductor);
        Assert.Equal(remote.Id, _f.Servers.Conductor(org.Id)!.ServerId);
        // а вот теперь автозапуски и расписание на ЭТОМ сервере работать не должны
        Assert.False(_f.Servers.IsLocalConductor(org.Id));
    }

    [Fact]
    public void Conductor_Cannot_Be_Detached()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var local = Local();
        _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null);

        // дирижёр обязателен: сначала назначают другого, потом отвязывают прежнего
        Assert.Throws<ArgumentException>(() => _f.Servers.Detach(org.Id, local.Id, null));
    }

    [Fact]
    public void Organization_Without_Servers_Runs_Everything_Locally()
    {
        // одиночная установка, где серверов никто не заводил, обязана работать как раньше:
        // иначе она перестала бы запускать задачи автоматически (ТЗ гл. 6)
        var org = _f.Orgs.Create("Фирма", "acme", null);

        Assert.Null(_f.Servers.Conductor(org.Id));
        Assert.True(_f.Servers.IsLocalConductor(org.Id));
    }

    // --- двустороннее подключение: заявка и решение человека (todo41 пп. 7–10) ---

    [Fact]
    public void Join_Request_Waits_For_A_Decision_And_Has_No_Code_Until_Accepted()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        _f.Servers.Attach(org.Id, Local().Id, OrgServerStatus.Active, "", null);
        var joining = Remote();

        var request = _f.Servers.Attach(org.Id, joining.Id, OrgServerStatus.Pending,
            "mike@example.com", null);

        Assert.Equal(OrgServerStatus.Pending, request.Status);
        Assert.False(request.IsConductor);       // дирижёром неподтверждённый сервер не станет
        Assert.Equal("mike@example.com", request.RequestedBy);
        // заявка видна отдельным списком — в UI она идёт внизу списка серверов (п. 8)
        Assert.Single(_f.Servers.Requests());
    }

    [Fact]
    public void Accepting_A_Request_Connects_The_Server()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        _f.Servers.Attach(org.Id, Local().Id, OrgServerStatus.Active, "", null);
        var request = _f.Servers.Attach(org.Id, Remote().Id, OrgServerStatus.Pending, "mike@x", null);

        var accepted = _f.Servers.AcceptRequest(request.Id, null);

        Assert.Equal(OrgServerStatus.Active, accepted.Status);
        Assert.Equal("S1", accepted.Code);
        Assert.Empty(_f.Servers.Requests());
        Assert.Contains(_f.ServerEvents.Query(limit: 100),
            e => e.EventType == EventTypes.ServerJoinAccepted);
    }

    [Fact]
    public void Accepting_A_Request_Activates_The_Server_Record()
    {
        // до подтверждения запись сервера неактивна и его токен никого не опознаёт;
        // «ПРИНЯТЬ» — это в том числе и разрешение ему обращаться к нам (ТЗ гл. 12)
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var joining = _f.Servers.Create(new ServerSaveInput
        {
            Hostname = "node2",
            Port = 5480,
            IsActive = false,
        }, null);
        var token = _f.Servers.IssueToken(joining.Id);
        var request = _f.Servers.Attach(org.Id, joining.Id, OrgServerStatus.Pending, "mike@x", null);

        Assert.Null(_f.Servers.ByToken(token));   // пока не подтвердили — не узнаём

        _f.Servers.AcceptRequest(request.Id, null);

        Assert.Equal(joining.Id, _f.Servers.ByToken(token)?.Id);
        Assert.True(_f.Servers.Get(joining.Id)!.IsActive);
    }

    [Fact]
    public void Deferring_A_Request_Keeps_It_In_The_List()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var request = _f.Servers.Attach(org.Id, Remote().Id, OrgServerStatus.Pending, "mike@x", null);

        var deferred = _f.Servers.DeferRequest(request.Id, null);

        Assert.Equal(OrgServerStatus.Deferred, deferred.Status);
        // «ОТЛОЖИТЬ» — решение не принято: запись остаётся в списке серверов (п. 10)
        Assert.Single(_f.Servers.Requests());
        Assert.Contains(_f.ServerEvents.Query(limit: 100),
            e => e.EventType == EventTypes.ServerJoinDeferred);
    }

    [Fact]
    public void Rejecting_A_Request_Removes_It_But_Leaves_The_Journal_Entry()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var request = _f.Servers.Attach(org.Id, Remote().Id, OrgServerStatus.Pending, "mike@x", null);

        _f.Servers.RejectRequest(request.Id, null);

        Assert.Empty(_f.Servers.Requests());
        Assert.Empty(_f.Servers.ServersOf(org.Id));
        // в журнале запись остаётся — иначе отказ невозможно было бы разобрать потом (п. 9)
        Assert.Contains(_f.ServerEvents.Query(limit: 100),
            e => e.EventType == EventTypes.ServerJoinRejected);
    }

    [Fact]
    public void Pending_Servers_Are_Listed_After_Connected_Ones()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null);
        _f.Servers.Attach(org.Id, Remote("node2").Id, OrgServerStatus.Pending, "mike@x", null);
        _f.Servers.Attach(org.Id, Local().Id, OrgServerStatus.Active, "", null);

        var links = _f.Servers.ServersOf(org.Id);

        Assert.Equal(OrgServerStatus.Active, links[0].Status);
        Assert.Equal(OrgServerStatus.Pending, links[^1].Status);
    }

    // --- токены доступа сервер-сервер (ТЗ гл. 12) ---

    [Fact]
    public void Token_Is_Issued_Once_And_Identifies_The_Server()
    {
        var remote = Remote();
        _f.Servers.Update(remote.Id, new ServerSaveInput
        {
            Name = remote.Name,
            Protocol = remote.Protocol,
            Hostname = remote.Hostname,
            Port = remote.Port,
            BasePath = remote.BasePath,
            IsActive = true,
        }, null);

        var token = _f.Servers.IssueToken(remote.Id);

        Assert.NotEmpty(token);
        Assert.Equal(token, _f.Servers.IssueToken(remote.Id)); // повторно тот же
        Assert.Equal(remote.Id, _f.Servers.ByToken(token)?.Id);
        Assert.Null(_f.Servers.ByToken("чужой-токен"));
        Assert.Null(_f.Servers.ByToken(""));    // пустой токен никого не опознаёт
    }

    [Fact]
    public void Inactive_Server_Is_Not_Recognized_By_Its_Token()
    {
        // подключение сервера начинается с НЕАКТИВНОЙ записи: пока человек не подтвердил
        // заявку, чужой сервер в кластере не работает
        var remote = Remote();
        var token = _f.Servers.IssueToken(remote.Id);

        Assert.Equal(remote.Id, _f.Servers.ByToken(token)?.Id);

        _f.Servers.Update(remote.Id, new ServerSaveInput
        {
            Name = remote.Name,
            Protocol = remote.Protocol,
            Hostname = remote.Hostname,
            Port = remote.Port,
            BasePath = remote.BasePath,
            IsActive = false,
        }, null);

        Assert.Null(_f.Servers.ByToken(token));
    }

    [Fact]
    public void Adopting_The_Real_Key_Keeps_Links_And_Tokens()
    {
        // сервер представился своим внутренним ключом при первом обмене — переносим на него
        // связи и токены: узнают друг друга серверы именно по ключу, не по адресу (п. 6)
        var org = _f.Orgs.Create("Фирма", "acme", null);
        var guessed = Remote();
        _f.Servers.Attach(org.Id, guessed.Id, OrgServerStatus.Active, "", null);
        var token = _f.Servers.IssueToken(guessed.Id);

        var realId = _f.Servers.AdoptId(guessed.Id, "00000000-real-key");

        Assert.Equal("00000000-real-key", realId);
        Assert.Null(_f.Servers.Get(guessed.Id));
        Assert.Equal(realId, _f.Servers.ByToken(token)?.Id);
        Assert.Equal(realId, _f.Servers.ServersOf(org.Id).Single().ServerId);
    }

    // --- дирижёр останавливает автозапуски на прочих серверах (ТЗ гл. 6) ---

    /// <summary>Расписание, срабатывание которого уже просрочено: отсчёт идёт от момента
    /// создания расписания, поэтому создание «состаривается» прямой правкой БД.</summary>
    private void OverdueSchedule()
    {
        var template = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var schedule = _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddMinutes(30),
        }, null);
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE schedules SET created_at=@c, start_at=@s WHERE id=@id",
            ("@c", Sql.ToDb(DateTime.UtcNow.AddHours(-2))),
            ("@s", Sql.ToDb(DateTime.UtcNow.AddHours(-1))),
            ("@id", schedule.Id));
    }

    [Fact]
    public void Schedule_Runner_Does_Nothing_When_The_Server_Is_Not_The_Conductor()
    {
        // расписание лежит в реплике у всех серверов организации: без отбора «своих» одно
        // срабатывание породило бы задачу на каждом сервере кластера. На этапе 42 отбор
        // делается по СЕРВЕРУ расписания, а не выключением расписаний на не-дирижёре целиком
        OverdueSchedule(); // без сервера — такое расписание ведёт дирижёр

        var other = new AI2P.Storage.ServerScope(() => "unid-S1", () => "S1", () => false);
        var schedules = new ScheduleService(_f.Db, _f.Events, other);
        var runner = new AI2P.Connectors.ScheduleRunner(_f.Tasks, schedules, _f.Picker, _f.Events);
        runner.Start();

        // просроченных не собрано: расписание без сервера — не наше
        Assert.Empty(runner.Overdue());
        runner.Dispose();
    }

    [Fact]
    public void Schedule_Runner_Collects_Overdue_On_The_Conductor()
    {
        OverdueSchedule();

        var runner = new AI2P.Connectors.ScheduleRunner(_f.Tasks, _f.Schedules, _f.Picker, _f.Events);
        runner.Start();

        Assert.Single(runner.Overdue());
        runner.Dispose();
    }

    // --- вход администратора сервера через внутренний вызов UI (ТЗ гл. 12) ---

    /// <summary>Запрос «как из UI»: токен процесса, аккаунт и петлевой адрес.</summary>
    private static Microsoft.AspNetCore.Http.DefaultHttpContext InternalRequest(
        string token, string admin, bool loopback = true)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Headers[AI2P.Core.Api.Ai2pHeaders.InternalToken] = token;
        ctx.Request.Headers[AI2P.Core.Api.Ai2pHeaders.InternalAccount] = "account-1";
        if (admin.Length > 0)
        {
            ctx.Request.Headers[AI2P.Core.Api.Ai2pHeaders.ServerAdmin] = admin;
        }
        ctx.Connection.RemoteIpAddress = loopback
            ? System.Net.IPAddress.Loopback
            : System.Net.IPAddress.Parse("192.168.1.50");
        ctx.Connection.LocalIpAddress = System.Net.IPAddress.Parse("192.168.1.10");
        return ctx;
    }

    [Fact]
    public void Ui_Passes_The_Server_Admin_Login_Through_The_Internal_Call()
    {
        // UI ходит в собственное API по петле, и cookie администратора сервера туда не
        // доходит: без передачи признака заголовком настройки самого сервера оставались бы
        // недоступными из UI даже после входа администратором (найдено живой проверкой)
        var token = AI2P.Server.Api.Ai2pAuth.InternalToken;

        Assert.True(AI2P.Server.Api.Ai2pAuth.InternalServerAdmin(InternalRequest(token, "1")));
        Assert.Equal("account-1", AI2P.Server.Api.Ai2pAuth.InternalAccountId(InternalRequest(token, "1")));
    }

    [Fact]
    public void Server_Admin_Header_Is_Trusted_Only_From_Our_Own_Process_And_Loopback()
    {
        var token = AI2P.Server.Api.Ai2pAuth.InternalToken;

        // без заголовка — обычный пользователь
        Assert.False(AI2P.Server.Api.Ai2pAuth.InternalServerAdmin(InternalRequest(token, "")));
        // чужой токен процесса — подделать признак снаружи нельзя
        Assert.False(AI2P.Server.Api.Ai2pAuth.InternalServerAdmin(InternalRequest("подделка", "1")));
        // не с этого компьютера — тоже нельзя: настройки сервера правит только его хозяин
        Assert.False(AI2P.Server.Api.Ai2pAuth.InternalServerAdmin(
            InternalRequest(token, "1", loopback: false)));
        Assert.Null(AI2P.Server.Api.Ai2pAuth.InternalAccountId(
            InternalRequest(token, "1", loopback: false)));
    }

    [Fact]
    public void Writing_Server_Settings_Requires_The_Server_Admin_Login()
    {
        // читать настройки локального сервера может любой вошедший (в форме они видны),
        // а писать — только администратор сервера (ТЗ гл. 12, todo41 п. 3). Роль по разделу
        // с T-174 — admin, а не owner: свой компьютер в кластере ведёт администратор, и
        // ЗАПИСЬ всё равно упирается в отдельный локальный логин (ApiPermissions.Check)
        Assert.Equal(SystemRole.Admin,
            AI2P.Server.Api.ApiPermissions.Required("GET", "/api/servers/local"));
        Assert.Equal(SystemRole.Admin,
            AI2P.Server.Api.ApiPermissions.Required("PUT", "/api/servers/local"));
    }

    [Fact]
    public void Directory_Settings_Of_The_Local_Server_Must_Be_Full_Paths()
    {
        // ПРАВИЛО ПЕРЕПИСАНО В T-291: относительный путь стал законным и даже умолчанием
        // («./models» — каталог рядом с установкой), потому что у вопроса «относительно
        // чего» появился один ответ на всю систему. Отвергается теперь другое: уход ВЫШЕ
        // этого каталога, недопустимые символы и абсолютный путь чужой платформы (T-164).
        //
        // Прежний повод отвергать любой относительный путь — мусор из автозаполнения
        // браузера (в поле каталога попадал КОД ОРГАНИЗАЦИИ, рядом стоит поле пароля) —
        // закрыт в форме атрибутом autocomplete="off" у всех трёх полей.
        var settings = new ServerSettingsDto
        {
            Port = 5480,
            Protocol = "http",
            Hostname = "localhost",
            BindAddress = "0.0.0.0",
            ModelsRepo = "../../mmfgrp",
        };

        var error = Assert.Throws<ArgumentException>(
            () => AI2P.Server.Api.ApiEndpoints.ValidateLocalServer(settings));
        Assert.Contains("mmfgrp", error.Message);

        // пустое значение — это «умолчание», оно допустимо; полный путь — тем более,
        // и относительный внутри каталога установки тоже
        settings.ModelsRepo = OperatingSystem.IsWindows() ? @"C:\ai" : "/opt/ai";
        settings.DistDir = "";
        settings.PackagesDir = "";
        AI2P.Server.Api.ApiEndpoints.ValidateLocalServer(settings);
        settings.ModelsRepo = "./models";
        AI2P.Server.Api.ApiEndpoints.ValidateLocalServer(settings);
    }

    [Fact]
    public void Local_Server_Address_Is_Validated_Before_Being_Written_To_Config()
    {
        var settings = new ServerSettingsDto { Port = 0, Hostname = "localhost" };
        Assert.Throws<ArgumentException>(() => AI2P.Server.Api.ApiEndpoints.ValidateLocalServer(settings));

        settings.Port = 5480;
        settings.Hostname = "  ";
        Assert.Throws<ArgumentException>(() => AI2P.Server.Api.ApiEndpoints.ValidateLocalServer(settings));
    }

    // --- схема серверной БД (ТЗ п. 6.4.1) ---

    [Fact]
    public void Cluster_Tables_Live_Only_In_The_Server_Database()
    {
        // серверы и организации — вне организаций: иначе кластер описывал бы сам себя
        // по-разному в каждой БД (ТЗ п. 6.4.1)
        Assert.Throws<ArgumentException>(() => new ServerService(_f.Db, _f.Events));
    }

    [Fact]
    public void Old_Org_Servers_Table_Is_Rebuilt_By_Migration()
    {
        // база этапа 40: сервер принадлежал ровно одной организации, адрес лежал в связи.
        // Миграция v16 → v17 пересоздаёт таблицу, а связь с локальным сервером при старте
        // заводит заново реестр организаций (ТЗ v1.48)
        var dir = Path.Combine(_f.Dir, "old");
        Directory.CreateDirectory(dir);
        var db = new Database(dir, "server.db", DatabaseKind.Server);
        db.Init();
        using (var conn = db.Open())
        {
            Sql.Exec(conn, null, "DROP TABLE org_servers");
            Sql.Exec(conn, null, """
                CREATE TABLE org_servers (
                  id TEXT PRIMARY KEY, org_id TEXT NOT NULL, code TEXT NOT NULL DEFAULT 'S0',
                  url TEXT NOT NULL DEFAULT '', is_conductor INTEGER NOT NULL DEFAULT 0,
                  is_local INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL, deleted_at TEXT)
                """);
        }

        var migrated = new Database(dir, "server.db", DatabaseKind.Server);
        migrated.Init();

        using var check = migrated.Open();
        var columns = Sql.Query(check, null, "PRAGMA table_info(org_servers)", r => r.S("name"));
        Assert.Contains("server_id", columns);
        Assert.Contains("status", columns);
        Assert.DoesNotContain("url", columns);
        // версия схемы растёт вместе с этапами: v17 — этап 41, v18 — этап 42 (владение),
        // v19 — этап 43 (репликация БД), v20 — этап 44 (файлы), v21 — этап 45 (ключи API),
        // v22 — T-139 (заявитель у заявки на подключение),
        // v23 — T-160 (очередь повтора репликации),
        // v24 — T-50-S0 (второй, внешний адрес сервера)
        Assert.Equal("24", Sql.Scalar<string>(check, null,
            "SELECT value FROM meta WHERE key='schema_version'"));
    }
}
