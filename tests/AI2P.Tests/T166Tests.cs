using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-166: ЛИМИТ ПОДПИСКИ CLI-АГЕНТА — ОЖИДАНИЕ ВМЕСТО ОШИБКИ, И ПРОДОЛЖЕНИЕ ТОЙ ЖЕ СЕССИИ.
///
/// Что было. Claude CLI сменил формулировку отказа: вместо «5-hour limit reached ∙ resets …»
/// он присылает «You've hit your session limit · resets 9:20pm (Europe/Moscow)» (код возврата 1,
/// в JSON — "api_error_status":429). Прежний список признаков (T-121) таких слов не знал,
/// поэтому лимит выглядел обычной ошибкой: задача вставала «с ошибкой», у исполнителя не было
/// «занят до», ждать сброса и продолжать работу было некому.
///
/// Что стало. Формулировка и код 429 распознаются как лимит; задача переходит в ОЖИДАНИЕ до
/// момента сброса (+1 минута запаса), исполнитель помечается занятым, а связь с агентом не
/// рвётся: id сессии CLI сохраняется контекстом задания, и по наступлении срока система
/// продолжает ТУ ЖЕ сессию словом «Continue» — агент доделывает начатое, а не начинает
/// задание заново.
/// </summary>
public sealed class T166Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Момент отсчёта для разбора времени сброса — как в Todo62Tests.</summary>
    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Настоящий вывод CLI из задания T-166 (сокращён до значимых полей).</summary>
    private const string RealLimitJson = """
        {"is_error":true,"duration_api_ms":276489,"num_turns":29,"stop_reason":"stop_sequence",
         "session_id":"2ec84314-b3c9-459f-a163-63ecfe1e2cce","total_cost_usd":2.391922,
         "usage":{"input_tokens":47,"cache_creation_input_tokens":25417,
                  "cache_read_input_tokens":3862884,"output_tokens":8243},
         "terminal_reason":"api_error","subtype":"success","api_error_status":429,
         "result":"You've hit your session limit · resets 9:20pm (Europe/Moscow)","type":"result"}
        """;

    // ---------- 1. распознавание нового отказа ----------

    [Fact]
    public void Session_Limit_Wording_Is_Recognized_With_Reset_Time()
    {
        // до T-166 эта строка лимитом не считалась: слов «limit reached» в ней нет
        const string text = "You've hit your session limit · resets 9:20pm (Europe/Moscow)";

        Assert.True(ClaudeCliLimit.TryDetect(text, Now, out var resetAt));
        // 21:20 по Москве = 18:20 UTC того же дня (Now — полдень UTC)
        Assert.Equal(new DateTime(2026, 8, 12, 18, 20, 0, DateTimeKind.Utc), resetAt);
    }

    [Fact]
    public void Real_Cli_Json_Is_A_Limit_And_Keeps_The_Session_Id()
    {
        var output = ClaudeCliConnector.ParseCliOutput(RealLimitJson, "claude-opus-5");

        Assert.True(output.IsError);
        Assert.Equal("2ec84314-b3c9-459f-a163-63ecfe1e2cce", output.SessionId);
        Assert.True(ClaudeCliLimit.TryDetect(RealLimitJson, Now, out var resetAt));
        Assert.Equal(new DateTime(2026, 8, 12, 18, 20, 0, DateTimeKind.Utc), resetAt);
    }

    [Fact]
    public void Api_Error_429_Is_A_Limit_Even_Without_Familiar_Words()
    {
        // слова у CLI меняются от версии к версии, код отказа — нет
        const string json = """{"is_error":true,"api_error_status":429,"result":"Try again later"}""";

        Assert.True(ClaudeCliLimit.TryDetect(json, Now, out var resetAt));
        Assert.Null(resetAt); // времени CLI не назвал — «занят до» поставится на час
    }

    [Fact]
    public void Ordinary_Cli_Failure_Is_Still_Not_A_Limit()
    {
        Assert.False(ClaudeCliLimit.TryDetect(
            """{"is_error":true,"api_error_status":500,"result":"API Error: internal"}""", Now, out _));
        Assert.False(ClaudeCliLimit.TryDetect("Claude CLI завершился с кодом 1: command not found",
            Now, out _));
    }

    [Fact]
    public void Report_About_Session_Limits_Is_Not_Mistaken_For_A_Limit()
    {
        // отчёт ЭТОЙ задачи полон слов «session limit» — успешный длинный ответ лимитом
        // считаться не должен, иначе задание оборвётся на ровном месте
        var report = "Разобрался с обработкой ответа «you've hit your session limit»: теперь "
                     + "задача ждёт сброса. " + new string('т', ClaudeCliLimit.ShortAnswerLimit);

        Assert.False(ClaudeCliLimit.TryDetectInAnswer(report, Now, out _));
    }

    // ---------- 2. лимит на середине работы: ожидание + сохранённая сессия ----------

    [Fact]
    public async Task Limit_Makes_Task_Wait_And_Keeps_The_Cli_Session()
    {
        var (job, _, task, ai) = await RunUntilLimitAsync("cli-limit");

        // задание — с ошибкой (в артефакте видно почему), задача — ЖДЁТ, а не «встала с ошибкой»;
        // ожидание по лимиту — состояние «пауза» (T-185)
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(task.Id)!.Status);

        // ждём до названного CLI времени + минута запаса (T-166)
        var expected = ClaudeCliLimit.ResetAt("resets 9:20pm (Europe/Moscow)", DateTime.UtcNow)!.Value
                       + ClaudeCliConnector.LimitGrace;
        var startAfter = TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson);
        Assert.Equal(expected, startAfter!.Value, TimeSpan.FromMinutes(1));
        // у исполнителя — «занят до» того же момента: автоподбор его до этого не назначит
        Assert.Equal(expected, _f.Executors.Get(ai.Id)!.BusyUntil!.Value, TimeSpan.FromMinutes(1));

        // связь с агентом не порвана: id сессии сохранён, задача помнит, какое задание продолжать
        Assert.Equal(job.Id, TaskService.LaunchResumeJob(_f.Tasks.Get(task.Id)!.LaunchJson));
        var context = _f.Files.ReadText(
            Path.Combine(_f.Files.TaskDirRel(null, task.DisplayId), "ai", $"{job.DisplayId}-context.json"));
        Assert.Contains("s-166", context);

        // человеку в чат — до какого времени ждём и что работа продолжится, а не начнётся заново
        // сообщение в чат пишется СЛЕДУЮЩЕЙ строкой после смены статуса задачи, а ждали мы
        // именно статуса (RunUntilLimitAsync) — под нагрузкой полного прогона тест успевал
        // прочитать чат в промежутке и падал на ровном месте, как это уже было со статусом
        var message = Assert.Single(await WaitForChat(task.Id));
        Assert.Equal(ai.Id, message.FromExecutorId);
        Assert.Contains("продолжится сама", message.Text);
        Assert.Contains("Компьютер можно выключать", message.Text);

        // в тексте ошибки — человеческая причина от CLI, а не весь JSON целиком
        var error = _f.Files.ReadText(job.ResultPath!);
        Assert.Contains("session limit", error);
        Assert.DoesNotContain("cache_read_input_tokens", error);
    }

    [Fact]
    public async Task When_Time_Comes_The_Same_Session_Continues_With_Continue()
    {
        var (job, cliDir, task, _) = await RunUntilLimitAsync("cli-continue");

        // до срока сторож задачу не трогает
        _f.Orchestrator.StartDeferredOnce();
        Assert.Single(_f.Jobs.ListByTask(task.Id));

        // срок наступил — продолжается ТО ЖЕ задание, новое не заводится
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Orchestrator.StartDeferredOnce();
        var finished = await WaitForJob(job.Id);

        Assert.Single(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(JobState.Done, finished.State);
        // состояние задачи ставится СЛЕДУЮЩЕЙ строкой после состояния задания, поэтому ждём
        // именно его: под нагрузкой полного прогона задание успевало стать Done раньше, чем
        // задача — «проверкой», и тест падал на ровном месте
        Assert.Equal(TaskStatuses.Review, await WaitForStatus(task.Id, TaskStatuses.Review));
        Assert.Contains("доделал", _f.Files.ReadText(finished.ResultPath!));

        // агенту ушло «Continue» ТОЙ ЖЕ сессией CLI — задание с начала не переигрывалось
        var prompt = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt2.txt"));
        Assert.StartsWith("Continue", prompt.TrimStart());
        var args = await File.ReadAllTextAsync(Path.Combine(cliDir, "args2.txt"));
        Assert.Contains("--resume", args);
        Assert.Contains("s-166", args);

        // ожидание снято: ни отложенного старта, ни указателя на продолжение
        var after = _f.Tasks.Get(task.Id)!;
        Assert.Null(TaskService.LaunchStartAfter(after.LaunchJson));
        Assert.Null(TaskService.LaunchResumeJob(after.LaunchJson));
    }

    [Fact]
    public async Task Without_Saved_Session_Task_Restarts_By_A_New_Job()
    {
        var (job, cliDir, task, _) = await RunUntilLimitAsync("cli-nosession");

        // контекст сессии потерян (чистка файлов, перенос задачи) — продолжать нечем
        File.Delete(_f.Files.Abs(
            Path.Combine(_f.Files.TaskDirRel(null, task.DisplayId), "ai", $"{job.DisplayId}-context.json")));
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Orchestrator.StartDeferredOnce();

        // задача не встала: запускается заново новым заданием, как было до T-166
        var jobs = _f.Jobs.ListByTask(task.Id);
        Assert.Equal(2, jobs.Count);
        var second = await WaitForJob(jobs.First(j => j.Id != job.Id).Id);
        Assert.Equal(JobState.Done, second.State);
        var prompt = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt2.txt"));
        Assert.DoesNotContain("Continue.", prompt);   // это новый запуск, а не продолжение
        Assert.Null(TaskService.LaunchResumeJob(_f.Tasks.Get(task.Id)!.LaunchJson));
    }

    [Fact]
    public async Task Vanished_Session_Does_Not_Break_The_Task_It_Starts_Over()
    {
        // сессии CLI не вечны: недельное окно лимита можно и не застать. Тогда продолжать
        // нечего — задание идёт с начала тем же заданием, а не встаёт ошибкой
        var (job, cliDir, task, _) = await RunUntilLimitAsync("cli-gone", sessionGone: true);

        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Orchestrator.StartDeferredOnce();
        var finished = await WaitForJob(job.Id);

        Assert.Single(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(JobState.Done, finished.State);
        // второй ход просил продолжения, третий — полный промпт задания в НОВОЙ сессии
        Assert.Contains("--resume", await File.ReadAllTextAsync(Path.Combine(cliDir, "args2.txt")));
        var third = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt3.txt"));
        Assert.Contains("прогони тесты", third);
        Assert.Contains("--session-id", await File.ReadAllTextAsync(Path.Combine(cliDir, "args3.txt")));
    }

    [Fact]
    public async Task Message_Written_While_Waiting_Rides_Along_With_Continue()
    {
        // человек увидел «⏳ лимит исчерпан» и написал в чат, пока задача ждала: агент этих
        // слов ещё не видел (в его сессии их нет) — они обязаны доехать вместе с «Continue»
        var (job, cliDir, task, _) = await RunUntilLimitAsync("cli-chat");
        var human = _f.Executors.Create(new Executor { Nick = "михаил", Kind = ExecutorKind.Human }, null);
        _f.Chat.Add(task.Id, human.Id, null, "начни с прогона тестов, остальное потом");

        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Orchestrator.StartDeferredOnce();
        await WaitForJob(job.Id);

        var prompt = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt2.txt"));
        Assert.StartsWith("Continue", prompt.TrimStart());
        Assert.Contains("начни с прогона тестов", prompt);
    }

    [Fact]
    public void Missing_Session_Answer_Of_Cli_Is_Recognized()
    {
        Assert.True(ClaudeCliConnector.SessionIsGone("No conversation found with session ID: s-166"));
        Assert.False(ClaudeCliConnector.SessionIsGone("Claude CLI завершился с кодом 1: command not found"));
    }

    // ---------- обвязка ----------

    private Executor CreateAi(string nick, string cliPath)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """
            {
              "skills": [ { "name": "code-test", "score": 90 } ],
              "cost": { "in_per_1m": 1, "out_per_1m": 2 }
            }
            """);
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{cliPath}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel, ProfilePath = profileRel,
        }, null);
    }

    /// <summary>
    /// Поддельный CLI: первый запуск упирается в лимит (код возврата 1 и настоящий JSON
    /// отказа), второй — отвечает результатом. Свой процесс вместо настоящего Claude Code:
    /// внешние процессы в тестах источник флаки (и денег подписки).
    /// </summary>
    /// <param name="sessionGone">Второй запуск (продолжение сессии) отвечает «сессии нет»,
    /// и результат отдаёт только третий — уже с полным промптом задания.</param>
    private string FakeCli(string dirName, string limitJson, string doneJson, bool sessionGone)
    {
        var dir = Path.Combine(_f.Dir, dirName);
        Directory.CreateDirectory(dir);
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(dir, "out1.json"), limitJson, utf8);
        File.WriteAllText(Path.Combine(dir, "out2.json"), doneJson, utf8);
        const string gone = "No conversation found with session ID: s-166";
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "if exist \"%~dp0prompt2.txt\" goto third\r\n" +
                "if exist \"%~dp0prompt1.txt\" goto second\r\n" +
                "echo %* > \"%~dp0args1.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt1.txt\"\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "exit /b 1\r\n" +
                ":second\r\n" +
                "echo %* > \"%~dp0args2.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt2.txt\"\r\n" +
                (sessionGone
                    ? $"echo {gone} 1>&2\r\nexit /b 1\r\n"
                    : "type \"%~dp0out2.json\"\r\nexit /b 0\r\n") +
                ":third\r\n" +
                "echo %* > \"%~dp0args3.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt3.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "d=\"$(dirname \"$0\")\"\n" +
            "if [ -f \"$d/prompt2.txt\" ]; then echo \"$@\" > \"$d/args3.txt\"; " +
            "cat > \"$d/prompt3.txt\"; cat \"$d/out2.json\"; exit 0;\n" +
            "elif [ -f \"$d/prompt1.txt\" ]; then echo \"$@\" > \"$d/args2.txt\"; " +
            "cat > \"$d/prompt2.txt\"; " +
            (sessionGone ? $"echo \"{gone}\" 1>&2; exit 1;\n" : "cat \"$d/out2.json\"; exit 0;\n") +
            "else echo \"$@\" > \"$d/args1.txt\"; cat > \"$d/prompt1.txt\"; " +
            "cat \"$d/out1.json\"; exit 1; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string LimitJson(string sessionId) =>
        """
        {"type":"result","subtype":"success","is_error":true,"session_id":"SID",
         "terminal_reason":"api_error","api_error_status":429,
         "result":"You've hit your session limit · resets 9:20pm (Europe/Moscow)",
         "usage":{"input_tokens":10,"output_tokens":5}}
        """.Replace("SID", sessionId);

    private static string DoneJson(string sessionId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = sessionId,
            result = "Готово: доделал начатое после сброса лимита.",
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    /// <summary>Задание ИИ-исполнителя на поддельном CLI, упёршееся в лимит: ждём, пока
    /// задание закроется, а задача перейдёт в ожидание.</summary>
    private async Task<(Job Job, string CliDir, TaskItem Task, Executor Ai)> RunUntilLimitAsync(
        string dirName, bool sessionGone = false)
    {
        var cliPath = FakeCli(dirName, LimitJson("s-166"), DoneJson("s-166"), sessionGone);
        var ai = CreateAi("клод-" + dirName, cliPath);
        var task = _f.Tasks.Create(new TaskItem
        {
            Title = "Прогон тестов", ExecutorIds = [ai.Id],
        }, "прогони тесты", "", null);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "прогони тесты");

        var finished = await WaitForJob(job.Id);
        for (var i = 0; i < 200 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await System.Threading.Tasks.Task.Delay(50);
        }
        return (finished, Path.GetDirectoryName(cliPath)!, task, ai);
    }

    /// <summary>Дождаться ожидаемого состояния задачи: завершение задания и смена состояния
    /// задачи — две записи подряд, и между ними тест может успеть прочитать старое значение.
    /// Возвращает состояние задачи (совпавшее либо последнее увиденное — тогда упадёт ассерт).</summary>
    private async Task<string> WaitForStatus(string taskId, string expected)
    {
        var status = "";
        for (var i = 0; i < 100; i++)
        {
            status = _f.Tasks.Get(taskId)!.Status;
            if (status == expected)
            {
                return status;
            }
            await System.Threading.Tasks.Task.Delay(50);
        }
        return status;
    }

    /// <summary>Дождаться, пока в чате задачи появится сообщение: его пишет тот же метод
    /// (<c>DeferAfterLimit</c>), но ПОСЛЕ смены статуса задачи, которого ждёт
    /// <see cref="RunUntilLimitAsync"/>.</summary>
    private async Task<List<ChatMessage>> WaitForChat(string taskId)
    {
        var messages = _f.Chat.ListByTask(taskId);
        for (var i = 0; i < 100 && messages.Count == 0; i++)
        {
            await System.Threading.Tasks.Task.Delay(50);
            messages = _f.Chat.ListByTask(taskId);
        }
        return messages;
    }

    /// <summary>Дождаться завершения фонового задания (ТЗ п. 3, принцип 4).</summary>
    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 600; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await System.Threading.Tasks.Task.Delay(100);
        }
        throw new TimeoutException($"Задание {jobId} не завершилось за 60 с");
    }
}
