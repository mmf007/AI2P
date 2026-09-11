using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-120 (версия 1.61): чтение родительского задания агентом БЕЗ инструментов AI2P.
///
/// У CLI-агента (Claude Code) инструменты AI2P не публикуются — позвать get_parent_task он
/// не может, а в файлах проекта задач нет (они в БД организации). Поэтому родительское
/// задание подставляется в промпт блоком: формулировка, критерии приёмки,
/// результаты-артефакты и чат. Правила безопасности действия AI2P.Tasks.GetParent
/// при этом соблюдаются.
/// </summary>
public sealed class Todo61Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Родитель с результатом и перепиской + его подзадача.</summary>
    private (TaskItem Parent, TaskItem Child, Project Project) Pair(string description = "обсуди лимиты агентов")
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Для обсуждения: лимиты агентов" },
            description, "критерий: перечислены варианты", null);
        _f.Files.WriteTaskArtifact(project.Slug, parent.DisplayId, "J-1-result.md",
            "1. указать лимиты у модели\n2. считать расход");
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Лимиты агента",
            // галочка «родительское задание подставлять в промпт» (T-23-S0): с T-29-S0
            // без неё вместо блока едет одна строка-напоминание, а здесь проверяется
            // именно содержимое блока
            ParentInPrompt = true,
        }, "делаем пункты 1–4", "", null);
        return (parent, child, project);
    }

    private TaskToolset Toolset(TaskItem current) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current);

    // --- блок родительского задания в промпте ---

    [Fact]
    public void Parent_Block_Has_Description_Acceptance_Result_And_Chat()
    {
        var (parent, child, _) = Pair();
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        _f.Chat.Add(parent.Id, human.Id, null, "давай ограничимся окном в сутки");

        var tools = Toolset(child);
        var block = tools.ParentPromptBlock();

        Assert.Contains(parent.DisplayId, block);
        Assert.Contains("Для обсуждения: лимиты агентов", block);
        Assert.Contains("обсуди лимиты агентов", block);          // формулировка
        Assert.Contains("критерий: перечислены варианты", block);  // критерии приёмки
        Assert.Contains("1. указать лимиты у модели", block);      // результат (артефакт)
        Assert.Contains("давай ограничимся окном в сутки", block); // чат
        // подсказки про инструмент get_task_chat в блоке быть не должно: у CLI-агента
        // инструментов AI2P нет, чат подставлен рядом целиком
        Assert.DoesNotContain("get_task_chat", block);
        Assert.Single(tools.CallLog);                              // вызов виден в журнале работ
    }

    [Fact]
    public void Parent_Block_Is_Empty_Without_Parent()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Одиночная" }, "т", "", null);
        var tools = Toolset(task);

        Assert.Equal("", tools.ParentPromptBlock());
        Assert.Empty(tools.CallLog);
    }

    [Fact]
    public void Parent_Block_Is_Empty_When_Parent_Deleted()
    {
        var (parent, child, _) = Pair();
        _f.Tasks.Delete(parent.Id, null);

        Assert.Equal("", Toolset(child).ParentPromptBlock());
    }

    [Fact]
    public void Parent_Block_Is_Clipped()
    {
        // родитель с огромной формулировкой не должен съедать весь контекст агента
        var (_, child, _) = Pair(new string('я', 200_000));

        var block = Toolset(child).ParentPromptBlock();

        Assert.True(block.Length < 41_000, $"блок не обрезан: {block.Length} симв.");
        Assert.Contains("обрезано", block);
    }

    // --- доставка через AgentToolset с проверкой правил безопасности ---

    [Fact]
    public void Toolset_Delivers_Parent_Block()
    {
        var (parent, child, _) = Pair();
        var tools = new AgentToolset(null, Toolset(child));

        Assert.Contains(parent.DisplayId, tools.ParentTaskBlock);
    }

    // --- сборка промпта CLI-агента ---

    [Fact]
    public void Cli_Prompt_Puts_Parent_Block_After_Task()
    {
        var prompt = ClaudeCliConnector.BuildPrompt("СИСТЕМА", "ЗАДАНИЕ", "# КОНТЕКСТ: родительское задание T-9");

        Assert.True(prompt.IndexOf("ЗАДАНИЕ", StringComparison.Ordinal)
                    < prompt.IndexOf("КОНТЕКСТ", StringComparison.Ordinal),
            "родительское задание должно идти после текста задания");
        Assert.StartsWith("СИСТЕМА", prompt);
    }

    [Fact]
    public void Cli_Prompt_Has_No_Empty_Parent_Section()
    {
        // родителя нет — лишней секции с разделителем в промпте быть не должно
        var prompt = ClaudeCliConnector.BuildPrompt("СИСТЕМА", "ЗАДАНИЕ", "");

        Assert.EndsWith("ЗАДАНИЕ", prompt);
    }

    [Fact]
    public void Denied_Action_Rule_Stops_Parent_Block()
    {
        var (_, child, _) = Pair();
        var rules = new List<SecurityRule>
        {
            new()
            {
                Target = SecurityTarget.Action,
                Permission = SecurityPermission.Deny,
                Pattern = "AI2P.Tasks.GetParent",
            },
        };
        var tools = new AgentToolset(null, Toolset(child),
            security: new SecurityEvaluator(rules, null),
            actionCodeByTool: name => name == "get_parent_task" ? "AI2P.Tasks.GetParent" : null);

        Assert.Equal("", tools.ParentTaskBlock);
        // срабатывание правила видно человеку — в журнале работ и в списке срабатываний
        Assert.Single(tools.SecurityTriggers);
        Assert.Contains("Deny", tools.SecurityTriggers[0]);
    }
}
