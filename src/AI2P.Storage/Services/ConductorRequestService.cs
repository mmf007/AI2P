using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ЗАЯВКИ НА СМЕНУ ДИРИЖЁРА ОРГАНИЗАЦИИ (T-21-S1).
///
/// Дирижёр обязателен и всегда ровно один (ТЗ гл. 6): он выдаёт коды серверам, ведёт
/// справочники, запускает расписания и с ним реплицируются все остальные. Переставить этот
/// признак «просто флажком» у себя нельзя — вторая сторона узнала бы о потере дирижёрства
/// постфактум, а при расхождении версий строки обе стороны считали бы дирижёром себя.
/// Поэтому передача сделана ОТДЕЛЬНОЙ ДВУСТОРОННЕЙ операцией, устроенной как первое
/// подключение сервера: заявка → решение человека на другой стороне.
///
/// Заявка едет второй стороне обычной репликацией (таблица в БД организации), решение
/// возвращается тем же путём. Отдельного протокола нет и не нужно.
///
/// Подать её вправе только ДВЕ стороны — назначаемый сервер и сегодняшний дирижёр. Третий
/// сервер кластера в чужую передачу не вмешивается: он не знает ни того, готов ли назначаемый
/// брать на себя дирижёрство, ни того, почему сегодняшний дирижёр его отдаёт.
///
/// АВАРИЙНЫЙ СЛУЧАЙ (ТЗ гл. 6, «на случай физической потери сервера дирижёра»). Если заявку
/// подал САМ назначаемый сервер, у неё идёт обратный отсчёт <see cref="UnilateralAfter"/>.
/// По его истечении назначаемый сервер вправе принять свою заявку В ОДНОСТОРОННЕМ ПОРЯДКЕ —
/// иначе организация с потерянным дирижёром осталась бы без дирижёра навсегда. У заявки,
/// поданной дирижёром, отсчёта нет: там вторая сторона заведомо жива, она же и подавала.
/// </summary>
public sealed class ConductorRequestService
{
    /// <summary>
    /// Через сколько заявку, поданную НАЗНАЧАЕМЫМ сервером, можно принять в одностороннем
    /// порядке (T-21-S1). Двенадцать часов — это «дирижёр не отозвался за половину суток»:
    /// достаточно долго, чтобы выключенный на ночь компьютер успел включиться и ответить,
    /// и достаточно коротко, чтобы потеря сервера не парализовала кластер на неделю.
    /// </summary>
    public static readonly TimeSpan UnilateralAfter = TimeSpan.FromHours(12);

    /// <summary>Сколько решённая заявка остаётся в таблице: она должна доехать до партнёров
    /// и побыть у них на виду, но вечно копить их незачем.</summary>
    public static readonly TimeSpan KeepDecided = TimeSpan.FromDays(7);

    private const string Table = "conductor_requests";

    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ConductorRequestService(Database db, EventStore events, ServerScope scope)
    {
        _db = db;
        _events = events;
        _scope = scope;
    }

