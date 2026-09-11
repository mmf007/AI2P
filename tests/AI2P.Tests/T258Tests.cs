using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-258 (выпуск 1.91): запись справочника Kandinsky-5.0-I2V-Lite-5s (изображение → видео)
/// и то, без чего она бесполезна — передача СТАРТОВОГО КАДРА в ComfyUI: разбор указания
/// в описании задачи (MediaInputDirective), загрузка файла POST /upload/image,
/// плейсхолдер {image} в workflow, фиксируемый seed (params.seed).
/// </summary>
public sealed class T258Tests : IDisposable
{
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";
    private const string I2VId = "6f1a45e0-0d31-4c65-9a01-000000000032";

    private readonly StorageFixture _f = new();

    public T258Tests() => _f.Models.Seed();

    public void Dispose() => _f.Dispose();

    // --- запись справочника ---

    [Fact]
    public void Seed_Adds_I2V_Model_With_Its_Own_Weights_And_Workflow()
    {
        var model = _f.Models.Get(I2VId);
        Assert.NotNull(model);
        Assert.Equal("Kandinsky-5.0-I2V-Lite-5s", model!.Name);
        Assert.False(model.IsCustom);

        var installs = new ModelInstallService(_f.Models, _f.Files, _f.Events,
            () => Path.Combine(_f.Dir, "models-repo"));
        var manifest = installs.Manifest(I2VId);
        Assert.NotNull(manifest);
        // группа та же, что у t2v: энкодеры и VAE у обеих записей общие, второй раз
        // качается только сам DiT
        Assert.Equal("Kandinsky-5", manifest!.Group);
        Assert.Equal(4, manifest.Files.Count);
        Assert.Equal("kandinsky5lite_i2v_5s.safetensors", manifest.Files[0].Name);
        Assert.Equal("diffusion_models", manifest.Files[0].Category);
        Assert.Equal(4573130528, manifest.Files[0].Size);
        Assert.Contains("Kandinsky-5.0-I2V-Lite-5s", manifest.Files[0].Url);
        // три оставшихся файла — те же самые, что у t2v-записи (имя в имя)
        var t2v = installs.Manifest(T2VId)!;
        Assert.Equal(t2v.Files.Skip(1).Select(f => f.Name), manifest.Files.Skip(1).Select(f => f.Name));

        // декларация: вход — картинка, поэтому i2v-навык у записи сильнее t2v-навыка
        var scope = _f.Files.ReadText(model.CapabilitiesPath);
        Assert.Contains("\"image/*\"", scope);
        Assert.Contains("video-animate", scope);
    }

    [Fact]
    public void I2V_Workflow_Feeds_The_Start_Image_Into_The_Kandinsky_Node()
    {
        var template = _f.Files.ReadText($"models/workflow_{I2VId}.json");
        using var doc = JsonDocument.Parse(template);
        var graph = doc.RootElement.GetProperty("prompt");

        // веса — свои, i2v
        Assert.Equal("kandinsky5lite_i2v_5s.safetensors",
            graph.GetProperty("4").GetProperty("inputs").GetProperty("unet_name").GetString());
        // картинка: LoadImage → ImageScale → start_image
        Assert.Equal("{image}", graph.GetProperty("12").GetProperty("inputs")
            .GetProperty("image").GetString());
        Assert.Equal("ImageScale", graph.GetProperty("13").GetProperty("class_type").GetString());
        var start = graph.GetProperty("5").GetProperty("inputs").GetProperty("start_image");
        Assert.Equal("13", start[0].GetString());
        // чистый латент стартового кадра (выход cond_latent, 4-й) вставляется в начало
        // результата сэмплера — иначе первый кадр «плывёт» относительно картинки
        var replace = graph.GetProperty("14").GetProperty("inputs");
        Assert.Equal("ReplaceVideoLatentFrames", graph.GetProperty("14").GetProperty("class_type").GetString());
        Assert.Equal("8", replace.GetProperty("destination")[0].GetString());
        Assert.Equal("5", replace.GetProperty("source")[0].GetString());
        Assert.Equal(3, replace.GetProperty("source")[1].GetInt32());
        Assert.Equal("15", graph.GetProperty("9").GetProperty("inputs").GetProperty("samples")[0].GetString());

        // t2v-шаблон стартового кадра не принимает — и не должен
        Assert.True(ComfyUiConnector.NeedsStartImage(template));
        Assert.False(ComfyUiConnector.NeedsStartImage(_f.Files.ReadText($"models/workflow_{T2VId}.json")));
    }

