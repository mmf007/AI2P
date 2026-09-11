using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo42 / этап 42 (ТЗ v1.49): РАЗДЕЛЬНОЕ ВЛАДЕНИЕ. У каждой строки ровно один
/// сервер-писатель: сервер указан — правит он, не указан — дирижёр; на остальных серверах
/// строка видна только для чтения. Отсюда номера с кодом сервера (<c>T-18-S1</c>),
/// пер-серверные каталоги проектов, серверные правила доступа к ФС, локальная часть
/// справочника моделей и кнопка «сменить сервер».
///
/// Оба сервера в тестах работают с ОДНОЙ базой: репликации ещё нет (этап 43), а проверять
/// нужно именно правила владения — как ведут себя два набора сервисов над общими данными.
/// </summary>
public sealed class Todo42Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    /// <summary>Внутренние ключи серверов: дирижёр (S0) и обычный участник (S1).</summary>
    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    /// <summary>Сервисы «глазами дирижёра»: код S0, правит и бесхозные строки.</summary>
    private ServerScope ConductorScope => new(
        () => ConductorId, () => "S0", () => true,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    /// <summary>Сервисы «глазами второго сервера»: код S1, дирижёром не является.</summary>
    private ServerScope OtherScope => new(
        () => OtherId, () => "S1", () => false,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private TaskService Tasks(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    /// <summary>Задача БЕЗ СЕРВЕРА, как её заводили версии до T-1-S0 (и как её присылает
    /// партнёр прежней версии): сегодня такие не создаются, а правила для них остались.</summary>
    private void Forget(string taskId)
    {
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE tasks SET server_id=NULL WHERE id=@id", ("@id", taskId));
    }

    private ScheduleService Schedules(ServerScope scope) => new(_f.Db, _f.Events, scope);

    private ProjectService Projects(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    private SecurityRuleService Security(ServerScope scope) => new(_f.Db, _f.Events, scope);

    private ExperienceService Experience(ServerScope scope) => new(_f.Db, _f.Events, scope);

    private ExecutorService Executors(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    public void Dispose() => _f.Dispose();

    // --- номера задач с кодом сервера (ТЗ гл. 6 «Номера задач») ---

    [Fact]
    public void Conductor_Numbers_Tasks_Without_A_Server_Suffix()
    {
        // одиночная установка должна выглядеть как раньше: T-1, а не T-1-S0
        var task = Tasks(ConductorScope).Create(new TaskItem { Title = "Задача" }, "", "", null);

        Assert.Equal("T-1", task.DisplayId);
        // сервер при этом проставлен (T-1-S0): суффикс номера считается по РОЛИ сервера,
        // а не по тому, пусто ли поле, — иначе смена дирижёра уводила бы задачу с компьютера
        Assert.Equal(ConductorId, task.ServerId);
        Assert.False(task.IsReadOnly);
    }

    [Fact]
    public void Other_Server_Numbers_Tasks_With_Its_Code()
    {
        var task = Tasks(OtherScope).Create(new TaskItem { Title = "Задача" }, "", "", null);

        Assert.Equal("T-1-S1", task.DisplayId);
        Assert.Equal(OtherId, task.ServerId);
        Assert.Equal("S1", task.ServerCode);
    }

    [Fact]
    public void Counters_Are_Per_Server_So_Numbers_Never_Collide()
    {
        // счётчик у каждого сервера свой: два сервера БЕЗ СВЯЗИ не должны создать две T-2
        var conductor = Tasks(ConductorScope);
        var other = Tasks(OtherScope);

        var first = conductor.Create(new TaskItem { Title = "первая" }, "", "", null);
        var second = conductor.Create(new TaskItem { Title = "вторая" }, "", "", null);
        var mine = other.Create(new TaskItem { Title = "своя" }, "", "", null);

        Assert.Equal("T-1", first.DisplayId);
        Assert.Equal("T-2", second.DisplayId);
        // счётчик второго сервера начался со своей единицы и коду не мешает
        Assert.Equal("T-1-S1", mine.DisplayId);
    }

    // --- режим «только чтение» для чужих строк (ТЗ гл. 6, разд. 3) ---

    [Fact]
    public void A_Task_Of_Another_Server_Is_Read_Only_Here()
    {
        var task = Tasks(OtherScope).Create(new TaskItem { Title = "чужая" }, "", "", null);

        var seenByConductor = Tasks(ConductorScope).Get(task.Id)!;

        Assert.True(seenByConductor.IsReadOnly);
        Assert.Equal("S1", seenByConductor.ServerCode);
        Assert.Equal("ноутбук", seenByConductor.ServerName);
    }

    [Fact]
    public void Editing_Deleting_And_Status_Of_A_Foreign_Task_Are_Refused()
    {
        var task = Tasks(OtherScope).Create(new TaskItem { Title = "чужая" }, "", "", null);
        var conductor = Tasks(ConductorScope);

        // текст ошибки называет сервер, на котором задача правится
        var edit = Assert.Throws<ArgumentException>(() =>
            conductor.Update(conductor.Get(task.Id)!, "", "", null));
        Assert.Contains("S1", edit.Message);
        Assert.Throws<ArgumentException>(() =>
            conductor.ChangeStatus(task.Id, TaskStatuses.InProgress, null));
        Assert.Throws<ArgumentException>(() => conductor.Delete(task.Id, null));
    }

    [Fact]
    public void A_Task_Without_A_Server_Is_Read_Only_Outside_The_Conductor()
    {
        // бесхозную строку правит только дирижёр — иначе её писали бы все сразу.
        // С T-1-S0 новые задачи такими не заводятся, поэтому строку прежних версий
        // (и приехавшую от партнёра прежней версии) изображаем правкой БД
        var task = Tasks(ConductorScope).Create(new TaskItem { Title = "общая" }, "", "", null);
        Forget(task.Id);
        var other = Tasks(OtherScope);

        Assert.True(other.Get(task.Id)!.IsReadOnly);
        var error = Assert.Throws<ArgumentException>(() =>
            other.ChangeStatus(task.Id, TaskStatuses.InProgress, null));
        Assert.Contains("дирижёре", error.Message);
    }

    [Fact]
    public void Templates_Belong_To_A_Server_Like_Tasks()
    {
        // T-36-S0: шаблон живёт по тому же правилу, что задача, — у него есть сервер-владелец,
        // и заводится он на том сервере, где его создают (прежде шаблоны заводились только
        // на дирижёре и сервера не имели вовсе)
        var theirs = Tasks(OtherScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        Assert.Equal(OtherId, theirs.ServerId);
        Assert.Equal("T-1-S1", theirs.DisplayId);

        var template = Tasks(ConductorScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        Assert.Equal(ConductorId, template.ServerId);
        Assert.Equal("T-1", template.DisplayId);

        // чужой шаблон здесь только для чтения
        Assert.True(Tasks(ConductorScope).Get(theirs.Id)!.IsReadOnly);
    }

    // --- смена сервера у задачи (ТЗ гл. 6, разд. 3.1) ---

    [Fact]
    public void Changing_The_Server_Keeps_The_Number_And_Moves_The_Write_Right()
    {
        var conductor = Tasks(ConductorScope);
        var task = conductor.Create(new TaskItem { Title = "переезжает" }, "", "", null);

        var moved = conductor.ChangeServer(task.Id, OtherId, null);

        // номер — идентификатор и имя каталога файлов: он не меняется никогда
        Assert.Equal(task.DisplayId, moved.DisplayId);
        Assert.Equal(OtherId, moved.ServerId);
        // на прежнем сервере задача стала только для чтения, на новом — редактируется
        Assert.True(conductor.Get(task.Id)!.IsReadOnly);
        Assert.False(Tasks(OtherScope).Get(task.Id)!.IsReadOnly);
    }

    [Fact]
    public void Only_The_Current_Owner_Changes_The_Server_Of_A_Task()
    {
        var task = Tasks(OtherScope).Create(new TaskItem { Title = "чужая" }, "", "", null);

        Assert.Throws<ArgumentException>(() =>
            Tasks(ConductorScope).ChangeServer(task.Id, ConductorId, null));
    }

    [Fact]
    public void Leaving_A_Task_Without_A_Server_Is_Refused_Now()
    {
        // T-1-S0: «без сервера» не предлагается никому, включая дирижёра, — такая задача
        // принадлежит не компьютеру, а РОЛИ, и при смене дирижёра уезжает вместе с ней
        var other = Tasks(OtherScope);
        var mine = other.Create(new TaskItem { Title = "своя" }, "", "", null);
        Assert.Throws<ArgumentException>(() => other.ChangeServer(mine.Id, null, null));

        var conductor = Tasks(ConductorScope);
        var his = conductor.Create(new TaskItem { Title = "дирижёрская" }, "", "", null);
        var error = Assert.Throws<ArgumentException>(() => conductor.ChangeServer(his.Id, "", null));
        Assert.Contains("сервер", error.Message);
        // отдать задачу дирижёру по-прежнему можно — НАЗВАВ его сервер
        Assert.Equal(ConductorId, other.ChangeServer(mine.Id, ConductorId, null).ServerId);
    }

    [Fact]
    public void Tasks_Of_A_Server_Left_The_Cluster_Are_Handed_Over_To_The_Conductor()
    {
        var other = Tasks(OtherScope);
        other.Create(new TaskItem { Title = "первая" }, "", "", null);
        other.Create(new TaskItem { Title = "вторая" }, "", "", null);
        var conductor = Tasks(ConductorScope);

        var moved = conductor.TakeOverTasksOf(OtherId, null);

        Assert.Equal(2, moved);
        Assert.All(conductor.List(), t => Assert.False(t.IsReadOnly));
        // это единственный случай, когда владельца меняет не сервер задачи, и делает это дирижёр
        Assert.Throws<ArgumentException>(() => other.TakeOverTasksOf(ConductorId, null));
    }

    [Fact]
    public async Task Everything_Around_A_Foreign_Task_Is_Read_Only_Too()
    {
        // «всё вокруг задачи» принадлежит тому же серверу: задания, чат, файлы (ТЗ гл. 6)
        var task = Tasks(OtherScope).Create(new TaskItem { Title = "чужая" }, "", "", null);
        var conductor = Tasks(ConductorScope);
        var orchestrator = new AI2P.Connectors.JobOrchestrator(conductor, _f.Jobs, _f.Executors,
            _f.Projects, _f.Teams, _f.Files, _f.Chat, _f.Connectors, _f.Security, _f.Picker,
            Experience(ConductorScope));

        // запуск задания на чужой задаче отклоняется: оно выполняется у владельца
        var start = await Assert.ThrowsAsync<ArgumentException>(() =>
            orchestrator.StartTaskAsync(task.Id, null));
        Assert.Contains("S1", start.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => orchestrator.StartSplitAsync(task.Id, null));
        // и файлы чужой задачи отсюда не удаляются
        Assert.Throws<ArgumentException>(() =>
            conductor.DeleteFile(conductor.Get(task.Id)!, "projects/x/y.md", null));
    }

    // --- иерархия между серверами меняется на дирижёре (ТЗ гл. 6, разд. 3.1) ---

    [Fact]
    public void Hierarchy_Across_Servers_Is_Changed_On_The_Conductor()
    {
        var conductor = Tasks(ConductorScope);
        var parent = conductor.Create(new TaskItem { Title = "родитель" }, "", "", null);
        var child = Tasks(OtherScope).Create(new TaskItem { Title = "чужой потомок" }, "", "", null);

        // дирижёр — единственный, кому разрешено писать не свои строки
        var moved = conductor.ChangeParent(child.Id, parent.Id, null);
        Assert.Equal(parent.Id, moved.ParentId);
    }

    [Fact]
    public void Hierarchy_Of_A_Foreign_Task_Is_Refused_Outside_The_Conductor()
    {
        var conductor = Tasks(ConductorScope);
        var parent = conductor.Create(new TaskItem { Title = "родитель" }, "", "", null);
        var foreign = conductor.Create(new TaskItem { Title = "общая" }, "", "", null);

        Assert.Throws<ArgumentException>(() =>
            Tasks(OtherScope).ChangeParent(foreign.Id, parent.Id, null));
    }

    // --- фильтр по серверам в списках (ТЗ гл. 6, разд. 4) ---

    [Fact]
    public void Task_List_Filters_By_Owning_Server()
    {
        // задача без сервера бывает только от прежних версий (T-1-S0) — фильтр «none»
        // оставлен ради них: иначе такие строки нечем было бы найти
        var legacy = Tasks(ConductorScope).Create(new TaskItem { Title = "без сервера" }, "", "", null);
        Forget(legacy.Id);
        Tasks(OtherScope).Create(new TaskItem { Title = "второго сервера" }, "", "", null);
        var conductor = Tasks(ConductorScope);

        Assert.Equal(2, conductor.List().Count);
        Assert.Equal("второго сервера", conductor.List(serverIds: [OtherId]).Single().Title);
        // «none» — задачи без сервера: их ведёт дирижёр
        Assert.Equal("без сервера", conductor.List(serverIds: [TaskService.NoServer]).Single().Title);
    }

    // --- расписания: сервер указывается и срабатывает только на нём (ТЗ гл. 6, разд. 4) ---

    [Fact]
    public void A_Schedule_Belongs_To_The_Server_It_Was_Created_On()
    {
        var template = Tasks(ConductorScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);

        var mine = Schedules(OtherScope).Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddHours(1),
        }, null);

        Assert.Equal(OtherId, mine.ServerId);
        Assert.Equal("SCH-1-S1", mine.DisplayId);
        // у дирижёра оно видно, но не правится: расписание ведёт свой сервер
        var seen = Schedules(ConductorScope).Get(mine.Id)!;
        Assert.True(seen.IsReadOnly);
        Assert.Throws<ArgumentException>(() => Schedules(ConductorScope).Delete(mine.Id, null));
    }

    [Fact]
    public void Only_Own_Schedules_Are_Due_On_This_Server()
    {
        var template = Tasks(ConductorScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        // расписание без сервера — его ведёт дирижёр
        var common = Schedules(ConductorScope).Create(new Schedule
        {
            TemplateTaskId = template.Id,
            Kind = "once",
            StartAt = DateTime.UtcNow.AddMinutes(30),
        }, null);
        // отсчёт срабатываний идёт от создания расписания — «состариваем» его правкой БД
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE schedules SET created_at=@c, start_at=@s WHERE id=@id",
                ("@c", Sql.ToDb(DateTime.UtcNow.AddHours(-2))),
                ("@s", Sql.ToDb(DateTime.UtcNow.AddHours(-1))),
                ("@id", common.Id));
        }

        var horizon = DateTime.UtcNow.AddMinutes(1);
        Assert.Contains(Schedules(ConductorScope).Due(horizon), d => d.Schedule.Id == common.Id);
        // на втором сервере то же расписание не срабатывает — иначе задача создалась бы дважды
        Assert.DoesNotContain(Schedules(OtherScope).Due(horizon), d => d.Schedule.Id == common.Id);
    }

    // --- пер-серверная часть проекта (ТЗ гл. 6, разд. 4.1) ---

    [Fact]
    public void Project_Folder_And_Activity_Are_Per_Server()
    {
        var conductor = Projects(ConductorScope);
        var project = conductor.Create("Проект", @"C:\work\prj", null, null);

        // каталог виден только у своего сервера, у второго его нет
        Assert.Equal(@"C:\work\prj", conductor.Get(project.Id)!.FolderPath);
        Assert.Equal("", Projects(OtherScope).Get(project.Id)!.FolderPath);

        var here = Projects(OtherScope).SaveLocalPart(project.Id, @"D:\prj", "out", true, null);
        Assert.Equal(@"D:\prj", here.FolderPath);
        Assert.Equal("out", here.CommonPath);
        // запись своего сервера чужую не трогает
        Assert.Equal(@"C:\work\prj", conductor.Get(project.Id)!.FolderPath);
    }

    [Fact]
    public void A_Project_With_A_Folder_Elsewhere_Cannot_Be_Activated_Without_One_Here()
    {
        var project = Projects(ConductorScope).Create("Проект", @"C:\work\prj", null, null);

        var error = Assert.Throws<ArgumentException>(() =>
            Projects(OtherScope).SaveLocalPart(project.Id, "", "", isActive: true, null));
        Assert.Contains("каталог", error.Message);

        // без активности запись создаётся: проект просто ещё не развёрнут на этом сервере
        Assert.False(Projects(OtherScope).SaveLocalPart(project.Id, "", "", isActive: false, null).IsActive);
    }

    [Fact]
    public void Old_Projects_Keep_Their_Folder_After_The_Move_To_Per_Server_Rows()
    {
        // до этапа 42 каталог лежал в настройках проекта — воспроизводим старую запись
        var conductor = Projects(ConductorScope);
        var project = conductor.Create("Старый", null, null, null);
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "DELETE FROM project_servers WHERE project_id=@p", ("@p", project.Id));
            Sql.Exec(conn, null, "UPDATE projects SET settings_json=@s WHERE id=@id",
                ("@s", """{"folderPath":"C:\\old\\prj","defaultTeamId":null,"qualityBias":0.5}"""),
                ("@id", project.Id));
        }
        Assert.Equal("", conductor.Get(project.Id)!.FolderPath);

        conductor.MigrateFolderPathsToServerParts();

        // каталог не потерялся: он переехал в пер-серверную строку этого сервера
        Assert.Equal(@"C:\old\prj", conductor.Get(project.Id)!.FolderPath);
        // повторный вызов ничего не ломает
        conductor.MigrateFolderPathsToServerParts();
        Assert.Single(conductor.ServerParts(project.Id));
    }

    [Fact]
    public void Project_Numbers_Get_The_Code_Of_The_Creating_Server()
    {
        Assert.Equal("PRJ-1", Projects(ConductorScope).Create("Первый", null, null, null).DisplayId);
        Assert.Equal("PRJ-1-S1", Projects(OtherScope).Create("Второй", null, null, null).DisplayId);
    }

    // --- правила доступа к файловой системе — свои на каждом сервере (ТЗ гл. 6, разд. 4.2) ---

    [Fact]
    public void Filesystem_Rules_Belong_To_Their_Server_And_Others_Are_Not_Applied()
    {
        var mine = Security(OtherScope).Create(new SecurityRule
        {
            Scope = SecurityScope.Global,
            Target = SecurityTarget.Directory,
            Permission = SecurityPermission.Allow,
            Pattern = @"D:\tmp\*",
            OpRead = true,
        }, null);
        Assert.Equal(OtherId, mine.ServerId);

        var conductor = Security(ConductorScope);
        // чужое правило видно в списке, но правке не подлежит
        Assert.True(conductor.List(SecurityScope.Global, null).Single(r => r.Id == mine.Id).IsReadOnly);
        Assert.Throws<ArgumentException>(() => conductor.Delete(mine.Id, null));

        // и в оценку доступа на этом сервере не входит: путь описывает чужой компьютер
        var task = Tasks(ConductorScope).Create(new TaskItem { Title = "задача" }, "", "", null);
        Assert.DoesNotContain(conductor.EffectiveForTask(task), r => r.Id == mine.Id);
        Assert.Contains(Security(OtherScope).EffectiveForTask(task), r => r.Id == mine.Id);
    }

    [Fact]
    public void Non_Filesystem_Rules_Stay_Common_And_Belong_To_The_Conductor()
    {
        var rule = Security(ConductorScope).Create(new SecurityRule
        {
            Scope = SecurityScope.Global,
            Target = SecurityTarget.Action,
            Permission = SecurityPermission.Allow,
            Pattern = "AI2P.Files.Read",
        }, null);

        Assert.Null(rule.ServerId);
        // остальные правила от сервера не зависят: на втором сервере они действуют так же
        var task = Tasks(ConductorScope).Create(new TaskItem { Title = "задача" }, "", "", null);
        Assert.Contains(Security(OtherScope).EffectiveForTask(task), r => r.Id == rule.Id);
        // но правит их дирижёр
        Assert.Throws<ArgumentException>(() => Security(OtherScope).Delete(rule.Id, null));
    }

    // --- опыт по узлам шаблонов: каждый сервер правит только свои записи (ТЗ гл. 6, разд. 4) ---

    [Fact]
    public void Experience_Records_Are_Edited_By_The_Server_That_Created_Them()
    {
        var template = Tasks(ConductorScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var mine = Experience(OtherScope).Create(template.Id, "урок второго сервера", null);

        Assert.Equal(OtherId, mine.ServerId);
        Assert.True(Experience(ConductorScope).Get(mine.Id)!.IsReadOnly);
        Assert.Throws<ArgumentException>(() => Experience(ConductorScope).Update(mine.Id, "правка", null));
        // свой сервер правит и удаляет свободно
        Assert.Equal("правка", Experience(OtherScope).Update(mine.Id, "правка", null).Text);
    }

    [Fact]
    public void Orphan_Experience_Records_Survive_The_Deletion_Of_Their_Template_Node()
    {
        var conductor = Tasks(ConductorScope);
        var template = conductor.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var record = Experience(OtherScope).Create(template.Id, "урок", null);

        // узел шаблона удалили на дирижёре — чужую запись он трогать не вправе
        conductor.Delete(template.Id, null);

        var orphans = Experience(OtherScope).ListOrphans();
        Assert.Contains(orphans, r => r.Id == record.Id && r.IsOrphan);
        // владелец может её убрать, хотя узла уже нет
        Experience(OtherScope).Delete(record.Id, null);
        Assert.Empty(Experience(OtherScope).ListOrphans());
    }

    // --- ИИ-исполнитель с локальной моделью привязан к серверу (ТЗ гл. 6, разд. 4) ---

    [Fact]
    public void An_Ai_Executor_With_A_Local_Model_Cannot_Take_A_Task_Of_Another_Server()
    {
        // модель локальная — значит принадлежит серверу, на котором стоит
        var model = _f.Models.Create(new AiModel { Name = "Локальная" }, null);
        _f.Files.WriteText(model.ProfilePath, """{"provider":"openai","launchCommand":"llama-server"}""");
        _f.Models.SyncLocalFlags();
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "локальный-ии",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);
        Assert.Equal("local", _f.Executors.Get(executor.Id)!.ServerId);

        // задача второго сервера с таким исполнителем не заводится: она бы не запустилась
        var error = Assert.Throws<ArgumentException>(() => Tasks(OtherScope).Create(new TaskItem
        {
            Title = "чужая задача",
            ExecutorIds = [executor.Id],
        }, "", "", null));
        Assert.Contains("модели сервера", error.Message);
    }

    /// <summary>
    /// T-220-S0: задача, ПЕРЕДАННАЯ на этот сервер, принимает его собственного исполнителя
    /// с локальной моделью. Форма задачи сервер не несёт (он меняется отдельной операцией),
    /// и до правки проверка «исполнитель на сервере задачи» видела пустое поле: на сервере,
    /// который не дирижёр, это читалось как «задача дирижёра», и свой же исполнитель
    /// объявлялся чужим — «работает на модели сервера S1, передайте задачу туда».
    /// </summary>
    [Fact]
    public void A_Task_Moved_Here_Accepts_Our_Own_Local_Executor()
    {
        var models = new AiModelService(_f.Db, _f.Events, _f.Files, OtherScope);
        var model = models.Create(new AiModel { Name = "Локальная S1" }, null);
        _f.Files.WriteText(model.ProfilePath, """{"provider":"openai","launchCommand":"llama-server"}""");
        models.SyncLocalFlags();
        var executor = Executors(ConductorScope).Create(new Executor
        {
            Nick = "Bill",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);
        Assert.Equal(OtherId, Executors(ConductorScope).Get(executor.Id)!.ServerId);

        var tasks = Tasks(OtherScope);
        var task = tasks.Create(new TaskItem { Title = "переданная задача" }, "", "", null);
        Assert.Equal(OtherId, task.ServerId);

        // форма сохраняет задачу целиком, но поля «сервер» в ней нет — приходит пустое
        var saved = tasks.Update(new TaskItem
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Status = task.Status,
            ExecutorIds = new List<string> { executor.Id },
        }, "", "", null);

        Assert.Contains(executor.Id, saved.ExecutorIds);
        Assert.Equal(OtherId, saved.ServerId);
    }

    [Fact]
    public void Executors_Are_Edited_On_The_Conductor_Only()
    {
        var conductor = Executors(ConductorScope);
        var executor = conductor.Create(new Executor { Nick = "человек" }, null);

        var error = Assert.Throws<ArgumentException>(() =>
            Executors(OtherScope).Create(new Executor { Nick = "второй" }, null));
        Assert.Contains("дирижёре", error.Message);
        Assert.Throws<ArgumentException>(() => Executors(OtherScope).Update(executor, null));

        // участник организации заводится где угодно: без исполнителя человек не может работать
        var member = Executors(OtherScope).CreateMember(new Executor { Nick = "участник" }, null);
        Assert.Equal("участник", member.Nick);
    }

    // --- локальная часть справочника моделей (ТЗ гл. 6, разд. 4) ---

    [Fact]
    public void The_Local_Part_Of_The_Model_Catalog_Belongs_To_Its_Server()
    {
        var models = new AiModelService(_f.Db, _f.Events, _f.Files, OtherScope);
        var model = models.Create(new AiModel { Name = "Локальная S1" }, null);
        _f.Files.WriteText(model.ProfilePath, """{"provider":"openai","launchCommand":"llama-server"}""");
        models.SyncLocalFlags();

        Assert.Equal(OtherId, models.Get(model.Id)!.ServerId);
        // ОБЩАЯ часть записи (название) у дирижёра только читается: запись завёл другой сервер
        var conductorModels = new AiModelService(_f.Db, _f.Events, _f.Files, ConductorScope);
        Assert.True(conductorModels.Get(model.Id)!.IsReadOnly);
        var rename = conductorModels.Get(model.Id)!;
        rename.Name = "Переименованная дирижёром";
        Assert.Throws<ArgumentException>(() => conductorModels.Update(rename, null));

        // а вот НАСТРОЙКА локальной модели пер-серверная (T-8-S1): она открывается на любом
        // сервере — там свои файлы, свои пути и своя активность. Сохранение без правки общей
        // части чужую запись не трогает и ошибкой больше не является
        Assert.True(conductorModels.Get(model.Id)!.CanConfigure);
        conductorModels.Update(conductorModels.Get(model.Id)!, null);
        Assert.Equal("Локальная S1", conductorModels.Get(model.Id)!.Name);
    }
}
