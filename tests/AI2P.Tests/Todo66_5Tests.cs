using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-132 (версия 1.66): ЗАДАНИЯ-СОСЕДИ — подзадачи ТОГО ЖЕ РОДИТЕЛЯ.
///
/// До этого потомок видел только родителя (get_parent_task, T-120) и умел искать по коду или
/// заголовку, но получить СПИСОК соседних подзадач с их результатами не мог — а именно это
/// нужно исполнителю очередной подзадачи, чтобы опереться на уже сделанное остальными.
/// Инструмент — get_sibling_tasks (действие справочника AI2P.Tasks.GetSiblings); у CLI-агента
/// инструментов AI2P нет, поэтому соседи подставляются ему блоком в промпт (как родитель).
/// Формулировок в списке нет намеренно: рядом уже едет родительский блок, промпт не резиновый.
/// </summary>
public sealed class Todo66_5Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string BaseUrl = "http://localhost:5480/ai2p/mmfgrp";

    /// <summary>Родитель с тремя подзадачами: у соседа с результатом есть исполнитель-ИИ.</summary>
    private (TaskItem Parent, TaskItem Current, TaskItem Done, TaskItem Pending, Project Project) Family()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = _f.Executors.Create(new Executor { Nick = "mrclaude", Kind = ExecutorKind.Ai }, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Доработка интерфейса" },
            "разбить на подзадачи", "", null);
        var done = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Доска: ленивый скролл",
            Status = TaskStatuses.Review, PriorityNum = 30, ExecutorIds = [ai.Id],
        }, "ОПИСАНИЕ СОСЕДА", "критерии соседа", null);
        _f.Files.WriteTaskArtifact(project.Slug, done.DisplayId, "J-1-result.md",
            "колонки скроллятся раздельно");
        var pending = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Вывод .md: выделение кода",
            PriorityNum = 24,
        }, "т", "", null);
        var current = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "ТЗ v1.66", PriorityNum = 5,
            // галочка «соседние задания подставлять в промпт» (T-23-S0): с T-29-S0 без неё
            // вместо блока едет строка-напоминание, а здесь проверяется сам блок
            SiblingsInPrompt = true,
        }, "написать ТЗ по сделанному", "", null);
        return (parent, current, done, pending, project);
    }

    private TaskToolset Toolset(TaskItem current) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current, publicBaseUrl: BaseUrl);

    private static Task<string> Call(TaskToolset tools) =>
        tools.ExecuteAsync("get_sibling_tasks", JsonDocument.Parse("{}").RootElement, default);

    // --- инструмент get_sibling_tasks (агент с инструментами AI2P) ---

    [Fact]
    public async Task Tool_Returns_Code_Title_Status_Executor_Link_And_Result()
    {
        var (_, current, done, pending, _) = Family();

        var tools = Toolset(current);
        var text = await Call(tools);

        Assert.Contains(done.DisplayId, text);                     // код
        Assert.Contains("Доска: ленивый скролл", text);            // заголовок
        Assert.Contains(TaskStatuses.Review, text);                // статус
        Assert.Contains("mrclaude", text);                         // исполнитель
        Assert.Contains($"{BaseUrl}/task/{done.Id}", text);        // ссылка
        Assert.Contains("колонки скроллятся раздельно", text);     // результат
        Assert.Contains(pending.DisplayId, text);                  // сосед без результата тоже в списке
        Assert.Contains("(результатов-артефактов пока нет)", text);
        // сама задача соседом себе не считается
        Assert.DoesNotContain(current.DisplayId, text);
        // формулировок соседей в списке нет — их читают отдельно по коду
        Assert.DoesNotContain("ОПИСАНИЕ СОСЕДА", text);
        Assert.Single(tools.CallLog);                              // вызов виден в журнале работ
    }

    [Fact]
    public async Task Tool_Says_There_Are_No_Siblings()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "т", "", null);
        var only = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Единственная",
        }, "т", "", null);

        Assert.Contains("нет других подзадач", await Call(Toolset(only)));
    }

    [Fact]
    public async Task Tool_Says_There_Is_No_Parent()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Одиночная" }, "т", "", null);

        Assert.Contains("нет родительской задачи", await Call(Toolset(task)));
    }

    [Fact]
    public async Task Deleted_Sibling_Is_Not_Listed()
    {
        var (_, current, done, _, _) = Family();
        _f.Tasks.Delete(done.Id, null);

        Assert.DoesNotContain(done.DisplayId, await Call(Toolset(current)));
    }

    [Fact]
    public async Task Sibling_From_Another_Parent_Is_Not_Listed()
    {
        var (_, current, _, _, project) = Family();
        var other = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Чужой родитель" },
            "т", "", null);
        var stranger = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = other.Id, Title = "Чужая подзадача",
        }, "т", "", null);

        Assert.DoesNotContain(stranger.DisplayId, await Call(Toolset(current)));
    }

    // --- блок соседей в промпте CLI-агента ---

    [Fact]
    public void Prompt_Block_Has_Results_But_No_Descriptions()
    {
        var (_, current, done, _, _) = Family();
        var tools = Toolset(current);

        var block = tools.SiblingsPromptBlock();

        Assert.Contains(done.DisplayId, block);
        Assert.Contains("колонки скроллятся раздельно", block);   // результат нужен
        Assert.DoesNotContain("ОПИСАНИЕ СОСЕДА", block);          // а полные описания — нет
        Assert.Single(tools.CallLog);
    }

    [Fact]
    public void Prompt_Block_Is_Empty_Without_Parent_Or_Siblings()
    {
        var single = _f.Tasks.Create(new TaskItem { Title = "Одиночная" }, "т", "", null);
        Assert.Equal("", Toolset(single).SiblingsPromptBlock());

        var project = _f.Projects.Create("Проект-2", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "т", "", null);
        var only = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Единственная",
        }, "т", "", null);
        Assert.Equal("", Toolset(only).SiblingsPromptBlock());
    }

    [Fact]
    public void Single_Loud_Sibling_Is_Clipped()
    {
        // болтливый сосед не должен съесть блок целиком: его результат режется отдельно
        var (_, current, done, _, project) = Family();
        Loud(project, current, "Болтливая", "J-9-result.md");

        var block = Toolset(current).SiblingsPromptBlock();

        Assert.Contains("обрезано до 4000 симв.", block);
        Assert.Contains(done.DisplayId, block); // соседям после болтливого место осталось
    }

    [Fact]
    public void Whole_Prompt_Block_Is_Clipped()
    {
        // болтливых соседей много — режется и блок целиком: он едет рядом с родительским,
        // и вдвоём они не должны раздуть промпт
        var (_, current, _, _, project) = Family();
        for (var i = 0; i < 8; i++)
        {
            Loud(project, current, $"Болтливая {i}", $"J-{i}-result.md");
        }

        var block = Toolset(current).SiblingsPromptBlock();

        Assert.True(block.Length < 21_000, $"блок не обрезан: {block.Length} симв.");
        Assert.Contains("соседние задания обрезаны", block);
    }

    /// <summary>Сосед с огромным результатом-артефактом.</summary>
    private void Loud(Project project, TaskItem current, string title, string artifact)
    {
        var loud = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = current.ParentId, Title = title,
        }, "т", "", null);
        _f.Files.WriteTaskArtifact(project.Slug, loud.DisplayId, artifact, new string('я', 200_000));
    }

    [Fact]
    public void Toolset_Delivers_Siblings_Block()
    {
        var (_, current, done, _, _) = Family();
        var tools = new AgentToolset(null, Toolset(current));

        Assert.Contains(done.DisplayId, tools.SiblingTasksBlock);
    }

    // --- правила безопасности: одно правило закрывает и инструмент, и блок ---

    [Fact]
    public async Task Denied_Action_Rule_Stops_Both_Block_And_Tool()
    {
        var (_, current, done, _, _) = Family();
        var rules = new List<SecurityRule>
        {
            new()
            {
                Target = SecurityTarget.Action,
                Permission = SecurityPermission.Deny,
                Pattern = "AI2P.Tasks.GetSiblings",
            },
        };
        var tools = new AgentToolset(null, Toolset(current),
            security: new SecurityEvaluator(rules, null),
            actionCodeByTool: name => name == "get_sibling_tasks" ? "AI2P.Tasks.GetSiblings" : null);

        // блок в промпте не подставляется, срабатывание видно человеку
        Assert.Equal("", tools.SiblingTasksBlock);
        Assert.Single(tools.SecurityTriggers);
        Assert.Contains("Deny", tools.SecurityTriggers[0]);

        // и сам вызов инструмента отклоняется ядром до выполнения
        var check = tools.Authorize("get_sibling_tasks", JsonDocument.Parse("{}").RootElement);
        Assert.Equal(SecurityDecision.Deny, check.Decision);
        // родительское задание тем же правилом не закрыто — действия разные
        Assert.Equal(SecurityDecision.Allow,
            tools.Authorize("get_parent_task", JsonDocument.Parse("{}").RootElement).Decision);
        Assert.Contains(done.DisplayId, await Call(Toolset(current))); // без правил — доступно
    }

    // --- сборка промпта CLI-агента ---

    [Fact]
    public void Cli_Prompt_Puts_Siblings_After_Parent()
    {
        var prompt = ClaudeCliConnector.BuildPrompt("СИСТЕМА", "ЗАДАНИЕ",
            "# КОНТЕКСТ: родительское задание T-9", "# Соседние задания: подзадачи T-9");

        Assert.True(prompt.IndexOf("родительское задание", StringComparison.Ordinal)
                    < prompt.IndexOf("Соседние задания", StringComparison.Ordinal),
            "соседи должны идти после родителя");
    }

    [Fact]
    public void Cli_Prompt_Has_No_Empty_Siblings_Section()
    {
        // соседей нет — лишней секции с разделителем в промпте быть не должно
        Assert.EndsWith("ЗАДАНИЕ", ClaudeCliConnector.BuildPrompt("СИСТЕМА", "ЗАДАНИЕ", "", ""));
        Assert.EndsWith("РОДИТЕЛЬ", ClaudeCliConnector.BuildPrompt("СИСТЕМА", "ЗАДАНИЕ", "РОДИТЕЛЬ", ""));
    }
}
