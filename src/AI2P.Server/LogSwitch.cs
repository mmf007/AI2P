using AI2P.Core.Events;
using Serilog.Core;
using Serilog.Events;
using LogLevel = AI2P.Core.Events.LogLevel;

namespace AI2P.Server;

/// <summary>
/// Уровень вывода в лог «на лету» (T-136): Настройки → Основное меняют его без перезапуска
/// приложения. Serilog умеет это только через <see cref="LoggingLevelSwitch"/> — минимальный
/// уровень, заданный при создании логгера, потом не меняется. Здесь же одной точкой
/// выставляется и фильтр журнала работ (<see cref="EventLog"/>): у обоих логов один
/// параметр настройки — <c>logging.level</c> (ТЗ п. 6.3).
/// </summary>
public static class LogSwitch
{
    /// <summary>Переключатель минимального уровня технического лога (Serilog).</summary>
    public static readonly LoggingLevelSwitch Serilog = new(LogEventLevel.Warning);

    /// <summary>Применить уровень из настроек: технический лог + журнал работ.
    /// Возвращает разобранное значение (для записи обратно в config.json).</summary>
    public static LogLevel Apply(string? level)
    {
        var parsed = EventLog.Parse(level);
        EventLog.Level = parsed;
        Serilog.MinimumLevel = parsed switch
        {
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Info => LogEventLevel.Information,
            _ => LogEventLevel.Warning,
        };
        return parsed;
    }
}
