using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Server.Org;

/// <summary>
/// Все сервисы ОДНОЙ организации (ТЗ п. 2.15, этап 40).
///
/// До этапа 40 такой набор существовал в единственном экземпляре: 36 синглтонов в
/// <c>Program.cs</c>, каждый со ссылкой на единственную <see cref="Database"/>. Организация —
/// это отдельная БД и отдельный каталог, поэтому набор стал пер-организационным: контекст
/// создаётся по требованию и живёт до конца работы приложения (<see cref="OrgRegistry"/>).
/// Фоновые процессы (расписание, работа команды, консоли заданий) тоже свои у каждой
/// организации — они держат состояние в памяти и общими быть не могут.
///
/// Пути в БД организации — относительные, а корень у каждой свой
/// (<c>data/orgs/&lt;ORG-N&gt;/</c>), поэтому файловый слой (<see cref="FileStore"/>)
/// изолируется сам собой: править его под организации не пришлось.
/// </summary>
public sealed class OrgContext : IDisposable
{
    public Organization Org { get; private set; }

    /// <summary>Код организации в URL: <c>/ai2p/&lt;код&gt;/…</c> (ТЗ гл. 11).</summary>
    public string Code => Org.Code;

    public Database Db { get; }
    public FileStore Files { get; }
    public EventStore Events { get; }
    public ProjectService Projects { get; }
    public ExecutorService Executors { get; }
    public TeamService Teams { get; }
    public RefDataService RefData { get; }
    public TaskStatusService TaskStatuses { get; }
    public AiModelService Models { get; }
    public TaskService Tasks { get; }

    /// <summary>Объекты проекта (ТЗ пп. 2.5–2.6; T-259): персонажи, локации, реквизит,
    /// стиль, эталонные кадры и адаптеры LoRA — то, на что ссылаются описания задач.</summary>
    public ObjectService Objects { get; }

    /// <summary>Обучение адаптеров LoRA (T-12-S1): исполняет то, что справочник моделей
    /// объявил настройкой <c>lora.train</c> — датасет, запуск, ожидание, результат.</summary>
    public LoraTrainService LoraTrain { get; }

    /// <summary>Загрузка объектов проекта в модель (T-14-S1): эталонный кадр и адаптер LoRA
    /// при запуске задания — что уедет в модель и что этому мешает.</summary>
    public ObjectLoadService ObjectLoads { get; }
    public JobService Jobs { get; }
    public ChatService Chat { get; }

    /// <summary>Заявки на запуск задачи с ДРУГОГО сервера кластера (T-196): нажать
    /// «запустить» можно везде, выполняет — владелец задачи.</summary>
    public RunRequestService RunRequests { get; }

    /// <summary>Заявки на СМЕНУ ДИРИЖЁРА организации (T-21-S1): передача дирижёрства —
    /// двусторонняя операция, заявка едет второй стороне обычной репликацией.</summary>
    public ConductorRequestService ConductorRequests { get; }
    public BillingService Billing { get; }
    public AppStateService AppState { get; }
    public ActionCatalogService Actions { get; }
    public SecurityRuleService SecurityRules { get; }
    public ExecutorPickService ExecutorPick { get; }
    public ExperienceService Experience { get; }
    public JobConsole JobConsole { get; }
    public ConnectorRegistry Connectors { get; }
    public JobOrchestrator Orchestrator { get; }
    public ModelInstallService ModelInstalls { get; }
    public ModelKeyService ModelKeys { get; }

    /// <summary>Хранилище ключей API организации (ТЗ гл. 10, этап 45): значения зашифрованы,
    /// строки реплицируются обычным журналом изменений.</summary>
    public ModelKeyStore KeyStore { get; }

    /// <summary>Ключ организации на ЭТОМ сервере (secrets.json, не реплицируется).</summary>
    public OrgSecretKey OrgKeys { get; }
    public TeamWorkService TeamWork { get; }
    public ImportSourceService Imports { get; }

    /// <summary>Ключ и токен источников импорта (ТЗ v1.65, T-123): вводятся в интерфейсе,
    /// лежат в БД организации зашифрованными — как ключи API моделей.</summary>
    public ImportKeyService ImportKeys { get; }
    public TrelloImporter Trello { get; }

    /// <summary>Импорт задач из GitLab (T-249, ТЗ v1.89): второй вид источника справочника
    /// импортов — issue проекта GitLab становится задачей.</summary>
    public GitLabImporter GitLab { get; }

    /// <summary>Импорт задач из GitHub (T-247, ТЗ v1.89): задача (issue) репозитория
    /// становится задачей AI2P, обсуждение — чатом задачи.</summary>
    public GitHubImporter GitHub { get; }

    /// <summary>Получение внешних файлов задания для агентов (T-255, ТЗ v1.90): картинка
    /// по ссылке — из кэша, из хранилища AI2P или из интернета.</summary>
    public FileFetchService Fetch { get; }
    public ScheduleService Schedules { get; }
    public ScheduleRunner ScheduleRunner { get; }

    /// <summary>Правила уведомлений организации (T-272): закладка «Настройки → Уведомления».</summary>
    public NotificationRuleService NotificationRules { get; }

    /// <summary>Сборка и отправка уведомлений (T-272): подписана на смену статуса задачи.</summary>
    public NotificationService Notifications { get; }

    /// <summary>Сторож сроков (T-272): раз в минуту смотрит, не подошёл ли срок задач
    /// ЭТОГО сервера. Остальные события уведомлений приходят сменой статуса.</summary>
    public NotificationRunner NotificationRunner { get; }

    /// <summary>Архивы организации (T-40-S0, выпуск 1.105): реестр, состояние на ЭТОМ
    /// сервере и операции с файлами — создать, открыть, закрыть, удалить с сервера.</summary>
    public ArchiveService Archives { get; }

    /// <summary>Правила архивации (T-41-S0): общие правила организации и правила каждого
    /// архива — одна таблица, две группы, отличает их только ссылка на архив.</summary>
    public ArchiveRuleService ArchiveRules { get; }

