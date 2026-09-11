using System.Security.Cryptography;
using System.Text;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ДОСТУП АГЕНТА К ВНЕШНИМ ФАЙЛАМ ЗАДАНИЯ (T-255).
///
/// Картинка задачи, приехавшей импортом (Trello, GitHub, GitLab), лежит для агента в двух
/// недосягаемых местах сразу: либо ссылкой в интернет — за авторизацией внешней системы,
/// либо файлом хранилища AI2P (<c>projects/&lt;код&gt;/uploads/…</c>), которого нет в каталоге
/// проекта. Инструментов дотянуться ни туда, ни туда у агента не было: файловые инструменты
/// ограничены каталогом проекта, а CLI-агент вообще работает своими средствами внутри
/// песочницы. На прямой вопрос «видишь ли ты приложенный скриншот» агент отвечал «не вижу».
///
/// Этот сервис даёт единственный ответ на оба случая: по ссылке он КЛАДЁТ ФАЙЛ НА ДИСК
/// в кэш агента (<c>&lt;dataDir&gt;/cache/agent-files</c>) и возвращает путь, по которому
/// файл можно прочитать обычными файловыми средствами. Правило простое:
/// <list type="number">
///   <item>файл уже в кэше (ключ — сама ссылка) — отдаётся из кэша, сеть не трогается;</item>
///   <item>ссылка наша (<c>api/files/raw?path=…</c>, <c>api/files/project?…</c> или путь
///         в каталоге данных) — файл берётся из хранилища и копируется в кэш;</item>
///   <item>иначе — скачивается из интернета с авторизацией той внешней системы, которой
///         принадлежит адрес (ключи берутся из активных источников импорта), и ложится в кэш.</item>
/// </list>
///
/// Кэш общий на организацию и живёт между заданиями: одна и та же картинка карточки,
/// нужная нескольким агентам, качается один раз.
/// </summary>
public sealed class FileFetchService
{
    private static readonly ILogger Logger = Log.ForContext<FileFetchService>();

    /// <summary>Общий HTTP-клиент сервиса; в тестах подменяется <see cref="HttpOverride"/>.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>Кэш скачанных агентом файлов — внутри каталога данных (ТЗ п. 6.4.4).</summary>
    public const string CacheDirRel = "cache/agent-files";

    /// <summary>Предел размера файла: больше агенту всё равно не прочитать, а место
    /// в каталоге данных занять можно молча.</summary>
    public const long MaxBytes = 50L * 1024 * 1024;

    private readonly FileStore _files;
    private readonly ImportSourceService _sources;
    private readonly ProjectService _projects;
    private readonly SecretStore _secrets;

    /// <summary>Значение секрета из БД организации (ТЗ гл. 10): ключи и токены источников
    /// импорта лежат там же, где ключи API моделей; null — только файлы секретов.</summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>Подменный HttpClient — только для тестов: живой интернет в прогоне не дёргается.</summary>
    public HttpClient? HttpOverride { get; set; }

    private HttpClient Client => HttpOverride ?? Http;

    public FileFetchService(FileStore files, ImportSourceService sources, ProjectService projects,
        SecretStore secrets)
    {
        _files = files;
        _sources = sources;
        _projects = projects;
        _secrets = secrets;
    }

    /// <summary>Каталог кэша (абсолютный): его же CLI-агент получает флагом
    /// <c>--add-dir</c> — иначе прочитать положенный туда файл ему не даст песочница.</summary>
    public string CacheDir => _files.Abs(CacheDirRel);

    /// <summary>Откуда взялся файл — для ответа агенту и для журнала.</summary>
    public enum FetchOrigin
    {
        /// <summary>Уже лежал в кэше (сеть не трогали).</summary>
        Cache,

        /// <summary>Файл хранилища AI2P (вложение импорта, артефакт) — скопирован в кэш.</summary>
        Storage,

