using AI2P.Core;
using System.Net;
using System.Security.Claims;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage;

namespace AI2P.Server.Api;

/// <summary>
/// Аутентификация и права (ТЗ гл. 12, этап 39).
///
/// Два независимых логина:
/// 1. <see cref="Scheme"/> — аккаунт пользователя (рабочий вход, cookie-аутентификация
///    ASP.NET Core); в дальнейшем реплицируется между серверами кластера;
/// 2. <see cref="AdminScheme"/> — <b>admin сервера</b>: отдельный локальный логин, которым
///    правятся настройки самого сервера. Хранится в `secrets.json` (рядом с config.json) и
///    НИКУДА не реплицируется — менять настройки сервера может только физический хозяин
///    компьютера.
/// </summary>
public static class Ai2pAuth
{
    /// <summary>Схема (и cookie) рабочего входа пользователя.</summary>
    public const string Scheme = Core.Api.Ai2pSchemes.User;

    /// <summary>Схема (и cookie) входа admin'а сервера — отдельная от пользовательской.</summary>
    public const string AdminScheme = Core.Api.Ai2pSchemes.ServerAdmin;

    /// <summary>Заголовок внутреннего вызова: UI (Blazor Server) ходит в собственное API
    /// по петле (localhost) своим HttpClient, cookie браузера туда не попадает.</summary>
    public const string InternalTokenHeader = Core.Api.Ai2pHeaders.InternalToken;

    /// <summary>Заголовок внутреннего вызова: от чьего имени идёт запрос из circuit'а UI.</summary>
    public const string InternalAccountHeader = Core.Api.Ai2pHeaders.InternalAccount;

    /// <summary>Заголовок внутреннего вызова: вошедший является ещё и admin'ом сервера.</summary>
    public const string InternalAdminHeader = Core.Api.Ai2pHeaders.ServerAdmin;

    /// <summary>
    /// Случайный токен процесса: живёт только в памяти, наружу не выдаётся и никуда не пишется.
    /// Знать его может лишь код в этом же процессе (наш UI), поэтому внутреннему вызову
    /// с этим токеном можно верить — при условии, что запрос пришёл с петлевого адреса.
    /// </summary>
    public static string InternalToken { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    // --- admin сервера: логин и хэш пароля в secrets.json (не реплицируются) ---

    public const string AdminLoginRef = "server.adminLogin";
    public const string AdminHashRef = "server.adminPasswordHash";

    /// <summary>Логин admin'а сервера; по умолчанию — «admin».</summary>
    public static string AdminLogin(SecretStore secrets) =>
        secrets.Read(AdminLoginRef) is { Length: > 0 } login ? login : "admin";

    /// <summary>Пароль admin'а сервера задан (иначе первый вход задаёт его).</summary>
    public static bool AdminPasswordSet(SecretStore secrets) =>
        secrets.Read(AdminHashRef) is { Length: > 0 };

    public static bool VerifyAdminPassword(SecretStore secrets, string password) =>
        PasswordHash.Verify(secrets.Read(AdminHashRef), password);

    public static void SetAdminPassword(SecretStore secrets, string password)
    {
        if (password.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.ai2pAuth.1"));
        }
        secrets.Write(AdminHashRef, PasswordHash.Hash(password));
    }

    // --- разбор личности запроса ---

    /// <summary>Запрос пришёл с этого же компьютера (петлевой адрес) — «локальный вход».</summary>
    public static bool IsLocalRequest(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return true; // in-process вызов без сокета
        }
        return IPAddress.IsLoopback(remote)
               || (ctx.Connection.LocalIpAddress is { } local && remote.Equals(local));
    }

    /// <summary>Внутренний вызов UI: правильный токен процесса и петлевой адрес.</summary>
    public static string? InternalAccountId(HttpContext ctx)
    {
        if (!IsInternalCall(ctx))
        {
            return null;
        }
        var account = ctx.Request.Headers[InternalAccountHeader].ToString();
        return account.Length > 0 ? account : null;
    }

