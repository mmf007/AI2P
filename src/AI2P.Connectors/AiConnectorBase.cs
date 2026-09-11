using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Общий жизненный цикл ИИ-коннекторов (ТЗ п. 7.2, этап 2): задание → running → фоновый вызов
/// провайдера → done (результат-артефакт, задача review) / failed (артефакт с ошибкой, задача error).
/// Отдельный исход — исчерпанный лимит провайдера (T-121): задание тоже failed, но задача не
/// «встаёт с ошибкой», а ЖДЁТ сброса — «ожидает» с отложенным стартом (DeferAfterLimit).
/// Вопросы агента (ТЗ v1.17, todo20): инструмент ask_question — агент задаёт вопрос человеку,
/// контекст диалога сохраняется файлом, job → waiting_human, задача → waiting_reply; ответ
/// человека восстанавливает контекст и продолжает работу (переживает перезапуск приложения).
/// Полные запрос/ответ — файлами в tasks/&lt;id&gt;/ai/ (п. 6.3), стоимость — по декларации (п. 7.3).
/// Наследник реализует только сам вызов провайдера (CallAsync) и пробу подключения (ProbeAsync).
/// </summary>
public abstract class AiConnectorBase : IAgentConnector
{
    /// <summary>Системный промпт исполнителя; при наличии файловых инструментов и права
    /// задавать вопросы (ask_question, ТЗ v1.17) — с их описанием; инструменты заданий
    /// (todo23) описываются всегда.</summary>
    /// <param name="language">Язык промпта — язык общения агентов команды задачи
    /// (<c>Team.AgentLanguage</c>, T-190), а не язык установки; null — язык установки.</param>
    protected static string SystemPrompt(string? language, bool hasFileTools, bool canAskQuestions)
    {
        var prompt =
            Loc.In(language, "prompt.agent.1");
        prompt += canAskQuestions
            ? Loc.In(language, "prompt.agent.2")
            : Loc.In(language, "prompt.agent.3");
        prompt +=
            Loc.In(language, "prompt.agent.4");
        if (hasFileTools)
        {
            prompt +=
                Loc.In(language, "prompt.agent.5");
        }
        return prompt;
    }

    /// <summary>Инструмент «задать вопрос человеку» (ТЗ v1.17): публикуется агенту как обычный
    /// tool. Текста здесь НЕТ (todo24): промпт — из справочника действий (AI2P.Chat.AskQuestion).</summary>
    protected static readonly FileToolset.ToolSpec AskQuestionSpec = new("ask_question", "",
        """
        {
          "type": "object",
          "properties": {
            "question": { "type": "string" },
            "options":  { "type": "array", "items": { "type": "string" } }
          },
          "required": ["question"]
        }
        """);

    /// <summary>Результат вызова провайдера: текст + токены для учёта стоимости.
    /// LimitPercent — текущий % использования лимита провайдера, если тот его прислал
    /// в заголовках ответа (ТЗ v1.37); null — провайдер не сообщает.</summary>
    /// <param name="ContextJson">Контекст, которым работу агента можно ПРОДОЛЖИТЬ, не начиная
    /// заново (T-185): у Claude CLI это id живой сессии. Нужен, когда задача уходит ждать своих
    /// подзадач: агент не закрывается, а висит — и финальный анализ делает та же сессия, помня
    /// всё, что уже выяснила. null — коннектор так не умеет (у API-агентов диалог существует
    /// только внутри вызова), и тогда финальный анализ будет отдельным заданием, как раньше.</param>
    protected sealed record AiCallResult(
        string Text, long InputTokens, long OutputTokens, string Model, string FinishReason,
        double? LimitPercent = null, string? ContextJson = null);

    /// <summary>Вопрос агента (ТЗ v1.17): текст, варианты и сериализованный контекст диалога
    /// для продолжения после ответа человека. IsConfirmation — запрос подтверждения правила
    /// безопасности (ТЗ гл. 12, todo25): при ответе «да» отложенный инструмент выполняется.</summary>
    /// <param name="ToExecutorId">Адресат вопроса (T-185): исполнитель РОДСТВЕННОЙ задачи,
    /// когда агент ждёт ответа не человека, а другого агента (send_task_message с wait=true).
    /// null — обычный вопрос человеку.</param>
    protected sealed record AgentQuestion(
        string Text, List<string> Options, string ContextJson,
        long InputTokens, long OutputTokens, string Model, bool IsConfirmation = false,
        string? ToExecutorId = null);

    /// <summary>Ответ человека — согласие? (подтверждение правила безопасности, todo25).</summary>
    protected static bool IsApproval(string answer)
    {
        var normalized = answer.Trim().ToLowerInvariant().TrimEnd('.', '!');
        return normalized is "да" or "yes" or "y" or "ok" or "ок" or "разрешаю" or "разрешить" or "allow"
               || normalized.StartsWith("да,") || normalized.StartsWith("да ")
               || normalized.StartsWith("разреш");
    }

    /// <summary>Исход вызова: либо финальный результат, либо вопрос человеку (ровно одно из двух).</summary>
    protected sealed record AiCallOutcome(AiCallResult? Result, AgentQuestion? Question)
    {
        public static AiCallOutcome Of(AiCallResult result) => new(result, null);
        public static AiCallOutcome Of(AgentQuestion question) => new(null, question);
    }

    /// <summary>Продолжение после ответа человека: сохранённый контекст + текст ответа (ТЗ v1.17).
    /// AfterLimit — продолжение не после ответа, а после сброса лимита (T-166): отвечать
    /// нечего, агенту уходит «Continue» и он доделывает начатое в той же сессии.</summary>
    protected sealed record AgentResume(string ContextJson, string AnswerText, bool AfterLimit = false);

    private static readonly JsonSerializerOptions DumpOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JobService _jobs;
    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly FileStore _files;
    private readonly EventStore _events;
    private readonly SecretStore _secrets;
    private readonly ChatService _chat;
    private readonly ActionCatalogService _actions;
    private readonly SecurityRuleService _security;
    private readonly RefDataService _refData;
    private readonly ExecutorPickService _picker;
    private readonly ExperienceService _experience;
    private readonly JobConsole _console;
    private readonly LocalModelProcessService? _localModels;
    private readonly TeamService _teams;
    private readonly Func<string> _language;
    private readonly string _publicBaseUrl;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();

