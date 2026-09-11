using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-42-S0: ДВИЖОК ПЕРЕНОСА ДАННЫХ В АРХИВ И ВОССТАНОВЛЕНИЯ ОБРАТНО (выпуск 1.105).
///
/// 1. Задача переносится ЦЕЛИКОМ со всеми потомками и сопутствующими данными (чат, задания,
///    тэги, опыт узла) — и в рабочей среде её не остаётся.
/// 2. Один потомок в неподходящем состоянии отменяет перенос ВСЕЙ иерархии, и отказ — внятный
///    текст из словаря, а не исключение с именем колонки.
/// 3. Шаблон, на который смотрит БУДУЩЕЕ расписание, не архивируется.
/// 4. Файл переносится, если ссылка на него единственная, и копируется, если на него
///    ссылается кто-то ещё, — общее правило задания про файлы.
/// 5. Локальные ссылки внутри архива указывают на архив (пометка arc=&lt;код&gt;), а при
///    восстановлении пометка снимается.
/// 6. Восстановление возвращает тот же состав обратно в рабочую среду.
/// 7. Объект, запись опыта и проект целиком переносятся своими видами переноса.
/// </summary>
public sealed class T42S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveService _archives = null!;

    /// <summary>Текущий архив, распакованный на этом сервере: без него переносить некуда.</summary>
    private ArchiveTransferService Service()
    {
        _archives = new ArchiveService(_f.Db, _f.Events);
        if (_archives.Current() is null)
        {
            _archives.Create("arc42", "Архив 2026", ArchiveRuleModes.Empty, null);
        }
        return new ArchiveTransferService(_f.Db, _archives, _f.Events);
    }

    private string ArcDir => _archives.DirOf("arc42");

    /// <summary>Соединение с базой архива — читаем её напрямую: сервисы работают с рабочей.</summary>
    private Database ArcDb() => new(ArcDir, Archives.DbFile);

    private static long Count(Database db, string table, string where, params (string, object?)[] args)
    {
        using var conn = db.Open();
        return Sql.Scalar<long>(conn, null, $"SELECT COUNT(*) FROM {table} WHERE {where}", args);
    }

    private TaskItem Task(string title, string status, string? parentId = null,
        string? projectId = null, string description = "", bool template = false) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            Status = status,
            ParentId = parentId,
            ProjectId = projectId,
            IsTemplate = template,
        }, description, "", null);

    // --- 1. задача с потомками ---

    [Fact]
    public void A_Task_Moves_To_The_Archive_With_All_Its_Children_And_Company()
    {
        var service = Service();
        var root = Task("Корневая", TaskStatuses.Done);
        var child = Task("Потомок", TaskStatuses.Cancelled, root.Id);
        var grand = Task("Внук", TaskStatuses.Review, child.Id);
        var author = _f.Executors.Create(new Executor
        {
            Nick = "arc42",
            InternalName = "Автор сообщения",
            Kind = ExecutorKind.Human,
        }, null);
        _f.Chat.Add(root.Id, author.Id, null, "разговор по задаче");

        var result = service.Move(ArchiveMoveTargets.Task, root.Id, null);

        Assert.Equal(3, result.Tasks.Count);
        Assert.Contains(grand.Id, result.Tasks);
        Assert.Equal("arc42", result.ArchiveCode);
        // в рабочей среде не осталось НИЧЕГО из ветки
        Assert.Null(_f.Tasks.Get(root.Id));
        Assert.Null(_f.Tasks.Get(grand.Id));
        // а в архиве лежит вся ветка вместе с чатом
        var arc = ArcDb();
        Assert.Equal(3, Count(arc, "tasks", "id IN (@a,@b,@c)",
            ("@a", root.Id), ("@b", child.Id), ("@c", grand.Id)));
        Assert.Equal(1, Count(arc, "chat_messages", "task_id=@t", ("@t", root.Id)));
        // справочники уехали вместе с данными: иначе архивную задачу не открыть
        Assert.True(Count(arc, "task_statuses", "1=1") > 0);
    }

    [Fact]
    public void Only_A_Top_Level_Task_Can_Be_Archived()
    {
        var service = Service();
        var root = Task("Корневая", TaskStatuses.Done);
        var child = Task("Потомок", TaskStatuses.Done, root.Id);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => service.Move(ArchiveMoveTargets.Task, child.Id, null));
        Assert.Contains(child.DisplayId, refusal.Message);
        // отказ — из словаря, а не текст исключения SQLite
        Assert.Equal(Loc.T("msg.arcmove.5", child.DisplayId), refusal.Message);
        Assert.NotNull(_f.Tasks.Get(child.Id));
    }

    // --- 2. неподходящее состояние потомка ---

    [Fact]
    public void A_Child_In_An_Unsuitable_Status_Cancels_The_Whole_Hierarchy()
    {
        var service = Service();
        var root = Task("Корневая", TaskStatuses.Done);
        var child = Task("Потомок в работе", TaskStatuses.InProgress, root.Id);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => service.Move(ArchiveMoveTargets.Task, root.Id, null));
        Assert.Equal(Loc.T("msg.arcmove.6", child.DisplayId, TaskStatuses.InProgress),
            refusal.Message);
        // ничего не уехало: перенос отменён целиком, а не наполовину
        Assert.NotNull(_f.Tasks.Get(root.Id));
        Assert.Equal(0, Count(ArcDb(), "tasks", "id=@id", ("@id", root.Id)));
    }

    [Fact]
    public void Four_Statuses_Are_Archivable_And_The_Rest_Are_Not()
    {
        Assert.True(ArchiveMoveTargets.CanArchive(TaskStatuses.Draft));
        Assert.True(ArchiveMoveTargets.CanArchive(TaskStatuses.Review));
        Assert.True(ArchiveMoveTargets.CanArchive(TaskStatuses.Done));
        Assert.True(ArchiveMoveTargets.CanArchive(TaskStatuses.Cancelled));
        Assert.False(ArchiveMoveTargets.CanArchive(TaskStatuses.Pending));
        Assert.False(ArchiveMoveTargets.CanArchive(TaskStatuses.InProgress));
        Assert.False(ArchiveMoveTargets.CanArchive(TaskStatuses.Paused));
        Assert.False(ArchiveMoveTargets.CanArchive(TaskStatuses.NeedsFix));
        Assert.False(ArchiveMoveTargets.CanArchive(TaskStatuses.Error));
    }

    // --- 3. шаблон и будущее расписание ---

    [Fact]
    public void A_Template_Referenced_By_A_Future_Schedule_Is_Refused()
    {
        var service = Service();
        var node = Task("Узел шаблона", TaskStatuses.Draft, template: true);
        var schedule = _f.Schedules.Create(new Schedule
        {
            TemplateTaskId = node.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddDays(30),
            IsActive = true,
        }, null);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => service.Move(ArchiveMoveTargets.Template, node.Id, null));
        Assert.Equal(Loc.T("msg.arcmove.7", schedule.DisplayId), refusal.Message);

        // ПРОШЕДШЕЕ разовое расписание помехой не является: заводить задачи по нему больше
        // не будут, и держать ради него шаблон в работе незачем
        schedule.StartAt = DateTime.UtcNow.AddDays(-30);
        _f.Schedules.Update(schedule, null);
        var result = service.Move(ArchiveMoveTargets.Template, node.Id, null);
        Assert.Single(result.Tasks);
        Assert.Null(_f.Tasks.Get(node.Id));
    }

    // --- 4. единственная и не единственная ссылка на файл ---

    [Fact]
    public void A_File_Moves_When_Its_Only_Reference_Leaves_And_Is_Copied_Otherwise()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект архивации", null, null, null);
        var lonely = _f.Files.SaveUpload(project.Slug, "one.png", [1, 2, 3]);
        var shared = _f.Files.SaveUpload(project.Slug, "two.png", [4, 5, 6]);

        var going = Task("Уезжает", TaskStatuses.Done, null, project.Id,
            $"![один](api/files/raw?path={Uri.EscapeDataString(lonely)}) "
            + $"![два](api/files/raw?path={Uri.EscapeDataString(shared)})");
        // вторая картинка нужна ещё и остающейся задаче — значит она обязана остаться
        Task("Остаётся", TaskStatuses.InProgress, null, project.Id,
            $"![два](api/files/raw?path={Uri.EscapeDataString(shared)})");

        var result = service.Move(ArchiveMoveTargets.Task, going.Id, null);

        Assert.False(File.Exists(Path.Combine(_f.Db.DataDir, lonely)));
        Assert.True(File.Exists(Path.Combine(ArcDir, lonely)));
        // ссылка НЕ единственная — файл скопирован, а в рабочей среде остался
        Assert.True(File.Exists(Path.Combine(_f.Db.DataDir, shared)));
        Assert.True(File.Exists(Path.Combine(ArcDir, shared)));
        Assert.True(result.FilesMoved >= 1);
        Assert.True(result.FilesCopied >= 1);
    }

    // --- 5. переписывание ссылок ---

    [Fact]
    public void Local_Links_Inside_The_Archive_Point_At_The_Archive()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект ссылок", null, null, null);
        var upload = _f.Files.SaveUpload(project.Slug, "pic.png", [7]);
        var other = Task("Соседняя", TaskStatuses.Done, null, project.Id);
        var task = Task("Со ссылками", TaskStatuses.Done, null, project.Id,
            $"[файл](api/files/raw?path={Uri.EscapeDataString(upload)}) "
            + $"и [задача](http://localhost:5480/ai2p/org/task/{other.Id}) "
            + "и [наружу](https://example.com/doc)");

        service.Move(ArchiveMoveTargets.Task, task.Id, null);

        var text = File.ReadAllText(Path.Combine(ArcDir,
            _f.Files.TaskDirRel(project.Slug, task.DisplayId), "description.md"));
        Assert.Contains(ArchiveLinks.Param + "=arc42", text);
        Assert.Contains($"/task/{other.Id}?arc=arc42", text);
        // внешняя ссылка не наша — её не трогаем вовсе
        Assert.Contains("https://example.com/doc", text);
        Assert.DoesNotContain("example.com/doc?arc", text);
    }

    [Fact]
    public void The_Archive_Mark_Is_Added_Once_And_Removed_Back()
    {
        const string url = "api/files/project?projectId=P1&path=a.md";
        var marked = ArchiveLinks.With(url, "arc42");
        Assert.Equal(url + "&arc=arc42", marked);
        Assert.Equal("arc42", ArchiveLinks.CodeOf(marked));
        // повторная пометка не даёт двух параметров подряд
        Assert.Equal(url + "&arc=b", ArchiveLinks.With(marked, "b"));
        Assert.Equal(url, ArchiveLinks.With(marked, null));
        // ссылка без запроса получает «?», а не «&»
        Assert.Equal("/task/T1?arc=x", ArchiveLinks.With("/task/T1", "x"));
        Assert.Equal("/task/T1", ArchiveLinks.With("/task/T1?arc=x", ""));
        Assert.False(ArchiveLinks.IsOurs("https://example.com/a"));
        Assert.True(ArchiveLinks.IsOurs("/task/T1"));
    }

    // --- 6. восстановление ---

    [Fact]
    public void Restore_Brings_The_Same_Set_Back_To_The_Working_Environment()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект возврата", null, null, null);
        var upload = _f.Files.SaveUpload(project.Slug, "back.png", [9]);
        var root = Task("Корневая", TaskStatuses.Done, null, project.Id,
            $"![кадр](api/files/raw?path={Uri.EscapeDataString(upload)})");
        var child = Task("Потомок", TaskStatuses.Done, root.Id, project.Id);

        service.Move(ArchiveMoveTargets.Task, root.Id, null);
        Assert.Null(_f.Tasks.Get(root.Id));
        Assert.False(File.Exists(Path.Combine(_f.Db.DataDir, upload)));

        var back = service.Restore(ArchiveMoveTargets.Task, root.Id, null);

        Assert.Equal(2, back.Tasks.Count);
        Assert.NotNull(_f.Tasks.Get(root.Id));
        Assert.NotNull(_f.Tasks.Get(child.Id));
        Assert.True(File.Exists(Path.Combine(_f.Db.DataDir, upload)));
        Assert.Equal(0, Count(ArcDb(), "tasks", "id=@id", ("@id", root.Id)));
        // ссылка вернулась к рабочему виду — пометки архива в ней больше нет
        var text = _f.Tasks.ReadDescription(_f.Tasks.Get(root.Id)!);
        Assert.DoesNotContain("arc=arc42", text);
    }

    // --- 7. объект, опыт и проект целиком ---

    [Fact]
    public void An_Object_Moves_With_Its_Children()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект объектов", null, null, null);
        var hero = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "Герой",
            Type = "character",
        }, null);
        var frame = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            ParentId = hero.Id,
            Name = "Эталонный кадр",
            Type = "file",
        }, null);

        var result = service.Move(ArchiveMoveTargets.Object, hero.Id, null);

        Assert.Equal(2, result.Objects.Count);
        Assert.Null(_f.Objects.Get(hero.Id));
        Assert.Null(_f.Objects.Get(frame.Id));
        Assert.Equal(2, Count(ArcDb(), "objects", "id IN (@a,@b)",
            ("@a", hero.Id), ("@b", frame.Id)));
    }

    [Fact]
    public void An_Experience_Record_Moves_With_Everything_Attached()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект опыта", null, null, null);
        var record = _f.Experience.CreateForProject(project.Id, "Урок про архивацию", null);

        var result = service.Move(ArchiveMoveTargets.Experience, record.Id, null);

        Assert.Single(result.Experience);
        Assert.Null(_f.Experience.Get(record.Id));
        Assert.Equal(1, Count(ArcDb(), "experience", "id=@id", ("@id", record.Id)));
    }

    [Fact]
    public void A_Whole_Project_Moves_With_Its_Tasks_Objects_Experience_And_Logs()
    {
        var service = Service();
        var project = _f.Projects.Create("Проект целиком", null, null, null);
        var task = Task("Задача проекта", TaskStatuses.Done, null, project.Id);
        var obj = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "Локация",
            Type = "location",
        }, null);
        var lesson = _f.Experience.CreateForProject(project.Id, "Урок проекта", null);

        var result = service.Move(ArchiveMoveTargets.Project, project.Id, null);

        Assert.Single(result.Projects);
        Assert.Contains(task.Id, result.Tasks);
        Assert.Contains(obj.Id, result.Objects);
        Assert.Contains(lesson.Id, result.Experience);
        Assert.Null(_f.Projects.Get(project.Id));
        Assert.Null(_f.Tasks.Get(task.Id));
        var arc = ArcDb();
        Assert.Equal(1, Count(arc, "projects", "id=@id", ("@id", project.Id)));
        Assert.Equal(1, Count(arc, "objects", "id=@id", ("@id", obj.Id)));
        Assert.Equal(1, Count(arc, "experience", "id=@id", ("@id", lesson.Id)));
    }

    [Fact]
    public void A_Project_With_Unfinished_Work_Is_Refused()
    {
        var service = Service();
        var project = _f.Projects.Create("Занятый проект", null, null, null);
        var busy = Task("В работе", TaskStatuses.InProgress, null, project.Id);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => service.Move(ArchiveMoveTargets.Project, project.Id, null));
        Assert.Equal(Loc.T("msg.arcmove.6", busy.DisplayId, TaskStatuses.InProgress),
            refusal.Message);
        Assert.NotNull(_f.Projects.Get(project.Id));
    }

    // --- общие проверки движка ---

    [Fact]
    public void Without_A_Current_Archive_The_Transfer_Refuses_By_Words()
    {
        var archives = new ArchiveService(_f.Db, _f.Events);
        var service = new ArchiveTransferService(_f.Db, archives, _f.Events);
        var task = Task("Задача", TaskStatuses.Done);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => service.Move(ArchiveMoveTargets.Task, task.Id, null));
        Assert.Equal(Loc.T("msg.arcmove.1"), refusal.Message);
    }

    [Fact]
    public void An_Unknown_Kind_Of_Transfer_Is_Refused_By_Words()
    {
        var service = Service();
        var refusal = Assert.Throws<ArgumentException>(
            () => service.Move("неведомый-вид", "id", null));
        Assert.Contains(Loc.T("msg.arcmove.3", "неведомый-вид"), refusal.Message);
        Assert.Equal(5, ArchiveMoveTargets.All.Length);
    }

    [Fact]
    public void Preview_Tells_What_Would_Leave_Without_Moving_Anything()
    {
        var service = Service();
        var root = Task("Корневая", TaskStatuses.Done);
        Task("Потомок", TaskStatuses.Done, root.Id);

        var preview = service.Preview(ArchiveMoveTargets.Task, root.Id);

        Assert.Equal(2, preview.Tasks.Count);
        Assert.Equal(0, preview.Rows);
        Assert.NotNull(_f.Tasks.Get(root.Id));
    }

    [Fact]
    public void Both_Dictionaries_Know_Every_Text_Of_The_Transfer()
    {
        foreach (var key in new[]
                 {
                     "msg.arcmove.1", "msg.arcmove.2", "msg.arcmove.3", "msg.arcmove.4",
                     "msg.arcmove.5", "msg.arcmove.6", "msg.arcmove.7", "msg.arcmove.8",
                     "msg.arcmove.9",
                 })
        {
            Assert.NotEqual(key, Loc.In("ru", key));
            Assert.NotEqual(key, Loc.In("en", key));
        }
    }
}
