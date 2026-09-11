using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-2-S0 «Состояние задачи». Жалоба заказчика по выпуску 1.100 (T-286): выпущенная задача
/// висела в представлении «в работе у ИИ» в состоянии «пауза», а в её карточке стояла
/// «проверка» — два места показывали разное. Причина: у корня осталась открытой очередь
/// иерархического запуска (T-159), потому что один потомок (T-241) так и не был выполнен и
/// никто его не брал. Очередь при этом вела к запуску самого корня, а он уже был сдан.
///
/// Правка тройная:
/// 1) очередь закрывается, как только КОРЕНЬ сдан (проверка, готово, отмена);
/// 2) сданная задача не показывается в «в работе у ИИ» и не носит пометку в карточке, даже
///    пока флаг ещё стоит (на чужом сервере проход очереди у нас и не состоится);
/// 3) вторая часть жалобы — решение человека «перенести невыполненную подзадачу в следующий
///    выпуск» выполнить было НЕЧЕМ: у агента нет инструмента смены родителя. Появилось
///    действие move_task (у CLI-агента — маркер AI2P_MOVE_TASK).
/// </summary>
public sealed class T2S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string MoveMarker = ClaudeCliConnector.MoveTaskMarker;

    /// <summary>ИИ-исполнитель с областью применимости — нужен, чтобы задача считалась работой
    /// агента (представление «в работе у ИИ» — список работы АГЕНТОВ).</summary>
    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """
            {
              "skills": [ { "name": "code-write", "score": 90 } ],
              "cost": { "in_per_1m": 1, "out_per_1m": 2 }
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel,
        }, null);
    }

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null,
        string status = TaskStatuses.Pending, string? projectId = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            ProjectId = projectId,
            Status = status,
            ExecutorIds = executor is null ? [] : [executor.Id],
        }, "текст", "", null);

    private AI2P.Core.Api.AiWorkItemDto? WorkItem(TaskItem task) =>
        _f.Tasks.AiWork().FirstOrDefault(i => i.Task.Id == task.Id);

    private static bool RunOpen(TaskItem task) =>
        TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyFlag);

    private AgentToolset Toolset(TaskItem current, List<SecurityRule>? rules = null) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, null),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: name => name == "move_task" ? "AI2P.Tasks.Move" : null);

    // ---------- 1. «работа сдана» — одно правило на всех ----------

    [Theory]
    [InlineData(TaskStatuses.Review, true)]
    [InlineData(TaskStatuses.Done, true)]
    [InlineData(TaskStatuses.Cancelled, true)]
    [InlineData(TaskStatuses.Draft, false)]
    [InlineData(TaskStatuses.Pending, false)]
    [InlineData(TaskStatuses.InProgress, false)]
    [InlineData(TaskStatuses.Paused, false)]
    [InlineData(TaskStatuses.Error, false)]
    [InlineData(TaskStatuses.NeedsFix, false)]
    public void Settled_Means_Review_Done_Or_Cancelled(string status, bool settled)
    {
        Assert.Equal(settled, TaskStatuses.Settled(status));
        // «завершена» — по-прежнему только два исхода: «проверка» ждёт человека, и для
        // блокирующих задач (T-6-S1) это НЕ то же самое, что «готово»
        Assert.Equal(status is TaskStatuses.Done or TaskStatuses.Cancelled,
            TaskStatuses.Finished(status));
    }

    // ---------- 2. очередь закрывается, когда корень сдан ----------

    [Fact]
    public async Task An_Unfinished_Subtask_No_Longer_Keeps_The_Queue_Of_A_Released_Root_Open()
    {
        // ровно случай 1.100: корень выпущен, а один потомок так и остался «ожидает»
        // и никем не берётся — исполнителя у него нет
        var root = CreateTask("выпустить версию", executor: CreateAi("выпускающий"));
        var orphan = CreateTask("невыполненный потомок", root.Id);
        _f.Tasks.SetHierarchyRun(root.Id, true);
        _f.Tasks.ChangeStatus(root.Id, TaskStatuses.Review, null);

        var run = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.False(RunOpen(_f.Tasks.Get(root.Id)!));
        Assert.Empty(run.Started);
        // потомка проход не тронул: его судьбу решает человек (перенести, отменить, доделать)
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(orphan.Id)!.Status);
    }

    [Fact]
    public async Task A_Running_Root_Keeps_Its_Queue()
    {
        // корень ещё не сдан — очередь обязана работать по-прежнему: она и существует ради
        // того, чтобы довести поддерево до его запуска. Незавершённая подзадача, которую
        // очередь ЖДЁТ (у неё блокирующая в «проверке» — для блокирующих это не «завершена»,
        // T-6-S1), держит очередь открытой, как и до T-2-S0
        var root = CreateTask("корень", executor: CreateAi("корневой"));
        var blocker = CreateTask("блокирующая", status: TaskStatuses.Review);
        _f.Tasks.Create(new TaskItem
        {
            Title = "подзадача",
            ParentId = root.Id,
            Status = TaskStatuses.Pending,
            ExecutorIds = [CreateAi("детский").Id],
            BlockerIds = [blocker.Id],
        }, "текст", "", null);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.Equal(0, run.Started.Count);
        Assert.True(run.Waiting > 0);
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task A_Cancelled_Root_Closes_Its_Queue_Too()
    {
        var root = CreateTask("корень", executor: CreateAi("корневой"));
        CreateTask("подзадача", root.Id, CreateAi("детский"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));

        _f.Tasks.ChangeStatus(root.Id, TaskStatuses.Cancelled, null);
        await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.False(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    // ---------- 3. сданной задачи нет в «в работе у ИИ» ----------

    [Fact]
    public void A_Released_Task_With_A_Stale_Flag_Is_Not_Ai_Work()
    {
        var root = CreateTask("выпустить версию", executor: CreateAi("выпускающий"));
        _f.Tasks.SetHierarchyRun(root.Id, true);
        Assert.NotNull(WorkItem(root)); // пока «ожидает» — законно стоит в очереди

        _f.Tasks.ChangeStatus(root.Id, TaskStatuses.Review, null);

        // это и есть жалоба: «в списке пауза, а в задаче проверка». Флаг может ещё стоять
        // (проход очереди — у сервера-владельца), но работы агента по задаче уже нет
        Assert.Null(WorkItem(root));
    }

    [Fact]
    public void A_Released_Task_With_A_Deferred_Start_Is_Not_Ai_Work_Either()
    {
        var task = CreateTask("задача", executor: CreateAi("агент"));
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddHours(2));
        Assert.NotNull(WorkItem(task));

        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Review, null);

        // отложенный старт сданную задачу не поднимет (ListStartAfterDue берёт только
        // черновик / ожидает / паузу) — значит и работой агента она не является
        Assert.Null(WorkItem(task));
    }

    [Fact]
    public void A_Live_Job_Keeps_The_Task_In_The_List_Whatever_Its_Status()
    {
        var agent = CreateAi("работник");
        var task = CreateTask("задача", executor: agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.Running, agent.Id);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Review, null);

        // исключение из правила: агент занят задачей прямо сейчас — что бы ни стояло
        // в статусе, показать его надо (иначе работающий агент пропал бы с экрана)
        Assert.NotNull(WorkItem(task));
    }

    [Fact]
    public void The_List_Shows_The_Task_Status_And_The_Agent_Work_Separately()
    {
        var view = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "AiWorkView.razor"));

        // две колонки вместо одной: состояние ЗАДАЧИ (как во всех списках) и состояние
        // РАБОТЫ агента — иначе слово «пауза» в списке читается как статус задачи
        Assert.Contains("data-aiwork-status=\"@item.Task.Status\"", view);
        Assert.Contains("data-aiwork-work=\"@WorkCode(item)\"", view);
        Assert.Contains("(\"work\", \"aiwork.work\")", view);
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty("aiwork.work", out var value),
                $"{lang}: нет ключа aiwork.work");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
        }
    }

    // ---------- 4. перенос задачи по иерархии инструментом агента ----------

    [Fact]
    public async Task Move_Task_Re_Parents_A_Task_Of_The_Same_Project()
    {
        var project = _f.Projects.Create("проект", null, null, null);
        var release = CreateTask("выпуск 1.100", projectId: project.Id);
        var next = CreateTask("выпуск 1.101", projectId: project.Id);
        var orphan = CreateTask("невыполненный потомок", release.Id, projectId: project.Id);
        var current = CreateTask("текущая", projectId: project.Id);
        var nextCode = _f.Tasks.Get(next.Id)!.DisplayId;

        var answer = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""{"code": "{{_f.Tasks.Get(orphan.Id)!.DisplayId}}", "parent": "{{nextCode}}"}"""),
            CancellationToken.None);

        Assert.Equal(next.Id, _f.Tasks.Get(orphan.Id)!.ParentId);
        Assert.Contains(nextCode, answer);
    }

    [Fact]
    public async Task Move_Task_Without_A_Parent_Makes_The_Task_A_Root_One()
    {
        // «следующего выпуска ещё нет» — тот самый исход, ради которого человек и предлагал
        // вариант «вынести в корень»
        var release = CreateTask("выпуск 1.100");
        var orphan = CreateTask("невыполненный потомок", release.Id);
        var current = CreateTask("текущая");

        var answer = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""{"code": "{{_f.Tasks.Get(orphan.Id)!.DisplayId}}"}"""), CancellationToken.None);

        Assert.Null(_f.Tasks.Get(orphan.Id)!.ParentId);
        Assert.Contains(_f.Tasks.Get(orphan.Id)!.DisplayId, answer);
    }

    [Fact]
    public async Task Move_Task_Refuses_To_Move_The_Agents_Own_Task()
    {
        var root = CreateTask("корень");
        var current = CreateTask("текущая", root.Id);

        var answer = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""{"code": "{{_f.Tasks.Get(current.Id)!.DisplayId}}"}"""), CancellationToken.None);

        // очередь иерархии ведётся по корню — агент не двигает условия своего же запуска
        Assert.Equal(root.Id, _f.Tasks.Get(current.Id)!.ParentId);
        Assert.Contains("свою задачу", answer);
    }

    [Fact]
    public async Task Move_Task_Reports_An_Unknown_Code_Instead_Of_Guessing()
    {
        var current = CreateTask("текущая");

        var missingTarget = await Toolset(current).ExecuteAsync("move_task",
            Args("""{"code": "T-99999"}"""), CancellationToken.None);
        var task = CreateTask("переносимая");
        var missingParent = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""{"code": "{{_f.Tasks.Get(task.Id)!.DisplayId}}", "parent": "T-99999"}"""),
            CancellationToken.None);

        Assert.Contains("T-99999", missingTarget);
        Assert.Contains("T-99999", missingParent);
        Assert.Null(_f.Tasks.Get(task.Id)!.ParentId); // ничего не переносилось
    }

    [Fact]
    public async Task Move_Task_Does_Not_Reach_Into_Another_Project()
    {
        var mine = _f.Projects.Create("мой", null, null, null);
        var alien = _f.Projects.Create("чужой", null, null, null);
        var current = CreateTask("текущая", projectId: mine.Id);
        var alienTask = CreateTask("чужая задача", projectId: alien.Id);

        var answer = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""{"code": "{{_f.Tasks.Get(alienTask.Id)!.DisplayId}}"}"""), CancellationToken.None);

        // задачи чужого проекта агенту не видны вовсе — как и у остальных инструментов заданий
        Assert.Contains(_f.Tasks.Get(alienTask.Id)!.DisplayId, answer);
        Assert.Equal(alien.Id, _f.Tasks.Get(alienTask.Id)!.ProjectId);
    }

    [Fact]
    public async Task A_Descendant_Cannot_Become_The_Parent()
    {
        var current = CreateTask("текущая");
        var head = CreateTask("голова");
        var tail = CreateTask("хвост", head.Id);

        var answer = await Toolset(current).ExecuteAsync("move_task",
            Args($$"""
                {"code": "{{_f.Tasks.Get(head.Id)!.DisplayId}}",
                 "parent": "{{_f.Tasks.Get(tail.Id)!.DisplayId}}"}
                """), CancellationToken.None);

        // запрет хранилища (ChangeParent): иначе ветка отвалилась бы от дерева
        Assert.Null(_f.Tasks.Get(head.Id)!.ParentId);
        Assert.Contains("move_task", answer);
    }

    // ---------- 5. маркер CLI-агента ----------

    [Fact]
    public async Task The_Cli_Agent_Moves_A_Task_By_A_Marker()
    {
        var release = CreateTask("выпуск 1.100");
        var next = CreateTask("выпуск 1.101");
        var orphan = CreateTask("невыполненный потомок", release.Id);
        var current = CreateTask("текущая");
        var text = "Версия выпущена.\n\n"
                   + MoveMarker + $" {{\"code\": \"{_f.Tasks.Get(orphan.Id)!.DisplayId}\", "
                   + $"\"parent\": \"{_f.Tasks.Get(next.Id)!.DisplayId}\"}}\n\nОтчёт готов.";

        var markers = ClaudeCliConnector.ParseMarkers(MoveMarker, text, out var cleaned);
        var summary = await ClaudeCliConnector.ApplyMarkersAsync(markers, Toolset(current),
            "## Перенос задач", _ => "move_task", CancellationToken.None);

        Assert.Single(markers);
        Assert.DoesNotContain(MoveMarker, cleaned); // маркер вырезан из текста ответа
        Assert.Contains("Версия выпущена.", cleaned);
        Assert.Equal(next.Id, _f.Tasks.Get(orphan.Id)!.ParentId);
        Assert.Contains(_f.Tasks.Get(next.Id)!.DisplayId, summary);
    }

    [Fact]
    public async Task A_Security_Rule_Closes_Moving_Tasks()
    {
        var release = CreateTask("выпуск 1.100");
        var orphan = CreateTask("невыполненный потомок", release.Id);
        var current = CreateTask("текущая");
        var rules = new List<SecurityRule>
        {
            new()
            {
                Scope = SecurityScope.Global, Target = SecurityTarget.Action,
                Pattern = "AI2P.Tasks.Move", Permission = SecurityPermission.Deny,
            },
        };
        var tools = Toolset(current, rules);

        var summary = await ClaudeCliConnector.ApplyMarkersAsync(
            [$"{{\"code\": \"{_f.Tasks.Get(orphan.Id)!.DisplayId}\"}}"], tools,
            "## Перенос задач", _ => "move_task", CancellationToken.None);

        Assert.Equal(release.Id, _f.Tasks.Get(orphan.Id)!.ParentId); // перенос не состоялся
        // про маркер такому агенту не рассказывают вовсе (как у подзадач и опыта)
        Assert.Equal("", tools.CliMoveNote(MoveMarker));
        Assert.NotEqual("", Toolset(current).CliMoveNote(MoveMarker));
        Assert.Contains("AI2P.Tasks.Move", summary);
    }

    [Fact]
    public void The_Action_Is_In_The_Catalog_In_Both_Languages()
    {
        // без записи справочника правила безопасности инструмент не закрывают вовсе
        // (ActionCatalogService.CodeByTool вернул бы null, а Authorize — «разрешено»)
        foreach (var lang in new[] { "ru", "en" })
        {
            var action = _f.Actions.List(lang).Single(a => a.Code == "AI2P.Tasks.Move");
            Assert.Equal("move_task", action.ToolName);
            Assert.False(string.IsNullOrWhiteSpace(action.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(action.Hint));
        }
        Assert.Equal("AI2P.Tasks.Move", _f.Actions.CodeByTool("move_task"));
    }

    private static System.Text.Json.JsonElement Args(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

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
}
