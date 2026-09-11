using System.Text.Json;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-4-S1. Перенос по иерархии НА ТЕЛЕФОНЕ: «не работает драг для смены подчинённости».
///
/// Механизм переноса (T-193: подержать палец 400 мс и вести) в эмуляции телефона исправен —
/// строка берётся, цель подсвечивается, родитель меняется (test/t4s1/probe1..3). Но на
/// НАСТОЯЩЕМ телефоне удержание пальца перехватывает сам браузер: Chrome на Android за те же
/// 400 мс выделяет слово и показывает маркеры выделения, Safari на iOS открывает своё меню, и
/// дальше палец ведёт маркер, а не строку. Плюс жест «на ощупь» — про него надо знать.
///
/// Лечение из трёх частей:
///   1) у каждой перетаскиваемой строки есть видимая РУЧКА (data-tree-grip) — за неё строка
///      берётся СРАЗУ, без удержания (правило движка с handle и hold = 0);
///   2) текст строки не выделяется (user-select: none, -webkit-touch-callout: none), а
///      контекстное меню, пока палец на экране, не показывается;
///   3) движок стал устойчивее: выделение снимается и нативное перетаскивание запрещается
///      повторно в начале жеста, допуск дрожания пальца поднят, автопрокрутка учитывает
///      липкую полосу «в корень» (иначе список уезжает ровно тогда, когда к ней подводят).
///
/// Доски правка не касается: карточка задачи не перетаскивается вовсе (T-193, второй заход) —
/// здесь это закреплено ещё раз, потому что заказчик просил «в иерархии работает, на досках
/// не работает».
///
/// Само поведение проверяется браузером (test/t4s1/ui4s1.py, настоящий Chrome с эмуляцией
/// касаний); здесь — контракт разметки, стилей, движка и словарей.
/// </summary>
public sealed class T4S1Tests
{
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

