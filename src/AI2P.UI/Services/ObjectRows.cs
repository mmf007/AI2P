using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Порядок строк объектов (T-259): дети идут сразу за своим родителем и с отступом.
///
/// Раскладка ОДНА на два представления: ТАБЛИЦУ (иерархия видна отступом и порядком) и
/// ИЕРАРХИЮ (T-266 — та же раскладка, но узлы сворачиваются и строку можно перетащить).
/// Плоский список по номеру растащил бы кадры персонажа по всей таблице: OBJ-3 «Герой»,
/// OBJ-9 его кадр, а между ними чужая локация.
///
/// Сирота (родитель удалён или лежит вне выборки — например, отсеян фильтром по тэгам)
/// НЕ ТЕРЯЕТСЯ: он выводится на верхнем уровне. Потерять строку из-за фильтра было бы хуже
/// всего — список объектов оказался бы короче, чем список тех же объектов в другом виде.
/// </summary>
public static class ObjectRows
{
    /// <summary>Строка списка: объект, его уровень вложенности (0 — верхний) и признак
    /// «внутри есть свои объекты» — по нему представление «иерархия» рисует стрелку
    /// сворачивания (у таблицы стрелок нет, и признак ей просто не нужен).</summary>
    public sealed record Row(ObjectItem Item, int Level, bool HasChildren = false);

    /// <summary>
    /// Разложить объекты деревом. Порядок внутри уровня — по номеру ЧИСЛОМ
    /// (<see cref="DisplayIds.Order"/>), то есть тот же, в котором их отдаёт сервер.
    /// </summary>
    /// <param name="collapsed">Свёрнутые узлы (T-266): их дети в список не попадают.
    /// null — развёрнуто всё, как в таблице.</param>
    public static List<Row> Build(IEnumerable<ObjectItem> items,
        IReadOnlySet<string>? collapsed = null)
    {
        var all = items.ToList();
        var known = all.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var byParent = new Dictionary<string, List<ObjectItem>>(StringComparer.Ordinal);
        var roots = new List<ObjectItem>();
        foreach (var item in all)
        {
            // родитель вне выборки — объект показывается корневым, а не пропадает
            if (item.ParentId is { Length: > 0 } parent && known.Contains(parent))
            {
                if (!byParent.TryGetValue(parent, out var list))
                {
                    byParent[parent] = list = [];
                }
                list.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }

        var rows = new List<Row>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        // hidden — «мы внутри свёрнутого узла»: строка не рисуется, но обход продолжается.
        // Без этого дети свёрнутого узла остались бы непосещёнными и хвост «строк, не
        // попавших в обход» вынес бы их на верхний уровень — свёртывание показывало бы
        // больше строк, чем разворачивание
        void Walk(ObjectItem item, int level, bool hidden)
        {
            // защита от цикла: хранилище его не пропускает (ObjectService.EnsureParent),
            // но список рисуется и по данным, приехавшим репликацией со старой версии
            if (!visited.Add(item.Id))
            {
                return;
            }
            var hasChildren = byParent.ContainsKey(item.Id);
            if (!hidden)
            {
                rows.Add(new Row(item, level, hasChildren));
            }
            if (!byParent.TryGetValue(item.Id, out var children))
            {
                return;
            }
            var childrenHidden = hidden || (collapsed?.Contains(item.Id) ?? false);
            foreach (var child in children.OrderBy(c => DisplayIds.Order(c.DisplayId)))
            {
                Walk(child, level + 1, childrenHidden);
            }
        }

        foreach (var root in roots.OrderBy(r => DisplayIds.Order(r.DisplayId)))
        {
            Walk(root, 0, false);
        }
        // строка, не попавшая в обход (замкнутый цикл), всё равно показывается: список
        // обязан быть не короче выборки
        foreach (var item in all.Where(o => !visited.Contains(o.Id)))
        {
            rows.Add(new Row(item, 0));
        }
        return rows;
    }
}
