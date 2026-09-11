using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Накопление опыта по шаблонам (ТЗ п. 2.11, todo32): CRUD записей опыта с авторством,
/// блок «Опыт выполнения» в промпте задания, инструменты агента create/update_experience
/// (публикуются только задачам из шаблона), статистика смен состояния по шаблону.
/// </summary>
public sealed class Todo32Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem CreateTemplate(string title = "Шаблон") =>
        _f.Tasks.Create(new TaskItem { Title = title, IsTemplate = true }, "", "", null);

    // ---------- CRUD записей опыта ----------

    [Fact]
    public void Experience_Create_Update_Delete_With_Authorship()
    {
        var template = CreateTemplate();
        var owner = _f.Executors.EnsureLocalOwner();

        var record = _f.Experience.Create(template.Id, "первый урок", owner.Id);
        Assert.Equal(owner.Id, record.CreatedBy);
        Assert.Equal(owner.Id, record.UpdatedBy);

        var agent = _f.Executors.Create(new Executor { Nick = "agent-x", Kind = ExecutorKind.Human }, null);
        var updated = _f.Experience.Update(record.Id, "уточнённый урок", agent.Id);
        Assert.Equal("уточнённый урок", updated.Text);
        Assert.Equal(owner.Id, updated.CreatedBy);   // автор создания не меняется
        Assert.Equal(agent.Id, updated.UpdatedBy);   // кто изменил — запомнен

        _f.Experience.Delete(record.Id, owner.Id);
        Assert.Empty(_f.Experience.ListByTemplate(template.Id));

        // события журнала: создание, правка, удаление
        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceRecorded));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceUpdated));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceDeleted));
    }

    [Fact]
    public void Experience_Only_For_Template_Nodes()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Обычная" }, "", "", null);
        Assert.Throws<ArgumentException>(() => _f.Experience.Create(task.Id, "текст", null));
        Assert.Throws<ArgumentException>(() => _f.Experience.Create(task.Id, "  ", null));
    }

    [Fact]
    public void Experience_List_Includes_Subtree_On_Request()
    {
        var head = CreateTemplate("Голова");
        var child = _f.Tasks.Create(new TaskItem { ParentId = head.Id, Title = "Шаг", IsTemplate = true },
            "", "", null);
        _f.Experience.Create(head.Id, "опыт головы", null);
        _f.Experience.Create(child.Id, "опыт шага", null);

        Assert.Single(_f.Experience.ListByTemplate(head.Id));
        var subtree = _f.Experience.ListByTemplate(head.Id, includeSubtree: true);
        Assert.Equal(2, subtree.Count);
        Assert.Single(_f.Experience.ListByTemplate(child.Id));
    }

    // ---------- опыт в промпте задания ----------

    [Fact]
    public void Experience_Block_Is_Built_For_Templated_Tasks()
    {
        var template = CreateTemplate();
        var record = _f.Experience.Create(template.Id, "не забыть про кэш", null);

        var block = JobOrchestrator.ExperienceBlock(_f.Experience, template.Id);
        Assert.Contains("Опыт выполнения", block);
        Assert.Contains("не забыть про кэш", block);
        Assert.Contains(record.Id, block); // id — для update_experience

        Assert.Equal("", JobOrchestrator.ExperienceBlock(_f.Experience, null));      // не из шаблона
        Assert.Equal("", JobOrchestrator.ExperienceBlock(_f.Experience, template.Id + "x"));
    }

    // ---------- инструменты агента ----------

    private TaskToolset ToolsetFor(TaskItem current, string? actorId = null) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
            actorExecutorId: actorId, experience: _f.Experience);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Agent_Tools_Create_And_Update_Experience()
    {
        var template = CreateTemplate();
        var task = _f.Tasks.InstantiateTemplate(template.Id, null, null);
        var agent = _f.Executors.Create(new Executor { Nick = "bot-exp", Kind = ExecutorKind.Human }, null);
        var tools = ToolsetFor(task, agent.Id);
        Assert.True(tools.CanExperience);
        Assert.Contains("накапливается опыт", tools.ExperienceNote);

        var created = await tools.ExecuteAsync("create_experience",
            Args("""{"text":"урок от агента"}"""), CancellationToken.None);
        Assert.Contains("добавлена", created);
        var record = _f.Experience.ListByTemplate(template.Id).Single();
        Assert.Equal("урок от агента", record.Text);
        Assert.Equal(agent.Id, record.CreatedBy);

        var updated = await tools.ExecuteAsync("update_experience",
            Args($$"""{"id":"{{record.Id}}","text":"поправленный урок"}"""), CancellationToken.None);
        Assert.Contains("обновлена", updated);
        Assert.Equal("поправленный урок", _f.Experience.Get(record.Id)!.Text);

        // чужой/несуществующий id — понятная ошибка
        var missing = await tools.ExecuteAsync("update_experience",
            Args("""{"id":"nope","text":"x"}"""), CancellationToken.None);
        Assert.StartsWith("Ошибка", missing);
    }

    [Fact]
    public async Task Experience_Tools_Unavailable_Without_Template()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Без шаблона" }, "", "", null);
        var tools = ToolsetFor(task);
        Assert.False(tools.CanExperience);
        Assert.Equal("", tools.ExperienceNote);

        var result = await tools.ExecuteAsync("create_experience",
            Args("""{"text":"не должно записаться"}"""), CancellationToken.None);
        Assert.StartsWith("Ошибка", result);

        // публикация инструментов: у задачи из шаблона есть, у обычной — нет
        var agentTools = new AgentToolset(null, tools);
        Assert.DoesNotContain(agentTools.Specs, s => s.Name == "create_experience");

        var template = CreateTemplate();
        var fromTemplate = _f.Tasks.InstantiateTemplate(template.Id, null, null);
        var withExp = new AgentToolset(null, ToolsetFor(fromTemplate));
        Assert.Contains(withExp.Specs, s => s.Name == "create_experience");
        Assert.Contains(withExp.Specs, s => s.Name == "update_experience");
    }

    [Fact]
    public void Experience_Actions_Are_Seeded_For_Security_Rules()
    {
        // коды действий — для правил безопасности (deny/confirm на создание/правку опыта)
        Assert.Equal("AI2P.Experience.Create", _f.Actions.CodeByTool("create_experience"));
        Assert.Equal("AI2P.Experience.Update", _f.Actions.CodeByTool("update_experience"));
    }

    // ---------- статистика по шаблону ----------

    [Fact]
    public void Template_Stats_Collect_Status_Changes_Of_Templated_Tasks()
    {
        var owner = _f.Executors.EnsureLocalOwner();
        var head = CreateTemplate("Голова");
        _f.Tasks.Create(new TaskItem { ParentId = head.Id, Title = "Шаг", IsTemplate = true },
            "", "", null);

        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null);
        var copyChild = _f.Tasks.List().Single(t => t.ParentId == copy.Id);
        _f.Tasks.ChangeStatus(copy.Id, TaskStatuses.InProgress, owner.Id);
        _f.Tasks.ChangeStatus(copyChild.Id, TaskStatuses.Done, owner.Id);

        // посторонняя задача в статистику шаблона не попадает
        var stranger = _f.Tasks.Create(new TaskItem { Title = "Чужая" }, "", "", null);
        _f.Tasks.ChangeStatus(stranger.Id, TaskStatuses.InProgress, owner.Id);

        var stats = _f.Tasks.TemplateStatusStats(head.Id);
        Assert.Equal(2, stats.Count);
        Assert.Contains(stats, s => s.TaskDisplayId == copy.DisplayId && s.Status == "in_progress");
        Assert.Contains(stats, s => s.TaskDisplayId == copyChild.DisplayId && s.Status == "done");
        Assert.All(stats, s => Assert.Equal(owner.Id, s.ExecutorId));
    }
}
