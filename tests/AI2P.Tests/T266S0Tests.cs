using System.Reflection;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-266-S0 (ветка T-318 «Проект опыта»): СОХРАНЕНИЕ ИСПОЛЬЗОВАННОГО ОПЫТА В ЗАДАЧЕ.
///
/// <list type="number">
/// <item>идентификаторы записей, ушедших в текст задания, сохраняются у задачи
/// (таблица <c>task_experience_used</c>, схема организации v47) — и ТОЛЬКО идентификаторы:
/// текст записи не копируется;</item>
/// <item>набор в таблице совпадает с тем, что НАПЕЧАТАНО в блоках опыта задания: не поместившиеся
/// в предел записи следа не оставляют;</item>
/// <item>повторный запуск задания строки не задваивает — он их дополняет;</item>
/// <item>удалённая (унесённая в архив) запись выдачу не ломает: строка остаётся, запись пуста;</item>
/// <item>есть статистика использования записи — сколько задач её получило и когда последний раз;</item>
/// <item>таблица РЕПЛИЦИРУЕТСЯ: задания идут на разных серверах кластера, а анализ опыта
/// делается на одном.</item>
/// </list>
/// </summary>
public sealed class T266S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Project NewProject() =>
        _f.Projects.Create("Проект T-266-S0", Path.Combine(_f.Dir, "prj266"), null, null);

    private TaskItem NewTask(Project project, string title = "Работа") =>
        _f.Tasks.Create(new TaskItem { Title = title, ProjectId = project.Id }, "", "", null);

    /// <summary>Промпт задания — тем же способом, что в T-11-S0: метод закрытый, а проверять
    /// надо именно его (след использования пишется ровно там, где печатается блок опыта).</summary>
    private string BuildRequest(TaskItem task) => (string)typeof(JobOrchestrator)
        .GetMethod("BuildAiRequest", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(_f.Orchestrator, [task, null])!;

    // ---------- запись факта ----------

    [Fact]
    public void The_Records_That_Went_Into_The_Prompt_Are_Remembered_By_The_Task()
    {
        var project = NewProject();
        var task = NewTask(project);
        var mine = _f.Experience.CreateForProject(project.Id, "вывод по проекту", null,
            alwaysLoad: true);
        var general = _f.Experience.CreateGeneral("общее правило работы", null, alwaysLoad: true);

        var request = BuildRequest(task);
        Assert.Contains(mine.Id, request);
        Assert.Contains(general.Id, request);

        var used = _f.Experience.ListUsedByTask(task.Id);
        Assert.Equal(2, used.Count);
        Assert.Contains(mine.Id, used.Select(u => u.ExperienceId));
        Assert.Contains(general.Id, used.Select(u => u.ExperienceId));
        Assert.All(used, u => Assert.NotNull(u.Record));
        Assert.All(used, u => Assert.True(u.UsedAt > DateTime.UtcNow.AddMinutes(-5)));
    }

    [Fact]
    public void Only_Identifiers_Are_Stored_The_Text_Is_Not_Copied()
    {
        var project = NewProject();
        var task = NewTask(project);
        _f.Experience.CreateForProject(project.Id, "текст, который нельзя копировать", null,
            alwaysLoad: true);
        BuildRequest(task);

        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(task_experience_used)",
            r => r.S("name"));
        Assert.Equal(["task_id", "experience_id", "job_id", "used_at"], columns);
        // и в самих строках текста нет ни у одной
        var dump = string.Join(" ", Sql.Query(conn, null,
            "SELECT task_id || ' ' || experience_id AS row FROM task_experience_used",
            r => r.S("row")));
        Assert.DoesNotContain("нельзя копировать", dump);
    }

    [Fact]
    public void A_Record_Skipped_By_The_Budget_Leaves_No_Trace()
    {
        var project = NewProject();
        // ЗАПИСИ УЗЛА ШАБЛОНА, а не пометка «загружать всегда»: с T-267-S0 помеченные
        // «загружать всегда» берутся ВНЕ бюджета и вне квот — то есть отбросить их пределом
        // нельзя вовсе (сведение ветки T-318). Опыт узла шаблона идёт в задание и без навыка
        // (skillRequired: false), поэтому тесный предел проверяется именно на нём
        var node = _f.Tasks.Create(new TaskItem { Title = "Узел", IsTemplate = true }, "", "", null);
        var task = _f.Tasks.Create(
            new TaskItem { Title = "Работа", ProjectId = project.Id, TemplateId = node.Id },
            "", "", null);
        // предел подстановки опыта — настройка проекта (T-29-S0): ставим тесный, чтобы
        // вторая запись в задание не поместилась
        project.SettingsJson = $$"""{"{{ProjectSettings.ExperienceLimitKey}}": 120}""";
        _f.Projects.Update(project, null);
        var first = _f.Experience.Create(node.Id, new string('а', 80), null);
        var second = _f.Experience.Create(node.Id, new string('б', 80), null);

        var request = BuildRequest(task);
        var used = _f.Experience.ListUsedByTask(task.Id).Select(u => u.ExperienceId).ToList();
        // след оставляет ровно то, что НАПЕЧАТАНО в блоке опыта, — ни строкой больше
        Assert.Equal(request.Contains(first.Id), used.Contains(first.Id));
        Assert.Equal(request.Contains(second.Id), used.Contains(second.Id));
        Assert.Single(used);
    }

    [Fact]
    public void A_Second_Run_Adds_Rows_Instead_Of_Doubling_Them()
    {
        var project = NewProject();
        var task = NewTask(project);
        var first = _f.Experience.CreateForProject(project.Id, "первая запись", null,
            alwaysLoad: true);
        BuildRequest(task);
        var firstUse = _f.Experience.ListUsedByTask(task.Id).Single().UsedAt;

        // перезапуск: та же запись плюс новая — строк ровно две, а не три
        var second = _f.Experience.CreateForProject(project.Id, "вторая запись", null,
            alwaysLoad: true);
        Thread.Sleep(5);
        BuildRequest(task);

        var used = _f.Experience.ListUsedByTask(task.Id);
        Assert.Equal(2, used.Count);
        Assert.Contains(first.Id, used.Select(u => u.ExperienceId));
        Assert.Contains(second.Id, used.Select(u => u.ExperienceId));
        // у уже известной пары время использования обновилось
        Assert.True(used.Single(u => u.ExperienceId == first.Id).UsedAt >= firstUse);
    }

    [Fact]
    public void Two_Tasks_Keep_Their_Own_Sets()
    {
        var project = NewProject();
        var one = NewTask(project, "Первая");
        var two = NewTask(project, "Вторая");
        _f.Experience.CreateForProject(project.Id, "общий для обеих вывод", null, alwaysLoad: true);

        BuildRequest(one);
        Assert.Single(_f.Experience.ListUsedByTask(one.Id));
        Assert.Empty(_f.Experience.ListUsedByTask(two.Id));

        BuildRequest(two);
        Assert.Single(_f.Experience.ListUsedByTask(two.Id));
    }

    // ---------- чтение ----------

    [Fact]
    public void A_Deleted_Record_Stays_In_The_List_As_Unavailable()
    {
        var project = NewProject();
        var task = NewTask(project);
        var record = _f.Experience.CreateForProject(project.Id, "запись, которую удалят", null,
            alwaysLoad: true);
        BuildRequest(task);

        _f.Experience.Delete(record.Id, null);

        var used = _f.Experience.ListUsedByTask(task.Id).Single();
        Assert.Equal(record.Id, used.ExperienceId);
        Assert.Null(used.Record);   // в выдаче — строка «запись недоступна»
    }

    [Fact]
    public void The_List_Tells_The_Scope_Of_Every_Record()
    {
        var project = NewProject();
        var task = NewTask(project);
        var general = _f.Experience.CreateGeneral("общее правило", null, alwaysLoad: true);
        var byProject = _f.Experience.CreateForProject(project.Id, "вывод проекта", null,
            alwaysLoad: true);
        BuildRequest(task);

        var used = _f.Experience.ListUsedByTask(task.Id);
        Assert.True(used.Single(u => u.ExperienceId == general.Id).Record!.IsGeneral);
        Assert.True(used.Single(u => u.ExperienceId == byProject.Id).Record!.IsProjectLevel);
    }

    // ---------- статистика ----------

    [Fact]
    public void The_Usage_Of_A_Record_Is_Counted_By_Tasks()
    {
        var project = NewProject();
        var record = _f.Experience.CreateForProject(project.Id, "часто берут", null,
            alwaysLoad: true);
        var never = _f.Experience.CreateForProject(project.Id, "не берут никогда", null);

        BuildRequest(NewTask(project, "Первая"));
        BuildRequest(NewTask(project, "Вторая"));

        var stat = _f.Experience.UsageOf(record.Id);
        Assert.Equal(2, stat.Count);
        Assert.NotNull(stat.LastUsedAt);

        // запись, которую не брали ни разу, отвечает честным нулём — по нему шаблон
        // «Анализ опыта» и будет гасить записи
        var empty = _f.Experience.UsageOf(never.Id);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.LastUsedAt);
    }

    [Fact]
    public void Noting_A_Use_Is_Idempotent_And_Keeps_The_Job()
    {
        var project = NewProject();
        var task = NewTask(project);
        var record = _f.Experience.CreateForProject(project.Id, "запись", null);

        Assert.Equal(1, _f.Experience.NoteUsed(task.Id, [record.Id], "job-1"));
        Assert.Equal(1, _f.Experience.NoteUsed(task.Id, [record.Id, record.Id], "job-2"));
        var used = _f.Experience.ListUsedByTask(task.Id).Single();
        Assert.Equal("job-2", used.JobId);
        // пустой список и пустая задача — не работа
        Assert.Equal(0, _f.Experience.NoteUsed(task.Id, []));
        Assert.Equal(0, _f.Experience.NoteUsed("", [record.Id]));
    }

    // ---------- репликация ----------

    [Fact]
    public void The_Table_Is_Replicated()
    {
        // задания идут на РАЗНЫХ серверах кластера, а анализ опыта делается на одном:
        // без репликации статистика была бы частичной
        Assert.Contains("task_experience_used", ChangeLog.OrgTables);

        var project = NewProject();
        var task = NewTask(project);
        var record = _f.Experience.CreateForProject(project.Id, "запись", null);
        _f.Experience.NoteUsed(task.Id, [record.Id], "job-1");

        using var conn = _f.Db.Open();
        Assert.True(Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM changes WHERE tbl='task_experience_used'") > 0);
    }
}
