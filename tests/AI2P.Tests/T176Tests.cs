using System.Text.Json;
using System.Text.RegularExpressions;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-176: ДРАГ В ИЕРАРХИИ ЗАДАЧ — «не переносит задачу в корень, просто ничего не делает».
///
/// Сам перенос был исправен (см. <see cref="Todo37TaskMoveTests"/> и проверки ниже): ломалась
/// ДОСТУПНОСТЬ зоны сброса. Полоса «в корень» была одна и стояла в самом конце дерева, а
/// вверху висела похожая на неё подсказка, зоной сброса не бывшая. В списке из 555 задач
/// конец дерева — на 16 000 px ниже видимой части фрейма: дотащить туда задачу мышью нельзя,
/// и сброс «в корень» выглядел как «ничего не происходит».
///
/// Здесь проверяется то, что проверяется без браузера: разметка дерева, правила стилей и
/// код JS. Само перетаскивание настоящей мышью — <c>test/t176/ui176.py</c> (headless Chrome
/// по CDP), проверка серверной части — ниже, тестами на <c>ChangeParent</c>.
/// </summary>
public sealed class T176Tests
{
    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string Ui(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "AI2P_app", "src", "AI2P.UI", .. parts]));

    private static string Tree() => Ui("Components", "TaskTreeView.razor");

    private static string Home() => Ui("Pages", "Home.razor");

    private static string Js() => Ui("wwwroot", "ai2p.js");

    /// <summary>Тело CSS-правила по его селектору (до первой закрывающей скобки).</summary>
    private static string Rule(string css, string selector)
    {
        var at = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(at > 0, $"нет правила {selector}");
        var body = css[(at + selector.Length)..];
        return body[..body.IndexOf('}')];
    }

    // --- 1. зона «в корень» вверху дерева, и она же — подсказка ---

    [Fact]
    public void Root_Drop_Zone_Is_Drawn_Before_The_Task_Rows()
    {
        var tree = Tree();
        var top = tree.IndexOf("ai2p-tree-root-top", StringComparison.Ordinal);
        var rows = tree.IndexOf("@foreach (var (task, depth, hasChildren) in Flatten())",
            StringComparison.Ordinal);
        Assert.True(top > 0, "нет верхней полосы «в корень»");
        Assert.True(rows > top, "полоса «в корень» должна рисоваться ДО списка задач");
    }

    [Fact]
    public void Both_Root_Zones_Are_Drop_Targets_With_Empty_Parent()
    {
        var tree = Tree();
        // зона сброса — data-tree-drop; пустое значение означает «в корень» (todo37)
        var zones = Regex.Matches(tree, @"class=""ai2p-tree-root[^""]*"" data-tree-drop="""" data-tree-root=""(top|bottom)""");
        Assert.Equal(2, zones.Count);
        Assert.Equal(["top", "bottom"], zones.Select(m => m.Groups[1].Value).ToArray());
    }

    [Fact]
    public void Hint_Lives_Inside_The_Top_Root_Zone()
    {
        // подсказка была отдельной полосой вверху и на зону сброса только ПОХОДИЛА;
        // теперь она внутри самой зоны — куда написано тащить, туда и бросают
        var tree = Tree();
        var top = tree.IndexOf("ai2p-tree-root-top", StringComparison.Ordinal);
        var hint = tree.IndexOf("tasks.tree.dnd.hint", StringComparison.Ordinal);
        Assert.True(top > 0 && hint > top, "подсказка должна быть внутри верхней полосы");
        var block = tree[top..(top + 500)];
        Assert.Contains("tasks.tree.dnd.root", block, StringComparison.Ordinal);
        Assert.EndsWith("</div>", block[..(block.IndexOf("</div>", StringComparison.Ordinal) + 6)],
            StringComparison.Ordinal);
        // подсказка и заголовок «В корень» — в одном элементе
        Assert.True(block.IndexOf("tasks.tree.dnd.hint", StringComparison.Ordinal)
                    < block.IndexOf("</div>", StringComparison.Ordinal),
            "подсказка вынесена из полосы «в корень»");
        // в САМОЙ полосе значка «ручка перетаскивания» быть не должно: с ним прежняя
        // фальшивая подсказка и была похожа на зону сброса. У строк дерева ручка своя,
        // настоящая (data-tree-grip, T-4-S1) — она к этому запрету не относится
        Assert.DoesNotContain("Icons.Material.Filled.DragIndicator", block, StringComparison.Ordinal);
    }

    // --- 2. полоса остаётся на виду и не просвечивает ---

    [Fact]
    public void Top_Root_Zone_Is_Sticky()
    {
        var rule = Rule(Home(), ".ai2p-tree-root-top");
        Assert.Contains("position: sticky", rule, StringComparison.Ordinal);
        Assert.Contains("top: 0", rule, StringComparison.Ordinal);
        // липкая полоса лежит ПОВЕРХ уезжающих под неё строк
        Assert.Contains("z-index", rule, StringComparison.Ordinal);
        Assert.Contains("background: var(--mud-palette-background)", rule, StringComparison.Ordinal);
        // приглушена цветом, а не прозрачностью: сквозь полупрозрачную полосу просвечивали
        // бы уезжающие строки списка
        Assert.Contains("opacity: 1", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_Zones_Are_Highlighted_While_A_Task_Is_Dragged()
    {
        var home = Home();
        var rule = Rule(home, ".ai2p-tree-dragging .ai2p-tree-root");
        Assert.Contains("border-style: solid", rule, StringComparison.Ordinal);
        Assert.Contains("var(--mud-palette-primary)", rule, StringComparison.Ordinal);
        // подсветка зоны под курсором объявлена ПОЗЖЕ — иначе её перебьёт общая подсветка
        Assert.True(home.IndexOf(".ai2p-tree-dragging .ai2p-tree-root {", StringComparison.Ordinal)
                    < home.IndexOf(".ai2p-tree-root.ai2p-tree-over {", StringComparison.Ordinal),
            "правило наведённой зоны должно идти после общего");
    }

    // --- 3. JS: пометка перетаскивания и автопрокрутка списка ---

    [Fact]
    public void Js_Marks_The_Tree_While_Dragging()
    {
        var js = Js();
        Assert.Contains("container.classList.add('ai2p-tree-dragging')", js, StringComparison.Ordinal);
        Assert.Contains("container.classList.remove('ai2p-tree-dragging')", js, StringComparison.Ordinal);
        // сброс гасит пометку сам: dragend после drop приходит не всегда.
        // Искать от initTreeDnd: такой же слушатель есть у закладок (initTabDnd) выше
        var tree = js.IndexOf("function initTreeDnd", StringComparison.Ordinal);
        Assert.True(tree > 0);
        var drop = js.IndexOf("container.addEventListener('drop'", tree, StringComparison.Ordinal);
        Assert.True(drop > tree);
        Assert.Contains("dragEnd();", js[drop..(drop + 400)], StringComparison.Ordinal);
    }

    [Fact]
    public void Js_Scrolls_The_List_While_Dragging_Near_The_Edges()
    {
        var js = Js();
        Assert.Contains("TreeScrollEdge", js, StringComparison.Ordinal);
        Assert.Contains("TreeScrollStep", js, StringComparison.Ordinal);
        // прокрутка идёт по таймеру: событий dragover при неподвижной мыши почти нет
        Assert.Contains("setInterval(autoScrollStep", js, StringComparison.Ordinal);
        Assert.Contains("clearInterval(scrollTimer)", js, StringComparison.Ordinal);
        // мышь слушается на всём документе: над пустым местом фрейма dragover контейнера
        // уже не приходит, а прокрутка нужна и там
        Assert.Contains("document.addEventListener('dragover', trackPointer)", js,
            StringComparison.Ordinal);
        Assert.Contains("document.removeEventListener('dragover', trackPointer)", js,
            StringComparison.Ordinal);
        // верхний край отсчитывается от НИЗА липкой полосы: иначе, подводя к ней задачу,
        // список уезжал бы вверх и сбросить в корень стало бы нельзя
        Assert.Contains(".ai2p-tree-root-top", js, StringComparison.Ordinal);
        Assert.Contains("sticky.getBoundingClientRect().bottom", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Js_Finds_The_Scrollable_Ancestor_Of_The_Tree()
    {
        // дерево живёт и во фрейме представления, и в панели эксплорера — прокручиваемый
        // предок у них разный, а страница целиком годится как запасной вариант
        var js = Js();
        Assert.Contains("function scroller()", js, StringComparison.Ordinal);
        Assert.Contains("scrollHeight > node.clientHeight", js, StringComparison.Ordinal);
        Assert.Contains("document.scrollingElement", js, StringComparison.Ordinal);
    }

    // --- 4. подписи ---

    [Fact]
    public void Drag_Captions_Exist_In_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            foreach (var key in new[] { "tasks.tree.dnd.root", "tasks.tree.dnd.hint",
                                        "tasks.tree.dnd.moved" })
            {
                Assert.True(map.TryGetValue(key, out var text) && text.Length > 0,
                    $"в {lang}.json нет подписи {key}");
            }
        }
    }
}

/// <summary>
/// T-176: серверная часть переноса «в корень» — сама она была исправна, и это закреплено
/// тестами: пустая строка родителя (именно её шлёт зона «в корень») означает «корень», а
/// повторный сброс уже корневой задачи ничего не ломает.
/// </summary>
public sealed class T176MoveToRootTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem NewTask(string title, string? projectId, string? parentId = null) =>
        _f.Tasks.Create(new TaskItem { ProjectId = projectId, ParentId = parentId, Title = title },
            "", "", null);

    [Fact]
    public void Empty_Parent_Means_Root()
    {
        // зона «в корень» — это data-tree-drop="", и до сервера доезжает пустая строка
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = NewTask("Родитель", project.Id);
        var task = NewTask("Задача", project.Id, parent.Id);

        var moved = _f.Tasks.ChangeParent(task.Id, "", null);

        Assert.Null(moved.ParentId);
        Assert.Null(_f.Tasks.Get(task.Id)!.ParentId);
    }

    [Fact]
    public void Root_Task_Dropped_To_Root_Again_Stays_Root()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var task = NewTask("Задача", project.Id);

        var moved = _f.Tasks.ChangeParent(task.Id, "", null);

        Assert.Null(moved.ParentId);
        Assert.Empty(_f.Events.Query(eventType: AI2P.Core.Events.EventTypes.TaskMoved));
    }

    [Fact]
    public void Subtree_Follows_The_Task_Moved_To_Root()
    {
        // у перенесённой в корень задачи её собственные подзадачи остаются на месте
        var project = _f.Projects.Create("Проект", null, null, null);
        var root = NewTask("Корень", project.Id);
        var middle = NewTask("Середина", project.Id, root.Id);
        var leaf = NewTask("Лист", project.Id, middle.Id);

        _f.Tasks.ChangeParent(middle.Id, "", null);

        Assert.Null(_f.Tasks.Get(middle.Id)!.ParentId);
        Assert.Equal(middle.Id, _f.Tasks.Get(leaf.Id)!.ParentId);
    }
}
