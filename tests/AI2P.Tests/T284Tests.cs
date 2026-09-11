using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-284: «НЕТ СВЯЗИ С СЕРВЕРОМ» ВНИЗУ ФОРМЫ СЕРВЕРА — А РЕПЛИКАЦИЯ ИДЁТ.
///
/// Жалоба заказчика: внизу формы сервера 192.168.0.20 висит
///
///     Нет связи с сервером «192.168.0.20» (http://192.168.0.20:5480/ai2p): Подключение
///     не установлено, т.к. конечный компьютер отверг запрос на подключение. …
///
/// при том, что репликация с этим сервером проходит нормально и без ошибок.
///
/// Разбор. Красная строка формы — это <c>servers.last_error</c>, отдельная от состояния пары
/// (<c>repl_state.last_error</c>, экран диагностики). Писали её три места (проба связи, заявка
/// на подключение и НЕУДАЧНЫЙ сеанс репликации), а снимали — только проба и пассивная сторона
/// сеанса (<c>/repl/end</c>, T-153). Ведущая сторона удачный сеанс не отмечала никак, поэтому
/// одна давняя неудача оставалась в записи сервера навсегда.
///
/// Второй случай той же жалобы — партнёр, до которого нам не дозвониться (закрытый порт,
/// межсетевой экран), но который звонит нам сам: пара реплицируется, а каждый наш заход
/// снова красит запись. Такой заход теперь в запись сервера не пишется — см.
/// <see cref="ReplicationService.PartnerDrives"/>.
/// </summary>
public sealed class T284Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t284-" + Guid.NewGuid().ToString("N"));

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

    private const string LocalId = "unid-S0";
    private const string PeerId = "unid-S1";

    /// <summary>Жалоба заказчика дословно — ею и красится запись сервера.</summary>
    private const string Complaint =
        "Нет связи с сервером «192.168.0.20» (http://192.168.0.20:5480/ai2p): Подключение "
        + "не установлено, т.к. конечный компьютер отверг запрос на подключение.";

    private OrgRegistry Registry()
    {
        var deps = new OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: new SecretStore(Path.Combine(_dir, "secrets.json")),
            LocalModels: new LocalModelProcessService(),
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => "",
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => true,
            LocalServerId: () => LocalId,
            LocalCode: _ => "S0",
            ServerCode: (_, _) => "S1",
            ServerName: _ => "сервер");
        return new OrgRegistry(Path.Combine(_dir, "server"), "server.db", deps);
    }

    private static ServerSaveInput Input(string name, int port) =>
        new() { Name = name, Protocol = "http", Hostname = "localhost", Port = port, BasePath = "/ai2p" };

    private static long Journal(OrgRegistry registry)
    {
        using var conn = registry.ServerDb.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM changes WHERE tbl='servers'");
    }

    // ---------- 1. «звонит он сам»: когда наш неудачный заход не новость ----------

    /// <summary>Первый заход после запуска приложения: про пару мы не знаем ничего, и молчать
    /// нельзя — неудача показывается человеку.</summary>
    [Fact]
    public void The_First_Call_After_A_Restart_Always_Reports_Its_Failure()
    {
        var now = DateTime.UtcNow;

        Assert.False(ReplicationService.PartnerDrives(pairOkAt: now.AddMinutes(-1),
            ourPreviousCallAt: null, ourLastOkAt: null));
        // и пары, о которой вообще нечего сказать, это тоже касается
        Assert.False(ReplicationService.PartnerDrives(null, null, null));
    }

    /// <summary>Удачных сеансов с нашего прошлого захода не было — пара стоит, и это ошибка
    /// связи в чистом виде.</summary>
    [Fact]
    public void A_Pair_That_Stopped_Replicating_Still_Reports_Its_Failure()
    {
        var now = DateTime.UtcNow;

        Assert.False(ReplicationService.PartnerDrives(pairOkAt: now.AddHours(-3),
            ourPreviousCallAt: now.AddMinutes(-5), ourLastOkAt: now.AddHours(-3)));
    }

    /// <summary>Прошлый удачный сеанс был НАШ — значит, дозвониться мы умеем, и сегодняшний
    /// отказ настоящая новость. Иначе поломка связи молчала бы целый заход.</summary>
    [Fact]
    public void Our_Own_Success_Does_Not_Excuse_The_Next_Failure()
    {
        var now = DateTime.UtcNow;
        var ours = now.AddMinutes(-4);

        Assert.False(ReplicationService.PartnerDrives(pairOkAt: ours,
            ourPreviousCallAt: now.AddMinutes(-5), ourLastOkAt: ours));
    }

    /// <summary>Случай заказчика: пара отреплицировалась ПОСЛЕ нашего прошлого звонка, и удача
    /// эта не наша — сеансы ведёт партнёр, красить запись сервера не за что.</summary>
    [Fact]
    public void A_Session_Run_By_The_Partner_Means_The_Link_Is_Alive()
    {
        var now = DateTime.UtcNow;

        // мы не дозванивались ни разу
        Assert.True(ReplicationService.PartnerDrives(pairOkAt: now.AddMinutes(-1),
            ourPreviousCallAt: now.AddMinutes(-5), ourLastOkAt: null));
        // и когда наш последний успех был давно, а партнёр звонил только что
        Assert.True(ReplicationService.PartnerDrives(pairOkAt: now.AddMinutes(-1),
            ourPreviousCallAt: now.AddMinutes(-5), ourLastOkAt: now.AddDays(-2)));
    }

    // ---------- 2. запись сервера: снимается ли жалоба ----------

    [Fact]
    public void The_Complaint_Lives_In_The_Server_Row_And_Is_Cleared_By_An_Empty_Value()
    {
        using var registry = Registry();
        registry.Servers.Create(Input("192.168.0.20", 5480), actorId: null, id: PeerId);

        registry.Servers.SetLastError(PeerId, Complaint);
        Assert.Equal(Complaint, registry.Servers.Get(PeerId)!.LastError);

        // ровно это делает удачный сеанс — и ведущей стороны тоже (T-284)
        registry.Servers.SetLastError(PeerId, "");
        Assert.Equal("", registry.Servers.Get(PeerId)!.LastError);
    }

    /// <summary>
    /// Строка <c>servers</c> реплицируется, поэтому её правка ложится записью в журнал
    /// изменений — даже такая, из которой партнёру не уедет ни одной колонки. Повторять
    /// одно и то же значение каждый сеанс (и каждый повтор к выключенному партнёру) значит
    /// вечно гонять пустые строки по сети.
    /// </summary>
    [Fact]
    public void The_Same_Value_Is_Not_Written_Twice()
    {
        using var registry = Registry();
        registry.Servers.Create(Input("192.168.0.20", 5480), actorId: null, id: PeerId);
        var after = Journal(registry);

        registry.Servers.SetLastError(PeerId, Complaint);
        var once = Journal(registry);
        Assert.True(once > after, "первая запись ошибки обязана попасть в журнал");

        // повтор той же неудачи (сторож репликации ходит по кругу каждую минуту)
        registry.Servers.SetLastError(PeerId, Complaint);
        registry.Servers.SetLastError(PeerId, Complaint);
        Assert.Equal(once, Journal(registry));

        // а снятие — это уже другое значение, и его записать надо
        registry.Servers.SetLastError(PeerId, "");
        Assert.True(Journal(registry) > once);
        // снимать нечего — второй раз в журнал не пишем
        var cleared = Journal(registry);
        registry.Servers.SetLastError(PeerId, "");
        Assert.Equal(cleared, Journal(registry));
    }

    /// <summary>Соседние серверы правкой одной записи не задеваются.</summary>
    [Fact]
    public void Clearing_One_Server_Leaves_The_Others_Alone()
    {
        using var registry = Registry();
        registry.Servers.Create(Input("192.168.0.20", 5480), actorId: null, id: PeerId);
        registry.Servers.Create(Input("192.168.0.21", 5480), actorId: null, id: "unid-S2");

        registry.Servers.SetLastError(PeerId, Complaint);
        registry.Servers.SetLastError("unid-S2", "другая беда");
        registry.Servers.SetLastError(PeerId, "");

        Assert.Equal("", registry.Servers.Get(PeerId)!.LastError);
        Assert.Equal("другая беда", registry.Servers.Get("unid-S2")!.LastError);
    }
}
