using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Общая обвязка: каждый тест получает чистое хранилище во временном каталоге.
/// </summary>
public sealed class StorageFixture : IDisposable
{
    public string Dir { get; }
    public Database Db { get; }
    public FileStore Files { get; }
    public EventStore Events { get; }
    public ProjectService Projects { get; }
    public ExecutorService Executors { get; }
    public TeamService Teams { get; }
    public RefDataService RefData { get; }
    public AiModelService Models { get; }
    public TaskService Tasks { get; }
    /// <summary>Объекты проекта (ТЗ пп. 2.5–2.6; T-259): персонажи, локации, эталонные кадры.</summary>
    public ObjectService Objects { get; }
    public JobService Jobs { get; }
    public ChatService Chat { get; }
    public BillingService Billing { get; }
    public ActionCatalogService Actions { get; }
    public SecurityRuleService Security { get; }
    public SecretStore Secrets { get; }
    /// <summary>Консоль заданий (ТЗ v1.45, todo37_3): живой вывод исполнителей.</summary>
    public JobConsole Console { get; } = new();
    /// <summary>Локальные серверы моделей (ТЗ п. 2.9): их вывод тоже идёт в консоль задания.</summary>
    public LocalModelProcessService LocalModels { get; } = new();
    public ComfyUiConnector ComfyUi { get; }

    /// <summary>Облачные медиа-модели через шлюз fal.ai (T-14-S0).</summary>
    public FalAiConnector FalAi { get; }

    /// <summary>Исполнитель «авто ПО» — запуск внешней программы плагина (T-154-S0).</summary>
    public SoftwareConnector Software { get; }

    /// <summary>Живые плагины «этого сервера» для «авто ПО»: тест кладёт сюда свой манифест
    /// вместо каталога plugins/ на диске.</summary>
    public List<GatewayPlugin> Gateways { get; } = [];

    /// <summary>Загрузка объектов проекта в модель (T-14-S1).</summary>
    public ObjectLoadService ObjectLoads { get; }

    /// <summary>Обучение адаптеров LoRA (T-12-S1): пять шагов из профайла модели.</summary>
    public LoraTrainService LoraTrain { get; }

    /// <summary>Репозиторий моделей этого «сервера»: там же лежит каталог адаптеров loras.</summary>
    public string ModelsRepo => Path.Combine(Dir, "models-repo");
    public ConnectorRegistry Connectors { get; }
    /// <summary>Статусы работы участников команд (ТЗ v1.14); запуск задания их отмечает (T-129).</summary>
    public TeamWorkService TeamWork { get; }
    public JobOrchestrator Orchestrator { get; }
    public ExecutorPickService Picker { get; }
    public ImportSourceService Imports { get; }
    public ExperienceService Experience { get; }
    public ScheduleService Schedules { get; }
    public TaskStatusService Statuses { get; }

    /// <summary>Правила уведомлений организации (T-272): закладка «Настройки → Уведомления».</summary>
    public NotificationRuleService NotificationRules { get; }
    /// <summary>
    /// СЕРВЕРНАЯ БД (ТЗ п. 6.4.1, todo40): аккаунты и организации живут вне организации,
    /// поэтому у них своя база. Db выше — база одной (тестовой) организации.
    /// </summary>
    public Database ServerDb { get; }

    public EventStore ServerEvents { get; }

    /// <summary>Организации (ТЗ п. 2.15, todo40).</summary>
    public OrgService Orgs { get; }

    /// <summary>Аккаунты пользователей и вход в систему (ТЗ п. 2.14, todo39).</summary>
    public AccountService Accounts { get; }

    /// <summary>Серверы кластера и их связь с организациями (ТЗ гл. 6, todo41).</summary>
    public ServerService Servers { get; }

    /// <summary>Идентификатор тестовой организации: им подписан её ключ шифрования (этап 45).</summary>
    public const string OrgId = "test-org";

    /// <summary>Ключи API организации в её БД — значения зашифрованы (ТЗ гл. 10, todo45).</summary>
    public ModelKeyStore KeyStore { get; }

    /// <summary>Ключ организации в secrets.json этого «сервера» (не реплицируется, todo45).</summary>
    public OrgSecretKey OrgKeys { get; }
    /// <summary>Состояние UI пер-пользователя (ТЗ v1.46, todo39).</summary>
    public AppStateService AppState { get; }

