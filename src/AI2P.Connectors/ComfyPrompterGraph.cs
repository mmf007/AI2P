using System.Globalization;
using System.Text;
using System.Text.Json;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ИМЕНОВАННАЯ ПОДСТАНОВКА значений МОДЕЛИ-СУФЛЁРА в граф ComfyUI (T-287-S0).
///
/// ЗАЧЕМ. До сих пор шаблон workflow знал восемь плейсхолдеров, и все, кроме
/// <c>{prompt}</c>, брались из ПРОФАЙЛА — то есть были одинаковы во всех заданиях.
/// У ACE-Step 1.5 это значило, что всё описание задачи оседало в поле стилевых тэгов
/// <c>tags</c>, слова песни были пусты всегда, длительность — 120 с из профайла, а язык
/// вокала — «unknown». Модель-суфлёр (T-286-S0: секция <c>prompter</c> профайла; T-288-S0:
/// её запуск) называет эти значения по описанию задачи — здесь они попадают в граф.
///
/// КАК ЗАПИСАНО В ШАБЛОНЕ. Плейсхолдер <c>{p:&lt;имя&gt;}</c> — имя поля управляющего json.
/// Стоит он внутри кавычек (как <c>{prompt}</c>) — значение уходит текстом и
/// JSON-экранируется; занимает всю строку ВМЕСТЕ С КАВЫЧКАМИ (как <c>"{seed}"</c>) —
/// на его место встаёт литерал json: число и bool без кавычек, строка — в кавычках.
/// Вид значения берётся из схемы профайла (<see cref="PrompterSettings"/>), а не угадывается:
/// «90» в поле типа int уйдёт числом, а не строкой, иначе ComfyUI откажет в счёте.
///
/// УСТОЙЧИВОСТЬ — главное правило этого места. Шаблон обязан собираться и БЕЗ суфлёра:
/// у плейсхолдера есть УМОЛЧАНИЕ, отделённое чертой — <c>{p:duration|{length}}</c>. Умолчание
/// это обычный текст шаблона, и обычные плейсхолдеры в нём работают: не назвал суфлёр
/// длительность — встанет <c>length</c> профайла, не назвал тэги (<c>{p:tags|{prompt}}</c>) —
/// встанет описание задачи, ровно как было до T-287-S0. Незаполненных плейсхолдеров в
/// графе не остаётся вовсе: нет ни значения, ни умолчания — встанет пустая строка (у
/// числового поля ноль), потому что <c>{p:…}</c>, уехавший в ComfyUI как есть, — это
/// «required input is missing» уже во время задания, и выглядит это поломкой движка.
///
/// ЧЕГО ЗДЕСЬ НЕТ. Вызова суфлёра и проверки его ответа по схеме на полноту (обязательные
/// поля) — это соседняя подзадача. Здесь ответ читается СНИСХОДИТЕЛЬНО: мусор вместо json,
/// лишние поля, значение не того вида, число за границей, значение не из перечня — не
/// ошибка, а «суфлёр этого не назвал» (либо граница, к которой значение прижато).
/// </summary>
public static class ComfyPrompterGraph
{
    /// <summary>Начало плейсхолдера суфлёра.</summary>
    private const string Head = "{p:";

