using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-247: ИМПОРТ ЗАДАЧ (ISSUES) ИЗ GITHUB.
///
/// Из задачи GitHub в задачу AI2P едут: САМА СТАТЬЯ (текст задачи) в описание,
/// ОБСУЖДЕНИЕ в чат задачи, заголовок и срок (дата вехи). Плюс процедура ОБНОВЛЕНИЯ
/// задачи из источника по образцу T-246: кнопка «обновить» перечитывает задачу GitHub
/// по ссылке импорта, хранящейся в самой задаче AI2P.
///
/// Что отличает GitHub от Trello и потому проверяется отдельно:
/// 1) секрет ОДИН — личный токен, и он уезжает ЗАГОЛОВКОМ, а не в строке запроса;
/// 2) запросы на слияние в API GitHub выглядят задачами, и их нельзя тащить в проект;
/// 3) срока у задачи нет — им становится дата вехи;
/// 4) обсуждение приезжает ОТДЕЛЬНЫМ запросом, и делать его надо только там, где оно есть.
/// </summary>
public sealed class T247Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ------------------------------------------------------------ заглушка GitHub

    /// <summary>Ответ вместо живого GitHub: тело подбирается по КУСКУ адреса. Заодно
    /// запоминает адреса обращений и заголовок Authorization каждого из них.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly List<(string Match, HttpStatusCode Status, string Body)> _routes = [];

        public List<string> Urls { get; } = [];

        public List<string> Auth { get; } = [];

        public List<string> Agents { get; } = [];

        public FakeGitHub Route(string match, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes.Add((match, status, body));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            Auth.Add(request.Headers.TryGetValues("Authorization", out var a) ? string.Join(",", a) : "");
            Agents.Add(request.Headers.TryGetValues("User-Agent", out var u) ? string.Join(",", u) : "");
            foreach (var (match, status, body) in _routes)
            {
                if (url.Contains(match, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"Not Found\"}", Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private const string Token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";

    private GitHubImporter Importer(FakeGitHub? handler = null) =>
        new(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Chat)
        {
            HttpOverride = handler is null ? null : new HttpClient(handler),
            KeyResolver = _ => (Token, "тест"),
        };

    private ImportSource GitHubSource(string login = "mmfgrp", string filter = "") =>
        _f.Imports.Create(new ImportSource
        {
            Name = "GitHub T-247 " + Guid.NewGuid().ToString("N")[..6],
            Kind = "github",
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(new { login, filter, tokenRef = "gh.token" }),
        }, null);

    /// <summary>Тело одной задачи в том виде, в каком его отдаёт GitHub.</summary>
    private static string IssueJson(int number = 42, string title = "Кнопка не нажимается",
        string body = "Статья задачи", string? due = "2026-09-01T00:00:00Z", int comments = 0,
        long id = 0, string repo = "mmfgrp/planner", bool pull = false)
    {
        var issue = new Dictionary<string, object?>
        {
            ["id"] = id > 0 ? id : 900000 + number,
            ["number"] = number,
            ["title"] = title,
            ["body"] = body,
            ["comments"] = comments,
            ["html_url"] = $"https://github.com/{repo}/issues/{number}",
            ["milestone"] = due is null ? null : new { title = "веха", due_on = due },
        };
        if (pull)
        {
            issue["pull_request"] = new { url = "https://api.github.com/pulls/1" };
        }
        return JsonSerializer.Serialize(issue);
    }

    /// <summary>Исполнитель, от чьего имени идёт импорт: без него обсуждение в чат
    /// не переносится вовсе — колонка from_executor_id пустой быть не может (T-152).</summary>
    private Executor Actor() =>
        _f.Executors.Create(new Executor
        {
            Nick = "импортёр " + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Editor,
        }, null);

    private static string CommentsJson(params (long Id, string Author, string Text, string Date)[] items) =>
        JsonSerializer.Serialize(items.Select(c => new
        {
            id = c.Id,
            body = c.Text,
            created_at = c.Date,
            user = new { login = c.Author },
        }));

    // ---------------------------------------------------- 1. разбор ссылки на задачу

    [Theory]
    [InlineData("https://github.com/mmfgrp/planner/issues/42", "mmfgrp", "planner", 42)]
    [InlineData("http://github.com/mmfgrp/planner/issues/7", "mmfgrp", "planner", 7)]
    [InlineData("https://www.github.com/org/repo/issues/1", "org", "repo", 1)]
    // хвосты: якорь на комментарий и строка запроса
    [InlineData("https://github.com/org/repo/issues/42#issuecomment-12345", "org", "repo", 42)]
    [InlineData("https://github.com/org/repo/issues/3?utm=1", "org", "repo", 3)]
    // адрес без схемы — так его копируют из адресной строки не целиком
    [InlineData("github.com/org/repo/issues/9", "org", "repo", 9)]
    public void Issue_url_is_parsed(string url, string owner, string repo, int number)
    {
        var parsed = GitHubImporter.ParseIssueUrl(url);
        Assert.NotNull(parsed);
        Assert.Equal(owner, parsed!.Owner);
        Assert.Equal(repo, parsed.Repo);
        Assert.Equal(number, parsed.Number);
        Assert.Equal($"{owner}/{repo}", parsed.FullName);
        Assert.True(GitHubImporter.CanRefresh(url));
    }

    [Theory]
    [InlineData("https://trello.com/c/abc123")]                        // чужой источник
    [InlineData("https://gitlab.com/group/proj/-/issues/42")]          // тоже чужой
    [InlineData("https://github.com/org/repo/pull/42")]                // запрос на слияние
    [InlineData("https://github.com/org/repo/issues")]                 // список, а не задача
    [InlineData("https://github.com/org/repo/issues/0")]
    [InlineData("https://github.com/org/repo/issues/abc")]
    [InlineData("https://github.com/org/repo")]
    [InlineData("")]
    [InlineData(null)]
    public void Foreign_url_is_not_a_github_issue(string? url)
    {
        Assert.Null(GitHubImporter.ParseIssueUrl(url));
        Assert.False(GitHubImporter.CanRefresh(url));
    }

    [Fact]
    public void External_ref_is_the_global_issue_id()
    {
        // НОМЕР задачи уникален только внутри репозитория: #42 двух репозиториев одного
        // владельца слились бы в одну задачу, поэтому ключом идёт глобальный id
        Assert.Equal("github:900042", GitHubImporter.ExternalRefOf("900042"));
        Assert.NotEqual(GitHubImporter.ExternalRefOf("1"), GitHubImporter.ExternalRefOf("2"));
    }

    // ------------------------------------------------------------- 2. разбор ответов

    [Fact]
    public void Issue_json_is_parsed()
    {
        var issue = GitHubImporter.ParseIssue(IssueJson(), "mmfgrp/planner");
        Assert.NotNull(issue);
        Assert.Equal(42, issue!.Number);
        Assert.Equal("900042", issue.Id);
        Assert.Equal("Кнопка не нажимается", issue.Title);
        Assert.Equal("Статья задачи", issue.Body);
        Assert.Equal("mmfgrp/planner", issue.Repo);
        Assert.Equal("https://github.com/mmfgrp/planner/issues/42", issue.Url);
        // срока у задачи GitHub нет — им становится дата вехи
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), issue.Due);
    }

    [Fact]
    public void An_issue_without_a_milestone_has_no_due_date()
    {
        var issue = GitHubImporter.ParseIssue(IssueJson(due: null), "mmfgrp/planner");
        Assert.Null(issue!.Due);
    }

    [Fact]
    public void Pull_requests_are_not_issues()
    {
        // в API GitHub запрос на слияние — тоже «issue»: без отбора импорт затащил бы
        // в проект все открытые PR репозитория
        Assert.Null(GitHubImporter.ParseIssue(IssueJson(pull: true), "mmfgrp/planner"));

        var json = "[" + IssueJson(1, "Первая") + "," + IssueJson(2, "Слияние", pull: true)
                   + "," + IssueJson(3, "Вторая") + ",{\"number\":0,\"title\":\"\"}]";
        var issues = GitHubImporter.ParseIssues(json, "mmfgrp/planner");
        Assert.Equal(["Первая", "Вторая"], issues.Select(i => i.Title));
    }

    [Fact]
    public void Comments_are_parsed_in_chronological_order()
    {
        var json = CommentsJson(
            (7, "petr", "поздний", "2026-08-02T10:00:00Z"),
            (5, "ivan", "ранний", "2026-08-01T10:00:00Z"),
            (9, "", "  ", "2026-08-03T10:00:00Z"));   // пустой текст — не сообщение
        var comments = GitHubImporter.ParseComments(json);
        Assert.Equal(["ранний", "поздний"], comments.Select(c => c.Text));
        Assert.Equal("ivan", comments[0].Author);
    }

    [Fact]
    public void Chat_messages_are_marked_as_github()
    {
        var comments = new List<GitHubImporter.IssueComment>
        {
            new("7", "petr", "поздний", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)),
            new("5", "", "ранний", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var chat = GitHubImporter.ChatOf(comments);
        Assert.Equal(["ранний", "поздний"], chat.Select(m => m.Text));
        // автора не узнали (аккаунт удалён) — сообщение всё равно принадлежит GitHub
        Assert.Equal("github:?", chat[0].Author);
        Assert.Equal("github:petr", chat[1].Author);
        Assert.Equal("github:5", chat[0].ExternalRef);
    }

    [Fact]
    public void Description_is_the_article_plus_the_source_line()
    {
        var issue = GitHubImporter.ParseIssue(IssueJson(body: "Текст статьи"), "mmfgrp/planner")!;
        var description = GitHubImporter.DescriptionOf(issue);
        Assert.StartsWith("Текст статьи", description);
        Assert.Contains("mmfgrp/planner", description);
        Assert.Contains("https://github.com/mmfgrp/planner/issues/42", description);

        // пустая статья — остаётся одна приписка-источник, без пустых разделителей
        var empty = GitHubImporter.DescriptionOf(
            GitHubImporter.ParseIssue(IssueJson(body: ""), "mmfgrp/planner")!);
        Assert.DoesNotContain("---", empty);
    }

    [Fact]
    public void Repositories_are_filtered_by_a_name_substring()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "planner", full_name = "mmfgrp/planner" },
            new { name = "PlannerDocs", full_name = "mmfgrp/PlannerDocs" },
            new { name = "site", full_name = "mmfgrp/site" },
        });
        var (all, total) = GitHubImporter.ParseRepos(json, "");
        Assert.Equal(3, total);
        Assert.Equal(3, all.Count);
        // отбор регистронезависимый — как у фильтра досок Trello
        var (some, _) = GitHubImporter.ParseRepos(json, "planner");
        Assert.Equal(["mmfgrp/planner", "mmfgrp/PlannerDocs"], some);
    }

    // --------------------------------------------- 3. импорт одиночной задачи по URL

    [Fact]
    public async Task Single_issue_is_imported_with_its_article_and_discussion()
    {
        GitHubSource();
        var fake = new FakeGitHub()
            .Route("/issues/42/comments", CommentsJson(
                (5, "ivan", "первое", "2026-08-01T10:00:00Z"),
                (6, "petr", "второе", "2026-08-01T11:00:00Z")))
            .Route("/repos/mmfgrp/planner/issues/42", IssueJson(comments: 2));

        var imported = await Importer(fake).ImportIssueContentAsync(
            "https://github.com/mmfgrp/planner/issues/42", sourceId: null);

        Assert.Equal("Кнопка не нажимается", imported.Title);
        Assert.Equal("github:900042", imported.ExternalRef);
        Assert.Equal("https://github.com/mmfgrp/planner/issues/42", imported.Url);
        Assert.Contains("Статья задачи", imported.Description);
        Assert.Equal(2, imported.Comments.Count);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), imported.Due);

        // токен уезжает ЗАГОЛОВКОМ, а не в строке запроса: строку запроса пишут в лог
        // и прокси, и сам GitHub
        Assert.All(fake.Auth, a => Assert.Equal($"Bearer {Token}", a));
        Assert.All(fake.Urls, u => Assert.DoesNotContain(Token, u));
        // без User-Agent GitHub отвечает отказом
        Assert.All(fake.Agents, a => Assert.StartsWith("AI2P/", a));
    }

    [Fact]
    public async Task An_issue_without_comments_costs_no_extra_request()
    {
        GitHubSource();
        var fake = new FakeGitHub().Route("/repos/mmfgrp/planner/issues/42", IssueJson(comments: 0));

        var imported = await Importer(fake).ImportIssueContentAsync(
            "https://github.com/mmfgrp/planner/issues/42", sourceId: null);

        Assert.Empty(imported.Comments);
        // обсуждение у GitHub приезжает отдельным запросом — и его не должно быть вовсе
        Assert.DoesNotContain(fake.Urls, u => u.Contains("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_link_of_another_system_is_refused_by_words()
    {
        GitHubSource();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitHub()).ImportIssueContentAsync(
                "https://trello.com/c/abc123", sourceId: null));
        Assert.Contains("github.com", ex.Message);
    }

    [Fact]
    public async Task A_source_of_another_kind_is_refused_by_words()
    {
        var trello = _f.Imports.Create(new ImportSource
        {
            Name = "Trello для T-247",
            Kind = "trello",
            IsActive = true,
            ParamsJson = "{\"login\":\"mike\"}",
        }, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitHub()).ImportIssueContentAsync(
                "https://github.com/mmfgrp/planner/issues/42", trello.Id));
        Assert.Contains("trello", ex.Message);
    }

    // ------------------------------------------------------- 4. массовый импорт

    [Fact]
    public async Task Open_issues_of_the_matching_repositories_become_draft_tasks()
    {
        var project = _f.Projects.Create("Импорт GitHub", null, null, null);
        var source = GitHubSource(filter: "planner");
        var actor = Actor();
        var fake = new FakeGitHub()
            .Route("/users/mmfgrp/repos", JsonSerializer.Serialize(new[]
            {
                new { name = "planner", full_name = "mmfgrp/planner" },
                new { name = "site", full_name = "mmfgrp/site" },
            }))
            .Route("/repos/mmfgrp/planner/issues/1/comments",
                CommentsJson((5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .Route("/repos/mmfgrp/planner/issues?",
                "[" + IssueJson(1, "Первая", comments: 1) + ","
                    + IssueJson(2, "Слияние", pull: true) + "]")
            // «кто я» — последним: этот кусок адреса есть и у /users/<логин>/repos
            .Route("/user", "{\"login\":\"someone-else\"}");

        var result = await Importer(fake).ImportAsync(project, source.Id, addNew: true,
            updateExisting: false, actorId: actor.Id);

        // запрос на слияние задачей не стал
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Comments);
        // репозиторий «site» фильтр не прошёл — за его задачами никто не ходил
        Assert.DoesNotContain(fake.Urls, u => u.Contains("/repos/mmfgrp/site/", StringComparison.Ordinal));

        var task = Assert.Single(_f.Tasks.List(project.Id));
        Assert.Equal("Первая", task.Title);
        Assert.Equal(TaskStatuses.Draft, task.Status);
        // ссылка импорта (T-246) проставлена сразу: по ней задачу потом обновляют
        Assert.Equal("https://github.com/mmfgrp/planner/issues/1", task.ImportUrl);
        Assert.Contains("Статья задачи", _f.Tasks.ReadDescription(task));
    }

    [Fact]
    public async Task A_repeated_import_makes_no_duplicates_and_adds_only_new_messages()
    {
        var project = _f.Projects.Create("Импорт GitHub 2", null, null, null);
        var source = GitHubSource();
        var actor = Actor();

        FakeGitHub Fake(string title, params (long, string, string, string)[] comments) =>
            new FakeGitHub()
                .Route("/users/mmfgrp/repos",
                    "[{\"name\":\"planner\",\"full_name\":\"mmfgrp/planner\"}]")
                .Route("/user", "{\"login\":\"someone-else\"}")
                .Route("/repos/mmfgrp/planner/issues/1/comments", CommentsJson(comments))
                .Route("/repos/mmfgrp/planner/issues?",
                    "[" + IssueJson(1, title, body: "Статья " + title, comments: comments.Length) + "]");

        var first = await Importer(Fake("Первая", (5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .ImportAsync(project, source.Id, true, false, actor.Id);
        Assert.Equal(1, first.Added);
        Assert.Equal(1, first.Comments);

        // тот же импорт БЕЗ галочки «обновить существующие» — задача пропускается
        var again = await Importer(Fake("Первая", (5, "ivan", "первое", "2026-08-01T10:00:00Z")))
            .ImportAsync(project, source.Id, true, false, actor.Id);
        Assert.Equal(0, again.Added);
        Assert.Equal(1, again.Skipped);
        Assert.Single(_f.Tasks.List(project.Id));

        // с галочкой — заголовок и статья обновляются, а в чат едут ТОЛЬКО новые сообщения
        var updated = await Importer(Fake("Переименована",
                (5, "ivan", "первое", "2026-08-01T10:00:00Z"),
                (6, "petr", "второе", "2026-08-02T10:00:00Z")))
            .ImportAsync(project, source.Id, true, true, actor.Id);
        Assert.Equal(1, updated.Updated);
        Assert.Equal(1, updated.Comments);

        var task = Assert.Single(_f.Tasks.List(project.Id));
        Assert.Equal("Переименована", task.Title);
        Assert.Contains("Статья Переименована", _f.Tasks.ReadDescription(task));
        Assert.Equal(2, _f.Chat.ListByTask(task.Id).Count);
    }

    // ---------------------------------------- 5. обновление задачи из источника (T-246)

    [Fact]
    public async Task A_task_is_refreshed_from_its_import_link()
    {
        var project = _f.Projects.Create("Обновление GitHub", null, null, null);
        GitHubSource();
        var actor = Actor();
        // задачу завели РУКАМИ и вписали ссылку импорта: внешнего ключа у неё ещё нет
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Старый заголовок",
            Status = TaskStatuses.Draft,
            ImportUrl = "https://github.com/mmfgrp/planner/issues/42#issuecomment-1",
        }, "старое описание", "критерии приёмки", actor.Id);

        var fake = new FakeGitHub()
            .Route("/issues/42/comments",
                CommentsJson((5, "ivan", "из обсуждения", "2026-08-01T10:00:00Z")))
            .Route("/repos/mmfgrp/planner/issues/42",
                IssueJson(42, "Свежий заголовок", "Свежая статья", comments: 1));

        var result = await Importer(fake).RefreshTaskAsync(task, sourceId: null, actor.Id);

        Assert.Equal("Свежий заголовок", result.Title);
        Assert.Equal(1, result.Comments);

        var reread = _f.Tasks.Get(task.Id)!;
        Assert.Equal("Свежий заголовок", reread.Title);
        Assert.Contains("Свежая статья", _f.Tasks.ReadDescription(reread));
        Assert.DoesNotContain("старое описание", _f.Tasks.ReadDescription(reread));
        // критерии приёмки обновление не трогает — их ведут здесь, а не в GitHub
        Assert.Equal("критерии приёмки", _f.Tasks.ReadAcceptance(reread));
        // внешний ключ теперь есть: повторный массовый импорт дубля не создаст
        Assert.Equal("github:900042", reread.ExternalRef);
        // адрес приведён к каноническому виду — без хвоста, который вставил человек
        Assert.Equal("https://github.com/mmfgrp/planner/issues/42", reread.ImportUrl);
        Assert.Single(_f.Chat.ListByTask(task.Id));

        // повторное обновление тех же данных сообщений не дублирует
        var twice = await Importer(fake).RefreshTaskAsync(reread, null, actor.Id);
        Assert.Equal(0, twice.Comments);
        Assert.Single(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public async Task A_task_without_an_import_link_is_refused_by_words()
    {
        var project = _f.Projects.Create("Без ссылки", null, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Своя задача",
            Status = TaskStatuses.Draft,
        }, "", "", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer(new FakeGitHub()).RefreshTaskAsync(task, null, null));
        Assert.Contains(task.DisplayId, ex.Message);
    }

    // ------------------------------------------------- 6. отказы GitHub словами (T-134)

    [Theory]
    [InlineData("ghp_0123456789abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("github_pat_11ABCDEFG0abcdefghijklmnop", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]  // прежний вид: 40 знаков
    [InlineData("1234567890abcdef1234567890abcdef", false)]         // ключ Trello: 32 знака
    [InlineData("ATTAsomethingsomethingsomething", false)]          // токен Trello
    [InlineData("", false)]
    public void A_token_is_recognised_by_its_shape(string value, bool looksLikeToken)
    {
        Assert.Equal(looksLikeToken, GitHubImporter.LooksLikeToken(value));
        // замечание МЯГКОЕ: оно подсказывает, а не запрещает — вид токенов задаёт GitHub
        var hint = GitHubImporter.SecretHint(value);
        Assert.Equal(looksLikeToken || value.Length == 0, hint.Length == 0);
        // само значение в подсказку не попадает никогда — только длина и префикс
        Assert.DoesNotContain(value.Length > 8 ? value : " нет", hint);
    }

    [Fact]
    public void A_refusal_of_github_is_explained_by_words()
    {
        var auth = new GitHubImporter.Auth(Token, "gh.token", "организация");

        var badToken = GitHubImporter.Explain(
            new GitHubImporter.GitHubApiException(401, "{\"message\":\"Bad credentials\"}"), auth);
        Assert.Contains("gh.token", badToken);
        Assert.Contains("организация", badToken);
        Assert.Contains(GitHubImporter.KeyPage, badToken);
        Assert.DoesNotContain(Token, badToken);

        var rateLimit = GitHubImporter.Explain(
            new GitHubImporter.GitHubApiException(403, "API rate limit exceeded"), auth);
        Assert.NotEqual(badToken, rateLimit);

        // 403 без упоминания предела — это про права токена, а не про частоту запросов
        var denied = GitHubImporter.Explain(
            new GitHubImporter.GitHubApiException(403, "Resource not accessible"), auth);
        Assert.NotEqual(rateLimit, denied);

        var notFound = GitHubImporter.Explain(
            new GitHubImporter.GitHubApiException(404, "Not Found"), auth);
        Assert.NotEqual(denied, notFound);
    }

    [Fact]
    public async Task A_check_of_the_connection_names_the_account()
    {
        var source = GitHubSource(login: "mmfgrp");
        var (ok, message) = await Importer(new FakeGitHub().Route("/user", "{\"login\":\"mmfgrp\"}"))
            .CheckAsync(source.Id);
        Assert.True(ok);
        Assert.Contains("mmfgrp", message);

        // токен выдан ДРУГОМУ аккаунту — проверка это называет, но отказом не считает
        var (okOther, other) = await Importer(new FakeGitHub().Route("/user", "{\"login\":\"someone\"}"))
            .CheckAsync(source.Id);
        Assert.True(okOther);
        Assert.Contains("someone", other);
        Assert.Contains("mmfgrp", other);

        // отказ GitHub — не ошибка запроса, а вердикт проверки
        var (bad, why) = await Importer(new FakeGitHub()
                .Route("/user", "{\"message\":\"Bad credentials\"}", HttpStatusCode.Unauthorized))
            .CheckAsync(source.Id);
        Assert.False(bad);
        Assert.Contains("gh.token", why);
    }

    // ------------------------------- 7. один токен вместо пары ключ + токен (форма)

    [Fact]
    public void A_github_source_needs_a_token_only()
    {
        var keys = new ImportKeyService(_f.Imports, new ModelKeyService(_f.Models, _f.Files,
            _f.Secrets, _f.KeyStore, _f.OrgKeys, StorageFixture.OrgId));
        var source = GitHubSource();

        Assert.False(ImportKeyService.NeedsKey("github"));
        Assert.True(ImportKeyService.NeedsKey("trello"));

        var status = keys.StatusOf(source);
        Assert.False(status.NeedsKey);
        // ключа нет вовсе, и «не заполнен» здесь означало бы «источник неисправен»
        Assert.True(status.HasKey);
        Assert.Equal("", status.KeyRef);
        Assert.Equal("gh.token", status.TokenRef);

        // класть значение в несуществующее поле ключа нельзя — отказ словами
        var ex = Assert.Throws<ArgumentException>(() =>
            keys.SetValue(source.Id, ImportKeyService.FieldKey, "что-то", null));
        Assert.Contains("github", ex.Message);
    }

    [Fact]
    public void Refs_of_a_github_source_are_its_own_and_never_trello_s()
    {
        // источник, заведённый мимо формы (руками в базе), не должен получить ЧУЖОЙ токен
        var source = new ImportSource { Kind = "github", Name = "Без ссылок", ParamsJson = "{}" };
        var (keyRef, tokenRef) = ImportKeyService.RefsOf(source);
        Assert.Equal("", keyRef);
        Assert.Equal(GitHubImporter.DefaultTokenRef, tokenRef);
        Assert.NotEqual(ImportSource.LegacyTokenRef, tokenRef);
    }

    // ----------------------------------------------------------- 8. словари (T-180)

    [Fact]
    public void All_github_texts_are_in_both_dictionaries()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        var ru = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(dir, "ru.json")))!;
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(dir, "en.json")))!;

        var keys = ru.Keys.Where(k => k.StartsWith("msg.githubImporter.", StringComparison.Ordinal)
                                      || k.StartsWith("imports.github.", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.True(en.ContainsKey(key), $"в en.json нет ключа {key}");
            Assert.NotEqual("", en[key].Trim());
        }
        // подсказка адреса в окне импорта по URL называет обе формы (T-247)
        Assert.Contains("github.com", ru["imports.card.urlPlaceholder"]);
        Assert.Contains("github.com", en["imports.card.urlPlaceholder"]);
        // отказ «такую ссылку обновлять не умеем» перечисляет и GitHub
        Assert.Contains("github.com", ru["msg.apiEndpoints.35"]);
        Assert.Contains("github.com", en["msg.apiEndpoints.35"]);
    }

    // ------------------------------------------------ 9. документ вида источника (гл. 14)

    [Fact]
    public void The_source_kind_has_a_document_in_both_languages()
    {
        // кнопка «i» рядом с полем «Вид» открывает doc/<язык>/import/<код вида>.md
        var root = FindAppDoc();
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(root, lang, "import", "github.md");
            Assert.True(File.Exists(path), $"нет документа {path}");
            var text = File.ReadAllText(path);
            // главное в документе — как получить токен: без него импорт не работает
            Assert.Contains("github.com/settings/tokens", text);
            Assert.Contains("issues", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Каталог поставляемой документации (AI2P_app/doc) от каталога сборки тестов.</summary>
    private static string FindAppDoc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "doc", "ru", "import");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "doc");
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("не найден каталог doc/<язык>/import");
    }
}
