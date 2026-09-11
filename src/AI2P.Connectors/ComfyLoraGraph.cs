using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ПОДКЛЮЧЕНИЕ АДАПТЕРА LoRA К ГРАФУ ComfyUI (T-14-S1) — шаг 5 настройки справочника
/// (<see cref="LoraApply"/>, T-13-S1) в исполнении.
///
/// Способов ровно два, и выбирает между ними сам шаблон:
/// <list type="number">
/// <item>В шаблоне есть ПЛЕЙСХОЛДЕР (<c>{lora}</c>, <c>{loraStrength}</c>) — тогда тот, кто
/// писал шаблон, уже поставил узел адаптера туда, куда хотел, и нам остаётся подставить
/// имя файла и вес.</item>
/// <item>Плейсхолдера нет — узел ВСТАВЛЯЕТСЯ в граф. Именно так устроены поставляемые
/// шаблоны Kandinsky: заранее вписать туда узел адаптера нельзя (T-13-S1) — с пустым именем
/// файла ComfyUI откажется считать граф вообще, и модель перестала бы работать без LoRA.</item>
/// </list>
///
/// КУДА ВСТАВЛЯТЬ. <c>LoraLoaderModelOnly</c> стоит между загрузчиком весов и всем, что этот
/// загрузчик потребляет: он принимает MODEL и отдаёт MODEL. Поэтому находим узел-загрузчик,
/// запоминаем ВСЕХ его потребителей по входу <c>model</c>, вставляем цепочку адаптеров и
/// переводим потребителей на её конец. Несколько адаптеров выстраиваются в цепочку — так же,
/// как их соединяют руками в ComfyUI.
///
/// Загрузчика в графе не нашлось — это не «тихо ничего не делаем», а ошибка: человек назвал
/// объект с адаптером, и молча сгенерированный без адаптера кадр хуже отказа.
/// </summary>
public static class ComfyLoraGraph
{
    /// <summary>Узлы, дающие в граф саму модель (её выход и разрывается адаптером).</summary>
    private static readonly string[] ModelLoaders =
    [
        "UNETLoader", "UnetLoaderGGUF", "CheckpointLoaderSimple", "CheckpointLoader",
        "DiffusionModelLoader", "UNETLoaderGGUF",
    ];

    /// <summary>Узел подключения адаптера по умолчанию, если в настройке он не назван.</summary>
    public const string DefaultNode = "LoraLoaderModelOnly";

    /// <summary>В шаблоне есть плейсхолдер адаптера — подставлять, а не вставлять узел.</summary>
    public static bool HasPlaceholder(string template, LoraApply apply) =>
        apply.Placeholder.Length > 0 && template.Contains(apply.Placeholder, StringComparison.Ordinal);

