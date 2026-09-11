using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-186 «Запуск иерархии задач: перезапускать вставшие с ошибкой»: в переспросе запуска
/// иерархии появилась галочка — запускать ли заново задачи в состоянии «встал с ошибкой».
/// Галочка действует на ВСЁ поддерево (такие подзадачи на любой глубине), на каждую задачу
/// тратится одна попытка (повторная ошибка оставляет её человеку), а выбор человека живёт
/// у корня очереди — его видят и проходы сторожа.
/// </summary>
public sealed class T186Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает (приём T-159):
    /// запущенное задание детерминированно висит в running — исполнитель «занят».</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T186Tests()
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

    private int Jobs(TaskItem task) => _f.Jobs.ListByTask(task.Id).Count;

    // ---------- галочка «и вставшие с ошибкой» ----------

    [Fact]
    public async Task Without_The_Checkbox_An_Errored_Subtask_Is_Not_Touched()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var failed = CreateTask("упавшая", root.Id, CreateAi("a"), status: TaskStatuses.Error);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(0, Jobs(failed)); // прежнее поведение (T-159): очередь ждёт человека
        Assert.Empty(run.Started);
        Assert.Equal(0, run.Restarted);
        Assert.Equal(2, run.Waiting); // упавшая и корень, который её ждёт
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(failed.Id)!.Status);
    }

    [Fact]
    public async Task With_The_Checkbox_Errored_Subtasks_Start_Again_At_Any_Depth()
    {
        // корень → A (упала) → A1 (упала глубже): «соответственно запускать и такие
        // подзадачи тоже» — обе уходят в работу, каждая своим исполнителем
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"), status: TaskStatuses.Error);
        var a1 = CreateTask("A1", a.Id, CreateAi("a1"), status: TaskStatuses.Error);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);

        Assert.Equal(1, Jobs(a1)); // самая глубокая пошла первой
        Assert.Equal(0, Jobs(a));  // A ждёт свою подзадачу — порядок очереди не сломан
        Assert.Equal([_f.Tasks.Get(a1.Id)!.DisplayId], run.Started);
        Assert.Equal(1, run.Restarted);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(a1.Id)!.Status);

        // A1 доделана — следующим проходом перезапускается упавшая A. Счётчики этого прохода
        // не проверяем: смена статуса подзадачи сама двигает очередь фоном (StatusChanged),
        // и запустить A мог как этот проход, так и фоновый — оба под общим замком
        _f.Jobs.SetState(_f.Jobs.ListByTask(a1.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(a1.Id, TaskStatuses.Review, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(1, Jobs(a));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(a.Id)!.Status);
    }

    [Fact]
    public async Task The_Choice_Is_Remembered_By_The_Root_Of_The_Run()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        CreateTask("упавшая", root.Id, CreateAi("a"), status: TaskStatuses.Error);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);

        // очередь идёт сама (завершение подзадачи, сторож раз в минуту) — переспросить
        // человека там негде, поэтому выбор хранится у корня рядом с флагом очереди
        var stored = _f.Tasks.Get(root.Id)!;
        Assert.True(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyFlag));
        Assert.True(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyErrorsFlag));
    }

    [Fact]
    public async Task An_Errored_Task_Is_Restarted_Once_Per_Run()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var failed = CreateTask("упавшая", root.Id, CreateAi("a"), status: TaskStatuses.Error);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);
        Assert.Equal(1, Jobs(failed));

        // задача упала СНОВА: завершение задания само двигает очередь, и без защиты она
        // крутила бы перезапуски по кругу
        _f.Jobs.SetState(_f.Jobs.ListByTask(failed.Id)[0].Id, JobState.Failed, null, "сломалось");
        _f.Tasks.ChangeStatus(failed.Id, TaskStatuses.Error, null);
        var again = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(1, Jobs(failed)); // второй раз очередь её не трогает
        Assert.Equal(0, again.Restarted);
        Assert.Equal(2, again.Waiting); // упавшая и корень — ждут человека

        // человек нажал кнопку ещё раз — пометки сняты, попытка снова есть
        var manual = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);

        Assert.Equal(2, Jobs(failed));
        Assert.Equal(1, manual.Restarted);
    }

    [Fact]
    public async Task The_Watchdog_Pass_Restarts_An_Errored_Task_Too()
    {
        // исполнитель занят задачей ВНЕ иерархии: перезапуск упавшей откладывается, а
        // трогает её с места проход сторожа — он читает выбор человека у корня
        var ai = CreateAi("bot");
        var root = CreateTask("корень", executor: CreateAi("root"));
        var busy = CreateTask("занятая", executor: ai);
        var failed = CreateTask("упавшая", root.Id, ai, status: TaskStatuses.Error);
        await _f.Orchestrator.StartTaskAsync(busy.Id, null);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);
        Assert.Equal(0, Jobs(failed));
        Assert.Equal(0, run.Restarted);
        // попытка не потрачена: до запуска дело не дошло
        Assert.False(TaskService.HasLaunchFlag(_f.Tasks.Get(failed.Id)!.LaunchJson,
            TaskService.HierarchyRetryFlag));

        _f.Jobs.SetState(_f.Jobs.ListByTask(busy.Id)[0].Id, JobState.Done, null, null);
        _f.Orchestrator.RunHierarchiesOnce();

        Assert.Equal(1, Jobs(failed));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(failed.Id)!.Status);
    }

    [Fact]
    public async Task A_Closed_Run_Forgets_The_Choice()
    {
        // запускать нечего — очередь закрылась и сняла с корня обе пометки
        var root = CreateTask("корень", executor: CreateAi("root"), status: TaskStatuses.Done);
        CreateTask("готовая", root.Id, CreateAi("a"), status: TaskStatuses.Done);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);

        Assert.True(run.Finished);
        var stored = _f.Tasks.Get(root.Id)!;
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyFlag));
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyErrorsFlag));
    }

    // ---------- переспрос, API и подписи ----------

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
    public void The_Confirmation_Has_A_Checkbox()
    {
        var card = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "TaskCardView.razor"));

        Assert.Contains("MudCheckBox", card);
        Assert.Contains("_hierarchyWithErrors", card);
        Assert.Contains("task.startHierarchy.withErrors", card);
        Assert.Contains("data-task-hierarchy-errors=\"1\"", card); // опора живой проверки в браузере
        // с T-210 у вызова появилась вторая галочка — проверяем начало строки вызова
        Assert.Contains("StartHierarchyAsync(TaskId, _hierarchyWithErrors", card);
    }

    [Fact]
    public void The_Api_Carries_The_Flag()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Services", "ApiClient.cs"));

        Assert.Contains("bool? withErrors", endpoints);
        Assert.Contains("withErrors ?? false", endpoints);
        // минимальные API разбирают bool? через bool.TryParse: withErrors=1 дал бы 400
        Assert.Contains("withErrors={(withErrors ? \"true\" : \"false\")}", client);
    }

    [Theory]
    [InlineData("task.startHierarchy.withErrors")]
    [InlineData("task.startHierarchy.withErrors.hint")]
    [InlineData("task.startHierarchy.restarted")]
    public void Checkbox_Labels_Exist_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустая подпись {key}");
        }
    }
}
