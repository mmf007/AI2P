using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-117-S0 — ШЛЮЗ В DAVINCI RESOLVE (free): экспорт OTIO, софт не ставим.
///
/// <para>ГЛАВНОЕ, ЧТО ЭТИ ПРОВЕРКИ СТЕРЕГУТ: бесплатный Resolve автоматизировать нельзя
/// (внешний скриптовый API в free закрыт, а с 19.1 закрыт окончательно), поэтому у плагина
/// РОВНО ОДНО действие — собрать <c>.otio</c>, — а блока <c>render</c> нет и появиться он не
/// должен: молча заведённый рендер означал бы обещание, которого система не выполнит.</para>
///
/// <para>ПОЧЕМУ ЭТАЛОН ПОСИМВОЛЬНО И ПОЧЕМУ ЕЩЁ И РАЗБОР JSON. OTIO — это JSON, и в отличие
/// от XML у него элементы массива разделяются ЗАПЯТОЙ: движок экспортёра склеивает куски
/// подряд, и без разделителя (<see cref="TimelineFormat.Separator"/>) получается не «слегка
/// другой» файл, а негодный JSON, который Resolve отвергает целиком. Поэтому проверок две:
/// текст совпал с эталоном И текст разбирается разборщиком JSON.</para>
///
/// <para>СОФТ: у этого плагина <c>package</c> ПУСТ намеренно — дистрибутив 3–4 ГБ отдаётся за
/// формой регистрации, поставить его за человека нельзя. Именно из пустого кода пакета
/// растёт состояние «укажите, где он уже стоит» (<see cref="PluginSoftwareStatus.NeedsManualPath"/>),
/// а не «Установить»; проверка этого — <see cref="The_Software_Cannot_Be_Installed_Only_Pointed_At"/>.</para>
/// </summary>
public sealed class T117S0Tests : IDisposable
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
    private static PluginManifest Resolve() =>
        PluginManifest.Parse(PluginSeed.ResolveJson)
        ?? throw new InvalidOperationException("манифест шлюза DaVinci Resolve негоден");

    // ---------- 1. Манифест ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ, и у него РОВНО ОДНО действие. Имя инструмента своё
    /// (<c>resolve_timeline_write</c>, а не общий <c>timeline_write</c>): правила безопасности
    /// ищут действие по имени инструмента, и общее имя на четыре шлюза означало бы одно
    /// правило на все четыре сразу.
    /// </summary>
    [Fact]
    public void The_Resolve_Manifest_Is_Read_Whole()
    {
        var manifest = Resolve();

        Assert.Equal(PluginSeed.ResolveCode, manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);

        var action = Assert.Single(manifest.Actions);
        Assert.Equal(PluginAction.RoleExport, action.Role);
        Assert.Equal("resolve_timeline_write", action.Tool);
        Assert.Equal("AI2P.Plugins.Resolve.TimelineWrite", action.Code);
        // сборка .otio идёт и без Resolve: файл пишем мы, а открывает его человек
        Assert.False(action.NeedsSoftware);

        // РЕНДЕРА НЕТ И БЫТЬ НЕ МОЖЕТ: бесплатный Resolve не автоматизируется вовсе
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

        // опыт: три записи, все с навыком video-edit (запись без навыка конкурирует за место
        // в промпте у КАЖДОЙ задачи организации — a8a79272)
        Assert.Equal(3, manifest.Experience.Count);
        Assert.All(manifest.Experience, e => Assert.Equal("video-edit", e.Skill));

        var format = manifest.Export;
        Assert.NotNull(format);
        Assert.Equal("ai2p_scenes.otio", format!.File);
        Assert.Equal(TimelineFormat.EscapeJson, format.Escape);
        // разделитель обязателен: без запятой между элементами массива JSON негоден
        Assert.Equal(",\n", format.Separator);
        Assert.Equal(2, format.Tracks.Count);
        Assert.Equal("Video", format.Tracks[0].Kind);
        Assert.Equal("Audio", format.Tracks[1].Kind);
    }

    /// <summary>
    /// ОПЫТ ПЛАГИНА ГОВОРИТ ПРЯМО О ТРЁХ ВЕЩАХ, из-за которых он и написан: free не
    /// автоматизируется и импортирует человек; под Linux нужен транскод плагином ffmpeg;
    /// OTIO — формат обмена, оформление он не переносит. Проверка нарочно смотрит на ТЕКСТ:
    /// эти записи и есть половина работы шлюза, и вычистить их правкой «для краткости» нельзя.
    /// </summary>
    [Fact]
    public void The_Experience_Says_Out_Loud_That_A_Human_Imports_The_File()
    {
        var texts = Resolve().Experience.Select(e => e.Text.Text("ru")).ToList();

        Assert.Contains(texts, t => t.Contains("не автоматизируется", StringComparison.OrdinalIgnoreCase)
                                    && t.Contains("Import", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)
                                    && t.Contains("H.264", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("обмена", StringComparison.OrdinalIgnoreCase));
        // то же самое обязано быть сказано и на английском: опыт уезжает в промпт на языке команды
        var english = Resolve().Experience.Select(e => e.Text.Text("en")).ToList();
        Assert.Contains(english, t => t.Contains("Resolve Studio", StringComparison.Ordinal));
    }

    /// <summary>Документ плагина назван на всех пяти языках и лежит там же, где документы
    /// моделей: без него кнопка «i» в форме плагина ведёт в никуда.</summary>
    [Fact]
    public void The_Plugin_Document_Exists_In_Every_Language()
    {
        var manifest = Resolve();
        var doc = DocRoot();

        foreach (var lang in Loc.Languages)
        {
            Assert.True(manifest.Doc.TryGetValue(lang, out var rel), lang);
            var file = Path.Combine(doc, lang, rel!);
            Assert.True(File.Exists(file), file);
            var text = File.ReadAllText(file);
            Assert.Contains("editor.resolve", text, StringComparison.Ordinal);
        }
    }

    // ---------- 2. Эталонный .otio ----------

    /// <summary>
    /// ЭТАЛОННЫЙ МОНТАЖНЫЙ ЛИСТ ПОСИМВОЛЬНО плюс разбор JSON. Эталон снят с НАСТОЯЩЕГО
    /// манифеста (<c>test/t117s0/otio.py</c>), а не написан на глаз: параметрический экспортёр
    /// собирает файл по шаблонам, и опечатка в шаблоне даёт молчаливо негодный JSON.
    /// </summary>
    [Fact]
    public void The_Timeline_Matches_The_Reference_File()
    {
        var text = TimelineExport.Build(Resolve().Export!,
        [
            new TimelineClip
            {
                Path = "media/sceneA.mp4", Kind = MediaMeta.Video, Name = "sceneA.mp4",
                Caption = "Вася бежит", Scene = "3", Take = 2, Order = 10, DurationSec = 2,
            },
            new TimelineClip
            {
                Path = "media/tema.mp3", Kind = MediaMeta.Audio, Name = "tema.mp3",
                Caption = "Тема \"финал\"", Scene = "3", DurationSec = 4,
            },
        ], "Ролик");

        Assert.Equal(Reference.Replace("\r\n", "\n"), text);
        // и он ДЕЙСТВИТЕЛЬНО JSON: посимвольного совпадения мало, эталон мог бы быть негодным
        using var parsed = JsonDocument.Parse(text);
        Assert.Equal("Timeline.1", parsed.RootElement.GetProperty("OTIO_SCHEMA").GetString());
        // подпись с кавычками уехала экранированной по правилам JSON, а не XML
        Assert.Contains("\\\"финал\\\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;", text, StringComparison.Ordinal);
    }

    /// <summary>Эталон: два клипа на двух дорожках, длительности в кадрах при 25 кадрах/с
    /// (2 с → 50, 4 с → 100), пути относительные.</summary>
    private const string Reference =
        """
        {
          "OTIO_SCHEMA": "Timeline.1",
          "name": "Ролик",
          "metadata": { "ai2p": { "generator": "AI2P", "clips": 2, "fps": 25, "width": 1920, "height": 1080 } },
          "global_start_time": { "OTIO_SCHEMA": "RationalTime.1", "rate": 25, "value": 0 },
          "tracks": {
            "OTIO_SCHEMA": "Stack.1",
            "name": "Ролик",
            "metadata": {},
            "source_range": null,
            "effects": [],
            "markers": [],
            "enabled": true,
            "children": [
              {
                "OTIO_SCHEMA": "Track.1",
                "name": "V1",
                "kind": "Video",
                "metadata": {},
                "source_range": null,
                "effects": [],
                "markers": [],
                "enabled": true,
                "children": [
                  {
                    "OTIO_SCHEMA": "Clip.1",
                    "name": "sceneA.mp4",
                    "metadata": { "ai2p": { "scene": "3", "take": 2, "order": 10, "kind": "video", "caption": "Вася бежит" } },
                    "source_range": {
                      "OTIO_SCHEMA": "TimeRange.1",
                      "start_time": { "OTIO_SCHEMA": "RationalTime.1", "rate": 25, "value": 0 },
                      "duration": { "OTIO_SCHEMA": "RationalTime.1", "rate": 25, "value": 50 }
                    },
                    "effects": [],
                    "markers": [],
                    "enabled": true,
                    "media_reference": {
                      "OTIO_SCHEMA": "ExternalReference.1",
                      "name": "sceneA.mp4",
                      "metadata": {},
                      "available_range": null,
                      "available_image_bounds": null,
                      "target_url": "media/sceneA.mp4"
                    }
                  }
                ]
              },
              {
                "OTIO_SCHEMA": "Track.1",
                "name": "A1",
                "kind": "Audio",
                "metadata": {},
                "source_range": null,
                "effects": [],
                "markers": [],
                "enabled": true,
                "children": [
                  {
                    "OTIO_SCHEMA": "Clip.1",
                    "name": "tema.mp3",
                    "metadata": { "ai2p": { "scene": "3", "take": 0, "order": 0, "kind": "audio", "caption": "Тема \"финал\"" } },
                    "source_range": {
                      "OTIO_SCHEMA": "TimeRange.1",
                      "start_time": { "OTIO_SCHEMA": "RationalTime.1", "rate": 25, "value": 0 },
                      "duration": { "OTIO_SCHEMA": "RationalTime.1", "rate": 25, "value": 100 }
                    },
                    "effects": [],
                    "markers": [],
                    "enabled": true,
                    "media_reference": {
                      "OTIO_SCHEMA": "ExternalReference.1",
                      "name": "tema.mp3",
                      "metadata": {},
                      "available_range": null,
                      "available_image_bounds": null,
                      "target_url": "media/tema.mp3"
                    }
                  }
                ]
              }
            ]
          }
        }

        """;

    /// <summary>
    /// JSON ОСТАЁТСЯ ГОДНЫМ ПРИ ЛЮБОМ ЧИСЛЕ КЛИПОВ — вот где ловится ошибка разделителя:
    /// один клип (запятой быть НЕ должно), три клипа на одной дорожке (две запятых), пустая
    /// звуковая дорожка (<c>"children": []</c> без запятой внутри). Ресурс без длительности
    /// получает не ноль, а умолчание: клип нулевой длины — это молча пропавший кадр.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void The_Otio_Stays_Valid_Json_With_Any_Number_Of_Clips(int count)
    {
        var clips = Enumerable.Range(0, count).Select(i => new TimelineClip
        {
            Path = $"media/scene{i}.png", Kind = MediaMeta.Image, Name = $"scene{i}.png",
        }).ToList();

        var text = TimelineExport.Build(Resolve().Export!, clips, "Ролик");

        using var parsed = JsonDocument.Parse(text);
        var tracks = parsed.RootElement.GetProperty("tracks").GetProperty("children");
        Assert.Equal(2, tracks.GetArrayLength());
        Assert.Equal(count, tracks[0].GetProperty("children").GetArrayLength());
        Assert.Equal(0, tracks[1].GetProperty("children").GetArrayLength());
        Assert.Equal(TimelineExport.Frames(TimelineExport.FallbackSec, 25),
            tracks[0].GetProperty("children")[0].GetProperty("source_range")
                .GetProperty("duration").GetProperty("value").GetInt32());
    }

    /// <summary>
    /// РАЗДЕЛИТЕЛЬ НЕ ТРОГАЕТ XML-ШЛЮЗЫ. Поле добавлено в общий движок ради JSON-форматов, и
    /// у MLT оно обязано остаться пустым: запятая между <c>&lt;producer&gt;</c> сделала бы
    /// монтажный лист Shotcut негодным. Сторож стоит здесь, потому что ломается это ЧУЖОЙ
    /// правкой — правкой манифеста Resolve.
    ///
    /// <para>ПРОВЕРЯЕТСЯ НЕ «ЗАПЯТЫХ НЕТ ВОВСЕ», А «ЗАПЯТЫХ НЕ ПРИБАВЛЯЕТСЯ ОТ КЛИПОВ».
    /// Первая редакция искала запятую по всему документу и краснела на исправном продукте:
    /// запятая в MLT есть и была всегда — внутри XML-комментария шаблона Shotcut
    /// («правьте свой, а этот перезаписывается», <c>PluginSeed.ShotcutJson</c>), файлу она не
    /// вредит. Разделитель же проявляется ровно на СТЫКЕ кусков, поэтому сторож считает
    /// запятые при одном и при двух клипах (число обязано совпасть) и отдельно смотрит стык
    /// двух <c>&lt;producer&gt;</c>.</para>
    /// </summary>
    [Fact]
    public void The_Separator_Is_Empty_For_The_Xml_Gateway()
    {
        var shotcut = PluginManifest.Parse(PluginSeed.ShotcutJson)!;
        static TimelineClip Clip(string path) =>
            new() { Path = path, Kind = MediaMeta.Video, DurationSec = 1 };

        var one = TimelineExport.Build(shotcut.Export!, [Clip("a.mp4")], "Ролик");
        var two = TimelineExport.Build(shotcut.Export!, [Clip("a.mp4"), Clip("b.mp4")], "Ролик");

        Assert.Equal("", shotcut.Export!.Separator);
        Assert.Equal(one.Count(c => c == ','), two.Count(c => c == ','));
        Assert.DoesNotContain(",\n", two, StringComparison.Ordinal);
        Assert.DoesNotContain("</producer>,", two, StringComparison.Ordinal);
    }

    // ---------- 3. Пути ----------

    private int _stands;

    /// <summary>Стенд: проект с папкой, задача проекта и шлюз Resolve.</summary>
    private (GatewayToolset Tools, string ProjectId, string Folder) Stand()
    {
        var number = ++_stands;
        var folder = Path.Combine(_f.Dir, "resolve-project" + number);
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик Resolve " + number, folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        var tools = new GatewayToolset([new GatewayPlugin(Resolve())], _f.Objects, task, folder,
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
    /// КАЖДЫЙ <c>target_url</c> — ТОЛЬКО ОТНОСИТЕЛЬНЫЙ (правило ветки T-110-S0). Ресурс
    /// хранилища организации лежит вне папки проекта, поэтому копируется рядом с монтажным
    /// листом: ссылка на него иначе была бы либо абсолютной, либо с <c>..</c>, то есть
    /// неверной на любом другом компьютере кластера.
    /// </summary>
    [Fact]
    public void Every_Target_Url_In_The_Otio_Is_Relative()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        var stored = Path.Combine(_f.Files.DataDir, "projects", "PRJ-R", "media");
        Directory.CreateDirectory(stored);
        File.WriteAllText(Path.Combine(stored, "sceneB.mp4"), "кадры дубля");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        AddMedia(projectId, "снятое", "store:projects/PRJ-R/media/sceneB.mp4", 3);

        var answer = tools.ExecuteAsync("resolve_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        var file = Path.Combine(folder, "ai2p_scenes.otio");
        Assert.True(File.Exists(file), answer);
        var text = File.ReadAllText(file);
        using var parsed = JsonDocument.Parse(text);
        var urls = parsed.RootElement.GetProperty("tracks").GetProperty("children")
            .EnumerateArray()
            .SelectMany(t => t.GetProperty("children").EnumerateArray())
            .Select(c => c.GetProperty("media_reference").GetProperty("target_url").GetString()!)
            .ToList();

        Assert.Equal(["media/sceneA.mp4", "ai2p_media/sceneB.mp4"], urls);
        Assert.True(File.Exists(Path.Combine(folder, "ai2p_media", "sceneB.mp4")));
        Assert.DoesNotContain(folder, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", text, StringComparison.Ordinal);
    }

    /// <summary>Медиатека пуста — понятный отказ и НИКАКОГО файла: пустой таймлайн в Resolve
    /// выглядит поломкой шлюза.</summary>
    [Fact]
    public void An_Empty_Library_Writes_Nothing()
    {
        var (tools, _, folder) = Stand();

        var answer = tools.ExecuteAsync("resolve_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(File.Exists(Path.Combine(folder, "ai2p_scenes.otio")));
        Assert.Equal(Loc.T("prompt.gateway.5"), answer);
    }

    /// <summary>
    /// ОТКАЗ ПО ПУТИ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ. Ограничение у файлового шлюза
    /// работает БУКВАЛЬНО (мы пишем JSON и кода не исполняем), и файла при отказе не
    /// появляется. На Windows «/tmp/x» считается АБСОЛЮТНЫМ путём и уехал бы в C:\tmp.
    /// </summary>
    [Theory]
    [InlineData("../снаружи.otio")]
    [InlineData(@"C:\снаружи.otio")]
    [InlineData("/tmp/снаружи.otio")]
    public void Export_Refuses_A_Path_Outside_The_Project_Folder(string path)
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);

        var answer = tools.ExecuteAsync("resolve_timeline_write",
            Args(JsonSerializer.Serialize(new { file = path })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains(path, answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "ai2p_scenes.otio")));
        Assert.Empty(Directory.GetFiles(_f.Dir, "снаружи.otio", SearchOption.AllDirectories));
    }

    // ---------- 4. Софт: поставить нельзя, можно только показать ----------

    /// <summary>
    /// СОФТ НЕ УСТАНАВЛИВАЕТСЯ, А УКАЗЫВАЕТСЯ РУКАМИ, и это видно ПО ИСХОДУ ПОИСКА: пустой
    /// код пакета даёт <see cref="PluginSoftwareStatus.NeedsManualPath"/> («укажите, где он
    /// уже стоит»), а не <see cref="PluginSoftwareStatus.CanInstall"/> («Установить»). Отсюда
    /// же растёт внятный отказ: пока путь не указан, состояние плагина на ЭТОМ сервере —
    /// «софт ищется», и действия агенту не публикуются.
    /// </summary>
    [Fact]
    public void The_Software_Cannot_Be_Installed_Only_Pointed_At()
    {
        var software = Resolve().Software!;
        Assert.Equal("", software.Package);          // ставить нечем — и это НАМЕРЕННО
        Assert.False(software.Required);
        Assert.Equal("resolvePath", software.PathKey);
        Assert.Contains("Resolve.exe", software.System!.Commands);
        // версию Resolve по командной строке не отдаёт — проверяется НАЛИЧИЕ файла
        Assert.Equal("", software.System.VersionArgs);

        var missing = PluginSoftwareProbe.Find(software, manualPath: "", searchPath: "");

        Assert.Equal(PluginSoftwareStatus.NeedsManualPath, missing.Status);
        Assert.False(missing.Ready);
        Assert.Equal(PluginLifecycle.ProbingSoftware,
            PluginLifecycle.Shown(PluginStates.Initialized, missing.Status));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Initialized, missing.Status));
        // и человеку сказано, что делать: подсказка манифеста объясняет, почему кнопки нет
        Assert.Contains("Blackmagic", software.Hint.Text("ru"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ К ПРЕДЫДУЩЕЙ ПРОВЕРКЕ: показанный руками Resolve НАХОДИТСЯ — иначе
    /// проверка выше доказывала бы лишь то, что не находится ничего. Человек показывает и
    /// файл программы, и каталог, куда он её поставил («вот сюда я поставил Resolve»).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Manually_Pointed_Resolve_Is_Found(bool pointAtFolder)
    {
        var software = Resolve().Software!;
        var dir = Path.Combine(_f.Dir, "resolve-home" + (pointAtFolder ? "-dir" : "-file"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "Resolve.exe");
        File.WriteAllText(exe, "не настоящая программа, но файл есть");

        var found = PluginSoftwareProbe.Find(software, pointAtFolder ? dir : exe, searchPath: "");

        Assert.Equal(PluginSoftwareStatus.Found, found.Status);
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
        var path = _f.Files.Abs(PluginManifest.PathOf(PluginSeed.ResolveCode));
        Assert.True(File.Exists(path));
        var stamp = File.GetLastWriteTimeUtc(path);

        PluginSeed.Write(_f.Files); // идемпотентно: та же версия — файл не трогается

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(PluginSeed.ResolveCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.ResolveCode)!.Code);
    }
}
