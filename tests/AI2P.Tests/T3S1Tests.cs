using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-3-S1: РЕПЛИКАЦИЯ СОСТАВА КОМАНД И СВЯЗЕЙ ЗАДАЧИ.
///
/// Жалоба: на втором сервере часть исполнителей не видна в составе команд, а часть — в списке
/// выбора исполнителя задачи. Причина не в самих исполнителях, а в том, как сохраняется СОСТАВ:
/// правка команды (и правка задачи) сначала удаляет все строки связи, а потом вставляет их
/// заново. Обе записи попадают в журнал изменений с отметкой времени в миллисекундах — и,
/// попав в одну миллисекунду, получают ОДИНАКОВЫЕ часы (ts, node). Приёмник применяет удаление,
/// а следующую за ним вставку той же строки отбрасывает как «не новее» — строка пропадает у
/// партнёра насовсем.
///
/// Здесь проверяется, что журнал не выдаёт двух изменений одной строки с одинаковыми часами,
/// и что состав команды и связи задачи доезжают целиком.
/// </summary>
public sealed class T3S1Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t3s1-" + Guid.NewGuid().ToString("N"));

    private readonly Database _a;
    private readonly Database _b;

    private const string NodeA = "unid-S0";
    private const string NodeB = "unid-S1";

    public T3S1Tests()
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
            // временный каталог уберёт система
        }
    }

    private TeamService Teams(Database db) => new(db, new EventStore(db));

    private ExecutorService Executors(Database db) =>
        new(db, new EventStore(db), new FileStore(db.DataDir));

    private TaskService Tasks(Database db, string serverId, string code, bool conductor) =>
        new(db, new EventStore(db), new FileStore(db.DataDir),
            new ServerScope(() => serverId, () => code, () => conductor));

    /// <summary>Такт репликации: перенести журнал источника в приёмник.</summary>
    private static long Sync(Database from, Database to, long cursor, string toNode)
    {
        List<RowChange> items;
        using (var conn = from.Open())
        {
            items = ChangeLog.Read(conn, cursor, 500, toNode);
        }
        if (items.Count == 0)
        {
            return cursor;
        }
        ChangeLog.Apply(to, items);
        return items[^1].Seq;
    }

    private static List<string> Members(Database db, string teamId)
    {
        using var conn = db.Open();
        return Sql.Query(conn, null,
            "SELECT executor_id FROM team_members WHERE team_id=@t ORDER BY executor_id",
            r => r.S("executor_id"), ("@t", teamId));
    }

    // --- 1. журнал: две правки одной строки не могут совпасть часами ---

    [Fact]
    public void Two_Changes_Of_One_Row_Never_Share_The_Same_Clock()
    {
        // ровно то, что делает сохранение состава: удалить всё и вставить заново, одной
        // транзакцией и в одну миллисекунду
        using (var conn = _a.Open())
        {
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < 20; i++)
            {
                Sql.Exec(conn, tx, "INSERT OR REPLACE INTO counters(prefix, next) VALUES ('X', @n)",
                    ("@n", i));
                Sql.Exec(conn, tx, "DELETE FROM counters WHERE prefix='X'");
            }
            tx.Commit();
        }

        using (var conn = _a.Open())
        {
            var stamps = Sql.Query(conn, null,
                "SELECT ts, node_id FROM changes WHERE tbl='counters' AND pk='X' ORDER BY seq",
                r => r.S("ts") + " " + r.S("node_id"));
            Assert.Equal(40, stamps.Count);
            Assert.Equal(stamps.Count, stamps.Distinct().Count());
        }
    }

    [Fact]
    public void A_Row_Deleted_And_Written_Back_Arrives_At_The_Partner()
    {
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, "INSERT INTO counters(prefix, next) VALUES ('Y', 1)");
        }
        var cursor = Sync(_a, _b, 0, NodeB);

        // одна транзакция: снести и записать заново — так сохраняется состав команды
        using (var conn = _a.Open())
        {
            using var tx = conn.BeginTransaction();
            Sql.Exec(conn, tx, "DELETE FROM counters WHERE prefix='Y'");
            Sql.Exec(conn, tx, "INSERT INTO counters(prefix, next) VALUES ('Y', 7)");
            tx.Commit();
        }
        Sync(_a, _b, cursor, NodeB);

        using (var conn = _b.Open())
        {
            Assert.Equal(7, Sql.Scalar<long>(conn, null,
                "SELECT next FROM counters WHERE prefix='Y'"));
        }
    }

    // --- 2. пачка от партнёра СТАРОЙ версии ---

    [Fact]
    public void A_Batch_From_An_Old_Partner_Keeps_The_Last_Change_Of_A_Row()
    {
        // партнёр версии 1.85 присылает удаление и вставку одной строки ОДНИМИ часами —
        // именно так выглядит у него сохранение состава команды. Раньше принималось только
        // удаление, и строка пропадала
        const string stamp = "2026-08-19T10:00:00.1230000Z";
        var batch = new List<RowChange>
        {
            Change(1, ChangeOps.Delete, stamp, """{"prefix":"Z"}"""),
            Change(2, ChangeOps.Upsert, stamp, """{"prefix":"Z","next":42}"""),
        };

        var result = ChangeLog.Apply(_b, batch);

        using var conn = _b.Open();
        Assert.Equal(42, Sql.Scalar<long>(conn, null, "SELECT next FROM counters WHERE prefix='Z'"));
        // курсор пачки не должен пострадать от схлопывания
        Assert.Equal(2, result.Cursor);
    }

    private static RowChange Change(long seq, string op, string ts, string payload) => new()
    {
        Seq = seq, Table = "counters", Pk = "Z", Op = op, Ts = ts, NodeId = NodeA,
        PayloadJson = payload,
    };

    // --- 3. шаг обновления билда 86: назвать потерянные строки заново ---

    [Fact]
    public void Upgrade_Step_86_Is_Declared()
    {
        var step = Assert.Single(AI2P.Server.Upgrade.Steps, s => s.Build == 86);
        Assert.Contains("часов", step.Title);
    }

    [Fact]
    public void Step_86_Names_The_Lost_Rows_Again_And_Is_Idempotent()
    {
        // журнал, накопленный СТАРОЙ версией: удаление и вставка одной строки одними часами
        const string stamp = "2026-08-19T10:00:00.1230000Z";
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json) VALUES
                  ('counters','Z','upsert','2026-08-19T09:00:00.0000000Z',@me,
                   '{"prefix":"Z","next":1}'),
                  ('counters','Z','delete',@ts,@me,'{"prefix":"Z"}'),
                  ('counters','Z','upsert',@ts,@me,'{"prefix":"Z","next":42}'),
                  ('counters','Q','upsert',@ts,@me,'{"prefix":"Q","next":1}')
                """, ("@ts", stamp), ("@me", NodeA));
        }
        // партнёр прочитал журнал ещё СТАРОЙ версией: удаление и вставка разошлись по разным
        // пачкам (граница пачки), и вставка была отброшена как «не новее»
        long cursor = 0;
        List<RowChange> journal;
        using (var conn = _a.Open())
        {
            journal = ChangeLog.Read(conn, 0, 500, NodeB);
        }
        foreach (var change in journal)
        {
            ChangeLog.Apply(_b, [change]);
            cursor = change.Seq;
        }
        using (var conn = _b.Open())
        {
            Assert.Equal(0, Sql.Scalar<long>(conn, null,
                "SELECT COUNT(*) FROM counters WHERE prefix='Z'"));
        }

        var resent = ChangeLog.ResendTiedRows(_a);

        Assert.Equal(1, resent);   // строка Q часами ни с кем не совпала — её не трогаем
        Sync(_a, _b, cursor, NodeB);
        using (var conn = _b.Open())
        {
            Assert.Equal(42, Sql.Scalar<long>(conn, null,
                "SELECT next FROM counters WHERE prefix='Z'"));
        }

        // повторный старт называет ту же строку тем же значением — состояние не меняется
        Assert.Equal(1, ChangeLog.ResendTiedRows(_a));
        Sync(_a, _b, cursor, NodeB);
        using (var conn = _b.Open())
        {
            Assert.Equal(42, Sql.Scalar<long>(conn, null,
                "SELECT next FROM counters WHERE prefix='Z'"));
        }
    }

    [Fact]
    public void Step_86_Does_Not_Speak_For_A_Foreign_Row()
    {
        // строку с тех пор поправил партнёр: его правка уже разошлась, и повторять её
        // своим голосом значило бы вернуть ему устаревшее состояние
        const string stamp = "2026-08-19T10:00:00.1230000Z";
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json) VALUES
                  ('counters','Z','delete',@ts,@me,'{"prefix":"Z"}'),
                  ('counters','Z','upsert',@ts,@me,'{"prefix":"Z","next":42}'),
                  ('counters','Z','upsert','2026-08-19T11:00:00.0000000Z',@peer,
                   '{"prefix":"Z","next":77}')
                """, ("@ts", stamp), ("@me", NodeA), ("@peer", NodeB));
        }

        Assert.Equal(0, ChangeLog.ResendTiedRows(_a));
    }

    // --- 4. рядовой сервер не засеивает справочники организации ---

    /// <summary>Реестр «сервера»: дирижёр он или рядовой — задаётся признаком.</summary>
    private AI2P.Server.Org.OrgRegistry Registry(string name, bool conductor)
    {
        var root = Path.Combine(_dir, name);
        var deps = new AI2P.Server.Org.OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: new AI2P.Connectors.SecretStore(Path.Combine(root, "secrets.json")),
            LocalModels: new AI2P.Connectors.LocalModelProcessService(),
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => "",
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => conductor,
            LocalServerId: () => "unid-S0",
            LocalCode: _ => conductor ? "S0" : "S1",
            ServerCode: (_, _) => "S0",
            ServerName: _ => "сервер");
        return new AI2P.Server.Org.OrgRegistry(Path.Combine(root, "server"), "server.db", deps);
    }

    private static long SkillCount(Database db)
    {
        using var conn = db.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM skills");
    }

    [Fact]
    public void The_Conductor_Seeds_The_Reference_Books()
    {
        using var registry = Registry("cond", conductor: true);
        var org = registry.Orgs.Create("Организация", "org", null);

        Assert.True(SkillCount(registry.Context(org).Db) > 0);
    }

    [Fact]
    public void A_Rank_And_File_Server_Never_Seeds_The_Reference_Books()
    {
        // навыки и форматы сеются со СЛУЧАЙНЫМИ идентификаторами, поэтому второй комплект
        // встроенных записей делает строки обеих сторон навсегда неприменимыми: UNIQUE(name)
        // отвергает чужие, а всё, что на них ссылается (навыки задачи), — по внешнему ключу.
        // Пометки реплики здесь нет нарочно: её можно потерять, а правило одно — справочники
        // ведёт дирижёр
        using var registry = Registry("plain", conductor: false);
        var org = registry.Orgs.Create("Организация", "org", null);

        Assert.Equal(0, SkillCount(registry.Context(org).Db));
    }

    // --- 5. состав команды ---

    [Fact]
    public void The_Whole_Team_Reaches_The_Second_Server()
    {
        var executors = Executors(_a);
        var people = Enumerable.Range(1, 5)
            .Select(i => executors.CreateMember(new Executor
            {
                Nick = "Исполнитель " + i,
                InternalName = "Человек " + i,
                Kind = ExecutorKind.Human,
                SystemRole = SystemRole.Editor,
            }, null))
            .ToList();
        var team = Teams(_a).Create(new Team
        {
            Name = "Команда",
            Members = people.Select(p => new TeamMember { ExecutorId = p.Id }).ToList(),
        }, null);
        var cursor = Sync(_a, _b, 0, NodeB);
        Assert.Equal(5, Members(_b, team.Id).Count);

        // правка команды: состав переписывается целиком — и именно здесь он терялся
        team.Name = "Команда (правка)";
        Teams(_a).Update(team, null);
        Sync(_a, _b, cursor, NodeB);

        Assert.Equal(Members(_a, team.Id), Members(_b, team.Id));
    }

    // --- 6. связи задачи: исполнитель, запасные, навыки, тэги ---

    [Fact]
    public void The_Task_Links_Reach_The_Second_Server()
    {
        var executors = Executors(_a);
        var main = executors.CreateMember(new Executor
        {
            Nick = "Основной", InternalName = "Основной", Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Editor,
        }, null);
        var spare = executors.CreateMember(new Executor
        {
            Nick = "Запасной", InternalName = "Запасной", Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Editor,
        }, null);

        var tasks = Tasks(_a, NodeA, "S0", conductor: true);
        var task = tasks.Create(new TaskItem
        {
            Title = "Задача",
            ExecutorIds = [main.Id],
            AltExecutorIds = [spare.Id],
            Tags = ["выпуск", "репликация"],
        }, "", "", null);
        var cursor = Sync(_a, _b, 0, NodeB);

        // правка задачи переписывает все её связи — тот же приём, что и у команды
        task.Title = "Задача (правка)";
        tasks.Update(task, "", "", null);
        Sync(_a, _b, cursor, NodeB);

        using var conn = _b.Open();
        Assert.Equal([main.Id], Sql.Query(conn, null,
            "SELECT executor_id FROM task_executors WHERE task_id=@t", r => r.S("executor_id"),
            ("@t", task.Id)));
        Assert.Equal([spare.Id], Sql.Query(conn, null,
            "SELECT executor_id FROM task_alt_executors WHERE task_id=@t", r => r.S("executor_id"),
            ("@t", task.Id)));
        Assert.Equal(2, Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM task_tags WHERE task_id=@t", ("@t", task.Id)));
    }
}
