using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Аккаунты пользователей и вход в систему (ТЗ п. 2.14, гл. 12; этапы 39–40).
///
/// Аккаунт — это ЛОГИН: почта (основной идентификатор), телефон (дополнительный), имя и
/// хэш пароля. Он живёт в СЕРВЕРНОЙ БД, вне организаций (ТЗ п. 6.4.1): один и тот же
/// человек работает в нескольких организациях одним логином.
///
/// Участие в организации — это исполнитель-человек (п. 2.2) с ссылкой на аккаунт в БД той
/// организации; роль в системе тоже принадлежит исполнителю, поэтому она у каждой
/// организации своя. Сервис аккаунтов про организации не знает и связку не создаёт —
/// это делает уровень приложения, у которого есть контекст организации.
/// </summary>
public sealed class AccountService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public AccountService(Database db, EventStore events)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.account.1"), nameof(db));
        }
        _db = db;
        _events = events;
    }

    /// <summary>Все аккаунты сервера (вкладка «Пользователи»; ник и роль подставляет
    /// вызывающий — они живут у исполнителя текущей организации).</summary>
    public List<Account> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM accounts" + (includeDeleted ? "" : " WHERE deleted_at IS NULL")
                  + " ORDER BY created_at";
        return Sql.Query(conn, null, sql, Map);
    }

    public Account? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM accounts WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    /// <summary>Аккаунтов нет ни одного — система показывает экран первого старта (гл. 11).</summary>
    public bool IsEmpty()
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM accounts WHERE deleted_at IS NULL") == 0;
    }

    /// <summary>Поиск по логину: почта либо телефон (оба — уникальные идентификаторы, п. 2.14).</summary>
    public Account? FindByLogin(string login)
    {
        login = login.Trim();
        if (login.Length == 0)
        {
            return null;
        }
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM accounts
            WHERE deleted_at IS NULL AND (email=@l COLLATE NOCASE OR (phone<>'' AND phone=@l))
            LIMIT 1
            """, Map, ("@l", login)).FirstOrDefault();
    }

    /// <summary>
    /// Проверка пароля при входе (гл. 12). <paramref name="isLocal"/> — запрос пришёл с этой же
    /// машины: аккаунт с ПУСТЫМ паролем пускается только локально, иначе кластер был бы открыт
    /// всей сети (HTTPS между серверами пока нет).
    /// </summary>
    public (Account? Account, string Error) Authenticate(string login, string password, bool isLocal)
    {
        var account = FindByLogin(login);
        if (account is null)
        {
            return (null, Loc.T("msg.account.2"));
        }
        if (!account.IsActive)
        {
            return (null, Loc.T("msg.account.3"));
        }
        var hash = StoredHash(account.Id);
        if (!PasswordHash.Verify(hash, password))
        {
            return (null, Loc.T("msg.account.2"));
        }
        if (PasswordHash.IsEmpty(hash) && !isLocal)
        {
            return (null, Loc.T("msg.account.4"));
        }
        return (account, "");
    }

    /// <summary>
    /// Создать аккаунт (владелец — вкладка «Пользователи»; первый — экран первого старта).
    /// Исполнителя в организации сервис не заводит: организаций он не знает (этап 40).
    /// </summary>
    public Account Create(AccountSaveInput input, string? actorId)
    {
        var account = new Account
        {
            Name = input.Name.Trim(),
            Email = input.Email.Trim(),
            Phone = input.Phone.Trim(),
            IsActive = input.IsActive,
        };
        Validate(account, isNew: true);
        var now = DateTime.UtcNow;
        account.CreatedAt = now;
        account.UpdatedAt = now;
        var hash = PasswordHash.Hash(input.Password);
        account.HasPassword = hash.Length > 0;

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUnique(conn, tx, account);
        account.DisplayId = Database.NextDisplayId(conn, tx, "A");
        Sql.Exec(conn, tx, """
            INSERT INTO accounts (id, display_id, name, email, phone, password_hash, is_active,
                                  created_at, updated_at)
            VALUES (@id, @did, @name, @email, @phone, @hash, @active, @created, @updated)
            """,
            ("@id", account.Id), ("@did", account.DisplayId), ("@name", account.Name),
            ("@email", account.Email), ("@phone", account.Phone), ("@hash", hash),
            ("@active", account.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountCreated,
            EntityType = "account",
            EntityId = account.Id,
            PayloadJson = JsonSerializer.Serialize(new { account.DisplayId, account.Name, account.Email }),
        });
        tx.Commit();
        return account;
    }

    /// <summary>Правка аккаунта владельцем: имя, телефон, активность, сброс пароля.
    /// Почта — идентификатор, не меняется; роль живёт у исполнителя организации (п. 2.2).</summary>
    public Account Update(string id, AccountSaveInput input, string? actorId)
    {
        var account = Get(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        account.Name = input.Name.Trim();
        account.Phone = input.Phone.Trim();
        account.IsActive = input.IsActive;
        Validate(account, isNew: false);

        var now = DateTime.UtcNow;
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            EnsureUnique(conn, tx, account);
            Sql.Exec(conn, tx, """
                UPDATE accounts SET name=@name, phone=@phone, is_active=@active, updated_at=@updated
                WHERE id=@id
                """,
                ("@name", account.Name), ("@phone", account.Phone),
                ("@active", account.IsActive ? 1 : 0), ("@updated", Sql.ToDb(now)), ("@id", id));
            if (input.Password is not null)
            {
                Sql.Exec(conn, tx, "UPDATE accounts SET password_hash=@h WHERE id=@id",
                    ("@h", PasswordHash.Hash(input.Password)), ("@id", id));
                _events.Append(conn, tx, new EventRecord
                {
                    ActorId = actorId,
                    EventType = EventTypes.AccountPasswordChanged,
                    EntityType = "account",
                    EntityId = id,
                    PayloadJson = JsonSerializer.Serialize(new { reset = true, account.Email }),
                });
            }
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                EventType = EventTypes.AccountUpdated,
                EntityType = "account",
                EntityId = id,
                PayloadJson = JsonSerializer.Serialize(new { account.Name, account.Email, account.IsActive }),
            });
            tx.Commit();
        }
        account.UpdatedAt = now;
        account.HasPassword = !PasswordHash.IsEmpty(StoredHash(id));
        return account;
    }

    /// <summary>
    /// УДАЛИТЬ ПОЛЬЗОВАТЕЛЯ (T-140, вкладка «Пользователи»): пометка <c>deleted_at</c>, а не
    /// вычёркивание строки. На аккаунт ссылаются исполнители организаций и записи журнала
    /// работ — кто что сделал, должно остаться читаемым и после увольнения человека. Удалённый
    /// аккаунт не виден в списке, в систему не пускается (<see cref="FindByLogin"/> отбирает
    /// только живые) и освобождает свою почту и телефон для нового аккаунта.
    ///
    /// Удаляется только НЕАКТИВНЫЙ аккаунт: сначала владелец снимает признак «активен» — вход
    /// закрывается сразу и обратимо, — и лишь потом удаляет. Так удаление не бывает случайным
    /// нажатием, а у человека, которого «выключили по ошибке», есть время это заметить.
    /// </summary>
    /// <param name="isOwner">аккаунт — ВЛАДЕЛЕЦ системы (роль owner хотя бы в одной организации
    /// сервера): такого не удаляют, иначе управлять пользователями станет некому. Признак
    /// считает вызывающий: роли живут у исполнителей организаций, а сервис аккаунтов про
    /// организации не знает (ТЗ п. 6.4.1).</param>
    public void Delete(string id, bool isOwner, string? actorId)
    {
        var account = Get(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        var who = account.Name.Length > 0 ? account.Name : account.Email;
        if (account.DeletedAt is not null)
        {
            throw new ArgumentException(Loc.T("msg.account.6", who));
        }
        if (isOwner)
        {
            throw new ArgumentException(
                Loc.T("msg.account.7", who));
        }
        if (account.IsActive)
        {
            throw new ArgumentException(
                Loc.T("msg.account.8", who));
        }

        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE accounts SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountDeleted,
            EntityType = "account",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { account.DisplayId, account.Name, account.Email }),
        });
        tx.Commit();
        account.DeletedAt = now;
    }

    /// <summary>Правка своего аккаунта (форма в правом верхнем углу): имя и телефон.</summary>
    public Account UpdateSelf(string id, string name, string phone, string? actorId)
    {
        var account = Get(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        return Update(id, new AccountSaveInput
        {
            Name = name,
            Email = account.Email,
            Phone = phone,
            IsActive = account.IsActive,
            Password = null,
        }, actorId);
    }

    /// <summary>
    /// Смена своего пароля (три поля формы). Старый пароль обязателен — кроме случая, когда
    /// он не задан (пустой пароль допустим, гл. 11).
    /// </summary>
    public void ChangePassword(string id, string currentPassword, string newPassword, string? actorId)
    {
        var hash = StoredHash(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        if (!PasswordHash.Verify(hash, currentPassword))
        {
            throw new ArgumentException(Loc.T("msg.account.9"));
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE accounts SET password_hash=@h, updated_at=@u WHERE id=@id",
            ("@h", PasswordHash.Hash(newPassword)), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountPasswordChanged,
            EntityType = "account",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { reset = false }),
        });
        tx.Commit();
    }

    /// <summary>
    /// ПОСТАВИТЬ ПАРОЛЬ БЕЗ СТАРОГО (T-150) — ровно один случай: подключение сервера
    /// к кластеру, когда дирижёр ответил «человек с этой почтой у меня уже есть» и ПРОВЕРИЛ
    /// его пароль у себя. Пароль тогда известен и уже доказан, а здешний аккаунт — тот же
    /// человек: разъезжаться паролям незачем, иначе до первой репликации он входил бы сюда
    /// одним, а после неё (строку аккаунта пришлёт дирижёр) — другим.
    ///
    /// Смена своего пароля человеком — это <see cref="ChangePassword"/> со старым паролем,
    /// сброс владельцем — <see cref="Update"/>; сюда они не ходят.
    /// </summary>
    public void SetPassword(string id, string password, string? actorId)
    {
        _ = Get(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE accounts SET password_hash=@h, updated_at=@u WHERE id=@id",
            ("@h", PasswordHash.Hash(password)), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountPasswordChanged,
            EntityType = "account",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { reset = false, cluster = true }),
        });
        tx.Commit();
    }

    /// <summary>Записать событие входа/выхода/неудачной попытки в СЕРВЕРНЫЙ журнал (п. 6.3):
    /// вход происходит до выбора организации, поэтому в журнал организации он не попадает.</summary>
    public void LogAuth(string eventType, string? accountId, string? actorId, object payload)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "account",
            EntityId = accountId,
            PayloadJson = JsonSerializer.Serialize(payload),
        });
        tx.Commit();
    }

    /// <summary>
    /// ПОПРАВИТЬ СЕБЯ НА ПЕРВОМ СТАРТЕ (T-141): имя, ПОЧТА и телефон.
    ///
    /// Почта — идентификатор, и обычная правка её не меняет (<see cref="Update"/>): на неё
    /// уже ссылаются журналы и участия в организациях. Ровно одно исключение — визард первого
    /// старта: организаций на сервере ещё нет, аккаунт один и никуда не уезжал. Именно там
    /// почту и приходится менять: дирижёр отвечает на заявку «такая почта (или имя) у меня
    /// уже есть», и исправлять это должен подключающийся сервер.
    /// </summary>
    public Account Rename(string id, string name, string email, string phone, string? actorId)
    {
        var account = Get(id) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        account.Name = name.Trim();
        account.Email = email.Trim();
        account.Phone = phone.Trim();
        Validate(account, isNew: false);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUnique(conn, tx, account);
        Sql.Exec(conn, tx, """
            UPDATE accounts SET name=@name, email=@email, phone=@phone, updated_at=@updated
            WHERE id=@id
            """,
            ("@name", account.Name), ("@email", account.Email), ("@phone", account.Phone),
            ("@updated", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountUpdated,
            EntityType = "account",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { account.Name, account.Email, firstStart = true }),
        });
        tx.Commit();
        account.UpdatedAt = now;
        return account;
    }

    /// <summary>
    /// ХЭШ ПАРОЛЯ аккаунта как он лежит в базе (T-139): его везёт с собой заявка на
    /// подключение сервера, чтобы дирижёр завёл того же человека с ТЕМ ЖЕ паролем.
    /// Открытого пароля система не хранит нигде, и передавать тут нечего.
    /// </summary>
    public string PasswordHashOf(string id) => StoredHash(id) ?? "";

    /// <summary>
    /// ВЗЯТЬ СЕБЕ ВНУТРЕННИЙ КЛЮЧ АККАУНТА С ДИРИЖЁРА (T-148). В кластере человека опознаёт
    /// именно ключ, а почта — его логин: если дирижёр отвечает на заявку «человек с этой
    /// почтой у меня уже есть, вот его ключ», подключающийся сервер обязан перейти на этот
    /// ключ. Иначе после репликации в базе оказались бы два аккаунта с одной почтой, и какой
    /// из них спросят при входе, стало бы делом случая.
    ///
    /// Применимо РОВНО в одном месте — на первом старте, пока сервер не подключён ни к одной
    /// организации: тогда за аккаунтом здесь ничего не стоит (ни исполнителей, ни задач,
    /// ни отреплицированных строк), и смена ключа безопасна. Вызывающий это и проверяет.
    /// Пароль остаётся ЗДЕШНИЙ: чужой хэш по сети не приезжает, а после первой репликации
    /// строку аккаунта пришлёт дирижёр — и паролем станет тот, что задан у него.
    /// </summary>
    /// <returns>Аккаунт под новым ключом.</returns>
    public Account Rekey(string oldId, string newId, string? actorId)
    {
        var account = Get(oldId) ?? throw new ArgumentException(Loc.T("msg.account.5"));
        newId = newId.Trim();
        if (newId.Length == 0 || newId == oldId)
        {
            return account;
        }
        if (Get(newId) is not null)
        {
            throw new ArgumentException(Loc.T("msg.account.10", newId));
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE accounts SET id=@new, updated_at=@u WHERE id=@old",
            ("@new", newId), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@old", oldId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountUpdated,
            EntityType = "account",
            EntityId = newId,
            PayloadJson = JsonSerializer.Serialize(new { account.Email, rekeyedFrom = oldId }),
        });
        tx.Commit();
        account.Id = newId;
        return account;
    }

    /// <summary>
    /// ПРИНЯТЬ АККАУНТ С ДРУГОГО СЕРВЕРА (T-139): тот же внутренний ключ, та же почта, тот же
    /// хэш пароля. Так подтверждение заявки заводит заявителя у дирижёра, не спрашивая пароль
    /// и не выдумывая новый: в кластере у человека ОДИН аккаунт, дальше строка живёт обычной
    /// репликацией. Почта занята другим аккаунтом — отказ (уникальность логина, п. 2.14).
    /// Аккаунт с таким ключом уже есть — возвращается он, ничего не переписывая: чужой
    /// пароль правкой по сети не меняют. Если он был УДАЛЁН здесь (T-140), приём заявки —
    /// это и есть решение человека пустить его обратно, поэтому пометка удаления снимается;
    /// но только если почту за это время не занял другой живой аккаунт (п. 2.14).
    /// </summary>
    public Account Import(string id, string name, string email, string phone, string passwordHash,
        string? actorId)
    {
        if (Get(id) is { } existing)
        {
            if (existing.DeletedAt is null)
            {
                return existing;
            }
            return Restore(existing, actorId);
        }
        var account = new Account
        {
            Id = id,
            Name = name.Trim(),
            Email = email.Trim(),
            Phone = phone.Trim(),
            IsActive = true,
        };
        Validate(account, isNew: true);
        var now = DateTime.UtcNow;
        account.CreatedAt = now;
        account.UpdatedAt = now;
        account.HasPassword = !PasswordHash.IsEmpty(passwordHash);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUnique(conn, tx, account);
        account.DisplayId = Database.NextDisplayId(conn, tx, "A");
        Sql.Exec(conn, tx, """
            INSERT INTO accounts (id, display_id, name, email, phone, password_hash, is_active,
                                  created_at, updated_at)
            VALUES (@id, @did, @name, @email, @phone, @hash, 1, @created, @updated)
            """,
            ("@id", account.Id), ("@did", account.DisplayId), ("@name", account.Name),
            ("@email", account.Email), ("@phone", account.Phone), ("@hash", passwordHash),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountCreated,
            EntityType = "account",
            EntityId = account.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                account.DisplayId, account.Name, account.Email, fromCluster = true,
            }),
        });
        tx.Commit();
        return account;
    }

    /// <summary>
    /// Снять пометку удаления с аккаунта (T-140): нужно ровно одному месту — приёму заявки
    /// на подключение сервера, где человека пускают обратно в кластер (<see cref="Import"/>).
    /// Кнопки «восстановить» в интерфейсе нет: удаление задумано как окончательное действие.
    /// </summary>
    private Account Restore(Account account, string? actorId)
    {
        account.DeletedAt = null;
        account.IsActive = true;
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // почту мог занять другой живой аккаунт, пока этот был удалён — тогда отказ
        EnsureUnique(conn, tx, account);
        Sql.Exec(conn, tx,
            "UPDATE accounts SET deleted_at=NULL, is_active=1, updated_at=@u WHERE id=@id",
            ("@u", Sql.ToDb(now)), ("@id", account.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.AccountCreated,
            EntityType = "account",
            EntityId = account.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                account.DisplayId, account.Name, account.Email, restored = true, fromCluster = true,
            }),
        });
        tx.Commit();
        account.UpdatedAt = now;
        return account;
    }

    private string? StoredHash(string id)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null, "SELECT password_hash FROM accounts WHERE id=@id", ("@id", id));
    }

    private static void Validate(Account account, bool isNew)
    {
        if (account.Email.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.account.11"));
        }
        if (!account.Email.Contains('@') || account.Email.Contains(' '))
        {
            throw new ArgumentException(Loc.T("msg.account.12", account.Email));
        }
        if (isNew && account.Name.Length == 0)
        {
            account.Name = account.Email[..account.Email.IndexOf('@')];
        }
    }

    private static void EnsureUnique(SqliteConnection conn, SqliteTransaction? tx, Account account)
    {
        var emailTaken = Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM accounts WHERE email=@e COLLATE NOCASE AND id<>@id AND deleted_at IS NULL
            """, ("@e", account.Email), ("@id", account.Id)) > 0;
        if (emailTaken)
        {
            throw new ArgumentException(Loc.T("msg.account.13", account.Email));
        }
        if (account.Phone.Length > 0)
        {
            var phoneTaken = Sql.Scalar<long>(conn, tx, """
                SELECT COUNT(*) FROM accounts WHERE phone=@p AND id<>@id AND deleted_at IS NULL
                """, ("@p", account.Phone), ("@id", account.Id)) > 0;
            if (phoneTaken)
            {
                throw new ArgumentException(Loc.T("msg.account.14", account.Phone));
            }
        }
    }

    private static Account Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Email = r.S("email"),
        Phone = r.S("phone"),
        IsActive = r.B("is_active"),
        HasPassword = r.S("password_hash").Length > 0,
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}

/// <summary>Данные формы аккаунта для сервиса (сервис не знает о DTO уровня API).</summary>
public sealed class AccountSaveInput
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>null — пароль не менять; "" — снять пароль (только локальный вход).</summary>
    public string? Password { get; set; }
    /// <summary>Роль исполнителя в ТЕКУЩЕЙ организации: сервис аккаунтов её не пишет,
    /// значение использует вызывающий (у него есть контекст организации).</summary>
    public SystemRole Role { get; set; } = SystemRole.Editor;
}
