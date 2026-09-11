using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-47-S0: РЕПЛИКАЦИЯ АРХИВОВ МЕЖДУ СЕРВЕРАМИ и скачивание архива с другого сервера.
///
/// Что здесь доказывается (репликацию как таковую тестами не докажешь — она проверяется
/// живьём на двух серверах; тесты закрывают ПРАВИЛА, на которых она стоит):
/// <list type="number">
/// <item>смена текущего архива едет ОТДЕЛЬНОЙ таблицей-поручением, а не новым видом строки
/// в уже существующей (наука T-263), и сервер прежней версии незнакомую таблицу пропускает
/// молча;</item>
/// <item>признак «текущий» НЕ приезжает колонкой: иначе он переключился бы раньше, чем
/// прежний текущий архив дошлёт свои данные;</item>
/// <item>открыть/закрыть/удалить не реплицируются (<c>archive_states</c> нет в журнале),
/// а «где архив есть» — реплицируется (<c>archive_servers</c> есть);</item>
/// <item>заводить архив на диске репликация вправе только текущий (или тот, который вот-вот
/// станет текущим): удалённый человеком архив сам собой не возвращается;</item>
/// <item>порядок передачи: сначала прежний текущий, потом новый;</item>
/// <item>тексты — в ОБОИХ словарях.</item>
/// </list>
/// </summary>
public sealed class T47S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveService Service() => new(_f.Db, _f.Events, ServerScope.Standalone());

    // --- 1. новый род поручения — новая таблица (наука T-263) ---

    [Fact]
    public void Handover_Is_Its_Own_Replicated_Table_And_Marks_Are_Local()
    {
        Assert.Contains("archive_handovers", ChangeLog.OrgTables);
        // отметки об исполнении — собственная память сервера (образец run_requests_applied)
        Assert.DoesNotContain("archive_handovers_applied", ChangeLog.OrgTables);
        // а состояние «открыт здесь / закрыт» не реплицируется по-прежнему
        Assert.DoesNotContain("archive_states", ChangeLog.OrgTables);
        // «где архив есть» — реплицируется: иначе неоткуда взять список источников скачивания
        Assert.Contains("archive_servers", ChangeLog.OrgTables);
    }

    [Fact]
    public void Old_Server_Skips_An_Unknown_Table_Silently()
    {
        // ровно то поведение, ради которого поручение вынесено в свою таблицу: строку
        // таблицы, которой у него нет, приёмник журнала пропускает и не делает НИЧЕГО
        var result = ChangeLog.Apply(_f.Db,
        [
            new RowChange
            {
                Table = "archive_handovers_of_the_future",
                Pk = "x",
                Op = ChangeOps.Upsert,
                Seq = 1,
                Ts = "2026-08-29T00:00:00.000Z",
                NodeId = "other",
                PayloadJson = "{\"id\":\"x\"}",
            },
        ], null);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Failed);
    }

    // --- 2. признак «текущий» не едет колонкой ---

    [Fact]
    public void Current_Flag_Never_Arrives_As_A_Column()
    {
        var values = new Dictionary<string, object?>
        {
            ["id"] = "a1",
            ["code"] = "arc2026",
            ["is_current"] = 1L,
        };
        Assert.True(AI2P.Server.Org.ReplicationService.KeepCurrentArchive(values));
        Assert.False(values.ContainsKey("is_current"));
        // остальное применяется как обычно
        Assert.Equal("arc2026", values["code"]);
    }

    // --- 3. поручение о смене: заводится, ждёт, исполняется ---

    [Fact]
    public void Creating_An_Archive_Records_A_Handover_With_The_Previous_One()
    {
        var service = Service();
        var first = service.Create("t47a", "Первый", "", null);
        var second = service.Create("t47b", "Второй", "", null);

        var pending = service.PendingHandovers("peer-1");
        Assert.Equal(2, pending.Count);
        Assert.Equal(first.Id, pending[0].ArchiveId);
        Assert.Equal("", pending[0].PrevArchiveId);   // архив первый — досылать нечего
        Assert.Equal(second.Id, pending[1].ArchiveId);
        Assert.Equal(first.Id, pending[1].PrevArchiveId);
    }

    [Fact]
    public void Handover_Marks_Are_Kept_Per_Peer()
    {
        var service = Service();
        service.Create("t47c", "Один", "", null);
        var pending = service.PendingHandovers("peer-1");
        service.ApplyHandover(pending[0], "peer-1");

        Assert.Empty(service.PendingHandovers("peer-1"));
        // второму партнёру досыл ещё не состоялся — для него поручение не исполнено
        Assert.Single(service.PendingHandovers("peer-2"));
    }

    [Fact]
    public void Applying_A_Handover_Switches_The_Current_Archive()
    {
        var service = Service();
        var first = service.Create("t47d", "Один", "", null);
        var second = service.Create("t47e", "Два", "", null);
        // приводим базу в состояние сервера-ПОЛУЧАТЕЛЯ: признак «текущий» туда колонкой
        // не приезжает, поэтому там он остался у прежнего архива
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null,
                "UPDATE archives SET is_current=CASE WHEN id=@id THEN 1 ELSE 0 END",
                ("@id", first.Id));
        }
        Assert.Equal(first.Id, service.Current()!.Id);

        foreach (var handover in service.PendingHandovers("peer-1"))
        {
            service.ApplyHandover(handover, "peer-1");
        }
        Assert.Equal(second.Id, service.Current()!.Id);
        Assert.False(service.Get(first.Id)!.IsCurrent);
        Assert.Single(service.List().Where(a => a.IsCurrent));
    }

    // --- 4. репликация вправе завести только текущий архив ---

    [Fact]
    public void Only_The_Current_Archive_May_Be_Re_Created_By_Replication()
    {
        var service = Service();
        var old = service.Create("t47f", "Старый", "", null);
        var current = service.Create("t47g", "Текущий", "", null);
        Assert.True(service.ShouldHost(current.Id));
        Assert.False(service.ShouldHost(old.Id));

        // человек удалил старый архив с этого сервера — репликация не вправе его вернуть
        service.Remove(old.Id, null);
        Assert.Equal(ArchiveStates.Deleted, service.Get(old.Id)!.State);
        Assert.Null(service.DbForReplication(service.Get(old.Id)!));
        Assert.NotNull(service.DbForReplication(service.Get(current.Id)!));
    }

    [Fact]
    public void A_Closed_Archive_Does_Not_Exchange_Data()
    {
        var service = Service();
        var first = service.Create("t47h", "Старый", "", null);
        service.Create("t47i", "Текущий", "", null);
        service.Close(first.Id, null);
        // состояние выбрал человек: распаковывать архив ради обмена нельзя
        Assert.Equal(ArchiveStates.Closed, service.Get(first.Id)!.State);
        Assert.Null(service.DbForReplication(service.Get(first.Id)!));
    }

    // --- 5. состав и порядок передачи ---

    [Fact]
    public void Archive_Scope_Names_The_Archive_In_Itself()
    {
        var scope = ReplScopes.ArchiveScope("a-1");
        Assert.Equal("arc:a-1", scope);
        Assert.Equal("a-1", ReplScopes.ArchiveOf(scope));
        // обычные виды обмена архивными не считаются
        Assert.Equal("", ReplScopes.ArchiveOf(ReplScopes.Org));
        Assert.Equal("", ReplScopes.ArchiveOf(ReplScopes.Server));
        Assert.Equal("", ReplScopes.ArchiveOf(ReplScopes.OrgFiles));
        Assert.Equal("", ReplScopes.ArchiveOf(null));
    }

    [Fact]
    public void Where_The_Archive_Is_Comes_From_The_Replicated_Table()
    {
        var service = Service();
        var archive = service.Create("t47j", "Текущий", "", null);
        Assert.Equal([ServerScope.Standalone().ServerId], service.Get(archive.Id)!.Servers);

        // сосед объявил, что архив есть и у него, — строка приехала репликацией
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO archive_servers (id, archive_id, server_id, has_copy, created_at, updated_at)
                VALUES (@id, @a, 'srv-2', 1, @at, @at)
                """, ("@id", archive.Id + "|srv-2"), ("@a", archive.Id),
                ("@at", Sql.ToDb(DateTime.UtcNow)));
        }
        Assert.Contains("srv-2", service.Get(archive.Id)!.Servers);
    }

    [Fact]
    public void A_Deleted_Archive_Is_Announced_As_Gone_But_Stays_In_The_Registry()
    {
        var service = Service();
        var first = service.Create("t47k", "Старый", "", null);
        service.Create("t47l", "Текущий", "", null);
        service.Remove(first.Id, null);
        var gone = service.Get(first.Id)!;
        Assert.Equal(ArchiveStates.Deleted, gone.State);
        // запись реестра осталась, а «есть у меня» снято: соседи по-прежнему видят архив
        // в списке и понимают, что здесь его нет и качать его надо у них
        Assert.DoesNotContain(ServerScope.Standalone().ServerId, gone.Servers);
    }

    // --- 6. отдача архива соседу: всегда .zip ---

    [Fact]
    public void An_Archive_Always_Travels_Closed()
    {
        var service = Service();
        var archive = service.Create("t47m", "Текущий", "", null);
        Assert.Equal(ArchiveStates.Open, service.Get(archive.Id)!.State);

        var zip = service.PackForShare(archive.Code);
        Assert.NotNull(zip);
        Assert.True(File.Exists(zip));
        Assert.EndsWith(".zip", zip);
        // свёрток временный: он НЕ означает «архив здесь закрыт»
        Assert.NotEqual(service.ZipOf(archive.Code), zip);
        Assert.Equal(ArchiveStates.Open, service.StateOf(archive.Code));

        service.DropShare(archive.Code);
        Assert.False(File.Exists(zip));
        Assert.Equal(ArchiveStates.Open, service.StateOf(archive.Code));
    }

    [Fact]
    public void Nothing_To_Share_When_The_Archive_Is_Not_Here()
    {
        var service = Service();
        var first = service.Create("t47n", "Старый", "", null);
        service.Create("t47o", "Текущий", "", null);
        service.Remove(first.Id, null);
        Assert.Null(service.PackForShare(first.Code));
    }

    // --- 7. тексты в обоих словарях ---

    [Fact]
    public void Texts_Are_In_Both_Dictionaries()
    {
        foreach (var key in new[] { "msg.arcrepl.1", "msg.arcrepl.2", "msg.arcrepl.3", "msg.arcrepl.4" })
        {
            Assert.NotEqual(key, Loc.In("ru", key));
            Assert.NotEqual(key, Loc.In("en", key));
        }
    }
}