    /// <summary>
    /// Подать заявку «назначить дирижёром сервер <paramref name="targetServerId"/>».
    /// Подающий — ЭТОТ сервер, и он обязан быть одной из двух сторон: проверку делает API
    /// (ему видны серверы организации), сюда приходит уже разрешённое действие.
    /// </summary>
    /// <param name="fromServerId">Сегодняшний дирижёр организации; пусто — дирижёра нет.</param>
    public ConductorRequest Add(string targetServerId, string fromServerId, string note,
        string requestedBy, string? actorId)
    {
        if (targetServerId.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.conductor.1"));
        }
        if (targetServerId == fromServerId)
        {
            throw new ArgumentException(Loc.T("msg.conductor.2"));
        }
        if (Open() is not null)
        {
            throw new ArgumentException(Loc.T("msg.conductor.3"));
        }
        var now = DateTime.UtcNow;
        var request = new ConductorRequest
        {
            TargetServerId = targetServerId.Trim(),
            FromServerId = fromServerId.Trim(),
            InitiatorServerId = _scope.ServerId,
            Status = ConductorRequestStatus.Pending,
            Note = note.Trim(),
            RequestedBy = requestedBy.Trim(),
            // владелец строки — АВТОР заявки, и назван он ЯВНО (в отличие от заявок на запуск,
            // где у дирижёра поле пусто): дирижёр может смениться прямо этой заявкой, и «строку
            // без сервера правит дирижёр» перестало бы указывать на её автора
            ServerId = _scope.ServerId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, $"""
            INSERT INTO {Table} (id, target_server_id, from_server_id, initiator_server_id,
                                 status, note, requested_by, decided_by, unilateral,
                                 server_id, created_at, updated_at)
            VALUES (@id, @target, @from, @init, @status, @note, @by, '', 0, @server, @cr, @up)
            """,
            ("@id", request.Id), ("@target", request.TargetServerId),
            ("@from", request.FromServerId), ("@init", request.InitiatorServerId),
            ("@status", request.Status), ("@note", request.Note), ("@by", request.RequestedBy),
            // владелец строки — АВТОР заявки, как у заявок на запуск (T-196)
            ("@server", _scope.ServerId), ("@cr", Sql.ToDb(now)), ("@up", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ConductorRequested,
            EntityType = "conductor_request",
            EntityId = request.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                target = _scope.CodeOf(request.TargetServerId),
                from = _scope.CodeOf(request.FromServerId),
                byTarget = request.ByTarget,
            }),
        });
        tx.Commit();
        return Decorate(request);
    }

    /// <summary>Заявка, которая ещё ждёт решения; null — таких нет. Она в организации одна:
    /// две одновременные передачи дирижёрства означали бы гонку за один признак.</summary>
    public ConductorRequest? Open()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, $"""
            SELECT * FROM {Table} WHERE deleted_at IS NULL AND status=@s ORDER BY created_at DESC LIMIT 1
            """, Map, ("@s", ConductorRequestStatus.Pending)).Select(Decorate).FirstOrDefault();
    }

    public ConductorRequest? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, $"SELECT * FROM {Table} WHERE id=@id", Map, ("@id", id))
            .Select(Decorate).FirstOrDefault();
    }

    /// <summary>
    /// Заявки для списка серверов. По умолчанию — только те, что ЖДУТ РЕШЕНИЯ: решённая
    /// заявка человеку больше ничего не говорит, а из списка не уходила неделю, до уборки
    /// <see cref="KeepDecided"/>, и висела там как незаконченное дело (жалоба T-293).
    ///
    /// <paramref name="all"/> — «показать все заявки»: тогда видна вся история, включая
    /// СНЯТЫЕ подавшим (они мягко удалены и не показываются иначе никогда). Ждущая решения
    /// впереди, решённые следом по времени подачи.
    /// </summary>
    public List<ConductorRequest> List(bool all = false)
    {
        using var conn = _db.Open();
        var sql = all
            ? $"SELECT * FROM {Table} ORDER BY created_at DESC"
            : $"SELECT * FROM {Table} WHERE deleted_at IS NULL AND status=@s ORDER BY created_at DESC";
        var rows = all
            ? Sql.Query(conn, null, sql, Map)
            : Sql.Query(conn, null, sql, Map, ("@s", ConductorRequestStatus.Pending));
        return rows
            .Select(Decorate)
            .OrderBy(r => ConductorRequestStatus.IsOpen(r.Status) ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// РЕШЕНИЕ ПО ЗАЯВКЕ. Принять или отклонить вправе ВТОРАЯ сторона — та, что заявку
    /// не подавала; снять заявку вправе только подавший. Само переназначение дирижёра
    /// делается вызывающей стороной (серверы лежат в другой БД), здесь — только запись
    /// решения, которая уедет партнёру обычной репликацией.
    /// </summary>
    /// <param name="unilateral">Принято в одностороннем порядке (T-21-S1): дирижёр
    /// не отозвался за <see cref="UnilateralAfter"/>, и назначаемый сервер принял свою же
    /// заявку сам. В журнал это идёт отдельным событием — согласия второй стороны не было.</param>
    public ConductorRequest Decide(string id, string status, string decidedBy, string? actorId,
        bool unilateral = false)
    {
        var request = Get(id) ?? throw new ArgumentException(Loc.T("msg.conductor.4"));
        if (!ConductorRequestStatus.IsOpen(request.Status))
        {
            throw new ArgumentException(Loc.T("msg.conductor.5"));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, $"""
            UPDATE {Table}
            SET status=@s, decided_by=@by, decided_at=@at, unilateral=@uni, updated_at=@at,
                deleted_at=@del
            WHERE id=@id
            """,
            ("@s", status), ("@by", decidedBy.Trim()), ("@at", Sql.ToDb(now)),
            ("@uni", unilateral ? 1 : 0),
            // СНЯТАЯ заявка исчезает из списков сразу — решать по ней больше нечего;
            // принятая и отклонённая остаются на виду до уборки
            ("@del", status == ConductorRequestStatus.Withdrawn ? Sql.ToDb(now) : null),
            ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = status switch
            {
                ConductorRequestStatus.Accepted when unilateral => EventTypes.ConductorTakenOver,
                ConductorRequestStatus.Accepted => EventTypes.ConductorAccepted,
                ConductorRequestStatus.Withdrawn => EventTypes.ConductorWithdrawn,
                _ => EventTypes.ConductorRejected,
            },
            EntityType = "conductor_request",
            EntityId = id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                target = _scope.CodeOf(request.TargetServerId),
                from = _scope.CodeOf(request.FromServerId),
                unilateral,
            }),
        });
        tx.Commit();
        request.Status = status;
        request.DecidedBy = decidedBy.Trim();
        request.DecidedAt = now;
        request.Unilateral = unilateral;
        request.UpdatedAt = now;
        return request;
    }

    /// <summary>
    /// Заявку можно принять В ОДНОСТОРОННЕМ ПОРЯДКЕ (T-21-S1): она ждёт решения, подал её
    /// САМ назначаемый сервер, нажимает кнопку тоже он, и отсчёт <see cref="UnilateralAfter"/>
    /// истёк. Заявку, поданную дирижёром, так принять нельзя никогда — вторая сторона
    /// (сам подавший) заведомо жива, и ждать её ответа незачем.
    /// </summary>
    /// <param name="localServerId">Внутренний ключ сервера, на котором нажимают кнопку.</param>
    public static bool CanTakeUnilaterally(ConductorRequest request, string localServerId, DateTime now) =>
        ConductorRequestStatus.IsOpen(request.Status)
        && request.ByTarget
        && request.TargetServerId == localServerId
        && now - request.CreatedAt >= UnilateralAfter;

    /// <summary>Сколько ждать до одностороннего принятия; <see cref="TimeSpan.Zero"/> —
    /// уже можно (или заявка такой возможности не даёт вовсе).</summary>
    public static TimeSpan Countdown(ConductorRequest request, DateTime now)
    {
        if (!request.ByTarget || !ConductorRequestStatus.IsOpen(request.Status))
        {
            return TimeSpan.Zero;
        }
        var left = request.CreatedAt + UnilateralAfter - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Уборка (проход сторожа): автор убирает свои решённые заявки, отжившие срок
    /// <see cref="KeepDecided"/>. Удаление физическое — оно едет партнёрам журналом изменений
    /// и убирает строку и у них.</summary>
    public int Cleanup()
    {
        var edge = DateTime.UtcNow - KeepDecided;
        using var conn = _db.Open();
        var mine = Sql.Query(conn, null,
                $"SELECT * FROM {Table} WHERE status<>@s", Map, ("@s", ConductorRequestStatus.Pending))
            .Where(r => _scope.CanWrite(r.ServerId) && r.UpdatedAt < edge)
            .ToList();
        foreach (var request in mine)
        {
            Sql.Exec(conn, null, $"DELETE FROM {Table} WHERE id=@id", ("@id", request.Id));
        }
        return mine.Count;
    }

    private ConductorRequest Decorate(ConductorRequest request)
    {
        request.TargetServerCode = _scope.CodeOf(request.TargetServerId);
        request.TargetServerName = _scope.NameOf(request.TargetServerId);
        request.FromServerCode = _scope.CodeOf(request.FromServerId);
        request.FromServerName = _scope.NameOf(request.FromServerId);
        return request;
    }

    private static ConductorRequest Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        TargetServerId = r.S("target_server_id"),
        FromServerId = r.S("from_server_id"),
        InitiatorServerId = r.S("initiator_server_id"),
        Status = r.S("status"),
        Note = r.S("note"),
        RequestedBy = r.S("requested_by"),
        DecidedBy = r.S("decided_by"),
        DecidedAt = r.DtN("decided_at"),
        Unilateral = r.L("unilateral") != 0,
        ServerId = r.SN("server_id"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
