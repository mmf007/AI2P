namespace AI2P.Core.Entities;

/// <summary>
/// Статус задачи (ТЗ п. 2.1) — строковый код из справочника состояний (ТЗ v1.37):
/// встроенные коды перечислены константами, пользователи добавляют кастомные через UI.
/// Встроенная логика (автозапуск, блокировки, доска) опирается только на встроенные коды.
/// </summary>
public static class TaskStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    /// <summary>
    /// «Пауза» (T-185): работа над задачей начата и НЕ закончена, но сейчас не идёт — агент
    /// висит в ожидании внешнего события и своей сессии не теряет. Ждать можно три вещи:
    /// ответа человека на вопрос агента (ТЗ v1.17), ответа агента другой задачи (T-185)
    /// и завершения подзадач, на которые задача разошлась (авторазбиение, ТЗ v1.26).
    /// Сюда же попадает задача, у исполнителя которой кончился лимит (T-121): работа не
    /// потеряна, старт перенесён на сброс окна.
    ///
    /// До T-185 состояние называлось «ждёт ответа» (код <c>waiting_reply</c>) и означало
    /// только вопрос человеку; старые данные переводит на новый код шаг обновления билда 80.
    /// </summary>
    public const string Paused = "paused";
    /// <summary>Прежний код состояния «ждёт ответа» (до T-185) — нужен только миграции данных
    /// и чтению старых записей журнала; в новой логике не используется.</summary>
    public const string LegacyWaitingReply = "waiting_reply";
    /// <summary>«Встал с ошибкой» (ТЗ v1.14): ИИ-агент прервался с ошибкой.</summary>
    public const string Error = "error";
    public const string Review = "review";
    public const string NeedsFix = "needs_fix";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    /// <summary>Встроенные коды в базовом порядке (порядок сида справочника).</summary>
    public static readonly string[] BuiltIn =
        [Draft, Pending, InProgress, Paused, Error, Review, NeedsFix, Done, Cancelled];

    /// <summary>«Завершена» — работы по задаче больше не будет: done или cancelled.
    /// ВНИМАНИЕ (T-6-S1): для БЛОКИРУЮЩИХ задач эти два исхода с версии 1.88 разные —
    /// «готово» отпускает ждущие задачи, «отмена» держит их навсегда
    /// (<see cref="BlockersState"/>).</summary>
    public static bool Finished(string status) => status is Done or Cancelled;

    /// <summary>«Работа сдана» (T-2-S0): проверка, готово, отмена — задачу больше не запускают
    /// ни очередь иерархического запуска (T-159), ни автозапуск. Отсюда следует и то, что такая
    /// задача не показывается в представлении «в работе у ИИ»: агент ею не занят.
    /// <para>От <see cref="Finished"/> отличается «проверкой»: та означает «работа сделана и ждёт
    /// человека» — вернуть задачу в работу может только он, сама она с места не тронется.</para></summary>
    public static bool Settled(string status) => status is Review or Done or Cancelled;
}

/// <summary>
/// Состояние блокирующих задач у одной задачи (T-6-S1, ТЗ п. 2.12). До версии 1.88 их было
/// два («все завершены» / «ждём»), причём отменённая блокирующая считалась завершённой и
/// отпускала ждущие задачи. Теперь исходов три, и отмена — отдельный: она означает, что
/// работа по ветке не состоится, поэтому ждущие задачи не запускаются вовсе, а открытая
/// очередь иерархического запуска (T-159) останавливается.
/// </summary>
public enum BlockersState
{
    /// <summary>Блокирующих нет либо все переведены в «готово» — задачу можно запускать.</summary>
    Done,

    /// <summary>Хотя бы одна блокирующая ещё в работе — запуск откладывается до её завершения.</summary>
    Waiting,

    /// <summary>Хотя бы одна блокирующая ОТМЕНЕНА — автоматического запуска не будет
    /// (ни очередью иерархии, ни автозапуском потомков), пока человек не поправит
    /// список блокирующих или состояние самой блокирующей.</summary>
    Cancelled,
}

