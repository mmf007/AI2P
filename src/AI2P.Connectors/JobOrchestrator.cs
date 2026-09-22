using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Запуск задач через очередь заданий (ТЗ п. 3 принцип 4): задача → job → коннектор исполнителя.
/// Этап 2.1: человек (HumanConnector) + Claude (AnthropicConnector); выбор коннектора —
/// через ConnectorRegistry по типу исполнителя и провайдеру профайла модели (п. 2.9).
/// </summary>
public sealed class JobOrchestrator : IDisposable
{
    /// <summary>Код действия-политики «автозапуск подзадач» в справочнике действий (ТЗ v1.26):
    /// правило безопасности deny/confirm на этот код отключает автоматический запуск агентов.</summary>
    public const string AutoStartActionCode = "AI2P.Tasks.AutoStart";

    private readonly TaskService _tasks;
    private readonly JobService _jobs;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly TeamService _teams;
    private readonly FileStore _files;
    private readonly ChatService _chat;
    private readonly ConnectorRegistry _connectors;
    private readonly SecurityRuleService? _security;
    private readonly ExecutorPickService? _picker;
    private readonly ExperienceService? _experience;
    /// <summary>Статусы работы участников команд (T-129): запуск задания кнопкой «выполнить»
    /// идёт мимо <see cref="TeamWorkService.Start"/>, и без этой отметки исполнитель так
    /// и висел в списке команды «не подключен». null — статусы никто не показывает (тесты).</summary>
    private readonly TeamWorkService? _teamWork;
    /// <summary>Заявки на запуск задачи с ДРУГОГО сервера (T-196): нажать «запустить» можно
    /// на любом сервере кластера, но физически задание пойдёт у владельца задачи — заявка
    /// едет к нему репликацией, и он разбирает её сторожем. null — заявок нет (тесты).</summary>
    private readonly RunRequestService? _runRequests;
    /// <summary>Этот сервер — дирижёр организации (ТЗ гл. 6, этап 41): АВТОМАТИЧЕСКИЕ запуски
    /// делает только он. Ручной запуск задачи доступен на любом сервере — он идёт не отсюда.</summary>
    private readonly Func<bool> _isConductor;
    /// <summary>Обработка подзадач разбиения сериализована (гонка завершений, ТЗ v1.26).</summary>
    private readonly SemaphoreSlim _splitLock = new(1, 1);
    /// <summary>
    /// БРОНИ ЗАПУСКА (T-304-S0): задачи и исполнители, для которых прямо сейчас идёт
    /// <see cref="StartTaskAsync"/>, а задание ещё не заведено. Автоматических проходов
    /// несколько (очередь иерархии, авторазбиение, автозапуск потомков, сторож), у каждого
    /// свой замок или нет никакого, и между «исполнитель свободен» и заведением задания
    /// лежит сборка промпта. Без брони два прохода видели одного исполнителя свободным и
    /// запускали ему две задачи разом (T-298-S0 и T-299-S0), а одну задачу — дважды.
    /// </summary>
    private readonly HashSet<string> _starting = new(StringComparer.Ordinal);

    public JobOrchestrator(TaskService tasks, JobService jobs, ExecutorService executors,
        ProjectService projects, TeamService teams, FileStore files, ChatService chat,
        ConnectorRegistry connectors, SecurityRuleService? security = null,
        ExecutorPickService? picker = null, ExperienceService? experience = null,
        Func<bool>? isConductor = null, TeamWorkService? teamWork = null,
        RunRequestService? runRequests = null, ObjectService? objects = null)
    {
        _objects = objects;
        _isConductor = isConductor ?? (() => true);
        _teamWork = teamWork;
        _runRequests = runRequests;
        _tasks = tasks;
        _jobs = jobs;
        _executors = executors;
        _projects = projects;
        _teams = teams;
        _files = files;
        _chat = chat;
        _connectors = connectors;
        _security = security;
        _picker = picker;
        _experience = experience;
        // автозапуск потомков при выполнении родителя (todo22, подготовка к этапу 3)
        _tasks.StatusChanged += OnTaskStatusChanged;
        // подзадача, заведённая ВО ВРЕМЯ работы над задачей идущей иерархии (T-16-S0)
        _tasks.Created += OnTaskCreated;
        // задание СУФЛЁРА завершилось (T-292-S0): разобрать управляющий json и запустить
        // основного исполнителя. Подписка одна на все ИИ-коннекторы: суфлёром работает любой
        _connectors.OnPrompterFinished(PrompterFinishedAsync);
    }

    /// <summary>Объекты проекта (T-259): ссылки <c>@obj:</c> в описании задачи разворачиваются
    /// в паспорта персонажей и локаций ПРИ СБОРКЕ ЗАДАНИЯ — то есть на каждом запуске, и
    /// правка паспорта действует на следующее же задание. null — подстановки нет (тесты).</summary>
    private readonly ObjectService? _objects;

    private static readonly ILogger Logger = Log.ForContext("SourceContext", nameof(JobOrchestrator));

    /// <summary>«Родитель выполнен» — любой переход в review / needs_fix / done (todo22).</summary>
    private static bool IsCompleted(string status) =>
        status is TaskStatuses.Review or TaskStatuses.NeedsFix or TaskStatuses.Done;

    /// <summary>
    /// Задача ЗАВЕДЕНА (T-16-S0). Агент по ходу работы создаёт подзадачи (маркер AI2P_SUBTASK,
    /// действие create_task), и до этой правки очередь иерархического запуска узнавала о них
    /// только на следующем своём проходе — то есть по завершении задания родителя либо
    /// сторожем раз в минуту; а если задание родителя не кончалось вовсе (агент ждёт ответа,
    /// висит), подзадачи не брал никто, и человеку приходилось нажимать «запустить иерархию»
    /// ещё раз. Теперь появление подзадачи внутри ОТКРЫТОЙ очереди само двигает эту очередь.
    /// <para>Очередь ведёт владелец корня, у неё свой замок и свои правила отбора, поэтому
    /// здесь только толчок: что именно запустить, решает обычный проход.</para>
    /// </summary>
    private void OnTaskCreated(TaskItem task, string? actorId)
    {
        if (task.IsTemplate || task.ParentId is null)
        {
            return;
        }
        // корневая задача очередью не двигает: пометка runHierarchy ищется у неё и её предков
        if (HierarchyRootOf(task) is not { } root || !_tasks.CanWrite(root))
        {
            return;
        }
        var rootId = root.Id;
        Logger.Information("Иерархия {Root}: заведена подзадача {Task} — очередь пересматривает "
                           + "поддерево (T-16-S0)", root.DisplayId, task.DisplayId);
        // фоном: заведение задачи не должно ждать запуска агентов
        _ = Task.Run(() => ProcessHierarchyAsync(rootId, actorId));
    }

    private void OnTaskStatusChanged(TaskItem task, string oldStatus, string? actorId)
    {
        // «любой переход в review / needs_fix / done» (todo22); повторные срабатывания
        // безопасны — уже запущенные потомки отфильтруются по статусу
        _ = oldStatus;
        if (task.IsTemplate)
        {
            return;
        }
        // автоматические запуски идут у ВЛАДЕЛЬЦА задачи (ТЗ гл. 6, этап 42): то же изменение
        // приедет репликацией на все серверы организации, и без этого условия каждый из них
        // запустил бы те же подзадачи ещё раз. Задачу без сервера ведёт дирижёр — он же
        // отвечает за расписания. Раньше (этап 41) здесь стоял просто признак дирижёра
        if (!_tasks.CanWrite(task))
        {
            return;
        }
        if (IsCompleted(task.Status))
        {
            // фоном: смена статуса не должна ждать запуска агентов
            _ = Task.Run(() => AutoStartChildrenAsync(task, actorId));
        }
        // задание разбиения завершилось — запустить подзадачи (ТЗ v1.26). С T-185 разбитая
        // задача уходит не в «проверку», а в «паузу» (работа не кончилась, а спустилась вниз),
        // поэтому очередь подзадач трогает с места и этот переход — иначе после разбиения
        // не запустилось бы ничего
        if ((IsCompleted(task.Status) || task.Status == TaskStatuses.Paused)
            && TaskService.HasLaunchFlag(task.LaunchJson, "autoSplit"))
        {
            var selfId = task.Id;
            _ = Task.Run(() => ProcessSplitParentAsync(selfId, actorId));
        }
        // блокирующая задача переведена в «готово» или «отменена» (ТЗ п. 2.12, T-299-S0):
        // ждавшие её задачи можно запускать — в том числе те, что стоят в открытой очереди
        // иерархии (T-6-S1). С T-299-S0 отмена блокирующей очередь больше НЕ останавливает:
        // отменённая ветка условия иначе намертво стопорила бы всю иерархию
        if (task.Status is TaskStatuses.Done or TaskStatuses.Cancelled)
        {
            _ = Task.Run(() => AutoStartUnblockedAsync(task, actorId));
        }
        // завершение подзадачи авторазбиения (ТЗ v1.26): освободился исполнитель — запустить
        // следующую по приоритету; все подзадачи готовы — финальный анализ родителя
        if (task.ParentId is not null
            && task.Status is TaskStatuses.Review or TaskStatuses.Done or TaskStatuses.Cancelled
                or TaskStatuses.Error or TaskStatuses.NeedsFix)
        {
            var parentId = task.ParentId;
            _ = Task.Run(() => ProcessSplitParentAsync(parentId, actorId));
        }
        // очередь иерархического запуска (T-159): задача поддерева выполнена — освободился
        // исполнитель и, может быть, дозрел её родитель. Проход идёт от КОРНЯ иерархии:
        // готовым родителем может оказаться не только прямой.
        // С T-16-S0 сюда добавлена ПАУЗА: это тоже конец хода задания (агент задал вопрос,
        // ушёл на подзадачи, встал по лимиту провайдера) — исполнитель свободен, а в поддереве
        // могли появиться подзадачи, заведённые агентом ПРЯМО В ХОДЕ работы (маркер
        // AI2P_SUBTASK, действие create_task). Проход собирает потомков заново, поэтому такие
        // подзадачи он видит и запускает готовых; до этого их подхватывал только сторож раз
        // в минуту либо человек, нажав «запустить иерархию» ещё раз
        // С T-31-S0 сюда добавлено и ОЖИДАНИЕ: задача-прогонщик, вернувшая соседей в доработку,
        // сама уходит в «ожидает» (wait_for_recheck) — и ровно в этот момент освобождается её
        // исполнитель, которому и полагается взять перезапущенные задачи. Без этого перехода
        // очередь двигал бы только сторож раз в минуту, то есть каждый круг проверки стоил бы
        // лишней минуты. Лишних запусков это не даёт: проход всё равно смотрит и статус,
        // и занятость исполнителя, и блокирующие
        if (task.Status is TaskStatuses.Review or TaskStatuses.Done or TaskStatuses.Cancelled
                or TaskStatuses.Error or TaskStatuses.NeedsFix or TaskStatuses.Paused
                or TaskStatuses.Pending
            && HierarchyRootOf(task) is { } hierarchyRoot)
        {
            var rootId = hierarchyRoot.Id;
            _ = Task.Run(() => ProcessHierarchyAsync(rootId, actorId));
        }
    }

    /// <summary>
    /// Автозапуск потомков выполненного родителя (todo22, подготовка к этапу 3): у потомка
    /// тип запуска «автоматический» и статус draft/pending. Исполнитель-человек — задача
    /// переводится в in_progress (система оповещения — позже); исполнитель-ИИ — in_progress
    /// и запуск агента.
    /// </summary>
    public async Task AutoStartChildrenAsync(TaskItem parent, string? actorId)
    {
        // автозапуск ветки выключен человеком (T-54-S0): пометка стоит на самой задаче или
        // выше по дереву и закрывает ВСЕ автоматические старты поддерева. Проверяется один
        // раз на родителя — у потомков остаётся посмотреть только их собственную пометку
        if (_tasks.AutoStartOffRootOf(parent) is { } off)
        {
            Logger.Information("Автозапуск потомков {Parent} не идёт: у задачи {Root} выключен "
                               + "автозапуск (T-54-S0)", parent.DisplayId, off.DisplayId);
            return;
        }
        // потомков условия и циклов (T-299-S0) ведёт очередь иерархии: анализатор завершается
        // РАНЬШЕ потомков, и автозапуск «родитель выполнен» пустил бы обе ветки условия и тело
        // цикла, которое агент велел пропустить
        if (TaskFlow.Read(parent.LaunchJson).Type != TaskFlow.Linear)
        {
            Logger.Information("Автозапуск потомков {Parent} не идёт: задача типа «{Type}», потомков "
                               + "ведёт очередь иерархии (T-299-S0)", parent.DisplayId,
                TaskFlow.Read(parent.LaunchJson).Type);
            return;
        }
        foreach (var child in _tasks.ListChildren(parent.Id))
        {
            try
            {
                if (child.IsTemplate
                    || LaunchMode(child) != "auto"
                    || TaskService.HasLaunchFlag(child.LaunchJson, TaskService.NoAutoStartFlag)
                    || child.Status is not (TaskStatuses.Draft or TaskStatuses.Pending))
                {
                    continue;
                }
                // потомок ДРУГОГО сервера запускается там (ТЗ гл. 6, этап 42): то же
                // завершение родителя приедет к нему репликой, и он запустит подзадачу сам
                if (!_tasks.CanWrite(child))
                {
                    Logger.Information("Автозапуск {Child} пропущен: задача сервера {Server}",
                        child.DisplayId, child.ServerCode);
                    continue;
                }
                if (!_tasks.BlockersDone(child))
                {
                    // блокирующие задачи не готовы (ТЗ п. 2.12): автозапуск отложен — сработает
                    // при завершении (готово/отмена, T-299-S0) последней блокирующей
                    Logger.Information("Автозапуск {Child} отложен: блокирующие задачи не завершены",
                        child.DisplayId);
                    continue;
                }
                if (WaitsDeferredStart(child))
                {
                    Logger.Information("Автозапуск {Child} отложен: задача ждёт сброса лимита (T-121)",
                        child.DisplayId);
                    continue;
                }
                var executor = child.ExecutorIds.Count == 1 ? _executors.Get(child.ExecutorIds[0]) : null;
                if (executor is { IsActive: false })
                {
                    // исполнителя выключили после назначения (todo_bugfix_3): не запускаем
                    Logger.Information("Автозапуск {Child} пропущен: исполнитель «{Nick}» не активен",
                        child.DisplayId, executor.Nick);
                    continue;
                }
                // «НЕ ЧЕЛОВЕК» (T-153-S0), а не «агент»: автозапуском берётся и «авто ПО» —
                // задание ему заводится так же, разница только в коннекторе. Человеку
                // задание не «запускается»: его задача просто переводится в работу (Inbox)
                if (executor is not null && executor.Kind != ExecutorKind.Human)
                {
                    Logger.Information(
                        "Автозапуск {Child} (родитель {Parent} → {Status}): исполнитель {Nick}",
                        child.DisplayId, parent.DisplayId, parent.Status, executor.Nick);
                    await StartTaskAsync(child.Id, actorId);
                }
                else
                {
                    Logger.Information(
                        "Автозапуск {Child} (родитель {Parent} → {Status}): перевод в in_progress",
                        child.DisplayId, parent.DisplayId, parent.Status);
                    _tasks.ChangeStatus(child.Id, TaskStatuses.InProgress, actorId);
                }
            }
            catch (Exception ex)
            {
                // ошибка одного потомка не останавливает запуск остальных
                Logger.Warning(ex, "Автозапуск {Child} не удался: {Error}", child.DisplayId, ex.Message);
            }
        }
    }

    /// <summary>
    /// Автозапуск задач, ждавших завершения блокирующей (ТЗ п. 2.12): условия те же, что
    /// у автозапуска потомков, плюс все блокирующие переведены в «готово».
    /// <para>С T-6-S1 задача, стоящая в ОТКРЫТОЙ очереди иерархического запуска (T-159),
    /// идёт не отсюда, а проходом самой очереди: там соблюдается порядок «снизу вверх, по
    /// убыванию приоритета», проверяется занятость исполнителя и не важен режим запуска
    /// задачи — иерархию человек запустил кнопкой, и переспрашивать про каждую подзадачу
    /// не за чем. Раньше такая задача ждала очередного прохода сторожа (до минуты), а при
    /// режиме запуска «вручную» не стартовала вовсе.</para>
    /// </summary>
    public async Task AutoStartUnblockedAsync(TaskItem blocker, string? actorId)
    {
        var hierarchyRoots = new List<string>();
        foreach (var blocked in _tasks.ListBlockedBy(blocker.Id))
        {
            try
            {
                // разблокированная задача чужого сервера запускается там же (ТЗ гл. 6, этап 42)
                if (!_tasks.CanWrite(blocked))
                {
                    continue;
                }
                // автозапуск ветки выключен человеком (T-54-S0): «блокирующая готова» — это
                // тоже автоматический старт, и он обязан молчать наравне с остальными
                if (_tasks.AutoStartOffRootOf(blocked) is { } off)
                {
                    Logger.Information("Автозапуск {Task} не идёт: у задачи {Root} выключен "
                                       + "автозапуск (T-54-S0)", blocked.DisplayId, off.DisplayId);
                    continue;
                }
                // задача внутри открытого иерархического запуска — её очередь (T-6-S1)
                if (!blocked.IsTemplate
                    && blocked.Status is TaskStatuses.Draft or TaskStatuses.Pending
                    && HierarchyRootOf(blocked) is { } hierarchyRoot)
                {
                    if (!hierarchyRoots.Contains(hierarchyRoot.Id, StringComparer.Ordinal))
                    {
                        hierarchyRoots.Add(hierarchyRoot.Id);
                    }
                    continue;
                }
                if (blocked.IsTemplate
                    || LaunchMode(blocked) != "auto"
                    || blocked.Status is not (TaskStatuses.Draft or TaskStatuses.Pending)
                    || !_tasks.BlockersDone(blocked)
                    || WaitsDeferredStart(blocked)) // ждёт сброса лимита (T-121)
                {
                    continue;
                }
                var executor = blocked.ExecutorIds.Count == 1 ? _executors.Get(blocked.ExecutorIds[0]) : null;
                if (executor is { IsActive: false })
                {
                    // исполнителя выключили после назначения (todo_bugfix_3): не запускаем
                    Logger.Information("Автозапуск {Task} пропущен: исполнитель «{Nick}» не активен",
                        blocked.DisplayId, executor.Nick);
                    continue;
                }
                // «не человек» (T-153-S0): «авто ПО» запускается так же, как агент
                if (executor is not null && executor.Kind != ExecutorKind.Human)
                {
                    Logger.Information("Автозапуск {Task}: блокирующая {Blocker} завершена, исполнитель {Nick}",
                        blocked.DisplayId, blocker.DisplayId, executor.Nick);
                    await StartTaskAsync(blocked.Id, actorId);
                }
                else
                {
                    Logger.Information("Автозапуск {Task}: блокирующая {Blocker} завершена, перевод в in_progress",
                        blocked.DisplayId, blocker.DisplayId);
                    _tasks.ChangeStatus(blocked.Id, TaskStatuses.InProgress, actorId);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Автозапуск {Task} не удался: {Error}", blocked.DisplayId, ex.Message);
            }
        }
        // задачи внутри открытых иерархий (T-6-S1): один проход на иерархию, а не на задачу —
        // очередь сама решит, что запускать первым и чей исполнитель сейчас свободен
        foreach (var rootId in hierarchyRoots)
        {
            try
            {
                Logger.Information("Блокирующая {Blocker} готова: проход очереди иерархии {Root} (T-6-S1)",
                    blocker.DisplayId, rootId);
                await ProcessHierarchyAsync(rootId, actorId);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Проход иерархии {Root} после блокирующей {Blocker} не удался: {Error}",
                    rootId, blocker.DisplayId, ex.Message);
            }
        }
    }

