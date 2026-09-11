using AI2P.Connectors;
using AI2P.Server;
using AI2P.Server.Api;
using AI2P.Storage;
using System.Text.Json;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-228 (выпуск 1.85): КЛЮЧИ МОДЕЛЕЙ — ОДИН JSON НА КЛЮЧ В ОТДЕЛЬНОМ ПОДКАТАЛОГЕ.
///
/// Раньше всё лежало в одном <c>secrets.json</c>: ключи API моделей, ключ и токен источников
/// импорта, ключи организаций и учётка администратора сервера. Запись ОДНОГО ключа
/// перечитывала и переписывала файл целиком.
///
/// Теперь ключ модели и ключ источника импорта — это ОТДЕЛЬНЫЙ json в подкаталоге
/// <c>secrets/</c> рядом с config.json, по одному файлу на ССЫЛКУ (<c>secretRef</c>).
/// Ссылка остаётся адресом ключа, поэтому несколько моделей одного провайдера продолжают
/// делить один файл. В <c>secrets.json</c> остаётся то, что не про модели и не реплицируется
/// никогда: <c>orgKeys.*</c> и <c>server.*</c>.
///
/// Совместимость: старый файл продолжает читаться, а при старте его значения раскладываются
/// по файлам подкаталога (<see cref="SecretStore.MigrateFileToDir"/>). Порядок чтения —
/// подкаталог → secrets.json → переменная окружения.
/// </summary>
public sealed class T228Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t228-" + Guid.NewGuid().ToString("N"));

    public T228Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Environment.SetEnvironmentVariable("AI2PT228_API_KEY", null);
    }

    private string FilePath => Path.Combine(_dir, "secrets.json");

    private SecretStore Store(string? json = null)
    {
        if (json is not null)
        {
            File.WriteAllText(FilePath, json);
        }
        return new SecretStore(FilePath);
    }

    /// <summary>Значение из файла ключа так, как его увидит человек, открывший файл.</summary>
    private static (string Ref, string Value) ReadKeyFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return (doc.RootElement.GetProperty("ref").GetString()!,
            doc.RootElement.GetProperty("value").GetString()!);
    }

    // ---------- 1. один ключ — один файл ----------

    [Fact]
    public void A_Model_Key_Goes_Into_Its_Own_File_In_The_Subdirectory()
    {
        var store = Store();

        store.Write("anthropic.apiKey", "sk-ant-1");

        var path = Path.Combine(_dir, SecretStore.DirName, "anthropic.apikey.json");
        Assert.Equal(path, store.PathOf("anthropic.apiKey"));
        Assert.True(File.Exists(path));
        // в файле лежит ИСХОДНАЯ ссылка (имя файла приведено к нижнему регистру) и значение
        Assert.Equal(("anthropic.apiKey", "sk-ant-1"), ReadKeyFile(path));
        Assert.Equal("sk-ant-1", store.Read("anthropic.apiKey"));
        // общий файл при этом не заводится вовсе
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Writing_One_Key_Does_Not_Rewrite_The_Others()
    {
        var store = Store();
        store.Write("anthropic.apiKey", "sk-ant");
        store.Write("deepseek.apiKey", "sk-deep");
        var before = File.ReadAllBytes(store.PathOf("anthropic.apiKey"));

        store.Write("openrouter.apiKey", "sk-or");
        store.Write("deepseek.apiKey", "sk-deep-2");

        // чужой файл не переписан ни при добавлении нового ключа, ни при правке соседнего
        Assert.Equal(before, File.ReadAllBytes(store.PathOf("anthropic.apiKey")));
        Assert.Equal("sk-ant", store.Read("anthropic.apiKey"));
        Assert.Equal("sk-deep-2", store.Read("deepseek.apiKey"));
        Assert.Equal("sk-or", store.Read("openrouter.apiKey"));
        // и в каждом файле лежит ТОЛЬКО свой ключ
        Assert.DoesNotContain("sk-deep", File.ReadAllText(store.PathOf("anthropic.apiKey")));
        Assert.Equal(3, Directory.GetFiles(store.Dir, "*.json").Length);
    }

    /// <summary>Файл заводится на ССЫЛКУ: две модели одного провайдера делят один ключ.</summary>
    [Fact]
    public void Models_Of_One_Provider_Share_One_File_Because_They_Share_The_Ref()
    {
        var store = Store();
        store.Write("anthropic.apiKey", "sk-общий");

        Assert.Equal(store.PathOf("anthropic.apiKey"), store.PathOf("anthropic.apiKey"));
        Assert.Single(Directory.GetFiles(store.Dir, "*.json"));
        Assert.Equal("sk-общий", store.Read("anthropic.apiKey"));
    }

    // ---------- 2. что остаётся в secrets.json ----------

    [Fact]
    public void Organization_Keys_And_The_Server_Admin_Stay_In_Secrets_Json()
    {
        var store = Store();

        new OrgSecretKey(store).Ensure("ORG-1");
        store.Write(Ai2pAuth.AdminHashRef, "хэш");
        store.Write(Ai2pAuth.AdminLoginRef, "admin");

        Assert.True(File.Exists(FilePath));
        var text = File.ReadAllText(FilePath);
        Assert.Contains(OrgSecretKey.Section, text);
        Assert.Contains("adminPasswordHash", text);
        // в подкаталоге ключей их нет
        Assert.False(Directory.Exists(store.Dir));
        Assert.Equal("хэш", store.Read(Ai2pAuth.AdminHashRef));
        Assert.True(new OrgSecretKey(store).Has("ORG-1"));
    }

    /// <summary>Признак «это секрет сервера» держится на тех же константах, что и код.</summary>
    [Fact]
    public void Server_Secrets_Are_Recognized_By_Their_Real_Refs()
    {
        Assert.True(SecretStore.IsServerSecret(OrgSecretKey.RefOf("ORG-1")));
        Assert.True(SecretStore.IsServerSecret(Ai2pAuth.AdminLoginRef));
        Assert.True(SecretStore.IsServerSecret(Ai2pAuth.AdminHashRef));
        // ключи моделей и источников импорта — не секреты сервера
        Assert.False(SecretStore.IsServerSecret("anthropic.apiKey"));
        Assert.False(SecretStore.IsServerSecret("trello.apiKey"));
        Assert.False(SecretStore.IsServerSecret("trello.token"));
    }

    // ---------- 3. порядок источников ----------

    [Fact]
    public void Reading_Order_Is_Subdirectory_Then_File_Then_Environment()
    {
        Environment.SetEnvironmentVariable("AI2PT228_API_KEY", "из-окружения");
        var store = Store("""{ "ai2pt228.apiKey": "из-файла" }""");

        // окружение — последнее: файл впереди него
        Assert.Equal("из-файла", store.Read("ai2pt228.apiKey"));
        Assert.StartsWith("secrets.json", store.ResolveWithSource("ai2pt228.apiKey").Source);

        store.Write("ai2pt228.apiKey", "из-подкаталога");

        Assert.Equal("из-подкаталога", store.Read("ai2pt228.apiKey"));
        Assert.StartsWith(SecretStore.DirName + "/", store.ResolveWithSource("ai2pt228.apiKey").Source);
        // старое значение в файле осталось нетронутым — файл писала не эта запись
        Assert.Contains("из-файла", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_Environment_Variable_Still_Works()
    {
        Environment.SetEnvironmentVariable("AI2PT228_API_KEY", "из-окружения");
        var store = Store();   // ни файла, ни подкаталога

        Assert.Equal("из-окружения", store.Read("ai2pt228.apiKey"));
        Assert.Equal("env:AI2PT228_API_KEY", store.ResolveWithSource("ai2pt228.apiKey").Source);
        // и оно НЕ считается «значением из хранилища»: перенос в организацию его не трогает
        Assert.Null(store.ReadStored("ai2pt228.apiKey"));
    }

    // ---------- 4. перенос старого secrets.json ----------

    [Fact]
    public void Startup_Moves_Model_And_Import_Keys_And_Leaves_The_Server_Ones()
    {
        var store = Store("""
            {
              "anthropic": { "apiKey": "sk-ant" },
              "deepseek.apiKey": "sk-deep",
              "trello": { "apiKey": "trello-ключ", "token": "trello-токен" },
              "orgKeys": { "ORG-1": "ключ-организации" },
              "server.adminPasswordHash": "хэш"
            }
            """);

        Assert.Equal(4, store.MigrateFileToDir());

        // ключи моделей и источников импорта — в подкаталоге, по одному файлу на ссылку
        foreach (var (secretRef, value) in new[]
                 {
                     ("anthropic.apiKey", "sk-ant"), ("deepseek.apiKey", "sk-deep"),
                     ("trello.apiKey", "trello-ключ"), ("trello.token", "trello-токен"),
                 })
        {
            Assert.True(File.Exists(store.PathOf(secretRef)));
            Assert.Equal((secretRef, value), ReadKeyFile(store.PathOf(secretRef)));
            Assert.Equal(value, store.Read(secretRef));
        }
        Assert.Equal(4, Directory.GetFiles(store.Dir, "*.json").Length);

        // в старом файле от них не осталось ничего, а секреты сервера не тронуты
        var text = File.ReadAllText(FilePath);
        Assert.DoesNotContain("sk-ant", text);
        Assert.DoesNotContain("trello", text);
        Assert.Contains("ключ-организации", text);
        Assert.Contains("хэш", text);
        Assert.Equal("ключ-организации", store.Read(OrgSecretKey.RefOf("ORG-1")));
        Assert.Equal("хэш", store.Read(Ai2pAuth.AdminHashRef));
    }

    /// <summary>Пояснение человека в файле (<c>_comment</c> шаблона secrets.example.json)
    /// ключом не является и в подкаталог не переезжает.</summary>
    [Fact]
    public void A_Comment_Line_Is_Not_A_Key()
    {
        var store = Store("""
            { "_comment": "шаблон файла секретов", "anthropic": { "apiKey": "sk-ant" } }
            """);

        Assert.Equal(1, store.MigrateFileToDir());

        Assert.Single(Directory.GetFiles(store.Dir, "*.json"));
        Assert.Contains("_comment", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_Move_Is_Idempotent_And_A_Restart_Spoils_Nothing()
    {
        var store = Store("""
            {
              "anthropic": { "apiKey": "sk-ant" },
              "orgKeys": { "ORG-1": "ключ-организации" }
            }
            """);
        Assert.Equal(1, store.MigrateFileToDir());
        var file = File.ReadAllText(FilePath);
        var key = File.ReadAllText(store.PathOf("anthropic.apiKey"));

        // второй и третий старт: переносить больше нечего, файлы не меняются
        Assert.Equal(0, store.MigrateFileToDir());
        Assert.Equal(0, new SecretStore(FilePath).MigrateFileToDir());

        Assert.Equal(file, File.ReadAllText(FilePath));
        Assert.Equal(key, File.ReadAllText(store.PathOf("anthropic.apiKey")));
        Assert.Equal("sk-ant", store.Read("anthropic.apiKey"));
    }

    /// <summary>Ссылка, у которой файл уже есть, не переносится и из secrets.json не убирается:
    /// значение подкаталога и так главнее, а терять вписанное руками нельзя.</summary>
    [Fact]
    public void A_Ref_That_Already_Has_A_File_Is_Left_Alone()
    {
        var store = Store("""{ "anthropic": { "apiKey": "из-файла" } }""");
        store.Write("anthropic.apiKey", "из-подкаталога");

        Assert.Equal(0, store.MigrateFileToDir());

        Assert.Equal("из-подкаталога", store.Read("anthropic.apiKey"));
        Assert.Contains("из-файла", File.ReadAllText(FilePath));
    }

    [Fact]
    public void A_Damaged_Secrets_File_Does_Not_Break_The_Start()
    {
        var store = Store("{ это не json ");

        Assert.Equal(0, store.MigrateFileToDir());   // ошибки нет, старт продолжается
        Assert.False(Directory.Exists(store.Dir));
    }

    // ---------- 5. имя файла из ссылки ----------

    [Fact]
    public void The_File_Name_Comes_From_The_Ref_And_Ignores_Case()
    {
        Assert.Equal("anthropic.apikey.json", SecretStore.FileNameOf("anthropic.apiKey"));
        // регистр ссылки задан ЯВНО: на Windows файловая система регистронезависима, и
        // разное поведение систем на одном и том же secrets.json недопустимо
        Assert.Equal(SecretStore.FileNameOf("anthropic.apiKey"),
            SecretStore.FileNameOf("ANTHROPIC.APIKEY"));
        Assert.Equal(SecretStore.FileNameOf("anthropic.apiKey"),
            SecretStore.FileNameOf("  anthropic.apiKey  "));
    }

    [Fact]
    public void An_Impossible_Ref_Turns_Into_A_Safe_Unique_Name()
    {
        var names = new List<string>();
        foreach (var secretRef in new[]
                 {
                     "a/b", "a\\b", "a:b", "a*b", "a b", "..", "",
                     "очень.длинная.ссылка".PadRight(200, 'x'),
                 })
        {
            var name = SecretStore.FileNameOf(secretRef);
            Assert.EndsWith(".json", name);
            Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, name);
            Assert.False(name.Contains('/') || name.Contains('\\'), name);
            Assert.True(name.Length is > 5 and <= 100, name);
            names.Add(name);
        }
        // «a/b», «a\b», «a:b» после очистки символов совпали бы — их разводит хвост из хэша
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Имена устройств Windows (nul, con, com1) файлом быть не могут даже с расширением.</summary>
    [Fact]
    public void Reserved_Windows_Names_Are_Not_Used_As_Is()
    {
        foreach (var reserved in new[] { "nul", "con", "prn", "aux", "com1", "lpt9" })
        {
            var name = SecretStore.FileNameOf(reserved);
            Assert.NotEqual(reserved + ".json", name);
            Assert.EndsWith(".json", name);
        }
        // «nul.apiKey» — тоже устройство: Windows смотрит на часть до первой точки
        Assert.StartsWith("_nul", SecretStore.FileNameOf("nul.apiKey"));
    }

    [Fact]
    public void A_Weird_Ref_Is_Written_And_Read_Back()
    {
        var store = Store();
        const string weird = "мой провайдер/ключ:1*";

        store.Write(weird, "значение");

        Assert.Equal("значение", store.Read(weird));
        Assert.Single(Directory.GetFiles(store.Dir, "*.json"));
        // исходная ссылка сохранена внутри файла — по имени файла её не восстановить
        Assert.Equal((weird, "значение"), ReadKeyFile(store.PathOf(weird)));
    }

    // ---------- 6. права на файлы ----------

    /// <summary>0600 на КАЖДОМ файле подкаталога и 0700 на самом подкаталоге (T-135, T-228).
    /// На Windows права задаёт ACL — проверять нечего.</summary>
    [Fact]
    public void Every_Key_File_Is_Readable_Only_By_The_Owner()
    {
        var store = Store();
        store.Write("anthropic.apiKey", "sk-1");
        store.Write("deepseek.apiKey", "sk-2");

        if (OperatingSystem.IsWindows())
        {
            return;
        }
        foreach (var file in Directory.GetFiles(store.Dir, "*.json"))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(file));
        }
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            new DirectoryInfo(store.Dir).UnixFileMode);
    }

    // ---------- 7. подкаталог не едет ни в репликацию, ни в установку ----------

    /// <summary>Подкаталог ключей лежит рядом с config.json, а НЕ внутри каталога данных —
    /// поэтому в реплицируемый каталог организации он не попадает в принципе.</summary>
    [Fact]
    public void The_Key_Directory_Is_Outside_Everything_That_Replicates()
    {
        var install = Path.Combine(_dir, "install");
        Directory.CreateDirectory(install);
        var configPath = Path.Combine(install, "config.json");
        var config = new Ai2pConfig();
        var dataDir = Path.GetFullPath(config.ResolveDir(configPath, config.Storage.DataDir));
        var store = new SecretStore(Path.Combine(install, "secrets.json"));
        store.Write("anthropic.apiKey", "sk-1");

        Assert.False(Path.GetFullPath(store.Dir)
            .StartsWith(dataDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        // и обход реплицируемого каталога организации его не видит
        var orgDir = Path.Combine(dataDir, "orgs", "ORG-1");
        Directory.CreateDirectory(orgDir);
        File.WriteAllText(Path.Combine(orgDir, "задача.md"), "текст");
        var entries = FileManifest.Scan(orgDir, RepIgnore.Empty, skipService: true);

        Assert.Single(entries);
        Assert.DoesNotContain(entries, e => e.Path.Contains(SecretStore.DirName, StringComparison.Ordinal));
    }

    [Fact]
    public void The_Installers_Never_Touch_The_Key_Directory()
    {
        var ps = Script("install.ps1");
        var sh = Script("install.sh");

        // не удаляется как «устаревшее» и не копируется из выкладки (а значит, и в опись
        // installed.json не попадает — опись строится из того же списка)
        Assert.Contains("$keep = @(\"data\", \"logs\", \"secrets.json\", \"secrets\", \"config.json\")", ps);
        Assert.Contains("$skip = @(\"data\", \"logs\", \"secrets.json\", \"secrets\")", ps);
        Assert.Contains("-e '^secrets/'", sh);
    }

    [Fact]
    public void The_Publisher_Never_Touches_The_Key_Directory()
    {
        Assert.Contains("$keep = @(\"data\", \"logs\", \"config.json\", \"secrets.json\", \"secrets\")",
            Script("buildRelease.ps1"));
        Assert.Contains("data|logs|config.json|secrets.json|secrets)", Script("buildRelease.sh"));
    }

    /// <summary>Каталог <c>AI2P_app</c>: рядом с ним лежат скрипты сборки и установки.</summary>
    private static string Script(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return File.ReadAllText(Path.Combine(dir.FullName, name));
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }
}
