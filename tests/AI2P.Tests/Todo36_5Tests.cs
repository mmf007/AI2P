using System.Net;
using System.Net.Sockets;
using System.Text;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo36_5 (ТЗ v1.42): справочник моделей — размещение (локальная / в облаке) и навыки
/// в списке; установщик — пакеты, необходимые для запуска (ComfyUI, llama.cpp), каталоги
/// дистрибутивов и пакетов, команда запуска в профайл после установки; встроенная локальная
/// модель Qwen3.6-35B-A3B-Local; ключи API облачных моделей (без ключа модель не активна);
/// переименование Claude-Opus-4.8 → Claude-Opus-5.0.
/// </summary>
public sealed class Todo36_5Tests : IDisposable
{
    private const string KandinskyId = "6f1a45e0-0d31-4c65-9a01-000000000007";
    private const string QwenId = "6f1a45e0-0d31-4c65-9a01-000000000008";
    private const string FableId = "6f1a45e0-0d31-4c65-9a01-000000000001";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;
    private readonly string _distDir;
    private readonly string _packagesDir;

    public Todo36_5Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _distDir = Path.Combine(_f.Dir, "dist");
        _packagesDir = Path.Combine(_f.Dir, "packages");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Установщик с каталогами по умолчанию (пакеты — в &lt;репозиторий&gt;/packages).</summary>
    private ModelInstallService NewDefaults() => new(_f.Models, _f.Files, _f.Events, () => _repoDir);

    /// <summary>Установщик с явно указанными каталогами дистрибутивов и пакетов.</summary>
    private ModelInstallService NewConfigured() =>
        new(_f.Models, _f.Files, _f.Events, () => _repoDir, () => _distDir, () => _packagesDir);

    private ModelKeyService NewKeys() =>
        new(_f.Models, _f.Files, _f.Secrets, _f.KeyStore, _f.OrgKeys, StorageFixture.OrgId);

    // --- справочник моделей: размещение и навыки ---

    [Fact]
    public void Seed_Marks_Local_And_Cloud_Models()
    {
        // облачные: подключение по API провайдера и по CLI
        Assert.False(_f.Models.Get(FableId)!.IsLocal);
        Assert.False(_f.Models.Get("6f1a45e0-0d31-4c65-9a01-000000000006")!.IsLocal); // Claude-Opus-5.0_cli
        Assert.False(_f.Models.Get("6f1a45e0-0d31-4c65-9a01-000000000003")!.IsLocal); // DeepSeek-V4-Pro
        // локальные: comfyui-модель и llama-server-модель с манифестом установки
        Assert.True(_f.Models.Get(KandinskyId)!.IsLocal);
        Assert.True(_f.Models.Get(QwenId)!.IsLocal);
    }

    [Fact]
    public void Local_Detection_Follows_Profile()
    {
        Assert.True(AiModelService.IsLocalProfile("""{"provider":"comfyui"}"""));
        Assert.True(AiModelService.IsLocalProfile("""{"launchCommand":"llama-server.exe"}"""));
        Assert.True(AiModelService.IsLocalProfile("""{"baseUrl":"http://localhost:8080/v1"}"""));
        Assert.True(AiModelService.IsLocalProfile("""{"baseUrl":"http://127.0.0.1:8188"}"""));
        Assert.True(AiModelService.IsLocalProfile("""{"install":{"group":"G"}}"""));
        Assert.False(AiModelService.IsLocalProfile("""{"baseUrl":"https://api.deepseek.com"}"""));
        Assert.False(AiModelService.IsLocalProfile("""{"provider":"anthropic"}"""));
        Assert.False(AiModelService.IsLocalProfile("сломанный json"));
    }

