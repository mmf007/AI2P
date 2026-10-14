using System.Globalization;
using System.Text;

namespace AI2P.Connectors;

/// <summary>Одна запись календарной ленты (T-422-S0): задача со сроком.</summary>
/// <param name="TaskId">id задачи — уходит в UID.</param>
/// <param name="Summary">Заголовок записи: номер и заголовок задачи.</param>
/// <param name="Description">Пояснение: проект, состояние.</param>
/// <param name="Link">Ссылка на карточку.</param>
/// <param name="Due">Срок (UTC).</param>
/// <param name="Updated">Когда задача последний раз менялась — из неё SEQUENCE.</param>
public sealed record IcsItem(string TaskId, string Summary, string Description, string Link,
    DateTime Due, DateTime Updated);

/// <summary>
/// iCalendar для телефона (T-422-S0, пп. Б1–Б2 плана T-412-S0). VTODO из письма заводят
/// только Outlook и Thunderbird, а календари телефонов из коробки понимают СОБЫТИЕ (VEVENT)
/// с напоминанием VALARM и ПОДПИСКУ на ленту — поэтому срок задачи уходит событием:
/// время = срок, UID = id задачи (повтор обновляет, а не дублирует), SEQUENCE растёт со
/// временем изменения, отменённая задача — METHOD:CANCEL.
/// </summary>
public static class IcsCalendar
{
    /// <summary>Длительность события «срок»: календарю нужен интервал, а не точка.</summary>
    private const int EventMinutes = 15;

    /// <summary>Вложение вида «почта событие»: один VEVENT по сроку задачи.</summary>
    public static string BuildEvent(NotificationMessage message)
    {
        var sb = new StringBuilder();
        Begin(sb, message.Cancelled ? "CANCEL" : "PUBLISH");
        var due = message.Due ?? DateTime.UtcNow;
        AppendEvent(sb, message.TaskId, message.Subject, message.Body, message.TaskLink, due,
            DateTime.UtcNow, message.LeadMinutes, message.Cancelled);
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    /// <summary>Личная лента (<c>/notify/ical/&lt;токен&gt;.ics</c>): события по срокам и,
    /// по желанию, задачи VTODO.</summary>
    public static string BuildFeed(string name, IEnumerable<IcsItem> items, int leadMinutes, bool withTodo)
    {
        var sb = new StringBuilder();
        Begin(sb, "PUBLISH");
        sb.Append("X-WR-CALNAME:").Append(Escape(name)).Append("\r\n");
        // подсказка клиенту, как часто перечитывать (Google её игнорирует — раз в несколько часов)
        sb.Append("REFRESH-INTERVAL;VALUE=DURATION:PT30M\r\n");
        sb.Append("X-PUBLISHED-TTL:PT30M\r\n");
        foreach (var item in items)
        {
            AppendEvent(sb, item.TaskId, item.Summary, item.Description, item.Link, item.Due,
                item.Updated, leadMinutes, cancelled: false);
            if (withTodo)
            {
                sb.Append("BEGIN:VTODO\r\n");
                sb.Append("UID:").Append(Escape(item.TaskId)).Append("-todo@ai2p\r\n");
                sb.Append("DTSTAMP:").Append(Stamp(item.Updated)).Append("\r\n");
                sb.Append("SUMMARY:").Append(Escape(item.Summary)).Append("\r\n");
                if (item.Link.Length > 0)
                {
                    sb.Append("URL:").Append(Escape(item.Link)).Append("\r\n");
                }
                sb.Append("DUE:").Append(Stamp(item.Due)).Append("\r\n");
                sb.Append("STATUS:NEEDS-ACTION\r\n");
                sb.Append("END:VTODO\r\n");
            }
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static void Begin(StringBuilder sb, string method)
    {
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//AI2P//Notifications//RU\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:").Append(method).Append("\r\n");
    }

    private static void AppendEvent(StringBuilder sb, string taskId, string summary, string description,
        string link, DateTime due, DateTime updated, int leadMinutes, bool cancelled)
    {
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(Escape(taskId)).Append("-due@ai2p\r\n");
        sb.Append("DTSTAMP:").Append(Stamp(DateTime.UtcNow)).Append("\r\n");
        sb.Append("SEQUENCE:").Append(Sequence(updated)).Append("\r\n");
        sb.Append("DTSTART:").Append(Stamp(due)).Append("\r\n");
        sb.Append("DTEND:").Append(Stamp(due.AddMinutes(EventMinutes))).Append("\r\n");
        sb.Append("SUMMARY:").Append(Escape(summary)).Append("\r\n");
        if (description.Trim().Length > 0)
        {
            sb.Append("DESCRIPTION:").Append(Escape(description)).Append("\r\n");
        }
        if (link.Length > 0)
        {
            sb.Append("URL:").Append(Escape(link)).Append("\r\n");
        }
        sb.Append("STATUS:").Append(cancelled ? "CANCELLED" : "CONFIRMED").Append("\r\n");
        sb.Append("TRANSP:TRANSPARENT\r\n");
        if (!cancelled)
        {
            sb.Append("BEGIN:VALARM\r\n");
            sb.Append("ACTION:DISPLAY\r\n");
            sb.Append("DESCRIPTION:").Append(Escape(summary)).Append("\r\n");
            sb.Append("TRIGGER:-PT").Append(Math.Max(0, leadMinutes)).Append("M\r\n");
            sb.Append("END:VALARM\r\n");
        }
        sb.Append("END:VEVENT\r\n");
    }

    /// <summary>Номер правки события: минуты от 2024 года — растёт с каждым изменением задачи,
    /// поэтому календарь принимает новое письмо (или новую ленту) как обновление.</summary>
    private static long Sequence(DateTime updated) =>
        Math.Max(0, (long)(updated.ToUniversalTime() - new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes);

    internal static string Stamp(DateTime moment) =>
        moment.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Экранирование по RFC 5545: обратная косая, точка с запятой, запятая и перевод строки.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
}
