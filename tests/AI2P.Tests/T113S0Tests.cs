using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-113-S0 — ИНСТРУМЕНТЫ АГЕНТА ДЛЯ МЕДИАТЕКИ ПРОЕКТА: media_add / media_list /
/// media_remove.
///
/// Пять вещей, ради которых проверки написаны:
/// <list type="number">
/// <item>ресурс ложится в медиатеку ССЫЛКОЙ — объектом проекта вида <c>media</c>, байты
/// не копируются, а сцена, дубль, порядок, длительность и задание-источник доезжают
/// до записи (из них потом собирается проект видеоредактора);</item>
/// <item>ПУТЬ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННОГО ОТВЕРГАЕТСЯ — абсолютный, с буквой диска,
/// с <c>..</c> и внешний адрес;</item>
/// <item>отбор media_list по сцене, виду и тэгу и порядок строк «как на монтажном листе»;</item>
/// <item>media_remove убирает ЗАПИСЬ, а файл на диске остаётся;</item>
/// <item>у каждого инструмента есть запись справочника действий на ВСЕХ пяти языках —
/// без неё правила безопасности инструмент не закрывают вовсе (2d3af8da).</item>
/// </list>
/// </summary>
public sealed class T113S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Медиатека одного проекта плюс задача, от имени которой работает агент.</summary>
    private (MediaToolset Tools, string ProjectId, TaskItem Task) Stand()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Снять сцену", ProjectId = project.Id },
            "", "", null);
        return (new MediaToolset(_f.Objects, task), project.Id, task);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    // ---------- 1. media_add ----------

    /// <summary>
    /// РЕСУРС ЛОЖИТСЯ ССЫЛКОЙ. Запись — объект проекта вида media; всё, что понадобится
    /// экспортёру (путь, вид, длительность, порядок, сцена, дубль, подпись), лежит в
    /// метаданных, а задание-источник проставляется само.
    /// </summary>
    [Fact]
    public void Media_Add_Puts_A_Link_Into_The_Library()
    {
        var (tools, projectId, task) = Stand();
        var answer = tools.Execute("media_add", Args("""
            {
              "path": "store:projects/PRJ-1/media/scene3-take2.mp4",
              "kind": "video",
              "caption": "Вася бежит",
              "scene": "3",
              "take": 2,
              "order": 10,
              "durationSec": 12.5,
              "tags": ["черновик"]
            }
            """));

        var item = Assert.Single(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
        Assert.Contains(item.DisplayId, answer);
        Assert.Equal("store:projects/PRJ-1/media/scene3-take2.mp4", item.PathOrUrl);
        var meta = MediaMeta.Parse(item.MetaJson);
        Assert.Equal(MediaMeta.Video, meta.Kind);
        Assert.Equal("Вася бежит", meta.Caption);
        Assert.Equal("3", meta.Scene);
        Assert.Equal(2, meta.Take);
        Assert.Equal(10, meta.Order);
        Assert.Equal(12.5, meta.DurationSec);
        // откуда взялось: по умолчанию — ТЕКУЩАЯ задача, иначе заполнять это поле агент
        // забудет ровно в тот раз, когда дубль понадобится переснять
        Assert.Equal(task.DisplayId, meta.SourceTask);
        // сцена ложится и тэгом — им работают уже готовый вид списка «тэги» и фильтр
        Assert.Contains(MediaLibrary.SceneTag("3"), item.Tags);
        Assert.Contains("черновик", item.Tags);
    }

    /// <summary>
    /// ПУТЬ ЗА ПРЕДЕЛАМИ РАЗРЕШЁННЫХ КАТАЛОГОВ ОТВЕРГАЕТСЯ. Проверка одна на всех и стоит
    /// в сервисе объектов (T-111-S0) — инструменту агента её не обойти, как и форме.
    /// </summary>
    [Theory]
    [InlineData(@"C:\video\scene3.mp4")]
    [InlineData("/var/video/scene3.mp4")]
    [InlineData("../соседний-проект/scene3.mp4")]
    [InlineData(@"\\сервер\общий\scene3.mp4")]
    [InlineData("https://example.com/scene3.mp4")]
    public void Media_Add_Refuses_A_Path_Outside_The_Allowed_Folders(string path)
    {
        var (tools, projectId, _) = Stand();
        var answer = tools.Execute("media_add",
            Args(JsonSerializer.Serialize(new { path })));

        Assert.Empty(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
        // отказ приходит агенту ТЕКСТОМ, а не исключением: иначе задание падало бы целиком
        Assert.Contains("media_add", answer);
        Assert.False(MediaLibrary.IsValidPath(path));
    }

    /// <summary>Неизвестный вид ресурса — понятный отказ со списком доступных, а не
    /// молча записанное «video».</summary>
    [Fact]
    public void Media_Add_Refuses_An_Unknown_Kind()
    {
        var (tools, projectId, _) = Stand();
        var answer = tools.Execute("media_add",
            Args("""{ "path": "render/a.mp4", "kind": "гифка" }"""));
        Assert.Empty(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
        Assert.Contains(MediaMeta.Video, answer);
        Assert.Contains(MediaMeta.Subtitle, answer);
    }

    /// <summary>
    /// ПОВТОРНЫЙ ВЫЗОВ С ТЕМ ЖЕ ПУТЁМ ОБНОВЛЯЕТ ЗАПИСЬ, а не плодит дубль: задание
    /// перезапускают, и медиатека, показывающая один ролик трижды, бесполезна.
    /// </summary>
    [Fact]
    public void Media_Add_Updates_The_Entry_Instead_Of_Duplicating_It()
    {
        var (tools, projectId, _) = Stand();
        tools.Execute("media_add",
            Args("""{ "path": "render/scene3.mp4", "scene": "3", "take": 1 }"""));
        tools.Execute("media_add",
            Args("""{ "path": "render/scene3.mp4", "scene": "3", "take": 2, "durationSec": 8 }"""));

        var item = Assert.Single(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
        var meta = MediaMeta.Parse(item.MetaJson);
        Assert.Equal(2, meta.Take);
        Assert.Equal(8, meta.DurationSec);
    }

    // ---------- 2. media_list ----------

    /// <summary>
    /// Отбор по сцене, виду и тэгу; порядок строк — монтажный: сцена, порядок, дубль.
    /// </summary>
    [Fact]
    public void Media_List_Filters_And_Keeps_The_Editing_Order()
    {
        var (tools, _, _) = Stand();
        tools.Execute("media_add", Args(
            """{ "path": "render/s1-b.mp4", "scene": "1", "order": 20, "caption": "второй" }"""));
        tools.Execute("media_add", Args(
            """{ "path": "render/s1-a.mp4", "scene": "1", "order": 10, "caption": "первый" }"""));
        tools.Execute("media_add", Args(
            """{ "path": "sound/s2.mp3", "kind": "audio", "scene": "2", "tags": ["музыка"] }"""));

        var all = tools.Execute("media_list", Args("{}"));
        Assert.True(all.IndexOf("s1-a.mp4", StringComparison.Ordinal)
                    < all.IndexOf("s1-b.mp4", StringComparison.Ordinal),
            "порядок 10 обязан стоять раньше порядка 20");
        Assert.Contains("s2.mp3", all);

        var scene1 = tools.Execute("media_list", Args("""{ "scene": "1" }"""));
        Assert.Contains("s1-a.mp4", scene1);
        Assert.DoesNotContain("s2.mp3", scene1);

        var audio = tools.Execute("media_list", Args("""{ "kind": "audio" }"""));
        Assert.Contains("s2.mp3", audio);
        Assert.DoesNotContain("s1-a.mp4", audio);

        var tagged = tools.Execute("media_list", Args("""{ "tag": "музыка" }"""));
        Assert.Contains("s2.mp3", tagged);
        Assert.DoesNotContain("s1-b.mp4", tagged);

        // отбор по сцене работает и тэгом сцены — той самой механикой объектов проекта
        var bySceneTag = tools.Execute("media_list",
            Args(JsonSerializer.Serialize(new { tag = MediaLibrary.SceneTag("2") })));
        Assert.Contains("s2.mp3", bySceneTag);
        Assert.DoesNotContain("s1-a.mp4", bySceneTag);
    }

    /// <summary>Пустая медиатека — обычный ответ, а не ошибка.</summary>
    [Fact]
    public void Media_List_Of_An_Empty_Library_Is_A_Plain_Answer()
    {
        var (tools, _, _) = Stand();
        Assert.Equal(Loc.T("prompt.media.8"), tools.Execute("media_list", Args("{}")));
    }

    // ---------- 3. media_remove ----------

    /// <summary>
    /// УБИРАЕТСЯ ЗАПИСЬ, А НЕ ФАЙЛ: медиатека хранит ссылки, и тот же файл обычно является
    /// артефактом задания — стирать его, «прибираясь в списке», она не вправе.
    /// </summary>
    [Fact]
    public void Media_Remove_Takes_Out_The_Entry_But_Not_The_File()
    {
        var (tools, projectId, _) = Stand();
        var dir = Path.Combine(_f.Dir, "media-stand");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "scene3.mp4");
        File.WriteAllBytes(file, [1, 2, 3]);

        tools.Execute("media_add", Args("""{ "path": "render/scene3.mp4" }"""));
        var code = Assert.Single(_f.Objects.List(projectId, kinds: [ObjectKinds.Media])).DisplayId;

        var answer = tools.Execute("media_remove", Args(JsonSerializer.Serialize(new { code })));
        Assert.Contains(code, answer);
        Assert.Empty(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
        Assert.True(File.Exists(file), "media_remove не имеет права трогать сам файл");
    }

    /// <summary>Убрать можно и по пути — код объекта агент помнить не обязан.</summary>
    [Fact]
    public void Media_Remove_Accepts_The_Path_Too()
    {
        var (tools, projectId, _) = Stand();
        tools.Execute("media_add", Args("""{ "path": "render/scene3.mp4" }"""));
        tools.Execute("media_remove", Args("""{ "path": "render/scene3.mp4" }"""));
        Assert.Empty(_f.Objects.List(projectId, kinds: [ObjectKinds.Media]));
    }

    /// <summary>Ни кода, ни пути — понятная просьба уточнить, а не молчаливое «готово».</summary>
    [Fact]
    public void Media_Remove_Without_A_Target_Says_So()
    {
        var (tools, _, _) = Stand();
        Assert.Equal(Loc.T("prompt.media.13"), tools.Execute("media_remove", Args("{}")));
        Assert.Contains("OBJ", tools.Execute("media_remove", Args("""{ "code": "OBJ-77" }""")));
    }

    // ---------- 4. публикация и справочник ----------

    /// <summary>
    /// У задачи БЕЗ ПРОЕКТА медиатеки нет: инструменты не публикуются, а вызов отвечает
    /// внятным отказом — складывать ролик просто некуда.
    /// </summary>
    [Fact]
    public void Media_Tools_Belong_To_A_Project()
    {
        var loose = new MediaToolset(_f.Objects, new TaskItem { Title = "Без проекта" });
        Assert.False(loose.CanUse);
        Assert.Equal(Loc.T("prompt.media.3"), loose.Execute("media_list", Args("{}")));

        var (tools, _, _) = Stand();
        Assert.True(tools.CanUse);
        Assert.Equal(["media_add", "media_list", "media_remove"],
            MediaToolset.Specs.Select(s => s.Name));
    }

    /// <summary>
    /// У КАЖДОГО ИНСТРУМЕНТА ЕСТЬ ЗАПИСЬ СПРАВОЧНИКА ДЕЙСТВИЙ НА ПЯТИ ЯЗЫКАХ. Без неё
    /// правила безопасности инструмент не закрывают ВОВСЕ: ActionCatalogService.CodeByTool
    /// возвращает null, и AgentToolset.Authorize разрешает вызов (2d3af8da).
    /// </summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("zh-cn")]
    public void Every_Media_Tool_Has_A_Catalog_Entry_In_Every_Language(string lang)
    {
        var i18n = Path.Combine(AppContext.BaseDirectory, "i18n");
        using var seed = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(i18n, $"ActionCatalogService_{lang}.json")));
        var actions = seed.RootElement.GetProperty("actions").EnumerateArray().ToList();
        // рост seedVersion обязателен: без него описания на уже работающих установках
        // остались бы прежними, а новых записей не появилось бы вовсе
        Assert.True(seed.RootElement.GetProperty("seedVersion").GetInt32() >= 19);
        foreach (var tool in MediaToolset.Specs.Select(s => s.Name))
        {
            // "tool" есть НЕ у каждой записи: системное действие без инструмента агента
            // (AI2P.Archives.AutoRun) поля не имеет вовсе, и безусловный GetProperty
            // бросил бы на ней ещё до сравнения
            var entry = actions.FirstOrDefault(
                a => a.TryGetProperty("tool", out var t) && t.GetString() == tool);
            Assert.True(entry.ValueKind == JsonValueKind.Object,
                $"в ActionCatalogService_{lang}.json нет записи на инструмент {tool}");
            Assert.StartsWith("AI2P.Media.", entry.GetProperty("code").GetString());
            foreach (var field in (string[])["title", "hint", "prompt"])
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty(field).GetString()),
                    $"{tool}: пустое поле {field} в языке {lang}");
            }
        }
        // и строки ответов инструментов — тоже во всех пяти словарях
        using var dict = JsonDocument.Parse(File.ReadAllText(Path.Combine(i18n, $"{lang}.json")));
        for (var i = 1; i <= 13; i++)
        {
            Assert.True(dict.RootElement.TryGetProperty($"prompt.media.{i}", out _),
                $"в i18n/{lang}.json нет ключа prompt.media.{i}");
        }
    }

    /// <summary>
    /// ФОРМА ЗАПИСИ МЕДИАТЕКИ — ДОГОВОР С ЭКСПОРТЁРАМИ (T-115…T-120): путь, вид,
    /// длительность, ПОРЯДОК, сцена, дубль, подпись и задание-источник обязаны
    /// пережить запись и чтение <c>meta_json</c>, не задев соседних ключей.
    /// </summary>
    [Fact]
    public void The_Record_Carries_Everything_The_Exporters_Need()
    {
        var json = new MediaMeta
        {
            Kind = MediaMeta.Project,
            Caption = "сборка",
            Scene = "финал",
            Take = 3,
            Order = 7,
            DurationSec = 61.25,
            SourceTask = "T-15",
            SourcePlugin = "editor.shotcut",
        }.Write("""{ "loraStrength": 0.8 }""");

        var back = MediaMeta.Parse(json);
        Assert.Equal(MediaMeta.Project, back.Kind);
        Assert.Equal("сборка", back.Caption);
        Assert.Equal("финал", back.Scene);
        Assert.Equal(3, back.Take);
        Assert.Equal(7, back.Order);
        Assert.Equal(61.25, back.DurationSec);
        Assert.Equal("T-15", back.SourceTask);
        Assert.Equal("editor.shotcut", back.SourcePlugin);
        Assert.Contains("loraStrength", json);

        // тэг сцены: запятая — разделитель тэгов, внутри тэга её быть не может
        Assert.Equal("scene:финал", MediaLibrary.SceneTag("финал"));
        Assert.Equal("", MediaLibrary.SceneTag("  "));
        Assert.DoesNotContain(',', MediaLibrary.SceneTag("3, дубль"));
    }
}
