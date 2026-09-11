using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// СТОРОЖ СРОКОВ (T-272): раз в минуту спрашивает <see cref="NotificationService.CheckDueAsync"/>,
/// не подошёл ли срок у задач этого сервера. Два других события уведомлений сторожа не
/// требуют вовсе — они приходят сменой статуса задачи.
///
/// Сторож работает на КАЖДОМ сервере кластера, а не только на дирижёре: отбор «своих» задач
/// делает сам сервис (<see cref="Storage.Services.TaskService.CanWrite(Core.Entities.TaskItem)"/>),
/// иначе не-дирижёр не уведомлял бы о сроках собственных задач — ровно та ошибка, которую
/// уже проходили с расписаниями (ScheduleRunner, этап 41).
/// </summary>
public sealed class NotificationRunner : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", nameof(NotificationRunner));

    /// <summary>Как часто просыпаться. Минута — та же частота, что у расписаний: письмо
    /// «подходит срок» на минуту позже никого не подводит, а чаще будить базу незачем.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly NotificationService _notifications;
    private Timer? _timer;
    private int _busy;

    public NotificationRunner(NotificationService notifications) => _notifications = notifications;

    public void Start() => _timer ??= new Timer(_ => Tick(), null, Interval, Interval);

    private void Tick()
    {
        // проход может затянуться на почтовом сервере — второй одновременно не начинаем
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var sent = await _notifications.CheckDueAsync();
                if (sent > 0)
                {
                    Logger.Information("Уведомления по сроку: отправлено {Count}", sent);
                }
            }
            catch (Exception ex)
            {
                // сторож обязан пережить любую беду: иначе одна ошибка выключает
                // уведомления до перезапуска приложения, и это никак не видно
                Logger.Warning("Сторож уведомлений: {Reason}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
