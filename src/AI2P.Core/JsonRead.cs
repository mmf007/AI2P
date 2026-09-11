using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// Чтение необязательных полей JSON-настроек (T-13-S1): поля нет, оно другого вида или
/// пустое — берётся значение по умолчанию. Настройки правит человек руками (профайл
/// модели — json-файл), поэтому разбор обязан переживать любую неполноту без исключений.
/// </summary>
public static class JsonRead
{
    public static string Str(JsonElement obj, string name, string fallback = "") =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        v.GetString() is { } s && s.Trim().Length > 0
            ? s.Trim()
            : fallback;

    /// <summary>
    /// СТРОКА КАК ЕСТЬ, БЕЗ ОБРЕЗКИ ПРОБЕЛОВ (T-120-S0). <see cref="Str"/> обрезает края
    /// намеренно — путь или код, набранный человеком с лишним пробелом, обязан работать. Но у
    /// поля, ГДЕ ПРОБЕЛ И ЕСТЬ ЗНАЧЕНИЕ, обрезка тихо портит смысл: разделитель элементов
    /// массива <c>",\n"</c> приезжал из манифеста как <c>","</c>, и JSON-шлюзы (OTIO, .osp)
    /// собирали файл в одну строку — годный, но нечитаемый, а проверка эталона краснела не
    /// на своём дефекте. Такие поля читаются этим методом.
    /// </summary>
    public static string Exact(JsonElement obj, string name, string fallback = "") =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        v.GetString() is { Length: > 0 } s
            ? s
            : fallback;

    public static bool Bool(JsonElement obj, string name, bool fallback) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            }
            : fallback;

    public static int Int(JsonElement obj, string name, int fallback) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out var value)
            ? value
            : fallback;

    public static double Num(JsonElement obj, string name, double fallback) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out var value)
            ? value
            : fallback;

    public static List<string> Strings(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .ToList()
            : [];
}
