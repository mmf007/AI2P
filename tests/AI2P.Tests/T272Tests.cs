using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-272: УВЕДОМЛЕНИЯ ПОЛЬЗОВАТЕЛЯ.
///
/// 1. Закладка «Настройки → Уведомления» — список правил организации, изначально пустой.
/// 2. Форма правила: исполнители и проекты множественным выбором (пусто — «все»), флаг
///    активности, событие (переведена в состояние / требуется проверка / подходит срок),
///    тип уведомления, заголовок и текст письма с макроподстановками.
/// 3. Представления списка: таблица и три вида категорий.
/// 4. Форма исполнителя: «почту брать из логина пользователя» и адрес для оповещения.
/// 5. Сама отправка: по смене состояния и по сроку; на дубли внимания не обращаем — два
///    подходящих правила дают два письма (прямо по заданию).
///
/// Хранилище, отбор правил, раскладка по категориям и сборка письма проверяются здесь
/// по-настоящему; разметка — по файлу (приём T-209/T-217), поведение в браузере —
/// живой проверкой test/t272.
/// </summary>
public sealed class T272Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Поддельный транспорт: письма никуда не уходят, а складываются в список —
    /// ровно то, что нужно проверке (настоящий SMTP тут не нужен и недоступен).</summary>
    private sealed class FakeTransport : INotificationTransport
    {
        public List<NotificationMessage> Sent { get; } = [];
        public bool Ready { get; set; } = true;
        public string NotReadyReason => Ready ? "" : "почта не настроена";
        public string Transport => NotificationChannels.EmailTransport;

        public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
        {
            if (!Ready)
            {
                throw new InvalidOperationException(NotReadyReason);
            }
            // пустой адрес отвергает и настоящий транспорт (EmailTransport): без этого
            // проверка «исполнителю без почты не пишем» проходила бы, ничего не проверив
            if (message.Address.Trim().Length == 0)
            {
                throw new InvalidOperationException("адрес не задан");
            }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private FakeTransport _mail = new();

    private NotificationService Notifications() =>
        new(_f.Tasks, _f.Executors, _f.Projects, _f.Statuses, _f.NotificationRules, _f.Events,
            () => new Organization { Name = "ММФ", Code = "mmfgrp" },
            () => "http://localhost:5480/ai2p/mmfgrp", _mail);

    private Executor Human(string nick, string email = "") =>
        _f.Executors.Create(new Executor
        {
            Nick = nick,
            InternalName = nick + " Иванов",
            Kind = ExecutorKind.Human,
            EmailFromLogin = email.Length == 0,
            NotifyEmail = email,
        }, null);

    private NotificationRule Rule(string eventKind, string status = "", int lead = 0,
        IEnumerable<string>? executors = null, IEnumerable<string>? projects = null,
        string channel = NotificationChannels.Mail, string subject = "AI2P: ${task.id}",
        string body = "${task.title}") =>
        _f.NotificationRules.Create(new NotificationRule
        {
            EventKind = eventKind,
            StatusCode = status,
            LeadMinutes = lead,
            Channel = channel,
            Subject = subject,
            Body = body,
            ExecutorIds = executors?.ToList() ?? [],
            ProjectIds = projects?.ToList() ?? [],
        }, null);

    /// <summary>
    /// Сменить статус и посмотреть, какие письма из этого следуют. Статус меняется ДО того,
    /// как заведён сервис: он подписан на смену статуса и отправил бы письмо сам, фоном, —
    /// и тогда проверка считала бы одно и то же письмо дважды. Подписка проверяется отдельно
    /// (<see cref="The_Service_Listens_To_The_Status_Change"/>).
    /// </summary>
    private (NotificationService Service, List<NotificationMessage> Messages) AfterStatus(
        TaskItem task, string status)
    {
        var changed = _f.Tasks.ChangeStatus(task.Id, status, null);
        var service = Notifications();
        return (service, service.StatusMessages(changed));
    }

    private TaskItem NewTask(string title, string? projectId = null, string? executorId = null,
        string? responsibleId = null, DateTime? due = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ProjectId = projectId,
            ResponsibleId = responsibleId,
            DueDate = due,
            ExecutorIds = executorId is null ? [] : [executorId],
        }, title, "", null);

    // ---------- 1. макроподстановки (ядро) ----------

    /// <summary>Все коды задания раскрываются значениями письма.</summary>
    [Fact]
    public void Macros_Expand_Every_Code()
    {
        var context = new NotificationContext
        {
            TaskId = "T-15",
            TaskTitle = "Выпустить версию",
            TaskLink = "http://localhost:5480/ai2p/mmfgrp/task/abc",
            TaskStatus = "проверка",
            TaskDue = "24.08.2026 10:00",
            PerformerNick = "mrclaude",
            PerformerName = "Иван Петров",
            CompanyName = "ММФ",
            CompanyId = "mmfgrp",
            ProjectName = "AI2P",
            ProjectId = "PRJ-1",
        };
        var template = string.Join(" | ", NotificationMacros.Catalog.Select(m => m.Marker));
        var expanded = NotificationMacros.Expand(template, context);

        Assert.DoesNotContain("${", expanded, StringComparison.Ordinal);
        Assert.Contains("T-15", expanded, StringComparison.Ordinal);
        Assert.Contains("mrclaude", expanded, StringComparison.Ordinal);
        Assert.Contains("Иван Петров", expanded, StringComparison.Ordinal);
        Assert.Contains("mmfgrp", expanded, StringComparison.Ordinal);
        Assert.Contains("PRJ-1", expanded, StringComparison.Ordinal);
    }

    /// <summary>Написание проекта из задания (progect) принимается как синоним: уже
    /// написанный по заданию текст не должен оказаться сломанным.</summary>
    [Fact]
    public void Macros_Accept_The_Spelling_From_The_Task()
    {
        var context = new NotificationContext { ProjectName = "AI2P", ProjectId = "PRJ-1" };
        Assert.Equal("AI2P PRJ-1",
            NotificationMacros.Expand("${progect.name} ${progect.id}", context));
        Assert.Equal("mrclaude",
            NotificationMacros.Expand("${performer.nick}",
                new NotificationContext { PerformerNick = "mrclaude" }));
    }

    /// <summary>Неизвестный макрос остаётся в тексте как есть — видно опечатку, а не
    /// молча пропавший кусок письма.</summary>
    [Fact]
    public void An_Unknown_Macro_Stays_As_Written()
    {
        Assert.Equal("а ${task.nosuch} б",
            NotificationMacros.Expand("а ${task.nosuch} б", new NotificationContext()));
    }

    /// <summary>Справочник для кнопки редактора содержит ровно коды задания (плюс два
    /// добавленных: состояние и срок).</summary>
    [Fact]
    public void The_Macro_Catalog_Covers_The_Task_List()
    {
        var codes = NotificationMacros.Catalog.Select(m => m.Code).ToList();
        foreach (var required in new[]
                 {
                     "task.id", "task.title", "task.link", "performer.nik", "performer.name",
                     "company.name", "company.id", "project.name", "project.id",
                 })
        {
            Assert.Contains(required, codes);
        }
        Assert.Contains("task.status", codes);
        Assert.Contains("task.due", codes);
        Assert.All(NotificationMacros.Catalog, m => Assert.Equal("${" + m.Code + "}", m.Marker));
    }

    // ---------- 2. хранилище правил ----------

    /// <summary>Списки исполнителей и проектов сохраняются и читаются обратно; номер — NTF-N.</summary>
    [Fact]
    public void A_Rule_Keeps_Its_Executors_And_Projects()
    {
        var one = Human("один");
        var two = Human("два");
        var project = _f.Projects.Create("Проект", null, null, null);
        var rule = Rule(NotificationEvents.Status, TaskStatuses.Review,
            executors: [one.Id, two.Id], projects: [project.Id]);

        Assert.StartsWith("NTF-", rule.DisplayId, StringComparison.Ordinal);
        var read = _f.NotificationRules.Get(rule.Id)!;
        Assert.Equal(2, read.ExecutorIds.Count);
        Assert.Equal([project.Id], read.ProjectIds);
        Assert.True(read.IsActive);

        // правка переписывает списки целиком
        read.ExecutorIds = [two.Id];
        read.ProjectIds = [];
        _f.NotificationRules.Update(read, null);
        var again = _f.NotificationRules.Get(rule.Id)!;
        Assert.Equal([two.Id], again.ExecutorIds);
        Assert.Empty(again.ProjectIds);
        Assert.Equal(rule.DisplayId, again.DisplayId);
    }

    /// <summary>Пустой список проектов означает ВСЕ проекты, включая задачи без проекта;
    /// заданный список — только свои.</summary>
    [Fact]
    public void An_Empty_Project_List_Means_All_Projects()
    {
        var mine = _f.Projects.Create("Мой", null, null, null);
        var other = _f.Projects.Create("Чужой", null, null, null);
        var all = Rule(NotificationEvents.Review);
        var only = Rule(NotificationEvents.Review, projects: [mine.Id]);

        Assert.Contains(_f.NotificationRules.Match(NotificationEvents.Review, mine.Id),
            r => r.Id == all.Id);
        Assert.Contains(_f.NotificationRules.Match(NotificationEvents.Review, mine.Id),
            r => r.Id == only.Id);
        Assert.DoesNotContain(_f.NotificationRules.Match(NotificationEvents.Review, other.Id),
            r => r.Id == only.Id);
        Assert.Contains(_f.NotificationRules.Match(NotificationEvents.Review, projectId: null),
            r => r.Id == all.Id);
        Assert.DoesNotContain(_f.NotificationRules.Match(NotificationEvents.Review, projectId: null),
            r => r.Id == only.Id);
    }

    /// <summary>Пустой список исполнителей означает всех, кого касается событие.</summary>
    [Fact]
    public void An_Empty_Executor_List_Means_Everyone()
    {
        var one = Human("один");
        var two = Human("два");
        var all = Rule(NotificationEvents.Review);
        var only = Rule(NotificationEvents.Review, executors: [one.Id]);

        Assert.True(NotificationRuleService.Covers(all, one.Id));
        Assert.True(NotificationRuleService.Covers(all, two.Id));
        Assert.True(NotificationRuleService.Covers(only, one.Id));
        Assert.False(NotificationRuleService.Covers(only, two.Id));
    }

    /// <summary>Неактивное правило не попадает в отбор, но остаётся в списке.</summary>
    [Fact]
    public void An_Inactive_Rule_Stays_In_The_List_But_Does_Not_Fire()
    {
        var rule = Rule(NotificationEvents.Review);
        rule.IsActive = false;
        _f.NotificationRules.Update(rule, null);

        Assert.Contains(_f.NotificationRules.List(), r => r.Id == rule.Id);
        Assert.Empty(_f.NotificationRules.Match(NotificationEvents.Review, null));
    }

    /// <summary>Проверки формы: событие и вид из известных, заголовок обязателен,
    /// у «переведена в состояние» обязательно состояние.</summary>
    [Fact]
    public void A_Rule_Is_Validated()
    {
        Assert.Throws<ArgumentException>(() => Rule("никакое"));
        Assert.Throws<ArgumentException>(() =>
            Rule(NotificationEvents.Review, channel: "голубь"));
        Assert.Throws<ArgumentException>(() => Rule(NotificationEvents.Review, subject: "  "));
        Assert.Throws<ArgumentException>(() => Rule(NotificationEvents.Status, status: ""));

        // уточнения чужого события не сохраняются: состояние — только у status,
        // время до срока — только у due
        var review = Rule(NotificationEvents.Review, status: TaskStatuses.Done, lead: 30);
        Assert.Equal("", review.StatusCode);
        Assert.Equal(0, review.LeadMinutes);
    }

    /// <summary>Удалённое правило исчезает из списка и из отбора.</summary>
    [Fact]
    public void A_Deleted_Rule_Disappears()
    {
        var rule = Rule(NotificationEvents.Review);
        _f.NotificationRules.Delete(rule.Id, null);
        Assert.Null(_f.NotificationRules.Get(rule.Id));
        Assert.Empty(_f.NotificationRules.List());
    }

    /// <summary>Отметка об отправке по сроку: повтор гасится, сдвинутый срок — новая метка.</summary>
    [Fact]
    public void The_Due_Mark_Guards_Only_The_Same_Due_Date()
    {
        var rule = Rule(NotificationEvents.Due, lead: 60);
        var task = NewTask("задача");
        Assert.False(_f.NotificationRules.AlreadySent(rule.Id, task.Id, "2026-08-24"));
        _f.NotificationRules.MarkSent(rule.Id, task.Id, "2026-08-24");
        Assert.True(_f.NotificationRules.AlreadySent(rule.Id, task.Id, "2026-08-24"));
        Assert.False(_f.NotificationRules.AlreadySent(rule.Id, task.Id, "2026-08-25"));
    }

    // ---------- 3. схема и репликация ----------

    /// <summary>Схема заводит все четыре таблицы уведомлений.</summary>
    [Fact]
    public void The_Schema_Has_The_Notification_Tables()
    {
        using var conn = _f.Db.Open();
        var tables = Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='table'", r => r.S("name"));
        foreach (var table in new[]
                 {
                     "notification_rules", "notification_executors",
                     "notification_projects", "notification_marks",
                 })
        {
            Assert.Contains(table, tables);
        }
        Assert.Equal("48", _f.Db.Meta("schema_version"));
    }

    /// <summary>
    /// Правило и его связи ЕДУТ партнёру, а отметка об отправке — НЕТ: письмо шлёт владелец
    /// задачи, и отметка это его собственная память (ср. run_requests_applied, T-196).
    /// </summary>
    [Fact]
    public void Rules_Replicate_And_Marks_Do_Not()
    {
        Assert.Contains("notification_rules", ChangeLog.OrgTables);
        Assert.Contains("notification_executors", ChangeLog.OrgTables);
        Assert.Contains("notification_projects", ChangeLog.OrgTables);
        Assert.DoesNotContain("notification_marks", ChangeLog.OrgTables);

        // правило действительно попадает в журнал изменений (триггером, а не кодом сервиса)
        var rule = Rule(NotificationEvents.Review);
        using var conn = _f.Db.Open();
        var logged = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM changes WHERE tbl='notification_rules' AND pk=@id",
            ("@id", rule.Id));
        Assert.True(logged > 0);
    }

    // ---------- 4. почта исполнителя ----------

    /// <summary>Умолчание — «брать из логина»: у всех уже заведённых людей адрес это их логин.</summary>
    [Fact]
    public void An_Executor_Takes_The_Mail_From_The_Login_By_Default()
    {
        var executor = Human("нико");
        Assert.True(_f.Executors.Get(executor.Id)!.EmailFromLogin);
    }

    /// <summary>Галочка поднята — адрес аккаунта входа; снята — адрес из формы; пусто —
    /// писать некуда.</summary>
    [Fact]
    public void The_Notification_Address_Follows_The_Checkbox()
    {
        var account = _f.Accounts.Create(new AccountSaveInput { Name = "Иван", Email = "ivan@example.com", Password = "pass" }, null);
        var executor = _f.AddMember(account, SystemRole.Editor);
        Assert.Equal("ivan@example.com", _f.Executors.NotifyAddressOf(_f.Executors.Get(executor.Id)!));

        executor = _f.Executors.Get(executor.Id)!;
        executor.EmailFromLogin = false;
        executor.NotifyEmail = "otdel@example.com";
        _f.Executors.Update(executor, null);
        Assert.Equal("otdel@example.com", _f.Executors.NotifyAddressOf(_f.Executors.Get(executor.Id)!));

        executor = _f.Executors.Get(executor.Id)!;
        executor.NotifyEmail = "";
        _f.Executors.Update(executor, null);
        Assert.Equal("", _f.Executors.NotifyAddressOf(_f.Executors.Get(executor.Id)!));
    }

    // ---------- 5. отправка по смене состояния ----------

    /// <summary>Задача переведена в выбранное состояние — письмо ИСПОЛНИТЕЛЮ, макросы раскрыты.</summary>
    [Fact]
    public async System.Threading.Tasks.Task A_State_Rule_Writes_To_The_Performer()
    {
        var performer = Human("испол", "ispol@example.com");
        var project = _f.Projects.Create("Проект", null, null, null);
        Rule(NotificationEvents.Status, TaskStatuses.InProgress,
            subject: "AI2P ${task.id}: ${task.title}",
            body: "Проект ${project.name}, вам: ${performer.nik}. ${task.link}");
        var task = NewTask("Сделать кадр", project.Id, performer.Id);

        var (service, messages) = AfterStatus(task, TaskStatuses.InProgress);
        Assert.Single(messages);
        Assert.Equal("ispol@example.com", messages[0].Address);
        Assert.Equal($"AI2P {task.DisplayId}: Сделать кадр", messages[0].Subject);
        Assert.Contains("Проект Проект", messages[0].Body, StringComparison.Ordinal);
        Assert.Contains("вам: испол", messages[0].Body, StringComparison.Ordinal);
        Assert.Contains("/task/" + task.Id, messages[0].Body, StringComparison.Ordinal);

        Assert.Equal(1, await service.SendAsync(messages, null));
        Assert.Single(_mail.Sent);
    }

    /// <summary>Другое состояние правило не трогает.</summary>
    [Fact]
    public void A_State_Rule_Ignores_Other_States()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Status, TaskStatuses.Done);
        var task = NewTask("Задача", null, performer.Id);
        Assert.Empty(AfterStatus(task, TaskStatuses.InProgress).Messages);
    }

    /// <summary>«Требуется проверка» пишет ОТВЕТСТВЕННОМУ, а не исполнителю.</summary>
    [Fact]
    public void A_Review_Rule_Writes_To_The_Responsible()
    {
        var performer = Human("испол", "ispol@example.com");
        var chief = Human("шеф", "chief@example.com");
        Rule(NotificationEvents.Review);
        var task = NewTask("Задача", null, performer.Id, responsibleId: chief.Id);

        var messages = AfterStatus(task, TaskStatuses.Review).Messages;
        Assert.Single(messages);
        Assert.Equal("chief@example.com", messages[0].Address);
        Assert.Equal(chief.Id, messages[0].ExecutorId);
    }

    /// <summary>Ответственного нет — писать некому, и это не ошибка.</summary>
    [Fact]
    public void A_Review_Rule_Is_Silent_Without_A_Responsible()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Review);
        var task = NewTask("Задача", null, performer.Id);
        Assert.Empty(AfterStatus(task, TaskStatuses.Review).Messages);
    }

    /// <summary>Правило, названное на другого исполнителя, этой задачи не касается.</summary>
    [Fact]
    public void A_Rule_Named_On_Another_Performer_Does_Not_Fire()
    {
        var performer = Human("испол", "ispol@example.com");
        var stranger = Human("чужой", "stranger@example.com");
        Rule(NotificationEvents.Status, TaskStatuses.InProgress, executors: [stranger.Id]);
        var task = NewTask("Задача", null, performer.Id);
        Assert.Empty(AfterStatus(task, TaskStatuses.InProgress).Messages);
    }

    /// <summary>НА ДУБЛИ ВНИМАНИЯ НЕ ОБРАЩАЕМ (прямо по заданию): два подходящих правила —
    /// два письма, а не одно.</summary>
    [Fact]
    public void Two_Matching_Rules_Give_Two_Letters()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Status, TaskStatuses.InProgress);
        Rule(NotificationEvents.Status, TaskStatuses.InProgress);
        var task = NewTask("Задача", null, performer.Id);
        Assert.Equal(2, AfterStatus(task, TaskStatuses.InProgress).Messages.Count);
    }

    /// <summary>Подписка на смену статуса живая: письмо уходит без ручного вызова.</summary>
    [Fact]
    public async System.Threading.Tasks.Task The_Service_Listens_To_The_Status_Change()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Status, TaskStatuses.InProgress);
        var task = NewTask("Задача", null, performer.Id);
        var service = Notifications();
        Assert.NotNull(service);

        _f.Tasks.ChangeStatus(task.Id, TaskStatuses.InProgress, null);
        // отправка идёт фоном: смена статуса не должна ждать почтового сервера
        for (var i = 0; i < 100 && _mail.Sent.Count == 0; i++)
        {
            await System.Threading.Tasks.Task.Delay(50);
        }
        Assert.Single(_mail.Sent);
    }

    // ---------- 6. отправка по сроку ----------

    /// <summary>До подхода срока писем нет; после — есть, и ровно один раз.</summary>
    [Fact]
    public async System.Threading.Tasks.Task A_Due_Rule_Fires_Once_Per_Due_Date()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Due, lead: 60, subject: "Срок ${task.id}", body: "${task.due}");
        var due = DateTime.UtcNow.AddMinutes(90);
        var task = NewTask("Задача", null, performer.Id, due: due);

        var service = Notifications();
        Assert.Empty(service.DueMessages(DateTime.UtcNow));          // до срока ещё 90 минут
        Assert.Single(service.DueMessages(DateTime.UtcNow.AddMinutes(45)));

        Assert.Equal(1, await service.CheckDueAsync(DateTime.UtcNow.AddMinutes(45)));
        Assert.Equal(0, await service.CheckDueAsync(DateTime.UtcNow.AddMinutes(50)));
        Assert.Single(_mail.Sent);

        // срок сдвинули — уведомить надо заново
        var moved = _f.Tasks.Get(task.Id)!;
        moved.DueDate = due.AddDays(1);
        _f.Tasks.Update(moved, "Задача", "", null);
        Assert.Equal(1, await service.CheckDueAsync(due.AddDays(1).AddMinutes(-30)));
        Assert.Equal(2, _mail.Sent.Count);
    }

    /// <summary>Задача без срока и закрытая задача сроком не тревожатся.</summary>
    [Fact]
    public void A_Due_Rule_Skips_Tasks_Without_A_Due_Date_And_Closed_Ones()
    {
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Due, lead: 60);
        NewTask("Без срока", null, performer.Id);
        var closed = NewTask("Закрытая", null, performer.Id, due: DateTime.UtcNow.AddMinutes(10));
        _f.Tasks.ChangeStatus(closed.Id, TaskStatuses.Done, null);

        Assert.Empty(Notifications().DueMessages(DateTime.UtcNow));
    }

    // ---------- 7. транспорт ----------

    /// <summary>Вид «почта задача» несёт вложение-календарь: почтовый клиент делает по нему
    /// задачу со сроком и ссылкой.</summary>
    [Fact]
    public void The_Mail_Task_Channel_Carries_A_Vtodo()
    {
        var ics = EmailTransport.BuildTodo(new NotificationMessage
        {
            TaskId = "task-1",
            Subject = "Срок; подходит",
            Body = "строка\nвторая",
            TaskLink = "http://localhost:5480/ai2p/mmfgrp/task/task-1",
            Due = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc),
        });
        Assert.Contains("BEGIN:VTODO", ics, StringComparison.Ordinal);
        Assert.Contains("UID:task-1@ai2p", ics, StringComparison.Ordinal);
        Assert.Contains("DUE:20260824T100000Z", ics, StringComparison.Ordinal);
        // экранирование RFC 5545: точка с запятой и перевод строки
        Assert.Contains("SUMMARY:Срок\\; подходит", ics, StringComparison.Ordinal);
        Assert.Contains("DESCRIPTION:строка\\nвторая", ics, StringComparison.Ordinal);
        Assert.Contains("END:VCALENDAR", ics, StringComparison.Ordinal);
    }

    /// <summary>Вид уведомления знает свой транспорт — на этом держится добавление
    /// мессенджеров: код отправки про виды не знает вовсе.</summary>
    [Fact]
    public void A_Channel_Knows_Its_Transport()
    {
        Assert.Equal(NotificationChannels.EmailTransport,
            NotificationChannels.TransportOf(NotificationChannels.Mail));
        Assert.Equal(NotificationChannels.EmailTransport,
            NotificationChannels.TransportOf(NotificationChannels.MailTask));
        Assert.True(NotificationChannels.IsKnown(NotificationChannels.MailTask));
        Assert.False(NotificationChannels.IsKnown("telegram"));
        Assert.NotNull(Notifications().TransportFor(NotificationChannels.Mail));
    }

    /// <summary>
    /// Почта не настроена — письмо не уходит, но работа не ломается: причина ложится
    /// в журнал работ. Уведомление не должно ронять то, о чём оно уведомляет.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task A_Broken_Transport_Does_Not_Break_The_Work()
    {
        _mail = new FakeTransport { Ready = false };
        var performer = Human("испол", "ispol@example.com");
        Rule(NotificationEvents.Status, TaskStatuses.InProgress);
        var task = NewTask("Задача", null, performer.Id);

        var (service, messages) = AfterStatus(task, TaskStatuses.InProgress);
        Assert.Equal(0, await service.SendAsync(messages, null));

        var failed = _f.Events.Query(taskId: task.Id)
            .Where(e => e.EventType == EventTypes.NotificationFailed)
            .ToList();
        Assert.Single(failed);
    }

    /// <summary>У исполнителя не задан адрес — письмо не уходит, причина в журнале.</summary>
    [Fact]
    public async System.Threading.Tasks.Task A_Performer_Without_An_Address_Is_Skipped()
    {
        // человек без аккаунта входа и с поднятой галочкой «почту брать из логина»
        var performer = Human("безпочты");
        Rule(NotificationEvents.Status, TaskStatuses.InProgress);
        var task = NewTask("Задача", null, performer.Id);

        var (service, messages) = AfterStatus(task, TaskStatuses.InProgress);
        Assert.Single(messages);
        Assert.Equal("", messages[0].Address);
        Assert.Equal(0, await service.SendAsync(messages, null));
        Assert.Empty(_mail.Sent);
    }

    // ---------- 8. представления списка ----------

    /// <summary>Категории «проект / событие»: правило без проектов уходит в «все проекты»,
    /// и эта категория стоит первой.</summary>
    [Fact]
    public void The_Project_Event_View_Groups_By_Project_Then_Event()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var all = Rule(NotificationEvents.Review);
        var mine = Rule(NotificationEvents.Status, TaskStatuses.Done, projects: [project.Id]);

        var groups = NotificationGroups.Build([all, mine], NotificationViews.ProjectEvent,
            key => key.Length == 0 ? "все проекты" : "Проект", _ => "", code => code);

        Assert.Equal(2, groups.Count);
        Assert.Equal(NotificationGroups.Any, groups[0].Key);       // «все» — первой
        Assert.Equal(project.Id, groups[1].Key);
        Assert.Equal(NotificationEvents.Review, groups[0].Items[0].Key);
        Assert.Equal(NotificationEvents.Status, groups[1].Items[0].Key);
        Assert.Equal(1, groups[0].Count);
    }

    /// <summary>Правило с двумя исполнителями попадает в ОБЕ категории: это перечисление,
    /// а не взаимоисключающие корзины.</summary>
    [Fact]
    public void A_Rule_With_Two_Performers_Lands_In_Both_Categories()
    {
        var one = Human("один");
        var two = Human("два");
        var project = _f.Projects.Create("Проект", null, null, null);
        var rule = Rule(NotificationEvents.Review, executors: [one.Id, two.Id],
            projects: [project.Id]);

        var groups = NotificationGroups.Build([rule], NotificationViews.ExecutorProject,
            _ => "Проект", key => key, _ => "");
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Items));
        Assert.All(groups, g => Assert.Single(g.Items[0].Rules));
    }

    /// <summary>Представление «проект / исполнитель» — тот же разбор, но наоборот.</summary>
    [Fact]
    public void The_Project_Executor_View_Groups_By_Project_Then_Performer()
    {
        var one = Human("один");
        var project = _f.Projects.Create("Проект", null, null, null);
        var rule = Rule(NotificationEvents.Review, executors: [one.Id], projects: [project.Id]);

        var groups = NotificationGroups.Build([rule], NotificationViews.ProjectExecutor,
            key => key, key => key, _ => "");
        Assert.Single(groups);
        Assert.Equal(project.Id, groups[0].Key);
        Assert.Equal(one.Id, groups[0].Items[0].Key);
    }

    /// <summary>Мусор в сохранённом состоянии не ломает список — это таблица.</summary>
    [Fact]
    public void An_Unknown_View_Falls_Back_To_The_Table()
    {
        Assert.Equal(NotificationViews.Table, NotificationViews.Normalize("что-то"));
        Assert.Equal(NotificationViews.Table, NotificationViews.Normalize(null));
        Assert.Equal(NotificationViews.ExecutorProject,
            NotificationViews.Normalize(NotificationViews.ExecutorProject));
    }

    // ---------- 9. разметка ----------

    /// <summary>
    /// Закладка «Уведомления» стоит в настройках, а поля почты исполнителя — в его форме.
    /// Проверка по файлу (приём T-209/T-217): разметку тесты не рисуют, но пропажу целой
    /// закладки поймать обязаны.
    /// </summary>
    [Fact]
    public void The_Markup_Has_The_Tab_And_The_Executor_Fields()
    {
        var ui = Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components");
        var settings = File.ReadAllText(Path.Combine(ui, "SettingsView.razor"));
        Assert.Contains("settings.tab.notifications", settings, StringComparison.Ordinal);
        Assert.Contains("<NotificationsView />", settings, StringComparison.Ordinal);
        Assert.Contains("data-mail-host", settings, StringComparison.Ordinal);

        var executor = File.ReadAllText(Path.Combine(ui, "ExecutorDialog.razor"));
        Assert.Contains("executors.emailFromLogin", executor, StringComparison.Ordinal);
        Assert.Contains("data-executor-notifyemail", executor, StringComparison.Ordinal);

        var dialog = File.ReadAllText(Path.Combine(ui, "NotificationDialog.razor"));
        // кнопка справочника макросов — у ОБОИХ текстовых полей (по заданию)
        Assert.Contains("data-notify-macro-subject", dialog, StringComparison.Ordinal);
        Assert.Contains("data-notify-macro-body", dialog, StringComparison.Ordinal);
    }

    /// <summary>Тексты закладки есть в словарях ОБОИХ языков (ТЗ гл. 9, T-180).</summary>
    [Fact]
    public void The_Dictionaries_Carry_The_New_Keys()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        var catalog = new LocCatalog(dir);
        foreach (var key in new[]
                 {
                     "settings.tab.notifications", "notify.title", "notify.event.due",
                     "notify.channel.mail_task", "notify.view.executor-project",
                     "mail.host", "executors.emailFromLogin", "msg.notify.10",
                 })
        {
            Assert.True(catalog.Has("ru", key), "ru: " + key);
            Assert.True(catalog.Has("en", key), "en: " + key);
        }
        // пояснения макросов — по ключу из каталога, иначе кнопка покажет сам ключ
        foreach (var macro in NotificationMacros.Catalog)
        {
            Assert.True(catalog.Has("ru", macro.TextKey), "ru: " + macro.TextKey);
            Assert.True(catalog.Has("en", macro.TextKey), "en: " + macro.TextKey);
        }
    }

    /// <summary>Корень репозитория приложения — от каталога сборки вверх до AI2P.sln.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
