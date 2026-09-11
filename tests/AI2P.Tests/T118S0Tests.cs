using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-118-S0 — ШЛЮЗ В BLENDER VSE: ПАРАМЕТРИЗОВАННЫЙ СКРИПТ PYTHON, а не правка
/// <c>.blend</c>.
///
/// <para>ГЛАВНЫЙ РИСК ЭТОГО ШЛЮЗА НЕ ТАКОЙ, КАК У ОСТАЛЬНЫХ ТРЁХ. Shotcut, Resolve и
/// OpenShot читают наш файл РАЗБОРЩИКОМ (XML, JSON) — испорченная подстановка там даёт
/// негодный файл, и только. У Blender наш файл ИСПОЛНЯЕТСЯ: подпись клипа с кавычкой,
/// уехавшая в скрипт как есть, — это не «слегка другой файл», а чужой код, который Blender
/// послушно выполнит, и ограничение каталогом его не остановит (Python внутри Blender
/// открывает любой файл через <c>open()</c>). Поэтому проверки здесь про две вещи:</para>
/// <list type="number">
/// <item>ЭКРАНИРОВАНИЕ ПОДСТАНОВОК — кавычка, перевод строки и обратная косая в имени файла
/// не рвут скрипт и не позволяют дописать в него код;</item>
/// <item>ПРОИЗВОЛЬНОГО СКРИПТА ОТ ИИ НЕТ — ни действием справочника («выполни этот
/// Python» не объявлено), ни в обход: рендер запускает ТОЛЬКО наш собранный файл
/// (<c>ownFileOnly</c>), и параметр «что запускать» агенту не объявляется вовсе.</item>
/// </list>
///
/// <para>Плюс общее для ветки: эталонный текст скрипта посимвольно, пути только
/// относительные, отказ по пути за пределами разрешённых каталогов (и контрольный опыт к
/// нему), пакет программы в справочнике.</para>
///
/// <para>Живая проверка (<see cref="Blender_Builds_The_Project_From_The_Generated_Script"/>)
/// идёт только при заданной <c>AI2P_BLENDER</c>: Blender в наборе нет, и краснеть у всех
/// из-за отсутствия чужой программы проверка не должна.</para>
/// </summary>
public sealed class T118S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Манифест шлюза дистрибутива — тот же, что ложится в каталог данных.</summary>
    private static PluginManifest Blender() =>
        PluginManifest.Parse(PluginSeed.BlenderJson)
        ?? throw new InvalidOperationException("манифест шлюза Blender негоден");

    // ---------- 1. Манифест ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ: два действия с ролями, экранирование именно
    /// <c>python</c> (у остальных шлюзов ветки — xml или json), пометка
    /// <c>ownFileOnly</c> у рендера, четыре записи опыта.
    /// </summary>
    [Fact]
    public void The_Blender_Manifest_Is_Read_Whole()
    {
        var manifest = Blender();

        Assert.Equal(PluginSeed.BlenderCode, manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);

        var export = Assert.Single(manifest.Actions.Where(a => a.Role == PluginAction.RoleExport));
        var render = Assert.Single(manifest.Actions.Where(a => a.Role == PluginAction.RoleRender));
        // имена инструментов СВОИ у шлюза: общий timeline_write на четыре шлюза сделал бы
        // правило безопасности одним на все четыре сразу
        Assert.Equal("blender_timeline_write", export.Tool);
        Assert.Equal("blender_render", render.Tool);
        Assert.StartsWith("AI2P.Plugins.Blender.", export.Code, StringComparison.Ordinal);
        Assert.StartsWith("AI2P.Plugins.Blender.", render.Code, StringComparison.Ordinal);
        Assert.True(render.NeedsSoftware);
        Assert.False(export.NeedsSoftware); // скрипт пишется и без установленного Blender

        foreach (var lang in Loc.Languages)
        {
            Assert.NotEqual("", manifest.Description.Text(lang));
            Assert.NotEqual("", export.Title.Text(lang));
            Assert.NotEqual("", export.Description.Text(lang));
            Assert.NotEqual("", render.Title.Text(lang));
            Assert.NotEqual("", render.Description.Text(lang));
        }

        Assert.NotNull(manifest.Software);
        Assert.Equal("blender", manifest.Software!.Package);
        Assert.False(manifest.Software.Required); // сборка скрипта идёт и без программы
        Assert.Equal("blenderPath", manifest.Software.PathKey);
        Assert.Contains("blender", manifest.Software.System!.Commands);
        // minVersion обязателен: у сборок 2.7x другой Python и другой VSE API
        Assert.Equal("3.0", manifest.Software.System.MinVersion);

        Assert.Equal(4, manifest.Experience.Count);
        Assert.All(manifest.Experience, e => Assert.Equal("video-edit", e.Skill));

        Assert.NotNull(manifest.Export);
        Assert.Equal("ai2p_timeline.py", manifest.Export!.File);
        Assert.Equal(TimelineFormat.EscapePython, manifest.Export.Escape);
        Assert.Equal(2, manifest.Export.Tracks.Count);
        Assert.NotNull(manifest.Render);
        Assert.Contains("--background", manifest.Render!.Args);
        Assert.Contains("--python", manifest.Render.Args);
        Assert.Contains("{file}", manifest.Render.Args);
        Assert.Equal(".blend", manifest.Render.OutExt);
        Assert.True(manifest.Render.OwnFileOnly);
        // документ шлюза объявлен на всех пяти языках
        Assert.All(Loc.Languages, lang => Assert.Equal("plugins/editor.blender.md",
            manifest.Doc[lang]));
    }

    /// <summary>
    /// ДЕЙСТВИЯ «ВЫПОЛНИТЬ ЭТОТ PYTHON» В МАНИФЕСТЕ НЕТ — и это главное требование задания.
    /// Роль у каждого действия известная (движок делает работу сам), а <c>convert</c>
    /// (именованные операции над произвольными файлами) шлюзу-скрипту не объявлен вовсе.
    /// </summary>
    [Fact]
    public void The_Manifest_Declares_No_Free_Script_Action()
    {
        var manifest = Blender();

        Assert.All(manifest.Actions, a => Assert.Contains(a.Role,
            new[] { PluginAction.RoleExport, PluginAction.RoleRender }));
        Assert.Null(manifest.Convert);
        // командная строка запуска целиком из манифеста, и подставляются в неё РОВНО два
        // пути — что исполнять и куда положить результат; ничего от агента больше не приходит
        var holes = manifest.Render!.Args
            .Where(a => a.Contains('{', StringComparison.Ordinal)).ToList();
        Assert.Equal(["{file}", "{out}"], holes);
    }

    // ---------- 2. Эталонный скрипт ----------

    private static string Build(params TimelineClip[] clips) =>
        TimelineExport.Build(Blender().Export!, clips, "Ролик");

    /// <summary>
    /// ЭТАЛОННЫЙ СКРИПТ ПОСИМВОЛЬНО. Проверка нарочно жёсткая: скрипт ИСПОЛНЯЕТСЯ, и «почти
    /// такой же» текст это либо отказ Python на середине сборки, либо молча пустой проект.
    /// Этот же вид скрипта отработан живым Blender без окна.
    /// </summary>
    [Fact]
    public void The_Script_Matches_The_Reference_File()
    {
        var text = Build(
            new TimelineClip
            {
                Path = "media/sceneA.mp4", Kind = MediaMeta.Video, Name = "sceneA.mp4",
                Caption = "Вася бежит", Scene = "3", Take = 2, Order = 10, DurationSec = 2,
            },
            new TimelineClip
            {
                Path = "media/tema.mp3", Kind = MediaMeta.Audio, Name = "tema.mp3",
                Caption = "Тема \"финал\"", Scene = "3", DurationSec = 4,
            });

        var dump = Environment.GetEnvironmentVariable("AI2P_T118_DUMP") ?? "";
        if (dump.Length > 0)
        {
            File.WriteAllText(dump, text);
        }
        // концы строк эталона приводятся к «\n»: файл проверок в репозитории может лежать и
        // с CRLF, а генератор всегда пишет «\n»
        Assert.Equal(Reference.Replace("\r\n", "\n"), text);
    }

    /// <summary>Эталон: два клипа на двух дорожках VSE, подпись с кавычками уехала
    /// экранированной.</summary>
    private const string Reference =
        """
        # -*- coding: utf-8 -*-
        # Собрано AI2P из медиатеки проекта по ШАБЛОНУ ПЛАГИНА. Это НАШ файл: он
        # перезаписывается целиком, правьте свой. Произвольного скрипта здесь быть не может:
        # подставляются только данные медиатеки, и все они экранированы как строки Python.
        # Запуск: blender --background --factory-startup --python ai2p_timeline.py -- <результат>
        # Результат с расширением .blend — сохранить проект, любой другой — посчитать ролик.
        import os
        import sys

        import bpy

        NAME = "Ролик"
        FPS = 25
        WIDTH = 1920
        HEIGHT = 1080
        OWN = "ai2p_timeline.blend"

        # Дорожка: [канал, имя, клипы]. Клип: [путь, вид, подпись, кадров, сцена, дубль].
        # Пути ТОЛЬКО относительные — они считаются от каталога этого файла.
        TRACKS = [
            [1, "V1", [
                ["media/sceneA.mp4", "video", "Вася бежит", 50, "3", 2],
            ]],
            [2, "A1", [
                ["media/tema.mp3", "audio", "Тема \"финал\"", 100, "3", 0],
            ]],
        ]

        BASE = os.path.dirname(os.path.abspath(__file__))


        def target():
            if "--" in sys.argv:
                rest = sys.argv[sys.argv.index("--") + 1:]
                if rest:
                    return rest[0]
            return os.path.join(BASE, OWN)


        def strips(editor):
            # 4.4 переименовала sequences в strips; понимаем оба имени
            found = getattr(editor, "strips", None)
            return editor.sequences if found is None else found


        def add(seq, kind, name, path, channel, start, frames):
            if kind == "audio":
                return seq.new_sound(name=name, filepath=path, channel=channel, frame_start=start)
            if kind == "image":
                strip = seq.new_image(name=name, filepath=path, channel=channel, frame_start=start)
                strip.frame_final_duration = frames
                return strip
            return seq.new_movie(name=name, filepath=path, channel=channel, frame_start=start)


        def build():
            scene = bpy.context.scene
            scene.render.fps = int(round(FPS))
            scene.render.fps_base = 1.0
            scene.render.resolution_x = WIDTH
            scene.render.resolution_y = HEIGHT
            scene.render.resolution_percentage = 100
            if scene.sequence_editor is None:
                scene.sequence_editor_create()
            seq = strips(scene.sequence_editor)
            for old in list(seq):
                seq.remove(old)
            last = 1
            for channel, track, clips in TRACKS:
                start = 1
                for path, kind, caption, frames, shot, take in clips:
                    full = os.path.normpath(os.path.join(BASE, path))
                    if not os.path.exists(full):
                        print("AI2P: нет файла", path)
                        continue
                    label = caption if caption else os.path.basename(path)
                    strip = add(seq, kind, label, full, channel, start, frames)
                    start = int(strip.frame_final_end)
                    last = max(last, start)
            scene.frame_start = 1
            scene.frame_end = max(1, last - 1)
            print("AI2P: дорожек", len(TRACKS), "клипов", len(list(seq)))
            return scene


        def save(scene, out):
            if out.lower().endswith(".blend"):
                bpy.ops.wm.save_as_mainfile(filepath=out, relative_remap=True)
                print("AI2P: проект сохранён", out)
                return
            scene.render.filepath = out
            scene.render.use_file_extension = False
            scene.render.image_settings.file_format = "FFMPEG"
            scene.render.ffmpeg.format = "MPEG4"
            scene.render.ffmpeg.codec = "H264"
            scene.render.ffmpeg.audio_codec = "AAC"
            bpy.ops.render.render(animation=True)
            print("AI2P: ролик посчитан", out)


        save(build(), target())

        """;

    // ---------- 3. Экранирование подстановок — главный риск шаблона ----------

    /// <summary>
    /// ПОДСТАНОВКА НЕ ВЫРЫВАЕТСЯ ИЗ СТРОКОВОГО ЛИТЕРАЛА. Имя файла с кавычкой, переводом
    /// строки и обратной косой — это ровно та подпись, которой ломают шаблонный генератор:
    /// кавычка закрывает литерал, перевод строки начинает НОВУЮ строку кода, а обратная
    /// косая перед кавычкой съедает экранирование следующей. Проверяется, что после
    /// подстановки клип остался ОДНОЙ строкой и все три знака ушли двухзначными
    /// последовательностями.
    /// </summary>
    [Fact]
    public void A_Substitution_Cannot_Break_Out_Of_The_Script()
    {
        // попытка дописать код: закрыть литерал, закрыть списки и начать свою строку. Строка
        // выбрана так, чтобы удавшийся взлом ОСТАВИЛ СЛЕД на диске — этим же скриптом живая
        // проверка убеждается, что настоящий Blender следа не оставил (test/t118s0/live.py)
        const string evil =
            "z\", \"video\", \"x\", 1, \"s\", 0]]]\nopen(\"ВЗЛОМ.txt\", \"w\").close()\nT = [[\\";
        var text = Build(new TimelineClip
        {
            Path = "media/" + evil + ".mp4", Kind = MediaMeta.Video, Name = evil,
            Caption = evil, Scene = evil, DurationSec = 2,
        });
        var dump = Environment.GetEnvironmentVariable("AI2P_T118_DUMP_EVIL") ?? "";
        if (dump.Length > 0)
        {
            File.WriteAllText(dump, text);
        }

        var line = Assert.Single(text.Split('\n')
            .Where(l => l.TrimStart().StartsWith("[\"media/", StringComparison.Ordinal)));
        // клип остался ОДНОЙ строкой — перевод строки уехал двумя знаками, а не разрывом
        Assert.EndsWith("],", line, StringComparison.Ordinal);
        Assert.Contains("\\n", line, StringComparison.Ordinal);
        Assert.Contains("\\\"", line, StringComparison.Ordinal);
        Assert.Contains("\\\\", line, StringComparison.Ordinal);
        // ни одной НОВОЙ строки кода в скрипте не появилось: open(…) стоит внутри литерала
        Assert.DoesNotContain("\nopen(\"ВЗЛОМ.txt\"", text, StringComparison.Ordinal);
        // строк в скрипте ровно столько же, сколько при безобидной подписи
        var plain = Build(new TimelineClip
        {
            Path = "media/ok.mp4", Kind = MediaMeta.Video, Name = "ok", Caption = "ok",
            DurationSec = 2,
        });
        Assert.Equal(plain.Split('\n').Length, text.Split('\n').Length);
    }

    /// <summary>Экранирование по виду формата — отдельно от генератора: у Python и JSON оно
    /// одно (строковый литерал в кавычках), у XML другое, и перепутать их нельзя.</summary>
    [Theory]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("кириллица", "кириллица")]
    public void Python_Escaping_Keeps_The_Value_Inside_The_Literal(string value, string escaped) =>
        Assert.Equal(escaped, TimelineExport.Escape(value, TimelineFormat.EscapePython));

    // ---------- 4. Стенд и пути ----------

    private int _stands;

    /// <summary>Стенд: проект с папкой, задача проекта и шлюз с найденной (или нет)
    /// программой.</summary>
    private (GatewayToolset Tools, string ProjectId, string Folder) Stand(string softwarePath = "")
    {
        var number = ++_stands;
        var folder = Path.Combine(_f.Dir, "blender-folder" + number);
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик Blender " + number, folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        var tools = new GatewayToolset([new GatewayPlugin(Blender(), softwarePath)],
            _f.Objects, task, folder, _f.Files.DataDir, project.Name);
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
    /// ПУТИ В СКРИПТЕ — ТОЛЬКО ОТНОСИТЕЛЬНЫЕ. Скрипт считает их от своего каталога
    /// (<c>__file__</c>), поэтому буквы диска и «..» в нём быть не должно: проект,
    /// сохранённый Blender по абсолютному пути, не откроется ни на одном другом компьютере
    /// кластера. Ресурс хранилища организации лежит вне папки проекта — он копируется рядом.
    /// </summary>
    [Fact]
    public void Every_Path_In_The_Script_Is_Relative()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        var stored = Path.Combine(_f.Files.DataDir, "projects", "PRJ-1", "media");
        Directory.CreateDirectory(stored);
        File.WriteAllText(Path.Combine(stored, "sceneB.mp4"), "кадры дубля");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        AddMedia(projectId, "снятое", "store:projects/PRJ-1/media/sceneB.mp4", 3);

        var answer = tools.ExecuteAsync("blender_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        var text = File.ReadAllText(Path.Combine(folder, "ai2p_timeline.py"));
        Assert.Contains("ai2p_timeline.py", answer, StringComparison.Ordinal);
        Assert.Contains("\"media/sceneA.mp4\"", text, StringComparison.Ordinal);
        Assert.Contains("\"ai2p_media/sceneB.mp4\"", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "ai2p_media", "sceneB.mp4")));
        Assert.DoesNotContain("[\"..", text, StringComparison.Ordinal);
        Assert.DoesNotContain(folder, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
    }

    /// <summary>Медиатека пуста — понятный отказ и НИКАКОГО скрипта: пустой скрипт, отданный
    /// Blender, дал бы пустой проект и выглядел бы поломкой шлюза.</summary>
    [Fact]
    public void An_Empty_Library_Writes_Nothing()
    {
        var (tools, _, folder) = Stand();

        var answer = tools.ExecuteAsync("blender_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(File.Exists(Path.Combine(folder, "ai2p_timeline.py")));
        Assert.Equal(Loc.T("prompt.gateway.5"), answer);
    }

    /// <summary>
    /// ОТКАЗ ПО ПУТИ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ. Здесь он важнее, чем у остальных
    /// шлюзов: файл, который мы пишем, потом ИСПОЛНЯЕТСЯ, и записать его куда угодно на
    /// диске означало бы оставить исполняемый скрипт вне зоны видимости человека.
    /// </summary>
    [Theory]
    [InlineData("../снаружи.py")]
    [InlineData(@"C:\снаружи.py")]
    [InlineData("/tmp/снаружи.py")]
    public void Export_Refuses_A_Path_Outside_The_Project_Folder(string path)
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);

        var answer = tools.ExecuteAsync("blender_timeline_write",
            Args(JsonSerializer.Serialize(new { file = path })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains(path, answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "ai2p_timeline.py")));
        Assert.Empty(Directory.GetFiles(_f.Dir, "снаружи.py", SearchOption.AllDirectories));
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ К ПРЕДЫДУЩЕЙ ПРОВЕРКЕ: во ВНЕШНИЙ каталог, открытый правилами
    /// безопасности задачи, писать РАЗРЕШЕНО — иначе проверка выше доказывала бы лишь то,
    /// что запрещено всё подряд.
    /// </summary>
    [Fact]
    public void Export_Writes_Into_An_External_Dir_Opened_By_The_Rules()
    {
        var folder = Path.Combine(_f.Dir, "blender-folder-ext");
        Directory.CreateDirectory(folder);
        var outside = Path.Combine(_f.Dir, "обмен-blender");
        Directory.CreateDirectory(outside);
        var project = _f.Projects.Create("Ролик снаружи Blender", folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(project.Id, "своё", "media/sceneA.mp4", 2);
        var tools = new GatewayToolset([new GatewayPlugin(Blender())], _f.Objects, task, folder,
            _f.Files.DataDir, project.Name, null, [outside]);

        var target = Path.Combine(outside, "ai2p_timeline.py");
        var answer = tools.ExecuteAsync("blender_timeline_write",
            Args(JsonSerializer.Serialize(new { file = target })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(File.Exists(target), answer);
        Assert.DoesNotContain(":\\", File.ReadAllText(target), StringComparison.Ordinal);
    }

    // ---------- 5. Произвольного скрипта от ИИ нет ----------

    /// <summary>
    /// АГЕНТ НЕ ВЫБИРАЕТ, ЧТО ИСПОЛНЯТЬ. Это и есть требование безопасности задания в его
    /// исполнимом виде: параметра <c>file</c> у рендера НЕТ в объявлении инструмента, а
    /// присланный «в обход» он не читается — запускается наш собранный
    /// <c>ai2p_timeline.py</c>. Без этого агент написал бы свой Python инструментом записи
    /// файлов и запустил бы его нашими руками.
    /// </summary>
    [Fact]
    public void The_Agent_Cannot_Choose_Which_Script_To_Run()
    {
        var (tools, projectId, folder) = Stand(softwarePath: Path.Combine(_f.Dir, "blender.exe"));
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        // свой скрипт агента, написанный инструментом записи файлов, лежит в папке проекта
        File.WriteAllText(Path.Combine(folder, "мой.py"), "import os\nos.system('calc')\n");

        var schema = Assert.Single(tools.Specs.Where(s => s.Name == "blender_render"))
            .ParametersJson;
        var answer = tools.ExecuteAsync("blender_render",
            Args("""{"file": "мой.py"}"""), CancellationToken.None).GetAwaiter().GetResult();

        // «что запускать» агенту не объявлено вовсе
        Assert.DoesNotContain("\"file\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"out\"", schema, StringComparison.Ordinal);
        // а присланный «в обход» — не прочитан: отказ про НАШ файл, которого ещё нет
        Assert.Contains("ai2p_timeline.py", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("мой.py", answer, StringComparison.Ordinal);
    }

    /// <summary>Без найденной программы действие рендера агенту не публикуется вовсе, а
    /// сборка скрипта работает: она Blender не требует.</summary>
    [Fact]
    public void The_Render_Tool_Is_Not_Published_Without_The_Program()
    {
        var (without, _, _) = Stand();
        var (with, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "blender.exe"));

        Assert.Contains("blender_timeline_write", without.Specs.Select(s => s.Name));
        Assert.DoesNotContain("blender_render", without.Specs.Select(s => s.Name));
        Assert.Contains("blender_render", with.Specs.Select(s => s.Name));
        Assert.False(without.Handles("blender_render"));
    }

    // ---------- 6. Пакет программы и файл манифеста ----------

    /// <summary>
    /// ПАКЕТ ПОРТАТИВНОГО BLENDER ЕСТЬ В СПРАВОЧНИКЕ, и ссылка ведёт на download.blender.org.
    /// Она снята HEAD-запросом (03.09.2026, 404 851 964 байта), а не выдумана: выдуманный
    /// адрес даёт кнопку «Установить», которая падает без объяснения. Блок <c>system</c>
    /// обязателен — у того, кто делает 3D, Blender стоит и так.
    /// </summary>
    [Fact]
    public void The_Portable_Blender_Package_Is_In_The_Catalog()
    {
        _f.Models.Seed();
        var catalog = ModelPackageCatalog.Parse(
            File.ReadAllText(_f.Files.Abs(AiModelService.PackagesPath)));

        var package = catalog.Find("blender");
        Assert.NotNull(package);
        Assert.Equal("blender.exe", package!.Check);
        var file = Assert.Single(package.Files);
        Assert.StartsWith("https://download.blender.org/release/", file.Url,
            StringComparison.Ordinal);
        Assert.EndsWith(".zip", file.Url, StringComparison.Ordinal);
        Assert.Equal("zip", file.Unpack);
        Assert.NotNull(package.System);
        Assert.Contains("blender", package.System!.Commands);
        Assert.Equal("3.0", package.System.MinVersion);
        // код пакета — тот же, что назван в манифесте шлюза, иначе кнопка «Установить»
        // ставила бы не то
        Assert.Equal(package.Id, Blender().Software!.Package);
    }

    /// <summary>Манифест шлюза ложится в каталог данных файлом дистрибутива.</summary>
    [Fact]
    public void The_Manifest_Is_Written_To_The_Data_Dir()
    {
        PluginSeed.Write(_f.Files);

        Assert.True(File.Exists(_f.Files.Abs(PluginManifest.PathOf(PluginSeed.BlenderCode))));
        Assert.Equal(PluginSeed.BlenderCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.BlenderCode)!.Code);
    }

    // ---------- 7. Живая проверка ----------

    /// <summary>
    /// ЖИВОЙ BLENDER БЕЗ ОКНА отрабатывает СГЕНЕРИРОВАННЫЙ НАМИ скрипт и даёт проект с
    /// дорожками. Идёт только при заданной <c>AI2P_BLENDER</c> (полный путь к blender):
    /// программы в наборе нет, и краснеть у всех из-за её отсутствия проверка не должна.
    /// Медиа делаются здесь же — настоящий PNG и настоящий WAV: на выдуманных байтах
    /// <c>new_movie</c> отказывает, и проверка доказывала бы не то.
    /// </summary>
    [Fact]
    public void Blender_Builds_The_Project_From_The_Generated_Script()
    {
        var blender = Environment.GetEnvironmentVariable("AI2P_BLENDER") ?? "";
        if (blender.Length == 0 || !File.Exists(blender))
        {
            return;
        }
        var (tools, projectId, folder) = Stand(blender);
        var media = Path.Combine(folder, "media");
        Directory.CreateDirectory(media);
        File.WriteAllBytes(Path.Combine(media, "кадр1.png"), Png());
        File.WriteAllBytes(Path.Combine(media, "кадр2.png"), Png());
        File.WriteAllBytes(Path.Combine(media, "тишина.wav"), Wav());
        AddMedia(projectId, "кадр1", "media/кадр1.png", 2, MediaMeta.Image);
        AddMedia(projectId, "кадр2", "media/кадр2.png", 2, MediaMeta.Image);
        AddMedia(projectId, "тишина", "media/тишина.wav", 1, MediaMeta.Audio);

        var built = tools.ExecuteAsync("blender_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.Contains("ai2p_timeline.py", built, StringComparison.Ordinal);

        var made = tools.ExecuteAsync("blender_render", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        var project = Path.Combine(folder, "ai2p_timeline.blend");
        Assert.True(File.Exists(project), made);
        Assert.True(new FileInfo(project).Length > 0, made);
        var keep = Environment.GetEnvironmentVariable("AI2P_BLENDER_OUT") ?? "";
        if (keep.Length > 0)
        {
            Directory.CreateDirectory(keep);
            foreach (var file in Directory.GetFiles(folder))
            {
                File.Copy(file, Path.Combine(keep, Path.GetFileName(file)), true);
            }
        }
    }

    /// <summary>Настоящий PNG 1×1 (иначе Blender откажется его открыть).</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Настоящий WAV: 8 кГц, моно, 16 бит, полсекунды тишины.</summary>
    private static byte[] Wav()
    {
        const int rate = 8000;
        const int samples = rate / 2;
        var data = new byte[samples * 2];
        using var stream = new MemoryStream();
        using var write = new BinaryWriter(stream);
        write.Write("RIFF"u8.ToArray());
        write.Write(36 + data.Length);
        write.Write("WAVE"u8.ToArray());
        write.Write("fmt "u8.ToArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)1);
        write.Write(rate);
        write.Write(rate * 2);
        write.Write((short)2);
        write.Write((short)16);
        write.Write("data"u8.ToArray());
        write.Write(data.Length);
        write.Write(data);
        write.Flush();
        return stream.ToArray();
    }
}
