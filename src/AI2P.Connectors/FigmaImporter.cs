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
/// ИМПОРТ ДИЗАЙНА ИЗ FIGMA (T-477-S0) — четвёртый вид источника после Trello, GitHub и GitLab.
/// Задачей становится не карточка, а ОБЛАСТЬ МАКЕТА: узел, названный в ссылке параметром
/// node-id. Экраны узла (и, по выбору, элементы дизайна) отрисовываются самой Figma в PNG,
/// картинки ложатся в uploads/ проекта, а в описание задачи под каждой картинкой выписываются
/// все подписи (текстовые слои) кадра в порядке сверху вниз.
///
/// Отличия от остальных видов, которые видно снаружи:
/// * импорт ТОЛЬКО ПО ССЫЛКЕ (одиночный и обновление), массового импорта по записи
///   справочника нет — у Figma нет «списка задач», а файл целиком бывает огромным;
/// * ссылка без node-id означает ВЕСЬ ФАЙЛ, и он импортируется только с явным
///   подтверждением (<see cref="ImportOptions.WholeFile"/>), иначе — отказ с объяснением;
/// * «только экраны» или «экраны и элементы» выбирает человек (<see cref="ImportOptions.Elements"/>);
///   выбор запоминается в ссылке импорта задачи (ai2p-elements=1, ai2p-whole=1), чтобы
///   обновление повторило тот же импорт;
/// * секрет один — личный токен Figma (право file_content:read): даже публичный файл,
///   открывающийся в браузере без входа, через API без токена не читается;
/// * обсуждения нет — комментарии макета в чат не переносятся.
///
/// ЭКРАН — фрейм верхнего уровня внутри указанного узла-контейнера (страница, секция, группа
/// проходятся насквозь); ссылка на сам фрейм — это один экран; не нашлось ни одного фрейма —
/// экраном считается сам узел. ЭЛЕМЕНТ —
/// компонент или экземпляр компонента (внутрь не спускаемся) и вложенные фреймы/группы.
/// </summary>
public sealed class FigmaImporter
{
    /// <summary>Код вида в справочнике импортов.</summary>
    public const string Kind = ImportKinds.Figma;

    private const string BaseUrl = "https://api.figma.com/v1";

    /// <summary>Страница, где выпускается личный токен (Settings → Security).</summary>
    public const string KeyPage = "https://www.figma.com/settings";

    /// <summary>Ссылка на токен у источника, у которого её нет в params_json. Ссылку
    /// Trello брать нельзя — это чужой токен.</summary>
    public const string DefaultTokenRef = "figma.token";

    /// <summary>Отметки выбора в ссылке импорта: по ним обновление повторяет тот же импорт.</summary>
    public const string ElementsMark = "ai2p-elements";
    public const string WholeMark = "ai2p-whole";

    /// <summary>Предел картинок за один импорт: «все элементы» большого экрана — это сотни
    /// фреймов автолейаута, и без предела задача получила бы тысячи файлов.</summary>
    public const int MaxImages = 300;

    /// <summary>Узлов на один запрос отрисовки: длинная строка ids и тяжёлый рендер
    /// упираются в таймаут Figma раньше, чем в предел длины адреса.</summary>
    private const int RenderBatch = 20;

    /// <summary>Элемент меньше этого (по любой стороне, px макета) картинкой не выносится:
    /// это разделители и точки, а не элементы дизайна.</summary>
    private const double MinElementSize = 8;

    /// <summary>Предел площади отрисовки при масштабе 2 (пикселей): больше — масштаб 1,
    /// иначе Figma не рисует картинку вовсе (её предел — 32 мегапикселя).</summary>
    private const double MaxPixelsAt2x = 25_000_000;

    private readonly TaskService _tasks;
    private readonly ImportSourceService _sources;
    private readonly SecretStore _secrets;
    private readonly EventStore _events;
    private readonly FileStore _files;

    public FigmaImporter(TaskService tasks, ImportSourceService sources, SecretStore secrets,
        EventStore events, FileStore files)
    {
        _tasks = tasks;
        _sources = sources;
        _secrets = secrets;
        _events = events;
        _files = files;
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(FigmaImporter));

    /// <summary>Откуда брать токен: подставляется контекстом организации
    /// (<c>ModelKeyService.ResolveWithSource</c>). Не подставлен — читаем secrets.json.</summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    /// <summary>Подменный HttpClient — только для тестов: живая Figma в прогоне не дёргается.</summary>
    public HttpClient? HttpOverride { get; set; }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private HttpClient Client => HttpOverride ?? Http;

    // ---------------------------------------------------------------- данные

    /// <summary>Выбор человека: null — не задано (тогда берётся отметка из ссылки).</summary>
    public sealed record ImportOptions(bool? WholeFile = null, bool? Elements = null);

    /// <summary>Разобранная ссылка Figma: ключ файла (у ветки — ключ ветки), узел (пусто —
    /// весь файл) и отметки выбора, запомненные в ссылке импорта.</summary>
    public sealed record FigmaRef(string FileKey, string NodeId, string Url, bool WholeMark,
        bool ElementsMark)
    {
        public bool HasNode => NodeId.Length > 0;
    }

