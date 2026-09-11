using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Организации и их серверы (ТЗ п. 2.15, гл. 6; этап 40).
///
/// Организация — контейнер всех рабочих данных: у неё своя БД и свой подкаталог хранилища,
/// поэтому нумерация задач/проектов/шаблонов у каждой своя, а пересечение организаций
/// возможно только через экспорт/импорт. Сама запись об организации лежит в СЕРВЕРНОЙ БД —
/// вне организаций, рядом с аккаунтами (аккаунт тоже живёт вне организации, п. 2.14).
///
/// Сервис знает только про записи. Заведение каталога, БД и справочников новой организации —
/// дело реестра организаций уровня приложения (он же запускает её фоновые процессы).
/// </summary>
public sealed class OrgService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public OrgService(Database db, EventStore events)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.org.1"), nameof(db));
        }
        _db = db;
        _events = events;
    }

    public List<Organization> List(bool includeInactive = true)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM orgs WHERE deleted_at IS NULL"
                  + (includeInactive ? "" : " AND is_active=1")
                  + " ORDER BY created_at";
        return Sql.Query(conn, null, sql, Map);
    }

    public Organization? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM orgs WHERE id=@id", Map, ("@id", id)).FirstOrDefault();
    }

    /// <summary>Организация по коду из URL (<c>/ai2p/&lt;код&gt;/…</c>); регистр не важен.</summary>
    public Organization? ByCode(string code)
    {
        code = code.Trim();
        if (code.Length == 0)
        {
            return null;
        }
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM orgs WHERE deleted_at IS NULL AND code=@c COLLATE NOCASE LIMIT 1
            """, Map, ("@c", code)).FirstOrDefault();
    }

    /// <summary>Организаций нет ни одной — система показывает экран первого старта (гл. 11).</summary>
    public bool IsEmpty()
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM orgs WHERE deleted_at IS NULL") == 0;
    }

    /// <summary>
    /// Создать организацию. Каталог, БД, справочники и связь с локальным сервером (код
    /// <c>S0</c>, он же дирижёр — ТЗ гл. 6) заводит реестр организаций: серверы живут
    /// отдельной сущностью уровня установки и связаны с организациями «многие ко многим»
    /// (<see cref="ServerService"/>, этап 41).
    /// </summary>
    public Organization Create(string name, string code, string? actorId)
    {
        var org = new Organization
        {
            Name = name.Trim(),
            Code = NormalizeCode(code.Trim().Length > 0 ? code : name),
            IsActive = true,
        };
        Validate(org);
        var now = DateTime.UtcNow;
        org.CreatedAt = now;
        org.UpdatedAt = now;

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueCode(conn, tx, org);
        org.DisplayId = NextFreeOrgId(conn, tx);
        Sql.Exec(conn, tx, """
            INSERT INTO orgs (id, display_id, name, code, is_active, created_at, updated_at)
            VALUES (@id, @did, @name, @code, 1, @created, @updated)
            """,
            ("@id", org.Id), ("@did", org.DisplayId), ("@name", org.Name), ("@code", org.Code),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.OrgCreated,
            EntityType = "org",
            EntityId = org.Id,
            PayloadJson = JsonSerializer.Serialize(new { org.DisplayId, org.Name, org.Code }),
        });
        tx.Commit();
        return org;
    }

    /// <summary>
    /// Завести организацию ЧУЖОГО дирижёра с его идентификаторами (ТЗ гл. 11, п. 11.2;
    /// этап 43). Ни собственного id, ни собственного номера <c>ORG-N</c> она здесь не
    /// получает: организация одна на кластер, и репликация должна узнавать её строку,
    /// а не заводить вторую. Уже заведена — возвращается как есть.
    /// </summary>
    public Organization Adopt(string id, string displayId, string name, string code, string? actorId)
    {
        if (id.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.org.2"));
        }
        if (Get(id) is { } existing)
        {
            return existing;
        }
        var org = new Organization
        {
            Id = id,
            DisplayId = displayId.Length > 0 ? displayId : "ORG-" + id[..Math.Min(8, id.Length)],
            Name = name.Trim().Length > 0 ? name.Trim() : code,
            Code = NormalizeCode(code),
            IsActive = true,
        };
        Validate(org);
        var now = DateTime.UtcNow;
        org.CreatedAt = now;
        org.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueCode(conn, tx, org);
        Sql.Exec(conn, tx, """
            INSERT INTO orgs (id, display_id, name, code, is_active, created_at, updated_at)
            VALUES (@id, @did, @name, @code, 1, @created, @updated)
            """,
            ("@id", org.Id), ("@did", org.DisplayId), ("@name", org.Name), ("@code", org.Code),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.OrgCreated,
            EntityType = "org",
            EntityId = org.Id,
            PayloadJson = JsonSerializer.Serialize(new { org.DisplayId, org.Name, org.Code, adopted = true }),
        });
        tx.Commit();
        return org;
    }

    /// <summary>Правка организации: название, код в URL, активность.</summary>
    public Organization Update(string id, string name, string code, bool isActive, string? actorId)
    {
        var org = Get(id) ?? throw new ArgumentException(Loc.T("msg.org.3"));
        org.Name = name.Trim();
        org.Code = NormalizeCode(code.Trim().Length > 0 ? code : name);
        org.IsActive = isActive;
        Validate(org);

        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueCode(conn, tx, org);
        Sql.Exec(conn, tx, """
            UPDATE orgs SET name=@name, code=@code, is_active=@active, updated_at=@updated WHERE id=@id
            """,
            ("@name", org.Name), ("@code", org.Code), ("@active", org.IsActive ? 1 : 0),
            ("@updated", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.OrgUpdated,
            EntityType = "org",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { org.Name, org.Code, org.IsActive }),
        });
        tx.Commit();
        org.UpdatedAt = now;
        return org;
    }

    /// <summary>
    /// ВЫДАТЬ ОРГАНИЗАЦИИ НОВЫЙ СВОБОДНЫЙ НОМЕР (T-148-S0) — починка уже случившегося
    /// столкновения <c>ORG-N</c>: до этой правки организация, заведённая на сервере,
    /// подключённом к чужому кластеру, получала номер приехавшей организации.
    ///
    /// Зовётся только у организации, дирижёр которой — МЫ (иначе номер общий на кластер
    /// и менять его нельзя), и только при столкновении: см. <c>OrgRegistry.StartAll</c>.
    /// Имя каталога на диске при этом НЕ трогается — оно уже записано в <c>org_dirs</c>,
    /// и переименовывать каталог с данными на ходу нельзя.
    /// </summary>
    /// <returns>Новый номер.</returns>
    public string Renumber(string id, string? actorId)
    {
        var org = Get(id) ?? throw new ArgumentException(Loc.T("msg.org.3"));
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var number = NextFreeOrgId(conn, tx);
        Sql.Exec(conn, tx, "UPDATE orgs SET display_id=@did, updated_at=@updated WHERE id=@id",
            ("@did", number), ("@updated", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.OrgUpdated,
            EntityType = "org",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                displayId = number, was = org.DisplayId, renumbered = true,
            }),
        });
        tx.Commit();
        return number;
    }

    /// <summary>
    /// УДАЛИТЬ ОРГАНИЗАЦИЮ С ЭТОГО СЕРВЕРА НАСОВСЕМ (T-148-S0, второй заход).
    ///
    /// Удаление НЕ мягкое и НЕ реплицируется: организация удаляется на каждом сервере
    /// по отдельности (решение заказчика). Мягкая отметка тут не годится вовсе — она
    /// оставила бы за организацией и номер <c>ORG-N</c>, и имя каталога, и связи с серверами,
    /// то есть ровно тот мусор, ради уборки которого удаление и заводится.
    ///
    /// Вычищаются ВСЕ строки серверной БД, знающие эту организацию: связи с серверами,
    /// имя каталога, состояние и очередь репликации, курсоры архивов, конфликты файлов
    /// и база сравнения файлов (у неё ключ вида <c>orgdata:&lt;id&gt;</c>). Сама БД
    /// организации и её каталог лежат на диске — их удаляет реестр организаций
    /// (<c>OrgRegistry.DeleteOrg</c>): сервис знает только про записи.
    ///
    /// Событие пишется ДО удаления строки: у события есть ссылка на сущность, и запись
    /// о том, что организация была, должна остаться в журнале сервера.
    /// </summary>
    public void Purge(string id, string? actorId)
    {
        var org = Get(id) ?? throw new ArgumentException(Loc.T("msg.org.3"));
        // ЖУРНАЛ ИЗМЕНЕНИЙ ЗАГЛУШЕН НАМЕРЕННО. Таблицы orgs и org_servers реплицируются
        // (ChangeLog.ServerTables), и без пометки «молчать» удаление уехало бы к партнёру
        // и снесло бы организацию у НЕГО — а решение принято только про этот сервер.
        using var mute = ChangeLog.Mute(_db);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.OrgDeleted,
            EntityType = "org",
            EntityId = org.Id,
            PayloadJson = JsonSerializer.Serialize(new { org.DisplayId, org.Name, org.Code }),
        });
        foreach (var sql in new[]
                 {
                     "DELETE FROM org_servers   WHERE org_id=@id",
                     "DELETE FROM repl_state    WHERE org_id=@id",
                     "DELETE FROM repl_arc_state WHERE org_id=@id",
                     "DELETE FROM repl_pending  WHERE org_id=@id",
                     "DELETE FROM file_conflicts WHERE org_id=@id",
                     "DELETE FROM file_sync     WHERE scope_key=@scope",
                     "DELETE FROM org_dirs      WHERE org_id=@id",
                     "DELETE FROM orgs          WHERE id=@id",
                 })
        {
            Sql.Exec(conn, tx, sql, ("@id", id), ("@scope", "orgdata:" + id));
        }
        tx.Commit();
    }

    /// <summary>Имя каталога организации, если оно уже выбрано; пусто — организация ни разу
    /// не открывалась. В отличие от <see cref="DirName"/> ничего не выбирает и не пишет:
    /// спрашивается перед удалением, когда заводить каталог заново незачем.</summary>
    public string KnownDirName(string orgId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null, "SELECT dir FROM org_dirs WHERE org_id=@id",
            ("@id", orgId)) ?? "";
    }

    /// <summary>
    /// Имя каталога организации НА ЭТОМ компьютере (ТЗ п. 6.4.4, этап 43). Выбирается один
    /// раз и запоминается: внешний код (<c>ORG-1</c>) для этого больше не годится — он
    /// реплицируется, и после подключения к чужой организации своя <c>ORG-1</c> и приехавшая
    /// <c>ORG-1</c> указали бы на один и тот же каталог с одной и той же базой.
    /// </summary>
    public string DirName(Organization org)
    {
        using var conn = _db.Open();
        var known = Sql.Scalar<string>(conn, null, "SELECT dir FROM org_dirs WHERE org_id=@id",
            ("@id", org.Id));
        if (known is { Length: > 0 })
        {
            return known;
        }
        var wanted = org.DisplayId.Length > 0 ? org.DisplayId : "ORG-" + org.Id[..8];
        var name = wanted;
        for (var n = 2; Taken(conn, name); n++)
        {
            name = $"{wanted}-{n}";
        }
        Sql.Exec(conn, null, "INSERT OR REPLACE INTO org_dirs(org_id, dir) VALUES (@id, @d)",
            ("@id", org.Id), ("@d", name));
        return name;
    }

    /// <summary>
    /// НОМЕР НОВОЙ ОРГАНИЗАЦИИ (T-148-S0): следующий свободный <c>ORG-N</c> на ЭТОЙ установке.
    ///
    /// Счётчика мало: организация чужого дирижёра приезжает сюда со СВОИМ номером
    /// (<see cref="Adopt"/>), а он счётчик не двигает — номер выдан на другом компьютере.
    /// Поэтому сервер, подключённый к чужой <c>ORG-1</c>, выдавал своей первой организации
    /// ту же <c>ORG-1</c>: в списке организаций два одинаковых номера, а каталог второй
    /// получал приставку (<c>ORG-1-2</c>, см. <see cref="DirName"/>) — по нему и было видно,
    /// что номера столкнулись.
    ///
    /// Занятыми считаются номера ВСЕХ строк, включая удалённые, и уже выбранные имена
    /// каталогов: каталог с данными удалённой организации остаётся на диске, и отдавать
    /// её номер новой нельзя.
    /// </summary>
    private static string NextFreeOrgId(SqliteConnection conn, SqliteTransaction tx)
    {
        for (var guard = 0; guard < 1000; guard++)
        {
            var candidate = Database.NextDisplayId(conn, tx, "ORG");
            var used = Sql.Scalar<long>(conn, tx,
                "SELECT COUNT(*) FROM orgs WHERE display_id=@d COLLATE NOCASE",
                ("@d", candidate)) > 0
                || Sql.Scalar<long>(conn, tx,
                    "SELECT COUNT(*) FROM org_dirs WHERE dir=@d COLLATE NOCASE",
                    ("@d", candidate)) > 0;
            if (!used)
            {
                return candidate;
            }
        }
        // сюда не попасть: счётчик растёт с каждым кругом. Но молча выдать занятый номер
        // хуже, чем некрасивый — уникальность важнее вида
        return "ORG-" + Guid.NewGuid().ToString("N")[..8];
    }

    private static bool Taken(SqliteConnection conn, string dir) =>
        Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM org_dirs WHERE dir=@d COLLATE NOCASE",
            ("@d", dir)) > 0;

    /// <summary>Код организации для URL — правило нормализации в <see cref="Organization"/>
    /// (его же применяет UI, подсказывая код по названию).</summary>
    public static string NormalizeCode(string source) => Organization.NormalizeCode(source);

    /// <summary>Коды, занятые собственными маршрутами приложения: организация с таким кодом
    /// сделала бы недоступной страницу входа или API (ТЗ гл. 11).</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "login", "logout", "server-admin", "_blazor", "_framework", "_content", "task",
    };

    private static void Validate(Organization org)
    {
        if (org.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.org.4"));
        }
        if (Reserved.Contains(org.Code))
        {
            throw new ArgumentException(Loc.T("msg.org.5", org.Code));
        }
    }

    private static void EnsureUniqueCode(SqliteConnection conn, SqliteTransaction? tx, Organization org)
    {
        var taken = Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM orgs WHERE code=@c COLLATE NOCASE AND id<>@id AND deleted_at IS NULL
            """, ("@c", org.Code), ("@id", org.Id)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.org.6", org.Code));
        }
    }

    private static Organization Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Code = r.S("code"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
