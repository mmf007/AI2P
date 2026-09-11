using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo37_3 (ТЗ v1.45) — доработки по итогам живой проверки медиа-модели:
/// консоль задания (живой вывод исполнителя, у ComfyUI — его собственная консоль),
/// параметры генерации и постановка в очередь в журнале работ, указание «положить
/// результат в файл …» в папку проекта, удаление файлов задачи, доформирование
/// команды запуска установленной модели.
/// </summary>
public sealed class Todo37_3DirectiveTests
{
    [Fact]
    public void Directive_On_Own_Line_Is_Taken_Out_Of_Prompt()
    {
        var (prompt, path) = MediaOutputDirective.Parse(
            "пушистый кот играет с клубком шерсти\nрезультат положить в корень проекта в файл trask1.mp4");

        Assert.Equal("trask1.mp4", path);
        // имя файла не должно попасть в промпт генерации — иначе модель рисует «файл»
        Assert.Equal("пушистый кот играет с клубком шерсти", prompt);
    }

    [Fact]
    public void Directive_In_Single_Line_Removes_Only_Its_Sentence()
    {
        var (prompt, path) = MediaOutputDirective.Parse(
            "Пушистый кот играет с клубком. Результат сохрани в файл trask1.mp4");

        Assert.Equal("trask1.mp4", path);
        Assert.Equal("Пушистый кот играет с клубком.", prompt);
    }

    [Fact]
    public void Subdirectory_Is_Kept()
    {
        var (_, path) = MediaOutputDirective.Parse("кот\nсохрани результат в out/videos/clip.mp4");
        Assert.Equal("out/videos/clip.mp4", path);

        var (_, windows) = MediaOutputDirective.Parse("кот\nсохрани результат в out\\videos\\clip.mp4");
        Assert.Equal("out/videos/clip.mp4", windows);
    }

    [Fact]
    public void Description_Without_Directive_Goes_To_Prompt_As_Is()
    {
        const string description = "морской закат, чайки, длинный план";
        var (prompt, path) = MediaOutputDirective.Parse(description);

        Assert.Null(path);
        Assert.Equal(description, prompt);
    }

    [Fact]
    public void File_Name_Without_Instruction_Word_Is_Not_A_Directive()
    {
        // просто упоминание файла в описании сцены указанием не является
        var (prompt, path) = MediaOutputDirective.Parse("кот в стиле ролика cats_2019.mp4");
        Assert.Null(path);
        Assert.Equal("кот в стиле ролика cats_2019.mp4", prompt);
    }

    [Theory]
    [InlineData("сохрани результат в ../../secret.mp4")]
    [InlineData("сохрани результат в C:/windows/system32/evil.mp4")]
    public void Escaping_Project_Folder_Is_Rejected(string line)
    {
        var (_, path) = MediaOutputDirective.Parse("кот\n" + line);
        Assert.Null(path);
    }
}

/// <summary>Консоль задания: кольцевой буфер, чтение хвоста по номеру строки.</summary>
public sealed class Todo37_3JobConsoleTests
{
    [Fact]
    public void Multiline_Output_Becomes_Separate_Lines()
    {
        var console = new JobConsole();
        console.Write("job-1", "первая\nвторая\r\nтретья");

        var (lines, nextSeq, _) = console.Read("job-1", 0);
        Assert.Equal(["первая", "вторая", "третья"], lines.Select(l => l.Text));
        Assert.Equal(3, nextSeq);
    }

    [Fact]
    public void Read_After_Returns_Only_The_Tail()
    {
        var console = new JobConsole();
        console.Write("job-1", "раз\nдва");
        var (_, seq, _) = console.Read("job-1", 0);

        console.Write("job-1", "три");
        var (tail, _, lost) = console.Read("job-1", seq);

        Assert.Equal(["три"], tail.Select(l => l.Text));
        Assert.Equal(0, lost);
    }

