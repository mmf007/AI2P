using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Запуск задач по расписанию (ТЗ п. 2.12, todo34). Проверка расписаний — при старте
/// приложения: просроченные срабатывания (пока приложение не работало) собираются в список
/// «кого можно запустить» — их запускает либо отклоняет человек (на сервере 24/7 списка
/// не будет). Срабатывания во время работы приложения запускаются автоматически (таймер).
/// При запуске шаблон копируется: базовая дата = момент запуска + смещение, применяется
/// правило поиска исполнителей расписания.
/// </summary>
public sealed class ScheduleRunner : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ScheduleRunner>();

    private readonly TaskService _tasks;
    private readonly ScheduleService _schedules;
    private readonly ExecutorPickService _picker;
    private readonly EventStore _events;
    private readonly object _lock = new();
    private readonly List<ScheduleOverdueDto> _overdue = [];
    private DateTime _startedAtUtc;
    private Timer? _timer;

    /// <remarks>
    /// Отбор «своих» расписаний (ТЗ гл. 6, этап 42) делает <see cref="ScheduleService.Due"/>:
    /// у расписания есть сервер, и срабатывает оно только на нём, а расписания без сервера
    /// ведёт дирижёр. На этапе 41 признак дирижёра проверялся здесь и выключал расписание
    /// целиком — тогда не-дирижёр не выполнял бы и собственных расписаний.
    /// </remarks>
    public ScheduleRunner(TaskService tasks, ScheduleService schedules,
        ExecutorPickService picker, EventStore events)
    {
        _tasks = tasks;
        _schedules = schedules;
        _picker = picker;
        _events = events;
    }

    /// <summary>
    /// Старт: собрать просроченные срабатывания и запустить таймер (раз в минуту).
    /// Ведутся только СВОИ расписания (ТЗ гл. 6, этап 42): у расписания есть сервер, и
    /// срабатывает оно на нём; расписания без сервера ведёт дирижёр. Отбор делает
    /// <see cref="ScheduleService.Due"/> — здесь таймер работает на каждом сервере, иначе
    /// не-дирижёр не выполнял бы собственных расписаний (так было на этапе 41).
    /// </summary>
    public void Start()
    {
        _startedAtUtc = DateTime.UtcNow;
        foreach (var (schedule, missed) in _schedules.Due(_startedAtUtc))
        {
            _overdue.Add(new ScheduleOverdueDto
            {
                ScheduleId = schedule.Id,
                ScheduleDisplayId = schedule.DisplayId,
                TemplateTaskId = schedule.TemplateTaskId,
                MissedCount = missed.Count,
                LastMissedAt = missed[^1],
            });
        }
        if (_overdue.Count > 0)
        {
            Logger.Information("Расписания: просроченных срабатываний {Count} (см. закладку «Расписание»)",
                _overdue.Count);
        }
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ (T-46-S0, выпуск 1.105): расписание умеет запускать не только
    /// копию шаблона, но и ДЕЙСТВИЕ, и пока действие ровно одно. Подставляется свойством,
    /// а не параметром конструктора: без него расписание задач остаётся работоспособным
    /// (та же связь, что у ядра архивации с правилами). null — действия не выполняются.
    /// </summary>
    public AutoArchiveService? AutoArchive { get; set; }

    /// <summary>
    /// АВТООБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208): «проверить» (false) и «проверить и поставить»
    /// (true), в ответе — строка для журнала. Делегатом, а не сервисом: обновляется
    /// установка целиком, а это уровень СЕРВЕРА, про который организация не знает
    /// (там же и config.json с флажками). null — действие честно отказывается строкой
    /// в журнале, расписание задач при этом работает как работало.
    /// </summary>
    public Func<bool, string>? AppUpdate { get; set; }

    /// <summary>Список просроченных срабатываний (по расписанию — последний пропуск и счётчик).</summary>
    public List<ScheduleOverdueDto> Overdue()
    {
        lock (_lock)
        {
            return _overdue.ToList();
        }
    }

    /// <summary>«Запустить» из списка просроченных: копия шаблона сейчас + пометка «обработано».
    /// У расписания-действия (T-46-S0) задача не заводится, и в ответе null.</summary>
    public Core.Entities.TaskItem? RunOverdue(string scheduleId, string? actorId)
    {
        var schedule = _schedules.Get(scheduleId)
                       ?? throw new InvalidOperationException(Loc.T("msg.schedule.2", scheduleId));
        var created = Run(schedule, actorId);
        Resolve(scheduleId);
        return created;
    }

    /// <summary>«Удалить из списка просроченных»: пометить обработанным без запуска.</summary>
    public void DismissOverdue(string scheduleId) => Resolve(scheduleId);

    private void Resolve(string scheduleId)
    {
        _schedules.MarkProcessed(scheduleId, _startedAtUtc);
        lock (_lock)
        {
            _overdue.RemoveAll(o => o.ScheduleId == scheduleId);
        }
    }

    /// <summary>Таймер: срабатывания, наступившие во время работы приложения, запускаются
    /// автоматически; просроченные до старта не трогаются — они в списке у человека.</summary>
    private void Tick()
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var (schedule, missed) in _schedules.Due(now))
            {
                // до старта приложения — только в список просроченных (обрабатывает человек)
                var dueNow = missed.Where(m => m > _startedAtUtc).ToList();
                if (dueNow.Count == 0)
                {
                    continue;
                }
                lock (_lock)
                {
                    if (_overdue.Any(o => o.ScheduleId == schedule.Id))
                    {
                        continue; // у расписания висят просроченные — сначала решение человека
                    }
                }
                try
                {
                    Run(schedule, actorId: null);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    Logger.Warning("Расписание {Schedule}: запуск не удался: {Error}",
                        schedule.DisplayId, ex.Message);
                }
                _schedules.MarkProcessed(schedule.Id, now);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проверка расписаний не удалась: {Error}", ex.Message);
        }
    }

    /// <summary>Срабатывание: копия шаблона (базовая дата = сейчас + смещение) с правилом
    /// подбора исполнителей расписания; событие schedule.triggered. У расписания-ДЕЙСТВИЯ
    /// (T-46-S0) задачи не заводится вовсе — работу делает сама система, и в ответе null.</summary>
    private Core.Entities.TaskItem? Run(Core.Entities.Schedule schedule, string? actorId)
    {
        if (ScheduleService.IsAction(schedule))
        {
            RunAction(schedule, actorId);
            return null;
        }
        var pick = schedule.Pick switch
        {
            "ai_first" => PickMode.AiFirst,
            "human_first" => PickMode.HumanFirst,
            _ => (PickMode?)null,
        };
        var baseDate = DateTime.UtcNow + TimeSpan.FromMinutes(schedule.OffsetMinutes);
        var created = _tasks.InstantiateTemplate(schedule.TemplateTaskId, baseDate, actorId, pick, _picker);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = created.ProjectId,
            TaskId = created.Id,
            EventType = EventTypes.ScheduleTriggered,
            EntityType = "schedule",
            EntityId = schedule.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                schedule.DisplayId,
                created = created.DisplayId,
                baseDate = baseDate.ToString("O"),
            }),
        });
        Logger.Information("Расписание {Schedule}: создана задача {Task}",
            schedule.DisplayId, created.DisplayId);
        return created;
    }

    /// <summary>
    /// СРАБАТЫВАНИЕ РАСПИСАНИЯ-ДЕЙСТВИЯ (T-46-S0, выпуск 1.105). Сегодня действие одно —
    /// автоматическая архивация; сводка о работе уходит в журнал приложения и событием
    /// schedule.triggered в журнал организации.
    ///
    /// «Только на дирижёре» проверяется ДВАЖДЫ и намеренно: расписание без сервера и так
    /// ведёт дирижёр (ТЗ гл. 6, отбор в <c>ScheduleService.Due</c>), но человек вправе
    /// указать расписанию конкретный сервер — и тогда отказ должен быть внятной строкой
    /// в журнале, а не тихим ничегонеделанием. Саму проверку делает
    /// <see cref="AutoArchiveService"/>, здесь только записывается её ответ.
    /// </summary>
    private void RunAction(Core.Entities.Schedule schedule, string? actorId)
    {
        // ОБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208) — второе и третье действие расписания. Задач оно
        // не заводит и базы организации не трогает вовсе: работа идёт над самой установкой
        if (schedule.Action is ScheduleActions.AppCheck or ScheduleActions.AppUpdate)
        {
            var summary = AppUpdate is null
                ? Loc.T("msg.update.9")
                : AppUpdate(schedule.Action == ScheduleActions.AppUpdate);
            Logger.Information("Расписание {Schedule}: {Summary}", schedule.DisplayId, summary);
            _events.Append(new EventRecord
            {
                ActorId = actorId,
                EventType = EventTypes.ScheduleTriggered,
                EntityType = "schedule",
                EntityId = schedule.Id,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    schedule.DisplayId,
                    action = schedule.Action,
                    summary,
                }),
            });
            return;
        }
        if (schedule.Action != ScheduleActions.AutoArchive || AutoArchive is null)
        {
            Logger.Warning("Расписание {Schedule}: действие «{Action}» выполнять нечем",
                schedule.DisplayId, schedule.Action ?? "");
            return;
        }
        var result = AutoArchive.Run(actorId);
        Logger.Information("Расписание {Schedule}: {Summary}", schedule.DisplayId, result.Summary);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ScheduleTriggered,
            EntityType = "schedule",
            EntityId = schedule.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                schedule.DisplayId,
                action = schedule.Action,
                result.Ran,
                result.Moved,
                result.Skipped,
                result.Failed,
                result.Summary,
            }),
        });
    }

    public void Dispose() => _timer?.Dispose();
}
