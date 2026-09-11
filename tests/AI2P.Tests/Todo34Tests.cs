using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>Условия запуска задач и расписание (ТЗ пп. 2.1, 2.12, v1.35, todo34):
/// блокирующие задачи, их подмена при копировании шаблона, вычисление срабатываний
/// расписаний, просроченные, проверка перехлеста исполнителей.</summary>
public sealed class Todo34Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem NewTask(string title, bool template = false, string? parentId = null,
        List<string>? blockers = null, List<string>? executors = null) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            IsTemplate = template,
            ParentId = parentId,
            BlockerIds = blockers ?? [],
            ExecutorIds = executors ?? [],
        }, "", "", null);

    [Fact]
    public void Blockers_Are_Saved_And_Loaded()
    {
        var a = NewTask("A");
        var b = NewTask("B");
        var c = NewTask("C", blockers: [a.Id, b.Id]);
        var loaded = _f.Tasks.Get(c.Id)!;
        Assert.Equal(2, loaded.BlockerIds.Count);
        Assert.Contains(a.Id, loaded.BlockerIds);
        Assert.Contains(b.Id, loaded.BlockerIds);
    }

    [Fact]
    public void Blockers_Validation()
    {
        var task = NewTask("A");
        // сама себя — нельзя
        task.BlockerIds = [task.Id];
        Assert.Throws<ArgumentException>(() => _f.Tasks.Update(task, "", "", null));
        // шаблон не может блокировать задачу
        var template = NewTask("T", template: true);
        Assert.Throws<ArgumentException>(() => NewTask("B", blockers: [template.Id]));
        // задача не может блокировать шаблон
        var plain = NewTask("C");
        Assert.Throws<ArgumentException>(() => NewTask("T2", template: true, blockers: [plain.Id]));
    }

    [Fact]
    public void BlockersDone_Requires_All_Blockers_Done()
    {
        // с версии 1.88 (T-6-S1) «завершена» для блокирующей — это только «готово»:
        // отменённая блокирующая ждущую задачу не отпускает, а держит её навсегда
        var a = NewTask("A");
        var b = NewTask("B");
        var c = NewTask("C", blockers: [a.Id, b.Id]);
        Assert.False(_f.Tasks.BlockersDone(c));
        _f.Tasks.ChangeStatus(a.Id, TaskStatuses.Done, null);
        Assert.False(_f.Tasks.BlockersDone(c));
        _f.Tasks.ChangeStatus(b.Id, TaskStatuses.Done, null);
        Assert.True(_f.Tasks.BlockersDone(c));
        Assert.Contains(_f.Tasks.ListBlockedBy(a.Id), t => t.Id == c.Id);

        // а отменённая — отдельный исход (BlockersState.Cancelled)
        var d = NewTask("D");
        var e = NewTask("E", blockers: [d.Id]);
        _f.Tasks.ChangeStatus(d.Id, TaskStatuses.Cancelled, null);
        Assert.False(_f.Tasks.BlockersDone(e));
        Assert.True(_f.Tasks.BlockersCancelled(e));
    }

    [Fact]
    public void Instantiate_Replaces_Template_Blockers_With_Copies()
    {
        // голова + два узла; Y заблокирован X — в копии ссылка должна указывать на копию X
        var head = NewTask("Голова", template: true);
        var x = NewTask("X", template: true, parentId: head.Id);
        var y = _f.Tasks.Create(new TaskItem
        {
            Title = "Y",
            IsTemplate = true,
            ParentId = head.Id,
            BlockerIds = [x.Id],
        }, "", "", null);
        Assert.Single(_f.Tasks.Get(y.Id)!.BlockerIds);

        var newHead = _f.Tasks.InstantiateTemplate(head.Id, null, null);
        var copies = _f.Tasks.List(includeTemplates: false)
            .Where(t => t.ParentId == newHead.Id).ToList();
        var xCopy = copies.Single(t => t.Title == "X");
        var yCopy = copies.Single(t => t.Title == "Y");
        Assert.Equal([xCopy.Id], _f.Tasks.Get(yCopy.Id)!.BlockerIds);
    }

    [Fact]
    public void Occurrences_Once_And_Weekly()
    {
        // однократное: попадает только в свой интервал
        var at = DateTime.UtcNow.AddHours(1);
        var once = new Schedule { Kind = "once", StartAt = at };
        Assert.Single(ScheduleService.Occurrences(once, DateTime.UtcNow, DateTime.UtcNow.AddDays(1)));
        Assert.Empty(ScheduleService.Occurrences(once, at, at.AddDays(1))); // строго после from

        // недельное: среда 10:00 локального времени в неделе 2026-07-20 (Пн) … 26 (Вс)
        var weekly = new Schedule
        {
            Kind = "periodic",
            PeriodJson = """{"type":"weekly","days":[3],"time":"10:00"}""",
        };
        var fromUtc = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var toUtc = new DateTime(2026, 7, 26, 23, 59, 0, DateTimeKind.Local).ToUniversalTime();
        var occurrences = ScheduleService.Occurrences(weekly, fromUtc, toUtc);
        var local = Assert.Single(occurrences).ToLocalTime();
        Assert.Equal(new DateTime(2026, 7, 22, 10, 0, 0), local);
    }

    [Fact]
    public void Occurrences_Monthly_And_Quarterly()
    {
        var monthly = new Schedule
        {
            Kind = "periodic",
            PeriodJson = """{"type":"monthly","day":31,"time":"08:00"}""",
        };
        var fromUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var toUtc = new DateTime(2026, 3, 31, 23, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var months = ScheduleService.Occurrences(monthly, fromUtc, toUtc)
            .Select(o => o.ToLocalTime()).ToList();
        // день 31 в коротком месяце сжимается до последнего дня (28 февраля)
        Assert.Equal(2, months.Count);
        Assert.Equal(new DateTime(2026, 2, 28, 8, 0, 0), months[0]);
        Assert.Equal(new DateTime(2026, 3, 31, 8, 0, 0), months[1]);

        // квартальный: 2-й месяц квартала, 15-е — февраль/май в первой половине года
        var quarterly = new Schedule
        {
            Kind = "periodic",
            PeriodJson = """{"type":"quarterly","month":2,"day":15,"time":"09:00"}""",
        };
        var q = ScheduleService.Occurrences(quarterly,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local).ToUniversalTime(),
                new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Local).ToUniversalTime())
            .Select(o => o.ToLocalTime()).ToList();
        Assert.Equal(2, q.Count);
        Assert.Equal(new DateTime(2026, 2, 15, 9, 0, 0), q[0]);
        Assert.Equal(new DateTime(2026, 5, 15, 9, 0, 0), q[1]);
    }

    [Fact]
    public void Schedule_Crud_Due_And_MarkProcessed()
    {
        var template = NewTask("Шаблон", template: true);
        // однократное срабатывание через час; проверка «спустя два часа» — просрочено
        // (отсчёт — от момента создания расписания: прошлое до создания не считается)
        var schedule = _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddHours(1),
            OffsetMinutes = 90,
        }, null);
        Assert.StartsWith("SCH-", schedule.DisplayId);

        var later = DateTime.UtcNow.AddHours(2);
        var due = _f.Schedules.Due(later);
        var missed = Assert.Single(due);
        Assert.Equal(schedule.Id, missed.Schedule.Id);
        Assert.Single(missed.Missed);

        _f.Schedules.MarkProcessed(schedule.Id, later);
        Assert.Empty(_f.Schedules.Due(later));

        // не-шаблон и узел не верхнего уровня отклоняются
        var plain = NewTask("Обычная");
        Assert.Throws<ArgumentException>(() => _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = plain.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow,
        }, null));
    }

    [Fact]
    public void Schedule_Stores_Project()
    {
        // проект расписания (todo34_2): сохраняется, перечитывается и правится
        var project = _f.Projects.Create("Проект расписаний", null, null, null);
        var template = NewTask("Шаблон", template: true);
        var schedule = _f.Schedules.Create(new Schedule
        {
            ProjectId = project.Id,
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddDays(1),
        }, null);
        Assert.Equal(project.Id, _f.Schedules.Get(schedule.Id)!.ProjectId);

        schedule.ProjectId = null;
        _f.Schedules.Update(schedule, null);
        Assert.Null(_f.Schedules.Get(schedule.Id)!.ProjectId);
    }

    [Fact]
    public void Schedule_Overlap_Is_Rejected()
    {
        var owner = _f.Executors.EnsureLocalOwner();
        var template = _f.Tasks.Create(new TaskItem
        {
            Title = "С исполнителем",
            IsTemplate = true,
            ExecutorIds = [owner.Id],
        }, "", "", null);
        var at = DateTime.UtcNow.AddDays(1);
        _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = at,
            PlannedMinutes = 60,
        }, null);
        // перехлест: тот же исполнитель занят через полчаса
        Assert.Throws<ArgumentException>(() => _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = at.AddMinutes(30),
            PlannedMinutes = 60,
        }, null));
        // без пересечения окон — проходит
        _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = at.AddHours(3),
            PlannedMinutes = 60,
        }, null);
    }
}
