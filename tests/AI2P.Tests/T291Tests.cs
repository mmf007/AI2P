using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server;
using AI2P.Server.Api;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-291: ПЕРВИЧНАЯ УСТАНОВКА У РЕАЛЬНОГО ПОЛЬЗОВАТЕЛЯ.
///
/// Жалоба заказчика после первой установки: «всё очень запутано, чтобы добраться до
/// выполнения первой задачи». Разбирается тремя правками, и каждая проверяется здесь.
///
/// 1. КАТАЛОГИ. Репозиторий моделей был абсолютным путём, одинаковым у всех
///    (<c>C:\ai</c>), а каталоги дистрибутивов и пакетов пустыми — куда что легло,
///    человек не знал. Теперь умолчания относительные (<c>./models</c>,
///    <c>./distribs</c>, <c>./packages</c>) и считаются от каталога РЯДОМ с установкой,
///    а форма сервера показывает, во что путь разворачивается на самом деле.
///
/// 2. ДВА ПАРОЛЯ. Пароль пользователя и пароль администратора сервера — разные вещи,
///    и на одиночной установке это неочевидно до тех пор, пока форма своего сервера
///    не окажется «только для чтения». Визард предлагает флажок «пароль тот же»
///    (по умолчанию отмечен), и связь живёт дальше: смена пароля тянет за собой второй.
///
/// 3. ПУСТОЙ ИНТЕРФЕЙС. Создав организацию, человек оставался перед пустым экраном.
///    Теперь у нового дирижёра кластера визард спрашивает типовое использование и
///    собирает рабочее место: ИИ-исполнители Jon, Bob, Stiv на лучших моделях,
///    команда <c>&lt;организация&gt;_team</c> с человеком-тимлидом и первый проект.
/// </summary>
public sealed class T291Tests : IClassFixture<StorageFixture>
{
    private readonly StorageFixture _f;

    public T291Tests(StorageFixture fixture) => _f = fixture;

    // ---------- 1. каталоги этого компьютера ----------

