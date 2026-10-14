using System.Text.RegularExpressions;

namespace AI2P.Core;

/// <summary>
/// СОБЫТИЕ, ПО КОТОРОМУ ШЛЁТСЯ УВЕДОМЛЕНИЕ (T-272). Код события лежит в записи правила
/// (<c>notification_rules.event_kind</c>) и решает две вещи сразу: когда правило срабатывает
/// и КОМУ уходит письмо — исполнителю задачи либо её ответственному.
/// </summary>
public static class NotificationEvents
{
    /// <summary>Задача переведена в выбранное состояние — письмо ИСПОЛНИТЕЛЮ задачи.
    /// Состояние указывается в самом правиле (<c>status_code</c>).</summary>
    public const string Status = "status";

    /// <summary>Требуется проверка задачи — письмо ОТВЕТСТВЕННОМУ. Состояние выбирать не
    /// нужно: «требуется проверка» — это переход в <see cref="Entities.TaskStatuses.Review"/>.</summary>
    public const string Review = "review";

    /// <summary>Подходит срок — письмо ИСПОЛНИТЕЛЮ задачи за <c>lead_minutes</c> до
    /// <c>due_date</c>. Единственное событие, которое считается сторожем по времени,
    /// а не сменой статуса, — поэтому только у него есть защита от повторной отправки.</summary>
    public const string Due = "due";

    /// <summary>Агент задал вопрос в чате задачи (T-422-S0) — письмо АДРЕСАТУ вопроса
    /// (человеку, которому он адресован, иначе ответственному задачи) со ссылками быстрого
    /// ответа. Статус задачи вопрос не меняет, поэтому событие ловится не сменой статуса,
    /// а появлением вопроса.</summary>
    public const string Question = "question";

    /// <summary>Задание выдано человеку (T-422-S0) — письмо ИСПОЛНИТЕЛЮ-человеку: задание
    /// перешло в «ждёт человека».</summary>
    public const string JobAssigned = "job_assigned";

    public static readonly string[] All = [Status, Review, Due, Question, JobAssigned];

    public static bool IsKnown(string? code) =>
        code is { Length: > 0 } && Array.IndexOf(All, code) >= 0;
}

/// <summary>
/// ВИД УВЕДОМЛЕНИЯ (T-272) — то, что человек выбирает в форме: «почта письмо» и
/// «почта задача». Вид знает свой ТРАНСПОРТ (<see cref="TransportOf"/>) — тот, кто
/// физически доставляет сообщение.
///
/// Разделение сделано на вырост: в дальнейшем к почте добавятся мессенджеры, и это будут
/// НОВЫЕ виды с ДРУГИМ транспортом (<c>telegram</c>, …). Код, который шлёт уведомление,
/// про виды не знает вовсе: он берёт транспорт по коду вида и отдаёт ему сообщение
/// (<c>INotificationTransport</c>), поэтому новый мессенджер — это новая реализация
/// транспорта плюс строка здесь и в словарях.
/// </summary>
public static class NotificationChannels
{
    /// <summary>Почта, обычное письмо.</summary>
    public const string Mail = "mail";

    /// <summary>Почта, ЗАДАЧА: то же письмо плюс вложение-календарь (iCalendar VTODO),
    /// из которого почтовый клиент делает у себя задачу со сроком.</summary>
    public const string MailTask = "mail_task";

    /// <summary>Почта, СОБЫТИЕ КАЛЕНДАРЯ (T-422-S0): письмо с вложением iCalendar VEVENT
    /// (время = срок задачи, напоминание VALARM). В отличие от VTODO событие ставят в календарь
    /// в одно касание и почта iOS, и Gmail, и Outlook на телефоне.</summary>
    public const string MailEvent = "mail_event";

    /// <summary>Web Push в браузер телефона или компьютера (T-422-S0): сервер открыт в браузере,
    /// подписка делается кнопкой в «Мои уведомления». Отдельного приложения не нужно.</summary>
    public const string WebPush = "webpush";

    /// <summary>Транспорт «электронная почта» — общий у почтовых видов.</summary>
    public const string EmailTransport = "email";

    /// <summary>Транспорт Web Push (VAPID, RFC 8030/8291/8292).</summary>
    public const string WebPushTransport = "webpush";

    public static readonly string[] All = [Mail, MailTask, MailEvent, WebPush];

    /// <summary>Вид → транспорт. Новый мессенджер добавляется строкой сюда (и своей
    /// реализацией транспорта) — трогать отправку уведомлений при этом не нужно.</summary>
    private static readonly Dictionary<string, string> Transports = new(StringComparer.OrdinalIgnoreCase)
    {
        [Mail] = EmailTransport,
        [MailTask] = EmailTransport,
        [MailEvent] = EmailTransport,
        [WebPush] = WebPushTransport,
    };

