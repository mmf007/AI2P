using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using System.Net;
using System.Text;

namespace AI2P.Server.Api;

/// <summary>
/// ОТКРЫТЫЕ АДРЕСА ОПОВЕЩЕНИЯ (T-422-S0) — то, что телефон открывает БЕЗ ВХОДА:
/// <list type="bullet">
/// <item><c>{org}/notify/r/{токен}</c> — лёгкая страница ответа на вопрос агента
/// (п. А2 плана T-412-S0). Ссылка подписана ключом сервера и несёт вопрос и человека,
/// одноразовость даёт сам вопрос: ответ его закрывает. GET только ПОКАЗЫВАЕТ страницу —
/// отвечает кнопка (POST): почтовые фильтры открывают ссылки из писем сами, и ответ по
/// GET ушёл бы агенту раньше, чем человек прочёл вопрос;</item>
/// <item><c>{org}/notify/ical/{токен}.ics</c> — личная календарная лента (п. Б2): сроки
/// задач человека событиями с напоминанием. Секрет ленты сбрасывается в «Мои уведомления».</item>
/// </list>
/// Организацию задаёт сегмент адреса (раздел <c>notify</c> — общий, см. OrgRouting.Shared),
/// личность — подпись ссылки или секрет ленты, а не cookie.
/// </summary>
public static class NotifyEndpoints
{
    public static void MapAi2pNotify(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<OrgRegistry>();
        var notify = app.MapGroup("/notify").AllowAnonymous();

        OrgContext? Org(HttpContext ctx) =>
            ctx.Items.TryGetValue(CurrentUserAccessor.OrgCodeItem, out var code)
                ? registry.ByCode(code as string)
                : null;

        notify.MapGet("/r/{token}", (HttpContext ctx, string token, int? a) =>
        {
            if (Org(ctx) is not { } org || org.ReplyTokens.Verify(token) is not { } claim)
            {
                return Page(Loc.T("notify.reply.title"), Notice(Loc.T("notify.reply.invalid")), 404);
            }
            if (org.Chat.Get(claim.QuestionId) is not { } question || question.DeletedAt is not null)
            {
                return Page(Loc.T("notify.reply.title"), Notice(Loc.T("notify.reply.invalid")), 404);
            }
            var task = org.Tasks.Get(question.TaskId);
            var html = new StringBuilder();
            html.Append("<p class=\"task\">").Append(Enc(task?.DisplayId ?? "")).Append(' ')
                .Append(Enc(task?.Title ?? "")).Append("</p>");
            html.Append("<div class=\"q\">").Append(Enc(question.Text)).Append("</div>");
            if (question.AnsweredAt is not null)
            {
                html.Append(Notice(Loc.T("notify.reply.already")));
            }
            else
            {
                var options = NotificationService.ParseOptions(question.OptionsJson);
                for (var i = 0; i < options.Count; i++)
                {
                    // выбранный в письме вариант подсвечен, но отвечает только касание кнопки
                    html.Append("<form method=\"post\"><input type=\"hidden\" name=\"answer\" value=\"")
                        .Append(Enc(options[i])).Append("\"><button class=\"opt")
                        .Append(a == i ? " sel" : "").Append("\">").Append(Enc(options[i]))
                        .Append("</button></form>");
                }
                html.Append("<form method=\"post\"><textarea name=\"answer\" rows=\"4\" placeholder=\"")
                    .Append(Enc(Loc.T("notify.reply.text"))).Append("\"></textarea><button>")
                    .Append(Enc(Loc.T("notify.reply.send"))).Append("</button></form>");
            }
            if (task is not null)
            {
                html.Append("<p><a href=\"").Append(Enc($"{org.PublicBaseUrl}/task/{task.Id}"))
                    .Append("\">").Append(Enc(Loc.T("notify.reply.card"))).Append("</a></p>");
            }
            return Page(Loc.T("notify.reply.title"), html.ToString());
        });

        notify.MapPost("/r/{token}", async (HttpContext ctx, string token) =>
        {
            if (Org(ctx) is not { } org || org.ReplyTokens.Verify(token) is not { } claim)
            {
                return Page(Loc.T("notify.reply.title"), Notice(Loc.T("notify.reply.invalid")), 404);
            }
            var form = await ctx.Request.ReadFormAsync();
            var answer = form["answer"].ToString().Trim();
            if (answer.Length == 0)
            {
                return Page(Loc.T("notify.reply.title"), Notice(Loc.T("notify.reply.empty"))
                    + Back(ctx), 400);
            }
            try
            {
                await org.Orchestrator.AnswerQuestionAsync(claim.QuestionId, answer, claim.ExecutorId);
                return Page(Loc.T("notify.reply.title"), Notice(Loc.T("notify.reply.done")));
            }
            catch (Exception ex) when (ex is ArgumentException
                                           or (InvalidOperationException and not ObjectDisposedException))
            {
                return Page(Loc.T("notify.reply.title"), Notice(ex.Message) + Back(ctx), 400);
            }
        }).DisableAntiforgery();

        notify.MapGet("/ical/{file}", (HttpContext ctx, string file) =>
        {
            var token = file.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
            if (Org(ctx) is not { } org || org.NotificationPrefs.ByIcalToken(token) is not { } prefs)
            {
                return Results.NotFound();
            }
            var me = prefs.ExecutorId;
            var items = org.Tasks.List(includeTemplates: false)
                .Where(t => t.DueDate is not null
                            && t.Status is not (TaskStatuses.Done or TaskStatuses.Cancelled)
                            && (t.ExecutorIds.Contains(me) || t.ActualExecutorId == me || t.ResponsibleId == me))
                .Select(t => new IcsItem(t.Id, $"{t.DisplayId} {t.Title}", t.Status,
                    $"{org.PublicBaseUrl}/task/{t.Id}", t.DueDate!.Value, t.UpdatedAt))
                .ToList();
            var ics = IcsCalendar.BuildFeed("AI2P " + org.Org.Name, items, prefs.DueLeadMinutes, prefs.IcalTodo);
            return Results.Text(ics, "text/calendar; charset=utf-8", Encoding.UTF8);
        });
    }