    public StorageFixture()
    {
        Dir = Path.Combine(Path.GetTempPath(), "ai2p-tests-" + Guid.NewGuid().ToString("N"));
        Db = new Database(Dir, "test.db");
        Db.Init();
        Files = new FileStore(Db.DataDir);
        Events = new EventStore(Db);
        Projects = new ProjectService(Db, Events, Files);
        Executors = new ExecutorService(Db, Events, Files);
        Teams = new TeamService(Db, Events);
        RefData = new RefDataService(Db, Path.Combine(AppContext.BaseDirectory, "i18n"));
        Models = new AiModelService(Db, Events, Files);
        Tasks = new TaskService(Db, Events, Files);
        Objects = new ObjectService(Db, Events);
        // список обучений LoRA показывает номер и код модели и её сегодняшнюю настройку
        // (T-12-S1): связь со справочником — делегатом, ровно как в контексте организации
        Objects.ModelResolver = modelId =>
        {
            var model = Models.Get(modelId);
            if (model is not null)
            {
                Models.ReadProfileSettings(model);
            }
            return model;
        };
        // Files — каталог данных: кадры датасета лежат в нём (T-98-S0)
        LoraTrain = new LoraTrainService(Objects, Models, Projects, () => ModelsRepo, null, Files);
        Jobs = new JobService(Db, Events);
        Chat = new ChatService(Db, Events);
        Billing = new BillingService(Db);
        // справочник действий (ТЗ v1.21, todo24): сид — из файлов дистрибутива
        // i18n/ActionCatalogService_<lang>.json (копируются в выход тестов из AI2P_app/i18n)
        Actions = new ActionCatalogService(Db, Path.Combine(AppContext.BaseDirectory, "i18n"));
        Actions.Seed();
        // правила безопасности (ТЗ гл. 12, todo25)
        Security = new SecurityRuleService(Db, Events);
        // цепочка этапа 2: секреты + коннекторы (Claude, OpenAI-совместимые) + реестр (ТЗ п. 7.2)
        Secrets = new SecretStore(Path.Combine(Dir, "secrets.json"));
        var human = new HumanConnector(Jobs);
        const string baseUrl = "http://localhost:5480/ai2p";
        // автоподбор исполнителя (ТЗ v1.26, todo28)
        // Jobs — «сначала свободные» внутри работающей очереди иерархии (T-160-S0)
        Picker = new ExecutorPickService(Teams, Executors, Projects, RefData, Files, Jobs);
        // опыт по узлам шаблонов (ТЗ п. 2.11, todo32)
        Experience = new ExperienceService(Db, Events);
        var anthropic = new AnthropicConnector(Jobs, Tasks, Executors, Projects, Files, Events, Secrets, Chat, Actions, Security, RefData, Picker, Experience, Console, LocalModels, Teams, () => "ru", baseUrl);
        var text = new OpenAiCompatibleConnector(Jobs, Tasks, Executors, Projects, Files, Events, Secrets, Chat, Actions, Security, RefData, Picker, Experience, Console, LocalModels, Teams, () => "ru", baseUrl);
        // подключение по CLI (todo29): Claude Code headless
        var claudeCli = new ClaudeCliConnector(Jobs, Tasks, Executors, Projects, Files, Events, Secrets, Chat, Actions, Security, RefData, Picker, Experience, Console, LocalModels, Teams, () => "ru", baseUrl);
        // медиа-модели через ComfyUI (ТЗ v1.41, todo36_4)
        // загрузка объектов проекта В МОДЕЛЬ (T-14-S1): эталонный кадр и адаптер LoRA
        ObjectLoads = new ObjectLoadService(Objects, Executors, Files, () => ModelsRepo);
        ComfyUi = new ComfyUiConnector(Jobs, Tasks, Executors, Projects, Files, Events, Console,
            Objects, ObjectLoads, Chat);
        // облачные медиа-модели через шлюз fal.ai (T-14-S0): секреты нужны за ключом API —
        // за него шлюз спрашивает в каждом запросе
        FalAi = new FalAiConnector(Jobs, Tasks, Executors, Projects, Files, Events, Console,
            Secrets, Objects, ObjectLoads, Chat);
        // исполнитель «авто ПО» (T-154-S0): внешняя программа плагина как задание
        Software = new SoftwareConnector(Jobs, Tasks, Executors, Projects, Files, Events, Console)
        {
            Gateways = () => Gateways,
        };
        Connectors = new ConnectorRegistry(Files, human, anthropic, text, claudeCli, ComfyUi, FalAi,
            Software);
        // статусы работы участников — до оркестратора: запуск задания отмечает в них
        // подключение исполнителя (T-129), иначе он висит в списке команды «не подключен»
        TeamWork = new TeamWorkService(Teams, Executors, Projects, Events, Connectors, LocalModels);
        Orchestrator = new JobOrchestrator(Tasks, Jobs, Executors, Projects, Teams, Files, Chat, Connectors, Security, Picker, Experience, null, TeamWork, null, Objects);
        // справочник импортов (ТЗ v1.28, todo30)
        Imports = new ImportSourceService(Db, Events);
        // расписание запуска задач (ТЗ п. 2.12, todo34)
        Schedules = new ScheduleService(Db, Events);
        // справочник состояний задач (ТЗ v1.37, todo34_3)
        Statuses = new TaskStatusService(Db);
        Statuses.Seed();
        // правила уведомлений организации (T-272)
        NotificationRules = new NotificationRuleService(Db, Events);
        // аккаунты, организации и состояние UI пер-пользователя (ТЗ пп. 2.14, 2.15; todo39–40):
        // серверная БД — отдельный файл рядом с базой организации
        ServerDb = new Database(Dir, "server.db", DatabaseKind.Server);
        ServerDb.Init(Db.NodeId);
        ServerEvents = new EventStore(ServerDb);
        Accounts = new AccountService(ServerDb, ServerEvents);
        // почта уведомлений «из логина пользователя» (T-272): в приложении справку об аккаунте
        // даёт OrgRegistry — аккаунты живут в СЕРВЕРНОЙ базе, организация про них не знает
        Executors.AccountLookup = AccountBrief;
        Orgs = new OrgService(ServerDb, ServerEvents);
        // серверы кластера (ТЗ гл. 6, todo41): локальный есть всегда — он же дирижёр
        Servers = new ServerService(ServerDb, ServerEvents);
        AppState = new AppStateService(Db);
        // ключи API организации (ТЗ гл. 10, todo45): хранилище в БД организации + ключ
        // шифрования в secrets.json. Ключ заводится сразу — иначе записать ключ API нечем
        KeyStore = new ModelKeyStore(Db, Events);
        OrgKeys = new OrgSecretKey(Secrets);
        OrgKeys.Ensure(OrgId);
        RefData.Seed();
    }

