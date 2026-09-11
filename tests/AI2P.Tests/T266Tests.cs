using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-266 (версия 1.94): ПРЕДСТАВЛЕНИЕ «ИЕРАРХИЯ» В СПИСКЕ ОБЪЕКТОВ ПРОЕКТА — то же, что у
/// задач: дерево со сворачиванием узлов и сменой подчинённости перетаскиванием.
///
/// Что проверяется:
/// <list type="number">
/// <item>ПЕРЕНОС (<c>ObjectService.ChangeParent</c>) — объект становится внутренним у
/// другого и снова корневым; отказы: сам себе родитель, собственный потомок, чужой
/// проект, несуществующий объект;</item>
/// <item>ПЕРЕНОС НЕ ТРОГАЕТ ОСТАЛЬНОЕ — паспорт, тэги, вид, состояние LoRA и номер
/// остаются прежними (ради этого он и сделан отдельным вызовом, а не PUT формы);</item>
/// <item>РАСКЛАДКА (<c>ObjectRows</c>) — свёрнутый узел прячет ВСЁ своё поддерево и не
/// выносит внуков на верхний уровень, признак «есть внутренний список» верен;</item>
/// <item>РАЗМЕТКА — третий вид в списке, дерево с ручкой переноса и зонами «в корень»,
/// общий движок ai2p.initTreeDnd; поведение в браузере — живой проверкой.</item>
/// </list>
///
/// Чего здесь НЕТ: самого перетаскивания. Оно живёт в браузере (мышь — нативный HTML5-drag,
/// палец — свой жест ai2p.initTouchDnd) и тестами не проверяется в принципе — только живым
/// прогоном по CDP (наука T-176, T-193, T-4-S1).
/// </summary>
public sealed class T266Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private string NewProject(string name = "Ролик") => _f.Projects.Create(name, null, null, null).Id;

    private ObjectItem NewObject(string projectId, string name,
        string kind = ObjectKinds.Character, string? parentId = null,
        string passport = "", IEnumerable<string>? tags = null) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = name,
            Type = kind,
            Description = passport,
            Tags = tags?.ToList() ?? [],
        }, null);

    // ---------- 1. перенос по иерархии ----------

    /// <summary>Объект, брошенный на другой, становится его внутренним объектом —
    /// эталонный кадр так попадает к своему персонажу.</summary>
    [Fact]
    public void An_Object_Dropped_Onto_Another_Becomes_Its_Child()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image);

        var moved = _f.Objects.ChangeParent(frame.Id, hero.Id, null);

        Assert.Equal(hero.Id, moved.ParentId);
        Assert.Equal(hero.Id, _f.Objects.Get(frame.Id)!.ParentId);
        Assert.Equal(1, _f.Objects.Get(hero.Id)!.ChildCount);
    }

    /// <summary>Полоса «в корень» снимает родителя. Пустая строка — то же, что null:
    /// из разметки зона сброса приходит именно пустой строкой (data-tree-drop="").</summary>
    [Fact]
    public void Dropping_On_The_Root_Bar_Clears_The_Parent()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        var moved = _f.Objects.ChangeParent(frame.Id, "", null);

        Assert.Null(moved.ParentId);
        Assert.Equal(0, _f.Objects.Get(hero.Id)!.ChildCount);
    }

    /// <summary>Объект нельзя сделать родителем самому себе: строка бы выпала из дерева,
    /// а обход её поддерева зациклился бы.</summary>
    [Fact]
    public void An_Object_Cannot_Become_Its_Own_Parent()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");

        Assert.Throws<ArgumentException>(() => _f.Objects.ChangeParent(hero.Id, hero.Id, null));
        Assert.Null(_f.Objects.Get(hero.Id)!.ParentId);
    }

    /// <summary>Перенос ПОД СОБСТВЕННОГО ПОТОМКА отвергается — иначе ветка отвалилась бы
    /// от дерева целиком и список стал бы короче выборки.</summary>
    [Fact]
    public void An_Object_Cannot_Be_Moved_Under_Its_Own_Descendant()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);
        var detail = NewObject(project, "Деталь", ObjectKinds.Image, parentId: frame.Id);

        Assert.Throws<ArgumentException>(() => _f.Objects.ChangeParent(hero.Id, detail.Id, null));
        Assert.Null(_f.Objects.Get(hero.Id)!.ParentId);
    }

    /// <summary>Родитель из ЧУЖОГО проекта отвергается: список объектов свой у каждого
    /// проекта, и такой ребёнок пропал бы из обоих списков.</summary>
    [Fact]
    public void The_New_Parent_Must_Live_In_The_Same_Project()
    {
        var first = NewProject("Первый");
        var second = NewProject("Второй");
        var mine = NewObject(first, "Герой");
        var alien = NewObject(second, "Чужой герой");

        Assert.Throws<ArgumentException>(() => _f.Objects.ChangeParent(mine.Id, alien.Id, null));
    }

    /// <summary>Несуществующий объект переносить нечего — внятный отказ, а не тишина
    /// (перетаскивание идёт по давно прочитанному списку, строку могли удалить).</summary>
    [Fact]
    public void Moving_A_Missing_Object_Is_Refused()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");

        Assert.Throws<ArgumentException>(() => _f.Objects.ChangeParent("no-such-id", hero.Id, null));
    }

    /// <summary>Сброс туда, где объект и так стоит, ошибкой не считается: промах мышью в
    /// собственного родителя — обычное дело, и отказ на ровном месте пугал бы зря.</summary>
    [Fact]
    public void Dropping_Onto_The_Current_Parent_Changes_Nothing()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        var moved = _f.Objects.ChangeParent(frame.Id, hero.Id, null);

        Assert.Equal(hero.Id, moved.ParentId);
    }

    /// <summary>
    /// ГЛАВНОЕ отличие переноса от правки формы: он меняет РОВНО родителя. Паспорт, тэги,
    /// вид, состояние LoRA, номер и название остаются прежними — иначе перетаскивание
    /// переписывало бы объект тем, что лежало в давно прочитанном списке.
    /// </summary>
    [Fact]
    public void Moving_Keeps_Everything_Except_The_Parent()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project,
            Name = "Кадр",
            Type = ObjectKinds.Image,
            Description = "рыжий, шрам на левой скуле",
            PathOrUrl = "refs/hero.png",
            LoraStatus = LoraStates.Ready,
            LoraPath = "loras/hero.safetensors",
            Tags = ["эталон", "герой"],
        }, null);

        var moved = _f.Objects.ChangeParent(frame.Id, hero.Id, null);

        Assert.Equal(hero.Id, moved.ParentId);
        Assert.Equal(frame.DisplayId, moved.DisplayId);
        Assert.Equal("Кадр", moved.Name);
        Assert.Equal("рыжий, шрам на левой скуле", moved.Description);
        Assert.Equal("refs/hero.png", moved.PathOrUrl);
        Assert.Equal(LoraStates.Ready, moved.LoraStatus);
        Assert.Equal("loras/hero.safetensors", moved.LoraPath);
        Assert.Equal(["герой", "эталон"], moved.Tags.OrderBy(t => t));
    }

    /// <summary>Перенос попадает в журнал работ (как правка объекта): «кто и когда увёл
    /// эталонный кадр к другому персонажу» должно быть видно.</summary>
    [Fact]
    public void Moving_Is_Written_To_The_History()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image);
        var before = _f.Events.Query(eventType: EventTypes.ObjectUpdated).Count;

        _f.Objects.ChangeParent(frame.Id, hero.Id, null);

        Assert.Equal(before + 1, _f.Events.Query(eventType: EventTypes.ObjectUpdated).Count);
    }

    /// <summary>Перенос ЧЕРЕЗ ветку: объект вместе со своим внутренним списком уходит к
    /// новому родителю целиком — дети едут за родителем, их трогать не надо.</summary>
    [Fact]
    public void Children_Follow_Their_Parent()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var street = NewObject(project, "Улица", ObjectKinds.Location);
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);
        var detail = NewObject(project, "Деталь", ObjectKinds.Image, parentId: frame.Id);

        _f.Objects.ChangeParent(frame.Id, street.Id, null);

        var rows = ObjectRows.Build(_f.Objects.List(project));
        Assert.Equal(["Герой", "Улица", "Кадр", "Деталь"], rows.Select(r => r.Item.Name));
        Assert.Equal([0, 0, 1, 2], rows.Select(r => r.Level));
        Assert.Equal(frame.Id, _f.Objects.Get(detail.Id)!.ParentId);
    }

    // ---------- 2. раскладка дерева (логика представления) ----------

    /// <summary>Свёрнутый узел прячет ВСЁ поддерево, а не только первый уровень: внук
    /// свёрнутого персонажа не должен всплыть отдельной строкой.</summary>
    [Fact]
    public void A_Collapsed_Node_Hides_Its_Whole_Subtree()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        var frame = NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);
        NewObject(project, "Деталь", ObjectKinds.Image, parentId: frame.Id);
        NewObject(project, "Улица", ObjectKinds.Location);

        var rows = ObjectRows.Build(_f.Objects.List(project),
            new HashSet<string>(StringComparer.Ordinal) { hero.Id });

        Assert.Equal(["Герой", "Улица"], rows.Select(r => r.Item.Name));
    }

    /// <summary>Признак «есть внутренний список» — по нему рисуется стрелка сворачивания.
    /// У свёрнутого узла он остаётся истинным (иначе стрелка исчезала бы вместе с детьми,
    /// и развернуть узел было бы нечем).</summary>
    [Fact]
    public void The_Has_Children_Flag_Survives_Collapsing()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);

        var open = ObjectRows.Build(_f.Objects.List(project));
        var closed = ObjectRows.Build(_f.Objects.List(project),
            new HashSet<string>(StringComparer.Ordinal) { hero.Id });

        Assert.True(open[0].HasChildren);
        Assert.False(open[1].HasChildren);
        Assert.True(closed.Single().HasChildren);
    }

    /// <summary>Без набора свёрнутых раскладка прежняя — таблица объектов (T-259) считается
    /// тем же кодом и от появления иерархии не изменилась.</summary>
    [Fact]
    public void Without_Collapsing_The_Layout_Is_The_Old_One()
    {
        var project = NewProject();
        var hero = NewObject(project, "Герой");
        NewObject(project, "Кадр", ObjectKinds.Image, parentId: hero.Id);
        NewObject(project, "Улица", ObjectKinds.Location);

        var rows = ObjectRows.Build(_f.Objects.List(project));

        Assert.Equal(["Герой", "Кадр", "Улица"], rows.Select(r => r.Item.Name));
        Assert.Equal([0, 1, 0], rows.Select(r => r.Level));
    }

    // ---------- 3. разметка и словари ----------

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

    /// <summary>У списка объектов появился ТРЕТИЙ вид — иерархия, и кнопки «свернуть/
    /// развернуть все» показываются только в нём.</summary>
    [Fact]
    public void The_List_Has_A_Third_View()
    {
        var markup = Ui("ObjectsView.razor");

        Assert.Contains("objects.view.tree", markup);
        Assert.Contains("<ObjectTreeView", markup);
        Assert.Contains("data-objects-collapse", markup);
        Assert.Contains("data-objects-expand", markup);
        Assert.Contains("_view == \"tree\"", markup);
    }

    /// <summary>
    /// Дерево объектов сделано ТЕМ ЖЕ движком, что дерево задач: те же атрибуты разметки и
    /// тот же ai2p.initTreeDnd. Это не косметика — от общего движка объектам достаются
    /// перенос пальцем, автопрокрутка у краёв и липкая зона «в корень», каждая из которых
    /// у задач стоила отдельного захода (T-176, T-193, T-4-S1).
    /// </summary>
    [Fact]
    public void The_Object_Tree_Uses_The_Same_Drag_Engine_As_Tasks()
    {
        var markup = Ui("ObjectTreeView.razor");

        foreach (var probe in new[]
                 {
                     "data-tree-task", "data-tree-drop", "data-tree-move", "data-tree-grip",
                     "ai2p-tree-node", "ai2p-tree-root-top", "ai2p.initTreeDnd",
                     "OnTreeDropAsync", "MoveObjectAsync", "data-object-node",
                 })
        {
            Assert.Contains(probe, markup);
        }
    }

    /// <summary>Перенос идёт СВОИМ вызовом API (одно поле), а не сохранением всей записи.</summary>
    [Fact]
    public void The_Move_Has_Its_Own_Endpoint()
    {
        var api = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.Server", "Api",
            "ApiEndpoints.cs"));

        Assert.Contains("\"/objects/{id}/parent\"", api);
        Assert.Contains("Objects().ChangeParent", api);
    }

    /// <summary>Тексты нового вида есть в ОБОИХ словарях (правило гл. 9): иначе английский
    /// интерфейс показал бы сам ключ.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        var ru = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "ru.json"));
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "en.json"));

        foreach (var key in new[]
                 {
                     "objects.view.tree", "objects.tree.dnd.root", "objects.tree.dnd.hint",
                     "objects.tree.dnd.grip", "objects.tree.dnd.moved",
                     "objects.tree.collapseAll", "objects.tree.expandAll",
                 })
        {
            Assert.Contains("\"" + key + "\"", ru);
            Assert.Contains("\"" + key + "\"", en);
        }
    }
}
