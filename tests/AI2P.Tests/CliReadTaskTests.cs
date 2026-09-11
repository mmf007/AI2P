using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-127: чтение чужих заданий CLI-агентом. Инструменты AI2P (get_task_by_code и соседние)
/// CLI-транспорту не публикуются, а /api закрыто входом по cookie — прочитать задачу, на
/// которую человек дал ссылку в формулировке, такой агент не мог вовсе (в промпте был только
/// блок родительского задания, T-120). Теперь задание запрашивается МАРКЕРОМ в ответе
/// (`AI2P_GET_TASK: {"code": "T-15"}`, можно url или title): система выполняет запрос как
/// обычный вызов инструмента (с правилами безопасности) и присылает агенту карточку задания
/// ВМЕСТЕ С ЧАТОМ следующим сообщением той же сессии CLI (`--resume`), после чего он
/// продолжает работу.
/// </summary>
public sealed class CliReadTaskTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string Marker = ClaudeCliConnector.TaskMarker;

    /// <summary>Набор инструментов текущей задачи; коды действий — из реального справочника.</summary>
    private AgentToolset Toolset(TaskItem current, List<SecurityRule>? rules = null) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, null),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: _f.Actions.CodeByTool);

    /// <summary>Задача-источник: описание, критерии приёмки, результат-артефакт и чат.</summary>
    private TaskItem SourceTask(Project project)
    {
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Источник" },
            "формулировка источника", "критерии источника", null);
        var rel = Path.Combine(_f.Files.TaskDirRel(project.Slug, task.DisplayId),
            "artifacts", "result.md").Replace('\\', '/');
        _f.Files.WriteText(rel, "РЕЗУЛЬТАТ выполнения источника");
        var human = _f.Executors.Create(new Executor { Nick = "человек", Kind = ExecutorKind.Human }, null);
        _f.Chat.Add(task.Id, human.Id, null, "реплика чата источника");
        return task;
    }

    // ---------- разбор маркеров ----------

    [Fact]
    public void Read_Markers_Are_Parsed_And_Cut_Out_Of_Answer()
    {
        var text = "Нужны два задания.\n"
                   + Marker + " {\"code\": \"T-15\"}\n"
                   + Marker + " {\"url\": \"http://localhost:5480/ai2p/mmfgrp/task/abc\"}\n"
                   + "Жду ответа.";

        var markers = ClaudeCliConnector.ParseMarkers(Marker, text, out var cleaned);

        Assert.Equal(2, markers.Count);
        Assert.Contains("T-15", markers[0]);
        Assert.Contains("/task/abc", markers[1]);
        Assert.DoesNotContain(Marker, cleaned);
        Assert.EndsWith("Жду ответа.", cleaned);
    }

    [Fact]
    public void Read_Marker_Inside_Line_Is_Not_A_Request()
    {
        // рассказ О САМОЙ возможности не должен уходить в запрос: маркер засчитывается
        // только с начала строки
        var text = "Задание читается строкой " + Marker + " {\"code\": \"T-1\"} — вот так.";

        Assert.Empty(ClaudeCliConnector.ParseMarkers(Marker, text, out var cleaned));
        Assert.Equal(text, cleaned);
    }

    [Fact]
    public void Subtask_Markers_Are_Not_Read_Requests()
    {
        // два маркера в одном ответе не путаются между собой
        var text = ClaudeCliConnector.SubtaskMarker + " {\"title\": \"X\", \"description\": \"д\"}\n"
                   + Marker + " {\"code\": \"T-7\"}";

        Assert.Single(ClaudeCliConnector.ParseMarkers(Marker, text, out _));
        Assert.Single(ClaudeCliConnector.ParseSubtaskMarkers(text, out _));
    }

    // ---------- выполнение маркеров ----------

    [Fact]
    public async Task Marker_By_Code_Returns_Description_Result_And_Chat()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);
        var tools = Toolset(current);

        var markers = ClaudeCliConnector.ParseMarkers(Marker,
            Marker + " {\"code\": \"" + source.DisplayId + "\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, tools, CancellationToken.None);

        Assert.Contains(source.DisplayId, answer);
        Assert.Contains("формулировка источника", answer);
        Assert.Contains("критерии источника", answer);
        Assert.Contains("РЕЗУЛЬТАТ выполнения источника", answer);   // артефакт задания
        Assert.Contains("реплика чата источника", answer);           // и чат целиком
        Assert.Contains("Продолжи выполнение задания", answer);
        // оба вызова — в журнал работ задания
        Assert.Equal(2, tools.CallLog.Count);
    }

    [Fact]
    public async Task Marker_By_Url_Returns_Task_With_Chat()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);

        var markers = ClaudeCliConnector.ParseMarkers(Marker,
            Marker + " {\"url\": \"http://localhost:5480/ai2p/mmfgrp/task/" + source.Id + "\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, Toolset(current), CancellationToken.None);

        Assert.Contains("формулировка источника", answer);
        Assert.Contains("реплика чата источника", answer);
    }

    [Fact]
    public async Task Marker_By_Title_Returns_Found_List()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);

        var markers = ClaudeCliConnector.ParseMarkers(Marker, Marker + " {\"title\": \"Источник\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, Toolset(current), CancellationToken.None);

        Assert.Contains(source.DisplayId, answer);
        Assert.Contains("Источник", answer);
        // поиск отдаёт список, а не тексты заданий
        Assert.DoesNotContain("формулировка источника", answer);
    }

    [Fact]
    public async Task Unknown_Code_Is_Reported_Without_Breaking_Job()
    {
        var current = _f.Tasks.Create(new TaskItem { Title = "Текущая" }, "т", "", null);

        var markers = ClaudeCliConnector.ParseMarkers(Marker, Marker + " {\"code\": \"T-999\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, Toolset(current), CancellationToken.None);

        Assert.Contains("не найдена", answer);
    }

    [Fact]
    public async Task Marker_Without_Parameters_Is_Reported()
    {
        var current = _f.Tasks.Create(new TaskItem { Title = "Текущая" }, "т", "", null);

        var markers = ClaudeCliConnector.ParseMarkers(Marker, Marker + " {\"задача\": \"T-1\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, Toolset(current), CancellationToken.None);

        // T-144: к параметрам чтения добавился templates (список шаблонов проекта)
        Assert.Contains("code, url, title или templates", answer);
    }

    // ---------- правила безопасности ----------

    private static List<SecurityRule> Deny(string actionCode) =>
    [
        new()
        {
            Target = SecurityTarget.Action,
            Permission = SecurityPermission.Deny,
            Pattern = actionCode,
        },
    ];

    [Fact]
    public async Task Denied_Action_Rule_Stops_Reading()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);
        var tools = Toolset(current, Deny("AI2P.Tasks.GetByCode"));

        var markers = ClaudeCliConnector.ParseMarkers(Marker,
            Marker + " {\"code\": \"" + source.DisplayId + "\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, tools, CancellationToken.None);

        Assert.Contains("Запрещено правилами безопасности", answer);
        Assert.DoesNotContain("формулировка источника", answer);
        Assert.Single(tools.SecurityTriggers);
    }

    [Fact]
    public async Task Denied_Chat_Rule_Leaves_The_Card()
    {
        // запрет чата не закрывает само задание: карточка приходит, вместо чата — отказ
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);
        var tools = Toolset(current, Deny("AI2P.Tasks.GetChat"));

        var markers = ClaudeCliConnector.ParseMarkers(Marker,
            Marker + " {\"code\": \"" + source.DisplayId + "\"}", out _);
        var answer = await ClaudeCliConnector.ReadTasksAsync(markers, tools, CancellationToken.None);

        Assert.Contains("формулировка источника", answer);
        Assert.Contains("Запрещено правилами безопасности", answer);
        Assert.DoesNotContain("реплика чата источника", answer);
    }

    // ---------- промпт агента ----------

    [Fact]
    public void Prompt_Note_Explains_The_Marker()
    {
        var current = _f.Tasks.Create(new TaskItem { Title = "Текущая" }, "т", "", null);

        var note = Toolset(current).CliReadTaskNote(Marker, ClaudeCliConnector.MaxTaskLookups);

        Assert.Contains(Marker, note);
        Assert.Contains("code", note);
        Assert.Contains("url", note);
        Assert.Contains("title", note);
        Assert.Contains(ClaudeCliConnector.MaxTaskLookups.ToString(), note);
    }

    [Fact]
    public void Prompt_Note_Is_Empty_When_Action_Denied()
    {
        var current = _f.Tasks.Create(new TaskItem { Title = "Текущая" }, "т", "", null);

        Assert.Equal("", Toolset(current, Deny("AI2P.Tasks.GetByCode"))
            .CliReadTaskNote(Marker, ClaudeCliConnector.MaxTaskLookups));
    }

    // ---------- сквозной прогон задания через коннектор ----------

    /// <summary>
    /// Поддельный CLI на два запуска (внешние процессы в тестах — источник флаки, свой скрипт
    /// детерминирован): первый запуск печатает out1.json (ответ с маркером), второй — out2.json
    /// (финальный ответ). Аргументы каждого запуска дописываются в args.txt — по нему видно,
    /// что второй запуск пошёл с --resume той же сессии.
    /// </summary>
    private string FakeCli(string firstJson, string secondJson)
    {
        var dir = Path.Combine(_f.Dir, "fake-cli-read");
        Directory.CreateDirectory(dir);
        // JSON — отдельными файлами в UTF-8: вывод «как есть» не зависит от кодовой страницы
        File.WriteAllText(Path.Combine(dir, "out1.json"), firstJson, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "out2.json"), secondJson, new System.Text.UTF8Encoding(false));
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "findstr \"^\" > nul\r\n" +
                "echo %*>>\"%~dp0args.txt\"\r\n" +
                "if exist \"%~dp0flag\" goto second\r\n" +
                "copy nul \"%~dp0flag\" > nul\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "goto :eof\r\n" +
                ":second\r\n" +
                "type \"%~dp0out2.json\"\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "cat > /dev/null\n" +
            "d=$(dirname \"$0\")\n" +
            "echo \"$@\" >> \"$d/args.txt\"\n" +
            "if [ -f \"$d/flag\" ]; then cat \"$d/out2.json\"; else touch \"$d/flag\"; cat \"$d/out1.json\"; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string CliJson(string answer, string sessionId, int input, int output) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = sessionId,
            result = answer,
            usage = new { input_tokens = input, output_tokens = output },
        });

    [Fact]
    public async Task Whole_Cli_Job_Reads_Task_And_Continues_Same_Session()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var source = SourceTask(project);
        var ai = _f.Executors.Create(new Executor { Nick = "клод-cli", Kind = ExecutorKind.Ai }, null);
        var first = "Нужен текст задания.\n" + Marker + " {\"code\": \"" + source.DisplayId + "\"}";
        const string second = "Готово: задание прочитано, отчёт составлен.";
        const string profileRel = "models/profile_cli_read.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize(
                  $"\"{FakeCli(CliJson(first, "s-1", 10, 20), CliJson(second, "s-1", 5, 7))}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Текущая", ExecutorIds = [ai.Id],
        }, "разберись с " + source.DisplayId, "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "разберись с " + source.DisplayId);
        var done = await WaitForJob(job.Id);

        Assert.Equal(JobState.Done, done.State);
        // в результат идёт ФИНАЛЬНЫЙ ответ агента, промежуточный (с маркером) — нет
        var result = _f.Files.ReadText(done.ResultPath!);
        Assert.Equal(second, result.Trim());
        Assert.DoesNotContain(Marker, result);
        // второй запуск CLI — продолжение той же сессии
        var args = File.ReadAllText(Path.Combine(_f.Dir, "fake-cli-read", "args.txt"));
        Assert.Contains("--resume s-1", args);
        // токены считаются по обоим виткам (иначе биллинг занижен)
        Assert.Equal(15, done.InputTokens);
        Assert.Equal(27, done.OutputTokens);
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
}
