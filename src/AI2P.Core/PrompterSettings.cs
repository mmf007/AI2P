using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКА МОДЕЛИ-СУФЛЁРА у записи справочника моделей (T-286-S0, ключ <c>prompter</c>
/// профайла) — третья секция того же рода, что <see cref="LoraSettings"/> (T-13-S1) и
/// <see cref="RefAudioSettings"/> (T-249-S0), и лежит она там же по той же причине:
/// «поддерживается» и «как именно» обязаны быть в ОДНОМ месте, иначе они разъезжаются
/// молча (наука T-257 про описания справочников).
///
/// ЗАЧЕМ СУФЛЁР. Медиа-задание получает промптом ТОЛЬКО описание задачи, и оно целиком
/// уходит в ОДИН текстовый плейсхолдер <c>{prompt}</c>. У ACE-Step 1.5 этот плейсхолдер
/// стоит в поле СТИЛЕВЫХ ТЭГОВ узла <c>TextEncodeAceStepAudio1.5</c>, а длительность,
/// язык, слова песни, темп и тональность — ОТДЕЛЬНЫЕ поля того же узла, которые сегодня
/// берутся из профайла и одинаковы во всех заданиях. Поэтому «песня на русском, 90 секунд,
/// слова такие-то» исполняется как «120 секунд, язык не указан, слова модель сочинит сама».
/// Решение: у исполнителя-ИИ с такой моделью появляется ВТОРАЯ модель — суфлёр: текстовая
/// LLM, которая по описанию задачи готовит управляющий json для рабочей модели.
///
/// ЧТО ЗДЕСЬ ЕСТЬ И ЧЕГО НЕТ. Здесь только ОБЪЯВЛЕНИЕ: нужен ли модели суфлёр
/// (<see cref="Required"/>), где лежат правила составления json (<see cref="Rules"/>) и
/// какие поля этот json несёт (<see cref="Schema"/>). Запуск суфлёра, разбор его ответа и
/// подстановка значений в граф — соседние подзадачи; настройка, как и все её родственницы,
/// ДЕКЛАРАТИВНА.
///
/// Признак производный: <c>AiModel.Prompter</c> заполняется сервисом при выдаче списка и
/// записи — ни колонки в БД, ни миграции схемы, ни шага обновления не нужно, а правка
/// профайла видна сразу (ровно как у <c>Lora</c>, <c>RefImage</c> и <c>RefAudio</c>).
/// Секции нет вовсе — суфлёр не нужен: молчание профайла здесь и есть ответ «не нужен»,
/// потому что работа без суфлёра это обычный путь всех медиа-моделей системы.
/// </summary>
public sealed class PrompterSettings
{
    /// <summary>Модели нужен суфлёр — колонка «Суфлёр» списка справочника.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Путь к файлу правил составления управляющего json — РЯДОМ С ПРОФАЙЛОМ, в каталоге
    /// данных (models/prompter_&lt;ID&gt;.md). Правила пишутся текстом для текстовой модели,
    /// и держать их в json-настройке значило бы прятать страницу текста в одну строку.
    /// </summary>
    public string Rules { get; set; } = "";

    /// <summary>
    /// Перечень полей управляющего json с типами и границами. Нужен трижды: суфлёру —
    /// в задании («вот что от тебя ждут»), разбору ответа — для проверки, а человеку —
    /// чтобы понимать, чем именно управляет суфлёр у этой модели.
    /// </summary>
    public List<PrompterField> Schema { get; set; } = [];

    /// <summary>Поле схемы по имени; нет такого — null.</summary>
    public PrompterField? Field(string name) =>
        Schema.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Разобрать из ТЕКСТА профайла; профайла или ключа нет — «суфлёр не нужен».</summary>
    public static PrompterSettings Parse(string? profileJson)
    {
        if (profileJson is not { Length: > 0 } || profileJson.Trim().Length == 0)
        {
            return new PrompterSettings();
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            return FromProfile(doc.RootElement);
        }
        catch (JsonException)
        {
            return new PrompterSettings();
        }
    }

    /// <summary>Разобрать из уже прочитанного профайла.</summary>
    public static PrompterSettings FromProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("prompter", out var e) || e.ValueKind != JsonValueKind.Object)
        {
            return new PrompterSettings();
        }
        var result = new PrompterSettings
        {
            Required = JsonRead.Bool(e, "required", false),
            Rules = JsonRead.Str(e, "rules"),
        };
        if (e.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var field = PrompterField.FromJson(item);
                if (field.Name.Length > 0)
                {
                    result.Schema.Add(field);
                }
            }
        }
        return result;
    }
}

