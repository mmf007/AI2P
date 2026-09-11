using AI2P.Core;
using AI2P.Core.Entities;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Курсоры и состояние репликации по паре «организация — сервер-партнёр» (ТЗ гл. 6, этап 43).
///
/// Живёт в СЕРВЕРНОЙ БД и не реплицируется: докуда мы дочитали чужой журнал и когда в
/// последний раз была связь — сведения этого узла о самом себе.
///
/// Курсоры ведут ОБА конца пары, независимо от того, кто вёл сеанс: сеанс может начать
/// и рядовой сервер, и дирижёр (тогда второй запуск просто игнорируется), а после смены
/// инициатора перечитывать журнал с нуля было бы расточительно.
/// </summary>
public sealed class ReplicationStateService
{
    private readonly Database _db;

    public ReplicationStateService(Database db)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.replicationState.1"), nameof(db));
        }
        _db = db;
    }

    /// <summary>Состояние пары; если записи ещё нет — пустое (курсоры нулевые, значит
    /// ближайший сеанс будет первичной репликацией).</summary>
    public ReplicationState Get(string orgId, string serverId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
                   "SELECT * FROM repl_state WHERE org_id=@o AND server_id=@s",
                   Map, ("@o", orgId), ("@s", serverId)).FirstOrDefault()
               ?? new ReplicationState { OrgId = orgId, ServerId = serverId };
    }

    /// <summary>Все состояния (экран диагностики и колонка «статус репликации»).</summary>
    public List<ReplicationState> List()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM repl_state", Map);
    }

    /// <summary>Состояния одной организации.</summary>
    public List<ReplicationState> ListOf(string orgId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM repl_state WHERE org_id=@o", Map, ("@o", orgId));
    }

    /// <summary>Записать состояние целиком (после сеанса или при его начале).</summary>
    public void Save(ReplicationState state)
    {
        state.UpdatedAt = DateTime.UtcNow;
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO repl_state (org_id, server_id, pull_cursor, push_cursor, srv_pull_cursor,
                                    srv_push_cursor, status, last_run_at, last_ok_at, next_run_at,
                                    last_error, conflicts, last_conflict, received, sent,
                                    files_received, files_sent, bytes_received, bytes_sent,
                                    pending_rows, last_pending, updated_at)
            VALUES (@o, @s, @pull, @push, @spull, @spush, @st, @run, @ok, @next, @err, @cf, @lcf,
                    @rec, @sent, @frec, @fsent, @brec, @bsent, @pend, @lpend, @u)
            ON CONFLICT(org_id, server_id) DO UPDATE SET
              pull_cursor=@pull, push_cursor=@push, srv_pull_cursor=@spull, srv_push_cursor=@spush,
              status=@st, last_run_at=@run, last_ok_at=@ok, next_run_at=@next, last_error=@err,
              conflicts=@cf, last_conflict=@lcf, received=@rec, sent=@sent,
              files_received=@frec, files_sent=@fsent, bytes_received=@brec, bytes_sent=@bsent,
              pending_rows=@pend, last_pending=@lpend, updated_at=@u
            """,
            ("@o", state.OrgId), ("@s", state.ServerId),
            ("@pull", state.PullCursor), ("@push", state.PushCursor),
            ("@spull", state.ServerPullCursor), ("@spush", state.ServerPushCursor),
            ("@st", state.Status), ("@run", Sql.ToDbN(state.LastRunAt)),
            ("@ok", Sql.ToDbN(state.LastOkAt)), ("@next", Sql.ToDbN(state.NextRunAt)),
            ("@err", state.LastError), ("@cf", state.Conflicts), ("@lcf", state.LastConflict),
            ("@rec", state.Received), ("@sent", state.Sent),
            ("@frec", state.FilesReceived), ("@fsent", state.FilesSent),
            ("@brec", state.BytesReceived), ("@bsent", state.BytesSent),
            ("@pend", state.Pending), ("@lpend", state.LastPending),
            ("@u", Sql.ToDb(state.UpdatedAt)));
    }

    /// <summary>
    /// Подвинуть ОДИН курсор, не трогая остального состояния. Нужно пассивной стороне сеанса:
    /// она отвечает на запросы партнёра и попутно узнаёт, докуда он её прочитал и докуда
    /// прочитан он сам, — чтобы после смены инициатора не перечитывать журнал заново.
    /// </summary>
    public void Advance(string orgId, string serverId, string column, long value)
    {
        if (!AllowedCursors.Contains(column))
        {
            throw new ArgumentException(Loc.T("msg.replicationState.2", column), nameof(column));
        }
        using var conn = _db.Open();
        var now = Sql.ToDb(DateTime.UtcNow);
        Sql.Exec(conn, null, $"""
            INSERT INTO repl_state (org_id, server_id, {column}, updated_at) VALUES (@o, @s, @v, @u)
            ON CONFLICT(org_id, server_id) DO UPDATE SET
              {column}=MAX({column}, @v), updated_at=@u
            """, ("@o", orgId), ("@s", serverId), ("@v", value), ("@u", now));
    }

    /// <summary>Курсоры — единственное, что подставляется в SQL именем, поэтому список закрытый.</summary>
    private static readonly HashSet<string> AllowedCursors = new(StringComparer.Ordinal)
    {
        "pull_cursor", "push_cursor", "srv_pull_cursor", "srv_push_cursor",
    };

    /// <summary>
    /// «ИДЁТ СЕАНС» НЕ ПЕРЕЖИВАЕТ ПЕРЕЗАПУСК (T-174).
    ///
    /// Признак «running» пишется в базу при начале сеанса и снимается при его окончании.
    /// Если приложение остановили посреди сеанса, он оставался в базе навсегда: замка пары
    /// в памяти уже нет, а список серверов вечно показывает прогресс-бар с 0% и автоматический
    /// обход такую пару пропускает — репликация не идёт и не жалуется. Сеансов в момент
    /// старта не бывает по определению, поэтому при запуске признак снимается.
    /// </summary>
    /// <returns>Сколько пар было помечено идущим сеансом.</returns>
    public int ClearRunning()
    {
        using var conn = _db.Open();
        return Sql.Exec(conn, null,
            "UPDATE repl_state SET status=@idle, updated_at=@u WHERE status=@run",
            ("@idle", ReplicationStatuses.Idle), ("@run", ReplicationStatuses.Running),
            ("@u", Sql.ToDb(DateTime.UtcNow)));
    }

    /// <summary>Забыть состояние пары: сервер выведен из организации либо человек попросил
    /// перечитать всё заново (кнопка «первичная репликация» на экране диагностики).</summary>
    public void Reset(string orgId, string serverId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM repl_state WHERE org_id=@o AND server_id=@s",
            ("@o", orgId), ("@s", serverId));
        // курсоры архивов той же пары (T-47-S0): «первичная репликация» обязана забывать
        // и их, иначе текущий архив остался бы скопированным до прежнего места (наука
        // T-20-S1 про очередь повтора — служебное состояние без кнопки «забыть»)
        Sql.Exec(conn, null, "DELETE FROM repl_arc_state WHERE org_id=@o AND server_id=@s",
            ("@o", orgId), ("@s", serverId));
    }

    // --- КУРСОРЫ АРХИВОВ (T-47-S0) ---

    /// <summary>
    /// Курсоры пары по ОДНОМУ архиву: у каждого архива своя база той же схемы и свой журнал
    /// изменений. Записи ещё нет — нули, то есть ближайший сеанс перевезёт архив целиком.
    /// </summary>
    public (long Pull, long Push) ArchiveCursors(string orgId, string serverId, string archiveId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT pull_cursor, push_cursor FROM repl_arc_state
            WHERE org_id=@o AND server_id=@s AND archive_id=@a
            """, r => (r.L("pull_cursor"), r.L("push_cursor")),
            ("@o", orgId), ("@s", serverId), ("@a", archiveId)).FirstOrDefault();
    }

    /// <summary>Подвинуть один курсор архива. Имя колонки подставляется в SQL, поэтому
    /// список закрытый — как у <see cref="Advance"/>.</summary>
    public void AdvanceArchive(string orgId, string serverId, string archiveId, string column,
        long value)
    {
        if (column is not ("pull_cursor" or "push_cursor"))
        {
            throw new ArgumentException(Loc.T("msg.replicationState.2", column), nameof(column));
        }
        using var conn = _db.Open();
        Sql.Exec(conn, null, $"""
            INSERT INTO repl_arc_state (org_id, server_id, archive_id, {column}, updated_at)
            VALUES (@o, @s, @a, @v, @u)
            ON CONFLICT(org_id, server_id, archive_id) DO UPDATE SET
              {column}=MAX({column}, @v), updated_at=@u
            """, ("@o", orgId), ("@s", serverId), ("@a", archiveId), ("@v", value),
            ("@u", Sql.ToDb(DateTime.UtcNow)));
    }

    private static ReplicationState Map(SqliteDataReader r) => new()
    {
        OrgId = r.S("org_id"),
        ServerId = r.S("server_id"),
        PullCursor = r.L("pull_cursor"),
        PushCursor = r.L("push_cursor"),
        ServerPullCursor = r.L("srv_pull_cursor"),
        ServerPushCursor = r.L("srv_push_cursor"),
        Status = r.S("status"),
        LastRunAt = r.DtN("last_run_at"),
        LastOkAt = r.DtN("last_ok_at"),
        NextRunAt = r.DtN("next_run_at"),
        LastError = r.S("last_error"),
        Conflicts = (int)r.L("conflicts"),
        LastConflict = r.S("last_conflict"),
        Received = (int)r.L("received"),
        Sent = (int)r.L("sent"),
        FilesReceived = (int)r.L("files_received"),
        FilesSent = (int)r.L("files_sent"),
        BytesReceived = r.L("bytes_received"),
        BytesSent = r.L("bytes_sent"),
        Pending = (int)r.L("pending_rows"),
        LastPending = r.S("last_pending"),
        UpdatedAt = r.Dt("updated_at"),
    };
}
