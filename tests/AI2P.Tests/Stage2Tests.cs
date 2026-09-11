using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>Секреты (ТЗ гл. 10, todo16): secrets.json + fallback на переменные окружения.</summary>
public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-secrets-" + Guid.NewGuid().ToString("N"));

    public SecretStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Environment.SetEnvironmentVariable("AI2PTEST_API_KEY", null);
    }

    private SecretStore Store(string? json = null)
    {
        var path = Path.Combine(_dir, "secrets.json");
        if (json is not null)
        {
            File.WriteAllText(path, json);
        }
        return new SecretStore(path);
    }

    [Fact]
    public void Resolves_Nested_Path()
    {
        var store = Store("""{ "anthropic": { "apiKey": "sk-nested" } }""");
        Assert.Equal("sk-nested", store.Resolve("anthropic.apiKey"));
    }

    [Fact]
    public void Resolves_Flat_Key()
    {
        var store = Store("""{ "anthropic.apiKey": "sk-flat" }""");
        Assert.Equal("sk-flat", store.Resolve("anthropic.apiKey"));
    }

    [Fact]
    public void Falls_Back_To_Environment_Variable()
    {
        // "ai2ptest.apiKey" → AI2PTEST_API_KEY
        Environment.SetEnvironmentVariable("AI2PTEST_API_KEY", "sk-env");
        var store = Store(); // файла нет
        Assert.Equal("sk-env", store.Resolve("ai2ptest.apiKey"));
    }

    [Fact]
    public void EnvName_Converts_CamelCase_And_Dots()
    {
        Assert.Equal("ANTHROPIC_API_KEY", SecretStore.EnvNameOf("anthropic.apiKey"));
        Assert.Equal("DEEPSEEK_API_KEY", SecretStore.EnvNameOf("deepseek.apiKey"));
    }

    [Fact]
    public void Missing_Secret_Is_Null()
    {
        var store = Store("""{ "anthropic": { "apiKey": "sk" } }""");
        Assert.Null(store.Resolve("nosuch.key"));
        Assert.Null(store.Resolve(""));
    }
}

/// <summary>Профайл модели (ТЗ п. 2.9): чтение настроек подключения коннекторами этапа 2.</summary>
public sealed class ModelProfileTests
{
    [Fact]
    public void Parses_Full_Profile()
    {
        var profile = ModelProfile.Parse("""
            {
              "_seed": 2,
              "provider": "anthropic",
              "model": "claude-fable-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": {
                "maxTokens": 16000,
                "effort": "high",
                "fallbacks": ["claude-opus-5"]
              }
            }
            """);

        Assert.Equal("anthropic", profile.Provider);
        Assert.Equal("claude-fable-5", profile.Model);
        Assert.Equal("anthropic.apiKey", profile.SecretRef);
        Assert.Equal(16000, profile.MaxTokens);
        Assert.Equal("high", profile.Effort);
        Assert.Equal(["claude-opus-5"], profile.Fallbacks);
    }

    [Fact]
    public void Missing_Params_Get_Defaults()
    {
        var profile = ModelProfile.Parse("""{ "provider": "anthropic", "model": "m" }""");
        Assert.Equal(16000, profile.MaxTokens);
        Assert.Null(profile.Effort);
        Assert.Empty(profile.Fallbacks);
    }
}

/// <summary>Статусы задачи — строковые коды справочника состояний (ТЗ v1.14, v1.37).</summary>
public sealed class NewTaskStatusTests
{
    [Fact]
    public void New_Statuses_Are_Db_Strings()
    {
        // «ждёт ответа» переименовано в «паузу» (T-185); старый код нужен только миграции данных
        Assert.Equal("paused", TaskStatuses.Paused);
        Assert.Equal("waiting_reply", TaskStatuses.LegacyWaitingReply);
        Assert.Equal("error", TaskStatuses.Error);
    }

    [Fact]
    public void BuiltIn_Contains_All_Statuses()
    {
        Assert.Equal(9, TaskStatuses.BuiltIn.Length);
        Assert.Contains(TaskStatuses.Draft, TaskStatuses.BuiltIn);
        Assert.Contains(TaskStatuses.Cancelled, TaskStatuses.BuiltIn);
        Assert.True(TaskStatuses.Finished(TaskStatuses.Done));
        Assert.True(TaskStatuses.Finished(TaskStatuses.Cancelled));
        Assert.False(TaskStatuses.Finished(TaskStatuses.InProgress));
    }
}