        /// <summary>Скачан из интернета.</summary>
        Network,
    }

    /// <summary>Полученный файл: путь на диске (по нему агент его и читает) и что это такое.</summary>
    public sealed record Fetched(string Path, string Name, long Bytes, string ContentType,
        FetchOrigin Origin);

    /// <summary>
    /// Достать файл по ссылке и положить его в кэш агента; возвращается путь на диске.
    /// Порядок — кэш → хранилище AI2P → интернет (см. описание класса). Ошибки понятны
    /// агенту: он читает их как результат вызова инструмента.
    /// </summary>
    /// <param name="url">Ссылка из текста задания: внешняя http(s), наша ссылка на файл
    /// (<c>api/files/raw?path=…</c>) или путь в каталоге данных.</param>
    /// <param name="language">Язык общения с агентом (T-190) — на нём тексты отказов.</param>
    public async Task<Fetched> FetchAsync(string url, string? language = null,
        CancellationToken ct = default)
    {
        var value = (url ?? "").Trim().Trim('<', '>', '"', '\'');
        if (value.Length == 0)
        {
            throw new ArgumentException(Loc.In(language, "prompt.tasks.104"));
        }

        var key = KeyOf(value);
        var cached = FindCached(key);

        // 1. наш файл: ссылка api/files/raw / api/files/project либо путь в каталоге данных.
        // Проверяется ДО кэша: файл хранилища мог смениться (задачу обновили из источника),
        // и отдавать вчерашнюю копию нельзя
        if (LocalSourceOf(value, language, cached is not null) is { } local)
        {
            var source = new FileInfo(local);
            if (cached is not null && new FileInfo(cached) is { } copy
                && copy.Length == source.Length && copy.LastWriteTimeUtc >= source.LastWriteTimeUtc)
            {
                return FromCache(value, cached, key);
            }
            var name = SafeName(source.Name);
            var target = Path.Combine(CacheDir, $"{key}-{name}");
            Directory.CreateDirectory(CacheDir);
            // прежняя копия под ДРУГИМ именем осталась бы лежать с тем же ключом, и следующий
            // запрос той же ссылки мог бы получить её вместо свежей
            if (cached is not null && !string.Equals(cached, target, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(cached);
            }
            File.Copy(local, target, overwrite: true);
            var bytes = new FileInfo(target).Length;
            Logger.Information("Файл агента {Url} — из хранилища {Source} → {Path} ({Bytes} байт)",
                value, local, target, bytes);
            return new Fetched(target, name, bytes, ContentTypeOf(target), FetchOrigin.Storage);
        }

        // 2. кэш: ключ — сама ссылка, поэтому повторный запрос той же картинки бесплатен
        if (cached is not null)
        {
            return FromCache(value, cached, key);
        }

        // 3. интернет
        if (!IsWebUrl(value, out var uri))
        {
            throw new InvalidOperationException(Loc.In(language, "prompt.tasks.105", value));
        }
        var (data, fileName, contentType) = await DownloadAsync(uri, language, ct);
        var saved = Path.Combine(CacheDir, $"{key}-{SafeName(fileName)}");
        Directory.CreateDirectory(CacheDir);
        // сначала во временный файл: оборванная закачка не должна остаться в кэше «готовой».
        // Имя временного файла НЕ подходит под маску поиска в кэше («<ключ>-…»), иначе
        // недокачанный огрызок отдавался бы следующему запросу как готовая картинка
        var tmp = Path.Combine(CacheDir, key + ".part");
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, saved, overwrite: true);
        Logger.Information("Файл агента {Url} скачан: {Path} ({Bytes} байт, {ContentType})",
            value, saved, data.Length, contentType);
        return new Fetched(saved, SafeName(fileName), data.LongLength, contentType,
            FetchOrigin.Network);
    }

    // ------------------------------------------------------------------ кэш

