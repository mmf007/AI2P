using System.Globalization;
using System.Text;
using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Connectors;

/// <summary>
/// Чем кончилась работа СУФЛЁРА (T-288-S0): управляющий json для рабочей модели и расход,
/// который надо записать в задание генерации.
/// </summary>
/// <remarks>
/// С T-292-S0 расход здесь НУЛЕВОЙ: суфлёр стал отдельным исполнителем со своим заданием, и
/// токены с ценой записаны на ЭТО задание. Поля оставлены — запись едет в сводку генерации
/// (кто и чем управлял), а складывать один вызов модели в два задания нельзя.
/// </remarks>
/// <param name="Json">Текст управляющего json (объект «имя поля → значение»), который уходит
/// в <see cref="ComfyUiConnector.BuildWorkflow"/> последним параметром (контракт T-287-S0).</param>
/// <param name="InputTokens">Токены запроса суфлёра; с T-292-S0 — 0.</param>
/// <param name="OutputTokens">Токены ответа суфлёра; с T-292-S0 — 0.</param>
/// <param name="Cost">Стоимость вызова (USD); с T-292-S0 — 0.</param>
/// <param name="Model">Кто отработал суфлёром — ник исполнителя (до T-292-S0 — id модели).</param>
/// <param name="Fields">Поля, которые суфлёр назвал — в сводку задания и в журнал.</param>
public sealed record PrompterOutcome(string Json, long InputTokens, long OutputTokens, double Cost,
    string Model, IReadOnlyList<string> Fields);

/// <summary>
/// ПРОМПТ И РАЗБОР ОТВЕТА СУФЛЁРА (T-288-S0; переделано в T-292-S0).
///
/// <para>ЧТО ИЗМЕНИЛОСЬ. До T-292-S0 этот класс ещё и ЗВАЛ модель — своим маленьким
/// HTTP-клиентом на два провайдера. Из-за этого суфлёрами не могли работать ни подписка CLI,
/// ни локальные модели: на старте в консоль писалось «можно использовать только api модели»,
/// и задание уходило в генерацию без управляющего json. Теперь суфлёр — ОТДЕЛЬНЫЙ ИСПОЛНИТЕЛЬ,
/// его задание идёт обычным коннектором со всей обычной механикой (очередь, вопросы человеку,
/// ошибки, остановка кнопкой), а здесь остались только две вещи, от транспорта не зависящие:
/// сборка промпта и вырезание json из ответа. Запуск — <see cref="JobOrchestrator"/>.</para>
///
/// <para>РАЗБОР ОТВЕТА СНИСХОДИТЕЛЬНЫЙ. Приведение значения к виду поля, прижатие к границам
/// схемы и отбраковку значения не из перечня делает коннектор (T-287-S0); здесь только СЛЕД
/// (<see cref="Notes"/>): чего суфлёр не назвал из обязательного и что будет поправлено
/// границей.</para>
/// </summary>
public static class PrompterService
{
    // ---------- промпт ----------

    /// <summary>
    /// Промпт суфлёра: правила составления json (файл <c>prompter.rules</c> профайла РАБОЧЕЙ
    /// модели), перечень полей со границами, заголовок задачи, критерии приёмки и описание
    /// с раскрытыми ссылками на объекты. Язык — язык команды задачи (T-190).
    /// </summary>
    public static string BuildPrompt(PrompterSettings settings, string rules, TaskItem task,
        string description, string acceptance, string? language)
    {
        var sb = new StringBuilder();
        if (rules.Trim().Length > 0)
        {
            sb.AppendLine(Loc.In(language, "prompt.prompter.2"));
            sb.AppendLine();
            sb.AppendLine(rules.Trim());
            sb.AppendLine();
        }
        if (settings.Schema.Count > 0)
        {
            sb.AppendLine(Loc.In(language, "prompt.prompter.3"));
            sb.AppendLine();
            foreach (var field in settings.Schema)
            {
                sb.AppendLine(FieldLine(field, language));
            }
            sb.AppendLine();
        }
        sb.AppendLine(Loc.In(language, "prompt.prompter.4", task.DisplayId, task.Title));
        sb.AppendLine();
        if (acceptance.Trim().Length > 0)
        {
            sb.AppendLine(Loc.In(language, "prompt.prompter.5"));
            sb.AppendLine();
            sb.AppendLine(acceptance.Trim());
            sb.AppendLine();
        }
        sb.AppendLine(Loc.In(language, "prompt.prompter.6"));
        sb.AppendLine();
        sb.AppendLine(description.Trim());
        sb.AppendLine();
        // «ответ — строго json»: до T-292-S0 это было системным указанием отдельного вызова,
        // а у задания исполнителя системный промпт свой, общий для всех агентов, — поэтому
        // указание переехало в конец САМОГО задания, где агент его точно прочитает
        sb.AppendLine(Loc.In(language, "prompt.prompter.1"));
        sb.AppendLine();
        sb.AppendLine(Loc.In(language, "prompt.prompter.7"));
        return sb.ToString();
    }

