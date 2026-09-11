using System.Text.Json;
using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// МЕДИАТЕКА ПРОЕКТА (T-111-S0) — ПРЕДСТАВЛЕНИЕ НАД ОБЪЕКТАМИ ПРОЕКТА, а не третий
/// параллельный список файлов.
///
/// РЕШЕНИЕ, ПРИНЯТОЕ ЗДЕСЬ (задание требовало выбрать ровно одно): медиаресурс отличается
/// от прочих объектов НОВЫМ ВИДОМ <see cref="ObjectKinds.Media"/>, а не отбором по
/// существующим видам. Отбор («всё, у чего путь кончается на .mp4») смешал бы снятый дубль
/// с эталонным кадром персонажа (<see cref="ObjectKinds.Image"/>) — а это вещи разного
/// назначения: кадр это ВХОД генерации, дубль это ВЫХОД, и попадать они должны в разные
/// списки. Обратно из такой смеси уже не разобрать, чьё что.
///
/// Отсюда вся медиатека — это <c>ObjectService.List(projectId, kinds: ["media"])</c> плюс
/// метаданные ролика в <c>meta_json</c> объекта (<see cref="MediaMeta"/>). Ни своей таблицы,
/// ни своей миграции схемы: список, иерархия, тэги, сервер-владелец, права и репликация
/// достаются медиатеке от объектов даром (T-259/T-266/T-102-S0).
///
/// ГДЕ ЛЕЖИТ САМ ФАЙЛ. Два законных места, и различает их приставка <c>store:</c>
/// (<see cref="ObjectFiles"/>):
/// <list type="bullet">
/// <item>файл, который ЧЕЛОВЕК выбрал сам, — в папке проекта, путь относительный ей;</item>
/// <item>файл, который сделала САМА ПРОГРАММА (снятый дубль, результат конвертора), — в
/// каталоге данных организации, <c>store:projects/PRJ-N/media/…</c>: папка проекта
/// пер-серверная и не реплицируется, и на соседнем сервере такого файла нет вовсе
/// (T-98-S0).</item>
/// </list>
/// Исключение из второго правила ровно одно — ПРОИЗВОДНЫЕ ФАЙЛЫ ЭКСПОРТА
/// (<c>ai2p_library.mlt</c> и родня): их человек открывает руками своим редактором, значит
/// лежать они обязаны там, где он их найдёт, — в папке проекта.
///
/// ПУТЬ ТОЛЬКО ОТНОСИТЕЛЬНЫЙ. Абсолютный путь внутри проекта редактора убивает переносимость
/// по кластеру: у соседа тот же ролик лежит по другому пути, и монтажный лист у него не
/// открывается. Проверка одна на всех — <see cref="IsValidPath"/>.
/// </summary>
public static class MediaLibrary
{
    /// <summary>Подкаталог медиатеки в хранилище организации: <c>projects/PRJ-N/media</c>.
    /// Туда ложится то, что программа сделала сама.</summary>
    public static string StoreDirRel(string projectSlug) => $"projects/{projectSlug}/media";

    /// <summary>Путь файла медиатеки в хранилище: <c>store:projects/PRJ-N/media/&lt;имя&gt;</c>.</summary>
    public static string StorePath(string projectSlug, string fileName) =>
        ObjectFiles.Store(StoreDirRel(projectSlug) + "/" + Path.GetFileName(fileName.Replace('\\', '/')));

    /// <summary>
    /// ПУТЬ ГОДИТСЯ ДЛЯ МЕДИАТЕКИ: относительный путь в папке проекта либо
    /// <c>store:</c>-путь каталога данных. Всё остальное — абсолютный путь, буква диска,
    /// <c>..</c>, сетевая шара, внешний адрес — не годится.
    /// </summary>
    public static bool IsValidPath(string? pathOrUrl)
    {
        var value = (pathOrUrl ?? "").Trim();
        if (value.Length == 0)
        {
            return false;
        }
        return ProjectFiles.IsRelative(ObjectFiles.Rel(value));
    }

    /// <summary>
    /// ПРИСТАВКА ТЭГА СЦЕНЫ (T-113-S0). Сцена ложится на существующую механику объектов
    /// ДВАЖДЫ и намеренно: точным значением в <see cref="MediaMeta.Scene"/> (его читает
    /// экспортёр — там нельзя ни потерять регистр, ни угадывать) и ТЭГОМ
    /// <c>scene:&lt;имя&gt;</c> (его читает человек). Тэг даёт даром всё, что у объектов
    /// проекта уже есть: вид списка «тэги» (T-266) раскладывает медиатеку по сценам сам,
    /// фильтр и отбор <c>ObjectService.List(tags:…)</c> работают без единой новой строки UI.
    ///
    /// <para>Дубль тэгом НЕ становится: тэг у проекта общий, и <c>take:2</c> слил бы вторые
    /// дубли всех сцен в одну кучу. Дубль — число в метаданных, и живёт он ВНУТРИ сцены.</para>
    ///
    /// <para>Запятая из имени сцены вырезается: у тэгов она разделитель
    /// (<see cref="TaskTags"/>), и тэга с запятой внутри не существует вовсе.</para>
    /// </summary>
    public const string SceneTagPrefix = "scene:";

    /// <summary>Тэг сцены по её имени; пустое имя — пустая строка (тэга нет).</summary>
    public static string SceneTag(string? scene)
    {
        var name = (scene ?? "").Replace(TaskTags.Separator, ' ').Trim();
        return name.Length == 0 ? "" : SceneTagPrefix + name;
    }
}

