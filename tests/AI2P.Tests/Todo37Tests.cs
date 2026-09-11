using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Доработки todo37. Здесь — порча текста при переключении предпросмотра MD-редактора:
/// лишний «\» перед «_» внутри слова и в конце строки с жёстким переносом.
/// </summary>
public sealed class Todo37MarkdownTests
{
    [Fact]
    public void Underscore_inside_word_is_not_escaped()
    {
        // CommonMark: подчёркивание внутри слова курсива не даёт — экранировать нечего,
        // иначе snake_case превращался в snake\_case при каждом переключении (todo37)
        Assert.Equal("файл snake_case и put_it_here",
            MdHtml.ToMarkdown("<p>файл snake_case и put_it_here</p>"));
    }

    [Fact]
    public void Underscore_at_word_edge_is_still_escaped()
    {
        // на границе слова «_» открывает/закрывает курсив — экранирование обязано остаться
        Assert.Equal("\\_не курсив\\_", MdHtml.ToMarkdown("<p>_не курсив_</p>"));
    }

    [Fact]
    public void Hard_break_round_trips_as_two_spaces_not_backslash()
    {
        // две концевые пробела — жёсткий перенос; раньше он возвращался как «\» в конце строки
        const string source = "первая строка  \nвторая строка";
        var once = MdHtml.ToMarkdown(Md.ToHtml(source));
        Assert.Equal(source, once);
        Assert.DoesNotContain("\\", once);
    }

    [Fact]
    public void Round_trip_of_snake_case_and_hard_breaks_is_stable()
    {
        // раунд-трип должен быть неподвижной точкой: повторное переключение ничего не меняет
        const string source = "путь c:\\ai\\models  \nимя model_file_name.gguf  \nконец";
        var once = MdHtml.ToMarkdown(Md.ToHtml(source));
        var twice = MdHtml.ToMarkdown(Md.ToHtml(once));
        Assert.Equal(once, twice);
        Assert.Contains("model_file_name.gguf", once);
    }
}

/// <summary>
/// Запуск работы команды пишется в историю по каждому участнику (todo37): событие
/// team.member_state на попытку подключения и на её результат, привязанное к проекту
/// команды — иначе ошибка запуска не видна ни в общей истории, ни во вкладке проекта.
/// </summary>
public sealed class Todo37TeamWorkHistoryTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Start_Writes_Member_Events_Bound_To_Project()
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var human = _f.Executors.Create(new Executor { Nick = "mike", Kind = ExecutorKind.Human }, null);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var offAi = _f.Executors.Create(new Executor
        {
            Nick = "выключенный", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            Members =
            [
                new TeamMember { ExecutorId = human.Id },
                new TeamMember { ExecutorId = ai.Id },
                new TeamMember { ExecutorId = offAi.Id },
            ],
        }, null);
        offAi.IsActive = false;
        _f.Executors.Update(offAi, null);
        // команда назначена проекту (defaultTeamId) — события должны попасть в его историю
        var project = _f.Projects.Create("Кино", null, team.Id, null);

        var work = new TeamWorkService(_f.Teams, _f.Executors, _f.Projects, _f.Events, _f.Connectors,
            new LocalModelProcessService());
        work.Start(team.Id, human.Id, human.Id);

        // сам запуск команды — в истории проекта
        var started = Assert.Single(_f.Events.Query(projectId: project.Id,
            eventType: EventTypes.TeamWorkStarted));
        Assert.Equal(team.Id, started.EntityId);

        // по каждому ИИ-участнику есть запись: активный «подключается», выключенный «не активен»
        var memberEvents = _f.Events.Query(projectId: project.Id,
            eventType: EventTypes.TeamMemberState);
        Assert.Contains(memberEvents, e => e.EntityId == ai.Id && e.PayloadJson.Contains("Connecting"));
        Assert.Contains(memberEvents, e => e.EntityId == offAi.Id && e.PayloadJson.Contains("Inactive"));
        // в записи есть адрес API и команда запуска локального сервера — для разбора ошибки
        Assert.All(memberEvents, e => Assert.Contains("launchCommand", e.PayloadJson));
    }
}

