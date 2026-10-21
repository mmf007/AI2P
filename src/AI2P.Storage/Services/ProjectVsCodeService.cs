using AI2P.Core;

namespace AI2P.Storage.Services;

/// <summary>
/// ВЫБОР КОПИИ VS CODE ДЛЯ ПРОЕКТА (T-510-S0, этап 8.5 плагина). Свойство МАШИНЫ: путь к
/// code у каждого сервера свой, поэтому строка живёт в СЕРВЕРНОЙ базе (таблица
/// project_vscode) и в журнал репликации не попадает — её нет в ChangeLog.ServerTables, а
/// событий об изменении сервис не пишет вовсе.
/// </summary>
public sealed class ProjectVsCodeService
{
    private readonly Database _db;

    public ProjectVsCodeService(Database db)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.deviceToken.1"), nameof(db));
        }
        _db = db;
    }

    /// <summary>Выбранный путь к code; null — не выбирали.</summary>
    public string? Get(string orgId, string projectId)
    {
        using var conn = _db.Open();
        var path = Sql.Scalar<string>(conn, null,
            "SELECT code_path FROM project_vscode WHERE org_id=@o AND project_id=@p",
            ("@o", orgId), ("@p", projectId));
        return path is { Length: > 0 } ? path : null;
    }

    public void Set(string orgId, string projectId, string codePath)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO project_vscode(org_id, project_id, code_path, updated_at) VALUES (@o, @p, @c, @t)
            ON CONFLICT(org_id, project_id) DO UPDATE SET code_path=excluded.code_path, updated_at=excluded.updated_at
            """, ("@o", orgId), ("@p", projectId), ("@c", codePath.Trim()), ("@t", Sql.ToDb(DateTime.UtcNow)));
    }

    /// <summary>Все запомненные копии на этом сервере — их обновляет при старте T-511-S0
    /// (VsCodeExtension.RememberedCopies).</summary>
    public List<string> AllPaths()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT DISTINCT code_path FROM project_vscode WHERE code_path<>''",
            r => r.S("code_path"));
    }

    public void Clear(string orgId, string projectId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM project_vscode WHERE org_id=@o AND project_id=@p",
            ("@o", orgId), ("@p", projectId));
    }
}
