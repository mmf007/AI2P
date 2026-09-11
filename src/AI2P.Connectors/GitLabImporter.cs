using AI2P.Core;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ИМПОРТ ЗАДАЧ ИЗ GITLAB (T-249, ТЗ v1.89) — второй вид источника справочника импортов
/// после Trello (T-134, T-152, T-246) и брат импорта из GitHub (T-247).
///
/// Единица импорта — <b>issue</b> (задача GitLab). Из неё берётся:
/// <list type="bullet">
///   <item>САМА СТАТЬЯ — текст issue ложится в описание задачи вместе с приложенными
///     файлами: GitLab не отдаёт список вложений отдельным полем, они живут ссылками
///     <c>/uploads/&lt;хэш&gt;/&lt;имя&gt;</c> внутри самого текста, поэтому файлы
///     вынимаются из текста, скачиваются в хранилище проекта, а ссылки переписываются
///     на местные (<see cref="ImportAttachmentsAsync"/>);</item>
///   <item>ОБСУЖДЕНИЕ — заметки issue (<c>notes</c>) становятся сообщениями чата задачи,
///     как это сделано для Trello (T-152); служебные заметки GitLab («изменил метку»,
///     «назначил») пропускаются — это не разговор людей;</item>
///   <item>заголовок и срок (<c>due_date</c>).</item>
/// </list>
///
/// Отличия от Trello, из которых следует всё остальное:
/// <list type="number">
///   <item>у GitLab ОДНО значение секрета — токен (personal access token, право
///     <c>read_api</c>); ключа API нет вовсе, поэтому источник вида gitlab активен,
///     когда введён токен (<see cref="ImportKeyService"/>);</item>
///   <item>GitLab бывает не только <c>gitlab.com</c>: адрес сервера — параметр источника
///     (<c>host</c>), и он же участвует во внешнем ключе задачи, иначе две установки
///     GitLab с одинаковыми номерами задач слились бы в одну;</item>
///   <item>токен уезжает ЗАГОЛОВКОМ <c>PRIVATE-TOKEN</c>, а не строкой запроса —
///     значит, он не попадает ни в один лог по дороге.</item>
/// </list>
/// </summary>
public sealed class GitLabImporter
{
    /// <summary>Код вида источника в справочнике импортов.</summary>
    public const string Kind = "gitlab";

    /// <summary>Адрес сервера по умолчанию — облачный GitLab.</summary>
    public const string DefaultHost = "https://gitlab.com";

    /// <summary>Страница, где выпускают токен, — она же названа в документе вида источника;
    /// в сообщении об отказе ссылка важнее любых слов (приём T-134).</summary>
    public const string KeyPage = "https://gitlab.com/-/user_settings/personal_access_tokens";

    /// <summary>Ссылка на токен, если её нет в параметрах источника. Своя ссылка у каждой
    /// записи заводится формой (<c>ImportSource.DefaultRefs</c>); эта — на случай источника,
    /// заведённого мимо формы (например, руками в базе): без неё токен искать негде.
    /// Ссылку Trello (<c>trello.token</c>) здесь брать НЕЛЬЗЯ — это чужой токен.</summary>
    public const string FallbackTokenRef = "gitlab.token";

    /// <summary>Предел размера скачиваемого приложенного файла — как у Trello и у загрузки
    /// в MD-редакторе.</summary>
    private const long MaxAttachmentBytes = 200 * 1024 * 1024;

    /// <summary>Сколько заметок обсуждения забирается за раз (предел страницы GitLab — 100).</summary>
    private const int PageSize = 100;

    /// <summary>Сколько страниц берётся у списочных запросов: 20 × 100 — десять тысяч задач
    /// проекта и тысяча сообщений одного обсуждения; дальше — защита от бесконечного круга,
    /// если сервер вернёт непустую страницу на любой номер.</summary>
    private const int MaxPages = 20;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly TaskService _tasks;
    private readonly ImportSourceService _sources;
    private readonly SecretStore _secrets;
    private readonly EventStore _events;
    private readonly FileStore _files;
    private readonly ChatService? _chat;

    /// <param name="chat">Чат задач: обсуждение issue переносится в чат задачи;
    /// null — заметки не переносятся (массовый импорт в тестах без чата).</param>
    public GitLabImporter(TaskService tasks, ImportSourceService sources, SecretStore secrets,
        EventStore events, FileStore files, ChatService? chat = null)
    {
        _tasks = tasks;
        _sources = sources;
        _secrets = secrets;
        _events = events;
        _files = files;
        _chat = chat;
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(GitLabImporter));

    /// <summary>Откуда брать токен: подставляется контекстом организации
    /// (<c>ModelKeyService.ResolveWithSource</c>) — значения лежат в БД организации
    /// зашифрованными. Не подставлен — читается secrets.json, как у Trello.</summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>Подменный HttpClient — только для тестов: живой GitLab в прогоне не дёргается.</summary>
    public HttpClient? HttpOverride { get; set; }

    private HttpClient Client => HttpOverride ?? Http;

    // ---------------------------------------------------------------- данные

    /// <summary>Заметка обсуждения issue: внутренний id (защита от дублей при повторном
    /// импорте), логин автора В GITLAB, текст и время.</summary>
    public sealed record IssueNote(string Id, string Author, string Text, DateTime Date);

    /// <summary>
    /// Issue GitLab (нужные импорту поля). <paramref name="Iid"/> — НОМЕР задачи внутри
    /// проекта (тот самый «#42», что виден человеку), <paramref name="ProjectId"/> —
    /// внутренний ключ проекта: вместе с адресом сервера они образуют внешний ключ задачи.
    /// Notes — обсуждение в хронологическом порядке; пусто — заметок нет или их не просили.
    /// </summary>
    public sealed record Issue(string Id, long Iid, string ProjectId, string Title, string Body,
        DateTime? Due, string ProjectPath, string WebUrl, List<IssueNote>? Notes = null);

