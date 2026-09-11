using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// ПАРАМЕТРИЧЕСКИЙ ЭКСПОРТЁР МОНТАЖНОГО ЛИСТА (T-115-S0) — общий движок для ВСЕХ шлюзов
/// в видеоредакторы. Вид выходного формата описан НЕ кодом, а блоком <c>export</c> манифеста
/// плагина — ровно так же, как секция <c>lora</c> профайла модели описывает пять шагов
/// обучения (T-13-S1), и ровно по той же причине: у нас четыре редактора и один и тот же
/// монтажный лист, и четыре копии одного генератора разъехались бы на первой же правке.
///
/// <para>ЧТО ЭТО ДАЁТ СОСЕДЯМ ПО ВЕТКЕ. Новый шлюз (OTIO для DaVinci Resolve T-117-S0,
/// <c>.osp</c> OpenShot T-120-S0, скрипт Python для Blender VSE T-118-S0) — это ФАЙЛ
/// МАНИФЕСТА, а не класс: меняются шаблоны и способ экранирования, движок тот же.</para>
///
/// <para>УСТРОЙСТВО — ТРИ УРОВНЯ ШАБЛОНОВ, и их хватает на все четыре формата:</para>
/// <list type="number">
/// <item><b>документ</b> (<c>document</c>) — каркас файла; в него подставляются собранные
/// куски <c>{clips}</c>, <c>{tracks}</c>, <c>{trackRefs}</c>;</item>
/// <item><b>клип</b> (<c>clip</c>) — описание ОДНОГО ресурса медиатеки (в MLT это
/// <c>&lt;producer&gt;</c>, в OTIO — <c>media_reference</c>, в .osp — элемент <c>files</c>);</item>
/// <item><b>дорожка</b> (<c>track</c> + <c>entry</c> + <c>trackRef</c>) — раскладка клипов
/// по дорожкам: какие виды медиа на какую дорожку идут, задаёт список <c>tracks</c>.</item>
/// </list>
///
/// <para>ЭКРАНИРОВАНИЕ — ЧАСТЬ ФОРМАТА, а не забота автора шаблона: <c>xml</c>, <c>json</c>,
/// <c>python</c>, <c>none</c> (<see cref="TimelineFormat.Escape"/>). Подпись «Сцена 3 «дубль»»
/// в XML обязана уехать с <c>&amp;quot;</c>, а в JSON — с <c>\"</c>, и полагаться тут на
/// внимательность того, кто пишет манифест, нельзя: испорченный файл проекта редактор
/// открывает молчаливой пустотой.</para>
///
/// <para>ПУТИ ТОЛЬКО ОТНОСИТЕЛЬНЫЕ (правило ветки T-110-S0). Абсолютный <c>resource</c>
/// убивает переносимость по кластеру: у соседа тот же ролик лежит по другому пути. Поэтому
/// путь клипа считается ОТ КАТАЛОГА ВЫХОДНОГО ФАЙЛА, а ресурс из хранилища организации
/// (<c>store:</c>) при экспорте копируется в подкаталог рядом с ним
/// (<see cref="TimelineFormat.CopyStore"/>): хранилище лежит вне папки проекта, и ссылка на
/// него из файла проекта была бы либо абсолютной, либо с <c>..</c> — то есть неверной на
/// любом другом компьютере.</para>
/// </summary>
public static class TimelineExport
{
    /// <summary>Длительность ресурса, у которого она неизвестна (картинка, титр), — секунды.
    /// Ноль в монтажном листе означал бы клип нулевой длины, то есть молча пропавший кадр.</summary>
    public const double FallbackSec = 5;

