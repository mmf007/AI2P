using System.Text;
using System.Text.RegularExpressions;

namespace AI2P.Core;

/// <summary>
/// ССЫЛКИ НА ОБЪЕКТЫ ПРОЕКТА (T-259, версия 1.91) — как персонаж, локация или стиль
/// попадают в промпт.
///
/// ПОЧЕМУ ЭТО ВООБЩЕ НУЖНО. Медиа-модель получает промптом ТОЛЬКО ОПИСАНИЕ ЗАДАЧИ и больше
/// ничего: заголовок, критерии приёмки и оба блока опыта коннектор видит в <c>requestText</c>
/// и игнорирует (разбор T-251, п. 2.1). Значит, единственный способ дать модели дословный
/// паспорт персонажа — положить его В ОПИСАНИЕ. Переписывать паспорт руками в каждый кадр
/// нельзя: пересказ от кадра к кадру и есть та самая причина, по которой персонаж «плывёт».
/// Поэтому в описании стоит ССЫЛКА, а дословный текст подставляется при запуске задания.
///
/// ФОРМА ССЫЛКИ. Две, и обе однострочные:
/// <list type="bullet">
/// <item><c>@obj:OBJ-3</c> — по номеру объекта; ровно это вставляет кнопка формы;</item>
/// <item><c>@obj:[Герой Вася]</c> — по названию в квадратных скобках, когда пишут руками
/// (в названии бывают пробелы, поэтому без скобок его не отличить от следующего слова).</item>
/// </list>
/// Обычная markdown-ссылка для этого не годится: она уехала бы в промпт как есть, вместе
/// со скобками и адресом, а модель приняла бы адрес за часть описания сцены.
///
/// НЕИЗВЕСТНАЯ ССЫЛКА ОСТАЁТСЯ КАК ЕСТЬ. Молча стереть её было бы хуже всего: человек
/// увидел бы правильный на вид промпт без персонажа и списывал бы непохожий кадр на модель.
///
/// ВЛОЖЕННОСТЬ НЕ РАСКРЫВАЕТСЯ: ссылка внутри паспорта остаётся ссылкой. Иначе пара
/// объектов, сославшихся друг на друга, раскрывалась бы бесконечно.
/// </summary>
/// <summary>
/// В КАКОЙ ФОРМЕ ПИШУТ ССЫЛКУ НА ОБЪЕКТ (T-267) — настройка проекта
/// (<see cref="ProjectSettings.ObjRefFormat"/>), а не всей установки: список объектов свой
/// у каждого проекта, и привычка называть их номерами либо названиями тоже своя.
///
/// На ЧТЕНИЕ ссылок это не влияет: обе формы понимаются всегда — иначе смена настройки
/// молча сломала бы уже написанные описания задач.
/// </summary>
public static class ObjectRefFormats
{
    /// <summary>По номеру объекта: <c>@obj:OBJ-3</c>. Не ломается при переименовании.</summary>
    public const string Code = "code";

    /// <summary>По названию: <c>@obj:[Герой Вася]</c>. Описание кадра читается целиком.</summary>
    public const string Name = "name";

    /// <summary>Умолчание — номер: так ссылку ставила кнопка списка объектов до T-267,
    /// и у всех уже заведённых проектов настройки нет вовсе.</summary>
    public const string Default = Code;

    /// <summary>Все значения — для формы выбора.</summary>
    public static readonly string[] All = [Code, Name];

    /// <summary>Незнакомое значение (чужая версия, испорченные настройки) — это умолчание,
    /// а не ошибка: настройки проекта едут между серверами кластера.</summary>
    public static string Normalize(string? format) =>
        string.Equals(format, Name, StringComparison.OrdinalIgnoreCase) ? Name : Default;
}

public static class ObjectRefs
{
    /// <summary>Начало ссылки; по нему же дешёвая проверка «есть ли тут вообще ссылки».</summary>
    public const string Prefix = "@obj:";

