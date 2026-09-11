using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-54-S0 «Ошибка запуска задачи»: у автоматических запусков не было выключателя.
///
/// Жалоба заказчика: разобранные подзадачи он переводил в «готово» руками — и каждый такой
/// перевод сам запускал работу дальше. Остановить это было нечем: кнопка «остановить»
/// снимает ОДНО задание, а кнопка остановки очереди иерархии (T-263) показывается только
/// при ОТКРЫТОЙ очереди, которой в этом случае и нет. Автоматических стартов между тем три
/// разных: потомки выполненной задачи (todo22), задачи, ждавшие её как блокирующую
/// (ТЗ п. 2.12), и очередь подзадач авторазбиения (ТЗ v1.26).
///
/// Теперь у задачи есть пометка «автозапуск подзадач выключен» (launch_json, noAutoStart).
/// Она действует на ВСЁ поддерево (ищется по цепочке предков, как runHierarchy), закрывает
/// все три источника автоматических стартов и заодно закрывает открытую очередь иерархии.
/// Запуск КНОПКОЙ она не отменяет: нажатие «запустить иерархию» её с корня снимает.
/// </summary>
public sealed class T54S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor Human() =>
        _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);

    private TaskItem Task(string title, string? parentId = null, string? executorId = null,
        List<string>? blockers = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = TaskStatuses.Pending,
            LaunchJson = """{"mode":"auto"}""",
            ExecutorIds = executorId is null ? [] : [executorId],
            BlockerIds = blockers ?? [],
        }, "текст", "", null);

    private string Status(TaskItem task) => _f.Tasks.Get(task.Id)!.Status;

    private bool Off(TaskItem task) =>
        TaskService.HasLaunchFlag(_f.Tasks.Get(task.Id)!.LaunchJson, TaskService.NoAutoStartFlag);

    private bool QueueOpen(TaskItem root) =>
        TaskService.HasLaunchFlag(_f.Tasks.Get(root.Id)!.LaunchJson, TaskService.HierarchyFlag);

    // ---------- 1. автозапуск потомков выполненной задачи ----------

    [Fact]
    public async Task Without_The_Mark_A_Completed_Parent_Still_Starts_Its_Children()
    {
        // контрольный опыт: прежнее поведение никуда не делось — иначе следующие проверки
        // ничего бы не доказывали (потомок мог не стартовать и по другой причине)
        var human = Human();
        var parent = Task("родитель");
        var child = Task("подзадача", parent.Id, human.Id);

        await _f.Orchestrator.AutoStartChildrenAsync(parent, null);

        Assert.Equal(TaskStatuses.InProgress, Status(child));
    }

    [Fact]
    public async Task A_Completed_Parent_Does_Not_Start_Children_When_AutoStart_Is_Off()
    {
        var human = Human();
        var parent = Task("родитель");
        var child = Task("подзадача", parent.Id, human.Id);
        await _f.Orchestrator.SetAutoStartAsync(parent.Id, null, on: false);

        await _f.Orchestrator.AutoStartChildrenAsync(_f.Tasks.Get(parent.Id)!, null);

        Assert.True(Off(parent));
        Assert.Equal(TaskStatuses.Pending, Status(child));
    }

    [Fact]
    public async Task The_Mark_Covers_The_Whole_Subtree_Not_Just_The_Nearest_Children()
    {
        // ровно случай жалобы: пометка стоит у ДЕДА, а «готово» человек ставит потомку —
        // и внуки всё равно не должны трогаться с места
        var human = Human();
        var grand = Task("дед");
        var parent = Task("родитель", grand.Id);
        var child = Task("внук", parent.Id, human.Id);
        await _f.Orchestrator.SetAutoStartAsync(grand.Id, null, on: false);

        await _f.Orchestrator.AutoStartChildrenAsync(_f.Tasks.Get(parent.Id)!, null);

        Assert.False(Off(parent));  // у самой задачи пометки нет...
        Assert.Equal(grand.Id, _f.Orchestrator.AutoStartOffRootOf(_f.Tasks.Get(parent.Id)!)?.Id);
        Assert.Equal(TaskStatuses.Pending, Status(child)); // ...а запусков всё равно нет
    }

    // ---------- 2. автозапуск по завершению блокирующей ----------

    [Fact]
    public async Task A_Finished_Blocker_Does_Not_Start_A_Task_With_AutoStart_Off()
    {
        var human = Human();
        var blocker = Task("блокирующая");
        var branch = Task("ветка");
        var waits = Task("ждущая", branch.Id, human.Id, blockers: [blocker.Id]);
        _f.Tasks.ChangeStatus(blocker.Id, TaskStatuses.Done, null);
        await _f.Orchestrator.SetAutoStartAsync(branch.Id, null, on: false);

        await _f.Orchestrator.AutoStartUnblockedAsync(_f.Tasks.Get(blocker.Id)!, null);

        Assert.Equal(TaskStatuses.Pending, Status(waits));
    }

    // ---------- 3. очередь иерархии ----------

    [Fact]
    public async Task Turning_AutoStart_Off_Closes_The_Open_Hierarchy_Queue()
    {
        // «остановить автозапуск» при идущей очереди без её закрытия было бы обманом:
        // человек нажал «остановить», а работа продолжала бы раздаваться
        var human = Human();
        var root = Task("корень", executorId: human.Id);
        var child = Task("подзадача", root.Id, human.Id);
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(QueueOpen(root));

        var result = await _f.Orchestrator.SetAutoStartAsync(child.Id, null, on: false);

        Assert.True(result.HierarchyStopped);
        Assert.Equal(_f.Tasks.Get(root.Id)!.DisplayId, result.HierarchyRoot);
        Assert.False(QueueOpen(root));
    }

    [Fact]
    public async Task Starting_The_Hierarchy_By_Hand_Clears_The_Mark_On_The_Root()
    {
        // явное нажатие кнопки — это «беги»: оставить пометку значило бы получить очередь,
        // которая молчит непонятно почему
        var human = Human();
        var root = Task("корень", executorId: human.Id);
        Task("подзадача", root.Id, human.Id);
        await _f.Orchestrator.SetAutoStartAsync(root.Id, null, on: false);
        Assert.True(Off(root));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.False(Off(root));
        Assert.True(QueueOpen(root));
    }

    // ---------- 4. включение обратно ----------

    [Fact]
    public async Task Turning_AutoStart_Back_On_Removes_The_Mark_And_Starts_Nothing()
    {
        var human = Human();
        var parent = Task("родитель");
        var child = Task("подзадача", parent.Id, human.Id);
        await _f.Orchestrator.SetAutoStartAsync(parent.Id, null, on: false);

        var result = await _f.Orchestrator.SetAutoStartAsync(parent.Id, null, on: true);

        Assert.False(Off(parent));
        Assert.False(result.HierarchyStopped);
        Assert.Equal(TaskStatuses.Pending, Status(child)); // включение само ничего не запускает

        // а следующий переход родителя в «готово» снова поднимает потомка
        await _f.Orchestrator.AutoStartChildrenAsync(_f.Tasks.Get(parent.Id)!, null);
        Assert.Equal(TaskStatuses.InProgress, Status(child));
    }

    [Fact]
    public async Task The_Mark_On_A_Child_Does_Not_Stop_Its_Siblings()
    {
        // пометка — решение про конкретную ветку, а не про весь список потомков
        var human = Human();
        var parent = Task("родитель");
        var quiet = Task("тихая", parent.Id, human.Id);
        var loud = Task("обычная", parent.Id, human.Id);
        await _f.Orchestrator.SetAutoStartAsync(quiet.Id, null, on: false);

        await _f.Orchestrator.AutoStartChildrenAsync(_f.Tasks.Get(parent.Id)!, null);

        Assert.Equal(TaskStatuses.Pending, Status(quiet));
        Assert.Equal(TaskStatuses.InProgress, Status(loud));
    }
}
