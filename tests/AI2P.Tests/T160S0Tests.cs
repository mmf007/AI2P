using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-160-S0 «Запуск агентов с другого сервера»: три доработки.
///
/// <list type="number">
/// <item>Действие агента <c>update_task</c> — правка полей ЧУЖОЙ задачи (исполнитель,
/// «могут заменить», ответственный, навыки, тэги). До этого проставить исполнителя задаче
/// внутри уже запущенной иерархии было нечем.</item>
/// <item>Автоподбор внутри РАБОТАЮЩЕЙ ОЧЕРЕДИ ИЕРАРХИИ ищет сначала СВОБОДНЫХ: назначенный
/// занятый исполнитель означал бы взаимное ожидание (задача, ждущая конца иерархии, и
/// подзадача этой же иерархии).</item>
/// <item>Повторный удалённый запуск — заявка подаётся ЗАНОВО (проверяется в T196Tests
/// и T263Tests: там живёт вся обвязка двух серверов).</item>
/// </list>
/// </summary>
public sealed class T160S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- обвязка ----------

    private Executor CreateAi(string nick, double score = 90)
    {
        var scopeRel = $"models/scope_t160_{nick}.json";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        _f.Files.WriteText(scopeRel,
            $$"""{ "skills": [ { "name": "code-write", "score": {{score.ToString(inv)}} } ] }""");
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
        }, null);
    }

    private Executor CreateHuman(string nick)
    {
        var human = _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);
        _f.Files.WriteText(human.CapabilitiesPath,
            """{ "skills": [ { "name": "code-write", "score": 50 } ] }""");
        return human;
    }

    private Team CreateTeam(params Executor[] members) =>
        _f.Teams.Create(new Team
        {
            Name = "Команда-" + Guid.NewGuid().ToString("N")[..6],
            Members = members.Select(e => new TeamMember { ExecutorId = e.Id }).ToList(),
        }, null);

    private TaskItem CreateTask(string title, string? teamId = null, string? parentId = null,
        Executor? executor = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            TeamId = teamId,
            ParentId = parentId,
            Status = TaskStatuses.Pending,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    private string SkillId(string name) => _f.RefData.Skills().First(s => s.Name == name).Id;

    /// <summary>Набор инструментов задания: актор — тот, от чьего имени идут действия.</summary>
    private AgentToolset Toolset(TaskItem current, string? actor = "exec-1") =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, actor),
            actionCodeByTool: _f.Actions.CodeByTool);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private Task<string> CallAsync(TaskItem current, string json, string? actor = "exec-1") =>
        Toolset(current, actor).ExecuteAsync("update_task", Args(json), CancellationToken.None);

    // ---------- 1. действие update_task ----------

    [Fact]
    public async Task Update_Task_Sets_Executor_Responsible_Skills_And_Tags()
    {
        var bot = CreateAi("бот");
        var spare = CreateAi("запасной");
        var mike = CreateHuman("майк");
        var team = CreateTeam(bot, spare, mike);
        var me = CreateTask("моя", team.Id);
        var other = CreateTask("чужая", team.Id);

        var answer = await CallAsync(me, $$"""
            {
              "code": "{{other.DisplayId}}",
              "executor": "бот",
              "altExecutors": ["запасной"],
              "responsible": "майк",
              "skills": ["code-write"],
              "tags": ["выпуск"]
            }
            """);

        var updated = _f.Tasks.Get(other.Id)!;
        Assert.Equal([bot.Id], updated.ExecutorIds);
        Assert.Equal([spare.Id], updated.AltExecutorIds);
        Assert.Equal(mike.Id, updated.ResponsibleId);
        Assert.Equal([SkillId("code-write")], updated.SkillIds);
        Assert.Equal(["выпуск"], updated.Tags);
        Assert.Contains(other.DisplayId, answer);
    }

    [Fact]
    public async Task Update_Task_Touches_Only_The_Named_Fields()
    {
        var bot = CreateAi("бот");
        var team = CreateTeam(bot);
        var me = CreateTask("моя", team.Id);
        var other = CreateTask("чужая", team.Id, executor: bot);

        // названы только тэги — исполнитель обязан остаться на месте
        await CallAsync(me, $$"""{ "code": "{{other.DisplayId}}", "tags": ["тема"] }""");

        var updated = _f.Tasks.Get(other.Id)!;
        Assert.Equal([bot.Id], updated.ExecutorIds);
        Assert.Equal(["тема"], updated.Tags);

        // а пустая строка исполнителя означает «снять»
        await CallAsync(me, $$"""{ "code": "{{other.DisplayId}}", "executor": "" }""");
        Assert.Empty(_f.Tasks.Get(other.Id)!.ExecutorIds);
        Assert.Equal(["тема"], _f.Tasks.Get(other.Id)!.Tags);
    }

    [Fact]
    public async Task Update_Task_Refuses_Own_Task_Unknown_Nick_And_Empty_Call()
    {
        var bot = CreateAi("бот");
        var team = CreateTeam(bot);
        var me = CreateTask("моя", team.Id);
        var other = CreateTask("чужая", team.Id);

        // свою задачу так не правят
        Assert.Contains("свою задачу", await CallAsync(me, $$"""
            { "code": "{{me.DisplayId}}", "executor": "бот" }
            """));
        // ника нет в организации — понятная ошибка со списком, а не тихое «ничего не сделал»
        var unknown = await CallAsync(me, $$"""
            { "code": "{{other.DisplayId}}", "executor": "неизвестный" }
            """);
        Assert.Contains("неизвестный", unknown);
        Assert.Empty(_f.Tasks.Get(other.Id)!.ExecutorIds);
        // ни одного поля — тоже отказ, а не «изменено»
        Assert.Contains("менять нечего", await CallAsync(me, $$"""
            { "code": "{{other.DisplayId}}" }
            """));
    }

    /// <summary>Инструмент публикуется агенту и закрывается правилом безопасности —
    /// значит у него есть запись справочника действий (иначе Authorize пропускает всё).</summary>
    [Fact]
    public void Update_Task_Is_Published_And_Has_A_Catalog_Record()
    {
        var task = CreateTask("задача");
        Assert.Contains(Toolset(task).Specs, s => s.Name == "update_task");
        // без исполнителя задания действие не публикуется: правку некому подписать
        Assert.DoesNotContain(Toolset(task, actor: null).Specs, s => s.Name == "update_task");
        Assert.Equal("AI2P.Tasks.Update", _f.Actions.CodeByTool("update_task"));
    }

    // ---------- 2. автоподбор: сначала свободные ----------

    [Fact]
    public void Pick_Prefers_A_Free_Executor_When_Asked()
    {
        var strong = CreateAi("сильный", 90);
        var weak = CreateAi("слабый", 70);
        var team = CreateTeam(strong, weak);
        var skills = new[] { SkillId("code-write") };

        // сильный занят настоящим заданием
        var busyTask = CreateTask("занимаем сильного", team.Id, executor: strong);
        _f.Jobs.Create(busyTask.Id, strong.Id, busyTask.DescriptionPath, null);
        Assert.True(_f.Jobs.HasActiveByExecutor(strong.Id));

        // обычный подбор о занятости не знает — берёт лучшего
        Assert.Equal(strong.Id, _f.Picker.Pick(null, team.Id, skills, PickMode.AiFirst).ExecutorId);
        // «сначала свободные» (очередь иерархии) — берёт свободного
        Assert.Equal(weak.Id,
            _f.Picker.Pick(null, team.Id, skills, PickMode.AiFirst, preferFree: true).ExecutorId);
    }

    [Fact]
    public void With_No_Free_Executor_The_Pick_Is_The_Same_As_Before()
    {
        var only = CreateAi("единственный");
        var team = CreateTeam(only);
        var skills = new[] { SkillId("code-write") };
        var busyTask = CreateTask("занимаем", team.Id, executor: only);
        _f.Jobs.Create(busyTask.Id, only.Id, busyTask.DescriptionPath, null);

        // свободных нет вовсе — отбор идёт по всем, задача без исполнителя не остаётся
        Assert.Equal(only.Id,
            _f.Picker.Pick(null, team.Id, skills, PickMode.AiFirst, preferFree: true).ExecutorId);
    }
}