    /// <summary>
    /// Собрать граф: именованные значения суфлёра — из <paramref name="prompterJson"/>,
    /// остальной текст шаблона — через <paramref name="classic"/> (обычные плейсхолдеры
    /// {prompt}, "{seed}" и прочие). Порядок именно такой: текст, пришедший ОТ СУФЛЁРА,
    /// через <paramref name="classic"/> не проходит — слова песни со строкой «{prompt}»
    /// внутри не должны толковаться как плейсхолдер.
    /// </summary>
    public static string Apply(string graph, PrompterSettings settings, string? prompterJson,
        Func<string, string> classic)
    {
        if (!graph.Contains(Head, StringComparison.Ordinal))
        {
            return classic(graph); // шаблона без суфлёра эта правка не касается вовсе
        }
        var values = Values(prompterJson);
        var result = new StringBuilder(graph.Length + 256);
        var pending = 0;
        var from = 0;
        while (true)
        {
            var at = graph.IndexOf(Head, from, StringComparison.Ordinal);
            if (at < 0)
            {
                result.Append(classic(graph[pending..]));
                break;
            }
            var close = CloseBrace(graph, at + Head.Length);
            var body = close < 0 ? "" : graph[(at + Head.Length)..close];
            var bar = body.IndexOf('|');
            var name = (bar < 0 ? body : body[..bar]).Trim();
            if (close < 0 || !IsName(name))
            {
                from = at + Head.Length; // не наш плейсхолдер — пусть остаётся как есть
                continue;
            }
            var def = bar < 0 ? null : body[(bar + 1)..];
            // литерал целиком (вместе с кавычками) — только когда плейсхолдер занимает
            // строку json полностью: "{p:bpm|120}" — число, "тэги {p:x}" — текст
            var standalone = at > 0 && graph[at - 1] == '"' &&
                             close + 1 < graph.Length && graph[close + 1] == '"';
            result.Append(classic(graph[pending..(standalone ? at - 1 : at)]));
            result.Append(Fill(settings, values, name, def, standalone, classic));
            pending = from = standalone ? close + 2 : close + 1;
        }
        return result.ToString();
    }

    /// <summary>
    /// Значения управляющего json суфлёра: имя поля → значение. Разбор снисходительный —
    /// мусор, не-объект и вложенные объекты дают пустой набор либо пропуск поля: ответ
    /// модели не обязан быть безупречным, а генерация из-за него падать не должна.
    /// </summary>
    public static IReadOnlyDictionary<string, PrompterValue> Values(string? prompterJson)
    {
        var result = new Dictionary<string, PrompterValue>(StringComparer.OrdinalIgnoreCase);
        if (prompterJson is not { Length: > 0 } || prompterJson.Trim().Length == 0)
        {
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(prompterJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var kind = p.Value.ValueKind;
                if (kind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or
                    JsonValueKind.Undefined)
                {
                    continue; // подставлять в поле узла нечего
                }
                result[p.Name] = new PrompterValue(kind, kind == JsonValueKind.String
                    ? p.Value.GetString() ?? ""
                    : p.Value.GetRawText());
            }
        }
        catch (JsonException)
        {
            // мусор вместо json — «суфлёр не назвал ничего», шаблон соберётся умолчаниями
        }
        return result;
    }

    /// <summary>Что встанет на место одного плейсхолдера.</summary>
    private static string Fill(PrompterSettings settings,
        IReadOnlyDictionary<string, PrompterValue> values, string name, string? def,
        bool standalone, Func<string, string> classic)
    {
        var field = settings.Field(name);
        if (values.TryGetValue(name, out var value) &&
            Normalize(value, field) is { } text)
        {
            var number = IsNumeric(value, field);
            return standalone
                ? number ? text : Quoted(JsonEscape(text))
                : JsonEscape(text);
        }
        if (def is not null)
        {
            return Default(def, standalone, classic, field);
        }
        // ни значения, ни умолчания — шаблон недописан; оставить {p:…} нельзя, это отказ
        // ComfyUI уже во время задания
        return standalone
            ? field?.Type switch
            {
                PrompterFieldTypes.Int or PrompterFieldTypes.Num => "0",
                PrompterFieldTypes.Bool => "false",
                _ => "\"\"",
            }
            : "";
    }