    /// <summary>
    /// Организация вместе с локальным сервером (ТЗ гл. 6, todo41): в приложении связь заводит
    /// реестр организаций — сервис организаций про серверы не знает. Локальный сервер получает
    /// в новой организации код <c>S0</c> и становится её дирижёром.
    /// </summary>
    public Organization CreateOrgWithLocalServer(string name, string code, string? actorId = null)
    {
        var local = Servers.Local()
                    ?? Servers.EnsureLocal("local", "http", "localhost", 5480, "/ai2p");
        var org = Orgs.Create(name, code, actorId);
        Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, requestedBy: "", actorId);
        return org;
    }

    /// <summary>
    /// Сделать аккаунт участником тестовой организации (ТЗ п. 2.15): участие — это
    /// исполнитель-человек с этим аккаунтом в БД организации. В приложении тем же занят
    /// <c>OrgRegistry.AddMember</c>; сервис аккаунтов организаций не знает.
    /// </summary>
    public Executor AddMember(Account account, SystemRole role, string? actorId = null) =>
        Executors.EnsureMember(account.Id, account.Name, account.Email, role, actorId, AccountBrief);

    /// <summary>Справка об аккаунте для сервиса исполнителей (T-146): в приложении её даёт
    /// <c>OrgRegistry</c> — аккаунты живут в серверной БД, организация про них не знает.</summary>
    public MemberAccount? AccountBrief(string accountId) =>
        Accounts.Get(accountId) is { } found
            ? new MemberAccount(found.Email, found.DeletedAt is null)
            : null;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
            // на Windows файлы БД могут освобождаться с задержкой — не валим тест на уборке
        }
    }
}

