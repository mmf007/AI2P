using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-21-S1 «Смена дирижёра».
///
/// Дирижёр обязателен и ровно один (ТЗ гл. 6): он выдаёт коды серверам, ведёт справочники
/// и с ним реплицируются остальные. Поэтому передача дирижёрства — не переставленный флажок,
/// а ОТДЕЛЬНАЯ ДВУСТОРОННЯЯ операция, устроенная как первое подключение сервера:
///
/// 1) подать заявку вправе только ДВЕ стороны — назначаемый сервер и сегодняшний дирижёр;
///    третий сервер кластера в чужую передачу не вмешивается, а сервер с петлевым адресом
///    дирижёром быть не может вовсе (по «localhost» остальные попадут к себе);
/// 2) заявка едет второй стороне обычной репликацией (таблица в БД организации), решение
///    возвращается тем же путём; подавший вправе снять свою заявку;
/// 3) АВАРИЙНЫЙ СЛУЧАЙ: у заявки, поданной САМИМ назначаемым сервером, идёт обратный отсчёт
///    12 часов — по его истечении она принимается в одностороннем порядке, иначе организация
///    с физически потерянным дирижёром осталась бы без дирижёра навсегда;
/// 4) о смене дирижёра узнают ВСЕ: признак едет журналом изменений, а тем, с кем у нового
///    дирижёра нет пары токенов, он объявляет о себе сам — объявление подписано ключом
///    организации (<see cref="ConductorProof"/>).
///
/// Оба «сервера» здесь работают с ОДНОЙ базой (приём Todo42Tests/T196Tests): сама репликация
/// проверяется живым прогоном двух настоящих серверов, а здесь — правила и записи как таковые.
/// </summary>
public sealed class T21S1Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string ConductorId = "unid-S0";
    private const string TargetId = "unid-S1";
    private const string ThirdId = "unid-S2";

    private static ServerScope Scope(string me) => new(
        () => me, () => Code(me), () => me == ConductorId,
        codeOf: Code, nameOf: Name);

    private static string Code(string id) => id switch
    {
        ConductorId => "S0",
        TargetId => "S1",
        ThirdId => "S2",
        _ => "",
    };

    private static string Name(string id) => id switch
    {
        ConductorId => "главный",
        TargetId => "ноутбук",
        ThirdId => "третий",
        _ => "",
    };

    private ConductorRequestService Requests(string me) => new(_f.Db, _f.Events, Scope(me));

    /// <summary>Запись сервера с настоящим (не петлевым) адресом: только такой годится
    /// в дирижёры.</summary>
    private static ServerNode Node(string id, string hostname = "notebook.local") => new()
    {
        Id = id,
        Name = Name(id),
        Hostname = hostname,
        Port = 5480,
        BasePath = "/ai2p",
    };

    private static OrgServer Link(string serverId, bool conductor,
        string status = OrgServerStatus.Active) => new()
    {
        ServerId = serverId,
        Code = Code(serverId),
        IsConductor = conductor,
        Status = status,
    };

    // ---------- 1. кто вправе подать заявку ----------

    [Fact]
    public void Both_Sides_May_File_The_Request_But_A_Third_Server_May_Not()
    {
        var target = Node(TargetId);
        var link = Link(TargetId, conductor: false);

        // назначаемый сервер просит дирижёрства себе
        Assert.Null(ConductorRules.ProblemWithRequest(target, link, TargetId, ConductorId));
        // дирижёр передаёт дирижёрство ему же
        Assert.Null(ConductorRules.ProblemWithRequest(target, link, ConductorId, ConductorId));
        // третий сервер кластера в чужую передачу не вмешивается
        var problem = ConductorRules.ProblemWithRequest(target, link, ThirdId, ConductorId);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void A_Server_With_A_Loopback_Address_Cannot_Become_The_Conductor()
    {
        var link = Link(TargetId, conductor: false);

        // «localhost» и «127.0.0.1» у каждого компьютера означают его самого: остальные
        // серверы кластера по такому адресу позвонят себе (T-141)
        foreach (var loopback in new[] { "localhost", "127.0.0.1", "::1", "" })
        {
            var problem = ConductorRules.ProblemWithRequest(Node(TargetId, loopback), link,
                TargetId, ConductorId);
            Assert.False(string.IsNullOrEmpty(problem));
        }
        // настоящее имя хоста — можно
        Assert.Null(ConductorRules.ProblemWithRequest(Node(TargetId, "192.168.1.10"), link,
            TargetId, ConductorId));
    }

    [Fact]
    public void The_Conductor_And_A_Stranger_Are_Not_Candidates()
    {
        // сервер уже дирижёр — назначать его незачем
        Assert.NotNull(ConductorRules.ProblemWithRequest(Node(TargetId),
            Link(TargetId, conductor: true), ConductorId, ConductorId));
        // сервер не подключён к организации (заявка на подключение ещё ждёт решения)
        Assert.NotNull(ConductorRules.ProblemWithRequest(Node(TargetId),
            Link(TargetId, conductor: false, OrgServerStatus.Pending), ConductorId, ConductorId));
        // связи нет вовсе
        Assert.NotNull(ConductorRules.ProblemWithRequest(Node(TargetId), null,
            ConductorId, ConductorId));
    }

    // ---------- 2. заявка и решение ----------

    [Fact]
    public void The_Request_Is_Filed_And_Decided_By_The_Other_Side()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "пора", "Вася", null);

        Assert.Equal(ConductorRequestStatus.Pending, request.Status);
        Assert.True(request.ByTarget);            // подал сам назначаемый сервер
        Assert.Equal(TargetId, request.ServerId); // владелец строки — её автор (ТЗ гл. 6)
        Assert.Equal("S1", request.TargetServerCode);
        Assert.Equal("S0", request.FromServerCode);

        // вторая сторона — дирижёр: он видит заявку у себя (она приехала бы репликацией)
        var open = Requests(ConductorId).Open();
        Assert.NotNull(open);
        Assert.Equal(request.Id, open!.Id);

        var decided = Requests(ConductorId).Decide(open.Id, ConductorRequestStatus.Accepted,
            "Пётр", null);
        Assert.Equal(ConductorRequestStatus.Accepted, decided.Status);
        Assert.False(decided.Unilateral);
        Assert.Null(Requests(ConductorId).Open());   // ждущих больше нет
    }

    [Fact]
    public void A_Second_Request_Is_Refused_While_The_First_Is_Open()
    {
        Requests(TargetId).Add(TargetId, ConductorId, "", "", null);

        // две одновременные передачи дирижёрства — это гонка за один признак
        Assert.Throws<ArgumentException>(() =>
            Requests(ConductorId).Add(TargetId, ConductorId, "", "", null));

        // заявку сняли — можно подавать заново
        var open = Requests(TargetId).Open()!;
        Requests(TargetId).Decide(open.Id, ConductorRequestStatus.Withdrawn, "Вася", null);
        Assert.Null(Requests(TargetId).Open());
        var again = Requests(ConductorId).Add(TargetId, ConductorId, "", "", null);
        Assert.Equal(ConductorRequestStatus.Pending, again.Status);
    }

    [Fact]
    public void A_Decided_Request_Is_Not_Decided_Twice()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        Requests(ConductorId).Decide(request.Id, ConductorRequestStatus.Rejected, "Пётр", null);

        Assert.Throws<ArgumentException>(() =>
            Requests(ConductorId).Decide(request.Id, ConductorRequestStatus.Accepted, "Пётр", null));
    }

    [Fact]
    public void Sides_Are_Read_From_The_Initiator()
    {
        var byTarget = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        Assert.True(byTarget.ByTarget);
        Requests(TargetId).Decide(byTarget.Id, ConductorRequestStatus.Withdrawn, "", null);

        // ту же заявку подал ДИРИЖЁР — обратного отсчёта у неё нет вовсе
        var byConductor = Requests(ConductorId).Add(TargetId, ConductorId, "", "", null);
        Assert.False(byConductor.ByTarget);
        Assert.Equal(ConductorId, byConductor.InitiatorServerId);
    }

    // ---------- 3. аварийный случай: 12 часов и одностороннее принятие ----------

    [Fact]
    public void Unilateral_Acceptance_Waits_Twelve_Hours_And_Only_On_The_Assigned_Server()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        var now = request.CreatedAt;

        // сразу после подачи — нельзя: дирижёр ещё может ответить
        Assert.False(ConductorRequestService.CanTakeUnilaterally(request, TargetId, now));
        Assert.False(ConductorRequestService.CanTakeUnilaterally(request, TargetId,
            now + TimeSpan.FromHours(11.9)));
        // отсчёт истёк — назначаемый сервер вправе принять свою заявку сам
        Assert.True(ConductorRequestService.CanTakeUnilaterally(request, TargetId,
            now + ConductorRequestService.UnilateralAfter));
        // но только ОН: дирижёр «в одностороннем порядке» не принимает — он принимает обычно
        Assert.False(ConductorRequestService.CanTakeUnilaterally(request, ConductorId,
            now + TimeSpan.FromDays(1)));
        Assert.False(ConductorRequestService.CanTakeUnilaterally(request, ThirdId,
            now + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void A_Request_Filed_By_The_Conductor_Is_Never_Accepted_Unilaterally()
    {
        // заявку подал дирижёр — вторая сторона (он сам) заведомо жива, ждать её нечего
        var request = Requests(ConductorId).Add(TargetId, ConductorId, "", "", null);

        Assert.False(ConductorRequestService.CanTakeUnilaterally(request, TargetId,
            request.CreatedAt + TimeSpan.FromDays(30)));
        Assert.Equal(TimeSpan.Zero,
            ConductorRequestService.Countdown(request, request.CreatedAt));
    }

    [Fact]
    public void The_Countdown_Counts_Down()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        var now = request.CreatedAt;

        Assert.Equal(ConductorRequestService.UnilateralAfter,
            ConductorRequestService.Countdown(request, now));
        Assert.Equal(TimeSpan.FromHours(2),
            ConductorRequestService.Countdown(request, now + TimeSpan.FromHours(10)));
        // после срока остаток нулевой, а не отрицательный
        Assert.Equal(TimeSpan.Zero,
            ConductorRequestService.Countdown(request, now + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void The_Unilateral_Acceptance_Is_Marked_As_Such()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        // «состарим» заявку: отсчёт считается от времени подачи
        Sql.Exec(_f.Db.Open(), null, "UPDATE conductor_requests SET created_at=@at WHERE id=@id",
            ("@at", Sql.ToDb(DateTime.UtcNow - TimeSpan.FromHours(13))), ("@id", request.Id));

        var aged = Requests(TargetId).Get(request.Id)!;
        Assert.True(ConductorRequestService.CanTakeUnilaterally(aged, TargetId, DateTime.UtcNow));

        var taken = Requests(TargetId).Decide(aged.Id, ConductorRequestStatus.Accepted,
            "Вася", null, unilateral: true);
        Assert.Equal(ConductorRequestStatus.Accepted, taken.Status);
        // согласия второй стороны не было, и это должно быть видно и в записи, и в журнале
        Assert.True(taken.Unilateral);
        Assert.NotEmpty(_f.Events.Query(eventType: AI2P.Core.Events.EventTypes.ConductorTakenOver));
    }

    // ---------- 4. смена дирижёра доходит до всех ----------

    [Fact]
    public void Accepting_The_Request_Moves_The_Conductor_Flag_To_Exactly_One_Server()
    {
        var org = _f.CreateOrgWithLocalServer("Кластер", "cluster");
        var local = _f.Servers.Local()!;
        var other = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ноутбук",
            Hostname = "notebook.local",
            Port = 5480,
            BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org.Id, other.Id, OrgServerStatus.Active, "", null);

        Assert.True(_f.Servers.IsLocalConductor(org.Id));

        _f.Servers.SetConductor(org.Id, other.Id, null);

        // дирижёр обязателен и ровно один — прежний его теряет, новый получает
        var links = _f.Servers.ServersOf(org.Id);
        Assert.Single(links, l => l.IsConductor);
        Assert.Equal(other.Id, _f.Servers.Conductor(org.Id)!.ServerId);
        Assert.False(_f.Servers.IsLocalConductor(org.Id));
        Assert.False(links.First(l => l.ServerId == local.Id).IsConductor);
    }

    [Fact]
    public void The_Request_Travels_By_Replication_Like_Any_Other_Row()
    {
        // отдельной таблицей, а не видом заявки на запуск (наука T-263): сервер прежней
        // версии принял бы незнакомый ВИД run_requests за просьбу ЗАПУСТИТЬ задачу,
        // а строку незнакомой ТАБЛИЦЫ приёмник журнала пропускает молча
        Assert.Contains("conductor_requests", ChangeLog.OrgTables);
        Assert.DoesNotContain("conductor_requests", ChangeLog.ServerTables);

        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        using var conn = _f.Db.Open();
        var logged = Sql.Query(conn, null,
            "SELECT pk FROM changes WHERE tbl='conductor_requests'", r => r.S("pk"));
        Assert.Contains(request.Id, logged);
    }

    [Fact]
    public void The_Announcement_Is_Proved_By_The_Organization_Key()
    {
        var orgKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(orgKey);
        var stamp = DateTime.UtcNow.ToString("o");
        var payload = ConductorProof.Payload("org-1", TargetId, "req-1", "token-1", stamp);
        var signature = ConductorProof.Sign(orgKey, payload);

        Assert.True(ConductorProof.Verify(orgKey, payload, signature, stamp, DateTime.UtcNow));

        // ключ организации не тот (сервер чужой) — не верим
        var stranger = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(stranger);
        Assert.False(ConductorProof.Verify(stranger, payload, signature, stamp, DateTime.UtcNow));
        // подменили того, кто объявляет себя дирижёром
        Assert.False(ConductorProof.Verify(orgKey,
            ConductorProof.Payload("org-1", ThirdId, "req-1", "token-1", stamp), signature, stamp,
            DateTime.UtcNow));
        // ключа нет вовсе, подпись пустая, подпись не base64
        Assert.False(ConductorProof.Verify(null, payload, signature, stamp, DateTime.UtcNow));
        Assert.False(ConductorProof.Verify(orgKey, payload, "", stamp, DateTime.UtcNow));
        Assert.False(ConductorProof.Verify(orgKey, payload, "не base64!", stamp, DateTime.UtcNow));
    }

    [Fact]
    public void A_Stale_Announcement_Is_Not_Replayed()
    {
        var orgKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(orgKey);
        var stamp = (DateTime.UtcNow - TimeSpan.FromDays(30)).ToString("o");
        var payload = ConductorProof.Payload("org-1", TargetId, "req-1", "token-1", stamp);
        var signature = ConductorProof.Sign(orgKey, payload);

        // подпись верна, но объявление месячной давности: перехваченное нельзя проиграть заново
        Assert.False(ConductorProof.Verify(orgKey, payload, signature, stamp, DateTime.UtcNow));
        // а свежее той же формы — принимается
        var fresh = DateTime.UtcNow.ToString("o");
        var freshPayload = ConductorProof.Payload("org-1", TargetId, "req-1", "token-1", fresh);
        Assert.True(ConductorProof.Verify(orgKey, freshPayload,
            ConductorProof.Sign(orgKey, freshPayload), fresh, DateTime.UtcNow));
    }

    // ---------- 5. уборка ----------

    [Fact]
    public void The_Author_Removes_Its_Own_Requests_When_They_Are_Old_Enough()
    {
        var request = Requests(TargetId).Add(TargetId, ConductorId, "", "", null);
        Requests(ConductorId).Decide(request.Id, ConductorRequestStatus.Rejected, "", null);

        // свежую решённую заявку не трогаем: человек должен увидеть, чем кончилось.
        // Показывается она с T-293 только по флажку «показать все заявки» — отсюда all: true
        Assert.Equal(0, Requests(TargetId).Cleanup());
        Assert.Single(Requests(TargetId).List(all: true));

        Sql.Exec(_f.Db.Open(), null, "UPDATE conductor_requests SET updated_at=@at WHERE id=@id",
            ("@at", Sql.ToDb(DateTime.UtcNow - TimeSpan.FromDays(30))), ("@id", request.Id));
        // убирает её АВТОР, а не вторая сторона (ТЗ гл. 6: строку правит её владелец)
        Assert.Equal(0, Requests(ConductorId).Cleanup());
        Assert.Equal(1, Requests(TargetId).Cleanup());
        Assert.Empty(Requests(TargetId).List(all: true));
    }
}