    private static string Enc(string text) => WebUtility.HtmlEncode(text);

    private static string Notice(string text) => "<p class=\"note\">" + Enc(text) + "</p>";

    private static string Back(HttpContext ctx) =>
        "<p><a href=\"" + Enc(ctx.Request.PathBase + ctx.Request.Path) + "\">←</a></p>";

    /// <summary>Своя вёрстка без Blazor: страница открывается на телефоне из письма и должна
    /// грузиться мгновенно, без входа и без канала SignalR.</summary>
    private static IResult Page(string title, string body, int status = 200)
    {
        var html = $$"""
            <!DOCTYPE html>
            <html lang="{{Loc.Lang}}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Enc(title)}}</title>
            <style>
            body{font-family:system-ui,sans-serif;margin:0;padding:16px;max-width:640px;background:#fafafa;color:#222}
            h1{font-size:1.2em}.task{opacity:.7;margin:0 0 8px}.q{white-space:pre-wrap;background:#fff;border:1px solid #ddd;border-radius:8px;padding:12px;margin-bottom:12px}
            form{margin:8px 0}button{width:100%;padding:14px;font-size:1em;border-radius:8px;border:1px solid #1976d2;background:#1976d2;color:#fff}
            button.opt{background:#fff;color:#1976d2}button.sel{background:#e3f2fd;font-weight:bold}
            textarea{width:100%;box-sizing:border-box;font-size:1em;padding:8px;border-radius:8px;border:1px solid #ccc;margin-bottom:8px}
            .note{background:#fff8e1;border:1px solid #ffe082;border-radius:8px;padding:12px}
            </style></head><body><h1>{{Enc(title)}}</h1>{{body}}</body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, status);
    }
}
