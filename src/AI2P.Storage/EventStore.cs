using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage;

/// <summary>
/// Event log — первичный источник истины (ТЗ пп. 3, 6.4.3).
/// Событие пишется в той же транзакции, что и проекция (таблицы сущностей).
/// </summary>
public sealed class EventStore
{
    private readonly Database _db;

    public EventStore(Database db) => _db = db;

    /// <summary>
    /// Фильтр событий по уровню вывода в лог (T-136): пишется ли событие такого типа.
    /// По умолчанию — общий на процесс уровень из настроек (<see cref="EventLog"/>);
    /// отдельным значением пользуются проверки, которым нужен свой уровень, не трогая
    /// общий (тесты идут параллельно).
    /// </summary>
    public Func<string, bool> Allows { get; init; } = EventLog.Allows;

    /// <summary>
    /// Добавить событие внутри открытой транзакции: назначает node_id, следующий seq и время.
    /// Идемпотентность: событие с уже известным id пропускается (слияние журналов, п. 6.1).
    /// </summary>
    public void Append(SqliteConnection conn, SqliteTransaction tx, EventRecord evt)
    {
        // уровень вывода в лог (T-136, Настройки → Основное): объёмные технические события
        // на верхних уровнях в журнал не пишутся. Фильтр стоит здесь, в единственной точке
        // записи, — иначе его пришлось бы повторять в каждом сервисе
        if (!Allows(evt.EventType))
        {
            return;
        }
        var exists = Sql.Scalar<long>(conn, tx, "SELECT COUNT(*) FROM events WHERE id=@id", ("@id", evt.Id));
        if (exists > 0)
        {
            return;
        }

        // payload храним человекочитаемым: \uXXXX-эскейпы кириллицы → буквы (todo_bugfix_1)
        evt.PayloadJson = AI2P.Core.JsonText.Readable(evt.PayloadJson);
        evt.NodeId = _db.NodeId;
        evt.Seq = (Sql.Scalar<long?>(conn, tx,
            "SELECT MAX(seq) FROM events WHERE node_id=@n", ("@n", evt.NodeId)) ?? 0) + 1;
        if (evt.Ts == default)
        {
            evt.Ts = DateTime.UtcNow;
        }

        Sql.Exec(conn, tx, """
            INSERT INTO events (id, node_id, seq, ts, actor_id, project_id, task_id,
                                event_type, entity_type, entity_id, payload_json)
            VALUES (@id, @node, @seq, @ts, @actor, @project, @task, @type, @etype, @eid, @payload)
            """,
            ("@id", evt.Id), ("@node", evt.NodeId), ("@seq", evt.Seq), ("@ts", Sql.ToDb(evt.Ts)),
            ("@actor", evt.ActorId), ("@project", evt.ProjectId), ("@task", evt.TaskId),
            ("@type", evt.EventType), ("@etype", evt.EntityType), ("@eid", evt.EntityId),
            ("@payload", evt.PayloadJson));
    }

    /// <summary>Добавить одиночное событие вне внешней транзакции (журнал запросов/ответов ИИ, этап 2).</summary>
    public void Append(EventRecord evt)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Append(conn, tx, evt);
        tx.Commit();
    }

    /// <summary>Журнал работ: выборка с фильтрами (ТЗ п. 6.3) — задача, исполнитель, период, тип.</summary>
    public List<EventRecord> Query(string? projectId = null, string? taskId = null, string? actorId = null,
        string? eventType = null, DateTime? from = null, DateTime? to = null, int limit = 200, int offset = 0)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();
        void Add(string cond, string name, object? value)
        {
            where.Add(cond);
            args.Add((name, value));
        }

        if (projectId is not null) Add("project_id=@project", "@project", projectId);
        if (taskId is not null) Add("task_id=@task", "@task", taskId);
        if (actorId is not null) Add("actor_id=@actor", "@actor", actorId);
        if (eventType is not null) Add("event_type=@type", "@type", eventType);
        if (from is not null) Add("ts>=@from", "@from", Sql.ToDb(from.Value));
        if (to is not null) Add("ts<=@to", "@to", Sql.ToDb(to.Value));

        var sql = "SELECT * FROM events"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                  + " ORDER BY ts DESC, seq DESC LIMIT @limit OFFSET @offset";
        args.Add(("@limit", limit));
        args.Add(("@offset", offset));

        using var conn = _db.Open();
        return Sql.Query(conn, null, sql, Map, args.ToArray());
    }

    private static EventRecord Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        NodeId = r.S("node_id"),
        Seq = r.L("seq"),
        Ts = r.Dt("ts"),
        ActorId = r.SN("actor_id"),
        ProjectId = r.SN("project_id"),
        TaskId = r.SN("task_id"),
        EventType = r.S("event_type"),
        EntityType = r.SN("entity_type"),
        EntityId = r.SN("entity_id"),
        PayloadJson = r.S("payload_json"),
    };
}
