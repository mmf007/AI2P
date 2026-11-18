using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-769-S0 «Задача и другой сервер».
/// 1) Исполнитель другого сервера назначается задаче этого сервера без отказа — форма и
///    инструменты только предупреждают, что запуск здесь невозможен (дальше задачу передают).
/// 2) У агента два новых действия: move_task_to_server (передать задачу) и run_task_on_server
///    (вызвать её на сервере-владельце; в открытой очереди иерархии вызов не нужен).
/// </summary>
public sealed class T769S0Tests : IDisposable
{
    private const string OtherId = "srv-1";

    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static ServerScope LocalScope => new(() => "local", () => "S0", () => true,
        id => id == OtherId ? "S1" : id == "local" ? "S0" : "", id => id == OtherId ? "ноутбук" : "");

    private TaskService Tasks() => new(_f.Db, _f.Events, _f.Files, LocalScope);

    private readonly List<string> _runRequests = [];

    private Executor Agent() =>
        _f.Executors.Create(new Executor { Nick = "агент", Kind = ExecutorKind.Ai }, null);

    private AgentToolset Toolset(TaskService tasks, TaskItem current, Executor actor, bool cluster = true) =>
        new(null,
            new TaskToolset(tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, actor.Id)
            {
                ResolveServer = cluster ? name => name.Equals("S1", StringComparison.OrdinalIgnoreCase) ? OtherId : null : null,
                RequestRemoteRun = cluster
                    ? (taskId, _) => { _runRequests.Add(taskId); return new RunRequest { TaskId = taskId }; }
                    : null,
            });

    private TaskItem Create(TaskService tasks, string title, string? parentId = null) =>
        tasks.Create(new TaskItem { Title = title, ParentId = parentId }, "текст", "", null);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ---------- 1. исполнитель другого сервера — предупреждение, а не отказ ----------

    [Fact]
    public void A_Local_Model_Executor_Of_Another_Server_Is_Saved_With_A_Warning()
    {
        var model = _f.Models.Create(new AiModel { Name = "Локальная" }, null);
        _f.Files.WriteText(model.ProfilePath, """{"provider":"openai","launchCommand":"llama-server"}""");
        _f.Models.SyncLocalFlags();
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "локальный-ии", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        Assert.Equal("local", _f.Executors.Get(executor.Id)!.ServerId);

        var other = new TaskService(_f.Db, _f.Events, _f.Files,
            new ServerScope(() => OtherId, () => "S1", () => false));
        var task = other.Create(new TaskItem { Title = "чужая", ExecutorIds = [executor.Id] }, "", "", null);
        // и правка той же задачи с тем же исполнителем — тоже без отказа
        var saved = other.Update(other.Get(task.Id)!, "", "", null);

        Assert.Contains(executor.Id, saved.ExecutorIds);
        var warning = other.ForeignExecutorWarning(saved);
        Assert.NotNull(warning);
        Assert.Contains("локальный-ии", warning);
    }

    [Fact]
    public void A_Cloud_Executor_Gives_No_Warning()
    {
        var agent = Agent();
        var task = _f.Tasks.Create(new TaskItem { Title = "задача", ExecutorIds = [agent.Id] }, "", "", null);
        Assert.Null(_f.Tasks.ForeignExecutorWarning(task));
    }

    // ---------- 2. move_task_to_server ----------

    [Fact]
    public async Task Move_Task_To_Server_Hands_The_Task_Over()
    {
        var tasks = Tasks();
        var agent = Agent();
        var current = Create(tasks, "текущая");
        var target = Create(tasks, "передаваемая");
        var code = tasks.Get(target.Id)!.DisplayId;

        var answer = await Toolset(tasks, current, agent).ExecuteAsync("move_task_to_server",
            Args($$"""{"code": "{{code}}", "server": "s1"}"""), CancellationToken.None);

        Assert.Equal(OtherId, tasks.Get(target.Id)!.ServerId);
        Assert.Contains(code, answer);
    }