    private static string UiRoot() => Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI");

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UiRoot() }.Concat(parts).ToArray()));

    private static string Js() => Read("wwwroot", "ai2p.js");
    private static string Home() => Read("Pages", "Home.razor");
    private static string Tree() => Read("Components", "TaskTreeView.razor");

    /// <summary>Тело CSS-правила из &lt;style&gt; страницы (по селектору).</summary>
    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"в стилях нет правила «{selector}»");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        Assert.True(close > open, $"правило «{selector}» не закрыто");
        return css[(open + 1)..close];
    }

    /// <summary>Кусок ai2p.js от одной функции до другой.</summary>
    private static string Between(string from, string to)
    {
        var js = Js();
        var start = js.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"в ai2p.js нет «{from}»");
        var rest = js[start..];
        var end = rest.IndexOf(to, StringComparison.Ordinal);
        Assert.True(end > 0, $"в ai2p.js после «{from}» нет «{to}»");
        return rest[..end];
    }

    // ---------- 1. ручка переноса в строке дерева ----------

    [Fact]
    public void Tree_Row_Has_A_Drag_Grip_Only_When_It_May_Be_Moved()
    {
        var tree = Tree();

        Assert.Contains("data-tree-grip=\"1\"", tree);
        Assert.Contains("class=\"ai2p-tree-grip\"", tree);
        // ручка есть только у строки, которую разрешено двигать (чужая задача — только на
        // дирижёре, ТЗ гл. 6, этап 42): условие ровно то же, что у draggable/data-tree-move
        var grip = tree[..tree.IndexOf("data-tree-grip", StringComparison.Ordinal)];
        Assert.Contains("@if (CanMove(task))", grip);
        // тап по ручке не должен открывать карточку задачи
        var afterGrip = tree[tree.IndexOf("data-tree-grip", StringComparison.Ordinal)..];
        Assert.Contains("@onclick:stopPropagation=\"true\"", afterGrip[..200]);
        // и у ручки есть подсказка — жест иначе не найти
        Assert.Contains("title=\"@L[\"tasks.tree.dnd.grip\"]\"", tree);
    }

    [Fact]
    public void Grip_Rule_Starts_The_Drag_Without_Any_Hold()
    {
        var init = Between("function initTreeDnd", "function observeLazyMd");

        // правило ручки — то же самое правило дерева, но берёт сразу
        Assert.Contains("handle: '[data-tree-grip]', hold: 0", init);
        // и обычное правило «подержать и вести» осталось: за строку по-прежнему можно взять
        Assert.Contains("source: '[data-tree-task][data-tree-move=\"1\"]'", init);
        Assert.Contains("OnTreeDropAsync", init);

        var engine = Between("function initTouchDnd", "// --- drag-n-drop иерархии задач");
        // правила с ручкой перебираются ПЕРВЫМИ: иначе касание ручки попало бы в общее
        // правило строки и снова ждало бы удержания
        Assert.Contains("rules.filter(function (r) { return r.handle; })", engine);
        Assert.Contains("if (ordered[i].handle && !e.target.closest(ordered[i].handle)) continue;", engine);
        Assert.Contains("rule.hold === undefined ? TouchHoldMs : rule.hold", engine);
        // взятое за ручку не отменяется движением пальца
        Assert.Contains("rule.hold !== 0 &&", engine);
    }

    // ---------- 2. браузер не должен перехватывать удержание ----------

    [Fact]
    public void Tree_Row_Text_Is_Not_Selectable_And_Has_No_Callout()
    {
        var row = Rule(Home(), ".ai2p-tree-node");

        Assert.Contains("user-select: none", row);
        Assert.Contains("-webkit-user-select: none", row);
        Assert.Contains("-webkit-touch-callout: none", row); // меню Safari на iOS

        // жест ручки — наш: браузеру запрещено перехватывать его прокруткой
        var grip = Rule(Home(), ".ai2p-tree-grip");
        Assert.Contains("touch-action: none", grip);
        Assert.Contains("cursor: grab", grip);
    }

    [Fact]
    public void Context_Menu_Is_Suppressed_While_The_Finger_Is_Down()
    {
        var engine = Between("function initTouchDnd", "// --- drag-n-drop иерархии задач");

        var menu = engine[engine.IndexOf("addEventListener('contextmenu'", StringComparison.Ordinal)..];
        Assert.Contains("if (touching) e.preventDefault();", menu[..200]);
    }

    [Fact]
    public void Gesture_Start_Clears_The_Selection_And_Blocks_Native_Drag_Again()
    {
        var engine = Between("function initTouchDnd", "// --- drag-n-drop иерархии задач");
        var begin = engine[engine.IndexOf("function begin()", StringComparison.Ordinal)..];
        begin = begin[..begin.IndexOf("function moveGhost()", StringComparison.Ordinal)];

        // выделение, которое браузер успел сделать за время удержания, снимается
        Assert.Contains("removeAllRanges", begin);
        // draggable мог вернуться ререндером Blazor за те же 400 мс — снимаем ещё раз
        Assert.Contains("noNativeDrag(src);", begin);
    }

    [Fact]
    public void Finger_Tremor_Does_Not_Cancel_The_Gesture_Too_Early()
    {
        var js = Js();
        var line = js[js.IndexOf("const TouchMoveTol", StringComparison.Ordinal)..];
        line = line[..line.IndexOf('\n')];
        var value = int.Parse(new string(line.Where(char.IsDigit).ToArray()));

        // палец на месте не стоит: прежних 10 px хватало, чтобы жест не начинался вовсе
        Assert.True(value >= 16, $"допуск дрожания пальца слишком мал: {value} px");
    }

    // ---------- 3. жест переживает перерисовку списка ----------

    [Fact]
    public void Gesture_Survives_A_Redraw_Of_The_List()
    {
        var engine = Between("function initTouchDnd", "// --- drag-n-drop иерархии задач");

        // касание браузер доставляет узлу, НА КОТОРОМ оно началось. Blazor во время жеста
        // перерисовывает список, старая строка уходит из документа — и всплывать событию
        // больше некуда: слушатель контейнера его не увидит, а жест пропадёт молча.
        // Поэтому обработчики висят и на строке тоже
        Assert.Contains("function bindSrc(node)", engine);
        Assert.Contains("function unbindSrc()", engine);
        Assert.Contains("bindSrc(src);", engine);
        foreach (var evt in new[] { "touchmove", "touchend", "touchcancel" })
        {
            Assert.Contains($"node.addEventListener('{evt}'", engine);
            Assert.Contains($"container.addEventListener('{evt}'", engine);
        }
        // событие приходит дважды (строка и контейнер) — обработать его надо ровно раз
        Assert.Contains("function once(e)", engine);
        Assert.Contains("e.__ai2pTouch = true;", engine);
        // и «личные» обработчики снимаются вместе с концом касания, а не копятся
        Assert.Contains("unbindSrc();", engine);
    }

    [Fact]
    public void Reload_Of_The_Task_List_Does_Not_Wipe_The_View()
    {
        var view = Read("Components", "TasksView.razor");

        // прежде на время КАЖДОГО перечитывания списка (а оно идёт на любое изменение
        // состояния) представление подменялось полосой прогресса: компонент разрушался, а
        // вместе с ним — начатый перенос и свёрнутость узлов дерева
        Assert.DoesNotContain("_loaded = false;", view);
        Assert.Contains("_busy = true;", view);
        Assert.Contains("_busy = false;", view);
        Assert.Contains("ai2p-view-busy", view);
        Assert.Contains("ai2p-view-busy", Home());
    }

    // ---------- 4. автопрокрутка не уводит список от липкой полосы «в корень» ----------

    [Fact]
    public void Touch_Auto_Scroll_Counts_The_Sticky_Drop_Zone()
    {
        var engine = Between("function initTouchDnd", "// --- drag-n-drop иерархии задач");
        var scroll = engine[engine.IndexOf("function autoScrollStep()", StringComparison.Ordinal)..];
        scroll = scroll[..scroll.IndexOf("function begin()", StringComparison.Ordinal)];

        // верхний край полосы автопрокрутки отсчитывается от НИЗА липкой зоны сброса
        Assert.Contains("rule.sticky", scroll);
        Assert.Contains("sticky.getBoundingClientRect().bottom", scroll);

        // у дерева липкая зона — верхняя полоса «в корень»
        var init = Between("function initTreeDnd", "function observeLazyMd");
        Assert.Contains("sticky: '.ai2p-tree-root-top'", init);
    }

    // ---------- 4. доска: перетаскивания задач нет и не появилось ----------

    [Fact]
    public void Board_Still_Does_Not_Drag_Tasks()
    {
        var board = Read("Components", "BoardView.razor");

        // «в иерархии работает, на досках не работает» (просьба заказчика): единственный
        // draggable доски — заголовок колонки, у карточки задачи нет ни атрибута, ни ручки
        Assert.Equal(1, board.Split("draggable=").Length - 1);
        Assert.Contains("draggable=\"true\" data-board-drag=\"@status.Id\"", board);
        Assert.DoesNotContain("data-tree-grip", board);
        Assert.DoesNotContain("<MudDropContainer", board);

        var init = Between("function initBoardTouch", "// --- копирование в буфер обмена");
        Assert.DoesNotContain("[data-board-task]", init);
        Assert.DoesNotContain("hold: 0", init); // мгновенный жест — только у ручки дерева
    }

    // ---------- 5. подсказка ручки есть на обоих языках ----------

    [Fact]
    public void Grip_Hint_Is_Translated()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty("tasks.tree.dnd.grip", out var value),
                $"в словаре {lang}.json нет ключа tasks.tree.dnd.grip");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
        }
    }
}
