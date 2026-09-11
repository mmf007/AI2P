using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Осведомлённость агента о правилах безопасности (todo26): без описания правил в системном
/// промпте агент считает, что кроме каталога проекта ему ничего не доступно, и отказывается
/// использовать каталоги, открытые правилами («другие пути мне недоступны»). Плюс работа
/// файловых инструментов с полными путями открытых каталогов: file:-ссылки и показ путей.
/// </summary>
public sealed class Todo26Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo26-" + Guid.NewGuid());

    public Todo26Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static SecurityRule Rule(string target, string permission, string pattern,
        bool read = false, bool write = false, bool delete = false) =>
        new()
        {
            Scope = SecurityScope.Global, Target = target, Permission = permission,
            Pattern = pattern, OpRead = read, OpWrite = write, OpDelete = delete,
        };

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void DescribeForAgent_Lists_Directory_Rules_And_Restrictions()
    {
        var rules = new List<SecurityRule>
        {
            Rule(SecurityTarget.Directory, SecurityPermission.Allow, @"c:\tmp\*",
                read: true, write: true),
            Rule(SecurityTarget.Directory, SecurityPermission.Deny, "secrets/*", read: true),
            Rule(SecurityTarget.Action, SecurityPermission.Confirm, "AI2P.Files.Write"),
            // разрешающее правило действия — не ограничение, в промпт не попадает
            Rule(SecurityTarget.Action, SecurityPermission.Allow, "AI2P.Files.Read"),
        };
        var text = new SecurityEvaluator(rules, null).DescribeForAgent();

        Assert.Contains(@"c:\tmp\*", text);
        Assert.Contains("читать, писать", text);
        Assert.Contains("разрешено", text);
        Assert.Contains("secrets/*", text);
        Assert.Contains("запрещено", text);
        Assert.Contains("AI2P.Files.Write", text);
        Assert.Contains("по подтверждению человека", text);
        Assert.DoesNotContain("AI2P.Files.Read", text);
        // указание обращаться по полному пути
        Assert.Contains("полному пути", text);
    }

    [Fact]
    public void DescribeForAgent_Empty_Without_Rules()
    {
        Assert.Equal("", new SecurityEvaluator([], null).DescribeForAgent());
        // одни разрешающие правила действий — тоже нечего сообщать
        var onlyAllowedActions = new List<SecurityRule>
        {
            Rule(SecurityTarget.Action, SecurityPermission.Allow, "AI2P.Files.Read"),
        };
        Assert.Equal("", new SecurityEvaluator(onlyAllowedActions, null).DescribeForAgent());
    }

    [Fact]
    public void SecurityNote_Comes_From_Evaluator_Through_Toolset()
    {
        var rules = new List<SecurityRule>
        {
            Rule(SecurityTarget.Directory, SecurityPermission.Allow, @"c:\tmp\*", read: true),
        };
        var evaluator = new SecurityEvaluator(rules, null);
        var toolset = new AgentToolset(null, null!, security: evaluator);
        Assert.Contains(@"c:\tmp\*", toolset.SecurityNote);

        // без оценщика правил — пустая дописка (промпт не меняется)
        Assert.Equal("", new AgentToolset(null, null!).SecurityNote);
    }

    [Fact]
    public async Task Write_Outside_Project_Creates_File_And_Returns_File_Link()
    {
        var root = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(_dir, "opened");
        Directory.CreateDirectory(outside);

        var rules = new List<SecurityRule>
        {
            Rule(SecurityTarget.Directory, SecurityPermission.Allow,
                Path.Combine(outside, "*"), read: true, write: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        // publicBaseUrl задан — но файл вне проекта HTTP-эндпойнт не отдаёт, нужна file:-ссылка
        var files = new FileToolset(root, "proj-id", "http://localhost:5480/ai2p",
            (abs, op) => evaluator.ForPath(abs, op).Decision != SecurityDecision.Deny);

        var target = Path.Combine(outside, "результат.txt");
        var reply = await files.ExecuteAsync("write_file",
            Args($$"""{"path":{{JsonSerializer.Serialize(target)}},"content":"данные"}"""),
            CancellationToken.None);

        Assert.Equal("данные", File.ReadAllText(target));
        Assert.Contains("file://", reply);
        Assert.DoesNotContain("api/files/project", reply);

        // file_url для внешнего файла — тоже file:-ссылка
        var url = await files.ExecuteAsync("file_url",
            Args($$"""{"path":{{JsonSerializer.Serialize(target)}}}"""), CancellationToken.None);
        Assert.Contains("file://", url);
    }

    [Fact]
    public async Task Write_Inside_Project_Keeps_Http_Link()
    {
        var root = Path.Combine(_dir, "proj2");
        Directory.CreateDirectory(root);
        var files = new FileToolset(root, "proj-id", "http://localhost:5480/ai2p");

        var reply = await files.ExecuteAsync("write_file",
            Args("""{"path":"sub/итог.txt","content":"внутри"}"""), CancellationToken.None);

        Assert.Contains("api/files/project", reply);
        Assert.DoesNotContain("file://", reply);
    }

    [Fact]
    public async Task List_And_Search_In_Opened_Directory_Show_Full_Paths()
    {
        var root = Path.Combine(_dir, "proj3");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(_dir, "opened3");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "заметка.txt"), "искомый текст");

        var rules = new List<SecurityRule>
        {
            Rule(SecurityTarget.Directory, SecurityPermission.Allow,
                Path.Combine(outside, "*"), read: true, write: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        var files = new FileToolset(root, null, null,
            (abs, op) => evaluator.ForPath(abs, op).Decision != SecurityDecision.Deny);

        var listing = await files.ExecuteAsync("list_files",
            Args($$"""{"path":{{JsonSerializer.Serialize(outside)}}}"""), CancellationToken.None);
        // путь вне проекта показывается полным (не «..»-относительным), слэши «/»
        Assert.Contains(outside.Replace('\\', '/') + "/заметка.txt", listing);
        Assert.DoesNotContain("..", listing);

        var found = await files.ExecuteAsync("search_files",
            Args($$"""{"query":"искомый","path":{{JsonSerializer.Serialize(outside)}}}"""),
            CancellationToken.None);
        Assert.Contains(outside.Replace('\\', '/') + "/заметка.txt", found);
    }
}

/// <summary>Обновление сида справочника действий на существующей базе (todo26: v2 → v3):
/// рост seedVersion подхватывает новые дефолтные промпты, правки пользователя не трогаются.</summary>
public sealed class Todo26SeedUpgradeTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Version_Growth_Updates_Defaults_But_Keeps_Custom_Edits()
    {
        // пользовательская правка промпта в «старой» базе
        var read = _f.Actions.List("ru").Single(a => a.Code == "AI2P.Files.Read");
        _f.Actions.Update(read.Id,
            new ActionSaveDto { Code = read.Code, Prompt = "МОЯ ПРАВКА" }, "ru");

        // имитация обновления дистрибутива: seedVersion выше + новый текст list_files
        var seedDir = Path.Combine(_f.Dir, "seed-next");
        Directory.CreateDirectory(seedDir);
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(AppContext.BaseDirectory, "i18n"), "ActionCatalogService_*.json"))
        {
            // версия сида берётся из самого файла и увеличивается: жёсткий номер здесь
            // ломался бы при каждом обновлении текстов дистрибутива
            var text = System.Text.RegularExpressions.Regex.Replace(
                    File.ReadAllText(file), "\"seedVersion\":\\s*(\\d+)",
                    m => $"\"seedVersion\": {int.Parse(m.Groups[1].Value) + 1}")
                .Replace("Список файлов в каталоге", "ОБНОВЛЁННЫЙ ПРОМПТ каталога");
            File.WriteAllText(Path.Combine(seedDir, Path.GetFileName(file)), text);
        }
        var actions = new ActionCatalogService(_f.Db, seedDir);
        actions.Seed();

        // новый дефолт подхватился, пользовательская правка не тронута
        Assert.Contains("ОБНОВЛЁННЫЙ ПРОМПТ", actions.PromptByTool("list_files", "ru"));
        Assert.Equal("МОЯ ПРАВКА", actions.PromptByTool("read_file", "ru"));
    }
}