public sealed class EventStoreTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seq_Is_Monotonic_Per_Node()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        _f.Projects.Update(project, null);

        var events = _f.Events.Query(limit: 10);
        var seqs = events.Select(e => e.Seq).OrderBy(s => s).ToList();
        Assert.Equal(seqs, seqs.Distinct().ToList());               // без дырок-дублей
        Assert.All(events, e => Assert.Equal(_f.Db.NodeId, e.NodeId));
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.Id)));
    }

    [Fact]
    public void Append_Is_Idempotent_By_Event_Id()
    {
        using var conn = _f.Db.Open();
        var evt = new EventRecord { EventType = "task.created", PayloadJson = "{}" };
        using (var tx = conn.BeginTransaction())
        {
            _f.Events.Append(conn, tx, evt);
            tx.Commit();
        }
        using (var tx = conn.BeginTransaction())
        {
            _f.Events.Append(conn, tx, evt); // повторное применение того же id — пропуск
            tx.Commit();
        }
        Assert.Single(_f.Events.Query(eventType: "task.created"));
    }

    [Fact]
    public void Payload_Is_Stored_Without_Unicode_Escapes()
    {
        // сериализация по умолчанию эскейпит кириллицу — журнал должен хранить буквы (todo_bugfix_1)
        var escaped = System.Text.Json.JsonSerializer.Serialize(
            new { error = "Провайдер ответил 400: превышен контекст" });
        Assert.Contains("\\u", escaped);

        _f.Events.Append(new EventRecord { EventType = "agent.response", PayloadJson = escaped });

        var stored = Assert.Single(_f.Events.Query(eventType: "agent.response"));
        Assert.Contains("Провайдер ответил 400", stored.PayloadJson);
        Assert.DoesNotContain("\\u", stored.PayloadJson);
    }

    [Fact]
    public void JsonText_Readable_Keeps_NonJson_And_Plain_Strings_As_Is()
    {
        Assert.Equal("{}", AI2P.Core.JsonText.Readable("{}"));
        Assert.Equal("не json \\u0432\\u043E", AI2P.Core.JsonText.Readable("не json \\u0432\\u043E"));
        Assert.Equal("""{"a":"текст"}""", AI2P.Core.JsonText.Readable("""{"a":"текст"}"""));
        Assert.Equal("""{"a":"вот"}""", AI2P.Core.JsonText.Readable("{\"a\":\"\\u0432\\u043e\\u0442\"}"));
    }
}

public sealed class ProjectTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Create_Makes_DisplayId_Slug_Dirs_And_Event()
    {
        var project = _f.Projects.Create("Мой Проект", @"C:\work\game", null, null);

        Assert.Equal("PRJ-1", project.DisplayId);
        // каталог хранилища назван внешним кодом проекта (todo37), а не слагом имени
        Assert.Equal("PRJ-1", project.Slug);
        Assert.True(Directory.Exists(Path.Combine(_f.Dir, "projects", project.Slug, "tasks")));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ProjectCreated));
    }

    [Fact]
    public void Slugs_Are_Unique_Codes_Not_Transliterated_Names()
    {
        // разбор слагов из имён убран (todo37): каталоги называются кодами и не конфликтуют
        var p1 = _f.Projects.Create("Мой проект", null, null, null);
        var p2 = _f.Projects.Create("Moy proekt", null, null, null);
        Assert.Equal(p1.DisplayId, p1.Slug);
        Assert.Equal(p2.DisplayId, p2.Slug);
        Assert.NotEqual(p1.Slug, p2.Slug);
    }

    [Fact]
    public void Duplicate_Name_Is_Rejected()
    {
        _f.Projects.Create("Alpha", null, null, null);
        Assert.Throws<ArgumentException>(() => _f.Projects.Create("Alpha", null, null, null));
    }

    /// <summary>
    /// УДАЛЕНИЕ ПРОЕКТА (T-44-S0, выпуск 1.105) — второй исход формы «перенести в архив или
    /// удалить совсем». Проект уходит ВМЕСТЕ СО СВОИМ СОДЕРЖИМЫМ: задача проекта, оставшись
    /// без него, не видна ни в одном списке, а связи из неё ведут в никуда. Повторное
    /// удаление ничего не меняет — второго события в журнале нет.
    /// </summary>
    [Fact]
    public void Delete_Removes_Project_With_Its_Tasks()
    {
        var project = _f.Projects.Create("Проект под снос", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = human.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Задача",
            ExecutorIds = [human.Id],
            ResponsibleId = human.Id,
        }, "тело", "", null);

        _f.Projects.Delete(project.Id, null);

        Assert.DoesNotContain(_f.Projects.List(), p => p.Id == project.Id);
        Assert.DoesNotContain(_f.Tasks.List(), t => t.Id == task.Id);
        Assert.Single(_f.Events.Query(eventType: EventTypes.ProjectDeleted));

        _f.Projects.Delete(project.Id, null);
        Assert.Single(_f.Events.Query(eventType: EventTypes.ProjectDeleted));
    }
}

