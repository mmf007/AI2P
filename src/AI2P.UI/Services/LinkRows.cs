namespace AI2P.UI.Services;

/// <summary>Строка окна ссылок: ключ разметки, название, сам адрес и пояснение.</summary>
/// <param name="Key">Ключ для проверок (<c>data-task-link-url</c>, <c>data-object-link-url</c>).</param>
/// <param name="Label">Название типа ссылки — «краткая», «полная внутренняя», «полная публичная».</param>
/// <param name="Url">Адрес.</param>
/// <param name="Hint">Пояснение под полем; <c>null</c> — без пояснения.</param>
public sealed record LinkRow(string Key, string Label, string Url, string? Hint);

/// <summary>
/// СОСТАВ ОКНА ССЫЛОК (T-207-S0, второй проход).
///
/// Окно ссылок одно на всё — задачу, файл задачи, объект, страницу документации
/// (<c>TaskLinkDialog</c>, <c>ObjectLinkDialog</c>), — и строки в нём собирались в каждом
/// окне СВОИ, хотя правило одно и то же. Заодно из названий убрано «на задачу»: названия
/// говорят про ТИП ссылки, а не про то, куда она ведёт (это и так сказано заголовком окна).
///
/// Типов ровно три, и порядок в окне такой же:
/// <list type="number">
/// <item><b>краткая</b> (<c>link.relative</c>) — от корня сайта, «/ai2p/org/task/T-18»:
/// без протокола и имени сервера, поэтому не ломается ни при переходе на https, ни при
/// смене адреса. Её ставят в тексты заданий и в документы этого же приложения;</item>
/// <item><b>полная внутренняя</b> (<c>link.local</c>) — петлевая: для того компьютера,
/// где стоит сервер;</item>
/// <item><b>полная публичная</b> (<c>link.external</c>) — по имени сервера из настроек,
/// её отправляют наружу.</item>
/// </list>
///
/// Полная ссылка бывает ОДНА (T-142): у сервера с петлевым именем внутренняя и публичная
/// совпадают, и вторая строка была бы копией первой.
/// </summary>
public static class LinkRows
{
    /// <summary>Строки окна ссылок.</summary>
    /// <param name="l">Словарь интерфейса.</param>
    /// <param name="url">Публичный адрес (по имени сервера из настроек); пусто — только внутренний.</param>
    /// <param name="localUrl">Внутренний (петлевой) адрес; пусто — только публичный.</param>
    /// <param name="relativeUrl">Краткий адрес от корня сайта; пусто — строки нет (ссылка не наша).</param>
    public static List<LinkRow> Build(I18nService l, string url, string localUrl, string relativeUrl)
    {
        var rows = new List<LinkRow>();
        if (relativeUrl.Length > 0)
        {
            rows.Add(new LinkRow("relative", l["link.relative"], relativeUrl, l["link.relative.hint"]));
        }
        var bothFull = url.Length > 0 && localUrl.Length > 0
            && !string.Equals(url, localUrl, StringComparison.OrdinalIgnoreCase);
        if (bothFull || localUrl.Length > 0 && url.Length == 0)
        {
            rows.Add(new LinkRow("local", l["link.local"], localUrl, l["link.local.hint"]));
        }
        if (url.Length > 0)
        {
            rows.Add(new LinkRow("external", l["link.external"], url, l["link.external.hint"]));
        }
        return rows;
    }
}
