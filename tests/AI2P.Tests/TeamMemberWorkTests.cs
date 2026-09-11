using System.Net;
using System.Net.Sockets;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-129 (подзадача T-118.2): УЧАСТНИК КОМАНДЫ ПОШТУЧНО.
///
/// 1) Индивидуальный запуск/останов участника: участника добавили в уже работающую команду
///    или он упал по ошибке — поднять его можно одной кнопкой, не трогая остальных. Останов
///    одного участника не гасит команду и не выгружает локальный сервер модели, если его
///    держит кто-то ещё (учёт владения — ПО ИСПОЛНИТЕЛЮ, а не по команде).
/// 2) Активность участника В КОМАНДЕ (<see cref="TeamMember.IsActive"/>) — отдельно от
///    активности исполнителя в справочнике: один и тот же исполнитель бывает активен
///    в команде А и выключен в команде Б. Неактивный в команде не подключается при старте
///    команды и не выбирается автоподбором.
/// 3) Запуск задания с карточки задачи отмечает подключение исполнителя в статусах команды:
///    раньше он висел «не подключен», потому что <c>_aiStates</c> заполнял только Start.
/// </summary>
public sealed class TeamMemberWorkTests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Сервер, который принимает соединение и молчит: проверка подключения и фоновый
    /// вызов модели висят, состояние остаётся детерминированным (приём из todo28/T-121).</summary>
    private readonly TcpListener _hang;
    private readonly List<TcpClient> _hangClients = [];

    public TeamMemberWorkTests()
    {
        _hang = new TcpListener(IPAddress.Loopback, 0);
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
        $$"""{"provider":"openai-compatible","model":"m","baseUrl":"http://127.0.0.1:{{((IPEndPoint)_hang.LocalEndpoint).Port}}","secretRef":""}""";

    /// <summary>ИИ-исполнитель, чей профайл смотрит в молчащий сервер (подключение висит).</summary>
    private Executor CreateAi(string nick)
    {
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel, HangingProfile);
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            ProfilePath = profileRel,
        }, null);
    }

    private Team CreateTeam(string name, params TeamMember[] members) =>
        _f.Teams.Create(new Team { Name = name, Members = [.. members] }, null);

    private TeamWorkService NewWorkService() =>
        new(_f.Teams, _f.Executors, _f.Projects, _f.Events, _f.Connectors, _f.LocalModels);

    private static ExecutorWorkState StateOf(TeamWorkStatusDto status, string executorId) =>
        status.Members.Single(m => m.ExecutorId == executorId).State;

    // ---------- (б) активность участника в команде ----------

    [Fact]
    public void Member_Is_Active_By_Default_And_Flag_Survives_Roundtrip()
    {
        var ai = CreateAi("бот");
        var team = CreateTeam("Команда", new TeamMember { ExecutorId = ai.Id });

        // умолчание — «активен»: старые команды ведут себя как раньше
        Assert.True(_f.Teams.Get(team.Id)!.Members.Single().IsActive);

        team.Members.Single().IsActive = false;
        _f.Teams.Update(team, null);
        Assert.False(_f.Teams.Get(team.Id)!.Members.Single().IsActive);
    }

    [Fact]
    public void Migration_Is_Idempotent_And_Old_Rows_Become_Active()
    {
        // повторный Init на уже накатанной базе (обновление приложения) проходит без ошибок
        _f.Db.Init(_f.Db.NodeId);
        _f.Db.Init(_f.Db.NodeId);
        // v30 — «и задачи в доработке» у заявки на запуск (T-210); v29 — сами заявки (T-196);
        // v31 — тэги задач (T-222); v32 — замена исполнителя (T-221);
        // v33 — ссылка импорта у задачи (T-246); v34 — статус по готовности (T-250);
        // v35 — объекты проекта (T-259); v36 — пер-серверная активность моделей (T-8-S1);
        // v37 — заявки на остановку с другого сервера, stop_requests (T-263);
        // v38 — обучение адаптера LoRA (T-12-S1); v39 — смена дирижёра заявкой (T-21-S1)
        Assert.Equal("46", _f.Db.Meta("schema_version"));

        // строка состава, записанная БЕЗ новой колонки (как её писала прошлая версия),
        // читается как «активен» — умолчание колонки, а не пустое значение
        var ai = CreateAi("бот");
        var team = CreateTeam("Команда");
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null,
                "INSERT INTO team_members (team_id, executor_id, is_lead) VALUES (@t, @e, 0)",
                ("@t", team.Id), ("@e", ai.Id));
        }
        Assert.True(_f.Teams.Get(team.Id)!.Members.Single().IsActive);
    }

    [Fact]
    public void Same_Executor_Is_Active_In_One_Team_And_Inactive_In_Another()
    {
        var ai = CreateAi("бот");
        var teamA = CreateTeam("Команда А", new TeamMember { ExecutorId = ai.Id });
        var teamB = CreateTeam("Команда Б", new TeamMember { ExecutorId = ai.Id, IsActive = false });
        var work = NewWorkService();

        // в команде Б участник выключен — при её запуске он не подключается вовсе
        var statusB = work.Start(teamB.Id, null, "me");
        Assert.Equal(ExecutorWorkState.Inactive, StateOf(statusB, ai.Id));

        // в команде А тот же исполнитель работает: запуск переводит его в «подключается»
        var statusA = work.Start(teamA.Id, null, "me");
        Assert.NotEqual(ExecutorWorkState.Inactive, StateOf(statusA, ai.Id));
        Assert.NotEqual(ExecutorWorkState.NotConnected, StateOf(statusA, ai.Id));

        // состояние подключения общее на все команды (модель одна), но активность — своя:
        // в команде Б он по-прежнему «не активен», а не «подключен»
        Assert.Equal(ExecutorWorkState.Inactive, StateOf(work.Status(teamB.Id, "me"), ai.Id));
    }

    [Fact]
    public void Inactive_Member_Cannot_Be_Started_Individually()
    {
        var ai = CreateAi("бот");
        var team = CreateTeam("Команда", new TeamMember { ExecutorId = ai.Id, IsActive = false });
        var work = NewWorkService();

        var ex = Assert.Throws<InvalidOperationException>(() => work.StartMember(team.Id, ai.Id, null, "me"));
        Assert.Contains("не активен", ex.Message);
        Assert.Equal(ExecutorWorkState.Inactive, StateOf(work.Status(team.Id, "me"), ai.Id));
    }

    [Fact]
    public void Inactive_Member_Is_Not_Picked_By_Auto_Selection()
    {
        var skill = _f.RefData.Skills().First(s => s.Name == "code-write");
        var able = CreateAi("умеет");
        _f.Files.WriteText("caps/able.json", """{"skills":[{"name":"code-write","score":90}]}""");
        able.CapabilitiesPath = "caps/able.json";
        _f.Executors.Update(able, null);

        var team = CreateTeam("Команда", new TeamMember { ExecutorId = able.Id });
        Assert.Equal(able.Id, _f.Picker.Pick(null, team.Id, [skill.Id], PickMode.AiFirst).ExecutorId);

        // выключаем участника именно в команде — исполнитель в справочнике активен
        team.Members.Single().IsActive = false;
        _f.Teams.Update(team, null);

        var picked = _f.Picker.Pick(null, team.Id, [skill.Id], PickMode.AiFirst);
        Assert.Null(picked.ExecutorId);
        Assert.True(_f.Executors.Get(able.Id)!.IsActive);
    }

    // ---------- (а) индивидуальный запуск и останов ----------

    [Fact]
    public void StartMember_Raises_Only_That_Member()
    {
        var first = CreateAi("первый");
        var second = CreateAi("второй");
        var team = CreateTeam("Команда",
            new TeamMember { ExecutorId = first.Id },
            new TeamMember { ExecutorId = second.Id });
        var work = NewWorkService();

        var status = work.StartMember(team.Id, second.Id, null, "me");

        Assert.Equal(ExecutorWorkState.NotConnected, StateOf(status, first.Id));
        Assert.NotEqual(ExecutorWorkState.NotConnected, StateOf(status, second.Id));
    }

    [Fact]
    public void StopMember_Keeps_The_Team_Running_And_Other_Members_Connected()
    {
        var first = CreateAi("первый");
        var second = CreateAi("второй");
        var team = CreateTeam("Команда",
            new TeamMember { ExecutorId = first.Id },
            new TeamMember { ExecutorId = second.Id });
        var work = NewWorkService();
        work.Start(team.Id, null, "me");

        var status = work.StopMember(team.Id, first.Id, null, "me");

        Assert.True(status.IsRunning);   // команда работает дальше
        Assert.Equal(ExecutorWorkState.NotConnected, StateOf(status, first.Id));
        Assert.NotEqual(ExecutorWorkState.NotConnected, StateOf(status, second.Id));
    }

    [Fact]
    public void StartMember_Rejects_Stranger_And_Human()
    {
        var ai = CreateAi("бот");
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var team = CreateTeam("Команда",
            new TeamMember { ExecutorId = ai.Id },
            new TeamMember { ExecutorId = human.Id });
        var stranger = CreateAi("чужой");
        var work = NewWorkService();

        Assert.Contains("не входит в состав",
            Assert.Throws<InvalidOperationException>(
                () => work.StartMember(team.Id, stranger.Id, null, "me")).Message);
        // человек не «подключается» — у него офлайн/онлайн (ТЗ п. 12.4)
        Assert.Contains("ИИ-исполнитель",
            Assert.Throws<InvalidOperationException>(
                () => work.StartMember(team.Id, human.Id, null, "me")).Message);
    }

    /// <summary>Локальный сервер модели бывает общим: останов одного участника его не выгружает,
    /// а выгружает только уход последнего владельца (учёт по исполнителю, T-129).</summary>
    [Fact]
    public async Task Shared_Local_Model_Is_Released_Only_By_The_Last_Member()
    {
        var service = new LocalModelProcessService();
        // долгоживущий процесс: он должен быть жив, пока его держит хоть один владелец
        var command = OperatingSystem.IsWindows()
            ? "cmd /c ping -n 30 127.0.0.1"
            : "/bin/sh -c \"sleep 30\"";
        const string teamId = "team-1";
        // baseUrl заведомо никем не занят — сервис решит, что внешнего сервера нет, и запустит процесс
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey(teamId, "ai-1"));
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey(teamId, "ai-2"));
        Assert.True(service.ProcessState(command) is { Alive: true });

        service.ReleaseMember(teamId, "ai-1");
        Assert.True(service.ProcessState(command) is { Alive: true });   // держит второй

        service.ReleaseMember(teamId, "ai-2");
        Assert.Null(service.ProcessState(command));                      // отпустили все — выгружен
    }

    /// <summary>Останов команды целиком освобождает владения ВСЕХ её участников.</summary>
    [Fact]
    public async Task ReleaseTeam_Frees_All_Members_Of_The_Team()
    {
        var service = new LocalModelProcessService();
        var command = OperatingSystem.IsWindows()
            ? "cmd /c ping -n 30 127.0.0.1"
            : "/bin/sh -c \"sleep 30\"";
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey("team-1", "ai-1"));
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey("team-1", "ai-2"));
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1",
            LocalModelProcessService.OwnerKey("team-2", "ai-3"));

        service.ReleaseTeam("team-1");
        Assert.True(service.ProcessState(command) is { Alive: true });   // держит вторая команда

        service.ReleaseTeam("team-2");
        Assert.Null(service.ProcessState(command));
    }

    // ---------- (в) запуск задания отмечает подключение ----------

    [Fact]
    public async Task Job_Start_Marks_Executor_Connected_In_The_Team()
    {
        var ai = CreateAi("бот");
        var team = CreateTeam("Команда", new TeamMember { ExecutorId = ai.Id });
        var task = _f.Tasks.Create(new TaskItem
        {
            Title = "Задача",
            Status = TaskStatuses.Pending,
            ExecutorIds = [ai.Id],
        }, "сделай", "", null);

        // до запуска задания — «не подключен» (работа команды не запускалась)
        Assert.Equal(ExecutorWorkState.NotConnected, StateOf(_f.TeamWork.Status(team.Id, "me"), ai.Id));

        await _f.Orchestrator.StartTaskAsync(task.Id, null);

        // задание ушло исполнителю — в списке команды он подключён, перезапуск команды не нужен
        Assert.Equal(ExecutorWorkState.Connected, StateOf(_f.TeamWork.Status(team.Id, "me"), ai.Id));
    }

    [Fact]
    public async Task Job_Start_Failure_Shows_Up_As_Connection_Error()
    {
        // ИИ-исполнитель без профайла модели: коннектор не резолвится — это ошибка подключения
        var ai = _f.Executors.Create(new Executor { Nick = "без профайла", Kind = ExecutorKind.Ai }, null);
        var team = CreateTeam("Команда", new TeamMember { ExecutorId = ai.Id });
        var task = _f.Tasks.Create(new TaskItem
        {
            Title = "Задача",
            Status = TaskStatuses.Pending,
            ExecutorIds = [ai.Id],
        }, "сделай", "", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _f.Orchestrator.StartTaskAsync(task.Id, null));

        var member = _f.TeamWork.Status(team.Id, "me").Members.Single(m => m.ExecutorId == ai.Id);
        Assert.Equal(ExecutorWorkState.Error, member.State);
        Assert.Contains("профайла модели", member.ErrorText);
    }

    // --- кнопки участника есть во ВСЕХ списках состава (повторный заход T-129) ---

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

    /// <summary>
    /// Жалоба заказчика при повторном заходе: «иконка остановки и запуска у каждого участника
    /// не появились». В списке команд они были, а во вкладке «команда» карточки проекта —
    /// нет: на состав чаще всего смотрят именно там. Компонент кнопок обязан стоять в обоих
    /// местах; bUnit в проекте нет, поэтому проверяется состав разметки.
    /// </summary>
    [Theory]
    [InlineData("TeamsView.razor", 2)]        // два представления: таблица и иерархия
    [InlineData("ProjectTeamTab.razor", 2)]   // то же самое во вкладке «команда» проекта
    public void Member_Start_Stop_Buttons_Are_Present_In_Every_Member_List(string file, int times)
    {
        var path = Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", file);
        var text = File.ReadAllText(path);
        var count = text.Split("<TeamMemberWorkButtons").Length - 1;
        Assert.True(count >= times,
            $"{file}: кнопок участника {count}, ожидалось не меньше {times} "
            + "(по одному разу на каждое представление состава)");
        Assert.Contains("StartTeamMemberAsync", text, StringComparison.Ordinal);
        Assert.Contains("StopTeamMemberAsync", text, StringComparison.Ordinal);
    }
}
