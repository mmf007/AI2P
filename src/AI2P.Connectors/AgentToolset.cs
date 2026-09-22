using System.Text.Json;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>Итог проверки безопасности вызова инструмента (ТЗ гл. 12, todo25).</summary>
public sealed record SecurityCheck(SecurityDecision Decision, string? RuleInfo, string Message)
{
    public static readonly SecurityCheck Allowed = new(SecurityDecision.Allow, null, "");
}

/// <summary>
/// Набор инструментов ИИ-агента для одного задания (todo23): файловые инструменты каталога
/// проекта (есть только когда папка проекта задана, todo17) + инструменты заданий (всегда).
/// Коннекторы публикуют Specs и выполняют вызовы через ExecuteAsync — диспетчеризация
/// по имени инструмента здесь. Описания инструментов (промпты для агента) подставляются
/// из справочника действий через резолвер (ТЗ v1.21, todo24) — тексты в коде лишь фолбэк.
/// </summary>
public sealed class AgentToolset
{
    /// <summary>Файловые инструменты; null — папка проекта не задана или не существует.</summary>
    public FileToolset? Files { get; }

    /// <summary>Инструменты поиска и чтения заданий (todo23) — доступны всегда.</summary>
    public TaskToolset Tasks { get; }

    /// <summary>Инструменты МЕДИАТЕКИ ПРОЕКТА (T-113-S0): media_add / media_list /
    /// media_remove. null — сервис объектов заданию не выдан (тесты, старые коннекторы);
    /// у задачи без проекта набор есть, но себя не публикует (<see cref="MediaToolset.CanUse"/>).</summary>
    public MediaToolset? Media { get; }

    /// <summary>Инструменты ШЛЮЗОВ в видеоредакторы (T-115-S0): сборка монтажного листа из
    /// медиатеки и рендер без окна. Набор складывается из манифестов живых записей плагинов;
    /// null — плагинов на этом сервере нет или медиатека заданию не выдана.
    /// <para>Ставится ПОСЛЕ создания набора (как <see cref="Chat"/>), поэтому язык раздаётся
    /// и здесь: порядок присваиваний в инициализаторе объекта не гарантирован, а инструмент,
    /// ответивший агенту на чужом языке, — это молчаливый дефект.</para></summary>
    public GatewayToolset? Gateways
    {
        get => _gateways;
        set
        {
            _gateways = value;
            if (value is not null)
            {
                value.Language = _language;
            }
        }
    }

    private GatewayToolset? _gateways;

    private readonly Func<FileToolset.ToolSpec, FileToolset.ToolSpec> _resolve;
    private readonly SecurityEvaluator? _security;
    private readonly Func<string, string?> _actionCodeByTool;

    /// <summary>Операция с файлами по инструменту (для правил доступа к каталогам, todo25).</summary>
    private static readonly Dictionary<string, string> FileOps = new(StringComparer.Ordinal)
    {
        ["list_files"] = "read",
        ["search_files"] = "read",
        ["read_file"] = "read",
        ["file_url"] = "read",
        ["write_file"] = "write",
    };

    /// <summary>Срабатывания правил безопасности (запреты и подтверждения) — в журнал работ
    /// (событие rule.triggered, ТЗ п. 12.3).</summary>
    public List<string> SecurityTriggers { get; } = [];