    [Fact]
    public async Task Move_Task_To_Server_Refuses_Unknown_Server_And_Own_Task()
    {
        var tasks = Tasks();
        var agent = Agent();
        var current = Create(tasks, "текущая");
        var target = Create(tasks, "передаваемая");
        var tools = Toolset(tasks, current, agent);

        var unknown = await tools.ExecuteAsync("move_task_to_server",
            Args($$"""{"code": "{{tasks.Get(target.Id)!.DisplayId}}", "server": "S9"}"""), CancellationToken.None);
        var own = await tools.ExecuteAsync("move_task_to_server",
            Args($$"""{"code": "{{tasks.Get(current.Id)!.DisplayId}}", "server": "S1"}"""), CancellationToken.None);

        Assert.Contains("S9", unknown);
        Assert.Equal("local", tasks.Get(target.Id)!.ServerId);
        Assert.Equal("local", tasks.Get(current.Id)!.ServerId);
        Assert.Contains("свою задачу", own, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 3. run_task_on_server ----------

    [Fact]
    public async Task Run_Task_On_Server_Files_A_Run_Request_For_A_Foreign_Task()
    {
        var tasks = Tasks();
        var agent = Agent();
        var current = Create(tasks, "текущая");
        var target = Create(tasks, "на другом сервере");
        tasks.ChangeServer(target.Id, OtherId, null);
        var tools = Toolset(tasks, current, agent);

        var answer = await tools.ExecuteAsync("run_task_on_server",
            Args($$"""{"code": "{{tasks.Get(target.Id)!.DisplayId}}"}"""), CancellationToken.None);

        Assert.Equal([target.Id], _runRequests);
        Assert.Contains("S1", answer);
    }

    [Fact]
    public async Task Run_Task_On_Server_Does_Nothing_Inside_An_Open_Hierarchy()
    {
        var tasks = Tasks();
        var agent = Agent();
        var root = Create(tasks, "корень");
        var child = Create(tasks, "потомок", root.Id);
        var current = Create(tasks, "текущая");
        tasks.SetHierarchyRun(root.Id, true);
        tasks.ChangeServer(child.Id, OtherId, null);

        var answer = await Toolset(tasks, current, agent).ExecuteAsync("run_task_on_server",
            Args($$"""{"code": "{{tasks.Get(child.Id)!.DisplayId}}"}"""), CancellationToken.None);

        // в очереди иерархии задача запустится сама в свой черёд
        Assert.Empty(_runRequests);
        Assert.Contains(tasks.Get(root.Id)!.DisplayId, answer);
    }

    [Fact]
    public async Task Run_Task_On_Server_Refuses_A_Task_Of_This_Server()
    {
        var tasks = Tasks();
        var agent = Agent();
        var current = Create(tasks, "текущая");
        var target = Create(tasks, "здешняя");

        var answer = await Toolset(tasks, current, agent).ExecuteAsync("run_task_on_server",
            Args($$"""{"code": "{{tasks.Get(target.Id)!.DisplayId}}"}"""), CancellationToken.None);

        Assert.Empty(_runRequests);
        Assert.Contains("move_task_to_server", answer);
    }

    // ---------- 4. публикация и справочник ----------

    [Fact]
    public void Tools_Are_Published_Only_In_A_Cluster()
    {
        var tasks = Tasks();
        var agent = Agent();
        var current = Create(tasks, "текущая");

        var with = Toolset(tasks, current, agent).Specs.Select(s => s.Name).ToList();
        var without = Toolset(tasks, current, agent, cluster: false).Specs.Select(s => s.Name).ToList();

        Assert.Contains("move_task_to_server", with);
        Assert.Contains("run_task_on_server", with);
        Assert.DoesNotContain("move_task_to_server", without);
        Assert.DoesNotContain("run_task_on_server", without);
    }

    [Fact]
    public void Both_Actions_Have_Catalog_Entries_In_Every_Language()
    {
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            var actions = _f.Actions.List(lang);
            Assert.Equal("move_task_to_server",
                actions.Single(a => a.Code == "AI2P.Tasks.ChangeServer").ToolName);
            Assert.Equal("run_task_on_server",
                actions.Single(a => a.Code == "AI2P.Tasks.RunRemote").ToolName);
        }
        Assert.Equal("AI2P.Tasks.ChangeServer", _f.Actions.CodeByTool("move_task_to_server"));
        Assert.Equal("AI2P.Tasks.RunRemote", _f.Actions.CodeByTool("run_task_on_server"));
    }
}
