using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-125: создание подзадач CLI-агентом. Инструменты AI2P (create_task) CLI-транспорту
/// не публикуются, а /api закрыто входом по cookie — разбить задачу на подзадачи такой
/// агент не мог вовсе. Теперь подзадача создаётся МАРКЕРОМ в ответе
/// (`AI2P_SUBTASK: {…}`, аналог маркера вопроса AI2P_QUESTION): система вырезает маркеры
/// из текста, выполняет их как обычный create_task (с правилами безопасности действия
/// AI2P.Tasks.Create) и дописывает в результат сводку созданных подзадач.
/// </summary>
public sealed class CliSubtaskTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string Marker = ClaudeCliConnector.SubtaskMarker;

    /// <summary>ИИ-исполнитель с областью применимости (для автоподбора исполнителя подзадачи).</summary>
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

    private AgentToolset Toolset(TaskItem current, List<SecurityRule>? rules = null) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, null),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: name => name == "create_task" ? "AI2P.Tasks.Create" : null);

    // ---------- разбор маркеров ----------

    [Fact]
    public void Markers_Are_Parsed_And_Cut_Out_Of_Answer()
    {
        var text = "Разбил работу на две части.\n\n"
                   + Marker + " {\"title\": \"Первая\", \"description\": \"делаем раз\"}\n"
                   + "  " + Marker + " {\"title\": \"Вторая\", \"description\": \"делаем два\"}\n"
                   + "\nГотово.";

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned);

        Assert.Equal(2, markers.Count);
        Assert.Contains("Первая", markers[0]);
        Assert.Contains("Вторая", markers[1]);
        Assert.DoesNotContain(Marker, cleaned);
        Assert.Contains("Разбил работу на две части.", cleaned);
        Assert.EndsWith("Готово.", cleaned);
    }

    [Fact]
    public void Marker_Json_May_Span_Several_Lines_And_Hold_Cyrillic()
    {
        // JSON в несколько строк с кириллицей: смещение считается в символах, а не в байтах —
        // иначе хвост маркера остался бы в тексте ответа
        var text = Marker + """
             {
               "title": "Проверка кодировки",
               "description": "описание с кириллицей — тире, «кавычки»",
               "priority": 25
             }
            """ + "\nхвост ответа";

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned);

        Assert.Single(markers);
        Assert.Contains("«кавычки»", markers[0]);
        Assert.Equal("хвост ответа", cleaned);
    }

    [Fact]
    public void Marker_Inside_Line_Is_Not_A_Subtask()
    {
        // отчёт О САМОЙ возможности не должен создавать подзадачи: маркер засчитывается
        // только с начала строки
        var text = "Подзадача создаётся строкой " + Marker + " {\"title\": \"пример\", "
                   + "\"description\": \"из документации\"} — так это работает.";

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned);

        Assert.Empty(markers);
        Assert.Equal(text, cleaned);
    }

    [Fact]
    public void Marker_Without_Valid_Json_Stays_In_Text()
    {
        var text = Marker + " создать подзадачу про отчёт";

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned);

        Assert.Empty(markers);
        Assert.Equal(text, cleaned);
    }

    [Fact]
    public void Answer_Without_Markers_Is_Untouched()
    {
        const string text = "Обычный ответ агента.\n\n## Итог\nсделано.";

        Assert.Empty(ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned));
        Assert.Equal(text, cleaned);
    }

    // ---------- выполнение маркеров ----------

    [Fact]
    public async Task Markers_Create_Subtasks_And_Mark_Parent_Split()
    {
        var ai = CreateAi("бот");
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда", Members = [new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель", TeamId = team.Id }, "т", "", null);
        var text = "Готово.\n"
                   + Marker + " {\"title\": \"Подзадача 1\", \"description\": \"делаем раз\", "
                   + "\"skills\": [\"code-write\"], \"priority\": 25, \"acceptance\": \"работает\"}\n"
                   + Marker + " {\"title\": \"Подзадача 2\", \"description\": \"делаем два\"}\n";
        var tools = Toolset(parent);

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(text, out var cleaned);
        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        var children = _f.Tasks.ListChildren(parent.Id).OrderBy(c => c.Title).ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal(25, children[0].PriorityNum);
        Assert.Equal([ai.Id], children[0].ExecutorIds.ToArray());   // исполнитель подобран
        Assert.Equal(TaskStatuses.Pending, children[0].Status);
        Assert.Equal("делаем раз", _f.Tasks.ReadDescription(children[0]));
        Assert.Equal("работает", _f.Tasks.ReadAcceptance(children[0]));

        // родитель помечен разбитым: по флагу autoSplit оркестратор ведёт очередь запуска
        var reloaded = _f.Tasks.Get(parent.Id)!;
        Assert.True(reloaded.IsNotSplit);
        Assert.True(TaskService.HasLaunchFlag(reloaded.LaunchJson, "autoSplit"));

        // сводка уходит человеку в результат задания, вызовы — в журнал работ
        Assert.Contains("Подзадачи", summary);
        Assert.Contains(children[0].DisplayId, summary);
        Assert.Contains(children[1].DisplayId, summary);
        Assert.Equal(2, tools.CallLog.Count);
        Assert.Equal("Готово.", cleaned);
    }

    [Fact]
    public async Task Second_Job_Cannot_Split_Again()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        _f.Tasks.MarkSplit(parent.Id);
        var tools = Toolset(_f.Tasks.Get(parent.Id)!);

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"Ещё одна\", \"description\": \"д\"}", out _);
        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        Assert.Empty(_f.Tasks.ListChildren(parent.Id));
        Assert.Contains("уже разбита", summary);
    }

    [Fact]
    public async Task Unknown_Skill_Is_Reported_Without_Breaking_Job()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var tools = Toolset(parent);

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"X\", \"description\": \"д\", \"skills\": [\"нет-такого\"]}", out _);
        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        Assert.Empty(_f.Tasks.ListChildren(parent.Id));
        Assert.Contains("нет в справочнике", summary);
    }

    // ---------- правила безопасности ----------

    private static List<SecurityRule> DenyCreate =>
    [
        new()
        {
            Target = SecurityTarget.Action,
            Permission = SecurityPermission.Deny,
            Pattern = "AI2P.Tasks.Create",
        },
    ];

    [Fact]
    public async Task Denied_Action_Rule_Stops_Subtask_Creation()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var tools = Toolset(parent, DenyCreate);

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"Запрещённая\", \"description\": \"д\"}", out _);
        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        Assert.Empty(_f.Tasks.ListChildren(parent.Id));
        Assert.False(_f.Tasks.Get(parent.Id)!.IsNotSplit);
        Assert.Contains("Запрещено правилами безопасности", summary);
        Assert.Single(tools.SecurityTriggers);
    }

    // ---------- сквозной прогон задания через коннектор ----------

    /// <summary>
    /// Поддельный CLI: собственный процесс вместо настоящего Claude Code (внешние процессы
    /// в тестах — источник флаки, свой скрипт детерминирован). Вычитывает stdin (промпт),
    /// печатает готовый JSON headless-запуска и завершается.
    /// </summary>
    private string FakeCli(string cliJson)
    {
        var dir = Path.Combine(_f.Dir, "fake-cli");
        Directory.CreateDirectory(dir);
        // JSON — отдельным файлом в UTF-8: вывод «как есть» не зависит от кодовой страницы
        var outPath = Path.Combine(dir, "out.json");
        File.WriteAllText(outPath, cliJson, new System.Text.UTF8Encoding(false));
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd, "@echo off\r\nfindstr \"^\" > nul\r\ntype \"%~dp0out.json\"\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh, "#!/bin/sh\ncat > /dev/null\ncat \"$(dirname \"$0\")/out.json\"\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Fact]
    public async Task Whole_Cli_Job_Creates_Subtask_And_Cleans_Result()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("клод-cli");
        var answer = "Работа разбита.\n"
                     + Marker + " {\"title\": \"Первая часть\", \"description\": \"делаем раз\"}\n"
                     + "Всё.";
        var cliJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = "s-1",
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });
        const string profileRel = "models/profile_cli.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{FakeCli(cliJson)}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Родитель", ExecutorIds = [ai.Id],
        }, "разбей работу", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "разбей работу");
        var done = await WaitForJob(job.Id);

        // работа ушла ВНИЗ, на подзадачи (T-185): задание не закрывается, а ждёт их завершения
        // той же сессией агента, задача — не «проверка», а «пауза». Раньше здесь было
        // Done + «проверка», хотя проверять было нечего: подзадачи ещё не начинались
        Assert.Equal(JobState.WaitingHuman, done.State);
        Assert.Equal(JobWaitKinds.Subtasks, done.WaitKind);
        Assert.Equal(TaskStatuses.Paused, await WaitForTaskStatus(task.Id, TaskStatuses.Paused));
        var child = Assert.Single(_f.Tasks.ListChildren(task.Id));
        Assert.Equal("Первая часть", child.Title);
        // маркер вырезан из результата, вместо него — сводка для человека
        var result = _f.Files.ReadText(done.ResultPath!);
        Assert.DoesNotContain(Marker, result);
        Assert.Contains("Работа разбита.", result);
        Assert.Contains("## Подзадачи", result);
        Assert.Contains(child.DisplayId, result);
    }

    /// <summary>Дождаться завершения фонового задания (ТЗ п. 3, принцип 4).</summary>
    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 300; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Задание {jobId} не завершилось за 30 с");
    }

    /// <summary>
    /// Дождаться ожидаемого статуса ЗАДАЧИ. Состояние задания коннектор ставит на строку
    /// раньше статуса задачи (<c>AiConnectorBase</c>: сначала <c>SetState</c>, потом
    /// <c>ChangeStatus</c>), поэтому дождавшийся задания тест успевает прочитать ещё прежний
    /// статус — под нагрузкой полного прогона это давало красноту на ровном месте (T-203).
    /// </summary>
    private async Task<string> WaitForTaskStatus(string taskId, string expected)
    {
        for (var i = 0; i < 100; i++)
        {
            var status = _f.Tasks.Get(taskId)!.Status;
            if (status == expected)
            {
                return status;
            }
            await Task.Delay(50);
        }
        return _f.Tasks.Get(taskId)!.Status;
    }

    // ---------- промпт агента ----------

    [Fact]
    public void Prompt_Note_Explains_Marker_And_Skills()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);

        var note = Toolset(parent).CliSplitNote(Marker);

        Assert.Contains(Marker, note);
        Assert.Contains("create_task", note);              // объяснено, что инструмента нет
        Assert.Contains("code-write", note);               // список навыков справочника
        Assert.Contains("приоритет", note);
    }

    [Fact]
    public void Split_Request_Tells_Cli_Agent_About_Marker()
    {
        // кнопка «разбить на подзадачи» звала create_task даже у CLI-агента, у которого
        // инструмента нет: разбивающему по CLI сказано про маркер
        var ai = CreateAi("клод-cli");
        const string profileRel = "models/profile_cli_split.json";
        _f.Files.WriteText(profileRel,
            """{ "provider": "anthropic", "transport": "cli", "model": "claude-fable-5", "secretRef": "" }""");
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Родитель", ExecutorIds = [ai.Id] }, "т", "", null);

        var prompt = _f.Orchestrator.BuildSplitRequest(task, ai);

        Assert.Contains(Marker, prompt);
        Assert.DoesNotContain("инструментом create_task", prompt);
    }

    [Fact]
    public void Split_Request_Still_Names_The_Tool_For_Api_Agent()
    {
        var ai = CreateAi("бот-api");
        const string profileRel = "models/profile_api_split.json";
        _f.Files.WriteText(profileRel,
            """{ "provider": "anthropic", "model": "claude-fable-5", "secretRef": "anthropic" }""");
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Родитель", ExecutorIds = [ai.Id] }, "т", "", null);

        Assert.Contains("инструментом create_task", _f.Orchestrator.BuildSplitRequest(task, ai));
    }

    [Fact]
    public void Prompt_Note_Is_Empty_For_Already_Split_Task()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        _f.Tasks.MarkSplit(parent.Id);

        Assert.Equal("", Toolset(_f.Tasks.Get(parent.Id)!).CliSplitNote(Marker));
    }

    [Fact]
    public void Prompt_Note_Is_Empty_When_Action_Denied()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);

        Assert.Equal("", Toolset(parent, DenyCreate).CliSplitNote(Marker));
    }
}