    /// <param name="actions">Справочник действий (ТЗ v1.21, todo24): промпты инструментов.</param>
    /// <param name="security">Правила безопасности (ТЗ гл. 12, todo25): проверка до выполнения.</param>
    /// <param name="refData">Справочник skills — для create_task (ТЗ v1.26, todo28).</param>
    /// <param name="picker">Автоподбор исполнителя подзадач create_task (ТЗ v1.26, todo28).</param>
    /// <param name="experience">Опыт по шаблонам (ТЗ п. 2.11, todo32): инструменты агента.</param>
    /// <param name="console">Консоль задания (ТЗ v1.45, todo37_3): живой ход работы для UI.</param>
    /// <param name="localModels">Локальные серверы моделей (ТЗ п. 2.9): их вывод тоже идёт
    /// в консоль задания, пока модель считает (todo37_3); null — вывод сервера не показывается.</param>
    /// <param name="teams">Команды (ТЗ п. 2.8): из команды задачи берётся язык ОБЩЕНИЯ
    /// с агентом (<c>Team.AgentLanguage</c>, T-190) — на нём собираются все промпты.</param>
    /// <param name="language">Текущий язык приложения (меняется из настроек «на лету»);
    /// им подписаны сообщения человеку и промпты задач без команды.</param>
    protected AiConnectorBase(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, SecretStore secrets,
        ChatService chat, ActionCatalogService actions, SecurityRuleService security,
        RefDataService refData, ExecutorPickService picker, ExperienceService experience,
        JobConsole console, LocalModelProcessService? localModels, TeamService teams,
        Func<string> language, string publicBaseUrl)
    {
        _jobs = jobs;
        _tasks = tasks;
        _executors = executors;
        _projects = projects;
        _files = files;
        _events = events;
        _secrets = secrets;
        _chat = chat;
        _actions = actions;
        _security = security;
        _refData = refData;
        _picker = picker;
        _experience = experience;
        _console = console;
        _localModels = localModels;
        _teams = teams;
        _language = language;
        _publicBaseUrl = publicBaseUrl;
    }

    /// <summary>
    /// Язык ОБЩЕНИЯ с агентом по этой задаче (T-190): язык команды задачи
    /// (<c>Team.AgentLanguage</c>, ТЗ п. 2.8) — на нём собираются системный промпт, дописки
    /// про маркеры, описания и ответы инструментов. Команды у задачи нет (или её удалили) —
    /// null, и тексты берутся на языке установки.
    /// </summary>
    private string? AgentLanguageOf(TaskItem task) =>
        task.TeamId is null ? null : _teams.Get(task.TeamId)?.AgentLanguage;

    public abstract string Kind { get; }

    /// <summary>Поддерживает ли коннектор вопросы агента (инструмент ask_question, ТЗ v1.17).</summary>
    protected virtual bool SupportsQuestions => false;

    /// <summary>Системный промпт запроса — для дампа request.json и наследников.
    /// CLI-коннектор (todo29) переопределяет: инструменты AI2P там не публикуются.</summary>
    protected virtual string RequestSystemPrompt(AgentToolset tools) =>
        SystemPrompt(tools.Language, tools.HasFileTools, SupportsQuestions)
        + tools.SplitNote + tools.ExperienceNote + tools.TalkNote() + tools.SecurityNote;

    /// <summary>Технический лог коннектора (Serilog, ТЗ п. 6.3): подробная диагностика этапа 2.</summary>
    protected ILogger Logger => Log.ForContext("SourceContext", GetType().Name);

