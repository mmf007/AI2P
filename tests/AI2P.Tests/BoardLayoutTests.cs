using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-128 (подзадача T-118.1), второй заход: РАЗМЕТКА И СТИЛИ ДОСКИ.
///
/// Жалоба заказчика (с картинкой Trello): у каждой колонки должен быть СВОЙ вертикальный
/// скролл — прокручивается та колонка, над которой мышь; внешний фрейм доски фиксирован по
/// высоте, своего вертикального скролла у него нет и вниз он не уходит; горизонтальная
/// полоса фрейма видна всегда, до неё не надо листать; фильтр общий на все колонки.
///
/// Живой проверкой в браузере (headless Chrome по CDP, test/t128) нашлись две настоящие
/// поломки первой редакции, обе — регрессии, на которые здесь стоят тесты:
/// 1) ленивая дорисовка не работала: <c>IntersectionObserver</c> на маячке срабатывал при
///    каждом ререндере MudDropContainer (маячок на мгновение оказывался вверху колонки),
///    и колонка из 520 задач дорисовывалась ЦЕЛИКОМ — 520 карточек в DOM вместо 30.
///    Теперь пачку тянет обработчик <c>scroll</c> самой колонки;
/// 2) во вкладке «Задачи» КАРТОЧКИ ПРОЕКТА высоту доске держать было нечем (MudTabs растёт
///    по содержимому): доска уходила вниз на тысячи пикселей, колонки теряли свой скролл,
///    а горизонтальная полоса оказывалась далеко под низом страницы.
///
/// Сама геометрия проверяется только браузером; здесь — контракт разметки, стилей и
/// interop, который эту геометрию задаёт.
/// </summary>
public sealed class BoardLayoutTests
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
    private static string Board() => Read("Components", "BoardView.razor");
    private static string Home() => Read("Pages", "Home.razor");
    private static string ProjectCard() => Read("Components", "ProjectCardView.razor");

    // ---------- 1. внешний фрейм доски ----------

    [Fact]
    public void Board_Frame_Is_Fixed_Height_With_Horizontal_Scrollbar_Only()
    {
        var css = Home();
        var rule = Rule(css, ".ai2p-board-frame ");

        Assert.Contains("height: 100%", rule);      // высота от фрейма закладки, а не от содержимого
        Assert.Contains("min-height: 0", rule);     // иначе flex-элемент не даёт себя сжать
        Assert.Contains("overflow-x: auto", rule);  // горизонтальная полоса — у доски
        Assert.Contains("overflow-y: hidden", rule); // общего вертикального скролла у доски НЕТ
    }

    [Fact]
    public void Tasks_View_Gives_The_Board_A_Full_Height_Column()
    {
        // представление «задачи» — колонка на всю высоту фрейма: тулбар с ОБЩИМ фильтром
        // сверху, доска нижним этажом
        Assert.Contains("ai2p-tasks-frame", Read("Components", "TasksView.razor"));
        var rule = Rule(Home(), ".ai2p-tasks-frame ");
        Assert.Contains("flex-direction: column", rule);
        Assert.Contains("height: 100%", rule);
        Assert.Contains("min-height: 0", rule);
    }

    // ---------- 2. своя прокрутка у каждой колонки ----------

    [Fact]
    public void Every_Column_Scrolls_On_Its_Own()
    {
        var rule = Rule(Home(), ".ai2p-board-body ");

        Assert.Contains("overflow-y: auto", rule);   // вертикальный скролл — у КОЛОНКИ
        Assert.Contains("overflow-x: hidden", rule);
        Assert.Contains("min-height: 0", rule);
        // полоса должна быть ЗАМЕТНА (жалоба заказчика: «виден раздельный скрол бар»)
        Assert.Contains("scrollbar-width: auto", rule);
        Assert.Contains("scrollbar-color:", rule);
        // колонка обрезает содержимое, иначе карточки вылезут из-под заголовка
        Assert.Contains("overflow: hidden", Rule(Home(), ".ai2p-board-col "));
    }

    [Fact]
    public void Board_Styles_Take_Colors_Only_From_Palette()
    {
        foreach (var selector in new[] { ".ai2p-board-frame ", ".ai2p-board-body ", ".ai2p-board-header " })
        {
            var rule = Rule(Home(), selector);
            Assert.DoesNotContain("#", rule);
            Assert.DoesNotContain("rgb", rule);
            Assert.Contains("var(--mud-palette-", rule);
        }
    }

    // ---------- 3. ленивая дорисовка карточек ----------

    [Fact]
    public void Lazy_Batches_Are_Driven_By_Column_Scroll_Not_By_Visibility()
    {
        var js = Js();

        // регрессия: наблюдатель видимости маячка дорисовывал колонку целиком
        Assert.DoesNotContain("observeBoardMore", js);
        Assert.DoesNotContain("boardMoreObserver", js);
        Assert.Contains("function initBoardLazy", js);
        Assert.Contains("addEventListener('scroll'", js);
        Assert.Contains("OnColumnMoreAsync", js);
        Assert.Contains("initBoardLazy: initBoardLazy", js);
        Assert.Contains("disposeBoardLazy: disposeBoardLazy", js);
    }

    [Fact]
    public void Column_Body_Carries_Lazy_Attributes_And_Board_Calls_The_New_Interop()
    {
        var board = Board();

        Assert.Contains("data-board-lazy=\"@status.Id\"", board);
        Assert.Contains("data-board-more=\"@(HasMore(status.Id) ? \"1\" : \"0\")\"", board);
        Assert.Contains("ai2p.initBoardLazy", board);
        Assert.Contains("ai2p.disposeBoardLazy", board);
        Assert.DoesNotContain("observeBoardMore", board);
    }

    [Fact]
    public void Column_Header_Counter_Counts_All_Tasks_Not_Rendered_Cards()
    {
        // счётчик в заголовке — ПОЛНЫЙ: считается по Tasks, а не по нарисованным _visible
        Assert.Contains("(@Tasks.Count(t => t.Status == status.Id))", Board());
    }

    // ---------- 4. перетаскивание колонок и карточек ----------

    [Fact]
    public void Column_Header_Is_The_Only_Thing_The_Board_Drags()
    {
        var board = Board();

        Assert.Contains("draggable=\"true\" data-board-drag=\"@status.Id\"", board);
        Assert.Contains("ai2p.initBoardDnd", board);
        // T-193 (второй заход): карточка задачи не перетаскивается вовсе — ни мышью, ни
        // пальцем. Никаких MudDropContainer/MudDropZone на доске больше нет: их
        // MudDynamicDropItem как раз и делал карточку перетаскиваемой
        Assert.DoesNotContain("<MudDropContainer", board);
        Assert.DoesNotContain("<MudDropZone", board);
        // draggable в разметке ровно один — у заголовка колонки
        Assert.Equal(1, board.Split("draggable=").Length - 1);
        // перенос колонки — единственное, что доска сохраняет за собой
        Assert.Contains("OnColumnDropAsync", board);
    }

    [Fact]
    public void Column_Drag_Ignores_Everything_But_The_Header()
    {
        var js = Js();
        var dnd = js[js.IndexOf("function initBoardDnd", StringComparison.Ordinal)..];
        dnd = dnd[..dnd.IndexOf("function initBoardTouch", StringComparison.Ordinal)];

        // пока за заголовок не тянут (dragged пуст), обработчики молчат
        Assert.Contains("if (!dragged) return;", dnd);
        // и карточку доска в dataTransfer больше не кладёт: тащить её нечем
        Assert.DoesNotContain("data-board-task", dnd);
    }

    // ---------- 5. доска в карточке проекта ----------

    [Fact]
    public void Project_Card_Keeps_The_Board_Inside_The_Frame()
    {
        var card = ProjectCard();

        Assert.Contains("class=\"ai2p-card-fill\"", card);
        Assert.Contains("class=\"pa-2 ai2p-card-tab-fill\"", card);

        var css = Home();
        Assert.Contains("height: 100%", Rule(css, ".ai2p-card-fill "));
        Assert.Contains("min-height: 0", Rule(css, ".ai2p-card-fill > .mud-tabs "));
        Assert.Contains("min-height: 0", Rule(css, ".ai2p-card-fill > .mud-tabs > .mud-tabs-panels "));
        Assert.Contains("height: 100%", Rule(css, ".ai2p-card-tab-fill "));
    }

    /// <summary>Тело CSS-правила по началу селектора: «.селектор { … }».</summary>
    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf(selector + "{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"в стилях нет правила «{selector}»");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        Assert.True(close > open, $"правило «{selector}» не закрыто");
        return css[(open + 1)..close];
    }
}
