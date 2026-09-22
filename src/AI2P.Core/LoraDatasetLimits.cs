using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКИ КОНТРОЛЯ КАРТИНОК ОДНОГО ДАТАСЕТА (T-274, версия 1.98).
///
/// Лежат в самом объекте-датасете (<c>objects.dataset_json</c>) и живут своей жизнью,
/// отдельно от общих настроек приложения. Причина простая: у разных моделей разные
/// требования к кадрам — Kandinsky учат на 768×512 PNG, а следующая модель попросит
/// 1024×1024, — и одно значение на всё приложение означало бы «пересобери датасет, когда
/// сменишь модель». Датасет заводится СНИМКОМ общих настроек и дальше правится сам.
///
/// Второй источник значений — справочник моделей (<see cref="LoraDataset"/> из профайла,
/// T-13-S1): кнопка «подставить из модели» переписывает сюда то, что модель объявила
/// требованием к датасету (<see cref="FromModel"/>). Обратной записи нет: справочник
/// правится в справочнике.
///
/// Перед запуском обучения датасет и модель СРАВНИВАЮТСЯ (<see cref="Compare"/>) — но
/// только в одну сторону: несовпадение считается бедой, когда значения датасета БОЛЬШЕ
/// объявленных моделью (кадр крупнее, чем модель берёт) либо формат не тот, который она
/// принимает. Датасет со значениями меньше объявленных законен: обучать на кадрах поменьше
/// никто не запрещает, а требовать «ровно столько» значило бы ругаться на исправную работу.
/// </summary>
public sealed class LoraDatasetLimits
{
    /// <summary>Умолчание длительности записи, когда модель о ней ничего не сказала.</summary>
    public const int DefaultMaxSeconds = 240;

    /// <summary>Умолчание частоты дискретизации (Гц) — 44 100, как у звуковой дорожки CD.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>
    /// ИЗ ЧЕГО СОБРАН ЭТОТ ДАТАСЕТ (T-250-S0): <see cref="LoraDatasetMedia"/>. От него
    /// зависит, ЧЕМ пределы вообще являются: у картинок это ширина, высота и вес файла,
    /// у записей — длительность, частота дискретизации и число каналов. Сверка ширины и
    /// высоты у звукового датасета не делается ВОВСЕ: их у записи нет.
    /// </summary>
    public string Media { get; set; } = LoraDatasetMedia.Image;

    /// <summary>Наибольшая ширина кадра в точках; кадр крупнее ужимается при добавлении.</summary>
    public int MaxWidth { get; set; } = 1024;

    /// <summary>Наибольшая высота кадра в точках.</summary>
    public int MaxHeight { get; set; } = 1024;

    /// <summary>Предел размера файла кадра в килобайтах.</summary>
    public int MaxKb { get; set; } = 2048;

    /// <summary>Формат, в который кадры переводятся: «png» либо «jpeg».</summary>
    public string Format { get; set; } = "png";

    /// <summary>Сколько кадров нужно как минимум; 0 — не сказано.</summary>
    public int MinItems { get; set; }

    /// <summary>Сколько кадров имеет смысл брать максимум; 0 — не сказано.</summary>
    public int MaxItems { get; set; }

    /// <summary>Наименьшая длительность записи в секундах (звук); 0 — не задана.</summary>
    public int MinSeconds { get; set; }

    /// <summary>Наибольшая длительность записи в секундах (звук).</summary>
    public int MaxSeconds { get; set; } = DefaultMaxSeconds;

    /// <summary>Частота дискретизации записей датасета (Гц).</summary>
    public int SampleRate { get; set; } = DefaultSampleRate;

    /// <summary>Каналов: 1 — моно, 2 — стерео.</summary>
    public int Channels { get; set; } = 2;

    /// <summary>Датасет собран из ЗВУКОВЫХ ЗАПИСЕЙ.</summary>
    public bool IsAudio => LoraDatasetMedia.Normalize(Media) == LoraDatasetMedia.Audio;