    public static bool IsKnown(string? code) =>
        code is { Length: > 0 } && Array.IndexOf(All, code) >= 0;

    /// <summary>Кто доставляет сообщение этого вида. Неизвестный вид — почта: так уведомление
    /// не потеряется, если код вида приехал репликацией из более новой версии.</summary>
    public static string TransportOf(string? channel) =>
        channel is { Length: > 0 } code && Transports.TryGetValue(code, out var transport)
            ? transport
            : EmailTransport;
}

/// <summary>Одна макроподстановка справочника: код и ключ пояснения в словаре (ТЗ гл. 9).</summary>
/// <param name="Code">Код без обрамления: <c>task.title</c>.</param>
/// <param name="TextKey">Ключ пояснения в i18n: <c>notify.macro.task.title</c>.</param>
public readonly record struct NotificationMacro(string Code, string TextKey)
{
    /// <summary>Как макрос выглядит в тексте: <c>${task.title}</c>.</summary>
    public string Marker => "${" + Code + "}";
}

/// <summary>
/// Значения макроподстановок для одного письма (T-272). Заполняется отправкой; пустая
/// строка — значения нет (у задачи нет проекта, у исполнителя не задано имя): макрос
/// раскрывается в пустоту, а не остаётся «${project.name}» в письме.
/// </summary>
public sealed class NotificationContext
{
    public string TaskId { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public string TaskLink { get; set; } = "";
    public string TaskStatus { get; set; } = "";
    public string TaskDue { get; set; } = "";
    public string PerformerNick { get; set; } = "";
    public string PerformerName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectId { get; set; } = "";
    /// <summary>Текст вопроса агента (событие «вопрос», T-422-S0).</summary>
    public string QuestionText { get; set; } = "";
    /// <summary>Ссылка быстрого ответа на вопрос — страница без входа (T-422-S0).</summary>
    public string ReplyLink { get; set; } = "";
    /// <summary>Варианты ответа по строке: «вариант: ссылка» (T-422-S0).</summary>
    public string ReplyOptions { get; set; } = "";
}

/// <summary>
/// МАКРОПОДСТАНОВКИ текстов уведомления (T-272): <c>${task.title}</c> и т. п. Раскрываются
/// в МОМЕНТ ОТПРАВКИ, поэтому правка шаблона действует на следующее же письмо.
///
/// Коды взяты из задания. Две правки, оговорённые заданием («если будет удобно, коды можно
/// поменять»):
/// <list type="bullet">
/// <item>проект пишется правильно — <c>${project.name}</c>, <c>${project.id}</c>; написание
/// из задания (<c>progect</c>) принимается как синоним, чтобы уже написанный по заданию
/// текст не оказался сломанным;</item>
/// <item>к списку добавлены <c>${task.status}</c> и <c>${task.due}</c>: без срока письмо
/// «подходит срок» бессмысленно, а без состояния — письмо о смене состояния.</item>
/// </list>
///
/// Неизвестный макрос НЕ трогается: в письме он останется как есть — так видно опечатку,
/// а не молча пропавший кусок текста.
/// </summary>
public static class NotificationMacros
{
    /// <summary>Справочник для кнопки в редакторе — порядок тот же, что в задании.</summary>
    public static readonly NotificationMacro[] Catalog =
    [
        new("task.id", "notify.macro.task.id"),
        new("task.title", "notify.macro.task.title"),
        new("task.link", "notify.macro.task.link"),
        new("task.status", "notify.macro.task.status"),
        new("task.due", "notify.macro.task.due"),
        new("performer.nik", "notify.macro.performer.nik"),
        new("performer.name", "notify.macro.performer.name"),
        new("company.name", "notify.macro.company.name"),
        new("company.id", "notify.macro.company.id"),
        new("project.name", "notify.macro.project.name"),
        new("project.id", "notify.macro.project.id"),
        new("question.text", "notify.macro.question.text"),
        new("reply.link", "notify.macro.reply.link"),
        new("reply.options", "notify.macro.reply.options"),
    ];

    /// <summary>Синонимы: слева написание из задания, справа канонический код.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["progect.name"] = "project.name",
        ["progect.id"] = "project.id",
        ["performer.nick"] = "performer.nik",
    };

    /// <summary>Макрос в тексте: <c>${код}</c>. Код — буквы, цифры, точка и подчёркивание.</summary>
    private static readonly Regex Pattern = new(@"\$\{([A-Za-z0-9._]+)\}", RegexOptions.Compiled);

