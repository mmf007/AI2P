using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo43 / этап 43 (ТЗ v1.50): РЕПЛИКАЦИЯ БАЗ ДАННЫХ. Журнал изменений строк, курсоры,
/// pull-протокол, первичная репликация и LWW.
///
/// Тесты работают с ДВУМЯ настоящими базами (два «сервера») и переносят между ними изменения
/// ровно так, как это делает сеанс репликации: читают журнал источника после курсора и
/// применяют пачку у приёмника. Сеть здесь ни при чём — проверяется механизм слияния.
/// </summary>
public sealed class Todo43Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo43-" + Guid.NewGuid().ToString("N"));

    /// <summary>«Дирижёр» (S0) и «ноутбук» (S1) — две независимые базы организации.</summary>
    private readonly Database _a;
    private readonly Database _b;

    private const string NodeA = "unid-S0";
    private const string NodeB = "unid-S1";

    public Todo43Tests()
    {
        _a = new Database(Path.Combine(_dir, "a"), "ai2p.db");
        _a.Init(NodeA);
        _b = new Database(Path.Combine(_dir, "b"), "ai2p.db");
        _b.Init(NodeB);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // файлы БД могут быть ещё заняты — временный каталог уберёт система
        }
    }

    private TaskService Tasks(Database db, string serverId, string code, bool conductor) =>
        new(db, new EventStore(db), new FileStore(db.DataDir),
            new ServerScope(() => serverId, () => code, () => conductor));

    /// <summary>Один такт репликации: перенести изменения из источника в приёмник.
    /// Ровно то, что делает сеанс, — «отдай мне изменения после курсора» и применение.</summary>
    private static (ApplyResult Result, long Cursor) Sync(Database from, Database to, long cursor,
        string toNode, int limit = 500)
    {
        List<RowChange> items;
        using (var conn = from.Open())
        {
            items = ChangeLog.Read(conn, cursor, limit, toNode);
        }
        if (items.Count == 0)
        {
            return (new ApplyResult(), cursor);
        }
        var result = ChangeLog.Apply(to, items);
        return (result, items[^1].Seq);
    }

    private static List<RowChange> Read(Database db, long after, string exceptNode)
    {
        using var conn = db.Open();
        return ChangeLog.Read(conn, after, 500, exceptNode);
    }

    // --- журнал изменений: та самая «одна точка записи» ---

    [Fact]
    public void Every_Write_Lands_In_The_Change_Log()
    {
        var task = Tasks(_a, NodeA, "S0", conductor: true).Create(new TaskItem { Title = "Задача" }, "", "", null);

        var changes = Read(_a, 0, "нет-такого-узла");
        var mine = changes.Where(c => c.Table == "tasks" && c.Pk == task.Id).ToList();

        Assert.Single(mine);
        Assert.Equal(ChangeOps.Upsert, mine[0].Op);
        // автор изменения — наш узел; им же гасится эхо
        Assert.Equal(NodeA, mine[0].NodeId);
        // в payload вся строка целиком, а не только изменённые поля
        var values = ChangeLog.Parse(mine[0].PayloadJson);
        Assert.Equal("Задача", values["title"]);
        Assert.Equal(task.DisplayId, values["display_id"]);
    }

    [Fact]
    public void Update_And_Soft_Delete_Are_Ordinary_Changes()
    {
        var service = Tasks(_a, NodeA, "S0", conductor: true);
        var task = service.Create(new TaskItem { Title = "Первая" }, "", "", null);
        task.Title = "Вторая";
        service.Update(task, "", "", null);

        var changes = Read(_a, 0, "нет-такого-узла").Where(c => c.Pk == task.Id).ToList();

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(ChangeOps.Upsert, c.Op));
        Assert.Equal("Вторая", ChangeLog.Parse(changes[^1].PayloadJson)["title"]);
    }

    [Fact]
    public void Physical_Delete_Travels_As_A_Delete_With_The_Key_Only()
    {
        // связки (исполнители задачи, навыки, блокировки) удаляются физически
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, "INSERT INTO counters(prefix, next) VALUES ('X', 5)");
            Sql.Exec(conn, null, "DELETE FROM counters WHERE prefix='X'");
        }

        var changes = Read(_a, 0, "нет-такого-узла").Where(c => c.Table == "counters" && c.Pk == "X").ToList();

        Assert.Equal(2, changes.Count);
        Assert.Equal(ChangeOps.Delete, changes[^1].Op);
        Assert.Equal("X", ChangeLog.Parse(changes[^1].PayloadJson)["prefix"]);
    }

    // --- перенос между серверами ---

    [Fact]
    public void Task_Created_On_One_Server_Appears_On_The_Other()
    {
        var task = Tasks(_a, NodeA, "S0", conductor: true)
            .Create(new TaskItem { Title = "Собрать релиз" }, "", "", null);

        var (result, cursor) = Sync(_a, _b, 0, NodeB);

        Assert.True(result.Applied > 0);
        Assert.True(cursor > 0);
        var copy = Tasks(_b, NodeB, "S1", conductor: false).Get(task.Id);
        Assert.NotNull(copy);
        Assert.Equal("Собрать релиз", copy!.Title);
        // номер задачи при переносе не меняется — это идентификатор и имя каталога
        Assert.Equal(task.DisplayId, copy.DisplayId);
        // владение переезжает вместе со строкой: у себя эта задача редактируется,
        // на втором сервере — только для чтения (ТЗ гл. 6)
        Assert.Equal(task.ServerId, copy.ServerId);
    }

    [Fact]
    public void Replication_Is_Idempotent_And_Does_Not_Echo_Back()
    {
        Tasks(_a, NodeA, "S0", conductor: true).Create(new TaskItem { Title = "Задача" }, "", "", null);
        var (_, cursor) = Sync(_a, _b, 0, NodeB);

        // 1) повторный такт с тем же курсором ничего не меняет
        var again = Sync(_a, _b, cursor, NodeB);
        Assert.Equal(0, again.Result.Applied);

        // 2) обратный такт тоже пуст: у B эти строки записаны под авторством A,
        // и обратно автору его собственные изменения не отдаются — эхо погашено
        Assert.Empty(Read(_b, 0, NodeA));
    }

    [Fact]
    public void Applied_Change_Keeps_The_Original_Author_And_Clock()
    {
        var task = Tasks(_a, NodeA, "S0", conductor: true).Create(new TaskItem { Title = "Задача" }, "", "", null);
        var original = Read(_a, 0, "нет-такого-узла").First(c => c.Pk == task.Id);
        Sync(_a, _b, 0, NodeB);

        var copy = Read(_b, 0, "нет-такого-узла").First(c => c.Pk == task.Id);

        // иначе транзитный сервер выдавал бы чужие правки за свои, и строка «молодела» бы
        // на каждом транзите, затирая более свежие данные
        Assert.Equal(NodeA, copy.NodeId);
        Assert.Equal(original.Ts, copy.Ts);
    }

    [Fact]
    public void Deletes_Replicate_Too()
    {
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, "INSERT INTO counters(prefix, next) VALUES ('T@S9', 7)");
        }
        var (_, cursor) = Sync(_a, _b, 0, NodeB);
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, "DELETE FROM counters WHERE prefix='T@S9'");
        }

        Sync(_a, _b, cursor, NodeB);

        using var check = _b.Open();
        Assert.Equal(0, Sql.Scalar<long>(check, null,
            "SELECT COUNT(*) FROM counters WHERE prefix='T@S9'"));
    }

    // --- LWW: «выиграл последний» ---

    [Fact]
    public void Later_Change_Wins_Over_Earlier_One()
    {
        Assert.True(ChangeLog.IsNewer("2026-08-03T10:00:01.0000000Z", NodeA,
            "2026-08-03T10:00:00.0000000Z", NodeB));
        Assert.False(ChangeLog.IsNewer("2026-08-03T10:00:00.0000000Z", NodeA,
            "2026-08-03T10:00:01.0000000Z", NodeB));
    }

    [Fact]
    public void Equal_Clocks_Are_Decided_By_The_Node_So_Servers_Never_Diverge()
    {
        const string ts = "2026-08-03T10:00:00.0000000Z";

        Assert.True(ChangeLog.IsNewer(ts, "unid-b", ts, "unid-a"));
        Assert.False(ChangeLog.IsNewer(ts, "unid-a", ts, "unid-b"));
        // сама с собой строка не «новее» — иначе одно и то же изменение применялось бы вечно
        Assert.False(ChangeLog.IsNewer(ts, "unid-a", ts, "unid-a"));
    }

    [Fact]
    public void Stale_Change_Loses_And_Is_Counted_As_A_Conflict()
    {
        // Раздельное владение (ТЗ гл. 6) как раз и должно делать этот случай невозможным:
        // у строки один сервер-писатель. Но страховка обязана работать — например, когда
        // дирижёр забирает задачи выбывшего сервера, а тот вернулся в сеть. Правку «мимо
        // владения» изображаем прямой записью в таблицу.
        var task = Tasks(_a, NodeA, "S0", conductor: true).Create(new TaskItem { Title = "Общая" }, "", "", null);
        var (_, cursor) = Sync(_a, _b, 0, NodeB);

        Rename(_b, task.Id, "Правка ноутбука");
        Thread.Sleep(20);
        Rename(_a, task.Id, "Правка дирижёра");

        // более поздняя правка приходит на B и выигрывает
        var forward = Sync(_a, _b, cursor, NodeB);
        Assert.True(forward.Result.Applied > 0);
        Assert.Equal("Правка дирижёра", Tasks(_b, NodeB, "S1", conductor: false).Get(task.Id)!.Title);

        // а более ранняя, приехав к A, проигрывает — и это считается конфликтом
        var back = Sync(_b, _a, 0, NodeA);
        Assert.True(back.Result.Conflicts > 0);
        Assert.Contains("tasks", back.Result.LastConflict);
        Assert.Equal("Правка дирижёра", Tasks(_a, NodeA, "S0", conductor: true).Get(task.Id)!.Title);
    }

    /// <summary>Правка строки мимо правил владения — так изображается редкая гонка.</summary>
    private static void Rename(Database db, string taskId, string title)
    {
        using var conn = db.Open();
        Sql.Exec(conn, null, "UPDATE tasks SET title=@t, updated_at=@u WHERE id=@id",
            ("@t", title), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
    }

    // --- первичная репликация ---

    [Fact]
    public void Existing_Rows_Are_In_The_Log_From_The_Start()
    {
        // журнал наполняется существующими строками при создании, поэтому «изменения
        // после 0» — это снимок базы целиком, и отдельного протокола первичной
        // репликации не нужно
        var dir = Path.Combine(_dir, "seeded");
        var db = new Database(dir, "ai2p.db");
        db.Init(NodeA);
        // строка, заведённая ДО появления журнала: убираем журнал и ставим его заново
        Tasks(db, NodeA, "S0", conductor: true).Create(new TaskItem { Title = "Старая" }, "", "", null);
        using (var conn = db.Open())
        {
            Sql.Exec(conn, null, "DELETE FROM changes");
            Sql.Exec(conn, null, "DELETE FROM meta WHERE key='changes_seeded'");
            ChangeLog.Install(conn, DatabaseKind.Org);
        }

        var snapshot = Read(db, 0, "нет-такого-узла");

        Assert.Contains(snapshot, c => c.Table == "tasks"
                                       && ChangeLog.Parse(c.PayloadJson)["title"] as string == "Старая");
        // часы взяты из самой строки, а не «сейчас»: иначе снимок выиграл бы LWW
        // у более свежих правок партнёра
        var row = snapshot.First(c => c.Table == "tasks");
        Assert.StartsWith("20", row.Ts);
    }

    [Fact]
    public void Whole_Reference_Data_Reaches_An_Empty_Server()
    {
        // так выглядит подключение к существующей организации: справочники у себя
        // не засеиваются, они приезжают от дирижёра целиком
        new RefDataService(_a, Path.Combine(AppContext.BaseDirectory, "i18n")).Seed();
        new TaskStatusService(_a).Seed();

        var cursor = 0L;
        while (true)
        {
            var (result, next) = Sync(_a, _b, cursor, NodeB, limit: 100);
            if (next == cursor)
            {
                break;
            }
            cursor = next;
            Assert.Equal(0, result.Conflicts);
        }

        Assert.NotEmpty(new RefDataService(_b, Path.Combine(AppContext.BaseDirectory, "i18n")).Skills());
        Assert.NotEmpty(new TaskStatusService(_b).List("ru"));
    }

    // --- что реплицируется, а что нет ---

    [Fact]
    public void Node_Id_Never_Replicates()
    {
        // meta не реплицируется: там идентификатор узла, он у каждого свой — иначе
        // серверы перестали бы различать авторов изменений
        Assert.DoesNotContain("meta", ChangeLog.OrgTables);
        Assert.DoesNotContain("meta", ChangeLog.ServerTables);
        Assert.DoesNotContain(Read(_a, 0, "нет-такого-узла"), c => c.Table == "meta");
        Assert.NotEqual(_a.NodeId, _b.NodeId);
    }

    [Fact]
    public void Ui_State_Stays_Local()
    {
        // лейаут фреймов зависит от экрана, а не от организации: реплицировать его незачем
        new AppStateService(_a).Set("acc-1", AppStateService.UiLayoutKey, "{\"frames\":[]}");

        Assert.DoesNotContain("app_state", ChangeLog.OrgTables);
        Assert.DoesNotContain(Read(_a, 0, "нет-такого-узла"), c => c.Table == "app_state");
    }

    [Fact]
    public void Server_Tokens_Never_Leave_The_Server()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var servers = new ServerService(serverDb, new EventStore(serverDb));
        var local = servers.EnsureLocal("главный", "http", "localhost", 5480, "/ai2p");
        var token = servers.IssueToken(local.Id);

        var changes = Read(serverDb, 0, "нет-такого-узла").Where(c => c.Table == "servers").ToList();

        Assert.NotEmpty(changes);
        Assert.All(changes, change =>
        {
            var values = ChangeLog.Parse(change.PayloadJson);
            // токены доступа, признак «это я» и состояние своей заявки не отдаются никогда
            Assert.False(values.ContainsKey("token"));
            Assert.False(values.ContainsKey("peer_token"));
            Assert.False(values.ContainsKey("is_local"));
            Assert.False(values.ContainsKey("last_error"));
        });
        Assert.NotEqual("", token);
    }

    [Fact]
    public void Replication_Intervals_Do_Replicate()
    {
        // единственная часть записи сервера, которая едет по кластеру: отсчёт ведут
        // оба конца пары, и дирижёр задаёт интервал рядовому серверу
        var serverDb = new Database(Path.Combine(_dir, "srv2"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var servers = new ServerService(serverDb, new EventStore(serverDb));
        var local = servers.EnsureLocal("главный", "http", "localhost", 5480, "/ai2p");
        servers.SetReplication(local.Id, 60, 20, null);

        var change = Read(serverDb, 0, "нет-такого-узла")
            .Last(c => c.Table == "servers" && c.Pk == local.Id);
        var values = ChangeLog.Parse(change.PayloadJson);

        Assert.Equal(60, Convert.ToInt64(values["repl_interval_sec"]));
        Assert.Equal(20, Convert.ToInt64(values["repl_retry_sec"]));
    }

    // --- интервалы репликации (todo43 «форма сервера») ---

    [Fact]
    public void Interval_Below_The_Minimum_Is_Refused()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv3"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var servers = new ServerService(serverDb, new EventStore(serverDb));
        var local = servers.EnsureLocal("главный", "http", "localhost", 5480, "/ai2p");

        Assert.Throws<ArgumentException>(() => servers.SetReplication(local.Id, 29, null, null));
        Assert.Throws<ArgumentException>(() => servers.SetReplication(local.Id, 60, 14, null));
    }

    [Fact]
    public void Empty_Interval_Means_Manual_Only()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv4"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var servers = new ServerService(serverDb, new EventStore(serverDb));
        var local = servers.EnsureLocal("главный", "http", "localhost", 5480, "/ai2p");

        var saved = servers.SetReplication(local.Id, null, null, null);

        Assert.Null(saved.ReplIntervalSec);
        Assert.Null(servers.Get(local.Id)!.ReplIntervalSec);
    }

    [Fact]
    public void Node_Id_Equals_The_Local_Server_Key()
    {
        // авторство в журнале подписывается внутренним ключом сервера — иначе гашение
        // эха не работало бы: партнёра мы знаем по unid, а не по идентификатору узла
        var serverDb = new Database(Path.Combine(_dir, "srv5"), "server.db", DatabaseKind.Server);
        serverDb.Init();
        var servers = new ServerService(serverDb, new EventStore(serverDb));

        var local = servers.EnsureLocal("главный", "http", "localhost", 5480, "/ai2p");

        Assert.Equal(serverDb.NodeId, local.Id);
    }

    // --- курсоры и состояние (экран диагностики) ---

    [Fact]
    public void Cursor_Only_Moves_Forward_And_Survives_A_Change_Of_Leader()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv6"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var state = new ReplicationStateService(serverDb);

        state.Advance("org-1", NodeB, "pull_cursor", 10);
        state.Advance("org-1", NodeB, "pull_cursor", 4);   // запоздавший ответ не откатывает
        state.Advance("org-1", NodeB, "push_cursor", 7);

        var saved = state.Get("org-1", NodeB);
        Assert.Equal(10, saved.PullCursor);
        Assert.Equal(7, saved.PushCursor);
    }

    [Fact]
    public void Diagnostics_Keeps_Errors_Conflicts_And_Next_Run()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv7"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var state = new ReplicationStateService(serverDb);
        var next = DateTime.UtcNow.AddSeconds(30);

        state.Save(new ReplicationState
        {
            OrgId = "org-1",
            ServerId = NodeB,
            Status = ReplicationStatuses.Error,
            LastError = "Нет связи с сервером",
            Conflicts = 2,
            LastConflict = "tasks abc",
            NextRunAt = next,
            Received = 5,
            Sent = 3,
        });

        var saved = state.Get("org-1", NodeB);
        Assert.Equal(ReplicationStatuses.Error, saved.Status);
        Assert.Equal("Нет связи с сервером", saved.LastError);
        Assert.Equal(2, saved.Conflicts);
        Assert.Equal(5, saved.Received);
        Assert.Equal(next.ToString("o"), saved.NextRunAt!.Value.ToString("o"));

        state.Reset("org-1", NodeB);
        Assert.Equal(0, state.Get("org-1", NodeB).PullCursor);
    }

    [Fact]
    public void Unknown_Cursor_Name_Is_Refused()
    {
        // имя курсора подставляется в SQL текстом — список закрытый
        var serverDb = new Database(Path.Combine(_dir, "srv8"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var state = new ReplicationStateService(serverDb);

        Assert.Throws<ArgumentException>(() => state.Advance("org-1", NodeB, "1=1; DROP TABLE orgs", 1));
    }

    // --- пачки: журнал читается порциями, а прогресс считается по остатку ---

    [Fact]
    public void Log_Is_Read_In_Batches_With_A_Known_Remainder()
    {
        var service = Tasks(_a, NodeA, "S0", conductor: true);
        for (var i = 0; i < 12; i++)
        {
            service.Create(new TaskItem { Title = "Задача " + i }, "", "", null);
        }

        var first = Read(_a, 0, NodeB).Take(5).ToList();
        using var conn = _a.Open();
        var remaining = ChangeLog.CountAfter(conn, first[^1].Seq, NodeB);

        Assert.Equal(5, first.Count);
        Assert.True(remaining > 0);
        Assert.True(ChangeLog.Head(conn) >= first[^1].Seq + remaining);
    }
}
