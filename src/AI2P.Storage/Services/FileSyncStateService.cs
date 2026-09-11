using AI2P.Core;
using AI2P.Core.Api;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// База сравнения и конфликты репликации ФАЙЛОВ (ТЗ гл. 6, п. 7 плана; этап 44).
///
/// «База сравнения» (<c>file_sync</c>) — снимок того, каким файл был в момент последнего
/// согласия с этим партнёром. Без неё две стороны различить нельзя: увидев два разных файла,
/// невозможно сказать, кто из них новый, а кто старый, — а от этого зависит и перенос,
/// и обнаружение конфликта. Это тот же приём, что индекс в git.
///
/// Конфликт (<c>file_conflicts</c>, todo44): файл изменился за интервал репликации НА ДВУХ
/// серверах. Такой файл не реплицируется вовсе, пока человек не выберет одно из четырёх
/// решений; решение применяется при следующем сеансе и только на один сеанс.
///
/// Всё лежит в СЕРВЕРНОЙ БД и не реплицируется: это сведения узла о собственном обмене.
/// </summary>
public sealed class FileSyncStateService
{
    private readonly Database _db;

    public FileSyncStateService(Database db)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.fileSyncState.1"),
                nameof(db));
        }
        _db = db;
    }

    /// <summary>Ключ каталога: <c>orgdata:&lt;orgId&gt;</c> либо <c>common:&lt;projectId&gt;</c>.</summary>
    public static string ScopeKey(string scope, string key) => scope + ":" + key;

    // --- база сравнения ---

    /// <summary>Снимок каталога на момент последнего согласия: путь → запись.</summary>
    public Dictionary<string, FileEntry> Baseline(string peerId, string scopeKey)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT path, size, mtime, hash FROM file_sync WHERE peer_id=@p AND scope_key=@s
            """,
            r => new FileEntry
            {
                Path = r.S("path"),
                Size = r.L("size"),
                Mtime = r.S("mtime"),
                Hash = r.S("hash"),
            },
            ("@p", peerId), ("@s", scopeKey))
            .ToDictionary(e => e.Path, StringComparer.Ordinal);
    }

    /// <summary>
    /// Известные хэши каталога по ключу «путь|размер|время» — для ПАССИВНОЙ стороны сеанса:
    /// она отдаёт манифест, не зная, кто спрашивает, а содержимое файла от партнёра не
    /// зависит. Без этого гигабайтные медиа хэшировались бы на каждый запрос манифеста.
    /// </summary>
    public Dictionary<string, string> KnownHashes(string scopeKey)
    {
        using var conn = _db.Open();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Sql.Query(conn, null,
                     "SELECT path, size, mtime, hash FROM file_sync WHERE scope_key=@s",
                     r => (Path: r.S("path"), Size: r.L("size"), Mtime: r.S("mtime"), Hash: r.S("hash")),
                     ("@s", scopeKey)))
        {
            result[row.Path + "|" + row.Size + "|" + row.Mtime] = row.Hash;
        }
        return result;
    }

    /// <summary>Записать согласованное состояние файла (файл перенесён либо совпал).</summary>
    public void Remember(string peerId, string scopeKey, FileEntry entry)
    {
        using var conn = _db.Open();
        Remember(conn, null, peerId, scopeKey, entry);
    }

    private static void Remember(SqliteConnection conn, SqliteTransaction? tx, string peerId,
        string scopeKey, FileEntry entry) =>
        Sql.Exec(conn, tx, """
            INSERT INTO file_sync (peer_id, scope_key, path, size, mtime, hash, synced_at)
            VALUES (@p, @s, @path, @size, @mtime, @hash, @now)
            ON CONFLICT(peer_id, scope_key, path) DO UPDATE SET
              size=@size, mtime=@mtime, hash=@hash, synced_at=@now
            """,
            ("@p", peerId), ("@s", scopeKey), ("@path", entry.Path), ("@size", entry.Size),
            ("@mtime", entry.Mtime), ("@hash", entry.Hash), ("@now", Sql.ToDb(DateTime.UtcNow)));

    /// <summary>Записать согласованное состояние пачкой — одной транзакцией на весь каталог.</summary>
    public void RememberAll(string peerId, string scopeKey, IEnumerable<FileEntry> entries)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var entry in entries)
        {
            Remember(conn, tx, peerId, scopeKey, entry);
        }
        tx.Commit();
    }

    /// <summary>Забыть файл: он удалён с обеих сторон (или перестал реплицироваться).</summary>
    public void Forget(string peerId, string scopeKey, IEnumerable<string> paths)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var path in paths)
        {
            Sql.Exec(conn, tx, "DELETE FROM file_sync WHERE peer_id=@p AND scope_key=@s AND path=@path",
                ("@p", peerId), ("@s", scopeKey), ("@path", path));
        }
        tx.Commit();
    }

    /// <summary>Забыть каталог целиком (сброс курсоров на экране диагностики: следующий сеанс
    /// сверит всё заново — расхождением это не грозит, сверка идёт по хэшам).</summary>
    public void Reset(string peerId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM file_sync WHERE peer_id=@p", ("@p", peerId));
    }

    // --- конфликты (todo44) ---

    /// <summary>Конфликты пары; пустой <paramref name="peerId"/> — все.</summary>
    public List<ReplFileConflictDto> Conflicts(string? peerId = null, string? orgId = null)
    {
        using var conn = _db.Open();
        var where = new List<string>();
        var args = new List<(string, object?)>();
        if (peerId is { Length: > 0 })
        {
            where.Add("server_id=@p");
            args.Add(("@p", peerId));
        }
        if (orgId is { Length: > 0 })
        {
            where.Add("org_id=@o");
            args.Add(("@o", orgId));
        }
        var sql = "SELECT * FROM file_conflicts"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                  + " ORDER BY detected_at, path";
        return Sql.Query(conn, null, sql, Map, args.ToArray());
    }

    /// <summary>Сколько конфликтов ждёт решения (число на кнопке в списке серверов).</summary>
    public int Count(string peerId)
    {
        using var conn = _db.Open();
        return (int)Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM file_conflicts WHERE server_id=@p", ("@p", peerId));
    }

    public ReplFileConflictDto? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM file_conflicts WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    /// <summary>
    /// Записать конфликт. Уже известный обновляется — но ВЫБРАННОЕ РЕШЕНИЕ сохраняется только
    /// пока версии файлов те же: если человек выбрал «принять левый», а файл после этого
    /// снова изменили, решение относится уже не к тому содержимому и сбрасывается.
    /// </summary>
    public void Record(string orgId, string peerId, string scope, string scopeKey, string projectId,
        string path, FileEntry left, FileEntry right, string leftValue = "", string rightValue = "")
    {
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        var existing = Sql.Query(conn, null, """
            SELECT id, left_hash, right_hash FROM file_conflicts
            WHERE org_id=@o AND server_id=@p AND scope_key=@s AND path=@path
            """,
            r => (Id: r.S("id"), Left: r.S("left_hash"), Right: r.S("right_hash")),
            ("@o", orgId), ("@p", peerId), ("@s", scopeKey), ("@path", path)).FirstOrDefault();
        if (existing != default)
        {
            var same = existing.Left == left.Hash && existing.Right == right.Hash;
            Sql.Exec(conn, null, """
                UPDATE file_conflicts SET left_size=@ls, left_mtime=@lm, left_hash=@lh,
                    right_size=@rs, right_mtime=@rm, right_hash=@rh,
                    left_value=@lv, right_value=@rv, updated_at=@now
                """ + (same ? "" : ", resolution=''") + " WHERE id=@id",
                ("@ls", left.Size), ("@lm", left.Mtime), ("@lh", left.Hash),
                ("@rs", right.Size), ("@rm", right.Mtime), ("@rh", right.Hash),
                ("@lv", leftValue), ("@rv", rightValue),
                ("@now", now), ("@id", existing.Id));
            return;
        }
        Sql.Exec(conn, null, """
            INSERT INTO file_conflicts (id, org_id, server_id, scope, scope_key, project_id, path,
                                        left_size, left_mtime, left_hash,
                                        right_size, right_mtime, right_hash,
                                        left_value, right_value,
                                        resolution, detected_at, updated_at)
            VALUES (@id, @o, @p, @scope, @s, @proj, @path, @ls, @lm, @lh, @rs, @rm, @rh,
                    @lv, @rv, '', @now, @now)
            """,
            ("@id", Guid.NewGuid().ToString()), ("@o", orgId), ("@p", peerId), ("@scope", scope),
            ("@s", scopeKey), ("@proj", projectId), ("@path", path),
            ("@ls", left.Size), ("@lm", left.Mtime), ("@lh", left.Hash),
            ("@rs", right.Size), ("@rm", right.Mtime), ("@rh", right.Hash),
            ("@lv", leftValue), ("@rv", rightValue), ("@now", now));
    }

    /// <summary>Выбрать решение по конфликту (кнопки в списке конфликтов, todo44).</summary>
    public void Resolve(string id, string resolution)
    {
        if (!ReplFileResolutions.IsKnown(resolution))
        {
            throw new ArgumentException(Loc.T("msg.fileSyncState.2", resolution));
        }
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE file_conflicts SET resolution=@r, updated_at=@now WHERE id=@id",
            ("@r", resolution), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
    }

    /// <summary>Убрать конфликт: он разрешён (решение применено) либо исчез сам —
    /// файлы совпали или один из них удалили.</summary>
    public void Clear(string orgId, string peerId, string scopeKey, string path)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            DELETE FROM file_conflicts WHERE org_id=@o AND server_id=@p AND scope_key=@s AND path=@path
            """, ("@o", orgId), ("@p", peerId), ("@s", scopeKey), ("@path", path));
    }

    private static ReplFileConflictDto Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        OrgId = r.S("org_id"),
        ServerId = r.S("server_id"),
        Scope = r.S("scope"),
        ProjectId = r.S("project_id"),
        Path = r.S("path"),
        LeftSize = r.L("left_size"),
        LeftMtime = r.S("left_mtime"),
        LeftHash = r.S("left_hash"),
        RightSize = r.L("right_size"),
        RightMtime = r.S("right_mtime"),
        RightHash = r.S("right_hash"),
        LeftValue = r.S("left_value"),
        RightValue = r.S("right_value"),
        Resolution = r.S("resolution"),
        DetectedAt = r.Dt("detected_at"),
    };
}