    /// <summary>
    /// Подставить адаптер в плейсхолдеры шаблона: имя файла — внутрь кавычек, вес — числом
    /// вместе с кавычками (как {seed} и {width} у остальных параметров).
    /// </summary>
    public static string FillPlaceholders(string graph, LoraApply apply,
        IReadOnlyList<ObjectLoadLora> adapters)
    {
        var first = adapters.Count > 0 ? adapters[0] : null;
        var name = first is null ? "" : LoraName(first.File, apply);
        var strength = first?.Strength ?? apply.Strength;
        var result = graph.Replace(apply.Placeholder, JsonEscape(name), StringComparison.Ordinal);
        if (apply.StrengthPlaceholder.Length > 0)
        {
            result = result
                .Replace("\"" + apply.StrengthPlaceholder + "\"",
                    strength.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace(apply.StrengthPlaceholder,
                    strength.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>
    /// Вставить цепочку узлов адаптера в граф API-формата ComfyUI (id узла → узел).
    /// Возвращает изменённый граф; список адаптеров пуст — граф возвращается как есть.
    /// </summary>
    /// <exception cref="InvalidOperationException">В графе не нашлось узла-загрузчика модели —
    /// подключать адаптер не к чему.</exception>
    public static string Insert(string graph, LoraApply apply, IReadOnlyList<ObjectLoadLora> adapters)
    {
        if (adapters.Count == 0)
        {
            return graph;
        }
        var root = JsonNode.Parse(graph) as JsonObject
                   ?? throw new InvalidOperationException(Loc.T("msg.objectLoad.20"));
        var sourceId = FindModelSource(root)
                       ?? throw new InvalidOperationException(Loc.T("msg.objectLoad.21",
                           string.Join(", ", ModelLoaders)));

        // потребителей запоминаем ДО вставки: иначе первый же добавленный узел сам станет
        // потребителем выхода загрузчика и мы замкнули бы цепочку на себя
        var consumers = ConsumersOfModel(root, sourceId);
        var node = apply.Node.Length > 0 ? apply.Node : DefaultNode;
        var previous = sourceId;
        var nextId = NextFreeId(root);
        foreach (var adapter in adapters)
        {
            var id = nextId++.ToString(CultureInfo.InvariantCulture);
            root[id] = new JsonObject
            {
                ["class_type"] = node,
                ["inputs"] = new JsonObject
                {
                    ["lora_name"] = LoraName(adapter.File, apply),
                    ["strength_model"] = adapter.Strength,
                    ["model"] = new JsonArray(previous, 0),
                },
            };
            previous = id;
        }
        foreach (var (consumerId, input) in consumers)
        {
            (root[consumerId]!["inputs"] as JsonObject)![input] = new JsonArray(previous, 0);
        }
        return root.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>
    /// ИМЯ АДАПТЕРА так, как его понимает ComfyUI: путь внутри его каталога <c>loras</c>.
    /// Файл к этому моменту уже лежит в этом каталоге (его туда кладёт коннектор), поэтому
    /// достаточно имени файла; каталог из настройки (<see cref="LoraApply.Dir"/>) в имя
    /// не входит — ComfyUI подставляет его сам.
    /// </summary>
    public static string LoraName(string file, LoraApply apply)
    {
        // каталог настройки задан ПОЛНЫМ путём (свой ComfyUI, свои каталоги) — имя считаем
        // от него: у ComfyUI в lora_name допускается подкаталог
        if (apply.Dir.Length > 0 && Path.IsPathRooted(apply.Dir))
        {
            var dir = Path.GetFullPath(apply.Dir);
            var abs = Path.GetFullPath(file);
            if (abs.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return abs[(dir.Length + 1)..].Replace('\\', '/');
            }
        }
        return Path.GetFileName(file);
    }

    /// <summary>Узел, дающий модель: первый по списку известных загрузчиков.</summary>
    private static string? FindModelSource(JsonObject root)
    {
        foreach (var loader in ModelLoaders)
        {
            foreach (var (id, node) in root)
            {
                if (node is JsonObject obj &&
                    obj["class_type"]?.GetValue<string>() is { } cls &&
                    cls.Equals(loader, StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }
        }
        return null;
    }

    /// <summary>Кто берёт выход узла-загрузчика входом «model»: id узла и имя входа.</summary>
    private static List<(string Id, string Input)> ConsumersOfModel(JsonObject root, string sourceId)
    {
        var found = new List<(string, string)>();
        foreach (var (id, node) in root)
        {
            if (node is not JsonObject obj || obj["inputs"] is not JsonObject inputs)
            {
                continue;
            }
            foreach (var (name, value) in inputs)
            {
                if (!name.Equals("model", StringComparison.OrdinalIgnoreCase) ||
                    value is not JsonArray link || link.Count < 1)
                {
                    continue;
                }
                if (link[0]?.GetValue<string>() == sourceId)
                {
                    found.Add((id, name));
                }
            }
        }
        return found;
    }

    /// <summary>Свободный числовой id узла: графы ComfyUI нумеруют узлы строками-числами.</summary>
    private static int NextFreeId(JsonObject root)
    {
        var max = 0;
        foreach (var (id, _) in root)
        {
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                max = Math.Max(max, value);
            }
        }
        return max + 1;
    }

    /// <summary>Текст для подстановки внутрь JSON-строки шаблона (кавычки остаются в шаблоне).</summary>
    private static string JsonEscape(string text)
    {
        var serialized = JsonSerializer.Serialize(text, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        return serialized[1..^1];
    }
}
