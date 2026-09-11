using System.Text;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-289: ПЕРВЫЙ ЗАПУСК ОБУЧЕНИЯ LoRA — «в справочнике нет указания, как обучать
/// Kandinsky-5.0-I2V-Lite-5s».
///
/// Причина жалобы была ровно одна: у обеих записей Kandinsky настройка <c>lora.train</c>
/// стояла с ПУСТОЙ командой (T-13-S1 намеренно не выдумывал путь к чужому репозиторию),
/// и первое же нажатие «обучить» отвечало отказом. Теперь команда есть, а вместе с ней —
/// две вещи, без которых одной командой не обойтись:
/// <list type="number">
/// <item>ФАЙЛЫ НАСТРОЙКИ тренера (<c>train.files</c>): у всякого обучающего репозитория
/// половина ответа лежит не в командной строке, а в файле конфигурации;</item>
/// <item>ПАКЕТ обучения (<c>train.packages</c>): сам тренер ставится по требованию, при
/// первом запуске обучения, а не манифестом установки модели — от манифеста зависит
/// признак «модель установлена», и у всех, у кого модель уже работает, она бы погасла.</item>
/// </list>
/// </summary>
public sealed class T289Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Kandinsky T2V и I2V из дистрибутива — записи, у которых объявлена работа с LoRA.</summary>
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";
    private const string I2VId = "6f1a45e0-0d31-4c65-9a01-000000000032";

    // --- 1. Справочник знает, чем обучать ---

    [Theory]
    [InlineData(T2VId, "k5-lite-t2v-5s-sd", "Kandinsky-5.0-T2V-Lite-sft-5s.safetensors")]
    [InlineData(I2VId, "k5-lite-i2v-5s-sd", "kandinsky5lite_i2v_5s.safetensors")]
    public void A_Kandinsky_Record_Says_How_To_Train_Lora(string modelId, string task, string weights)
    {
        _f.Models.Seed();
        var lora = LoraSettings.Parse(File.ReadAllText(_f.Files.Abs(_f.Models.Get(modelId)!.ProfilePath)));

        Assert.True(lora.Supported);
        Assert.Equal(LoraTrainKinds.Process, lora.Train.Kind);
        // ГЛАВНОЕ этой задачи: команда обучения перестала быть пустой
        Assert.NotEqual("", lora.Train.Start.Command);
        Assert.Equal(LoraStartKinds.Process, lora.Train.Start.Kind);
        Assert.NotEqual("", lora.Train.Start.WorkDir);
        Assert.Contains("musubi-tuner", lora.Train.Packages);
        Assert.NotEqual("", lora.Train.DocUrl);

        // файлы настройки: конфигурация датасета и запускающий скрипт
        var names = lora.Train.Files.Select(f => Path.GetFileName(f.Path)).ToList();
        Assert.Contains("dataset.toml", names);
        Assert.Contains("train.cmd", names);

        // у каждой записи СВОИ веса и своя задача тренера: перепутать их значит обучить
        // адаптер под чужую модель, и заметно это будет только по испорченному ролику
        var script = lora.Train.Files.Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text;
        Assert.Contains("--task " + task, script);
        Assert.Contains(weights, script);
        // адаптер забирается оттуда, куда его кладёт тренер, и ложится туда, где его ищет движок
        Assert.Contains("{object}", lora.Train.Result.Path);
        Assert.Equal("loras/{object}.safetensors", lora.Train.Result.Target);
    }

    [Fact]
    public void The_Training_Script_Does_All_Three_Steps_And_Stops_On_The_First_Failure()
    {
        _f.Models.Seed();
        var lora = LoraSettings.Parse(File.ReadAllText(_f.Files.Abs(_f.Models.Get(I2VId)!.ProfilePath)));
        var script = lora.Train.Files.Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text;

        // у musubi-tuner обучение — это ТРИ шага подряд, и пропуск любого из них даёт
        // не ошибку, а молчаливо пустой адаптер
        Assert.Contains("kandinsky5_cache_latents.py", script);
        Assert.Contains("kandinsky5_cache_text_encoder_outputs.py", script);
        Assert.Contains("kandinsky5_train_network.py", script);
        // каждый шаг проверяется: без этого упавший кэш увёл бы обучение дальше, а конец
        // процесса пришёл бы с кодом ноль — и AI2P записал бы «обучено»
        Assert.True(script.Split("\r\n").Count(l => l.Trim() == "if errorlevel 1 exit /b 1") >= 3);
        // одна видеокарта и sdpa: flash-attention на Windows не ставится, а NCCL там нет вовсе
        Assert.Contains("--sdpa", script);
        // кириллицы в командном файле быть не должно: консоль читает .cmd в кодировке OEM
        Assert.DoesNotContain(script, c => c > 127);
    }

    // --- 2. Пакет тренера в справочнике пакетов ---

    [Fact]
    public void The_Trainer_Package_Is_In_The_Catalog_And_Pinned_To_A_Tag()
    {
        _f.Models.Seed();
        var catalog = ModelPackageCatalog.Parse(
            File.ReadAllText(_f.Files.Abs(AiModelService.PackagesPath)));

        var package = catalog.Find("musubi-tuner");
        Assert.NotNull(package);
        Assert.Equal("kandinsky5_train_network.py", package!.Check);
        Assert.NotEqual("", package.Hint);
        var file = Assert.Single(package.Files);
        Assert.Equal("zip", file.Unpack);
        // ссылка с ТЕГОМ, а не releases/latest: иначе версия тренера у каждого своя,
        // и разбор жалобы «у меня обучение падает» становится гаданием (наука T-240)
        Assert.Contains("/tags/", file.Url);
        Assert.DoesNotContain("latest", file.Url);
    }

    [Fact]
    public void The_Trainer_Package_Is_Declared_By_The_Lora_Settings_Not_By_The_Install_Manifest()
    {
        _f.Models.Seed();
        foreach (var id in new[] { T2VId, I2VId })
        {
            var manifest = ModelInstallManifest.Parse(
                File.ReadAllText(_f.Files.Abs(_f.Models.Get(id)!.ProfilePath)))!;
            // объявлен тренер по-прежнему в настройке LoRA — там всё про адаптеры (T-13-S1)
            Assert.DoesNotContain("musubi-tuner", manifest.Packages);
            Assert.Contains("comfyui", manifest.Packages);
            // а ставится с T-4-S0 вместе с моделью: список установки — install + train
            Assert.Contains("musubi-tuner", manifest.AllPackages);
        }
    }

    // --- 3. Файлы настройки пишутся перед запуском ---

    [Fact]
    public async Task The_Setup_Files_Are_Written_Before_The_Command_Runs()
    {
        var projectDir = ProjectDir();
        WriteProfile(files: """
            {
              "path": "lora/{object}/dataset.toml",
              "text": [
                "[general]",
                "resolution = [{width}, {height}]",
                "",
                "[[datasets]]",
                "image_directory = '{dataset}'"
              ]
            }
            """);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой в плаще");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitAsync(row.Id);
        Assert.Equal("", done.Error);
        Assert.Equal(LoraModelStates.Ready, done.Status);

        var toml = Path.Combine(projectDir, "lora", lora.DisplayId, "dataset.toml");
        Assert.True(File.Exists(toml));
        var text = File.ReadAllText(toml);
        // подстановки те же, что и в команде: иначе в каждом поле пришлось бы помнить,
        // что в нём работает
        Assert.Contains("resolution = [768, 512]", text);
        // путь датасета ПОЛНЫЙ: конфигурацию читает не наш код, и относительный путь
        // разворачивался бы от рабочего каталога тренера, а не от папки проекта.
        // Разделители в нём смешаны (корень от системы, хвост из настройки) — это законно,
        // Windows одинаково понимает оба
        Assert.Contains("image_directory = '" + projectDir, text);
        Assert.Contains("lora/" + lora.DisplayId + "/dataset'", text);
        // массив строк — это строки, а не одна склейка: файл читает python и cmd.exe
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("{width}", text);

        // и написан он БЫЛ ДО команды: команда сама скопировала его себе в результат
        Assert.True(File.Exists(Path.Combine(_f.ModelsRepo, "loras", lora.DisplayId + ".safetensors")));
    }

    [Fact]
    public async Task Model_Paths_Reach_The_Setup_Files_And_The_Command()
    {
        var projectDir = ProjectDir();
        WriteProfile(files: """
            {
              "path": "lora/{object}/paths.txt",
              "text": ["dit={model:weights.safetensors}"]
            }
            """);
        // пути установки знает установщик моделей, а не сервис обучения: связь делегатом
        _f.LoraTrain.ModelPaths = (_, value) =>
            value.Replace("{model:weights.safetensors}", @"D:\repo\models\Kandinsky-5\weights.safetensors");
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        await WaitAsync(row.Id);

        var text = File.ReadAllText(Path.Combine(projectDir, "lora", lora.DisplayId, "paths.txt"));
        Assert.Equal(@"dit=D:\repo\models\Kandinsky-5\weights.safetensors", text.Trim());
    }

    /// <summary>
    /// С T-4-S0 обучение пакеты не СТАВИТ, а спрашивает: они приходят с установкой модели.
    /// Проверка того, что спрашивает именно свой список, осталась здесь — она про настройку.
    /// </summary>
    [Fact]
    public async Task Training_Asks_About_Its_Packages_Before_The_Start()
    {
        var projectDir = ProjectDir();
        WriteProfile(packages: "\"musubi-tuner\"");
        var asked = new List<string>();
        _f.LoraTrain.MissingPackages = ids =>
        {
            asked.AddRange(ids);
            return [];
        };
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        await WaitAsync(row.Id);

        Assert.Equal(new[] { "musubi-tuner" }, asked);
    }

    [Fact]
    public void A_Setup_File_Text_May_Be_A_Single_String()
    {
        var lora = LoraSettings.Parse("""
            {
              "lora": {
                "supported": true,
                "train": { "files": [ { "path": "a.txt", "text": "one line" },
                                      { "text": "без пути — не файл" } ] }
              }
            }
            """);
        // безымянный файл в список не попадает: класть его некуда
        var file = Assert.Single(lora.Train.Files);
        Assert.Equal("a.txt", file.Path);
        Assert.Equal("one line", file.Text);
    }

    [Fact]
    public async Task A_Failed_Training_Reports_Both_Output_Streams()
    {
        var projectDir = ProjectDir();
        // оболочка пишет своё в stderr, а что делать человеку — печатает скрипт в stdout:
        // по одному stderr первый запуск обучения чинить нечем (найдено пробой T-289)
        WriteProfile(command: OperatingSystem.IsWindows()
            ? "echo AI2P-HINT-OUT& echo AI2P-HINT-ERR 1>&2& exit /b 7"
            : "echo AI2P-HINT-OUT; echo AI2P-HINT-ERR 1>&2; exit 7");
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitAsync(row.Id);

        Assert.Equal(LoraModelStates.Error, done.Status);
        Assert.Contains("AI2P-HINT-OUT", done.Error);
        Assert.Contains("AI2P-HINT-ERR", done.Error);
        Assert.Contains("7", done.Error);
    }

    // --- 4. Словари ---

    [Fact]
    public void The_New_Message_Has_A_Text_In_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = Loc.In(lang, "msg.lora.32", "C:\\p\\dataset.toml", "нет доступа");
            Assert.NotEqual("msg.lora.32", text);
            Assert.Contains("dataset.toml", text);
        }
    }

    // --- общее ---

    private string ProjectDir()
    {
        var dir = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private ObjectItem NewLora(string projectId, string name) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = ObjectKinds.Lora,
        }, null);

    private ObjectItem AddFrame(string projectId, string ownerId, string fileName, string caption)
    {
        var rel = "lora-src/" + fileName;
        var abs = Path.Combine(ProjectDir(), "lora-src", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, "png", new UTF8Encoding(false));
        return _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = ownerId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Type = ObjectKinds.Image,
            PathOrUrl = rel,
            Description = caption,
        }, null);
    }

    /// <summary>
    /// Профайл модели с настройкой обучения: файлы настройки и команда, которая делает вид,
    /// что обучила адаптер, — она же и доказывает ПОРЯДОК шагов, потому что кладёт в результат
    /// файл, написанный шагом настройки.
    /// </summary>
    private void WriteProfile(string files = "", string packages = "", string command = "")
    {
        _f.Models.Seed();
        var model = _f.Models.Get(T2VId)!;
        command = command.Length > 0 ? command : OperatingSystem.IsWindows()
            ? "mkdir \\\"{output}\\\" 2>nul & echo weights> \\\"{output}\\\\adapter.safetensors\\\""
            : "mkdir -p '{output}' && echo weights > '{output}/adapter.safetensors'";
        _f.Files.WriteText(model.ProfilePath, $$"""
            {
              "provider": "comfyui",
              "model": "test",
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": { "kind": "workflow", "node": "LoraLoaderModelOnly", "dir": "loras" },
                "train": {
                  "kind": "process",
                  "packages": [{{packages}}],
                  "dataset": { "kind": "dir", "path": "lora/{object}/dataset",
                               "captions": "txt", "minItems": 1, "width": 768, "height": 512 },
                  "files": [{{files}}],
                  "start": { "kind": "process", "command": "{{command}}", "steps": 10 },
                  "wait": { "kind": "process", "timeoutMinutes": 5 },
                  "result": { "kind": "file", "path": "lora/{object}/out/adapter.safetensors",
                              "target": "loras/{object}.safetensors" }
                }
              }
            }
            """);
    }

    private async Task<ObjectLoraModel> WaitAsync(string loraId)
    {
        for (var i = 0; i < 300; i++)
        {
            var row = _f.Objects.LoraModel(loraId)!;
            if (row.Status != LoraModelStates.Training)
            {
                return row;
            }
            await Task.Delay(100);
        }
        return _f.Objects.LoraModel(loraId)!;
    }
}
