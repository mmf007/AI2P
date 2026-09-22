using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// ПРЕДЕЛ ПОДСТАНОВКИ ОПЫТА В ПРОМПТ ЗАДАНИЯ (T-29-S0) — один счётчик на все три блока
/// опыта: общие правила работы, опыт проекта и опыт узла шаблона. До этого предела не было
/// вовсе, и блок опыта занимал около трети контекста задания, разрастаясь с каждым
/// завершённым заданием (замер T-12-S0).
///
/// Контроль МЯГКИЙ: остаток проверяется ПЕРЕД очередной записью, а сама запись вставляется
/// целиком. Значит вставленное всегда немного больше предела — ровно на хвост последней
/// записи. Резать запись посередине нельзя: обрубок урока читается как другой урок, а
/// половина ссылки или кода вводит агента в заблуждение вернее, чем отсутствие записи.
///
/// Величина берётся из настроек ПРОЕКТА (<see cref="ProjectSettings.ExperienceLimitChars"/>);
/// у задачи вне проекта настройку взять неоткуда — там то же умолчание.
/// </summary>
public sealed class ExperienceBudget
{
    private int _left;

    /// <param name="limit">Предел в знаках; не больше нуля — умолчание (выключить опыт
    /// настройкой размера нельзя, для этого есть отбор по навыкам и тэгам).</param>
    public ExperienceBudget(int limit)
    {
        Limit = limit > 0 ? limit : ProjectSettings.ExperienceLimitDefault;
        _left = Limit;
    }

    /// <summary>Предел, с которым бюджет заведён (знаков).</summary>
    public int Limit { get; }

    /// <summary>Сколько знаков ещё не потрачено; ноль или меньше — вставлять больше нечего.</summary>
    public int Left => _left;

    /// <summary>Осталось ли место под ещё одну запись. Именно так и выглядит мягкий контроль:
    /// спрашиваем ДО вставки, а вставляем целиком.</summary>
    public bool HasRoom => _left > 0;

    /// <summary>Записать расход: длина того, что действительно ушло в промпт.</summary>
    public void Spend(int chars) => _left -= chars;

    /// <summary>
    /// КВОТЫ УРОВНЕЙ (T-267-S0): доли остатка бюджета, которые отводятся опыту узла шаблона,
    /// опыту проекта и общим правилам работы. Числа зашиты намеренно: ещё одна настройка,
    /// которую никто не крутит, дороже пользы, а без квот место целиком доставалось первому
    /// уровню в списке — замер 16.09.2026 показал задание, где опыт проекта съел весь предел
    /// и до агента не доехало даже пятое общее правило организации.
    /// </summary>
    public static readonly int[] SharePercent = [50, 30, 20];

    /// <summary>
    /// Уровень записи для дележа квот (T-267-S0): 0 — узел шаблона (включая узлы-предки),
    /// 1 — проект, 2 — общие правила организации. Опознаётся по полям самой записи, а не по
    /// тому, из какого списка она пришла: у каждой из трёх разновидностей признак свой.
    /// </summary>
    public static int LevelOf(ExperienceRecord record) =>
        record.TemplateTaskId.Length > 0 ? 0 : record.IsProjectLevel ? 1 : 2;

    /// <summary>
    /// Уместить записи опыта в остаток бюджета (T-29-S0, квоты — T-267-S0). Порядок ОТБОРА
    /// и порядок ПЕЧАТИ — разные вещи, и в этом весь смысл: печатается взятое в ИСХОДНОМ
    /// порядке списка (он хронологический) — так блок читается как история работы, а не как
    /// рейтинг. Отбор идёт тремя проходами:
    /// <list type="number">
    /// <item>записи «ЗАГРУЖАТЬ ВСЕГДА» — вне конкурса и вне квот, как и раньше;</item>
    /// <item>каждый УРОВЕНЬ берёт из своей квоты (<see cref="SharePercent"/>) — так опыт
    /// проекта больше не вытесняет общие правила и опыт узла;</item>
    /// <item>НЕИСПОЛЬЗОВАННАЯ КВОТА ПЕРЕТЕКАЕТ соседям: то, что не поместилось в свою долю,
    /// добирается из общего остатка по прицельности. Пустой уровень не крадёт места.</item>
    /// </list>
    /// Контроль мягкий на каждом проходе: остаток спрашивается ДО записи, запись вставляется
    /// целиком (обрубок урока читается как другой урок).
    /// </summary>
    /// <param name="line">Как запись выглядит в промпте: по её длине и считается расход.</param>
    /// <param name="rank">Насколько запись прицельна (меньше — важнее): свой узел шаблона
    /// важнее родителя, родитель важнее деда, дальше опыт проекта, потом общие правила.
    /// Без этого порядок решала бы очерёдность блоков в промпте, и при тесном пределе общие
    /// правила съедали бы место у самого нужного (поймано живой проверкой T-29-S0).</param>
    /// <param name="onTopic">Тэги записи сошлись с тэгами задачи (T-267-S0). Тэг — СИГНАЛ,
    /// а не фильтр: несовпадение больше не отсекает запись (а то <c>create_experience</c>,
    /// проставляющий новым записям тэги задачи, со временем начал бы отсекать нужное по
    /// формальному несовпадению слова), но совпадение двигает запись вперёд — ПОЛУУРОВНЯ
    /// прицельности: опыт своего узла не по теме всё ещё важнее опыта проекта по теме.</param>
    /// <returns>Что вставляем и сколько записей не поместилось.</returns>
    public (List<ExperienceRecord> Taken, int Skipped) Fit(
        IReadOnlyList<ExperienceRecord> records, Func<ExperienceRecord, string> line,
        Func<ExperienceRecord, int>? rank = null, Func<ExperienceRecord, bool>? onTopic = null)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        // прицельность записи с прибавкой за тэги: ранг стоит двух шагов, совпадение тэгов —
        // одного, поэтому тэги решают только между записями одного и того же уровня
        int Key(ExperienceRecord r) =>
            (rank is null ? 0 : rank(r)) * 2 + (onTopic is null || onTopic(r) ? 0 : 1);
        IOrderedEnumerable<ExperienceRecord> Sorted(IEnumerable<ExperienceRecord> src) => src
            .OrderBy(Key)
            .ThenByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt);
        bool Take(ExperienceRecord r)
        {
            Spend(line(r).Length);
            return taken.Add(r.Id);
        }

        foreach (var record in Sorted(records.Where(r => r.AlwaysLoad)))
        {
            Take(record);
        }
        var rest = records.Where(r => !r.AlwaysLoad).ToList();
        var room = Math.Max(_left, 0);
        var spent = new int[SharePercent.Length];
        for (var level = 0; level < SharePercent.Length; level++)
        {
            var quota = room * SharePercent[level] / 100;
            foreach (var record in Sorted(rest.Where(r => LevelOf(r) == level)))
            {
                if (spent[level] >= quota || !HasRoom)
                {
                    break;
                }
                var before = _left;
                Take(record);
                spent[level] += before - _left;
            }
        }
        foreach (var record in Sorted(rest.Where(r => !taken.Contains(r.Id))))
        {
            if (!HasRoom)
            {
                break;
            }
            Take(record);
        }
        return (records.Where(r => taken.Contains(r.Id)).ToList(), records.Count - taken.Count);
    }
}
