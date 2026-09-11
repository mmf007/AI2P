using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Чистая логика представления «Тэги» списка ОБЪЕКТОВ проекта (T-259) — вынесена из разметки,
/// чтобы её проверяли тесты, как <see cref="TaskTagGroups"/> у задач.
///
/// Правила те же, что у задач, и это намеренно: объект с несколькими тэгами попадает в
/// КАЖДУЮ свою категорию (тэги — ключевые слова, а не взаимоисключающие корзины), объект
/// без тэгов уходит в категорию <see cref="NoTag"/> в конец списка — иначе список в
/// представлении «тэги» был бы короче остальных и это читалось бы как потеря объектов.
///
/// Своей копией, а не общей функцией с задачами, это сделано по той же причине, по которой
/// у объектов ОТДЕЛЬНАЯ группа тэгов: списки разные, и общий обобщённый код связал бы их
/// первой же правкой одного из них.
/// </summary>
public static class ObjectTagGroups
{
    /// <summary>Код категории «без тэгов»: у тэга код непустой, поэтому пустая строка свободна.</summary>
    public const string NoTag = "";

    /// <summary>Категория представления: тэг и объекты с ним.</summary>
    public sealed record Group(string Tag, IReadOnlyList<ObjectItem> Items);

    /// <summary>
    /// Разложить объекты по тэгам. Категории — по алфавиту без учёта регистра («без тэгов»
    /// в конец), внутри категории порядок объектов сохраняется тем, в котором они пришли:
    /// это порядок выборки (по номеру), и пересортировывать его здесь второй раз нельзя.
    /// </summary>
    public static List<Group> Build(IEnumerable<ObjectItem> items)
    {
        var byTag = new Dictionary<string, List<ObjectItem>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var tags = item.Tags.Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
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
                list.Add(item);
            }
        }

        return byTag
            .OrderBy(g => g.Key.Length == 0)                       // «без тэгов» — последней
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new Group(g.Key, g.Value))
            .ToList();
    }
}
