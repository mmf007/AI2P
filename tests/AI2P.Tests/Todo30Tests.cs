using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// md улучшенный и импорт (ТЗ v1.28, todo30): MdLinks.GetAllLinks (единый метод ссылок),
/// каскадное удаление вставленных файлов uploads/ при удалении задачи (с защитой от
/// удаления файлов, на которые ссылаются другие задачи), справочник импортов,
/// импорт из Trello (разбор JSON + применение к проекту с дедупликацией по external_ref).
/// </summary>
public sealed class Todo30Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // --- MdLinks ---

    [Fact]
    public void GetAllLinks_finds_links_and_images_without_duplicates()
    {
        var links = MdLinks.GetAllLinks("""
            Текст ![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png) и
            [файл](http://localhost:5480/ai2p/api/files/project?projectId=1&path=b.txt "заголовок")
            повтор ![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png)
            и видео ![клип](api/files/raw?path=projects%2Fp%2Fuploads%2Fv.mp4){width=50%}
            """);
        Assert.Equal(3, links.Count);
        Assert.Contains("api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png", links);
        Assert.Contains("http://localhost:5480/ai2p/api/files/project?projectId=1&path=b.txt", links);
        Assert.Contains("api/files/raw?path=projects%2Fp%2Fuploads%2Fv.mp4", links);
        Assert.Empty(MdLinks.GetAllLinks(""));
    }

    [Fact]
    public void UploadPathsOf_extracts_only_upload_files()
    {
        var paths = MdLinks.UploadPathsOf(
        [
            "api/files/raw?path=projects%2Fp%2Fuploads%2F%D0%BA%D0%B0%D1%80%D1%82.png",
            "api/files/raw?path=projects%2Fp%2Ftasks%2FT-1%2Fdescription.md", // не uploads
            "https://example.com/pic.png",                                     // внешняя
            "api/files/project?projectId=1&path=file.txt",                     // папка проекта
        ]);
        Assert.Single(paths);
        Assert.Equal("projects/p/uploads/карт.png", paths[0]);
    }

    // --- каскадное удаление (todo30): getAllLinks → файлы uploads/ в корзину ---

    [Fact]
    public void Task_delete_trashes_unreferenced_uploads_and_keeps_shared()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var slug = _f.Projects.Get(project.Id)!.Slug;
        var unique = _f.Files.SaveUpload(slug, "уникальный.png", [1, 2, 3]);
        var shared = _f.Files.SaveUpload(slug, "общий.png", [4, 5, 6]);
        string Url(string rel) => "api/files/raw?path=" + Uri.EscapeDataString(rel);

        var doomed = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Удаляемая" },
            $"![u]({Url(unique)}) и ![s]({Url(shared)})", "", null);
        _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Живая" },
            $"![s]({Url(shared)})", "", null);

        _f.Tasks.Delete(doomed.Id, null);

        Assert.False(File.Exists(_f.Files.Abs(unique)));  // осиротевший — в корзине
        Assert.True(File.Exists(_f.Files.Abs(shared)));   // на общий ссылается живая задача
        var trashed = Directory.GetFiles(_f.Files.Abs($"projects/{slug}/.trash/uploads"));
        Assert.Single(trashed);
        Assert.Contains("уникальный", Path.GetFileName(trashed[0]));
        // .md задачи уехал в корзину вместе с каталогом задачи
        Assert.True(Directory.Exists(_f.Files.Abs($"projects/{slug}/.trash/{doomed.DisplayId}")));
    }

    [Fact]
    public void Task_delete_considers_chat_links()
    {
        var project = _f.Projects.Create("Проект-чат", null, null, null);
        var slug = _f.Projects.Get(project.Id)!.Slug;
        var upload = _f.Files.SaveUpload(slug, "из_чата.png", [7]);
        var url = "api/files/raw?path=" + Uri.EscapeDataString(upload);

        var doomed = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "С чатом" }, "без ссылок", "", null);
        _f.Chat.Add(doomed.Id, _f.Executors.EnsureLocalOwner().Id, null, $"смотри ![пик]({url})");

        _f.Tasks.Delete(doomed.Id, null);
        Assert.False(File.Exists(_f.Files.Abs(upload))); // ссылка была только в чате удаляемой
    }

    // --- справочник импортов ---

    [Fact]
    public void Import_sources_crud_with_unique_name()
    {
        var source = _f.Imports.Create(new ImportSource
        {
            Name = "Trello mmffr",
            ParamsJson = """{"login": "mmffr", "filter": "AI2P"}""",
        }, null);
        Assert.Throws<ArgumentException>(() =>
            _f.Imports.Create(new ImportSource { Name = "trello MMFFR" }, null));

        source.Name = "Trello основной";
        source.IsActive = false;
        _f.Imports.Update(source, null);
        var loaded = _f.Imports.List().Single();
        Assert.Equal("Trello основной", loaded.Name);
        Assert.False(loaded.IsActive);

        _f.Imports.Delete(source.Id, null);
        Assert.Empty(_f.Imports.List());
    }

    // --- Trello: разбор JSON и применение к проекту ---

    [Fact]
    public void ParseBoards_filters_by_name_substring()
    {
        const string json = """[{"id":"b1","name":"AI2P backlog"},{"id":"b2","name":"Личное"}]""";
        Assert.Equal(2, TrelloImporter.ParseBoards(json, "").Count);
        var filtered = TrelloImporter.ParseBoards(json, "ai2p");
        Assert.Single(filtered);
        Assert.Equal("b1", filtered[0].Id);
    }

    [Fact]
    public void ParseCards_reads_fields_and_due_date()
    {
        var cards = TrelloImporter.ParseCards("""
            [{"id":"c1","name":"Карточка","desc":"описание","due":"2026-08-01T10:00:00.000Z",
              "shortUrl":"https://trello.com/c/abc"},
             {"id":"","name":"без id — пропускается"}]
            """, "Доска");
        Assert.Single(cards);
        Assert.Equal("Карточка", cards[0].Name);
        Assert.Equal("описание", cards[0].Desc);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), cards[0].Due);
        Assert.Equal("Доска", cards[0].BoardName);
    }

    [Fact]
    public void Apply_adds_updates_and_skips_by_external_ref()
    {
        var project = _f.Projects.Create("Импорт", null, null, null);
        var importer = new TrelloImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files);
        var card = new TrelloImporter.Card("c1", "Задача из Trello", "текст", null, "Доска",
            "https://trello.com/c/abc");

        // первый прогон — добавление
        var r1 = importer.Apply(project, [card], addNew: true, updateExisting: false, null);
        Assert.Equal((1, 0, 0), (r1.Added, r1.Updated, r1.Skipped));
        var task = _f.Tasks.FindByExternalRef(project.Id, "trello:c1");
        Assert.NotNull(task);
        Assert.Equal("Задача из Trello", task!.Title);
        Assert.Equal(TaskStatuses.Draft, task.Status);
        Assert.Contains("текст", _f.Tasks.ReadDescription(task));

        // повторный прогон без обновления — дубль не создаётся
        var r2 = importer.Apply(project, [card], addNew: true, updateExisting: false, null);
        Assert.Equal((0, 0, 1), (r2.Added, r2.Updated, r2.Skipped));
        Assert.Single(_f.Tasks.List(project.Id), t => t.ExternalRef == "trello:c1");

        // обновление существующей
        var renamed = card with { Name = "Новое имя", Due = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) };
        var r3 = importer.Apply(project, [renamed], addNew: true, updateExisting: true, null);
        Assert.Equal((0, 1, 0), (r3.Added, r3.Updated, r3.Skipped));
        var updated = _f.Tasks.FindByExternalRef(project.Id, "trello:c1")!;
        Assert.Equal("Новое имя", updated.Title);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), updated.DueDate);

        // «добавлять новые» снят — новая карточка пропускается
        var other = card with { Id = "c2", Name = "Другая" };
        var r4 = importer.Apply(project, [other], addNew: false, updateExisting: true, null);
        Assert.Equal((0, 0, 1), (r4.Added, r4.Updated, r4.Skipped));
        Assert.Null(_f.Tasks.FindByExternalRef(project.Id, "trello:c2"));
    }
}