    /// <summary>
    /// СОБРАТЬ ТЕКСТ ФАЙЛА ПРОЕКТА по формату и списку клипов. Клипы приходят уже в том
    /// порядке, в котором встают на дорожку (медиатека сортирует их по сцене, порядку и дублю).
    /// </summary>
    /// <param name="format">Вид формата из манифеста плагина.</param>
    /// <param name="clips">Ресурсы медиатеки с путями, уже приведёнными к каталогу файла.</param>
    /// <param name="title">Название проекта — оно видно человеку в редакторе.</param>
    public static string Build(TimelineFormat format, IReadOnlyList<TimelineClip> clips,
        string title)
    {
        var fps = format.Fps > 0 ? format.Fps : 25;
        // РАСКЛАДКА ПО ДОРОЖКАМ. Дорожек в описании может не быть вовсе (плоский формат вроде
        // списка команд Blender) — тогда все клипы идут одной безымянной дорожкой
        var tracks = format.Tracks.Count > 0
            ? format.Tracks
            : [new TimelineFormat.TrackDef { Id = "V1" }];
        var placed = new List<(TimelineFormat.TrackDef Track, List<int> Clips)>();
        var used = new bool[clips.Count];
        foreach (var track in tracks)
        {
            var own = new List<int>();
            for (var i = 0; i < clips.Count; i++)
            {
                // клип попадает НА ПЕРВУЮ ПОДХОДЯЩУЮ дорожку и только на неё: тот же ролик,
                // положенный на две дорожки сразу, редактор покажет наложением самого на себя
                if (!used[i] && track.Takes(clips[i].Kind))
                {
                    own.Add(i);
                    used[i] = true;
                }
            }
            placed.Add((track, own));
        }

        var clipText = new List<string>();
        // {entries} — ВСЕ клипы одним списком, вне раскладки по дорожкам: в MLT это папка
        // проекта (main_bin) редактора Shotcut, в .osp OpenShot — плоский список files
        var allEntries = new List<string>();
        for (var i = 0; i < clips.Count; i++)
        {
            var fields = ClipFields(format, clips[i], i, fps);
            Piece(clipText, Fill(format.ClipTemplate(clips[i].Kind), fields, format.Escape));
            Piece(allEntries, Fill(format.Entry, fields, format.Escape));
        }

        var trackText = new List<string>();
        var refText = new List<string>();
        var docFrames = 0;
        for (var t = 0; t < placed.Count; t++)
        {
            var (track, own) = placed[t];
            var entries = new List<string>();
            // ДЛИНА ДОРОЖКИ СЧИТАЕТСЯ ДО ЗАПИСЕЙ, а не вместе с ними (T-120-S0): записи
            // дорожки видят и поля самой дорожки ({track.layer} у .osp), а те содержат её
            // длину — иначе получалась бы ссылка на ещё не посчитанную величину
            var frames = own.Sum(i => Frames(clips[i].DurationSec, fps));
            docFrames = Math.Max(docFrames, frames);
            var fields = TrackFields(track, t, own.Count, frames, fps);
            // МЕСТО КЛИПА НА ДОРОЖКЕ, кадров от её начала. Форматам, где дорожка — это
            // ПОСЛЕДОВАТЕЛЬНОСТЬ (playlist в MLT, children в OTIO), оно не нужно вовсе: клипы
            // идут подряд сами. А в .osp OpenShot список клипов ПЛОСКИЙ, и каждый клип несёт
            // свои position и layer — без начала все клипы легли бы друг на друга в нуле
            var offset = 0;
            foreach (var i in own)
            {
                var clipFields = ClipFields(format, clips[i], i, fps, offset);
                foreach (var (key, value) in fields)
                {
                    clipFields[key] = value;
                }
                Piece(entries, Fill(format.Entry, clipFields, format.Escape));
                offset += Frames(clips[i].DurationSec, fps);
            }
            fields["track.entries"] = new Raw(string.Join(format.Separator, entries));
            Piece(trackText, Fill(format.Track, fields, format.Escape));
            Piece(refText, Fill(format.TrackRef, fields, format.Escape));
        }

        var doc = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["doc.title"] = title,
            ["doc.generator"] = "AI2P",
            ["doc.fps"] = fps.ToString(CultureInfo.InvariantCulture),
            ["doc.width"] = format.Width.ToString(CultureInfo.InvariantCulture),
            ["doc.height"] = format.Height.ToString(CultureInfo.InvariantCulture),
            ["doc.count"] = clips.Count.ToString(CultureInfo.InvariantCulture),
            ["doc.tracks"] = placed.Count.ToString(CultureInfo.InvariantCulture),
            ["doc.frames"] = docFrames.ToString(CultureInfo.InvariantCulture),
            // ДЛИНА ВСЕГО ЛИСТА В СЕКУНДАХ (T-120-S0): у .osp длина проекта — секунды числом,
            // а не тайм-код и не кадры
            ["doc.durationSec"] = Seconds(docFrames, fps),
            ["doc.length"] = Timecode(docFrames, fps),
            ["doc.out"] = Timecode(Math.Max(0, docFrames - 1), fps),
            ["clips"] = new Raw(string.Join(format.Separator, clipText)),
            ["entries"] = new Raw(string.Join(format.Separator, allEntries)),
            ["tracks"] = new Raw(string.Join(format.Separator, trackText)),
            ["trackRefs"] = new Raw(string.Join(format.Separator, refText)),
        };
        return Fill(format.Document, doc, format.Escape);
    }

    /// <summary>
    /// ОДИН СОБРАННЫЙ КУСОК В СПИСОК — и ПУСТОЙ КУСОК В НЕГО НЕ ПОПАДАЕТ (T-117-S0). Куски
    /// склеиваются разделителем (<see cref="TimelineFormat.Separator"/>), а у формата, где
    /// шаблон куска не описан вовсе (у OTIO нет отдельного списка клипов), пустые куски дали
    /// бы строку из одних запятых — то есть негодный JSON на ровном месте.
    /// </summary>
    private static void Piece(List<string> to, string text)
    {
        if (text.Length > 0)
        {
            to.Add(text);
        }
    }

    /// <param name="offsetFrames">Начало клипа на дорожке, кадров от её начала (T-120-S0);
    /// вне раскладки по дорожкам — ноль.</param>
    private static Dictionary<string, object> ClipFields(TimelineFormat format, TimelineClip clip,
        int index, double fps, int offsetFrames = 0)
    {
        var seconds = clip.DurationSec > 0 ? clip.DurationSec : FallbackSec;
        var frames = Frames(seconds, fps);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["clip.positionFrames"] = offsetFrames.ToString(CultureInfo.InvariantCulture),
            ["clip.positionSec"] = Seconds(offsetFrames, fps),
            ["clip.endSec"] = Seconds(offsetFrames + frames, fps),
            ["clip.id"] = format.ClipIdPrefix + index.ToString(CultureInfo.InvariantCulture),
            ["clip.index"] = index.ToString(CultureInfo.InvariantCulture),
            ["clip.number"] = (index + 1).ToString(CultureInfo.InvariantCulture),
            ["clip.path"] = clip.Path,
            ["clip.name"] = clip.Name,
            ["clip.caption"] = clip.Caption.Length > 0 ? clip.Caption : clip.Name,
            ["clip.scene"] = clip.Scene,
            ["clip.take"] = clip.Take.ToString(CultureInfo.InvariantCulture),
            ["clip.order"] = clip.Order.ToString(CultureInfo.InvariantCulture),
            ["clip.kind"] = clip.Kind,
            ["clip.service"] = format.ServiceOf(clip.Kind),
            // ЧАСТОТА КАДРОВ НУЖНА И ВНУТРИ КЛИПА (T-117-S0): в OTIO каждая величина времени
            // несёт свой rate, и брать её из шаблона числом нельзя — настройка записи плагина
            // (WithSettings) меняет fps, а вписанное в манифест число осталось бы прежним
            ["clip.fps"] = fps.ToString(CultureInfo.InvariantCulture),
            ["clip.in"] = Timecode(0, fps),
            // OUT — ПОСЛЕДНИЙ КАДР, а не длина: в MLT граница включающая, и out, равный
            // длине, даёт лишний кадр в конце каждого клипа
            ["clip.out"] = Timecode(Math.Max(0, frames - 1), fps),
            ["clip.length"] = Timecode(frames, fps),
            ["clip.frames"] = frames.ToString(CultureInfo.InvariantCulture),
            ["clip.durationSec"] = seconds.ToString("0.###", CultureInfo.InvariantCulture),
        };
    }

    private static Dictionary<string, object> TrackFields(TimelineFormat.TrackDef track, int index,
        int count, int frames, double fps) =>
        new(StringComparer.Ordinal)
        {
            ["track.id"] = track.Id,
            // ВИД ДОРОЖКИ САМОГО ФОРМАТА (T-117-S0): в OTIO у дорожки обязательное поле
            // kind со значениями Video / Audio, и «V1» ему не годится — имя дорожки видит
            // человек, а вид читает редактор
            ["track.kind"] = track.Kind,
            // НОМЕР ДОРОЖКИ В ТЕРМИНАХ ФОРМАТА (T-120-S0): у .osp дорожка это «слой» со своим
            // числом (1000000, 2000000 — так их нумерует сам OpenShot), и каждый клип
            // ссылается на него полем layer. Порядковый {track.number} тут не годится
            ["track.layer"] = track.Layer.Length > 0
                ? track.Layer
                : (index + 1).ToString(CultureInfo.InvariantCulture),
            ["track.index"] = index.ToString(CultureInfo.InvariantCulture),
            ["track.number"] = (index + 1).ToString(CultureInfo.InvariantCulture),
            ["track.name"] = track.Id,
            ["track.hide"] = track.Hide,
            ["track.count"] = count.ToString(CultureInfo.InvariantCulture),
            ["track.frames"] = frames.ToString(CultureInfo.InvariantCulture),
            ["track.length"] = Timecode(frames, fps),
            ["track.out"] = Timecode(Math.Max(0, frames - 1), fps),
        };

    /// <summary>Кадров в отрезке; меньше одного кадра не бывает — клип нулевой длины это
    /// молча пропавший кадр.</summary>
    public static int Frames(double seconds, double fps) =>
        Math.Max(1, (int)Math.Round((seconds > 0 ? seconds : FallbackSec) * fps,
            MidpointRounding.AwayFromZero));

    /// <summary>Секунды числом (<c>2.5</c>) — разделитель дробной части точка, как и у
    /// тайм-кода: величину читает программа, а не человек с русской локалью.</summary>
    public static string Seconds(int frames, double fps) =>
        (frames / (fps > 0 ? fps : 25)).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Тайм-код MLT: <c>ЧЧ:ММ:СС.ммм</c>. Разделитель дробной части — точка
    /// (InvariantCulture): файл читает программа, а не человек с русской локалью.</summary>
    public static string Timecode(int frames, double fps)
    {
        var total = frames / (fps > 0 ? fps : 25);
        var span = TimeSpan.FromSeconds(total);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}",
            (int)span.TotalHours, span.Minutes, span.Seconds, span.Milliseconds);
    }

    /// <summary>Кусок, который подставляется в шаблон КАК ЕСТЬ: собранные блоки клипов и
    /// дорожек уже экранированы внутри себя, и второе экранирование превратило бы XML в текст.</summary>
    private sealed record Raw(string Text);

    private static string Fill(string template, Dictionary<string, object> fields, string escape)
    {
        if (template.Length == 0)
        {
            return "";
        }
        var text = new StringBuilder(template.Length + 64);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                text.Append(template[i]);
                continue;
            }
            var end = template.IndexOf('}', i + 1);
            if (end < 0)
            {
                text.Append(template[i]);
                continue;
            }
            var name = template[(i + 1)..end];
            if (!fields.TryGetValue(name, out var value))
            {
                // НЕИЗВЕСТНЫЙ ПЛЕЙСХОЛДЕР ОСТАЁТСЯ КАК ЕСТЬ: в шаблоне встречаются фигурные
                // скобки самого формата (JSON, словари Python), и вырезать их значило бы
                // сломать любой не-XML шлюз
                text.Append(template[i]);
                continue;
            }
            text.Append(value is Raw raw ? raw.Text : Escape((string)value, escape));
            i = end;
        }
        return text.ToString();
    }

    /// <summary>Экранирование значения по виду формата.</summary>
    public static string Escape(string value, string escape) => escape switch
    {
        TimelineFormat.EscapeXml => value.Replace("&", "&amp;").Replace("<", "&lt;")
            .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;"),
        // JSON и Python: строковый литерал в кавычках. JsonEncodedText не годится — она
        // экранирует и не-ASCII, а кириллическая подпись в .osp должна остаться читаемой
        TimelineFormat.EscapeJson or TimelineFormat.EscapePython =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
                .Replace("\r", "\\r").Replace("\t", "\\t"),
        _ => value,
    };
}

