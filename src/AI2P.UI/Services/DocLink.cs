using AI2P.Core;
using AI2P.UI.Components;
using MudBlazor;

namespace AI2P.UI.Services;

/// <summary>
/// ССЫЛКИ НА СТРАНИЦУ ДОКУМЕНТАЦИИ (T-207-S0).
///
/// Прочитав что-то нужное, человек хочет это ПЕРЕСЛАТЬ — «смотри вот здесь». До сих пор
/// переслать было нечего: страница адресуется только внутри приложения (закладка чтения
/// и окно справки), а в адресной строке при этом стоит адрес самого приложения.
///
/// Окно берётся СТАНДАРТНОЕ — то же <see cref="TaskLinkDialog"/>, что у ссылки на задачу
/// и на файл: строка только для чтения плюс кнопка копирования. Ссылок три, и это те же
/// три, что у задачи:
/// <list type="bullet">
/// <item><b>краткая</b> — от корня сайта («/ai2p/org/doc/man/https.md»): не ломается ни при
/// переходе на https, ни при смене имени сервера, её ставят в тексты заданий и в документы,
/// которые читают в этом же приложении;</item>
/// <item><b>внутренняя</b> — петлевая: открывается на том компьютере, где стоит сервер;</item>
/// <item><b>внешняя</b> — по имени сервера из настроек, её отправляют наружу. Имя сервера
/// петлевое — внешней ссылки нет вовсе, и окно показывает одну (T-142).</item>
/// </list>
///
/// Открывается ссылка маршрутом <c>/doc/{путь}</c> (Home.razor): закладка чтения встаёт
/// на названную страницу. Языка в адресе нет намеренно — страница ищется на языке того,
/// кто ссылку открыл.
/// </summary>
public static class DocLink
{
    /// <summary>Окно ссылок на страницу документации.</summary>
    /// <param name="dialogs">Служба окон.</param>
    /// <param name="state">Состояние UI — от него берутся базовые адреса.</param>
    /// <param name="l">Словарь интерфейса.</param>
    /// <param name="relPath">Путь страницы внутри языкового каталога («man/https.md»).</param>
    public static Task ShowAsync(IDialogService dialogs, UiState state, I18nService l, string relPath)
    {
        var parameters = new DialogParameters<TaskLinkDialog>
        {
            { d => d.Url, state.DocUrl(relPath) },
            { d => d.LocalUrl, state.HasExternalUrl ? state.DocLocalUrl(relPath) : "" },
            { d => d.RelativeUrl, LinkUrls.RelativeOf(state.DocUrl(relPath)) },
        };
        // окно шире Small — как у ссылки на задачу: адрес в нём читают глазами
        return dialogs.ShowAsync<TaskLinkDialog>(l["docs.link"], parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
    }
}
