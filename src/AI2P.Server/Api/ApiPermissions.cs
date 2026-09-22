using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Server.Api;

/// <summary>
/// Права по ролям для HTTP API (ТЗ п. 2.2, гл. 12; этап 39).
///
/// Проверка одна на всю группу <c>/api</c>, а не на каждый из ~100 эндпойнтов: требуемая
/// роль выводится из метода и первого сегмента пути. Так правило видно целиком в одном
/// месте и не расползается по обработчикам — новый эндпойнт автоматически попадает под
/// правило своего раздела (по умолчанию — <c>editor</c>).
/// </summary>
public static class ApiPermissions
{
    /// <summary>Минимальная роль для запроса; чтение (GET/HEAD) доступно любому вошедшему.</summary>
    public static SystemRole Required(string method, string path)
    {
        var rest = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? path["/api/".Length..]
            : path.TrimStart('/');
        var section = rest.Split('/', 2)[0].ToLowerInvariant();
        // список пользователей закрыт и на чтение: там почты и телефоны (ТЗ п. 2.14)
        if (section is "accounts")
        {
            return SystemRole.Owner;
        }
        // СПИСОК СЕРВЕРОВ — АДМИНИСТРАТОРУ (T-174). Роли в организации значат разное:
        // owner — владелец всей организации (кластера целиком), admin — тот, кто отвечает
        // за настройку системы, и в том числе за СВОЙ компьютер в кластере. Всё, что
        // человек делает на своей машине — видеть список серверов, править запись своего
        // сервера, запускать репликацию руками, смотреть её диагностику, — не должно
        // требовать прав на весь кластер: до T-174 это требовало owner, и администратор
        // рядового сервера упирался в «нужна роль owner, у вас admin».
        //
        // Кластерные решения остаются за владельцем: кого ПУСТИТЬ в организацию (решение
        // по входящей заявке) и передача задач выбывшего сервера дирижёру.
        if (section is "servers")
        {
            return ClusterWide(method, rest) ? SystemRole.Owner : SystemRole.Admin;
        }
        // ОБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208) закрыто целиком, включая чтение: проверка ходит
        // в сеть от имени этого компьютера, а сама установка перезапускает сервер и снимает
        // агентов ВСЕХ организаций. Это работа хозяина машины — та же полка, что настройки
        if (section is "update")
        {
            return SystemRole.Admin;
        }
        if (method is "GET" or "HEAD" or "OPTIONS")
        {
            return SystemRole.Reader;
        }
        return section switch
        {
            // организации создаёт и правит только владелец (ТЗ п. 2.15, этап 40)
            "orgs" => SystemRole.Owner,
            // настройка системы и справочники — администратор
            // правила уведомлений (T-272) — часть настройки системы: они рассылают почту
            // от имени организации, и заводить их вправе тот же, кто правит справочники
            // архивы организации (T-40-S0) — та же полка, что настройки: архивация меняет
            // состав оперативных данных целиком, и распоряжаться ею вправе тот же, кто
            // правит справочники. Создание архива вдобавок требует дирижёра (ArchiveService)
            // плагины и MCP (T-114-S0) — та же полка, что справочники: плагин заводит записи
            // справочника действий и записи общего опыта, то есть меняет то, чем работают
            // агенты ВСЕЙ организации. Ручной путь к программе — «моя машина», и это ровно
            // роль admin (хозяин одного компьютера кластера, b26391b3); owner проходит по рангу
            "settings" or "models" or "actions" or "skills" or "roles" or "ioformats"
                or "task-statuses" or "imports" or "executors" or "notifications"
                or "archives" or "plugins" or "packs"
                => SystemRole.Admin,
            // проекты, команды, расписания и правила безопасности — администратор проектов
            "projects" or "teams" or "schedules" or "security-rules" => SystemRole.ProjectAdmin,
            // своё состояние UI и свой аккаунт правит кто угодно, включая читателя
            "state" or "account" => SystemRole.Reader,
            // задачи, чат, задания, файлы, опыт — редактор
            _ => SystemRole.Editor,
        };
    }