    /// <summary>Отбор кандидатов на архивацию по правилу (T-41-S0): нужен автоматической
    /// архивации (T-46-S0) и форме добавления архива (T-43-S0).</summary>
    public ArchiveCandidateService ArchiveCandidates { get; }

    /// <summary>Записи плагинов организации (T-111-S0): шлюзы в видеоредакторы, конверторы,
    /// подключения к серверам MCP. Пути к софту здесь не хранятся — они в config.json.</summary>
    public PluginService Plugins { get; }

    /// <summary>Жизненный цикл плагина (T-112-S0): инициализация, регистрация действий в
    /// справочнике, записи опыта, снятие. Поверх <see cref="Plugins"/>, <see cref="Experience"/>
    /// и <see cref="RefData"/> — своих таблиц у него нет.</summary>
    public PluginSetupService PluginSetup { get; }

    /// <summary>НАБОРЫ ОПЫТА — «библиотека стилей работы» (T-270-S0): установка пачки записей
    /// в выбранную область, снятие по пометке владельца <c>pack:&lt;код&gt;</c> и выгрузка
    /// отобранных записей файлом <c>packs/&lt;код&gt;/pack.json</c>. Своих таблиц нет — поверх
    /// <see cref="Experience"/>, <see cref="RefData"/> и каталога данных.</summary>
    public ExperiencePackService ExperiencePacks { get; }

    /// <summary>Записи плагинов, заводимые установкой модели по её пакетам обучения
    /// (T-156-S0): тренер LoRA появляется в списке «Плагины и MCP» вместе с программой.</summary>
    public TrainerPluginService TrainerPlugins { get; }

    /// <summary>Обучение LoRA задачей (T-157-S0): шаблон → исполнитель «авто ПО» → задача.</summary>
    public LoraTrainTaskService LoraTrainTasks { get; }

    /// <summary>Перенос данных в текущий архив и восстановление обратно (T-42-S0): рабочая
    /// механика, которую зовут формы ручной (T-44-S0) и автоматической (T-46-S0) архивации.</summary>
    public ArchiveTransferService ArchiveTransfer { get; }

    /// <summary>Автоматическая архивация (T-46-S0): отбор по правилам текущего архива плюс
    /// перенос, ТОЛЬКО на дирижёре. Зовётся расписанием и формой добавления архива.</summary>
    public AutoArchiveService AutoArchive { get; }

    /// <summary>Базовый URL организации для внешних ссылок агента на файлы (ТЗ гл. 11):
    /// <c>{протокол}://{hostname}:{порт}{basePath}/{код организации}</c>.</summary>
    public string PublicBaseUrl { get; }

    /// <summary>
    /// Этот сервер — дирижёр организации (ТЗ гл. 6, этап 41). Автоматически запускать задачи —
    /// и по логике, и по расписанию — может только дирижёр: иначе на каждом сервере кластера
    /// сработало бы одно и то же расписание и задача запустилась бы столько раз, сколько
    /// в организации серверов. Спрашивается у реестра каждый раз: дирижёра переназначают
    /// на ходу, а контекст живёт до конца работы приложения.
    /// </summary>
    public bool IsConductor => _isConductor(Org.Id);

    private readonly Func<string, bool> _isConductor;

    /// <summary>
    /// Кто мы в кластере с точки зрения ЭТОЙ организации (ТЗ гл. 6, этап 42): внутренний ключ
    /// локального сервера, его код (<c>S0</c>, <c>S1</c>, …) и признак дирижёра. Отсюда
    /// сервисы хранилища узнают, какие строки здесь правятся, а какие видны только для чтения.
    /// </summary>
    public ServerScope Scope { get; }

