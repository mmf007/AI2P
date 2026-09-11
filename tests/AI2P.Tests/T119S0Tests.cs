using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-119-S0 (ветка «Плагины и MCP»): КЛИЕНТ MCP — подключение сервера MCP записью плагина.
///
/// Ради чего этот набор написан. Список инструментов у сервера MCP ДИНАМИЧЕСКИЙ: сервер
/// вправе добавить инструмент в новой версии, и если такой инструмент доедет до агента,
/// не пройдя регистрацию в справочнике действий, он окажется вне правил безопасности
/// (наука 2d3af8da: инструмент без записи справочника правилами не закрывается вовсе).
/// Поэтому проверяется вся цепочка: сняли список с сервера → завели запись на КАЖДЫЙ
/// инструмент → и только теперь он публикуется; незарегистрированный не публикуется и
/// ЗАПРЕЩЁН обратной политикой; исчезнувший теряет запись; а секрет подключения не попадает
/// ни в config.json, ни в тело запроса, ни в строку журнала.
/// </summary>
public sealed class T119S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private PluginSetupService Setup => new(_f.Db, _f.Experience, _f.RefData);

    private static JsonElement NoArgs => JsonDocument.Parse("{}").RootElement.Clone();

    private const string Code = "mcp.unity";

    /// <summary>Манифест подключения MCP: списка действий у него нет и быть не может —
    /// инструменты называет сам сервер.</summary>
    private static PluginManifest Manifest(string transport = "stdio") =>
        PluginManifest.Parse($$"""
            {
              "code": "{{Code}}",
              "kind": "mcp",
              "name": "Unity MCP",
              "connection": {
                "transport": "{{transport}}",
                "command": "node", "args": ["server.js"],
                "url": "http://127.0.0.1:8765/mcp",
                "secretRef": "plugin.mcp.unity.token",
                "secretHeader": "Authorization"
              }
            }
            """)!;

    // ---------- 1. снятие списка инструментов с сервера MCP ----------

    [Fact]
    public async Task The_Tool_List_Is_Fetched_From_The_Server_With_A_Handshake()
    {
        var server = new FakeMcpServer();
        var tools = await McpClient.ListToolsAsync(server);

        // рукопожатие обязательно: без initialize сервер вправе не отвечать ни на что
        Assert.Equal(["initialize", "notifications/initialized", "tools/list", "tools/list"],
            server.Methods);

        // список приходит СТРАНИЦАМИ — пропущенная страница означала бы незарегистрированный
        // инструмент, то есть запрет там, где человек ждёт работы
        Assert.Equal(["unity_run_tests", "unity_menu_item", "unity_read_console"],
            tools.Select(t => t.Name));
        Assert.Equal("Прогнать тесты", tools[0].Title);
        Assert.Equal("Запускает Test Runner и отдаёт итог", tools[0].Description);
        // сервер не назвал заголовка — в справочнике встанет само имя инструмента
        Assert.Equal("unity_menu_item", tools[1].TitleOrName);
    }

    [Fact]
    public void A_Manifest_Of_Kind_Mcp_Carries_The_Connection_And_No_Actions()
    {
        var manifest = Manifest();
        Assert.Equal(PluginKinds.Mcp, manifest.Kind);
        Assert.Empty(manifest.Actions); // список действий у MCP вычищается разбором
        Assert.NotNull(manifest.Connection);
        Assert.True(manifest.Connection!.IsComplete);
        // ссылка на секрет — из манифеста; не названа — складывается по умолчанию
        Assert.Equal("plugin.mcp.unity.token",
            PluginCodes.SecretRef(Code, manifest.Connection.SecretRef));
        Assert.Equal("plugin.mcp.other.token", PluginCodes.SecretRef("mcp.other", ""));
        // ТОКЕНА В МАНИФЕСТЕ НЕТ И БЫТЬ НЕ МОЖЕТ: это файл дистрибутива, он одинаков у всех
        Assert.DoesNotContain("token\":", JsonSerializer.Serialize(manifest.Connection));
    }

    // ---------- 2. запись справочника на каждый инструмент ----------

    [Fact]
    public async Task Every_Tool_Of_The_Server_Gets_Its_Own_Custom_Catalog_Entry()
    {
        var server = new FakeMcpServer();
        var tools = await McpClient.ListToolsAsync(server);
        var names = tools.Select(t => t.Name).ToList();

        Assert.Equal(3, Setup.Initialize(Manifest(), names, "ru", tools));

        var actions = _f.Actions.List("ru");
        var run = actions.Single(a => a.ToolName == "unity_run_tests");
        // код складывается приставкой плагина: своего кода у инструмента MCP нет
        Assert.Equal("Plugin.mcp.unity.unity_run_tests", run.Code);
        Assert.True(run.IsCustom);
        // название и подсказка — из описания инструмента, как его назвал сам сервер
        Assert.Equal("Прогнать тесты", run.Title);
        // с этой минуты правила безопасности видят инструмент по коду
        Assert.Equal("Plugin.mcp.unity.unity_run_tests", _f.Actions.CodeByTool("unity_run_tests"));
        // сервер названия не дал — в справочнике имя инструмента, а не пустая строка
        Assert.Equal("unity_menu_item", actions.Single(a => a.ToolName == "unity_menu_item").Title);

        // идемпотентно: повторное обновление списка вторых экземпляров не заводит
        Assert.Equal(0, Setup.Initialize(Manifest(), names, "ru", tools));

        // и запись есть на ВСЕХ языках: справочник читают на языке команды задачи
        foreach (var lang in Loc.Languages)
        {
            Assert.Equal(3, _f.Actions.List(lang)
                .Count(a => a.ToolName?.StartsWith("unity_", StringComparison.Ordinal) == true));
        }
    }

    [Fact]
    public void A_Tool_Without_A_Catalog_Entry_Is_Not_Published_And_Is_Denied()
    {
        var manifest = Manifest();
        // на момент инициализации сервер отдавал один инструмент
        Assert.Equal(1, Setup.Initialize(manifest, ["unity_run_tests"], "ru",
            [new McpToolInfo("unity_run_tests", "Прогнать тесты")]));

        // а теперь в новой версии сервера появился второй — записи у него нет
        var now = new[] { "unity_run_tests", "unity_delete_project" };
        Assert.Equal(["unity_run_tests"], Setup.PublishedTools(manifest, now));
        Assert.Null(_f.Actions.CodeByTool("unity_delete_project"));

        var task = _f.Tasks.Create(new TaskItem { Title = "сборка" }, "", "", actorId: null);
        var tools = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            security: new SecurityEvaluator(_f.Security.EffectiveForTask(task), null),
            actionCodeByTool: _f.Actions.CodeByTool)
        {
            // набор считается по КЭШУ инструментов сервера, а не по справочнику: смысл
            // политики ровно в том, чтобы поймать имя, записи у которого ещё нет
            PluginTools = PluginSetupService.PluginToolNames([manifest],
                new Dictionary<string, IReadOnlyList<string>> { [Code] = now }),
        };

        // КОНТРОЛЬНЫЙ ОПЫТ: обычный инструмент без записи справочника по-прежнему РАЗРЕШЁН —
        // без него проверка ниже доказывала бы лишь то, что запрещено всё подряд
        Assert.Equal(SecurityDecision.Allow, tools.Authorize("no_such_tool", NoArgs).Decision);
        // а инструмент сервера MCP, не прошедший регистрацию, — запрещён
        Assert.Equal(SecurityDecision.Deny, tools.Authorize("unity_delete_project", NoArgs).Decision);
        // зарегистрированный работает по обычным правилам
        Assert.Equal(SecurityDecision.Allow, tools.Authorize("unity_run_tests", NoArgs).Decision);
    }

    // ---------- 3. исчезнувший инструмент ----------

    [Fact]
    public void A_Vanished_Tool_Loses_Its_Catalog_Entry()
    {
        var manifest = Manifest();
        var before = new[] { "unity_run_tests", "unity_menu_item" };
        Assert.Equal(2, Setup.Initialize(manifest, before));

        // сервер обновился и инструмент убрал — запись справочника снимается
        Assert.Equal(1, Setup.RemoveTools(Code, ["unity_menu_item"]));

        Assert.Null(_f.Actions.CodeByTool("unity_menu_item"));
        Assert.Equal(["unity_run_tests"], Setup.PublishedTools(manifest, before));
        Assert.Equal("Plugin.mcp.unity.unity_run_tests", _f.Actions.CodeByTool("unity_run_tests"));

        // снимать нечего — не падаем и ничего не трогаем
        Assert.Equal(0, Setup.RemoveTools(Code, []));
        Assert.Equal(0, Setup.RemoveTools(Code, ["unity_menu_item"]));
    }

    [Fact]
    public void Removing_The_Plugin_Takes_Away_The_Entries_Of_Its_Mcp_Tools()
    {
        var manifest = Manifest();
        var tools = new[] { "unity_run_tests", "unity_menu_item" };
        Setup.Initialize(manifest, tools);

        // имена инструментов манифест не называет — снятию их подаёт кэш этого сервера
        Setup.Remove(manifest, tools);

        Assert.Empty(Setup.RegisteredTools(manifest, tools));
        Assert.Null(_f.Actions.CodeByTool("unity_run_tests"));
    }

    // ---------- 4. секрет ----------

    [Fact]
    public async Task The_Secret_Goes_Only_Into_The_Header_And_Never_Into_Config_Or_The_Log()
    {
        const string token = "s3cret-token-of-the-mcp-server";
        var dir = Path.Combine(_f.Dir, "mcp-secret");
        Directory.CreateDirectory(dir);

        // 1. секрет живёт в подкаталоге secrets, как пароль почты (T-272)
        var store = new SecretStore(Path.Combine(dir, "secrets.json"));
        var secretRef = PluginCodes.SecretRef(Code, "");
        store.Write(secretRef, token);
        Assert.Equal(token, store.Read(secretRef));

        // 2. в config.json его нет: там лежит только кэш имён инструментов
        var configPath = Path.Combine(dir, "config.json");
        var config = new Ai2pConfig();
        config.Plugins[Code] = new Ai2pConfig.PluginSettings
        {
            Tools = ["unity_run_tests"],
            ToolsAt = DateTime.UtcNow,
        };
        config.Save(configPath);
        var configText = File.ReadAllText(configPath);
        Assert.DoesNotContain(token, configText);
        Assert.Contains("unity_run_tests", configText);

        // 3. в строке журнала подключения его тоже нет — она вся про то, куда мы звоним
        var connection = Manifest("http").Connection!;
        var described = McpClient.Describe(connection);
        Assert.DoesNotContain(token, described);
        Assert.Contains("http://127.0.0.1:8765/mcp", described);

        // 4. и в теле запроса его нет: секрет уходит ТОЛЬКО в заголовок манифеста
        var http = new RecordingHandler();
        using var transport = new McpHttpTransport(connection.Url, connection.SecretHeader,
            store.Read(secretRef), new HttpClient(http));
        var tools = await McpClient.ListToolsAsync(transport);

        Assert.NotEmpty(tools);
        Assert.All(http.Bodies, body => Assert.DoesNotContain(token, body));
        Assert.All(http.Bodies, body => Assert.DoesNotContain("Authorization", body));
        // «Authorization: <токен>» без схемы сервер не примет, а слово Bearer в файле ключа
        // хранить незачем — его дописывает транспорт
        Assert.Equal("Bearer " + token, http.Headers[0]);
    }

    [Fact]
    public async Task The_Http_Transport_Understands_An_Event_Stream_Answer()
    {
        // Streamable HTTP отвечает либо телом JSON, либо потоком событий — разбираются оба,
        // иначе половина серверов MCP выглядела бы как «не ответил»
        var http = new RecordingHandler { AsEventStream = true };
        using var transport = new McpHttpTransport("http://127.0.0.1:8765/mcp", "", "",
            new HttpClient(http));
        var tools = await McpClient.ListToolsAsync(transport);
        Assert.Equal(["unity_run_tests"], tools.Select(t => t.Name));
        // идентификатор сеанса, выданный сервером, возвращается ему в следующих запросах
        Assert.Contains("sess-1", http.Sessions.Skip(1));
    }

    [Fact]
    public async Task An_Error_From_The_Server_Is_A_Refusal_And_Not_An_Empty_List()
    {
        // молча вернуть пустой список нельзя: обновление сняло бы записи у ВСЕХ инструментов
        var server = new FakeMcpServer { Error = "tools are not available" };
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => McpClient.ListToolsAsync(server));
        Assert.Contains("tools are not available", refused.Message);
    }

    [Fact]
    public async Task An_Incomplete_Connection_Is_Refused_Before_Any_Process_Is_Started()
    {
        var manifest = PluginManifest.Parse("""
            { "code": "mcp.empty", "kind": "mcp", "connection": { "transport": "stdio" } }
            """)!;
        Assert.False(manifest.Connection!.IsComplete);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => McpClient.ListToolsAsync(manifest.Connection));
    }

    // ---------- поддельный сервер MCP ----------

    /// <summary>
    /// ПОДДЕЛЬНЫЙ СЕРВЕР MCP поверх транспорта: отвечает на рукопожатие и отдаёт список
    /// инструментов ДВУМЯ страницами (курсор) — ровно то, на чём ломается наивный клиент.
    /// </summary>
    private sealed class FakeMcpServer : IMcpTransport
    {
        public List<string> Methods { get; } = [];

        /// <summary>Сервер отказывает на tools/list этим сообщением.</summary>
        public string Error { get; init; } = "";

        public Task<string?> SendAsync(string requestJson, bool waitsAnswer, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(requestJson);
            var method = doc.RootElement.GetProperty("method").GetString() ?? "";
            Methods.Add(method);
            var id = doc.RootElement.TryGetProperty("id", out var v) ? v.GetInt32() : 0;
            if (!waitsAnswer)
            {
                return Task.FromResult<string?>(null);
            }
            if (method == "initialize")
            {
                return Task.FromResult<string?>("{\"jsonrpc\":\"2.0\",\"id\":" + id
                    + ",\"result\":{\"protocolVersion\":\"2025-06-18\"}}");
            }
            if (Error.Length > 0)
            {
                return Task.FromResult<string?>("{\"jsonrpc\":\"2.0\",\"id\":" + id
                    + ",\"error\":{\"code\":-32000,\"message\":\"" + Error + "\"}}");
            }
            var second = doc.RootElement.GetProperty("params").TryGetProperty("cursor", out _);
            var page = second
                ? """{"tools":[{"name":"unity_read_console","description":"Читает консоль"}]}"""
                : """
                  {"tools":[
                    {"name":"unity_run_tests","title":"Прогнать тесты",
                     "description":"Запускает Test Runner и отдаёт итог"},
                    {"name":"unity_menu_item","description":"Жмёт пункт меню"},
                    {"name":"","description":"безымянный — пропускается"}],
                   "nextCursor":"page2"}
                  """;
            return Task.FromResult<string?>(
                $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{page}}}""");
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Сервер MCP по HTTP: записывает тела и заголовки запросов — по ним и видно,
    /// что секрет уходит только в заголовок.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Headers { get; } = [];
        public List<string> Sessions { get; } = [];
        public bool AsEventStream { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            Headers.Add(request.Headers.TryGetValues("Authorization", out var auth)
                ? auth.First()
                : "");
            Sessions.Add(request.Headers.TryGetValues("Mcp-Session-Id", out var s) ? s.First() : "");
            var method = body.Contains("\"tools/list\"") ? "tools/list" : "initialize";
            var payload = method == "tools/list"
                ? """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"unity_run_tests"}]}}"""
                : """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18"}}""";
            var answer = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = AsEventStream
                    ? new StringContent("event: message\ndata: " + payload + "\n\n", Encoding.UTF8,
                        "text/event-stream")
                    : new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            answer.Headers.TryAddWithoutValidation("Mcp-Session-Id", "sess-1");
            return answer;
        }
    }
}
