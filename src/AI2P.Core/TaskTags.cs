namespace AI2P.Core;

/// <summary>
/// ТЭГИ ЗАДАЧ — ключевые слова (T-222). Справочника у тэгов нет намеренно: тэг заводится тем,
/// что его написали в форме задачи, и исчезает, когда его не осталось ни у одной задачи;
/// набор тэгов организации — это DISTINCT по таблице <c>task_tags</c>.
///
/// Правило приведения одно на всю систему и живёт здесь, в ядре: его применяют и хранилище
/// (запись задачи, разбор фильтра), и форма задачи в UI. Слой UI на Storage не ссылается,
/// а два одинаковых по смыслу, но разных по букве правила разъехались бы в первый же выпуск.
/// </summary>
public static class TaskTags
{
    /// <summary>Разделитель тэгов в поле ввода и в строке запроса фильтра.</summary>
    public const char Separator = ',';

    /// <summary>
    /// Привести список тэгов: пробелы по краям снимаются, пустые отбрасываются, повторы
    /// снимаются БЕЗ УЧЁТА РЕГИСТРА — первое написание побеждает. Совсем сводить регистр
    /// нельзя: тэг пишет человек, и «UI» не должен превращаться в «ui»; а вот две записи
    /// «UI» и «ui» у ОДНОЙ задачи — это дубль.
    ///
    /// ЗАПЯТАЯ — РАЗДЕЛИТЕЛЬ, а не символ тэга: ею человек перечисляет тэги в поле ввода,
    /// ею же они едут в строке запроса фильтра (<c>?tags=a,b</c>). Поэтому «ui, сборка»
    /// превращается в два тэга, а тэга с запятой внутри не существует вовсе.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var raw in tags ?? [])
        {
            foreach (var part in raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries
                                                      | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(part))
                {
                    result.Add(part);
                }
            }
        }
        return result;
    }

    /// <summary>То же приведение для одной строки: поле ввода нового тэга и параметр запроса.</summary>
    public static List<string> Normalize(string? tags) => Normalize([tags ?? ""]);

    /// <summary>Тэги — одно и то же слово (сравнение категорий и защита от дублей).</summary>
    public static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
