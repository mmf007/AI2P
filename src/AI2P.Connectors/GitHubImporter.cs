using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ИМПОРТ ЗАДАЧ ИЗ GITHUB (T-247, версия 1.89) — второй вид источника после Trello.
/// Устроен по образцу <see cref="TrelloImporter"/>, и снаружи выглядит так же: запись
/// справочника импортов (kind «github», params: login — владелец репозиториев, filter —
/// подстрока имени репозитория, tokenRef — ссылка на токен), три способа импорта
/// (массовый по источнику, одиночный по ссылке, обновление уже созданной задачи) и
/// обсуждение задачи GitHub, уезжающее в чат задачи AI2P.
///
/// Отличия от Trello, которые видно снаружи:
/// * секрет ОДИН — персональный токен (PAT); отдельного ключа API у GitHub нет;
/// * берутся ОТКРЫТЫЕ задачи (issues) репозиториев владельца; запросы на слияние
///   (pull requests) в API GitHub тоже «issues», но задачами не считаются и пропускаются;
/// * срока у задачи GitHub нет — за срок берётся дата вехи (milestone.due_on), если она есть;
/// * приложенные файлы у GitHub живут ссылками внутри самого текста задачи, отдельного
///   списка вложений в API нет — текст переносится как есть, ссылки остаются ссылками.
/// </summary>
public sealed class GitHubImporter
{
    /// <summary>Код вида в справочнике импортов.</summary>
    public const string Kind = ImportKinds.GitHub;

    private const string BaseUrl = "https://api.github.com";

    /// <summary>Страница, где выдаётся токен — она же названа в документе вида источника;
    /// в сообщении об отказе ссылка важнее любых слов (приём T-134).</summary>
    public const string KeyPage = "https://github.com/settings/tokens";

    /// <summary>Записей на страницу — предел GitHub.</summary>
    private const int PerPage = 100;

    /// <summary>Сколько страниц забирается за раз: и репозиториев, и задач, и комментариев.
    /// Предел нужен, чтобы один импорт не ходил по чужому большому аккаунту бесконечно;
    /// упёрлись в него — это видно в логе.</summary>
    private const int MaxPages = 10;

    private readonly TaskService _tasks;
    private readonly ImportSourceService _sources;
    private readonly SecretStore _secrets;
    private readonly EventStore _events;
    private readonly ChatService? _chat;

    /// <param name="chat">Чат задач (T-152): обсуждение задачи GitHub переносится в чат
    /// задачи AI2P; null — комментарии не переносятся (массовый импорт в тестах без чата).</param>
    public GitHubImporter(TaskService tasks, ImportSourceService sources, SecretStore secrets,
        EventStore events, ChatService? chat = null)
    {
        _tasks = tasks;
        _sources = sources;
        _secrets = secrets;
        _events = events;
        _chat = chat;
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(GitHubImporter));

    /// <summary>Откуда брать токен: подставляется контекстом организации
    /// (<c>ModelKeyService.ResolveWithSource</c>) — значение лежит в БД организации
    /// зашифрованным. Не подставлен — читаем secrets.json, как у Trello.</summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>Подменный HttpClient — только для тестов: живой GitHub в прогоне не дёргается.</summary>
    public HttpClient? HttpOverride { get; set; }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private HttpClient Client => HttpOverride ?? Http;

    /// <summary>GitHub требует заголовок User-Agent и отвечает 403 на запрос без него.</summary>
    private static string UserAgent => $"{AppInfo.Name}/{AppInfo.Version}";

    /// <summary>Комментарий задачи GitHub — сообщение обсуждения: id (защита от дублей),
    /// логин автора В GITHUB, текст и время.</summary>
    public sealed record IssueComment(string Id, string Author, string Text, DateTime Date);

    /// <summary>Задача GitHub (нужные импорту поля). Repo — «владелец/репозиторий»,
    /// Number — номер задачи в репозитории, Url — её адрес (html_url), Due — дата вехи.
    /// Comments — обсуждение в хронологическом порядке; пусто — комментариев нет
    /// или их не запрашивали.</summary>
    public sealed record Issue(string Id, int Number, string Title, string Body, DateTime? Due,
        string Repo, string Url, List<IssueComment>? Comments = null)
    {
        /// <summary>Сколько комментариев у задачи по данным самого GitHub (поле comments
        /// ответа). Обсуждение приезжает ОТДЕЛЬНЫМ запросом, и по этому числу видно,
        /// нужно ли его делать вовсе.</summary>
        public int CommentCount { get; init; }
    }

    /// <summary>Итог импорта: добавлено / обновлено / пропущено и сколько сообщений
    /// обсуждения перенесено в чат.</summary>
    public sealed record ImportResult(int Added, int Updated, int Skipped, int Comments = 0);