    /// <summary>Форматы файла записи, которые понимаем мы сами (и наши тренеры).</summary>
    public static readonly string[] AudioFormats = ["wav", "mp3", "flac", "ogg", "m4a", "opus"];

    /// <summary>Формат приведён к «png» либо «jpeg»: другого холст браузера не отдаёт.</summary>
    public static string NormalizeFormat(string? value) =>
        Normalize(value) == "jpeg" ? "jpeg" : "png";

    /// <summary>
    /// Формат к общему виду С ОГЛЯДКОЙ НА ВИД ДАТАСЕТА: у картинок выбор из двух (холст
    /// браузера другого не отдаёт), у записей — из тех, что умеют читать тренеры звука;
    /// незнакомое значение у звука — «wav» (он без потерь и читается всем).
    /// </summary>
    public static string NormalizeFormat(string? value, string media)
    {
        if (LoraDatasetMedia.Normalize(media) != LoraDatasetMedia.Audio)
        {
            return NormalizeFormat(value);
        }
        var text = Normalize(value);
        return Array.IndexOf(AudioFormats, text) >= 0 ? text : "wav";
    }

    /// <summary>
    /// Разобрать настройку из текста. Пусто, мусор или чужой JSON — умолчания: настройка
    /// контроля картинок никогда не должна мешать открыть датасет.
    /// </summary>
    public static LoraDatasetLimits Parse(string? json)
    {
        if (json is not { Length: > 0 } || json.Trim().Length == 0)
        {
            return new LoraDatasetLimits();
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object)
            {
                return new LoraDatasetLimits();
            }
            return new LoraDatasetLimits
            {
                Media = LoraDatasetMedia.Normalize(JsonRead.Str(e, "media", LoraDatasetMedia.Image)),
                MaxWidth = JsonRead.Int(e, "maxWidth", 1024),
                MaxHeight = JsonRead.Int(e, "maxHeight", 1024),
                MaxKb = JsonRead.Int(e, "maxKb", 2048),
                Format = JsonRead.Str(e, "format", "png"),
                MinItems = JsonRead.Int(e, "minItems", 0),
                MaxItems = JsonRead.Int(e, "maxItems", 0),
                MinSeconds = JsonRead.Int(e, "minSeconds", 0),
                MaxSeconds = JsonRead.Int(e, "maxSeconds", DefaultMaxSeconds),
                SampleRate = JsonRead.Int(e, "sampleRate", DefaultSampleRate),
                Channels = JsonRead.Int(e, "channels", 2),
            }.Sane();
        }
        catch (JsonException)
        {
            return new LoraDatasetLimits();
        }
    }

    /// <summary>Текст для хранения в объекте-датасете.</summary>
    public string ToJson()
    {
        var node = new JsonObject
        {
            ["media"] = LoraDatasetMedia.Normalize(Media),
            ["maxWidth"] = MaxWidth,
            ["maxHeight"] = MaxHeight,
            ["maxKb"] = MaxKb,
            ["format"] = Format,
            ["minItems"] = MinItems,
            ["maxItems"] = MaxItems,
            ["minSeconds"] = MinSeconds,
            ["maxSeconds"] = MaxSeconds,
            ["sampleRate"] = SampleRate,
            ["channels"] = Channels,
        };
        return node.ToJsonString();
    }

    /// <summary>Значения, при которых датасет остаётся рабочим: ноль в пределе кадра
    /// означал бы «кадров размером ноль», то есть добавить нельзя ни одного.</summary>
    public LoraDatasetLimits Sane()
    {
        Media = LoraDatasetMedia.Normalize(Media);
        // ширина, высота и вес кадра остаются рабочими и у звукового датасета: вид
        // датасета человек переключает туда и обратно, и обнулённые пределы картинок
        // после возврата к картинкам означали бы «добавить нельзя ни одного кадра»
        MaxWidth = MaxWidth > 0 ? MaxWidth : 1024;
        MaxHeight = MaxHeight > 0 ? MaxHeight : 1024;
        MaxKb = MaxKb > 0 ? MaxKb : 2048;
        Format = NormalizeFormat(Format, Media);
        MinItems = MinItems > 0 ? MinItems : 0;
        MaxItems = MaxItems > 0 ? MaxItems : 0;
        MinSeconds = MinSeconds > 0 ? MinSeconds : 0;
        MaxSeconds = MaxSeconds > 0 ? MaxSeconds : DefaultMaxSeconds;
        SampleRate = SampleRate > 0 ? SampleRate : DefaultSampleRate;
        Channels = Channels is 1 or 2 ? Channels : 2;
        return this;
    }

    /// <summary>Точная копия — форма правит снимок, а не то, что лежит в записи.</summary>
    public LoraDatasetLimits Copy() => new()
    {
        Media = Media,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        MaxKb = MaxKb,
        Format = Format,
        MinItems = MinItems,
        MaxItems = MaxItems,
        MinSeconds = MinSeconds,
        MaxSeconds = MaxSeconds,
        SampleRate = SampleRate,
        Channels = Channels,
    };

    /// <summary>
    /// ПОДСТАВИТЬ ИЗ МОДЕЛИ — то, что модель объявила требованием к датасету обучения.
    /// Незаполненное моделью поле берётся из <paramref name="fallback"/> (сегодняшние
    /// настройки датасета либо общие настройки приложения): у справочника нет обязанности
    /// заполнять всё, а датасет без предела размера кадра нерабочий.
    ///
    /// ФОРМАТ выбирается по правилу задания: из перечисленных моделью берётся PNG, если он
    /// есть, — он без потерь, и обучающие скрипты Kandinsky читают именно его.
    /// </summary>
    public static LoraDatasetLimits FromModel(LoraDataset model, LoraDatasetLimits? fallback = null)
    {
        var basis = fallback?.Copy() ?? new LoraDatasetLimits();
        // ВИД ДАТАСЕТА берётся у модели, а не у прежних настроек: подставляя пределы
        // звуковой модели в датасет картинок, человек именно этого и хочет — собрать
        // датасет под неё, а пределы одного вида к другому неприменимы вовсе
        var media = LoraDatasetMedia.Normalize(model.Media);
        return new LoraDatasetLimits
        {
            Media = media,
            MaxWidth = model.Width > 0 ? model.Width : basis.MaxWidth,
            MaxHeight = model.Height > 0 ? model.Height : basis.MaxHeight,
            MaxKb = model.MaxKb > 0 ? model.MaxKb : basis.MaxKb,
            Format = PickFormat(model.Formats, basis.Format, media),
            MinItems = model.MinItems > 0 ? model.MinItems : basis.MinItems,
            MaxItems = model.MaxItems > 0 ? model.MaxItems : basis.MaxItems,
            MinSeconds = model.MinSeconds > 0 ? model.MinSeconds : basis.MinSeconds,
            MaxSeconds = model.MaxSeconds > 0 ? model.MaxSeconds : basis.MaxSeconds,
            SampleRate = model.SampleRate > 0 ? model.SampleRate : basis.SampleRate,
            Channels = model.Channels > 0 ? model.Channels : basis.Channels,
        }.Sane();
    }

    /// <summary>
    /// Какой формат брать из перечисленных моделью: PNG, если он есть; иначе первый
    /// понятный нам; ничего не сказано — оставить прежний.
    /// </summary>
    public static string PickFormat(IEnumerable<string>? formats, string current = "png",
        string media = LoraDatasetMedia.Image)
    {
        // у звука тем же правилом берётся WAV: он без потерь, и его читают все тренеры
        var audio = LoraDatasetMedia.Normalize(media) == LoraDatasetMedia.Audio;
        var allowed = audio ? AudioFormats : ["png", "jpeg"];
        var best = audio ? "wav" : "png";
        var known = (formats ?? []).Select(Normalize)
            .Where(f => Array.IndexOf(allowed, f) >= 0)
            .ToList();
        if (known.Count == 0)
        {
            return NormalizeFormat(current, media);
        }
        return known.Contains(best) ? best : known[0];
    }

    /// <summary>
    /// СВЕРКА С МОДЕЛЬЮ перед обучением. Возвращает готовые фразы о несовпадениях —
    /// пусто значит «сверять было не с чем либо всё в порядке».
    ///
    /// Считаются только несовпадения В БОЛЬШУЮ СТОРОНУ (кадры датасета крупнее, тяжелее
    /// или их больше, чем объявила модель) и несовпадение формата: датасет со значениями
    /// меньше объявленных — законная работа, и переспрашивать о ней незачем.
    /// </summary>
    public List<string> Compare(LoraDataset model)
    {
        var problems = new List<string>();
        var media = LoraDatasetMedia.Normalize(Media);
        var modelMedia = LoraDatasetMedia.Normalize(model.Media);
        // ВИД ДАТАСЕТА сверяется первым и только у модели, которая вообще что-то объявила:
        // молчащая модель не спорит ни с картинками, ни с записями
        if (model.HasLimits && media != modelMedia)
        {
            problems.Add(Loc.T("msg.lora.48", Loc.T("lora.limits.media." + media),
                Loc.T("lora.limits.media." + modelMedia)));
        }
        if (modelMedia == LoraDatasetMedia.Audio)
        {
            // у записи ширины и высоты нет вовсе — вместо них ДЛИТЕЛЬНОСТЬ, частота
            // дискретизации и число каналов
            if (model.MaxSeconds > 0 && MaxSeconds > model.MaxSeconds)
            {
                problems.Add(Loc.T("msg.lora.44", MaxSeconds, model.MaxSeconds));
            }
            if (model.SampleRate > 0 && SampleRate > model.SampleRate)
            {
                problems.Add(Loc.T("msg.lora.45", SampleRate, model.SampleRate));
            }
            if (model.Channels > 0 && Channels > model.Channels)
            {
                problems.Add(Loc.T("msg.lora.46", Channels, model.Channels));
            }
        }
        else
        {
            if (model.Width > 0 && MaxWidth > model.Width)
            {
                problems.Add(Loc.T("msg.lora.23", MaxWidth, model.Width));
            }
            if (model.Height > 0 && MaxHeight > model.Height)
            {
                problems.Add(Loc.T("msg.lora.24", MaxHeight, model.Height));
            }
        }
        if (model.MaxKb > 0 && MaxKb > model.MaxKb)
        {
            problems.Add(Loc.T("msg.lora.25", MaxKb, model.MaxKb));
        }
        if (model.MaxItems > 0 && MaxItems > model.MaxItems)
        {
            problems.Add(Loc.T("msg.lora.26", MaxItems, model.MaxItems));
        }
        var accepted = (model.Formats ?? []).Select(Normalize).Where(f => f.Length > 0).ToList();
        if (accepted.Count > 0 && !accepted.Contains(NormalizeFormat(Format, media)))
        {
            problems.Add(Loc.T("msg.lora.27", Format.ToUpperInvariant(),
                string.Join(", ", accepted.Select(f => f.ToUpperInvariant()))));
        }
        return problems;
    }

    /// <summary>
    /// Название формата к общему виду: справочник пишет их как угодно — «png», «.png»,
    /// «image/png», «JPG», — а сравнивать надо одно с одним.
    /// </summary>
    private static string Normalize(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return "";
        }
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }
        text = text.TrimStart('.', '*');
        return text switch
        {
            "jpg" or "jpe" or "jpeg" => "jpeg",
            _ => text,
        };
    }
}
