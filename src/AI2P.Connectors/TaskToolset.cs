using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// Инструменты поиска и чтения заданий для ИИ-агента (todo23): родительское задание,
/// поиск по заголовку внутри проекта, по внешнему коду (display_id), по URL, чат задания.
/// Возвращают формулировку, критерии приёмки, статус, результаты (артефакты) и переписку —
/// агент может, например, проверить правильность выполнения родительского задания.
/// Доступ ограничен задачами того же проекта, что и текущая задача (ТЗ гл. 12).
/// Публикуются всегда (независимо от папки проекта), как и ask_question.
/// </summary>
public sealed class TaskToolset
{
    private const int MaxTextChars = 30_000;
    private const int MaxFoundTasks = 30;
    private const int MaxChatMessages = 100;

    /// <summary>Предел блока родительского задания в промпте (T-120): его агент не заказывал,
    /// поэтому платить за него всем контекстом нельзя.</summary>
    private const int MaxBlockChars = 40_000;

    /// <summary>Предел результата ОДНОГО соседнего задания в ответе инструмента
    /// get_sibling_tasks (T-132): соседей может быть много, и один разговорчивый сосед
    /// не должен съесть весь ответ.</summary>
    private const int MaxSiblingResultChars = 8_000;

    /// <summary>То же для блока соседей в промпте CLI-агента (T-132): блок едет рядом
    /// с родительским, поэтому и на соседа, и на весь блок предел вдвое строже.</summary>
    private const int MaxSiblingBlockResultChars = 4_000;

    private const int MaxSiblingsBlockChars = 20_000;

    private readonly TaskService _tasks;
    private readonly ChatService _chat;
    private readonly ExecutorService _executors;
    private readonly FileStore _files;
    private readonly TaskItem _current;
    private readonly RefDataService? _refData;
    private readonly ExecutorPickService? _picker;
    private readonly string? _actorExecutorId;
    private readonly ExperienceService? _experience;
    private readonly TrelloImporter? _importer;

    /// <summary>Импорт из GitLab (T-249): вторая процедура импорта по URL — какая возьмётся
    /// за ссылку, решает её вид, а не настройка.</summary>
    private readonly GitLabImporter? _gitlab;

    /// <summary>Импорт из GitHub (T-247) — третья процедура импорта по URL; выбор тот же,
    /// по виду ссылки.</summary>
    private readonly GitHubImporter? _github;
    private readonly Project? _project;
    private readonly string? _publicBaseUrl;

    /// <summary>Получение внешних файлов задания (T-255): картинка карточки — либо ссылка
    /// в интернет, либо файл хранилища; ни то, ни другое агенту недоступно само по себе.
    /// null — инструмент fetch_file не публикуется.</summary>
    private readonly FileFetchService? _fetch;

    /// <summary>Лог вызовов — для журнала работ (agent.tool_calls), как у FileToolset.</summary>
    public List<string> CallLog { get; } = [];

    /// <summary>Язык, на котором с агентом разговаривают (T-190): язык команды задачи
    /// (<c>Team.AgentLanguage</c>), а не язык установки — ответы инструментов читает агент.
    /// null — язык установки (у задачи нет команды, тесты). Ставится вызывающим через
    /// <see cref="AgentToolset.Language"/>.</summary>
    public string? Language { get; set; }

    /// <param name="refData">Справочник skills — для create_task (ТЗ v1.26); null — create_task недоступен.</param>
    /// <param name="picker">Автоподбор исполнителя подзадачи «сначала ИИ, потом человек» (ТЗ v1.26).</param>
    /// <param name="actorExecutorId">Исполнитель-агент — актор создаваемых подзадач и записей опыта.</param>
    /// <param name="experience">Опыт по шаблонам (ТЗ п. 2.11, todo32); null — инструменты опыта недоступны.</param>
    /// <param name="importer">Импорт из Trello (ТЗ v1.50, todo50); null — import_task_from_url недоступен.</param>
    /// <param name="project">Проект текущей задачи — каталог uploads/ для файлов импорта.</param>
    /// <param name="publicBaseUrl">Внешний адрес организации (…/ai2p/&lt;код&gt;) — из него
    /// строятся ссылки на задачи (get_sibling_tasks, T-132); null — ссылки не выдаются.</param>
    /// <param name="fetch">Получение внешних файлов задания (T-255); null — fetch_file недоступен.</param>
    public TaskToolset(TaskService tasks, ChatService chat, ExecutorService executors,
        FileStore files, TaskItem current, RefDataService? refData = null,
        ExecutorPickService? picker = null, string? actorExecutorId = null,
        ExperienceService? experience = null, TrelloImporter? importer = null,
        Project? project = null, string? publicBaseUrl = null, GitLabImporter? gitlab = null,
        GitHubImporter? github = null, FileFetchService? fetch = null)
    {
        _fetch = fetch;
        _gitlab = gitlab;
        _github = github;
        _tasks = tasks;
        _chat = chat;
        _executors = executors;
        _files = files;
        _current = current;
        _refData = refData;
        _picker = picker;
        _actorExecutorId = actorExecutorId;
        _experience = experience;
        _importer = importer;
        _project = project;
        _publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/');
    }

    /// <summary>Инструмент create_task публикуется агенту (ТЗ v1.26): задача ещё не разбита
    /// (isNotSplit снят) и заданы зависимости создания. Снимок на момент старта задания:
    /// первый create_task ставит isNotSplit родителю, но не блокирует остальные вызовы
    /// этого же задания — агент создаёт несколько подзадач за один запуск.</summary>
    public bool CanCreate => !_current.IsNotSplit && _refData is not null && _picker is not null;

    /// <summary>
    /// Инструменты опыта публикуются (ТЗ п. 2.11), когда опыт есть КУДА писать: задача создана
    /// из шаблона (есть template_id — опыт узла шаблона, todo32) ЛИБО задача принадлежит проекту
    /// (опыт проекта, todo48). До T-144 второго условия не было: агент видел блок «Опыт проекта»
    /// в задании, но пополнить его не мог ничем — ни инструментом, ни маркером, и на прямую
    /// просьбу человека «заполни опыт проекта» отвечал отказом.
    /// </summary>
    public bool CanExperience =>
        _experience is not null && (_current.TemplateId is not null || _current.ProjectId is not null);

    /// <summary>
    /// ПОИСК по опыту (search_experience, T-268-S0) публикуется, как только опыт вообще
    /// подключён: условие «есть куда писать» здесь не годится. Общие правила работы
    /// организации получает КАЖДАЯ задача, в том числе заведённая вне проекта и вне
    /// шаблона, — а значит ей есть что искать, даже если писать ей некуда.
    /// </summary>
    public bool CanSearchExperience => _experience is not null;

    /// <summary>Инструменты шаблонов (T-144) публикуются задачам проекта: узлы шаблонов
    /// перечисляются, создаются и правятся в пределах проекта текущей задачи. Сама запись
    /// шаблона возможна только на дирижёре организации (ТЗ гл. 6) — отказ приходит от
    /// хранилища понятным текстом.</summary>
    public bool CanTemplates => _current.ProjectId is not null;

    /// <summary>Импорт задания по URL публикуется (ТЗ v1.50, todo50), когда задан хотя бы
    /// один импортёр и проект текущей задачи (файлы вложений сохраняются в uploads/ проекта).
    /// Какой именно импортёр возьмётся за ссылку, решает её вид (T-249).</summary>
    public bool CanImport =>
        (_importer is not null || _gitlab is not null || _github is not null) && _project is not null;

    /// <summary>
    /// Получение внешнего файла задания (fetch_file, T-255) публикуется, когда сервис задан.
    /// Проекта и папки проекта инструменту не нужно вовсе: файл кладётся в кэш агента внутри
    /// каталога данных — то есть работает и у задачи без проекта.
    /// </summary>
    public bool CanFetch => _fetch is not null;

    /// <summary>Каталог кэша полученных файлов (T-255): CLI-агенту он передаётся флагом
    /// <c>--add-dir</c>, иначе прочитать положенный туда файл ему не даст песочница.
    /// null — инструмент недоступен.</summary>
    public string? FetchDir => _fetch?.CacheDir;

    /// <summary>Письмо в чат задачи (send_chat_message, T-161) публикуется, когда известно,
    /// от чьего имени писать — исполнитель задания. Нужно, чтобы агент мог ОТВЕТИТЬ человеку,
    /// написавшему в чат во время его работы (перебивка, <see cref="ChatWatch"/>).</summary>
    public bool CanChat => _actorExecutorId is not null;

    /// <summary>Смена состояния ЧУЖОЙ задачи (set_task_status, T-34-S0) публикуется, когда
    /// известно, от чьего имени её менять: смена статуса пишется в журнал работ задачи
    /// актором, и безымянной она быть не должна.</summary>
    public bool CanSetStatus => _actorExecutorId is not null;

    /// <summary>Список подзадач (get_child_tasks, T-34-S0) доступен всегда: своё поддерево
    /// у задачи есть или его нет, и второй случай — обычный ответ, а не отказ.</summary>
    public bool CanChildren => true;

    /// <summary>Правка полей чужой задачи (update_task, T-160-S0) публикуется по тому же
    /// условию, что и смена состояния: правка пишется в журнал работ задачи актором,
    /// и безымянной она быть не должна.</summary>
    public bool CanUpdate => _actorExecutorId is not null;

    /// <summary>Перезапуск чужой задачи для повторной проверки и уход в ожидание
    /// (restart_task_for_recheck, wait_for_recheck, T-31-S0) публикуются по тому же условию,
    /// что и смена состояния: перезапуск меняет состояние чужой задачи и пишет об этом
    /// в журнал работ, а безымянной такая запись быть не должна. Ожидание идёт следом
    /// за перезапуском и в одиночку смысла не имеет.</summary>
    public bool CanRecheck => _actorExecutorId is not null;

    /// <summary>Тип текущей задачи (T-298-S0): «Условие» — агенту публикуется
    /// set_condition_result, цикл — set_loop_result (T-300-S0). У линейной задачи решать
    /// нечего, и инструмент, отвечающий только отказом, агенту не показывается.</summary>
    public string FlowType => TaskFlow.Read(_current.LaunchJson).Type;

    /// <summary>Задачи ветви из узла шаблона (create_tasks_from_template, T-300-S0) — по тем
    /// же условиям, что create_task и шаблоны проекта: подбор исполнителя и проект.</summary>
    public bool CanFromTemplate => CanCreate && CanTemplates;

    /// <summary>Остановка выполнения иерархии (stop_hierarchy, T-300-S0) — по тому же
    /// условию, что смена состояния: меняет очередь чужой задачи-корня, и запись об этом
    /// безымянной быть не должна.</summary>
    public bool CanStopHierarchy => _actorExecutorId is not null;

    /// <summary>Опубликованные агенту инструменты (todo23). Текстов здесь НЕТ (todo24):
    /// промпты (описания) подставляются из справочника действий (Настройки → Действия);
    /// в коде — только имена и структурные схемы параметров.</summary>
    public static IReadOnlyList<FileToolset.ToolSpec> Specs { get; } =
    [
        new("get_parent_task", "",
            """{ "type": "object", "properties": {} }"""),
        new("get_sibling_tasks", "",
            """{ "type": "object", "properties": {} }"""),
        new("get_child_tasks", "",
            """
            {
              "type": "object",
              "properties": {
                "code":  { "type": "string" },
                "depth": { "type": "integer" }
              }
            }
            """),
        new("set_task_status", "",
            """
            {
              "type": "object",
              "properties": {
                "code":    { "type": "string" },
                "status":  { "type": "string" },
                "restart": { "type": "boolean" }
              },
              "required": ["code", "status"]
            }
            """),
        new("restart_task_for_recheck", "",
            """
            {
              "type": "object",
              "properties": {
                "code":    { "type": "string" },
                "comment": { "type": "string" }
              },
              "required": ["code"]
            }
            """),
        new("wait_for_recheck", "",
            """{ "type": "object", "properties": {} }"""),
        new("get_task_by_code", "",
            """{ "type": "object", "properties": { "code": { "type": "string" } }, "required": ["code"] }"""),
        new("get_task_by_url", "",
            """{ "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] }"""),
        new("find_tasks_by_title", "",
            """{ "type": "object", "properties": { "title": { "type": "string" } }, "required": ["title"] }"""),
        new("get_task_chat", "",
            """{ "type": "object", "properties": { "code": { "type": "string" } } }"""),
        new("create_task", "",
            """
            {
              "type": "object",
              "properties": {
                "title":       { "type": "string" },
                "description": { "type": "string" },
                "skills":      { "type": "array", "items": { "type": "string" } },
                "priority":    { "type": "integer" },
                "acceptance":  { "type": "string" },
                "startAfterMinutes": { "type": "integer" }
              },
              "required": ["title", "description"]
            }
            """),
        new("move_task", "",
            """
            {
              "type": "object",
              "properties": {
                "code":   { "type": "string" },
                "parent": { "type": "string" }
              },
              "required": ["code"]
            }
            """),
        new("update_task", "",
            """
            {
              "type": "object",
              "properties": {
                "code":         { "type": "string" },
                "executor":     { "type": "string" },
                "altExecutors": { "type": "array", "items": { "type": "string" } },
                "responsible":  { "type": "string" },
                "skills":       { "type": "array", "items": { "type": "string" } },
                "tags":         { "type": "array", "items": { "type": "string" } }
              },
              "required": ["code"]
            }
            """),
        new("create_experience", "",
            """
            {
              "type": "object",
              "properties": {
                "text":     { "type": "string" },
                "scope":    { "type": "string", "enum": ["project", "template", "general"] },
                "template": { "type": "string" },
                "skill":    { "type": "string" }
              },
              "required": ["text"]
            }
            """),
        new("update_experience", "",
            """
            {
              "type": "object",
              "properties": {
                "id":         { "type": "string" },
                "text":       { "type": "string" },
                "skill":      { "type": "string" },
                "tags":       { "type": "array", "items": { "type": "string" } },
                "alwaysLoad": { "type": "boolean" },
                "active":     { "type": "boolean" }
              },
              "required": ["id", "text"]
            }
            """),
        new("set_experience_active", "",
            """
            {
              "type": "object",
              "properties": {
                "id":     { "type": "string" },
                "active": { "type": "boolean" }
              },
              "required": ["id", "active"]
            }
            """),
        new("list_experience", "",
            """
            {
              "type": "object",
              "properties": {
                "scope":           { "type": "string", "enum": ["project", "template", "general"] },
                "template":        { "type": "string" },
                "skill":           { "type": "string" },
                "tag":             { "type": "string" },
                "includeInactive": { "type": "boolean" },
                "limit":           { "type": "integer" },
                "offset":          { "type": "integer" }
              }
            }
            """),
        new("experience_usage", "",
            """
            {
              "type": "object",
              "properties": {
                "id":     { "type": "string" },
                "scope":  { "type": "string", "enum": ["project", "template", "general"] },
                "limit":  { "type": "integer" },
                "offset": { "type": "integer" }
              }
            }
            """),
        new("move_experience", "",
            """
            {
              "type": "object",
              "properties": {
                "id":       { "type": "string" },
                "scope":    { "type": "string", "enum": ["project", "template", "general"] },
                "template": { "type": "string" }
              },
              "required": ["id", "scope"]
            }
            """),
        new("search_experience", "",
            """
            {
              "type": "object",
              "properties": {
                "query":           { "type": "string" },
                "scope":           { "type": "string", "enum": ["project", "template", "general", "all"] },
                "limit":           { "type": "integer" },
                "includeInactive": { "type": "boolean" }
              },
              "required": ["query"]
            }
            """),
        new("list_templates", "",
            """{ "type": "object", "properties": {} }"""),
        new("create_template", "",
            """
            {
              "type": "object",
              "properties": {
                "title":       { "type": "string" },
                "description": { "type": "string" },
                "acceptance":  { "type": "string" },
                "parent":      { "type": "string" },
                "skills":      { "type": "array", "items": { "type": "string" } }
              },
              "required": ["title", "description"]
            }
            """),
        new("update_template", "",
            """
            {
              "type": "object",
              "properties": {
                "code":        { "type": "string" },
                "title":       { "type": "string" },
                "description": { "type": "string" },
                "acceptance":  { "type": "string" }
              },
              "required": ["code"]
            }
            """),
        new("import_task_from_url", "",
            """{ "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] }"""),
        new("fetch_file", "",
            """{ "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] }"""),
        new("send_chat_message", "",
            """{ "type": "object", "properties": { "text": { "type": "string" } }, "required": ["text"] }"""),
        new("send_task_message", "",
            """
            {
              "type": "object",
              "properties": {
                "code": { "type": "string" },
                "text": { "type": "string" },
                "wait": { "type": "boolean" }
              },
              "required": ["code", "text"]
            }
            """),
        new("wait_task_reply", "",
            """{ "type": "object", "properties": { "minutes": { "type": "integer" } } }"""),
        // ветвление и циклы (T-300-S0): решение условия, исход проверки цикла, задачи ветви
        // из узла шаблона, остановка выполнения иерархии
        new("set_condition_result", "",
            """{ "type": "object", "properties": { "value": { "type": "boolean" } }, "required": ["value"] }"""),
        new("set_loop_result", "",
            """{ "type": "object", "properties": { "continue": { "type": "boolean" } }, "required": ["continue"] }"""),
        new("create_tasks_from_template", "",
            """
            {
              "type": "object",
              "properties": {
                "template": { "type": "string" },
                "parent":   { "type": "string" }
              },
              "required": ["template"]
            }
            """),
        new("stop_hierarchy", "",
            """{ "type": "object", "properties": { "reason": { "type": "string" } } }"""),
    ];

