using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-120-S0 — ШЛЮЗ В OPENSHOT (<c>.osp</c>, JSON), ЗАПАСНОЙ.
///
/// <para>ЧТО ЭТИ ПРОВЕРКИ СТЕРЕГУТ. Формат <c>.osp</c> — чистый JSON, и по формальным
/// признакам OpenShot лучший из четырёх кандидатов ветки: кроссплатформенный, бесплатный,
/// файл пишется напрямую и открывается двойным щелчком. Но рендера без окна у него НЕТ, а
/// репутация стабильности слабая, поэтому он объявлен ЗАПАСНЫМ — и это должно быть сказано
/// вслух в опыте и в документе, иначе агент возьмёт его первым и пообещает человеку ролик,
/// которого никто не посчитает. Отсюда проверка
/// <see cref="The_Experience_Says_Out_Loud_That_Shotcut_Comes_First"/> и отсутствие блока
/// <c>render</c>.</para>
///
/// <para>ЧЕМ <c>.osp</c> ОТЛИЧАЕТСЯ ОТ MLT И OTIO И ПОЧЕМУ ЭТО ПРОВЕРЯЕТСЯ ОТДЕЛЬНО: список
/// клипов у него ПЛОСКИЙ (<c>clips</c> — один массив на весь файл), и дорожку с местом на ней
/// называет САМ КЛИП полями <c>layer</c> и <c>position</c>. В форматах-последовательностях
/// клипы встают подряд сами, здесь же без начала все они легли бы друг на друга в нуле — это
/// не отказ, а молча испорченный монтаж, и ловится он только проверкой позиций
/// (<see cref="Clips_On_One_Layer_Stand_In_A_Row_And_Do_Not_Overlap"/>).</para>
///
/// <para>ЭТАЛОН СНЯТ ДВОЙНИКОМ ГЕНЕРАТОРА (<c>test/t120s0/osp.py</c> читает манифест прямо из
/// <see cref="PluginSeed"/>), а не написан на глаз. И проверок на него ДВЕ: текст совпал
/// посимвольно И текст разбирается разборщиком JSON — посимвольного совпадения мало, эталон
/// сам мог бы быть негодным JSON (наука T-117-S0).</para>
/// </summary>
public sealed class T120S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Каталог поставляемой документации — тем же правилом, что у T18S1Tests:
    /// корень ищется по AI2P.sln, а не складывается из «..» относительно вывода сборки.</summary>
    private static string DocRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return Path.Combine(dir.FullName, "doc");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    /// <summary>Манифест шлюза дистрибутива — тот же, что ложится в каталог данных.</summary>
    private static PluginManifest OpenShot() =>
        PluginManifest.Parse(PluginSeed.OpenShotJson)
        ?? throw new InvalidOperationException("манифест шлюза OpenShot негоден");

    // ---------- 1. Манифест ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ, и у него РОВНО ОДНО действие — сборка <c>.osp</c>. Имя
    /// инструмента своё (<c>openshot_timeline_write</c>, а не общий <c>timeline_write</c>):
    /// правила безопасности ищут действие по имени инструмента, и общее имя на четыре шлюза
    /// означало бы одно правило сразу на все четыре.
    /// </summary>
    [Fact]
    public void The_OpenShot_Manifest_Is_Read_Whole()
    {
        var manifest = OpenShot();

        Assert.Equal(PluginSeed.OpenShotCode, manifest.Code);
        Assert.Equal("editor.openshot", manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);

        var action = Assert.Single(manifest.Actions);
        Assert.Equal(PluginAction.RoleExport, action.Role);
        Assert.Equal("openshot_timeline_write", action.Tool);
        Assert.Equal("AI2P.Plugins.OpenShot.TimelineWrite", action.Code);
        // сборка .osp идёт и без установленного OpenShot: файл пишем мы, открывает человек
        Assert.False(action.NeedsSoftware);

        // РЕНДЕРА НЕТ И БЫТЬ НЕ ДОЛЖНО: openshot-qt считает ролик только своим окном, и
        // молча заведённый блок render означал бы обещание, которого система не выполнит
        Assert.Null(manifest.Render);
        Assert.Null(manifest.Convert);

        // тексты — на всех пяти языках: словари i18n плагину не правят, текст приходит файлом
        foreach (var lang in Loc.Languages)
        {
            Assert.NotEqual("", manifest.Description.Text(lang));
            Assert.NotEqual("", action.Title.Text(lang));
            Assert.NotEqual("", action.Description.Text(lang));
            Assert.NotEqual("", manifest.Software!.Hint.Text(lang));
        }

        // опыт: три записи, все с навыком video-edit — запись без навыка конкурирует за место
        // в промпте у КАЖДОЙ задачи организации (a8a79272)
        Assert.Equal(3, manifest.Experience.Count);
        Assert.All(manifest.Experience, e => Assert.Equal("video-edit", e.Skill));
    }

    /// <summary>
    /// ОПИСАНИЕ ФОРМАТА — то место, где .osp отличается от трёх остальных шлюзов: JSON с
    /// разделителем-запятой (без него получается не «слегка другой» файл, а негодный JSON),
    /// плоский список клипов и ДОРОЖКИ-СЛОИ со своими номерами. Номера слоёв не порядковые:
    /// OpenShot нумерует их 1000000, 2000000, и на эти числа ссылается каждый клип полем
    /// <c>layer</c> — порядковый номер дорожки тут не годится.
    /// </summary>
    [Fact]
    public void The_Format_Is_Json_With_Flat_Clips_On_Numbered_Layers()
    {
        var format = OpenShot().Export;

        Assert.NotNull(format);
        Assert.Equal("ai2p_library.osp", format!.File);
        Assert.Equal(TimelineFormat.EscapeJson, format.Escape);
        Assert.Equal(",\n", format.Separator);
        Assert.Equal(2, format.Tracks.Count);
        Assert.Equal("V1", format.Tracks[0].Id);
        Assert.Equal("2000000", format.Tracks[0].Layer);
        Assert.Equal("A1", format.Tracks[1].Id);
        Assert.Equal("1000000", format.Tracks[1].Layer);
        // у каждого вида медиа своя запись файла: у картинки has_single_image, у звука нет
        // видео — один шаблон на всех дал бы OpenShot заведомо неверные признаки ресурса
        Assert.Equal(3, format.ClipByKind.Count);
        Assert.Equal("FFmpegReader", format.ServiceOf(MediaMeta.Video));
        Assert.Equal("QtImageReader", format.ServiceOf(MediaMeta.Image));
    }

    /// <summary>
    /// ОПЫТ ПЛАГИНА ГОВОРИТ ПРЯМО ТРИ ВЕЩИ, ради которых он и написан: .osp — это JSON и
    /// пишется напрямую; пути только относительные; и главное — ЭТОТ ШЛЮЗ ЗАПАСНОЙ, первый
    /// выбор Shotcut/Kdenlive. Проверка нарочно смотрит на ТЕКСТ: эта оговорка и есть половина
    /// работы задания, и вычистить её правкой «для краткости» нельзя.
    /// </summary>
    [Fact]
    public void The_Experience_Says_Out_Loud_That_Shotcut_Comes_First()
    {
        var texts = OpenShot().Experience.Select(e => e.Text.Text("ru")).ToList();

        Assert.Contains(texts, t => t.Contains("JSON", StringComparison.Ordinal)
                                    && t.Contains("напрямую", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(texts, t => t.Contains("относительно", StringComparison.OrdinalIgnoreCase)
                                    && t.Contains("path", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("ЗАПАСНОЙ", StringComparison.Ordinal)
                                    && t.Contains("Shotcut", StringComparison.Ordinal)
                                    && t.Contains("стабильности", StringComparison.OrdinalIgnoreCase));
        // то же самое обязано быть сказано и по-английски: опыт уезжает в промпт на языке команды
        var english = OpenShot().Experience.Select(e => e.Text.Text("en")).ToList();
        Assert.Contains(english, t => t.Contains("BACKUP", StringComparison.Ordinal)
                                      && t.Contains("Shotcut", StringComparison.Ordinal));
    }

    /// <summary>Документ плагина назван на всех пяти языках, лежит на месте и несёт раздел про
    /// ОГРАНИЧЕНИЯ ПО ОПЕРАЦИОННЫМ СИСТЕМАМ: без него человек узнаёт про отсутствие рендера
    /// уже после того, как поручил агенту собрать ролик.</summary>
    [Fact]
    public void The_Plugin_Document_Exists_In_Every_Language()
    {
        var manifest = OpenShot();
        var doc = DocRoot();

        foreach (var lang in Loc.Languages)
        {
            Assert.True(manifest.Doc.TryGetValue(lang, out var rel), lang);
            Assert.Equal("plugins/editor.openshot.md", rel);
            var file = Path.Combine(doc, lang, rel!);
            Assert.True(File.Exists(file), file);
            var text = File.ReadAllText(file);
            Assert.Contains("editor.openshot", text, StringComparison.Ordinal);
            // раздел «Ограничения по операционным системам» — обязателен по заданию ветки
            Assert.Contains("## ", text, StringComparison.Ordinal);
            Assert.Contains("openshot-qt", text, StringComparison.Ordinal);
        }
    }

    // ---------- 2. Генератор: эталонный .osp ----------

    /// <summary>
    /// ЭТАЛОННЫЙ ФАЙЛ: три клипа — два видео на слое 2000000 и звук на слое 1000000, пути
    /// относительные, подпись с кавычками экранирована по правилам JSON. Эталон снят двойником
    /// генератора (<c>test/t120s0/osp.py</c>) с настоящего манифеста.
    /// </summary>
    [Fact]
    public void The_Timeline_Matches_The_Reference_File()
    {
        var text = TimelineExport.Build(OpenShot().Export!, Three(), "Ролик");

        Assert.Equal(Reference.Replace("\r\n", "\n"), text);
        // и он ДЕЙСТВИТЕЛЬНО JSON: посимвольного совпадения мало, эталон мог бы быть негодным
        using var parsed = JsonDocument.Parse(text);
        Assert.Equal(3, parsed.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal(3, parsed.RootElement.GetProperty("clips").GetArrayLength());
        // длина проекта — секундами числом, а не тайм-кодом: самая длинная дорожка (2 + 3 с)
        Assert.Equal(5, parsed.RootElement.GetProperty("duration").GetDouble());
        // подпись с кавычками уехала экранированной по правилам JSON, а не XML
        Assert.Contains("\\\"финал\\\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;", text, StringComparison.Ordinal);
    }

    /// <summary>Три клипа эталона: два видео и звук.</summary>
    private static List<TimelineClip> Three() =>
    [
        new()
        {
            Path = "media/sceneA.mp4", Kind = MediaMeta.Video, Name = "sceneA.mp4",
            Caption = "Вася бежит", Scene = "3", Take = 2, Order = 10, DurationSec = 2,
        },
        new()
        {
            Path = "media/sceneB.mp4", Kind = MediaMeta.Video, Name = "sceneB.mp4",
            Caption = "Вася падает", Scene = "4", Take = 1, Order = 20, DurationSec = 3,
        },
        new()
        {
            Path = "media/tema.mp3", Kind = MediaMeta.Audio, Name = "tema.mp3",
            Caption = "Тема \"финал\"", Scene = "3", DurationSec = 4,
        },
    ];

    private const string Reference =
        """
        {
         "id": "AI2P000001",
         "fps": { "num": 25, "den": 1 },
         "display_ratio": { "num": 16, "den": 9 },
         "pixel_ratio": { "num": 1, "den": 1 },
         "width": 1920,
         "height": 1080,
         "sample_rate": 48000,
         "channels": 2,
         "channel_layout": 3,
         "profile": "FHD PAL 1080p 25 fps",
         "duration": 5,
         "scale": 15.0,
         "tick_pixels": 100,
         "playhead_position": 0,
         "settings": {},
         "export_settings": null,
         "markers": [],
         "progress": [],
         "effects": [],
         "history": { "undo": [], "redo": [] },
         "layers": [
          { "id": "L1", "label": "A1", "number": 1000000, "y": 0, "lock": false },
          { "id": "L2", "label": "V1", "number": 2000000, "y": 0, "lock": false },
          { "id": "L3", "label": "", "number": 3000000, "y": 0, "lock": false },
          { "id": "L4", "label": "", "number": 4000000, "y": 0, "lock": false },
          { "id": "L5", "label": "", "number": 5000000, "y": 0, "lock": false }
         ],
         "files": [
          {
           "id": "F0",
           "path": "media/sceneA.mp4",
           "name": "sceneA.mp4",
           "media_type": "video",
           "type": "FFmpegReader",
           "duration": 2,
           "video_length": "50",
           "fps": { "num": 25, "den": 1 },
           "has_video": true,
           "has_audio": true,
           "has_single_image": false,
           "metadata": { "ai2p_scene": "3", "ai2p_take": "2", "ai2p_caption": "Вася бежит" }
          },
          {
           "id": "F1",
           "path": "media/sceneB.mp4",
           "name": "sceneB.mp4",
           "media_type": "video",
           "type": "FFmpegReader",
           "duration": 3,
           "video_length": "75",
           "fps": { "num": 25, "den": 1 },
           "has_video": true,
           "has_audio": true,
           "has_single_image": false,
           "metadata": { "ai2p_scene": "4", "ai2p_take": "1", "ai2p_caption": "Вася падает" }
          },
          {
           "id": "F2",
           "path": "media/tema.mp3",
           "name": "tema.mp3",
           "media_type": "audio",
           "type": "FFmpegReader",
           "duration": 4,
           "video_length": "100",
           "fps": { "num": 25, "den": 1 },
           "has_video": false,
           "has_audio": true,
           "has_single_image": false,
           "metadata": { "ai2p_scene": "3", "ai2p_take": "0", "ai2p_caption": "Тема \"финал\"" }
          }
         ],
         "clips": [
          {
           "id": "C0",
           "file_id": "F0",
           "title": "Вася бежит",
           "layer": 2000000,
           "position": 0,
           "start": 0,
           "end": 2,
           "duration": 2,
           "reader": {
            "type": "FFmpegReader",
            "path": "media/sceneA.mp4",
            "media_type": "video",
            "duration": 2,
            "video_length": "50",
            "fps": { "num": 25, "den": 1 }
           }
          },
          {
           "id": "C1",
           "file_id": "F1",
           "title": "Вася падает",
           "layer": 2000000,
           "position": 2,
           "start": 0,
           "end": 3,
           "duration": 3,
           "reader": {
            "type": "FFmpegReader",
            "path": "media/sceneB.mp4",
            "media_type": "video",
            "duration": 3,
            "video_length": "75",
            "fps": { "num": 25, "den": 1 }
           }
          },
          {
           "id": "C2",
           "file_id": "F2",
           "title": "Тема \"финал\"",
           "layer": 1000000,
           "position": 0,
           "start": 0,
           "end": 4,
           "duration": 4,
           "reader": {
            "type": "FFmpegReader",
            "path": "media/tema.mp3",
            "media_type": "audio",
            "duration": 4,
            "video_length": "100",
            "fps": { "num": 25, "den": 1 }
           }
          }
         ],
         "version": { "openshot-qt": "4.0.0", "libopenshot": "1.0.0" }
        }

        """;

    /// <summary>
    /// КЛИПЫ ОДНОГО СЛОЯ СТОЯТ В РЯД, А НЕ ДРУГ НА ДРУГЕ — главное отличие плоского формата.
    /// В MLT и OTIO дорожка это последовательность, и клипы встают подряд сами; здесь начало
    /// каждого клипа считаем мы (<c>{clip.positionSec}</c>), и ошибка тут даёт не отказ, а
    /// молча испорченный монтаж: все клипы в нуле, виден только верхний. Звуковая дорожка
    /// СВОЯ, и отсчёт на ней начинается заново с нуля.
    /// </summary>
    [Fact]
    public void Clips_On_One_Layer_Stand_In_A_Row_And_Do_Not_Overlap()
    {
        var text = TimelineExport.Build(OpenShot().Export!, Three(), "Ролик");

        using var parsed = JsonDocument.Parse(text);
        var clips = parsed.RootElement.GetProperty("clips").EnumerateArray()
            .Select(c => (Layer: c.GetProperty("layer").GetInt32(),
                          Position: c.GetProperty("position").GetDouble(),
                          Duration: c.GetProperty("duration").GetDouble()))
            .ToList();

        var video = clips.Where(c => c.Layer == 2000000).ToList();
        Assert.Equal(2, video.Count);
        Assert.Equal(0, video[0].Position);
        // второй начинается ровно там, где кончился первый
        Assert.Equal(video[0].Position + video[0].Duration, video[1].Position);
        // а звук — на своём слое и со своего нуля
        var audio = Assert.Single(clips.Where(c => c.Layer == 1000000));
        Assert.Equal(0, audio.Position);
    }

    /// <summary>
    /// JSON ОСТАЁТСЯ ГОДНЫМ ПРИ ЛЮБОМ ЧИСЛЕ КЛИПОВ — вот где ловится ошибка разделителя: один
    /// клип (запятой быть НЕ должно) и три клипа (две запятых), и при этом звуковой дорожки
    /// нет вовсе, то есть пустой кусок в склейку попасть не должен. Ресурс без длительности
    /// (картинка) получает не ноль, а умолчание: клип нулевой длины — это молча пропавший кадр.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void The_Osp_Stays_Valid_Json_With_Any_Number_Of_Clips(int count)
    {
        var clips = Enumerable.Range(0, count).Select(i => new TimelineClip
        {
            Path = $"media/scene{i}.png", Kind = MediaMeta.Image, Name = $"scene{i}.png",
        }).ToList();

        var text = TimelineExport.Build(OpenShot().Export!, clips, "Ролик");

        using var parsed = JsonDocument.Parse(text);
        Assert.Equal(count, parsed.RootElement.GetProperty("files").GetArrayLength());
        Assert.Equal(count, parsed.RootElement.GetProperty("clips").GetArrayLength());
        var first = parsed.RootElement.GetProperty("files")[0];
        Assert.True(first.GetProperty("has_single_image").GetBoolean());
        Assert.Equal("QtImageReader", first.GetProperty("type").GetString());
        Assert.Equal(TimelineExport.FallbackSec, first.GetProperty("duration").GetDouble());
    }

    /// <summary>
    /// ДОБАВКИ ПЛОСКОГО ФОРМАТА НЕ ТРОГАЮТ ТРИ ОСТАЛЬНЫХ ШЛЮЗА. Ради <c>.osp</c> в общий
    /// движок добавлены <c>{clip.positionSec}</c>, <c>{track.layer}</c> и поля дорожки,
    /// видные записи дорожки. Сторож стоит здесь потому, что ломается это ЧУЖОЙ правкой —
    /// правкой шаблонов OpenShot: у форматов-последовательностей место клипа считается самим
    /// редактором, и лишнее «position» в их файлах сдвинуло бы весь монтаж.
    /// </summary>
    [Fact]
    public void The_Flat_Layout_Additions_Do_Not_Change_The_Sequence_Gateways()
    {
        var clips = Three();

        var mlt = TimelineExport.Build(PluginManifest.Parse(PluginSeed.ShotcutJson)!.Export!,
            clips, "Ролик");
        var otio = TimelineExport.Build(PluginManifest.Parse(PluginSeed.ResolveJson)!.Export!,
            clips, "Ролик");

        Assert.DoesNotContain("position", mlt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("layer", mlt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"position\"", otio, StringComparison.Ordinal);
        // и оба по-прежнему собираются целиком: неизвестных плейсхолдеров не осталось
        Assert.DoesNotContain("{clip.", mlt, StringComparison.Ordinal);
        Assert.DoesNotContain("{track.", otio, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(otio);
        Assert.Equal("Timeline.1", parsed.RootElement.GetProperty("OTIO_SCHEMA").GetString());
    }

    // ---------- 3. Пути ----------

    private int _stands;

    /// <summary>Стенд: проект с папкой, задача проекта и шлюз OpenShot.</summary>
    private (GatewayToolset Tools, string ProjectId, string Folder) Stand()
    {
        var number = ++_stands;
        var folder = Path.Combine(_f.Dir, "openshot-project" + number);
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик OpenShot " + number, folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        var tools = new GatewayToolset([new GatewayPlugin(OpenShot())], _f.Objects, task, folder,
            _f.Files.DataDir, project.Name);
        return (tools, project.Id, folder);
    }

    private void AddMedia(string projectId, string name, string path, double seconds,
        string kind = MediaMeta.Video)
    {
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = ObjectKinds.Media,
            PathOrUrl = path,
            MetaJson = new MediaMeta { Kind = kind, DurationSec = seconds }.Write("{}"),
        }, null);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>
    /// КАЖДЫЙ ПУТЬ В .OSP — ТОЛЬКО ОТНОСИТЕЛЬНЫЙ, и это родной вид формата: OpenShot сам
    /// хранит пути относительно каталога проекта. Ресурс хранилища организации лежит ВНЕ папки
    /// проекта, поэтому копируется рядом с файлом: ссылка на него иначе была бы либо
    /// абсолютной, либо с <c>..</c>, то есть неверной на любом другом компьютере кластера.
    /// Путь стоит в файле ДВАЖДЫ — в <c>files</c> и в <c>reader</c> клипа, — и относительными
    /// обязаны быть оба.
    /// </summary>
    [Fact]
    public void Every_Path_In_The_Osp_Is_Relative()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        var stored = Path.Combine(_f.Files.DataDir, "projects", "PRJ-O", "media");
        Directory.CreateDirectory(stored);
        File.WriteAllText(Path.Combine(stored, "sceneB.mp4"), "кадры дубля");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        AddMedia(projectId, "снятое", "store:projects/PRJ-O/media/sceneB.mp4", 3);

        var answer = tools.ExecuteAsync("openshot_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        var file = Path.Combine(folder, "ai2p_library.osp");
        Assert.True(File.Exists(file), answer);
        var text = File.ReadAllText(file);
        using var parsed = JsonDocument.Parse(text);
        var paths = parsed.RootElement.GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("path").GetString()!).ToList();
        var readers = parsed.RootElement.GetProperty("clips").EnumerateArray()
            .Select(c => c.GetProperty("reader").GetProperty("path").GetString()!).ToList();

        Assert.Equal(["media/sceneA.mp4", "ai2p_media/sceneB.mp4"], paths);
        Assert.Equal(paths, readers);
        Assert.True(File.Exists(Path.Combine(folder, "ai2p_media", "sceneB.mp4")));
        Assert.DoesNotContain(folder, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", text, StringComparison.Ordinal);
    }

    /// <summary>Медиатека пуста — понятный отказ и НИКАКОГО файла: пустой проект, открытый в
    /// OpenShot, выглядит поломкой шлюза.</summary>
    [Fact]
    public void An_Empty_Library_Writes_Nothing()
    {
        var (tools, _, folder) = Stand();

        var answer = tools.ExecuteAsync("openshot_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(File.Exists(Path.Combine(folder, "ai2p_library.osp")));
        Assert.Equal(Loc.T("prompt.gateway.5"), answer);
    }

    /// <summary>
    /// ОТКАЗ ПО ПУТИ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ. Ограничение каталогом у файлового
    /// шлюза работает БУКВАЛЬНО — мы пишем JSON и кода не исполняем, — и файла при отказе не
    /// появляется вовсе. На Windows «/tmp/x» считается АБСОЛЮТНЫМ путём и уехал бы в C:\tmp.
    /// </summary>
    [Theory]
    [InlineData("../снаружи.osp")]
    [InlineData(@"C:\снаружи.osp")]
    [InlineData("/tmp/снаружи.osp")]
    public void Export_Refuses_A_Path_Outside_The_Project_Folder(string path)
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);

        var answer = tools.ExecuteAsync("openshot_timeline_write",
            Args(JsonSerializer.Serialize(new { file = path })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains(path, answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "ai2p_library.osp")));
        Assert.Empty(Directory.GetFiles(_f.Dir, "снаружи.osp", SearchOption.AllDirectories));
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ К ОТКАЗУ: путь ВНУТРИ папки проекта проходит, и файл ложится именно
    /// туда. Без него проверка выше доказывала бы лишь то, что не работает ничего.
    /// </summary>
    [Fact]
    public void Export_Accepts_A_Path_Inside_The_Project_Folder()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);

        var answer = tools.ExecuteAsync("openshot_timeline_write",
            Args(JsonSerializer.Serialize(new { file = "монтаж/своё.osp" })),
            CancellationToken.None).GetAwaiter().GetResult();

        var file = Path.Combine(folder, "монтаж", "своё.osp");
        Assert.True(File.Exists(file), answer);
        // путь клипа считается от каталога ВЫХОДНОГО файла, а не от папки проекта, и ресурс,
        // который оттуда иначе не адресовать (ушёл бы «..»), копируется рядом с файлом
        using var parsed = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("ai2p_media/sceneA.mp4",
            parsed.RootElement.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.True(File.Exists(Path.Combine(folder, "монтаж", "ai2p_media", "sceneA.mp4")));
    }

    // ---------- 4. Софт: поставить нечем, можно только показать ----------

    /// <summary>
    /// СОФТ НЕ УСТАНАВЛИВАЕТСЯ, А УКАЗЫВАЕТСЯ РУКАМИ, и это видно ПО ИСХОДУ ПОИСКА: пустой код
    /// пакета даёт <see cref="PluginSoftwareStatus.NeedsManualPath"/> («укажите, где он уже
    /// стоит»), а не <see cref="PluginSoftwareStatus.CanInstall"/> («Установить»). Под Windows
    /// OpenShot отдаётся только установщиком, портативного архива у проекта нет, а мы
    /// распаковываем архивы и чужих установщиков не запускаем — ссылка и размер названы в
    /// подсказке, чтобы человек понял, почему кнопки нет.
    /// </summary>
    [Fact]
    public void The_Software_Cannot_Be_Installed_Only_Pointed_At()
    {
        var software = OpenShot().Software!;
        Assert.Equal("", software.Package);          // ставить нечем — и это НАМЕРЕННО
        Assert.False(software.Required);
        Assert.Equal("openshotPath", software.PathKey);
        Assert.Contains("openshot-qt.exe", software.System!.Commands);
        // версию OpenShot по командной строке не отдаёт — проверяется НАЛИЧИЕ файла
        Assert.Equal("", software.System.VersionArgs);

        var missing = PluginSoftwareProbe.Find(software, manualPath: "", searchPath: "");

        Assert.Equal(PluginSoftwareStatus.NeedsManualPath, missing.Status);
        Assert.False(missing.Ready);
        Assert.Equal(PluginLifecycle.ProbingSoftware,
            PluginLifecycle.Shown(PluginStates.Initialized, missing.Status));
        // и человеку сказано, что делать: подсказка объясняет, почему кнопки «Установить» нет
        Assert.Contains("openshot-qt", software.Hint.Text("ru"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("github.com/OpenShot", software.Hint.Text("en"), StringComparison.Ordinal);
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ: показанный руками OpenShot НАХОДИТСЯ — иначе проверка выше доказывала
    /// бы лишь то, что не находится ничего. Человек показывает и файл программы, и каталог,
    /// куда он её поставил.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Manually_Pointed_OpenShot_Is_Found(bool pointAtFolder)
    {
        var software = OpenShot().Software!;
        var dir = Path.Combine(_f.Dir, "openshot-home" + (pointAtFolder ? "-dir" : "-file"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "openshot-qt.exe");
        File.WriteAllText(exe, "не настоящая программа, но файл есть");

        var found = PluginSoftwareProbe.Find(software, pointAtFolder ? dir : exe, searchPath: "");

        Assert.Equal(PluginSoftwareStatus.Found, found.Status);
        Assert.True(found.Ready);
        Assert.Equal(exe, found.Path);
        Assert.True(PluginLifecycle.Publishes(PluginStates.Initialized, found.Status));
    }

    // ---------- 5. Манифест дистрибутива ----------

    /// <summary>Манифест ложится в каталог данных файлом дистрибутива и повторную запись
    /// переживает: у него своя версия, а не общая версия сида.</summary>
    [Fact]
    public void The_Manifest_Is_Written_To_The_Data_Dir()
    {
        PluginSeed.Write(_f.Files);
        var path = _f.Files.Abs(PluginManifest.PathOf(PluginSeed.OpenShotCode));
        Assert.True(File.Exists(path));
        var stamp = File.GetLastWriteTimeUtc(path);

        PluginSeed.Write(_f.Files); // идемпотентно: та же версия — файл не трогается

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(PluginSeed.OpenShotCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.OpenShotCode)!.Code);
    }
}
