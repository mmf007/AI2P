using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// СПИСОК ШАБЛОНОВ В ФОРМЕ НОВОЙ ЗАДАЧИ (ТЗ гл. 5, T-157).
///
/// Шаблон принадлежит проекту либо не принадлежит никакому — тогда он ОБЩИЙ и годится
/// всюду. Форма новой задачи показывала подряд все шаблоны организации, поэтому в проекте
/// с десятком чужих шаблонов свой приходилось искать глазами, а создать задачу по шаблону
/// СОСЕДНЕГО проекта можно было случайно, одним промахом мыши.
///
/// Порядок такой: сначала шаблоны ЭТОГО проекта, потом общие, чужих нет вовсе. «Этот» —
/// проект формы (вкладка «задачи» карточки проекта из закладки «Проекты»), а не текущий
/// проект пользователя: в карточке проекта задача заводится в неё, что бы ни было выбрано
/// текущим. Если проекта у формы нет (общий список задач), скрывать нечего — показываются
/// все шаблоны, общие сверху.
/// </summary>
public static class TaskTemplates
{
    /// <summary>
    /// Шаблоны верхнего уровня для формы новой задачи: только свои проекту и общие,
    /// свои — первыми, внутри группы — по заголовку.
    /// </summary>
    /// <param name="templates">Шаблоны организации (узлы любого уровня).</param>
    /// <param name="projectId">Проект формы; null — форма без проекта (общий список).</param>
    public static List<TaskItem> ForProject(IEnumerable<TaskItem>? templates, string? projectId)
    {
        var project = Norm(projectId);
        return (templates ?? [])
            // только головы: иерархия копируется целиком от головы шаблона
            .Where(t => t.ParentId is null)
            // чужой проект — вон из списка; при форме без проекта чужих нет
            .Where(t => project is null || Norm(t.ProjectId) is null || Norm(t.ProjectId) == project)
            .OrderBy(t => Norm(t.ProjectId) == project ? 0 : 1)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Пустая строка приходит из форм наравне с null и значит то же самое.</summary>
    private static string? Norm(string? id) => string.IsNullOrWhiteSpace(id) ? null : id;
}
