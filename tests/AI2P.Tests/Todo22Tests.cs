using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Автозапуск потомков при выполнении родителя (todo22, подготовка к этапу 3):
/// переход родителя в review / needs_fix / done запускает потомков с типом запуска «auto».
/// </summary>
public sealed class AutoStartTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem CreateChild(string parentId, string executorId, string launchJson,
        string status = TaskStatuses.Pending) =>
        _f.Tasks.Create(new TaskItem
        {
            ParentId = parentId,
            Title = "Потомок",
            Status = status,
            LaunchJson = launchJson,
            ExecutorIds = [executorId],
        }, "описание", "", null);

    [Fact]
    public void StatusChanged_Event_Fires_After_Commit()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        (TaskItem Task, string Old)? fired = null;
        _f.Tasks.StatusChanged += (t, old, _) => fired = (t, old);

        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Review, null);

        Assert.NotNull(fired);
        Assert.Equal(task.Id, fired!.Value.Task.Id);
        Assert.Equal(TaskStatuses.Draft, fired.Value.Old);
        Assert.Equal(TaskStatuses.Review, fired.Value.Task.Status);
    }

    [Fact]
    public async Task Auto_Child_With_Human_Executor_Goes_InProgress()
    {
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var auto = CreateChild(parent.Id, human.Id, """{"mode":"auto"}""");
        var manual = CreateChild(parent.Id, human.Id, """{"mode":"manual"}""");

        // напрямую (без гонки с фоновым обработчиком события): родитель «выполнен»
        await _f.Orchestrator.AutoStartChildrenAsync(parent, null);

        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(auto.Id)!.Status);
        // тип запуска «вручную» — автозапуск не трогает
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(manual.Id)!.Status);
    }

    [Fact]
    public async Task Already_Started_Or_Done_Children_Are_Not_Restarted()
    {
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var done = CreateChild(parent.Id, human.Id, """{"mode":"auto"}""", TaskStatuses.Done);
        var inWork = CreateChild(parent.Id, human.Id, """{"mode":"auto"}""", TaskStatuses.InProgress);

        await _f.Orchestrator.AutoStartChildrenAsync(parent, null);

        Assert.Equal(TaskStatuses.Done, _f.Tasks.Get(done.Id)!.Status);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(inWork.Id)!.Status);
    }

    [Fact]
    public async Task Auto_Child_With_Ai_Executor_Gets_Job()
    {
        // ИИ-исполнитель с профайлом-заглушкой: задание создаётся, статус — in_progress;
        // сам вызов провайдера уходит в фон и здесь не проверяется
        var profileRel = "models/profile_test.json";
        _f.Files.WriteText(profileRel,
            """{"provider":"openai-compatible","model":"deepseek-chat","secretRef":"deepseek.apiKey"}""");
        File.WriteAllText(_f.Secrets.Path, """{"deepseek.apiKey":"test-key"}""");
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "bot",
            Kind = ExecutorKind.Ai,
            ProfilePath = profileRel,
        }, null);
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var child = CreateChild(parent.Id, ai.Id, """{"mode":"auto"}""");

        await _f.Orchestrator.AutoStartChildrenAsync(parent, null);

        Assert.NotEmpty(_f.Jobs.ListByTask(child.Id));
        // задача ушла в работу (in_progress; фоновый вызов провайдера может успеть перевести
        // её в error — ключа нет, это нормально для теста)
        Assert.NotEqual(TaskStatuses.Pending, _f.Tasks.Get(child.Id)!.Status);
    }
}
