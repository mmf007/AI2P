using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-152: ОБСУЖДЕНИЕ КАРТОЧКИ TRELLO ПОПАДАЕТ В ЧАТ ЗАДАЧИ.
///
/// Карточка импортировалась целиком, кроме главного — переписки под ней: комментарии
/// оставались в Trello, и человек читал задачу без половины смысла. Теперь обсуждение
/// переносится в чат задачи: автор «светится» так, как он назван В TRELLO
/// («trello:&lt;логин&gt;» — исполнителя с таким именем в организации нет и быть не должно),
/// а порядок и время сообщений остаются теми же, что в источнике (чат сортируется
/// по created_at, поэтому хронология сохраняется сама). Повторный импорт той же карточки
/// добавляет только НОВЫЕ сообщения — уже перенесённые узнаются по внешнему id действия.
/// </summary>
public sealed class T152Tests : IDisposable
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

    private static string SourceParams() =>
        JsonSerializer.Serialize(new { login = "mmffr", filter = "", keyRef = "k", tokenRef = "t" });

    // ---------- 1. разбор обсуждения из ответа Trello ----------

    [Fact]
    public void ParseComments_reads_author_text_and_time_in_chronological_order()
    {
        // Trello отдаёт действия ОТ НОВЫХ К СТАРЫМ и вперемешку с другими типами
        var comments = TrelloImporter.ParseComments(JsonDocument.Parse("""
            {"id":"c1","name":"Карточка","actions":[
              {"id":"a3","type":"commentCard","date":"2026-08-03T12:00:00.000Z",
               "data":{"text":"третий"},"memberCreator":{"username":"petya","fullName":"Пётр"}},
              {"id":"a2","type":"updateCard","date":"2026-08-02T12:00:00.000Z",
               "data":{"text":"не комментарий"},"memberCreator":{"username":"petya"}},
              {"id":"a1","type":"commentCard","date":"2026-08-01T12:00:00.000Z",
               "data":{"text":"первый"},"memberCreator":{"username":"mmffr"}}]}
            """).RootElement);

        Assert.Equal(2, comments.Count);
        Assert.Equal(["a1", "a3"], comments.Select(c => c.Id));           // хронология источника
        Assert.Equal("mmffr", comments[0].Author);
        Assert.Equal("первый", comments[0].Text);
        Assert.Equal(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), comments[0].Date);
        Assert.Equal("petya", comments[1].Author);
    }

    [Fact]
    public void ParseComments_falls_back_to_full_name_and_skips_empty()
    {
        var comments = TrelloImporter.ParseComments(JsonDocument.Parse("""
            [{"id":"a1","type":"commentCard","date":"2026-08-01T12:00:00.000Z",
              "data":{"text":"  "},"memberCreator":{"username":"mmffr"}},
             {"id":"a2","type":"commentCard","date":"2026-08-02T12:00:00.000Z",
              "data":{"text":"есть текст"},"memberCreator":{"fullName":"Иван Иванов"}},
             {"id":"a3","type":"commentCard","date":"2026-08-03T12:00:00.000Z",
              "idMemberCreator":"m7","data":{"text":"автора не отдали"}}]
            """).RootElement);

        // пустой комментарий не переносится, автор — логин, иначе полное имя; не отдали
        // ни того, ни другого — остаётся ключ участника, логин спросят отдельно
        Assert.Equal(["a2", "a3"], comments.Select(c => c.Id));
        Assert.Equal("Иван Иванов", comments[0].Author);
        Assert.Equal("", comments[1].Author);
        Assert.Equal("m7", comments[1].AuthorId);
        Assert.Empty(TrelloImporter.ParseComments(JsonDocument.Parse("""{"id":"c1"}""").RootElement));
    }

    [Fact]
    public void ChatOf_marks_unknown_author_but_keeps_him_trello()
    {
        var messages = TrelloImporter.ChatOf([
            new TrelloImporter.CardComment("a1", "", "автор неизвестен", DateTime.UtcNow, "m7"),
        ]);
        Assert.Equal("trello:?", messages[0].Author);
    }

    [Fact]
    public void ChatOf_names_author_the_trello_way()
    {
        var messages = TrelloImporter.ChatOf([
            new TrelloImporter.CardComment("a2", "petya", "второй",
                new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)),
            new TrelloImporter.CardComment("a1", "mmffr", "первый",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
        ]);
        Assert.Equal(["trello:mmffr", "trello:petya"], messages.Select(m => m.Author));
        Assert.Equal(["trello:a1", "trello:a2"], messages.Select(m => m.ExternalRef));
    }

    // ---------- 2. чат: автор, хронология, отсутствие дублей ----------

    [Fact]
    public void Chat_import_keeps_author_and_chronology()
    {
        var project = _f.Projects.Create("Импорт", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "", null);
        var owner = _f.Executors.EnsureLocalOwner();

        var added = _f.Chat.Import(task.Id, owner.Id, [
            new ChatService.ImportedMessage("trello:a2", "trello:petya", "второй",
                new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc)),
            new ChatService.ImportedMessage("trello:a1", "trello:mmffr", "первый",
                new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc)),
        ]);
        Assert.Equal(2, added);

        var chat = _f.Chat.ListByTask(task.Id);
        Assert.Equal(["первый", "второй"], chat.Select(m => m.Text));           // порядок источника
        Assert.Equal(["trello:mmffr", "trello:petya"], chat.Select(m => m.AuthorName));
        Assert.Equal(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc), chat[0].CreatedAt);
        // от чьего имени импортировали — видно, но в чате «светится» автор из Trello
        Assert.All(chat, m => Assert.Equal(owner.Id, m.FromExecutorId));
        Assert.All(chat, m => Assert.Equal(ChatMessageKind.Message, m.Kind));

        // своё сообщение автора не подменяет и внешнего id не получает
        var own = _f.Chat.Add(task.Id, owner.Id, null, "своё сообщение");
        Assert.Equal("", own.AuthorName);
        Assert.Equal("", _f.Chat.ListByTask(task.Id).Last().ExternalRef);
    }

    [Fact]
    public void Chat_import_is_repeatable_and_adds_only_new()
    {
        var project = _f.Projects.Create("Повтор", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "", null);
        var owner = _f.Executors.EnsureLocalOwner();
        var first = new ChatService.ImportedMessage("trello:a1", "trello:mmffr", "первый",
            new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        var second = new ChatService.ImportedMessage("trello:a2", "trello:petya", "второй",
            new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, _f.Chat.Import(task.Id, owner.Id, [first]));
        Assert.Equal(1, _f.Chat.Import(task.Id, owner.Id, [first, second])); // первое уже есть
        Assert.Equal(0, _f.Chat.Import(task.Id, owner.Id, [first, second]));
        Assert.Equal(["первый", "второй"], _f.Chat.ListByTask(task.Id).Select(m => m.Text));
    }

    [Fact]
    public void Chat_schema_migration_is_idempotent_and_old_messages_have_no_author()
    {
        _f.Db.Init(_f.Db.NodeId);
        _f.Db.Init(_f.Db.NodeId);
        // v31 — тэги задач (T-222); v32 — замена исполнителя (T-221);
        // v33 — ссылка импорта у задачи (T-246); v34 — статус по готовности (T-250);
        // v35 — объекты проекта (T-259); v36 — пер-серверная активность моделей (T-8-S1);
        // v37 — заявки на остановку с другого сервера, stop_requests (T-263);
        // v38 — обучение адаптера LoRA (T-12-S1); v39 — смена дирижёра заявкой (T-21-S1)
        Assert.Equal("46", _f.Db.Meta("schema_version"));

        var project = _f.Projects.Create("Схема", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "", null);
        var owner = _f.Executors.EnsureLocalOwner();
        // строка, записанная БЕЗ новых колонок (как её писала прошлая версия приложения)
        using (var conn = _f.Db.Open())
        {
            var now = Sql.ToDb(DateTime.UtcNow);
            Sql.Exec(conn, null, """
                INSERT INTO chat_messages (id, task_id, from_executor_id, text, created_at, updated_at)
                VALUES (@id, @task, @from, 'старое сообщение', @ts, @ts)
                """,
                ("@id", Guid.NewGuid().ToString()), ("@task", task.Id), ("@from", owner.Id),
                ("@ts", now));
        }
        var old = _f.Chat.ListByTask(task.Id).Single();
        Assert.Equal("", old.AuthorName);
        Assert.Equal("", old.ExternalRef);
    }

    // ---------- 3. импорт карточки: обсуждение едет вместе с содержимым ----------

    [Fact]
    public async Task Card_import_by_url_brings_the_discussion()
    {
        var project = _f.Projects.Create("По ссылке", null, null, null);
        _f.Imports.Create(new ImportSource
        {
            Name = "Trello", Kind = "trello", IsActive = true, ParamsJson = SourceParams(),
        }, null);
        var handler = new FakeTrello(
            (HttpStatusCode.OK, """
                {"id":"c1","name":"Карточка","desc":"описание","shortUrl":"https://trello.com/c/abc",
                 "idBoard":"b1","attachments":[],
                 "actions":[
                   {"id":"a2","type":"commentCard","date":"2026-08-02T10:00:00.000Z",
                    "data":{"text":"ответ"},"memberCreator":{"username":"petya"}},
                   {"id":"a1","type":"commentCard","date":"2026-08-01T10:00:00.000Z",
                    "data":{"text":"вопрос"},"memberCreator":{"username":"mmffr"}}]}
                """),
            (HttpStatusCode.OK, """{"id":"b1","name":"Доска"}"""));
        var importer = Importer(handler);

        var card = await importer.ImportCardContentAsync(project, "https://trello.com/c/abc", null);

        // обсуждение приезжает тем же запросом — отдельного обращения за ним нет
        Assert.Contains("actions=commentCard", handler.Urls[0]);
        Assert.Equal(["вопрос", "ответ"], card.Comments.Select(c => c.Text));

        // задача создаётся снаружи (форма или инструмент агента), чат наполняет ImportChat
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = card.Title, ExternalRef = card.ExternalRef,
        }, card.Description, "", null);
        var owner = _f.Executors.EnsureLocalOwner();
        Assert.Equal(2, importer.ImportChat(task.Id, card.Comments, owner.Id));
        var chat = _f.Chat.ListByTask(task.Id);
        Assert.Equal(["вопрос", "ответ"], chat.Select(m => m.Text));
        Assert.Equal(["trello:mmffr", "trello:petya"], chat.Select(m => m.AuthorName));
    }

    [Fact]
    public async Task Author_login_is_asked_separately_when_trello_gives_only_his_key()
    {
        // так отвечает НАСТОЯЩИЙ Trello на вложенные действия: memberCreator не развёрнут,
        // есть только idMemberCreator (проверено живым обращением к api.trello.com)
        var project = _f.Projects.Create("Автор по ключу", null, null, null);
        _f.Imports.Create(new ImportSource
        {
            Name = "Trello", Kind = "trello", IsActive = true, ParamsJson = SourceParams(),
        }, null);
        var handler = new FakeTrello(
            (HttpStatusCode.OK, """
                {"id":"c1","name":"Карточка","desc":"","shortUrl":"https://trello.com/c/abc",
                 "idBoard":"b1","attachments":[],
                 "actions":[{"id":"a1","type":"commentCard","date":"2026-08-01T10:00:00.000Z",
                             "idMemberCreator":"m7","data":{"text":"комментарий"}}]}
                """),
            (HttpStatusCode.OK, """{"id":"m7","username":"mmffr","fullName":"Михаил"}"""),
            (HttpStatusCode.OK, """{"id":"b1","name":"Доска"}"""));

        var card = await Importer(handler).ImportCardContentAsync(project,
            "https://trello.com/c/abc", null);

        Assert.Contains("/members/m7", handler.Urls[1]);
        Assert.Equal("mmffr", card.Comments.Single().Author);
        Assert.Equal("trello:mmffr", TrelloImporter.ChatOf(card.Comments).Single().Author);
    }

    [Fact]
    public async Task Unknown_author_does_not_break_the_import()
    {
        var project = _f.Projects.Create("Автор молчит", null, null, null);
        _f.Imports.Create(new ImportSource
        {
            Name = "Trello", Kind = "trello", IsActive = true, ParamsJson = SourceParams(),
        }, null);
        var handler = new FakeTrello(
            (HttpStatusCode.OK, """
                {"id":"c1","name":"Карточка","desc":"","shortUrl":"https://trello.com/c/abc",
                 "idBoard":"b1","attachments":[],
                 "actions":[{"id":"a1","type":"commentCard","date":"2026-08-01T10:00:00.000Z",
                             "idMemberCreator":"m7","data":{"text":"комментарий"}}]}
                """),
            (HttpStatusCode.NotFound, """{"message":"member not found"}"""),
            (HttpStatusCode.OK, """{"id":"b1","name":"Доска"}"""));

        // участника удалили — карточка всё равно импортируется, автор помечен «?»
        var card = await Importer(handler).ImportCardContentAsync(project,
            "https://trello.com/c/abc", null);
        Assert.Equal("trello:?", TrelloImporter.ChatOf(card.Comments).Single().Author);
    }

    [Fact]
    public void Import_chat_without_actor_moves_nothing()
    {
        var project = _f.Projects.Create("Без актора", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "", null);
        // from_executor_id ссылается на справочник исполнителей и пустым быть не может:
        // без актора обсуждение не переносится, но импорт задачи не падает
        Assert.Equal(0, Importer().ImportChat(task.Id, [
            new TrelloImporter.CardComment("a1", "mmffr", "текст", DateTime.UtcNow),
        ], null));
        Assert.Empty(_f.Chat.ListByTask(task.Id));
    }

    // ---------- 4. массовый импорт: обсуждение у каждой карточки ----------

    [Fact]
    public void Bulk_apply_moves_discussion_and_counts_it()
    {
        var project = _f.Projects.Create("Массовый", null, null, null);
        var owner = _f.Executors.EnsureLocalOwner();
        var importer = Importer();
        var card = new TrelloImporter.Card("c1", "Задача из Trello", "текст", null, "Доска",
            "https://trello.com/c/abc",
            [new TrelloImporter.CardComment("a1", "mmffr", "первый",
                new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc))]);

        var added = importer.Apply(project, [card], addNew: true, updateExisting: false, owner.Id);
        Assert.Equal((1, 0, 0, 1), (added.Added, added.Updated, added.Skipped, added.Comments));
        var task = _f.Tasks.FindByExternalRef(project.Id, "trello:c1")!;
        Assert.Equal("trello:mmffr", _f.Chat.ListByTask(task.Id).Single().AuthorName);

        // повторный импорт с обновлением: в обсуждении прибавилось одно сообщение —
        // переносится только оно, старое не задваивается
        var grown = card with
        {
            Comments =
            [
                card.Comments![0],
                new TrelloImporter.CardComment("a2", "petya", "второй",
                    new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc)),
            ],
        };
        var updated = importer.Apply(project, [grown], addNew: true, updateExisting: true, owner.Id);
        Assert.Equal((0, 1, 0, 1), (updated.Added, updated.Updated, updated.Skipped, updated.Comments));
        Assert.Equal(["первый", "второй"], _f.Chat.ListByTask(task.Id).Select(m => m.Text));
    }
}
