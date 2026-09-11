namespace AI2P.Connectors;

/// <summary>
/// Провайдер не пустил: сеанс входа истёк или его не было (todo96, версия 1.96).
/// Коннектор бросает это исключение вместо общего, чтобы задача НЕ вставала «с ошибкой»:
/// человеку надо всего лишь войти заново, и работа продолжится с того же места.
///
/// Отличие от <see cref="ProviderLimitException"/> — момента, когда «отпустит», не существует:
/// ждать нечего, нужно действие человека. Поэтому задача уходит в паузу с пометкой «ждёт
/// входа», а отпускает её сам вход из AI2P (<see cref="ClaudeLoginService"/>).
/// </summary>
public sealed class ProviderAuthException : InvalidOperationException
{
    /// <summary>
    /// Контекст, которым работу можно ПРОДОЛЖИТЬ после входа: у Claude CLI это id сессии —
    /// та же сессия оживает командой <c>claude -p --resume &lt;id&gt;</c>. null — продолжать
    /// нечем, задача просто перезапустится новым заданием.
    /// </summary>
    public string? ContextJson { get; }

    public ProviderAuthException(string message, string? contextJson = null)
        : base(message)
    {
        ContextJson = contextJson;
    }
}