    /// <summary>Умолчания стали относительными и годятся обеим системам.</summary>
    [Fact]
    public void Machine_Dirs_Default_To_Relative_Paths()
    {
        var storage = new Ai2pConfig().Storage;

        Assert.Equal("./models", storage.ModelsRepo);
        Assert.Equal("./distribs", storage.DistDir);
        Assert.Equal("./packages", storage.PackagesDir);
        foreach (var windows in new[] { true, false })
        {
            Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(storage.ModelsRepo, windows));
            Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(storage.DistDir, windows));
            Assert.False(Ai2pConfig.StorageSettings.IsForeignPath(storage.PackagesDir, windows));
        }
    }

    /// <summary>
    /// ГЛАВНОЕ ПРАВИЛО ЗАДАНИЯ: относительный каталог считается НА ШАГ ВЫШЕ каталога
    /// запуска — программа в <c>C:\ai\AI2P</c> означает модели в <c>C:\ai\models</c>.
    /// </summary>
    [Fact]
    public void Relative_Dir_Is_Resolved_Next_To_The_Install()
    {
        var app = Path.Combine(_f.Dir, "AI2P");
        var root = Ai2pConfig.StorageSettings.MachineRoot(app, app);

        Assert.Equal(Path.GetFullPath(_f.Dir), Path.GetFullPath(root));

        var config = new Ai2pConfig();
        Assert.Equal(Path.Combine(Path.GetFullPath(_f.Dir), "models"), config.ModelsRepoPath(root));
        Assert.Equal(Path.Combine(Path.GetFullPath(_f.Dir), "distribs"), config.DistDirPath(root));
        Assert.Equal(Path.Combine(Path.GetFullPath(_f.Dir), "packages"), config.PackagesDirPath(root));
    }

    /// <summary>Абсолютный путь и путь от домашнего каталога берутся как есть (T-135).</summary>
    [Fact]
    public void Absolute_And_Home_Paths_Are_Taken_As_They_Are()
    {
        var root = Path.Combine(_f.Dir, "root");
        var absolute = OperatingSystem.IsWindows() ? @"D:\weights" : "/opt/weights";

        Assert.Equal(Path.GetFullPath(absolute),
            Ai2pConfig.StorageSettings.ResolveMachineDir(root, absolute));
        var home = Ai2pConfig.StorageSettings.ResolveMachineDir(root, "~/ai");
        Assert.Equal(Path.GetFullPath(AI2P.Core.PathHome.Expand("~/ai")), home);
        Assert.DoesNotContain("~", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// Установка в каталог программ (T-287): рабочие файлы уехали в каталог данных, и шаг
    /// вверх привёл бы в <c>C:\Program Files</c> — туда писать нельзя. Считаем от рабочего
    /// каталога, где уже лежат data и logs.
    /// </summary>
    [Fact]
    public void Relocated_Install_Counts_From_The_Working_Dir()
    {
        var app = Path.Combine(_f.Dir, "Program Files", "AI2P");
        var home = Path.Combine(_f.Dir, "ProgramData", "AI2P");

        var root = Ai2pConfig.StorageSettings.MachineRoot(app, home);

        Assert.Equal(Path.GetFullPath(home), Path.GetFullPath(root));
        Assert.Equal(Path.Combine(Path.GetFullPath(home), "models"),
            new Ai2pConfig().ModelsRepoPath(root));
    }

    /// <summary>Пустые дистрибутивы и пакеты означают прежнее поведение («временный каталог»
    /// и «&lt;репозиторий&gt;/packages»), а не каталог рядом с установкой: у конфигураций,
    /// переживших обновление, эти поля пусты, и менять им каталог за спиной нельзя.</summary>
    [Fact]
    public void Empty_Package_Dirs_Keep_The_Old_Meaning()
    {
        var root = Path.Combine(_f.Dir, "root");
        var config = new Ai2pConfig();
        config.Storage.DistDir = "";
        config.Storage.PackagesDir = "";

        Assert.Equal("", config.DistDirPath(root));
        Assert.Equal("", config.PackagesDirPath(root));
        // а пустой репозиторий моделей — это всё-таки умолчание: без каталога весов
        // установщик моделей работать не может вовсе
        config.Storage.ModelsRepo = "";
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "models"), config.ModelsRepoPath(root));
    }

    /// <summary>Форма сервера принимает относительный путь (до T-291 отвергала) и по-прежнему
    /// отбивает путь чужой системы и уход выше каталога установки.</summary>
    [Fact]
    public void Server_Form_Accepts_Relative_And_Rejects_Escapes()
    {
        var dto = new ServerSettingsDto
        {
            Port = 5480,
            Hostname = "localhost",
            ModelsRepo = "./models",
            DistDir = "distribs",
            PackagesDir = "packages/comfy",
        };

        ApiEndpoints.ValidateLocalServer(dto);   // не бросает

        // шаг вверх уже уводит из каталога рядом с установкой — туда пусть кладут
        // абсолютным путём, осознанно
        dto.PackagesDir = "../packages";
        var error = Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(dto));
        Assert.Contains("уводит выше", error.Message);

        dto.PackagesDir = OperatingSystem.IsWindows() ? "/home/mike/ai" : @"C:\ai";
        Assert.Contains("другой системы",
            Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(dto)).Message);
    }

    /// <summary>Каталоги дистрибутива — ровно те, что названы в задании.</summary>
    [Fact]
    public void Shipped_Config_Holds_The_New_Dirs()
    {
        var shipped = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "config.json"));

        foreach (var expected in new[]
                 {
                     "\"distDir\": \"./distribs\"",
                     "\"packagesDir\": \"./packages\"",
                     "\"modelsRepo\": \"./models\"",
                     "\"serverDbFile\": \"server.db\"",
                     "\"dataDir\": \"./data\"",
                     "\"dbFile\": \"ai2p.db\"",
                 })
        {
            Assert.Contains(expected, shipped, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ОБНОВЛЕНИЕ ПОВЕРХ ПРОШЛОЙ ВЕРСИИ. Слияние кладёт значения пользователя поверх новых
    /// умолчаний (<see cref="ConfigMerge"/>), поэтому у того, кто уже настроил каталог
    /// (а его прописывает визард прошлых версий и форма сервера), он не меняется. Пустые
    /// дистрибутивы и пакеты старой конфигурации тоже переживают обновление и продолжают
    /// значить прежнее — иначе у человека «переехал» бы уже установленный ComfyUI.
    /// </summary>
    [Fact]
    public void Upgrade_Keeps_The_Dirs_Of_The_User()
    {
        var shipped = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "config.json"));
        var old = OperatingSystem.IsWindows()
            ? "{\"ui\":{\"port\":5480},\"storage\":{\"dataDir\":\"./data\"," +
              "\"modelsRepo\":\"C:\\\\ai\",\"distDir\":\"\",\"packagesDir\":\"\"}}"
            : "{\"ui\":{\"port\":5480},\"storage\":{\"dataDir\":\"./data\"," +
              "\"modelsRepo\":\"~/ai\",\"distDir\":\"\",\"packagesDir\":\"\"}}";

        var merged = Ai2pConfig.Load(Save(ConfigMerge.Merge(shipped, old)));

        Assert.Equal(OperatingSystem.IsWindows() ? @"C:\ai" : "~/ai", merged.Storage.ModelsRepo);
        Assert.Equal("", merged.Storage.DistDir);
        Assert.Equal("", merged.Storage.PackagesDir);
        Assert.Empty(merged.NormalizePlatformPaths());
        var root = Path.Combine(_f.Dir, "root");
        Assert.Equal("", merged.PackagesDirPath(root));   // прежнее «<репозиторий>/packages»

        // а конфигурация БЕЗ каталога получает новое умолчание — это и есть чистая установка
        var fresh = Ai2pConfig.Load(Save(ConfigMerge.Merge(shipped, "{\"ui\":{\"port\":5480}}")));
        Assert.Equal("./models", fresh.Storage.ModelsRepo);
        Assert.Equal("./distribs", fresh.Storage.DistDir);
    }

    private string Save(string json)
    {
        var path = Path.Combine(_f.Dir, "t291-merge-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Форма сервера показывает ФАКТИЧЕСКИЙ путь: относительное значение поля само
    /// по себе человеку ничего не говорит.</summary>
    [Fact]
    public void Server_Form_Shows_The_Real_Path()
    {
        var dialog = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "LocalServerDialog.razor"));

        Assert.Contains("_dto.ModelsRepoPath", dialog, StringComparison.Ordinal);
        Assert.Contains("_dto.DistDirPath", dialog, StringComparison.Ordinal);
        Assert.Contains("_dto.PackagesDirPath", dialog, StringComparison.Ordinal);
        Assert.Contains("settings.dirNow", dialog, StringComparison.Ordinal);
        // подсказки переписаны на обоих языках
        foreach (var lang in new[] { "ru", "en" })
        {
            var dict = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json"));
            Assert.Contains("./models", dict, StringComparison.Ordinal);
            Assert.Contains("./distribs", dict, StringComparison.Ordinal);
            Assert.Contains("./packages", dict, StringComparison.Ordinal);
            Assert.Contains("settings.dirNow", dict, StringComparison.Ordinal);
        }
    }

    // ---------- 2. один пароль на два входа ----------

    [Fact]
    public void Admin_Password_Follows_The_User_Only_While_Linked()
    {
        var config = new Ai2pConfig();

        // умолчание — «тот же пароль»: на одиночной установке это один человек
        Assert.True(config.ServerAdmin.SameAsUser);
        // но пока почта не названа, следовать не за кем
        Assert.False(config.ServerAdmin.Follows("mike@example.com"));

        config.ServerAdmin.UserEmail = "Mike@Example.com";
        Assert.True(config.ServerAdmin.Follows("mike@example.com"));   // почта без учёта регистра
        Assert.False(config.ServerAdmin.Follows("other@example.com"));
        Assert.False(config.ServerAdmin.Follows(""));

        config.ServerAdmin.SameAsUser = false;
        Assert.False(config.ServerAdmin.Follows("mike@example.com"));
    }

    /// <summary>Настройка переживает запись и чтение config.json: связь паролей должна
    /// работать и после перезапуска, иначе смена пароля разведёт их молча.</summary>
    [Fact]
    public void Admin_Password_Link_Survives_The_Config_File()
    {
        var path = Path.Combine(_f.Dir, "t291-config.json");
        var config = new Ai2pConfig();
        config.ServerAdmin.SameAsUser = true;
        config.ServerAdmin.UserEmail = "owner@localhost";
        config.Save(path);

        var loaded = Ai2pConfig.Load(path);

        Assert.True(loaded.ServerAdmin.SameAsUser);
        Assert.Equal("owner@localhost", loaded.ServerAdmin.UserEmail);
        Assert.True(loaded.ServerAdmin.Follows("owner@localhost"));
    }

    /// <summary>Визард спрашивает про общий пароль и задаёт его обоими входами.</summary>
    [Fact]
    public void Wizard_Offers_The_Shared_Password()
    {
        var pages = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "AuthPages.cs"));

        Assert.Contains("setup.adminSame", pages, StringComparison.Ordinal);
        Assert.Contains("view.AdminSame || !view.Primary", pages, StringComparison.Ordinal);
        Assert.Contains("ApplyAdminSame(configHolder", pages, StringComparison.Ordinal);
        // и связь применяется при смене пароля — обоими способами
        var api = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        Assert.Equal(2, api.Split("FollowAdminPassword(configHolder").Length - 1);
    }

    // ---------- 3. рабочее место под типовое использование ----------

    [Fact]
    public void Usage_Values_Are_Four_And_Unknown_Falls_Back()
    {
        Assert.Equal(["code", "video", "image", "none"], FirstSetup.Usages);
        Assert.Equal(FirstSetup.UsageCode, FirstSetup.NormalizeUsage("CODE"));
        Assert.Equal(FirstSetup.UsageNone, FirstSetup.NormalizeUsage("что-то ещё"));
        Assert.Equal(FirstSetup.UsageNone, FirstSetup.NormalizeUsage(null));
        // «нет типового использования» — рабочее место не собирается вовсе
        Assert.Empty(FirstSetup.SkillsOf(FirstSetup.UsageNone));
        Assert.Contains("code-write", FirstSetup.SkillsOf(FirstSetup.UsageCode));
        Assert.Contains("analyze-plan", FirstSetup.SkillsOf(FirstSetup.UsageCode));
        Assert.Contains("video-generate", FirstSetup.SkillsOf(FirstSetup.UsageVideo));
        Assert.Contains("image-generate", FirstSetup.SkillsOf(FirstSetup.UsageImage));
        // семейства не пересекаются: иначе видеомодель попала бы в команду программистов
        Assert.Empty(FirstSetup.SkillsOf(FirstSetup.UsageVideo)
            .Intersect(FirstSetup.SkillsOf(FirstSetup.UsageCode)));
    }

    /// <summary>
    /// Подбор моделей: только активные, лучшие по сумме баллов нужных навыков, БЕЗ ПОВТОРОВ.
    /// Проверяется на справочнике дистрибутива — том самом, что видит человек на первой
    /// установке.
    /// </summary>
    [Fact]
    public void Models_Are_Picked_By_Skill_Without_Repeats()
    {
        _f.Models.Seed();

        var picked = FirstSetup.PickModels(_f.Models, FirstSetup.UsageCode, 3);

        Assert.Equal(3, picked.Count);
        Assert.Equal(3, picked.Select(m => m.Id).Distinct().Count());
        Assert.All(picked, model => Assert.True(model.IsActive));
        // порядок именно по оценке: первая не хуже последней
        var scores = picked
            .Select(model => _f.Models.SkillsOf(model)
                .Where(skill => FirstSetup.SkillsOf(FirstSetup.UsageCode).Contains(skill.Name))
                .Sum(skill => skill.Score))
            .ToList();
        Assert.True(scores[0] >= scores[^1]);
        Assert.All(scores, score => Assert.True(score > 0));
    }

    /// <summary>Медиа-навыки берут медиа-модели, а не текстовые: у текстовой модели баллов
    /// по видео нет вовсе, и в подбор она не попадает.</summary>
    [Fact]
    public void Media_Usage_Does_Not_Pick_Text_Models()
    {
        _f.Models.Seed();

        foreach (var model in FirstSetup.PickModels(_f.Models, FirstSetup.UsageVideo, 3))
        {
            var skills = _f.Models.SkillsOf(model).Select(skill => skill.Name).ToList();
            Assert.Contains(skills, name => FirstSetup.SkillsOf(FirstSetup.UsageVideo).Contains(name));
        }
    }

    /// <summary>
    /// СБОРКА РАБОЧЕГО МЕСТА целиком: исполнители Jon/Bob/Stiv, команда
    /// <c>&lt;организация&gt;_team</c> с человеком-тимлидом и проект с этой командой
    /// по умолчанию.
    /// </summary>
    [Fact]
    public void First_Setup_Builds_Executors_Team_And_Project()
    {
        _f.Models.Seed();
        var owner = _f.Executors.Create(new Executor
        {
            Nick = "Хозяин-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Owner,
        }, null);
        var orgName = "Орг" + Guid.NewGuid().ToString("N")[..6];
        var dir = Path.Combine(_f.Dir, "работа-" + Guid.NewGuid().ToString("N")[..6]);

        var result = FirstSetup.Apply(Services(), orgName, owner, FirstSetup.UsageCode,
            "Первый проект " + orgName, dir);

        // 1. исполнители — с именами из задания и на РАЗНЫХ моделях. Сравнение по началу
        // имени: ники в организации уникальны, и соседний тест того же класса мог занять
        // «Jon» раньше — тогда законно выйдет «Jon-2»
        Assert.Equal(3, result.Executors.Count);
        Assert.Collection(result.Executors,
            nick => Assert.StartsWith("Jon", nick, StringComparison.Ordinal),
            nick => Assert.StartsWith("Bob", nick, StringComparison.Ordinal),
            nick => Assert.StartsWith("Stiv", nick, StringComparison.Ordinal));
        var agents = result.Executors
            .Select(nick => _f.Executors.List().Single(e => e.Nick == nick))
            .ToList();
        Assert.All(agents, agent => Assert.Equal(ExecutorKind.Ai, agent.Kind));
        Assert.Equal(3, agents.Select(agent => agent.ModelId).Distinct().Count());

        // 2. команда: название по организации, тимлид — человек, ИИ у него в подчинении
        Assert.Equal(orgName + "_team", result.TeamName);
        var team = _f.Teams.List().Single(t => t.Name == result.TeamName);
        Assert.Equal(4, team.Members.Count);
        var lead = Assert.Single(team.Members.Where(m => m.IsLead));
        Assert.Equal(owner.Id, lead.ExecutorId);
        Assert.All(team.Members.Where(m => m.ExecutorId != owner.Id),
            member => Assert.Equal(owner.Id, member.ParentExecutorId));

        // 3. проект: с папкой и с этой командой по умолчанию
        var project = _f.Projects.List().Single(p => p.Id == result.ProjectId);
        Assert.Equal("Первый проект " + orgName, project.Name);
        Assert.Equal(dir, project.FolderPath);
        Assert.Contains(team.Id, project.SettingsJson, StringComparison.Ordinal);
    }

    /// <summary>«Нет типового использования» — исполнителей не заводим вовсе; команда и
    /// проект человеку всё равно нужны, но этот вариант до сборки просто не доходит,
    /// поэтому проверяется сам подбор.</summary>
    [Fact]
    public void No_Usage_Picks_Nothing()
    {
        _f.Models.Seed();

        Assert.Empty(FirstSetup.PickModels(_f.Models, FirstSetup.UsageNone, 3));
        Assert.Empty(FirstSetup.PickModels(_f.Models, FirstSetup.UsageCode, 0));
    }

    /// <summary>Занятые имена не роняют визард на последнем шаге: и ник, и название команды
    /// подбираются свободные.</summary>
    [Fact]
    public void Taken_Names_Do_Not_Break_The_Wizard()
    {
        _f.Models.Seed();
        var orgName = "Дубль" + Guid.NewGuid().ToString("N")[..6];
        _f.Teams.Create(new Team { Name = orgName + "_team" }, null);
        _f.Executors.Create(new Executor { Nick = "Jon", Kind = ExecutorKind.Human }, null);
        var owner = _f.Executors.Create(new Executor
        {
            Nick = "Хозяин2-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Owner,
        }, null);

        var result = FirstSetup.Apply(Services(), orgName, owner, FirstSetup.UsageCode,
            "Проект " + orgName, "");

        Assert.Equal(orgName + "_team-2", result.TeamName);
        var jon = result.Executors[0];
        Assert.StartsWith("Jon-", jon, StringComparison.Ordinal);
        Assert.NotEqual("Jon", jon);
    }

    // ---------- 4. сам визард ----------

    /// <summary>Новые шаги есть, и они идут только у первичного сервера: у подключаемого
    /// рабочее место приедет репликацией.</summary>
    [Fact]
    public void Wizard_Has_The_Two_New_Steps()
    {
        var pages = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "AuthPages.cs"));

        Assert.Contains("StepUsage = \"usage\"", pages, StringComparison.Ordinal);
        Assert.Contains("StepProject = \"project\"", pages, StringComparison.Ordinal);
        // число шагов зависит от ветки: 6 у первичного, 4 у подключаемого
        Assert.Contains("view.Primary ? 6 : 4", pages, StringComparison.Ordinal);
        // каталоги спрашиваются на шаге сервера
        Assert.Contains("Field(\"modelsRepo\"", pages, StringComparison.Ordinal);
        Assert.Contains("Field(\"distDir\"", pages, StringComparison.Ordinal);
        Assert.Contains("Field(\"packagesDir\"", pages, StringComparison.Ordinal);
        // назад с выбора использования нельзя: организация уже создана
        Assert.Contains("view.Step is not (StepLang or StepUsage)", pages, StringComparison.Ordinal);
    }

    /// <summary>Тексты новых шагов есть на обоих языках (иначе на английском экране
    /// человек увидел бы сам ключ словаря).</summary>
    [Fact]
    public void New_Wizard_Texts_Exist_In_Both_Languages()
    {
        var keys = new[]
        {
            "setup.step.usage", "setup.hint.usage", "setup.usage", "setup.usage.code",
            "setup.usage.video", "setup.usage.image", "setup.usage.none", "setup.usage.hint",
            "setup.usage.noModels", "setup.step.project", "setup.hint.project",
            "setup.project.name", "setup.project.dir", "setup.project.dir.hint",
            "setup.adminSame", "setup.adminSame.hint", "setup.dirs.hint", "settings.dirNow",
            "login.error.projectName", "msg.authPages.3",
        };
        foreach (var lang in new[] { "ru", "en" })
        {
            var dict = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json"));
            foreach (var key in keys)
            {
                Assert.Contains("\"" + key + "\":", dict, StringComparison.Ordinal);
            }
        }
    }

    private FirstSetup.Services Services() =>
        new(_f.Executors, _f.Teams, _f.Projects, _f.Models);

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
}
