using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-224 «Запуск иерархии задач»: у задачи, по которой нажата кнопка «запустить всю
/// иерархию», появилась пометка «запуск иерархии» — там же, где печатается причина паузы
/// (T-187): за статусом в форме задачи и в столбце «причина паузы» представления
/// «в работе у ИИ».
///
/// Случай заказчика: очередь иерархии (T-159) идёт снизу вверх, поэтому сама задача уходит
/// в работу ПОСЛЕДНЕЙ — до тех пор она так и стоит в состоянии «ожидает», и со стороны это
/// выглядит как «нажал кнопку, и ничего не происходит». Пометка объясняет ожидание, а
/// СОСТОЯНИЕ задачи при этом не трогается: «ожидает» — правильный статус, задача ждёт
/// своей очереди.
///
/// Границы правки (осознанные, см. отчёт): пометка ставится ТОЙ задаче, по которой нажата
/// кнопка (у неё в launch_json флаг runHierarchy), а не всему поддереву; и она молчит,
/// пока агент занят задачей сам — тогда причина ожидания считается по заданию, как и до T-224.
/// </summary>
public sealed class T224Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает (приём T-159,
    /// T-210): запущенное задание детерминированно висит в running.</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public T224Tests()
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
        string status = TaskStatuses.Pending) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = status,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    /// <summary>Строка задачи в представлении «в работе у ИИ»; null — задачи там нет.</summary>
    private AI2P.Core.Api.AiWorkItemDto? WorkItem(TaskItem task) =>
        _f.Tasks.AiWork().FirstOrDefault(i => i.Task.Id == task.Id);

    // ---------- 1. правила расчёта причины (общие для формы задачи и списка) ----------

    [Fact]
    public void Open_Hierarchy_Run_Is_A_Named_Reason()
    {
        var pause = AiPause.Of(waiting: false, "", startAfter: null, pendingQuestions: 0,
            DateTime.UtcNow, hierarchyRun: true);

        Assert.Equal(AiPauseKinds.Hierarchy, pause!.Kind);
        Assert.Null(pause.Until);
        Assert.Equal(0, pause.Questions);
    }

    [Fact]
    public void Without_The_Flag_Nothing_Changes()
    {
        // прежние вызовы (их в коде большинство) не передают ничего — расчёт обязан остаться
        // таким же, каким был до T-224
        Assert.Null(AiPause.Of(waiting: false, "", startAfter: null, pendingQuestions: 0,
            DateTime.UtcNow));
    }

    [Fact]
    public void Limit_Wins_Over_The_Hierarchy_Mark()
    {
        var now = DateTime.UtcNow;

        var pause = AiPause.Of(waiting: false, "", now.AddHours(1), 0, now, hierarchyRun: true);

        // человеку важнее, КОГДА работа продолжится сама (T-121), — очередь иерархии дождётся
        Assert.Equal(AiPauseKinds.Limit, pause!.Kind);
    }

    [Fact]
    public void A_Question_Wins_Over_The_Hierarchy_Mark()
    {
        var now = DateTime.UtcNow;

        var pause = AiPause.Of(waiting: true, JobWaitKinds.Human, null, 2, now, hierarchyRun: true);

        // вопрос ждёт ЧЕЛОВЕКА: пока на него не ответят, очередь всё равно не сдвинется
        Assert.Equal(AiPauseKinds.Question, pause!.Kind);
        Assert.Equal(2, pause.Questions);
    }

    [Fact]
    public void Waiting_For_Subtasks_Wins_Over_The_Hierarchy_Mark()
    {
        var now = DateTime.UtcNow;

        var pause = AiPause.Of(waiting: true, JobWaitKinds.Subtasks, null, 0, now, hierarchyRun: true);

        Assert.Equal(AiPauseKinds.Subtasks, pause!.Kind);
    }

    // ---------- 2. представление «в работе у ИИ» ----------

    [Fact]
    public void Task_Of_An_Open_Run_Is_In_The_List_And_Stays_Pending()
    {
        var agent = CreateAi("корневой");
        var task = CreateTask("корень", executor: agent);
        _f.Tasks.SetHierarchyRun(task.Id, true);

        var item = WorkItem(task);

        Assert.NotNull(item);
        Assert.True(item!.Paused);
        Assert.Equal(AiPauseKinds.Hierarchy, item.Pause!.Kind);
        // СОСТОЯНИЕ задачи пометка не трогает: задача ждёт своей очереди — это «ожидает»
        Assert.Equal(TaskStatuses.Pending, item.Task.Status);
        // задания у задачи ещё не было: исполнителем числится назначенный ей агент
        Assert.Equal(agent.Id, item.ExecutorId);
        Assert.Equal("", item.JobId);
        Assert.Null(item.StartedAt);
    }

    [Fact]
    public void A_Working_Task_Is_Not_Marked_By_The_Queue()
    {
        var agent = CreateAi("работник");
        var task = CreateTask("корень", executor: agent);
        _f.Tasks.SetHierarchyRun(task.Id, true);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.Running, agent.Id);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, agent.Id);

        var item = WorkItem(task);

        // очередь дошла до самой задачи: агент ею занят, ждать нечего — паузы нет
        Assert.NotNull(item);
        Assert.Null(item!.Pause);
        Assert.False(item.Paused);
    }

    [Fact]
    public void A_Question_Of_The_Task_Wins_In_The_List_Too()
    {
        var agent = CreateAi("спросивший");
        var task = CreateTask("корень", executor: agent);
        _f.Tasks.SetHierarchyRun(task.Id, true);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id, waitKind: JobWaitKinds.Human);
        _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "Куда класть результат?", []);

        var item = WorkItem(task);

        Assert.Equal(AiPauseKinds.Question, item!.Pause!.Kind);
        Assert.Equal(1, item.Pause.Questions);
    }

    [Fact]
    public void A_Closed_Queue_Takes_The_Mark_Away()
    {
        var task = CreateTask("корень", executor: CreateAi("агент"));
        _f.Tasks.SetHierarchyRun(task.Id, true);
        Assert.NotNull(WorkItem(task));

        _f.Tasks.SetHierarchyRun(task.Id, false);

        // очередь закрыта — пометки нет, и задача уходит из списка работы ИИ
        Assert.Null(WorkItem(task));
    }

    [Fact]
    public void A_Finished_Task_Is_Not_In_The_List()
    {
        var task = CreateTask("корень", executor: CreateAi("агент"));
        _f.Tasks.SetHierarchyRun(task.Id, true);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Done, null);

        // очередь закрывается своим проходом, и до тех пор флаг у выполненной задачи может
        // ещё стоять: списку работы ИИ такая задача не нужна
        Assert.Null(WorkItem(task));
    }

    [Fact]
    public void A_Task_Without_An_Ai_Executor_Is_Not_Ai_Work()
    {
        var human = _f.Executors.Create(new Executor { Nick = "миша", Kind = ExecutorKind.Human }, null);
        var task = CreateTask("корень", executor: human);
        _f.Tasks.SetHierarchyRun(task.Id, true);

        // «в работе у ИИ» — список работы АГЕНТОВ: очередь по задачам человека в него не идёт
        // (в форме задачи пометка при этом показывается — она считается по флагу, а не по агенту)
        Assert.Null(WorkItem(task));
    }

    // ---------- 3. сквозной путь: нажатие кнопки → пометка ----------

    [Fact]
    public async Task Pressing_The_Button_Marks_The_Task_While_Subtasks_Work()
    {
        var root = CreateTask("корень", executor: CreateAi("корневой"));
        var child = CreateTask("подзадача", root.Id, CreateAi("детский"));

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        // очередь пошла снизу вверх: работает подзадача, корень ждёт своей очереди
        Assert.Equal([_f.Tasks.Get(child.Id)!.DisplayId], run.Started);
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(root.Id)!.Status);
        Assert.True(TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson,
            TaskService.HierarchyFlag));

        var rootItem = WorkItem(root);
        Assert.Equal(AiPauseKinds.Hierarchy, rootItem!.Pause!.Kind);
        // у работающей подзадачи причины ждать нет — она в работе
        Assert.Null(WorkItem(child)!.Pause);
    }

    [Fact]
    public async Task When_The_Queue_Is_Over_The_Mark_Is_Gone()
    {
        var root = CreateTask("корень", executor: CreateAi("корневой"));
        var child = CreateTask("подзадача", root.Id, CreateAi("детский"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        // подзадача сдана — очередь дошла до корня, он уходит в работу
        _f.Jobs.SetState(_f.Jobs.ListByTask(child.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(child.Id, TaskStatuses.Review, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(root.Id)!.Status);
        Assert.Null(WorkItem(root)!.Pause); // работает сам — пометка молчит

        // корень сдан: следующий проход закрывает очередь и снимает флаг
        _f.Jobs.SetState(_f.Jobs.ListByTask(root.Id)[0].Id, JobState.Done, null, null);
        _f.Tasks.ChangeStatus(root.Id, TaskStatuses.Review, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.False(TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson,
            TaskService.HierarchyFlag));
        Assert.Null(WorkItem(root));
    }

    // ---------- 4. вывод человеку ----------

    [Theory]
    [InlineData("aiwork.pause.hierarchy")]
    [InlineData("aiwork.pause.hierarchy.hint")]
    public void The_Mark_Is_Named_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустой текст {key}");
        }
    }

    [Fact]
    public void The_Mark_Wears_The_Icon_Of_Its_Button()
    {
        // тот же значок, что у кнопки «запустить иерархию» (T-209): пометка и кнопка,
        // которая её поставила, узнаются друг в друге
        Assert.Equal(AI2P.UI.Ai2pIcons.TripleArrowRight,
            AI2P.UI.Services.AiPauseText.Icon(new AiPauseDto { Kind = AiPauseKinds.Hierarchy }));
    }

    [Fact]
    public void The_Card_Explains_The_Mark_Its_Own_Way()
    {
        var card = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "TaskCardView.razor"));

        // подсказка чипа выбирается по виду паузы: у очереди иерархии она своя — объясняет,
        // почему задача так и стоит в «ожидает»
        Assert.Contains("PauseHint(pause)", card);
        Assert.Contains("aiwork.pause.hierarchy.hint", card);
        // опора живой проверки в браузере: вид паузы висит атрибутом на чипе (T-187)
        Assert.Contains("data-task-pause=\"@pause.Kind\"", card);
    }

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
}
