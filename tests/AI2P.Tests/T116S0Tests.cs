using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-116-S0 — ПЛАГИН «ВИДЕО КОНВЕРТОР (ffmpeg)», подзадача ветки T-110-S0.
///
/// Шесть вещей, ради которых написаны проверки:
/// <list type="number">
/// <item>ДЕЙСТВИЙ «ВЫПОЛНИТЬ КОМАНДНУЮ СТРОКУ» НЕТ, и это главное требование заказчика:
/// у каждого действия роль <c>convert</c> и ИМЯ операции, а операция целиком описана в
/// манифесте — от агента приходят только пути;</item>
/// <item>ЭТАЛОННАЯ КОМАНДНАЯ СТРОКА НА КАЖДОЕ ДЕЙСТВИЕ: подставленный не туда ключ
/// («-c:a» вместо «-c:v») иначе замечается по испорченному файлу через полчаса счёта;</item>
/// <item>ОТКАЗ ПО ПУТИ за пределами разрешённых каталогов — и по входному, и по выходному,
/// плюс контрольный опыт: внешний каталог, открытый правилами задачи, работает;</item>
/// <item>ОТКАЗ, ЕСЛИ ffmpeg НЕ НАЙДЕН НА ЭТОМ СЕРВЕРЕ: инструмент не публикуется вовсе;</item>
/// <item>ПРОВЕРКА ВЕРСИИ обязательна — набор ключей ffmpeg заметно менялся между 4.x и 7.x,
/// и найденный 5.1 из репозитория Debian сорвал бы работу уже во время задания;</item>
/// <item>ПАКЕТ ffmpeg есть в справочнике пакетов, ссылка снята запросом.</item>
/// </list>
///
/// <para>Живая проверка (<see cref="Ffmpeg_Converts_A_Real_Generation"/>) идёт только при
/// заданной переменной окружения <c>AI2P_FFMPEG</c>: программы в наборе нет, и краснеть у всех
/// из-за отсутствия чужой программы проверка не должна.</para>
/// </summary>
public sealed class T116S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Манифест конвертора дистрибутива — тот же, что ложится в каталог данных.</summary>
    private static PluginManifest Ffmpeg() =>
        PluginManifest.Parse(PluginSeed.FfmpegJson)
        ?? throw new InvalidOperationException("манифест конвертора ffmpeg негоден");

    // ---------- 1. Манифест и запрет произвольной команды ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ: четыре узких именованных действия, софт с обязательной
    /// проверкой версии, настройки-параметры работы и опыт с навыками.
    /// </summary>
    [Fact]
    public void The_Ffmpeg_Manifest_Is_Read_Whole()
    {
        var manifest = Ffmpeg();

        Assert.Equal(PluginSeed.FfmpegCode, manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);
        // конвертор не собирает монтажный лист и не рендерит: он готовит для них материал
        Assert.Null(manifest.Export);
        Assert.Null(manifest.Render);
        Assert.NotNull(manifest.Convert);

        Assert.Equal(4, manifest.Actions.Count);
        Assert.Equal(["ffmpeg_to_edit", "ffmpeg_extract_audio", "ffmpeg_concat", "ffmpeg_uniform"],
            manifest.Actions.Select(a => a.Tool));
        Assert.All(manifest.Actions, a =>
        {
            Assert.Equal(PluginAction.RoleConvert, a.Role);
            Assert.True(a.NeedsSoftware);   // перекодировать нечем без самой программы
            Assert.StartsWith("AI2P.Plugins.Ffmpeg.", a.Code, StringComparison.Ordinal);
            foreach (var lang in Loc.Languages)
            {
                Assert.NotEqual("", a.Title.Text(lang));
                Assert.NotEqual("", a.Description.Text(lang));
            }
        });

        // софт: ищется на этом сервере, ставится пакетом, версия проверяется ОБЯЗАТЕЛЬНО
        Assert.NotNull(manifest.Software);
        Assert.Equal("ffmpeg", manifest.Software!.Package);
        Assert.True(manifest.Software.Required);
        Assert.Equal("ffmpegPath", manifest.Software.PathKey);
        Assert.Contains("ffmpeg", manifest.Software.System!.Commands);
        Assert.Equal("-version", manifest.Software.System.VersionArgs);
        Assert.NotEqual("", manifest.Software.System.MinVersion);

        // опыт: навык у КАЖДОЙ записи (требование заказчика; запись без навыка конкурирует
        // за место в промпте у каждой задачи организации — a8a79272)
        Assert.All(manifest.Experience, e => Assert.NotEqual("", e.Skill));
        Assert.Contains("video-edit", manifest.Experience.Select(e => e.Skill));
        Assert.Contains("audio-music", manifest.Experience.Select(e => e.Skill));
        Assert.Contains("audio-speech", manifest.Experience.Select(e => e.Skill));

        // документ плагина — на всех пяти языках
        foreach (var lang in Loc.Languages)
        {
            Assert.Equal("plugins/tool.ffmpeg.md", manifest.Doc[lang]);
        }
    }

    /// <summary>
    /// ДЕЙСТВИЯ «ВЫПОЛНИТЬ КОМАНДНУЮ СТРОКУ ffmpeg» НЕТ — и его нельзя выпросить обходом:
    /// у каждого действия есть ИМЯ операции, а вся операция описана в манифесте. Действие с
    /// пустым <c>op</c> не публикуется и не исполняется вовсе: «сделать что-нибудь
    /// конвертором» — это и есть произвольная командная строка.
    /// </summary>
    [Fact]
    public void There_Is_No_Run_A_Command_Line_Action()
    {
        var manifest = Ffmpeg();

        Assert.All(manifest.Actions, a =>
        {
            Assert.NotEqual("", a.Op);
            Assert.NotNull(manifest.Convert!.Find(a.Op));
        });
        // операций ровно столько, сколько действий: «лишняя» операция манифеста означала бы
        // работу, до которой не добраться, а лишнее действие — действие без работы
        Assert.Equal(manifest.Actions.Count, manifest.Convert!.Ops.Count);

        // безымянное действие с той же ролью инструментом не становится — при том что у
        // соседнего действия ТОГО ЖЕ манифеста имя операции есть, и оно публикуется
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));
        var (loose, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"), manifest:
            PluginManifest.Parse("""
                {
                  "code": "tool.any", "kind": "gateway",
                  "actions": [
                    { "code": "X.Any",  "tool": "ffmpeg_anything", "role": "convert" },
                    { "code": "X.Edit", "tool": "ffmpeg_named", "role": "convert", "op": "one" }
                  ],
                  "convert": { "ops": [ { "op": "one", "args": ["-i", "{in}", "{out}"] } ] }
                }
                """));

        Assert.DoesNotContain("ffmpeg_anything", loose.Specs.Select(s => s.Name));
        Assert.Contains("ffmpeg_named", loose.Specs.Select(s => s.Name));
        Assert.False(loose.Handles("ffmpeg_anything"));
        Assert.Equal(4, tools.Specs.Count());
    }

    // ---------- 2. Эталонные командные строки ----------

    /// <summary>
    /// ЭТАЛОННАЯ КОМАНДНАЯ СТРОКА НА КАЖДОЕ ДЕЙСТВИЕ. Проверка нарочно посимвольная: строка,
    /// которую получает ffmpeg, и есть граница безопасности плагина, а перепутанный ключ
    /// («-c:a» вместо «-c:v», забытый «-vn») даёт не отказ, а МОЛЧА испорченный файл через
    /// полчаса счёта.
    /// </summary>
    [Theory]
    [InlineData("intermediate",
        "-hide_banner -nostdin -y -i IN -map 0:v:0? -c:v dnxhd -profile:v dnxhr_hq "
        + "-pix_fmt yuv422p -map 0:a? -c:a pcm_s16le -ar 48000 -progress pipe:1 -nostats OUT")]
    [InlineData("audio",
        "-hide_banner -nostdin -y -i IN -vn -map 0:a:0? -c:a pcm_s16le -ar 48000 "
        + "-progress pipe:1 -nostats OUT")]
    [InlineData("concat",
        "-hide_banner -nostdin -y -f concat -safe 0 -i LIST -c copy "
        + "-progress pipe:1 -nostats OUT")]
    [InlineData("uniform",
        "-hide_banner -nostdin -y -i IN -map 0:v:0? -map 0:a? -vf "
        + "scale=1920:1080:force_original_aspect_ratio=decrease,"
        + "pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=25 "
        + "-c:v dnxhd -profile:v dnxhr_hq -pix_fmt yuv422p -c:a pcm_s16le -ar 48000 "
        + "-progress pipe:1 -nostats OUT")]
    public void Every_Operation_Builds_Its_Reference_Command_Line(string op, string expected)
    {
        var manifest = Ffmpeg();
        var values = ConvertLine.Values(manifest.Settings, "");

        var line = ConvertLine.Build(manifest.Convert!.Find(op)!, "IN", "OUT", "LIST", values,
            out var unresolved);

        Assert.Equal("", unresolved);
        Assert.Equal(expected, string.Join(" ", line!));
    }

    /// <summary>
    /// ПАРАМЕТРЫ РАБОТЫ БЕРУТСЯ ИЗ НАСТРОЕК ЗАПИСИ, а не из строки задания: человек поменял
    /// кодек в форме плагина — поменялась команда. Значение, которого нет среди объявленных
    /// (опечатка в имени кодека), заменяется умолчанием: иначе оно стало бы отказом ffmpeg
    /// через полчаса счёта.
    /// </summary>
    [Fact]
    public void Settings_Of_The_Record_Drive_The_Line_And_A_Typo_Falls_Back()
    {
        var manifest = Ffmpeg();

        var chosen = ConvertLine.Values(manifest.Settings,
            """{"videoCodec": "prores_ks", "videoProfile": "3", "pixelFormat": "yuv422p10le"}""");
        var typo = ConvertLine.Values(manifest.Settings, """{"videoCodec": "dnxdh"}""");
        var broken = ConvertLine.Values(manifest.Settings, "{ это не JSON");

        var line = ConvertLine.Build(manifest.Convert!.Find("intermediate")!, "IN", "OUT", "",
            chosen, out _);
        Assert.Contains("prores_ks", line!);
        Assert.Contains("yuv422p10le", line);
        Assert.DoesNotContain("dnxhd", line);
        Assert.Equal("dnxhd", typo["videoCodec"]);
        Assert.Equal("dnxhd", broken["videoCodec"]);
    }

    /// <summary>Нераскрытый плейсхолдер — дефект манифеста, и он виден отказом ПЛАГИНА:
    /// отданный ffmpeg, он обернулся бы невнятным отказом самой программы.</summary>
    [Fact]
    public void An_Unresolved_Placeholder_Is_A_Refusal_Not_An_Argument()
    {
        var op = new ConvertOp { Op = "x", Args = ["-i", "{in}", "-c:v", "{нетТакой}", "{out}"] };

        var line = ConvertLine.Build(op, "IN", "OUT", "", new Dictionary<string, string>(),
            out var unresolved);

        Assert.Null(line);
        Assert.Equal("{нетТакой}", unresolved);
    }

    /// <summary>Файл-список склейки: апостроф в имени экранируется — иначе строка оборвалась
    /// бы и склеилось бы не то.</summary>
    [Fact]
    public void The_Concat_List_Escapes_The_Quote()
    {
        var op = Ffmpeg().Convert!.Find("concat")!;

        var text = ConvertLine.ListText(op, ["/x/a.mov", "/x/д'артаньян.mov"]);

        Assert.Equal("file '/x/a.mov'\nfile '/x/д'\\''артаньян.mov'\n", text);
    }

    // ---------- 3. Стенд ----------

    private int _stands;

    /// <summary>Стенд: проект с папкой, задача проекта и конвертор с найденной (или нет)
    /// программой.</summary>
    private (GatewayToolset Tools, string ProjectId, string Folder) Stand(
        string softwarePath = "", IReadOnlyList<string>? allowedDirs = null,
        Func<string, string, bool>? permitted = null, PluginManifest? manifest = null)
    {
        var number = ++_stands;
        var folder = Path.Combine(_f.Dir, "project-folder" + number);
        Directory.CreateDirectory(folder);
        var project = _f.Projects.Create("Ролик " + number, folder, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Перекодировать", ProjectId = project.Id },
            "", "", null);
        var tools = new GatewayToolset([new GatewayPlugin(manifest ?? Ffmpeg(), softwarePath)],
            _f.Objects, task, folder, _f.Files.DataDir, project.Name, permitted, allowedDirs);
        return (tools, project.Id, folder);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static string Run(GatewayToolset tools, string tool, string args) =>
        tools.ExecuteAsync(tool, Args(args), CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Пустышка вместо ролика: до запуска ffmpeg дело в этих проверках не доходит —
    /// они все про отказ ДО запуска.</summary>
    private static string Clip(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "кадры");
        return path;
    }

    // ---------- 4. Ограничение каталогом ----------

    /// <summary>
    /// ОТКАЗ ПО ВЫХОДНОМУ ПУТИ за пределами разрешённых каталогов. Проверять это одним лишь
    /// правилом безопасности нельзя: у задачи без правил «запрета нет», и тогда конвертор
    /// писал бы куда угодно на диске (на Windows «/tmp/x» считается АБСОЛЮТНЫМ путём).
    /// </summary>
    [Theory]
    [InlineData("../снаружи.mov")]
    [InlineData(@"C:\снаружи.mov")]
    [InlineData("/tmp/снаружи.mov")]
    public void Convert_Refuses_An_Output_Path_Outside_The_Allowed_Dirs(string path)
    {
        var (tools, _, folder) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));
        Clip(folder, "media/gen.mp4");

        var answer = Run(tools, "ffmpeg_to_edit",
            JsonSerializer.Serialize(new { @in = "media/gen.mp4", @out = path }));

        Assert.Contains(path, answer, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_f.Dir, "снаружи.mov", SearchOption.AllDirectories));
    }

    /// <summary>ОТКАЗ ПО ВХОДНОМУ ПУТИ: читать чужое так же нельзя, как писать в чужое —
    /// иначе перекодированием можно было бы вынести наружу любой файл диска.</summary>
    [Theory]
    [InlineData("../снаружи.mp4")]
    [InlineData(@"C:\Windows\win.ini")]
    public void Convert_Refuses_An_Input_Path_Outside_The_Allowed_Dirs(string path)
    {
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));

        var answer = Run(tools, "ffmpeg_to_edit", JsonSerializer.Serialize(new { @in = path }));

        Assert.Contains(path, answer, StringComparison.Ordinal);
    }

    /// <summary>
    /// КОНТРОЛЬНЫЙ ОПЫТ К ДВУМ ПРЕДЫДУЩИМ: ВНЕШНИЙ КАТАЛОГ, открытый правилами безопасности
    /// задачи, конвертору доступен — иначе те проверки доказывали бы лишь то, что запрещено
    /// всё подряд. Здесь дело доходит до запуска программы, поэтому отказ ожидается уже НЕ
    /// про путь: путь принят, а «ffmpeg.exe» — пустышка.
    /// </summary>
    [Fact]
    public void An_External_Dir_Opened_By_The_Rules_Is_Allowed()
    {
        var outside = Path.Combine(_f.Dir, "обмен");
        Directory.CreateDirectory(outside);
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"),
            allowedDirs: [outside]);
        var source = Clip(outside, "gen.mp4");

        var answer = Run(tools, "ffmpeg_to_edit", JsonSerializer.Serialize(
            new { @in = source, @out = Path.Combine(outside, "gen_edit.mov") }));

        Assert.DoesNotContain(Loc.T("prompt.gateway.16", source), answer, StringComparison.Ordinal);
        Assert.DoesNotContain(Loc.T("prompt.gateway.17", source), answer, StringComparison.Ordinal);
    }

    /// <summary>Правило безопасности задачи главнее: закрытый на чтение путь внутри папки
    /// проекта конвертору тоже закрыт — иначе «запретить агенту читать подкаталог» работало бы
    /// для read_file и не работало для перекодирования.</summary>
    [Fact]
    public void Convert_Obeys_The_Security_Rules_Of_The_Task()
    {
        var (tools, _, folder) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"),
            permitted: (_, op) => op != "read");
        Clip(folder, "media/gen.mp4");

        var answer = Run(tools, "ffmpeg_to_edit",
            JsonSerializer.Serialize(new { @in = "media/gen.mp4" }));

        Assert.Equal(Loc.T("prompt.gateway.17", "media/gen.mp4"), answer);
    }

    /// <summary>Результат НЕ ЛОЖИТСЯ поверх исходника: ffmpeg с ключом «-y» затёр бы его
    /// пустым файлом ещё до чтения, и молча потерянный материал хуже отказа.</summary>
    [Fact]
    public void Convert_Refuses_To_Overwrite_Its_Own_Source()
    {
        var (tools, _, folder) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));
        Clip(folder, "media/gen.mp4");

        var answer = Run(tools, "ffmpeg_to_edit", JsonSerializer.Serialize(
            new { @in = "media/gen.mp4", @out = "media/gen.mp4" }));

        Assert.Equal(Loc.T("prompt.ffmpeg.2", "media/gen.mp4"), answer);
        Assert.Equal("кадры", File.ReadAllText(Path.Combine(folder, "media", "gen.mp4")));
    }

    /// <summary>Входного файла нет — внятный отказ, а не запуск программы впустую.</summary>
    [Fact]
    public void Convert_Refuses_When_The_Input_Does_Not_Exist()
    {
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));

        var answer = Run(tools, "ffmpeg_to_edit",
            JsonSerializer.Serialize(new { @in = "media/нет-такого.mp4" }));

        Assert.Equal(Loc.T("prompt.ffmpeg.8", "media/нет-такого.mp4"), answer);
    }

    /// <summary>Склейке нечего склеивать (ни списка, ни медиатеки) — отказ с именем операции,
    /// а не пустой файл.</summary>
    [Fact]
    public void Concat_Refuses_When_There_Is_Nothing_To_Join()
    {
        var (tools, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));

        var answer = Run(tools, "ffmpeg_concat", "{}");

        Assert.Equal(Loc.T("prompt.ffmpeg.7", "concat"), answer);
    }

    /// <summary>Склейка берёт список из МЕДИАТЕКИ проекта тем же порядком, что печатает
    /// media_list: сцена, порядок, дубль. Файл-список ложится рядом с результатом.</summary>
    [Fact]
    public void Concat_Takes_The_Project_Media_Library_In_The_Media_List_Order()
    {
        var (tools, projectId, folder) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));
        Clip(folder, "media/second.mp4");
        Clip(folder, "media/first.mp4");
        AddMedia(projectId, "второй", "media/second.mp4", order: 20);
        AddMedia(projectId, "первый", "media/first.mp4", order: 10);

        Run(tools, "ffmpeg_concat", "{}");

        var list = File.ReadAllText(Path.Combine(folder, "media", "ai2p_concat.txt"));
        Assert.True(list.IndexOf("first.mp4", StringComparison.Ordinal)
                    < list.IndexOf("second.mp4", StringComparison.Ordinal), list);
    }

    private void AddMedia(string projectId, string name, string path, int order = 0,
        string kind = MediaMeta.Video)
    {
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = ObjectKinds.Media,
            PathOrUrl = path,
            MetaJson = new MediaMeta { Kind = kind, Order = order, DurationSec = 2 }.Write("{}"),
        }, null);
    }

    // ---------- 5. Софт: не найден и версия ----------

    /// <summary>
    /// БЕЗ НАЙДЕННОЙ ПРОГРАММЫ ДЕЙСТВИЙ У КОНВЕРТОРА НЕТ ВОВСЕ: перекодировать нечем, и
    /// показывать агенту инструмент, который заведомо ответит отказом, значит тратить его ход
    /// на выяснение этого. Вызов «в обход» списка тоже отвергается.
    /// </summary>
    [Fact]
    public void Nothing_Is_Published_When_Ffmpeg_Is_Not_Found_Here()
    {
        var (without, _, _) = Stand();
        var (with, _, _) = Stand(softwarePath: Path.Combine(_f.Dir, "ffmpeg.exe"));

        Assert.Empty(without.Specs);
        Assert.False(without.Handles("ffmpeg_to_edit"));
        Assert.Equal(Loc.T("prompt.gateway.1", "ffmpeg_to_edit"),
            Run(without, "ffmpeg_to_edit", """{"in": "media/gen.mp4"}"""));
        Assert.Equal(4, with.Specs.Count());
    }

    /// <summary>
    /// ПРОВЕРКА ВЕРСИИ ОБЯЗАТЕЛЬНА: набор ключей ffmpeg заметно менялся между 4.x и 7.x, и
    /// найденный в PATH 5.1 (Debian 12) сорвал бы работу уже во время задания — а искали бы
    /// причину в плагине. Пределы включительные, по паре «старшая.младшая».
    /// </summary>
    [Theory]
    [InlineData("4.4.2", false)]
    [InlineData("5.1.6", false)]
    [InlineData("6.0", true)]
    [InlineData("7.1.1", true)]
    [InlineData("9.0.1", true)]
    [InlineData("", false)]
    public void The_Version_Of_A_Found_Ffmpeg_Is_Checked(string version, bool good)
    {
        var probe = Ffmpeg().Software!.System!;

        Assert.Equal(good, SystemPackageProbe.Accepts(version, probe.MinVersion, probe.MaxVersion));
    }

    /// <summary>Строка ответа настоящего ffmpeg разбирается на версию: «не разобрали» здесь
    /// означает «не годится», и молчаливо сойти за годную она не может.</summary>
    [Fact]
    public void The_Version_Is_Taken_From_The_Real_Banner_Of_Ffmpeg()
    {
        var probe = Ffmpeg().Software!.System!;
        const string banner = "ffmpeg version 9.0.1-essentials_build-www.gyan.dev Copyright (c) "
                              + "2000-2026 the FFmpeg developers";

        var match = System.Text.RegularExpressions.Regex.Match(banner, @"(\d+)\.(\d+)(\.\d+)?");

        Assert.Equal("9.0.1", match.Value);
        Assert.True(SystemPackageProbe.Accepts(match.Value, probe.MinVersion, probe.MaxVersion));
    }

    /// <summary>
    /// ПАКЕТ ffmpeg ЕСТЬ В СПРАВОЧНИКЕ, и ссылка снята HEAD-запросом (03.09.2026,
    /// 111 253 802 байта), а не выдумана: выдуманный адрес даёт кнопку «Установить», которая
    /// падает без объяснения. Блок <c>system</c> обязателен — ffmpeg чаще всего уже стоит.
    /// </summary>
    [Fact]
    public void The_Ffmpeg_Package_Is_In_The_Catalog()
    {
        _f.Models.Seed();
        var catalog = ModelPackageCatalog.Parse(
            File.ReadAllText(_f.Files.Abs(AiModelService.PackagesPath)));

        var package = catalog.Find("ffmpeg");
        Assert.NotNull(package);
        Assert.Equal("ffmpeg.exe", package!.Check);
        var file = Assert.Single(package.Files);
        Assert.StartsWith("https://", file.Url, StringComparison.Ordinal);
        Assert.EndsWith(".zip", file.Url, StringComparison.Ordinal);
        Assert.Equal("zip", file.Unpack);
        // ссылка ВЕРСИОНИРОВАННАЯ: подвижная означала бы, что версия у каждого своя
        Assert.DoesNotContain("release-essentials", file.Url, StringComparison.Ordinal);
        Assert.NotNull(package.System);
        Assert.Equal("-version", package.System!.VersionArgs);
        // код пакета — тот же, что назван в манифесте, иначе кнопка «Установить» ставила бы
        // не то, а проверка версии шла бы мимо
        Assert.Equal(package.Id, Ffmpeg().Software!.Package);
        Assert.Equal(package.System.MinVersion, Ffmpeg().Software!.System!.MinVersion);
    }

    /// <summary>Манифест конвертора ложится в каталог данных файлом дистрибутива — рядом со
    /// шлюзом в Shotcut, своим подкаталогом.</summary>
    [Fact]
    public void The_Manifest_Is_Written_To_The_Data_Dir()
    {
        PluginSeed.Write(_f.Files);

        Assert.True(File.Exists(_f.Files.Abs(PluginManifest.PathOf(PluginSeed.FfmpegCode))));
        Assert.Equal(PluginSeed.FfmpegCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.FfmpegCode)!.Code);
        Assert.Contains(PluginSeed.FfmpegJson, PluginSeed.All);
    }

    /// <summary>Тексты отказов и ответов конвертора есть во ВСЕХ пяти словарях: ключ, забытый
    /// в словаре, доезжает до агента строкой «prompt.ffmpeg.6» и читается как поломка.</summary>
    [Fact]
    public void Every_Answer_Of_The_Converter_Is_Translated_Into_All_Languages()
    {
        for (var number = 1; number <= 8; number++)
        {
            var key = "prompt.ffmpeg." + number;
            foreach (var lang in Loc.Languages)
            {
                Assert.NotEqual(key, Loc.In(lang, key));
            }
        }
    }

    // ---------- 6. Живая проверка ----------

    /// <summary>
    /// ЖИВАЯ ПЕРЕКОДИРОВКА НАСТОЯЩЕГО ФАЙЛА ГЕНЕРАЦИИ (mp4/H.264 → промежуточный формат
    /// монтажа). Идёт только при заданной <c>AI2P_FFMPEG</c> (полный путь к ffmpeg): программы
    /// в наборе нет, и краснеть у всех из-за её отсутствия проверка не должна.
    ///
    /// <para>Каталог <c>AI2P_FFMPEG_MEDIA</c>: <c>gen.mp4</c> — ролик генерации как есть,
    /// <c>gen_sound.mp4</c> — он же со звуковой дорожкой (у генераций Kandinsky звука нет
    /// вовсе, а извлекать его надо из чего-то). Результаты складываются в
    /// <c>AI2P_FFMPEG_OUT</c>, там их разбирает ffprobe’ом <c>test/t116s0/live.py</c>.</para>
    /// </summary>
    [Fact]
    public void Ffmpeg_Converts_A_Real_Generation()
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AI2P_FFMPEG") ?? "";
        if (ffmpeg.Length == 0 || !File.Exists(ffmpeg))
        {
            return;
        }
        var source = Environment.GetEnvironmentVariable("AI2P_FFMPEG_MEDIA") ?? "";
        Assert.True(Directory.Exists(source), "AI2P_FFMPEG_MEDIA: каталог с роликами генерации");

        var (tools, projectId, folder) = Stand(ffmpeg);
        Directory.CreateDirectory(Path.Combine(folder, "media"));
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(folder, "media", Path.GetFileName(file)), true);
        }
        Assert.True(File.Exists(Path.Combine(folder, "media", "gen.mp4")), "нет media/gen.mp4");

        // 1. промежуточный формат монтажа — то самое, ради чего плагин и заведён
        var edit = Run(tools, "ffmpeg_to_edit", """{"in": "media/gen.mp4"}""");
        Assert.True(File.Exists(Path.Combine(folder, "media", "gen_edit.mov")), edit);

        // 2. звук отдельным файлом WAV
        var audio = Run(tools, "ffmpeg_extract_audio", """{"in": "media/gen_sound.mp4"}""");
        Assert.True(File.Exists(Path.Combine(folder, "media", "gen_sound_audio.wav")), audio);

        // 3. общий кадр и частота — подготовка кусков к склейке
        var first = Run(tools, "ffmpeg_uniform",
            """{"in": "media/gen.mp4", "out": "media/u1.mov"}""");
        var second = Run(tools, "ffmpeg_uniform",
            """{"in": "media/gen_sound.mp4", "out": "media/u2.mov"}""");
        Assert.True(File.Exists(Path.Combine(folder, "media", "u1.mov")), first);
        Assert.True(File.Exists(Path.Combine(folder, "media", "u2.mov")), second);

        // 4. склейка по списку — БЕЗ перекодирования, поэтому куски и приводились к общему виду
        var joined = Run(tools, "ffmpeg_concat",
            """{"files": ["media/u1.mov", "media/u2.mov"], "out": "media/together.mov"}""");
        Assert.True(File.Exists(Path.Combine(folder, "media", "together.mov")), joined);
        Assert.Contains("concat", joined, StringComparison.Ordinal);

        var keep = Environment.GetEnvironmentVariable("AI2P_FFMPEG_OUT") ?? "";
        if (keep.Length > 0)
        {
            Directory.CreateDirectory(keep);
            foreach (var file in Directory.GetFiles(Path.Combine(folder, "media")))
            {
                File.Copy(file, Path.Combine(keep, Path.GetFileName(file)), true);
            }
        }
    }
}