    /// <summary>Снять с корня пометку открытой очереди иерархии (T-6-S1): вместе с ней
    /// снимаются и галочки переспроса — новое нажатие кнопки задаст их заново.</summary>
    private void StopHierarchyRun(TaskItem root, string reason)
    {
        if (!TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyFlag))
        {
            return; // очередь уже закрыта
        }
        _tasks.SetHierarchyRun(root.Id, false);
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyErrorsFlag, false);
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyNeedsFixFlag, false);
        var released = ReleaseLoopWaits(root, 0);
        Logger.Information("Иерархический запуск {Root} остановлен: {Reason} (T-6-S1); "
                           + "циклов снято с паузы «ждёт окончания цикла»: {Released} (T-338-S0)",
            root.DisplayId, reason, released);
    }

    /// <summary>
    /// Остановленная очередь (T-338-S0): анализаторы циклов, стоявшие на паузе «ждёт окончания
    /// цикла», возвращаются в «ожидает». Иначе цикл ДРУГОЙ ветки, чей круг прервала остановка,
    /// висит на паузе навсегда, и диаграмма показывает его цепочку «в работе». Отметка круга
    /// (loopBody) остаётся: новый запуск иерархии продолжит прерванный круг, а не начнёт заново.
    /// </summary>
    private int ReleaseLoopWaits(TaskItem node, int depth)
    {
        if (depth > MaxHierarchyDepth || node.IsTemplate || node.DeletedAt is not null)
        {
            return 0;
        }
        var count = 0;
        var current = _tasks.Get(node.Id) ?? node;
        if (current.Status == TaskStatuses.Paused && TaskFlowRun.InLoopBody(current.LaunchJson)
            && _tasks.CanWrite(current))
        {
            _tasks.ChangeStatus(current.Id, TaskStatuses.Pending, actorId: null);
            count++;
        }
        foreach (var child in _tasks.ListChildren(current.Id))
        {
            count += ReleaseLoopWaits(child, depth + 1);
        }
        return count;
    }

    /// <summary>Режим запуска из launch_json задачи: manual / auto / schedule (ТЗ п. 2.1).</summary>
    private static string LaunchMode(TaskItem task)
    {
        try
        {
            using var doc = JsonDocument.Parse(task.LaunchJson);
            return doc.RootElement.TryGetProperty("mode", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "manual"
                : "manual";
        }
        catch (JsonException)
        {
            return "manual";
        }
    }

    /// <summary>
    /// Запустить задачу вручную (условие запуска «вручную», ТЗ п. 2.1; для ИИ — кнопка
    /// «запустить» на карточке задачи, ТЗ v1.14).
    ///
    /// force (T-121) — запускать, не глядя на остаток лимита исполнителя. По умолчанию
    /// остаток проверяется: если его слишком мало, задание не запускается, старт задачи
    /// переносится на момент сброса окна, а в чат идёт предупреждение
    /// (<see cref="AgentLimitLowException"/>). Лимиты у исполнителя не указаны — проверки нет.
    ///
    /// autoChildren (T-274-S0) — ответ человека на переспрос «автоматически выполнять новых
    /// потомков»: true снимает с задачи пометку <see cref="TaskService.NoAutoStartFlag"/>,
    /// false ставит её. null — решения нет, пометка остаётся как была (так идут все
    /// автоматические запуски: очередь иерархии, автозапуск потомков, сторож). Спрашивается
    /// именно здесь, потому что подзадачи заводит сам агент ПРЯМО В ХОДЕ работы, и другого
    /// момента сказать «этих не запускать» у человека нет.
    /// </summary>
    public async Task<Job> StartTaskAsync(string taskId, string? actorId, bool force = false,
        bool? autoChildren = null)
    {
        var task = _tasks.Get(taskId) ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (task.IsTemplate)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.1"));
        }
        // СОСТОЯНИЕ ЗАДАЧИ НА ВХОДЕ (T-292-S0): по нему решается, переделывать ли работу
        // суфлёра. Запоминается здесь, потому что ниже задача переводится «в работу»
        var statusOnStart = task.Status;
        // задание ВСЕГДА выполняется на сервере-владельце задачи (ТЗ гл. 6, этап 42): там же
        // лежат её файлы и туда же ложится результат. Межсерверного запуска в системе нет
        _tasks.EnsureCanWrite(task);
        if (task.Kind != TaskKind.Task)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.2"));
        }
        if (task.ExecutorIds.Count != 1)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.3"));
        }
        // бронь задачи (T-304-S0): проверка «задание уже есть» и сама бронь — под одним
        // замком, иначе два прохода заводят одной задаче два задания
        using var hold = new StartHold(this);
        if (!hold.Take("t:" + taskId, () => _jobs.HasActive(taskId)))
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.4"));
        }

        var assigned = _executors.Get(task.ExecutorIds[0])
                       ?? throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.5"));
        if (!assigned.IsActive)
        {
            // исполнителя выключили после назначения (todo_bugfix_3)
            throw new InvalidOperationException(
                Loc.T("msg.jobOrchestrator.6", assigned.Nick));
        }
        // ПРОГРАММА СТОИТ НА КОНКРЕТНОМ КОМПЬЮТЕРЕ (T-153-S0): исполнитель «авто ПО» работает
        // только на своём сервере — здесь его программы нет вовсе, и запускать нечего
        EnsureSoftwareIsHere(assigned);
        // СУФЛЁР (T-292-S0): рабочая модель назначенного исполнителя требует управляющего
        // json. У такой задачи ДВЕ фазы (сначала суфлёр, потом генерация), и «исполнитель
        // занят» для неё не отказ, а ожидание: json уже может быть готов, и терять его,
        // отказав в запуске, нельзя
        var needsPrompter = NeedsPrompter(assigned);
        // каждый ИИ-исполнитель работает над одним заданием (ТЗ v1.26): одна модель может
        // вести несколько заданий одновременно только через разных исполнителей
        var busy = IsBusy(assigned);
        // остаток лимита за окно (T-121): мало — исполнитель работу тоже не возьмёт
        AgentLimitStatus? low = null;
        if (!busy && !force && _executors.LimitStatus(assigned) is { IsLow: true } status)
        {
            low = status;
        }
        // ЗАМЕНА ИСПОЛНИТЕЛЯ (T-221): назначенный сейчас работу взять не может — отдаём её
        // первому свободному из списка «могут заменить исполнителя». Список пуст или все
        // заняты — всё как раньше: занят → отказ, лимит → перенос старта
        var executor = assigned;
        if (busy || low is not null)
        {
            if (FreeSubstitute(task, assigned, force) is not { } substitute)
            {
                if (busy)
                {
                    if (needsPrompter)
                    {
                        // «основной исполнитель и те, кто могут заменить, заняты — ждём,
                        // поставив задачу на паузу» (T-292-S0): поднимет её сторож
                        // отложенных стартов, как только кто-нибудь освободится
                        throw WaitForFree(task, Loc.T("msg.prompter.22", assigned.Nick));
                    }
                    throw new InvalidOperationException(
                        Loc.T("msg.jobOrchestrator.7", assigned.Nick));
                }
                throw DeferStart(task, assigned, low!);
            }
            executor = substitute;
            Logger.Information("Задача {Task}: назначенный исполнитель «{Nick}» {Why} — работу берёт "
                               + "«{Substitute}» из списка замены (T-221)",
                task.DisplayId, assigned.Nick, busy ? "занят" : "почти исчерпал лимит", substitute.Nick);
            // человеку это видно только из чата: в списках стоит назначенный исполнитель,
            // и «почему работает не он» иначе взять неоткуда
            _chat.Add(task.Id, substitute.Id, task.ResponsibleId,
                Loc.T(busy ? "msg.jobOrchestrator.24" : "msg.jobOrchestrator.25",
                    assigned.Nick, substitute.Nick));
        }
        // БРОНЬ ИСПОЛНИТЕЛЯ (T-304-S0): занятость переспрашивается под замком брони — между
        // проверкой выше и заведением задания ниже другой проход мог отдать ему свою задачу
        if (executor.Kind != ExecutorKind.Human && !hold.Take("e:" + executor.Id, () => IsBusy(executor)))
        {
            if (needsPrompter)
            {
                throw WaitForFree(task, Loc.T("msg.prompter.22", executor.Nick));
            }
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.7", executor.Nick));
        }
        // ПРАВИЛА БЕЗОПАСНОСТИ ДО ЗАПУСКА ПРОГРАММЫ (T-155-S0): запуск задачи «авто ПО» — это
        // работа внешней программы на этом компьютере, и заводить её могут не только люди, но
        // и ИИ-агенты (create_task + запуск). Поэтому решение спрашивается ЗДЕСЬ, в общем
        // месте запуска, а не в коннекторе: мимо StartTaskAsync задание не заводит никто
        EnsureSoftwareAllowed(task, executor);

        IAgentConnector connector;
        try
        {
            connector = _connectors.Resolve(executor);
        }
        catch (Exception ex)
        {
            // до коннектора дело не дошло (нет профайла модели, не тот провайдер) — это
            // ошибка подключения, и в списке команды она должна быть видна (T-129)
            _teamWork?.NoteConnection(executor, ex.Message);
            throw;
        }

        // работу оборвал лимит подписки, и у задания осталась живая сессия агента (T-166):
        // продолжаем ЕЁ, а не начинаем задание заново — агент доделает начатое
        if (await TryContinueAfterLimitAsync(task, executor, connector) is { } continued)
        {
            return continued;
        }

        // ПОМЕТКИ ЗАПУСКА СНИМАЮТСЯ ДО ФАЗЫ СУФЛЁРА (T-292-S0): суфлёр — это отдельное
        // задание той же задачи, и запуск генерации будет ВТОРЫМ заходом в этот же метод.
        // Сними мы их только вместе с генерацией, отложенный старт и ожидание входа в CLI
        // успели бы сработать ещё раз, пока суфлёр считает.
        // Задача пошла в работу — отложенный старт (T-121) больше не нужен; лишней записи
        // в задачу не делаем: у большинства задач переноса нет
        if (TaskService.LaunchStartAfter(task.LaunchJson) is not null)
        {
            _tasks.SetStartAfter(taskId, null);
        }
        // работа начинается заново — продолжать старую сессию агента (T-166) уже не будем
        if (TaskService.LaunchResumeJob(task.LaunchJson) is not null)
        {
            _tasks.SetResumeJob(taskId, null);
        }
        // задача тронулась — ждать входа в CLI ей больше не надо (todo96)
        if (TaskService.HasLaunchFlag(task.LaunchJson, TaskService.WaitAuthFlag))
        {
            _tasks.SetLaunchFlag(taskId, TaskService.WaitAuthFlag, false);
        }
        // АВТОЗАПУСК НОВЫХ ПОТОМКОВ (T-274-S0): человек ответил на переспрос запуска —
        // ставим или снимаем пометку ветки. Пишем только при РАСХОЖДЕНИИ: у большинства
        // задач ответ совпадает с тем, что уже стоит, и лишняя правка launch_json уехала бы
        // репликацией. Запуск иерархии эту пометку снимает сам (StartHierarchyAsync)
        if (autoChildren is { } auto
            && TaskService.HasLaunchFlag(task.LaunchJson, TaskService.NoAutoStartFlag) == auto)
        {
            _tasks.SetLaunchFlag(taskId, TaskService.NoAutoStartFlag, !auto);
            Logger.Information("Задача {Task}: автозапуск новых потомков {State} при запуске "
                               + "(T-274-S0)", task.DisplayId, auto ? "включён" : "выключен");
        }
        _tasks.ChangeStatus(taskId, TaskStatuses.InProgress, actorId);

        // ФАЗА СУФЛЁРА (T-292-S0). Управляющий json для медиа-модели готовит ОТДЕЛЬНЫЙ
        // ИСПОЛНИТЕЛЬ отдельным заданием — обычным, со своим коннектором, своими правилами
        // безопасности и своей занятостью. Поэтому суфлёром работает кто угодно: подписка
        // CLI, локальная модель, облачное API. Закончив, он освобождается, а генерацию
        // система запускает сама, зайдя в этот же метод второй раз (PrompterFinishedAsync).
        //
        // ПОВТОРНЫЙ ЗАПУСК смотрит на СОСТОЯНИЕ задачи: «пауза»/«ошибка» с готовым json —
        // фаза суфлёра пропускается (переделывать нечего, это продолжение начатого); а
        // «черновик»/«ожидание»/«доработка» означают новую работу, и json готовится заново
        string? prompterJson = null;
        if (needsPrompter && connector is ComfyUiConnector)
        {
            prompterJson = ReadyPrompterJson(task, statusOnStart);
            if (prompterJson is null)
            {
                return await StartPrompterJobAsync(task, executor, actorId);
            }
            Logger.Information("Задача {Task}: управляющий json суфлёра уже готов (состояние «{Status}») — "
                               + "фазу суфлёра пропускаю (T-292-S0)", task.DisplayId, statusOnStart);
        }

        // «АГЕНТ» (T-153-S0), а не «не человек»: собранный промпт задания (заголовок, критерии,
        // опыт) нужен ИМЕННО агенту. Человек получает описание задачи как есть (Inbox), и
        // «авто ПО» — тоже: промпта программа не читает, ей нужны параметры операции
        // ИСПОЛЬЗОВАННЫЙ ОПЫТ (T-266-S0): какие записи ушли в текст — их собирает сборка
        // промпта, а номер задания проставляется ниже, когда задание заведено
        var usedExperience = new List<string>();
        var requestText = executor.Kind == ExecutorKind.Ai
            ? BuildAiRequest(task, usedExperience)
            : _tasks.ReadDescription(task);

        var job = _jobs.Create(taskId, executor.Id, task.DescriptionPath, actorId);
        // задание заведено — теперь занятость видна по базе, бронь больше не нужна (T-304-S0)
        hold.Dispose();
        if (usedExperience.Count > 0)
        {
            _experience?.NoteUsed(taskId, usedExperience, job.Id);
        }
        // кто РЕАЛЬНО ведёт задачу (T-221): пишется при каждом запуске, а не только при
        // замене — иначе по задаче, которую один раз выполнил запасной, а потом снова
        // назначенный, осталась бы неверная запись
        _tasks.SetActualExecutor(taskId, executor.Id);
        if (prompterJson is not null && connector is ComfyUiConnector comfy)
        {
            // значения суфлёра — в граф (T-287-S0). Токены и цена здесь нулевые НАМЕРЕННО:
            // их уже записало на себя задание суфлёра, а сложить их сюда значило бы посчитать
            // один и тот же вызов модели дважды
            comfy.UsePrompter(job.Id, new PrompterOutcome(prompterJson, 0, 0, 0,
                PrompterNickOf(task), ComfyPrompterGraph.Values(prompterJson).Keys
                    .OrderBy(k => k, StringComparer.Ordinal).ToList()));
        }
        try
        {
            await connector.SubmitJobAsync(job, requestText);
        }
        catch (Exception ex)
        {
            // задание не ушло исполнителю — в списке команды это «ошибка подключения» (T-129)
            _teamWork?.NoteConnection(executor, ex.Message);
            throw;
        }
        // задание принято — исполнитель подключён; без этой отметки он оставался в списке
        // команды «не подключен» до перезапуска всей команды (T-129)
        _teamWork?.NoteConnection(executor, null);
        return _jobs.Get(job.Id)!; // коннектор уже сменил состояние (ИИ — running, человек — waiting_human)
    }

    /// <summary>Имя файла-артефакта с управляющим json суфлёра (T-292-S0). Имя ПОСТОЯННОЕ,
    /// без номера задания: по нему система и узнаёт, что json готов, — а номер задания у
    /// повторного запуска будет уже другой.</summary>
    public const string PrompterArtifact = "prompter.json";

    /// <summary>
    /// РАБОЧЕЙ МОДЕЛИ ИСПОЛНИТЕЛЯ НУЖЕН СУФЛЁР (T-292-S0): в профайле стоит
    /// <c>prompter.required</c> (T-286-S0). Профайл не читается или его нет вовсе — «не
    /// нужен»: про испорченный профайл скажет сам коннектор своей понятной ошибкой, и
    /// подменять её жалобой на суфлёра незачем.
    /// </summary>
    private bool NeedsPrompter(Executor executor)
    {
        if (executor.Kind != ExecutorKind.Ai)
        {
            return false;
        }
        try
        {
            return _connectors.ProfileOf(executor).Prompter.Required;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            Logger.Warning("Задача: профайл исполнителя «{Nick}» не прочитан — считаю, что суфлёр "
                           + "не нужен ({Error})", executor.Nick, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// ГОТОВЫЙ УПРАВЛЯЮЩИЙ JSON ЗАДАЧИ (T-292-S0) или null — «фазу суфлёра надо пройти».
    ///
    /// <para>Решает СОСТОЯНИЕ ЗАДАЧИ НА МОМЕНТ ЗАПУСКА. «Пауза» и «ошибка» означают, что
    /// работа уже начиналась и её продолжают: готовый json переделывать незачем — суфлёр
    /// свою часть отработал, и второй его запуск стоил бы денег и времени, дав тот же ответ.
    /// «Черновик», «ожидание» и «доработка» — это НОВАЯ работа (задачу поправили, вернули,
    /// запустили заново), и json готовится заново, что бы ни лежало в артефактах.</para>
    /// </summary>
    private string? ReadyPrompterJson(TaskItem task, string statusOnStart)
    {
        if (statusOnStart is TaskStatuses.Draft or TaskStatuses.Pending or TaskStatuses.NeedsFix)
        {
            return null;
        }
        var text = _files.ReadText(PrompterArtifactRel(task));
        return text.Trim().Length > 0 ? text : null;
    }

    /// <summary>Путь артефакта с управляющим json задачи — без записи и без проверки, что
    /// файл есть (T-292-S0).</summary>
    private string PrompterArtifactRel(TaskItem task) =>
        _files.TaskArtifactRel(task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.Slug,
            task.DisplayId, PrompterArtifact);

    /// <summary>Ник суфлёра задачи для сводки задания; не назначен — пусто.</summary>
    private string PrompterNickOf(TaskItem task) =>
        task.PrompterExecutorId is { Length: > 0 } id && _executors.Get(id) is { } e ? e.Nick : "";

    /// <summary>
    /// ЗАПУСТИТЬ ФАЗУ СУФЛЁРА (T-292-S0) — отдельное задание отдельного исполнителя, которое
    /// по описанию задачи готовит управляющий json для медиа-модели.
    ///
    /// <para>ПОЧЕМУ ИСПОЛНИТЕЛЬ, А НЕ МОДЕЛЬ. До T-292-S0 суфлёра звал свой маленький
    /// HTTP-клиент (T-288-S0), и подписка CLI с локальными моделями работать суфлёрами не
    /// могли вовсе — «можно использовать только api модели». Исполнитель приносит с собой
    /// всё нужное: коннектор своего вида, профайл, ключи, правила безопасности, таймауты,
    /// занятость и умение задать вопрос человеку.</para>
    ///
    /// <para>ТРИ ИСХОДА, и все три — обычная механика заданий, своего кода не требующая:
    /// ошибка задания ставит задаче «ошибку», вопрос человеку — «паузу», а нормальное
    /// завершение приходит сюда же обратно (<see cref="PrompterFinishedAsync"/>) и запускает
    /// генерацию.</para>
    ///
    /// <para>Промпт суфлёра получает описание с УЖЕ РАСКРЫТЫМИ ссылками <c>@obj:</c> (T-259) —
    /// так же, как его получает сам коннектор: иначе суфлёр увидел бы «@obj:OBJ-3» вместо
    /// паспорта персонажа и не смог бы назвать ни тембр вокала, ни стиль.</para>
    /// </summary>
    private async Task<Job> StartPrompterJobAsync(TaskItem task, Executor main, string? actorId)
    {
        // 1. КТО СУФЛЁР. Поле задачи главнее: оно заполняется из исполнителя при подборе,
        // но человек вправе назначить по этой задаче другого
        var prompterId = task.PrompterExecutorId is { Length: > 0 } own
            ? own
            : main.PrompterExecutorId;
        if (prompterId is not { Length: > 0 })
        {
            // «нуждается в суфлёре, а его нет — писать ошибку запуска и останавливаться»
            throw PrompterFailed(task, main, Loc.T("msg.prompter.17", main.Nick));
        }
        if (task.PrompterExecutorId is not { Length: > 0 })
        {
            // подставили из исполнителя — пусть это будет видно в карточке задачи, а не
            // только в логе: иначе «кто готовил json» взять человеку неоткуда
            _tasks.SetPrompter(task.Id, prompterId);
            task.PrompterExecutorId = prompterId;
        }

        // 2. СВОБОДНЫЙ СУФЛЁР: назначенный, а занят — первый свободный из «могут заменить
        // суфлёра». Живых нет вовсе (выключены, удалены) — это ошибка запуска; все живые
        // заняты — ЖДЁМ: суфлёр освободится через минуты, а работа никуда не денется
        var live = new[] { prompterId }.Concat(task.PrompterAltExecutorIds)
            .Distinct(StringComparer.Ordinal)
            .Select(_executors.Get)
            .Where(e => e is { IsActive: true, DeletedAt: null } && e.Kind != ExecutorKind.Human)
            .Select(e => e!)
            .ToList();
        if (live.Count == 0)
        {
            throw PrompterFailed(task, main, Loc.T("msg.prompter.23", main.Nick));
        }
        // свободный суфлёр бронируется так же, как основной исполнитель (T-304-S0)
        using var hold = new StartHold(this);
        var chosen = live.FirstOrDefault(e => hold.Take("e:" + e.Id, () => IsBusy(e)));
        if (chosen is null)
        {
            throw WaitForFree(task, Loc.T("msg.prompter.18",
                string.Join(", ", live.Select(e => e.Nick))));
        }

        // 3. ПРОМПТ: правила составления json (файл профайла РАБОЧЕЙ модели), перечень полей
        // со границами и сама задача — ровно тот же текст, что собирал прежний суфлёр
        var settings = _connectors.ProfileOf(main).Prompter;
        var language = AgentLanguageOf(task);
        var description = _tasks.ReadDescription(task);
        if (_objects is not null)
        {
            description = _objects.Expand(task.ProjectId, description,
                kind => ObjectKinds.Title(kind, language));
        }
        var requestText = PrompterService.BuildPrompt(settings, _files.ReadText(settings.Rules),
            task, description, _tasks.ReadAcceptance(task), language);

        IAgentConnector connector;
        try
        {
            connector = _connectors.Resolve(chosen);
        }
        catch (Exception ex)
        {
            _teamWork?.NoteConnection(chosen, ex.Message);
            throw PrompterFailed(task, main, Loc.T("msg.prompter.5", chosen.Nick, ex.Message));
        }
        var job = _jobs.Create(task.Id, chosen.Id, task.DescriptionPath, actorId, JobRoles.Prompter);
        hold.Dispose();
        Logger.Information("Задача {Task}: фаза суфлёра — задание {Job} исполнителю «{Nick}», "
                           + "промпт {Length} симв. (T-292-S0)",
            task.DisplayId, job.DisplayId, chosen.Nick, requestText.Length);
        _chat.Add(task.Id, chosen.Id, task.ResponsibleId, Loc.T("msg.prompter.19", chosen.Nick));
        try
        {
            await connector.SubmitJobAsync(job, requestText);
        }
        catch (Exception ex)
        {
            _teamWork?.NoteConnection(chosen, ex.Message);
            throw;
        }
        _teamWork?.NoteConnection(chosen, null);
        return _jobs.Get(job.Id)!;
    }

    /// <summary>
    /// ЗАДАНИЕ СУФЛЁРА ЗАВЕРШИЛОСЬ НОРМАЛЬНО (T-292-S0) — зовётся коннектором из фоновой
    /// работы агента. Управляющий json вырезается из ответа, ложится в артефакты задачи
    /// постоянным именем (<see cref="PrompterArtifact"/>) и запускается основной исполнитель.
    ///
    /// <para>JSON НЕ НАШЁЛСЯ — это ошибка задачи, а не молчаливый запуск генерации по
    /// умолчаниям: человек просил песню на русском длиной 90 секунд, и «сделаю как обычно»
    /// он увидит только через полчаса счёта, когда трек уже готов не тот.</para>
    ///
    /// <para>ОСНОВНОЙ ИСПОЛНИТЕЛЬ МОЖЕТ БЫТЬ ЗАНЯТ — тогда <see cref="StartTaskAsync"/> сам
    /// поставит задачу на паузу с отложенным стартом, и генерацию поднимет сторож. Потерять
    /// работу суфлёра при этом нельзя: json уже лежит в артефактах, и второй заход его
    /// возьмёт готовым.</para>
    /// </summary>
    public async Task PrompterFinishedAsync(Job job, TaskItem task, string answerText)
    {
        try
        {
            var json = PrompterService.ExtractJson(answerText);
            if (json is null)
            {
                var text = Loc.T("msg.prompter.21", answerText.Length <= 300
                    ? answerText
                    : answerText[..300] + "…");
                Logger.Warning("Задание {Job}: суфлёр задачи {Task} не вернул json — задача встаёт "
                               + "с ошибкой (T-292-S0)", job.DisplayId, task.DisplayId);
                _chat.Add(task.Id, job.ExecutorId, task.ResponsibleId, text);
                _tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: job.ExecutorId);
                return;
            }
            var path = PrompterArtifactRel(task);
            _files.WriteText(path, json);
            Logger.Information("Задание {Job}: управляющий json задачи {Task} готов ({Path}) — "
                               + "запускаю основного исполнителя (T-292-S0)",
                job.DisplayId, task.DisplayId, path);
            var values = ComfyPrompterGraph.Values(json);
            _chat.Add(task.Id, job.ExecutorId, task.ResponsibleId,
                Loc.T("msg.prompter.20", string.Join(", ",
                    values.Keys.OrderBy(k => k, StringComparer.Ordinal))));
            // что суфлёр не назвал и что будет поправлено границей схемы — в чат: иначе
            // «взяли 1000 секунд вместо названных 2000» не видно нигде (T-288-S0)
            if (_executors.Get(task.ExecutorIds.FirstOrDefault() ?? "") is { } main)
            {
                foreach (var note in PrompterService.Notes(_connectors.ProfileOf(main).Prompter, values))
                {
                    _chat.Add(task.Id, job.ExecutorId, task.ResponsibleId, note);
                }
            }
            await StartTaskAsync(task.Id, actorId: null);
        }
        catch (Exception ex)
        {
            // «занят / жду» сюда приходит обычным исключением: задача уже переведена в паузу
            // с отложенным стартом, и поднимет её сторож — ругаться в журнал не о чем
            Logger.Warning("Задача {Task}: генерация после суфлёра не началась: {Error}",
                task.DisplayId, ex.Message);
        }
    }

    /// <summary>
    /// ЗАДАЧА ЖДЁТ ОСВОБОЖДЕНИЯ ИСПОЛНИТЕЛЯ (T-292-S0): «пауза» плюс отложенный старт на
    /// минуту вперёд. Поднимет её тот же сторож, что и перенос по лимиту
    /// (<see cref="StartDeferredOnce"/>), — и так каждую минуту, пока кто-нибудь не
    /// освободится. Это не отказ: у медиа-задачи с суфлёром работа могла быть уже наполовину
    /// сделана, и «исполнитель занят, запустите позже» означало бы выбросить её.
    /// <para>В чат ничего не пишется намеренно: сторож ходит раз в минуту, и одинаковые
    /// сообщения засорили бы переписку (то же правило, что у переноса по лимиту).</para>
    /// </summary>
    private InvalidOperationException WaitForFree(TaskItem task, string message)
    {
        _tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(1));
        if (task.Status != TaskStatuses.Paused)
        {
            _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: null);
        }
        Logger.Information("Задача {Task}: ждём освобождения — {Message} (T-292-S0)",
            task.DisplayId, message);
        return new InvalidOperationException(message);
    }

    /// <summary>
    /// ФАЗА СУФЛЁРА НЕВОЗМОЖНА (T-292-S0): задача встаёт с ошибкой, причина — в чат и в
    /// журнал. Ошибка, а не «пойдём без суфлёра»: модель сама объявила, что без управляющего
    /// json её параметры берутся с потолка, и тихая генерация «как обычно» — это полчаса
    /// счёта ради заведомо не того результата.
    /// </summary>
    private InvalidOperationException PrompterFailed(TaskItem task, Executor main, string message)
    {
        Logger.Warning("Задача {Task}: запуск остановлен — {Message} (T-292-S0)",
            task.DisplayId, message);
        _chat.Add(task.Id, main.Id, task.ResponsibleId, message);
        _tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: null);
        return new InvalidOperationException(message);
    }

    /// <summary>
    /// «АВТО ПО» РАБОТАЕТ ТОЛЬКО НА СВОЁМ СЕРВЕРЕ (T-153-S0). У ИИ с локальной моделью то же
    /// самое обеспечивается связкой «модель включена на этом сервере» (T-8-S1), а у программы
    /// проверять нечего, кроме её собственного сервера: плагин, его путь и сама программа —
    /// пер-серверные, они лежат в config.json того компьютера. Задача при этом может быть
    /// нашей (владелец задачи и владелец исполнителя — разные вещи), поэтому проверка своя.
    /// </summary>
    private void EnsureSoftwareIsHere(Executor executor)
    {
        if (executor.Kind != ExecutorKind.Software || _tasks.Scope.IsMine(executor.ServerId))
        {
            return;
        }
        throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.26",
            executor.Nick, _tasks.Scope.Describe(executor.ServerId)));
    }

    /// <summary>Код действия справочника «запустить задачу „авто ПО“» (T-155-S0): без записи
    /// справочника правило безопасности не закрывает ничего вовсе (наука 2d3af8da), поэтому
    /// запись заведена сидом, а решение по ней спрашивается перед каждым запуском.</summary>
    public const string SoftwareRunActionCode = "AI2P.Software.Run";

    /// <summary>
    /// ЗАПУСК ВНЕШНЕЙ ПРОГРАММЫ РАЗРЕШЁН ПРАВИЛАМИ (T-155-S0). Спрашиваются два решения:
    /// действие справочника <see cref="SoftwareRunActionCode"/> («этой задаче вообще нельзя
    /// запускать программы») и новый вид правила «плагины и MCP» с операцией ЗАПУСКАТЬ
    /// («нельзя запускать вот этот плагин или вот эту его операцию»). Побеждает строгое.
    /// <para>По умолчанию разрешено всё — правил нет, значит можно. Переспросить здесь не у
    /// кого (запуск идёт из очереди и от агента), поэтому <c>confirm</c>, как и у автозапуска
    /// подзадач, означает отказ: решение остаётся за человеком.</para>
    /// </summary>
    private void EnsureSoftwareAllowed(TaskItem task, Executor executor)
    {
        if (executor.Kind != ExecutorKind.Software || _security is null)
        {
            return;
        }
        var evaluator = new SecurityEvaluator(_security.EffectiveForTask(task), projectRoot: null);
        var (decision, rule) = evaluator.ForAction(SoftwareRunActionCode);
        var (pluginDecision, pluginRule) = evaluator.ForPlugin(executor.PluginCode,
            SecurityEvaluator.PluginRun, executor.PluginOp);
        if (pluginDecision > decision)
        {
            (decision, rule) = (pluginDecision, pluginRule);
        }
        if (decision == SecurityDecision.Allow)
        {
            return;
        }
        var why = rule is null
            ? decision.ToString()
            : $"{rule.Scope}: {rule.Target} {rule.Pattern} → {rule.Permission}";
        Logger.Warning("Задача {Task}: запуск «авто ПО» «{Nick}» закрыт правилом безопасности ({Why})",
            task.DisplayId, executor.Nick, why);
        throw new InvalidOperationException(
            Loc.T("msg.jobOrchestrator.27", executor.Nick, why));
    }

    // --- замена исполнителя (T-221) ---

    /// <summary>
    /// Исполнитель занят прямо сейчас. Занятость есть у всех, КРОМЕ ЧЕЛОВЕКА (ТЗ v1.26: один
    /// агент — одно задание за раз); человек берёт сколько угодно заданий, у него очередь Inbox.
    /// Условие про «не человека», а не про агента (T-153-S0): «авто ПО» — это ОДНА программа
    /// на одном компьютере, и два задания разом она тоже не считает.
    /// </summary>
    /// <remarks>
    /// У «авто ПО» занятость бывает ЧУЖАЯ (T-154-S0): у операции плагина стоит флаг «один
    /// экземпляр», и эту программу уже гоняет другой исполнитель. Видеокарта одна, и второй
    /// запуск обучения не «идёт медленнее», а падает по памяти — поэтому ждём, а не запускаем.
    /// Ответ спрашивается у коннектора: живые процессы знает только он, и только в этом
    /// процессе приложения (после перезапуска сервера чужой процесс уже не наш).
    /// </remarks>
    private bool IsBusy(Executor executor) =>
        executor.Kind != ExecutorKind.Human
        && (IsStarting("e:" + executor.Id)
            || _jobs.HasActiveByExecutor(executor.Id)
            || _connectors.Software?.SingleInstanceBusy(executor) == true);

    /// <summary>Для исполнителя или задачи прямо сейчас идёт запуск (бронь T-304-S0).</summary>
    private bool IsStarting(string key)
    {
        lock (_starting)
        {
            return _starting.Contains(key);
        }
    }

    /// <summary>
    /// Брони одного запуска (T-304-S0). <see cref="Take"/> атомарно — под замком набора
    /// броней — переспрашивает занятость и бронирует; снимаются брони при
    /// <see cref="Dispose"/> (повторный вызов безопасен): сразу после заведения задания
    /// либо на выходе из метода, если запуск сорвался.
    /// </summary>
    private sealed class StartHold(JobOrchestrator owner) : IDisposable
    {
        private readonly List<string> _keys = [];

        public bool Take(string key, Func<bool> busy)
        {
            lock (owner._starting)
            {
                if (owner._starting.Contains(key) || busy())
                {
                    return false;
                }
                owner._starting.Add(key);
                _keys.Add(key);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._starting)
            {
                foreach (var key in _keys)
                {
                    owner._starting.Remove(key);
                }
                _keys.Clear();
            }
        }
    }

    /// <summary>
    /// Первый свободный из списка «могут заменить исполнителя» (T-221). Порядок списка —
    /// порядок предпочтения, поэтому перебор идёт как есть, без сортировки по цене и
    /// качеству: замену человек назвал явно, и переставлять её за него незачем.
    ///
    /// «Свободен» — активен, не занят другим заданием и не упёрся в лимит окна (T-121):
    /// исполнитель с исчерпанным лимитом работу тоже не возьмёт, и назначать его — значит
    /// просто перенести старт ещё раз. При принудительном запуске (<paramref name="force"/>)
    /// лимит не смотрим — человек уже сказал «запускай».
    /// </summary>
    private Executor? FreeSubstitute(TaskItem task, Executor assigned, bool force)
    {
        foreach (var candidateId in task.AltExecutorIds)
        {
            if (candidateId == assigned.Id)
            {
                continue;   // назначенный в списке замены — сам себя не заменяет
            }
            var candidate = _executors.Get(candidateId);
            if (candidate is null || !candidate.IsActive || candidate.DeletedAt is not null
                || IsBusy(candidate))
            {
                continue;
            }
            if (candidate.Kind == ExecutorKind.Software && !_tasks.Scope.IsMine(candidate.ServerId))
            {
                continue;   // «авто ПО» чужого сервера: программы здесь нет (T-153-S0)
            }
            if (!force && _executors.LimitStatus(candidate) is { IsLow: true })
            {
                continue;
            }
            return candidate;
        }
        return null;
    }

    /// <summary>
    /// Кто возьмёт задачу, если запустить её ПРЯМО СЕЙЧАС (T-221): назначенный исполнитель,
    /// а если он занят — первый свободный из списка «могут заменить исполнителя».
    /// null — запускать некому: исполнитель не назначен, выключен, либо занят и свободной
    /// замены нет.
    ///
    /// Этим спрашивают ОЧЕРЕДИ (авторазбиение, иерархия, отложенный старт) перед тем, как
    /// звать <see cref="StartTaskAsync"/>: они пропускают задачу с занятым исполнителем, и
    /// без общего ответа на вопрос «а есть ли замена» до запуска дело бы не дошло вовсе.
    /// Лимит окна здесь не проверяется намеренно: перенос старта по лимиту — дело самого
    /// запуска (T-121), очередь про него знать не должна.
    /// </summary>
    public Executor? RunExecutorFor(TaskItem task)
    {
        if (task.ExecutorIds.Count != 1)
        {
            return null;
        }
        var assigned = _executors.Get(task.ExecutorIds[0]);
        if (assigned is null || !assigned.IsActive)
        {
            return null;
        }
        if (assigned.Kind == ExecutorKind.Software && !_tasks.Scope.IsMine(assigned.ServerId))
        {
            // «авто ПО» чужого сервера (T-153-S0): очередь не должна и пробовать — программа
            // стоит там, и запуск отсюда кончится отказом
            return FreeSubstitute(task, assigned, force: false);
        }
        return IsBusy(assigned) ? FreeSubstitute(task, assigned, force: false) : assigned;
    }

    // --- лимиты агентов (T-121) ---

    /// <summary>
    /// Продолжить работу, оборванную лимитом подписки (T-166), вместо запуска задания заново:
    /// у Claude CLI лимит связь не рвёт — сессия агента жива, её id сохранён в контексте
    /// задания, и по наступлении отложенного старта агенту достаточно послать «Continue».
    /// Возвращает продолженное задание либо null — продолжать нечем (указатель протух, файла
    /// контекста нет, исполнителя сменили, коннектор так не умеет): тогда обычный запуск.
    /// </summary>
    private async Task<Job?> TryContinueAfterLimitAsync(TaskItem task, Executor executor,
        IAgentConnector connector)
    {
        if (TaskService.LaunchResumeJob(task.LaunchJson) is not { } jobId)
        {
            return null;
        }
        var job = _jobs.Get(jobId);
        if (job is null || job.TaskId != task.Id || job.ExecutorId != executor.Id
            || job.State is JobState.Running or JobState.Done)
        {
            // указатель протух: задание удалено, исполнителя сменили либо работа уже идёт
            _tasks.SetResumeJob(task.Id, null);
            return null;
        }
        try
        {
            // промпт задания едет с собой: сессии агента может уже не быть (CLI её не нашёл),
            // и тогда он начнёт работу заново вместо отказа
            await connector.ContinueAfterLimitAsync(job, BuildAiRequest(task));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Logger.Warning("Задача {Task}: продолжить сессию задания {Job} после лимита не вышло " +
                           "({Error}) — запускаю задание заново (T-166)",
                task.DisplayId, job.DisplayId, ex.Message);
            _tasks.SetResumeJob(task.Id, null);
            return null;
        }
        _tasks.SetResumeJob(task.Id, null);
        if (TaskService.LaunchStartAfter(task.LaunchJson) is not null)
        {
            _tasks.SetStartAfter(task.Id, null);
        }
        // работа продолжилась — пометка «ждёт входа» (todo96) больше не нужна
        if (TaskService.HasLaunchFlag(task.LaunchJson, TaskService.WaitAuthFlag))
        {
            _tasks.SetLaunchFlag(task.Id, TaskService.WaitAuthFlag, false);
        }
        _teamWork?.NoteConnection(executor, null);
        Logger.Information("Задача {Task}: лимит отпустил — задание {Job} продолжает ту же сессию " +
                           "агента (T-166)", task.DisplayId, job.DisplayId);
        return _jobs.Get(job.Id)!;
    }

    /// <summary>
    /// Задача ждёт отложенного старта (T-121) — раньше срока АВТОМАТИЧЕСКИЕ запуски её не
    /// трогают: иначе очередь авторазбиения или автозапуск потомков подняли бы её сразу же
    /// и снова упёрлись в тот же лимит. Срок наступил — задачу поднимает сторож
    /// (<see cref="StartDeferredOnce"/>); человек кнопкой «запустить» запускает её когда угодно.
    /// </summary>
    private static bool WaitsDeferredStart(TaskItem task) =>
        TaskService.LaunchStartAfter(task.LaunchJson) is { } at && at > DateTime.UtcNow;

    /// <summary>
    /// Остаток лимита исполнителя задачи (T-121) — для UI перед запуском: у задачи один
    /// назначенный ИИ-исполнитель. null — исполнителя нет, он человек либо лимиты у него
    /// не указаны (тогда вычисление игнорируется).
    /// </summary>
    public AgentLimitStatus? LimitForTask(string taskId)
    {
        var task = _tasks.Get(taskId);
        if (task is null || task.ExecutorIds.Count != 1)
        {
            return null;
        }
        var executor = _executors.Get(task.ExecutorIds[0]);
        return executor is { Kind: ExecutorKind.Ai } ? _executors.LimitStatus(executor) : null;
    }

    /// <summary>
    /// Перенести старт задачи на момент сброса окна лимита по решению человека (T-121):
    /// кнопка «перенести старт» в окне предупреждения о лимите. Возвращает момент старта.
    /// </summary>
    public DateTime DeferTaskStart(string taskId)
    {
        var task = _tasks.Get(taskId) ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        // перенос — работа над задачей, значит только на её сервере (ТЗ гл. 6, этап 42)
        _tasks.EnsureCanWrite(task);
        if (task.ExecutorIds.Count != 1)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.3"));
        }
        var executor = _executors.Get(task.ExecutorIds[0])
                       ?? throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.5"));
        var limit = _executors.LimitStatus(executor)
                    ?? throw new InvalidOperationException(
                        Loc.T("msg.jobOrchestrator.8", executor.Nick));
        return DeferStart(task, executor, limit).StartAfter;
    }

    /// <summary>
    /// Перенести старт задачи на момент сброса окна лимита (T-121) и предупредить в чате.
    /// Возвращает исключение для вызывающего: задание не запущено, и запуск об этом обязан
    /// сообщить (в UI — окно с выбором «перенести / разбить / запустить сейчас»).
    /// Сообщение в чат идёт от имени ИСПОЛНИТЕЛЯ: это он не может взять работу сейчас.
    /// </summary>
    private AgentLimitLowException DeferStart(TaskItem task, Executor executor, AgentLimitStatus limit)
    {
        var now = DateTime.UtcNow;
        var startAfter = limit.ResetAt is { } reset && reset > now
            ? reset
            : now + TimeSpan.FromHours(limit.WindowHours);
        var message = LimitMessage(executor, limit, startAfter);
        var previous = _tasks.SetStartAfter(task.Id, startAfter);
        // задача переходит в «паузу» (T-185): её уже пробовали запустить и перенесли по лимиту
        // исполнителя — это не «ожидает» (никто не брал), и в очередях иерархии и разбиения
        // она числится ждущей, а не свободной. Поднимет её сторож отложенных стартов
        if (task.Status is TaskStatuses.Draft or TaskStatuses.Pending)
        {
            _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: null);
        }
        // предупреждение в чат — только когда время переноса изменилось: сторож пробует
        // запуск раз в минуту, и одинаковые сообщения засорили бы переписку
        if (previous is null || Math.Abs((previous.Value - startAfter).TotalMinutes) >= 1)
        {
            _chat.Add(task.Id, executor.Id, task.ResponsibleId, message);
        }
        Logger.Warning("Задача {Task}: у исполнителя {Nick} осталось {Remaining} из {Limit} токенов " +
                       "за окно {Hours} ч — старт перенесён на {StartAfter} (T-121)",
            task.DisplayId, executor.Nick, limit.Remaining, limit.Limit, limit.WindowHours,
            startAfter.ToLocalTime());
        return new AgentLimitLowException(message, startAfter);
    }

    /// <summary>Текст предупреждения о лимите (T-121): сколько осталось, куда перенесён старт
    /// и что можно сделать иначе — разбить задачу либо запустить принудительно.</summary>
    public static string LimitMessage(Executor executor, AgentLimitStatus limit, DateTime startAfter) =>
        Loc.T("msg.jobOrchestrator.9", executor.Nick, limit.Used, limit.Limit, limit.WindowHours, limit.Remaining, limit.Percent, startAfter.ToLocalTime());

    /// <summary>
    /// Запуск задач с наступившим отложенным стартом (T-121). Работает тем же сторожем, что
    /// и продолжение зависших заданий: раз в минуту, только свои задачи. Лимит к этому времени
    /// мог и не восстановиться — тогда старт переносится дальше, без нового сообщения в чат.
    /// </summary>
    public void StartDeferredOnce()
    {
        try
        {
            foreach (var task in _tasks.ListStartAfterDue(DateTime.UtcNow))
            {
                if (!_tasks.CanWrite(task) || _jobs.HasActive(task.Id))
                {
                    continue;
                }
                // исполнитель занят другим заданием (ТЗ п. 2.2) — задача дождётся его
                // освобождения молча: сторож ходит раз в минуту, и ругаться тут не о чем.
                // Занят назначенный, а в списке замены (T-221) есть свободный — запускаем
                // с ним, ждать нечего
                if (RunExecutorFor(task) is null)
                {
                    continue;
                }
                try
                {
                    Logger.Information("Задача {Task}: наступил отложенный старт — запускаю (T-121)",
                        task.DisplayId);
                    StartTaskAsync(task.Id, actorId: null).GetAwaiter().GetResult();
                }
                catch (AgentLimitLowException ex)
                {
                    Logger.Information("Задача {Task}: лимит ещё не восстановился — старт перенесён на {At}",
                        task.DisplayId, ex.StartAfter.ToLocalTime());
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    Logger.Warning("Отложенный старт задачи {Task} не удался: {Error}",
                        task.DisplayId, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проверка отложенных стартов не удалась: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// ОТПУСТИТЬ ЗАДАЧИ, ЖДАВШИЕ ВХОДА В CLI (todo96): вызывается после успешного входа
    /// из AI2P. Задания встали не по своей вине — сеанс CLI истёк посреди работы, — и ждать
    /// им больше нечего: отложенный старт переводится на «сейчас», а поднимет их тот же
    /// сторож, что и обычный перенос (<see cref="StartDeferredOnce"/>), — со всеми его
    /// проверками занятости исполнителя и продолжения сессии.
    ///
    /// Возвращает, сколько задач отпущено, — это видит человек в форме входа.
    /// </summary>
    public int ReleaseWaitingForAuth()
    {
        var released = 0;
        foreach (var task in _tasks.ListWaitAuth())
        {
            if (!_tasks.CanWrite(task))
            {
                continue;   // чужой сервер: там свой вход и свой сторож (ТЗ гл. 6)
            }
            try
            {
                _tasks.SetStartAfter(task.Id, DateTime.UtcNow);
                _tasks.SetLaunchFlag(task.Id, TaskService.WaitAuthFlag, false);
                released++;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warning("Задача {Task}: отпустить после входа не вышло: {Error}",
                    task.DisplayId, ex.Message);
            }
        }
        if (released > 0)
        {
            Logger.Information("Вход в Claude CLI выполнен — отпущено задач: {Count} (todo96)", released);
            StartDeferredOnce();
        }
        return released;
    }

    /// <summary>
    /// КОМАНДА ЗАПУСКА CLAUDE CLI, настроенная в организации (todo96): берётся из профайла
    /// первого ИИ-исполнителя с транспортом "cli". Нужна форме входа: у человека CLI может
    /// лежать не в PATH, а по своему пути — и входить надо именно в тот CLI, которым потом
    /// работает агент. Ничего не нашлось — пусто, а это значит «claude» из PATH.
    /// </summary>
    public string ClaudeCliCommand()
    {
        foreach (var executor in _executors.List().Where(e => e.Kind == ExecutorKind.Ai))
        {
            if (executor.ProfilePath.Trim().Length == 0)
            {
                continue;
            }
            try
            {
                var profile = ModelProfile.Parse(_files.ReadText(executor.ProfilePath));
                if (profile.IsCli && profile.CliCommand.Trim().Length > 0)
                {
                    return profile.CliCommand.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                // профайл не читается — это забота формы исполнителя, не входа в CLI
            }
        }
        return "";
    }

    /// <summary>Сколько задач этого сервера стоят и ждут входа в CLI (todo96): число
    /// показывается в форме входа, чтобы было видно, ради чего входить.</summary>
    public int WaitingForAuthCount() => _tasks.ListWaitAuth().Count(_tasks.CanWrite);

    /// <summary>
    /// Промпт задания для ИИ (этап 2.1): заголовок, описание, критерии приёмки,
    /// язык ответа — язык общения агентов команды (ТЗ п. 2.8, гл. 9).
    /// </summary>
    /// <param name="usedExperience">Сюда складываются идентификаторы записей опыта, реально
    /// ушедших в текст (T-266-S0): вызывающий запуск дописывает им НОМЕР ЗАДАНИЯ, когда оно
    /// заведено (промпт собирается раньше задания). Сам след пишется в любом случае.</param>
    private string BuildAiRequest(TaskItem task, List<string>? usedExperience = null)
    {
        // язык ОБЩЕНИЯ с агентом — язык команды задачи (ТЗ п. 2.8, T-190), а не язык установки
        var language = AgentLanguageOf(task);
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, "prompt.job.1", task.DisplayId, task.Title));
        sb.AppendLine();
        // ССЫЛКИ НА ОБЪЕКТЫ ПРОЕКТА (T-259): «@obj:OBJ-3» в описании превращается в
        // дословный паспорт персонажа (локации, стиля) и пути его эталонных файлов.
        // Название вида — на языке КОМАНДЫ: этот текст читает агент (T-190)
        var description = _tasks.ReadDescription(task);
        sb.AppendLine(_objects is null
            ? description
            : _objects.Expand(task.ProjectId, description, kind => ObjectKinds.Title(kind, language)));
        var acceptance = _tasks.ReadAcceptance(task);
        if (acceptance.Trim().Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(language, "prompt.tasks.91"));
            sb.AppendLine(acceptance);
        }
        // ВРЕМЯ ↔ КАЧЕСТВО (T-23-S0): значение поля задачи (пусто — умолчание её проекта).
        // Печатается ВСЕГДА и одной строкой: само по себе число агенту ничего не говорит,
        // а как на него отзываться — сколько проверок гонять, насколько глубоко разбираться —
        // записано в общих правилах работы организации. Читается заново из базы: задание
        // идёт часами, и человек мог подвинуть бегунок уже после старта агента
        sb.AppendLine();
        sb.AppendLine(Loc.In(language, "prompt.job.23", _tasks.TimeQualityOf(task.Id)));
        // ВЕТВЛЕНИЕ И ЦИКЛ (T-300-S0): задача «Условие» / «Цикл» получает инструкцию, ЧТО
        // вернуть и КАКИМ действием; у линейной задачи блока нет
        sb.Append(FlowSection(task, language));
        // ОПЫТ (T-29-S0): три блока — общие правила работы (T-11-S0), опыт проекта (todo48)
        // и опыт узла шаблона (todo32). Отбирается он ОДИН РАЗ на всё задание: правило
        // отбора общее (навык исполнителя, тэги задачи, пометка «загружать всегда»), предел
        // из настроек проекта тоже общий. Иначе блоки соревновались бы за место порядком
        // печати, и при тесном пределе общие правила съедали бы весь бюджет, а самый
        // прицельный опыт узла не доезжал вовсе (поймано живой проверкой)
        var ownedSkills = OwnedSkills(task);
        // тэги перечитываются: сюда задача попадает и от подписчиков событий, а те получают
        // её по одной таблице tasks — без связей (T-272)
        var taskTags = TagsOf(task);
        sb.Append(ExperienceSection(task, ownedSkills, taskTags, language, usedExperience));
        if (!string.IsNullOrWhiteSpace(language))
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(language, "prompt.job.2", language));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Блок промпта задачи типа «Условие» или «Цикл» (T-300-S0): что от агента требуется
    /// (решение true/false; исход проверки условий цикла) и каким действием это вернуть —
    /// инструментом, командой ai2p или маркером (текст один на все виды агентов: оркестратор
    /// не знает, какой коннектор возьмёт задание). У условия перечисляются ветки: задача
    /// ветки, «создавать задачи» (тогда агенту заводить их самому ДО сдачи) или «завершить
    /// выполнение». Пусто — задача линейная.
    /// </summary>
    internal string FlowSection(TaskItem task, string? language)
    {
        var flow = TaskFlow.Read(task.LaunchJson);
        if (flow.Type == TaskFlow.Linear)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        if (flow.Type == TaskFlow.If)
        {
            sb.AppendLine(Loc.In(language, "prompt.flow.1"));
            Branch(Loc.In(language, "prompt.flow.6"), flow.IfTrueTaskId, flow.IfTrueCreateTasks, flow.IfTrueStopHierarchy);
            Branch(Loc.In(language, "prompt.flow.7"), flow.IfFalseTaskId, flow.IfFalseCreateTasks, flow.IfFalseStopHierarchy);
        }
        else
        {
            sb.AppendLine(Loc.In(language, "prompt.flow.8",
                Loc.In(language, flow.Type == TaskFlow.Loop ? "prompt.flow.9" : "prompt.flow.10")));
            sb.AppendLine(Loc.In(language, "prompt.flow.11", TaskFlowRun.LoopPass(task.LaunchJson), LoopLimitOf(task)));
        }
        sb.AppendLine(Loc.In(language, "prompt.flow.12"));
        return sb.ToString();

        void Branch(string name, string? taskId, bool create, bool stop)
        {
            var target = taskId is { Length: > 0 } ? _tasks.Get(taskId) : null;
            sb.AppendLine(target is not null
                ? Loc.In(language, "prompt.flow.2", name, target.DisplayId, target.Title)
                : create
                    ? Loc.In(language, "prompt.flow.3", name)
                    : Loc.In(language, stop ? "prompt.flow.4" : "prompt.flow.5", name));
        }
    }

    /// <summary>
    /// ТЭГИ ЗАДАЧИ для отбора опыта (T-29-S0). Читаются заново: связи задачи (тэги, навыки,
    /// исполнители) подгружаются только в Get/List, а в подписчиках событий и после смены
    /// статуса задача приходит по одной таблице <c>tasks</c> — с пустыми связями (T-272).
    /// Перечитать не удалось (задача уже удалена) — берём то, что пришло.
    /// </summary>
    private IReadOnlyCollection<string> TagsOf(TaskItem task) =>
        _tasks.Get(task.Id)?.Tags ?? task.Tags;

    /// <summary>
    /// Предел подстановки опыта для этой задачи (T-29-S0): настройка ПРОЕКТА, а задаче вне
    /// проекта — то же умолчание (взять настройку неоткуда, а без предела блок опыта растёт
    /// с каждым завершённым заданием).
    /// </summary>
    private int ExperienceLimitOf(TaskItem task)
    {
        var settings = task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.SettingsJson;
        return ProjectSettings.ExperienceLimitChars(settings);
    }

    /// <summary>Уместить записи опыта в общий предел (T-29-S0). Сам отбор живёт в
    /// <see cref="ExperienceBudget.Fit"/>; бюджета нет (внешний вызов, тесты) — берём всё,
    /// как было до предела.</summary>
    private static (List<ExperienceRecord> Taken, int Skipped) Fit(List<ExperienceRecord> records,
        ExperienceBudget? budget, Func<ExperienceRecord, string> line) =>
        budget is null ? (records, 0) : budget.Fit(records, line);

    /// <summary>
    /// ВСЯ ЧАСТЬ ПРОМПТА ПРО ОПЫТ (T-29-S0): три блока подряд — общие правила работы,
    /// опыт проекта, опыт узла шаблона. Записи всех трёх видов отбираются в ОДИН приём,
    /// одним бюджетом: место достаётся сначала пометке «загружать всегда», потом более
    /// прицельным записям (узел шаблона важнее проекта, проект важнее общих правил), а
    /// внутри — свежим. Порядок ПЕЧАТИ при этом прежний: рамка, потом её уточнения.
    /// </summary>
    private string ExperienceSection(TaskItem task, IReadOnlyCollection<string> ownedSkills,
        IReadOnlyCollection<string> taskTags, string? language, List<string>? usedExperience = null)
    {
        if (_experience is null)
        {
            return "";
        }
        var general = _experience.ListGeneralForExecutor(ownedSkills, taskTags);
        var project = task.ProjectId is null
            ? []
            : _experience.ListForExecutor(task.ProjectId, ownedSkills, taskTags);
        var node = task.TemplateId is null
            ? []
            : _experience.ListByTemplateForExecutor(task.TemplateId, ownedSkills, taskTags);

        // НАСКОЛЬКО ЗАПИСЬ ПРИЦЕЛЬНА (меньше — важнее). С T-267-S0 у опыта узла шаблона это
        // РАССТОЯНИЕ ДО УЗЛА: сам узел важнее родителя, родитель важнее деда (опыт предков
        // теперь тоже доезжает до задачи), дальше идёт опыт проекта, потом общие правила
        // организации. Записи опознаются по своим полям, а не по тому, из какого списка
        // пришли: у каждой из трёх разновидностей признак свой (T-11-S0)
        List<string> chain = task.TemplateId is null ? [] : _experience.TemplateChain(task.TemplateId);
        var distance = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < chain.Count; i++)
        {
            distance[chain[i]] = i;
        }
        int Rank(ExperienceRecord r) => r.TemplateTaskId.Length > 0
            // узла нет в цепочке быть не может (записи пришли по ней же), но если шаблон
            // сменили на ходу — такая запись идёт последней среди опыта узлов
            ? (distance.TryGetValue(r.TemplateTaskId, out var d) ? d : chain.Count)
            : r.IsProjectLevel ? chain.Count + 1 : chain.Count + 2;

        var budget = new ExperienceBudget(ExperienceLimitOf(task));
        var (taken, _) = budget.Fit([.. general, .. project, .. node],
            r => ExperienceLine(r, withSkill: r.TemplateTaskId.Length == 0), Rank,
            // ТЭГИ — СИГНАЛ, А НЕ ФИЛЬТР (T-267-S0): отбор их больше не проверяет, зато
            // запись по теме задачи идёт вперёд своих ровесников при дележе бюджета
            r => ExperienceService.TagsOverlap(r.Tags, taskTags));
        var kept = taken.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        // ИСПОЛЬЗОВАННЫЙ ОПЫТ (T-266-S0): запоминаем ровно то, что ушло в текст задания —
        // отбор уже сделан бюджетом, второй раз его гонять незачем. Номер задания здесь ещё
        // неизвестен (промпт собирается ДО того, как задание заведено), поэтому запуск
        // дописывает его сам — по списку usedExperience
        _experience.NoteUsed(task.Id, kept);
        usedExperience?.AddRange(kept);

        var sb = new StringBuilder();
        Append(sb, "prompt.job.21", "prompt.job.22", general, withSkill: true);
        Append(sb, "prompt.job.3", "prompt.job.4", project, withSkill: true);
        Append(sb, "prompt.job.5", "prompt.job.6", node, withSkill: false);
        return sb.ToString();

        void Append(StringBuilder target, string headKey, string hintKey,
            List<ExperienceRecord> records, bool withSkill)
        {
            var shown = records.Where(r => kept.Contains(r.Id)).ToList();
            if (shown.Count == 0)
            {
                return;
            }
            target.AppendLine();
            target.AppendLine(Loc.In(language, headKey));
            target.AppendLine(Loc.In(language, hintKey));
            foreach (var record in shown)
            {
                target.AppendLine(ExperienceLine(record, withSkill));
            }
            target.Append(SkippedNote(records.Count - shown.Count, language));
        }
    }

    /// <summary>Строка записи опыта в промпте: id — чтобы агент мог её поправить
    /// (update_experience / маркер AI2P_EXPERIENCE), код навыка — чтобы было видно, кому
    /// запись адресована.</summary>
    private static string ExperienceLine(ExperienceRecord record, bool withSkill = true)
    {
        var skill = withSkill && record.SkillName.Length > 0 ? $"[{record.SkillName}] " : "";
        return $"- (id: {record.Id}) {skill}{record.Text}";
    }

    /// <summary>Строка «остальное не показано» в конце блока опыта (T-29-S0): о том, что
    /// записи есть, но не поместились в предел, агенту надо СКАЗАТЬ — иначе он считает
    /// показанное полным опытом проекта. Пусто — поместилось всё.</summary>
    private static string SkippedNote(int skipped, string? language) =>
        skipped > 0 ? Loc.In(language, "prompt.job.24", skipped) + "\n" : "";

    /// <summary>
    /// Навыки исполнителя задачи по его декларации возможностей (todo48). Исполнителя нет
    /// или декларация не читается — пустой список: тогда агент получит только записи опыта
    /// без навыка (общие), но не чужие.
    /// </summary>
    private IReadOnlyCollection<string> OwnedSkills(TaskItem task)
    {
        if (_picker is null || task.ExecutorIds.Count == 0)
        {
            return [];
        }
        var executor = _executors.Get(task.ExecutorIds[0]);
        return executor is null ? [] : _picker.DeclaredSkills(executor);
    }

    /// <summary>
    /// Блок «Опыт проекта» промпта задания (ТЗ п. 2.11, todo48): обобщение работы по всему
    /// проекту, которое получает ЛЮБАЯ задача проекта. Отбираются только записи по навыкам
    /// исполнителя (и записи без навыка — они общие): медиа-модели незачем читать выводы
    /// кодировщиков. Пусто — задача вне проекта либо подходящих записей нет.
    /// </summary>
    public static string ProjectExperienceBlock(ExperienceService? experience, string? projectId,
        IReadOnlyCollection<string> ownedSkills, string? language = null,
        IReadOnlyCollection<string>? taskTags = null, ExperienceBudget? budget = null)
    {
        if (experience is null || string.IsNullOrEmpty(projectId))
        {
            return "";
        }
        // отбор с T-29-S0: навык исполнителя, тэги задачи и пометка «загружать всегда»
        var records = experience.ListForExecutor(projectId, ownedSkills, taskTags);
        if (records.Count == 0)
        {
            return "";
        }
        var (taken, skipped) = Fit(records, budget, r => ExperienceLine(r));
        if (taken.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, "prompt.job.3"));
        sb.AppendLine(Loc.In(language, "prompt.job.4"));
        foreach (var record in taken)
        {
            // id записи печатается с T-144: опыт проекта агент теперь не только читает,
            // но и правит (update_experience / маркер AI2P_EXPERIENCE), а без id указать
            // ему, какую именно запись обновить, нечем
            sb.AppendLine(ExperienceLine(record));
        }
        sb.Append(SkippedNote(skipped, language));
        return sb.ToString();
    }

    /// <summary>
    /// Блок «Общие правила работы» промпта задания (T-11-S0): записи опыта, не привязанные
    /// ни к проекту, ни к узлу шаблона, — правила, годные для любого проекта организации.
    /// Их получает КАЖДАЯ задача, в том числе заведённая вне проекта. Отбор по навыкам
    /// исполнителя тот же, что у опыта проекта; id записи печатается по той же причине —
    /// без него агенту нечем указать, какую запись он правит.
    /// </summary>
    public static string GeneralExperienceBlock(ExperienceService? experience,
        IReadOnlyCollection<string> ownedSkills, string? language = null,
        IReadOnlyCollection<string>? taskTags = null, ExperienceBudget? budget = null)
    {
        var records = experience?.ListGeneralForExecutor(ownedSkills, taskTags) ?? [];
        if (records.Count == 0)
        {
            return "";
        }
        var (taken, skipped) = Fit(records, budget, r => ExperienceLine(r));
        if (taken.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, "prompt.job.21"));
        sb.AppendLine(Loc.In(language, "prompt.job.22"));
        foreach (var record in taken)
        {
            sb.AppendLine(ExperienceLine(record));
        }
        sb.Append(SkippedNote(skipped, language));
        return sb.ToString();
    }

    /// <summary>Блок «Опыт выполнения» промпта задания (ТЗ п. 2.11, todo32): задачи с
    /// template_id обязательно получают записи опыта своего узла шаблона (id записи —
    /// для инструмента update_experience). Пусто — задача не из шаблона или опыта нет.
    /// <para>С T-29-S0 к этому блоку применяются навыки исполнителя и тэги задачи, а место
    /// он берёт из общего предела опыта — но правило «нет навыка ⇒ не берём» на него не
    /// распространяется: опыт узла написан ровно про эту работу.</para></summary>
    public static string ExperienceBlock(ExperienceService? experience, string? templateId,
        string? language = null, IReadOnlyCollection<string>? ownedSkills = null,
        IReadOnlyCollection<string>? taskTags = null, ExperienceBudget? budget = null)
    {
        if (experience is null || templateId is null)
        {
            return "";
        }
        var records = experience.ListByTemplateForExecutor(templateId, ownedSkills ?? [], taskTags);
        if (records.Count == 0)
        {
            return "";
        }
        var (taken, skipped) = Fit(records, budget, r => ExperienceLine(r, withSkill: false));
        if (taken.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, "prompt.job.5"));
        sb.AppendLine(Loc.In(language, "prompt.job.6"));
        foreach (var record in taken)
        {
            sb.AppendLine(ExperienceLine(record, withSkill: false));
        }
        sb.Append(SkippedNote(skipped, language));
        return sb.ToString();
    }

    /// <summary>
    /// Ответ человека из Inbox: результат — файл в artifacts/ задачи, job завершается,
    /// задача переходит в review (результат принимает ответственный/автор).
    /// </summary>
    public Job AnswerJob(string jobId, string resultText, string actorId)
    {
        var job = _jobs.Get(jobId) ?? throw new InvalidOperationException(Loc.T("msg.job.1", jobId));
        if (job.State != JobState.WaitingHuman)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.10"));
        }

        var task = _tasks.Get(job.TaskId) ?? throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.2"));
        // задания принадлежат тому же серверу, что и задача (ТЗ гл. 6, этап 42)
        _tasks.EnsureCanWrite(task);
        var slug = task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.Slug;
        var resultPath = _files.WriteTaskArtifact(slug, task.DisplayId, $"{job.DisplayId}-result.md", resultText);

        var updated = _jobs.SetState(jobId, JobState.Done, actorId, resultPath);
        _tasks.ChangeStatus(job.TaskId, TaskStatuses.Review, actorId);
        return updated;
    }

    /// <summary>
    /// Ответ человека на вопрос ИИ-агента в чате задачи (ТЗ v1.17, todo20):
    /// ответ пишется в чат, вопрос закрывается, агент продолжает работу с сохранённого места.
    ///
    /// С T-196 ответить можно и на ЧУЖОЙ задаче: чат общий на весь кластер, и человек,
    /// сидящий за другим сервером, отвечать вправе. Тогда здесь только запись — ответ едет
    /// владельцу задачи репликацией, и работу агента продолжает он
    /// (<see cref="ResumeAnsweredOnce"/>). Ответить успели на обоих серверах — оба ответа
    /// доедут до агента, и решать, что с ними делать, ему (согласуются — работаем дальше,
    /// противоречат — переспросит).
    /// </summary>
    public async Task<ChatMessage> AnswerQuestionAsync(string questionId, string text, string actorId)
    {
        var question = _chat.Get(questionId)
                       ?? throw new InvalidOperationException(Loc.T("msg.chat.1", questionId));
        if (question.Kind != ChatMessageKind.Question)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.11"));
        }
        var owner = _tasks.Get(question.TaskId);
        // задача чужая — пишем ответ и на этом всё: продолжать агента отсюда нечем (его
        // сессия и файлы контекста на сервере-владельце), да и не нужно
        var remote = owner is not null && !_tasks.CanWrite(owner);
        if (question.AnsweredAt is not null)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.3"));
        }
        if (text.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.jobOrchestrator.12"));
        }
        if (question.JobId is null)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.13"));
        }
        var job = _jobs.Get(question.JobId)
                  ?? throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.14", question.JobId));
        if (job.State != JobState.WaitingHuman)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.15"));
        }
        if (remote)
        {
            // ответ на чужой задаче (T-196): только запись — она уедет владельцу задачи,
            // и работу агента продолжит он. Исполнитель здесь может быть даже не разрешим
            // (у ИИ с локальной моделью коннектора на этом сервере нет вовсе)
            var (_, remoteAnswer) = _chat.Answer(questionId, actorId, text);
            Logger.Information("Ответ на вопрос задачи {Task} записан здесь; работу продолжит " +
                               "сервер {Server} (T-196)",
                owner!.DisplayId, _tasks.Scope.Describe(owner.ServerId));
            return remoteAnswer;
        }
        var executor = _executors.Get(job.ExecutorId)
                       ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.4"));

        var (_, answer) = _chat.Answer(questionId, actorId, text);
        await _connectors.Resolve(executor).ResumeJobAsync(job, text.Trim());
        return answer;
    }

    /// <summary>
    /// СНЯТЬ вопрос агента, на который уже некому отвечать (T-1-S1). Случай заказчика:
    /// задание упало (или его остановили) с висящим вопросом в чате — ответ отклоняется
    /// («задание уже не ждёт ответа»), а вопрос при этом навсегда остаётся в счётчике задачи
    /// и в Inbox. Снятие закрывает такой вопрос без ответа.
    ///
    /// Пока задание ЖДЁТ (waiting_human), снять вопрос нельзя: на него можно ответить —
    /// агент продолжит работу, — а если отвечать не хочется, задание останавливают кнопкой
    /// «отменить» (отмена сама закрывает висящие вопросы, ТЗ v1.17). Иначе снятие оставило бы
    /// агента ждать ответа, которого уже никто не даст.
    /// </summary>
    public ChatMessage DismissQuestion(string questionId, string actorId)
    {
        var question = _chat.Get(questionId)
                       ?? throw new InvalidOperationException(Loc.T("msg.chat.1", questionId));
        if (question.Kind != ChatMessageKind.Question)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.5"));
        }
        // чат задачи принадлежит её серверу (ТЗ гл. 6, этап 42) — как и ответ на вопрос
        if (_tasks.Get(question.TaskId) is { } owner)
        {
            _tasks.EnsureCanWrite(owner);
        }
        if (question.JobId is { Length: > 0 } jobId
            && _jobs.Get(jobId) is { State: JobState.WaitingHuman })
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.23"));
        }
        return _chat.Dismiss(questionId, actorId);
    }

    /// <summary>
    /// Запустить авторазбиение задачи на подзадачи (ТЗ v1.26, todo28; кнопка карточки задачи):
    /// ИИ-агент анализирует текст задания и чат и создаёт подзадачи инструментом create_task
    /// (skills + числовой приоритет; исполнители подбираются «сначала ИИ, потом человек»).
    /// Обычная задача: после разбиения подзадачи запускаются автоматически (если правила
    /// позволяют). Шаблон: подзадачи создаются шаблонными, исполнение не запускается,
    /// итоговая сводка — комментарием в чат.
    /// </summary>
    public async Task<Job> StartSplitAsync(string taskId, string? actorId)
    {
        if (_picker is null)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.16"));
        }
        var task = _tasks.Get(taskId) ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        // разбиение — работа над задачей, значит только на её сервере (ТЗ гл. 6, этап 42)
        _tasks.EnsureCanWrite(task);
        if (task.IsNotSplit)
        {
            throw new InvalidOperationException(
                Loc.T("msg.jobOrchestrator.17"));
        }
        if (_jobs.HasActive(taskId))
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.4"));
        }

        // разбивает ИИ: исполнитель задачи, если он ИИ, иначе лучший ИИ команды под skills задачи
        Executor? splitter = null;
        if (task.ExecutorIds.Count == 1)
        {
            var assigned = _executors.Get(task.ExecutorIds[0]);
            if (assigned is { Kind: ExecutorKind.Ai, IsActive: true })
            {
                // неактивный назначенный (todo_bugfix_3) не берётся — подберётся другой ИИ
                splitter = assigned;
            }
        }
        if (splitter is null)
        {
            var picked = _picker.Pick(task.ProjectId, task.TeamId, task.SkillIds, PickMode.AiOnly);
            if (picked.ExecutorId is null)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.jobOrchestrator.18", picked.Reason));
            }
            splitter = _executors.Get(picked.ExecutorId)!;
            if (!task.IsTemplate && task.ExecutorIds.Count == 0)
            {
                // родительской задаче нужен исполнитель — он же проведёт финальный анализ
                task.ExecutorIds = [splitter.Id];
                task = _tasks.Update(task, _tasks.ReadDescription(task), _tasks.ReadAcceptance(task), actorId);
            }
        }
        if (splitter.Kind == ExecutorKind.Ai && _jobs.HasActiveByExecutor(splitter.Id))
        {
            throw new InvalidOperationException(
                Loc.T("msg.jobOrchestrator.7", splitter.Nick));
        }

        Logger.Information("Авторазбиение {Task} ({Mode}): агент {Nick}",
            task.DisplayId, task.IsTemplate ? "шаблон" : "задача", splitter.Nick);
        var connector = _connectors.Resolve(splitter);
        var job = _jobs.Create(taskId, splitter.Id, task.DescriptionPath, actorId);
        if (!task.IsTemplate)
        {
            _tasks.ChangeStatus(taskId, TaskStatuses.InProgress, actorId);
        }
        await connector.SubmitJobAsync(job, BuildSplitRequest(task, splitter));
        return _jobs.Get(job.Id)!;
    }

    /// <summary>
    /// Ведение подзадач авторазбиения (ТЗ v1.26): вызывается при завершении задания разбиения
    /// и каждой подзадачи. Запускает ожидающие подзадачи по убыванию числового приоритета
    /// (каждый ИИ-исполнитель — одно задание за раз; правила безопасности могут запретить
    /// автозапуск — код действия AI2P.Tasks.AutoStart). Когда все подзадачи завершены,
    /// исполнитель родительской задачи проводит финальный анализ отдельным заданием.
    /// </summary>
    public async Task ProcessSplitParentAsync(string parentId, string? actorId)
    {
        await _splitLock.WaitAsync();
        try
        {
            var parent = _tasks.Get(parentId);
            if (parent is null || parent.DeletedAt is not null || parent.IsTemplate
                || !TaskService.HasLaunchFlag(parent.LaunchJson, "autoSplit"))
            {
                return;
            }
            // разбитой задачей занимается её сервер (ТЗ гл. 6, этап 42): и очередью запуска
            // подзадач, и финальным анализом — иначе их вёл бы каждый сервер кластера
            if (!_tasks.CanWrite(parent))
            {
                return;
            }
            // автозапуск ветки выключен человеком (T-54-S0): очередь подзадач авторазбиения и
            // финальный анализ родителя — такие же автоматические старты, как остальные
            if (_tasks.AutoStartOffRootOf(parent) is { } off)
            {
                Logger.Information("Очередь подзадач {Parent} не идёт: у задачи {Root} выключен "
                                   + "автозапуск (T-54-S0)", parent.DisplayId, off.DisplayId);
                return;
            }
            var children = _tasks.ListChildren(parent.Id)
                .Where(c => c.DeletedAt is null && !c.IsTemplate)
                .ToList();
            if (children.Count == 0)
            {
                return;
            }

            // очередь запуска: ожидающие подзадачи по убыванию числового приоритета
            foreach (var child in children
                         .Where(c => c.Status is TaskStatuses.Draft or TaskStatuses.Pending
                                     && c.ExecutorIds.Count == 1
                                     && _tasks.BlockersDone(c) // блокирующие завершены (ТЗ v1.35)
                                     && !WaitsDeferredStart(c)) // ждёт сброса лимита (T-121)
                         .OrderByDescending(c => c.PriorityNum)
                         .ThenBy(c => c.CreatedAt))
            {
                if (!_tasks.CanWrite(child))
                {
                    continue; // подзадача другого сервера запускается там (ТЗ гл. 6, этап 42)
                }
                var executor = _executors.Get(child.ExecutorIds[0]);
                if (executor is null)
                {
                    continue;
                }
                if (!executor.IsActive)
                {
                    // исполнителя выключили после назначения (todo_bugfix_3): не запускаем
                    Logger.Information("Запуск подзадачи {Child} пропущен: исполнитель «{Nick}» не активен",
                        child.DisplayId, executor.Nick);
                    continue;
                }
                // занят назначенный, а в списке замены (T-221) есть свободный — подзадачу
                // берёт он; замены нет — как раньше, подзадача дождётся освобождения
                if (RunExecutorFor(child) is not { } runner)
                {
                    continue;
                }
                executor = runner;
                if (!AutoStartAllowed(child, out var ruleInfo))
                {
                    Logger.Information("Автозапуск {Child} запрещён правилом безопасности: {Rule}",
                        child.DisplayId, ruleInfo);
                    continue;
                }
                try
                {
                    Logger.Information("Авторазбиение {Parent}: запуск подзадачи {Child} " +
                                       "(приоритет {Priority}, исполнитель {Nick})",
                        parent.DisplayId, child.DisplayId, child.PriorityNum, executor.Nick);
                    await StartTaskAsync(child.Id, actorId);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    // ошибка одной подзадачи не останавливает очередь
                    Logger.Warning("Запуск подзадачи {Child} не удался: {Error}", child.DisplayId, ex.Message);
                }
            }

            // все подзадачи завершены → финальный анализ исполнителем родительской задачи
            var allDone = children.All(c =>
                c.Status is TaskStatuses.Review or TaskStatuses.Done or TaskStatuses.Cancelled);
            if (!allDone)
            {
                return;
            }
            // над задачей висит её же агент, ждущий подзадач (T-185): финальный анализ делает
            // ТА ЖЕ сессия — она помнит, как задача разбивалась и почему. Это не новое задание,
            // а продолжение старого, поэтому и активное задание родителя тут не помеха
            if (_jobs.WaitingByTask(parent.Id, JobWaitKinds.Subtasks) is { } waiting)
            {
                await ResumeWaitingParentAsync(parent, waiting, children);
                return;
            }
            if (_jobs.HasActive(parent.Id))
            {
                return;
            }
            // финальный анализ делает ТОТ, КТО ВЁЛ задачу (T-221): если разбивал её запасной
            // исполнитель, подводить итог назначенному незачем — он этой работы не видел.
            // Замену тут не ищем: это продолжение чужой работы, а не новая задача
            var analystId = parent.ActualExecutorId is { Length: > 0 } actual
                ? actual
                : parent.ExecutorIds.Count == 1 ? parent.ExecutorIds[0] : null;
            var parentExecutor = analystId is null ? null : _executors.Get(analystId);
            if (parentExecutor?.Kind != ExecutorKind.Ai)
            {
                return; // финальный анализ — только ИИ; человек видит готовые подзадачи сам
            }
            if (!parentExecutor.IsActive)
            {
                // исполнителя выключили после назначения (todo_bugfix_3): финал не запускается
                Logger.Information("Финальный анализ {Parent} пропущен: исполнитель «{Nick}» не активен",
                    parent.DisplayId, parentExecutor.Nick);
                return;
            }
            if (parentExecutor.Kind == ExecutorKind.Ai && _jobs.HasActiveByExecutor(parentExecutor.Id))
            {
                return; // исполнитель занят — финал запустится при следующем завершении
            }
            if (!_tasks.TryMarkFinalStarted(parent.Id))
            {
                return; // финальный анализ уже запускался
            }
            Logger.Information("Авторазбиение {Parent}: все подзадачи завершены — финальный анализ ({Nick})",
                parent.DisplayId, parentExecutor.Nick);
            var connector = _connectors.Resolve(parentExecutor);
            var job = _jobs.Create(parent.Id, parentExecutor.Id, parent.DescriptionPath, actorId);
            _tasks.ChangeStatus(parent.Id, TaskStatuses.InProgress, actorId);
            await connector.SubmitJobAsync(job, BuildFinalRequest(parent, children));
        }
        finally
        {
            _splitLock.Release();
        }
    }

    /// <summary>
    /// Продолжить работу агента, висевшего в ожидании подзадач (T-185): подзадачи завершены —
    /// той же сессии уходит текст финального анализа, как ответ человека на вопрос. Задание
    /// не заводится заново: агент помнит, зачем задачу разбивали, и сколько уже сделано.
    /// Исполнитель занят другим заданием — ждём: проход повторится при следующем завершении
    /// и раз в минуту сторожем. Сессии уже нет (файл контекста пропал) — задание закрывается,
    /// и финальный анализ пойдёт обычным путём, отдельным заданием.
    /// </summary>
    private async Task ResumeWaitingParentAsync(TaskItem parent, Job waiting, List<TaskItem> children)
    {
        var executor = _executors.Get(waiting.ExecutorId);
        if (executor is null || !executor.IsActive)
        {
            Logger.Information("Финальный анализ {Parent} отложен: исполнитель задания {Job} " +
                               "не найден или не активен", parent.DisplayId, waiting.DisplayId);
            return;
        }
        // «занят» здесь считается по ДРУГИМ заданиям: собственное ждущее задание исполнителя
        // не занимает (JobService.HasActiveByExecutor), поэтому проверка честная
        if (_jobs.HasActiveByExecutor(executor.Id))
        {
            Logger.Information("Финальный анализ {Parent} отложен: исполнитель «{Nick}» занят",
                parent.DisplayId, executor.Nick);
            return;
        }
        try
        {
            Logger.Information("Авторазбиение {Parent}: все подзадачи завершены — продолжаю ту же " +
                               "сессию агента {Nick} финальным анализом (T-185)",
                parent.DisplayId, executor.Nick);
            await _connectors.Resolve(executor)
                .ResumeJobAsync(waiting, BuildFinalRequest(parent, children));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // контекста нет или коннектор не умеет продолжать — закрываем ожидание, и финальный
            // анализ пойдёт обычным путём (следующий проход заведёт новое задание)
            Logger.Warning("Продолжить ждущее задание {Job} не вышло ({Error}) — закрываю ожидание, "
                           + "финальный анализ пойдёт отдельным заданием", waiting.DisplayId, ex.Message);
            _jobs.SetState(waiting.Id, JobState.Done, actorId: null);
        }
    }

    /// <summary>Правила безопасности позволяют автозапуск агента подзадачи (ТЗ v1.26):
    /// решение по коду действия AI2P.Tasks.AutoStart; deny и confirm запрещают автозапуск
    /// (подтверждать некому — запуск остаётся за человеком).</summary>
    private bool AutoStartAllowed(TaskItem child, out string ruleInfo)
    {
        ruleInfo = "";
        if (_security is null)
        {
            return true;
        }
        var evaluator = new SecurityEvaluator(_security.EffectiveForTask(child), projectRoot: null);
        var (decision, rule) = evaluator.ForAction(AutoStartActionCode);
        if (decision == SecurityDecision.Allow)
        {
            return true;
        }
        ruleInfo = rule is null
            ? decision.ToString()
            : $"правило {rule.Scope}: {rule.Pattern} → {rule.Permission}";
        return false;
    }

    // --- иерархический запуск (T-159) ---

    /// <summary>Задача «выполнена» для иерархического запуска (T-159): проверка, готово,
    /// отменено — такие задачи очередь пропускает и не ждёт.</summary>
    private static bool HierarchyDone(string status) => TaskStatuses.Settled(status);

    /// <summary>Обход иерархии сериализован: проход запускает и кнопка, и завершение
    /// каждой подзадачи, и сторож — иначе один и тот же исполнитель получил бы два задания.</summary>
    private readonly SemaphoreSlim _hierarchyLock = new(1, 1);

    /// <summary>Защита от закольцованных parent_id: глубже этого иерархия не обходится.</summary>
    private const int MaxHierarchyDepth = 32;

    /// <summary>
    /// Запустить всю иерархию задачи (T-159, кнопка «запустить иерархию» на карточке).
    /// Корень помечается флагом runHierarchy, и очередь ведётся до конца: подзадачи
    /// запускаются снизу вверх по убыванию приоритета (у потомка есть свои потомки —
    /// сначала они), выполненные (проверка/готово/отменено) пропускаются, занятый
    /// исполнитель дожидается освобождения, а корневой родитель запускается последним.
    /// <para><paramref name="withErrors"/> (галочка в переспросе, T-186) — перезапускать и
    /// задачи, вставшие с ошибкой, на любой глубине поддерева. Каждую такую задачу очередь
    /// перезапускает ОДИН раз: повторная ошибка оставляет её человеку, а новое нажатие
    /// кнопки снимает пометки и даёт ещё попытку.</para>
    /// <para><paramref name="withNeedsFix"/> (вторая галочка переспроса, T-210) — то же самое
    /// для задач в состоянии «доработка»: без неё очередь их не трогает и ждёт человека,
    /// с ней — запускает такие подзадачи на любой глубине, тоже по одной попытке на запуск.</para>
    /// </summary>
    public async Task<HierarchyRunDto> StartHierarchyAsync(string taskId, string? actorId,
        bool withErrors = false, bool withNeedsFix = false)
    {
        var root = _tasks.Get(taskId) ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (root.IsTemplate)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.1"));
        }
        // иерархию ведёт сервер-владелец корня (ТЗ гл. 6, этап 42) — там же, где её запускают
        _tasks.EnsureCanWrite(root);
        if (_tasks.ListChildren(root.Id).All(c => c.IsTemplate))
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.19"));
        }
        _tasks.SetHierarchyRun(root.Id, true);
        // нажали «запустить иерархию» — значит запреты автозапуска на самом корне сняты
        // (T-54-S0): человек прямо сказал «беги», и оставлять пометку, из-за которой очередь
        // потом молчала бы, нельзя. Пометки ПОТОМКОВ не трогаем — это отдельные решения
        if (TaskService.HasLaunchFlag(root.LaunchJson, TaskService.NoAutoStartFlag))
        {
            _tasks.SetLaunchFlag(root.Id, TaskService.NoAutoStartFlag, false);
        }
        // выбор человека в переспросе (T-186, T-210) живёт у корня до конца очереди: его
        // читают и проходы сторожа, и продолжение очереди после перезапуска приложения
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyErrorsFlag, withErrors);
        _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyNeedsFixFlag, withNeedsFix);
        if (withErrors || withNeedsFix)
        {
            // нажали кнопку ещё раз — прошлые перезапуски забыты, у каждой вставшей с
            // ошибкой (и у каждой отправленной в доработку) задачи снова есть одна попытка
            ClearRetries(root, new HashSet<string>(StringComparer.Ordinal), 0, withErrors, withNeedsFix);
        }
        Logger.Information("Иерархический запуск {Task}: очередь открыта, вставшие с ошибкой {Errors}, "
                           + "в доработке {NeedsFix} (T-159, T-186, T-210)",
            root.DisplayId, withErrors ? "перезапускаются" : "не трогаются",
            withNeedsFix ? "запускаются" : "не трогаются");
        return await ProcessHierarchyAsync(root.Id, actorId);
    }

    /// <summary>Снять со всего поддерева пометки «очередь уже запускала эту задачу повторно»
    /// (T-186 — после ошибки, T-210 — из доработки): новое нажатие кнопки даёт каждой такой
    /// задаче ещё одну попытку. Снимается только то, что человек попросил галочкой.</summary>
    private void ClearRetries(TaskItem node, HashSet<string> seen, int depth,
        bool withErrors, bool withNeedsFix)
    {
        if (depth > MaxHierarchyDepth || !seen.Add(node.Id))
        {
            return;
        }
        if (withErrors && TaskService.HasLaunchFlag(node.LaunchJson, TaskService.HierarchyRetryFlag))
        {
            _tasks.SetLaunchFlag(node.Id, TaskService.HierarchyRetryFlag, false);
        }
        if (withNeedsFix && TaskService.HasLaunchFlag(node.LaunchJson, TaskService.HierarchyNeedsFixRetryFlag))
        {
            _tasks.SetLaunchFlag(node.Id, TaskService.HierarchyNeedsFixRetryFlag, false);
        }
        foreach (var child in _tasks.ListChildren(node.Id).Where(c => c.DeletedAt is null && !c.IsTemplate))
        {
            ClearRetries(child, seen, depth + 1, withErrors, withNeedsFix);
        }
    }

    /// <summary>
    /// Один проход очереди иерархического запуска (T-159): обход поддерева корня в глубину,
    /// потомки — по убыванию числового приоритета. Родитель запускается только когда все его
    /// подзадачи выполнены, поэтому корень уходит в работу последним. Проход повторяется
    /// сам — при завершении любой задачи иерархии и раз в минуту сторожем.
    /// </summary>
    public async Task<HierarchyRunDto> ProcessHierarchyAsync(string rootId, string? actorId)
    {
        var run = new HierarchyRunDto();
        await _hierarchyLock.WaitAsync();
        try
        {
            var root = _tasks.Get(rootId);
            if (root is null || root.DeletedAt is not null || root.IsTemplate || !_tasks.CanWrite(root))
            {
                return run;
            }
            if (!TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyFlag))
            {
                // очередь закрыта или остановлена отменённой блокирующей (T-6-S1): проход
                // не идёт вовсе — иначе завершение любой подзадачи снова двигало бы её
                return run;
            }
            // у КОРНЯ-условия и корня-цикла (T-299-S0) анализатор сдаётся РАНЬШЕ потомков:
            // его «сдан» ещё не конец очереди — её закроет обычное «запускать и ждать нечего»
            var flowRoot = TaskFlow.Read(root.LaunchJson).Type is TaskFlow.If or TaskFlow.Loop or TaskFlow.DoLoop
                           && root.Status != TaskStatuses.Cancelled;
            if (TaskStatuses.Settled(root.Status) && !flowRoot)
            {
                // КОРЕНЬ УЖЕ СДАН (T-2-S0): очередь вела к его запуску, а он состоялся —
                // ждать ей больше нечего. Раньше незавершённая подзадача (её никто не берёт:
                // нет исполнителя, ждёт блокирующую) держала очередь открытой навсегда, и
                // выпущенная задача вечно висела в «в работе у ИИ» с пометкой «пауза»,
                // хотя в карточке стояла «проверка» — ровно эта жалоба и завела T-2-S0
                StopHierarchyRun(root, $"корень сдан ({root.Status}) — очередь больше не нужна (T-2-S0)");
                // для вызывающего это обычный конец очереди: запускать нечего и ждать нечего
                run.Finished = true;
                return run;
            }
            // выбор человека в переспросе (T-186, T-210) хранится у корня: очередь идёт сама
            // (завершение подзадачи, проход сторожа), и спросить его больше негде
            var withErrors = TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyErrorsFlag);
            var withNeedsFix = TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyNeedsFixFlag);
            await RunHierarchyNodeAsync(root, run, actorId, new HashSet<string>(StringComparer.Ordinal), 0,
                withErrors, withNeedsFix);
            if (run.Stopped)
            {
                // выполнение иерархии велела остановить задача-условие («завершить выполнение»)
                // или цикл, превысивший предел с флажком остановки (T-299-S0). До T-299-S0 сюда
                // же приводила отменённая блокирующая (T-6-S1) — теперь она считается завершённой
                StopHierarchyRun(root, $"остановку велели задачи {string.Join(", ", run.StoppedBy)}");
                return run;
            }
            // запускать больше нечего и ждать нечего — очередь закрыта
            run.Finished = run.Started.Count == 0 && run.Waiting == 0;
            if (run.Finished && TaskService.HasLaunchFlag(root.LaunchJson, TaskService.HierarchyFlag))
            {
                _tasks.SetHierarchyRun(root.Id, false);
                _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyErrorsFlag, false);
                _tasks.SetLaunchFlag(root.Id, TaskService.HierarchyNeedsFixFlag, false);
                Logger.Information("Иерархический запуск {Task}: очередь закрыта — запускать нечего (T-159)",
                    root.DisplayId);
            }
            return run;
        }
        finally
        {
            _hierarchyLock.Release();
        }
    }

    /// <summary>
    /// Узел иерархии (T-159): сначала поддеревья потомков по убыванию приоритета, потом сам
    /// узел. Возвращает «поддерево улажено» — узел и все его потомки выполнены; пока это не
    /// так, родитель узла не запускается.
    /// </summary>
    private async Task<bool> RunHierarchyNodeAsync(TaskItem node, HierarchyRunDto run, string? actorId,
        HashSet<string> seen, int depth, bool withErrors, bool withNeedsFix)
    {
        if (depth > MaxHierarchyDepth || !seen.Add(node.Id))
        {
            // закольцованный parent_id: считаем улаженным, иначе обход не кончится
            return true;
        }
        if (run.Stopped)
        {
            // выполнение иерархии велено остановить (T-299-S0): больше ничего не запускаем
            return false;
        }
        run.Total++;
        // тип задачи (T-298-S0): условие и циклы обходятся своим порядком (T-299-S0)
        switch (TaskFlow.Read(node.LaunchJson).Type)
        {
            case TaskFlow.If:
                return await RunIfNodeAsync(node, run, actorId, seen, depth, withErrors, withNeedsFix);
            case TaskFlow.Loop:
                return await RunLoopNodeAsync(node, run, actorId, seen, depth, withErrors, withNeedsFix);
            case TaskFlow.DoLoop:
                return await RunDoLoopNodeAsync(node, run, actorId, seen, depth, withErrors, withNeedsFix);
        }
        var childrenSettled = await RunChildrenAsync(node, run, actorId, seen, depth,
            withErrors, withNeedsFix);
        if (run.Stopped)
        {
            return false; // остановку велела задача поддерева (T-299-S0) — родителя не запускаем
        }

        var task = _tasks.Get(node.Id) ?? node; // статус мог смениться, пока шли потомки
        if (HierarchyDone(task.Status))
        {
            run.Skipped++; // выполненные пропускаем (T-159)
            return childrenSettled;
        }
        if (!childrenSettled)
        {
            run.Waiting++; // родитель ждёт своих подзадач
            return false;
        }
        await TryStartInHierarchyAsync(task, run, actorId, withErrors, withNeedsFix);
        return false;
    }

    /// <summary>Поддеревья потомков по убыванию приоритета (T-159); true — все улажены.</summary>
    private async Task<bool> RunChildrenAsync(TaskItem node, HierarchyRunDto run, string? actorId,
        HashSet<string> seen, int depth, bool withErrors, bool withNeedsFix)
    {
        var childrenSettled = true;
        foreach (var child in _tasks.ListChildren(node.Id)
                     .Where(c => c.DeletedAt is null && !c.IsTemplate)
                     .OrderByDescending(c => c.PriorityNum)
                     .ThenBy(c => c.CreatedAt))
        {
            // все ветки обходятся до конца: разным исполнителям задания раздаются сразу,
            // и ранняя остановка на первой незавершённой ветке их бы придержала
            childrenSettled &= await RunHierarchyNodeAsync(child, run, actorId, seen, depth + 1,
                withErrors, withNeedsFix);
        }
        return childrenSettled;
    }

    // ---------- ветвление и циклы в очереди иерархии (T-299-S0) ----------
    // Условия вычисляет АГЕНТ по описанию задачи и чату и сообщает решение действием
    // (T-300-S0 → TaskService.SetFlowDecision → ключ flowDecision launch_json). Очередь его
    // только читает, когда задание задачи завершилось, и двигает дерево.

    /// <summary>
    /// Задача «Условие» (T-299-S0): запускается СРАЗУ, в потомков очередь не проваливается,
    /// пока агент не решил. Решение применяется один раз: непройденная ветка (задача и всё её
    /// поддерево) отменяется, у выбранной ветки без задачи — либо агент уже завёл потомков
    /// («создавать задачи»), либо выполнение иерархии останавливается («завершить»). Дальше
    /// потомки идут штатно.
    /// </summary>
    private async Task<bool> RunIfNodeAsync(TaskItem node, HierarchyRunDto run, string? actorId,
        HashSet<string> seen, int depth, bool withErrors, bool withNeedsFix)
    {
        var task = _tasks.Get(node.Id) ?? node;
        if (task.Status == TaskStatuses.Cancelled)
        {
            // отменили само условие — не пойдёт ни одна его ветка
            run.Skipped++;
            return true;
        }
        if (!HierarchyDone(task.Status))
        {
            if (TaskFlowRun.Applied(task.LaunchJson) && task.Status is TaskStatuses.Draft or TaskStatuses.Pending)
            {
                // условие вернули в работу заново (человек, перезапуск) — прежнее решение
                // больше не действует
                _tasks.EditFlowRun(task.Id, ClearFlowRun);
                task = _tasks.Get(task.Id) ?? task;
            }
            await TryStartInHierarchyAsync(task, run, actorId, withErrors, withNeedsFix);
            return false;
        }
        if (!TaskFlowRun.Applied(task.LaunchJson))
        {
            var flow = TaskFlow.Read(task.LaunchJson);
            if (TaskFlowRun.Decision(task.LaunchJson) is not { } decision)
            {
                // агент решения не сообщил: выбирать ветку за него AI2P не вправе
                // (основное правило T-297-S0) — выполнение иерархии останавливается, решает человек
                StopRun(run, task, "условие без решения агента");
                _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.StoppedKey] = true); // знак Stop диаграммы (T-301-S0)
                return false;
            }
            var chosen = decision ? flow.IfTrueTaskId : flow.IfFalseTaskId;
            var other = decision ? flow.IfFalseTaskId : flow.IfTrueTaskId;
            var children = _tasks.ListChildren(task.Id).Where(c => c.DeletedAt is null && !c.IsTemplate)
                .ToList();
            if (other is { Length: > 0 } && !string.Equals(other, chosen, StringComparison.Ordinal)
                && children.FirstOrDefault(c => c.Id == other) is { } skipped)
            {
                var count = CancelSubtree(skipped, actorId, onlyWaiting: false);
                Logger.Information("Условие {Task}: решение {Decision}, непройденная ветка {Branch} "
                                   + "отменена ({Count} задач) (T-299-S0)",
                    task.DisplayId, decision, skipped.DisplayId, count);
            }
            _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.AppliedKey] = true);
            if (chosen is not { Length: > 0 }
                && (decision ? flow.IfTrueStopHierarchy : flow.IfFalseStopHierarchy))
            {
                StopRun(run, task, $"условие → {(decision ? "Да" : "Нет")}: завершить выполнение");
                _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.StoppedKey] = true); // знак Stop диаграммы (T-301-S0)
                return false;
            }
        }
        // решение применено — потомки (выбранная ветка, заведённые агентом, прочие) идут штатно
        return await RunChildrenAsync(task, run, actorId, seen, depth, withErrors, withNeedsFix);
    }

    /// <summary>
    /// «Цикл до» (T-299-S0): задача-анализатор запускается ДО потомков. Условия выполнены —
    /// задача встаёт на паузу «ждёт окончания цикла», потомки идут штатно; последний потомок
    /// выполнен — анализатор перезапускается и проверяет условия снова (исполнитель между
    /// кругами свободен). Не выполнены на первом же круге — ожидающие и черновые потомки
    /// отменяются. Предел кругов — <see cref="TaskFlowRun.LoopLimit"/>.
    /// </summary>
    private async Task<bool> RunLoopNodeAsync(TaskItem node, HierarchyRunDto run, string? actorId,
        HashSet<string> seen, int depth, bool withErrors, bool withNeedsFix)
    {
        var task = _tasks.Get(node.Id) ?? node;
        if (task.Status == TaskStatuses.Cancelled)
        {
            run.Skipped++;
            return true;
        }
        if (TaskFlowRun.InLoopBody(task.LaunchJson))
        {
            // идёт тело цикла: ждём всех потомков
            if (!await RunChildrenAsync(task, run, actorId, seen, depth, withErrors, withNeedsFix))
            {
                run.Waiting++;
                return false;
            }
            var pass = TaskFlowRun.LoopPass(task.LaunchJson) + 1;
            _tasks.EditFlowRun(task.Id, root =>
            {
                root.Remove(TaskFlowRun.LoopBodyKey);
                root.Remove(TaskFlowRun.DecisionKey);
                root[TaskFlowRun.LoopPassKey] = pass;
            });
            if (pass >= LoopLimitOf(task))
            {
                return FinishLoop(task, run, actorId, $"пройдено кругов {pass} — предел цикла", exceeded: true);
            }
            // круг окончен — анализатор перепроверяет условия (перезапуск, как restart_task_for_recheck)
            Logger.Information("Цикл {Task}: круг {Pass} окончен — перепроверка условий (T-299-S0)",
                task.DisplayId, pass);
            await RestartAnalyzerAsync(task, run, actorId, withErrors, withNeedsFix);
            return false;
        }
        if (TaskFlowRun.LoopDone(task.LaunchJson))
        {
            if (HierarchyDone(task.Status))
            {
                return true; // цикл окончен
            }
            // цикл запустили заново — прежний счёт кругов больше не действует
            _tasks.EditFlowRun(task.Id, ClearFlowRun);
            task = _tasks.Get(task.Id) ?? task;
        }
        if (!HierarchyDone(task.Status))
        {
            // анализатор не отработал: запускаем его сам, в потомков не проваливаемся
            await TryStartInHierarchyAsync(task, run, actorId, withErrors, withNeedsFix);
            return false;
        }
        // анализатор отработал — читаем решение
        var decision = TaskFlowRun.Decision(task.LaunchJson);
        var done = TaskFlowRun.LoopPass(task.LaunchJson);
        _tasks.EditFlowRun(task.Id, root => root.Remove(TaskFlowRun.DecisionKey));
        if (decision is null)
        {
            StopRun(run, task, "цикл без решения агента");
            _tasks.EditFlowRun(task.Id, root =>
            {
                root[TaskFlowRun.LoopDoneKey] = true;
                root[TaskFlowRun.StoppedKey] = true; // знак Stop диаграммы (T-301-S0)
            });
            return false;
        }
        if (decision == false)
        {
            if (done == 0)
            {
                // условие не выполнено на первом же круге: тело не нужно вовсе
                var count = CancelChildren(task, actorId);
                Logger.Information("Цикл {Task}: условие не выполнено на первом круге — отменено "
                                   + "ожидающих потомков {Count} (T-299-S0)", task.DisplayId, count);
            }
            return FinishLoop(task, run, actorId, "условия цикла не выполнены", exceeded: false);
        }
        if (done >= LoopLimitOf(task))
        {
            return FinishLoop(task, run, actorId, $"пройдено кругов {done} — предел цикла", exceeded: true);
        }
        // условия выполнены: новый круг тела
        if (done > 0)
        {
            ResetSubtreeForRound(task, actorId);
        }
        _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.LoopBodyKey] = true);
        _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId);
        Logger.Information("Цикл {Task}: круг {Pass} — задача ждёт окончания цикла (T-299-S0)",
            task.DisplayId, done + 1);
        task = _tasks.Get(task.Id) ?? task;
        await RunChildrenAsync(task, run, actorId, seen, depth, withErrors, withNeedsFix);
        run.Waiting++;
        return false;
    }

    /// <summary>
    /// «Цикл после» (T-299-S0): потомки идут первыми, как у обычного родителя; задача-анализатор
    /// запускается после всех потомков. Условия выполнены — поддерево возвращается в «ожидает»
    /// и проходит новый круг, задача ждёт на паузе «ждёт окончания цикла»; после круга
    /// анализатор перезапускается. Предел кругов и остановка — как у «Цикла до».
    /// </summary>
    private async Task<bool> RunDoLoopNodeAsync(TaskItem node, HierarchyRunDto run, string? actorId,
        HashSet<string> seen, int depth, bool withErrors, bool withNeedsFix)
    {
        var task = _tasks.Get(node.Id) ?? node;
        if (task.Status == TaskStatuses.Cancelled)
        {
            run.Skipped++;
            return true;
        }
        if (TaskFlowRun.LoopDone(task.LaunchJson))
        {
            if (HierarchyDone(task.Status))
            {
                return true;
            }
            _tasks.EditFlowRun(task.Id, ClearFlowRun);
            task = _tasks.Get(task.Id) ?? task;
        }
        var childrenSettled = await RunChildrenAsync(task, run, actorId, seen, depth, withErrors, withNeedsFix);
        if (!childrenSettled)
        {
            run.Waiting++; // тело цикла идёт — анализатор ждёт
            return false;
        }
        if (TaskFlowRun.InLoopBody(task.LaunchJson))
        {
            // круг тела окончен — анализатор перепроверяет условия
            _tasks.EditFlowRun(task.Id, root => root.Remove(TaskFlowRun.LoopBodyKey));
            Logger.Information("Цикл после {Task}: круг окончен — перепроверка условий (T-299-S0)",
                task.DisplayId);
            await RestartAnalyzerAsync(task, run, actorId, withErrors, withNeedsFix);
            return false;
        }
        if (!HierarchyDone(task.Status))
        {
            await TryStartInHierarchyAsync(task, run, actorId, withErrors, withNeedsFix);
            return false;
        }
        var decision = TaskFlowRun.Decision(task.LaunchJson);
        var pass = TaskFlowRun.LoopPass(task.LaunchJson) + 1; // круг тела только что пройден
        _tasks.EditFlowRun(task.Id, root =>
        {
            root.Remove(TaskFlowRun.DecisionKey);
            root[TaskFlowRun.LoopPassKey] = pass;
        });
        if (decision is null)
        {
            StopRun(run, task, "цикл без решения агента");
            _tasks.EditFlowRun(task.Id, root =>
            {
                root[TaskFlowRun.LoopDoneKey] = true;
                root[TaskFlowRun.StoppedKey] = true; // знак Stop диаграммы (T-301-S0)
            });
            return false;
        }
        if (decision == false)
        {
            return FinishLoop(task, run, actorId, "условия цикла не выполнены", exceeded: false);
        }
        if (pass >= LoopLimitOf(task))
        {
            return FinishLoop(task, run, actorId, $"пройдено кругов {pass} — предел цикла", exceeded: true);
        }
        // новый круг: поддерево — в «ожидает», задача ждёт окончания цикла
        ResetSubtreeForRound(task, actorId);
        _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.LoopBodyKey] = true);
        _tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId);
        Logger.Information("Цикл после {Task}: круг {Pass} — задача ждёт окончания цикла (T-299-S0)",
            task.DisplayId, pass + 1);
        task = _tasks.Get(task.Id) ?? task;
        await RunChildrenAsync(task, run, actorId, seen, depth, withErrors, withNeedsFix);
        run.Waiting++;
        return false;
    }

    /// <summary>Предел кругов цикла задачи (T-299-S0): свой recheckLimit, иначе проекта.</summary>
    private int LoopLimitOf(TaskItem task) =>
        TaskFlowRun.LoopLimit(TaskFlow.Read(task.LaunchJson), _tasks.RecheckLimitOf(task.Id));

    /// <summary>Цикл окончен (T-299-S0). Анализатор, стоявший на паузе «ждёт окончания цикла»,
    /// сдаётся в «готово»; при превышении предела и флажке «остановить выполнение всей
    /// иерархии» очередь закрывается, иначе обход просто идёт дальше.</summary>
    private bool FinishLoop(TaskItem task, HierarchyRunDto run, string? actorId, string reason, bool exceeded)
    {
        _tasks.EditFlowRun(task.Id, root =>
        {
            root.Remove(TaskFlowRun.LoopBodyKey);
            root[TaskFlowRun.LoopDoneKey] = true;
        });
        Logger.Information("Цикл {Task} окончен: {Reason} (T-299-S0)", task.DisplayId, reason);
        if (!HierarchyDone(task.Status))
        {
            _tasks.ChangeStatus(task.Id, TaskStatuses.Done, actorId);
        }
        if (exceeded && TaskFlow.Read(task.LaunchJson).LoopStopHierarchy)
        {
            StopRun(run, task, reason);
            _tasks.EditFlowRun(task.Id, root => root[TaskFlowRun.StoppedKey] = true); // знак Stop диаграммы (T-301-S0)
            return false;
        }
        return true;
    }

    /// <summary>Перезапуск задачи-анализатора цикла между кругами (T-299-S0): снова «ожидает»
    /// и сразу в очередь запуска — исполнитель между кругами был свободен и мог брать потомков.
    /// Пометки «очередь уже перезапускала» снимаются, как у restart_task_for_recheck.</summary>
    private async Task RestartAnalyzerAsync(TaskItem task, HierarchyRunDto run, string? actorId,
        bool withErrors, bool withNeedsFix)
    {
        _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyRetryFlag, false);
        _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyNeedsFixRetryFlag, false);
        _tasks.ChangeStatus(task.Id, TaskStatuses.Pending, actorId);
        var fresh = _tasks.Get(task.Id) ?? task;
        await TryStartInHierarchyAsync(fresh, run, actorId, withErrors, withNeedsFix);
    }

    /// <summary>Остановить выполнение всей иерархии (T-299-S0): очередь закроет корень по
    /// пометке прохода (<see cref="HierarchyRunDto.Stopped"/>) — ДО любых новых запусков.</summary>
    private static void StopRun(HierarchyRunDto run, TaskItem task, string reason)
    {
        Logger.Information("Иерархия: выполнение останавливает {Task} — {Reason} (T-299-S0)",
            task.DisplayId, reason);
        run.Stopped = true;
        if (!run.StoppedBy.Contains(task.DisplayId, StringComparer.Ordinal))
        {
            run.StoppedBy.Add(task.DisplayId);
        }
    }

    /// <summary>Снять состояние ветвления/цикла — задача начинает заново.</summary>
    private static void ClearFlowRun(System.Text.Json.Nodes.JsonObject root)
    {
        foreach (var key in TaskFlowRun.Keys)
        {
            root.Remove(key);
        }
    }

    /// <summary>Отменить задачу и всё её поддерево (непройденная ветка условия, T-299-S0).
    /// onlyWaiting — трогать только «ожидает» и «черновик». Сданные задачи не трогаются.
    /// Возвращает число отменённых.</summary>
    private int CancelSubtree(TaskItem node, string? actorId, bool onlyWaiting, int depth = 0)
    {
        if (depth > MaxHierarchyDepth || node.IsTemplate || node.DeletedAt is not null)
        {
            return 0;
        }
        var count = 0;
        foreach (var child in _tasks.ListChildren(node.Id))
        {
            count += CancelSubtree(child, actorId, onlyWaiting, depth + 1);
        }
        var task = _tasks.Get(node.Id) ?? node;
        var cancel = onlyWaiting
            ? task.Status is TaskStatuses.Pending or TaskStatuses.Draft
            : !HierarchyDone(task.Status);
        if (cancel && _tasks.CanWrite(task))
        {
            _tasks.ChangeStatus(task.Id, TaskStatuses.Cancelled, actorId);
            count++;
        }
        return count;
    }

    /// <summary>Все потомки задачи с подпотомками в «ожидает»/«черновик» → «отменена»
    /// (условие «Цикла до» не выполнено на первом круге, T-299-S0).</summary>
    private int CancelChildren(TaskItem task, string? actorId) =>
        _tasks.ListChildren(task.Id).Sum(c => CancelSubtree(c, actorId, onlyWaiting: true));

    /// <summary>Новый круг цикла (T-299-S0): всё поддерево, сданное в прошлом круге, снова
    /// «ожидает», а вложенные условия и циклы забывают прошлые решения.</summary>
    private void ResetSubtreeForRound(TaskItem task, string? actorId)
    {
        void Reset(TaskItem node, int depth)
        {
            if (depth > MaxHierarchyDepth || node.IsTemplate || node.DeletedAt is not null)
            {
                return;
            }
            var current = _tasks.Get(node.Id) ?? node;
            if (_tasks.CanWrite(current))
            {
                _tasks.EditFlowRun(current.Id, root =>
                {
                    ClearFlowRun(root);
                    root.Remove(TaskService.HierarchyRetryFlag);
                    root.Remove(TaskService.HierarchyNeedsFixRetryFlag);
                });
                if (HierarchyDone(current.Status) || current.Status is TaskStatuses.Error or TaskStatuses.NeedsFix)
                {
                    _tasks.ChangeStatus(current.Id, TaskStatuses.Pending, actorId);
                }
            }
            foreach (var child in _tasks.ListChildren(current.Id))
            {
                Reset(child, depth + 1);
            }
        }
        foreach (var child in _tasks.ListChildren(task.Id))
        {
            Reset(child, 1);
        }
    }

    /// <summary>Задачу, вставшую с ошибкой, очередь перезапускает (T-186): галочка в
    /// переспросе стоит, а эта очередь её ещё не перезапускала.</summary>
    private static bool CanRestartAfterError(TaskItem task, bool withErrors) =>
        withErrors && task.Status == TaskStatuses.Error
                   && !TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyRetryFlag);

    /// <summary>Задачу в состоянии «доработка» очередь запускает (T-210): вторая галочка
    /// переспроса стоит, а эта очередь её ещё не запускала. Попытка одна на запуск по той же
    /// причине, что у ошибки: задачу могут вернуть в доработку прямо в ходе очереди (проверка
    /// родителем, человек), и без счёта попыток вышел бы круг «доработка → запуск → доработка».</summary>
    private static bool CanRestartAfterNeedsFix(TaskItem task, bool withNeedsFix) =>
        withNeedsFix && task.Status == TaskStatuses.NeedsFix
                     && !TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyNeedsFixRetryFlag);

    /// <summary>Запуск одной задачи очередью иерархии (T-159): те же условия, что у очереди
    /// авторазбиения — свой сервер, назначенный активный исполнитель, завершённые блокирующие,
    /// свободный исполнитель и разрешающее правило безопасности. Задача, вставшая с ошибкой,
    /// запускается заново, если человек попросил галочкой в переспросе (T-186); задача
    /// в доработке — по второй галочке (T-210).</summary>
    private async Task TryStartInHierarchyAsync(TaskItem task, HierarchyRunDto run, string? actorId,
        bool withErrors, bool withNeedsFix)
    {
        var restartAfterError = CanRestartAfterError(task, withErrors);
        var startFromNeedsFix = CanRestartAfterNeedsFix(task, withNeedsFix);
        if (task.Status is not (TaskStatuses.Draft or TaskStatuses.Pending)
            && !restartAfterError && !startFromNeedsFix)
        {
            // выполняется, ждёт ответа, встала с ошибкой или в доработке (галочки нет либо
            // повторный запуск уже был) — иерархия ждёт человека или агента
            run.Waiting++;
            return;
        }
        if (!_tasks.CanWrite(task))
        {
            // задача другого сервера физически запускается ТАМ (ТЗ гл. 6, этап 42), но с
            // T-196 очередь её не бросает: владельцу уходит заявка на запуск, а иерархия
            // ждёт — завершение приедет сюда репликацией. До T-196 такой узел просто
            // пропускался, и родитель мог стартовать раньше своей чужой подзадачи
            if (_runRequests is null)
            {
                Logger.Information("Иерархия: {Task} пропущена — задача сервера {Server}",
                    task.DisplayId, task.ServerCode);
                run.Skipped++;
                return;
            }
            try
            {
                if (RequestRun(task.Id, RunRequestKinds.Task, withErrors: false, actorId) is { } request)
                {
                    Logger.Information("Иерархия: запуск {Task} передан на сервер {Server} (T-196)",
                        task.DisplayId, request.TargetServerCode);
                    run.Requested++;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Logger.Warning("Иерархия: заявка на запуск {Task} не подана: {Error}",
                    task.DisplayId, ex.Message);
            }
            run.Waiting++;
            return;
        }
        if (task.ExecutorIds.Count != 1)
        {
            Logger.Information("Иерархия: {Task} пропущена — исполнитель не назначен", task.DisplayId);
            run.Skipped++;
            return;
        }
        var executor = _executors.Get(task.ExecutorIds[0]);
        if (executor is null || !executor.IsActive)
        {
            Logger.Information("Иерархия: {Task} пропущена — исполнитель не найден или не активен",
                task.DisplayId);
            run.Skipped++;
            return;
        }
        if (!_tasks.BlockersDone(task))
        {
            // блокирующие задачи ещё не готовы (ТЗ п. 2.12): очередь ждёт — задачу тронет
            // с места завершение (готово/отмена, T-299-S0) последней блокирующей
            run.Waiting++;
            return;
        }
        if (WaitsDeferredStart(task))
        {
            run.Waiting++; // ждёт сброса лимита (T-121) — поднимет сторож
            return;
        }
        if (IsBusy(executor))
        {
            // назначенный уже работает над другой задачей. С T-221 сначала смотрим список
            // «могут заменить исполнителя»: есть свободный — задача идёт к нему, и ждать
            // нечего; нет — как раньше, ждём освобождения назначенного (T-159)
            if (FreeSubstitute(task, executor, force: false) is not { } substitute)
            {
                Logger.Information("Иерархия: {Task} ждёт — исполнитель «{Nick}» занят, свободной "
                                   + "замены нет", task.DisplayId, executor.Nick);
                run.Waiting++;
                return;
            }
            Logger.Information("Иерархия: {Task} — «{Nick}» занят, работу берёт «{Substitute}» (T-221)",
                task.DisplayId, executor.Nick, substitute.Nick);
            executor = substitute;
        }
        if (!AutoStartAllowed(task, out var ruleInfo))
        {
            Logger.Information("Иерархия: запуск {Task} запрещён правилом безопасности: {Rule}",
                task.DisplayId, ruleInfo);
            run.Skipped++;
            return;
        }
        try
        {
            if (restartAfterError)
            {
                // одна попытка на очередь (T-186): пометка ставится ДО запуска, иначе
                // повторная ошибка тут же вернула бы задачу в перезапуск — и так по кругу
                _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyRetryFlag, true);
                Logger.Information("Иерархия: {Task} перезапускается после ошибки (T-186)", task.DisplayId);
            }
            if (startFromNeedsFix)
            {
                // та же одна попытка на очередь (T-210): иначе задача, которую снова вернули
                // в доработку, тут же ушла бы в работу опять — и так по кругу
                _tasks.SetLaunchFlag(task.Id, TaskService.HierarchyNeedsFixRetryFlag, true);
                Logger.Information("Иерархия: {Task} запускается из доработки (T-210)", task.DisplayId);
            }
            Logger.Information("Иерархия: запуск {Task} (приоритет {Priority}, исполнитель {Nick})",
                task.DisplayId, task.PriorityNum, executor.Nick);
            await StartTaskAsync(task.Id, actorId);
            run.Started.Add(task.DisplayId);
            if (restartAfterError)
            {
                run.Restarted++;
            }
            if (startFromNeedsFix)
            {
                run.Reworked++;
            }
        }
        catch (AgentLimitLowException ex)
        {
            // лимит исполнителя почти исчерпан (T-121): старт перенесён, поднимет сторож
            Logger.Information("Иерархия: {Task} перенесена на {At} — лимит исполнителя",
                task.DisplayId, ex.StartAfter.ToLocalTime());
            run.Waiting++;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // ошибка одной задачи не останавливает очередь
            Logger.Warning("Иерархия: запуск {Task} не удался: {Error}", task.DisplayId, ex.Message);
            run.Skipped++;
        }
    }

    /// <summary>Корень открытой иерархии над задачей (T-159): сама задача или ближайший
    /// предок с флагом runHierarchy. null — задача не в иерархическом запуске.
    /// <para>Наружу открыт с T-263: по нему карточка задачи решает, надо ли переспрашивать
    /// про остановку всей иерархии — снятия одного задания там мало.</para>
    /// <para>Само правило с T-31-S0 живёт в хранилище: им пользуется не только очередь, но и
    /// перезапуск задачи агентом — тому надо поднять у корня галочку «запускать и задачи
    /// в доработке», а инструментам агента оркестратор недоступен.</para></summary>
    public TaskItem? HierarchyRootOf(TaskItem task) => _tasks.HierarchyRootOf(task);

    /// <summary>
    /// Проход очередей всех открытых иерархий (T-159) — раз в минуту сторожем. Завершение
    /// задачи иерархии двигает очередь само, но исполнитель мог освободиться на задаче ИЗ
    /// ДРУГОЙ иерархии (или вовсе вне её) — тогда ждущую подзадачу трогает с места этот проход.
    /// </summary>
    public void RunHierarchiesOnce()
    {
        try
        {
            foreach (var root in _tasks.ListHierarchyRuns())
            {
                if (!_tasks.CanWrite(root))
                {
                    continue; // иерархию ведёт сервер-владелец корня (ТЗ гл. 6, этап 42)
                }
                try
                {
                    ProcessHierarchyAsync(root.Id, actorId: null).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    Logger.Warning("Проход иерархии {Task} не удался: {Error}", root.DisplayId, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проверка иерархических запусков не удалась: {Error}", ex.Message);
        }
    }

    // --- запуск задачи С ДРУГОГО СЕРВЕРА (T-196) ---

    /// <summary>
    /// Попросить сервер-ВЛАДЕЛЬЦА задачи запустить её (T-196): нажать «запустить» человек
    /// может на любом сервере кластера, но задание пойдёт там, где лежит задача. Заявка —
    /// обычная строка, она едет владельцу репликацией, и он разбирает её у себя
    /// (<see cref="ApplyRunRequestsOnce"/>).
    ///
    /// Уже запущенное не трогаем: есть активное задание — заявки не будет вовсе (по условию
    /// «если задача или иерархия уже запущена, ничего не делаем»). Решающая проверка всё
    /// равно у владельца: здешние данные о его заданиях приезжают репликацией и отстают.
    /// </summary>
    /// <returns>Заявка; null — запускать нечего (задача уже в работе либо заявка уже подана).</returns>
    /// <param name="withNeedsFix">Иерархия: запускать и задачи в доработке (T-210). Стоит
    /// ПОСЛЕДНИМ и с умолчанием нарочно: прежние вызовы и тесты от этого не переписываются.</param>
    /// <param name="renew">
    /// ЧЕЛОВЕК НАЖАЛ КНОПКУ СНОВА (T-160-S0): прежнюю свою заявку того же вида снять и подать
    /// новую. Без этого повторный удалённый запуск не работал вовсе — отметка «заявка
    /// отработана» лежит в нереплицируемой таблице у ВЛАДЕЛЬЦА задачи, автору она не видна,
    /// и давно выполненная (а потом остановленная) заявка навсегда глушила новое нажатие:
    /// человеку писали «запуск передан на S0», а там не происходило ничего.
    /// <para>Умолчание false — для очереди иерархии: она зовёт этот метод на каждом проходе
    /// по чужой подзадаче (раз в несколько секунд), и «перезаводить» заявку там означало бы
    /// поток строк на репликацию. Ей достаточно одной висящей заявки: как только задача
    /// тронется, автор снимет её сам (<see cref="RunRequestService.Cleanup"/>).</para>
    /// </param>
    public RunRequest? RequestRun(string taskId, string kind, bool withErrors, string? actorId,
        bool withNeedsFix = false, bool renew = false)
    {
        if (_runRequests is null)
        {
            throw new InvalidOperationException(Loc.T("msg.runRequest.2"));
        }
        var task = _tasks.Get(taskId) ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (task.IsTemplate)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.1"));
        }
        if (_tasks.CanWrite(task))
        {
            // задача наша — заявка не нужна, запускаем напрямую
            throw new InvalidOperationException(Loc.T("msg.runRequest.3", task.DisplayId));
        }
        var single = kind != RunRequestKinds.Hierarchy;
        if (single && _jobs.HasActive(task.Id))
        {
            Logger.Information("Запуск {Task} с чужого сервера не нужен — задание уже идёт (T-196)",
                task.DisplayId);
            return null;
        }
        if (!single && TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyFlag))
        {
            Logger.Information("Иерархия {Task} уже запущена — заявка не нужна (T-196)", task.DisplayId);
            return null;
        }
        var wantedKind = single ? RunRequestKinds.Task : RunRequestKinds.Hierarchy;
        if (_runRequests.HasOpen(task.Id, wantedKind))
        {
            if (!renew)
            {
                Logger.Information("Заявка на запуск {Task} уже подана — второй не заводим (T-196)",
                    task.DisplayId);
                return null;
            }
            // повторное нажатие человека: прежняя заявка на том конце может быть давно
            // отработана, и новую под тем же идентификатором никто не выполнит (T-160-S0)
            _runRequests.DropOpen(task.Id, wantedKind);
            Logger.Information("Прежняя заявка на запуск {Task} снята — подаём новую (T-160-S0)",
                task.DisplayId);
        }
        var request = _runRequests.Add(task, single ? RunRequestKinds.Task : RunRequestKinds.Hierarchy,
            withErrors, withNeedsFix, actorId);
        Logger.Information("Запуск {Task} ({Kind}) передан на сервер {Server} заявкой {Id} (T-196)",
            task.DisplayId, request.Kind, _tasks.Scope.Describe(task.ServerId), request.Id);
        return request;
    }

    /// <summary>
    /// Разобрать заявки на запуск, приехавшие с других серверов (T-196) — проход сторожа.
    /// Берутся только заявки по СВОИМ задачам: чужие разберёт их владелец. Отметка «отработал»
    /// ставится ДО запуска и живёт в своей, нереплицируемой таблице — повторно та же заявка
    /// не сработает ни после перезапуска, ни после нового сеанса репликации.
    /// </summary>
    public void ApplyRunRequestsOnce()
    {
        if (_runRequests is null)
        {
            return;
        }
        try
        {
            foreach (var request in _runRequests.Unapplied())
            {
                var task = _tasks.Get(request.TaskId);
                if (task is null || task.DeletedAt is not null || task.IsTemplate || !_tasks.CanWrite(task))
                {
                    continue; // не наша задача — заявку разберёт её владелец
                }
                _runRequests.MarkApplied(request.Id, ApplyRunRequest(request, task));
            }
            _runRequests.Cleanup();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Разбор заявок на запуск не удался: {Error}", ex.Message);
        }
    }

    /// <summary>Выполнить одну заявку; возвращает итог словами — он остаётся в отметке
    /// об исполнении и виден при разборе полётов.</summary>
    private string ApplyRunRequest(RunRequest request, TaskItem task)
    {
        try
        {
            if (RunRequestKinds.IsStop(request.Kind))
            {
                // остановка с другого сервера (T-263): выполняется тем же кодом, что и
                // кнопка «остановить» здесь, — правила владения проверяет он сам
                Logger.Information("Заявка {Id} с сервера {Server}: останавливаю {Task} ({Kind}) (T-263)",
                    request.Id, request.ServerCode, task.DisplayId, request.Kind);
                var stop = StopTaskAsync(task.Id, request.RequestedBy,
                        withHierarchy: request.Kind != RunRequestKinds.Stop,
                        withChildren: request.Kind == RunRequestKinds.StopHierarchyAll)
                    .GetAwaiter().GetResult();
                return stop.Nothing
                    ? "stop-nothing"
                    : $"stopped: jobs {stop.Jobs}, hierarchy {(stop.HierarchyStopped ? "closed" : "-")}";
            }
            if (request.Kind == RunRequestKinds.Hierarchy)
            {
                if (TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyFlag))
                {
                    Logger.Information("Заявка {Id}: иерархия {Task} уже запущена — ничего не делаем (T-196)",
                        request.Id, task.DisplayId);
                    return "hierarchy-already-running";
                }
                // запущенные одиночно задачи очередь иерархии не трогает: она их дожидается
                // (TryStartInHierarchyAsync) — это и есть «запуск с обходом уже запущенных»
                Logger.Information("Заявка {Id} с сервера {Server}: запускаю иерархию {Task} (T-196)",
                    request.Id, request.ServerCode, task.DisplayId);
                StartHierarchyAsync(task.Id, request.RequestedBy, request.WithErrors, request.WithNeedsFix)
                    .GetAwaiter().GetResult();
                return "hierarchy-started";
            }
            if (_jobs.HasActive(task.Id))
            {
                Logger.Information("Заявка {Id}: задача {Task} уже выполняется — ничего не делаем (T-196)",
                    request.Id, task.DisplayId);
                return "already-running";
            }
            Logger.Information("Заявка {Id} с сервера {Server}: запускаю задачу {Task} (T-196)",
                request.Id, request.ServerCode, task.DisplayId);
            StartTaskAsync(task.Id, request.RequestedBy).GetAwaiter().GetResult();
            return "started";
        }
        catch (AgentLimitLowException ex)
        {
            // лимит исполнителя (T-121): старт перенесён, задачу поднимет сторож — заявка
            // своё дело сделала
            Logger.Information("Заявка {Id}: старт {Task} перенесён на {At} — лимит исполнителя",
                request.Id, task.DisplayId, ex.StartAfter.ToLocalTime());
            return "deferred";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Logger.Warning("Заявка {Id} на запуск {Task} не выполнена: {Error}",
                request.Id, task.DisplayId, ex.Message);
            return "error: " + ex.Message;
        }
    }

    /// <summary>Промпт задания авторазбиения (ТЗ v1.26): текст задачи + чат + инструкция
    /// создать подзадачи инструментом create_task и не выполнять их самому. Остаток лимита
    /// исполнителя (T-121) подсказывает агенту, насколько мелкими делать подзадачи.</summary>
    public string BuildSplitRequest(TaskItem task, Executor splitter)
    {
        var language = AgentLanguageOf(task);
        var sb = new StringBuilder();
        sb.AppendLine(BuildAiRequest(task).TrimEnd());
        var chat = _chat.ListByTask(task.Id);
        if (chat.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(language, "prompt.job.7"));
            foreach (var message in chat.TakeLast(50))
            {
                // перенесённое импортом обсуждение подписано автором внешней системы (T-152)
                var nick = message.AuthorName is { Length: > 0 } author
                    ? author
                    : _executors.Get(message.FromExecutorId)?.Nick ?? message.FromExecutorId;
                sb.AppendLine($"- {nick}: {message.Text}");
            }
        }
        // остаток лимита (T-121): подзадачи, которые в него не поместятся, система запустит
        // сама после сброса окна — агенту важно знать, какого размера куски делать
        if (_executors.LimitStatus(splitter) is { } limit)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.In(language, "prompt.job.8"));
            sb.AppendLine(
                Loc.In(language, "prompt.job.9", splitter.Nick, limit.Remaining, limit.Limit, limit.WindowHours) +
                (limit.ResetAt is { } reset ? Loc.In(language, "prompt.job.10", reset.ToLocalTime()) : "") +
                Loc.In(language, "prompt.job.11"));
        }
        var aiCount = TeamAiCount(task.TeamId);
        sb.AppendLine();
        sb.AppendLine(Loc.In(language, "prompt.job.12"));
        sb.AppendLine(
            Loc.In(language, "prompt.job.13", SplitWay(splitter, language), aiCount));
        sb.AppendLine(task.IsTemplate
            ? Loc.In(language, "prompt.job.14")
            : Loc.In(language, "prompt.job.15"));
        return sb.ToString();
    }

    /// <summary>Промпт финального анализа (ТЗ v1.26): все подзадачи выполнены — исполнитель
    /// родительской задачи сверяет результаты, при необходимости дорабатывает и пишет итог.</summary>
    private string BuildFinalRequest(TaskItem parent, List<TaskItem> children)
    {
        var language = AgentLanguageOf(parent);
        var sb = new StringBuilder();
        sb.AppendLine(BuildAiRequest(parent).TrimEnd());
        sb.AppendLine();
        sb.AppendLine(Loc.In(language, "prompt.job.12"));
        sb.AppendLine(
            Loc.In(language, "prompt.job.16"));
        sb.AppendLine();
        sb.AppendLine(Loc.In(language, "prompt.job.17"));
        foreach (var child in children)
        {
            sb.AppendLine(Loc.In(language, "prompt.job.18", child.DisplayId, child.Title, child.Status));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Чем разбивающий агент создаёт подзадачи (T-125): у CLI-агента инструментов AI2P нет —
    /// он делает это маркером в ответе, у остальных есть инструмент create_task. Профайл
    /// не читается (не задан, повреждён) — считаем, что инструмент есть: так было всегда.
    /// </summary>
    private string SplitWay(Executor splitter, string? language)
    {
        try
        {
            if (_connectors.ProfileOf(splitter).IsCli)
            {
                return Loc.In(language, "prompt.job.19", ClaudeCliConnector.SubtaskMarker);
            }
        }
        catch (InvalidOperationException)
        {
            // профайла нет — задание всё равно упадёт позже, с понятной ошибкой
        }
        return Loc.In(language, "prompt.job.20");
    }

    /// <summary>Число активных ИИ-исполнителей команды (для промпта разбиения). Активность
    /// двойная (T-129): и в справочнике исполнителей, и в составе этой команды.</summary>
    /// <summary>Язык ОБЩЕНИЯ с агентом по задаче (T-190): язык команды задачи
    /// (<c>Team.AgentLanguage</c>, ТЗ п. 2.8 и гл. 9). Команды нет — null: промпт соберётся
    /// на языке установки.</summary>
    private string? AgentLanguageOf(TaskItem task) =>
        task.TeamId is null ? null : _teams.Get(task.TeamId)?.AgentLanguage;

    private int TeamAiCount(string? teamId)
    {
        if (teamId is null)
        {
            return 0;
        }
        var team = _teams.Get(teamId);
        return team is null
            ? 0
            : team.Members.Count(m => m.IsActive
                                      && _executors.Get(m.ExecutorId) is { Kind: ExecutorKind.Ai, IsActive: true });
    }

    // --- зависшие задания (T-117): автопродолжение и кнопка «продолжить» ---

    /// <summary>Фора после последней смены состояния задания (T-117): раньше неё running-задание
    /// зависшим не считается — SubmitJobAsync ставит running чуть раньше старта фонового вызова.</summary>
    public static readonly TimeSpan StallGrace = TimeSpan.FromMinutes(2);

    private Timer? _watchdog;

    /// <summary>
    /// Задание зависло (T-117): в БД running, а живого фонового вызова у коннектора нет —
    /// так остаются задания после перезапуска приложения (и оставались после таймаута CLI,
    /// пока таймаут не стал ошибкой задания). Такое задание толкают повторной подачей —
    /// сторож автоматически, человек кнопкой «продолжить».
    /// </summary>
    public bool IsStalled(Job job)
    {
        if (job.State != JobState.Running || DateTime.UtcNow - job.UpdatedAt < StallGrace)
        {
            return false;
        }
        var executor = _executors.Get(job.ExecutorId);
        // «не человек» (T-153-S0): зависшее задание бывает и у «авто ПО» — процесс упал вместе
        // с приложением, а строка осталась running. Человека это не касается: у него задание
        // висит ровно столько, сколько он его делает
        if (executor is null || executor.Kind == ExecutorKind.Human)
        {
            return false;
        }
        try
        {
            return !_connectors.Resolve(executor).HasActiveRun(job.Id);
        }
        catch (InvalidOperationException)
        {
            // профайл/коннектор не разрешился — продолжение всё равно не удастся
            return false;
        }
    }

    /// <summary>
    /// «Толкнуть» зависшее running-задание (T-117): повторная подача тому же исполнителю
    /// тем же заданием. Текст запроса берётся из сохранённого дампа J-N-request.json
    /// (там полный промпт исходного запуска — с разбиением, опытом и чатом), а если дампа
    /// нет — собирается заново. Агент уже работает — понятная ошибка вместо второго запуска.
    /// </summary>
    public async Task<Job> ContinueJobAsync(string jobId, string? actorId)
    {
        var job = _jobs.Get(jobId) ?? throw new InvalidOperationException(Loc.T("msg.job.1", jobId));
        var task = _tasks.Get(job.TaskId) ?? throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.2"));
        // задание выполняется на сервере задачи — там же и продолжается (ТЗ гл. 6, этап 42)
        _tasks.EnsureCanWrite(task);
        if (job.State != JobState.Running)
        {
            throw new InvalidOperationException(
                Loc.T("msg.jobOrchestrator.20"));
        }
        var executor = _executors.Get(job.ExecutorId)
                       ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.4"));
        if (executor.Kind != ExecutorKind.Ai)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.21"));
        }
        var connector = _connectors.Resolve(executor);
        if (connector.HasActiveRun(job.Id))
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.22"));
        }
        Logger.Information("Задание {Job} по задаче {Task}: повторная подача зависшего задания (T-117)",
            job.DisplayId, task.DisplayId);
        await connector.SubmitJobAsync(job, SavedRequestText(task, job));
        return _jobs.Get(job.Id)!;
    }

    /// <summary>Текст запроса исходного запуска из дампа tasks/&lt;id&gt;/ai/J-N-request.json;
    /// дампа нет или не читается — промпт собирается заново (BuildAiRequest).</summary>
    private string SavedRequestText(TaskItem task, Job job)
    {
        var slug = task.ProjectId is null ? null : _projects.Get(task.ProjectId)?.Slug;
        var rel = Path.Combine(_files.TaskDirRel(slug, task.DisplayId), "ai", $"{job.DisplayId}-request.json");
        try
        {
            var json = _files.ReadText(rel);
            if (json.Trim().Length > 0)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("message", out var m)
                    && m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // дамп повреждён — соберём промпт заново
        }
        return BuildAiRequest(task);
    }

    /// <summary>Сторож (T-117): раз в минуту толкает свои running-задания без живого вызова —
    /// так задача не остаётся «в работе» навсегда после перезапуска. Он же запускает задачи
    /// с наступившим отложенным стартом (T-121), двигает очереди иерархий (T-159), будит
    /// агентов, чьё ожидание кончилось (T-185), и разбирает заявки на запуск, приехавшие
    /// с других серверов (T-196).</summary>
    public void StartWatchdog() =>
        _watchdog ??= new Timer(_ =>
            {
                WatchStalledOnce();
                StartDeferredOnce();
                RunHierarchiesOnce(); // очереди иерархических запусков (T-159)
                ResumeWaitingAgentsOnce(); // ожидание ответа агента и подзадач (T-185)
                ApplyRunRequestsOnce(); // запуск, запрошенный с другого сервера (T-196)
            }, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    // --- приостановленные агенты (T-185) ---

    /// <summary>
    /// Один проход по агентам, которые висят в ожидании (T-185):
    /// • ждущим ОТВЕТА АГЕНТА соседней задачи — если собеседник написал в наш чат, вопрос
    ///   закрывается его сообщением и работа продолжается той же сессией;
    /// • ждущим СВОИХ ПОДЗАДАЧ — проверяется, не готовы ли они (обычно это двигает завершение
    ///   самой подзадачи, но подзадача могла закрыться на другом сервере или в обход события).
    /// Отдельного канала «сервер → фоновое задание» в системе нет, поэтому проверка идёт
    /// опросом раз в минуту, тем же сторожем, что и остальные очереди.
    /// </summary>
    public void ResumeWaitingAgentsOnce()
    {
        try
        {
            foreach (var job in _jobs.ListWaiting(JobWaitKinds.Agent))
            {
                var task = _tasks.Get(job.TaskId);
                if (task is null || task.DeletedAt is not null || !_tasks.CanWrite(task))
                {
                    continue;
                }
                if (_chat.PendingQuestionByJob(job.Id) is not { } question
                    || _chat.AgentReplyTo(question) is not { } reply)
                {
                    continue; // собеседник ещё не ответил; ответить может и человек — из Inbox
                }
                var executor = _executors.Get(job.ExecutorId);
                if (executor is null || !executor.IsActive)
                {
                    continue;
                }
                try
                {
                    Logger.Information("Задача {Task}: агент задачи-собеседника ответил — продолжаю " +
                                       "работу задания {Job} (T-185)", task.DisplayId, job.DisplayId);
                    _chat.CloseByAgentReply(question.Id, reply.Id);
                    _connectors.Resolve(executor)
                        .ResumeJobAsync(job, AgentReplyText(reply, AgentLanguageOf(task)))
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                               or NotSupportedException)
                {
                    Logger.Warning("Продолжение задания {Job} ответом агента не удалось: {Error}",
                        job.DisplayId, ex.Message);
                }
            }

            foreach (var job in _jobs.ListWaiting(JobWaitKinds.Subtasks))
            {
                var task = _tasks.Get(job.TaskId);
                if (task is null || task.DeletedAt is not null || !_tasks.CanWrite(task))
                {
                    continue;
                }
                ProcessSplitParentAsync(task.Id, actorId: null).GetAwaiter().GetResult();
            }

            ResumeAnsweredOnce(); // ответ человека, приехавший с другого сервера (T-196)
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проверка приостановленных агентов не удалась: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Продолжить своих агентов, чей вопрос ответили НА ДРУГОМ СЕРВЕРЕ (T-196). Локальный
    /// ответ продолжает работу сразу (<see cref="AnswerQuestionAsync"/>), но ответ, записанный
    /// на соседнем сервере, приходит сюда репликацией — строкой в чате, без всякого вызова.
    /// Поэтому раз в минуту смотрим свои ждущие задания: вопрос закрыт, а задание всё ещё
    /// ждёт — значит ответ приехал, и работу пора продолжать.
    ///
    /// Ответов может оказаться НЕСКОЛЬКО (два сервера ответили, пока реплика не доехала) —
    /// агенту уходят все: сойдутся, он работает дальше, разойдутся — переспросит в чате.
    /// </summary>
    public void ResumeAnsweredOnce()
    {
        foreach (var job in _jobs.ListWaitingHuman())
        {
            var task = _tasks.Get(job.TaskId);
            if (task is null || task.DeletedAt is not null || !_tasks.CanWrite(task))
            {
                continue;
            }
            // вопрос задания ещё висит — ответа не было (его ждут человек из Inbox или
            // собеседник-агент, это соседний проход)
            if (_chat.PendingQuestionByJob(job.Id) is not null)
            {
                continue;
            }
            var question = _chat.LastQuestionByJob(job.Id);
            if (question is null || _chat.AnswersTo(question.Id) is not { Count: > 0 } answers)
            {
                continue; // вопрос сняли без ответа (T-1-S1) — продолжать нечем
            }
            var executor = _executors.Get(job.ExecutorId);
            if (executor is null || !executor.IsActive)
            {
                continue;
            }
            try
            {
                Logger.Information("Задача {Task}: ответ на вопрос пришёл с другого сервера " +
                                   "({Count} шт.) — продолжаю задание {Job} (T-196)",
                    task.DisplayId, answers.Count, job.DisplayId);
                _connectors.Resolve(executor)
                    .ResumeJobAsync(job, RemoteAnswerText(answers, AgentLanguageOf(task)))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or NotSupportedException)
            {
                Logger.Warning("Продолжение задания {Job} ответом с другого сервера не удалось: {Error}",
                    job.DisplayId, ex.Message);
            }
        }
    }

    /// <summary>Текст продолжения, когда ответ пришёл репликацией (T-196). Один ответ —
    /// он и есть текст; несколько (ответили на разных серверах) — все подряд, с прямым
    /// указанием: согласуются — работать дальше, противоречат — задать вопрос заново.
    /// Читает это АГЕНТ, поэтому язык — язык команды задачи (T-190).</summary>
    private string RemoteAnswerText(IReadOnlyList<ChatMessage> answers, string? language)
    {
        if (answers.Count == 1)
        {
            return answers[0].Text;
        }
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, "prompt.talk.16", answers.Count));
        sb.AppendLine();
        foreach (var answer in answers)
        {
            var nick = _executors.Get(answer.FromExecutorId)?.Nick ?? answer.FromExecutorId;
            sb.AppendLine($"--- [{answer.CreatedAt:yyyy-MM-dd HH:mm} UTC] {nick}:");
            sb.AppendLine(answer.Text);
            sb.AppendLine();
        }
        sb.Append(Loc.In(language, "prompt.talk.17"));
        return sb.ToString();
    }

    /// <summary>Текст, которым продолжается работа спросившего агента (T-185): подписанный
    /// ответ собеседника — агент должен понимать, что это ответ на его вопрос, а не новое
    /// указание человека. Текст читает АГЕНТ, поэтому язык — язык команды задачи (T-190).</summary>
    private string AgentReplyText(ChatMessage reply, string? language) =>
        Loc.In(language, "prompt.talk.15",
            _executors.Get(reply.FromExecutorId)?.Nick ?? reply.FromExecutorId)
        + "\n\n" + reply.Text;

    /// <summary>Один проход сторожа (отдельно — для тестов и ручного вызова).</summary>
    public void WatchStalledOnce()
    {
        try
        {
            foreach (var job in _jobs.ListRunning())
            {
                var task = _tasks.Get(job.TaskId);
                if (task is null || task.DeletedAt is not null
                    || !_tasks.CanWrite(task) || !IsStalled(job))
                {
                    continue;
                }
                Logger.Warning(
                    "Задание {Job} по задаче {Task} висит «в работе» без живого вызова — толкаю агента (T-117)",
                    job.DisplayId, task.DisplayId);
                try
                {
                    ContinueJobAsync(job.Id, actorId: null).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    Logger.Warning("Автопродолжение задания {Job} не удалось: {Error}",
                        job.DisplayId, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проверка зависших заданий не удалась: {Error}", ex.Message);
        }
    }

    public void Dispose() => _watchdog?.Dispose();

    // --- ОСТАНОВКА ЗАДАЧИ И ИЕРАРХИИ (T-263) ---

    /// <summary>
    /// ОСТАНОВИТЬ работу по задаче (T-263) — то, что делает кнопка «остановить».
    ///
    /// До T-263 она снимала только текущее задание, и на задаче ИЗ ОТКРЫТОЙ ИЕРАРХИИ это
    /// не помогало: очередь на ближайшем же проходе (завершение соседней подзадачи или
    /// сторож раз в минуту) запускала задачу снова. Поэтому останавливать надо саму очередь:
    /// с корня снимается пометка runHierarchy — тем же способом, что при отменённой
    /// блокирующей (T-6-S1).
    ///
    /// <para><paramref name="withChildren"/> — второй исход переспроса: снять задания и у
    /// всех работающих задач поддерева. Без него уже запущенные потомки доигрывают: очередь
    /// закрыта, новых запусков не будет, но начатую работу агента не рвём.</para>
    ///
    /// <para>КЛАСТЕР: задание снимается там, где оно идёт (ТЗ гл. 6), а очередь ведёт сервер
    /// корня. Чужое отсюда не остановить — уезжает заявка, как у запуска (T-196), и владелец
    /// разбирает её у себя тем же методом.</para>
    /// </summary>
    /// <param name="withHierarchy">Закрыть и очередь иерархии, в которой идёт задача.</param>
    public async Task<TaskStopDto> StopTaskAsync(string taskId, string? actorId,
        bool withHierarchy = false, bool withChildren = false)
    {
        var task = _tasks.Get(taskId)
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (task.IsTemplate)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.1"));
        }
        var result = new TaskStopDto { TaskId = task.Id, TaskDisplayId = task.DisplayId };
        // очередь закрывается ПЕРВОЙ: сними мы сначала задание, проход очереди (его двигает
        // и завершение задания) успел бы запустить эту же задачу заново
        var root = withHierarchy ? HierarchyRootOf(task) : null;
        var foreignRoot = false;
        if (root is not null)
        {
            result.HierarchyRoot = root.DisplayId;
            if (_tasks.CanWrite(root))
            {
                StopHierarchyRun(root, $"остановлено человеком по задаче {task.DisplayId} (T-263)");
                result.HierarchyStopped = true;
            }
            else
            {
                // очередь ведёт сервер корня — остановка уезжает туда заявкой (T-196)
                foreignRoot = true;
                RequestStop(root, withChildren
                    ? RunRequestKinds.StopHierarchyAll
                    : RunRequestKinds.StopHierarchy, actorId, result);
            }
        }
        if (withChildren && !foreignRoot)
        {
            // «и работающих потомков тоже»: поддерево корня очереди, а если её уже нет
            // (закрылась сама, пока человек думал) — поддерево самой задачи. Иначе просьба
            // остановить всё молча не сделала бы ничего
            await StopSubtreeJobsAsync(root ?? task, actorId, result,
                new HashSet<string>(StringComparer.Ordinal), 0);
        }
        // задание самой задачи: у корня чужой иерархии его снимет та же заявка выше
        if (root is null || root.Id != task.Id || _tasks.CanWrite(root))
        {
            await StopTaskJobsAsync(task, actorId, result);
        }
        result.Nothing = result.Jobs == 0 && !result.HierarchyStopped && result.Requested.Count == 0;
        return result;
    }

    /// <summary>Задача, выключившая автозапуск для этой ветки (T-54-S0), — сама задача или её
    /// предок. null — автозапуск разрешён. Наружу открыт для карточки задачи.</summary>
    public TaskItem? AutoStartOffRootOf(TaskItem task) => _tasks.AutoStartOffRootOf(task);

    /// <summary>
    /// ВЫКЛЮЧИТЬ (или снова включить) АВТОЗАПУСК подзадач ветки — кнопка «остановить
    /// автозапуск» на карточке (T-54-S0).
    ///
    /// <para>Кнопки «остановить» (T-263) для этого не хватало: она снимает текущее задание и
    /// закрывает ОТКРЫТУЮ очередь иерархии, а автоматические старты идут и без очереди —
    /// завершение задачи само поднимает её потомков (todo22), ждавшие её задачи (ТЗ п. 2.12)
    /// и очередь авторазбиения. Поэтому человек, переводивший разобранные подзадачи в
    /// «готово», каждым переводом запускал работу заново, и остановить это было нечем.</para>
    ///
    /// <para>Выключение заодно делает всё, что имеет смысл при слове «остановить»: закрывает
    /// открытую очередь иерархии над задачей и, если попросили, снимает задания всего
    /// поддерева. Включение — только снимает пометку, само ничего не запускает.</para>
    /// </summary>
    /// <param name="on">true — автозапуск разрешён (пометка снимается), false — выключен.</param>
    /// <param name="withChildren">Заодно снять задания работающих задач поддерева.</param>
    public async Task<TaskStopDto> SetAutoStartAsync(string taskId, string? actorId,
        bool on, bool withChildren = false)
    {
        var task = _tasks.Get(taskId)
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (task.IsTemplate)
        {
            throw new InvalidOperationException(Loc.T("msg.jobOrchestrator.1"));
        }
        // пометка живёт в самой задаче и уезжает репликацией: автозапуск ведёт сервер-владелец
        // задачи, и решение человека обязано доехать до него (ТЗ гл. 6, этап 42)
        _tasks.EnsureCanWrite(task);
        _tasks.SetLaunchFlag(task.Id, TaskService.NoAutoStartFlag, !on);
        var result = new TaskStopDto { TaskId = task.Id, TaskDisplayId = task.DisplayId };
        Logger.Information("Автозапуск подзадач {Task} {State} человеком (T-54-S0)",
            task.DisplayId, on ? "включён" : "выключен");
        if (on)
        {
            return result; // включение ничего не останавливает и ничего не запускает
        }
        // открытая очередь иерархии автозапуском не считается (её открыл человек кнопкой), но
        // «остановить автозапуск» при идущей очереди без её закрытия было бы обманом
        if (HierarchyRootOf(task) is { } root && _tasks.CanWrite(root))
        {
            result.HierarchyRoot = root.DisplayId;
            StopHierarchyRun(root, $"выключен автозапуск по задаче {task.DisplayId} (T-54-S0)");
            result.HierarchyStopped = true;
        }
        if (withChildren)
        {
            await StopSubtreeJobsAsync(task, actorId, result,
                new HashSet<string>(StringComparer.Ordinal), 0);
        }
        result.Nothing = result.Jobs == 0 && !result.HierarchyStopped && result.Requested.Count == 0;
        return result;
    }

    /// <summary>Снять активные задания ОДНОЙ задачи (T-263); чужая задача — заявка её серверу.
    /// Заданий может быть несколько (перезапуски), поэтому снимаются все активные.</summary>
    private async Task StopTaskJobsAsync(TaskItem task, string? actorId, TaskStopDto result)
    {
        var active = _jobs.ListByTask(task.Id)
            .Where(j => j.State is JobState.Queued or JobState.Running or JobState.WaitingHuman)
            .ToList();
        if (active.Count == 0)
        {
            return;
        }
        if (!_tasks.CanWrite(task))
        {
            RequestStop(task, RunRequestKinds.Stop, actorId, result);
            return;
        }
        foreach (var job in active)
        {
            try
            {
                await CancelJobAsync(job.Id, actorId);
                result.Jobs++;
                if (!result.Stopped.Contains(task.DisplayId))
                {
                    result.Stopped.Add(task.DisplayId);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Logger.Warning("Остановка задания {Job} задачи {Task} не удалась: {Error}",
                    job.DisplayId, task.DisplayId, ex.Message);
                result.Errors.Add($"{task.DisplayId}: {ex.Message}");
            }
        }
    }

    /// <summary>Снять задания всего поддерева (T-263, «и работающих потомков тоже»): обход
    /// такой же, как у очереди запуска, — с защитой от закольцованных parent_id.</summary>
    private async Task StopSubtreeJobsAsync(TaskItem node, string? actorId, TaskStopDto result,
        HashSet<string> seen, int depth)
    {
        if (depth > MaxHierarchyDepth || !seen.Add(node.Id))
        {
            return;
        }
        await StopTaskJobsAsync(node, actorId, result);
        foreach (var child in _tasks.ListChildren(node.Id)
                     .Where(c => c.DeletedAt is null && !c.IsTemplate))
        {
            await StopSubtreeJobsAsync(child, actorId, result, seen, depth + 1);
        }
    }

    /// <summary>
    /// Попросить сервер-ВЛАДЕЛЬЦА остановить у себя задачу или иерархию (T-263): та же
    /// заявка, что у запуска (T-196), — своим кодом.
    /// <para>Повторное нажатие ЗАВОДИТ ЗАЯВКУ ЗАНОВО (T-160-S0): прежнюю, свою и открытую,
    /// снимаем и кладём новую. Старая заявка на том конце может быть давно отработана —
    /// отметка об этом лежит у ВЛАДЕЛЬЦА и автору не видна, — и «второй такой же не заводим»
    /// означало бы «поручение больше не передаётся никогда».</para>
    /// </summary>
    private void RequestStop(TaskItem task, string kind, string? actorId, TaskStopDto result)
    {
        if (_runRequests is null)
        {
            result.Errors.Add(Loc.T("msg.runRequest.2"));
            return;
        }
        result.TargetServer = _tasks.Scope.Describe(task.ServerId) is { Length: > 0 } named
            ? named
            : Loc.T("msg.runRequest.4");
        // прежняя заявка снимается молча: человеку важно, что поручение УШЛО, а «заявка уже
        // подана» он читал бы как «ничего не сделано, ждите» — и был бы прав, см. T-160-S0
        if (_runRequests.DropOpen(task.Id, kind) > 0)
        {
            Logger.Information("Прежняя заявка на остановку {Task} ({Kind}) снята — подаём новую (T-160-S0)",
                task.DisplayId, kind);
        }
        var request = _runRequests.Add(task, kind, withErrors: false, withNeedsFix: false, actorId);
        result.Requested.Add(task.DisplayId);
        Logger.Information("Остановка {Task} ({Kind}) передана на сервер {Server} заявкой {Id} (T-263)",
            task.DisplayId, kind, result.TargetServer, request.Id);
    }

    /// <summary>Остановка активного задания (для ИИ — кнопка «остановить», ТЗ v1.14);
    /// задача возвращается в pending.</summary>
    public async Task<Job> CancelJobAsync(string jobId, string? actorId)
    {
        var job = _jobs.Get(jobId) ?? throw new InvalidOperationException(Loc.T("msg.job.1", jobId));
        // задание выполняется на сервере задачи — там же оно и снимается (ТЗ гл. 6, этап 42)
        if (_tasks.Get(job.TaskId) is { } owner)
        {
            _tasks.EnsureCanWrite(owner);
        }
        var executor = _executors.Get(job.ExecutorId);
        if (executor is not null)
        {
            try
            {
                await _connectors.Resolve(executor).CancelAsync(jobId);
            }
            catch (InvalidOperationException)
            {
                // профайл/провайдер недоступен — задание всё равно снимается
                _jobs.SetState(jobId, JobState.Cancelled, actorId);
            }
        }
        else
        {
            _jobs.SetState(jobId, JobState.Cancelled, actorId);
        }
        _tasks.ChangeStatus(job.TaskId, TaskStatuses.Pending, actorId);
        return _jobs.Get(jobId)!;
    }
}
