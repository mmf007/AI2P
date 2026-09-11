using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-19-S0 (вторая очередь локальных моделей изображений, §6.2 и §7.2 отчёта
/// <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>): к трём записям T-241 добавлены
/// FLUX.2 [klein] 4B в двух режимах, FLUX.2 [dev], Kandinsky 5.0 Image Lite и нижняя
/// ступень для слабого железа — SD 3.5 Large и SDXL 1.0.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, коды навыков
/// из справочника, связность графа и совпадение имён весов с манифестом установки, ОБЩАЯ
/// группа установки у записей одной модели, вид работы с LoRA (у половины записей тренера
/// нет вовсе — тогда обещать команду нельзя), закрытость репозиториев весов и документы
/// на обоих языках с разделом «Лицензия», где у несвободных весов сказано об этом прямо.
/// </summary>
public sealed class T19S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием.</summary>
    public static readonly string[] ImageModels =
    [
        "FLUX.2-klein-4B", "FLUX.2-klein-4B-Edit", "Kandinsky-5.0-Image-Lite",
        "SD-3.5-Large", "SDXL-1.0", "FLUX.2-dev",
    ];

    /// <summary>Записи, обучающие адаптер СВОИМ процессом (musubi-tuner).</summary>
    private static readonly string[] Trainable = ["FLUX.2-klein-4B", "FLUX.2-klein-4B-Edit"];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in ImageModels)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T19S0Tests()
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
    public void Catalogue_Has_The_Second_Wave_Of_Image_Models()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in ImageModels)
        {
            Assert.Contains(expected, names);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Image_Model_Draws_Pictures_Through_Comfyui(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.Equal("comfyui", profile.Provider);

        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("id").GetString());
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.Equal(["image/*"], outputs);

        // кадр у картинки один: умолчание профайла — 121 кадр (видео), и сводка задания
        // сказала бы человеку «кадров: 121» на одной-единственной картинке
        Assert.Equal(1, profile.MediaLength);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Skill_Codes_Come_From_The_Reference_And_Are_Image_Ones(string name)
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
            Assert.StartsWith("image-", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_Weak_Hardware_Step_Is_Really_The_Lightest_One()
    {
        // SDXL заведена ровно как нижняя ступень: если она вдруг станет тяжелее соседей,
        // смысла в записи не останется вовсе
        Assert.True(GroupSize("SDXL-1.0") < GroupSize("SD-3.5-Large"));
        Assert.True(GroupSize("SD-3.5-Large") < GroupSize("FLUX.2-klein-4B"));
        Assert.True(GroupSize("FLUX.2-klein-4B") < GroupSize("FLUX.2-dev"));
    }

    private long GroupSize(string name)
    {
        using var doc = JsonDocument.Parse(Profile(name));
        return doc.RootElement.GetProperty("install").GetProperty("files").EnumerateArray()
            .Sum(f => f.GetProperty("size").GetInt64());
    }

    // --- установка ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Install_Manifest_Names_Sizes_And_Known_Categories(string name)
    {
        using var doc = JsonDocument.Parse(Profile(name));
        var install = doc.RootElement.GetProperty("install");
        Assert.Equal("comfyui", install.GetProperty("packages")[0].GetString());

        foreach (var file in install.GetProperty("files").EnumerateArray())
        {
            // размер сверяется при скачивании побайтно (T-4-S0), ноль означал бы «качать
            // нечего» и модель считалась бы установленной сразу
            Assert.True(file.GetProperty("size").GetInt64() > 0);
            Assert.StartsWith("https://huggingface.co/", file.GetProperty("url").GetString()!,
                StringComparison.Ordinal);
            var category = file.GetProperty("category").GetString();
            Assert.Contains(category, new[] { "diffusion_models", "text_encoders", "vae", "checkpoints" });
        }
    }

    [Fact]
    public void Weights_Come_From_Repositories_Open_To_Anonymous_Download()
    {
        // ГЛАВНАЯ находка задания: репозитории Black Forest Labs ЗАКРЫТЫ — resolve отвечает
        // 401 без токена (проверено 27.08.2026), поэтому ни один манифест не вправе на них
        // ссылаться: скачивание молча падало бы, а модель навсегда осталась бы неустановленной.
        // Открытые перепаковки тех же весов лежат у Comfy-Org, оттуда мы и качаем.
        foreach (var model in _f.Models.List())
        {
            var profile = _f.Files.ReadText(model.ProfilePath);
            if (profile.Length == 0)
            {
                continue;
            }
            Assert.DoesNotContain("huggingface.co/black-forest-labs", profile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Two_Modes_Of_One_Model_Share_The_Install_Group()
    {
        // веса у генерации и правки картинки одни и те же — модель одна. Разойдись эти два
        // манифеста, вторая запись группы молча перетёрла бы файлы первой, и человек качал
        // бы 16 ГБ дважды
        using var draw = JsonDocument.Parse(Profile("FLUX.2-klein-4B"));
        using var edit = JsonDocument.Parse(Profile("FLUX.2-klein-4B-Edit"));

        var a = draw.RootElement.GetProperty("install");
        var b = edit.RootElement.GetProperty("install");
        Assert.Equal(a.GetProperty("group").GetString(), b.GetProperty("group").GetString());
        Assert.Equal(a.GetProperty("files").GetRawText(), b.GetProperty("files").GetRawText());
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
                var target = input.Value[0].GetString()!;
                Assert.Contains(target, ids);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Saves_An_Image_And_Names_The_Job(string name)
    {
        using var wf = Workflow(name);
        var nodes = wf.RootElement.GetProperty("prompt");

        var save = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "SaveImage");
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

    [Fact]
    public void Flux2_Family_Schedules_Steps_By_Frame_Size()
    {
        // у FLUX.2 расписание сигм зависит от РАЗМЕРА кадра, поэтому шаги живут не в
        // сэмплере, а в Flux2Scheduler: подставь {steps} не туда — и число шагов молча
        // перестанет действовать вовсе
        foreach (var name in new[] { "FLUX.2-klein-4B", "FLUX.2-klein-4B-Edit", "FLUX.2-dev" })
        {
            using var wf = Workflow(name);
            var scheduler = wf.RootElement.GetProperty("prompt").EnumerateObject()
                .Single(n => n.Value.GetProperty("class_type").GetString() == "Flux2Scheduler");
            Assert.Equal("{steps}", scheduler.Value.GetProperty("inputs").GetProperty("steps").GetString());
        }
    }

    [Fact]
    public void Only_The_Editing_Model_Asks_For_A_Source_Picture()
    {
        foreach (var name in ImageModels.Where(n => n != "FLUX.2-klein-4B-Edit"))
        {
            Assert.Equal("none", ModelProfile.Parse(Profile(name)).RefImage.Kind);
        }

        var edit = ModelProfile.Parse(Profile("FLUX.2-klein-4B-Edit"));
        Assert.Equal("upload", edit.RefImage.Kind);
        Assert.True(edit.RefImage.Required);

        using var wf = Workflow("FLUX.2-klein-4B-Edit");
        var load = wf.RootElement.GetProperty("prompt").EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "LoadImage");
        Assert.Equal(edit.RefImage.Placeholder,
            load.Value.GetProperty("inputs").GetProperty("image").GetString());

        // размер кадра у правки берётся у самой картинки (GetImageSize), а не из настроек
        Assert.Contains(wf.RootElement.GetProperty("prompt").EnumerateObject(),
            n => n.Value.GetProperty("class_type").GetString() == "GetImageSize");
    }

    [Fact]
    public void The_Editing_Model_Declares_A_Picture_On_Input()
    {
        // пара inputs/outputs участвует в подборе исполнителя (SkillIo, T-257): без
        // «image/*» на входе правка картинки не бралась бы на такую задачу вовсе
        using var edit = JsonDocument.Parse(Scope("FLUX.2-klein-4B-Edit"));
        var inputs = edit.RootElement.GetProperty("inputs").EnumerateArray()
            .Select(i => i.GetString()).ToList();
        Assert.Contains("image/*", inputs);

        foreach (var name in ImageModels.Where(n => n != "FLUX.2-klein-4B-Edit"))
        {
            using var scope = JsonDocument.Parse(Scope(name));
            var others = scope.RootElement.GetProperty("inputs").EnumerateArray()
                .Select(i => i.GetString()).ToList();
            Assert.DoesNotContain("image/*", others);
        }
    }

    // --- LoRA ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Applied_By_A_Node_In_Every_Record(string name)
    {
        // применение адаптера работает у всех записей ComfyUI, включая те, что ставятся
        // одним чекпойнтом: узел вставки ищется и после CheckpointLoaderSimple (T-14-S1)
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
    public void Training_Is_Promised_Only_Where_A_Trainer_Exists(string name)
    {
        // у SDXL, SD 3.5, Kandinsky Image и FLUX.2 [dev] тренера нет: musubi-tuner их либо
        // не знает вовсе (Image Lite — прямая оговорка его docs/kandinsky5.md), либо
        // требует закрытых оригинальных весов. Обещать команду в таком случае нельзя —
        // задание встало бы «обучается» и упало на первой строке
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        if (Trainable.Contains(name))
        {
            Assert.Equal(LoraTrainKinds.Process, lora.Train.Kind);
            Assert.Contains("musubi-tuner", lora.Train.Packages);
            Assert.Contains("python", lora.Train.Packages);
            Assert.Equal("train.cmd", lora.Train.Start.Command);
            return;
        }
        Assert.Equal(LoraTrainKinds.External, lora.Train.Kind);
        Assert.Equal("", lora.Train.Start.Command);
        Assert.Empty(lora.Train.Files);
        Assert.Empty(lora.Train.Packages);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Script_Checks_Every_Step_And_Holds_No_Cyrillic(string name)
    {
        if (!Trainable.Contains(name))
        {
            return;
        }
        // .cmd читается консолью в OEM-кодировке: кириллица в нём превращается в мусор
        var files = ModelProfile.Parse(Profile(name)).Lora.Train.Files;
        var cmd = files.Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal));
        var text = string.Join("\r\n", cmd.Text);

        Assert.DoesNotContain(text, c => c >= 'А' && c <= 'я');
        Assert.True(text.Split("if errorlevel 1 exit /b 1").Length - 1 >= 3, text);
        Assert.Contains("{steps}", text, StringComparison.Ordinal);
        Assert.Contains("{object}", text, StringComparison.Ordinal);
        Assert.Contains("exit /b 0", text, StringComparison.Ordinal);
        Assert.Single(files.Where(f => f.Path.EndsWith("dataset.toml", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Klein_Trains_On_The_Very_Files_It_Installs(string name)
    {
        if (!Trainable.Contains(name))
        {
            return;
        }
        // отличие от Z-Image и Qwen-Image (T-241): там генерация считает на fp8, которых
        // тренер не принимает, и train.cmd докачивает пару bf16. У klein 4B в манифесте
        // лежит сам базовый чекпойнт — докачивать нечего, и лишнего hf download быть не должно
        var profile = ModelProfile.Parse(Profile(name));
        using var doc = JsonDocument.Parse(Profile(name));
        var declared = doc.RootElement.GetProperty("install").GetProperty("files")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var text = string.Join("\r\n", profile.Lora.Train.Files
            .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text);

        Assert.Contains("flux_2_train_network.py", text, StringComparison.Ordinal);
        Assert.Contains("--network_module networks.lora_flux_2", text, StringComparison.Ordinal);
        // вариант весов назвать ОБЯЗАТЕЛЬНО: без --model_version тренер считает, что это dev
        Assert.Contains("--model_version klein-base-4b", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hf.exe", text, StringComparison.Ordinal);

        foreach (var asked in System.Text.RegularExpressions.Regex.Matches(text, @"\{model:([^}]+)\}")
                     .Select(m => m.Groups[1].Value))
        {
            Assert.Contains(asked, declared);
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
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("## Лицензия", ru, StringComparison.Ordinal);
        Assert.Contains("## Licence", en, StringComparison.Ordinal);
        // ComfyUI под GPL-3.0 — про это в документе локальной медиа-модели молчать нельзя
        Assert.Contains("GPL-3.0", ru, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0", en, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_Free_Weights_Are_Called_Non_Free_In_Plain_Words()
    {
        // ради этого случая раздел «Лицензия» и заводили: FLUX.2 [dev] можно брать только
        // для некоммерческой работы, а SD 3.5 бесплатна лишь до выручки в 1 млн долларов
        var devRu = File.ReadAllText(DocPath("ru", "FLUX.2-dev"));
        var devEn = File.ReadAllText(DocPath("en", "FLUX.2-dev"));
        Assert.Contains("некоммерческ", devRu, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-commercial", devEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FLUX Non-Commercial License", devRu, StringComparison.Ordinal);
        Assert.Contains("FLUX Non-Commercial License", devEn, StringComparison.Ordinal);

        var sdRu = File.ReadAllText(DocPath("ru", "SD-3.5-Large"));
        var sdEn = File.ReadAllText(DocPath("en", "SD-3.5-Large"));
        Assert.Contains("1 млн", sdRu, StringComparison.Ordinal);
        Assert.Contains("$1M", sdEn, StringComparison.Ordinal);

        // а у свободных записей лицензия названа своим именем
        Assert.Contains("Apache 2.0", File.ReadAllText(DocPath("ru", "FLUX.2-klein-4B")),
            StringComparison.Ordinal);
        Assert.Contains("MIT", File.ReadAllText(DocPath("ru", "Kandinsky-5.0-Image-Lite")),
            StringComparison.Ordinal);
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
        Assert.Contains("Установить", ru, StringComparison.Ordinal);
        Assert.Contains("Install", en, StringComparison.Ordinal);
        Assert.Contains("LoRA", ru, StringComparison.Ordinal);
        Assert.Contains("LoRA", en, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Repeats_The_File_Names_Of_The_Manifest(string name)
    {
        // документ и справочник расходятся молча (наука T-214): имя файла весов —
        // ровно то место, по которому человек ищет недокачанное
        using var profileDoc = JsonDocument.Parse(Profile(name));
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        foreach (var file in profileDoc.RootElement.GetProperty("install")
                     .GetProperty("files").EnumerateArray())
        {
            var fileName = file.GetProperty("name").GetString()!;
            Assert.Contains(fileName, ru, StringComparison.Ordinal);
            Assert.Contains(fileName, en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_Languages_List_The_New_Models_In_The_Catalogue_Readme()
    {
        var ru = File.ReadAllText(Path.Combine(AppDir(), "doc", "ru", "models", "README.md"));
        var en = File.ReadAllText(Path.Combine(AppDir(), "doc", "en", "models", "README.md"));

        foreach (var name in ImageModels)
        {
            Assert.Contains($"({name}.md)", ru, StringComparison.Ordinal);
            Assert.Contains($"({name}.md)", en, StringComparison.Ordinal);
        }
    }
}
