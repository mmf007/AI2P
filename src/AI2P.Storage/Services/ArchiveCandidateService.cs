using AI2P.Core;
using AI2P.Core.Entities;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Запись, которую правило архивации отобрало в кандидаты (T-41-S0).</summary>
/// <param name="Target">Вид данных (<see cref="ArchiveRuleTargets"/>).</param>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="DisplayId">Номер записи (T-15, OBJ-3); у видов без номера — пусто.</param>
/// <param name="Title">Как запись зовётся на экране — чтобы человек узнал, что уедет.</param>
/// <param name="Date">Дата, по которой считался возраст (создания либо изменения).</param>
/// <param name="Children">Сколько потомков уедет вместе с записью (у иерархических видов).</param>
public sealed record ArchiveCandidate(string Target, string Id, string DisplayId, string Title,
    DateTime Date, int Children);

/// <summary>
/// ОТБОР КАНДИДАТОВ НА АРХИВАЦИЮ ПО ПРАВИЛУ (T-41-S0, выпуск 1.105) — отдельный класс,
/// потому что читателей у него двое: автоматическая архивация по расписанию (T-46-S0)
/// и форма добавления архива (T-43-S0), которая проверяет, выполнены ли правила у прежнего
/// текущего архива. Ничего не переносит и ничего не меняет — только отвечает на вопрос
/// «что по этому правилу пора уносить».
///
/// ГЛАВНОЕ ПРАВИЛО ОТБОРА: иерархия задач архивируется ЦЕЛИКОМ, частями её архивировать
/// нельзя — иначе в рабочей среде осталась бы подзадача, у которой нет родителя. Поэтому
/// записи правила относятся только к КОРНЕВЫМ задачам (и корневым узлам шаблонов): у корня
/// проверяется состояние и возраст, а у КАЖДОГО потомка — только попадание его состояния
/// в список состояний потомков. Один неподходящий потомок отменяет архивацию всей иерархии.
/// Пустой список состояний потомков означает «любое состояние» (а не «никакое»).
///
/// Остальное сравнение прямо следует из полей записи: активность данных и возраст по
/// выбранной дате — временной (дни) или календарный (месяцы и года).
/// </summary>
public sealed class ArchiveCandidateService
{
    private readonly Database _db;

    public ArchiveCandidateService(Database db) => _db = db;

