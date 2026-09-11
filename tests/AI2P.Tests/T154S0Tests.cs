using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-154-S0 — исполнитель «авто ПО»: задание выполняет ВНЕШНЯЯ ПРОГРАММА плагина.
///
/// Проверяется то, ради чего коннектор и заведён: программа запускается, её вывод виден в
/// консоли задания и ложится в артефакт задания, а работа кончается результатом и статусом
/// по настройке. Отдельно — флаг «один экземпляр»: второй запуск того же плагина обязан
/// ЖДАТЬ (очередь его пропускает), а не падать с невнятной ошибкой.
/// </summary>
public sealed class T154S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Программа операционной системы, которую заведомо можно запустить.</summary>
    private static string Shell() => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    /// <summary>Аргументы «напечатай метку и выйди с нулём».</summary>
    private static string EchoArgs(string mark) => OperatingSystem.IsWindows()
        ? $"""["/c", "echo", "{mark}"]"""
        : $"""["-c", "echo {mark}"]""";

    /// <summary>Аргументы «работай долго»: на них проверяется занятость плагина.</summary>
    private static string SleepArgs() => OperatingSystem.IsWindows()
        ? """["/c", "ping", "-n", "60", "127.0.0.1"]"""
        : """["-c", "sleep 60"]""";

    /// <summary>Аргументы «молчи»: ни строки в вывод — на них проверяется тайм-аут молчания.</summary>
    private static string SilentArgs() => OperatingSystem.IsWindows()
        ? """["/c", "ping -n 60 127.0.0.1 > nul"]"""
        : """["-c", "sleep 60"]""";

    private static PluginManifest Manifest(string code, string args, bool single,
        int idleSec = 120) =>
        PluginManifest.Parse($$"""
            {
              "code": "{{code}}",
              "kind": "gateway",
              "actions": [
                {
                  "code": "AI2P.Plugins.Test.Run",
                  "tool": "test_run",
                  "role": "convert",
                  "op": "run",
                  "needsSoftware": true,
                  "singleInstance": {{(single ? "true" : "false")}},
                  "idleTimeoutSec": {{idleSec}},
                  "timeoutSec": 300
                }
              ],
              "settings": [ { "key": "mark", "type": "text", "default": "AI2P" } ],
              "convert": { "ops": [ { "op": "run", "args": {{args}} } ] }
            }
            """)!;

    private Executor SoftwareExecutor(string nick, string pluginCode) =>
        _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Software,
            PluginCode = pluginCode,
            PluginOp = "test_run",
        }, null);

    private TaskItem TaskFor(Executor executor, string description)
    {
        var project = _f.Projects.Create("Проект " + executor.Nick, null, null, null);
        return _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Работа программы",
            ExecutorIds = [executor.Id],
        }, description, "", null);
    }

    private async Task<Job> WaitAsync(string jobId, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State != JobState.Running)
            {
                return job;
            }
            await Task.Delay(100);
        }
        return _f.Jobs.Get(jobId)!;
    }

    /// <summary>Дождаться, пока коннектор отпустит задание: состояние задания он ставит
    /// раньше, чем убирает запись о живом процессе, — это разные события.</summary>
    private async Task ReleasedAsync(string jobId)
    {
        for (var i = 0; i < 100 && _f.Software.HasActiveRun(jobId); i++)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public void Manifest_Reads_Single_Instance_And_Both_Timeouts()
    {
        // флаг и оба предела — настройка ОПЕРАЦИИ: тайм-аут молчания отдельно от общего
        var action = Manifest("tool.test", EchoArgs("AI2P"), single: true).Actions[0];

        Assert.True(action.SingleInstance);
        Assert.Equal(120, action.IdleTimeoutSec);
        Assert.Equal(300, action.TimeoutSec);

        var plain = Manifest("tool.test", EchoArgs("AI2P"), single: false).Actions[0];
        Assert.False(plain.SingleInstance);
    }

    [Fact]
    public void Task_Params_Are_Read_From_The_Description()
    {
        // программа промпта не читает — параметры она получает строками описания задачи
        var asked = SoftwareConnector.TaskParams("""
            Обучить адаптер.

            steps: 1200
            mark = метка
            Это обычная фраза с двоеточием: её параметром считать нельзя
            """);

        Assert.Equal("1200", asked["steps"]);
        Assert.Equal("метка", asked["mark"]);
        // ключ из нескольких слов — не параметр, а обычная фраза
        Assert.DoesNotContain(asked.Keys, k => k.Contains(' '));
    }

    [Fact]
    public async Task Program_Runs_And_Its_Output_Goes_To_The_Console_And_To_An_Artifact()
    {
        const string mark = "AI2P-T154S0-OK";
        _f.Gateways.Add(new GatewayPlugin(Manifest("tool.test", EchoArgs(mark), single: false),
            Shell()));
        var executor = SoftwareExecutor("программа", "tool.test");
        var task = TaskFor(executor, "запусти программу");

        var started = await _f.Orchestrator.StartTaskAsync(task.Id, null);
        var job = await WaitAsync(started.Id, TimeSpan.FromSeconds(60));

        Assert.Equal(JobState.Done, job.State);
        await ReleasedAsync(started.Id);
        // статус по настройке задачи — тот же, что у ИИ (T-250): по умолчанию «проверка»
        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.Status);

        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        var log = Assert.Single(artifacts.Where(a => a.EndsWith("-console.log", StringComparison.Ordinal)));
        Assert.Contains(mark, _f.Files.ReadText(log), StringComparison.Ordinal);
        var result = Assert.Single(artifacts.Where(a => a.EndsWith("-result.md", StringComparison.Ordinal)));
        Assert.Contains("tool.test", _f.Files.ReadText(result), StringComparison.Ordinal);
        // консоль задания: тот же вывод человек видит ЖИВЬЁМ, пока программа работает
        Assert.Contains(_f.Console.Read(started.Id, 0).Lines, l => l.Text.Contains(mark, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_Program_Explains_Itself_By_The_Last_Output_Lines()
    {
        var args = OperatingSystem.IsWindows()
            ? """["/c", "echo AI2P-BAD-END && exit 3"]"""
            : """["-c", "echo AI2P-BAD-END; exit 3"]""";
        _f.Gateways.Add(new GatewayPlugin(Manifest("tool.bad", args, single: false), Shell()));
        var executor = SoftwareExecutor("плохая", "tool.bad");
        var task = TaskFor(executor, "запусти программу");

        var started = await _f.Orchestrator.StartTaskAsync(task.Id, null);
        var job = await WaitAsync(started.Id, TimeSpan.FromSeconds(60));

        Assert.Equal(JobState.Failed, job.State);
        await ReleasedAsync(started.Id);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var error = Assert.Single(_f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Where(a => a.EndsWith("-error.md", StringComparison.Ordinal)));
        var text = _f.Files.ReadText(error);
        Assert.Contains("3", text, StringComparison.Ordinal);
        Assert.Contains("AI2P-BAD-END", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Silent_Program_Is_Stopped_By_The_Idle_Timeout()
    {
        // ТАЙМ-АУТ МОЛЧАНИЯ, а не длительности: программа жива, пока печатает. Здесь она
        // молчит с первой секунды — и это единственный случай, когда её надо гасить
        _f.Gateways.Add(new GatewayPlugin(Manifest("tool.mute", SilentArgs(), single: false,
            idleSec: 1), Shell()));
        var executor = SoftwareExecutor("молчунья", "tool.mute");
        var task = TaskFor(executor, "запусти программу");

        var started = await _f.Orchestrator.StartTaskAsync(task.Id, null);
        var job = await WaitAsync(started.Id, TimeSpan.FromSeconds(60));

        Assert.Equal(JobState.Failed, job.State);
        await ReleasedAsync(started.Id);
        Assert.False(_f.Software.HasActiveRun(started.Id));
        var error = Assert.Single(_f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Where(a => a.EndsWith("-error.md", StringComparison.Ordinal)));
        // отказ объясняет себя словами про МОЛЧАНИЕ, а не «программа не уложилась в срок»
        var expected = Loc.T("msg.softwareConnector.9", TimeSpan.FromSeconds(1), "").Split('\n')[0];
        Assert.Contains(expected, _f.Files.ReadText(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Single_Instance_Makes_The_Second_Task_Wait_And_Stop_Kills_The_Tree()
    {
        _f.Gateways.Add(new GatewayPlugin(Manifest("tool.one", SleepArgs(), single: true), Shell()));
        var first = SoftwareExecutor("первая", "tool.one");
        var second = SoftwareExecutor("вторая", "tool.one");
        var busyTask = TaskFor(first, "долгая работа");
        var waiting = TaskFor(second, "вторая работа");

        var started = await _f.Orchestrator.StartTaskAsync(busyTask.Id, null);
        Assert.Equal(JobState.Running, _f.Jobs.Get(started.Id)!.State);

        // ОЧЕРЕДЬ ЖДЁТ: исполнитель второй задачи свободен, а ПРОГРАММА занята — запускать
        // некому, и очередь иерархии такую задачу пропускает (run.Waiting), а не роняет
        Assert.True(_f.Software.SingleInstanceBusy(second));
        Assert.Null(_f.Orchestrator.RunExecutorFor(waiting));

        // остановка задачи гасит процесс (вместе с деревом) и закрывает задание
        await _f.Orchestrator.StopTaskAsync(busyTask.Id, null);
        var stopped = await WaitAsync(started.Id, TimeSpan.FromSeconds(30));
        Assert.NotEqual(JobState.Running, stopped.State);
        await ReleasedAsync(started.Id);
        Assert.False(_f.Software.HasActiveRun(started.Id));
        // программа освободилась — вторая задача больше не ждёт
        Assert.False(_f.Software.SingleInstanceBusy(second));
    }
}
