using System.Net;
using System.Net.Sockets;
using System.Text;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo36_6 (ТЗ v1.43): доработки по итогам живой проверки todo36_5 — переименование
/// моделей дистрибутива у существующих установок (Claude-Opus-4.8 → Claude-Opus-5.0),
/// удаление кастомной записи справочника (только записи, файлы остаются) и размер/прогресс
/// пакетов в окне установки.
/// </summary>
public sealed class Todo36_6Tests : IDisposable
{
    private const string OpusId = "6f1a45e0-0d31-4c65-9a01-000000000002";
    private const string OpusCliId = "6f1a45e0-0d31-4c65-9a01-000000000006";
    private const string QwenId = "6f1a45e0-0d31-4c65-9a01-000000000008";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;
    private readonly string _distDir;
    private readonly string _packagesDir;

    public Todo36_6Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _distDir = Path.Combine(_f.Dir, "dist");
        _packagesDir = Path.Combine(_f.Dir, "packages");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private ModelInstallService NewInstalls() =>
        new(_f.Models, _f.Files, _f.Events, () => _repoDir, () => _distDir, () => _packagesDir);

    // --- переименование моделей дистрибутива на существующей установке ---

    [Fact]
    public void Seed_Renames_Existing_Distribution_Models()
    {
        // как на установке пользователя: записи созданы прежней версией со старыми именами
        Rename(OpusId, "Claude-Opus-4.8");
        Rename(OpusCliId, "Claude-Opus-4.8_cli");

        _f.Models.Seed();

        Assert.Equal("Claude-Opus-5.0", _f.Models.Get(OpusId)!.Name);
        Assert.Equal("Claude-Opus-5.0_cli", _f.Models.Get(OpusCliId)!.Name);
        // id записи не менялся — ссылки исполнителей на модель уцелели
        Assert.DoesNotContain("Claude-Opus-4.8", _f.Models.List().Select(m => m.Name));
        // переименование попало в журнал работ
        Assert.Contains(_f.Events.Query(eventType: "model.updated", limit: 200),
            e => e.EntityId == OpusId);
    }

    [Fact]
    public void Seed_Rename_Frees_Name_Taken_By_Custom_Model()
    {
        Rename(OpusId, "Claude-Opus-4.8");
        // пользователь успел завести свою модель с новым именем — она уступает имя дистрибутиву
        var custom = _f.Models.Create(new AiModel { Name = "Claude-Opus-5.0-моя" }, null);
        Rename(custom.Id, "Claude-Opus-5.0");

        _f.Models.Seed();

        Assert.Equal("Claude-Opus-5.0", _f.Models.Get(OpusId)!.Name);
        Assert.Equal("Claude-Opus-5.0 (инсталлятор)", _f.Models.Get(custom.Id)!.Name);
    }

    [Fact]
    public void Seed_Does_Not_Touch_Names_That_Already_Match()
    {
        var before = _f.Models.Get(OpusId)!.UpdatedAt;
        _f.Models.Seed();
        Assert.Equal("Claude-Opus-5.0", _f.Models.Get(OpusId)!.Name);
        Assert.Equal(before, _f.Models.Get(OpusId)!.UpdatedAt); // лишней записи в БД нет
    }

    // --- удаление кастомной записи справочника ---

    [Fact]
    public void Custom_Model_Is_Deleted_From_Catalog_But_Files_Stay()
    {
        var model = _f.Models.Create(new AiModel { Name = "Своя модель" }, null);
        _f.Files.WriteText(model.ProfilePath, """{"provider":"openai-compatible"}""");
        var profileAbs = _f.Files.Abs(model.ProfilePath);

        _f.Models.Delete(model.Id, null);

        Assert.DoesNotContain(_f.Models.List(), m => m.Id == model.Id);
        Assert.NotNull(_f.Models.Get(model.Id)!.DeletedAt);   // удаление мягкое
        Assert.True(File.Exists(profileAbs));                 // файлы модели остаются
        Assert.Contains(_f.Events.Query(eventType: "model.deleted", limit: 200),
            e => e.EntityId == model.Id);
    }

    [Fact]
    public void Distribution_Model_Cannot_Be_Deleted()
    {
        var error = Assert.Throws<ArgumentException>(() => _f.Models.Delete(OpusId, null));
        Assert.Contains("дистрибутив", error.Message);
        Assert.Contains(_f.Models.List(), m => m.Id == OpusId);
    }

    [Fact]
    public void Model_Used_By_Executor_Cannot_Be_Deleted()
    {
        var model = _f.Models.Create(new AiModel { Name = "Занятая модель" }, null);
        _f.Executors.Create(new Executor
        {
            Nick = "агент",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            InternalName = model.Name,
        }, null);

        var error = Assert.Throws<ArgumentException>(() => _f.Models.Delete(model.Id, null));
        Assert.Contains("агент", error.Message);
        Assert.Contains(_f.Models.List(), m => m.Id == model.Id);
    }

