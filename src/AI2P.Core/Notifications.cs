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

    public static readonly string[] All = [Status, Review, Due];

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

    /// <summary>Транспорт «электронная почта» — общий у обоих сегодняшних видов.</summary>
    public const string EmailTransport = "email";

    public static readonly string[] All = [Mail, MailTask];

    /// <summary>Вид → транспорт. Новый мессенджер добавляется строкой сюда (и своей
    /// реализацией транспорта) — трогать отправку уведомлений при этом не нужно.</summary>
    private static readonly Dictionary<string, string> Transports = new(StringComparer.OrdinalIgnoreCase)
    {
        [Mail] = EmailTransport,
        [MailTask] = EmailTransport,
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
