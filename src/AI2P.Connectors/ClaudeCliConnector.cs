using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI2P.Core;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// ClaudeCliConnector (ТЗ п. 7.2, todo29): исполнитель-ИИ Claude через Claude Code CLI
/// в headless-режиме (`claude -p --output-format json`) вместо Messages API — по подписке
/// заметно дешевле API. Выбирается полем transport: "cli" профайла модели (п. 2.9);
/// команда — cliCommand профайла (пусто — «claude» из PATH). Авторизация — сессией CLI
/// (claude login), ключ из секретов не используется (secretRef пуст). Инструменты AI2P
/// агенту не публикуются — CLI работает собственными инструментами, рабочий каталог
/// процесса — папка проекта. Вопросы агента (ТЗ v1.17) поддерживаются маркером
/// AI2P_QUESTION в ответе; продолжение после ответа человека — той же сессией CLI
/// (`--resume &lt;session_id&gt;`, сессии CLI хранятся на диске и переживают перезапуск).
/// Так же (маркером AI2P_SUBTASK в ответе) CLI-агент создаёт подзадачи — T-125, маркером
/// AI2P_GET_TASK читает другие задания проекта вместе с их чатами — T-127 (ответ приходит
/// следующим сообщением той же сессии, как ответ человека на вопрос), а маркерами
/// AI2P_EXPERIENCE и AI2P_TEMPLATE пишет опыт проекта/шаблона и правит шаблоны — T-144.
/// Правила безопасности задачи передаются агенту текстом промпта (принудительно ядром
/// не проверяются — действия выполняет CLI своими правами; права записи файлов задаются
/// флагами cliCommand, например `--permission-mode acceptEdits`).
/// </summary>
public sealed class ClaudeCliConnector : AiConnectorBase
{
    /// <summary>Kind коннектора; провайдер в профайле остаётся "anthropic" + transport "cli".</summary>
    public const string ProviderKind = "anthropic-cli";

    private const string DefaultCommand = "claude";

    public ClaudeCliConnector(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, SecretStore secrets,
        ChatService chat, ActionCatalogService actions, SecurityRuleService security,
        RefDataService refData, ExecutorPickService picker, ExperienceService experience,
        JobConsole console, LocalModelProcessService? localModels, TeamService teams,
        Func<string> language, string publicBaseUrl)
        : base(jobs, tasks, executors, projects, files, events, secrets, chat, actions, security,
            refData, picker, experience, console, localModels, teams, language, publicBaseUrl)
    {
    }

    public override string Kind => ProviderKind;

    /// <summary>Вопросы человеку поддерживаются: маркер AI2P_QUESTION в ответе агента,
    /// продолжение — той же сессией CLI (`--resume &lt;session_id&gt;`).</summary>
    protected override bool SupportsQuestions => true;

    /// <summary>Маркер вопроса человеку в ответе CLI-агента (аналог ask_question, ТЗ v1.17):
    /// строка `AI2P_QUESTION: {"question": "...", "options": ["...", ...]}`.</summary>
    public const string QuestionMarker = "AI2P_QUESTION:";

    /// <summary>
    /// Маркер создания подзадачи в ответе CLI-агента (T-125, аналог create_task): строка
    /// `AI2P_SUBTASK: {"title": "...", "description": "...", "skills": [...], "priority": N,
    /// "acceptance": "..."}` — параметры те же, что у инструмента (действие справочника
    /// AI2P.Tasks.Create). Инструменты AI2P CLI-агенту не публикуются, а API AI2P закрыто
    /// входом по cookie — без маркера разбить задачу на подзадачи он не мог вовсе (T-125).
    /// </summary>
    public const string SubtaskMarker = "AI2P_SUBTASK:";

    /// <summary>
    /// Маркер чтения ДРУГОГО задания в ответе CLI-агента (T-127, аналог get_task_by_code /
    /// get_task_by_url / find_tasks_by_title + get_task_chat): строка
    /// `AI2P_GET_TASK: {"code": "T-15"}` (либо "url", либо "title"). Ответ — карточка задания
    /// (формулировка, критерии приёмки, статус, результаты-артефакты) И чат целиком; приходит
    /// следующим сообщением ТОЙ ЖЕ сессии CLI, как ответ человека на вопрос, после чего агент
    /// продолжает работу. До T-127 у CLI-агента был только блок родительского задания в промпте
    /// (T-120): любую другую задачу — например ту, на которую человек дал ссылку в формулировке —
    /// он прочитать не мог вовсе (инструментов AI2P нет, /api закрыто входом по cookie).
    /// </summary>
    public const string TaskMarker = "AI2P_GET_TASK:";

    /// <summary>
    /// Маркер записи ОПЫТА в ответе CLI-агента (T-144, аналог create_experience /
    /// update_experience): строка `AI2P_EXPERIENCE: {"text": "...", "scope": "project",
    /// "skill": "код навыка"}`; со "template" — опыт узла шаблона, с "id" — правка записи.
    /// До T-144 канала к опыту у CLI-агента не было вовсе: он видел блок «Опыт проекта»
    /// в задании, но на прямую просьбу человека «заполни опыт проекта» мог только перенести
    /// выводы в файлы репозитория и написать, что поля опыта ему недоступны.
    /// </summary>
    public const string ExperienceMarker = "AI2P_EXPERIENCE:";

    /// <summary>
    /// Маркер правки ШАБЛОНОВ проекта в ответе CLI-агента (T-144, аналог create_template /
    /// update_template): строка `AI2P_TEMPLATE: {"title": "...", "description": "...", ...}`;
    /// с "code" — правка существующего узла. Выполняется РАНЬШЕ маркеров опыта, чтобы опыт
    /// нового узла привязывался к нему по заголовку (кода узла агент заранее не знает).
    /// </summary>
    public const string TemplateMarker = "AI2P_TEMPLATE:";

    /// <summary>
    /// Маркер сообщения в ЧАТ задачи в ответе CLI-агента (T-161, аналог send_chat_message):
    /// строка `AI2P_CHAT: {"text": "..."}`. Нужен прежде всего для ОТВЕТА на сообщение,
    /// которое человек написал агенту, пока тот работал (перебивка чатом, <see cref="ChatWatch"/>):
    /// инструментов AI2P у CLI-агента нет, и ответить человеку ему было нечем — его слова
    /// доходили только итоговым результатом, то есть уже после закрытия задания. Маркеры
    /// разбираются на КАЖДОМ витке сессии, а не только в итоговом ответе: человек ждёт ответа
    /// сейчас.
    /// </summary>
    public const string ChatMarker = "AI2P_CHAT:";

    /// <summary>
    /// Маркер письма АГЕНТУ РОДСТВЕННОЙ ЗАДАЧИ в ответе CLI-агента (T-185, аналог
    /// send_task_message): строка `AI2P_TO_TASK: {"code": "T-186", "text": "...", "wait": true}`.
    /// Сообщение ложится в чат той задачи и доезжает до её агента перебивкой чата — тем же
    /// путём, каким агента перебивает человек. С "wait": true работа встаёт на паузу до ответа
    /// собеседника: задание уходит в waiting_human (как на вопросе человеку), задача — в
    /// «паузу», а продолжает работу сторож оркестратора, когда ответ придёт.
    /// Разбирается на КАЖДОМ витке сессии, как AI2P_CHAT: собеседник ждёт ответа сейчас.
    /// </summary>
    public const string TaskChatMarker = "AI2P_TO_TASK:";

    /// <summary>Чем CLI-агент отвечает на сообщение из чата — текстом для блока доставки
    /// (у агентов с инструментами AI2P на этом месте инструмент send_chat_message).
    /// Текст читает агент, поэтому язык — язык команды задачи (T-190).</summary>
    private static string ChatReplyHow(string? language) =>
        Loc.In(language, "prompt.cli.1") + ChatMarker + Loc.In(language, "prompt.cli.2");

    /// <summary>
    /// Маркер ПОЛУЧЕНИЯ ФАЙЛА ПО ССЫЛКЕ в ответе CLI-агента (T-255, аналог fetch_file):
    /// строка `AI2P_GET_FILE: {"url": "https://…"}`. Картинка, приложенная к задаче импортом
    /// из Trello / GitHub / GitLab, лежит либо ссылкой в интернете (за авторизацией внешней
    /// системы), либо файлом хранилища AI2P — и то, и другое CLI-агенту недоступно: сети
    /// у него может не быть вовсе, реквизитов внешней системы нет, а каталог данных лежит
    /// вне его песочницы. По маркеру система кладёт файл в кэш агента и отвечает путём
    /// на диске следующим сообщением ТОЙ ЖЕ сессии CLI — дальше агент читает картинку
    /// своими средствами (каталог кэша передаётся ему флагом --add-dir).
    /// </summary>
    public const string FileMarker = "AI2P_GET_FILE:";

    /// <summary>
    /// Маркер ПЕРЕНОСА ЗАДАЧИ ПО ИЕРАРХИИ в ответе CLI-агента (T-2-S0, аналог move_task):
    /// строка `AI2P_MOVE_TASK: {"code": "T-241", "parent": "T-300"}`; parent пуст или «root» —
    /// задача выносится в корень. Заведён по жалобе на выпуск 1.100: человек велел выпускающей
    /// задаче «выпускать без невыполненного потомка, перенести его в следующий выпуск», а
    /// выполнить это решение агенту было НЕЧЕМ — он написал в отчёт «перевесить должен человек»,
    /// и невыполненная подзадача осталась в иерархии выпущенной версии.
    /// </summary>
    public const string MoveTaskMarker = "AI2P_MOVE_TASK:";