    public OrgContext(Organization org, string orgDir, string nodeId, OrgDeps deps)
    {
        _isConductor = deps.IsConductor;
        Org = org;
        // серверы лежат в СЕРВЕРНОЙ БД, а сервисы работают с БД организации — связь через
        // делегаты: значения спрашиваются на каждом обращении, потому что дирижёра
        // переназначают на ходу, а контекст организации живёт до конца работы приложения
        Scope = new ServerScope(
            serverId: deps.LocalServerId,
            code: () => deps.LocalCode(Org.Id),
            isConductor: () => IsConductor,
            codeOf: serverId => deps.ServerCode(Org.Id, serverId),
            nameOf: deps.ServerName);
        Db = new Database(orgDir, deps.DbFile);
        Db.Init(nodeId);
        Files = new FileStore(Db.DataDir);
        Events = new EventStore(Db);
        Projects = new ProjectService(Db, Events, Files, Scope);
        Executors = new ExecutorService(Db, Events, Files, Scope);
        Teams = new TeamService(Db, Events);
        RefData = new RefDataService(Db, deps.I18nDir);
        TaskStatuses = new TaskStatusService(Db);
        Models = new AiModelService(Db, Events, Files, Scope);
        Tasks = new TaskService(Db, Events, Files, Scope);
        // объекты проекта (T-259): с T-102-S0 у них есть свой сервер-владелец — как у задач,
        // объект правится только на нём, а партнёрам строка едет обычным журналом изменений
        Objects = new ObjectService(Db, Events, Scope);
        // список обучений показывает НОМЕР и КОД модели и её сегодняшнюю настройку LoRA,
        // а справочник — не дело сервиса объектов: связь делегатом, как ActivationGuard
        Objects.ModelResolver = modelId =>
        {
            var model = Models.Get(modelId);
            if (model is not null)
            {
                Models.ReadProfileSettings(model);
            }
            return model;
        };
        // обучение адаптера (T-12-S1): все пять шагов приходят из профайла модели, сервис
        // только исполняет их и записывает, чем кончилось
        // files — каталог данных организации: кадры датасета лежат в нём (T-98-S0)
        // JobConsole — живой вывод тренера (T-152-S0), поэтому она заводится ДО обучения
        JobConsole = new JobConsole();
        LoraTrain = new LoraTrainService(Objects, Models, Projects, deps.ModelsRepo, null, Files,
            JobConsole);
        Jobs = new JobService(Db, Events, Scope);
        Chat = new ChatService(Db, Events);
        RunRequests = new RunRequestService(Db, Events, Scope);
        ConductorRequests = new ConductorRequestService(Db, Events, Scope);
        Billing = new BillingService(Db);
        AppState = new AppStateService(Db);
        Actions = new ActionCatalogService(Db, deps.I18nDir);
        SecurityRules = new SecurityRuleService(Db, Events, Scope);
        // Jobs — чтобы подбор умел «сначала свободные» внутри работающей иерархии (T-160-S0)
        ExecutorPick = new ExecutorPickService(Teams, Executors, Projects, RefData, Files, Jobs);
        Experience = new ExperienceService(Db, Events, Scope);
        PublicBaseUrl = deps.PublicBaseUrl() + "/" + org.Code;

        // ключи API — организации (ТЗ гл. 10, этап 45): значения лежат в её БД зашифрованными
        // ключом организации, который живёт в secrets.json этого сервера и не реплицируется.
        // Создаётся ДО коннекторов: они берут ключи через него, а не из secrets.json напрямую
        KeyStore = new ModelKeyStore(Db, Events);
        OrgKeys = new OrgSecretKey(deps.Secrets);
        ModelKeys = new ModelKeyService(Models, Files, deps.Secrets, KeyStore, OrgKeys, org.Id);

        var human = new HumanConnector(Jobs);
        var anthropic = new AnthropicConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            deps.Secrets, Chat, Actions, SecurityRules, RefData, ExecutorPick, Experience,
            JobConsole, deps.LocalModels, Teams, deps.Language, PublicBaseUrl);
        var text = new OpenAiCompatibleConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            deps.Secrets, Chat, Actions, SecurityRules, RefData, ExecutorPick, Experience,
            JobConsole, deps.LocalModels, Teams, deps.Language, PublicBaseUrl);
        var claudeCli = new ClaudeCliConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            deps.Secrets, Chat, Actions, SecurityRules, RefData, ExecutorPick, Experience,
            JobConsole, deps.LocalModels, Teams, deps.Language, PublicBaseUrl);
        // медиа-коннектор получает объекты проекта (T-259): промптом ему служит описание
        // задачи, и ссылка @obj: — единственный способ дать модели постоянного персонажа.
        // Загрузка объектов В МОДЕЛЬ (T-14-S1) — рядом: эталонный кадр и адаптер LoRA
        // промптом не передаются, их надо залить в движок и подключить к самой модели
        ObjectLoads = new ObjectLoadService(Objects, Executors, Files, deps.ModelsRepo);
        var comfy = new ComfyUiConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            JobConsole, Objects, ObjectLoads, Chat);
        // ОБЛАЧНОЕ медиа через шлюз fal.ai (T-14-S0): тот же набор зависимостей, что у
        // ComfyUI, плюс секреты — за каждый запрос шлюз спрашивает ключ API
        var falAi = new FalAiConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            JobConsole, deps.Secrets, Objects, ObjectLoads, Chat);
        // ключ API коннектор берёт у организации, а не из secrets.json (ТЗ гл. 10, этап 45)
        anthropic.KeyResolver = ModelKeys.ResolveWithSource;
        text.KeyResolver = ModelKeys.ResolveWithSource;
        claudeCli.KeyResolver = ModelKeys.ResolveWithSource;
        falAi.KeyResolver = ModelKeys.ResolveWithSource;
        // ИСПОЛНИТЕЛЬ «АВТО ПО» (T-154-S0): задание выполняет внешняя программа плагина.
        // Список живых плагинов — тот же, что публикуется агенту: манифест на диске, годная
        // запись организации и найденный ЗДЕСЬ путь к программе (LiveGateways)
        var software = new SoftwareConnector(Jobs, Tasks, Executors, Projects, Files, Events,
            JobConsole) { Gateways = LiveGateways };
        Connectors = new ConnectorRegistry(Files, human, anthropic, text, claudeCli, comfy, falAi,
            software);
        // автозапуски (потомки, разблокировка, разбиение) идут у ВЛАДЕЛЬЦА задачи (гл. 6,
        // этап 42): задачи без сервера ведёт дирижёр, свои — каждый сервер сам
        // статусы работы участников строятся ДО оркестратора: запуск задания с карточки задачи
        // отмечает в них подключение исполнителя (T-129) — иначе он висел «не подключен»
        TeamWork = new TeamWorkService(Teams, Executors, Projects, Events, Connectors, deps.LocalModels);
        Orchestrator = new JobOrchestrator(Tasks, Jobs, Executors, Projects, Teams, Files, Chat,
            Connectors, SecurityRules, ExecutorPick, Experience, () => IsConductor, TeamWork,
            RunRequests, Objects);
        // СУФЛЁР с T-292-S0 — это ОТДЕЛЬНЫЙ ИСПОЛНИТЕЛЬ (Executor.PrompterExecutorId), и его
        // задание идёт обычным коннектором: подключать ему что-либо отдельно не нужно, поэтому
        // прежней связки «оркестратор → PrompterService» здесь больше нет. От T-288-S0 остались
        // только СТАТИЧЕСКИЕ части PrompterService — сборка промпта и вырезание json из ответа
        ModelInstalls = new ModelInstallService(Models, Files, Events,
            deps.ModelsRepo, deps.DistDir, deps.PackagesDir)
        {
            // дополнительные опции установки (T-190-S0) — выбор ЭТОГО компьютера, он лежит
            // в config.json и не реплицируется: тренер LoRA нужен одному серверу кластера
            OptionsOff = deps.ModelOptionsOff,
            SaveOptionsOff = deps.SaveModelOptionsOff,
        };
        // обучению адаптера нужны те же пути, что и запуску модели (T-289): каталог весов
        // группы, файл DiT, каталог пакета обучающего репозитория. Знает их установщик,
        // а заводится он позже сервиса обучения — поэтому связь делегатом
        LoraTrain.ModelPaths = (model, value) => ModelInstalls.ExpandPaths(model.Id, value);
        // пакеты обучения ставит установка модели (T-4-S0), обучение их только проверяет
        LoraTrain.MissingPackages = ids => ModelInstalls.MissingPackages(ids);
        // активность модели = установленность (v1.41) + наличие ключа API (v1.42)
        Models.ActivationGuard = model =>
            ModelInstalls.ActivationError(model.Id) ?? ModelKeys.ActivationError(model.Id);
        Imports = new ImportSourceService(Db, Events);
        // ключ и токен источника импорта — там же, где ключи API моделей (ТЗ гл. 10, v1.65):
        // в БД организации, зашифрованными. Источник без обоих значений активным быть не может
        ImportKeys = new ImportKeyService(Imports, ModelKeys);
        Imports.ActivationGuard = ImportKeys.ActivationError;
        // чат нужен импортёру для переноса обсуждения карточки в чат задачи (T-152)
        Trello = new TrelloImporter(Tasks, Imports, deps.Secrets, Events, Files, Chat)
        {
            KeyResolver = ModelKeys.ResolveWithSource,
        };
        // GitLab (T-249): тот же набор зависимостей — чат нужен для переноса обсуждения
        // issue в чат задачи, файлы — для приложенных к статье файлов
        GitLab = new GitLabImporter(Tasks, Imports, deps.Secrets, Events, Files, Chat)
        {
            KeyResolver = ModelKeys.ResolveWithSource,
        };
        // GitHub (T-247): вложений отдельным списком у задачи GitHub нет, поэтому
        // хранилище файлов импортёру не нужно вовсе — только чат для обсуждения
        GitHub = new GitHubImporter(Tasks, Imports, deps.Secrets, Events, Chat)
        {
            KeyResolver = ModelKeys.ResolveWithSource,
        };
        // получение внешних файлов задания (T-255): ключи внешних систем берутся у активных
        // источников импорта, поэтому сервис строится после них — и после коннекторов,
        // которым он подставляется свойством
        Fetch = new FileFetchService(Files, Imports, Projects, deps.Secrets)
        {
            KeyResolver = ModelKeys.ResolveWithSource,
        };
        anthropic.Fetch = Fetch;
        text.Fetch = Fetch;
        // CLI-агенту он нужен не меньше: инструментов AI2P у него нет, зато есть маркер
        // AI2P_GET_FILE — и своя песочница, куда каталог кэша передаётся флагом --add-dir
        claudeCli.Fetch = Fetch;
        // импортёр строится после коннекторов — подставляется свойством (ТЗ v1.50, todo50)
        anthropic.Trello = Trello;
        text.Trello = Trello;
        anthropic.GitLab = GitLab;
        text.GitLab = GitLab;
        anthropic.GitHub = GitHub;
        text.GitHub = GitHub;
        // МЕДИАТЕКА ПРОЕКТА (T-113-S0): инструменты media_add / media_list / media_remove
        // работают над объектами проекта — своего хранилища у медиатеки нет. Нужны ВСЕМ
        // ТРЁМ текстовым коннекторам: монтажный лист собирает тот же агент, что пишет сцены
        anthropic.Objects = Objects;
        text.Objects = Objects;
        claudeCli.Objects = Objects;
        Schedules = new ScheduleService(Db, Events);
        // расписание срабатывает на СВОЁМ сервере (ТЗ гл. 6, этап 42): у расписания есть
        // сервер, а расписания без сервера ведёт дирижёр — отбор делает ScheduleService.Due
        ScheduleRunner = new ScheduleRunner(Tasks, Schedules, ExecutorPick, Events);

        // УВЕДОМЛЕНИЯ ПОЛЬЗОВАТЕЛЯ (T-272). Правила принадлежат организации и реплицируются,
        // а транспорт (почтовый сервер) — этому компьютеру: отсюда и деление на сервис правил
        // в хранилище и отправку в коннекторах. Письмо шлёт ВЛАДЕЛЕЦ ЗАДАЧИ, иначе одно
        // событие дало бы столько писем, сколько в кластере серверов
        NotificationRules = new NotificationRuleService(Db, Events, Scope);
        Notifications = new NotificationService(Tasks, Executors, Projects, TaskStatuses,
            NotificationRules, Events, () => Org, () => PublicBaseUrl,
            new EmailTransport(deps.Mail ?? EmptyMail, deps.Secrets));
        NotificationRunner = new NotificationRunner(Notifications);

        // АРХИВАЦИЯ (T-40-S0, выпуск 1.105). Реестр архивов принадлежит организации и
        // реплицируется, а открыт архив здесь или закрыт — дело этого компьютера: отсюда
        // и ServerScope, из которого сервис узнаёт, дирижёр ли он (архив создаётся только
        // на дирижёре) и под каким ключом сервера писать своё локальное состояние
        Archives = new ArchiveService(Db, Events, Scope);
        // ПРАВИЛА АРХИВАЦИИ (T-41-S0): общие правила организации (справочник настроек) и
        // правила каждого архива — одна таблица, один сервис. Ядру архивации они нужны
        // ровно в одном месте — при создании архива, поэтому связь делегатом-свойством,
        // а не параметром конструктора: без правил ядро остаётся работоспособным
        ArchiveRules = new ArchiveRuleService(Db, Events, Scope);
        Archives.Rules = ArchiveRules;
        ArchiveCandidates = new ArchiveCandidateService(Db);
        // ПЛАГИНЫ (T-111-S0): записи принадлежат организации и реплицируются, а установка
        // софта — дело этого компьютера, и её состояние лежит в config.json. Отсюда и
        // ServerScope: из него сервис берёт код сервера для номера записи (PLG-N-S1)
        Plugins = new PluginService(Db, Events, Scope);
        // жизненный цикл плагина (T-112-S0): экран «Настройки → Плагины и MCP» (T-114-S0)
        // зовёт именно его — сам по себе PluginService состояний не меняет и действий не заводит
        PluginSetup = new PluginSetupService(Db, Experience, RefData, Plugins);
        // НАБОРЫ ОПЫТА (T-270-S0): тот же механизм, что у записей опыта плагина, отданный
        // человеку отдельно — закладка «Настройки → Наборы опыта»
        ExperiencePacks = new ExperiencePackService(Files, Experience, RefData, Tasks);
        // ЗАПИСЬ ПЛАГИНА-ТРЕНЕРА ПО ИТОГАМ УСТАНОВКИ МОДЕЛИ (T-156-S0): пакеты обучения
        // (lora.train.packages) ставит установка модели, и в этот же момент в списке «Плагины
        // и MCP» должна появиться запись программы, которая ими работает. Связь делегатом:
        // установщик заводится раньше сервиса плагинов и о базе организации не знает
        TrainerPlugins = new TrainerPluginService(ModelInstalls, Files, Plugins)
        {
            SavePath = deps.SavePluginPath,
            Language = deps.Language,
        };
        ModelInstalls.AfterModelInstalled = manifest => TrainerPlugins.DeclareForModel(manifest);
        // ОБУЧЕНИЕ LoRA — ЗАДАЧА (T-157-S0). Кнопка «Обучить» после проверки датасета ищет
        // подходящий узел шаблонов, предлагает завести исполнителя «авто ПО» из записи
        // «Плагинов и MCP» и создаёт задачу; заводится ПОСЛЕ оркестратора и сервиса плагинов —
        // ему нужны оба
        LoraTrainTasks = new LoraTrainTaskService(Objects, Models, Tasks, Executors, Plugins, Files)
        {
            Language = deps.Language,
            Gateways = LiveGateways,
            StartTask = (taskId, actorId) => Orchestrator.StartTaskAsync(taskId, actorId),
            CheckTrain = (loraId, datasetId) => LoraTrain.Check(loraId, datasetId),
        };
        // а это обратная сторона той же связи: коннектор «авто ПО», получив задание, узнаёт по
        // ЗАДАЧЕ, что она обучает адаптер (обратный поиск по строке обучения), и ведёт все пять
        // шагов профайла модели сам — своих полей с параметрами у задачи для этого не нужно
        software.LoraOfTask = taskId => Objects.LoraModelByTrainTask(taskId)?.Id;
        software.RunLoraAsync = (loraId, jobId, ct) => LoraTrain.RunForTaskAsync(loraId, jobId, ct);
        // номер задачи в строке обучения: состояние «обучается» в списке моделей объекта —
        // ссылка в карточку задачи, где консоль, лог и кнопка остановки
        Objects.TaskResolver = taskId => Tasks.Get(taskId)?.DisplayId;
        ArchiveTransfer = new ArchiveTransferService(Db, Archives, Events);
        // АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ (T-46-S0): своего отбора и своего переноса у неё нет —
        // она связывает правила с движком и решает, где этому происходить (только дирижёр).
        // Расписанию она подставляется свойством: без неё расписание задач работает как
        // работало, а действие честно отказывается одной строкой в журнале
        AutoArchive = new AutoArchiveService(Archives, ArchiveRules, ArchiveCandidates,
            ArchiveTransfer, Events, Scope);
        ScheduleRunner.AutoArchive = AutoArchive;
        // автообновление приложения (T-208): расписание умеет проверять и ставить новую
        // версию — работу делает сервер, расписание только назначает ей время
        ScheduleRunner.AppUpdate = deps.AppUpdate;
        // почта «из логина пользователя» (T-272): аккаунты живут в СЕРВЕРНОЙ базе, поэтому
        // связь делегатом — ровно так же, как участие аккаунта в организации
        Executors.AccountLookup = deps.Account;
        // ШЛЮЗЫ В ВИДЕОРЕДАКТОРЫ (T-115-S0): агенту публикуются действия ТОЛЬКО живых записей
        // и ТОЛЬКО там, где софт найден на ЭТОМ компьютере — установка плагина всегда локальна
        // (T-110-S0 §1.3.4). Поставщик, а не готовый список: и записи, и найденный софт
        // меняются, пока приложение работает, а коннектор один на все задания сразу
        anthropic.Gateways = LiveGateways;
        text.Gateways = LiveGateways;
        claudeCli.Gateways = LiveGateways;
        // ПОДКЛЮЧЕНИЯ MCP (T-119-S0): агенту от них нужен пока один список имён — под
        // обратную политику безопасности «нет записи справочника → запрещено»
        anthropic.McpTools = LiveMcpTools;
        text.McpTools = LiveMcpTools;
        claudeCli.McpTools = LiveMcpTools;
        _pluginPath = deps.PluginPath;
        _pluginTools = deps.PluginTools;
    }

    private readonly Func<string, string>? _pluginPath;
    private readonly Func<string, IReadOnlyList<string>>? _pluginTools;

    /// <summary>
    /// ЖИВЫЕ ШЛЮЗЫ ЭТОГО СЕРВЕРА: манифест на диске + запись организации в состоянии, при
    /// котором действия публикуются (<see cref="PluginLifecycle.Publishes"/>) + найденный
    /// здесь путь к программе. Манифест без записи (плагин ещё не инициализировали) и запись
    /// без манифеста (плагин завели у соседа) не публикуют ничего — это не ошибка, а
    /// нормальное состояние распределённой установки.
    /// </summary>
    public IReadOnlyList<GatewayPlugin> LiveGateways()
    {
        var records = Plugins.List().ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        var live = new List<GatewayPlugin>();
        foreach (var manifest in AI2P.Core.PluginManifest.ReadAll(Files.DataDir))
        {
            if (manifest.Kind != PluginKinds.Gateway
                || !records.TryGetValue(manifest.Code, out var record))
            {
                continue;
            }
            var software = PluginSoftwareProbe.Find(manifest.Software,
                _pluginPath?.Invoke(manifest.Code) ?? "");
            if (!AI2P.Core.PluginLifecycle.Publishes(record.State, software.Status))
            {
                continue;
            }
            live.Add(new GatewayPlugin(manifest, software.Path, record.SettingsJson));
        }
        return live;
    }

    /// <summary>
    /// ИНСТРУМЕНТЫ СЕРВЕРОВ MCP ЭТОГО СЕРВЕРА (T-119-S0): «код плагина → имена из кэша
    /// config.json». Список снят с самого сервера MCP явным действием человека; запись
    /// организации при этом обязана существовать — манифест без записи ничего не подключает.
    /// Всё, что здесь перечислено, попадает под обратную политику безопасности.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LiveMcpTools()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (_pluginTools is null)
        {
            return map;
        }
        var records = Plugins.List().ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in AI2P.Core.PluginManifest.ReadAll(Files.DataDir))
        {
            if (manifest.Kind != PluginKinds.Mcp || !records.ContainsKey(manifest.Code))
            {
                continue;
            }
            var tools = _pluginTools(manifest.Code);
            if (tools.Count > 0)
            {
                map[manifest.Code] = tools;
            }
        }
        return map;
    }

    /// <summary>
    /// Первичное наполнение организации (ТЗ п. 2.15): справочники ролей, навыков, состояний,
    /// действий агентов и ИИ-моделей. До этапа 40 это делалось один раз для единственной БД —
    /// теперь для каждой организации отдельно, поэтому у неё свои коды и своя нумерация.
    /// Идемпотентно: вызывается при каждом открытии организации.
    /// </summary>
    public void SeedAndMigrate(string? sharedModelsImportDir)
    {
        // организация-РЕПЛИКА (ТЗ гл. 11, п. 11.2; этап 43): она заведена подключением
        // к чужому дирижёру, и её справочники со СЛУЧАЙНЫМИ идентификаторами — роли, навыки,
        // форматы, состояния — приедут репликацией. Засеять их своими значит получить на
        // каждом сервере свой комплект «встроенных» записей с другими идентификаторами
        // и вечные дубли. Справочники с ФИКСИРОВАННЫМИ идентификаторами (действия, модели)
        // сеет каждый сервер сам — ниже
        var isReplica = Db.Meta(ReplicaMetaKey) is { Length: > 0 };
        // ключ организации (ТЗ гл. 10, этап 45) заводится ВМЕСТЕ С ОРГАНИЗАЦИЕЙ, но только
        // у своей: реплика обязана получить ключ дирижёра, иначе зашифрует ключи API так,
        // что их не прочитает никто, включая её саму после переподключения
        if (!isReplica)
        {
            OrgKeys.Ensure(Org.Id);
        }
        // каталог хранилища проекта именуется его внешним кодом (todo37)
        Projects.MigrateSlugsToDisplayId();
        // каталог проекта переехал в пер-серверную строку (ТЗ гл. 6, этап 42): у проектов,
        // заведённых раньше, он лежал в настройках — переносим, иначе после обновления
        // каталоги всех проектов оказались бы пустыми
        Projects.MigrateFolderPathsToServerParts();
        // ЗАДАЧ БЕЗ СЕРВЕРА НЕ ОСТАЁТСЯ (T-1-S0): «без сервера» означает «у того, кто сегодня
        // дирижёр», и смена дирижёра уводила такие задачи на другой компьютер вместе с их
        // заданиями. Дирижёр записывает себя владельцем явно — закрепляет то, что и так
        // сегодня правит он. Сторож, а не разовый шаг обновления: бесхозные строки приезжают
        // и от партнёра прежней версии. На рядовом сервере не делает ничего
        var claimed = Tasks.ClaimTasksWithoutServer();
        if (claimed > 0)
        {
            Log.Information("AI2P: задач без сервера получили владельца: {Count} ({Org})",
                claimed, Org.Code);
        }
        // ТО ЖЕ САМОЕ У ОБЪЕКТОВ ПРОЕКТА (T-102-S0): до этой версии сервера у объекта не было
        // вовсе, и правил его дирижёр — то есть объект принадлежал РОЛИ, а не компьютеру
        var claimedObjects = Objects.ClaimObjectsWithoutServer();
        if (claimedObjects > 0)
        {
            Log.Information("AI2P: объектов без сервера получили владельца: {Count} ({Org})",
                claimedObjects, Org.Code);
        }
        // АРХИВ, У КОТОРОГО НА ДИСКЕ И КАТАЛОГ, И .zip (T-203-S0), — это незаконченное
        // закрытие: .zip приехал общей репликацией файлов организации (до этой версии каталог
        // архивов из неё не был исключён) либо каталог не удалился после упаковки. Чиним
        // сторожем при открытии организации, а не разовым шагом обновления: беспорядок уже
        // лежит на дисках, и разложить его мог кто угодно из партнёров (наука T-148-S0)
        var settled = Archives.SettleHalfClosed();
        if (settled > 0)
        {
            Log.Information("AI2P: архивов дочищено после незаконченного закрытия: {Count} ({Org})",
                settled, Org.Code);
        }
        // ВТОРОЙ ЗАМОК НА СИД СПРАВОЧНИКОВ (T-3-S1): справочники принадлежат организации,
        // а её ведёт ДИРИЖЁР — рядовому серверу засеивать их нечего в любом случае, была бы
        // пометка реплики или нет. Пометку можно потерять (перенос данных, старая установка,
        // гонка при подключении), а последствия несимметричны: у навыков и форматов
        // идентификаторы СЛУЧАЙНЫЕ, поэтому второй комплект встроенных записей делает
        // строки обеих сторон навсегда неприменимыми — UNIQUE(name) у них же
        if (!isReplica && IsConductor)
        {
            RefData.Seed();
            TaskStatuses.Seed();
        }
        // СПРАВОЧНИК ДЕЙСТВИЙ СЕЮТ ВСЕ СЕРВЕРЫ (T-280-S0) — в отличие от навыков, форматов
        // и состояний выше. Причина та же, по которой это делают общие правила работы
        // (T-30-S0) и справочник моделей ниже: у действий дистрибутива идентификаторы
        // ФИКСИРОВАННЫЕ (ac710000-…, они прописаны в i18n/ActionCatalogService_<язык>.json),
        // поэтому «свой комплект встроенных записей с другими идентификаторами и вечные
        // дубли» здесь невозможен — приехавшая репликацией строка и засеянная своя это
        // ОДНА строка. А замок «только дирижёр и только не реплика» оставлял рядовой сервер
        // и реплику вовсе без НОВЫХ действий версии: старые у них уже есть (приехали
        // репликацией), а новая запись появлялась только после обновления дирижёра — со
        // стороны это выглядит как «одно действие потерялось» (жалоба T-280-S0 про
        // AI2P.Experience.Search). Сид ничего чужого не портит: новую строку он вставляет
        // только если её НЕТ у него, тексты переписывает лишь при росте seedVersion файла,
        // кастомную правку промпта (prompt_custom) и кастомные действия не трогает,
        // а снятое встроенное действие не воскрешает — строка остаётся с deleted_at
        Actions.Seed();
        // ОБЩИЕ ПРАВИЛА РАБОТЫ (T-11-S0): правила, годные для любого проекта и любой
        // организации, — записи опыта без проекта и без узла шаблона. Новая организация
        // получает их при заведении, уже заведённая — при первом старте с этой версии;
        // дальше они правятся и удаляются человеком, и обратно сид их не возвращает
        // (отметка в meta).
        //
        // СЕЮТ ВСЕ СЕРВЕРЫ, а не только дирижёр (T-30-S0) — по той же причине, по которой
        // это делает справочник моделей ниже: у правил дистрибутива ФИКСИРОВАННЫЕ
        // идентификаторы, поэтому второй комплект тут невозможен, а условие «только дирижёр»
        // оставляло установку вовсе без поставляемых правил. Дирижёр меняется (T-21-S1),
        // и после смены не сеет никто: прежний уже не дирижёр, а новый подключался заявкой
        // и до конца дней помечен репликой (replica_of ставится один раз и не снимается)
        var rules = Experience.SeedGeneral();
        if (rules > 0)
        {
            Log.Information("AI2P: общих правил работы засеяно: {Count} ({Org})", rules, Org.Code);
        }
        // ЛЕКСИЧЕСКИЙ ИНДЕКС ПО ОПЫТУ (T-268-S0) — догоняющий проход по тем записям, которых
        // в индексе нет. Сторож, а не разовый шаг обновления: записи приезжают РЕПЛИКАЦИЕЙ
        // прямо в таблицу, мимо сервиса, а сам индекс не реплицируется вовсе (таблица
        // производная). FTS5 в сборке нет — проход не делает ничего, поиск работает
        // подстрочным сравнением
        var indexed = Experience.CatchUpSearchIndex();
        if (indexed > 0)
        {
            Log.Information("AI2P: записей опыта заиндексировано: {Count} ({Org})", indexed, Org.Code);
        }
        // ШАБЛОНОВ ЗАДАЧ В ДИСТРИБУТИВЕ НЕТ (T-307). Шаблон «Актуализация моделей» был
        // засеян сюда задачей T-142-S0, но заказчик решил иначе: он нужен ровно одному
        // проекту (самому AI2P) и заведён в нём вручную, а остальным организациям не нужен.
        // Поэтому и механизма сида шаблонов больше нет — TemplateSeed убран целиком
        // СПРАВОЧНИК МОДЕЛЕЙ СЕЕТСЯ И НА РЕПЛИКЕ (T-227) — в отличие от справочников выше.
        // Причина в идентификаторах: у записей дистрибутива они ФИКСИРОВАННЫЕ, поэтому
        // «свой комплект встроенных записей с другими идентификаторами и вечные дубли»
        // здесь невозможны. А с тех пор как эти записи перестали ехать журналом изменений
        // (их создаёт себе каждая установка сама), пропуск сида на реплике оставил бы её
        // вовсе без справочника моделей
        Models.Seed();
        // МАНИФЕСТЫ ПЛАГИНОВ ДИСТРИБУТИВА (T-115-S0) — файлы plugins/<код>/plugin.json, как
        // справочник пакетов. Их кладёт КАЖДЫЙ сервер, включая реплику: манифест это описание
        // возможностей на ЭТОМ компьютере, оно не реплицируется и сиротой быть не может —
        // записи в базе плагин не заводит, их заводит человек кнопкой «Инициализировать»
        PluginSeed.Write(Files);
        // НАБОРЫ ОПЫТА ДИСТРИБУТИВА (T-270-S0) — файлы packs/<код>/pack.json, по тому же
        // правилу, что манифесты плагинов: файл кладёт каждый сервер, а ЗАПИСИ в опыт из него
        // кладёт человек кнопкой «Установить», выбрав область (решение заказчика) — иначе
        // поставляемый опыт начал бы приходить всем задачам молча
        ExperiencePackSeed.Write(Files);
        // старые коды навыков в декларациях возможностей → новые (ТЗ v1.39, todo35_2)
        RefDataService.MigrateSkillCodes(Files);
        // модели, подготовленные инсталляторами (install_local_model)
        Models.ImportPending(sharedModelsImportDir);
        // модель, чьих файлов нет на месте / облачная без ключа API, не может быть активной.
        // У ЛОКАЛЬНОЙ модели это считается ПО ЭТОМУ СЕРВЕРУ (T-8-S1): файлы у каждого свои,
        // и там, где модель не установлена, она гаснет сама — а раньше не гасла вовсе, потому
        // что запись принадлежала другому серверу и правке здесь не поддавалась
        ModelInstalls.EnforceActivationOnStartup();
        Models.SyncLocalFlags();
        // модель, СТАВШАЯ локальной (профайл правили мимо формы), получает свою пер-серверную
        // строку здесь: сама по себе она заводится при создании, правке и установке модели
        Models.MigrateActivationToServerParts();
        // «все введённые ключи — ключи организации» (ТЗ гл. 10, этап 45): значения из
        // secrets.json переезжают в БД организации зашифрованными. Идемпотентно: уже
        // заведённый в организации ключ не трогается
        ModelKeys.MigrateSecretsToOrg();
        ModelKeys.EnforceActivationOnStartup();
        // то же для ключа и токена источников импорта (ТЗ v1.65, T-123): вписанное когда-то
        // руками в secrets.json переезжает в организацию, и форма источника показывает
        // «значение установлено» без повторного ввода
        ImportKeys.MigrateSecretsToOrg();
    }

    /// <summary>«Почта не настроена» (T-272): умолчание, когда настроек почтового сервера
    /// контексту не передали — так его поднимают проверки. Уведомления при этом собираются,
    /// но не отправляются, и причина видна в журнале работ.</summary>
    private static MailOptions EmptyMail() =>
        new("", 0, false, "", MailOptions.DefaultPasswordRef, "", "");

    /// <summary>Ключ в <c>meta</c> базы организации: организация заведена подключением
    /// к чужому дирижёру (значение — внутренний ключ дирижёра). Справочники такой
    /// организации не засеиваются — они приезжают репликацией (ТЗ гл. 11, п. 11.2).</summary>
    public const string ReplicaMetaKey = "replica_of";

    /// <summary>Организация — реплика чужого дирижёра (заведена подключением, этап 43).</summary>
    public bool IsReplica => Db.Meta(ReplicaMetaKey) is { Length: > 0 };

    /// <summary>Освежить запись организации в контексте после правки (название, код, активность).</summary>
    public void Refresh(Organization org) => Org = org;

    public void Dispose()
    {
        ScheduleRunner.Dispose();
        NotificationRunner.Dispose(); // сторож сроков уведомлений (T-272)
        Orchestrator.Dispose(); // сторож зависших заданий (T-117)
    }
}

