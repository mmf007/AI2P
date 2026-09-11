using AI2P.Core;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ОЧЕРЕДЬ ПОВТОРА РЕПЛИКАЦИИ (T-160): изменения, которые база принять отказалась.
///
/// Зачем она. Пачка изменений применялась одной транзакцией, и любое нарушение целостности
/// (не приехавшая ещё родительская строка, занятое уникальное значение) откатывало все
/// четыреста строк, а сеанс кончался исключением «SQLite Error 19». Курсор при этом не
/// двигался, поэтому следующий сеанс упирался в ту же строку — и так навсегда. Организация
/// оставалась скопированной ровно до этого места, а до репликации ФАЙЛОВ (описания задач,
/// критерии, результаты заданий — они идут последним шагом сеанса) дело не доходило вовсе.
/// Именно так выглядела жалоба: «от задач есть только заголовки».
///
/// Теперь отвергнутая строка откладывается сюда и пробуется заново — в конце вычитки и в
/// каждом следующем сеансе. Ключ записи — сама строка (организация, партнёр, база, таблица,
/// первичный ключ): повторный отказ обновляет запись, а новая версия той же строки заменяет
/// старую. Живёт в СЕРВЕРНОЙ БД рядом с курсорами и не реплицируется: это сведения узла
/// о собственном обмене (ТЗ п. 6.4.1).
/// </summary>
public sealed class ReplPendingService
{
    private readonly Database _db;

    public ReplPendingService(Database db)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.replPending.1"),
                nameof(db));
        }
        _db = db;
    }

    /// <summary>Отложить отвергнутые строки (или обновить причину у уже отложенных).</summary>
    public void Save(string orgId, string peerId, string scope, IEnumerable<RowFailure> failures)
    {
        var list = failures.ToList();
        if (list.Count == 0)
        {
            return;
        }
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var failure in list)
        {
            var change = failure.Change;
            Sql.Exec(conn, tx, """
                INSERT INTO repl_pending (org_id, peer_id, scope, tbl, pk, seq, op, ts, node_id,
                                          payload_json, reason, tries, first_at, last_at)
                VALUES (@o, @p, @sc, @t, @pk, @seq, @op, @ts, @node, @payload, @reason, 1, @now, @now)
                ON CONFLICT(org_id, peer_id, scope, tbl, pk) DO UPDATE SET
                  seq=@seq, op=@op, ts=@ts, node_id=@node, payload_json=@payload,
                  reason=@reason, tries=tries+1, last_at=@now
                """,
                ("@o", orgId), ("@p", peerId), ("@sc", scope), ("@t", change.Table), ("@pk", change.Pk),
                ("@seq", change.Seq), ("@op", change.Op), ("@ts", change.Ts), ("@node", change.NodeId),
                ("@payload", change.PayloadJson), ("@reason", failure.Reason), ("@now", now));
        }
        tx.Commit();
    }

    /// <summary>Отложенные строки в порядке журнала источника: родитель раньше ребёнка.</summary>
    public List<RowChange> Take(string orgId, string peerId, string scope)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT seq, tbl, pk, op, ts, node_id, payload_json FROM repl_pending
            WHERE org_id=@o AND peer_id=@p AND scope=@sc ORDER BY seq
            """,
            r => new RowChange
            {
                Seq = r.L("seq"),
                Table = r.S("tbl"),
                Pk = r.S("pk"),
                Op = r.S("op"),
                Ts = r.S("ts"),
                NodeId = r.S("node_id"),
                PayloadJson = r.S("payload_json"),
            },
            ("@o", orgId), ("@p", peerId), ("@sc", scope));
    }

    /// <summary>Строка наконец применилась — из очереди её убираем.</summary>
    public void Remove(string orgId, string peerId, string scope, IEnumerable<RowChange> changes)
    {
        var list = changes.ToList();
        if (list.Count == 0)
        {
            return;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var change in list)
        {
            Sql.Exec(conn, tx, """
                DELETE FROM repl_pending
                WHERE org_id=@o AND peer_id=@p AND scope=@sc AND tbl=@t AND pk=@pk
                """,
                ("@o", orgId), ("@p", peerId), ("@sc", scope), ("@t", change.Table), ("@pk", change.Pk));
        }
        tx.Commit();
    }

    /// <summary>Сколько строк ждёт повтора по паре (обе базы вместе).</summary>
    public int Count(string orgId, string peerId)
    {
        using var conn = _db.Open();
        return (int)Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM repl_pending WHERE org_id=@o AND peer_id=@p",
            ("@o", orgId), ("@p", peerId));
    }

    /// <summary>Последняя причина отказа словами — её показывает экран диагностики.</summary>
    public string Last(string orgId, string peerId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT tbl, pk, reason, tries FROM repl_pending
            WHERE org_id=@o AND peer_id=@p ORDER BY last_at DESC LIMIT 1
            """,
            r => Loc.T("msg.replPending.2", r.S("tbl"), r.S("pk"), r.S("reason"), r.L("tries")),
            ("@o", orgId), ("@p", peerId)).FirstOrDefault() ?? "";
    }

    /// <summary>Все отложенные строки пары — для экрана диагностики.</summary>
    public List<(string Scope, string Table, string Pk, string Reason, int Tries)> List(
        string orgId, string peerId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT scope, tbl, pk, reason, tries FROM repl_pending
            WHERE org_id=@o AND peer_id=@p ORDER BY scope, seq
            """,
            r => (r.S("scope"), r.S("tbl"), r.S("pk"), r.S("reason"), (int)r.L("tries")),
            ("@o", orgId), ("@p", peerId));
    }

    /// <summary>Забыть очередь пары: сервер выведен из организации либо человек попросил
    /// перечитать всё заново (кнопка «первичная репликация»).</summary>
    public void Reset(string orgId, string peerId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM repl_pending WHERE org_id=@o AND peer_id=@p",
            ("@o", orgId), ("@p", peerId));
    }
}
