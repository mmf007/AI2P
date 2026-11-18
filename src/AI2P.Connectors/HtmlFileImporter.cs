using AI2P.Core;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ИМПОРТ СОХРАНЁННОЙ СТРАНИЦЫ HTML (T-680-S0) — шестой вид источника. Ссылка
/// file:///C:/tmp/страница.htm (или просто полный путь к .htm/.html): задачей становится
/// одна страница — заголовок из &lt;title&gt; (либо первого &lt;h1&gt;, либо имени файла),
/// описание — её текст, переведённый в Markdown.
///
/// * записи справочника и секретов нет: единственная проверка — право ЧИТАТЬ файл. У человека
///   это права учётной записи сервера, у агента — ещё и правила безопасности задачи
///   (<see cref="ImportContentAsync"/>, параметр readAllowed): по умолчанию вне папки проекта
///   чтение запрещено;
/// * картинки ПЕРЕНОСЯТСЯ В ЗАДАЧУ: файлы рядом со страницей (папка «…_files», которую кладёт
///   браузер), data:-картинки и картинки по http(s) копируются в uploads/ проекта, в тексте
///   остаются ссылки на копии — удалят исходную страницу, задача останется целой;
/// * HTML богаче Markdown, поэтому перевод «по максимуму»: заголовки, абзацы, списки
///   (вложенные), таблицы, ссылки, выделение, код, цитаты, линии; остальное — текстом.
///
/// С T-691-S0 это импорт записи справочника «Публичный HTML»: кроме file: он берёт и
/// ОТКРЫТУЮ СТРАНИЦУ САЙТА по http(s) — только её саму (по ссылкам не ходит), картинки
/// так же переносятся в задачу, а ссылки делаются абсолютными и ведут на сайт-источник.
/// Страница сайта не может достать файлов этого компьютера: картинки и фреймы file: у неё
/// не читаются. http(s)-адрес, который узнаёт другой импортёр (Trello, GitHub, GitLab,
/// Figma), остаётся ему.
/// </summary>
public sealed class HtmlFileImporter
{
    /// <summary>Код вида импорта.</summary>
    public const string Kind = ImportKinds.HtmlFile;

    /// <summary>Предел размера страницы.</summary>
    private const long MaxPageBytes = 64L * 1024 * 1024;

    /// <summary>Предел размера одной картинки.</summary>
    private const long MaxImageBytes = 50L * 1024 * 1024;

    /// <summary>Предел числа переносимых картинок за импорт.</summary>
    public const int MaxImages = 300;

    private static readonly string[] Extensions = [".htm", ".html", ".xhtml", ".shtml"];

    private readonly TaskService _tasks;
    private readonly EventStore _events;
    private readonly FileStore _files;

    public HtmlFileImporter(TaskService tasks, EventStore events, FileStore files)
    {
        _tasks = tasks;
        _events = events;
        _files = files;
    }

    static HtmlFileImporter()
    {
        // windows-1251 и прочие однобайтовые кодировки сохранённых страниц
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(HtmlFileImporter));

    /// <summary>Подменный HttpClient — только для тестов.</summary>
    public HttpClient? HttpOverride { get; set; }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private HttpClient Client => HttpOverride ?? Http;

    /// <summary>Содержимое импорта: <see cref="Files"/> — имена файлов, перенесённых в uploads/.</summary>
    public sealed record ImportedPage(string Title, string Description, string ExternalRef, string Url,
        List<string> Files);

    public sealed record RefreshResult(string DisplayId, string Title, int Files);

    // ------------------------------------------------------------ разбор ссылки