    /// <summary>Итог импорта: добавлено / обновлено / пропущено и сколько сообщений
    /// обсуждения перенесено в чат — всё для сообщения в UI.</summary>
    public sealed record ImportResult(int Added, int Updated, int Skipped, int Comments = 0);

    /// <summary>Итог обновления задачи из источника (та же тройка, что у Trello, T-246).
    /// Exported (T-107-S0) — сколько результатов задачи уехало ОБРАТНО, записями
    /// в обсуждение issue.</summary>
    public sealed record RefreshResult(string DisplayId, string Title, int Comments, int Files,
        int Exported = 0);

    /// <summary>Содержимое одной issue для формы новой задачи или для обновления уже
    /// созданной: файлы уже скачаны в uploads/ проекта, ссылки на них — прямо в описании.
    /// Url — канонический адрес issue (<c>web_url</c>), он ложится в задачу ссылкой импорта
    /// (T-246), и по нему задачу потом обновляют.</summary>
    public sealed record ImportedIssue(string Title, string Description, DateTime? Due,
        string ExternalRef, List<string> Files, List<IssueNote> Notes, string Url = "");

    /// <summary>Разобранный адрес issue: сервер, путь проекта и номер задачи в проекте.</summary>
    public sealed record IssueRef(string Host, string ProjectPath, long Iid)
    {
        /// <summary>Путь проекта в том виде, в каком его ждёт API GitLab: он передаётся
        /// одним сегментом, поэтому косые черты внутри пути кодируются (<c>%2F</c>).</summary>
        public string ApiProject => Uri.EscapeDataString(ProjectPath);
    }

