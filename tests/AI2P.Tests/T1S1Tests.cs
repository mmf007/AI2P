using AI2P.Core.Entities;
using AI2P.Core.Events;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-1-S1: ВИСЯЧИЙ ВОПРОС В ЧАТЕ — КНОПКА «СНЯТЬ ВОПРОС».
///
/// Случай заказчика (задача T-191): в чате задачи висит вопрос агента, но на попытку ответить
/// система отвечает «задание уже не ждёт ответа (остановлено или завершено)». Ответить нельзя,
/// а закрыть вопрос было нечем — и он навсегда оставался в счётчике вопросов задачи (чип в
/// дереве, доске и таблице, бейдж вкладки «Чат») и в Inbox.
///
/// Теперь такой вопрос СНИМАЕТСЯ: он закрывается без ответа (answered_at) и разом уходит из
/// всех признаков «висит вопрос». Пока задание ЖДЁТ ответа, снимать нечего — на вопрос нужно
/// отвечать (агент продолжит работу) или останавливать задание.
/// </summary>
public sealed class T1S1Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Human(string nick = "mike") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private Executor Ai(string nick = "клод") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Ai }, null);

    /// <summary>Задача, задание ИИ-агента по ней и висящий вопрос в чате.</summary>
    private (TaskItem Task, Job Job, Executor Agent, ChatMessage Question) AskedTask(
        string title = "Задача", string question = "Какой формат отчёта?")
    {
        var agent = Ai("клод-" + title);
        var task = _f.Tasks.Create(new TaskItem { Title = title }, "текст задания", "", null);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id);
        var asked = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, question, ["md", "html"]);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);
        return (task, _f.Jobs.Get(job.Id)!, agent, asked);
    }

    /// <summary>То же, но агента уже нет: задание упало — ровно случай заказчика.</summary>
    private (TaskItem Task, Job Job, Executor Agent, ChatMessage Question) FailedAskedTask(
        string title = "Задача")
    {
        var (task, job, agent, question) = AskedTask(title);
        _f.Jobs.SetState(job.Id, JobState.Failed, agent.Id);
        return (task, _f.Jobs.Get(job.Id)!, agent, question);
    }

    // ---------- 1. вопрос уходит из ВСЕХ признаков «висит вопрос» ----------

    [Fact]
    public void Dismissed_Question_Leaves_The_Task_Counter()
    {
        var (task, _, _, question) = FailedAskedTask();
        Assert.Equal(1, _f.Tasks.Get(task.Id)!.PendingQuestions);

        _f.Orchestrator.DismissQuestion(question.Id, Human().Id);

        // счётчик вопросов задачи — это чип в дереве, на доске, в таблице и бейдж вкладки «Чат»
        Assert.Equal(0, _f.Tasks.Get(task.Id)!.PendingQuestions);
        Assert.Equal(0, _f.Tasks.List().Single(t => t.Id == task.Id).PendingQuestions);
    }

    [Fact]
    public void Dismissed_Question_Leaves_The_Inbox()
    {
        var (_, _, _, question) = FailedAskedTask();
        var stale = Assert.Single(_f.Jobs.Inbox());
        Assert.False(stale.CanAnswer); // до снятия: висит и отвечать некому

        _f.Orchestrator.DismissQuestion(question.Id, Human().Id);

        Assert.Empty(_f.Jobs.Inbox());
    }

    [Fact]
    public void Dismissed_Question_Is_Not_Pending_For_The_Job_Anymore()
    {
        var (_, job, _, question) = FailedAskedTask();
        Assert.NotNull(_f.Chat.PendingQuestionByJob(job.Id));

        _f.Orchestrator.DismissQuestion(question.Id, Human().Id);

        // после перезапуска приложения ожидание по этому заданию не восстановится
        Assert.Null(_f.Chat.PendingQuestionByJob(job.Id));
    }

    [Fact]
    public void Dismissed_Question_Stays_In_The_Chat()
    {
        var (task, _, _, question) = FailedAskedTask();

        _f.Orchestrator.DismissQuestion(question.Id, Human().Id);

        // сам вопрос из переписки не пропадает — пропадает только ожидание ответа
        var message = Assert.Single(_f.Chat.ListByTask(task.Id));
        Assert.Equal(question.Id, message.Id);
        Assert.Equal(ChatMessageKind.Question, message.Kind);
        Assert.NotNull(message.AnsweredAt);
    }

    // ---------- 2. снятый вопрос отличается от отвеченного ----------

    [Fact]
    public void Dismissed_Question_Has_No_Reply_But_An_Answered_One_Has()
    {
        var (_, _, _, dismissed) = FailedAskedTask("Снятая");
        var (_, _, _, answered) = AskedTask("Отвеченная");
        var human = Human();

        _f.Orchestrator.DismissQuestion(dismissed.Id, human.Id);
        _f.Chat.Answer(answered.Id, human.Id, "md");

        // по этому признаку карточка задачи пишет «вопрос снят», а не «ответ получен»
        Assert.False(_f.Chat.HasReply(dismissed.Id));
        Assert.True(_f.Chat.HasReply(answered.Id));
    }

    [Fact]
    public void Dismissal_Is_Recorded_In_The_Work_Log()
    {
        var (task, _, _, question) = FailedAskedTask();
        var human = Human();

        _f.Orchestrator.DismissQuestion(question.Id, human.Id);

        var record = Assert.Single(_f.Events.Query(taskId: task.Id),
            e => e.EventType == EventTypes.ChatQuestionDismissed);
        Assert.Equal(human.Id, record.ActorId);
        Assert.Equal(question.Id, record.EntityId);
    }

    // ---------- 3. пока агент ЖДЁТ, снимать нечего ----------

    [Fact]
    public void Question_Of_A_Waiting_Job_Cannot_Be_Dismissed()
    {
        var (task, _, _, question) = AskedTask();

        var ex = Assert.Throws<InvalidOperationException>(
            () => _f.Orchestrator.DismissQuestion(question.Id, Human().Id));

        // на такой вопрос отвечают (агент продолжит работу) либо останавливают задание
        Assert.Contains("ещё ждёт", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, _f.Tasks.Get(task.Id)!.PendingQuestions);
    }

    [Fact]
    public void Cancelled_Job_Question_Can_Be_Dismissed_Too()
    {
        // остановленное задание закрывает свои вопросы само (ТЗ v1.17), но если вопрос
        // всё же остался висеть (задание завершилось иначе) — снять его можно
        var (_, job, agent, question) = AskedTask();
        _f.Jobs.SetState(job.Id, JobState.Done, agent.Id);

        _f.Orchestrator.DismissQuestion(question.Id, Human().Id);

        Assert.Empty(_f.Jobs.Inbox());
    }

    // ---------- 4. отказы ----------

    [Fact]
    public void Dismissing_Twice_Is_Refused()
    {
        var (_, _, _, question) = FailedAskedTask();
        var human = Human();
        _f.Orchestrator.DismissQuestion(question.Id, human.Id);

        Assert.Throws<InvalidOperationException>(
            () => _f.Orchestrator.DismissQuestion(question.Id, human.Id));
    }

    [Fact]
    public void Answered_Question_Cannot_Be_Dismissed()
    {
        var (_, job, agent, question) = AskedTask();
        var human = Human();
        _f.Chat.Answer(question.Id, human.Id, "md");
        _f.Jobs.SetState(job.Id, JobState.Done, agent.Id);

        Assert.Throws<InvalidOperationException>(
            () => _f.Orchestrator.DismissQuestion(question.Id, human.Id));
    }

    [Fact]
    public void Only_A_Question_Can_Be_Dismissed()
    {
        var human = Human();
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        var message = _f.Chat.Add(task.Id, human.Id, null, "обычное сообщение");

        Assert.Throws<InvalidOperationException>(
            () => _f.Orchestrator.DismissQuestion(message.Id, human.Id));
    }

    [Fact]
    public void Unknown_Question_Is_Refused()
    {
        Assert.Throws<InvalidOperationException>(
            () => _f.Orchestrator.DismissQuestion("нет-такого", Human().Id));
    }

    // ---------- 5. представления и словари ----------

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
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

    private static string View(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", name));

    [Fact]
    public void Task_Card_Shows_The_Dismiss_Button_On_A_Stale_Question()
    {
        var view = View("TaskCardView.razor");
        // опоры живой проверки
        Assert.Contains("data-task-question-stale=\"1\"", view, StringComparison.Ordinal);
        Assert.Contains("data-task-question-dismiss=\"1\"", view, StringComparison.Ordinal);
        // кнопка показывается вместо поля ответа — ровно у вопроса без ждущего задания
        Assert.Contains("isPending && IsStaleQuestion(message)", view, StringComparison.Ordinal);
        Assert.Contains("Api.DismissQuestionAsync(questionId)", view, StringComparison.Ordinal);
        // закрытый вопрос: отвеченный отличается от снятого
        Assert.Contains("task.chat.dismissed", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_Shows_The_Dismiss_Button_On_A_Stale_Question()
    {
        var view = View("InboxView.razor");
        Assert.Contains("data-inbox-dismiss=\"1\"", view, StringComparison.Ordinal);
        Assert.Contains("Api.DismissQuestionAsync(questionId)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_Languages_Have_The_New_Labels()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json")));
            foreach (var key in new[]
                     {
                         "task.chat.stale", "task.chat.dismiss", "task.chat.dismissed",
                         "inbox.dismiss", "msg.chat.5", "msg.chat.6", "msg.jobOrchestrator.23",
                     })
            {
                Assert.True(json.RootElement.TryGetProperty(key, out var value) &&
                            value.GetString()!.Length > 0, $"{lang}.json: нет ключа {key}");
            }
        }
    }
}