/// <summary>Справочник ИИ-моделей и форма исполнителя (ТЗ пп. 2.2, 2.9; todo15).</summary>
public sealed class AiModelAndExecutorTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Creates_Distribution_Models_With_Files_Idempotently()
    {
        _f.Models.Seed();
        _f.Models.Seed(); // повторный запуск не дублирует

        var models = _f.Models.List();
        // 2×Claude API + 2×Claude CLI (todo29) + 2×DeepSeek + Kandinsky (todo36_3) + Qwen (todo36_5)
        // + 23 записи по таблицам §5 отчёта T-213 (T-214) + Kandinsky I2V (T-258)
        // + 3 локальные модели изображений через ComfyUI (T-241, §6.2 того же отчёта)
        // + 12 облачных медиа-моделей через шлюз fal.ai (T-14-S0, §6.2–6.5)
        // + 9 локальных видео-моделей через ComfyUI (T-15-S0, §6.1 и §6.3 того же отчёта:
        //   Wan 2.2 t2v и i2v, HunyuanVideo 1.5, LTX-2.5 и пять вариантов Kandinsky Lite)
        // + 3 локальные модели звука через ComfyUI (T-18-S0, §6.4: ACE-Step 1.5 XL
        //   base, sft и turbo)
        // + 6 локальных моделей изображений, вторая очередь (T-19-S0, §6.2 и §7.2:
        //   FLUX.2 klein 4B в двух режимах, FLUX.2 dev, Kandinsky 5.0 Image Lite,
        //   SD 3.5 Large и SDXL 1.0)
        // + 3 локальные 3D-модели через ComfyUI (T-20-S0, §6.5: Hunyuan3D 2.1, TRELLIS 2
        //   и TripoSplat — картинка → сетка либо гауссовы сплаты)
        // + 9 записей актуализации справочника по рынку (T-216-S0, сверка 11.09.2026:
        //   GPT-6-Astra, Claude-Fable-5.1, Gemini-3.8-Flash, Muse-Spark-1.3,
        //   DeepSeek-V4.1-Flash, GLM-5.3 — текст; GPT-Image-2.5, Wan-3.0-Prime и
        //   MiniMax-H3-Max — медиа через шлюз fal.ai)
        // — точный состав проверяет T214Tests
        Assert.Equal(77, models.Count);
        Assert.All(models, m => Assert.False(m.IsCustom));           // из дистрибутива
        var claude = models.Single(m => m.Name == "Claude-Fable-5");
        Assert.Equal($"models/profile_{claude.Id}.json", claude.ProfilePath);
        Assert.Equal($"models/scope_{claude.Id}.json", claude.CapabilitiesPath);
        Assert.True(File.Exists(Path.Combine(_f.Dir, "models", $"profile_{claude.Id}.json")));
        Assert.True(File.Exists(Path.Combine(_f.Dir, "models", $"scope_{claude.Id}.json")));
    }

    [Fact]
    public void Model_Name_Must_Be_Unique()
    {
        _f.Models.Create(new AiModel { Name = "Test-Model-1" }, null);
        Assert.Throws<ArgumentException>(() =>
            _f.Models.Create(new AiModel { Name = "Test-Model-1" }, null));
    }

    [Fact]
    public void Nick_Must_Be_Unique()
    {
        _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        Assert.Throws<ArgumentException>(() =>
            _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Ai }, null));
    }

    [Fact]
    public void Human_Internal_Name_Must_Be_Unique()
    {
        _f.Executors.Create(new Executor
        {
            Nick = "mike", InternalName = "Иванов Иван", Kind = ExecutorKind.Human,
        }, null);
        Assert.Throws<ArgumentException>(() => _f.Executors.Create(new Executor
        {
            Nick = "mike2", InternalName = "Иванов Иван", Kind = ExecutorKind.Human,
        }, null));
    }

    [Fact]
    public void Human_Gets_Profile_And_Scope_Files_By_Scheme()
    {
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);

        Assert.Equal($"executors/profile_{human.Id}.json", human.ProfilePath);
        Assert.Equal($"executors/scope_{human.Id}.json", human.CapabilitiesPath);
        Assert.True(File.Exists(Path.Combine(_f.Dir, "executors", $"profile_{human.Id}.json")));
    }

    [Fact]
    public void Ai_Executor_Syncs_Name_And_Files_From_Model()
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "DeepSeek-V4-Pro");

        var ai = _f.Executors.Create(new Executor
        {
            Nick = "кодер", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);

        Assert.Equal(model.Name, ai.InternalName);                 // синхронно из справочника
        Assert.Equal(model.ProfilePath, ai.ProfilePath);
        Assert.Equal(model.CapabilitiesPath, ai.CapabilitiesPath);
    }

    [Fact]
    public void Inactive_Model_Rejected_For_New_Ai_Executor()
    {
        var model = _f.Models.Create(new AiModel { Name = "Test-Off", IsActive = false }, null);
        Assert.Throws<ArgumentException>(() => _f.Executors.Create(new Executor
        {
            Nick = "bot", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null));
    }

    [Fact]
    public void Team_Duplicate_Name_Is_Rejected()
    {
        _f.Teams.Create(new Team { Name = "Команда" }, null);
        Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team { Name = "Команда" }, null));
    }
}