    /// <summary>Узел макета (нужные импорту поля).</summary>
    public sealed class Node
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Visible { get; init; } = true;
        public string Characters { get; init; } = "";
        public string ComponentId { get; init; } = "";
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public List<Node> Children { get; init; } = [];
    }

    /// <summary>Кадр, который становится картинкой: экран или элемент экрана.</summary>
    public sealed record Frame(Node Node, List<string> Texts, List<Frame> Elements);

    /// <summary>Содержимое импорта для формы задачи или инструмента агента: картинки уже
    /// скачаны в uploads/ проекта, ссылки на них — в описании.</summary>
    public sealed record ImportedDesign(string Title, string Description, string ExternalRef,
        List<string> Files, string Url, int Screens, int Elements);

    /// <summary>Итог обновления задачи из Figma — для сообщения человеку.</summary>
    public sealed record RefreshResult(string DisplayId, string Title, int Files);

    /// <summary>Параметры источника: у Figma полей нет, только ссылка на токен.</summary>
    public sealed record SourceParams(string TokenRef)
    {
        public static SourceParams Parse(string paramsJson)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson);
            var tokenRef = doc.RootElement.ValueKind == JsonValueKind.Object
                           && doc.RootElement.TryGetProperty("tokenRef", out var v)
                           && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";
            return new SourceParams(tokenRef.Length > 0 ? tokenRef : DefaultTokenRef);
        }
    }

    // ------------------------------------------------------------ разбор ссылки

    /// <summary>
    /// Ссылка вида https://www.figma.com/design/&lt;ключ&gt;/&lt;имя&gt;?node-id=1-2 (а также
    /// /file/, /proto/, /board/, /slides/; у ветки — …/&lt;ключ&gt;/branch/&lt;ключ ветки&gt;/…).
    /// null — это не ссылка на файл Figma. node-id в адресе пишется через «-», API ждёт «:».
    /// </summary>
    public static FigmaRef? ParseUrl(string? url)
    {
        if (url is null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("figma.com", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".figma.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || parts[0].ToLowerInvariant() is not ("design" or "file" or "proto" or "board" or "slides"))
        {
            return null;
        }
        var key = parts[1];
        var branch = Array.FindIndex(parts, p => p.Equals("branch", StringComparison.OrdinalIgnoreCase));
        if (branch > 0 && branch + 1 < parts.Length)
        {
            key = parts[branch + 1];
        }
        if (key.Length < 10 || !key.All(char.IsLetterOrDigit))
        {
            return null;
        }
        var query = ParseQuery(uri.Query);
        var node = query.TryGetValue("node-id", out var n) ? n.Trim().Replace('-', ':') : "";
        return new FigmaRef(key, node, url.Trim(),
            query.TryGetValue(WholeMark, out var w) && w == "1",
            query.TryGetValue(ElementsMark, out var e) && e == "1");
    }

    /// <summary>Умеет ли импорт разобрать эту ссылку — по ней выбирается процедура
    /// одиночного импорта и обновления.</summary>
    public static bool CanRefresh(string? importUrl) =>
        importUrl is { Length: > 0 } && ParseUrl(importUrl) is not null;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[name] = value;
        }
        return result;
    }

    /// <summary>Ссылка импорта задачи: адрес без хвоста (t=…, page-id и прочее), с узлом
    /// и отметками выбора — по ней обновление повторит ровно тот же импорт.</summary>
    public static string ImportUrlOf(FigmaRef r, bool wholeFile, bool elements)
    {
        var uri = new Uri(r.Url);
        var query = new List<string>();
        if (r.HasNode)
        {
            query.Add("node-id=" + r.NodeId.Replace(':', '-'));
        }
        else if (wholeFile)
        {
            query.Add(WholeMark + "=1");
        }
        if (elements)
        {
            query.Add(ElementsMark + "=1");
        }
        var path = uri.GetLeftPart(UriPartial.Path);
        return query.Count == 0 ? path : path + "?" + string.Join("&", query);
    }

    /// <summary>Внешний ключ задачи: файл и узел («*» — весь файл). Выбор «экраны /
    /// элементы» в ключ не входит: это та же область макета, и дубль заводить незачем.</summary>
    public static string ExternalRefOf(FigmaRef r) =>
        $"figma:{r.FileKey}:{(r.HasNode ? r.NodeId : "*")}";

    // -------------------------------------------------------- импорт по ссылке

    /// <summary>
    /// Импорт области макета по ссылке: узел читается из Figma, экраны (и элементы)
    /// отрисовываются в PNG и скачиваются в uploads/ проекта, подписи выписываются под
    /// картинками. Задача здесь НЕ создаётся: результат подставляется в форму (UI) или
    /// используется инструментом агента <c>import_task_from_url</c>.
    /// </summary>
    public async Task<ImportedDesign> ImportContentAsync(Project project, string url,
        string? sourceId, ImportOptions options, CancellationToken ct = default)
    {
        var r = ParseUrl(url) ?? throw new InvalidOperationException(Loc.T("msg.figmaImporter.3"));
        var wholeFile = options.WholeFile ?? r.WholeMark;
        var elements = options.Elements ?? r.ElementsMark;
        if (!r.HasNode && !wholeFile)
        {
            throw new InvalidOperationException(Loc.T("msg.figmaImporter.14"));
        }
        var auth = ResolveAuth(ResolveSourceParams(sourceId));
        var importUrl = ImportUrlOf(r, wholeFile, elements);
        // ДОИМПОРТ (T-481-S0): картинки, скачанные до отказа Figma по пределу запросов,
        // лежат в uploads/ проекта, а опись «узел → файл» — в файле продолжения; их не
        // отрисовываем и не качаем заново
        var resume = LoadResume(project, r);

        Logger.Information("Импорт Figma: файл {File}, узел {Node}, элементы {Elements}, проект {Project}, "
                           + "уже скачано {Done}",
            r.FileKey, r.HasNode ? r.NodeId : "(весь файл)", elements, project.Name, resume.Count);
        string fileName;
        Node root;
        try
        {
            (fileName, root) = await FetchRootAsync(r, auth, ct);
        }
        catch (FigmaApiException ex) when (ex.Status == 429)
        {
            // до структуры макета не дошли: задача получает только шапку и ссылку
            var limit = Explain(ex, auth);
            var pending = new ImportedDesign(Loc.T("msg.figmaImporter.33", r.FileKey),
                PendingBanner(ex) + "\n\n" + Loc.T("msg.figmaImporter.17", importUrl) + "\n",
                ExternalRefOf(r), [], importUrl, 0, 0);
            throw new FigmaRateLimitException(limit, ex, pending, 0, 0);
        }
        catch (FigmaApiException ex)
        {
            throw Explained(ex, auth);
        }

        var screens = BuildFrames(root, elements);
        var all = Flatten(screens).ToList();
        var limited = all.Count > MaxImages;
        var toRender = all.Take(MaxImages).ToList();
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<string>();
        foreach (var f in toRender)
        {
            if (resume.TryGetValue(f.Node.Id, out var done) && File.Exists(_files.Abs(done.Rel)))
            {
                links[f.Node.Id] = LinkOf(done.Rel);
                files.Add(done.Name);
            }
        }
        var rest = toRender.Where(f => !links.ContainsKey(f.Node.Id)).ToList();
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        FigmaApiException? limitHit = null;
        try
        {
            await RenderAsync(r.FileKey, rest.Select(f => f.Node).ToList(), auth, images, ct);
        }
        catch (FigmaApiException ex) when (ex.Status == 429)
        {
            // то, что Figma успела отрисовать до отказа, всё равно скачиваем
            limitHit = ex;
        }
        catch (FigmaApiException ex)
        {
            throw Explained(ex, auth);
        }
        var (newLinks, newFiles) = await DownloadAsync(project, rest, images, files.Count, ct);
        foreach (var (node, link) in newLinks)
        {
            links[node] = link.Link;
            files.Add(link.Name);
            resume[node] = new ResumeEntry(link.Rel, link.Name);
        }

        var title = r.HasNode && root.Name.Length > 0 && !root.Name.Equals(fileName, StringComparison.Ordinal)
            ? $"{fileName} — {root.Name}"
            : fileName.Length > 0 ? fileName : root.Name;
        var description = DescriptionOf(fileName, r.HasNode ? root.Name : Loc.T("msg.figmaImporter.23"),
            importUrl, screens, links, limited);
        var elementCount = all.Count - screens.Count;
        var design = new ImportedDesign(title, description, ExternalRefOf(r), files, importUrl,
            screens.Count, elementCount);
        if (limitHit is not null)
        {
            SaveResume(project, r, resume);
            Logger.Warning("Импорт Figma прерван пределом запросов: скачано {Done} из {Total}",
                links.Count, toRender.Count);
            design = design with { Description = PendingBanner(limitHit) + "\n\n" + description };
            throw new FigmaRateLimitException(Explain(limitHit, auth), limitHit, design, links.Count,
                toRender.Count);
        }
        DropResume(project, r);
        Logger.Information("Импорт Figma: «{Title}», экранов {Screens}, элементов {Elements}, "
                           + "скачано картинок {Files} (новых {New})", title, screens.Count, elementCount,
            files.Count, newFiles);
        return design;
    }

    // ------------------------------------------- продолжение импорта (T-481-S0)

    /// <summary>Отказ Figma по пределу запросов посреди импорта: в нём уже собранная часть
    /// (описание с пометкой «ожидает продолжения», скачанные картинки) и сколько готово.</summary>
    public sealed class FigmaRateLimitException(string message, FigmaApiException inner,
        ImportedDesign design, int done, int total) : InvalidOperationException(message, inner)
    {
        public ImportedDesign Design { get; } = design;
        public DateTime RetryAt { get; } = inner.RetryAt;
        public int Done { get; } = done;
        public int Total { get; } = total;
    }

    /// <summary>Пометка в начале описания: импорт не закончен, когда и как его продолжить.</summary>
    private static string PendingBanner(FigmaApiException ex) =>
        Loc.T("msg.figmaImporter.32", When(ex.RetryAt), Or(ex.LimitType), Or(ex.PlanTier));

    private static string When(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    private static string Or(string value) => value.Length > 0 ? value : Loc.T("msg.figmaImporter.30");

    /// <summary>
    /// Сохранить незаконченный импорт задачей: новая задача создаётся в состоянии «черновик»
    /// (уже заведённая по той же области макета — обновляется), с описанием, где сказано,
    /// что импорт ждёт продолжения и с какого времени. Ссылка импорта хранит выбор человека
    /// (весь файл, элементы), поэтому кнопка «обновить» повторит ровно тот же импорт.
    /// Возвращает текст для человека: причина отказа и что делать дальше.
    /// </summary>
    public string SavePending(Project project, FigmaRateLimitException ex, string? actorId)
    {
        var d = ex.Design;
        var task = _tasks.FindByExternalRef(project.Id, d.ExternalRef);
        if (task is null)
        {
            task = _tasks.Create(new TaskItem
            {
                ProjectId = project.Id,
                Title = d.Title,
                Status = TaskStatuses.Draft,
                ExternalRef = d.ExternalRef,
                ImportUrl = d.Url,
            }, d.Description, "", actorId);
        }
        else
        {
            SavePendingInto(task, ex, actorId);
        }
        Logger.Information("Импорт Figma отложен: задача {Task}, продолжение после {RetryAt}",
            task.DisplayId, ex.RetryAt);
        return ex.Message + "\n" + Loc.T("msg.figmaImporter.34", task.DisplayId) + " "
               + Loc.T("msg.figmaImporter.31", task.DisplayId, ex.Done, ex.Total, When(ex.RetryAt));
    }

    /// <summary>Текст окна импорта, когда задачи ещё нет (T-485-S0): причина отказа и что
    /// собранное сохранится черновиком кнопкой «Сохранить».</summary>
    public string PendingMessage(FigmaRateLimitException ex) =>
        ex.Message + "\n" + Loc.T("msg.figmaImporter.35", ex.Done, ex.Total, When(ex.RetryAt));

    private void SavePendingInto(TaskItem task, FigmaRateLimitException ex, string? actorId)
    {
        var d = ex.Design;
        if (d.Screens > 0 || d.Elements > 0)
        {
            task.Title = d.Title;
        }
        task.ExternalRef = d.ExternalRef;
        task.ImportUrl = d.Url;
        _tasks.Update(task, d.Description, _tasks.ReadAcceptance(task), actorId);
    }

    private sealed record ResumeEntry(string Rel, string Name);

    /// <summary>Файл продолжения области макета: uploads/.figma-resume/&lt;файл&gt;_&lt;узел&gt;.json.</summary>
    private string ResumePath(Project project, FigmaRef r) =>
        _files.Abs(Path.Combine("projects", project.Slug ?? "_no_project", "uploads", ".figma-resume",
            $"{r.FileKey}_{(r.HasNode ? r.NodeId.Replace(':', '-') : "all")}.json"));

    private Dictionary<string, ResumeEntry> LoadResume(Project project, FigmaRef r)
    {
        var path = ResumePath(project, r);
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, ResumeEntry>>(File.ReadAllText(path))
                       ?? new Dictionary<string, ResumeEntry>(StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Logger.Warning("Импорт Figma: файл продолжения {Path} не прочитан ({Reason})", path, ex.Message);
        }
        return new Dictionary<string, ResumeEntry>(StringComparer.Ordinal);
    }

    private void SaveResume(Project project, FigmaRef r, Dictionary<string, ResumeEntry> state)
    {
        var path = ResumePath(project, r);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }

    /// <summary>Импорт закончен целиком — опись больше не нужна: следующее обновление
    /// обязано отрисовать макет заново, он мог измениться.</summary>
    private void DropResume(Project project, FigmaRef r)
    {
        var path = ResumePath(project, r);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string LinkOf(string rel) => "api/files/raw?path=" + Uri.EscapeDataString(rel);

    /// <summary>
    /// ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ FIGMA: область макета перечитывается по ссылке импорта задачи
    /// (с тем же выбором экраны/элементы, что запомнен в ссылке), и в задачу ложатся свежие
    /// заголовок и описание вместе с заново отрисованными картинками. Критерии приёмки,
    /// исполнители, статус и связи не трогаются.
    /// </summary>
    public async Task<RefreshResult> RefreshTaskAsync(Project project, TaskItem task,
        string? sourceId, string? actorId, CancellationToken ct = default)
    {
        var url = TaskService.NormalizeImportUrl(task.ImportUrl)
                  ?? throw new InvalidOperationException(Loc.T("msg.figmaImporter.24", task.DisplayId));
        Logger.Information("Обновление задачи {Task} из Figma по ссылке {Url}", task.DisplayId, url);
        ImportedDesign imported;
        try
        {
            imported = await ImportContentAsync(project, url, sourceId, new ImportOptions(), ct);
        }
        catch (FigmaRateLimitException ex)
        {
            // доимпорт снова упёрся в предел: скачанное за этот заход остаётся в задаче,
            // пометка «ожидает продолжения» получает новое время
            SavePendingInto(task, ex, actorId);
            throw new InvalidOperationException(ex.Message + "\n"
                + Loc.T("msg.figmaImporter.31", task.DisplayId, ex.Done, ex.Total, When(ex.RetryAt)), ex);
        }
        task.Title = imported.Title;
        task.ExternalRef = imported.ExternalRef;
        task.ImportUrl = imported.Url;
        _tasks.Update(task, imported.Description, _tasks.ReadAcceptance(task), actorId);
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
            }),
        });
        return new RefreshResult(task.DisplayId, task.Title, imported.Files.Count);
    }

    // ------------------------------------------------ экраны, элементы, подписи

    /// <summary>Экраны узла (с элементами, если просили) — отделено от сети, тестируемо.</summary>
    public static List<Frame> BuildFrames(Node root, bool elements)
    {
        // ссылка на сам фрейм — это ОДИН экран: его вложенные фреймы — части экрана
        // (автолейаут), а не соседние экраны; набор экранов лежит в странице или секции
        var screenNodes = new List<Node>();
        if (!IsScreen(root))
        {
            CollectScreens(root, screenNodes);
        }
        if (screenNodes.Count == 0)
        {
            screenNodes.Add(root);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return screenNodes
            .Select(s => new Frame(s, TextsOf(s), elements ? ElementsOf(s, seen) : []))
            .ToList();
    }

    private static bool IsScreen(Node n) =>
        n.Type is "FRAME" or "COMPONENT" or "COMPONENT_SET" or "INSTANCE";

    private static bool IsContainer(Node n) =>
        n.Type is "DOCUMENT" or "CANVAS" or "SECTION" or "GROUP";

    /// <summary>Экраны — фреймы верхнего уровня под узлом-контейнером; контейнеры (страница,
    /// секция, группа) проходятся насквозь.</summary>
    private static void CollectScreens(Node node, List<Node> screens)
    {
        foreach (var child in Ordered(node.Children).Where(c => c.Visible))
        {
            if (IsScreen(child))
            {
                screens.Add(child);
            }
            else if (IsContainer(child))
            {
                CollectScreens(child, screens);
            }
        }
    }

    /// <summary>Элементы экрана: компоненты и их экземпляры (внутрь не спускаемся) и вложенные
    /// фреймы/группы. Одинаковые экземпляры одного компонента с одинаковыми подписями
    /// выносятся один раз на весь импорт.</summary>
    private static List<Frame> ElementsOf(Node screen, HashSet<string> seen)
    {
        var result = new List<Frame>();
        void Walk(Node node)
        {
            foreach (var child in Ordered(node.Children).Where(c => c.Visible))
            {
                var big = child.Width >= MinElementSize && child.Height >= MinElementSize;
                if (child.Type is "INSTANCE" or "COMPONENT" or "COMPONENT_SET")
                {
                    var texts = TextsOf(child);
                    var key = child.ComponentId.Length > 0
                        ? child.ComponentId + "|" + string.Join("\u0001", texts)
                        : child.Id;
                    if (big && seen.Add(key))
                    {
                        result.Add(new Frame(child, texts, []));
                    }
                }
                else if (child.Type is "FRAME" or "GROUP" or "SECTION")
                {
                    if (big)
                    {
                        result.Add(new Frame(child, TextsOf(child), []));
                    }
                    Walk(child);
                }
            }
        }
        Walk(screen);
        return result;
    }

    /// <summary>Подписи кадра — тексты видимых текстовых слоёв, сверху вниз и слева направо.</summary>
    public static List<string> TextsOf(Node node)
    {
        var found = new List<Node>();
        void Walk(Node n)
        {
            foreach (var child in n.Children.Where(c => c.Visible))
            {
                if (child.Type == "TEXT")
                {
                    if (child.Characters.Trim().Length > 0)
                    {
                        found.Add(child);
                    }
                }
                else
                {
                    Walk(child);
                }
            }
        }
        if (node.Type == "TEXT")
        {
            found.Add(node);
        }
        Walk(node);
        return Ordered(found).Select(t => t.Characters.Trim()).ToList();
    }

    /// <summary>Порядок чтения: сверху вниз, в строке — слева направо (полпикселя допуска,
    /// чтобы строки одного ряда не разъезжались из-за дробных координат).</summary>
    private static IEnumerable<Node> Ordered(IEnumerable<Node> nodes) =>
        nodes.OrderBy(n => Math.Round(n.Y)).ThenBy(n => n.X);

    private static IEnumerable<Frame> Flatten(IEnumerable<Frame> screens)
    {
        foreach (var screen in screens)
        {
            yield return screen;
            foreach (var element in screen.Elements)
            {
                yield return element;
            }
        }
    }

    /// <summary>Описание задачи: шапка-источник, по разделу на экран — картинка и подписи
    /// под ней, следом элементы экрана тем же видом. Картинки нет — так и сказано.</summary>
    public static string DescriptionOf(string fileName, string nodeName, string url,
        IReadOnlyList<Frame> screens, IReadOnlyDictionary<string, string> links, bool limited)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("msg.figmaImporter.16", fileName, nodeName, url));
        var number = 0;
        foreach (var screen in screens)
        {
            number++;
            sb.AppendLine();
            sb.AppendLine($"## {number}. {Heading(screen.Node.Name)}");
            AppendFrame(sb, screen, links);
            if (screen.Elements.Count == 0)
            {
                continue;
            }
            sb.AppendLine();
            sb.AppendLine("### " + Loc.T("msg.figmaImporter.20", Heading(screen.Node.Name)));
            var sub = 0;
            foreach (var element in screen.Elements)
            {
                sub++;
                sb.AppendLine();
                sb.AppendLine($"#### {number}.{sub}. {Heading(element.Node.Name)}");
                AppendFrame(sb, element, links);
            }
        }
        if (limited)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("msg.figmaImporter.22", MaxImages));
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(Loc.T("msg.figmaImporter.17", url));
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendFrame(StringBuilder sb, Frame frame, IReadOnlyDictionary<string, string> links)
    {
        sb.AppendLine();
        if (links.TryGetValue(frame.Node.Id, out var link))
        {
            sb.AppendLine($"![{Heading(frame.Node.Name)}]({link})");
        }
        else
        {
            sb.AppendLine("_" + Loc.T("msg.figmaImporter.21") + "_");
        }
        sb.AppendLine();
        if (frame.Texts.Count == 0)
        {
            sb.AppendLine(Loc.T("msg.figmaImporter.19"));
            return;
        }
        sb.AppendLine(Loc.T("msg.figmaImporter.18"));
        foreach (var text in frame.Texts)
        {
            // многострочная подпись остаётся одним пунктом: переводы строк — мягкие
            sb.AppendLine("- " + text.Replace("\r", "").Replace("\n", "  \n  "));
        }
    }

    /// <summary>Имя слоя для заголовка и подписи картинки: без переводов строк и скобок,
    /// ломающих разметку ссылки.</summary>
    private static string Heading(string name)
    {
        var clean = name.Replace('\n', ' ').Replace('\r', ' ').Replace('[', '(').Replace(']', ')').Trim();
        return clean.Length > 0 ? clean : "—";
    }

    // ------------------------------------------------------------------ сеть

    /// <summary>Имя файла и корневой узел: указанный в ссылке либо документ целиком.</summary>
    private async Task<(string FileName, Node Root)> FetchRootAsync(FigmaRef r, Auth auth,
        CancellationToken ct)
    {
        if (r.HasNode)
        {
            var json = await GetAsync(
                $"{BaseUrl}/files/{r.FileKey}/nodes?ids={Uri.EscapeDataString(r.NodeId)}", auth, ct);
            using var doc = JsonDocument.Parse(json);
            var fileName = Str(doc.RootElement, "name");
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes)
                || !nodes.TryGetProperty(r.NodeId, out var entry)
                || entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("document", out var document))
            {
                throw new InvalidOperationException(Loc.T("msg.figmaImporter.15", r.NodeId));
            }
            return (fileName, ParseNode(document));
        }
        var whole = await GetAsync($"{BaseUrl}/files/{r.FileKey}", auth, ct);
        using var wholeDoc = JsonDocument.Parse(whole);
        if (!wholeDoc.RootElement.TryGetProperty("document", out var root))
        {
            throw new InvalidOperationException(Loc.T("msg.figmaImporter.15", "document"));
        }
        return (Str(wholeDoc.RootElement, "name"), ParseNode(root));
    }

    /// <summary>Узел из JSON Figma (рекурсивно) — отделено от сети, тестируемо.</summary>
    public static Node ParseNode(JsonElement e)
    {
        double x = 0, y = 0, w = 0, h = 0;
        if (e.TryGetProperty("absoluteBoundingBox", out var box) && box.ValueKind == JsonValueKind.Object)
        {
            x = Num(box, "x");
            y = Num(box, "y");
            w = Num(box, "width");
            h = Num(box, "height");
        }
        var children = new List<Node>();
        if (e.TryGetProperty("children", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in list.EnumerateArray())
            {
                children.Add(ParseNode(child));
            }
        }
        return new Node
        {
            Id = Str(e, "id"),
            Name = Str(e, "name"),
            Type = Str(e, "type"),
            Visible = !(e.TryGetProperty("visible", out var v) && v.ValueKind == JsonValueKind.False),
            Characters = Str(e, "characters"),
            ComponentId = Str(e, "componentId"),
            X = x,
            Y = y,
            Width = w,
            Height = h,
            Children = children,
        };
    }

    /// <summary>Отрисовка узлов в PNG: id узла → временная ссылка на картинку. Узлы, которые
    /// Figma не отрисовала (пустые, слишком большие), в ответе не появляются.</summary>
    private async Task RenderAsync(string fileKey, List<Node> nodes, Auth auth,
        Dictionary<string, string> result, CancellationToken ct)
    {
        // result заполняется по ходу: при отказе посреди пачек уже отрисованное остаётся
        foreach (var group in nodes.GroupBy(n => n.Width * n.Height * 4 <= MaxPixelsAt2x ? 2 : 1))
        {
            foreach (var batch in group.Chunk(RenderBatch))
            {
                var ids = string.Join(",", batch.Select(n => n.Id));
                var json = await GetAsync(
                    $"{BaseUrl}/images/{fileKey}?ids={Uri.EscapeDataString(ids)}&format=png&scale={group.Key}",
                    auth, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("images", out var images)
                    && images.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in images.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } link)
                        {
                            result[p.Name] = link;
                        }
                    }
                }
            }
        }
    }

    private sealed record Downloaded(string Link, string Rel, string Name);

    /// <summary>Скачать отрисованные картинки в uploads/ проекта: id узла → ссылка для
    /// описания. Картинка не скачалась — не повод ронять импорт: подписи важнее.
    /// Нумерация имён продолжается после уже скачанных (<paramref name="numberFrom"/>).</summary>
    private async Task<(Dictionary<string, Downloaded> Links, int Count)> DownloadAsync(
        Project project, List<Frame> frames, Dictionary<string, string> images, int numberFrom,
        CancellationToken ct)
    {
        var links = new Dictionary<string, Downloaded>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(6);
        var number = numberFrom;
        var jobs = frames.Where(f => images.ContainsKey(f.Node.Id)).Select(async f =>
        {
            var name = $"figma-{Interlocked.Increment(ref number):D3}-{f.Node.Name}";
            name = (name.Length > 80 ? name[..80] : name) + ".png";
            await gate.WaitAsync(ct);
            try
            {
                var bytes = await Client.GetByteArrayAsync(images[f.Node.Id], ct);
                var rel = _files.SaveUpload(project.Slug, name, bytes);
                lock (links)
                {
                    links[f.Node.Id] = new Downloaded(LinkOf(rel), rel, name);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                           && !ct.IsCancellationRequested)
            {
                Logger.Warning("Импорт Figma: картинка узла {Node} не скачана ({Reason})", f.Node.Id, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(jobs);
        return (links, links.Count);
    }

    private async Task<string> GetAsync(string url, Auth auth, CancellationToken ct)
    {
        Logger.Information("Figma API: GET {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // личный токен Figma уезжает своим заголовком, а не строкой запроса
        request.Headers.TryAddWithoutValidation("X-Figma-Token", auth.Token);
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            // при 429 Figma сообщает заголовками, сколько ждать и какой предел исчерпан (T-481-S0)
            var retryAfter = response.Headers.RetryAfter is { } ra
                ? ra.Delta ?? (ra.Date is { } at ? at - DateTimeOffset.UtcNow : null)
                : null;
            var limitType = Header(response, "X-Figma-Rate-Limit-Type");
            var planTier = Header(response, "X-Figma-Plan-Tier");
            Logger.Warning("Figma API: {Url} → HTTP {Status}: {Body} (Retry-After {RetryAfter}, "
                           + "X-Figma-Rate-Limit-Type {LimitType}, X-Figma-Plan-Tier {PlanTier})",
                url, (int)response.StatusCode, preview, retryAfter?.TotalSeconds, limitType, planTier);
            throw new FigmaApiException((int)response.StatusCode, preview, retryAfter, limitType, planTier);
        }
        return body;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values).Trim() : "";

    private static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : "";

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    // ------------------------------------------- источник, токен, отказы Figma

    /// <summary>Параметры источника: по id, а без него — единственный активный figma-источник.</summary>
    private SourceParams ResolveSourceParams(string? sourceId)
    {
        ImportSource? source;
        if (sourceId is { Length: > 0 })
        {
            source = _sources.Get(sourceId)
                     ?? throw new InvalidOperationException(Loc.T("msg.figmaImporter.1"));
        }
        else
        {
            var figma = _sources.List().Where(s => s.IsActive && IsFigma(s)).ToList();
            source = figma.Count switch
            {
                0 => throw new InvalidOperationException(Loc.T("msg.figmaImporter.4")),
                1 => figma[0],
                _ => throw new InvalidOperationException(Loc.T("msg.figmaImporter.5")),
            };
        }
        if (!IsFigma(source))
        {
            throw new InvalidOperationException(Loc.T("msg.figmaImporter.2", source.Kind));
        }
        return SourceParams.Parse(source.ParamsJson);
    }

    /// <summary>Вид записи справочника — figma.</summary>
    public static bool IsFigma(ImportSource source) =>
        string.Equals(source.Kind, Kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>Токен вместе с тем, ОТКУДА он взят (приём T-134).</summary>
    public sealed record Auth(string Token, string TokenRef, string TokenSource);

    private Auth ResolveAuth(SourceParams p)
    {
        var (value, source) = KeyResolver is not null
            ? KeyResolver(p.TokenRef)
            : _secrets.ResolveWithSource(p.TokenRef);
        if (string.IsNullOrWhiteSpace(value))
        {
            Logger.Warning("Импорт Figma: значение {SecretRef} не найдено ({Source})", p.TokenRef, source);
            throw new InvalidOperationException(Loc.T("msg.figmaImporter.6", p.TokenRef,
                _secrets.LocationOf(p.TokenRef), SecretStore.EnvNameOf(p.TokenRef)));
        }
        var token = value.Trim();
        Logger.Information("Figma: токен {TokenRef} — {TokenSource}, {TokenShape}", p.TokenRef, source, Shape(token));
        return new Auth(token, p.TokenRef, source);
    }

    /// <summary>Личные токены Figma начинаются с figd_.</summary>
    public static bool LooksLikeToken(string value) =>
        value.Trim().StartsWith("figd_", StringComparison.Ordinal) && value.Trim().Length > 20;

    /// <summary>«Форма» значения для сообщений и логов — длина и префикс, но не само значение.</summary>
    public static string Shape(string value)
    {
        value = value.Trim();
        return value.StartsWith("figd_", StringComparison.Ordinal)
            ? Loc.T("msg.figmaImporter.26", value.Length)
            : Loc.T("msg.figmaImporter.27", value.Length);
    }

    /// <summary>Замечание к введённому токену: пусто — похоже на токен Figma. Мягкая проверка.</summary>
    public static string SecretHint(string value)
    {
        value = value.Trim();
        return value.Length == 0 || LooksLikeToken(value)
            ? ""
            : Loc.T("msg.figmaImporter.25", Shape(value), KeyPage);
    }

    /// <summary>Ответ Figma не 2xx.</summary>
    public sealed class FigmaApiException(int status, string body, TimeSpan? retryAfter = null,
        string limitType = "", string planTier = "")
        : InvalidOperationException(Loc.T("msg.figmaImporter.7", status, body))
    {
        public int Status { get; } = status;

        public string Body { get; } = body;

        /// <summary>Заголовок Retry-After: сколько ждать до следующего запроса (null — не сообщён).</summary>
        public TimeSpan? RetryAfter { get; } = retryAfter;

        /// <summary>Когда можно продолжать (UTC). Retry-After не сообщён — через минуту.</summary>
        public DateTime RetryAt { get; } = DateTime.UtcNow + (retryAfter is { } d && d > TimeSpan.Zero
            ? d
            : TimeSpan.FromMinutes(1));

        /// <summary>Заголовок X-Figma-Rate-Limit-Type (low, high…).</summary>
        public string LimitType { get; } = limitType;

        /// <summary>Заголовок X-Figma-Plan-Tier — тариф, определивший предел.</summary>
        public string PlanTier { get; } = planTier;
    }

    /// <summary>Отказ Figma человеческими словами. Негодный токен Figma отдаёт как 403
    /// «Invalid token», а не 401 — поэтому смотрим и на текст.</summary>
    public static string Explain(FigmaApiException ex, Auth auth)
    {
        var lower = ex.Body.ToLowerInvariant();
        if (ex.Status == 401 || lower.Contains("invalid token") || lower.Contains("token expired"))
        {
            return Loc.T("msg.figmaImporter.8", ex.Status, ex.Body.Trim(), auth.TokenRef, auth.TokenSource,
                Shape(auth.Token));
        }
        if (ex.Status == 429)
        {
            // подробности предела — из заголовков ответа, в ПЕРВОМ же сообщении (T-481-S0)
            var wait = ex.RetryAfter is { } d
                ? ((int)Math.Ceiling(d.TotalSeconds)).ToString(System.Globalization.CultureInfo.CurrentCulture)
                : Loc.T("msg.figmaImporter.30");
            return Loc.T("msg.figmaImporter.29", wait, When(ex.RetryAt), Or(ex.LimitType), Or(ex.PlanTier),
                ex.Body.Trim());
        }
        if (ex.Status == 403)
        {
            return Loc.T("msg.figmaImporter.9", ex.Status, ex.Body.Trim());
        }
        if (ex.Status == 404)
        {
            return Loc.T("msg.figmaImporter.10", ex.Body.Trim());
        }
        return Loc.T("msg.figmaImporter.7", ex.Status, ex.Body.Trim());
    }

    private static InvalidOperationException Explained(FigmaApiException ex, Auth auth)
    {
        var message = Explain(ex, auth);
        Logger.Warning("Импорт Figma отклонён: {Reason}", message);
        return new InvalidOperationException(message, ex);
    }

    /// <summary>ПРОВЕРКА ПОДКЛЮЧЕНИЯ: Figma спрашивают «кто я» (/v1/me). Исключений не бросает.</summary>
    public async Task<(bool Ok, string Message)> CheckAsync(string? sourceId, CancellationToken ct = default)
    {
        Auth auth;
        try
        {
            auth = ResolveAuth(ResolveSourceParams(sourceId));
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
        try
        {
            using var doc = JsonDocument.Parse(await GetAsync($"{BaseUrl}/me", auth, ct));
            var who = Str(doc.RootElement, "handle");
            var email = Str(doc.RootElement, "email");
            return (true, Loc.T("msg.figmaImporter.12", email.Length > 0 ? $"{who} ({email})" : who));
        }
        catch (FigmaApiException ex)
        {
            return (false, Explained(ex, auth).Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "Проверка подключения Figma не удалась");
            return (false, Loc.T("msg.figmaImporter.13", ex.Message));
        }
    }
}