    /// <summary>Полный путь файла по ссылке file:// (или по самому пути); null — это не
    /// локальная страница HTML.</summary>
    public static string? PathOf(string? url)
    {
        if (url is null)
        {
            return null;
        }
        url = url.Trim().Trim('"');
        string path;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
            {
                return null;
            }
            path = uri.LocalPath;
        }
        else if (Path.IsPathFullyQualified(url))
        {
            path = url;
        }
        else
        {
            return null;
        }
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            ? Path.GetFullPath(path)
            : null;
    }

    /// <summary>Адрес страницы сайта (http/https); null — это не он.</summary>
    public static Uri? WebUriOf(string? url)
    {
        url = url?.Trim().Trim('"');
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : null;
    }

    /// <summary>Ссылку узнаёт импортёр другого вида (T-691-S0): ему она и остаётся.</summary>
    public static bool IsOtherSourceUrl(string url) =>
        FigmaImporter.CanRefresh(url) || GitHubImporter.CanRefresh(url)
        || GitLabImporter.CanRefresh(url) || TrelloImporter.CanRefresh(url);

    /// <summary>Умеет ли импорт разобрать эту ссылку — по ней выбирается процедура: локальная
    /// страница либо страница сайта, которую не узнал ни один другой импортёр.</summary>
    public static bool CanRefresh(string? importUrl) =>
        PathOf(importUrl) is not null
        || (WebUriOf(importUrl) is not null && !IsOtherSourceUrl(importUrl!.Trim()));

    public static string ExternalRefOf(string path) => "html-file:" + new Uri(path).AbsoluteUri;

    // -------------------------------------------------------- импорт по ссылке

    /// <summary>
    /// Прочитать страницу и перенести картинки в uploads/ проекта. Задача здесь не создаётся.
    /// <paramref name="readAllowed"/> — проверка правил безопасности для пути (агент);
    /// null — проверяются только права ОС (человек).
    /// </summary>
    public async Task<ImportedPage> ImportContentAsync(Project project, string url,
        Func<string, bool>? readAllowed = null, CancellationToken ct = default)
    {
        if (PathOf(url) is null && WebUriOf(url) is { } web)
        {
            return await ImportWebAsync(project, web, ct);
        }
        var path = PathOf(url) ?? throw new InvalidOperationException(Loc.T("msg.htmlFile.1", url));
        var bytes = ReadFile(path, readAllowed, MaxPageBytes);
        var html = Decode(bytes);
        var baseUri = new Uri(path);
        var images = new ImageCopier(this, project, baseUri, readAllowed, ct);
        var frameHeading = "";
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };

        // фрейм с локальным файлом (папка «…_files» рядом со страницей) разворачивается на месте;
        // картинки фрейма считаются от ЕГО адреса, глубина вложения ограничена
        HtmlToMarkdown Make(string text, Uri at, int depth) =>
            new(text, src => images.Resolve(src, at),
                depth >= 3 ? null : new Func<string, string>(src => Frame(src, at, depth + 1)));

        string Frame(string src, Uri at, int depth)
        {
            if (!Uri.TryCreate(at, src.Trim(), out var abs) || !abs.IsFile || !visited.Add(abs.LocalPath))
            {
                return "";
            }
            try
            {
                var inner = Make(Decode(ReadFile(abs.LocalPath, readAllowed, MaxPageBytes)), abs, depth);
                var md = inner.Convert();
                if (frameHeading.Length == 0)
                {
                    frameHeading = inner.FirstHeading;
                }
                return md;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warning("Импорт страницы HTML: фрейм {Src} не прочитан ({Reason})", abs.LocalPath, ex.Message);
                return "";
            }
        }

        var converter = Make(html, baseUri, 0);
        var markdown = converter.Convert();
        await images.CompleteAsync();
        markdown = images.Apply(markdown);
        var heading = converter.FirstHeading.Length > 0 ? converter.FirstHeading : frameHeading;
        var title = converter.Title.Length > 0
            ? converter.Title
            : heading.Length > 0 ? heading : Path.GetFileNameWithoutExtension(path);
        var link = baseUri.AbsoluteUri;
        var description = markdown.TrimEnd() + "\n\n" + Loc.T("msg.htmlFile.4", link);
        Logger.Information("Импорт страницы HTML {Path}: {Chars} знаков, картинок перенесено {Files}",
            path, markdown.Length, images.Files.Count);
        return new ImportedPage(title.Length > 300 ? title[..300] : title, description,
            ExternalRefOf(path), link, images.Files);
    }

    /// <summary>
    /// Страница сайта по http(s) (T-691-S0): только она сама, картинки — в uploads/ проекта,
    /// ссылки — абсолютные на сайт-источник. Файлы этого компьютера страница не достаёт:
    /// картинки file: не читаются (readAllowed всегда «нет»), фреймы не разворачиваются.
    /// </summary>
    private async Task<ImportedPage> ImportWebAsync(Project project, Uri web, CancellationToken ct)
    {
        byte[] bytes;
        Uri at;
        string? charset;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, web);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; AI2P)");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(Loc.T("msg.htmlFile.8", web.AbsoluteUri,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"));
            }
            if (response.Content.Headers.ContentLength > MaxPageBytes)
            {
                throw new InvalidOperationException(Loc.T("msg.htmlFile.9", web.AbsoluteUri, MaxPageBytes / (1024 * 1024)));
            }
            bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > MaxPageBytes)
            {
                throw new InvalidOperationException(Loc.T("msg.htmlFile.9", web.AbsoluteUri, MaxPageBytes / (1024 * 1024)));
            }
            // после переадресаций относительные адреса считаются от КОНЕЧНОЙ страницы
            at = response.RequestMessage?.RequestUri ?? web;
            charset = response.Content.Headers.ContentType?.CharSet?.Trim('"', ' ');
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(Loc.T("msg.htmlFile.8", web.AbsoluteUri, ex.Message));
        }
        var html = Decode(bytes);
        // кодировка из заголовка ответа — когда в самой странице её не назвали
        if (charset is { Length: > 0 }
            && !Regex.IsMatch(Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192)), @"charset\s*=",
                RegexOptions.IgnoreCase))
        {
            try
            {
                html = Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // незнакомая кодировка заголовка — остаётся разбор по BOM и UTF-8
            }
        }
        var images = new ImageCopier(this, project, at, _ => false, ct);
        var converter = new HtmlToMarkdown(html, src => images.Resolve(src, at)) { LinkBase = at };
        var markdown = converter.Convert();
        await images.CompleteAsync();
        markdown = images.Apply(markdown);
        var title = converter.Title.Length > 0 ? converter.Title
            : converter.FirstHeading.Length > 0 ? converter.FirstHeading
            : at.Host + at.AbsolutePath.TrimEnd('/');
        var link = at.AbsoluteUri;
        var description = markdown.TrimEnd() + "\n\n" + Loc.T("msg.htmlFile.10", link);
        Logger.Information("Импорт страницы сайта {Url}: {Chars} знаков, картинок перенесено {Files}",
            link, markdown.Length, images.Files.Count);
        return new ImportedPage(title.Length > 300 ? title[..300] : title, description,
            "html:" + link, link, images.Files);
    }

    /// <summary>Обновление задачи: страница перечитывается по ссылке импорта.</summary>
    public async Task<RefreshResult> RefreshTaskAsync(Project project, TaskItem task, string? actorId,
        CancellationToken ct = default)
    {
        var url = TaskService.NormalizeImportUrl(task.ImportUrl)
                  ?? throw new InvalidOperationException(Loc.T("msg.htmlFile.1", task.ImportUrl ?? ""));
        var page = await ImportContentAsync(project, url, null, ct);
        task.Title = page.Title;
        task.ExternalRef = page.ExternalRef;
        task.ImportUrl = page.Url;
        _tasks.Update(task, page.Description, _tasks.ReadAcceptance(task), actorId);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.ImportRun,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                new { kind = "refresh", source = Kind, url = task.ImportUrl, files = page.Files.Count }),
        });
        return new RefreshResult(task.DisplayId, task.Title, page.Files.Count);
    }

    /// <summary>Чтение файла с проверкой прав: правила безопасности (если заданы) и права ОС.</summary>
    private static byte[] ReadFile(string path, Func<string, bool>? readAllowed, long limit)
    {
        if (readAllowed is not null && !readAllowed(path))
        {
            throw new InvalidOperationException(Loc.T("msg.htmlFile.6", path));
        }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new InvalidOperationException(Loc.T("msg.htmlFile.2", path));
            }
            if (info.Length > limit)
            {
                throw new InvalidOperationException(Loc.T("msg.htmlFile.7", path, info.Length / (1024 * 1024),
                    limit / (1024 * 1024)));
            }
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       and not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new InvalidOperationException(Loc.T("msg.htmlFile.3", path, ex.Message));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(Loc.T("msg.htmlFile.2", path));
        }
    }

    /// <summary>Текст страницы: BOM, затем charset из meta, иначе UTF-8.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF)
            || bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8192));
        var m = Regex.Match(head, @"charset\s*=\s*[""']?\s*([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            try
            {
                return Encoding.GetEncoding(m.Groups[1].Value).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // незнакомая кодировка — как UTF-8
            }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    // -------------------------------------------------------------- картинки

    /// <summary>
    /// Перенос картинок: на время разбора каждой картинке выдаётся метка, после разбора
    /// картинки копируются (http — параллельно), и метки заменяются ссылками на копии.
    /// Не перенеслась — остаётся исходный адрес (у локального файла — с пометкой).
    /// </summary>
    private sealed class ImageCopier(HtmlFileImporter owner, Project project, Uri baseUri,
        Func<string, bool>? readAllowed, CancellationToken ct)
    {
        private readonly Dictionary<string, string> _tokenBySource = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _linkByToken = new(StringComparer.Ordinal);
        private readonly List<(string Token, string Source)> _queue = [];

        public List<string> Files { get; } = [];

        /// <param name="at">Адрес документа, где стоит картинка (страница или её фрейм).</param>
        public string Resolve(string src, Uri? at = null)
        {
            src = src.Trim();
            if (src.Length == 0)
            {
                return "";
            }
            if (at is not null && !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(at, src, out var abs))
            {
                src = abs.AbsoluteUri;
            }
            if (_tokenBySource.TryGetValue(src, out var known))
            {
                return known;
            }
            var token = $"\u0001AI2PIMG{_tokenBySource.Count}\u0001";
            _tokenBySource[src] = token;
            _queue.Add((token, src));
            return token;
        }

        public async Task CompleteAsync()
        {
            using var gate = new SemaphoreSlim(6);
            var number = 0;
            var jobs = _queue.Select(async item =>
            {
                string link;
                var n = Interlocked.Increment(ref number);
                if (n > MaxImages)
                {
                    link = Fallback(item.Source);
                }
                else
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        link = await CopyAsync(item.Source, n) ?? Fallback(item.Source);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
                lock (_linkByToken)
                {
                    _linkByToken[item.Token] = link;
                }
            });
            await Task.WhenAll(jobs);
        }

        public string Apply(string markdown)
        {
            foreach (var (token, link) in _linkByToken)
            {
                markdown = markdown.Replace(token, link, StringComparison.Ordinal);
            }
            return markdown;
        }

        private string Fallback(string src) =>
            src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? ""
                : Uri.TryCreate(baseUri, src, out var abs) ? abs.AbsoluteUri.Replace(" ", "%20") : src;

        private async Task<string?> CopyAsync(string src, int n)
        {
            try
            {
                byte[] bytes;
                string name;
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = src.IndexOf(',');
                    if (comma < 0 || !src[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                    bytes = Convert.FromBase64String(WebUtility.UrlDecode(src[(comma + 1)..]));
                    var mime = src[5..comma].Split(';')[0];
                    var ext = mime.Contains('/') ? mime[(mime.IndexOf('/') + 1)..].Replace("svg+xml", "svg") : "png";
                    name = $"html-{n:D3}.{(ext == "jpeg" ? "jpg" : ext)}";
                }
                else
                {
                    if (!Uri.TryCreate(baseUri, src, out var abs))
                    {
                        return null;
                    }
                    if (abs.IsFile)
                    {
                        bytes = ReadFile(abs.LocalPath, readAllowed, MaxImageBytes);
                    }
                    else if (abs.Scheme is "http" or "https")
                    {
                        using var response = await owner.Client.GetAsync(abs, ct);
                        if (!response.IsSuccessStatusCode
                            || response.Content.Headers.ContentLength > MaxImageBytes)
                        {
                            return null;
                        }
                        bytes = await response.Content.ReadAsByteArrayAsync(ct);
                    }
                    else
                    {
                        return null;
                    }
                    var file = Path.GetFileName(abs.IsFile ? abs.LocalPath : Uri.UnescapeDataString(abs.AbsolutePath));
                    if (Path.GetExtension(file).Length == 0)
                    {
                        file += ".png";
                    }
                    name = $"html-{n:D3}-{file}";
                    if (name.Length > 100)
                    {
                        name = name[..(100 - Path.GetExtension(file).Length)] + Path.GetExtension(file);
                    }
                }
                var rel = owner._files.SaveUpload(project.Slug, name, bytes);
                lock (Files)
                {
                    Files.Add(name);
                }
                return "api/files/raw?path=" + Uri.EscapeDataString(rel);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                           or InvalidOperationException or FormatException
                                       && !ct.IsCancellationRequested)
            {
                Logger.Warning("Импорт страницы HTML: картинка {Src} не перенесена ({Reason})",
                    src.Length > 200 ? src[..200] : src, ex.Message);
                return null;
            }
        }
    }
}

/// <summary>
/// Перевод HTML в Markdown без сторонних библиотек: собственный разбор тегов (терпим к
/// незакрытым тегам), заголовки, абзацы, вложенные списки, таблицы, ссылки, выделение,
/// код, цитаты. Содержимое script/style/head и т. п. выбрасывается.
/// </summary>
public sealed class HtmlToMarkdown
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "noscript", "template", "svg", "head", "iframe", "object", "button", "select", "canvas", "math" };

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "figure", "figcaption",
        "form", "fieldset", "address", "details", "summary", "dl", "dt", "dd", "center", "body", "html",
        "caption", "legend",
    };

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
        { "br", "hr", "img", "input", "meta", "link", "source", "area", "base", "col", "wbr", "embed", "param", "track" };

    private static readonly Regex Attr = new(
        @"([^\s=/>""']+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+)))?", RegexOptions.Compiled);

    private readonly string _html;
    private readonly Func<string, string> _image;
    private readonly StringBuilder _sb = new();
    private int _pos;

    private readonly Stack<(string Tag, int Start, string Extra)> _open = new();
    private readonly Stack<(bool Ordered, int Number)> _lists = new();
    private int _pre;
    private int _skipDepth;
    private string _skipTag = "";

    // таблица: позиция начала ячейки, строки, число ячеек первой строки
    private sealed class Table
    {
        public int Row;
        public int Cells;
        public int FirstRowCells;
        public bool HeaderDone;
    }

    private readonly Stack<Table> _tables = new();

    // начала открытых ячеек таблицы: абзац внутри ячейки не должен рвать строку таблицы (T-776-S0)
    private readonly Stack<int> _cells = new();

    public string Title { get; private set; } = "";
    public string FirstHeading { get; private set; } = "";

    /// <summary>Сколько знаков текста пришло из фреймов.</summary>
    public int FrameChars { get; private set; }

    private readonly Func<string, string>? _frame;
    private readonly StringBuilder _frames = new();

    /// <summary>Адрес страницы сайта (T-691-S0): ссылки делаются абсолютными от него и ведут
    /// на сайт-источник, в том числе якоря «#…». null — ссылки остаются как в разметке.</summary>
    public Uri? LinkBase { get; init; }

    /// <param name="frame">Markdown фрейма по его src (пусто — фрейм пропускается); null — фреймы не читаются.</param>
    public HtmlToMarkdown(string html, Func<string, string> image, Func<string, string>? frame = null)
    {
        _html = html;
        _image = image;
        _frame = frame;
    }

    public string Convert()
    {
        var titleMatch = Regex.Match(_html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success)
        {
            Title = Collapse(WebUtility.HtmlDecode(titleMatch.Groups[1].Value)).Trim();
        }
        while (_pos < _html.Length)
        {
            var lt = _html.IndexOf('<', _pos);
            if (lt < 0)
            {
                Text(_html[_pos..]);
                break;
            }
            if (lt > _pos)
            {
                Text(_html[_pos..lt]);
            }
            _pos = lt;
            if (Starts("<!--"))
            {
                var end = _html.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
                _pos = end < 0 ? _html.Length : end + 3;
                continue;
            }
            if (Starts("<!") || Starts("<?"))
            {
                var end = _html.IndexOf('>', _pos);
                _pos = end < 0 ? _html.Length : end + 1;
                continue;
            }
            var close = _pos + 1 < _html.Length && _html[_pos + 1] == '/';
            var nameStart = _pos + (close ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < _html.Length && (char.IsLetterOrDigit(_html[nameEnd]) || _html[nameEnd] is '-' or ':'))
            {
                nameEnd++;
            }
            if (nameEnd == nameStart)
            {
                Text("<");
                _pos++;
                continue;
            }
            var tagEnd = FindTagEnd(nameEnd);
            var name = _html[nameStart..nameEnd].ToLowerInvariant();
            var attrs = _html[nameEnd..Math.Max(nameEnd, tagEnd)];
            _pos = tagEnd < _html.Length ? tagEnd + 1 : _html.Length;
            if (_skipDepth > 0)
            {
                if (name == _skipTag)
                {
                    _skipDepth += close ? -1 : (attrs.EndsWith('/') ? 0 : 1);
                }
                continue;
            }
            if (!close && name is "iframe" or "frame" && _frame is not null)
            {
                // сохранённая браузером страница часто держит СОДЕРЖИМОЕ во фрейме, файл которого
                // лежит в «…_files» (так у артефактов Claude, писем, встроенных документов)
                var src = Attributes(attrs).GetValueOrDefault("src") ?? "";
                var inner = src.Length > 0 ? _frame(src) : "";
                if (inner.Trim().Length > 0)
                {
                    Break(2);
                    _sb.Append(inner.Trim('\n'));
                    Break(2);
                    FrameChars += inner.Trim().Length;
                    _frames.Append(inner.Trim('\n')).Append("\n\n");
                }
            }
            if (!close && Skip.Contains(name))
            {
                if (!attrs.TrimEnd().EndsWith('/'))
                {
                    _skipTag = name;
                    _skipDepth = 1;
                }
                continue;
            }
            if (close)
            {
                CloseTag(name);
            }
            else
            {
                OpenTag(name, attrs);
            }
        }
        while (_open.Count > 0)
        {
            CloseTag(_open.Peek().Tag);
        }
        var all = Tidy(_sb.ToString());
        // вокруг фрейма только обвязка (панель, подписи) — берём одно содержимое фреймов
        if (FrameChars > 0 && all.Trim().Length - FrameChars < FrameChars / 4)
        {
            return Tidy(_frames.ToString());
        }
        return all;
    }

    private bool Starts(string s) => string.CompareOrdinal(_html, _pos, s, 0, s.Length) == 0;

    private int FindTagEnd(int from)
    {
        char quote = '\0';
        for (var i = from; i < _html.Length; i++)
        {
            var c = _html[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }
        return _html.Length;
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attr.Matches(text))
        {
            var value = m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value
                : m.Groups[4].Value;
            map.TryAdd(m.Groups[1].Value, WebUtility.HtmlDecode(value));
        }
        return map;
    }

    private void Text(string raw)
    {
        if (_skipDepth > 0)
        {
            return;
        }
        var text = WebUtility.HtmlDecode(raw).Replace('\u00A0', ' ');
        if (_pre > 0)
        {
            _sb.Append(text.Replace("\r", ""));
            return;
        }
        text = Collapse(text);
        if (text.Length == 0)
        {
            return;
        }
        if (text[0] == ' ' && (_sb.Length == 0 || char.IsWhiteSpace(_sb[^1])))
        {
            text = text[1..];
        }
        _sb.Append(_code > 0 ? text.Replace("`", "'") : EscapeMd(text));
    }

    private int _code;

    private static string Collapse(string text) => Regex.Replace(text, @"[\s]+", " ");

    /// <summary>Экранировать то, что Markdown прочёл бы разметкой.</summary>
    private static string EscapeMd(string text) => Regex.Replace(text, @"([\\`*_\[\]<>])", @"\$1");

    private void Break(int lines)
    {
        if (_cells.Count > 0)
        {
            // внутри ячейки: одиночный перевод строки (станет <br>), начало ячейки не трогаем
            var cellStart = _cells.Peek();
            while (_sb.Length > cellStart && _sb[^1] == ' ')
            {
                _sb.Length--;
            }
            if (_sb.Length > cellStart && _sb[^1] != '\n')
            {
                _sb.Append('\n');
            }
            return;
        }
        // убрать хвостовые пробелы и добить до нужного числа переводов строки
        while (_sb.Length > 0 && _sb[^1] == ' ')
        {
            _sb.Length--;
        }
        if (_sb.Length == 0)
        {
            return;
        }
        var have = 0;
        for (var i = _sb.Length - 1; i >= 0 && _sb[i] == '\n'; i--)
        {
            have++;
        }
        for (; have < lines; have++)
        {
            _sb.Append('\n');
        }
    }

    private string ListIndent => new(' ', Math.Max(0, _lists.Count - 1) * 3);

    private void OpenTag(string name, string attrText)
    {
        var selfClosing = Void.Contains(name) || attrText.TrimEnd().EndsWith('/');
        switch (name)
        {
            case "br":
                _sb.Append(_pre > 0 ? "\n" : "  \n" + (_lists.Count > 0 ? ListIndent + "   " : ""));
                return;
            case "hr":
                Break(2);
                _sb.Append("---");
                Break(2);
                return;
            case "img":
            {
                var a = Attributes(attrText);
                var src = a.GetValueOrDefault("src") ?? "";
                foreach (var lazy in new[] { "data-src", "data-original", "data-lazy-src" })
                {
                    if ((src.Length == 0 || src.StartsWith("data:image/gif", StringComparison.OrdinalIgnoreCase))
                        && a.TryGetValue(lazy, out var v) && v.Length > 0)
                    {
                        src = v;
                    }
                }
                var link = _image(src);
                if (link.Length > 0)
                {
                    var alt = Collapse(a.GetValueOrDefault("alt") ?? a.GetValueOrDefault("title") ?? "").Trim();
                    _sb.Append("![").Append(alt.Replace("[", "").Replace("]", "")).Append("](").Append(link).Append(')');
                }
                return;
            }
        }
        if (selfClosing)
        {
            return;
        }
        var extra = "";
        switch (name)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                Break(2);
                _sb.Append(new string('#', name[1] - '0')).Append(' ');
                break;
            case "ul":
            case "ol":
                if (_lists.Count == 0)
                {
                    Break(2);
                }
                var a = Attributes(attrText);
                _lists.Push((name == "ol", int.TryParse(a.GetValueOrDefault("start"), out var s) ? s - 1 : 0));
                break;
            case "li":
            {
                Break(1);
                if (_lists.Count == 0)
                {
                    _lists.Push((false, 0));
                    extra = "auto";
                }
                var (ordered, number) = _lists.Pop();
                _lists.Push((ordered, number + 1));
                _sb.Append(ListIndent).Append(ordered ? $"{number + 1}. " : "- ");
                break;
            }
            case "pre":
                Break(2);
                _sb.Append("```\n");
                _pre++;
                break;
            case "code":
            case "kbd":
            case "samp":
            case "tt":
                if (_pre == 0)
                {
                    _sb.Append('`');
                    _code++;
                }
                break;
            case "strong": case "b":
                _sb.Append("**");
                break;
            case "em": case "i": case "cite": case "dfn":
                _sb.Append('*');
                break;
            case "s": case "del": case "strike":
                _sb.Append("~~");
                break;
            case "a":
                extra = Attributes(attrText).GetValueOrDefault("href") ?? "";
                break;
            case "blockquote":
                Break(2);
                break;
            case "table":
                Break(2);
                _tables.Push(new Table());
                break;
            case "tr":
                if (_tables.Count > 0)
                {
                    Break(1);
                    var t = _tables.Peek();
                    t.Cells = 0;
                    _sb.Append('|');
                }
                break;
            case "td":
            case "th":
                if (_tables.Count > 0)
                {
                    _sb.Append(' ');
                    _cells.Push(_sb.Length);
                }
                break;
            default:
                if (Blocks.Contains(name))
                {
                    Break(_lists.Count > 0 ? 1 : 2);
                    if (_lists.Count > 0 && name is "p" or "div")
                    {
                        _sb.Append(ListIndent).Append("   ");
                    }
                }
                break;
        }
        _open.Push((name, _sb.Length, extra));
    }

    private void CloseTag(string name)
    {
        // закрыть всё, что осталось открытым внутри (терпимость к кривой разметке)
        if (!_open.Any(o => o.Tag == name))
        {
            if (Blocks.Contains(name))
            {
                Break(2);
            }
            return;
        }
        while (_open.Count > 0)
        {
            var (tag, start, extra) = _open.Pop();
            Finish(tag, start, extra);
            if (tag == name)
            {
                break;
            }
        }
    }

    private void Finish(string tag, int start, string extra)
    {
        switch (tag)
        {
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                if (FirstHeading.Length == 0 && tag == "h1")
                {
                    FirstHeading = Regex.Replace(_sb.ToString(start, _sb.Length - start), @"\\(.)", "$1").Trim();
                }
                Break(2);
                break;
            case "ul":
            case "ol":
                if (_lists.Count > 0)
                {
                    _lists.Pop();
                }
                Break(_lists.Count > 0 ? 1 : 2);
                break;
            case "li":
                if (extra == "auto" && _lists.Count > 0)
                {
                    _lists.Pop();
                }
                Break(1);
                break;
            case "pre":
                _pre = Math.Max(0, _pre - 1);
                if (_sb.Length > 0 && _sb[^1] != '\n')
                {
                    _sb.Append('\n');
                }
                _sb.Append("```");
                Break(2);
                break;
            case "code": case "kbd": case "samp": case "tt":
                if (_pre == 0)
                {
                    _sb.Append('`');
                    _code = Math.Max(0, _code - 1);
                }
                break;
            case "strong": case "b":
                Wrap(start, "**");
                break;
            case "em": case "i": case "cite": case "dfn":
                Wrap(start, "*");
                break;
            case "s": case "del": case "strike":
                Wrap(start, "~~");
                break;
            case "a":
            {
                var href = extra.Trim();
                if (LinkBase is not null && href.Length > 0
                    && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(LinkBase, href, out var absHref)
                    && absHref.Scheme is "http" or "https" or "mailto")
                {
                    href = absHref.AbsoluteUri;
                }
                var inner = _sb.ToString(start, _sb.Length - start);
                if (href.Length == 0 || href.StartsWith('#')
                    || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || inner.Trim().Length == 0)
                {
                    break;
                }
                if (inner.Trim().StartsWith("![", StringComparison.Ordinal) && inner.Contains("](api/files/", StringComparison.Ordinal)
                    || inner.Contains('\u0001'))
                {
                    // картинка-ссылка на саму себя: оставляем картинку
                    break;
                }
                _sb.Insert(start, '[');
                _sb.Append("](").Append(href.Replace(" ", "%20").Replace(")", "%29")).Append(')');
                break;
            }
            case "blockquote":
            {
                var inner = _sb.ToString(start, _sb.Length - start).Trim('\n');
                _sb.Length = start;
                _sb.Append(string.Join("\n", inner.Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l)));
                Break(2);
                break;
            }
            case "td":
            case "th":
                if (_tables.Count > 0)
                {
                    if (_cells.Count > 0)
                    {
                        _cells.Pop();
                    }
                    var cell = _sb.ToString(start, _sb.Length - start);
                    cell = Regex.Replace(cell.Trim(), @"\s*\n\s*", "<br>").Replace("|", "\\|");
                    _sb.Length = start;
                    _sb.Append(cell).Append(" |");
                    _tables.Peek().Cells++;
                }
                break;
            case "tr":
                if (_tables.Count > 0)
                {
                    var t = _tables.Peek();
                    if (!t.HeaderDone)
                    {
                        t.FirstRowCells = Math.Max(1, t.Cells);
                        t.HeaderDone = true;
                        _sb.Append('\n').Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", t.FirstRowCells)));
                    }
                    t.Row++;
                    _sb.Append('\n');
                }
                break;
            case "table":
                if (_tables.Count > 0)
                {
                    _tables.Pop();
                }
                Break(2);
                break;
            default:
                if (Blocks.Contains(tag))
                {
                    Break(_lists.Count > 0 ? 1 : 2);
                }
                break;
        }
    }

    /// <summary>Обернуть выделением; пустое выделение убрать, пробелы вынести наружу.</summary>
    private void Wrap(int start, string mark)
    {
        // открывающая метка уже стоит в start - mark.Length
        var open = start - mark.Length;
        var inner = _sb.ToString(start, _sb.Length - start);
        if (inner.Trim().Length == 0)
        {
            _sb.Remove(open, mark.Length);
            return;
        }
        var trailing = inner.Length - inner.TrimEnd().Length;
        _sb.Length -= trailing;
        _sb.Append(mark).Append(' ', trailing);
        var leading = inner.Length - inner.TrimStart().Length;
        if (leading > 0)
        {
            _sb.Remove(start, leading);
            _sb.Insert(open, new string(' ', leading));
        }
    }

    private static string Tidy(string md)
    {
        var lines = md.Replace("\r", "").Split('\n')
            .Select(l => l.TrimEnd() is var t && l.EndsWith("  ", StringComparison.Ordinal) && t.Length > 0 ? t + "  " : l.TrimEnd());
        var text = string.Join("\n", lines);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim('\n') + "\n";
    }
}