    /// <summary>
    /// Внутренний вызов UI от пользователя, вошедшего ЕЩЁ И администратором сервера
    /// (ТЗ гл. 12). Отдельная cookie администратора живёт в браузере и до собственного API,
    /// вызываемого по петле, не доходит — поэтому признак передаётся заголовком на тех же
    /// условиях, что и аккаунт: токен процесса знает только код в этом же процессе, а запрос
    /// обязан прийти с петлевого адреса.
    /// </summary>
    public static bool InternalServerAdmin(HttpContext ctx) =>
        IsInternalCall(ctx) && ctx.Request.Headers[InternalAdminHeader].ToString() == "1";

    /// <summary>Запрос сделан нашим же UI: правильный токен процесса и петлевой адрес.</summary>
    private static bool IsInternalCall(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue(InternalTokenHeader, out var token)
        && string.Equals(token.ToString(), InternalToken, StringComparison.Ordinal)
        && IsLocalRequest(ctx);

    /// <summary>
    /// ClaimsPrincipal вошедшего пользователя — только АККАУНТ (ТЗ п. 2.15, этап 40).
    /// Исполнитель и роль в cookie не кладутся: они принадлежат организации, а организаций
    /// у аккаунта может быть несколько (и роль в них разная). Оба определяются на каждый
    /// запрос по его организации — <see cref="CurrentUserAccessor"/>.
    /// </summary>
    public static ClaimsPrincipal Principal(Account account, string scheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id),
            new(ClaimTypes.Name, account.Name.Length > 0 ? account.Name : account.Email),
            new(ClaimTypes.Email, account.Email),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    /// <summary>Старшинство ролей (ТЗ п. 2.2): чем больше, тем больше прав.</summary>
    public static int Rank(SystemRole role) => role switch
    {
        SystemRole.Owner => 4,
        SystemRole.Admin => 3,
        SystemRole.ProjectAdmin => 2,
        SystemRole.Editor => 1,
        _ => 0,
    };
}

/// <summary>
/// Личность и организация текущего запроса (ТЗ гл. 12, п. 2.15; этапы 39–40): аккаунт,
/// организация запроса, исполнитель-актор в ней и роль.
///
/// Заменяет прежний синглтон «локальный пользователь»: актор берётся из контекста запроса,
/// а не из замыкания, созданного при старте приложения. С этапа 40 к нему добавилась
/// организация — данных вне организации в системе почти нет, и обработчик обязан знать,
/// в чьей БД он работает.
/// </summary>
public sealed class CurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly OrgRegistry _registry;

    public CurrentUserAccessor(IHttpContextAccessor http, OrgRegistry registry)
    {
        _http = http;
        _registry = registry;
    }

    private const string AccountItem = "ai2p:account";
    private const string ExecutorItem = "ai2p:executorEntity";
    private const string OrgItem = "ai2p:orgContext";

    /// <summary>Рабочая среда запроса (T-45-S0) — она нужна и тогда, когда данные читаются
    /// из архива: личность вошедшего и реестр архивов живут только в ней.</summary>
    private const string HomeItem = "ai2p:homeContext";

    /// <summary>Ключ, которым middleware маршрутизации кладёт код организации из пути
    /// <c>/ai2p/&lt;код&gt;/api/…</c> (браузер шлёт относительные ссылки на файлы).</summary>
    public const string OrgCodeItem = "ai2p:orgCode";

    /// <summary>Реестр организаций — нужен обработчикам, работающим вне организации
    /// (список организаций, аккаунты, первый старт).</summary>
    public OrgRegistry Registry => _registry;

    /// <summary>Аккаунт вошедшего; null — запрос без аутентификации.
    /// В пределах запроса читается из БД один раз (эндпойнт спрашивает актора несколько раз).</summary>
    public Account? Account
    {
        get
        {
            var ctx = _http.HttpContext;
            var id = AccountId;
            if (id is null)
            {
                return null;
            }
            if (ctx is not null && ctx.Items.TryGetValue(AccountItem, out var cached))
            {
                return cached as Account;
            }
            var account = _registry.Accounts.Get(id);
            if (ctx is not null)
            {
                ctx.Items[AccountItem] = account;
            }
            return account;
        }
    }

