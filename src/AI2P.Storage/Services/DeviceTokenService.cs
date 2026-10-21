using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ТОКЕНЫ УСТРОЙСТВ (T-508-S0): вход расширения VS Code в AI2P от имени человека без формы.
///
/// Токен долгоживущий, привязан к аккаунту, организации и проекту. В базе лежит только
/// SHA-256 от него: токен — 32 случайных байта, подбирать по хэшу нечего, а медленный
/// PBKDF2 здесь ни к чему (по хэшу ищется строка на каждый обмен). Таблица в СЕРВЕРНОЙ
/// базе и в журнал репликации не входит (ChangeLog.ServerTables) — токен годен только на
/// машине, где его выпустили.
///
/// Сам токен для входа не годится: расширение меняет его на одноразовый короткоживущий
/// <see cref="EmbedTokenStore"/>, а тот — на cookie встроенного режима.
/// </summary>
public sealed class DeviceTokenService
{
    /// <summary>Приставка токена: по ней токен узнаётся в логах и в секретах.</summary>
    public const string Prefix = "ai2pd_";

    private readonly Database _db;
    private readonly EventStore _events;

    public DeviceTokenService(Database db, EventStore events)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.deviceToken.1"), nameof(db));
        }
        _db = db;
        _events = events;
    }

    /// <summary>Хэш токена — так он лежит в базе.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();

    /// <summary>Выпустить токен. Возвращает запись и сам токен — он показывается ОДИН раз.</summary>
    public (DeviceToken Record, string Token) Issue(string accountId, string orgId, string projectId,
        string name)
    {
        if (accountId.Length == 0 || orgId.Length == 0 || projectId.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.deviceToken.2"));
        }
        var token = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = new DeviceToken
        {
            AccountId = accountId,
            OrgId = orgId,
            ProjectId = projectId,
            Name = name.Trim().Length > 100 ? name.Trim()[..100] : name.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            INSERT INTO device_tokens(id, account_id, org_id, project_id, name, token_hash, created_at)
            VALUES (@id, @a, @o, @p, @n, @h, @c)
            """, ("@id", record.Id), ("@a", accountId), ("@o", orgId), ("@p", projectId),
            ("@n", record.Name), ("@h", Hash(token)), ("@c", Sql.ToDb(record.CreatedAt)));
        Log(conn, tx, EventTypes.DeviceTokenIssued, record);
        tx.Commit();
        return (record, token);
    }

    public DeviceToken? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM device_tokens WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    /// <summary>Токены человека в организации (по проекту, если задан); отозванные — по желанию.</summary>
    public List<DeviceToken> List(string accountId, string orgId, string? projectId = null,
        bool includeRevoked = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM device_tokens WHERE account_id=@a AND org_id=@o"
                  + (projectId is { Length: > 0 } ? " AND project_id=@p" : "")
                  + (includeRevoked ? "" : " AND revoked_at IS NULL")
                  + " ORDER BY created_at DESC";
        return Sql.Query(conn, null, sql, Map, ("@a", accountId), ("@o", orgId), ("@p", projectId ?? ""));
    }

    /// <summary>
    /// Проверить предъявленный токен. null — неизвестен или отозван. Отметка последнего
    /// использования ставится тут же: по ней человек отличает живое устройство от забытого.
    /// </summary>
    public DeviceToken? Verify(string token)
    {
        token = token.Trim();
        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }
        using var conn = _db.Open();
        var record = Sql.Query(conn, null,
                "SELECT * FROM device_tokens WHERE token_hash=@h AND revoked_at IS NULL", Map,
                ("@h", Hash(token)))
            .FirstOrDefault();
        if (record is not null)
        {
            record.LastUsedAt = DateTime.UtcNow;
            Sql.Exec(conn, null, "UPDATE device_tokens SET last_used_at=@t WHERE id=@id",
                ("@t", Sql.ToDb(record.LastUsedAt.Value)), ("@id", record.Id));
        }
        return record;
    }

    /// <summary>Токен по-прежнему действует — им проверяется cookie встроенного режима на каждый запрос.</summary>
    public bool IsActive(string id)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM device_tokens WHERE id=@id AND revoked_at IS NULL", ("@id", id)) > 0;
    }

    /// <summary>Отозвать свой токен. false — такого действующего токена у этого человека нет.</summary>
    public bool Revoke(string id, string accountId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var record = Sql.Query(conn, tx,
                "SELECT * FROM device_tokens WHERE id=@id AND account_id=@a AND revoked_at IS NULL", Map,
                ("@id", id), ("@a", accountId))
            .FirstOrDefault();
        if (record is null)
        {
            return false;
        }
        RevokeRow(conn, tx, record);
        tx.Commit();
        return true;
    }

    /// <summary>«Отключить VS Code»: отозвать все токены человека по проекту. Возвращает их число.</summary>
    public int RevokeProject(string accountId, string orgId, string projectId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var rows = Sql.Query(conn, tx, """
            SELECT * FROM device_tokens
            WHERE account_id=@a AND org_id=@o AND project_id=@p AND revoked_at IS NULL
            """, Map, ("@a", accountId), ("@o", orgId), ("@p", projectId));
        foreach (var record in rows)
        {
            RevokeRow(conn, tx, record);
        }
        tx.Commit();
        return rows.Count;
    }

    private void RevokeRow(SqliteConnection conn, SqliteTransaction tx, DeviceToken record)
    {
        record.RevokedAt = DateTime.UtcNow;
        Sql.Exec(conn, tx, "UPDATE device_tokens SET revoked_at=@t WHERE id=@id",
            ("@t", Sql.ToDb(record.RevokedAt.Value)), ("@id", record.Id));
        Log(conn, tx, EventTypes.DeviceTokenRevoked, record);
    }

    // в журнал — действие человека-владельца токена; самого токена и хэша там нет
    private void Log(SqliteConnection conn, SqliteTransaction tx, string type, DeviceToken record) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = record.AccountId,
            ProjectId = record.ProjectId,
            EventType = type,
            EntityType = "device_token",
            EntityId = record.Id,
            PayloadJson = JsonSerializer.Serialize(new { org = record.OrgId, name = record.Name }),
        });

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DeviceToken Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        AccountId = r.S("account_id"),
        OrgId = r.S("org_id"),
        ProjectId = r.S("project_id"),
        Name = r.S("name"),
        CreatedAt = r.Dt("created_at"),
        LastUsedAt = r.DtN("last_used_at"),
        RevokedAt = r.DtN("revoked_at"),
    };
}

/// <summary>
/// ОДНОРАЗОВЫЙ ТОКЕН ВХОДА встроенного режима (T-508-S0, <c>embedToken</c>). Живёт в памяти
/// процесса <see cref="Lifetime"/> и гасится первым предъявлением: им iframe VS Code один раз
/// получает cookie встроенного режима. Перезапуск сервера их теряет — это допустимо, расширение
/// просто возьмёт новый обменом токена устройства.
/// </summary>
public sealed class EmbedTokenStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public sealed record Entry(string DeviceTokenId, string AccountId, string OrgId, string ProjectId,
        DateTime Until);

    private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;

    public EmbedTokenStore(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>Выдать одноразовый токен под действующий токен устройства.</summary>
    public string Issue(DeviceToken device)
    {
        Sweep();
        var token = DeviceTokenService.Base64Url(RandomNumberGenerator.GetBytes(24));
        _tokens[DeviceTokenService.Hash(token)] = new Entry(device.Id, device.AccountId, device.OrgId,
            device.ProjectId, _now() + Lifetime);
        return token;
    }

    /// <summary>Предъявить токен: запись гасится сразу, повтор и просрочка дают null.</summary>
    public Entry? Redeem(string token)
    {
        if (token.Trim().Length == 0 || !_tokens.TryRemove(DeviceTokenService.Hash(token), out var entry))
        {
            return null;
        }
        return entry.Until >= _now() ? entry : null;
    }

    /// <summary>Выбросить просроченные — невостребованные токены иначе копились бы в памяти.</summary>
    private void Sweep()
    {
        var now = _now();
        foreach (var (key, entry) in _tokens)
        {
            if (entry.Until < now)
            {
                _tokens.TryRemove(key, out _);
            }
        }
    }
}