    [Fact]
    public void Deleting_Twice_Is_Harmless()
    {
        var model = _f.Models.Create(new AiModel { Name = "Ещё одна" }, null);
        _f.Models.Delete(model.Id, null);
        _f.Models.Delete(model.Id, null); // повторное удаление молча ничего не делает
        Assert.DoesNotContain(_f.Models.List(), m => m.Id == model.Id);
    }

    // --- размер и прогресс пакета в окне установки ---

    [Fact]
    public async Task Package_Size_Is_Resolved_And_Progress_Is_Reported()
    {
        var payload = new byte[4096];
        using var server = new SizeHttpServer(payload);
        var service = NewInstalls();
        WritePackagesCatalog(server.Url + "llama.zip");
        _f.Files.WriteText(_f.Models.Get(QwenId)!.ProfilePath,
            """{"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"]}}""");

        // до запроса к серверу раздачи размер неизвестен
        var before = service.Status(QwenId).Packages[0];
        Assert.Equal(0, before.Size);
        Assert.Equal("missing", before.State);
        Assert.False(before.Running);

        // окно установки спрашивает размеры — HEAD к серверу раздачи
        var resolved = await service.ResolveSizesAsync(QwenId);
        Assert.Equal(payload.Length, resolved.Packages[0].Size);
        Assert.Equal(0, resolved.Packages[0].Downloaded);
        Assert.Contains("HEAD", server.Requests);

        // наполовину скачанный дистрибутив виден как прогресс пакета
        Directory.CreateDirectory(_distDir);
        File.WriteAllBytes(Path.Combine(_distDir, "llama.zip"), payload[..2048]);
        var half = service.Status(QwenId).Packages[0];
        Assert.Equal(2048, half.Downloaded);
        Assert.Equal("partial", half.State);
        Assert.False(half.Installed);
    }

    [Fact]
    public void Installed_Package_Reports_Full_Size_And_Done_State()
    {
        var service = NewInstalls();
        var llama = service.Packages().Find("llama.cpp")!;
        Directory.CreateDirectory(service.PackageDir(llama));
        File.WriteAllText(Path.Combine(service.PackageDir(llama), "llama-server.exe"), "");
        _f.Files.WriteText(_f.Models.Get(QwenId)!.ProfilePath,
            """{"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"]}}""");

        var package = service.Status(QwenId).Packages[0];
        Assert.True(package.Installed);
        Assert.Equal("done", package.State);
        // размера ещё не спрашивали — «—» в окне; после установки временный архив уже удалён,
        // потому прогресс не пересчитывается по каталогу дистрибутивов
        Assert.Equal(package.Size, package.Downloaded);
    }

    [Fact]
    public async Task Resolve_Survives_Unreachable_Server()
    {
        var service = NewInstalls();
        WritePackagesCatalog("http://127.0.0.1:9/llama.zip"); // порт 9 — соединения нет
        _f.Files.WriteText(_f.Models.Get(QwenId)!.ProfilePath,
            """{"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"]}}""");

        var status = await service.ResolveSizesAsync(QwenId); // не бросает — размер остаётся 0

        Assert.Equal(0, status.Packages[0].Size);
        Assert.False(status.Installed);
    }

    // --- помощники ---

    private void Rename(string modelId, string name)
    {
        using var conn = _f.Db.Open();
        Sql.Exec(conn, null, "UPDATE ai_models SET name=@name WHERE id=@id",
            ("@name", name), ("@id", modelId));
    }

    private void WritePackagesCatalog(string url) =>
        _f.Files.WriteText(AiModelService.PackagesPath, $$"""
            {
              "packages": [
                {
                  "id": "llama.cpp", "name": "llama.cpp (тест)", "dir": "llama",
                  "check": "llama-server.exe",
                  "files": [ { "name": "llama.zip", "url": "{{url}}", "unpack": "zip" } ]
                }
              ]
            }
            """);

    /// <summary>Мини-HTTP-сервер (паттерн todo28/36_3/36_5): отвечает на HEAD размером файла.</summary>
    private sealed class SizeHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _payload;
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }
        public List<string> Requests { get; } = [];

        public SizeHttpServer(byte[] payload)
        {
            _payload = payload;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                using (client)
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    string? line;
                    var method = "";
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        if (method.Length == 0 && line.Contains("HTTP/1.", StringComparison.Ordinal))
                        {
                            method = line.Split(' ')[0];
                        }
                    }
                    lock (Requests)
                    {
                        Requests.Add(method);
                    }
                    var header = $"HTTP/1.1 200 OK\r\nContent-Length: {_payload.Length}\r\n" +
                                 "Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                    if (method == "GET")
                    {
                        await stream.WriteAsync(_payload);
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