    /// <summary>Содержимое одиночной задачи GitHub для формы задачи или инструмента агента.</summary>
    public sealed record ImportedIssue(string Title, string Description, DateTime? Due,
        string ExternalRef, List<IssueComment> Comments, string Url);

    /// <summary>Итог обновления задачи из GitHub — для сообщения человеку. Exported
    /// (T-107-S0) — сколько результатов задачи уехало ОБРАТНО, записями в обсуждение.</summary>
    public sealed record RefreshResult(string DisplayId, string Title, int Comments,
        int Exported = 0);

    /// <summary>Адрес задачи GitHub, разобранный на части.</summary>
    public sealed record IssueRef(string Owner, string Repo, int Number)
    {
        /// <summary>«владелец/репозиторий» — так репозиторий называет сам GitHub.</summary>
        public string FullName => $"{Owner}/{Repo}";
    }

    /// <summary>Параметры источника (params_json записи справочника импортов): владелец
    /// репозиториев лежит в том же поле «login», что и логин Trello — поле формы общее.</summary>
    public sealed record SourceParams(string Login, string Filter, string TokenRef)
    {
        public static SourceParams Parse(string paramsJson)
        {
            using var doc = JsonDocument.Parse(paramsJson);
            var root = doc.RootElement;
            string Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()!.Trim()
                    : "";
            var tokenRef = Str("tokenRef");
            return new SourceParams(Str("login"), Str("filter"),
                tokenRef.Length > 0 ? tokenRef : DefaultTokenRef);
        }
    }

    /// <summary>Ссылка на токен у источника, у которого её нет в params_json.</summary>
    public const string DefaultTokenRef = "github.token";

    // --- массовый импорт по источнику ---

