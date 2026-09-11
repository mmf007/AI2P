using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Создание задач из шаблона (ТЗ п. 2.4, todo31): анализ иерархии шаблона (что
/// переспрашивать), пересчёт сроков от самой ранней даты, автоподбор исполнителей
/// (в т.ч. замена неактивных), чат шаблона не копируется.
/// </summary>
public sealed class Todo31Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor CreateAi(string nick, string scopeJson)
    {
        var scopeRel = $"models/scope_test_{nick}.json";
        _f.Files.WriteText(scopeRel, scopeJson);
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
        }, null);
    }

    private Team CreateTeam(params Executor[] members) =>
        _f.Teams.Create(new Team
        {
            Name = "Команда-" + Guid.NewGuid().ToString("N")[..6],
            Members = members.Select(e => new TeamMember { ExecutorId = e.Id }).ToList(),
        }, null);

    private const string WriteCodeScope = """
        { "skills": [ { "name": "code-write", "score": 90 } ],
          "cost": { "in_per_1m": 1, "out_per_1m": 2 } }
        """;

    private string SkillId(string name) => _f.RefData.Skills().First(s => s.Name == name).Id;

    private void Deactivate(Executor executor)
    {
        executor.IsActive = false;
        _f.Executors.Update(executor, null);
    }

    // ---------- анализ иерархии ----------

    [Fact]
    public void AnalyzeTemplate_Reports_Dates_And_Pick_Need()
    {
        var childDue = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var grandDue = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
        var head = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ParentId = head.Id,
            Title = "С датой и skills",
            IsTemplate = true,
            DueDate = childDue,
            SkillIds = [SkillId("code-write")],
        }, "", "", null);
        _f.Tasks.Create(new TaskItem
        {
            ParentId = child.Id,
            Title = "С более ранней датой",
            IsTemplate = true,
            DueDate = grandDue,
        }, "", "", null);

        var info = _f.Tasks.AnalyzeTemplate(head.Id);
        Assert.True(info.HasDueDates);
        Assert.Equal(grandDue, info.MinDueDate); // самая ранняя дата — из глубины иерархии
        Assert.True(info.NeedsExecutorPick);     // skills без исполнителя

        // шаблон без дат и skills — переспрашивать нечего
        var plain = _f.Tasks.Create(new TaskItem { Title = "Пустой", IsTemplate = true }, "", "", null);
        var plainInfo = _f.Tasks.AnalyzeTemplate(plain.Id);
        Assert.False(plainInfo.HasDueDates);
        Assert.False(plainInfo.NeedsExecutorPick);
        Assert.Null(plainInfo.MinDueDate);

        // не-шаблон отвергается
        var task = _f.Tasks.Create(new TaskItem { Title = "Обычная" }, "", "", null);
        Assert.Throws<ArgumentException>(() => _f.Tasks.AnalyzeTemplate(task.Id));
    }

    [Fact]
    public void AnalyzeTemplate_Skills_With_Active_Executor_Do_Not_Need_Pick()
    {
        var ai = CreateAi("bot-active", WriteCodeScope);
        var team = CreateTeam(ai);
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон с исполнителем",
            IsTemplate = true,
            TeamId = team.Id,
            ExecutorIds = [ai.Id],
            SkillIds = [SkillId("code-write")],
        }, "", "", null);

        Assert.False(_f.Tasks.AnalyzeTemplate(head.Id).NeedsExecutorPick);

        // исполнитель стал неактивным — подбор снова нужен
        Deactivate(ai);
        Assert.True(_f.Tasks.AnalyzeTemplate(head.Id).NeedsExecutorPick);
    }

    // ---------- пересчёт сроков от самой ранней даты ----------

    [Fact]
    public void BaseDate_Anchors_To_Earliest_Date_In_Hierarchy()
    {
        var head = _f.Tasks.Create(new TaskItem { Title = "Голова без даты", IsTemplate = true },
            "", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ParentId = head.Id,
            Title = "Ранний шаг",
            IsTemplate = true,
            DueDate = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
        }, "", "", null);
        _f.Tasks.Create(new TaskItem
        {
            ParentId = child.Id,
            Title = "Поздний шаг",
            IsTemplate = true,
            DueDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
        }, "", "", null);

        var baseDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var newHead = _f.Tasks.InstantiateTemplate(head.Id, baseDate, null);

        // базовая встала на место самой ранней (5 июля), интервал 5 дней сохранён
        Assert.Null(newHead.DueDate);
        var copies = _f.Tasks.List();
        var newChild = copies.Single(t => t.ParentId == newHead.Id);
        var newGrand = copies.Single(t => t.ParentId == newChild.Id);
        Assert.Equal(baseDate, newChild.DueDate);
        Assert.Equal(baseDate.AddDays(5), newGrand.DueDate);
    }

    // ---------- исполнители: неактивные и автоподбор ----------

    [Fact]
    public void Inactive_Executor_Is_Replaced_By_Pick()
    {
        var old = CreateAi("bot-old", WriteCodeScope);
        var fresh = CreateAi("bot-fresh", WriteCodeScope);
        var team = CreateTeam(old, fresh);
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон",
            IsTemplate = true,
            TeamId = team.Id,
            ExecutorIds = [old.Id],
            SkillIds = [SkillId("code-write")],
        }, "", "", null);
        Deactivate(old);

        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null, PickMode.AiFirst, _f.Picker);

        Assert.Equal([fresh.Id], copy.ExecutorIds); // неактивный заменён подобранным
    }

    [Fact]
    public void Inactive_Executor_And_Responsible_Are_Dropped_Without_Pick()
    {
        var ai = CreateAi("bot-gone", WriteCodeScope);
        var human = _f.Executors.Create(new Executor { Nick = "man-gone", Kind = ExecutorKind.Human }, null);
        var team = CreateTeam(ai, human);
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон",
            IsTemplate = true,
            TeamId = team.Id,
            ExecutorIds = [ai.Id],
            ResponsibleId = human.Id,
            SkillIds = [SkillId("code-write")],
        }, "", "", null);
        Deactivate(ai);
        Deactivate(human);

        // без варианта подбора создание не падает: неактивные просто снимаются
        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        Assert.Empty(copy.ExecutorIds);
        Assert.Null(copy.ResponsibleId);
    }

    [Fact]
    public void Active_Executor_Is_Kept_Not_Replaced()
    {
        var chosen = CreateAi("bot-chosen", WriteCodeScope);
        var other = CreateAi("bot-other", WriteCodeScope);
        var team = CreateTeam(chosen, other);
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон",
            IsTemplate = true,
            TeamId = team.Id,
            ExecutorIds = [chosen.Id],
            SkillIds = [SkillId("code-write")],
        }, "", "", null);

        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null, PickMode.AiFirst, _f.Picker);

        Assert.Equal([chosen.Id], copy.ExecutorIds); // указанный активный не трогается
    }

    // ---------- чат шаблона не копируется ----------

    [Fact]
    public void Template_Chat_Is_Not_Copied()
    {
        var head = _f.Tasks.Create(new TaskItem { Title = "Шаблон с чатом", IsTemplate = true },
            "", "", null);
        var author = _f.Executors.EnsureLocalOwner();
        _f.Chat.Add(head.Id, author.Id, null, "комментарий в шаблоне");

        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        Assert.Empty(_f.Chat.ListByTask(copy.Id));
        Assert.Single(_f.Chat.ListByTask(head.Id)); // исходный чат на месте
    }
}
