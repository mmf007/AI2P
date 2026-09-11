using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Чистая логика представления «Навыки» справочника моделей (T-217), вынесенная из
/// <c>SettingsView.razor</c>, чтобы её можно было проверить тестами (как <see cref="BoardColumns"/>).
///
/// Справочник стал большим (31 запись в дистрибутиве 1.83), и таблицей его читать неудобно:
/// вопрос у человека обычно обратный — «кто лучше всех умеет code-write-cs». Поэтому
/// записи раскладываются ПО НАВЫКАМ: категория — код навыка из декларации возможностей
/// (ТЗ п. 7.3), внутри категории модели идут по УБЫВАНИЮ оценки этого навыка.
/// Первое поле сортировки — сама категория (код навыка), второе — оценка.
///
/// Модель попадает в столько категорий, сколько навыков объявлено в её декларации;
/// модель без навыков не теряется — она уходит в отдельную категорию <see cref="NoSkill"/>,
/// которая всегда стоит последней.
/// </summary>
public static class ModelSkillGroups
{
    /// <summary>Код категории «без навыков»: у навыка код непустой, поэтому пустая строка свободна.</summary>
    public const string NoSkill = "";

    /// <summary>Строка категории: модель и её оценка ПО НАВЫКУ этой категории.</summary>
    public sealed record Row(AiModel Model, int Score);

    /// <summary>Категория представления: код навыка и модели по убыванию оценки.</summary>
    public sealed record Group(string Skill, IReadOnlyList<Row> Models);

    /// <summary>
    /// Разложить справочник по навыкам. Категории — по коду навыка (Ordinal, регистр не важен:
    /// коды справочника в нижнем регистре), «без навыков» — в конец. Внутри категории:
    /// оценка по убыванию, при равной оценке — имя модели, чтобы порядок не «дрожал»
    /// от перезагрузки к перезагрузке. Повторный навык в декларации берётся по большей оценке.
    /// </summary>
    public static List<Group> Build(IEnumerable<AiModel> models)
    {
        var bySkill = new Dictionary<string, Dictionary<string, Row>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            List<ModelSkill> skills = model.Skills.Where(s => s.Name.Length > 0).ToList();
            if (skills.Count == 0)
            {
                skills = [new ModelSkill { Name = NoSkill }];
            }
            foreach (var skill in skills)
            {
                if (!bySkill.TryGetValue(skill.Name, out var rows))
                {
                    bySkill[skill.Name] = rows = new Dictionary<string, Row>(StringComparer.Ordinal);
                }
                if (!rows.TryGetValue(model.Id, out var was) || was.Score < skill.Score)
                {
                    rows[model.Id] = new Row(model, skill.Score);
                }
            }
        }

        return bySkill
            .OrderBy(g => g.Key.Length == 0)                       // «без навыков» — последней
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)  // первое поле — категория
            .Select(g => new Group(g.Key, g.Value.Values
                .OrderByDescending(r => r.Score)                   // второе поле — оценка, по убыванию
                .ThenBy(r => r.Model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();
    }

    /// <summary>
    /// Навыки модели для колонки представления «таблица» — ПО КУСКАМ «код - оценка,»,
    /// каждый из которых разметка рисует своим НЕРАЗРЫВНЫМ элементом (правка T-238,
    /// класс <c>ai2p-skill-chunk</c> = <c>white-space: nowrap</c>).
    ///
    /// Зачем куски, а не одна строка: на телефоне колонка сжимается до нескольких символов,
    /// и браузер ломал строку ВНУТРИ навыка — на строке оставалось одно слово
    /// («code-» / «write» / «-» / «92»). Мест разрыва внутри куска ДВА, и оба надо закрыть:
    /// пробелы и ДЕФИС (коды навыков дефисные: <c>code-write</c>, <c>analyze-requirements</c>),
    /// причём дефис — законное место переноса, и неразрывными пробелами он не закрывается
    /// (проверено живьём: 10 навыков из 13 рвались по дефису при неразрывных пробелах).
    ///
    /// Между кусками разметка ставит обычный пробел — это и есть единственное разрешённое
    /// место переноса, «после запятой». Наименьшая ширина колонки становится равна длине
    /// самого длинного навыка: колонка получается шире, а строк в ней меньше.
    /// </summary>
    public static IReadOnlyList<string> SkillsParts(AiModel model)
    {
        var parts = model.Skills.Select(s => $"{s.Name} - {s.Score}").ToList();
        for (var i = 0; i < parts.Count - 1; i++)
        {
            parts[i] += ",";
        }
        return parts;
    }
}