    public string? AccountId
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null)
            {
                return null;
            }
            if (Ai2pAuth.InternalAccountId(ctx) is { } internalId)
            {
                return internalId;
            }
            return ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    /// <summary>
    /// Организация запроса (ТЗ п. 2.15). Определяется по порядку: сегмент пути
    /// <c>/ai2p/&lt;код&gt;/…</c> (так ходит браузер по относительным ссылкам) → заголовок
    /// <c>X-AI2P-Org</c> (внутренние вызовы UI и внешние клиенты) → параметр <c>?org=</c> →
    /// организация вошедшего по умолчанию (у одного пользователя она обычно одна).
    /// null — вошедший не участник ни одной организации (или организаций ещё нет).
    /// </summary>
    public OrgContext? Org
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(OrgItem, out var cached))
            {
                return cached as OrgContext;
            }
            var org = Resolve(ctx);
            // СРЕДА ЗАПРОСА (T-45-S0): выбран архив — обработчик работает с ЕГО базой и его
            // файлами. Подменяется именно набор сервисов, поэтому ни один обработчик про
            // архивы не знает и знать не должен
            if (org is not null && ArchiveAsked(ctx) is { Length: > 0 } asked)
            {
                org = _registry.ArchiveContext(org, asked) ?? org;
            }
            if (ctx is not null)
            {
                ctx.Items[OrgItem] = org;
            }
            return org;
        }
    }

    /// <summary>
    /// РАБОЧАЯ СРЕДА запроса (T-45-S0) — организация как она есть, даже когда на экране открыт
    /// архив. Нужна двум вещам. Первая — ЛИЧНОСТЬ: исполнитель вошедшего и его роль лежат в
    /// базе организации, а в архиве их нет вовсе, и роль там оказалась бы «читатель» у всех.
    /// Вторая — САМ РЕЕСТР АРХИВОВ и перенос данных: они принадлежат рабочей среде, и
    /// «восстановить из архива» пишет как раз в неё.
    /// </summary>
    public OrgContext? Home
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(HomeItem, out var cached))
            {
                return cached as OrgContext;
            }
            var org = Resolve(ctx);
            if (ctx is not null)
            {
                ctx.Items[HomeItem] = org;
            }
            return org;
        }
    }

    /// <summary>Выбранный архив: заголовок <c>X-AI2P-Archive</c> (так ходит UI) либо параметр
    /// адреса <c>arc=</c> (так помечены ссылки внутри архива, T-42-S0). Пусто — рабочая среда.</summary>
    public string ArchiveAsked() => ArchiveAsked(_http.HttpContext);

    private static string ArchiveAsked(HttpContext? ctx)
    {
        if (ctx is null)
        {
            return "";
        }
        var asked = ctx.Request.Headers[Core.Api.Ai2pHeaders.Archive].ToString();
        if (asked.Length == 0)
        {
            asked = ctx.Request.Query[Core.ArchiveLinks.Param].ToString();
        }
        return asked.Trim();
    }

    /// <summary>Запрос идёт по данным АРХИВА, а не рабочей среды (T-45-S0): такие запросы
    /// только читают.</summary>
    public bool InArchive => ArchiveAsked().Length > 0 && !ReferenceEquals(Org, Home);

    private OrgContext? Resolve(HttpContext? ctx)
    {
        if (ctx is null)
        {
            return null;
        }
        if (ctx.Items.TryGetValue(OrgCodeItem, out var fromPath)
            && _registry.ByCode(fromPath as string) is { } byPath)
        {
            return byPath;
        }
        var asked = ctx.Request.Headers[Core.Api.Ai2pHeaders.Org].ToString();
        if (asked.Length == 0)
        {
            asked = ctx.Request.Query["org"].ToString();
        }
        if (asked.Length > 0)
        {
            // клиент может назвать организацию и кодом, и id — принимаем оба
            return _registry.ByCode(asked) ?? _registry.ById(asked);
        }
        return AccountId is { } accountId ? Default(accountId) : null;
    }

    /// <summary>Организация по умолчанию: первая, где вошедший — участник (ТЗ гл. 11).</summary>
    public OrgContext? Default(string accountId)
    {
        foreach (var org in _registry.Orgs.List(includeInactive: false))
        {
            var context = _registry.Context(org);
            if (context.Executors.ByAccount(accountId) is not null)
            {
                return context;
            }
        }
        return null;
    }

    /// <summary>Исполнитель-человек вошедшего В ЭТОЙ ОРГАНИЗАЦИИ — актор всех его действий
    /// в её журнале (п. 6.4.3). null — вошедший не участник организации запроса.</summary>
    public Executor? Executor
    {
        get
        {
            var ctx = _http.HttpContext;
            var accountId = AccountId;
            if (accountId is null)
            {
                return null;
            }
            if (ctx is not null && ctx.Items.TryGetValue(ExecutorItem, out var cached))
            {
                return cached as Executor;
            }
            // ИМЕННО Home, а не Org (T-45-S0): при просмотре архива данные читаются из его
            // базы, а исполнителей в ней нет — роль вошедшего стала бы «читатель» у всех,
            // и вернуться в рабочую среду он смог бы, но уже без своих прав
            var executor = Home?.Executors.ByAccount(accountId);
            if (ctx is not null)
            {
                ctx.Items[ExecutorItem] = executor;
            }
            return executor;
        }
    }

    /// <summary>
    /// Роль вошедшего в организации запроса (ТЗ п. 2.2); не участник — reader.
    ///
    /// ХОЗЯИН УСТАНОВКИ (T-148) — владелец, даже когда участия ещё нет: см.
    /// <see cref="SoleAccount"/>. Иначе роль осталась бы «читатель», а список серверов закрыт
    /// владельцем — и урезанный интерфейс ожидания не мог бы ни спросить решение по заявке,
    /// ни запустить репликацию, которой участие как раз и приезжает.
    /// </summary>
    public SystemRole Role => Executor?.SystemRole
                              ?? (SoleAccount ? SystemRole.Owner : SystemRole.Reader);

    /// <summary>
    /// ВОШЕДШИЙ — ЕДИНСТВЕННЫЙ ЧЕЛОВЕК ЭТОЙ УСТАНОВКИ (T-148), то есть её хозяин: аккаунт
    /// заводится визардом первого старта, а второй завести неоткуда — пользователями
    /// управляют внутри организации, которой тут ещё нет.
    ///
    /// По этому признаку сервер, ждущий подключения, отличается от обычного сервера, куда
    /// зашёл человек без доступа: первому нужен урезанный интерфейс со списком серверов
    /// (заявка, решение по ней, ручной пуск репликации), второму — страница «вы не состоите
    /// ни в одной организации». Признак сам собой перестаёт действовать, как только участие
    /// приезжает репликацией: тогда роль даёт исполнитель.
    /// </summary>
    public bool SoleAccount =>
        AccountId is { } id && _registry.Accounts.List() is { Count: 1 } only && only[0].Id == id;

    /// <summary>
    /// Пользователь дополнительно вошёл как admin сервера (вторая cookie).
    ///
    /// Три источника, и все три нужны: личность запроса (браузер прислал cookie), пометка
    /// middleware (вторая схема не разбирается автоматически — она не схема по умолчанию)
    /// и заголовок внутреннего вызова — UI ходит в собственное API по петле, куда cookie
    /// администратора не попадает, и без него настройки сервера были бы недоступны из UI
    /// даже после входа администратором.
    /// </summary>
    public bool IsServerAdmin
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null)
            {
                return false;
            }
            return ctx.User.Identities.Any(i => i.AuthenticationType == Ai2pAuth.AdminScheme)
                   || ctx.Items.ContainsKey(Ai2pAuth.AdminScheme)
                   || Ai2pAuth.InternalServerAdmin(ctx);
        }
    }

    /// <summary>
    /// ХОЗЯИН ЭТОГО КОМПЬЮТЕРА (T-186-S0, T-201-S0): владелец системы (роль owner хотя бы
    /// в одной организации этой установки) либо локальный администратор сервера.
    ///
    /// Признак НЕ зависит от организации запроса — и в этом весь смысл. Список организаций,
    /// как и список серверов (T-174), описывает саму установку, а не открытую сейчас
    /// организацию: считать право по роли в ней значит терять его при переходе в организацию,
    /// приехавшую репликацией (там у хозяина машины роль admin, T-186-S0) — жалоба
    /// «переключился на организацию с S1, и список организаций из настроек исчез».
    /// </summary>
    public bool IsMachineOwner =>
        AccountId is { Length: > 0 } id && (IsServerAdmin || _registry.IsOwnerAccount(id));
}
