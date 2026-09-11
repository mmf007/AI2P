using System.Text.RegularExpressions;

namespace AI2P.UI.Services;

/// <summary>
/// ВНУТРЕННИЕ ССЫЛКИ ПОСТАВЛЯЕМОЙ ДОКУМЕНТАЦИИ (T-17-S1).
///
/// Документация читается закладкой, и переходы по ссылкам между её страницами обязаны
/// оставаться ВНУТРИ этой закладки. Сама по себе относительная ссылка Markdown
/// (<c>[Модели](models/README.md)</c>) этого не даёт: браузер разворачивает её по адресу
/// страницы приложения (<c>/ai2p/&lt;орг&gt;/models/README.md</c>), клик уводит с экрана и
/// убивает подключение Blazor — ровно та беда, из-за которой в T-214 относительные ссылки
/// из документов моделей пришлось выбросить.
///
/// Поэтому в готовом HTML такие ссылки ПОМЕЧАЮТСЯ: <c>href</c> обезвреживается («#»),
/// а рядом встаёт <c>data-doc-link</c> с путём страницы внутри языкового каталога. Клик по
/// помеченной ссылке перехватывает свой обработчик (<c>ai2p.initDocLinks</c>) и отдаёт путь
/// закладке — та читает страницу и рисует её на месте прежней.
///
/// Ссылки НАРУЖУ (http/https, mailto) не трогаются вовсе: их уже разметил
/// <see cref="Md.ToHtml"/> — они открываются новой вкладкой.
/// </summary>
public static partial class DocLinks
{
    /// <summary>Ссылка, у которой <c>href</c> идёт первым атрибутом — как их печатает
    /// Markdig. У внешних ссылок к этому моменту первым стоит <c>target</c>
    /// (<see cref="Md.ToHtml"/>), поэтому сюда они не попадают.</summary>
    [GeneratedRegex("<a href=\"(?<url>[^\"]*)\"")]
    private static partial Regex LinkRegex();

    /// <summary>
    /// Пометить внутренние ссылки готового HTML страницы документации.
    /// </summary>
    /// <param name="html">HTML, уже собранный <see cref="Md.ToHtml"/>.</param>
    /// <param name="currentRelPath">Путь показанной страницы внутри языкового каталога —
    /// относительные ссылки считаются от её каталога.</param>
    public static string Mark(string html, string currentRelPath) =>
        html.Length == 0 ? html : LinkRegex().Replace(html, match =>
        {
            var url = System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
            var target = Resolve(currentRelPath, url);
            return target is null
                ? match.Value
                // href обезврежен, но оставлен: без него браузер рисует не ссылку, а текст,
                // и внутренний переход перестал бы отличаться от обычного абзаца
                : $"<a href=\"#\" data-doc-link=\"{Attr(target)}\"";
        });

    /// <summary>Картинка готового HTML: <c>src</c> идёт первым атрибутом — так их печатает
    /// и Markdig (<c>![…](…)</c>), и своя сборка тега <c>&lt;img&gt;</c> (T-262).</summary>
    [GeneratedRegex("<img src=\"(?<url>[^\"]*)\"")]
    private static partial Regex ImageRegex();

    /// <summary>
    /// КАРТИНКИ СТРАНИЦЫ ДОКУМЕНТАЦИИ (T-97-S0).
    ///
    /// Документ ссылается на картинку относительным путём (<c>../images/ai2p-logo.png</c>
    /// в шапке README). Во внешнем просмотрщике это работает, а у нас — нет: страница
    /// показана ВНУТРИ приложения, и браузер разворачивает такой путь по адресу приложения
    /// (<c>/ai2p/&lt;орг&gt;/images/…</c>), где картинки нет — на месте логотипа пустота.
    ///
    /// Поэтому относительный адрес переписывается на эндпойнт <c>api/doc/image</c>, а путь
    /// считается ОТ КОРНЯ документации: картинки общие для всех языков и лежат выше
    /// языкового каталога, поэтому «..» здесь обязано выводить из него (в отличие от ссылок
    /// на страницы — те живут внутри языка).
    ///
    /// Адрес полный (http/https, data:) или от корня сайта не трогается вовсе: это чужая
    /// картинка либо наш же файл из хранилища, они и так открываются.
    /// </summary>
    /// <param name="html">HTML, уже собранный <see cref="Md.ToHtml"/>.</param>
    /// <param name="language">Язык, на котором страница нашлась (каталог внутри doc/).</param>
    /// <param name="currentRelPath">Путь показанной страницы внутри языкового каталога.</param>
    public static string MarkImages(string html, string language, string currentRelPath) =>
        html.Length == 0 ? html : ImageRegex().Replace(html, match =>
        {
            var url = System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
            return ResolveFile(language, currentRelPath, url) is { } target
                ? $"<img src=\"api/doc/image?path={Attr(Uri.EscapeDataString(target))}\""
                : match.Value;
        });