    /// <summary>Ключ кэша — по САМОЙ ссылке: одинаковые ссылки в разных задачах указывают
    /// на один файл, а разные (пусть и на одну картинку) честно качаются по разу.</summary>
    public static string KeyOf(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim())))[..12].ToLowerInvariant();

    /// <summary>Готовый ответ по файлу, который уже лежит в кэше.</summary>
    private static Fetched FromCache(string url, string path, string key)
    {
        var info = new FileInfo(path);
        Logger.Information("Файл агента {Url} — из кэша: {Path} ({Bytes} байт)",
            url, path, info.Length);
        return new Fetched(path, info.Name[(key.Length + 1)..], info.Length,
            ContentTypeOf(path), FetchOrigin.Cache);
    }

    /// <summary>Файл кэша по ключу; null — не качали. Имя хранится как
    /// <c>&lt;ключ&gt;-&lt;имя файла&gt;</c>, поэтому имя видно и в самом кэше.</summary>
    private string? FindCached(string key)
    {
        var dir = CacheDir;
        if (!Directory.Exists(dir))
        {
            return null;
        }
        return Directory.EnumerateFiles(dir, key + "-*").FirstOrDefault();
    }

    // ------------------------------------------------- наш файл (без сети)

    /// <summary>
    /// Абсолютный путь НАШЕГО файла по ссылке; null — ссылка не наша. Разбираются: ссылка
    /// хранилища (<c>api/files/raw?path=…</c>, путь от каталога данных), ссылка на файл
    /// каталога проекта (<c>api/files/project?projectId=…&amp;path=…</c>) и голый
    /// относительный путь внутри каталога данных — именно в таком виде пути вложений
    /// попадают в описание задачи при импорте.
    /// </summary>
    /// <param name="haveCopy">Копия файла уже лежит в кэше: пропавший в хранилище исходник
    /// не должен рушить запрос — отдадим то, что получили раньше.</param>
    private string? LocalSourceOf(string url, string? language, bool haveCopy)
    {
        var link = FileLinks.Parse(url);
        if (link is null)
        {
            // «projects/PRJ-2/uploads/964ca6d4-image.png» — путь хранилища как есть
            return !IsWebUrl(url, out _) && !Path.IsPathRooted(url) && IsProjectFile(url)
                   && File.Exists(_files.Abs(url))
                ? _files.Abs(url)
                : null;
        }
        if (link.Kind == FileLinks.Raw)
        {
            if (!IsProjectFile(link.Path))
            {
                throw new InvalidOperationException(Loc.In(language, "prompt.tasks.106", link.Path));
            }
            var abs = _files.Abs(link.Path);
            return File.Exists(abs) ? abs
                : haveCopy ? null
                : throw new FileNotFoundException(Loc.In(language, "prompt.tasks.107", link.Path));
        }
        // файл каталога проекта: каталог свой на каждом сервере, путь внутри — общий
        var project = _projects.Get(link.ProjectId)
                      ?? throw new InvalidOperationException(
                          Loc.In(language, "prompt.tasks.108", link.ProjectId));
        var inFolder = InProjectFolder(project, link.Path)
                       ?? throw new InvalidOperationException(
                           Loc.In(language, "prompt.tasks.106", link.Path));
        return File.Exists(inFolder) ? inFolder
            : haveCopy ? null
            : throw new FileNotFoundException(Loc.In(language, "prompt.tasks.107", link.Path));
    }

    /// <summary>
    /// Путь каталога данных, который агенту ОТДАЁТСЯ (T-255): только файлы проектов —
    /// вложения импорта (<c>projects/&lt;код&gt;/uploads/…</c>), описания и артефакты задач.
    /// Всё остальное содержимое каталога организации (её база, профайлы моделей, ключи,
    /// файлы репликации) агенту по ссылке не выдаётся: в текстах заданий таких ссылок
    /// не бывает, а вынести базу организации одной строкой маркера было бы можно.
    /// </summary>
    private bool IsProjectFile(string relPath)
    {
        if (!_files.IsInside(relPath))
        {
            return false;
        }
        var rel = Path.GetRelativePath(_files.DataDir, _files.Abs(relPath)).Replace('\\', '/');
        return rel.StartsWith("projects/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Путь внутри каталога проекта; null — каталог не задан или путь выходит наружу.</summary>
    private static string? InProjectFolder(Project project, string relPath)
    {
        var folder = project.FolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }
        var root = Path.GetFullPath(folder);
        var abs = Path.GetFullPath(Path.Combine(root, relPath));
        return abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? abs
            : null;
    }

    // -------------------------------------------------------------- интернет

    /// <summary>Адрес http(s) — только такие качаются: file:, ftp: и прочее агенту не даём.</summary>
    private static bool IsWebUrl(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Скачать файл с авторизацией внешней системы, которой принадлежит адрес.</summary>
    private async Task<(byte[] Data, string Name, string ContentType)> DownloadAsync(Uri uri,
        string? language, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // без User-Agent часть сайтов (в том числе GitHub) отвечает отказом
        request.Headers.TryAddWithoutValidation("User-Agent", "AI2P");
        var auth = AuthorizeFor(uri);
        if (auth is { } header)
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }
        Logger.Information("Файл агента: GET {Url}{Auth}", uri,
            auth is null ? "" : $" (авторизация {auth.Value.Kind})");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning("Файл агента {Url} не отдан, HTTP {Status}", uri, (int)response.StatusCode);
            throw new InvalidOperationException(Loc.In(language, "prompt.tasks.109",
                uri.ToString(), (int)response.StatusCode,
                auth is null
                    ? Loc.In(language, "prompt.tasks.110")
                    : Loc.In(language, "prompt.tasks.111", auth.Value.Kind)));
        }
        if (response.Content.Headers.ContentLength is { } declared && declared > MaxBytes)
        {
            throw new InvalidOperationException(
                Loc.In(language, "prompt.tasks.112", declared, MaxBytes));
        }
        var data = await ReadLimitedAsync(response, language, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var name = NameOf(uri, response, contentType);
        return (data, name, contentType.Length > 0 ? contentType : ContentTypeOf(name));
    }

    /// <summary>Чтение тела ответа с пределом: сервер мог не назвать длину заранее.</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response,
        string? language, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                throw new InvalidOperationException(
                    Loc.In(language, "prompt.tasks.112", buffer.Length, MaxBytes));
            }
        }
        return buffer.ToArray();
    }

    /// <summary>Имя файла: заголовок Content-Disposition, иначе последний сегмент адреса;
    /// без расширения — оно достраивается по типу содержимого (картинки Trello приезжают
    /// адресом вида …/download/image.webp, но бывает и без имени вовсе).</summary>
    private static string NameOf(Uri uri, HttpResponseMessage response, string contentType)
    {
        var fromHeader = response.Content.Headers.ContentDisposition?.FileNameStar
                         ?? response.Content.Headers.ContentDisposition?.FileName;
        var name = (fromHeader ?? "").Trim('"', ' ');
        if (name.Length == 0)
        {
            name = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? "").Trim('/');
        }
        if (name.Length == 0)
        {
            name = "file";
        }
        return Path.HasExtension(name) ? name : name + ExtensionOf(contentType);
    }

    /// <summary>Расширение по типу содержимого — для файлов, у которых имени в адресе нет.</summary>
    private static string ExtensionOf(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        "text/markdown" => ".md",
        "application/json" => ".json",
        _ => "",
    };

    /// <summary>Тип содержимого по расширению — для файлов, взятых из хранилища и кэша.</summary>
    public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".pdf" => "application/pdf",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".md" or ".txt" => "text/plain",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };

    /// <summary>Имя файла в кэше: без каталогов и без знаков, которые файловая система
    /// не примет (правило общее с <see cref="FileStore.SaveUpload"/>).</summary>
    private static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        }
        var safe = sb.Length == 0 ? "file" : sb.ToString();
        return safe.Length <= 80 ? safe : safe[^80..];
    }

    // -------------------------------------------------- авторизация по адресу

    /// <summary>Заголовок авторизации внешней системы: вид (для лога и сообщений об ошибке),
    /// имя и значение заголовка.</summary>
    private readonly record struct AuthHeader(string Kind, string Name, string Value);

    /// <summary>
    /// Чем авторизоваться на этом адресе (T-255): ключи берутся у АКТИВНЫХ источников импорта
    /// той же системы — других реквизитов внешних систем в AI2P нет. Правило владения
    /// секретами то же, что у импорта (T-249): ссылки «trello.*» полагаются только Trello,
    /// токен GitLab уезжает заголовком PRIVATE-TOKEN, GitHub — Bearer. Источника нет или
    /// значение не заполнено — качаем без авторизации: публичная картинка отдастся и так.
    /// </summary>
    private AuthHeader? AuthorizeFor(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        try
        {
            if (host is "trello.com" or "www.trello.com" or "api.trello.com"
                || host.EndsWith(".trellousercontent.com", StringComparison.Ordinal))
            {
                var source = ActiveSource("trello");
                if (source is null)
                {
                    return null;
                }
                var p = TrelloImporter.SourceParams.Parse(source.ParamsJson);
                var key = Secret(p.KeyRef);
                var token = Secret(p.TokenRef);
                return key.Length > 0 && token.Length > 0
                    // download-эндпоинт Trello принимает только OAuth-заголовок (key/token
                    // строкой запроса ему недостаточно) — то же, что в импорте вложений
                    ? new AuthHeader("Trello", "Authorization",
                        $"OAuth oauth_consumer_key=\"{key}\", oauth_token=\"{token}\"")
                    : null;
            }
            if (host is "github.com" or "api.github.com" or "raw.githubusercontent.com"
                || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal))
            {
                var source = ActiveSource(GitHubImporter.Kind);
                if (source is null)
                {
                    return null;
                }
                var token = Secret(GitHubImporter.SourceParams.Parse(source.ParamsJson).TokenRef);
                return token.Length > 0
                    ? new AuthHeader("GitHub", "Authorization", $"Bearer {token}")
                    : null;
            }
            // GitLab бывает и свой: сравниваем с адресом сервера из источника
            foreach (var source in _sources.List().Where(s => s.IsActive
                         && string.Equals(s.Kind, GitLabImporter.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                var p = GitLabImporter.SourceParams.Parse(source.ParamsJson);
                if (!Uri.TryCreate(p.Host, UriKind.Absolute, out var serverUri)
                    || !string.Equals(serverUri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var token = Secret(p.TokenRef);
                if (token.Length > 0)
                {
                    return new AuthHeader("GitLab", "PRIVATE-TOKEN", token);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            // сломанный params_json источника не должен рушить получение файла: он мог
            // и не требовать никакой авторизации вовсе
            Logger.Warning(ex, "Файл агента: реквизиты для {Host} не разобраны — качаю без них", host);
        }
        return null;
    }

    /// <summary>Единственный активный источник импорта этого вида; null — их нет или
    /// их несколько (какой из них «тот самый», решить нечем).</summary>
    private ImportSource? ActiveSource(string kind)
    {
        var found = _sources.List()
            .Where(s => s.IsActive && string.Equals(s.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Значение секрета по ссылке; пусто — не заполнено (в лог значения не пишем).</summary>
    private string Secret(string secretRef)
    {
        if (secretRef.Trim().Length == 0)
        {
            return "";
        }
        var (value, _) = KeyResolver is not null
            ? KeyResolver(secretRef)
            : _secrets.ResolveWithSource(secretRef);
        return (value ?? "").Trim();
    }
}
