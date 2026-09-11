using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// МИНИМАЛЬНЫЙ КЛИЕНТ MCP (T-119-S0) — ровно то, что нужно записи плагина вида <c>mcp</c>:
/// подключиться к серверу, поздороваться и снять список инструментов (<c>tools/list</c>).
///
/// <para>ПОЧЕМУ СВОЙ, А НЕ ГОТОВАЯ БИБЛИОТЕКА. Официальный SDK
/// <c>ModelContextProtocol</c> 2.2.0 (github.com/modelcontextprotocol/csharp-sdk, лицензия
/// Apache-2.0 — репозиторий переводится с MIT на Apache-2.0, обе разрешительные и нам
/// годятся) в зависимостях требует <c>Microsoft.Extensions.Caching.Abstractions</c> и
/// <c>Microsoft.Extensions.Hosting.Abstractions</c> версии <b>10.0.10 и выше</b>, то есть
/// тянет в приложение на <c>net8.0</c> весь стек расширений .NET 10. Ради двух вызовов
/// протокола это дорого, а обновление целевой платформы — отдельная работа. Взамен здесь
/// 200 строк JSON-RPC 2.0 — ровно тем же способом, каким в AI2P написаны все прочие
/// клиенты чужих API (ComfyUI, fal.ai, Trello, GitHub). Полное обоснование и что случится,
/// когда понадобятся ресурсы и подписки MCP, — <c>doc/T-119-S0_mcp_клиент.md</c>.</para>
///
/// <para>ГЛАВНОЕ ПРАВИЛО РАЗДЕЛА, ради которого этот клиент вообще существует: <b>MCP
/// подключается ТОЛЬКО через нашу запись плагина.</b> MCP-сервер, прописанный напрямую в
/// конфиг CLI-агента, проходит мимо всей безопасности AI2P — его инструментов нет ни в
/// справочнике действий, ни в правилах, ни в журнале.</para>
///
/// <para>СЕКРЕТ (токен) сюда приходит ЗНАЧЕНИЕМ из хранилища <c>secrets/</c> и уходит
/// только в заголовок запроса: ни в строку журнала (<see cref="Describe"/>), ни в
/// config.json, ни в тело JSON-RPC он не попадает.</para>
/// </summary>
public static class McpClient
{
    private static readonly ILogger Logger = Log.ForContext(typeof(McpClient));

    /// <summary>Версия протокола, которую мы объявляем в <c>initialize</c>.</summary>
    public const string ProtocolVersion = "2025-06-18";

    /// <summary>Сколько страниц <c>tools/list</c> разбирать: защита от сервера, который
    /// отдаёт курсор бесконечно.</summary>
    private const int MaxPages = 20;

