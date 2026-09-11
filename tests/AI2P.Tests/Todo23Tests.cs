using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Инструменты поиска и чтения заданий для агента (todo23): родительское задание,
/// по коду, по URL, по заголовку; доступ ограничен проектом текущей задачи.
/// </summary>
public sealed class TaskToolsetTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static async Task<string> Call(TaskToolset tools, string name, string argsJson = "{}")
        => await tools.ExecuteAsync(name, Args(argsJson), CancellationToken.None);

    [Fact]
    public async Task Parent_Task_Is_Returned_With_Description_And_Artifacts()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" },
            "сделай список дестратификаторов", "критерий: список полон", null);
        _f.Files.WriteTaskArtifact(project.Slug, parent.DisplayId, "J-1-result.md", "готовый список: А, Б");
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Проверка",
        }, "проверь родителя", "", null);

        var tools = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, child);
        var result = await Call(tools, "get_parent_task");

        Assert.Contains(parent.DisplayId, result);
        Assert.Contains("сделай список дестратификаторов", result);
        Assert.Contains("критерий: список полон", result);
        Assert.Contains("готовый список: А, Б", result);
        Assert.Single(tools.CallLog);
    }

    [Fact]
    public async Task Task_Is_Found_By_Code_By_Url_And_By_Title()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Список дестратификаторов" },
            "описание задачи", "", null);
        var other = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Другая" }, "т", "", null);

        var tools = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, other);

        var byCode = await Call(tools, "get_task_by_code", $$"""{"code":"{{task.DisplayId}}"}""");
        Assert.Contains("описание задачи", byCode);

        var byUrl = await Call(tools, "get_task_by_url",
            $$"""{"url":"http://localhost:5480/ai2p/task/{{task.Id}}"}""");
        Assert.Contains("описание задачи", byUrl);

        var byTitle = await Call(tools, "find_tasks_by_title", """{"title":"дестратификатор"}""");
        Assert.Contains(task.DisplayId, byTitle);
        Assert.DoesNotContain(other.DisplayId, byTitle);
    }

    [Fact]
    public async Task Access_Is_Limited_To_Current_Project()
    {
        var p1 = _f.Projects.Create("Первый", null, null, null);
        var p2 = _f.Projects.Create("Второй", null, null, null);
        var foreign = _f.Tasks.Create(new TaskItem { ProjectId = p1.Id, Title = "Чужая" }, "секрет", "", null);
        var current = _f.Tasks.Create(new TaskItem { ProjectId = p2.Id, Title = "Наша" }, "т", "", null);

        var tools = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current);

        var byUrl = await Call(tools, "get_task_by_url", $$"""{"url":"http://x/task/{{foreign.Id}}"}""");
        Assert.DoesNotContain("секрет", byUrl);
        Assert.Contains("другому проекту", byUrl);

        var byCode = await Call(tools, "get_task_by_code", $$"""{"code":"{{foreign.DisplayId}}"}""");
        Assert.DoesNotContain("секрет", byCode);
    }

    [Fact]
    public async Task Missing_Parent_And_Bad_Url_Give_Readable_Errors()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Без родителя" }, "т", "", null);
        var tools = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task);

        Assert.Contains("нет родительской", await Call(tools, "get_parent_task"));
        Assert.Contains("Не удалось разобрать", await Call(tools, "get_task_by_url", """{"url":"http://x/y"}"""));
        Assert.Contains("не найдена", await Call(tools, "get_task_by_url", """{"url":"http://x/task/nope"}"""));
    }

    [Fact]
    public async Task Chat_Is_Readable_For_Current_Task_And_By_Code()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var ai = _f.Executors.Create(new Executor { Nick = "bot", Kind = ExecutorKind.Ai }, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "т", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, ParentId = parent.Id, Title = "Проверка",
        }, "т", "", null);
        _f.Chat.Add(parent.Id, human.Id, ai.Id, "уточняю: формат md");
        _f.Chat.Add(child.Id, human.Id, null, "сообщение в текущей задаче");

        var tools = new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, child);

        // без code — чат текущей задачи
        var own = await Call(tools, "get_task_chat");
        Assert.Contains("сообщение в текущей задаче", own);
        Assert.Contains("mike", own);

        // по коду — чат родителя (другой задачи проекта); адресат виден
        var parentChat = await Call(tools, "get_task_chat", $$"""{"code":"{{parent.DisplayId}}"}""");
        Assert.Contains("уточняю: формат md", parentChat);
        Assert.Contains("mike → bot", parentChat);

        // карточка задания подсказывает про чат
        var card = await Call(tools, "get_parent_task");
        Assert.Contains("get_task_chat", card);
    }

    [Fact]
    public async Task Agent_Toolset_Dispatches_Task_Tools_Without_Project_Folder()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        var toolset = new AgentToolset(files: null, new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task));

        // без папки проекта файловых инструментов нет, но инструменты заданий доступны
        Assert.DoesNotContain(toolset.Specs, s => s.Name == "read_file");
        Assert.Contains(toolset.Specs, s => s.Name == "get_parent_task");

        var fileResult = await toolset.ExecuteAsync("read_file", Args("""{"path":"x"}"""), CancellationToken.None);
        Assert.Contains("папка проекта не задана", fileResult);
    }
}
