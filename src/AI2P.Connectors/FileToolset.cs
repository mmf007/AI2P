using AI2P.Core;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Connectors;

/// <summary>
/// Инструменты (tools) работы с файлами каталога проекта для ИИ-агента (ТЗ п. 2.4, п. 7.2,
/// todo17): публикуются агенту в формате function calling / MCP; агент вызывает их обычным
/// tool call, результат возвращается как tool result. По умолчанию операции ограничены
/// каталогом проекта (folderPath, ТЗ гл. 12); правила безопасности могут открыть каталоги
/// вне проекта (обращение по полному пути) и закрыть каталоги внутри (todo25/todo26);
/// удаления нет. Каждый коннектор оборачивает Specs в свой формат, выполняет ExecuteAsync.
/// </summary>
public sealed class FileToolset
{
    /// <summary>Описание инструмента: имя, назначение, JSON Schema параметров (строка).</summary>
    public sealed record ToolSpec(string Name, string Description, string ParametersJson);

    private const int MaxListEntries = 1000;
    private const int MaxReadBytes = 200_000;
    private const int MaxSearchHits = 100;
    private const long MaxScanFileBytes = 2_000_000;

    // служебные каталоги, которые не показываем и не сканируем
    private static readonly HashSet<string> SkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules", ".vs", ".idea" };

