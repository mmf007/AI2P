using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-221 «Задача: несколько исполнителей». У задачи появился список «могут заменить
/// исполнителя»: если в момент запуска назначенный исполнитель занят другим заданием,
/// работу берёт первый СВОБОДНЫЙ из этого списка, а задача запоминает, кто её реально
/// ведёт (<see cref="TaskItem.ActualExecutorId"/>). Правило одно на все пути запуска —
/// кнопка, автозапуск потомков, очередь авторазбиения, очередь иерархии и отложенный
/// старт: все они идут через <see cref="JobOrchestrator.StartTaskAsync"/> и
/// <see cref="JobOrchestrator.RunExecutorFor"/>. Список копируется при создании задачи
/// из шаблона.
/// </summary>
public sealed class T221Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает (приём T-159):
    /// запущенное задание детерминированно висит в running — исполнитель «занят».</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T221Tests()
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
        IEnumerable<Executor>? alternates = null, int priority = 15,
        string status = TaskStatuses.Pending) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = status,
            PriorityNum = priority,
            ExecutorIds = executor is null ? [] : [executor.Id],
            AltExecutorIds = alternates?.Select(e => e.Id).ToList() ?? [],
        }, "текст", "", null);

    private int Jobs(TaskItem task) => _f.Jobs.ListByTask(task.Id).Count;

    /// <summary>Ник исполнителя задания задачи (у задачи оно одно).</summary>
    private string? JobExecutorNick(TaskItem task) =>
        _f.Jobs.ListByTask(task.Id).Select(j => _f.Executors.Get(j.ExecutorId)?.Nick).FirstOrDefault();

    /// <summary>Занять исполнителя настоящим заданием: своя задача, повисшая в running.</summary>
    private async Task<TaskItem> OccupyAsync(Executor executor)
    {
        var busy = CreateTask("занимаем " + executor.Nick, executor: executor);
        await _f.Orchestrator.StartTaskAsync(busy.Id, null);
        Assert.True(_f.Jobs.HasActiveByExecutor(executor.Id));
        return busy;
    }

    // ---------- хранение списка ----------

    [Fact]
    public void The_Alternates_List_Survives_Create_And_Update()
    {
        var main = CreateAi("main");
        var first = CreateAi("first");
        var second = CreateAi("second");

        // порядок в списке — порядок предпочтения, поэтому он и хранится
        var task = CreateTask("задача", executor: main, alternates: [second, first]);
        Assert.Equal([second.Id, first.Id], _f.Tasks.Get(task.Id)!.AltExecutorIds);

        var reloaded = _f.Tasks.Get(task.Id)!;
        reloaded.AltExecutorIds = [first.Id];
        _f.Tasks.Update(reloaded, "текст", "", null);
        Assert.Equal([first.Id], _f.Tasks.Get(task.Id)!.AltExecutorIds);
    }

    [Fact]
    public void The_Assigned_Executor_Is_Never_Kept_Among_The_Alternates()
    {
        // сам себя не заменяет: иначе «замена» была бы тем же исполнителем, и объяснить
        // человеку, кто взял работу, было бы нечем
        var main = CreateAi("main");
        var spare = CreateAi("spare");

        var task = CreateTask("задача", executor: main, alternates: [main, spare, spare]);

        Assert.Equal([spare.Id], _f.Tasks.Get(task.Id)!.AltExecutorIds);
    }

    // ---------- сам запуск ----------

    [Fact]
    public async Task A_Free_Alternate_Takes_The_Task_When_The_Assigned_One_Is_Busy()
    {
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);

        var task = CreateTask("задача", executor: main, alternates: [spare]);
        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        Assert.Equal("spare", JobExecutorNick(task));
        // назначенный исполнитель НЕ меняется — меняется только «кто реально ведёт»
        var saved = _f.Tasks.Get(task.Id)!;
        Assert.Equal([main.Id], saved.ExecutorIds);
        Assert.Equal(spare.Id, saved.ActualExecutorId);
    }

    [Fact]
    public async Task The_Assigned_Executor_Is_Preferred_While_He_Is_Free()
    {
        var main = CreateAi("main");
        var spare = CreateAi("spare");

        var task = CreateTask("задача", executor: main, alternates: [spare]);
        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        Assert.Equal("main", JobExecutorNick(task));
        Assert.Equal(main.Id, _f.Tasks.Get(task.Id)!.ActualExecutorId);
    }

    [Fact]
    public async Task The_First_Free_Alternate_Wins_And_Busy_Ones_Are_Skipped()
    {
        // порядок списка — порядок предпочтения: первый занят, берётся следующий свободный
        var main = CreateAi("main");
        var busySpare = CreateAi("busy-spare");
        var freeSpare = CreateAi("free-spare");
        await OccupyAsync(main);
        await OccupyAsync(busySpare);

        var task = CreateTask("задача", executor: main, alternates: [busySpare, freeSpare]);
        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        Assert.Equal("free-spare", JobExecutorNick(task));
    }

    [Fact]
    public async Task A_Disabled_Alternate_Is_Not_Used()
    {
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);
        var task = CreateTask("задача", executor: main, alternates: [spare]);

        spare.IsActive = false;
        _f.Executors.Update(spare, null);

        // выключенный запасной работу не возьмёт — поведение прежнее: отказ «исполнитель занят»
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        Assert.Equal(0, Jobs(task));
    }

    [Fact]
    public async Task Without_The_List_A_Busy_Executor_Still_Blocks_The_Start()
    {
        // прежнее поведение (ТЗ v1.26) сохранено дословно: список пуст — ждём своего
        var main = CreateAi("main");
        await OccupyAsync(main);
        var task = CreateTask("задача", executor: main);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        Assert.Equal(0, Jobs(task));
    }

    [Fact]
    public async Task All_Alternates_Busy_Means_The_Task_Waits_As_Before()
    {
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);
        await OccupyAsync(spare);

        var task = CreateTask("задача", executor: main, alternates: [spare]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        Assert.Equal(0, Jobs(task));
    }

    [Fact]
    public async Task The_Substitution_Is_Explained_In_The_Task_Chat()
    {
        // в списках задач стоит назначенный исполнитель, поэтому «почему работает другой»
        // человеку взять больше неоткуда
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);

        var task = CreateTask("задача", executor: main, alternates: [spare]);
        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        var messages = _f.Chat.ListByTask(task.Id);
        Assert.Contains(messages, m => m.Text.Contains("main") && m.Text.Contains("spare"));
    }

    // ---------- автоматические запуски ----------

    [Fact]
    public async Task The_Hierarchy_Queue_Uses_An_Alternate_Instead_Of_Waiting()
    {
        // T-159: очередь иерархии ждала освобождения занятого исполнителя. Теперь она
        // сначала смотрит список замены — ждать незачем, если есть кому работать
        var rootExec = CreateAi("root");
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);

        var root = CreateTask("корень", executor: rootExec);
        var child = CreateTask("подзадача", root.Id, main, alternates: [spare]);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(1, Jobs(child));
        Assert.Equal("spare", JobExecutorNick(child));
        Assert.Equal([_f.Tasks.Get(child.Id)!.DisplayId], run.Started);
        Assert.Equal(spare.Id, _f.Tasks.Get(child.Id)!.ActualExecutorId);
    }

    [Fact]
    public async Task The_Hierarchy_Queue_Still_Waits_When_There_Is_No_Free_Alternate()
    {
        var rootExec = CreateAi("root");
        var main = CreateAi("main");
        await OccupyAsync(main);

        var root = CreateTask("корень", executor: rootExec);
        var child = CreateTask("подзадача", root.Id, main);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(0, Jobs(child));
        Assert.Empty(run.Started);
        Assert.Equal(2, run.Waiting); // подзадача и корень, который её ждёт
    }

    [Fact]
    public async Task Auto_Start_Of_A_Child_Uses_An_Alternate_Too()
    {
        // «использовать при ЛЮБЫХ автоматических запусках»: автозапуск потомка завершённого
        // родителя идёт тем же StartTaskAsync, поэтому замена работает и здесь
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        await OccupyAsync(main);

        var parent = CreateTask("родитель", executor: CreateAi("parent"));
        var child = _f.Tasks.Create(new TaskItem
        {
            Title = "потомок",
            ParentId = parent.Id,
            Status = TaskStatuses.Pending,
            ExecutorIds = [main.Id],
            AltExecutorIds = [spare.Id],
            LaunchJson = """{"mode":"auto"}""",
        }, "текст", "", null);

        await _f.Orchestrator.AutoStartChildrenAsync(_f.Tasks.Get(parent.Id)!, null);

        Assert.Equal(1, Jobs(child));
        Assert.Equal("spare", JobExecutorNick(child));
    }

    [Fact]
    public async Task RunExecutorFor_Answers_Who_Would_Take_The_Task_Right_Now()
    {
        // общий ответ для всех очередей: пока назначенный свободен — он, занят — замена,
        // замены нет — никто (задача ждёт)
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        var withSpare = CreateTask("с заменой", executor: main, alternates: [spare]);
        var without = CreateTask("без замены", executor: main);

        Assert.Equal(main.Id, _f.Orchestrator.RunExecutorFor(_f.Tasks.Get(withSpare.Id)!)?.Id);

        await OccupyAsync(main);

        Assert.Equal(spare.Id, _f.Orchestrator.RunExecutorFor(_f.Tasks.Get(withSpare.Id)!)?.Id);
        Assert.Null(_f.Orchestrator.RunExecutorFor(_f.Tasks.Get(without.Id)!));
    }

    // ---------- копия шаблона ----------

    [Fact]
    public void Creating_A_Task_From_A_Template_Copies_The_Alternates()
    {
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        var node = _f.Tasks.Create(new TaskItem
        {
            Title = "узел шаблона",
            IsTemplate = true,
            ExecutorIds = [main.Id],
            AltExecutorIds = [spare.Id],
        }, "текст", "", null);

        var copy = _f.Tasks.InstantiateTemplate(node.Id, null, null);

        Assert.Equal([main.Id], copy.ExecutorIds);
        Assert.Equal([spare.Id], _f.Tasks.Get(copy.Id)!.AltExecutorIds);
    }

    [Fact]
    public void A_Disabled_Alternate_Does_Not_Travel_Into_The_Copy()
    {
        // то же правило, что у основного исполнителя (todo31): выключенный не копируется
        var main = CreateAi("main");
        var spare = CreateAi("spare");
        var node = _f.Tasks.Create(new TaskItem
        {
            Title = "узел шаблона",
            IsTemplate = true,
            ExecutorIds = [main.Id],
            AltExecutorIds = [spare.Id],
        }, "текст", "", null);

        spare.IsActive = false;
        _f.Executors.Update(spare, null);

        var copy = _f.Tasks.InstantiateTemplate(node.Id, null, null);

        Assert.Empty(_f.Tasks.Get(copy.Id)!.AltExecutorIds);
    }

    // ---------- ограничения формы ----------

    [Fact]
    public void An_Alternate_Outside_The_Task_Team_Is_Rejected()
    {
        // то же правило, что у основного исполнителя (ТЗ п. 2.1): выбор ограничен командой
        var main = CreateAi("main");
        var stranger = CreateAi("stranger");
        var team = _f.Teams.Create(new Team
        {
            Name = "команда",
            Members = [new TeamMember { ExecutorId = main.Id }],
        }, null);

        Assert.Throws<ArgumentException>(() => _f.Tasks.Create(new TaskItem
        {
            Title = "задача",
            TeamId = team.Id,
            ExecutorIds = [main.Id],
            AltExecutorIds = [stranger.Id],
        }, "текст", "", null));
    }
}
