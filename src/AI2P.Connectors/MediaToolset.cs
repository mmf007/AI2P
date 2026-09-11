using System.Globalization;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// ИНСТРУМЕНТЫ МЕДИАТЕКИ ПРОЕКТА ДЛЯ ИИ-АГЕНТА (T-113-S0) — первый шаг работы ИИ с монтажом:
/// <c>media_add</c> (положить ссылку на ролик), <c>media_list</c> (список с отбором),
/// <c>media_remove</c> (убрать запись, файл не трогая).
///
/// <para>СВОЕГО ХРАНИЛИЩА У ЭТИХ ИНСТРУМЕНТОВ НЕТ. Медиатека — ПРЕДСТАВЛЕНИЕ над объектами
/// проекта (T-259/T-266, решение T-111-S0): запись это объект вида
/// <see cref="ObjectKinds.Media"/>, метаданные ролика — <see cref="MediaMeta"/> в его
/// <c>meta_json</c>. Отсюда медиатека получает даром иерархию, тэги, три вида списка,
/// сервер-владельца, права и репликацию; третьего параллельного списка файлов в системе
/// не заводится.</para>
///
/// <para>КУДА ЛОЖАТСЯ СЦЕНА И ДУБЛЬ (решение этой задачи). Сцена — ДВАЖДЫ и намеренно:
/// точным значением в <see cref="MediaMeta.Scene"/> (его читает экспортёр, там угадывать
/// нельзя) и ТЭГОМ <c>scene:&lt;имя&gt;</c> (<see cref="MediaLibrary.SceneTag"/>) — тэг
/// включает уже готовый вид списка «тэги» и отбор <c>ObjectService.List(tags:…)</c>, то есть
/// сцены раскладываются по полкам без единой новой строки интерфейса. Дубль — только число
/// в метаданных: тэг у проекта общий, и <c>take:2</c> слил бы вторые дубли ВСЕХ сцен в одну
/// кучу. Иерархия объектов медиатекой не навязывается — параметр <c>parent</c> оставлен
/// человеку и шлюзам на случай, когда ролики хочется сложить внутрь объекта-сцены.</para>
///
/// <para>ПУТЬ ТОЛЬКО ОТНОСИТЕЛЬНЫЙ — папки проекта либо <c>store:</c>-путь каталога данных
/// организации. Своей проверки здесь НЕТ намеренно: отказ стоит в
/// <c>ObjectService.Validate</c> (T-111-S0), то есть один и тот же и для формы, и для API,
/// и для инструмента агента, и для будущего шлюза. Дублировать его тут значило бы завести
/// вторую редакцию правила, которая рано или поздно разойдётся с первой.</para>
///
/// <para>ЧТО НА ЭТОМ СТРОИТСЯ ДАЛЬШЕ (T-115…T-120): экспортёры в <c>.mlt</c>, <c>.otio</c>,
/// <c>.osp</c> и в <c>.py</c> для Blender собираются ИЗ медиатеки. Канонический вид данных —
/// НАШ, файл редактора производный, поэтому в записи есть всё, что нужно экспортёру: путь,
/// вид, длительность, ПОРЯДОК, сцена, дубль, подпись. Читать медиатеку экспортёру надо не
/// текстом этих инструментов, а прямо: <c>ObjectService.List(projectId, kinds:["media"])</c>
/// + <see cref="MediaMeta.Parse"/>.</para>
/// </summary>
public sealed class MediaToolset
{
    /// <summary>Сколько записей печатается в ответе media_list: медиатека большого ролика
    /// это сотни дублей, и весь список в контекст агента не нужен — дальше отбор.</summary>
    private const int MaxRows = 300;

    private readonly ObjectService _objects;
    private readonly TaskItem _current;
    private readonly string? _actorExecutorId;

    /// <summary>Язык ответов (T-190) — их читает агент; ставится через
    /// <see cref="AgentToolset.Language"/>.</summary>
    public string? Language { get; set; }

    /// <summary>Лог вызовов — для журнала работ (agent.tool_calls), как у остальных наборов.</summary>
    public List<string> CallLog { get; } = [];

    public MediaToolset(ObjectService objects, TaskItem current, string? actorExecutorId = null)
    {
        _objects = objects;
        _current = current;
        _actorExecutorId = actorExecutorId;
    }

