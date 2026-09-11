using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Справочник состояний задач и занятость исполнителей (ТЗ v1.37, todo34_3):
/// сид встроенных состояний (номера через 10), CRUD с локализуемыми названиями,
/// «занят до» исполнителя и его учёт автоподбором.
/// </summary>
public sealed class Todo34_3Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Creates_BuiltIn_Statuses_With_Step10_Order()
    {
        var statuses = _f.Statuses.List("ru", includeInactive: true);

        Assert.Equal(TaskStatuses.BuiltIn.Length, statuses.Count);
        Assert.All(statuses, s => Assert.False(s.IsCustom));
        Assert.All(statuses, s => Assert.True(s.IsActive));
        // порядковые номера встроенных — через 10, порядок сида сохранён
        Assert.Equal([10, 20, 30, 40, 50, 60, 70, 80, 90], statuses.Select(s => s.SortOrder));
        Assert.Equal(TaskStatuses.BuiltIn, statuses.Select(s => s.Id));
        // названия — прежние надписи ru
        Assert.Equal("в работе", statuses.Single(s => s.Id == TaskStatuses.InProgress).Name);
        // повторный сид ничего не дублирует
        _f.Statuses.Seed();
        Assert.Equal(TaskStatuses.BuiltIn.Length, _f.Statuses.List("ru", includeInactive: true).Count);
    }

    [Fact]
    public void Custom_Status_Created_And_Localized_Name_Saved_Per_Language()
    {
        var created = _f.Statuses.Create(new TaskStatusDef
        {
            Id = "testing", Name = "тестирование", Color = "#123456", SortOrder = 45,
        }, "ru");
        Assert.True(created.IsCustom); // новые через UI — всегда кастомные

        var ru = _f.Statuses.List("ru", includeInactive: true).Single(s => s.Id == "testing");
        Assert.Equal("тестирование", ru.Name);
        Assert.Equal("#123456", ru.Color);
        Assert.Equal(45, ru.SortOrder);
        // на языке без правки — фолбэк en → код
        Assert.Equal("testing", _f.Statuses.List("en", includeInactive: true)
            .Single(s => s.Id == "testing").Name);

        // правка названия на en не трогает ru
        _f.Statuses.Update("testing", new TaskStatusDef
        {
            Id = "testing", Name = "testing phase", Color = "#123456", IsCustom = true,
            IsActive = true, SortOrder = 45,
        }, "en");
        Assert.Equal("тестирование",
            _f.Statuses.List("ru", includeInactive: true).Single(s => s.Id == "testing").Name);
        Assert.Equal("testing phase",
            _f.Statuses.List("en", includeInactive: true).Single(s => s.Id == "testing").Name);

        // дубль кода отклоняется
        Assert.Throws<ArgumentException>(() =>
            _f.Statuses.Create(new TaskStatusDef { Id = "testing" }, "ru"));
    }

    [Fact]
    public void Inactive_Status_Excluded_From_Active_List_And_Sorting_By_Order()
    {
        _f.Statuses.Create(new TaskStatusDef { Id = "first", Name = "первое", SortOrder = 5 }, "ru");
        var active = _f.Statuses.List("ru");
        Assert.Equal("first", active[0].Id); // порядковый номер 5 — впереди встроенных

        var done = _f.Statuses.List("ru").Single(s => s.Id == TaskStatuses.Done);
        done.IsActive = false;
        _f.Statuses.Update(done.Id, done, "ru");
        Assert.DoesNotContain(_f.Statuses.List("ru"), s => s.Id == TaskStatuses.Done);
        Assert.Contains(_f.Statuses.List("ru", includeInactive: true), s => s.Id == TaskStatuses.Done);
    }

    [Fact]
    public void Task_Accepts_Custom_Status_Code()
    {
        _f.Statuses.Create(new TaskStatusDef { Id = "testing", Name = "тестирование" }, "ru");
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);

        _f.Tasks.ChangeStatus(task.Id, "testing", null);

        Assert.Equal("testing", _f.Tasks.Get(task.Id)!.Status);
        Assert.True(_f.Statuses.Exists("testing"));
        Assert.False(_f.Statuses.Exists("no_such"));
    }

    [Fact]
    public void Busy_Executor_Roundtrip_And_SetBusy()
    {
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var until = DateTime.UtcNow.AddHours(2);

        // человек: «занят до» проставляется через форму (Update)
        human.BusyUntil = until;
        _f.Executors.Update(human, null);
        var reread = _f.Executors.Get(human.Id)!;
        Assert.NotNull(reread.BusyUntil);
        Assert.Equal(until, reread.BusyUntil!.Value, TimeSpan.FromSeconds(1));

        // ИИ: фиксация лимита провайдера (SetBusy) — «занят до» и % использования
        _f.Executors.SetBusy(human.Id, until.AddHours(1), 87.5);
        reread = _f.Executors.Get(human.Id)!;
        Assert.Equal(until.AddHours(1), reread.BusyUntil!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(87.5, reread.LimitPercent);

        // только процент — «занят до» не трогается
        _f.Executors.SetBusy(human.Id, null, 12.0);
        Assert.Equal(12.0, _f.Executors.Get(human.Id)!.LimitPercent);
        Assert.NotNull(_f.Executors.Get(human.Id)!.BusyUntil);
    }

    [Fact]
    public void Pick_Skips_Busy_Executor_Until_Free()
    {
        var busy = _f.Executors.Create(new Executor { Nick = "busy", Kind = ExecutorKind.Human }, null);
        var free = _f.Executors.Create(new Executor { Nick = "free", Kind = ExecutorKind.Human }, null);
        var skill = _f.RefData.Skills().First();
        foreach (var executor in new[] { busy, free })
        {
            _f.Files.WriteText(executor.CapabilitiesPath,
                $$"""{ "skills": [ { "name": "{{skill.Name}}", "score": 80 } ] }""");
        }
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members = [new TeamMember { ExecutorId = busy.Id }, new TeamMember { ExecutorId = free.Id }],
        }, null);
        _f.Executors.SetBusy(busy.Id, DateTime.UtcNow.AddHours(3), null);

        // запуск сейчас: занятый пропускается, назначается свободный
        var picked = _f.Picker.Pick(null, team.Id, [skill.Id], PickMode.HumanFirst);
        Assert.Equal(free.Id, picked.ExecutorId);

        // запуск после освобождения: занятый снова кандидат («busy» алфавитно раньше «free»)
        picked = _f.Picker.Pick(null, team.Id, [skill.Id], PickMode.HumanFirst,
            startAt: DateTime.UtcNow.AddHours(4));
        Assert.Equal(busy.Id, picked.ExecutorId);

        // все заняты — понятная причина
        _f.Executors.SetBusy(free.Id, DateTime.UtcNow.AddHours(3), null);
        picked = _f.Picker.Pick(null, team.Id, [skill.Id], PickMode.HumanFirst);
        Assert.Null(picked.ExecutorId);
        Assert.Contains("заняты", picked.Reason);
    }
}
