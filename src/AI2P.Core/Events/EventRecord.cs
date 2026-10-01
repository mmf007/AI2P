namespace AI2P.Core.Events;

/// <summary>
/// Событие журнала — первичный источник истины по истории (ТЗ п. 3 принцип 3, п. 6.4.3).
/// Глобально-уникальный id + монотонный seq в пределах узла (node_id) — для слияния журналов.
/// </summary>
public sealed class EventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NodeId { get; set; } = "";
    public long Seq { get; set; }
    public DateTime Ts { get; set; }
    /// <summary>Исполнитель-инициатор; null — система.</summary>
    public string? ActorId { get; set; }
    /// <summary>Денормализовано — для фильтров журнала и репликации.</summary>
    public string? ProjectId { get; set; }
    public string? TaskId { get; set; }
    public string EventType { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

/// <summary>Стартовый набор типов событий (ТЗ п. 6.4.3, расширяемый).</summary>
public static class EventTypes
{
    public const string ProjectCreated = "project.created";
    public const string ProjectUpdated = "project.updated";
    /// <summary>Проект удалён целиком со своим содержимым (T-44-S0, выпуск 1.105):
    /// «удалить совсем» на форме выбора «перенести в архив или удалить».</summary>
    public const string ProjectDeleted = "project.deleted";
    public const string TaskCreated = "task.created";
    public const string TaskUpdated = "task.updated";
    public const string TaskStatusChanged = "task.status_changed";
    public const string TaskAssigned = "task.assigned";
    public const string TaskDeleted = "task.deleted";
    /// <summary>Удаление файла задачи из окна «все файлы» (ТЗ v1.45, todo37_3).</summary>
    public const string TaskFileDeleted = "task.file_deleted";
    /// <summary>Перенос задачи по иерархии — drag-n-drop в представлении «иерархия» (todo37).</summary>
    public const string TaskMoved = "task.moved";
    /// <summary>Смена сервера-владельца задачи (ТЗ гл. 6, этап 42): запись делает ПРЕЖНИЙ
    /// владелец — единственный писатель строки сохраняется, новый её просто принимает.</summary>
    public const string TaskServerChanged = "task.server_changed";
    /// <summary>Задачи выбывшего из кластера сервера переданы дирижёру (ТЗ гл. 6, этап 42):
    /// единственный случай, когда владельца задачи меняет не её сервер.</summary>
    public const string TasksTakenOver = "task.taken_over";
    public const string JobSubmitted = "job.submitted";
    /// <summary>Задание принято внешним движком в очередь (ТЗ v1.45, todo37_3): ComfyUI
    /// вернул prompt_id — по событию видно, что генерация реально стартовала.</summary>
    public const string JobQueued = "job.queued";
    public const string JobStateChanged = "job.state_changed";
    public const string JobCompleted = "job.completed";
    public const string AgentRequest = "agent.request";
    public const string AgentResponse = "agent.response";
    /// <summary>Вызовы файловых инструментов агентом (function calling, ТЗ п. 2.4, todo17).</summary>
    public const string AgentToolCalls = "agent.tool_calls";
    /// <summary>ПОДМЕНА МОДЕЛИ (T-359-S0): фактически ответила не та модель, которую просили
    /// в профайле записи справочника. У Claude Code CLI такое даёт подписка без доступа к
    /// старшей модели, а прежде это было видно только строкой в логе — запись
    /// «Claude-Opus-5.5_cli», молча работающая на Sonnet, выглядела исправной.</summary>
    public const string AgentModelMismatch = "agent.model_mismatch";
    /// <summary>Отработала МОДЕЛЬ-СУФЛЁР перед медиа-заданием (T-288-S0): в полезной нагрузке —
    /// модель, путь файла управляющего json, токены, стоимость и названные поля. Отдельный тип,
    /// а не <see cref="AgentRequest"/>: это ВТОРОЙ вызов модели в том же задании, и в журнале
    /// работ он обязан быть отличим от запроса к самой рабочей модели.</summary>
    public const string PrompterRun = "job.prompter";
    public const string ChatMessage = "chat.message";
    /// <summary>Вопрос ИИ-агента в чате задачи (ТЗ v1.17): агент ждёт ответа человека.</summary>
    public const string ChatQuestion = "chat.question";
    /// <summary>Ответ на вопрос ИИ-агента (ТЗ v1.17): агент продолжает работу.</summary>
    public const string ChatAnswer = "chat.answer";
    /// <summary>Вопрос агента СНЯТ человеком без ответа (T-1-S1): агента уже нет, отвечать
    /// некому — вопрос закрыт, чтобы не висел в счётчиках задачи и в Inbox.</summary>
    public const string ChatQuestionDismissed = "chat.question_dismissed";
    /// <summary>Запуск задачи ЗАПРОШЕН с другого сервера (T-196): заявка едет владельцу
    /// задачи, физически запускает её он.</summary>
    public const string RunRequested = "task.run_requested";
    /// <summary>Остановка ЗАПРОШЕНА с другого сервера (T-263): заявка на снятие задания
    /// (или на закрытие очереди иерархии) едет владельцу задачи — останавливает он.
    /// Отдельный тип, а не <see cref="RunRequested"/>: в журнале работ строка «запуск
    /// передан» на самом деле означала бы обратное действие.</summary>
    public const string StopRequested = "task.stop_requested";
    /// <summary>Переименование проекта: каталог хранилища переименован (ТЗ v1.17).</summary>
    public const string ProjectRenamed = "project.renamed";
    /// <summary>Пер-серверная часть проекта (каталог, папка Common, активность на этом
    /// сервере) изменена (ТЗ гл. 6, этап 42).</summary>
    public const string ProjectServerUpdated = "project.server_updated";
    public const string HumanConfirmRequested = "human.confirm_requested";
    public const string HumanConfirmAnswered = "human.confirm_answered";
    public const string RuleTriggered = "rule.triggered";
    public const string CycleIteration = "cycle.iteration";
    public const string ExperienceRecorded = "experience.recorded";
    // опыт по шаблонам (ТЗ v1.33, todo32): правка и удаление записей опыта
    public const string ExperienceUpdated = "experience.updated";
    public const string ExperienceDeleted = "experience.deleted";
    /// <summary>Признак активности записи опыта переключён (T-265-S0).</summary>
    public const string ExperienceActivity = "experience.activity";
    /// <summary>Запись опыта ПЕРЕНЕСЕНА между областями (T-269-S0): общие правила ↔ опыт
    /// проекта ↔ опыт узла шаблона. Отдельный тип, а не <see cref="ExperienceUpdated"/>:
    /// в журнале «запись изменена» не сказало бы главного — что сменился её адресат.</summary>
    public const string ExperienceMoved = "experience.moved";
    public const string ObjectRegistered = "object.registered";
    public const string TemplateApplied = "template.applied";
    // расписание запуска задач (ТЗ п. 2.12, v1.35)
    public const string ScheduleCreated = "schedule.created";
    public const string ScheduleUpdated = "schedule.updated";
    public const string ScheduleDeleted = "schedule.deleted";
    /// <summary>Срабатывание расписания: шаблон скопирован в задачу.</summary>
    public const string ScheduleTriggered = "schedule.triggered";
    // расширение стартового набора: справочники и команды
    public const string ExecutorCreated = "executor.created";
    public const string ExecutorUpdated = "executor.updated";
    public const string TeamCreated = "team.created";
    public const string TeamUpdated = "team.updated";
    // запуск/остановка работы команды (ТЗ v1.14, этап 2.1)
    public const string TeamWorkStarted = "team.work_started";
    public const string TeamWorkStopped = "team.work_stopped";
    /// <summary>Результат подключения участника при запуске работы команды (диагностика, ТЗ v1.14).</summary>
    public const string TeamMemberState = "team.member_state";
    public const string ModelCreated = "model.created";
    public const string ModelUpdated = "model.updated";
    /// <summary>Удаление кастомной записи справочника моделей (ТЗ v1.43, todo36_6): мягкое,
    /// скачанные файлы модели остаются на диске.</summary>
    public const string ModelDeleted = "model.deleted";
    // установка локальной модели в репозиторий моделей (ТЗ v1.40, todo36_3)
    public const string ModelInstallStarted = "model.install_started";
    public const string ModelInstallFinished = "model.install_finished";
    public const string ModelInstallFailed = "model.install_failed";
    // объекты проекта (ТЗ пп. 2.5–2.6; T-259): персонажи, локации, реквизит, стиль,
    // эталонные кадры и адаптеры LoRA
    public const string ObjectCreated = "object.created";
    public const string ObjectUpdated = "object.updated";
    public const string ObjectDeleted = "object.deleted";
    // справочник импортов и запуск импорта (ТЗ v1.28, todo30)
    public const string ImportSourceCreated = "import.source_created";
    public const string ImportSourceUpdated = "import.source_updated";
    public const string ImportSourceDeleted = "import.source_deleted";
    public const string ImportRun = "import.run";
    // аккаунты пользователей и вход в систему (ТЗ п. 2.14, гл. 12, этап 39)
    public const string AccountCreated = "account.created";
    public const string AccountUpdated = "account.updated";
    /// <summary>Пользователь удалён владельцем (T-140): строка помечена deleted_at.</summary>
    public const string AccountDeleted = "account.deleted";
    /// <summary>Смена пароля (своего или сброшенного владельцем); значения в payload нет.</summary>
    public const string AccountPasswordChanged = "account.password_changed";
    public const string AccountLogin = "account.login";
    public const string AccountLogout = "account.logout";
    /// <summary>Неудачная попытка входа (диагностика подбора пароля, п. 6.3).</summary>
    public const string AccountLoginFailed = "account.login_failed";
    // организации и их серверы (ТЗ п. 2.15, этап 40) — события СЕРВЕРНОГО журнала
    public const string OrgCreated = "org.created";
    public const string OrgUpdated = "org.updated";
    /// <summary>Организация удалена С ЭТОГО сервера насовсем (T-148-S0): записи серверной БД,
    /// каталог и база. Не реплицируется — удаление делается на каждом сервере отдельно.</summary>
    public const string OrgDeleted = "org.deleted";
    /// <summary>Аккаунт стал участником организации (заведён исполнитель с этим аккаунтом).</summary>
    public const string OrgMemberAdded = "org.member_added";
    // серверы кластера (ТЗ п. 2.15, гл. 6; этап 41) — события СЕРВЕРНОГО журнала
    public const string ServerCreated = "server.created";
    public const string ServerUpdated = "server.updated";
    /// <summary>Сервер связан с организацией (получил её код S0, S1, …).</summary>
    public const string ServerLinked = "server.linked";
    /// <summary>Сервер отвязан от организации.</summary>
    public const string ServerUnlinked = "server.unlinked";
    /// <summary>Запись сервера удалена из списка целиком (T-141): так убирают неудачное
    /// первое подключение — до первой репликации это безопасно, и код (S1) освобождается.</summary>
    public const string ServerDeleted = "server.deleted";
    /// <summary>Дирижёр организации сменился (дирижёр обязателен и ровно один).</summary>
    public const string ServerConductorChanged = "server.conductor_changed";
    /// <summary>Заявка на подключение сервера к организации подана (двусторонний обмен).</summary>
    public const string ServerJoinRequested = "server.join_requested";
    /// <summary>«ПРИНЯТЬ»: заявка подтверждена, сервер получил код в организации.</summary>
    public const string ServerJoinAccepted = "server.join_accepted";
    /// <summary>«ОТЛОЖИТЬ»: решение не принято, запись остаётся в списке серверов.</summary>
    public const string ServerJoinDeferred = "server.join_deferred";
    /// <summary>«ОТКЛОНИТЬ»: запись убирается из списка, событие в журнале остаётся.</summary>
    public const string ServerJoinRejected = "server.join_rejected";

    // СМЕНА ДИРИЖЁРА ЗАЯВКОЙ (T-21-S1) — события ЖУРНАЛА ОРГАНИЗАЦИИ: заявка живёт в её
    // базе и ездит второй стороне обычной репликацией
    /// <summary>Подана заявка «назначить дирижёром» (подать её вправе только назначаемый
    /// сервер или сегодняшний дирижёр).</summary>
    public const string ConductorRequested = "conductor.requested";
    /// <summary>Заявка принята второй стороной — дирижёр сменился по согласию.</summary>
    public const string ConductorAccepted = "conductor.accepted";
    /// <summary>Заявка отклонена второй стороной.</summary>
    public const string ConductorRejected = "conductor.rejected";
    /// <summary>Заявка снята подавшим.</summary>
    public const string ConductorWithdrawn = "conductor.withdrawn";
    /// <summary>Заявка принята В ОДНОСТОРОННЕМ ПОРЯДКЕ (T-21-S1): дирижёр не отозвался
    /// за 12 часов, и назначаемый сервер принял свою заявку сам. Отдельное событие —
    /// согласия второй стороны здесь не было.</summary>
    public const string ConductorTakenOver = "conductor.taken_over";
    /// <summary>Смена дирижёра объявлена остальным серверам кластера (или получена от
    /// нового дирижёра): у них меняется запись о том, кто теперь дирижёр.</summary>
    public const string ConductorAnnounced = "conductor.announced";

    // ключи API моделей организации (ТЗ гл. 10, этап 45). В payload — только ССЫЛКА
    // на ключ (anthropic.apiKey), само значение в журнал не попадает никогда (п. 6.3)
    public const string ModelKeyCreated = "model_key.created";
    public const string ModelKeyUpdated = "model_key.updated";
    public const string ModelKeyDeleted = "model_key.deleted";
    /// <summary>Конфликт ключа при репликации разрешён человеком (todo45).</summary>
    public const string ModelKeyResolved = "model_key.resolved";

    // УВЕДОМЛЕНИЯ ПОЛЬЗОВАТЕЛЯ (T-272): правила закладки «Настройки → Уведомления»
    // и факт отправки. Адресов и текстов писем в payload нет — только правило и задача
    public const string NotificationRuleCreated = "notification.rule_created";
    public const string NotificationRuleUpdated = "notification.rule_updated";
    public const string NotificationRuleDeleted = "notification.rule_deleted";
    /// <summary>Уведомление отправлено (правило, задача, вид уведомления).</summary>
    public const string NotificationSent = "notification.sent";
    /// <summary>Уведомление отправить не удалось: почтовый сервер не настроен, адрес
    /// не задан, отказ SMTP. В журнал попадает причина — без неё «письма не приходят»
    /// разобрать нечем.</summary>
    public const string NotificationFailed = "notification.failed";

    // АРХИВАЦИЯ (T-40-S0, выпуск 1.105). Создание архива реплицируется вместе со строкой
    // реестра, а вот открыть/закрыть/удалить — событие ЭТОГО сервера: на соседнем тот же
    // архив может лежать иначе
    public const string ArchiveCreated = "archive.created";
    /// <summary>Архив стал текущим (а прежний текущий — просто открытым).</summary>
    public const string ArchiveCurrentChanged = "archive.current_changed";
    /// <summary>Архив распакован на этом сервере — на него можно переключиться.</summary>
    public const string ArchiveOpened = "archive.opened";
    /// <summary>Архив упакован в .zip на этом сервере.</summary>
    public const string ArchiveClosed = "archive.closed";
    /// <summary>Архив удалён С ЭТОГО СЕРВЕРА; запись реестра осталась — он есть у других.</summary>
    public const string ArchiveRemoved = "archive.removed";

    // ПЕРЕНОС ДАННЫХ В АРХИВ И ОБРАТНО (T-42-S0). Событие пишется в журнал РАБОЧЕЙ среды:
    // в архивной оно оказалось бы среди перенесённых логов и рассказывало бы само о себе
    /// <summary>Задача, шаблон, объект, опыт или проект целиком перенесены в текущий архив.</summary>
    public const string ArchiveMoved = "archive.moved";
    /// <summary>То же самое возвращено из текущего архива в рабочую среду.</summary>
    public const string ArchiveRestored = "archive.restored";

    /// <summary>Прогон автоматической архивации (T-46-S0): сколько отобрано правилами
    /// текущего архива и сколько из этого уехало. Пишется на дирижёре — только он её ведёт.</summary>
    public const string ArchiveAutoRun = "archive.auto_run";

    // ПРАВИЛА АРХИВАЦИИ (T-41-S0) — и общие правила организации, и правила архива: запись
    // одна и та же, поэтому и события общие. Едут репликацией вместе с реестром архивов
    public const string ArchiveRuleCreated = "archive.rule_created";
    public const string ArchiveRuleUpdated = "archive.rule_updated";
    public const string ArchiveRuleDeleted = "archive.rule_deleted";
    /// <summary>Правила архива заменены целиком: «вернуть умолчание» на форме правил либо
    /// перенос правил при создании архива (общие / из прежнего текущего / без правил).</summary>
    public const string ArchiveRulesReset = "archive.rules_reset";

    // ПЛАГИНЫ (T-111-S0): шлюз в видеоредактор, конвертор, подключение к серверу MCP.
    // Смена состояния — отдельное событие: «выключили плагин» и «поправили его настройки»
    // разбираются по журналу по-разному, а одно событие на всё заставляло бы сравнивать
    // полезную нагрузку соседних записей
    public const string PluginCreated = "plugin.created";
    public const string PluginUpdated = "plugin.updated";
    public const string PluginStateChanged = "plugin.state_changed";
    public const string PluginDeleted = "plugin.deleted";
}
