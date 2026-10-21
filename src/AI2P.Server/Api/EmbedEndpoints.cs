using System.Net;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Org;
using AI2P.Storage.Services;
using Microsoft.AspNetCore.Authentication;

namespace AI2P.Server.Api;

/// <summary>
/// ТОКЕН УСТРОЙСТВА И ВХОД ВСТРОЕННОГО РЕЖИМА (T-508-S0, этап 8.3 плагина VS Code).
///
/// Цепочка входа: кнопка «Подключить VS Code» выпускает токен устройства (обычная cookie
/// входа, <c>POST /api/device-tokens</c>) и передаёт его расширению; расширение хранит его
/// в SecretStorage и на каждое окно меняет на одноразовый <c>embedToken</c>
/// (<c>POST /api/embed/exchange</c>); iframe открывает <c>/api/embed/enter?embedToken=</c>,
/// и сервер ставит cookie встроенного режима (SameSite=None; Secure). Дальше Blazor и
/// SignalR живут на ней, в том числе при переподключении.
///
/// Адреса <c>/api/embed/*</c> согласованы с расширением (T-509-S0). Они открыты без входа и
/// стоят ОТДЕЛЬНОЙ группой: общая проверка прав группы <c>/api</c> требует cookie, а у
/// расширения есть только токен устройства — им и подтверждается личность.
/// </summary>
public static class EmbedEndpoints
{
    public static void MapAi2pEmbed(this WebApplication app, string basePath)
    {
        var registry = app.Services.GetRequiredService<OrgRegistry>();
        var current = app.Services.GetRequiredService<CurrentUserAccessor>();

        // --- управление токенами: обычный вход, участник организации запроса ---
        // (группа /api с её проверкой прав: раздел device-tokens открыт и читателю — токен
        // даёт ровно права своего человека, не шире)
        var api = app.MapGroup("/api/device-tokens");
        api.AddEndpointFilter(async (ctx, next) =>
            ApiPermissions.Check(ctx.HttpContext, current) ?? await next(ctx));

        OrgContext Org() => current.Org ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.3"));
        string Account() => current.AccountId ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.2"));

        api.MapGet("", (string? projectId, bool? all) => ApiEndpoints.Handle(() =>
            Results.Ok(registry.DeviceTokens.List(Account(), Org().Org.Id, projectId, all == true))));

        api.MapPost("", (DeviceTokenIssueDto dto) => ApiEndpoints.Handle(() =>
        {
            // встроенный режим сам себе токенов не выпускает: иначе вкладка VS Code плодила бы
            // ключи, переживающие отзыв того, которым она вошла
            if (Ai2pAuth.EmbedOrgId(current.HttpUser) is not null)
            {
                return Results.Problem(Loc.T("msg.deviceToken.3"), statusCode: StatusCodes.Status403Forbidden);
            }
            var org = Org();
            var project = org.Projects.Get(dto.ProjectId);
            if (project is null || project.DeletedAt is not null)
            {
                return Results.Problem(Loc.T("msg.deviceToken.4"), statusCode: StatusCodes.Status404NotFound);
            }
            var name = dto.Name.Trim().Length > 0 ? dto.Name : "VS Code";
            var (record, token) = registry.DeviceTokens.Issue(Account(), org.Org.Id, project.Id, name);
            return Results.Ok(new DeviceTokenIssuedDto
            {
                Token = token,
                Record = record,
                OrgCode = org.Org.Code,
            });
        }));

        api.MapDelete("/{id}", (string id) => ApiEndpoints.Handle(() =>
            registry.DeviceTokens.Revoke(id, Account())
                ? Results.Ok()
                : Results.Problem(Loc.T("msg.deviceToken.5"), statusCode: StatusCodes.Status404NotFound)));

        // «Отключить VS Code»: все токены человека по проекту
        api.MapPost("/revoke-project", (DeviceTokenProjectDto dto) => ApiEndpoints.Handle(() =>
            Results.Ok(new { revoked = registry.DeviceTokens.RevokeProject(Account(), Org().Org.Id, dto.ProjectId) })));

        // --- вход встроенного режима: без cookie, личность — токен устройства ---
        var embed = app.MapGroup("/api/embed").AllowAnonymous().DisableAntiforgery();

        // токен устройства из заголовка Authorization: Bearer — и вся проверка его годности
        (DeviceToken Device, Account Account, OrgContext Org)? Device(HttpContext ctx)
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            const string bearer = "Bearer ";
            if (!header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
                || registry.DeviceTokens.Verify(header[bearer.Length..]) is not { } device)
            {
                return null;
            }
            // человек отключён, удалён или уже не участник организации — токен не действует,
            // хотя и не отозван: права токена не шире прав его человека
            if (registry.Accounts.Get(device.AccountId) is not { IsActive: true, DeletedAt: null } account
                || registry.ById(device.OrgId) is not { } org
                || org.Executors.ByAccount(device.AccountId) is null)
            {
                return null;
            }
            return (device, account, org);
        }

