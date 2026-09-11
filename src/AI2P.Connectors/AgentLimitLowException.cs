namespace AI2P.Connectors;

/// <summary>
/// У ИИ-исполнителя почти не осталось лимита за окно (T-121), поэтому задание не запущено,
/// а старт задачи перенесён на <see cref="StartAfter"/> — момент, когда окно освободится.
/// Предупреждение с тем же текстом пишется в чат задачи. Запуск «всё равно сейчас» —
/// повторный вызов с force.
/// </summary>
public sealed class AgentLimitLowException : InvalidOperationException
{
    public AgentLimitLowException(string message, DateTime startAfter) : base(message) =>
        StartAfter = startAfter;

    /// <summary>Момент (UTC), на который перенесён старт задачи.</summary>
    public DateTime StartAfter { get; }
}