    /// <summary>
    /// Маркер СМЕНЫ СОСТОЯНИЯ ЧУЖОЙ ЗАДАЧИ в ответе CLI-агента (T-34-S0, аналог
    /// set_task_status): строка
    /// <c>AI2P_SET_STATUS: {"code": "T-15", "status": "pending", "restart": true}</c>.
    /// Нужна задаче-прогонщику: прогнала тесты, увидела падение — вернула соответствующие
    /// задачи в работу. Запасной путь: то же самое дешевле сделать клиентом
    /// (<c>ai2p status T-15 --status pending --restart</c>) — маркер это конец хода.
    /// </summary>
    public const string StatusMarker = "AI2P_SET_STATUS:";

    /// <summary>
    /// Маркер ПЕРЕЗАПУСКА ЧУЖОЙ ЗАДАЧИ ДЛЯ ПОВТОРНОЙ ПРОВЕРКИ в ответе CLI-агента (T-31-S0,
    /// аналог restart_task_for_recheck): строка
    /// <c>AI2P_RESTART_TASK: {"code": "T-15", "comment": "упал T15Tests"}</c>.
    /// Одно действие вместо четырёх: «доработка» + «статус при завершении = готово» +
    /// снятые попытки очереди + запись задачи себе в блокирующие.
    /// </summary>
    public const string RestartTaskMarker = "AI2P_RESTART_TASK:";

    /// <summary>
    /// Маркер УХОДА В ОЖИДАНИЕ перезапущенных задач (T-31-S0, аналог wait_for_recheck):
    /// строка <c>AI2P_WAIT_RECHECK: {}</c>. Ставится ПОСЛЕ всех перезапусков и означает
    /// «моё задание кончено, задача уходит в „ожидает“ и вернётся, когда перезапущенные
    /// завершатся». Исполнитель при этом освобождается и может взять их же.
    /// </summary>
    public const string WaitRecheckMarker = "AI2P_WAIT_RECHECK:";

    /// <summary>Сколько файлов агент может запросить маркером за одно задание (T-255):
    /// защита и от зацикливания, и от выкачивания половины интернета в каталог данных.</summary>
    public const int MaxFileFetches = 20;

    /// <summary>Сколько заданий агент может запросить маркером за одно задание (T-127):
    /// защита от зацикливания «запросил — прочитал — снова запросил». Предел исчерпан —
    /// агенту говорится об этом, и он завершает работу с тем, что есть.</summary>
    public const int MaxTaskLookups = 10;

    /// <summary>Промпт CLI-агента: без инструментов AI2P (файлами CLI работает сам),
    /// вопрос человеку — маркером AI2P_QUESTION, подзадачи — маркером AI2P_SUBTASK (T-125),
    /// чтение чужих заданий — маркером AI2P_GET_TASK (T-127), опыт и шаблоны — маркерами
    /// AI2P_EXPERIENCE и AI2P_TEMPLATE (T-144), правила безопасности — текстом (todo29).</summary>
    protected override string RequestSystemPrompt(AgentToolset tools)
    {
        var splitNote = tools.CliSplitNote(SubtaskMarker);
        return
            Loc.In(tools.Language, "prompt.cli.3") +
            QuestionMarker + Loc.In(tools.Language, "prompt.cli.4") +
            (tools.HasFileTools
                ? Loc.In(tools.Language, "prompt.cli.5") + FileLinkNote(tools.Files!, tools.Language)
                : Loc.In(tools.Language, "prompt.cli.6"))
            + BackgroundNote(splitNote.Length > 0, tools.CallTimeout, tools.Language)
            + splitNote
            + tools.CliReadTaskNote(TaskMarker, MaxTaskLookups)
            + tools.CliFetchNote(FileMarker, MaxFileFetches) // внешние файлы задания (T-255)
            + tools.CliChatNote(ChatMarker)
            + tools.TalkNote(TaskChatMarker) // разговор с агентами родственных задач (T-185)
            + tools.CliExperienceNote(ExperienceMarker)
            + tools.CliTemplateNote(TemplateMarker, TaskMarker.TrimEnd(':'))
            + tools.CliMoveNote(MoveTaskMarker) // перенос задачи по иерархии (T-2-S0)
            + tools.CliStatusNote(StatusMarker) // состояние чужой задачи (T-34-S0)
            // перезапуск чужой задачи для повторной проверки и ожидание (T-31-S0)
            + tools.CliRecheckNote(RestartTaskMarker, WaitRecheckMarker)
            + CliClientNote(tools) // действия вызовом, а не маркером (T-34-S0)
            + tools.SecurityNote;
    }

    /// <summary>
    /// ДЕЙСТВИЯ AI2P ВЫЗОВОМ, А НЕ МАРКЕРОМ (T-34-S0) — дописка к системному промпту про
    /// клиент командной строки <c>ai2p</c>. Пусто — клиента заданию не выдали (нет живой
    /// сессии, рядом с программой нет обёртки, сервер не поднят): тогда всё работает
    /// по-старому, маркерами.
    ///
    /// <para>Почему это важно сказать агенту прямо: маркер — не вызов, а КОНЕЦ ХОДА
    /// (процесс claude завершается, ответ подставляется в промпт, сессия продолжается
    /// через --resume), и по телеметрии T-288 один такой ход стоит около 160 тыс. токенов
    /// чтения контекста. Клиент отвечает в том же ходе и пределов «10 заданий / 20 файлов»
    /// не имеет.</para>
    /// </summary>
    public static string CliClientNote(AgentToolset tools)
    {
        if (tools.Session is null
            || AgentSessionRegistry.LocalBaseUrl.Trim().Length == 0
            || AgentCli.WrapperPath() is not { } wrapper)
        {
            return "";
        }
        return Loc.In(tools.Language, "prompt.cli.40", AgentCli.CommandLine(wrapper),
            AgentCli.UrlVar, AgentCli.TokenVar);
    }

    /// <summary>
    /// Дописка к промпту про фоновую работу (T-138): агент запускал долгий прогон тестов
    /// в фоне и сдавал задание словами «как только закончится — доделаю». Задание при этом
    /// закрывалось, а фоновый процесс обрывался вместе с ходом агента — продолжения не
    /// наступало никогда (T-137: лог прогона оборвался на первых строках). Правило простое:
    /// либо дождаться в своём ходе, либо оформить отложенную проверочную подзадачу.
    /// </summary>
    /// <param name="canSplit">Доступно ли создание подзадач (иначе про них не рассказываем).</param>
    /// <param name="timeout">Таймаут ответа исполнителя (T-124): сколько агенту можно молчать,
    /// пока его считают живым. null — без ограничения.</param>
    public static string BackgroundNote(bool canSplit, TimeSpan? timeout = null, string? language = null) =>
        Loc.In(language, "prompt.cli.7") +
        (timeout is { } limit
            ? Loc.In(language, "prompt.cli.8", limit.TotalMinutes)
            : Loc.In(language, "prompt.cli.9")) +
        Loc.In(language, "prompt.cli.10") +
        (canSplit
            ? Loc.In(language, "prompt.cli.11")
            : Loc.In(language, "prompt.cli.12") + QuestionMarker.TrimEnd(':') + Loc.In(language, "prompt.cli.13"));

    /// <summary>Как дать внешнюю ссылку на файл проекта (todo29): у CLI-агента нет инструмента
    /// file_url, поэтому шаблон URL (формат FileToolset.ExternalUrl) даётся текстом промпта.</summary>
    public static string FileLinkNote(FileToolset files, string? language = null)
    {
        if (files.ProjectId is null || files.PublicBaseUrl is null)
        {
            return "";
        }
        var prefix = $"{files.PublicBaseUrl}/api/files/project" +
                     $"?projectId={Uri.EscapeDataString(files.ProjectId)}&path=";
        return Loc.In(language, "prompt.cli.14", prefix, prefix);
    }

    /// <summary>
    /// Промпт первого хода задания: системная часть, текст задания и — последними — блок
    /// родительского задания (T-120) и блок заданий-соседей (T-132); пустой блок (родителя
    /// или соседей нет, действие закрыто правилом) секции не даёт вовсе.
    /// При продолжении после вопроса промпт другой: блоки уже в сессии CLI.
    /// </summary>
    public static string BuildPrompt(string systemPrompt, string requestText, string parentBlock,
        string siblingsBlock = "", string? language = null) =>
        systemPrompt + Loc.In(language, "prompt.cli.15") + requestText
        + Section(parentBlock) + Section(siblingsBlock);

    private static string Section(string block) =>
        block.Trim().Length == 0 ? "" : "\n\n---\n\n" + block;

    /// <summary>Разобранный итоговый JSON headless-запуска CLI (--output-format json).</summary>
    public sealed record CliOutput(string Text, long InputTokens, long OutputTokens,
        string Model, string StopReason, bool IsError, string SessionId);

    /// <summary>Контекст висящего вопроса (ТЗ v1.17): вместо истории сообщений — id сессии CLI,
    /// продолжение через `claude -p --resume &lt;session_id&gt;` (сессии CLI переживают перезапуск).</summary>
    private sealed class CliContextDto
    {
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    }

