using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Чистая логика представления «Тэги» списка задач (T-222), вынесенная из разметки, чтобы
/// её можно было проверить тестами (как <see cref="BoardColumns"/> и <see cref="ModelSkillGroups"/>).
///
/// Категория — ТЭГ, внутри категории идёт обычный список задач (то же содержимое, что
/// в представлении «таблица»). Задача с несколькими тэгами попадает в КАЖДУЮ свою категорию:
/// тэги — это ключевые слова, а не взаимоисключающие корзины, и «покажи всё про UI» должно
/// показывать задачу и там, и в категории «сборка».
///
/// Задача без тэгов не теряется — она уходит в категорию <see cref="NoTag"/>, которая всегда
/// стоит последней: иначе список задач в представлении «тэги» был бы короче, чем в остальных,
/// и это выглядело бы как потеря задач.
/// </summary>
public static class TaskTagGroups
{
    /// <summary>Код категории «без тэгов»: у тэга код непустой, поэтому пустая строка свободна.</summary>
    public const string NoTag = "";

    /// <summary>Категория представления: тэг и задачи с ним.</summary>
    public sealed record Group(string Tag, IReadOnlyList<TaskItem> Tasks);

    /// <summary>
    /// Разложить задачи по тэгам. Категории — по алфавиту без учёта регистра («без тэгов» —
    /// в конец), внутри категории порядок задач сохраняется тем, в котором они пришли:
    /// это порядок выборки и выбранной сортировки, и второй раз пересортировывать его здесь
    /// нельзя — иначе выбор сортировки в тулбаре перестал бы действовать на это представление.
    ///
    /// Два написания одного тэга («UI» и «ui») в одну категорию НЕ сводятся: тэг хранится так,
    /// как его написали, и склейка показывала бы задачу под чужим написанием. Сравнение
    /// категорий — точное, а от появления таких пар защищает поле формы (там тэг выбирают
    /// из готового списка) и <c>TaskService.NormalizeTags</c> (в пределах одной задачи).
    /// </summary>
    public static List<Group> Build(IEnumerable<TaskItem> tasks)
    {
        var byTag = new Dictionary<string, List<TaskItem>>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            var tags = task.Tags.Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            if (tags.Count == 0)
            {
                tags = [NoTag];
            }
            foreach (var tag in tags)
            {
                if (!byTag.TryGetValue(tag, out var list))
                {
                    byTag[tag] = list = [];
                }
                list.Add(task);
            }
        }

        return byTag
            .OrderBy(g => g.Key.Length == 0)                       // «без тэгов» — последней
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new Group(g.Key, g.Value))
            .ToList();
    }
}