public sealed class TaskTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private (Project project, Executor human, Team team) Arrange()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = human.Id }],
        }, null);
        return (project, human, team);
    }

    [Fact]
    public void Create_Writes_Description_File_And_Events()
    {
        var (project, human, team) = Arrange();
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Задача 1",
            ExecutorIds = [human.Id],
            ResponsibleId = human.Id,
        }, "# Описание\nтело", "", null);

        Assert.Equal("T-1", task.DisplayId);
        Assert.Equal($"projects/{project.Slug}/tasks/T-1/description.md", task.DescriptionPath);
        Assert.Equal("# Описание\nтело", _f.Tasks.ReadDescription(task));
        Assert.Single(_f.Events.Query(eventType: EventTypes.TaskCreated));
        Assert.Single(_f.Events.Query(eventType: EventTypes.TaskAssigned));
    }

    [Fact]
    public void Executor_Must_Be_Team_Member()
    {
        var (project, _, team) = Arrange();
        var outsider = _f.Executors.Create(new Executor { Nick = "чужой", Kind = ExecutorKind.Human }, null);

        Assert.Throws<ArgumentException>(() => _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Задача",
            ExecutorIds = [outsider.Id],
        }, "", "", null));
    }

    [Fact]
    public void Responsible_Must_Be_Human()
    {
        var (project, human, team) = Arrange();
        var ai = _f.Executors.Create(new Executor { Nick = "bot", Kind = ExecutorKind.Ai }, null);

        Assert.Throws<ArgumentException>(() => _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ResponsibleId = ai.Id,
        }, "", "", null));
    }

    [Fact]
    public void StatusChange_Writes_Event()
    {
        var (project, _, _) = Arrange();
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" }, "", "", null);

        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);

        var events = _f.Events.Query(eventType: EventTypes.TaskStatusChanged);
        Assert.Single(events);
        Assert.Contains("in_progress", events[0].PayloadJson);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(task.Id)!.Status);
    }

    [Fact]
    public void Delete_Is_Soft_And_Moves_Files_To_Trash()
    {
        var (project, _, _) = Arrange();
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" }, "тело", "", null);

        _f.Tasks.Delete(task.Id, null);

        Assert.NotNull(_f.Tasks.Get(task.Id)!.DeletedAt);          // мягкое удаление
        Assert.Empty(_f.Tasks.List(project.Id));                   // из списков ушла
        Assert.True(Directory.Exists(Path.Combine(_f.Dir, "projects", project.Slug, ".trash", "T-1")));
    }

    [Fact]
    public void Subtask_Keeps_Parent_Id()
    {
        var (project, _, _) = Arrange();
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            ParentId = parent.Id,
            Title = "Подзадача",
        }, "", "", null);

        Assert.Equal(parent.Id, _f.Tasks.Get(child.Id)!.ParentId);
        Assert.Single(_f.Tasks.List(project.Id), t => t.ParentId == parent.Id);
    }

    [Fact]
    public void Templates_Do_Not_Appear_On_Board()
    {
        var (project, _, _) = Arrange();
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Шаблон", IsTemplate = true }, "", "", null);

        Assert.Empty(_f.Tasks.List(project.Id));
        Assert.Single(_f.Tasks.List(project.Id, includeTemplates: true));
    }
}