    [Fact]
    public void Local_Flag_Follows_Edited_Profile()
    {
        var model = _f.Models.Create(new AiModel { Name = "Своя локальная" }, null);
        Assert.False(model.IsLocal); // профайл пустой — считается облачной

        _f.Files.WriteText(model.ProfilePath,
            """{"provider":"openai-compatible","baseUrl":"http://localhost:9999/v1"}""");
        _f.Models.SyncLocalFlags();
        Assert.True(_f.Models.Get(model.Id)!.IsLocal);

        // правка модели из формы тоже пересчитывает признак по профайлу
        _f.Files.WriteText(model.ProfilePath,
            """{"provider":"openai-compatible","baseUrl":"https://api.example.com/v1"}""");
        var edited = _f.Models.Get(model.Id)!;
        _f.Models.Update(edited, null);
        Assert.False(_f.Models.Get(model.Id)!.IsLocal);
    }

    [Fact]
    public void List_With_Skills_Fills_Skills_From_Scope()
    {
        var kandinsky = _f.Models.ListWithSkills().Single(m => m.Id == KandinskyId);
        Assert.Equal(["video-generate", "video-animate"], kandinsky.Skills.Select(s => s.Name));
        Assert.Equal(76, kandinsky.Skills[0].Score);

        // у Claude — полный набор code/text/analyze (13 навыков, ТЗ v1.39)
        var fable = _f.Models.ListWithSkills().Single(m => m.Id == FableId);
        Assert.Equal(13, fable.Skills.Count);
        // оценка подтянута к таблице §5.1 отчёта T-213 (задание T-214): было 96
        Assert.Contains(fable.Skills, s => s is { Name: "code-write", Score: 98 });
    }

    // --- переименование моделей Claude ---

    [Fact]
    public void Seed_Renames_Opus_To_5_0_Everywhere()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();
        Assert.Contains("Claude-Opus-5.0", names);
        Assert.Contains("Claude-Opus-5.0_cli", names);
        Assert.DoesNotContain("Claude-Opus-4.8", names);
        Assert.DoesNotContain("Claude-Opus-4.8_cli", names);