/// <summary>
/// ОДИН РЕСУРС МЕДИАТЕКИ, приведённый к тому виду, в котором его берёт экспортёр: путь уже
/// посчитан относительно каталога выходного файла, вид известен, длительность подставлена.
/// Своей связи с базой у него нет намеренно — по этой же записи собирается и <c>.mlt</c>,
/// и <c>.otio</c>, и <c>.osp</c>, и скрипт Blender.
/// </summary>
public sealed class TimelineClip
{
    /// <summary>Путь ОТНОСИТЕЛЬНО каталога выходного файла, разделитель «/».</summary>
    public string Path { get; set; } = "";

    /// <summary>Вид ресурса: <see cref="MediaMeta.Kinds"/>.</summary>
    public string Kind { get; set; } = MediaMeta.Video;

    public string Name { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Scene { get; set; } = "";
    public int Take { get; set; }
    public int Order { get; set; }
    public double DurationSec { get; set; }
}

/// <summary>
/// ВИД ФОРМАТА — блок <c>export</c> манифеста плагина. Разбирается тем же правилом, что весь
/// манифест (T-111-S0): неполнота переживается, негодный блок это <c>null</c>, а не исключение.
/// </summary>
public sealed class TimelineFormat
{
    public const string EscapeXml = "xml";
    public const string EscapeJson = "json";
    public const string EscapePython = "python";
    public const string EscapeNone = "none";

