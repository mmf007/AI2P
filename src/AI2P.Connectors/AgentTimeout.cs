using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ТАЙМАУТ ОТВЕТА ИСПОЛНИТЕЛЯ (T-124). Сколько ждать ответа модели, прежде чем считать вызов
/// зависшим: до T-124 значение было зашито в коде каждого коннектора (Claude CLI — 30 мин,
/// чат-API — 10 мин), и молчащая долгая модель обрывалась на полпути. Теперь это поле формы
/// исполнителя (<c>Executor.ResponseTimeoutMinutes</c>):
///
/// <list type="bullet">
/// <item>пусто — умолчание системы <see cref="Default"/> (30 мин), у медиа-моделей —
///       params.timeoutMinutes профайла (ComfyUI);</item>
/// <item>0 — БЕЗ ОГРАНИЧЕНИЯ: ждём столько, сколько модель будет считать (генерация видео
///       спокойно занимает час и всё это время молчит);</item>
/// <item>число — ровно столько минут.</item>
/// </list>
/// </summary>
public static class AgentTimeout
{
    /// <summary>Умолчание системы, когда у исполнителя поле не заполнено (T-124).</summary>
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Таймаут вызова по значению из формы исполнителя; null — ждать без ограничения.
    /// </summary>
    /// <param name="minutes">Поле исполнителя: null — умолчание, 0 (и меньше) — без ограничения.</param>
    /// <param name="fallback">Умолчание коннектора, если поле не заполнено; null — <see cref="Default"/>.</param>
    public static TimeSpan? Of(int? minutes, TimeSpan? fallback = null) => minutes switch
    {
        null => fallback ?? Default,
        <= 0 => (TimeSpan?)null,
        int m => TimeSpan.FromMinutes(m),
    };

    /// <summary>Текст таймаута для логов и сообщений об ошибке: «01:30:00» либо «без ограничения».</summary>
    public static string Describe(TimeSpan? timeout) =>
        timeout is { } limit ? $"{limit:hh\\:mm\\:ss}" : Loc.T("msg.agentTimeout.1");
}
