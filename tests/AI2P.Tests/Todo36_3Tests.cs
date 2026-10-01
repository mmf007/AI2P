using System.Net;
using System.Net.Sockets;
using System.Text;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo36_3 (ТЗ v1.40): репозиторий моделей + общий механизм установки локальных моделей —
/// манифест install в профайле, состояние по файлам с точными размерами, фоновая загрузка
/// с докачкой (HTTP Range), первая модель с манифестом — Kandinsky-5.0-T2V-Lite-sft-5s.
/// </summary>
public sealed class Todo36_3Tests : IDisposable
{
    private readonly StorageFixture _f = new();
    private readonly string _repoDir;

    public Todo36_3Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private ModelInstallService NewService() => new(_f.Models, _f.Files, _f.Events, () => _repoDir);

    private const string KandinskyId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    // --- сид справочника ---

    [Fact]
    public void Seed_Adds_Kandinsky_With_Install_Manifest_And_Skills()
    {
        var model = _f.Models.Get(KandinskyId);
        Assert.NotNull(model);
        Assert.Equal("Kandinsky-5.0-T2V-Lite-sft-5s", model!.Name);
        Assert.False(model.IsCustom);

        // манифест установки: группа Kandinsky-5 и 4 файла с точными размерами
        var manifest = NewService().Manifest(KandinskyId);
        Assert.NotNull(manifest);
        Assert.Equal("Kandinsky-5", manifest!.Group);
        Assert.Equal(4, manifest.Files.Count);
        Assert.Equal("Kandinsky-5.0-T2V-Lite-sft-5s.safetensors", manifest.Files[0].Name);
        Assert.Equal(4573130528 + 9384670680 + 246144152 + 492986478, manifest.TotalSize);

        // декларация: навыки видео из отчёта todo36
        var scope = _f.Files.ReadText(model.CapabilitiesPath);
        Assert.Contains("video-generate", scope);
        Assert.Contains("video-animate", scope);
    }

    [Fact]
    public void Cloud_Models_Have_No_Manifest()
    {
        var service = NewService();
        // облачные модели дистрибутива не ставятся локально — кнопки установки у них нет
        Assert.Null(service.Manifest("6f1a45e0-0d31-4c65-9a01-000000000001"));
        // а общий список статусов содержит только модели с манифестом (todo36_5: Kandinsky
        // и локальная Qwen3.6-35B-A3B; T-214 добавил ещё три локальные — 27B-модели и Glimmer,
        // T-258 — вторую Kandinsky, «изображение → видео», T-241 — три локальные модели
        // изображений через ComfyUI, T-15-S0 — девять локальных видео-моделей,
        // T-18-S0 — три варианта ACE-Step 1.5 XL, звук, T-19-S0 — ещё шесть моделей
        // изображений: FLUX.2 klein 4B в двух режимах, FLUX.2 dev, Kandinsky Image Lite,
        // SD 3.5 Large и SDXL 1.0, T-20-S0 — три локальные 3D-модели: Hunyuan3D 2.1,
        // TRELLIS 2 и TripoSplat, T-347-S0 — лёгкая локальная Qwen3.5-4B для роли суфлёра)
        var all = service.StatusAll();
        Assert.Equal(31, all.Count);
        Assert.Contains(all, s => s.ModelId == KandinskyId);
    }

    // --- разбор манифеста ---

    [Fact]
    public void Manifest_Parse_Ignores_Broken_Entries_And_Missing_Section()
    {
        Assert.Null(ModelInstallManifest.Parse("""{"provider":"anthropic"}"""));
        // файлы без имени/URL/размера отбрасываются; без валидных файлов манифеста нет
        Assert.Null(ModelInstallManifest.Parse(
            """{"install":{"group":"G","files":[{"name":"a.bin"},{"url":"http://x","size":5}]}}"""));

        var manifest = ModelInstallManifest.Parse(
            """{"install":{"group":"G","files":[{"name":"a.bin","url":"http://x/a","size":10}]}}""");
        Assert.NotNull(manifest);
        Assert.Equal(10, manifest!.TotalSize);
    }

    // --- состояние установки по файлам и размерам ---