    [Fact]
    public void Overflow_Is_Reported_As_Lost_Lines()
    {
        var console = new JobConsole();
        console.Write("job-1", "старт");
        var (_, seq, _) = console.Read("job-1", 0);

        // вывод шёл быстрее, чем UI читал: часть строк вытеснена из кольца
        console.Write("job-1", string.Join("\n", Enumerable.Range(0, JobConsole.MaxLines + 10)
            .Select(i => "строка " + i)));
        var (lines, _, lost) = console.Read("job-1", seq);

        Assert.Equal(JobConsole.MaxLines, lines.Count);
        Assert.True(lost > 0, "потерянные строки должны быть посчитаны");
    }

    [Fact]
    public void Unknown_Job_Has_Empty_Console()
    {
        var console = new JobConsole();
        var (lines, _, _) = console.Read("нет такого", 0);
        Assert.Empty(lines);
        Assert.False(console.Has("нет такого"));
    }
}

/// <summary>Вывод локального сервера модели — тоже в консоль задания (ТЗ v1.45).</summary>
public sealed class Todo37_3LocalServerOutputTests
{
    [Fact]
    public async Task Server_Output_Is_Readable_By_Sequence()
    {
        // текст латиницей: cmd.exe печатает кириллицу в OEM-кодировке, и проверять
        // в тесте пришлось бы кодировку консоли Windows, а не саму механику чтения
        var command = OperatingSystem.IsWindows()
            ? "cmd /c echo first-line& echo second-line"
            : "/bin/sh -c \"echo first-line; echo second-line\"";
        var service = new LocalModelProcessService();
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1", "team-1");
        for (var i = 0; i < 100 && service.ReadOutput(command, 0).Lines.Count < 2; i++)
        {
            await Task.Delay(50);
        }

        var (all, next) = service.ReadOutput(command, 0);
        Assert.Contains(all, l => l.Contains("first-line"));
        // дочитывание с номера: нового вывода нет — и строк нет
        Assert.Empty(service.ReadOutput(command, next).Lines);
    }

    [Fact]
    public void Unknown_Command_Has_No_Output()
    {
        var service = new LocalModelProcessService();
        var (lines, next) = service.ReadOutput("никогда не запускалось", 0);
        Assert.Empty(lines);
        Assert.Equal(0, next);
    }
}

/// <summary>Стыковка «хвоста» консоли ComfyUI: он отдаёт весь свой буфер целиком.</summary>
public sealed class Todo37_3ComfyLogTailTests
{
    [Fact]
    public void First_Read_Returns_Everything()
    {
        var tail = new ComfyLogTail();
        Assert.Equal(["a", "b", "c"], tail.Advance(["a", "b", "c"]));
    }

    [Fact]
    public void Repeated_Buffer_Gives_Only_New_Lines()
    {
        var tail = new ComfyLogTail();
        tail.Advance(["a", "b", "c"]);

        Assert.Equal(["d", "e"], tail.Advance(["a", "b", "c", "d", "e"]));
        Assert.Empty(tail.Advance(["a", "b", "c", "d", "e"]));
    }

    [Fact]
    public void Shifted_Ring_Buffer_Still_Finds_The_Seam()
    {
        var tail = new ComfyLogTail();
        // длина буфера ComfyUI не растёт: старые строки выпадают, новые дописываются —
        // по количеству строк новизну не вычислить, стыковка идёт по содержимому
        tail.Advance(["1", "2", "3", "4", "5", "6"]);
        Assert.Equal(["7", "8"], tail.Advance(["3", "4", "5", "6", "7", "8"]));
    }

    [Fact]
    public void Progress_Bar_Frames_Are_All_New()
    {
        var tail = new ComfyLogTail();
        tail.Advance(["  4%|▍         | 2/50"]);
        Assert.Equal(["  6%|▌         | 3/50"], tail.Advance(["  4%|▍         | 2/50", "  6%|▌         | 3/50"]));
    }

    [Fact]
    public void Comfy_Log_Entries_Are_Parsed()
    {
        var lines = ComfyUiConnector.ParseLogEntries("""
            {"entries":[{"t":"stdout","m":"[INFO] got prompt\n"},
                        {"t":"stderr","m":"  4%|x| 2/50\r  6%|xx| 3/50\r"}],
             "size":{"cols":80,"rows":25}}
            """);

        Assert.Equal(["[INFO] got prompt", "  4%|x| 2/50", "  6%|xx| 3/50"], lines);
    }
}

