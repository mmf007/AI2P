using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>Импорт одиночного задания по URL (ТЗ v1.50, todo50): разбор ссылки на карточку,
/// разбор карточки с вложениями, сборка описания и публикация инструмента агента.</summary>
public sealed class Todo50Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Theory]
    [InlineData("https://trello.com/c/abc123", "abc123")]
    [InlineData("https://trello.com/c/abc123/12-заголовок-карточки", "abc123")]
    [InlineData("https://trello.com/c/abc123?menu=filter", "abc123")]
    [InlineData("trello.com/c/xYz", "xYz")]
    [InlineData("https://example.com/c/abc123", null)]
    [InlineData("просто текст", null)]
    [InlineData("https://trello.com/c/", null)]
    public void ParseCardUrl_Extracts_Short_Link(string url, string? expected) =>
        Assert.Equal(expected, TrelloImporter.ParseCardUrl(url));

    [Fact]
    public void ParseCardDetails_Reads_Card_And_Attachments()
    {
        var (card, attachments, boardId) = TrelloImporter.ParseCardDetails("""
            {"id":"c1","name":"Карточка","desc":"текст","due":"2026-08-01T10:00:00.000Z",
             "shortUrl":"https://trello.com/c/abc","idBoard":"b1",
             "attachments":[
               {"id":"a1","name":"план.png","url":"https://trello.com/1/cards/c1/attachments/a1/download/план.png","bytes":1234,"isUpload":true},
               {"id":"a2","name":"Ссылка на доку","url":"https://example.com/doc","isUpload":false},
               {"id":"","name":"без id — пропускается"}]}
            """);
        Assert.Equal("c1", card.Id);
        Assert.Equal("Карточка", card.Name);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), card.Due);
        Assert.Equal("b1", boardId);
        Assert.Equal(2, attachments.Count);
        Assert.True(attachments[0].IsUpload);
        Assert.Equal(1234, attachments[0].Bytes);
        Assert.False(attachments[1].IsUpload);
    }

    [Fact]
    public void BuildImportedDescription_Contains_Text_Files_And_Source()
    {
        var card = new TrelloImporter.Card("c1", "Карточка", "текст задачи", null, "Задачи",
            "https://trello.com/c/abc");
        var text = TrelloImporter.BuildImportedDescription(card,
            ["![план.png](api/files/raw?path=projects%2Fp%2Fuploads%2Fplan.png)",
             "- [Ссылка на доку](https://example.com/doc)"]);

        Assert.StartsWith("текст задачи", text);
        Assert.Contains("Приложенные файлы", text);
        Assert.Contains("![план.png]", text);
        Assert.Contains("https://example.com/doc", text);
        Assert.Contains("доска «Задачи»", text);
        Assert.Contains("https://trello.com/c/abc", text);

        // без описания и вложений остаётся только источник
        var bare = TrelloImporter.BuildImportedDescription(card with { Desc = "" }, []);
        Assert.DoesNotContain("Приложенные файлы", bare);
        Assert.Contains("Импортировано из Trello", bare);
    }

    [Fact]
    public async Task Import_Tool_Published_Only_With_Importer_And_Project()
    {
        var project = _f.Projects.Create("Импорт по URL", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Текущая" },
            "т", "", null);

        // без импортёра инструмент не публикуется и вежливо отказывает
        var without = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task));
        Assert.DoesNotContain(without.Specs, s => s.Name == "import_task_from_url");
        var refusal = await without.ExecuteAsync("import_task_from_url",
            JsonDocument.Parse("""{"url":"https://trello.com/c/abc"}""").RootElement, default);
        Assert.Contains("недоступен", refusal);

        // с импортёром и проектом — публикуется
        var importer = new TrelloImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files);
        var with = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task,
                importer: importer, project: project));
        Assert.Contains(with.Specs, s => s.Name == "import_task_from_url");

        // не-trello ссылка даёт понятную ошибку, не дойдя до HTTP
        var error = await with.ExecuteAsync("import_task_from_url",
            JsonDocument.Parse("""{"url":"https://example.com/x"}""").RootElement, default);
        Assert.Contains("trello.com/c/", error);
    }
}