    protected override async Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
        string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
    {
        // продолжение после ответа человека (ТЗ v1.17): та же сессия CLI по session_id
        string? resumeSessionId = null;
        if (resume is not null)
        {
            CliContextDto? context;
            try
            {
                context = JsonSerializer.Deserialize<CliContextDto>(resume.ContextJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(Loc.T("msg.anthropicConnector.7", ex.Message));
            }
            if (context is null || context.SessionId.Trim().Length == 0)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.claudeCliConnector.1"));
            }
            resumeSessionId = context.SessionId.Trim();
        }

        var (fileName, baseArgs) = LocalModelProcessService.SplitCommand(
            profile.CliCommand.Trim().Length > 0 ? profile.CliCommand.Trim() : DefaultCommand);
        var args = new StringBuilder(baseArgs);
        args.Append(" -p --output-format json");
        if (profile.Model.Length > 0)
        {
            args.Append(" --model ").Append(profile.Model);
        }
        // разрешающие dir_access-правила задачи → флаги --add-dir (ТЗ гл. 12, T-117):
        // текстовое правило в промпте песочницу Claude Code НЕ расширяет — без флага
        // пользователю приходилось руками вписывать каталог в .claude/settings.local.json
        // проекта (наблюдалось на T-66). Текст правила в промпте при этом остаётся —
        // агенту он тоже полезен
        foreach (var dir in tools.AllowedDirs)
        {
            args.Append(" --add-dir \"").Append(dir).Append('"');
        }
        // кэш полученных по ссылке файлов (T-255): картинку, которую мы для агента скачали,
        // он читает своими средствами — а песочница Claude Code пускает только в каталог
        // проекта и в то, что названо флагом
        if (tools.FetchDir is { Length: > 0 } fetchDir)
        {
            Directory.CreateDirectory(fetchDir);
            args.Append(" --add-dir \"").Append(fetchDir).Append('"');
        }
        // РАЗРЕШЕНИЕ НА ЗАПУСК КЛИЕНТА AI2P (T-34-S0). Без него вызов упирается в песочницу
        // Claude Code: агент получает отказ и теряет ход (в телеметрии T-288 таких отказов
        // подряд было пять) — то есть правка была бы сделана и не заработала. Текст промпта
        // песочницу не расширяет, как и у каталогов (T-117): нужен именно флаг запуска
        var cliEnv = AgentProcessEnv(tools);
        if (cliEnv is not null && AgentCli.WrapperPath() is { } cliWrapper)
        {
            args.Append(" --allowedTools");
            foreach (var pattern in AgentCli.AllowedToolPatterns(cliWrapper))
            {
                // КАВЫЧКИ ВНУТРИ ЗНАЧЕНИЯ ЭКРАНИРУЮТСЯ (поймано живой проверкой). Один из
                // шаблонов сам содержит кавычки — так агент напишет путь с пробелом, — и
                // без экранирования аргумент «"Bash("C:\путь":*)"» разбирается как
                // «Bash(C:\путь:*)»: второй шаблон молча превращался в копию первого
                args.Append(" \"").Append(pattern.Replace("\"", "\\\"")).Append('"');
            }
        }
        var baseArguments = args.ToString().Trim();

        // родительское задание текстом (T-120): у CLI-агента инструментов AI2P нет, позвать
        // get_parent_task он не может, а в файлах проекта задач нет — они в БД организации.
        // При продолжении после вопроса блок уже в сессии CLI — второй раз не шлём
        var parentBlock = resume is null ? tools.ParentTaskBlock : "";

        // задания-соседи (подзадачи того же родителя) текстом (T-132): исполнителю подзадачи
        // нужны результаты уже сделанных соседей, а позвать get_sibling_tasks он не может.
        // Соседи даются сокращённо — без формулировок: рядом уже едет родительский блок
        var siblingsBlock = resume is null ? tools.SiblingTasksBlock : "";

        // промпт — через stdin: обходит лимиты длины и экранирование командной строки.
        // продолжение после сброса лимита (T-166) — это НЕ ответ человека: агенту уходит
        // короткое «Continue», его собственный контекст уже в сессии CLI
        var prompt = resume is null
            ? BuildPrompt(RequestSystemPrompt(tools), requestText, parentBlock, siblingsBlock, tools.Language)
            : resume.AfterLimit
                // + сообщения, написанные человеком, ПОКА задача ждала сброса лимита (T-166):
                // ответа он ждёт с той минуты, а агент их ещё не видел
                ? ContinuePrompt(tools.Language) + Section(ChatDelivery(tools, interrupted: false, ChatReplyHow(tools.Language)))
                : Loc.In(tools.Language, "prompt.cli.16", QuestionMarker.TrimEnd(':'), resume.AnswerText);
        var workDir = tools.Files?.RootDir ?? Environment.CurrentDirectory;

        // токены копятся по всем витками сессии: чтение чужих заданий (T-127) — это ещё
        // один запуск CLI, и в биллинге он должен быть учтён
        // свой id сессии CLI (T-161): штатно id приходит ТОЛЬКО в итоговом JSON, а прерванный
        // процесс ничего не печатает — без заранее известного id продолжить прерванную чатом
        // работу было бы нечем. Флаг ставится один раз, на первый запуск сессии
        var ownSessionId = resume is null ? Guid.NewGuid().ToString() : null;

        long totalInput = 0, totalOutput = 0;
        var lookups = 0;
        var lookupsAllowed = true;
        string? unreadReason = null;
        // получение файлов по ссылке (T-255): свой счётчик и свой предел — читать задания
        // и качать картинки агент может независимо друг от друга
        var fetches = 0;
        var fetchesAllowed = true;
        string? unfetchedReason = null;
        // перебивка чатом (T-161): пока агент работает, за чатом задачи следит ChatWatch
        var watch = tools.Chat;
        // сводка отправленных в чат сообщений агента (маркеры AI2P_CHAT) — человеку в результат
        var chatSent = new List<string>();
        // работа была прервана сообщением из чата — пояснение к возможной ошибке продолжения
        var interruptedByChat = false;
        // недоделанная сдача (T-138): переспрашиваем ОДИН раз за задание — иначе упрямый
        // агент гонял бы сессию по кругу за деньги подписки
        var nudged = false;
        var canSplit = tools.CliSplitNote(SubtaskMarker).Length > 0;
        // сводки выполненных маркеров действий (подзадачи, шаблоны, опыт, переносы, смены
        // состояния, перезапуски): они применяются на КАЖДОМ витке, а текст витка следующим
        // ходом заменяется целиком — поэтому сводки копятся отдельно и печатаются в финале
        var applied = new List<string>();

        // МАРКЕРЫ ДЕЙСТВИЙ — НА КАЖДОМ ВИТКЕ, А НЕ ТОЛЬКО В ИТОГОВОМ ОТВЕТЕ (T-96-S0).
        // До этой правки они разбирались один раз, после выхода из цикла, из ПОСЛЕДНЕГО
        // ответа CLI. А витков у хода много: чтение чужих заданий (AI2P_GET_TASK), получение
        // файлов, перебивка чатом (T-161), доставка сообщения к концу хода, переспрос
        // недоделанной сдачи (T-138) — и каждый следующий виток затирает текст предыдущего.
        // Поэтому подзадачи, заведённые маркером на промежуточном витке, молча пропадали:
        // агент видел свой маркер в истории сессии и честно писал в отчёте «подзадачи
        // созданы», а в дереве их не было (T-230: шесть подзадач перевода doc/es).
        // Порядок применения прежний и он важен: шаблоны → опыт → переносы → состояния →
        // перезапуски → подзадачи (узел шаблона нужен опыту, а коды задач маркеров переноса
        // и перезапуска не могут ссылаться на только что созданные подзадачи).
        async Task<string> ApplyActionMarkersAsync(string text)
        {
            var lang = tools.Language;
            text = await OneKindAsync(TemplateMarker, "шаблонов", Loc.In(lang, "prompt.cli.22"),
                a => HasText(a, "code") ? "update_template" : "create_template", text);
            text = await OneKindAsync(ExperienceMarker, "опыта", Loc.In(lang, "prompt.cli.23"),
                a => HasText(a, "id") ? "update_experience" : "create_experience", text);
            text = await OneKindAsync(MoveTaskMarker, "переноса задач", Loc.In(lang, "prompt.cli.39"),
                _ => "move_task", text);
            text = await OneKindAsync(StatusMarker, "смены состояния задач", Loc.In(lang, "prompt.cli.41"),
                _ => "set_task_status", text);
            text = await OneKindAsync(RestartTaskMarker, "перезапуска задач", Loc.In(lang, "prompt.cli.42"),
                _ => "restart_task_for_recheck", text);
            var subtasks = ParseSubtaskMarkers(text, out var withoutSubtasks);
            if (subtasks.Count > 0)
            {
                Logger.Information("Claude CLI: маркеров подзадач в ответе {Count}", subtasks.Count);
                applied.Add(await CreateSubtasksAsync(subtasks, tools, ct));
                text = withoutSubtasks;
            }
            return text;

            async Task<string> OneKindAsync(string marker, string what, string title,
                Func<JsonElement, string> pick, string source)
            {
                var found = ParseMarkers(marker, source, out var without);
                if (found.Count == 0)
                {
                    return source;
                }
                Logger.Information("Claude CLI: маркеров {What} в ответе {Count}", what, found.Count);
                applied.Add(await ApplyMarkersAsync(found, tools, title, pick, ct));
                return without;
            }
        }

        CliOutput output;
        while (true)
        {
            var arguments = resumeSessionId is null
                ? baseArguments + (ownSessionId is null ? "" : $" --session-id {ownSessionId}")
                : $"{baseArguments} --resume {resumeSessionId}";
            // id сессии, которым можно продолжить работу, если её сейчас прервут (T-161)
            var knownSession = resumeSessionId ?? ownSessionId;
            Logger.Information(
                "Claude CLI: {FileName} {Args}, рабочий каталог {WorkDir}, длина промпта {PromptLength} симв.",
                fileName, arguments, workDir, prompt.Length);

            // таймаут ожидания ответа — из формы исполнителя (T-124); 0 в форме = ждём столько,
            // сколько CLI работает. Раньше значение было зашито (30 мин) и молчащий, но живой
            // агент обрывался на полпути
            using var timeout = CallTimeoutCts(tools, ct);
            // перебивка чатом (T-161): пока идёт процесс CLI, следим за чатом задачи. Иначе
            // никак: headless-запуск читает промпт со stdin и закрывает его, дописать в живой
            // диалог нечего — поэтому работа прерывается, а сессия продолжается словами человека
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var watching = watch is not null && knownSession is not null && !watch.LimitReached
                ? watch.WaitAsync(runCts.Token, ChatWatch.InterruptGrace)
                : null;
            (int ExitCode, string Stdout, string Stderr) run;
            try
            {
                var running = RunProcessAsync(fileName, arguments, workDir, prompt, runCts.Token, cliEnv);
                if (watching is not null && await Task.WhenAny(running, watching) == watching
                    && await watching)
                {
                    // человек написал в чат — останавливаем работу и продолжаем ТУ ЖЕ сессию
                    // его словами. Сделанное агентом в файлах проекта никуда не девается,
                    // теряется только незаконченный шаг текущего хода (и его токены)
                    runCts.Cancel();
                    try
                    {
                        await running;
                    }
                    catch (OperationCanceledException)
                    {
                        // процесс убит нами — это и был смысл прерывания
                    }
                    watch!.CountInterrupt();
                    interruptedByChat = true;
                    resumeSessionId = knownSession;
                    prompt = FirstNonEmpty(ChatDelivery(tools, interrupted: true, ChatReplyHow(tools.Language)),
                        Loc.In(tools.Language, "prompt.cli.17"));
                    Logger.Warning("Claude CLI: работа прервана сообщением из чата задачи " +
                                   "(прерывание {Count}, сессия {SessionId}) — T-161",
                        watch.Interrupts, knownSession);
                    tools.Trace?.Invoke(Loc.T("msg.claudeCliConnector.2"));
                    continue;
                }
                run = await running;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // CLI молчал дольше таймаута. До T-124 это считалось исчерпанным лимитом подписки
                // и исполнитель помечался занятым на час — на практике оказалось неверно: лимит
                // был израсходован на 37%, а агент просто долго работал. Настоящий лимит CLI
                // сообщает текстом и его ловит ClaudeCliLimit (T-121), поэтому молчание — это
                // обычная ошибка задания с подсказкой, где увеличить таймаут
                throw new InvalidOperationException(
                    Loc.T("msg.claudeCliConnector.3", AgentTimeout.Describe(tools.CallTimeout)));
            }
            finally
            {
                // наблюдение за чатом на этом ходе больше не нужно (T-161): опрос должен
                // прекратиться до того, как токен уйдёт вместе с runCts
                runCts.Cancel();
            }
            var (exitCode, stdout, stderr) = run;
            if (exitCode != 0)
            {
                // CLI сам сообщает об исчерпанном лимите подписки (T-121, повторный пуск):
                // «5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)». Раньше это выглядело
                // обычной ошибкой — задача светилась «встал с ошибкой» без всякого «ожидает до»
                var failure = FirstNonEmpty(stderr, stdout, Loc.T("msg.claudeCliConnector.4"));
                // старый CLI не знает --session-id (T-161) — повторяем без него: перебивка
                // чатом до первого ответа CLI станет недоступна, но задание не встанет
                if (ownSessionId is not null && resumeSessionId is null && UnknownSessionIdOption(failure))
                {
                    Logger.Warning("Claude CLI не принял --session-id ({Failure}) — повтор без него; " +
                                   "прервать работу сообщением из чата до первого ответа CLI " +
                                   "будет нельзя (T-161)", Short(failure));
                    ownSessionId = null;
                    continue;
                }
                // сессии, которую продолжали после сброса лимита, у CLI уже нет (T-166):
                // работа не встаёт ошибкой — начинаем задание заново, с полным промптом
                if (resume is { AfterLimit: true } && resumeSessionId is not null
                    && requestText.Trim().Length > 0 && SessionIsGone(failure))
                {
                    Logger.Warning("Claude CLI не нашёл сессию {SessionId} ({Failure}) — " +
                                   "продолжать нечего, задание пойдёт с начала (T-166)",
                        resumeSessionId, Short(failure));
                    tools.Trace?.Invoke(Loc.T("msg.claudeCliConnector.5"));
                    resumeSessionId = null;
                    ownSessionId = Guid.NewGuid().ToString();
                    prompt = BuildPrompt(RequestSystemPrompt(tools), requestText,
                        tools.ParentTaskBlock, tools.SiblingTasksBlock, tools.Language);
                    resume = null;
                    continue;
                }
                // СЕАНС CLI ИСТЁК (todo96): «Failed to authenticate: OAuth session expired
                // and could not be refreshed». Раньше это была обычная ошибка задания —
                // задача вставала «встал с ошибкой», а понять, что надо просто войти заново,
                // было неоткуда. Вход проверяется РАНЬШЕ лимита: у отказов разные коды
                // (401/403 против 429), и путать их нельзя
                if (ClaudeCliAuth.LooksLikeAuthGone(stdout + "\n" + stderr))
                {
                    var parsedAuth = TryParseCliOutput(stdout, profile.Model);
                    throw AuthException(FailureText(parsedAuth, failure),
                        FirstNonEmpty(parsedAuth?.SessionId ?? "", knownSession ?? ""));
                }
                if (ClaudeCliLimit.TryDetect(stdout + "\n" + stderr, DateTime.UtcNow, out var resetAt))
                {
                    // сессия CLI известна и после сброса лимита оживёт (T-166): свой id мы
                    // задали флагом --session-id, а если CLI успел вернуть JSON — берём его id
                    var parsed = TryParseCliOutput(stdout, profile.Model);
                    throw LimitException(FailureText(parsed, failure), resetAt,
                        FirstNonEmpty(parsed?.SessionId ?? "", knownSession ?? ""));
                }
                throw new InvalidOperationException(Loc.T("msg.claudeCliConnector.6", exitCode, failure) +
                    (interruptedByChat
                        ? Loc.T("msg.claudeCliConnector.7")
                        : resume is { AfterLimit: true }
                            ? Loc.T("msg.claudeCliConnector.8")
                            : ""));
            }

            // «войдите заново» приходит и БЕЗ JSON при нулевом коде возврата (todo96) —
            // как и лимит ниже: без этой проверки разбор упал бы на «CLI не вернул
            // JSON-результат», и задача светилась бы «встал с ошибкой» вместо ожидания
            if (ClaudeCliAuth.LooksLikeAuthGoneInAnswer(stdout))
            {
                throw AuthException(stdout.Trim(), knownSession ?? "");
            }
            // лимит приходит и БЕЗ JSON при нулевом коде возврата — просто короткой строкой
            // (T-121): без этой проверки разбор упал бы на «CLI не вернул JSON-результат»,
            // и задача снова светилась бы «встал с ошибкой» вместо ожидания сброса
            if (ClaudeCliLimit.TryDetectInAnswer(stdout, DateTime.UtcNow, out var plainResetAt))
            {
                throw LimitException(stdout.Trim(), plainResetAt, knownSession ?? "");
            }

            output = ParseCliOutput(stdout, profile.Model);
            totalInput += output.InputTokens;
            totalOutput += output.OutputTokens;
            Logger.Information("Claude CLI → модель {Model}, stopReason {StopReason}, токены {In}/{Out}",
                output.Model, output.StopReason, output.InputTokens, output.OutputTokens);
            if (output.IsError)
            {
                var failure = FirstNonEmpty(output.Text, stderr, Loc.T("msg.claudeCliConnector.9"));
                if (ClaudeCliAuth.LooksLikeAuthGone(output.Text + "\n" + stderr + "\n" + stdout))
                {
                    throw AuthException(failure, FirstNonEmpty(output.SessionId, knownSession ?? ""));
                }
                if (ClaudeCliLimit.TryDetect(output.Text + "\n" + stderr + "\n" + stdout,
                        DateTime.UtcNow, out var resetAt))
                {
                    throw LimitException(failure, resetAt,
                        FirstNonEmpty(output.SessionId, knownSession ?? ""));
                }
                throw new InvalidOperationException(
                    Loc.T("msg.claudeCliConnector.10", output.StopReason, failure));
            }
            // лимит приходит и «успешным» ответом — коротким текстом вместо результата задания
            if (ClaudeCliAuth.LooksLikeAuthGoneInAnswer(output.Text))
            {
                throw AuthException(output.Text.Trim(),
                    FirstNonEmpty(output.SessionId, knownSession ?? ""));
            }
            if (ClaudeCliLimit.TryDetectInAnswer(output.Text, DateTime.UtcNow, out var answerResetAt))
            {
                throw LimitException(output.Text.Trim(), answerResetAt,
                    FirstNonEmpty(output.SessionId, knownSession ?? ""));
            }

            // ответы агента в чат по маркерам AI2P_CHAT (T-161) — на КАЖДОМ витке, а не только
            // в итоговом ответе: человек, написавший агенту во время работы, ждёт ответа сейчас,
            // а до конца задания может пройти час. Маркеры из текста убираются
            var chatMarkers = ParseMarkers(ChatMarker, output.Text, out var withoutChat);
            if (chatMarkers.Count > 0)
            {
                Logger.Information("Claude CLI: сообщений в чат в ответе {Count}", chatMarkers.Count);
                output = output with { Text = withoutChat };
                chatSent.AddRange(await PostChatAsync(chatMarkers, tools, ct));
            }

            // письма агентам родственных задач по маркерам AI2P_TO_TASK (T-185) — тоже на
            // КАЖДОМ витке: собеседник ждёт ответа сейчас, а до конца задания может пройти час
            var talkMarkers = ParseMarkers(TaskChatMarker, output.Text, out var withoutTalk);
            if (talkMarkers.Count > 0)
            {
                Logger.Information("Claude CLI: писем агентам соседних задач в ответе {Count}",
                    talkMarkers.Count);
                output = output with { Text = withoutTalk };
                chatSent.AddRange(await PostTaskChatAsync(talkMarkers, tools, ct));
            }

            // маркеры действий (подзадачи, шаблоны, опыт, переносы, состояния, перезапуски) —
            // тоже на КАЖДОМ витке (T-96-S0): текст витка следующим ходом заменяется целиком,
            // и всё, что в нём осталось невыполненным, пропадает молча
            var doneText = await ApplyActionMarkersAsync(output.Text);
            if (!string.Equals(doneText, output.Text, StringComparison.Ordinal))
            {
                output = output with { Text = doneText };
            }

            // чтение чужих заданий по маркерам AI2P_GET_TASK (T-127): это НЕ конец работы —
            // система отвечает агенту текстом задания и продолжает ту же сессию CLI, как
            // при ответе человека на вопрос
            var readMarkers = lookupsAllowed
                ? ParseMarkers(TaskMarker, output.Text, out _)
                : [];
            // получение файлов по ссылке маркерами AI2P_GET_FILE (T-255) — устроено так же:
            // система кладёт файл в кэш агента и отвечает путём на диске тем же ходом сессии
            var fileMarkers = fetchesAllowed
                ? ParseMarkers(FileMarker, output.Text, out _)
                : [];
            if (readMarkers.Count == 0 && fileMarkers.Count == 0)
            {
                // сообщение пришло в чат под конец хода (T-161): прерывать было уже нечего,
                // но задание вот-вот закроется — отдаём его агенту ещё одним ходом ТОЙ ЖЕ
                // сессии. Вопрос человеку (AI2P_QUESTION) — законная пауза: там агент и так
                // ждёт человека, и его сообщение придёт ответом на вопрос
                if (watch is not null && output.SessionId.Length > 0 && watch.HasNew
                    && !TryParseQuestionMarker(output.Text, out _, out _)
                    && ChatDelivery(tools, interrupted: false, ChatReplyHow(tools.Language)) is { Length: > 0 } tail)
                {
                    resumeSessionId = output.SessionId;
                    prompt = tail;
                    Logger.Information("Claude CLI: сообщение из чата пришло к концу хода — " +
                                       "отдаю агенту ещё одним ходом сессии {SessionId} (T-161)",
                        output.SessionId);
                    tools.Trace?.Invoke(Loc.T("msg.claudeCliConnector.11"));
                    continue;
                }

                // сдача недоделанного (T-138): «прогон идёт в фоне, потом доделаю». Фоновая
                // работа агента уже оборвана вместе с его ходом, а задание вот-вот закроется
                // как выполненное — поэтому даём ему ещё один ход ТОЙ ЖЕ сессии с требованием
                // довести дело до конца либо оформить отложенную проверочную подзадачу.
                // Вопрос человеку (AI2P_QUESTION) — законная пауза, его не трогаем
                if (!nudged && output.SessionId.Length > 0
                    && !TryParseQuestionMarker(output.Text, out _, out _)
                    && BackgroundPromise.Detect(output.Text, out var evidence))
                {
                    nudged = true;
                    resumeSessionId = output.SessionId;
                    prompt = UnfinishedNudge(evidence, canSplit, tools.Language);
                    Logger.Warning("Claude CLI сдал задание недоделанным ({Evidence}) — " +
                                   "требую довести работу до конца (T-138)", evidence);
                    tools.Trace?.Invoke(Loc.T("msg.claudeCliConnector.12", evidence));
                    continue;
                }
                break;
            }
            if (output.SessionId.Length == 0)
            {
                // без session_id продолжить сессию нечем — отдаём как обычный результат
                Logger.Warning("Claude CLI запросил задания или файлы, но не вернул session_id — " +
                               "запрос не выполнен");
                if (readMarkers.Count > 0)
                {
                    unreadReason = Loc.In(tools.Language, "prompt.cli.18");
                }
                if (fileMarkers.Count > 0)
                {
                    unfetchedReason = Loc.In(tools.Language, "prompt.cli.18");
                }
                break;
            }
            resumeSessionId = output.SessionId;

            // файлы — первыми: пути к ним агент увидит рядом с прочитанными заданиями
            var reply = new StringBuilder();
            if (fileMarkers.Count > 0)
            {
                fetches += fileMarkers.Count;
                if (fetches > MaxFileFetches)
                {
                    fetchesAllowed = false;
                    unfetchedReason = Loc.In(tools.Language, "prompt.cli.35", MaxFileFetches);
                    Logger.Warning("Claude CLI: предел запросов файлов ({Max}) исчерпан", MaxFileFetches);
                    reply.Append(Loc.In(tools.Language, "prompt.cli.36", MaxFileFetches,
                        FileMarker.TrimEnd(':')));
                }
                else
                {
                    Logger.Information("Claude CLI: запрошено файлов {Count} (всего за задание {Total})",
                        fileMarkers.Count, fetches);
                    reply.Append(await FetchFilesAsync(fileMarkers, tools, ct));
                }
            }
            if (readMarkers.Count > 0)
            {
                lookups += readMarkers.Count;
                if (reply.Length > 0)
                {
                    reply.Append("\n\n");
                }
                if (lookups > MaxTaskLookups)
                {
                    lookupsAllowed = false;
                    unreadReason = Loc.In(tools.Language, "prompt.cli.19", MaxTaskLookups);
                    Logger.Warning("Claude CLI: предел запросов заданий ({Max}) исчерпан", MaxTaskLookups);
                    reply.Append(Loc.In(tools.Language, "prompt.cli.20", MaxTaskLookups,
                        TaskMarker.TrimEnd(':')));
                }
                else
                {
                    Logger.Information("Claude CLI: запрошено заданий {Count} (всего за задание {Total})",
                        readMarkers.Count, lookups);
                    reply.Append(await ReadTasksAsync(readMarkers, tools, ct));
                }
            }
            else
            {
                // ответ на одни только файлы завершается тем же «продолжай работу», что
                // и ответ с заданиями (его добавляет ReadTasksAsync)
                reply.Append("\n\n").Append(Loc.In(tools.Language, "prompt.cli.17"));
            }
            prompt = reply.ToString();
        }

        // невыполненные маркеры чтения (предел исчерпан либо нет сессии) — из текста вон,
        // но человеку в результате видно, что агент просил ещё
        var answer = output.Text;
        if (unreadReason is not null)
        {
            var leftovers = ParseMarkers(TaskMarker, answer, out var withoutReads);
            if (leftovers.Count > 0)
            {
                answer = withoutReads + Loc.In(tools.Language, "prompt.cli.21", leftovers.Count, unreadReason);
            }
        }
        // то же для невыполненных запросов файлов (T-255)
        if (unfetchedReason is not null)
        {
            var leftovers = ParseMarkers(FileMarker, answer, out var withoutFetches);
            if (leftovers.Count > 0)
            {
                answer = withoutFetches +
                         Loc.In(tools.Language, "prompt.cli.37", leftovers.Count, unfetchedReason);
            }
        }

        // маркеры действий итогового ответа — той же функцией и тем же порядком, что и на
        // промежуточных витках (шаблоны → опыт → переносы → состояния → перезапуски →
        // подзадачи, T-144/T-2-S0/T-34-S0/T-31-S0/T-125). Обычно здесь уже пусто: последний
        // виток разобрал их внутри цикла, — но выйти из цикла можно и не дойдя до разбора
        var answerText = await ApplyActionMarkersAsync(answer);
        answer = answerText;

        // уход в ожидание перезапущенных задач (AI2P_WAIT_RECHECK, T-31-S0) — ПОСЛЕ
        // перезапусков: ожидание проверяет, есть ли незавершённые блокирующие, а появляются
        // они именно перезапуском. Тела у маркера нет, годится и пустой объект. Этот маркер
        // выполняется ТОЛЬКО в итоговом ответе: он заканчивает работу задачи, и на середине
        // хода ему делать нечего
        var waitMarkers = ParseMarkers(WaitRecheckMarker, answer, out var withoutWaits);
        if (waitMarkers.Count > 0)
        {
            Logger.Information("Claude CLI: маркеров ухода в ожидание в ответе {Count}",
                waitMarkers.Count);
            answer = withoutWaits;
            applied.Add(await ApplyMarkersAsync(waitMarkers, tools,
                Loc.In(tools.Language, "prompt.cli.43"), _ => "wait_for_recheck", ct));
        }

        // сводки выполненных маркеров — в конец результата (их видит человек): каждая написана
        // тогда, когда действие уже выполнено, в том числе на промежуточном витке
        if (applied.Count > 0)
        {
            answer += "\n\n" + string.Join("\n\n", applied);
        }

        // сводка ответов агента в чат (маркеры AI2P_CHAT, T-161): сами сообщения уже ушли
        // человеку по ходу работы, здесь — только след в результате, чтобы было видно,
        // что агент на перебивку ответил (и что ему на это ответили правила безопасности)
        if (chatSent.Count > 0)
        {
            answer += Loc.In(tools.Language, "prompt.cli.24") + string.Join("\n", chatSent.Select(l => "- " + l));
        }

        // вопрос человеку (маркер AI2P_QUESTION, ТЗ v1.17): контекст — id сессии CLI
        if (TryParseQuestionMarker(answer, out var question, out var options))
        {
            if (output.SessionId.Length == 0)
            {
                // без session_id продолжить сессию нельзя — отдаём как обычный результат
                Logger.Warning("Claude CLI задал вопрос, но не вернул session_id — вопрос не поддержан");
            }
            else
            {
                var contextJson = JsonSerializer.Serialize(new CliContextDto
                {
                    Provider = ProviderKind,
                    SessionId = output.SessionId,
                }, new JsonSerializerOptions { WriteIndented = true });
                Logger.Information("Claude CLI задал вопрос (сессия {SessionId}): {Question}",
                    output.SessionId, question);
                return AiCallOutcome.Of(new AgentQuestion(question, options, contextJson,
                    totalInput, totalOutput, output.Model));
            }
        }

        // агент написал соседу и ждёт ответа (маркер AI2P_TO_TASK с "wait": true, T-185):
        // пауза устроена ровно как вопрос человеку — та же сессия CLI, тот же файл контекста,
        // только адресат вопроса не человек, а исполнитель родственной задачи
        if (tools.Tasks.Waiting is { } waiting)
        {
            if (output.SessionId.Length == 0)
            {
                Logger.Warning("Claude CLI ждёт ответа задачи {Task}, но не вернул session_id — " +
                               "продолжить будет нечем, отдаю результат как есть", waiting.TaskCode);
            }
            else
            {
                var waitContext = JsonSerializer.Serialize(new CliContextDto
                {
                    Provider = ProviderKind,
                    SessionId = output.SessionId,
                }, new JsonSerializerOptions { WriteIndented = true });
                Logger.Information("Claude CLI ждёт ответа агента задачи {Task} (сессия {SessionId})",
                    waiting.TaskCode, output.SessionId);
                return AiCallOutcome.Of(new AgentQuestion(
                    Loc.In(tools.Language, "prompt.talk.14", waiting.TaskCode, waiting.Question),
                    [], waitContext, totalInput, totalOutput, output.Model,
                    IsConfirmation: false, ToExecutorId: waiting.ExecutorId));
            }
        }
        // сессия CLI живёт на диске и после конца хода (T-166): её id едет с результатом —
        // по нему работу продолжают, не начиная заново (ожидание подзадач, T-185)
        var sessionContext = output.SessionId.Length == 0
            ? null
            : JsonSerializer.Serialize(new CliContextDto
            {
                Provider = ProviderKind,
                SessionId = output.SessionId,
            }, new JsonSerializerOptions { WriteIndented = true });
        return AiCallOutcome.Of(new AiCallResult(answer, totalInput, totalOutput,
            output.Model, output.StopReason, LimitPercent: null, ContextJson: sessionContext));
    }

