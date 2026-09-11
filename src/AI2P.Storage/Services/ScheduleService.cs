using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Расписание запуска задач из шаблонов (ТЗ п. 2.12, todo34): CRUD, вычисление
/// срабатываний (однократных и периодических — недельных/месячных/квартальных),
/// просроченные срабатывания, проверка перехлеста исполнителей.
/// Времена периодов ("time":"HH:mm") — локальное время компьютера; моменты в БД — UTC.
/// </summary>
public sealed class ScheduleService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ScheduleService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    public List<Schedule> List(bool includeInactive = true)
    {
        using var conn = _db.Open();
        var schedules = Sql.Query(conn, null,
            "SELECT * FROM schedules WHERE deleted_at IS NULL" +
            (includeInactive ? "" : " AND is_active=1") + " ORDER BY created_at",
            Map);
        foreach (var schedule in schedules)
        {
            Decorate(schedule);
        }
        return schedules;
    }

    /// <summary>
    /// Расписания, которые ведёт ЭТОТ сервер (ТЗ гл. 6, этап 42): свои плюс — у дирижёра —
    /// те, у кого сервер не указан. Только они здесь и срабатывают: иначе одно и то же
    /// расписание из реплики запустилось бы на каждом сервере кластера.
    /// </summary>
    public List<Schedule> ListMine(bool includeInactive = true) =>
        List(includeInactive).Where(CanWrite).ToList();

    public Schedule? Get(string id)
    {
        using var conn = _db.Open();
        var schedule = Sql.Query(conn, null, "SELECT * FROM schedules WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();
        return schedule is null ? null : Decorate(schedule);
    }

    /// <summary>Расписание правится и срабатывает на этом сервере (ТЗ гл. 6, этап 42).</summary>
    public bool CanWrite(Schedule schedule) => _scope.CanWrite(schedule.ServerId);

    /// <summary>Подставить сервер расписания в выдачу наружу (код, имя, «только чтение»).</summary>
    private Schedule Decorate(Schedule schedule)
    {
        schedule.ServerCode = _scope.CodeOf(schedule.ServerId);
        schedule.ServerName = _scope.NameOf(schedule.ServerId);
        schedule.IsReadOnly = !CanWrite(schedule);
        return schedule;
    }

    public Schedule Create(Schedule schedule, string? actorId)
    {
        var now = DateTime.UtcNow;
        schedule.CreatedAt = now;
        schedule.UpdatedAt = now;
        // сервер расписания (ТЗ гл. 6, этап 42): по умолчанию — тот, где его заводят; на
        // дирижёре сервер не указывается, и номер остаётся без суффикса (SCH-3, а не SCH-3-S0)
        schedule.ServerId = schedule.ServerId is { Length: > 0 }
            ? schedule.ServerId
            : _scope.IsConductor ? null : _scope.ServerId;
        _scope.EnsureCanWrite(schedule.ServerId, Loc.T("msg.schedule.1"));
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Validate(conn, tx, schedule);
        schedule.DisplayId = Database.NextDisplayId(conn, tx, "SCH", _scope.CodeOf(schedule.ServerId));
        Sql.Exec(conn, tx, """
            INSERT INTO schedules (id, display_id, project_id, template_task_id, action,
                                   offset_minutes,
                                   pick, kind, start_at, period_json, planned_minutes, is_active,
                                   processed_at, server_id, created_at, updated_at)
            VALUES (@id, @did, @project, @template, @action, @offset, @pick, @kind, @start, @period,
                    @minutes, @active, @processed, @server, @created, @updated)
            """,
            ("@server", schedule.ServerId), ("@action", Action(schedule)),
            ("@id", schedule.Id), ("@did", schedule.DisplayId), ("@project", schedule.ProjectId),
            ("@template", Template(schedule)), ("@offset", schedule.OffsetMinutes),
            ("@pick", schedule.Pick), ("@kind", schedule.Kind),
            ("@start", Sql.ToDbN(schedule.StartAt)), ("@period", schedule.PeriodJson),
            ("@minutes", schedule.PlannedMinutes), ("@active", schedule.IsActive ? 1 : 0),
            ("@processed", Sql.ToDbN(schedule.ProcessedAt)),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        Append(conn, tx, EventTypes.ScheduleCreated, schedule, actorId);
        tx.Commit();
        return Decorate(schedule);
    }

    public Schedule Update(Schedule schedule, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var old = Sql.Query(conn, tx, "SELECT * FROM schedules WHERE id=@id AND deleted_at IS NULL",
                      Map, ("@id", schedule.Id)).FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.schedule.2", schedule.Id));
        // чужое расписание правится на своём сервере (ТЗ гл. 6); сервер меняет его владелец
        _scope.EnsureCanWrite(old.ServerId, Loc.T("msg.schedule.3") + old.DisplayId);
        if (schedule.ServerId != old.ServerId)
        {
            _scope.EnsureCanWrite(schedule.ServerId, Loc.T("msg.schedule.1"));
        }
        schedule.DisplayId = old.DisplayId;
        schedule.ProcessedAt = old.ProcessedAt;
        Validate(conn, tx, schedule);
        Sql.Exec(conn, tx, """
            UPDATE schedules SET project_id=@project, template_task_id=@template, action=@action,
                                 offset_minutes=@offset, pick=@pick,
                                 kind=@kind, start_at=@start, period_json=@period,
                                 planned_minutes=@minutes, is_active=@active, server_id=@server,
                                 updated_at=@updated
            WHERE id=@id
            """,
            ("@server", schedule.ServerId), ("@action", Action(schedule)),
            ("@project", schedule.ProjectId),
            ("@template", Template(schedule)), ("@offset", schedule.OffsetMinutes),
            ("@pick", schedule.Pick), ("@kind", schedule.Kind),
            ("@start", Sql.ToDbN(schedule.StartAt)), ("@period", schedule.PeriodJson),
            ("@minutes", schedule.PlannedMinutes), ("@active", schedule.IsActive ? 1 : 0),
            ("@updated", Sql.ToDb(now)), ("@id", schedule.Id));
        Append(conn, tx, EventTypes.ScheduleUpdated, schedule, actorId);
        tx.Commit();
        schedule.UpdatedAt = now;
        return Decorate(schedule);
    }

    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var schedule = Sql.Query(conn, tx, "SELECT * FROM schedules WHERE id=@id AND deleted_at IS NULL",
                           Map, ("@id", id)).FirstOrDefault()
                       ?? throw new InvalidOperationException(Loc.T("msg.schedule.2", id));
        _scope.EnsureCanWrite(schedule.ServerId, Loc.T("msg.schedule.3") + schedule.DisplayId);
        Sql.Exec(conn, tx, "UPDATE schedules SET deleted_at=@now WHERE id=@id",
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        Append(conn, tx, EventTypes.ScheduleDeleted, schedule, actorId);
        tx.Commit();
    }

    /// <summary>Срабатывания по эту дату (UTC) обработаны — запущены либо отклонены.</summary>
    public void MarkProcessed(string id, DateTime atUtc)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE schedules SET processed_at=@at, updated_at=@at WHERE id=@id",
            ("@at", Sql.ToDb(atUtc)), ("@id", id));
    }

    // --- вычисление срабатываний ---

    /// <summary>Моменты срабатывания расписания в интервале (fromUtc, toUtc] — UTC,
    /// по возрастанию. Периоды считаются в локальном времени компьютера.</summary>
    public static List<DateTime> Occurrences(Schedule schedule, DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<DateTime>();
        if (schedule.Kind == "once")
        {
            if (schedule.StartAt is { } start && start > fromUtc && start <= toUtc)
            {
                result.Add(start);
            }
            return result;
        }
        var (type, days, day, month, time) = ParsePeriod(schedule.PeriodJson);
        if (type.Length == 0)
        {
            return result;
        }
        var fromLocal = fromUtc.ToLocalTime();
        var toLocal = toUtc.ToLocalTime();
        // перебор дней интервала — интервалы проверок короткие (минуты) либо ограничены горизонтом
        for (var date = fromLocal.Date; date <= toLocal.Date; date = date.AddDays(1))
        {
            var matches = type switch
            {
                "weekly" => days.Contains(IsoDay(date.DayOfWeek)),
                "monthly" => date.Day == Math.Min(day, DateTime.DaysInMonth(date.Year, date.Month)),
                // квартальный: месяц внутри квартала (1–3), кварталы с января
                "quarterly" => (date.Month - 1) % 3 + 1 == month
                               && date.Day == Math.Min(day, DateTime.DaysInMonth(date.Year, date.Month)),
                _ => false,
            };
            if (!matches)
            {
                continue;
            }
            var local = DateTime.SpecifyKind(date + time, DateTimeKind.Local);
            var utc = local.ToUniversalTime();
            if (utc > fromUtc && utc <= toUtc)
            {
                result.Add(utc);
            }
        }
        return result;
    }

    /// <summary>ISO-день недели: 1 — понедельник … 7 — воскресенье.</summary>
    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static (string Type, HashSet<int> Days, int Day, int Month, TimeSpan Time) ParsePeriod(
        string periodJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(periodJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var days = new HashSet<int>();
            if (root.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in d.EnumerateArray())
                {
                    days.Add(item.GetInt32());
                }
            }
            var day = root.TryGetProperty("day", out var dd) ? dd.GetInt32() : 1;
            var month = root.TryGetProperty("month", out var m) ? m.GetInt32() : 1;
            var time = root.TryGetProperty("time", out var tm)
                       && TimeSpan.TryParse(tm.GetString(), out var parsed)
                ? parsed
                : TimeSpan.Zero;
            return (type, days, day, month, time);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return ("", [], 1, 1, TimeSpan.Zero);
        }
    }

    /// <summary>Необработанные срабатывания active-расписаний в (processed_at ?? created_at, toUtc].</summary>
    public List<(Schedule Schedule, List<DateTime> Missed)> Due(DateTime toUtc)
    {
        var result = new List<(Schedule, List<DateTime>)>();
        // только СВОИ расписания (ТЗ гл. 6, этап 42): чужие ведёт их собственный сервер
        foreach (var schedule in ListMine(includeInactive: false))
        {
            var from = schedule.ProcessedAt ?? schedule.CreatedAt;
            var missed = Occurrences(schedule, from, toUtc);
            if (missed.Count > 0)
            {
                result.Add((schedule, missed));
            }
        }
        return result;
    }

    // --- валидация ---

    /// <summary>Действие расписания (T-46-S0) — в базу пишется null, а не пустая строка:
    /// «действия нет» и «действие с пустым кодом» это разные вещи.</summary>
    private static string? Action(Schedule schedule) =>
        schedule.Action is { Length: > 0 } action ? action : null;

    /// <summary>Шаблон расписания; у расписания-действия его нет вовсе — в базе NULL
    /// (внешний ключ на задачу пустую строку не принял бы).</summary>
    private static string? Template(Schedule schedule) =>
        IsAction(schedule) || schedule.TemplateTaskId.Length == 0 ? null : schedule.TemplateTaskId;

    /// <summary>Расписание запускает ДЕЙСТВИЕ, а не копию шаблона (T-46-S0).</summary>
    public static bool IsAction(Schedule schedule) => schedule.Action is { Length: > 0 };

    private void Validate(SqliteConnection conn, SqliteTransaction tx, Schedule schedule)
    {
        // РАСПИСАНИЕ-ДЕЙСТВИЕ (T-46-S0): шаблона у него нет, значит нечего проверять на
        // «верхний уровень» и не с кем сверять перехлёст исполнителей — задача не заводится
        // и исполнителя не занимает. Проверяется только сам код действия и время срабатывания
        if (IsAction(schedule))
        {
            if (!ScheduleActions.IsKnown(schedule.Action))
            {
                throw new ArgumentException(Loc.T("msg.schedule.11", schedule.Action ?? ""));
            }
            schedule.TemplateTaskId = "";
            ValidateTiming(schedule);
            return;
        }
        var template = Sql.Query(conn, tx,
                "SELECT is_template, parent_id FROM tasks WHERE id=@id AND deleted_at IS NULL",
                r => (IsTemplate: r.B("is_template"), ParentId: r.SN("parent_id")),
                ("@id", schedule.TemplateTaskId))
            .FirstOrDefault();
        if (template == default)
        {
            throw new ArgumentException(Loc.T("msg.schedule.4"));
        }
        if (!template.IsTemplate || template.ParentId is not null)
        {
            throw new ArgumentException(Loc.T("msg.schedule.5"));
        }
        ValidateTiming(schedule);
        CheckOverlap(conn, tx, schedule);
    }

    /// <summary>Время срабатывания: однократное с датой либо период известного вида.
    /// Общее для обоих видов расписания — и для копии шаблона, и для действия (T-46-S0).</summary>
    private static void ValidateTiming(Schedule schedule)
    {
        if (schedule.Kind == "once")
        {
            if (schedule.StartAt is null)
            {
                throw new ArgumentException(Loc.T("msg.schedule.6"));
            }
        }
        else if (schedule.Kind == "periodic")
        {
            var (type, days, _, _, _) = ParsePeriod(schedule.PeriodJson);
            if (type is not ("weekly" or "monthly" or "quarterly"))
            {
                throw new ArgumentException(Loc.T("msg.schedule.7"));
            }
            if (type == "weekly" && days.Count == 0)
            {
                throw new ArgumentException(Loc.T("msg.schedule.8"));
            }
        }
        else
        {
            throw new ArgumentException(Loc.T("msg.schedule.9"));
        }
    }

    /// <summary>Горизонт проверки перехлеста вперёд от текущего момента (ТЗ п. 2.12).</summary>
    private static readonly TimeSpan OverlapHorizon = TimeSpan.FromDays(62);

    /// <summary>Проверка перехлеста (ТЗ п. 2.12): если в шаблоне указан исполнитель, он не должен
    /// быть занят другим расписанием в то же время с учётом плановой длительности.
    /// Сравниваются срабатывания в горизонте 62 дней (окно = момент + смещение + длительность).</summary>
    private void CheckOverlap(SqliteConnection conn, SqliteTransaction? tx, Schedule schedule)
    {
        var executors = TemplateExecutors(conn, tx, schedule.TemplateTaskId);
        if (executors.Count == 0)
        {
            return;
        }
        var others = Sql.Query(conn, tx,
            "SELECT * FROM schedules WHERE deleted_at IS NULL AND is_active=1 AND id<>@id",
            Map, ("@id", schedule.Id));
        var now = DateTime.UtcNow;
        var windows = Windows(schedule, now, now + OverlapHorizon);
        foreach (var other in others)
        {
            var shared = TemplateExecutors(conn, tx, other.TemplateTaskId).Intersect(executors).ToList();
            if (shared.Count == 0)
            {
                continue;
            }
            var otherWindows = Windows(other, now, now + OverlapHorizon);
            foreach (var (start, end) in windows)
            {
                foreach (var (otherStart, otherEnd) in otherWindows)
                {
                    if (start <= otherEnd && otherStart <= end)
                    {
                        var nick = Sql.Scalar<string>(conn, tx,
                            "SELECT nick FROM executors WHERE id=@id", ("@id", shared[0])) ?? shared[0];
                        throw new ArgumentException(
                            Loc.T("msg.schedule.10", nick, other.DisplayId, otherStart.ToLocalTime()));
                    }
                }
            }
        }
    }

    /// <summary>Окна занятости расписания: срабатывание + смещение, длина — плановая длительность.</summary>
    private static List<(DateTime Start, DateTime End)> Windows(Schedule schedule,
        DateTime fromUtc, DateTime toUtc) =>
        Occurrences(schedule, fromUtc, toUtc)
            .Select(occ =>
            {
                var start = occ + TimeSpan.FromMinutes(schedule.OffsetMinutes);
                return (start, start + TimeSpan.FromMinutes(schedule.PlannedMinutes ?? 0));
            })
            .ToList();

    /// <summary>Исполнители узлов поддерева шаблона (для проверки перехлеста).</summary>
    private static List<string> TemplateExecutors(SqliteConnection conn, SqliteTransaction? tx,
        string templateId) =>
        Sql.Query(conn, tx, """
            WITH RECURSIVE nodes(id) AS (
              SELECT id FROM tasks WHERE id=@id
              UNION ALL
              SELECT t.id FROM tasks t JOIN nodes n ON t.parent_id = n.id
              WHERE t.deleted_at IS NULL
            )
            SELECT DISTINCT executor_id FROM task_executors WHERE task_id IN (SELECT id FROM nodes)
            """, r => r.S("executor_id"), ("@id", templateId));

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        Schedule schedule, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            // у расписания-действия шаблона нет (T-46-S0), и задачей событие не помечается
            TaskId = schedule.TemplateTaskId is { Length: > 0 } ? schedule.TemplateTaskId : null,
            EventType = eventType,
            EntityType = "schedule",
            EntityId = schedule.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schedule.DisplayId,
                schedule.Kind,
                schedule.OffsetMinutes,
            }),
        });

    private static Schedule Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        ProjectId = r.SN("project_id"),
        // шаблон стал необязательным (T-46-S0): у расписания-действия его нет вовсе
        TemplateTaskId = r.SN("template_task_id") ?? "",
        Action = r.Has("action") ? r.SN("action") : null,
        OffsetMinutes = (int)r.L("offset_minutes"),
        Pick = r.SN("pick"),
        Kind = r.S("kind"),
        StartAt = r.DtN("start_at"),
        PeriodJson = r.S("period_json"),
        PlannedMinutes = r.IsDBNull(r.GetOrdinal("planned_minutes")) ? null : (int)r.L("planned_minutes"),
        IsActive = r.B("is_active"),
        ProcessedAt = r.DtN("processed_at"),
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
