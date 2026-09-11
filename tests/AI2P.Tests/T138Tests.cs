using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-138: НЕДОДЕЛАННАЯ СДАЧА ЗАДАНИЯ ИЗ-ЗА ФОНОВОЙ РАБОТЫ.
///
/// На T-137 агент запустил полный прогон тестов в фоне и сдал результат словами «прогон
/// идёт в фоне; как только закончится, соберу выкладку и подведу итог». Задание закрылось
/// (задача → review), а фоновый процесс оборвался вместе с ходом агента: лог прогона
/// остался на первых строках, выкладка не собрана, ждать продолжения оказалось некому.
///
/// Лечится с двух сторон:
/// 1. Промпт CLI-агента прямо говорит, что фоновая работа живёт только до конца его хода
///    (<see cref="ClaudeCliConnector.BackgroundNote"/>).
/// 2. Ядро распознаёт такую сдачу (<see cref="BackgroundPromise"/>) и даёт агенту ещё один
///    ход ТОЙ ЖЕ сессии CLI с требованием довести работу до конца — один раз за задание.
/// 3. Ждать иногда действительно нельзя: подзадача (инструмент create_task и маркер
///    AI2P_SUBTASK) получила поле startAfterMinutes — проверочная подзадача запускается
///    сама через заданное время тем же сторожем, что и перенос старта по лимиту (T-121).
/// </summary>
public sealed class T138Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string Marker = ClaudeCliConnector.SubtaskMarker;

    /// <summary>Ответ агента с T-137 — тот самый, из-за которого задание закрылось недоделанным.</summary>
    private const string J34Answer =
        "Прогон тестов идёт в фоне. Как только он закончится, соберу выкладку buildRelease.cmd " +
        "и подведу итог.";

    // ---------- 1. распознавание недоделанной сдачи ----------

    [Theory]
    [InlineData(J34Answer)]
    [InlineData("Сборка выполняется в фоне, отчёт допишу после.")]
    [InlineData("Как только тесты пройдут, обновлю выкладку.")]
    [InlineData("По завершении прогона соберу релиз.")]
    [InlineData("Осталось немного — доделаю в следующий заход.")]
    [InlineData("Дождитесь окончания прогона и посмотрите лог.")]
    public void Unfinished_Answers_Are_Detected(string answer)
    {
        Assert.True(BackgroundPromise.Detect(answer, out var evidence), answer);
        Assert.NotEqual("", evidence);
    }

    [Theory]
    [InlineData("Тесты прогнаны: 729/729, лог t138-tests.log. Выкладка собрана.")]
    [InlineData("Прогон тестов в фоне уже закончился — результат зелёный.")]
    [InlineData("Тестовый сервер работает в фоне на порту 5492, он остановлен в конце проверки.")]
    [InlineData("## Что НЕ сделано\n\nНа Linux ничего не проверялось.")]
    public void Finished_Answers_Are_Not_Detected(string answer)
    {
        Assert.False(BackgroundPromise.Detect(answer, out _), answer);
    }

    [Fact]
    public void Code_Blocks_And_Inline_Code_Are_Ignored()
    {
        // в логах и командах такие фразы — данные, а не обещание агента
        const string answer = """
            Итог: всё сделано.

            ```
            [12:32] прогон идёт в фоне
            ```

            Строка `как только закончится` встречалась в логе.
            """;

        Assert.False(BackgroundPromise.Detect(answer, out _));
    }

    [Fact]
    public void Evidence_Is_The_Offending_Phrase()
    {
        Assert.True(BackgroundPromise.Detect("Работа сделана.\n" + J34Answer, out var evidence));
        // цитируется ровно та фраза, из-за которой работа сочтена незаконченной
        Assert.Contains("Как только он закончится", evidence);
        Assert.DoesNotContain("Работа сделана", evidence);
    }

    // ---------- 2. промпт агента ----------

    [Fact]
    public void Prompt_Warns_That_Background_Work_Dies_With_The_Answer()
    {
        var note = ClaudeCliConnector.BackgroundNote(canSplit: true, TimeSpan.FromMinutes(90));

        Assert.Contains("фоновые процессы обрываются", note);
        Assert.Contains("startAfterMinutes", note);
        // сколько именно ждать можно — из формы исполнителя (T-124), иначе агент гадает
        Assert.Contains("90 мин", note);
        Assert.Contains("времени тебя не ограничивают",
            ClaudeCliConnector.BackgroundNote(canSplit: true, timeout: null));

        // подзадачи закрыты правилом или задача уже разбита — остаётся вопрос человеку
        var without = ClaudeCliConnector.BackgroundNote(canSplit: false);
        Assert.DoesNotContain("startAfterMinutes", without);
        Assert.Contains(ClaudeCliConnector.QuestionMarker.TrimEnd(':'), without);
    }

    [Fact]
    public void Nudge_Quotes_The_Phrase_And_Demands_Full_Result()
    {
        var text = ClaudeCliConnector.UnfinishedNudge("прогон идёт в фоне", canSplit: true);

        Assert.Contains("прогон идёт в фоне", text);
        Assert.Contains("startAfterMinutes", text);
        Assert.Contains("ПОЛНЫЙ итоговый результат", text);
    }

    // ---------- 3. отложенный старт проверочной подзадачи ----------

    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """
            {
              "skills": [ { "name": "code-test", "score": 90 } ],
              "cost": { "in_per_1m": 1, "out_per_1m": 2 }
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel,
        }, null);
    }

    private AgentToolset Toolset(TaskItem current) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, null),
            actionCodeByTool: name => name == "create_task" ? "AI2P.Tasks.Create" : null);

    [Fact]
    public async Task Subtask_Marker_Can_Defer_Its_Start()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var tools = Toolset(parent);
        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"Проверить прогон\", \"description\": \"смотри t138-tests.log\", "
                   + "\"startAfterMinutes\": 30}", out _);

        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        var child = Assert.Single(_f.Tasks.ListChildren(parent.Id));
        var startAfter = TaskService.LaunchStartAfter(child.LaunchJson);
        Assert.NotNull(startAfter);
        Assert.Equal(DateTime.UtcNow.AddMinutes(30), startAfter!.Value, TimeSpan.FromMinutes(1));
        Assert.Equal(TaskStatuses.Pending, child.Status);   // сторож поднимает draft/pending
        Assert.Contains("запуск отложен", summary);
    }

    [Fact]
    public async Task Subtask_Without_Delay_Starts_As_Usual()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"Обычная\", \"description\": \"д\"}", out _);

        var summary = await ClaudeCliConnector.CreateSubtasksAsync(markers, Toolset(parent),
            CancellationToken.None);

        var child = Assert.Single(_f.Tasks.ListChildren(parent.Id));
        Assert.Null(TaskService.LaunchStartAfter(child.LaunchJson));
        Assert.DoesNotContain("запуск отложен", summary);
    }

    [Fact]
    public async Task Absurd_Delay_Is_Clamped_To_A_Day()
    {
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            Marker + " {\"title\": \"Через год\", \"description\": \"д\", "
                   + "\"startAfterMinutes\": 999999}", out _);

        await ClaudeCliConnector.CreateSubtasksAsync(markers, Toolset(parent), CancellationToken.None);

        var child = Assert.Single(_f.Tasks.ListChildren(parent.Id));
        Assert.Equal(DateTime.UtcNow.AddMinutes(TaskToolset.MaxStartDelayMinutes),
            TaskService.LaunchStartAfter(child.LaunchJson)!.Value, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Deferred_Subtask_Is_Picked_Up_When_Its_Time_Comes()
    {
        // тот же механизм, что у переноса старта по лимиту (T-121): сторож выбирает задачи
        // с наступившим startAfter — раньше срока задача в выборку не попадает
        var parent = _f.Tasks.Create(new TaskItem { Title = "Родитель" }, "т", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "Проверка прогона", Status = TaskStatuses.Pending,
        }, "д", "", null);
        _f.Tasks.SetStartAfter(child.Id, DateTime.UtcNow.AddMinutes(30));

        Assert.DoesNotContain(_f.Tasks.ListStartAfterDue(DateTime.UtcNow), t => t.Id == child.Id);
        Assert.Contains(_f.Tasks.ListStartAfterDue(DateTime.UtcNow.AddMinutes(31)), t => t.Id == child.Id);
    }

    // ---------- 4. сквозной прогон задания через коннектор ----------

    /// <summary>
    /// Поддельный CLI на два разных ответа: первый вызов печатает out1.json, второй и
    /// последующие — out2.json; промпт каждого вызова сохраняется в prompt&lt;N&gt;.txt
    /// (по нему видно, что именно система потребовала от агента и сколько было вызовов).
    /// Свой процесс вместо настоящего Claude Code: внешние процессы в тестах — источник флаки.
    /// </summary>
    private string FakeCli(string dirName, string out1Json, string out2Json)
    {
        var dir = Path.Combine(_f.Dir, dirName);
        Directory.CreateDirectory(dir);
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(dir, "out1.json"), out1Json, utf8);
        File.WriteAllText(Path.Combine(dir, "out2.json"), out2Json, utf8);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "if exist \"%~dp0prompt2.txt\" goto third\r\n" +
                "if exist \"%~dp0prompt1.txt\" goto second\r\n" +
                "findstr \"^\" > \"%~dp0prompt1.txt\"\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "exit /b 0\r\n" +
                ":second\r\n" +
                "findstr \"^\" > \"%~dp0prompt2.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n" +
                ":third\r\n" +
                "findstr \"^\" > \"%~dp0prompt3.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "d=\"$(dirname \"$0\")\"\n" +
            "if [ -f \"$d/prompt2.txt\" ]; then cat > \"$d/prompt3.txt\"; cat \"$d/out2.json\";\n" +
            "elif [ -f \"$d/prompt1.txt\" ]; then cat > \"$d/prompt2.txt\"; cat \"$d/out2.json\";\n" +
            "else cat > \"$d/prompt1.txt\"; cat \"$d/out1.json\"; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string CliJson(string answer) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = "s-138",
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    /// <summary>Задача с ИИ-исполнителем на поддельном CLI.</summary>
    private async Task<(Job Job, string CliDir)> RunCliJobAsync(string dirName, string first, string second)
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("клод-" + dirName);
        var cliPath = FakeCli(dirName, CliJson(first), CliJson(second));
        var profileRel = $"models/profile_{dirName}.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{cliPath}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Версия 1.67", ExecutorIds = [ai.Id],
        }, "прогони тесты и собери выкладку", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "прогони тесты и собери выкладку");
        return (await WaitForJob(job.Id), Path.GetDirectoryName(cliPath)!);
    }

    [Fact]
    public async Task Job_Sold_As_Unfinished_Gets_One_More_Turn()
    {
        var (job, cliDir) = await RunCliJobAsync("cli-nudge", J34Answer,
            "Тесты 729/729, выкладка собрана. Готово.");

        Assert.Equal(JobState.Done, job.State);
        // результат задания — ВТОРОЙ ответ: первый (недоделанный) человек не видит
        var result = _f.Files.ReadText(job.ResultPath!);
        Assert.Contains("Тесты 729/729", result);
        Assert.DoesNotContain("подведу итог", result);

        // первый промпт — само задание с правилом про фон, второй — требование системы
        var first = File.ReadAllText(Path.Combine(cliDir, "prompt1.txt"));
        Assert.Contains("фоновые процессы обрываются", first);
        var second = File.ReadAllText(Path.Combine(cliDir, "prompt2.txt"));
        Assert.Contains("Ответ системы AI2P", second);
        Assert.Contains("Как только он закончится", second);   // цитата из недоделанной сдачи
        Assert.False(File.Exists(Path.Combine(cliDir, "prompt3.txt")));
    }

    [Fact]
    public async Task Nudge_Happens_Only_Once_Per_Job()
    {
        // агент упёрся и второй раз сдал то же самое: задание всё равно завершается,
        // сессия по кругу не гоняется (каждый ход — деньги подписки)
        var (job, cliDir) = await RunCliJobAsync("cli-stubborn", J34Answer, J34Answer);

        Assert.Equal(JobState.Done, job.State);
        Assert.True(File.Exists(Path.Combine(cliDir, "prompt2.txt")));
        Assert.False(File.Exists(Path.Combine(cliDir, "prompt3.txt")));
    }

    [Fact]
    public async Task Question_Is_A_Legal_Pause_And_Is_Not_Nudged()
    {
        // вопрос человеку — законная остановка работы: переспрашивать агента незачем,
        // даже если рядом сказано «как только ответите — доделаю»
        var (job, cliDir) = await RunCliJobAsync("cli-question",
            "Не знаю, какую версию выпускать. Как только ответите, доделаю.\n"
            + ClaudeCliConnector.QuestionMarker + " {\"question\": \"Какая версия?\"}",
            "не должно понадобиться");

        Assert.Equal(JobState.WaitingHuman, job.State);
        Assert.False(File.Exists(Path.Combine(cliDir, "prompt2.txt")));
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
