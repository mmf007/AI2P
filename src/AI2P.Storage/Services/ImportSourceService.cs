using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Справочник импортов (ТЗ v1.28, todo30) — часть настройки системы: откуда импортировать
/// задачи. Первая поддержанная запись — Trello: по фильтру у конкретного логина
/// (параметры в params_json: login, filter, ссылки на ключи в секретах).
/// </summary>
public sealed class ImportSourceService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public ImportSourceService(Database db, EventStore events)
    {
        _db = db;
        _events = events;
    }

    /// <summary>
    /// Можно ли делать источник активным (ТЗ v1.65, T-123): возвращает текст ошибки, если
    /// нельзя, и null, если можно. Подставляется контекстом организации
    /// (<c>ImportKeyService.ActivationError</c>): импорт без ключа и токена работать всё
    /// равно не может, значит и активным быть не должен. Сам справочник про секреты не знает —
    /// они лежат этажом выше (AI2P.Connectors), поэтому связь через делегат, как у моделей.
    /// </summary>
    public Func<ImportSource, string?>? ActivationGuard { get; set; }

    public List<ImportSource> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM import_sources" + (includeDeleted ? "" : " WHERE deleted_at IS NULL")
                  + " ORDER BY name";
        return Sql.Query(conn, null, sql, Map);
    }

    public ImportSource? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM import_sources WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    public ImportSource Create(ImportSource source, string? actorId)
    {
        Validate(source);
        EnsureActivationAllowed(source);
        var now = DateTime.UtcNow;
        source.CreatedAt = now;
        source.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, source.Name, source.Id);
        Sql.Exec(conn, tx, """
            INSERT INTO import_sources (id, name, kind, params_json, is_active, created_at, updated_at)
            VALUES (@id, @name, @kind, @params, @active, @created, @updated)
            """,
            ("@id", source.Id), ("@name", source.Name), ("@kind", source.Kind),
            ("@params", source.ParamsJson), ("@active", source.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        Append(conn, tx, source, EventTypes.ImportSourceCreated, actorId);
        tx.Commit();
        return source;
    }

    public ImportSource Update(ImportSource source, string? actorId)
    {
        Validate(source);
        EnsureActivationAllowed(source);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, source.Name, source.Id);
        Sql.Exec(conn, tx, """
            UPDATE import_sources SET name=@name, kind=@kind, params_json=@params,
                                      is_active=@active, updated_at=@updated
            WHERE id=@id
            """,
            ("@name", source.Name), ("@kind", source.Kind), ("@params", source.ParamsJson),
            ("@active", source.IsActive ? 1 : 0), ("@updated", Sql.ToDb(now)), ("@id", source.Id));
        Append(conn, tx, source, EventTypes.ImportSourceUpdated, actorId);
        tx.Commit();
        source.UpdatedAt = now;
        return source;
    }

    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var source = Sql.Query(conn, tx, "SELECT * FROM import_sources WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (source is null || source.DeletedAt is not null)
        {
            return;
        }
        Sql.Exec(conn, tx, "UPDATE import_sources SET deleted_at=@deleted, updated_at=@deleted WHERE id=@id",
            ("@deleted", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        Append(conn, tx, source, EventTypes.ImportSourceDeleted, actorId);
        tx.Commit();
    }

    private static void Validate(ImportSource source)
    {
        source.Name = source.Name.Trim();
        if (source.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.importSource.1"));
        }
        if (source.Kind.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.importSource.2"));
        }
        try
        {
            JsonDocument.Parse(source.ParamsJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException(Loc.T("msg.importSource.3"));
        }
    }

    /// <summary>Источник импорта без ключа и токена активным быть не может (ТЗ v1.65, T-123).</summary>
    private void EnsureActivationAllowed(ImportSource source)
    {
        if (source.IsActive && ActivationGuard?.Invoke(source) is { } error)
        {
            throw new ArgumentException(error);
        }
    }

    private static void EnsureUniqueName(SqliteConnection conn, SqliteTransaction tx, string name, string selfId)
    {
        var taken = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM import_sources " +
            "WHERE name=@name COLLATE NOCASE AND id<>@id AND deleted_at IS NULL",
            ("@name", name), ("@id", selfId)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.importSource.4", name));
        }
    }

    private void Append(SqliteConnection conn, SqliteTransaction tx, ImportSource source,
        string eventType, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "import_source",
            EntityId = source.Id,
            PayloadJson = JsonSerializer.Serialize(new { source.Name, source.Kind }),
        });

    private static ImportSource Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        Name = r.S("name"),
        Kind = r.S("kind"),
        ParamsJson = r.S("params_json"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
