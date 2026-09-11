using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Список кандидатов в БЛОКИРУЮЩИЕ ЗАДАЧИ для формы задачи (T-248), вынесенный из
/// <c>TaskDialog.razor</c>, чтобы правило отбора проверялось тестами — как
/// <see cref="BoardColumns"/> и <see cref="TaskSort"/>.
///
/// Правило целиком: показываем задачи ПРОЕКТА задачи (отбор делает сервер — параметр
/// <c>projectId</c>), кроме самой задачи и кроме тех, работа по которым уже кончилась
/// («готово» и «отменена»), в порядке НОМЕРА задачи. Поиск — один на заголовок и описание,
/// его тоже делает сервер (<c>search</c>): описание лежит в файле .md, и в браузере его нет.
///
/// ВАЖНОЕ ИСКЛЮЧЕНИЕ: уже выбранная блокирующая остаётся в списке всегда — и завершённая,
/// и отброшенная поиском. Иначе у <c>MudSelect</c> пропадает пункт выбранного значения:
/// снять его стало бы нечем, а в поле вместо кода задачи показался бы GUID.
/// </summary>
public static class BlockerCandidates
{
    /// <summary>
    /// Пункты выпадающего списка блокирующих задач.
    /// </summary>
    /// <param name="loaded">Что пришло с сервера последним запросом: задачи проекта,
    /// при непустом поиске — только подошедшие ему.</param>
    /// <param name="chosen">Уже выбранные блокирующие задачи (те из них, что форма знает
    /// по прежним ответам сервера): в списке остаются при любом отборе.</param>
    /// <param name="selfId">Идентификатор правимой задачи: сама себя блокировать не может
    /// (хранилище такую ссылку и не примет).</param>
    public static List<TaskItem> Options(
        IEnumerable<TaskItem> loaded, IEnumerable<TaskItem> chosen, string? selfId)
    {
        var chosenIds = new HashSet<string>(chosen.Select(t => t.Id), StringComparer.Ordinal);
        var options = new List<TaskItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in loaded)
        {
            // завершённая блокирующая ничего не держит (ТЗ п. 2.12), а отменённая держит
            // навсегда (T-6-S1) — предлагать в список ни ту, ни другую незачем
            if (task.Id == selfId || (TaskStatuses.Finished(task.Status) && !chosenIds.Contains(task.Id)))
            {
                continue;
            }
            if (seen.Add(task.Id))
            {
                options.Add(task);
            }
        }
        foreach (var task in chosen)
        {
            if (task.Id != selfId && seen.Add(task.Id))
            {
                options.Add(task);
            }
        }
        // порядок — по номеру задачи (T-2 раньше T-10); одинаковых номеров не бывает,
        // но при пустом номере порядок должен оставаться устойчивым
        return options
            .OrderBy(t => DisplayIds.Order(t.DisplayId))
            .ThenBy(t => t.DisplayId, StringComparer.Ordinal)
            .ToList();
    }
}
