using System.Text.RegularExpressions;

namespace AI2P.Core;

/// <summary>
/// ОБЛАСТИ ЗАПИСИ ОПЫТА (T-269-S0) — одними и теми же словами их называют инструмент
/// агента, API, UI и хранилище, поэтому слова живут в Core. До этого они были константами
/// <c>ExperienceService</c> (хранилище) и <c>TaskToolset</c> (коннекторы) по отдельности,
/// а UI писал их строками.
/// </summary>
public static class ExperienceScopes
{
    /// <summary>Опыт ПРОЕКТА (todo48): его получает любая задача проекта.</summary>
    public const string Project = "project";

    /// <summary>Опыт УЗЛА ШАБЛОНА (todo32): про этот шаг процесса.</summary>
    public const string Template = "template";

    /// <summary>ОБЩИЕ ПРАВИЛА РАБОТЫ организации (T-11-S0): их получает каждая задача.</summary>
    public const string General = "general";

    /// <summary>Значение — одна из трёх областей.</summary>
    public static bool IsKnown(string? scope) => scope is Project or Template or General;
}

/// <summary>
/// РАЗГРАНИЧЕНИЕ ОБЛАСТЕЙ ОПЫТА (T-269-S0, ветка T-318) — дешёвая проверка «эта запись
/// выглядит проектной» для ОБЩИХ ПРАВИЛ РАБОТЫ организации.
///
/// <para>Правило, которое проверка сторожит, одно и то же во всех трёх местах (подсказка
/// инструмента агента, промпт, форма записи):
/// <list type="bullet">
/// <item><b>общее</b> — про то, КАК МЫ РАБОТАЕМ (процесс, отчётность, дисциплина проверок):
/// верно в любом проекте организации, не называет ни файла, ни кода задачи, ни названия
/// продукта;</item>
/// <item><b>опыт проекта</b> — про ЭТОТ продукт: устройство, грабли, решения, имена
/// сущностей;</item>
/// <item><b>опыт узла шаблона</b> — про ЭТОТ шаг процесса.</item>
/// </list></para>
///
/// <para>Проверка НАМЕРЕННО без модели: она стоит на пути каждой записи в общий опыт
/// (в том числе из инструмента агента), поэтому обязана быть мгновенной и одинаковой
/// на сервере и в форме. Отсюда и её природа — четыре ПРИЗНАКА, каждый из которых
/// в общем правиле не встречается почти никогда: код задачи (<c>T-241</c>), путь файла,
/// расширение исходника и имя проекта или организации.</para>
///
/// <para>Ошибается она в одну сторону: лишний раз предупредить дешевле, чем пропустить
/// в общий опыт мусор, который потом приходит КАЖДОЙ задаче организации. Поэтому
/// у человека предупреждение подтверждается кнопкой, а агенту — отказ с предложением
/// завести ту же запись в опыт проекта (решение заказчика по T-269-S0: молчаливая
/// подмена области сбила бы агента, который потом ищет запись по id).</para>
/// </summary>
public static class ExperienceScopeCheck
{
    /// <summary>Вид признака: КОД ЗАДАЧИ (T-241, T-12-S0).</summary>
    public const string KindTaskCode = "taskCode";

    /// <summary>Вид признака: ПУТЬ ФАЙЛА (src/AI2P.Core/Loc.cs, C:\ai\AI2P).</summary>
    public const string KindPath = "path";

    /// <summary>Вид признака: РАСШИРЕНИЕ ИСХОДНИКА (.cs, .razor, .json).</summary>
    public const string KindExtension = "ext";

    /// <summary>Вид признака: ИМЯ ПРОЕКТА ИЛИ ОРГАНИЗАЦИИ.</summary>
    public const string KindName = "name";

    /// <summary>Найденный признак: его вид и то место текста, которым он пойман, —
    /// человеку и агенту нужно показать, ЧТО именно сочтено проектным.</summary>
    public sealed record Sign(string Kind, string Sample);

    private static readonly Regex TaskCode =
        new(@"\bT-\d+(-S\d+)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Путь: буква диска (<c>C:\ai\AI2P</c>), корень POSIX, звено с точкой
    /// (<c>src/AI2P.Core</c>) либо три звена подряд (<c>doc\ru\man</c>).
    /// <para>Звенья — ТОЛЬКО латиницей и намеренно: иначе «Время/качество» из поставляемого
    /// правила работы (<c>seed.experience.2</c>) ловилось бы как файл, и общее правило
    /// дистрибутива само не проходило бы проверку. По той же причине двух звеньев без точки
    /// мало: «and/or» путём не является.</para>
    /// </summary>
    private static readonly Regex Path = new(
        @"(\b[A-Za-z]:[\\/][\w.+-]*)"
        + @"|(\B/(usr|etc|opt|home|var)\b)"
        + @"|(\b[A-Za-z0-9_+-]+[\\/][A-Za-z0-9_+-]*\.[A-Za-z0-9_+-]+)"
        + @"|(\b[A-Za-z0-9_+-]+[\\/][A-Za-z0-9_+-]+[\\/][A-Za-z0-9_+-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Расширения ИСХОДНИКОВ и файлов проекта. Списком, а не «точка и три буквы»:
    /// сокращения вроде «т.е.» и номера версий иначе попадали бы в признаки.</summary>
    private static readonly Regex Extension = new(
        @"\b[\w.+-]+\.(cs|razor|cshtml|csproj|sln|json|xml|yml|yaml|md|py|ps1|psm1|cmd|bat|sh|sql|js|ts|tsx|css|scss|html|c|h|cpp|hpp|go|rs|java|kt|php|swift|rb|toml|ini|cfg|log|safetensors|gguf)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// ПРИЗНАКИ ПРОЕКТНОГО в тексте. Пустой список — текст на общее правило похож.
    /// </summary>
    /// <param name="text">Текст записи.</param>
    /// <param name="names">Названия проектов и организации: их знает только сервер,
    /// поэтому параметр необязателен — форма проверяет тремя остальными признаками.</param>
    public static List<Sign> Signs(string? text, IEnumerable<string>? names = null)
    {
        var found = new List<Sign>();
        var value = text ?? "";
        if (value.Trim().Length == 0)
        {
            return found;
        }
        Add(KindTaskCode, TaskCode.Match(value));
        Add(KindExtension, Extension.Match(value));
        Add(KindPath, Path.Match(value));
        foreach (var name in names ?? [])
        {
            // короткое название («АИ», «QA») даёт ложные срабатывания внутри слов,
            // поэтому имя короче трёх знаков признаком не считаем вовсе
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length < 3)
            {
                continue;
            }
            // граница нужна только СЛЕВА: имя проекта человек склоняет («в Планировщике»),
            // и требование границы справа не поймало бы ни одной живой фразы
            var match = Regex.Match(value, @"(?<![\w-])" + Regex.Escape(trimmed),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                found.Add(new Sign(KindName, match.Value));
                break;   // одного имени довольно: перечислять все — шум
            }
        }
        return found;

        void Add(string kind, Match match)
        {
            if (match.Success)
            {
                found.Add(new Sign(kind, match.Value));
            }
        }
    }

    /// <summary>Текст выглядит проектным — коротко, когда образцы не нужны.</summary>
    public static bool LooksProjectSpecific(string? text, IEnumerable<string>? names = null) =>
        Signs(text, names).Count > 0;

    /// <summary>Образцы найденного через запятую — ими называется причина отказа
    /// и предупреждения формы.</summary>
    public static string Samples(IEnumerable<Sign> signs) =>
        string.Join(", ", signs.Select(s => "«" + s.Sample + "»"));
}
