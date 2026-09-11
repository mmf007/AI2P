using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-124 (версия 1.63): ТАЙМАУТ ОТВЕТА ИСПОЛНИТЕЛЯ вынесен в форму.
///
/// 1) Сколько ждать ответа модели, задаётся у исполнителя: пусто — 30 мин (умолчание системы),
///    0 — без ограничения, число — столько минут. Раньше значение было зашито в коннекторе
///    (Claude CLI — 30 мин, чат-API — 10 мин). 2) Значение доезжает до коннектора отдельно
///    на каждое задание. 3) Вышел таймаут — задание падает с ошибкой и понятной подсказкой,
///    но исполнитель БОЛЬШЕ НЕ помечается занятым «по лимиту»: молчание агента лимитом
///    не является (в жалобе, с которой пришло задание, лимит был израсходован на 37%).
/// </summary>
public sealed class Todo63Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. правило значения ----------

    [Fact]
    public void Empty_Means_Default_Zero_Means_No_Limit()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), AgentTimeout.Default);
        // пусто — умолчание системы
        Assert.Equal(AgentTimeout.Default, AgentTimeout.Of(null));
        // 0 — ждать без ограничения
        Assert.Null(AgentTimeout.Of(0));
        // отрицательное значение бессмысленно — тоже «без ограничения», а не отрицательный срок
        Assert.Null(AgentTimeout.Of(-5));
        // число — ровно столько минут
        Assert.Equal(TimeSpan.FromMinutes(90), AgentTimeout.Of(90));
    }

    [Fact]
    public void Connector_Default_Is_Used_When_Field_Is_Empty()
    {
        // у медиа-моделей своё умолчание — params.timeoutMinutes профайла (ComfyUI)
        var media = TimeSpan.FromMinutes(180);

        Assert.Equal(media, AgentTimeout.Of(null, media));
        // заданное в форме значение сильнее умолчания коннектора
        Assert.Equal(TimeSpan.FromMinutes(45), AgentTimeout.Of(45, media));
        Assert.Null(AgentTimeout.Of(0, media));
    }

    [Fact]
    public void Describe_Reads_Well_In_Errors()
    {
        Assert.Equal("00:30:00", AgentTimeout.Describe(AgentTimeout.Default));
        Assert.Equal("без ограничения", AgentTimeout.Describe(null));
    }

    // ---------- 2. хранение ----------

    private Executor CreateAi(string nick, int? timeoutMinutes)
    {
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel,
            """{"provider":"anthropic","model":"test-model","secretRef":"","params":{}}""");
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            ProfilePath = profileRel,
            ResponseTimeoutMinutes = timeoutMinutes,
        }, null);
    }

    [Fact]
    public void Timeout_Survives_Save_Including_Zero()
    {
        Assert.Equal(60, _f.Executors.Get(CreateAi("час", 60).Id)!.ResponseTimeoutMinutes);
        // ноль — ЗНАЧИМОЕ значение («без ограничения»), а не «не задано»
        Assert.Equal(0, _f.Executors.Get(CreateAi("без лимита", 0).Id)!.ResponseTimeoutMinutes);
        // пусто — умолчание системы, в базе NULL
        Assert.Null(_f.Executors.Get(CreateAi("по умолчанию", null).Id)!.ResponseTimeoutMinutes);
    }

    [Fact]
    public void Timeout_Is_Changed_By_Editing_The_Executor()
    {
        var ai = CreateAi("бот", null);

        ai.ResponseTimeoutMinutes = 120;
        _f.Executors.Update(ai, null);
        Assert.Equal(120, _f.Executors.Get(ai.Id)!.ResponseTimeoutMinutes);

        ai.ResponseTimeoutMinutes = 0;
        _f.Executors.Update(ai, null);
        Assert.Equal(0, _f.Executors.Get(ai.Id)!.ResponseTimeoutMinutes);
    }

    [Fact]
    public void Human_Has_No_Response_Timeout()
    {
        // человека ждут столько, сколько нужно — поле про ИИ и в форме человеку не показывается
        var human = _f.Executors.Create(new Executor
        {
            Nick = "человек", Kind = ExecutorKind.Human, ResponseTimeoutMinutes = 15,
        }, null);

        Assert.Null(_f.Executors.Get(human.Id)!.ResponseTimeoutMinutes);
    }

    [Fact]
    public void Timeout_Travels_Through_The_Api_Json()
    {
        // API отдаёт и принимает сущность исполнителя как есть (GET/POST /api/executors),
        // поэтому поле должно называться в JSON так же, как его пишет и читает UI
        var ai = _f.Executors.Get(CreateAi("апи", 45).Id)!;

        var json = System.Text.Json.JsonSerializer.Serialize(ai, AI2P.Core.Api.Ai2pJson.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<Executor>(json,
            AI2P.Core.Api.Ai2pJson.Options)!;

        Assert.Contains("\"responseTimeoutMinutes\":45", json);
        Assert.Equal(45, back.ResponseTimeoutMinutes);
        // ноль и пусто в JSON тоже различимы — иначе «без ограничения» приезжало бы умолчанием
        Assert.Contains("\"responseTimeoutMinutes\":0", System.Text.Json.JsonSerializer.Serialize(
            _f.Executors.Get(CreateAi("апи-ноль", 0).Id)!, AI2P.Core.Api.Ai2pJson.Options));
        Assert.Contains("\"responseTimeoutMinutes\":null", System.Text.Json.JsonSerializer.Serialize(
            _f.Executors.Get(CreateAi("апи-пусто", null).Id)!, AI2P.Core.Api.Ai2pJson.Options));
    }

    [Fact]
    public void Schema_Has_Timeout_Column()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(executors)", r => r.S("name"));

        Assert.Contains("response_timeout_minutes", columns);
    }

    // ---------- 3. значение доезжает до коннектора ----------

    /// <summary>Коннектор, который запоминает таймаут своего вызова и сразу отвечает.</summary>
    private sealed class CapturingConnector : AiConnectorBase
    {
        public CapturingConnector(StorageFixture f)
            : base(f.Jobs, f.Tasks, f.Executors, f.Projects, f.Files, f.Events, f.Secrets,
                f.Chat, f.Actions, f.Security, f.RefData, f.Picker, f.Experience, f.Console,
                localModels: null, f.Teams, () => "ru", "http://localhost:5480/ai2p")
        {
        }

        public override string Kind => "test-capture";

        /// <summary>Таймаут, с которым коннектора позвали; «значение не пришло» — Missing.</summary>
        public TimeSpan? Seen { get; private set; }
        public bool Called { get; private set; }

        protected override Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
        {
            Seen = tools.CallTimeout;
            Called = true;
            return Task.FromResult(AiCallOutcome.Of(new AiCallResult("готово", 1, 1, profile.Model, "stop")));
        }

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    /// <summary>Коннектор, чей вызов «висит до таймаута»: OperationCanceledException БЕЗ
    /// отмены кнопкой — так выглядит вышедший таймаут ожидания ответа.</summary>
    private sealed class TimedOutConnector : AiConnectorBase
    {
        public TimedOutConnector(StorageFixture f)
            : base(f.Jobs, f.Tasks, f.Executors, f.Projects, f.Files, f.Events, f.Secrets,
                f.Chat, f.Actions, f.Security, f.RefData, f.Picker, f.Experience, f.Console,
                localModels: null, f.Teams, () => "ru", "http://localhost:5480/ai2p")
        {
        }

        public override string Kind => "test-timedout";

        protected override Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct) =>
            throw new OperationCanceledException("вышел таймаут ожидания ответа");

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private TaskItem CreateTask(Executor executor) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = "Задача T-124",
            Status = TaskStatuses.Pending,
            ExecutorIds = [executor.Id],
        }, "сделай", "", null);

    private async Task<Job> RunJob(AiConnectorBase connector, Executor ai, TaskItem task)
    {
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);
        await connector.SubmitJobAsync(job, "текст задания");
        for (var i = 0; i < 300 && _f.Jobs.Get(job.Id)!.State == JobState.Running; i++)
        {
            await Task.Delay(50);
        }
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await Task.Delay(50);
        }
        return _f.Jobs.Get(job.Id)!;
    }

    [Fact]
    public async Task Executor_Timeout_Reaches_The_Connector()
    {
        var ai = CreateAi("бот", 90);
        var connector = new CapturingConnector(_f);

        await RunJob(connector, ai, CreateTask(ai));

        Assert.True(connector.Called);
        Assert.Equal(TimeSpan.FromMinutes(90), connector.Seen);
    }

    [Fact]
    public async Task Zero_Means_Connector_Waits_Without_Limit()
    {
        var ai = CreateAi("долгий бот", 0);
        var connector = new CapturingConnector(_f);

        await RunJob(connector, ai, CreateTask(ai));

        // так ждут «Кандинского»: 5 секунд видео он делает почти час и всё это время молчит
        Assert.True(connector.Called);
        Assert.Null(connector.Seen);
    }

    [Fact]
    public async Task Empty_Field_Gives_Thirty_Minutes()
    {
        var ai = CreateAi("обычный бот", null);
        var connector = new CapturingConnector(_f);

        await RunJob(connector, ai, CreateTask(ai));

        Assert.Equal(AgentTimeout.Default, connector.Seen);
    }

    // ---------- 4. вышедший таймаут — ошибка, а не «занят по лимиту» ----------

    [Fact]
    public async Task Timeout_Fails_Task_And_Does_Not_Mark_Executor_Busy()
    {
        var ai = CreateAi("молчун", 30);
        var task = CreateTask(ai);

        var job = await RunJob(new TimedOutConnector(_f), ai, task);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        // главное отличие T-124: исполнитель НЕ занят и задача НЕ ждёт мнимого сброса лимита
        Assert.Null(_f.Executors.Get(ai.Id)!.BusyUntil);
        Assert.Null(TaskService.LaunchStartAfter(_f.Tasks.Get(task.Id)!.LaunchJson));
        // в артефакте ошибки — что делать: где увеличить таймаут и что значит 0
        var errorFile = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!).Single(a => a.EndsWith("-error.md"));
        var text = _f.Files.ReadText(errorFile);
        Assert.Contains("Таймаут ответа", text);
        Assert.Contains("00:30:00", text);
        Assert.Contains("0 — ждать без ограничения", text);
    }
}