/// <summary>Признак «активен» (ТЗ v1.10, пп. 2.2, 2.7, 2.8), фильтры и поиск (гл. 5).</summary>
public sealed class ActiveFlagAndFilterTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Inactive_Project_Rejected_For_New_Task()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        project.IsActive = false;
        _f.Projects.Update(project, null);

        Assert.Throws<ArgumentException>(() =>
            _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" }, "", "", null));
    }

    [Fact]
    public void Inactive_Executor_Rejected_For_New_Team_And_Task()
    {
        var human = _f.Executors.Create(new Executor { Nick = "off", Kind = ExecutorKind.Human }, null);
        human.IsActive = false;
        _f.Executors.Update(human, null);

        Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members = [new TeamMember { ExecutorId = human.Id }],
        }, null));
        Assert.Throws<ArgumentException>(() =>
            _f.Tasks.Create(new TaskItem { Title = "Задача", ExecutorIds = [human.Id] }, "", "", null));
    }

    [Fact]
    public void Inactive_Team_Rejected_For_New_Project()
    {
        var team = _f.Teams.Create(new Team { Name = "Команда" }, null);
        team.IsActive = false;
        _f.Teams.Update(team, null);

        Assert.Throws<ArgumentException>(() => _f.Projects.Create("Проект", null, team.Id, null));
    }

    [Fact]
    public void Existing_Task_Of_Inactive_Project_Still_Updates()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" }, "", "", null);
        project.IsActive = false;
        _f.Projects.Update(project, null);

        task.Title = "Задача 2";
        var updated = _f.Tasks.Update(task, "тело", "", null); // существующие связи не рвутся

        Assert.Equal("Задача 2", updated.Title);
    }

    [Fact]
    public void PlannedHours_Roundtrip()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            PlannedHours = 2.5,
        }, "", "", null);

        Assert.Equal(2.5, _f.Tasks.Get(task.Id)!.PlannedHours);
    }

    [Fact]
    public void List_Filters_By_Status_Executor_And_Due_Interval()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Моя в работе",
            Status = TaskStatuses.InProgress,
            ExecutorIds = [human.Id],
            DueDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        }, "", "", null);
        _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Чужая",
            DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
        }, "", "", null);

        Assert.Single(_f.Tasks.List(status: TaskStatuses.InProgress));
        Assert.Single(_f.Tasks.List(executorId: human.Id));
        Assert.Single(_f.Tasks.List(mineId: human.Id));
        // открытый интервал «по» (ТЗ гл. 5)
        Assert.Single(_f.Tasks.List(dueTo: new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(2, _f.Tasks.List(dueFrom: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)).Count);
    }

    [Fact]
    public void Search_Looks_Into_Title_And_Description_Excluding_Templates()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Починить сборку" },
            "падает на линковке", "", null);
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Другое" }, "про тесты", "", null);
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Шаблон линковки", IsTemplate = true },
            "линковка", "", null);

        Assert.Single(_f.Tasks.List(search: "ЛИНКОВК"));               // описание, без учёта регистра
        Assert.Single(_f.Tasks.List(search: "починить"));              // заголовок
        Assert.Single(_f.Tasks.List(templatesOnly: true, search: "линковк")); // поиск шаблона отдельно
    }
}

