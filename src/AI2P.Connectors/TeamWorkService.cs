using AI2P.Core;
using System.Collections.Concurrent;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Запуск и остановка работы команды (ТЗ v1.14, этап 2.1): кнопки «запустить» / «остановить»;
/// у каждого члена команды — вычисляемый статус работы:
/// ИИ — не подключен / в процессе подключения / подключен / ошибки подключения (+текст);
/// человек — офлайн / онлайн (онлайн — локальный пользователь, п. 12.4).
/// Состояние держится в памяти процесса (при перезапуске команды «не запущены»).
///
/// T-129: участник поднимается и гасится ПОШТУЧНО (<see cref="StartMember"/> /
/// <see cref="StopMember"/>) — добавленного в работающую команду или упавшего по ошибке
/// незачем ждать всей команде; активность участника считается и по справочнику исполнителей,
/// и по составу ЭТОЙ команды (<see cref="TeamMember.IsActive"/>); запуск задания с карточки
/// задачи отмечает подключение здесь же (<see cref="NoteConnection"/>).
/// </summary>
public sealed class TeamWorkService
{
    /// <summary>Сколько ждать готовности локального сервера модели после запуска процесса:
    /// llama-server грузит десятки ГБ весов с диска — это минуты (ТЗ п. 2.9, launchCommand).</summary>
    private static readonly TimeSpan LocalServerReadyTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LocalServerRetryDelay = TimeSpan.FromSeconds(5);

    private readonly TeamService _teams;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly EventStore _events;
    private readonly ConnectorRegistry _connectors;
    private readonly LocalModelProcessService _localModels;

    /// <summary>Команды, у которых работа запущена.</summary>
    private readonly ConcurrentDictionary<string, byte> _runningTeams = new();

    /// <summary>
    /// Состояние подключения ИИ-исполнителей (общее на все команды — модель одна).
    /// Attempt — id попытки подключения: результат фоновой проверки записывается только если
    /// попытка всё ещё актуальна (не было Stop/повторного Start), иначе он отбрасывается.
    /// </summary>
    private readonly ConcurrentDictionary<string, (ExecutorWorkState State, string? Error, Guid Attempt)> _aiStates = new();

    public TeamWorkService(TeamService teams, ExecutorService executors, ProjectService projects,
        EventStore events, ConnectorRegistry connectors, LocalModelProcessService localModels)
    {
        _teams = teams;
        _executors = executors;
        _projects = projects;
        _events = events;
        _connectors = connectors;
        _localModels = localModels;
    }

    /// <summary>Запустить работу команды: все ИИ-участники переводятся в «подключается»,
    /// подключение проверяется в фоне; статус вычисляется по мере готовности.</summary>
    /// <summary>Технический лог (Serilog, ТЗ п. 6.3): диагностика запуска работы команды.</summary>
    private static ILogger Logger => Log.ForContext("SourceContext", nameof(TeamWorkService));

