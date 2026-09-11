namespace AI2P.Connectors;

/// <summary>
/// Исчерпан лимит ИИ-провайдера (HTTP 429 / rate limit, ТЗ v1.37). Коннектор бросает это
/// исключение вместо общего, чтобы зафиксировать у исполнителя «занят до»: RetryAt — когда
/// лимит отпустит (из Retry-After / заголовков сброса; null — провайдер не сообщил),
/// LimitPercent — % использования лимита, если провайдер его прислал.
/// </summary>
public sealed class ProviderLimitException : InvalidOperationException
{
    public DateTime? RetryAt { get; }

    public double? LimitPercent { get; }

    /// <summary>
    /// Контекст, которым работу можно ПРОДОЛЖИТЬ, когда лимит отпустит (T-166): у Claude CLI
    /// это id сессии — та же сессия оживает командой <c>claude -p --resume &lt;id&gt;</c>, и агент
    /// доделывает начатое вместо запуска задания с нуля. null — продолжать нечем (провайдер
    /// без сессий, id сессии CLI неизвестен): задача просто перезапустится новым заданием.
    /// </summary>
    public string? ContextJson { get; }

    public ProviderLimitException(string message, DateTime? retryAt, double? limitPercent = null,
        string? contextJson = null)
        : base(message)
    {
        RetryAt = retryAt;
        LimitPercent = limitPercent;
        ContextJson = contextJson;
    }
}