        // внутренние вызовы: id модели у провайдера и модель подстраховки Fable
        var opus = _f.Models.List().Single(m => m.Name == "Claude-Opus-5.0");
        Assert.Equal("claude-opus-5", ModelProfile.Parse(_f.Files.ReadText(opus.ProfilePath)).Model);
        Assert.Contains("\"id\": \"claude-opus-5\"", _f.Files.ReadText(opus.CapabilitiesPath));
        var fable = _f.Models.Get(FableId)!;
        Assert.Equal(["claude-opus-5"],
            ModelProfile.Parse(_f.Files.ReadText(fable.ProfilePath)).Fallbacks);
    }

    // --- встроенная локальная модель Qwen ---

    [Fact]
    public void Seed_Adds_Qwen_With_Package_And_Launch_Template()
    {
        var model = _f.Models.Get(QwenId)!;
        Assert.Equal("Qwen3.6-35B-A3B-Local", model.Name);
        Assert.False(model.IsCustom);

        var manifest = NewDefaults().Manifest(QwenId)!;
        Assert.Equal("Qwen3.6", manifest.Group);
        Assert.Equal(["llama.cpp"], manifest.Packages);
        Assert.Single(manifest.Files);
        Assert.Equal("Qwen3.6-35B-A3B-UD-Q4_K_M.gguf", manifest.Files[0].Name);
        Assert.Equal(22134528992, manifest.Files[0].Size);
        Assert.Contains("{package:llama.cpp:llama-server.exe}", manifest.LaunchCommand);
        Assert.Contains("--port 8080", manifest.LaunchCommand);
    }

    [Fact]
    public void Seed_Renames_Custom_Model_That_Took_The_Distribution_Name()
    {
        // модель, добавленная старым инсталлятором install_local_model (кастомная запись)
        var custom = _f.Models.Create(new AiModel { Name = "Qwen3.6-35B-A3B-Local-новая" }, null);
        using (var conn = _f.Db.Open())
        {
            AI2P.Storage.Sql.Exec(conn, null,
                "UPDATE ai_models SET name='Qwen3.6-35B-A3B-Local' WHERE id=@id", ("@id", custom.Id));
            // как на старой установке: ни записи дистрибутива, ни её пер-серверной активности
            // (таблица ai_model_servers появилась в T-8-S1 — без её строк не сработает FK)
            AI2P.Storage.Sql.Exec(conn, null,
                "DELETE FROM ai_model_servers WHERE model_id=@id", ("@id", QwenId));
            AI2P.Storage.Sql.Exec(conn, null,
                "DELETE FROM ai_models WHERE id=@id", ("@id", QwenId));
        }

        _f.Models.Seed();

        Assert.Equal("Qwen3.6-35B-A3B-Local", _f.Models.Get(QwenId)!.Name);
        Assert.Equal("Qwen3.6-35B-A3B-Local (инсталлятор)", _f.Models.Get(custom.Id)!.Name);
    }

    // --- пакеты: справочник, каталоги, проверка установленности ---

    [Fact]
    public void Packages_Catalog_Seeded_With_Comfyui_And_Llama()
    {
        var catalog = NewDefaults().Packages();
        var comfy = catalog.Find("comfyui")!;
        Assert.Equal("ComfyUI", comfy.Dir);
        Assert.Equal("python.exe", comfy.Check);
        Assert.Single(comfy.Files);
        Assert.Equal("7z", comfy.Files[0].Unpack);

        var llama = catalog.Find("llama.cpp")!;
        Assert.Equal("llama-server.exe", llama.Check);
        Assert.Equal("ggml-org/llama.cpp", llama.Files[0].GitHubRepo);
        // архив CUDA-рантайма нужен только для сборки с CUDA
        Assert.True(llama.Files[1].MatchesFlavor("win-cuda-12.4-x64"));
        Assert.False(llama.Files[1].MatchesFlavor("win-cpu-x64"));
        Assert.True(llama.Files[0].MatchesFlavor("win-cpu-x64"));
    }

    [Fact]
    public void Package_Dirs_Default_To_Models_Repo_And_Temp()
    {
        var defaults = NewDefaults();
        Assert.Equal(Path.Combine(_repoDir, "packages"), defaults.PackagesRoot());
        Assert.True(defaults.DistIsTemporary());               // дистрибутивы — во временный каталог
        Assert.StartsWith(Path.GetTempPath(), defaults.DistRoot());

        var configured = NewConfigured();
        Assert.Equal(_packagesDir, configured.PackagesRoot());
        Assert.Equal(_distDir, configured.DistRoot());
        Assert.False(configured.DistIsTemporary());            // указанный каталог не чистится
    }

    [Fact]
    public void Package_Installed_Is_Detected_By_Check_File_Anywhere_Inside()
    {
        var service = NewConfigured();
        var comfy = service.Packages().Find("comfyui")!;
        Assert.False(service.IsPackageInstalled(comfy));

        // архив кладёт внутрь свой корневой каталог — признак ищется рекурсивно
        var inner = Path.Combine(service.PackageDir(comfy), "ComfyUI_windows_portable", "python_embeded");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "python.exe"), "");
        Assert.True(service.IsPackageInstalled(comfy));
        Assert.Equal(Path.Combine(inner, "python.exe"), service.FindInPackage(comfy, "python.exe"));
    }

    [Fact]
    public void Status_Reports_Packages_And_Blocks_Activation_Until_They_Are_Installed()
    {
        var service = NewConfigured();
        _f.Models.ActivationGuard = m => service.ActivationError(m.Id);

        // файлы весов на месте, пакета ComfyUI нет — модель ещё не установлена
        MaterializeKandinskyFiles(service);
        var status = service.Status(KandinskyId);
        Assert.Single(status.Packages);
        Assert.Equal("comfyui", status.Packages[0].Id);
        Assert.False(status.Packages[0].Installed);
        Assert.False(status.Installed);
        Assert.All(status.Files, f => Assert.Equal("done", f.State));

        var model = _f.Models.Get(KandinskyId)!;
        model.IsActive = true;
        var error = Assert.Throws<ArgumentException>(() => _f.Models.Update(model, null));
        Assert.Contains("ComfyUI", error.Message);

        // пакет появился — модель установлена и активируется
        InstallFakeComfyUi(service);
        Assert.True(service.Status(KandinskyId).Installed);
        model.IsActive = true;
        _f.Models.Update(model, null);
        Assert.True(_f.Models.Get(KandinskyId)!.IsActive);
    }

    [Fact]
    public void Launch_Command_Is_Resolved_And_Written_Into_Profile()
    {
        var service = NewConfigured();
        MaterializeKandinskyFiles(service);
        var pythonPath = InstallFakeComfyUi(service);

        var manifest = service.Manifest(KandinskyId)!;
        service.WriteLaunchCommand(KandinskyId, manifest);

        var profile = ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(KandinskyId)!.ProfilePath));
        Assert.Contains($"\"{pythonPath}\"", profile.LaunchCommand);
        Assert.Contains("main.py", profile.LaunchCommand);
        Assert.Contains(Path.Combine(_repoDir, ModelInstallService.ExtraModelPathsFile),
            profile.LaunchCommand);
        Assert.DoesNotContain("{package:", profile.LaunchCommand);
        // профайл остался валидным json и не потерял остальные поля
        Assert.Equal("comfyui", profile.Provider);
        // и попал в состояние установки для показа в окне загрузчика
        Assert.Equal(profile.LaunchCommand, service.Status(KandinskyId).LaunchCommand);
    }

    [Fact]
    public void Launch_Command_Of_Qwen_Points_To_Package_And_Weights()
    {
        var service = NewConfigured();
        var llama = service.Packages().Find("llama.cpp")!;
        var binDir = Path.Combine(service.PackageDir(llama), "build", "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "llama-server.exe"), "");

        var manifest = service.Manifest(QwenId)!;
        var command = service.ResolveLaunchCommand(manifest);

        Assert.StartsWith($"\"{Path.Combine(binDir, "llama-server.exe")}\"", command);
        Assert.Contains(Path.Combine(_repoDir, "models", "Qwen3.6", "Qwen3.6-35B-A3B-UD-Q4_K_M.gguf"),
            command);
        Assert.Contains("--alias qwen3.6-35b-a3b", command);
    }

    [Fact]
    public void Missing_Package_File_Gives_Understandable_Error()
    {
        var service = NewConfigured();
        var error = Assert.Throws<InvalidOperationException>(() =>
            service.ResolveLaunchCommand(service.Manifest(QwenId)!));
        Assert.Contains("llama-server.exe", error.Message);
    }

    // --- установка пакета: дистрибутив скачивается, распаковывается, повторно не качается ---

    [Fact]
    public async Task Package_Is_Downloaded_Unpacked_And_Not_Reinstalled()
    {
        var zip = MakeZip(("tools/llama-server.exe", "двоичный файл"));
        using var server = new FileHttpServer(zip);
        var service = NewConfigured();
        WritePackagesCatalog(server.Url + "llama.zip");
        // манифест модели: только пакет, без гигабайтных весов
        var model = _f.Models.Get(QwenId)!;
        _f.Files.WriteText(model.ProfilePath, """
            {"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"],
             "launchCommand":"\"{package:llama.cpp:llama-server.exe}\" --port 8080"}}
            """);

        service.Start(QwenId);
        await WaitFinishedAsync(service, QwenId);

        var status = service.Status(QwenId);
        Assert.Equal("", status.Error);
        Assert.True(status.Installed);
        Assert.True(status.Packages[0].Installed);
        // распакован в каталог пакета, дистрибутив сохранён в указанном каталоге (не удалён)
        Assert.True(File.Exists(Path.Combine(_packagesDir, "llama", "tools", "llama-server.exe")));
        Assert.True(File.Exists(Path.Combine(_distDir, "llama.zip")));
        // команда запуска прописана в профайл модели
        Assert.Contains("llama-server.exe",
            ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath)).LaunchCommand);
        var downloads = server.Requests.Count;

        // повторный запуск: пакет уже установлен — ни скачивания, ни распаковки
        service.Start(QwenId);
        await WaitFinishedAsync(service, QwenId);
        Assert.Equal(downloads, server.Requests.Count);
    }

    [Fact]
    public async Task Ready_Distributive_Is_Not_Downloaded_Again()
    {
        var zip = MakeZip(("llama-server.exe", "двоичный файл"));
        using var server = new FileHttpServer(zip);
        var service = NewConfigured();
        WritePackagesCatalog(server.Url + "llama.zip");
        var model = _f.Models.Get(QwenId)!;
        _f.Files.WriteText(model.ProfilePath,
            """{"provider":"openai-compatible","install":{"group":"Qwen3.6","packages":["llama.cpp"]}}""");
        // дистрибутив уже лежит в каталоге дистрибутивов (скачан раньше или руками)
        Directory.CreateDirectory(_distDir);
        File.WriteAllBytes(Path.Combine(_distDir, "llama.zip"), zip);

        service.Start(QwenId);
        await WaitFinishedAsync(service, QwenId);

        Assert.True(service.Status(QwenId).Installed);
        // скачивания не было — только HEAD-проверка размера
        Assert.DoesNotContain(server.Requests, r => r == "GET");
    }

    // --- ключи API облачных моделей ---

    [Fact]
    public void Key_Is_Required_Only_For_Cloud_Api_Models()
    {
        var keys = NewKeys();
        Assert.True(keys.Status(FableId).Required);                                  // облако + API
        Assert.Equal("anthropic.apiKey", keys.Status(FableId).SecretRef);
        Assert.False(keys.Status("6f1a45e0-0d31-4c65-9a01-000000000005").Required);  // CLI-подключение
        Assert.False(keys.Status(KandinskyId).Required);                             // локальная
        Assert.False(keys.Status(QwenId).Required);                                  // локальная
    }

    [Fact]
    public void Model_Without_Key_Cannot_Be_Active_And_Activates_When_Key_Is_Set()
    {
        var keys = NewKeys();
        _f.Models.ActivationGuard = m => keys.ActivationError(m.Id);

        var model = _f.Models.Get(FableId)!;
        model.IsActive = false;
        _f.Models.Update(model, null); // выключить можно всегда

        model.IsActive = true;
        var error = Assert.Throws<ArgumentException>(() => _f.Models.Update(model, null));
        Assert.Contains("anthropic.apiKey", error.Message);

        // установка ключа включает модель. С этапа 45 ключ пишется в ОРГАНИЗАЦИЮ
        // (зашифрованным), а не в secrets.json — читается он тем же сервисом
        var status = keys.SetKey(FableId, "sk-test-12345", null);
        Assert.True(status.HasKey);
        Assert.True(_f.Models.Get(FableId)!.IsActive);
        Assert.Equal("sk-test-12345", keys.Resolve("anthropic.apiKey"));
        Assert.Null(_f.Secrets.Read("anthropic.apiKey"));
        // значение ключа наружу не отдаётся — только признак и источник
        Assert.DoesNotContain("sk-test", System.Text.Json.JsonSerializer.Serialize(status));
    }

    [Fact]
    public void Key_Write_Goes_To_The_Organization_And_Leaves_Secrets_File_Alone()
    {
        // с этапа 45 ключи API принадлежат организации: значение пишется в её БД
        // зашифрованным, а secrets.json остаётся для инфраструктурных секретов
        // (ключ организации, токены серверов, Trello) — их правка ключа не трогает
        File.WriteAllText(_f.Secrets.Path,
            """{"anthropic":{"apiKey":"старый"},"trello":{"apiKey":"чужой ключ"}}""");
        _f.OrgKeys.Ensure(StorageFixture.OrgId);
        var keys = NewKeys();

        keys.SetKey(FableId, "новый", null);

        Assert.Equal("новый", keys.Resolve("anthropic.apiKey"));   // организация впереди файла
        Assert.Equal("старый", _f.Secrets.Read("anthropic.apiKey")); // файл не тронут
        Assert.Equal("чужой ключ", _f.Secrets.Read("trello.apiKey"));
        // в базе значение лежит только зашифрованным
        Assert.DoesNotContain("новый", _f.KeyStore.Encrypted("anthropic.apiKey"));
    }

    [Fact]
    public void Startup_Deactivates_Cloud_Models_Without_Key()
    {
        var keys = NewKeys();
        Assert.True(_f.Models.Get(FableId)!.IsActive); // сид создаёт активной

        keys.EnforceActivationOnStartup();

        Assert.False(_f.Models.Get(FableId)!.IsActive);           // облачная без ключа выключена
        Assert.True(_f.Models.Get("6f1a45e0-0d31-4c65-9a01-000000000005")!.IsActive); // CLI не трогаем
        Assert.True(_f.Models.Get(KandinskyId)!.IsActive);        // локальные — не наша забота
    }

    [Fact]
    public void Secret_Store_Read_Write_Has_Are_The_Single_Entry_Point()
    {
        Assert.False(_f.Secrets.Has("some.key"));
        Assert.Null(_f.Secrets.Read("some.key"));

        _f.Secrets.Write("some.key", "значение");

        Assert.True(_f.Secrets.Has("some.key"));
        Assert.Equal("значение", _f.Secrets.Read("some.key"));
        Assert.Throws<ArgumentException>(() => _f.Secrets.Write("  ", "x"));
    }

    // --- помощники ---

    /// <summary>Подменить профайл Kandinsky маленьким манифестом и подложить готовые файлы.</summary>
    private void MaterializeKandinskyFiles(ModelInstallService service)
    {
        var model = _f.Models.Get(KandinskyId)!;
        _f.Files.WriteText(model.ProfilePath, """
            {"provider":"comfyui","install":{"group":"Kandinsky-5","packages":["comfyui"],
              "launchCommand":"\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --extra-model-paths-config \"{extraModelPaths}\"",
              "files":[{"size":4,"name":"dit.safetensors","url":"http://127.0.0.1:9/x","category":"diffusion_models"}]}}
            """);
        var dir = service.TargetDir(service.Manifest(KandinskyId)!);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "dit.safetensors"), new byte[4]);
    }

    /// <summary>Разложить «установленный» ComfyUI; возвращает путь python.exe.</summary>
    private string InstallFakeComfyUi(ModelInstallService service)
    {
        var comfy = service.Packages().Find("comfyui")!;
        var root = Path.Combine(service.PackageDir(comfy), "ComfyUI_windows_portable");
        Directory.CreateDirectory(Path.Combine(root, "python_embeded"));
        Directory.CreateDirectory(Path.Combine(root, "ComfyUI"));
        var python = Path.Combine(root, "python_embeded", "python.exe");
        File.WriteAllText(python, "");
        File.WriteAllText(Path.Combine(root, "ComfyUI", "main.py"), "");
        return python;
    }

    /// <summary>Справочник пакетов с одним пакетом llama.cpp по локальной ссылке (без GitHub).</summary>
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

    private static async Task WaitFinishedAsync(ModelInstallService service, string modelId)
    {
        for (var i = 0; i < 200; i++)
        {
            if (!service.Status(modelId).Running)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Установка не завершилась за 20 секунд");
    }

    /// <summary>Zip-архив в памяти с заданными файлами.</summary>
    private static byte[] MakeZip(params (string Path, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memory,
                   System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }
        return memory.ToArray();
    }

    /// <summary>
    /// Мини-HTTP-сервер на TcpListener (паттерн todo28/36_3): отдаёт один и тот же файл
    /// на GET и его размер на HEAD; запоминает методы запросов для проверок.
    /// </summary>
    private sealed class FileHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _payload;
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }
        public List<string> Requests { get; } = [];

        public FileHttpServer(byte[] payload)
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
                        if (line.Contains("HTTP/1.", StringComparison.Ordinal) && method.Length == 0)
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
