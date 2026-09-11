using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Варианты сортировки списка задач и ИЕРАРХИИ (ТЗ v1.17), вынесенные из
/// <c>TasksView.razor</c> и <c>TemplatesView.razor</c>, чтобы правило порядка жило в одном
/// месте и проверялось тестами (как <see cref="BoardColumns"/> и <see cref="ModelSkillGroups"/>).
///
/// До T-223 один и тот же <c>switch</c> был написан в обоих представлениях дважды: новый
/// вариант приходилось добавлять в двух файлах, и разойтись они могли молча.
///
/// В иерархии сортировка применяется только к самому верхнему уровню (<c>TaskTreeView.RootSort</c>):
/// подзадачи всегда идут под своим родителем, иначе дерево перестанет быть деревом.
/// </summary>
public static class TaskSort
{
    /// <summary>
    /// Ключи вариантов в порядке показа в меню. Подписи берутся из словаря по ключу
    /// <c>tasks.sort.&lt;ключ&gt;</c>; «без сортировки» (null) в список не входит — оно стоит
    /// отдельным пунктом за разделителем.
    /// </summary>
    public static readonly string[] Keys =
    [
        "priority_asc", "priority_desc", "prionum_asc", "prionum_desc", "due_asc", "due_desc",
        "executor", "created_asc", "created_desc", "updated_asc", "updated_desc", "title",
    ];

    /// <summary>
    /// Компаратор выбранного варианта; null — неизвестный ключ или «без сортировки»,
    /// то есть порядок, в котором список пришёл с сервера.
    /// </summary>
    /// <param name="key">Ключ варианта из <see cref="Keys"/>.</param>
    /// <param name="nick">Ник первого исполнителя задачи — знает только UI (справочник
    /// исполнителей лежит в состоянии страницы), поэтому передаётся снаружи.</param>
    public static Comparison<TaskItem>? Comparison(string? key, Func<TaskItem, string> nick)
    {
        // задачи без срока — в конце при любом направлении
        static DateTime Due(TaskItem t, bool desc) =>
            t.DueDate ?? (desc ? DateTime.MinValue : DateTime.MaxValue);
        return key switch
        {
            "priority_asc" => (a, b) => a.Priority.CompareTo(b.Priority),
            "priority_desc" => (a, b) => b.Priority.CompareTo(a.Priority),
            "prionum_asc" => (a, b) => a.PriorityNum.CompareTo(b.PriorityNum),
            "prionum_desc" => (a, b) => b.PriorityNum.CompareTo(a.PriorityNum),
            "due_asc" => (a, b) => Due(a, false).CompareTo(Due(b, false)),
            "due_desc" => (a, b) => Due(b, true).CompareTo(Due(a, true)),
            "executor" => (a, b) => string.Compare(nick(a), nick(b), StringComparison.CurrentCultureIgnoreCase),
            "created_asc" => (a, b) => a.CreatedAt.CompareTo(b.CreatedAt),
            "created_desc" => (a, b) => b.CreatedAt.CompareTo(a.CreatedAt),
            // время изменения самой задачи (tasks.updated_at, T-223): его двигает любая правка
            // задачи, включая смену статуса и переносы по иерархии. Правка ПОДЗАДАЧИ время
            // родителя не двигает — сортируется ровно то, что показано в строке
            "updated_asc" => (a, b) => a.UpdatedAt.CompareTo(b.UpdatedAt),
            "updated_desc" => (a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt),
            "title" => (a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase),
            _ => null,
        };
    }

    /// <summary>Копия списка в выбранном порядке; при «без сортировки» — тот же список.</summary>
    public static List<TaskItem> Sorted(List<TaskItem> tasks, string? key, Func<TaskItem, string> nick)
    {
        var comparison = Comparison(key, nick);
        if (comparison is null)
        {
            return tasks;
        }
        var sorted = new List<TaskItem>(tasks);
        sorted.Sort(comparison);
        return sorted;
    }
}