    /// <summary>Одно поле схемы строкой списка: имя, вид, границы, перечень, пояснение.</summary>
    private static string FieldLine(PrompterField field, string? language)
    {
        var marks = new List<string> { field.Type };
        if (field.Required)
        {
            marks.Add(Loc.In(language, "prompt.prompter.8"));
        }
        if (field.Min is not null || field.Max is not null)
        {
            marks.Add(Loc.In(language, "prompt.prompter.9",
                Num(field.Min), Num(field.Max)));
        }
        if (field.MaxLength > 0)
        {
            marks.Add(Loc.In(language, "prompt.prompter.10", field.MaxLength));
        }
        if (field.Values.Count > 0)
        {
            marks.Add(Loc.In(language, "prompt.prompter.11", string.Join(" | ", field.Values)));
        }
        var line = $"- {field.Name} ({string.Join(", ", marks)})";
        return field.Description.Trim().Length > 0 ? line + " — " + field.Description.Trim() : line;
    }

    /// <summary>Граница числом; не задана — «…» (пределу с одной стороны это и значит).</summary>
    private static string Num(double? value) =>
        value is { } v ? v.ToString("0.############", CultureInfo.InvariantCulture) : "…";

    // ---------- разбор ответа ----------

    /// <summary>
    /// Json-ОБЪЕКТ из ответа модели. «Ответ — строго json» сказано в промпте, но модель всё
    /// равно обрамляет его ```json-заборчиком и дописывает пояснения, поэтому объект вырезается
    /// по скобкам (кавычки и экранирование учтены) и проверяется разбором. Не нашли объект —
    /// null: с T-292-S0 это ошибка задачи, а не тихий запуск генерации по умолчаниям.
    /// </summary>
    public static string? ExtractJson(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }
        var start = answer.IndexOf('{');
        while (start >= 0)
        {
            if (Balanced(answer, start) is { } end)
            {
                var candidate = answer[start..(end + 1)];
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(candidate);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        return candidate;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // не json — пробуем следующую открывающую скобку
                }
            }
            start = answer.IndexOf('{', start + 1);
        }
        return null;
    }

    /// <summary>Индекс скобки, закрывающей объект, начатый в <paramref name="from"/>; строки и
    /// экранирование внутри них учтены — иначе «{» в словах песни ломает разбор.</summary>
    private static int? Balanced(string text, int from)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0)
                    {
                        return i;
                    }
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Что стоит сказать человеку про ответ суфлёра: каких обязательных полей он не назвал и
    /// какие значения будут поправлены схемой. Сама правка — дело коннектора (T-287-S0),
    /// здесь только след: иначе «взяли границу вместо названных 2000 секунд» не видно нигде.
    /// </summary>
    public static List<string> Notes(PrompterSettings settings,
        IReadOnlyDictionary<string, PrompterValue> values)
    {
        var notes = new List<string>();
        var missing = settings.Schema
            .Where(f => f.Required && !values.ContainsKey(f.Name))
            .Select(f => f.Name)
            .ToList();
        if (missing.Count > 0)
        {
            notes.Add(Loc.T("msg.prompter.10", string.Join(", ", missing)));
        }
        foreach (var (name, value) in values)
        {
            if (settings.Field(name) is not { } field)
            {
                continue;   // лишнее поле ответа игнорируется молча — оно никуда не уйдёт
            }
            switch (field.Type)
            {
                case PrompterFieldTypes.Int:
                case PrompterFieldTypes.Num:
                {
                    if (!double.TryParse(value.Text.Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var n))
                    {
                        notes.Add(Loc.T("msg.prompter.11", name, Preview(value.Text)));
                        break;
                    }
                    var clamped = Math.Clamp(n, field.Min ?? double.MinValue, field.Max ?? double.MaxValue);
                    if (Math.Abs(clamped - n) > double.Epsilon)
                    {
                        notes.Add(Loc.T("msg.prompter.12", name, Num(n), Num(field.Min),
                            Num(field.Max), Num(clamped)));
                    }
                    break;
                }
                case PrompterFieldTypes.Enum when field.Values.Count > 0:
                {
                    if (!field.Values.Any(v => v.Equals(value.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        notes.Add(Loc.T("msg.prompter.13", name, Preview(value.Text)));
                    }
                    break;
                }
                case PrompterFieldTypes.Str when field.MaxLength > 0 && value.Text.Length > field.MaxLength:
                    notes.Add(Loc.T("msg.prompter.14", name, value.Text.Length, field.MaxLength));
                    break;
            }
        }
        return notes;
    }

    internal static string Preview(string text) =>
        text.Length <= 300 ? text : text[..300] + "…";
}
