using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-227: СПРАВОЧНИК МОДЕЛЕЙ — владелец записи, репликация и конфликты.
///
/// Беда, с которой всё началось: поставили новую версию на дирижёра, потом на второй сервер —
/// и часть записей справочника на ДИРИЖЁРЕ стала «чужой» (S1) и перестала правиться, хотя
/// человек их нигде не трогал. Причина — пересчёт производного признака размещения
/// (<c>SyncLocalFlags</c>): он зовётся при каждом открытии организации, в том числе на
/// реплике, и заодно проставлял владельца записи, а на не-дирижёре владельцем всегда
/// становился свой сервер.
///
/// Здесь проверяется четыре вещи:
/// <list type="number">
/// <item>пересчёт размещения владельца записи больше не меняет — присвоить чужую запись
///   нечем;</item>
/// <item>записи, которые каждая установка заводит СЕБЕ САМА (сид дистрибутива, импорт
///   инсталлятора), в журнал изменений не попадают, а правка человека — попадает;</item>
/// <item>одновременная правка одной записи на двух серверах — конфликт с решением
///   «принять левую / правую», совпавшие значения сливаются молча;</item>
/// <item>шаг обновления билда 85 возвращает уже присвоенные записи в общую часть
///   справочника и идемпотентен.</item>
/// </list>
/// </summary>
public sealed class T227Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t227-" + Guid.NewGuid().ToString("N"));

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;

    public T227Tests()
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

    /// <summary>Справочник моделей глазами дирижёра (S0).</summary>
    private AiModelService Conductor() => new(_db, _events, _files,
        new ServerScope(() => ConductorId, () => "S0", () => true,
            id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
            id => id));

    /// <summary>Тот же справочник глазами РЯДОВОГО сервера (S1) — то есть реплики.</summary>
    private AiModelService Other() => new(_db, _events, _files,
        new ServerScope(() => OtherId, () => "S1", () => false,
            id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
            id => id));

    /// <summary>Ключи строк таблицы в журнале изменений (то, что уедет партнёру).</summary>
    private List<string> Journal(string table)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT pk FROM changes WHERE tbl=@t ORDER BY seq",
            r => r.S("pk"), ("@t", table));
    }

    private string? OwnerOf(string modelId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null, "SELECT server_id FROM ai_models WHERE id=@id",
            ("@id", modelId));
    }

    // --- 1. пересчёт размещения не присваивает записи ---

    [Fact]
    public void Recalculating_Placement_Does_Not_Take_Over_The_Conductors_Records()
    {
        // облачная запись дирижёра: владельца нет — общая часть справочника
        var models = Conductor();
        var cloud = models.Create(new AiModel { Name = "Облачная" }, null);
        Assert.Null(OwnerOf(cloud.Id));

        // ровно то, что делает старт приложения на РЯДОВОМ сервере (в том числе на реплике)
        Other().SyncLocalFlags();

        // до T-227 здесь оказывался unid-S1, и на дирижёре запись становилась чужой навсегда
        Assert.Null(OwnerOf(cloud.Id));
        Assert.False(Conductor().Get(cloud.Id)!.IsReadOnly);
    }

    [Fact]
    public void Recalculating_Placement_Does_Not_Take_Over_A_Foreign_Servers_Records()
    {
        var models = Other();
        var mine = models.Create(new AiModel { Name = "Заведена на S1" }, null);
        Assert.Equal(OtherId, OwnerOf(mine.Id));

        // дирижёр пересчитывает размещение у себя — забрать чужую запись он тоже не должен
        Conductor().SyncLocalFlags();

        Assert.Equal(OtherId, OwnerOf(mine.Id));
    }

    [Fact]
    public void A_Model_That_Became_Local_Still_Belongs_To_Its_Server()
    {
        // обратная сторона правила: локальная модель работает на конкретном компьютере,
        // поэтому бесхозная запись, ставшая локальной, достаётся тому, кто вправе её писать
        var models = Conductor();
        var model = models.Create(new AiModel { Name = "Локальная" }, null);
        _files.WriteText(model.ProfilePath,
            """{"provider":"openai","launchCommand":"llama-server"}""");

        models.SyncLocalFlags();

        Assert.True(models.Get(model.Id)!.IsLocal);
        Assert.Equal(ConductorId, OwnerOf(model.Id));
    }

    [Fact]
    public void A_Foreign_Local_Model_Is_Not_Taken_Over_By_The_Recalculation()
    {
        var model = Other().Create(new AiModel { Name = "Локальная S1" }, null);
        _files.WriteText(model.ProfilePath,
            """{"provider":"openai","launchCommand":"llama-server"}""");
        Other().SyncLocalFlags();
        Assert.Equal(OtherId, OwnerOf(model.Id));

        // дирижёр видит тот же профайл (файлы реплицируются), но владельца не переписывает
        Conductor().SyncLocalFlags();

        Assert.Equal(OtherId, OwnerOf(model.Id));
    }

    // --- 2. что попадает в журнал изменений, а что нет ---

    [Fact]
    public void Distribution_Records_Never_Reach_The_Change_Log()
    {
        var models = Conductor();

        models.Seed();

        Assert.NotEmpty(models.List());
        Assert.Empty(Journal("ai_models"));
        // и порядковые номера записей тоже: они местные, у партнёра свои
        Assert.Empty(Journal("counters"));
    }

    [Fact]
    public void Installer_Import_Never_Reaches_The_Change_Log()
    {
        _files.WriteText("models/import_qwen.json", """
            {"name": "Импортированная", "profile": {"launchCommand": "llama-server"}}
            """);

        Conductor().ImportPending();

        var imported = Assert.Single(Conductor().List(), m => m.Name == "Импортированная");
        Assert.True(imported.IsLocal);
        // локальная модель принадлежит серверу, на котором её поставил инсталлятор
        Assert.Equal(ConductorId, OwnerOf(imported.Id));
        Assert.Empty(Journal("ai_models"));
    }

    [Fact]
    public void A_Human_Edit_Does_Reach_The_Change_Log()
    {
        var models = Conductor();
        models.Seed();
        // ОБЛАЧНАЯ запись нужна намеренно: у локальной активность с T-8-S1 живёт в своей
        // пер-серверной таблице ai_model_servers, и правка IsActive в журнал ai_models не
        // попадает вовсе. Список отсортирован по имени, и раньше First() отдавал облачную
        // случайно — пока T-18-S0 не завёл записи ACE-Step, вставшие по алфавиту прежде
        // Claude. Молчаливая зависимость от порядка выглядела как дефект репликации
        var model = models.List().First(m => !m.IsLocal);
        Assert.Empty(Journal("ai_models"));

        model.IsActive = false;
        models.Update(model, null);

        // правка человека реплицируется — ради неё всё и затевалось
        Assert.Equal([model.Id], Journal("ai_models"));
    }

    [Fact]
    public void A_Custom_Record_Added_By_Hand_Does_Reach_The_Change_Log()
    {
        var created = Conductor().Create(new AiModel { Name = "Своя" }, null);

        Assert.Contains(created.Id, Journal("ai_models"));
    }

    [Fact]
    public void The_Mute_Is_Released_Even_When_The_Seed_Fails()
    {
        // пометка обязана сниматься при ЛЮБОМ исходе: иначе после ошибки сида перестала бы
        // реплицироваться ВСЯ организация — молча и до перезапуска
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var mute = ChangeLog.Mute(_db);
            Assert.True(ChangeLog.IsMuted(_db));
            throw new InvalidOperationException("сбой посреди сида");
        }));

        Assert.False(ChangeLog.IsMuted(_db));
        var model = Conductor().Create(new AiModel { Name = "После сбоя" }, null);
        Assert.Contains(model.Id, Journal("ai_models"));
    }

    [Fact]
    public void Nested_Mutes_Do_Not_Unmute_Each_Other()
    {
        using (var outer = ChangeLog.Mute(_db))
        {
            using (var inner = ChangeLog.Mute(_db))
            {
                Assert.True(ChangeLog.IsMuted(_db));
            }
            // внутренняя пометка снята, внешняя держится — иначе сид, зовущий импорт,
            // начал бы писать в журнал с середины
            Assert.True(ChangeLog.IsMuted(_db));
            Conductor().Create(new AiModel { Name = "Под пометкой" }, null);
        }

        Assert.False(ChangeLog.IsMuted(_db));
        Assert.Empty(Journal("ai_models"));
    }

    // --- 3. конфликт «левая / правая» ---

    [Fact]
    public void A_Record_We_Did_Not_Touch_Is_Applied_As_Usual()
    {
        var mine = new AiModel { Name = "Своя", IsActive = true, UpdatedAt = Time("2026-08-01") };
        var verdict = ReplicationService.JudgeModel(mine, Time("2026-08-10"),
            Incoming("Переименована", active: false));

        Assert.True(verdict.Accept);
    }

    [Fact]
    public void The_Very_First_Session_Is_Never_A_Conflict()
    {
        // удачных сеансов с этим партнёром ещё не было: соглашаться было не о чем, и весь
        // справочник дистрибутива объявлялся бы спорным — тридцать конфликтов на ровном месте
        var mine = new AiModel { Name = "Своя", IsActive = true, UpdatedAt = DateTime.UtcNow };

        var verdict = ReplicationService.JudgeModel(mine, null, Incoming("Другая"));

        Assert.True(verdict.Accept);
    }

    [Fact]
    public void An_Unknown_Record_Is_Applied_As_Usual()
    {
        var verdict = ReplicationService.JudgeModel(null, Time("2026-08-10"), Incoming("Новая"));

        Assert.True(verdict.Accept);
    }

    [Fact]
    public void The_Same_Edit_On_Both_Servers_Merges_Silently()
    {
        var mine = new AiModel { Name = "Общая", IsActive = false, UpdatedAt = Time("2026-08-12") };

        var verdict = ReplicationService.JudgeModel(mine, Time("2026-08-10"),
            Incoming("Общая", active: false));

        Assert.True(verdict.Accept);
    }

    [Fact]
    public void Different_Edits_On_Both_Servers_Are_A_Conflict()
    {
        var mine = new AiModel { Name = "Имя дирижёра", IsActive = true, UpdatedAt = Time("2026-08-12") };

        var verdict = ReplicationService.JudgeModel(mine, Time("2026-08-10"),
            Incoming("Имя второго сервера"));

        Assert.False(verdict.Accept);
        // стороны хранятся как JSON (кириллица в нём экранируется — сравниваем значениями)
        Assert.Equal(ReplicationService.ModelValue("Имя дирижёра", true, false, false), verdict.Ours);
        Assert.Equal(ReplicationService.ModelValue("Имя второго сервера", true, false, false),
            verdict.Theirs);
    }

    [Fact]
    public void Deleting_On_One_Side_And_Renaming_On_The_Other_Is_A_Conflict()
    {
        var mine = new AiModel
        {
            Name = "Своя", IsActive = true, UpdatedAt = Time("2026-08-12"),
            DeletedAt = Time("2026-08-12"),
        };

        var verdict = ReplicationService.JudgeModel(mine, Time("2026-08-10"), Incoming("Своя"));

        Assert.False(verdict.Accept);
    }

    [Fact]
    public void Model_Conflict_Lives_In_The_Same_List_As_Files_And_Keys()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv"), "server.db", DatabaseKind.Server);
        serverDb.Init("node-a");
        var conflicts = new FileSyncStateService(serverDb);
        var scopeKey = FileSyncStateService.ScopeKey(ReplicationService.ModelScope, "org-1");
        var left = ReplicationService.ModelValue("Слева", true, false, false);
        var right = ReplicationService.ModelValue("Справа", true, false, false);

        conflicts.Record("org-1", OtherId, ReplicationService.ModelScope, scopeKey, "", "model-1",
            left: new FileEntry { Path = "model-1", Mtime = "t1", Hash = "aaaa" },
            right: new FileEntry { Path = "model-1", Mtime = "t2", Hash = "bbbb" },
            leftValue: left, rightValue: right);

        var row = Assert.Single(conflicts.Conflicts(OtherId));
        Assert.Equal("model", row.Scope);
        Assert.Equal("model-1", row.Path);
        Assert.Equal(left, row.LeftValue);
        Assert.Equal(right, row.RightValue);

        // решение снимает конфликт — у файлов и ключей это делается тем же вызовом
        conflicts.Clear("org-1", OtherId, scopeKey, "model-1");
        Assert.Empty(conflicts.Conflicts(OtherId));
    }

    [Fact]
    public void Applying_A_Side_Writes_It_And_Sends_It_To_The_Partner()
    {
        var models = Conductor();
        var model = models.Create(new AiModel { Name = "Спорная" }, null);

        models.ApplyConflictResolution(model.Id,
            ReplicationService.ModelValue("Победившее имя", false, false, false), null);

        var applied = models.Get(model.Id)!;
        Assert.Equal("Победившее имя", applied.Name);
        Assert.False(applied.IsActive);
        Assert.Null(applied.DeletedAt);
        // решение — обычная свежая запись: она уедет партнёру и снимет конфликт и у него
        Assert.Contains(model.Id, Journal("ai_models"));
    }

    [Fact]
    public void Applying_A_Side_Can_Delete_The_Record()
    {
        var models = Conductor();
        var model = models.Create(new AiModel { Name = "Удалена на втором" }, null);

        models.ApplyConflictResolution(model.Id,
            ReplicationService.ModelValue("Удалена на втором", true, false, deleted: true), null);

        Assert.NotNull(models.Get(model.Id)!.DeletedAt);
        Assert.DoesNotContain(models.List(), m => m.Id == model.Id);
    }

    // --- 4. шаг обновления билда 85 ---

    [Fact]
    public void Upgrade_Step_85_Is_Declared()
    {
        var step = Assert.Single(Upgrade.Steps, s => s.Build == 85);
        Assert.Contains("справочник", step.Title);
    }

    [Fact]
    public void Step_85_Returns_Stolen_Cloud_Records_And_Is_Idempotent()
    {
        var stolen = Row("M-3", isLocal: false, owner: OtherId);            // приехала с дирижёра
        var ownCloud = Row("M-9-S1", isLocal: false, owner: OtherId);       // заведена на S1
        var ownLocal = Row("M-4-S1", isLocal: true, owner: OtherId);        // локальная модель S1
        var conductors = Row("M-5-S0", isLocal: false, owner: ConductorId); // общая часть

        var freed = Conductor().ReleaseStolenCloudRecords(ConductorId, CodeOf);

        Assert.Equal(1, freed);
        Assert.Null(OwnerOf(stolen));
        Assert.Equal(OtherId, OwnerOf(ownCloud));
        Assert.Equal(OtherId, OwnerOf(ownLocal));
        Assert.Equal(ConductorId, OwnerOf(conductors));
        // исправление обязано доехать до партнёра — значит идёт журналом изменений
        Assert.Contains(stolen, Journal("ai_models"));

        // повторный старт ничего не находит и ничего не ломает
        Assert.Equal(0, Conductor().ReleaseStolenCloudRecords(ConductorId, CodeOf));
        Assert.Null(OwnerOf(stolen));

        static string CodeOf(string id) => id == ConductorId ? "S0" : id == OtherId ? "S1" : "";
    }

    [Fact]
    public void Step_85_Works_On_The_Rank_And_File_Server_Too()
    {
        // чинить надо на ОБЕИХ сторонах: у себя рядовой сервер снимает то, что присвоил
        var stolen = Row("M-3", isLocal: false, owner: OtherId);

        var freed = Other().ReleaseStolenCloudRecords(ConductorId,
            id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "");

        Assert.Equal(1, freed);
        Assert.Null(OwnerOf(stolen));
    }

    // --- вспомогательное ---

    /// <summary>Строка справочника прямо в базе: нужны сочетания номера, владельца
    /// и размещения, которых обычным путём не завести.</summary>
    private string Row(string displayId, bool isLocal, string owner)
    {
        var id = Guid.NewGuid().ToString();
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO ai_models (id, display_id, name, is_active, is_custom, is_local,
                                   profile_path, capabilities_path, server_id, created_at, updated_at)
            VALUES (@id, @did, @name, 1, 1, @local, '', '', @server, @now, @now)
            """,
            ("@id", id), ("@did", displayId), ("@name", "Модель " + displayId),
            ("@local", isLocal ? 1 : 0), ("@server", owner),
            ("@now", Sql.ToDb(DateTime.UtcNow)));
        return id;
    }

    private static DateTime Time(string date) =>
        DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal
            | System.Globalization.DateTimeStyles.AssumeUniversal);

    /// <summary>Приходящая строка справочника — как её разбирает журнал изменений.</summary>
    private static Dictionary<string, object?> Incoming(string name, bool active = true,
        bool local = false, string? deletedAt = null) =>
        new(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["is_active"] = active ? 1L : 0L,
            ["is_local"] = local ? 1L : 0L,
            ["deleted_at"] = deletedAt,
            ["updated_at"] = "2026-08-12T00:00:00.0000000Z",
        };
}
