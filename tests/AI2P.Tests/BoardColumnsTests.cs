using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-128 (подзадача T-118.1): ДОСКА — ЛЕНИВАЯ ОТРИСОВКА КОЛОНОК И ИХ ПОРЯДОК МЫШКОЙ.
///
/// Раньше доска рисовала ВСЕ карточки всех колонок сразу и растягивала страницу вниз,
/// а порядок колонок был жёстко задан порядковым номером справочника состояний.
/// Теперь колонка рисует первую пачку карточек (счётчик в её заголовке всё равно полный),
/// а следующие дорисовываются, когда пользователь долистал колонку до низа; порядок колонок
/// переставляется перетаскиванием заголовка и запоминается по ключу
/// <c>board.order.&lt;projectId&gt;</c> в состояниях представлений аккаунта.
///
/// Здесь проверяется чистая часть этой логики (<see cref="BoardColumns"/>); разметка,
/// прокрутка и сами события мыши живут в BoardView.razor и ai2p.js.
/// </summary>
public sealed class BoardColumnsTests
{
    private static TaskStatusDef Status(string id, int sortOrder) =>
        new() { Id = id, Name = id, SortOrder = sortOrder };

    /// <summary>Встроенный справочник состояний: порядковые номера через 10 (ТЗ v1.37).</summary>
    private static List<TaskStatusDef> Reference() =>
    [
        Status("draft", 10), Status("in_progress", 20), Status("review", 30), Status("done", 40),
    ];

    private static TaskItem Task(string status, string title) =>
        new() { Status = status, Title = title };

    // ---------- 1. порядок колонок ----------

    [Fact]
    public void Without_saved_order_columns_follow_reference_sort_order()
    {
        var columns = BoardColumns.Order(Reference(), []);

        Assert.Equal(["draft", "in_progress", "review", "done"], columns.Select(c => c.Id));
    }

    [Fact]
    public void Saved_order_wins_over_reference_sort_order()
    {
        var columns = BoardColumns.Order(Reference(), ["done", "review"]);

        // сохранённые — в своём порядке, остальные следом по SortOrder
        Assert.Equal(["done", "review", "draft", "in_progress"], columns.Select(c => c.Id));
    }

    [Fact]
    public void Status_missing_from_saved_order_goes_to_the_tail()
    {
        // состояние добавлено пользователем ПОСЛЕ того, как порядок был сохранён
        var reference = Reference();
        reference.Add(Status("my_own", 5)); // маленький SortOrder — но в хвост, а не в начало
        var columns = BoardColumns.Order(reference, ["done", "review", "draft", "in_progress"]);

        Assert.Equal("my_own", columns[^1].Id);
    }

    [Fact]
    public void Saved_order_may_mention_unknown_statuses()
    {
        // состояние удалили из справочника, а в сохранённом порядке оно осталось
        var columns = BoardColumns.Order(Reference(), ["ghost", "done"]);

        Assert.Equal(["done", "draft", "in_progress", "review"], columns.Select(c => c.Id));
    }

    // ---------- 2. перестановка колонки мышкой ----------

    [Fact]
    public void Dropping_column_before_target_puts_it_to_the_left()
    {
        var order = BoardColumns.Move(["draft", "in_progress", "review", "done"], [],
            fromId: "done", toId: "in_progress", before: true);

        Assert.Equal(["draft", "done", "in_progress", "review"], order);
    }

    [Fact]
    public void Dropping_column_after_target_puts_it_to_the_right()
    {
        var order = BoardColumns.Move(["draft", "in_progress", "review", "done"], [],
            fromId: "draft", toId: "review", before: false);

        Assert.Equal(["in_progress", "review", "draft", "done"], order);
    }

    [Fact]
    public void Dropping_column_on_itself_changes_nothing()
    {
        var saved = new List<string> { "done", "draft" };
        var order = BoardColumns.Move(["draft", "done"], saved, "done", "done", before: true);

        Assert.Equal(saved, order);
    }

    [Fact]
    public void Move_keeps_statuses_that_are_not_on_the_board_now()
    {
        // «отменено» неактивно и задач в нём нет — колонки на доске нет, а порядок его помнит
        var order = BoardColumns.Move(["draft", "done"], ["cancelled", "draft", "done"],
            fromId: "done", toId: "draft", before: true);

        Assert.Equal(["done", "draft", "cancelled"], order);
    }

    // ---------- 3. какие карточки нарисованы ----------

    [Fact]
    public void Only_the_first_page_of_every_column_is_rendered()
    {
        var tasks = new List<TaskItem>();
        for (var i = 0; i < 500; i++)
        {
            tasks.Add(Task("done", $"готово {i}"));
        }
        for (var i = 0; i < 7; i++)
        {
            tasks.Add(Task("draft", $"черновик {i}"));
        }

        Assert.Equal(BoardColumns.PageSize, BoardColumns.Column(tasks, "done", BoardColumns.PageSize).Count);
        // короткой колонке хватает пачки
        Assert.Equal(7, BoardColumns.Column(tasks, "draft", BoardColumns.PageSize).Count);
        // счётчик в заголовке колонки считается по ПОЛНОМУ списку, а не по нарисованному
        Assert.Equal(500, tasks.Count(t => t.Status == "done"));
    }

    [Fact]
    public void Rendered_cards_keep_the_order_of_the_source_list()
    {
        var tasks = new List<TaskItem>
        {
            Task("done", "первая"), Task("draft", "черновик"), Task("done", "вторая"),
        };

        // из колонки взята ПЕРВАЯ карточка, сортировка представления не переставлена
        Assert.Equal(["первая"], BoardColumns.Column(tasks, "done", 1).Select(t => t.Title));
        Assert.Equal(["первая", "вторая"], BoardColumns.Column(tasks, "done", 30).Select(t => t.Title));
    }

    [Fact]
    public void Next_page_adds_cards_only_to_the_scrolled_column()
    {
        var tasks = new List<TaskItem>();
        for (var i = 0; i < 100; i++)
        {
            tasks.Add(Task("done", $"готово {i}"));
            tasks.Add(Task("draft", $"черновик {i}"));
        }
        // «долистали» до низа только колонку done — её предел вырос, у соседней прежний
        Assert.Equal(BoardColumns.PageSize * 2,
            BoardColumns.Column(tasks, "done", BoardColumns.PageSize * 2).Count);
        Assert.Equal(BoardColumns.PageSize,
            BoardColumns.Column(tasks, "draft", BoardColumns.PageSize).Count);
    }

    [Fact]
    public void Column_Takes_Only_Its_Own_Status()
    {
        var tasks = new List<TaskItem>
        {
            Task("done", "готовая"), Task("draft", "черновик"), Task("DONE", "чужой регистр"),
        };

        var column = BoardColumns.Column(tasks, "done", BoardColumns.PageSize);

        // код состояния сравнивается точно: «DONE» — другое состояние справочника
        Assert.Equal(["готовая"], column.Select(t => t.Title));
    }
}
