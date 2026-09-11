using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-244 «Список подзадач»: во вкладке «Подзадачи» карточки задачи появилась колонка
/// ЧИСЛОВОГО ПРИОРИТЕТА (<c>priority_num</c>).
///
/// Зачем: подзадачи приходят с сервера отсортированными по убыванию числового приоритета
/// (<c>ORDER BY t.priority_num DESC, t.created_at</c>), и именно в этом порядке очередь
/// иерархии (T-159) и автоподбор берут их в работу. До правки порядок строк на экране
/// ничем не объяснялся: приоритет был виден только в карточке самой подзадачи.
///
/// Колонка поставлена ЗА СТАТУСОМ — там же, где она стоит в представлениях «таблица»
/// (<c>TaskTableView</c>) и «тэги» (<c>TaskTagsView</c>), и подписана тем же ключом
/// словаря <c>task.priorityNum</c>, поэтому оба языка получают её сразу.
///
/// Разметка проверяется по файлу (приём T-209/T-217/T-222), порядок и значение —
/// по-настоящему через хранилище; вид в браузере — живой проверкой <c>test/t244</c>.
/// </summary>
public sealed class T244Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. данные, которые показывает колонка ----------

    /// <summary>Список подзадач набирается тем же серверным списком по проекту, что и
    /// вкладка карточки, и уже несёт числовой приоритет каждой строки.</summary>
    [Fact]
    public void The_Subtask_List_Carries_The_Numeric_Priority()
    {
        var project = _f.Projects.Create("T-244", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "", "", null);
        _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            ParentId = parent.Id,
            Title = "Подзадача",
            PriorityNum = 37,
        }, "", "", null);

        var subtasks = _f.Tasks.List(project.Id, includeTemplates: true)
            .Where(t => t.ParentId == parent.Id)
            .ToList();

        Assert.Equal(37, Assert.Single(subtasks).PriorityNum);
    }

    /// <summary>Без указания приоритет — «обычный» середины диапазона: в колонке не должно
    /// быть пусто у задачи, которую заводили, не трогая поле.</summary>
    [Fact]
    public void A_Subtask_Without_A_Priority_Gets_The_Default()
    {
        var project = _f.Projects.Create("T-244 умолчание", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            ParentId = parent.Id,
            Title = "Подзадача",
        }, "", "", null);

        Assert.Equal(TaskPriority.DefaultMedium, _f.Tasks.Get(child.Id)!.PriorityNum);
    }

    /// <summary>Порядок строк, который колонка и объясняет: важнее — выше, при равенстве
    /// приоритетов — по времени создания.</summary>
    [Fact]
    public void Subtasks_Are_Ordered_By_The_Numeric_Priority_Descending()
    {
        var project = _f.Projects.Create("T-244 порядок", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" }, "", "", null);
        foreach (var (title, prio) in new[] { ("низкая", 5), ("высокая", 30), ("обычная", 15) })
        {
            _f.Tasks.Create(new TaskItem
            {
                ProjectId = project.Id,
                ParentId = parent.Id,
                Title = title,
                PriorityNum = prio,
            }, "", "", null);
        }

        var titles = _f.Tasks.List(project.Id, includeTemplates: true)
            .Where(t => t.ParentId == parent.Id)
            .Select(t => t.Title)
            .ToList();

        Assert.Equal(["высокая", "обычная", "низкая"], titles);
    }

    // ---------- 2. разметка вкладки ----------

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string Ui(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "AI2P_app", "src", "AI2P.UI", .. parts]));

    /// <summary>Отрезок файла с таблицей подзадач: от вкладки «Подзадачи» до её конца.</summary>
    private static string SubtaskTable()
    {
        var card = Ui("Components", "TaskCardView.razor");
        var from = card.IndexOf("L[\"task.card.subtasks\"]", StringComparison.Ordinal);
        Assert.True(from > 0, "во вкладках карточки нет «Подзадачи»");
        var to = card.IndexOf("</MudTabPanel>", from, StringComparison.Ordinal);
        Assert.True(to > from, "вкладка «Подзадачи» не закрыта");
        return card[from..to];
    }

    /// <summary>В шапке списка подзадач есть колонка числового приоритета, и подписана она
    /// общим ключом словаря — значит, приходит на обоих языках.</summary>
    [Fact]
    public void The_Subtask_Table_Has_A_Numeric_Priority_Column()
    {
        var table = SubtaskTable();

        Assert.Contains("<th style=\"white-space: nowrap;\">@L[\"task.priorityNum\"]</th>", table);
        Assert.Contains("data-subtask-prio=\"@subtask.PriorityNum\"", table);
        Assert.Contains(">@subtask.PriorityNum</td>", table);
    }

    /// <summary>Колонка стоит между статусом и исполнителем — так же, как в представлениях
    /// «таблица» и «тэги»; и в шапке, и в строке одинаково (иначе данные съедут).</summary>
    [Fact]
    public void The_Column_Stands_After_The_Status_In_The_Head_And_In_The_Row()
    {
        var table = SubtaskTable();

        var headStatus = table.IndexOf("<th>@L[\"task.status\"]</th>", StringComparison.Ordinal);
        var headPrio = table.IndexOf("@L[\"task.priorityNum\"]", StringComparison.Ordinal);
        var headExecutor = table.IndexOf("<th>@L[\"task.executor\"]</th>", StringComparison.Ordinal);
        Assert.True(headStatus > 0 && headPrio > headStatus && headExecutor > headPrio,
            "в шапке приоритет не между статусом и исполнителем");

        var rowStatus = table.IndexOf("State.StatusName(subtask.Status)", StringComparison.Ordinal);
        var rowPrio = table.IndexOf("data-subtask-prio", StringComparison.Ordinal);
        var rowExecutor = table.IndexOf("@Nick(subtask.ExecutorIds", StringComparison.Ordinal);
        Assert.True(rowStatus > 0 && rowPrio > rowStatus && rowExecutor > rowPrio,
            "в строке приоритет не между статусом и исполнителем");
    }

    /// <summary>Ячеек в строке ровно столько же, сколько колонок в шапке.</summary>
    [Fact]
    public void The_Row_Has_As_Many_Cells_As_The_Head_Has_Columns()
    {
        var table = SubtaskTable();
        // отсчёт от КОНЦА «<thead>»: сам тэг тоже начинается с «<th» и попал бы в счёт
        var head = table[(table.IndexOf("<thead>", StringComparison.Ordinal) + "<thead>".Length)
                         ..table.IndexOf("</thead>", StringComparison.Ordinal)];
        var body = table[table.IndexOf("<tbody>", StringComparison.Ordinal)..table.IndexOf("</tbody>", StringComparison.Ordinal)];

        Assert.Equal(Count(head, "<th"), Count(body, "<td"));
        static int Count(string text, string needle) =>
            text.Split(needle, StringSplitOptions.None).Length - 1;
    }

    /// <summary>Подпись колонки есть в словарях ОБОИХ языков (правило T-180): без неё на
    /// экран уехал бы сам ключ.</summary>
    [Fact]
    public void The_Column_Title_Is_In_Both_Dictionaries()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = AI2P.Core.Loc.In(lang, "task.priorityNum");
            Assert.False(string.IsNullOrWhiteSpace(text), $"нет подписи для {lang}");
            Assert.NotEqual("task.priorityNum", text);
        }
    }
}