/// <summary>
/// ОДНО ПОЛЕ управляющего json (T-286-S0). Границы названы здесь, а не в правилах текстом:
/// правила читает модель, а проверяет ответ код, и число, написанное в двух местах, рано
/// или поздно разойдётся. Незаполненная граница (<see cref="Min"/>, <see cref="Max"/>,
/// <see cref="MaxLength"/> — null либо 0) означает «модель об этом не сказала».
/// </summary>
public sealed class PrompterField
{
    /// <summary>Имя поля json — оно же имя, под которым значение ищет коннектор.</summary>
    public string Name { get; set; } = "";

    /// <summary>Тип значения: <see cref="PrompterFieldTypes"/>.</summary>
    public string Type { get; set; } = PrompterFieldTypes.Str;

    /// <summary>Поле обязательно: без него управляющий json считается неполным.</summary>
    public bool Required { get; set; }

    /// <summary>Наименьшее допустимое значение числового поля; null — не ограничено.</summary>
    public double? Min { get; set; }

    /// <summary>Наибольшее допустимое значение числового поля; null — не ограничено.</summary>
    public double? Max { get; set; }

    /// <summary>Предел длины строкового поля в знаках; 0 — не ограничена.</summary>
    public int MaxLength { get; set; }

    /// <summary>Допустимые значения поля-перечисления; у остальных типов пусто.</summary>
    public List<string> Values { get; set; } = [];

    /// <summary>Что это поле значит — одной строкой, для суфлёра и для человека.</summary>
    public string Description { get; set; } = "";

    /// <summary>Разобрать поле схемы; вид значения проверяется отдельно (JsonRead), поэтому
    /// строка вместо числа настройку не роняет, а просто не даёт границы.</summary>
    public static PrompterField FromJson(JsonElement e) => new()
    {
        Name = JsonRead.Str(e, "name"),
        Type = JsonRead.Str(e, "type", PrompterFieldTypes.Str),
        Required = JsonRead.Bool(e, "required", false),
        Min = NumOrNull(e, "min"),
        Max = NumOrNull(e, "max"),
        MaxLength = JsonRead.Int(e, "maxLength", 0),
        Values = JsonRead.Strings(e, "values"),
        Description = JsonRead.Str(e, "description"),
    };

    /// <summary>Число или «не задано»: у границы ноль — осмысленное значение, поэтому
    /// отличать «нет поля» от «ноль» приходится явно.</summary>
    private static double? NumOrNull(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out var value)
            ? value
            : null;
}

/// <summary>Типы полей управляющего json. Перечень намеренно короткий: суфлёр — текстовая
/// модель, и чем меньше у неё выбора, тем меньше поводов ошибиться.</summary>
public static class PrompterFieldTypes
{
    /// <summary>Строка.</summary>
    public const string Str = "string";

    /// <summary>Целое число.</summary>
    public const string Int = "int";

    /// <summary>Дробное число.</summary>
    public const string Num = "number";

    /// <summary>Да/нет.</summary>
    public const string Bool = "bool";

    /// <summary>Одно значение из списка <see cref="PrompterField.Values"/>.</summary>
    public const string Enum = "enum";

    public static readonly string[] All = [Str, Int, Num, Bool, Enum];
}
