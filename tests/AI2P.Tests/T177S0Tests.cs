using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-177-S0: МЕСТНЫЕ ИСПОЛНИТЕЛИ заводятся на каждом сервере, а не только на дирижёре.
///
/// Местный — тот, чья работа физически лежит на этом компьютере: ИИ с ЛОКАЛЬНОЙ моделью и
/// «авто ПО» (плагины и MCP — они тоже локальны). Номер такому исполнителю выдаётся по тому
/// же правилу, что и задаче (ТЗ гл. 6): с суффиксом кода сервера — <c>E-1-S1</c>. Всё
/// остальное (человек, облачный ИИ) — записи всей организации, и правит их дирижёр.
///
/// Оба сервера работают с ОДНОЙ базой, как в Todo42Tests: проверяются правила владения,
/// а не сама репликация.
/// </summary>
public sealed class T177S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";

    private static ServerScope Scope(string serverId, string code, bool conductor) =>
        new(() => serverId, () => code, () => conductor,
            codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
            nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private static ServerScope ConductorScope => Scope(ConductorId, "S0", conductor: true);

    private static ServerScope OtherScope => Scope(OtherId, "S1", conductor: false);

    private ExecutorService Executors(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    private AiModelService Models(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    public void Dispose() => _f.Dispose();

    /// <summary>Локальная модель, включённая на ВТОРОМ сервере: заводит её дирижёр (запись
    /// справочника общая), а включает у себя S1 — активность локальной модели пер-серверная.</summary>
    private AiModel LocalModelOnS1(string name = "Локальная")
    {
        var models = Models(ConductorScope);
        var model = models.Create(new AiModel { Name = name }, null);
        _f.Files.WriteText(model.ProfilePath, """{"launchCommand":"llama-server"}""");
        models.SyncLocalFlags();

        var onS1 = Models(OtherScope).Get(model.Id)!;
        onS1.IsActive = true;
        return Models(OtherScope).Update(onS1, null);
    }

    private Executor SoftwareOnS1(string nick = "тренер") =>
        Executors(OtherScope).Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Software,
            PluginCode = "trainer.musubi",
            PluginOp = "lora.train",
        }, null);

    private List<string> Journal(string table)
    {
        using var conn = _f.Db.Open();
        return Sql.Query(conn, null, "SELECT pk FROM changes WHERE tbl=@t ORDER BY seq",
            r => r.S("pk"), ("@t", table));
    }

    // --- 1. что МОЖНО завести не на дирижёре ---

    [Fact]
    public void An_Executor_With_A_Local_Model_Is_Created_On_Its_Own_Server()
    {
        var model = LocalModelOnS1();

        var executor = Executors(OtherScope).Create(new Executor
        {
            Nick = "локальный агент",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);

        // номер с кодом сервера — как у задачи (ТЗ гл. 6): счётчики двух серверов иначе
        // дали бы двух разных исполнителей с одним номером
        Assert.Equal("E-1-S1", executor.DisplayId);
        Assert.Equal(OtherId, executor.ServerId);
    }

    [Fact]
    public void A_Plugin_Executor_Is_Created_On_Its_Own_Server()
    {
        var executor = SoftwareOnS1();

        Assert.Equal("E-1-S1", executor.DisplayId);
        Assert.Equal(OtherId, executor.ServerId);
        Assert.Equal("trainer.musubi", executor.PluginCode);
    }

    [Fact]
    public void The_Conductor_Still_Numbers_Executors_Without_A_Suffix()
    {
        var executor = Executors(ConductorScope).Create(new Executor { Nick = "человек" }, null);

        Assert.Equal("E-1", executor.DisplayId);
    }

    // --- 2. что НЕЛЬЗЯ завести не на дирижёре ---

    [Fact]
    public void A_Human_Is_Still_Created_On_The_Conductor_Only()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Executors(OtherScope).Create(new Executor { Nick = "второй" }, null));

        Assert.Contains("дирижёре", error.Message);
    }

    [Fact]
    public void An_Executor_With_A_Cloud_Model_Is_Created_On_The_Conductor_Only()
    {
        // облачная модель одна на всю организацию — её исполнитель тоже общий
        var cloud = Models(ConductorScope).Create(new AiModel { Name = "Облачная", IsActive = true }, null);

        var error = Assert.Throws<ArgumentException>(() =>
            Executors(OtherScope).Create(new Executor
            {
                Nick = "облачный агент",
                Kind = ExecutorKind.Ai,
                ModelId = cloud.Id,
            }, null));

        Assert.Contains("дирижёре", error.Message);
    }

    // --- 3. правка ---

    [Fact]
    public void Its_Own_Local_Executor_Is_Edited_Here()
    {
        var executor = SoftwareOnS1();
        executor.Nick = "тренер LoRA";

        var saved = Executors(OtherScope).Update(executor, null);

        Assert.Equal("тренер LoRA", saved.Nick);
    }

    [Fact]
    public void A_Foreign_Executor_Is_Not_Edited_Here()
    {
        var human = Executors(ConductorScope).Create(new Executor { Nick = "человек" }, null);
        human.Nick = "переименован";

        Assert.Throws<ArgumentException>(() => Executors(OtherScope).Update(human, null));
    }

    [Fact]
    public void Changing_The_Kind_Does_Not_Take_Over_A_Foreign_Row()
    {
        // право считается по ТОМУ, ЧТО ЛЕЖИТ В БАЗЕ: иначе сменой типа на «авто ПО» со своим
        // сервером второй сервер забрал бы себе общую запись дирижёра
        var human = Executors(ConductorScope).Create(new Executor { Nick = "человек" }, null);
        human.Kind = ExecutorKind.Software;
        human.PluginCode = "trainer.musubi";
        human.PluginOp = "lora.train";
        human.ServerId = OtherId;

        Assert.Throws<ArgumentException>(() => Executors(OtherScope).Update(human, null));
    }

    // --- 4. репликация ---

    [Fact]
    public void A_Local_Executor_Goes_To_The_Replication_Journal()
    {
        var executor = SoftwareOnS1();

        // executors — реплицируемая таблица (ChangeLog): заведённый на S1 исполнитель
        // уезжает партнёрам обычным журналом изменений, отдельного пути ему не нужно
        Assert.Contains(executor.Id, Journal("executors"));
    }
}
