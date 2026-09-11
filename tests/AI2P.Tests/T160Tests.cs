using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-160 «ошибка репликации»: ВОСПРОИЗВЕДЕНИЕ. Первичная репликация настоящей организации
/// (справочники, проект, команда, десятки задач, задания и чат) на пустой сервер — пачками
/// по <see cref="AI2P.Server.Org.ReplicationService.BatchSize"/>, ровно как в сеансе.
/// </summary>
public sealed class T160Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t160-" + Guid.NewGuid().ToString("N"));
    private readonly Database _a;
    private readonly Database _b;

    /// <summary>Серверная БД реплики: в ней живут курсоры и очередь повтора (T-160).</summary>
    private readonly Database _srv;

    private const string NodeA = "unid-S0";
    private const string NodeB = "unid-S1";

    public T160Tests()
    {
        _a = new Database(Path.Combine(_dir, "a"), "ai2p.db");
        _a.Init(NodeA);
        _b = new Database(Path.Combine(_dir, "b"), "ai2p.db");
        _b.Init(NodeB);
        _srv = new Database(Path.Combine(_dir, "b"), "server.db", DatabaseKind.Server);
        _srv.Init(NodeB);
    }

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

    /// <summary>Наполнить «дирижёра» так, как выглядит рабочая организация.</summary>
    private void FillConductor()
    {
        var events = new EventStore(_a);
        var files = new FileStore(_a.DataDir);
        var scope = new ServerScope(() => "", () => "", () => true);
        new RefDataService(_a, Path.Combine(AppContext.BaseDirectory, "i18n")).Seed();
        new TaskStatusService(_a).Seed();
        new ActionCatalogService(_a, Path.Combine(AppContext.BaseDirectory, "i18n")).Seed();
        var projects = new ProjectService(_a, events, files);
        var executors = new ExecutorService(_a, events, files);
        var teams = new TeamService(_a, events);
        var tasks = new TaskService(_a, events, files, scope);
        var jobs = new JobService(_a, events, scope);
        var chat = new ChatService(_a, events);

        var project = projects.Create("Проект", Path.Combine(_dir, "prj"), null, null);
        var human = executors.Create(new Executor
        {
            Nick = "Человек", Kind = ExecutorKind.Human, SystemRole = SystemRole.Owner,
        }, null);
        var ai = executors.Create(new Executor
        {
            Nick = "Агент", Kind = ExecutorKind.Ai, SystemRole = SystemRole.Editor,
        }, null);
        var team = teams.Create(new Team
        {
            Name = "Команда", ProjectId = project.Id,
            Members =
            [
                new TeamMember { ExecutorId = human.Id, IsLead = true },
                new TeamMember { ExecutorId = ai.Id, ParentExecutorId = human.Id },
            ],
        }, null);

        for (var i = 0; i < 40; i++)
        {
            var parent = tasks.Create(new TaskItem
            {
                Title = $"Задача {i}", ProjectId = project.Id, TeamId = team.Id,
                ResponsibleId = human.Id,
            }, $"Описание задачи {i}", "Критерии", null);
            for (var k = 0; k < 2; k++)
            {
                var child = tasks.Create(new TaskItem
                {
                    Title = $"Подзадача {i}.{k}", ProjectId = project.Id, TeamId = team.Id,
                    ParentId = parent.Id, ResponsibleId = human.Id,
                }, "Описание подзадачи", "", null);
                var job = jobs.Create(child.Id, ai.Id, "req.md", null);
                jobs.SetState(job.Id, JobState.Done, null, "res.md");
                chat.Add(child.Id, human.Id, ai.Id, "вопрос " + k);
                chat.Add(child.Id, ai.Id, human.Id, "ответ " + k);
            }
        }
    }

    /// <summary>Журнал изменений, каким он бывает у сервера, обновлённого до версии
    /// с репликацией: снимок существующих строк (Backfill) вместо истории правок.</summary>
    private void RebuildJournalAsSnapshot()
    {
        using var conn = _a.Open();
        Sql.Exec(conn, null, "DELETE FROM changes");
        Sql.Exec(conn, null, "DELETE FROM meta WHERE key='changes_seeded'");
        ChangeLog.DropTriggers(conn);
        ChangeLog.Install(conn, DatabaseKind.Org);
    }

    /// <summary>Первичная репликация пачками — как <c>ReplicationService.PullAsync</c>.</summary>
    private (int Applied, int Batches) PullAll(int batch = AI2P.Server.Org.ReplicationService.BatchSize)
    {
        long cursor = 0;
        var applied = 0;
        var batches = 0;
        while (true)
        {
            List<RowChange> items;
            using (var conn = _a.Open())
            {
                items = ChangeLog.Read(conn, cursor, batch, NodeB);
            }
            if (items.Count == 0)
            {
                return (applied, batches);
            }
            var result = ChangeLog.Apply(_b, items);
            applied += result.Applied;
            batches++;
            cursor = items[^1].Seq;
        }
    }

    private long Count(Database db, string table)
    {
        using var conn = db.Open();
        return Sql.Scalar<long>(conn, null, $"SELECT COUNT(*) FROM {table}");
    }

    [Fact]
    public void Primary_Replication_Of_A_Real_Org_Goes_Through_In_Batches()
    {
        FillConductor();
        RebuildJournalAsSnapshot();

        var (_, batches) = PullAll();

        Assert.True(batches > 1, "данных должно хватить на несколько пачек");
        Assert.Equal(Count(_a, "tasks"), Count(_b, "tasks"));
        Assert.Equal(Count(_a, "chat_messages"), Count(_b, "chat_messages"));
        Assert.Equal(Count(_a, "jobs"), Count(_b, "jobs"));
        Assert.Equal(Count(_a, "task_skills"), Count(_b, "task_skills"));
    }

    [Fact]
    public void Primary_Replication_Of_A_Live_Journal_Goes_Through_In_Batches()
    {
        FillConductor();

        var (_, batches) = PullAll();

        Assert.True(batches > 1);
        Assert.Equal(Count(_a, "tasks"), Count(_b, "tasks"));
        Assert.Equal(Count(_a, "chat_messages"), Count(_b, "chat_messages"));
    }

    // --- T-160: строка, которую база не приняла ---

    /// <summary>Изменение строки таблицы — как его отдал бы журнал партнёра.</summary>
    private static RowChange Row(long seq, string table, string pk, string json) => new()
    {
        Seq = seq,
        Table = table,
        Pk = pk,
        Op = ChangeOps.Upsert,
        Ts = Sql.ToDb(new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc).AddSeconds(seq)),
        NodeId = NodeA,
        PayloadJson = json,
    };

    private static string TaskJson(string id, string title, string? parent = null) =>
        $$"""
          {"id":"{{id}}","display_id":"{{title}}","project_id":null,"team_id":null,
           "parent_id":{{(parent is null ? "null" : "\"" + parent + "\"")}},"kind":"task",
           "title":"{{title}}","description_path":"","status":"draft","priority":1,
           "priority_num":15,"is_not_split":0,"launch_json":"{}","acceptance_path":"",
           "is_template":0,"created_at":"2026-08-12T10:00:00.0000000Z",
           "updated_at":"2026-08-12T10:00:00.0000000Z"}
          """;

    /// <summary>
    /// ОДНА ОТВЕРГНУТАЯ СТРОКА НЕ ТЕРЯЕТ ПАЧКУ. Раньше нарушение ограничения откатывало все
    /// изменения пачки и роняло сеанс исключением; теперь остальные строки применяются,
    /// а виновная называется таблицей, ключом и причиной.
    /// </summary>
    [Fact]
    public void One_Rejected_Row_Does_Not_Lose_The_Rest_Of_The_Batch()
    {
        // на реплике уже есть навык с этим именем, но с ДРУГИМ ключом: приехавшая строка
        // упрётся в уникальность skills.name — ровно тот отказ, что видел заказчик
        using (var conn = _b.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO skills (id, name, description, is_custom, created_at, updated_at)
                VALUES ('local-id', 'code-write', '', 0, '2026-08-01T00:00:00.0000000Z',
                        '2026-08-01T00:00:00.0000000Z')
                """);
        }
        var batch = new List<RowChange>
        {
            Row(1, "skills", "remote-id", """
                {"id":"remote-id","display_id":"","name":"code-write","description":"","is_custom":0,
                 "created_at":"2026-08-12T10:00:00.0000000Z","updated_at":"2026-08-12T10:00:00.0000000Z"}
                """),
            Row(2, "tasks", "t1", TaskJson("t1", "Задача 1")),
            Row(3, "tasks", "t2", TaskJson("t2", "Задача 2")),
        };

        var result = ChangeLog.Apply(_b, batch);

        Assert.Equal(2, result.Applied);
        Assert.Equal(2, Count(_b, "tasks"));
        var failure = Assert.Single(result.Failed);
        Assert.Equal("skills", failure.Change.Table);
        Assert.Contains("UNIQUE", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("skills.name", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>Ссылка «вперёд» ВНУТРИ пачки: подзадача идёт раньше родителя — это законно
    /// и должно проходить, даже когда пачка применяется построчно.</summary>
    [Fact]
    public void Child_Before_Parent_In_One_Batch_Applies()
    {
        var batch = new List<RowChange>
        {
            Row(1, "tasks", "child", TaskJson("child", "Подзадача", parent: "parent")),
            Row(2, "tasks", "parent", TaskJson("parent", "Родитель")),
            // строка, из-за которой пачка пойдёт по «медленному» пути: такой таблицы нет
            Row(3, "chat_messages", "m1", """
                {"id":"m1","task_id":"нет-такой-задачи","from_executor_id":"нет","text":"привет",
                 "kind":"message","options_json":"[]","author_name":"",
                 "created_at":"2026-08-12T10:00:00.0000000Z","updated_at":"2026-08-12T10:00:00.0000000Z"}
                """),
        };

        var result = ChangeLog.Apply(_b, batch);

        Assert.Equal(2, Count(_b, "tasks"));
        Assert.Equal(2, result.Applied);
        Assert.Single(result.Failed);
    }

    /// <summary>
    /// РОДИТЕЛЬ ПРИЕХАЛ СЛЕДУЮЩЕЙ ПАЧКОЙ. Именно этот случай и останавливал репликацию
    /// намертво: ребёнок в одной пачке, родитель в другой — сеанс падал, курсор стоял,
    /// и всё, что шло после (чат, задания, файлы), не приезжало никогда. Теперь строка
    /// ждёт в очереди повтора и применяется, как только приедет родитель.
    /// </summary>
    [Fact]
    public void Child_In_An_Earlier_Batch_Waits_For_Its_Parent_And_Applies()
    {
        var queue = new ReplPendingService(_srv);

        var first = ChangeLog.Apply(_b, [Row(1, "tasks", "child", TaskJson("child", "Подзадача", "parent"))]);
        queue.Save("org", NodeA, ReplScopes.Org, first.Failed);

        Assert.Equal(0, Count(_b, "tasks"));
        Assert.Equal(1, queue.Count("org", NodeA));
        Assert.Contains("FOREIGN KEY", queue.Last("org", NodeA), StringComparison.Ordinal);

        // следующая пачка привозит родителя — и очередь разбирается
        ChangeLog.Apply(_b, [Row(2, "tasks", "parent", TaskJson("parent", "Родитель"))]);
        var pending = queue.Take("org", NodeA, ReplScopes.Org);
        var retry = ChangeLog.Apply(_b, pending);
        queue.Remove("org", NodeA, ReplScopes.Org, pending.Where(p => retry.Failed.All(f => f.Change.Pk != p.Pk)));

        Assert.Equal(1, retry.Applied);
        Assert.Equal(2, Count(_b, "tasks"));
        Assert.Equal(0, queue.Count("org", NodeA));
        Assert.Equal("", queue.Last("org", NodeA));
    }

    /// <summary>
    /// СЕАНС ИДЁТ ДАЛЬШЕ ОТВЕРГНУТОЙ СТРОКИ. Проверка того самого симптома: задачи приехали
    /// заголовками, а чат — нет. Дальние таблицы обязаны доехать, даже если в середине
    /// журнала есть строка, которую база принять не может.
    /// </summary>
    [Fact]
    public void Session_Goes_Past_A_Rejected_Row_And_Brings_The_Chat()
    {
        FillConductor();
        RebuildJournalAsSnapshot();
        // навык-двойник на реплике: он отвергнет одну строку в самом начале журнала
        using (var conn = _b.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO skills (id, name, description, is_custom, created_at, updated_at)
                VALUES ('local-id', 'code-write', '', 0, '2026-08-01T00:00:00.0000000Z',
                        '2026-08-01T00:00:00.0000000Z')
                """);
        }

        PullAll();

        Assert.Equal(Count(_a, "tasks"), Count(_b, "tasks"));
        Assert.Equal(Count(_a, "chat_messages"), Count(_b, "chat_messages"));
        Assert.Equal(Count(_a, "jobs"), Count(_b, "jobs"));
    }

    /// <summary>
    /// КАК БЫЛО ДО ПРАВКИ: пачка применялась одной транзакцией, и отказ базы на одной строке
    /// откатывал её целиком, а исключение уходило наружу и обрывало сеанс. Ровно это заказчик
    /// и видел строкой «SQLite Error 19».
    /// </summary>
    [Fact]
    public void Before_The_Fix_One_Rejected_Row_Threw_And_Lost_The_Whole_Batch()
    {
        using (var conn = _b.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO skills (id, name, description, is_custom, created_at, updated_at)
                VALUES ('local-id', 'code-write', '', 0, '2026-08-01T00:00:00.0000000Z',
                        '2026-08-01T00:00:00.0000000Z')
                """);
        }
        var batch = new List<RowChange>
        {
            Row(1, "tasks", "t1", TaskJson("t1", "Задача 1")),
            Row(2, "skills", "remote-id", """
                {"id":"remote-id","display_id":"","name":"code-write","description":"","is_custom":0,
                 "created_at":"2026-08-12T10:00:00.0000000Z","updated_at":"2026-08-12T10:00:00.0000000Z"}
                """),
        };

        var thrown = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(
            () => ChangeLog.ApplyInOneTransaction(_b, batch));

        Assert.Equal(19, thrown.SqliteErrorCode);
        Assert.Equal(0, Count(_b, "tasks"));   // пачка потеряна целиком, хотя задача была годной
        // а «умное» применение ту же пачку доводит до конца
        Assert.Single(ChangeLog.Apply(_b, batch).Failed);
        Assert.Equal(1, Count(_b, "tasks"));
    }

    /// <summary>Очередь повтора помнит строку между сеансами и не плодит дублей.</summary>
    [Fact]
    public void Retry_Queue_Keeps_One_Record_Per_Row()
    {
        var queue = new ReplPendingService(_srv);
        var failure = new RowFailure
        {
            Change = Row(7, "tasks", "child", TaskJson("child", "Подзадача", "parent")),
            Reason = "FOREIGN KEY constraint failed",
        };

        queue.Save("org", NodeA, ReplScopes.Org, [failure]);
        queue.Save("org", NodeA, ReplScopes.Org, [failure]);

        Assert.Equal(1, queue.Count("org", NodeA));
        Assert.Contains("попыток 2", queue.Last("org", NodeA), StringComparison.Ordinal);
        var one = Assert.Single(queue.List("org", NodeA));
        Assert.Equal("tasks", one.Table);
        Assert.Equal(2, one.Tries);

        queue.Reset("org", NodeA);
        Assert.Equal(0, queue.Count("org", NodeA));
    }

    /// <summary>Состояние пары помнит, сколько строк ждёт повтора (колонка экрана диагностики).</summary>
    [Fact]
    public void Replication_State_Carries_The_Pending_Count()
    {
        var states = new ReplicationStateService(_srv);
        var state = states.Get("org", NodeA);
        state.Pending = 3;
        state.LastPending = "tasks child: FOREIGN KEY constraint failed";

        states.Save(state);

        var loaded = states.Get("org", NodeA);
        Assert.Equal(3, loaded.Pending);
        Assert.StartsWith("tasks child", loaded.LastPending, StringComparison.Ordinal);
    }
}
