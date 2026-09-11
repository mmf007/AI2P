using AI2P.Core.Api;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo44 / этап 44 (ТЗ v1.51): РЕПЛИКАЦИЯ ФАЙЛОВ. Манифест каталога, фильтр <c>.repignore</c>,
/// база сравнения (снимок последнего согласия) и конфликты — файл, изменившийся за интервал
/// репликации на ДВУХ серверах, не реплицируется, пока человек не выберет решение.
///
/// Сеть здесь ни при чём: проверяется механизм — что обходится, что фильтруется, что
/// считается изменившимся и что попадает в конфликт.
/// </summary>
public sealed class Todo44Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo44-" + Guid.NewGuid().ToString("N"));

    public Todo44Tests() => Directory.CreateDirectory(_dir);

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

    private string Make(string relativePath, string text = "x")
    {
        var full = Path.Combine(_dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    // --- фильтр .repignore: синтаксис .gitignore ---

    [Fact]
    public void Mask_Works_In_Any_Subdirectory()
    {
        var ignore = RepIgnore.Parse("*.tmp");

        Assert.True(ignore.IsIgnored("a.tmp"));
        Assert.True(ignore.IsIgnored("под/каталог/a.tmp"));
        Assert.False(ignore.IsIgnored("a.mp4"));
    }

    [Fact]
    public void Leading_Slash_Anchors_To_The_Root()
    {
        var ignore = RepIgnore.Parse("/сборка");

        Assert.True(ignore.IsIgnored("сборка", isDirectory: true));
        Assert.True(ignore.IsIgnored("сборка/итог.mp4"));
        // тот же каталог в глубине под правило не подходит: правило привязано к корню
        Assert.False(ignore.IsIgnored("проект/сборка/итог.mp4"));
    }

    [Fact]
    public void Directory_Rule_Hides_The_Directory()
    {
        var ignore = RepIgnore.Parse("кэш/");

        Assert.True(ignore.IsIgnored("кэш", isDirectory: true));
        Assert.True(ignore.IsIgnored("под/кэш", isDirectory: true));
        // правило с завершающим слэшем к ФАЙЛУ не применяется — содержимое отсекается
        // тем, что в скрытый каталог обход не заходит (см. FileManifest)
        Assert.False(ignore.IsIgnored("кэш"));
    }

    [Fact]
    public void Negation_Brings_A_File_Back_And_The_Last_Rule_Wins()
    {
        var ignore = RepIgnore.Parse("*.mp4\n!итог.mp4");

        Assert.True(ignore.IsIgnored("черновик.mp4"));
        Assert.False(ignore.IsIgnored("итог.mp4"));
    }

    [Fact]
    public void Comments_And_Blank_Lines_Are_Skipped()
    {
        var ignore = RepIgnore.Parse("# это комментарий\n\n   \n*.log");

        Assert.True(ignore.IsIgnored("работа.log"));
        Assert.False(ignore.IsIgnored("это"));
    }

    [Fact]
    public void Double_Star_Crosses_Directories()
    {
        var ignore = RepIgnore.Parse("сборка/**/врем");

        Assert.True(ignore.IsIgnored("сборка/врем"));
        Assert.True(ignore.IsIgnored("сборка/a/b/врем"));
    }

    [Fact]
    public void Missing_Filter_Replicates_Everything()
    {
        var ignore = RepIgnore.Load(_dir);

        Assert.True(ignore.IsEmpty);
        Assert.False(ignore.IsIgnored("что-угодно.mp4"));
    }

    // --- манифест каталога ---

    [Fact]
    public void Manifest_Lists_Files_With_Size_And_Time()
    {
        Make("a.txt", "привет");
        Make("под/b.txt", "мир");

        var entries = FileManifest.Scan(_dir, RepIgnore.Empty, skipService: false);

        Assert.Equal(2, entries.Count);
        // пути всегда прямыми слэшами — иначе Windows и Linux не поняли бы друг друга
        Assert.Contains(entries, e => e.Path == "под/b.txt");
        Assert.All(entries, e => Assert.True(e.Size > 0));
        Assert.All(entries, e => Assert.EndsWith("Z", e.Mtime));
    }

    [Fact]
    public void Service_Directories_And_The_Database_Never_Replicate()
    {
        Make("описание.md");
        Make(".trash/удалённая/description.md");
        Make("ai/запрос-1.json");
        Make("ai2p.db");
        Make("ai2p.db-wal");

        var entries = FileManifest.Scan(_dir, RepIgnore.Empty, skipService: true);

        Assert.Single(entries);
        Assert.Equal("описание.md", entries[0].Path);
    }

    [Fact]
    public void Common_Folder_Keeps_Everything_The_User_Put_There()
    {
        // папка Common принадлежит пользователю: там и «ai», и «.trash» — обычные каталоги
        Make("ai/кадр.png");
        Make(".trash/старое.png");

        var entries = FileManifest.Scan(_dir, RepIgnore.Empty, skipService: false);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Ignored_Directory_Is_Not_Walked_At_All()
    {
        Make("итог.mp4");
        Make("кэш/1.bin");
        Make("кэш/глубже/2.bin");

        var entries = FileManifest.Scan(_dir, RepIgnore.Parse("кэш/"), skipService: false);

        Assert.Single(entries);
        Assert.Equal("итог.mp4", entries[0].Path);
    }

    [Fact]
    public void RepIgnore_Itself_Replicates_Unless_It_Excludes_Itself()
    {
        File.WriteAllText(Path.Combine(_dir, RepIgnore.FileName), "*.tmp");
        Make("a.tmp");
        Make("b.txt");

        var entries = FileManifest.Scan(_dir, RepIgnore.Load(_dir), skipService: false);

        // сам фильтр — часть договорённости между серверами, а не локальная настройка
        Assert.Contains(entries, e => e.Path == RepIgnore.FileName);
        Assert.DoesNotContain(entries, e => e.Path == "a.tmp");

        File.WriteAllText(Path.Combine(_dir, RepIgnore.FileName), "*.tmp\n.repignore");
        var hidden = FileManifest.Scan(_dir, RepIgnore.Load(_dir), skipService: false);
        Assert.DoesNotContain(hidden, e => e.Path == RepIgnore.FileName);
    }

    // --- хэши: считаются только у изменившихся ---

    [Fact]
    public void Known_Hash_Is_Reused_While_Size_And_Time_Match()
    {
        Make("большой.mp4", "содержимое");
        var entries = FileManifest.Scan(_dir, RepIgnore.Empty, skipService: false);
        var asked = 0;

        FileManifest.FillHashes(_dir, entries, (path, size, mtime) =>
        {
            asked++;
            return path == "большой.mp4" && size > 0 && mtime.Length > 0 ? "известный-хэш" : null;
        });

        // гигабайтный файл не должен хэшироваться каждый сеанс — в этом весь смысл
        Assert.Equal(1, asked);
        Assert.Equal("известный-хэш", entries[0].Hash);
    }

    [Fact]
    public void Unknown_File_Is_Hashed_For_Real()
    {
        Make("новый.txt", "содержимое");
        var entries = FileManifest.Scan(_dir, RepIgnore.Empty, skipService: false);

        FileManifest.FillHashes(_dir, entries, (_, _, _) => null);

        Assert.Equal(64, entries[0].Hash.Length);   // sha256 в hex
        Assert.Equal(FileManifest.Hash(Path.Combine(_dir, "новый.txt")), entries[0].Hash);
    }

    // --- защита от выхода за пределы каталога (ТЗ гл. 12) ---

    [Fact]
    public void Path_Outside_The_Root_Is_Refused()
    {
        // пути называет ПАРТНЁР, и без проверки он записал бы файл куда угодно
        Assert.Null(FileManifest.Resolve(_dir, "../снаружи.txt"));
        Assert.Null(FileManifest.Resolve(_dir, "под/../../снаружи.txt"));
        Assert.Null(FileManifest.Resolve(_dir, Path.Combine(Path.GetTempPath(), "чужой.txt")));
        Assert.NotNull(FileManifest.Resolve(_dir, "под/свой.txt"));
    }

    // --- база сравнения и конфликты ---

    private FileSyncStateService Sync()
    {
        var db = new Database(Path.Combine(_dir, "_srv"), "server.db", DatabaseKind.Server);
        db.Init("unid-S0");
        return new FileSyncStateService(db);
    }

    [Fact]
    public void Baseline_Remembers_What_Both_Sides_Agreed_On()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.CommonFiles, "PRJ-1");

        sync.RememberAll("unid-S1", key,
        [
            new FileEntry { Path = "итог.mp4", Size = 100, Mtime = "2026-08-03T10:00:00.0000000Z", Hash = "aaa" },
        ]);

        var baseline = sync.Baseline("unid-S1", key);
        Assert.Single(baseline);
        Assert.Equal("aaa", baseline["итог.mp4"].Hash);
        // база своя у каждой пары: другой партнёр про это согласие не знает
        Assert.Empty(sync.Baseline("unid-S2", key));
    }

    [Fact]
    public void Forgetting_Removes_The_File_From_The_Baseline()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.OrgFiles, "org-1");
        sync.RememberAll("unid-S1", key,
            [new FileEntry { Path = "a.md", Size = 1, Mtime = "t", Hash = "h" }]);

        sync.Forget("unid-S1", key, ["a.md"]);

        Assert.Empty(sync.Baseline("unid-S1", key));
    }

    [Fact]
    public void Conflict_Is_Recorded_With_Both_Versions()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.CommonFiles, "PRJ-1");

        sync.Record("org-1", "unid-S1", ReplScopes.CommonFiles, key, "PRJ-1", "итог.mp4",
            left: new FileEntry { Path = "итог.mp4", Size = 10, Mtime = "t1", Hash = "l" },
            right: new FileEntry { Path = "итог.mp4", Size = 20, Mtime = "t2", Hash = "r" });

        var conflicts = sync.Conflicts("unid-S1");
        Assert.Single(conflicts);
        // левая сторона — всегда дирижёр: пара выглядит одинаково на обоих серверах
        Assert.Equal(10, conflicts[0].LeftSize);
        Assert.Equal(20, conflicts[0].RightSize);
        Assert.Equal("", conflicts[0].Resolution);
        Assert.Equal(1, sync.Count("unid-S1"));
    }

    [Fact]
    public void Resolution_Is_Dropped_When_The_File_Changes_Again()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.CommonFiles, "PRJ-1");
        void Record(string leftHash, string rightHash) =>
            sync.Record("org-1", "unid-S1", ReplScopes.CommonFiles, key, "PRJ-1", "итог.mp4",
                new FileEntry { Path = "итог.mp4", Hash = leftHash },
                new FileEntry { Path = "итог.mp4", Hash = rightHash });

        Record("l1", "r1");
        sync.Resolve(sync.Conflicts("unid-S1")[0].Id, ReplFileResolutions.Left);
        Assert.Equal(ReplFileResolutions.Left, sync.Conflicts("unid-S1")[0].Resolution);

        // те же версии — решение остаётся
        Record("l1", "r1");
        Assert.Equal(ReplFileResolutions.Left, sync.Conflicts("unid-S1")[0].Resolution);

        // файл переписали — решение относится уже не к тому содержимому
        Record("l2", "r1");
        Assert.Equal("", sync.Conflicts("unid-S1")[0].Resolution);
    }

    [Fact]
    public void Unknown_Resolution_Is_Refused()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.OrgFiles, "org-1");
        sync.Record("org-1", "unid-S1", ReplScopes.OrgFiles, key, "", "a.md",
            new FileEntry { Path = "a.md" }, new FileEntry { Path = "a.md" });
        var id = sync.Conflicts("unid-S1")[0].Id;

        Assert.Throws<ArgumentException>(() => sync.Resolve(id, "удалить-всё"));
        Assert.True(ReplFileResolutions.IsKnown(ReplFileResolutions.RenameRight));
    }

    [Fact]
    public void Cleared_Conflict_Disappears()
    {
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.OrgFiles, "org-1");
        sync.Record("org-1", "unid-S1", ReplScopes.OrgFiles, key, "", "a.md",
            new FileEntry { Path = "a.md" }, new FileEntry { Path = "a.md" });

        sync.Clear("org-1", "unid-S1", key, "a.md");

        Assert.Empty(sync.Conflicts("unid-S1"));
    }

    [Fact]
    public void Known_Hashes_Are_Shared_Between_Peers()
    {
        // пассивная сторона отдаёт манифест, не зная, кто спрашивает: содержимое файла
        // от партнёра не зависит, поэтому хэш переиспользуется от любой известной пары
        var sync = Sync();
        var key = FileSyncStateService.ScopeKey(ReplScopes.CommonFiles, "PRJ-1");
        sync.RememberAll("unid-S1", key,
            [new FileEntry { Path = "итог.mp4", Size = 100, Mtime = "t", Hash = "aaa" }]);

        var known = sync.KnownHashes(key);

        Assert.Equal("aaa", known["итог.mp4|100|t"]);
    }

    // --- переименование проигравшей копии ---

    [Fact]
    public void Rename_Adds_The_Server_Code_To_The_Name()
    {
        Assert.Equal("итог~S1.mp4",
            AI2P.Server.Org.FileReplicationService.WithSuffix("итог.mp4", "S1"));
        Assert.Equal("кадры/итог~S0.mp4",
            AI2P.Server.Org.FileReplicationService.WithSuffix("кадры/итог.mp4", "S0"));
        Assert.Equal("README~S1",
            AI2P.Server.Org.FileReplicationService.WithSuffix("README", "S1"));
    }

    // --- время изменения переносится вместе с файлом ---

    [Fact]
    public void Copied_File_Keeps_The_Source_Time()
    {
        var path = Make("итог.mp4", "кадры");
        var source = new DateTime(2026, 8, 1, 12, 30, 0, DateTimeKind.Utc);

        AI2P.Server.Org.FileReplicationService.SetMtime(path, Sql.ToDb(source));

        // иначе следующий сеанс счёл бы файл изменившимся на обеих сторонах — и это был бы
        // конфликт на ровном месте
        Assert.Equal(source, File.GetLastWriteTimeUtc(path), TimeSpan.FromSeconds(1));
    }
}
