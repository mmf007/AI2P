using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-16-S0 «Запускать подзадачи, созданные ИИ-агентом, в составе идущей иерархии».
///
/// Жалоба (выпуск 1.101, T-241): агент по ходу работы завёл семь подзадач маркером
/// AI2P_SUBTASK, а очередь иерархии их не взяла — человеку пришлось нажимать «запустить
/// иерархию» ещё раз.
///
/// Разбор показал три вещи.
/// 1) Сам проход очереди потомков НЕ кеширует: <see cref="JobOrchestrator.ProcessHierarchyAsync"/>
///    читает их заново на каждом проходе, поэтому подзадачу, появившуюся во время работы
///    родителя, он видит. Это сторожат тесты 1–4: правка, которая захочет собрать список
///    потомков один раз на всю очередь, покраснит их.
/// 2) А вот ТРОНУТЬ очередь с места было нечем в двух случаях: (а) ход задания кончился
///    ПАУЗОЙ — агент задал вопрос, ушёл на подзадачи (T-185) или встал по лимиту провайдера
///    (T-121): список статусов, на которые подписан оркестратор, паузу не включал; (б) задание
///    родителя вообще не кончалось (агент ждёт ответа человека часами). Оставался только
///    сторож раз в минуту либо человек с кнопкой. Ровно так кончилось задание T-241: лимит
///    подписки → «пауза» → семь заведённых подзадач никто не взял.
/// 3) Поэтому очередь теперь двигают ДВА события: заведение подзадачи внутри открытой
///    иерархии (TaskService.Created) и конец хода задания, включая паузу.
///
/// Проверки идут на исполнителях-ЛЮДЯХ: путь запуска тот же (StartTaskAsync), а модель и
/// заглушки не нужны вовсе (приём T-210/T-224).
/// </summary>
public sealed class T16S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Executor CreateHuman(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private TaskItem CreateTask(string title, string? parentId = null, Executor? executor = null,
        int priority = 15, string status = TaskStatuses.Pending, List<string>? blockers = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = status,
            PriorityNum = priority,
            ExecutorIds = executor is null ? [] : [executor.Id],
            BlockerIds = blockers ?? [],
        }, "текст", "", null);

    /// <summary>Назначить исполнителя уже заведённой задаче. Правка задачи очередь НЕ двигает,
    /// поэтому так делается подзадача, которую при заведении взять было некому: дальше её
    /// может тронуть с места только проход по концу задания родителя.</summary>
    private void Assign(TaskItem task, Executor executor)
    {
        var fresh = _f.Tasks.Get(task.Id)!;
        fresh.ExecutorIds = [executor.Id];
        _f.Tasks.Update(fresh, "текст", "", null);
    }

    private int Jobs(TaskItem task) => _f.Jobs.ListByTask(task.Id).Count;

    private bool HasJob(TaskItem task) => Jobs(task) > 0;

    private static bool RunOpen(TaskItem task) =>
        TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyFlag);

    /// <summary>Очередь двигают фоновые проходы (Task.Run в обработчиках событий), поэтому
    /// ждём появления задания, а не читаем сразу.</summary>
    private async Task<bool> WaitJobAsync(TaskItem task, int seconds = 10)
    {
        for (var i = 0; i < seconds * 20 && !HasJob(task); i++)
        {
            await Task.Delay(50);
        }
        return HasJob(task);
    }

    /// <summary>Обратное ожидание: убедиться, что задание НЕ появилось. Ждём коротко —
    /// проход очереди на пустом хранилище занимает миллисекунды.</summary>
    private async Task<bool> StaysWithoutJobAsync(TaskItem task, int millis = 1500)
    {
        for (var i = 0; i < millis / 50; i++)
        {
            if (HasJob(task))
            {
                return false;
            }
            await Task.Delay(50);
        }
        return !HasJob(task);
    }

    // ---------- 1–4. подзадача, заведённая во время работы ----------

    [Fact]
    public async Task A_Subtask_Created_During_Work_Is_Started_At_Once()
    {
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(HasJob(a));
        Assert.False(HasJob(root)); // корень идёт последним

        // подзадача заведена ПОСЛЕ старта A — ровно то, что делает агент маркером AI2P_SUBTASK
        var a1 = CreateTask("A1", a.Id, CreateHuman("a1"));

        Assert.True(await WaitJobAsync(a1),
            "подзадача, заведённая во время работы, не запущена очередью");
        // корень по-прежнему ждёт: его поддерево не улажено, пока не сдана новая подзадача
        Assert.False(HasJob(root));
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task A_Subtask_Nobody_Could_Take_Is_Started_When_The_Job_Ends()
    {
        // подзадача заведена без исполнителя — при заведении её взять некому. Исполнитель
        // появляется позже (правка задачи очередь не двигает), и тронуть её с места должен
        // проход ПО КОНЦУ ЗАДАНИЯ родителя: именно он обязан собрать потомков заново
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        var a1 = CreateTask("A1", a.Id);
        Assert.True(await StaysWithoutJobAsync(a1)); // исполнителя нет — очередь пропускает
        Assign(a1, CreateHuman("a1"));
        Assert.True(await StaysWithoutJobAsync(a1)); // правка задачи очередь не двигает

        _f.Orchestrator.AnswerJob(_f.Jobs.ListByTask(a.Id)[0].Id, "готово", "actor");

        Assert.True(await WaitJobAsync(a1),
            "проход по концу задания не собрал потомков заново");
        Assert.False(HasJob(root));
    }

    [Fact]
    public async Task A_Subtask_Created_Deeper_In_The_Subtree_Is_Picked_Up_Too()
    {
        // корень → A → A1: подзадача заводится у ВНУКА, а проход идёт от корня
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));
        var a1 = CreateTask("A1", a.Id, CreateHuman("a1"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.True(HasJob(a1));

        var a2 = CreateTask("A1.1", a1.Id, CreateHuman("a2"));

        Assert.True(await WaitJobAsync(a2), "подзадача внука не запущена очередью");
        Assert.False(HasJob(a)); // A всё ещё ждёт своё поддерево
    }

    [Fact]
    public async Task The_Pass_Reads_The_Children_Again_On_Every_Run()
    {
        // сторож на кеширование: между двумя проходами ОДНОЙ очереди появляются новые
        // подзадачи, и следующий проход обязан их увидеть
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"), status: TaskStatuses.Review);

        var first = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        Assert.Equal([_f.Tasks.Get(root.Id)!.DisplayId], first.Started); // всё улажено — пошёл корень

        var fresh = CreateTask("новая", a.Id); // без исполнителя: проход при заведении пропустит
        Assert.True(await StaysWithoutJobAsync(fresh)); // дождались этого прохода
        Assign(fresh, CreateHuman("n"));
        var second = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);

        Assert.Contains(_f.Tasks.Get(fresh.Id)!.DisplayId, second.Started);
        Assert.True(HasJob(fresh));
    }

    // ---------- 5–8. конец хода ПАУЗОЙ тоже двигает очередь ----------

    [Fact]
    public async Task A_Paused_Parent_Moves_The_Queue()
    {
        // агент задал вопрос человеку (T-185/ТЗ v1.17): задача → «пауза». До T-16-S0 очередь
        // на этот переход не подписывалась вовсе, и заведённая подзадача ждала сторожа
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        var a1 = CreateTask("A1", a.Id);
        Assert.True(await StaysWithoutJobAsync(a1)); // взять некому — проход заведения пропустил
        Assign(a1, CreateHuman("a1"));
        Assert.True(await StaysWithoutJobAsync(a1)); // правка задачи очередь не двигает

        _f.Tasks.ChangeStatus(a.Id, TaskStatuses.Paused, null);

        Assert.True(await WaitJobAsync(a1),
            "подзадача не запущена: ход задания кончился «паузой»");
    }

    [Fact]
    public async Task A_Parent_Deferred_By_The_Provider_Limit_Moves_The_Queue()
    {
        // лимит подписки (T-121): старт перенесён, задача в «паузе» — так кончилось T-241
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        var a1 = CreateTask("A1", a.Id);
        Assert.True(await StaysWithoutJobAsync(a1));
        Assign(a1, CreateHuman("a1"));
        Assert.True(await StaysWithoutJobAsync(a1));

        _f.Tasks.SetStartAfter(a.Id, DateTime.UtcNow.AddHours(4));
        _f.Tasks.ChangeStatus(a.Id, TaskStatuses.Paused, null);

        Assert.True(await WaitJobAsync(a1), "подзадача не запущена при переносе старта по лимиту");
        // сама отложенная задача второго задания не получает — её поднимет сторож
        Assert.Equal(1, Jobs(a));
    }

    [Fact]
    public async Task The_Paused_Task_Itself_Is_Not_Restarted()
    {
        // «перезапуска по кругу нет»: пауза двигает очередь, но саму паузу очередь не трогает
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.ChangeStatus(a.Id, TaskStatuses.Paused, null);

        for (var i = 0; i < 5; i++)
        {
            var run = await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);
            Assert.Empty(run.Started);
            Assert.Equal(0, run.Restarted);
        }

        Assert.Equal(1, Jobs(a));
        Assert.Equal(TaskStatuses.Paused, _f.Tasks.Get(a.Id)!.Status);
        Assert.False(HasJob(root)); // корень ждёт: поддерево не улажено
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task Outside_An_Open_Hierarchy_Nothing_Starts()
    {
        // очередь закрыта (кнопку не нажимали) — ни заведение подзадачи, ни «пауза»
        // родителя не должны запускать ничего
        var parent = CreateTask("родитель", executor: CreateHuman("p"));
        var child = CreateTask("подзадача", parent.Id, CreateHuman("c"));
        Assert.True(await StaysWithoutJobAsync(child));

        _f.Tasks.ChangeStatus(parent.Id, TaskStatuses.Paused, null);

        Assert.True(await StaysWithoutJobAsync(child),
            "подзадача запущена без открытой очереди иерархии");
    }

    // ---------- 9–11. правила, которые ломать нельзя ----------

    [Fact]
    public async Task A_Settled_Root_Still_Closes_The_Queue_And_Fresh_Subtasks_Wait_For_A_Human()
    {
        // T-2-S0: очередь ведёт К ЗАПУСКУ КОРНЯ, и сданный корень закрывает её, что бы ни
        // осталось в поддереве. Новые подзадачи этого правила не отменяют
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"), status: TaskStatuses.Review);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        _f.Tasks.ChangeStatus(root.Id, TaskStatuses.Review, null);
        // тот же переход двигает очередь фоном, поэтому закрыть её мог уже он — ждём итога
        for (var i = 0; i < 200 && RunOpen(_f.Tasks.Get(root.Id)!); i++)
        {
            await _f.Orchestrator.ProcessHierarchyAsync(root.Id, null);
            await Task.Delay(10);
        }
        Assert.False(RunOpen(_f.Tasks.Get(root.Id)!));

        var a1 = CreateTask("A1", a.Id, CreateHuman("a1"));

        Assert.True(await StaysWithoutJobAsync(a1));
        Assert.False(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task A_Cancelled_Blocker_No_Longer_Stops_The_Queue()
    {
        // T-299-S0 (было T-6-S1): отменённая блокирующая считается ЗАВЕРШЁННОЙ — очередь не
        // останавливается, а ждавшая подзадача уходит в работу
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var blocker = CreateTask("блокирующая", executor: CreateHuman("b"),
            status: TaskStatuses.Cancelled);
        var a = CreateTask("A", root.Id, CreateHuman("a"), blockers: [blocker.Id]);

        var run = await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        Assert.False(run.Stopped);
        Assert.Contains(a.DisplayId, run.Started);
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task A_Fresh_Subtask_Without_An_Executor_Does_Not_Break_The_Queue()
    {
        // подзадача без исполнителя очередью пропускается (T-224), но очередь при этом
        // обязана остаться открытой: её родитель ещё не улажен
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        var a1 = CreateTask("A1", a.Id); // исполнителя нет
        _f.Orchestrator.AnswerJob(_f.Jobs.ListByTask(a.Id)[0].Id, "готово", "actor");

        Assert.True(await StaysWithoutJobAsync(a1));
        Assert.False(HasJob(root));
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }

    [Fact]
    public async Task A_Subtask_Of_The_Agent_Goes_To_The_Queue_And_Keeps_Its_Deferred_Start()
    {
        // настоящий путь агента: маркер AI2P_SUBTASK → create_task. Проверочная подзадача
        // (T-138) получает отложенный старт УЖЕ ПОСЛЕ заведения, поэтому очередь узнаёт о ней
        // только когда она дописана: иначе проход запустил бы её сразу, не глядя на перенос
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        var tools = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, _f.Tasks.Get(a.Id)!,
                _f.RefData, _f.Picker, null),
            actionCodeByTool: name => name == "create_task" ? "AI2P.Tasks.Create" : null);

        var markers = ClaudeCliConnector.ParseSubtaskMarkers(
            ClaudeCliConnector.SubtaskMarker
            + " {\"title\": \"Обычная подзадача\", \"description\": \"работа\"}\n"
            + ClaudeCliConnector.SubtaskMarker
            + " {\"title\": \"Проверить прогон\", \"description\": \"смотри лог\","
            + " \"startAfterMinutes\": 30}", out _);
        await ClaudeCliConnector.CreateSubtasksAsync(markers, tools, CancellationToken.None);

        var children = _f.Tasks.ListChildren(a.Id);
        var plain = children.Single(c => c.Title == "Обычная подзадача");
        var later = children.Single(c => c.Title == "Проверить прогон");
        Assert.NotNull(TaskService.LaunchStartAfter(_f.Tasks.Get(later.Id)!.LaunchJson));

        // исполнителя подзадачам подбирает сама CreateSubtasksAsync; человека в фикстуре
        // нет только если подбор не нашёл никого — тогда проверять нечего
        if (_f.Tasks.Get(plain.Id)!.ExecutorIds.Count == 1)
        {
            Assert.True(await WaitJobAsync(plain), "подзадача агента не запущена очередью");
        }
        Assert.True(await StaysWithoutJobAsync(later),
            "проверочная подзадача запущена, не дождавшись отложенного старта");
    }

    [Fact]
    public async Task A_Template_Copy_Starts_Bottom_Up_Even_Inside_A_Running_Queue()
    {
        // копия шаблона заводится узел за узлом СВЕРХУ ВНИЗ, а блокирующие связи ставятся
        // после всех: если рассказать очереди о голове сразу, она запустит её первой — то
        // есть родителя раньше подзадач. Поэтому событие поднимается один раз и в конце
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));
        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);

        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "шаблон: голова", IsTemplate = true, ExecutorIds = [CreateHuman("th").Id],
        }, "текст", "", null);
        var leaf = _f.Tasks.Create(new TaskItem
        {
            Title = "шаблон: лист", ParentId = head.Id, IsTemplate = true,
            ExecutorIds = [CreateHuman("tl").Id],
        }, "текст", "", null);

        var copyHead = _f.Tasks.InstantiateTemplate(head.Id, null, null, parentId: a.Id);
        var copyLeaf = _f.Tasks.ListChildren(copyHead.Id).First();
        Assert.Equal(leaf.Title, copyLeaf.Title);

        Assert.True(await WaitJobAsync(copyLeaf), "лист копии шаблона не запущен очередью");
        Assert.True(await StaysWithoutJobAsync(copyHead),
            "голова копии шаблона запущена раньше своей подзадачи");
    }

    [Fact]
    public async Task A_Template_Subtask_Moves_Nothing()
    {
        // копия шаблона в поддереве идущей иерархии — не работа: очередь шаблоны не берёт
        var root = CreateTask("корень", executor: CreateHuman("root"));
        var a = CreateTask("A", root.Id, CreateHuman("a"));

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        var node = _f.Tasks.Create(new TaskItem
        {
            Title = "узел шаблона",
            ParentId = a.Id,
            IsTemplate = true,
            ExecutorIds = [CreateHuman("t").Id],
        }, "текст", "", null);

        Assert.True(await StaysWithoutJobAsync(node));
        Assert.True(RunOpen(_f.Tasks.Get(root.Id)!));
    }
}
