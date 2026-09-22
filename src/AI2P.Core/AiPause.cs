using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// Почему работа ИИ-агента над задачей сейчас не идёт (T-187). Ожидание бывает трёх видов,
/// и человеку важно разное: при лимите — когда работа продолжится сама, при вопросе — сколько
/// вопросов висит в чате, при разбиении — что ждать нечего, кроме подзадач.
/// </summary>
public static class AiPauseKinds
{
    /// <summary>Кончился лимит исполнителя (T-121, T-166): старт перенесён, работа продолжится
    /// сама — в <see cref="AiPauseDto.Until"/> лежит время следующего запуска.</summary>
    public const string Limit = "limit";

    /// <summary>Агент задал вопрос и ждёт ответа человека или агента другой задачи
    /// (ТЗ v1.17, T-185): в <see cref="AiPauseDto.Questions"/> — сколько вопросов висит в чате.</summary>
    public const string Question = "question";

    /// <summary>Задача разошлась на подзадачи и ждёт их завершения (T-185, авторазбиение ТЗ v1.26).</summary>
    public const string Subtasks = "subtasks";

    /// <summary>По задаче нажата кнопка «запустить всю иерархию» (T-159), и очередь запуска
    /// её поддерева ещё открыта (T-224): работа идёт снизу вверх, сама задача ждёт своей
    /// очереди — статус у неё так и остаётся «ожидает», и без пометки это выглядит как
    /// «нажал, и ничего не происходит».</summary>
    public const string Hierarchy = "hierarchy";

    /// <summary>Задача ждёт завершения блокирующих задач (ТЗ п. 2.12, T-6-S1): пока они
    /// не переведены в «готово», ни автозапуск, ни очередь иерархии её не берут — и без
    /// пометки это выглядит как «очередь идёт мимо задачи без всякой причины».</summary>
    public const string Blocked = "blocked";

    /// <summary>Блокирующая задача ОТМЕНЕНА (T-6-S1): работа, которой ждала эта задача,
    /// не состоится, поэтому автоматически её больше не запустят, а очередь иерархии
    /// остановлена. Дальше решает человек: снять блокировку, вернуть блокирующую в работу
    /// или запустить задачу вручную.</summary>
    public const string BlockerCancelled = "blocker-cancelled";

    /// <summary>Задача типа «цикл» (T-299-S0): условия проверены, идёт круг тела цикла —
    /// потомки выполняются, а сама задача стоит на паузе и ждёт окончания цикла.</summary>
    public const string Loop = "loop";
}

/// <summary>Причина паузы одной строкой данных (T-187): вид + то, что к нему относится.</summary>
public sealed class AiPauseDto
{
    /// <summary>Вид ожидания (<see cref="AiPauseKinds"/>).</summary>
    public string Kind { get; set; } = "";

    /// <summary>Время следующего запуска (только для лимита); UTC.</summary>
    public DateTime? Until { get; set; }

    /// <summary>Сколько вопросов висит в чате задачи (только для вопроса).</summary>
    public int Questions { get; set; }
}

/// <summary>
/// Причина паузы задачи, над которой работает ИИ (T-187). Считается В ОДНОМ месте, потому
/// что показывается в двух: в представлении «в работе у ИИ» и в форме задачи за статусом —
/// раньше форма знала только про лимит, а список не знал ничего.
/// </summary>
public static class AiPause
{
    /// <summary>
    /// Чего ждёт работа над задачей; null — не ждёт ничего, агент работает.
    /// </summary>
    /// <param name="waiting">Есть приостановленное задание (job в waiting_human).</param>
    /// <param name="waitKind">Чего оно ждёт (<see cref="JobWaitKinds"/>); пусто — ответа человека,
    /// как было до T-185.</param>
    /// <param name="startAfter">Отложенный старт задачи (T-121); UTC.</param>
    /// <param name="pendingQuestions">Висящие вопросы в чате задачи.</param>
    /// <param name="nowUtc">Текущий момент.</param>
    /// <param name="hierarchyRun">Задача ждёт своей очереди в открытом иерархическом запуске
    /// (T-224): у неё стоит флаг runHierarchy, а живого задания сейчас нет. Это самая слабая
    /// причина — она называется только тогда, когда других ожиданий у задачи нет.</param>
    /// <param name="blockers">Состояние блокирующих задач (T-6-S1); считается только для
    /// задачи, работа над которой ещё не начата, — у начатой причина ожидания всегда в её
    /// задании. «Ждём» и «отменена» сильнее очереди иерархии: очередь как раз и стоит
    /// из-за них.</param>
    public static AiPauseDto? Of(bool waiting, string waitKind, DateTime? startAfter,
        int pendingQuestions, DateTime nowUtc, bool hierarchyRun = false,
        BlockersState blockers = BlockersState.Done, bool loopBody = false)
    {
        // лимит идёт первым: он определяет, КОГДА работа продолжится, — даже если у задания
        // при этом остался висеть вопрос, ответ на него агент прочитает только после сброса окна
        if (startAfter is { } at && at > nowUtc)
        {
            return new AiPauseDto { Kind = AiPauseKinds.Limit, Until = at };
        }
        if (!waiting)
        {
            // идёт круг тела цикла (T-299-S0): задача на паузе ждёт своих потомков
            if (loopBody)
            {
                return new AiPauseDto { Kind = AiPauseKinds.Loop };
            }
            // блокирующие задачи (T-6-S1) называются раньше очереди иерархии: очередь дошла
            // до задачи и стоит именно из-за них, а отменённая блокирующая ещё и закрыла
            // очередь — иначе человек видел бы «запуск иерархии» у задачи, которая не пойдёт
            if (blockers == BlockersState.Cancelled)
            {
                return new AiPauseDto { Kind = AiPauseKinds.BlockerCancelled };
            }
            if (blockers == BlockersState.Waiting)
            {
                return new AiPauseDto { Kind = AiPauseKinds.Blocked };
            }
            // ждать нечего, но очередь иерархии открыта (T-224): задача стоит в ней и
            // дождётся своего запуска сама — человеку важно, что нажатие кнопки не пропало
            return hierarchyRun ? new AiPauseDto { Kind = AiPauseKinds.Hierarchy } : null;
        }
        if (waitKind == JobWaitKinds.Subtasks)
        {
            return new AiPauseDto { Kind = AiPauseKinds.Subtasks };
        }
        // human, agent и пустое значение (задания старше T-185) — это вопрос в чате
        return new AiPauseDto { Kind = AiPauseKinds.Question, Questions = pendingQuestions };
    }
}
