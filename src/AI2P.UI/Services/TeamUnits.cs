using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Оргединицы команды в списке команд (T-629-S0): обход деревом, подпись, проверка
/// «внутри поддерева». Общие для формы единицы и обоих представлений TeamsView.
/// </summary>
public static class TeamUnits
{
    /// <summary>Единицы деревом: обход в глубину с уровнем вложенности. Единица с
    /// вышестоящей вне набора считается верхним уровнем; цикл в данных не зацикливает.</summary>
    public static List<(TeamUnit Unit, int Depth)> Ordered(IReadOnlyList<TeamUnit> units)
    {
        var ids = units.Select(u => u.Id).ToHashSet();
        var byParent = units.Where(u => u.ParentUnitId is { } p && ids.Contains(p))
            .ToLookup(u => u.ParentUnitId!);
        var result = new List<(TeamUnit, int)>();
        var seen = new HashSet<string>();
        void Walk(IEnumerable<TeamUnit> level, int depth)
        {
            foreach (var unit in level.OrderBy(u => u.Sort).ThenBy(u => u.Name))
            {
                if (seen.Add(unit.Id))
                {
                    result.Add((unit, depth));
                    Walk(byParent[unit.Id], depth + 1);
                }
            }
        }
        Walk(units.Where(u => u.ParentUnitId is null || !ids.Contains(u.ParentUnitId)), 0);
        return result;
    }

    /// <summary>Лежит ли <paramref name="unit"/> внутри единицы <paramref name="ancestorId"/>
    /// (или совпадает с ней).</summary>
    public static bool IsInside(IReadOnlyList<TeamUnit> units, TeamUnit unit, string ancestorId)
    {
        var seen = new HashSet<string>();
        for (var up = unit; up is not null && seen.Add(up.Id);
             up = units.FirstOrDefault(u => u.Id == up.ParentUnitId))
        {
            if (up.Id == ancestorId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Наименование единицы, а без него — вид; с исполнителем в скобках.</summary>
    public static string Title(TeamUnit unit, UiState state, I18nService l)
    {
        var name = unit.Name.Trim().Length > 0 ? unit.Name.Trim() : l["teams.unitKind." + unit.Kind];
        return unit.ExecutorId is { } e
            ? $"{name} ({state.Executors.FirstOrDefault(x => x.Id == e)?.Nick ?? e})"
            : name;
    }
}
