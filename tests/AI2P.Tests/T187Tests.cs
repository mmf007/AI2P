using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-187: ПРЕДСТАВЛЕНИЕ «В РАБОТЕ У ИИ».
///
/// Случай заказчика: чем заняты агенты прямо сейчас, было видно только по одной задаче за раз —
/// открыть карточку, посмотреть задание, догадаться, почему ничего не происходит. Теперь есть
/// отдельный список без фильтра: все задачи всех проектов и серверов, над которыми ИИ работает
/// или которые держит на паузе, — и у паузы ВСЕГДА названа причина.
///
/// Причин ровно три (T-121, T-185): кончился лимит исполнителя (тогда важно время следующего
/// запуска), агент задал вопрос (сколько вопросов висит в чате) и задача ждёт своих подзадач.
/// Считает их <see cref="AiPause"/> — один и тот же код для списка и для формы задачи, иначе
/// два места разошлись бы в словах и в правилах.
/// </summary>
public sealed class T187Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Ai(string nick = "клод") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Ai }, null);

    private Executor Human(string nick = "миша") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private TaskItem NewTask(string title, Executor? executor = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст задания", "", null);

    /// <summary>Задача, которую агент выполняет прямо сейчас.</summary>
    private (TaskItem Task, Executor Agent, Job Job) Running(string title = "Работа")
    {
        var agent = Ai("клод-" + title);
        var task = NewTask(title, agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.Running, agent.Id);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, agent.Id);
        return (task, agent, _f.Jobs.Get(job.Id)!);
    }

    // ---------- 1. что попадает в список ----------

    [Fact]
    public void Running_Ai_Task_Is_In_The_List()
    {
        var (task, agent, job) = Running("Отчёт");

        var item = Assert.Single(_f.Tasks.AiWork());

        Assert.Equal(task.Id, item.Task.Id);
        Assert.Equal(task.DisplayId, item.Task.DisplayId);
        Assert.Equal(agent.Id, item.ExecutorId);
        Assert.Equal(agent.Nick, item.ExecutorNick);
        Assert.Equal(job.Id, item.JobId);
        // время работы считается от запуска задания
        Assert.Equal(job.StartedAt, item.StartedAt);
        Assert.False(item.Paused);
        Assert.Null(item.Pause);
    }

    [Fact]
    public void Queued_Ai_Task_Is_In_The_List_Too()
    {
        var agent = Ai();
        var task = NewTask("В очереди", agent);
        _f.Jobs.Create(task.Id, agent.Id, "", null);

        var item = Assert.Single(_f.Tasks.AiWork());

        Assert.Equal(task.Id, item.Task.Id);
        Assert.False(item.Paused);
    }

    [Fact]
    public void Human_Work_Is_Not_Ai_Work()
    {
        var human = Human();
        var task = NewTask("Ручная работа", human);
        var job = _f.Jobs.Create(task.Id, human.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, human.Id);

        Assert.Empty(_f.Tasks.AiWork());
    }

    [Fact]
    public void Finished_Task_Is_Not_In_The_List()
    {
        var (task, agent, job) = Running("Готовая");
        _f.Jobs.SetState(job.Id, JobState.Done, agent.Id);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Done, agent.Id);

        Assert.Empty(_f.Tasks.AiWork());
    }

    [Fact]
    public void Templates_Are_Never_In_The_List()
    {
        var agent = Ai();
        var template = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон",
            IsTemplate = true,
            ExecutorIds = [agent.Id],
        }, "текст", "", null);
        _f.Jobs.Create(template.Id, agent.Id, "", null);

        // задание по шаблону — случай невозможный, но список должен остаться списком ЗАДАЧ
        Assert.Empty(_f.Tasks.AiWork());
    }

    // ---------- 2. пауза: вопрос в чате ----------

    [Fact]
    public void Question_Pause_Counts_Questions_In_Chat()
    {
        var (task, agent, job) = Running("Вопрос");
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id, waitKind: JobWaitKinds.Human);
        _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "Какой формат отчёта?", ["md", "html"]);
        _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "Куда класть файлы?", []);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);

        var item = Assert.Single(_f.Tasks.AiWork());

        Assert.True(item.Paused);
        Assert.Equal(AiPauseKinds.Question, item.Pause!.Kind);
        Assert.Equal(2, item.Pause.Questions);
        Assert.Null(item.Pause.Until);
    }

    [Fact]
    public void Waiting_For_Another_Agent_Is_A_Question_Too()
    {
        var (task, agent, job) = Running("Вопрос агенту");
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id, waitKind: JobWaitKinds.Agent);
        _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "Готов ли макет?", []);

        var item = Assert.Single(_f.Tasks.AiWork());

        // на вопрос агенту всегда может ответить человек — для списка это тот же вопрос в чате
        Assert.Equal(AiPauseKinds.Question, item.Pause!.Kind);
        Assert.Equal(1, item.Pause.Questions);
    }

    // ---------- 3. пауза: подзадачи ----------

    [Fact]
    public void Subtasks_Pause_Is_Named_Its_Own_Way()
    {
        var (task, agent, job) = Running("Разбитая");
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id, waitKind: JobWaitKinds.Subtasks);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);

        var item = Assert.Single(_f.Tasks.AiWork());

        Assert.True(item.Paused);
        Assert.Equal(AiPauseKinds.Subtasks, item.Pause!.Kind);
        Assert.Equal(0, item.Pause.Questions);
    }

    // ---------- 4. пауза: лимит ----------

    [Fact]
    public void Limit_Pause_Shows_When_Work_Continues()
    {
        var (task, agent, job) = Running("Лимит");
        var until = DateTime.UtcNow.AddHours(2);
        // лимит оборвал работу: задание закрыто, старт перенесён, сессия агента жива (T-166)
        _f.Jobs.SetState(job.Id, JobState.Failed, agent.Id);
        _f.Tasks.SetStartAfter(task.Id, until);
        _f.Tasks.SetResumeJob(task.Id, job.Id);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);

        var item = Assert.Single(_f.Tasks.AiWork());

        Assert.True(item.Paused);
        Assert.Equal(AiPauseKinds.Limit, item.Pause!.Kind);
        Assert.Equal(until, item.Pause.Until!.Value, TimeSpan.FromSeconds(1));
        // работу продолжит тот же агент, и время работы считается от НАЧАЛА работы
        Assert.Equal(agent.Id, item.ExecutorId);
        Assert.Equal(job.StartedAt, item.StartedAt);
    }

    [Fact]
    public void Limit_Pause_Before_The_First_Job()
    {
        var agent = Ai();
        var task = NewTask("Перенесённая", agent);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(30));
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, null);

        var item = Assert.Single(_f.Tasks.AiWork());

        // задание не создавалось: исполнитель — назначенный задаче агент, работа не начиналась
        Assert.Equal(agent.Id, item.ExecutorId);
        Assert.Equal("", item.JobId);
        Assert.Null(item.StartedAt);
        Assert.Equal(AiPauseKinds.Limit, item.Pause!.Kind);
    }

    [Fact]
    public void Passed_Start_Time_Is_Not_Work()
    {
        var agent = Ai();
        var task = NewTask("Пора запускать", agent);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-5));
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, null);

        // время переноса прошло — задачу вот-вот поднимет сторож, но ИИ ею сейчас не занят
        Assert.Empty(_f.Tasks.AiWork());
    }

    [Fact]
    public void Deferred_Task_Without_Ai_Executor_Is_Not_Ai_Work()
    {
        var task = NewTask("Ничья", Human());
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddHours(1));

        Assert.Empty(_f.Tasks.AiWork());
    }

    // ---------- 5. список целиком ----------

    [Fact]
    public void List_Holds_All_Projects_And_Has_No_Filter()
    {
        var first = _f.Projects.Create("Первый", null, null, null);
        var second = _f.Projects.Create("Второй", null, null, null);
        foreach (var project in new[] { first, second })
        {
            var agent = Ai("клод-" + project.Name);
            var task = _f.Tasks.Create(new TaskItem
            {
                Title = "Задача " + project.Name,
                ProjectId = project.Id,
                ExecutorIds = [agent.Id],
            }, "текст", "", null);
            var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
            _f.Jobs.SetState(job.Id, JobState.Running, agent.Id);
        }

        var items = _f.Tasks.AiWork();

        Assert.Equal(2, items.Count);
        // сортируются ОБЕ стороны: id проектов — GUID, и порядок их создания с порядком строк
        // не связан. Сравнение отсортированного списка с несортированным давало зелёный тест
        // примерно через раз (поймано выпуском 1.80)
        Assert.Equal(new[] { first.Id, second.Id }.Order().ToArray(),
            items.Select(i => i.Task.ProjectId ?? "").Order().ToArray());
    }

    // ---------- 6. правила расчёта причины (общие для списка и формы задачи) ----------

    [Fact]
    public void Working_Agent_Has_No_Pause()
    {
        Assert.Null(AiPause.Of(waiting: false, "", startAfter: null, pendingQuestions: 0, DateTime.UtcNow));
    }

    [Fact]
    public void Limit_Wins_Over_Question()
    {
        var now = DateTime.UtcNow;

        var pause = AiPause.Of(waiting: true, JobWaitKinds.Human, now.AddHours(1), 3, now);

        // пока не сброшен лимит, ответ на вопрос агент всё равно не прочитает — человеку
        // важнее знать, когда работа продолжится
        Assert.Equal(AiPauseKinds.Limit, pause!.Kind);
    }

    [Fact]
    public void Old_Job_Without_Wait_Kind_Is_A_Question()
    {
        var now = DateTime.UtcNow;

        // задания старше T-185 причину ожидания не хранят — это всегда был вопрос человеку
        var pause = AiPause.Of(waiting: true, "", startAfter: null, pendingQuestions: 1, now);

        Assert.Equal(AiPauseKinds.Question, pause!.Kind);
        Assert.Equal(1, pause.Questions);
    }

    [Fact]
    public void Passed_Start_Time_Is_Not_A_Pause()
    {
        var now = DateTime.UtcNow;

        Assert.Null(AiPause.Of(waiting: false, "", now.AddMinutes(-1), 0, now));
    }
}
