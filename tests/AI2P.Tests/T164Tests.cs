using AI2P.Core.Api;
using AI2P.Server;
using AI2P.Server.Api;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-164: ПЕРВИЧНАЯ УСТАНОВКА — РЕПОЗИТОРИЙ МОДЕЛЕЙ ПОД ПЛАТФОРМУ.
///
/// На Ubuntu после первичной установки в конфигурации оказывался виндовый каталог
/// репозитория моделей — <c>C:\ai</c>. Причина не в коде умолчания (оно платформенное
/// с T-135), а в том, что значение было ЗАШИТО в <c>config.json</c> дистрибутива: файл
/// копируется установкой как есть, а при обновлении рабочие значения пользователя
/// (<see cref="ConfigMerge"/>) ложатся поверх новых умолчаний — то есть однажды попавший
/// в файл <c>C:\ai</c> сам оттуда уже не уходил.
///
/// Лечится с двух сторон:
/// 1) в <c>config.json</c> дистрибутива каталога больше нет вовсе — умолчание определяет
///    приложение по системе (Windows — <c>C:\ai</c>, Linux/macOS — <c>~/ai</c>);
/// 2) при старте конфигурация приводится к платформе (<see cref="Ai2pConfig.NormalizePlatformPaths"/>):
///    путь другой системы заменяется умолчанием и записывается в config.json — это чинит
///    и уже установленные экземпляры, и конфигурацию, привезённую с другого компьютера.
/// </summary>
public sealed class T164Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t164-" + Guid.NewGuid().ToString("N"));

    public T164Tests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    /// <summary>Путь этой системы и путь чужой — для проверок, идущих на обеих платформах.</summary>
    private static string OwnPath => OperatingSystem.IsWindows() ? @"D:\models" : "/opt/models";

    private static string ForeignPath => OperatingSystem.IsWindows() ? "/home/mike/ai" : @"C:\ai";

    // ---------- 1. умолчание ----------

    /// <summary>
    /// УМОЛЧАНИЕ ПЕРЕПИСАНО В T-291: репозиторий моделей больше не абсолютный путь платформы
    /// (<c>C:\ai</c> / <c>~/ai</c>), а относительный <c>./models</c> — каталог рядом
    /// с установкой. Задача T-164 при этом решена по-прежнему и даже прочнее: относительный
    /// путь чужим не бывает вовсе, а пример пути своей системы остался для сообщений
    /// (<see cref="Ai2pConfig.StorageSettings.SamplePath()"/>).
    /// </summary>
    [Fact]
    public void Default_Models_Repo_Is_Relative_To_The_Install()
    {
        Assert.Equal("./models", Ai2pConfig.StorageSettings.DefaultModelsRepo);
        Assert.Equal("./distribs", Ai2pConfig.StorageSettings.DefaultDistDir);
        Assert.Equal("./packages", Ai2pConfig.StorageSettings.DefaultPackagesDir);
        // умолчание самого объекта настроек — то же самое
        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, new Ai2pConfig().Storage.ModelsRepo);
        // пример пути для сообщений по-прежнему платформенный
        Assert.Equal(OperatingSystem.IsWindows() ? @"C:\ai" : "~/ai",
            Ai2pConfig.StorageSettings.SamplePath());
    }

    // ---------- 2. распознавание чужого пути ----------

    [Fact]
    public void Foreign_Path_Is_Recognized()
    {
        Assert.True(Ai2pConfig.StorageSettings.IsForeignPath(ForeignPath));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(OwnPath));
        // домашний каталог понимают обе системы, пустое значение — это «умолчание»
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath("~/ai"));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(""));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(null));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(Ai2pConfig.StorageSettings.DefaultModelsRepo));
        // сетевой путь Windows на Linux тоже чужой, а на Windows свой — в обоих написаниях
        Assert.Equal(!OperatingSystem.IsWindows(),
            Ai2pConfig.StorageSettings.IsForeignPath(@"\\сервер\общий"));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath("//сервер/общий"));
    }

    /// <summary>Обе системы разбираются на любой из них: ломалось это на Ubuntu, а прогон
    /// тестов идёт на Windows — без явно заданной системы Linux-ветка не проверялась бы.</summary>
    [Theory]
    // на Linux/macOS чужое — буква диска и сетевой путь Windows
    [InlineData(false, @"C:\ai", true)]
    [InlineData(false, @"c:/ai", true)]
    [InlineData(false, @"\\сервер\общий", true)]
    [InlineData(false, "/home/mike/ai", false)]
    [InlineData(false, "~/ai", false)]
    // на Windows чужое — путь от корня POSIX
    [InlineData(true, "/home/mike/ai", true)]
    [InlineData(true, @"C:\ai", false)]
    [InlineData(true, @"\\сервер\общий", false)]
    [InlineData(true, "~/ai", false)]
    public void Foreign_Path_Is_Recognized_For_Both_Systems(bool windows, string path, bool foreign)
    {
        Assert.Equal(foreign, Ai2pConfig.StorageSettings.IsForeignPath(path, windows));
    }

    /// <summary>ТОТ САМЫЙ случай T-164: конфигурация с виндовым <c>C:\ai</c> на Ubuntu.
    /// С T-291 такой путь заменяется относительным умолчанием — годным обеим системам.</summary>
    [Fact]
    public void Windows_Repo_On_Linux_Becomes_The_Default()
    {
        var config = new Ai2pConfig();
        config.Storage.ModelsRepo = @"C:\ai";

        var changes = config.NormalizePlatformPaths(windows: false);

        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, config.Storage.ModelsRepo);
        Assert.Single(changes);
        Assert.Contains(@"C:\ai", changes[0]);
        // и на Windows тот же путь остаётся нетронутым: человек задал его сам
        var windows = new Ai2pConfig();
        windows.Storage.ModelsRepo = @"C:\ai";
        Assert.Empty(windows.NormalizePlatformPaths(windows: true));
        Assert.Equal(@"C:\ai", windows.Storage.ModelsRepo);
    }

    /// <summary>Конфигурация дистрибутива годится обеим системам (T-291): каталоги в ней
    /// ОТНОСИТЕЛЬНЫЕ, поэтому один и тот же файл верен и на Windows, и на Linux —
    /// то самое требование T-164, только решённое иначе.</summary>
    [Fact]
    public void Shipped_Config_Fits_Both_Systems()
    {
        var shipped = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "config.json"));

        Assert.Contains("\"modelsRepo\": \"./models\"", shipped, StringComparison.Ordinal);
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath("./models", windows: true));
        Assert.False(Ai2pConfig.StorageSettings.IsForeignPath("./models", windows: false));
        Assert.Equal("~/ai", Ai2pConfig.StorageSettings.SamplePath(windows: false));
        Assert.Equal(@"C:\ai", Ai2pConfig.StorageSettings.SamplePath(windows: true));
    }

    // ---------- 3. приведение конфигурации к платформе ----------

    [Fact]
    public void Foreign_Models_Repo_Is_Replaced_With_System_Default()
    {
        var config = new Ai2pConfig();
        config.Storage.ModelsRepo = ForeignPath;

        var changes = config.NormalizePlatformPaths();

        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, config.Storage.ModelsRepo);
        Assert.Single(changes);
        Assert.Contains(ForeignPath, changes[0]);
    }

    [Fact]
    public void Empty_Models_Repo_Gets_The_System_Default()
    {
        var config = new Ai2pConfig();
        config.Storage.ModelsRepo = "   ";

        Assert.Single(config.NormalizePlatformPaths());
        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, config.Storage.ModelsRepo);
    }

    [Fact]
    public void Own_Paths_Are_Left_Alone()
    {
        var config = new Ai2pConfig();
        config.Storage.ModelsRepo = OwnPath;
        config.Storage.DistDir = "";
        config.Storage.PackagesDir = "~/ai/packages";

        Assert.Empty(config.NormalizePlatformPaths());
        Assert.Equal(OwnPath, config.Storage.ModelsRepo);
        Assert.Equal("~/ai/packages", config.Storage.PackagesDir);
        // умолчание платформы тоже не считается правкой — иначе config.json переписывался
        // бы на каждом старте
        config.Storage.ModelsRepo = Ai2pConfig.StorageSettings.DefaultModelsRepo;
        Assert.Empty(config.NormalizePlatformPaths());
    }

    /// <summary>Каталоги дистрибутивов и пакетов необязательны: чужой путь у них не
    /// заменяется, а очищается — пусто у них и значит «умолчание».</summary>
    [Fact]
    public void Foreign_Package_Dirs_Are_Cleared()
    {
        var config = new Ai2pConfig();
        config.Storage.DistDir = ForeignPath;
        config.Storage.PackagesDir = ForeignPath;

        var changes = config.NormalizePlatformPaths();

        Assert.Equal("", config.Storage.DistDir);
        Assert.Equal("", config.Storage.PackagesDir);
        Assert.Equal(2, changes.Count);
    }

    // ---------- 4. дистрибутив и обновление ----------

    /// <summary>В <c>config.json</c> дистрибутива нет АБСОЛЮТНОГО пути платформы: файл один
    /// на все системы. Каталоги в нём относительные (T-291) — это законно и проверяется
    /// отдельно (<see cref="Shipped_Config_Fits_Both_Systems"/>).</summary>
    [Fact]
    public void Shipped_Config_Has_No_Hardcoded_Windows_Repo()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "config.json"),
                     // выкладка: publish кладёт config.json только если его там ещё нет,
                     // а установка не перезаписывает рабочий — до пользователя доезжает
                     // именно этот файл. Каталог выкладки с 1.87 — по системе (T-243),
                     // а сам builds с 1.114 лежит внутри AI2P_app (T-131-S0)
                     Path.Combine(RepoRoot(), "AI2P_app", "builds", "windows", "release", "config.json"),
                 })
        {
            if (!File.Exists(path))
            {
                continue;   // выкладки может не быть вовсе (чистое дерево исходников)
            }
            var json = File.ReadAllText(path);
            Assert.DoesNotContain(@"C:\\ai", json, StringComparison.Ordinal);
            Assert.DoesNotContain("/home/", json, StringComparison.Ordinal);
        }
    }

    /// <summary>Конфигурация дистрибутива, прочитанная как есть, даёт каталог своей системы
    /// и переписывать файл при старте не требует.</summary>
    [Fact]
    public void Shipped_Config_Loads_With_System_Default()
    {
        var shipped = Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "config.json");
        var path = Path.Combine(_dir, "config.json");
        File.Copy(shipped, path);

        var config = Ai2pConfig.Load(path);

        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, config.Storage.ModelsRepo);
        Assert.Empty(config.NormalizePlatformPaths());
    }

    /// <summary>Установка НОВОЙ версии поверх старой: значения пользователя ложатся поверх
    /// умолчаний, поэтому старый <c>C:\ai</c> доезжает до Linux-установки и снимается уже
    /// приведением к платформе — вместе с записью правки в config.json.</summary>
    [Fact]
    public void Upgrade_Over_Old_Install_Is_Fixed_On_Start()
    {
        var path = Path.Combine(_dir, "config.json");
        var shipped = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "config.json"));
        var old = """
        {
          "ui": { "port": 5481 },
          "storage": { "dataDir": "./data", "modelsRepo": "%FOREIGN%" }
        }
        """.Replace("%FOREIGN%", ForeignPath.Replace("\\", "\\\\"));
        File.WriteAllText(path, ConfigMerge.Merge(shipped, old));

        var config = Ai2pConfig.Load(path);
        var changes = config.NormalizePlatformPaths();
        config.Save(path);

        Assert.Single(changes);
        // настройка пользователя, платформе не противоречащая, пережила обновление
        Assert.Equal(5481, config.Ui.Port);
        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, config.Storage.ModelsRepo);
        // правка записана в файл: следующий старт уже ничего не меняет
        var reloaded = Ai2pConfig.Load(path);
        Assert.Equal(Ai2pConfig.StorageSettings.DefaultModelsRepo, reloaded.Storage.ModelsRepo);
        Assert.Empty(reloaded.NormalizePlatformPaths());
    }

    /// <summary>Приведение к платформе делается при старте — до того, как каталогами
    /// начинают пользоваться установщик моделей и запуск локальных серверов.</summary>
    [Fact]
    public void Startup_Normalizes_Config_And_Saves_It()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "Program.cs"));

        var load = program.IndexOf("Ai2pConfig.Load(configPath)", StringComparison.Ordinal);
        var normalize = program.IndexOf("config.NormalizePlatformPaths()", StringComparison.Ordinal);
        var orgs = program.IndexOf("new OrgRegistry(", StringComparison.Ordinal);

        Assert.True(load > 0 && normalize > load && orgs > normalize,
            "конфигурация приводится к платформе сразу после чтения и до создания сервисов");
        Assert.Contains("config.Save(configPath)", program, StringComparison.Ordinal);
    }

    // ---------- 5. форма настроек сервера ----------

    /// <summary>Путь чужой системы, набранный руками, форма не принимает: на Windows
    /// «/home/…» проверку «полного пути» проходит, и без отдельного отказа он молча уехал
    /// бы в config.json.</summary>
    [Fact]
    public void Local_Server_Form_Rejects_Foreign_Path()
    {
        var dto = new ServerSettingsDto
        {
            Port = 5480,
            Hostname = "localhost",
            ModelsRepo = ForeignPath,
            DistDir = "",
            PackagesDir = "",
        };

        var error = Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(dto));
        Assert.Contains("другой системы", error.Message);

        // свой путь и «~/ai» принимаются по-прежнему
        dto.ModelsRepo = OwnPath;
        ApiEndpoints.ValidateLocalServer(dto);
        dto.ModelsRepo = "~/ai";
        ApiEndpoints.ValidateLocalServer(dto);
    }

    /// <summary>Пустое поле формы означает «умолчание системы», а не пустой путь.</summary>
    [Fact]
    public void Empty_Form_Field_Means_System_Default()
    {
        var hint = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", "ru.json"));
        Assert.Contains("settings.modelsRepoHint", hint, StringComparison.Ordinal);
        Assert.Contains("~/ai", hint, StringComparison.Ordinal);

        var api = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        Assert.Contains("Ai2pConfig.StorageSettings.DefaultModelsRepo", api, StringComparison.Ordinal);
    }
}
