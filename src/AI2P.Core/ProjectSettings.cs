using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКИ ПРОЕКТА (<c>projects.settings_json</c>) — та их часть, которую читает не только
/// форма проекта.
///
/// Настройки реплицируются вместе с проектом, поэтому здесь лежит только то, что одинаково
/// на всех серверах: каталог и активность пер-серверные и живут отдельной строкой
/// (<c>project_servers</c>), а не тут.
///
/// Разбор намеренно СНИСХОДИТЕЛЬНЫЙ: настройки пишут разные версии приложения и разные
/// серверы кластера, поэтому незнакомое или испорченное значение — это не ошибка, а повод
/// взять умолчание.
/// </summary>
public static class ProjectSettings
{
    /// <summary>Ключ формата ссылки на объект проекта (T-267).</summary>
    public const string ObjRefFormatKey = "objRefFormat";

    /// <summary>Ключ ответственного по умолчанию (T-5-S0).</summary>
    public const string DefaultResponsibleKey = "defaultResponsibleId";

    /// <summary>Ключ команды по умолчанию (ТЗ п. 2.7).</summary>
    public const string DefaultTeamKey = "defaultTeamId";

    /// <summary>Ключ предела подстановки опыта в промпт задания (T-29-S0).</summary>
    public const string ExperienceLimitKey = "experienceLimitChars";

    /// <summary>Ключ предела кругов «прогнал тесты → вернул в доработку → прогнал» (T-31-S0).</summary>
    public const string RecheckLimitKey = "recheckLimit";

    /// <summary>Ключ длительности задачи по умолчанию, часы (T-132-S0, диаграмма подзадач).</summary>
    public const string DefaultTaskHoursKey = "defaultTaskHours";

    /// <summary>
    /// Сколько ЧАСОВ занимает задача, у которой не указана плановая длительность
    /// (<see cref="Entities.TaskItem.PlannedHours"/>) и по которой ещё не работали, — T-132-S0.
    /// Столько же берётся у задачи вне проекта. Час выбран потому, что именно с него
    /// диаграмма подзадач начинала жизнь: квадрат такой задачи виден, но не забивает шкалу.
    /// </summary>
    public const double DefaultTaskHoursDefault = 1.0;

