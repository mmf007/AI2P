using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-165: INBOX — ВСЕ ВОПРОСЫ В ОДНОМ МЕСТЕ.
///
/// Случай заказчика: задача встала с вопросом агента в чате, а Inbox показывал «ничего».
/// Так и было задумано до T-165: <c>JobService.Inbox</c> отбирал только задания исполнителей-ЛЮДЕЙ
/// в waiting_human, а вопросы ИИ-агентов сознательно отфильтровывались (на них отвечали из
/// карточки задачи). Смысл Inbox — все вопросы в одном месте, поэтому теперь висящий вопрос
/// агента такой же элемент Inbox, как задание человеку, и ответить на него можно прямо оттуда.
///
/// Вопрос, чьё задание уже не ждёт (остановлено, упало), из Inbox не прячется — иначе он
/// потеряется совсем, — но помечается как «отвечать некому» (CanAnswer=false).
/// </summary>
public sealed class T165Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Human(string nick = "mike") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private Executor Ai(string nick = "клод") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Ai }, null);

    private TaskItem NewTask(string title = "Задача") =>
        _f.Tasks.Create(new TaskItem { Title = title }, "текст задания", "", null);

    /// <summary>Задача, по ней задание ИИ-агента, агент задал вопрос и ждёт ответа.</summary>
    private (TaskItem Task, Job Job, Executor Agent, ChatMessage Question) AskedTask(
        string title = "Задача", string question = "Какой формат отчёта?", string[]? options = null)
    {
        var agent = Ai("клод-" + title);
        var task = NewTask(title);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, agent.Id);
        var asked = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, question, options ?? ["md", "html"]);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);
        return (task, _f.Jobs.Get(job.Id)!, agent, asked);
    }

    // ---------- 1. вопрос агента виден в Inbox ----------

    [Fact]
    public void Agent_Question_Is_An_Inbox_Item()
    {
        var (task, job, agent, question) = AskedTask();

        var inbox = _f.Jobs.Inbox();

        var item = Assert.Single(inbox);
        Assert.Equal(InboxItemKinds.Question, item.Kind);
        Assert.Equal(question.Id, item.QuestionId);
        Assert.Equal("Какой формат отчёта?", item.QuestionText);
        Assert.Equal(["md", "html"], item.Options);
        Assert.True(item.CanAnswer);
        // по элементу видно, где вопрос задан и кто спрашивает
        Assert.Equal(task.DisplayId, item.TaskDisplayId);
        Assert.Equal(task.Title, item.TaskTitle);
        Assert.Equal(job.Id, item.Job.Id);
        Assert.Equal(agent.Nick, item.ExecutorNick);
        Assert.Equal(question.CreatedAt, item.CreatedAt);
    }

    [Fact]
    public void Question_Without_Options_Is_Shown_Too()
    {
        AskedTask(question: "Продолжать?", options: []);

        var item = Assert.Single(_f.Jobs.Inbox());
        Assert.Equal("Продолжать?", item.QuestionText);
        Assert.Empty(item.Options);
    }

    // ---------- 2. отвеченный вопрос уходит ----------

    [Fact]
    public void Answered_Question_Leaves_The_Inbox()
    {
        var (_, _, _, question) = AskedTask();
        var human = Human();
        Assert.Single(_f.Jobs.Inbox());

        _f.Chat.Answer(question.Id, human.Id, "md");

        Assert.Empty(_f.Jobs.Inbox());
    }

    [Fact]
    public void Cancelled_Job_Closes_Question_And_Empties_The_Inbox()
    {
        var (_, job, _, _) = AskedTask();
        Assert.Single(_f.Jobs.Inbox());

        // так делает отмена задания (ТЗ v1.17): висящие вопросы закрываются
        _f.Chat.ClosePendingByJob(job.Id);

        Assert.Empty(_f.Jobs.Inbox());
    }

    // ---------- 3. вопрос без живого агента: виден, но отвечать некому ----------

    [Fact]
    public void Question_Of_A_Failed_Job_Is_Visible_But_Not_Answerable()
    {
        var (_, job, agent, _) = AskedTask();

        _f.Jobs.SetState(job.Id, JobState.Failed, agent.Id);

        var item = Assert.Single(_f.Jobs.Inbox());
        Assert.Equal(InboxItemKinds.Question, item.Kind);
        Assert.False(item.CanAnswer); // продолжать некого — но вопрос из виду не пропал
    }

    // ---------- 4. задания людям остались как были ----------

    [Fact]
    public async Task Human_Job_Is_Still_An_Inbox_Item()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = Human();
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = human.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Сделать руками",
            ExecutorIds = [human.Id],
        }, "инструкция", "", null);

        var job = await _f.Orchestrator.StartTaskAsync(task.Id, human.Id);

        var item = Assert.Single(_f.Jobs.Inbox());
        Assert.Equal(InboxItemKinds.Job, item.Kind);
        Assert.Null(item.QuestionId);
        Assert.Equal("", item.QuestionText);
        Assert.True(item.CanAnswer);
        Assert.Equal(job.Id, item.Job.Id);
        Assert.Equal(human.Nick, item.ExecutorNick);
        Assert.Equal(job.CreatedAt, item.CreatedAt);
    }

    [Fact]
    public void Ai_Job_Without_A_Question_Does_Not_Show_Up()
    {
        // задание ИИ само по себе — не элемент Inbox: человеку там нечего делать,
        // пока агент не спросил (иначе Inbox превратился бы в список работающих агентов)
        var agent = Ai();
        var task = NewTask();
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.Running, agent.Id);

        Assert.Empty(_f.Jobs.Inbox());
    }

    // ---------- 5. общий список: всё вместе, новое сверху ----------

    [Fact]
    public async Task Jobs_And_Questions_Are_One_List_Newest_First()
    {
        var human = Human();
        var task = _f.Tasks.Create(new TaskItem { Title = "Руками", ExecutorIds = [human.Id] }, "и", "", null);
        var humanJob = _f.Jobs.Create(task.Id, human.Id, "", null);
        _f.Jobs.SetState(humanJob.Id, JobState.WaitingHuman, human.Id);
        await Task.Delay(20);
        var (_, _, _, question) = AskedTask("Спросили позже");

        var inbox = _f.Jobs.Inbox();

        Assert.Equal(2, inbox.Count);
        Assert.Equal(question.Id, inbox[0].QuestionId);          // вопрос задан позже — он сверху
        Assert.Equal(InboxItemKinds.Job, inbox[1].Kind);
        Assert.True(inbox[0].CreatedAt >= inbox[1].CreatedAt);
    }

    [Fact]
    public void Several_Questions_From_Different_Tasks_Are_All_There()
    {
        AskedTask("Первая", "Вопрос 1");
        AskedTask("Вторая", "Вопрос 2");
        AskedTask("Третья", "Вопрос 3");

        var inbox = _f.Jobs.Inbox();

        Assert.Equal(3, inbox.Count);
        Assert.All(inbox, i => Assert.Equal(InboxItemKinds.Question, i.Kind));
        Assert.Equal(["Вопрос 1", "Вопрос 2", "Вопрос 3"],
            inbox.Select(i => i.QuestionText).OrderBy(t => t, StringComparer.Ordinal));
    }

    // ---------- 6. фильтр по человеку ----------

    [Fact]
    public void Executor_Filter_Keeps_Questions_For_Everyone()
    {
        // задание человека отбирается по исполнителю, а вопрос агента адресован любому
        // человеку (to_executor_id у него не проставлен) — и приходит всем
        var mike = Human("mike");
        var other = Human("другой");
        var task = _f.Tasks.Create(new TaskItem { Title = "Руками", ExecutorIds = [mike.Id] }, "и", "", null);
        var job = _f.Jobs.Create(task.Id, mike.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.WaitingHuman, mike.Id);
        AskedTask("С вопросом");

        Assert.Equal(2, _f.Jobs.Inbox(mike.Id).Count);
        var forOther = Assert.Single(_f.Jobs.Inbox(other.Id));
        Assert.Equal(InboxItemKinds.Question, forOther.Kind);
    }

    // ---------- 7. представление Inbox ----------

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

    private static string InboxView() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", "InboxView.razor"));

    [Fact]
    public void View_Renders_Questions_And_Answers_Them_In_Place()
    {
        var view = InboxView();
        // опоры живой проверки
        Assert.Contains("data-inbox-item=\"@item.Kind\"", view, StringComparison.Ordinal);
        Assert.Contains("data-inbox-question=\"1\"", view, StringComparison.Ordinal);
        Assert.Contains("data-inbox-stale=\"1\"", view, StringComparison.Ordinal);
        // ответ уходит тем же путём, что из чата задачи, — вопрос закрывается, агент продолжает
        Assert.Contains("Api.AnswerQuestionAsync(questionId", view, StringComparison.Ordinal);
        // варианты ответа — кнопками
        Assert.Contains("data-inbox-option=\"@option\"", view, StringComparison.Ordinal);
        // список перечитывается сам: вопрос появляется, пока Inbox уже открыт
        Assert.Contains("StartPolling", view, StringComparison.Ordinal);
        Assert.Contains("IDisposable", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_Languages_Have_The_New_Labels()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var json = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json")));
            foreach (var key in new[] { "inbox.question", "inbox.questionStale", "inbox.answerQuestion" })
            {
                Assert.True(json.RootElement.TryGetProperty(key, out var value) &&
                            value.GetString()!.Length > 0, $"{lang}.json: нет ключа {key}");
            }
        }
    }
}
