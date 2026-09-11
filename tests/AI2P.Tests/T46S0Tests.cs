using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-46-S0: АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ — действие для расписания на дирижёре (выпуск 1.105).
///
/// 1. Отбор идёт по правилам ТЕКУЩЕГО архива, и отобранное переносится: корневая задача
///    проверяется по своему состоянию и возрасту, потомки — по списку состояний правила,
///    и один неподходящий потомок отменяет архивацию всей иерархии.
/// 2. Работает ТОЛЬКО на дирижёре: на прочих серверах — внятный отказ сводкой, а не
///    исключение и не молчание.
/// 3. Виды данных, которые движок поодиночке не переносит (правила безопасности, логи),
///    попадают в «пропущено» с причиной, а не теряются молча.
/// 4. Расписание умеет запускать ДЕЙСТВИЕ вместо копии шаблона: шаблон тогда не указывается
///    вовсе, а неизвестный код действия не сохраняется.
/// 5. Сводка есть всегда — и у прогона, и у отказа: её читают расписание (в журнал) и форма
///    добавления архива (человеку в окно).
/// </summary>
public sealed class T46S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveService _archives = null!;
    private ArchiveRuleService _rules = null!;

    /// <summary>Автоархивация при ТЕКУЩЕМ открытом архиве; scope — кто мы в кластере.</summary>
    private AutoArchiveService Service(ServerScope? scope = null)
    {
        _rules = new ArchiveRuleService(_f.Db, _f.Events);
        _archives = new ArchiveService(_f.Db, _f.Events) { Rules = _rules };
        if (_archives.Current() is null)
        {
            _archives.Create("arc46", "Архив 2026", ArchiveRuleModes.Empty, null);
        }
        return new AutoArchiveService(_archives, _rules, new ArchiveCandidateService(_f.Db),
            new ArchiveTransferService(_f.Db, _archives, _f.Events), _f.Events, scope);
    }

    /// <summary>Правило архива: задачи в состоянии status старше days дней; children —
    /// допустимые состояния потомков (пусто — любые).</summary>
    private ArchiveRule TaskRule(string status, int days, params string[] children) =>
        _rules.Create(new ArchiveRule
        {
            ArchiveId = _archives.Current()!.Id,
            Target = ArchiveRuleTargets.Tasks,
            TaskStatus = status,
            ChildStatuses = [.. children],
            DateField = ArchiveRuleDates.Updated,
            AgeKind = ArchiveRuleAges.Days,
            AgeDays = days,
        }, null);

    private TaskItem Task(string title, string status, string? parentId = null) =>
        _f.Tasks.Create(new TaskItem { Title = title, Status = status, ParentId = parentId },
            "", "", null);

    /// <summary>Состарить задачу: даты в базе правит только хранилище, а нам нужен возраст.</summary>
    private void Age(string taskId, int days)
    {
        using var conn = _f.Db.Open();
        var when = Sql.ToDb(DateTime.UtcNow.AddDays(-days));
        Sql.Exec(conn, null, "UPDATE tasks SET created_at=@w, updated_at=@w WHERE id=@id",
            ("@w", when), ("@id", taskId));
    }

    // --- 1. отбор и перенос ---

    [Fact]
    public void Auto_Archiving_Moves_What_The_Rules_Of_The_Current_Archive_Select()
    {
        var service = Service();
        TaskRule(TaskStatuses.Done, 30, TaskStatuses.Done, TaskStatuses.Cancelled);

        // подходит целиком: корень старый и «готово», потомок в списке состояний потомков
        var ready = Task("Старая готовая", TaskStatuses.Done);
        var readyChild = Task("Её потомок", TaskStatuses.Cancelled, ready.Id);
        Age(ready.Id, 40);
        Age(readyChild.Id, 40);
        // НЕ подходит: один потомок в работе — иерархия остаётся целиком
        var busy = Task("Старая с работающим потомком", TaskStatuses.Done);
        var busyChild = Task("Потомок в работе", TaskStatuses.InProgress, busy.Id);
        Age(busy.Id, 40);
        Age(busyChild.Id, 40);
        // НЕ подходит по возрасту
        var fresh = Task("Свежая готовая", TaskStatuses.Done);

        var result = service.Run(null);

        Assert.True(result.Ran);
        Assert.Equal("arc46", result.ArchiveCode);
        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Failed);
        Assert.True(result.Rows > 0);
        Assert.NotEmpty(result.Summary);
        // уехала только подходящая ветка — целиком
        Assert.Null(_f.Tasks.Get(ready.Id));
        Assert.Null(_f.Tasks.Get(readyChild.Id));
        Assert.NotNull(_f.Tasks.Get(busy.Id));
        Assert.NotNull(_f.Tasks.Get(busyChild.Id));
        Assert.NotNull(_f.Tasks.Get(fresh.Id));
    }

    [Fact]
    public void Nothing_To_Move_Is_A_Normal_Run_With_An_Empty_Summary_Of_Work()
    {
        var service = Service();
        TaskRule(TaskStatuses.Done, 30);
        Task("Свежая готовая", TaskStatuses.Done);

        var result = service.Run(null);

        Assert.True(result.Ran);
        Assert.Equal(0, result.Candidates);
        Assert.Equal(0, result.Moved);
        Assert.Empty(result.Items);
        Assert.NotEmpty(result.Summary);
    }

    // --- 2. только на дирижёре ---

    [Fact]
    public void On_A_Non_Conductor_Server_It_Refuses_With_A_Readable_Reason()
    {
        // архив и правило заводятся под дирижёром (иначе не завести сам архив)
        var conductor = Service();
        TaskRule(TaskStatuses.Done, 30);
        var old = Task("Старая готовая", TaskStatuses.Done);
        Age(old.Id, 40);

        var elsewhere = Service(new ServerScope(() => "s1", () => "S1", () => false));
        var result = elsewhere.Run(null);

        Assert.False(result.Ran);
        Assert.NotEmpty(result.Reason);
        Assert.Equal(result.Reason, result.Summary);
        Assert.Empty(result.Items);
        // и ничего не тронуто — задача осталась в рабочей среде
        Assert.NotNull(_f.Tasks.Get(old.Id));

        // а на дирижёре то же самое проходит
        Assert.True(conductor.Run(null).Ran);
        Assert.Null(_f.Tasks.Get(old.Id));
    }

    // --- 3. виды данных, которые поодиночке не переносятся ---

    [Fact]
    public void Rule_Kinds_Map_To_Move_Kinds_And_Two_Of_Them_Are_Not_Moved_One_By_One()
    {
        Assert.Equal(ArchiveMoveTargets.Task,
            AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Tasks));
        Assert.Equal(ArchiveMoveTargets.Template,
            AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Templates));
        Assert.Equal(ArchiveMoveTargets.Object,
            AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Objects));
        Assert.Equal(ArchiveMoveTargets.Experience,
            AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Experience));
        // правила безопасности и логи движок поодиночке не переносит — «пропущено» с причиной
        Assert.Equal("", AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Security));
        Assert.Equal("", AutoArchiveService.MoveTargetOf(ArchiveRuleTargets.Logs));
    }

    // --- 4. действие в расписании ---

    [Fact]
    public void A_Schedule_Runs_An_Action_Instead_Of_A_Template()
    {
        var schedules = new ScheduleService(_f.Db, _f.Events);
        var saved = schedules.Create(new Schedule
        {
            Action = ScheduleActions.AutoArchive,
            Kind = "periodic",
            PeriodJson = """{"type":"weekly","days":[1],"time":"03:00"}""",
        }, null);

        Assert.Equal(ScheduleActions.AutoArchive, saved.Action);
        Assert.Equal("", saved.TemplateTaskId);
        Assert.True(ScheduleService.IsAction(saved));

        var read = schedules.Get(saved.Id)!;
        Assert.Equal(ScheduleActions.AutoArchive, read.Action);
        Assert.Equal("", read.TemplateTaskId);

        // расписание задач как работало, так и работает: шаблон обязателен
        Assert.Throws<ArgumentException>(() => schedules.Create(new Schedule
        {
            Kind = "periodic",
            PeriodJson = """{"type":"weekly","days":[1],"time":"03:00"}""",
        }, null));
        // и выдуманное действие не сохраняется
        Assert.Throws<ArgumentException>(() => schedules.Create(new Schedule
        {
            Action = "выдуманное",
            Kind = "periodic",
            PeriodJson = """{"type":"weekly","days":[1],"time":"03:00"}""",
        }, null));
    }

    // --- 5. запись в справочнике действий ---

    [Fact]
    public void The_Action_Has_A_Catalog_Record_In_Both_Languages()
    {
        var code = ScheduleActions.CatalogCodeOf(ScheduleActions.AutoArchive);
        Assert.Equal("AI2P.Archives.AutoRun", code);

        foreach (var lang in new[] { "ru", "en" })
        {
            var action = _f.Actions.List(lang).Single(a => a.Code == code);
            Assert.NotEmpty(action.Title);
            Assert.NotEmpty(action.Hint);
            Assert.False(action.IsCustom);
        }
    }
}
