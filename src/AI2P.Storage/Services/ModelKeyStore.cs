using AI2P.Core;
using System.Security.Cryptography;
using System.Text;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Ключ API организации в хранилище (ТЗ гл. 10, разд. 8 плана; этап 45).</summary>
public sealed class ModelKeyRow
{
    public string Id { get; set; } = "";

    /// <summary>Ссылка из профайла модели: <c>anthropic.apiKey</c>. Она же естественный ключ:
    /// несколько моделей могут пользоваться одним ключом провайдера.</summary>
    public string SecretRef { get; set; } = "";

    /// <summary>Значение, ЗАШИФРОВАННОЕ ключом организации: <c>base64(nonce|tag|шифртекст)</c>.
    /// В открытом виде значение в базе не лежит никогда.</summary>
    public string ValueEnc { get; set; } = "";

    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// КЛЮЧИ API МОДЕЛЕЙ — хранилище (ТЗ гл. 10, разд. 8 плана; этап 45).
///
/// Ключ принадлежит ОРГАНИЗАЦИИ, а не пользователю и не серверу: личных ключей аккаунта нет,
/// кому нужна отдельная работа — заводит себе организацию. Это заодно снимает вопрос
/// «чем расшифрует ключ автозапуск по расписанию, когда пользователя за экраном нет».
///
/// Значение всегда лежит зашифрованным (AES-GCM на ключе организации), поэтому строка
/// спокойно реплицируется обычным журналом изменений: расшифровать её может только сервер,
/// которому ключ организации выдан. Сам ключ организации живёт в <c>secrets.json</c>
/// и не реплицируется никогда.
///
/// Идентификатор строки ВЫВОДИТСЯ из <c>secret_ref</c> детерминированно: два сервера,
/// независимо заведшие ключ «anthropic.apiKey», обязаны получить ОДНУ строку. Иначе
/// репликация принесла бы вторую и упёрлась в <c>UNIQUE(secret_ref)</c>.
/// </summary>
public sealed class ModelKeyStore
{
    private readonly Database _db;
    private readonly EventStore _events;

    public ModelKeyStore(Database db, EventStore events)
    {
        _db = db;
        _events = events;
    }

    /// <summary>
    /// Идентификатор строки по ссылке на ключ: UUID из первых 16 байт sha256. Он одинаков
    /// на всех серверах — в этом весь смысл (см. описание класса).
    /// </summary>
    public static string IdOf(string secretRef)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("model_key:" + secretRef.Trim()));
        return new Guid(hash.AsSpan(0, 16).ToArray()).ToString();
    }

    public List<ModelKeyRow> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM model_keys" + (includeDeleted ? "" : " WHERE deleted_at IS NULL")
            + " ORDER BY secret_ref", Map);
    }

    /// <summary>Строка ключа по ссылке; null — ключ не заведён.</summary>
    public ModelKeyRow? Get(string secretRef)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM model_keys WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", IdOf(secretRef))).FirstOrDefault();
    }

    /// <summary>Строка по идентификатору (им ключ назван в журнале изменений); null — нет.</summary>
    public ModelKeyRow? GetById(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM model_keys WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    /// <summary>Зашифрованное значение ключа; пусто — ключа нет.</summary>
    public string Encrypted(string secretRef) => Get(secretRef)?.ValueEnc ?? "";

    /// <summary>
    /// Записать зашифрованное значение ключа. Время изменения — часы LWW: при репликации
    /// побеждает более поздняя запись, а одновременная правка ОДНОГО ключа на двух серверах
    /// становится конфликтом и решается человеком (todo45).
    /// </summary>
    public ModelKeyRow Set(string secretRef, string encrypted, string? actorId,
        DateTime? updatedAt = null)
    {
        secretRef = secretRef.Trim();
        if (secretRef.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.modelKey.1"));
        }
        var now = updatedAt ?? DateTime.UtcNow;
        var row = new ModelKeyRow
        {
            Id = IdOf(secretRef),
            SecretRef = secretRef,
            ValueEnc = encrypted,
            UpdatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var existed = Sql.Scalar<long>(conn, tx, "SELECT COUNT(*) FROM model_keys WHERE id=@id",
            ("@id", row.Id)) > 0;
        Sql.Exec(conn, tx, """
            INSERT INTO model_keys (id, secret_ref, value_enc, updated_by, created_at, updated_at)
            VALUES (@id, @ref, @val, @by, @now, @now)
            ON CONFLICT(id) DO UPDATE SET
              value_enc=@val, updated_by=@by, updated_at=@now, deleted_at=NULL
            """,
            ("@id", row.Id), ("@ref", secretRef), ("@val", encrypted), ("@by", actorId),
            ("@now", Sql.ToDb(now)));
        // в журнал работ едет ССЫЛКА на ключ, но никогда его значение (ТЗ п. 6.3)
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = existed ? EventTypes.ModelKeyUpdated : EventTypes.ModelKeyCreated,
            EntityType = "model_key",
            EntityId = row.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { secretRef }),
        });
        tx.Commit();
        return row;
    }

    /// <summary>Убрать ключ (мягко): модель без ключа перестаёт быть активной.</summary>
    public void Delete(string secretRef, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE model_keys SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", Sql.ToDb(now)), ("@id", IdOf(secretRef)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ModelKeyDeleted,
            EntityType = "model_key",
            EntityId = IdOf(secretRef),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { secretRef }),
        });
        tx.Commit();
    }

    private static ModelKeyRow Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        SecretRef = r.S("secret_ref"),
        ValueEnc = r.S("value_enc"),
        UpdatedBy = r.SN("updated_by"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