    /// <summary>
    /// Вызов провайдера; отказ модели (refusal и т.п.) — исключение с пояснением.
    /// tools != null — агенту публикуются файловые инструменты (function calling, ТЗ п. 2.4):
    /// наследник выполняет цикл tool call → tool result до финального ответа.
    /// resume != null — продолжение после ответа человека на вопрос агента (ТЗ v1.17):
    /// диалог восстанавливается из resume.ContextJson, ответ подставляется как tool result.
    /// </summary>
    protected abstract Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
        string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct);

    /// <summary>Проба подключения (ключ действует, модель существует): null — успех, иначе текст ошибки.</summary>
    protected abstract Task<string?> ProbeAsync(ModelProfile profile, string apiKey, CancellationToken ct);

    /// <summary>
    /// Токен вызова провайдера с таймаутом ожидания ответа (T-124): значение — из формы
    /// исполнителя (<see cref="AgentToolset.CallTimeout"/>), не задано (0 в форме) — ждём
    /// столько, сколько модель считает, и обрывает только кнопка «остановить».
    /// </summary>
    protected static CancellationTokenSource CallTimeoutCts(AgentToolset tools, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (tools.CallTimeout is { } limit)
        {
            cts.CancelAfter(limit);
        }
        return cts;
    }

    /// <summary>
    /// Сообщения ЛЮДЕЙ, написанные в чат задачи пока агент работал (T-161) — текстом для
    /// подстановки в диалог; пусто — новых сообщений нет либо за чатом не следим. Каждое
    /// сообщение отдаётся ровно один раз. Агент отвечает инструментом send_chat_message —
    /// у CLI-агента инструментов нет, у него свой текст ответа (маркер).
    /// </summary>
    /// <param name="interrupted">Работа была прервана на середине (CLI-коннектор).</param>
    /// <param name="replyHow">Чем агенту отвечать; null — инструментом send_chat_message.</param>
    protected static string ChatDelivery(AgentToolset tools, bool interrupted = false,
        string? replyHow = null)
    {
        if (tools.Chat is not { } watch)
        {
            return "";
        }
        var fresh = watch.Take();
        if (fresh.Count == 0)
        {
            return "";
        }
        tools.Trace?.Invoke(Loc.T("msg.aiConnectorBase.1", fresh.Count));
        return watch.Block(fresh,
            replyHow ?? Loc.In(tools.Language, "prompt.agent.6"), interrupted, tools.Language);
    }

    public Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default)
    {
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        StartRun(job, requestText, resume: null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Продолжить задание после ответа человека на вопрос агента (ТЗ v1.17): контекст диалога
    /// читается из файла tasks/&lt;id&gt;/ai/&lt;J-N&gt;-context.json — ожидание переживает перезапуск.
    /// </summary>
    public Task ResumeJobAsync(Job job, string answerText, CancellationToken ct = default)
    {
        var task = _tasks.Get(job.TaskId) ?? throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.2"));
        var slug = task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.Slug;
        var contextJson = _files.ReadText(ContextRel(slug, task.DisplayId, job.DisplayId));
        if (contextJson.Trim().Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.aiConnectorBase.3"));
        }
        Logger.Information("Задание {JobDisplayId}: ответ человека получен, продолжаю работу агента",
            job.DisplayId);
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        _tasks.ChangeStatus(job.TaskId, TaskStatuses.InProgress, actorId: null);
        StartRun(job, requestText: "", new AgentResume(contextJson, answerText));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Продолжить задание, оборванное лимитом подписки, когда лимит отпустил (T-166): связь
    /// с агентом не рвалась — у Claude CLI сохранён id сессии, и она оживает тем же способом,
    /// что и после ответа человека (`--resume &lt;id&gt;`), только вместо ответа агенту уходит
    /// «Continue». Контекст лежит там же, в tasks/&lt;id&gt;/ai/&lt;J-N&gt;-context.json, поэтому
    /// ожидание переживает и перезапуск приложения, и выключение компьютера.
    /// </summary>
    /// <param name="requestText">Промпт задания на случай, если сессии агента больше нет
    /// (CLI её не нашёл): тогда работа начнётся заново, а не встанет ошибкой.</param>
    public Task ContinueAfterLimitAsync(Job job, string requestText = "", CancellationToken ct = default)
    {
        var task = _tasks.Get(job.TaskId) ?? throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.2"));
        var slug = task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.Slug;
        var contextJson = _files.ReadText(ContextRel(slug, task.DisplayId, job.DisplayId));
        if (contextJson.Trim().Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.aiConnectorBase.4"));
        }
        Logger.Information("Задание {JobDisplayId}: лимит отпустил — продолжаю ту же сессию агента (T-166)",
            job.DisplayId);
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        _tasks.ChangeStatus(job.TaskId, TaskStatuses.InProgress, actorId: null);
        _console.Write(job.Id, Loc.T("msg.aiConnectorBase.5"));
        StartRun(job, requestText, new AgentResume(contextJson, "", AfterLimit: true));
        return Task.CompletedTask;
    }

    private void StartRun(Job job, string requestText, AgentResume? resume)
    {
        var cts = new CancellationTokenSource();
        _active[job.Id] = cts;
        // фоновое выполнение: результат придёт асинхронно (ТЗ п. 3, принцип 4)
        _ = Task.Run(() => RunAsync(job, requestText, resume, cts.Token), CancellationToken.None);
    }

    /// <summary>Есть ли по заданию живой фоновый вызов в ЭТОМ процессе (T-117): running-задание
    /// без живого вызова — зависшее (перезапуск приложения, старый обрыв) — его можно
    /// «толкнуть» повторной подачей (JobOrchestrator.ContinueJobAsync).</summary>
    public bool HasActiveRun(string jobId) => _active.ContainsKey(jobId);

    public Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        var wasActive = _active.TryRemove(jobId, out var cts);
        if (wasActive)
        {
            cts!.Cancel();
            cts.Dispose();
        }
        Logger.Information("Задание {JobId} остановлено (активный вызов: {WasActive})", jobId, wasActive);
        // висящие вопросы отменённого задания закрываются (ТЗ v1.17)
        _chat.ClosePendingByJob(jobId);
        _jobs.SetState(jobId, JobState.Cancelled, actorId: null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Проверка подключения (кнопка «запустить работу команды», ТЗ v1.14):
    /// профайл + ключ + проба провайдера. null — успех, иначе текст ошибки.
    /// </summary>
    public async Task<string?> TestConnectionAsync(Executor executor, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var profile = LoadProfile(executor);
            var (apiKey, keySource) = ResolveKeyWithSource(profile);
            Logger.Information(
                "Проба подключения: исполнитель {Nick}, провайдер {Provider}, модель {Model}, " +
                "baseUrl {BaseUrl}, secretRef {SecretRef}, ключ: {KeySource}, длина {KeyLength}",
                executor.Nick, profile.Provider, profile.Model,
                profile.BaseUrl.Length > 0 ? profile.BaseUrl : "(стандартный)",
                profile.SecretRef, keySource, apiKey.Length);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var error = await ProbeAsync(profile, apiKey, timeout.Token);
            if (error is null)
            {
                Logger.Information("Проба подключения: исполнитель {Nick} подключен за {Elapsed} мс",
                    executor.Nick, sw.ElapsedMilliseconds);
            }
            else
            {
                Logger.Warning("Проба подключения: исполнитель {Nick} — ошибка за {Elapsed} мс: {Error}",
                    executor.Nick, sw.ElapsedMilliseconds, error);
            }
            return error;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Проба подключения: исполнитель {Nick} — таймаут за {Elapsed} мс",
                executor.Nick, sw.ElapsedMilliseconds);
            return Loc.T("msg.openAiCompatibleConnector.4");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проба подключения: исполнитель {Nick} — исключение", executor.Nick);
            return ex.Message;
        }
    }

    private async Task RunAsync(Job job, string requestText, AgentResume? resume, CancellationToken ct)
    {
        var task = _tasks.Get(job.TaskId);
        if (task is null)
        {
            Logger.Error("Задание {JobDisplayId}: задача {TaskId} не найдена", job.DisplayId, job.TaskId);
            _jobs.SetState(job.Id, JobState.Failed, actorId: null);
            return;
        }
        var project = task.ProjectId is null ? null : _projects.Get(task.ProjectId);
        var slug = project?.Slug;
        var sw = Stopwatch.StartNew();
        // инструменты агента: файловые — если папка проекта задана и существует (ТЗ п. 2.4,
        // todo17); инструменты заданий (родитель/код/URL/заголовок/чат, todo23) — всегда;
        // промпты (описания) подставляются из справочника действий по языку приложения (todo24);
        // правила безопасности проверяются ядром ДО выполнения действия (ТЗ гл. 12, todo25)
        var folderPath = ProjectFolder(project);
        var security = new SecurityEvaluator(_security.EffectiveForTask(task), folderPath);
        var fileTools = folderPath is null
            ? null
            : new FileToolset(folderPath, project!.Id, _publicBaseUrl,
                (abs, op) => security.ForPath(abs, op).Decision != SecurityDecision.Deny);
        // язык ОБЩЕНИЯ с агентом — язык команды задачи (T-190), а не язык установки:
        // на нём собираются и промпты, и описания инструментов из справочника действий
        var agentLanguage = AgentLanguageOf(task);
        var tools = new AgentToolset(fileTools,
            new TaskToolset(_tasks, _chat, _executors, _files, task, _refData, _picker, job.ExecutorId,
                _experience, Trello, project, _publicBaseUrl, GitLab, GitHub, Fetch),
            spec => ResolveSpec(spec, agentLanguage), security, _actions.CodeByTool,
            // медиатека проекта (T-113-S0): ролики, звук и титры складываются ССЫЛКАМИ
            // в объекты проекта — из неё потом собираются проекты видеоредакторов
            Objects is null ? null : new MediaToolset(Objects, task, job.ExecutorId))
        {
            Language = agentLanguage,
            // вызовы инструментов — сразу в консоль задания (ТЗ v1.45, todo37_3):
            // пока агент работает, по ней видно, чем он занят
            Trace = line => _console.Write(job.Id, line),
            // перебивка чатом (T-161): всё, что человек напишет в чат задачи с этой минуты,
            // доставляется агенту прямо в ходе работы; уже написанное — контекст задания,
            // перебивкой не считается. Продолжение после лимита (T-166) — исключение:
            // написанное, ПОКА задача ждала сброса, агент ещё не видел, и оно едет с «Continue»
            Chat = new ChatWatch(_chat, _executors, task.Id, job.ExecutorId,
                since: resume is { AfterLimit: true } ? job.FinishedAt ?? job.UpdatedAt : null),
        };
        // ШЛЮЗЫ В ВИДЕОРЕДАКТОРЫ (T-115-S0): состав инструментов приходит МАНИФЕСТАМИ живых
        // записей плагинов ЭТОГО сервера — где софта нет, там действия не публикуются
        // (T-110-S0 §1.3.4). Поставщик подставляется контекстом организации, как Objects;
        // не подставлен — шлюзов у задания нет, и всё остальное работает как работало
        if (Gateways is not null && Objects is not null)
        {
            var plugins = Gateways();
            tools.Gateways = new GatewayToolset(plugins, Objects, task, folderPath,
                _files.DataDir, project?.Name ?? "",
                (abs, op) => security.ForPath(abs, op).Decision != SecurityDecision.Deny,
                security.AllowedFullPathDirs());
            // ОБРАТНАЯ ПОЛИТИКА БЕЗОПАСНОСТИ (T-112-S0): инструмент плагина без записи
            // справочника действий ЗАПРЕЩЁН, а не разрешён по умолчанию
            tools.PluginTools = Storage.Services.PluginSetupService.PluginToolNames(
                plugins.Select(p => p.Manifest));
            // чей инструмент — для правил вида «плагины и MCP» (T-155-S0)
            tools.PluginToolOwners = Storage.Services.PluginSetupService.PluginToolOwners(
                plugins.Select(p => p.Manifest));
        }
        // ИНСТРУМЕНТЫ СЕРВЕРОВ MCP (T-119-S0) — под ту же обратную политику, и это здесь
        // ГЛАВНОЕ: список у сервера MCP динамический, он вправе добавить инструмент в новой
        // версии, и такой инструмент обязан упереться в «нет записи справочника — запрещено»,
        // а не проехать мимо правил молча. Имена берутся из кэша ЭТОГО сервера — того самого,
        // который заполняет явное «обновить список инструментов»
        var mcpTools = McpTools?.Invoke();
        if (mcpTools is { Count: > 0 })
        {
            var names = new HashSet<string>(tools.PluginTools, StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(tools.PluginToolOwners, StringComparer.Ordinal);
            foreach (var (pluginCode, list) in mcpTools)
            {
                foreach (var tool in list)
                {
                    names.Add(tool);
                    // инструмент сервера MCP тоже принадлежит плагину — правило «плагины и
                    // MCP» закрывает и его (T-155-S0)
                    owners[tool] = pluginCode;
                }
            }
            tools.PluginTools = names;
            tools.PluginToolOwners = owners;
        }
        // ТОКЕН ЗАДАНИЯ (T-34-S0): реквизиты, под которыми агент зовёт действия AI2P клиентом
        // командной строки — синхронно, внутри своего хода, а не маркером (то есть концом хода).
        // Живёт только в памяти процесса и только пока идёт задание: снимается в finally ниже.
        // Личность вызова — ИСПОЛНИТЕЛЬ задания: набор инструментов тут ровно тот же, которым
        // работает само задание, поэтому и авторство в чате, опыте и журнале, и правила
        // безопасности считаются так же, как при вызове инструмента или разборе маркера
        tools.Session = AgentSessionRegistry.Register(job.Id, job.DisplayId, task.Id,
            task.DisplayId, job.ExecutorId, tools);
        // таймаут ожидания ответа — из формы исполнителя (T-124); подставляется в toolset
        // перед вызовом, потому что коннектор один на все задания сразу и своего поля
        // иметь не может. null — ждать без ограничения
        TimeSpan? callTimeout = AgentTimeout.Default;
        try
        {
            var executor = _executors.Get(job.ExecutorId)
                           ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.4"));
            var profile = LoadProfile(executor);
            var (apiKey, keySource) = ResolveKeyWithSource(profile);
            callTimeout = AgentTimeout.Of(executor.ResponseTimeoutMinutes);
            tools.CallTimeout = callTimeout;
            Logger.Information(
                "Задание {JobDisplayId} по задаче {TaskDisplayId}: исполнитель {Nick}, провайдер {Provider}, " +
                "модель {Model}, baseUrl {BaseUrl}, maxTokens {MaxTokens}, effort {Effort}, " +
                "таймаут ответа {CallTimeout}, ключ: {KeySource}, файловые инструменты: {Tools}, {Mode}, " +
                "длина запроса {RequestLength} симв.",
                job.DisplayId, task.DisplayId, executor.Nick, profile.Provider, profile.Model,
                profile.BaseUrl.Length > 0 ? profile.BaseUrl : "(стандартный)",
                profile.MaxTokens, profile.Effort ?? "(нет)", AgentTimeout.Describe(callTimeout),
                keySource, tools.Files is null ? "нет" : tools.Files.RootDir,
                resume is null ? "новый запуск" : "продолжение после ответа", requestText.Length);
            _console.Write(job.Id, Loc.T("msg.aiConnectorBase.23",
                Loc.T(resume is null ? "msg.aiConnectorBase.24" : "msg.aiConnectorBase.25"),
                profile.Provider, profile.Model, profile.MaxTokens, requestText.Length,
                AgentTimeout.Describe(callTimeout)));

            if (resume is null)
            {
                // полный запрос — файлом (п. 6.3: большие тексты не в событии), в журнале — путь
                var requestDump = JsonSerializer.Serialize(new
                {
                    provider = Kind,
                    model = profile.Model,
                    maxTokens = profile.MaxTokens,
                    effort = profile.Effort,
                    fallbacks = profile.Fallbacks,
                    fileTools = tools.Files?.RootDir,
                    system = RequestSystemPrompt(tools),
                    message = requestText,
                }, DumpOptions);
                var requestPath = WriteAiFile(slug, task.DisplayId, $"{job.DisplayId}-request.json", requestDump);
                AppendEvent(task, job, EventTypes.AgentRequest, new { model = profile.Model, path = requestPath });
            }
            else
            {
                AppendEvent(task, job, EventTypes.AgentRequest,
                    new { model = profile.Model, resume = true, answer = Preview(resume.AnswerText) });
            }

            // пока модель считает, в консоль задания идёт и вывод её локального сервера
            // (llama-server пишет скорость и число токенов, ТЗ v1.45, todo37_3) — по нему
            // видно, работает модель или встала
            AiCallOutcome outcome;
            using (var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var pump = PumpLocalServerAsync(profile, job.Id, pumpCts.Token);
                try
                {
                    outcome = await CallAsync(profile, apiKey, requestText, tools, resume, ct);
                }
                finally
                {
                    pumpCts.Cancel();
                    try
                    {
                        await pump;
                    }
                    catch (Exception ex)
                    {
                        // сбой чтения консоли не должен подменять собой исход задания
                        Logger.Warning(ex, "Задание {JobDisplayId}: чтение вывода локального сервера прервано",
                            job.DisplayId);
                    }
                }
            }

            // вызовы инструментов (файловых и заданий) — в журнал работ (UI «История работ»)
            var callLog = tools.CallLog;
            if (callLog.Count > 0)
            {
                Logger.Information("Задание {JobDisplayId}: вызовов инструментов {Count}",
                    job.DisplayId, callLog.Count);
                AppendEvent(task, job, EventTypes.AgentToolCalls, new { calls = callLog });
            }
            // срабатывания правил безопасности (запреты, подтверждения) — события журнала (п. 12.3)
            if (tools.SecurityTriggers.Count > 0)
            {
                Logger.Information("Задание {JobDisplayId}: срабатываний правил безопасности {Count}",
                    job.DisplayId, tools.SecurityTriggers.Count);
                AppendEvent(task, job, EventTypes.RuleTriggered, new { triggers = tools.SecurityTriggers });
            }

            // токены накапливаются по сегментам работы (до/после вопросов, ТЗ v1.17)
            var current = _jobs.Get(job.Id) ?? job;

            if (outcome.Question is { } question)
            {
                // агент задал вопрос (ТЗ v1.17): контекст — файлом, job → waiting_human,
                // задача → waiting_reply; продолжение — после ответа человека (ResumeJobAsync)
                var totalIn = current.InputTokens + question.InputTokens;
                var totalOut = current.OutputTokens + question.OutputTokens;
                var partialCost = ComputeCost(executor.CapabilitiesPath, totalIn, totalOut);
                _files.WriteText(ContextRel(slug, task.DisplayId, job.DisplayId), question.ContextJson);
                _chat.AddQuestion(task.Id, job.ExecutorId, job.Id, question.Text, question.Options,
                    question.ToExecutorId);
                // чего ждём (T-185): ответа человека или ответа агента родственной задачи —
                // от этого зависит, кто продолжит работу (человек из Inbox или сторож
                // оркестратора, увидевший ответ собеседника в чате)
                _jobs.SetState(job.Id, JobState.WaitingHuman, actorId: job.ExecutorId, resultPath: null,
                    partialCost, totalIn, totalOut, "USD",
                    question.ToExecutorId is null ? JobWaitKinds.Human : JobWaitKinds.Agent);
                if (!task.IsTemplate) // шаблон не исполняется — его статус не трогаем (ТЗ v1.26)
                {
                    _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: job.ExecutorId);
                }
                if (question.IsConfirmation)
                {
                    // запрос подтверждения правила безопасности (ТЗ п. 12.3) — событие журнала
                    AppendEvent(task, job, EventTypes.HumanConfirmRequested,
                        new { question = Preview(question.Text) });
                }
                Logger.Information(
                    "Задание {JobDisplayId}: агент задал вопрос за {Elapsed} мс и ждёт ответа: {Question}",
                    job.DisplayId, sw.ElapsedMilliseconds, Preview(question.Text));
                _console.Write(job.Id, Loc.T("msg.aiConnectorBase.6") + Preview(question.Text));
                return;
            }

            var result = outcome.Result!;
            if (result.LimitPercent is not null)
            {
                // провайдер прислал текущий % использования лимита — показывается
                // в списке исполнителей (ТЗ v1.37)
                _executors.SetBusy(job.ExecutorId, busyUntil: null, result.LimitPercent);
            }
            var inputTokens = current.InputTokens + result.InputTokens;
            var outputTokens = current.OutputTokens + result.OutputTokens;
            var cost = ComputeCost(executor.CapabilitiesPath, inputTokens, outputTokens);
            Logger.Information(
                "Задание {JobDisplayId} выполнено за {Elapsed} мс: модель {Model}, finishReason {FinishReason}, " +
                "токены {InputTokens}/{OutputTokens}, стоимость ${Cost}, длина ответа {TextLength} симв.",
                job.DisplayId, sw.ElapsedMilliseconds, result.Model, result.FinishReason,
                inputTokens, outputTokens, cost, result.Text.Length);
            _console.Write(job.Id,
                Loc.T("msg.aiConnectorBase.7", sw.Elapsed, result.Text.Length, inputTokens, outputTokens, result.FinishReason));
            var responseDump = JsonSerializer.Serialize(new
            {
                model = result.Model,
                finishReason = result.FinishReason,
                inputTokens,
                outputTokens,
                cost,
                text = result.Text,
            }, DumpOptions);
            var responsePath = WriteAiFile(slug, task.DisplayId, $"{job.DisplayId}-response.json", responseDump);

            // генерация оборвана по лимиту выходных токенов профайла (todo_bugfix_2):
            // итоговый текст пуст или неполон — задание завершается ошибкой с подсказкой,
            // а не пустым результатом на «проверке»
            if (IsTruncatedOutput(result.FinishReason))
            {
                throw new InvalidOperationException(
                    Loc.T("msg.aiConnectorBase.8", result.FinishReason, profile.MaxTokens) +
                    (result.Text.Length == 0 ? Loc.T("msg.aiConnectorBase.9") : Loc.T("msg.aiConnectorBase.10")) +
                    Loc.T("msg.aiConnectorBase.11", responsePath));
            }

            // результат — артефакт задачи; задача уходит на проверку человеку (этап 2);
            // токены и валюта пишутся в задание для биллинга (ТЗ v1.15, todo18); валюта ИИ — USD
            var resultPath = _files.WriteTaskArtifact(slug, task.DisplayId, $"{job.DisplayId}-result.md", result.Text);
            if (task.IsTemplate)
            {
                _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost,
                    inputTokens, outputTokens, "USD");
                // шаблон не исполняется: статус не трогаем, итог агента (например, сводка
                // авторазбиения на подзадачи) — комментарием в чат задачи (ТЗ v1.26, todo28)
                _chat.Add(task.Id, job.ExecutorId, null, result.Text);
            }
            else if (WentToSubtasks(task))
            {
                // задание разошлось на ПОДЗАДАЧИ (T-185): работа над задачей не кончилась,
                // а ушла вниз — проверять человеку пока нечего. Поэтому не «проверка», а
                // «пауза»: задача ждёт своих подзадач. Агент при этом НЕ закрывается — job
                // остаётся ждущим (wait_kind=subtasks), а его сессия сохраняется тем же файлом
                // контекста, что и у висящего вопроса: когда подзадачи будут готовы, финальный
                // анализ проведёт та же сессия, помня всё, что уже выяснила (ProcessSplitParent).
                // Контекста нет (коннектор так не умеет) — задание закрывается, и финальный
                // анализ будет отдельным заданием, как до T-185
                var canWait = result.ContextJson is { Length: > 0 };
                if (canWait)
                {
                    _files.WriteText(ContextRel(slug, task.DisplayId, job.DisplayId), result.ContextJson!);
                }
                _jobs.SetState(job.Id, canWait ? JobState.WaitingHuman : JobState.Done,
                    actorId: job.ExecutorId, resultPath, cost, inputTokens, outputTokens, "USD",
                    JobWaitKinds.Subtasks);
                _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: job.ExecutorId);
                Logger.Information("Задание {JobDisplayId}: задача {TaskDisplayId} ушла на подзадачи — " +
                                   "«пауза», агент {Waits} (T-185)",
                    job.DisplayId, task.DisplayId,
                    canWait ? "ждёт их завершения той же сессией" : "закрыт: продолжать нечем");
                _console.Write(job.Id, Loc.T(canWait ? "msg.talk.1" : "msg.talk.2"));
            }
            else
            {
                _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost,
                    inputTokens, outputTokens, "USD");
                // ОЖИДАНИЕ ПЕРЕЗАПУЩЕННЫХ ЗАДАЧ (T-31-S0): задача-прогонщик вернула соседей
                // в доработку, прописала их себе блокирующими и попросила подождать
                // (wait_for_recheck) — тогда её собственный итог не «статус при завершении»,
                // а «ожидает»: исполнитель освобождается и может взять те самые задачи,
                // а очередь поднимет прогон снова, когда они завершатся. Пометка живёт
                // ровно один заход и снимается этим же чтением
                var rechecking = _tasks.TakeRecheckWait(task.Id);
                if (rechecking)
                {
                    Logger.Information("Задание {JobDisplayId}: задача {TaskDisplayId} уходит в ожидание "
                                       + "перезапущенных задач — «ожидает» (T-31-S0)",
                        job.DisplayId, task.DisplayId);
                }
                // СТАТУС ПО ГОТОВНОСТИ (T-250): куда уходит нормально завершённая задача,
                // решает сама задача, а не коннектор. По умолчанию это «проверка» — прежнее
                // поведение; поставленное человеком «готово» означает «проверять нечего»
                // и тем же переходом отпускает задачи, которые эта блокировала
                _tasks.ChangeStatus(task.Id,
                    rechecking ? TaskStatuses.Pending : _tasks.AiDoneStatusOf(task.Id),
                    actorId: job.ExecutorId);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new
            {
                model = profile.Model,
                path = responsePath,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                cost,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // остановлено кнопкой «остановить» — состояния уже переведены в CancelAsync
            Logger.Information("Задание {JobDisplayId} отменено после {Elapsed} мс",
                job.DisplayId, sw.ElapsedMilliseconds);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.19", sw.Elapsed));
        }
        catch (Exception exRaw)
        {
            // Сюда попадает и OperationCanceledException БЕЗ отмены кнопкой — таймаут ожидания
            // ответа (T-117): раньше он выглядел как «остановлено» и не менял состояний,
            // из-за чего задача вечно висела «в работе», а исполнитель — занятым. Теперь это
            // ошибка задания: job → failed, задача → error. Сам таймаут с T-124 задаётся
            // в форме исполнителя — об этом и говорит текст ошибки. Исключение — лимит
            // провайдера (T-121): задание тоже failed, а задача ЖДЁТ сброса (DeferAfterLimit).
            var ex = exRaw is OperationCanceledException
                ? new InvalidOperationException(
                    Loc.T("msg.aiConnectorBase.12", sw.Elapsed, AgentTimeout.Describe(callTimeout)))
                : exRaw;
            Logger.Error(ex, "Задание {JobDisplayId} по задаче {TaskDisplayId} завершилось ошибкой за {Elapsed} мс",
                job.DisplayId, task.DisplayId, sw.ElapsedMilliseconds);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.21") + ex.Message);
            DateTime? limitUntil = null;
            if (ex is ProviderLimitException limit)
            {
                // исчерпан лимит провайдера (ТЗ v1.37): фиксируется «занят до» — когда лимит
                // отпустит (Retry-After провайдера; не сообщил — час); автоподбор не назначит
                // занятого исполнителя до этого времени
                limitUntil = limit.RetryAt ?? DateTime.UtcNow.AddHours(1);
                _executors.SetBusy(job.ExecutorId, limitUntil, limit.LimitPercent ?? 100);
                Logger.Warning("Исполнитель задания {JobDisplayId}: лимит провайдера исчерпан, занят до {Until}",
                    job.DisplayId, limitUntil.Value.ToLocalTime());
            }
            // связь с агентом лимит не рвёт (T-166): у Claude CLI осталась живая сессия —
            // её id сохраняется тем же файлом контекста, что и у висящего вопроса, и когда
            // лимит отпустит, задача продолжит РАБОТУ, а не начнёт задание заново
            var resumeContext = (ex as ProviderLimitException)?.ContextJson
                                ?? (ex as ProviderAuthException)?.ContextJson;
            if (resumeContext is not null)
            {
                _files.WriteText(ContextRel(slug, task.DisplayId, job.DisplayId), resumeContext);
            }
            var errorPath = _files.WriteTaskArtifact(slug, task.DisplayId,
                $"{job.DisplayId}-error.md", Loc.T("msg.aiConnectorBase.13", ex.Message));
            _jobs.SetState(job.Id, JobState.Failed, actorId: null, errorPath);
            if (!task.IsTemplate)
            {
                // просьба уйти в ожидание перезапущенных задач (T-31-S0) относилась к ЭТОМУ
                // заданию: оно не удалось, значит и просьба протухла — иначе следующий заход
                // ушёл бы в «ожидает» ничего не перезапустив. Счётчик кругов при этом
                // сохраняется: он считает круги задачи, а не заходы
                _tasks.TakeRecheckWait(task.Id, clearPass: false);
                if (limitUntil is { } waitUntil)
                {
                    // лимит оборвал задание на середине (T-121, повторный пуск): это не
                    // «встал с ошибкой», а ОЖИДАНИЕ — задача ждёт сброса окна и запускается
                    // сама тем же сторожем, что и отложенный старт. Компьютер можно выключать
                    DeferAfterLimit(task, job, waitUntil, resumeContext is not null);
                }
                else if (ex is ProviderAuthException)
                {
                    // сеанс входа истёк (todo96): это тоже не «встал с ошибкой» — ждать
                    // нечего, нужен человек. Задача уходит в паузу с пометкой «ждёт входа»,
                    // а отпускает её вход в AI2P (Настройки → Модели → «Вход в Claude CLI»)
                    DeferAfterAuth(task, job, resumeContext is not null);
                }
                else
                {
                    // статус «встал с ошибкой» (ТЗ v1.14); статус шаблона не трогаем (ТЗ v1.26)
                    _tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: null);
                }
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new { error = ex.Message });
        }
        finally
        {
            // задание кончилось — токен клиента командной строки перестаёт работать (T-34-S0)
            AgentSessionRegistry.Release(tools.Session);
            tools.Session = null;
            if (_active.TryRemove(job.Id, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Работа задачи ушла ВНИЗ, на подзадачи (T-185): задание пометило задачу разбитой
    /// (флаг autoSplit, ТЗ v1.26) и оставило после себя хотя бы одну незавершённую подзадачу.
    /// Такая задача не идёт на «проверку»: проверять пока нечего — сначала должны отработать
    /// подзадачи. Состояние задачи и заданий читается из БД заново: подзадачи агент создавал
    /// уже после старта, и в снимке задачи их нет.
    /// </summary>
    private bool WentToSubtasks(TaskItem task)
    {
        var fresh = _tasks.Get(task.Id);
        if (fresh is null || !TaskService.HasLaunchFlag(fresh.LaunchJson, "autoSplit"))
        {
            return false;
        }
        return _tasks.ListChildren(task.Id)
            .Any(child => child.DeletedAt is null && !child.IsTemplate
                          && child.Status is not (TaskStatuses.Review or TaskStatuses.Done
                              or TaskStatuses.Cancelled));
    }

    /// <summary>
    /// Лимит провайдера оборвал задание на середине (T-121, повторный пуск): задача не
    /// «встаёт с ошибкой», а ЖДЁТ сброса лимита — старт переносится на <paramref name="untilUtc"/>,
    /// статус становится «ожидает», в чат уходит объяснение от имени исполнителя. Дальше её
    /// подхватит сторож отложенных стартов (JobOrchestrator.StartDeferredOnce) — в том числе
    /// после выключения компьютера: перенос хранится в самой задаче.
    ///
    /// canContinue (T-166) — у задания сохранён контекст живой сессии агента: тогда в задаче
    /// остаётся и указатель на это задание, и сторож не начнёт работу заново, а продолжит её
    /// словом «Continue».
    /// </summary>
    private void DeferAfterLimit(TaskItem task, Job job, DateTime untilUtc, bool canContinue = false)
    {
        var nick = _executors.Get(job.ExecutorId)?.Nick ?? "";
        try
        {
            _tasks.SetStartAfter(task.Id, untilUtc);
            _tasks.SetResumeJob(task.Id, canContinue ? job.Id : null);
            // «пауза», а не «ожидает» (T-185): работа НАЧАТА и не потеряна — у CLI-агента жива
            // и сама сессия (T-166). «Ожидает» означало бы, что задачу ещё никто не брал,
            // и в списках она стояла бы рядом с нетронутыми
            _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: null);
            _chat.Add(task.Id, job.ExecutorId, task.ResponsibleId,
                LimitWaitMessage(nick, untilUtc, canContinue));
        }
        catch (InvalidOperationException ex)
        {
            // задачу успели удалить/перенести на другой сервер — исход задания это менять
            // не должно, но и молчать о таком нельзя
            Logger.Warning(ex, "Задача {TaskDisplayId}: перенос старта после лимита не удался", task.DisplayId);
            return;
        }
        Logger.Warning("Задача {TaskDisplayId}: лимит исполнителя {Nick} исчерпан на середине задания — " +
                       "старт перенесён на {Until} (T-121)", task.DisplayId, nick, untilUtc.ToLocalTime());
        _console.Write(job.Id,
            Loc.T("msg.aiConnectorBase.14", untilUtc.ToLocalTime()));
    }

    /// <summary>
    /// Сеанс входа в CLI истёк (todo96): задача не «встаёт с ошибкой», а ЖДЁТ ВХОДА.
    /// В отличие от лимита, момента «когда отпустит» не существует — отпускает человек,
    /// войдя в AI2P (Настройки → Модели → «Вход в Claude CLI»): вход снимает пометку и
    /// пускает такие задачи в работу разом.
    ///
    /// Отложенный старт при этом всё-таки ставится — на <see cref="AuthRetry"/> вперёд.
    /// Это запасной путь: человек мог войти и мимо AI2P, прямо в терминале, и тогда задача
    /// тронется сама, без единого нажатия.
    ///
    /// canContinue — у задания сохранён контекст живой сессии агента: после входа работа
    /// продолжится ТОЙ ЖЕ сессией, а не начнётся заново (тот же приём, что при лимите, T-166).
    /// </summary>
    private void DeferAfterAuth(TaskItem task, Job job, bool canContinue)
    {
        var nick = _executors.Get(job.ExecutorId)?.Nick ?? "";
        var retryAt = DateTime.UtcNow + AuthRetry;
        try
        {
            _tasks.SetStartAfter(task.Id, retryAt);
            _tasks.SetResumeJob(task.Id, canContinue ? job.Id : null);
            _tasks.SetLaunchFlag(task.Id, TaskService.WaitAuthFlag, true);
            _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: null);
            _chat.Add(task.Id, job.ExecutorId, task.ResponsibleId, AuthWaitMessage(nick, canContinue));
        }
        catch (InvalidOperationException ex)
        {
            // задачу успели удалить/перенести на другой сервер — исход задания это менять
            // не должно, но и молчать о таком нельзя
            Logger.Warning(ex, "Задача {TaskDisplayId}: пометка «ждёт входа» не поставилась", task.DisplayId);
            return;
        }
        Logger.Warning("Задача {TaskDisplayId}: сеанс входа исполнителя {Nick} истёк — " +
                       "задача ждёт входа (todo96)", task.DisplayId, nick);
        _console.Write(job.Id, Loc.T("msg.aiConnectorBase.26"));
    }

    /// <summary>Через сколько задача, ждущая входа, пробует запуститься сама (todo96):
    /// человек мог войти мимо AI2P — прямо в терминале.</summary>
    public static readonly TimeSpan AuthRetry = TimeSpan.FromMinutes(30);

    /// <summary>Сообщение в чат задачи, когда задание оборвал истёкший вход (todo96):
    /// что случилось, что сделать и что будет дальше.</summary>
    public static string AuthWaitMessage(string nick, bool canContinue = false) =>
        Loc.T("msg.aiConnectorBase.27", nick) +
        (canContinue
            ? Loc.T("msg.aiConnectorBase.28")
            : Loc.T("msg.aiConnectorBase.29")) +
        Loc.T("msg.aiConnectorBase.30");

    /// <summary>Сообщение в чат задачи, когда лимит оборвал задание (T-121): до какого времени
    /// ждём, что произойдёт само и что можно сделать руками. canContinue (T-166) — работа
    /// продолжится ТОЙ ЖЕ сессией агента, а не начнётся заново.</summary>
    public static string LimitWaitMessage(string nick, DateTime untilUtc, bool canContinue = false) =>
        Loc.T("msg.aiConnectorBase.15", nick, untilUtc.ToLocalTime()) +
        (canContinue
            ? Loc.T("msg.aiConnectorBase.16")
            : Loc.T("msg.aiConnectorBase.17")) +
        Loc.T("msg.aiConnectorBase.18");

    /// <summary>
    /// Переливать вывод локального сервера модели (launchCommand, ТЗ п. 2.9) в консоль задания,
    /// пока идёт вызов (ТЗ v1.45, todo37_3). Сервер запускали не мы (внешний) или модель
    /// облачная — делать нечего.
    /// </summary>
    private async Task PumpLocalServerAsync(ModelProfile profile, string jobId, CancellationToken ct)
    {
        if (_localModels is null || profile.LaunchCommand.Trim().Length == 0)
        {
            return;
        }
        // с начала работы задания, а не с начала жизни сервера: загрузка весов уже позади
        var seq = _localModels.ReadOutput(profile.LaunchCommand, long.MaxValue).NextSeq;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            var (lines, next) = _localModels.ReadOutput(profile.LaunchCommand, seq);
            seq = next;
            foreach (var line in lines)
            {
                _console.Write(jobId, Loc.T("msg.aiConnectorBase.19") + line);
            }
        }
        // добрать то, что сервер напечатал уже после завершения вызова (итоговая статистика)
        foreach (var line in _localModels.ReadOutput(profile.LaunchCommand, seq).Lines)
        {
            _console.Write(jobId, Loc.T("msg.aiConnectorBase.19") + line);
        }
    }

    /// <summary>Промпт инструмента из справочника действий (ТЗ v1.21, todo24):
    /// кастомная правка либо значение по умолчанию; нет в справочнике — текст из кода.</summary>
    /// <param name="language">Язык описания инструмента — язык команды задачи (T-190):
    /// описание читает агент, а не человек; null — язык установки.</param>
    private FileToolset.ToolSpec ResolveSpec(FileToolset.ToolSpec spec, string? language)
    {
        try
        {
            var prompt = _actions.PromptByTool(spec.Name,
                string.IsNullOrWhiteSpace(language) ? _language() : language);
            return prompt is null ? spec : spec with { Description = prompt };
        }
        catch (Exception ex)
        {
            // справочник недоступен — работаем на текстах из кода (фолбэк)
            Logger.Warning(ex, "Справочник действий недоступен для {Tool} — описание из кода", spec.Name);
            return spec;
        }
    }

    /// <summary>
    /// Папка проекта (ТЗ п. 2.7) для файловых инструментов и правил безопасности:
    /// null — папка не задана или не существует. Значение ПЕР-СЕРВЕРНОЕ (ТЗ гл. 6, этап 42) —
    /// его подставляет ProjectService для того сервера, на котором мы работаем.
    /// </summary>
    private static string? ProjectFolder(Core.Entities.Project? project)
    {
        var folderPath = project?.FolderPath;
        return string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)
            ? null
            : folderPath;
    }

    protected ModelProfile LoadProfile(Executor executor)
    {
        if (executor.ProfilePath.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.37", executor.Nick));
        }
        var json = _files.ReadText(executor.ProfilePath);
        if (json.Trim().Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.38", executor.ProfilePath));
        }
        var profile = ModelProfile.Parse(json);
        if (profile.Model.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.20"));
        }
        return profile;
    }

    protected string ResolveKey(ModelProfile profile) => ResolveKeyWithSource(profile).Value;

    /// <summary>
    /// Откуда брать ключ API (ТЗ гл. 10, этап 45): с появлением ключей ОРГАНИЗАЦИИ значение
    /// лежит в её БД зашифрованным, а не в secrets.json. Подставляется контекстом организации
    /// (<c>ModelKeyService.ResolveWithSource</c>); не подставлен — читаем secrets.json, как
    /// раньше: так работают тесты и установки без организации.
    /// </summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>
    /// Импортёр Trello для инструмента агента import_task_from_url (ТЗ v1.50, todo50).
    /// Подставляется контекстом организации ПОСЛЕ создания коннектора (импортёр строится
    /// позже коннекторов); не подставлен — инструмент агенту не публикуется.
    /// </summary>
    public TrelloImporter? Trello { get; set; }

    /// <summary>Импортёр GitLab для того же инструмента (T-249): процедуру выбирает ВИД
    /// ССЫЛКИ, поэтому агенту достаточно одного import_task_from_url на все источники.
    /// Подставляется так же — контекстом организации после создания коннектора.</summary>
    public GitLabImporter? GitLab { get; set; }

    /// <summary>Импортёр GitHub для того же инструмента (T-247) — подставляется так же,
    /// контекстом организации после создания коннектора.</summary>
    public GitHubImporter? GitHub { get; set; }

    /// <summary>
    /// Получение внешних файлов задания (fetch_file, T-255): картинка карточки лежит либо
    /// ссылкой в интернете, либо файлом хранилища — по этой ссылке сервис кладёт её в кэш
    /// агента и отдаёт путь на диске. Подставляется контекстом организации после создания
    /// коннектора (сервису нужны источники импорта — они строятся позже); не подставлен —
    /// инструмент агенту не публикуется.
    /// </summary>
    public FileFetchService? Fetch { get; set; }

    /// <summary>
    /// ОБЪЕКТЫ ПРОЕКТА для инструментов МЕДИАТЕКИ (media_add / media_list / media_remove,
    /// T-113-S0): медиатека — представление над теми же объектами, своего хранилища у неё
    /// нет. Подставляется контекстом организации после создания коннектора, как импортёры;
    /// не подставлен — инструменты медиатеки агенту не публикуются.
    /// </summary>
    public Storage.Services.ObjectService? Objects { get; set; }

    /// <summary>
    /// ЖИВЫЕ ЗАПИСИ ШЛЮЗОВ ЭТОГО СЕРВЕРА (T-115-S0) — манифест плюс найденный здесь путь к
    /// программе. Поставщик, а не готовый список: софт ищется на этом компьютере и ответ
    /// живёт минуту, а коннектор один на все задания сразу. Подставляется контекстом
    /// организации, как <see cref="Objects"/>; не подставлен — инструменты шлюзов агенту
    /// не публикуются, и остальная работа идёт как прежде.
    /// </summary>
    public Func<IReadOnlyList<GatewayPlugin>>? Gateways { get; set; }

    /// <summary>
    /// ИНСТРУМЕНТЫ СЕРВЕРОВ MCP ЭТОГО СЕРВЕРА (T-119-S0): «код плагина → имена инструментов»
    /// из кэша, снятого с самих серверов. Нужны они ровно для обратной политики безопасности:
    /// имя, у которого нет записи справочника, обязано быть ЗАПРЕЩЕНО, а не разрешено по
    /// умолчанию. Поставщик, а не готовый список, — кэш меняется, пока приложение работает.
    /// Не подставлен — подключений MCP у этого сервера нет, всё работает как прежде.
    /// </summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyList<string>>>? McpTools { get; set; }

    /// <summary>Ключ + источник для диагностики (само значение ключа в логи не пишется, п. 6.3).
    /// Пустой secretRef — сервер без авторизации (локальный llama-server/Ollama): ключ не ищется.</summary>
    private (string Value, string Source) ResolveKeyWithSource(ModelProfile profile)
    {
        if (profile.SecretRef.Trim().Length == 0)
        {
            return ("", Loc.T("msg.aiConnectorBase.21"));
        }
        var (value, source) = KeyResolver is not null
            ? KeyResolver(profile.SecretRef)
            : _secrets.ResolveWithSource(profile.SecretRef);
        if (value is null)
        {
            Logger.Warning("Ключ API не найден: secretRef {SecretRef}, {Source}", profile.SecretRef, source);
            throw new InvalidOperationException(
                // куда именно класть ключ, знает хранилище: у ключа модели это свой файл
                // в подкаталоге secrets/ (T-228), а не общий secrets.json
                Loc.T("msg.aiConnectorBase.22", profile.SecretRef,
                    _secrets.LocationOf(profile.SecretRef), SecretStore.EnvNameOf(profile.SecretRef)));
        }
        return (value, source);
    }

    /// <summary>Стоимость по декларации возможностей (п. 7.3): cost.in_per_1m / out_per_1m, USD за 1M токенов.</summary>
    private double ComputeCost(string capabilitiesPath, long inputTokens, long outputTokens)
    {
        try
        {
            var json = _files.ReadText(capabilitiesPath);
            if (json.Length == 0)
            {
                return 0;
            }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("cost", out var cost))
            {
                return 0;
            }
            double inPer1M = cost.TryGetProperty("in_per_1m", out var i) && i.TryGetDouble(out var iv) ? iv : 0;
            double outPer1M = cost.TryGetProperty("out_per_1m", out var o) && o.TryGetDouble(out var ov) ? ov : 0;
            return inputTokens * inPer1M / 1_000_000 + outputTokens * outPer1M / 1_000_000;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Файл контекста агента (диалог до вопроса, ТЗ v1.17) в tasks/&lt;id&gt;/ai/.</summary>
    private string ContextRel(string? slug, string taskDisplayId, string jobDisplayId) =>
        Path.Combine(_files.TaskDirRel(slug, taskDisplayId), "ai", $"{jobDisplayId}-context.json");

    /// <summary>Файл в tasks/&lt;display_id&gt;/ai/ (п. 6.4.4); возвращает относительный путь для журнала.</summary>
    private string WriteAiFile(string? slug, string taskDisplayId, string fileName, string text)
    {
        var rel = Path.Combine(_files.TaskDirRel(slug, taskDisplayId), "ai", fileName);
        _files.WriteText(rel, text);
        return rel.Replace('\\', '/');
    }

    private static string Preview(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";

    /// <summary>Генерация оборвана по лимиту выходных токенов: «length» у OpenAI-совместимых,
    /// «max_tokens» (StopReason.MaxTokens) у Anthropic.</summary>
    protected static bool IsTruncatedOutput(string finishReason) =>
        finishReason.Equals("length", StringComparison.OrdinalIgnoreCase)
        || finishReason.Replace("_", "").Equals("maxtokens", StringComparison.OrdinalIgnoreCase);

    private void AppendEvent(TaskItem task, Job job, string eventType, object payload) =>
        _events.Append(new EventRecord
        {
            ActorId = job.ExecutorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = eventType,
            EntityType = "job",
            EntityId = job.Id,
            PayloadJson = JsonSerializer.Serialize(payload),
        });
}
