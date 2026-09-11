using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-210 «Запуск иерархии задач: запускать и задачи в доработке»: в переспросе запуска
/// иерархии появилась ВТОРАЯ галочка — запускать ли задачи в состоянии «доработка»
/// (needs_fix). Она устроена ровно как галочка T-186 «и вставшие с ошибкой»: действует на
/// ВСЁ поддерево (такие подзадачи на любой глубине), тратит на каждую задачу одну попытку
/// (вернули в доработку снова — очередь оставляет её человеку), а выбор человека живёт
/// у корня очереди, потому что дальше очередь идёт сама и переспросить в ней негде.
/// </summary>
public sealed class T210Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает (приём T-159):
    /// запущенное задание детерминированно висит в running — исполнитель «занят».</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T210Tests()
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

    // ---------- галочка «и задачи в доработке» ----------

    [Fact]
    public async Task Without_The_Checkbox_A_NeedsFix_Subtask_Is_Not_Touched()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var fix = CreateTask("в доработке", root.Id, CreateAi("a"), status: TaskStatuses.NeedsFix);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(0, Jobs(fix)); // прежнее поведение (T-159): очередь ждёт человека
        Assert.Empty(run.Started);
        Assert.Equal(0, run.Reworked);
        Assert.Equal(2, run.Waiting); // задача в доработке и корень, который её ждёт
        Assert.Equal(TaskStatuses.NeedsFix, _f.Tasks.Get(fix.Id)!.Status);
    }

    [Fact]
    public async Task With_The_Checkbox_NeedsFix_Subtasks_Start_At_Any_Depth()
    {
        // корень → A (доработка) → A1 (доработка глубже): «соответственно запускать и такие
        // подзадачи тоже» — обе уходят в работу, каждая своим исполнителем
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"), status: TaskStatuses.NeedsFix);
        var a1 = CreateTask("A1", a.Id, CreateAi("a1"), status: TaskStatuses.NeedsFix);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);

        Assert.Equal(1, Jobs(a1)); // самая глубокая пошла первой
        Assert.Equal(0, Jobs(a));  // A ждёт свою подзадачу — порядок очереди не сломан
        Assert.Equal([_f.Tasks.Get(a1.Id)!.DisplayId], run.Started);
        Assert.Equal(1, run.Reworked);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(a1.Id)!.Status);

        // A1 доделана — следующим проходом запускается A из доработки. Счётчики этого прохода
        // не проверяем: смена статуса подзадачи сама двигает очередь фоном (StatusChanged)
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
        CreateTask("в доработке", root.Id, CreateAi("a"), status: TaskStatuses.NeedsFix);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);

        // очередь идёт сама (завершение подзадачи, сторож раз в минуту) — переспросить
        // человека там негде, поэтому выбор хранится у корня рядом с флагом очереди
        var stored = _f.Tasks.Get(root.Id)!;
        Assert.True(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyFlag));
        Assert.True(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyNeedsFixFlag));
        // вторая галочка не включает первую: вставшие с ошибкой человек не просил
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyErrorsFlag));
    }

    [Fact]
    public async Task A_NeedsFix_Task_Is_Started_Once_Per_Run()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var fix = CreateTask("в доработке", root.Id, CreateAi("a"), status: TaskStatuses.NeedsFix);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);
        Assert.Equal(1, Jobs(fix));

        // задачу вернули в доработку СНОВА: завершение задания само двигает очередь, и без
        // защиты она крутила бы запуски по кругу
        _f.Jobs.SetState(_f.Jobs.ListByTask(fix.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(fix.Id, TaskStatuses.NeedsFix, null);
        var again = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(1, Jobs(fix)); // второй раз очередь её не трогает
        Assert.Equal(0, again.Reworked);
        Assert.Equal(2, again.Waiting); // задача и корень — ждут человека

        // человек нажал кнопку ещё раз — пометки сняты, попытка снова есть
        var manual = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);

        Assert.Equal(2, Jobs(fix));
        Assert.Equal(1, manual.Reworked);
    }

    [Fact]
    public async Task The_Watchdog_Pass_Starts_A_NeedsFix_Task_Too()
    {
        // исполнитель занят задачей ВНЕ иерархии: запуск откладывается, а трогает задачу
        // с места проход сторожа — он читает выбор человека у корня
        var ai = CreateAi("bot");
        var root = CreateTask("корень", executor: CreateAi("root"));
        var busy = CreateTask("занятая", executor: ai);
        var fix = CreateTask("в доработке", root.Id, ai, status: TaskStatuses.NeedsFix);
        await _f.Orchestrator.StartTaskAsync(busy.Id, null);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);
        Assert.Equal(0, Jobs(fix));
        Assert.Equal(0, run.Reworked);
        // попытка не потрачена: до запуска дело не дошло
        Assert.False(TaskService.HasLaunchFlag(_f.Tasks.Get(fix.Id)!.LaunchJson,
            TaskService.HierarchyNeedsFixRetryFlag));

        _f.Jobs.SetState(_f.Jobs.ListByTask(busy.Id)[0].Id, JobState.Done, null, null);
        _f.Orchestrator.RunHierarchiesOnce();

        Assert.Equal(1, Jobs(fix));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(fix.Id)!.Status);
    }

    [Fact]
    public async Task A_Closed_Run_Forgets_The_Choice()
    {
        // запускать нечего — очередь закрылась и сняла с корня пометки, в том числе новую
        var root = CreateTask("корень", executor: CreateAi("root"), status: TaskStatuses.Done);
        CreateTask("готовая", root.Id, CreateAi("a"), status: TaskStatuses.Done);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);

        Assert.True(run.Finished);
        var stored = _f.Tasks.Get(root.Id)!;
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyFlag));
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyNeedsFixFlag));
    }

    [Fact]
    public async Task Both_Checkboxes_Work_Together()
    {
        // галочки независимы: одна поднимает упавшую, вторая — отправленную в доработку
        var root = CreateTask("корень", executor: CreateAi("root"));
        var failed = CreateTask("упавшая", root.Id, CreateAi("a"), priority: 20,
            status: TaskStatuses.Error);
        var fix = CreateTask("в доработке", root.Id, CreateAi("b"), priority: 10,
            status: TaskStatuses.NeedsFix);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null,
            withErrors: true, withNeedsFix: true);

        Assert.Equal(1, Jobs(failed));
        Assert.Equal(1, Jobs(fix));
        Assert.Equal(1, run.Restarted);
        Assert.Equal(1, run.Reworked);
        Assert.Equal(2, run.Started.Count);
    }

    [Fact]
    public async Task Only_The_Asked_Marks_Are_Cleared_By_A_New_Press()
    {
        // нажали кнопку ТОЛЬКО со второй галочкой: потраченная попытка упавшей задачи
        // остаётся потраченной — человек про неё не просил
        var root = CreateTask("корень", executor: CreateAi("root"));
        var failed = CreateTask("упавшая", root.Id, CreateAi("a"), status: TaskStatuses.Error);
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true);
        _f.Jobs.SetState(_f.Jobs.ListByTask(failed.Id)[0].Id, JobState.Failed, null, "сломалось");
        _f.Tasks.ChangeStatus(failed.Id, TaskStatuses.Error, null);
        Assert.True(TaskService.HasLaunchFlag(_f.Tasks.Get(failed.Id)!.LaunchJson,
            TaskService.HierarchyRetryFlag));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withNeedsFix: true);

        Assert.True(TaskService.HasLaunchFlag(_f.Tasks.Get(failed.Id)!.LaunchJson,
            TaskService.HierarchyRetryFlag));
        Assert.Equal(1, Jobs(failed)); // и запускать её никто не стал: галочка ошибок снята
    }

    // ---------- заявка на чужой сервер несёт тот же выбор ----------

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    private static ServerScope Scope(string me, bool conductor) => new(
        () => me, () => me == ConductorId ? "S0" : "S1", () => conductor,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    [Fact]
    public void A_Request_To_Another_Server_Carries_The_Choice()
    {
        // запуск иерархии с ЧУЖОГО сервера (T-196) — это строка-заявка: выбор человека
        // обязан доехать вместе с ней, иначе владелец запустит иерархию без доработок
        var conductor = Scope(ConductorId, conductor: true);
        var other = Scope(OtherId, conductor: false);
        var task = new TaskService(_f.Db, _f.Events, _f.Files, conductor).Create(
            new TaskItem { Title = "чужая для S1", Status = TaskStatuses.Pending }, "текст", "", null);

        var requests = new RunRequestService(_f.Db, _f.Events, other);
        var request = requests.Add(task, RunRequestKinds.Hierarchy, withErrors: false,
            withNeedsFix: true, actorId: null);

        Assert.True(request.WithNeedsFix);
        Assert.False(request.WithErrors);
        // и читается обратно из базы: колонка with_needs_fix едет партнёру журналом изменений
        var stored = requests.ListByTask(task.Id).Single();
        Assert.True(stored.WithNeedsFix);
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
    public void The_Confirmation_Has_A_Second_Checkbox()
    {
        var card = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "TaskCardView.razor"));

        Assert.Contains("_hierarchyWithNeedsFix", card);
        Assert.Contains("task.startHierarchy.withNeedsFix", card);
        Assert.Contains("data-task-hierarchy-needsfix=\"1\"", card); // опора живой проверки
        Assert.Contains("StartHierarchyAsync(TaskId, _hierarchyWithErrors, _hierarchyWithNeedsFix)", card);
        // галочка спрашивается заново при каждом нажатии кнопки
        Assert.Contains("_hierarchyWithNeedsFix = false;", card);
    }

    [Fact]
    public void The_Api_Carries_The_Flag()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Services", "ApiClient.cs"));

        Assert.Contains("bool? withNeedsFix", endpoints);
        Assert.Contains("withNeedsFix ?? false", endpoints);
        // минимальные API разбирают bool? через bool.TryParse: withNeedsFix=1 дал бы 400
        Assert.Contains("withNeedsFix={(withNeedsFix ? \"true\" : \"false\")}", client);
    }

    [Theory]
    [InlineData("task.startHierarchy.withNeedsFix")]
    [InlineData("task.startHierarchy.withNeedsFix.hint")]
    [InlineData("task.startHierarchy.reworked")]
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
