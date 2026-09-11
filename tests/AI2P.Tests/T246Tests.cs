using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-246: ССЫЛКА ИМПОРТА У ЗАДАЧИ И ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ ИСТОЧНИКА.
///
/// Задача, приехавшая импортом, знала только внутренний ключ карточки («trello:&lt;id&gt;»):
/// в браузере он не открывается, человеком не правится, и обновить по нему задачу было
/// нечем — приходилось гонять массовый импорт всей доски. Теперь у задачи есть СВОЯ ссылка
/// импорта: она проставляется сама при импорте (и массовом, и одиночном по URL, и
/// инструментом агента), правится и очищается в разделе «Расширенное» формы задачи,
/// а кнопка «обновить» карточки по ней переспрашивает — обновить из источника или просто
/// перечитать задачу здесь.
///
/// Обновление из источника перечитывает карточку и кладёт в задачу заголовок, срок,
/// описание и НОВЫЕ сообщения обсуждения; критерии приёмки, исполнители и статус —
/// местные, их не трогают.
/// </summary>
public sealed class T246Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Ответ вместо живого Trello: тела по порядку обращений (приём из T134Tests).</summary>
    private sealed class FakeTrello(params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            var (status, body) = responses[Math.Min(_next++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private TrelloImporter Importer(FakeTrello? handler = null) =>
        new(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files, _f.Chat)
        {
            HttpOverride = handler is null ? null : new HttpClient(handler),
            KeyResolver = _ => ("значение", "тест"),
        };

    private ImportSource TrelloSource()
    {
        var source = _f.Imports.Create(new ImportSource
        {
            Name = "Trello T-246",
            Kind = "trello",
            IsActive = true,
            ParamsJson = JsonSerializer.Serialize(
                new { login = "mmffr", filter = "", keyRef = "k", tokenRef = "t" }),
        }, null);
        return source;
    }

    // ---------- 1. поле задачи: хранится, правится, очищается ----------

    [Fact]
    public void Import_url_is_stored_and_read_back()
    {
        var project = _f.Projects.Create("Ссылка импорта", null, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Приехала из Trello",
            ImportUrl = "https://trello.com/c/abc123",
        }, "описание", "", null);

        Assert.Equal("https://trello.com/c/abc123", _f.Tasks.Get(task.Id)!.ImportUrl);
    }

    [Fact]
    public void Import_url_can_be_edited_and_cleared()
    {
        var project = _f.Projects.Create("Правка ссылки", null, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ImportUrl = "https://trello.com/c/abc123",
        }, "описание", "", null);

        // правка: в поле формы ввели другой адрес
        task.ImportUrl = "https://trello.com/c/xyz789";
        _f.Tasks.Update(task, "описание", "", null);
        Assert.Equal("https://trello.com/c/xyz789", _f.Tasks.Get(task.Id)!.ImportUrl);

        // очистка: поле формы опустошили — задача отвязана от источника. Именно этого
        // не умеет external_ref (он пишется через COALESCE и очистке не поддаётся)
        task.ImportUrl = "";
        _f.Tasks.Update(task, "описание", "", null);
        Assert.Null(_f.Tasks.Get(task.Id)!.ImportUrl);
    }

    [Fact]
    public void Blank_import_url_is_stored_as_empty()
    {
        var project = _f.Projects.Create("Пробелы", null, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ImportUrl = "   https://trello.com/c/abc123   ",
        }, "о", "", null);
        // пробелы по краям снимаются, «пусто» в базе всегда одно и то же значение
        Assert.Equal("https://trello.com/c/abc123", _f.Tasks.Get(task.Id)!.ImportUrl);

        var blank = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Без источника",
            ImportUrl = "  ",
        }, "о", "", null);
        Assert.Null(_f.Tasks.Get(blank.Id)!.ImportUrl);
    }

    // ---------- 2. импорт проставляет ссылку сам ----------

    [Fact]
    public void Mass_import_writes_the_card_url_into_the_task()
    {
        var project = _f.Projects.Create("Массовый импорт", null, null, null);
        var result = Importer().Apply(project, [
            new TrelloImporter.Card("c1", "Карточка", "текст", null, "Доска",
                "https://trello.com/c/abc123"),
        ], addNew: true, updateExisting: false, actorId: null);

        Assert.Equal(1, result.Added);
        var task = _f.Tasks.FindByExternalRef(project.Id, "trello:c1")!;
        Assert.Equal("https://trello.com/c/abc123", task.ImportUrl);
    }

    [Fact]
    public void Mass_import_fills_the_url_of_a_task_imported_by_an_older_version()
    {
        var project = _f.Projects.Create("Обновление существующих", null, null, null);
        // задача, приехавшая прошлой версией: внешний ключ есть, ссылки нет
        var old = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Старый заголовок",
            ExternalRef = "trello:c1",
        }, "старое описание", "", null);

        var result = Importer().Apply(project, [
            new TrelloImporter.Card("c1", "Новый заголовок", "текст", null, "Доска",
                "https://trello.com/c/abc123"),
        ], addNew: false, updateExisting: true, actorId: null);

        Assert.Equal(1, result.Updated);
        var updated = _f.Tasks.Get(old.Id)!;
        Assert.Equal("Новый заголовок", updated.Title);
        Assert.Equal("https://trello.com/c/abc123", updated.ImportUrl);
    }

    // ---------- 3. обновление задачи из Trello ----------

    /// <summary>Ответы Trello на обновление: карточка (с обсуждением) и доска.</summary>
    private static FakeTrello CardAndBoard(string cardJson, string boardName = "Доска") =>
        new((HttpStatusCode.OK, cardJson),
            (HttpStatusCode.OK, JsonSerializer.Serialize(new { name = boardName })));

    [Fact]
    public async Task Refresh_pulls_title_due_description_and_new_comments()
    {
        var project = _f.Projects.Create("Обновление из источника", null, null, null);
        TrelloSource();
        var human = _f.Executors.Create(new Executor
        {
            Nick = "человек", Kind = ExecutorKind.Human, SystemRole = SystemRole.Editor,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Как было",
            ImportUrl = "https://trello.com/c/abc123/17-заголовок-карточки",
        }, "старое описание", "критерии приёмки", null);

        var handler = CardAndBoard("""
            {"id":"c1","name":"Как стало","desc":"свежий текст карточки",
             "due":"2026-09-01T10:00:00.000Z","shortUrl":"https://trello.com/c/abc123",
             "idBoard":"b1","attachments":[],
             "actions":[{"id":"a1","type":"commentCard","date":"2026-08-20T12:00:00.000Z",
                         "data":{"text":"новый комментарий"},
                         "memberCreator":{"username":"mmffr"}}]}
            """);
        var result = await Importer(handler).RefreshTaskAsync(project, task, null, human.Id);

        Assert.Equal(task.DisplayId, result.DisplayId);
        Assert.Equal("Как стало", result.Title);
        Assert.Equal(1, result.Comments);

        var updated = _f.Tasks.Get(task.Id)!;
        Assert.Equal("Как стало", updated.Title);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), updated.DueDate);
        Assert.Contains("свежий текст карточки", _f.Tasks.ReadDescription(updated));
        // внешний ключ карточки проставлен — повторный массовый импорт дубля не сделает
        Assert.Equal("trello:c1", updated.ExternalRef);
        // адрес приведён к короткому виду, каким его называет сам Trello
        Assert.Equal("https://trello.com/c/abc123", updated.ImportUrl);
        // местное остаётся местным: критерии приёмки не из Trello и не трогаются
        Assert.Equal("критерии приёмки", _f.Tasks.ReadAcceptance(updated));
        Assert.Contains(_f.Chat.ListByTask(task.Id), m => m.Text == "новый комментарий");
    }

    [Fact]
    public async Task Refresh_twice_does_not_duplicate_the_discussion()
    {
        var project = _f.Projects.Create("Повторное обновление", null, null, null);
        TrelloSource();
        var human = _f.Executors.Create(new Executor
        {
            Nick = "человек2", Kind = ExecutorKind.Human, SystemRole = SystemRole.Editor,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            ImportUrl = "https://trello.com/c/abc123",
        }, "описание", "", null);

        const string card = """
            {"id":"c1","name":"Карточка","desc":"текст","shortUrl":"https://trello.com/c/abc123",
             "idBoard":"b1","attachments":[],
             "actions":[{"id":"a1","type":"commentCard","date":"2026-08-20T12:00:00.000Z",
                         "data":{"text":"один и тот же"},
                         "memberCreator":{"username":"mmffr"}}]}
            """;
        var first = await Importer(CardAndBoard(card)).RefreshTaskAsync(
            project, _f.Tasks.Get(task.Id)!, null, human.Id);
        var second = await Importer(CardAndBoard(card)).RefreshTaskAsync(
            project, _f.Tasks.Get(task.Id)!, null, human.Id);

        Assert.Equal(1, first.Comments);
        Assert.Equal(0, second.Comments);   // уже перенесённое узнаётся по внешнему id действия
        Assert.Single(_f.Chat.ListByTask(task.Id), m => m.Text == "один и тот же");
    }

    [Fact]
    public async Task Refresh_refuses_a_task_without_an_import_url()
    {
        var project = _f.Projects.Create("Без ссылки", null, null, null);
        TrelloSource();
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Своя задача" },
            "описание", "", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer().RefreshTaskAsync(project, task, null, null));
        Assert.Contains("ссылка импорта", ex.Message);
    }

    [Fact]
    public async Task Refresh_refuses_a_link_that_is_not_a_trello_card()
    {
        var project = _f.Projects.Create("Чужая ссылка", null, null, null);
        TrelloSource();
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Задача",
            // github/gitlab (T-247, T-249) добавят свои процедуры обновления — до тех пор
            // такая ссылка честно отвергается, а не молча импортируется как карточка
            ImportUrl = "https://github.com/org/repo/issues/7",
        }, "описание", "", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Importer().RefreshTaskAsync(project, task, null, null));
        Assert.Contains("Trello", ex.Message);
    }

    [Fact]
    public void CanRefresh_recognizes_only_a_trello_card_link()
    {
        Assert.True(TrelloImporter.CanRefresh("https://trello.com/c/abc123"));
        Assert.True(TrelloImporter.CanRefresh("https://trello.com/c/abc123/17-заголовок"));
        Assert.False(TrelloImporter.CanRefresh("https://github.com/org/repo/issues/7"));
        Assert.False(TrelloImporter.CanRefresh("https://trello.com/b/board"));
        Assert.False(TrelloImporter.CanRefresh(""));
        Assert.False(TrelloImporter.CanRefresh(null));
    }

    // ---------- 4. шаг обновления билда 89: ссылка у уже импортированных задач ----------

    [Fact]
    public void Trello_url_is_found_in_the_description_footer()
    {
        // ровно то, что дописывает импорт в конец описания
        Assert.Equal("https://trello.com/c/abc123", TaskService.TrelloUrlInText(
            "текст задачи\n\n---\n*Импортировано из Trello: доска «Доска», "
            + "[карточка](https://trello.com/c/abc123).*"));
        Assert.Null(TaskService.TrelloUrlInText("обычное описание без ссылок"));
    }

    [Fact]
    public void Upgrade_step_fills_urls_of_already_imported_tasks_and_is_idempotent()
    {
        var project = _f.Projects.Create("Шаг обновления", null, null, null);
        // как выглядела задача, импортированная прошлой версией: внешний ключ есть,
        // ссылки нет, адрес карточки — только в описании
        var imported = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Импортирована прошлой версией",
            ExternalRef = "trello:c1",
        }, "текст\n\n---\n*Импортировано из Trello: доска «Доска», "
           + "[карточка](https://trello.com/c/abc123).*", "", null);
        // своя задача шага не касается
        var own = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Своя" },
            "https://trello.com/c/zzz в тексте, но задача не импортирована", "", null);
        // задача с уже заданной человеком ссылкой не перезаписывается
        var manual = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Ссылку задали руками",
            ExternalRef = "trello:c2",
            ImportUrl = "https://trello.com/c/manual",
        }, "*Импортировано из Trello: [карточка](https://trello.com/c/other).*", "", null);

        Assert.Equal(1, _f.Tasks.FillImportUrlsFromDescriptions());
        Assert.Equal("https://trello.com/c/abc123", _f.Tasks.Get(imported.Id)!.ImportUrl);
        Assert.Null(_f.Tasks.Get(own.Id)!.ImportUrl);
        Assert.Equal("https://trello.com/c/manual", _f.Tasks.Get(manual.Id)!.ImportUrl);

        // идемпотентность: повторный старт находить нечего
        Assert.Equal(0, _f.Tasks.FillImportUrlsFromDescriptions());
    }

    // ---------- 5. словари: у новых текстов есть оба языка ----------

    [Fact]
    public void Both_dictionaries_know_the_new_texts()
    {
        string[] keys =
        [
            "task.importUrl", "task.importUrl.hint", "task.refresh.title", "task.refresh.confirm",
            "task.refresh.fromImport", "task.refresh.internal", "task.refresh.done",
            "msg.apiEndpoints.34", "msg.apiEndpoints.35", "msg.apiEndpoints.36",
            "msg.apiEndpoints.37",
            "msg.trelloImporter.39", "msg.trelloImporter.40",
        ];
        foreach (var key in keys)
        {
            // Loc возвращает САМ КЛЮЧ, когда текста в словаре нет
            Assert.NotEqual(key, AI2P.Core.Loc.In("ru", key));
            Assert.NotEqual(key, AI2P.Core.Loc.In("en", key));
        }
    }
}