    /// <summary>ИМЯ СВОЕГО ФАЙЛА. Агент пишет ТОЛЬКО его: файл человека
    /// (<c>проект.mlt</c>, открытый в Shotcut) наш экспорт не трогает никогда — иначе
    /// сохранение из редактора и запись агента затирают друг друга (правило ветки).</summary>
    public string File { get; set; } = "";

    /// <summary>Экранирование значений: <see cref="EscapeXml"/> и родня.</summary>
    public string Escape { get; set; } = EscapeNone;

    /// <summary>Кадров в секунду монтажного листа.</summary>
    public double Fps { get; set; } = 25;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    /// <summary>Приставка идентификатора клипа в файле: <c>producer</c> → <c>producer0</c>.</summary>
    public string ClipIdPrefix { get; set; } = "clip";

    /// <summary>
    /// ЧТО СТАВИТСЯ МЕЖДУ ПОВТОРЯЮЩИМИСЯ КУСКАМИ (T-117-S0): между клипами, записями дорожки,
    /// дорожками и ссылками на них. У XML разделителя нет и быть не должно (элементы просто
    /// идут подряд) — поэтому умолчание пустое и вид MLT от этого поля не меняется. А вот у
    /// JSON-форматов (OTIO у DaVinci Resolve, <c>.osp</c> у OpenShot) элементы массива
    /// разделяются ЗАПЯТОЙ, и без неё получается не «слегка другой» файл, а негодный JSON.
    /// </summary>
    public string Separator { get; set; } = "";