    /// <summary>
    /// ДЛИТЕЛЬНОСТЬ ЗАДАЧИ ПО УМОЛЧАНИЮ, часы (T-132-S0): ширина квадрата на диаграмме
    /// подзадач у задачи без плановой длительности. Настройка проекта, а не константа кода:
    /// в одном проекте шаг работы — час, в другом — рабочий день, и от этого зависит вся
    /// шкала времени диаграммы.
    ///
    /// Разбор снисходительный, как у соседей: мусор и неположительное число — повод взять
    /// умолчание (нулевая длительность означала бы квадрат нулевой ширины).
    /// </summary>
    public static double DefaultTaskHours(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return DefaultTaskHoursDefault;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            // ValueKind проверяется ОТДЕЛЬНО (наука T-29-S0): у строкового значения
            // TryGetDouble не возвращает false, а бросает InvalidOperationException
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(DefaultTaskHoursKey, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var hours)
                || double.IsNaN(hours))
            {
                return DefaultTaskHoursDefault;
            }
            return hours > 0 ? hours : DefaultTaskHoursDefault;
        }
        catch (JsonException)
        {
            return DefaultTaskHoursDefault;
        }
    }

    /// <summary>
    /// Сколько РАЗ задача-прогонщик (T-31-S0) может уйти в ожидание перезапущенных задач,
    /// если ключа нет в настройках проекта. Столько же получает задача ВНЕ проекта.
    /// Ограничитель нужен на случай, который иначе не кончается вовсе: тест падает не
    /// из-за задачи, которую перезапускают, — тогда круг «прогнал → доработка → прогнал»
    /// шёл бы вечно, каждый раз тратя задание агента.
    /// </summary>
    public const int RecheckLimitDefault = 3;

    /// <summary>
    /// Предел ОБЩЕЙ подстановки опыта в промпт задания по умолчанию (T-29-S0), знаков.
    /// Столько же получает задача ВНЕ проекта: настройки взять неоткуда, а без предела
    /// блок опыта растёт с каждым завершённым заданием и съедает контекст агента.
    /// </summary>
    public const int ExperienceLimitDefault = 100_000;

    /// <summary>
    /// ПРЕДЕЛ ПОДСТАНОВКИ ОПЫТА В ПРОМПТ (T-29-S0), знаков — суммарно по трём блокам:
    /// общие правила работы, опыт проекта, опыт узла шаблона. До этой настройки предела
    /// не было вовсе, и опыт занимал около трети контекста задания (замер T-12-S0).
    ///
    /// Контроль МЯГКИЙ: размер проверяется после каждой вставленной записи и текст записи
    /// не режется посередине — вставленное всегда немного больше предела. Резать запись
    /// нельзя: обрубок урока читается как другой урок.
    ///
    /// Значение меньше единицы (ноль, отрицательное, испорченное) — умолчание: «выключить
    /// опыт совсем» настройкой размера не делается, для этого есть отбор по навыкам и тэгам.
    /// </summary>
    public static int ExperienceLimitChars(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return ExperienceLimitDefault;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            // ValueKind проверяется ОТДЕЛЬНО: TryGetInt32 у строкового значения не
            // возвращает false, а БРОСАЕТ InvalidOperationException — на испорченной
            // настройке («много» вместо числа) это уронило бы сборку промпта задания
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(ExperienceLimitKey, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var limit))
            {
                return ExperienceLimitDefault;
            }
            return limit > 0 ? limit : ExperienceLimitDefault;
        }
        catch (JsonException)
        {
            return ExperienceLimitDefault;
        }
    }

    /// <summary>
    /// ПРЕДЕЛ КРУГОВ ПОВТОРНОЙ ПРОВЕРКИ (T-31-S0): сколько раз задача, гоняющая тесты за
    /// всю ветку, может вернуть соседей в доработку и уйти ждать их повторно. Круг — это
    /// одно ожидание: «перезапустил тех, у кого упало → ушёл в ожидание → проснулся и
    /// прогнал снова». Предел исчерпан — задача обязана закончить работу и рассказать
    /// человеку, что осталось красным.
    ///
    /// Разбор снисходительный, как у соседних настроек: мусор и неположительное число —
    /// повод взять умолчание. Ноль здесь означал бы «повторных проходов не делать вовсе»,
    /// но настройкой размера такое не выключают: для этого достаточно не звать действие.
    /// </summary>
    public static int RecheckLimit(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return RecheckLimitDefault;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            // ValueKind проверяется ОТДЕЛЬНО (наука T-29-S0): TryGetInt32 у строкового
            // значения не возвращает false, а БРОСАЕТ InvalidOperationException
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(RecheckLimitKey, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetInt32(out var limit))
            {
                return RecheckLimitDefault;
            }
            return limit > 0 ? limit : RecheckLimitDefault;
        }
        catch (JsonException)
        {
            return RecheckLimitDefault;
        }
    }

    /// <summary>Ключ умолчания «время ↔ качество» (T-23-S0).</summary>
    public const string TimeQualityKey = "timeQuality";

    /// <summary>Умолчание «время ↔ качество», когда его нет и в настройках проекта (T-23-S0).</summary>
    public const double TimeQualityDefault = 0.5;

    /// <summary>
    /// ВРЕМЯ ↔ КАЧЕСТВО ПО УМОЛЧАНИЮ (T-23-S0): значение, которое подставляется в поле новой
    /// задачи проекта и берётся у задачи, где поле не заполнено. 0.0 — минимум времени и
    /// минимум качества, 1.0 — максимум качества и максимум времени; ключа нет — 0.5.
    ///
    /// Настройка реплицируется вместе с проектом: она про сам проект («тут делаем быстро»,
    /// «тут делаем тщательно»), а не про компьютер, на котором работает исполнитель.
    ///
    /// Разбор снисходительный, как и у соседей: мусор и число за пределами отрезка не ошибка,
    /// а повод взять умолчание (значение приводится к 0..1).
    /// </summary>
    public static double TimeQuality(string? settingsJson) =>
        TimeQualityOrNull(settingsJson) ?? TimeQualityDefault;

    /// <summary>
    /// То же, но «ключа нет» отличается от «стоит 0.5»: нужно форме проекта — она обязана
    /// показать ровно то, что записано, и не приписывать проекту чужого выбора.
    /// </summary>
    public static double? TimeQualityOrNull(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(TimeQualityKey, out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var number)
                || double.IsNaN(number))
            {
                return null;
            }
            return Math.Clamp(number, 0.0, 1.0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ОТВЕТСТВЕННЫЙ ПО УМОЛЧАНИЮ (T-5-S0): исполнитель-человек, который подставляется
    /// в поле «ответственный» новой задачи проекта. Пусто (null) — как было раньше:
    /// поле новой задачи остаётся незаполненным.
    ///
    /// Настройка реплицируется вместе с проектом, потому что исполнители общие для всей
    /// организации, а не пер-серверные.
    ///
    /// Значение здесь — это ПОЖЕЛАНИЕ, а не обязательство: исполнителя могли выключить или
    /// удалить уже после того, как его записали в настройки. Поэтому проверку «жив ли он и
    /// человек ли он» делает тот, кто подставляет (форма задачи и копирование шаблона), а не
    /// разбор настроек — иначе выключенный ответственный ронял бы создание задач.
    /// </summary>
    public static string? DefaultResponsibleId(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(DefaultResponsibleKey, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var id = value.GetString();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// КОМАНДА ПО УМОЛЧАНИЮ (ТЗ п. 2.7): команда, которая подставляется в новые задачи проекта.
    /// Ключ настроек читали пять разных мест руками (форма проекта, эксплорер, список задач,
    /// вкладка команды) — здесь он собран в одно место (T-63-S0).
    ///
    /// Значение, как и у ответственного, — ПОЖЕЛАНИЕ: команду могли выключить или удалить
    /// уже после того, как её записали в настройки, поэтому годность проверяет тот, кто
    /// подставляет.
    /// </summary>
    public static string? DefaultTeamId(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(DefaultTeamKey, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var id = value.GetString();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ФОРМАТ ССЫЛКИ НА ОБЪЕКТ, которую кнопка редактора ставит в текст (T-267):
    /// <see cref="ObjectRefFormats.Code"/> — <c>@obj:OBJ-3</c>, <see cref="ObjectRefFormats.Name"/>
    /// — <c>@obj:[Герой Вася]</c>. Настройка привязана к ПРОЕКТУ: в одном проекте объекты
    /// зовут номерами (короче и не ломается при переименовании), в другом — названиями
    /// (описание кадра читается человеком целиком, без похода в список объектов).
    ///
    /// На чтение ссылок это не влияет вовсе: <see cref="ObjectRefs"/> понимает обе формы
    /// всегда — иначе смена настройки ломала бы уже написанные описания.
    /// </summary>
    public static string ObjRefFormat(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return ObjectRefFormats.Default;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(ObjRefFormatKey, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? ObjectRefFormats.Normalize(value.GetString())
                : ObjectRefFormats.Default;
        }
        catch (JsonException)
        {
            return ObjectRefFormats.Default;
        }
    }
}
