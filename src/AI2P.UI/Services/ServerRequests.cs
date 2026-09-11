using AI2P.Core.Entities;
using AI2P.UI.Components;
using MudBlazor;

namespace AI2P.UI.Services;

/// <summary>
/// Заявки на подключение серверов (ТЗ гл. 6; todo41 пп. 9–10).
///
/// Одна и та же форма показывается в двух местах: по клику на строке заявки внизу списка
/// серверов и МОДАЛЬНО — владельцу или администратору, работающему на этом сервере в момент
/// прихода заявки. Чтобы решение обрабатывалось одинаково в обоих случаях, оно вынесено сюда.
/// </summary>
public static class ServerRequests
{
    /// <summary>Показать форму заявки и применить решение; "" — форма закрыта без решения.</summary>
    public static async Task<string> DecideAsync(IDialogService dialogs, ApiClient api,
        ISnackbar snackbar, I18nService l, OrgServer request)
    {
        var parameters = new DialogParameters<ServerRequestDialog>
        {
            { d => d.Request, request },
        };
        var dialog = await dialogs.ShowAsync<ServerRequestDialog>(l["servers.request.title"], parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is not { Canceled: false, Data: ServerRequestDecision taken }
            || taken.Decision.Length == 0)
        {
            // форму закрыли без решения — заявка остаётся в списке, как «ОТЛОЖИТЬ»
            return "";
        }
        var decision = taken.Decision;
        try
        {
            await api.DecideServerRequestAsync(request.Id, decision, taken.AddMember);
            // ПРИНЯТО БЕЗ УЧАСТНИКА (T-150) — отдельное сообщение: сервер подключён, а человек
            // в организацию не попал, и об этом надо сказать прямо, а не «готово»
            var done = decision == "accept" && !taken.AddMember && request.ApplicantEmail.Length > 0
                ? l["servers.request.done.accept.noMember"]
                : l["servers.request.done." + decision];
            snackbar.Add(done, Severity.Success);
            return decision;
        }
        catch (ApiException ex)
        {
            snackbar.Add(ex.Message, Severity.Error);
            return "";
        }
    }
}

/// <summary>
/// Решение по заявке, как его вернула форма (T-150): само слово (accept / defer / reject)
/// и флажок «завести заявителя участником организации». Раньше формой возвращалось одно
/// слово; участник заводился молча, и про него забывали — а без исполнителя человек
/// организацию не увидит и после репликации (ТЗ п. 2.15).
/// </summary>
public sealed record ServerRequestDecision(string Decision, bool AddMember);
