using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-276: СПИСОК ОБЪЕКТОВ — ТАБЛИЦА С ПРЕВЬЮ, ОТБОР ПО ВИДАМ, ВЫБОР ОБЪЕКТА ИЗ РЕДАКТОРА.
///
/// Что проверяется:
/// <list type="number">
/// <item>РАЗБОР ВИДОВ (<c>ObjectKinds.Parse</c>) — строка запроса «character,image» в список
/// видов; неизвестный код отбрасывается, а не опустошает список;</item>
/// <item>ОТБОР ПО ВИДАМ в хранилище (<c>ObjectService.List</c>) — один вид, несколько видов
/// по ИЛИ, вместе с тэгами и поиском; отбор не ломает число детей;</item>
/// <item>АДРЕСА ПРЕВЬЮ (<c>ObjectPics</c>) — свой файл объекта, картинка внутреннего списка
/// (в том числе через датасет), удалённый адрес, «картинки нет», защита от цикла;</item>
/// <item>РАЗМЕТКА И СЛОВАРИ — четыре вида списка, фильтры, компонент превью с ленивой
/// загрузкой, ключи в ОБОИХ словарях.</item>
/// </list>
///
/// Чего здесь НЕТ: самой загрузки картинок. Её делает браузер (<c>loading="lazy"</c>), и
/// доказывается она живой проверкой по CDP — <c>img.naturalWidth &gt; 0</c> (наука T-262).
/// </summary>
public sealed class T276Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private string NewProject(string name = "Ролик") => _f.Projects.Create(name, null, null, null).Id;

    private ObjectItem NewObject(string projectId, string name,
        string kind = ObjectKinds.Character, string? parentId = null,
        string path = "", IEnumerable<string>? tags = null) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = name,
            Type = kind,
            PathOrUrl = path,
            Tags = tags?.ToList() ?? [],
        }, null);

    // ---------- 1. разбор списка видов ----------

    /// <summary>Строка запроса — в виды: пробелы и регистр не мешают, повторов не остаётся,
    /// порядок приводится к порядку показа.</summary>
    [Fact]
    public void The_Kinds_Of_A_Query_Are_Parsed()
    {
        Assert.Equal(new[] { ObjectKinds.Character }, ObjectKinds.Parse("character"));
        Assert.Equal(new[] { ObjectKinds.Character, ObjectKinds.Image },
            ObjectKinds.Parse(" image , Character "));
        Assert.Equal(new[] { ObjectKinds.Character }, ObjectKinds.Parse("character,character"));
    }

    /// <summary>
    /// НЕИЗВЕСТНЫЙ КОД ОТБРАСЫВАЕТСЯ, а «одни неизвестные» означают «отбора нет»: строка
    /// приходит из запроса, и опечатка в ней не должна показывать пустой список — пустой
    /// список человек читает как «объектов нет».
    /// </summary>
    [Fact]
    public void An_Unknown_Kind_Is_Dropped_And_Never_Empties_The_List()
    {
        Assert.Equal(new[] { ObjectKinds.Character }, ObjectKinds.Parse("character,вымысел"));
        Assert.Empty(ObjectKinds.Parse("вымысел"));
        Assert.Empty(ObjectKinds.Parse(""));
        Assert.Empty(ObjectKinds.Parse(null));
    }

    // ---------- 2. отбор по видам в хранилище ----------

    /// <summary>Отбор по одному виду отдаёт только его; отбора нет — отдаётся всё.</summary>
    [Fact]
    public void The_List_Can_Be_Filtered_By_One_Kind()
    {
        var project = NewProject();
        NewObject(project, "Герой");
        NewObject(project, "Улица", ObjectKinds.Location);
        NewObject(project, "Кадр", ObjectKinds.Image);

        var all = _f.Objects.List(project);
        var heroes = _f.Objects.List(project, kinds: [ObjectKinds.Character]);

        Assert.Equal(3, all.Count);
        Assert.Equal("Герой", Assert.Single(heroes).Name);
    }

    /// <summary>Несколько видов соединяются по ИЛИ — как тэги: «персонажи И локации» это
    /// одно требование.</summary>
    [Fact]
    public void Several_Kinds_Are_Joined_By_Or()
    {
        var project = NewProject();
        NewObject(project, "Герой");
        NewObject(project, "Улица", ObjectKinds.Location);
        NewObject(project, "Кадр", ObjectKinds.Image);

        var picked = _f.Objects.List(project, kinds: [ObjectKinds.Character, ObjectKinds.Location]);

        Assert.Equal(new[] { "Герой", "Улица" },
            picked.Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>Отбор по видам складывается с тэгами и поиском (все три — «и»), а
    /// неизвестный вид отбор не включает вовсе.</summary>
    [Fact]
    public void The_Kind_Filter_Works_Together_With_Tags_And_Search()
    {
        var project = NewProject();
        NewObject(project, "Герой", tags: ["главный"]);
        NewObject(project, "Второй герой", tags: ["второй"]);
        NewObject(project, "Улица", ObjectKinds.Location, tags: ["главный"]);

        Assert.Equal("Герой", Assert.Single(_f.Objects.List(project, tags: ["главный"],
            kinds: [ObjectKinds.Character])).Name);
        Assert.Equal("Второй герой", Assert.Single(_f.Objects.List(project, search: "Второй",
            kinds: [ObjectKinds.Character])).Name);
        Assert.Equal(3, _f.Objects.List(project, kinds: ["вымысел"]).Count);
    }

    /// <summary>
    /// Отбор по видам НЕ ВРЁТ про внутренний список: число детей считается по проекту
    /// целиком, поэтому у персонажа, чьи кадры отсеяны фильтром, в строке остаётся честное
    /// «+2» — иначе фильтр по виду выглядел бы как пропажа кадров.
    /// </summary>
    [Fact]
    public void Filtering_By_Kind_Keeps_The_Child_Count_Honest()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        NewObject(project, "Кадр 1", ObjectKinds.Image, hero.Id);
        NewObject(project, "Кадр 2", ObjectKinds.Image, hero.Id);

        var heroes = _f.Objects.List(project, kinds: [ObjectKinds.Character]);

        Assert.Equal(2, Assert.Single(heroes).ChildCount);
    }

    // ---------- 3. адреса превью ----------

    /// <summary>Свой файл объекта — картинка: адрес строится на эндпойнт ПАПКИ ПРОЕКТА,
    /// путь остаётся относительным (на соседнем сервере это будет его копия файла).</summary>
    [Fact]
    public void The_Own_File_Of_An_Object_Is_Its_Preview()
    {
        var project = NewProject();
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, path: "art/hero.png");

        var pics = ObjectPics.Build([frame], project);

        var url = Assert.Contains(frame.Id, pics);
        Assert.Contains("api/files/project", url);
        Assert.Contains(Uri.EscapeDataString("art/hero.png"), url);
        Assert.Contains(Uri.EscapeDataString(project), url);
    }

    /// <summary>Удалённый адрес отдаётся как есть: за ним чужой сервер, и гонять его через
    /// себя ради превью незачем. Не картинка — превью нет вовсе.</summary>
    [Fact]
    public void A_Remote_Address_Is_Taken_As_Is_And_A_Non_Image_Has_No_Preview()
    {
        var project = NewProject();
        var remote = NewObject(project, "Обложка", ObjectKinds.Image,
            path: "https://example.org/a/hero.jpg");
        var doc = NewObject(project, "Сценарий", ObjectKinds.File, path: "docs/plan.md");
        var empty = NewObject(project, "Стиль", ObjectKinds.Style);

        var pics = ObjectPics.Build([remote, doc, empty], project);

        Assert.Equal("https://example.org/a/hero.jpg", pics[remote.Id]);
        Assert.DoesNotContain(doc.Id, pics);
        Assert.DoesNotContain(empty.Id, pics);
    }

    /// <summary>
    /// У ПЕРСОНАЖА своего файла обычно нет — его лицо лежит у эталонных кадров-детей.
    /// Берётся ПЕРВЫЙ по номеру, и обход идёт ВГЛУБЬ: с T-274 кадры лежат ещё уровнем ниже,
    /// внутри датасета, и правило «только прямые дети» оставило бы персонажа без превью.
    /// </summary>
    [Fact]
    public void A_Character_Borrows_The_First_Picture_Of_Its_Inner_List()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var dataset = NewObject(project, "Датасет", ObjectKinds.Dataset, hero.Id);
        var first = NewObject(project, "Кадр 1", ObjectKinds.Image, dataset.Id, "lora/1.png");
        NewObject(project, "Кадр 2", ObjectKinds.Image, dataset.Id, "lora/2.png");

        var pics = ObjectPics.Build(_f.Objects.List(project), project);

        Assert.Equal(pics[first.Id], pics[hero.Id]);
        Assert.Equal(pics[first.Id], pics[dataset.Id]);
        Assert.Contains(Uri.EscapeDataString("lora/1.png"), pics[hero.Id]);
    }

    /// <summary>Свой файл сильнее детского: у объекта с картинкой берётся его собственная.</summary>
    [Fact]
    public void The_Own_Picture_Wins_Over_The_Childs_One()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой", path: "art/hero.png");
        NewObject(project, "Кадр", ObjectKinds.Image, hero.Id, "lora/1.png");

        var pics = ObjectPics.Build(_f.Objects.List(project), project);

        Assert.Contains(Uri.EscapeDataString("art/hero.png"), pics[hero.Id]);
    }

    /// <summary>
    /// ЗАМКНУТАЯ ССЫЛКА не вешает расчёт. Хранилище цикла не пропускает, но список рисуется
    /// и по данным, приехавшим репликацией со старой версии, — та же оговорка, что у
    /// раскладки строк (ObjectRows).
    /// </summary>
    [Fact]
    public void A_Cycle_In_The_Data_Does_Not_Hang_The_Preview()
    {
        var a = new ObjectItem { Id = "a", ParentId = "b", Name = "A", DisplayId = "OBJ-1" };
        var b = new ObjectItem { Id = "b", ParentId = "a", Name = "B", DisplayId = "OBJ-2" };

        var pics = ObjectPics.Build([a, b], "p1");

        Assert.Empty(pics);
    }

    // ---------- 4. разметка и словари ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    private static string Ui(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components", file));

    /// <summary>Видов списка стало ЧЕТЫРЕ: прежняя таблица называется краткой, а «таблица» —
    /// это новая, с колонкой превью.</summary>
    [Fact]
    public void The_List_Has_A_Table_With_Previews_Next_To_The_Brief_One()
    {
        var markup = Ui("ObjectsView.razor");

        Assert.Contains("objects.view.table.short", markup);
        Assert.Contains("data-objects-view=\"tablefull\"", markup);
        Assert.Contains("data-objects-view=\"table\"", markup);
        Assert.Contains("<ObjectPic", markup);
        Assert.Contains("ObjectPics.Build", markup);
    }

    /// <summary>Отбор по видам есть и в списке (множественный), и в выборе объекта из
    /// редактора .md (одиночный).</summary>
    [Fact]
    public void Both_Lists_Filter_By_Kind()
    {
        var view = Ui("ObjectsView.razor");
        var picker = Ui("ObjectPickerDialog.razor");

        Assert.Contains("data-objects-filter-kinds", view);
        Assert.Contains("MultiSelection=\"true\"", view);
        Assert.Contains("objects.filter.kinds", view);

        Assert.Contains("data-objpick-kind", picker);
        Assert.Contains("<ObjectPic", picker);
    }

    /// <summary>
    /// ЗАГРУЗКА ПРЕВЬЮ АСИНХРОННАЯ и это главное свойство: список не ждёт ни картинок, ни
    /// запросов о них. Ленивую загрузку делает сам браузер (loading="lazy"), а пропавший
    /// файл гасит ai2p.initPics — иначе в строке торчал бы битый значок.
    /// </summary>
    [Fact]
    public void The_Previews_Are_Loaded_By_The_Browser_Itself()
    {
        var pic = Ui("ObjectPic.razor");
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "wwwroot", "ai2p.js"));

        Assert.Contains("loading=\"lazy\"", pic);
        Assert.Contains("decoding=\"async\"", pic);
        Assert.Contains("data-ai2p-pic", pic);
        Assert.Contains("function initPics", js);
        Assert.Contains("initPics: initPics", js);
    }

    /// <summary>Отбор по видам доезжает до сервера параметром запроса, а не отбирается
    /// в браузере: список объектов проекта собирает тот, кто его читает.</summary>
    [Fact]
    public void The_Kind_Filter_Goes_To_The_Server()
    {
        var api = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.Server", "Api",
            "ApiEndpoints.cs"));
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Services",
            "ApiClient.cs"));

        Assert.Contains("string? kinds", api);
        Assert.Contains("ObjectKinds.Parse(kinds)", api);
        Assert.Contains("kinds=", client);
    }

    /// <summary>Новые тексты — в ОБОИХ словарях (правило гл. 9): иначе английский интерфейс
    /// показал бы сам ключ.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        var ru = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "ru.json"));
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "en.json"));

        foreach (var key in new[] { "objects.view.table.short", "objects.filter.kinds" })
        {
            Assert.Contains("\"" + key + "\"", ru);
            Assert.Contains("\"" + key + "\"", en);
        }
    }
}
