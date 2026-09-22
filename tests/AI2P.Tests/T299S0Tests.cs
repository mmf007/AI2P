using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-299-S0 «Выполнение иерархии: условие, цикл до, цикл после, блокирующие». Исполнители —
/// люди: очередь переводит их задачи в работу без заданий агента, поэтому сценарий идёт
/// детерминированно — решение «агента» пишется напрямую (<see cref="TaskService.SetFlowDecision"/>),
/// а конец задания изображается переводом в «готово».
/// </summary>
public sealed class T299S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Human(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private TaskItem Create(string title, string? parentId = null, int priority = 15,
        TaskFlowDto? flow = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = TaskStatuses.Pending,
            PriorityNum = priority,
            ExecutorIds = [Human(title + Guid.NewGuid().ToString("N")[..6]).Id],
            LaunchJson = flow is null ? "{}" : TaskFlow.Write("{}", flow),
        }, "текст", "", null);

    private void SetFlow(TaskItem task, TaskFlowDto flow) =>
        _f.Tasks.EditFlowRun(task.Id, root =>
        {
            var written = System.Text.Json.Nodes.JsonNode.Parse(TaskFlow.Write(root.ToJsonString(), flow))!.AsObject();
            foreach (var key in written.Select(p => p.Key).ToList())
            {
                root[key] = written[key]!.DeepClone();
            }
        });

    private string Status(TaskItem task) => _f.Tasks.Get(task.Id)!.Status;

    private bool Open(TaskItem root) =>
        TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag);

    // ---------- состояние в launch_json ----------

    [Fact]
    public void The_Decision_Is_Strictly_Boolean()
    {
        Assert.Null(TaskFlowRun.Decision("{}"));
        Assert.Null(TaskFlowRun.Decision("""{"flowDecision":"true"}"""));
        Assert.Null(TaskFlowRun.Decision("испорчено"));
        Assert.True(TaskFlowRun.Decision("""{"flowDecision":true}"""));
        Assert.False(TaskFlowRun.Decision("""{"flowDecision":false}"""));
        Assert.Equal(0, TaskFlowRun.LoopPass("""{"loopPass":"много"}"""));
    }

    [Fact]
    public void A_Linear_Task_Never_Loops()
    {
        Assert.Equal(1, TaskFlowRun.LoopLimit(new TaskFlowDto { Type = TaskFlow.Linear, RecheckLimit = 7 }, 3));
        Assert.Equal(7, TaskFlowRun.LoopLimit(new TaskFlowDto { Type = TaskFlow.Loop, RecheckLimit = 7 }, 3));
        Assert.Equal(3, TaskFlowRun.LoopLimit(new TaskFlowDto { Type = TaskFlow.DoLoop }, 3));
    }

    // ---------- условие ----------

    [Fact]
    public async Task A_Condition_Runs_First_Then_Cancels_The_Branch_Not_Taken()
    {
        var root = Create("корень");
        var cond = Create("условие", root.Id);
        var yes = Create("да", cond.Id);
        var yesChild = Create("да-потомок", yes.Id);
        var no = Create("нет", cond.Id);
        var noChild = Create("нет-потомок", no.Id);
        SetFlow(cond, new TaskFlowDto { Type = TaskFlow.If, IfTrueTaskId = yes.Id, IfFalseTaskId = no.Id });

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        // условие запущено сразу, в потомков очередь не провалилась
        Assert.Contains(cond.DisplayId, run.Started);
        Assert.Equal(TaskStatuses.Pending, Status(yesChild));
        Assert.Equal(TaskStatuses.Pending, Status(noChild));

        _f.Tasks.SetFlowDecision(cond.Id, false);
        _f.Tasks.ChangeStatus(cond.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(TaskStatuses.Cancelled, Status(yes));
        Assert.Equal(TaskStatuses.Cancelled, Status(yesChild));
        Assert.Equal(TaskStatuses.InProgress, Status(noChild)); // выбранная ветка пошла штатно
        Assert.True(Open(root));
    }

    [Fact]
    public async Task A_Condition_Can_Stop_The_Whole_Hierarchy()
    {
        var root = Create("корень");
        var cond = Create("условие", root.Id);
        var other = Create("соседняя", root.Id, priority: 1);
        SetFlow(cond, new TaskFlowDto { Type = TaskFlow.If, IfTrueStopHierarchy = true });

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.SetFlowDecision(cond.Id, true);
        _f.Tasks.ChangeStatus(cond.Id, TaskStatuses.Done, null);
        _f.Tasks.ChangeStatus(other.Id, TaskStatuses.Done, null);
        var run = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.True(run.Stopped || !Open(root));
        Assert.False(Open(root));
        Assert.Equal(TaskStatuses.Pending, Status(root));
    }

    [Fact]
    public async Task A_Condition_Without_A_Decision_Stops_Instead_Of_Guessing()
    {
        var root = Create("корень");
        var cond = Create("условие", root.Id);
        var yes = Create("да", cond.Id);
        SetFlow(cond, new TaskFlowDto { Type = TaskFlow.If, IfTrueTaskId = yes.Id });

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.ChangeStatus(cond.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.False(Open(root));
        Assert.Equal(TaskStatuses.Pending, Status(yes));
    }

    // ---------- цикл до ----------

    [Fact]
    public async Task A_Loop_Failing_On_The_First_Pass_Cancels_Waiting_Children()
    {
        var root = Create("корень");
        var loop = Create("цикл", root.Id, flow: new TaskFlowDto { Type = TaskFlow.Loop, RecheckLimit = 3 });
        var body = Create("тело", loop.Id);
        var bodyChild = Create("тело-потомок", body.Id);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.Contains(loop.DisplayId, run.Started);
        Assert.Equal(TaskStatuses.Pending, Status(body));

        _f.Tasks.SetFlowDecision(loop.Id, false);
        _f.Tasks.ChangeStatus(loop.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(TaskStatuses.Cancelled, Status(body));
        Assert.Equal(TaskStatuses.Cancelled, Status(bodyChild));
    }

    [Fact]
    public async Task A_Loop_Pauses_Runs_Its_Body_And_Stops_At_The_Limit()
    {
        var root = Create("корень");
        var loop = Create("цикл", root.Id, flow: new TaskFlowDto
        {
            Type = TaskFlow.Loop, RecheckLimit = 1, LoopStopHierarchy = true,
        });
        var body = Create("тело", loop.Id);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.SetFlowDecision(loop.Id, true);
        _f.Tasks.ChangeStatus(loop.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        // условия выполнены: анализатор на паузе «ждёт окончания цикла», тело пошло
        Assert.Equal(TaskStatuses.Paused, Status(loop));
        Assert.True(TaskFlowRun.InLoopBody(_f.Tasks.Get(loop.Id)!.LaunchJson));
        Assert.Equal(TaskStatuses.InProgress, Status(body));

        // круг окончен, предел 1 исчерпан, флажок остановки стоит — иерархия закрыта
        _f.Tasks.ChangeStatus(body.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(1, TaskFlowRun.LoopPass(_f.Tasks.Get(loop.Id)!.LaunchJson));
        Assert.Equal(TaskStatuses.Done, Status(loop));
        Assert.False(Open(root));
    }

    // ---------- цикл после ----------

    [Fact]
    public async Task A_Do_Loop_Runs_Children_First_And_Restarts_Them()
    {
        var root = Create("корень");
        var loop = Create("цикл после", root.Id, flow: new TaskFlowDto { Type = TaskFlow.DoLoop, RecheckLimit = 5 });
        var body = Create("тело", loop.Id);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.Contains(body.DisplayId, run.Started);
        Assert.Equal(TaskStatuses.Pending, Status(loop));

        _f.Tasks.ChangeStatus(body.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);
        Assert.Equal(TaskStatuses.InProgress, Status(loop)); // анализатор после потомков

        _f.Tasks.SetFlowDecision(loop.Id, true);
        _f.Tasks.ChangeStatus(loop.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        // новый круг: тело снова в работе, анализатор ждёт окончания цикла
        Assert.Equal(TaskStatuses.InProgress, Status(body));
        Assert.Equal(TaskStatuses.Paused, Status(loop));
        Assert.Equal(1, TaskFlowRun.LoopPass(_f.Tasks.Get(loop.Id)!.LaunchJson));
    }
}