    /// <summary>
    /// Сообщение агенту, сдавшему работу незаконченной (T-138): уходит следующим ходом ТОЙ ЖЕ
    /// сессии CLI, как ответ человека на вопрос. Требование одно — довести дело до конца сейчас
    /// либо оформить проверочную подзадачу с отложенным стартом; ответ на него заменяет
    /// результат задания целиком, поэтому итог нужно вывести заново.
    /// </summary>
    /// <param name="evidence">Фраза ответа, по которой работа сочтена незаконченной.</param>
    /// <param name="canSplit">Доступно ли создание подзадач.</param>
    public static string UnfinishedNudge(string evidence, bool canSplit, string? language = null) =>
        Loc.In(language, "prompt.cli.25", evidence) +
        (canSplit
            ? Loc.In(language, "prompt.cli.26", SubtaskMarker.TrimEnd(':'))
            : Loc.In(language, "prompt.cli.27", QuestionMarker.TrimEnd(':'))) +
        Loc.In(language, "prompt.cli.28");

    /// <summary>
    /// Отправить в чат задачи сообщения по маркерам AI2P_CHAT (T-161): каждый маркер идёт
    /// как обычный вызов send_chat_message — с проверкой правил безопасности (действие
    /// AI2P.Chat.Send), журналом и трассой в консоль задания. Возвращает по строке на маркер
    /// (отправлено либо отказ правила) — они попадают в результат задания.
    /// </summary>
    public static async Task<List<string>> PostChatAsync(List<string> markers, AgentToolset tools,
        CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var raw in markers)
        {
            using var doc = JsonDocument.Parse(raw);
            var args = doc.RootElement;
            var check = tools.Authorize("send_chat_message", args);
            lines.Add(check.Decision == SecurityDecision.Allow
                ? await tools.ExecuteAsync("send_chat_message", args, ct)
                : check.Message);
        }
        return lines;
    }

    /// <summary>
    /// Отправить письма агентам родственных задач по маркерам AI2P_TO_TASK (T-185): каждый
    /// маркер идёт обычным вызовом send_task_message — с проверкой правил безопасности
    /// (действие AI2P.Chat.SendToTask), журналом и трассой в консоль задания. Маркер с
    /// "wait": true оставляет у набора инструментов пометку «ждём ответа»; саму паузу задания
    /// коннектор делает после хода — вопросом в чате своей задачи (как при вопросе человеку).
    /// </summary>
    public static async Task<List<string>> PostTaskChatAsync(List<string> markers, AgentToolset tools,
        CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var raw in markers)
        {
            using var doc = JsonDocument.Parse(raw);
            var args = doc.RootElement;
            var check = tools.Authorize("send_task_message", args);
            lines.Add(check.Decision == SecurityDecision.Allow
                ? await tools.ExecuteAsync("send_task_message", args, ct)
                : check.Message);
        }
        return lines;
    }

    /// <summary>CLI не нашёл сессию, которую просили продолжить (T-166): «No conversation found
    /// with session ID …». Сессии хранятся на диске и не вечны — пережидание длинного лимита
    /// (недельное окно) может их и не застать.</summary>
    public static bool SessionIsGone(string failure) =>
        (failure.Contains("no conversation found", StringComparison.OrdinalIgnoreCase)
         || failure.Contains("session not found", StringComparison.OrdinalIgnoreCase)
         || failure.Contains("no session found", StringComparison.OrdinalIgnoreCase))
        || (failure.Contains("--resume", StringComparison.OrdinalIgnoreCase)
            && failure.Contains("not found", StringComparison.OrdinalIgnoreCase));

    /// <summary>Старый Claude CLI не знает флага --session-id (T-161) — по такому отказу
    /// коннектор повторяет запуск без него.</summary>
    private static bool UnknownSessionIdOption(string failure) =>
        failure.Contains("--session-id", StringComparison.OrdinalIgnoreCase)
        && (failure.Contains("unknown option", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("unknown argument", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("unrecognized", StringComparison.OrdinalIgnoreCase)
            || failure.Contains("invalid", StringComparison.OrdinalIgnoreCase));

    /// <summary>Короткая выжимка текста для лога.</summary>
    private static string Short(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";

    /// <summary>
    /// Создать подзадачи по маркерам ответа CLI-агента (T-125): каждый маркер выполняется как
    /// обычный вызов create_task — с проверкой правил безопасности (действие AI2P.Tasks.Create),
    /// записью в журнал работ и трассой в консоль задания. Отказ правила или ошибка создания
    /// (задача уже разбита, неизвестный навык) задание не рушат: причина попадает в сводку,
    /// которую человек видит в результате.
    /// </summary>
    public static Task<string> CreateSubtasksAsync(List<string> markers, AgentToolset tools,
        CancellationToken ct) =>
        ApplyMarkersAsync(markers, tools, Loc.In(tools.Language, "prompt.cli.29"), _ => "create_task", ct);

    /// <summary>
    /// Выполнить маркеры-действия ответа CLI-агента (T-125, T-144) и собрать сводку для
    /// человека: каждый маркер идёт как обычный вызов инструмента — с проверкой правил
    /// безопасности по коду действия справочника, записью в журнал работ и трассой в консоль
    /// задания. Отказ правила или ошибка выполнения задание не рушат: причина попадает
    /// в сводку, которую человек видит в результате.
    /// </summary>
    /// <param name="heading">Заголовок раздела сводки («## Подзадачи», «## Опыт»…).</param>
    /// <param name="toolOf">Инструмент по аргументам маркера: create_* или update_*.</param>
    public static async Task<string> ApplyMarkersAsync(List<string> markers, AgentToolset tools,
        string heading, Func<JsonElement, string> toolOf, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var raw in markers)
        {
            using var doc = JsonDocument.Parse(raw);
            var args = doc.RootElement;
            var tool = toolOf(args);
            var check = tools.Authorize(tool, args);
            lines.Add("- " + (check.Decision == SecurityDecision.Allow
                ? await tools.ExecuteAsync(tool, args, ct)
                : check.Message));
        }
        return heading + "\n\n" + string.Join("\n", lines);
    }

    /// <summary>Непустая строка в аргументах маркера — по ней выбирается create_* или update_*
    /// (T-144): "id" у записи опыта, "code" у узла шаблона.</summary>
    private static bool HasText(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length > 0;

    /// <summary>
    /// Прочитать задания по маркерам ответа CLI-агента (T-127) и собрать сообщение, которое
    /// уйдёт ему следующим ходом ТОЙ ЖЕ сессии CLI. Каждый маркер выполняется как обычный вызов
    /// инструмента заданий — с проверкой правил безопасности (действия AI2P.Tasks.GetByCode /
    /// GetByUrl / FindByTitle и AI2P.Tasks.GetChat), записью в журнал работ и трассой в консоль.
    /// К карточке задания сразу прикладывается его чат: у агента нет инструмента get_task_chat,
    /// а без переписки картина задания неполная. Отказ правила и ненайденная задача работу
    /// не рушат — текст отказа получает сам агент и решает, что делать дальше.
    /// </summary>
    public static async Task<string> ReadTasksAsync(List<string> markers, AgentToolset tools,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(tools.Language, "prompt.cli.30", TaskMarker.TrimEnd(':')));
        foreach (var raw in markers)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(tools.Language, "prompt.cli.31", raw));
            sb.AppendLine();
            sb.AppendLine(await ReadOneTaskAsync(raw, tools, ct));
        }
        sb.AppendLine();
        sb.Append(Loc.In(tools.Language, "prompt.cli.17"));
        return sb.ToString();
    }

    /// <summary>
    /// Получить файлы по маркерам ответа CLI-агента (T-255) и собрать сообщение, которое
    /// уйдёт ему следующим ходом ТОЙ ЖЕ сессии CLI. Каждый маркер выполняется как обычный
    /// вызов инструмента fetch_file — с проверкой правил безопасности (действие
    /// AI2P.Files.Fetch), записью в журнал работ и трассой в консоль задания. Ошибка одного
    /// файла (нет такой картинки, отказ внешней системы) работу не рушит: причину получает
    /// сам агент и решает, что делать дальше. Завершающего «продолжай работу» здесь нет —
    /// его добавляет вызывающий, чтобы при ответе и с файлами, и с заданиями оно было одно.
    /// </summary>
    public static async Task<string> FetchFilesAsync(List<string> markers, AgentToolset tools,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.In(tools.Language, "prompt.cli.34", FileMarker.TrimEnd(':')));
        foreach (var raw in markers)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(Loc.In(tools.Language, "prompt.cli.31", raw));
            sb.AppendLine();
            sb.Append(await FetchOneFileAsync(raw, tools, ct));
        }
        return sb.ToString();
    }

    /// <summary>Один маркер получения файла: обязателен параметр "url" — ссылка из текста
    /// задания (внешняя http(s), наша ссылка на файл или путь в каталоге данных).</summary>
    private static async Task<string> FetchOneFileAsync(string rawArgs, AgentToolset tools,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(rawArgs);
        var url = Text(doc.RootElement, "url");
        if (url.Length == 0)
        {
            return Loc.In(tools.Language, "prompt.cli.38", FileMarker.TrimEnd(':'));
        }
        var args = Args("url", url);
        var check = tools.Authorize("fetch_file", args);
        return check.Decision == SecurityDecision.Allow
            ? await tools.ExecuteAsync("fetch_file", args, ct)
            : check.Message;
    }

    /// <summary>Один маркер чтения задания: code (по коду), url (по ссылке) или title (поиск
    /// по заголовку — возвращается только список найденного, без текстов и чатов).</summary>
    private static async Task<string> ReadOneTaskAsync(string rawArgs, AgentToolset tools,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(rawArgs);
        var root = doc.RootElement;
        // список шаблонов проекта (T-144): у него параметров нет вовсе — «templates» с любым
        // значением. Отдельного маркера чтения заводить незачем: механика та же
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("templates", out _))
        {
            var listArgs = JsonDocument.Parse("{}").RootElement.Clone();
            var listCheck = tools.Authorize("list_templates", listArgs);
            return listCheck.Decision == SecurityDecision.Allow
                ? await tools.ExecuteAsync("list_templates", listArgs, ct)
                : listCheck.Message;
        }
        // РОДИТЕЛЬ, СОСЕДИ И ПОТОМКИ маркером (T-34-S0). Родительский блок и блок соседей
        // подставляются в промпт первым ходом, но задача может их не получить (галочки
        // подстановки сняты — T-29-S0, правило безопасности, продолжение сессии), а кода
        // родителя агент не знает НИОТКУДА, кроме самого блока: без этих форм он остался бы
        // без родителя навсегда. Формы живут внутри того же маркера чтения заданий — один
        // разбор и один общий предел запросов
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("parent", out _))
            {
                return await CallSimpleAsync("get_parent_task", tools, ct);
            }
            if (root.TryGetProperty("siblings", out _))
            {
                return await CallSimpleAsync("get_sibling_tasks", tools, ct);
            }
            if (root.TryGetProperty("children", out var children))
            {
                var childArgs = children.ValueKind == JsonValueKind.String
                                && children.GetString()!.Trim().Length > 0
                    ? Args("code", children.GetString()!.Trim())
                    : AgentSession.NoArgs;
                var childCheck = tools.Authorize("get_child_tasks", childArgs);
                return childCheck.Decision == SecurityDecision.Allow
                    ? await tools.ExecuteAsync("get_child_tasks", childArgs, ct)
                    : childCheck.Message;
            }
        }
        var (tool, name, value) =
            Text(root, "code") is { Length: > 0 } code ? ("get_task_by_code", "code", code)
            : Text(root, "url") is { Length: > 0 } url ? ("get_task_by_url", "url", url)
            : Text(root, "title") is { Length: > 0 } title ? ("find_tasks_by_title", "title", title)
            : ("", "", "");
        if (tool.Length == 0)
        {
            return Loc.In(tools.Language, "prompt.cli.32", TaskMarker.TrimEnd(':'));
        }
        var args = Args(name, value);
        var check = tools.Authorize(tool, args);
        if (check.Decision != SecurityDecision.Allow)
        {
            return check.Message;
        }
        var card = await tools.ExecuteAsync(tool, args, ct);
        // поиск по заголовку отдаёт список задач — прикладывать чат не к чему
        if (tool == "find_tasks_by_title")
        {
            return card;
        }
        // чат — по коду из уже прочитанной карточки: по ссылке кода в маркере нет, а задачи
        // «не найдено» карточкой не являются (чат такой задачи запрашивать незачем)
        var taskCode = TaskToolset.CardCode(card);
        if (taskCode is null)
        {
            return card;
        }
        var chatArgs = Args("code", taskCode);
        var chatCheck = tools.Authorize("get_task_chat", chatArgs);
        var chat = chatCheck.Decision == SecurityDecision.Allow
            ? await tools.ExecuteAsync("get_task_chat", chatArgs, ct)
            : chatCheck.Message;
        return card + "\n\n" + chat;
    }

    /// <summary>Вызов инструмента БЕЗ параметров (родитель, соседи) с проверкой правил
    /// безопасности — общий кусок форм маркера чтения заданий (T-34-S0).</summary>
    private static async Task<string> CallSimpleAsync(string tool, AgentToolset tools,
        CancellationToken ct)
    {
        var check = tools.Authorize(tool, AgentSession.NoArgs);
        return check.Decision == SecurityDecision.Allow
            ? await tools.ExecuteAsync(tool, AgentSession.NoArgs, ct)
            : check.Message;
    }

    /// <summary>Аргументы вызова инструмента из одной строковой пары; Clone — чтобы элемент
    /// пережил свой JsonDocument.</summary>
    private static JsonElement Args(string name, string value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(
            new Dictionary<string, string> { [name] = value })).RootElement.Clone();

    private static string Text(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim()
            : "";

    /// <summary>
    /// Вырезать из ответа агента маркеры подзадач (T-125): возвращает JSON-аргументы каждого
    /// маркера в порядке появления, а в <paramref name="cleanedText"/> — ответ без них.
    /// Маркер засчитывается ТОЛЬКО с начала строки: пример маркера в середине предложения
    /// (в том числе в отчёте о самой этой возможности) подзадачей не станет. JSON может
    /// занимать несколько строк; маркер без разбираемого JSON-объекта остаётся в тексте как есть.
    /// </summary>
    public static List<string> ParseSubtaskMarkers(string text, out string cleanedText) =>
        ParseMarkers(SubtaskMarker, text, out cleanedText);

    /// <summary>Тот же разбор для любого маркера с JSON-аргументами: подзадачи (T-125) и
    /// чтение заданий (T-127) отличаются только строкой маркера.</summary>
    public static List<string> ParseMarkers(string marker, string text, out string cleanedText)
    {
        var found = new List<string>();
        var kept = new StringBuilder();
        var pos = 0;
        while (pos < text.Length)
        {
            var markerAt = text.IndexOf(marker, pos, StringComparison.Ordinal);
            if (markerAt < 0)
            {
                break;
            }
            var afterMarker = markerAt + marker.Length;
            // отступ строки уходит вместе с маркером
            var cutFrom = markerAt;
            while (cutFrom > pos && (text[cutFrom - 1] == ' ' || text[cutFrom - 1] == '\t'))
            {
                cutFrom--;
            }
            var tail = text[afterMarker..];
            var braceAt = tail.IndexOf('{');
            string? json = null;
            var jsonChars = 0;
            if ((cutFrom == 0 || text[cutFrom - 1] == '\n')
                && braceAt >= 0 && tail[..braceAt].Trim().Length == 0)
            {
                try
                {
                    // ParseValue читает ровно один JSON-объект — текст после него не мешает
                    var bytes = Encoding.UTF8.GetBytes(tail[braceAt..]);
                    var reader = new Utf8JsonReader(bytes);
                    using var doc = JsonDocument.ParseValue(ref reader);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        json = doc.RootElement.GetRawText();
                        // сколько СИМВОЛОВ занял JSON: байты читателя ≠ символы (кириллица)
                        jsonChars = Encoding.UTF8.GetString(bytes, 0, (int)reader.BytesConsumed).Length;
                    }
                }
                catch (JsonException)
                {
                    // маркер без разбираемого JSON — строка остаётся в тексте ответа
                }
            }
            if (json is null)
            {
                kept.Append(text, pos, afterMarker - pos);
                pos = afterMarker;
                continue;
            }
            found.Add(json);
            kept.Append(text, pos, cutFrom - pos);
            pos = afterMarker + braceAt + jsonChars;
            // хвост строки после JSON (пробелы и перевод строки) — тоже часть маркера
            while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\r'))
            {
                pos++;
            }
            if (pos < text.Length && text[pos] == '\n')
            {
                pos++;
            }
        }
        kept.Append(text, pos, text.Length - pos);
        cleanedText = kept.ToString().TrimEnd();
        return found;
    }

    /// <summary>
    /// Запас после названного CLI времени сброса (T-166): будим агента на минуту позже —
    /// минута в минуту сервер лимита ещё отвечает отказом (у него своё округление времени),
    /// и продолжение сорвалось бы тем же лимитом.
    /// </summary>
    public static readonly TimeSpan LimitGrace = TimeSpan.FromMinutes(1);

    /// <summary>Промпт продолжения после сброса лимита (T-166): работа не начинается заново —
    /// оживает ТА ЖЕ сессия CLI (`--resume &lt;session_id&gt;`), а в ней достаточно одного слова.
    /// Язык — язык команды задачи (T-190).</summary>
    public static string ContinuePrompt(string? language = null) =>
        Loc.In(language, "prompt.cli.33");

    /// <summary>Исчерпан лимит подписки по сообщению самого CLI (T-121): исполнитель будет
    /// помечен занятым до момента сброса, а задача — ждать этого момента (AiConnectorBase).
    /// Времени в сообщении нет — «занят до» ставится на час (ТЗ v1.37). Известен id сессии —
    /// работа не начнётся заново: та же сессия продолжится словом «Continue» (T-166).</summary>
    private static ProviderLimitException LimitException(string cliMessage, DateTime? resetAt,
        string sessionId = "")
    {
        // будим на минуту позже названного времени (T-166)
        var at = resetAt is { } reset ? reset + LimitGrace : (DateTime?)null;
        var canContinue = sessionId.Trim().Length > 0;
        return new ProviderLimitException(
            Loc.T("msg.claudeCliConnector.13", cliMessage) +
            (at is { } until
                ? Loc.T("msg.claudeCliConnector.14", (until - LimitGrace).ToLocalTime())
                : Loc.T("msg.claudeCliConnector.15")) +
            (canContinue
                ? Loc.T("msg.claudeCliConnector.16")
                : Loc.T("msg.claudeCliConnector.17")),
            at,
            limitPercent: null,
            contextJson: canContinue
                ? JsonSerializer.Serialize(new CliContextDto
                {
                    Provider = ProviderKind,
                    SessionId = sessionId.Trim(),
                }, new JsonSerializerOptions { WriteIndented = true })
                : null);
    }

    /// <summary>
    /// СЕАНС CLI ИСТЁК ЛИБО ВХОДА НЕТ (todo96): задача не «встанет с ошибкой», а уйдёт
    /// в паузу с предложением войти — вход делается прямо в AI2P (Настройки → Модели →
    /// «Вход в Claude CLI»). Известен id сессии — после входа работа продолжится ТОЙ ЖЕ
    /// сессией (`--resume`), а не начнётся заново, ровно как после сброса лимита (T-166).
    /// </summary>
    private static ProviderAuthException AuthException(string cliMessage, string sessionId = "")
    {
        var canContinue = sessionId.Trim().Length > 0;
        return new ProviderAuthException(
            Loc.T("msg.claudeCliConnector.26", cliMessage) +
            (canContinue
                ? Loc.T("msg.claudeCliConnector.27")
                : Loc.T("msg.claudeCliConnector.28")),
            canContinue
                ? JsonSerializer.Serialize(new CliContextDto
                {
                    Provider = ProviderKind,
                    SessionId = sessionId.Trim(),
                }, new JsonSerializerOptions { WriteIndented = true })
                : null);
    }

    /// <summary>Разбор итогового JSON CLI, когда он мог и не прийти (ошибочный исход): null —
    /// вывод не JSON. Нужен, чтобы у отказа по лимиту взять id сессии и человеческий текст
    /// причины, а не весь JSON целиком (T-166).</summary>
    private static CliOutput? TryParseCliOutput(string stdout, string fallbackModel)
    {
        try
        {
            return ParseCliOutput(stdout, fallbackModel);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Человеческий текст причины отказа: поле result итогового JSON («You've hit your
    /// session limit · resets 9:20pm (Europe/Moscow)», «Failed to authenticate: …»), иначе —
    /// вывод как есть (T-166; отказ по входу — todo96).</summary>
    private static string FailureText(CliOutput? parsed, string fallback) =>
        FirstNonEmpty(parsed?.Text ?? "", fallback);

    /// <summary>
    /// Найти в ответе агента маркер вопроса `AI2P_QUESTION: {"question": "...", "options": [...]}`.
    /// JSON разбирается с первого «{» после маркера, хвост (закрывающий код-блок и т.п.)
    /// игнорируется; невалидный JSON — вопросом считается остаток строки после маркера.
    /// </summary>
    public static bool TryParseQuestionMarker(string text, out string question, out List<string> options)
    {
        question = "";
        options = [];
        var markerAt = text.LastIndexOf(QuestionMarker, StringComparison.Ordinal);
        if (markerAt < 0)
        {
            return false;
        }
        var tail = text[(markerAt + QuestionMarker.Length)..];
        var braceAt = tail.IndexOf('{');
        if (braceAt >= 0)
        {
            try
            {
                // ParseValue читает ровно один JSON-объект, текст после него не мешает
                var bytes = Encoding.UTF8.GetBytes(tail[braceAt..]);
                var reader = new Utf8JsonReader(bytes);
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                if (root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(q.GetString()))
                {
                    question = q.GetString()!.Trim();
                    if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        options = opts.EnumerateArray()
                            .Where(o => o.ValueKind == JsonValueKind.String)
                            .Select(o => o.GetString()!.Trim())
                            .Where(o => o.Length > 0)
                            .ToList();
                    }
                    return true;
                }
            }
            catch (JsonException)
            {
                // JSON не разобрался — фолбэк на текст строки ниже
            }
        }
        // фолбэк: вопросом считается остаток строки с маркером
        var lineEnd = tail.IndexOf('\n');
        question = (lineEnd < 0 ? tail : tail[..lineEnd]).Trim().TrimEnd('`').Trim();
        return question.Length > 0;
    }

    /// <summary>Проба: CLI установлен и запускается (`claude --version`), а сеанс входа жив
    /// (`claude auth status --json`, todo96). До 1.96 вход не проверялся вовсе — «бесплатной
    /// команды нет», — и протухший сеанс обнаруживался только падением задания. Старый CLI
    /// команды auth не знает: тогда состояние входа неизвестно и пробу это не рушит.</summary>
    protected override async Task<string?> ProbeAsync(ModelProfile profile, string apiKey, CancellationToken ct)
    {
        var (fileName, baseArgs) = LocalModelProcessService.SplitCommand(
            profile.CliCommand.Trim().Length > 0 ? profile.CliCommand.Trim() : DefaultCommand);
        try
        {
            Logger.Information("Проба: Claude CLI {FileName} --version", fileName);
            var (exitCode, stdout, stderr) = await RunProcessAsync(
                fileName, (baseArgs + " --version").Trim(), Environment.CurrentDirectory, stdin: null, ct);
            if (exitCode == 0)
            {
                Logger.Information("Проба: Claude CLI {Version}", stdout.Trim());
                var auth = await ClaudeCliAuth.StatusAsync(profile.CliCommand, ct);
                return auth is { Known: true, LoggedIn: false }
                    ? Loc.T("msg.claudeCliConnector.29")
                    : null;
            }
            return Loc.T("msg.claudeCliConnector.23", exitCode,
                FirstNonEmpty(stderr, stdout, Loc.T("msg.claudeCliConnector.4")));
        }
        catch (OperationCanceledException)
        {
            return Loc.T("msg.claudeCliConnector.18");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проба: Claude CLI {FileName} — исключение", fileName);
            return Loc.T("msg.claudeCliConnector.19", fileName, ex.Message);
        }
    }

    /// <summary>
    /// Итоговый JSON headless-запуска: {"type":"result","subtype":"success","result":"…",
    /// "usage":{…},"modelUsage":{"модель":{…}},"is_error":false}. Во входные токены входят
    /// и кэшированные (cache_read/cache_creation) — для честного учёта в биллинге.
    /// </summary>
    public static CliOutput ParseCliOutput(string stdout, string fallbackModel)
    {
        // штатно stdout — один JSON-объект; на всякий случай отрезаем возможный шум до «{»
        var start = stdout.IndexOf('{');
        if (start < 0)
        {
            throw new InvalidOperationException(Loc.T("msg.claudeCliConnector.24",
                FirstNonEmpty(stdout.Trim(), Loc.T("msg.claudeCliConnector.25"))));
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(stdout[start..]);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Loc.T("msg.claudeCliConnector.20", ex.Message));
        }
        using (doc)
        {
            var root = doc.RootElement;
            var text = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()!
                : "";
            var stopReason = root.TryGetProperty("subtype", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString()!
                : "";
            var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            var sessionId = root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String
                ? sid.GetString()!.Trim()
                : "";

            long inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = Long(usage, "input_tokens")
                              + Long(usage, "cache_creation_input_tokens")
                              + Long(usage, "cache_read_input_tokens");
                outputTokens = Long(usage, "output_tokens");
            }

            // modelUsage содержит и служебные модели CLI (классификатор haiku и т.п.) —
            // берём модель профайла, а если её нет в списке, то модель с наибольшим
            // объёмом токенов (фактически выполнявшую задание)
            var model = fallbackModel;
            if (root.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object
                && !mu.TryGetProperty(fallbackModel, out _))
            {
                var main = mu.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                    .OrderByDescending(p => Long(p.Value, "inputTokens")
                                            + Long(p.Value, "cacheReadInputTokens")
                                            + Long(p.Value, "outputTokens"))
                    .FirstOrDefault();
                if (main.Value.ValueKind == JsonValueKind.Object)
                {
                    model = main.Name;
                }
            }
            return new CliOutput(text, inputTokens, outputTokens, model, stopReason, isError, sessionId);
        }
    }

    private static long Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt64(out var value) ? value : 0;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => v.Trim().Length > 0)?.Trim() ?? "";

    /// <summary>Запуск CLI-процесса: stdin — промпт (UTF-8), stdout/stderr вычитываются
    /// полностью (без дедлока на буферах), отмена — kill всего дерева процессов. Сам запуск —
    /// общий с входом в CLI из AI2P (<see cref="CliProcess"/>, todo96).</summary>
    private static Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, string arguments, string workDir, string? stdin, CancellationToken ct,
        IReadOnlyDictionary<string, string>? env = null) =>
        CliProcess.RunAsync(fileName, arguments, workDir, stdin, ct, env);

    /// <summary>
    /// Переменные окружения процесса CLI (T-34-S0): адрес сервера ПО ПЕТЛЕ, токен задания,
    /// код задачи и язык общения. Больше их взять неоткуда — до T-34-S0 окружение процессу
    /// не задавалось вовсе. null — клиента заданию не выдаём (сессии нет либо сервер не
    /// поднят): тогда и разрешение на запуск команды не нужно.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? AgentProcessEnv(AgentToolset tools)
    {
        if (tools.Session is not { } session
            || AgentSessionRegistry.LocalBaseUrl.Trim() is not { Length: > 0 } baseUrl
            || AgentCli.WrapperPath() is null)
        {
            return null;
        }
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AgentCli.UrlVar] = baseUrl,
            [AgentCli.TokenVar] = session.Token,
            [AgentCli.TaskVar] = session.TaskCode,
            [AgentCli.LangVar] = tools.Language ?? Loc.Lang,
        };
    }
}
