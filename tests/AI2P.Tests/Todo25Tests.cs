using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Безопасность (ТЗ гл. 12, todo25): три уровня правил, складывание с учётом иерархии задач,
/// оценка (действия / каталоги / команды OS), проверка ядром до выполнения инструментов.
/// </summary>
public sealed class SecurityRuleTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static SecurityRule Rule(string scope, string? scopeId, string target,
        string permission, string pattern, bool read = false, bool write = false, bool delete = false) =>
        new()
        {
            Scope = scope, ScopeId = scopeId, Target = target, Permission = permission,
            Pattern = pattern, OpRead = read, OpWrite = write, OpDelete = delete,
        };

    [Fact]
    public void Validation_Rejects_Bad_Rules()
    {
        // глобальное правило каталога с относительным путём — нельзя (todo25)
        Assert.Throws<ArgumentException>(() => _f.Security.Create(
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Allow, "sub/dir", read: true), null));
        // правило каталога без действий над файлами — нельзя
        Assert.Throws<ArgumentException>(() => _f.Security.Create(
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Allow, @"C:\tmp\*"), null));
        // правило уровня проекта без id проекта — нельзя
        Assert.Throws<ArgumentException>(() => _f.Security.Create(
            Rule(SecurityScope.Project, null, SecurityTarget.Action,
                SecurityPermission.Deny, "AI2P.Files.Write"), null));
    }

    [Fact]
    public void Rules_Fold_Global_Project_And_Task_Hierarchy_Lower_Level_Wins()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "т", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Потомок",
        }, "т", "", null);

        // глобально: запрет rm *; у потомка то же правило — переспросить (нижний перекрывает)
        _f.Security.Create(Rule(SecurityScope.Global, null, SecurityTarget.OsCommand,
            SecurityPermission.Deny, "rm *"), null);
        _f.Security.Create(Rule(SecurityScope.Task, child.Id, SecurityTarget.OsCommand,
            SecurityPermission.Confirm, "rm *"), null);
        // правило проекта и правило родительской задачи наследуются потомком
        _f.Security.Create(Rule(SecurityScope.Project, project.Id, SecurityTarget.Action,
            SecurityPermission.Deny, "AI2P.Files.Write"), null);
        _f.Security.Create(Rule(SecurityScope.Task, parent.Id, SecurityTarget.Action,
            SecurityPermission.Confirm, "AI2P.Tasks.GetChat"), null);

        var effective = _f.Security.EffectiveForTask(_f.Tasks.Get(child.Id)!);
        // rm * одно (перекрыто уровнем задачи), плюс два разных правила действий
        Assert.Equal(3, effective.Count);
        Assert.Equal(SecurityPermission.Confirm,
            effective.Single(r => r.Pattern == "rm *").Permission);
        Assert.Contains(effective, r => r.Pattern == "AI2P.Files.Write");
        Assert.Contains(effective, r => r.Pattern == "AI2P.Tasks.GetChat");

        var evaluator = new SecurityEvaluator(effective, null);
        Assert.Equal(SecurityDecision.Confirm, evaluator.ForOsCommand("rm -rf /").Decision);
        Assert.Equal(SecurityDecision.Deny, evaluator.ForAction("AI2P.Files.Write").Decision);
        Assert.Equal(SecurityDecision.Confirm, evaluator.ForAction("AI2P.Tasks.GetChat").Decision);
        // действия без правил — разрешены по умолчанию
        Assert.Equal(SecurityDecision.Allow, evaluator.ForAction("AI2P.Files.Read").Decision);
    }

    [Fact]
    public void Template_Instantiation_Copies_Task_Rules()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var template = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Шаблон", IsTemplate = true,
        }, "т", "", null);
        _f.Security.Create(Rule(SecurityScope.Task, template.Id, SecurityTarget.OsCommand,
            SecurityPermission.Deny, "format *"), null);

        var created = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        var copied = _f.Security.List(SecurityScope.Task, created.Id);
        Assert.Single(copied);
        Assert.Equal("format *", copied[0].Pattern);
        Assert.Equal(SecurityPermission.Deny, copied[0].Permission);
        // правило шаблона осталось на месте
        Assert.Single(_f.Security.List(SecurityScope.Task, template.Id));
    }

    [Fact]
    public void Directory_Defaults_Project_Open_Outside_Denied()
    {
        var root = Path.Combine(_f.Dir, "proj-root");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(_f.Dir, "outside");
        Directory.CreateDirectory(outside);

        var evaluator = new SecurityEvaluator([], root);
        // каталог проекта открыт полностью (todo25)
        Assert.Equal(SecurityDecision.Allow,
            evaluator.ForPath(Path.Combine(root, "a", "b.txt"), "write").Decision);
        // вне каталога проекта — запрещено без правил
        Assert.Equal(SecurityDecision.Deny,
            evaluator.ForPath(Path.Combine(outside, "x.txt"), "read").Decision);

        // разрешающее правило открывает каталог вне проекта (например C:\tmp\*)
        var rules = new List<SecurityRule>
        {
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Allow, Path.Combine(outside, "*"), read: true, write: true),
        };
        var opened = new SecurityEvaluator(rules, root);
        Assert.Equal(SecurityDecision.Allow,
            opened.ForPath(Path.Combine(outside, "x.txt"), "read").Decision);
        // удаление правилом не покрыто — остаётся запрет
        Assert.Equal(SecurityDecision.Deny,
            opened.ForPath(Path.Combine(outside, "x.txt"), "delete").Decision);
    }

    [Fact]
    public void Directory_Rules_Support_Relative_Paths_And_Strictest_Wins()
    {
        var root = Path.Combine(_f.Dir, "proj2");
        Directory.CreateDirectory(root);

        var rules = new List<SecurityRule>
        {
            // относительный путь — от папки проекта (уровень проекта/задачи, todo25)
            Rule(SecurityScope.Project, "p", SecurityTarget.Directory,
                SecurityPermission.Deny, Path.Combine("secrets", "*"), read: true, write: true),
            // из разных подходящих правил побеждает самое строгое
            Rule(SecurityScope.Project, "p", SecurityTarget.Directory,
                SecurityPermission.Allow, Path.Combine("secrets", "*"), read: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        Assert.Equal(SecurityDecision.Deny,
            evaluator.ForPath(Path.Combine(root, "secrets", "key.txt"), "read").Decision);
        // остальной каталог проекта открыт
        Assert.Equal(SecurityDecision.Allow,
            evaluator.ForPath(Path.Combine(root, "readme.md"), "read").Decision);
    }

    [Fact]
    public void Full_Path_Detection_Is_Os_Independent()
    {
        // полный путь — независимо от ОС приложения (todo25-фикс): Windows, unix, UNC
        Assert.True(SecurityRulePaths.IsFullPath(@"c:\tmp\*"));
        Assert.True(SecurityRulePaths.IsFullPath("C:/tmp/*"));
        Assert.True(SecurityRulePaths.IsFullPath("/tmp/*"));
        Assert.True(SecurityRulePaths.IsFullPath(@"\\server\share\*"));
        Assert.False(SecurityRulePaths.IsFullPath("sub/dir"));
        Assert.False(SecurityRulePaths.IsFullPath("tmp"));
        // кириллическая буква диска («с» с русской раскладки) — не полный путь,
        // а отдельная понятная ошибка
        Assert.False(SecurityRulePaths.IsFullPath(@"с:\tmp\*"));
        Assert.True(SecurityRulePaths.HasCyrillicDriveLetter(@"с:\tmp\*"));
        Assert.False(SecurityRulePaths.HasCyrillicDriveLetter(@"c:\tmp\*"));

        // валидация сервиса: кириллическая буква диска — сообщение про раскладку
        var cyrillic = Assert.Throws<ArgumentException>(() => _f.Security.Create(
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Allow, @"с:\tmp\*", read: true), null));
        Assert.Contains("кириллицей", cyrillic.Message);

        // unix-стиль полного пути принимается глобальным правилом на любой ОС
        var unixRule = _f.Security.Create(Rule(SecurityScope.Global, null, SecurityTarget.Directory,
            SecurityPermission.Allow, "/tmp/*", read: true), null);
        Assert.Equal("/tmp/*", unixRule.Pattern);
    }

    [Fact]
    public void Unix_Style_Pattern_Is_Not_Treated_As_Project_Relative()
    {
        var root = Path.Combine(_f.Dir, "proj5");
        Directory.CreateDirectory(root);
        // раньше "/tmp/*" считался бы относительным (Path.IsPathRooted зависит от ОС) и
        // склеивался с папкой проекта; теперь это полный путь — внутри проекта не срабатывает
        var rules = new List<SecurityRule>
        {
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Deny, "/tmp/*", read: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        Assert.Equal(SecurityDecision.Allow,
            evaluator.ForPath(Path.Combine(root, "tmp", "x.txt"), "read").Decision);
    }

    [Fact]
    public void Os_Command_Patterns_Wildcard_And_Regex()
    {
        var rules = new List<SecurityRule>
        {
            Rule(SecurityScope.Global, null, SecurityTarget.OsCommand, SecurityPermission.Deny, "rm *"),
            Rule(SecurityScope.Global, null, SecurityTarget.OsCommand, SecurityPermission.Allow, "git status"),
            Rule(SecurityScope.Global, null, SecurityTarget.OsCommand, SecurityPermission.Deny, "re:^curl\\s+.*"),
        };
        var evaluator = new SecurityEvaluator(rules, null);
        Assert.Equal(SecurityDecision.Deny, evaluator.ForOsCommand("rm -rf C:\\").Decision);
        Assert.Equal(SecurityDecision.Allow, evaluator.ForOsCommand("git status").Decision);
        Assert.Equal(SecurityDecision.Deny, evaluator.ForOsCommand("curl http://evil").Decision);
        // без правил — переспросить (задел: инструментов OS пока нет)
        Assert.Equal(SecurityDecision.Confirm, evaluator.ForOsCommand("dir").Decision);
    }

    [Fact]
    public async Task Toolset_Authorize_Checks_Rules_Before_Execution()
    {
        var root = Path.Combine(_f.Dir, "proj3");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "inside.txt"), "внутри");
        var outside = Path.Combine(_f.Dir, "outside3");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "секрет снаружи");

        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        var rules = new List<SecurityRule>
        {
            // запись в проект — только через подтверждение; чтение снаружи открыто правилом
            Rule(SecurityScope.Global, null, SecurityTarget.Action,
                SecurityPermission.Confirm, "AI2P.Files.Write"),
            Rule(SecurityScope.Global, null, SecurityTarget.Directory,
                SecurityPermission.Allow, Path.Combine(outside, "*"), read: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        var files = new FileToolset(root, null, null,
            (abs, op) => evaluator.ForPath(abs, op).Decision != SecurityDecision.Deny);
        var toolset = new AgentToolset(files,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            security: evaluator, actionCodeByTool: _f.Actions.CodeByTool);

        JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

        // запись — переспросить (правило действия AI2P.Files.Write из справочника по tool_name)
        var write = toolset.Authorize("write_file", Args("""{"path":"a.txt","content":"x"}"""));
        Assert.Equal(SecurityDecision.Confirm, write.Decision);
        Assert.Contains("подтверждения", write.Message);

        // чтение внутри проекта — разрешено по умолчанию
        Assert.Equal(SecurityDecision.Allow,
            toolset.Authorize("read_file", Args("""{"path":"inside.txt"}""")).Decision);

        // чтение вне проекта: без правила — запрет, с разрешающим правилом — разрешено
        var deniedOutside = toolset.Authorize("read_file",
            Args($$"""{"path":{{JsonSerializer.Serialize(Path.Combine(_f.Dir, "nowhere.txt"))}}}"""));
        Assert.Equal(SecurityDecision.Deny, deniedOutside.Decision);
        var allowedOutside = toolset.Authorize("read_file",
            Args($$"""{"path":{{JsonSerializer.Serialize(Path.Combine(outside, "secret.txt"))}}}"""));
        Assert.Equal(SecurityDecision.Allow, allowedOutside.Decision);

        // и само выполнение чтения снаружи работает (граница каталога — по правилам)
        var text = await files.ExecuteAsync("read_file",
            Args($$"""{"path":{{JsonSerializer.Serialize(Path.Combine(outside, "secret.txt"))}}}"""),
            CancellationToken.None);
        Assert.Contains("секрет снаружи", text);

        // срабатывания записаны для журнала (rule.triggered, п. 12.3)
        Assert.Contains(toolset.SecurityTriggers, t => t.StartsWith("Confirm"));
        Assert.Contains(toolset.SecurityTriggers, t => t.StartsWith("Deny"));
    }

    [Fact]
    public async Task Denied_Directory_Blocks_Execution_And_Hides_Files_From_Listing()
    {
        var root = Path.Combine(_f.Dir, "proj4");
        Directory.CreateDirectory(Path.Combine(root, "secrets"));
        File.WriteAllText(Path.Combine(root, "open.txt"), "открыто");
        File.WriteAllText(Path.Combine(root, "secrets", "key.txt"), "ключ");

        var rules = new List<SecurityRule>
        {
            Rule(SecurityScope.Project, "p", SecurityTarget.Directory,
                SecurityPermission.Deny, "secrets\\*", read: true, write: true),
        };
        var evaluator = new SecurityEvaluator(rules, root);
        var files = new FileToolset(root, null, null,
            (abs, op) => evaluator.ForPath(abs, op).Decision != SecurityDecision.Deny);

        JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

        // чтение закрытого файла — понятный отказ
        var denied = await files.ExecuteAsync("read_file",
            Args("""{"path":"secrets/key.txt"}"""), CancellationToken.None);
        Assert.Contains("запрещён правилами безопасности", denied);

        // в списке файлов закрытое не показывается
        var listing = await files.ExecuteAsync("list_files", Args("{}"), CancellationToken.None);
        Assert.Contains("open.txt", listing);
        Assert.DoesNotContain("key.txt", listing);
    }
}