/// <summary>
/// Перенос задачи по иерархии (todo37): drag-n-drop представления «иерархия» —
/// TaskService.ChangeParent с защитой от цикла, смешения шаблонов с задачами и
/// переноса между проектами.
/// </summary>
public sealed class Todo37TaskMoveTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem NewTask(string title, string? projectId, string? parentId = null,
        bool isTemplate = false) =>
        _f.Tasks.Create(new TaskItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Title = title,
            IsTemplate = isTemplate,
        }, "", "", null);

    [Fact]
    public void Move_Makes_Task_A_Child_And_Logs_Event()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = NewTask("Родитель", project.Id);
        var task = NewTask("Задача", project.Id);

        var moved = _f.Tasks.ChangeParent(task.Id, parent.Id, null);

        Assert.Equal(parent.Id, moved.ParentId);
        Assert.Equal(parent.Id, _f.Tasks.Get(task.Id)!.ParentId);
        var evt = Assert.Single(_f.Events.Query(eventType: EventTypes.TaskMoved));
        Assert.Contains(parent.DisplayId, evt.PayloadJson);
        Assert.Equal(project.Id, evt.ProjectId);
    }

    [Fact]
    public void Move_To_Root_Clears_Parent()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = NewTask("Родитель", project.Id);
        var task = NewTask("Задача", project.Id, parent.Id);

        var moved = _f.Tasks.ChangeParent(task.Id, null, null);

        Assert.Null(moved.ParentId);
        Assert.Null(_f.Tasks.Get(task.Id)!.ParentId);
    }

    [Fact]
    public void Move_Into_Own_Subtree_Is_Rejected()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var root = NewTask("Корень", project.Id);
        var child = NewTask("Дитя", project.Id, root.Id);
        var grandChild = NewTask("Внук", project.Id, child.Id);

        Assert.Throws<ArgumentException>(() => _f.Tasks.ChangeParent(root.Id, grandChild.Id, null));
        Assert.Throws<ArgumentException>(() => _f.Tasks.ChangeParent(root.Id, root.Id, null));
        Assert.Null(_f.Tasks.Get(root.Id)!.ParentId);
    }

    [Fact]
    public void Template_And_Task_Are_Not_Mixed()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var template = NewTask("Шаблон", project.Id, isTemplate: true);
        var task = NewTask("Задача", project.Id);

        Assert.Throws<ArgumentException>(() => _f.Tasks.ChangeParent(task.Id, template.Id, null));
        Assert.Throws<ArgumentException>(() => _f.Tasks.ChangeParent(template.Id, task.Id, null));
    }

    [Fact]
    public void Move_Between_Projects_Is_Rejected()
    {
        var one = _f.Projects.Create("Первый", null, null, null);
        var two = _f.Projects.Create("Второй", null, null, null);
        var parent = NewTask("Родитель", one.Id);
        var task = NewTask("Задача", two.Id);

        Assert.Throws<ArgumentException>(() => _f.Tasks.ChangeParent(task.Id, parent.Id, null));
    }

    [Fact]
    public void Move_To_Same_Parent_Changes_Nothing()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = NewTask("Родитель", project.Id);
        var task = NewTask("Задача", project.Id, parent.Id);

        _f.Tasks.ChangeParent(task.Id, parent.Id, null);

        Assert.Equal(parent.Id, _f.Tasks.Get(task.Id)!.ParentId);
        Assert.Empty(_f.Events.Query(eventType: EventTypes.TaskMoved));
    }
}

/// <summary>
/// Каталог хранилища проекта именуется внешним кодом (todo37): новые проекты сразу,
/// старые — разовой миграцией при старте (каталог переносится, пути в БД правятся).
/// </summary>
public sealed class Todo37ProjectStorageTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void New_project_dir_is_named_by_display_id()
    {
        var project = _f.Projects.Create("Проект Кино", null, null, null);
        Assert.Equal(project.DisplayId, project.Slug);
        Assert.StartsWith("PRJ-", project.Slug);
        Assert.True(Directory.Exists(_f.Files.Abs($"projects/{project.DisplayId}")));
    }

    [Fact]
    public void Migration_moves_old_named_dirs_and_fixes_paths()
    {
        var project = _f.Projects.Create("Кино", null, null, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Ролик" },
            "описание", "критерии", null);

        // имитируем состояние старой установки: каталог и пути названы слагом имени
        MoveToLegacySlug(project.Id, project.DisplayId, "kino");
        Assert.True(Directory.Exists(_f.Files.Abs("projects/kino")));
        Assert.StartsWith("projects/kino/", _f.Tasks.Get(task.Id)!.DescriptionPath);

        _f.Projects.MigrateSlugsToDisplayId();

        var migrated = _f.Projects.Get(project.Id)!;
        Assert.Equal(project.DisplayId, migrated.Slug);
        Assert.True(Directory.Exists(_f.Files.Abs($"projects/{project.DisplayId}")));
        Assert.False(Directory.Exists(_f.Files.Abs("projects/kino")));

        var reloaded = _f.Tasks.Get(task.Id)!;
        Assert.StartsWith($"projects/{project.DisplayId}/", reloaded.DescriptionPath);
        Assert.Equal("описание", _f.Tasks.ReadDescription(reloaded));
        Assert.StartsWith($"projects/{project.DisplayId}/", reloaded.AcceptancePath);
        Assert.Equal("критерии", _f.Tasks.ReadAcceptance(reloaded));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ProjectRenamed));

        // повторный прогон ничего не делает (миграция идемпотентна)
        _f.Projects.MigrateSlugsToDisplayId();
        Assert.Single(_f.Events.Query(eventType: EventTypes.ProjectRenamed));
    }

    /// <summary>Откатить проект к «старому» имени каталога — как на установке до todo37.</summary>
    private void MoveToLegacySlug(string projectId, string displayId, string legacySlug)
    {
        Directory.Move(_f.Files.Abs($"projects/{displayId}"), _f.Files.Abs($"projects/{legacySlug}"));
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE projects SET slug=@slug WHERE id=@id",
            ("@slug", legacySlug), ("@id", projectId));
        Sql.Exec(conn, null, """
            UPDATE tasks SET
              description_path = replace(description_path, @old, @new),
              acceptance_path  = replace(acceptance_path,  @old, @new)
            """,
            ("@old", $"projects/{displayId}/"), ("@new", $"projects/{legacySlug}/"));
    }
}
