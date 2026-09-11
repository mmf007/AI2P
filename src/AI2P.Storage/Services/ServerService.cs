using AI2P.Core;
using System.Security.Cryptography;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Серверы кластера и их связь с организациями (ТЗ п. 2.15, гл. 6; этап 41).
///
/// Сервер — сущность уровня УСТАНОВКИ: с организациями он связан «многие ко многим», то есть
/// один сервер обслуживает несколько организаций, а организация живёт на нескольких серверах.
/// Код в организации (<c>S0</c>, <c>S1</c>, …) и признак дирижёра принадлежат именно связи:
/// сервер бывает дирижёром одной организации и обычным участником другой.
///
/// Записи лежат в СЕРВЕРНОЙ БД (ТЗ п. 6.4.1) — вне организаций, рядом с аккаунтами.
/// Адрес и каталоги ЛОКАЛЬНОГО сервера здесь только отражаются: правятся они в config.json,
/// потому что от них зависит сам запуск приложения и исправлять их нужно уметь снаружи
/// системы — например, когда порт занят и сервер не поднимается (ТЗ гл. 10).
///
/// Узнают друг друга серверы по внутреннему ключу (<c>id</c>, unid), а не по адресу: адрес
/// меняется, а ключ репликации должен оставаться прежним (todo41 п. 6).
/// </summary>
public sealed class ServerService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public ServerService(Database db, EventStore events)
    {
        if (db.Kind != DatabaseKind.Server)
        {
            throw new ArgumentException(Loc.T("msg.server.1"), nameof(db));
        }
        _db = db;
        _events = events;
    }

    // --- серверы ---

    /// <summary>Все серверы установки; ЛОКАЛЬНЫЙ ВСЕГДА ПЕРВЫЙ (todo41 п. 2), остальные —
    /// в порядке добавления. Организации сервера подставляются в <see cref="ServerNode.Orgs"/>.</summary>
    public List<ServerNode> List(bool includeInactive = true)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM servers WHERE deleted_at IS NULL"
                  + (includeInactive ? "" : " AND is_active=1")
                  + " ORDER BY is_local DESC, created_at";
        var servers = Sql.Query(conn, null, sql, Map);
        var links = Sql.Query(conn, null, LinkSelect + " WHERE l.deleted_at IS NULL", MapLink);
        foreach (var server in servers)
        {
            server.Orgs = links.Where(l => l.ServerId == server.Id).ToList();
        }
        return servers;
    }

    public ServerNode? Get(string id)
    {
        using var conn = _db.Open();
        var server = Sql.Query(conn, null, "SELECT * FROM servers WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (server is not null)
        {
            server.Orgs = Sql.Query(conn, null,
                LinkSelect + " WHERE l.deleted_at IS NULL AND l.server_id=@s", MapLink, ("@s", id));
        }
        return server;
    }

    /// <summary>Локальный сервер: он есть всегда и в списке идёт первым.</summary>
    public ServerNode? Local()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM servers WHERE is_local=1 AND deleted_at IS NULL LIMIT 1",
            Map).FirstOrDefault();
    }

    /// <summary>Сервер по токену доступа сервер-сервер (заголовок запроса кластера, гл. 12).</summary>
    public ServerNode? ByToken(string token)
    {
        if (token.Trim().Length == 0)
        {
            return null;
        }
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM servers WHERE token=@t AND token<>'' AND is_active=1 AND deleted_at IS NULL LIMIT 1
            """, Map, ("@t", token)).FirstOrDefault();
    }

    /// <summary>
    /// Завести (или обновить) запись о ЛОКАЛЬНОМ сервере по настройкам config.json.
    /// Вызывается при каждом старте: адрес сервера мог смениться, а его внутренний ключ —
    /// нет, поэтому связи с организациями и токены переживают правку настроек.
    /// </summary>
    /// <param name="hostname2">Второй (внешний) адрес из <c>config.json</c>; пусто — его нет
    /// (T-50-S0). Он лежит в записи сервера и реплицируется вместе с ней: соседям надо знать,
    /// по какому адресу нас звать, когда внутренний из их сети не виден.</param>
    /// <param name="port2">Порт второго адреса; <c>null</c> — тот же, что основной.</param>
    public ServerNode EnsureLocal(string name, string protocol, string hostname, int port,
        string basePath, string hostname2 = "", int? port2 = null)
    {
        hostname2 = (hostname2 ?? "").Trim();
        port2 = hostname2.Length > 0 ? port2 : null;
        var local = Local();
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        if (local is null)
        {
            local = new ServerNode
            {
                // внутренний ключ сервера = идентификатор узла (ТЗ гл. 6, этап 43): установка
                // на компьютере одна, и репликации нужно, чтобы автор изменения в журнале
                // и сервер, которому его не надо возвращать, назывались одинаково
                Id = _db.NodeId.Length > 0 ? _db.NodeId : Guid.NewGuid().ToString(),
                Name = name,
                Protocol = protocol,
                Hostname = hostname,
                Port = port,
                Hostname2 = hostname2,
                Port2 = port2,
                BasePath = basePath,
                IsLocal = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            using var tx = conn.BeginTransaction();
            local.DisplayId = Database.NextDisplayId(conn, tx, "SRV");
            Insert(conn, tx, local);
            tx.Commit();
            DropCache();
            return local;
        }
        if (local.Protocol == protocol && local.Hostname == hostname && local.Port == port
            && local.BasePath == basePath && local.Hostname2 == hostname2 && local.Port2 == port2
            && (local.Name.Length > 0 || name.Length == 0))
        {
            return local;
        }
        // имя, ВЫВЕДЕННОЕ из прежнего адреса, едет за адресом (T-139): до экрана первого
        // старта с именем сервера запись заводилась как «localhost», и после ввода
        // настоящего имени в списке серверов кластера так и оставалось бы «localhost»
        var derived = local.Name.Length == 0
                      || string.Equals(local.Name, local.Hostname, StringComparison.OrdinalIgnoreCase);
        local.Protocol = protocol;
        local.Hostname = hostname;
        local.Port = port;
        local.Hostname2 = hostname2;
        local.Port2 = port2;
        local.BasePath = basePath;
        if (derived && name.Length > 0)
        {
            local.Name = name;
        }
        Sql.Exec(conn, null, """
            UPDATE servers SET name=@n, protocol=@pr, hostname=@h, port=@p, hostname2=@h2, port2=@p2,
                               base_path=@bp, updated_at=@u
            WHERE id=@id
            """,
            ("@n", local.Name), ("@pr", protocol), ("@h", hostname), ("@p", port),
            ("@h2", hostname2), ("@p2", port2), ("@bp", basePath),
            ("@u", Sql.ToDb(now)), ("@id", local.Id));
        local.UpdatedAt = now;
        DropCache();
        return local;
    }

    /// <summary>
    /// Добавить сервер кластера (форма «остальных» серверов: протокол, имя хоста, порт,
    /// активность — todo41 п. 5). <paramref name="id"/> задаётся, когда сервер сам представился
    /// своим внутренним ключом при подключении; иначе ключ выдаётся здесь и заменяется
    /// настоящим после первого обмена.
    /// </summary>
    public ServerNode Create(ServerSaveInput input, string? actorId, string? id = null)
    {
        var server = new ServerNode
        {
            Id = id is { Length: > 0 } ? id : Guid.NewGuid().ToString(),
            Name = input.Name.Trim(),
            Protocol = Normalize(input.Protocol),
            Hostname = input.Hostname.Trim(),
            Port = input.Port,
            Hostname2 = input.Hostname2.Trim(),
            Port2 = input.Hostname2.Trim().Length > 0 ? input.Port2 : null,
            BasePath = NormalizeBasePath(input.BasePath),
            IsActive = input.IsActive,
        };
        Validate(server);
        var now = DateTime.UtcNow;
        server.CreatedAt = now;
        server.UpdatedAt = now;

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        server.DisplayId = Database.NextDisplayId(conn, tx, "SRV");
        Insert(conn, tx, server);
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerCreated,
            EntityType = "server",
            EntityId = server.Id,
            PayloadJson = JsonSerializer.Serialize(new { server.DisplayId, server.Name, server.Url }),
        });
        tx.Commit();
        DropCache();
        return server;
    }

    /// <summary>Правка удалённого сервера: протокол, имя хоста, порт, активность (todo41 п. 5).
    /// Внешний код сервера в организации отсюда не меняется — его выдаёт дирижёр.</summary>
    public ServerNode Update(string id, ServerSaveInput input, string? actorId)
    {
        var server = Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
        if (server.IsLocal)
        {
            throw new ArgumentException(
                Loc.T("msg.server.2"));
        }
        server.Name = input.Name.Trim();
        server.Protocol = Normalize(input.Protocol);
        server.Hostname = input.Hostname.Trim();
        server.Port = input.Port;
        // второй (внешний) адрес чужого сервера обычно приезжает репликацией от него самого,
        // но задать его руками можно и здесь — например, пока обмена ещё не было (T-50-S0)
        server.Hostname2 = input.Hostname2.Trim();
        server.Port2 = server.Hostname2.Length > 0 ? input.Port2 : null;
        server.BasePath = NormalizeBasePath(input.BasePath);
        server.IsActive = input.IsActive;
        Validate(server);

        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE servers SET name=@n, protocol=@pr, hostname=@h, port=@p, hostname2=@h2, port2=@p2,
                               base_path=@bp, is_active=@a, updated_at=@u
            WHERE id=@id
            """,
            ("@n", server.Name), ("@pr", server.Protocol), ("@h", server.Hostname),
            ("@p", server.Port), ("@h2", server.Hostname2), ("@p2", server.Port2),
            ("@bp", server.BasePath), ("@a", server.IsActive ? 1 : 0),
            ("@u", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerUpdated,
            EntityType = "server",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { server.Name, server.Url, server.IsActive }),
        });
        tx.Commit();
        DropCache();
        server.UpdatedAt = now;
        return server;
    }

    // --- удаление сервера из кластера (T-141) ---

    /// <summary>
    /// Почему запись сервера удалить нельзя; null — можно (T-141).
    ///
    /// Удаление разрешено, только пока с сервером НЕ БЫЛО НИ ОДНОЙ УДАЧНОЙ РЕПЛИКАЦИИ:
    /// до неё запись никуда не уезжала и ни на что не ссылается — её можно вычеркнуть
    /// начисто, вместе с выданным кодом. После первого же сеанса у него появляются свои
    /// строки (задачи, файлы) с его кодом в номере, и «удалять» такой сервер можно только
    /// снятием активности плюс передачей задач дирижёру (ТЗ гл. 6, этап 42).
    /// </summary>
    public string? DeleteProblem(string id)
    {
        var server = Get(id);
        if (server is null)
        {
            return Loc.T("msg.apiEndpoints.5");
        }
        if (server.IsLocal)
        {
            return Loc.T("msg.server.3");
        }
        if (server.Orgs.Any(l => l.IsConductor && l.Status == OrgServerStatus.Active))
        {
            return Loc.T("msg.server.4");
        }
        using var conn = _db.Open();
        var replicated = Sql.Scalar<long>(conn, null, """
            SELECT COUNT(*) FROM repl_state WHERE server_id=@id AND last_ok_at IS NOT NULL
            """, ("@id", id)) > 0;
        return replicated
            ? Loc.T("msg.server.5")
            : null;
    }

    /// <summary>
    /// УДАЛИТЬ СЕРВЕР ИЗ СПИСКА (T-141): запись вычёркивается НАЧИСТО, вместе со своими
    /// связями с организациями и состоянием репликации. Не пометка удаления, а именно
    /// удаление строки: иначе выданный серверу код (S1) остался бы занятым навсегда —
    /// код не переиспользуется, пока связь существует хоть в каком-то виде. Освободить его
    /// и нужно: неудачное первое подключение не должно съедать номер, а при повторной
    /// попытке появлялся бы второй такой же сервер с новым кодом.
    ///
    /// Разрешено только тому, у кого <see cref="DeleteProblem"/> вернул null.
    /// </summary>
    public void Delete(string id, string? actorId)
    {
        if (DeleteProblem(id) is { } problem)
        {
            throw new ArgumentException(problem);
        }
        var server = Get(id)!;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // событие ДО удаления строк: в журнале должно остаться, что и кем было убрано (п. 6.4.3)
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerDeleted,
            EntityType = "server",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                server.DisplayId, server.Name, server.Url,
                codes = server.Orgs.Select(l => l.Code).ToList(),
            }),
        });
        Sql.Exec(conn, tx, "DELETE FROM repl_state WHERE server_id=@id", ("@id", id));
        Sql.Exec(conn, tx, "DELETE FROM org_servers WHERE server_id=@id", ("@id", id));
        Sql.Exec(conn, tx, "DELETE FROM servers WHERE id=@id", ("@id", id));
        tx.Commit();
        DropCache();
    }

    // --- токены доступа сервер-сервер (ТЗ гл. 12) ---

    /// <summary>Выдать серверу токен, которым он будет ходить к нам; уже выданный — вернуть.</summary>
    public string IssueToken(string serverId)
    {
        using var conn = _db.Open();
        var existing = Sql.Scalar<string>(conn, null, "SELECT token FROM servers WHERE id=@id", ("@id", serverId));
        if (existing is { Length: > 0 })
        {
            return existing;
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Sql.Exec(conn, null, "UPDATE servers SET token=@t, updated_at=@u WHERE id=@id",
            ("@t", token), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", serverId));
        return token;
    }

    /// <summary>Токен, которым МЫ ходим к этому серверу; пусто — сервер нам его не выдавал
    /// (значит, репликация с ним ещё невозможна).</summary>
    public string PeerToken(string serverId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null, "SELECT peer_token FROM servers WHERE id=@id",
            ("@id", serverId)) ?? "";
    }

    /// <summary>Запомнить токен, которым МЫ ходим к этому серверу (выдан им при подключении).</summary>
    public void SetPeerToken(string serverId, string token)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE servers SET peer_token=@t, updated_at=@u WHERE id=@id",
            ("@t", token), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", serverId));
    }

    /// <summary>Состояние исходящей заявки на подключение (двусторонний обмен, todo41 п. 7).
    /// Заявок к одному дирижёру может быть несколько — по одной на организацию (T-141):
    /// коды и ссылки хранятся списком через запятую, см. <see cref="AddJoin"/>.</summary>
    public void SetJoin(string serverId, string status, string orgCode, string reference)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            UPDATE servers SET join_status=@s, join_org=@o, join_ref=@r, updated_at=@u WHERE id=@id
            """,
            ("@s", status), ("@o", orgCode), ("@r", reference), ("@u", Sql.ToDb(DateTime.UtcNow)),
            ("@id", serverId));
    }

    /// <summary>
    /// ЕЩЁ ОДНА ЗАЯВКА К ТОМУ ЖЕ ДИРИЖЁРУ (T-141): организаций у него бывает несколько,
    /// и выбор в форме подключения МНОЖЕСТВЕННЫЙ — сервер вправе работать сразу в нескольких
    /// (ТЗ п. 2.15). Заявка на каждую своя, поэтому её ссылка добавляется к уже поданным,
    /// а не заменяет их: иначе решение по первой организации потерялось бы.
    /// </summary>
    public void AddJoin(string serverId, string status, string orgCode, string reference)
    {
        var server = Get(serverId);
        var refs = Split(server?.JoinRef);
        var codes = Split(server?.JoinOrgCode);
        if (reference.Trim().Length > 0 && !refs.Contains(reference.Trim()))
        {
            refs.Add(reference.Trim());
            codes.Add(orgCode.Trim());
        }
        SetJoin(serverId, status, string.Join(",", codes), string.Join(",", refs));
    }

    /// <summary>Список через запятую (коды организаций и ссылки на заявки, T-141).</summary>
    public static List<string> Split(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    // --- интервалы репликации (ТЗ гл. 6, этап 43) ---

    /// <summary>Наименьший интервал автоматической репликации, секунды (todo43).</summary>
    public const int MinReplIntervalSec = 30;

    /// <summary>Наименьший интервал повтора после ошибки, секунды (todo43).</summary>
    public const int MinReplRetrySec = 15;

    /// <summary>
    /// Интервал репликации ПОДКЛЮЧЁННОГО сервера по умолчанию, секунды (T-153).
    ///
    /// Пустой интервал означает «только по кнопке», и у новой записи он именно пустой: сервер
    /// подключался к организации и оставался стоять — репликация не начиналась сама никогда,
    /// а догадаться, что её надо ещё и включить в форме сервера, человеку неоткуда. Поэтому
    /// подключение задаёт интервал само; поменять или убрать его можно там же, в форме.
    /// </summary>
    public const int JoinedReplIntervalSec = 300;

    /// <summary>
    /// Задать интервал репликации, ЕСЛИ он ещё не задан (T-153): подключение к организации
    /// включает автоматические сеансы, но заданное человеком не трогает. Значение принадлежит
    /// записи рядового сервера, и его пишут обе стороны пары — дирижёр при подтверждении
    /// заявки, подключившийся у себя (ТЗ гл. 6).
    /// </summary>
    public ServerNode? EnsureReplication(string serverId, string? actorId) =>
        Get(serverId) is { ReplIntervalSec: null } server
            ? SetReplication(server.Id, JoinedReplIntervalSec, server.ReplRetrySec, actorId)
            : Get(serverId);

    /// <summary>
    /// Задать интервалы репликации сервера (ТЗ гл. 6, этап 43): пусто — автоматически
    /// не реплицировать, только по кнопке. Отсчёт ведут оба конца пары, поэтому значение
    /// принадлежит записи сервера и реплицируется — на дирижёре его можно задать всем
    /// серверам, на остальных только своему.
    /// </summary>
    public ServerNode SetReplication(string serverId, int? intervalSec, int? retrySec, string? actorId)
    {
        var server = Get(serverId) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
        server.ReplIntervalSec = NormalizeInterval(intervalSec, MinReplIntervalSec, Loc.T("msg.server.6"));
        server.ReplRetrySec = NormalizeInterval(retrySec, MinReplRetrySec, Loc.T("msg.server.7"));
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE servers SET repl_interval_sec=@i, repl_retry_sec=@r, updated_at=@u WHERE id=@id
            """,
            ("@i", server.ReplIntervalSec), ("@r", server.ReplRetrySec),
            ("@u", Sql.ToDb(now)), ("@id", serverId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerUpdated,
            EntityType = "server",
            EntityId = serverId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                server.Name, server.ReplIntervalSec, server.ReplRetrySec,
            }),
        });
        tx.Commit();
        server.UpdatedAt = now;
        return server;
    }

    private static int? NormalizeInterval(int? value, int minimum, string what)
    {
        if (value is not { } seconds || seconds <= 0)
        {
            return null;   // пусто — автоматической репликации нет, только ручная
        }
        if (seconds < minimum)
        {
            throw new ArgumentException(
                Loc.T("msg.server.8", what, minimum));
        }
        return seconds;
    }

    /// <summary>
    /// Последняя ошибка связи с сервером — показывается в списке серверов и внизу его формы.
    ///
    /// ТО ЖЕ САМОЕ ЗНАЧЕНИЕ НЕ ПЕРЕЗАПИСЫВАЕТСЯ (T-284): строка <c>servers</c> реплицируется,
    /// и любая её правка ложится записью в журнал изменений — даже такая, из которой партнёру
    /// не уедет ни одной колонки (<c>last_error</c> вырезается как местная). Пока условия не
    /// было, каждый неудачный заход к выключенному партнёру и каждый удачный сеанс дописывали
    /// в журнал по строке — вечный лишний трафик на ровном месте (ср. сид справочников T-227).
    /// </summary>
    public void SetLastError(string serverId, string error)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null,
            "UPDATE servers SET last_error=@e, updated_at=@u WHERE id=@id AND last_error<>@e",
            ("@e", error), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", serverId));
    }

    /// <summary>Заменить внутренний ключ сервера настоящим (сервер представился при обмене):
    /// связи и токены переносятся на новый ключ. Ключ уже известен — ничего не делаем.</summary>
    public string AdoptId(string serverId, string realId)
    {
        if (realId.Trim().Length == 0 || realId == serverId)
        {
            return serverId;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var taken = Sql.Scalar<long>(conn, tx, "SELECT COUNT(*) FROM servers WHERE id=@id", ("@id", realId)) > 0;
        if (taken)
        {
            // такой сервер уже заведён: переносить нечего, а дубль убираем из списка
            Sql.Exec(conn, tx, "UPDATE org_servers SET server_id=@real WHERE server_id=@old",
                ("@real", realId), ("@old", serverId));
            Sql.Exec(conn, tx, "DELETE FROM servers WHERE id=@old", ("@old", serverId));
            tx.Commit();
            DropCache();
            return realId;
        }
        // FK org_servers → servers: сначала новая запись, потом перенос связей, потом старая
        Sql.Exec(conn, tx, """
            INSERT INTO servers (id, display_id, name, protocol, hostname, port, hostname2, port2,
                                 base_path, is_local,
                                 is_active, token, peer_token, join_status, join_org, join_ref,
                                 last_error, repl_interval_sec, repl_retry_sec, created_at, updated_at)
            SELECT @real, display_id, name, protocol, hostname, port, hostname2, port2,
                   base_path, is_local, is_active,
                   token, peer_token, join_status, join_org, join_ref, last_error,
                   repl_interval_sec, repl_retry_sec, created_at, updated_at
            FROM servers WHERE id=@old
            """, ("@real", realId), ("@old", serverId));
        Sql.Exec(conn, tx, "UPDATE org_servers SET server_id=@real WHERE server_id=@old",
            ("@real", realId), ("@old", serverId));
        Sql.Exec(conn, tx, "DELETE FROM servers WHERE id=@old", ("@old", serverId));
        tx.Commit();
        DropCache();
        return realId;
    }

    // --- связь «организация ↔ сервер» ---

    /// <summary>Серверы организации (форма организации, todo41 п. 11): подтверждённые связи
    /// и заявки — заявки идут последними, отдельной строкой (пп. 8–9).</summary>
    public List<OrgServer> ServersOf(string orgId, bool includeRequests = true)
    {
        using var conn = _db.Open();
        var links = Sql.Query(conn, null,
            LinkSelect + " WHERE l.deleted_at IS NULL AND l.org_id=@o", MapLink, ("@o", orgId));
        return Sorted(links, includeRequests);
    }

    /// <summary>
    /// Ключи ВСЕХ серверов, когда-либо связанных с организацией, — включая мягко удалённые
    /// связи: отвязанный сервер (<see cref="Detach"/>) и отклонённую заявку
    /// (<see cref="RejectRequest"/>).
    ///
    /// Нужно это репликации (T-20-S1). Строка связи <c>org_servers</c> ссылается на
    /// <c>servers</c> внешним ключом, и партнёру связь отдаётся по организации — ЛЮБАЯ, в том
    /// числе мягко удалённая (иначе он никогда не узнал бы об отвязке). А запись самого сервера
    /// отбиралась по <see cref="ServersOf"/>, где удалённых связей нет: ребёнок уезжал без
    /// родителя, и у партнёра строка навсегда оставалась в очереди повтора с «FOREIGN KEY
    /// constraint failed» — ровно та жалоба, с которой началась задача.
    /// </summary>
    public List<string> LinkedServerIds(string orgId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT DISTINCT server_id FROM org_servers WHERE org_id=@o", r => r.S("server_id"),
            ("@o", orgId));
    }

    /// <summary>
    /// КОД, ПОД КОТОРЫМ СЕРВЕР УЖЕ ИЗВЕСТЕН В КЛАСТЕРЕ (T-148-S0): его код в другой
    /// организации этой установки, если он там свободен в <paramref name="orgId"/>.
    /// Пусто — сервер нигде не связан либо код занят, и тогда его выдаёт правило по умолчанию
    /// (<c>S0</c> первому серверу организации).
    ///
    /// Нужно это при заведении ВТОРОЙ организации на компьютере, который в первой значится,
    /// скажем, <c>S1</c>: без этого он получал бы в новой организации <c>S0</c> — код,
    /// которым в кластере зовут ДРУГУЮ машину, — и человек в списке организаций видел бы,
    /// что новая организация «уехала на соседний сервер». Внутри организации код по-прежнему
    /// уникален, а <c>S0</c> не значит «дирижёр»: дирижёрство — отдельный признак связи.
    /// </summary>
    public string KnownCodeOf(string serverId, string orgId)
    {
        var known = ClusterCodeOf(serverId);
        if (known.Length == 0)
        {
            return "";
        }
        using var conn = _db.Open();
        var taken = Sql.Scalar<long>(conn, null, """
            SELECT COUNT(*) FROM org_servers
            WHERE org_id=@o AND code=@c COLLATE NOCASE AND server_id<>@s AND deleted_at IS NULL
            """, ("@o", orgId), ("@c", known), ("@s", serverId)) > 0;
        return taken ? "" : known;
    }

    /// <summary>
    /// КОД, КОТОРЫМ ЭТОТ КОМПЬЮТЕР ЗОВУТ В КЛАСТЕРЕ (T-148-S0, второй заход) — независимо от
    /// конкретной организации; пусто — сервер нигде не связан.
    ///
    /// Спрашивается сперва у организаций, где серверов НЕСКОЛЬКО: там код выдал дирижёр
    /// кластера, и это единственное настоящее свидетельство. Организация-одиночка
    /// свидетельствовать не может — её код мы выдали себе сами, и если он ошибочный
    /// (<c>S0</c> на машине, которую все зовут <c>S1</c>), то по прежнему правилу «первая
    /// попавшаяся связь» ошибка размножалась бы на каждую следующую организацию.
    /// </summary>
    public string ClusterCodeOf(string serverId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT l.code AS code FROM org_servers l
            WHERE l.server_id=@s AND l.code<>'' AND l.deleted_at IS NULL
            ORDER BY (SELECT COUNT(*) FROM org_servers p
                      WHERE p.org_id = l.org_id AND p.deleted_at IS NULL) DESC, l.created_at
            LIMIT 1
            """, r => r.S("code"), ("@s", serverId)).FirstOrDefault() ?? "";
    }

    /// <summary>
    /// СМЕНИТЬ КОД СЕРВЕРА В ОРГАНИЗАЦИИ (T-148-S0, второй заход). Обычно код выдаёт дирижёр
    /// один раз и навсегда; отсюда он меняется только починкой уже сложившейся установки —
    /// когда организация, заведённая на этом компьютере, записала его под чужим кодом
    /// (см. <c>OrgRegistry.FixLocalServerCodes</c>). Занятый в организации код не ставится:
    /// код в ней уникален.
    /// </summary>
    public OrgServer SetCode(string orgId, string serverId, string code, string? actorId)
    {
        var link = Link(orgId, serverId) ?? throw new ArgumentException(Loc.T("msg.server.9"));
        code = code.Trim();
        if (code.Length == 0 || code == link.Code)
        {
            return link;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var taken = Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM org_servers
            WHERE org_id=@o AND code=@c COLLATE NOCASE AND id<>@id AND deleted_at IS NULL
            """, ("@o", orgId), ("@c", code), ("@id", link.Id)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.server.12", code));
        }
        var now = DateTime.UtcNow;
        Sql.Exec(conn, tx, "UPDATE org_servers SET code=@c, updated_at=@u WHERE id=@id",
            ("@c", code), ("@u", Sql.ToDb(now)), ("@id", link.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerLinked,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new { orgId, serverId, code, was = link.Code }),
        });
        tx.Commit();
        DropCache();
        link.Code = code;
        link.UpdatedAt = now;
        return link;
    }

    /// <summary>Организации сервера (форма сервера, todo41 п. 11).</summary>
    public List<OrgServer> OrgsOf(string serverId, bool includeRequests = true)
    {
        using var conn = _db.Open();
        var links = Sql.Query(conn, null,
            LinkSelect + " WHERE l.deleted_at IS NULL AND l.server_id=@s", MapLink, ("@s", serverId));
        return Sorted(links, includeRequests);
    }

    /// <summary>
    /// Заявки на подключение, ждущие решения человека (внизу списка серверов, п. 8).
    ///
    /// <paramref name="all"/> — «ПОКАЗАТЬ ВСЕ ЗАЯВКИ» (T-293): к ждущим добавляются решённые,
    /// то есть ОТКЛОНЁННЫЕ — их <see cref="RejectRequest"/> ещё и мягко удаляет, поэтому
    /// обычный список их не видит вовсе. Принятая заявка (<c>active</c>) сюда не попадает
    /// ни при каком отборе: она и есть строка сервера в том же списке, и второй строкой
    /// была бы просто двойником.
    /// </summary>
    public List<OrgServer> Requests(bool all = false)
    {
        using var conn = _db.Open();
        return all
            ? Sql.Query(conn, null, LinkSelect + """
                 WHERE l.status IN ('pending','deferred','rejected')
                 ORDER BY l.created_at
                """, MapLink)
            : Sql.Query(conn, null, LinkSelect + """
                 WHERE l.deleted_at IS NULL AND l.status IN ('pending','deferred')
                 ORDER BY l.created_at
                """, MapLink);
    }

    public OrgServer? Link(string orgId, string serverId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            LinkSelect + " WHERE l.deleted_at IS NULL AND l.org_id=@o AND l.server_id=@s",
            MapLink, ("@o", orgId), ("@s", serverId)).FirstOrDefault();
    }

    public OrgServer? LinkById(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, LinkSelect + " WHERE l.id=@id", MapLink, ("@id", id)).FirstOrDefault();
    }

    /// <summary>
    /// Связать сервер с организацией. Код выдаётся дирижёром и не переиспользуется; первый
    /// сервер организации получает <c>S0</c> и становится её дирижёром — дирижёр обязателен
    /// и всегда ровно один (ТЗ гл. 6). Связь уже есть — возвращается она же.
    /// </summary>
    /// <param name="id">Идентификатор связи; задаётся при подключении к чужой организации —
    /// связь должна называться там и здесь одинаково, иначе репликация задвоила бы её
    /// и упёрлась в <c>UNIQUE(org_id, server_id)</c> (ТЗ гл. 11, п. 11.2; этап 43).</param>
    /// <param name="isConductor">Явно назначить дирижёром: при подключении к чужой
    /// организации дирижёр известен — это тот сервер, к которому подключились.</param>
    /// <param name="applicant">Человек, подавший заявку (T-139): заявка анонимная и называет
    /// его сама, а подтверждение делает его участником организации.</param>
    public OrgServer Attach(string orgId, string serverId, string status, string requestedBy,
        string? actorId, string? code = null, string? id = null, bool? isConductor = null,
        JoinApplicant? applicant = null)
    {
        if (Link(orgId, serverId) is { } existing)
        {
            return existing;
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // СВЯЗЬ УЖЕ БЫЛА, И ЕЁ СНЯЛИ (T-175-S0). Отвязка (<see cref="Detach"/>) и отказ по
        // заявке (<see cref="RejectRequest"/>) удаляют связь МЯГКО: строка остаётся, иначе
        // партнёр никогда не узнал бы об отвязке. А UNIQUE(org_id, server_id) считает и её —
        // и попытка связать тот же сервер заново упиралась в «UNIQUE constraint failed»,
        // то есть 500. Ломались сразу оба пути подключения: галочка сервера в форме
        // организации на дирижёре и заявка на подключение с самого сервера, — и НАВСЕГДА:
        // снятую однажды галочку вернуть было нечем, а отклонённый сервер не мог подать
        // заявку никогда. Поэтому снятую связь ВОЗВРАЩАЕМ к жизни, а не заводим вторую.
        if (Revive(conn, tx, orgId, serverId, status, requestedBy, actorId, code, id, isConductor,
                applicant, now) is { } revived)
        {
            return revived;
        }
        var link = new OrgServer
        {
            Id = id is { Length: > 0 } ? id : Guid.NewGuid().ToString(),
            OrgId = orgId,
            ServerId = serverId,
            Code = code is { Length: > 0 } ? code : NextCode(conn, tx, orgId),
            Status = status,
            RequestedBy = requestedBy,
            ApplicantId = applicant?.Id.Trim() ?? "",
            ApplicantName = applicant?.Name.Trim() ?? "",
            ApplicantEmail = applicant?.Email.Trim() ?? "",
            ApplicantHash = applicant?.PasswordHash ?? "",
            IsActive = true,
            // дирижёр появляется только у подтверждённой связи и только если его ещё нет
            IsConductor = isConductor
                          ?? (status == OrgServerStatus.Active && !HasConductor(conn, tx, orgId)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Sql.Exec(conn, tx, """
            INSERT INTO org_servers (id, org_id, server_id, code, is_conductor, is_active, status,
                                     requested_by, note, applicant_id, applicant_name,
                                     applicant_email, applicant_hash, created_at, updated_at)
            VALUES (@id, @o, @s, @c, @cond, 1, @st, @by, '', @aid, @aname, @amail, @ahash,
                    @cr, @up)
            """,
            ("@id", link.Id), ("@o", orgId), ("@s", serverId), ("@c", link.Code),
            ("@cond", link.IsConductor ? 1 : 0), ("@st", link.Status), ("@by", link.RequestedBy),
            ("@aid", link.ApplicantId), ("@aname", link.ApplicantName),
            ("@amail", link.ApplicantEmail), ("@ahash", link.ApplicantHash),
            ("@cr", Sql.ToDb(now)), ("@up", Sql.ToDb(now)));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = status == OrgServerStatus.Active
                ? EventTypes.ServerLinked
                : EventTypes.ServerJoinRequested,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orgId, serverId, link.Code, link.Status, link.RequestedBy,
            }),
        });
        tx.Commit();
        DropCache();
        return link;
    }

    /// <summary>
    /// ВЕРНУТЬ К ЖИЗНИ мягко удалённую связь «организация ↔ сервер» (T-175-S0); null —
    /// удалённой связи нет, и <see cref="Attach"/> заводит новую обычным порядком.
    ///
    /// Строка та же самая — с прежним идентификатором: он общий на весь кластер, и завести
    /// вместо неё вторую значило бы задвоить связь у партнёра. Идентификатор меняется
    /// только если его назвали явно (подключение к чужой организации: там связь обязана
    /// называться одинаково у обеих сторон) и он не сошёлся с прежним.
    ///
    /// Код в организации сохраняется прежний, если новый не назван: сервер, однажды
    /// получивший в организации <c>S1</c>, должен остаться <c>S1</c> и после возвращения —
    /// иначе задачи и файлы, помеченные его кодом, читались бы как чужие.
    /// </summary>
    private OrgServer? Revive(SqliteConnection conn, SqliteTransaction tx, string orgId,
        string serverId, string status, string requestedBy, string? actorId, string? code,
        string? id, bool? isConductor, JoinApplicant? applicant, DateTime now)
    {
        var goneId = Sql.Scalar<string>(conn, tx, """
            SELECT id FROM org_servers
            WHERE org_id=@o AND server_id=@s AND deleted_at IS NOT NULL
            ORDER BY updated_at DESC LIMIT 1
            """, ("@o", orgId), ("@s", serverId)) ?? "";
        if (goneId.Length == 0)
        {
            return null;
        }
        var wasCode = Sql.Scalar<string>(conn, tx, "SELECT code FROM org_servers WHERE id=@id",
            ("@id", goneId)) ?? "";
        var wasCreated = Sql.Query(conn, tx, "SELECT created_at FROM org_servers WHERE id=@id",
            r => r.Dt("created_at"), ("@id", goneId)).FirstOrDefault();
        var link = new OrgServer
        {
            Id = id is { Length: > 0 } ? id : goneId,
            OrgId = orgId,
            ServerId = serverId,
            Code = code is { Length: > 0 } ? code
                : wasCode.Length > 0 ? wasCode : NextCode(conn, tx, orgId),
            Status = status,
            RequestedBy = requestedBy,
            ApplicantId = applicant?.Id.Trim() ?? "",
            ApplicantName = applicant?.Name.Trim() ?? "",
            ApplicantEmail = applicant?.Email.Trim() ?? "",
            ApplicantHash = applicant?.PasswordHash ?? "",
            IsActive = true,
            IsConductor = isConductor
                          ?? (status == OrgServerStatus.Active && !HasConductor(conn, tx, orgId)),
            CreatedAt = wasCreated == default ? now : wasCreated,
            UpdatedAt = now,
        };
        Sql.Exec(conn, tx, """
            UPDATE org_servers
            SET id=@nid, code=@c, is_conductor=@cond, is_active=1, status=@st, requested_by=@by,
                applicant_id=@aid, applicant_name=@aname, applicant_email=@amail,
                applicant_hash=@ahash, deleted_at=NULL, updated_at=@up
            WHERE id=@id
            """,
            ("@nid", link.Id), ("@c", link.Code), ("@cond", link.IsConductor ? 1 : 0),
            ("@st", link.Status), ("@by", link.RequestedBy), ("@aid", link.ApplicantId),
            ("@aname", link.ApplicantName), ("@amail", link.ApplicantEmail),
            ("@ahash", link.ApplicantHash), ("@up", Sql.ToDb(now)), ("@id", goneId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = status == OrgServerStatus.Active
                ? EventTypes.ServerLinked
                : EventTypes.ServerJoinRequested,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orgId, serverId, link.Code, link.Status, link.RequestedBy, revived = true,
            }),
        });
        tx.Commit();
        DropCache();
        return link;
    }

    /// <summary>Отвязать сервер от организации (мягко). Дирижёра отвязать нельзя: он
    /// обязателен — сначала назначьте дирижёром другой сервер (ТЗ гл. 6).</summary>
    public void Detach(string orgId, string serverId, string? actorId)
    {
        var link = Link(orgId, serverId) ?? throw new ArgumentException(Loc.T("msg.server.9"));
        if (link.IsConductor)
        {
            throw new ArgumentException(
                Loc.T("msg.server.4"));
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var now = DateTime.UtcNow;
        Sql.Exec(conn, tx, "UPDATE org_servers SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", Sql.ToDb(now)), ("@id", link.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerUnlinked,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new { orgId, serverId, link.Code }),
        });
        tx.Commit();
        DropCache();
    }

    /// <summary>Активность связи: неактивный в организации сервер не опрашивается.</summary>
    public void SetLinkActive(string orgId, string serverId, bool isActive, string? actorId)
    {
        var link = Link(orgId, serverId) ?? throw new ArgumentException(Loc.T("msg.server.9"));
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE org_servers SET is_active=@a, updated_at=@u WHERE id=@id",
            ("@a", isActive ? 1 : 0), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", link.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerUpdated,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new { orgId, serverId, isActive }),
        });
        tx.Commit();
    }

    /// <summary>
    /// Назначить дирижёра организации: отметка снимается у прежнего — дирижёр обязателен
    /// и всегда ровно один (ТЗ гл. 6). Только он запускает задачи автоматически и по расписанию.
    /// </summary>
    public void SetConductor(string orgId, string serverId, string? actorId)
    {
        var link = Link(orgId, serverId) ?? throw new ArgumentException(Loc.T("msg.server.9"));
        if (link.Status != OrgServerStatus.Active)
        {
            throw new ArgumentException(Loc.T("msg.server.10"));
        }
        var previous = Conductor(orgId);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        MoveReplicationIntervals(conn, tx, previous, serverId);
        Sql.Exec(conn, tx, "UPDATE org_servers SET is_conductor=0, updated_at=@u WHERE org_id=@o",
            ("@u", Sql.ToDb(DateTime.UtcNow)), ("@o", orgId));
        Sql.Exec(conn, tx, "UPDATE org_servers SET is_conductor=1, updated_at=@u WHERE id=@id",
            ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", link.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerConductorChanged,
            EntityType = "org_server",
            EntityId = link.Id,
            PayloadJson = JsonSerializer.Serialize(new { orgId, serverId, link.Code }),
        });
        tx.Commit();
    }

    /// <summary>
    /// ПЕРЕДАЧА ДИРИЖЁРСТВА и интервалы репликации (T-135).
    ///
    /// Интервал пары принадлежит записи РЯДОВОГО сервера (см. <see cref="SetReplication"/>):
    /// у дирижёра своей пары нет, и его интервалы всегда пусты. После смены дирижёра стороны
    /// меняются местами — и пара осталась бы вообще без интервала, то есть автоматическая
    /// репликация молча прекратилась бы до ручной правки настроек. Поэтому интервалы нового
    /// дирижёра (он ими и жил, будучи рядовым) переносятся прежнему дирижёру — ритм пары
    /// сохраняется. Уже заданные у прежнего значения не трогаются: их задал человек.
    /// </summary>
    private static void MoveReplicationIntervals(SqliteConnection conn, SqliteTransaction? tx,
        OrgServer? previous, string newConductorId)
    {
        if (previous is null || previous.ServerId == newConductorId)
        {
            return;
        }
        var interval = Sql.Scalar<long?>(conn, tx,
            "SELECT repl_interval_sec FROM servers WHERE id=@id", ("@id", newConductorId));
        var retry = Sql.Scalar<long?>(conn, tx,
            "SELECT repl_retry_sec FROM servers WHERE id=@id", ("@id", newConductorId));
        if (interval is null && retry is null)
        {
            return;   // и новый дирижёр реплицировался только по кнопке — переносить нечего
        }
        Sql.Exec(conn, tx, """
            UPDATE servers
            SET repl_interval_sec = COALESCE(repl_interval_sec, @i),
                repl_retry_sec    = COALESCE(repl_retry_sec, @r),
                updated_at = @u
            WHERE id = @id
            """,
            ("@i", interval), ("@r", retry), ("@u", Sql.ToDb(DateTime.UtcNow)),
            ("@id", previous.ServerId));
    }

    /// <summary>Дирижёр организации: он ровно один; null — организация без серверов.</summary>
    public OrgServer? Conductor(string orgId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, LinkSelect + """
             WHERE l.deleted_at IS NULL AND l.org_id=@o AND l.is_conductor=1 AND l.status='active'
             LIMIT 1
            """, MapLink, ("@o", orgId)).FirstOrDefault();
    }

    /// <summary>
    /// Этот сервер — дирижёр организации (ТЗ гл. 6). От ответа зависит, работают ли здесь
    /// автозапуски и расписание: автоматически запускает задачи только дирижёр.
    /// Серверов у организации ещё нет вовсе — считаем дирижёром себя: иначе одиночная
    /// установка, где серверов никто не заводил, перестала бы запускать задачи.
    /// </summary>
    public bool IsLocalConductor(string orgId)
    {
        return Conductor(orgId)?.IsLocal ?? true;
    }

    /// <summary>Код локального сервера в организации (S0, S1, …) — показывается в настройках
    /// только для чтения (todo41, замечание 2); пусто — сервер с организацией не связан.</summary>
    public string LocalCode(string orgId) => CodeOf(orgId, LocalId());

    // --- быстрый справочник «кто есть кто» (ТЗ гл. 6, этап 42) ---
    //
    // Владелец подставляется КАЖДОЙ строке задачи, расписания, модели при выдаче наружу,
    // а серверы лежат в другой БД: без кэша список из 200 задач дал бы 400 запросов.
    // Топология меняется редко и только через этот сервис, поэтому кэш сбрасывается явно
    // при каждой записи — «протухших» значений не бывает, и номер новой задачи получает
    // правильный код сразу после подключения сервера к организации.

    private readonly object _cacheLock = new();
    private Dictionary<string, string>? _names;      // serverId → имя
    private Dictionary<string, string>? _codes;      // orgId + "|" + serverId → код (S0, S1, …)
    private string? _localId;

    /// <summary>Внутренний ключ (unid) локального сервера; пусто — записи ещё нет.</summary>
    public string LocalId()
    {
        lock (_cacheLock)
        {
            return _localId ??= Local()?.Id ?? "";
        }
    }

    /// <summary>Код сервера в организации (S0, S1, …); пусто — сервер с ней не связан.</summary>
    public string CodeOf(string orgId, string? serverId)
    {
        if (serverId is not { Length: > 0 } || orgId.Length == 0)
        {
            return "";
        }
        lock (_cacheLock)
        {
            EnsureCache();
            return _codes!.GetValueOrDefault(orgId + "|" + serverId, "");
        }
    }

    /// <summary>Имя сервера по его внутреннему ключу — для подсказок и текстов ошибок.</summary>
    public string NameOf(string? serverId)
    {
        if (serverId is not { Length: > 0 })
        {
            return "";
        }
        lock (_cacheLock)
        {
            EnsureCache();
            return _names!.GetValueOrDefault(serverId, "");
        }
    }

    private void EnsureCache()
    {
        if (_names is not null && _codes is not null)
        {
            return;
        }
        using var conn = _db.Open();
        _names = Sql.Query(conn, null, "SELECT id, name FROM servers WHERE deleted_at IS NULL",
                r => (Id: r.S("id"), Name: r.S("name")))
            .ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);
        _codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Sql.Query(conn, null,
                     "SELECT org_id, server_id, code FROM org_servers WHERE deleted_at IS NULL",
                     r => (Org: r.S("org_id"), Server: r.S("server_id"), Code: r.S("code"))))
        {
            _codes[row.Org + "|" + row.Server] = row.Code;
        }
    }

    /// <summary>Сбросить кэш кодов и имён — вызывается после любой записи о серверах.</summary>
    private void DropCache()
    {
        lock (_cacheLock)
        {
            _names = null;
            _codes = null;
            _localId = null;
        }
    }

    // --- заявки на подключение (todo41 пп. 7–10) ---

    /// <summary>«ПРИНЯТЬ»: заявка подтверждена — сервер работает в организации.</summary>
    public OrgServer AcceptRequest(string linkId, string? actorId) =>
        Decide(linkId, OrgServerStatus.Active, EventTypes.ServerJoinAccepted, actorId);

    /// <summary>«ОТЛОЖИТЬ»: решение не принято, запись остаётся в списке серверов.</summary>
    public OrgServer DeferRequest(string linkId, string? actorId) =>
        Decide(linkId, OrgServerStatus.Deferred, EventTypes.ServerJoinDeferred, actorId);

    /// <summary>«ОТКЛОНИТЬ»: запись убирается из списка, а событие в журнале остаётся (п. 9).</summary>
    public OrgServer RejectRequest(string linkId, string? actorId)
    {
        var link = LinkById(linkId) ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.8"));
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE org_servers SET status=@st, deleted_at=@d, updated_at=@d WHERE id=@id
            """, ("@st", OrgServerStatus.Rejected), ("@d", Sql.ToDb(now)), ("@id", linkId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ServerJoinRejected,
            EntityType = "org_server",
            EntityId = linkId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                link.OrgId, link.ServerId, link.Code, link.RequestedBy, link.ServerName, link.Url,
            }),
        });
        tx.Commit();
        DropCache();
        link.Status = OrgServerStatus.Rejected;
        link.DeletedAt = now;
        return link;
    }

    /// <summary>Забыть хэш пароля заявителя (T-139): он нужен ровно один раз — при
    /// подтверждении заявки, когда по нему заводится аккаунт человека.</summary>
    public void ForgetApplicantSecret(string linkId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE org_servers SET applicant_hash='' WHERE id=@id", ("@id", linkId));
        DropCache();
    }

    private OrgServer Decide(string linkId, string status, string eventType, string? actorId)
    {
        var link = LinkById(linkId) ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.8"));
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // первый подтверждённый сервер организации становится её дирижёром (ТЗ гл. 6)
        var conductor = status == OrgServerStatus.Active && !HasConductor(conn, tx, link.OrgId);
        Sql.Exec(conn, tx, """
            UPDATE org_servers SET status=@st, is_conductor=@cond, updated_at=@u WHERE id=@id
            """,
            ("@st", status), ("@cond", conductor || link.IsConductor ? 1 : 0),
            ("@u", Sql.ToDb(now)), ("@id", linkId));
        if (status == OrgServerStatus.Active)
        {
            // до подтверждения запись сервера НЕАКТИВНА и его токен никого не опознаёт:
            // «ПРИНЯТЬ» — это в том числе и разрешение ему обращаться к нам (ТЗ гл. 12)
            Sql.Exec(conn, tx, "UPDATE servers SET is_active=1, updated_at=@u WHERE id=@id",
                ("@u", Sql.ToDb(now)), ("@id", link.ServerId));
        }
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "org_server",
            EntityId = linkId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                link.OrgId, link.ServerId, link.Code, link.RequestedBy, link.ServerName, link.Url,
            }),
        });
        tx.Commit();
        DropCache();
        link.Status = status;
        link.IsConductor = conductor || link.IsConductor;
        link.UpdatedAt = now;
        return link;
    }

    // --- внутреннее ---

    /// <summary>Следующий код сервера в организации: S0 — первому, дальше по возрастанию.
    /// Учитываются и удалённые связи: код не переиспользуется после вывода сервера
    /// из кластера — иначе номера задач «T-18-S1» стали бы неоднозначными (ТЗ гл. 6).</summary>
    private static string NextCode(SqliteConnection conn, SqliteTransaction? tx, string orgId)
    {
        var codes = Sql.Query(conn, tx, "SELECT code FROM org_servers WHERE org_id=@o",
            r => r.S("code"), ("@o", orgId));
        var max = -1;
        foreach (var code in codes)
        {
            if (code.Length > 1 && code[0] is 'S' or 's'
                && int.TryParse(code[1..], out var number) && number > max)
            {
                max = number;
            }
        }
        return "S" + (max + 1);
    }

    private static bool HasConductor(SqliteConnection conn, SqliteTransaction? tx, string orgId) =>
        Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM org_servers
            WHERE org_id=@o AND is_conductor=1 AND status='active' AND deleted_at IS NULL
            """, ("@o", orgId)) > 0;

    /// <summary>Подтверждённые связи впереди, заявки — последними (todo41 п. 8).</summary>
    private static List<OrgServer> Sorted(List<OrgServer> links, bool includeRequests) =>
        links.Where(l => includeRequests || l.Status == OrgServerStatus.Active)
            .OrderBy(l => OrgServerStatus.IsRequest(l.Status) ? 1 : 0)
            .ThenBy(l => l.IsLocal ? 0 : 1)
            .ThenBy(l => l.CreatedAt)
            .ToList();

    private static void Insert(SqliteConnection conn, SqliteTransaction? tx, ServerNode server) =>
        Sql.Exec(conn, tx, """
            INSERT INTO servers (id, display_id, name, protocol, hostname, port, hostname2, port2,
                                 base_path, is_local, is_active, created_at, updated_at)
            VALUES (@id, @did, @n, @pr, @h, @p, @h2, @p2, @bp, @loc, @a, @cr, @up)
            """,
            ("@id", server.Id), ("@did", server.DisplayId), ("@n", server.Name),
            ("@pr", server.Protocol), ("@h", server.Hostname), ("@p", server.Port),
            ("@h2", server.Hostname2), ("@p2", server.Port2),
            ("@bp", server.BasePath), ("@loc", server.IsLocal ? 1 : 0),
            ("@a", server.IsActive ? 1 : 0),
            ("@cr", Sql.ToDb(server.CreatedAt)), ("@up", Sql.ToDb(server.UpdatedAt)));

    private static string Normalize(string protocol) =>
        protocol.Trim().ToLowerInvariant() == "https" ? "https" : "http";

    private static string NormalizeBasePath(string basePath)
    {
        var path = basePath.Trim().Trim('/');
        return path.Length == 0 ? "" : "/" + path;
    }

    private static void Validate(ServerNode server)
    {
        if (server.Hostname.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.server.11"));
        }
        if (server.Hostname.Contains('/') || server.Hostname.Contains(' '))
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.17", server.Hostname));
        }
        if (server.Port is < 1 or > 65535)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.16", server.Port));
        }
        // второй адрес — такое же имя хоста, и проверяется так же (T-50-S0)
        if (server.Hostname2.Contains('/') || server.Hostname2.Contains(' '))
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.17", server.Hostname2));
        }
        if (server.Port2 is < 1 or > 65535)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.16", server.Port2));
        }
        if (server.Name.Length == 0)
        {
            server.Name = server.Hostname;
        }
    }

    /// <summary>
    /// Чья запись занимает этот адрес; null — свободен. С T-141 это только СПРАВКА для
    /// сообщений человеку: одинаковый адрес больше не запрещён — серверы различаются
    /// внутренним ключом, а не адресом (<see cref="ServerAddress"/>), и за NAT половина
    /// кластера законно называется «localhost».
    /// </summary>
    public ServerNode? AddressOwner(string hostname, int port, string basePath, string exceptId = "")
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM servers
            WHERE hostname=@h COLLATE NOCASE AND port=@p AND base_path=@bp AND id<>@id
              AND deleted_at IS NULL
            LIMIT 1
            """, Map, ("@h", hostname.Trim()), ("@p", port), ("@bp", NormalizeBasePath(basePath)),
            ("@id", exceptId)).FirstOrDefault();
    }

    /// <summary>Связь с именами сервера и организации: список серверов показывает и то, и другое.</summary>
    private const string LinkSelect = """
        SELECT l.*, s.name AS server_name, s.protocol, s.hostname, s.port, s.base_path,
               s.is_local, o.name AS org_name, o.code AS org_code
        FROM org_servers l
        JOIN servers s ON s.id = l.server_id
        JOIN orgs    o ON o.id = l.org_id
        """;

    private static OrgServer MapLink(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        OrgId = r.S("org_id"),
        ServerId = r.S("server_id"),
        Code = r.S("code"),
        IsConductor = r.B("is_conductor"),
        IsActive = r.B("is_active"),
        Status = r.S("status"),
        RequestedBy = r.S("requested_by"),
        Note = r.S("note"),
        ApplicantId = r.S("applicant_id"),
        ApplicantName = r.S("applicant_name"),
        ApplicantEmail = r.S("applicant_email"),
        ApplicantHash = r.S("applicant_hash"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
        ServerName = r.S("server_name"),
        Url = $"{r.S("protocol")}://{r.S("hostname")}:{r.L("port")}{r.S("base_path")}",
        IsLocal = r.B("is_local"),
        OrgName = r.S("org_name"),
        OrgCode = r.S("org_code"),
    };

    private static ServerNode Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Protocol = r.S("protocol"),
        Hostname = r.S("hostname"),
        Port = (int)r.L("port"),
        Hostname2 = r.S("hostname2"),
        Port2 = (int?)r.LN("port2"),
        BasePath = r.S("base_path"),
        IsLocal = r.B("is_local"),
        IsActive = r.B("is_active"),
        JoinStatus = r.S("join_status"),
        JoinOrgCode = r.S("join_org"),
        JoinRef = r.S("join_ref"),
        LastError = r.S("last_error"),
        ReplIntervalSec = (int?)r.LN("repl_interval_sec"),
        ReplRetrySec = (int?)r.LN("repl_retry_sec"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}

/// <summary>Данные формы сервера для сервиса (сервис не знает о DTO уровня API).</summary>
public sealed class ServerSaveInput
{
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "http";
    public string Hostname { get; set; } = "";
    public int Port { get; set; } = 5480;
    /// <summary>Второй (внешний) адрес сервера (T-50-S0); пусто — его нет.</summary>
    public string Hostname2 { get; set; } = "";
    /// <summary>Порт второго адреса; null — тот же, что основной.</summary>
    public int? Port2 { get; set; }
    public string BasePath { get; set; } = "/ai2p";
    public bool IsActive { get; set; } = true;
}