    /// <summary>
    /// Куда ведёт относительная ссылка на ФАЙЛ рядом с документом: путь от КОРНЯ
    /// документации (<c>images/ai2p-logo.png</c>) либо null, если адрес не относительный.
    /// </summary>
    public static string? ResolveFile(string language, string currentRelPath, string href)
    {
        var url = (href ?? "").Trim();
        if (url.Length == 0 || url.StartsWith('#') || url.StartsWith('/') || url.StartsWith('\\'))
        {
            return null;
        }
        // схема (http:, data:, javascript:) — двоеточие ПЕРЕД первой косой чертой, как в Resolve
        var colon = url.IndexOf(':');
        var slash = url.IndexOf('/');
        if (colon >= 0 && (slash < 0 || colon < slash))
        {
            return null;
        }
        var cut = url.IndexOfAny(['#', '?']);
        var path = cut >= 0 ? url[..cut] : url;
        if (path.Length == 0)
        {
            return null;
        }
        var lang = (language ?? "").Trim().Replace('\\', '/').Trim('/');
        var current = (currentRelPath ?? "").Trim().Replace('\\', '/').TrimStart('/');
        // путь считается от каталога страницы, а он лежит внутри языкового каталога
        return Combine(lang.Length == 0 ? current : lang + "/" + current, path);
    }

    /// <summary>
    /// Куда ведёт ссылка документа: путь страницы внутри языкового каталога либо null,
    /// если это не внутренняя ссылка на страницу документации.
    ///
    /// Внутренней считается ОТНОСИТЕЛЬНАЯ ссылка на файл <c>.md</c> или на каталог
    /// (у каталога открывается его <c>README.md</c> — это решает сервер). Всё остальное —
    /// полный адрес, путь от корня сайта, якорь внутри страницы, письмо — оставляется как
    /// есть: подменять чужую ссылку своим переходом нельзя.
    /// </summary>
    /// <param name="currentRelPath">Путь показанной страницы (<c>models/README.md</c>).</param>
    /// <param name="href">Значение ссылки как оно написано в документе.</param>
    public static string? Resolve(string currentRelPath, string href)
    {
        var url = (href ?? "").Trim();
        if (url.Length == 0 || url.StartsWith('#') || url.StartsWith('/') || url.StartsWith('\\'))
        {
            return null;
        }
        // схема (http:, mailto:, javascript:) узнаётся по двоеточию ПЕРЕД первой косой
        // чертой — в относительном пути двоеточие бывает только после неё (как в Md.IsSafeSrc)
        var colon = url.IndexOf(':');
        var slash = url.IndexOf('/');
        if (colon >= 0 && (slash < 0 || colon < slash))
        {
            return null;
        }
        // якорь и хвост запроса адресуют место ВНУТРИ страницы, а не другую страницу
        var cut = url.IndexOfAny(['#', '?']);
        var path = cut >= 0 ? url[..cut] : url;
        if (path.Length == 0)
        {
            return null;
        }
        // ссылка на файл другого рода (картинка, архив) страницей документации не является
        var name = path.Replace('\\', '/').Split('/')[^1];
        var dot = name.LastIndexOf('.');
        if (dot > 0 && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return Combine(currentRelPath, path);
    }

    /// <summary>
    /// Путь ссылки, посчитанный от КАТАЛОГА показанной страницы: «..» поднимает на уровень
    /// вверх, «.» стоит на месте. Выше корня документации не поднимаемся — лишние «..»
    /// просто съедаются (наружу ходить некуда, а отказ вместо страницы человеку ничего
    /// не объяснит).
    /// </summary>
    private static string Combine(string currentRelPath, string href)
    {
        var parts = new List<string>();
        var current = (currentRelPath ?? "").Replace('\\', '/');
        var slash = current.LastIndexOf('/');
        if (slash > 0)
        {
            parts.AddRange(current[..slash].Split('/', StringSplitOptions.RemoveEmptyEntries));
        }
        foreach (var segment in href.Replace('\\', '/').Split('/'))
        {
            switch (segment)
            {
                case "":
                case ".":
                    continue;
                case "..":
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }
                    continue;
                default:
                    parts.Add(segment);
                    continue;
            }
        }
        return string.Join('/', parts);
    }

    /// <summary>Значение атрибута для готового HTML.</summary>
    private static string Attr(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