/// <summary>«Создать бизнес-процесс из шаблона» (ТЗ пп. 2.3, 2.4; todo14).</summary>
public sealed class InstantiateTemplateTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private (TaskItem head, TaskItem child, TaskItem grandchild) ArrangeTemplate(
        string? projectId, DateTime? headDue, DateTime? childDue)
    {
        var head = _f.Tasks.Create(new TaskItem
        {
            ProjectId = projectId,
            Title = "Шаблон-процесс",
            Kind = TaskKind.Process,
            IsTemplate = true,
            DueDate = headDue,
        }, "описание головы", "критерии", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = projectId,
            ParentId = head.Id,
            Title = "Шаг 1",
            IsTemplate = true,
            DueDate = childDue,
            PlannedHours = 4,
        }, "описание шага", "", null);
        var grandchild = _f.Tasks.Create(new TaskItem
        {
            ProjectId = projectId,
            ParentId = child.Id,
            Title = "Подшаг 1.1",
            IsTemplate = true,
        }, "", "", null);
        return (head, child, grandchild);
    }

    [Fact]
    public void Copies_Hierarchy_Unsets_Template_And_Writes_Event()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var (head, _, _) = ArrangeTemplate(project.Id, null, null);

        var newHead = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        Assert.False(newHead.IsTemplate);
        Assert.Equal(head.Id, newHead.TemplateId);
        Assert.Equal(TaskStatuses.Pending, newHead.Status);
        Assert.Null(newHead.ParentId);
        Assert.Equal("описание головы", _f.Tasks.ReadDescription(_f.Tasks.Get(newHead.Id)!));
        Assert.Equal("критерии", _f.Tasks.ReadAcceptance(_f.Tasks.Get(newHead.Id)!));

        // вся иерархия скопирована: голова + шаг + подшаг, признак шаблона снят
        var copies = _f.Tasks.List(project.Id);
        Assert.Equal(3, copies.Count);
        var newChild = copies.Single(t => t.ParentId == newHead.Id);
        Assert.Single(copies, t => t.ParentId == newChild.Id);
        Assert.Equal(4, newChild.PlannedHours);
        Assert.Single(_f.Events.Query(eventType: EventTypes.TemplateApplied));
        // исходный шаблон не тронут
        Assert.True(_f.Tasks.Get(head.Id)!.IsTemplate);
    }

    [Fact]
    public void BaseDate_Shifts_All_Due_Dates()
    {
        var headDue = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var childDue = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var (head, _, _) = ArrangeTemplate(null, headDue, childDue);

        var baseDate = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc); // сдвиг +41 день
        var newHead = _f.Tasks.InstantiateTemplate(head.Id, baseDate, null);

        Assert.Equal(baseDate, newHead.DueDate);
        var newChild = _f.Tasks.List().Single(t => t.ParentId == newHead.Id);
        Assert.Equal(childDue.AddDays(41), newChild.DueDate);
    }

    [Fact]
    public void Without_BaseDate_Dates_Are_Copied_As_Is()
    {
        var headDue = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var childDue = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var (head, _, _) = ArrangeTemplate(null, headDue, childDue);

        var newHead = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        Assert.Equal(headDue, newHead.DueDate);
        Assert.Equal(childDue, _f.Tasks.List().Single(t => t.ParentId == newHead.Id).DueDate);
    }

    [Fact]
    public void Non_Template_Is_Rejected()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Обычная" }, "", "", null);

        Assert.Throws<ArgumentException>(() => _f.Tasks.InstantiateTemplate(task.Id, null, null));
    }
}

public sealed class HumanJobFlowTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Full_Human_Loop_Start_Inbox_Answer()
    {
        // задача на человека → запуск → Inbox → ответ → review (ТЗ пп. 3, 7.2)
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = human.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Сделать руками",
            ExecutorIds = [human.Id],
        }, "инструкция", "", null);

        var job = await _f.Orchestrator.StartTaskAsync(task.Id, human.Id);

        Assert.Equal(JobState.WaitingHuman, _f.Jobs.Get(job.Id)!.State);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(task.Id)!.Status);
        var inbox = _f.Jobs.Inbox();
        Assert.Single(inbox);
        Assert.Equal("T-1", inbox[0].TaskDisplayId);

        var done = _f.Orchestrator.AnswerJob(job.Id, "готово, вот результат", human.Id);

        Assert.Equal(JobState.Done, done.State);
        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.Status);
        Assert.Empty(_f.Jobs.Inbox());
        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        Assert.Single(artifacts);
        Assert.Equal("готово, вот результат", _f.Files.ReadText(artifacts[0]));
        Assert.Single(_f.Events.Query(eventType: EventTypes.JobCompleted));
    }

    [Fact]
    public async Task Start_Requires_Executor()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Без исполнителя" },
            "", "", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _f.Orchestrator.StartTaskAsync(task.Id, null));
    }
}