/// <summary>
/// Зависимости уровня СЕРВЕРА, общие для всех организаций (ТЗ п. 6.4.1, этап 40):
/// секреты (файл рядом с config.json, не реплицируется), процессы локальных моделей
/// (они физически на этом компьютере), настройки и каталоги из config.json.
/// </summary>
public sealed record OrgDeps(
    string DbFile,
    string I18nDir,
    SecretStore Secrets,
    LocalModelProcessService LocalModels,
    Func<string> Language,
    Func<string> PublicBaseUrl,
    Func<string> ModelsRepo,
    Func<string> DistDir,
    Func<string> PackagesDir,
    /// <summary>Локальный сервер — дирижёр этой организации (ТЗ гл. 6, этап 41): он ведёт
    /// расписания и задачи, у которых сервер не указан.</summary>
    Func<string, bool> IsConductor,
    /// <summary>Внутренний ключ (unid) ЛОКАЛЬНОГО сервера — им он владеет строками (этап 42).</summary>
    Func<string> LocalServerId,
    /// <summary>Код локального сервера в организации: S0, S1, … (суффикс номеров, этап 42).</summary>
    Func<string, string> LocalCode,
    /// <summary>Код произвольного сервера в организации по его unid (для показа владельца).</summary>
    Func<string, string, string> ServerCode,
    /// <summary>Имя произвольного сервера по его unid (для подсказок и текстов ошибок).</summary>
    Func<string, string> ServerName,
    /// <summary>Справка об аккаунте по его ключу (T-272): почта и жив ли он. Аккаунты лежат
    /// в СЕРВЕРНОЙ базе, организация про них не знает — отсюда делегат. Нужна уведомлениям:
    /// «почту брать из логина» означает «взять адрес аккаунта входа».
    /// <para>null — справки нет (так контекст поднимают проверки): почта «из логина» тогда
    /// просто не находится, и уведомление этому исполнителю не уходит.</para></summary>
    Func<string, MemberAccount?>? Account = null,
    /// <summary>Настройки почтового сервера этого компьютера (T-272): делегатом, потому что
    /// их правят «на лету» из формы настроек — как язык и каталоги. null — почта не
    /// настроена (уведомления собираются, но не отправляются).</summary>
    Func<MailOptions>? Mail = null,
    /// <summary>РУЧНОЙ ПУТЬ К ПРОГРАММЕ ПЛАГИНА по его коду (T-115-S0): «установлен ли он
    /// здесь и где лежит софт» — пер-серверная величина из config.json, она не реплицируется
    /// (T-110-S0 §1.3.4), а организация о config.json не знает — отсюда делегат.
    /// <para>null — ручных путей нет (так контекст поднимают проверки): софт тогда ищется
    /// только в PATH, а не найденный означает «действия рендера не публикуются».</para></summary>
    Func<string, string>? PluginPath = null,
    // кэш имён инструментов сервера MCP (T-119-S0): тоже пер-серверный, лежит в config.json
    Func<string, IReadOnlyList<string>>? PluginTools = null,
    /// <summary>ЗАПОМНИТЬ ПУТЬ К ПРОГРАММЕ ПЛАГИНА (T-156-S0): код плагина, имя записи софта
    /// (пусто — ГЛАВНАЯ программа) и найденный путь. Нужен установке модели: она ставит пакеты
    /// обучения, а поиск программы плагина в каталог пакетов не заглядывает — только PATH и
    /// ручной путь. Пишется ТОЛЬКО в пустое поле: указанное человеком значение главнее.
    /// <para>null — путей не пишем (так контекст поднимают проверки): запись плагина заведётся,
    /// а программу человек покажет «Обзором».</para></summary>
    Action<string, string, string>? SavePluginPath = null,
    /// <summary>ВЫКЛЮЧЕННЫЕ ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ МОДЕЛИ (T-190-S0) по её
    /// идентификатору: пер-серверная величина из config.json (LoRA обучают на одной машине
    /// кластера). null — выбора не хранит никто, ставится всё.</summary>
    Func<string, IReadOnlyList<string>>? ModelOptionsOff = null,
    /// <summary>Запомнить выключенные опции установки модели (T-190-S0).</summary>
    Action<string, IReadOnlyList<string>>? SaveModelOptionsOff = null,
    /// <summary>АВТООБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208): «проверить» (false) и «проверить
    /// и поставить» (true), в ответе — строка для журнала. Обновляется установка целиком,
    /// то есть уровень СЕРВЕРА, — организация о нём не знает, отсюда делегат.
    /// <para>null — расписание-действие обновления честно отказывается строкой в журнале
    /// (так контекст поднимают проверки).</para></summary>
    Func<bool, string>? AppUpdate = null);
