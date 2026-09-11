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
    /// Уместить записи опыта в остаток бюджета (T-29-S0). Порядок ОТБОРА и порядок ПЕЧАТИ —
    /// разные вещи, и в этом весь смысл: место занимают сначала записи «загружать всегда»
    /// (их велено вставлять при любом раскладе), затем самые свежие — старый опыт чаще уже
    /// учтён и в коде, и в более новых записях. А печатается взятое в ИСХОДНОМ порядке
    /// списка (он хронологический) — так блок читается как история работы, а не как рейтинг.
    /// </summary>
    /// <param name="line">Как запись выглядит в промпте: по её длине и считается расход.</param>
    /// <param name="rank">Насколько запись прицельна (меньше — важнее): опыт узла шаблона
    /// написан ровно про эту работу, опыт проекта шире, общие правила ещё шире. Без этого
    /// порядок решала бы очерёдность блоков в промпте, и при тесном пределе общие правила
    /// съедали бы место у самого нужного (поймано живой проверкой T-29-S0).</param>
    /// <returns>Что вставляем и сколько записей не поместилось.</returns>
    public (List<ExperienceRecord> Taken, int Skipped) Fit(
        IReadOnlyList<ExperienceRecord> records, Func<ExperienceRecord, string> line,
        Func<ExperienceRecord, int>? rank = null)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var record in records
                     .OrderByDescending(r => r.AlwaysLoad)
                     .ThenBy(r => rank is null ? 0 : rank(r))
                     .ThenByDescending(r => r.UpdatedAt)
                     .ThenByDescending(r => r.CreatedAt))
        {
            if (!HasRoom)
            {
                skipped++;
                continue;
            }
            Spend(line(record).Length);
            taken.Add(record.Id);
        }
        return (records.Where(r => taken.Contains(r.Id)).ToList(), skipped);
    }
}
