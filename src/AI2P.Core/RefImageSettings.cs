using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКА РЕФЕРЕНСНОЙ КАРТИНКИ у записи справочника моделей (T-13-S1, ключ
/// <c>refImage</c> профайла). Стоит отдельно от <see cref="LoraSettings"/> НАМЕРЕННО:
/// эталонный кадр нужен и там, где никакого адаптера нет — режим «изображение → видео»
/// работает по одной картинке (T-258), а адаптер это уже обучение.
///
/// Настройка отвечает ровно на один вопрос — КАК картинка попадает в модель:
/// у ComfyUI её сначала надо залить в движок (<c>POST /upload/image</c>) и подставить
/// в граф ИМЯ файла из ответа, у облачного API — положить в поле запроса, у части
/// моделей передать её нечем вовсе (<see cref="RefImageKinds.None"/>).
/// </summary>
public sealed class RefImageSettings
{
    /// <summary>Способ передачи: <see cref="RefImageKinds"/>.</summary>
    public string Kind { get; set; } = RefImageKinds.None;

    /// <summary>Плейсхолдер имени картинки в шаблоне workflow (ComfyUI).</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Адрес загрузки картинки в движок (относительно baseUrl профайла).</summary>
    public string UploadPath { get; set; } = "";

    /// <summary>Имя части multipart-формы загрузки либо поля запроса генерации.</summary>
    public string Field { get; set; } = "";

    /// <summary>Сколько картинок модель принимает разом.</summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>Форматы, которые модель принимает («image/png», «image/jpeg»).</summary>
    public List<string> Formats { get; set; } = [];

    /// <summary>Картинка обязательна: без неё задание этой модели запускать нечем (i2v).</summary>
    public bool Required { get; set; }

    /// <summary>Модель принимает картинку на вход.</summary>
    public bool Supported => !Kind.Equals(RefImageKinds.None, StringComparison.OrdinalIgnoreCase);

    /// <summary>Разобрать из ТЕКСТА профайла; профайла или ключа нет — «передать нечем».</summary>
    public static RefImageSettings Parse(string? profileJson)
    {
        if (profileJson is not { Length: > 0 } || profileJson.Trim().Length == 0)
        {
            return new RefImageSettings();
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            return FromProfile(doc.RootElement);
        }
        catch (JsonException)
        {
            return new RefImageSettings();
        }
    }

    /// <summary>Разобрать из уже прочитанного профайла.</summary>
    public static RefImageSettings FromProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("refImage", out var e) || e.ValueKind != JsonValueKind.Object)
        {
            return new RefImageSettings();
        }
        return new RefImageSettings
        {
            Kind = JsonRead.Str(e, "kind", RefImageKinds.None),
            Placeholder = JsonRead.Str(e, "placeholder"),
            UploadPath = JsonRead.Str(e, "uploadPath"),
            Field = JsonRead.Str(e, "field"),
            MaxCount = JsonRead.Int(e, "maxCount", 1),
            Formats = JsonRead.Strings(e, "formats"),
            Required = JsonRead.Bool(e, "required", false),
        };
    }
}

/// <summary>Способы передачи референсной картинки в модель.</summary>
public static class RefImageKinds
{
    /// <summary>Передать нечем: модель картинку на вход не берёт.</summary>
    public const string None = "none";

    /// <summary>Залить в движок и подставить в граф имя файла из ответа (ComfyUI).</summary>
    public const string Upload = "upload";

    /// <summary>Положить в поле запроса генерации (облачный API).</summary>
    public const string RequestField = "request-field";

    /// <summary>Отправить блоком в сообщении диалога (мультимодальные текстовые модели).</summary>
    public const string Message = "message";
}
