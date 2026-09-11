using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-192-S0 «Диаграмма». Жалоба заказчика по выпуску 1.124: не готовые и идущие задачи одного
/// исполнителя должны стоять в порядке ЧИСЛОВОГО ПРИОРИТЕТА с учётом иерархии — ровно так, как
/// их запустит старт иерархии (T-159).
///
/// Причина: «уже случившимся» (T-164-S0) считалась любая задача с заданием, а фактическое
/// начало у неё сильнее очереди. Поэтому идущая задача — и даже ждущая, которую однажды
/// запускали, — занимала дорожку по времени своего задания и обгоняла незаконченные задачи с
/// БОЛЬШИМ приоритетом. Правка: фактическим порядком раскладывается только СДАННОЕ
/// (проверка/готово/отмена), всё незаконченное идёт после него в порядке очереди.
///
/// Проверяется чистая функция <see cref="TaskDiagram.Layout"/> — базы данных ей не нужно.
/// </summary>
public class T192S0Tests
{
    private static TaskDiagramNodeDto Node(string id, string status, int priority,
        DateTime? startedAt = null, string? parent = null, string executor = "mrclaude") => new()
        {
            Id = id,
            DisplayId = id,
            ExecutorId = executor,
            Status = status,
            PriorityNum = priority,
            DurationHours = 1,
            StartedAt = startedAt,
            ParentId = parent,
            Settled = TaskStatuses.Settled(status),
        };

    private static readonly DateTime Day = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unfinished_Tasks_Stand_In_Priority_Order_Whatever_They_Already_Ran()
    {
        // порядок списка — очередь: приоритеты 30, 20, 10. Первую и третью уже запускали
        // (задание есть), вторая ещё ждёт — но все три не закончены, значит это план
        var high = Node("h", TaskStatuses.Pending, 30, Day.AddHours(5));
        var mid = Node("m", TaskStatuses.Pending, 20);
        var low = Node("l", TaskStatuses.InProgress, 10, Day);
        var nodes = new List<TaskDiagramNodeDto> { high, mid, low };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, high.StartHours);
        Assert.Equal(1, mid.StartHours);
        Assert.Equal(2, low.StartHours);
    }

    [Fact]
    public void Settled_Tasks_Keep_The_Real_Order_And_The_Plan_Goes_After_Them()
    {
        // сданное — это факт: оно стоит в том порядке, в каком случилось (T-164-S0), а
        // незаконченные задачи занимают дорожку после него, по приоритету
        var planHigh = Node("ph", TaskStatuses.Pending, 30);
        var planLow = Node("pl", TaskStatuses.InProgress, 10, Day.AddHours(1));
        var doneLate = Node("d2", TaskStatuses.Done, 20, Day.AddHours(3));
        var doneFirst = Node("d1", TaskStatuses.Review, 5, Day);
        var nodes = new List<TaskDiagramNodeDto> { planHigh, doneLate, planLow, doneFirst };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, doneFirst.StartHours);
        Assert.Equal(1, doneLate.StartHours);
        Assert.Equal(2, planHigh.StartHours);
        Assert.Equal(3, planLow.StartHours);
    }

    [Fact]
    public void Taken_Into_Work_Wins_Only_Among_The_Equal_Ones()
    {
        // правило T-171-S0 осталось, но действует среди РАВНЫХ по приоритету соседей одного
        // родителя: там очередь различает задачи лишь временем создания. Задачу с приоритетом
        // выше идущая задача не обгоняет
        var high = Node("h", TaskStatuses.Pending, 30, parent: "root");
        var takenSame = Node("t", TaskStatuses.InProgress, 20, parent: "root");
        var waitingSame = Node("w", TaskStatuses.Pending, 20, parent: "root");
        // в очереди равные по приоритету стоят по времени создания: ждущая раньше идущей
        var nodes = new List<TaskDiagramNodeDto> { high, waitingSame, takenSame };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, high.StartHours);
        Assert.Equal(1, takenSame.StartHours);
        Assert.Equal(2, waitingSame.StartHours);
    }

    [Fact]
    public void Parent_With_Subtasks_Does_Not_Fall_Behind_A_Weaker_Neighbour()
    {
        // второй заход по жалобе: T-74-S0 (приоритет 35, есть подзадачи) стояла ПРАВЕЕ
        // T-145-S0 (приоритет 15, подзадач нет) — обе ждут, обе у одного исполнителя, обе
        // дети одного родителя. Раскладка клала пачками «все готовые сразу», поэтому задача
        // с подзадачами ждала целый проход и пропускала вперёд все листья дерева
        var kid1 = Node("k1", TaskStatuses.Pending, 30, parent: "big");
        var kid2 = Node("k2", TaskStatuses.Pending, 20, parent: "big");
        var big = Node("big", TaskStatuses.Pending, 35, parent: "root");
        var small = Node("small", TaskStatuses.Pending, 15, parent: "root");
        var root = Node("root", TaskStatuses.Pending, 0);
        // порядок очереди иерархии: потомки раньше родителя, внутри уровня по приоритету
        var nodes = new List<TaskDiagramNodeDto> { kid1, kid2, big, small, root };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, kid1.StartHours);
        Assert.Equal(1, kid2.StartHours);
        Assert.Equal(2, big.StartHours);   // сразу за своими подзадачами
        Assert.Equal(3, small.StartHours); // а не наоборот
        Assert.Equal(4, root.StartHours);
    }

    [Fact]
    public void Hierarchy_Is_Still_Stronger_Than_The_Priority()
    {
        // подзадача с маленьким приоритетом всё равно идёт раньше своего родителя: очередь
        // выполняет родителя последним, и диаграмма обязана показывать то же самое
        var child = Node("c", TaskStatuses.Pending, 1, parent: "p");
        var parent = Node("p", TaskStatuses.Pending, 90);
        var nodes = new List<TaskDiagramNodeDto> { child, parent };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, child.StartHours);
        Assert.Equal(1, parent.StartHours);
    }
}
