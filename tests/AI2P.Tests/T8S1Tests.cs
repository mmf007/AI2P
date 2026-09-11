using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-8-S1: ЛОКАЛЬНАЯ МОДЕЛЬ НАСТРАИВАЕТСЯ НА КАЖДОМ СЕРВЕРЕ ОТДЕЛЬНО.
///
/// Жалоба заказчика: на втором сервере (S1) в справочнике горят активными Qwen3.6 и
/// Kandinsky, хотя установлены они на S0; выключить их там нельзя — кнопка «изменить»
/// неактивна, запись принадлежит другому серверу. Причина — одна общая колонка активности
/// на всю организацию.
///
/// Здесь проверяется пять вещей:
/// <list type="number">
/// <item>активность локальной модели своя на каждом сервере (<c>ai_model_servers</c>);</item>
/// <item>форма настройки открывается на ЛЮБОМ сервере, а название записи по-прежнему
///   правит её владелец;</item>
/// <item>в справочнике видно, на каких серверах локальная модель включена;</item>
/// <item>прежняя общая активность переносится в свою строку один раз и идемпотентно;</item>
/// <item>исполнитель с локальной моделью активен только там, где включена его модель.</item>
/// </list>
/// </summary>
public sealed class T8S1Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t8s1-" + Guid.NewGuid().ToString("N"));

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;

    public T8S1Tests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "org"), "ai2p.db");
        _db.Init("node-a");
        _events = new EventStore(_db);
        _files = new FileStore(_db.DataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private static ServerScope Scope(string serverId, string code, bool conductor) =>
        new(() => serverId, () => code, () => conductor,
            id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
            id => id);

    /// <summary>Справочник глазами дирижёра S0.</summary>
    private AiModelService Conductor() =>
        new(_db, _events, _files, Scope(ConductorId, "S0", conductor: true));

    /// <summary>Тот же справочник глазами рядового сервера S1.</summary>
    private AiModelService Other() =>
        new(_db, _events, _files, Scope(OtherId, "S1", conductor: false));

    private ExecutorService ConductorExecutors() =>
        new(_db, _events, _files, Scope(ConductorId, "S0", conductor: true));

    /// <summary>Локальная модель, заведённая на дирижёре и включённая там.</summary>
    private AiModel LocalModelOnS0(string name = "Локальная")
    {
        var models = Conductor();
        var model = models.Create(new AiModel { Name = name }, null);
        _files.WriteText(model.ProfilePath, """{"launchCommand":"llama-server"}""");
        models.SyncLocalFlags();
        model = models.Get(model.Id)!;
        model.IsActive = true;
        return models.Update(model, null);
    }

    private List<string> Journal(string table)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT pk FROM changes WHERE tbl=@t ORDER BY seq",
            r => r.S("pk"), ("@t", table));
    }

    // --- 1. активность локальной модели — своя на каждом сервере ---

    [Fact]
    public void A_Local_Model_Enabled_On_One_Server_Is_Off_On_The_Other()
    {
        var model = LocalModelOnS0();

        Assert.True(Conductor().Get(model.Id)!.IsActive);
        // ровно жалоба заказчика: на S1 модель не установлена, и активной она быть не должна
        Assert.False(Other().Get(model.Id)!.IsActive);
    }

    [Fact]
    public void Each_Server_Switches_Its_Own_Activity()
    {
        var model = LocalModelOnS0();

        // S1 включает модель у себя — на S0 это ничего не меняет
        var onS1 = Other().Get(model.Id)!;
        onS1.IsActive = true;
        Other().Update(onS1, null);

        Assert.True(Other().Get(model.Id)!.IsActive);
        Assert.True(Conductor().Get(model.Id)!.IsActive);

        // S0 выключает у себя — у S1 модель остаётся включённой
        var onS0 = Conductor().Get(model.Id)!;
        onS0.IsActive = false;
        Conductor().Update(onS0, null);

        Assert.False(Conductor().Get(model.Id)!.IsActive);
        Assert.True(Other().Get(model.Id)!.IsActive);
    }

    [Fact]
    public void Cloud_Model_Activity_Stays_Common()
    {
        // у облачной модели ключ API один на организацию — активность общая, как и была
        var models = Conductor();
        var cloud = models.Create(new AiModel { Name = "Облачная", IsActive = true }, null);

        Assert.True(Other().Get(cloud.Id)!.IsActive);
        Assert.Empty(Other().Get(cloud.Id)!.Servers);
    }

    [Fact]
    public void Per_Server_Rows_Do_Reach_The_Change_Log()
    {
        // строка пер-серверной активности обязана уехать партнёру: из таких строк
        // складывается список «на каких серверах модель включена»
        var model = LocalModelOnS0();

        Assert.Contains(Journal("ai_model_servers"), pk => pk.Length > 0);
        Assert.NotEmpty(Conductor().Get(model.Id)!.Servers);
    }

    // --- 2. форма настройки открывается на любом сервере ---

    [Fact]
    public void The_Settings_Form_Opens_On_Any_Server_For_A_Local_Model()
    {
        var model = LocalModelOnS0();

        var seenFromS1 = Other().Get(model.Id)!;
        // общая часть (название) по-прежнему чужая…
        Assert.True(seenFromS1.IsReadOnly);
        // …а настройка своей части открывается здесь — это и была неактивная кнопка
        Assert.True(seenFromS1.CanConfigure);
    }

    [Fact]
    public void A_Foreign_Cloud_Record_Is_Still_Not_Configurable_Here()
    {
        // у облачной модели пер-серверной части нет: и правится она там же, где и раньше
        var models = Other();
        var mine = models.Create(new AiModel { Name = "Облачная S1" }, null);

        Assert.False(Conductor().Get(mine.Id)!.CanConfigure);
    }

    [Fact]
    public void A_Foreign_Server_Switches_Activity_But_Does_Not_Rename()
    {
        var model = LocalModelOnS0();

        // включить у себя — можно
        var onS1 = Other().Get(model.Id)!;
        onS1.IsActive = true;
        Other().Update(onS1, null);
        Assert.True(Other().Get(model.Id)!.IsActive);

        // переименовать чужую запись — нельзя, правило одного писателя не отменялось
        var rename = Other().Get(model.Id)!;
        rename.Name = "Переименованная с S1";
        Assert.Throws<ArgumentException>(() => Other().Update(rename, null));
        Assert.Equal(model.Name, Conductor().Get(model.Id)!.Name);
    }

    // --- 3. в справочнике видно, где модель включена ---

    [Fact]
    public void The_Catalog_Shows_The_Servers_Where_The_Model_Is_On()
    {
        var model = LocalModelOnS0();
        var onS1 = Other().Get(model.Id)!;
        onS1.IsActive = true;
        Other().Update(onS1, null);

        var listed = Conductor().List().Single(m => m.Id == model.Id);

        Assert.Equal(["S0", "S1"],
            listed.Servers.Where(s => s.IsActive).Select(s => s.ServerCode).OrderBy(c => c));
        Assert.Contains(listed.Servers, s => s is { ServerCode: "S0", IsSelf: true });
        Assert.Contains(listed.Servers, s => s is { ServerCode: "S1", IsSelf: false });
    }

    // --- 4. перенос прежней общей активности ---

    [Fact]
    public void The_Old_Common_Activity_Moves_Into_The_Row_Of_Its_Own_Server()
    {
        // данные, какими их оставила версия до 1.93: активность общая, владелец — S0
        var id = Guid.NewGuid().ToString();
        using (var conn = _db.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO ai_models (id, display_id, name, is_active, is_custom, is_local,
                                       profile_path, capabilities_path, server_id,
                                       created_at, updated_at)
                VALUES (@id, 'M-9', 'Старая локальная', 1, 1, 1, '', '', @owner, @now, @now)
                """,
                ("@id", id), ("@owner", ConductorId), ("@now", Sql.ToDb(DateTime.UtcNow)));
        }

        Assert.Equal(1, Conductor().MigrateActivationToServerParts());
        Assert.True(Conductor().Get(id)!.IsActive);
        // на S1 модель не установлена — строки у неё там нет, и активной она не горит
        Assert.Equal(0, Other().MigrateActivationToServerParts());
        Assert.False(Other().Get(id)!.IsActive);

        // повторный перенос ничего не находит
        Assert.Equal(0, Conductor().MigrateActivationToServerParts());
    }

    [Fact]
    public void The_Migration_Does_Not_Switch_On_What_Was_Off()
    {
        var id = Guid.NewGuid().ToString();
        using (var conn = _db.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO ai_models (id, display_id, name, is_active, is_custom, is_local,
                                       profile_path, capabilities_path, server_id,
                                       created_at, updated_at)
                VALUES (@id, 'M-10', 'Выключенная локальная', 0, 1, 1, '', '', @owner, @now, @now)
                """,
                ("@id", id), ("@owner", ConductorId), ("@now", Sql.ToDb(DateTime.UtcNow)));
        }

        Assert.Equal(0, Conductor().MigrateActivationToServerParts());
        Assert.False(Conductor().Get(id)!.IsActive);
    }

    // --- 5. исполнитель локальной модели ---

    [Fact]
    public void An_Executor_Of_A_Local_Model_Lands_On_The_Server_Where_It_Is_On()
    {
        var model = LocalModelOnS0();

        var executor = ConductorExecutors().Create(new Executor
        {
            Nick = "агент-S0",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            IsActive = true,
        }, null);

        Assert.Equal(ConductorId, executor.ServerId);
    }

    [Fact]
    public void An_Executor_Follows_The_Only_Server_Where_The_Model_Is_On()
    {
        // модель включена ТОЛЬКО на S1, а исполнителей заводят на дирижёре (ТЗ гл. 6)
        var model = LocalModelOnS0();
        var offS0 = Conductor().Get(model.Id)!;
        offS0.IsActive = false;
        Conductor().Update(offS0, null);
        var onS1 = Other().Get(model.Id)!;
        onS1.IsActive = true;
        Other().Update(onS1, null);

        var executor = ConductorExecutors().Create(new Executor
        {
            Nick = "агент-S1",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            IsActive = true,
        }, null);

        Assert.Equal(OtherId, executor.ServerId);
    }

    [Fact]
    public void An_Executor_Cannot_Be_Active_Where_Its_Model_Is_Off()
    {
        var model = LocalModelOnS0();
        var executors = ConductorExecutors();
        var executor = executors.Create(new Executor
        {
            Nick = "агент",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            IsActive = true,
        }, null);

        // модель выключили там, где живёт исполнитель
        var off = Conductor().Get(model.Id)!;
        off.IsActive = false;
        Conductor().Update(off, null);

        // сам исполнитель при этом выключается — работать ему нечем
        Assert.False(executors.Get(executor.Id)!.IsActive);

        // и включить его обратно, пока модель выключена, нельзя
        var again = executors.Get(executor.Id)!;
        again.IsActive = true;
        var error = Assert.Throws<ArgumentException>(() => executors.Update(again, null));
        Assert.Contains(model.Name, error.Message);
    }

    [Fact]
    public void An_Executor_Of_A_Foreign_Server_Is_Not_Touched()
    {
        // модель включена на обоих серверах, исполнитель привязан к S1
        var model = LocalModelOnS0();
        var onS1 = Other().Get(model.Id)!;
        onS1.IsActive = true;
        Other().Update(onS1, null);
        var executors = ConductorExecutors();
        var executor = executors.Create(new Executor
        {
            Nick = "агент-чужой",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            IsActive = true,
        }, null);
        SetServer(executor.Id, OtherId);

        // S0 выключает модель у СЕБЯ — исполнителя другого сервера это не касается
        var off = Conductor().Get(model.Id)!;
        off.IsActive = false;
        Conductor().Update(off, null);

        Assert.True(executors.Get(executor.Id)!.IsActive);
    }

    private void SetServer(string executorId, string serverId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE executors SET server_id=@s WHERE id=@id",
            ("@s", serverId), ("@id", executorId));
    }
}