    [Fact]
    public void Status_Detects_Done_Partial_And_Missing_Files()
    {
        var service = NewService();
        WriteProfileWithManifest(service, ("a.bin", 4), ("b.bin", 8), ("c.bin", 6));
        var dir = Path.Combine(_repoDir, "models", "Test-Group");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[4]); // готов
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[3]); // частично (докачка)

        var status = service.Status(KandinskyId);
        Assert.True(status.HasManifest);
        Assert.False(status.Installed);
        Assert.Equal(18, status.TotalSize);
        Assert.Equal(7, status.DownloadedSize); // 4 + 3
        Assert.Equal(["done", "partial", "missing"], status.Files.Select(f => f.State));

        // все файлы точного размера — модель «установлена», кнопка станет «Обновить»
        File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[8]);
        File.WriteAllBytes(Path.Combine(dir, "c.bin"), new byte[6]);
        Assert.True(service.Status(KandinskyId).Installed);
    }

    [Fact]
    public void Status_File_Bigger_Than_Expected_Is_Not_Done()
    {
        var service = NewService();
        WriteProfileWithManifest(service, ("a.bin", 4));
        var dir = Path.Combine(_repoDir, "models", "Test-Group");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[9]); // битый (длиннее манифеста)

        var status = service.Status(KandinskyId);
        Assert.False(status.Installed);
        Assert.Equal("partial", status.Files[0].State);
        Assert.Equal(4, status.DownloadedSize); // не больше ожидаемого размера
    }

    // --- загрузка с докачкой (локальный HTTP-сервер, как в todo28 — без внешней сети) ---

    [Fact]
    public async Task Install_Downloads_Missing_And_Resumes_Partial()
    {
        var payload = Encoding.ASCII.GetBytes("0123456789ABCDEF"); // 16 байт
        using var server = new RangeHttpServer(payload);
        var service = NewService();
        WriteProfileWithManifest(service, url: server.Url, ("whole.bin", 16), ("part.bin", 16));

        // part.bin скачан наполовину (например, вручную curl-ом до появления кнопки)
        var dir = Path.Combine(_repoDir, "models", "Test-Group");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "part.bin"), payload[..10]);

        service.Start(KandinskyId);
        await WaitInstalledAsync(service);

        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(dir, "whole.bin")));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(dir, "part.bin")));
        // whole.bin качался целиком, part.bin — докачкой с 10-го байта (Range)
        Assert.Contains(server.Requests, r => r.File == "whole.bin" && r.RangeFrom is null);
        Assert.Contains(server.Requests, r => r.File == "part.bin" && r.RangeFrom == 10);

        var status = service.Status(KandinskyId);
        Assert.True(status.Installed);
        Assert.False(status.Running);
        Assert.Equal("", status.Error);
    }

    [Fact]
    public async Task Install_Reports_Error_When_Server_Fails()
    {
        using var server = new RangeHttpServer(new byte[4], failAll: true);
        var service = NewService();
        WriteProfileWithManifest(service, url: server.Url, ("a.bin", 4));

        service.Start(KandinskyId);
        await WaitFinishedAsync(service);

        var status = service.Status(KandinskyId);
        Assert.False(status.Installed);
        Assert.Contains("500", status.Error);
    }

    // --- помощники ---

    /// <summary>Подменить профайл Kandinsky тестовым манифестом (группа Test-Group).</summary>
    private void WriteProfileWithManifest(ModelInstallService service,
        params (string Name, long Size)[] files) =>
        WriteProfileWithManifest(service, url: "http://127.0.0.1:9/", files);

    private void WriteProfileWithManifest(ModelInstallService service, string url,
        params (string Name, long Size)[] files)
    {
        var model = _f.Models.Get(KandinskyId)!;
        var fileJson = string.Join(",", files.Select(f =>
            $$"""{"size":{{f.Size}},"name":"{{f.Name}}","url":"{{url}}{{f.Name}}"}"""));
        _f.Files.WriteText(model.ProfilePath,
            $$"""{"provider":"comfyui","install":{"group":"Test-Group","files":[{{fileJson}}] } }""");
    }

    private async Task WaitInstalledAsync(ModelInstallService service)
    {
        await WaitFinishedAsync(service);
        Assert.True(service.Status(KandinskyId).Installed,
            "Загрузка завершилась, но модель не собралась: " + service.Status(KandinskyId).Error);
    }

    private async Task WaitFinishedAsync(ModelInstallService service)
    {
        for (var i = 0; i < 100; i++)
        {
            if (!service.Status(KandinskyId).Running)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Загрузка не завершилась за 10 секунд");
    }

    /// <summary>
    /// Мини-HTTP-сервер на TcpListener (паттерн todo28): отдаёт один и тот же payload по любому
    /// имени файла, понимает Range (206 с хвостом), запоминает запросы для проверок.
    /// </summary>
    private sealed class RangeHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _payload;
        private readonly bool _failAll;
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }
        public List<(string File, long? RangeFrom)> Requests { get; } = [];

        public RangeHttpServer(byte[] payload, bool failAll = false)
        {
            _payload = payload;
            _failAll = failAll;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/";
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
                    var file = "";
                    long? rangeFrom = null;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        if (line.StartsWith("GET ", StringComparison.Ordinal))
                        {
                            file = line.Split(' ')[1].TrimStart('/');
                        }
                        else if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                        {
                            rangeFrom = long.Parse(line["Range: bytes=".Length..].TrimEnd('-'));
                        }
                    }
                    lock (Requests)
                    {
                        Requests.Add((file, rangeFrom));
                    }

                    byte[] body;
                    string header;
                    if (_failAll)
                    {
                        body = [];
                        header = "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    }
                    else if (rangeFrom is { } from)
                    {
                        body = _payload[(int)from..];
                        header = $"HTTP/1.1 206 Partial Content\r\nContent-Length: {body.Length}\r\n" +
                                 $"Content-Range: bytes {from}-{_payload.Length - 1}/{_payload.Length}\r\n" +
                                 "Connection: close\r\n\r\n";
                    }
                    else
                    {
                        body = _payload;
                        header = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    }
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                    await stream.WriteAsync(body);
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
