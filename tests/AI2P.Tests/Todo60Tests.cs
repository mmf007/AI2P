using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-117 (версия 1.60): исправление ошибок обрыва агента и выравнивание версий.
///
/// 1) Версия приложения и версия ТЗ с 1.60 ОДИНАКОВЫ — с каждой версией выпускается ТЗ
///    с тем же номером. 2) Внутренний таймаут коннектора больше не выглядит как «остановлено
///    кнопкой»: задание → failed, задача → error (раньше оба вечно висели «в работе»).
/// 3) Зависшее running-задание (перезапуск приложения) распознаётся (IsStalled) и толкается
///    повторной подачей (ContinueJobAsync + сторож). 4) Разрешающие dir_access-правила
///    транслируются CLI-агенту флагами --add-dir (AllowedFullPathDirs).
/// </summary>
public sealed class Todo60Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
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

    // --- версия приложения = версия ТЗ (T-117) ---

    [Fact]
    public void App_Version_Equals_Tz_Version()
    {
        // с этого задания номера одинаковые: версия 1.60 ↔ doc/AI2P_ТЗ_v1.60.md
        Assert.True(AppInfo.Build >= 60, "с T-117 билд начинается с 60");
        Assert.Equal(AppInfo.Version, $"1.{AppInfo.Build}");
        var tz = Path.Combine(RepoRoot(), "doc", $"AI2P_ТЗ_v{AppInfo.Version}.md");
        Assert.True(File.Exists(tz), $"нет ТЗ текущей версии: {tz} (правило T-117: версия = номер ТЗ)");
    }

    // --- --add-dir из разрешающих правил каталогов (ТЗ гл. 12, T-117) ---

    [Fact]
    public void AllowedFullPathDirs_Takes_Allow_Rules_With_Full_Paths_Only()
    {
        var rules = new List<SecurityRule>
        {
            // полный путь с хвостовым вилдкардом — «каталог и всё внутри», хвост отрезается
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Allow,
                    Pattern = @"C:\mmf\other\*", OpRead = true },
            // полный unix-путь без вилдкарда — как есть
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Allow,
                    Pattern = "/opt/data", OpRead = true },
            // запрет в песочницу не транслируется
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Deny,
                    Pattern = @"C:\secret", OpRead = true },
            // регулярное выражение в конкретный каталог не превращается
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Allow,
                    Pattern = "re:.*tmp", OpRead = true },
            // относительный путь — внутри папки проекта, песочница CLI её и так покрывает
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Allow,
                    Pattern = "sub/dir", OpRead = true },
            // вилдкард в середине пути — конкретного каталога нет
            new() { Target = SecurityTarget.Directory, Permission = SecurityPermission.Allow,
                    Pattern = @"C:\a*b\in", OpRead = true },
            // правила действий к каталогам отношения не имеют
            new() { Target = SecurityTarget.Action, Permission = SecurityPermission.Allow,
                    Pattern = "AI2P.*" },
        };

        var dirs = new SecurityEvaluator(rules, @"C:\proj").AllowedFullPathDirs();

        Assert.Equal([@"C:\mmf\other", "/opt/data"], dirs);
    }

    // --- обрыв агента: таймаут коннектора ≠ остановка кнопкой (T-117) ---

    /// <summary>Коннектор, чей вызов «висит до таймаута»: бросает OperationCanceledException
    /// БЕЗ отмены снаружи — как внутренний CallTimeout ClaudeCliConnector.</summary>
    private sealed class TimeoutingConnector : AiConnectorBase
    {
        public TimeoutingConnector(StorageFixture f)
            : base(f.Jobs, f.Tasks, f.Executors, f.Projects, f.Files, f.Events, f.Secrets,
                f.Chat, f.Actions, f.Security, f.RefData, f.Picker, f.Experience, f.Console,
                localModels: null, f.Teams, () => "ru", "http://localhost:5480/ai2p")
        {
        }

        public override string Kind => "test-timeout";

        protected override Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct) =>
            throw new OperationCanceledException("внутренний таймаут");

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    /// <summary>Коннектор, чей вызов честно ждёт отмены кнопкой «остановить».</summary>
    private sealed class WaitingConnector : AiConnectorBase
    {
        public WaitingConnector(StorageFixture f)
            : base(f.Jobs, f.Tasks, f.Executors, f.Projects, f.Files, f.Events, f.Secrets,
                f.Chat, f.Actions, f.Security, f.RefData, f.Picker, f.Experience, f.Console,
                localModels: null, f.Teams, () => "ru", "http://localhost:5480/ai2p")
        {
        }

        public override string Kind => "test-waiting";

        protected override async Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("недостижимо");
        }

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => Task.FromResult<string?>(null);
    }

    /// <summary>ИИ-исполнитель с профайлом без ключа (secretRef пуст) + задача на него.</summary>
    private (Executor Ai, TaskItem Task) CreateAiTask()
    {
        var model = _f.Models.Create(new AiModel { Name = "Test-T117-" + Guid.NewGuid().ToString("N")[..6] }, null);
        _f.Files.WriteText(model.ProfilePath,
            """{ "provider": "anthropic", "model": "test-model", "secretRef": "", "params": {} }""");
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "агент-" + Guid.NewGuid().ToString("N")[..6], Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            Title = "Задача T-117",
            ExecutorIds = [ai.Id],
        }, "сделай", "", null);
        return (ai, task);
    }

    private async Task WaitJobLeaves(string jobId, JobState state)
    {
        for (var i = 0; i < 300 && _f.Jobs.Get(jobId)!.State == state; i++)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Connector_Timeout_Fails_Job_And_Task_Instead_Of_Hanging()
    {
        var (ai, task) = CreateAiTask();
        var connector = new TimeoutingConnector(_f);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);

        await connector.SubmitJobAsync(job, "текст задания");
        await WaitJobLeaves(job.Id, JobState.Running);
        // статус задачи пишется следом за состоянием задания — дожидаемся и его
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress; i++)
        {
            await Task.Delay(50);
        }

        // раньше таймаут выглядел как «остановлено кнопкой»: состояния никто не менял,
        // задача вечно висела «в работе» (наблюдалось на T-119)
        Assert.Equal(JobState.Failed, _f.Jobs.Get(job.Id)!.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        Assert.Contains(artifacts, a => a.EndsWith("-error.md"));
    }

    [Fact]
    public async Task Stop_Button_Still_Cancels_Without_Turning_Into_Error()
    {
        var (ai, task) = CreateAiTask();
        var connector = new WaitingConnector(_f);
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);

        await connector.SubmitJobAsync(job, "текст задания");
        await connector.CancelAsync(job.Id);
        await WaitJobLeaves(job.Id, JobState.Running);
        await Task.Delay(200); // фоновый вызов дообрабатывает отмену — состояние меняться не должно

        Assert.Equal(JobState.Cancelled, _f.Jobs.Get(job.Id)!.State);
    }

    // --- зависшие задания: IsStalled + «продолжить» (T-117) ---

    /// <summary>Состарить задание: updated_at раньше форы StallGrace (как после перезапуска).</summary>
    private void AgeJob(string jobId)
    {
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE jobs SET updated_at=@t WHERE id=@id",
            ("@t", Sql.ToDb(DateTime.UtcNow - JobOrchestrator.StallGrace - TimeSpan.FromMinutes(1))),
            ("@id", jobId));
    }

    [Fact]
    public void Running_Job_Without_Live_Call_Is_Stalled_After_Grace()
    {
        var (ai, task) = CreateAiTask();
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Jobs.SetState(job.Id, JobState.Running, null);

        // свежее running-задание — фора: SubmitJobAsync ставит running чуть раньше вызова
        Assert.False(_f.Orchestrator.IsStalled(_f.Jobs.Get(job.Id)!));

        AgeJob(job.Id);
        // живого вызова у коннектора нет (в этом процессе его никто не запускал) — зависло
        Assert.True(_f.Orchestrator.IsStalled(_f.Jobs.Get(job.Id)!));
    }

    [Fact]
    public void Human_Jobs_Are_Never_Stalled()
    {
        var human = _f.Executors.Create(new Executor { Nick = "человек", Kind = ExecutorKind.Human }, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Ручная", ExecutorIds = [human.Id] }, "сделай", "", null);
        var job = _f.Jobs.Create(task.Id, human.Id, task.DescriptionPath, null);
        _f.Jobs.SetState(job.Id, JobState.Running, null);
        AgeJob(job.Id);

        // человек работает не процессом приложения — толкать его повторной подачей нечем
        Assert.False(_f.Orchestrator.IsStalled(_f.Jobs.Get(job.Id)!));
    }

    [Fact]
    public async Task Continue_Requires_Running_State()
    {
        var (ai, task) = CreateAiTask();
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null); // queued

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.ContinueJobAsync(job.Id, null));
        Assert.Contains("выполняется", ex.Message);
    }

    [Fact]
    public void ListRunning_Returns_Only_Running_Jobs()
    {
        var (ai, task) = CreateAiTask();
        var queued = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        var running = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Jobs.SetState(running.Id, JobState.Running, null);

        var list = _f.Jobs.ListRunning();

        Assert.Contains(list, j => j.Id == running.Id);
        Assert.DoesNotContain(list, j => j.Id == queued.Id);
    }
}
