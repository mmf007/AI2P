using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-41-S0: ПРАВИЛА АРХИВАЦИИ — общие правила организации и правила архива.
///
/// 1. Таблица правил ОДНА на обе группы: у общего правила ссылки на архив нет, у правила
///    архива есть; признак группы производный, отдельной колонки не заведено.
/// 2. Правила реплицируются вместе с реестром архивов (ChangeLog.OrgTables).
/// 3. Правила нового архива переносятся по выбору человека: общие / из предыдущего
///    текущего архива / никакие. «Вернуть умолчание» — то же действие с общими правилами.
/// 4. ОТБОР КАНДИДАТОВ: иерархия задач архивируется ЦЕЛИКОМ — у корня проверяются
///    состояние и возраст, у КАЖДОГО потомка только попадание состояния в список; один
///    неподходящий потомок отменяет архивацию всей ветки.
/// 5. Возраст считается по выбранной дате, срок бывает временной (дни) и календарный
///    (месяцы и года; месяцы не указаны — только года).
/// </summary>
public sealed class T41S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveRuleService Rules() => new(_f.Db, _f.Events);

    private ArchiveService Archives()
    {
        var service = new ArchiveService(_f.Db, _f.Events) { Rules = Rules() };
        return service;
    }

    private ArchiveCandidateService Candidates() => new(_f.Db);

    private static ArchiveRule TaskRule(string status, int days, string? archiveId = null,
        params string[] children) => new()
    {
        ArchiveId = archiveId,
        Target = ArchiveRuleTargets.Tasks,
        TaskStatus = status,
        ChildStatuses = [.. children],
        DateField = ArchiveRuleDates.Updated,
        AgeKind = ArchiveRuleAges.Days,
        AgeDays = days,
    };

    /// <summary>Состарить задачу: даты в базе правит только хранилище, а нам нужен возраст.</summary>
    private void Age(string taskId, int days)
    {
        using var conn = _f.Db.Open();
        var when = Sql.ToDb(DateTime.UtcNow.AddDays(-days));
        Sql.Exec(conn, null, "UPDATE tasks SET created_at=@w, updated_at=@w WHERE id=@id",
            ("@w", when), ("@id", taskId));
    }

    private TaskItem Task(string title, string status, string? parentId = null)
    {
        return _f.Tasks.Create(new TaskItem
        {
            Title = title,
            Status = status,
            ParentId = parentId,
        }, "", "", null);
    }

    // --- модель правила ---

    [Fact]
    public void Rules_Are_Replicated_And_The_Group_Is_Derived_From_The_Archive_Link()
    {
        Assert.Contains("archive_rules", ChangeLog.OrgTables);

        var rules = Rules();
        var common = rules.Create(TaskRule(TaskStatuses.Done, 30), null);
        Assert.True(common.IsCommon);
        Assert.Null(common.ArchiveId);
        Assert.Equal("ARR-1", common.DisplayId);

        var archive = Archives().Create("arc41a", "Архив", ArchiveRuleModes.Empty, null);
        var own = rules.Create(TaskRule(TaskStatuses.Done, 30, archive.Id), null);
        Assert.False(own.IsCommon);
        Assert.Equal(archive.Id, own.ArchiveId);

        // группы не смешиваются: общие правила и правила архива запрашиваются раздельно
        Assert.Single(rules.List());
        Assert.Single(rules.List(archive.Id));
    }

    [Fact]
    public void A_Rule_Keeps_Every_Field_Of_The_Task()
    {
        var rules = Rules();
        var saved = rules.Create(new ArchiveRule
        {
            Target = ArchiveRuleTargets.Objects,
            Activity = ArchiveRuleActivity.Inactive,
            DateField = ArchiveRuleDates.Created,
            AgeKind = ArchiveRuleAges.Calendar,
            AgeMonths = 3,
            AgeYears = 1,
            IsActive = false,
        }, null);
        var read = rules.Get(saved.Id)!;
        Assert.Equal(ArchiveRuleTargets.Objects, read.Target);
        Assert.Equal(ArchiveRuleActivity.Inactive, read.Activity);
        Assert.Equal(ArchiveRuleDates.Created, read.DateField);
        Assert.Equal(ArchiveRuleAges.Calendar, read.AgeKind);
        Assert.Equal(3, read.AgeMonths);
        Assert.Equal(1, read.AgeYears);
        Assert.False(read.IsActive);
        // состояние задачи у правила НЕ про задачи не хранится вовсе
        Assert.Equal("", read.TaskStatus);

        var withChildren = rules.Create(
            TaskRule(TaskStatuses.Done, 10, null, TaskStatuses.Done, TaskStatuses.Cancelled), null);
        Assert.Equal([TaskStatuses.Done, TaskStatuses.Cancelled],
            rules.Get(withChildren.Id)!.ChildStatuses);
    }

    [Fact]
    public void A_Rule_About_Tasks_Without_A_Status_And_A_Rule_Without_A_Term_Are_Refused()
    {
        var rules = Rules();
        Assert.Throws<ArgumentException>(() => rules.Create(TaskRule("", 30), null));
        Assert.Throws<ArgumentException>(() => rules.Create(TaskRule(TaskStatuses.Done, 0), null));
        Assert.Throws<ArgumentException>(() => rules.Create(new ArchiveRule
        {
            Target = "неведомый-вид",
            AgeDays = 5,
        }, null));
        Assert.Empty(rules.List());
    }

    // --- перенос правил ---

    [Fact]
    public void A_New_Archive_Takes_The_Common_Rules_The_Previous_Ones_Or_Nothing()
    {
        var rules = Rules();
        rules.Create(TaskRule(TaskStatuses.Done, 30), null);
        rules.Create(TaskRule(TaskStatuses.Cancelled, 60), null);
        var archives = Archives();

        // способ «общие правила» — умолчание создания архива
        var first = archives.Create("arc41b", "Первый", ArchiveRuleModes.Common, null);
        Assert.Equal(2, rules.List(first.Id).Count);
        Assert.All(rules.List(first.Id), r => Assert.Equal(first.Id, r.ArchiveId));
        // копия, а не ссылка: правка правила архива общих правил не трогает
        var copy = rules.List(first.Id)[0];
        copy.AgeDays = 999;
        rules.Update(copy, null);
        Assert.DoesNotContain(rules.List(), r => r.AgeDays == 999);

        // способ «копия прежнего текущего архива» — источником берётся именно он
        var second = archives.Create("arc41c", "Второй", ArchiveRuleModes.Copy, null);
        Assert.Equal(2, rules.List(second.Id).Count);
        Assert.Contains(rules.List(second.Id), r => r.AgeDays == 999);

        // способ «без правил»
        var third = archives.Create("arc41d", "Третий", ArchiveRuleModes.Empty, null);
        Assert.Empty(rules.List(third.Id));
    }

    [Fact]
    public void Restore_The_Default_Replaces_The_Rules_Of_The_Archive_With_The_Common_Ones()
    {
        var rules = Rules();
        rules.Create(TaskRule(TaskStatuses.Done, 30), null);
        var archive = Archives().Create("arc41e", "Архив", ArchiveRuleModes.Empty, null);
        rules.Create(TaskRule(TaskStatuses.Error, 5, archive.Id), null);
        Assert.Single(rules.List(archive.Id));

        var restored = rules.ResetToCommon(archive.Id, null);
        Assert.Single(restored);
        var now = rules.List(archive.Id);
        Assert.Single(now);
        Assert.Equal(TaskStatuses.Done, now[0].TaskStatus);
        Assert.Equal(30, now[0].AgeDays);
        // общие правила при этом остались на месте — это образец, а не источник переноса
        Assert.Single(rules.List());
    }

    // --- отбор кандидатов ---

    [Fact]
    public void A_Root_Task_Is_Selected_By_Its_Status_And_Age()
    {
        var old = Task("Старая готовая", TaskStatuses.Done);
        Age(old.Id, 100);
        var fresh = Task("Свежая готовая", TaskStatuses.Done);
        var wrongStatus = Task("Старая в работе", TaskStatuses.InProgress);
        Age(wrongStatus.Id, 100);

        var found = Candidates().Select(TaskRule(TaskStatuses.Done, 30));
        Assert.Single(found);
        Assert.Equal(old.Id, found[0].Id);
        Assert.DoesNotContain(found, c => c.Id == fresh.Id || c.Id == wrongStatus.Id);
    }

    [Fact]
    public void One_Unsuitable_Child_Cancels_The_Archiving_Of_The_Whole_Hierarchy()
    {
        var root = Task("Корень", TaskStatuses.Done);
        var child = Task("Потомок", TaskStatuses.Done, root.Id);
        var grand = Task("Внук", TaskStatuses.InProgress, child.Id);
        Age(root.Id, 100);
        Age(child.Id, 100);
        Age(grand.Id, 100);

        var service = Candidates();
        // внук «в работе» — вся ветка остаётся в оперативной среде
        Assert.Empty(service.Select(TaskRule(TaskStatuses.Done, 30, null, TaskStatuses.Done)));
        // список состояний потомков ПУСТ — значит любое состояние, ветка подходит
        var any = service.Select(TaskRule(TaskStatuses.Done, 30));
        Assert.Single(any);
        Assert.Equal(root.Id, any[0].Id);
        Assert.Equal(2, any[0].Children);
        // потомок и внук сами по себе кандидатами не бывают: правило про КОРНЕВЫЕ задачи
        Assert.DoesNotContain(any, c => c.Id == child.Id || c.Id == grand.Id);

        // разрешили и «в работе» — ветка уходит целиком
        Assert.Single(service.Select(
            TaskRule(TaskStatuses.Done, 30, null, TaskStatuses.Done, TaskStatuses.InProgress)));
    }

    [Fact]
    public void The_Term_By_Days_Counts_Days_And_The_Calendar_Term_Counts_Months_And_Years()
    {
        var now = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        // ВРЕМЕННОЙ срок: ровно столько дней назад
        Assert.Equal(now.AddDays(-30),
            ArchiveAge.Threshold(ArchiveRuleAges.Days, 30, 0, 0, now));
        // календарные поля временному сроку безразличны
        Assert.Equal(now.AddDays(-30),
            ArchiveAge.Threshold(ArchiveRuleAges.Days, 30, 7, 3, now));

        // КАЛЕНДАРНЫЙ срок: месяцы и года — по календарю, а не по 30 дней в месяце
        Assert.Equal(new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc),
            ArchiveAge.Threshold(ArchiveRuleAges.Calendar, 0, 3, 0, now));
        Assert.Equal(new DateTime(2024, 5, 29, 12, 0, 0, DateTimeKind.Utc),
            ArchiveAge.Threshold(ArchiveRuleAges.Calendar, 0, 3, 2, now));
        // МЕСЯЦЫ НЕ УКАЗАНЫ — учитываются только года (прямое требование задания)
        Assert.Equal(new DateTime(2024, 8, 29, 12, 0, 0, DateTimeKind.Utc),
            ArchiveAge.Threshold(ArchiveRuleAges.Calendar, 0, 0, 2, now));
        // дни календарному сроку безразличны
        Assert.Equal(new DateTime(2025, 8, 29, 12, 0, 0, DateTimeKind.Utc),
            ArchiveAge.Threshold(ArchiveRuleAges.Calendar, 900, 0, 1, now));
    }

    [Fact]
    public void The_Calendar_Term_Selects_Tasks_Older_Than_A_Whole_Year()
    {
        var older = Task("Позапрошлогодняя", TaskStatuses.Done);
        Age(older.Id, 400);
        var newer = Task("Полугодовалая", TaskStatuses.Done);
        Age(newer.Id, 180);

        var rule = TaskRule(TaskStatuses.Done, 0);
        rule.AgeKind = ArchiveRuleAges.Calendar;
        rule.AgeYears = 1;
        var found = Candidates().Select(rule);
        Assert.Single(found);
        Assert.Equal(older.Id, found[0].Id);

        // тот же срок, но с добавкой в полгода — уже подходят обе задачи
        rule.AgeYears = 0;
        rule.AgeMonths = 5;
        Assert.Equal(2, Candidates().Select(rule).Count);
    }

    [Fact]
    public void The_Date_Of_Creation_And_The_Date_Of_Change_Give_Different_Answers()
    {
        var task = Task("Заведена давно, правилась вчера", TaskStatuses.Done);
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE tasks SET created_at=@c, updated_at=@u WHERE id=@id",
                ("@c", Sql.ToDb(DateTime.UtcNow.AddDays(-400))),
                ("@u", Sql.ToDb(DateTime.UtcNow.AddDays(-1))), ("@id", task.Id));
        }
        var byCreated = TaskRule(TaskStatuses.Done, 100);
        byCreated.DateField = ArchiveRuleDates.Created;
        Assert.Single(Candidates().Select(byCreated));
        // по дате изменения та же задача в архив ещё не идёт
        Assert.Empty(Candidates().Select(TaskRule(TaskStatuses.Done, 100)));
    }

    [Fact]
    public void Rules_Are_Satisfied_When_There_Is_Nothing_Left_To_Archive()
    {
        var service = Candidates();
        var rules = new[] { TaskRule(TaskStatuses.Done, 30) };
        Assert.True(service.Satisfied(rules));

        var old = Task("Старая готовая", TaskStatuses.Done);
        Age(old.Id, 100);
        Assert.False(service.Satisfied(rules));

        // выключенное правило в отбор не идёт вовсе
        var off = TaskRule(TaskStatuses.Done, 30);
        off.IsActive = false;
        Assert.True(service.Satisfied([off]));
    }

    [Fact]
    public void Texts_Of_The_Section_Are_In_Both_Dictionaries()
    {
        var keys = new List<string>();
        for (var i = 1; i <= 7; i++)
        {
            keys.Add("msg.arcrule." + i);
        }
        keys.AddRange(["arcrules.title", "arcrules.common.title", "arcrules.add",
            "arcrules.reset", "arcrules.reset.ask", "arcrules.open", "arcrules.target",
            "arcrules.status", "arcrules.condition", "arcrules.children",
            "arcrules.children.any", "arcrules.activity", "arcrules.date", "arcrules.age",
            "arcrules.days", "arcrules.months", "arcrules.years"]);
        keys.AddRange(ArchiveRuleTargets.All.Select(t => "arcrules.target." + t));
        keys.AddRange(ArchiveRuleActivity.All.Select(a => "arcrules.activity." + a));
        keys.AddRange(ArchiveRuleDates.All.Select(d => "arcrules.date." + d));
        keys.AddRange(ArchiveRuleAges.All.Select(a => "arcrules.age." + a));
        foreach (var key in keys)
        {
            Assert.NotEqual(key, Loc.In("ru", key));
            Assert.NotEqual(key, Loc.In("en", key));
        }
    }
}