/// <summary>
/// Состояние задачи из справочника состояний (ТЗ v1.37, настройки → справочники):
/// внутренний код (у встроенных не меняется), локализуемое название, цвет надписи,
/// признак кастомного, активность и порядковый номер (доска и выпадающие списки).
/// </summary>
public sealed class TaskStatusDef
{
    /// <summary>Внутренний код — ключ таблицы tasks.status; у встроенных менять нельзя.</summary>
    public string Id { get; set; } = "";
    /// <summary>Название на языке из настроек; правки пользователя хранятся по языкам.</summary>
    public string Name { get; set; } = "";
    /// <summary>Цвет фона надписи состояния (#rrggbb).</summary>
    public string Color { get; set; } = "#9e9e9e";
    /// <summary>false — встроенное, true — добавлено пользователем; менять можно только в debug.</summary>
    public bool IsCustom { get; set; } = true;
    /// <summary>Неактивное состояние не предлагается в списках и не показывается колонкой доски.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Порядковый номер: сортировка доски и выпадающих списков; встроенные — через 10.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Вид записи в tasks: одиночная задача или бизнес-процесс (ТЗ пп. 2.1, 2.3).</summary>
public enum TaskKind
{
    Task,
    Process,
}

/// <summary>Тип исполнителя (ТЗ п. 2.2).</summary>
public enum ExecutorKind
{
    Human,
    Ai,

    /// <summary>
    /// АВТО ПО (T-153-S0) — обычная программа длительной работы: обучение LoRA, кодирование
    /// видео, расчёт. Ни человек, ни агент: промпта она не читает, токенов не тратит, лимитов
    /// провайдера у неё нет, и стоит она на КОНКРЕТНОМ компьютере (сервер обязателен).
    /// Автоподбором такой исполнитель не назначается НИКОГДА (см. ExecutorPickService):
    /// его назначает человек, ИИ-агент или специальный алгоритм («запуск обучения LoRA»).
    /// </summary>
    Software,
}

/// <summary>Роль в системе — права доступа (ТЗ п. 2.2).</summary>
public enum SystemRole
{
    Owner,
    Admin,
    ProjectAdmin,
    Editor,
    Reader,
}

/// <summary>Состояние задания в очереди (ТЗ п. 6.4.2, таблица jobs).</summary>
public enum JobState
{
    Queued,
    Running,
    WaitingHuman,
    Done,
    Failed,
    Cancelled,
}

/// <summary>
/// Преобразование enum ↔ строка БД. В БД и API — snake_case строки из ТЗ
/// (draft / in_progress / waiting_human / project_admin …).
/// </summary>
public static class EnumMap
{
    public static string ToDb(this TaskKind v) => v == TaskKind.Process ? "process" : "task";

    public static TaskKind TaskKindFromDb(string s) => s == "process" ? TaskKind.Process : TaskKind.Task;

    public static string ToDb(this ExecutorKind v) => v switch
    {
        ExecutorKind.Ai => "ai",
        ExecutorKind.Software => "software",
        _ => "human",
    };

    /// <summary>Неизвестная строка (запись приехала от партнёра будущей версии) читается как
    /// «человек»: так строка не пропадает и работой её никто автоматически не займёт.</summary>
    public static ExecutorKind ExecutorKindFromDb(string s) => s switch
    {
        "ai" => ExecutorKind.Ai,
        "software" => ExecutorKind.Software,
        _ => ExecutorKind.Human,
    };

    public static string ToDb(this SystemRole v) => v switch
    {
        SystemRole.Owner => "owner",
        SystemRole.Admin => "admin",
        SystemRole.ProjectAdmin => "project_admin",
        SystemRole.Editor => "editor",
        SystemRole.Reader => "reader",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static SystemRole SystemRoleFromDb(string s) => s switch
    {
        "owner" => SystemRole.Owner,
        "admin" => SystemRole.Admin,
        "project_admin" => SystemRole.ProjectAdmin,
        "editor" => SystemRole.Editor,
        "reader" => SystemRole.Reader,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, Loc.T("msg.enums.1")),
    };

    public static string ToDb(this JobState v) => v switch
    {
        JobState.Queued => "queued",
        JobState.Running => "running",
        JobState.WaitingHuman => "waiting_human",
        JobState.Done => "done",
        JobState.Failed => "failed",
        JobState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public static JobState JobStateFromDb(string s) => s switch
    {
        "queued" => JobState.Queued,
        "running" => JobState.Running,
        "waiting_human" => JobState.WaitingHuman,
        "done" => JobState.Done,
        "failed" => JobState.Failed,
        "cancelled" => JobState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, Loc.T("msg.enums.2")),
    };
}
