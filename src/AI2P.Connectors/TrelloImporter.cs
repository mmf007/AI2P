using AI2P.Core;
using System.Text;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Импорт задач из Trello (ТЗ v1.28, todo30): по фильтру у конкретного логина.
/// Источник — запись справочника импортов (kind "trello", params: login, filter,
/// ссылки на ключи в секретах). REST API Trello: доски участника → открытые карточки
/// досок, имя которых содержит фильтр (пустой фильтр — все доски). Карточка → задача
/// текущего проекта; защита от дублей — external_ref = «trello:&lt;cardId&gt;»:
/// по чекбоксам добавляются новые и/или обновляются существующие.
/// </summary>
public sealed class TrelloImporter
{
    private const string BaseUrl = "https://api.trello.com/1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Страница, где берутся ключ API и токен — она же названа в документе вида
    /// источника; в сообщениях об ошибке ссылка важнее любых слов (T-134).</summary>
    public const string KeyPage = "https://trello.com/power-ups/admin";

    /// <summary>Предел размера скачиваемого приложенного файла — как у загрузки в MD-редакторе.</summary>
    private const long MaxAttachmentBytes = 200 * 1024 * 1024;

    /// <summary>Сколько комментариев карточки забирается за раз — предел Trello для
    /// вложенных действий; больше тысячи сообщений в обсуждении одной карточки не бывает.</summary>
    private const int MaxComments = 1000;

    private readonly TaskService _tasks;
    private readonly ImportSourceService _sources;
    private readonly SecretStore _secrets;
    private readonly EventStore _events;
    private readonly FileStore _files;
    private readonly ChatService? _chat;

    /// <param name="chat">Чат задач (T-152): обсуждение карточки переносится в чат задачи;
    /// null — комментарии не переносятся (массовый импорт в тестах без чата).</param>
    public TrelloImporter(TaskService tasks, ImportSourceService sources, SecretStore secrets,
        EventStore events, FileStore files, ChatService? chat = null)
    {
        _tasks = tasks;
        _sources = sources;
        _secrets = secrets;
        _events = events;
        _files = files;
        _chat = chat;
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(TrelloImporter));

    /// <summary>
    /// Откуда брать ключ и токен (ТЗ v1.65, T-123): с версии 1.65 они вводятся в интерфейсе
    /// и лежат в БД ОРГАНИЗАЦИИ зашифрованными, а не в secrets.json. Подставляется контекстом
    /// организации (<c>ModelKeyService.ResolveWithSource</c>); не подставлен — читаем
    /// secrets.json, как раньше: так работают тесты и установки без организации.
    /// </summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>Подменный HttpClient — только для тестов: живой Trello в прогоне не дёргается.</summary>
    public HttpClient? HttpOverride { get; set; }

    private HttpClient Client => HttpOverride ?? Http;

    /// <summary>
    /// Комментарий карточки Trello — обсуждение (T-152): id действия (защита от дублей),
    /// логин автора В TRELLO, текст и время. Автор пишется в чат как есть —
    /// «trello:&lt;логин&gt;» строится при переносе (<see cref="ChatOf"/>).
    /// AuthorId — внутренний ключ участника Trello: во ВЛОЖЕННЫХ действиях (карточка вместе
    /// с обсуждением) Trello отдаёт только его, а логин приходится спрашивать отдельно
    /// (<see cref="ResolveAuthorsAsync"/>); Author при этом пуст.
    /// </summary>
    public sealed record CardComment(string Id, string Author, string Text, DateTime Date,
        string AuthorId = "");

    /// <summary>Карточка Trello (нужные импорту поля); Comments — обсуждение карточки
    /// в хронологическом порядке (T-152), пусто — комментариев нет или их не запрашивали.</summary>
    public sealed record Card(string Id, string Name, string Desc, DateTime? Due, string BoardName,
        string Url, List<CardComment>? Comments = null);

    /// <summary>Итог импорта: добавлено / обновлено / пропущено и сколько сообщений обсуждения
    /// перенесено в чат (T-152) — всё для сообщения в UI.</summary>
    public sealed record ImportResult(int Added, int Updated, int Skipped, int Comments = 0);

    /// <summary>Параметры источника (params_json записи справочника импортов).</summary>
    public sealed record SourceParams(string Login, string Filter, string KeyRef, string TokenRef)
    {
        public static SourceParams Parse(string paramsJson)
        {
            using var doc = JsonDocument.Parse(paramsJson);
            var root = doc.RootElement;
            string Str(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()!.Trim()
                    : "";
            var keyRef = Str("keyRef");
            var tokenRef = Str("tokenRef");
            // ссылок нет — источник заведён до v1.65, у него они общие (ТЗ п. 2.10)
            return new SourceParams(Str("login"), Str("filter"),
                keyRef.Length > 0 ? keyRef : ImportSource.LegacyKeyRef,
                tokenRef.Length > 0 ? tokenRef : ImportSource.LegacyTokenRef);
        }
    }

