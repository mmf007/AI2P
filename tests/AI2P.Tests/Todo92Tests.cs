using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo92 (версия 1.92): ПАДЕНИЕ ПРИ СТАРТЕ ОБНОВЛЁННОЙ УСТАНОВКИ.
///
/// Версия 1.91 не поднималась на уже работающей базе:
///
/// <code>
/// AI2P: обновление данных с билда 90 до 91
/// AI2P: аварийное завершение
/// SQLite Error 1: 'no such column: parent_id'
///   at AI2P.Storage.Database.Init
/// </code>
///
/// Причина не в самой колонке, а в ПОРЯДКЕ: индексы лежали в скрипте схемы и выполнялись
/// ДО миграций, а колонку <c>objects.parent_id</c> у СУЩЕСТВУЮЩЕЙ таблицы добавляет как раз
/// миграция v34 → v35. На чистой базе всё сходилось (колонку создаёт сама схема), поэтому
/// ни один тест беды не видел — а у человека приложение падало насмерть, и войти было нельзя.
///
/// Исправление: <see cref="Database.Init"/> выполняет схему → миграции → ИНДЕКСЫ, а списки
/// индексов вынесены из схемы отдельными константами. Тесты здесь держат обе стороны:
/// поведение (старая база поднимается) и договорённость (в схеме индексов больше нет).
/// </summary>
public sealed class Todo92Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo92-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>Каталог <c>AI2P_app</c> — по <c>AI2P.sln</c> рядом с ним.</summary>
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    /// <summary>
    /// Привести базу к виду билда 90: таблица <c>objects</c> без колонок правки v1.91,
    /// таблицы тэгов объектов нет вовсе. Ровно то, что лежало у человека на диске.
    /// </summary>
    private static void RollBackToBuild90(Database db)
    {
        using var conn = db.Open();
        // триггеры журнала перечисляют все колонки таблицы — пока они стоят, таблицу не тронуть
        ChangeLog.DropTriggers(conn);
        Sql.Exec(conn, null, "DROP TABLE object_tags");
        Sql.Exec(conn, null, "DROP TABLE objects");
        Sql.Exec(conn, null, """
            CREATE TABLE objects (
              id               TEXT PRIMARY KEY,
              display_id       TEXT NOT NULL,
              project_id       TEXT REFERENCES projects(id),
              type             TEXT NOT NULL DEFAULT 'file',
              path_or_url      TEXT NOT NULL DEFAULT '',
              meta_json        TEXT NOT NULL DEFAULT '{}',
              transport        TEXT,
              command_or_url   TEXT,
              tools_cache_json TEXT,
              rules_json       TEXT,
              created_at       TEXT NOT NULL,
              updated_at       TEXT NOT NULL,
              deleted_at       TEXT
            )
            """);
        Sql.Exec(conn, null, "INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '34')");
    }

    /// <summary>ВОСПРОИЗВЕДЕНИЕ: старт версии 1.92 поверх базы билда 90.</summary>
    [Fact]
    public void An_Old_Org_Database_Survives_The_Start_Of_A_New_Version()
    {
        var dir = Path.Combine(_dir, "org");
        var first = new Database(dir, "ai2p.db");
        first.Init("unid-S0");
        RollBackToBuild90(first);

        // ровно то, что делает OrgContext при первом старте после обновления
        var upgraded = new Database(dir, "ai2p.db");
        upgraded.Init("unid-S0");

        using var conn = upgraded.Open();
        var columns = ChangeLog.ColumnsOf(conn, "objects");
        Assert.Contains("parent_id", columns);
        Assert.Contains("name", columns);
        Assert.Contains("description", columns);
        Assert.Contains("is_active", columns);
        Assert.Contains("lora_path", columns);
        Assert.Contains("lora_status", columns);
        // v36 — пер-серверная активность локальных моделей, ai_model_servers (T-8-S1);
        // v37 — заявки на остановку с другого сервера, stop_requests (T-263)
        // v38 — обучение адаптера LoRA под модель, object_loras (T-12-S1);
        // v39 — заявки на смену дирижёра, conductor_requests (T-21-S1);
        // v40 — уведомления пользователя, notification_* (T-272)
        Assert.Equal("46", Sql.Scalar<string>(conn, null,
            "SELECT value FROM meta WHERE key='schema_version'"));

        // индекс по новой колонке — тот самый, на котором всё падало
        var indexes = Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='index'", r => r.S("name"));
        Assert.Contains("ix_objects_parent", indexes);
        Assert.Contains("ix_objects_project", indexes);
        Assert.Contains("ix_object_tags_tag", indexes);
        Assert.Contains("ix_tasks_parent", indexes);
    }

    /// <summary>Второй старт ничего не ломает: и схема, и миграции, и индексы идемпотентны.</summary>
    [Fact]
    public void The_Repeated_Start_Changes_Nothing()
    {
        var dir = Path.Combine(_dir, "again");
        var first = new Database(dir, "ai2p.db");
        first.Init("unid-S0");
        RollBackToBuild90(first);

        new Database(dir, "ai2p.db").Init("unid-S0");
        new Database(dir, "ai2p.db").Init("unid-S0");

        using var conn = first.Open();
        Assert.Contains("parent_id", ChangeLog.ColumnsOf(conn, "objects"));
    }

    /// <summary>Серверная БД идёт тем же порядком — её индексы тоже вынесены из схемы.</summary>
    [Fact]
    public void The_Server_Database_Keeps_Its_Indexes()
    {
        var dir = Path.Combine(_dir, "srv");
        var db = new Database(dir, "server.db", DatabaseKind.Server);
        db.Init("unid-S0");

        using var conn = db.Open();
        var indexes = Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='index'", r => r.S("name"));
        Assert.Contains("ix_accounts_email", indexes);
        Assert.Contains("ix_org_servers_s", indexes);
        Assert.Contains("ix_servers_token", indexes);
    }

    /// <summary>
    /// СТОРОЖ ДОГОВОРЁННОСТИ: в скриптах схемы индексов быть не должно — им место
    /// в <c>OrgIndexes</c> / <c>ServerIndexes</c>, которые выполняются после миграций.
    /// Иначе следующий индекс по новой колонке уронит следующее обновление тем же способом.
    /// </summary>
    [Fact]
    public void The_Schema_Scripts_Carry_No_Indexes()
    {
        var source = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Storage", "Database.cs"));
        foreach (var name in new[] { "OrgSchema", "ServerSchema" })
        {
            var body = ConstBody(source, name);
            Assert.DoesNotContain("CREATE INDEX", body);
        }
        // а в списках индексов они, наоборот, есть — иначе тест выше проверял бы пустоту
        Assert.Contains("CREATE INDEX", ConstBody(source, "OrgIndexes"));
        Assert.Contains("CREATE INDEX", ConstBody(source, "ServerIndexes"));
    }

    /// <summary>Текст многострочной константы <c>private const string имя = """…""";</c>.</summary>
    private static string ConstBody(string source, string name)
    {
        var start = source.IndexOf($"const string {name} =", StringComparison.Ordinal);
        Assert.True(start >= 0, $"в Database.cs нет константы {name}");
        var open = source.IndexOf("\"\"\"", start, StringComparison.Ordinal);
        var close = source.IndexOf("\"\"\";", open + 3, StringComparison.Ordinal);
        Assert.True(close > open, $"не найден конец константы {name}");
        return source[(open + 3)..close];
    }
}