/// <summary>
/// Живой прогон медиа-задания (ComfyUI): консоль задания, события журнала и файл
/// результата в папке проекта по указанию из описания.
/// </summary>
public sealed class Todo37_3MediaJobTests : IDisposable
{
    private const string KandinskyId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    private readonly StorageFixture _f = new();
    private readonly string _projectFolder;

    public Todo37_3MediaJobTests()
    {
        _f.Models.Seed();
        _projectFolder = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(_projectFolder);
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Job_Console_Gets_Comfyui_Output_And_Result_Lands_In_Project_Folder()
    {
        var video = Encoding.ASCII.GetBytes("FAKE-MP4-BYTES");
        string? submittedWorkflow = null;
        // первый опрос истории — «ещё не готово»: иначе генерация может завершиться
        // раньше первого чтения консоли ComfyUI и проверять будет нечего
        var historyCalls = new int[1];
        using var server = new Todo36_4Tests.FakeHttpServer(request =>
        {
            if (request.Path.StartsWith("/prompt"))
            {
                submittedWorkflow = request.Body;
                return (200, "application/json", Encoding.UTF8.GetBytes("""{"prompt_id":"p-1"}"""));
            }
            if (request.Path.StartsWith("/internal/logs/raw"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes("""
                    {"entries":[{"t":"stdout","m":"[INFO] got prompt"},
                                {"t":"stderr","m":"  76%|#######   | 38/50 [42:02<13:16, 66.40s/it]"}]}
                    """));
            }
            if (request.Path.StartsWith("/history/p-1"))
            {
                if (++historyCalls[0] == 1)
                {
                    return (200, "application/json", Encoding.UTF8.GetBytes("{}"));
                }
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

        var (task, job) = await RunMediaJobAsync(server.Url,
            "пушистый кот играет с клубком шерсти\nрезультат положить в корень проекта в файл trask1.mp4");

        Assert.True(job.State == JobState.Done, ErrorText(task));

        // 1. указание исполнено: файл лежит в КОРНЕ ПАПКИ ПРОЕКТА под заказанным именем
        var expected = Path.Combine(_projectFolder, "trask1.mp4");
        Assert.True(File.Exists(expected), "файл результата должен появиться в папке проекта");
        Assert.Equal(video, File.ReadAllBytes(expected));
        // …и при этом остаётся артефактом задачи
        Assert.Contains(_f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!), a => a.EndsWith(".mp4"));

        // 2. в промпт генерации указание не попало
        Assert.Contains("пушистый кот", submittedWorkflow!);
        Assert.DoesNotContain("trask1.mp4", submittedWorkflow);

        // 3. консоль задания: и наши строки, и консоль самого ComfyUI с прогрессом
        var (lines, _, _) = _f.Console.Read(job.Id, 0);
        var console = string.Join("\n", lines.Select(l => l.Text));
        Assert.Contains("prompt_id p-1", console);
        Assert.Contains("38/50", console);
        Assert.Contains("Результат скопирован в папку проекта: trask1.mp4", console);

        // 4. журнал работ: параметры генерации и постановка в очередь — во вкладках
        // «История» задачи и проекта
        var events = _f.Events.Query(taskId: task.Id, limit: 100);
        var request = events.Single(e => e.EventType == EventTypes.AgentRequest);
        using var payload = JsonDocument.Parse(request.PayloadJson);
        Assert.Equal(768, payload.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(50, payload.RootElement.GetProperty("steps").GetInt32());
        Assert.Equal("trask1.mp4", payload.RootElement.GetProperty("outputFile").GetString());
        var queued = events.Single(e => e.EventType == EventTypes.JobQueued);
        Assert.Contains("p-1", queued.PayloadJson);
        // событие привязано к проекту — иначе его не видно во вкладке «История» проекта
        Assert.All(events, e => Assert.Equal(task.ProjectId, e.ProjectId));
    }

    [Fact]
    public async Task Missing_Comfyui_Console_Does_Not_Break_The_Job()
    {
        // старая сборка ComfyUI без /internal/logs/raw: задание обязано доработать до конца,
        // а в консоли — честная строка «консоль недоступна»
        var video = Encoding.ASCII.GetBytes("FAKE");
        var historyCalls = new int[1];
        using var server = new Todo36_4Tests.FakeHttpServer(request =>
        {
            if (request.Path.StartsWith("/prompt"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes("""{"prompt_id":"p-2"}"""));
            }
            if (request.Path.StartsWith("/history/p-2"))
            {
                // первый ответ — «ещё не готово»: даём коннектору прочитать консоль ComfyUI
                if (++historyCalls[0] == 1)
                {
                    return (200, "application/json", Encoding.UTF8.GetBytes("{}"));
                }
                return (200, "application/json", Encoding.UTF8.GetBytes("""
                    {"p-2":{"status":{"status_str":"success"},
                     "outputs":{"11":{"images":[{"filename":"J_00001.mp4","subfolder":"","type":"output"}]}}}}
                    """));
            }
            if (request.Path.StartsWith("/view"))
            {
                return (200, "video/mp4", video);
            }
            return (404, "text/plain", []);
        });

        var (task, job) = await RunMediaJobAsync(server.Url, "морской закат");

        Assert.True(job.State == JobState.Done, ErrorText(task));
        var (lines, _, _) = _f.Console.Read(job.Id, 0);
        Assert.Contains(lines, l => l.Text.Contains("консоль ComfyUI недоступна"));
    }

    /// <summary>Текст ошибки задания (артефакт J-N-error.md) — чтобы падение теста объясняло себя.</summary>
    private string ErrorText(TaskItem task) =>
        string.Join("\n", _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Where(a => a.EndsWith("-error.md"))
            .Select(a => File.ReadAllText(_f.Files.Abs(a))));

    /// <summary>Задача с описанием-промптом → задание в ComfyUiConnector → дождаться финала.</summary>
    private async Task<(TaskItem Task, Job Job)> RunMediaJobAsync(string baseUrl, string description)
    {
        var model = _f.Models.Get(KandinskyId)!;
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
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "кандинский-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);
        // папка проекта нужна, чтобы было куда класть файл результата (ТЗ п. 2.7)
        var project = _f.Projects.Create("Медиа-проект-" + Guid.NewGuid().ToString("N")[..6],
            _projectFolder, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Ролик про кота",
            ExecutorIds = [executor.Id],
        }, description, "", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.ComfyUi.SubmitJobAsync(job, requestText: "");
        for (var i = 0; i < 600 && _f.Jobs.Get(job.Id)!.State == JobState.Running; i++)
        {
            await Task.Delay(100);
        }
        for (var i = 0; i < 100 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.Draft; i++)
        {
            await Task.Delay(100);
        }
        return (task, _f.Jobs.Get(job.Id)!);
    }
}

/// <summary>Удаление файлов задачи из окна «все файлы» (кнопка у каждой записи).</summary>
public sealed class Todo37_3TaskFileDeleteTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem NewTask(out Core.Entities.Project project)
    {
        project = _f.Projects.Create("Проект-" + Guid.NewGuid().ToString("N")[..6], null, null, null);
        return _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Задача" },
            "описание", "", null);
    }

    [Fact]
    public void Artifact_Is_Deleted_And_Logged()
    {
        var task = NewTask(out var project);
        var artifact = _f.Files.WriteTaskArtifactBytes(project.Slug, task.DisplayId,
            "J-1-video.mp4", new byte[1024]);
        Assert.True(_f.Tasks.OwnsFile(task, artifact));

        _f.Tasks.DeleteFile(task, artifact, actorId: null);

        Assert.False(File.Exists(_f.Files.Abs(artifact)));
        Assert.DoesNotContain(artifact, _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!));
        Assert.Contains(_f.Events.Query(taskId: task.Id, limit: 50),
            e => e.EventType == EventTypes.TaskFileDeleted && e.PayloadJson.Contains("J-1-video.mp4"));
    }

    [Fact]
    public void Upload_Of_The_Project_Is_Deletable()
    {
        var task = NewTask(out var project);
        var upload = _f.Files.SaveUpload(project.Slug, "картинка.png", new byte[16]);

        Assert.True(_f.Tasks.OwnsFile(task, upload));
        _f.Tasks.DeleteFile(task, upload, actorId: null);
        Assert.False(File.Exists(_f.Files.Abs(upload)));
    }

    [Fact]
    public void Foreign_Files_Are_Not_Deletable()
    {
        var task = NewTask(out _);
        var other = _f.Tasks.Create(new TaskItem { Title = "Чужая задача" }, "описание", "", null);
        var foreign = _f.Files.WriteTaskArtifact(null, other.DisplayId, "J-9-result.md", "чужой результат");

        Assert.False(_f.Tasks.OwnsFile(task, foreign));
        Assert.Throws<ArgumentException>(() => _f.Tasks.DeleteFile(task, foreign, null));
        Assert.True(File.Exists(_f.Files.Abs(foreign)));

        // и выход за пределы хранилища тоже закрыт
        Assert.False(_f.Tasks.OwnsFile(task, "../secrets.json"));
    }

    [Fact]
    public void Missing_File_Gives_Clear_Error()
    {
        var task = NewTask(out var project);
        var rel = Path.Combine(_f.Files.TaskDirRel(project.Slug, task.DisplayId), "artifacts", "нет.mp4")
            .Replace('\\', '/');
        var ex = Assert.Throws<ArgumentException>(() => _f.Tasks.DeleteFile(task, rel, null));
        Assert.Contains("не найден", ex.Message);
    }
}

/// <summary>Команда запуска у модели, установленной мимо установщика (кнопка «Обновить»).</summary>
public sealed class Todo37_3LaunchCommandTests : IDisposable
{
    private const string KandinskyId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;