        IResult Unauthorized() =>
            Results.Problem(Loc.T("msg.deviceToken.6"), statusCode: StatusCodes.Status401Unauthorized);

        embed.MapPost("/exchange", (HttpContext ctx) =>
        {
            if (Device(ctx) is not { } found)
            {
                return Unauthorized();
            }
            var token = registry.EmbedTokens.Issue(found.Device);
            return Results.Ok(new
            {
                embedToken = token,
                expiresIn = (int)EmbedTokenStore.Lifetime.TotalSeconds,
                org = found.Org.Org.Code,
                projectId = found.Device.ProjectId,
            });
        });

        embed.MapGet("/info", (HttpContext ctx) =>
        {
            if (Device(ctx) is not { } found)
            {
                return Unauthorized();
            }
            var project = found.Org.Projects.Get(found.Device.ProjectId);
            return Results.Ok(new
            {
                version = AppInfo.Version,
                hasArchives = found.Org.Archives.List().Count > 0,
                org = found.Org.Org.Code,
                orgName = found.Org.Org.Name,
                projectId = found.Device.ProjectId,
                projectName = project?.Name ?? "",
                user = found.Account.Name.Length > 0 ? found.Account.Name : found.Account.Email,
            });
        });

        embed.MapGet("/enter", async (HttpContext ctx, string? embedToken, string? to) =>
        {
            var entry = registry.EmbedTokens.Redeem(embedToken ?? "");
            if (entry is null
                || registry.Accounts.Get(entry.AccountId) is not { IsActive: true, DeletedAt: null } account
                || registry.ById(entry.OrgId) is not { } org
                || !registry.DeviceTokens.IsActive(entry.DeviceTokenId))
            {
                return Results.Content(Page(Loc.T("msg.deviceToken.7")), "text/html; charset=utf-8", null,
                    StatusCodes.Status401Unauthorized);
            }
            var device = registry.DeviceTokens.Get(entry.DeviceTokenId)!;
            await ctx.SignInAsync(Ai2pAuth.EmbedScheme, Ai2pAuth.EmbedPrincipal(account, device),
                new AuthenticationProperties { IsPersistent = true });
            registry.Accounts.LogAuth(EventTypes.AccountEmbedLogin, account.Id, account.Id,
                new { org = org.Org.Code, device = device.Id, name = device.Name });
            return Results.Redirect(basePath + EnterTarget(to, org.Org.Code));
        });

        // ОБНОВЛЕНИЕ РАСШИРЕНИЯ (T-511-S0): версия сервера и файл расширения той же версии.
        // Без входа, как /api/cluster/hello: vsix — файл дистрибутива, секретов в нём нет,
        // а расширение, чей токен отозван, тоже должно суметь обновиться
        embed.MapGet("/version", () =>
        {
            var vsix = VsCodeExtension.VsixPath(AppContext.BaseDirectory);
            return Results.Ok(new
            {
                version = AppInfo.Version,
                extensionVersion = VsCodeExtension.ExtensionVersion(AppInfo.Version),
                vsix = vsix is null ? null : Path.GetFileName(vsix),
            });
        });

        embed.MapGet("/vsix", () =>
            VsCodeExtension.VsixPath(AppContext.BaseDirectory) is { } vsix
                ? Results.File(vsix, "application/vsix", Path.GetFileName(vsix))
                : Results.NotFound());

        // «нужно подключить VS Code»: сюда уводит cookie встроенного режима, ставшая
        // недействительной (токен отозван). Страница сообщает об этом расширению наверх
        var connect = app.MapGroup("/embed").AllowAnonymous();
        connect.MapGet("/connect", () => Results.Content(Page(Loc.T("msg.deviceToken.8"),
            "<script>try{window.parent.postMessage({type:'ai2p.needLink'},'*')}catch(e){}</script>"),
            "text/html; charset=utf-8"));
    }

    /// <summary>
    /// Куда увести после входа: путь ВНУТРИ приложения без базового пути. Чужой адрес
    /// (схема, двойной слэш, обратный слэш) — на главную организации; путь без кода
    /// организации токена получает его спереди.
    /// </summary>
    public static string EnterTarget(string? to, string orgCode)
    {
        to = (to ?? "").Trim();
        if (to.Length == 0 || to[0] != '/' || to.StartsWith("//") || to.Contains('\\') || to.Contains("://"))
        {
            return "/" + orgCode + "/";
        }
        var first = to[1..].Split('/', '?', '#')[0];
        return string.Equals(first, orgCode, StringComparison.OrdinalIgnoreCase) ? to : "/" + orgCode + to;
    }

    private static string Page(string text, string extra = "") =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>AI2P</title>"
        + "<style>body{font-family:system-ui,sans-serif;margin:2rem;color:#888}</style></head><body><p>"
        + WebUtility.HtmlEncode(text) + "</p>" + extra + "</body></html>";
}
