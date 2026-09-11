using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-1-S0: ЗАДАЧ БЕЗ СЕРВЕРА НЕ БЫВАЕТ.
///
/// «Без сервера» означало не «ничей», а «у того, кто СЕГОДНЯ дирижёр» (ТЗ гл. 6: строку без
/// сервера правит дирижёр). Пока дирижёр один и тот же, разницы не видно; но стоит передать
/// дирижёрство другому компьютеру — и все такие задачи разом становятся чужими там, где с ними
/// работали: правка запрещена, задания не запускаются, чинится только возвратом дирижёрства.
///
/// Отсюда правило: сервер-владелец у новой задачи проставляется ВСЕГДА — тем сервером, на
/// котором её создают, в том числе дирижёром. Номера при этом не поехали: суффикс кода
/// (<c>T-18-S1</c>) считается по РОЛИ сервера, а не по тому, пусто ли поле, поэтому у дирижёра
/// номера остались без суффикса, а одиночная установка выглядит ровно как раньше.
///
/// Уже накопленные бесхозные задачи забирает себе дирижёр — сторожем при открытии организации
/// (<see cref="TaskService.ClaimTasksWithoutServer"/>), а не разовым шагом обновления: такие
/// строки приезжают и после обновления — от партнёра прежней версии.
/// </summary>
public sealed class T1S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    /// <summary>Кто сегодня дирижёр: величина меняется по ходу проверки — ровно это и делает
    /// человек кнопкой «сменить дирижёра» (T-21-S1).</summary>
    private string _conductor = ConductorId;

    private ServerScope ScopeOf(string serverId, string code) => new(
        () => serverId, () => code, () => _conductor == serverId,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private ServerScope S0 => ScopeOf(ConductorId, "S0");

    private ServerScope S1 => ScopeOf(OtherId, "S1");

    private TaskService Tasks(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    /// <summary>Задача БЕЗ СЕРВЕРА, как её заводили версии до T-1-S0 и как её присылает
    /// партнёр прежней версии.</summary>
    private void Forget(string taskId)
    {
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE tasks SET server_id=NULL WHERE id=@id", ("@id", taskId));
    }

    private string? ServerOf(string taskId)
    {
        using var conn = _f.Db.Open();
        return Sql.Scalar<string>(conn, null, "SELECT server_id FROM tasks WHERE id=@id",
            ("@id", taskId));
    }

    public void Dispose() => _f.Dispose();

    // ---------- 1. Сервер проставляется всегда ----------

    [Fact]
    public void A_Task_Created_On_The_Conductor_Gets_Its_Server()
    {
        var task = Tasks(S0).Create(new TaskItem { Title = "на дирижёре" }, "", "", null);

        Assert.Equal(ConductorId, task.ServerId);
        Assert.Equal("S0", task.ServerCode);
        Assert.False(task.IsReadOnly);
    }

    [Fact]
    public void The_Number_Of_A_Conductor_Task_Stays_Without_A_Suffix()
    {
        // одиночная установка обязана выглядеть как раньше: T-1, а не T-1-S0 (T-18)
        var first = Tasks(S0).Create(new TaskItem { Title = "первая" }, "", "", null);
        var second = Tasks(S0).Create(new TaskItem { Title = "вторая" }, "", "", null);
        var theirs = Tasks(S1).Create(new TaskItem { Title = "у второго" }, "", "", null);

        Assert.Equal("T-1", first.DisplayId);
        Assert.Equal("T-2", second.DisplayId);
        Assert.Equal("T-1-S1", theirs.DisplayId);
    }

    [Fact]
    public void A_Subtask_And_A_Task_From_A_Template_Get_The_Server_Too()
    {
        var tasks = Tasks(S0);
        var parent = tasks.Create(new TaskItem { Title = "родитель" }, "", "", null);
        var child = tasks.Create(new TaskItem { Title = "потомок", ParentId = parent.Id }, "", "", null);
        var template = tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);

        var copy = tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Equal(ConductorId, child.ServerId);
        Assert.Equal(ConductorId, copy.ServerId);
        // с T-36-S0 сервер есть и у самого шаблона: он тоже принадлежит компьютеру,
        // а не роли дирижёра, и передаётся кнопкой «сменить сервер»
        Assert.Equal(ConductorId, template.ServerId);
    }

    // ---------- 2. Смена дирижёра больше не уводит задачи ----------

    [Fact]
    public void A_Conductor_Change_Leaves_Tasks_Where_They_Were()
    {
        var task = Tasks(S0).Create(new TaskItem { Title = "работаем здесь" }, "", "", null);

        // дирижёрство передали второму серверу (T-21-S1)
        _conductor = OtherId;

        // задача осталась у своего компьютера: здесь правится, там — только чтение
        Assert.False(Tasks(S0).Get(task.Id)!.IsReadOnly);
        Assert.True(Tasks(S1).Get(task.Id)!.IsReadOnly);
        Tasks(S0).ChangeStatus(task.Id, TaskStatuses.InProgress, null);
        Assert.Throws<ArgumentException>(() =>
            Tasks(S1).ChangeStatus(task.Id, TaskStatuses.Done, null));
    }

    [Fact]
    public void As_It_Was_A_Task_Without_A_Server_Moves_To_The_New_Conductor()
    {
        // ДОКАЗАТЕЛЬСТВО ДЕФЕКТА: строка прежних версий (сервер не указан) после смены
        // дирижёра перестаёт правиться там, где с ней работали, — ровно жалоба заказчика
        var task = Tasks(S0).Create(new TaskItem { Title = "старая" }, "", "", null);
        Forget(task.Id);
        Assert.False(Tasks(S0).Get(task.Id)!.IsReadOnly);

        _conductor = OtherId;

        Assert.True(Tasks(S0).Get(task.Id)!.IsReadOnly);
        var error = Assert.Throws<ArgumentException>(() =>
            Tasks(S0).ChangeStatus(task.Id, TaskStatuses.InProgress, null));
        Assert.Contains("дирижёре", error.Message);
    }

    // ---------- 3. Сторож: бесхозные задачи забирает дирижёр ----------

    [Fact]
    public void The_Conductor_Claims_Tasks_Without_A_Server()
    {
        var tasks = Tasks(S0);
        var mine = tasks.Create(new TaskItem { Title = "старая" }, "", "", null);
        var second = tasks.Create(new TaskItem { Title = "и эта" }, "", "", null);
        var theirs = Tasks(S1).Create(new TaskItem { Title = "чужая" }, "", "", null);
        var template = tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        Forget(mine.Id);
        Forget(second.Id);
        // шаблоны прежних версий сервера не имели вовсе — с T-36-S0 их забирает тот же сторож
        Forget(template.Id);

        var claimed = tasks.ClaimTasksWithoutServer();

        Assert.Equal(3, claimed);
        Assert.Equal(ConductorId, ServerOf(mine.Id));
        Assert.Equal(ConductorId, ServerOf(second.Id));
        Assert.Equal(ConductorId, ServerOf(template.Id));
        // номер — идентификатор и имя каталога файлов: он не меняется никогда
        Assert.Equal(mine.DisplayId, tasks.Get(mine.Id)!.DisplayId);
        // чужая задача не тронута
        Assert.Equal(OtherId, ServerOf(theirs.Id));
        // повторный проход находить нечего — сторож зовётся на каждом старте
        Assert.Equal(0, tasks.ClaimTasksWithoutServer());
    }

    [Fact]
    public void An_Ordinary_Server_Claims_Nothing()
    {
        var task = Tasks(S0).Create(new TaskItem { Title = "общая" }, "", "", null);
        Forget(task.Id);

        // рядовой сервер бесхозные строки писать не вправе (ТЗ гл. 6) — и не пишет
        Assert.Equal(0, Tasks(S1).ClaimTasksWithoutServer());
        Assert.Null(ServerOf(task.Id));

        // а новый дирижёр — забирает: до части строк прежний мог и не добраться
        _conductor = OtherId;
        Assert.Equal(1, Tasks(S1).ClaimTasksWithoutServer());
        Assert.Equal(OtherId, ServerOf(task.Id));
    }

    [Fact]
    public void Deleted_Tasks_Are_Left_Alone()
    {
        var tasks = Tasks(S0);
        var task = tasks.Create(new TaskItem { Title = "удалённая" }, "", "", null);
        tasks.Delete(task.Id, null);
        Forget(task.Id);

        Assert.Equal(0, tasks.ClaimTasksWithoutServer());
        Assert.Null(ServerOf(task.Id));
    }

    // ---------- 4. «Без сервера» больше не предлагается ----------

    [Fact]
    public void A_Task_Cannot_Be_Left_Without_A_Server()
    {
        var tasks = Tasks(S0);
        var task = tasks.Create(new TaskItem { Title = "задача" }, "", "", null);

        Assert.Throws<ArgumentException>(() => tasks.ChangeServer(task.Id, null, null));
        Assert.Throws<ArgumentException>(() => tasks.ChangeServer(task.Id, "", null));
        // сервер у задачи остался прежним: отказ ничего не переписал
        Assert.Equal(ConductorId, ServerOf(task.Id));
    }

    [Fact]
    public void A_Legacy_Task_Is_Given_A_Server_By_Hand_Too()
    {
        // кнопка «сменить сервер» — второй способ вылечить строку прежней версии,
        // когда дирижёрство уже переехало и сторож до неё не дошёл
        var task = Tasks(S0).Create(new TaskItem { Title = "старая" }, "", "", null);
        Forget(task.Id);

        var moved = Tasks(S0).ChangeServer(task.Id, ConductorId, null);

        Assert.Equal(ConductorId, moved.ServerId);
        Assert.Equal("S0", moved.ServerCode);
        Assert.False(moved.IsReadOnly);
    }
}