    /// <summary>Запуск импорта в проект (кнопка списка задач): источник из справочника
    /// + чекбоксы «добавлять новые» / «обновить существующие».</summary>
    public async Task<ImportResult> ImportAsync(Project project, string sourceId, bool addNew,
        bool updateExisting, string? actorId, CancellationToken ct = default)
    {
        var source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.githubImporter.1"));
        EnsureGitHub(source);
        var p = SourceParams.Parse(source.ParamsJson);
        if (p.Login.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.githubImporter.3"));
        }
        var auth = ResolveAuth(p);

        Logger.Information("Импорт GitHub: источник {Source}, владелец {Owner}, фильтр «{Filter}», " +
                           "проект {Project}, новые {AddNew}, обновление {Update}",
            source.Name, p.Login, p.Filter, project.Name, addNew, updateExisting);
        List<Issue> issues;
        try
        {
            issues = await FetchIssuesAsync(p.Login, p.Filter, auth, ct);
        }
        catch (GitHubApiException ex)
        {
            throw Explained(ex, auth);
        }
        var result = Apply(project, issues, addNew, updateExisting, actorId);
        Logger.Information("Импорт GitHub: задач {Issues} → добавлено {Added}, обновлено {Updated}, " +
                           "пропущено {Skipped}, сообщений обсуждения {Comments}",
            issues.Count, result.Added, result.Updated, result.Skipped, result.Comments);

        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = project.Id,
            EventType = EventTypes.ImportRun,
            EntityType = "import_source",
            EntityId = source.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                source.Name,
                issues = issues.Count,
                added = result.Added,
                updated = result.Updated,
                skipped = result.Skipped,
                comments = result.Comments,
            }),
        });
        return result;
    }

    /// <summary>Применить задачи GitHub к проекту (отделено от HTTP — тестируемо):
    /// external_ref «github:&lt;id&gt;» — есть в проекте → обновление (по чекбоксу),
    /// нет → создание.</summary>
    public ImportResult Apply(Project project, IReadOnlyList<Issue> issues, bool addNew,
        bool updateExisting, string? actorId)
    {
        int added = 0, updated = 0, skipped = 0, comments = 0;
        foreach (var issue in issues)
        {
            var externalRef = ExternalRefOf(issue.Id);
            var existing = _tasks.FindByExternalRef(project.Id, externalRef);
            if (existing is null)
            {
                if (!addNew)
                {
                    skipped++;
                    continue;
                }
                var task = new TaskItem
                {
                    ProjectId = project.Id,
                    Title = issue.Title,
                    Status = TaskStatuses.Draft,
                    DueDate = issue.Due,
                    ExternalRef = externalRef,
                    // ссылка импорта (T-246): по ней задачу потом обновляют из источника
                    ImportUrl = TaskService.NormalizeImportUrl(issue.Url),
                };
                var created = _tasks.Create(task, DescriptionOf(issue), "", actorId);
                comments += ImportChat(created.Id, issue.Comments, actorId);
                added++;
            }
            else if (updateExisting)
            {
                existing.Title = issue.Title;
                existing.DueDate = issue.Due;
                existing.ExternalRef = externalRef;
                // адрес задачи GitHub отдаёт всегда, но заданное человеком значение
                // пустотой не затираем — правило то же, что у Trello (T-246)
                existing.ImportUrl = TaskService.NormalizeImportUrl(issue.Url) ?? existing.ImportUrl;
                _tasks.Update(existing, DescriptionOf(issue), _tasks.ReadAcceptance(existing), actorId);
                // повторный импорт добавляет только НОВЫЕ сообщения обсуждения (T-152)
                comments += ImportChat(existing.Id, issue.Comments, actorId);
                updated++;
            }
            else
            {
                skipped++;
            }
        }
        return new ImportResult(added, updated, skipped, comments);
    }

    /// <summary>Внешний ключ задачи GitHub: глобальный id задачи, а не её номер — номер
    /// уникален только внутри репозитория, и задачи двух репозиториев склеились бы.</summary>
    public static string ExternalRefOf(string issueId) => $"github:{issueId}";

    /// <summary>Обсуждение задачи GitHub → чат задачи AI2P (T-152): автор — «github:&lt;логин&gt;»,
    /// время — время комментария в GitHub. Ничего не переносится, если чат не подставлен
    /// или неизвестен актор (колонка from_executor_id пустой быть не может).</summary>
    public int ImportChat(string taskId, IReadOnlyList<IssueComment>? comments, string? actorId)
    {
        if (_chat is null || comments is not { Count: > 0 })
        {
            return 0;
        }
        if (string.IsNullOrEmpty(actorId))
        {
            Logger.Warning("Импорт GitHub: обсуждение задачи ({Count} сообщ.) не перенесено — " +
                           "неизвестен исполнитель, от чьего имени импортируют", comments.Count);
            return 0;
        }
        var moved = _chat.Import(taskId, actorId, ChatOf(comments));
        if (moved > 0)
        {
            Logger.Information("Импорт GitHub: в чат задачи перенесено сообщений обсуждения {Count}", moved);
        }
        return moved;
    }

    /// <summary>Комментарии задачи в виде сообщений чата: автор — «github:&lt;логин&gt;»,
    /// внешний id — «github:&lt;id комментария&gt;», порядок — хронологический.</summary>
    public static List<ChatService.ImportedMessage> ChatOf(IEnumerable<IssueComment> comments) =>
        comments.OrderBy(c => c.Date)
            .Select(c => new ChatService.ImportedMessage($"github:{c.Id}",
                // автора GitHub не отдал (аккаунт удалён) — «?»: сообщение всё равно
                // принадлежит GitHub, и это должно быть видно
                $"github:{(c.Author.Length > 0 ? c.Author : "?")}", c.Text, c.Date))
            .ToList();

    /// <summary>Описание задачи из задачи GitHub: текст задачи + приписка-источник
    /// (репозиторий и ссылка на задачу).</summary>
    public static string DescriptionOf(Issue issue)
    {
        var body = issue.Body.Trim();
        var footer = Loc.T("msg.githubImporter.4", issue.Repo, issue.Number, issue.Url);
        return body.Length > 0 ? body + "\n\n---\n" + footer : footer;
    }

    // --- импорт одиночной задачи по ссылке ---

    /// <summary>Номер задачи из ссылки https://github.com/&lt;владелец&gt;/&lt;репозиторий&gt;/issues/&lt;номер&gt;;
    /// null — это не ссылка на задачу GitHub. Хвост (#issuecomment-…, ?query) отбрасывается.</summary>
    public static IssueRef? ParseIssueUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }
        var marker = url.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }
        var rest = url[(marker + "github.com/".Length)..];
        var cut = rest.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            rest = rest[..cut];
        }
        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4
            || !parts[2].Equals("issues", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[3], out var number) || number <= 0)
        {
            return null;
        }
        return new IssueRef(parts[0], parts[1], number);
    }

    /// <summary>Умеет ли обновление разобрать эту ссылку импорта (T-246): по ней выбирается
    /// процедура обновления задачи.</summary>
    public static bool CanRefresh(string? importUrl) =>
        importUrl is { Length: > 0 } && ParseIssueUrl(importUrl) is not null;

    /// <summary>
    /// Импорт содержимого одной задачи GitHub по ссылке: текст задачи ложится в описание,
    /// обсуждение возвращается отдельно (в чат оно уезжает после того, как задача создана).
    /// Задача здесь НЕ создаётся: результат подставляется в форму (UI) или используется
    /// инструментом агента import_task_from_url.
    /// </summary>
    public async Task<ImportedIssue> ImportIssueContentAsync(string url, string? sourceId,
        CancellationToken ct = default)
    {
        var issueRef = ParseIssueUrl(url)
                       ?? throw new InvalidOperationException(Loc.T("msg.githubImporter.5"));
        var p = ResolveSourceParams(sourceId);
        var auth = ResolveAuth(p);

        Logger.Information("Импорт задачи GitHub по URL: {Url}", url);
        Issue issue;
        try
        {
            var json = await GetAsync(IssueApiUrl(issueRef), auth, ct);
            issue = ParseIssue(json, issueRef.FullName)
                    ?? throw new InvalidOperationException(Loc.T("msg.githubImporter.5"));
            issue = issue with { Comments = await FetchCommentsAsync(issueRef, issue, auth, ct) };
        }
        catch (GitHubApiException ex)
        {
            throw Explained(ex, auth);
        }

        var comments = issue.Comments ?? [];
        Logger.Information("Импорт задачи GitHub: «{Title}» ({Repo} #{Number}), " +
                           "сообщений обсуждения {Comments}",
            issue.Title, issue.Repo, issue.Number, comments.Count);
        return new ImportedIssue(issue.Title, DescriptionOf(issue), issue.Due,
            ExternalRefOf(issue.Id), comments,
            issue.Url.Trim().Length > 0 ? issue.Url.Trim() : url.Trim());
    }

    // --- обновление УЖЕ СОЗДАННОЙ задачи из источника импорта (T-246) ---

    /// <summary>
    /// ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ GITHUB. Задача GitHub перечитывается по ссылке импорта,
    /// сохранённой в самой задаче AI2P, и её содержимое ложится поверх: заголовок, срок
    /// (дата вехи) и описание. НОВЫЕ сообщения обсуждения доезжают в чат, уже перенесённые
    /// не дублируются (внешний id комментария). Критерии приёмки, исполнители, статус и связи
    /// не трогаются вовсе — их ведут здесь, а не в GitHub.
    ///
    /// Описание переписывается ЦЕЛИКОМ — ровно так же, как это делает массовый импорт
    /// с галочкой «обновить существующие», и так же, как обновление из Trello (T-246).
    /// </summary>
    public async Task<RefreshResult> RefreshTaskAsync(TaskItem task, string? sourceId,
        string? actorId, CancellationToken ct = default)
    {
        var url = TaskService.NormalizeImportUrl(task.ImportUrl)
                  ?? throw new InvalidOperationException(Loc.T("msg.githubImporter.20", task.DisplayId));
        if (ParseIssueUrl(url) is null)
        {
            throw new InvalidOperationException(Loc.T("msg.githubImporter.21", url));
        }

        Logger.Information("Обновление задачи {Task} из GitHub по ссылке {Url}", task.DisplayId, url);
        var imported = await ImportIssueContentAsync(url, sourceId, ct);

        task.Title = imported.Title;
        task.DueDate = imported.Due;
        // внешний ключ мог быть не проставлен (задачу завели руками и указали ссылку) —
        // после обновления он есть, и повторный массовый импорт дубля не создаст
        task.ExternalRef = imported.ExternalRef;
        task.ImportUrl = TaskService.NormalizeImportUrl(imported.Url) ?? url;
        _tasks.Update(task, imported.Description, _tasks.ReadAcceptance(task), actorId);
        var comments = ImportChat(task.Id, imported.Comments, actorId);
        // и ОБРАТНОЕ направление (T-107-S0): результаты задачи — записями в конец обсуждения
        var exported = await ExportResultsAsync(task, url, imported.Comments, sourceId, ct);

        Logger.Information("Обновление задачи {Task} из GitHub: «{Title}», "
                           + "новых сообщений обсуждения {Comments}, выложено результатов {Exported}",
            task.DisplayId, task.Title, comments, exported);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.ImportRun,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                kind = "refresh",
                source = Kind,
                url = task.ImportUrl,
                comments,
                exported,
            }),
        });
        return new RefreshResult(task.DisplayId, task.Title, comments, exported);
    }

    /// <summary>
    /// РЕЗУЛЬТАТЫ ЗАДАЧИ — В ОБСУЖДЕНИЕ ЗАДАЧИ GITHUB (T-107-S0). Каждый результат уезжает
    /// своей записью в конец обсуждения; уже выложенное узнаётся по маркеру в тексте
    /// комментариев, которые тот же сеанс обновления только что перечитал. Технические
    /// записи (ожидание сброса лимита исполнителя и прочие ошибки) не уезжают вовсе —
    /// см. <see cref="ResultExport"/>. Отказ GitHub на выкладке обновление не рвёт.
    /// </summary>
    private async Task<int> ExportResultsAsync(TaskItem task, string url,
        IReadOnlyList<IssueComment> discussion, string? sourceId, CancellationToken ct)
    {
        if (ParseIssueUrl(url) is not { } issueRef)
        {
            return 0;
        }
        var pending = ResultExport.Pending(_tasks, task, discussion.Select(c => (string?)c.Text));
        if (pending.Count == 0)
        {
            return 0;
        }
        var auth = ResolveAuth(ResolveSourceParams(sourceId));
        var posted = 0;
        foreach (var piece in pending)
        {
            try
            {
                await PostCommentAsync(issueRef, ResultExport.Compose(task.DisplayId, piece), auth, ct);
                posted++;
            }
            catch (Exception ex) when (ex is GitHubApiException or HttpRequestException
                                           or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warning("Выкладка результата {Result} задачи {Task} в GitHub не удалась — {Reason}",
                    piece.Name, task.DisplayId, ex.Message);
                break;
            }
        }
        if (posted > 0)
        {
            Logger.Information("Задача {Task}: в обсуждение задачи GitHub выложено результатов {Count}",
                task.DisplayId, posted);
        }
        return posted;
    }

    /// <summary>Запись в обсуждение задачи GitHub: тело — JSON с полем body.</summary>
    private async Task PostCommentAsync(IssueRef issueRef, string text, Auth auth,
        CancellationToken ct)
    {
        var url = $"{IssueApiUrl(issueRef)}/comments";
        Logger.Information("GitHub API: POST {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = text }),
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {auth.Token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("GitHub API: комментарий не принят, HTTP {Status}: {Body}",
                (int)response.StatusCode, preview);
            throw new GitHubApiException((int)response.StatusCode, preview);
        }
    }

    // --- HTTP: репозитории, задачи, комментарии ---

    /// <summary>Репозитории владельца → открытые задачи репозиториев, прошедших фильтр
    /// по имени (подстрока, регистронезависимо; пустой фильтр — все репозитории).</summary>
    private async Task<List<Issue>> FetchIssuesAsync(string login, string filter, Auth auth,
        CancellationToken ct)
    {
        var repos = await FetchReposAsync(login, filter, ct, auth);
        var issues = new List<Issue>();
        foreach (var repo in repos)
        {
            var (owner, name) = ParseFullName(repo, login);
            for (var page = 1; page <= MaxPages; page++)
            {
                var json = await GetAsync(
                    $"{BaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}" +
                    $"/issues?state=open&per_page={PerPage}&page={page}", auth, ct);
                issues.AddRange(ParseIssues(json, repo));
                // конец списка считаем по ПРИШЕДШИМ записям, а не по разобранным:
                // запросы на слияние отбрасываются, и неполной страница выглядела бы
                // всякий раз, когда среди задач попался PR
                if (CountOf(json) < PerPage)
                {
                    break;
                }
            }
        }
        // обсуждение — отдельным запросом на задачу (у GitHub оно не приезжает вместе
        // с задачей, в отличие от Trello); спрашиваем только там, где комментарии есть
        for (var i = 0; i < issues.Count; i++)
        {
            var (owner, name) = ParseFullName(issues[i].Repo, login);
            issues[i] = issues[i] with
            {
                Comments = await FetchCommentsAsync(
                    new IssueRef(owner, name, issues[i].Number), issues[i], auth, ct),
            };
        }
        return issues;
    }

    /// <summary>Репозитории владельца, отфильтрованные по подстроке имени. У СВОЕГО аккаунта
    /// (токен выдан ему же) спрашиваем /user/repos — иначе приватные репозитории не видны
    /// вовсе; у чужого владельца и у организации — /users/{login}/repos.</summary>
    private async Task<List<string>> FetchReposAsync(string login, string filter,
        CancellationToken ct, Auth auth)
    {
        var me = await WhoAmIAsync(auth, ct);
        var mine = me.Length > 0 && me.Equals(login, StringComparison.OrdinalIgnoreCase);
        var repos = new List<string>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var url = mine
                ? $"{BaseUrl}/user/repos?affiliation=owner&per_page={PerPage}&page={page}&sort=full_name"
                : $"{BaseUrl}/users/{Uri.EscapeDataString(login)}/repos?per_page={PerPage}&page={page}&sort=full_name";
            var json = await GetAsync(url, auth, ct);
            var portion = ParseRepos(json, filter);
            repos.AddRange(portion.Names);
            if (portion.Total < PerPage)
            {
                break;
            }
        }
        Logger.Information("Импорт GitHub: репозиториев по фильтру «{Filter}» — {Count} ({Mode})",
            filter, repos.Count, mine ? "свой аккаунт" : "чужой аккаунт или организация");
        return repos;
    }

    /// <summary>Обсуждение задачи (страницами): спрашивается, только когда GitHub сказал,
    /// что комментарии есть — у задачи без обсуждения лишнего запроса не будет.</summary>
    private async Task<List<IssueComment>> FetchCommentsAsync(IssueRef issueRef, Issue issue,
        Auth auth, CancellationToken ct)
    {
        if (issue.Comments is { Count: > 0 })
        {
            return issue.Comments;
        }
        if (issue.CommentCount == 0)
        {
            return [];
        }
        var comments = new List<IssueComment>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var json = await GetAsync(
                $"{BaseUrl}/repos/{Uri.EscapeDataString(issueRef.Owner)}/{Uri.EscapeDataString(issueRef.Repo)}" +
                $"/issues/{issueRef.Number}/comments?per_page={PerPage}&page={page}", auth, ct);
            var portion = ParseComments(json);
            comments.AddRange(portion);
            if (CountOf(json) < PerPage)
            {
                break;
            }
        }
        comments.Sort((a, b) => a.Date.CompareTo(b.Date));
        return comments;
    }

    private static string IssueApiUrl(IssueRef issueRef) =>
        $"{BaseUrl}/repos/{Uri.EscapeDataString(issueRef.Owner)}/{Uri.EscapeDataString(issueRef.Repo)}" +
        $"/issues/{issueRef.Number}";

    /// <summary>«владелец/репозиторий» → части; имя без владельца — владелец из источника.</summary>
    public static (string Owner, string Repo) ParseFullName(string fullName, string fallbackOwner)
    {
        var slash = fullName.IndexOf('/');
        return slash > 0
            ? (fullName[..slash], fullName[(slash + 1)..])
            : (fallbackOwner, fullName);
    }

    // --- разбор ответов GitHub (отделено от HTTP — тестируемо) ---

    /// <summary>Полные имена репозиториев из ответа GitHub, отобранные по подстроке имени;
    /// Total — сколько записей пришло всего (по нему видно, есть ли следующая страница).</summary>
    public static (List<string> Names, int Total) ParseRepos(string json, string filter)
    {
        var names = new List<string>();
        var total = 0;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return (names, 0);
        }
        foreach (var repo in doc.RootElement.EnumerateArray())
        {
            total++;
            var name = Str(repo, "name");
            var fullName = Str(repo, "full_name");
            if (name.Length == 0)
            {
                continue;
            }
            if (filter.Trim().Length == 0
                || name.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                names.Add(fullName.Length > 0 ? fullName : name);
            }
        }
        return (names, total);
    }

    /// <summary>Задачи репозитория из ответа GitHub. ЗАПРОСЫ НА СЛИЯНИЕ ПРОПУСКАЮТСЯ:
    /// в API GitHub pull request — это тоже issue, и без отбора по полю pull_request
    /// импорт затащил бы в проект все открытые PR репозитория.</summary>
    public static List<Issue> ParseIssues(string json, string repoFullName)
    {
        var result = new List<Issue>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (ParseIssue(element, repoFullName) is { } issue)
            {
                result.Add(issue);
            }
        }
        return result;
    }

    /// <summary>Одна задача из ответа GitHub (JSON объекта); null — это не задача
    /// (запрос на слияние либо запись без обязательных полей).</summary>
    public static Issue? ParseIssue(string json, string repoFullName)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseIssue(doc.RootElement, repoFullName);
    }

    private static Issue? ParseIssue(JsonElement element, string repoFullName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        // запрос на слияние: у него есть поле pull_request, у задачи его нет
        if (element.TryGetProperty("pull_request", out var pr) && pr.ValueKind == JsonValueKind.Object)
        {
            return null;
        }
        var id = Num(element, "id");
        var number = element.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number
            ? n.GetInt32()
            : 0;
        var title = Str(element, "title");
        if (id.Length == 0 || number <= 0 || title.Length == 0)
        {
            return null;
        }
        // репозиторий: у задачи, приехавшей списком репозитория, поля repository нет —
        // берём то имя, по которому спрашивали; у одиночной задачи оно есть
        var repo = repoFullName;
        if (element.TryGetProperty("repository", out var repoElement)
            && repoElement.ValueKind == JsonValueKind.Object
            && Str(repoElement, "full_name") is { Length: > 0 } full)
        {
            repo = full;
        }
        // срока у задачи GitHub нет — берём дату вехи, если она задана
        DateTime? due = null;
        if (element.TryGetProperty("milestone", out var milestone)
            && milestone.ValueKind == JsonValueKind.Object)
        {
            due = Date(milestone, "due_on");
        }
        var commentCount = element.TryGetProperty("comments", out var c)
                           && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32()
            : 0;
        return new Issue(id, number, title, Str(element, "body"), due, repo,
            Str(element, "html_url"))
        {
            CommentCount = commentCount,
        };
    }

    /// <summary>Комментарии задачи из ответа GitHub (массив), в хронологическом порядке.</summary>
    public static List<IssueComment> ParseComments(string json)
    {
        var result = new List<IssueComment>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var id = Num(element, "id");
            var text = Str(element, "body");
            if (id.Length == 0 || text.Trim().Length == 0)
            {
                continue;
            }
            var author = "";
            if (element.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                author = Str(user, "login");
            }
            result.Add(new IssueComment(id, author, text.Trim(),
                Date(element, "created_at") ?? DateTime.UtcNow));
        }
        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return result;
    }

    /// <summary>Сколько записей в массиве ответа — по нему видно, была ли страница полной.</summary>
    private static int CountOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.GetArrayLength()
            : 0;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    /// <summary>Числовой id GitHub строкой: id задач и комментариев — числа, а внешним
    /// ключом они хранятся текстом.</summary>
    private static string Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.String => v.GetString()!,
                _ => "",
            }
            : "";

    private static DateTime? Date(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), null,
            System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    // --- источник, токен и разбор отказов GitHub ---

    /// <summary>Параметры источника: по id, а без него — единственный активный
    /// github-источник.</summary>
    private SourceParams ResolveSourceParams(string? sourceId)
    {
        ImportSource? source;
        if (sourceId is { Length: > 0 })
        {
            source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.githubImporter.1"));
        }
        else
        {
            var github = _sources.List().Where(s => s.IsActive && IsGitHub(s)).ToList();
            source = github.Count switch
            {
                0 => throw new InvalidOperationException(Loc.T("msg.githubImporter.6")),
                1 => github[0],
                _ => throw new InvalidOperationException(Loc.T("msg.githubImporter.7")),
            };
        }
        EnsureGitHub(source);
        return SourceParams.Parse(source.ParamsJson);
    }

    /// <summary>Вид записи справочника — github.</summary>
    public static bool IsGitHub(ImportSource source) =>
        string.Equals(source.Kind, Kind, StringComparison.OrdinalIgnoreCase);

    private static void EnsureGitHub(ImportSource source)
    {
        if (!IsGitHub(source))
        {
            throw new InvalidOperationException(Loc.T("msg.githubImporter.2", source.Kind));
        }
    }

    /// <summary>Токен источника вместе с тем, ОТКУДА он взят: источник нужен в сообщении
    /// об ошибке — без него непонятно, чей токен GitHub отверг (приём T-134).</summary>
    public sealed record Auth(string Token, string TokenRef, string TokenSource);

    private Auth ResolveAuth(SourceParams p)
    {
        var (value, source) = KeyResolver is not null
            ? KeyResolver(p.TokenRef)
            : _secrets.ResolveWithSource(p.TokenRef);
        if (string.IsNullOrWhiteSpace(value))
        {
            Logger.Warning("Импорт GitHub: значение {SecretRef} не найдено ({Source})",
                p.TokenRef, source);
            throw new InvalidOperationException(
                Loc.T("msg.githubImporter.8", p.TokenRef, _secrets.LocationOf(p.TokenRef),
                    SecretStore.EnvNameOf(p.TokenRef)));
        }
        // пробелы и перевод строки по краям — обычное дело при копировании значения в файл
        // секретов; GitHub отвечает на них «Bad credentials» (приём T-134)
        var token = value.Trim();
        var auth = new Auth(token, p.TokenRef, source);
        Logger.Information("GitHub: токен {TokenRef} — {TokenSource}, {TokenShape}",
            p.TokenRef, source, Shape(token));
        return auth;
    }

    /// <summary>Токен GitHub: нынешние начинаются с ghp_ / github_pat_ (и родня — gho_, ghu_,
    /// ghs_, ghr_), прежние были 40 шестнадцатеричными знаками.</summary>
    public static bool LooksLikeToken(string value)
    {
        value = value.Trim();
        foreach (var prefix in TokenPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length + 8)
            {
                return true;
            }
        }
        return value.Length == 40 && value.All(Uri.IsHexDigit);
    }

    private static readonly string[] TokenPrefixes =
        ["ghp_", "github_pat_", "gho_", "ghu_", "ghs_", "ghr_"];

    /// <summary>«Форма» значения для сообщений и логов: длина и префикс, но НИКОГДА само
    /// значение — по ней видно, что в поле токена оказался, например, ключ Trello.</summary>
    public static string Shape(string value)
    {
        value = value.Trim();
        foreach (var prefix in TokenPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Loc.T("msg.githubImporter.9", value.Length, prefix);
            }
        }
        return Loc.T("msg.githubImporter.10", value.Length);
    }

    /// <summary>Замечание к введённому токену: пусто — значение похоже на то, что просят.
    /// Проверка МЯГКАЯ — она подсказывает, а не запрещает (приём T-134): вид токенов
    /// задаёт GitHub, и запрет однажды закрыл бы вход вполне рабочему значению.</summary>
    public static string SecretHint(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || LooksLikeToken(value))
        {
            return "";
        }
        return Loc.T("msg.githubImporter.11", Shape(value), KeyPage);
    }

    /// <summary>Ответ GitHub не 2xx — отдельный тип, чтобы разобрать причину и объяснить
    /// её человеку там, где известно, чей токен уехал в запрос (приём T-134).</summary>
    public sealed class GitHubApiException(int status, string body)
        : InvalidOperationException(Loc.T("msg.githubImporter.12", status, body))
    {
        public int Status { get; } = status;

        public string Body { get; } = body;
    }

    /// <summary>Отказ GitHub человеческими словами: что именно не принято, откуда взят
    /// токен и что с этим делать.</summary>
    public static string Explain(GitHubApiException ex, Auth auth)
    {
        var body = ex.Body.Trim();
        var lower = body.ToLowerInvariant();
        if (ex.Status == 401 || lower.Contains("bad credentials"))
        {
            var hint = SecretHint(auth.Token);
            return Loc.T("msg.githubImporter.13", ex.Status, body, auth.TokenRef, auth.TokenSource,
                       Shape(auth.Token))
                   + (hint.Length > 0 ? hint + " " : "")
                   + Loc.T("msg.githubImporter.14", KeyPage);
        }
        if (lower.Contains("rate limit") || ex.Status == 429)
        {
            return Loc.T("msg.githubImporter.15");
        }
        if (ex.Status == 403)
        {
            return Loc.T("msg.githubImporter.16", ex.Status, body);
        }
        if (ex.Status == 404)
        {
            return Loc.T("msg.githubImporter.17", body);
        }
        return Loc.T("msg.githubImporter.12", ex.Status, body);
    }

    /// <summary>Объяснение отказа — в лог И в ответ (приём T-134): уровень вывода в лог
    /// по умолчанию «предупреждения», и подробности запроса до него не доходят.</summary>
    private static InvalidOperationException Explained(GitHubApiException ex, Auth auth)
    {
        var message = Explain(ex, auth);
        Logger.Warning("Импорт GitHub отклонён: {Reason}", message);
        return new InvalidOperationException(message, ex);
    }

    /// <summary>
    /// ПРОВЕРКА ПОДКЛЮЧЕНИЯ источника: кнопка формы источника. GitHub спрашивают «кто я» —
    /// этого хватает, чтобы отделить негодный токен от нехватки прав, не заводя ни одной
    /// задачи. Исключений не бросает: ответ — вердикт.
    /// </summary>
    public async Task<(bool Ok, string Message)> CheckAsync(string? sourceId,
        CancellationToken ct = default)
    {
        SourceParams p;
        Auth auth;
        try
        {
            p = ResolveSourceParams(sourceId);
            auth = ResolveAuth(p);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning("Проверка подключения GitHub: {Reason}", ex.Message);
            return (false, ex.Message);
        }

        try
        {
            var login = await WhoAmIAsync(auth, ct);
            var message = Loc.T("msg.githubImporter.18", login);
            if (p.Login.Length > 0 && !p.Login.Equals(login, StringComparison.OrdinalIgnoreCase))
            {
                message += Loc.T("msg.githubImporter.19", p.Login, login);
            }
            Logger.Information("Проверка подключения GitHub: аккаунт @{Login}", login);
            return (true, message);
        }
        catch (GitHubApiException ex)
        {
            return (false, Explained(ex, auth).Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "Проверка подключения GitHub не удалась");
            return (false, Loc.T("msg.githubImporter.22", ex.Message));
        }
    }

    /// <summary>Логин аккаунта, которому выдан токен.</summary>
    private async Task<string> WhoAmIAsync(Auth auth, CancellationToken ct)
    {
        var json = await GetAsync($"{BaseUrl}/user", auth, ct);
        using var doc = JsonDocument.Parse(json);
        return Str(doc.RootElement, "login");
    }

    private async Task<string> GetAsync(string url, Auth auth, CancellationToken ct)
    {
        Logger.Information("GitHub API: GET {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // токен уезжает ЗАГОЛОВКОМ, а не строкой запроса: строку запроса пишут в лог
        // и прокси, и сам GitHub
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {auth.Token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("GitHub API: {Url} → HTTP {Status}: {Body}",
                url, (int)response.StatusCode, preview);
            throw new GitHubApiException((int)response.StatusCode, preview);
        }
        return body;
    }
}
