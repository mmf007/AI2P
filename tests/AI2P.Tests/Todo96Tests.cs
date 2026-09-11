using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo96 (версия 1.96, экстренный выпуск): ИСТЁКШИЙ ВХОД В CLAUDE CLI — ОЖИДАНИЕ ВМЕСТО
/// ОШИБКИ, И ВХОД ИЗ САМОГО AI2P.
///
/// Что было. Сеанс CLI протух, и КАЖДЫЙ запуск агента стал возвращать код 1 с итоговым JSON
/// <c>"result":"Failed to authenticate: OAuth session expired and could not be refreshed"</c>.
/// Система об этом не знала: задача вставала «встал с ошибкой», в консоли лежала простыня
/// JSON, а войти заново можно было только руками в терминале того компьютера, где работает
/// сервер (<c>claude auth login</c>).
///
/// Что стало.
/// <list type="number">
/// <item>отказ по входу распознаётся (<see cref="ClaudeCliAuth"/>) и НЕ путается с лимитом:
///   у них разные коды — 401/403 против 429;</item>
/// <item>задача уходит в ПАУЗУ с пометкой «ждёт входа», а не в ошибку; сессия агента
///   сохраняется, поэтому после входа работа продолжается, а не начинается заново;</item>
/// <item>вход делается из AI2P (Настройки → Модели → «Вход в Claude CLI»): ссылка авторизации
///   и код со страницы входа — <see cref="ClaudeLoginService"/>;</item>
/// <item>успешный вход отпускает все ждавшие задачи разом
///   (<see cref="JobOrchestrator.ReleaseWaitingForAuth"/>).</item>
/// </list>
/// </summary>
public sealed class Todo96Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Настоящий вывод CLI из задания todo96 (сокращён до значимых полей).</summary>
    private const string RealAuthJson = """
        {"is_error":true,"duration_api_ms":0,"num_turns":1,"stop_reason":"stop_sequence",
         "session_id":"25114fa6-e63f-4642-80f0-abe70c2ef8d8","total_cost_usd":0,
         "usage":{"input_tokens":0,"output_tokens":0},
         "terminal_reason":"api_error","subtype":"success","api_error_status":null,
         "result":"Failed to authenticate: OAuth session expired and could not be refreshed",
         "type":"result","duration_ms":278}
        """;

    // ---------- 1. распознавание «входа нет» ----------

    [Fact]
    public void Real_Cli_Json_Is_An_Auth_Failure_And_Keeps_The_Session_Id()
    {
        var output = ClaudeCliConnector.ParseCliOutput(RealAuthJson, "claude-opus-5");

        Assert.True(output.IsError);
        Assert.Equal("25114fa6-e63f-4642-80f0-abe70c2ef8d8", output.SessionId);
        Assert.True(ClaudeCliAuth.LooksLikeAuthGone(RealAuthJson));
    }

    [Fact]
    public void Api_Error_401_Is_An_Auth_Failure_Even_Without_Familiar_Words()
    {
        // слова у CLI меняются от версии к версии, код отказа — нет
        Assert.True(ClaudeCliAuth.LooksLikeAuthGone(
            """{"is_error":true,"api_error_status":401,"result":"Try again"}"""));
        Assert.True(ClaudeCliAuth.LooksLikeAuthGone("Please run /login to authenticate"));
        Assert.True(ClaudeCliAuth.LooksLikeAuthGone("You are not logged in"));
    }

    [Fact]
    public void Auth_Failure_And_Limit_Are_Not_Confused()
    {
        // 429 — это лимит и только лимит: пережидание, а не вход
        const string limit = """
            {"is_error":true,"api_error_status":429,
             "result":"You've hit your session limit · resets 9:20pm (Europe/Moscow)"}
            """;
        Assert.False(ClaudeCliAuth.LooksLikeAuthGone(limit));
        Assert.True(ClaudeCliLimit.TryDetect(limit, Now, out _));

        // и наоборот: истёкший вход лимитом не считается — ждать там нечего
        Assert.False(ClaudeCliLimit.TryDetect(RealAuthJson, Now, out _));
    }

    [Fact]
    public void Ordinary_Cli_Failure_Is_Not_An_Auth_Failure()
    {
        Assert.False(ClaudeCliAuth.LooksLikeAuthGone(
            """{"is_error":true,"api_error_status":500,"result":"API Error: internal"}"""));
        Assert.False(ClaudeCliAuth.LooksLikeAuthGone(
            "Claude CLI завершился с кодом 1: command not found"));
    }

    [Fact]
    public void Report_About_Sign_In_Is_Not_Mistaken_For_An_Auth_Failure()
    {
        // отчёт ЭТОЙ задачи полон слов «failed to authenticate» — успешный длинный ответ
        // отказом по входу считаться не должен, иначе задание оборвётся на ровном месте
        var report = "Сделал обработку ответа «failed to authenticate: OAuth session expired»: "
                     + "теперь задача ждёт входа. " + new string('т', ClaudeCliAuth.ShortAnswerLimit);

        Assert.False(ClaudeCliAuth.LooksLikeAuthGoneInAnswer(report));
        // а короткое сообщение вместо результата — считается
        Assert.True(ClaudeCliAuth.LooksLikeAuthGoneInAnswer(
            "Failed to authenticate: OAuth session expired and could not be refreshed"));
    }

    // ---------- 2. ответ claude auth status --json ----------

    [Fact]
    public void Auth_Status_Json_Is_Parsed()
    {
        var status = ClaudeCliAuth.ParseStatus("""
            {"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty",
             "email":"mike@roomplannerapp.com","orgName":"Org","subscriptionType":"max"}
            """);

        Assert.True(status.Known);
        Assert.True(status.LoggedIn);
        Assert.Equal("mike@roomplannerapp.com", status.Email);
        Assert.Equal("claude.ai", status.Method);
        Assert.Equal("max", status.Subscription);
    }

    [Fact]
    public void Unknown_Answer_Of_An_Old_Cli_Is_Not_A_Missing_Sign_In()
    {
        // старый CLI команды auth не знает: «выяснить не удалось» — не то же самое,
        // что «входа нет», и пробу подключения это ронять не должно
        foreach (var text in new[] { "", "error: unknown command 'auth'", "{\"foo\":1}" })
        {
            var status = ClaudeCliAuth.ParseStatus(text);
            Assert.False(status.Known);
            Assert.False(status.LoggedIn);
        }
        Assert.False(ClaudeCliAuth.ParseStatus("""{"loggedIn":false}""").LoggedIn);
        Assert.True(ClaudeCliAuth.ParseStatus("""{"loggedIn":false}""").Known);
    }

    // ---------- 3. команда входа берётся из профайла, но БЕЗ флагов агента ----------

    [Fact]
    public void Login_Uses_Only_The_Executable_Of_The_Profile_Command()
    {
        // флаги запуска агента команде `auth` не подходят — берётся только исполняемый файл
        Assert.Equal("claude", ClaudeLoginService.Executable("claude --permission-mode acceptEdits"));
        Assert.Equal("claude", ClaudeLoginService.Executable(""));
        Assert.Equal("claude", ClaudeLoginService.Executable(null));
        Assert.Equal(@"C:\tools\claude.cmd",
            ClaudeLoginService.Executable("\"C:\\tools\\claude.cmd\" --verbose"));
    }

    [Fact]
    public void Cli_Command_Is_Taken_From_The_Cli_Profile_Of_An_Executor()
    {
        CreateAi("клод-профайл", "claude --permission-mode acceptEdits");

        Assert.Equal("claude --permission-mode acceptEdits", _f.Orchestrator.ClaudeCliCommand());
    }

    // ---------- 4. истёкший вход на середине работы: пауза, а не ошибка ----------

    [Fact]
    public async Task Expired_Sign_In_Makes_Task_Wait_For_Login_Instead_Of_Failing()
    {
        var (job, _, task, ai) = await RunUntilAuthFailureAsync("cli-auth");

        // задание — с ошибкой (в артефакте видно почему), задача — ЖДЁТ входа, а не «встала»
        Assert.Equal(JobState.Failed, job.State);
        var stored = _f.Tasks.Get(task.Id)!;
        Assert.Equal(TaskStatuses.Paused, stored.Status);
        Assert.True(TaskService.HasLaunchFlag(stored.LaunchJson, TaskService.WaitAuthFlag));

        // исполнителя лимит не помечал: он не занят, он просто не может войти
        Assert.Null(_f.Executors.Get(ai.Id)!.BusyUntil);

        // запасной путь: человек мог войти и в терминале — тогда задача тронется сама
        var startAfter = TaskService.LaunchStartAfter(stored.LaunchJson);
        Assert.Equal(DateTime.UtcNow + AiConnectorBase.AuthRetry, startAfter!.Value,
            TimeSpan.FromMinutes(2));

        // связь с агентом не порвана: id сессии сохранён, задача помнит, что продолжать
        Assert.Equal(job.Id, TaskService.LaunchResumeJob(stored.LaunchJson));
        var context = _f.Files.ReadText(
            Path.Combine(_f.Files.TaskDirRel(null, task.DisplayId), "ai", $"{job.DisplayId}-context.json"));
        Assert.Contains("s-96", context);

        // человеку в чат — что случилось и КУДА идти входить
        var message = Assert.Single(await WaitForChat(task.Id));
        Assert.Equal(ai.Id, message.FromExecutorId);
        Assert.Contains("Вход в Claude CLI", message.Text);
        Assert.Contains("продолжится сама", message.Text);

        // в тексте ошибки — человеческая причина от CLI, а не весь JSON целиком
        var error = _f.Files.ReadText(job.ResultPath!);
        Assert.Contains("OAuth session expired", error);
        Assert.DoesNotContain("duration_api_ms", error);
    }

    [Fact]
    public async Task Waiting_Tasks_Are_Counted_And_Released_By_A_Sign_In()
    {
        var (job, cliDir, task, _) = await RunUntilAuthFailureAsync("cli-release");

        // форма входа показывает, ради чего входить
        Assert.Equal(1, _f.Orchestrator.WaitingForAuthCount());

        // вошли — задачи отпущены и тронулись ТОЙ ЖЕ сессией агента, новых заданий нет
        Assert.Equal(1, _f.Orchestrator.ReleaseWaitingForAuth());
        var finished = await WaitForJob(job.Id);

        Assert.Single(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(JobState.Done, finished.State);
        Assert.Equal(TaskStatuses.Review, await WaitForStatus(task.Id, TaskStatuses.Review));

        var args = await File.ReadAllTextAsync(Path.Combine(cliDir, "args2.txt"));
        Assert.Contains("--resume", args);
        Assert.Contains("s-96", args);

        // ожидание снято целиком: ни пометки, ни отложенного старта, ни указателя продолжения
        var after = _f.Tasks.Get(task.Id)!;
        Assert.False(TaskService.HasLaunchFlag(after.LaunchJson, TaskService.WaitAuthFlag));
        Assert.Null(TaskService.LaunchStartAfter(after.LaunchJson));
        Assert.Null(TaskService.LaunchResumeJob(after.LaunchJson));
        Assert.Equal(0, _f.Orchestrator.WaitingForAuthCount());
    }

    [Fact]
    public async Task Tasks_Waiting_For_Login_Are_Listed_Apart_From_The_Rest()
    {
        var (_, _, task, _) = await RunUntilAuthFailureAsync("cli-list");
        // соседняя задача на паузе, но входа не ждёт — в список попадать не должна
        var other = _f.Tasks.Create(new TaskItem { Title = "Соседняя" }, "текст", "", null);
        _f.Tasks.ChangeStatus(other.Id, TaskStatuses.Paused, null);

        var waiting = _f.Tasks.ListWaitAuth();

        Assert.Equal(task.Id, Assert.Single(waiting).Id);
    }

    // ---------- 5. текст сообщения человеку ----------

    [Fact]
    public void Chat_Message_Says_Where_To_Sign_In()
    {
        var withSession = AiConnectorBase.AuthWaitMessage("клод", canContinue: true);
        var without = AiConnectorBase.AuthWaitMessage("клод", canContinue: false);

        foreach (var text in new[] { withSession, without })
        {
            Assert.Contains("клод", text);
            Assert.Contains("Вход в Claude CLI", text);   // куда идти
            Assert.Contains("claude auth login", text);    // и как то же самое сделать руками
        }
        Assert.Contains("продолжится сама", withSession);
        Assert.Contains("запустится сама", without);
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
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize(cliPath)}},
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
    /// Поддельный CLI: первый запуск упирается в истёкший вход (код возврата 1 и настоящий
    /// JSON отказа), второй — отвечает результатом. Свой процесс вместо настоящего Claude
    /// Code: внешние процессы в тестах источник флаки (и денег подписки).
    /// </summary>
    private string FakeCli(string dirName)
    {
        var dir = Path.Combine(_f.Dir, dirName);
        Directory.CreateDirectory(dir);
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(dir, "out1.json"), AuthJson("s-96"), utf8);
        File.WriteAllText(Path.Combine(dir, "out2.json"), DoneJson("s-96"), utf8);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "if exist \"%~dp0prompt1.txt\" goto second\r\n" +
                "echo %* > \"%~dp0args1.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt1.txt\"\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "exit /b 1\r\n" +
                ":second\r\n" +
                "echo %* > \"%~dp0args2.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt2.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "d=\"$(dirname \"$0\")\"\n" +
            "if [ -f \"$d/prompt1.txt\" ]; then echo \"$@\" > \"$d/args2.txt\"; " +
            "cat > \"$d/prompt2.txt\"; cat \"$d/out2.json\"; exit 0;\n" +
            "else echo \"$@\" > \"$d/args1.txt\"; cat > \"$d/prompt1.txt\"; " +
            "cat \"$d/out1.json\"; exit 1; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string AuthJson(string sessionId) =>
        """
        {"type":"result","subtype":"success","is_error":true,"session_id":"SID",
         "terminal_reason":"api_error","api_error_status":null,
         "result":"Failed to authenticate: OAuth session expired and could not be refreshed",
         "usage":{"input_tokens":0,"output_tokens":0}}
        """.Replace("SID", sessionId);

    private static string DoneJson(string sessionId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = sessionId,
            result = "Готово: доделал начатое после входа.",
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    /// <summary>Задание ИИ-исполнителя на поддельном CLI, упёршееся в истёкший вход:
    /// ждём, пока задание закроется, а задача уйдёт в паузу.</summary>
    private async Task<(Job Job, string CliDir, TaskItem Task, Executor Ai)> RunUntilAuthFailureAsync(
        string dirName)
    {
        var cliPath = FakeCli(dirName);
        var ai = CreateAi("клод-" + dirName, $"\"{cliPath}\"");
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

    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 600; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await System.Threading.Tasks.Task.Delay(50);
        }
        return _f.Jobs.Get(jobId)!;
    }
}
