using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Server;
using AI2P.Storage;
using AI2P.Storage.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-183: ИНКРЕМЕНТНАЯ УСТАНОВКА — обновление ЧЕРЕЗ несколько пропущенных версий.
///
/// Правило простое: выкладка полная, а не заплатка к предыдущей, поэтому 1.79 ставится
/// поверх 1.70 ровно так же, как поверх 1.78. Отсюда три места, где «прыжок» обязан
/// отработать при ПЕРВОМ старте новой версии:
/// <list type="number">
/// <item>шаги обновления данных (<see cref="Upgrade"/>) — выполняются ВСЕ шаги
///   промежуточных билдов, по возрастанию;</item>
/// <item>схема БД (<see cref="Database"/>) — идемпотентные миграции доводят базу любой
///   давности, стоящая в <c>meta</c> версия схемы им не указ;</item>
/// <item>конфигурация (<see cref="ConfigMerge"/>) — параметры, появившиеся в пропущенных
///   версиях, приходят с умолчаниями дистрибутива, значения человека остаются.</item>
/// </list>
/// Плюс сама установка: устаревшие файлы прошлых версий убираются по описи (installed.json).
/// </summary>
public sealed class T183Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t183-" + Guid.NewGuid().ToString("N"));

    public T183Tests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Каталог <c>AI2P_app</c>: рядом с ним лежат скрипты установки.</summary>
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

    // --- шаги обновления через несколько версий ---

    [Fact]
    public void Skipping_Many_Versions_Runs_Every_Step_In_Order()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        // установка версии 1.60, человек пропустил всё до текущей
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "60");
        var ran = new List<int>();
        var steps = new[]
        {
            new Upgrade.Step(59, "до неё", _ => ran.Add(59)),
            new Upgrade.Step(60, "она сама", _ => ran.Add(60)),
            new Upgrade.Step(61, "пропущенная версия", _ => ran.Add(61)),
            new Upgrade.Step(66, "пропущенная версия", _ => ran.Add(66)),
            new Upgrade.Step(73, "пропущенная версия", _ => ran.Add(73)),
            new Upgrade.Step(AppInfo.Build, "текущая", _ => ran.Add(AppInfo.Build)),
        };

        var done = Upgrade.Run(registry, steps);

        // ни один шаг промежуточной версии не потерян, порядок — по возрастанию билда
        Assert.Equal(4, done);
        Assert.Equal([61, 66, 73, AppInfo.Build], ran);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void Old_Install_Without_The_Build_Mark_Runs_Every_Step()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        // установка старше билда 46: данные есть, а отметки билда в них нет
        registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "petr123", IsActive = true,
        }, null);
        Assert.Null(registry.ServerDb.Meta(Upgrade.BuildKey));
        var ran = new List<int>();
        var steps = new[]
        {
            new Upgrade.Step(46, "первый", _ => ran.Add(46)),
            new Upgrade.Step(73, "второй", _ => ran.Add(73)),
        };

        var done = Upgrade.Run(registry, steps);

        // раньше такая установка считалась чистой и МОЛЧА пропускала все шаги разом
        Assert.Equal(2, done);
        Assert.Equal([46, 73], ran);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void Fresh_Install_Is_Told_Apart_From_An_Old_One()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        // запись о локальном сервере есть и на чистой установке — признаком она быть не может
        registry.EnsureLocalServer("этот", "http", "localhost", 5480, "/ai2p");
        Assert.True(Upgrade.IsFreshInstall(registry));

        registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "petr123", IsActive = true,
        }, null);

        Assert.False(Upgrade.IsFreshInstall(registry));
    }

    [Fact]
    public void Fresh_Install_Still_Runs_Nothing()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        var ran = new List<int>();

        var done = Upgrade.Run(registry, [new Upgrade.Step(46, "древний шаг", _ => ran.Add(46))]);

        Assert.Equal(0, done);
        Assert.Empty(ran);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void A_Half_Done_Jump_Continues_From_Where_It_Stopped()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "petr123", IsActive = true,
        }, null);
        var ran = new List<int>();
        var broken = new[]
        {
            new Upgrade.Step(61, "первый", _ => ran.Add(61)),
            new Upgrade.Step(66, "падает", _ => throw new InvalidOperationException("сбой")),
            new Upgrade.Step(73, "третий", _ => ran.Add(73)),
        };

        // упавший шаг останавливает старт: работать на доведённых до середины данных нельзя
        Assert.Throws<InvalidOperationException>(() => Upgrade.Run(registry, broken));
        Assert.Equal("61", registry.ServerDb.Meta(Upgrade.BuildKey));

        var fixed_ = new[]
        {
            new Upgrade.Step(61, "первый", _ => ran.Add(61)),
            new Upgrade.Step(66, "починен", _ => ran.Add(66)),
            new Upgrade.Step(73, "третий", _ => ran.Add(73)),
        };
        // повтор не делает заново того, что уже удалось
        Assert.Equal(2, Upgrade.Run(registry, fixed_));
        Assert.Equal([61, 66, 73], ran);
    }

    [Fact]
    public void Real_Steps_Run_All_At_Once_On_A_Jump_From_The_Very_Beginning()
    {
        // не выдуманные шаги, а объявленные в Upgrade.Steps: прыжок с самого начала
        // выполняет их ВСЕ подряд — значит каждый обязан выдерживать данные любой давности
        // (и повторный запуск: идемпотентность)
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.EnsureLocalServer("этот", "http", "localhost", 5480, "/ai2p");
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "0");

        // шаг ГОТОВЯЩЕГОСЯ выпуска (его номер билда выше текущего) не выполняется, пока
        // билд не подняли, — так и задумано (T-185: шаг 80 живёт в дереве при билде 79).
        // Поэтому ожидается не длина списка, а число шагов, дозревших до текущего билда
        var due = Upgrade.Steps.Count(s => s.Build <= AppInfo.Build);

        var done = Upgrade.Run(registry);

        Assert.Equal(due, done);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));

        // повтор с той же отметки — те же шаги ещё раз, и снова без ошибок
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "0");
        Assert.Equal(due, Upgrade.Run(registry));
    }

    // --- схема базы любой давности ---

    [Fact]
    public void A_Database_Of_An_Old_Version_Is_Brought_Up_By_Init()
    {
        // база «старой версии»: таблица чата без колонок, добавленных в v26 (T-152),
        // и с версией схемы, которой она себя считает
        var dir = Path.Combine(_dir, "oldbase");
        Directory.CreateDirectory(dir);
        var db = new Database(dir, "ai2p.db");
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(dir, "ai2p.db")};Pooling=false"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta(key, value) VALUES ('schema_version', '10');
                CREATE TABLE chat_messages (
                  id TEXT PRIMARY KEY, task_id TEXT NOT NULL, body TEXT NOT NULL DEFAULT '',
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                INSERT INTO chat_messages(id, task_id, body, created_at, updated_at)
                VALUES ('m1', 't1', 'сообщение из прошлой версии', '2020-01-01', '2020-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        db.Init();

        using var check = db.Open();
        var columns = Sql.Query(check, null, "PRAGMA table_info(chat_messages)", r => r.S("name"));
        // колонки всех пропущенных версий доехали за один вызов — миграции идемпотентны
        // и не смотрят на записанную в meta версию схемы
        Assert.Contains("kind", columns);
        Assert.Contains("author_name", columns);
        Assert.Contains("external_ref", columns);
        // данные при этом на месте
        Assert.Equal("сообщение из прошлой версии", Sql.Scalar<string>(check, null,
            "SELECT body FROM chat_messages WHERE id='m1'"));
        // v31 — тэги задач (T-222); v32 — замена исполнителя (T-221);
        // v33 — ссылка импорта у задачи (T-246); v34 — статус по готовности (T-250)
        // v35 — объекты проекта (T-259); v36 — пер-серверная активность моделей (T-8-S1);
        // v37 — заявки на остановку с другого сервера, stop_requests (T-263);
        // v38 — обучение адаптера LoRA (T-12-S1); v39 — смена дирижёра заявкой (T-21-S1)
        Assert.Equal("46", Sql.Scalar<string>(check, null, "SELECT value FROM meta WHERE key='schema_version'"));

        // повторный старт той же версии ничего не ломает
        db.Init();
    }

    // --- конфигурация через пропущенные версии ---

    [Fact]
    public void Parameters_Of_Every_Skipped_Version_Arrive_With_Defaults()
    {
        // config.json человека остался от 1.70, дистрибутив — от текущей версии: в нём есть
        // и то, что добавили в 1.74, и то, что в 1.78
        var old = """
            { "ui": { "port": 5999, "language": "ru" }, "storage": { "dataDir": "d:/ai2p" } }
            """;
        var shipped = """
            { "ui": { "port": 5480, "language": "en", "hostname": "localhost" },
              "storage": { "dataDir": "data" }, "logging": { "level": "Warning" } }
            """;

        var merged = ConfigMerge.Merge(shipped, old);

        // значения человека победили
        Assert.Contains("5999", merged);
        Assert.Contains("d:/ai2p", merged);
        // а параметры пропущенных версий пришли с умолчаниями дистрибутива
        Assert.Contains("hostname", merged);
        Assert.Contains("Warning", merged);
    }

    [Fact]
    public void A_Second_Install_Before_The_First_Start_Does_Not_Lose_Anything()
    {
        // так бывает при прыжке через версию: поставили 1.74, не запустили, поставили 1.78 —
        // рядом лежит config.new.json от 1.78, и слить надо именно его
        var configPath = Path.Combine(_dir, "config.json");
        File.WriteAllText(configPath, """{ "ui": { "port": 5999 } }""");
        var incoming = Path.Combine(_dir, ConfigMerge.NewConfigName);
        File.WriteAllText(incoming, """{ "ui": { "port": 5480, "hostname": "localhost" } }""");
        // вторая установка кладёт свой файл поверх непринятого — так делает install.ps1
        File.WriteAllText(incoming, """{ "ui": { "port": 5480, "hostname": "localhost" }, "logging": { "level": "Warning" } }""");

        Assert.True(ConfigMerge.ApplyPending(configPath));

        var merged = File.ReadAllText(configPath);
        Assert.Contains("5999", merged);
        Assert.Contains("hostname", merged);
        Assert.Contains("Warning", merged);
        Assert.False(File.Exists(incoming));
        Assert.True(File.Exists(incoming + ConfigMerge.AppliedSuffix));
        // третий старт уже ничего не сливает
        Assert.False(ConfigMerge.ApplyPending(configPath));
    }

    // --- опись установки: устаревшие файлы прошлых версий ---

    [Fact]
    public void Both_Installers_Keep_An_Inventory_Of_What_They_Put_There()
    {
        var ps = File.ReadAllText(Path.Combine(AppDir(), "install.ps1"));
        var sh = File.ReadAllText(Path.Combine(AppDir(), "install.sh"));

        // опись нужна обеим системам и называется одинаково: установку переносят между ними
        Assert.Contains("installed.json", ps);
        Assert.Contains("installed.json", sh);
        // каталоги, целиком принадлежащие дистрибутиву, переписываются начисто
        foreach (var dir in new[] { "doc", "wwwroot", "runtimes" })
        {
            Assert.Contains(dir, ps);
            Assert.Contains(dir, sh);
        }
    }

    [Fact]
    public void The_Inventory_Never_Lists_User_Data()
    {
        var ps = File.ReadAllText(Path.Combine(AppDir(), "install.ps1"));
        var sh = File.ReadAllText(Path.Combine(AppDir(), "install.sh"));

        // данные пользователя не входят в опись и потому не могут быть удалены как «устаревшие»
        // secrets — подкаталог с ключами моделей, по одному json на ключ (T-228)
        Assert.Contains("$keep = @(\"data\", \"logs\", \"secrets.json\", \"secrets\", \"config.json\")", ps);
        Assert.Contains("secrets\\.json", sh);
        Assert.Contains("config\\.json", sh);
    }

    /// <summary>Реестр организаций поверх временного каталога тестовой базы.</summary>
    private static AI2P.Server.Org.OrgRegistry TestRegistry(StorageFixture fixture) =>
        new(Path.Combine(fixture.Dir, "registry"), "server.db", new AI2P.Server.Org.OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: fixture.Secrets,
            LocalModels: fixture.LocalModels,
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => fixture.Dir,
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => true,
            LocalServerId: () => "local",
            LocalCode: _ => "",
            ServerCode: (_, _) => "",
            ServerName: _ => ""));
}
