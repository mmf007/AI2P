using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-193. НАЧАЛО МОБИЛЬНОЙ ВЕРСИИ: прокрутка досок пальцем и компактный заголовок.
///
/// Жалоба заказчика (Firefox на Android): «при попытке листнуть доску вертикально вместо
/// скролла отрывается карточка задачи». Причина — перетаскивание карточки: браузер телефона
/// начинает его от обычного движения пальца.
///
/// ВТОРОЙ ЗАХОД (после проверки заказчиком: «скролинг доски вниз так и не починен»). Первая
/// редакция снимала у карточки draggable на время касания — этого не хватило по двум
/// причинам: (1) MudBlazor вешает на свой MudDynamicDropItem СОБСТВЕННЫЕ ontouchmove/
/// ontouchend, и они тащат карточку независимо от атрибута; (2) Blazor перерисовывает
/// карточки прямо во время прокрутки (ленивые превью) и возвращает draggable из разметки.
/// Поэтому теперь карточка задачи не перетаскивается ВООБЩЕ: доска не строится на
/// MudDropContainer/MudDropZone, состояние задачи меняется в её карточке. Перенос колонок
/// (за зону заголовка) остался — заказчик просил его сохранить. Ещё одна страховка общего
/// движка: пока палец на экране, начатое браузером перетаскивание отменяется в dragstart —
/// это и защищает дерево задач от возврата draggable ререндером.
///
/// Вторая просьба — уменьшить заголовок под закладкой до размера названия проекта в шапке
/// карточки задачи: на экране телефона он съедал высоту.
///
/// Само поведение проверяется только браузером (test/t193/ui193b.py, настоящий Chrome с
/// эмуляцией касаний); здесь — контракт разметки, стилей и interop, на котором оно держится.
/// </summary>
public sealed class T193Tests
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

    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"в стилях нет правила «{selector}»");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        Assert.True(close > open, $"правило «{selector}» не закрыто");
        return css[(open + 1)..close];
    }

    // ---------- 1. нативное перетаскивание на время касания выключается ----------

    [Fact]
    public void Touch_Turns_Native_Drag_Off_While_The_Finger_Is_Down()
    {
        var js = Js();
        var engine = js[js.IndexOf("function initTouchDnd", StringComparison.Ordinal)..];
        engine = engine[..engine.IndexOf("function initBoardLazy", StringComparison.Ordinal)];

        // именно этим прокрутка и возвращается: без draggable браузер телефона листает
        Assert.Contains("setAttribute('draggable', 'false')", engine);
        // и возвращается ровно в конце касания, а не при отмене жеста посреди прокрутки:
        // иначе браузер подхватил бы то самое движение пальца
        Assert.Contains("function restoreNativeDrag", engine);
        var reset = engine[engine.IndexOf("function reset()", StringComparison.Ordinal)..];
        reset = reset[..reset.IndexOf("container.addEventListener('dragstart'", StringComparison.Ordinal)];
        Assert.DoesNotContain("restoreNativeDrag();", reset);
        // конец касания — обработчик onEnd (с T-4-S1 он висит и на строке, и на контейнере)
        var end = engine[engine.IndexOf("function onEnd(e)", StringComparison.Ordinal)..];
        end = end[..end.IndexOf("function onCancel(e)", StringComparison.Ordinal)];
        Assert.Contains("restoreNativeDrag();", end);
    }

    [Fact]
    public void Native_Drag_Started_Mid_Touch_Is_Cancelled()
    {
        var js = Js();
        var engine = js[js.IndexOf("function initTouchDnd", StringComparison.Ordinal)..];
        engine = engine[..engine.IndexOf("function initBoardLazy", StringComparison.Ordinal)];

        // одного снятия атрибута мало: Blazor перерисовывает строки во время прокрутки и
        // возвращает draggable="true" из разметки прямо посреди касания. Поэтому, пока
        // палец на экране, начатое браузером перетаскивание отменяется
        var dragstart = engine[engine.IndexOf("addEventListener('dragstart'", StringComparison.Ordinal)..];
        dragstart = dragstart[..dragstart.IndexOf("addEventListener('touchstart'", StringComparison.Ordinal)];
        Assert.Contains("if (touching) e.preventDefault();", dragstart);
        Assert.Contains("}, true);", dragstart); // фаза перехвата — раньше обработчиков доски
        // признак «палец на экране» ставится и снимается вместе с касанием
        Assert.Contains("touching = true;", engine);
        Assert.Contains("touching = false;", engine);
    }

    [Fact]
    public void Drag_By_Finger_Starts_Only_After_A_Hold()
    {
        var js = Js();

        // «подержать и вести»: пока карточку не взяли, touchmove НЕ перехватывается —
        // это обычная прокрутка колонки или списка
        Assert.Contains("const TouchHoldMs", js);
        Assert.Contains("const TouchMoveTol", js);
        // порог берётся из правила, а по умолчанию — прежний TouchHoldMs (T-4-S1: у ручки
        // переноса своё значение 0 — взятое за ручку ждать удержания не должно)
        Assert.Contains("holdTimer = setTimeout(begin, rule.hold === undefined ? TouchHoldMs : rule.hold)", js);
        // движение пальца разбирает onMove (с T-4-S1 — именованная функция: её вешают и на
        // строку, и на контейнер)
        var move = js[js.IndexOf("function onMove(e)", StringComparison.Ordinal)..];
        move = move[..move.IndexOf("function onEnd(e)", StringComparison.Ordinal)];
        var beforeGhost = move[..move.IndexOf("e.preventDefault();", StringComparison.Ordinal)];
        Assert.Contains("if (!ghost)", beforeGhost);
        Assert.Contains("TouchMoveTol", beforeGhost);
        // слушатель обязан быть НЕ passive, иначе preventDefault не сработает вовсе
        Assert.Contains("container.addEventListener('touchmove', onMove, { passive: false });", js);
    }

    // ---------- 2. доска: карточка не перетаскивается, колонка — да ----------

    [Fact]
    public void Board_Cards_Are_Not_Draggable_At_All()
    {
        var board = Read("Components", "BoardView.razor");

        // главное лечение второго захода: карточку не за что взять ни мышью, ни пальцем
        Assert.DoesNotContain("<MudDropContainer", board);
        Assert.DoesNotContain("<MudDropZone", board);
        Assert.DoesNotContain("OnCardMoveAsync", board);
        Assert.DoesNotContain("ItemDropped", board);
        // карточка осталась КЛИКАБЕЛЬНОЙ (открывает задачу) и помечена своим якорем
        Assert.Contains("data-board-task=\"@task.Id\"", board);
        Assert.Contains("State.OpenTask(task.Id", board);
        // единственный draggable на доске — заголовок колонки
        Assert.Equal(1, board.Split("draggable=").Length - 1);
        Assert.Contains("draggable=\"true\" data-board-drag=\"@status.Id\"", board);
    }

    [Fact]
    public void Board_Touch_Rule_Covers_Only_Column_Headers()
    {
        var js = Js();
        var init = js[js.IndexOf("function initBoardTouch", StringComparison.Ordinal)..];
        init = init[..init.IndexOf("function copyText", StringComparison.Ordinal)];

        // жест пальцем оставлен только заголовку: свайп по карточке листает колонку
        Assert.DoesNotContain("[data-board-task]", init);
        Assert.DoesNotContain("OnCardMoveAsync", init);
        Assert.Contains("source: '[data-board-drag]'", init);
        Assert.Contains("target: '[data-board-col]'", init);
        Assert.Contains("OnColumnDropAsync", init);
    }

    // ---------- 3. дерево задач болело тем же ----------

    [Fact]
    public void Tree_Rows_Are_Dragged_By_The_Same_Gesture()
    {
        var tree = Read("Components", "TaskTreeView.razor");
        var js = Js();

        // право на перенос читается из своего атрибута: draggable как раз и снимается
        Assert.Contains("data-tree-move=\"@(CanMove(task) ? \"1\" : \"0\")\"", tree);
        Assert.Contains("source: '[data-tree-task][data-tree-move=\"1\"]'", js);
        Assert.Contains("target: '[data-tree-drop]'", js);
    }

    // ---------- 4. карточка «на пальце» и подсветка цели ----------

    [Fact]
    public void Flying_Copy_Does_Not_Catch_The_Finger()
    {
        var ghost = Rule(Home(), ".ai2p-touch-ghost");

        Assert.Contains("position: fixed", ghost);
        // без этого elementFromPoint под пальцем всегда находил бы копию, а не колонку
        Assert.Contains("pointer-events: none", ghost);
        Assert.Contains(".ai2p-touch-over", Home());
        Assert.Contains(".ai2p-touch-src", Home());
    }

    // ---------- 5. колонка переносится по-прежнему, и только за заголовок ----------

    [Fact]
    public void Column_Is_Still_Dragged_By_Its_Header_With_The_Mouse()
    {
        var js = Js();
        var dnd = js[js.IndexOf("function initBoardDnd", StringComparison.Ordinal)..];
        dnd = dnd[..dnd.IndexOf("function initBoardTouch", StringComparison.Ordinal)];

        // Firefox без данных в dataTransfer перетаскивание не начинает
        Assert.Contains("e.dataTransfer.setData('text/plain', dragged)", dnd);
        Assert.Contains("OnColumnDropAsync", dnd);
        // а карточку доска не тащит вовсе — упоминаний о ней в переносе не осталось
        Assert.DoesNotContain("data-board-task", dnd);
    }

    // ---------- 6. компактный заголовок представления ----------

    [Fact]
    public void View_Title_Is_As_Small_As_The_Project_Name_In_The_Task_Header()
    {
        var home = Home();
        var title = Rule(home, ".mud-typography.ai2p-view-title");
        var project = Rule(home, ".ai2p-task-project");

        var size = FontSize(title);
        Assert.Equal(FontSize(project), size);
        Assert.True(size < 1.0, "заголовок представления должен быть заметно меньше Typo.h5");
    }

    [Fact]
    public void Every_View_Title_Uses_The_Compact_Class()
    {
        var views = new[]
        {
            "BillingView", "ExecutorsView", "HistoryView", "InboxView", "ProjectsView",
            "ScheduleView", "SettingsView", "TasksView", "TeamsView", "TemplatesView",
        };
        foreach (var view in views)
        {
            var text = Read("Components", view + ".razor");
            var h5 = text.IndexOf("Typo=\"Typo.h5\"", StringComparison.Ordinal);
            Assert.True(h5 > 0, $"{view}: заголовка представления нет вовсе");
            Assert.Contains("Typo=\"Typo.h5\" Class=\"ai2p-view-title\"", text);
        }
    }

    private static double FontSize(string rule)
    {
        var at = rule.IndexOf("font-size:", StringComparison.Ordinal);
        Assert.True(at >= 0, "в правиле нет font-size");
        var value = rule[(at + "font-size:".Length)..];
        value = value[..value.IndexOf("rem", StringComparison.Ordinal)].Trim();
        return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
