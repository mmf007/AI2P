using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-6-S1 «Проверка блокирующей задачи» (версия 1.88): поведение блокирующих задач внутри
/// запуска иерархии (T-159).
/// <list type="number">
/// <item>задачи, ждущие блокирующую, очередь иерархии не запускает — ни когда блокирующая
/// лежит в том же поддереве, ни когда она вне иерархии;</item>
/// <item>блокирующую задачу на ЧЕЛОВЕКЕ очередь переводит в работу только тогда, когда та
/// лежит в иерархии общего родителя; блокирующую вне иерархии не трогает никто;</item>
/// <item>перевод блокирующей в «готово» руками сразу запускает всех, кто её ждал, —
/// и в том случае, когда сама блокирующая иерархии не принадлежит;</item>
/// <item>перевод блокирующей в «отмена» с T-299-S0 снова ОТПУСКАЕТ ждущие задачи, как
/// «готово» (в 1.88–1.137 держал их и останавливал весь иерархический запуск): иначе
/// отменённая ветка задачи-условия намертво стопорила бы очередь.</item>
/// </list>
/// </summary>
public sealed class T6S1Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает (приём T-159):
    /// запущенное задание детерминированно висит в running — исполнитель «занят».</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T6S1Tests()
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

    private Executor CreateHuman(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null,
        int priority = 15, List<string>? blockers = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = TaskStatuses.Pending,
            PriorityNum = priority,
            ExecutorIds = executor is null ? [] : [executor.Id],
            BlockerIds = blockers ?? [],
        }, "текст", "", null);

    private bool HasJob(TaskItem task) => _f.Jobs.ListByTask(task.Id).Count > 0;

    private string Status(TaskItem task) => _f.Tasks.Get(task.Id)!.Status;

    private bool HierarchyOpen(TaskItem root) =>
        TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag);

    /// <summary>Автозапуски идут фоном (Task.Run в JobOrchestrator): ждём результата, а не
    /// читаем сразу — иначе проверка меряет состояние до срабатывания.</summary>
    private async Task<bool> WaitFor(Func<bool> condition, int seconds = 10)
    {
        for (var i = 0; i < seconds * 20; i++)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(50);
        }
        return condition();
    }

    // ---------- 1. состояние блокирующих ----------

    [Fact]
    public void A_Cancelled_Blocker_Is_A_Finished_One()
    {
        // в 1.88–1.137 (T-6-S1) отмена была отдельным исходом и держала ждущую задачу;
        // с T-299-S0 блокирующая завершена при «готово» ИЛИ «отменена»
        var done = CreateTask("готовая");
        var cancelled = CreateTask("отменённая");
        var running = CreateTask("в работе");

        var waits = CreateTask("ждёт", blockers: [done.Id, running.Id]);
        Assert.Equal(BlockersState.Waiting, _f.Tasks.BlockersStateOf(_f.Tasks.Get(waits.Id)!));

        _f.Tasks.ChangeStatus(done.Id, TaskStatuses.Done, null);
        _f.Tasks.ChangeStatus(running.Id, TaskStatuses.Done, null);
        Assert.Equal(BlockersState.Done, _f.Tasks.BlockersStateOf(_f.Tasks.Get(waits.Id)!));
        Assert.True(_f.Tasks.BlockersDone(_f.Tasks.Get(waits.Id)!));

        var blocked = CreateTask("отпущена отменой", blockers: [done.Id, cancelled.Id]);
        Assert.Equal(BlockersState.Waiting, _f.Tasks.BlockersStateOf(_f.Tasks.Get(blocked.Id)!));
        _f.Tasks.ChangeStatus(cancelled.Id, TaskStatuses.Cancelled, null);
        Assert.Equal(BlockersState.Done, _f.Tasks.BlockersStateOf(_f.Tasks.Get(blocked.Id)!));
        Assert.False(_f.Tasks.BlockersCancelled(_f.Tasks.Get(blocked.Id)!));
        Assert.True(_f.Tasks.BlockersDone(_f.Tasks.Get(blocked.Id)!));
    }

    // ---------- 2. очередь иерархии и блокирующая ВНЕ иерархии ----------

    [Fact]
    public async Task Tasks_Waiting_For_An_Outside_Blocker_Do_Not_Start()
    {
        // блокирующая — задача ЧЕЛОВЕКА вне иерархии: очередь её не трогает вовсе,
        // а обе ждущие подзадачи и корень остаются в «ожидает»
        var human = CreateHuman("человек");
        var blocker = CreateTask("блокирующая вне иерархии", executor: human);
        var root = CreateTask("корень", executor: CreateAi("root"));
        var first = CreateTask("первая", root.Id, CreateAi("a"), priority: 25, blockers: [blocker.Id]);
        var second = CreateTask("вторая", root.Id, CreateAi("b"), priority: 5, blockers: [blocker.Id]);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Empty(run.Started);
        Assert.Equal(3, run.Waiting); // обе подзадачи и корень
        Assert.False(HasJob(first));
        Assert.False(HasJob(second));
        Assert.False(HasJob(root));
        // блокирующая вне иерархии в работу НЕ переводится: очередь до неё не доходит
        Assert.Equal(TaskStatuses.Pending, Status(blocker));
        Assert.False(HasJob(blocker));
        Assert.True(HierarchyOpen(root)); // очередь открыта: ждём человека
    }

    [Fact]
    public async Task A_Blocker_Set_To_Done_By_Hand_Starts_Everyone_Who_Waited()
    {
        // САМОЕ ГЛАВНОЕ (T-6-S1): человек доделал блокирующую задачу вне иерархии и поставил
        // «готово» — обе ждавшие подзадачи уходят в работу сами, без прохода сторожа
        var human = CreateHuman("человек");
        var blocker = CreateTask("блокирующая вне иерархии", executor: human);
        var root = CreateTask("корень", executor: CreateAi("root"));
        var first = CreateTask("первая", root.Id, CreateAi("a"), priority: 25, blockers: [blocker.Id]);
        var second = CreateTask("вторая", root.Id, CreateAi("b"), priority: 5, blockers: [blocker.Id]);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.False(HasJob(first));

        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Done, null);

        Assert.True(await WaitFor(() => HasJob(first) && HasJob(second)),
            "ждавшие блокирующую подзадачи должны запуститься сразу после её завершения");
        Assert.True(HierarchyOpen(root)); // корень дождётся своей очереди последним
    }

    [Fact]
    public async Task A_Cancelled_Blocker_Starts_Everyone_Who_Waited()
    {
        // T-299-S0 (было T-6-S1 «отмена останавливает очередь»): отмена блокирующей — тоже
        // завершение, ждавшие задачи уходят в работу, очередь иерархии остаётся открытой
        var human = CreateHuman("человек");
        var blocker = CreateTask("блокирующая вне иерархии", executor: human);
        var root = CreateTask("корень", executor: CreateAi("root"));
        var first = CreateTask("первая", root.Id, CreateAi("a"), priority: 25, blockers: [blocker.Id]);
        var second = CreateTask("вторая", root.Id, CreateAi("b"), priority: 5, blockers: [blocker.Id]);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(HierarchyOpen(root));

        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Cancelled, null);

        Assert.True(await WaitFor(() => HasJob(first) && HasJob(second)),
            "отменённая блокирующая обязана отпускать ждавшие задачи");
        Assert.True(HierarchyOpen(root));
    }

    // ---------- 3. блокирующая ВНУТРИ иерархии ----------

    [Fact]
    public async Task A_Human_Blocker_Inside_The_Hierarchy_Goes_In_Progress()
    {
        // блокирующая — подзадача той же иерархии и стоит на человеке: очередь доходит до
        // неё и переводит в работу, а ждущая подзадача и корень ждут
        var human = CreateHuman("человек");
        var root = CreateTask("корень", executor: CreateAi("root"));
        var blocker = CreateTask("блокирующая", root.Id, human, priority: 30);
        var waits = CreateTask("ждущая", root.Id, CreateAi("a"), priority: 20,
            blockers: [blocker.Id]);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Contains(blocker.DisplayId, run.Started);
        Assert.Equal(TaskStatuses.InProgress, Status(blocker));
        Assert.False(HasJob(waits));
        Assert.Equal(TaskStatuses.Pending, Status(waits));

        // человек доделал свою задачу — ждавшая подзадача уходит в работу сама
        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Done, null);
        Assert.True(await WaitFor(() => HasJob(waits)),
            "после завершения блокирующей подзадачи ждущая обязана запуститься");
    }

    [Fact]
    public async Task A_Cancelled_Blocker_Inside_The_Hierarchy_Lets_The_Waiting_Task_Go()
    {
        var human = CreateHuman("человек");
        var root = CreateTask("корень", executor: CreateAi("root"));
        var blocker = CreateTask("блокирующая", root.Id, human, priority: 30);
        var waits = CreateTask("ждущая", root.Id, CreateAi("a"), priority: 20,
            blockers: [blocker.Id]);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.Equal(TaskStatuses.InProgress, Status(blocker));

        // человек решил, что делать эту работу не будет
        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Cancelled, null);

        // T-299-S0: отменённая блокирующая — завершённая, ждущая подзадача идёт в работу
        Assert.True(await WaitFor(() => HasJob(waits)),
            "после отмены блокирующей подзадачи ждущая обязана запуститься");
        Assert.True(HierarchyOpen(root));
    }

    [Fact]
    public async Task A_Blocker_Cancelled_Before_The_Run_Does_Not_Stop_It()
    {
        // блокирующую отменили ДО нажатия кнопки: с T-299-S0 это не тупик — очередь
        // открывается и запускает ждущую задачу
        var blocker = CreateTask("отменённая", executor: CreateHuman("человек"));
        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Cancelled, null);
        var root = CreateTask("корень", executor: CreateAi("root"));
        var waits = CreateTask("ждущая", root.Id, CreateAi("a"), blockers: [blocker.Id]);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.False(run.Stopped);
        Assert.Empty(run.StoppedBy);
        Assert.Contains(waits.DisplayId, run.Started);
        Assert.True(HierarchyOpen(root));
    }

    // ---------- 4. автозапуск потомков вне иерархии ----------

    [Fact]
    public async Task Auto_Start_Of_Children_Treats_A_Cancelled_Blocker_As_Finished()
    {
        // автозапуск потомков (todo22) — второй путь, где блокирующие проверяются: с T-299-S0
        // отменённая блокирующая потомка отпускает, как и «готово»
        var parent = CreateTask("родитель", executor: CreateAi("p"));
        var blocker = CreateTask("блокирующая", executor: CreateHuman("человек"));
        var child = _f.Tasks.Create(new TaskItem
        {
            Title = "потомок",
            ParentId = parent.Id,
            Status = TaskStatuses.Pending,
            ExecutorIds = [CreateAi("c").Id],
            BlockerIds = [blocker.Id],
            LaunchJson = """{"mode":"auto"}""",
        }, "текст", "", null);

        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Cancelled, null);
        _f.Tasks.ChangeStatus(parent.Id, TaskStatuses.Done, null);

        Assert.True(await WaitFor(() => HasJob(child)),
            "отменённая блокирующая не должна держать автозапуск потомка (T-299-S0)");
    }

    [Fact]
    public async Task A_Blocked_Task_Outside_Any_Hierarchy_Starts_By_Auto_Mode()
    {
        // задача с режимом запуска «автоматически» вне иерархии работает как прежде:
        // блокирующая переведена в «готово» — задача стартует
        var blocker = CreateTask("блокирующая", executor: CreateHuman("человек"));
        var auto = _f.Tasks.Create(new TaskItem
        {
            Title = "автоматическая",
            Status = TaskStatuses.Pending,
            ExecutorIds = [CreateAi("a").Id],
            BlockerIds = [blocker.Id],
            LaunchJson = """{"mode":"auto"}""",
        }, "текст", "", null);

        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Done, null);

        Assert.True(await WaitFor(() => HasJob(auto)),
            "автоматическая задача обязана запуститься по завершении блокирующей");
    }

    // ---------- 5. пометка причины для человека ----------

    [Fact]
    public void The_Pause_Mark_Names_The_Blockers()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(AiPauseKinds.Blocked,
            AiPause.Of(false, "", null, 0, now, blockers: BlockersState.Waiting)!.Kind);
        Assert.Equal(AiPauseKinds.BlockerCancelled,
            AiPause.Of(false, "", null, 0, now, blockers: BlockersState.Cancelled)!.Kind);
        // блокирующие сильнее очереди иерархии: очередь стоит именно из-за них
        Assert.Equal(AiPauseKinds.Blocked,
            AiPause.Of(false, "", null, 0, now, hierarchyRun: true,
                blockers: BlockersState.Waiting)!.Kind);
        // лимит по-прежнему сильнее всего: он говорит, КОГДА работа продолжится сама
        Assert.Equal(AiPauseKinds.Limit,
            AiPause.Of(false, "", now.AddHours(1), 0, now, blockers: BlockersState.Cancelled)!.Kind);
        // блокирующих нет — пометки нет
        Assert.Null(AiPause.Of(false, "", null, 0, now));
    }
}
