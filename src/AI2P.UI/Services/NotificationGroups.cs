using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>Представления списка уведомлений (T-272) — значение переключателя «Вид».</summary>
public static class NotificationViews
{
    /// <summary>Таблица: все правила подряд, как в остальных справочниках.</summary>
    public const string Table = "table";

    /// <summary>Категории «проект / событие».</summary>
    public const string ProjectEvent = "project-event";

    /// <summary>Категории «проект / исполнитель».</summary>
    public const string ProjectExecutor = "project-executor";

    /// <summary>Категории «исполнитель / проект».</summary>
    public const string ExecutorProject = "executor-project";

    public static readonly string[] All = [Table, ProjectEvent, ProjectExecutor, ExecutorProject];

    /// <summary>Известный код представления; мусор из сохранённого состояния — таблица.</summary>
    public static string Normalize(string? view) =>
        view is { Length: > 0 } code && Array.IndexOf(All, code) >= 0 ? code : Table;
}

/// <summary>
/// Чистая логика КАТЕГОРИЙ списка уведомлений (T-272), вынесенная из разметки, чтобы её
/// можно было проверить тестами — как <see cref="TaskTagGroups"/> и <see cref="ModelSkillGroups"/>.
///
/// Категории двухуровневые: «проект / событие», «проект / исполнитель», «исполнитель / проект».
/// Правило со списком из нескольких проектов (или исполнителей) попадает в КАЖДУЮ свою
/// категорию — это не взаимоисключающие корзины, а перечисление: правило действительно
/// касается и того проекта, и другого.
///
/// ПУСТОЙ СПИСОК в правиле означает «все» (все проекты, все, кого касается событие), и такое
/// правило уходит в отдельную категорию <see cref="Any"/>, которая стоит ПЕРВОЙ. Складывать
/// её с перечислением конкретных проектов нельзя: «все проекты» — это в том числе те, которых
/// в организации ещё нет.
/// </summary>
public static class NotificationGroups
{
    /// <summary>Ключ категории «все»: у проекта и исполнителя ключ непустой (UUID),
    /// поэтому пустая строка свободна.</summary>
    public const string Any = "";

    /// <summary>Категория второго уровня: ключ, заголовок и правила.</summary>
    public sealed record SubGroup(string Key, string Title, IReadOnlyList<NotificationRule> Rules);

    /// <summary>Категория первого уровня.</summary>
    public sealed record Group(string Key, string Title, IReadOnlyList<SubGroup> Items)
    {
        /// <summary>Сколько всего правил в категории (показывается рядом с заголовком).</summary>
        public int Count => Items.Sum(i => i.Rules.Count);
    }

    /// <summary>
    /// Разложить правила по категориям выбранного представления.
    /// </summary>
    /// <param name="rules">Правила в том порядке, в каком их отдал сервер.</param>
    /// <param name="view">Код представления (<see cref="NotificationViews"/>).</param>
    /// <param name="projectTitle">Название проекта по ключу; «» — «все проекты».</param>
    /// <param name="executorTitle">Ник исполнителя по ключу; «» — «все исполнители».</param>
    /// <param name="eventTitle">Название события по коду.</param>
    public static List<Group> Build(IEnumerable<NotificationRule> rules, string view,
        Func<string, string> projectTitle, Func<string, string> executorTitle,
        Func<string, string> eventTitle)
    {
        var projects = (Func<NotificationRule, IReadOnlyList<string>>)(r =>
            r.ProjectIds.Count > 0 ? r.ProjectIds : [Any]);
        var executors = (Func<NotificationRule, IReadOnlyList<string>>)(r =>
            r.ExecutorIds.Count > 0 ? r.ExecutorIds : [Any]);
        var events = (Func<NotificationRule, IReadOnlyList<string>>)(r => new[] { r.EventKind });

        var (outer, inner, outerTitle, innerTitle) = NotificationViews.Normalize(view) switch
        {
            NotificationViews.ProjectExecutor => (projects, executors, projectTitle, executorTitle),
            NotificationViews.ExecutorProject => (executors, projects, executorTitle, projectTitle),
            // ProjectEvent и всё непонятное — «проект / событие»
            _ => (projects, events, projectTitle, eventTitle),
        };

        var tree = new Dictionary<string, Dictionary<string, List<NotificationRule>>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var outerKey in outer(rule).Distinct(StringComparer.Ordinal))
            {
                if (!tree.TryGetValue(outerKey, out var branch))
                {
                    tree[outerKey] = branch = new Dictionary<string, List<NotificationRule>>(StringComparer.Ordinal);
                }
                foreach (var innerKey in inner(rule).Distinct(StringComparer.Ordinal))
                {
                    if (!branch.TryGetValue(innerKey, out var list))
                    {
                        branch[innerKey] = list = [];
                    }
                    list.Add(rule);
                }
            }
        }

        return tree
            .Select(g => new Group(g.Key, outerTitle(g.Key), g.Value
                .Select(s => new SubGroup(s.Key, innerTitle(s.Key), s.Value))
                .OrderBy(s => s.Key.Length > 0)     // «все» — первой
                .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList()))
            .OrderBy(g => g.Key.Length > 0)
            .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
