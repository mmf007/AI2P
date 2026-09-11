using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-263 «Выключение запуска иерархии»: у запущенной иерархии не было выключателя.
///
/// Кнопка «остановить» снимала ТОЛЬКО текущее задание, и на задаче из открытой очереди это
/// ничего не давало: очередь поднимала задачу снова — завершением соседней подзадачи или
/// проходом сторожа раз в минуту. Теперь остановка закрывает и саму очередь (пометка
/// runHierarchy снимается с корня — тем же способом, что при отменённой блокирующей,
/// T-6-S1), а по второму исходу переспроса — снимает задания и у всех работающих задач
/// поддерева.
///
/// Второе из задания — ОСТАНОВКА С ДРУГОГО СЕРВЕРА, по образцу запуска (T-196): снять
/// чужое задание отсюда нечем, поэтому уезжает заявка (run_requests) своего вида, и
/// владелец выполняет её у себя тем же кодом. «Серверы» здесь работают с одной базой
/// (как в T196Tests): репликация проверяется живым прогоном двух настоящих серверов,
/// а тут — правила владения и разбор заявок.
/// </summary>
public sealed class T263Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и молчит (приём T-159/T-186):
    /// запущенное задание детерминированно висит в running.</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    public T263Tests()
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

    private static ServerScope Scope(string me, bool conductor) => new(
        () => me, () => me == ConductorId ? "S0" : "S1", () => conductor,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private ServerScope ConductorScope => Scope(ConductorId, conductor: true);

    private ServerScope OtherScope => Scope(OtherId, conductor: false);

    private TaskService Tasks(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    private RunRequestService Requests(ServerScope scope) => new(_f.Db, _f.Events, scope);

    private JobOrchestrator Orchestrator(ServerScope scope) =>
        new(Tasks(scope), _f.Jobs, _f.Executors, _f.Projects, _f.Teams, _f.Files, _f.Chat,
            _f.Connectors, _f.Security, _f.Picker, new ExperienceService(_f.Db, _f.Events, scope),
            () => scope.IsConductor, _f.TeamWork, Requests(scope));

    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """{ "skills": [ { "name": "code-write", "score": 90 } ] }""");
        var profileRel = $"models/profile_{nick}.json";
        var port = ((System.Net.IPEndPoint)_hang.LocalEndpoint).Port;
        _f.Files.WriteText(profileRel,
            $$"""{"provider":"openai-compatible","model":"m","baseUrl":"http://127.0.0.1:{{port}}","secretRef":""}""");
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
            ProfilePath = profileRel,
        }, null);
    }

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null,
        int priority = 15) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = TaskStatuses.Pending,
            PriorityNum = priority,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    /// <summary>Задача сервера-дирижёра (сервер у неё не указан — правит только он).</summary>
    private TaskItem ConductorTask(string title, Executor? executor = null, string? parentId = null) =>
        Tasks(ConductorScope).Create(new TaskItem
        {
            Title = title,
            Status = TaskStatuses.Pending,
            ParentId = parentId,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    private bool Running(TaskItem task) => _f.Jobs.HasActive(task.Id);

    private bool QueueOpen(TaskItem root) =>
        TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag);

    // ---------- 1. остановка задачи закрывает очередь иерархии ----------

    [Fact]
    public async Task Stopping_A_Task_Of_An_Open_Hierarchy_Closes_The_Queue()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(Running(child));
        Assert.True(QueueOpen(root));

        var stop = await _f.Orchestrator.StopTaskAsync(child.Id, null, withHierarchy: true);

        Assert.False(Running(child));            // задание снято
        Assert.False(QueueOpen(root));           // и очередь закрыта — это и есть T-263
        Assert.True(stop.HierarchyStopped);
        Assert.Equal(_f.Tasks.Get(root.Id)!.DisplayId, stop.HierarchyRoot);
        Assert.Equal(1, stop.Jobs);
        Assert.Equal([_f.Tasks.Get(child.Id)!.DisplayId], stop.Stopped);
        Assert.False(stop.Nothing);
    }

    [Fact]
    public async Task After_The_Stop_The_Queue_Does_Not_Start_The_Task_Again()
    {
        // до T-263 остановка «не помогала»: сторож раз в минуту (RunHierarchiesOnce) и
        // завершение соседней подзадачи двигали очередь, и задача уходила в работу снова
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        await _f.Orchestrator.StopTaskAsync(child.Id, null, withHierarchy: true);
        _f.Orchestrator.RunHierarchiesOnce();
        var pass = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.False(Running(child));
        Assert.Empty(pass.Started);
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(child.Id)!.Status);
    }

    [Fact]
    public async Task Stopping_Without_The_Hierarchy_Keeps_The_Queue_Open()
    {
        // прежнее поведение никуда не делось: «остановить только это задание» — это выбор
        // человека в переспросе, и очередь тогда остаётся открытой
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        var stop = await _f.Orchestrator.StopTaskAsync(child.Id, null);

        Assert.Equal(1, stop.Jobs);
        Assert.False(stop.HierarchyStopped);
        Assert.Equal("", stop.HierarchyRoot);
        Assert.True(QueueOpen(root));
    }

    [Fact]
    public async Task The_Stop_Clears_The_Checkboxes_Of_The_Run()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null, withErrors: true, withNeedsFix: true);

        await _f.Orchestrator.StopTaskAsync(child.Id, null, withHierarchy: true);

        var stored = _f.Tasks.Get(root.Id)!;
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyFlag));
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyErrorsFlag));
        Assert.False(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.HierarchyNeedsFixFlag));
    }

    // ---------- 2. «и работающих потомков тоже» ----------

    [Fact]
    public async Task Queue_Only_Leaves_The_Running_Subtasks_Alone()
    {
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"));
        var b = CreateTask("B", root.Id, CreateAi("b"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(Running(a));
        Assert.True(Running(b));

        var stop = await _f.Orchestrator.StopTaskAsync(a.Id, null, withHierarchy: true);

        Assert.False(Running(a));
        Assert.True(Running(b)); // начатую работу второго агента не рвём
        Assert.Equal(1, stop.Jobs);
        Assert.False(QueueOpen(root));
    }

    [Fact]
    public async Task With_Children_Every_Running_Job_Of_The_Hierarchy_Is_Cancelled()
    {
        // корень → A → A1 и корень → B: снимается вся работающая иерархия, на любой глубине
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"));
        var a1 = CreateTask("A1", a.Id, CreateAi("a1"));
        var b = CreateTask("B", root.Id, CreateAi("b"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(Running(a1));
        Assert.True(Running(b));

        var stop = await _f.Orchestrator.StopTaskAsync(a1.Id, null,
            withHierarchy: true, withChildren: true);

        Assert.False(Running(a1));
        Assert.False(Running(b));
        Assert.False(Running(a));
        Assert.False(QueueOpen(root));
        Assert.Equal(2, stop.Jobs); // работали двое — A и корень ещё ждали своей очереди
        Assert.Contains(_f.Tasks.Get(a1.Id)!.DisplayId, stop.Stopped);
        Assert.Contains(_f.Tasks.Get(b.Id)!.DisplayId, stop.Stopped);
    }

    [Fact]
    public async Task Stopping_From_The_Root_Works_The_Same_Way()
    {
        // кнопка «остановить запуск иерархии» стоит у КОРНЯ: своего задания у него нет —
        // он ждёт подзадач, и без этой кнопки очередь нечем было бы закрыть вовсе
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.False(Running(root));

        var stop = await _f.Orchestrator.StopTaskAsync(root.Id, null,
            withHierarchy: true, withChildren: true);

        Assert.True(stop.HierarchyStopped);
        Assert.False(QueueOpen(root));
        Assert.False(Running(child));
    }

    [Fact]
    public async Task With_Children_Works_Even_If_The_Queue_Closed_By_Itself()
    {
        // очередь могла закрыться сама, пока человек думал над переспросом: просьба
        // «остановить всё» обязана всё равно снять работающих потомков задачи
        var root = CreateTask("корень", executor: CreateAi("root"));
        var child = CreateTask("подзадача", root.Id, CreateAi("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.SetHierarchyRun(root.Id, false);
        Assert.True(Running(child));

        var stop = await _f.Orchestrator.StopTaskAsync(root.Id, null,
            withHierarchy: true, withChildren: true);

        Assert.False(Running(child));
        Assert.Equal(1, stop.Jobs);
        Assert.False(stop.HierarchyStopped); // закрывать было уже нечего
    }

    [Fact]
    public async Task Nothing_To_Stop_Is_Said_Plainly()
    {
        var task = CreateTask("одинокая", executor: CreateAi("a"));

        var stop = await _f.Orchestrator.StopTaskAsync(task.Id, null, withHierarchy: true);

        Assert.True(stop.Nothing);
        Assert.Equal(0, stop.Jobs);
        Assert.False(stop.HierarchyStopped);
        Assert.Empty(stop.Requested);
    }

    [Fact]
    public async Task The_Root_Of_The_Open_Hierarchy_Is_Named_For_The_Card()
    {
        // карточка задачи берёт корень отсюда: по нему кнопка «остановить» решает,
        // переспрашивать ли про всю иерархию
        var root = CreateTask("корень", executor: CreateAi("root"));
        var a = CreateTask("A", root.Id, CreateAi("a"));
        var deep = CreateTask("A1", a.Id, CreateAi("a1"));
        var alone = CreateTask("вне иерархии", executor: CreateAi("x"));

        Assert.Null(_f.Orchestrator.HierarchyRootOf(_f.Tasks.Get(deep.Id)!));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(root.Id, _f.Orchestrator.HierarchyRootOf(_f.Tasks.Get(deep.Id)!)?.Id);
        Assert.Equal(root.Id, _f.Orchestrator.HierarchyRootOf(_f.Tasks.Get(root.Id)!)?.Id);
        Assert.Null(_f.Orchestrator.HierarchyRootOf(_f.Tasks.Get(alone.Id)!));
    }

    // ---------- 3. остановка С ДРУГОГО СЕРВЕРА (по аналогии с запуском, T-196) ----------

    [Fact]
    public async Task Stopping_A_Foreign_Task_Files_A_Request()
    {
        var task = ConductorTask("чужая для S1", CreateAi("bot"));
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);
        Assert.True(Running(task));

        var stop = await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);

        // здесь снять нечем: задание идёт у владельца — уехала заявка
        Assert.True(Running(task));
        Assert.Equal(0, stop.Jobs);
        Assert.Equal([Tasks(OtherScope).Get(task.Id)!.DisplayId], stop.Requested);
        // с T-1-S0 сервер есть и у задач дирижёра, поэтому заявка называет его прямо;
        // подпись «дирижёр организации» осталась запасной — для задач прежних версий,
        // у которых сервер не проставлен (иначе человек прочитал бы «остановка передана: »
        // с пустым местом)
        Assert.Equal("S0 (главный)", stop.TargetServer);
        var request = Requests(OtherScope).ListByTask(task.Id).Single();
        Assert.Equal(RunRequestKinds.Stop, request.Kind);
        // владелец строки — АВТОР заявки: правит и убирает её только он (ТЗ гл. 6)
        Assert.Equal(OtherId, request.ServerId);
    }

    [Fact]
    public async Task A_Foreign_Hierarchy_Is_Stopped_By_Its_Own_Kind_Of_Request()
    {
        var root = ConductorTask("корень", CreateAi("root"));
        var child = ConductorTask("подзадача", CreateAi("a"), root.Id);
        await Orchestrator(ConductorScope).StartHierarchyAsync(root.Id, null);
        Assert.True(Running(child));

        var stop = await Orchestrator(OtherScope).StopTaskAsync(child.Id, null,
            withHierarchy: true, withChildren: true);

        // очередь ведёт сервер корня — с чужого сервера туда уходит заявка, а сама
        // очередь и задания остаются нетронутыми до её разбора
        Assert.True(QueueOpen(root));
        Assert.True(Running(child));
        Assert.Equal(RunRequestKinds.StopHierarchyAll,
            Requests(OtherScope).ListByTask(root.Id).Single().Kind);
        Assert.Equal(RunRequestKinds.Stop,
            Requests(OtherScope).ListByTask(child.Id).Single().Kind);
        Assert.Equal(2, stop.Requested.Count);
    }

    [Fact]
    public async Task Without_Children_The_Request_Kind_Is_The_Queue_Only_One()
    {
        var root = ConductorTask("корень", CreateAi("root"));
        var child = ConductorTask("подзадача", CreateAi("a"), root.Id);
        await Orchestrator(ConductorScope).StartHierarchyAsync(root.Id, null);

        await Orchestrator(OtherScope).StopTaskAsync(child.Id, null, withHierarchy: true);

        Assert.Equal(RunRequestKinds.StopHierarchy,
            Requests(OtherScope).ListByTask(root.Id).Single().Kind);
    }

    /// <summary>
    /// ПОВТОРНОЕ НАЖАТИЕ ПОДАЁТ ЗАЯВКУ ЗАНОВО (T-160-S0), а не «отказывает, потому что такая
    /// уже есть»: прежняя заявка на том конце может быть давно отработана — отметка об этом
    /// лежит у ВЛАДЕЛЬЦА и автору не видна. Строка при этом остаётся одна: старая снимается.
    /// </summary>
    [Fact]
    public async Task A_Second_Stop_Press_Files_A_Fresh_Request()
    {
        var task = ConductorTask("чужая для S1", CreateAi("bot"));
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);

        await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);
        var first = Requests(OtherScope).ListByTask(task.Id).Single();
        var again = await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);

        Assert.False(again.AlreadyRequested);
        Assert.Single(again.Requested);
        var second = Requests(OtherScope).ListByTask(task.Id).Single();
        Assert.NotEqual(first.Id, second.Id);   // новый идентификатор — новое поручение
    }

    [Fact]
    public async Task The_Owner_Applies_A_Stop_Request()
    {
        var root = ConductorTask("корень", CreateAi("root"));
        var child = ConductorTask("подзадача", CreateAi("a"), root.Id);
        await Orchestrator(ConductorScope).StartHierarchyAsync(root.Id, null);
        await Orchestrator(OtherScope).StopTaskAsync(child.Id, null,
            withHierarchy: true, withChildren: true);

        // заявка «доехала» — владелец разбирает её у себя проходом сторожа
        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        Assert.False(QueueOpen(root));
        Assert.False(Running(child));
    }

    [Fact]
    public async Task A_Stop_Request_Is_Applied_Only_Once()
    {
        var task = ConductorTask("чужая для S1", CreateAi("bot"));
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);
        await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);

        var conductor = Orchestrator(ConductorScope);
        conductor.ApplyRunRequestsOnce();
        // задачу запустили снова уже ПОСЛЕ остановки: повторный разбор той же заявки
        // не должен снимать новое задание — отметка об исполнении своя у сервера
        await conductor.StartTaskAsync(task.Id, null);
        conductor.ApplyRunRequestsOnce();

        Assert.True(Running(task));
    }

    [Fact]
    public async Task The_Author_Removes_The_Stop_Request_When_It_Is_Done()
    {
        var task = ConductorTask("чужая для S1", CreateAi("bot"));
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);
        await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);
        Assert.Single(Requests(OtherScope).ListByTask(task.Id));

        // пока задание идёт, заявка ждёт своего часа
        Assert.Equal(0, Requests(OtherScope).Cleanup());
        Assert.Single(Requests(OtherScope).ListByTask(task.Id));

        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        Assert.Equal(1, Requests(OtherScope).Cleanup());
        Assert.Empty(Requests(OtherScope).ListByTask(task.Id));
    }

    [Fact]
    public async Task A_Stop_Hierarchy_Request_Waits_For_The_Queue_To_Close()
    {
        var root = ConductorTask("корень", CreateAi("root"));
        ConductorTask("подзадача", CreateAi("a"), root.Id);
        await Orchestrator(ConductorScope).StartHierarchyAsync(root.Id, null);
        await Orchestrator(OtherScope).StopTaskAsync(root.Id, null, withHierarchy: true);

        // очередь ещё открыта — заявку убирать рано
        Assert.Equal(0, Requests(OtherScope).Cleanup());

        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        Assert.Equal(1, Requests(OtherScope).Cleanup());
        Assert.False(QueueOpen(root));
    }

    [Fact]
    public async Task Stop_Requests_Live_In_Their_Own_Replicated_Table()
    {
        // отдельная таблица — не косметика: сервер ПРЕЖНЕЙ версии разбирает run_requests по
        // правилу «вид не „иерархия“ — запустить задачу», то есть заявку на остановку он
        // ЗАПУСТИЛ БЫ. Строку незнакомой таблицы приёмник журнала пропускает молча
        var task = ConductorTask("чужая для S1", CreateAi("bot"));
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);
        await Orchestrator(OtherScope).StopTaskAsync(task.Id, null);

        using var conn = _f.Db.Open();
        Assert.Equal(0, Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM run_requests"));
        Assert.Equal(1, Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM stop_requests"));
        Assert.Equal(RunRequestKinds.Stop,
            Sql.Scalar<string>(conn, null, "SELECT kind FROM stop_requests"));
        // и она РЕПЛИЦИРУЕТСЯ: без этого поручение не доехало бы до владельца задачи
        Assert.Contains("stop_requests", ChangeLog.OrgTables);
        Assert.Equal(1, Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM changes WHERE tbl='stop_requests'"));
    }

    [Fact]
    public void The_Kinds_Of_Request_Are_Told_Apart()
    {
        Assert.True(RunRequestKinds.IsStop(RunRequestKinds.Stop));
        Assert.True(RunRequestKinds.IsStop(RunRequestKinds.StopHierarchy));
        Assert.True(RunRequestKinds.IsStop(RunRequestKinds.StopHierarchyAll));
        Assert.False(RunRequestKinds.IsStop(RunRequestKinds.Task));
        Assert.False(RunRequestKinds.IsStop(RunRequestKinds.Hierarchy));
        // строка от сервера более старой версии (вида, которого мы не знаем) — это запуск
        Assert.Equal(RunRequestKinds.Task, RunRequestKinds.Normalize("что-то новое"));
        Assert.Equal(RunRequestKinds.StopHierarchy,
            RunRequestKinds.Normalize(RunRequestKinds.StopHierarchy));
    }
}