    public Todo37_3LaunchCommandTests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Профайл с манифестом без пакетов: шаблон команды — по путям весов.</summary>
    private ModelInstallService WithInstalledModel(bool filesOnDisk)
    {
        var installs = new ModelInstallService(_f.Models, _f.Files, _f.Events, () => _repoDir);
        var model = _f.Models.Get(KandinskyId)!;
        _f.Files.WriteText(model.ProfilePath, """
            {
              "provider": "comfyui",
              "model": "kandinsky5lite_t2v_sft_5s",
              "install": {
                "group": "Test-Group",
                "launchCommand": "python main.py --model \"{model:dit.safetensors}\" --paths \"{extraModelPaths}\"",
                "files": [ {"size":4,"name":"dit.safetensors","url":"http://127.0.0.1:9/x","category":"diffusion_models"} ]
              }
            }
            """);
        if (filesOnDisk)
        {
            var dir = installs.TargetDir(installs.Manifest(KandinskyId)!);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "dit.safetensors"), new byte[4]);
        }
        return installs;
    }

    [Fact]
    public void Installed_Model_Without_Command_Gets_It_On_Status()
    {
        // ровно случай заказчика: веса скачаны и пакеты поставлены руками, установщик
        // не отрабатывал — в профайле нет launchCommand, запускать модель нечем
        var installs = WithInstalledModel(filesOnDisk: true);
        Assert.True(installs.Status(KandinskyId).Installed);

        var command = installs.Status(KandinskyId).LaunchCommand;

        Assert.Contains("dit.safetensors", command);
        Assert.Contains(Path.Combine(_repoDir, "models", "Test-Group"), command);
        // команда именно ПРОПИСАНА в профайл — её увидит запуск работы команды (п. 2.9)
        Assert.Contains("launchCommand", _f.Files.ReadText(_f.Models.Get(KandinskyId)!.ProfilePath));
    }

    [Fact]
    public void Existing_Command_Is_Not_Overwritten()
    {
        var installs = WithInstalledModel(filesOnDisk: true);
        var model = _f.Models.Get(KandinskyId)!;
        var profile = _f.Files.ReadText(model.ProfilePath)
            .Replace("{\n  \"provider\"", "{\n  \"launchCommand\": \"моя команда\",\n  \"provider\"");
        _f.Files.WriteText(model.ProfilePath, profile);

        Assert.Equal("моя команда", installs.Status(KandinskyId).LaunchCommand);
    }

    [Fact]
    public void Not_Installed_Model_Gets_No_Command()
    {
        // пока веса не на месте, команда бессмысленна — профайл не трогаем
        var installs = WithInstalledModel(filesOnDisk: false);
        Assert.False(installs.Status(KandinskyId).Installed);
        Assert.Equal("", installs.Status(KandinskyId).LaunchCommand);
    }
}
