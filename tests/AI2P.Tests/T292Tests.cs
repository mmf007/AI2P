using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-292: установщик пакетов сначала пробует распаковать архив СРЕДСТВАМИ .NET
/// (System.IO.Compression и System.Formats.Tar) и только потом зовёт внешние утилиты.
/// Вид архива определяется по подписи файла, а не по объявлению unpack в справочнике пакетов.
/// </summary>
public sealed class T292Tests : IDisposable
{
    private const string QwenId = "6f1a45e0-0d31-4c65-9a01-000000000008";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t292-" + Guid.NewGuid().ToString("N"));

    public T292Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // временный каталог прогона — не повод ронять тест
        }
    }

    // --- определение вида архива ---

    [Fact]
    public void Detect_Reads_The_Signature_And_Not_The_Name()
    {
        // zip, названный «.7z» — ровно то, что случается с ассетами релизов
        var named7z = Path.Combine(_dir, "pack.7z");
        File.WriteAllBytes(named7z, Zip(("a.txt", "текст")));
        Assert.Equal("zip", ArchiveExtractor.Detect(named7z));

        Assert.Equal("tar", ArchiveExtractor.Detect(Save(Tar(("a.txt", "текст")), "a.bin")));
        Assert.Equal("gzip", ArchiveExtractor.Detect(Save(Gzip(Tar(("a.txt", "т"))), "a.tgz")));
        Assert.Equal("7z", ArchiveExtractor.Detect(Save([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 0], "a.7z")));
        Assert.Equal("xz", ArchiveExtractor.Detect(Save([0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00], "a.xz")));
        Assert.Equal("bzip2", ArchiveExtractor.Detect(Save([0x42, 0x5A, 0x68, 0x39], "a.bz2")));
        Assert.Equal("zstd", ArchiveExtractor.Detect(Save([0x28, 0xB5, 0x2F, 0xFD], "a.zst")));
        Assert.Equal("", ArchiveExtractor.Detect(Save(Encoding.UTF8.GetBytes("просто файл"), "a.txt")));
        Assert.Equal("", ArchiveExtractor.Detect(Path.Combine(_dir, "которого-нет.zip")));

        // сам по себе .NET умеет ровно три вида — остальное остаётся внешним утилитам
        Assert.Equal(new[] { "zip", "gzip", "tar" }, ArchiveExtractor.Supported);
        Assert.True(ArchiveExtractor.CanExtract(named7z));
        Assert.False(ArchiveExtractor.CanExtract(Save([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], "b.7z")));
    }

    // --- распаковка средствами .NET ---

    [Fact]
    public void Zip_Is_Unpacked_Without_Any_External_Tool()
    {
        var archive = Save(Zip(("tools/llama-server.exe", "двоичный файл"), ("readme.md", "как есть")), "d.zip");
        var target = Path.Combine(_dir, "out-zip");

        Assert.True(ArchiveExtractor.TryExtract(archive, target, out var error));

        Assert.Equal("", error);
        Assert.Equal("двоичный файл", File.ReadAllText(Path.Combine(target, "tools", "llama-server.exe")));
        Assert.Equal("как есть", File.ReadAllText(Path.Combine(target, "readme.md")));
    }

    [Fact]
    public void Tar_And_Tar_Gz_Are_Unpacked_By_Dotnet()
    {
        var tar = Save(Tar(("bin/tool", "первый")), "d.tar");
        var targetTar = Path.Combine(_dir, "out-tar");
        Assert.True(ArchiveExtractor.TryExtract(tar, targetTar, out _));
        Assert.Equal("первый", File.ReadAllText(Path.Combine(targetTar, "bin", "tool")));

        // .tar.gz — два слоя, и оба закрываются рантаймом
        var targz = Save(Gzip(Tar(("bin/tool", "второй"))), "d.tar.gz");
        var targetGz = Path.Combine(_dir, "out-targz");
        Assert.True(ArchiveExtractor.TryExtract(targz, targetGz, out _));
        Assert.Equal("второй", File.ReadAllText(Path.Combine(targetGz, "bin", "tool")));
    }

    [Fact]
    public void Gz_Of_A_Single_File_Loses_Only_Its_Extension()
    {
        var archive = Save(Gzip(Encoding.UTF8.GetBytes("веса модели")), "model.gguf.gz");
        var target = Path.Combine(_dir, "out-gz");

        Assert.True(ArchiveExtractor.TryExtract(archive, target, out _));

        Assert.Equal("веса модели", File.ReadAllText(Path.Combine(target, "model.gguf")));
    }

    [Fact]
    public void Seven_Zip_Is_Honestly_Left_To_An_External_Tool()
    {
        var archive = Save([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 1, 2, 3, 4], "d.7z");

        Assert.False(ArchiveExtractor.TryExtract(archive, Path.Combine(_dir, "out-7z"), out var error));

        // пустая причина — «этот формат .NET не разбирает вовсе», а не «попытка сорвалась»
        Assert.Equal("", error);
    }

    [Fact]
    public void Broken_Archive_Names_The_Reason()
    {
        var archive = Save([0x50, 0x4B, 0x03, 0x04, 9, 9, 9, 9, 9, 9], "d.zip");

        Assert.False(ArchiveExtractor.TryExtract(archive, Path.Combine(_dir, "out-broken"), out var error));

        Assert.NotEqual("", error);
    }

    [Fact]
    public void Entry_Cannot_Escape_The_Target_Directory()
    {
        var archive = Save(Zip(("../чужой.txt", "выход наружу")), "evil.zip");
        var target = Path.Combine(_dir, "out-evil");

        Assert.False(ArchiveExtractor.TryExtract(archive, target, out var error));

        Assert.NotEqual("", error);
        Assert.False(File.Exists(Path.Combine(_dir, "чужой.txt")));
    }

    // --- установщик пакетов ---

    [Fact]
    public async Task Package_Declared_As_7z_But_Really_Zip_Is_Installed()
    {
        using var f = new StorageFixture();
        f.Models.Seed();
        var (service, dist, packages) = NewInstaller(f);
        // объявлено «7z», а в ассете лежит zip — раньше это упиралось во внешний архиватор
        WriteCatalog(f, "llama.7z", "7z");
        File.WriteAllBytes(Path.Combine(dist, "llama.7z"),
            Zip(("tools/llama-server.exe", "двоичный файл")));

        service.Start(QwenId);
        await WaitFinishedAsync(service, QwenId);

        var status = service.Status(QwenId);
        Assert.Equal("", status.Error);
        Assert.True(status.Installed);
        Assert.True(File.Exists(Path.Combine(packages, "llama", "tools", "llama-server.exe")));
    }

    [Fact]
    public async Task Unreadable_Archive_Ends_With_The_Hint_For_The_Human()
    {
        using var f = new StorageFixture();
        f.Models.Seed();
        var (service, dist, _) = NewInstaller(f);
        // подпись 7z, а внутри мусор: ни .NET, ни tar, ни 7-Zip его не возьмут
        WriteCatalog(f, "llama.7z", "7z");
        File.WriteAllBytes(Path.Combine(dist, "llama.7z"),
            [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 1, 2, 3, 4, 5, 6, 7, 8]);

        service.Start(QwenId);
        await WaitFinishedAsync(service, QwenId);

        var status = service.Status(QwenId);
        Assert.False(status.Installed);
        Assert.Contains("llama.7z", status.Error);
        Assert.Contains("руками", status.Error); // подсказка пакета из справочника
    }

    // --- помощники ---

    /// <summary>Установщик с отдельными каталогами дистрибутивов и пакетов (как в todo36_5).</summary>
    private static (ModelInstallService Service, string Dist, string Packages) NewInstaller(StorageFixture f)
    {
        var repo = Path.Combine(f.Dir, "models-repo");
        var dist = Path.Combine(f.Dir, "dist");
        var packages = Path.Combine(f.Dir, "packages");
        Directory.CreateDirectory(dist);
        return (new ModelInstallService(f.Models, f.Files, f.Events, () => repo, () => dist, () => packages),
            dist, packages);
    }

    /// <summary>
    /// Справочник пакетов из одного пакета и профайл модели, которой нужен только он.
    /// Ссылка ведёт в никуда намеренно: дистрибутив уже лежит в каталоге, проверяется распаковка.
    /// </summary>
    private static void WriteCatalog(StorageFixture f, string fileName, string unpack)
    {
        f.Files.WriteText(AiModelService.PackagesPath, $$"""
            {
              "packages": [
                {
                  "id": "llama.cpp", "name": "llama.cpp (тест)", "dir": "llama",
                  "check": "llama-server.exe", "hint": "Распакуйте архив руками.",
                  "files": [ { "name": "{{fileName}}", "url": "http://127.0.0.1:9/x", "unpack": "{{unpack}}" } ]
                }
              ]
            }
            """);
        f.Files.WriteText(f.Models.Get(QwenId)!.ProfilePath,
            """{"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"]}}""");
    }

    private static async Task WaitFinishedAsync(ModelInstallService service, string modelId)
    {
        for (var i = 0; i < 300; i++)
        {
            if (!service.Status(modelId).Running)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Установка не завершилась за 30 секунд");
    }

    /// <summary>Положить байты во временный файл прогона и вернуть путь.</summary>
    private string Save(byte[] bytes, string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Zip(params (string Path, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }
        return memory.ToArray();
    }

    private static byte[] Tar(params (string Path, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var writer = new TarWriter(memory, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
                };
                writer.WriteEntry(entry);
            }
        }
        return memory.ToArray();
    }

    private static byte[] Gzip(byte[] payload)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(payload, 0, payload.Length);
        }
        return memory.ToArray();
    }
}
