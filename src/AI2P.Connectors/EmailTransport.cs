using AI2P.Core;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace AI2P.Connectors;

/// <summary>
/// НАСТРОЙКИ ПОЧТЫ (T-272) — уровня СЕРВЕРА, а не организации: почтовый сервер, через
/// который уходят письма, принадлежит компьютеру (config.json, раздел <c>mail</c>), как
/// адрес и каталоги. Пароль в config.json не лежит — он в хранилище секретов по ссылке
/// <see cref="PasswordRef"/> (подкаталог <c>secrets/</c>, ТЗ гл. 10).
/// </summary>
/// <param name="Host">Сервер SMTP; пусто — почта не настроена, уведомления не уходят.</param>
/// <param name="Port">Порт SMTP (587 — STARTTLS, 465 — SSL, 25 — без шифрования).</param>
/// <param name="UseSsl">Шифровать соединение (STARTTLS / SSL).</param>
/// <param name="User">Логин SMTP; пусто — сервер без авторизации.</param>
/// <param name="PasswordRef">Ссылка на пароль в хранилище секретов.</param>
/// <param name="From">Адрес отправителя; пусто — берётся <paramref name="User"/>.</param>
/// <param name="FromName">Имя отправителя в письме.</param>
public sealed record MailOptions(
    string Host,
    int Port,
    bool UseSsl,
    string User,
    string PasswordRef,
    string From,
    string FromName)
{
    /// <summary>Ссылка на пароль SMTP по умолчанию — файл <c>secrets/mail.password.json</c>.</summary>
    public const string DefaultPasswordRef = "mail.password";

    /// <summary>Почта не настроена — сервер не указан.</summary>
    public bool Empty => Host.Trim().Length == 0;

    /// <summary>Адрес отправителя: указанный явно, иначе логин SMTP.</summary>
    public string Sender => From.Trim().Length > 0 ? From.Trim() : User.Trim();
}

/// <summary>
/// ДОСТАВКА УВЕДОМЛЕНИЙ ПОЧТОЙ (T-272) — транспорт для видов «почта письмо» и «почта задача».
///
/// Оба вида — одно и то же письмо; «почта задача» отличается вложением-календарём
/// (iCalendar VTODO), из которого почтовый клиент делает у себя ЗАДАЧУ со сроком и ссылкой.
/// Отдельного протокола у «задачи» не существует, а VTODO понимают и Outlook, и Thunderbird,
/// и почта Apple — поэтому вид сделан так, а не письмом с другим заголовком.
///
/// Берётся <see cref="SmtpClient"/> из .NET: в поставку не добавляется ни одной зависимости,
/// а от почтового сервера нам нужны только STARTTLS/SSL и логин с паролем.
/// </summary>
public sealed class EmailTransport : INotificationTransport
{
    private readonly Func<MailOptions> _options;
    private readonly SecretStore _secrets;

    public EmailTransport(Func<MailOptions> options, SecretStore secrets)
    {
        _options = options;
        _secrets = secrets;
    }

    public string Transport => NotificationChannels.EmailTransport;

    public bool Ready => NotReadyReason.Length == 0;

    public string NotReadyReason
    {
        get
        {
            var options = _options();
            if (options.Empty)
            {
                return Loc.T("msg.notify.10");
            }
            return options.Sender.Length == 0 ? Loc.T("msg.notify.12") : "";
        }
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var options = _options();
        if (NotReadyReason is { Length: > 0 } reason)
        {
            throw new InvalidOperationException(reason);
        }
        if (message.Address.Trim().Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.notify.11", message.ExecutorNick));
        }
        using var client = new SmtpClient(options.Host.Trim(), options.Port)
        {
            EnableSsl = options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (options.User.Trim().Length > 0)
        {
            // пароль лежит в хранилище секретов (ТЗ гл. 10) — в config.json его нет
            client.Credentials = new NetworkCredential(options.User.Trim(),
                _secrets.Read(options.PasswordRef) ?? "");
            client.UseDefaultCredentials = false;
        }
        using var mail = new MailMessage
        {
            From = options.FromName.Trim().Length > 0
                ? new MailAddress(options.Sender, options.FromName.Trim(), Encoding.UTF8)
                : new MailAddress(options.Sender),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.To.Add(message.Address.Trim());
        if (message.Channel == NotificationChannels.MailTask)
        {
            // вид «почта задача»: то же письмо плюс вложение-календарь — почтовый клиент
            // заводит по нему задачу со сроком и ссылкой на карточку AI2P
            var ics = Encoding.UTF8.GetBytes(BuildTodo(message));
            var attachment = new Attachment(new MemoryStream(ics),
                new ContentType("text/calendar; method=REQUEST; charset=utf-8"))
            {
                Name = "task.ics",
            };
            mail.Attachments.Add(attachment);
        }
        await client.SendMailAsync(mail, ct);
    }

    /// <summary>
    /// Вложение вида «почта задача»: iCalendar с одним VTODO. Идентификатор задачи AI2P
    /// становится UID — повторное письмо по той же задаче почтовый клиент считает
    /// обновлением, а не второй задачей.
    /// </summary>
    public static string BuildTodo(NotificationMessage message)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//AI2P//Notifications//RU\r\n");
        sb.Append("METHOD:REQUEST\r\n");
        sb.Append("BEGIN:VTODO\r\n");
        sb.Append("UID:").Append(Escape(message.TaskId)).Append("@ai2p\r\n");
        sb.Append("DTSTAMP:").Append(Stamp(DateTime.UtcNow)).Append("\r\n");
        sb.Append("SUMMARY:").Append(Escape(message.Subject)).Append("\r\n");
        if (message.Body.Trim().Length > 0)
        {
            sb.Append("DESCRIPTION:").Append(Escape(message.Body)).Append("\r\n");
        }
        if (message.TaskLink.Length > 0)
        {
            sb.Append("URL:").Append(Escape(message.TaskLink)).Append("\r\n");
        }
        if (message.Due is { } due)
        {
            sb.Append("DUE:").Append(Stamp(due)).Append("\r\n");
        }
        sb.Append("STATUS:NEEDS-ACTION\r\n");
        sb.Append("END:VTODO\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Stamp(DateTime moment) =>
        moment.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Экранирование по RFC 5545: обратная косая, точка с запятой, запятая и перевод строки.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
}
