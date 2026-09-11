using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-121 (версия 1.62): ЛИМИТЫ АГЕНТОВ.
///
/// 1) Свой счётчик по скользящему окну: у ИИ-исполнителя задаются «лимит токенов за окно»
///    и длина окна, расход считается по заданиям (AgentLimitService). Лимиты НЕ указаны —
///    вычисление игнорируется целиком. 2) Мало остатка перед запуском — задание не
///    запускается, старт переносится на сброс окна, предупреждение уходит в чат задачи.
///    3) В промпте разбиения агент видит остаток и делает подзадачи мельче. 4) Отложенный
///    старт хранится в задаче и срабатывает сторожем — компьютер можно выключать.
/// </summary>
public sealed class Todo62Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и молчит: фоновый вызов модели висит,
    /// задание детерминированно остаётся running (приём из todo28).</summary>
    private readonly System.Net.Sockets.TcpListener _hang;
    private readonly List<System.Net.Sockets.TcpClient> _hangClients = [];

    public Todo62Tests()
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

    private string HangingProfile =>
        $$"""{"provider":"openai-compatible","model":"m","baseUrl":"http://127.0.0.1:{{((System.Net.IPEndPoint)_hang.LocalEndpoint).Port}}","secretRef":""}""";

    /// <summary>ИИ-исполнитель с лимитом за окно; лимит null — «лимиты не указаны».</summary>
    private Executor CreateAi(string nick, long? tokenLimit, double? windowHours)
    {
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel, HangingProfile);
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            ProfilePath = profileRel,
            TokenLimit = tokenLimit,
            LimitWindowHours = windowHours,
        }, null);
    }

    private TaskItem CreateTask(Executor executor, string title = "Задача") =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            Status = TaskStatuses.Pending,
            ExecutorIds = [executor.Id],
        }, "сделай", "", null);

    /// <summary>Завершённое задание исполнителя с токенами — расход за окно.</summary>
    private void SpendTokens(Executor executor, long tokens, TimeSpan ago)
    {
        var task = CreateTask(executor, "расход");
        var job = _f.Jobs.Create(task.Id, executor.Id, "", null);
        _f.Jobs.SetState(job.Id, JobState.Done, null, null, 0, tokens, 0, "USD");
        // время задания сдвигается назад — иначе весь расход всегда «только что»
        using var conn = _f.Db.Open();
        var at = Sql.ToDb(DateTime.UtcNow - ago);
        Sql.Exec(conn, null,
            "UPDATE jobs SET started_at=@at, finished_at=@at, created_at=@at WHERE id=@id",
            ("@at", at), ("@id", job.Id));
    }

    // ---------- 1. счётчик по скользящему окну ----------

    [Fact]
    public void Limit_Is_Ignored_When_Not_Set()
    {
        // ни лимита, ни окна — вычисления нет вовсе (главное правило T-121)
        Assert.Null(_f.Executors.LimitStatus(CreateAi("без лимита", null, null)));
        // указано только одно из двух — считать всё равно нечем
        Assert.Null(_f.Executors.LimitStatus(CreateAi("без окна", 1000, null)));
        Assert.Null(_f.Executors.LimitStatus(CreateAi("без лимита токенов", null, 5)));
        // ноль в форме = «не указано»
        Assert.Null(_f.Executors.LimitStatus(CreateAi("нули", 0, 0)));
    }

    [Fact]
    public void Window_Counts_Only_Jobs_Inside_It()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 700, TimeSpan.FromHours(9));  // вне окна — не считается
        SpendTokens(ai, 300, TimeSpan.FromHours(2));
        SpendTokens(ai, 100, TimeSpan.FromMinutes(5));

        var status = _f.Executors.LimitStatus(ai)!;

        Assert.Equal(400, status.Used);
        Assert.Equal(600, status.Remaining);
        Assert.Equal(40, status.Percent, 1);
        // окно освободится через 5 ч после самого раннего задания окна (оно было 2 ч назад)
        Assert.NotNull(status.ResetAt);
        Assert.InRange(status.ResetAt!.Value, DateTime.UtcNow.AddHours(2.9), DateTime.UtcNow.AddHours(3.1));
        Assert.False(status.IsLow);
    }

    [Fact]
    public void Remaining_Below_Threshold_Is_Low()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 810, TimeSpan.FromMinutes(30)); // остаток 190 < 20% лимита

        var status = _f.Executors.LimitStatus(ai)!;

        Assert.True(status.IsLow);
        Assert.Equal(190, status.Remaining);
    }

    [Fact]
    public void Limits_Survive_Save_And_Are_Cleared_For_Humans()
    {
        var ai = CreateAi("бот", 500_000, 5);
        var loaded = _f.Executors.Get(ai.Id)!;
        Assert.Equal(500_000, loaded.TokenLimit);
        Assert.Equal(5, loaded.LimitWindowHours);

        // у человека лимита токенов нет — поля обнуляются на сохранении
        var human = _f.Executors.Create(new Executor
        {
            Nick = "человек", Kind = ExecutorKind.Human, TokenLimit = 100, LimitWindowHours = 5,
        }, null);
        Assert.Null(_f.Executors.Get(human.Id)!.TokenLimit);
        Assert.Null(_f.Executors.Get(human.Id)!.LimitWindowHours);
    }

    [Fact]
    public void List_Shows_Window_Usage()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 250, TimeSpan.FromMinutes(10));

        var listed = _f.Executors.List().Single(e => e.Id == ai.Id);

        Assert.Equal(250, listed.WindowTokens);
        Assert.NotNull(listed.WindowResetAt);
    }

    [Fact]
    public void Schema_Has_Limit_Columns()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(executors)", r => r.S("name"));

        Assert.Contains("token_limit", columns);
        Assert.Contains("limit_window_hours", columns);
    }

    // ---------- 2. предупреждение в чате и перенос старта ----------

    [Fact]
    public async Task Start_Is_Deferred_And_Warned_In_Chat_When_Limit_Low()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 900, TimeSpan.FromHours(1));
        var task = CreateTask(ai);

        var ex = await Assert.ThrowsAsync<AgentLimitLowException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));

        // задание не создано, задача осталась ждать — в состоянии «пауза» (T-185): её уже
        // пробовали запустить и перенесли по лимиту, это не «ожидает», где никто не брал
        Assert.Empty(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(task.Id)!.Status);
        // старт перенесён на сброс окна — примерно 4 ч вперёд (окно 5 ч, расход был час назад)
        var startAfter = TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson);
        Assert.NotNull(startAfter);
        Assert.Equal(ex.StartAfter, startAfter!.Value, TimeSpan.FromSeconds(1));
        Assert.InRange(startAfter.Value, DateTime.UtcNow.AddHours(3.9), DateTime.UtcNow.AddHours(4.1));
        // предупреждение в чате задачи — от имени исполнителя, с числами и вариантами действий
        var message = Assert.Single(_f.Chat.ListByTask(task.Id));
        Assert.Equal(ai.Id, message.FromExecutorId);
        Assert.Contains("Лимит исполнителя «бот»", message.Text);
        Assert.Contains("разбить задачу на подзадачи", message.Text);
        Assert.Contains("Компьютер можно выключать", message.Text);
    }

    [Fact]
    public async Task Repeated_Start_Does_Not_Duplicate_Warning()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 900, TimeSpan.FromHours(1));
        var task = CreateTask(ai);

        await Assert.ThrowsAsync<AgentLimitLowException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        await Assert.ThrowsAsync<AgentLimitLowException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));

        // время переноса не изменилось — второго одинакового сообщения в чате нет
        Assert.Single(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public async Task Force_Start_Ignores_Low_Limit()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 900, TimeSpan.FromHours(1));
        var task = CreateTask(ai);

        var job = await _f.Orchestrator.StartTaskAsync(task.Id, null, force: true);

        Assert.NotNull(job);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(task.Id)!.Status);
        // запущенная задача переноса больше не ждёт
        Assert.Null(TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson));
    }

    [Fact]
    public async Task Start_Without_Limits_Runs_As_Before()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);

        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        Assert.NotEmpty(_f.Jobs.ListByTask(task.Id));
        Assert.Empty(_f.Chat.ListByTask(task.Id)); // предупреждать не о чем
    }

    [Fact]
    public void Manual_Defer_Sets_Start_And_Writes_Chat()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 900, TimeSpan.FromHours(1));
        var task = CreateTask(ai);

        var startAfter = _f.Orchestrator.DeferTaskStart(task.Id);

        Assert.Equal(startAfter, TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson)!.Value,
            TimeSpan.FromSeconds(1));
        Assert.Single(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public void Manual_Defer_Without_Limits_Is_Refused()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);

        var ex = Assert.Throws<InvalidOperationException>(() => _f.Orchestrator.DeferTaskStart(task.Id));

        Assert.Contains("лимиты не указаны", ex.Message);
    }

    // ---------- 3. разбиение на подзадачи ----------

    [Fact]
    public void Split_Prompt_Tells_Agent_About_Remaining_Limit()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 700, TimeSpan.FromMinutes(30));
        var task = CreateTask(ai, "Большая задача");

        var prompt = _f.Orchestrator.BuildSplitRequest(_f.Tasks.Get(task.Id)!, _f.Executors.Get(ai.Id)!);

        Assert.Contains("## Лимит исполнителя", prompt);
        Assert.Contains("300 токенов", prompt);
        Assert.Contains("подзадачи мелкими", prompt);
    }

    [Fact]
    public void Split_Prompt_Says_Nothing_About_Limits_When_They_Are_Not_Set()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai, "Большая задача");

        var prompt = _f.Orchestrator.BuildSplitRequest(_f.Tasks.Get(task.Id)!, _f.Executors.Get(ai.Id)!);

        Assert.DoesNotContain("Лимит исполнителя", prompt);
    }

    // ---------- 4. отложенный старт переживает выключение компьютера ----------

    [Fact]
    public void Deferred_Task_Starts_When_Time_Comes()
    {
        // лимиты сняты (окно освободилось) — сторож просто запускает отложенную задачу
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));

        _f.Orchestrator.StartDeferredOnce();

        Assert.NotEmpty(_f.Jobs.ListByTask(task.Id));
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(task.Id)!.Status);
        Assert.Null(TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson));
    }

    [Fact]
    public void Deferred_Task_Waits_Until_Its_Time()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddHours(1));

        Assert.Empty(_f.Tasks.ListStartAfterDue(DateTime.UtcNow));
        _f.Orchestrator.StartDeferredOnce();

        Assert.Empty(_f.Jobs.ListByTask(task.Id));
        Assert.NotNull(TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson));
    }

    [Fact]
    public void Deferred_Start_Is_Postponed_Again_While_Limit_Is_Still_Low()
    {
        var ai = CreateAi("бот", 1000, 5);
        SpendTokens(ai, 900, TimeSpan.FromMinutes(10));
        var task = CreateTask(ai);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));

        _f.Orchestrator.StartDeferredOnce();

        // лимит не восстановился: задания нет, старт отодвинут дальше
        Assert.Empty(_f.Jobs.ListByTask(task.Id));
        var startAfter = TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson);
        Assert.NotNull(startAfter);
        Assert.True(startAfter!.Value > DateTime.UtcNow);
    }

    [Fact]
    public async Task Deferred_Task_Waits_While_Executor_Is_Busy()
    {
        var ai = CreateAi("бот", null, null);
        var busy = CreateTask(ai, "занял исполнителя");
        await _f.Orchestrator.StartTaskAsync(busy.Id, null); // задание висит (профайл молчит)
        var deferred = CreateTask(ai, "отложенная");
        _f.Tasks.SetStartAfter(deferred.Id, DateTime.UtcNow.AddMinutes(-1));

        _f.Orchestrator.StartDeferredOnce();

        // исполнитель занят другим заданием — отложенная задача ждёт, перенос не снят
        Assert.Empty(_f.Jobs.ListByTask(deferred.Id));
        Assert.NotNull(TaskService.LaunchStartAfter(_f.Tasks.Get(deferred.Id)!.LaunchJson));
    }

    [Fact]
    public void Deferred_List_Skips_Tasks_Already_In_Work()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);

        Assert.Empty(_f.Tasks.ListStartAfterDue(DateTime.UtcNow));
    }

    // ---------- 5. лимит, о котором сообщил сам CLI (повторный пуск T-121) ----------

    /// <summary>Момент отсчёта тестов разбора: 05.08.2026 10:00 UTC = 13:00 в Москве.</summary>
    private static readonly DateTime Now = new(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cli_Limit_Message_With_Clock_And_Zone_Is_Parsed()
    {
        // ровно та форма, которая пришла в задаче: часы, am/pm и зона IANA в скобках
        var detected = ClaudeCliLimit.TryDetect(
            "5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)", Now, out var resetAt);

        Assert.True(detected);
        Assert.Equal(new DateTime(2026, 8, 5, 13, 10, 0, DateTimeKind.Utc), resetAt); // 16:10 МСК
    }

    [Fact]
    public void Cli_Limit_Clock_That_Already_Passed_Means_Tomorrow()
    {
        // 17:00 в Москве, сброс в 16:10 — значит, речь о завтрашнем сбросе, а не о прошлом
        var detected = ClaudeCliLimit.TryDetect("Claude usage limit reached. Your limit will reset " +
                                                "at 4:10pm (Europe/Moscow)", Now.AddHours(4), out var resetAt);

        Assert.True(detected);
        Assert.Equal(new DateTime(2026, 8, 6, 13, 10, 0, DateTimeKind.Utc), resetAt);
    }

    [Fact]
    public void Cli_Limit_Message_With_Unix_Time_Is_Parsed()
    {
        // старая форма CLI: метка времени Unix через вертикальную черту
        var epoch = new DateTimeOffset(Now.AddHours(2)).ToUnixTimeSeconds();

        var detected = ClaudeCliLimit.TryDetect($"Claude AI usage limit reached|{epoch}", Now, out var resetAt);

        Assert.True(detected);
        Assert.Equal(Now.AddHours(2), resetAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Cli_Limit_Message_With_Relative_And_Iso_Time_Is_Parsed()
    {
        Assert.True(ClaudeCliLimit.TryDetect("usage limit reached, resets in 25 minutes", Now, out var inMinutes));
        Assert.Equal(Now.AddMinutes(25), inMinutes!.Value, TimeSpan.FromSeconds(1));

        Assert.True(ClaudeCliLimit.TryDetect("rate limit; resets at 2026-08-05T12:30:00Z", Now, out var iso));
        Assert.Equal(new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc), iso);
    }

    [Fact]
    public void Cli_Limit_Without_Time_Is_Still_Recognized()
    {
        // времени в сообщении нет — лимит распознан, «занят до» поставит вызывающий (час)
        Assert.True(ClaudeCliLimit.TryDetect("Claude AI usage limit reached", Now, out var resetAt));
        Assert.Null(resetAt);
        // мусорное время (сброс «через месяц») в расчёт не берётся
        Assert.True(ClaudeCliLimit.TryDetect("usage limit reached, resets at 2027-01-01T00:00:00Z", Now, out var far));
        Assert.Null(far);
    }

    [Fact]
    public void Ordinary_Errors_Are_Not_Mistaken_For_Limit()
    {
        Assert.False(ClaudeCliLimit.TryDetect("Error: ENOENT no such file or directory", Now, out _));
        Assert.False(ClaudeCliLimit.TryDetect("", Now, out _));
    }

    [Fact]
    public void Long_Answer_About_Limits_Is_Not_A_Limit_Message()
    {
        // отчёт агента про лимиты (как в этом самом задании) не должен обрывать задачу:
        // в «успешном» ответе лимитом считается только короткий текст вместо результата
        var report = "# Отчёт\n\nРазобрал, как CLI сообщает про usage limit reached и resets 4:10pm "
                     + "(Europe/Moscow). " + new string('т', ClaudeCliLimit.ShortAnswerLimit);

        Assert.False(ClaudeCliLimit.TryDetectInAnswer(report, Now, out _));
        Assert.True(ClaudeCliLimit.TryDetectInAnswer("5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)",
            Now, out _));
    }

    [Fact]
    public void Cli_Json_With_Limit_Message_Is_Recognized_As_Limit()
    {
        // так выглядит вывод `claude -p --output-format json`, когда упёрлись в лимит:
        // текст лимита приходит вместо результата, задание помечено ошибкой
        var stdout = """
            {"type":"result","subtype":"error_during_execution","is_error":true,
             "result":"5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)",
             "session_id":"abc","usage":{"input_tokens":10,"output_tokens":0}}
            """;

        var output = ClaudeCliConnector.ParseCliOutput(stdout, "claude-opus-5");

        Assert.True(output.IsError);
        Assert.True(ClaudeCliLimit.TryDetect(output.Text, Now, out var resetAt));
        Assert.Equal(new DateTime(2026, 8, 5, 13, 10, 0, DateTimeKind.Utc), resetAt);
    }

    [Fact]
    public void Cli_Plain_Text_Limit_Without_Json_Is_Recognized_As_Limit()
    {
        // тот же лимит, но CLI ответил кодом 0 и БЕЗ JSON — просто строкой. Разбор такого
        // вывода падает («не вернул JSON-результат»), поэтому лимит проверяется до разбора
        const string stdout = "5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)\n";

        Assert.Throws<InvalidOperationException>(() => ClaudeCliConnector.ParseCliOutput(stdout, "claude-opus-5"));
        Assert.True(ClaudeCliLimit.TryDetectInAnswer(stdout, Now, out var resetAt));
        Assert.Equal(new DateTime(2026, 8, 5, 13, 10, 0, DateTimeKind.Utc), resetAt);
    }

    /// <summary>Коннектор, чей вызов упирается в лимит провайдера (как Claude CLI, сообщивший
    /// «limit reached ∙ resets …»).</summary>
    private sealed class LimitedConnector : AiConnectorBase
    {
        private readonly DateTime? _retryAt;

        public LimitedConnector(StorageFixture f, DateTime? retryAt)
            : base(f.Jobs, f.Tasks, f.Executors, f.Projects, f.Files, f.Events, f.Secrets,
                f.Chat, f.Actions, f.Security, f.RefData, f.Picker, f.Experience, f.Console,
                localModels: null, f.Teams, () => "ru", "http://localhost:5480/ai2p") => _retryAt = retryAt;

        public override string Kind => "test-limit";

        protected override Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct) =>
            throw new ProviderLimitException("Исчерпан лимит подписки Claude Code", _retryAt);

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private async Task WaitJobLeaves(string jobId, JobState state)
    {
        for (var i = 0; i < 300 && _f.Jobs.Get(jobId)!.State == state; i++)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Limit_In_The_Middle_Makes_Task_Wait_Instead_Of_Error()
    {
        var ai = CreateAi("бот", null, null); // лимиты не указаны — вычисление игнорируется
        var task = CreateTask(ai);
        var until = DateTime.UtcNow.AddHours(3);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);

        await new LimitedConnector(_f, until).SubmitJobAsync(job, "текст задания");
        await WaitJobLeaves(job.Id, JobState.Running);
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await Task.Delay(50);
        }

        // задание — с ошибкой (в артефакте видно почему), а вот ЗАДАЧА не «встала с ошибкой»,
        // а ждёт сброса лимита: раньше здесь светилось «встал с ошибкой» без «ожидает до».
        // Состояние ожидания — «пауза» (T-185), а не «ожидает»
        Assert.Equal(JobState.Failed, _f.Jobs.Get(job.Id)!.State);
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(task.Id)!.Status);
        var startAfter = TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson);
        Assert.Equal(until, startAfter!.Value, TimeSpan.FromSeconds(1));
        // исполнитель занят до того же момента — автоподбор его до этого не назначит
        Assert.Equal(until, _f.Executors.Get(ai.Id)!.BusyUntil!.Value, TimeSpan.FromSeconds(1));
        // человеку в чат — что произошло, до какого времени ждём и что можно сделать
        var message = Assert.Single(_f.Chat.ListByTask(task.Id));
        Assert.Equal(ai.Id, message.FromExecutorId);
        Assert.Contains("Лимит исполнителя «бот» исчерпан", message.Text);
        Assert.Contains("Компьютер можно выключать", message.Text);
    }

    [Fact]
    public async Task Limit_Without_Reset_Time_Waits_An_Hour()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);

        await new LimitedConnector(_f, retryAt: null).SubmitJobAsync(job, "текст задания");
        await WaitJobLeaves(job.Id, JobState.Running);
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await Task.Delay(50);
        }

        var startAfter = TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson);
        Assert.NotNull(startAfter);
        Assert.InRange(startAfter!.Value, DateTime.UtcNow.AddMinutes(55), DateTime.UtcNow.AddMinutes(65));
    }

    [Fact]
    public async Task Waiting_Subtask_Is_Not_Restarted_By_Auto_Start()
    {
        // подзадача ждёт сброса лимита; очередь авторазбиения не должна поднимать её раньше
        // срока — иначе она тут же упрётся в тот же лимит
        var ai = CreateAi("бот", null, null);
        var parent = _f.Tasks.Create(new TaskItem
        {
            Title = "Родитель", ExecutorIds = [ai.Id],
            LaunchJson = """{"mode":"manual","autoSplit":true}""",
        }, "т", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ParentId = parent.Id, Title = "ждёт лимита", Status = TaskStatuses.Pending,
            ExecutorIds = [ai.Id],
        }, "т", "", null);
        _f.Tasks.SetStartAfter(child.Id, DateTime.UtcNow.AddHours(2));

        await _f.Orchestrator.ProcessSplitParentAsync(parent.Id, null);

        Assert.Empty(_f.Jobs.ListByTask(child.Id));
        Assert.NotNull(TaskService.LaunchStartAfter(_f.Tasks.Get(child.Id)!.LaunchJson));
        // родитель тоже ждёт: незавершённая подзадача не даёт запустить финальный анализ
        Assert.Empty(_f.Jobs.ListByTask(parent.Id));
    }

    [Fact]
    public async Task Task_Stopped_By_Limit_Restarts_Itself_When_It_Is_Time()
    {
        var ai = CreateAi("бот", null, null);
        var task = CreateTask(ai);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);
        await new LimitedConnector(_f, DateTime.UtcNow.AddHours(3)).SubmitJobAsync(job, "текст задания");
        await WaitJobLeaves(job.Id, JobState.Running);
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await Task.Delay(50);
        }

        // до срока сторож задачу не трогает
        _f.Orchestrator.StartDeferredOnce();
        Assert.Single(_f.Jobs.ListByTask(task.Id));

        // срок наступил — задача запускается сама, переноса больше нет
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(-1));
        _f.Orchestrator.StartDeferredOnce();

        Assert.Equal(2, _f.Jobs.ListByTask(task.Id).Count);
        Assert.Equal(TaskStatuses.InProgress, _f.Tasks.Get(task.Id)!.Status);
        Assert.Null(TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson));
    }
}