    /// <summary>
    /// Кандидаты по ОДНОМУ правилу. <paramref name="now"/> задаётся снаружи (а не берётся
    /// внутри) намеренно: без этого проверку календарного срока пришлось бы писать на
    /// сегодняшней дате и ждать смены месяца.
    /// </summary>
    public List<ArchiveCandidate> Select(ArchiveRule rule, DateTime? now = null)
    {
        using var conn = _db.Open();
        return Select(conn, rule, now ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Кандидаты по НАБОРУ правил (правила одного архива либо общие правила организации),
    /// без повторов: два правила легко отбирают одну и ту же запись.
    /// </summary>
    public List<ArchiveCandidate> Select(IEnumerable<ArchiveRule> rules, DateTime? now = null)
    {
        var moment = now ?? DateTime.UtcNow;
        using var conn = _db.Open();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<ArchiveCandidate>();
        foreach (var rule in rules.Where(r => r.IsActive))
        {
            foreach (var candidate in Select(conn, rule, moment))
            {
                if (seen.Add(candidate.Target + "|" + candidate.Id))
                {
                    all.Add(candidate);
                }
            }
        }
        return all;
    }

    /// <summary>
    /// Правила ВЫПОЛНЕНЫ — по ним в рабочей среде не осталось ничего, что пора уносить.
    /// Этим форма добавления архива проверяет прежний текущий архив (T-43-S0): заводить
    /// следующий архив, не разобравшись с предыдущим, человеку стоит хотя бы показать.
    /// </summary>
    public bool Satisfied(IEnumerable<ArchiveRule> rules, DateTime? now = null) =>
        Select(rules, now).Count == 0;

    // --- внутреннее ---

    private static List<ArchiveCandidate> Select(SqliteConnection conn, ArchiveRule rule,
        DateTime now)
    {
        var shape = Shape.Of(rule.Target);
        if (shape is null)
        {
            return [];
        }
        var date = shape.DateColumn(rule.DateField);
        var sql = $"SELECT id, {shape.DisplayExpr} AS did, {shape.TitleExpr} AS ttl, "
                  + $"{date} AS dt FROM {shape.Table} WHERE {date} <= @edge"
                  + shape.Where(rule)
                  + " ORDER BY dt";
        var args = new List<(string, object?)> { ("@edge", Sql.ToDb(rule.Threshold(now))) };
        if (ArchiveRuleTargets.HasTaskStatus(rule.Target))
        {
            args.Add(("@st", rule.TaskStatus));
        }
        var rows = Sql.Query(conn, null, sql,
            r => new ArchiveCandidate(rule.Target, r.S("id"), r.S("did"), r.S("ttl"),
                r.Dt("dt"), 0),
            [.. args]);
        if (!ArchiveRuleTargets.IsHierarchical(rule.Target))
        {
            return rows;
        }
        // ИЕРАРХИЯ ЦЕЛИКОМ: у потомков смотрим ТОЛЬКО состояние — ни возраст, ни активность
        // к ним не относятся. Хотя бы один потомок мимо списка — вся ветка остаётся в работе
        var kept = new List<ArchiveCandidate>();
        foreach (var row in rows)
        {
            var children = ChildStatuses(conn, row.Id);
            if (rule.ChildStatuses.Count > 0
                && children.Any(s => !rule.ChildStatuses.Contains(s, StringComparer.Ordinal)))
            {
                continue;
            }
            kept.Add(row with { Children = children.Count });
        }
        return kept;
    }

    /// <summary>Состояния ВСЕХ потомков задачи на любой глубине (рекурсивный обход дерева).</summary>
    private static List<string> ChildStatuses(SqliteConnection conn, string rootId) =>
        Sql.Query(conn, null, """
            WITH RECURSIVE sub(id, status) AS (
                SELECT id, status FROM tasks WHERE parent_id=@root AND deleted_at IS NULL
                UNION ALL
                SELECT t.id, t.status FROM tasks t JOIN sub ON t.parent_id = sub.id
                WHERE t.deleted_at IS NULL
            )
            SELECT status FROM sub
            """,
            r => r.S("status"), ("@root", rootId));

    /// <summary>
    /// Как вид данных правила ложится на таблицу базы: имя таблицы, колонки даты, номера
    /// и заголовка и то, чем у него выражается активность. Держать это одной описью дешевле
    /// шести почти одинаковых запросов, и новый вид данных добавляется одной строкой.
    /// </summary>
    private sealed record Shape(string Table, string DisplayExpr, string TitleExpr,
        bool HasIsActive, bool HasDeletedAt, string? FixedDateColumn, string ExtraWhere)
    {
        public static Shape? Of(string target) => target switch
        {
            // задачи и шаблоны — строки ОДНОЙ таблицы; правило относится к КОРНЕВЫМ записям
            ArchiveRuleTargets.Tasks => new("tasks", "display_id", "title",
                false, true, null, " AND is_template=0 AND parent_id IS NULL"),
            ArchiveRuleTargets.Templates => new("tasks", "display_id", "title",
                false, true, null, " AND is_template=1 AND parent_id IS NULL"),
            ArchiveRuleTargets.Objects => new("objects", "display_id", "name",
                true, true, null, ""),
            // у записи опыта с T-265-S0 есть СВОЙ признак активности, поэтому «только
            // неактивные» означает у неё и погашенные записи, а не одни удалённые
            ArchiveRuleTargets.Experience => new("experience", "''", "substr(text, 1, 80)",
                true, true, null, ""),
            ArchiveRuleTargets.Security => new("security_rules", "''", "kind",
                false, true, null, ""),
            // журнал событий пишется один раз и не правится: даты изменения у него нет
            // вовсе, поэтому обе даты правила приходят на ts, а активности нет тем более
            ArchiveRuleTargets.Logs => new("events", "''", "event_type",
                false, false, "ts", ""),
            _ => null,
        };

        public string DateColumn(string dateField) => FixedDateColumn
            ?? (dateField == ArchiveRuleDates.Created ? "created_at" : "updated_at");

        /// <summary>Хвост условия: свои ограничения вида плюс активность данных.</summary>
        public string Where(ArchiveRule rule)
        {
            var where = ExtraWhere;
            if (ArchiveRuleTargets.HasTaskStatus(rule.Target))
            {
                // СОСТОЯНИЕ ЗАДАЧИ спрашивается только у правил про задачи — у остальных
                // видов вместо него активность данных (у шаблонов состояние узла ничего
                // не значит: они не выполняются)
                where += " AND status=@st";
            }
            if (!HasDeletedAt)
            {
                return where;   // логи: ни удаления, ни выключения у записи нет
            }
            return where + (rule.Activity switch
            {
                ArchiveRuleActivity.Active => HasIsActive
                    ? " AND deleted_at IS NULL AND is_active=1"
                    : " AND deleted_at IS NULL",
                ArchiveRuleActivity.Inactive => HasIsActive
                    ? " AND (deleted_at IS NOT NULL OR is_active=0)"
                    : " AND deleted_at IS NOT NULL",
                // «любая» — берём и живые, и помеченные удалёнными: удалённая запись
                // тоже занимает оперативный объём, ради которого архивация и затевается
                _ => "",
            });
        }
    }
}
