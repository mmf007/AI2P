using System.Text.Json;
using System.Text.Json.Nodes;
using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Connectors;

/// <summary>
/// ОДИН ПЛАГИН SaaS-РЕДАКТОРА САЙТОВ, ГОТОВЫЙ К РАБОТЕ НА ЭТОМ СЕРВЕРЕ (T-567-S0): манифест
/// (подключение MCP и блок <c>saas</c>), список сайтов из config.json этого сервера и
/// чтение ключей из <c>secrets/</c>.
/// </summary>
/// <param name="Manifest">Манифест плагина вида mcp с блоком <c>saas</c>.</param>
/// <param name="Sites">Сайты, заведённые на этом сервере.</param>
/// <param name="Secret">Чтение секрета по ссылке; null — секрета нет.</param>
public sealed record SaasPlugin(PluginManifest Manifest, IReadOnlyList<SaasSite> Sites,
    Func<string, string?> Secret)
{
    /// <summary>Ключ сайта: свой ключ сайта, иначе общий ключ плагина.</summary>
    public string? KeyOf(SaasSite site) =>
        Secret(SaasSite.SecretRef(Manifest.Code, site.Code)) is { Length: > 0 } own
            ? own
            : Secret(PluginCodes.SecretRef(Manifest.Code, Manifest.Connection?.SecretRef));

    /// <summary>Заголовки подключения с подставленными полями сайта.</summary>
    public Dictionary<string, string> HeadersOf(SaasSite site) =>
        (Manifest.Connection?.Headers ?? [])
        .ToDictionary(h => h.Key, h => site.Fill(h.Value), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// УНИВЕРСАЛЬНЫЕ ДЕЙСТВИЯ АГЕНТА НАД САЙТАМИ В SaaS-РЕДАКТОРАХ (T-567-S0): <c>site_list</c>,
/// <c>site_tools</c>, <c>site_call</c>. Одни на все SaaS: Wix сегодня, следующий конструктор
/// сайтов — это манифест плагина с блоком <c>saas</c>, а не новые инструменты.
///
/// <para>БЕЗОПАСНОСТЬ В ТРИ СЛОЯ, и все три считаются ДО вызова (<see cref="Check"/> из
/// <c>AgentToolset.Authorize</c>):</para>
/// <list type="number">
/// <item>запись справочника действий у самого инструмента <c>site_*</c> (AI2P.Site.*) — им
/// закрывается работа с сайтами целиком;</item>
/// <item>правило «плагины и MCP» по маске <c>&lt;плагин&gt;:&lt;сайт&gt;:&lt;операция&gt;</c>:
/// <c>saas.wix</c> — весь Wix, <c>saas.wix:ai2p:*</c> — один сайт, <c>*:*:publish</c> —
/// публикация везде, <c>*</c> — всё;</item>
/// <item>у вызываемого инструмента SaaS должна быть запись справочника (её заводит
/// инициализация плагина) — иначе ЗАПРЕЩЕНО, как у любого инструмента MCP (T-112-S0).</item>
/// </list>
/// </summary>
public sealed class SiteToolset
{
    public const string ListTool = "site_list";
    public const string ToolsTool = "site_tools";
    public const string CallTool = "site_call";
    public const string UploadTool = "site_upload";

    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(StringComparer.Ordinal) { ListTool, ToolsTool, CallTool, UploadTool };

    /// <summary>Сколько текста ответа SaaS отдавать агенту за один вызов.</summary>
    private const int MaxAnswer = 60_000;

    private readonly IReadOnlyList<SaasPlugin> _plugins;
    private readonly SecurityEvaluator? _security;
    private readonly Func<string, string?> _codeByTool;
    private readonly string? _projectFolder;
    private readonly Func<string, string, bool> _isPathPermitted;
    private readonly HttpClient? _http;

    public string? Language { get; set; }

    public List<string> CallLog { get; } = [];

    /// <param name="projectFolder">Папка проекта — корень папки выкладки <c>site_upload</c>;
    /// null — выкладки у задания нет.</param>
    /// <param name="isPathPermitted">Правила доступа к каталогам (как у файловых
    /// инструментов): выкладка ЧИТАЕТ файлы папки.</param>
    public SiteToolset(IReadOnlyList<SaasPlugin> plugins, SecurityEvaluator? security,
        Func<string, string?>? codeByTool, string? projectFolder = null,
        Func<string, string, bool>? isPathPermitted = null, HttpClient? http = null)
    {
        _plugins = plugins;
        _security = security;
        _codeByTool = codeByTool ?? (_ => null);
        _projectFolder = projectFolder;
        _isPathPermitted = isPathPermitted ?? ((_, _) => true);
        _http = http;
    }

    public bool CanUse => _plugins.Count > 0;

    /// <summary>Выкладка публикуется, только если её умеет хоть один SaaS и у задачи есть папка
    /// проекта: инструмент, который заведомо откажет, — потраченный ход агента.</summary>
    private bool CanUpload => _projectFolder is { Length: > 0 }
                              && _plugins.Any(p => p.Manifest.Saas!.Upload is not null);

    public bool Handles(string name) => CanUse && Names.Contains(name) && (name != UploadTool || CanUpload);

    /// <summary>Описания — из справочника действий (AI2P.Site.*), здесь только схемы.</summary>
    public IEnumerable<FileToolset.ToolSpec> Specs
    {
        get
        {
            if (!CanUse)
            {
                yield break;
            }
            yield return new(ListTool, "", """{"type":"object","properties":{"saas":{"type":"string"}}}""");
            yield return new(ToolsTool, "", """{"type":"object","properties":{"saas":{"type":"string"},"site":{"type":"string"},"filter":{"type":"string"}}}""");
            yield return new(CallTool, "", """{"type":"object","properties":{"saas":{"type":"string"},"site":{"type":"string"},"tool":{"type":"string"},"arguments":{"type":"object"}},"required":["site","tool"]}""");
            if (CanUpload)
            {
                yield return new(UploadTool, "", """{"type":"object","properties":{"saas":{"type":"string"},"site":{"type":"string"},"dir":{"type":"string"}},"required":["site","dir"]}""");
            }
        }
    }

    /// <summary>
    /// РЕШЕНИЕ ПРАВИЛ ПО ВЫЗОВУ: операция над сайтом и регистрация инструмента SaaS. null —
    /// добавить к общему решению нечего (список сайтов, не найденный сайт: отказ скажет сам
    /// вызов).
    /// </summary>
    public (SecurityDecision Decision, SecurityRule? Rule, string Info)? Check(string tool, JsonElement args)
    {
        if (tool == ListTool || Resolve(args, out var plugin, out var site, out _) is false)
        {
            return null;
        }
        var native = Str(args, "tool");
        var op = tool switch
        {
            ToolsTool => SaasOps.Read,
            // выкладка заменяет все файлы и публикует — операция из манифеста (publish)
            UploadTool => plugin!.Manifest.Saas!.Upload?.Op ?? SaasOps.Publish,
            _ => plugin!.Manifest.Saas!.Classify(native, ArgsText(args)),
        };
        var best = (Decision: SecurityDecision.Allow, Rule: (SecurityRule?)null, Info: "");
        if (_security is not null)
        {
            var (decision, rule) = _security.ForPlugin(plugin!.Manifest.Code, SecurityEvaluator.PluginUse,
                site!.Code + ":" + op);
            best = (decision, rule, rule is null ? "" : Loc.In(Language, "prompt.site.1",
                rule.Pattern, plugin.Manifest.Code + ":" + site.Code + ":" + op));
        }
        if (tool == CallTool && native.Length > 0
            && !string.Equals(_codeByTool(native), PluginCodes.Action(plugin!.Manifest.Code, native),
                StringComparison.OrdinalIgnoreCase))
        {
            // ОБРАТНАЯ ПОЛИТИКА (T-112-S0): инструмент SaaS без записи справочника человек не
            // разрешал — он появился на сервере после инициализации или не существует вовсе
            return (SecurityDecision.Deny, null, Loc.In(Language, "prompt.site.2", native,
                plugin.Manifest.Code));
        }
        return best.Decision == SecurityDecision.Allow ? null : best;
    }

    public async Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        string result;
        try
        {
            result = name switch
            {
                ListTool => List(args),
                ToolsTool => await ToolsAsync(args, ct),
                CallTool => await CallAsync(args, ct),
                UploadTool => await UploadAsync(args, ct),
                _ => Loc.In(Language, "prompt.tools.5", name),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                       or TaskCanceledException or IOException or JsonException
                                       or UnauthorizedAccessException)
        {
            result = Loc.In(Language, "prompt.site.3", name, ex.Message);
        }
        CallLog.Add($"{name}({Preview(ArgsText(args))}) → {Preview(result)}");
        return result;
    }

    // --- site_list ---

    private string List(JsonElement args)
    {
        var saas = Str(args, "saas");
        var list = new JsonArray();
        foreach (var plugin in _plugins.Where(p => saas.Length == 0 || Same(p, saas)))
        {
            foreach (var site in plugin.Sites)
            {
                var denied = _security is not null && _security.ForPlugin(plugin.Manifest.Code,
                    SecurityEvaluator.PluginUse, site.Code + ":" + SaasOps.Read).Decision == SecurityDecision.Deny;
                list.Add(new JsonObject
                {
                    ["saas"] = plugin.Manifest.Saas!.Code,
                    ["plugin"] = plugin.Manifest.Code,
                    ["site"] = site.Code,
                    ["name"] = site.Name,
                    ["url"] = site.Url,
                    ["siteId"] = site.SiteId,
                    ["note"] = site.Note,
                    ["hasKey"] = plugin.KeyOf(site) is { Length: > 0 },
                    ["deniedByRules"] = denied,
                });
            }
        }
        if (list.Count == 0)
        {
            return Loc.In(Language, "prompt.site.4");
        }
        return new JsonObject
        {
            ["sites"] = list,
            ["operations"] = new JsonArray([.. SaasOps.All.Select(o => (JsonNode)o)]),
        }.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    // --- site_tools ---

    private async Task<string> ToolsAsync(JsonElement args, CancellationToken ct)
    {
        if (!Resolve(args, out var plugin, out var site, out var refusal))
        {
            return refusal;
        }
        using var transport = Open(plugin!, site!);
        var id = await McpClient.InitializeAsync(transport, ct);
        var tools = await McpClient.ListPagesAsync(transport, id, ct);
        var filter = Str(args, "filter");
        var list = new JsonArray();
        foreach (var t in tools.Where(t => filter.Length == 0
                                           || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                           || t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new JsonObject
            {
                ["tool"] = t.Name,
                ["registered"] = string.Equals(_codeByTool(t.Name),
                    PluginCodes.Action(plugin!.Manifest.Code, t.Name), StringComparison.OrdinalIgnoreCase),
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.Length > 0 ? JsonNode.Parse(t.InputSchema) : null,
            });
        }
        return Cut(new JsonObject
        {
            ["saas"] = plugin!.Manifest.Saas!.Code,
            ["site"] = site!.Code,
            ["siteArg"] = plugin.Manifest.Saas.SiteArg,
            ["tools"] = list,
        }.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    // --- site_call ---

    private async Task<string> CallAsync(JsonElement args, CancellationToken ct)
    {
        if (!Resolve(args, out var plugin, out var site, out var refusal))
        {
            return refusal;
        }
        var tool = Str(args, "tool");
        if (tool.Length == 0)
        {
            return Loc.In(Language, "prompt.site.5");
        }
        var arguments = args.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(a.GetRawText())!.AsObject()
            : new JsonObject();
        using var transport = Open(plugin!, site!);
        var id = await McpClient.InitializeAsync(transport, ct);
        var tools = await McpClient.ListPagesAsync(transport, id, ct);
        var info = tools.FirstOrDefault(t => t.Name == tool);
        if (info is null)
        {
            return Loc.In(Language, "prompt.site.6", tool, plugin!.Manifest.Code);
        }
        // САЙТ ПОДСТАВЛЯЕТСЯ ИЗ СПИСКА, а чужой идентификатор в вызове — отказ: иначе
        // правило «только сайт ai2p» обходилось бы одной подменой аргумента
        var siteArg = plugin!.Manifest.Saas!.SiteArg;
        if (siteArg.Length > 0 && site!.SiteId.Trim().Length > 0)
        {
            var given = arguments[siteArg]?.GetValueKind() == JsonValueKind.String
                ? arguments[siteArg]!.GetValue<string>()
                : null;
            if (given is { Length: > 0 }
                && !string.Equals(given.Trim(), site.SiteId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Loc.In(Language, "prompt.site.7", siteArg, given, site.Code);
            }
            if (TakesArg(info, siteArg))
            {
                arguments[siteArg] = site.SiteId.Trim();
            }
        }
        var answer = await McpClient.CallToolAsync(transport, 1000, tool, arguments.ToJsonString(), ct);
        return Cut(answer);
    }

    // --- site_upload ---

    /// <summary>
    /// ВЫКЛАДКА ПАПКИ ПРОЕКТА НА САЙТ (T-582-S0): все файлы папки <c>dir</c> уходят одним
    /// запросом multipart на адрес <c>saas.upload.url</c> с ключом сайта. Загрузка заменяет
    /// ВЕСЬ набор файлов сайта, поэтому всё проверяется ДО запроса: папка внутри проекта,
    /// правила каталогов, наличие главной страницы, предел размера — неполная или пустая
    /// выкладка оставила бы опубликованный сайт пустым.
    /// </summary>
    private async Task<string> UploadAsync(JsonElement args, CancellationToken ct)
    {
        if (!Resolve(args, out var plugin, out var site, out var refusal))
        {
            return refusal;
        }
        var upload = plugin!.Manifest.Saas!.Upload;
        if (upload is null || _projectFolder is not { Length: > 0 })
        {
            return Loc.In(Language, "prompt.site.9", plugin.Manifest.Code);
        }
        if (site!.SiteId.Trim().Length == 0)
        {
            return Loc.In(Language, "prompt.site.10", site.Code);
        }
        var dir = Str(args, "dir");
        var root = Path.GetFullPath(_projectFolder);
        var abs = Path.GetFullPath(Path.Combine(root, dir.Length == 0 ? "." : dir));
        var rootSlash = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(dir) || !(abs + Path.DirectorySeparatorChar).StartsWith(rootSlash,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return Loc.In(Language, "prompt.site.11", dir);
        }
        if (!Directory.Exists(abs))
        {
            return Loc.In(Language, "prompt.site.12", dir);
        }
        if (!_isPathPermitted(abs, "read"))
        {
            return Loc.In(Language, "prompt.site.13", dir);
        }
        var files = Directory.EnumerateFiles(abs, "*", SearchOption.AllDirectories)
            .Select(f => (Abs: f, Rel: Path.GetRelativePath(abs, f).Replace('\\', '/')))
            .Where(f => !upload.Skip.Contains(f.Rel, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            return Loc.In(Language, "prompt.site.12", dir);
        }
        if (upload.Index.Length > 0
            && !files.Any(f => string.Equals(f.Rel, upload.Index, StringComparison.OrdinalIgnoreCase)))
        {
            return Loc.In(Language, "prompt.site.14", dir, upload.Index);
        }
        var total = files.Sum(f => new FileInfo(f.Abs).Length);
        if (total > upload.MaxBytes)
        {
            return Loc.In(Language, "prompt.site.15", total, upload.MaxBytes);
        }

        using var content = new MultipartFormDataContent();
        foreach (var (fileAbs, rel) in files)
        {
            var part = new ByteArrayContent(await File.ReadAllBytesAsync(fileAbs, ct));
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MimeOf(rel));
            // имена ОБЯЗАТЕЛЬНО в кавычках (наука T-258): .NET по умолчанию их опускает, и
            // путь с подкаталогом сервер прочёл бы иначе
            part.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
            {
                Name = "\"" + upload.Part + "\"",
                FileName = "\"" + rel + "\"",
            };
            content.Add(part);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, site.Fill(upload.Url)) { Content = content };
        var connection = plugin.Manifest.Connection;
        var key = plugin.KeyOf(site);
        if (connection is not null && connection.SecretHeader.Length > 0 && key is { Length: > 0 })
        {
            // ключ — так же, как его шлёт подключение MCP этого плагина (у Wix — как есть)
            var value = connection.SecretScheme is not null
                ? (connection.SecretScheme.Length > 0 ? connection.SecretScheme + " " + key.Trim() : key.Trim())
                : connection.SecretHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                  && !key.Trim().Contains(' ')
                    ? "Bearer " + key.Trim()
                    : key.Trim();
            request.Headers.TryAddWithoutValidation(connection.SecretHeader, value);
        }
        foreach (var (name, value) in plugin.HeadersOf(site))
        {
            if (value.Trim().Length > 0 && !value.Contains('{'))
            {
                request.Headers.TryAddWithoutValidation(name, value.Trim());
            }
        }
        var http = _http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return Cut(Loc.In(Language, response.IsSuccessStatusCode ? "prompt.site.16" : "prompt.site.17",
                (int)response.StatusCode, files.Count, total, body));
        }
        finally
        {
            if (_http is null)
            {
                http.Dispose();
            }
        }
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".json" or ".map" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".txt" or ".md" => "text/plain",
        ".xml" => "application/xml",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };

    private static bool TakesArg(McpToolInfo info, string arg)
    {
        if (info.InputSchema.Length == 0)
        {
            return false;
        }
        using var doc = JsonDocument.Parse(info.InputSchema);
        return doc.RootElement.TryGetProperty("properties", out var props)
               && props.ValueKind == JsonValueKind.Object
               && props.TryGetProperty(arg, out _);
    }

    private IMcpTransport Open(SaasPlugin plugin, SaasSite site)
    {
        var connection = plugin.Manifest.Connection
                         ?? throw new InvalidOperationException(Loc.T("msg.mcp.1", plugin.Manifest.Code));
        return McpClient.Open(connection, plugin.KeyOf(site), plugin.HeadersOf(site));
    }

    // --- общее ---

    /// <summary>Найти плагин и сайт по аргументам <c>saas</c> и <c>site</c>. SaaS можно не
    /// называть, если имя сайта однозначно; сайт можно не называть, если он у SaaS один.</summary>
    private bool Resolve(JsonElement args, out SaasPlugin? plugin, out SaasSite? site, out string refusal)
    {
        plugin = null;
        site = null;
        var saas = Str(args, "saas");
        var code = Str(args, "site");
        var candidates = _plugins
            .Where(p => saas.Length == 0 || Same(p, saas))
            .SelectMany(p => p.Sites.Select(s => (Plugin: p, Site: s)))
            .Where(x => code.Length == 0 || string.Equals(x.Site.Code, code, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count != 1)
        {
            refusal = Loc.In(Language, "prompt.site.8", saas.Length > 0 ? saas : "*",
                code.Length > 0 ? code : "*",
                string.Join(", ", _plugins.SelectMany(p => p.Sites.Select(s => p.Manifest.Saas!.Code + "/" + s.Code))));
            return false;
        }
        (plugin, site) = candidates[0];
        refusal = "";
        return true;
    }

    private static bool Same(SaasPlugin plugin, string saas) =>
        string.Equals(plugin.Manifest.Saas!.Code, saas, StringComparison.OrdinalIgnoreCase)
        || string.Equals(plugin.Manifest.Code, saas, StringComparison.OrdinalIgnoreCase);

    private static string Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim()
            : "";

    /// <summary>Текст аргументов вызова SaaS — по нему правила манифеста узнают операцию.</summary>
    private static string ArgsText(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty("arguments", out var a)
            ? a.GetRawText()
            : "";

    private static string Cut(string text) =>
        text.Length <= MaxAnswer ? text : text[..MaxAnswer] + "\n… (" + text.Length + ")";

    private static string Preview(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