    /// <summary>
    /// Параметры источника (params_json записи справочника импортов). host — адрес сервера
    /// GitLab (пусто — облачный gitlab.com), project — путь проекта вида
    /// <c>группа/проект</c> для массового импорта, labels — необязательный отбор по меткам
    /// (через запятую), tokenRef — ссылка на токен в хранилище секретов.
    /// </summary>
    public sealed record SourceParams(string Host, string Project, string Labels, string TokenRef)
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
            return new SourceParams(NormalizeHost(Str("host")), Str("project"), Str("labels"),
                tokenRef.Length > 0 ? tokenRef : FallbackTokenRef);
        }
    }

    /// <summary>Токен источника вместе с тем, ОТКУДА он взят: без источника непонятно, чей
    /// токен GitLab отверг (приём T-134). Само значение в лог не попадает никогда.</summary>
    public sealed record Auth(string Token, string TokenRef, string TokenSource, string Host);

    /// <summary>Ответ GitLab не 2xx — отдельный тип, чтобы разобрать причину и объяснить её
    /// человеку там, где известно, чей токен уехал в запрос (приём T-134).</summary>
    public sealed class GitLabApiException(int status, string body)
        : InvalidOperationException(Loc.T("msg.gitlabImporter.1", status, body))
    {
        public int Status { get; } = status;

        public string Body { get; } = body;
    }

    // ------------------------------------------------------- адреса и разбор

    /// <summary>Адрес сервера в одном виде: без хвостовой косой черты, без схемы — значит
    /// https. Пусто — облачный gitlab.com.</summary>
    public static string NormalizeHost(string? host)
    {
        var value = (host ?? "").Trim().TrimEnd('/');
        if (value.Length == 0)
        {
            return DefaultHost;
        }
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }
        return value.TrimEnd('/');
    }

    /// <summary>Имя сервера без схемы и порта — им помечается внешний ключ задачи: две
    /// установки GitLab с одинаковыми номерами задач не должны сливаться в одну.</summary>
    public static string HostNameOf(string host) =>
        Uri.TryCreate(NormalizeHost(host), UriKind.Absolute, out var uri)
            ? uri.Host
            : NormalizeHost(host).Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Разбор ссылки на issue: <c>https://gitlab.com/группа/проект/-/issues/42</c>.
    /// Понимаются подгруппы (путь любой глубины), старый вид без разделителя <c>/-/</c>,
    /// хвосты <c>?</c>, <c>#note_…</c> и адрес своего сервера GitLab. null — не ссылка
    /// на issue.
    /// </summary>
    public static IssueRef? ParseIssueUrl(string? url)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }
        // путь: <любые сегменты пути проекта>[/-]/issues/<номер>
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToList();
        var marker = parts.FindLastIndex(p => p.Equals("issues", StringComparison.OrdinalIgnoreCase));
        if (marker < 0 || marker + 1 >= parts.Count
            || !long.TryParse(parts[marker + 1], out var iid) || iid <= 0)
        {
            return null;
        }
        var path = parts.Take(marker).ToList();
        // разделитель GitLab между путём проекта и его разделами
        var hasSeparator = path.Count > 0 && path[^1] == "-";
        if (hasSeparator)
        {
            path.RemoveAt(path.Count - 1);
        }
        if (path.Count < 2)
        {
            // проект — это минимум «группа/проект»: одиночный сегмент ссылкой на issue
            // проекта быть не может (так выглядит, например, адрес доски группы)
            return null;
        }
        // БЕЗ разделителя «/-/» адрес неотличим от ссылки на issue GitHub
        // (github.com/<владелец>/<репозиторий>/issues/<номер>, T-247), и такую ссылку
        // GitLab забирал бы себе. Поэтому старый вид адреса принимается, только когда
        // сервер назван GitLab: сам GitLab перешёл на «/-/» ещё в 2019 году
        if (!hasSeparator && !uri.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var host = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return new IssueRef(host, string.Join('/', path), iid);
    }

    /// <summary>Умеет ли обновление разобрать эту ссылку импорта (точка выбора процедуры
    /// обновления, T-246): ссылка на issue GitLab — умеет.</summary>
    public static bool CanRefresh(string? importUrl) => ParseIssueUrl(importUrl) is not null;

    /// <summary>
    /// Внешний ключ задачи: <c>gitlab:&lt;сервер&gt;:&lt;проект&gt;:&lt;номер&gt;</c>.
    /// Сервер в ключе обязателен — иначе issue #42 своего GitLab и issue #42 облачного
    /// считались бы одной задачей. Проект — его внутренний ключ, а не путь: проект
    /// переименовывают и переносят между группами, ключ при этом не меняется.
    /// </summary>
    public static string ExternalRefOf(string host, string projectId, long iid) =>
        $"{Kind}:{HostNameOf(host)}:{projectId}:{iid}";

    // ------------------------------------------------------- массовый импорт

    /// <summary>
    /// МАССОВЫЙ ИМПОРТ проекта GitLab (кнопка списка задач): открытые issue проекта,
    /// названного в источнике, становятся задачами текущего проекта AI2P. Чекбоксы —
    /// те же, что у Trello: «добавлять новые» и «обновить существующие».
    /// </summary>
    public async Task<ImportResult> ImportAsync(Project project, string sourceId, bool addNew,
        bool updateExisting, string? actorId, CancellationToken ct = default)
    {
        var source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.gitlabImporter.2"));
        EnsureKind(source);
        var p = SourceParams.Parse(source.ParamsJson);
        if (p.Project.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.gitlabImporter.3"));
        }
        var auth = ResolveAuth(p);

        Logger.Information("Импорт GitLab: источник {Source}, сервер {Host}, проект {Repo}, "
                           + "метки «{Labels}», проект AI2P {Project}, новые {AddNew}, обновление {Update}",
            source.Name, p.Host, p.Project, p.Labels, project.Name, addNew, updateExisting);
        List<Issue> issues;
        try
        {
            issues = await FetchIssuesAsync(p, auth, ct);
        }
        catch (GitLabApiException ex)
        {
            throw Explained(ex, auth);
        }
        var result = await ApplyAsync(project, issues, addNew, updateExisting, auth, actorId, ct);
        Logger.Information("Импорт GitLab: задач {Issues} → добавлено {Added}, обновлено {Updated}, "
                           + "пропущено {Skipped}, сообщений обсуждения {Comments}",
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
                kind = Kind,
                issues = issues.Count,
                added = result.Added,
                updated = result.Updated,
                skipped = result.Skipped,
                comments = result.Comments,
            }),
        });
        return result;
    }

    /// <summary>
    /// Применить issue к проекту: external_ref есть в проекте → обновление (по чекбоксу),
    /// нет → создание. Приложенные файлы скачиваются здесь же — у GitLab они живут ссылками
    /// внутри текста, поэтому «положить статью в описание» и «забрать файлы» — одно действие
    /// (<paramref name="auth"/> = null — файлы не трогаются, текст ложится как есть; так
    /// удобно проверять разбор без сети).
    /// </summary>
    public async Task<ImportResult> ApplyAsync(Project project, IReadOnlyList<Issue> issues,
        bool addNew, bool updateExisting, Auth? auth, string? actorId, CancellationToken ct = default)
    {
        int added = 0, updated = 0, skipped = 0, comments = 0;
        foreach (var issue in issues)
        {
            var externalRef = ExternalRefOf(
                auth?.Host ?? HostOfUrl(issue.WebUrl), issue.ProjectId, issue.Iid);
            var existing = _tasks.FindByExternalRef(project.Id, externalRef);
            if (existing is null && !addNew)
            {
                skipped++;
                continue;
            }
            if (existing is not null && !updateExisting)
            {
                skipped++;
                continue;
            }
            var (body, files) = auth is null
                ? (issue.Body, new List<string>())
                : await ImportAttachmentsAsync(project, issue, auth, ct);
            var description = DescriptionOf(issue with { Body = body });
            if (existing is null)
            {
                var created = _tasks.Create(new TaskItem
                {
                    ProjectId = project.Id,
                    Title = issue.Title,
                    Status = TaskStatuses.Draft,
                    DueDate = issue.Due,
                    ExternalRef = externalRef,
                    // ссылка импорта (T-246): по ней задачу потом обновляют из источника
                    ImportUrl = TaskService.NormalizeImportUrl(issue.WebUrl),
                }, description, "", actorId);
                comments += ImportChat(created.Id, issue.Notes, actorId);
                added++;
                Logger.Debug("Импорт GitLab: задача {Task} создана из issue {Ref}, файлов {Files}",
                    created.DisplayId, externalRef, files.Count);
            }
            else
            {
                existing.Title = issue.Title;
                existing.DueDate = issue.Due;
                existing.ExternalRef = externalRef;
                // ссылку импорта проставляем и при обновлении: задача могла приехать
                // прошлой версией, когда этого поля ещё не было (T-246)
                existing.ImportUrl = TaskService.NormalizeImportUrl(issue.WebUrl) ?? existing.ImportUrl;
                _tasks.Update(existing, description, _tasks.ReadAcceptance(existing), actorId);
                // повторный импорт добавляет только НОВЫЕ заметки обсуждения (T-152)
                comments += ImportChat(existing.Id, issue.Notes, actorId);
                updated++;
            }
        }
        return new ImportResult(added, updated, skipped, comments);
    }

    /// <summary>Сервер из адреса issue — на случай, если авторизация не подставлена
    /// (разбор без сети): внешний ключ всё равно обязан знать, чей это GitLab.</summary>
    private static string HostOfUrl(string webUrl) =>
        ParseIssueUrl(webUrl) is { } r ? r.Host : DefaultHost;

    /// <summary>
    /// Обсуждение issue → чат задачи (по образцу T-152). Автор — «gitlab:&lt;логин&gt;»,
    /// время — время заметки в GitLab, поэтому хронология источника сохраняется.
    /// </summary>
    public int ImportChat(string taskId, IReadOnlyList<IssueNote>? notes, string? actorId)
    {
        if (_chat is null || notes is not { Count: > 0 })
        {
            return 0;
        }
        if (string.IsNullOrEmpty(actorId))
        {
            Logger.Warning("Импорт GitLab: обсуждение задачи ({Count} сообщ.) не перенесено — "
                           + "неизвестен исполнитель, от чьего имени импортируют", notes.Count);
            return 0;
        }
        var moved = _chat.Import(taskId, actorId, ChatOf(notes));
        if (moved > 0)
        {
            Logger.Information("Импорт GitLab: в чат задачи перенесено сообщений обсуждения {Count}", moved);
        }
        return moved;
    }

    /// <summary>Заметки issue в виде сообщений чата: автор — «gitlab:&lt;логин&gt;»,
    /// внешний id — «gitlab:note:&lt;id&gt;», порядок хронологический.</summary>
    public static List<ChatService.ImportedMessage> ChatOf(IEnumerable<IssueNote> notes) =>
        notes.OrderBy(n => n.Date)
            .Select(n => new ChatService.ImportedMessage($"{Kind}:note:{n.Id}",
                // логин не узнан (участника удалили) — «?»: сообщение всё равно
                // принадлежит GitLab, и это должно быть видно
                $"{Kind}:{(n.Author.Length > 0 ? n.Author : "?")}", n.Text, n.Date))
            .ToList();

    /// <summary>Описание задачи из issue: САМА СТАТЬЯ + приписка-источник со ссылкой.</summary>
    public static string DescriptionOf(Issue issue)
    {
        var footer = Loc.T("msg.gitlabImporter.4", issue.ProjectPath, issue.Iid, issue.WebUrl);
        var body = issue.Body.Trim();
        return body.Length > 0 ? body + "\n\n---\n" + footer : footer;
    }

    // -------------------------------------------- импорт одиночной issue по URL

    /// <summary>
    /// Импорт содержимого ОДНОЙ issue по ссылке: issue читается по адресу, приложенные
    /// файлы скачиваются в uploads/ проекта, обсуждение приезжает вместе с ней. Задача
    /// здесь НЕ создаётся: результат подставляется в форму (UI) или используется
    /// инструментом агента <c>import_task_from_url</c>.
    /// </summary>
    public async Task<ImportedIssue> ImportIssueContentAsync(Project project, string url,
        string? sourceId, CancellationToken ct = default)
    {
        var issueRef = ParseIssueUrl(url)
                       ?? throw new InvalidOperationException(Loc.T("msg.gitlabImporter.5", url));
        var p = ResolveSourceParams(sourceId, issueRef.Host);
        // адрес сервера берётся ИЗ ССЫЛКИ, а не из источника: у одного источника может
        // быть свой gitlab, а человек вставил ссылку на другой — токен тогда не подойдёт,
        // и об этом честно скажет отказ, а не молчаливое чтение чужого сервера
        var auth = ResolveAuth(p with { Host = issueRef.Host });

        Logger.Information("Импорт issue GitLab по URL: {Url}, проект {Project}", url, project.Name);
        Issue issue;
        try
        {
            var json = await GetAsync(
                $"{issueRef.Host}/api/v4/projects/{issueRef.ApiProject}/issues/{issueRef.Iid}", auth, ct);
            issue = ParseIssue(json, issueRef.ProjectPath);
            issue = issue with { Notes = await FetchNotesAsync(issueRef, auth, ct) };
        }
        catch (GitLabApiException ex)
        {
            throw Explained(ex, auth);
        }

        var (body, files) = await ImportAttachmentsAsync(project, issue, auth, ct);
        issue = issue with { Body = body };
        var notes = issue.Notes ?? [];
        Logger.Information("Импорт issue GitLab: «{Title}», скачано файлов {Files}, "
                           + "сообщений обсуждения {Notes}", issue.Title, files.Count, notes.Count);
        return new ImportedIssue(issue.Title, DescriptionOf(issue), issue.Due,
            ExternalRefOf(issueRef.Host, issue.ProjectId, issue.Iid), files, notes,
            issue.WebUrl.Trim().Length > 0 ? issue.WebUrl.Trim() : url.Trim());
    }

    // ------------------------------- обновление уже созданной задачи (T-246)

    /// <summary>
    /// ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ GITLAB — процедура обновления по образцу Trello (T-246).
    /// Issue перечитывается по ссылке импорта, хранящейся в самой задаче, и её содержимое
    /// ложится в задачу поверх: заголовок, срок, описание (вместе со свежими приложенными
    /// файлами — они скачиваются заново) и НОВЫЕ сообщения обсуждения. Уже перенесённые
    /// сообщения чата не дублируются (внешний id заметки); критерии приёмки, исполнители,
    /// статус, приоритет и связи задачи не трогаются вовсе — их ведут здесь, а не в GitLab.
    /// Описание переписывается ЦЕЛИКОМ: обновление из источника затем и запускают.
    /// </summary>
    public async Task<RefreshResult> RefreshTaskAsync(Project project, TaskItem task,
        string? sourceId, string? actorId, CancellationToken ct = default)
    {
        var url = TaskService.NormalizeImportUrl(task.ImportUrl)
                  ?? throw new InvalidOperationException(Loc.T("msg.gitlabImporter.6", task.DisplayId));
        if (ParseIssueUrl(url) is null)
        {
            throw new InvalidOperationException(Loc.T("msg.gitlabImporter.5", url));
        }

        Logger.Information("Обновление задачи {Task} из GitLab по ссылке {Url}", task.DisplayId, url);
        var imported = await ImportIssueContentAsync(project, url, sourceId, ct);

        task.Title = imported.Title;
        task.DueDate = imported.Due;
        // внешний ключ мог быть не проставлен (задачу завели руками и указали ссылку) —
        // после обновления он есть, и повторный массовый импорт дубля не создаст
        task.ExternalRef = imported.ExternalRef;
        // адрес приводим к каноническому виду, каким его называет сам GitLab: человек мог
        // вставить ссылку с хвостом «#note_…»
        task.ImportUrl = TaskService.NormalizeImportUrl(imported.Url) ?? url;
        _tasks.Update(task, imported.Description, _tasks.ReadAcceptance(task), actorId);
        var comments = ImportChat(task.Id, imported.Notes, actorId);
        // и ОБРАТНОЕ направление (T-107-S0): результаты задачи — записями в конец обсуждения
        var exported = await ExportResultsAsync(task, url, imported.Notes, sourceId, ct);

        Logger.Information("Обновление задачи {Task} из GitLab: «{Title}», файлов {Files}, "
                           + "новых сообщений обсуждения {Comments}, выложено результатов {Exported}",
            task.DisplayId, task.Title, imported.Files.Count, comments, exported);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = project.Id,
            TaskId = task.Id,
            EventType = EventTypes.ImportRun,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                kind = "refresh",
                source = Kind,
                url = task.ImportUrl,
                files = imported.Files.Count,
                comments,
                exported,
            }),
        });
        return new RefreshResult(task.DisplayId, task.Title, comments, imported.Files.Count,
            exported);
    }

    /// <summary>
    /// РЕЗУЛЬТАТЫ ЗАДАЧИ — В ОБСУЖДЕНИЕ ISSUE (T-107-S0). Каждый результат уезжает своей
    /// заметкой в конец обсуждения; уже выложенное узнаётся по маркеру в тексте заметок,
    /// которые тот же сеанс обновления только что перечитал. Технические записи (ожидание
    /// сброса лимита исполнителя и прочие ошибки) не уезжают вовсе — см. <see cref="ResultExport"/>.
    /// Отказ GitLab на выкладке обновление не рвёт.
    /// </summary>
    private async Task<int> ExportResultsAsync(TaskItem task, string url,
        IReadOnlyList<IssueNote> discussion, string? sourceId, CancellationToken ct)
    {
        if (ParseIssueUrl(url) is not { } issueRef)
        {
            return 0;
        }
        var pending = ResultExport.Pending(_tasks, task, discussion.Select(n => (string?)n.Text));
        if (pending.Count == 0)
        {
            return 0;
        }
        // токен берётся так же, как при чтении issue: подсказка по адресу сервера отбирает
        // источник нужного GitLab, если их заведено несколько
        var auth = ResolveAuth(ResolveSourceParams(sourceId, issueRef.Host)
            with { Host = issueRef.Host });
        var posted = 0;
        foreach (var piece in pending)
        {
            try
            {
                await PostNoteAsync(issueRef, ResultExport.Compose(task.DisplayId, piece), auth, ct);
                posted++;
            }
            catch (Exception ex) when (ex is GitLabApiException or HttpRequestException
                                           or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warning("Выкладка результата {Result} задачи {Task} в GitLab не удалась — {Reason}",
                    piece.Name, task.DisplayId, ex.Message);
                break;
            }
        }
        if (posted > 0)
        {
            Logger.Information("Задача {Task}: в обсуждение issue GitLab выложено результатов {Count}",
                task.DisplayId, posted);
        }
        return posted;
    }

    /// <summary>Заметка в обсуждение issue: тело — JSON с полем body.</summary>
    private async Task PostNoteAsync(IssueRef issueRef, string text, Auth auth,
        CancellationToken ct)
    {
        var url = $"{issueRef.Host}/api/v4/projects/{issueRef.ApiProject}"
                  + $"/issues/{issueRef.Iid}/notes";
        Logger.Information("GitLab API: POST {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { body = text }),
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", auth.Token);
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("GitLab API: заметка не принята, HTTP {Status}: {Body}",
                (int)response.StatusCode, preview);
            throw new GitLabApiException((int)response.StatusCode, preview);
        }
    }

    // ----------------------------------------------------- приложенные файлы

    /// <summary>
    /// Ссылка на приложенный файл внутри текста GitLab. Отдельного списка вложений у issue
    /// НЕТ: загруженный файл GitLab вставляет в текст markdown-ссылкой на
    /// <c>/uploads/&lt;хэш&gt;/&lt;имя&gt;</c> (относительно проекта). Здесь ловятся и такие
    /// ссылки, и абсолютные на тот же сервер.
    /// </summary>
    private static readonly Regex UploadLink = new(
        @"(?<bang>!?)\[(?<text>[^\]\r\n]*)\]\((?<url>[^)\s]*?/uploads/[^)\s]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Ссылки на приложенные файлы в тексте issue (разбор отделён от сети —
    /// тестируемо): подпись, адрес и признак «это картинка» (ссылка с «!»).</summary>
    public static List<(string Text, string Url, bool IsImage)> ParseAttachmentLinks(string body)
    {
        var result = new List<(string, string, bool)>();
        foreach (Match m in UploadLink.Matches(body ?? ""))
        {
            var url = m.Groups["url"].Value.Trim();
            if (url.Length > 0)
            {
                result.Add((m.Groups["text"].Value, url, m.Groups["bang"].Value == "!"));
            }
        }
        return result;
    }

    /// <summary>
    /// Скачать приложенные файлы issue и переписать ссылки на местные. Возвращает новый
    /// текст статьи и список имён скачанных файлов. Не скачалось (нет прав, файла нет,
    /// файл больше предела) — ссылка становится АБСОЛЮТНОЙ на GitLab: относительная
    /// «/uploads/…» в описании задачи AI2P всё равно никуда не ведёт, а абсолютная
    /// хотя бы открывается тем, у кого есть доступ.
    /// </summary>
    private async Task<(string Body, List<string> Files)> ImportAttachmentsAsync(Project project,
        Issue issue, Auth auth, CancellationToken ct)
    {
        var links = ParseAttachmentLinks(issue.Body);
        if (links.Count == 0)
        {
            return (issue.Body, []);
        }
        var body = issue.Body;
        var files = new List<string>();
        var done = new Dictionary<string, string>(StringComparer.Ordinal);
        // «!» перед ссылкой в тексте не трогаем вовсе: картинка так и остаётся картинкой,
        // меняется только адрес внутри скобок
        foreach (var (text, url, _) in links)
        {
            if (done.ContainsKey(url))
            {
                continue;
            }
            var absolute = AbsoluteUploadUrl(auth.Host, issue.ProjectPath, url);
            var name = UploadFileName(url, text);
            string replacement;
            try
            {
                var bytes = await DownloadAsync(absolute, auth, ct);
                if (bytes is null)
                {
                    replacement = absolute;
                }
                else
                {
                    var rel = _files.SaveUpload(project.Slug, name, bytes);
                    replacement = "api/files/raw?path=" + Uri.EscapeDataString(rel);
                    files.Add(name);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                or IOException or GitLabApiException)
            {
                // файл — не повод ронять импорт всей задачи: статья важнее вложения
                Logger.Warning("Импорт GitLab: файл «{Name}» не скачан ({Reason}) — "
                               + "в описании останется ссылка на GitLab", name, ex.Message);
                replacement = absolute;
            }
            done[url] = replacement;
            body = body.Replace("(" + url + ")", "(" + replacement + ")", StringComparison.Ordinal);
        }
        return (body, files);
    }

    /// <summary>Абсолютный адрес приложенного файла: относительный «/uploads/…» отсчитывается
    /// ОТ ПРОЕКТА, а не от корня сервера — так их отдаёт GitLab.</summary>
    public static string AbsoluteUploadUrl(string host, string projectPath, string url)
    {
        var value = url.Trim();
        if (value.Contains("://", StringComparison.Ordinal))
        {
            return value;
        }
        var tail = value.TrimStart('/');
        // «/-/project/<id>/uploads/…» и прочие адреса от корня сервера — уже полный путь
        return tail.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? $"{NormalizeHost(host)}/{projectPath}/{tail}"
            : $"{NormalizeHost(host)}/{tail}";
    }

    /// <summary>Имя сохраняемого файла: последний сегмент адреса (его GitLab и показывает),
    /// а если там пусто — подпись ссылки.</summary>
    public static string UploadFileName(string url, string text)
    {
        var path = url.Split(['?', '#'], 2)[0].TrimEnd('/');
        var name = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]).Trim();
        if (name.Length == 0)
        {
            name = text.Trim();
        }
        return name.Length > 0 ? name : "file";
    }

    // ------------------------------------------------------------ HTTP GitLab

    /// <summary>Открытые issue проекта источника постранично; отбор по меткам — на стороне
    /// GitLab. Обсуждение каждой задачи спрашивается отдельно: у GitLab его во вложенном
    /// виде не отдают вовсе.</summary>
    private async Task<List<Issue>> FetchIssuesAsync(SourceParams p, Auth auth, CancellationToken ct)
    {
        var project = Uri.EscapeDataString(p.Project);
        var labels = p.Labels.Length > 0 ? "&labels=" + Uri.EscapeDataString(p.Labels) : "";
        var issues = new List<Issue>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var json = await GetAsync(
                $"{p.Host}/api/v4/projects/{project}/issues?state=opened&per_page={PageSize}"
                + $"&page={page}{labels}", auth, ct);
            var batch = ParseIssues(json, p.Project);
            issues.AddRange(batch);
            if (batch.Count < PageSize)
            {
                break;
            }
        }
        // обсуждение — отдельным запросом на задачу (у GitLab нет вложенных заметок)
        for (var i = 0; i < issues.Count; i++)
        {
            var issueRef = new IssueRef(p.Host, p.Project, issues[i].Iid);
            issues[i] = issues[i] with { Notes = await FetchNotesAsync(issueRef, auth, ct) };
        }
        return issues;
    }

    /// <summary>Заметки обсуждения issue постранично, от старых к новым; служебные
    /// («изменил метку», «назначил») пропускаются — это не разговор людей.</summary>
    private async Task<List<IssueNote>> FetchNotesAsync(IssueRef issueRef, Auth auth,
        CancellationToken ct)
    {
        var notes = new List<IssueNote>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var json = await GetAsync(
                $"{issueRef.Host}/api/v4/projects/{issueRef.ApiProject}/issues/{issueRef.Iid}/notes"
                + $"?per_page={PageSize}&page={page}&sort=asc&order_by=created_at", auth, ct);
            var batch = ParseNotes(json);
            notes.AddRange(batch.Notes);
            if (batch.Total < PageSize)
            {
                break;
            }
        }
        notes.Sort((a, b) => a.Date.CompareTo(b.Date));
        return notes;
    }

    /// <summary>Список issue из JSON GitLab (отделено от HTTP — тестируемо).</summary>
    public static List<Issue> ParseIssues(string json, string projectPath)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<Issue>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (IssueOf(item, projectPath) is { } issue)
            {
                result.Add(issue);
            }
        }
        return result;
    }

    /// <summary>Одна issue из JSON GitLab.</summary>
    public static Issue ParseIssue(string json, string projectPath)
    {
        using var doc = JsonDocument.Parse(json);
        return IssueOf(doc.RootElement, projectPath)
               ?? throw new InvalidOperationException(Loc.T("msg.gitlabImporter.7"));
    }

    private static Issue? IssueOf(JsonElement element, string projectPath)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var iid = Num(element, "iid");
        var title = Str(element, "title");
        if (iid <= 0 || title.Length == 0)
        {
            return null;
        }
        var webUrl = Str(element, "web_url");
        // путь проекта у issue не назван: он берётся из адреса, а не из настроек источника —
        // проект мог быть перенесён в другую группу
        var path = ParseIssueUrl(webUrl)?.ProjectPath ?? projectPath;
        return new Issue(Num(element, "id").ToString(), iid, Num(element, "project_id").ToString(),
            title, Str(element, "description"), DateOf(element, "due_date"), path, webUrl);
    }

    /// <summary>Заметки обсуждения из JSON: Total — сколько заметок пришло ВСЕГО (по нему
    /// видно, есть ли следующая страница), Notes — только человеческие.</summary>
    public static (List<IssueNote> Notes, int Total) ParseNotes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return ([], 0);
        }
        var notes = new List<IssueNote>();
        var total = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            total++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            // служебная заметка GitLab («изменил метку», «назначил», «закрыл») —
            // это не разговор, и в чате задачи ей не место
            if (item.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.True)
            {
                continue;
            }
            var id = Num(item, "id").ToString();
            var text = Str(item, "body").Trim();
            if (id == "0" || text.Length == 0)
            {
                continue;
            }
            var author = "";
            if (item.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                author = Str(a, "username");
                if (author.Length == 0)
                {
                    author = Str(a, "name");
                }
            }
            notes.Add(new IssueNote(id, author, text,
                DateOf(item, "created_at") ?? DateTime.UtcNow));
        }
        return (notes, total);
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    private static long Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var value)
            ? value
            : 0;

    /// <summary>Дата GitLab: «2026-08-20» у срока и полное время у заметок — оба вида
    /// приводятся к UTC, как это делает импорт Trello.</summary>
    private static DateTime? DateOf(JsonElement obj, string name)
    {
        var value = Str(obj, name);
        if (value.Length == 0 || !DateTime.TryParse(value, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    // ------------------------------------------------- токен и разбор отказов

    private void EnsureKind(ImportSource source)
    {
        if (!string.Equals(source.Kind, Kind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.T("msg.gitlabImporter.8", source.Kind));
        }
    }

    /// <summary>Параметры источника: по id, а без него — единственный активный
    /// gitlab-источник; несколько — предпочитается тот, чей сервер совпал с адресом ссылки
    /// (у человека может быть и облачный GitLab, и свой).</summary>
    private SourceParams ResolveSourceParams(string? sourceId, string? hostHint = null)
    {
        if (sourceId is { Length: > 0 })
        {
            var byId = _sources.Get(sourceId)
                       ?? throw new InvalidOperationException(Loc.T("msg.gitlabImporter.2"));
            EnsureKind(byId);
            return SourceParams.Parse(byId.ParamsJson);
        }
        var all = _sources.List()
            .Where(s => s.IsActive && string.Equals(s.Kind, Kind, StringComparison.OrdinalIgnoreCase))
            .Select(s => SourceParams.Parse(s.ParamsJson)).ToList();
        if (all.Count == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.gitlabImporter.9"));
        }
        if (all.Count == 1)
        {
            return all[0];
        }
        var host = NormalizeHost(hostHint);
        var matched = all.Where(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matched.Count == 1
            ? matched[0]
            : throw new InvalidOperationException(Loc.T("msg.gitlabImporter.10"));
    }

    /// <summary>Значение токена и ОТКУДА оно взято: источник нужен в сообщении об ошибке —
    /// без него непонятно, чей токен GitLab отверг (приём T-134).</summary>
    private Auth ResolveAuth(SourceParams p)
    {
        var (value, source) = KeyResolver is not null
            ? KeyResolver(p.TokenRef)
            : _secrets.ResolveWithSource(p.TokenRef);
        if (string.IsNullOrWhiteSpace(value))
        {
            Logger.Warning("Импорт GitLab: значение {SecretRef} не найдено ({Source})",
                p.TokenRef, source);
            throw new InvalidOperationException(Loc.T("msg.gitlabImporter.11", p.TokenRef,
                _secrets.LocationOf(p.TokenRef), SecretStore.EnvNameOf(p.TokenRef), KeyPage));
        }
        // пробелы и перевод строки по краям — обычное дело при копировании значения в файл
        // секретов; GitLab отвечает на них «401 Unauthorized» (грабля T-134)
        var auth = new Auth(value.Trim(), p.TokenRef, source, NormalizeHost(p.Host));
        Logger.Information("GitLab: сервер {Host}, токен {TokenRef} — {TokenSource}, {Shape}",
            auth.Host, p.TokenRef, source, Shape(auth.Token));
        return auth;
    }

    /// <summary>Токен GitLab: нынешние личные токены начинаются с <c>glpat-</c>; бывают
    /// и другие (проектные, OAuth), поэтому проверка МЯГКАЯ — подсказывает, а не запрещает.</summary>
    public static bool LooksLikeToken(string value) =>
        value.StartsWith("glpat-", StringComparison.Ordinal) && value.Length > 20;

    /// <summary>«Форма» значения для сообщений и логов: длина и начало, но НИКОГДА само
    /// значение — по ней видно, что в поле токена оказалось что-то другое.</summary>
    public static string Shape(string value) =>
        value.Length == 0
            ? Loc.T("msg.gitlabImporter.12")
            : Loc.T("msg.gitlabImporter.13", value.Length,
                LooksLikeToken(value) ? "glpat-…" : Loc.T("msg.gitlabImporter.14"));

    /// <summary>Замечание к введённому значению: пусто — значение похоже на токен GitLab.
    /// Проверка мягкая, как у Trello (T-134): формат задаёт GitLab, и запрет однажды
    /// закрыл бы вход вполне рабочему токену.</summary>
    public static string SecretHint(string field, string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0 || LooksLikeToken(value)
            || !field.Trim().Equals(ImportKeyService.FieldToken, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }
        return Loc.T("msg.gitlabImporter.15", Shape(value), KeyPage);
    }

    /// <summary>Отказ GitLab человеческими словами (приём T-134): что именно не принято,
    /// откуда взят токен и что с этим делать.</summary>
    public static string Explain(GitLabApiException ex, Auth auth)
    {
        var body = ex.Body.Trim();
        var where = Loc.T("msg.gitlabImporter.16", auth.TokenRef, auth.TokenSource);
        return ex.Status switch
        {
            401 => Loc.T("msg.gitlabImporter.17", body, where, Shape(auth.Token), KeyPage),
            403 => Loc.T("msg.gitlabImporter.18", body, where),
            404 => Loc.T("msg.gitlabImporter.19", auth.Host, body),
            429 => Loc.T("msg.gitlabImporter.20"),
            _ => Loc.T("msg.gitlabImporter.1", ex.Status, body),
        };
    }

    /// <summary>Объяснение отказа — в лог И в ответ (приём T-134): уровень вывода по
    /// умолчанию «предупреждения», а разбираться по логу приходится как раз тогда, когда
    /// импорт уже не прошёл.</summary>
    private static InvalidOperationException Explained(GitLabApiException ex, Auth auth)
    {
        var message = Explain(ex, auth);
        Logger.Warning("Импорт GitLab отклонён: {Reason}", message);
        return new InvalidOperationException(message, ex);
    }

    /// <summary>
    /// ПРОВЕРКА ПОДКЛЮЧЕНИЯ источника (кнопка формы источника, приём T-134): GitLab
    /// спрашивают «кто я», а если в источнике назван проект — виден ли он этому токену.
    /// Исключений не бросает: ответ — вердикт.
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
            Logger.Warning("Проверка подключения GitLab: {Reason}", ex.Message);
            return (false, ex.Message);
        }

        try
        {
            var json = await GetAsync($"{p.Host}/api/v4/user", auth, ct);
            using var doc = JsonDocument.Parse(json);
            var username = Str(doc.RootElement, "username");
            var message = Loc.T("msg.gitlabImporter.21", username, p.Host);
            if (p.Project.Length > 0)
            {
                // проект тоже проверяем: «токен принят» ещё не значит «этот проект виден»
                try
                {
                    var projectJson = await GetAsync(
                        $"{p.Host}/api/v4/projects/{Uri.EscapeDataString(p.Project)}", auth, ct);
                    using var projectDoc = JsonDocument.Parse(projectJson);
                    message += Loc.T("msg.gitlabImporter.22",
                        Str(projectDoc.RootElement, "path_with_namespace"));
                }
                catch (GitLabApiException ex)
                {
                    return (false, Loc.T("msg.gitlabImporter.23", p.Project, Explain(ex, auth)));
                }
            }
            Logger.Information("Проверка подключения GitLab: аккаунт @{Username} на {Host}",
                username, p.Host);
            return (true, message);
        }
        catch (GitLabApiException ex)
        {
            return (false, Explained(ex, auth).Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "Проверка подключения GitLab не удалась");
            return (false, Loc.T("msg.gitlabImporter.24", ex.Message));
        }
    }

    /// <summary>Запрос к API: токен уезжает ЗАГОЛОВКОМ, поэтому в лог попадает чистый
    /// адрес и его не приходится вычищать, как у Trello (там ключ в строке запроса).</summary>
    private async Task<string> GetAsync(string url, Auth auth, CancellationToken ct)
    {
        Logger.Information("GitLab API: GET {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", auth.Token);
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("GitLab API: {Url} → HTTP {Status}: {Body}",
                url, (int)response.StatusCode, preview);
            throw new GitLabApiException((int)response.StatusCode, preview);
        }
        return body;
    }

    /// <summary>Скачивание приложенного файла: null — файл больше предела (остаётся ссылкой).</summary>
    private async Task<byte[]?> DownloadAsync(string url, Auth auth, CancellationToken ct)
    {
        Logger.Information("GitLab: скачивание файла {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", auth.Token);
        using var response = await Client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitLabApiException((int)response.StatusCode, response.ReasonPhrase ?? "");
        }
        if (response.Content.Headers.ContentLength > MaxAttachmentBytes)
        {
            Logger.Warning("GitLab: файл {Url} больше предела ({Bytes} б) — останется ссылкой",
                url, response.Content.Headers.ContentLength);
            return null;
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        // длину не назвали — проверяем по факту: предел затем и нужен, чтобы огромный файл
        // не лёг в хранилище проекта
        if (bytes.LongLength > MaxAttachmentBytes)
        {
            Logger.Warning("GitLab: файл {Url} больше предела ({Bytes} б) — останется ссылкой",
                url, bytes.LongLength);
            return null;
        }
        return bytes;
    }
}
