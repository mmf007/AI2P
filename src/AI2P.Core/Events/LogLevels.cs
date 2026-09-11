namespace AI2P.Core.Events;

/// <summary>
/// Уровень вывода в лог (T-136): Настройки → Основное, параметр <c>logging.level</c>
/// в <c>config.json</c>. Одно значение управляет ОБОИМИ логами системы (ТЗ п. 6.3):
///
/// <list type="bullet">
///   <item>техническим логом приложения (Serilog, <c>logs/ai2p-*.jsonl</c>) — как
///     минимальный уровень сообщений;</item>
///   <item>журналом работ (события в БД, UI «История работ») — объёмные технические
///     события на верхних уровнях не пишутся (<see cref="EventLog"/>).</item>
/// </list>
///
/// Уровней ровно три, и означают они следующее:
/// <list type="bullet">
///   <item><b>Debug</b> — пишется всё;</item>
///   <item><b>Info</b> — без самых объёмных событий (вызовы инструментов агента
///     <c>agent.tool_calls</c>, диагностика подключения участников команды);</item>
///   <item><b>Warning</b> — самое необходимое: доменная история (задачи, задания, чат,
///     аккаунты, серверы) и ошибки; технические записи каждого вызова модели не пишутся.
///     Это значение — <b>по умолчанию</b>.</item>
/// </list>
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
}

/// <summary>
/// Фильтр журнала работ по уровню логирования (T-136). Значение общее на процесс —
/// как <c>Log.Logger</c> у Serilog: событие пишется из десятков мест (хранилище,
/// коннекторы, импорт), протаскивать настройку в каждое из них смысла нет.
/// Умолчание здесь — <see cref="LogLevel.Debug"/> («пишем всё»): приложение выставляет
/// уровень из <c>config.json</c> при старте и при сохранении настроек, а в тестах и
/// служебных утилитах ничего не теряется молча.
/// </summary>
public static class EventLog
{
    /// <summary>Текущий уровень; выставляется приложением из настроек.</summary>
    public static LogLevel Level { get; set; } = LogLevel.Debug;

    /// <summary>Разобрать значение <c>logging.level</c> (Debug / Info / Information /
    /// Warning); неизвестное — <see cref="LogLevel.Warning"/> (умолчание системы).</summary>
    public static LogLevel Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "debug" or "verbose" or "trace" => LogLevel.Debug,
        "info" or "information" => LogLevel.Info,
        _ => LogLevel.Warning,
    };

    /// <summary>Имя уровня для <c>config.json</c> и UI.</summary>
    public static string Name(LogLevel level) => level switch
    {
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        _ => "Warning",
    };

    /// <summary>Самые объёмные события: вызовы инструментов агента (в payload — весь лог
    /// вызовов) и диагностика подключения участников команды. Не пишутся с уровня Info.</summary>
    private static readonly HashSet<string> BulkEvents =
    [
        EventTypes.AgentToolCalls,
        EventTypes.TeamMemberState,
    ];

    /// <summary>Технические события каждого вызова модели: на уровне Warning в журнале
    /// остаётся доменная история (что стало с задачей и заданием), а не протокол обмена.</summary>
    private static readonly HashSet<string> TechnicalEvents =
    [
        EventTypes.AgentRequest,
        EventTypes.AgentResponse,
        EventTypes.JobStateChanged,
        EventTypes.CycleIteration,
    ];

    /// <summary>Писать ли событие такого типа при текущем уровне (T-136).</summary>
    public static bool Allows(string eventType) => Allows(eventType, Level);

    /// <summary>То же с явным уровнем — для проверок и тестов.</summary>
    public static bool Allows(string eventType, LogLevel level) => level switch
    {
        LogLevel.Debug => true,
        LogLevel.Info => !BulkEvents.Contains(eventType),
        _ => !BulkEvents.Contains(eventType) && !TechnicalEvents.Contains(eventType),
    };
}
