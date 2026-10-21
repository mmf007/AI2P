using AI2P.Core.Entities;

namespace AI2P.Storage;

/// <summary>
/// БЛИЗОСТЬ ЗАПИСИ ОПЫТА К ТЕКСТУ ЗАДАЧИ (T-554-S0). До этой правки отбор опыта в задание
/// вовсе не смотрел на саму задачу: навык исполнителя пропускал почти всё (у CLI-агента
/// навыков много), тэги только двигали запись вперёд, а предел заполнялся по свежести —
/// и любая задача проекта получала одни и те же ~80 записей на 96 % предела.
///
/// <para>Мера чисто лексическая (эмбеддингов у нас нет, см. <c>ExperienceService.Search</c>):
/// основы слов задачи (заголовок, описание, критерии) против основ слов записи. В счёт идут
/// только РЕДКИЕ основы — те, что встречаются не больше чем в <see cref="RareShare"/> записей
/// набора: «задача», «тест», «файл» есть почти в каждой записи и о теме не говорят ничего.
/// Вес основы — её редкость (IDF), балл записи — сумма весов совпавших основ.</para>
/// </summary>
public static class ExperienceRelevance
{
    /// <summary>Основа считается редкой, если встречается не больше чем в этой доле записей.
    /// Подобрано на опыте проекта AI2P (539 записей, 593 тыс. знаков, test/t554s0/exp.py):
    /// при 0.10 и двух совпадениях задача получает 16–39 записей (30–51 тыс. знаков), при
    /// 0.15 — до 93 записей, то есть опять упирается в предел.</summary>
    public const double RareShare = 0.10;

    /// <summary>Сколько редких основ должно совпасть, чтобы запись считалась «по теме».
    /// Одно совпадение — почти всегда случайность (общее слово длинного описания).</summary>
    public const int MinRareHits = 2;

    /// <summary>Основы короче этого в счёт не идут: предлоги, союзы, «что», «для».</summary>
    private const int MinStem = 3;

    /// <summary>Близость одной записи: сколько редких основ совпало и их суммарный вес.</summary>
    public readonly record struct Fit(int Hits, double Score)
    {
        public bool OnTopic => Hits >= MinRareHits;
    }

    /// <summary>
    /// Близость каждой записи к тексту задачи. Пустой словарь — у задачи нет ни одного
    /// пригодного слова: отбирать не по чему, и вызывающий обязан взять всё, как раньше.
    /// </summary>
    public static Dictionary<string, Fit> Score(IReadOnlyList<ExperienceRecord> records, string topic)
    {
        var result = new Dictionary<string, Fit>(StringComparer.Ordinal);
        var wanted = StemSet(topic);
        if (wanted.Count == 0 || records.Count == 0)
        {
            return result;
        }
        var stemsOf = records.ToDictionary(r => r.Id, r => StemSet(r.Text), StringComparer.Ordinal);
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stems in stemsOf.Values)
        {
            foreach (var stem in stems.Where(wanted.Contains))
            {
                df[stem] = df.GetValueOrDefault(stem) + 1;
            }
        }
        var n = stemsOf.Count;
        var rareMax = Math.Max(1, (int)(n * RareShare));
        foreach (var (id, stems) in stemsOf)
        {
            var hits = 0;
            var score = 0.0;
            foreach (var stem in stems)
            {
                if (df.TryGetValue(stem, out var count) && count <= rareMax)
                {
                    hits++;
                    score += Math.Log((double)n / count) + 1;
                }
            }
            result[id] = new Fit(hits, score);
        }
        return result;
    }

    private static HashSet<string> StemSet(string text) =>
        ExperienceIndex.Stems(text).Where(s => s.Length >= MinStem).ToHashSet(StringComparer.Ordinal);
}
