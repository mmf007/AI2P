using AI2P.Connectors;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo_bugfix_3: исполнитель, выключенный в справочнике после включения в команду
/// или назначения на задачу, — при запуске работы команды показывается «не активен»
/// и не подключается; задачи с ним не запускаются (ни вручную, ни автозапуском).
/// </summary>
public sealed class TodoBugfix3Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Deactivate(Executor executor)
    {
        executor.IsActive = false;
        return _f.Executors.Update(executor, null);
    }

    [Fact]
    public void TeamWork_Inactive_Members_Are_Reported_And_Not_Connected()
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members = [new TeamMember { ExecutorId = human.Id }, new TeamMember { ExecutorId = ai.Id }],
        }, null);
        Deactivate(human);
        Deactivate(ai);
        var work = new TeamWorkService(_f.Teams, _f.Executors, _f.Projects, _f.Events, _f.Connectors,
            new LocalModelProcessService());

        // и до запуска, и после него оба участника — «не активен» (кирпично-красный в UI);
        // подключение неактивного ИИ не выполняется (иначе был бы Connecting/Error)
        var status = work.Status(team.Id, human.Id);
        Assert.All(status.Members, m => Assert.Equal(ExecutorWorkState.Inactive, m.State));

        status = work.Start(team.Id, human.Id, human.Id);
        Assert.True(status.IsRunning);
        Assert.All(status.Members, m => Assert.Equal(ExecutorWorkState.Inactive, m.State));
    }

    [Fact]
    public async Task StartTask_Throws_For_Inactive_Executor()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ExecutorIds = [human.Id],
        }, "сделай", "", null);
        Deactivate(human);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        Assert.Contains("не активен", ex.Message);
        Assert.Equal(TaskStatuses.Draft, _f.Tasks.Get(task.Id)!.Status);
        Assert.Empty(_f.Jobs.ListByTask(task.Id));
    }

    [Fact]
    public async Task AutoStart_Skips_Children_With_Inactive_Executor()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" },
            "", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            ParentId = parent.Id,
            Title = "Потомок",
            ExecutorIds = [human.Id],
            LaunchJson = """{ "mode": "auto" }""",
        }, "сделай", "", null);
        Deactivate(human);

        await _f.Orchestrator.AutoStartChildrenAsync(parent, null);

        // активный человек перешёл бы в in_progress; неактивный — пропущен
        Assert.Equal(TaskStatuses.Draft, _f.Tasks.Get(child.Id)!.Status);
    }
}
