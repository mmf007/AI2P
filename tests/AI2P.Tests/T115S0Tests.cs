using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-115-S0 — ШЛЮЗ В SHOTCUT / KDENLIVE (MLT XML), эталонный шлюз ветки T-110-S0.
///
/// Шесть вещей, ради которых проверки написаны:
/// <list type="number">
/// <item>МАНИФЕСТ дистрибутива читается целиком: два действия с ролями, блоки
/// <c>export</c> и <c>render</c>, три записи опыта с навыком <c>video-edit</c>, поиск
/// программы по <c>melt</c>/<c>qmelt</c>;</item>
/// <item>ЭТАЛОННЫЙ <c>.mlt</c> ПОСИМВОЛЬНО — генератор параметрический, и «слегка другой»
/// файл MLT это либо пустой проект в редакторе, либо отказ разборщика;</item>
/// <item>ПУТИ ТОЛЬКО ОТНОСИТЕЛЬНЫЕ: ресурс хранилища организации копируется рядом с
/// монтажным листом, а запись с путём, который наружу не привести, пропускается;</item>
/// <item>ОТКАЗ ПО ПУТИ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ — и по пути записываемого файла,
/// и по путям, вписываемым ВНУТРЬ проекта;</item>
/// <item>РЕНДЕР не публикуется и отказывает там, где программы нет;</item>
/// <item>ПАКЕТ портативного Shotcut есть в справочнике, ссылка ведёт на релиз
/// mltframework/shotcut (снята запросом, а не выдумана).</item>
/// </list>
///
/// <para>Живая проверка рендера (<see cref="Melt_Renders_The_Exported_Timeline"/>) идёт
/// только при заданной переменной окружения <c>AI2P_MELT</c>: melt в наборе нет, и краснеть
/// у всех из-за отсутствия чужой программы проверка не должна.</para>
/// </summary>
public sealed class T115S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Манифест шлюза дистрибутива — тот же, что ложится в каталог данных.</summary>
    private static PluginManifest Shotcut() =>
        PluginManifest.Parse(PluginSeed.ShotcutJson)
        ?? throw new InvalidOperationException("манифест шлюза Shotcut негоден");

    // ---------- 1. Манифест ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ. Каждое поле здесь несёт службу: без <c>code</c> действия
    /// правила безопасности инструмент не закрывают вовсе (2d3af8da), без <c>role</c> движок
    /// шлюзов не знает, что делать, без блока <c>export</c> собирать нечем.
    /// </summary>
    [Fact]
    public void The_Shotcut_Manifest_Is_Read_Whole()
    {
        var manifest = Shotcut();

        Assert.Equal(PluginSeed.ShotcutCode, manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);

        // два действия: сборка монтажного листа и рендер; имена инструментов СВОИ у шлюза —
        // общий timeline_write на четыре шлюза сделал бы правило безопасности одним на все
        var export = Assert.Single(manifest.Actions.Where(a => a.Role == PluginAction.RoleExport));
        var render = Assert.Single(manifest.Actions.Where(a => a.Role == PluginAction.RoleRender));
        Assert.Equal("shotcut_timeline_write", export.Tool);
        Assert.Equal("shotcut_render", render.Tool);
        Assert.StartsWith("AI2P.Plugins.Shotcut.", export.Code, StringComparison.Ordinal);
        Assert.StartsWith("AI2P.Plugins.Shotcut.", render.Code, StringComparison.Ordinal);
        Assert.True(render.NeedsSoftware);
        Assert.False(export.NeedsSoftware); // файл проекта собирается и без melt

        // тексты — на всех пяти языках: словари i18n плагину не правят, текст приходит файлом
        foreach (var lang in Loc.Languages)
        {
            Assert.NotEqual("", manifest.Description.Text(lang));
            Assert.NotEqual("", export.Title.Text(lang));
            Assert.NotEqual("", render.Title.Text(lang));
        }

        // софт: ищется в PATH под ОБОИМИ именами (в поставке Shotcut для Windows это melt.exe,
        // на других сборках встречается qmelt), версия проверяется обязательно
        Assert.NotNull(manifest.Software);
        Assert.Equal("shotcut", manifest.Software!.Package);
        Assert.False(manifest.Software.Required); // сборка .mlt идёт и без программы
        Assert.Equal("meltPath", manifest.Software.PathKey);
        Assert.Contains("melt", manifest.Software.System!.Commands);
        Assert.Contains("qmelt", manifest.Software.System.Commands);
        Assert.Equal("7.0", manifest.Software.System.MinVersion);

        // опыт: три записи, все с навыком video-edit (запись без навыка конкурирует за место
        // в промпте у КАЖДОЙ задачи организации — a8a79272)
        Assert.Equal(3, manifest.Experience.Count);
        Assert.All(manifest.Experience, e => Assert.Equal("video-edit", e.Skill));

        // формат и рендер
        Assert.NotNull(manifest.Export);
        Assert.Equal("ai2p_library.mlt", manifest.Export!.File);
        Assert.Equal(TimelineFormat.EscapeXml, manifest.Export.Escape);
        Assert.Equal(2, manifest.Export.Tracks.Count);
        Assert.NotNull(manifest.Render);
        Assert.Contains("avformat:{out}", manifest.Render!.Args);
        Assert.Equal("offscreen", manifest.Render.Env["QT_QPA_PLATFORM"]);
        Assert.Equal("linux", manifest.Render.EnvOs); // на Windows эта переменная не нужна
    }

    /// <summary>У сервера MCP шлюзовых блоков быть не может: инструменты он называет сам,
    /// и наш экспортёр к ним отношения не имеет.</summary>
    [Fact]
    public void An_Mcp_Manifest_Carries_No_Export_Or_Render()
    {
        var manifest = PluginManifest.Parse("""
            {
              "code": "mcp.some", "kind": "mcp",
              "connection": { "transport": "stdio", "command": "node" },
              "export": { "file": "x.json", "document": "{clips}" },
              "render": { "args": ["{file}"] }
            }
            """);

        Assert.NotNull(manifest);
        Assert.Null(manifest!.Export);
        Assert.Null(manifest.Render);
    }

    // ---------- 2. Эталонный .mlt ----------

    /// <summary>
    /// ЭТАЛОННЫЙ МОНТАЖНЫЙ ЛИСТ ПОСИМВОЛЬНО. Проверка нарочно жёсткая: MLT читает разборщик
    /// XML, и «почти такой же» файл открывается пустым проектом молча. Этот же вид файла
    /// отрендерен живым melt 7.41.0 (портативный Shotcut 26.8.1) без окна.
    /// </summary>
    [Fact]
    public void The_Timeline_Matches_The_Reference_File()
    {
        var format = Shotcut().Export!;
        var text = TimelineExport.Build(format,
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

        // концы строк эталона приводятся к «\n»: файл проверок в репозитории может лежать
        // и с CRLF, а генератор всегда пишет «\n» — сравнивать надо содержимое, а не то,
        // чем оболочка сохранила исходник
        Assert.Equal(Reference.Replace("\r\n", "\n"), text);
        // подпись с кавычками уехала экранированной — иначе XML негоден
        Assert.Contains("&quot;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<property name='shotcut:caption'>Тема \"", text,
            StringComparison.Ordinal);
    }

    /// <summary>Эталон: два клипа, две дорожки, длина — по самой длинной дорожке (звук 4 с).</summary>
    private const string Reference =
        """
        <?xml version='1.0' encoding='utf-8'?>
        <!-- Собрано AI2P из медиатеки проекта. Это НАШ файл: правьте свой, а этот перезаписывается. -->
        <mlt LC_NUMERIC='C' version='7.0.0' title='Ролик' producer='main_bin'>
          <profile description='ai2p' width='1920' height='1080' progressive='1' sample_aspect_num='1' sample_aspect_den='1' display_aspect_num='16' display_aspect_den='9' frame_rate_num='25' frame_rate_den='1' colorspace='709'/>
          <producer id='producer0' in='00:00:00.000' out='00:00:01.960'>
            <property name='length'>00:00:02.000</property>
            <property name='mlt_service'>avformat</property>
            <property name='resource'>media/sceneA.mp4</property>
            <property name='shotcut:caption'>Вася бежит</property>
            <property name='ai2p:scene'>3</property>
            <property name='ai2p:take'>2</property>
          </producer>
          <producer id='producer1' in='00:00:00.000' out='00:00:03.960'>
            <property name='length'>00:00:04.000</property>
            <property name='mlt_service'>avformat</property>
            <property name='resource'>media/tema.mp3</property>
            <property name='shotcut:caption'>Тема &quot;финал&quot;</property>
            <property name='ai2p:scene'>3</property>
            <property name='ai2p:take'>0</property>
          </producer>
          <playlist id='main_bin'>
            <property name='xml_retain'>1</property>
            <entry producer='producer0' in='00:00:00.000' out='00:00:01.960'/>
            <entry producer='producer1' in='00:00:00.000' out='00:00:03.960'/>
          </playlist>
          <producer id='black' in='00:00:00.000' out='00:00:03.960'>
            <property name='length'>00:00:04.000</property>
            <property name='mlt_service'>color</property>
            <property name='resource'>black</property>
            <property name='aspect_ratio'>1</property>
          </producer>
          <playlist id='background'>
            <entry producer='black' in='00:00:00.000' out='00:00:03.960'/>
          </playlist>
          <playlist id='playlist0'>
            <property name='shotcut:name'>V1</property>
            <entry producer='producer0' in='00:00:00.000' out='00:00:01.960'/>
          </playlist>
          <playlist id='playlist1'>
            <property name='shotcut:name'>A1</property>
            <entry producer='producer1' in='00:00:00.000' out='00:00:03.960'/>
          </playlist>
          <tractor id='tractor0' title='Ролик' in='00:00:00.000' out='00:00:03.960'>
            <property name='shotcut'>1</property>
            <property name='ai2p'>1</property>
            <track producer='background'/>
            <track producer='playlist0' hide=''/>
            <track producer='playlist1' hide='video'/>
          </tractor>
        </mlt>

        """;

    /// <summary>Ресурс без длительности (картинка, титр) получает НЕ ноль: клип нулевой
    /// длины — это молча пропавший кадр.</summary>
    [Fact]
    public void A_Clip_Without_Duration_Gets_A_Fallback_Length()
    {
        var text = TimelineExport.Build(Shotcut().Export!,
            [new TimelineClip { Path = "media/title.png", Kind = MediaMeta.Image }], "Ролик");

        Assert.Contains("<property name='mlt_service'>qimage</property>", text,
            StringComparison.Ordinal);
        Assert.Contains($"<property name='length'>{TimelineExport.Timecode(125, 25)}</property>",
            text, StringComparison.Ordinal);
    }

    // ---------- 3. Пути ----------

    private int _stands;

    /// <summary>Стенд: проект с папкой, задача проекта и шлюз с найденной (или нет) программой.
    /// Название проекта у каждого стенда своё — они уникальны в организации.</summary>
    private (GatewayToolset Tools, string ProjectId, string Folder) Stand(string softwarePath = "")
    {
        var number = ++_stands;
        var folder = Path.Combine(_f.Dir, "project-folder" + number);
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик " + number, folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        var tools = new GatewayToolset([new GatewayPlugin(Shotcut(), softwarePath)],
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
    /// ПУТИ В СОБРАННОМ ФАЙЛЕ — ТОЛЬКО ОТНОСИТЕЛЬНЫЕ, и это главное правило ветки: абсолютный
    /// <c>resource</c> убивает переносимость по кластеру. Ресурс ХРАНИЛИЩА организации лежит
    /// вне папки проекта, поэтому копируется рядом с монтажным листом — ссылка на него иначе
    /// была бы либо абсолютной, либо с <c>..</c>.
    /// </summary>
    [Fact]
    public void Every_Path_In_The_Timeline_Is_Relative()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        var stored = Path.Combine(_f.Files.DataDir, "projects", "PRJ-1", "media");
        Directory.CreateDirectory(stored);
        File.WriteAllText(Path.Combine(stored, "sceneB.mp4"), "кадры дубля");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        AddMedia(projectId, "снятое", "store:projects/PRJ-1/media/sceneB.mp4", 3);

        var answer = tools.ExecuteAsync("shotcut_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        var text = File.ReadAllText(Path.Combine(folder, "ai2p_library.mlt"));
        Assert.Contains("ai2p_library.mlt", answer, StringComparison.Ordinal);
        Assert.Contains("<property name='resource'>media/sceneA.mp4</property>", text,
            StringComparison.Ordinal);
        // ролик из хранилища лёг рядом и адресуется относительно монтажного листа
        Assert.Contains("<property name='resource'>ai2p_media/sceneB.mp4</property>", text,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "ai2p_media", "sceneB.mp4")));
        Assert.DoesNotContain("resource'>..", text, StringComparison.Ordinal);
        Assert.DoesNotContain(folder, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Медиатека пуста — понятный отказ и НИКАКОГО файла: пустой монтажный лист
    /// в редакторе выглядит поломкой шлюза.</summary>
    [Fact]
    public void An_Empty_Library_Writes_Nothing()
    {
        var (tools, _, folder) = Stand();

        var answer = tools.ExecuteAsync("shotcut_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(File.Exists(Path.Combine(folder, "ai2p_library.mlt")));
        Assert.Equal(Loc.T("prompt.gateway.5"), answer);
    }

    // ---------- 4. Ограничение каталогом ----------

    /// <summary>
    /// ОТКАЗ ПО ПУТИ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ. Здесь ограничение работает
    /// БУКВАЛЬНО (мы пишем XML и кода не исполняем), поэтому проверяются оба рода путей:
    /// абсолютный и уводящий за край папки проекта. Файла при отказе не появляется.
    /// </summary>
    [Theory]
    [InlineData("../снаружи.mlt")]
    [InlineData(@"C:\снаружи.mlt")]
    [InlineData("/tmp/снаружи.mlt")]
    public void Export_Refuses_A_Path_Outside_The_Project_Folder(string path)
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);

        var answer = tools.ExecuteAsync("shotcut_timeline_write",
            Args(JsonSerializer.Serialize(new { file = path })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains(path, answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "ai2p_library.mlt")));
        Assert.Empty(Directory.GetFiles(_f.Dir, "снаружи.mlt", SearchOption.AllDirectories));
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ К ПРЕДЫДУЩЕЙ ПРОВЕРКЕ: ВНЕШНИЙ КАТАЛОГ, открытый правилами
    /// безопасности задачи, писать РАЗРЕШЕНО — иначе проверка выше доказывала бы лишь то, что
    /// запрещено всё подряд. Без этой половины правило «каталогом проекта, а если в правилах
    /// указаны внешние каталоги — то и в них» (T-110-S0 §1.3.7) выполнено наполовину.
    /// </summary>
    [Fact]
    public void Export_Writes_Into_An_External_Dir_Opened_By_The_Rules()
    {
        var folder = Path.Combine(_f.Dir, "project-folder-ext");
        Directory.CreateDirectory(folder);
        var outside = Path.Combine(_f.Dir, "обмен");
        Directory.CreateDirectory(outside);
        var project = _f.Projects.Create("Ролик снаружи", folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(project.Id, "своё", "media/sceneA.mp4", 2);
        var tools = new GatewayToolset([new GatewayPlugin(Shotcut())], _f.Objects, task, folder,
            _f.Files.DataDir, project.Name, null, [outside]);

        var target = Path.Combine(outside, "ai2p_library.mlt");
        var answer = tools.ExecuteAsync("shotcut_timeline_write",
            Args(JsonSerializer.Serialize(new { file = target })),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(File.Exists(target), answer);
        // ролик остался в папке проекта, поэтому рядом с листом появилась его копия —
        // абсолютный путь в resource запрещён правилом ветки в любом случае
        var text = File.ReadAllText(target);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// ПРАВИЛО БЕЗОПАСНОСТИ ЗАДАЧИ ЗАКРЫВАЕТ ПУТЬ, лежащий внутри папки проекта: у шлюза
    /// проверка та же, что у файловых инструментов агента, — иначе «запретить агенту писать
    /// в подкаталог» работало бы для write_file и не работало для экспорта.
    /// </summary>
    [Fact]
    public void Export_Obeys_The_Security_Rules_Of_The_Task()
    {
        var folder = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик", folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Смонтировать", ProjectId = project.Id },
            "", "", null);
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(project.Id, "своё", "media/sceneA.mp4", 2);
        // запись закрыта везде: правила задачи главнее «своего файла» плагина
        var tools = new GatewayToolset([new GatewayPlugin(Shotcut())], _f.Objects, task, folder,
            _f.Files.DataDir, project.Name, (_, op) => op != "write");

        var answer = tools.ExecuteAsync("shotcut_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains("ai2p_library.mlt", answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "ai2p_library.mlt")));
    }

    /// <summary>Запись медиатеки с путём, который наружу не привести, ПРОПУСКАЕТСЯ, а не
    /// роняет весь экспорт: одна битая ссылка не повод оставить человека без монтажного листа —
    /// но её номер называется в ответе.</summary>
    [Fact]
    public void A_Media_Record_With_An_Unusable_Path_Is_Skipped_By_Name()
    {
        var (tools, projectId, folder) = Stand();
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        File.WriteAllText(Path.Combine(folder, "media", "sceneA.mp4"), "кадры");
        AddMedia(projectId, "своё", "media/sceneA.mp4", 2);
        // путь мимо проверки формы: строка приехала репликацией от партнёра прежней версии
        var broken = _f.Objects.List(projectId, kinds: [ObjectKinds.Media])[0];
        AddMedia(projectId, "битое", "media/нет-такого.mp4", 2);
        var missing = _f.Objects.List(projectId, kinds: [ObjectKinds.Media])
            .First(o => o.Id != broken.Id);

        var answer = tools.ExecuteAsync("shotcut_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        // файла нет на диске — ProjectFiles.Resolve вернёт путь, но копировать нечего:
        // такой ресурс уезжает в лист как есть, потому что он ВНУТРИ папки проекта
        var text = File.ReadAllText(Path.Combine(folder, "ai2p_library.mlt"));
        Assert.Contains("media/нет-такого.mp4", text, StringComparison.Ordinal);
        Assert.Contains(missing.DisplayId, _f.Objects.List(projectId,
            kinds: [ObjectKinds.Media]).Select(o => o.DisplayId));
        Assert.Contains("2", answer, StringComparison.Ordinal);
    }

    // ---------- 5. Рендер ----------

    /// <summary>
    /// БЕЗ НАЙДЕННОЙ ПРОГРАММЫ ДЕЙСТВИЕ РЕНДЕРА АГЕНТУ НЕ ПУБЛИКУЕТСЯ ВОВСЕ: показывать
    /// инструмент, который заведомо ответит отказом, значит тратить ход агента на выяснение
    /// этого. Сборка монтажного листа при этом работает — она программы не требует.
    /// </summary>
    [Fact]
    public void The_Render_Tool_Is_Not_Published_Without_The_Program()
    {
        var (without, _, _) = Stand();
        var (with, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "melt.exe"));

        Assert.Contains("shotcut_timeline_write", without.Specs.Select(s => s.Name));
        Assert.DoesNotContain("shotcut_render", without.Specs.Select(s => s.Name));
        Assert.Contains("shotcut_render", with.Specs.Select(s => s.Name));
        // и вызов «в обход» тоже отвергается: список инструментов агент может и не читать
        Assert.False(without.Handles("shotcut_render"));
    }

    /// <summary>Рендерить нечего — понятный отказ, а не пустой файл: собрать монтажный лист
    /// агент мог и забыть.</summary>
    [Fact]
    public void Render_Refuses_When_There_Is_No_Timeline_Yet()
    {
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "melt.exe"));

        var answer = tools.ExecuteAsync("shotcut_render", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Contains("ai2p_library.mlt", answer, StringComparison.Ordinal);
    }

    // ---------- 6. Пакет программы ----------

    /// <summary>
    /// ПАКЕТ ПОРТАТИВНОГО SHOTCUT ЕСТЬ В СПРАВОЧНИКЕ, и ссылка ведёт на релиз
    /// mltframework/shotcut. Она снята запросом (GitHub API + HEAD, 03.09.2026), а не
    /// выдумана: выдуманный адрес даёт кнопку «Установить», которая падает без объяснения.
    /// Блок <c>system</c> обязателен — melt чаще всего уже стоит у того, кто монтирует.
    /// </summary>
    [Fact]
    public void The_Portable_Shotcut_Package_Is_In_The_Catalog()
    {
        _f.Models.Seed();
        var catalog = ModelPackageCatalog.Parse(
            File.ReadAllText(_f.Files.Abs(AiModelService.PackagesPath)));

        var package = catalog.Find("shotcut");
        Assert.NotNull(package);
        Assert.Equal("melt.exe", package!.Check);
        var file = Assert.Single(package.Files);
        Assert.StartsWith("https://github.com/mltframework/shotcut/releases/download/",
            file.Url, StringComparison.Ordinal);
        Assert.EndsWith(".zip", file.Url, StringComparison.Ordinal);
        Assert.Equal("zip", file.Unpack);
        Assert.NotNull(package.System);
        Assert.Contains("melt", package.System!.Commands);
        Assert.Equal("7.0", package.System.MinVersion);
        // код пакета — тот же, что назван в манифесте шлюза, иначе кнопка «Установить»
        // ставила бы не то
        Assert.Equal(package.Id, Shotcut().Software!.Package);
    }

    /// <summary>Манифест шлюза ложится в каталог данных файлом дистрибутива и повторную
    /// запись переживает: у него своя версия, а не общая версия сида.</summary>
    [Fact]
    public void The_Manifest_Is_Written_To_The_Data_Dir()
    {
        PluginSeed.Write(_f.Files);
        var path = _f.Files.Abs(PluginManifest.PathOf(PluginSeed.ShotcutCode));
        Assert.True(File.Exists(path));
        var stamp = File.GetLastWriteTimeUtc(path);

        PluginSeed.Write(_f.Files); // идемпотентно: та же версия — файл не трогается

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(PluginSeed.ShotcutCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.ShotcutCode)!.Code);
    }

    // ---------- 7. Живая проверка ----------

    /// <summary>
    /// ЖИВОЙ РЕНДЕР БЕЗ ОКНА. Идёт только при заданной <c>AI2P_MELT</c> (полный путь к melt):
    /// программы в наборе нет, и краснеть у всех из-за её отсутствия проверка не должна.
    /// Сделано так, чтобы проверку можно было повторить: она и есть доказательство главного
    /// довода за этот шлюз — из четырёх редакторов ветки считать ролик без человека умеет
    /// только MLT.
    /// </summary>
    [Fact]
    public void Melt_Renders_The_Exported_Timeline()
    {
        var melt = Environment.GetEnvironmentVariable("AI2P_MELT") ?? "";
        if (melt.Length == 0 || !File.Exists(melt))
        {
            return;
        }
        var source = Environment.GetEnvironmentVariable("AI2P_MELT_MEDIA") ?? "";
        Assert.True(Directory.Exists(source), "AI2P_MELT_MEDIA: каталог с пробными роликами");

        var (tools, projectId, folder) = Stand(melt);
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        var names = new List<string>();
        foreach (var file in Directory.GetFiles(source).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(folder, "media", name), true);
            // РАЗНЫЕ ВИДЫ МЕДИА идут на РАЗНЫЕ дорожки: звук обязан лечь на A1, а не встать
            // третьим клипом в видеоряд — ради этого раскладка и описана в манифесте
            var kind = Path.GetExtension(name).ToLowerInvariant() is ".mp3" or ".m4a" or ".wav"
                ? MediaMeta.Audio
                : MediaMeta.Video;
            AddMedia(projectId, name, "media/" + name, 2, kind);
            names.Add(name);
        }
        Assert.NotEmpty(names);

        var built = tools.ExecuteAsync("shotcut_timeline_write", Args("{}"),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.Contains("ai2p_library.mlt", built, StringComparison.Ordinal);

        var rendered = tools.ExecuteAsync("shotcut_render", Args("""{"out": "out.mp4"}"""),
            CancellationToken.None).GetAwaiter().GetResult();

        var result = Path.Combine(folder, "out.mp4");
        Assert.True(File.Exists(result), rendered);
        Assert.True(new FileInfo(result).Length > 0, rendered);
        // собранный и посчитанный проект остаётся на диске, если попросили: по нему живая
        // проверка смотрит ffprobe’ом, что дорожки встали на свои места
        var keep = Environment.GetEnvironmentVariable("AI2P_MELT_OUT") ?? "";
        if (keep.Length > 0)
        {
            Directory.CreateDirectory(keep);
            foreach (var file in Directory.GetFiles(folder))
            {
                File.Copy(file, Path.Combine(keep, Path.GetFileName(file)), true);
            }
        }
    }
}
