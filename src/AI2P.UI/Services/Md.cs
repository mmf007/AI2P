using System.Text;
using System.Text.RegularExpressions;
using AI2P.Core;
using Markdig;

namespace AI2P.UI.Services;

/// <summary>
/// Рендеринг Markdown → HTML (Markdig) для предпросмотра в редакторе и карточки задачи
/// (ТЗ гл. 11). Сырой HTML в исходном тексте отключён — описания приходят и от ИИ.
/// ЕДИНСТВЕННОЕ исключение — картинка тегом <c>&lt;img&gt;</c> (T-262): её ставят внешние
/// системы, из которых мы импортируем задачи (см. <see cref="HideImgTags"/>).
/// </summary>
public static partial class Md
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>
    /// Внешний базовый адрес ЭТОГО сервера (<c>http://имя:порт/ai2p</c>) — по нему
    /// узнаются свои же ссылки на файлы (T-142). Значение одно на установку (из
    /// <c>config.json</c>, гл. 10), поэтому хранится статически: организации и вошедшего
    /// оно не касается. Пусто — ссылки не переписываются.
    /// </summary>
    public static string? OwnBaseUrl { get; set; }

    /// <summary>
    /// ВТОРОЙ собственный адрес сервера (T-2-S1) — <c>hostname</c>/<c>port</c> из
    /// <c>config.json</c>, пока ссылки строятся по внешней паре <c>hostname2</c>/<c>port2</c>.
    /// Пусто — второго адреса нет. Нужен затем же, зачем и первый: ссылка на нас самих,
    /// написанная по внутреннему адресу (а такие накопились в задачах), обязана узнаваться
    /// своей — иначе она уводила бы в сеть за файлом, который лежит на этом же диске.
    /// </summary>
    public static string? OwnBaseUrl2 { get; set; }

    /// <summary>
    /// АДРЕСА ОСТАЛЬНЫХ СЕРВЕРОВ ОРГАНИЗАЦИИ (T-13-S0). Ссылку на файл агент пишет от адреса
    /// своего сервера, а файл лежит только там — папка проекта не реплицируется. Такая
    /// ссылка сворачивается в путь и открывается через ЭТОТ сервер: он принесёт файл от
    /// соседа сам. Пусто — соседей нет либо список ещё не прочитан.
    /// </summary>
    public static IReadOnlyCollection<string> PeerBaseUrls { get; set; } = [];

    /// <summary>Ссылки на файловые эндпойнты (`…/api/files/…`, todo19/todo29): URL лежит
    /// внутри base href приложения, клик перехватывает Blazor-роутер и показывает
    /// «нет такой страницы» вместо отдачи файла — поэтому target="_blank".</summary>
    [GeneratedRegex("<a href=\"(?<url>[^\"]*/api/files/[^\"]*)\"")]
    private static partial Regex FileLinkRegex();

    /// <summary>Полные адреса файлов (ссылки и картинки), которые могли бы указывать
    /// на нас самих (T-142).</summary>
    [GeneratedRegex("(?<attr>href|src)=\"(?<url>https?://[^\"]*/api/files/[^\"]*)\"")]
    private static partial Regex AbsoluteFileLinkRegex();

    /// <summary>Ссылка полным адресом — ещё не размеченная (href идёт первым атрибутом,
    /// как их печатает Markdig): своя она или чужая, решает <see cref="ToNewTabLinks"/>.
    /// Файловые ссылки к этому моменту уже размечены <see cref="FileLinkRegex"/> и сюда
    /// не попадают — второго target у них не появится.</summary>
    [GeneratedRegex("<a href=\"(?<url>https?://[^\"]*)\"")]
    private static partial Regex AbsoluteLinkRegex();

    /// <param name="markdown">Исходный текст.</param>
    /// <param name="ownBaseUrl">Свой адрес для <see cref="ToLocalLinks"/>; null — взять
    /// <see cref="OwnBaseUrl"/> (обычный вызов из UI), пустая строка — не переписывать
    /// ничего. Параметр нужен тестам: подменять статическое поле нельзя — классы тестов
    /// идут параллельно, и подмена задела бы соседей.</param>
    /// <param name="altBaseUrl">Второй свой адрес (T-2-S1); null — взять
    /// <see cref="OwnBaseUrl2"/>, пустая строка — второго адреса нет.</param>
    /// <param name="peerBaseUrls">Адреса остальных серверов кластера (T-13-S0); null — взять
    /// <see cref="PeerBaseUrls"/> (и только при обычном вызове из UI, как и второй адрес).</param>
    public static string ToHtml(string markdown, string? ownBaseUrl = null, string? altBaseUrl = null,
        IReadOnlyCollection<string>? peerBaseUrls = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }
        var ownBase = ownBaseUrl ?? OwnBaseUrl;
        // второй адрес того же сервера: подставляется только к «своему» адресу из настроек —
        // тесты, задающие ownBaseUrl явно, соседского поля не касаются
        var altBase = altBaseUrl ?? (ownBaseUrl is null ? OwnBaseUrl2 : null);
        var peers = peerBaseUrls ?? (ownBaseUrl is null ? PeerBaseUrls : []);
        var ownBases = LinkUrls.OwnBases(ownBase, altBase);
        // картинки <img> прячутся ДО Markdig и возвращаются ПОСЛЕ (T-262): иначе их адрес
        // разбирается как обычный текст и портится разметкой
        var source = HideImgTags(markdown, out var images);
        var html = RestoreImgTags(Markdown.ToHtml(source, Pipeline), images);
        html = LinkifyBareUrls(html);
        html = ToLocalLinks(html, ownBases, peers);
        html = FileLinkRegex().Replace(html, "<a target=\"_blank\" rel=\"noopener\" href=\"${url}\"");
        return ToNewTabLinks(html, ownBases);
    }

    /// <summary>
    /// Markdown → ПРОСТОЙ ТЕКСТ (T-67-S0): ровно то, что дало бы выделение отрендеренного
    /// текста мышью и Ctrl+C — разметка снята, содержимое осталось. Нужен кнопке
    /// «скопировать простой текст»: поле карточки нередко только для чтения, и выделить
    /// его руками человеку не мешает ничто, но собрать так ВСЕ закладки задачи он не может.
    /// Картинка, вставленная тегом (T-262), превращается в свою подпись: сам тег в простом
    /// тексте был бы мусором.
    /// </summary>
    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }
        var source = markdown.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0
            ? markdown
            : ImgTagRegex().Replace(markdown, m => ParseImgTag(m.Value) is { } img ? img.Alt : m.Value);
        return Markdown.ToPlainText(source, Pipeline).Replace("\r\n", "\n").TrimEnd();
    }

    // --- ГОЛЫЙ URL В ТЕКСТЕ — ССЫЛКОЙ (T-179) ---

    /// <summary>
    /// URL, написанный в тексте просто так — без синтаксиса Markdown. Автоссылки Markdig
    /// (входят в <c>UseAdvancedExtensions</c>) такой URL размечают НЕ всегда, и мимо них
    /// проходит как раз самое частое здесь:
    /// <list type="bullet">
    /// <item><b>адрес без точки в имени хоста</b> — <c>http://localhost:5480/…</c>:
    /// свои же ссылки на задачу и на файл система и ИИ-агент пишут именно такими,
    /// и ссылкой они не становились (в Markdig 0.37 домен без точки доменом не считается,
    /// а настройки на это нет);</item>
    /// <item><b>URL сразу после знака препинания</b> — кавычки, двоеточия, запятой:
    /// автоссылке Markdig нужен пробел перед адресом.</item>
    /// </list>
    /// Поэтому голые URL размечаются своим проходом по готовому HTML — до всех остальных,
    /// чтобы дальше с ними работали общие правила (свои файловые ссылки — локальными,
    /// чужие адреса — новой вкладкой). Разметка идёт ТОЛЬКО по тексту: содержимое
    /// <c>a</c>, <c>code</c> и <c>pre</c> пропускается целиком (в ссылке разметка уже есть,
    /// в коде URL — часть кода), внутренности тегов не трогаются вовсе.
    /// </summary>
    private static string LinkifyBareUrls(string html) =>
        MapTextNodes(html, ["a", "code", "pre"],
            (text, opaque) => opaque ? text : LinkifyText(text));

    /// <summary>
    /// Обход ГОТОВОГО HTML по текстовым участкам: внутренности тегов (атрибуты) не
    /// трогаются вовсе, содержимое перечисленных «непрозрачных» тегов отдаётся одним куском
    /// с признаком <c>opaque</c> — в ссылке разметка уже есть, в коде текст обязан остаться
    /// текстом. Общая часть двух проходов по готовому HTML: разметки голых URL и возврата
    /// картинок (T-262).
    /// </summary>
    private static string MapTextNodes(string html, IReadOnlyCollection<string> opaqueTags,
        Func<string, bool, string> map)
    {
        var sb = new StringBuilder(html.Length + 64);
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                sb.Append(map(html[i..], false));
                break;
            }
            sb.Append(map(html[i..lt], false));
            var gt = html.IndexOf('>', lt);
            if (gt < 0)
            {
                sb.Append(html, lt, html.Length - lt);
                break;
            }
            sb.Append(html, lt, gt - lt + 1);
            i = gt + 1;
            var tag = TagNameAt(html, lt);
            if (!opaqueTags.Contains(tag) || html[gt - 1] == '/')
            {
                continue;
            }
            var close = html.IndexOf("</" + tag, i, StringComparison.OrdinalIgnoreCase);
            var closeEnd = close < 0 ? -1 : html.IndexOf('>', close);
            if (closeEnd < 0)
            {
                sb.Append(map(html[i..], true));
                break;
            }
            sb.Append(map(html[i..close], true));
            sb.Append(html, close, closeEnd - close + 1);
            i = closeEnd + 1;
        }
        return sb.ToString();
    }

    /// <summary>Имя открывающего тега в позиции '&lt;'; пусто — закрывающий тег или не тег.</summary>
    private static string TagNameAt(string html, int lt)
    {
        var start = lt + 1;
        if (start >= html.Length || !char.IsAsciiLetter(html[start]))
        {
            return "";
        }
        var end = start;
        while (end < html.Length && char.IsAsciiLetterOrDigit(html[end]))
        {
            end++;
        }
        return html[start..end].ToLowerInvariant();
    }

    /// <summary>Знаки препинания, прилипающие к URL в конце предложения (как в MdLinks).</summary>
    private const string TrailingPunctuation = ".,;:!?'\"»«";

    /// <summary>
    /// Голый URL в ТЕКСТЕ (уже экранированном для HTML): схема http/https либо «www.»,
    /// дальше — всё до пробела. Сущности &amp;amp; внутри адреса — это его «&amp;»
    /// (разделитель параметров), поэтому они частью адреса и остаются; &amp;quot;, &amp;lt;
    /// и &amp;gt; — это кавычка и скобки исходного текста, на них адрес кончается.
    /// </summary>
    [GeneratedRegex("""(?:https?://|www\.)(?:(?!&(?:quot|lt|gt|nbsp);)[^\s<>"'`])+""",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex();

    private static string LinkifyText(string text) =>
        text.Length == 0 ? text : BareUrlRegex().Replace(text, match =>
        {
            var url = TrimUrlTail(match.Value);
            if (url.Length == 0)
            {
                return match.Value;
            }
            // «www.имя» без схемы браузер счёл бы путём на нашем же сайте
            var href = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + url : url;
            return $"<a href=\"{href}\">{url}</a>{match.Value[url.Length..]}";
        });

    /// <summary>Хвост URL, который на деле принадлежит предложению: точка в конце,
    /// закрывающая кавычка, непарная скобка (адрес в скобках — «(см. http://…)»).</summary>
    private static string TrimUrlTail(string url)
    {
        var end = url.Length;
        while (end > 0)
        {
            var ch = url[end - 1];
            if (TrailingPunctuation.Contains(ch))
            {
                end--;
                continue;
            }
            var head = url[..end];
            if (ch == ')' && head.Count(c => c == ')') > head.Count(c => c == '('))
            {
                end--;
                continue;
            }
            break;
        }
        return url[..end];
    }

    // --- КАРТИНКА ТЕГОМ <img> (T-262) ---

    /// <summary>
    /// КАРТИНКА, ВСТАВЛЕННАЯ ТЕГОМ, А НЕ РАЗМЕТКОЙ. Внешние системы, из которых мы
    /// импортируем задачи (GitHub, GitLab, Trello), пишут вставленную картинку HTML-тегом
    /// <c>&lt;img width="…" alt="…" src="…" /&gt;</c>, а не разметкой <c>![…](…)</c>. Сырой
    /// HTML у нас выключен намеренно (описания приходят и от ИИ), поэтому такой тег
    /// показывался ТЕКСТОМ — картинки в импортированной задаче не было видно ни в описании,
    /// ни в чате.
    ///
    /// Тег не «включается обратно», а ЗАМЕНЯЕТСЯ СВОЕЙ картинкой: до разбора Markdown он
    /// прячется меткой, после разбора на её месте печатается собственный
    /// <c>&lt;img&gt;</c> — только с адресом, подписью и размером, без каких бы то ни было
    /// других атрибутов (<c>onerror</c> и прочее в разметку не попадают вовсе). Адрес при
    /// этом обязан быть картинкой из сети или из нашего хранилища (<see cref="IsSafeSrc"/>).
    ///
    /// Прятать НУЖНО ИМЕННО ДО Markdig, и это же лечит вторую половину беды: адрес внутри
    /// тега для Markdown — обычный текст, а подписанные адреса картинок GitHub
    /// (<c>…?jwt=eyJ0eXAi…</c>) состоят из base64url, где «_», «*» и «~~» встречаются
    /// сплошь и рядом. Такой текст разбирался как КУРСИВ, кусок адреса уезжал в теги
    /// <c>&lt;em&gt;</c>, и ссылка обрывалась сразу после «?jwt=» — картинка не открывалась
    /// даже вручную (жалоба заказчика в T-262).
    /// </summary>
    private sealed record ImgTag(string Original, string Src, string Alt, string Width, string Height)
    {
        public string Html()
        {
            var sb = new StringBuilder("<img src=\"").Append(Attr(Src)).Append('"');
            sb.Append(" alt=\"").Append(Attr(Alt)).Append('"');
            if (Width.Length > 0)
            {
                sb.Append(" width=\"").Append(Attr(Width)).Append('"');
            }
            if (Height.Length > 0)
            {
                sb.Append(" height=\"").Append(Attr(Height)).Append('"');
            }
            return sb.Append(" />").ToString();
        }
    }

    /// <summary>Метка спрятанной картинки: символ приватной области Юникода, номер, он же.
    /// Для Markdown это обычное слово — ни разметкой, ни ссылкой оно не станет.</summary>
    private const char ImgMark = '\uE262';

    /// <summary>Тег картинки целиком. Внутри тега угловых скобок быть не может: в адресе
    /// они кодируются, в подписи их экранирует сама внешняя система.</summary>
    [GeneratedRegex("""<img\s[^<>]*?/?>""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    /// <summary>Атрибут тега: имя и значение в кавычках либо без них. Значение без кавычек
    /// кончается пробелом, а не «=»: браузеры так и разбирают, а в адресе картинки «=» есть
    /// почти всегда (<c>?jwt=…</c>).</summary>
    [GeneratedRegex("""([a-zA-Z][a-zA-Z0-9-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'<>`]+))""")]
    private static partial Regex ImgAttrRegex();

    /// <summary>Метка спрятанной картинки в готовом HTML.</summary>
    [GeneratedRegex("\\uE262([0-9]{1,6})\\uE262")]
    private static partial Regex ImgMarkRegex();

    /// <summary>Размер: только число или проценты — всё прочее не размер.</summary>
    [GeneratedRegex("""^[0-9]{1,5}(?:px|%)?$""", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    private static string HideImgTags(string markdown, out List<ImgTag> images)
    {
        images = [];
        // ToHtml зовётся на каждое сообщение чата и каждую карточку доски, а тег картинки
        // встречается редко — сначала дешёвый поиск подстроки, и только потом разбор
        if (markdown.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return markdown;
        }
        List<ImgTag>? found = null;
        // символ метки, написанный человеком, — мусор из приватной области Юникода; убираем,
        // иначе он выдал бы себя за спрятанную картинку
        if (markdown.IndexOf(ImgMark) >= 0)
        {
            markdown = markdown.Replace(ImgMark.ToString(), "");
        }
        var hidden = ImgTagRegex().Replace(markdown, match =>
        {
            var img = ParseImgTag(match.Value);
            if (img is null)
            {
                return match.Value; // не картинка (нет адреса или адрес не тот) — как было
            }
            found ??= [];
            found.Add(img);
            return $"{ImgMark}{found.Count - 1}{ImgMark}";
        });
        images = found ?? [];
        return images.Count == 0 ? markdown : hidden;
    }

    private static ImgTag? ParseImgTag(string tag)
    {
        string src = "", alt = "", width = "", height = "";
        foreach (Match m in ImgAttrRegex().Matches(tag[4..]))
        {
            var raw = m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value
                // без кавычек значение кончается пробелом, поэтому закрывающая косая
                // самозакрытого тега прилипает к нему: «<img src=a.png/>»
                : m.Groups[4].Value.TrimEnd('/');
            var value = System.Net.WebUtility.HtmlDecode(raw).Trim();
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "src": src = value; break;
                case "alt": alt = value; break;
                case "width": width = SizeRegex().IsMatch(value) ? value : ""; break;
                case "height": height = SizeRegex().IsMatch(value) ? value : ""; break;
            }
        }
        return IsSafeSrc(src) ? new ImgTag(tag, src, alt, width, height) : null;
    }

    /// <summary>
    /// Годится ли адрес для картинки: сеть (<c>http</c>/<c>https</c>), путь на нас самих
    /// (<c>/ai2p/…/api/files/…</c>) или путь рядом. Любая ДРУГАЯ схема — и прежде всего
    /// <c>javascript:</c> — картинкой не рисуется: тег остаётся текстом, как и был до T-262.
    /// Схема узнаётся по двоеточию ПЕРЕД первой косой чертой: в относительном пути двоеточие
    /// тоже бывает, но только после неё.
    /// </summary>
    private static bool IsSafeSrc(string src)
    {
        if (src.Length == 0 || src.Any(char.IsControl))
        {
            return false;
        }
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith('/'))
        {
            return true;
        }
        var colon = src.IndexOf(':');
        var slash = src.IndexOf('/');
        return colon < 0 || (slash >= 0 && slash < colon);
    }

    /// <summary>
    /// Возврат картинок на место меток. Внутри <c>code</c> и <c>pre</c> картинка не
    /// рисуется: там тег написан как ПРИМЕР и обязан остаться текстом (мы для этого и не
    /// разбираем блоки кода отдельно — их узнаёт уже готовый HTML).
    /// </summary>
    private static string RestoreImgTags(string html, List<ImgTag> images) =>
        images.Count == 0
            ? html
            : MapTextNodes(html, ["code", "pre"], (text, opaque) =>
                text.IndexOf(ImgMark) < 0
                    ? text
                    : ImgMarkRegex().Replace(text, match =>
                    {
                        var index = int.Parse(match.Groups[1].Value);
                        return index >= images.Count ? match.Value
                            : opaque ? Attr(images[index].Original)
                            : images[index].Html();
                    }));

    /// <summary>Значение атрибута (и текст) для готового HTML — как их печатает Markdig.</summary>
    private static string Attr(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// ЧУЖОЙ АДРЕС — В НОВОЙ ВКЛАДКЕ (T-179). Приложение живёт в одной вкладке (Blazor
    /// Server, канал к серверу), и уход по ссылке наружу рвёт его: человек теряет
    /// открытую карточку и набранное. Поэтому ссылка на ЧУЖОЙ хост (её теперь даёт и
    /// голый URL из текста) открывается новой вкладкой, а ссылка на нас самих — как была,
    /// в текущей: там переход отработает роутер, не перезагружая приложение.
    ///
    /// Свой адрес неизвестен (пустой ownBaseUrl) — считаем чужими все полные адреса:
    /// открыть лишнюю вкладку не страшно, потерять работу — страшно.
    /// </summary>
    private static string ToNewTabLinks(string html, string?[] ownBaseUrls) =>
        AbsoluteLinkRegex().Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            return LinkUrls.OwnPathOf(url, ownBaseUrls) is null
                ? $"<a target=\"_blank\" rel=\"noopener\" href=\"{url}\""
                : match.Value;
        });

    /// <summary>
    /// СВОИ ССЫЛКИ НА ФАЙЛЫ — ЛОКАЛЬНЫМИ (T-142). Ссылку на созданный файл ИИ-агент пишет
    /// в текст задания полным адресом, от ВНЕШНЕГО имени своего сервера (гл. 10). Человек,
    /// открывший карточку на этом же сервере, подключён к нему обычно по петле, и клик по
    /// такой ссылке уводил его наружу: через DNS и NAT обратно к себе же — а файл (бывает
    /// и гигабайт) ехал бы по сети. Если адрес ссылки — это мы сами, имя хоста убирается,
    /// и браузер достраивает ссылку тем адресом, по которому человек уже подключён.
    ///
    /// С T-13-S0 то же делается и со ссылкой на ДРУГОЙ сервер кластера (а равно с любым
    /// петлевым адресом — такими ссылки и выходят, пока внешнее имя сервера не задано).
    /// Прежде такие ссылки не трогались: «файла здесь может и не лежать». Теперь его
    /// наличие здесь и не нужно — сервер спрашивает файл у соседа сам, — а ссылка на чужой
    /// адрес не работала ни на другом сервере (там нет входа), ни с телефона (петлевой
    /// адрес там означает сам телефон). Именно из-за этого картинки, положенные агентом
    /// в чат, у соседа не открывались.
    /// </summary>
    private static string ToLocalLinks(string html, string?[] ownBaseUrls,
        IReadOnlyCollection<string> peerBaseUrls) =>
        ownBaseUrls.All(string.IsNullOrWhiteSpace) && peerBaseUrls.Count == 0
            ? html
            : AbsoluteFileLinkRegex().Replace(html, match =>
            {
                var url = match.Groups["url"].Value;
                return LinkUrls.ClusterPathOf(url, ownBaseUrls, peerBaseUrls) is { } path
                    ? $"{match.Groups["attr"].Value}=\"{path}\""
                    : match.Value;
            });
}