    /// <summary>Медиатека публикуется задачам ПРОЕКТА: список медиаресурсов — у проекта,
    /// и задаче без проекта складывать ролик просто некуда.</summary>
    public bool CanUse => _current.ProjectId is { Length: > 0 };

    /// <summary>Опубликованные агенту инструменты. Текстов здесь нет (todo24): описания
    /// подставляются из справочника действий по кодам AI2P.Media.*.</summary>
    public static IReadOnlyList<FileToolset.ToolSpec> Specs { get; } =
    [
        new("media_add", "",
            """
            {
              "type": "object",
              "properties": {
                "path":        { "type": "string" },
                "kind":        { "type": "string", "enum": ["video", "audio", "image", "subtitle", "project"] },
                "name":        { "type": "string" },
                "caption":     { "type": "string" },
                "scene":       { "type": "string" },
                "take":        { "type": "integer" },
                "order":       { "type": "integer" },
                "durationSec": { "type": "number" },
                "tags":        { "type": "array", "items": { "type": "string" } },
                "sourceTask":  { "type": "string" },
                "sourcePlugin": { "type": "string" },
                "parent":      { "type": "string" }
              },
              "required": ["path"]
            }
            """),
        new("media_list", "",
            """
            {
              "type": "object",
              "properties": {
                "scene": { "type": "string" },
                "kind":  { "type": "string" },
                "tag":   { "type": "string" }
              }
            }
            """),
        new("media_remove", "",
            """
            {
              "type": "object",
              "properties": {
                "code": { "type": "string" },
                "path": { "type": "string" }
              }
            }
            """),
    ];

    /// <summary>Имена инструментов медиатеки — для диспетчеризации в AgentToolset.</summary>
    public static readonly HashSet<string> Names =
        Specs.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Выполнить вызов; результат (или текст ошибки) возвращается агенту.</summary>
    public string Execute(string name, JsonElement args)
    {
        try
        {
            if (!CanUse)
            {
                return Loc.In(Language, "prompt.media.3");
            }
            var result = name switch
            {
                "media_add" => Add(args),
                "media_list" => List(args),
                "media_remove" => Remove(args),
                _ => Loc.In(Language, "prompt.media.1", name),
            };
            CallLog.Add($"{name}({args.GetRawText()}) → {Preview(result)}");
            return result;
        }
        catch (Exception ex)
        {
            var error = Loc.In(Language, "prompt.media.2", name, ex.Message);
            CallLog.Add(error);
            return error;
        }
    }

    /// <summary>
    /// media_add — положить в медиатеку ССЫЛКУ на готовый файл (байты не копируются: у нас
    /// уже есть артефакты заданий, и второй экземпляр ролика никому не нужен).
    ///
    /// <para>ПОВТОРНЫЙ ВЫЗОВ С ТЕМ ЖЕ ПУТЁМ НЕ ПЛОДИТ ДУБЛЬ, а обновляет запись: задание
    /// перезапускают, и медиатека, которая после трёх попыток показывает один ролик трижды,
    /// бесполезна ровно так же, как пустая.</para>
    /// </summary>
    private string Add(JsonElement args)
    {
        var projectId = _current.ProjectId!;
        var path = Required(args, "path");
        var kind = (Optional(args, "kind") ?? MediaMeta.Video).Trim().ToLowerInvariant();
        if (kind.Length == 0)
        {
            kind = MediaMeta.Video;
        }
        if (!MediaMeta.IsKnownKind(kind))
        {
            return Loc.In(Language, "prompt.media.5", kind, string.Join(", ", MediaMeta.Kinds));
        }
        var scene = (Optional(args, "scene") ?? "").Trim();
        var meta = new MediaMeta
        {
            Kind = kind,
            Caption = (Optional(args, "caption") ?? "").Trim(),
            Scene = scene,
            Take = Num(args, "take") is { } take ? Math.Max(0, (int)take) : 0,
            Order = Num(args, "order") is { } order ? Math.Max(0, (int)order) : 0,
            DurationSec = Num(args, "durationSec") is { } d ? Math.Max(0, d) : 0,
            // ОТКУДА ВЗЯЛОСЬ по умолчанию — ТЕКУЩЕЕ задание: по этому полю из медиатеки
            // видно, какую задачу перезапускать, чтобы дубль переснять, и заполнять его
            // руками агент забудет ровно в тот раз, когда оно понадобится
            SourceTask = (Optional(args, "sourceTask") ?? _current.DisplayId).Trim(),
            SourcePlugin = (Optional(args, "sourcePlugin") ?? "").Trim(),
        };

        // тэги: что попросили + тэг сцены (см. решение в шапке класса)
        var tags = Strings(args, "tags");
        var sceneTag = MediaLibrary.SceneTag(scene);
        if (sceneTag.Length > 0)
        {
            tags.Add(sceneTag);
        }

        var same = _objects.List(projectId, kinds: [ObjectKinds.Media])
            .FirstOrDefault(o => string.Equals(o.PathOrUrl.Trim(), path,
                StringComparison.OrdinalIgnoreCase));
        if (same is not null)
        {
            var existing = _objects.Get(same.Id)!;
            existing.PathOrUrl = path;
            existing.MetaJson = meta.Write(existing.MetaJson);
            existing.Tags = TaskTags.Normalize([.. existing.Tags, .. tags]);
            var requested = (Optional(args, "name") ?? "").Trim();
            if (requested.Length > 0)
            {
                existing.Name = requested;
            }
            var updated = _objects.Update(existing, _actorExecutorId);
            return Loc.In(Language, "prompt.media.7",
                updated.DisplayId, updated.Name, kind, updated.PathOrUrl);
        }

        var item = new ObjectItem
        {
            ProjectId = projectId,
            ParentId = ParentId(args),
            Name = FreeName(projectId, args, meta, path),
            Type = ObjectKinds.Media,
            PathOrUrl = path,
            Description = meta.Caption,
            MetaJson = meta.Write("{}"),
            Tags = tags,
        };
        var created = _objects.Create(item, _actorExecutorId);
        return Loc.In(Language, "prompt.media.6",
            created.DisplayId, created.Name, kind, created.PathOrUrl);
    }

