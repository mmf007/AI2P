using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ГОТОВОЕ УВЕДОМЛЕНИЕ (T-272) — то, что осталось доставить: адрес, заголовок и текст уже
/// собраны, макроподстановки раскрыты. Транспорт про правила, задачи и исполнителей ничего
/// не знает — он умеет только доставлять.
/// </summary>
public sealed class NotificationMessage
{
    /// <summary>Правило, по которому уведомление собрано (для журнала и диагностики).</summary>
    public string RuleId { get; set; } = "";
    public string RuleDisplayId { get; set; } = "";

    /// <summary>Вид уведомления: <see cref="NotificationChannels"/>.</summary>
    public string Channel { get; set; } = NotificationChannels.Mail;

    /// <summary>Транспорт вида: <c>email</c>, дальше — мессенджеры.</summary>
    public string Transport { get; set; } = NotificationChannels.EmailTransport;

    /// <summary>Куда: почтовый адрес (у мессенджера здесь будет его адрес).</summary>
    public string Address { get; set; } = "";

    /// <summary>Кому — исполнитель организации (для журнала).</summary>
    public string ExecutorId { get; set; } = "";
    public string ExecutorNick { get; set; } = "";

    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>Задача, о которой уведомление (для журнала и для вложения-задачи).</summary>
    public string TaskId { get; set; } = "";
    public string TaskDisplayId { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public string TaskLink { get; set; } = "";

    /// <summary>Срок задачи (UTC) — уходит во вложение-задачу; null — срок не задан.</summary>
    public DateTime? Due { get; set; }
}

/// <summary>
/// ТРАНСПОРТ УВЕДОМЛЕНИЙ (T-272): кто физически доставляет сообщение. Сегодня он один —
/// электронная почта; задание прямо просит учесть, что дальше добавятся мессенджеры,
/// поэтому отправка уведомлений с транспортом разговаривает только через этот интерфейс,
/// а нужный транспорт выбирается по коду вида (<see cref="NotificationChannels.TransportOf"/>).
///
/// Новый мессенджер — это новый класс с этим интерфейсом, строка в
/// <see cref="NotificationChannels"/> и пункт в форме правила. Ни сбор писем, ни сторож
/// сроков, ни хранилище при этом не трогаются.
/// </summary>
public interface INotificationTransport
{
    /// <summary>Код транспорта: <c>email</c>, в дальнейшем <c>telegram</c> и т. п.</summary>
    string Transport { get; }

    /// <summary>Транспорт настроен и может доставлять. Не настроен — уведомление
    /// не отправляется, а причина пишется в журнал работ.</summary>
    bool Ready { get; }

    /// <summary>Почему транспорт не готов (для журнала и для формы настроек); пусто — готов.</summary>
    string NotReadyReason { get; }

    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}