    /// <summary>
    /// Значение суфлёра, приведённое к виду поля схемы: число из строки «90», обрезка по
    /// maxLength, прижатие к min/max, отказ от значения не из перечня (null — «не назвал»,
    /// встанет умолчание). Прижатие, а не отказ, потому что у ComfyUI границы жёсткие:
    /// duration за пределом узла — ошибка счёта, а не «немного не то».
    /// </summary>
    private static string? Normalize(PrompterValue value, PrompterField? field)
    {
        var type = field?.Type ?? "";
        var text = value.Text.Trim();
        switch (type)
        {
            case PrompterFieldTypes.Int:
            case PrompterFieldTypes.Num:
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                {
                    return null;
                }
                n = Math.Clamp(n, field!.Min ?? double.MinValue, field.Max ?? double.MaxValue);
                return type == PrompterFieldTypes.Int
                    ? ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture)
                    : n.ToString("0.############", CultureInfo.InvariantCulture);
            }
            case PrompterFieldTypes.Bool:
                return text.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" => "true",
                    "false" or "0" or "no" => "false",
                    _ => null,
                };
            case PrompterFieldTypes.Enum when field!.Values.Count > 0:
            {
                var known = field.Values.FirstOrDefault(
                    v => v.Equals(text, StringComparison.OrdinalIgnoreCase));
                return known; // не из перечня — как не названное
            }
            default:
            {
                var s = value.Text;
                return field is { MaxLength: > 0 } && s.Length > field.MaxLength
                    ? s[..field.MaxLength]
                    : s;
            }
        }
    }

    /// <summary>Значение уходит в json БЕЗ кавычек (число или bool).</summary>
    private static bool IsNumeric(PrompterValue value, PrompterField? field) =>
        field?.Type switch
        {
            PrompterFieldTypes.Int or PrompterFieldTypes.Num or PrompterFieldTypes.Bool => true,
            PrompterFieldTypes.Str or PrompterFieldTypes.Enum => false,
            // поля нет в схеме — вид берётся у самого значения
            _ => value.Kind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False,
        };

    /// <summary>
    /// Умолчание — обычный текст ШАБЛОНА, поэтому обычные плейсхолдеры в нём работают:
    /// <c>{p:duration|{length}}</c> без суфлёра даёт длительность профайла. В форме литерала
    /// умолчание пробуется сначала В КАВЫЧКАХ (так записан числовой <c>"{length}"</c>), и
    /// только если это ничего не поменяло — как есть. У поля, объявленного схемой СТРОКОЙ
    /// или перечнем, кавычки остаются всегда: умолчание «4» размера такта — это строка «4»
    /// узла-перечня, а не число, и числом ComfyUI его не примет.
    /// </summary>
    private static string Default(string def, bool standalone, Func<string, string> classic,
        PrompterField? field)
    {
        if (!standalone)
        {
            return classic(def);
        }
        if (field?.Type is PrompterFieldTypes.Str or PrompterFieldTypes.Enum)
        {
            return Quoted(classic(def));
        }
        var quoted = Quoted(def);
        var replaced = classic(quoted);
        if (replaced != quoted)
        {
            return replaced; // "{length}" → 120: число вместе с кавычками
        }
        var plain = classic(def);
        return IsJsonLiteral(plain) ? plain : Quoted(plain);
    }

    /// <summary>Число, true или false, записанное текстом умолчания.</summary>
    private static bool IsJsonLiteral(string text) =>
        text is "true" or "false" ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Индекс закрывающей скобки плейсхолдера; вложенные {…} умолчания учтены.</summary>
    private static int CloseBrace(string text, int from)
    {
        var depth = 1;
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Имя поля: буквы, цифры, «_», «-», «.» — иначе это не наш плейсхолдер.</summary>
    private static bool IsName(string name) =>
        name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');

    private static string Quoted(string text) => "\"" + text + "\"";

    /// <summary>Кириллица не эскейпится — граф задания остаётся читаемым (как в коннекторе).</summary>
    private static readonly JsonSerializerOptions EscapeOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Текст внутрь строки json (кавычки не добавляются) — как в коннекторе.</summary>
    private static string JsonEscape(string text)
    {
        var serialized = JsonSerializer.Serialize(text, EscapeOptions);
        return serialized[1..^1]; // без обрамляющих кавычек
    }
}

/// <summary>Одно значение управляющего json суфлёра: вид и текст (T-287-S0).</summary>
/// <param name="Kind">Вид значения json: строка, число, true/false.</param>
/// <param name="Text">Значение текстом: у строки — она сама, у числа и bool — их запись.</param>
public sealed record PrompterValue(JsonValueKind Kind, string Text);