    /// <summary>Имена инструментов заданий — для диспетчеризации в AgentToolset.</summary>
    public static readonly HashSet<string> Names =
        Specs.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Выполнить вызов инструмента; результат (или текст ошибки) возвращается агенту.</summary>
    public async Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        try
        {
            var result = name switch
            {
                "get_parent_task" => GetParentTask(),
                "get_sibling_tasks" => GetSiblingTasks(),
                "get_child_tasks" => GetChildTasks(args),
                "set_task_status" => SetTaskStatus(args),
                "restart_task_for_recheck" => RestartTaskForRecheck(args),
                "wait_for_recheck" => WaitForRecheck(args),
                "get_task_by_code" => GetTaskByCode(RequiredString(args, "code", Language)),
                "get_task_by_url" => GetTaskByUrl(RequiredString(args, "url", Language)),
                "find_tasks_by_title" => FindTasksByTitle(RequiredString(args, "title", Language)),
                "get_task_chat" => GetTaskChat(OptionalString(args, "code")),
                "create_task" => CreateTask(args),
                "move_task" => MoveTask(args),
                "update_task" => UpdateTask(args),
                "create_experience" => CreateExperience(args),
                "update_experience" => UpdateExperience(args),
                "move_experience" => MoveExperience(args),
                "search_experience" => SearchExperience(args),
                "set_experience_active" => SetExperienceActive(args),
                "list_experience" => ListExperience(args),
                "experience_usage" => ExperienceUsage(args),
                "list_templates" => ListTemplates(),
                "create_template" => CreateTemplate(args),
                "update_template" => UpdateTemplate(args),
                "import_task_from_url" => await ImportTaskFromUrlAsync(RequiredString(args, "url", Language), ct),
                "fetch_file" => await FetchFileAsync(RequiredString(args, "url", Language), ct),
                "send_chat_message" => SendChatMessage(RequiredString(args, "text", Language)),
                "send_task_message" => SendTaskMessage(args),
                "wait_task_reply" => await WaitTaskReplyAsync(args, ct),
                "set_condition_result" => SetFlowDecision(args, "value", loop: false),
                "set_loop_result" => SetFlowDecision(args, "continue", loop: true),
                "create_tasks_from_template" => CreateTasksFromTemplate(args),
                "stop_hierarchy" => StopHierarchy(args),
                _ => Loc.In(Language, "prompt.tasks.1", name),
            };
            CallLog.Add($"{name}({args.GetRawText()}) → {Preview(result)}");
            return result;
        }
        catch (Exception ex)
        {
            var error = Loc.In(Language, "prompt.tasks.2", name, ex.Message);
            CallLog.Add(error);
            return error;
        }
    }

    /// <summary>
    /// Импорт одиночного задания по URL (ТЗ v1.50, todo50): карточка Trello либо issue
    /// GitLab (T-249) → задача-черновик текущего проекта со статьёй, приложенными файлами
    /// и обсуждением; повторный импорт той же карточки не создаёт дубль (external_ref).
    /// Процедуру выбирает ВИД ССЫЛКИ: агенту не нужно знать, какие источники заведены.
    /// </summary>
    private async Task<string> ImportTaskFromUrlAsync(string url, CancellationToken ct)
    {
        // СНАЧАЛА ИЩЕМ У СЕБЯ (T-196-S0): ссылка на НАШУ же задачу — это не импорт, а чтение.
        // Во внешнюю систему по ней лезть незачем — там такой карточки нет вовсе, и агент
        // получал бы отказ вместо задания. Поиск не смотрит ни на протокол, ни на корень сайта
        if (LocalTaskOf(url) is { } own)
        {
            return own.ProjectId == _current.ProjectId
                ? Render(own)
                : Loc.In(Language, "prompt.tasks.33");
        }
        if (!CanImport)
        {
            return Loc.In(Language, "prompt.tasks.3");
        }
        // содержимое приводится к общему виду: дальше создание задачи одинаковое
        string title, description, externalRef, importUrl;
        DateTime? due;
        List<string> files;
        Func<string, int> importChat;
        if (_github is not null && GitHubImporter.CanRefresh(url))
        {
            var issue = await _github.ImportIssueContentAsync(url, sourceId: null, ct);
            (title, description, due, externalRef, files, importUrl) =
                (issue.Title, issue.Description, issue.Due, issue.ExternalRef,
                    // вложений отдельным списком у GitHub нет: файлы живут ссылками
                    // внутри текста задачи и ссылками остаются
                    new List<string>(), issue.Url);
            importChat = taskId => _github.ImportChat(taskId, issue.Comments, _actorExecutorId);
        }
        else if (_gitlab is not null && GitLabImporter.CanRefresh(url))
        {
            var issue = await _gitlab.ImportIssueContentAsync(_project!, url, sourceId: null, ct);
            (title, description, due, externalRef, files, importUrl) =
                (issue.Title, issue.Description, issue.Due, issue.ExternalRef, issue.Files, issue.Url);
            importChat = taskId => _gitlab.ImportChat(taskId, issue.Notes, _actorExecutorId);
        }
        else
        {
            if (_importer is null)
            {
                return Loc.In(Language, "prompt.tasks.3");
            }
            var card = await _importer.ImportCardContentAsync(_project!, url, sourceId: null, ct);
            (title, description, due, externalRef, files, importUrl) =
                (card.Title, card.Description, card.Due, card.ExternalRef, card.Files, card.Url);
            importChat = taskId => _importer.ImportChat(taskId, card.Comments, _actorExecutorId);
        }

        var existing = _tasks.FindByExternalRef(_project!.Id, externalRef);
        if (existing is not null)
        {
            return Loc.In(Language, "prompt.tasks.4", existing.DisplayId, existing.Title);
        }
        var task = new TaskItem
        {
            ProjectId = _project.Id,
            TeamId = _current.TeamId,
            Title = title,
            Status = TaskStatuses.Draft,
            DueDate = due,
            ExternalRef = externalRef,
            // ссылка импорта (T-246): по ней задачу потом обновляют из источника кнопкой.
            // Берётся канонический адрес от самого источника, а не то, что дал агент
            ImportUrl = AI2P.Storage.Services.TaskService.NormalizeImportUrl(
                importUrl.Trim().Length > 0 ? importUrl : url),
        };
        var created = _tasks.Create(task, description, "", _actorExecutorId);
        // обсуждение — в чат созданной задачи (T-152): авторы названы так же, как в источнике
        // («trello:<логин>», «gitlab:<логин>»), порядок и время сообщений сохраняются
        var comments = importChat(created.Id);
        var filesNote = files.Count == 0
            ? Loc.In(Language, "prompt.tasks.5")
            : Loc.In(Language, "prompt.tasks.6", string.Join(", ", files));
        var chatNote = comments == 0 ? "" : Loc.In(Language, "prompt.tasks.7", comments);
        return Loc.In(Language, "prompt.tasks.8", created.DisplayId, created.Title, filesNote, chatNote);
    }

    /// <summary>
    /// ПОЛУЧИТЬ ФАЙЛ ПО ССЫЛКЕ ЗАДАНИЯ (fetch_file, T-255): картинка, приложенная к задаче
    /// импортом, лежит либо ссылкой в интернете (за авторизацией внешней системы), либо
    /// файлом хранилища AI2P — и то, и другое агенту недоступно: файловые инструменты
    /// ограничены каталогом проекта, а CLI-агент живёт в своей песочнице. Инструмент кладёт
    /// файл в кэш агента и возвращает путь на диске: дальше картинка читается обычными
    /// файловыми средствами. Уже полученный файл берётся из кэша, сеть при этом не трогается.
    /// </summary>
    private async Task<string> FetchFileAsync(string url, CancellationToken ct)
    {
        if (_fetch is null)
        {
            return Loc.In(Language, "prompt.tasks.99");
        }
        var file = await _fetch.FetchAsync(url, Language, ct);
        var origin = file.Origin switch
        {
            FileFetchService.FetchOrigin.Cache => Loc.In(Language, "prompt.tasks.101"),
            FileFetchService.FetchOrigin.Storage => Loc.In(Language, "prompt.tasks.102"),
            _ => Loc.In(Language, "prompt.tasks.103"),
        };
        return Loc.In(Language, "prompt.tasks.100", file.Path, file.Name, file.Bytes,
            file.ContentType, origin);
    }

    /// <summary>
    /// То же для агента БЕЗ инструментов AI2P (CLI-агент, T-255): позвать fetch_file он
    /// не может, поэтому файл запрашивается МАРКЕРОМ <paramref name="marker"/> в ответе —
    /// система кладёт файл в кэш и присылает путь следующим сообщением ТОЙ ЖЕ сессии CLI
    /// (как ответ человека на вопрос), после чего агент читает картинку своими средствами.
    /// Пусто — получение файлов недоступно.
    /// </summary>
    /// <param name="maxFetches">Предел таких запросов за одно задание.</param>
    public string FetchMarkerNote(string marker, int maxFetches) =>
        !CanFetch
            ? ""
            : Loc.In(Language, "prompt.tasks.113") + marker +
              Loc.In(Language, "prompt.tasks.114", maxFetches, FetchDir);

    /// <summary>
    /// send_chat_message (T-161): агент пишет человеку в ЧАТ текущей задачи от своего имени.
    /// Главное назначение — ОТВЕТИТЬ на сообщение, пришедшее пока агент работал (перебивка
    /// чатом, <see cref="ChatWatch"/>): без этого инструмента ответить агенту было нечем —
    /// человек видел его слова только в итоговом результате, то есть уже после закрытия
    /// задания. Годится и для короткой весточки по ходу работы («иду дольше обычного»).
    /// Итоговый результат задания сюда писать не нужно — он и так уходит артефактом.
    /// </summary>
    private string SendChatMessage(string text)
    {
        if (!CanChat)
        {
            return Loc.In(Language, "prompt.tasks.9");
        }
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Loc.In(Language, "prompt.tasks.10");
        }
        _chat.Add(_current.Id, _actorExecutorId!, null, trimmed);
        return Loc.In(Language, "prompt.tasks.11", _current.DisplayId, trimmed.Length);
    }

    // --- разговор агентов между собой (T-185) ---

    /// <summary>Предел ожидания ответа собеседника инструментом wait_task_reply (T-185):
    /// дольше держать ход агента бессмысленно — ответ доедет перебивкой чата или следующим
    /// заданием, а таймаут ответа исполнителя (T-124) тратить нельзя.</summary>
    public const int MaxWaitReplyMinutes = 30;

    private const int DefaultWaitReplyMinutes = 10;

    /// <summary>Как часто заглядывать в чат, ожидая ответа собеседника (wait_task_reply).</summary>
    private static readonly TimeSpan WaitPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Разговор с агентами РОДСТВЕННЫХ задач (T-185) публикуется, когда известно, от чьего
    /// имени писать (исполнитель задания), и рядом есть с кем говорить: подзадачи того же
    /// родителя, сам родитель, свои прямые потомки. Одинокой задаче инструменты не нужны.
    /// </summary>
    public bool CanTalk => _actorExecutorId is not null && Relatives().Count > 0;

    /// <summary>
    /// Задача, которой агент ждёт ответа (T-185): проставляется вызовом send_task_message
    /// с wait=true. Коннектор CLI-агента по ней ставит работу на паузу тем же способом, что
    /// и на вопросе человеку (job → waiting_human, задача → «пауза»), а продолжает работу
    /// сторож оркестратора, когда собеседник ответит. null — ждать нечего.
    /// </summary>
    public PendingReply? Waiting { get; private set; }

    /// <summary>Кого и о чём агент ждёт (T-185): задача-собеседник, её исполнитель и вопрос.</summary>
    /// <param name="TaskCode">Код задачи-собеседника (для текста вопроса и журнала).</param>
    /// <param name="ExecutorId">Исполнитель задачи-собеседника — адресат вопроса в чате.</param>
    /// <param name="Question">Текст, который агент послал собеседнику.</param>
    public sealed record PendingReply(string TaskCode, string ExecutorId, string Question);

    /// <summary>
    /// Задачи, с агентами которых можно разговаривать (T-185): подзадачи ТОГО ЖЕ родителя,
    /// сам родитель и свои прямые потомки. Дальше родства переписка не идёт намеренно —
    /// иначе агент, которому не хватает данных, начал бы писать всему проекту; чужие задания
    /// читаются инструментами чтения (get_task_by_code), а не разговором.
    /// </summary>
    private List<TaskItem> Relatives()
    {
        var all = new List<TaskItem>();
        if (_current.ParentId is { } parentId && _tasks.Get(parentId) is { DeletedAt: null } parent)
        {
            all.Add(parent);
            all.AddRange(Siblings());
        }
        all.AddRange(_tasks.ListChildren(_current.Id).Where(c => c.DeletedAt is null && !c.IsTemplate));
        return all
            .Where(t => !string.Equals(t.Id, _current.Id, StringComparison.Ordinal))
            .DistinctBy(t => t.Id)
            .ToList();
    }

    /// <summary>Родственная задача по коду; null — такой рядом нет (чужая ветка, опечатка).</summary>
    private TaskItem? Relative(string code) =>
        Relatives().FirstOrDefault(t => t.DisplayId.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// send_task_message (T-185): написать агенту РОДСТВЕННОЙ задачи — соседней подзадаче
    /// того же родителя, родителю или своему потомку. Сообщение ложится в ЧАТ той задачи
    /// от имени этого исполнителя, и если над ней сейчас работает агент, оно доедет до него
    /// прямо в ходе работы (перебивка чатом, <see cref="ChatWatch"/>) — тем же путём, каким
    /// агента перебивает человек. Никого нет за работой — сообщение просто ждёт в чате:
    /// его увидит и человек, и агент, когда задачу запустят.
    ///
    /// wait=true — после отправки работа встаёт на паузу до ответа собеседника: в чате СВОЕЙ
    /// задачи заводится вопрос, адресованный его исполнителю, задание уходит в waiting_human,
    /// задача — в «паузу». Ответом считается сообщение собеседника в наш чат; не дождавшись
    /// его, ответить всегда может человек — вопрос висит у него на виду, как обычный.
    /// </summary>
    private string SendTaskMessage(JsonElement args)
    {
        if (!CanChat)
        {
            return Loc.In(Language, "prompt.talk.1");
        }
        var code = RequiredString(args, "code", Language).Trim();
        var text = RequiredString(args, "text", Language).Trim();
        if (text.Length == 0)
        {
            return Loc.In(Language, "prompt.tasks.10");
        }
        var target = Relative(code);
        if (target is null)
        {
            var known = Relatives();
            return known.Count == 0
                ? Loc.In(Language, "prompt.talk.2", code)
                : Loc.In(Language, "prompt.talk.3", code,
                    string.Join(", ", known.Select(t => t.DisplayId)));
        }
        // подпись обязательна: собеседник видит сообщение как перебивку чата своей задачи,
        // и без неё не понял бы ни от кого оно, ни куда отвечать
        var signed = Loc.In(Language, "prompt.talk.4", _current.DisplayId, _current.Title,
            Nick(_actorExecutorId!), _current.DisplayId) + "\n\n" + text;
        // адресат — тот, кто РЕАЛЬНО ведёт задачу сейчас (T-221): при замене занятого
        // исполнителя запасным ответа ждать надо от него, а не от назначенного
        var addressee = target.ActualExecutorId is { Length: > 0 } actual
            ? actual
            : target.ExecutorIds.Count == 1 ? target.ExecutorIds[0] : null;
        _chat.Add(target.Id, _actorExecutorId!, addressee, signed);

        var wants = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("wait", out var w)
                    && w.ValueKind is JsonValueKind.True;
        if (!wants)
        {
            return Loc.In(Language, "prompt.talk.5", target.DisplayId, text.Length);
        }
        if (addressee is null)
        {
            // ждать нечего: у задачи-собеседника нет назначенного исполнителя, отвечать некому
            return Loc.In(Language, "prompt.talk.6", target.DisplayId);
        }
        Waiting = new PendingReply(target.DisplayId, addressee, text);
        return Loc.In(Language, "prompt.talk.7", target.DisplayId);
    }

    /// <summary>
    /// wait_task_reply (T-185): дождаться ответа в чате СВОЕЙ задачи, не заканчивая ход —
    /// для агентов с инструментами AI2P (у CLI-агента для ожидания есть пауза задания:
    /// маркер с wait=true). Ждём появления сообщения от кого угодно, кроме себя, но не дольше
    /// <paramref name="args"/>.minutes (умолчание 10, предел <see cref="MaxWaitReplyMinutes"/>).
    /// Никто не ответил — так и говорим: работу нужно продолжать с тем, что есть.
    /// </summary>
    private async Task<string> WaitTaskReplyAsync(JsonElement args, CancellationToken ct)
    {
        if (!CanChat)
        {
            return Loc.In(Language, "prompt.talk.1");
        }
        var minutes = args.ValueKind == JsonValueKind.Object
                      && args.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var value)
            ? Math.Clamp(value, 1, MaxWaitReplyMinutes)
            : DefaultWaitReplyMinutes;
        var since = DateTime.UtcNow;
        var deadline = since + TimeSpan.FromMinutes(minutes);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(WaitPollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return Loc.In(Language, "prompt.talk.8");
            }
            var fresh = _chat.ListByTask(_current.Id)
                .Where(msg => msg.CreatedAt >= since
                              && msg.Kind != ChatMessageKind.Question
                              && !string.Equals(msg.FromExecutorId, _actorExecutorId, StringComparison.Ordinal))
                .ToList();
            if (fresh.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine(Loc.In(Language, "prompt.talk.9", fresh.Count));
                foreach (var msg in fresh)
                {
                    sb.AppendLine();
                    sb.AppendLine($"--- {Nick(msg.FromExecutorId)}:");
                    sb.AppendLine(ClipMessage(msg.Text));
                }
                return sb.ToString().TrimEnd();
            }
        }
        return Loc.In(Language, "prompt.talk.10", minutes);
    }

    /// <summary>
    /// Дописка к системному промпту про разговор с агентами родственных задач (T-185):
    /// кто рядом, чем им писать и когда это уместно. Пусто — говорить не с кем.
    /// </summary>
    /// <param name="marker">Маркер письма для агента БЕЗ инструментов AI2P (CLI-агент);
    /// null — у агента есть инструменты send_task_message / wait_task_reply.</param>
    public string TalkNote(string? marker = null)
    {
        var relatives = Relatives();
        if (!CanChat || relatives.Count == 0)
        {
            return "";
        }
        var who = string.Join(", ", relatives.Take(MaxFoundTasks).Select(t =>
            $"{t.DisplayId} ({t.Status}" +
            (t.ExecutorIds.Count == 1 ? $", {Nick(t.ExecutorIds[0])})" : ")")));
        return marker is null
            ? Loc.In(Language, "prompt.talk.11", who, MaxWaitReplyMinutes)
            : Loc.In(Language, "prompt.talk.12", who) + marker + Loc.In(Language, "prompt.talk.13");
    }

    /// <summary>
    /// Дописка к системному промпту про чат для агента БЕЗ инструментов AI2P (CLI-агент,
    /// T-161): позвать send_chat_message он не может, поэтому пишет в чат МАРКЕРОМ в ответе.
    /// Здесь же объясняется сама перебивка: человек может написать во время работы, и такое
    /// сообщение придёт агенту прямо в ходе задания — на него нужно ответить.
    /// </summary>
    /// <param name="language">Язык промпта — язык команды задачи (T-190).</param>
    public static string ChatMarkerNote(string marker, string? language = null) =>
        Loc.In(language, "prompt.tasks.12") +
        marker + Loc.In(language, "prompt.tasks.13");

    private string GetParentTask()
    {
        if (_current.ParentId is null)
        {
            return Loc.In(Language, "prompt.tasks.14");
        }
        var parent = _tasks.Get(_current.ParentId);
        return parent is null || parent.DeletedAt is not null
            ? Loc.In(Language, "prompt.tasks.15")
            : Render(parent);
    }

    /// <summary>
    /// Родительское задание ТЕКСТОМ для агента, которому инструменты AI2P не публикуются
    /// (CLI-агент, ТЗ п. 7.2): формулировка, критерии приёмки, результаты-артефакты И чат —
    /// то же, что дал бы вызов get_parent_task + get_task_chat (действие AI2P.Tasks.GetParent).
    /// Блок подставляется в промпт задания, потому что позвать инструмент такой агент не может,
    /// а в файлах проекта текста родительской задачи нет — она живёт в БД организации (T-120).
    /// Пусто — родителя нет либо он удалён.
    ///
    /// <para>С T-29-S0 подстановка идёт ТОЛЬКО по галочке задачи «родительское задание
    /// подставлять в промпт» (T-23-S0). Без галочки вместо текста родителя едет ОДНА СТРОКА
    /// с его кодом и заголовком: сам блок весит до 40 000 знаков и нужен далеко не каждой
    /// подзадаче, но и молчать нельзя — кода родителя агент не знает ниоткуда, а прочитать
    /// его он вправе всегда (вызов клиента <c>ai2p parent</c> или маркер AI2P_GET_TASK).
    /// Галочка управляет только автоподстановкой: ни инструмент, ни маркер она не закрывает.</para>
    /// </summary>
    public string ParentPromptBlock()
    {
        if (_current.ParentId is null)
        {
            return "";
        }
        var parent = _tasks.Get(_current.ParentId);
        if (parent is null || parent.DeletedAt is not null)
        {
            return "";
        }
        if (!_current.ParentInPrompt)
        {
            var note = Loc.In(Language, "prompt.tasks.136", parent.DisplayId, parent.Title);
            CallLog.Add(Loc.T("msg.taskToolset.3", parent.DisplayId));
            return note;
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.16", parent.DisplayId, parent.Title));
        sb.AppendLine();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.17"));
        sb.AppendLine();
        sb.AppendLine(Render(parent, chatHint: false));
        if (_chat.ListByTask(parent.Id).Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(RenderChat(parent));
        }
        var text = sb.ToString().TrimEnd();
        if (text.Length > MaxBlockChars)
        {
            text = text[..MaxBlockChars] + Loc.In(Language, "prompt.tasks.18", MaxBlockChars);
        }
        CallLog.Add(Loc.T("msg.taskToolset.1", parent.DisplayId, text.Length));
        return text;
    }

    /// <summary>
    /// get_sibling_tasks (T-132): задания-соседи — подзадачи ТОГО ЖЕ родителя, что и текущая
    /// задача (кроме неё самой): код, заголовок, статус, исполнитель, ссылка и результаты
    /// (артефакты). Формулировок здесь нет намеренно — соседей бывает много; полный текст
    /// нужного читается отдельно (get_task_by_code). Нужно, когда работа разбита на подзадачи
    /// и очередной исполнитель должен опираться на то, что уже сделали остальные.
    /// </summary>
    private string GetSiblingTasks()
    {
        if (_current.ParentId is null)
        {
            return Loc.In(Language, "prompt.tasks.19");
        }
        var parent = _tasks.Get(_current.ParentId);
        if (parent is null || parent.DeletedAt is not null)
        {
            return Loc.In(Language, "prompt.tasks.20");
        }
        var siblings = Siblings();
        return siblings.Count == 0
            ? Loc.In(Language, "prompt.tasks.21", parent.DisplayId)
            : RenderSiblings(parent, siblings, MaxSiblingResultChars, MaxBlockChars);
    }

    /// <summary>
    /// Задания-соседи ТЕКСТОМ для агента, которому инструменты AI2P не публикуются
    /// (CLI-агент, ТЗ п. 7.2): то же, что вернул бы вызов get_sibling_tasks (действие
    /// AI2P.Tasks.GetSiblings). Блок едет в промпте рядом с родительским (T-120), поэтому
    /// он ещё короче: формулировок соседей нет вовсе, результаты обрезаются жёстче.
    /// Пусто — родителя нет, он удалён или соседей нет.
    /// </summary>
    public string SiblingsPromptBlock()
    {
        if (_current.ParentId is null)
        {
            return "";
        }
        var parent = _tasks.Get(_current.ParentId);
        if (parent is null || parent.DeletedAt is not null)
        {
            return "";
        }
        var siblings = Siblings();
        if (siblings.Count == 0)
        {
            return "";
        }
        // ГАЛОЧКА «соседние задания подставлять в промпт» (T-29-S0, поле T-23-S0): без неё
        // вместо блока едет одна строка — коды, заголовки и состояния соседей. Так агент
        // знает, что соседи есть и как их зовут, а читать их (вызовом ai2p siblings или
        // маркером) он вправе и без галочки: она управляет только автоподстановкой
        if (!_current.SiblingsInPrompt)
        {
            var list = string.Join("; ", siblings.Take(MaxFoundTasks)
                .Select(t => Loc.In(Language, "prompt.tasks.138", t.DisplayId, t.Title, t.Status)));
            var note = Loc.In(Language, "prompt.tasks.137", siblings.Count, list);
            CallLog.Add(Loc.T("msg.taskToolset.4", siblings.Count));
            return note;
        }
        var text = RenderSiblings(parent, siblings, MaxSiblingBlockResultChars, MaxSiblingsBlockChars);
        CallLog.Add(Loc.T("msg.taskToolset.2", siblings.Count, text.Length));
        return text;
    }

    /// <summary>Подзадачи того же родителя, кроме текущей (не удалённые), в порядке списка
    /// задач — по убыванию числового приоритета, то есть в порядке запуска.</summary>
    private List<TaskItem> Siblings() =>
        _current.ParentId is null
            ? []
            : ProjectTasks(_current.IsTemplate)
                .Where(t => t.ParentId == _current.ParentId
                            && !string.Equals(t.Id, _current.Id, StringComparison.Ordinal))
                .ToList();

    /// <summary>Список соседей текстом: шапка + по заданию код, заголовок, статус, исполнитель,
    /// ссылка и результаты-артефакты. <paramref name="resultChars"/> — предел результата одного
    /// задания, <paramref name="totalChars"/> — предел всего текста.</summary>
    private string RenderSiblings(TaskItem parent, List<TaskItem> siblings, int resultChars, int totalChars)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.22", parent.DisplayId, parent.Title, siblings.Count));
        sb.AppendLine();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.23"));
        var shown = siblings.Take(MaxFoundTasks).ToList();
        foreach (var task in shown)
        {
            sb.AppendLine();
            sb.AppendLine($"## {task.DisplayId}: {task.Title}");
            var who = task.ExecutorIds.Count == 0
                ? Loc.In(Language, "prompt.tasks.24")
                : string.Join(", ", task.ExecutorIds.Select(Nick));
            var link = TaskLink(task);
            sb.AppendLine(Loc.In(Language, "prompt.tasks.25", task.Status, who)
                          + (link is null ? "" : Loc.In(Language, "prompt.tasks.26", link)));
            var artifacts = _tasks.Artifacts(task);
            if (artifacts.Count == 0)
            {
                sb.AppendLine(Loc.In(Language, "prompt.tasks.27"));
                continue;
            }
            foreach (var rel in artifacts)
            {
                sb.AppendLine();
                sb.AppendLine($"### {Path.GetFileName(rel)}");
                sb.AppendLine(ClipTo(_files.ReadText(rel), resultChars, Language));
            }
        }
        if (siblings.Count > shown.Count)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.28", siblings.Count - shown.Count, MaxFoundTasks));
        }
        var text = sb.ToString().TrimEnd();
        return text.Length <= totalChars
            ? text
            : text[..totalChars] + Loc.In(Language, "prompt.tasks.29", totalChars);
    }

    /// <summary>Внешняя ссылка на задачу (…/task/&lt;id&gt;) — по ней человек откроет задание
    /// в интерфейсе; null — внешний адрес организации не задан.</summary>
    private string? TaskLink(TaskItem task) =>
        _publicBaseUrl is null ? null : $"{_publicBaseUrl}/task/{task.Id}";

    // --- потомки и смена статуса чужой задачи (T-34-S0) ---

    /// <summary>Сколько подзадач показывать в ответе get_child_tasks: список должен
    /// оставаться списком, а не превращаться в выгрузку проекта.</summary>
    private const int MaxChildTasks = 100;

    /// <summary>Предел глубины обхода в get_child_tasks (1 — только прямые подзадачи).</summary>
    private const int MaxChildDepth = 3;

    /// <summary>
    /// get_child_tasks (T-34-S0): ПОДЗАДАЧИ задачи — код, заголовок, состояние, исполнитель
    /// и ссылка каждой. По умолчанию берутся подзадачи ТЕКУЩЕЙ задачи; параметр code —
    /// любая задача того же проекта (например родитель, чтобы увидеть всю ветку выпуска),
    /// depth — глубина обхода (1 — только прямые потомки, максимум
    /// <see cref="MaxChildDepth"/>).
    ///
    /// <para>Формулировок и результатов здесь нет НАМЕРЕННО: восемь полных карточек — это
    /// десятки килобайт контекста, то есть ровно та беда, ради которой всё это и делается.
    /// Полный текст нужной подзадачи читается отдельно по её коду.</para>
    /// </summary>
    private string GetChildTasks(JsonElement args)
    {
        var code = (OptionalString(args, "code") ?? "").Trim();
        var all = ProjectTasks(includeTemplates: _current.IsTemplate).ToList();
        TaskItem root;
        if (code.Length == 0)
        {
            root = _current;
        }
        else
        {
            var found = all.FirstOrDefault(t =>
                t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                return Loc.In(Language, "prompt.tasks.30", code);
            }
            root = found;
        }
        var depth = args.ValueKind == JsonValueKind.Object
                    && args.TryGetProperty("depth", out var d) && d.TryGetInt32(out var value)
            ? Math.Clamp(value, 1, MaxChildDepth)
            : 1;

        var body = new StringBuilder();
        var shown = 0;
        var total = 0;
        void Walk(string parentId, int level)
        {
            foreach (var task in all.Where(t => t.ParentId == parentId))
            {
                total++;
                if (shown < MaxChildTasks)
                {
                    shown++;
                    var who = task.ExecutorIds.Count == 0
                        ? Loc.In(Language, "prompt.tasks.24")
                        : string.Join(", ", task.ExecutorIds.Select(Nick));
                    var link = TaskLink(task);
                    body.AppendLine(new string(' ', (level - 1) * 2) +
                                    $"- {task.DisplayId} «{task.Title}» — " +
                                    Loc.In(Language, "prompt.tasks.25", task.Status, who) +
                                    (link is null ? "" : Loc.In(Language, "prompt.tasks.26", link)));
                }
                if (level < depth)
                {
                    Walk(task.Id, level + 1);
                }
            }
        }
        Walk(root.Id, 1);
        if (total == 0)
        {
            return Loc.In(Language, "prompt.tasks.125", root.DisplayId, root.Title);
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.126", root.DisplayId, root.Title, total));
        sb.AppendLine();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.127", depth));
        sb.AppendLine();
        sb.Append(body);
        if (total > shown)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.28", total - shown, MaxChildTasks));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// set_task_status (T-34-S0): СМЕНИТЬ СОСТОЯНИЕ ЧУЖОЙ ЗАДАЧИ того же проекта —
    /// с флагами перезапуска. Нужно задаче-прогонщику (T-31-S0): она собирает у соседей
    /// список тестов, гоняет их и, если что-то упало, возвращает соответствующие задачи
    /// в работу.
    ///
    /// <para>Три ограничения. (1) СВОЮ задачу так не тронуть: её состояние ставит система
    /// по итогу задания («Статус при завершении», T-250), и агент, переписавший его сам,
    /// получил бы гонку с собственным коннектором. (2) Задачи только своего проекта —
    /// как у move_task. (3) Код состояния — из встроенных: придуманный код тихо превратил
    /// бы задачу в невидимую ни одному представлению.</para>
    ///
    /// <para><c>restart: true</c> — не просто состояние, а именно ПЕРЕЗАПУСК: с задачи
    /// снимаются пометки «эта очередь уже перезапускала» (T-186, T-210), снимается отложенный
    /// старт (T-121) и режим запуска ставится автоматическим — иначе задача, возвращённая
    /// в pending вне открытой очереди иерархии, не тронулась бы с места вовсе
    /// (<c>AutoStartUnblockedAsync</c> берёт только задачи с mode=auto). Флаги ставятся
    /// ДО смены состояния: завершение и смена состояния сами двигают очередь.</para>
    /// </summary>
    private string SetTaskStatus(JsonElement args)
    {
        if (!CanSetStatus)
        {
            return Loc.In(Language, "prompt.tasks.128");
        }
        var code = RequiredString(args, "code", Language).Trim();
        var status = RequiredString(args, "status", Language).Trim().ToLowerInvariant();
        var task = ProjectTasks(includeTemplates: true)
            .FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return Loc.In(Language, "prompt.tasks.30", code);
        }
        if (string.Equals(task.Id, _current.Id, StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.129");
        }
        if (!TaskStatuses.BuiltIn.Contains(status, StringComparer.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.130", status,
                string.Join(", ", TaskStatuses.BuiltIn));
        }
        var restart = args.ValueKind == JsonValueKind.Object
                      && args.TryGetProperty("restart", out var r) && r.ValueKind == JsonValueKind.True;
        var was = task.Status;
        if (restart)
        {
            _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyRetryFlag, false);
            _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyNeedsFixRetryFlag, false);
            _tasks.SetStartAfter(task.Id, null);
            _tasks.SetLaunchMode(task.Id, "auto");
        }
        if (string.Equals(was, status, StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.131", task.DisplayId, status)
                   + (restart ? Loc.In(Language, "prompt.tasks.133") : "");
        }
        var updated = _tasks.ChangeStatus(task.Id, status, _actorExecutorId);
        return Loc.In(Language, "prompt.tasks.132", updated.DisplayId, updated.Title, was, updated.Status)
               + (restart ? Loc.In(Language, "prompt.tasks.133") : "");
    }

    // --- повторная проверка после прогона тестов (T-31-S0) ---

    /// <summary>
    /// restart_task_for_recheck (T-31-S0): ПЕРЕЗАПУСТИТЬ ЧУЖУЮ ЗАДАЧУ ДЛЯ СВОЕЙ ПОВТОРНОЙ
    /// ПРОВЕРКИ — одно действие вместо четырёх, которые иначе надо помнить и делать подряд.
    /// Ради него всё и затевалось: задача, гоняющая тесты за всю ветку, возвращает в работу
    /// тех, у кого проверки упали, а потом ждёт их и гоняет снова.
    ///
    /// <para>Что делается и почему именно так — по порядку.</para>
    /// <list type="number">
    /// <item>«Статус при завершении» перезапускаемой задачи ставится в «готово»: для
    /// блокирующей «завершена» означает ТОЛЬКО «готово» (T-6-S1), и задача, оставленная
    /// с «проверкой», встала бы в проверку у человека, а ждущий её прогон не проснулся
    /// бы никогда.</item>
    /// <item>Снимаются потраченные попытки очереди (T-186, T-210) и отложенный старт (T-121),
    /// режим запуска ставится автоматическим — иначе задача не тронется с места ни в очереди
    /// иерархии, ни автозапуском.</item>
    /// <item>Задача прописывается БЛОКИРУЮЩЕЙ текущей: это и есть «режим ожидания» — очередь
    /// сама поднимет прогон, когда перезапущенные завершатся (<c>BlockersStateOf</c>).</item>
    /// <item>У корня открытой очереди иерархии поднимается галочка «запускать и задачи
    /// в доработке» (T-210): без неё проход очереди просто пройдёт мимо, и со стороны это
    /// выглядит как «система молчит».</item>
    /// <item>И только теперь состояние меняется на «доработка» — флаги ставятся ДО смены
    /// состояния, потому что смена состояния сама двигает очередь.</item>
    /// </list>
    ///
    /// <para>Текущая задача в «ожидает» здесь НЕ переводится намеренно: перезапустить надо
    /// обычно несколько задач, и уход в ожидание — отдельное решение самого агента, когда
    /// разбор прогона закончен (<see cref="WaitForRecheck"/>).</para>
    ///
    /// <para>Предок текущей задачи так не перезапускается: родитель в очереди идёт ПОСЛЕ
    /// своих потомков, и ожидание собственного родителя — это ожидание самого себя.</para>
    /// </summary>
    private string RestartTaskForRecheck(JsonElement args)
    {
        if (!CanSetStatus)
        {
            return Loc.In(Language, "prompt.tasks.128");
        }
        var code = RequiredString(args, "code", Language).Trim();
        var comment = (OptionalString(args, "comment") ?? "").Trim();
        var task = ProjectTasks()
            .FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return Loc.In(Language, "prompt.tasks.30", code);
        }
        if (string.Equals(task.Id, _current.Id, StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.129");
        }
        if (IsAncestorOfCurrent(task))
        {
            return Loc.In(Language, "prompt.tasks.140", task.DisplayId);
        }
        // 1. «завершена» для блокирующей — это только «готово» (T-6-S1)
        _tasks.SetAiDoneStatus(task.Id, TaskStatuses.Done);
        // 2. потраченные попытки очереди, отложенный старт, режим запуска
        _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyRetryFlag, false);
        _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyNeedsFixRetryFlag, false);
        _tasks.SetStartAfter(task.Id, null);
        _tasks.SetLaunchMode(task.Id, "auto");
        // 3. ожидание: перезапущенная задача становится блокирующей у текущей
        var blocked = _tasks.AddBlocker(_current.Id, task.Id);
        // 4. открытой очереди иерархии — «доработку тоже запускать» (T-210)
        var root = _tasks.HierarchyRootOf(task);
        if (root is not null)
        {
            _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyNeedsFixFlag, true);
        }
        // 5. и только теперь состояние
        var was = task.Status;
        if (!string.Equals(was, TaskStatuses.NeedsFix, StringComparison.Ordinal))
        {
            task = _tasks.ChangeStatus(task.Id, TaskStatuses.NeedsFix, _actorExecutorId);
        }
        // записка исполнителю перезапущенной задачи: без неё он не узнает, что именно упало
        if (comment.Length > 0)
        {
            _chat.Add(task.Id, _actorExecutorId!, null, comment);
        }
        var answer = new StringBuilder();
        answer.Append(Loc.In(Language, "prompt.tasks.141", task.DisplayId, task.Title, was));
        answer.Append(Loc.In(Language, blocked ? "prompt.tasks.142" : "prompt.tasks.143"));
        answer.Append(root is not null
            ? Loc.In(Language, "prompt.tasks.144", root.DisplayId)
            : Loc.In(Language, "prompt.tasks.145"));
        if (comment.Length > 0)
        {
            answer.Append(Loc.In(Language, "prompt.tasks.146"));
        }
        return answer.ToString();
    }

    /// <summary>
    /// wait_for_recheck (T-31-S0): УЙТИ В ОЖИДАНИЕ перезапущенных задач. Ставит текущей
    /// задаче пометку «по концу задания — не „статус при завершении“, а „ожидает“»; сам
    /// уход происходит, когда агент закончит ответ, — тогда же освобождается исполнитель
    /// и может взять перезапущенные задачи (в том числе те, что он же и перезапустил).
    ///
    /// <para>Отказывает в двух случаях, и оба важнее удобства. (1) Ждать нечего: ни одной
    /// блокирующей задачи в работе — уход в «ожидает» означал бы, что задача не проснётся
    /// вовсе. (2) Исчерпан предел кругов проекта (<see cref="ProjectSettings.RecheckLimit"/>,
    /// по умолчанию три): тест мог падать не из-за тех задач, которые перезапускают, и без
    /// предела круг «прогнал → доработка → прогнал» не кончился бы никогда. Отказ — не беда,
    /// а указание закончить работу и рассказать человеку, что осталось красным.</para>
    /// </summary>
    private string WaitForRecheck(JsonElement args)
    {
        _ = args;
        var current = _tasks.Get(_current.Id) ?? _current;
        // считаем именно НЕЗАВЕРШЁННЫЕ блокирующие: у задачи могли остаться и старые,
        // давно готовые, а агенту важно, скольких он в самом деле ждёт
        var blockers = current.BlockerIds
            .Select(id => _tasks.Get(id))
            .Where(t => t is not null && t.DeletedAt is null)
            .ToList();
        var awaited = blockers.Count(t => t!.Status != TaskStatuses.Done);
        // все блокирующие уже готовы — ждать имеет смысл, ТОЛЬКО пока открыта очередь
        // иерархии: она поднимает готовую к запуску задачу сама. Вне очереди ждущую задачу
        // будит лишь завершение блокирующей, а оно уже случилось — задача уснула бы навсегда
        var inQueue = _tasks.HierarchyRootOf(current) is not null;
        if (blockers.Count == 0 || (awaited == 0 && !inQueue))
        {
            return Loc.In(Language, "prompt.tasks.147");
        }
        var limit = _tasks.RecheckLimitOf(current.Id);
        var pass = TaskService.RecheckPass(current.LaunchJson) + 1;
        if (pass > limit)
        {
            return Loc.In(Language, "prompt.tasks.148", limit);
        }
        _tasks.SetRecheckWait(current.Id, pass);
        // режим запуска: ждущую задачу ВНЕ открытой очереди иерархии поднимает только
        // автозапуск по завершении блокирующей, а он берёт задачи с режимом «auto» (T-34-S0)
        _tasks.SetLaunchMode(current.Id, "auto");
        return Loc.In(Language, "prompt.tasks.149", awaited, pass, limit);
    }

    /// <summary>Задача — предок текущей (T-31-S0): подниматься по цепочке родителей дешевле,
    /// чем обходить поддерево, а предел глубины тот же, что у обходов иерархии.</summary>
    private bool IsAncestorOfCurrent(TaskItem task)
    {
        var current = _current;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; current is not null && depth <= TaskService.MaxHierarchyDepth
                            && seen.Add(current.Id); depth++)
        {
            if (string.Equals(current.Id, task.Id, StringComparison.Ordinal) && depth > 0)
            {
                return true;
            }
            current = current.ParentId is null ? null : _tasks.Get(current.ParentId);
        }
        return false;
    }

    /// <summary>
    /// То же про перезапуск чужой задачи и уход в ожидание — для агента БЕЗ инструментов
    /// AI2P (CLI-агент, T-31-S0): маркеры в ответе — запасной путь, когда клиент командной
    /// строки недоступен.
    /// </summary>
    public string RecheckMarkerNote(string restartMarker, string waitMarker) =>
        Loc.In(Language, "prompt.tasks.139", restartMarker, waitMarker);

    /// <summary>
    /// Записка В ЧАТ ЗАДАЧИ о том, что правила безопасности не дали сменить состояние чужой
    /// задачи (T-31-S0). Без неё отказ виден только агенту: он получает текст «правило
    /// deny», а человек не узнаёт ни что работа встала, ни что от него требуется.
    /// <para>Пишется НЕ ЧАЩЕ ОДНОГО РАЗА на задание для каждого действия: агент вполне может
    /// попробовать перезапустить десяток задач подряд, и десять одинаковых сообщений в чате
    /// хуже, чем ни одного.</para>
    /// </summary>
    public void ReportDeniedTaskChange(string toolName, JsonElement args, string ruleInfo)
    {
        if (_actorExecutorId is null || !_deniedReported.Add(toolName))
        {
            return;
        }
        var code = (OptionalString(args, "code") ?? "").Trim();
        try
        {
            _chat.Add(_current.Id, _actorExecutorId,
                null,
                Loc.T("msg.task.33", code.Length > 0 ? code : Loc.T("msg.task.34"), ruleInfo));
        }
        catch (Exception)
        {
            // чат не обязан быть доступен (задача удалена по ходу задания) — отказ агенту
            // всё равно уже вернулся, и ронять из-за записки в чат нечего
        }
    }

    /// <summary>Действия, про отказ по которым человеку уже написали (T-31-S0).</summary>
    private readonly HashSet<string> _deniedReported = new(StringComparer.Ordinal);

    /// <summary>
    /// То же про смену состояния чужой задачи для агента БЕЗ инструментов AI2P (CLI-агент,
    /// T-34-S0): маркер в ответе — запасной путь, когда клиент командной строки недоступен.
    /// </summary>
    public string StatusMarkerNote(string marker) =>
        Loc.In(Language, "prompt.tasks.134") + marker + Loc.In(Language, "prompt.tasks.135",
            string.Join(", ", TaskStatuses.BuiltIn));

    private string GetTaskByCode(string code)
    {
        code = code.Trim();
        // узлы шаблонов читаются наравне с задачами (T-144): чтобы поправить шаблон,
        // агент должен сперва увидеть его формулировку, а списком list_templates даются
        // только коды и заголовки
        var task = ProjectTasks(includeTemplates: true)
            .FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
        return task is null
            ? Loc.In(Language, "prompt.tasks.30", code)
            : Render(task);
    }

    /// <summary>
    /// ИДЕНТИФИКАТОР ЗАДАЧИ ИЗ НАШЕЙ ССЫЛКИ; пусто — ссылка не наша.
    ///
    /// Поиск ИНВАРИАНТЕН К ПРОТОКОЛУ И К КОРНЮ САЙТА (T-196-S0): ни <c>http</c>/<c>https</c>,
    /// ни имя сервера, ни базовый путь не участвуют — берётся сегмент после <c>/task/</c>.
    /// Годится и полный адрес (<c>https://мойпк:5480/ai2p/org/task/…</c>), и относительный
    /// от корня сайта (<c>/ai2p/org/task/…</c>): одна и та же задача есть на каждом сервере
    /// кластера, задачи реплицируются.
    /// </summary>
    private static string TaskIdInUrl(string url)
    {
        var marker = url.LastIndexOf("/task/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return "";
        }
        var id = url[(marker + "/task/".Length)..].Trim().TrimEnd('/');
        var query = id.IndexOfAny(['?', '#']);
        return query >= 0 ? id[..query] : id;
    }

    /// <summary>Живая задача по нашей ссылке; null — ссылка не наша либо задачи здесь нет.</summary>
    private TaskItem? LocalTaskOf(string url) =>
        TaskIdInUrl(url) is { Length: > 0 } id && _tasks.Get(id) is { DeletedAt: null } task
            ? task
            : null;

    private string GetTaskByUrl(string url)
    {
        if (TaskIdInUrl(url).Length == 0)
        {
            return Loc.In(Language, "prompt.tasks.31");
        }
        var task = LocalTaskOf(url);
        if (task is null)
        {
            return Loc.In(Language, "prompt.tasks.32", url);
        }
        if (task.ProjectId != _current.ProjectId)
        {
            return Loc.In(Language, "prompt.tasks.33");
        }
        return Render(task);
    }

    private string FindTasksByTitle(string title)
    {
        title = title.Trim();
        var found = ProjectTasks(includeTemplates: true)
            .Where(t => t.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase))
            .Take(MaxFoundTasks)
            .ToList();
        if (found.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.34", title);
        }
        var sb = new StringBuilder();
        foreach (var t in found)
        {
            sb.AppendLine(Loc.In(Language, "prompt.tasks.35", t.DisplayId, t.Title, (t.IsTemplate ? Loc.In(Language, "prompt.tasks.96") : t.Status))
                          + (t.ParentId is null ? "" : Loc.In(Language, "prompt.tasks.36", CodeOf(t.ParentId))));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Чат задания (todo23-уточнение): переписка людей и агентов, вопросы и ответы.
    /// code пуст — чат текущей задачи; иначе — задача этого же проекта.</summary>
    private string GetTaskChat(string? code)
    {
        TaskItem task;
        if (code is null || code.Trim().Length == 0)
        {
            task = _current;
        }
        else
        {
            var found = ProjectTasks()
                .FirstOrDefault(t => t.DisplayId.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                return Loc.In(Language, "prompt.tasks.37", code.Trim());
            }
            task = found;
        }
        return RenderChat(task);
    }

    /// <summary>Переписка задания текстом: показываются последние MaxChatMessages сообщений.</summary>
    private string RenderChat(TaskItem task)
    {
        var messages = _chat.ListByTask(task.Id);
        if (messages.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.38", task.DisplayId);
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.39", task.DisplayId, task.Title, messages.Count));
        if (messages.Count > MaxChatMessages)
        {
            sb.AppendLine(Loc.In(Language, "prompt.tasks.40", MaxChatMessages));
        }
        foreach (var message in messages.TakeLast(MaxChatMessages))
        {
            var kind = message.Kind switch
            {
                ChatMessageKind.Question => message.AnsweredAt is null
                    ? Loc.In(Language, "prompt.tasks.41")
                    : Loc.In(Language, "prompt.tasks.42"),
                ChatMessageKind.Answer => Loc.In(Language, "prompt.tasks.43"),
                _ => "",
            };
            var to = message.ToExecutorId is null ? "" : $" → {Nick(message.ToExecutorId)}";
            sb.AppendLine();
            // автор из внешней системы (T-152) назван так же, как в чате: «trello:<логин>»
            var from = message.AuthorName is { Length: > 0 } author
                ? author
                : Nick(message.FromExecutorId);
            sb.AppendLine($"[{message.CreatedAt:yyyy-MM-dd HH:mm} UTC] {from}{to}{kind}:");
            sb.AppendLine(ClipMessage(message.Text));
            var options = message.Options();
            if (options.Count > 0)
            {
                sb.AppendLine(Loc.In(Language, "prompt.tasks.44", string.Join(" / ", options)));
            }
        }
        var text = sb.ToString().TrimEnd();
        return text.Length <= MaxTextChars ? text : text[..MaxTextChars] + Loc.In(Language, "prompt.tasks.45");
    }

    /// <summary>
    /// create_task (ТЗ v1.26, todo28): агент создаёт подзадачу текущей задачи — заголовок,
    /// описание, skills (имена из справочника), числовой приоритет, критерии приёмки.
    /// Исполнитель подбирается автоматически «сначала ИИ, потом человек» с учётом
    /// цена↔качество проекта. Первый вызов помечает текущую задачу разбитой
    /// (isNotSplit + флаг autoSplit — по нему оркестратор ведёт очередь запуска подзадач).
    /// У задачи-шаблона подзадачи создаются шаблонными и не исполняются.
    /// </summary>
    private string CreateTask(JsonElement args)
    {
        if (!CanCreate)
        {
            return Loc.In(Language, "prompt.tasks.46");
        }
        var title = RequiredString(args, "title", Language).Trim();
        var description = RequiredString(args, "description", Language);
        var acceptance = OptionalString(args, "acceptance") ?? "";
        var priorityNum = args.ValueKind == JsonValueKind.Object
                          && args.TryGetProperty("priority", out var p) && p.TryGetInt32(out var num)
            ? Math.Max(0, num)
            : TaskPriority.DefaultMedium;

        if (!TryResolveSkills(args, out var skillIds, out var skillError))
        {
            return skillError;
        }

        var subtask = new TaskItem
        {
            ParentId = _current.Id,
            ProjectId = _current.ProjectId,
            TeamId = _current.TeamId,
            Kind = TaskKind.Task,
            Title = title,
            Status = TaskStatuses.Pending,
            Priority = TaskPriority.LevelFor(priorityNum),
            PriorityNum = priorityNum,
            IsTemplate = _current.IsTemplate,
            SkillIds = skillIds,
        };

        // подбор исполнителя «сначала ИИ, потом человек» (ТЗ v1.26). Внутри ОТКРЫТОЙ ОЧЕРЕДИ
        // ИЕРАРХИИ — вдобавок «сначала свободные» (T-160-S0): такая подзадача пойдёт в работу
        // сейчас, и занятый исполнитель означает ожидание, а то и взаимное (задача, ждущая
        // конца всей иерархии, назначенная подзадаче этой же иерархии, не дождётся никогда)
        var inQueue = _tasks.HierarchyRootOf(_current) is not null;
        var picked = _picker!.Pick(_current.ProjectId, _current.TeamId, skillIds, PickMode.AiFirst,
            startAt: null, preferFree: inQueue);
        if (picked.ExecutorId is not null)
        {
            subtask.ExecutorIds = [picked.ExecutorId];
            // СУФЛЁР подбирается вместе с исполнителем (T-292-S0): медиа-модель, которой
            // нужен управляющий json, без него не запустится вовсе — подзадача встала бы
            // ошибкой при первом же старте
            subtask.PrompterExecutorId = picked.PrompterExecutorId;
        }

        // очередь иерархического запуска узнаёт о подзадаче ПОСЛЕ того, как та дописана
        // (T-16-S0): ниже ей может проставляться отложенный старт (T-138), и проход по
        // ещё не дописанной задаче запустил бы проверочную подзадачу прямо сейчас
        subtask = _tasks.Create(subtask, description, acceptance, _actorExecutorId, notify: false);
        // защита от повторного разбиения + флаг autoSplit для оркестратора (идемпотентно)
        _tasks.MarkSplit(_current.Id);

        // отложенный старт подзадачи (T-138): проверить результат долгой фоновой работы
        // (прогон тестов, сборка) сразу нельзя — она ещё идёт, а фоновый процесс агента
        // всё равно обрывается вместе с его ходом. Механизм тот же, что у переноса старта
        // по лимиту (T-121): startAfter в launch_json, задачу поднимает сторож оркестратора
        var delay = DelayMinutes(args);
        var startAfter = delay > 0 ? DateTime.UtcNow.AddMinutes(delay) : (DateTime?)null;
        if (startAfter is { } at)
        {
            _tasks.SetStartAfter(subtask.Id, at);
        }
        // подзадача дописана — теперь о ней можно рассказать очереди иерархии (T-16-S0)
        _tasks.NotifyCreated(_tasks.Get(subtask.Id) ?? subtask, _actorExecutorId);

        var executorText = picked.ExecutorId is null
            ? Loc.In(Language, "prompt.tasks.47", picked.Reason)
            : Loc.In(Language, "prompt.tasks.48", picked.Nick, Loc.In(Language, picked.Kind == ExecutorKind.Ai ? "prompt.tasks.97" : "prompt.tasks.98"), picked.Reason);
        return Loc.In(Language, "prompt.tasks.49", subtask.DisplayId, subtask.Title, priorityNum)
               + executorText
               + (startAfter is { } when
                   ? Loc.In(Language, "prompt.tasks.50", delay, when.ToLocalTime())
                   : "");
    }

    /// <summary>
    /// move_task (T-2-S0): ПЕРЕНЕСТИ ЗАДАЧУ ПО ИЕРАРХИИ — сменить ей родителя или сделать её
    /// корневой (parent пуст либо «root»). Задача и новый родитель берутся по коду среди задач
    /// ТОГО ЖЕ ПРОЕКТА: подзадача обязана лежать в проекте родителя, это же правило проверяет
    /// и хранилище (<see cref="TaskService.ChangeParent"/> — там же запреты «сам себе родитель»,
    /// «родитель из своего поддерева», «шаблон и задача не смешиваются» и правило владения
    /// сервером).
    ///
    /// <para>Зачем это агенту: выпускающая задача, у которой один потомок так и не выполнен,
    /// получает от человека решение «выпускать без него, перенести в следующий выпуск» — и до
    /// T-2-S0 выполнить это решение было НЕЧЕМ: агент писал в отчёт «перевесить должен человек»,
    /// а невыполненный потомок оставался в иерархии выпущенной версии.</para>
    ///
    /// <para>СВОЮ задачу агент не переносит: очередь иерархии ведётся по её корню, и агент,
    /// уводящий себя из поддерева, менял бы условия собственного запуска. Такой перенос —
    /// решение человека, и его надо просить вопросом.</para>
    /// </summary>
    private string MoveTask(JsonElement args)
    {
        var code = RequiredString(args, "code", Language).Trim();
        var parentCode = (OptionalString(args, "parent") ?? "").Trim();
        var task = ProjectTasks(includeTemplates: true)
            .FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return Loc.In(Language, "prompt.tasks.30", code);
        }
        if (string.Equals(task.Id, _current.Id, StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.115");
        }
        // пусто или «root» — задача становится корневой: ровно тот исход, который человек
        // называет «вынести из этого выпуска», когда следующей выпускающей задачи ещё нет
        var toRoot = parentCode.Length == 0
                     || parentCode.Equals("root", StringComparison.OrdinalIgnoreCase);
        TaskItem? parent = null;
        if (!toRoot)
        {
            parent = ProjectTasks(includeTemplates: true)
                .FirstOrDefault(t => t.DisplayId.Equals(parentCode, StringComparison.OrdinalIgnoreCase));
            if (parent is null)
            {
                return Loc.In(Language, "prompt.tasks.116", parentCode);
            }
        }
        var wasCode = task.ParentId is null
            ? Loc.In(Language, "prompt.tasks.117")
            : _tasks.Get(task.ParentId)?.DisplayId ?? Loc.In(Language, "prompt.tasks.117");
        var moved = _tasks.ChangeParent(task.Id, parent?.Id, _actorExecutorId);
        return toRoot
            ? Loc.In(Language, "prompt.tasks.118", moved.DisplayId, moved.Title, wasCode)
            : Loc.In(Language, "prompt.tasks.119", moved.DisplayId, moved.Title, parent!.DisplayId,
                parent.Title, wasCode);
    }

    /// <summary>
    /// update_task (T-160-S0): ИЗМЕНИТЬ ПОЛЯ ЧУЖОЙ ЗАДАЧИ того же проекта — исполнителя,
    /// список «могут заменить», ответственного, навыки и тэги.
    ///
    /// <para>Ради чего заведено: у задачи внутри запущенной иерархии не был указан
    /// исполнитель, человек попросил агента проставить его — и сделать это было НЕЧЕМ.
    /// Агент предложил вместо правки создать копию задачи, а копии автоподбор выдал того же
    /// занятого исполнителя, и работа встала окончательно.</para>
    ///
    /// <para>Каждое поле необязательно: не названное — не трогается, названное пустым
    /// (пустая строка у исполнителя и ответственного, пустой список у остальных) — очищается.
    /// Исполнители называются НИКАМИ (агент видит в задании их, а не идентификаторы),
    /// навыки — кодами справочника, как в create_task.</para>
    ///
    /// <para>Свою задачу так не правят — по той же причине, что и в set_task_status с
    /// move_task: исполнителя текущей задачи выбирает система при запуске, и агент,
    /// переписавший его себе, спорил бы с собственным коннектором.</para>
    /// </summary>
    private string UpdateTask(JsonElement args)
    {
        if (!CanUpdate)
        {
            return Loc.In(Language, "prompt.tasks.150");
        }
        var code = RequiredString(args, "code", Language).Trim();
        var task = ProjectTasks(includeTemplates: true)
            .FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return Loc.In(Language, "prompt.tasks.30", code);
        }
        if (string.Equals(task.Id, _current.Id, StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.tasks.151");
        }

        // исполнитель и ответственный — по нику; пустая строка означает «снять»
        List<string>? executorIds = null;
        if (OptionalString(args, "executor") is { } executorNick)
        {
            if (!TryResolveExecutors([executorNick], out executorIds, out var executorError))
            {
                return executorError;
            }
        }
        List<string>? altExecutorIds = null;
        if (StringList(args, "altExecutors") is { } altNicks
            && !TryResolveExecutors(altNicks, out altExecutorIds, out var altError))
        {
            return altError;
        }
        string? responsibleId = null;
        if (OptionalString(args, "responsible") is { } responsibleNick)
        {
            if (!TryResolveExecutors([responsibleNick], out var found, out var responsibleError))
            {
                return responsibleError;
            }
            responsibleId = found.Count == 0 ? "" : found[0];
        }
        List<string>? skillIds = null;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("skills", out _))
        {
            if (!TryResolveSkills(args, out var resolved, out var skillError))
            {
                return skillError;
            }
            skillIds = resolved;
        }
        var tags = StringList(args, "tags");

        if (executorIds is null && altExecutorIds is null && responsibleId is null
            && skillIds is null && tags is null)
        {
            return Loc.In(Language, "prompt.tasks.153");
        }
        var updated = _tasks.ChangeAssignment(task.Id, executorIds, altExecutorIds, responsibleId,
            skillIds, tags, _actorExecutorId);
        var none = Loc.In(Language, "prompt.tasks.155");
        return Loc.In(Language, "prompt.tasks.154", updated.DisplayId, updated.Title,
            Nicks(updated.ExecutorIds, none), Nicks(updated.AltExecutorIds, none),
            updated.ResponsibleId is null ? none : Nick(updated.ResponsibleId),
            SkillNames(updated.SkillIds, none),
            updated.Tags.Count == 0 ? none : string.Join(", ", updated.Tags));
    }

    /// <summary>Ники исполнителей списком (для ответа агенту); пустой список — прочерк.</summary>
    private string Nicks(IReadOnlyList<string> executorIds, string none) =>
        executorIds.Count == 0 ? none : string.Join(", ", executorIds.Select(Nick));

    /// <summary>Имена навыков списком по их идентификаторам; пустой список — прочерк.</summary>
    private string SkillNames(IReadOnlyList<string> skillIds, string none)
    {
        if (skillIds.Count == 0)
        {
            return none;
        }
        var byId = (_refData?.Skills() ?? []).ToDictionary(s => s.Id, s => s.Name);
        return string.Join(", ", skillIds.Select(id => byId.TryGetValue(id, out var name) ? name : id));
    }

    /// <summary>
    /// Исполнители по НИКАМ (update_task, T-160-S0): агент видит в задании ники, а не
    /// идентификаторы. Пустое имя пропускается — так «executor»: «» и означает «снять
    /// исполнителя» (пустой список). Неизвестный ник — понятная ошибка со списком: угадывать
    /// за агента нельзя, назначение исполнителя решает, кто будет делать работу.
    /// </summary>
    private bool TryResolveExecutors(IEnumerable<string> names, out List<string> executorIds,
        out string error)
    {
        executorIds = [];
        error = "";
        var all = _executors.List();
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }
            var found = all.FirstOrDefault(e => e.Nick.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? all.FirstOrDefault(e => e.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                error = Loc.In(Language, "prompt.tasks.152", name,
                    string.Join(", ", all.Where(e => e.IsActive).Select(e => e.Nick)));
                executorIds = [];
                return false;
            }
            if (!executorIds.Contains(found.Id))
            {
                executorIds.Add(found.Id);
            }
        }
        return true;
    }

    /// <summary>Список строк из аргументов вызова; null — поля нет вовсе («не трогать»).</summary>
    private static List<string>? StringList(JsonElement args, string property)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        return list.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.Trim())
            .Where(value => value.Length > 0)
            .ToList();
    }

    // ---------- ветвление и циклы (T-300-S0, ветка T-297-S0) ----------
    // Условия вычисляет АГЕНТ, AI2P их не анализирует: агент сообщает решение действием,
    // оно ложится ключом flowDecision в launch_json СВОЕЙ задачи (TaskService.SetFlowDecision),
    // а очередь иерархии (JobOrchestrator, T-299-S0) читает его, когда задание сдано.
    // Отмену непройденной ветки, паузу «ждёт окончания цикла», перезапуск анализатора и
    // остановку по флажкам задачи делает ДВИЖОК — агенту на них действий намеренно нет.

    /// <summary>
    /// set_condition_result / set_loop_result (T-300-S0): решение задачи «Условие»
    /// (value: true — ветка «Да», false — «Нет») либо исход проверки условий цикла
    /// (continue: true — на круг, false — выход). Только про СВОЮ задачу и только нужного типа.
    /// Третьего исхода нет: значение принимается булевым JSON либо строкой «true»/«false»
    /// (так его передаёт клиент командной строки), всё прочее — «да», «1», «скорее нет»,
    /// пропуск — ОШИБКА, а не молчаливый выбор ветки. Повторный вызов переписывает решение.
    /// </summary>
    private string SetFlowDecision(JsonElement args, string property, bool loop)
    {
        var task = _tasks.Get(_current.Id) ?? _current;
        var type = TaskFlow.Read(task.LaunchJson).Type;
        if (loop ? !TaskFlow.IsLoop(type) : type != TaskFlow.If)
        {
            return Loc.In(Language, loop ? "prompt.tasks.173" : "prompt.tasks.172", task.DisplayId, type);
        }
        if (!TryStrictBool(args, property, out var decision, out var raw))
        {
            return Loc.In(Language, "prompt.tasks.174", property, raw);
        }
        _tasks.SetFlowDecision(task.Id, decision);
        var key = loop
            ? decision ? "prompt.tasks.177" : "prompt.tasks.178"
            : decision ? "prompt.tasks.175" : "prompt.tasks.176";
        return Loc.In(Language, key, task.DisplayId);
    }

    /// <summary>Строгое булево значение аргумента: true/false JSON либо строка «true»/«false»
    /// без учёта регистра. raw — что пришло на самом деле (для текста ошибки).</summary>
    private static bool TryStrictBool(JsonElement args, string property, out bool value, out string raw)
    {
        value = false;
        raw = "—";
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(property, out var v))
        {
            return false;
        }
        raw = v.GetRawText();
        switch (v.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                return true;
            case JsonValueKind.String:
                var text = (v.GetString() ?? "").Trim();
                if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }
                return text.Equals("false", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    /// <summary>
    /// create_tasks_from_template (T-300-S0): развернуть узел шаблона проекта подзадачей
    /// (<see cref="TaskService.InstantiateTemplate"/> с parentId, как кнопка «из шаблона»
    /// в карточке, T-201). Нужно ветке условия без задачи с флажком «создавать задачи»:
    /// типовую работу ветки удобнее держать шаблоном, чем пересказывать create_task по
    /// подзадаче. Родитель — своя задача либо задача из своего поддерева: чужое дерево
    /// агент так не растит (в чужой очереди новые задачи пошли бы в работу без спроса).
    /// </summary>
    private string CreateTasksFromTemplate(JsonElement args)
    {
        if (!CanFromTemplate)
        {
            return Loc.In(Language, "prompt.tasks.179");
        }
        var code = RequiredString(args, "template", Language).Trim();
        var templates = ProjectTemplates();
        var node = templates.FirstOrDefault(t => t.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase))
                   ?? templates.FirstOrDefault(t => t.Title.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return Loc.In(Language, "prompt.tasks.180", code);
        }
        var parent = _tasks.Get(_current.Id) ?? _current;
        var parentCode = (OptionalString(args, "parent") ?? "").Trim();
        if (parentCode.Length > 0 && !parentCode.Equals(parent.DisplayId, StringComparison.OrdinalIgnoreCase))
        {
            var found = ProjectTasks()
                .FirstOrDefault(t => t.DisplayId.Equals(parentCode, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                return Loc.In(Language, "prompt.tasks.186", parentCode);
            }
            if (!IsInOwnSubtree(found))
            {
                return Loc.In(Language, "prompt.tasks.181", found.DisplayId, _current.DisplayId);
            }
            parent = found;
        }
        var head = _tasks.InstantiateTemplate(node.Id, baseDate: null, _actorExecutorId, PickMode.AiFirst,
            _picker, parent.Id);
        return Loc.In(Language, "prompt.tasks.182", head.DisplayId, head.Title, node.DisplayId, parent.DisplayId);
    }

    /// <summary>Задача лежит в поддереве текущей (сама текущая сюда не входит).</summary>
    private bool IsInOwnSubtree(TaskItem task)
    {
        var parentId = task.ParentId;
        for (var depth = 0; parentId is not null && depth < 64; depth++)
        {
            if (string.Equals(parentId, _current.Id, StringComparison.Ordinal))
            {
                return true;
            }
            parentId = _tasks.Get(parentId)?.ParentId;
        }
        return false;
    }

    /// <summary>
    /// stop_hierarchy (T-300-S0): ЗАВЕРШИТЬ ВЫПОЛНЕНИЕ ИЕРАРХИИ, в которой идёт своя задача, —
    /// снять пометку открытой очереди у её корня (с галочками переспроса, как это делает сам
    /// движок, T-6-S1). Уже запущенные задания не снимаются: их останавливает человек кнопкой
    /// (T-263); не запустится только то, что очередь ещё не взяла. Флажки «завершить
    /// выполнение» у ветки условия и у цикла движок исполняет сам — действие для случая,
    /// когда агент по описанию задачи и чату решил, что дальше идти нельзя.
    /// </summary>
    private string StopHierarchy(JsonElement args)
    {
        if (!CanStopHierarchy)
        {
            return Loc.In(Language, "prompt.tasks.185");
        }
        var task = _tasks.Get(_current.Id) ?? _current;
        var root = _tasks.HierarchyRootOf(task);
        if (root is null)
        {
            return Loc.In(Language, "prompt.tasks.183", task.DisplayId);
        }
        _tasks.SetHierarchyRun(root.Id, false);
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyErrorsFlag, false);
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyNeedsFixFlag, false);
        var reason = (OptionalString(args, "reason") ?? "").Trim();
        return Loc.In(Language, "prompt.tasks.184", root.DisplayId, root.Title,
            reason.Length == 0 ? "—" : reason);
    }

    /// <summary>
    /// То же про перенос задачи для агента БЕЗ инструментов AI2P (CLI-агент, T-2-S0):
    /// перенос делается маркером в ответе, как подзадачи и опыт.
    /// </summary>
    public string MoveMarkerNote(string marker) =>
        Loc.In(Language, "prompt.tasks.120") + marker + Loc.In(Language, "prompt.tasks.121");

    /// <summary>
    /// Навыки из аргументов вызова (имена справочника, без учёта регистра) — общее для
    /// create_task и create_template (T-144). Неизвестное имя — понятная ошибка со списком
    /// доступных навыков: угадывать за агента нельзя, подбор исполнителя идёт по навыкам.
    /// </summary>
    private bool TryResolveSkills(JsonElement args, out List<string> skillIds, out string error)
    {
        skillIds = [];
        error = "";
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
        {
            return true;
        }
        List<Skill> catalog = _refData?.Skills() ?? [];
        var byName = catalog.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in skills.EnumerateArray())
        {
            var skillName = entry.ValueKind == JsonValueKind.String ? entry.GetString()!.Trim() : "";
            if (skillName.Length == 0)
            {
                continue;
            }
            if (!byName.TryGetValue(skillName, out var skillId))
            {
                error = Loc.In(Language, "prompt.tasks.51", skillName) +
                        string.Join(", ", catalog.Select(s => s.Name));
                skillIds = [];
                return false;
            }
            if (!skillIds.Contains(skillId))
            {
                skillIds.Add(skillId);
            }
        }
        return true;
    }

    /// <summary>Предел отложенного старта подзадачи (T-138): дольше суток ждать нечего —
    /// это уже работа для человека, а не «дождаться прогона тестов».</summary>
    public const int MaxStartDelayMinutes = 24 * 60;

    /// <summary>Задержка старта подзадачи из аргументов create_task (T-138), минуты;
    /// 0 — запуск как обычно. Отрицательное и мусор игнорируются, слишком большое
    /// подрезается пределом <see cref="MaxStartDelayMinutes"/>.</summary>
    private static int DelayMinutes(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty("startAfterMinutes", out var v) && v.TryGetInt32(out var minutes)
            ? Math.Clamp(minutes, 0, MaxStartDelayMinutes)
            : 0;

    /// <summary>
    /// Дописка к системному промпту про create_task (ТЗ v1.26): когда разбивать, список
    /// skills справочника, правило числового приоритета. Пусто — инструмент не опубликован.
    /// </summary>
    public string CreateNote =>
        !CanCreate
            ? ""
            : Loc.In(Language, "prompt.tasks.52") + SplitRules;

    /// <summary>
    /// То же про подзадачи, но для агента БЕЗ инструментов AI2P (CLI-агент, T-125): позвать
    /// create_task он не может, поэтому подзадача создаётся МАРКЕРОМ в ответе — строкой
    /// <paramref name="marker"/> с теми же параметрами, что у инструмента. Маркеры выполняет
    /// система после завершения работы агента (ClaudeCliConnector). Пусто — задача уже разбита.
    /// </summary>
    public string CreateMarkerNote(string marker) =>
        !CanCreate
            ? ""
            : Loc.In(Language, "prompt.tasks.53") +
              marker + Loc.In(Language, "prompt.tasks.54") + SplitRules;

    /// <summary>
    /// Дописка к системному промпту про ЧТЕНИЕ чужих заданий для агента БЕЗ инструментов AI2P
    /// (CLI-агент, T-127): позвать get_task_by_code / get_task_by_url / find_tasks_by_title /
    /// get_task_chat он не может, а задачи лежат в базе организации — в файлах проекта их нет.
    /// Поэтому задание запрашивается МАРКЕРОМ <paramref name="marker"/> в ответе: система
    /// выполняет запрос и присылает текст задания следующим сообщением ТОЙ ЖЕ сессии CLI
    /// (как ответ человека на вопрос), после чего агент продолжает работу.
    /// В отличие от инструмента, ответ маркера всегда полный: карточка (формулировка, критерии
    /// приёмки, статус, результаты-артефакты) И чат задания целиком.
    /// </summary>
    /// <param name="maxLookups">Предел таких запросов за одно задание (защита от зацикливания).</param>
    /// <param name="language">Язык промпта — язык команды задачи (T-190).</param>
    public static string ReadMarkerNote(string marker, int maxLookups, string? language = null) =>
        Loc.In(language, "prompt.tasks.55") +
        marker + Loc.In(language, "prompt.tasks.56", maxLookups);

    /// <summary>Общая часть промпта про разбиение (когда разбивать, навыки, приоритет) —
    /// одинакова для инструмента create_task и для маркера CLI-агента (T-125).</summary>
    private string SplitRules =>
        Loc.In(Language, "prompt.tasks.57", string.Join(", ", _refData!.Skills().Select(s => s.Name)), MaxStartDelayMinutes);

    // --- опыт: узлов шаблонов (ТЗ п. 2.11, todo32) и проекта (todo48, запись агентом — T-144) ---

    /// <summary>Область записи опыта: весь проект (блок «Опыт проекта» задания).</summary>
    private const string ScopeProject = "project";

    /// <summary>Область записи опыта: узел шаблона (блок «Опыт выполнения» задания).</summary>
    private const string ScopeTemplate = "template";

    /// <summary>Область записи опыта: ОБЩИЕ ПРАВИЛА РАБОТЫ организации (T-11-S0) —
    /// блок «Общие правила работы» задания. Такая запись не привязана ни к проекту,
    /// ни к узлу шаблона, и её получает каждая задача организации.</summary>
    private const string ScopeGeneral = "general";

    /// <summary>
    /// Новая запись опыта (create_experience): по умолчанию — узла шаблона текущей задачи,
    /// а у задачи вне шаблона — проекта. Явно область задаётся scope (project/template),
    /// узел шаблона — template (код узла либо его заголовок: код только что созданного
    /// маркером узла агент заранее не знает), навык записи — skill (код справочника;
    /// не указан — запись общая, её читают все исполнители).
    /// <para>ТЭГИ записи (T-24-S0) берутся из ТЕКУЩЕЙ ЗАДАЧИ и агентом не задаются: тема,
    /// по которой сделан вывод, — это тема работы, а не отдельное решение агента. Пометку
    /// «загружать всегда» агент не ставит вовсе: это решение человека.</para>
    /// </summary>
    private string CreateExperience(JsonElement args)
    {
        if (!CanExperience)
        {
            return Loc.In(Language, "prompt.tasks.58");
        }
        var text = RequiredString(args, "text", Language);
        if (!TryResolveSkill(args, out var skillId, out var skillError))
        {
            return skillError;
        }
        var node = OptionalString(args, "template")?.Trim() ?? "";
        var scope = (OptionalString(args, "scope") ?? "").Trim().ToLowerInvariant();
        if (scope.Length == 0)
        {
            scope = node.Length > 0 || _current.TemplateId is not null ? ScopeTemplate : ScopeProject;
        }
        if (scope is not (ScopeGeneral or ScopeProject or ScopeTemplate))
        {
            return Loc.In(Language, "prompt.tasks.61", scope, ScopeProject, ScopeTemplate, ScopeGeneral);
        }
        // ДЕДУПЛИКАЦИЯ ПРИ ЗАПИСИ (T-268-S0, §4.9 проекта опыта): сначала ищем похожую запись
        // ТОЙ ЖЕ ОБЛАСТИ. Сильное совпадение — новую не заводим вовсе и предлагаем поправить
        // найденную; слабое — заводим, но помечаем тэгом «похоже на …» для ревизии человеком.
        // Порог сильного совпадения СТРОГИЙ намеренно: сравнение без эмбеддингов чисто
        // лексическое, и ложный отказ завести запись хуже дубля
        var similar = _experience!.FindSimilar(text, scope,
            scope == ScopeGeneral ? null : _current.ProjectId);
        if (similar is { } found && found.Ratio >= ExperienceService.DuplicateRatio)
        {
            return Loc.In(Language, "prompt.tasks.159", found.Record.Id, found.Record.Text);
        }
        // общие правила работы (T-11-S0) ни к проекту, ни к шаблону не привязаны —
        // их пишет и задача, заведённая вне проекта
        if (scope == ScopeGeneral)
        {
            var generalRecord = _experience.CreateGeneral(text, _actorExecutorId, skillId,
                tags: _current.Tags);
            return MarkSimilar(generalRecord, similar,
                Loc.In(Language, "prompt.tasks.124", generalRecord.Id));
        }
        if (scope == ScopeProject)
        {
            if (_current.ProjectId is null)
            {
                return Loc.In(Language, "prompt.tasks.59");
            }
            var projectRecord = _experience.CreateForProject(_current.ProjectId, text,
                _actorExecutorId, skillId, tags: _current.Tags);
            return MarkSimilar(projectRecord, similar,
                Loc.In(Language, "prompt.tasks.60", projectRecord.Id));
        }
        var templateId = node.Length > 0 ? TemplateIdOf(node) : _current.TemplateId;
        if (templateId is null)
        {
            return node.Length > 0
                ? Loc.In(Language, "prompt.tasks.62", node)
                : Loc.In(Language, "prompt.tasks.63", ScopeProject);
        }
        var record = _experience.Create(templateId, text, _actorExecutorId, skillId,
            tags: _current.Tags);
        return MarkSimilar(record, similar,
            Loc.In(Language, "prompt.tasks.64", CodeOf(templateId), record.Id));
    }

    /// <summary>
    /// Пометить только что заведённую запись тэгом «похоже на …» (T-268-S0), если слабое
    /// совпадение нашлось. Пометка нужна ЧЕЛОВЕКУ: по ней он находит кандидатов на слияние
    /// в списке опыта. Тэгом, а не текстом, — обрубок чужой мысли внутри записи читался бы
    /// как часть урока.
    /// </summary>
    private string MarkSimilar(ExperienceRecord created, ExperienceService.SimilarFound? similar,
        string plainAnswer)
    {
        if (similar is null)
        {
            return plainAnswer;
        }
        var tag = ExperienceService.SimilarTag(similar.Record.Id);
        _experience!.Update(created.Id, created.Text, _actorExecutorId,
            tags: created.Tags.Append(tag).ToList());
        return Loc.In(Language, "prompt.tasks.160", created.Id, similar.Record.Id, tag);
    }

    /// <summary>
    /// ПОИСК ПО ЗАПИСЯМ ОПЫТА (search_experience, T-268-S0). До этой версии достать запись,
    /// не поместившуюся в задание, было нечем вовсе: у агента были только create_experience
    /// и update_experience, а подсказка про отброшенные записи предлагала «спроси человека».
    /// <para>Параметры: query — слова запроса; scope — область (project / template / general;
    /// «all» или пусто — все); limit — сколько строк (1..50, по умолчанию 10);
    /// includeInactive — искать И по неактивным записям. По умолчанию ищет ТОЛЬКО ПО
    /// АКТИВНЫМ: неактивную запись агент не получает ни при каком раскладе, и выдавать её
    /// по умолчанию значило бы вернуть погашенную запись в работу через чёрный ход.</para>
    /// <para>Поиск лексический: слова, а не смысл. Тэги ТЕКУЩЕЙ ЗАДАЧИ идут в ранг прибавкой
    /// за совпадение темы — агент их не задаёт.</para>
    /// </summary>
    private string SearchExperience(JsonElement args)
    {
        if (!CanSearchExperience)
        {
            return Loc.In(Language, "prompt.tasks.158");
        }
        var query = RequiredString(args, "query", Language).Trim();
        var scope = (OptionalString(args, "scope") ?? "").Trim().ToLowerInvariant();
        var limit = OptionalInt(args, "limit") ?? 10;
        var includeInactive = OptionalBool(args, "includeInactive") ?? false;
        var hits = _experience!.Search(query, scope is "all" ? null : scope, limit,
            includeInactive, _current.ProjectId, _current.Tags);
        if (hits.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.157", query);
        }
        return Loc.In(Language, "prompt.tasks.156", hits.Count, query) + "\n"
               + string.Join("\n", hits.Select(hit => ExperienceLine(hit.Record)));
    }

    /// <summary>Строка записи опыта в выдаче инструментов (поиск, перечисление, статистика):
    /// форма одна на все три — агент читает их подряд и сличает между собой.</summary>
    private string ExperienceLine(ExperienceRecord record)
    {
        var skill = record.SkillName.Length > 0 ? $"[{record.SkillName}] " : "";
        var tags = record.Tags.Count > 0 ? $"{{{string.Join(", ", record.Tags)}}} " : "";
        var off = record.IsActive ? "" : $"({Loc.In(Language, "prompt.tasks.161")}) ";
        return $"- (id: {record.Id}) [{ExperienceService.ScopeOf(record)}] {skill}{tags}{off}{record.Text}";
    }

    /// <summary>Правка записи опыта (update_experience); id — из блока «Опыт выполнения» или
    /// «Опыт проекта» промпта. Правятся только записи своего проекта и его шаблонов.
    /// <para>С T-271-S0 правятся не только текст и навык, но и ТЭГИ, пометка «загружать
    /// всегда» и АКТИВНОСТЬ — шаблону «Анализ опыта» надо расставлять тэги и навыки пачкой.
    /// НЕ ПЕРЕДАННОЕ ПОЛЕ НЕ МЕНЯЕТСЯ (хранилище различает «пусто» и «не передали»): иначе
    /// правка текста снимала бы пометки, которые поставил человек.</para></summary>
    private string UpdateExperience(JsonElement args)
    {
        if (!CanExperience)
        {
            return Loc.In(Language, "prompt.tasks.58");
        }
        var id = RequiredString(args, "id", Language).Trim();
        var text = RequiredString(args, "text", Language);
        var record = _experience!.Get(id);
        if (record is null || !IsMyExperience(record))
        {
            return Loc.In(Language, "prompt.tasks.65", id);
        }
        if (record.IsReadOnly)
        {
            return Loc.In(Language, "prompt.tasks.164", id);
        }
        if (!TryResolveSkill(args, out var skillId, out var skillError))
        {
            return skillError;
        }
        var changeSkill = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("skill", out _);
        _experience.Update(id, text, _actorExecutorId, skillId, changeSkill,
            alwaysLoad: OptionalBool(args, "alwaysLoad"),
            tags: StringList(args, "tags"),
            isActive: OptionalBool(args, "active"));
        return Loc.In(Language, "prompt.tasks.66", id);
    }

    /// <summary>
    /// ПОГАСИТЬ ИЛИ ОЖИВИТЬ ЗАПИСЬ ОПЫТА (set_experience_active, T-271-S0). Отдельным
    /// действием, а не правкой всей записи: меняется одно поле, и текст, тэги и навык не
    /// переписываются значениями давно прочитанного списка.
    /// <para>УДАЛЕНИЯ У АГЕНТА НЕТ И НЕ БУДЕТ: неактивную запись в задания не подставляют,
    /// а дальше её уносит правило архивации — то есть «погасить» и есть та операция,
    /// которой шаблон «Анализ опыта» убирает устаревшее.</para>
    /// <para>ПОСТАВЛЯЕМОЕ ПРАВИЛО ДИСТРИБУТИВА агент не гасит (фиксированные id
    /// <c>a1e5b5c0-…</c>): это правила, по которым он сам работает, и выключать их —
    /// решение человека. Человеку такая кнопка оставлена (T-265-S0).</para>
    /// </summary>
    private string SetExperienceActive(JsonElement args)
    {
        if (!CanExperience)
        {
            return Loc.In(Language, "prompt.tasks.58");
        }
        var id = RequiredString(args, "id", Language).Trim();
        var active = OptionalBool(args, "active")
                     ?? throw new ArgumentException(Loc.In(Language, "prompt.tasks.95", "active"));
        var record = _experience!.Get(id);
        if (record is null || !IsMyExperience(record))
        {
            return Loc.In(Language, "prompt.tasks.65", id);
        }
        if (ExperienceService.IsDistributionRule(id))
        {
            return Loc.In(Language, "prompt.tasks.163", id);
        }
        if (record.IsReadOnly)
        {
            return Loc.In(Language, "prompt.tasks.164", id);
        }
        _experience.SetActive(id, active, _actorExecutorId);
        return Loc.In(Language, active ? "prompt.tasks.165" : "prompt.tasks.166", id);
    }

    /// <summary>
    /// ПЕРЕЧИСЛИТЬ ЗАПИСИ ОБЛАСТИ (list_experience, T-271-S0) — чтобы разбирать опыт
    /// ПАЧКАМИ: поиск отдаёт то, что похоже на запрос, а анализу нужен весь корпус подряд,
    /// страницами по limit/offset. Область: project (по умолчанию), template (узел текущей
    /// задачи либо заданный параметром template), general — общие правила организации.
    /// <para>Отбор по навыку и тэгу — точным совпадением кода и тэга; includeInactive
    /// добавляет погашенные записи (по умолчанию их нет: анализ разбирает живой опыт,
    /// а погашенные уже разобраны).</para>
    /// </summary>
    private string ListExperience(JsonElement args)
    {
        if (!CanSearchExperience)
        {
            return Loc.In(Language, "prompt.tasks.158");
        }
        var scope = (OptionalString(args, "scope") ?? "").Trim().ToLowerInvariant();
        if (scope.Length == 0)
        {
            scope = _current.ProjectId is not null ? ScopeProject : ScopeGeneral;
        }
        if (scope is not (ScopeGeneral or ScopeProject or ScopeTemplate))
        {
            return Loc.In(Language, "prompt.tasks.61", scope, ScopeProject, ScopeTemplate, ScopeGeneral);
        }
        if (!TryScopeRecords(args, scope, out var all, out var error))
        {
            return error;
        }
        var skill = (OptionalString(args, "skill") ?? "").Trim();
        var tag = (OptionalString(args, "tag") ?? "").Trim();
        var includeInactive = OptionalBool(args, "includeInactive") ?? false;
        var picked = all
            .Where(r => includeInactive || r.IsActive)
            .Where(r => skill.Length == 0
                        || string.Equals(r.SkillName, skill, StringComparison.OrdinalIgnoreCase))
            .Where(r => tag.Length == 0
                        || r.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var offset = Math.Max(0, OptionalInt(args, "offset") ?? 0);
        var limit = Math.Clamp(OptionalInt(args, "limit") ?? 20, 1, 200);
        var page = picked.Skip(offset).Take(limit).ToList();
        if (page.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.167", scope, picked.Count);
        }
        return Loc.In(Language, "prompt.tasks.168", scope, page.Count,
                   offset + 1, offset + page.Count, picked.Count) + "\n"
               + string.Join("\n", page.Select(ExperienceLine));
    }

    /// <summary>
    /// СТАТИСТИКА ИСПОЛЬЗОВАНИЯ (experience_usage, T-271-S0): сколько ЗАДАЧ получило запись
    /// в тексте задания и когда это было в последний раз (след ведёт T-266-S0). Спрашивается
    /// либо по одной записи (id), либо по целой области (scope) — тогда строки идут от самых
    /// невостребованных к самым ходовым: первым же экраном видно, что пора гасить.
    /// </summary>
    private string ExperienceUsage(JsonElement args)
    {
        if (!CanSearchExperience)
        {
            return Loc.In(Language, "prompt.tasks.158");
        }
        var usage = _experience!.UsageMap();
        if (OptionalString(args, "id")?.Trim() is { Length: > 0 } id)
        {
            var one = _experience.Get(id);
            return one is null
                ? Loc.In(Language, "prompt.tasks.65", id)
                : UsageLine(one, usage);
        }
        var scope = (OptionalString(args, "scope") ?? "").Trim().ToLowerInvariant();
        if (scope.Length == 0)
        {
            scope = _current.ProjectId is not null ? ScopeProject : ScopeGeneral;
        }
        if (scope is not (ScopeGeneral or ScopeProject or ScopeTemplate))
        {
            return Loc.In(Language, "prompt.tasks.61", scope, ScopeProject, ScopeTemplate, ScopeGeneral);
        }
        if (!TryScopeRecords(args, scope, out var all, out var error))
        {
            return error;
        }
        var offset = Math.Max(0, OptionalInt(args, "offset") ?? 0);
        var limit = Math.Clamp(OptionalInt(args, "limit") ?? 50, 1, 200);
        var ordered = all
            .OrderBy(r => usage.TryGetValue(r.Id, out var u) ? u.Count : 0)
            .ThenBy(r => usage.TryGetValue(r.Id, out var u) ? u.LastUsedAt ?? DateTime.MinValue : DateTime.MinValue)
            .ToList();
        var never = all.Count(r => !usage.ContainsKey(r.Id));
        var page = ordered.Skip(offset).Take(limit).ToList();
        if (page.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.167", scope, all.Count);
        }
        return Loc.In(Language, "prompt.tasks.169", scope, all.Count, never) + "\n"
               + string.Join("\n", page.Select(r => UsageLine(r, usage)));
    }

    /// <summary>Строка статистики: сколько задач получило запись и когда в последний раз.</summary>
    private string UsageLine(ExperienceRecord record,
        IReadOnlyDictionary<string, ExperienceUsageStat> usage)
    {
        var stat = usage.TryGetValue(record.Id, out var found) ? found : null;
        var last = stat?.LastUsedAt is { } at
            ? at.ToLocalTime().ToString("yyyy-MM-dd")
            : Loc.In(Language, "prompt.tasks.170");
        return Loc.In(Language, "prompt.tasks.171", record.Id, stat?.Count ?? 0, last,
            record.IsActive ? "" : " (" + Loc.In(Language, "prompt.tasks.161") + ")",
            Preview(record.Text));
    }

    /// <summary>Записи выбранной области целиком — общая часть перечисления и статистики.
    /// Область «template» берёт узел параметра <c>template</c>, а без него — узел текущей
    /// задачи; поддерево узла НЕ берётся (как и при отборе опыта в задание).</summary>
    private bool TryScopeRecords(JsonElement args, string scope,
        out List<ExperienceRecord> records, out string error)
    {
        records = [];
        error = "";
        if (scope == ScopeGeneral)
        {
            records = _experience!.ListGeneral();
            return true;
        }
        if (scope == ScopeProject)
        {
            if (_current.ProjectId is null)
            {
                error = Loc.In(Language, "prompt.tasks.59");
                return false;
            }
            records = _experience!.ListByProject(_current.ProjectId);
            return true;
        }
        var node = OptionalString(args, "template")?.Trim() ?? "";
        var templateId = node.Length > 0 ? TemplateIdOf(node) : _current.TemplateId;
        if (templateId is null)
        {
            error = node.Length > 0
                ? Loc.In(Language, "prompt.tasks.62", node)
                : Loc.In(Language, "prompt.tasks.63", ScopeProject);
            return false;
        }
        records = _experience!.ListByTemplate(templateId);
        return true;
    }

    /// <summary>
    /// ПЕРЕНЕСТИ ЗАПИСЬ ОПЫТА В ДРУГУЮ ОБЛАСТЬ (move_experience, T-269-S0). Области три и
    /// разграничены они так: ОБЩЕЕ правило — про то, как мы работаем (верно в любом проекте
    /// организации), опыт ПРОЕКТА — про этот продукт, опыт УЗЛА ШАБЛОНА — про этот шаг
    /// процесса. До этой версии исправить ошибку области было нечем: запись оставалось
    /// удалить и завести заново, потеряв id, историю и авторство.
    /// <para>Параметры: id — запись (из блоков опыта задания либо из выдачи
    /// search_experience); scope — куда (project / template / general); template — код или
    /// заголовок узла шаблона для scope=template (не указан — узел текущей задачи).
    /// Проект-получатель агент не выбирает: это ВСЕГДА проект текущей задачи — переносить
    /// записи в чужие проекты агент не должен.</para>
    /// <para>Перенести чужую (созданную другим сервером) и поставляемую запись нельзя,
    /// а текст с признаками проектного (код задачи, путь файла, имя проекта) не примут
    /// в общие правила — подтверждения у агента нет, это решение человека.</para>
    /// </summary>
    private string MoveExperience(JsonElement args)
    {
        if (!CanExperience)
        {
            return Loc.In(Language, "prompt.tasks.58");
        }
        var id = RequiredString(args, "id", Language).Trim();
        var scope = RequiredString(args, "scope", Language).Trim().ToLowerInvariant();
        if (scope is not (ScopeGeneral or ScopeProject or ScopeTemplate))
        {
            return Loc.In(Language, "prompt.tasks.61", scope, ScopeProject, ScopeTemplate, ScopeGeneral);
        }
        var record = _experience!.Get(id);
        if (record is null || !IsMyExperience(record))
        {
            return Loc.In(Language, "prompt.tasks.65", id);
        }
        string? projectId = null;
        string? templateId = null;
        if (scope == ScopeProject)
        {
            if (_current.ProjectId is null)
            {
                return Loc.In(Language, "prompt.tasks.59");
            }
            projectId = _current.ProjectId;
        }
        if (scope == ScopeTemplate)
        {
            var node = OptionalString(args, "template")?.Trim() ?? "";
            templateId = node.Length > 0 ? TemplateIdOf(node) : _current.TemplateId;
            if (templateId is null)
            {
                return node.Length > 0
                    ? Loc.In(Language, "prompt.tasks.62", node)
                    : Loc.In(Language, "prompt.tasks.63", ScopeProject);
            }
        }
        _experience.Move(id, scope, projectId, templateId, _actorExecutorId);
        return Loc.In(Language, "prompt.tasks.162", id, scope);
    }

    /// <summary>Запись опыта относится к текущему проекту (опыт проекта) или к узлу шаблона
    /// этого же проекта — чужие записи агент не правит (ТЗ гл. 12).</summary>
    private bool IsMyExperience(ExperienceRecord record)
    {
        // общие правила работы (T-11-S0) агент читает в задании и правит по id оттуда же:
        // они принадлежат организации целиком, «чужими» для задачи быть не могут
        if (record.IsGeneral)
        {
            return true;
        }
        if (record.IsProjectLevel)
        {
            return record.ProjectId == _current.ProjectId;
        }
        if (record.TemplateTaskId == _current.TemplateId)
        {
            return true;
        }
        var node = _tasks.Get(record.TemplateTaskId);
        return node is { IsTemplate: true, DeletedAt: null } && node.ProjectId == _current.ProjectId;
    }

    /// <summary>Навык записи опыта из аргументов (код справочника); не задан — null (общая
    /// запись). Неизвестный код — понятная ошибка со списком доступных.</summary>
    private bool TryResolveSkill(JsonElement args, out string? skillId, out string error)
    {
        skillId = null;
        error = "";
        var skill = OptionalString(args, "skill")?.Trim() ?? "";
        if (skill.Length == 0)
        {
            return true;
        }
        if (_refData is null)
        {
            error = Loc.In(Language, "prompt.tasks.67");
            return false;
        }
        var catalog = _refData.Skills();
        var found = catalog.FirstOrDefault(s => s.Name.Equals(skill, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            error = Loc.In(Language, "prompt.tasks.51", skill) +
                    string.Join(", ", catalog.Select(s => s.Name));
            return false;
        }
        skillId = found.Id;
        return true;
    }

    // --- шаблоны проекта (ТЗ п. 2.4; правка агентом — T-144) ---

    /// <summary>Узлы шаблонов проекта текущей задачи (у задачи вне проекта — шаблоны
    /// вне проектов): область работы агента ограничена его проектом, как и у задач.</summary>
    private List<TaskItem> ProjectTemplates() =>
        _current.ProjectId is null
            ? _tasks.List(templatesOnly: true).Where(t => t.ProjectId is null).ToList()
            : _tasks.List(_current.ProjectId, templatesOnly: true);

    /// <summary>Узел шаблона по коду задания либо по заголовку; null — не найден.
    /// Заголовок нужен CLI-агенту: код узла, созданного маркером в этом же ответе,
    /// он заранее знать не может (T-144).</summary>
    private string? TemplateIdOf(string codeOrTitle)
    {
        var templates = ProjectTemplates();
        var found = templates.FirstOrDefault(t =>
                        t.DisplayId.Equals(codeOrTitle, StringComparison.OrdinalIgnoreCase))
                    ?? templates.FirstOrDefault(t =>
                        t.Title.Equals(codeOrTitle, StringComparison.CurrentCultureIgnoreCase));
        return found?.Id;
    }

    /// <summary>list_templates (T-144): узлы шаблонов проекта — код, заголовок, родитель
    /// и число записей опыта. С этого начинается любая работа с шаблонами: коды узлов
    /// агенту иначе взять неоткуда (в файлах проекта шаблонов нет).</summary>
    private string ListTemplates()
    {
        if (!CanTemplates)
        {
            return Loc.In(Language, "prompt.tasks.68");
        }
        var templates = ProjectTemplates();
        if (templates.Count == 0)
        {
            return Loc.In(Language, "prompt.tasks.69");
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.70", templates.Count));
        foreach (var node in templates)
        {
            var parent = node.ParentId is null ? "" : Loc.In(Language, "prompt.tasks.36", CodeOf(node.ParentId));
            var records = _experience?.ListByTemplate(node.Id).Count ?? 0;
            sb.AppendLine(Loc.In(Language, "prompt.tasks.71", node.DisplayId, node.Title, parent, records));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>create_template (T-144): новый узел шаблона проекта. parent — код (или
    /// заголовок) узла-родителя; без него создаётся корневой шаблон. Шаблоны создаются
    /// только на дирижёре организации (ТЗ гл. 6) — на прочих серверах хранилище откажет.</summary>
    private string CreateTemplate(JsonElement args)
    {
        if (!CanTemplates)
        {
            return Loc.In(Language, "prompt.tasks.68");
        }
        var title = RequiredString(args, "title", Language).Trim();
        var description = RequiredString(args, "description", Language);
        var acceptance = OptionalString(args, "acceptance") ?? "";
        var parentCode = OptionalString(args, "parent")?.Trim() ?? "";
        string? parentId = null;
        if (parentCode.Length > 0)
        {
            parentId = TemplateIdOf(parentCode);
            if (parentId is null)
            {
                return Loc.In(Language, "prompt.tasks.72", parentCode);
            }
        }
        if (!TryResolveSkills(args, out var skillIds, out var skillError))
        {
            return skillError;
        }
        var node = _tasks.Create(new TaskItem
        {
            ProjectId = _current.ProjectId,
            TeamId = _current.TeamId,
            ParentId = parentId,
            Kind = TaskKind.Task,
            Title = title,
            Status = TaskStatuses.Draft,
            IsTemplate = true,
            SkillIds = skillIds,
        }, description, acceptance, _actorExecutorId);
        return Loc.In(Language, "prompt.tasks.73", node.DisplayId, node.Title)
               + (parentId is null ? Loc.In(Language, "prompt.tasks.74") : Loc.In(Language, "prompt.tasks.75", CodeOf(parentId)));
    }

    /// <summary>update_template (T-144): правка узла шаблона по коду (или заголовку) —
    /// заголовок, формулировка, критерии приёмки. Не переданное поле не меняется.</summary>
    private string UpdateTemplate(JsonElement args)
    {
        if (!CanTemplates)
        {
            return Loc.In(Language, "prompt.tasks.68");
        }
        var code = RequiredString(args, "code", Language).Trim();
        var id = TemplateIdOf(code);
        var node = id is null ? null : _tasks.Get(id);
        if (node is null)
        {
            return Loc.In(Language, "prompt.tasks.72", code);
        }
        if (OptionalString(args, "title")?.Trim() is { Length: > 0 } title)
        {
            node.Title = title;
        }
        // пустые критерии приёмки хранилище понимает как «оставить прежние»,
        // а формулировку пишет всегда — не переданную подставляем текущую
        var description = OptionalString(args, "description") ?? _tasks.ReadDescription(node);
        var updated = _tasks.Update(node, description, OptionalString(args, "acceptance") ?? "",
            _actorExecutorId);
        return Loc.In(Language, "prompt.tasks.76", updated.DisplayId, updated.Title);
    }

    /// <summary>
    /// Дописка к системному промпту про опыт и шаблоны (ТЗ п. 2.11; T-144). Опыт узла шаблона
    /// агент обязан поправить сам, если ход работы отклонился от записей; опыт ПРОЕКТА и
    /// шаблоны он трогает только по прямой просьбе задания — это общая память проекта,
    /// а не заметки одного исполнителя. Пусто — писать некуда, инструменты не опубликованы.
    /// </summary>
    public string ExperienceNote
    {
        get
        {
            var sb = new StringBuilder();
            if (CanExperience && _current.TemplateId is not null)
            {
                sb.Append(Loc.In(Language, "prompt.tasks.77"));
            }
            if (CanExperience && _current.ProjectId is not null)
            {
                sb.Append(Loc.In(Language, "prompt.tasks.78"));
            }
            // общие правила работы (T-11-S0): их получает каждая задача, поэтому дописка
            // идёт всюду, где опыт вообще доступен
            if (CanExperience)
            {
                sb.Append(Loc.In(Language, "prompt.tasks.122"));
            }
            if (CanTemplates)
            {
                sb.Append(Loc.In(Language, "prompt.tasks.79"));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// То же про опыт, но для агента БЕЗ инструментов AI2P (CLI-агент, T-144): позвать
    /// create_experience / update_experience он не может, поэтому запись делается МАРКЕРОМ
    /// в ответе — система выполняет её после работы агента (как подзадачи, T-125).
    /// До T-144 у CLI-агента канала к опыту не было вовсе: на просьбу «заполни опыт проекта»
    /// он переносил выводы в файлы репозитория и честно писал, что поля опыта ему недоступны.
    /// </summary>
    public string ExperienceMarkerNote(string marker) =>
        !CanExperience
            ? ""
            : Loc.In(Language, "prompt.tasks.80") +
              marker + Loc.In(Language, "prompt.tasks.81") + ScopeProject + Loc.In(Language, "prompt.tasks.82") + ScopeTemplate + Loc.In(Language, "prompt.tasks.83") + ScopeGeneral + Loc.In(Language, "prompt.tasks.123");

    /// <summary>
    /// То же про шаблоны для агента БЕЗ инструментов AI2P (CLI-агент, T-144). Маркеры шаблонов
    /// выполняются ПЕРЕД маркерами опыта, поэтому опыт только что созданного узла привязывается
    /// к нему по заголовку — кода узла агент заранее не знает.
    /// </summary>
    /// <param name="marker">Маркер шаблонов в ответе агента.</param>
    /// <param name="readMarker">Маркер чтения заданий (T-127): им же запрашивается список
    /// шаблонов проекта — ответ приходит следующим сообщением той же сессии.</param>
    public string TemplateMarkerNote(string marker, string readMarker) =>
        !CanTemplates
            ? ""
            : Loc.In(Language, "prompt.tasks.84") +
              marker + Loc.In(Language, "prompt.tasks.85", readMarker);

    private string Nick(string executorId) => _executors.Get(executorId)?.Nick ?? executorId;

    private static string ClipMessage(string text) =>
        text.Length <= 2000 ? text : text[..2000] + "…";

    /// <summary>Задачи того же проекта, что и текущая (включая процессы; шаблоны исключены).</summary>
    /// <param name="includeTemplates">Показывать и узлы шаблонов — нужно, когда текущая задача
    /// сама шаблонная (её соседи тоже шаблонные, T-132).</param>
    private IEnumerable<TaskItem> ProjectTasks(bool includeTemplates = false) =>
        _current.ProjectId is null
            ? _tasks.List(includeTemplates: includeTemplates).Where(t => t.ProjectId is null)
            : _tasks.List(_current.ProjectId, includeTemplates);

    /// <summary>
    /// Код задания из карточки <see cref="Render"/>; null — текст не карточка
    /// (например «задача не найдена»). Первая строка карточки — заголовок Markdown вида
    /// «# Задание T-15: заголовок»; с T-190 он переводится (в en это «# Task T-15: …»),
    /// поэтому код берётся по ФОРМЕ строки — последнее слово до двоеточия, — а не по
    /// русскому префиксу: иначе у команды с agent_language=en чат к прочитанной карточке
    /// перестал бы прикладываться (маркер чтения задания у CLI-агента, T-127).
    /// </summary>
    public static string? CardCode(string card)
    {
        var firstLine = card.Split('\n', 2)[0].Trim();
        if (!firstLine.StartsWith("# ", StringComparison.Ordinal))
        {
            return null;
        }
        var rest = firstLine[2..];
        var colon = rest.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }
        var head = rest[..colon].Trim();
        var space = head.LastIndexOf(' ');
        var code = (space < 0 ? head : head[(space + 1)..]).Trim();
        return code.Length == 0 ? null : code;
    }

    /// <summary>Карточка задания для агента: формулировка, критерии, статус, артефакты.</summary>
    /// <param name="chatHint">Дописать подсказку «читай чат инструментом get_task_chat»;
    /// false — чат подставляется рядом целиком (агенту без инструментов, T-120).</param>
    private string Render(TaskItem task, bool chatHint = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.86", task.DisplayId, task.Title));
        sb.AppendLine((task.IsTemplate ? Loc.In(Language, "prompt.tasks.87") : "") + Loc.In(Language, "prompt.tasks.88", task.Status)
                      + (task.ParentId is null ? "" : Loc.In(Language, "prompt.tasks.89", CodeOf(task.ParentId))));
        sb.AppendLine();
        sb.AppendLine(Loc.In(Language, "prompt.tasks.90"));
        sb.AppendLine(Clip(_tasks.ReadDescription(task), Language));
        var acceptance = _tasks.ReadAcceptance(task);
        if (acceptance.Trim().Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.91"));
            sb.AppendLine(Clip(acceptance, Language));
        }
        var artifacts = _tasks.Artifacts(task);
        if (artifacts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.92"));
            foreach (var rel in artifacts)
            {
                sb.AppendLine();
                sb.AppendLine($"### {Path.GetFileName(rel)}");
                sb.AppendLine(Clip(_files.ReadText(rel), Language));
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.27"));
        }
        // подсказка про чат — обсуждение задания читается отдельным инструментом
        var chatCount = chatHint ? _chat.ListByTask(task.Id).Count : 0;
        if (chatCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(Language, "prompt.tasks.93", chatCount, task.DisplayId));
        }
        return sb.ToString().TrimEnd();
    }

    private string CodeOf(string taskId) => _tasks.Get(taskId)?.DisplayId ?? taskId;

    private static string Clip(string text, string? language) => ClipTo(text, MaxTextChars, language);

    private static string ClipTo(string text, int max, string? language) =>
        text.Length <= max ? text : text[..max] + Loc.In(language, "prompt.tasks.94", max);

    private static string RequiredString(JsonElement args, string name, string? language) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim().Length > 0
            ? v.GetString()!
            : throw new ArgumentException(Loc.In(language, "prompt.tasks.95", name));

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Необязательное число из аргументов (T-268-S0): не передано или не число — null.</summary>
    private static int? OptionalInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var value)
            ? value
            : null;

    /// <summary>Необязательный флаг из аргументов (T-268-S0): не передан — null.</summary>
    private static bool? OptionalBool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static string Preview(string text)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length > 120 ? firstLine[..120] + "…" : firstLine;
    }
}
