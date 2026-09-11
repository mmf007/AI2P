using AI2P.Core;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo46 / этап 46 (ТЗ v1.53): ВЕРСИОННОСТЬ, УСТАНОВКА И ОБНОВЛЕНИЕ.
///
/// Версия — <c>1.&lt;номер билда&gt;</c>, где номер билда это <c>xx</c> из <c>todo&lt;xx&gt;.md</c>.
/// По ней скрипт установки решает, обновлять ли выкладку, а приложение при первом старте
/// после обновления доводит данные и сливает конфигурацию: новая — основа, старая — поверх.
/// </summary>
public sealed class Todo46Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo46-" + Guid.NewGuid().ToString("N"));

    public Todo46Tests() => Directory.CreateDirectory(_dir);

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

    /// <summary>Каталог <c>AI2P_app</c>: рядом с ним лежат скрипты сборки и установки.</summary>
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

    // --- скрипты установки в выкладке ---

    [Fact]
    public void Windows_Install_Has_A_Cmd_Wrapper()
    {
        // у .ps1 в Windows ассоциация «редактировать»: из проводника и FAR по Enter скрипт
        // открывается в блокноте, а не выполняется — поэтому рядом лежит обёртка .cmd,
        // как у build и buildRelease. Ключи (-WhatIf/-Force/-Lang) она передаёт дальше.
        //
        // С T-65-S0 обёртка разбирает командную строку САМА (ей нужно узнать «--help» и
        // превратить «--lang ru» в «-Lang ru» — PowerShell двойную чёрточку не принимает),
        // поэтому дословного «%*» в файле больше нет: хвост копится в %ARGS% и уезжает в
        // .ps1 в ОБЕИХ ветках — и рабочей, и справочной. Сторожим именно это, иначе
        // «install.cmd D:\AI2P -WhatIf» молча поставит программу по-настоящему.
        var cmd = File.ReadAllText(Path.Combine(AppDir(), "install.cmd"));

        Assert.Contains("install.ps1", cmd);

        var runs = File.ReadAllLines(Path.Combine(AppDir(), "install.cmd"))
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("rem", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.IndexOf("install.ps1", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        Assert.Equal(2, runs.Count); // рабочая ветка и ветка справки
        foreach (var line in runs)
        {
            Assert.True(line.Contains("%ARGS%") || line.Contains("%*"),
                $"обёртка глотает ключи командной строки: {line}");
        }

        // и нормализация написания ключа языка: человек пишет «--Lang ru» как остальные ключи
        Assert.Contains("--lang", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-Lang %1", cmd);
    }

    /// <summary>
    /// Скрипты установки кладутся в выкладку — но с T-211 только СВОЕЙ платформы: в выкладке
    /// для Windows нечего делать <c>install.sh</c>, в выкладке для Linux/macOS — <c>install.ps1</c>
    /// и <c>install.cmd</c>. Проверка платформенного выбора — в <see cref="T211Tests"/>.
    /// </summary>
    [Fact]
    public void Publish_Puts_All_Install_Scripts_Into_The_Package()
    {
        var app = AppDir();

        var ps = File.ReadAllText(Path.Combine(app, "buildRelease.ps1"));
        var sh = File.ReadAllText(Path.Combine(app, "buildRelease.sh"));

        Assert.Contains("@(\"install.ps1\", \"install.cmd\")", ps);
        Assert.Contains("@(\"install.sh\")", ps);
        Assert.Contains("INSTALL_SCRIPTS=\"install.ps1 install.cmd\"", sh);
        Assert.Contains("INSTALL_SCRIPTS=\"install.sh\"", sh);
        // сами файлы на месте: копировать нечего, если их нет
        foreach (var script in new[] { "install.ps1", "install.cmd", "install.sh" })
        {
            Assert.True(File.Exists(Path.Combine(app, script)), script);
        }
    }

    // --- номер версии ---

    [Fact]
    public void Version_Is_One_Dot_Build()
    {
        // формат задан заданием: 1.<номер билда>, номер билда = xx из todoxx.md
        Assert.Equal(AppInfo.Version, $"1.{AppInfo.Build}");
        Assert.True(AppInfo.Build >= 46, "номер билда не может быть меньше задания, в котором он введён");
        // правило todo46: номер поднимается с КАЖДЫМ новым заданием (doc/AI2P_release.md)
    }

    [Fact]
    public void Build_Is_Parsed_Back_From_The_Version()
    {
        Assert.Equal(AppInfo.Build, AppInfo.BuildOf(AppInfo.Version));
        Assert.Equal(46, AppInfo.BuildOf("1.46"));
        Assert.Equal(7, AppInfo.BuildOf(" 1.7 "));
        // разобрать не удалось — 0, и обновление тогда считает установку «неизвестной»
        Assert.Equal(0, AppInfo.BuildOf("мусор"));
        Assert.Equal(0, AppInfo.BuildOf(null));
        Assert.Equal(0, AppInfo.BuildOf("1"));
    }

    // --- слияние конфигурации при обновлении ---

    [Fact]
    public void New_Parameters_Come_With_Defaults_And_User_Values_Survive()
    {
        // это и есть правило из задания: «считываем новый конфиг, поверх считываем старый»
        var merged = ConfigMerge.Merge(
            newJson: """{"ui":{"port":5480,"protocol":"http","newOption":"умолчание"},"language":"ru"}""",
            oldJson: """{"ui":{"port":9999,"protocol":"http"},"language":"en"}""");

        var root = System.Text.Json.Nodes.JsonNode.Parse(merged)!;
        Assert.Equal(9999, (int)root["ui"]!["port"]!);              // рабочее значение осталось
        Assert.Equal("умолчание", (string)root["ui"]!["newOption"]!); // новый параметр появился
        Assert.Equal("en", (string)root["language"]!);
    }

    [Fact]
    public void Unknown_Sections_Of_The_Old_Config_Are_Kept()
    {
        // в конфиге бывают заготовки, о которых код ещё не знает (ui.https); терять их нельзя
        var merged = ConfigMerge.Merge(
            newJson: """{"ui":{"port":5480}}""",
            oldJson: """{"ui":{"port":5480,"https":{"certFile":"c.pem"}},"мойРаздел":{"a":1}}""");

        var root = System.Text.Json.Nodes.JsonNode.Parse(merged)!;
        Assert.Equal("c.pem", (string)root["ui"]!["https"]!["certFile"]!);
        Assert.Equal(1, (int)root["мойРаздел"]!["a"]!);
    }

    [Fact]
    public void Arrays_Are_Replaced_Whole()
    {
        // массив в конфиге — одна настройка, а не набор независимых: сливать поэлементно
        // означало бы получить список, которого пользователь не задавал
        var merged = ConfigMerge.Merge(
            newJson: """{"list":["a","b","c"]}""",
            oldJson: """{"list":["x"]}""");

        var root = System.Text.Json.Nodes.JsonNode.Parse(merged)!;
        Assert.Single(root["list"]!.AsArray());
        Assert.Equal("x", (string)root["list"]![0]!);
    }

    [Fact]
    public void Pending_Config_Is_Applied_Once_And_Marked()
    {
        var config = Path.Combine(_dir, "config.json");
        File.WriteAllText(config, """{"ui":{"port":9999},"language":"en"}""");
        File.WriteAllText(Path.Combine(_dir, ConfigMerge.NewConfigName),
            """{"ui":{"port":5480,"bindAddress":"0.0.0.0"},"language":"ru"}""");

        Assert.True(ConfigMerge.ApplyPending(config));

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(config))!;
        Assert.Equal(9999, (int)root["ui"]!["port"]!);                 // моё
        Assert.Equal("0.0.0.0", (string)root["ui"]!["bindAddress"]!);  // новое
        Assert.Equal("en", (string)root["language"]!);
        // файл новой версии помечен применённым — второй раз слияние не повторится
        Assert.False(File.Exists(Path.Combine(_dir, ConfigMerge.NewConfigName)));
        Assert.True(File.Exists(Path.Combine(_dir, ConfigMerge.NewConfigName + ConfigMerge.AppliedSuffix)));
        Assert.False(ConfigMerge.ApplyPending(config));
    }

    [Fact]
    public void Without_A_Working_Config_The_New_One_Simply_Becomes_It()
    {
        // первичная установка: рабочего конфига ещё нет, сливать не с чем
        var config = Path.Combine(_dir, "config.json");
        File.WriteAllText(Path.Combine(_dir, ConfigMerge.NewConfigName), """{"language":"ru"}""");

        Assert.True(ConfigMerge.ApplyPending(config));

        Assert.True(File.Exists(config));
        Assert.Contains("\"language\"", File.ReadAllText(config));
    }

    [Fact]
    public void Nothing_Happens_Without_A_Pending_Config()
    {
        var config = Path.Combine(_dir, "config.json");
        File.WriteAllText(config, """{"language":"ru"}""");

        Assert.False(ConfigMerge.ApplyPending(config));

        Assert.Equal("""{"language":"ru"}""", File.ReadAllText(config));
    }

    [Fact]
    public void Broken_New_Config_Does_Not_Destroy_The_Working_One()
    {
        var merged = ConfigMerge.Merge(newJson: "{это не json", oldJson: """{"language":"en"}""");

        var root = System.Text.Json.Nodes.JsonNode.Parse(merged)!;
        Assert.Equal("en", (string)root["language"]!);
    }

    // --- одноразовые шаги обновления ---

    [Fact]
    public void Fresh_Install_Runs_No_Steps_And_Just_Remembers_The_Build()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        var ran = new List<int>();
        var steps = new[] { new Upgrade.Step(1, "древний шаг", _ => ran.Add(1)) };

        var done = Upgrade.Run(registry, steps);

        // конвертировать нечего: база создана уже новой версией
        Assert.Equal(0, done);
        Assert.Empty(ran);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void Only_Steps_Newer_Than_The_Stored_Build_Run()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "44");
        var ran = new List<int>();
        var steps = new[]
        {
            new Upgrade.Step(43, "уже сделано", _ => ran.Add(43)),
            new Upgrade.Step(44, "тоже сделано", _ => ran.Add(44)),
            new Upgrade.Step(45, "нужно", _ => ran.Add(45)),
            new Upgrade.Step(46, "нужно", _ => ran.Add(46)),
            new Upgrade.Step(999, "из будущего", _ => ran.Add(999)),
        };

        var done = Upgrade.Run(registry, steps);

        Assert.Equal(2, done);
        Assert.Equal([45, 46], ran);
        Assert.Equal(AppInfo.Build.ToString(), registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void Interrupted_Upgrade_Continues_Where_It_Stopped()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "43");
        var ran = new List<int>();
        var steps = new[]
        {
            new Upgrade.Step(44, "первый", _ => ran.Add(44)),
            new Upgrade.Step(45, "падает", _ => throw new InvalidOperationException("сбой")),
            new Upgrade.Step(46, "третий", _ => ran.Add(46)),
        };

        Assert.Throws<InvalidOperationException>(() => Upgrade.Run(registry, steps));
        // билд записан ПОСЛЕ удавшегося шага — повтор не станет делать его заново
        Assert.Equal("44", registry.ServerDb.Meta(Upgrade.BuildKey));

        var fixed_ = new[]
        {
            new Upgrade.Step(44, "первый", _ => ran.Add(44)),
            new Upgrade.Step(45, "починен", _ => ran.Add(45)),
            new Upgrade.Step(46, "третий", _ => ran.Add(46)),
        };
        Assert.Equal(2, Upgrade.Run(registry, fixed_));
        Assert.Equal([44, 45, 46], ran);
    }

    [Fact]
    public void Running_An_Older_Version_Does_Not_Roll_Anything_Back()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "999");
        var ran = new List<int>();
        var steps = new[] { new Upgrade.Step(46, "шаг", _ => ran.Add(46)) };

        var done = Upgrade.Run(registry, steps);

        Assert.Equal(0, done);
        Assert.Empty(ran);
        // записанный билд не понижается: данные уже доведены до более новой версии
        Assert.Equal("999", registry.ServerDb.Meta(Upgrade.BuildKey));
    }

    [Fact]
    public void Second_Start_Of_The_Same_Version_Does_Nothing()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);
        registry.ServerDb.SetMeta(Upgrade.BuildKey, AppInfo.Build.ToString());
        var ran = new List<int>();

        var done = Upgrade.Run(registry, [new Upgrade.Step(AppInfo.Build, "шаг", _ => ran.Add(1))]);

        Assert.Equal(0, done);
        Assert.Empty(ran);
    }

    [Fact]
    public void Declared_Steps_Are_Ordered_And_Not_From_The_Future()
    {
        // страховка от опечатки в номере билда шага: шаг «из будущего» не выполнится никогда.
        // Предел — не текущий билд, а СЛЕДУЮЩИЙ: задание выпуска поднимает билд в самом конце,
        // поэтому в дереве законно живёт шаг готовящейся версии (T-185: шаг 80 при билде 79).
        // Опечатку это ловит по-прежнему — мимо на один билд не промахиваются
        var next = AppInfo.Build + 1;
        var builds = Upgrade.Steps.Select(s => s.Build).ToList();
        Assert.Equal(builds.OrderBy(b => b).ToList(), builds);
        Assert.All(Upgrade.Steps, s => Assert.True(s.Build <= next,
            $"шаг обновления {s.Build} новее готовящегося билда {next}"));
        Assert.Equal(builds.Distinct().Count(), builds.Count);
    }

    /// <summary>Реестр организаций поверх временного каталога тестовой базы: шагам обновления
    /// нужен доступ и к серверной БД, и к организациям.</summary>
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
