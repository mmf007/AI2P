using System.Text.Json;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Файловые инструменты агента (ТЗ п. 2.4, todo17): песочница в каталоге проекта,
/// поиск/чтение/запись, защита от выхода за каталог (ТЗ гл. 12).
/// </summary>
public sealed class FileToolsetTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ai2p-tools-" + Guid.NewGuid().ToString("N"));
    private readonly FileToolset _tools;

    public FileToolsetTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "проект AI2P\nстрока с TODO внутри");
        File.WriteAllText(Path.Combine(_root, "src", "code.cs"), "class C { }");
        _tools = new FileToolset(_root, "prj-1", "http://localhost:5480/ai2p");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<string> Call(string name, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        return await _tools.ExecuteAsync(name, doc.RootElement, default);
    }

    [Fact]
    public void Specs_Cover_File_Tools()
    {
        var names = FileToolset.Specs.Select(s => s.Name).ToList();
        Assert.Equal(["list_files", "search_files", "read_file", "write_file", "file_url"], names);
        // схемы параметров — валидный JSON
        Assert.All(FileToolset.Specs, s => JsonDocument.Parse(s.ParametersJson).Dispose());
    }

    [Fact]
    public async Task WriteFile_Returns_External_Http_Url()
    {
        var result = await Call("write_file", """{ "path": "out/res.txt", "content": "готово" }""");
        Assert.Contains("http://localhost:5480/ai2p/api/files/project?projectId=prj-1", result);
        Assert.Contains("path=out%2Fres.txt", result);              // путь URL-кодирован
    }

    [Fact]
    public async Task FileUrl_Returns_Link_For_Existing_File_And_Encodes_Cyrillic()
    {
        await Call("write_file", """{ "path": "Дестратификатор_список.txt", "content": "x" }""");
        var url = await Call("file_url", """{ "path": "Дестратификатор_список.txt" }""");
        Assert.StartsWith("http://localhost:5480/ai2p/api/files/project?projectId=prj-1&path=", url);
        Assert.DoesNotContain("Дестратификатор", url);              // кириллица закодирована (%D0…)
    }

    [Fact]
    public async Task FileUrl_Missing_File_Is_Reported()
    {
        var url = await Call("file_url", """{ "path": "нет.txt" }""");
        Assert.Contains("Файл не найден", url);
    }

    [Fact]
    public async Task File_Url_Falls_Back_To_File_Scheme_Without_BaseUrl()
    {
        var toolsNoUrl = new FileToolset(_root); // без projectId/baseUrl
        using var doc = JsonDocument.Parse("""{ "path": "readme.md" }""");
        var url = await toolsNoUrl.ExecuteAsync("file_url", doc.RootElement, default);
        Assert.StartsWith("file:///", url);
    }

    [Fact]
    public async Task ListFiles_Returns_Relative_Paths()
    {
        var result = await Call("list_files", "{}");
        Assert.Contains("readme.md", result);
        Assert.Contains("src/code.cs", result);
    }

    [Fact]
    public async Task SearchFiles_Finds_By_Content_Case_Insensitive()
    {
        var result = await Call("search_files", """{ "query": "todo" }""");
        Assert.Contains("readme.md:2", result);
        Assert.DoesNotContain("code.cs", result);
    }

    [Fact]
    public async Task ReadFile_Returns_Content()
    {
        var result = await Call("read_file", """{ "path": "readme.md" }""");
        Assert.Contains("проект AI2P", result);
    }

    [Fact]
    public async Task WriteFile_Creates_New_File_Next_To_Source()
    {
        var result = await Call("write_file", """{ "path": "src/result.txt", "content": "готово" }""");
        Assert.Contains("создан", result);
        Assert.Equal("готово", File.ReadAllText(Path.Combine(_root, "src", "result.txt")));
    }

    [Fact]
    public async Task WriteFile_Overwrites_Existing()
    {
        var result = await Call("write_file", """{ "path": "readme.md", "content": "новое" }""");
        Assert.Contains("перезаписан", result);
        Assert.Equal("новое", File.ReadAllText(Path.Combine(_root, "readme.md")));
    }

    [Fact]
    public async Task Path_Traversal_Is_Rejected()
    {
        // с todo25 текст отказа — про правила безопасности: вне каталога по умолчанию закрыто
        var read = await Call("read_file", """{ "path": "../secret.txt" }""");
        Assert.Contains("вне каталога проекта", read);
        var write = await Call("write_file", """{ "path": "..\\evil.txt", "content": "x" }""");
        Assert.Contains("вне каталога проекта", write);
        // файл наружу не создан
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt")));
    }

    [Fact]
    public async Task Missing_Required_Argument_Is_Reported()
    {
        var result = await Call("read_file", "{}");
        Assert.Contains("Не задан обязательный параметр", result);
    }

    [Fact]
    public async Task Unknown_Tool_Is_Reported()
    {
        var result = await Call("delete_everything", "{}");
        Assert.Contains("неизвестный инструмент", result);
    }

    [Fact]
    public async Task CallLog_Records_Calls_Without_Full_Content()
    {
        await Call("write_file", """{ "path": "a.txt", "content": "очень длинное содержимое файла" }""");
        var logged = Assert.Single(_tools.CallLog);
        Assert.Contains("write_file", logged);
        Assert.Contains("симв.", logged);                 // content заменён на длину
        Assert.DoesNotContain("очень длинное содержимое", logged);
    }
}
