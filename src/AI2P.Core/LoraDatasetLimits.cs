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

    /// <summary>Формат приведён к «png» либо «jpeg»: другого холст браузера не отдаёт.</summary>
    public static string NormalizeFormat(string? value) =>
        Normalize(value) == "jpeg" ? "jpeg" : "png";

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
                MaxWidth = JsonRead.Int(e, "maxWidth", 1024),
                MaxHeight = JsonRead.Int(e, "maxHeight", 1024),
                MaxKb = JsonRead.Int(e, "maxKb", 2048),
                Format = NormalizeFormat(JsonRead.Str(e, "format", "png")),
                MinItems = JsonRead.Int(e, "minItems", 0),
                MaxItems = JsonRead.Int(e, "maxItems", 0),
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
            ["maxWidth"] = MaxWidth,
            ["maxHeight"] = MaxHeight,
            ["maxKb"] = MaxKb,
            ["format"] = Format,
            ["minItems"] = MinItems,
            ["maxItems"] = MaxItems,
        };
        return node.ToJsonString();
    }

    /// <summary>Значения, при которых датасет остаётся рабочим: ноль в пределе кадра
    /// означал бы «кадров размером ноль», то есть добавить нельзя ни одного.</summary>
    public LoraDatasetLimits Sane()
    {
        MaxWidth = MaxWidth > 0 ? MaxWidth : 1024;
        MaxHeight = MaxHeight > 0 ? MaxHeight : 1024;
        MaxKb = MaxKb > 0 ? MaxKb : 2048;
        Format = NormalizeFormat(Format);
        MinItems = MinItems > 0 ? MinItems : 0;
        MaxItems = MaxItems > 0 ? MaxItems : 0;
        return this;
    }

    /// <summary>Точная копия — форма правит снимок, а не то, что лежит в записи.</summary>
    public LoraDatasetLimits Copy() => new()
    {
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        MaxKb = MaxKb,
        Format = Format,
        MinItems = MinItems,
        MaxItems = MaxItems,
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
        return new LoraDatasetLimits
        {
            MaxWidth = model.Width > 0 ? model.Width : basis.MaxWidth,
            MaxHeight = model.Height > 0 ? model.Height : basis.MaxHeight,
            MaxKb = model.MaxKb > 0 ? model.MaxKb : basis.MaxKb,
            Format = PickFormat(model.Formats, basis.Format),
            MinItems = model.MinItems > 0 ? model.MinItems : basis.MinItems,
            MaxItems = model.MaxItems > 0 ? model.MaxItems : basis.MaxItems,
        }.Sane();
    }

    /// <summary>
    /// Какой формат брать из перечисленных моделью: PNG, если он есть; иначе первый
    /// понятный нам; ничего не сказано — оставить прежний.
    /// </summary>
    public static string PickFormat(IEnumerable<string>? formats, string current = "png")
    {
        var known = (formats ?? []).Select(Normalize)
            .Where(f => f is "png" or "jpeg")
            .ToList();
        if (known.Count == 0)
        {
            return NormalizeFormat(current);
        }
        return known.Contains("png") ? "png" : known[0];
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
        if (model.Width > 0 && MaxWidth > model.Width)
        {
            problems.Add(Loc.T("msg.lora.23", MaxWidth, model.Width));
        }
        if (model.Height > 0 && MaxHeight > model.Height)
        {
            problems.Add(Loc.T("msg.lora.24", MaxHeight, model.Height));
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
        if (accepted.Count > 0 && !accepted.Contains(NormalizeFormat(Format)))
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
