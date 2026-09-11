using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-188: СМЕНА ОРГАНИЗАЦИИ — ОТДЕЛЬНАЯ ФОРМА, А НЕ ВЫПАДАЮЩИЙ СПИСОК.
///
/// Случай заказчика: организация переключалась одним кликом в тулбаре, а переключение это
/// дорогое — уход в другую базу, при котором агенты покидаемой организации останавливаются
/// принудительно, подключения рвутся, локальные серверы моделей выгружаются. Теперь сначала
/// показывают, кого остановят, и только по решению человека останавливают.
///
/// Здесь проверяется то, на чём эта форма стоит:
/// 1) <see cref="JobService.ListActive"/> — что вообще считается незакрытым заданием;
/// 2) <see cref="OrgAgents.Stoppable"/> — список предупреждения = список остановки, и в него
///    НЕ попадают задачи с перенесённым стартом (за ними нет ни задания, ни подключения);
/// 3) <see cref="TeamWorkService.ActiveTeams"/> и <see cref="TeamWorkService.StopAllTeams"/> —
///    отключение агентов и выгрузка локальных серверов моделей, включая команду, поднятую
///    поштучно одним участником (T-129): признака «команда запущена» у неё нет, а модель
///    она держит.
/// </summary>
public sealed class T188Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- обвязка ----------

    private Executor Ai(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Ai }, null);

    private TaskItem NewTask(string title, Executor executor) =>
        _f.Tasks.Create(new TaskItem { Title = title, ExecutorIds = [executor.Id] },
            "текст задания", "", null);

    /// <summary>Задача с заданием агента в заданном состоянии.</summary>
    private (TaskItem Task, Executor Agent, Job Job) NewJob(string title, JobState state)
    {
        var agent = Ai("агент-" + title);
        var task = NewTask(title, agent);
        var job = _f.Jobs.Create(task.Id, agent.Id, "", null);
        _f.Jobs.SetState(job.Id, state, agent.Id);
        return (task, agent, _f.Jobs.Get(job.Id)!);
    }

    private TeamWorkService NewWorkService() =>
        new(_f.Teams, _f.Executors, _f.Projects, _f.Events, _f.Connectors, _f.LocalModels);

    // ---------- 1. что считается незакрытым заданием ----------

    [Fact]
    public void ListActive_Takes_Queued_Running_And_Waiting()
    {
        var queued = NewJob("Очередь", JobState.Queued);
        var running = NewJob("Работа", JobState.Running);
        var waiting = NewJob("Вопрос", JobState.WaitingHuman);
        // закрытые не в счёт: останавливать в них нечего
        NewJob("Готово", JobState.Done);
        NewJob("Ошибка", JobState.Failed);
        NewJob("Снято", JobState.Cancelled);

        var active = _f.Jobs.ListActive().Select(j => j.Id).Order().ToArray();

        Assert.Equal(new[] { queued.Job.Id, running.Job.Id, waiting.Job.Id }.Order().ToArray(), active);
    }

    // ---------- 2. список предупреждения = список остановки ----------

    [Fact]
    public void Warning_Lists_Exactly_What_Will_Be_Stopped()
    {
        var running = NewJob("Работа", JobState.Running);
        _f.Tasks.ChangeStatus(running.Task.Id, TaskStatuses.InProgress, running.Agent.Id);

        var stoppable = OrgAgents.Stoppable(_f.Tasks.AiWork(),
            _f.Jobs.ListActive().Select(j => j.Id));

        var item = Assert.Single(stoppable);
        Assert.Equal(running.Job.Id, item.JobId);
        Assert.Equal(running.Agent.Nick, item.ExecutorNick);
        Assert.Equal(running.Task.DisplayId, item.Task.DisplayId);
    }

    [Fact]
    public void Waiting_Agent_Is_In_The_List_With_Its_Reason()
    {
        var waiting = NewJob("Вопрос", JobState.WaitingHuman);
        _f.Jobs.SetState(waiting.Job.Id, JobState.WaitingHuman, waiting.Agent.Id,
            waitKind: JobWaitKinds.Human);
        _f.Chat.AddQuestion(waiting.Task.Id, waiting.Agent.Id, waiting.Job.Id, "Какой вариант?", []);

        var item = Assert.Single(OrgAgents.Stoppable(_f.Tasks.AiWork(),
            _f.Jobs.ListActive().Select(j => j.Id)));

        // «ждущий» агент — тоже агент: его задание живое, и при смене организации оно снимается
        Assert.Equal(waiting.Job.Id, item.JobId);
        Assert.Equal(AiPauseKinds.Question, item.Pause!.Kind);
    }

    [Fact]
    public void Deferred_Start_Is_Not_Promised_To_Be_Stopped()
    {
        // лимит оборвал работу: задание ЗАКРЫТО, старт перенесён (T-121, T-166). В представлении
        // «в работе у ИИ» такая задача видна, но останавливать в ней нечего — и обещать
        // её остановку нельзя
        var (task, agent, job) = NewJob("Лимит", JobState.Running);
        _f.Jobs.SetState(job.Id, JobState.Failed, agent.Id);
        _f.Tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddHours(2));
        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.Paused, agent.Id);

        Assert.Single(_f.Tasks.AiWork());
        Assert.Empty(OrgAgents.Stoppable(_f.Tasks.AiWork(), _f.Jobs.ListActive().Select(j => j.Id)));
    }

    [Fact]
    public void Nothing_Running_Means_Empty_Warning()
    {
        NewJob("Готово", JobState.Done);

        Assert.Empty(OrgAgents.Stoppable(_f.Tasks.AiWork(), _f.Jobs.ListActive().Select(j => j.Id)));
    }

    // ---------- 3. отключение команд и выгрузка локальных моделей ----------

    [Fact]
    public void Active_Teams_Include_Running_And_Member_Started()
    {
        var work = NewWorkService();
        var idle = _f.Teams.Create(new Team { Name = "Спящая" }, null);
        var runningTeam = _f.Teams.Create(new Team { Name = "Работает" }, null);

        work.Start(runningTeam.Id, null, "me");

        var names = work.ActiveTeams().Select(t => t.Name).ToArray();
        Assert.Equal(["Работает"], names);
        Assert.DoesNotContain(idle.Name, names);
    }

    [Fact]
    public void Stop_All_Teams_Disconnects_Everyone()
    {
        var work = NewWorkService();
        var first = _f.Teams.Create(new Team { Name = "Первая" }, null);
        var second = _f.Teams.Create(new Team { Name = "Вторая" }, null);
        work.Start(first.Id, null, "me");
        work.Start(second.Id, null, "me");

        Assert.Equal(2, work.StopAllTeams(null, "me"));

        Assert.Empty(work.ActiveTeams());
        Assert.False(work.Status(first.Id, "me").IsRunning);
        Assert.False(work.Status(second.Id, "me").IsRunning);
        // повторный вызов ничего не ломает: останавливать уже нечего
        Assert.Equal(0, work.StopAllTeams(null, "me"));
    }

    [Fact]
    public void Team_Started_By_One_Member_Is_Stopped_Too()
    {
        // T-129: участника подняли поштучно — признака «команда запущена» нет, а локальный
        // сервер модели он держит. Без этой ветки модель осталась бы висеть после смены
        // организации, и «выгрузить локальные» было бы неправдой
        var work = NewWorkService();
        var agent = Ai("одиночка");
        var team = _f.Teams.Create(new Team
        {
            Name = "Поштучная",
            Members = [new TeamMember { ExecutorId = agent.Id }],
        }, null);
        work.NoteConnection(agent, error: null);

        Assert.Equal(["Поштучная"], work.ActiveTeams().Select(t => t.Name).ToArray());
        Assert.Equal(1, work.StopAllTeams(null, "me"));

        Assert.Empty(work.ActiveTeams());
        Assert.Equal(ExecutorWorkState.NotConnected,
            work.Status(team.Id, "me").Members.Single().State);
    }

    /// <summary>«И локальных выгрузить»: процесс сервера модели, поднятый командой этой
    /// организации, после общего останова не остаётся жить.</summary>
    [Fact]
    public async Task Stop_All_Teams_Unloads_Local_Model_Servers()
    {
        var work = NewWorkService();
        var team = _f.Teams.Create(new Team { Name = "С моделью" }, null);
        work.Start(team.Id, null, "me");
        // долгоживущий процесс, baseUrl заведомо свободен — сервис решит, что внешнего
        // сервера нет, и запустит свой (приём из T-129)
        var command = OperatingSystem.IsWindows()
            ? "cmd /c ping -n 30 127.0.0.1"
            : "/bin/sh -c \"sleep 30\"";
        await _f.LocalModels.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey(team.Id, "ai-1"));
        Assert.True(_f.LocalModels.ProcessState(command) is { Alive: true });

        Assert.Equal(1, work.StopAllTeams(null, "me"));

        Assert.Null(_f.LocalModels.ProcessState(command));
    }
}
