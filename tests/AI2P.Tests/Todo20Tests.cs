using AI2P.Core.Entities;
using AI2P.Core.Events;
using Xunit;

namespace AI2P.Tests;

/// <summary>Каталог хранилища проекта (ТЗ v1.17, todo20; правка todo37): назван внешним
/// кодом проекта и за названием больше НЕ следует — переименование файлы не двигает.</summary>
public sealed class ProjectRenameTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Rename_Keeps_Storage_Dir_Named_By_Display_Id()
    {
        var project = _f.Projects.Create("Старый проект", null, null, null);
        Assert.Equal(project.DisplayId, project.Slug);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "критерии", null);
        Assert.StartsWith($"projects/{project.DisplayId}/", task.DescriptionPath);
        Assert.True(Directory.Exists(_f.Files.Abs($"projects/{project.DisplayId}")));

        project.Name = "Новый проект";
        var updated = _f.Projects.Update(project, null);

        // слаг и каталог — прежние (todo37): код проекта не меняется
        Assert.Equal(project.DisplayId, updated.Slug);
        Assert.True(Directory.Exists(_f.Files.Abs($"projects/{project.DisplayId}")));

        var reloaded = _f.Tasks.Get(task.Id)!;
        Assert.StartsWith($"projects/{project.DisplayId}/", reloaded.DescriptionPath);
        Assert.Equal("описание", _f.Tasks.ReadDescription(reloaded));
        Assert.Empty(_f.Events.Query(eventType: EventTypes.ProjectRenamed));
    }

    [Fact]
    public void Update_Without_Rename_Keeps_Slug()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var slug = project.Slug;
        project.IsActive = false;
        var updated = _f.Projects.Update(project, null);
        Assert.Equal(slug, updated.Slug);
        Assert.Empty(_f.Events.Query(eventType: EventTypes.ProjectRenamed));
    }
}

/// <summary>Иерархия команды и тимлид (ТЗ v1.17, todo20).</summary>
public sealed class TeamHierarchyTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor NewHuman(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    [Fact]
    public void Single_Root_Becomes_Lead_Automatically()
    {
        var boss = NewHuman("босс");
        var worker = NewHuman("работник");
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members =
            [
                new TeamMember { ExecutorId = boss.Id },
                new TeamMember { ExecutorId = worker.Id, ParentExecutorId = boss.Id },
            ],
        }, null);

        var reloaded = _f.Teams.Get(team.Id)!;
        Assert.True(reloaded.Members.Single(m => m.ExecutorId == boss.Id).IsLead);
        Assert.False(reloaded.Members.Single(m => m.ExecutorId == worker.Id).IsLead);
        Assert.Equal(boss.Id, reloaded.Members.Single(m => m.ExecutorId == worker.Id).ParentExecutorId);
    }

    [Fact]
    public void Cycle_In_Hierarchy_Is_Rejected()
    {
        var a = NewHuman("а");
        var b = NewHuman("б");
        var ex = Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team
        {
            Name = "Циклы",
            Members =
            [
                new TeamMember { ExecutorId = a.Id, ParentExecutorId = b.Id },
                new TeamMember { ExecutorId = b.Id, ParentExecutorId = a.Id },
            ],
        }, null));
        Assert.Contains("Цикл", ex.Message);
    }

    [Fact]
    public void Lead_Flag_On_Subordinate_Is_Cleared()
    {
        var a = NewHuman("рук");
        var b = NewHuman("под");
        var c = NewHuman("равный");
        var team = _f.Teams.Create(new Team
        {
            Name = "Одноранговая",
            Members =
            [
                new TeamMember { ExecutorId = a.Id },
                new TeamMember { ExecutorId = b.Id, ParentExecutorId = a.Id, IsLead = true },
                new TeamMember { ExecutorId = c.Id, IsLead = true },
            ],
        }, null);

        var reloaded = _f.Teams.Get(team.Id)!;
        // у подчинённого признак снят; на верхнем уровне двое — тимлид тот, кого пометили
        Assert.False(reloaded.Members.Single(m => m.ExecutorId == b.Id).IsLead);
        Assert.True(reloaded.Members.Single(m => m.ExecutorId == c.Id).IsLead);
    }

    [Fact]
    public void Duplicate_Member_Is_Rejected_With_Clear_Error()
    {
        // багфикс v1.18 (todo21): дубликат исполнителя ронял ToDictionary (крах circuit'а в UI);
        // теперь — понятная ошибка валидации
        var a = NewHuman("дубль");
        var ex = Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team
        {
            Name = "Дубликаты",
            Members =
            [
                new TeamMember { ExecutorId = a.Id },
                new TeamMember { ExecutorId = a.Id },
            ],
        }, null));
        Assert.Contains("дубликат", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_Member_Is_Rejected_With_Clear_Error()
    {
        var ex = Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team
        {
            Name = "Пустой участник",
            Members = [new TeamMember { ExecutorId = "" }],
        }, null));
        Assert.Contains("не выбран", ex.Message);
    }

    [Fact]
    public void Two_Leads_Are_Rejected()
    {
        var a = NewHuman("л1");
        var b = NewHuman("л2");
        var c = NewHuman("л3");
        Assert.Throws<ArgumentException>(() => _f.Teams.Create(new Team
        {
            Name = "Два лида",
            Members =
            [
                new TeamMember { ExecutorId = a.Id, IsLead = true },
                new TeamMember { ExecutorId = b.Id, IsLead = true },
                new TeamMember { ExecutorId = c.Id },
            ],
        }, null));
    }
}

