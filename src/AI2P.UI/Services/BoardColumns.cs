using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Чистая логика доски (T-128), вынесенная из <c>BoardView.razor</c>, чтобы её можно было
/// проверить тестами: порядок колонок, перестановка колонки мышкой и выбор карточек,
/// которые сейчас нарисованы.
///
/// Порядок колонок пользователь задаёт перетаскиванием заголовка; он хранится списком кодов
/// состояний в <c>UiState.ComponentState</c> по ключу <c>board.order.&lt;projectId&gt;</c>
/// и попадает в <c>app_state</c> аккаунта. Состояний, которых в сохранённом списке нет
/// (новые встроенные, добавленные пользователем), порядок не ломает — они идут в хвост
/// по порядковому номеру справочника (ТЗ v1.37).
/// </summary>
public static class BoardColumns
{
    /// <summary>Размер пачки карточек колонки: столько рисуется сразу и столько же
    /// добавляется каждый раз, когда пользователь долистал колонку до низа.</summary>
    public const int PageSize = 30;

    /// <summary>Колонки доски в пользовательском порядке; чего нет в сохранённом
    /// порядке — в хвост по <see cref="TaskStatusDef.SortOrder"/>.</summary>
    public static List<TaskStatusDef> Order(
        IEnumerable<TaskStatusDef> statuses, IReadOnlyList<string> saved)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < saved.Count; i++)
        {
            rank.TryAdd(saved[i], i);
        }
        return statuses
            .OrderBy(s => rank.TryGetValue(s.Id, out var i) ? i : int.MaxValue)
            .ThenBy(s => s.SortOrder)
            .ToList();
    }

    /// <summary>
    /// Новый сохраняемый порядок после перетаскивания колонки <paramref name="fromId"/>
    /// на колонку <paramref name="toId"/>: <paramref name="before"/> — встать слева от неё.
    /// <paramref name="shown"/> — колонки в том порядке, в каком они сейчас на экране,
    /// <paramref name="saved"/> — прошлый сохранённый порядок (в нём могут быть состояния,
    /// которых на доске сейчас нет: неактивные и без задач — их порядок не теряем).
    /// Перенос сам на себя или на неизвестную колонку ничего не меняет.
    /// </summary>
    public static List<string> Move(
        IReadOnlyList<string> shown, IReadOnlyList<string> saved,
        string fromId, string toId, bool before)
    {
        var ids = shown.ToList();
        if (fromId == toId || !ids.Contains(toId) || !ids.Remove(fromId))
        {
            return saved.ToList();
        }
        var target = ids.IndexOf(toId);
        ids.Insert(before ? target : target + 1, fromId);
        foreach (var id in saved)
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>
    /// Нарисованные карточки одной колонки: первые <paramref name="limit"/> задач этого
    /// состояния в исходном порядке (сортировка приходит из представления). Счётчик в
    /// заголовке колонки считается по ПОЛНОМУ списку, а не по этому.
    /// </summary>
    public static List<TaskItem> Column(IEnumerable<TaskItem> tasks, string statusId, int limit) =>
        tasks.Where(t => string.Equals(t.Status, statusId, StringComparison.Ordinal))
             .Take(limit)
             .ToList();
}