    /// <summary>
    /// Ссылка целиком: <c>@obj:[название]</c> либо <c>@obj:номер</c>. Во второй форме адрес
    /// кончается на пробеле или на знаке препинания — иначе «@obj:OBJ-3, крупный план»
    /// искало бы объект с запятой в номере.
    /// </summary>
    private static readonly Regex Reference = new(
        @"@obj:(?:\[(?<name>[^\]\r\n]+)\]|(?<code>[^\s\[\],;:!?()""'«»]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Готовая ссылка на объект с таким номером — то, что кладётся в буфер обмена.</summary>
    public static string Marker(string code) => Prefix + code;

    /// <summary>Ссылка по названию — вторая форма, для ручного письма.</summary>
    public static string MarkerByName(string name) => Prefix + "[" + name + "]";

    /// <summary>
    /// Ссылка в том виде, какой ЗАДАН НАСТРОЙКОЙ ПРОЕКТА (T-267): это то, что кнопка
    /// редактора вставляет в текст. Читаются обе формы всегда (<see cref="Find"/>,
    /// <see cref="Expand"/>) — настройка решает только, какую ПИШУТ.
    ///
    /// Объект без названия ссылкой по названию не адресуется, поэтому у безымянного
    /// (такого быть не должно, но хранилище допускает) берётся номер: пустая ссылка
    /// <c>@obj:[]</c> не нашла бы ничего.
    /// </summary>
    public static string MarkerFor(string? format, string code, string? name) =>
        ObjectRefFormats.Normalize(format) == ObjectRefFormats.Name && name is { } n && n.Trim().Length > 0
            ? MarkerByName(n.Trim())
            : Marker(code);

    /// <summary>
    /// Все адреса объектов, упомянутые в тексте, в порядке появления и без повторов.
    /// Адрес — это либо номер (<c>OBJ-3</c>), либо название: различать их здесь не нужно,
    /// поиск объекта всё равно идёт по обоим (<see cref="Expand"/>).
    /// </summary>
    public static List<string> Find(string? text)
    {
        var found = new List<string>();
        if (text is null || !text.Contains(Prefix, StringComparison.Ordinal))
        {
            return found;
        }
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (Match match in Reference.Matches(text))
        {
            var key = RefOf(match);
            if (key.Length > 0 && seen.Add(key))
            {
                found.Add(key);
            }
        }
        return found;
    }

    /// <summary>
    /// Подставить паспорта объектов вместо ссылок. <paramref name="resolve"/> ищет объект
    /// по номеру ИЛИ по названию и возвращает его карточку; null — объект не найден, и
    /// ссылка остаётся в тексте нетронутой.
    /// </summary>
    public static string Expand(string? text, Func<string, ObjectCard?> resolve)
    {
        if (text is null || !text.Contains(Prefix, StringComparison.Ordinal))
        {
            return text ?? "";
        }
        return Reference.Replace(text, match =>
        {
            var key = RefOf(match);
            var card = key.Length == 0 ? null : resolve(key);
            return card is null ? match.Value : card.ToPrompt();
        });
    }

    /// <summary>Адрес из совпадения: название в скобках либо номер.</summary>
    private static string RefOf(Match match) =>
        (match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["code"].Value).Trim();

    /// <summary>
    /// Карточка объекта в том виде, в каком она уходит В ПРОМПТ. Слов-подписей здесь нет
    /// НАМЕРЕННО: текст читает то модель генерации, то агент, и язык у них разный (язык
    /// установки против языка команды, T-190) — подпись «Файлы:» пришлось бы выбирать
    /// наугад. Поэтому в промпт уходит только то, что написал человек: заголовок с
    /// названием и видом, дословный паспорт и пути эталонных файлов по одному в строке.
    /// </summary>
    /// <param name="Code">Номер объекта (OBJ-3) — по нему кадр потом ищут глазами.</param>
    /// <param name="Name">Название.</param>
    /// <param name="Kind">Название вида на языке читателя; пусто — вид не показывается.</param>
    /// <param name="Passport">Паспорт объекта: дословное описание внешности/обстановки.</param>
    /// <param name="Files">Пути эталонных файлов — своего и всех детей объекта.</param>
    public sealed record ObjectCard(string Code, string Name, string Kind, string Passport,
        IReadOnlyList<string> Files)
    {
        /// <summary>Текст подстановки; переводов строки в конце не оставляет — ссылка стоит
        /// внутри предложения не реже, чем отдельной строкой.</summary>
        public string ToPrompt()
        {
            var sb = new StringBuilder();
            sb.Append(Name.Trim().Length > 0 ? Name.Trim() : Code);
            sb.Append(Kind.Trim().Length > 0 ? $" ({Kind.Trim()}, {Code})" : $" ({Code})");
            if (Passport.Trim().Length > 0)
            {
                sb.Append('\n').Append(Passport.Trim());
            }
            foreach (var file in Files.Where(f => f.Trim().Length > 0).Distinct(StringComparer.Ordinal))
            {
                sb.Append('\n').Append(file.Trim());
            }
            return sb.ToString();
        }
    }
}