    public TeamWorkStatusDto Start(string teamId, string? actorId, string localUserId)
    {
        var team = _teams.Get(teamId) ?? throw new InvalidOperationException(Loc.T("msg.teamWork.1", teamId));
        var projectId = ProjectOf(team);
        _runningTeams[teamId] = 1;
        Logger.Information("Запуск работы команды {TeamName} ({TeamId}): участников {MemberCount}",
            team.Name, teamId, team.Members.Count);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            EventType = EventTypes.TeamWorkStarted,
            EntityType = "team",
            EntityId = teamId,
            PayloadJson = JsonSerializer.Serialize(new { team.Name, members = team.Members.Count }),
        });

        foreach (var member in team.Members)
        {
            var executor = _executors.Get(member.ExecutorId);
            // «АГЕНТ» (T-153-S0): запуск команды поднимает сессии моделей. «Авто ПО»
            // подключать нечего — программа запускается самим заданием и живёт ровно
            // столько, сколько считает
            if (executor is null || executor.Kind != ExecutorKind.Ai)
            {
                continue;
            }
            if (InactiveReason(member, executor) is { } reason)
            {
                // исполнителя выключили после включения в команду (todo_bugfix_3) или он выключен
                // именно в этой команде (T-129): не подключаем — в статусе он «не активен»
                Logger.Information("Команда {TeamName}: исполнитель {Nick} не подключается — {Reason}",
                    team.Name, executor.Nick, reason);
                AppendMemberState(projectId, actorId, team, executor,
                    ExecutorWorkState.Inactive, error: null);
                continue;
            }
            ConnectMember(team, projectId, actorId, executor);
        }
        return Status(teamId, localUserId);
    }

    /// <summary>
    /// Запустить ОДНОГО участника команды (T-129): участника добавили в уже работающую команду
    /// либо он упал по ошибке — перезапускать всю команду ради него незачем. Подключение — то же
    /// самое, что в цикле <see cref="Start"/>; признак «команда запущена» не трогается.
    /// </summary>
    public TeamWorkStatusDto StartMember(string teamId, string executorId, string? actorId, string localUserId)
    {
        var team = _teams.Get(teamId) ?? throw new InvalidOperationException(Loc.T("msg.teamWork.1", teamId));
        var member = team.Members.FirstOrDefault(m => m.ExecutorId == executorId)
                     ?? throw new InvalidOperationException(Loc.T("msg.teamWork.2"));
        var executor = _executors.Get(executorId)
                       ?? throw new InvalidOperationException(Loc.T("msg.teamWork.3", executorId));
        // «АГЕНТ» (T-153-S0): кнопка «подключить участника» поднимает сессию модели;
        // ни человек, ни «авто ПО» так не подключаются
        if (executor.Kind != ExecutorKind.Ai)
        {
            // человек не «подключается»: его состояние — офлайн/онлайн (ТЗ п. 12.4)
            throw new InvalidOperationException(Loc.T("msg.teamWork.4"));
        }
        if (InactiveReason(member, executor) is { } reason)
        {
            throw new InvalidOperationException(Loc.T("msg.teamWork.5", executor.Nick, reason));
        }
        Logger.Information("Запуск участника {Nick} команды {TeamName} ({TeamId})",
            executor.Nick, team.Name, teamId);
        ConnectMember(team, ProjectOf(team), actorId, executor);
        return Status(teamId, localUserId);
    }

    /// <summary>
    /// Остановить ОДНОГО участника команды (T-129): он снова «не подключен». Работа команды
    /// при этом НЕ гасится, а локальный сервер модели выгружается, только если его не держит
    /// больше никто — модель бывает общей у нескольких исполнителей (учёт по исполнителю).
    /// </summary>
    public TeamWorkStatusDto StopMember(string teamId, string executorId, string? actorId, string localUserId)
    {
        var team = _teams.Get(teamId) ?? throw new InvalidOperationException(Loc.T("msg.teamWork.1", teamId));
        var executor = _executors.Get(executorId)
                       ?? throw new InvalidOperationException(Loc.T("msg.teamWork.3", executorId));
        if (team.Members.All(m => m.ExecutorId != executorId))
        {
            throw new InvalidOperationException(Loc.T("msg.teamWork.2"));
        }
        Logger.Information("Остановка участника {Nick} команды {TeamName} ({TeamId})",
            executor.Nick, team.Name, teamId);
        _aiStates.TryRemove(executorId, out _);
        _localModels.ReleaseMember(teamId, executorId);
        AppendMemberState(ProjectOf(team), actorId, team, executor, ExecutorWorkState.NotConnected, error: null);
        return Status(teamId, localUserId);
    }

    /// <summary>
    /// Почему участник не подключается; null — подключается. Активность двойная (T-129):
    /// глобальный признак исполнителя (ТЗ п. 2.2) и признак участия в ЭТОЙ команде.
    /// </summary>
    private static string? InactiveReason(TeamMember member, Executor executor) =>
        !executor.IsActive ? Loc.T("msg.teamWork.6")
        : !member.IsActive ? Loc.T("msg.teamWork.7")
        : null;

    /// <summary>
    /// Подключение одного ИИ-участника: «подключается» → фоновая проверка → «подключен»/«ошибка».
    /// Общая часть запуска команды целиком и запуска отдельного участника (T-129).
    /// </summary>
    private void ConnectMember(Team team, string? projectId, string? actorId, Executor executor)
    {
        var attempt = Guid.NewGuid();
        var connecting = (ExecutorWorkState.Connecting, (string?)null, attempt);
        _aiStates[executor.Id] = connecting;
        Logger.Information("Команда {TeamName}: ИИ-исполнитель {Nick} — в процессе подключения",
            team.Name, executor.Nick);
        // запуск пишется в журнал по КАЖДОМУ участнику (todo37): видно, кого вообще
        // пытались поднять, даже если подключение потом зависло
        AppendMemberState(projectId, actorId, team, executor, ExecutorWorkState.Connecting, error: null);
        _ = Task.Run(async () =>
        {
            (ExecutorWorkState State, string? Error, Guid Attempt) result;
            // подробности исключения (тип + стек) — только в журнал: в подсказке статуса
            // они бы не поместились, а для разбора запуска нужны (todo37)
            string? detail = null;
            try
            {
                var connector = _connectors.Resolve(executor);
                // локальный сервер модели (launchCommand в профайле, п. 2.9): поднять процесс
                // и дождаться готовности API — пока грузятся веса, статус «подключается»
                var error = await StartLocalServerAsync(executor, team.Id);
                error ??= await connector.TestConnectionAsync(executor);
                result = error is null
                    ? (ExecutorWorkState.Connected, null, attempt)
                    : (ExecutorWorkState.Error, error, attempt);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Команда {TeamName}: исполнитель {Nick} — исключение при подключении",
                    team.Name, executor.Nick);
                result = (ExecutorWorkState.Error, ex.Message, attempt);
                detail = ex.ToString();
            }
            // записываем только если эта попытка всё ещё актуальна (не было Stop / нового Start)
            var applied = _aiStates.TryUpdate(executor.Id, result, connecting);
            // ошибка подключения — уровнем Error (T-393-S0): её текст человек видит в подсказке
            // статуса, и найти его в журнале он обязан при любом пороге журнала
            Logger.Write(result.Error is null ? Serilog.Events.LogEventLevel.Information
                    : Serilog.Events.LogEventLevel.Error,
                "Команда {TeamName}: исполнитель {Nick} → {State}{Error} (результат учтён: {Applied})",
                team.Name, executor.Nick, result.State,
                result.Error is null ? "" : $" — {result.Error}", applied);
            if (applied)
            {
                // результат подключения — в журнал работ (виден в UI «История работ», ТЗ v1.14);
                // ошибка сюда попадает целиком — из хинта её было не скопировать (todo37)
                AppendMemberState(projectId, actorId, team, executor, result.State, result.Error, detail);
            }
        });
    }

    /// <summary>
    /// Отметить результат подключения исполнителя ВНЕ запуска команды (T-129): задание запущено
    /// кнопкой «выполнить» на карточке задачи — коннектор резолвится напрямую, мимо
    /// <see cref="Start"/>, и участник в списке команды так и висел «не подключен».
    /// <paramref name="error"/> — null при успехе, иначе текст ошибки подключения.
    /// Состояние общее на все команды (модель одна), как и у запуска команды.
    /// </summary>
    public void NoteConnection(Executor executor, string? error)
    {
        // «не человек» (T-153-S0): у «авто ПО» состояние подключения такое же — программа
        // либо нашлась и запустилась, либо нет
        if (executor.Kind == ExecutorKind.Human)
        {
            return;   // у человека состояние вычисляется (офлайн/онлайн), записывать нечего
        }
        var state = error is null ? ExecutorWorkState.Connected : ExecutorWorkState.Error;
        // своя попытка: фоновый результат Start, начатый раньше, эту запись уже не перепишет
        _aiStates[executor.Id] = (state, error, Guid.NewGuid());
        Logger.Write(error is null ? Serilog.Events.LogEventLevel.Information
                : Serilog.Events.LogEventLevel.Error,
            "Запуск задания: исполнитель {Nick} → {State}{Error}",
            executor.Nick, state, error is null ? "" : $" — {error}");
    }

    /// <summary>
    /// Запись о состоянии участника при запуске работы команды в журнал работ (todo37):
    /// событие пишется и на попытку подключения, и на её результат — и ошибка целиком,
    /// вместе с командой запуска локального сервера и адресом API модели (диагностика).
    /// Событие привязано к проекту команды — попадает во вкладку «История» проекта.
    /// </summary>
    private void AppendMemberState(string? projectId, string? actorId, Team team, Executor executor,
        ExecutorWorkState state, string? error, string? detail = null)
    {
        var (launchCommand, baseUrl) = ProfileInfo(executor);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            EventType = EventTypes.TeamMemberState,
            EntityType = "executor",
            EntityId = executor.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                team = team.Name,
                nick = executor.Nick,
                state = state.ToString(),
                error,
                detail,
                baseUrl,
                launchCommand,
            }),
        });
    }

    /// <summary>Адрес API и команда запуска из профайла модели — для журнала (todo37);
    /// нечитаемый профайл не должен ломать запуск команды.</summary>
    private (string LaunchCommand, string BaseUrl) ProfileInfo(Executor executor)
    {
        try
        {
            var profile = _connectors.ProfileOf(executor);
            return (profile.LaunchCommand, profile.BaseUrl);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Исполнитель {Nick}: не удалось прочитать профайл модели", executor.Nick);
            return ("", "");
        }
    }

    /// <summary>Проект команды: явная привязка teams.project_id, иначе проект, у которого
    /// эта команда назначена командой по умолчанию (ТЗ п. 2.7, defaultTeamId).</summary>
    private string? ProjectOf(Team team)
    {
        if (team.ProjectId is { Length: > 0 })
        {
            return team.ProjectId;
        }
        foreach (var project in _projects.List())
        {
            try
            {
                using var doc = JsonDocument.Parse(project.SettingsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("defaultTeamId", out var v)
                    && v.GetString() == team.Id)
                {
                    return project.Id;
                }
            }
            catch (JsonException)
            {
                // повреждённые настройки проекта не должны ломать запуск команды
            }
        }
        return null;
    }

    /// <summary>
    /// Локальный сервер модели исполнителя (ТЗ п. 2.9, launchCommand): запустить процесс,
    /// если он ещё не работает, и дождаться готовности API (модель грузится минуты).
    /// null — готов или запуск не требуется; иначе текст ошибки.
    /// </summary>
    private async Task<string?> StartLocalServerAsync(Executor executor, string teamId)
    {
        var profile = _connectors.ProfileOf(executor);
        if (profile.LaunchCommand.Length == 0)
        {
            return null;
        }
        // владение сервером считается ПО ИСПОЛНИТЕЛЮ (T-129): останов одного участника
        // не должен выгружать модель, которой пользуются остальные
        await _localModels.EnsureStartedAsync(profile.LaunchCommand, profile.BaseUrl,
            LocalModelProcessService.OwnerKey(teamId, executor.Id));

        var connector = _connectors.Resolve(executor);
        var deadline = DateTime.UtcNow + LocalServerReadyTimeout;
        string? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            var state = _localModels.ProcessState(profile.LaunchCommand);
            if (state is { Alive: false })
            {
                // причину падения объясняет вывод самого процесса — раньше он терялся (todo37_2)
                return Loc.T("msg.teamWork.8")
                       + _localModels.ExitReport(profile.LaunchCommand, state.Value.ExitCode)
                       + Loc.T("msg.teamWork.9", profile.LaunchCommand);
            }
            lastError = await connector.TestConnectionAsync(executor);
            if (lastError is null)
            {
                return null;
            }
            Logger.Information("Исполнитель {Nick}: локальный сервер ещё не готов ({Error}), жду…",
                executor.Nick, lastError);
            await Task.Delay(LocalServerRetryDelay);
        }
        return Loc.T("msg.teamWork.10", LocalServerReadyTimeout.TotalMinutes, lastError);
    }

    /// <summary>Остановить работу команды: ИИ-участники — «не подключен»;
    /// запущенные при старте локальные серверы моделей выгружаются (по запомненному pid).</summary>
    public TeamWorkStatusDto Stop(string teamId, string? actorId, string localUserId)
    {
        var team = _teams.Get(teamId) ?? throw new InvalidOperationException(Loc.T("msg.teamWork.1", teamId));
        Logger.Information("Остановка работы команды {TeamName} ({TeamId})", team.Name, teamId);
        _runningTeams.TryRemove(teamId, out _);
        _localModels.ReleaseTeam(teamId);
        foreach (var member in team.Members)
        {
            _aiStates.TryRemove(member.ExecutorId, out _);
        }
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = ProjectOf(team),
            EventType = EventTypes.TeamWorkStopped,
            EntityType = "team",
            EntityId = teamId,
            PayloadJson = JsonSerializer.Serialize(new { team.Name }),
        });
        return Status(teamId, localUserId);
    }

    /// <summary>
    /// КОМАНДЫ С ЖИВЫМИ ПОДКЛЮЧЕНИЯМИ (T-188). Работа бывает запущена и командой целиком
    /// (<see cref="Start"/>), и поштучно одним участником (<see cref="StartMember"/>, T-129) —
    /// во втором случае признака «команда запущена» нет, а локальный сервер модели участник
    /// держит точно так же. Поэтому активной считается и та команда, у которой хоть кто-то
    /// подключён или подключается.
    /// </summary>
    public List<(string Id, string Name)> ActiveTeams()
    {
        var result = new List<(string, string)>();
        foreach (var team in _teams.List())
        {
            var connected = team.Members.Any(m =>
                _aiStates.TryGetValue(m.ExecutorId, out var ai)
                && ai.State is ExecutorWorkState.Connecting or ExecutorWorkState.Connected);
            if (_runningTeams.ContainsKey(team.Id) || connected)
            {
                result.Add((team.Id, team.Name));
            }
        }
        return result;
    }

    /// <summary>
    /// Остановить работу ВСЕХ команд организации (T-188, смена организации): подключения
    /// ИИ-участников снимаются, поднятые ими локальные серверы моделей выгружаются.
    /// Возвращает, сколько команд остановлено. Удалённая на ходу команда пропускается —
    /// смену организации это ронять не должно.
    /// </summary>
    public int StopAllTeams(string? actorId, string localUserId)
    {
        var stopped = 0;
        foreach (var (id, _) in ActiveTeams())
        {
            try
            {
                Stop(id, actorId, localUserId);
                stopped++;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warning(ex, "Команда {TeamId} не остановлена: {Error}", id, ex.Message);
                _runningTeams.TryRemove(id, out _);
            }
        }
        return stopped;
    }

    /// <summary>Текущий статус работы команды и её участников (вычисляемый).</summary>
    public TeamWorkStatusDto Status(string teamId, string localUserId)
    {
        var team = _teams.Get(teamId) ?? throw new InvalidOperationException(Loc.T("msg.teamWork.1", teamId));
        var dto = new TeamWorkStatusDto
        {
            TeamId = teamId,
            IsRunning = _runningTeams.ContainsKey(teamId),
        };
        foreach (var member in team.Members)
        {
            var executor = _executors.Get(member.ExecutorId);
            if (executor is null)
            {
                continue;
            }
            var status = new MemberWorkStatusDto
            {
                ExecutorId = executor.Id,
                Nick = executor.Nick,
                Kind = executor.Kind,
            };
            if (InactiveReason(member, executor) is not null)
            {
                // выключен в справочнике исполнителей (todo_bugfix_3) или в составе именно этой
                // команды (T-129): состояние «не активен». Проверка идёт ПЕРЕД _aiStates —
                // состояние подключения общее на все команды, а активность у каждой своя
                status.State = ExecutorWorkState.Inactive;
            }
            else if (executor.Kind == ExecutorKind.Human)
            {
                // «ЧЕЛОВЕК», а не «не агент» (T-153-S0): «авто ПО» идёт нижней веткой вместе
                // с агентом — состояние подключения у него записывается так же (NoteConnection),
                // до первого запуска это «не подключён».
                // логина пока нет (п. 12.4): онлайн — локальный пользователь этой машины
                status.State = executor.Id == localUserId ? ExecutorWorkState.Online : ExecutorWorkState.Offline;
            }
            else if (_aiStates.TryGetValue(executor.Id, out var ai))
            {
                status.State = ai.State;
                status.ErrorText = ai.Error;
            }
            else
            {
                status.State = ExecutorWorkState.NotConnected;
            }
            dto.Members.Add(status);
        }
        return dto;
    }
}
