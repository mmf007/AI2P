using AI2P.Server.Api;

namespace AI2P.Server.Org;

/// <summary>
/// Организация в URL (ТЗ гл. 11, этап 40): всё приложение живёт по адресу
/// <c>{basePath}/{код организации}/…</c>, например <c>/ai2p/acme/task/T-18</c>.
///
/// Почему сегментом пути, а не только заголовком: браузер сам достраивает относительные
/// ссылки (картинки и видео в описаниях, <c>_blazor</c>, <c>_content</c>) от &lt;base href&gt;
/// страницы. Сделав base href равным <c>/ai2p/acme/</c>, мы получаем правильную организацию
/// во всех таких запросах бесплатно и без правки самих ссылок.
///
/// Служебные адреса общие для всех организаций (API, ресурсы Blazor, вход) — у них сегмент
/// организации отрезается, а сама организация запоминается в контексте запроса. Страницам
/// путь не меняется: Blazor сопоставляет маршруты относительно base href, поэтому
/// <c>/ai2p/acme/task/T-18</c> для роутера — это по-прежнему <c>/task/{id}</c>.
/// </summary>
public static class OrgRouting
{
    /// <summary>Разделы, общие для всех организаций: у них сегмент организации отрезается.</summary>
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "_blazor", "_framework", "_content", "login", "logout", "server-admin", "no-org",
    };

    /// <summary>Разбор сегмента организации; ставится ДО маршрутизации — он меняет путь.</summary>
    public static void UseAi2pOrgPath(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<OrgRegistry>();
        app.Use(async (ctx, next) =>
        {
            // путь может быть пустым: заход на «{basePath}» без завершающего слэша отдаёт
            // UsePathBase остаток "" — искать в нём разделитель нечего
            var path = ctx.Request.Path.Value ?? "";
            var slash = path.Length > 1 ? path.IndexOf('/', 1) : -1;
            var first = slash < 0 ? path.Trim('/') : path[1..slash];
            if (first.Length > 0 && registry.IsOrgCode(first))
            {
                ctx.Items[CurrentUserAccessor.OrgCodeItem] = first;
                var rest = slash < 0 ? "/" : path[slash..];
                var second = rest.Length > 1 ? rest[1..].Split('/', 2)[0] : "";
                if (Shared.Contains(second))
                {
                    // общий раздел: организация уже запомнена, дальше идёт обычный путь
                    ctx.Request.Path = rest;
                }
                else
                {
                    // страница приложения: сегмент организации становится частью PathBase.
                    // Именно частью, а не «отрезанным куском»: от PathBase считается base href
                    // страницы и BaseUri у NavigationManager, поэтому и адреса, которые строит
                    // приложение, и маршруты Blazor остаются внутри организации сами собой.
                    ctx.Request.PathBase = ctx.Request.PathBase.Add("/" + first);
                    ctx.Request.Path = rest;
                }
            }
            await next();
        });
    }

    /// <summary>
    /// Заход на корень без организации: увести в организацию вошедшего по умолчанию.
    /// Ставится ПОСЛЕ аутентификации — организация выбирается по участию аккаунта.
    /// </summary>
    public static void UseAi2pOrgHome(this WebApplication app, string basePath)
    {
        app.Use(async (ctx, next) =>
        {
            // корень приложения — и «{basePath}/», и «{basePath}» без завершающего слэша
            var path = ctx.Request.Path.Value ?? "";
            if (path is not ("" or "/")
                || ctx.Items.ContainsKey(CurrentUserAccessor.OrgCodeItem)
                || ctx.User.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }
            var current = ctx.RequestServices.GetRequiredService<CurrentUserAccessor>();
            if (current.AccountId is not { } accountId)
            {
                await next();
                return;
            }
            var org = current.Default(accountId);
            if (org is null)
            {
                // ХОЗЯИН УСТАНОВКИ, ЕЩЁ НЕ СТАВШИЙ УЧАСТНИКОМ (T-148) — это сервер, который
                // подключается: заявка подана и ждёт решения либо уже подтверждена, а участие
                // ещё не приехало репликацией. Ему открывается сам интерфейс, но урезанный:
                // данных нет, зато есть настройки и список серверов, а в нём — решение
                // по заявке и ручной пуск репликации (дирижёр серверу за NAT не звонит,
                // сеанс начинает он сам). Ждать в интерфейсе — требование T-148
                if (current.SoleAccount)
                {
                    await next();
                    return;
                }
                // организации есть, но вошедший не участник ни одной — объясняем это,
                // иначе пользователь упирался бы в пустой экран без причины
                ctx.Response.Redirect(basePath + "/no-org");
                return;
            }
            ctx.Response.Redirect(basePath + "/" + org.Code + "/");
        });
    }
}
