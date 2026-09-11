using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-241 (подключение ИИ медиа-моделей, §6 отчёта <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>):
/// справочник пополнен ЛОКАЛЬНЫМИ моделями изображений через ComfyUI. До этого задания
/// навыки <c>image-*</c> не закрывала ни одна запись — медиа были только видео.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, коды навыков
/// из справочника, разбираемость workflow и связность его узлов, совпадение имён весов в
/// шаблоне с манифестом установки (иначе ComfyUI ответит «model not found» уже во время
/// задания), настройка LoRA (применение И обучение — требование задания) и наличие
/// документов на обоих языках с разделом «Лицензия».
/// </summary>
public sealed class T241Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием.</summary>
    public static readonly string[] ImageModels =
    [
        "Z-Image-Turbo", "Qwen-Image-2512", "Qwen-Image-Edit-2511",
    ];

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

    public T241Tests()
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
    public void Catalogue_Has_The_Three_Image_Models()
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
    public void Only_The_Editing_Model_Asks_For_A_Source_Picture()
    {
        // рисование с нуля исходной картинки не требует, правка без неё бессмысленна:
        // задание должно отказываться сразу, а не выдавать пустой результат
        Assert.Equal("none", ModelProfile.Parse(Profile("Z-Image-Turbo")).RefImage.Kind);
        Assert.Equal("none", ModelProfile.Parse(Profile("Qwen-Image-2512")).RefImage.Kind);

        var edit = ModelProfile.Parse(Profile("Qwen-Image-Edit-2511"));
        Assert.Equal("upload", edit.RefImage.Kind);
        Assert.True(edit.RefImage.Required);

        using var wf = Workflow("Qwen-Image-Edit-2511");
        var load = wf.RootElement.GetProperty("prompt").EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "LoadImage");
        Assert.Equal(edit.RefImage.Placeholder,
            load.Value.GetProperty("inputs").GetProperty("image").GetString());
    }

    // --- LoRA (требование задания: описать использование с учётом T-289) ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Applied_By_A_Node_And_Trained_By_A_Process(string name)
    {
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        Assert.True(lora.Supported);
        Assert.Equal(LoraApplyKinds.Workflow, lora.Apply.Kind);
        Assert.Equal("LoraLoaderModelOnly", lora.Apply.Node);
        Assert.Equal("loras", lora.Apply.Dir);

        Assert.Equal(LoraTrainKinds.Process, lora.Train.Kind);
        Assert.Contains("musubi-tuner", lora.Train.Packages);
        Assert.Contains("python", lora.Train.Packages);
        Assert.NotEqual("", lora.Train.DocUrl);
        Assert.StartsWith("loras/", lora.Train.Result.Target, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Script_Checks_Every_Step_And_Holds_No_Cyrillic(string name)
    {
        // .cmd читается консолью в OEM-кодировке: кириллица в нём превращается в мусор
        var files = ModelProfile.Parse(Profile(name)).Lora.Train.Files;
        var cmd = files.Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal));
        var text = string.Join("\r\n", cmd.Text);

        Assert.DoesNotContain(text, c => c >= 'А' && c <= 'я');
        // три шага обучения (кеш латентов, кеш текстового энкодера, само обучение) —
        // после каждого проверяется код возврата, иначе падение первого шага молча
        // доводит до конца и «обучение» кончается отсутствием файла
        Assert.True(text.Split("if errorlevel 1 exit /b 1").Length - 1 >= 3, text);
        Assert.Contains("{steps}", text, StringComparison.Ordinal);
        Assert.Contains("{object}", text, StringComparison.Ordinal);
        Assert.Contains("exit /b 0", text, StringComparison.Ordinal);
        Assert.Single(files.Where(f => f.Path.EndsWith("dataset.toml", StringComparison.Ordinal)));
    }

    [Fact]
    public void Qwen_Training_Fetches_Bf16_Weights_Because_Fp8_Ones_Do_Not_Fit()
    {
        // musubi-tuner прямо говорит, что fp8-сборки (ими считает генерация) обучению
        // не годятся: тренер берёт свою пару bf16, и это должно быть видно в скрипте
        foreach (var name in new[] { "Qwen-Image-2512", "Qwen-Image-Edit-2511" })
        {
            var cmd = ModelProfile.Parse(Profile(name)).Lora.Train.Files
                .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal));
            var text = string.Join("\r\n", cmd.Text);

            Assert.Contains("bf16.safetensors", text, StringComparison.Ordinal);
            Assert.Contains("hf.exe", text, StringComparison.Ordinal);
        }

        // у Z-Image причина другая: turbo — дистиллят, тренер учит на базовых весах
        var zimage = string.Join("\r\n", ModelProfile.Parse(Profile("Z-Image-Turbo")).Lora.Train.Files
            .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text);
        Assert.Contains("z_image_bf16.safetensors", zimage, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Training_Task_Matches_The_Model(string name)
    {
        // перепутанный скрипт тренера — это часы счёта и негодный адаптер в конце
        var expected = name switch
        {
            "Z-Image-Turbo" => "zimage_train_network.py",
            _ => "qwen_image_train_network.py",
        };
        var text = string.Join("\r\n", ModelProfile.Parse(Profile(name)).Lora.Train.Files
            .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        if (name == "Qwen-Image-Edit-2511")
        {
            Assert.Contains("--model_version edit-2511", text, StringComparison.Ordinal);
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
        Assert.Contains("Apache 2.0", ru, StringComparison.Ordinal);
        Assert.Contains("Apache 2.0", en, StringComparison.Ordinal);
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