    [Fact]
    public void BuildWorkflow_Substitutes_The_Uploaded_Image_Name()
    {
        var profile = ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(I2VId)!.ProfilePath));
        var workflow = _f.ComfyUi.BuildWorkflow(profile, prompt: "герой машет рукой",
            seed: 42, jobDisplayId: "J-7", imageName: "ai2p_J-7.png");

        using var doc = JsonDocument.Parse(workflow);
        Assert.DoesNotContain("{image}", workflow);
        Assert.Equal("ai2p_J-7.png",
            doc.RootElement.GetProperty("12").GetProperty("inputs").GetProperty("image").GetString());
        Assert.Equal(768, doc.RootElement.GetProperty("13").GetProperty("inputs")
            .GetProperty("width").GetInt32());
    }

    // --- разбор указания на стартовый кадр ---

    [Fact]
    public void Input_Directive_Takes_The_File_Out_Of_The_Prompt()
    {
        var (prompt, path) = MediaInputDirective.Parse(
            "Взять за основу refs/hero.png\nГерой поворачивает голову и улыбается");
        Assert.Equal("refs/hero.png", path);
        Assert.Equal("Герой поворачивает голову и улыбается", prompt);

        // описание в одну строку: убирается только предложение с указанием
        var (single, singlePath) = MediaInputDirective.Parse(
            "Исходный кадр refs/hero.png. Герой машет рукой.");
        Assert.Equal("refs/hero.png", singlePath);
        Assert.Equal("Герой машет рукой.", single);
    }

    [Fact]
    public void Bare_Path_Lines_Of_An_Expanded_Object_Become_The_Start_Image()
    {
        // так выглядит раскрытие ссылки «@obj:OBJ-3» (T-259): паспорт и пути кадров
        var (prompt, path) = MediaInputDirective.Parse("""
            Герой Вася (персонаж, OBJ-3)
            рыжий, шрам на левой щеке
            refs/hero.png
            refs/hero-2.png
            Крупный план, дождь
            """);
        Assert.Equal("refs/hero.png", path);
        // пути ушли из промпта целиком, паспорт остался
        Assert.DoesNotContain("refs/hero", prompt);
        Assert.Contains("шрам на левой щеке", prompt);
        Assert.Contains("Крупный план, дождь", prompt);
    }

    [Fact]
    public void A_Reference_Inside_A_Sentence_Keeps_The_Tail_Of_The_Phrase()
    {
        // «Крупный план: @obj:OBJ-3, дождь» раскрывается так, что за ПОСЛЕДНИМ путём
        // остаётся продолжение фразы: путь убираем, хвост оставляем промптом
        var (prompt, path) = MediaInputDirective.Parse("""
            Крупный план: Герой Вася (персонаж, OBJ-3)
            рыжий, шрам на левой щеке
            refs/hero.png, дождь
            """);
        Assert.Equal("refs/hero.png", path);
        Assert.DoesNotContain("refs/hero.png", prompt);
        Assert.Contains("дождь", prompt);
        Assert.Contains("Крупный план", prompt);
    }

    [Fact]
    public void Words_Beat_Bare_Paths_And_Bad_Paths_Are_Refused()
    {
        // названный человеком файл сильнее строки-пути объекта
        var (_, path) = MediaInputDirective.Parse(
            "refs/hero.png\nСтартовый кадр refs/hero-2.png\nгерой идёт");
        Assert.Equal("refs/hero-2.png", path);

        // выход из папки проекта и абсолютный путь указанием не считаются
        Assert.Null(MediaInputDirective.Parse("основой взять ../secret.png").Path);
        Assert.Null(MediaInputDirective.Parse(@"основой взять C:\secret.png").Path);
        // видео стартовым кадром не бывает
        Assert.Null(MediaInputDirective.Parse("исходный файл scene.mp4").Path);
        // упоминание картинки без слова-указания — просто текст промпта
        var plain = MediaInputDirective.Parse("нарисуй плакат в стиле poster.png");
        Assert.Null(plain.Path);
        Assert.Equal("нарисуй плакат в стиле poster.png", plain.Prompt);
    }

    [Fact]
    public void Input_And_Output_Directives_Do_Not_Steal_Each_Other()
    {
        // «исходный ФАЙЛ …» разбирается как вход, хотя слово «файл» есть и у указания
        // о результате: вход снимается первым
        var (afterInput, input) = MediaInputDirective.Parse(
            "Исходный файл refs/hero.png\nгерой машет рукой\nрезультат положить в out/scene-01.mp4");
        var (prompt, output) = MediaOutputDirective.Parse(afterInput);
        Assert.Equal("refs/hero.png", input);
        Assert.Equal("out/scene-01.mp4", output);
        Assert.Equal("герой машет рукой", prompt);
    }

    // --- фиксируемый seed ---

    [Fact]
    public void Fixed_Seed_Comes_From_The_Profile()
    {
        Assert.Null(ModelProfile.Parse("""{"provider":"comfyui","params":{"steps":50}}""").MediaSeed);
        // ноль — это «случайный»: пустое поле формы даёт именно его
        Assert.Null(ModelProfile.Parse("""{"provider":"comfyui","params":{"seed":0}}""").MediaSeed);
        Assert.Equal(1234567L, ModelProfile.Parse(
            """{"provider":"comfyui","params":{"seed":1234567}}""").MediaSeed);
        // у записей дистрибутива seed не задан — поведение прежнее, случайный на задание
        Assert.Null(ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(I2VId)!.ProfilePath)).MediaSeed);
    }

    // --- задание целиком: картинка уезжает в ComfyUI ---

    [Fact]
    public async Task Media_Job_Uploads_The_Start_Image_And_Puts_It_In_The_Graph()
    {
        string? uploadBody = null;
        string? submitted = null;
        using var server = new Todo36_4Tests.FakeHttpServer(request =>
        {
            if (request.Path.StartsWith("/upload/image"))
            {
                uploadBody = request.Body;
                return (200, "application/json",
                    Encoding.UTF8.GetBytes("""{"name":"ai2p_J-1.png","subfolder":"","type":"input"}"""));
            }
            if (request.Path.StartsWith("/prompt"))
            {
                submitted = request.Body;
                return (200, "application/json", Encoding.UTF8.GetBytes("""{"prompt_id":"p-1"}"""));
            }
            if (request.Path.StartsWith("/history/p-1"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes("""
                    {"p-1":{"status":{"status_str":"success","completed":true},
                     "outputs":{"11":{"images":[{"filename":"J-1_00001.mp4","subfolder":"AI2P","type":"output"}]}}}}
                    """));
            }
            return request.Path.StartsWith("/view")
                ? (200, "video/mp4", Encoding.ASCII.GetBytes("FAKE-MP4"))
                : (404, "text/plain", []);
        });

        var (_, job) = await RunI2VJobAsync(server.Url,
            "Взять за основу refs/hero.png\nгерой поворачивает голову", withImageFile: true);

        Assert.Equal(JobState.Done, job.State);
        // картинка ушла в ComfyUI...
        Assert.NotNull(uploadBody);
        Assert.Contains("PNG-BYTES", uploadBody);
        Assert.Contains("name=\"overwrite\"", uploadBody);
        // ...и ушла так, как её шлёт браузер: имя файла в заголовке ОДНО (T-277 — второе,
        // filename*, .NET заполнял вместе с кавычками, и ComfyUI отвечал HTTP 500)
        Assert.Contains("filename=\"ai2p_J-1.png\"", uploadBody);
        Assert.DoesNotContain("filename*", uploadBody);
        // ...а в граф попало имя, которым её принял ComfyUI, и указание ушло из промпта
        Assert.Contains("ai2p_J-1.png", submitted);
        Assert.DoesNotContain("refs/hero.png", submitted);
        Assert.Contains("поворачивает голову", submitted);
    }

    [Fact]
    public async Task Media_Job_Without_A_Start_Image_Fails_With_A_Clear_Message()
    {
        using var server = new Todo36_4Tests.FakeHttpServer(_ => (404, "text/plain", []));
        var (task, job) = await RunI2VJobAsync(server.Url, "герой поворачивает голову",
            withImageFile: false);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var error = _f.Files.ReadText(_f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Single(a => a.EndsWith("-error.md")));
        Assert.Contains("@obj:OBJ-N", error);
    }

    [Fact]
    public async Task Missing_Start_Image_File_Is_Named_In_The_Error()
    {
        using var server = new Todo36_4Tests.FakeHttpServer(_ => (404, "text/plain", []));
        var (task, job) = await RunI2VJobAsync(server.Url,
            "Взять за основу refs/none.png\nгерой идёт", withImageFile: true);

        Assert.Equal(JobState.Failed, job.State);
        var error = _f.Files.ReadText(_f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Single(a => a.EndsWith("-error.md")));
        Assert.Contains("refs/none.png", error);
    }

    // --- помощники ---

    /// <summary>Задача на i2v-модели: папка проекта с эталонным кадром → задание → финал.</summary>
    private async Task<(TaskItem Task, Job Job)> RunI2VJobAsync(string baseUrl, string description,
        bool withImageFile)
    {
        var model = _f.Models.Get(I2VId)!;
        _f.Files.WriteText(model.ProfilePath, $$"""
            {
              "provider": "comfyui",
              "model": "kandinsky5lite_i2v_5s",
              "baseUrl": "{{baseUrl.TrimEnd('/')}}",
              "secretRef": "",
              "workflow": "models/workflow_{{I2VId}}.json",
              "params": { "width": 768, "height": 512, "length": 121, "steps": 50 }
            }
            """);
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "кандинский-i2v-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);

        var folder = Path.Combine(_f.Dir, "prj-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(folder, "refs"));
        if (withImageFile)
        {
            File.WriteAllText(Path.Combine(folder, "refs", "hero.png"), "PNG-BYTES");
        }
        var project = _f.Projects.Create("Медиа-проект-" + Guid.NewGuid().ToString("N")[..6],
            folder, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Кадр с героем",
            ExecutorIds = [executor.Id],
        }, description, "Критерии: 5 секунд", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.ComfyUi.SubmitJobAsync(job, requestText: "");
        // ждём финала задания так же, как Todo36_4Tests: под нагрузкой полного прогона
        // поллинг коннектора (раз в 3 с) укладывается в минуты
        for (var i = 0; i < 3600 && _f.Jobs.Get(job.Id)!.State == JobState.Running; i++)
        {
            await Task.Delay(100);
        }
        // статус ЗАДАЧИ коннектор ставит уже после состояния задания
        for (var i = 0; i < 300 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.Draft; i++)
        {
            await Task.Delay(100);
        }
        return (task, _f.Jobs.Get(job.Id)!);
    }
}
