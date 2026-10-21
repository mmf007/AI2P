using System.Net;
using AI2P.Core;
using AI2P.UI.Services;

namespace AI2P.Server.Api;

/// <summary>
/// ВСТРОЕННЫЙ РЕЖИМ НА СТОРОНЕ СЕРВЕРА (T-507-S0, плагин VS Code 8.2): заголовки
/// встраивания и заглушка вместо формы входа. Признак — параметр <c>embed=vscode</c>
/// запроса (<see cref="EmbedParams"/>); у обычных страниц всё остаётся как было.
/// </summary>
public static class EmbedMode
{
    /// <summary>Кто вправе показывать страницу в рамке: сам сервер и WebView VS Code.</summary>
    public const string FrameAncestors = "frame-ancestors 'self' vscode-webview: vscode-file:";

    public static bool IsEmbedRequest(HttpRequest request) =>
        EmbedParams.IsEmbed(request.Query["embed"].ToString());

    /// <summary>Заголовки ответа перед отправкой. Встроенный режим: X-Frame-Options (его
    /// ставит Antiforgery на каждую страницу Blazor, и с ним iframe не грузится вовсе,
    /// T-499-S0) снимается, вместо него — CSP frame-ancestors. Обычная HTML-страница:
    /// X-Frame-Options: SAMEORIGIN остаётся (и ставится, если его вдруг не поставили).</summary>
    public static void Apply(HttpRequest request, HttpResponse response)
    {
        var headers = response.Headers;
        if (IsEmbedRequest(request))
        {
            headers.Remove("X-Frame-Options");
            headers["Content-Security-Policy"] = FrameAncestors;
            return;
        }
        if ((response.ContentType ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            && !headers.ContainsKey("X-Frame-Options"))
        {
            headers["X-Frame-Options"] = "SAMEORIGIN";
        }
    }

    /// <summary>Middleware: заголовки выставляются в последний момент (OnStarting), когда
    /// Antiforgery свои уже поставил.</summary>
    public static void UseAi2pEmbedHeaders(this WebApplication app) =>
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                Apply(ctx.Request, ctx.Response);
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>Встроенная страница без входа: вместо перехода на /login (его форма с
    /// antiforgery в iframe не работает) — понятная заглушка «нужно подключить VS Code».
    /// Наверх уходит ai2p.needAuth: по нему расширение возьмёт токен и войдёт само (8.3).</summary>
    public static Task WriteNeedAuthAsync(HttpContext ctx)
    {
        var embed = EmbedParams.Parse(key => ctx.Request.Query[key].ToString(), Loc.Languages);
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        return ctx.Response.WriteAsync(NeedAuthPage(embed?.Lang ?? Loc.Lang, embed?.Theme == "dark"));
    }

    public static string NeedAuthPage(string lang, bool dark)
    {
        var title = WebUtility.HtmlEncode(Loc.In(lang, "embed.needAuth.title"));
        var text = WebUtility.HtmlEncode(Loc.In(lang, "embed.needAuth.text"));
        var (bg, fg) = dark ? ("#1e1e1e", "#d4d4d4") : ("#ffffff", "#1f1f1f");
        return $$"""
            <!DOCTYPE html>
            <html lang="{{WebUtility.HtmlEncode(lang)}}"><head><meta charset="utf-8" /><title>AI2P</title></head>
            <body data-embed-needauth="1" style="margin:0;padding:24px;font-family:system-ui,sans-serif;background:{{bg}};color:{{fg}};">
            <h3 style="margin-top:0;">{{title}}</h3>
            <p>{{text}}</p>
            <script>if (window.parent !== window) { window.parent.postMessage({ type: 'ai2p.needAuth' }, '*'); }</script>
            </body></html>
            """;
    }
}
