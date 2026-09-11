using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-20-S0 (локальные 3D-модели, §6.5 отчёта <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>):
/// справочник пополнен тремя записями «картинка → 3D» через уже работающий коннектор
/// <c>comfyui</c>. До этого задания навыки <c>3d-*</c> закрывали только облачные записи
/// (Tripo и Meshy через fal.ai, T-14-S0) — локальных не было ни одной.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, коды навыков
/// из справочника, СОВМЕСТИМОСТЬ объявленных навыков с парой inputs/outputs по правилам
/// <see cref="SkillIo"/> (иначе запись видна в справочнике, но подбор исполнителя её
/// никогда не возьмёт), разбираемость workflow и связность его узлов, совпадение имён
/// весов в шаблоне с манифестом установки, честный отказ по LoRA и наличие документов на
/// обоих языках с разделом «Лицензия».
///
/// Живьём ни одна модель не считалась: нужны видеокарта NVIDIA и 3,5–9 ГБ весов. Что имена
/// УЗЛОВ и их ВХОДОВ существуют в ядре ComfyUI, проверяет <c>test/t20s0/check3d.py</c> по
/// исходникам движка — тестам исходники ComfyUI недоступны.
/// </summary>
public sealed class T20S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием.</summary>
    public static readonly string[] Models3D =
    [
        "Hunyuan3D-2.1", "TRELLIS-2", "TripoSplat",
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Models3D)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T20S0Tests()
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
    public void Catalogue_Has_The_Three_Local_3D_Models()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in Models3D)
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Local_3D_Is_The_First_One_In_The_Catalogue()
    {
        // до этого задания ни одна ЛОКАЛЬНАЯ запись не закрывала 3d-*: были только
        // облачные Tripo и Meshy через fal.ai (T-14-S0)
        var local = new List<string>();
        foreach (var model in _f.Models.List())
        {
            using var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));
            var has3d = scope.RootElement.GetProperty("skills").EnumerateArray()
                .Any(s => s.GetProperty("name").GetString()!.StartsWith("3d-", StringComparison.Ordinal));
            if (has3d && ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath)).Provider == "comfyui")
            {
                local.Add(model.Name);
            }
        }

        Assert.Equal(Models3D.OrderBy(n => n), local.OrderBy(n => n));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Model_Builds_3D_Through_Comfyui(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.Equal("comfyui", profile.Provider);

        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("id").GetString());
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()!).ToList();
        Assert.All(outputs, o => Assert.StartsWith("model/", o, StringComparison.Ordinal));

        // кадр один: умолчание профайла — 121 кадр (видео), и сводка задания сказала бы
        // человеку «кадров: 121» на одной-единственной модели
        Assert.Equal(1, profile.MediaLength);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Skill_Codes_Come_From_The_Reference_And_Are_3D_Ones(string name)
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
            Assert.StartsWith("3d-", code, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Declared_Skills_Survive_The_Executor_Pick_Rules(string name)
    {
        // главная ловушка записи «картинка → 3D»: с T-257 подбор исполнителя обнуляет
        // оценку медиа-навыка, если вход навыка не принимается объявленными inputs.
        // 3d-generate и 3d-environment по SkillIo идут ОТ ТЕКСТА, 3d-texture — от готовой
        // сетки; объявить их у записи, которая работает от картинки, значит завести
        // запись, которую подбор не возьмёт НИКОГДА, и молча
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        var inputs = root.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()!).ToList();
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()!).ToList();

        foreach (var skill in root.GetProperty("skills").EnumerateArray())
        {
            var code = skill.GetProperty("name").GetString()!;
            Assert.NotNull(SkillIo.For(code));
            Assert.True(SkillIo.Fits(code, inputs, outputs), $"{name}: {code}");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_Model_Takes_A_Starting_Picture(string name)
    {
        // все три записи работают ТОЛЬКО от картинки: текстового энкодера в их графах нет
        // вовсе, поэтому без стартового кадра задание должно отказываться сразу, а не
        // выдавать модель, слепленную из ничего
        var profile = ModelProfile.Parse(Profile(name));

        Assert.Equal("upload", profile.RefImage.Kind);
        Assert.True(profile.RefImage.Required);
        Assert.Equal("/upload/image", profile.RefImage.UploadPath);

        using var wf = Workflow(name);
        Assert.Contains(profile.RefImage.Placeholder, wf.RootElement.GetRawText(), StringComparison.Ordinal);
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
                Assert.Contains(input.Value[0].GetString()!, ids);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Saves_A_3D_File_And_Names_The_Job(string name)
    {
        // коннектор забирает результат по ЛЮБОМУ узлу, отдавшему объект с полем filename
        // (SaveGLB кладёт его в ui.3d) — значит .glb и .spz едут той же дорогой, что
        // картинки и ролики, и правки коннектора это не потребовало
        using var wf = Workflow(name);
        var nodes = wf.RootElement.GetProperty("prompt");

        var save = nodes.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == "SaveGLB");
        var prefix = save.Value.GetProperty("inputs").GetProperty("filename_prefix").GetString()!;
        Assert.Contains("{job}", prefix, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Workflow_Holds_No_Text_Placeholder(string name)
    {
        // в графах 3D нет текстового энкодера вовсе: условие даёт CLIP Vision по картинке.
        // Плейсхолдер {prompt} в таком шаблоне означал бы, что описание задачи на что-то
        // влияет, — а оно не влияет
        using var wf = Workflow(name);

        Assert.DoesNotContain("{prompt}", wf.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("{negative}", wf.RootElement.GetRawText(), StringComparison.Ordinal);
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

        var used = new HashSet<string>(StringComparer.Ordinal);
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
                    used.Add(value);
                    Assert.Contains(value, declared);
                }
            }
        }

        // и обратное: лишний файл манифеста — это десятки гигабайт, которые качаются зря
        Assert.Equal(declared.OrderBy(f => f), used.OrderBy(f => f));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Install_Manifest_Is_Readable_And_Every_File_Has_A_Size(string name)
    {
        // размер нужен точный: по нему считается «скачано ли» и докачка по HTTP Range
        var manifest = ModelInstallManifest.Parse(Profile(name));

        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest!.Group);
        Assert.Contains("comfyui", manifest.Packages);
        Assert.NotEmpty(manifest.Files);
        Assert.All(manifest.Files, f =>
        {
            Assert.True(f.Size > 0, f.Name);
            Assert.NotEmpty(f.Category);
            Assert.StartsWith("https://huggingface.co/", f.Url, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Groups_Of_The_Three_Models_Are_Their_Own()
    {
        // группа — это каталог весов: общая группа у моделей с общими файлами (так сделаны
        // варианты Kandinsky), а у этих трёх общих файлов нет, кроме birefnet — и он
        // качается в каждую группу свой, потому что группа удаляется целиком
        var groups = Models3D
            .Select(n => ModelInstallManifest.Parse(Profile(n))!.Group).ToList();

        Assert.Equal(groups.Count, groups.Distinct(StringComparer.Ordinal).Count());
    }

    // --- LoRA ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Refused_With_A_Named_Reason(string name)
    {
        // публичного тренера LoRA для 3D-архитектур сегодня нет: у musubi-tuner, на котором
        // держится всё обучение AI2P, есть только картинки и видео. Пустая настройка
        // выглядела бы как недоделка — стоит честный отказ с причиной, и форма модели
        // показывает его человеку словами
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        Assert.False(lora.Supported);
        Assert.Equal(LoraReasons.NoLora, lora.Reason);
        // «unknown» здесь не годится: по правилу T-13-S1 это «настройка не заполнена»,
        // то есть работа, а не ответ, и сторож Every_Seeded_Model_Has_A_Lora_Setting
        // такую запись честно красит
        Assert.NotEqual(LoraReasons.Unknown, lora.Reason);
        Assert.Contains(lora.Reason, LoraReasons.All);
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

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Says_The_Model_Reads_A_Picture_And_Not_The_Text(string name)
    {
        // самая частая будущая жалоба по этим записям: «написал описание, а модель его не
        // послушала». Документ обязан сказать это прямо
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("3d-image", ru, StringComparison.Ordinal);
        Assert.Contains("3d-image", en, StringComparison.Ordinal);
        Assert.Contains("@obj:", ru, StringComparison.Ordinal);
        Assert.Contains("@obj:", en, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_Languages_List_The_New_Models_In_The_Catalogue_Readme()
    {
        var ru = File.ReadAllText(Path.Combine(AppDir(), "doc", "ru", "models", "README.md"));
        var en = File.ReadAllText(Path.Combine(AppDir(), "doc", "en", "models", "README.md"));

        foreach (var name in Models3D)
        {
            Assert.Contains($"({name}.md)", ru, StringComparison.Ordinal);
            Assert.Contains($"({name}.md)", en, StringComparison.Ordinal);
        }
    }

    // --- справочник форматов ---

    [Fact]
    public void Reference_Knows_The_3D_Formats_Declared_By_The_Records()
    {
        // мелочь, которую легко пропустить: формат, названный в декларации, но не
        // заведённый в справочнике io formats, редактор возможностей просто не покажет
        var known = _f.RefData.IoFormats("ru").Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in Models3D)
        {
            using var scope = JsonDocument.Parse(Scope(name));
            foreach (var key in new[] { "inputs", "outputs" })
            {
                foreach (var format in scope.RootElement.GetProperty(key).EnumerateArray())
                {
                    Assert.Contains(format.GetString()!, known);
                }
            }
        }
    }
}