    // бинарные расширения — не читаем как текст, не ищем внутри
    private static readonly HashSet<string> BinaryExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".pdb", ".zip", ".gz", ".7z", ".rar", ".png", ".jpg", ".jpeg",
            ".gif", ".webp", ".bmp", ".ico", ".pdf", ".mp3", ".mp4", ".mov", ".avi", ".wav",
            ".ttf", ".otf", ".woff", ".woff2", ".so", ".dylib", ".bin", ".dat",
        };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Читаемый лог: не экранировать кириллицу/символы в сводке аргументов.</summary>
    private static readonly JsonSerializerOptions LogJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Абсолютный каталог проекта (корень песочницы инструментов).</summary>
    public string RootDir { get; }

    /// <summary>Id проекта — для внешних HTTP-ссылок на файлы (todo19).</summary>
    public string? ProjectId { get; }

    /// <summary>Публичный базовый URL приложения ({протокол}://{host}:{порт}{basePath}) для ссылок.</summary>
    public string? PublicBaseUrl { get; }

    /// <summary>Лог вызовов инструментов для журнала работ и диагностики.</summary>
    public List<string> CallLog { get; } = [];

    /// <summary>
    /// Язык ОТВЕТОВ инструментов (T-190/T-180): результат вызова возвращается агенту как
    /// tool result, значит его язык задаёт команда задачи (<c>Team.AgentLanguage</c>), а не
    /// язык установки; null — язык установки. Раздаёт <see cref="AgentToolset.Language"/>.
    /// Записи <see cref="CallLog"/> — наоборот, журнал работ для ЧЕЛОВЕКА: они на языке
    /// установки (<c>Loc.T</c>).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Разрешён ли доступ к пути (absPath, op: read/write/delete) — правила
    /// безопасности (ТЗ гл. 12, todo25); по умолчанию — только внутри каталога проекта.</summary>
    private readonly Func<string, string, bool> _isPathPermitted;

    public FileToolset(string rootDir, string? projectId = null, string? publicBaseUrl = null,
        Func<string, string, bool>? isPathPermitted = null)
    {
        RootDir = Path.GetFullPath(rootDir);
        ProjectId = projectId;
        PublicBaseUrl = publicBaseUrl?.TrimEnd('/');
        _isPathPermitted = isPathPermitted ?? ((abs, _) => IsInsideRoot(abs));
    }

    /// <summary>Путь внутри каталога проекта (граница по умолчанию, ТЗ гл. 12).</summary>
    private bool IsInsideRoot(string abs) =>
        abs.StartsWith(RootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || string.Equals(abs, RootDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>Полный путь БЕЗ проверки границ — для проверки правил безопасности
    /// до выполнения (AgentToolset.Authorize, todo25); пусто — корень проекта.</summary>
    public string ResolveUnchecked(string? relPath) =>
        string.IsNullOrWhiteSpace(relPath) ? RootDir : Path.GetFullPath(Path.Combine(RootDir, relPath));

    /// <summary>Опубликованные агенту инструменты (ТЗ п. 2.4). Текстов здесь НЕТ (todo24):
    /// промпты (описания) подставляются из справочника действий (Настройки → Действия);
    /// в коде — только имена и структурные схемы параметров.</summary>
    public static IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("list_files", "",
            """{ "type": "object", "properties": { "path": { "type": "string" } } }"""),
        new("search_files", "",
            """{ "type": "object", "properties": { "query": { "type": "string" }, "path": { "type": "string" } }, "required": ["query"] }"""),
        new("read_file", "",
            """{ "type": "object", "properties": { "path": { "type": "string" } }, "required": ["path"] }"""),
        new("write_file", "",
            """{ "type": "object", "properties": { "path": { "type": "string" }, "content": { "type": "string" } }, "required": ["path", "content"] }"""),
        new("file_url", "",
            """{ "type": "object", "properties": { "path": { "type": "string" } }, "required": ["path"] }"""),
    ];

    /// <summary>Выполнить вызов инструмента; результат (или текст ошибки) возвращается агенту.</summary>
    public Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        try
        {
            var result = name switch
            {
                "list_files" => ListFiles(OptionalString(args, "path")),
                "search_files" => SearchFiles(RequiredString(args, "query"), OptionalString(args, "path")),
                "read_file" => ReadFile(RequiredString(args, "path")),
                "write_file" => WriteFile(RequiredString(args, "path"), RequiredString(args, "content")),
                "file_url" => FileUrl(RequiredString(args, "path")),
                _ => Loc.In(Language, "prompt.tasks.1", name),
            };
            CallLog.Add($"{name}({ArgsSummary(args)}) → {Preview(result)}");
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            var error = Loc.In(Language, "prompt.tasks.2", name, ex.Message);
            CallLog.Add(error);
            return Task.FromResult(error);
        }
    }

    private string ListFiles(string? subPath)
    {
        var dir = ResolveDir(subPath);
        var sb = new StringBuilder();
        var count = 0;
        // файлы, закрытые правилами безопасности на чтение, не показываются (todo25)
        foreach (var file in EnumerateFiles(dir).Where(f => _isPathPermitted(f, "read")))
        {
            if (count++ >= MaxListEntries)
            {
                sb.AppendLine(Loc.In(Language, "prompt.fileToolset.1", MaxListEntries));
                break;
            }
            sb.AppendLine(Loc.In(Language, "prompt.fileToolset.2", DisplayPath(file), new FileInfo(file).Length));
        }
        return sb.Length == 0 ? Loc.In(Language, "prompt.fileToolset.3") : sb.ToString().TrimEnd();
    }

    private string SearchFiles(string query, string? subPath)
    {
        var dir = ResolveDir(subPath);
        var sb = new StringBuilder();
        var hits = 0;
        foreach (var file in EnumerateFiles(dir).Where(f => _isPathPermitted(f, "read")))
        {
            if (IsBinary(file) || new FileInfo(file).Length > MaxScanFileBytes)
            {
                continue;
            }
            var rel = DisplayPath(file);
            var lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    if (hits++ >= MaxSearchHits)
                    {
                        return sb.AppendLine(Loc.In(Language, "prompt.fileToolset.4", MaxSearchHits)).ToString().TrimEnd();
                    }
                    var snippet = line.Trim();
                    sb.AppendLine($"{rel}:{lineNo}: {(snippet.Length > 200 ? snippet[..200] + "…" : snippet)}");
                }
            }
        }
        return hits == 0 ? Loc.In(Language, "prompt.fileToolset.5", query) : sb.ToString().TrimEnd();
    }

    private string ReadFile(string relPath)
    {
        var abs = ResolveFile(relPath);
        if (!File.Exists(abs))
        {
            return Loc.In(Language, "prompt.fileToolset.6", relPath);
        }
        if (IsBinary(abs))
        {
            return Loc.In(Language, "prompt.fileToolset.7", relPath);
        }
        var bytes = File.ReadAllBytes(abs);
        var text = Utf8NoBom.GetString(bytes);
        if (bytes.Length > MaxReadBytes)
        {
            text = Utf8NoBom.GetString(bytes, 0, MaxReadBytes) + Loc.In(Language, "prompt.fileToolset.8", MaxReadBytes);
        }
        return text;
    }

    private string WriteFile(string relPath, string content)
    {
        var abs = ResolveFile(relPath, "write");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        var existed = File.Exists(abs);
        File.WriteAllText(abs, content, Utf8NoBom);
        // в ответе — внешняя ссылка, чтобы агент давал корректный URL, а не придумывал (todo19)
        return Loc.In(Language, "prompt.fileToolset.12",
            Loc.In(Language, existed ? "prompt.fileToolset.13" : "prompt.fileToolset.14"),
            relPath, Utf8NoBom.GetByteCount(content), ExternalUrl(relPath));
    }

    /// <summary>Внешняя ссылка (URL) на файл проекта.</summary>
    private string FileUrl(string relPath)
    {
        var abs = ResolveFile(relPath); // проверка каталога + приведение
        return File.Exists(abs)
            ? ExternalUrl(relPath)
            : Loc.In(Language, "prompt.fileToolset.6", relPath);
    }

    /// <summary>
    /// Внешняя ссылка на файл (ТЗ v1.16, todo19): HTTP-отдача через приложение для файлов
    /// внутри каталога проекта. Файл вне проекта (каталог открыт правилом безопасности,
    /// todo26) HTTP-эндпойнт не отдаёт — возвращается локальная file:-ссылка.
    /// </summary>
    private string ExternalUrl(string relPath)
    {
        var abs = Path.GetFullPath(Path.Combine(RootDir, relPath));
        if (!IsInsideRoot(abs) || PublicBaseUrl is null || ProjectId is null)
        {
            return new Uri(abs).AbsoluteUri;
        }
        var normalized = Path.GetRelativePath(RootDir, abs).Replace('\\', '/');
        return $"{PublicBaseUrl}/api/files/project?projectId={Uri.EscapeDataString(ProjectId)}" +
               $"&path={Uri.EscapeDataString(normalized)}";
    }

    /// <summary>Путь для показа агенту: внутри проекта — относительный, вне (каталог открыт
    /// правилом, todo26) — полный; слэши «/».</summary>
    private string DisplayPath(string absFile)
    {
        var rel = Path.GetRelativePath(RootDir, absFile);
        var outside = rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel);
        return (outside ? absFile : rel).Replace('\\', '/');
    }

    private IEnumerable<string> EnumerateFiles(string dir)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }
        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var sub in subdirs)
            {
                if (!SkipDirs.Contains(Path.GetFileName(sub)))
                {
                    stack.Push(sub);
                }
            }
            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>Абсолютный путь файла с проверкой правил безопасности (ТЗ гл. 12, todo25):
    /// по умолчанию — каталог проекта; правила могут открыть путь вне проекта и закрыть внутри.</summary>
    private string ResolveFile(string relPath, string op = "read")
    {
        if (string.IsNullOrWhiteSpace(relPath))
        {
            throw new ArgumentException(Loc.In(Language, "prompt.fileToolset.9"));
        }
        var abs = Path.GetFullPath(Path.Combine(RootDir, relPath));
        if (!_isPathPermitted(abs, op))
        {
            throw new UnauthorizedAccessException(IsInsideRoot(abs)
                ? Loc.In(Language, "prompt.fileToolset.10", relPath, op)
                : Loc.In(Language, "prompt.fileToolset.11", relPath));
        }
        return abs;
    }

    private string ResolveDir(string? subPath, string op = "read") =>
        string.IsNullOrWhiteSpace(subPath) ? RootDir : ResolveFile(subPath, op);

    private static bool IsBinary(string path) => BinaryExtensions.Contains(Path.GetExtension(path));

    // не static: текст отказа читает АГЕНТ, значит он берётся по языку команды (Language)
    private string RequiredString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException(Loc.In(Language, "prompt.tasks.95", name));

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string ArgsSummary(JsonElement args)
    {
        try
        {
            var node = JsonNode.Parse(args.GetRawText());
            if (node is JsonObject obj && obj.TryGetPropertyValue("content", out var c) && c is not null)
            {
                obj["content"] = Loc.T("msg.fileToolset.1", c.ToString().Length); // содержимое файла в лог не пишем целиком
            }
            return node?.ToJsonString(LogJson) ?? args.GetRawText();
        }
        catch (JsonException)
        {
            return args.GetRawText();
        }
    }

    private static string Preview(string text)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length > 120 ? firstLine[..120] + "…" : firstLine;
    }
}
