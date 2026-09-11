using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-248 (версия 1.89): ВЫПАДАЮЩИЙ СПИСОК БЛОКИРУЮЩИХ ЗАДАЧ В ФОРМЕ ЗАДАЧИ.
///
/// Что изменилось:
/// 1. рядом со списком — ОДИН поиск на заголовок и описание (его выполняет сервер:
///    описание задачи лежит файлом .md, в браузере его нет);
/// 2. задачи в состояниях «готово» и «отменена» в список не попадают — блокировать
///    задачу тем, по чему работа уже кончилась, незачем;
/// 3. порядок — по НОМЕРУ задачи (T-2 раньше T-10), а не по приоритету;
/// 4. кандидаты — только задачи ПРОЕКТА задачи (так было и раньше; теперь список
///    перечитывается и при смене проекта прямо в форме).
///
/// Здесь проверяется чистая часть (<see cref="BlockerCandidates"/>, <see cref="DisplayIds"/>)
/// и контракт хранилища, на котором держится поиск; разметка формы — по файлу
/// (приём <see cref="T209Tests"/>), поведение в браузере — живой проверкой <c>test/t248</c>.
/// </summary>
public sealed class T248Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static TaskItem Task(string displayId, string status = TaskStatuses.Pending) =>
        new() { Id = "id-" + displayId, DisplayId = displayId, Title = displayId, Status = status };

    // ---------- 1. порядок номеров (общее правило ядра) ----------

    /// <summary>Номер читается как ЧИСЛО: T-2 раньше T-10 (строковый порядок дал бы обратное).</summary>
    [Fact]
    public void Display_Id_Is_Ordered_As_A_Number()
    {
        Assert.True(DisplayIds.Order("T-2") < DisplayIds.Order("T-10"));
        Assert.True(DisplayIds.Order("T-9") < DisplayIds.Order("T-100"));
    }

    /// <summary>Суффикс кода сервера (ТЗ гл. 6) на порядок не влияет: считает счётчик.</summary>
    [Fact]
    public void A_Server_Suffix_Does_Not_Change_The_Order()
    {
        Assert.Equal(DisplayIds.Order("T-18"), DisplayIds.Order("T-18-S1"));
        Assert.True(DisplayIds.Order("T-18-S1") < DisplayIds.Order("T-19"));
    }

    /// <summary>Номера нет или он чужого вида — запись уходит в конец списка, а не роняет его.</summary>
    [Fact]
    public void An_Unknown_Number_Goes_To_The_End()
    {
        Assert.Equal(long.MaxValue, DisplayIds.Order(""));
        Assert.Equal(long.MaxValue, DisplayIds.Order(null));
        Assert.Equal(long.MaxValue, DisplayIds.Order("T-x"));
    }

    // ---------- 2. отбор кандидатов в блокирующие ----------

    /// <summary>Завершённая и отменённая задачи в список не предлагаются.</summary>
    [Fact]
    public void Finished_Tasks_Are_Not_Offered()
    {
        var options = BlockerCandidates.Options(
            [Task("T-1"), Task("T-2", TaskStatuses.Done), Task("T-3", TaskStatuses.Cancelled),
             Task("T-4", TaskStatuses.InProgress)],
            [], "id-self");

        Assert.Equal(["T-1", "T-4"], options.Select(t => t.DisplayId));
    }

    /// <summary>Сама задача себя блокировать не может — её в списке нет.</summary>
    [Fact]
    public void The_Task_Itself_Is_Not_Offered()
    {
        var options = BlockerCandidates.Options([Task("T-1"), Task("T-2")], [], "id-T-1");

        Assert.Equal(["T-2"], options.Select(t => t.DisplayId));
    }

    /// <summary>Порядок — по номеру задачи, а не в том, в каком список пришёл с сервера
    /// (сервер отдаёт по приоритету и времени создания).</summary>
    [Fact]
    public void Options_Are_Sorted_By_Task_Number()
    {
        var options = BlockerCandidates.Options(
            [Task("T-10"), Task("T-2"), Task("T-33-S1"), Task("T-7")], [], null);

        Assert.Equal(["T-2", "T-7", "T-10", "T-33-S1"], options.Select(t => t.DisplayId));
    }

    /// <summary>
    /// УЖЕ ВЫБРАННАЯ блокирующая остаётся пунктом списка, даже если успела завершиться:
    /// пропади пункт — снять её было бы нечем, а в поле вместо кода задачи показался бы GUID.
    /// </summary>
    [Fact]
    public void A_Chosen_Blocker_Stays_In_The_List_Even_When_Finished()
    {
        var done = Task("T-5", TaskStatuses.Done);

        var options = BlockerCandidates.Options([Task("T-1"), done], [done], null);

        Assert.Equal(["T-1", "T-5"], options.Select(t => t.DisplayId));
    }

    /// <summary>Выбранная блокирующая, которую отбросил поиск (её нет в ответе сервера),
    /// тоже остаётся в списке — и ровно одним пунктом.</summary>
    [Fact]
    public void A_Chosen_Blocker_Survives_The_Search_And_Is_Not_Duplicated()
    {
        var chosen = Task("T-5");

        var filteredOut = BlockerCandidates.Options([Task("T-1")], [chosen], null);
        var stillThere = BlockerCandidates.Options([Task("T-1"), chosen], [chosen], null);

        Assert.Equal(["T-1", "T-5"], filteredOut.Select(t => t.DisplayId));
        Assert.Equal(["T-1", "T-5"], stillThere.Select(t => t.DisplayId));
    }

    // ---------- 3. чем поиск и отбор по проекту обеспечены в хранилище ----------

    /// <summary>ОДИН поиск на заголовок И описание: форма передаёт серверу параметр
    /// <c>search</c>, а описание живёт файлом .md — в браузере его нет вовсе.</summary>
    [Fact]
    public void One_Search_Covers_Both_Title_And_Description()
    {
        var project = _f.Projects.Create("T248", null, null, null);
        _f.Tasks.Create(new TaskItem { Title = "рыба", ProjectId = project.Id }, "прочее", "", null);
        _f.Tasks.Create(new TaskItem { Title = "прочее", ProjectId = project.Id }, "рыба в описании", "", null);
        _f.Tasks.Create(new TaskItem { Title = "мимо", ProjectId = project.Id }, "мимо", "", null);

        var found = _f.Tasks.List(projectId: project.Id, search: "рыба");

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, t => t.Title == "мимо");
    }

    /// <summary>Кандидаты берутся из ПРОЕКТА задачи: задачи чужого проекта не приезжают
    /// вовсе — отбор делает сервер, а не браузер.</summary>
    [Fact]
    public void Candidates_Come_From_The_Project_Of_The_Task()
    {
        var mine = _f.Projects.Create("T248 свой", null, null, null);
        var other = _f.Projects.Create("T248 чужой", null, null, null);
        _f.Tasks.Create(new TaskItem { Title = "своя", ProjectId = mine.Id }, "", "", null);
        _f.Tasks.Create(new TaskItem { Title = "чужая", ProjectId = other.Id }, "", "", null);

        var found = _f.Tasks.List(projectId: mine.Id);

        Assert.Equal(["своя"], found.Select(t => t.Title));
    }

    // ---------- 4. разметка формы ----------

    /// <summary>Поле поиска стоит РЯДОМ со списком блокирующих и помечено под живую
    /// проверку; список рисуется отобранными кандидатами, а не сырым ответом сервера.</summary>
    [Fact]
    public void The_Form_Has_A_Search_Field_Next_To_The_Blockers_Select()
    {
        var razor = File.ReadAllText(SourceFile("src/AI2P.UI/Components/TaskDialog.razor"));

        Assert.Contains("data-blocker-search=\"1\"", razor);
        Assert.Contains("data-task-blockers=\"1\"", razor);
        Assert.Contains("in BlockerOptions", razor);
        Assert.Contains("ReloadBlockersAsync", razor);
    }

    /// <summary>
    /// Выбранная блокирующая из ЧУЖОГО проекта среди кандидатов не приезжает (кандидаты —
    /// задачи проекта задачи), поэтому её карточка дотягивается отдельным запросом: без него
    /// пункта списка у неё нет вовсе — в поле показался бы GUID, а снять её было бы нечем.
    /// Ссылка законна: одного проекта у блокирующих хранилище не требует.
    /// </summary>
    [Fact]
    public void A_Blocker_From_Another_Project_Is_Allowed_And_Is_Fetched_For_The_List()
    {
        var mine = _f.Projects.Create("T248 проект задачи", null, null, null);
        var other = _f.Projects.Create("T248 проект блокирующей", null, null, null);
        var blocker = _f.Tasks.Create(new TaskItem { Title = "чужая", ProjectId = other.Id }, "", "", null);
        var task = _f.Tasks.Create(
            new TaskItem { Title = "своя", ProjectId = mine.Id, BlockerIds = [blocker.Id] },
            "", "", null);

        // хранилище такую ссылку принимает, а в кандидатах (задачи проекта) её нет
        Assert.Equal([blocker.Id], _f.Tasks.Get(task.Id)!.BlockerIds);
        Assert.DoesNotContain(_f.Tasks.List(projectId: mine.Id), t => t.Id == blocker.Id);

        var razor = File.ReadAllText(SourceFile("src/AI2P.UI/Components/TaskDialog.razor"));
        Assert.Contains("ResolveChosenBlockersAsync", razor);
    }

    /// <summary>
    /// Поле блокирующих рисуется ТОЛЬКО после загрузки кандидатов (<c>_blockersLoaded</c>).
    /// Подпись выбранного значения <c>MudSelect</c> считает при ПЕРВОЙ отрисовке и больше не
    /// пересчитывает: нарисованное раньше ответа сервера, поле навсегда показывало бы GUID
    /// вместо кода задачи (поймано живой проверкой T-248).
    /// </summary>
    [Fact]
    public void The_Blockers_Field_Is_Drawn_Only_After_The_Candidates_Are_Loaded()
    {
        var razor = File.ReadAllText(SourceFile("src/AI2P.UI/Components/TaskDialog.razor"));

        Assert.Contains("@if (_blockersLoaded)", razor);
        Assert.Contains("_blockersLoaded = true;", razor);
        // флаг ставится ПОСЛЕ обоих запросов — иначе поле опять нарисуется раньше данных
        var flag = razor.IndexOf("_blockersLoaded = true;", StringComparison.Ordinal);
        var resolve = razor.IndexOf("await ResolveChosenBlockersAsync();", StringComparison.Ordinal);
        Assert.True(resolve > 0 && flag > resolve);
    }

    /// <summary>Разбор номера живёт в одном месте (<see cref="DisplayIds"/>): своих копий
    /// в представлениях больше нет — до T-248 их было четыре.</summary>
    [Fact]
    public void The_Number_Parsing_Is_Not_Copied_Into_Views()
    {
        var views = new[]
        {
            "src/AI2P.UI/Components/TaskTableView.razor",
            "src/AI2P.UI/Components/AiWorkView.razor",
            "src/AI2P.UI/Components/ExecutorsView.razor",
            "src/AI2P.UI/Components/ScheduleView.razor",
        };

        foreach (var view in views)
        {
            var razor = File.ReadAllText(SourceFile(view));
            Assert.DoesNotContain("private static long DisplayIdOrder", razor);
            Assert.Contains("DisplayIds.Order(", razor);
        }
    }

    /// <summary>Путь к файлу исходников от каталога сборки тестов: над каталогом AI2P_app
    /// лежит AI2P.sln (приём T-209).</summary>
    private static string SourceFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }
}
