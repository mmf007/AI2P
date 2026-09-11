using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-255: ДОСТУП АГЕНТА К ВНЕШНИМ ФАЙЛАМ ЗАДАНИЯ.
///
/// Картинка задачи, приехавшей импортом, лежала для агента в двух недосягаемых местах сразу:
/// ссылкой в интернет (за авторизацией внешней системы) и файлом хранилища AI2P
/// (projects/&lt;код&gt;/uploads/…), которого нет в каталоге проекта. На прямой вопрос
/// «видишь ли ты приложенный скриншот» агент отвечал «не вижу» — и был прав.
///
/// Теперь есть действие fetch_file (у CLI-агента — маркер AI2P_GET_FILE): по ссылке файл
/// кладётся в кэш агента и возвращается путь на диске. Порядок: кэш → хранилище AI2P →
/// интернет (с реквизитами активного источника импорта той же системы).
/// </summary>
public sealed class T255Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string Marker = ClaudeCliConnector.FileMarker;

    /// <summary>Картинка вместо живого интернета: тела по порядку обращений; помнит,
    /// сколько раз и с какими заголовками к ней ходили.</summary>
    private sealed class FakeWeb(params (HttpStatusCode Status, byte[] Body, string ContentType)[] responses)
        : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = [];

        public List<string> AuthHeaders { get; } = [];

        /// <summary>Заявленная длина ответа; null — как есть (по длине тела).</summary>
        public long? DeclaredLength { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            AuthHeaders.Add(request.Headers.TryGetValues("Authorization", out var auth)
                ? string.Join(";", auth)
                : request.Headers.TryGetValues("PRIVATE-TOKEN", out var priv)
                    ? "PRIVATE-TOKEN " + string.Join(";", priv)
                    : "");
            var (status, body, contentType) = responses[Math.Min(_next++, responses.Length - 1)];
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            if (DeclaredLength is { } declared)
            {
                content.Headers.ContentLength = declared;
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3, 4, 5];

    private FileFetchService Fetch(FakeWeb? web = null) =>
        new(_f.Files, _f.Imports, _f.Projects, _f.Secrets)
        {
            HttpOverride = web is null ? null : new HttpClient(web),
            KeyResolver = secretRef => (secretRef == "k" ? "КЛЮЧ" : "ТОКЕН", "тест"),
        };

    /// <summary>Набор инструментов текущей задачи; коды действий — из реального справочника.</summary>
    private AgentToolset Toolset(TaskItem current, FileFetchService? fetch,
        List<SecurityRule>? rules = null) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, null, fetch: fetch),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: _f.Actions.CodeByTool);

    private TaskItem NewTask(Project project) =>
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача с картинкой" },
            "описание", "", null);

    private static JsonElement Args(string url) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { url })).RootElement.Clone();

    // ---------- 1. интернет и кэш ----------

    [Fact]
    public async Task External_Image_Is_Downloaded_Into_The_Cache()
    {
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"));
        var fetch = Fetch(web);

        var file = await fetch.FetchAsync("https://example.org/pics/shot.png");

        Assert.Equal(FileFetchService.FetchOrigin.Network, file.Origin);
        Assert.Equal("shot.png", file.Name);
        Assert.Equal(Png.Length, file.Bytes);
        Assert.Equal("image/png", file.ContentType);
        Assert.True(File.Exists(file.Path));
        Assert.Equal(Png, await File.ReadAllBytesAsync(file.Path));
        // файл лежит именно в кэше агента — этот каталог открывается CLI флагом --add-dir
        Assert.StartsWith(fetch.CacheDir, file.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Single(web.Urls);
    }

    [Fact]
    public async Task Second_Fetch_Of_The_Same_Link_Comes_From_The_Cache()
    {
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"));
        var fetch = Fetch(web);
        const string url = "https://example.org/pics/shot.png";

        var first = await fetch.FetchAsync(url);
        var second = await fetch.FetchAsync(url);

        Assert.Equal(FileFetchService.FetchOrigin.Cache, second.Origin);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal("shot.png", second.Name);
        Assert.Equal(Png.Length, second.Bytes);
        // главное: в сеть второй раз не ходили
        Assert.Single(web.Urls);
    }

    [Fact]
    public async Task Name_Without_Extension_Is_Completed_By_Content_Type()
    {
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/webp"));

        var file = await Fetch(web).FetchAsync("https://trello.com/1/cards/abc/attachments/def/download");

        Assert.Equal("download.webp", file.Name);
        Assert.EndsWith(".webp", file.Path);
    }

    // ---------- 2. файл хранилища AI2P (та самая ссылка из описания) ----------

    [Fact]
    public async Task Storage_Link_Of_An_Imported_Attachment_Is_Taken_Without_Network()
    {
        var project = _f.Projects.Create("Картинки", null, null, null);
        // именно так вложение карточки попадает в описание задачи при импорте
        var rel = _f.Files.SaveUpload(project.Slug, "image.png", Png);
        var link = "api/files/raw?path=" + Uri.EscapeDataString(rel);
        var web = new FakeWeb((HttpStatusCode.OK, [], "image/png"));
        var fetch = Fetch(web);

        var file = await fetch.FetchAsync(link);

        Assert.Equal(FileFetchService.FetchOrigin.Storage, file.Origin);
        Assert.Equal(Png, await File.ReadAllBytesAsync(file.Path));
        Assert.StartsWith(fetch.CacheDir, file.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(web.Urls);
    }

    [Fact]
    public async Task Bare_Storage_Path_Works_The_Same_Way()
    {
        var project = _f.Projects.Create("Картинки", null, null, null);
        var rel = _f.Files.SaveUpload(project.Slug, "image.png", Png);

        var file = await Fetch().FetchAsync(rel.Replace('\\', '/'));

        Assert.Equal(FileFetchService.FetchOrigin.Storage, file.Origin);
        Assert.Equal(Png, await File.ReadAllBytesAsync(file.Path));
    }

    [Fact]
    public async Task Full_External_Url_Of_Our_Own_File_Is_Recognized_As_Ours()
    {
        var project = _f.Projects.Create("Картинки", null, null, null);
        var rel = _f.Files.SaveUpload(project.Slug, "image.png", Png);
        // ссылка, какой её выдаёт само приложение (внешнее имя сервера)
        var url = "http://mypc:5480/ai2p/mmfgrp/api/files/raw?path=" + Uri.EscapeDataString(rel);
        var web = new FakeWeb((HttpStatusCode.OK, [], "image/png"));

        var file = await Fetch(web).FetchAsync(url);

        Assert.Equal(FileFetchService.FetchOrigin.Storage, file.Origin);
        // за своим файлом в сеть не ходим: там всё равно cookie-вход
        Assert.Empty(web.Urls);
    }

    [Fact]
    public async Task Missing_Storage_File_Is_Reported_Plainly()
    {
        var link = "api/files/raw?path=" + Uri.EscapeDataString("projects/PRJ-9/uploads/нет.png");

        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => Fetch().FetchAsync(link));

        Assert.Contains("нет.png", error.Message);
    }

    [Fact]
    public async Task Storage_Copy_Is_Refreshed_When_The_Source_Changes()
    {
        // задачу обновили из источника — картинка в uploads/ стала другой, и агенту
        // нельзя отдавать вчерашнюю копию из кэша
        var project = _f.Projects.Create("Обновление", null, null, null);
        var rel = _f.Files.SaveUpload(project.Slug, "image.png", Png);
        var link = "api/files/raw?path=" + Uri.EscapeDataString(rel);
        var fetch = Fetch();
        await fetch.FetchAsync(link);

        byte[] fresh = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        await File.WriteAllBytesAsync(_f.Files.Abs(rel), fresh);
        var again = await fetch.FetchAsync(link);

        Assert.Equal(FileFetchService.FetchOrigin.Storage, again.Origin);
        Assert.Equal(fresh, await File.ReadAllBytesAsync(again.Path));
    }

    // ---------- 3. отказы ----------

    [Fact]
    public async Task Only_Project_Files_Of_The_Storage_Are_Given_Out()
    {
        // база организации лежит в том же каталоге данных, что и вложения задач,
        // и её нельзя вынести одной строкой маркера
        await File.WriteAllTextAsync(Path.Combine(_f.Files.DataDir, "ai2p.db"), "секрет");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch().FetchAsync("api/files/raw?path=ai2p.db"));

        Assert.Contains("ai2p.db", error.Message);
    }

    [Fact]
    public async Task Escaping_The_Data_Directory_Is_Refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch().FetchAsync("api/files/raw?path=" + Uri.EscapeDataString("../../secrets.json")));

        Assert.Contains("secrets.json", error.Message);
    }

    [Fact]
    public async Task Unsupported_Link_Is_Refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch().FetchAsync("file:///C:/secrets/passwords.txt"));

        Assert.Contains("http", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_Failure_Names_The_Status_And_Whether_We_Were_Authorized()
    {
        var web = new FakeWeb((HttpStatusCode.Unauthorized, [], "text/plain"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch(web).FetchAsync("https://trello.com/1/cards/a/attachments/b/download/x.png"));

        Assert.Contains("401", error.Message);
        // источника Trello в этой организации нет — так и сказано, иначе непонятно,
        // почему картинка «за авторизацией» не пришла
        Assert.Contains("без авторизации", error.Message);
    }

    [Fact]
    public async Task Too_Large_File_Is_Refused_And_Not_Cached()
    {
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"))
        {
            DeclaredLength = FileFetchService.MaxBytes + 1,
        };
        var fetch = Fetch(web);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fetch.FetchAsync("https://example.org/huge.png"));

        Assert.False(Directory.Exists(fetch.CacheDir)
                     && Directory.EnumerateFiles(fetch.CacheDir).Any());
    }

    // ---------- 4. реквизиты внешних систем ----------

    [Fact]
    public async Task Trello_Attachment_Is_Requested_With_The_Source_Credentials()
    {
        _f.Imports.Create(new ImportSource
        {
            Name = "Trello",
            Kind = "trello",
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(
                new { login = "mmffr", filter = "", keyRef = "k", tokenRef = "t" }),
        }, null);
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/webp"));

        await Fetch(web).FetchAsync(
            "https://trello.com/1/cards/abc/attachments/def/download/image.webp");

        // download-эндпоинт Trello принимает только OAuth-заголовок (как в импорте вложений)
        Assert.Contains("OAuth", web.AuthHeaders[0]);
        Assert.Contains("КЛЮЧ", web.AuthHeaders[0]);
        Assert.Contains("ТОКЕН", web.AuthHeaders[0]);
    }

    [Fact]
    public async Task Gitlab_Attachment_Is_Requested_With_Private_Token_Of_Its_Own_Server()
    {
        _f.Imports.Create(new ImportSource
        {
            Name = "GitLab",
            Kind = GitLabImporter.Kind,
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(
                new { host = "https://git.example.org", project = "g/p", tokenRef = "t" }),
        }, null);
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"));

        await Fetch(web).FetchAsync("https://git.example.org/uploads/hash/shot.png");

        Assert.StartsWith("PRIVATE-TOKEN", web.AuthHeaders[0]);
        Assert.Contains("ТОКЕН", web.AuthHeaders[0]);
    }

    [Fact]
    public async Task Foreign_Host_Is_Requested_Without_Any_Credentials()
    {
        _f.Imports.Create(new ImportSource
        {
            Name = "Trello",
            Kind = "trello",
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(
                new { login = "mmffr", filter = "", keyRef = "k", tokenRef = "t" }),
        }, null);
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"));

        await Fetch(web).FetchAsync("https://example.org/shot.png");

        // токен Trello не уезжает на чужой сайт (правило владения секретами, T-249)
        Assert.Equal("", web.AuthHeaders[0]);
    }

    // ---------- 5. инструмент агента ----------

    [Fact]
    public async Task Tool_Answers_With_The_Path_On_Disk()
    {
        var project = _f.Projects.Create("Инструмент", null, null, null);
        var web = new FakeWeb((HttpStatusCode.OK, Png, "image/png"));
        var fetch = Fetch(web);
        var tools = Toolset(NewTask(project), fetch);

        var answer = await tools.ExecuteAsync("fetch_file", Args("https://example.org/shot.png"),
            CancellationToken.None);

        Assert.Contains(fetch.CacheDir, answer);
        Assert.Contains("shot.png", answer);
        Assert.Contains("image/png", answer);
    }

    [Fact]
    public void Tool_Is_Published_Only_When_Fetching_Is_Wired()
    {
        var project = _f.Projects.Create("Публикация", null, null, null);
        var task = NewTask(project);

        Assert.Contains(Toolset(task, Fetch()).Specs, s => s.Name == "fetch_file");
        Assert.DoesNotContain(Toolset(task, null).Specs, s => s.Name == "fetch_file");
    }

    [Fact]
    public void Tool_Has_An_Action_Code_So_Security_Rules_Can_Close_It()
    {
        // без записи справочника действий правило на инструмент не действует вовсе
        // (CodeByTool → null, Authorize разрешает) — опыт выпуска 1.80
        Assert.Equal("AI2P.Files.Fetch", _f.Actions.CodeByTool("fetch_file"));
    }

    [Fact]
    public void Deny_Rule_Closes_Fetching_And_Removes_The_Marker_Note()
    {
        var project = _f.Projects.Create("Запрет", null, null, null);
        var task = NewTask(project);
        List<SecurityRule> rules =
        [
            new()
            {
                Target = SecurityTarget.Action,
                Permission = SecurityPermission.Deny,
                Pattern = "AI2P.Files.Fetch",
            },
        ];
        var tools = Toolset(task, Fetch(), rules);

        var check = tools.Authorize("fetch_file", Args("https://example.org/shot.png"));

        Assert.Equal(SecurityDecision.Deny, check.Decision);
        // про маркер закрытого действия агенту не рассказываем вовсе (правило T-125)
        Assert.Equal("", tools.CliFetchNote(Marker, ClaudeCliConnector.MaxFileFetches));
        Assert.Null(tools.FetchDir);
    }

    // ---------- 6. маркер CLI-агента ----------

    [Fact]
    public void File_Markers_Are_Parsed_And_Cut_Out_Of_Answer()
    {
        var text = "Нужна картинка.\n"
                   + Marker + " {\"url\": \"https://trello.com/1/cards/a/attachments/b/download/x.png\"}\n"
                   + "Жду.";

        var markers = ClaudeCliConnector.ParseMarkers(Marker, text, out var cleaned);

        Assert.Single(markers);
        Assert.Contains("x.png", markers[0]);
        Assert.DoesNotContain(Marker, cleaned);
        Assert.EndsWith("Жду.", cleaned);
    }

    [Fact]
    public void File_Marker_Inside_Line_Is_Not_A_Request()
    {
        // рассказ О САМОЙ возможности (в том числе в отчёте про неё) запросом не становится
        var text = "Файл берётся строкой " + Marker + " {\"url\": \"https://x/y.png\"} — вот так.";

        Assert.Empty(ClaudeCliConnector.ParseMarkers(Marker, text, out var cleaned));
        Assert.Equal(text, cleaned);
    }

    [Fact]
    public async Task Marker_Answer_Holds_The_Path_The_Agent_Can_Read()
    {
        var project = _f.Projects.Create("Маркер", null, null, null);
        var rel = _f.Files.SaveUpload(project.Slug, "shot.png", Png);
        var fetch = Fetch();
        var tools = Toolset(NewTask(project), fetch);
        var markers = ClaudeCliConnector.ParseMarkers(Marker,
            Marker + " {\"url\": \"api/files/raw?path=" + Uri.EscapeDataString(rel) + "\"}\n",
            out _);

        var reply = await ClaudeCliConnector.FetchFilesAsync(markers, tools, CancellationToken.None);

        Assert.Contains(fetch.CacheDir, reply);
        Assert.Contains("shot.png", reply);
        var path = reply.Split('\n').First(l => l.Contains(fetch.CacheDir, StringComparison.Ordinal));
        Assert.True(File.Exists(path[(path.IndexOf(fetch.CacheDir, StringComparison.Ordinal))..].Trim()));
    }

    [Fact]
    public async Task Marker_Without_Url_Is_Answered_With_A_Hint()
    {
        var project = _f.Projects.Create("Маркер без url", null, null, null);
        var tools = Toolset(NewTask(project), Fetch());

        var reply = await ClaudeCliConnector.FetchFilesAsync(["{\"path\": \"x\"}"], tools,
            CancellationToken.None);

        Assert.Contains("url", reply);
    }

    [Fact]
    public async Task Broken_Link_Does_Not_Break_The_Job()
    {
        var project = _f.Projects.Create("Плохая ссылка", null, null, null);
        var web = new FakeWeb((HttpStatusCode.NotFound, [], "text/plain"));
        var tools = Toolset(NewTask(project), Fetch(web));

        var reply = await ClaudeCliConnector.FetchFilesAsync(
            ["{\"url\": \"https://example.org/нет.png\"}"], tools, CancellationToken.None);

        // причина уходит агенту текстом, а не исключением задания
        Assert.Contains("404", reply);
    }

    [Fact]
    public void Cli_Prompt_Tells_About_The_Marker_And_The_Cache_Directory()
    {
        var project = _f.Projects.Create("Промпт", null, null, null);
        var fetch = Fetch();
        var tools = Toolset(NewTask(project), fetch);

        var note = tools.CliFetchNote(Marker, ClaudeCliConnector.MaxFileFetches);

        Assert.Contains(Marker, note);
        Assert.Contains(fetch.CacheDir, note);
        Assert.Equal(fetch.CacheDir, tools.FetchDir);
    }
}
