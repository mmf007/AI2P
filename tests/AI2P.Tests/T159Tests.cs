using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-159 «Иерархический запуск задачи»: кнопка запускает всё поддерево — потомки по
/// убыванию приоритета, у потомка с потомками сначала они, выполненные (проверка/готово/
/// отменено) пропускаются, занятый исполнитель дожидается освобождения, корневой родитель
/// запускается последним.
/// </summary>
public sealed class T159Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает: фоновый вызов
    /// модели детерминированно висит, задание остаётся running — так исполнитель «занят»
    /// (недостижимый IP не годится: на части сетей соединение отбивается мгновенно).</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T159Tests()
    {
        _hang = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        _hang.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    _hangClients.Add(await _hang.AcceptTcpClientAsync());
                }
            }
            catch (Exception)
            {
                // листенер остановлен в Dispose
            }
        });
    }

    public void Dispose()
    {
        _hang.Stop();
        foreach (var client in _hangClients)
        {
            client.Dispose();
        }
        _f.Dispose();
    }

    // ---------- обвязка ----------

    private string HangingProfile =>
        $$"""{"provider":"openai-compatible","model":"m","baseUrl":"http://127.0.0.1:{{((System.Net.IPEndPoint)_hang.LocalEndpoint).Port}}","secretRef":""}""";

    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """{ "skills": [ { "name": "code-write", "score": 90 } ] }""");
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel, HangingProfile);
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
            ProfilePath = profileRel,
        }, null);
    }

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null,
        int priority = 15, string status = TaskStatuses.Pending) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = status,
            PriorityNum = priority,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    private bool HasJob(TaskItem task) => _f.Jobs.ListByTask(task.Id).Count > 0;

    // ---------- очередь запуска ----------

    [Fact]
    public async Task Deepest_And_Most_Important_Go_First_And_Root_Goes_Last()
    {
        // корень → A (приоритет 25, со своей подзадачей A1) и B (приоритет 5);
        // исполнители разные — задания раздаются сразу всем свободным
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"), priority: 25);
        var a1 = CreateTask("A1", a.Id, CreateAi("a1"), priority: 15);
        var b = CreateTask("B", root.Id, CreateAi("b"), priority: 5);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        // запущены только листья: A ждёт свою подзадачу, корень — обоих потомков
        Assert.True(HasJob(a1));
        Assert.True(HasJob(b));
        Assert.False(HasJob(a));
        Assert.False(HasJob(root));
        Assert.Equal(2, run.Started.Count);
        Assert.Equal(2, run.Waiting); // A и корень ждут потомков
        Assert.Equal(4, run.Total);
        Assert.False(run.Finished);
        // корень помечен: очередь продолжится сама при завершении каждой подзадачи
        Assert.True(TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag));
    }

    [Fact]
    public async Task A_Busy_Executor_Makes_The_Next_Task_Wait()
    {
        // один исполнитель на обе подзадачи: уходит приоритетная, вторая ждёт освобождения
        var ai = CreateAi("bot");
        var root = CreateTask("корень", executor: ai);
        var low = CreateTask("низкий", root.Id, ai, priority: 5);
        var high = CreateTask("высокий", root.Id, ai, priority: 25);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal([_f.Tasks.Get(high.Id)!.DisplayId], run.Started);
        Assert.False(HasJob(low));
        Assert.False(HasJob(root));
        Assert.Equal(2, run.Waiting); // низкий и корень
    }

    [Fact]
    public async Task Completed_Subtasks_Are_Skipped_And_The_Root_Starts_Last()
    {
        // все подзадачи выполнены (проверка / готово / отменено) — запускается сам корень
        var root = CreateTask("корень", executor: CreateAi("root"));
        CreateTask("проверка", root.Id, CreateAi("a"), status: TaskStatuses.Review);
        CreateTask("готово", root.Id, CreateAi("b"), status: TaskStatuses.Done);
        CreateTask("отменено", root.Id, CreateAi("c"), status: TaskStatuses.Cancelled);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal([_f.Tasks.Get(root.Id)!.DisplayId], run.Started);
        Assert.Equal(3, run.Skipped);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(root.Id)!.Status);
    }

    [Fact]
    public async Task The_Queue_Moves_On_When_A_Subtask_Is_Finished()
    {
        var ai = CreateAi("bot");
        var root = CreateTask("корень", executor: ai);
        var first = CreateTask("первая", root.Id, ai, priority: 25);
        var second = CreateTask("вторая", root.Id, ai, priority: 5);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(HasJob(first));
        Assert.False(HasJob(second));

        // первая выполнена и исполнитель освободился — очередь двигает следующую
        _f.Jobs.SetState(_f.Jobs.ListByTask(first.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(first.Id, TaskStatuses.Review, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);
        Assert.True(HasJob(second));
        Assert.False(HasJob(root)); // корень всё ещё ждёт вторую

        // вторая выполнена — последним уходит в работу корень
        _f.Jobs.SetState(_f.Jobs.ListByTask(second.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(second.Id, TaskStatuses.Review, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);
        Assert.True(HasJob(root));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(root.Id)!.Status);
    }

    [Fact]
    public async Task The_Run_Is_Closed_When_There_Is_Nothing_Left()
    {
        var root = CreateTask("корень", executor: CreateAi("root"), status: TaskStatuses.Done);
        CreateTask("готовая", root.Id, CreateAi("a"), status: TaskStatuses.Done);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Empty(run.Started);
        Assert.True(run.Finished);
        // пометка снята — сторож больше не ходит по этой иерархии
        Assert.False(TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag));
    }

    [Fact]
    public async Task A_Task_Without_Subtasks_Is_Refused()
    {
        var single = CreateTask("одиночка", executor: CreateAi("bot"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartHierarchyAsync(single.Id, null));

        Assert.Contains("нет подзадач", ex.Message);
        Assert.False(TaskService.HasLaunchFlag(_f.Tasks.Get(single.Id)!.LaunchJson, TaskService.HierarchyFlag));
    }

    [Fact]
    public async Task A_Deferred_Or_Blocked_Subtask_Only_Waits()
    {
        // блокирующая задача не завершена (ТЗ v1.35) — подзадача не запускается, но и не
        // пропускается: корень ждёт её, а не уходит в работу вперёд неё
        var blocker = CreateTask("блокирующая", executor: CreateAi("x"));
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = _f.Tasks.Create(new TaskItem
        {
            Title = "заблокированная",
            ParentId = root.Id,
            Status = TaskStatuses.Pending,
            ExecutorIds = [CreateAi("a").Id],
            BlockerIds = [blocker.Id],
        }, "текст", "", null);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.False(HasJob(child));
        Assert.False(HasJob(root));
        Assert.Empty(run.Started);
        Assert.Equal(2, run.Waiting);
        Assert.False(run.Finished);
    }

    [Fact]
    public async Task A_Security_Rule_Stops_The_Hierarchy_Launch()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        _f.Security.Create(new SecurityRule
        {
            Scope = SecurityScope.Global,
            Target = SecurityTarget.Action,
            Permission = SecurityPermission.Deny,
            Pattern = JobOrchestrator.AutoStartActionCode,
        }, null);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.False(HasJob(child));
        Assert.False(HasJob(root));
        Assert.Empty(run.Started);
        Assert.Equal(1, run.Skipped); // подзадача — запуск запрещён правилом
        Assert.Equal(1, run.Waiting); // корень так и ждёт её: вперёд подзадач он не идёт
    }

    [Fact]
    public async Task The_Watchdog_Pass_Picks_Up_An_Open_Hierarchy()
    {
        var ai = CreateAi("bot");
        var root = CreateTask("корень", executor: CreateAi("root"));
        var busy = CreateTask("занятая", executor: ai); // задача ВНЕ иерархии
        var child = CreateTask("подзадача", root.Id, ai);
        await _f.Orchestrator.StartTaskAsync(busy.Id, null); // исполнитель занят

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.False(HasJob(child)); // ждёт освобождения исполнителя

        // исполнитель освободился на задаче ИЗ ДРУГОЙ иерархии — событие завершения сюда
        // не приходит, подзадачу трогает с места проход сторожа
        _f.Jobs.SetState(_f.Jobs.ListByTask(busy.Id)[0].Id, JobState.Done, null, null);
        _f.Orchestrator.RunHierarchiesOnce();

        Assert.True(HasJob(child));
    }

    // ---------- кнопка и подписи ----------

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

    [Fact]
    public void The_Card_Has_A_Hierarchy_Button_With_A_Triple_Arrow_Icon()
    {
        var card = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "TaskCardView.razor"));

        // кнопка есть только у задачи с подзадачами, иконка — три стрелки вправо (T-209)
        Assert.Contains("_subtasks.Count > 0", card);
        Assert.Contains("Ai2pIcons.TripleArrowRight", card);
        Assert.Contains("StartHierarchyAsync", card);
        Assert.Contains("data-task-hierarchy=\"1\"", card); // опора живой проверки в браузере
    }

    [Theory]
    [InlineData("task.startHierarchy")]
    [InlineData("task.startHierarchy.confirm")]
    [InlineData("task.startHierarchy.yes")]
    [InlineData("task.startHierarchy.started")]
    [InlineData("task.startHierarchy.waiting")]
    public void Hierarchy_Labels_Exist_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустая подпись {key}");
        }
    }

    [Fact]
    public void The_Api_Publishes_The_Hierarchy_Start_Route()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        Assert.Contains("/tasks/{id}/start-hierarchy", endpoints);
        Assert.Contains("StartHierarchyAsync", endpoints);
    }
}