/// <summary>Вопросы ИИ-агентов в чате задачи (ТЗ v1.17, todo20).</summary>
public sealed class ChatQuestionTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private (TaskItem Task, Job Job, Executor Ai, Executor Human) NewTaskWithJob()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var ai = _f.Executors.Create(new Executor { Nick = "ии", Kind = ExecutorKind.Human }, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" }, "т", "", null);
        var job = _f.Jobs.Create(task.Id, ai.Id, "", null);
        return (task, job, ai, human);
    }

    [Fact]
    public void Question_Is_Pending_Until_Answered_And_Counted_In_Lists()
    {
        var (task, job, ai, human) = NewTaskWithJob();

        var question = _f.Chat.AddQuestion(task.Id, ai.Id, job.Id, "Какой формат?", ["md", "html"]);
        Assert.Equal(ChatMessageKind.Question, question.Kind);
        Assert.Equal(["md", "html"], question.Options());
        Assert.Null(question.AnsweredAt);

        // счётчик висящих вопросов в списках и карточке (ТЗ v1.17)
        Assert.Equal(1, _f.Tasks.Get(task.Id)!.PendingQuestions);
        Assert.Equal(1, _f.Tasks.List(task.ProjectId).Single().PendingQuestions);

        var (answered, answer) = _f.Chat.Answer(question.Id, human.Id, "md");
        Assert.NotNull(answered.AnsweredAt);
        Assert.Equal(ChatMessageKind.Answer, answer.Kind);
        Assert.Equal(question.Id, answer.AnswerToId);
        Assert.Equal(0, _f.Tasks.Get(task.Id)!.PendingQuestions);

        // повторный ответ отклоняется
        Assert.Throws<InvalidOperationException>(() => _f.Chat.Answer(question.Id, human.Id, "html"));

        // журнал: chat.question + chat.answer
        Assert.Single(_f.Events.Query(eventType: EventTypes.ChatQuestion));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ChatAnswer));
    }

    [Fact]
    public void ClosePendingByJob_Clears_Counter()
    {
        var (task, job, ai, _) = NewTaskWithJob();
        _f.Chat.AddQuestion(task.Id, ai.Id, job.Id, "Вопрос?", []);
        Assert.Equal(1, _f.Tasks.Get(task.Id)!.PendingQuestions);

        _f.Chat.ClosePendingByJob(job.Id);
        Assert.Equal(0, _f.Tasks.Get(task.Id)!.PendingQuestions);
    }

    [Fact]
    public void PendingQuestionByJob_Finds_Open_Question()
    {
        var (task, job, ai, _) = NewTaskWithJob();
        Assert.Null(_f.Chat.PendingQuestionByJob(job.Id));
        var question = _f.Chat.AddQuestion(task.Id, ai.Id, job.Id, "Вопрос?", []);
        Assert.Equal(question.Id, _f.Chat.PendingQuestionByJob(job.Id)!.Id);
    }
}

/// <summary>Справочники skills / io formats (ТЗ v1.17, todo20).</summary>
public sealed class RefDataTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Fills_IoFormats_And_Marks_Builtin()
    {
        var formats = _f.RefData.IoFormats();
        Assert.Contains(formats, f => f.Name == "text/markdown" && !f.IsCustom);
        Assert.Contains(formats, f => f.Name == "application/pdf" && !f.IsCustom);

        var skills = _f.RefData.Skills();
        Assert.Contains(skills, s => s.Name == "code-write" && !s.IsCustom);
    }

    [Fact]
    public void User_Added_Items_Are_Custom_And_Unique()
    {
        _f.RefData.AddIoFormat("model/gltf", "модель glTF");
        var format = _f.RefData.IoFormats().Single(f => f.Name == "model/gltf");
        Assert.True(format.IsCustom);

        // повторное добавление того же кода не создаёт дубль
        _f.RefData.AddIoFormat("model/gltf", "дубль");
        Assert.Single(_f.RefData.IoFormats(), f => f.Name == "model/gltf");

        _f.RefData.AddSkill("compose-music", "сочинение музыки");
        Assert.True(_f.RefData.Skills().Single(s => s.Name == "compose-music").IsCustom);
    }
}