    /// <summary>
    /// media_list — медиатека проекта таблицей, с отбором по сцене, виду и тэгу.
    /// Порядок строк — тот, в котором ролики встанут на дорожку: сцена, порядок, дубль,
    /// код объекта. Порядок 0 («не задан») уезжает в конец сцены, а не в начало: иначе
    /// ролик без номера молча оказывался бы первым.
    /// </summary>
    private string List(JsonElement args)
    {
        var scene = (Optional(args, "scene") ?? "").Trim();
        var kind = (Optional(args, "kind") ?? "").Trim().ToLowerInvariant();
        var tag = (Optional(args, "tag") ?? "").Trim();
        var rows = _objects.List(_current.ProjectId!, kinds: [ObjectKinds.Media],
                tags: tag.Length > 0 ? [tag] : null)
            .Select(o => (Item: o, Meta: MediaMeta.Parse(o.MetaJson)))
            .Where(r => scene.Length == 0
                        || string.Equals(r.Meta.Scene, scene, StringComparison.CurrentCultureIgnoreCase))
            .Where(r => kind.Length == 0 || r.Meta.Kind == kind)
            .OrderBy(r => r.Meta.Scene, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Meta.Order == 0 ? int.MaxValue : r.Meta.Order)
            .ThenBy(r => r.Meta.Take)
            .ThenBy(r => DisplayIds.Order(r.Item.DisplayId))
            .ToList();
        if (rows.Count == 0)
        {
            return Loc.In(Language, "prompt.media.8");
        }
        var text = new StringBuilder();
        text.AppendLine(Loc.In(Language, "prompt.media.9", rows.Count));
        foreach (var (item, meta) in rows.Take(MaxRows))
        {
            text.AppendLine(string.Join(" | ",
                item.DisplayId,
                meta.Kind,
                meta.Scene,
                meta.Take.ToString(CultureInfo.InvariantCulture),
                meta.Order.ToString(CultureInfo.InvariantCulture),
                meta.DurationSec.ToString("0.###", CultureInfo.InvariantCulture),
                item.PathOrUrl,
                meta.Caption.Length > 0 ? meta.Caption : item.Name,
                meta.SourceTask));
        }
        if (rows.Count > MaxRows)
        {
            text.AppendLine(Loc.In(Language, "prompt.media.12", MaxRows, rows.Count));
        }
        return text.ToString();
    }