    /// <summary>Копировать ли ресурсы хранилища организации (<c>store:</c>) рядом с выходным
    /// файлом: без этого ссылка на них была бы либо абсолютной, либо с <c>..</c>.</summary>
    public bool CopyStore { get; set; } = true;

    /// <summary>Подкаталог рядом с выходным файлом, куда ложатся копии ресурсов хранилища.</summary>
    public string StoreDir { get; set; } = "ai2p_media";

    /// <summary>Служба чтения ресурса по виду медиа (<c>avformat</c>, <c>qimage</c>) —
    /// подставляется как <c>{clip.service}</c>.</summary>
    public Dictionary<string, string> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Шаблон документа целиком; в нём стоят <c>{clips}</c>, <c>{tracks}</c>,
    /// <c>{trackRefs}</c>.</summary>
    public string Document { get; set; } = "";

    /// <summary>Шаблон одного клипа; переопределение по виду медиа — <see cref="ClipByKind"/>.</summary>
    public string Clip { get; set; } = "";

    /// <summary>Шаблон клипа для отдельного вида медиа (картинке в MLT нужна другая служба
    /// и своя длина).</summary>
    public Dictionary<string, string> ClipByKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Шаблон дорожки; внутри стоит <c>{track.entries}</c>.</summary>
    public string Track { get; set; } = "";

    /// <summary>
    /// Шаблон одной записи дорожки (ссылка на клип). Ему доступны И поля клипа, И поля
    /// ДОРОЖКИ, на которую он лёг (<c>{track.layer}</c>, <c>{track.id}</c>), и начало клипа
    /// на этой дорожке (<c>{clip.positionSec}</c>) — так устроены плоские форматы, где список
    /// клипов один на весь файл, а дорожку и место называет сам клип (<c>.osp</c> OpenShot).
    /// В общем списке <c>{entries}</c>, который идёт мимо раскладки по дорожкам, полей
    /// дорожки нет и место равно нулю.
    /// </summary>
    public string Entry { get; set; } = "";

    /// <summary>Шаблон ссылки на дорожку в сборке (<c>&lt;track producer=…&gt;</c> в tractor).</summary>
    public string TrackRef { get; set; } = "";

