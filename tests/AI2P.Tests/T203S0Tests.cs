using System.IO.Compression;
using AI2P.Core;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-203-S0: ЗАКРЫТИЕ АРХИВА И РЕПЛИКАЦИЯ.
///
/// Жалоба заказчика: архив закрыли на S1 (каталог упакован в .zip и убран), репликация
/// перетащила .zip на S0, а каталог этого архива на S0 остался.
///
/// Причина: каталог архивов <c>arc</c> лежит ВНУТРИ каталога данных организации, и общая
/// репликация файлов забирала его целиком, хотя у архивов свой механизм переноса. Отсюда
/// две проверки:
/// <list type="bullet">
/// <item>каталог архивов из общей репликации файлов исключён — .zip больше не приезжает,
/// а содержимое чужого открытого архива больше не вычищается «удалением» файлов;</item>
/// <item>тот беспорядок, что уже лежит на дисках (каталог И .zip одновременно), чинится
/// сторожем: закрытие доводится до конца, каталог убирается.</item>
/// </list>
/// </summary>
public sealed class T203S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ArchiveService Service() => new(_f.Db, _f.Events, ServerScope.Standalone());

    // --- репликация файлов организации не трогает каталог архивов ---

    [Fact]
    public void Archives_Directory_Never_Replicates_With_The_Org_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai2p-t203-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, Archives.Dir, "a2026", Archives.ProjectsDir));
        File.WriteAllText(Path.Combine(root, "описание.md"), "рабочая среда");
        File.WriteAllText(Path.Combine(root, Archives.Dir, "a2026.zip"), "закрытый архив");
        File.WriteAllText(Path.Combine(root, Archives.Dir, "a2026", Archives.DbFile), "база архива");
        File.WriteAllText(Path.Combine(root, Archives.Dir, "a2026", Archives.ProjectsDir, "к.md"),
            "файл проекта архива");
        try
        {
            var entries = FileManifest.Scan(root, RepIgnore.Empty, skipService: true);

            Assert.Single(entries);
            Assert.Equal("описание.md", entries[0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Service_Paths_Are_Recognized_In_A_Foreign_Manifest()
    {
        // партнёр ПРЕЖНЕЙ версии назовёт эти пути в своём манифесте: без опознания сверка
        // сочла бы их удалёнными у нас и вычистила бы архивы у него
        Assert.True(FileManifest.IsServicePath("arc/a2026.zip"));
        Assert.True(FileManifest.IsServicePath("arc/a2026/projects/к.md"));
        Assert.True(FileManifest.IsServicePath(".trash/задача/description.md"));
        Assert.True(FileManifest.IsServicePath("ai/запрос-1.json"));
        Assert.True(FileManifest.IsServicePath("ai2p.db"));
        // а это обычные файлы человека, они реплицируются как раньше
        Assert.False(FileManifest.IsServicePath("описание.md"));
        Assert.False(FileManifest.IsServicePath("projects/P-1/arc.txt"));
        Assert.False(FileManifest.IsServicePath("projects/P-1/материалы/схема.png"));
    }

    // --- незаконченное закрытие доводится до конца ---

    [Fact]
    public void Archive_With_Both_A_Directory_And_A_Zip_Is_Settled_As_Closed()
    {
        var service = Service();
        var current = service.Create("a2026a", "Прошлый архив", "", null);
        service.Create("a2026b", "Текущий архив", "", null);   // прежний перестал быть текущим
        // ровно то, что видел заказчик: каталог на месте, а рядом лежит приехавший .zip
        ZipFile.CreateFromDirectory(service.DirOf(current.Code), service.ZipOf(current.Code),
            CompressionLevel.Fastest, includeBaseDirectory: false);
        Assert.True(Directory.Exists(service.DirOf(current.Code)));

        var fixedUp = service.SettleHalfClosed();

        Assert.Equal(1, fixedUp);
        Assert.False(Directory.Exists(service.DirOf(current.Code)));
        Assert.True(File.Exists(service.ZipOf(current.Code)));
        Assert.Equal(ArchiveStates.Closed, service.StateOf(current.Code));
        Assert.Equal(ArchiveStates.Closed, service.Get(current.Id)!.State);
    }

    [Fact]
    public void The_Current_Archive_Is_Never_Settled_Away()
    {
        // в текущий архив идёт перенос, и его каталог репликация заведёт заново:
        // трогать его нельзя даже при лишнем .zip рядом
        var service = Service();
        var current = service.Create("a2026c", "Текущий архив", "", null);
        ZipFile.CreateFromDirectory(service.DirOf(current.Code), service.ZipOf(current.Code),
            CompressionLevel.Fastest, includeBaseDirectory: false);

        Assert.Equal(0, service.SettleHalfClosed());
        Assert.True(Directory.Exists(service.DirOf(current.Code)));
    }

    [Fact]
    public void A_Tidy_Archive_Is_Left_Alone()
    {
        var service = Service();
        var first = service.Create("a2026d", "Прошлый архив", "", null);
        service.Create("a2026e", "Текущий архив", "", null);
        service.Close(first.Id, null);

        Assert.Equal(0, service.SettleHalfClosed());
        Assert.Equal(ArchiveStates.Closed, service.StateOf(first.Code));
        Assert.True(File.Exists(service.ZipOf(first.Code)));
    }
}
