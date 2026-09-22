using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-298-S0 «Тип задачи (линейная/условие/цикл до/цикл после)»: базовая часть ветвления и
/// циклов T-297-S0. Тип и его параметры живут ключами launch_json (<see cref="TaskFlow"/>),
/// а НЕ в tasks.kind — старый сервер разобрал бы kind="if" как обычную задачу и выполнил бы
/// все ветки. Отсутствие ключа — «линейная»: так читаются все прежние задачи и шаблоны.
/// </summary>
public sealed class T298S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem Create(string title, string? parentId = null, string launchJson = """{"mode":"manual"}""",
        bool template = false, string? projectId = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            ProjectId = projectId,
            Status = TaskStatuses.Pending,
            LaunchJson = launchJson,
            IsTemplate = template,
        }, "", "", null);

    [Fact]
    public void A_Task_Without_The_Key_Reads_As_Linear()
    {
        Assert.Equal(TaskFlow.Linear, TaskFlow.Read("""{"mode":"auto","noAutoStart":true}""").Type);
        Assert.Equal(TaskFlow.Linear, TaskFlow.Read("не json").Type);
        Assert.Equal(TaskFlow.Linear, TaskFlow.Read("""{"flowType":"switch"}""").Type);
        Assert.Equal(TaskFlow.Linear, new TaskItem().Flow.Type);
    }

    [Fact]
    public void Write_Keeps_Other_Keys_And_Drops_Keys_Of_Another_Type()
    {
        var json = TaskFlow.Write("""{"mode":"auto","noAutoStart":true}""", new TaskFlowDto
        {
            Type = TaskFlow.Loop, RecheckLimit = 5, LoopStopHierarchy = true,
            IfTrueTaskId = "лишнее",
        });
        var flow = TaskFlow.Read(json);
        Assert.Equal(TaskFlow.Loop, flow.Type);
        Assert.Equal(5, flow.RecheckLimit);
        Assert.True(flow.LoopStopHierarchy);
        Assert.Null(flow.IfTrueTaskId);
        Assert.DoesNotContain(TaskFlow.IfTrueTaskKey, json);
        Assert.Contains("\"noAutoStart\":true", json);

        var linear = TaskFlow.Write(json, new TaskFlowDto());
        Assert.DoesNotContain(TaskFlow.TypeKey, linear);
        Assert.DoesNotContain(TaskFlow.LoopLimitKey, linear);
        Assert.Contains("\"mode\":\"auto\"", linear);
    }

    [Fact]
    public void A_Garbage_Limit_Does_Not_Throw()
    {
        var flow = TaskFlow.Read("""{"flowType":"do-loop","recheckLimit":"много"}""");
        Assert.Equal(TaskFlow.DoLoop, flow.Type);
        Assert.Null(flow.RecheckLimit);
    }

    [Fact]
    public void A_Branch_With_A_Task_Carries_No_Check_Boxes()
    {
        var json = TaskFlow.Write("{}", new TaskFlowDto
        {
            Type = TaskFlow.If, IfTrueTaskId = "a", IfTrueCreateTasks = true, IfTrueStopHierarchy = true,
            IfFalseCreateTasks = true, IfFalseStopHierarchy = true,
        });
        var flow = TaskFlow.Read(json);
        Assert.Equal("a", flow.IfTrueTaskId);
        Assert.False(flow.IfTrueCreateTasks);
        Assert.False(flow.IfTrueStopHierarchy);
        Assert.True(flow.IfFalseCreateTasks);
        // «завершить выполнение» есть только у ветви, которая задач не создаёт
        Assert.False(flow.IfFalseStopHierarchy);
    }

    [Fact]
    public void A_New_Loop_Gets_The_Project_Limit()
    {
        var task = Create("цикл", launchJson: """{"mode":"manual","flowType":"loop"}""");
        Assert.Equal(ProjectSettings.RecheckLimitDefault, _f.Tasks.Get(task.Id)!.Flow.RecheckLimit);
    }

    [Fact]
    public void A_Branch_Must_Be_A_Direct_Child()
    {
        var parent = Create("условие");
        var child = Create("да", parent.Id);
        var grandchild = Create("внук", child.Id);

        var edit = _f.Tasks.Get(parent.Id)!;
        edit.LaunchJson = TaskFlow.Write(edit.LaunchJson,
            new TaskFlowDto { Type = TaskFlow.If, IfTrueTaskId = grandchild.Id });
        Assert.Throws<ArgumentException>(() => _f.Tasks.Update(edit, "", "", null));

        edit.LaunchJson = TaskFlow.Write(edit.LaunchJson,
            new TaskFlowDto { Type = TaskFlow.If, IfTrueTaskId = child.Id, IfFalseStopHierarchy = true });
        _f.Tasks.Update(edit, "", "", null);
        var flow = _f.Tasks.Get(parent.Id)!.Flow;
        Assert.Equal(child.Id, flow.IfTrueTaskId);
        Assert.True(flow.IfFalseStopHierarchy);
    }

    [Fact]
    public void A_Template_Copy_Carries_The_Type_And_Remaps_The_Branches()
    {
        var head = Create("шаблон-условие", template: true);
        var yes = Create("ветвь да", head.Id, template: true);
        var edit = _f.Tasks.Get(head.Id)!;
        edit.LaunchJson = TaskFlow.Write(edit.LaunchJson,
            new TaskFlowDto { Type = TaskFlow.If, IfTrueTaskId = yes.Id, IfFalseCreateTasks = true });
        _f.Tasks.Update(edit, "", "", null);

        var copy = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        var flow = _f.Tasks.Get(copy.Id)!.Flow;
        Assert.Equal(TaskFlow.If, flow.Type);
        Assert.True(flow.IfFalseCreateTasks);
        Assert.NotNull(flow.IfTrueTaskId);
        Assert.NotEqual(yes.Id, flow.IfTrueTaskId);
        Assert.Equal(copy.Id, _f.Tasks.Get(flow.IfTrueTaskId!)!.ParentId);
    }

    [Fact]
    public void The_Dictionaries_Have_The_Form_Labels_In_Every_Language()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            var text = File.ReadAllText(Path.Combine(dir, lang + ".json"));
            foreach (var type in TaskFlow.Types)
            {
                Assert.Contains($"\"task.flow.{type}\"", text);
            }
            Assert.Contains("\"task.flow.loop.stop\"", text);
            Assert.Contains("\"msg.taskFlow.1\"", text);
        }
    }

    [Fact]
    public void The_Form_Has_The_Markup_For_Browser_Checks()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "AI2P.UI")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var razor = File.ReadAllText(Path.Combine(dir!, "src", "AI2P.UI", "Components", "TaskDialog.razor"));
        foreach (var mark in new[] { "data-task-flow-type", "data-task-flow-branch-task", "data-task-flow-branch-create",
                     "data-task-flow-branch-stop", "data-task-flow-limit", "data-task-flow-loop-stop" })
        {
            Assert.Contains(mark, razor);
        }
    }
}
