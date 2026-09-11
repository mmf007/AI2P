using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;
using System.Globalization;
using System.Text.Json;

namespace AI2P.Connectors;

/// <summary>
/// ОТПРАВКА УВЕДОМЛЕНИЙ (T-272): по событию задачи собрать письма из подходящих правил
/// и отдать их транспорту.
///
/// ГДЕ ЭТО ПРОИСХОДИТ. Правила реплицируются и видны всем серверам организации, поэтому
/// «кто шлёт» надо решать явно, иначе одно событие дало бы столько писем, сколько в кластере
/// серверов. Решение: шлёт ВЛАДЕЛЕЦ ЗАДАЧИ (<see cref="TaskService.CanWrite(TaskItem)"/>) —
/// тот сервер, на котором событие и произошло. То же правило у сторожа сроков
/// (<see cref="DueMessages"/>): он смотрит только свои задачи.
///
/// ЧТО СЧИТАЕТСЯ СОБЫТИЕМ (задание T-272):
/// <list type="bullet">
/// <item><b>задача переведена в состояние</b> — письмо ИСПОЛНИТЕЛЮ задачи; состояние
/// выбирается в правиле;</item>
/// <item><b>требуется проверка</b> — письмо ОТВЕТСТВЕННОМУ; это переход в «проверку»;</item>
/// <item><b>подходит срок</b> — письмо ИСПОЛНИТЕЛЮ за указанное время до <c>due_date</c>.</item>
/// </list>
///
/// НА ДУБЛИ ВНИМАНИЯ НЕ ОБРАЩАЕМ (прямо по заданию): два подходящих правила — два письма.
/// Единственная защита от повтора — у события «подходит срок», и она нужна не от дублей,
/// а от того, что сторож просыпается раз в минуту (<see cref="NotificationRuleService.MarkSent"/>).
/// </summary>
public sealed class NotificationService
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", nameof(NotificationService));

    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly TaskStatusService _statuses;
    private readonly NotificationRuleService _rules;
    private readonly EventStore _events;
    private readonly Func<Organization> _org;
    private readonly Func<string> _publicBaseUrl;
    private readonly Dictionary<string, INotificationTransport> _transports;

    public NotificationService(TaskService tasks, ExecutorService executors,
        ProjectService projects, TaskStatusService statuses, NotificationRuleService rules,
        EventStore events, Func<Organization> org, Func<string> publicBaseUrl,
        params INotificationTransport[] transports)
    {
        _tasks = tasks;
        _executors = executors;
        _projects = projects;
        _statuses = statuses;
        _rules = rules;
        _events = events;
        _org = org;
        _publicBaseUrl = publicBaseUrl;
        _transports = transports.ToDictionary(t => t.Transport, StringComparer.OrdinalIgnoreCase);
        _tasks.StatusChanged += OnTaskStatusChanged;
    }

    /// <summary>Транспорт по коду вида уведомления; null — такого транспорта здесь нет.</summary>
    public INotificationTransport? TransportFor(string channel) =>
        _transports.TryGetValue(NotificationChannels.TransportOf(channel), out var transport)
            ? transport
            : null;

    // --- события ---

    private void OnTaskStatusChanged(TaskItem task, string oldStatus, string? actorId)
    {
        _ = oldStatus;
        if (task.IsTemplate || !_tasks.CanWrite(task))
        {
            // шлёт владелец задачи: то же изменение приедет репликацией на все серверы
            // организации, и без этого условия письмо ушло бы с каждого из них
            return;
        }
        var messages = StatusMessages(task);
        if (messages.Count == 0)
        {
            return;
        }
        // фоном: смена статуса не должна ждать почтового сервера
        _ = Task.Run(() => SendAsync(messages, actorId));
    }

    /// <summary>
    /// Письма по смене состояния задачи: правила «переведена в состояние» (исполнителям)
    /// и «требуется проверка» (ответственному). Метод чистый — ничего не отправляет,
    /// поэтому его же зовут проверки.
    /// </summary>
    public List<NotificationMessage> StatusMessages(TaskItem task)
    {
        var messages = new List<NotificationMessage>();
        // ЗАДАЧУ ПЕРЕЧИТЫВАЕМ. Событие смены статуса приносит запись, собранную по одной
        // таблице tasks: связи (исполнители, запасные) лежат в своих таблицах и подгружаются
        // только в Get/List. Без перечитывания список исполнителей пуст, и письмо «задача
        // переведена в состояние» не уходило бы никому — притом что «требуется проверка»
        // работало бы (ответственный — колонка самой задачи), и беда выглядела бы выборочной
        task = _tasks.Get(task.Id) ?? task;
        foreach (var rule in _rules.Match(NotificationEvents.Status, task.ProjectId)
                     .Where(r => r.StatusCode == task.Status))
        {
            Collect(messages, rule, task, Performers(task));
        }
        if (task.Status == TaskStatuses.Review && task.ResponsibleId is { Length: > 0 } responsible)
        {
            foreach (var rule in _rules.Match(NotificationEvents.Review, task.ProjectId))
            {
                Collect(messages, rule, task, [responsible]);
            }
        }
        return messages;
    }

    /// <summary>
    /// Письма «подходит срок» на момент <paramref name="nowUtc"/>: у задачи задан срок,
    /// работа по ней ещё не закончена, и до срока осталось не больше указанного в правиле.
    /// Отметку об отправке метод НЕ ставит — это делает <see cref="CheckDueAsync"/>, иначе
    /// проверка не смогла бы посмотреть на результат отбора.
    /// </summary>
    public List<NotificationMessage> DueMessages(DateTime nowUtc)
    {
        var messages = new List<NotificationMessage>();
        var rules = _rules.Match(NotificationEvents.Due, projectId: null);
        if (rules.Count == 0)
        {
            return messages;
        }
        foreach (var task in _tasks.List(includeTemplates: false))
        {
            if (task.DueDate is not { } due || !_tasks.CanWrite(task) || IsClosed(task.Status))
            {
                continue;
            }
            foreach (var rule in rules)
            {
                if (rule.ProjectIds.Count > 0
                    && (task.ProjectId is not { Length: > 0 } || !rule.ProjectIds.Contains(task.ProjectId)))
                {
                    continue;
                }
                if (due - TimeSpan.FromMinutes(rule.LeadMinutes) > nowUtc)
                {
                    continue;   // срок ещё не подходит
                }
                if (_rules.AlreadySent(rule.Id, task.Id, Sql.ToDb(due)))
                {
                    continue;   // уже уведомляли по ЭТОМУ сроку; сдвинут — уведомим заново
                }
                Collect(messages, rule, task, Performers(task));
            }
        }
        return messages;
    }

    /// <summary>Проход сторожа сроков: собрать, отправить, запомнить отправленное.</summary>
    public async Task<int> CheckDueAsync(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var messages = DueMessages(now);
        if (messages.Count == 0)
        {
            return 0;
        }
        // отметка ставится ДО отправки — по науке заявок на запуск (T-196): иначе отказ
        // почтового сервера крутил бы одно и то же письмо каждую минуту без конца
        foreach (var message in messages)
        {
            var due = message.Due is { } value ? Sql.ToDb(value) : "";
            _rules.MarkSent(message.RuleId, message.TaskId, due);
        }
        return await SendAsync(messages, actorId: null);
    }

    // --- отправка ---

    /// <summary>
    /// Отправить собранные уведомления. Возвращает число доставленных. Отказ по одному
    /// письму не мешает остальным: причина пишется в журнал работ и в лог, а не наверх —
    /// уведомление не должно ломать ту работу, о которой оно уведомляет.
    /// </summary>
    public async Task<int> SendAsync(IReadOnlyList<NotificationMessage> messages, string? actorId)
    {
        var sent = 0;
        foreach (var message in messages)
        {
            try
            {
                var transport = TransportFor(message.Channel)
                                ?? throw new InvalidOperationException(
                                    Loc.T("msg.notify.9", message.Transport));
                await transport.SendAsync(message);
                sent++;
                Journal(EventTypes.NotificationSent, message, actorId, error: "");
                Logger.Information(
                    "Уведомление {Rule} по задаче {Task} отправлено: {Channel} → {Executor}",
                    message.RuleDisplayId, message.TaskDisplayId, message.Channel, message.ExecutorNick);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException
                                           or System.Net.Mail.SmtpException or IOException
                                           or System.Net.Sockets.SocketException
                                           or ArgumentException or TimeoutException)
            {
                Journal(EventTypes.NotificationFailed, message, actorId, ex.Message);
                Logger.Warning("Уведомление {Rule} по задаче {Task} не отправлено: {Reason}",
                    message.RuleDisplayId, message.TaskDisplayId, ex.Message);
            }
        }
        return sent;
    }

    // --- сборка письма ---

    /// <summary>Исполнители задачи: назначенный и тот, кто реально её ведёт (замена, T-221).</summary>
    private static List<string> Performers(TaskItem task)
    {
        var performers = new List<string>(task.ExecutorIds);
        if (task.ActualExecutorId is { Length: > 0 } actual && !performers.Contains(actual))
        {
            performers.Add(actual);
        }
        return performers;
    }

    /// <summary>Работа по задаче закончена — сроком её больше не тревожим.</summary>
    private static bool IsClosed(string status) =>
        status is TaskStatuses.Done or TaskStatuses.Cancelled;

    private void Collect(List<NotificationMessage> messages, NotificationRule rule,
        TaskItem task, IReadOnlyList<string> recipients)
    {
        foreach (var executorId in recipients)
        {
            if (!NotificationRuleService.Covers(rule, executorId))
            {
                continue;
            }
            if (_executors.Get(executorId) is not { } executor)
            {
                continue;
            }
            var context = Context(task, executor);
            messages.Add(new NotificationMessage
            {
                RuleId = rule.Id,
                RuleDisplayId = rule.DisplayId,
                Channel = rule.Channel,
                Transport = NotificationChannels.TransportOf(rule.Channel),
                Address = _executors.NotifyAddressOf(executor),
                ExecutorId = executor.Id,
                ExecutorNick = executor.Nick,
                Subject = NotificationMacros.Expand(rule.Subject, context),
                Body = NotificationMacros.Expand(rule.Body, context),
                TaskId = task.Id,
                TaskDisplayId = task.DisplayId,
                TaskTitle = task.Title,
                TaskLink = context.TaskLink,
                Due = task.DueDate,
            });
        }
    }

    /// <summary>Значения макроподстановок для пары «задача + получатель» (T-272).</summary>
    public NotificationContext Context(TaskItem task, Executor executor)
    {
        var org = _org();
        var project = task.ProjectId is { Length: > 0 } projectId ? _projects.Get(projectId) : null;
        return new NotificationContext
        {
            // ид задачи — её НОМЕР (T-15, T-15-S1): им человек и пользуется, а UUID
            // в письме не нужен никому. Сам UUID уходит ссылкой ${task.link}
            TaskId = task.DisplayId,
            TaskTitle = task.Title,
            TaskLink = $"{_publicBaseUrl()}/task/{task.Id}",
            TaskStatus = StatusName(task.Status),
            TaskDue = task.DueDate is { } due
                ? due.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "",
            PerformerNick = executor.Nick,
            PerformerName = executor.InternalName,
            CompanyName = org.Name,
            CompanyId = org.Code,
            ProjectName = project?.Name ?? "",
            ProjectId = project?.DisplayId ?? "",
        };
    }

    /// <summary>Название состояния из справочника на языке установки; нет такого — код.</summary>
    private string StatusName(string code)
    {
        try
        {
            return _statuses.List(Loc.Lang, includeInactive: true)
                       .FirstOrDefault(s => s.Id == code)?.Name
                   ?? code;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return code;
        }
    }

    private void Journal(string eventType, NotificationMessage message, string? actorId, string error)
    {
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            TaskId = message.TaskId,
            EventType = eventType,
            EntityType = "notification_rule",
            EntityId = message.RuleId,
            // АДРЕСА И ТЕКСТА ПИСЬМА В ЖУРНАЛЕ НЕТ (ТЗ п. 6.3): это личные данные человека,
            // а для разбора «почему не пришло» хватает правила, вида и причины отказа
            PayloadJson = JsonSerializer.Serialize(new
            {
                rule = message.RuleDisplayId,
                task = message.TaskDisplayId,
                channel = message.Channel,
                executor = message.ExecutorNick,
                error,
            }),
        });
    }
}