    /// <summary>
    /// Решение по разделу серверов, которое касается ВСЕЙ организации, а не своего
    /// компьютера (T-174), — только владельцу:
    /// <list type="bullet">
    /// <item><c>POST /api/servers/requests/{id}/{решение}</c> — принять или отклонить чужой
    /// сервер: это приём нового участника в организацию;</item>
    /// <item><c>POST /api/servers/{id}/takeover</c> — передать задачи выбывшего сервера
    /// дирижёру: единственный случай смены владельца строк не её сервером (ТЗ гл. 6).</item>
    /// </list>
    /// </summary>
    /// <param name="rest">Путь без префикса <c>/api/</c>.</param>
    private static bool ClusterWide(string method, string rest)
    {
        if (method is "GET" or "HEAD" or "OPTIONS")
        {
            return false;   // читать список серверов и заявки — дело администратора машины
        }
        var parts = rest.Split('/');
        return (parts.Length > 1 && parts[1].Equals("requests", StringComparison.OrdinalIgnoreCase))
               || parts[^1].Equals("takeover", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверка запроса: не вошёл — 401, роли не хватает — 403, настройки сервера без
    /// логина admin'а сервера — 403. null — запрос разрешён.
    /// </summary>
    public static IResult? Check(HttpContext http, CurrentUserAccessor current)
    {
        var path = http.Request.Path.Value ?? "";
        var method = http.Request.Method;

        if (current.AccountId is null)
        {
            return Results.Problem(Loc.T("msg.apiPermissions.1"), statusCode: StatusCodes.Status401Unauthorized);
        }

        // настройки самого сервера (форма локального сервера, ТЗ гл. 10, гл. 12) — отдельный
        // локальный логин admin'а сервера: адрес, порт и каталоги этого компьютера правит
        // только его физический хозяин. Читать их может любой вошедший — в форме они видны
        // только для чтения (todo41 пп. 2–3)
        if (path.StartsWith("/api/servers/local", StringComparison.OrdinalIgnoreCase)
            && method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            return current.IsServerAdmin
                ? null
                : Results.Problem(Loc.T("msg.apiPermissions.2"),
                    statusCode: StatusCodes.Status403Forbidden);
        }

        // Доступ к данным организации даёт только УЧАСТИЕ в ней (ТЗ п. 2.15, этап 40).
        // Организацию называет сам запрос (сегмент URL или заголовок), поэтому без этой
        // проверки любой вошедший читал бы чужую организацию, просто подставив её код.
        // Разделы вне организации — список организаций, свой аккаунт и серверы — проверку
        // не проходят; к ним в T-148 добавились настройки приложения и чтение состояния UI,
        // но только когда организации у запроса нет вовсе (см. ниже).
        var outsideOrg = path.StartsWith("/api/orgs", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/api/servers", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWith("/api/account", StringComparison.OrdinalIgnoreCase)
                         // ПОСТАВЛЯЕМАЯ ДОКУМЕНТАЦИЯ (T-17-S1) — файлы рядом с приложением,
                         // организации в них нет вовсе. Читать их вправе любой вошедший, в том
                         // числе на сервере, который ещё ждёт решения по своей заявке: чтобы
                         // разобраться, что делать дальше, документация нужна именно там
                         || path.StartsWith("/api/doc", StringComparison.OrdinalIgnoreCase)
                         // настройки приложения (config.json) и ЧТЕНИЕ состояния UI — только
                         // когда организации у запроса НЕТ ВОВСЕ (T-148): это и есть урезанный
                         // интерфейс сервера, ждущего решения по заявке. Организация названа,
                         // а вошедший ей чужой — прежний отказ, иначе через эти два адреса
                         // читали бы чужое. Запись состояния UI не разрешается и здесь:
                         // оно лежит в базе организации, а её нет
                         || (current.Org is null
                             && (path.StartsWith("/api/settings", StringComparison.OrdinalIgnoreCase)
                                 || (path.StartsWith("/api/state", StringComparison.OrdinalIgnoreCase)
                                     && method is "GET" or "HEAD" or "OPTIONS")));
        if (!outsideOrg)
        {
            if (current.Org is null)
            {
                return Results.Problem(Loc.T("msg.apiPermissions.3"),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            if (current.Executor is null)
            {
                return Results.Problem(
                    Loc.T("msg.apiPermissions.4", current.Org.Org.Name),
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var required = Required(method, path);
        // СПИСОК СЕРВЕРОВ — ЕЩЁ И АДМИНИСТРАТОРУ СЕРВЕРА (T-148). Это карта кластера с точки
        // зрения ЭТОЙ установки, и её физический хозяин вправе её видеть, править и запускать
        // репликацию руками — сеанс с сервером за NAT начинает только он сам (ТЗ гл. 6).
        // Без этого человек, подключивший свой сервер к чужой организации, получал бы там
        // обычную роль (editor) и терял доступ к списку серверов СОБСТВЕННОЙ машины.
        // Роль раздела при этом не важна (T-174): раньше здесь стояло «нужен owner», а после
        // разделения ролей большинству запросов раздела хватает admin — и проверка перестала
        // бы срабатывать ровно для тех, ради кого сделана (у них роль ниже admin).
        if (current.IsServerAdmin
            && path.StartsWith("/api/servers", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // СПИСОК ОРГАНИЗАЦИЙ — ХОЗЯИНУ ЭТОГО КОМПЬЮТЕРА (T-201-S0), а не владельцу открытой
        // сейчас организации. Организации — это то, что лежит на ДАННОЙ установке (своя БД,
        // свой каталог), и раздел ими и управляет: завести, переименовать, удалить с диска,
        // вступить. Роль же считается в организации ЗАПРОСА, и у хозяина машины, перешедшего
        // в организацию, приехавшую репликацией, она admin (участником его делает join,
        // T-186-S0) — раздел пропадал ровно у того, кому принадлежит компьютер.
        // Условие проверяется вторым: признак стоит обхода всех организаций установки.
        if (path.StartsWith("/api/orgs", StringComparison.OrdinalIgnoreCase)
            && current.IsMachineOwner)
        {
            return null;
        }
        if (Ai2pAuth.Rank(current.Role) < Ai2pAuth.Rank(required))
        {
            return Results.Problem(
                Loc.T("msg.apiPermissions.5", required.ToDb(), current.Role.ToDb()),
                statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }
}
