using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКА РЕФЕРЕНСНОГО АУДИО у записи справочника моделей (T-249-S0, ключ
/// <c>refAudio</c> профайла) — звуковой близнец <see cref="RefImageSettings"/>.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ СЕКЦИЯ, А НЕ «ОБОБЩЁННЫЙ МЕДИА-ВХОД». У звука свои пределы, которых
/// у картинки нет и быть не может (длительность образца), а у картинки — свои (ширина,
/// высота). Модель, которая берёт и кадр, и голос сразу (озвучка персонажа по портрету),
/// объявляет ОБЕ секции, и каждая со своим плейсхолдером; одна общая секция такую пару
/// описать не может. Ровно тем же доводом стоит отдельно и <c>refImage</c> от
/// <see cref="LoraSettings"/> (T-13-S1).
///
/// ЧТО ЭТО ДАЁТ. Постоянный голос одного человека и постоянный звук одного объекта у
/// аудио-моделей достигаются НЕ адаптером, а образцом: 3–30 секунд записи подаются в
/// запрос, и модель копирует тембр (zero-shot clone). Обучать при этом нечего — значит
/// и механизма обучения не нужно, нужен ВХОД. Он и заведён здесь.
///
/// Способ передачи тот же, что у кадра (<see cref="RefAudioKinds"/>): у ComfyUI файл
/// сначала заливается в движок (исторически тем же <c>POST /upload/image</c> — его берут
/// и узлы <c>LoadAudio</c>) и в граф идёт ИМЯ файла, у облачного шлюза уходит адресом
/// <c>data:</c> в поле запроса, у части моделей передать его нечем вовсе
/// (<see cref="RefAudioKinds.None"/>).
/// </summary>
public sealed class RefAudioSettings
{
    /// <summary>Способ передачи: <see cref="RefAudioKinds"/>.</summary>
    public string Kind { get; set; } = RefAudioKinds.None;

    /// <summary>Плейсхолдер имени файла в шаблоне workflow либо запроса.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Адрес загрузки записи в движок (относительно baseUrl профайла).</summary>
    public string UploadPath { get; set; } = "";

    /// <summary>Имя части multipart-формы загрузки либо поля запроса генерации.</summary>
    public string Field { get; set; } = "";

    /// <summary>Сколько записей модель принимает разом.</summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>Форматы, которые модель принимает («audio/wav», «audio/mpeg»).</summary>
    public List<string> Formats { get; set; } = [];

    /// <summary>Запись обязательна: без неё задание этой модели запускать нечем.</summary>
    public bool Required { get; set; }

    /// <summary>Наибольшая длительность образца в секундах; 0 — модель об этом не сказала.</summary>
    public int MaxSeconds { get; set; }

    /// <summary>Модель принимает запись на вход.</summary>
    public bool Supported => !Kind.Equals(RefAudioKinds.None, StringComparison.OrdinalIgnoreCase);

    /// <summary>Разобрать из ТЕКСТА профайла; профайла или ключа нет — «передать нечем».</summary>
    public static RefAudioSettings Parse(string? profileJson)
    {
        if (profileJson is not { Length: > 0 } || profileJson.Trim().Length == 0)
        {
            return new RefAudioSettings();
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            return FromProfile(doc.RootElement);
        }
        catch (JsonException)
        {
            return new RefAudioSettings();
        }
    }

    /// <summary>Разобрать из уже прочитанного профайла.</summary>
    public static RefAudioSettings FromProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("refAudio", out var e) || e.ValueKind != JsonValueKind.Object)
        {
            return new RefAudioSettings();
        }
        return new RefAudioSettings
        {
            Kind = JsonRead.Str(e, "kind", RefAudioKinds.None),
            Placeholder = JsonRead.Str(e, "placeholder"),
            UploadPath = JsonRead.Str(e, "uploadPath"),
            Field = JsonRead.Str(e, "field"),
            MaxCount = JsonRead.Int(e, "maxCount", 1),
            Formats = JsonRead.Strings(e, "formats"),
            Required = JsonRead.Bool(e, "required", false),
            MaxSeconds = JsonRead.Int(e, "maxSeconds", 0),
        };
    }
}

/// <summary>Способы передачи референсного аудио в модель. Значения намеренно те же, что у
/// картинки (<see cref="RefImageKinds"/>): способ передачи файла от его вида не зависит,
/// а одинаковые коды дают один набор словарных ключей и одну форму справочника.</summary>
public static class RefAudioKinds
{
    /// <summary>Передать нечем: модель запись на вход не берёт.</summary>
    public const string None = "none";

    /// <summary>Залить в движок и подставить в граф имя файла из ответа (ComfyUI).</summary>
    public const string Upload = "upload";

    /// <summary>Положить в поле запроса генерации (облачный API, адрес data:).</summary>
    public const string RequestField = "request-field";

    /// <summary>Отправить блоком в сообщении диалога (мультимодальные текстовые модели).</summary>
    public const string Message = "message";

    public static readonly string[] All = [None, Upload, RequestField, Message];
}