    /// <summary>Значения по кодам для одного письма.</summary>
    public static Dictionary<string, string> Values(NotificationContext context) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["task.id"] = context.TaskId,
            ["task.title"] = context.TaskTitle,
            ["task.link"] = context.TaskLink,
            ["task.status"] = context.TaskStatus,
            ["task.due"] = context.TaskDue,
            ["performer.nik"] = context.PerformerNick,
            ["performer.name"] = context.PerformerName,
            ["company.name"] = context.CompanyName,
            ["company.id"] = context.CompanyId,
            ["project.name"] = context.ProjectName,
            ["project.id"] = context.ProjectId,
            ["question.text"] = context.QuestionText,
            ["reply.link"] = context.ReplyLink,
            ["reply.options"] = context.ReplyOptions,
        };

    /// <summary>Раскрыть макросы в тексте. Неизвестный код остаётся в тексте как есть.</summary>
    public static string Expand(string? template, NotificationContext context)
    {
        if (template is null or "")
        {
            return "";
        }
        var values = Values(context);
        return Pattern.Replace(template, match =>
        {
            var code = match.Groups[1].Value;
            if (Aliases.TryGetValue(code, out var canonical))
            {
                code = canonical;
            }
            return values.TryGetValue(code, out var value) ? value : match.Value;
        });
    }
}

/// <summary>
/// ЛИЧНЫЕ НАСТРОЙКИ УВЕДОМЛЕНИЙ человека (T-422-S0, п. В1 плана T-412-S0). Правила
/// организации (<c>notification_rules</c>) — инструмент администратора; эта запись решает,
/// что человек получает САМ, без правил: готовые наборы, канал и тихие часы. Строки нет —
/// действуют умолчания: включён только набор «мои вопросы и задания», канал «оба».
/// </summary>
public sealed class NotificationPrefs
{
    public string ExecutorId { get; set; } = "";
    /// <summary>«Мои вопросы и задания — сразу»: вопрос агента мне и задание мне.</summary>
    public bool Inbox { get; set; } = true;
    /// <summary>«Мои сроки — в календарь»: событие календаря по сроку моей задачи.</summary>
    public bool Due { get; set; }
    /// <summary>За сколько минут до срока напоминать (набор «сроки»).</summary>
    public int DueLeadMinutes { get; set; } = 60;
    /// <summary>«Проверка — мне как ответственному».</summary>
    public bool Review { get; set; }
    /// <summary>Канал: <see cref="NotificationPrefChannels"/>.</summary>
    public string Channel { get; set; } = NotificationPrefChannels.Both;
    /// <summary>Тихие часы «ЧЧ:мм» по времени сервера; пусто — без тихих часов. В тихие
    /// часы push не шлётся (письмо уходит: оно не будит).</summary>
    public string QuietFrom { get; set; } = "";
    public string QuietTo { get; set; } = "";
    /// <summary>Секрет личной календарной ленты (<c>/notify/ical/&lt;токен&gt;.ics</c>);
    /// пусто — лента не заведена.</summary>
    public string IcalToken { get; set; } = "";
    /// <summary>Класть в ленту задачи (VTODO) рядом с событиями.</summary>
    public bool IcalTodo { get; set; }

    public bool WantsMail => Channel is NotificationPrefChannels.Mail or NotificationPrefChannels.Both;
    public bool WantsPush => Channel is NotificationPrefChannels.Push or NotificationPrefChannels.Both;

    /// <summary>Момент <paramref name="localTime"/> попадает в тихие часы. Интервал может
    /// переходить через полночь (22:00–07:00).</summary>
    public bool IsQuiet(TimeOnly localTime)
    {
        if (!TimeOnly.TryParse(QuietFrom, System.Globalization.CultureInfo.InvariantCulture, out var from)
            || !TimeOnly.TryParse(QuietTo, System.Globalization.CultureInfo.InvariantCulture, out var to)
            || from == to)
        {
            return false;
        }
        return from < to
            ? localTime >= from && localTime < to
            : localTime >= from || localTime < to;
    }
}

/// <summary>Канал личных уведомлений (T-422-S0).</summary>
public static class NotificationPrefChannels
{
    public const string Mail = "mail";
    public const string Push = "push";
    public const string Both = "both";
    public const string None = "none";

    public static readonly string[] All = [Both, Push, Mail, None];

    public static bool IsKnown(string? code) =>
        code is { Length: > 0 } && Array.IndexOf(All, code) >= 0;
}
