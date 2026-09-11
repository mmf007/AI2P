using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-15-S0 (локальные ВИДЕО-модели через ComfyUI, §6.1 и §6.3 отчёта
/// <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>): справочник пополнен открытыми
/// линейками Wan 2.2, HunyuanVideo 1.5 и LTX-2.5, а у Kandinsky 5.0 Video Lite заведены
/// недостающие варианты — 10-секундные, no-CFG и distil16.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, коды навыков
/// из справочника, связность узлов workflow, совпадение имён весов в шаблоне с манифестом
/// установки (иначе ComfyUI ответит «model not found» уже во время задания), настройка
/// LoRA (применение И обучение), догрузка весов обучения там, где генерация считает на
/// fp8, и наличие документов на обоих языках с разделом «Лицензия».
/// </summary>
public sealed class T15S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием.</summary>
    public static readonly string[] VideoModels =
    [
        "Wan-2.2-T2V-A14B", "Wan-2.2-I2V-A14B", "HunyuanVideo-1.5-720p-T2V", "LTX-2.5",
        "Kandinsky-5.0-T2V-Lite-sft-10s", "Kandinsky-5.0-T2V-Lite-nocfg-5s",
        "Kandinsky-5.0-T2V-Lite-nocfg-10s", "Kandinsky-5.0-T2V-Lite-distil16-5s",
        "Kandinsky-5.0-T2V-Lite-distil16-10s",
    ];

    /// <summary>Варианты Kandinsky Lite: имя → (число шагов, сила следования, кадров).</summary>
    public static readonly Dictionary<string, (int Steps, int Cfg, int Length)> Variants = new()
    {
        ["Kandinsky-5.0-T2V-Lite-sft-10s"] = (50, 5, 241),
        ["Kandinsky-5.0-T2V-Lite-nocfg-5s"] = (50, 1, 121),
        ["Kandinsky-5.0-T2V-Lite-nocfg-10s"] = (50, 1, 241),
        ["Kandinsky-5.0-T2V-Lite-distil16-5s"] = (16, 1, 121),
        ["Kandinsky-5.0-T2V-Lite-distil16-10s"] = (16, 1, 241),
    };

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in VideoModels)
        {
            data.Add(name);
        }
        return data;
    }

    public static TheoryData<string> VariantNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Variants.Keys)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T15S0Tests()
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

    private static string TrainScript(string profileJson) =>
        string.Join("\r\n", ModelProfile.Parse(profileJson).Lora.Train.Files
            .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text);

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
    public void Catalogue_Has_Every_Video_Model()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in VideoModels)
        {
            Assert.Contains(expected, names);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Video_Model_Draws_Clips_Through_Comfyui(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.Equal("comfyui", profile.Provider);

        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("id").GetString());
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.Equal(["video/*"], outputs);

        // у ролика кадров много: единица — умолчание картинки, и сводка задания сказала бы
        // человеку «кадров: 1» про пятисекундное видео
        Assert.True(profile.MediaLength > 1, name + ": " + profile.MediaLength);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Skill_Codes_Come_From_The_Reference_And_Are_Video_Ones(string name)
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
            Assert.StartsWith("video-", code, StringComparison.Ordinal);
        }
    }

    // --- workflow ComfyUI ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Is_Written_By_Seed_And_Its_Links_Lead_Somewhere(string name)
    {
        using var wf = Workflow(name);
        var nodes = wf.RootElement.GetProperty("prompt");

        var ids = nodes.EnumerateObject().Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(ids);
        foreach (var node in nodes.EnumerateObject())
        {
            Assert.NotEqual("", node.Value.GetProperty("class_type").GetString());
            foreach (var input in node.Value.GetProperty("inputs").EnumerateObject())
            {
                if (input.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                // ссылка на другой узел — пара [id, слот]; висящая ссылка роняет весь граф
                var target = input.Value[0].GetString()!;
                Assert.Contains(target, ids);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Saves_A_Video_And_Names_The_Job(string name)
    {
        using var wf = Workflow(name);
        var nodes = wf.RootElement.GetProperty("prompt");

        var save = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "SaveVideo");
        var prefix = save.Value.GetProperty("inputs").GetProperty("filename_prefix").GetString()!;
        Assert.Contains("{job}", prefix, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Weights_Are_Declared_In_The_Install_Manifest(string name)
    {
        // имя файла весов, которого нет в манифесте, ломает не установку, а САМО ЗАДАНИЕ:
        // ComfyUI отвечает «model not found», и со стороны это выглядит поломкой движка
        using var profileDoc = JsonDocument.Parse(Profile(name));
        var declared = profileDoc.RootElement.GetProperty("install").GetProperty("files")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        using var wf = Workflow(name);
        foreach (var node in wf.RootElement.GetProperty("prompt").EnumerateObject())
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
                    Assert.Contains(value, declared);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Has_Every_Placeholder_The_Connector_Substitutes(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        var raw = _f.Files.ReadText(profile.Workflow);

        foreach (var placeholder in new[] { "{prompt}", "{job}", "\"{seed}\"",
                                            "\"{width}\"", "\"{height}\"", "\"{length}\"" })
        {
            Assert.Contains(placeholder, raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_The_Image_To_Video_Model_Asks_For_A_Start_Frame()
    {
        // рисование по тексту стартового кадра не требует, «оживление картинки» без него
        // бессмысленно: задание должно отказываться сразу, а не рисовать что-то своё
        foreach (var name in VideoModels.Where(n => n != "Wan-2.2-I2V-A14B"))
        {
            Assert.Equal("none", ModelProfile.Parse(Profile(name)).RefImage.Kind);
        }

        var i2v = ModelProfile.Parse(Profile("Wan-2.2-I2V-A14B"));
        Assert.Equal("upload", i2v.RefImage.Kind);
        Assert.True(i2v.RefImage.Required);

        using var wf = Workflow("Wan-2.2-I2V-A14B");
        var load = wf.RootElement.GetProperty("prompt").EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "LoadImage");
        Assert.Equal(i2v.RefImage.Placeholder,
            load.Value.GetProperty("inputs").GetProperty("image").GetString());
    }

    [Fact]
    public void Wan_Runs_Two_Experts_And_Hands_The_Latent_Over()
    {
        // Wan 2.2 14B — модель составная: «шумный» эксперт считает первые шаги и отдаёт
        // латент «чистому». Перепутанный порядок даёт мутный ролик, а не ошибку
        foreach (var name in new[] { "Wan-2.2-T2V-A14B", "Wan-2.2-I2V-A14B" })
        {
            using var wf = Workflow(name);
            var nodes = wf.RootElement.GetProperty("prompt");
            var samplers = nodes.EnumerateObject()
                .Where(n => n.Value.GetProperty("class_type").GetString() == "KSamplerAdvanced")
                .ToList();
            Assert.Equal(2, samplers.Count);

            var first = samplers.Single(s => s.Value.GetProperty("inputs")
                .GetProperty("add_noise").GetString() == "enable");
            var second = samplers.Single(s => s.Value.GetProperty("inputs")
                .GetProperty("add_noise").GetString() == "disable");
            Assert.Equal("enable", first.Value.GetProperty("inputs")
                .GetProperty("return_with_leftover_noise").GetString());
            Assert.Equal(first.Name, second.Value.GetProperty("inputs")
                .GetProperty("latent_image")[0].GetString());
            // граница экспертов у второго начинается там же, где кончается первый
            Assert.Equal(first.Value.GetProperty("inputs").GetProperty("end_at_step").GetInt32(),
                second.Value.GetProperty("inputs").GetProperty("start_at_step").GetInt32());

            var loaders = nodes.EnumerateObject()
                .Where(n => n.Value.GetProperty("class_type").GetString() == "UNETLoader")
                .Select(n => n.Value.GetProperty("inputs").GetProperty("unet_name").GetString()!)
                .ToList();
            Assert.Equal(2, loaders.Count);
            Assert.Single(loaders.Where(f => f.Contains("high_noise", StringComparison.Ordinal)));
            Assert.Single(loaders.Where(f => f.Contains("low_noise", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Ltx_Draws_Sound_Together_With_The_Picture()
    {
        // ради этого LTX-2.5 и заводилась: единственная открытая модель, у которой звук
        // считается тем же проходом. Потеря звукового латента даёт немой ролик без ошибки
        using var wf = Workflow("LTX-2.5");
        var nodes = wf.RootElement.GetProperty("prompt");
        var classes = nodes.EnumerateObject()
            .Select(n => n.Value.GetProperty("class_type").GetString()!).ToList();

        Assert.Contains("LTXVEmptyLatentAudio", classes);
        Assert.Contains("LTXVConcatAVLatent", classes);
        Assert.Contains("LTXVSeparateAVLatent", classes);
        Assert.Contains("LTXVAudioVAEDecode", classes);

        var video = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "CreateVideo");
        var audioSource = video.Value.GetProperty("inputs").GetProperty("audio")[0].GetString()!;
        Assert.Equal("LTXVAudioVAEDecode",
            nodes.GetProperty(audioSource).GetProperty("class_type").GetString());
    }

    [Theory]
    [MemberData(nameof(VariantNames))]
    public void Kandinsky_Variant_Carries_Its_Own_Weights_Steps_And_Guidance(string name)
    {
        var (steps, cfg, length) = Variants[name];
        var profile = ModelProfile.Parse(Profile(name));

        Assert.Equal(steps, profile.MediaSteps);
        Assert.Equal(length, profile.MediaLength);

        using var wf = Workflow(name);
        var nodes = wf.RootElement.GetProperty("prompt");
        var sampler = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "KSampler");
        Assert.Equal(cfg, sampler.Value.GetProperty("inputs").GetProperty("cfg").GetInt32());

        var unet = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "UNETLoader")
            .Value.GetProperty("inputs").GetProperty("unet_name").GetString()!;
        // технический плейсхолдер шаблона обязан быть подставлен: с ним ComfyUI искал бы
        // файл с именем «{unet}» и отвечал бы «model not found»
        Assert.DoesNotContain("{", unet, StringComparison.Ordinal);
        Assert.EndsWith(".safetensors", unet, StringComparison.Ordinal);
    }

    [Fact]
    public void Kandinsky_Variants_Share_The_Group_But_Not_The_Weights()
    {
        // текстовые энкодеры и VAE у всей линейки одни и те же файлы — группа общая с уже
        // работающими записями, второй раз их качать незачем; свой у варианта только DiT
        var dits = new List<string>();
        foreach (var name in Variants.Keys)
        {
            using var doc = JsonDocument.Parse(Profile(name));
            var install = doc.RootElement.GetProperty("install");
            Assert.Equal("Kandinsky-5", install.GetProperty("group").GetString());

            var own = install.GetProperty("files").EnumerateArray()
                .Where(f => f.GetProperty("category").GetString() == "diffusion_models")
                .Select(f => f.GetProperty("name").GetString()!).ToList();
            Assert.Single(own);
            dits.Add(own[0]);
        }
        Assert.Equal(dits.Count, dits.Distinct(StringComparer.Ordinal).Count());
    }

    // --- LoRA (требование задания: секция заполняется целиком либо честно отказывает) ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Applied_By_A_Node_In_Every_Record(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        Assert.True(lora.Supported);
        Assert.Equal(LoraApplyKinds.Workflow, lora.Apply.Kind);
        Assert.Equal("LoraLoaderModelOnly", lora.Apply.Node);
        Assert.Equal("loras", lora.Apply.Dir);
        Assert.NotEqual("", lora.Train.DocUrl);
        Assert.StartsWith("loras/", lora.Train.Result.Target, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Runs_As_A_Process_Unless_There_Is_No_Trainer(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        if (name == "LTX-2.5")
        {
            // тренера LTX-2.5 у musubi-tuner нет вовсе, поэтому обучение объявлено внешним:
            // человек получает внятный отказ со ссылкой, а не «строка полчаса повисела»
            Assert.Equal(LoraTrainKinds.External, lora.Train.Kind);
            Assert.Empty(lora.Train.Packages);
            Assert.Empty(lora.Train.Files);
            return;
        }

        Assert.Equal(LoraTrainKinds.Process, lora.Train.Kind);
        Assert.Contains("musubi-tuner", lora.Train.Packages);
        Assert.Contains("python", lora.Train.Packages);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Script_Checks_Every_Step_And_Holds_No_Cyrillic(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;
        if (lora.Train.Kind == LoraTrainKinds.External)
        {
            return;
        }
        // .cmd читается консолью в OEM-кодировке: кириллица в нём превращается в мусор
        var text = TrainScript(Profile(name));

        Assert.DoesNotContain(text, c => c >= 'А' && c <= 'я');
        // три шага обучения (кеш латентов, кеш текстового энкодера, само обучение) —
        // после каждого проверяется код возврата, иначе падение первого шага молча
        // доводит до конца и «обучение» кончается отсутствием файла
        Assert.True(text.Split("if errorlevel 1 exit /b 1").Length - 1 >= 3, text);
        Assert.Contains("{steps}", text, StringComparison.Ordinal);
        Assert.Contains("{object}", text, StringComparison.Ordinal);
        Assert.Contains("exit /b 0", text, StringComparison.Ordinal);
        Assert.Single(lora.Train.Files.Where(
            f => f.Path.EndsWith("dataset.toml", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Task_Matches_The_Model(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;
        if (lora.Train.Kind == LoraTrainKinds.External)
        {
            return;
        }
        // перепутанный скрипт тренера — это часы счёта и негодный адаптер в конце
        var text = TrainScript(Profile(name));
        var (script, task) = name switch
        {
            "Wan-2.2-T2V-A14B" => ("wan_train_network.py", "--task t2v-A14B"),
            "Wan-2.2-I2V-A14B" => ("wan_train_network.py", "--task i2v-A14B"),
            "HunyuanVideo-1.5-720p-T2V" => ("hv_1_5_train_network.py", "--task t2v"),
            "Kandinsky-5.0-T2V-Lite-sft-10s" => ("kandinsky5_train_network.py",
                "--task k5-lite-t2v-10s-sd"),
            "Kandinsky-5.0-T2V-Lite-nocfg-5s" => ("kandinsky5_train_network.py",
                "--task k5-lite-t2v-5s-nocfg-sd"),
            "Kandinsky-5.0-T2V-Lite-nocfg-10s" => ("kandinsky5_train_network.py",
                "--task k5-lite-t2v-10s-nocfg-sd"),
            "Kandinsky-5.0-T2V-Lite-distil16-5s" => ("kandinsky5_train_network.py",
                "--task k5-lite-t2v-5s-distil-sd"),
            _ => ("kandinsky5_train_network.py", "--task k5-lite-t2v-10s-distil-sd"),
        };

        Assert.Contains(script, text, StringComparison.Ordinal);
        Assert.Contains(task, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Training_Fetches_Its_Own_Weights_Where_Generation_Runs_On_Fp8()
    {
        // musubi-tuner прямо говорит, что fp8_scaled-сборки обучению не годятся, а в
        // манифестах стоят именно они: тренер догружает свою пару сам, первым запуском
        foreach (var name in new[] { "Wan-2.2-T2V-A14B", "Wan-2.2-I2V-A14B" })
        {
            var text = TrainScript(Profile(name));
            Assert.Contains("fp16.safetensors", text, StringComparison.Ordinal);
            Assert.Contains("models_t5_umt5-xxl-enc-bf16.pth", text, StringComparison.Ordinal);
            Assert.Contains("hf.exe", text, StringComparison.Ordinal);
            Assert.Contains("{groupDir}", text, StringComparison.Ordinal);
        }

        // у Hunyuan причина та же, но файлы другие: оригинальный DiT и полный энкодер
        var hunyuan = TrainScript(Profile("HunyuanVideo-1.5-720p-T2V"));
        Assert.Contains("tencent/HunyuanVideo-1.5", hunyuan, StringComparison.Ordinal);
        Assert.Contains("qwen_2.5_vl_7b.safetensors", hunyuan, StringComparison.Ordinal);

        // а вариантам Kandinsky догружать нечего: их веса и так не fp8, а текстовые
        // энкодеры тренер берёт сам с HuggingFace по имени репозитория
        foreach (var name in Variants.Keys)
        {
            Assert.DoesNotContain("hf.exe", TrainScript(Profile(name)), StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Takes_Weights_From_The_Manifest(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;
        if (lora.Train.Kind == LoraTrainKinds.External)
        {
            return;
        }
        using var doc = JsonDocument.Parse(Profile(name));
        var declared = doc.RootElement.GetProperty("install").GetProperty("files")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        // {model:файл} раскрывается в <каталог группы>/<файл>: имя мимо манифеста означает
        // путь в никуда, и обучение упадёт через полчаса подготовки, а не сразу
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(
                     TrainScript(Profile(name)), @"\{model:([^}]+)\}"))
        {
            Assert.Contains(m.Groups[1].Value, declared);
        }
    }

    // --- манифест установки ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Install_Manifest_Names_Sizes_And_Categories(string name)
    {
        using var doc = JsonDocument.Parse(Profile(name));
        var install = doc.RootElement.GetProperty("install");
        Assert.NotEqual("", install.GetProperty("group").GetString());
        Assert.Contains("comfyui", install.GetProperty("packages").EnumerateArray()
            .Select(p => p.GetString()));

        var files = install.GetProperty("files").EnumerateArray().ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            // точный размер — это и докачка по Range, и признак «модель установлена»
            Assert.True(file.GetProperty("size").GetInt64() > 0, name);
            Assert.StartsWith("https://huggingface.co/",
                file.GetProperty("url").GetString()!, StringComparison.Ordinal);
            Assert.Contains(file.GetProperty("category").GetString(),
                new[] { "diffusion_models", "text_encoders", "vae", "clip_vision", "loras" });
        }
    }

    [Fact]
    public void Shared_Files_Keep_The_Same_Size_Across_Records()
    {
        // файлы лежат в каталоге группы плоско: разъехавшийся размер одного и того же
        // файла означал бы вечную докачку и «модель то установлена, то нет»
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var model in _f.Models.List())
        {
            var profileJson = _f.Files.ReadText(model.ProfilePath);
            using var doc = JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("install", out var install) ||
                install.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            foreach (var file in install.GetProperty("files").EnumerateArray())
            {
                var key = file.GetProperty("url").GetString()!;
                var size = file.GetProperty("size").GetInt64();
                if (sizes.TryGetValue(key, out var known))
                {
                    Assert.Equal(known, size);
                }
                else
                {
                    sizes[key] = size;
                }
            }
        }
    }

    // --- документы ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Exists_In_Both_Languages(string name)
    {
        Assert.True(File.Exists(DocPath("ru", name)), DocPath("ru", name));
        Assert.True(File.Exists(DocPath("en", name)), DocPath("en", name));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Has_The_Licence_Section_Next_To_The_Money_One(string name)
    {
        // раздел лицензии стоит там же, где у платных моделей стоит стоимость: человек
        // ищет «во что мне это обойдётся» в одном месте, а у локальной модели ответ —
        // не деньги, а условия лицензии
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("## Лицензия", ru, StringComparison.Ordinal);
        Assert.Contains("## Licence", en, StringComparison.Ordinal);
        // ComfyUI под GPL-3.0 — про это в документе локальной медиа-модели молчать нельзя
        Assert.Contains("GPL-3.0", ru, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0", en, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Answers_The_Two_Obligatory_Questions(string name)
    {
        // правило проекта: документ модели обязан отвечать, как её подключить (у локальной
        // это установка) и какие требования к железу
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("Требования к железу", ru, StringComparison.Ordinal);
        Assert.Contains("Hardware requirements", en, StringComparison.Ordinal);
        Assert.Contains("LoRA", ru, StringComparison.Ordinal);
        Assert.Contains("LoRA", en, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Links_Are_Absolute(string name)
    {
        // относительная ссылка в поставляемом документе мёртвая (T-214): окно документа
        // рисуется внутри страницы приложения, и нажатие уводит на 404
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(DocPath(lang, name));
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"\]\(([^)]+)\)"))
            {
                var href = m.Groups[1].Value;
                Assert.True(href.StartsWith("https://", StringComparison.Ordinal)
                            || href.StartsWith("http://", StringComparison.Ordinal),
                    name + " (" + lang + "): " + href);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Repeats_The_Numbers_Of_The_Record(string name)
    {
        // документ и справочник расходятся молча (T-214): ни один тест не сверяет цифры,
        // а правят их в разных местах — поэтому здесь сверяются размер кадра и длина ролика
        var profile = ModelProfile.Parse(Profile(name));
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(DocPath(lang, name));
            Assert.Contains(profile.MediaWidth.ToString(), text, StringComparison.Ordinal);
            Assert.Contains(profile.MediaHeight.ToString(), text, StringComparison.Ordinal);
            Assert.Contains(profile.MediaLength.ToString(), text, StringComparison.Ordinal);
        }
    }
}
