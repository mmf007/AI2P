using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ПРАВИЛА УВЕДОМЛЕНИЙ ОРГАНИЗАЦИИ (T-272) — список закладки «Настройки → Уведомления»:
/// CRUD, отбор подходящих правил и отметки об отправке по сроку.
///
/// Сервис НИЧЕГО НЕ ШЛЁТ: он только отвечает на вопрос «какие правила касаются вот этого
/// события вот этой задачи». Само письмо собирает и отправляет <c>NotificationService</c>
/// в слое коннекторов — там, где есть транспорты (почта, дальше мессенджеры).
///
/// Владение строкой — как у расписаний (ТЗ гл. 6, этап 42): правило правится на том сервере,
/// где заведено; на дирижёре сервер не указывается. На ОТПРАВКУ это не влияет — письмо шлёт
/// владелец задачи, иначе одно событие дало бы столько писем, сколько в кластере серверов.
/// </summary>
public sealed class NotificationRuleService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public NotificationRuleService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    public List<NotificationRule> List(bool includeInactive = true)
    {
        using var conn = _db.Open();
        var rules = Sql.Query(conn, null,
            "SELECT * FROM notification_rules WHERE deleted_at IS NULL"
            + (includeInactive ? "" : " AND is_active=1") + " ORDER BY created_at",
            Map);
        foreach (var rule in rules)
        {
            Load(conn, null, rule);
            Decorate(rule);
        }
        return rules;
    }

    public NotificationRule? Get(string id)
    {
        using var conn = _db.Open();
        var rule = Sql.Query(conn, null,
            "SELECT * FROM notification_rules WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();
        if (rule is null)
        {
            return null;
        }
        Load(conn, null, rule);
        return Decorate(rule);
    }

    /// <summary>Правило правится на этом сервере (ТЗ гл. 6, этап 42).</summary>
    public bool CanWrite(NotificationRule rule) => _scope.CanWrite(rule.ServerId);

    private NotificationRule Decorate(NotificationRule rule)
    {
        rule.ServerCode = _scope.CodeOf(rule.ServerId);
        rule.ServerName = _scope.NameOf(rule.ServerId);
        rule.IsReadOnly = !CanWrite(rule);
        return rule;
    }

    public NotificationRule Create(NotificationRule rule, string? actorId)
    {
        var now = DateTime.UtcNow;
        rule.CreatedAt = now;
        rule.UpdatedAt = now;
        // сервер правила — тот, где его заводят; на дирижёре сервер не указывается,
        // и номер остаётся без суффикса (NTF-3, а не NTF-3-S0), как у расписаний
        rule.ServerId = rule.ServerId is { Length: > 0 }
            ? rule.ServerId
            : _scope.IsConductor ? null : _scope.ServerId;
        _scope.EnsureCanWrite(rule.ServerId, Loc.T("msg.notify.1"));
        Validate(rule);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        rule.DisplayId = Database.NextDisplayId(conn, tx, "NTF", _scope.CodeOf(rule.ServerId));
        Sql.Exec(conn, tx, """
            INSERT INTO notification_rules (id, display_id, event_kind, status_code, lead_minutes,
                                            channel, subject, body, is_active, server_id,
                                            created_at, updated_at)
            VALUES (@id, @did, @event, @status, @lead, @channel, @subject, @body, @active,
                    @server, @created, @updated)
            """,
            ("@id", rule.Id), ("@did", rule.DisplayId), ("@event", rule.EventKind),
            ("@status", rule.StatusCode), ("@lead", rule.LeadMinutes),
            ("@channel", rule.Channel), ("@subject", rule.Subject), ("@body", rule.Body),
            ("@active", rule.IsActive ? 1 : 0), ("@server", rule.ServerId),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        SaveLinks(conn, tx, rule);
        Append(conn, tx, EventTypes.NotificationRuleCreated, rule, actorId);
        tx.Commit();
        return Decorate(rule);
    }

    public NotificationRule Update(NotificationRule rule, string? actorId)
    {
        var now = DateTime.UtcNow;
        Validate(rule);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var old = Sql.Query(conn, tx,
                      "SELECT * FROM notification_rules WHERE id=@id AND deleted_at IS NULL",
                      Map, ("@id", rule.Id)).FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.notify.2", rule.Id));
        _scope.EnsureCanWrite(old.ServerId, Loc.T("msg.notify.3") + old.DisplayId);
        rule.DisplayId = old.DisplayId;
        rule.ServerId = old.ServerId;
        Sql.Exec(conn, tx, """
            UPDATE notification_rules SET event_kind=@event, status_code=@status,
                                          lead_minutes=@lead, channel=@channel,
                                          subject=@subject, body=@body, is_active=@active,
                                          updated_at=@updated
            WHERE id=@id
            """,
            ("@event", rule.EventKind), ("@status", rule.StatusCode), ("@lead", rule.LeadMinutes),
            ("@channel", rule.Channel), ("@subject", rule.Subject), ("@body", rule.Body),
            ("@active", rule.IsActive ? 1 : 0), ("@updated", Sql.ToDb(now)), ("@id", rule.Id));
        SaveLinks(conn, tx, rule);
        Append(conn, tx, EventTypes.NotificationRuleUpdated, rule, actorId);
        tx.Commit();
        rule.UpdatedAt = now;
        return Decorate(rule);
    }

    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var rule = Sql.Query(conn, tx,
                       "SELECT * FROM notification_rules WHERE id=@id AND deleted_at IS NULL",
                       Map, ("@id", id)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.notify.2", id));
        _scope.EnsureCanWrite(rule.ServerId, Loc.T("msg.notify.3") + rule.DisplayId);
        Sql.Exec(conn, tx, "UPDATE notification_rules SET deleted_at=@now, updated_at=@now WHERE id=@id",
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        Append(conn, tx, EventTypes.NotificationRuleDeleted, rule, actorId);
        tx.Commit();
    }

    // --- отбор правил под событие ---

    /// <summary>
    /// Активные правила события, подходящие задаче ЭТОГО проекта. Пустой список проектов
    /// у правила означает «все проекты», поэтому строка без ограничений подходит и задаче
    /// без проекта вовсе.
    /// </summary>
    public List<NotificationRule> Match(string eventKind, string? projectId) =>
        List(includeInactive: false)
            .Where(r => r.EventKind == eventKind)
            .Where(r => r.ProjectIds.Count == 0
                        || (projectId is { Length: > 0 } && r.ProjectIds.Contains(projectId)))
            .ToList();

    /// <summary>
    /// Правило касается этого исполнителя: список исполнителей пуст — касается всех,
    /// кого касается само событие (исполнителя задачи либо её ответственного).
    /// </summary>
    public static bool Covers(NotificationRule rule, string executorId) =>
        rule.ExecutorIds.Count == 0 || rule.ExecutorIds.Contains(executorId);

    // --- отметки об отправке (только событие «подходит срок») ---

    /// <summary>
    /// Отметка «правило по этой задаче уже сработало» — сравнивается с меткой <paramref name="mark"/>
    /// (сроком задачи). Метка другая — значит срок сдвинули, и уведомить надо заново.
    /// </summary>
    public bool AlreadySent(string ruleId, string taskId, string mark)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null,
            "SELECT mark FROM notification_marks WHERE rule_id=@r AND task_id=@t",
            ("@r", ruleId), ("@t", taskId)) == mark;
    }

    /// <summary>Запомнить отправку по сроку (см. <see cref="AlreadySent"/>).</summary>
    public void MarkSent(string ruleId, string taskId, string mark)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO notification_marks (rule_id, task_id, mark, sent_at)
            VALUES (@r, @t, @m, @at)
            ON CONFLICT(rule_id, task_id) DO UPDATE SET mark=@m, sent_at=@at
            """,
            ("@r", ruleId), ("@t", taskId), ("@m", mark), ("@at", Sql.ToDb(DateTime.UtcNow)));
    }

    // --- внутреннее ---

    private static void Validate(NotificationRule rule)
    {
        if (!NotificationEvents.IsKnown(rule.EventKind))
        {
            throw new ArgumentException(Loc.T("msg.notify.4", rule.EventKind));
        }
        if (!NotificationChannels.IsKnown(rule.Channel))
        {
            throw new ArgumentException(Loc.T("msg.notify.5", rule.Channel));
        }
        rule.Subject = (rule.Subject ?? "").Trim();
        rule.Body = rule.Body ?? "";
        if (rule.Subject.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.notify.6"));
        }
        if (rule.EventKind == NotificationEvents.Status)
        {
            rule.StatusCode = (rule.StatusCode ?? "").Trim();
            if (rule.StatusCode.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.notify.7"));
            }
            rule.LeadMinutes = 0;
        }
        else
        {
            rule.StatusCode = "";
        }
        if (rule.EventKind == NotificationEvents.Due)
        {
            if (rule.LeadMinutes < 0)
            {
                throw new ArgumentException(Loc.T("msg.notify.8"));
            }
        }
        else
        {
            rule.LeadMinutes = 0;
        }
    }

    /// <summary>Списки исполнителей и проектов правила: снести и записать заново — так же,
    /// как состав исполнителей задачи. Часы журнала изменений у пары «удаление + вставка»
    /// разводит <c>ChangeLog.StampSql</c> (T-3-S1), поэтому строка не теряется у партнёра.</summary>
    private static void SaveLinks(SqliteConnection conn, SqliteTransaction tx, NotificationRule rule)
    {
        Sql.Exec(conn, tx, "DELETE FROM notification_executors WHERE rule_id=@r", ("@r", rule.Id));
        foreach (var executorId in rule.ExecutorIds.Distinct(StringComparer.Ordinal))
        {
            Sql.Exec(conn, tx,
                "INSERT INTO notification_executors (rule_id, executor_id) VALUES (@r, @e)",
                ("@r", rule.Id), ("@e", executorId));
        }
        Sql.Exec(conn, tx, "DELETE FROM notification_projects WHERE rule_id=@r", ("@r", rule.Id));
        foreach (var projectId in rule.ProjectIds.Distinct(StringComparer.Ordinal))
        {
            Sql.Exec(conn, tx,
                "INSERT INTO notification_projects (rule_id, project_id) VALUES (@r, @p)",
                ("@r", rule.Id), ("@p", projectId));
        }
    }

    private static void Load(SqliteConnection conn, SqliteTransaction? tx, NotificationRule rule)
    {
        rule.ExecutorIds = Sql.Query(conn, tx,
            "SELECT executor_id FROM notification_executors WHERE rule_id=@r ORDER BY executor_id",
            r => r.S("executor_id"), ("@r", rule.Id));
        rule.ProjectIds = Sql.Query(conn, tx,
            "SELECT project_id FROM notification_projects WHERE rule_id=@r ORDER BY project_id",
            r => r.S("project_id"), ("@r", rule.Id));
    }

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        NotificationRule rule, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "notification_rule",
            EntityId = rule.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                rule.DisplayId,
                rule.EventKind,
                rule.Channel,
            }),
        });

    private static NotificationRule Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        EventKind = r.S("event_kind"),
        StatusCode = r.S("status_code"),
        LeadMinutes = (int)r.L("lead_minutes"),
        Channel = r.S("channel"),
        Subject = r.S("subject"),
        Body = r.S("body"),
        IsActive = r.B("is_active"),
        ServerId = r.SN("server_id"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
