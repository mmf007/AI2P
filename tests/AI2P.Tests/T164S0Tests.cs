using AI2P.Core;
using AI2P.Core.Api;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-164-S0 «Диаграмма». Жалоба заказчика по выпуску 1.118: на временной шкале корневой
/// задачи T-149-S0 последняя ещё не сделанная подзадача (прогон ветки, приоритет 5) стояла
/// САМОЙ ЛЕВОЙ — то есть раньше уже выполненных задач того же исполнителя.
///
/// Причина: раскладка шла в порядке ОЧЕРЕДИ иерархии (внутри уровня по убыванию приоритета),
/// и задача, которую очередь запустила бы первой, занимала дорожку исполнителя с нуля, даже
/// если по факту её начали последней. Правка — перед раскладкой список пересортировывается по
/// фактическому началу (<see cref="TaskDiagramNodeDto.StartedAt"/>), а ещё не сделанные задачи
/// идут после сделанных в прежнем порядке очереди. С T-192-S0 фактическим порядком
/// раскладывается только СДАННОЕ (проверка/готово/отмена), поэтому здесь у сделанных задач
/// стоит <see cref="TaskDiagramNodeDto.Settled"/>.
///
/// Проверяется чистая функция <see cref="TaskDiagram.Layout"/> — базы данных ей не нужно.
/// </summary>
public class T164S0Tests
{
    private static TaskDiagramNodeDto Node(string id, string executor, double hours,
        DateTime? startedAt = null, string? parent = null, bool settled = false) => new()
        {
            Id = id,
            DisplayId = id,
            ExecutorId = executor,
            DurationHours = hours,
            StartedAt = startedAt,
            ParentId = parent,
            // фактическим порядком раскладывается только СДАННОЕ (T-192-S0)
            Settled = settled,
        };

    private static readonly DateTime Day = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Task_Started_Last_Stands_After_The_Finished_Ones_Of_The_Same_Executor()
    {
        // порядок списка — очередь иерархии: у прогона приоритет выше, поэтому он первый,
        // но НАЧАЛИ его последним, когда две другие задачи исполнителя уже сделаны
        var run = Node("run", "mrclaude", 2, Day.AddHours(6));
        var first = Node("a", "mrclaude", 3, Day, settled: true);
        var second = Node("b", "mrclaude", 3, Day.AddHours(3), settled: true);
        var nodes = new List<TaskDiagramNodeDto> { run, first, second };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, first.StartHours);
        Assert.Equal(3, second.StartHours);
        Assert.Equal(6, run.StartHours);
    }

    [Fact]
    public void Not_Started_Tasks_Keep_The_Queue_Order_And_Go_After_The_Started_Ones()
    {
        var done = Node("done", "mrclaude", 2, Day, settled: true);
        var planned1 = Node("p1", "mrclaude", 1);
        var planned2 = Node("p2", "mrclaude", 1);
        // в списке плановые стоят ПЕРЕД сделанной — так их ставит очередь по приоритету
        var nodes = new List<TaskDiagramNodeDto> { planned1, planned2, done };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, done.StartHours);
        Assert.Equal(2, planned1.StartHours);
        Assert.Equal(3, planned2.StartHours);
    }

    [Fact]
    public void Parent_Still_Starts_After_Its_Children_Whatever_The_Real_Start_Was()
    {
        // родитель начался раньше всех по-настоящему, но очередь выполняет его последним —
        // топология сильнее фактического времени, иначе диаграмма перестала бы быть очередью
        var parent = Node("p", "mrclaude", 1, Day);
        var child = Node("c", "helper", 4, Day.AddHours(2), parent: "p");
        var nodes = new List<TaskDiagramNodeDto> { child, parent };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, child.StartHours);
        Assert.Equal(4, parent.StartHours);
    }

    [Fact]
    public void Executors_Are_Independent_Of_Each_Other()
    {
        var mine = Node("m", "mrclaude", 2, Day.AddHours(5));
        var theirs = Node("t", "helper", 2, Day);
        var nodes = new List<TaskDiagramNodeDto> { mine, theirs };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, mine.StartHours);
        Assert.Equal(0, theirs.StartHours);
    }
}
