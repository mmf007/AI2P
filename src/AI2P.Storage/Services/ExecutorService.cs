using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Исполнители (ТЗ п. 2.2) — люди и ИИ.
///
/// В кластере (ТЗ гл. 6, этап 42) исполнители правятся ТОЛЬКО на дирижёре: они назначаются
/// задачам всех серверов, и список должен быть один на организацию. Исключение — заведение
/// участника при входе аккаунта в организацию: без исполнителя человек не смог бы работать
/// вовсе, поэтому такой исполнитель создаётся на любом сервере (<see cref="CreateMember"/>).
///
/// У ИИ с ЛОКАЛЬНОЙ моделью есть сервер: модель, её файлы и команда запуска физически лежат
/// на конкретном компьютере, и назначать такого исполнителя задаче другого сервера нельзя.
///
/// МЕСТНЫЕ ИСПОЛНИТЕЛИ (T-177-S0) заводятся и правятся НА СВОЁМ СЕРВЕРЕ, а не на дирижёре:
/// это ИИ с локальной моделью и «авто ПО» (плагины и MCP — они тоже локальны). Всё, чем
/// работает такой исполнитель, стоит на его компьютере, и человек за этим компьютером не
/// должен ждать дирижёра, чтобы завести себе исполнителя. Номер такому исполнителю выдаётся
/// по тому же правилу, что и задаче (ТЗ гл. 6 «Номера задач»): с суффиксом кода сервера —
/// <c>E-3-S1</c>, — иначе счётчики двух серверов дали бы два разных исполнителя с одним
/// номером. Дальше запись едет обычной репликацией (<c>ChangeLog</c>, таблица executors).
/// </summary>
public sealed class ExecutorService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;
    private readonly ServerScope _scope;
    private readonly AgentLimitService _limits;

    public ExecutorService(Database db, EventStore events, FileStore files, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _files = files;
        _scope = scope ?? ServerScope.Standalone();
        _limits = new AgentLimitService(db);
    }

    /// <summary>Список исполнителей правится только на дирижёре организации (ТЗ гл. 6).
    /// Исключение — МЕСТНЫЕ исполнители (T-177-S0), см. <see cref="IsLocal"/>.</summary>
    public bool CanManage => _scope.IsConductor;

    /// <summary>
    /// МЕСТНЫЙ исполнитель (T-177-S0): «авто ПО» (плагин, MCP) и ИИ с ЛОКАЛЬНОЙ моделью —
    /// то есть всё, у чего есть сервер-владелец. Признак производный и проверяется ПОСЛЕ
    /// <see cref="Validate"/>: сервер там проставляется по факту — у «авто ПО» это сервер,
    /// где стоит программа, у ИИ с локальной моделью — сервер, где модель включена,
    /// а у человека и у облачного ИИ он гасится в null (запись общая, её правит дирижёр).
    /// </summary>
    public static bool IsLocal(Executor executor) =>
        executor.Kind != ExecutorKind.Human && executor.ServerId is { Length: > 0 };

    /// <summary>
    /// Право править ЭТУ запись здесь: на дирижёре — любую, на остальных серверах — только
    /// свою местную (T-177-S0). Проверка идёт по СВОЕМУ серверу записи, а не по «бесхозное
    /// правит дирижёр»: чужой местный исполнитель правится там, где стоит его модель или
    /// программа, — здесь про них ничего не известно.
    /// </summary>
    private void EnsureCanManage(Executor executor)
    {
        if (CanManage || (IsLocal(executor) && _scope.IsMine(executor.ServerId)))
        {
            return;
        }
        throw new ArgumentException(Loc.T("msg.executor.1"));
    }

    /// <summary>
    /// Справка об аккаунте по его внутреннему ключу (T-272). Аккаунты лежат в СЕРВЕРНОЙ базе
    /// (ТЗ п. 6.4.1), и организация про них ничего не знает, — поэтому связь делегатом, как
    /// у <see cref="EnsureMember"/>. Нужна почте уведомлений: «брать из логина» означает
    /// «взять адрес аккаунта входа». Делегат не задан (тесты, реплика) — логин просто не
    /// подставляется, и правило «почта из логина» даёт пустой адрес.
    /// </summary>
    public Func<string, MemberAccount?>? AccountLookup { get; set; }

    /// <summary>Почта аккаунта входа исполнителя; пусто — аккаунта нет либо он не найден.</summary>
    public string LoginEmailOf(Executor executor) =>
        executor.AccountId is { Length: > 0 } id && AccountLookup?.Invoke(id) is { } account
            ? account.Email.Trim()
            : "";

    /// <summary>
    /// АДРЕС ДЛЯ УВЕДОМЛЕНИЙ (T-272): галочка «почту брать из логина пользователя» поднята —
    /// адрес аккаунта входа, снята — адрес из формы исполнителя. Пусто — писать некуда,
    /// и уведомление этому исполнителю не уходит (в журнал пишется причина).
    /// </summary>
    public string NotifyAddressOf(Executor executor) =>
        executor.EmailFromLogin
            ? (executor.LoginEmail.Trim().Length > 0 ? executor.LoginEmail.Trim() : LoginEmailOf(executor))
            : executor.NotifyEmail.Trim();

    private Executor Decorate(Executor executor)
    {
        executor.ServerCode = _scope.CodeOf(executor.ServerId);
        executor.ServerName = _scope.NameOf(executor.ServerId);
        // почта аккаунта входа (T-272): её показывает форма исполнителя при поднятой галочке
        // «брать из логина» — иначе непонятно, на какой адрес уйдёт письмо
        executor.LoginEmail = LoginEmailOf(executor);
        // расход за окно лимита (T-121) — только если лимиты заданы: не заданы, значит
        // вычисление игнорируется целиком и лишних запросов к заданиям не делается
        if (LimitStatus(executor) is { } limit)
        {
            executor.WindowTokens = limit.Used;
            executor.WindowResetAt = limit.ResetAt;
        }
        return executor;
    }

    /// <summary>
    /// Остаток лимита исполнителя за скользящее окно (T-121); null — лимиты у исполнителя
    /// не указаны, вычисление игнорируется. Нужен запуску задания (предупреждение и перенос
    /// старта) и списку исполнителей.
    /// </summary>
    public AgentLimitStatus? LimitStatus(Executor executor, DateTime? nowUtc = null) =>
        _limits.Status(executor, nowUtc);

    /// <summary>Пути файлов исполнителя-человека по схеме из ТЗ п. 2.2 (profile_&lt;ID&gt;.json).</summary>
    public static string ProfilePathOf(string executorId) => $"executors/profile_{executorId}.json";

    public static string ScopePathOf(string executorId) => $"executors/scope_{executorId}.json";

    public List<Executor> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM executors" + (includeDeleted ? "" : " WHERE deleted_at IS NULL") + " ORDER BY nick";
        var executors = Sql.Query(conn, null, sql, Map);
        foreach (var executor in executors)
        {
            Decorate(executor);
        }
        return executors;
    }

    public Executor? Get(string id)
    {
        using var conn = _db.Open();
        var executor = Sql.Query(conn, null, "SELECT * FROM executors WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        return executor is null ? null : Decorate(executor);
    }

    /// <summary>
    /// Исполнитель-человек, привязанный к аккаунту (ТЗ п. 2.14, п. 2.15): это и есть участие
    /// аккаунта в ЭТОЙ организации — нет исполнителя, значит доступа к организации нет.
    /// null — аккаунт не участник.
    /// </summary>
    public Executor? ByAccount(string accountId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM executors WHERE account_id=@a AND deleted_at IS NULL
            ORDER BY created_at LIMIT 1
            """, Map, ("@a", accountId)).FirstOrDefault();
    }

    /// <summary>
    /// Аккаунты всех участников организации (ТЗ п. 2.15). Нужны репликации (этап 43):
    /// аккаунты живут в серверной БД и едут между серверами именно «в рамках организации» —
    /// иначе войти на второй сервер было бы нечем, пока сервер-владелец аккаунта недоступен.
    /// Заодно это граница: чужие аккаунты установки партнёру не отдаются.
    /// </summary>
    public List<string> AccountIds()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT DISTINCT account_id FROM executors WHERE account_id IS NOT NULL AND account_id<>''",
            r => r.S("account_id"));
    }

    /// <summary>Свободный ник по желаемому: ник исполнителя уникален (п. 2.2), при совпадении
    /// добавляется номер. Нужен при заведении исполнителя по аккаунту (ТЗ п. 2.14).</summary>
    public string UniqueNick(string wanted) => Free(wanted, "user", e => e.Nick);

    /// <summary>
    /// Свободное ВНУТРЕННЕЕ ИМЯ человека (T-146): оно тоже уникально (п. 2.2), и при
    /// совпадении к нему так же добавляется номер. Раньше номер добавлялся только к нику,
    /// а внутреннее имя бралось как есть — и заведение участника падало на совпадении
    /// имён (см. <see cref="EnsureMember"/>).
    /// </summary>
    public string UniqueInternalName(string wanted) =>
        Free(wanted, Loc.T("msg.executor.2"), e => e.Kind == ExecutorKind.Human ? e.InternalName : "");

    private string Free(string wanted, string fallback, Func<Executor, string> field)
    {
        wanted = wanted.Trim();
        if (wanted.Length == 0)
        {
            wanted = fallback;
        }
        var taken = List(includeDeleted: true)
            .Select(field)
            .Where(v => v.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted))
        {
            return wanted;
        }
        for (var i = 2; ; i++)
        {
            var candidate = $"{wanted} {i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// УЧАСТИЕ АККАУНТА В ОРГАНИЗАЦИИ (T-146) — единственная точка, где оно заводится.
    ///
    /// Раньше участник просто создавался, а совпадение внутреннего имени с уже существующим
    /// человеком выбрасывало исключение «Человек с внутренним именем «…» уже есть». Ловилось
    /// это на самом неудачном месте: аккаунт удалённого пользователя (T-140) уходит из списка,
    /// а его ИСПОЛНИТЕЛЬ остаётся — он актор прошлых действий в журнале работ, — и завести
    /// того же человека заново было нельзя ни руками, ни подтверждением заявки на подключение
    /// сервера. Приём заявки при этом уже успевал подтвердить связь, и починить это в интерфейсе
    /// было нечем.
    ///
    /// Теперь участник заводится ВСЕГДА, тремя способами по убыванию точности:
    /// <list type="number">
    /// <item>исполнитель этого аккаунта уже есть — берётся он (и снова включается, если был
    /// погашен удалением аккаунта);</item>
    /// <item>есть ОСИРОТЕВШИЙ исполнитель — человек с тем же внутренним именем, чей аккаунт
    /// удалён или не указан вовсе. Это тот же человек, вернувшийся с новым аккаунтом: он
    /// привязывается к нему, и вся его история — задачи, назначения, журнал — остаётся за ним.
    /// Чтобы не привязать однофамильца, почта удалённого аккаунта обязана совпасть с новой;</item>
    /// <item>иначе заводится новый исполнитель, и внутреннее имя при совпадении получает
    /// номер: неудобное имя лучше несостоявшегося подключения.</item>
    /// </list>
    /// </summary>
    /// <param name="accountOf">Справка об аккаунте по его ключу: почта и жив ли он; null —
    /// аккаунта на этом сервере нет вовсе. Аккаунты живут в СЕРВЕРНОЙ базе, и сервис
    /// исполнителей про них не знает (ТЗ п. 6.4.1) — справку даёт вызывающий
    /// (<c>OrgRegistry.AddMember</c>).</param>
    public Executor EnsureMember(string accountId, string name, string email, SystemRole role,
        string? actorId, Func<string, MemberAccount?> accountOf)
    {
        name = name.Trim();
        if (Bound(accountId) is { } mine)
        {
            return Revive(mine, accountId, role, actorId);
        }
        if (name.Length > 0 && Orphan(name, email, accountOf) is { } orphan)
        {
            return Revive(orphan, accountId, role, actorId);
        }
        return CreateMember(new Executor
        {
            Nick = UniqueNick(name.Length > 0 ? name : email),
            InternalName = UniqueInternalName(name.Length > 0 ? name : email),
            Kind = ExecutorKind.Human,
            SystemRole = role,
            AccountId = accountId,
        }, actorId);
    }

    /// <summary>Исполнитель этого аккаунта, даже погашенный или помеченный удалённым:
    /// участие возобновляют, а не заводят второй раз.</summary>
    private Executor? Bound(string accountId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM executors WHERE account_id=@a ORDER BY created_at LIMIT 1",
            Map, ("@a", accountId)).FirstOrDefault();
    }

    /// <summary>
    /// Человек с тем же внутренним именем, оставшийся без живого аккаунта, — тот же человек,
    /// вернувшийся с новым аккаунтом (T-146). Однофамильца отсекает почта: у УДАЛЁННОГО
    /// аккаунта она обязана совпасть с новой. Аккаунт живой — это другой человек, и его
    /// участие не трогается; аккаунт нам вовсе не известен — судить не по чему, и мы тоже
    /// не трогаем: неудобное имя с номером дешевле склеенных по ошибке людей.
    /// </summary>
    private Executor? Orphan(string name, string email, Func<string, MemberAccount?> accountOf)
    {
        using var conn = _db.Open();
        var same = Sql.Query(conn, null, """
            SELECT * FROM executors
            WHERE kind='human' AND internal_name=@name COLLATE NOCASE AND deleted_at IS NULL
            ORDER BY created_at
            """, Map, ("@name", name));
        foreach (var executor in same)
        {
            if (executor.AccountId is not { Length: > 0 } bound)
            {
                return executor;   // человек без входа: аккаунт ему как раз и заводят
            }
            if (accountOf(bound) is { Alive: false } gone
                && string.Equals(gone.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return executor;
            }
        }
        return null;
    }

    /// <summary>Вернуть человека в организацию: живой, привязан к аккаунту, роль как просили.</summary>
    private Executor Revive(Executor executor, string accountId, SystemRole role, string? actorId)
    {
        var now = DateTime.UtcNow;
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            Sql.Exec(conn, tx, """
                UPDATE executors SET account_id=@a, system_role=@role, is_active=1,
                                     deleted_at=NULL, updated_at=@u
                WHERE id=@id
                """,
                ("@a", accountId), ("@role", role.ToDb()), ("@u", Sql.ToDb(now)), ("@id", executor.Id));
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                EventType = EventTypes.ExecutorUpdated,
                EntityType = "executor",
                EntityId = executor.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    executor.Nick, accountId, role = role.ToDb(), member = true,
                }),
            });
            tx.Commit();
        }
        executor.AccountId = accountId;
        executor.SystemRole = role;
        executor.IsActive = true;
        executor.DeletedAt = null;
        executor.UpdatedAt = now;
        EnsureHumanFiles(executor);
        return Decorate(executor);
    }

    /// <summary>
    /// ЧЕЛОВЕК БОЛЬШЕ НЕ РАБОТАЕТ (T-146): его аккаунт удалён (T-140), и участие в организации
    /// гаснет вместе с ним. Исполнитель НЕ вычёркивается — он актор прошлых действий в журнале
    /// работ, — но перестаёт попадать в подбор исполнителей и в состав команд. Вернётся человек
    /// (тем же аккаунтом или новым с той же почтой) — <see cref="EnsureMember"/> включит его.
    /// </summary>
    public void SuspendMember(string accountId, string? actorId)
    {
        if (Bound(accountId) is not { } executor || !executor.IsActive)
        {
            return;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE executors SET is_active=0, updated_at=@u WHERE id=@id",
            ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", executor.Id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ExecutorUpdated,
            EntityType = "executor",
            EntityId = executor.Id,
            PayloadJson = JsonSerializer.Serialize(new { executor.Nick, suspended = true }),
        });
        tx.Commit();
    }

    /// <summary>Завести исполнителя из формы или API: право проверяется по самой записи
    /// (T-177-S0) — на не-дирижёре пройдёт только СВОЙ местный исполнитель.</summary>
    public Executor Create(Executor executor, string? actorId) =>
        CreateMember(executor, actorId, checkRights: true);

    /// <summary>
    /// Завести исполнителя БЕЗ проверки «только дирижёр» (ТЗ гл. 6, этап 42): так появляется
    /// участник организации при входе аккаунта и первый владелец при первом старте. Без
    /// исполнителя у человека нет актора действий, и работать в организации он не может —
    /// значит, дожидаться дирижёра тут нельзя.
    /// </summary>
    /// <param name="checkRights">Проверять право заводить такую запись здесь (T-177-S0):
    /// зовущие «в обход» (участник организации, первый владелец) его не проверяют — без
    /// исполнителя человек работать не может, и дожидаться дирижёра тут нельзя.</param>
    public Executor CreateMember(Executor executor, string? actorId, bool checkRights = false)
    {
        if (executor.Nick.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.executor.3"));
        }
        var now = DateTime.UtcNow;
        executor.Nick = executor.Nick.Trim();
        executor.InternalName = executor.InternalName.Trim();
        executor.CreatedAt = now;
        executor.UpdatedAt = now;

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Validate(conn, tx, executor, isNew: true);
        if (checkRights)
        {
            // право считается по УЖЕ ПРОВЕРЕННОЙ записи: сервер у неё проставлен фактом
            // (локальная модель, программа плагина), а не тем, что прислала форма (T-177-S0)
            EnsureCanManage(executor);
        }
        // код создавшего сервера в номере (ТЗ гл. 6): на дирижёре суффикса нет — E-4, как раньше
        executor.DisplayId = Database.NextDisplayId(conn, tx, "E",
            _scope.IsConductor ? "" : _scope.Code);
        var limits = Limits(executor);

        Sql.Exec(conn, tx, """
            INSERT INTO executors (id, display_id, nick, internal_name, kind, system_role, model_id,
                                   plugin_code, plugin_op,
                                   account_id, profile_path, capabilities_path, is_active,
                                   token_limit, limit_window_hours, response_timeout_minutes,
                                   email_from_login, notify_email,
                                   server_id, created_at, updated_at)
            VALUES (@id, @did, @nick, @internal, @kind, @role, @model, @plugin, @op, @account,
                    @profile, @caps, @active, @tokenLimit, @window, @timeout,
                    @fromLogin, @notifyEmail,
                    @server, @created, @updated)
            """,
            // «авто ПО» (T-153-S0): пара «плагин + именованная операция» вместо модели
            ("@plugin", executor.PluginCode), ("@op", executor.PluginOp),
            // почта для уведомлений (T-272): по умолчанию берётся из логина
            ("@fromLogin", executor.EmailFromLogin ? 1 : 0),
            ("@notifyEmail", executor.NotifyEmail.Trim()),
            ("@server", executor.ServerId),
            ("@tokenLimit", limits.Tokens), ("@window", limits.Hours),
            ("@timeout", ResponseTimeout(executor)),
            ("@id", executor.Id), ("@did", executor.DisplayId), ("@nick", executor.Nick),
            ("@internal", executor.InternalName), ("@kind", executor.Kind.ToDb()),
            ("@role", executor.SystemRole.ToDb()), ("@model", executor.ModelId),
            ("@account", executor.AccountId),
            ("@profile", executor.ProfilePath),
            ("@caps", executor.CapabilitiesPath), ("@active", executor.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ExecutorCreated,
            EntityType = "executor",
            EntityId = executor.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                executor.DisplayId,
                executor.Nick,
                kind = executor.Kind.ToDb(),
            }),
        });

        tx.Commit();
        EnsureHumanFiles(executor);
        return Decorate(executor);
    }

    public Executor Update(Executor executor, string? actorId)
    {
        var now = DateTime.UtcNow;
        executor.Nick = executor.Nick.Trim();
        executor.InternalName = executor.InternalName.Trim();

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        if (!CanManage)
        {
            // ЧТО БЫЛО в базе, а не что прислала форма (T-177-S0): иначе сменой типа на
            // «авто ПО» со своим сервером можно было бы забрать себе чужую общую запись
            var stored = Sql.Query(conn, tx, "SELECT * FROM executors WHERE id=@id", Map,
                ("@id", executor.Id)).FirstOrDefault();
            EnsureCanManage(stored ?? executor);
        }
        Validate(conn, tx, executor, isNew: false);
        EnsureCanManage(executor);
        var limits = Limits(executor);

        Sql.Exec(conn, tx, """
            UPDATE executors SET nick=@nick, internal_name=@internal, kind=@kind, system_role=@role,
                                 model_id=@model, plugin_code=@plugin, plugin_op=@op,
                                 account_id=@account,
                                 profile_path=@profile, capabilities_path=@caps,
                                 is_active=@active, busy_until=@busy, server_id=@server,
                                 token_limit=@tokenLimit, limit_window_hours=@window,
                                 response_timeout_minutes=@timeout,
                                 email_from_login=@fromLogin, notify_email=@notifyEmail,
                                 updated_at=@updated
            WHERE id=@id
            """,
            // почта для уведомлений (T-272)
            ("@fromLogin", executor.EmailFromLogin ? 1 : 0),
            ("@notifyEmail", executor.NotifyEmail.Trim()),
            ("@server", executor.ServerId),
            ("@tokenLimit", limits.Tokens), ("@window", limits.Hours),
            ("@timeout", ResponseTimeout(executor)),
            ("@nick", executor.Nick), ("@internal", executor.InternalName),
            ("@kind", executor.Kind.ToDb()), ("@role", executor.SystemRole.ToDb()),
            ("@model", executor.ModelId), ("@account", executor.AccountId),
            // «авто ПО» (T-153-S0)
            ("@plugin", executor.PluginCode), ("@op", executor.PluginOp),
            ("@profile", executor.ProfilePath), ("@caps", executor.CapabilitiesPath),
            ("@active", executor.IsActive ? 1 : 0), ("@busy", Sql.ToDbN(executor.BusyUntil)),
            ("@updated", Sql.ToDb(now)), ("@id", executor.Id));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ExecutorUpdated,
            EntityType = "executor",
            EntityId = executor.Id,
            PayloadJson = JsonSerializer.Serialize(new { executor.Nick }),
        });

        tx.Commit();
        executor.UpdatedAt = now;
        EnsureHumanFiles(executor);
        return Decorate(executor);
    }

    /// <summary>
    /// Роль участника организации (ТЗ п. 2.2) — правится в списке пользователей владельцем.
    /// Идёт мимо проверки «только дирижёр»: аккаунты и их роли заводятся там, где человек
    /// входит, иначе на не-дирижёре нельзя было бы даже добавить пользователя.
    /// </summary>
    public void SetRole(string executorId, SystemRole role, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE executors SET system_role=@role, updated_at=@now WHERE id=@id",
            ("@role", role.ToDb()), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", executorId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ExecutorUpdated,
            EntityType = "executor",
            EntityId = executorId,
            PayloadJson = JsonSerializer.Serialize(new { role = role.ToDb() }),
        });
        tx.Commit();
    }

    /// <summary>
    /// Проверки формы исполнителя (ТЗ п. 2.2, гл. 11):
    /// ник уникален; у человека уникально внутреннее имя (ФИО); у ИИ имя, профайл и
    /// декларация синхронизируются со справочником моделей (п. 2.9) по model_id.
    /// </summary>
    private void Validate(SqliteConnection conn, SqliteTransaction tx, Executor executor, bool isNew)
    {
        var nickTaken = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM executors WHERE nick=@nick AND id<>@id AND deleted_at IS NULL",
            ("@nick", executor.Nick), ("@id", executor.Id)) > 0;
        if (nickTaken)
        {
            throw new ArgumentException(Loc.T("msg.executor.4", executor.Nick));
        }

        // «АВТО ПО» (T-153-S0): это ни человек, ни агент, а обычная программа длительной
        // работы. Ни модели, ни аккаунта входа, ни токенов у неё нет; работает она парой
        // «плагин + именованная операция», и стоит на КОНКРЕТНОМ компьютере — поэтому сервер
        // ОБЯЗАТЕЛЕН, как у ИИ с локальной моделью. Оставить его пустым нельзя: «бесхозную
        // строку правит дирижёр» здесь читалось бы как «программа переехала на другой
        // компьютер», а при смене дирижёра уехала бы разом вся такая работа (b113de29).
        if (executor.Kind == ExecutorKind.Software)
        {
            executor.ModelId = null;
            executor.AccountId = null;
            // токенов, лимитов провайдера и окна у программы нет вовсе (T-121 — про агента);
            // в форме этих полей у неё тоже не показывается
            executor.TokenLimit = null;
            executor.LimitWindowHours = null;
            executor.PluginCode = executor.PluginCode.Trim();
            executor.PluginOp = executor.PluginOp.Trim();
            if (executor.PluginCode.Length == 0 || executor.PluginOp.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.executor.12"));
            }
            if (executor.ServerId is not { Length: > 0 })
            {
                // исполнителя заводят там, где стоит программа — как локальную модель
                executor.ServerId = _scope.ServerId;
            }
            if (executor.ServerId is not { Length: > 0 })
            {
                throw new ArgumentException(Loc.T("msg.executor.13"));
            }
            return;
        }
        // плагин с операцией — только у «авто ПО»: у человека и агента поля пусты
        executor.PluginCode = "";
        executor.PluginOp = "";

        if (executor.Kind == ExecutorKind.Human)
        {
            executor.ModelId = null;
            // лимит токенов — про ИИ (T-121); у человека полей нет и в форме они не показываются.
            // Таймаут ответа (T-124) — тоже: человека ждут столько, сколько нужно
            executor.TokenLimit = null;
            executor.LimitWindowHours = null;
            executor.ResponseTimeoutMinutes = null;
            // аккаунт входа (ТЗ п. 2.14, v1.46): один аккаунт — не более одного исполнителя,
            // иначе непонятно, от чьего имени работает вошедший
            if (executor.AccountId is { Length: > 0 })
            {
                var accountTaken = Sql.Scalar<long>(conn, tx, """
                    SELECT COUNT(*) FROM executors
                    WHERE account_id=@a AND id<>@id AND deleted_at IS NULL
                    """,
                    ("@a", executor.AccountId), ("@id", executor.Id)) > 0;
                if (accountTaken)
                {
                    throw new ArgumentException(Loc.T("msg.executor.5"));
                }
            }
            else
            {
                executor.AccountId = null; // пустая строка из формы — это «без аккаунта»
            }
            if (executor.InternalName.Length > 0)
            {
                var nameTaken = Sql.Scalar<long>(conn, tx, """
                    SELECT COUNT(*) FROM executors
                    WHERE kind='human' AND internal_name=@name AND id<>@id AND deleted_at IS NULL
                    """,
                    ("@name", executor.InternalName), ("@id", executor.Id)) > 0;
                if (nameTaken)
                {
                    throw new ArgumentException(
                        Loc.T("msg.executor.6", executor.InternalName));
                }
            }
            // файлы человека — по схеме profile_<ID>.json / scope_<ID>.json (п. 2.2)
            if (executor.ProfilePath.Length == 0)
            {
                executor.ProfilePath = ProfilePathOf(executor.Id);
            }
            if (executor.CapabilitiesPath.Length == 0)
            {
                executor.CapabilitiesPath = ScopePathOf(executor.Id);
            }
            return;
        }

        executor.AccountId = null; // у ИИ аккаунта входа нет (ТЗ п. 2.14)
        // ИИ: имя, профайл и декларация — из справочника моделей (синхронно с model_id)
        if (executor.ModelId is not null)
        {
            var model = Sql.Query(conn, tx, """
                SELECT name, is_active, is_local, server_id, profile_path, capabilities_path
                FROM ai_models WHERE id=@id AND deleted_at IS NULL
                """,
                r => new { Name = r.S("name"), Active = r.B("is_active"), Local = r.B("is_local"),
                           Server = r.SN("server_id"),
                           Profile = r.S("profile_path"), Caps = r.S("capabilities_path") },
                ("@id", executor.ModelId)).FirstOrDefault();
            if (model is null)
            {
                throw new ArgumentException(Loc.T("msg.executor.7"));
            }
            executor.InternalName = model.Name;
            executor.ProfilePath = model.Profile;
            executor.CapabilitiesPath = model.Caps;
            if (!model.Local)
            {
                // облачная модель доступна отовсюду: одна на всю организацию
                if (isNew && !model.Active)
                {
                    throw new ArgumentException(Loc.T("msg.executor.8"));
                }
                executor.ServerId = null;
                return;
            }
            // ЛОКАЛЬНАЯ модель ставится на каждом компьютере ОТДЕЛЬНО (T-8-S1): она может
            // быть включена на одних серверах и выключена на других, поэтому её исполнитель
            // привязывается к серверу, где модель ВКЛЮЧЕНА, и активным может быть только там
            var activeServers = Sql.Query(conn, tx, """
                SELECT server_id FROM ai_model_servers
                WHERE model_id=@m AND is_active=1 AND deleted_at IS NULL
                """, r => r.S("server_id"), ("@m", executor.ModelId));
            executor.ServerId = PickServerFor(executor.ServerId, activeServers) ?? model.Server;
            if (isNew && activeServers.Count == 0)
            {
                throw new ArgumentException(Loc.T("msg.executor.8"));
            }
            if (executor.IsActive && !activeServers.Contains(executor.ServerId ?? ""))
            {
                var where = _scope.Describe(executor.ServerId);
                throw new ArgumentException(where.Length > 0
                    ? Loc.T("msg.executor.10", model.Name, where)
                    : Loc.T("msg.executor.11", model.Name));
            }
        }
        else
        {
            executor.ServerId = null;
        }
    }

    /// <summary>
    /// Сервер исполнителя ЛОКАЛЬНОЙ модели (T-8-S1): оставляем прежний, если модель там
    /// по-прежнему включена; иначе берём этот сервер (исполнителя заводят там, где работают);
    /// иначе — единственный сервер, где модель включена. Выбирать за человека из нескольких
    /// чужих серверов нельзя: он получит понятный отказ и заведёт исполнителя там, где нужно.
    /// </summary>
    private string? PickServerFor(string? current, List<string> activeServers)
    {
        if (current is { Length: > 0 } && activeServers.Contains(current))
        {
            return current;
        }
        if (activeServers.Contains(_scope.ServerId))
        {
            return _scope.ServerId;
        }
        return activeServers.Count == 1 ? activeServers[0] : current;
    }

    /// <summary>
    /// Лимиты исполнителя для записи в таблицу (T-121): ноль и отрицательные значения из формы
    /// значат «лимиты не указаны» — в базу идёт NULL, и вычисление остатка не делается.
    /// </summary>
    private static (object? Tokens, object? Hours) Limits(Executor executor) =>
        (executor.TokenLimit is > 0 ? executor.TokenLimit.Value : null,
         executor.LimitWindowHours is > 0 ? executor.LimitWindowHours.Value : null);

    /// <summary>
    /// Таймаут ответа исполнителя для записи в таблицу (T-124): пусто — умолчание системы
    /// (30 мин), 0 — ждать без ограничения (значимое значение, в базу идёт нулём).
    /// Отрицательное значение из формы приравнивается к «не задано».
    /// </summary>
    private static object? ResponseTimeout(Executor executor) =>
        executor.ResponseTimeoutMinutes is >= 0 ? executor.ResponseTimeoutMinutes.Value : null;

    /// <summary>Создать пустые файлы профайла/декларации человека, если их ещё нет.</summary>
    private void EnsureHumanFiles(Executor executor)
    {
        if (executor.Kind != ExecutorKind.Human)
        {
            return;
        }
        if (executor.ProfilePath.Length > 0 && !File.Exists(_files.Abs(executor.ProfilePath)))
        {
            _files.WriteText(executor.ProfilePath, "{}");
        }
        if (executor.CapabilitiesPath.Length > 0 && !File.Exists(_files.Abs(executor.CapabilitiesPath)))
        {
            _files.WriteText(executor.CapabilitiesPath, AiModelService.DefaultScopeJson(executor.Nick));
        }
    }

    /// <summary>
    /// Занятость ИИ-исполнителя (ТЗ v1.37): фиксируется автоматически при исчерпании
    /// лимита провайдера («занят до» = когда лимит отпустит) и при получении от провайдера
    /// текущего % использования лимита. null-аргумент поле не трогает.
    /// </summary>
    public void SetBusy(string executorId, DateTime? busyUntil, double? limitPercent)
    {
        using var conn = _db.Open();
        if (busyUntil is not null)
        {
            Sql.Exec(conn, null, "UPDATE executors SET busy_until=@busy, updated_at=@now WHERE id=@id",
                ("@busy", Sql.ToDb(busyUntil.Value)), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", executorId));
        }
        if (limitPercent is not null)
        {
            Sql.Exec(conn, null, "UPDATE executors SET limit_percent=@pct, updated_at=@now WHERE id=@id",
                ("@pct", limitPercent.Value), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", executorId));
        }
    }

    /// <summary>
    /// Локальный пользователь (ТЗ п. 12.4): система однопользовательская, все действия в UI
    /// приписываются одному человеку с ролью owner. Создаётся при первом запуске.
    /// </summary>
    public Executor EnsureLocalOwner()
    {
        using (var conn = _db.Open())
        {
            var existing = Sql.Query(conn, null,
                "SELECT * FROM executors WHERE kind='human' AND system_role='owner' AND deleted_at IS NULL ORDER BY created_at LIMIT 1",
                Map).FirstOrDefault();
            if (existing is not null)
            {
                return existing;
            }
        }
        return CreateMember(new Executor
        {
            Nick = Environment.UserName,
            InternalName = Loc.T("msg.executor.9"),
            Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Owner,
        }, actorId: null);
    }

    private static Executor Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Nick = r.S("nick"),
        InternalName = r.S("internal_name"),
        Kind = EnumMap.ExecutorKindFromDb(r.S("kind")),
        SystemRole = EnumMap.SystemRoleFromDb(r.S("system_role")),
        ModelId = r.SN("model_id"),
        // «авто ПО» (T-153-S0): у записей прежних версий колонок ещё нет
        PluginCode = r.Has("plugin_code") ? r.S("plugin_code") : "",
        PluginOp = r.Has("plugin_op") ? r.S("plugin_op") : "",
        AccountId = r.SN("account_id"),
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        ProfilePath = r.S("profile_path"),
        CapabilitiesPath = r.S("capabilities_path"),
        IsActive = r.B("is_active"),
        BusyUntil = r.DtN("busy_until"),
        LimitPercent = r.DN("limit_percent"),
        // лимиты агента (T-121): у старых записей пусто — вычисление остатка игнорируется
        TokenLimit = r.Has("token_limit") ? r.LN("token_limit") : null,
        LimitWindowHours = r.Has("limit_window_hours") ? r.DN("limit_window_hours") : null,
        // таймаут ответа (T-124): пусто — умолчание системы, 0 — ждать без ограничения
        ResponseTimeoutMinutes = r.Has("response_timeout_minutes") && r.LN("response_timeout_minutes") is { } m
            ? (int)m
            : null,
        // почта для уведомлений (T-272): у старых записей колонок нет — значит «из логина»,
        // и это ровно то поведение, которого человек ждёт по умолчанию
        EmailFromLogin = !r.Has("email_from_login") || r.B("email_from_login"),
        NotifyEmail = r.Has("notify_email") ? r.S("notify_email") : "",
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}

/// <summary>
/// Справка об аккаунте для сервиса исполнителей (T-146): почта и жив ли он. Аккаунты лежат
/// в СЕРВЕРНОЙ базе (ТЗ п. 6.4.1), и организация про них ничего не знает — эти два поля
/// единственное, что нужно, чтобы отличить «человек вернулся» от «полный тёзка».
/// </summary>
/// <param name="Email">Почта аккаунта — она же логин (п. 2.14).</param>
/// <param name="Alive">Аккаунт не помечен удалённым (T-140).</param>
public readonly record struct MemberAccount(string Email, bool Alive);
