using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-755-S0: create_template / update_template умеют «статус при завершении» (aiDoneStatus),
/// «время/качество» (timeQuality) и запасных исполнителей (altExecutors). Без статуса узлы-
/// блокирующие шаблона публикации завершались в «проверке», а блокирующая считается
/// завершённой только в «готово» — цепочка копии вставала. Не переданное поле не меняется,
/// пустая строка — снять; копия узла в задачу (InstantiateTemplate) значения сохраняет.
/// </summary>
public sealed class T755S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Project _project = null!;

    private Project Project => _project ??= _f.Projects.Create("Проект", null, null, null);

    private TaskToolset Tools()
    {
        var current = _f.Tasks.Create(new TaskItem { ProjectId = Project.Id, Title = "Задача" }, "текст", "", null);
        return new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current, _f.RefData, _f.Picker, null,
            _f.Experience, project: Project);
    }

    private Executor Human(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private static System.Text.Json.JsonElement Args(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    private TaskItem Node(string title) =>
        _f.Tasks.List(Project.Id, includeTemplates: true).Single(t => t.IsTemplate && t.Title == title);

    [Fact]
    public async Task Create_Template_Sets_Done_Status_Time_Quality_And_Alternates()
    {
        var spare = Human("spare");
        var answer = await Tools().ExecuteAsync("create_template",
            Args("""{"title":"Прогон тестов","description":"гнать всё","aiDoneStatus":"done","timeQuality":1,"altExecutors":["spare"]}"""),
            CancellationToken.None);

        Assert.DoesNotContain("Ошибка", answer);
        var node = _f.Tasks.Get(Node("Прогон тестов").Id)!;
        Assert.Equal(TaskStatuses.Done, node.AiDoneStatus);
        Assert.Equal(1.0, node.TimeQuality);
        Assert.Equal([spare.Id], node.AltExecutorIds);
    }

    [Fact]
    public async Task Create_Template_Without_New_Fields_Keeps_Defaults()
    {
        await Tools().ExecuteAsync("create_template",
            Args("""{"title":"Узел","description":"текст"}"""), CancellationToken.None);

        var node = _f.Tasks.Get(Node("Узел").Id)!;
        Assert.Equal(TaskStatuses.Review, node.AiDoneStatus);
        Assert.Null(node.TimeQuality);
        Assert.Empty(node.AltExecutorIds);
    }

    [Fact]
    public async Task Update_Template_Changes_Only_Passed_Fields_And_Empty_String_Clears()
    {
        var spare = Human("spare");
        var tools = Tools();
        await tools.ExecuteAsync("create_template",
            Args("""{"title":"Узел","description":"текст","aiDoneStatus":"done","timeQuality":"0,5","altExecutors":["spare"]}"""),
            CancellationToken.None);
        var code = Node("Узел").DisplayId;
        Assert.Equal(0.5, _f.Tasks.Get(Node("Узел").Id)!.TimeQuality);   // строкой и с запятой — как от клиента ai2p

        // правка заголовка не трогает новых полей
        await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","title":"Узел 2"}"""), CancellationToken.None);
        var node = _f.Tasks.Get(Node("Узел 2").Id)!;
        Assert.Equal(TaskStatuses.Done, node.AiDoneStatus);
        Assert.Equal(0.5, node.TimeQuality);
        Assert.Equal([spare.Id], node.AltExecutorIds);

        // строкой «1.00» — как от клиента ai2p
        await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","timeQuality":"1.00"}"""), CancellationToken.None);
        Assert.Equal(1.0, _f.Tasks.Get(node.Id)!.TimeQuality);

        // пустая строка — снять: статус к умолчанию «проверка», время/качество — к проекту
        var answer = await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","aiDoneStatus":"","timeQuality":"","altExecutors":[]}"""),
            CancellationToken.None);
        Assert.DoesNotContain("Ошибка", answer);
        node = _f.Tasks.Get(node.Id)!;
        Assert.Equal(TaskStatuses.Review, node.AiDoneStatus);
        Assert.Null(node.TimeQuality);
        Assert.Empty(node.AltExecutorIds);
    }

    [Fact]
    public async Task Bad_Values_Are_Refused_And_Nothing_Changes()
    {
        var tools = Tools();
        await tools.ExecuteAsync("create_template",
            Args("""{"title":"Узел","description":"текст"}"""), CancellationToken.None);
        var code = Node("Узел").DisplayId;

        Assert.StartsWith("Ошибка", await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","aiDoneStatus":"in_progress"}"""), CancellationToken.None));
        Assert.StartsWith("Ошибка", await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","timeQuality":1.5}"""), CancellationToken.None));
        Assert.StartsWith("Ошибка", await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{code}}","timeQuality":"быстро"}"""), CancellationToken.None));
        Assert.StartsWith("Ошибка", await tools.ExecuteAsync("create_template",
            Args("""{"title":"Другой","description":"текст","aiDoneStatus":"готово"}"""), CancellationToken.None));

        var node = _f.Tasks.Get(Node("Узел").Id)!;
        Assert.Equal(TaskStatuses.Review, node.AiDoneStatus);
        Assert.Null(node.TimeQuality);
        Assert.DoesNotContain(_f.Tasks.List(Project.Id, includeTemplates: true), t => t.Title == "Другой");
    }

    [Fact]
    public async Task Copy_Of_The_Node_Keeps_Done_Status_And_Time_Quality()
    {
        var tools = Tools();
        await tools.ExecuteAsync("create_template",
            Args("""{"title":"Публикация","description":"корень"}"""), CancellationToken.None);
        var root = Node("Публикация");
        await tools.ExecuteAsync("create_template",
            Args($$"""{"title":"Прогон тестов","description":"гнать всё","parent":"{{root.DisplayId}}","aiDoneStatus":"done","timeQuality":"1.00"}"""),
            CancellationToken.None);

        var copy = _f.Tasks.InstantiateTemplate(root.Id, null, null);

        var child = _f.Tasks.List(Project.Id).Single(t => !t.IsTemplate && t.ParentId == copy.Id);
        child = _f.Tasks.Get(child.Id)!;
        Assert.Equal("Прогон тестов", child.Title);
        Assert.Equal(TaskStatuses.Done, child.AiDoneStatus);
        Assert.Equal(1.0, child.TimeQuality);
        Assert.Equal(TaskStatuses.Done, _f.Tasks.AiDoneStatusOf(child.Id));
        Assert.Equal(1.0, _f.Tasks.TimeQualityOf(child.Id));
    }
}