/// <summary>Запуск работы команды и статусы участников (ТЗ v1.14); seed-файлы моделей (todo16).</summary>
public sealed class TeamWorkAndSeedTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Fills_Profiles_And_Scopes_With_Distribution_Data()
    {
        _f.Models.Seed();

        var claude = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var profile = ModelProfile.Parse(_f.Files.ReadText(claude.ProfilePath));
        Assert.Equal("anthropic", profile.Provider);
        Assert.Equal("claude-fable-5", profile.Model);
        Assert.Equal(["claude-opus-5"], profile.Fallbacks);

        var scope = _f.Files.ReadText(claude.CapabilitiesPath);
        Assert.Contains("code-write", scope);
        Assert.Contains("in_per_1m", scope);
    }

    [Fact]
    public void Seed_Overwrites_Old_Seed_Files_But_Not_Current()
    {
        _f.Models.Seed();
        var claude = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");

        // файл текущей seed-версии: правка пользователя сохраняется
        var edited = _f.Files.ReadText(claude.ProfilePath).Replace("\"high\"", "\"max\"");
        _f.Files.WriteText(claude.ProfilePath, edited);
        _f.Models.Seed();
        Assert.Contains("\"max\"", _f.Files.ReadText(claude.ProfilePath));

        // файл старого формата (без _seed) — обновляется данными дистрибутива
        _f.Files.WriteText(claude.ProfilePath, """{ "provider": "anthropic", "params": {} }""");
        _f.Models.Seed();
        Assert.Contains("claude-fable-5", _f.Files.ReadText(claude.ProfilePath));
    }

    [Fact]
    public void TeamWork_Start_Stop_And_Member_States()
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members = [new TeamMember { ExecutorId = human.Id }, new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var work = new TeamWorkService(_f.Teams, _f.Executors, _f.Projects, _f.Events, _f.Connectors,
            new LocalModelProcessService());

        // до запуска: ИИ «не подключен», локальный человек «онлайн», другой человек был бы «офлайн»
        var status = work.Status(team.Id, human.Id);
        Assert.False(status.IsRunning);
        Assert.Equal(Core.Connectors.ExecutorWorkState.Online,
            status.Members.Single(m => m.ExecutorId == human.Id).State);
        Assert.Equal(Core.Connectors.ExecutorWorkState.NotConnected,
            status.Members.Single(m => m.ExecutorId == ai.Id).State);

        // запуск: ИИ переходит в «подключается», затем (без ключа) — в «ошибка подключения»
        status = work.Start(team.Id, human.Id, human.Id);
        Assert.True(status.IsRunning);
        var aiState = status.Members.Single(m => m.ExecutorId == ai.Id).State;
        Assert.True(aiState is Core.Connectors.ExecutorWorkState.Connecting
            or Core.Connectors.ExecutorWorkState.Error);

        // остановка: снова «не подключен»
        status = work.Stop(team.Id, human.Id, human.Id);
        Assert.False(status.IsRunning);
        Assert.Equal(Core.Connectors.ExecutorWorkState.NotConnected,
            status.Members.Single(m => m.ExecutorId == ai.Id).State);
        Assert.Single(_f.Events.Query(eventType: Core.Events.EventTypes.TeamWorkStarted));
        Assert.Single(_f.Events.Query(eventType: Core.Events.EventTypes.TeamWorkStopped));
    }

    [Fact]
    public void Registry_Resolves_Connectors_By_Provider()
    {
        _f.Models.Seed();
        var claudeModel = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var deepseekModel = _f.Models.List().Single(m => m.Name == "DeepSeek-V4-Pro");
        var claude = _f.Executors.Create(new Executor
        {
            Nick = "клод-р", Kind = ExecutorKind.Ai, ModelId = claudeModel.Id,
        }, null);
        var deepseek = _f.Executors.Create(new Executor
        {
            Nick = "дипсик-р", Kind = ExecutorKind.Ai, ModelId = deepseekModel.Id,
        }, null);

        Assert.Equal("anthropic", _f.Connectors.Resolve(claude).Kind);
        Assert.Equal("openai-compatible", _f.Connectors.Resolve(deepseek).Kind);

        var profile = _f.Connectors.ProfileOf(deepseek);
        Assert.Equal("deepseek-v4-pro", profile.Model);
        Assert.Equal("https://api.deepseek.com", profile.BaseUrl);
    }

    [Fact]
    public async Task Unsupported_Provider_Gives_Clear_Error_On_Start()
    {
        var model = _f.Models.Create(new AiModel { Name = "Test-Unknown" }, null);
        _f.Files.WriteText(model.ProfilePath,
            """{ "provider": "gigachat", "model": "giga", "secretRef": "x.key", "params": {} }""");
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "гига", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "ИИ-задача",
            ExecutorIds = [ai.Id],
        }, "сделай", "", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _f.Orchestrator.StartTaskAsync(task.Id, null));
        Assert.Contains("gigachat", ex.Message);
        Assert.Contains("openai-compatible", ex.Message); // список доступных провайдеров в ошибке
    }

    [Fact]
    public async Task DeepSeek_Task_Without_Key_Fails_To_Error_Status()
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "DeepSeek-V4-Flash");
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "дипсик", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "ИИ-задача",
            ExecutorIds = [ai.Id],
        }, "сделай", "", null);

        // ключа deepseek.apiKey нет: задание уходит в failed, задача — «встал с ошибкой»
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null); // изоляция от окружения машины
        var job = await _f.Orchestrator.StartTaskAsync(task.Id, null);
        // до 15 с: фоновый Task.Run может стартовать с задержкой под нагрузкой параллельных
        // тестов; ждём и статус задачи — он пишется коннектором следом за состоянием задания
        // (иначе гонка: задание уже failed, статус ещё старый)
        for (var i = 0; i < 300 && (_f.Jobs.Get(job.Id)!.State == JobState.Running ||
                                    _f.Tasks.Get(task.Id)!.Status == TaskStatuses.InProgress); i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(JobState.Failed, _f.Jobs.Get(job.Id)!.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        Assert.Contains(artifacts, a => a.EndsWith("-error.md"));
    }
}
