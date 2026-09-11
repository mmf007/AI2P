using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo28 (этап 3.1, ТЗ v1.26): числовой приоритет, цена↔качество проекта, автоподбор
/// исполнителя, инструмент create_task и оркестрация автосоздания подзадач.
/// </summary>
public sealed class Todo28Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и никогда не отвечает: фоновый вызов
    /// модели детерминированно висит, задание остаётся running (недостижимый IP для этого
    /// не годится — на части сетей соединение отбивается мгновенно и тест флакал).</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public Todo28Tests()
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

    private Executor CreateAi(string nick, string scopeJson, string? profileJson = null)
    {
        var scopeRel = $"models/scope_test_{nick}.json";
        _f.Files.WriteText(scopeRel, scopeJson);
        var profileRel = "";
        if (profileJson is not null)
        {
            profileRel = $"models/profile_test_{nick}.json";
            _f.Files.WriteText(profileRel, profileJson);
        }
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
            ProfilePath = profileRel,
        }, null);
    }

    private Executor CreateHuman(string nick, string scopeJson)
    {
        var human = _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);
        _f.Files.WriteText(human.CapabilitiesPath, scopeJson);
        return human;
    }

    private Team CreateTeam(params Executor[] members) =>
        _f.Teams.Create(new Team
        {
            Name = "Команда-" + Guid.NewGuid().ToString("N")[..6],
            Members = members.Select(e => new TeamMember { ExecutorId = e.Id }).ToList(),
        }, null);

    private static string Scope(double writeCode, double inCost, double outCost)
    {
        // числа — инвариантной культурой: ru-RU дал бы «0,5» и сломал JSON
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $$"""
        {
          "skills": [ { "name": "code-write", "score": {{writeCode.ToString(inv)}} } ],
          "cost": { "in_per_1m": {{inCost.ToString(inv)}}, "out_per_1m": {{outCost.ToString(inv)}} }
        }
        """;
    }

    /// <summary>Профайл, вызов которого висит (сервер _hang молчит): задание остаётся
    /// running, исполнитель «занят» (детерминированность тестов очереди).</summary>
    private string HangingProfile =>
        $$"""{"provider":"openai-compatible","model":"m","baseUrl":"http://127.0.0.1:{{((System.Net.IPEndPoint)_hang.LocalEndpoint).Port}}","secretRef":""}""";

    private string SkillId(string name) => _f.RefData.Skills().First(s => s.Name == name).Id;

    // ---------- числовой приоритет ----------

    [Fact]
    public void Priority_Level_And_Num_Mapping()
    {
        Assert.Equal(5, TaskPriority.NumFor(0));
        Assert.Equal(15, TaskPriority.NumFor(1));
        Assert.Equal(25, TaskPriority.NumFor(2));
        Assert.Equal(0, TaskPriority.LevelFor(10));
        Assert.Equal(1, TaskPriority.LevelFor(11));
        Assert.Equal(1, TaskPriority.LevelFor(20));
        Assert.Equal(2, TaskPriority.LevelFor(21));
    }

    [Fact]
    public void PriorityNum_Is_Stored_Filtered_And_Orders_Lists()
    {
        var low = _f.Tasks.Create(new TaskItem { Title = "низкий", Priority = 0, PriorityNum = 5 }, "т", "", null);
        var mid = _f.Tasks.Create(new TaskItem { Title = "обычный", Priority = 1, PriorityNum = 15 }, "т", "", null);
        var high = _f.Tasks.Create(new TaskItem { Title = "высокий", Priority = 2, PriorityNum = 25 }, "т", "", null);

        Assert.Equal(25, _f.Tasks.Get(high.Id)!.PriorityNum);

        var filtered = _f.Tasks.List(priorityMin: 11, priorityMax: 20);
        Assert.Single(filtered);
        Assert.Equal(mid.Id, filtered[0].Id);

        // список упорядочен по убыванию числового приоритета
        var all = _f.Tasks.List();
        Assert.Equal([high.Id, mid.Id, low.Id], all.Select(t => t.Id).ToArray());
    }

    // ---------- проект: цена ↔ качество ----------

    [Fact]
    public void Project_QualityBias_Stored_And_Read()
    {
        var project = _f.Projects.Create("Байас", null, null, null, isActive: true, qualityBias: 0.9);
        Assert.Equal(0.9, ProjectService.QualityBias(_f.Projects.Get(project.Id)));

        var plain = _f.Projects.Create("Без байаса", null, null, null);
        Assert.Equal(0.5, ProjectService.QualityBias(_f.Projects.Get(plain.Id)));
        Assert.Equal(0.5, ProjectService.QualityBias(null));
    }

    // ---------- автоподбор исполнителя ----------

    [Fact]
    public void Pick_Prefers_Quality_Or_Price_By_Project_Bias()
    {
        var strong = CreateAi("strong", Scope(90, 30, 30)); // качество 90, цена 60
        var cheap = CreateAi("cheap", Scope(70, 0.5, 0.5)); // качество 70, цена 1
        var team = CreateTeam(strong, cheap);
        var quality = _f.Projects.Create("Качество", null, null, null, true, 1.0);
        var price = _f.Projects.Create("Цена", null, null, null, true, 0.0);
        var skills = new[] { SkillId("code-write") };

        Assert.Equal(strong.Id, _f.Picker.Pick(quality.Id, team.Id, skills, PickMode.AiFirst).ExecutorId);
        Assert.Equal(cheap.Id, _f.Picker.Pick(price.Id, team.Id, skills, PickMode.AiFirst).ExecutorId);
    }

    [Fact]
    public void Pick_Respects_Kind_Order_And_Falls_Back()
    {
        var ai = CreateAi("bot", Scope(90, 10, 10));
        var human = CreateHuman("mike", """{ "skills": [ { "name": "image-concept", "score": 80 } ] }""");
        var team = CreateTeam(ai, human);

        var writeCode = new[] { SkillId("code-write") };
        var draw = new[] { SkillId("image-concept") };

        // сначала человек: подходит человек — берётся человек, хоть ИИ и есть
        Assert.Equal(ai.Id, _f.Picker.Pick(null, team.Id, writeCode, PickMode.AiFirst).ExecutorId);
        // code-write есть только у ИИ — «сначала человек» падает на ИИ
        Assert.Equal(ai.Id, _f.Picker.Pick(null, team.Id, writeCode, PickMode.HumanFirst).ExecutorId);
        // image-concept есть только у человека — «сначала ИИ» падает на человека…
        Assert.Equal(human.Id, _f.Picker.Pick(null, team.Id, draw, PickMode.AiFirst).ExecutorId);
        // …а «только ИИ» (выбор агента-разбивателя) — никого
        Assert.Null(_f.Picker.Pick(null, team.Id, draw, PickMode.AiOnly).ExecutorId);
        // без команды — понятная причина
        Assert.Null(_f.Picker.Pick(null, null, writeCode, PickMode.AiFirst).ExecutorId);
    }

    // ---------- инструмент create_task ----------

    [Fact]
    public async Task CreateTask_Tool_Creates_Subtask_With_Skills_Executor_And_Marks_Parent()
    {
        var ai = CreateAi("bot", Scope(90, 1, 2));
        var team = CreateTeam(ai);
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель", TeamId = team.Id }, "т", "", null);

        var toolset = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, parent,
            _f.RefData, _f.Picker, ai.Id);
        using var args = JsonDocument.Parse(
            """{"title":"Подзадача","description":"описание","skills":["code-write"],"priority":25}""");
        var result = await toolset.ExecuteAsync("create_task", args.RootElement, CancellationToken.None);

        Assert.Contains("Создана подзадача", result);
        var child = _f.Tasks.ListChildren(parent.Id).Single();
        Assert.Equal("Подзадача", child.Title);
        Assert.Equal(25, child.PriorityNum);
        Assert.Equal(2, child.Priority);
        Assert.Equal([SkillId("code-write")], child.SkillIds.ToArray());
        Assert.Equal([ai.Id], child.ExecutorIds.ToArray());
        Assert.Equal(TaskStatuses.Pending, child.Status);

        // родитель помечен разбитым: isNotSplit + флаг autoSplit для оркестратора
        var reloaded = _f.Tasks.Get(parent.Id)!;
        Assert.True(reloaded.IsNotSplit);
        Assert.True(TaskService.HasLaunchFlag(reloaded.LaunchJson, "autoSplit"));

        // повторное разбиение запрещено (новое задание видит isNotSplit)
        var again = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, reloaded,
            _f.RefData, _f.Picker, ai.Id);
        Assert.False(again.CanCreate);
        var error = await again.ExecuteAsync("create_task", args.RootElement, CancellationToken.None);
        Assert.Contains("уже разбита", error);
    }

    [Fact]
    public async Task CreateTask_Tool_Rejects_Unknown_Skill()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var toolset = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, parent,
            _f.RefData, _f.Picker, null);
        using var args = JsonDocument.Parse(
            """{"title":"X","description":"d","skills":["no-such-skill"]}""");
        var result = await toolset.ExecuteAsync("create_task", args.RootElement, CancellationToken.None);
        Assert.Contains("нет в справочнике", result);
        Assert.Empty(_f.Tasks.ListChildren(parent.Id));
    }

    // ---------- очередь запуска подзадач и финальный анализ ----------

    [Fact]
    public async Task Busy_Ai_Executor_Gets_One_Job_Higher_Priority_First()
    {
        var ai = CreateAi("bot", Scope(90, 1, 2), HangingProfile);
        var team = CreateTeam(ai);
        var parent = _f.Tasks.Create(new TaskItem
        {
            Title = "Родитель",
            TeamId = team.Id,
            ExecutorIds = [ai.Id],
            LaunchJson = """{"mode":"manual","autoSplit":true}""",
        }, "т", "", null);
        var low = _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "низкий", TeamId = team.Id, Status = TaskStatuses.Pending,
            PriorityNum = 5, ExecutorIds = [ai.Id],
        }, "т", "", null);
        var high = _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "высокий", TeamId = team.Id, Status = TaskStatuses.Pending,
            PriorityNum = 25, ExecutorIds = [ai.Id],
        }, "т", "", null);

        await _f.Orchestrator.ProcessSplitParentAsync(parent.Id, null);

        // запущена только приоритетная подзадача — исполнитель занят ею (одно задание за раз)
        Assert.NotEmpty(_f.Jobs.ListByTask(high.Id));
        Assert.Empty(_f.Jobs.ListByTask(low.Id));
        Assert.True(_f.Jobs.HasActiveByExecutor(ai.Id));

        // ручной запуск второй подзадачи тем же исполнителем тоже отбивается
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(low.Id, null));
        Assert.Contains("уже работает", ex.Message);
    }

    [Fact]
    public async Task Final_Analysis_Starts_Once_When_All_Children_Done()
    {
        var ai = CreateAi("bot", Scope(90, 1, 2), HangingProfile);
        var team = CreateTeam(ai);
        var parent = _f.Tasks.Create(new TaskItem
        {
            Title = "Родитель",
            TeamId = team.Id,
            ExecutorIds = [ai.Id],
            LaunchJson = """{"mode":"manual","autoSplit":true}""",
        }, "т", "", null);
        _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "готовая", TeamId = team.Id,
            Status = TaskStatuses.Done, ExecutorIds = [ai.Id],
        }, "т", "", null);

        await _f.Orchestrator.ProcessSplitParentAsync(parent.Id, null);

        // финальный анализ: задание исполнителю родителя, задача снова в работе
        Assert.Single(_f.Jobs.ListByTask(parent.Id));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(parent.Id)!.Status);
        Assert.True(TaskService.HasLaunchFlag(_f.Tasks.Get(parent.Id)!.LaunchJson, "finalStarted"));

        // повторная обработка не создаёт второе финальное задание
        await _f.Orchestrator.ProcessSplitParentAsync(parent.Id, null);
        Assert.Single(_f.Jobs.ListByTask(parent.Id));
    }

    [Fact]
    public async Task AutoStart_Denied_By_Security_Rule()
    {
        var ai = CreateAi("bot", Scope(90, 1, 2), HangingProfile);
        var team = CreateTeam(ai);
        var parent = _f.Tasks.Create(new TaskItem
        {
            Title = "Родитель", TeamId = team.Id,
            LaunchJson = """{"mode":"manual","autoSplit":true}""",
        }, "т", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "подзадача", TeamId = team.Id,
            Status = TaskStatuses.Pending, ExecutorIds = [ai.Id],
        }, "т", "", null);
        _f.Security.Create(new SecurityRule
        {
            Scope = SecurityScope.Global,
            Target = SecurityTarget.Action,
            Permission = SecurityPermission.Deny,
            Pattern = JobOrchestrator.AutoStartActionCode,
        }, null);

        await _f.Orchestrator.ProcessSplitParentAsync(parent.Id, null);

        // правило deny на AI2P.Tasks.AutoStart — подзадача не запущена автоматически
        Assert.Empty(_f.Jobs.ListByTask(child.Id));
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(child.Id)!.Status);
    }

    [Fact]
    public void MarkSplit_And_FinalStarted_Flags()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        _f.Tasks.MarkSplit(task.Id);
        _f.Tasks.MarkSplit(task.Id); // идемпотентно
        var loaded = _f.Tasks.Get(task.Id)!;
        Assert.True(loaded.IsNotSplit);
        Assert.True(TaskService.HasLaunchFlag(loaded.LaunchJson, "autoSplit"));
        Assert.Equal("manual", ReadMode(loaded.LaunchJson)); // режим запуска не потерян

        Assert.True(_f.Tasks.TryMarkFinalStarted(task.Id));
        Assert.False(_f.Tasks.TryMarkFinalStarted(task.Id));
    }

    private static string ReadMode(string launchJson)
    {
        using var doc = JsonDocument.Parse(launchJson);
        return doc.RootElement.GetProperty("mode").GetString()!;
    }

    [Fact]
    public void Migration_Backfills_PriorityNum_From_Level_On_Existing_Db()
    {
        // имитация БД v7: убираем новые колонки и заводим задачи со старыми уровнями
        var task = _f.Tasks.Create(new TaskItem { Title = "старая", Priority = 2 }, "т", "", null);
        using (var conn = _f.Db.Open())
        {
            // у базы v7 журнала изменений ещё не было (он появился на этапе 43), а его
            // триггеры перечисляют все колонки — SQLite не даст удалить колонку под ними
            AI2P.Storage.ChangeLog.DropTriggers(conn);
            AI2P.Storage.Sql.Exec(conn, null, "ALTER TABLE tasks DROP COLUMN priority_num");
            AI2P.Storage.Sql.Exec(conn, null, "ALTER TABLE tasks DROP COLUMN is_not_split");
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        _f.Db.Init(); // повторная инициализация = миграция v7 → v8

        var migrated = _f.Tasks.Get(task.Id)!;
        Assert.Equal(25, migrated.PriorityNum); // высокий уровень → 25
        Assert.False(migrated.IsNotSplit);
    }
}