    /// <summary>
    /// ИНСТРУМЕНТЫ, ПРИШЕДШИЕ ОТ ПЛАГИНОВ (T-112-S0) — имена, объявленные манифестами шлюзов
    /// и серверами MCP (<c>PluginSetupService.PluginToolNames</c>). У них политика ОБРАТНАЯ
    /// общей: нет записи в справочнике действий — ЗАПРЕЩЕНО. Общее правило («нет записи —
    /// правил на инструмент нет, значит можно», 2d3af8da) для плагинов не годится: список
    /// инструментов у MCP динамический, и первый же сервер, добавивший инструмент в новой
    /// версии, протащил бы его мимо правил молча. Пусто — плагинов нет (обычное задание).
    /// </summary>
    public IReadOnlySet<string> PluginTools { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// ЧЕЙ ЭТО ИНСТРУМЕНТ: имя инструмента → код плагина (T-155-S0). Нужен новому виду правил
    /// безопасности «плагины и MCP»: правило заводится на ПЛАГИН целиком («не пускать агента
    /// в trainer.musubi»), а вызов приходит именем инструмента, и связать одно с другим больше
    /// нечем. Пусто — плагинов у задания нет, проверка не делается вовсе.
    /// </summary>
    public IReadOnlyDictionary<string, string> PluginToolOwners { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Живой след работы агента — в консоль задания (ТЗ v1.45, todo37_3):
    /// вызовы инструментов видны сразу, а не только в журнале после завершения.</summary>
    public Action<string>? Trace { get; set; }

    /// <summary>
    /// Сколько ждать ответа провайдера в этом задании (T-124) — из формы исполнителя
    /// (<c>Executor.ResponseTimeoutMinutes</c>, см. <see cref="AgentTimeout"/>);
    /// null — БЕЗ ОГРАНИЧЕНИЯ. Проставляется вызывающим (AiConnectorBase) на каждое задание,
    /// поэтому и живёт здесь, а не в поле коннектора: коннектор один на все задания сразу.
    /// </summary>
    public TimeSpan? CallTimeout { get; set; } = AgentTimeout.Default;

    /// <summary>
    /// Перебивка чатом (T-161): сообщения людей, написанные в чат задачи ПОКА агент работает,
    /// доставляются ему прямо в ходе работы, а его ответ уходит обратно в чат. Ставится
    /// вызывающим (AiConnectorBase) на каждое задание — как <see cref="CallTimeout"/>:
    /// коннектор один на все задания сразу и своего поля иметь не может.
    /// null — за чатом не следим (тесты инструментов, шаблоны).
    /// </summary>
    public ChatWatch? Chat { get; set; }

    /// <summary>
    /// Язык ПРОМПТОВ агента (T-190): язык общения агентов команды задачи
    /// (<c>Team.AgentLanguage</c>, ТЗ п. 2.8 и гл. 9), а НЕ язык установки. Всё, что читает
    /// сам агент — системный промпт, дописки о маркерах, описания и ответы инструментов, —
    /// берётся из словарей по этому языку (<see cref="Loc.In(string?,string)"/>);
    /// null — язык установки (команды у задачи нет, тесты). Ставится вызывающим
    /// (AiConnectorBase) на каждое задание, как <see cref="CallTimeout"/>: коннектор один
    /// на все задания сразу и своего поля иметь не может. Значение раздаётся и инструментам
    /// заданий — их ответы тоже читает агент.
    /// </summary>
    public string? Language
    {
        get => _language;
        set
        {
            _language = value;
            if (Tasks is not null)
            {
                Tasks.Language = value;
            }
            if (Files is not null)
            {
                Files.Language = value;
            }
            if (Media is not null)
            {
                Media.Language = value;
            }
            if (Gateways is not null)
            {
                Gateways.Language = value;
            }
        }
    }

    private string? _language;

    /// <param name="resolveSpec">Подстановка промпта из справочника действий (todo24);
    /// null — спецификации публикуются как есть (описания из кода).</param>
    /// <param name="security">Оценка правил безопасности (ТЗ гл. 12, todo25); null — без проверок.</param>
    /// <param name="actionCodeByTool">Код действия справочника по имени инструмента (для правил).</param>
    /// <param name="media">Медиатека проекта (T-113-S0); null — сервис объектов заданию
    /// не выдан, инструменты media_* не публикуются.</param>
    public AgentToolset(FileToolset? files, TaskToolset tasks,
        Func<FileToolset.ToolSpec, FileToolset.ToolSpec>? resolveSpec = null,
        SecurityEvaluator? security = null,
        Func<string, string?>? actionCodeByTool = null,
        MediaToolset? media = null)
    {
        Files = files;
        Tasks = tasks;
        Media = media;
        _resolve = resolveSpec ?? (spec => spec);
        _security = security;
        _actionCodeByTool = actionCodeByTool ?? (_ => null);
    }

    /// <summary>
    /// Проверка правил безопасности ДО выполнения инструмента (ТЗ п. 12.3, todo25):
    /// правила на действие AI2P (по коду из справочника) + правила доступа к каталогам
    /// (для файловых инструментов — по пути из аргументов). Побеждает самое строгое решение.
    /// Deny — вызов не выполняется (агенту возвращается отказ); Confirm — подтверждение
    /// человеком через вопрос в чате задачи (механика вопросов агента).
    /// </summary>
    public SecurityCheck Authorize(string toolName, JsonElement args)
    {
        if (_security is null)
        {
            return SecurityCheck.Allowed;
        }
        var decision = SecurityDecision.Allow;
        Core.Entities.SecurityRule? rule = null;

        var code = _actionCodeByTool(toolName);
        var pluginWithoutAction = false;
        if (code is not null)
        {
            (decision, rule) = _security.ForAction(code);
        }
        else if (PluginTools.Contains(toolName))
        {
            // ОБРАТНАЯ ПОЛИТИКА ДЛЯ ПЛАГИНОВ (T-112-S0): у инструмента плагина записи
            // справочника нет — значит он не проходил регистрацию при инициализации, и
            // закрыть его правилом человеку было нечем. Разрешить такой вызов означает
            // отдать первому же MCP-серверу право добавлять себе возможности молча
            (decision, rule) = (SecurityDecision.Deny, null);
            pluginWithoutAction = true;
        }

        // ПЛАГИНЫ И MCP (T-155-S0): правило на плагин целиком либо на его операцию. Считается
        // ПОВЕРХ правила действия: у инструмента плагина есть и код справочника (своё правило
        // на одно действие), и хозяин-плагин (правило «в этот плагин агента не пускать»)
        if (PluginToolOwners.TryGetValue(toolName, out var pluginCode))
        {
            var (pluginDecision, pluginRule) =
                _security.ForPlugin(pluginCode, SecurityEvaluator.PluginUse, toolName);
            if (Rank(pluginDecision) > Rank(decision))
            {
                (decision, rule) = (pluginDecision, pluginRule);
                pluginWithoutAction = false;
            }
        }

        // файловые инструменты: правило доступа к каталогу по пути из аргументов
        if (Files is not null && FileOps.TryGetValue(toolName, out var op))
        {
            var path = args.ValueKind == JsonValueKind.Object
                       && args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? ""
                : "";
            var abs = Files.ResolveUnchecked(path);
            var (dirDecision, dirRule) = _security.ForPath(abs, op);
            if (Rank(dirDecision) > Rank(decision))
            {
                (decision, rule) = (dirDecision, dirRule);
            }
        }

        if (decision == SecurityDecision.Allow)
        {
            return SecurityCheck.Allowed;
        }
        var ruleInfo = pluginWithoutAction
            ? Loc.In(Language, "prompt.tools.6")
            : rule is null
                ? Loc.In(Language, "prompt.tools.1")
                : Loc.In(Language, "prompt.tools.2", rule.Scope, rule.Target, rule.Pattern, rule.Permission);
        var call = $"{toolName}({ArgsPreview(args)})";
        var message = decision == SecurityDecision.Deny
            ? Loc.In(Language, "prompt.tools.3", call, ruleInfo)
            : Loc.In(Language, "prompt.tools.4", call, ruleInfo);
        SecurityTriggers.Add($"{decision}: {call} — {ruleInfo}");
        var log = $"security {decision}: {call} — {ruleInfo}";
        (FileOps.ContainsKey(toolName) && Files is not null ? Files.CallLog : Tasks.CallLog).Add(log);
        // СМЕНА СОСТОЯНИЯ ЧУЖОЙ ЗАДАЧИ, закрытая правилом (T-31-S0), пишется В ЧАТ задачи:
        // отказ читает только агент, а решение тут за человеком — либо открыть действие
        // правилом, либо перевести задачу в доработку руками. Молчание в этом месте
        // выглядит как «прогон закончился, и ничего не произошло»
        if (StatusTools.Contains(toolName))
        {
            Tasks.ReportDeniedTaskChange(toolName, args, ruleInfo);
        }
        return new SecurityCheck(decision, ruleInfo, message);
    }

    /// <summary>Действия, меняющие состояние ЧУЖОЙ задачи (T-34-S0, T-31-S0): отказ по ним
    /// человеку пишется в чат — работа прогона на этом встаёт, и без записки об этом никто
    /// не узнает. Ожидание (wait_for_recheck) сюда не входит: оно ничего чужого не трогает
    /// и человеку от его отказа делать нечего.</summary>
    private static readonly HashSet<string> StatusTools =
        new(StringComparer.Ordinal) { "set_task_status", "restart_task_for_recheck" };

    private static int Rank(SecurityDecision d) => d switch
    {
        SecurityDecision.Deny => 2,
        SecurityDecision.Confirm => 1,
        _ => 0,
    };

    private static string ArgsPreview(JsonElement args)
    {
        var raw = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText();
        return raw.Length <= 200 ? raw : raw[..200] + "…";
    }

    /// <summary>Есть ли файловые инструменты (для системного промпта и диагностики).</summary>
    public bool HasFileTools => Files is not null;

    /// <summary>Дописка к системному промпту: правила безопасности задачи (todo26) — иначе
    /// агент не знает про открытые правилами каталоги и отказывается сам.</summary>
    public string SecurityNote => _security?.DescribeForAgent(Language) ?? "";

    /// <summary>Каталоги, открытые разрешающими правилами (T-117): CLI-коннектор передаёт их
    /// флагами --add-dir — текст правила в промпте песочницу Claude Code не расширяет.</summary>
    public IReadOnlyList<string> AllowedDirs => _security?.AllowedFullPathDirs() ?? [];

    /// <summary>Дописка к системному промпту про create_task (ТЗ v1.26, todo28): когда разбивать
    /// на подзадачи, список skills, правило приоритета; пусто — инструмент не опубликован.</summary>
    public string SplitNote => Tasks.CreateNote;

    /// <summary>
    /// То же для агента БЕЗ инструментов AI2P (CLI-агент, T-125): подзадача создаётся маркером
    /// в ответе, а не вызовом create_task. Правило безопасности на действие AI2P.Tasks.Create
    /// (deny или confirm) — про маркер агенту не рассказываем вовсе: подтвердить его до начала
    /// работы некому, а сам маркер при разборе ответа всё равно будет отклонён (Authorize).
    /// Пусто — задача уже разбита либо действие закрыто правилом.
    /// </summary>
    public string CliSplitNote(string marker) =>
        Tasks.CanCreate && ActionAllowed("create_task") ? Tasks.CreateMarkerNote(marker) : "";

    /// <summary>
    /// Дописка к промпту про ПЕРЕНОС ЗАДАЧИ ПО ИЕРАРХИИ маркером — для агента БЕЗ инструментов
    /// AI2P (CLI-агент, T-2-S0). Правило безопасности на действие AI2P.Tasks.Move (deny или
    /// confirm) закрывает возможность целиком, как у подзадач: про маркер агенту не
    /// рассказываем, а сам маркер при разборе ответа всё равно будет отклонён (Authorize).
    /// </summary>
    public string CliMoveNote(string marker) =>
        ActionAllowed("move_task") ? Tasks.MoveMarkerNote(marker) : "";

    /// <summary>
    /// Дописка к промпту про СМЕНУ СОСТОЯНИЯ ЧУЖОЙ ЗАДАЧИ маркером — для агента БЕЗ
    /// инструментов AI2P (CLI-агент, T-34-S0). Правило безопасности на действие
    /// AI2P.Tasks.SetStatus (deny или confirm) закрывает возможность целиком, как у переноса
    /// задач: про маркер агенту не рассказываем, а сам маркер при разборе ответа всё равно
    /// будет отклонён (Authorize).
    /// </summary>
    public string CliStatusNote(string marker) =>
        Tasks.CanSetStatus && ActionAllowed("set_task_status") ? Tasks.StatusMarkerNote(marker) : "";

    /// <summary>
    /// Дописка к промпту про ПЕРЕЗАПУСК ЧУЖОЙ ЗАДАЧИ ДЛЯ ПОВТОРНОЙ ПРОВЕРКИ и уход
    /// в ожидание маркерами — для агента БЕЗ инструментов AI2P (CLI-агент, T-31-S0).
    /// Правило безопасности на действие AI2P.Tasks.Recheck (deny или confirm) закрывает
    /// возможность целиком: про маркеры не рассказываем, а сами маркеры при разборе ответа
    /// всё равно будут отклонены (Authorize).
    /// </summary>
    public string CliRecheckNote(string restartMarker, string waitMarker) =>
        Tasks.CanRecheck && ActionAllowed("restart_task_for_recheck")
            ? Tasks.RecheckMarkerNote(restartMarker, waitMarker)
            : "";

    /// <summary>
    /// ЖИВАЯ СЕССИЯ ЗАДАНИЯ (T-34-S0) — реквизиты, под которыми агент зовёт действия AI2P
    /// клиентом командной строки синхронно, внутри своего хода. Ставится вызывающим
    /// (AiConnectorBase) на каждое задание, как <see cref="CallTimeout"/>; null — клиент
    /// заданию не выдан (тесты, коннекторы с инструментами AI2P), и всё работает по-старому.
    /// </summary>
    public AgentSession? Session { get; set; }

    /// <summary>
    /// Дописка к промпту про запись опыта маркером — для агента БЕЗ инструментов AI2P
    /// (CLI-агент, T-144). Правило безопасности на действие AI2P.Experience.Create (deny или
    /// confirm) закрывает возможность целиком, как у подзадач: про маркер агенту не
    /// рассказываем, а сам маркер при разборе ответа всё равно будет отклонён (Authorize).
    /// Пусто — опыт вести негде (ни проекта, ни шаблона) или запись закрыта правилом.
    /// </summary>
    public string CliExperienceNote(string marker) =>
        ActionAllowed("create_experience") ? Tasks.ExperienceMarkerNote(marker) : "";

    /// <summary>
    /// То же про шаблоны проекта (CLI-агент, T-144): действие AI2P.Templates.Create.
    /// Пусто — задача вне проекта или правка шаблонов закрыта правилом.
    /// </summary>
    /// <param name="readMarker">Маркер чтения заданий — им запрашивается список шаблонов.</param>
    public string CliTemplateNote(string marker, string readMarker) =>
        ActionAllowed("create_template") ? Tasks.TemplateMarkerNote(marker, readMarker) : "";

    /// <summary>Разрешают ли правила безопасности действие инструмента <paramref name="tool"/>;
    /// правил нет либо инструмента нет в справочнике — разрешено (умолчание для действий).</summary>
    public bool ActionAllowed(string tool) =>
        _security is null || _actionCodeByTool(tool) is not { } code
        || _security.ForAction(code).Decision == SecurityDecision.Allow;

    /// <summary>
    /// Дописка к промпту про ПОЛУЧЕНИЕ ФАЙЛА ПО ССЫЛКЕ маркером — для агента БЕЗ инструментов
    /// AI2P (CLI-агент, T-255). Правило безопасности на действие AI2P.Files.Fetch (deny или
    /// confirm) закрывает возможность целиком, как у подзадач: про маркер агенту не
    /// рассказываем, а сам маркер при разборе ответа всё равно будет отклонён (Authorize).
    /// </summary>
    public string CliFetchNote(string marker, int maxFetches) =>
        Tasks.CanFetch && ActionAllowed("fetch_file") ? Tasks.FetchMarkerNote(marker, maxFetches) : "";

    /// <summary>Каталог кэша полученных файлов (T-255) — CLI-коннектор передаёт его флагом
    /// <c>--add-dir</c>: без него агент не прочитает то, что мы для него скачали. null —
    /// получение файлов недоступно или закрыто правилом безопасности.</summary>
    public string? FetchDir =>
        Tasks.CanFetch && ActionAllowed("fetch_file") ? Tasks.FetchDir : null;

    /// <summary>
    /// Дописка к промпту про чтение чужих заданий маркером — для агента БЕЗ инструментов AI2P
    /// (CLI-агент, T-127). Правило безопасности на действие AI2P.Tasks.GetByCode (deny или
    /// confirm) закрывает возможность целиком: про маркер агенту не рассказываем, а сам маркер
    /// при разборе ответа всё равно будет отклонён (Authorize по каждому вызову). Пусто —
    /// чтение заданий закрыто правилом.
    /// </summary>
    public string CliReadTaskNote(string marker, int maxLookups) =>
        ActionAllowed("get_task_by_code") ? TaskToolset.ReadMarkerNote(marker, maxLookups, Language) : "";

    /// <summary>
    /// Дописка к промпту про чат и перебивку — для агента БЕЗ инструментов AI2P (CLI-агент,
    /// T-161): ответ человеку пишется маркером. Правило безопасности на действие
    /// AI2P.Chat.Send (deny или confirm) закрывает возможность целиком, как у подзадач:
    /// про маркер агенту не рассказываем, а сам маркер при разборе ответа будет отклонён.
    /// </summary>
    public string CliChatNote(string marker) =>
        Tasks.CanChat && ActionAllowed("send_chat_message") ? TaskToolset.ChatMarkerNote(marker, Language) : "";

    /// <summary>
    /// Дописка к системному промпту про разговор с агентами РОДСТВЕННЫХ задач (T-185):
    /// соседние подзадачи одного родителя, родитель и свои потомки. Правило безопасности
    /// на действие AI2P.Chat.SendToTask (deny или confirm) закрывает возможность целиком —
    /// как у подзадач и опыта: про инструмент (или маркер) агенту не рассказываем, а сам
    /// вызов при разборе всё равно будет отклонён (Authorize).
    /// </summary>
    /// <param name="marker">Маркер письма для агента БЕЗ инструментов AI2P (CLI-агент);
    /// null — у агента есть инструменты send_task_message / wait_task_reply.</param>
    public string TalkNote(string? marker = null) =>
        Tasks.CanTalk && ActionAllowed("send_task_message") ? Tasks.TalkNote(marker) : "";

    /// <summary>
    /// Родительское задание текстом для агента БЕЗ инструментов AI2P (CLI-агент, T-120):
    /// формулировка, критерии, результаты и чат родителя — то же, что вернуло бы действие
    /// AI2P.Tasks.GetParent (get_parent_task). Правила безопасности учитываются, как при
    /// обычном вызове: запрет или требование подтверждения — блок не подставляется
    /// (спросить подтверждение до начала работы агента не у кого).
    /// Пусто — родителя нет, он удалён или действие закрыто правилом.
    /// </summary>
    public string ParentTaskBlock
    {
        get
        {
            if (!PromptBlockAllowed("get_parent_task"))
            {
                return "";
            }
            var block = Tasks.ParentPromptBlock();
            if (block.Length > 0)
            {
                Trace?.Invoke(Loc.T("msg.agentToolset.1", block.Length));
            }
            return block;
        }
    }

    /// <summary>
    /// Задания-соседи (подзадачи того же родителя) текстом для агента БЕЗ инструментов AI2P
    /// (CLI-агент, T-132): код, заголовок, статус, исполнитель, ссылка и результат каждого —
    /// то же, что вернуло бы действие AI2P.Tasks.GetSiblings (get_sibling_tasks). Правила
    /// безопасности учитываются, как у родительского блока: запрет или требование
    /// подтверждения — блок не подставляется. Пусто — родителя нет, соседей нет или
    /// действие закрыто правилом.
    /// </summary>
    public string SiblingTasksBlock
    {
        get
        {
            if (!PromptBlockAllowed("get_sibling_tasks"))
            {
                return "";
            }
            var block = Tasks.SiblingsPromptBlock();
            if (block.Length > 0)
            {
                Trace?.Invoke(Loc.T("msg.agentToolset.2", block.Length));
            }
            return block;
        }
    }

    /// <summary>Разрешают ли правила безопасности подставить в промпт блок, заменяющий вызов
    /// инструмента <paramref name="tool"/> (T-120, T-132). Не Allow — блока не будет:
    /// спросить подтверждение до начала работы агента не у кого; срабатывание правила видно
    /// человеку в журнале работ и в списке срабатываний.</summary>
    private bool PromptBlockAllowed(string tool)
    {
        if (_security is null || _actionCodeByTool(tool) is not { } code)
        {
            return true;
        }
        var (decision, rule) = _security.ForAction(code);
        if (decision == SecurityDecision.Allow)
        {
            return true;
        }
        var info = rule is null ? code : Loc.T("msg.agentToolset.3", rule.Scope, rule.Pattern, rule.Permission);
        SecurityTriggers.Add(Loc.T("msg.agentToolset.4", decision, tool, info));
        Tasks.CallLog.Add(Loc.T("msg.agentToolset.5", decision, tool, info));
        return false;
    }

    /// <summary>Дописка к системному промпту про опыт и шаблоны (ТЗ п. 2.11; T-144):
    /// обязательно учесть блок «Опыт выполнения» и поправить/дополнить опыт при отклонениях,
    /// а опыт проекта и шаблоны править по прямой просьбе задания; пусто — писать некуда.</summary>
    public string ExperienceNote => Tasks.ExperienceNote;

    /// <summary>Спецификация с промптом из справочника действий (для ask_question в коннекторах).</summary>
    public FileToolset.ToolSpec Resolve(FileToolset.ToolSpec spec) => _resolve(spec);

    /// <summary>Все опубликованные агенту инструменты (без ask_question — он добавляется коннектором);
    /// описания — из справочника действий (todo24). create_task публикуется, только пока задача
    /// не разбита (isNotSplit, ТЗ v1.26); инструменты опыта — когда опыт есть куда писать
    /// (узел шаблона или проект, ТЗ п. 2.11), шаблоны — у задач проекта (T-144).</summary>
    public IEnumerable<FileToolset.ToolSpec> Specs =>
        (Files is null ? [] : FileToolset.Specs)
        .Concat(TaskToolset.Specs.Where(s => s.Name switch
        {
            "create_task" => Tasks.CanCreate,
            // ПЕРЕНОС записи между областями (T-269-S0) — там же, где правка: переносит агент
            // только записи своего проекта и общие правила, прочитанные в задании
            "create_experience" or "update_experience" or "move_experience" => Tasks.CanExperience,
            // ПОИСК по опыту (T-268-S0) — условие шире: искать есть что и у задачи, которой
            // писать опыт некуда (общие правила работы получает каждая задача)
            "search_experience" => Tasks.CanSearchExperience,
            // РАЗБОР ОПЫТА (T-271-S0): перечислить область и спросить статистику использования
            // может любая задача — это чтение; гасить и оживлять записи разрешено там же,
            // где правка (условие CanExperience)
            "list_experience" or "experience_usage" => Tasks.CanSearchExperience,
            "set_experience_active" => Tasks.CanExperience,
            "list_templates" or "create_template" or "update_template" => Tasks.CanTemplates,
            "import_task_from_url" => Tasks.CanImport,
            "fetch_file" => Tasks.CanFetch, // внешние файлы задания (T-255)
            "send_chat_message" => Tasks.CanChat,
            "send_task_message" or "wait_task_reply" => Tasks.CanTalk, // разговор агентов (T-185)
            "get_child_tasks" => Tasks.CanChildren, // подзадачи (T-34-S0)
            "set_task_status" => Tasks.CanSetStatus, // состояние чужой задачи (T-34-S0)
            "update_task" => Tasks.CanUpdate, // поля чужой задачи (T-160-S0)
            // перезапуск чужой задачи для повторной проверки и ожидание (T-31-S0)
            "restart_task_for_recheck" or "wait_for_recheck" => Tasks.CanRecheck,
            // ветвление и циклы (T-300-S0): решать агенту есть что только в задаче своего типа
            "set_condition_result" => Tasks.FlowType == TaskFlow.If,
            "set_loop_result" => TaskFlow.IsLoop(Tasks.FlowType),
            "create_tasks_from_template" => Tasks.CanFromTemplate,
            "stop_hierarchy" => Tasks.CanStopHierarchy,
            _ => true,
        }))
        // медиатека проекта (T-113-S0): у задачи без проекта складывать ролик некуда,
        // и показывать агенту инструмент, который ответит отказом, незачем
        .Concat(Media is { CanUse: true } ? MediaToolset.Specs : [])
        // шлюзы в видеоредакторы (T-115-S0): состав приходит МАНИФЕСТАМИ живых плагинов,
        // поэтому список инструментов у каждого сервера свой — как и найденный на нём софт
        .Concat(Gateways?.Specs ?? [])
        .Select(_resolve);

    /// <summary>Суммарный лог вызовов для журнала работ (agent.tool_calls).</summary>
    public List<string> CallLog =>
        [.. Files?.CallLog ?? [], .. Tasks.CallLog, .. Media?.CallLog ?? [],
            .. Gateways?.CallLog ?? []];

    /// <summary>Выполнить вызов инструмента; результат (или текст ошибки) возвращается агенту.</summary>
    public async Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        Trace?.Invoke(Loc.T("msg.agentToolset.6", name, ArgsPreview(args)));
        var result = TaskToolset.Names.Contains(name)
            ? await Tasks.ExecuteAsync(name, args, ct)
            : MediaToolset.Names.Contains(name) && Media is not null
                ? Media.Execute(name, args)
                : Gateways is not null && Gateways.Handles(name)
                ? await Gateways.ExecuteAsync(name, args, ct)
                : Files is not null
                    ? await Files.ExecuteAsync(name, args, ct)
                    : Loc.In(Language, "prompt.tools.5", name);
        Trace?.Invoke(Loc.T("msg.agentToolset.7", name, result.Length));
        return result;
    }
}
