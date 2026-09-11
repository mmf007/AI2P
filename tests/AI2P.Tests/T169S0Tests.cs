using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-169-S0: СТРОКА, У КОТОРОЙ НА ПРИЁМНИКЕ НЕТ РОДИТЕЛЯ, ВСЁ РАВНО ДОЛЖНА ПРИЕХАТЬ.
///
/// Жалоба заказчика: заведённый на дирижёре (S1) исполнитель не приезжает на второй сервер
/// (S0), и новый член команды с этим исполнителем — тоже. Причина не в журнале и не в
/// курсорах: строка исполнителя ссылается на запись справочника моделей, а записи справочника
/// у партнёра может не быть ВОВСЕ — модели дистрибутива и модели, подготовленные
/// инсталлятором, намеренно не реплицируются (T-227). База отвечала «FOREIGN KEY constraint
/// failed», строка уходила в очередь повтора (T-160) и пробовалась там вечно, а следом
/// вставало всё, что на исполнителя ссылается: состав команды, назначения задач.
///
/// Воспроизведено живьём на двух настоящих серверах выпущенной 1.119
/// (test/t169s0/fk169.py): «pending 2, executors …: FOREIGN KEY constraint failed».
/// </summary>
public sealed class T169S0Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t169s0-" + Guid.NewGuid().ToString("N"));

    private readonly Database _a;
    private readonly Database _b;

    private const string NodeA = "unid-S1";
    private const string NodeB = "unid-S0";

    public T169S0Tests()
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

    private ExecutorService Executors(Database db) =>
        new(db, new EventStore(db), new FileStore(db.DataDir));

    private TeamService Teams(Database db) => new(db, new EventStore(db));

    /// <summary>Такт репликации: перенести журнал источника в приёмник.</summary>
    private static ApplyResult Sync(Database from, Database to, string toNode)
    {
        List<RowChange> items;
        using (var conn = from.Open())
        {
            items = ChangeLog.Read(conn, 0, 500, toNode);
        }
        return ChangeLog.Apply(to, items);
    }

    /// <summary>Запись справочника моделей ТОЛЬКО на одном сервере — так и живут модели
    /// дистрибутива и модели инсталлятора: в журнал они не пишутся (T-227).</summary>
    private static string ModelOnlyHere(Database db, string name)
    {
        var id = Guid.NewGuid().ToString();
        using (ChangeLog.Mute(db))
        {
            using var conn = db.Open();
            Sql.Exec(conn, null, """
                INSERT INTO ai_models (id, name, is_custom, is_active, profile_path,
                                       capabilities_path, created_at, updated_at)
                VALUES (@id, @name, 1, 1, '', '', @now, @now)
                """,
                ("@id", id), ("@name", name), ("@now", Sql.ToDb(DateTime.UtcNow)));
        }
        return id;
    }

    private static long Count(Database db, string sql, params (string, object?)[] args)
    {
        using var conn = db.Open();
        return Sql.Scalar<long>(conn, null, sql, args);
    }

    // --- 1. исполнитель с моделью, которой у партнёра нет ---

    [Fact]
    public void An_Executor_Whose_Model_Is_Missing_At_The_Partner_Still_Arrives()
    {
        var modelId = ModelOnlyHere(_a, "Модель только здесь");
        var agent = Executors(_a).CreateMember(new Executor
        {
            Nick = "Агент",
            InternalName = "Модель только здесь",
            Kind = ExecutorKind.Ai,
            SystemRole = SystemRole.Editor,
            ModelId = modelId,
        }, null);

        var result = Sync(_a, _b, NodeB);

        Assert.Empty(result.Failed);
        Assert.Contains(result.Adopted, f => f.Change.Table == "executors");
        Assert.Equal(1, Count(_b, "SELECT COUNT(*) FROM executors WHERE id=@id", ("@id", agent.Id)));
        // ссылка висит в пустоту — ровно так же, как account_id, у которого FK нет вовсе
        Assert.Equal(0, Count(_b, "SELECT COUNT(*) FROM ai_models WHERE id=@id", ("@id", modelId)));
    }

    // --- 2. и член команды с ним ---

    [Fact]
    public void A_Team_Member_With_Such_An_Executor_Arrives_Too()
    {
        var modelId = ModelOnlyHere(_a, "Модель только здесь");
        var agent = Executors(_a).CreateMember(new Executor
        {
            Nick = "Агент", InternalName = "Агент", Kind = ExecutorKind.Ai,
            SystemRole = SystemRole.Editor, ModelId = modelId,
        }, null);
        var team = Teams(_a).Create(new Team
        {
            Name = "Команда",
            Members = [new TeamMember { ExecutorId = agent.Id }],
        }, null);

        var result = Sync(_a, _b, NodeB);

        Assert.Empty(result.Failed);
        Assert.Equal(1, Count(_b, "SELECT COUNT(*) FROM team_members WHERE team_id=@t",
            ("@t", team.Id)));
    }

    // --- 3. родитель приехал — чинить нечего, второго прохода не было ---

    [Fact]
    public void When_The_Parent_Is_There_Nothing_Is_Adopted()
    {
        var models = new AiModelService(_a, new EventStore(_a), new FileStore(_a.DataDir));
        var model = models.Create(new AiModel { Name = "Обычная модель", IsCustom = true }, null);
        var agent = Executors(_a).CreateMember(new Executor
        {
            Nick = "Агент", InternalName = "Агент", Kind = ExecutorKind.Ai,
            SystemRole = SystemRole.Editor, ModelId = model.Id,
        }, null);

        var result = Sync(_a, _b, NodeB);

        Assert.Empty(result.Failed);
        Assert.Empty(result.Adopted);
        Assert.Equal(1, Count(_b, "SELECT COUNT(*) FROM executors WHERE id=@id", ("@id", agent.Id)));
        Assert.Equal(1, Count(_b, "SELECT COUNT(*) FROM ai_models WHERE id=@id", ("@id", model.Id)));
    }

    // --- 4. отказ ДРУГОГО рода по-прежнему ждёт в очереди повтора (T-160) ---

    [Fact]
    public void A_Refusal_Of_Another_Kind_Still_Waits_In_The_Retry_Queue()
    {
        // у навыков UNIQUE(name): одно и то же имя с разными идентификаторами
        // на двух серверах — не «нет родителя», а спор, который сам не решится
        foreach (var (db, id) in new[] { (_a, Guid.NewGuid().ToString()), (_b, Guid.NewGuid().ToString()) })
        {
            using var conn = db.Open();
            Sql.Exec(conn, null, """
                INSERT INTO skills (id, name, is_custom, created_at, updated_at)
                VALUES (@id, 'спорный', 1, @now, @now)
                """,
                ("@id", id), ("@now", Sql.ToDb(DateTime.UtcNow)));
        }

        var result = Sync(_a, _b, NodeB);

        Assert.Contains(result.Failed, f => f.Change.Table == "skills");
        Assert.DoesNotContain(result.Adopted, f => f.Change.Table == "skills");
    }
}
