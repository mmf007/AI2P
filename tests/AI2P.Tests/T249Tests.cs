using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-249: ИМПОРТ ЗАДАЧ ИЗ GITLAB.
///
/// Второй вид источника справочника импортов после Trello. Из задачи GitLab (issue)
/// в задачу AI2P едут: САМА СТАТЬЯ (текст issue) в описание вместе с приложенными файлами,
/// ОБСУЖДЕНИЕ в чат задачи, заголовок и срок. Плюс процедура ОБНОВЛЕНИЯ задачи из источника
/// по образцу T-246: кнопка «обновить» перечитывает issue по ссылке импорта, хранящейся
/// в самой задаче.
///
/// Что отличает GitLab от Trello и потому проверяется отдельно:
/// 1) у него ОДНО значение секрета — личный токен, и он уезжает ЗАГОЛОВКОМ, а не в адресе;
/// 2) сервер GitLab бывает не только gitlab.com, поэтому он входит во внешний ключ задачи;
/// 3) списка вложений у issue нет — файлы живут ссылками внутри текста статьи.
/// </summary>
public sealed class T249Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ------------------------------------------------------------ заглушка GitLab

    /// <summary>Ответ вместо живого GitLab: тело подбирается по КУСКУ адреса (приём T134Tests,
    /// но по образцу адреса — у GitLab за один импорт идёт несколько разных запросов).
    /// Заодно запоминает адреса и заголовок PRIVATE-TOKEN каждого обращения.</summary>
    private sealed class FakeGitLab : HttpMessageHandler
    {
        private readonly List<(string Match, HttpStatusCode Status, string Body, byte[]? Bytes)> _routes = [];

        public List<string> Urls { get; } = [];

        public List<string> Tokens { get; } = [];

        public FakeGitLab Route(string match, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((match, status, body, null));
            return this;
        }

        public FakeGitLab RouteBytes(string match, byte[] bytes)
        {
            _routes.Add((match, HttpStatusCode.OK, "", bytes));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            Tokens.Add(request.Headers.TryGetValues("PRIVATE-TOKEN", out var v)
                ? string.Join(",", v)
                : "");
            foreach (var (match, status, body, bytes) in _routes)
            {
                if (!url.Contains(match, StringComparison.Ordinal))
                {
                    continue;
                }
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = bytes is not null
                        ? new ByteArrayContent(bytes)
                        : new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"404 Not found\"}", Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private GitLabImporter Importer(FakeGitLab? handler = null) =>
        new(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files, _f.Chat)
        {
            HttpOverride = handler is null ? null : new HttpClient(handler),
            KeyResolver = _ => ("glpat-XXXXXXXXXXXXXXXXXXXX", "тест"),
        };

    private ImportSource GitLabSource(string project = "group/proj", string host = "",
        string labels = "") =>
        _f.Imports.Create(new ImportSource
        {
            Name = "GitLab T-249 " + Guid.NewGuid().ToString("N")[..6],
            Kind = "gitlab",
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(
                new { host, project, labels, tokenRef = "gl.token" }),
        }, null);

    /// <summary>Тело одной issue в том виде, в каком его отдаёт GitLab.</summary>
    private static string IssueJson(long iid = 42, string title = "Кнопка не нажимается",
        string body = "Статья задачи", string due = "2026-09-01", long projectId = 77,
        string path = "group/proj") =>
        JsonSerializer.Serialize(new
        {
            id = 1000 + iid,
            iid,
            project_id = projectId,
            title,
            description = body,
            due_date = due,
            web_url = $"https://gitlab.com/{path}/-/issues/{iid}",
        });

    private static string NotesJson(params (long Id, string Author, string Text, string Date)[] notes) =>
        JsonSerializer.Serialize(notes.Select(n => new
        {
            id = n.Id,
            body = n.Text,
            system = false,
            created_at = n.Date,
            author = new { username = n.Author, name = n.Author },
        }));

    // ---------------------------------------------------- 1. разбор ссылки на issue

    [Theory]
    [InlineData("https://gitlab.com/group/proj/-/issues/42", "https://gitlab.com", "group/proj", 42)]
    [InlineData("https://gitlab.com/group/sub/proj/-/issues/7", "https://gitlab.com", "group/sub/proj", 7)]
    // старый вид адреса — без разделителя «/-/»
    [InlineData("https://gitlab.com/group/proj/issues/9", "https://gitlab.com", "group/proj", 9)]
    // хвосты: якорь на заметку и строка запроса
    [InlineData("https://gitlab.com/group/proj/-/issues/42#note_12345", "https://gitlab.com", "group/proj", 42)]
    [InlineData("https://gitlab.com/g/p/-/issues/3?work_item_iid=3", "https://gitlab.com", "g/p", 3)]
    // свой сервер GitLab, в том числе с портом
    [InlineData("https://git.example.com/team/app/-/issues/1", "https://git.example.com", "team/app", 1)]
    [InlineData("http://192.168.0.5:8929/team/app/-/issues/5", "http://192.168.0.5:8929", "team/app", 5)]
    public void Issue_url_is_parsed(string url, string host, string path, long iid)
    {
        var parsed = GitLabImporter.ParseIssueUrl(url);
        Assert.NotNull(parsed);
        Assert.Equal(host, parsed!.Host);
        Assert.Equal(path, parsed.ProjectPath);
        Assert.Equal(iid, parsed.Iid);
        Assert.True(GitLabImporter.CanRefresh(url));
    }

    [Theory]
    [InlineData("https://trello.com/c/abc123")]                       // чужой источник
    [InlineData("https://gitlab.com/group/proj/-/merge_requests/42")] // не issue
    [InlineData("https://gitlab.com/group/proj/-/boards")]
    [InlineData("https://gitlab.com/group/proj/-/issues")]            // список, а не задача
    [InlineData("https://gitlab.com/proj/-/issues/1")]                // одного сегмента мало
    // ссылка на issue GitHub (T-247) отличается от СТАРОГО вида адреса GitLab только
    // именем сервера: без разделителя «/-/» её нельзя забирать себе
    [InlineData("https://github.com/owner/repo/issues/7")]
    [InlineData("https://git.example.com/team/app/issues/5")]
    [InlineData("")]
    [InlineData(null)]
    public void Foreign_url_is_not_a_gitlab_issue(string? url)
    {
        Assert.Null(GitLabImporter.ParseIssueUrl(url));
        Assert.False(GitLabImporter.CanRefresh(url));
    }

    [Fact]
    public void External_ref_names_the_server()
    {
        // задача №42 своего GitLab и задача №42 облачного — РАЗНЫЕ задачи, и в одном
        // проекте AI2P они не должны слиться в одну
        var cloud = GitLabImporter.ExternalRefOf("https://gitlab.com", "77", 42);
        var own = GitLabImporter.ExternalRefOf("https://git.example.com", "77", 42);
        Assert.Equal("gitlab:gitlab.com:77:42", cloud);
        Assert.NotEqual(cloud, own);
    }

    // ------------------------------------------------------------- 2. разбор ответов

    [Fact]
    public void Issue_json_is_parsed()
    {
        var issue = GitLabImporter.ParseIssue(IssueJson(), "group/proj");
        Assert.Equal(42, issue.Iid);
        Assert.Equal("77", issue.ProjectId);
        Assert.Equal("Кнопка не нажимается", issue.Title);
        Assert.Equal("Статья задачи", issue.Body);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), issue.Due);
        Assert.Equal("https://gitlab.com/group/proj/-/issues/42", issue.WebUrl);
    }

    [Fact]
    public void Issue_list_is_parsed_and_broken_rows_are_skipped()
    {
        var json = "[" + IssueJson(1, "Первая") + "," + IssueJson(2, "Вторая")
                   + ",{\"iid\":0,\"title\":\"\"}]";
        var issues = GitLabImporter.ParseIssues(json, "group/proj");
        Assert.Equal(2, issues.Count);
        Assert.Equal(["Первая", "Вторая"], issues.Select(i => i.Title));
    }

    [Fact]
    public void System_notes_are_not_a_conversation()
    {
        // «изменил метку» и «назначил» GitLab отдаёт такими же заметками, как комментарии
        var json = """
            [
              {"id":1,"body":"Первый комментарий","system":false,"created_at":"2026-08-01T10:00:00Z",
               "author":{"username":"ivan"}},
              {"id":2,"body":"changed the description","system":true,"created_at":"2026-08-01T10:05:00Z",
               "author":{"username":"ivan"}},
              {"id":3,"body":"Второй","system":false,"created_at":"2026-08-01T11:00:00Z",
               "author":{"name":"Пётр"}}
            ]
            """;
        var (notes, total) = GitLabImporter.ParseNotes(json);
        // total считает ВСЕ заметки: по нему видно, есть ли следующая страница
        Assert.Equal(3, total);
        Assert.Equal(2, notes.Count);
        Assert.Equal("ivan", notes[0].Author);
        // логина нет — берётся полное имя
        Assert.Equal("Пётр", notes[1].Author);
    }

    [Fact]
    public void Chat_messages_are_marked_as_gitlab()
    {
        var notes = new List<GitLabImporter.IssueNote>
        {
            new("7", "ivan", "поздний", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)),
            new("5", "", "ранний", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var chat = GitLabImporter.ChatOf(notes);
        // порядок — хронологический, а не тот, в каком заметки пришли
        Assert.Equal(["ранний", "поздний"], chat.Select(m => m.Text));
        // автора не узнали — сообщение всё равно принадлежит GitLab, и это видно
        Assert.Equal("gitlab:?", chat[0].Author);
        Assert.Equal("gitlab:ivan", chat[1].Author);
        Assert.Equal("gitlab:note:5", chat[0].ExternalRef);
    }

    [Fact]
    public void Description_is_the_article_plus_the_source_line()
    {
        var issue = GitLabImporter.ParseIssue(IssueJson(body: "Текст статьи"), "group/proj");
        var description = GitLabImporter.DescriptionOf(issue);
        Assert.StartsWith("Текст статьи", description);
        Assert.Contains("group/proj", description);
        Assert.Contains("https://gitlab.com/group/proj/-/issues/42", description);

        // пустая статья — остаётся одна приписка-источник, без пустых разделителей
        var empty = GitLabImporter.DescriptionOf(
            GitLabImporter.ParseIssue(IssueJson(body: ""), "group/proj"));
        Assert.DoesNotContain("---", empty);
    }

    // ------------------------------------------------- 3. приложенные файлы в тексте

    [Fact]
    public void Attachment_links_are_found_in_the_article()
    {
        const string body = """
            Вот картинка:

            ![экран](/uploads/aaa111/screen.png)

            и файл [отчёт.pdf](/uploads/bbb222/%D0%BE%D1%82%D1%87%D1%91%D1%82.pdf),
            а это обычная ссылка [сайт](https://example.com/page).
            """;
        var links = GitLabImporter.ParseAttachmentLinks(body);
        Assert.Equal(2, links.Count);
        Assert.True(links[0].IsImage);
        Assert.False(links[1].IsImage);
        Assert.Equal("/uploads/aaa111/screen.png", links[0].Url);
        // имя файла берётся из адреса и раскодируется
        Assert.Equal("screen.png", GitLabImporter.UploadFileName(links[0].Url, links[0].Text));
        Assert.Equal("отчёт.pdf", GitLabImporter.UploadFileName(links[1].Url, links[1].Text));
    }

    [Fact]
    public void Relative_upload_link_is_counted_from_the_project()
    {
        // «/uploads/…» у GitLab отсчитывается ОТ ПРОЕКТА, а не от корня сервера
        Assert.Equal("https://git.example.com/group/proj/uploads/h/f.png",
            GitLabImporter.AbsoluteUploadUrl("https://git.example.com", "group/proj",
                "/uploads/h/f.png"));
        // абсолютный адрес остаётся как есть
        Assert.Equal("https://cdn.example.com/uploads/h/f.png",
            GitLabImporter.AbsoluteUploadUrl("https://gitlab.com", "group/proj",
                "https://cdn.example.com/uploads/h/f.png"));
    }

    // --------------------------------------------- 4. импорт одиночной issue по URL

    [Fact]
    public async Task Single_issue_is_imported_with_article_files_and_discussion()
    {
        var project = _f.Projects.Create("Импорт GitLab", null, null, null);
        GitLabSource();
        var body = "Статья.\n\n![экран](/uploads/aaa111/screen.png)";
        var fake = new FakeGitLab()
            .Route("/issues/42/notes", NotesJson(
                (5, "ivan", "первое", "2026-08-01T10:00:00Z"),
                (6, "petr", "второе", "2026-08-01T11:00:00Z")))
            .Route("/api/v4/projects/group%2Fproj/issues/42", IssueJson(body: body))
            .RouteBytes("/uploads/aaa111/screen.png", [1, 2, 3, 4]);

        var imported = await Importer(fake).ImportIssueContentAsync(project,
            "https://gitlab.com/group/proj/-/issues/42", sourceId: null);

        Assert.Equal("Кнопка не нажимается", imported.Title);
        Assert.Equal("gitlab:gitlab.com:77:42", imported.ExternalRef);
        Assert.Equal("https://gitlab.com/group/proj/-/issues/42", imported.Url);
        Assert.Equal(2, imported.Notes.Count);
        // файл скачан и лежит в хранилище проекта, а ссылка в статье переписана на местную
        Assert.Equal(["screen.png"], imported.Files);
        Assert.Contains("api/files/raw?path=", imported.Description);
        Assert.DoesNotContain("/uploads/aaa111/screen.png", imported.Description);
        // картинка осталась картинкой
        Assert.Contains("![экран](api/files/raw", imported.Description);
        // токен уехал ЗАГОЛОВКОМ и ни в один адрес не попал
        Assert.All(fake.Tokens, t => Assert.Equal("glpat-XXXXXXXXXXXXXXXXXXXX", t));
        Assert.All(fake.Urls, u => Assert.DoesNotContain("glpat-", u));
    }

    [Fact]
    public async Task A_file_that_cannot_be_downloaded_stays_an_absolute_link()
    {
        var project = _f.Projects.Create("Файл без прав", null, null, null);
        GitLabSource();
        var body = "Статья.\n\n[секрет.zip](/uploads/ccc/secret.zip)";
        var fake = new FakeGitLab()
            .Route("/issues/42/notes", "[]")
            .Route("/api/v4/projects/group%2Fproj/issues/42", IssueJson(body: body));
        // маршрута на файл нет — заглушка ответит 404

        var imported = await Importer(fake).ImportIssueContentAsync(project,
            "https://gitlab.com/group/proj/-/issues/42", sourceId: null);

        // статья импортирована, а ссылка стала АБСОЛЮТНОЙ: относительная в задаче AI2P
        // никуда не ведёт, а по абсолютной хотя бы можно перейти
        Assert.Empty(imported.Files);
        Assert.Contains("https://gitlab.com/group/proj/uploads/ccc/secret.zip", imported.Description);
        Assert.StartsWith("Статья.", imported.Description);
    }

    [Fact]
    public async Task A_link_that_is_not_a_gitlab_issue_is_refused_by_words()
    {
        var project = _f.Projects.Create("Чужая ссылка", null, null, null);
        GitLabSource();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitLab()).ImportIssueContentAsync(project,
                "https://gitlab.com/group/proj/-/merge_requests/1", null));
        Assert.Contains("issues", ex.Message);
    }

    // ------------------------------------------------------------ 5. массовый импорт

    [Fact]
    public async Task Bulk_import_creates_tasks_and_does_not_duplicate_them()
    {
        var project = _f.Projects.Create("Массовый GitLab", null, null, null);
        var source = GitLabSource();
        var fake = new FakeGitLab()
            .Route("/issues/1/notes", NotesJson((11, "ivan", "к первой", "2026-08-01T10:00:00Z")))
            .Route("/issues/2/notes", "[]")
            .Route("state=opened", "[" + IssueJson(1, "Первая", path: "group/proj")
                                       + "," + IssueJson(2, "Вторая", path: "group/proj") + "]");
        var importer = Importer(fake);
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "imp" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human, SystemRole = SystemRole.Editor,
        }, null);

        var first = await importer.ImportAsync(project, source.Id, true, false, executor.Id);
        Assert.Equal(2, first.Added);
        Assert.Equal(1, first.Comments);

        var tasks = _f.Tasks.List(project.Id);
        var one = tasks.Single(t => t.Title == "Первая");
        Assert.Equal("gitlab:gitlab.com:77:1", one.ExternalRef);
        // ссылка импорта (T-246) проставлена сама: по ней задачу потом обновляют
        Assert.Equal("https://gitlab.com/group/proj/-/issues/1", one.ImportUrl);
        Assert.Equal(TaskStatuses.Draft, one.Status);

        // повторный прогон с «добавлять новые» ничего не добавляет и обсуждение не дублирует
        var second = await importer.ImportAsync(project, source.Id, true, true, executor.Id);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Updated);
        Assert.Equal(0, second.Comments);
        Assert.Equal(2, _f.Tasks.List(project.Id).Count);
        Assert.Single(_f.Chat.ListByTask(one.Id));
    }

    [Fact]
    public async Task Bulk_import_without_add_new_skips_unknown_issues()
    {
        var project = _f.Projects.Create("Только обновление", null, null, null);
        var source = GitLabSource();
        var fake = new FakeGitLab()
            .Route("/notes", "[]")
            .Route("state=opened", "[" + IssueJson(1, "Первая") + "]");

        var result = await Importer(fake).ImportAsync(project, source.Id, false, true, null);
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(_f.Tasks.List(project.Id));
    }

    [Fact]
    public async Task A_source_without_a_project_is_refused_by_words()
    {
        var project = _f.Projects.Create("Без проекта GitLab", null, null, null);
        var source = GitLabSource(project: "");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitLab()).ImportAsync(project, source.Id, true, false, null));
        Assert.Contains("GitLab", ex.Message);
    }

    // ------------------------------------------ 6. обновление задачи из GitLab (T-246)

    [Fact]
    public async Task Refresh_rereads_the_issue_and_keeps_what_is_ours()
    {
        var project = _f.Projects.Create("Обновление GitLab", null, null, null);
        GitLabSource();
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "man" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human, SystemRole = SystemRole.Editor,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Старый заголовок",
            Status = TaskStatuses.InProgress,
            ImportUrl = "https://gitlab.com/group/proj/-/issues/42#note_1",
        }, "старое описание", "критерии приёмки местные", executor.Id);

        var fake = new FakeGitLab()
            .Route("/issues/42/notes", NotesJson((5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .Route("/api/v4/projects/group%2Fproj/issues/42",
                IssueJson(title: "Новый заголовок", body: "Свежая статья"));

        var result = await Importer(fake).RefreshTaskAsync(project, task, null, executor.Id);

        var after = _f.Tasks.Get(task.Id)!;
        Assert.Equal("Новый заголовок", after.Title);
        Assert.Contains("Свежая статья", _f.Tasks.ReadDescription(after));
        // адрес приведён к каноническому: хвост «#note_1» ушёл
        Assert.Equal("https://gitlab.com/group/proj/-/issues/42", after.ImportUrl);
        // внешний ключ проставлен — повторный массовый импорт дубль не создаст
        Assert.Equal("gitlab:gitlab.com:77:42", after.ExternalRef);
        // СВОЁ обновление не трогает: статус и критерии приёмки ведут здесь
        Assert.Equal(TaskStatuses.InProgress, after.Status);
        Assert.Equal("критерии приёмки местные", _f.Tasks.ReadAcceptance(after));
        Assert.Equal(1, result.Comments);
        Assert.Single(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public async Task Repeated_refresh_adds_only_new_messages()
    {
        var project = _f.Projects.Create("Повторное обновление", null, null, null);
        GitLabSource();
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "man2" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human, SystemRole = SystemRole.Editor,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ImportUrl = "https://gitlab.com/group/proj/-/issues/42",
        }, "о", "", executor.Id);

        var first = new FakeGitLab()
            .Route("/issues/42/notes", NotesJson((5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .Route("/issues/42", IssueJson());
        await Importer(first).RefreshTaskAsync(project, _f.Tasks.Get(task.Id)!, null, executor.Id);

        var second = new FakeGitLab()
            .Route("/issues/42/notes", NotesJson(
                (5, "ivan", "первое", "2026-08-01T10:00:00Z"),
                (6, "petr", "второе", "2026-08-02T10:00:00Z")))
            .Route("/issues/42", IssueJson());
        var result = await Importer(second)
            .RefreshTaskAsync(project, _f.Tasks.Get(task.Id)!, null, executor.Id);

        Assert.Equal(1, result.Comments);
        Assert.Equal(2, _f.Chat.ListByTask(task.Id).Count);
    }

    [Fact]
    public async Task Refresh_of_a_task_without_an_import_link_is_refused()
    {
        var project = _f.Projects.Create("Без ссылки", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "о", "", null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitLab()).RefreshTaskAsync(project, task, null, null));
        Assert.Contains(task.DisplayId, ex.Message);
    }

    // ------------------------------------------------- 7. токен, проверка, отказы

    [Fact]
    public async Task Check_tells_whose_account_it_is_and_whether_the_project_is_visible()
    {
        GitLabSource();
        var fake = new FakeGitLab()
            .Route("/api/v4/user", "{\"username\":\"mmffr\"}")
            .Route("/api/v4/projects/group%2Fproj",
                "{\"path_with_namespace\":\"group/proj\"}");

        var (ok, message) = await Importer(fake).CheckAsync(null);
        Assert.True(ok);
        Assert.Contains("mmffr", message);
        Assert.Contains("group/proj", message);
    }

    [Fact]
    public async Task A_rejected_token_is_explained_by_words()
    {
        GitLabSource();
        var fake = new FakeGitLab()
            .Route("/api/v4/user", "{\"message\":\"401 Unauthorized\"}", HttpStatusCode.Unauthorized);

        var (ok, message) = await Importer(fake).CheckAsync(null);
        Assert.False(ok);
        // отказ объясняется словами: что не принято, откуда взят токен, где выпустить новый
        Assert.Contains("401", message);
        Assert.Contains("read_api", message);
        Assert.Contains(GitLabImporter.KeyPage, message);
        // само значение токена в сообщение не попадает никогда — только его длина
        Assert.DoesNotContain("glpat-XXXXXXXXXXXXXXXXXXXX", message);
    }

    [Fact]
    public async Task A_missing_token_names_the_place_where_to_put_it()
    {
        GitLabSource();
        var importer = new GitLabImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files,
            _f.Chat)
        {
            HttpOverride = new HttpClient(new FakeGitLab()),
            KeyResolver = _ => (null, "не найдено"),
        };
        var (ok, message) = await importer.CheckAsync(null);
        Assert.False(ok);
        Assert.Contains("gl.token", message);
    }

    [Fact]
    public void Token_shape_is_hinted_but_never_forbidden()
    {
        Assert.True(GitLabImporter.LooksLikeToken("glpat-1234567890abcdefghij"));
        Assert.False(GitLabImporter.LooksLikeToken("просто строка"));
        // подсказка есть только у непохожего значения, и самого значения в ней нет
        Assert.Equal("", GitLabImporter.SecretHint("token", "glpat-1234567890abcdefghij"));
        var hint = GitLabImporter.SecretHint("token", "секретное-значение");
        Assert.NotEqual("", hint);
        Assert.DoesNotContain("секретное-значение", hint);
        // поле ключа у GitLab не спрашивают вовсе — замечаний к нему нет
        Assert.Equal("", GitLabImporter.SecretHint("key", "что угодно"));
    }

    [Fact]
    public void Host_is_normalized_to_one_form()
    {
        Assert.Equal("https://gitlab.com", GitLabImporter.NormalizeHost(""));
        Assert.Equal("https://gitlab.com", GitLabImporter.NormalizeHost("  https://gitlab.com/  "));
        // схему можно не писать — подставляется https
        Assert.Equal("https://git.example.com", GitLabImporter.NormalizeHost("git.example.com"));
        Assert.Equal("gitlab.com", GitLabImporter.HostNameOf("https://gitlab.com"));
    }

    // ------------------------------- 8. один токен вместо пары ключ + токен (форма)

    [Fact]
    public void A_gitlab_source_needs_a_token_only()
    {
        var keys = new ImportKeyService(_f.Imports, new ModelKeyService(_f.Models, _f.Files,
            _f.Secrets, _f.KeyStore, _f.OrgKeys, StorageFixture.OrgId));
        var source = GitLabSource();

        Assert.False(ImportKeyService.NeedsKey("gitlab"));
        Assert.True(ImportKeyService.NeedsKey("trello"));

        var status = keys.StatusOf(source);
        Assert.False(status.NeedsKey);
        // ключа нет вовсе, и «не заполнен» здесь означало бы «источник неисправен»
        Assert.True(status.HasKey);
        Assert.Equal("", status.KeyRef);
        Assert.Equal("gl.token", status.TokenRef);

        // класть значение в несуществующее поле ключа нельзя — отказ словами
        var ex = Assert.Throws<ArgumentException>(() =>
            keys.SetValue(source.Id, ImportKeyService.FieldKey, "что-то", null));
        Assert.Contains("gitlab", ex.Message);
    }

    [Fact]
    public void Refs_of_a_gitlab_source_are_its_own_and_never_trello_s()
    {
        // источник, заведённый мимо формы (руками в базе), не должен получить ЧУЖОЙ токен
        var source = new ImportSource { Kind = "gitlab", Name = "Без ссылок", ParamsJson = "{}" };
        var (keyRef, tokenRef) = ImportKeyService.RefsOf(source);
        Assert.Equal("", keyRef);
        Assert.Equal(GitLabImporter.FallbackTokenRef, tokenRef);
        Assert.NotEqual(ImportSource.LegacyTokenRef, tokenRef);
    }

    // --------------------------- 9. инструмент агента: процедуру выбирает вид ссылки

    [Fact]
    public async Task The_agent_tool_picks_the_procedure_by_the_link()
    {
        var project = _f.Projects.Create("Инструмент агента", null, null, null);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);
        GitLabSource();
        var fake = new FakeGitLab()
            .Route("/issues/42/notes", NotesJson((5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .Route("/api/v4/projects/group%2Fproj/issues/42", IssueJson(body: "Статья агенту"));

        // оба импортёра подставлены: агент зовёт ОДИН инструмент на все источники
        var tools = new AgentToolset(null, new TaskToolset(_f.Tasks, _f.Chat, _f.Executors,
            _f.Files, current,
            importer: new TrelloImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files),
            project: project, gitlab: Importer(fake)));
        Assert.Contains(tools.Specs, s => s.Name == "import_task_from_url");

        var answer = await tools.ExecuteAsync("import_task_from_url",
            JsonDocument.Parse(
                """{"url":"https://gitlab.com/group/proj/-/issues/42"}""").RootElement, default);

        var created = _f.Tasks.List(project.Id).Single(t => t.Title == "Кнопка не нажимается");
        Assert.Contains(created.DisplayId, answer);
        Assert.Contains("Статья агенту", _f.Tasks.ReadDescription(created));
        Assert.Equal("https://gitlab.com/group/proj/-/issues/42", created.ImportUrl);
        Assert.Equal("gitlab:gitlab.com:77:42", created.ExternalRef);

        // повторный вызов дубля не создаёт — говорит, что задача уже есть
        var again = await tools.ExecuteAsync("import_task_from_url",
            JsonDocument.Parse(
                """{"url":"https://gitlab.com/group/proj/-/issues/42"}""").RootElement, default);
        Assert.Contains(created.DisplayId, again);
        Assert.Single(_f.Tasks.List(project.Id).Where(t => t.Title == "Кнопка не нажимается"));
    }

    [Fact]
    public async Task Only_the_gitlab_importer_is_enough_for_the_agent_tool()
    {
        var project = _f.Projects.Create("Только GitLab", null, null, null);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);
        var tools = new AgentToolset(null, new TaskToolset(_f.Tasks, _f.Chat, _f.Executors,
            _f.Files, current, project: project, gitlab: Importer(new FakeGitLab())));
        Assert.Contains(tools.Specs, s => s.Name == "import_task_from_url");

        // без импортёра Trello ЧУЖАЯ ссылка отказывает словами, а не падает
        var refusal = await tools.ExecuteAsync("import_task_from_url",
            JsonDocument.Parse("""{"url":"https://trello.com/c/abc"}""").RootElement, default);
        Assert.Contains("недоступен", refusal);
    }

    // ----------------------------------------------------------- 10. словари (T-180)

    [Fact]
    public void All_gitlab_texts_are_in_both_dictionaries()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        var ru = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(dir, "ru.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(dir, "en.json")))!;

        var mine = ru.Keys.Where(k => k.StartsWith("msg.gitlabImporter.", StringComparison.Ordinal)
                                      || k.StartsWith("imports.gitlab.", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(mine);
        foreach (var key in mine)
        {
            Assert.True(en.ContainsKey(key), $"нет английского текста для {key}");
            Assert.NotEqual("", en[key].Trim());
            // текст, уезжающий в интерфейс на английском, не должен содержать кириллицы
            Assert.DoesNotContain(en[key], c => c is >= 'А' and <= 'я');
        }
        // и наоборот: лишних английских ключей быть не должно
        foreach (var key in en.Keys.Where(k =>
                     k.StartsWith("msg.gitlabImporter.", StringComparison.Ordinal)))
        {
            Assert.True(ru.ContainsKey(key), $"нет русского текста для {key}");
        }
    }

    // ------------------------------------------- 11. поставляемая документация вида

    [Fact]
    public void The_source_kind_has_a_document_in_both_languages()
    {
        // документ вида источника (кнопка «i» формы) обязан быть на обоих языках, иначе
        // человеку негде прочитать, где взять токен
        var root = FindAppDocRoot();
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(root, lang, "import", "gitlab.md");
            Assert.True(File.Exists(path), $"нет документа {path}");
            var text = File.ReadAllText(path);
            Assert.Contains("read_api", text);
            Assert.Contains("personal_access_tokens", text);
            // относительных markdown-ссылок в поставляемом документе быть не должно:
            // они мертвы внутри страницы приложения (опыт T-214)
            Assert.DoesNotMatch(@"\]\((?!https?://|#)[^)]+\.md\)", text);
        }
    }

    /// <summary>Каталог поставляемой документации: тесты идут из каталога сборки, поэтому
    /// AI2P_app/doc ищется вверх по дереву — так же, как его ищет DocStore.</summary>
    private static string FindAppDocRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "doc");
            if (Directory.Exists(Path.Combine(candidate, "ru"))
                && Directory.Exists(Path.Combine(candidate, "en")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("каталог поставляемой документации не найден");
    }
}
