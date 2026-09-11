using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// JSON для человека: кириллица и прочие не-ASCII символы без \uXXXX-эскейпов
/// (журнал событий, поле «Детали» истории — todo_bugfix_1).
/// </summary>
public static class JsonText
{
    /// <summary>Опции сериализации без эскейпа не-ASCII (HTML-экранирование делает Blazor при выводе).</summary>
    public static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Перекодирует JSON-строку так, чтобы \uXXXX-эскейпы стали обычными буквами.
    /// Строка без эскейпов или не-JSON возвращается как есть.
    /// </summary>
    public static string Readable(string json)
    {
        if (string.IsNullOrEmpty(json) || !json.Contains("\\u"))
        {
            return json;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Relaxed);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
