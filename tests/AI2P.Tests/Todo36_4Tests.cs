using System.Net;
using System.Net.Sockets;
using System.Text;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo36_4 (ТЗ v1.41): активность = установленность (неустановленную модель нельзя
/// активировать, после установки активируется автоматически) + запуск задач на медиа-модели
/// через ComfyUiConnector (workflow-шаблон с плейсхолдерами, POST /prompt → /history → /view,
/// бинарные артефакты) + файл путей весов ai2p_extra_model_paths.yaml.
/// </summary>
public sealed class Todo36_4Tests : IDisposable
{
    private const string KandinskyId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;

    public Todo36_4Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private ModelInstallService NewInstalls() => new(_f.Models, _f.Files, _f.Events, () => _repoDir);

    /// <summary>Включить правило «активность = установленность» (в приложении — Program.cs).</summary>
    private ModelInstallService EnableGuard()
    {
        var installs = NewInstalls();
        _f.Models.ActivationGuard = m => installs.ActivationError(m.Id);
        return installs;
    }

    /// <summary>Заменить профайл Kandinsky маленьким манифестом (чтобы не создавать
    /// гигабайтные файлы в тестах) и подложить файлы точных размеров («установлена»).</summary>
    private void MaterializeInstall(ModelInstallService installs)
    {
        var model = _f.Models.Get(KandinskyId)!;
        _f.Files.WriteText(model.ProfilePath, """
            {"provider":"comfyui","install":{"group":"Test-Group","files":[
              {"size":4,"name":"dit.safetensors","url":"http://127.0.0.1:9/x","category":"diffusion_models"},
              {"size":6,"name":"vae.safetensors","url":"http://127.0.0.1:9/y","category":"vae"}
            ] } }
            """);
        var dir = installs.TargetDir(installs.Manifest(KandinskyId)!);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "dit.safetensors"), new byte[4]);
        File.WriteAllBytes(Path.Combine(dir, "vae.safetensors"), new byte[6]);
    }

    // --- часть 1: активность = установленность ---

    [Fact]
    public void Uninstalled_Model_Cannot_Be_Activated_Manually()
    {
        EnableGuard();
        var model = _f.Models.Get(KandinskyId)!;
        model.IsActive = true;
        var ex = Assert.Throws<ArgumentException>(() => _f.Models.Update(model, null));
        Assert.Contains("не установлена", ex.Message);

        // деактивация и правка неактивной — свободно
        model.IsActive = false;
        _f.Models.Update(model, null);
        Assert.False(_f.Models.Get(KandinskyId)!.IsActive);
    }

    [Fact]
    public void Installed_Model_Can_Be_Activated_And_Deactivated()
    {
        var installs = EnableGuard();
        MaterializeInstall(installs);
        var model = _f.Models.Get(KandinskyId)!;
        model.IsActive = true;
        _f.Models.Update(model, null); // установлена — активация разрешена
        Assert.True(_f.Models.Get(KandinskyId)!.IsActive);

        // пользователь волен сбросить флаг по своему усмотрению
        model.IsActive = false;
        _f.Models.Update(model, null);
        Assert.False(_f.Models.Get(KandinskyId)!.IsActive);
    }

    [Fact]
    public void Cloud_Models_Are_Not_Affected_By_Guard()
    {
        EnableGuard();
        var claude = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        claude.IsActive = true;
        _f.Models.Update(claude, null); // манифеста нет — правило не про них
        Assert.True(_f.Models.Get(claude.Id)!.IsActive);
    }

    [Fact]
    public void Startup_Enforce_Deactivates_Uninstalled_But_Keeps_User_Choice()
    {
        var installs = EnableGuard();
        // сид создаёт Kandinsky активной, файлов нет — при старте деактивируется
        Assert.True(_f.Models.Get(KandinskyId)!.IsActive);
        installs.EnforceActivationOnStartup();
        Assert.False(_f.Models.Get(KandinskyId)!.IsActive);

        // установленная, но выключенная пользователем НЕ включается при старте
        MaterializeInstall(installs);
        installs.EnforceActivationOnStartup();
        Assert.False(_f.Models.Get(KandinskyId)!.IsActive);
    }

    [Fact]
    public async Task Finished_Install_Activates_Model_And_Writes_Yaml()
    {
        var installs = EnableGuard();
        installs.EnforceActivationOnStartup(); // как при старте: модель деактивирована
        Assert.False(_f.Models.Get(KandinskyId)!.IsActive);

        // маленький манифест на локальный HTTP-сервер, категории — как у Kandinsky
        var payload = Encoding.ASCII.GetBytes("WEIGHTS!");
        using var server = new FakeHttpServer(_ => (200, "application/octet-stream", payload));
        var model = _f.Models.Get(KandinskyId)!;
        _f.Files.WriteText(model.ProfilePath, $$"""
            {"provider":"comfyui","install":{"group":"Test-Group","files":[
              {"size":8,"name":"dit.safetensors","url":"{{server.Url}}dit","category":"diffusion_models"},
              {"size":8,"name":"enc.safetensors","url":"{{server.Url}}enc","category":"text_encoders"}
            ] } }
            """);

        installs.Start(KandinskyId);
        for (var i = 0; i < 100 && installs.Status(KandinskyId).Running; i++)
        {
            await Task.Delay(100);
        }

        Assert.True(installs.Status(KandinskyId).Installed);
        // донастройка: модель активирована автоматически…
        Assert.True(_f.Models.Get(KandinskyId)!.IsActive);
        // …и написан файл путей весов для ComfyUI
        var yaml = File.ReadAllText(Path.Combine(_repoDir, "ai2p_extra_model_paths.yaml"));
        Assert.Contains("base_path: " + Path.Combine(_repoDir, "models"), yaml);
        Assert.Contains("diffusion_models: Test-Group", yaml);
        Assert.Contains("text_encoders: Test-Group", yaml);
    }

    // --- часть 2: запуск задачи на медиа-модели через ComfyUI ---

    [Fact]
    public void Workflow_Template_Placeholders_Are_Substituted()
    {
        var model = _f.Models.Get(KandinskyId)!;
        var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
        Assert.Equal("comfyui", profile.Provider);
        Assert.True(profile.Workflow.Length > 0);

        var workflow = _f.ComfyUi.BuildWorkflow(profile,
            prompt: "кот \"играет\" с клубком\nна ковре", seed: 42, jobDisplayId: "J-7");

        // результат — валидный JSON без остатков плейсхолдеров
        using var doc = System.Text.Json.JsonDocument.Parse(workflow);
        Assert.DoesNotContain("{prompt}", workflow);
        Assert.DoesNotContain("{seed}", workflow);
        var ksampler = doc.RootElement.GetProperty("8").GetProperty("inputs");
        Assert.Equal(42, ksampler.GetProperty("seed").GetInt64()); // число, не строка
        Assert.Equal(50, ksampler.GetProperty("steps").GetInt32());
        var positive = doc.RootElement.GetProperty("7").GetProperty("inputs").GetProperty("text");
        Assert.Equal("кот \"играет\" с клубком\nна ковре", positive.GetString()); // экранирование
        var save = doc.RootElement.GetProperty("11").GetProperty("inputs");
        Assert.Equal("AI2P/J-7", save.GetProperty("filename_prefix").GetString());
        // DiT в графе — под именем файла из репозитория моделей (todo36_3)
        Assert.Equal("Kandinsky-5.0-T2V-Lite-sft-5s.safetensors",
            doc.RootElement.GetProperty("4").GetProperty("inputs").GetProperty("unet_name").GetString());
    }

    [Fact]
    public async Task Media_Task_Runs_To_Review_With_Binary_Artifact()
    {
        var video = Encoding.ASCII.GetBytes("FAKE-MP4-BYTES");
        string? submittedWorkflow = null;
        using var server = new FakeHttpServer(request =>
        {
            if (request.Path.StartsWith("/prompt"))
            {
                submittedWorkflow = request.Body;
                return (200, "application/json", Encoding.UTF8.GetBytes("""{"prompt_id":"p-1","number":1}"""));
            }
            if (request.Path.StartsWith("/history/p-1"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes("""
                    {"p-1":{"status":{"status_str":"success","completed":true},
                     "outputs":{"11":{"images":[{"filename":"J-1_00001.mp4","subfolder":"AI2P","type":"output"}]}}}}
                    """));
            }
            if (request.Path.StartsWith("/view"))
            {
                return (200, "video/mp4", video);
            }
            return (404, "text/plain", []);
        });

        var (task, job) = await RunMediaJobAsync(server.Url);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.Status);
        // промпт генерации — описание задачи, а не сборный агентный промпт
        Assert.Contains("пушистый кот", submittedWorkflow!);
        Assert.DoesNotContain("Критерии", submittedWorkflow);
        // бинарный артефакт + сводка
        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        var videoRel = artifacts.Single(a => a.EndsWith(".mp4"));
        Assert.Equal(video, File.ReadAllBytes(Path.Combine(_f.Dir, videoRel)));
        var summaryRel = artifacts.Single(a => a.EndsWith("-result.md"));
        var summary = File.ReadAllText(Path.Combine(_f.Dir, summaryRel));
        Assert.Contains("seed", summary);
        Assert.Contains("J-1_00001.mp4", summary.Replace(job.DisplayId + "-", ""));
    }

    [Fact]
    public async Task Media_Task_Fails_To_Error_When_Comfyui_Rejects_Workflow()
    {
        using var server = new FakeHttpServer(request =>
            request.Path.StartsWith("/prompt")
                ? (400, "application/json", Encoding.UTF8.GetBytes(
                    """{"error":{"type":"invalid_prompt","message":"Cannot execute because node UNETLoader does not exist."},"node_errors":{}}"""))
                : (404, "text/plain", []));

        var (task, job) = await RunMediaJobAsync(server.Url);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var errorMd = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!).Single(a => a.EndsWith("-error.md"));
        Assert.Contains("UNETLoader", File.ReadAllText(Path.Combine(_f.Dir, errorMd)));
    }

    [Fact]
    public async Task Probe_Checks_System_Stats()
    {
        using var server = new FakeHttpServer(request =>
            request.Path.StartsWith("/system_stats")
                ? (200, "application/json", Encoding.UTF8.GetBytes("""{"system":{}}"""))
                : (404, "text/plain", []));
        var executor = CreateMediaExecutor(server.Url);
        Assert.Null(await _f.ComfyUi.TestConnectionAsync(executor));

        // сервер не отвечает — понятная ошибка
        server.Dispose();
        var error = await _f.ComfyUi.TestConnectionAsync(executor);
        Assert.NotNull(error);
        Assert.Contains("ComfyUI", error);
    }

    // --- помощники ---

    /// <summary>ИИ-исполнитель на модели Kandinsky с baseUrl тестового сервера.</summary>
    private Executor CreateMediaExecutor(string baseUrl)
    {
        var model = _f.Models.Get(KandinskyId)!;
        // профайл модели переписывается на тестовый сервер; workflow — сидовый
        _f.Files.WriteText(model.ProfilePath, $$"""
            {
              "provider": "comfyui",
              "model": "kandinsky5lite_t2v_sft_5s",
              "baseUrl": "{{baseUrl.TrimEnd('/')}}",
              "secretRef": "",
              "workflow": "models/workflow_{{KandinskyId}}.json",
              "params": { "width": 768, "height": 512, "length": 121, "steps": 50 }
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = "кандинский-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);
    }

    /// <summary>Задача с описанием-промптом → задание в ComfyUiConnector → дождаться финала.</summary>
    private async Task<(TaskItem Task, Job Job)> RunMediaJobAsync(string baseUrl)
    {
        var executor = CreateMediaExecutor(baseUrl);
        var project = _f.Projects.Create("Медиа-проект-" + Guid.NewGuid().ToString("N")[..6], null, null, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Медиа-команда-" + Guid.NewGuid().ToString("N")[..6],
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = executor.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Ролик про кота",
            ExecutorIds = [executor.Id],
        }, "пушистый кот играет с клубком шерсти", "Критерии: ролик 5 секунд", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.ComfyUi.SubmitJobAsync(job, requestText: "");
        // поллинг коннектора — раз в 3 с; на загруженной машине (полный прогон тестов)
        // 15 с не хватало и тест изредка падал — ждём до минуты (todo36_5), а с ростом
        // набора тестов (T-121) и минуты стало мало: генерация укладывалась в ~1,5 мин,
        // и полный прогон падал на ровном месте — ждём до трёх минут (цикл выходит сразу,
        // как только задание сменит состояние, так что быстрым прогонам это ничего не стоит).
        // К выпуску 1.82 набор дорос до 1332 тестов, и трёх минут снова стало не хватать
        // (T-203: «Expected Done, Actual Running» в двух прогонах подряд) — ждём до шести
        for (var i = 0; i < 3600 && _f.Jobs.Get(job.Id)!.State == JobState.Running; i++)
        {
            await Task.Delay(100);
        }
        // состояние ЗАДАЧИ коннектор ставит уже ПОСЛЕ состояния задания — на загруженной
        // машине проверка успевала увидеть ещё draft (флаки-падение, todo37): ждём и его
        for (var i = 0; i < 300 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.Draft; i++)
        {
            await Task.Delay(100);
        }
        return (task, _f.Jobs.Get(job.Id)!);
    }

    /// <summary>Разобранный HTTP-запрос тестового сервера.</summary>
    /// <param name="Auth">Значение заголовка Authorization (T-14-S0): у шлюза fal.ai ключ
    /// API идёт в каждом запросе, и проверить это можно только на стороне сервера.</param>
    public sealed record FakeRequest(string Method, string Path, string Body, string Auth = "");

    /// <summary>
    /// Мини-HTTP-сервер на TcpListener (паттерн todo28/36_3): маршрутизация делегатом,
    /// поддержка тела запроса по Content-Length. Публичный — им пользуются и тесты
    /// доработок медиа-коннектора (todo37_3).
    /// </summary>
    public sealed class FakeHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<FakeRequest, (int Status, string ContentType, byte[] Body)> _handler;
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }

        public FakeHttpServer(Func<FakeRequest, (int, string, byte[])> handler)
        {
            _handler = handler;
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
                // КАЖДОЕ соединение обслуживается СВОЕЙ задачей, а цикл сразу возвращается
                // к приёму следующего (T-249-S0, второй круг). Раньше обслуживание шло прямо
                // здесь, и «вежливое» закрытие ниже держало приём до ПЯТИ СЕКУНД на запрос:
                // медиа-коннектор опрашивает /history раз в 3 с и консоль раз в 2 с, то есть
                // запросы приходили чаще, чем освобождался цикл, очередь росла сама себя
                // разгоняя, и через 5 минут запрос упирался в таймаут HttpClient — задание
                // уходило в Failed вместо Done. Так падали Todo36_4Tests.Media_Task_Runs_…
                // и T258Tests.Media_Job_Uploads_… — по 5–8 минут, в двух десятках полных
                // прогонов начиная с 1.86 и задолго до правок этого выпуска.
                _ = Task.Run(() => HandleAsync(client));
            }
        }

        /// <summary>Обслужить одно соединение: разобрать запрос, ответить, закрыться.</summary>
        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    // разбор запроса на уровне байтов: Content-Length — в байтах,
                    // а тело с кириллицей в UTF-8 длиннее в символах, чем в байтах
                    var stream = client.GetStream();
                    var raw = new List<byte>();
                    var buffer = new byte[8192];
                    var headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        var n = await stream.ReadAsync(buffer, _cts.Token);
                        if (n == 0)
                        {
                            break;
                        }
                        raw.AddRange(buffer.AsSpan(0, n).ToArray());
                        headerEnd = FindHeaderEnd(raw);
                    }
                    if (headerEnd < 0)
                    {
                        return;
                    }
                    var headerText = Encoding.ASCII.GetString(raw.ToArray(), 0, headerEnd);
                    var lines = headerText.Split("\r\n");
                    var parts = lines[0].Split(' ');
                    var contentLength = lines
                        .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => int.Parse(l["Content-Length:".Length..].Trim()))
                        .FirstOrDefault();
                    var bodyStart = headerEnd + 4;
                    while (raw.Count < bodyStart + contentLength)
                    {
                        var n = await stream.ReadAsync(buffer, _cts.Token);
                        if (n == 0)
                        {
                            break;
                        }
                        raw.AddRange(buffer.AsSpan(0, n).ToArray());
                    }
                    var body = Encoding.UTF8.GetString(raw.ToArray(), bodyStart,
                        Math.Min(contentLength, raw.Count - bodyStart));

                    var auth = lines
                        .Where(l => l.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => l["Authorization:".Length..].Trim())
                        .FirstOrDefault() ?? "";
                    var (status, contentType, responseBody) = _handler(
                        new FakeRequest(parts.ElementAtOrDefault(0) ?? "", parts.ElementAtOrDefault(1) ?? "",
                            body, auth));
                    var header = $"HTTP/1.1 {status} X\r\nContent-Type: {contentType}\r\n" +
                                 $"Content-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                    await stream.WriteAsync(responseBody);
                    await stream.FlushAsync();
                    // корректное закрытие: сначала half-close, потом ждём, пока клиент дочитает
                    // ответ. Без этого Dispose сокета изредка рвёт соединение RST-ом (на загруженной
                    // машине — при полном прогоне тестов), клиент видит обрыв вместо ответа
                    // и тест падает не по делу (todo36_6).
                    try
                    {
                        client.Client.Shutdown(SocketShutdown.Send);
                        using var drain = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                        drain.CancelAfter(TimeSpan.FromSeconds(5));
                        while (await stream.ReadAsync(buffer, drain.Token) > 0)
                        {
                            // клиент дочитывает ответ и закрывает свою половину соединения
                        }
                    }
                    catch (Exception ex) when (ex is SocketException or IOException
                                                  or OperationCanceledException)
                    {
                        // клиент уже ушёл — закрываем соединение как есть
                    }
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException
                                          or ObjectDisposedException or OperationCanceledException)
            {
                // сервер остановлен (Dispose) или соединение оборвалось — это не беда теста:
                // задача обслуживания фоновая, и необработанное исключение в ней никому
                // не достаётся, кроме финализатора задачи
            }
        }

        /// <summary>Позиция «\r\n\r\n» — конец заголовков; -1, если ещё не дочитан.</summary>
        private static int FindHeaderEnd(List<byte> raw)
        {
            for (var i = 0; i + 3 < raw.Count; i++)
            {
                if (raw[i] == '\r' && raw[i + 1] == '\n' && raw[i + 2] == '\r' && raw[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