/// <summary>
/// МЕТАДАННЫЕ МЕДИАРЕСУРСА — то, чем ролик отличается от файла: чем он снят, к какой сцене
/// и дублю относится, сколько идёт и откуда взялся.
///
/// Живут в <c>meta_json</c> объекта, ключом <c>media</c>: <c>{"media":{…}}</c>. Своих колонок
/// нет намеренно — состав метаданных у монтажного шлюза, конвертора и MCP разный и будет
/// прирастать, а колонка на каждое поле означала бы миграцию схемы на каждый новый плагин.
/// Рядом в том же <c>meta_json</c> уже живёт <c>loraStrength</c> (T-14-S1), и разбор одного
/// ключа не мешает другому.
/// </summary>
public sealed class MediaMeta
{
    /// <summary>Ключ метаданных внутри <c>meta_json</c> объекта.</summary>
    public const string Key = "media";

    public const string Video = "video";
    public const string Audio = "audio";
    public const string Image = "image";
    public const string Subtitle = "subtitle";
    public const string Project = "project";

    /// <summary>Виды медиаресурса. <c>project</c> — файл проекта редактора (.mlt, .blend,
    /// .osp): он тоже лежит в медиатеке, потому что его делает шлюз.</summary>
    public static readonly string[] Kinds = [Video, Audio, Image, Subtitle, Project];

    /// <summary>Вид ресурса: <see cref="Kinds"/>.</summary>
    public string Kind { get; set; } = Video;

    /// <summary>Подпись — то, что человек читает в списке; пусто — берётся имя объекта.</summary>
    public string Caption { get; set; } = "";

    /// <summary>Сцена: свободная строка («3», «финал»).</summary>
    public string Scene { get; set; } = "";

    /// <summary>Дубль; 0 — дубли не считают.</summary>
    public int Take { get; set; }

    /// <summary>
    /// ПОРЯДОК на монтажном листе (T-113-S0) — то, чем ролик встаёт на дорожку впереди
    /// соседнего. 0 — порядок не задан, и тогда медиатека выстраивает ресурсы по коду
    /// объекта. Поле обязано быть СВОИМ, а не выводиться из номера объекта или времени
    /// создания: экспортёр (.mlt, .otio, .osp, .py для Blender) собирает дорожку именно
    /// по нему, а порядок съёмки и порядок в фильме — разные вещи.
    /// </summary>
    public int Order { get; set; }

    /// <summary>Длительность в секундах; 0 — неизвестна (картинка, титр).</summary>
    public double DurationSec { get; set; }

    /// <summary>ОТКУДА ВЗЯЛОСЬ — код задания, сделавшего файл (<c>T-15</c>). По нему из
    /// медиатеки видно, какую задачу перезапускать, чтобы дубль переснять.</summary>
    public string SourceTask { get; set; } = "";

    /// <summary>Код плагина, который файл сделал (<c>editor.shotcut</c>); пусто — человек
    /// добавил файл сам.</summary>
    public string SourcePlugin { get; set; } = "";

    /// <summary>Вид известен системе.</summary>
    public static bool IsKnownKind(string? kind) =>
        kind is not null && Array.IndexOf(Kinds, kind) >= 0;

    /// <summary>
    /// Метаданные из <c>meta_json</c> объекта; блока нет или он мусор — пустые метаданные
    /// с умолчаниями. Мусор в метаданных не должен ронять список медиатеки.
    /// </summary>
    public static MediaMeta Parse(string? metaJson)
    {
        var meta = new MediaMeta();
        if (string.IsNullOrWhiteSpace(metaJson))
        {
            return meta;
        }
        try
        {
            using var doc = JsonDocument.Parse(metaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty(Key, out var e) ||
                e.ValueKind != JsonValueKind.Object)
            {
                return meta;
            }
            var kind = JsonRead.Str(e, "kind", Video);
            meta.Kind = IsKnownKind(kind) ? kind : Video;
            meta.Caption = JsonRead.Str(e, "caption");
            meta.Scene = JsonRead.Str(e, "scene");
            meta.Take = Math.Max(0, JsonRead.Int(e, "take", 0));
            meta.Order = Math.Max(0, JsonRead.Int(e, "order", 0));
            meta.DurationSec = Math.Max(0, JsonRead.Num(e, "durationSec", 0));
            meta.SourceTask = JsonRead.Str(e, "sourceTask");
            meta.SourcePlugin = JsonRead.Str(e, "sourcePlugin");
        }
        catch (JsonException)
        {
            // meta_json правят руками и через API — испорченный блок это не повод падать
        }
        return meta;
    }

    /// <summary>
    /// Положить метаданные в <c>meta_json</c> объекта, СОХРАНИВ остальные его ключи:
    /// рядом лежит <c>loraStrength</c>, и переписать метаданные целиком значило бы молча
    /// потерять чужую настройку.
    /// </summary>
    public string Write(string? metaJson)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(metaJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(metaJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Name != Key)
                        {
                            fields[p.Name] = p.Value.Clone();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // испорченное содержимое не сохраняем — иначе объект стало бы не записать
            }
        }
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var (name, value) in fields)
            {
                w.WritePropertyName(name);
                value.WriteTo(w);
            }
            w.WritePropertyName(Key);
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            if (Caption.Length > 0)
            {
                w.WriteString("caption", Caption);
            }
            if (Scene.Length > 0)
            {
                w.WriteString("scene", Scene);
            }
            if (Take > 0)
            {
                w.WriteNumber("take", Take);
            }
            if (Order > 0)
            {
                w.WriteNumber("order", Order);
            }
            if (DurationSec > 0)
            {
                w.WriteNumber("durationSec", DurationSec);
            }
            if (SourceTask.Length > 0)
            {
                w.WriteString("sourceTask", SourceTask);
            }
            if (SourcePlugin.Length > 0)
            {
                w.WriteString("sourcePlugin", SourcePlugin);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
