using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server;
using AI2P.Server.Api;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-135: ПОДГОТОВКА К КЛАСТЕРУ СЕРВЕРОВ.
///
/// Первый запуск нескольких серверов упирался в четыре вещи:
///
/// 1. «~» в путях. На Linux/macOS каталог сервера — <c>~/ai/AI2P</c>, и человек набирает
///    так же каталог проекта, каталоги этого компьютера и правила безопасности. Ни
///    <c>Path.GetFullPath</c>, ни <c>Path.Combine</c> «~» не понимают: получался бы каталог
///    с именем «~» рядом с приложением, а правило безопасности «~/…» считалось бы путём
///    ВНУТРИ папки проекта (относительным) — то есть не срабатывало бы вовсе.
/// 2. Папка <c>Common</c>, наоборот, обязана быть ОТНОСИТЕЛЬНОЙ (она внутри каталога
///    проекта): полный путь репликация файлов молча отвергала.
/// 3. Не первичный сервер кластера: организацию у себя он не создаёт, она приезжает
///    репликацией. Экран первого старта получил флажок «первичный сервер кластера», а обмен
///    заявками — свою страницу (организаций нет, значит и UI, живущего внутри организации,
///    нет). Здесь проверяется разбор адреса дирижёра и заведение его записи.
/// 4. Смена дирижёра: интервал репликации пары принадлежит записи РЯДОВОГО сервера,
///    поэтому при передаче дирижёрства он переносится прежнему дирижёру — иначе
///    автоматическая репликация пары молча прекратилась бы.
/// </summary>
public sealed class T135Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static string Home => PathHome.Home();

    // ---------- 1. «~» — домашний каталог ----------

    [Fact]
    public void Tilde_Is_Expanded_To_Home()
    {
        Assert.Equal(Path.Combine(Home, "ai", "AI2P"), PathHome.Expand("~/ai/AI2P"));
        Assert.Equal(Path.Combine(Home, "ai", "AI2P"), PathHome.Expand("~\\ai\\AI2P"));
        Assert.Equal(Home, PathHome.Expand("~"));
        Assert.Equal(Home, PathHome.Expand("  ~  "));
    }

    [Fact]
    public void Only_Leading_Own_Home_Is_Expanded()
    {
        // «~user/…» — чужой домашний каталог (синтаксис оболочки POSIX): не наше дело
        Assert.Equal("~user/ai", PathHome.Expand("~user/ai"));
        // «~» в середине пути — обычный символ имени файла
        Assert.Equal("/opt/ai/~tmp", PathHome.Expand("/opt/ai/~tmp"));
        Assert.Equal("/opt/ai", PathHome.Expand("/opt/ai"));
        Assert.Equal(@"C:\ai", PathHome.Expand(@"C:\ai"));
        Assert.Equal("", PathHome.Expand(null));
    }

    [Fact]
    public void Home_Path_Is_Rooted()
    {
        Assert.True(PathHome.IsRooted("~/ai"));
        Assert.True(PathHome.StartsWithHome("~/ai"));
        Assert.False(PathHome.StartsWithHome("~user/ai"));
        Assert.False(PathHome.IsRooted("ai/models"));
    }

    /// <summary>Каталоги из config.json (dataDir, logs, репозиторий моделей): «~/ai» — полный
    /// путь от дома, а не подкаталог рядом с config.json.</summary>
    [Fact]
    public void Config_Dirs_Resolve_Tilde()
    {
        var config = new Ai2pConfig();
        var configPath = Path.Combine(_f.Dir, "config.json");

        Assert.Equal(Path.GetFullPath(Path.Combine(Home, "ai")), config.ResolveDir(configPath, "~/ai"));
        // относительный путь по-прежнему считается от каталога config.json
        Assert.Equal(Path.GetFullPath(Path.Combine(_f.Dir, "data")), config.ResolveDir(configPath, "./data"));
    }

    /// <summary>Каталоги этого компьютера в форме локального сервера: «~/ai» принимается.
    /// Относительный путь («ai», «./models») с T-291 принимается ТОЖЕ — вопрос «относительно
    /// чего» получил один ответ на всю систему (каталог рядом с установкой), и именно
    /// относительный путь стал умолчанием. Отвергается только уход выше этого каталога.</summary>
    [Fact]
    public void Local_Server_Dirs_Accept_Tilde()
    {
        var dto = new ServerSettingsDto
        {
            Port = 5480,
            Hostname = "localhost",
            ModelsRepo = "~/ai",
            DistDir = "",
            PackagesDir = "~/ai/dist",
        };

        ApiEndpoints.ValidateLocalServer(dto);   // не бросает

        dto.ModelsRepo = "ai";
        ApiEndpoints.ValidateLocalServer(dto);   // относительный путь тоже законен (T-291)

        dto.ModelsRepo = "../../ai";
        var error = Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(dto));
        Assert.Contains("уводит выше", error.Message);
    }

    /// <summary>Каталог проекта хранится РАСКРЫТЫМ: им пользуются инструменты агента,
    /// песочница CLI (--add-dir) и репликация файлов — все ждут настоящий путь.</summary>
    [Fact]
    public void Project_Folder_Is_Stored_Expanded()
    {
        var created = _f.Projects.Create("Проект", "~/work/проект", null, null);
        Assert.Equal(Path.Combine(Home, "work", "проект"), created.FolderPath);

        var saved = _f.Projects.SaveLocalPart(created.Id, "~/work/другой", "", true, null);
        Assert.Equal(Path.Combine(Home, "work", "другой"), saved.FolderPath);
        Assert.Equal(saved.FolderPath, _f.Projects.Get(created.Id)!.FolderPath);
    }

    // ---------- 2. папка Common — только относительный путь ----------

    [Fact]
    public void Common_Folder_Must_Be_Relative()
    {
        var project = _f.Projects.Create("Проект", "/work/p", null, null);

        // так — правильно: путь внутри каталога проекта
        Assert.Equal("media", _f.Projects.SaveLocalPart(project.Id, "/work/p", "media", true, null).CommonPath);

        foreach (var wrong in new[] { "~/media", "/media", @"C:\media", "../media" })
        {
            var error = Assert.Throws<ArgumentException>(() =>
                _f.Projects.SaveLocalPart(project.Id, "/work/p", wrong, true, null));
            Assert.Contains("ОТНОСИТЕЛЬНО", error.Message);
        }
    }

    // ---------- 3. правила безопасности ----------

    [Fact]
    public void Security_Rule_With_Tilde_Is_A_Full_Path()
    {
        Assert.True(SecurityRulePaths.IsFullPath("~/ai/AI2P"));
        Assert.True(SecurityRulePaths.IsFullPath("~\\ai"));
        Assert.False(SecurityRulePaths.IsFullPath("~user/ai"));
        Assert.False(SecurityRulePaths.IsFullPath("подпапка/файлы"));
    }

    /// <summary>Глобальное правило «~/shared» открывает НАСТОЯЩИЙ каталог в доме, а не
    /// подпапку «~» внутри проекта (до T-135 путь считался относительным).</summary>
    [Fact]
    public void Security_Rule_With_Tilde_Matches_Real_Directory()
    {
        var root = Path.Combine(_f.Dir, "проект");
        Directory.CreateDirectory(root);
        var rules = new List<SecurityRule>
        {
            new()
            {
                Scope = SecurityScope.Global, Target = SecurityTarget.Directory,
                Permission = SecurityPermission.Allow, Pattern = "~/shared/*",
                OpRead = true, OpWrite = true,
            },
        };
        var evaluator = new SecurityEvaluator(rules, root);

        Assert.Equal(SecurityDecision.Allow,
            evaluator.ForPath(Path.Combine(Home, "shared", "файл.txt"), "write").Decision);
        // вне проекта и вне открытых каталогов — по-прежнему запрещено (todo25)
        Assert.Equal(SecurityDecision.Deny,
            evaluator.ForPath(Path.Combine(Home, "секреты", "key.txt"), "read").Decision);
        // CLI-агенту каталог уходит флагом --add-dir раскрытым (T-117 + T-135)
        Assert.Equal([Path.Combine(Home, "shared")], evaluator.AllowedFullPathDirs());
    }

    /// <summary>Каталог проекта, заданный от дома, накрывается правилом по умолчанию.</summary>
    [Fact]
    public void Project_Root_With_Tilde_Is_Inside_Project()
    {
        var evaluator = new SecurityEvaluator([], "~/work/проект");

        Assert.True(evaluator.IsInsideProject(Path.Combine(Home, "work", "проект", "файл.md")));
    }

    // ---------- 4. не первичный сервер: подключение к кластеру ----------

    [Fact]
    public void Conductor_Address_Is_Parsed()
    {
        Assert.True(ClusterJoinFlow.TryParseAddress("http://192.168.1.10:5480/ai2p",
            out var protocol, out var host, out var port, out var basePath, out _));
        Assert.Equal(("http", "192.168.1.10", 5480, "/ai2p"), (protocol, host, port, basePath));

        // без протокола и без порта: протокол http, порт — умолчание AI2P
        Assert.True(ClusterJoinFlow.TryParseAddress("ubuntu.local/ai2p",
            out protocol, out host, out port, out basePath, out _));
        Assert.Equal(("http", "ubuntu.local", 5480, "/ai2p"), (protocol, host, port, basePath));

        // без префикса
        Assert.True(ClusterJoinFlow.TryParseAddress("https://ai2p.example.com:8443",
            out protocol, out host, out port, out basePath, out _));
        Assert.Equal(("https", "ai2p.example.com", 8443, ""), (protocol, host, port, basePath));

        Assert.False(ClusterJoinFlow.TryParseAddress("  ", out _, out _, out _, out _, out var empty));
        Assert.NotEqual("", empty);
        Assert.False(ClusterJoinFlow.TryParseAddress("ftp://host/", out _, out _, out _, out _, out _));
    }

    /// <summary>Локальный сервер, поставленный НЕ первичным, дирижёром не является: связей
    /// с организациями у него нет вовсе, и в кластере он получит код от дирижёра.</summary>
    [Fact]
    public void Secondary_Server_Has_No_Org_And_No_Conductor_Role()
    {
        var local = _f.Servers.EnsureLocal("ubuntu", "http", "ubuntu.local", 5480, "/ai2p");

        var org = _f.Orgs.Create("Приехавшая", "acme", null);

        Assert.True(local.IsLocal);
        Assert.Empty(_f.Servers.OrgsOf(local.Id));
        Assert.Null(_f.Servers.Conductor(org.Id));
    }

    /// <summary>Команда запуска локального сервера модели идёт в процесс БЕЗ оболочки —
    /// «~» в пути исполняемого файла раскрывать некому, кроме нас (ТЗ п. 2.9).</summary>
    [Fact]
    public void Local_Model_Launch_Command_Expands_Tilde()
    {
        var (file, args) = LocalModelProcessService.SplitCommand("~/ai/llama/llama-server -m q.gguf");
        Assert.Equal(Path.Combine(Home, "ai", "llama", "llama-server"), file);
        Assert.Equal("-m q.gguf", args);

        var (quoted, _) = LocalModelProcessService.SplitCommand("\"~/ai/llama cpp/llama-server\" -m q");
        Assert.Equal(Path.Combine(Home, "ai", "llama cpp", "llama-server"), quoted);
    }

    // ---------- 5. смена дирижёра ----------

    /// <summary>
    /// Дирижёрство передаётся другому серверу: отметка ровно одна, а интервалы репликации
    /// пары переезжают прежнему дирижёру — иначе пара осталась бы без интервала и
    /// автоматическая репликация молча прекратилась бы (T-135).
    /// </summary>
    [Fact]
    public void Conductor_Handover_Moves_Replication_Intervals()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null).Id;
        var windows = _f.Servers.EnsureLocal("windows", "http", "windows", 5480, "/ai2p");
        var ubuntu = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Protocol = "http", Hostname = "ubuntu.local", Port = 5480, BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org, windows.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(org, ubuntu.Id, OrgServerStatus.Active, "", null);
        // интервал пары задан у РЯДОВОГО сервера (ТЗ гл. 6, этап 43)
        _f.Servers.SetReplication(ubuntu.Id, 60, 30, null);
        Assert.True(_f.Servers.Conductor(org)!.ServerId == windows.Id);

        _f.Servers.SetConductor(org, ubuntu.Id, null);

        var conductor = _f.Servers.Conductor(org);
        Assert.Equal(ubuntu.Id, conductor!.ServerId);
        Assert.Single(_f.Servers.ServersOf(org), l => l.IsConductor);
        // ритм пары сохранился: теперь он принадлежит записи прежнего дирижёра
        Assert.Equal(60, _f.Servers.Get(windows.Id)!.ReplIntervalSec);
        Assert.Equal(30, _f.Servers.Get(windows.Id)!.ReplRetrySec);
    }

    /// <summary>Заданный человеком интервал прежнего дирижёра при передаче не затирается.</summary>
    [Fact]
    public void Conductor_Handover_Keeps_Explicit_Intervals()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null).Id;
        var windows = _f.Servers.EnsureLocal("windows", "http", "windows", 5480, "/ai2p");
        var ubuntu = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Protocol = "http", Hostname = "ubuntu.local", Port = 5480, BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org, windows.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(org, ubuntu.Id, OrgServerStatus.Active, "", null);
        _f.Servers.SetReplication(ubuntu.Id, 60, 30, null);
        _f.Servers.SetReplication(windows.Id, 300, 45, null);

        _f.Servers.SetConductor(org, ubuntu.Id, null);

        Assert.Equal(300, _f.Servers.Get(windows.Id)!.ReplIntervalSec);
        Assert.Equal(45, _f.Servers.Get(windows.Id)!.ReplRetrySec);
    }

    /// <summary>Дирижёр обязателен: отвязать его нельзя, назначить неподключённый — тоже.</summary>
    [Fact]
    public void Conductor_Is_Mandatory_And_Single()
    {
        var org = _f.Orgs.Create("Фирма", "acme", null).Id;
        var windows = _f.Servers.EnsureLocal("windows", "http", "windows", 5480, "/ai2p");
        var pending = _f.Servers.Create(new ServerSaveInput
        {
            Name = "новый", Protocol = "http", Hostname = "new.local", Port = 5480, BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org, windows.Id, OrgServerStatus.Active, "", null);
        _f.Servers.Attach(org, pending.Id, OrgServerStatus.Pending, "кто-то", null);

        Assert.Throws<ArgumentException>(() => _f.Servers.SetConductor(org, pending.Id, null));
        Assert.Throws<ArgumentException>(() => _f.Servers.Detach(org, windows.Id, null));
    }
}
