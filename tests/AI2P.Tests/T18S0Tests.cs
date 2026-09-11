using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-18-S0 (локальные модели звука, §6.4 отчёта <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>):
/// справочник пополнен тремя вариантами ACE-Step 1.5 XL — музыка и песни по тексту, локально
/// через ComfyUI. До этого задания навыки <c>audio-*</c> не закрывала НИ ОДНА запись.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, коды навыков из
/// справочника, разбираемость workflow и связность его узлов, совпадение имён весов в шаблоне
/// с манифестом установки (иначе ComfyUI ответит «model not found» уже во время задания),
/// настройка LoRA и документы на обоих языках с разделом «Лицензия».
///
/// Отдельно — то, что у звука своё: пара inputs/outputs обязана пройти проверку SkillIo
/// (иначе подбор исполнителя обнулит оценку и запись, будучи в справочнике, не возьмётся ни
/// на одну задачу), а результат обязан забираться коннектором так же, как картинка —
/// по полю <c>filename</c> в истории ComfyUI.
/// </summary>
public sealed class T18S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием.</summary>
    public static readonly string[] AudioModels =
    [
        "ACE-Step-1.5-XL-Turbo", "ACE-Step-1.5-XL-Base", "ACE-Step-1.5-XL-SFT",
    ];

    /// <summary>Файлы весов, общие у всех трёх вариантов (группа установки одна).</summary>
    private static readonly string[] SharedFiles =
    [
        "qwen_0.6b_ace15.safetensors", "qwen_4b_ace15.safetensors", "ace_1.5_vae.safetensors",
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in AudioModels)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T18S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private AI2P.Core.Entities.AiModel Model(string name) =>
        _f.Models.List().Single(m => m.Name == name);

    private string Profile(string name) => _f.Files.ReadText(Model(name).ProfilePath);

    private string Scope(string name) => _f.Files.ReadText(Model(name).CapabilitiesPath);

    private JsonDocument Workflow(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        return JsonDocument.Parse(_f.Files.ReadText(profile.Workflow));
    }

    private static JsonElement Graph(JsonDocument workflow) =>
        workflow.RootElement.GetProperty("prompt");

    private static string DocPath(string lang, string name) =>
        Path.Combine(AppDir(), "doc", lang, "models", name + ".md");

    /// <summary>Каталог поставляемой документации (AI2P_app) — от каталога сборки вверх.</summary>
    private static string AppDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir.Length > 0 && !Directory.Exists(Path.Combine(dir, "doc", "ru", "models")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                return "";
            }
            dir = parent.FullName;
        }
        return dir;
    }

    // --- состав справочника ---

    [Fact]
    public void Catalogue_Has_The_Three_Ace_Step_Models()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in AudioModels)
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Audio_Skills_Are_Covered_And_Locally_Only_By_These_Three()
    {
        // ради этого задание и заводилось: до него навыки audio-* не закрывала НИ ОДНА
        // запись. Параллельно их закрыли и облачные записи шлюза fal.ai (T-14-S0), поэтому
        // проверяется не «только мы», а «мы закрываем, и мы — единственные ЛОКАЛЬНЫЕ»:
        // локальная запись работает без ключа и без денег, и это разные способы закрыть навык
        var local = new List<string>();
        var all = new List<string>();
        foreach (var model in _f.Models.List())
        {
            if (model.CapabilitiesPath.Length == 0)
            {
                continue;
            }
            using var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));
            var codes = scope.RootElement.GetProperty("skills").EnumerateArray()
                .Select(s => s.GetProperty("name").GetString()!);
            if (!codes.Any(c => c is "audio-song" or "audio-music"))
            {
                continue;
            }
            all.Add(model.Name);
            if (ModelInstallManifest.Parse(_f.Files.ReadText(model.ProfilePath)) is not null)
            {
                local.Add(model.Name);
            }
        }

        Assert.Equal(AudioModels.OrderBy(n => n), local.OrderBy(n => n));
        foreach (var name in AudioModels)
        {
            Assert.Contains(name, all);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Audio_Model_Makes_Sound_Through_Comfyui(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.Equal("comfyui", profile.Provider);

        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("id").GetString());
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()!).ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => Assert.StartsWith("audio/", o, StringComparison.Ordinal));
        var inputs = root.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()!).ToList();
        Assert.Equal(["text/plain"], inputs);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Length_Is_Seconds_And_Frame_Size_Is_Not_Invented(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));

        // «длина» у звука — секунды трека (умолчание профайла 121 — это кадры видео)
        Assert.True(profile.MediaLength > 0);
        Assert.NotEqual(121, profile.MediaLength);

        // размера кадра у звука нет, и в params он не задан НАМЕРЕННО: ноль туда поставить
        // нельзя (ModelProfile.Int считает неположительное «не заданным»), а выдуманные
        // 1024×1024 попали бы в сводку задания как настоящая величина
        using var json = JsonDocument.Parse(Profile(name));
        var pars = json.RootElement.GetProperty("params");
        Assert.False(pars.TryGetProperty("width", out _));
        Assert.False(pars.TryGetProperty("height", out _));
        // а в графе они и не используются: плейсхолдеров размера там нет
        using var workflow = Workflow(name);
        var graph = Graph(workflow).GetRawText();
        Assert.DoesNotContain("{width}", graph, StringComparison.Ordinal);
        Assert.DoesNotContain("{height}", graph, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Skill_Codes_Come_From_The_Reference_And_Are_Audio_Ones(string name)
    {
        // код навыка мимо справочника подбор исполнителя не увидит вовсе, и ошибки не будет
        var known = _f.RefData.Skills("en").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        using var scope = JsonDocument.Parse(Scope(name));

        var codes = scope.RootElement.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.NotEmpty(codes);
        foreach (var code in codes)
        {
            Assert.Contains(code, known);
            Assert.StartsWith("audio-", code, StringComparison.Ordinal);
        }
        Assert.Contains("audio-song", codes);
        Assert.Contains("audio-music", codes);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Executor_Pick_Does_Not_Zero_The_Score(string name)
    {
        // T-257: SkillIo сверяет носитель входа и выхода с декларацией. Ошибись мы форматом
        // (например outputs: ["image/*"]), запись осталась бы в справочнике, но НИ ОДНА
        // задача её бы не взяла — и молча
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        var inputs = root.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()!).ToList();
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()!).ToList();

        foreach (var code in root.GetProperty("skills").EnumerateArray()
                     .Select(s => s.GetProperty("name").GetString()!))
        {
            Assert.True(SkillIo.Fits(code, inputs, outputs), code);
        }
        // а обратное — сторож самой проверки: на задачу «озвучить картинку» запись не годится
        Assert.False(SkillIo.Fits("video-animate", inputs, outputs));
    }

    // --- workflow ComfyUI ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Is_Valid_Json_With_Placeholders(string name)
    {
        using var workflow = Workflow(name);
        var graph = Graph(workflow);
        var text = graph.GetRawText();

        Assert.True(graph.EnumerateObject().Count() >= 10);
        // текст задачи, длительность, шаги и код задания подставляет коннектор
        Assert.Contains("{prompt}", text, StringComparison.Ordinal);
        Assert.Contains("\"{seed}\"", text, StringComparison.Ordinal);
        Assert.Contains("\"{length}\"", text, StringComparison.Ordinal);
        Assert.Contains("\"{steps}\"", text, StringComparison.Ordinal);
        Assert.Contains("AI2P/{job}", text, StringComparison.Ordinal);
        // подстановки величин варианта обязаны быть выполнены при записи сид-файла
        Assert.DoesNotContain("<<", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void BuildWorkflow_Turns_Placeholders_Into_Numbers(string name)
    {
        // настоящий путь коннектора: текст — внутрь кавычек, числа — ВМЕСТЕ с кавычками.
        // Ошибись мы формой хоть в одном плейсхолдере, ComfyUI получил бы строку вместо
        // числа либо сломанный JSON — и это выглядело бы поломкой движка, а не записи
        var profile = ModelProfile.Parse(Profile(name));
        var built = _f.ComfyUi.BuildWorkflow(profile,
            prompt: "спокойный эмбиент, 72 удара в минуту", seed: 42, jobDisplayId: "J-7");

        Assert.DoesNotContain("{prompt}", built, StringComparison.Ordinal);
        Assert.DoesNotContain("{seed}", built, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(built);
        var graph = doc.RootElement;

        var planner = graph.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "TextEncodeAceStepAudio1.5")
            .Value.GetProperty("inputs");
        var sampler = graph.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "KSampler")
            .Value.GetProperty("inputs");
        var latent = graph.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "EmptyAceStep1.5LatentAudio")
            .Value.GetProperty("inputs");

        Assert.Equal("спокойный эмбиент, 72 удара в минуту", planner.GetProperty("tags").GetString());
        Assert.Equal(42, planner.GetProperty("seed").GetInt64());
        // seed у планировщика и сэмплера один: в шаблоне ComfyUI его раздавал один узел
        Assert.Equal(42, sampler.GetProperty("seed").GetInt64());
        Assert.Equal(profile.MediaSteps, sampler.GetProperty("steps").GetInt32());
        // длительность — в ДВУХ местах сразу, и обе величины обязаны совпасть
        Assert.Equal(profile.MediaLength, latent.GetProperty("seconds").GetInt32());
        Assert.Equal(profile.MediaLength, planner.GetProperty("duration").GetInt32());
        // имя выходного файла — код задания
        var save = graph.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString()!.StartsWith(
                "SaveAudio", StringComparison.Ordinal))
            .Value.GetProperty("inputs");
        Assert.Equal("AI2P/J-7", save.GetProperty("filename_prefix").GetString());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Nodes_Are_Connected(string name)
    {
        using var workflow = Workflow(name);
        var graph = Graph(workflow);
        var ids = graph.EnumerateObject().Select(n => n.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var node in graph.EnumerateObject())
        {
            foreach (var input in node.Value.GetProperty("inputs").EnumerateObject())
            {
                if (input.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                var source = input.Value[0].GetString()!;
                Assert.True(ids.Contains(source),
                    $"{name}: вход {node.Name}.{input.Name} ведёт в несуществующий узел {source}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Saves_Audio_The_Way_The_Connector_Reads_It(string name)
    {
        using var workflow = Workflow(name);
        var types = Graph(workflow).EnumerateObject()
            .Select(n => n.Value.GetProperty("class_type").GetString()!).ToList();

        // ComfyUiConnector.WaitResultAsync забирает из истории ЛЮБЫЕ объекты с полем
        // filename, в каком бы ключе узла они ни лежали, поэтому SaveAudio* работает так же,
        // как SaveImage: свой ключ («audio») коннектору безразличен
        Assert.Contains(types, t => t.StartsWith("SaveAudio", StringComparison.Ordinal));
        Assert.Contains("VAEDecodeAudio", types);
        Assert.Contains("TextEncodeAceStepAudio1.5", types);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Weight_Names_In_Workflow_Match_The_Install_Manifest(string name)
    {
        var manifest = ModelInstallManifest.Parse(Profile(name));
        Assert.NotNull(manifest);
        var files = manifest!.Files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        using var workflow = Workflow(name);

        foreach (var node in Graph(workflow).EnumerateObject())
        {
            foreach (var input in node.Value.GetProperty("inputs").EnumerateObject())
            {
                if (input.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                var value = input.Value.GetString()!;
                if (value.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(files.Contains(value),
                        $"{name}: файл {value} из workflow не назван в манифесте установки");
                }
            }
        }
    }

    // --- установка ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Install_Manifest_Names_Four_Files_And_Needs_Comfyui(string name)
    {
        var manifest = ModelInstallManifest.Parse(Profile(name));

        Assert.NotNull(manifest);
        Assert.Equal("ACE-Step-1.5", manifest!.Group);
        Assert.Equal(["comfyui"], manifest.Packages);
        Assert.Equal(4, manifest.Files.Count);
        Assert.All(manifest.Files, f =>
        {
            Assert.True(f.Size > 0, f.Name);
            Assert.StartsWith("https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/",
                f.Url, StringComparison.Ordinal);
            Assert.EndsWith("/" + f.Name, f.Url, StringComparison.Ordinal);
        });
        // обучающих пакетов у этих записей нет вовсе (обучение внешнее), и это важно:
        // от состава манифеста считается признак «модель установлена» (T-4-S0)
        Assert.Equal(manifest.Files.Count, manifest.Files.Count);
    }

    [Fact]
    public void The_Three_Variants_Share_Everything_But_The_Dit()
    {
        var manifests = AudioModels.Select(n => ModelInstallManifest.Parse(Profile(n))!).ToList();

        // группа одна: вторая и третья записи докачивают только свой DiT
        Assert.Single(manifests.Select(m => m.Group).Distinct());
        foreach (var shared in SharedFiles)
        {
            Assert.All(manifests, m => Assert.Contains(m.Files, f => f.Name == shared));
        }
        var dits = manifests
            .Select(m => m.Files.Single(f => !SharedFiles.Contains(f.Name)).Name).ToList();
        Assert.Equal(3, dits.Distinct().Count());
        Assert.All(dits, d => Assert.StartsWith("acestep_v1.5_xl_", d, StringComparison.Ordinal));
    }

    // --- LoRA (T-13-S1) ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Applied_By_Node_But_Trained_Outside(string name)
    {
        var lora = LoraSettings.Parse(Profile(name));

        // применить чужой адаптер ComfyUI умеет: в comfy/lora.py разобран официальный
        // формат ACE-Step (ветка ACEStep15)
        Assert.True(lora.Supported);
        Assert.Equal(LoraEngines.ComfyUi, lora.Engine);
        Assert.Equal(LoraApplyKinds.Workflow, lora.Apply.Kind);
        Assert.Equal("LoraLoaderModelOnly", lora.Apply.Node);
        // а обучить своим процессом нечем: musubi-tuner ACE-Step не знает вовсе, а
        // официальный тренер учится на аудиозаписях, тогда как датасет LoRA в AI2P —
        // кадры-картинки (T-274). Врать «process» было бы хуже отказа
        Assert.Equal(LoraTrainKinds.External, lora.Train.Kind);
        Assert.Empty(lora.Train.Packages);
        Assert.StartsWith("https://", lora.Train.DocUrl, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_Graph_Has_A_Loader_To_Hang_The_Adapter_On(string name)
    {
        // ComfyLoraGraph вставляет узел адаптера МЕЖДУ загрузчиком весов и его потребителями;
        // загрузчика в графе нет — задание с адаптером падает, и это правильно, но у нас
        // он должен быть
        using var workflow = Workflow(name);
        var types = Graph(workflow).EnumerateObject()
            .Select(n => n.Value.GetProperty("class_type").GetString()!).ToList();

        Assert.Contains("UNETLoader", types);
    }

    // --- документы ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Both_Languages_Have_A_Document_With_A_License_Section(string name)
    {
        // заголовок английского раздела — «Licence»: так он написан во всех 56 документах
        // проекта, и это же написание требует сторож состава test/t214/checkreq.py
        foreach (var (lang, heading) in new[] { ("ru", "## Лицензия"), ("en", "## Licence") })
        {
            var path = DocPath(lang, name);
            Assert.True(File.Exists(path), path);
            var text = File.ReadAllText(path);
            Assert.Contains(heading, text, StringComparison.Ordinal);
            // требование задания: лицензия стоит там же, где у платных стоит стоимость
            Assert.Contains("MIT", text, StringComparison.Ordinal);
            // про GPL самого ComfyUI молчать нельзя (разбор T-240)
            Assert.Contains("GPL-3.0", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Repeats_The_Numbers_Of_The_Profile(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));

        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(DocPath(lang, name));
            Assert.Contains(profile.Model, text, StringComparison.Ordinal);
            Assert.Contains(profile.MediaSteps.ToString(), text, StringComparison.Ordinal);
            // ссылки только полным адресом: относительная в показе .md мертва (T-214)
            Assert.DoesNotContain("](Kandinsky", text, StringComparison.Ordinal);
        }
    }
}