    /// <summary>Дорожки и то, какие виды медиа на них идут.</summary>
    public List<TrackDef> Tracks { get; set; } = [];

    /// <summary>Формат описан: есть имя файла и каркас документа.</summary>
    public bool IsValid => File.Length > 0 && Document.Length > 0;

    /// <summary>Шаблон клипа для этого вида медиа.</summary>
    public string ClipTemplate(string kind) =>
        ClipByKind.TryGetValue(kind, out var own) && own.Length > 0 ? own : Clip;

    /// <summary>Служба чтения для этого вида медиа; не названа — пусто.</summary>
    public string ServiceOf(string kind) =>
        Services.TryGetValue(kind, out var service) ? service : "";

    /// <summary>Дорожка монтажного листа.</summary>
    public sealed class TrackDef
    {
        /// <summary>Имя дорожки, видное человеку в редакторе: <c>V1</c>, <c>A1</c>.</summary>
        public string Id { get; set; } = "";

        /// <summary>Вид дорожки в терминах самого формата (<c>Video</c> / <c>Audio</c> в
        /// OTIO) — <c>{track.kind}</c>. Пусто — формату вид дорожки не нужен (так в MLT,
        /// где дорожки различает <see cref="Hide"/>).</summary>
        public string Kind { get; set; } = "";

        /// <summary>Номер дорожки в терминах формата — <c>{track.layer}</c>. У OpenShot это
        /// «слой» (<c>1000000</c>, <c>2000000</c>), на который ссылается каждый клип; пусто —
        /// подставится порядковый номер дорожки.</summary>
        public string Layer { get; set; } = "";

        /// <summary>Виды медиа, которые на неё идут; пусто — любые.</summary>
        public List<string> Kinds { get; set; } = [];

        /// <summary>Что у дорожки спрятано (<c>video</c> у звуковой) — <c>{track.hide}</c>.</summary>
        public string Hide { get; set; } = "";

        /// <summary>Берёт ли дорожка этот вид медиа.</summary>
        public bool Takes(string kind) =>
            Kinds.Count == 0 || Kinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));

        internal static TrackDef? From(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var track = new TrackDef
            {
                Id = JsonRead.Str(e, "id"),
                Kind = JsonRead.Str(e, "kind"),
                Layer = JsonRead.Str(e, "layer"),
                Kinds = JsonRead.Strings(e, "kinds"),
                Hide = JsonRead.Str(e, "hide"),
            };
            return track.Id.Length > 0 ? track : null;
        }
    }

    /// <summary>Разобрать блок <c>export</c>; null — блока нет или он неполон (нет имени
    /// файла или каркаса документа): шлюз без формата умеет только рендер.</summary>
    public static TimelineFormat? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var escape = JsonRead.Str(v, "escape", EscapeNone).ToLowerInvariant();
        var format = new TimelineFormat
        {
            File = JsonRead.Str(v, "file"),
            Escape = escape is EscapeXml or EscapeJson or EscapePython ? escape : EscapeNone,
            Fps = JsonRead.Num(v, "fps", 25),
            Width = JsonRead.Int(v, "width", 1920),
            Height = JsonRead.Int(v, "height", 1080),
            ClipIdPrefix = JsonRead.Str(v, "clipIdPrefix", "clip"),
            // РАЗДЕЛИТЕЛЬ ЧИТАЕТСЯ БЕЗ ОБРЕЗКИ КРАЁВ: у него пробел И ЕСТЬ значение, а
            // JsonRead.Str обрезает края — «,\n» приезжал бы как «,» (T-120-S0)
            Separator = JsonRead.Exact(v, "separator"),
            CopyStore = JsonRead.Bool(v, "copyStore", true),
            StoreDir = JsonRead.Str(v, "storeDir", "ai2p_media"),
            Document = Lines(v, "document"),
            Clip = Lines(v, "clip"),
            Track = Lines(v, "track"),
            Entry = Lines(v, "entry"),
            TrackRef = Lines(v, "trackRef"),
        };
        if (v.TryGetProperty("clipByKind", out var byKindLines)
            && byKindLines.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in byKindLines.EnumerateObject())
            {
                format.ClipByKind[p.Name] = Text(p.Value);
            }
        }
        if (v.TryGetProperty("services", out var services) && services.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in services.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    format.Services[p.Name] = p.Value.GetString() ?? "";
                }
            }
        }
        if (v.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
        {
            format.Tracks = [.. tracks.EnumerateArray().Select(TrackDef.From)
                .Where(t => t is not null).Select(t => t!)];
        }
        return format.IsValid ? format : null;
    }

    /// <summary>
    /// ШАБЛОН ИЗ МАНИФЕСТА — строкой ЛИБО СПИСКОМ СТРОК (склеивается переводом строки).
    /// Список нужен по-человечески: каркас файла проекта это два десятка строк XML, и в
    /// одной строке JSON с «\n» его не правит никто — а править их будут авторы остальных
    /// трёх шлюзов.
    /// </summary>
    private static string Lines(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? Text(v) : "";

    private static string Text(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Array => string.Join("\n", v.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())),
        _ => "",
    };
}

