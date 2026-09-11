using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-185 «Взаимодействие агентов» (выпуск 1.80).
///
/// 1) РАЗГОВОР. Агенты, работающие над РОДСТВЕННЫМИ задачами (подзадачи одного родителя,
///    родитель и свои потомки), могут писать друг другу через чат задачи собеседника:
///    инструмент send_task_message (у CLI-агента — маркер), ожидание ответа — wait_task_reply
///    либо пауза задания. Дальше родства переписка не идёт: чужие задания читаются
///    инструментами чтения, а не разговором.
/// 2) СОСТОЯНИЕ. «Ждёт ответа» (waiting_reply) переименовано в «паузу» (paused): ждать теперь
///    можно не только ответа человека, но и ответа соседнего агента, и завершения своих
///    подзадач. Накопленные задачи переводит шаг обновления билда 80.
/// 3) ПОДЗАДАЧИ. Задача, разошедшаяся на подзадачи, идёт не на «проверку», а в «паузу»,
///    и её агент НЕ закрывается: задание висит в ожидании (wait_kind=subtasks), а такое
///    ожидание не занимает исполнителя — иначе подзадача, назначенная ему же, не запустилась
///    бы никогда.
/// 4) ЛИМИТ. Кончился лимит исполнителя — задача тоже в «паузе», а не в «ожидает»
///    (проверяется там, где живёт сам перенос старта: Todo62Tests, T166Tests).
/// </summary>
public sealed class T185Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- обвязка ----------

    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """{ "skills": [ { "name": "code-write", "score": 90 } ] }""");
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
        }, null);
    }

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст задания", "", null);

    private TaskToolset Tools(TaskItem current, string? actorExecutorId) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
            _f.RefData, _f.Picker, actorExecutorId);

    private static async Task<string> Call(TaskToolset tools, string name, string argsJson = "{}")
        => await tools.ExecuteAsync(name, Args(argsJson), CancellationToken.None);

    private static System.Text.Json.JsonElement Args(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    // ---------- 1. кому агент может написать ----------

    [Fact]
    public async Task An_Agent_Writes_To_A_Sibling_Subtask_Of_The_Same_Parent()
    {
        // ровно тот случай из задания: параллельные подзадачи одного родителя
        var me = CreateAi("первый");
        var neighbour = CreateAi("второй");
        var parent = CreateTask("родитель");
        var mine = CreateTask("моя часть", parent.Id, me);
        var theirs = CreateTask("соседняя часть", parent.Id, neighbour);

        var answer = await Call(Tools(mine, me.Id), "send_task_message",
            $$"""{"code":"{{theirs.DisplayId}}","text":"я поменял формат ответа, учти"}""");

        // сообщение легло в чат ЗАДАЧИ-СОБЕСЕДНИКА, а не в свой
        var message = Assert.Single(_f.Chat.ListByTask(theirs.Id));
        Assert.Equal(me.Id, message.FromExecutorId);
        Assert.Equal(neighbour.Id, message.ToExecutorId); // адресат — её единственный исполнитель
        Assert.Contains("я поменял формат ответа, учти", message.Text);
        // подпись: собеседник видит письмо как перебивку чата и должен понять, от кого оно
        Assert.Contains(mine.DisplayId, message.Text);
        Assert.Contains("первый", message.Text);
        Assert.Contains(theirs.DisplayId, answer);
        Assert.Empty(_f.Chat.ListByTask(mine.Id));
    }

    [Fact]
    public async Task A_Parent_And_Its_Own_Children_Are_Reachable_Too()
    {
        // второй случай из задания: родитель работает одновременно со своими подзадачами
        var boss = CreateAi("родительский");
        var kid = CreateAi("детский");
        var parent = CreateTask("родитель", executor: boss);
        var child = CreateTask("подзадача", parent.Id, kid);

        // родитель → потомку
        await Call(Tools(parent, boss.Id), "send_task_message",
            $$"""{"code":"{{child.DisplayId}}","text":"сначала сделай схему"}""");
        // потомок → родителю
        await Call(Tools(child, kid.Id), "send_task_message",
            $$"""{"code":"{{parent.DisplayId}}","text":"схема готова"}""");

        Assert.Contains("сначала сделай схему", Assert.Single(_f.Chat.ListByTask(child.Id)).Text);
        Assert.Contains("схема готова", Assert.Single(_f.Chat.ListByTask(parent.Id)).Text);
    }

    [Fact]
    public async Task A_Task_From_Another_Branch_Is_Refused_With_The_List_Of_Neighbours()
    {
        // «дальше родства переписка не идёт»: агент не должен получить возможность писать
        // всему проекту — иначе нехватка данных превращается в рассылку
        var me = CreateAi("первый");
        var parent = CreateTask("родитель");
        var mine = CreateTask("моя часть", parent.Id, me);
        var sibling = CreateTask("соседняя часть", parent.Id, CreateAi("второй"));
        var stranger = CreateTask("чужая ветка"); // без общего родителя

        var answer = await Call(Tools(mine, me.Id), "send_task_message",
            $$"""{"code":"{{stranger.DisplayId}}","text":"привет"}""");

        Assert.Empty(_f.Chat.ListByTask(stranger.Id));
        Assert.Contains(stranger.DisplayId, answer); // сказано, кого не нашли
        Assert.Contains(sibling.DisplayId, answer);  // и кому написать всё-таки можно
    }

    [Fact]
    public async Task Without_An_Executor_Of_Its_Own_The_Agent_Does_Not_Chat()
    {
        // инструмент публикуется только тому, от чьего имени есть кому писать
        var parent = CreateTask("родитель");
        var mine = CreateTask("моя часть", parent.Id);
        var theirs = CreateTask("соседняя часть", parent.Id, CreateAi("второй"));

        var answer = await Call(Tools(mine, null), "send_task_message",
            $$"""{"code":"{{theirs.DisplayId}}","text":"привет"}""");

        Assert.Empty(_f.Chat.ListByTask(theirs.Id));
        Assert.NotEmpty(answer);
    }

    // ---------- 2. ожидание ответа собеседника ----------

    [Fact]
    public async Task With_Wait_The_Job_Is_Paused_Until_The_Neighbour_Answers()
    {
        var me = CreateAi("первый");
        var neighbour = CreateAi("второй");
        var parent = CreateTask("родитель");
        var mine = CreateTask("моя часть", parent.Id, me);
        var theirs = CreateTask("соседняя часть", parent.Id, neighbour);

        var tools = Tools(mine, me.Id);
        await Call(tools, "send_task_message",
            $$"""{"code":"{{theirs.DisplayId}}","text":"какой у тебя формат?","wait":true}""");

        // коннектор по этой пометке ставит задание на паузу — как на вопросе человеку
        Assert.NotNull(tools.Waiting);
        Assert.Equal(theirs.DisplayId, tools.Waiting!.TaskCode);
        Assert.Equal(neighbour.Id, tools.Waiting.ExecutorId); // ждём ИМЕННО его ответа
    }

    [Fact]
    public async Task There_Is_Nobody_To_Wait_For_When_The_Neighbour_Has_No_Executor()
    {
        // ждать ответа задачи без исполнителя бессмысленно: отвечать некому, и агент
        // должен работать дальше, а не висеть до вмешательства человека
        var me = CreateAi("первый");
        var parent = CreateTask("родитель");
        var mine = CreateTask("моя часть", parent.Id, me);
        var theirs = CreateTask("ничья часть", parent.Id);

        var tools = Tools(mine, me.Id);
        await Call(tools, "send_task_message",
            $$"""{"code":"{{theirs.DisplayId}}","text":"ответь мне","wait":true}""");

        Assert.Null(tools.Waiting);
        Assert.Single(_f.Chat.ListByTask(theirs.Id)); // само письмо всё равно доставлено
    }

    [Fact]
    public void The_Reply_Of_The_Addressee_Closes_The_Question_And_Nothing_Else_Does()
    {
        // ответом считается сообщение ТОГО, кому вопрос адресован, написанное ПОСЛЕ вопроса
        var me = CreateAi("первый");
        var neighbour = CreateAi("второй");
        var outsider = CreateAi("третий");
        var task = CreateTask("моя часть", executor: me);
        var job = _f.Jobs.Create(task.Id, me.Id, task.DescriptionPath, null);
        var question = _f.Chat.AddQuestion(task.Id, me.Id, job.Id, "какой формат?", [], neighbour.Id);

        // посторонний написал — это не ответ, ждём дальше
        _f.Chat.Add(task.Id, outsider.Id, null, "я мимо проходил");
        Assert.Null(_f.Chat.AgentReplyTo(question));

        // ответил адресат — вопрос закрывается ЕГО сообщением, второй записи не заводится
        var reply = _f.Chat.Add(task.Id, neighbour.Id, me.Id, "формат JSON");
        var found = _f.Chat.AgentReplyTo(question);
        Assert.NotNull(found);
        Assert.Equal(reply.Id, found!.Id);

        _f.Chat.CloseByAgentReply(question.Id, reply.Id);
        Assert.NotNull(_f.Chat.Get(question.Id)!.AnsweredAt);
        Assert.Equal(question.Id, _f.Chat.Get(reply.Id)!.AnswerToId);
    }

    [Fact]
    public void A_Question_To_A_Human_Is_Never_Closed_By_Somebody_Elses_Message()
    {
        // вопрос человеку адресата-исполнителя не имеет: его закрывает только настоящий ответ
        var me = CreateAi("первый");
        var task = CreateTask("моя часть", executor: me);
        var job = _f.Jobs.Create(task.Id, me.Id, task.DescriptionPath, null);
        var question = _f.Chat.AddQuestion(task.Id, me.Id, job.Id, "как быть?", []);

        _f.Chat.Add(task.Id, CreateAi("второй").Id, null, "какое-то сообщение");

        Assert.Null(_f.Chat.AgentReplyTo(question));
    }

    // ---------- 3. чего ждёт задание (jobs.wait_kind) ----------

    [Fact]
    public void Waiting_For_Subtasks_Does_Not_Keep_The_Executor_Busy_But_A_Question_Does()
    {
        // подзадачи разбитой задачи автоподбор часто отдаёт тому же агенту: считай его занятым —
        // и очередь встанет намертво (родитель ждёт подзадач, подзадачи ждут его исполнителя)
        var ai = CreateAi("агент");
        var task = CreateTask("родитель", executor: ai);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);

        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Subtasks);
        Assert.False(_f.Jobs.HasActiveByExecutor(ai.Id));

        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Human);
        Assert.True(_f.Jobs.HasActiveByExecutor(ai.Id));

        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Agent);
        Assert.True(_f.Jobs.HasActiveByExecutor(ai.Id)); // разговор — обычная пауза, агент занят
    }

    [Fact]
    public void Waiting_Jobs_Are_Selected_By_What_They_Wait_For()
    {
        // по этим выборкам сторож раз в минуту решает, кого пора будить
        var ai = CreateAi("агент");
        var other = CreateAi("другой");
        var mine = CreateTask("моя", executor: ai);
        var theirs = CreateTask("другая", executor: other);
        var subtasksJob = _f.Jobs.Create(mine.Id, ai.Id, mine.DescriptionPath, null);
        var agentJob = _f.Jobs.Create(theirs.Id, other.Id, theirs.DescriptionPath, null);

        _f.Jobs.SetState(subtasksJob.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Subtasks);
        _f.Jobs.SetState(agentJob.Id, JobState.WaitingHuman, other.Id, waitKind: JobWaitKinds.Agent);

        Assert.Equal(subtasksJob.Id, Assert.Single(_f.Jobs.ListWaiting(JobWaitKinds.Subtasks)).Id);
        Assert.Equal(agentJob.Id, Assert.Single(_f.Jobs.ListWaiting(JobWaitKinds.Agent)).Id);
        Assert.Empty(_f.Jobs.ListWaiting(JobWaitKinds.Human));

        Assert.Equal(subtasksJob.Id, _f.Jobs.WaitingByTask(mine.Id, JobWaitKinds.Subtasks)!.Id);
        Assert.Null(_f.Jobs.WaitingByTask(mine.Id, JobWaitKinds.Agent));
        Assert.Equal(subtasksJob.Id, _f.Jobs.WaitingByTask(mine.Id)!.Id); // без уточнения — любое
    }

    [Fact]
    public void The_Wait_Reason_Is_Cleared_When_The_Job_Goes_On()
    {
        // «чего ждём» — свойство ПАУЗЫ: у работающего и у законченного задания его быть не должно
        var ai = CreateAi("агент");
        var task = CreateTask("задача", executor: ai);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);

        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Subtasks);
        Assert.Equal(JobWaitKinds.Subtasks, _f.Jobs.Get(job.Id)!.WaitKind);

        _f.Jobs.SetState(job.Id, JobState.Running, ai.Id);
        Assert.Equal("", _f.Jobs.Get(job.Id)!.WaitKind);
    }

    [Fact]
    public void An_Old_Job_Without_The_Column_Value_Means_Waiting_For_A_Human()
    {
        // задания, заведённые до T-185: колонка пуста — прежнее поведение, вопрос человеку
        var ai = CreateAi("агент");
        var task = CreateTask("задача", executor: ai);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, ai.Id, waitKind: JobWaitKinds.Human);
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE jobs SET wait_kind=NULL WHERE id=@id", ("@id", job.Id));
        }

        Assert.Equal("", _f.Jobs.Get(job.Id)!.WaitKind);
        Assert.True(_f.Jobs.HasActiveByExecutor(ai.Id)); // исполнитель занят, как и раньше
        Assert.Empty(_f.Jobs.ListWaiting(JobWaitKinds.Subtasks));
    }

    [Fact]
    public void Schema_Has_The_Wait_Kind_Column()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(jobs)", r => r.S("name"));

        Assert.Contains("wait_kind", columns);
    }

    // ---------- 4. «ждёт ответа» стало «паузой» ----------

    [Fact]
    public void The_Status_Is_Called_Pause_And_The_Old_Code_Is_Gone()
    {
        var statuses = _f.Statuses.List("ru");

        var paused = Assert.Single(statuses.Where(s => s.Id == TaskStatuses.Paused));
        Assert.False(paused.IsCustom);
        Assert.NotEmpty(paused.Name);
        Assert.NotEqual(TaskStatuses.Paused, paused.Name); // название, а не код
        Assert.DoesNotContain(statuses, s => s.Id == TaskStatuses.LegacyWaitingReply);
        Assert.Contains(TaskStatuses.Paused, TaskStatuses.BuiltIn);
        Assert.DoesNotContain(TaskStatuses.LegacyWaitingReply, TaskStatuses.BuiltIn);
    }

    [Fact]
    public void Accumulated_Tasks_Are_Moved_To_The_New_Code_By_The_Upgrade_Step()
    {
        // база прошлой версии: задачи со старым кодом и старая запись справочника
        var stale = CreateTask("висела в ожидании ответа");
        var untouched = CreateTask("обычная");
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE tasks SET status=@old WHERE id=@id",
                ("@old", TaskStatuses.LegacyWaitingReply), ("@id", stale.Id));
            Sql.Exec(conn, null, """
                INSERT INTO task_statuses (id, color, is_custom, is_active, sort_order, created_at, updated_at)
                VALUES (@id, '#ab47bc', 0, 1, 35, '2020-01-01', '2020-01-01')
                ON CONFLICT(id) DO NOTHING
                """, ("@id", TaskStatuses.LegacyWaitingReply));
            Sql.Exec(conn, null, """
                INSERT INTO task_status_texts (status_id, lang, name) VALUES (@id, 'ru', 'ждёт ответа')
                ON CONFLICT(status_id, lang) DO NOTHING
                """, ("@id", TaskStatuses.LegacyWaitingReply));
        }

        var moved = _f.Statuses.MigrateWaitingReplyToPaused();

        Assert.Equal(1, moved);
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(stale.Id)!.Status);
        Assert.Equal(untouched.Status, _f.Tasks.Get(untouched.Id)!.Status); // чужие не тронуты
        // «состояния без названия» не остаётся: старой записи справочника больше нет
        Assert.DoesNotContain(_f.Statuses.List("ru"), s => s.Id == TaskStatuses.LegacyWaitingReply);

        // повтор шага (прыжок через версии, перезапуск) ничего не портит
        Assert.Equal(0, _f.Statuses.MigrateWaitingReplyToPaused());
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(stale.Id)!.Status);
    }

    [Fact]
    public void A_Custom_Status_With_The_Old_Code_Belongs_To_The_Human_And_Stays()
    {
        // такой код мог завести человек своим состоянием — оно его, и удалять его нельзя
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO task_statuses (id, color, is_custom, is_active, sort_order, created_at, updated_at)
                VALUES (@id, '#123456', 1, 1, 95, '2020-01-01', '2020-01-01')
                ON CONFLICT(id) DO NOTHING
                """, ("@id", TaskStatuses.LegacyWaitingReply));
            Sql.Exec(conn, null, """
                INSERT INTO task_status_texts (status_id, lang, name) VALUES (@id, 'ru', 'моё состояние')
                ON CONFLICT(status_id, lang) DO NOTHING
                """, ("@id", TaskStatuses.LegacyWaitingReply));
        }

        _f.Statuses.MigrateWaitingReplyToPaused();

        var kept = Assert.Single(_f.Statuses.List("ru").Where(s => s.Id == TaskStatuses.LegacyWaitingReply));
        Assert.Equal("моё состояние", kept.Name);
        Assert.True(kept.IsCustom);
    }
}
