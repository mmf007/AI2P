using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-196 «Задача на чужом сервере»: править её по-прежнему нельзя, но ДВА действия работают
/// с любого сервера кластера.
///
/// 1) ЧАТ. Запись и ответ на вопрос агента доступны везде: сообщение ложится в общий чат
///    задачи и едет владельцу репликацией. Ответили сразу на двух серверах — оба ответа
///    доходят до агента, и разбирается он сам (согласуются — работает, противоречат —
///    переспрашивает).
/// 2) ЗАПУСК, одиночный и иерархический. Нажать «запустить» можно везде, но ФИЗИЧЕСКИ
///    задание идёт на сервере задачи: с чужого сервера уезжает ЗАЯВКА (таблица run_requests),
///    и владелец разбирает её у себя. Уже запущенное не трогается; иерархия, запускаемая
///    поверх одиночных запусков, их дожидается, а не запускает повторно.
///
/// Оба «сервера» здесь работают с ОДНОЙ базой (как в Todo42Tests): репликация проверяется
/// живым прогоном двух настоящих серверов (test/t196), а здесь — правила владения и разбор
/// заявок как таковые.
/// </summary>
public sealed class T196Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и молчит (приём T-159/T-186): запущенное
    /// задание детерминированно висит в running, а исполнитель считается занятым.</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    /// <summary>Внутренний ключ сервера-дирижёра. Это ИМЕННО «local» — сервер одиночной
    /// установки (<see cref="ServerScope.Standalone"/>): под ним работают коннекторы фикстуры,
    /// а с T-1-S0 задача дирижёра принадлежит ему явно, и продолжить её задание вправе только
    /// её сервер. Разные ключи здесь означали бы «задача чужая даже для своего коннектора».</summary>
    private const string ConductorId = "local";
    private const string OtherId = "unid-S1";

    public T196Tests()
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

    // ---------- два «сервера» над одной базой ----------

    private static ServerScope Scope(string me, bool conductor) => new(
        () => me, () => me == ConductorId ? "S0" : "S1", () => conductor,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private ServerScope ConductorScope => Scope(ConductorId, conductor: true);

    private ServerScope OtherScope => Scope(OtherId, conductor: false);

    private TaskService Tasks(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    private RunRequestService Requests(ServerScope scope) => new(_f.Db, _f.Events, scope);

    /// <summary>Оркестратор «глазами» одного из серверов: свои сервисы владения и свои заявки.</summary>
    private JobOrchestrator Orchestrator(ServerScope scope) =>
        new(Tasks(scope), _f.Jobs, _f.Executors, _f.Projects, _f.Teams, _f.Files, _f.Chat,
            _f.Connectors, _f.Security, _f.Picker, new ExperienceService(_f.Db, _f.Events, scope),
            () => scope.IsConductor, _f.TeamWork, Requests(scope));

    private Executor CreateHuman(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

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

    /// <summary>Задача сервера-дирижёра (у неё сервер не указан — правит её только он).</summary>
    private TaskItem ConductorTask(string title, Executor? executor = null, string? parentId = null) =>
        Tasks(ConductorScope).Create(new TaskItem
        {
            Title = title,
            Status = TaskStatuses.Pending,
            ParentId = parentId,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    private TaskItem OtherTask(string title, Executor? executor = null, string? parentId = null) =>
        Tasks(OtherScope).Create(new TaskItem
        {
            Title = title,
            Status = TaskStatuses.Pending,
            ParentId = parentId,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    // ---------- 1. ЗАПУСК: заявка вместо отказа ----------

    [Fact]
    public void Starting_A_Foreign_Task_Files_A_Request_Instead_Of_Failing()
    {
        var executor = CreateHuman("Пётр");
        var task = ConductorTask("чужая для S1", executor);

        var request = Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null);

        Assert.NotNull(request);
        Assert.Equal(RunRequestKinds.Task, request!.Kind);
        Assert.Equal(task.Id, request.TaskId);
        // владелец строки — АВТОР заявки: править и убирать её вправе только он (ТЗ гл. 6)
        Assert.Equal(OtherId, request.ServerId);
        Assert.Equal("S1", request.ServerCode);
        // заявка называет сервер задачи — с T-1-S0 он проставлен и у задач дирижёра
        Assert.Equal(ConductorId, request.TargetServerId);
        // сама задача не тронута: запускать её здесь нечем
        Assert.Equal(TaskStatuses.Pending, Tasks(OtherScope).Get(task.Id)!.Status);
        Assert.Empty(_f.Jobs.ListByTask(task.Id));
    }

    [Fact]
    public void A_Request_Is_Not_Needed_For_Our_Own_Task()
    {
        var task = ConductorTask("своя", CreateHuman("Пётр"));

        // на своей задаче запускают напрямую — заявка означала бы лишний круг через журнал
        var error = Assert.Throws<InvalidOperationException>(() =>
            Orchestrator(ConductorScope).RequestRun(task.Id, RunRequestKinds.Task, false, null));
        Assert.Contains(task.DisplayId, error.Message);
    }

    [Fact]
    public async Task The_Owner_Applies_The_Request_And_Starts_The_Task_Exactly_Once()
    {
        var executor = CreateHuman("Пётр");
        var task = ConductorTask("запустить с другого сервера", executor);
        var request = Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null)!;

        var owner = Orchestrator(ConductorScope);
        owner.ApplyRunRequestsOnce();

        // задание пошло У ВЛАДЕЛЬЦА: человеку — в Inbox (waiting_human), задача в работе
        var job = Assert.Single(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(JobState.WaitingHuman, job.State);
        Assert.Equal(TaskStatuses.InProgress, Tasks(ConductorScope).Get(task.Id)!.Status);
        Assert.True(Requests(ConductorScope).IsApplied(request.Id));

        // повторный проход второго задания не создаёт — отметка «отработал» лежит у владельца
        // и переживает и перезапуск, и новый сеанс репликации
        owner.ApplyRunRequestsOnce();
        Assert.Single(_f.Jobs.ListByTask(task.Id));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_Request_Is_Not_Filed_For_A_Task_That_Already_Runs()
    {
        var executor = CreateHuman("Пётр");
        var task = ConductorTask("уже в работе", executor);
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);

        // «если задача уже запущена, то ничего не делаем» — заявки нет вовсе
        var request = Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null);

        Assert.Null(request);
        Assert.Empty(Requests(OtherScope).ListByTask(task.Id));
    }

    [Fact]
    public async Task A_Request_That_Arrived_Late_Does_Not_Start_A_Second_Job()
    {
        // заявка подана, пока задача стояла, а к разбору её уже запустили руками у владельца:
        // второго задания быть не должно (данные о чужих заданиях приезжают с задержкой)
        var executor = CreateHuman("Пётр");
        var task = ConductorTask("запустили раньше", executor);
        Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null);
        await Orchestrator(ConductorScope).StartTaskAsync(task.Id, null);

        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        Assert.Single(_f.Jobs.ListByTask(task.Id));
    }

    [Fact]
    public void Requests_Are_Not_Duplicated_By_A_Second_Press()
    {
        var task = ConductorTask("чужая", CreateHuman("Пётр"));
        var other = Orchestrator(OtherScope);

        var first = other.RequestRun(task.Id, RunRequestKinds.Task, false, null);
        var second = other.RequestRun(task.Id, RunRequestKinds.Task, false, null);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(Requests(OtherScope).ListByTask(task.Id));
    }

    /// <summary>
    /// ПОВТОРНОЕ НАЖАТИЕ ЧЕЛОВЕКА (renew, T-160-S0) заводит заявку ЗАНОВО — с новым
    /// идентификатором. Иначе повторный удалённый запуск не работает вовсе: отметку «заявку
    /// отработал» владелец держит у себя и автору не показывает, поэтому давно выполненная
    /// (а потом остановленная) заявка глушила бы кнопку навсегда.
    /// </summary>
    [Fact]
    public void A_Second_Press_By_A_Human_Files_A_Fresh_Request()
    {
        var task = ConductorTask("чужая", CreateHuman("Пётр"));
        var other = Orchestrator(OtherScope);

        var first = other.RequestRun(task.Id, RunRequestKinds.Task, false, null)!;
        var second = other.RequestRun(task.Id, RunRequestKinds.Task, false, null, renew: true);

        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second!.Id);
        Assert.Single(Requests(OtherScope).ListByTask(task.Id));   // старая снята, висит одна
    }

    [Fact]
    public void Only_The_Author_Removes_Its_Own_Request()
    {
        var task = ConductorTask("чужая", CreateHuman("Пётр"));
        var request = Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null)!;

        // заявку пишет и убирает её автор: владелец задачи только читает её (ТЗ гл. 6)
        Assert.Throws<ArgumentException>(() => Requests(ConductorScope).Remove(request.Id));
        Requests(OtherScope).Remove(request.Id);
        Assert.Empty(Requests(OtherScope).ListByTask(task.Id));
    }

    [Fact]
    public async Task The_Author_Cleans_Up_A_Request_Once_The_Task_Has_Started()
    {
        var executor = CreateHuman("Пётр");
        var task = ConductorTask("отработавшая заявка", executor);
        Orchestrator(OtherScope).RequestRun(task.Id, RunRequestKinds.Task, false, null);
        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        // уборка идёт на сервере-АВТОРЕ: задача уже не ждёт запуска — заявке делать нечего
        var removed = Requests(OtherScope).Cleanup();

        Assert.Equal(1, removed);
        Assert.Empty(Requests(OtherScope).ListByTask(task.Id));
        // а память владельца о том, что он её отработал, живёт дольше самой заявки: иначе
        // не доехавшее вовремя удаление воскресило бы запуск
        Assert.Single(_f.Jobs.ListByTask(task.Id));
        await Task.CompletedTask;
    }

    // ---------- 2. ИЕРАРХИЯ ----------

    [Fact]
    public void A_Hierarchy_Request_Opens_The_Queue_On_The_Owner()
    {
        var executor = CreateHuman("Пётр");
        var parent = ConductorTask("родитель", executor);
        ConductorTask("подзадача", executor, parent.Id);

        Orchestrator(OtherScope).RequestRun(parent.Id, RunRequestKinds.Hierarchy, true, null);
        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        var root = Tasks(ConductorScope).Get(parent.Id)!;
        Assert.True(TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyFlag));
        // галочка «и вставшие с ошибкой» (T-186) доехала вместе с заявкой
        Assert.True(TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyErrorsFlag));
    }

    [Fact]
    public async Task A_Hierarchy_Started_Over_Running_Tasks_Waits_For_Them_Instead_Of_Restarting()
    {
        // «если идёт запуск иерархии, а запущены одиночные задачи — запускаем иерархию
        // с обходом уже запущенных»
        var first = CreateHuman("Пётр");
        var second = CreateHuman("Павел");
        var parent = ConductorTask("родитель", first);
        var child = ConductorTask("подзадача", second, parent.Id);
        await Orchestrator(ConductorScope).StartTaskAsync(child.Id, null); // одиночный запуск

        Orchestrator(OtherScope).RequestRun(parent.Id, RunRequestKinds.Hierarchy, false, null);
        Orchestrator(ConductorScope).ApplyRunRequestsOnce();

        // у уже запущенной подзадачи второго задания не появилось, а родитель ждёт её
        Assert.Single(_f.Jobs.ListByTask(child.Id));
        Assert.Empty(_f.Jobs.ListByTask(parent.Id));
        Assert.Equal(TaskStatuses.Pending, Tasks(ConductorScope).Get(parent.Id)!.Status);
    }

    [Fact]
    public void An_Already_Running_Hierarchy_Is_Left_Alone()
    {
        var parent = ConductorTask("родитель", CreateHuman("Пётр"));
        ConductorTask("подзадача", CreateHuman("Павел"), parent.Id);
        Tasks(ConductorScope).SetHierarchyRun(parent.Id, true);

        var request = Orchestrator(OtherScope).RequestRun(parent.Id, RunRequestKinds.Hierarchy, false, null);

        Assert.Null(request);
    }

    [Fact]
    public async Task A_Foreign_Subtask_Inside_Our_Hierarchy_Gets_A_Request_And_Is_Waited_For()
    {
        // иерархия идёт у нас, а одна из подзадач принадлежит другому серверу: до T-196 её
        // молча пропускали, и родитель мог уйти в работу раньше неё
        var mine = CreateHuman("Пётр");
        var theirs = CreateHuman("Павел");
        var parent = ConductorTask("родитель", mine);
        var foreign = OtherTask("чужая подзадача", theirs);
        Tasks(ConductorScope).ChangeParent(foreign.Id, parent.Id, null);

        var run = await Orchestrator(ConductorScope).StartHierarchyAsync(parent.Id, null);

        Assert.Equal(1, run.Requested);
        Assert.True(run.Waiting > 0);
        Assert.Empty(run.Started);           // родитель ждёт чужую подзадачу
        Assert.Empty(_f.Jobs.ListByTask(parent.Id));
        var request = Assert.Single(Requests(ConductorScope).ListByTask(foreign.Id));
        Assert.Equal(RunRequestKinds.Task, request.Kind);
        Assert.Equal(OtherId, request.TargetServerId);

        // а её владелец эту заявку выполняет у себя
        Orchestrator(OtherScope).ApplyRunRequestsOnce();
        Assert.Single(_f.Jobs.ListByTask(foreign.Id));
    }

    // ---------- 3. ЧАТ ----------

    [Fact]
    public void Writing_To_The_Chat_Of_A_Foreign_Task_Is_Allowed()
    {
        var task = ConductorTask("чужая для S1", CreateHuman("Пётр"));
        var author = CreateHuman("Мария");

        // чат общий на весь кластер: сообщение уедет владельцу задачи репликацией
        var message = _f.Chat.Add(task.Id, author.Id, null, "посмотри, пожалуйста, критерии");

        Assert.Equal(task.Id, message.TaskId);
        Assert.Single(_f.Chat.ListByTask(task.Id));
        // при этом ПРАВКА чужой задачи по-прежнему закрыта
        Assert.Throws<ArgumentException>(() =>
            Tasks(OtherScope).ChangeStatus(task.Id, TaskStatuses.Done, null));
    }

    [Fact]
    public async Task Answering_A_Question_Of_A_Foreign_Task_Only_Writes_The_Answer()
    {
        var agent = CreateAi("ИИ");
        var human = CreateHuman("Мария");
        var task = ConductorTask("вопрос агента", agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, null, null, 0, 0, 0, "", JobWaitKinds.Human);
        var question = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "какой формат ответа?", []);

        var answer = await Orchestrator(OtherScope).AnswerQuestionAsync(question.Id, "JSON", human.Id);

        Assert.Equal(ChatMessageKind.Answer, answer.Kind);
        Assert.NotNull(_f.Chat.Get(question.Id)!.AnsweredAt);
        // продолжать работу отсюда нечем: сессия агента и файлы контекста у владельца задачи,
        // задание так и осталось ждущим — его поднимет сервер задачи, увидев ответ
        Assert.Equal(JobState.WaitingHuman, _f.Jobs.Get(job.Id)!.State);
    }

    [Fact]
    public async Task The_Owner_Resumes_The_Agent_When_The_Answer_Arrives_By_Replication()
    {
        var agent = CreateAi("ИИ");
        var human = CreateHuman("Мария");
        var task = ConductorTask("вопрос агента", agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, null, null, 0, 0, 0, "", JobWaitKinds.Human);
        var question = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "какой формат ответа?", []);
        // контекст диалога агента — без него продолжать нечего (ТЗ v1.17)
        _f.Files.WriteText(
            Path.Combine(_f.Files.TaskDirRel(null, task.DisplayId), "ai", $"{job.DisplayId}-context.json"),
            """{"messages":[]}""");
        // ответ написан НА ДРУГОМ сервере: сюда он приезжает строкой чата, без всякого вызова
        await Orchestrator(OtherScope).AnswerQuestionAsync(question.Id, "JSON", human.Id);

        Orchestrator(ConductorScope).ResumeAnsweredOnce();

        // работа продолжена: задание снова в работе, задача — тоже
        Assert.Equal(JobState.Running, _f.Jobs.Get(job.Id)!.State);
        Assert.Equal(TaskStatuses.InProgress, Tasks(ConductorScope).Get(task.Id)!.Status);
    }

    [Fact]
    public async Task Both_Answers_Reach_The_Agent_When_Two_Servers_Answered()
    {
        var agent = CreateAi("ИИ");
        var first = CreateHuman("Мария");
        var second = CreateHuman("Иван");
        var task = ConductorTask("вопрос агента", agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, null, null, 0, 0, 0, "", JobWaitKinds.Human);
        var question = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "какой формат ответа?", []);

        await Orchestrator(OtherScope).AnswerQuestionAsync(question.Id, "JSON", first.Id);
        // второй ответ написан на ДРУГОМ сервере (там вопрос ещё висел) и приехал сюда
        // строкой репликации: «выиграл последний» решает судьбу отметки об ответе,
        // но не самих сообщений — оба остаются в чате
        ArriveAnswer(question.Id, task.Id, second.Id, "нет, YAML");

        var answers = _f.Chat.AnswersTo(question.Id);

        Assert.Equal(2, answers.Count);
        Assert.Contains(answers, a => a.Text == "JSON");
        Assert.Contains(answers, a => a.Text == "нет, YAML");
    }

    [Fact]
    public void A_Late_Answer_Reaches_A_Working_Agent_As_A_Chat_Interruption()
    {
        // второй ответ (его написали на соседнем сервере) приезжает уже ПОСЛЕ того, как работу
        // продолжили первым: механика вопросов закрывает вопрос один раз, и без перебивки
        // такой ответ не дошёл бы до агента вовсе
        var agent = CreateAi("ИИ");
        var human = CreateHuman("Мария");
        var task = ConductorTask("вопрос агента", agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        var question = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "какой формат?", []);
        _f.Chat.Answer(question.Id, human.Id, "JSON");

        // наблюдатель строится при продолжении работы: всё, что уже в чате, — контекст
        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);
        Assert.False(watch.HasNew);

        // а вот ВТОРОЙ ответ, приехавший после, доставляется агенту
        ArriveAnswer(question.Id, task.Id, human.Id, "поправка: YAML");
        var fresh = watch.Take();

        Assert.Single(fresh);
        Assert.Equal("поправка: YAML", fresh[0].Text);
    }

    /// <summary>
    /// Ответ, ПРИЕХАВШИЙ РЕПЛИКАЦИЕЙ: строка чата появляется в базе сама, без вызова API —
    /// её написали на соседнем сервере, где вопрос ещё висел неотвеченным. Через сервисы
    /// такое не воспроизвести (у нас вопрос уже закрыт), поэтому строка кладётся так же,
    /// как её кладёт применение журнала изменений.
    /// </summary>
    private void ArriveAnswer(string questionId, string taskId, string fromExecutorId, string text)
    {
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO chat_messages (id, task_id, from_executor_id, to_executor_id, text,
                                       kind, options_json, answer_to_id, created_at, updated_at)
            VALUES (@id, @task, @from, NULL, @text, 'answer', '[]', @answer, @ts, @ts)
            """,
            ("@id", Guid.NewGuid().ToString()), ("@task", taskId), ("@from", fromExecutorId),
            ("@text", text), ("@answer", questionId), ("@ts", Sql.ToDb(DateTime.UtcNow)));
    }
}