    /// <summary>
    /// media_remove — убрать запись из медиатеки. ФАЙЛ ПРИ ЭТОМ НЕ ТРОГАЕТСЯ: медиатека
    /// хранит ССЫЛКИ, и стереть чужой ролик, «прибираясь в списке», она не вправе —
    /// тем более что тот же файл бывает и артефактом задания.
    /// </summary>
    private string Remove(JsonElement args)
    {
        var code = (Optional(args, "code") ?? "").Trim();
        var path = (Optional(args, "path") ?? "").Trim();
        if (code.Length == 0 && path.Length == 0)
        {
            return Loc.In(Language, "prompt.media.13");
        }
        var found = _objects.List(_current.ProjectId!, kinds: [ObjectKinds.Media])
            .FirstOrDefault(o =>
                (code.Length > 0 && o.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase))
                || (code.Length == 0 && path.Length > 0
                    && o.PathOrUrl.Trim().Equals(path, StringComparison.OrdinalIgnoreCase)));
        if (found is null)
        {
            return Loc.In(Language, "prompt.media.10", code.Length > 0 ? code : path);
        }
        _objects.Delete(found.Id, _actorExecutorId);
        return Loc.In(Language, "prompt.media.11", found.DisplayId, found.Name, found.PathOrUrl);
    }

    /// <summary>Родитель новой записи по коду объекта (OBJ-7); пусто — запись корневая.
    /// Неизвестный код — молча корневая: медиатека иерархии не навязывает, и ронять
    /// добавление ролика из-за опечатки в необязательном поле незачем.</summary>
    private string? ParentId(JsonElement args)
    {
        var code = (Optional(args, "parent") ?? "").Trim();
        return code.Length == 0
            ? null
            : _objects.List(_current.ProjectId!)
                .FirstOrDefault(o => o.DisplayId.Equals(code, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Название, свободное в проекте: у объектов оно уникально (ObjectService.EnsureUniqueName),
    /// а медиатеку наполняет агент пачками — «сцена 3 дубль 2» у второго ролика встречается
    /// сплошь и рядом. Названное явно НЕ подгоняем: занятое имя лучше вернуть отказом, чем
    /// молча переименовать не туда.
    /// </summary>
    private string FreeName(string projectId, JsonElement args, MediaMeta meta, string path)
    {
        var requested = (Optional(args, "name") ?? "").Trim();
        if (requested.Length > 0)
        {
            return requested;
        }
        var wanted = meta.Caption.Length > 0
            ? meta.Caption
            : Path.GetFileName(ObjectFiles.Rel(path).Replace('\\', '/'));
        if (wanted.Length == 0)
        {
            wanted = ObjectKinds.Media;
        }
        var taken = _objects.List(projectId).Select(o => o.Name).ToList();
        bool Free(string name) =>
            !taken.Any(t => string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));
        if (Free(wanted))
        {
            return wanted;
        }
        for (var i = 2; i < 1000; i++)
        {
            if (Free($"{wanted} {i}"))
            {
                return $"{wanted} {i}";
            }
        }
        return $"{wanted} {Guid.NewGuid().ToString("N")[..6]}";
    }

    private string Required(JsonElement args, string name) =>
        Optional(args, name) is { Length: > 0 } value
            ? value.Trim()
            : throw new ArgumentException(Loc.In(Language, "prompt.media.4", name));

    private static string? Optional(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Число из аргументов — И ЧИСЛОМ, И СТРОКОЙ. Строкой оно приезжает от клиента
    /// командной строки (<c>ai2p media-add --durationSec 12.5</c>): там значения аргументов
    /// текстовые, и отказывать агенту в разборе «12.5» значило бы завести ему грабли на
    /// ровном месте. Разделитель дробной части — точка (InvariantCulture): строку пишет
    /// агент, а не человек.
    /// </summary>
    private static double? Num(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
        {
            return null;
        }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var number))
        {
            return number;
        }
        return v.ValueKind == JsonValueKind.String
               && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Список строк из аргументов: массив либо строка через запятую (так тэги
    /// приезжают от клиента командной строки).</summary>
    private static List<string> Strings(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v))
        {
            return [];
        }
        if (v.ValueKind == JsonValueKind.Array)
        {
            return TaskTags.Normalize(v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? ""));
        }
        return v.ValueKind == JsonValueKind.String ? TaskTags.Normalize(v.GetString()) : [];
    }

    private static string Preview(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