/// <summary>
/// РЕНДЕР БЕЗ ОКНА — блок <c>render</c> манифеста. Из четырёх редакторов ветки такой рендер
/// есть только у MLT (<c>melt</c> / <c>qmelt</c> Shotcut), и ради него шлюз Shotcut делался
/// первым: для системы, где работу делает агент, «посчитать ролик без человека» — решающее
/// свойство.
///
/// <para>ПЕРЕМЕННЫЕ ОКРУЖЕНИЯ — часть описания, а не код: на headless Linux <c>melt</c> без
/// <c>QT_QPA_PLATFORM=offscreen</c> не стартует вовсе («could not connect to display»), а на
/// Windows эта же переменная не нужна и вредна. Поэтому у окружения есть отбор по системе
/// (<see cref="EnvOs"/>).</para>
/// </summary>
public sealed class RenderCommand
{
    /// <summary>Аргументы командной строки; <c>{file}</c> — наш монтажный лист,
    /// <c>{out}</c> — файл результата.</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>Расширение файла результата по умолчанию (<c>.mp4</c>).</summary>
    public string OutExt { get; set; } = ".mp4";

    /// <summary>
    /// ЗАПУСКАТЬ ТОЛЬКО СВОЙ СОБРАННЫЙ ФАЙЛ (T-118-S0). У Shotcut «что рендерить» может
    /// назвать агент — melt читает XML и кода не исполняет. А у Blender рендер это
    /// <c>--python &lt;файл&gt;</c>, то есть ИСПОЛНЕНИЕ ФАЙЛА: разреши агенту назвать файл —
    /// и он напишет свой Python инструментом записи файлов и запустит его нашими руками, в
    /// обход всех правил (Python внутри Blender открывает любой файл через <c>open()</c>,
    /// ограничение путём там иллюзорно). Поэтому у такого шлюза параметр <c>file</c> не
    /// объявляется агенту вовсе и не читается: исполняется РОВНО файл из
    /// <see cref="TimelineFormat.File"/>, который собрали мы сами по своему шаблону.
    /// </summary>
    public bool OwnFileOnly { get; set; }

    /// <summary>Переменные окружения процесса рендера.</summary>
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Система, на которой ставятся переменные окружения: <c>linux</c>,
    /// <c>windows</c>, <c>macos</c>; пусто — на любой.</summary>
    public string EnvOs { get; set; } = "";

    /// <summary>Сколько ждать рендера, секунд.</summary>
    public int TimeoutSec { get; set; } = 3600;

    /// <summary>Ставятся ли переменные окружения на ЭТОЙ системе.</summary>
    public bool EnvHere() => EnvOs.Length == 0 || EnvOs.ToLowerInvariant() switch
    {
        "windows" => OperatingSystem.IsWindows(),
        "linux" => OperatingSystem.IsLinux(),
        "macos" => OperatingSystem.IsMacOS(),
        _ => true,
    };

    /// <summary>Команда описана: есть хоть один аргумент.</summary>
    public bool IsValid => Args.Count > 0;

    internal static RenderCommand? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var command = new RenderCommand
        {
            Args = JsonRead.Strings(v, "args"),
            OutExt = JsonRead.Str(v, "outExt", ".mp4"),
            OwnFileOnly = JsonRead.Bool(v, "ownFileOnly", false),
            EnvOs = JsonRead.Str(v, "envOs"),
            TimeoutSec = Math.Max(1, JsonRead.Int(v, "timeoutSec", 3600)),
        };
        if (v.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in env.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    command.Env[p.Name] = p.Value.GetString() ?? "";
                }
            }
        }
        return command.IsValid ? command : null;
    }
}