    /// <summary>Сколько ждать ответа сервера MCP на один запрос.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// СНЯТЬ СПИСОК ИНСТРУМЕНТОВ С СЕРВЕРА: <c>initialize</c> → <c>notifications/initialized</c>
    /// → <c>tools/list</c> (со страницами по курсору). Транспорт выбирается по манифесту.
    /// </summary>
    public static async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(PluginConnection connection,
        string? secret = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsComplete)
        {
            throw new InvalidOperationException(Loc.T("msg.mcp.3", Describe(connection)));
        }
        Logger.Information("MCP: подключение {Connection}", Describe(connection));
        using var transport = Open(connection, secret);
        return await ListToolsAsync(transport, ct);
    }

    /// <summary>Тот же разговор поверх готового транспорта — им пользуются проверки
    /// (поддельный сервер MCP) и вызывающий, который держит соединение сам.</summary>
    public static async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(IMcpTransport transport,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var id = 0;
        // 1. рукопожатие: без него сервер вправе не отвечать ни на что
        var hello = "{\"protocolVersion\":\"" + ProtocolVersion + "\",\"capabilities\":{},"
                    + "\"clientInfo\":{\"name\":\"AI2P\",\"version\":\"" + AppInfo.Version + "\"}}";
        await CallAsync(transport, ++id, "initialize", hello, ct);
        await transport.SendAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", waitsAnswer: false, ct);

        // 2. список инструментов — страницами: у сервера с сотней инструментов он не влезает
        // в один ответ, и пропущенная страница означала бы молча незарегистрированный
        // инструмент, то есть запрет там, где человек ждёт работы
        var tools = new List<McpToolInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var args = cursor is { Length: > 0 }
                ? $$"""{"cursor":{{JsonSerializer.Serialize(cursor)}}}"""
                : "{}";
            using var answer = await CallAsync(transport, ++id, "tools/list", args, ct);
            cursor = ReadTools(answer.RootElement, tools, names);
            if (cursor is not { Length: > 0 })
            {
                break;
            }
        }
        return tools;
    }

    /// <summary>Транспорт по описанию подключения: процесс (stdio) либо адрес (http).</summary>
    public static IMcpTransport Open(PluginConnection connection, string? secret = null) =>
        connection.Transport == PluginConnection.Http
            ? new McpHttpTransport(connection.Url, connection.SecretHeader, secret)
            : new McpStdioTransport(connection.Command, connection.Args);

    /// <summary>
    /// СТРОКА ДЛЯ ЖУРНАЛА И ДЛЯ СООБЩЕНИЯ ОБ ОШИБКЕ: транспорт и то, куда мы звоним.
    /// Секрета в ней нет и быть не может — это единственное, что уходит в лог о подключении.
    /// </summary>
    public static string Describe(PluginConnection connection) =>
        connection.Transport == PluginConnection.Http
            ? PluginConnection.Http + " " + connection.Url
            : PluginConnection.Stdio + " " + connection.Command
              + (connection.Args.Count > 0 ? " " + string.Join(" ", connection.Args) : "");

    /// <summary>Один запрос JSON-RPC с разбором ответа; ошибка сервера — исключение.</summary>
    private static async Task<JsonDocument> CallAsync(IMcpTransport transport, int id,
        string method, string paramsJson, CancellationToken ct)
    {
        var request = $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""";
        var answer = await transport.SendAsync(request, waitsAnswer: true, ct);
        if (answer is not { Length: > 0 })
        {
            throw new InvalidOperationException(Loc.T("msg.mcp.4", method));
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(answer);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Loc.T("msg.mcp.5", method, ex.Message));
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("error", out var error))
        {
            var text = error.ValueKind == JsonValueKind.Object
                       && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? ""
                : error.ToString();
            doc.Dispose();
            throw new InvalidOperationException(Loc.T("msg.mcp.2", method, text));
        }
        return doc;
    }

    /// <summary>Разобрать страницу <c>tools/list</c>; возвращает курсор следующей страницы.</summary>
    private static string? ReadTools(JsonElement root, List<McpToolInfo> tools, HashSet<string> names)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (result.TryGetProperty("tools", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in array.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var name = JsonRead.Str(tool, "name");
                // инструмент без имени звать нечем — такой пропускаем молча, как негодный
                // элемент манифеста: один кривой инструмент не должен прятать остальные
                if (name.Length == 0 || !names.Add(name))
                {
                    continue;
                }
                tools.Add(new McpToolInfo(name, JsonRead.Str(tool, "title"),
                    JsonRead.Str(tool, "description")));
            }
        }
        return result.TryGetProperty("nextCursor", out var cursor)
               && cursor.ValueKind == JsonValueKind.String
            ? cursor.GetString()
            : null;
    }
}

/// <summary>
/// ТРАНСПОРТ MCP — как именно доставляется строка JSON-RPC. Интерфейс существует не ради
/// красоты: поддельный сервер MCP в проверках — это его третья реализация, и без него
/// снятие списка инструментов проверялось бы только запуском настоящей чужой программы.
/// </summary>
public interface IMcpTransport : IDisposable
{
    /// <summary>Отправить запрос и (если ответ ожидается) вернуть тело ответа.
    /// null — ответа нет: так отвечает уведомление.</summary>
    Task<string?> SendAsync(string requestJson, bool waitsAnswer, CancellationToken ct);
}

/// <summary>
/// ТРАНСПОРТ <c>stdio</c>: сервер MCP запускается ПРОЦЕССОМ на этом компьютере, разговор
/// идёт строками JSON в стандартный ввод и из стандартного вывода. Отсюда пер-серверность
/// подключения: программа стоит там, где стоит, а путь к ней лежит в config.json сервера.
/// </summary>
public sealed class McpStdioTransport : IMcpTransport
{
    private readonly Process _process;

    public McpStdioTransport(string command, IEnumerable<string>? args = null)
    {
        var info = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in args ?? [])
        {
            info.ArgumentList.Add(arg);
        }
        try
        {
            _process = Process.Start(info)
                       ?? throw new InvalidOperationException(Loc.T("msg.mcp.6", command));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new InvalidOperationException(Loc.T("msg.mcp.6", command + ": " + ex.Message));
        }
    }

    public async Task<string?> SendAsync(string requestJson, bool waitsAnswer, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(requestJson.ReplaceLineEndings(" "));
        await _process.StandardInput.FlushAsync(ct);
        if (!waitsAnswer)
        {
            return null;
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(McpClient.Timeout);
        while (!limit.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(limit.Token);
            if (line is null)
            {
                return null; // сервер закрыл вывод
            }
            // сервер вправе печатать в stdout и не-JSON (баннеры, предупреждения), а свои
            // уведомления присылать без «id» — ответом считается объект с result или error
            var text = line.Trim();
            if (text.StartsWith('{') && (text.Contains("\"result\"") || text.Contains("\"error\"")))
            {
                return text;
            }
        }
        return null;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(2000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException
                                       or System.ComponentModel.Win32Exception)
        {
            // процесс уже умер — гасить нечего
        }
        _process.Dispose();
    }
}

/// <summary>
/// ТРАНСПОРТ <c>http</c> (Streamable HTTP): сервер уже где-то работает, запрос уходит
/// POST-ом, ответ приходит либо телом JSON, либо потоком событий (<c>text/event-stream</c>) —
/// поэтому разбираются оба вида. Идентификатор сеанса (<c>Mcp-Session-Id</c>), если сервер
/// его выдал, возвращается ему в каждом следующем запросе.
///
/// СЕКРЕТ ставится ТОЛЬКО в заголовок, названный манифестом (<c>secretHeader</c>), и никуда
/// больше: ни в адрес, ни в тело, ни в журнал.
/// </summary>
public sealed class McpHttpTransport : IMcpTransport
{
    private readonly HttpClient _http;
    private readonly bool _own;
    private readonly string _url;
    private readonly string _header;
    private readonly string _secret;
    private string _session = "";

    public McpHttpTransport(string url, string? secretHeader = null, string? secret = null,
        HttpClient? http = null)
    {
        _url = (url ?? "").Trim();
        _header = (secretHeader ?? "").Trim();
        _secret = (secret ?? "").Trim();
        _own = http is null;
        _http = http ?? new HttpClient { Timeout = McpClient.Timeout };
    }

    public async Task<string?> SendAsync(string requestJson, bool waitsAnswer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpClient.ProtocolVersion);
        if (_session.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _session);
        }
        if (_header.Length > 0 && _secret.Length > 0)
        {
            // «Authorization: <токен>» без схемы сервер не примет, а писать слово Bearer в
            // сам секрет — значит хранить в файле ключа то, что ключом не является
            var value = _header.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                        && !_secret.Contains(' ')
                ? "Bearer " + _secret
                : _secret;
            request.Headers.TryAddWithoutValidation(_header, value);
        }
        using var response = await _http.SendAsync(request, ct);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
        {
            _session = ids.FirstOrDefault() ?? _session;
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.mcp.7", (int)response.StatusCode, Short(body)));
        }
        return waitsAnswer ? FromStream(body) : null;
    }

    /// <summary>Ответ потоком событий: нужное лежит в строке <c>data:</c>.</summary>
    private static string? FromStream(string body)
    {
        var text = body.Trim();
        if (text.Length == 0 || text.StartsWith('{'))
        {
            return text.Length == 0 ? null : text;
        }
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = s[5..].Trim();
                if (data.StartsWith('{'))
                {
                    return data;
                }
            }
        }
        return text;
    }

    private static string Short(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";

    public void Dispose()
    {
        if (_own)
        {
            _http.Dispose();
        }
    }
}
