using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-171-S0 «Диаграмма». Жалоба заказчика: у одного исполнителя несколько задач с ОДИНАКОВЫМ
/// приоритетом, и та, которую уже взяли в работу, рисовалась правее тех, что ещё ждут очереди.
///
/// Причина: раскладка разводила задачи только фактическим началом (T-164-S0), а оно есть лишь
/// у задачи с заданием (<c>jobs.started_at</c>). Задача, стоящая «в работе» без задания (её
/// делает человек либо статус переставили руками), фактического начала не имеет и попадала в
/// общую кучу «плановых», где порядок задаёт очередь — при равном приоритете это порядок
/// создания. Правка — второй ключ сортировки: взятая в работу раньше ждущей.
///
/// Проверяется чистая функция <see cref="TaskDiagram.Layout"/> — базы данных ей не нужно.
/// </summary>
public class T171S0Tests
{
    private static TaskDiagramNodeDto Node(string id, string status,
        DateTime? startedAt = null, bool activeJob = false) => new()
        {
            Id = id,
            DisplayId = id,
            ExecutorId = "mrclaude",
            Status = status,
            DurationHours = 1,
            StartedAt = startedAt,
            HasActiveJob = activeJob,
            Settled = TaskStatuses.Settled(status),
        };

    private static readonly DateTime Day = new(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Task_In_Progress_Stands_Before_The_Waiting_Ones_Of_The_Same_Executor()
    {
        // порядок списка — очередь: приоритет у всех троих одинаковый, поэтому очередь
        // расставила их по времени создания, и идущая задача оказалась последней
        var waiting1 = Node("w1", TaskStatuses.Pending);
        var waiting2 = Node("w2", TaskStatuses.Pending);
        var running = Node("run", TaskStatuses.InProgress);
        var nodes = new List<TaskDiagramNodeDto> { waiting1, waiting2, running };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, running.StartHours);
        Assert.Equal(1, waiting1.StartHours);
        Assert.Equal(2, waiting2.StartHours);
    }

    [Fact]
    public void Paused_Task_And_A_Live_Job_Count_As_Taken_Into_Work_Too()
    {
        // «пауза» — это начатая и не законченная работа (агент ждёт ответа), а задачу с живым
        // заданием могли запустить, не успев переписать статус: обе идут раньше ждущей
        var waiting = Node("w", TaskStatuses.Pending);
        var paused = Node("p", TaskStatuses.Paused);
        var live = Node("j", TaskStatuses.NeedsFix, activeJob: true);
        var nodes = new List<TaskDiagramNodeDto> { waiting, paused, live };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, paused.StartHours);
        Assert.Equal(1, live.StartHours);
        Assert.Equal(2, waiting.StartHours);
    }

    [Fact]
    public void Real_Start_Is_Still_Stronger_Than_The_Status()
    {
        // четвёртое правило (T-164-S0) осталось главным для того, что уже СЛУЧИЛОСЬ: сданная
        // задача стоит там, где её сделали, и идущую вперёд себя не пропускает. С T-192-S0
        // это работает именно у сданной — незаконченная, даже однажды запускавшаяся, стоит
        // в порядке очереди
        var ranBefore = Node("ran", TaskStatuses.Done, Day);
        var running = Node("run", TaskStatuses.InProgress, Day.AddHours(1));
        var nodes = new List<TaskDiagramNodeDto> { running, ranBefore };

        TaskDiagram.Layout(nodes);

        Assert.Equal(0, ranBefore.StartHours);
        Assert.Equal(1, running.StartHours);
    }
}
