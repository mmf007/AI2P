namespace AI2P.Core;

/// <summary>
/// ПРАВИЛА АРХИВАЦИИ (T-41-S0, выпуск 1.105) — коды полей записи правила, общие на все слои.
///
/// Правило отвечает на один вопрос: «что и какой давности пора уносить в архив». Групп
/// правил две, а СОСТАВ ПОЛЕЙ у них один и тот же:
/// <list type="bullet">
/// <item><b>общие</b> — правила самой организации (закладка «Настройки → Справочники»).
/// Это ОБРАЗЕЦ: с них копируются правила нового архива;</item>
/// <item><b>правила архива</b> — свои у каждого архива, правятся с его записи.</item>
/// </list>
/// Отдельной колонки «группа» нет намеренно: признак производный — у общего правила ссылки
/// на архив нет, у правила архива есть (ровно так же устроен ТРЕТИЙ вид записей опыта,
/// T-11-S0, и по той же причине — колонка-признак разъезжается со ссылкой молча).
/// </summary>
public static class ArchiveRuleTargets
{
    /// <summary>Задачи. Единственный вид, у которого спрашивается СОСТОЯНИЕ задачи.</summary>
    public const string Tasks = "tasks";

    /// <summary>Шаблоны задач (те же строки <c>tasks</c> с признаком шаблона).</summary>
    public const string Templates = "templates";

    /// <summary>Объекты проектов (персонажи, локации, эталонные кадры, адаптеры LoRA).</summary>
    public const string Objects = "objects";

    /// <summary>Записи опыта — проекта, узла шаблона и общие правила организации.</summary>
    public const string Experience = "experience";

    /// <summary>Правила безопасности.</summary>
    public const string Security = "security";

    /// <summary>Журнал событий (логи).</summary>
    public const string Logs = "logs";

    public static readonly string[] All =
        [Tasks, Templates, Objects, Experience, Security, Logs];

    /// <summary>Вид с ИЕРАРХИЕЙ: у записи бывают потомки, и архивируется она только целиком
    /// (см. <see cref="ArchiveRuleAges"/> и сервис отбора кандидатов). Таких два — задачи
    /// и шаблоны, обе живут строками одной таблицы <c>tasks</c>.</summary>
    public static bool IsHierarchical(string target) => target is Tasks or Templates;

    /// <summary>Состояние задачи спрашивается ТОЛЬКО у правил про задачи (по заданию);
    /// у остальных видов вместо него — активность данных (<see cref="ArchiveRuleActivity"/>).</summary>
    public static bool HasTaskStatus(string target) => target == Tasks;

    public static bool IsKnown(string? target) => target is not null && All.Contains(target);
}

/// <summary>
/// ПО КАКОЙ ДАТЕ СЧИТАТЬ ВОЗРАСТ записи (T-41-S0). Разница существенная: задача, заведённая
/// год назад и правившаяся вчера, по дате создания в архив уходит, а по дате изменения — нет.
/// </summary>
public static class ArchiveRuleDates
{
    /// <summary>Дата создания записи.</summary>
    public const string Created = "created";

    /// <summary>Дата последнего изменения записи.</summary>
    public const string Updated = "updated";

    public static readonly string[] All = [Created, Updated];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// СРОК ПРАВИЛА (T-41-S0) — «временной» или «календарный».
/// </summary>
public static class ArchiveRuleAges
{
    /// <summary>ВРЕМЕННОЙ срок: число ДНЕЙ от текущей даты.</summary>
    public const string Days = "days";

    /// <summary>КАЛЕНДАРНЫЙ срок: число месяцев и лет от текущей даты. Месяцы не указаны —
    /// учитываются только года.</summary>
    public const string Calendar = "calendar";

    /// <summary>
    /// ВОЗРАСТ НЕ ВАЖЕН (T-265-S0): правило отбирает записи независимо от их давности —
    /// решают только вид данных и активность. Понадобилось точному требованию заказчика
    /// «архивировать ВСЕ неактивные записи опыта»: неактивная запись не нужна ни сегодня,
    /// ни через год, и ждать от неё срока не за чем. Числа у такого правила нет вовсе.
    /// </summary>
    public const string Any = "any";

    public static readonly string[] All = [Days, Calendar, Any];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// АКТИВНОСТЬ ОСТАЛЬНЫХ ДАННЫХ (T-41-S0) — то, что у задач заменяет состояние. «Активна»
/// значит «не удалена и не выключена»: у объектов и правил справочников для этого есть
/// колонка <c>is_active</c>, у записей опыта и логов её нет вовсе — там активность
/// определяется только тем, жива запись или помечена удалённой.
/// </summary>
public static class ArchiveRuleActivity
{
    /// <summary>Любая — активность не проверяется.</summary>
    public const string Any = "any";

    /// <summary>Только активные записи.</summary>
    public const string Active = "active";

    /// <summary>Только неактивные (выключенные либо помеченные удалёнными).</summary>
    public const string Inactive = "inactive";

    public static readonly string[] All = [Any, Active, Inactive];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}

/// <summary>
/// СРОК ПРАВИЛА В ВИДЕ ДАТЫ (T-41-S0). Вынесен из сервиса отдельной чистой функцией
/// намеренно: это единственное место, где «3 месяца и 2 года» превращаются в границу
/// на календаре, и проверить его можно без базы вовсе.
/// </summary>
public static class ArchiveAge
{
    /// <summary>
    /// Граничная дата: запись, у которой выбранная дата НЕ ПОЗЖЕ границы, по возрасту
    /// подходит. Временной срок отсчитывается днями (<paramref name="days"/>), календарный —
    /// месяцами и годами; месяцы не указаны (ноль) — учитываются только года.
    /// </summary>
    /// <param name="now">Текущий момент (UTC) — передаётся, а не берётся внутри, чтобы
    /// проверку можно было написать на любой дате, а не на сегодняшней.</param>
    public static DateTime Threshold(string ageKind, int days, int months, int years, DateTime now) =>
        ageKind == ArchiveRuleAges.Any
            // «возраст не важен» (T-265-S0): граница уносится в бесконечность, и по возрасту
            // подходит ЛЮБАЯ запись. Именно дата, а не отдельная ветка в запросе: условие
            // отбора остаётся одно на все виды срока, и ошибиться в нём негде
            ? DateTime.MaxValue
            : ageKind == ArchiveRuleAges.Calendar
            // AddMonths(0) — тождество, поэтому «месяцы не указаны» отдельной веткой писать
            // не надо: остаются одни года, ровно как сказано в задании
            ? now.AddYears(-Math.Max(0, years)).AddMonths(-Math.Max(0, months))
            : now.AddDays(-Math.Max(0, days));
}
