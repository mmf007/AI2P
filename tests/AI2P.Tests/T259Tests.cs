using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-259 (версия 1.91): ОБЪЕКТЫ ПРОЕКТА — хранение образа медиа-персонажа.
///
/// Что проверяется:
/// <list type="number">
/// <item>ССЫЛКА <c>@obj:…</c> — обе формы (номер и название в скобках), неизвестная ссылка
/// остаётся в тексте, вложенная ссылка внутри паспорта не раскрывается;</item>
/// <item>ХРАНИЛИЩЕ — свой список у каждого проекта, тэги СВОЕЙ группой (отдельно от тэгов
/// задач), уникальность названия в проекте, иерархия и защита от цикла;</item>
/// <item>ПОДСТАНОВКА В ПРОМПТ — паспорт объекта и пути эталонных файлов ЕГО ДЕТЕЙ;</item>
/// <item>ПРЕДСТАВЛЕНИЯ — таблица (дети идут за родителем с отступом) и тэги;</item>
/// <item>РАЗМЕТКА — вкладка проекта, два вида, поля формы и задел LoRA (проверка по файлу,
/// приём T-209/T-217; поведение в браузере — живой проверкой).</item>
/// </list>
///
/// Чего здесь НЕТ: загрузки картинки объекта в саму модель. Медиа-коннектор сегодня умеет
/// подставлять в граф только текст (разбор T-251, п. 2.3.A) — передача входного изображения
/// в ComfyUI делается отдельно (T-258). Поэтому «загрузка объекта в модель» на этом черновике
/// означает подстановку его ПАСПОРТА и ПУТЕЙ файлов в промпт.
/// </summary>
public sealed class T259Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private string NewProject(string name = "Ролик") => _f.Projects.Create(name, null, null, null).Id;

    private ObjectItem NewObject(string projectId, string name,
        string kind = ObjectKinds.Character, string passport = "", string path = "",
        string? parentId = null, IEnumerable<string>? tags = null) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = name,
            Type = kind,
            Description = passport,
            PathOrUrl = path,
            Tags = tags?.ToList() ?? [],
        }, null);

    // ---------- 1. ссылка на объект (ядро) ----------

    /// <summary>Обе формы ссылки находятся: по номеру и по названию в квадратных скобках.
    /// Скобки нужны как раз потому, что в названии бывают пробелы.</summary>
    [Fact]
    public void Both_Forms_Of_A_Reference_Are_Found()
    {
        var found = ObjectRefs.Find("Крупный план @obj:OBJ-3 на фоне @obj:[Ночная улица], дождь");

        Assert.Equal(["OBJ-3", "Ночная улица"], found);
    }

    /// <summary>Знак препинания сразу после номера в ссылку НЕ входит: иначе «@obj:OBJ-3,»
    /// искало бы объект с запятой в номере и не находило бы ничего.</summary>
    [Fact]
    public void Punctuation_Right_After_The_Code_Is_Not_Part_Of_It()
    {
        Assert.Equal(["OBJ-3"], ObjectRefs.Find("@obj:OBJ-3, крупный план"));
        Assert.Equal(["OBJ-7"], ObjectRefs.Find("(@obj:OBJ-7)"));
    }

    /// <summary>Повтор одной ссылки в тексте отдаётся один раз — список нужен для показа
    /// «на кого ссылается задача», а не для подсчёта упоминаний.</summary>
    [Fact]
    public void A_Repeated_Reference_Is_Listed_Once()
    {
        Assert.Equal(["OBJ-1"], ObjectRefs.Find("@obj:OBJ-1 идёт, @obj:OBJ-1 садится"));
    }

    /// <summary>Текста без ссылок подстановка не касается вовсе (и не стоит ничего).</summary>
    [Fact]
    public void A_Text_Without_References_Is_Returned_As_Is()
    {
        const string text = "Обычное описание кадра без единой ссылки";

        Assert.Equal(text, ObjectRefs.Expand(text, _ => null));
        Assert.Empty(ObjectRefs.Find(text));
    }

    /// <summary>
    /// НЕИЗВЕСТНАЯ ССЫЛКА ОСТАЁТСЯ В ТЕКСТЕ. Молча стереть её нельзя: человек увидел бы
    /// правильный на вид промпт без персонажа и списал бы непохожий кадр на модель.
    /// </summary>
    [Fact]
    public void An_Unknown_Reference_Stays_In_The_Text()
    {
        Assert.Equal("кадр @obj:OBJ-99 крупно",
            ObjectRefs.Expand("кадр @obj:OBJ-99 крупно", _ => null));
    }

    /// <summary>Подстановка даёт название, вид, номер, дословный паспорт и пути файлов.</summary>
    [Fact]
    public void A_Card_Is_Substituted_With_The_Passport_And_The_Files()
    {
        var card = new ObjectRefs.ObjectCard("OBJ-3", "Герой Вася", "персонаж",
            "рыжий, шрам на левой щеке", ["refs/hero-1.png", "refs/hero-2.png"]);

        var text = ObjectRefs.Expand("Крупный план: @obj:OBJ-3", _ => card);

        Assert.Equal("Крупный план: Герой Вася (персонаж, OBJ-3)\n"
                     + "рыжий, шрам на левой щеке\nrefs/hero-1.png\nrefs/hero-2.png", text);
    }

    /// <summary>Ссылка ВНУТРИ паспорта остаётся ссылкой: иначе два объекта, сославшихся
    /// друг на друга, раскрывались бы бесконечно.</summary>
    [Fact]
    public void A_Reference_Inside_A_Passport_Is_Not_Expanded()
    {
        var card = new ObjectRefs.ObjectCard("OBJ-1", "Герой", "персонаж", "рядом @obj:OBJ-2", []);

        var text = ObjectRefs.Expand("@obj:OBJ-1", _ => card);

        Assert.Contains("@obj:OBJ-2", text);
    }

    // ---------- 2. хранилище ----------

    [Fact]
    public void An_Object_Is_Saved_And_Read_Back()
    {
        var project = NewProject();

        var hero = NewObject(project, "Герой Вася", passport: "рыжий, шрам",
            path: "refs/hero.png", tags: ["главный", "ролик-1"]);

        var loaded = _f.Objects.Get(hero.Id)!;
        Assert.Equal("Герой Вася", loaded.Name);
        Assert.Equal(ObjectKinds.Character, loaded.Type);
        Assert.Equal("рыжий, шрам", loaded.Description);
        Assert.Equal("refs/hero.png", loaded.PathOrUrl);
        Assert.Equal(["главный", "ролик-1"], loaded.Tags);
        Assert.StartsWith("OBJ-", loaded.DisplayId);
    }

    /// <summary>Список объектов СВОЙ У КАЖДОГО ПРОЕКТА: персонажи одного ролика в другом
    /// не показываются вовсе.</summary>
    [Fact]
    public void The_List_Is_Per_Project()
    {
        var first = NewProject("Первый");
        var second = NewProject("Второй");
        NewObject(first, "Герой Вася");
        NewObject(second, "Робот");

        Assert.Equal(["Герой Вася"], _f.Objects.List(first).Select(o => o.Name));
        Assert.Equal(["Робот"], _f.Objects.List(second).Select(o => o.Name));
    }

    /// <summary>Объект без проекта потерялся бы навсегда — общего списка объектов нет.</summary>
    [Fact]
    public void An_Object_Without_A_Project_Is_Rejected()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _f.Objects.Create(new ObjectItem { Name = "Ничей" }, null));

        Assert.Equal(Loc.T("msg.object.2"), error.Message);
    }

    [Fact]
    public void An_Unknown_Kind_Is_Rejected()
    {
        var project = NewProject();

        Assert.Throws<ArgumentException>(() =>
            _f.Objects.Create(new ObjectItem
            {
                ProjectId = project,
                Name = "Странный",
                Type = "робот-пылесос",
            }, null));
    }

    /// <summary>Название уникально в пределах проекта: по названию разрешается ссылка
    /// <c>@obj:[Герой Вася]</c>, и двух Вась в одном проекте быть не должно — иначе в промпт
    /// попадал бы случайный из них. В ДРУГОМ проекте такое же название законно.</summary>
    [Fact]
    public void The_Name_Is_Unique_Inside_The_Project_Only()
    {
        var first = NewProject("Первый");
        var second = NewProject("Второй");
        NewObject(first, "Герой Вася");

        Assert.Throws<ArgumentException>(() => NewObject(first, "герой вася"));
        NewObject(second, "Герой Вася");   // в другом проекте — можно
    }

    /// <summary>
    /// ТЭГИ ОБЪЕКТОВ — ОТДЕЛЬНАЯ ГРУППА. Тэг задачи не появляется среди тэгов объектов и
    /// наоборот: «персонаж» в списке тэгов задач был бы мусором.
    /// </summary>
    [Fact]
    public void Object_Tags_Are_A_Separate_Group_From_Task_Tags()
    {
        var project = NewProject();
        _f.Tasks.Create(new TaskItem { ProjectId = project, Title = "Кадр", Tags = ["сборка"] },
            "Кадр", "", null);
        NewObject(project, "Герой Вася", tags: ["персонаж"]);

        Assert.Equal(["персонаж"], _f.Objects.ListTags(project));
        Assert.Equal(["сборка"], _f.Tasks.ListTags());
    }

    /// <summary>Правка ЗАМЕНЯЕТ набор тэгов, а не дополняет его: снятый в форме тэг обязан
    /// исчезнуть, иначе снять его было бы нечем.</summary>
    [Fact]
    public void Editing_Replaces_The_Set_Of_Tags()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой Вася", tags: ["персонаж", "главный"]);

        hero.Tags = ["персонаж"];
        _f.Objects.Update(hero, null);

        Assert.Equal(["персонаж"], _f.Objects.Get(hero.Id)!.Tags);
        Assert.Equal(["персонаж"], _f.Objects.ListTags(project));
    }

    /// <summary>Отбор по тэгам — как у задач: несколько тэгов соединяются по ИЛИ.</summary>
    [Fact]
    public void The_Tag_Filter_Joins_Tags_By_Or()
    {
        var project = NewProject();
        NewObject(project, "Герой", tags: ["персонаж"]);
        NewObject(project, "Улица", kind: ObjectKinds.Location, tags: ["фон"]);
        NewObject(project, "Зонт", kind: ObjectKinds.Prop, tags: ["мелочь"]);

        var found = _f.Objects.List(project, ["персонаж", "фон"]).Select(o => o.Name).ToList();

        Assert.Equal(["Герой", "Улица"], found);
    }

    // ---------- 3. иерархия (внутренний список) ----------

    /// <summary>Эталонные кадры персонажа — это его дети; у родителя виден их счёт.</summary>
    [Fact]
    public void Children_Form_The_Inner_List_Of_An_Object()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой Вася");
        NewObject(project, "Вася анфас", ObjectKinds.Image, path: "refs/hero-1.png", parentId: hero.Id);
        NewObject(project, "Вася профиль", ObjectKinds.Image, path: "refs/hero-2.png", parentId: hero.Id);

        Assert.Equal(2, _f.Objects.Children(hero.Id).Count);
        Assert.Equal(2, _f.Objects.List(project).First(o => o.Id == hero.Id).ChildCount);
    }

    /// <summary>Родитель из ДРУГОГО проекта запрещён — иначе список проекта показывал бы
    /// чужих детей (то же правило, что у подзадач, T-201).</summary>
    [Fact]
    public void A_Parent_From_Another_Project_Is_Rejected()
    {
        var first = NewProject("Первый");
        var second = NewProject("Второй");
        var alien = NewObject(second, "Чужой герой");

        var error = Assert.Throws<ArgumentException>(() =>
            NewObject(first, "Кадр", ObjectKinds.Image, parentId: alien.Id));

        Assert.Equal(Loc.T("msg.object.9"), error.Message);
    }

    /// <summary>Замкнуть иерархию нельзя: «родитель сам себе предок» подвесил бы обход.</summary>
    [Fact]
    public void A_Cycle_In_The_Hierarchy_Is_Rejected()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        hero.ParentId = frame.Id;   // герой внутрь своего же кадра

        var error = Assert.Throws<ArgumentException>(() => _f.Objects.Update(hero, null));
        Assert.Equal(Loc.T("msg.object.7"), error.Message);
    }

    /// <summary>Объект с внутренним списком не удаляется: иначе его кадры остались бы
    /// в проекте без родителя и без способа их найти.</summary>
    [Fact]
    public void An_Object_With_Children_Is_Not_Deleted()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        Assert.Throws<ArgumentException>(() => _f.Objects.Delete(hero.Id, null));
        Assert.NotNull(_f.Objects.Get(hero.Id)!.DeletedAt is null ? "жив" : null);
    }

    // ---------- 4. подстановка в промпт ----------

    /// <summary>
    /// ГЛАВНОЕ: ссылка в описании превращается в дословный паспорт персонажа И в пути
    /// эталонных кадров — своего и всех детей. Ради этого объекты и заводились: медиа-модель
    /// получает промптом только описание задачи (разбор T-251).
    /// </summary>
    [Fact]
    public void A_Reference_Expands_To_The_Passport_And_All_Reference_Files()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой Вася", passport: "рыжий, шрам на левой щеке",
            path: "refs/hero.png");
        NewObject(project, "Вася профиль", ObjectKinds.Image, path: "refs/hero-2.png",
            parentId: hero.Id);

        var text = _f.Objects.Expand(project, $"Крупный план: @obj:{hero.DisplayId}, дождь",
            kind => ObjectKinds.Title(kind));

        Assert.Contains("Герой Вася", text);
        Assert.Contains("рыжий, шрам на левой щеке", text);
        Assert.Contains("refs/hero.png", text);
        Assert.Contains("refs/hero-2.png", text);
        Assert.Contains(", дождь", text);          // остаток описания на месте
        Assert.DoesNotContain("@obj:", text);
    }

    /// <summary>Вторая форма ссылки — по названию: так пишут руками.</summary>
    [Fact]
    public void A_Reference_By_Name_Works_Too()
    {
        var project = NewProject();
        NewObject(project, "Ночная улица", ObjectKinds.Location, passport: "мокрый асфальт, неон");

        var text = _f.Objects.Expand(project, "Фон: @obj:[ночная улица]", null);

        Assert.Contains("мокрый асфальт, неон", text);
    }

    /// <summary>Объект ЧУЖОГО проекта по ссылке не достаётся: ссылка живёт в пределах
    /// своего проекта, как и сам список объектов.</summary>
    [Fact]
    public void A_Reference_Does_Not_Reach_Into_Another_Project()
    {
        var first = NewProject("Первый");
        var second = NewProject("Второй");
        var alien = NewObject(second, "Чужой герой", passport: "секретная внешность");

        var text = _f.Objects.Expand(first, $"@obj:{alien.DisplayId}", null);

        Assert.DoesNotContain("секретная внешность", text);
        Assert.Contains("@obj:", text);   // ссылка осталась как есть
    }

    /// <summary>Правка паспорта действует на СЛЕДУЮЩЕЕ ЖЕ задание: подстановка идёт при
    /// каждом запуске, и переписывать описания задач не нужно.</summary>
    [Fact]
    public void Editing_The_Passport_Changes_The_Next_Prompt()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой", passport: "рыжий");
        var marker = ObjectRefs.Marker(hero.DisplayId);

        hero.Description = "седой";
        _f.Objects.Update(hero, null);

        Assert.Contains("седой", _f.Objects.Expand(project, marker, null));
    }

    // ---------- 5. представления (логика UI) ----------

    /// <summary>Таблица показывает иерархию: ребёнок идёт сразу за родителем и с отступом.</summary>
    [Fact]
    public void The_Table_Puts_Children_Right_After_Their_Parent()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        NewObject(project, "Улица", ObjectKinds.Location);
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        var rows = ObjectRows.Build(_f.Objects.List(project));

        Assert.Equal(["Герой", "Кадр", "Улица"], rows.Select(r => r.Item.Name));
        Assert.Equal([0, 1, 0], rows.Select(r => r.Level));
        Assert.Equal(frame.Id, rows[1].Item.Id);
    }

    /// <summary>Сирота (родитель отсеян фильтром) не теряется — показывается верхним уровнем.
    /// Потерять строку из-за фильтра было бы хуже всего.</summary>
    [Fact]
    public void An_Orphan_Row_Is_Shown_At_The_Top_Level()
    {
        var rows = ObjectRows.Build([
            new ObjectItem { Id = "child", DisplayId = "OBJ-2", Name = "Кадр", ParentId = "missing" },
        ]);

        Assert.Single(rows);
        Assert.Equal(0, rows[0].Level);
    }

    /// <summary>Представление «тэги»: объект с двумя тэгами попадает в обе категории,
    /// объект без тэгов — в категорию «без тэгов» В КОНЦЕ списка.</summary>
    [Fact]
    public void The_Tags_View_Puts_An_Object_Into_Every_Category()
    {
        var groups = ObjectTagGroups.Build([
            new ObjectItem { Name = "Герой", Tags = ["персонаж", "главный"] },
            new ObjectItem { Name = "Зонт" },
        ]);

        Assert.Equal(["главный", "персонаж", ObjectTagGroups.NoTag], groups.Select(g => g.Tag));
        Assert.Equal("Герой", groups[0].Items[0].Name);
        Assert.Equal("Герой", groups[1].Items[0].Name);
        Assert.Equal("Зонт", groups[^1].Items[0].Name);
    }

    // ---------- 6. разметка и словари ----------

    private static string Ui(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    /// <summary>Объекты живут ВКЛАДКОЙ КАРТОЧКИ ПРОЕКТА: список свой у каждого проекта,
    /// общего вида экшен-бара у них нет намеренно.</summary>
    [Fact]
    public void The_Objects_Tab_Is_On_The_Project_Card()
    {
        var markup = Ui("ProjectCardView.razor");

        Assert.Contains("projects.tab.objects", markup);
        Assert.Contains("<ObjectsView ProjectId=", markup);
    }

    /// <summary>Представлений было два — таблица и тэги; с T-266 к ним добавилась
    /// иерархия (её проверяет T266Tests). Здесь сторожатся два исходных.</summary>
    [Fact]
    public void The_List_Has_The_Table_And_The_Tags_Views()
    {
        var markup = Ui("ObjectsView.razor");

        Assert.Contains("objects.view.table", markup);
        Assert.Contains("objects.view.tags", markup);
        Assert.Contains("data-objects-view=\"table\"", markup);
        Assert.Contains("data-objects-view=\"tags\"", markup);
        // все категории представления «тэги» при загрузке свёрнуты (T-217, T-222)
        Assert.Contains("data-object-open=", markup);
    }

    /// <summary>Форма объекта: название, вид, родитель, тэги, путь, паспорт — и раздел LoRA.
    /// Трёх кнопок-заглушек (создать, обучить, хранилище) с T-12-S1 нет: за ними стоит один
    /// вход в редактор LoRA, где эти три дела и делаются.</summary>
    [Fact]
    public void The_Object_Form_Has_The_Passport_And_The_Lora_Groundwork()
    {
        var markup = Ui("ObjectDialog.razor");

        foreach (var probe in new[]
                 {
                     "data-object-name", "data-object-kind", "data-object-parent",
                     "data-object-tags", "data-object-path", "data-object-passport",
                     "data-object-lora-setup",
                     "data-object-lora-path", "data-object-lora-status",
                 })
        {
            Assert.Contains(probe, markup);
        }
    }

    /// <summary>Тексты нового раздела есть в ОБОИХ словарях (правило гл. 9): состав ключей
    /// обязан сходиться, иначе английский интерфейс покажет сам ключ.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        var ru = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "ru.json"));
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "en.json"));

        foreach (var key in new[]
                 {
                     "projects.tab.objects", "objects.title", "objects.passport", "objects.link",
                     // с T-12-S1 вход в работу с адаптером один — «настройка LoRA»
                     "objects.prompt", "objects.lora.setup", "objects.lora.status",
                     "objects.lora.path", "object.kind.character", "object.kind.location",
                     "msg.object.1", "msg.object.10",
                 })
        {
            Assert.Contains($"\"{key}\"", ru);
            Assert.Contains($"\"{key}\"", en);
        }
    }

    /// <summary>Название вида берётся из словаря, а неизвестный вид остаётся кодом —
    /// придумывать за него перевод не за чем.</summary>
    [Fact]
    public void A_Kind_Is_Named_By_The_Dictionary()
    {
        Assert.Equal("персонаж", ObjectKinds.Title(ObjectKinds.Character, "ru"));
        Assert.Equal("character", ObjectKinds.Title(ObjectKinds.Character, "en"));
        Assert.Equal("робот-пылесос", ObjectKinds.Title("робот-пылесос", "ru"));
    }

    /// <summary>Объекты и их тэги едут партнёру обычным журналом изменений (ТЗ гл. 6):
    /// без строки в списке реплицируемых таблиц персонажи остались бы на одном сервере.</summary>
    [Fact]
    public void Objects_And_Their_Tags_Are_Replicated()
    {
        Assert.Contains("objects", ChangeLog.OrgTables);
        Assert.Contains("object_tags", ChangeLog.OrgTables);
    }
}
