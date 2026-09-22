using AI2P.Core;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-40-S0: ЯДРО АРХИВАЦИИ — хранилище архивов, состояния, открыть/закрыть.
///
/// 1. Реестр архивов (реплицируемый) и локальное состояние (нереплицируемое) — две разные
///    таблицы, и это проверяется по списку <see cref="ChangeLog.OrgTables"/>, а не на глаз:
///    попади archive_states в репликацию, «закрыл у себя» означало бы «закрыл у всех».
/// 2. Схема организации поднята до v43 ОДИН раз на весь выпуск 1.105.
/// 3. Архив создаётся только на дирижёре; новый становится текущим, прежний — открытым.
/// 4. Каталог архива устроен как каталог организации: своя база ТОЙ ЖЕ схемы и подкаталог
///    файлов проектов — иначе соседям по выпуску пришлось бы писать своё чтение архива.
/// 5. Открыть/закрыть — распаковка и упаковка .zip средствами .NET; текущий архив нельзя
///    ни закрыть, ни удалить.
/// 6. Удаление снимает файлы с ЭТОГО сервера, а запись реестра остаётся: архив есть
///    на других серверах.
/// </summary>
public sealed class T40S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveService Service(bool conductor = true) =>
        new(_f.Db, _f.Events, conductor
            ? ServerScope.Standalone()
            : new ServerScope(() => "srv-1", () => "S1", () => false));

    [Fact]
    public void Registry_Is_Replicated_And_Local_State_Is_Not()
    {
        Assert.Contains("archives", ChangeLog.OrgTables);
        // ГЛАВНОЕ ограничение задачи: операции открыть/закрыть/удалить у каждого сервера свои
        Assert.DoesNotContain("archive_states", ChangeLog.OrgTables);
    }

    [Fact]
    public void Org_Schema_Is_V43()
    {
        Assert.Equal("48", _f.Db.Meta("schema_version"));
    }

    [Fact]
    public void Create_Makes_The_New_Archive_Current_And_The_Previous_One_Open()
    {
        var service = Service();
        var first = service.Create("arc2026a", "Архив за первое полугодие", "", null);
        Assert.True(first.IsCurrent);
        Assert.Equal(ArchiveStates.Open, first.State);
        Assert.Equal("ARC-1", first.DisplayId);
        Assert.Equal(ArchiveRuleModes.Common, first.RulesMode);

        var second = service.Create("arc2026b", "Архив за второе полугодие",
            ArchiveRuleModes.Copy, null);
        Assert.True(second.IsCurrent);
        Assert.Equal(ArchiveRuleModes.Copy, second.RulesMode);
        Assert.Equal(second.Id, service.Current()!.Id);

        // прежний текущий стал ПРОСТО ОТКРЫТЫМ: закрывать его никто не обязан, но теперь можно
        var again = service.Get(first.Id)!;
        Assert.False(again.IsCurrent);
        Assert.Equal(ArchiveStates.Open, again.State);
        Assert.Single(service.List().Where(a => a.IsCurrent));
    }

    [Fact]
    public void Archive_Directory_Looks_Like_An_Organization_Directory()
    {
        var service = Service();
        var archive = service.Create("arc2026c", "Архив", "", null);
        var dbFile = service.DbFileOf(archive.Code);
        Assert.True(File.Exists(dbFile));
        Assert.True(Directory.Exists(service.ProjectsDirOf(archive.Code)));
        Assert.Equal(Path.Combine(_f.Db.DataDir, Archives.Dir, archive.Code), archive.Path);

        // база архива — ТОЙ ЖЕ схемы: соседи по выпуску читают архив готовыми сервисами
        var archiveDb = new Database(service.DirOf(archive.Code), Archives.DbFile);
        Assert.Equal("48", archiveDb.Meta("schema_version"));
        using var conn = archiveDb.Open();
        var tasks = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tasks'");
        Assert.Equal(1, tasks);
    }

    [Fact]
    public void Close_Packs_The_Directory_And_Open_Unpacks_It_Back()
    {
        var service = Service();
        var old = service.Create("arc2026d", "Старый", "", null);
        // положим в архив файл — после круга «закрыли-открыли» он обязан быть на месте
        var marker = Path.Combine(service.ProjectsDirOf(old.Code), "marker.txt");
        File.WriteAllText(marker, "данные архива");
        service.Create("arc2026e", "Новый", "", null);   // прежний перестал быть текущим

        var closed = service.Close(old.Id, null);
        Assert.Equal(ArchiveStates.Closed, closed.State);
        Assert.False(Directory.Exists(service.DirOf(old.Code)));
        Assert.True(File.Exists(service.ZipOf(old.Code)));
        Assert.Equal(ArchiveStates.Closed, service.Get(old.Id)!.State);

        var opened = service.Open(old.Id, null);
        Assert.Equal(ArchiveStates.Open, opened.State);
        Assert.False(File.Exists(service.ZipOf(old.Code)));
        Assert.True(File.Exists(marker));
        Assert.Equal("данные архива", File.ReadAllText(marker));
    }

    [Fact]
    public void The_Current_Archive_Can_Be_Neither_Closed_Nor_Deleted()
    {
        var service = Service();
        var current = service.Create("arc2026f", "Текущий", "", null);
        Assert.Throws<InvalidOperationException>(() => service.Close(current.Id, null));
        Assert.Throws<InvalidOperationException>(() => service.Remove(current.Id, null));
        Assert.True(Directory.Exists(service.DirOf(current.Code)));
    }

    [Fact]
    public void Remove_Clears_The_Files_But_Keeps_The_Registry_Row()
    {
        var service = Service();
        var old = service.Create("arc2026g", "Старый", "", null);
        service.Create("arc2026h", "Новый", "", null);

        var removed = service.Remove(old.Id, null);
        Assert.Equal(ArchiveStates.Deleted, removed.State);
        Assert.False(Directory.Exists(service.DirOf(old.Code)));
        Assert.False(File.Exists(service.ZipOf(old.Code)));
        // запись остаётся: архив есть на других серверах, оттуда его можно скачать (T-47-S0)
        var row = service.Get(old.Id);
        Assert.NotNull(row);
        Assert.Equal(ArchiveStates.Deleted, row!.State);
        Assert.Equal("", row.Path);
        Assert.Contains(service.List(), a => a.Id == old.Id);
        // открыть удалённый нечем — внятный отказ, а не пустой каталог
        Assert.Throws<InvalidOperationException>(() => service.Open(old.Id, null));
    }

    [Fact]
    public void An_Archive_Is_Created_Only_On_The_Conductor()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Service(conductor: false).Create("arc2026i", "Чужой", "", null));
        Assert.Equal(Loc.T("msg.arc.1"), ex.Message);
        Assert.False(Directory.Exists(Service().DirOf("arc2026i")));
    }

    [Fact]
    public void Bad_Code_Empty_Name_And_Duplicates_Are_Refused()
    {
        var service = Service();
        Assert.Throws<ArgumentException>(() => service.Create("архив 2026", "Имя", "", null));
        Assert.Throws<ArgumentException>(() => service.Create("arc2026j", "  ", "", null));
        Assert.Throws<ArgumentException>(
            () => service.Create("arc2026j", "Имя", "неведомый-способ", null));
        service.Create("arc2026j", "Имя", "", null);
        Assert.Throws<ArgumentException>(() => service.Create("arc2026j", "Второй", "", null));
        Assert.Single(service.List());
    }

    [Fact]
    public void Local_State_Row_Is_Written_Per_Server()
    {
        var service = Service();
        var archive = service.Create("arc2026k", "Архив", "", null);
        using var conn = _f.Db.Open();
        var state = Sql.Scalar<string>(conn, null,
            "SELECT state FROM archive_states WHERE archive_id=@a AND server_id=@s",
            ("@a", archive.Id), ("@s", ServerScope.Standalone().ServerId));
        Assert.Equal(ArchiveStates.Open, state);
    }

    [Fact]
    public void Messages_Of_The_Section_Are_In_Both_Dictionaries()
    {
        for (var i = 1; i <= 21; i++)
        {
            var key = "msg.arc." + i;
            Assert.NotEqual(key, Loc.In("ru", key));
            Assert.NotEqual(key, Loc.In("en", key));
        }
    }
}
