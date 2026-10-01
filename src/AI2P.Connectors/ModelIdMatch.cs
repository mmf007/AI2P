namespace AI2P.Connectors;

/// <summary>
/// СВЕРКА ЗАПРОШЕННОЙ И ФАКТИЧЕСКОЙ МОДЕЛИ (T-359-S0). Запись справочника называет модель
/// полем <c>model</c> профайла, а провайдер возвращает ту, что реально считала ответ — и это
/// не всегда одно и то же: у Claude Code CLI подписка без доступа к старшей модели, у API —
/// датированный псевдоним. Молчаливая подмена страшна тем, что запись «Claude-Opus-5.5_cli»,
/// работающая на Sonnet, от исправной неотличима.
///
/// Совпадением считаются три случая (проверено живым запуском CLI 24.09.2026):
/// <list type="number">
/// <item>полное равенство (без учёта регистра) — <c>claude-sonnet-5</c> → <c>claude-sonnet-5</c>;</item>
/// <item>уточнение датой или ревизией — <c>claude-haiku-4-5</c> → <c>claude-haiku-4-5-20251001</c>;</item>
/// <item>АЛИАС последней версии — <c>fable</c> → <c>claude-fable-5-1</c>, <c>opus</c> →
/// <c>claude-opus-5</c>. Алиас на то и алиас, что означает «последняя», поэтому подменой
/// это не считается; зато и записи справочника алиасом называть не стоит — с выходом
/// следующей версии он молча переедет.</item>
/// </list>
/// </summary>
public static class ModelIdMatch
{
    /// <summary>Алиасы Claude Code CLI (`claude --help`: «alias of the latest model»).</summary>
    private static readonly string[] Aliases = ["fable", "opus", "sonnet", "haiku"];

    /// <summary>Совпала ли фактическая модель с запрошенной. Пустая запрошенная («модель
    /// выбирает сам CLI») и пустая фактическая («провайдер не сказал») — не повод ругаться.</summary>
    public static bool Matches(string? requested, string? actual)
    {
        var want = (requested ?? "").Trim();
        var got = (actual ?? "").Trim();
        if (want.Length == 0 || got.Length == 0)
        {
            return true;
        }
        if (string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // уточнение версии суффиксом: запрошенное имя целиком в начале фактического
        if (got.StartsWith(want + "-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // алиас последней версии: «opus» → «claude-opus-5», «fable» → «claude-fable-5-1»
        if (Aliases.Contains(want, StringComparer.OrdinalIgnoreCase))
        {
            return got.StartsWith("claude-" + want + "-", StringComparison.OrdinalIgnoreCase)
                   || got.Contains("-" + want + "-", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