    /// <summary>Запуск импорта в проект (кнопка списка задач, ТЗ v1.28): источник из
    /// справочника + чекбоксы «добавлять новые» / «обновить существующие».</summary>
    public async Task<ImportResult> ImportAsync(Project project, string sourceId, bool addNew,
        bool updateExisting, string? actorId, CancellationToken ct = default)
    {
        var source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.trelloImporter.1"));
        if (!string.Equals(source.Kind, "trello", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.T("msg.trelloImporter.2", source.Kind));
        }
        var p = SourceParams.Parse(source.ParamsJson);
        if (p.Login.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.trelloImporter.3"));
        }
        var auth = ResolveAuth(p);

        Logger.Information("Импорт Trello: источник {Source}, логин {Login}, фильтр «{Filter}», " +
                           "проект {Project}, новые {AddNew}, обновление {Update}",
            source.Name, p.Login, p.Filter, project.Name, addNew, updateExisting);
        List<Card> cards;
        try
        {
            cards = await FetchCardsAsync(p.Login, p.Filter, auth, ct);
        }
        catch (TrelloApiException ex)
        {
            // отказ Trello — человеческими словами: что не принято и где это лежит (T-134)
            throw Explained(ex, auth);
        }
        var result = Apply(project, cards, addNew, updateExisting, actorId);
        Logger.Information("Импорт Trello: карточек {Cards} → добавлено {Added}, обновлено {Updated}, " +
                           "пропущено {Skipped}, сообщений обсуждения {Comments}",
            cards.Count, result.Added, result.Updated, result.Skipped, result.Comments);

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
                cards = cards.Count,
                added = result.Added,
                updated = result.Updated,
                skipped = result.Skipped,
                comments = result.Comments,
            }),
        });
        return result;
    }

    /// <summary>Применить карточки к проекту (отделено от HTTP — тестируемо, todo30):
    /// external_ref «trello:id» — есть в проекте → обновление (по чекбоксу), нет → создание.</summary>
    public ImportResult Apply(Project project, IReadOnlyList<Card> cards, bool addNew,
        bool updateExisting, string? actorId)
    {
        int added = 0, updated = 0, skipped = 0, comments = 0;
        foreach (var card in cards)
        {
            var externalRef = $"trello:{card.Id}";
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
                    Title = card.Name,
                    Status = TaskStatuses.Draft,
                    DueDate = card.Due,
                    ExternalRef = externalRef,
                    // ссылка импорта (T-246): по ней задачу потом обновляют из источника
                    ImportUrl = TaskService.NormalizeImportUrl(card.Url),
                };
                var created = _tasks.Create(task, DescriptionOf(card), "", actorId);
                comments += ImportChat(created.Id, card.Comments, actorId);
                added++;
            }
            else if (updateExisting)
            {
                existing.Title = card.Name;
                existing.DueDate = card.Due;
                existing.ExternalRef = externalRef;
                // ссылку импорта проставляем и при обновлении: задача могла приехать
                // прошлой версией, когда этого поля ещё не было (T-246). Заданное человеком
                // значение не затирается пустотой — Trello адрес карточки отдаёт всегда
                existing.ImportUrl = TaskService.NormalizeImportUrl(card.Url) ?? existing.ImportUrl;
                _tasks.Update(existing, DescriptionOf(card), _tasks.ReadAcceptance(existing), actorId);
                // повторный импорт добавляет только НОВЫЕ сообщения обсуждения (T-152):
                // уже перенесённые узнаются по внешнему id действия Trello
                comments += ImportChat(existing.Id, card.Comments, actorId);
                updated++;
            }
            else
            {
                skipped++;
            }
        }
        return new ImportResult(added, updated, skipped, comments);
    }

    /// <summary>
    /// Обсуждение карточки → чат задачи (T-152). Автор сообщения — «trello:&lt;логин&gt;»,
    /// время — время комментария в Trello, поэтому хронология источника сохраняется.
    /// Ничего не переносится, если чат не подставлен или неизвестен актор: колонка
    /// from_executor_id ссылается на справочник исполнителей и пустой быть не может.
    /// </summary>
    public int ImportChat(string taskId, IReadOnlyList<CardComment>? comments, string? actorId)
    {
        if (_chat is null || comments is not { Count: > 0 })
        {
            return 0;
        }
        if (string.IsNullOrEmpty(actorId))
        {
            Logger.Warning("Импорт Trello: обсуждение карточки ({Count} сообщ.) не перенесено — " +
                           "неизвестен исполнитель, от чьего имени импортируют", comments.Count);
            return 0;
        }
        var moved = _chat.Import(taskId, actorId, ChatOf(comments));
        if (moved > 0)
        {
            Logger.Information("Импорт Trello: в чат задачи перенесено сообщений обсуждения {Count}", moved);
        }
        return moved;
    }

    /// <summary>Комментарии карточки в виде сообщений чата (T-152): автор — «trello:&lt;логин&gt;»
    /// (так он и «светится» в чате), внешний id — «trello:&lt;id действия&gt;», порядок —
    /// хронологический (Trello отдаёт действия от новых к старым).</summary>
    public static List<ChatService.ImportedMessage> ChatOf(IEnumerable<CardComment> comments) =>
        comments.OrderBy(c => c.Date)
            .Select(c => new ChatService.ImportedMessage($"trello:{c.Id}",
                // логин не узнан (участника удалили либо Trello не отдал его имя) — «?»:
                // сообщение всё равно принадлежит Trello, и это должно быть видно
                $"trello:{(c.Author.Length > 0 ? c.Author : "?")}", c.Text, c.Date))
            .ToList();

    /// <summary>
    /// Дозапрос логинов авторов обсуждения (T-152). Комментарии приезжают ВЛОЖЕННЫМИ
    /// в карточку — одним запросом на всю доску вместо сотни, — но у вложенного действия
    /// Trello отдаёт только внутренний ключ участника (проверено живым обращением к API:
    /// у вложенных действий <c>memberCreator</c> отсутствует). Логин спрашивается один раз
    /// на участника и подставляется всем его комментариям; не ответил Trello — остаётся «?»,
    /// импорт из-за этого не падает.
    /// </summary>
    private async Task ResolveAuthorsAsync(IEnumerable<Card> cards, Auth auth, CancellationToken ct)
    {
        var lists = cards.Select(c => c.Comments).OfType<List<CardComment>>()
            .Where(l => l.Count > 0).ToList();
        var unknown = lists.SelectMany(l => l)
            .Where(c => c.Author.Length == 0 && c.AuthorId.Length > 0)
            .Select(c => c.AuthorId).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count == 0)
        {
            return;
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var memberId in unknown)
        {
            try
            {
                var json = await GetAsync(
                    $"{BaseUrl}/members/{Uri.EscapeDataString(memberId)}?fields=username,fullName&{auth.Query}", ct);
                using var doc = JsonDocument.Parse(json);
                var name = Str(doc.RootElement, "username");
                if (name.Length == 0)
                {
                    name = Str(doc.RootElement, "fullName");
                }
                if (name.Length > 0)
                {
                    names[memberId] = name;
                }
            }
            catch (Exception ex) when (ex is TrelloApiException or JsonException)
            {
                Logger.Warning("Импорт Trello: имя автора комментария {Member} узнать не удалось — {Reason}",
                    memberId, ex.Message);
            }
        }
        foreach (var list in lists)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Author.Length == 0 && names.TryGetValue(list[i].AuthorId, out var name))
                {
                    list[i] = list[i] with { Author = name };
                }
            }
        }
    }

    /// <summary>Описание задачи из карточки: текст + источник (доска, ссылка на карточку).</summary>
    private static string DescriptionOf(Card card)
    {
        var footer = Loc.T("msg.trelloImporter.4", card.BoardName, card.Url);
        return card.Desc.Trim().Length > 0 ? card.Desc.Trim() + "\n\n---\n" + footer : footer;
    }

    // --- импорт одиночной карточки по URL (ТЗ v1.50, todo50) ---

    /// <summary>Приложенный файл карточки: isUpload — файл хранится в Trello (скачивается
    /// с авторизацией), иначе это внешняя ссылка (остаётся ссылкой).</summary>
    public sealed record CardAttachment(string Id, string Name, string Url, long Bytes, bool IsUpload);

    /// <summary>Содержимое одиночной карточки для импорта в форму задачи или в задачу-черновик:
    /// файлы уже скачаны в uploads/ проекта, ссылки на них — в списке файлов и в описании.
    /// Url — короткий адрес карточки (T-246): он ложится в задачу ссылкой импорта, и по нему
    /// задачу потом обновляют из источника. Trello отдаёт его сам (shortUrl); не отдал —
    /// остаётся тот адрес, который ввёл человек.</summary>
    public sealed record ImportedCard(string Title, string Description, DateTime? Due,
        string ExternalRef, List<string> Files, List<CardComment> Comments, string Url = "");

    /// <summary>Короткий код карточки из ссылки https://trello.com/c/&lt;код&gt;[/…]; null — не ссылка на карточку.</summary>
    public static string? ParseCardUrl(string url)
    {
        var marker = url.IndexOf("trello.com/c/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }
        var id = url[(marker + "trello.com/c/".Length)..];
        var end = id.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            id = id[..end];
        }
        id = id.Trim();
        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// Импорт содержимого одной карточки по URL (ТЗ v1.50): карточка читается по короткому
    /// коду из ссылки, файлы-вложения скачиваются в uploads/ проекта, внешние вложения
    /// остаются ссылками. Ключи — из источника импорта (по id или единственный активный
    /// trello-источник). Задача здесь НЕ создаётся: результат подставляется в форму (UI)
    /// или используется инструментом агента import_task_from_url.
    /// </summary>
    public async Task<ImportedCard> ImportCardContentAsync(Project project, string url,
        string? sourceId, CancellationToken ct = default)
    {
        var shortLink = ParseCardUrl(url)
                        ?? throw new InvalidOperationException(
                            Loc.T("msg.trelloImporter.5"));
        var p = ResolveSourceParams(sourceId);
        var auth = ResolveAuth(p);

        Logger.Information("Импорт карточки Trello по URL: {Url}, проект {Project}", url, project.Name);
        string cardJson;
        try
        {
            // обсуждение карточки едет тем же запросом (T-152): actions=commentCard —
            // комментарии с автором и временем, они станут сообщениями чата задачи
            cardJson = await GetAsync($"{BaseUrl}/cards/{Uri.EscapeDataString(shortLink)}" +
                                      $"?fields=name,desc,due,shortUrl,idBoard" +
                                      $"&attachments=true&attachment_fields=id,name,url,bytes,isUpload" +
                                      $"&actions=commentCard&actions_limit={MaxComments}" +
                                      $"&action_memberCreator=true&action_memberCreator_fields=username,fullName" +
                                      $"&{auth.Query}", ct);
        }
        catch (TrelloApiException ex)
        {
            // «Trello ответил 401: invalid key» человеку ничего не объясняло (T-134)
            throw Explained(ex, auth);
        }
        var (card, attachments, boardId) = ParseCardDetails(cardJson);
        // логины авторов обсуждения: у вложенных действий Trello отдаёт только ключ участника
        await ResolveAuthorsAsync([card], auth, ct);
        var boardName = "";
        if (boardId.Length > 0)
        {
            var boardJson = await GetAsync($"{BaseUrl}/boards/{Uri.EscapeDataString(boardId)}?fields=name&{auth.Query}", ct);
            using var doc = JsonDocument.Parse(boardJson);
            boardName = Str(doc.RootElement, "name");
        }
        card = card with { BoardName = boardName };

        var files = new List<string>();
        var links = new List<string>();
        foreach (var att in attachments)
        {
            if (att.IsUpload)
            {
                if (att.Bytes > MaxAttachmentBytes)
                {
                    links.Add(Loc.T("msg.trelloImporter.6", att.Name, att.Url));
                    continue;
                }
                var bytes = await DownloadAttachmentAsync(card.Id, att, auth, ct);
                var rel = _files.SaveUpload(project.Slug, att.Name, bytes);
                var link = "api/files/raw?path=" + Uri.EscapeDataString(rel);
                links.Add(IsImage(att.Name) ? $"![{att.Name}]({link})" : $"- [{att.Name}]({link})");
                files.Add(att.Name);
            }
            else
            {
                links.Add($"- [{att.Name}]({att.Url})");
            }
        }
        var comments = card.Comments ?? [];
        Logger.Information("Импорт карточки Trello: «{Name}», вложений {Count}, скачано {Saved}, " +
                           "сообщений обсуждения {Comments}",
            card.Name, attachments.Count, files.Count, comments.Count);
        return new ImportedCard(card.Name, BuildImportedDescription(card, links), card.Due,
            $"trello:{card.Id}", files, comments,
            card.Url.Trim().Length > 0 ? card.Url.Trim() : url.Trim());
    }

    // --- обновление УЖЕ СОЗДАННОЙ задачи из источника импорта (T-246) ---

    /// <summary>Итог обновления задачи из источника (T-246): что стало с задачей и сколько
    /// приехало нового — для сообщения человеку в UI. Exported (T-107-S0) — сколько
    /// результатов задачи уехало ОБРАТНО, записями в обсуждение карточки.</summary>
    public sealed record RefreshResult(string DisplayId, string Title, int Comments, int Files,
        int Exported = 0);

    /// <summary>Умеет ли обновление разобрать эту ссылку импорта (T-246). Точка расширения
    /// для остальных источников: у каждого своя проверка, и по ней выбирается процедура
    /// обновления.</summary>
    public static bool CanRefresh(string? importUrl) =>
        importUrl is { Length: > 0 } && ParseCardUrl(importUrl) is not null;

    /// <summary>
    /// ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ TRELLO (T-246). Карточка перечитывается по ссылке импорта,
    /// сохранённой в самой задаче, и её содержимое ложится в задачу поверх: заголовок,
    /// срок, описание (вместе со свежим списком приложенных файлов — они скачиваются заново)
    /// и НОВЫЕ сообщения обсуждения. Уже перенесённые сообщения чата не дублируются
    /// (внешний id действия), критерии приёмки, исполнители, статус и связи задачи
    /// не трогаются вовсе — их ведут здесь, а не в Trello.
    ///
    /// Описание переписывается ЦЕЛИКОМ — ровно так же, как это делает массовый импорт
    /// с галочкой «обновить существующие»: обновление из источника затем и запускают,
    /// чтобы в задаче стало то, что сейчас в карточке.
    /// </summary>
    public async Task<RefreshResult> RefreshTaskAsync(Project project, TaskItem task,
        string? sourceId, string? actorId, CancellationToken ct = default)
    {
        var url = TaskService.NormalizeImportUrl(task.ImportUrl)
                  ?? throw new InvalidOperationException(Loc.T("msg.trelloImporter.39", task.DisplayId));
        if (ParseCardUrl(url) is null)
        {
            throw new InvalidOperationException(Loc.T("msg.trelloImporter.40", url));
        }

        Logger.Information("Обновление задачи {Task} из Trello по ссылке {Url}", task.DisplayId, url);
        var imported = await ImportCardContentAsync(project, url, sourceId, ct);

        task.Title = imported.Title;
        task.DueDate = imported.Due;
        // внешний ключ карточки мог быть не проставлен (задачу завели руками и указали
        // ссылку) — после обновления он есть, и повторный массовый импорт дубля не создаст
        task.ExternalRef = imported.ExternalRef;
        // адрес приводим к короткому виду, каким его называет сам Trello: человек мог
        // вставить ссылку с хвостом заголовка карточки
        task.ImportUrl = TaskService.NormalizeImportUrl(imported.Url) ?? url;
        _tasks.Update(task, imported.Description, _tasks.ReadAcceptance(task), actorId);
        var comments = ImportChat(task.Id, imported.Comments, actorId);
        // и ОБРАТНОЕ направление (T-107-S0): результаты задачи — записями в конец обсуждения
        var exported = await ExportResultsAsync(task, imported.ExternalRef, imported.Comments,
            sourceId, ct);

        Logger.Information("Обновление задачи {Task} из Trello: «{Title}», файлов {Files}, "
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
    /// РЕЗУЛЬТАТЫ ЗАДАЧИ — В ОБСУЖДЕНИЕ КАРТОЧКИ (T-107-S0). Каждый результат уезжает своей
    /// записью в конец обсуждения; уже выложенное узнаётся по маркеру в тексте комментариев,
    /// которые тот же сеанс обновления только что перечитал, — поэтому повтора не будет
    /// ни со второго нажатия, ни с другого сервера кластера. Технические записи (ожидание
    /// сброса лимита исполнителя и прочие ошибки) не уезжают вовсе — см. <see cref="ResultExport"/>.
    ///
    /// Отказ Trello на выкладке ОБНОВЛЕНИЕ НЕ РВЁТ: задача уже обновлена, и терять это
    /// из-за не принятого комментария нельзя. Что не уехало — видно в логе и по числу в UI.
    /// </summary>
    private async Task<int> ExportResultsAsync(TaskItem task, string externalRef,
        IReadOnlyList<CardComment> discussion, string? sourceId, CancellationToken ct)
    {
        // внешний ключ задачи — «trello:<id карточки>»: id карточки для API берём из него
        var cardId = externalRef.StartsWith("trello:", StringComparison.Ordinal)
            ? externalRef["trello:".Length..]
            : "";
        if (cardId.Length == 0)
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
                await PostCommentAsync(cardId, ResultExport.Compose(task.DisplayId, piece), auth, ct);
                posted++;
            }
            catch (Exception ex) when (ex is TrelloApiException or HttpRequestException
                                           or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warning("Выкладка результата {Result} задачи {Task} в Trello не удалась — {Reason}",
                    piece.Name, task.DisplayId, ex.Message);
                break;
            }
        }
        if (posted > 0)
        {
            Logger.Information("Задача {Task}: в обсуждение карточки Trello выложено результатов {Count}",
                task.DisplayId, posted);
        }
        return posted;
    }

    /// <summary>Запись в обсуждение карточки: текст уезжает ТЕЛОМ запроса, а не строкой
    /// запроса — результат бывает длиной в тысячи знаков.</summary>
    private async Task PostCommentAsync(string cardId, string text, Auth auth, CancellationToken ct)
    {
        var url = $"{BaseUrl}/cards/{Uri.EscapeDataString(cardId)}/actions/comments?{auth.Query}";
        Logger.Information("Trello API: POST {Url}",
            $"{BaseUrl}/cards/{cardId}/actions/comments");
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("text", text)]),
        };
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("Trello API: комментарий не принят, HTTP {Status}: {Body}",
                (int)response.StatusCode, preview);
            throw new TrelloApiException((int)response.StatusCode, preview);
        }
    }

    /// <summary>Описание задачи из одиночной карточки: текст + блок приложенных файлов + источник.</summary>
    public static string BuildImportedDescription(Card card, IReadOnlyList<string> attachmentLinks)
    {
        var sb = new StringBuilder();
        if (card.Desc.Trim().Length > 0)
        {
            sb.AppendLine(card.Desc.Trim());
            sb.AppendLine();
        }
        if (attachmentLinks.Count > 0)
        {
            sb.AppendLine(Loc.T("msg.trelloImporter.7"));
            sb.AppendLine();
            foreach (var link in attachmentLinks)
            {
                sb.AppendLine(link);
            }
            sb.AppendLine();
        }
        sb.Append(Loc.T("msg.trelloImporter.8", card.BoardName, card.Url));
        return sb.ToString();
    }

    /// <summary>Карточка с вложениями из JSON одиночного GET /cards/{id} (отделено от HTTP — тестируемо).</summary>
    public static (Card Card, List<CardAttachment> Attachments, string BoardId) ParseCardDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        DateTime? due = null;
        if (root.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.String
            && DateTime.TryParse(d.GetString(), null,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            due = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
        var card = new Card(Str(root, "id"), Str(root, "name"), Str(root, "desc"), due, "",
            Str(root, "shortUrl"), ParseComments(root));
        var attachments = new List<CardAttachment>();
        if (root.TryGetProperty("attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
        {
            foreach (var att in atts.EnumerateArray())
            {
                var id = Str(att, "id");
                var name = Str(att, "name");
                if (id.Length == 0 || name.Length == 0)
                {
                    continue;
                }
                var bytes = att.TryGetProperty("bytes", out var b) && b.ValueKind == JsonValueKind.Number
                    ? b.GetInt64()
                    : 0;
                var isUpload = att.TryGetProperty("isUpload", out var u)
                               && u.ValueKind == JsonValueKind.True;
                attachments.Add(new CardAttachment(id, name, Str(att, "url"), bytes, isUpload));
            }
        }
        return (card, attachments, Str(root, "idBoard"));
    }

    /// <summary>Картинки вставляются в описание превью, остальные файлы — обычной ссылкой.</summary>
    private static bool IsImage(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant()
            is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg";

    /// <summary>Параметры источника: по id, а без него — единственный активный trello-источник.</summary>
    private SourceParams ResolveSourceParams(string? sourceId)
    {
        ImportSource? source;
        if (sourceId is { Length: > 0 })
        {
            source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.trelloImporter.1"));
        }
        else
        {
            var trello = _sources.List().Where(s =>
                s.IsActive && string.Equals(s.Kind, "trello", StringComparison.OrdinalIgnoreCase)).ToList();
            source = trello.Count switch
            {
                0 => throw new InvalidOperationException(
                    Loc.T("msg.trelloImporter.9")),
                1 => trello[0],
                _ => throw new InvalidOperationException(
                    Loc.T("msg.trelloImporter.10")),
            };
        }
        if (!string.Equals(source.Kind, "trello", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                Loc.T("msg.trelloImporter.2", source.Kind));
        }
        return SourceParams.Parse(source.ParamsJson);
    }

    /// <summary>Скачивание файла-вложения: download-эндпоинт Trello требует OAuth-заголовок
    /// (key/token в query для него недостаточно).</summary>
    private async Task<byte[]> DownloadAttachmentAsync(string cardId, CardAttachment att,
        Auth auth, CancellationToken ct)
    {
        var url = $"{BaseUrl}/cards/{Uri.EscapeDataString(cardId)}/attachments/" +
                  $"{Uri.EscapeDataString(att.Id)}/download/{Uri.EscapeDataString(att.Name)}";
        Logger.Information("Trello API: GET {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"OAuth oauth_consumer_key=\"{auth.Key}\", oauth_token=\"{auth.Token}\"");
        using var response = await Client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning("Trello API: файл «{Name}» не отдан, HTTP {Status}",
                att.Name, (int)response.StatusCode);
            throw new InvalidOperationException(
                Loc.T("msg.trelloImporter.11", att.Name, (int)response.StatusCode));
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Доски участника → открытые карточки досок, прошедших фильтр по имени
    /// (подстрока, регистронезависимо; пустой фильтр — все доски).</summary>
    private async Task<List<Card>> FetchCardsAsync(string login, string filter,
        Auth auth, CancellationToken ct)
    {
        var boardsJson = await GetAsync(
            $"{BaseUrl}/members/{Uri.EscapeDataString(login)}/boards?fields=name&filter=open&{auth.Query}", ct);
        var boards = ParseBoards(boardsJson, filter);

        var cards = new List<Card>();
        foreach (var (boardId, boardName) in boards)
        {
            // обсуждение карточек приезжает вместе с ними (T-152): отдельного запроса
            // на каждую карточку доски не делается — их были бы сотни
            var cardsJson = await GetAsync(
                $"{BaseUrl}/boards/{Uri.EscapeDataString(boardId)}/cards?fields=name,desc,due,shortUrl" +
                $"&filter=open&actions=commentCard&actions_limit={MaxComments}" +
                $"&action_memberCreator=true&action_memberCreator_fields=username,fullName" +
                $"&{auth.Query}", ct);
            cards.AddRange(ParseCards(cardsJson, boardName));
        }
        // логины авторов обсуждения — одним запросом на участника (T-152)
        await ResolveAuthorsAsync(cards, auth, ct);
        return cards;
    }

    /// <summary>Доски из JSON Trello, отфильтрованные по подстроке имени (пусто — все).</summary>
    public static List<(string Id, string Name)> ParseBoards(string json, string filter)
    {
        var result = new List<(string, string)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var board in doc.RootElement.EnumerateArray())
        {
            var id = Str(board, "id");
            var name = Str(board, "name");
            if (id.Length == 0)
            {
                continue;
            }
            if (filter.Trim().Length == 0
                || name.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                result.Add((id, name));
            }
        }
        return result;
    }

    /// <summary>Карточки из JSON Trello (открытые — фильтр на стороне API).</summary>
    public static List<Card> ParseCards(string json, string boardName)
    {
        var result = new List<Card>();
        using var doc = JsonDocument.Parse(json);
        foreach (var card in doc.RootElement.EnumerateArray())
        {
            var id = Str(card, "id");
            var name = Str(card, "name");
            if (id.Length == 0 || name.Length == 0)
            {
                continue;
            }
            DateTime? due = null;
            if (card.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.String
                && DateTime.TryParse(d.GetString(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                due = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            result.Add(new Card(id, name, Str(card, "desc"), due, boardName, Str(card, "shortUrl"),
                ParseComments(card)));
        }
        return result;
    }

    /// <summary>
    /// Обсуждение карточки из JSON (T-152): действия вида <c>commentCard</c> — автор
    /// (<c>memberCreator.username</c>), текст (<c>data.text</c>) и время. Принимает как саму
    /// карточку (комментарии лежат в свойстве <c>actions</c>), так и массив действий из
    /// <c>/cards/{id}/actions</c>. Порядок — хронологический: Trello отдаёт действия
    /// от новых к старым, а в чате они должны идти так же, как в Trello.
    /// </summary>
    public static List<CardComment> ParseComments(JsonElement element)
    {
        var actions = element;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (!element.TryGetProperty("actions", out actions))
            {
                return [];
            }
        }
        if (actions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<CardComment>();
        foreach (var action in actions.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object
                || !Str(action, "type").Equals("commentCard", StringComparison.Ordinal))
            {
                continue;
            }
            var id = Str(action, "id");
            var text = action.TryGetProperty("data", out var data)
                       && data.ValueKind == JsonValueKind.Object
                ? Str(data, "text")
                : "";
            if (id.Length == 0 || text.Trim().Length == 0)
            {
                continue;
            }
            // имя автора В TRELLO: логин, а если его не отдали — полное имя. Во вложенных
            // действиях (карточка вместе с обсуждением) Trello участника не разворачивает
            // вовсе — остаётся его внутренний ключ, логин спрашивается отдельно
            var author = "";
            if (action.TryGetProperty("memberCreator", out var member)
                && member.ValueKind == JsonValueKind.Object)
            {
                author = Str(member, "username");
                if (author.Length == 0)
                {
                    author = Str(member, "fullName");
                }
            }
            var date = DateTime.TryParse(Str(action, "date"), null,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : DateTime.UtcNow;
            result.Add(new CardComment(id, author, text.Trim(), date, Str(action, "idMemberCreator")));
        }
        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return result;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    /// <summary>Значение секрета и ОТКУДА оно взято: источник нужен в сообщении об ошибке —
    /// без него непонятно, чей ключ Trello отверг (T-134).</summary>
    private (string Value, string Source) ResolveSecretWithSource(string secretRef)
    {
        var (value, source) = KeyResolver is not null
            ? KeyResolver(secretRef)
            : _secrets.ResolveWithSource(secretRef);
        if (string.IsNullOrWhiteSpace(value))
        {
            Logger.Warning("Импорт Trello: значение {SecretRef} не найдено ({Source})",
                secretRef, source);
            throw new InvalidOperationException(
                Loc.T("msg.trelloImporter.12", secretRef, secretRef,
                    _secrets.LocationOf(secretRef), SecretStore.EnvNameOf(secretRef)));
        }
        // пробелы и перевод строки по краям — обычное дело при копировании значения в файл
        // секретов или в переменную окружения, а Trello отвечает на них «invalid key» (T-134).
        // Введённое в форме значение обрезается при записи, это — про остальные два хранилища
        return (value.Trim(), source);
    }

    // --- ключ, токен и разбор отказов Trello (T-134) ---

    /// <summary>
    /// Ключ и токен источника вместе с тем, ОТКУДА они взяты (T-134). Всё, что нужно, чтобы
    /// объяснить человеку отказ Trello: сами значения в лог и в UI не попадают никогда,
    /// а вот ссылка на секрет, хранилище и «форма» значения — попадают, иначе отказ
    /// «invalid key» нечем отличить от «вставили токен в поле ключа».
    /// </summary>
    public sealed record Auth(string Key, string Token, string KeyRef, string TokenRef,
        string KeySource, string TokenSource)
    {
        /// <summary>Хвост запроса к API Trello: ключ и токен параметрами строки запроса.</summary>
        public string Query =>
            $"key={Uri.EscapeDataString(Key)}&token={Uri.EscapeDataString(Token)}";
    }

    /// <summary>Ключ API Trello — ровно 32 шестнадцатеричных знака.</summary>
    public static bool LooksLikeKey(string value) =>
        value.Length == 32 && value.All(Uri.IsHexDigit);

    /// <summary>Токен Trello — 64 шестнадцатеричных знака (прежний вид) либо строка,
    /// начинающаяся с <c>ATTA</c> (нынешний).</summary>
    public static bool LooksLikeToken(string value) =>
        (value.Length == 64 && value.All(Uri.IsHexDigit))
        || (value.StartsWith("ATTA", StringComparison.Ordinal) && value.Length > 32);

    /// <summary>«Форма» значения для сообщений и логов: длина и набор знаков, но НИКОГДА
    /// само значение — по ней видно, что ключ и токен перепутаны местами.</summary>
    public static string Shape(string value)
    {
        var kind = value.All(Uri.IsHexDigit) ? Loc.T("msg.trelloImporter.13")
            : value.StartsWith("ATTA", StringComparison.Ordinal) ? Loc.T("msg.trelloImporter.14")
            : Loc.T("msg.trelloImporter.15");
        var tail = value.Length % 100 is >= 11 and <= 14 ? Loc.T("msg.trelloImporter.16")
            : (value.Length % 10) switch
            {
                1 => Loc.T("msg.trelloImporter.17"),
                2 or 3 or 4 => Loc.T("msg.trelloImporter.18"),
                _ => Loc.T("msg.trelloImporter.16"),
            };
        return $"{value.Length} {tail}, {kind}";
    }

    /// <summary>
    /// Замечание к введённому значению (T-134): пусто — значение похоже на то, что просят.
    /// Проверка МЯГКАЯ — она подсказывает, а не запрещает: формат ключей задаёт Trello,
    /// и запрет на непохожее значение однажды закрыл бы вход вполне рабочему ключу.
    /// </summary>
    public static string SecretHint(string field, string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return "";
        }
        var isKey = field.Trim().Equals("key", StringComparison.OrdinalIgnoreCase);
        if (isKey ? LooksLikeKey(value) : LooksLikeToken(value))
        {
            return "";
        }
        var swapped = isKey ? LooksLikeToken(value) : LooksLikeKey(value);
        var what = isKey
            ? Loc.T("msg.trelloImporter.19")
            : Loc.T("msg.trelloImporter.20");
        var head = swapped
            ? isKey
                ? Loc.T("msg.trelloImporter.21")
                : Loc.T("msg.trelloImporter.22")
            : Loc.T("msg.trelloImporter.23", what);
        return Loc.T("msg.trelloImporter.24", head, Shape(value), KeyPage);
    }

    /// <summary>Ответ Trello не 2xx — отдельный тип, чтобы разобрать причину и объяснить
    /// её человеку там, где известно, чей ключ уехал в запрос (T-134).</summary>
    public sealed class TrelloApiException(int status, string body)
        : InvalidOperationException(Loc.T("msg.trelloImporter.25", status, body))
    {
        public int Status { get; } = status;

        public string Body { get; } = body;
    }

    /// <summary>
    /// Отказ Trello человеческими словами (T-134): что именно не принято, откуда взято
    /// значение и что с этим делать. Раньше наружу уходило «Trello ответил 401: invalid key»,
    /// и по нему нельзя было понять ни какой ключ подставлен, ни где он лежит.
    /// </summary>
    public static string Explain(TrelloApiException ex, Auth auth)
    {
        var body = ex.Body.Trim();
        var lower = body.ToLowerInvariant();
        var where = Loc.T("msg.trelloImporter.26", auth.KeyRef, auth.KeySource);
        if (lower.Contains("invalid key"))
        {
            var hint = SecretHint("key", auth.Key);
            return Loc.T("msg.trelloImporter.27", ex.Status, body, where, Shape(auth.Key)) +
                   (hint.Length > 0 ? hint + " " : Loc.T("msg.trelloImporter.28", KeyPage)) +
                   Loc.T("msg.trelloImporter.29");
        }
        if (lower.Contains("invalid token") || lower.Contains("expired token")
            || lower.Contains("token not valid"))
        {
            var hint = SecretHint("token", auth.Token);
            return Loc.T("msg.trelloImporter.30", ex.Status, body, auth.TokenRef, auth.TokenSource, Shape(auth.Token)) +
                   (hint.Length > 0 ? hint + " " : Loc.T("msg.trelloImporter.31", KeyPage)) +
                   Loc.T("msg.trelloImporter.32");
        }
        if (ex.Status is 401 or 403)
        {
            return Loc.T("msg.trelloImporter.33", ex.Status, body);
        }
        if (ex.Status == 404)
        {
            return Loc.T("msg.trelloImporter.34", body);
        }
        if (ex.Status == 429)
        {
            return Loc.T("msg.trelloImporter.35");
        }
        return Loc.T("msg.trelloImporter.25", ex.Status, body);
    }

    /// <summary>
    /// Объяснение отказа — в лог И в ответ (T-134). В лог именно объяснение целиком, а не
    /// код ответа: уровень вывода в лог по умолчанию «предупреждения», и подробности запроса
    /// (уровень «сведения») до него не доходят — а разбираться по логу приходится как раз тогда,
    /// когда импорт уже не прошёл.
    /// </summary>
    private static InvalidOperationException Explained(TrelloApiException ex, Auth auth)
    {
        var message = Explain(ex, auth);
        Logger.Warning("Импорт Trello отклонён: {Reason}", message);
        return new InvalidOperationException(message, ex);
    }

    /// <summary>Ключ и токен источника + запись в лог, откуда они взяты и какой они формы
    /// (значения не пишутся никогда) — T-134.</summary>
    private Auth ResolveAuth(SourceParams p)
    {
        var (key, keySource) = ResolveSecretWithSource(p.KeyRef);
        var (token, tokenSource) = ResolveSecretWithSource(p.TokenRef);
        var auth = new Auth(key, token, p.KeyRef, p.TokenRef, keySource, tokenSource);
        Logger.Information(
            "Trello: ключ {KeyRef} — {KeySource}, {KeyShape}; токен {TokenRef} — {TokenSource}, {TokenShape}",
            p.KeyRef, keySource, Shape(key), p.TokenRef, tokenSource, Shape(token));
        return auth;
    }

    /// <summary>
    /// ПРОВЕРКА ПОДКЛЮЧЕНИЯ источника (T-134): кнопка формы источника. Trello спрашивают
    /// «кто я» — этого хватает, чтобы отделить неверный ключ от неверного токена и от
    /// нехватки прав, не заводя ни одной задачи. Исключений не бросает: ответ — вердикт.
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
            Logger.Warning("Проверка подключения Trello: {Reason}", ex.Message);
            return (false, ex.Message);
        }

        try
        {
            var json = await GetAsync($"{BaseUrl}/members/me?fields=username,fullName&{auth.Query}", ct);
            using var doc = JsonDocument.Parse(json);
            var username = Str(doc.RootElement, "username");
            var message = Loc.T("msg.trelloImporter.36", username);
            if (p.Login.Length > 0
                && !p.Login.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                message += Loc.T("msg.trelloImporter.37", p.Login, username);
            }
            Logger.Information("Проверка подключения Trello: аккаунт @{Username}", username);
            return (true, message);
        }
        catch (TrelloApiException ex)
        {
            return (false, Explained(ex, auth).Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "Проверка подключения Trello не удалась");
            return (false, Loc.T("msg.trelloImporter.38", ex.Message));
        }
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        var marker = url.IndexOf("key=", StringComparison.Ordinal);
        var safe = marker >= 0 ? url[..marker] + "key=…" : url;
        Logger.Information("Trello API: GET {Url}", safe);
        using var response = await Client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            // отказ Trello раньше не попадал в лог вообще: человек видел только хинт в UI (T-134)
            Logger.Warning("Trello API: {Url} → HTTP {Status}: {Body}",
                safe, (int)response.StatusCode, preview);
            throw new TrelloApiException((int)response.StatusCode, preview);
        }
        return body;
    }
}
