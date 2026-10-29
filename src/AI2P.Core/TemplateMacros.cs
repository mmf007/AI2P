using System.Text.RegularExpressions;

namespace AI2P.Core;

/// <summary>
/// Макроподстановки в тексте шаблона (T-655-S0): <c>${key}</c>, где key — произвольный текст
/// без «}», как у уведомлений (NotificationField.Marker). Подстановка проводится только для
/// ключа, значение которого содержит хотя бы один знак (пробел тоже знак): пустое или не
/// названное значение оставляет <c>${key}</c> в тексте как есть.
/// </summary>
public static class TemplateMacros
{
    private static readonly Regex Pattern = new(@"\$\{([^}\r\n]+)\}", RegexOptions.Compiled);

    /// <summary>Ключи подстановок в текстах — по первому появлению, без повторов.</summary>
    public static List<string> Find(IEnumerable<string?> texts)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            foreach (Match m in Pattern.Matches(text))
            {
                if (seen.Add(m.Groups[1].Value))
                {
                    keys.Add(m.Groups[1].Value);
                }
            }
        }
        return keys;
    }

    /// <summary>Подставить значения. Подставленный текст повторно не разбирается.</summary>
    public static string Apply(string text, IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }
        return Pattern.Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : m.Value);
    }
}
