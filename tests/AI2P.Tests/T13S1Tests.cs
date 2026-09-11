using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-13-S1 (версия 1.95): СПРАВОЧНИК МОДЕЛЕЙ — настройка работы с LoRA и с референсной
/// картинкой.
///
/// Что проверяется:
/// <list type="number">
/// <item>РАЗБОР настройки из профайла: пять шагов работы с адаптером (датасет, запуск
/// обучения, ожидание, результат, подключение к модели) читаются, неполнота и мусор
/// разбор не роняют;</item>
/// <item>СИД: настройка есть у КАЖДОЙ записи дистрибутива — и у поддерживающих LoRA,
/// и у остальных (там это отметка «нельзя, потому что…»);</item>
/// <item>СПРАВОЧНИК: отметка доезжает до списка, которым рисуется таблица настроек
/// (колонка «LoRA»), и до одиночной записи, по которой открывается форма;</item>
/// <item>РЕФЕРЕНСНАЯ КАРТИНКА: у i2v-записи она обязательна и заливается в движок,
/// у t2v — передать её нечем;</item>
/// <item>СЛОВАРИ: у каждого кода (вид подключения, вид обучения, причина отказа, вид
/// передачи картинки) есть текст в ОБОИХ языках.</item>
/// </list>
/// </summary>
public sealed class T13S1Tests
{
    private const string ComfyProfile = """
        {
          "provider": "comfyui",
          "model": "kandinsky5lite_i2v_5s",
          "lora": {
            "supported": true,
            "engine": "comfyui",
            "apply": {
              "kind": "workflow",
              "node": "LoraLoaderModelOnly",
              "placeholder": "{lora}",
              "strengthPlaceholder": "{loraStrength}",
              "strength": 0.8,
              "dir": "loras",
              "maxCount": 2,
              "formats": [".safetensors"]
            },
            "train": {
              "kind": "process",
              "docUrl": "https://example.invalid/train",
              "dataset": {
                "kind": "dir",
                "path": "lora/{object}/dataset",
                "captions": "txt",
                "minItems": 10,
                "maxItems": 60,
                "width": 768,
                "height": 512
              },
              "start": { "kind": "process", "command": "train.py", "workDir": "C:/tools", "steps": 2000 },
              "wait": { "kind": "process", "timeoutMinutes": 600 },
              "result": {
                "kind": "file",
                "path": "lora/{object}/out/adapter_model.safetensors",
                "target": "loras/{object}.safetensors"
              }
            }
          },
          "refImage": {
            "kind": "upload",
            "placeholder": "{image}",
            "uploadPath": "/upload/image",
            "field": "image",
            "maxCount": 1,
            "formats": ["image/png"],
            "required": true
          }
        }
        """;

    // ---------- 1. разбор настройки ----------

    /// <summary>Все пять шагов работы с адаптером читаются из профайла.</summary>
    [Fact]
    public void All_Five_Steps_Of_Lora_Work_Are_Read_From_The_Profile()
    {
        var lora = LoraSettings.Parse(ComfyProfile);
        Assert.True(lora.Supported);
        Assert.Equal(LoraEngines.ComfyUi, lora.Engine);

        // шаг 5 — подключение адаптера к модели
        Assert.Equal(LoraApplyKinds.Workflow, lora.Apply.Kind);
        Assert.Equal("LoraLoaderModelOnly", lora.Apply.Node);
        Assert.Equal("{lora}", lora.Apply.Placeholder);
        Assert.Equal("{loraStrength}", lora.Apply.StrengthPlaceholder);
        Assert.Equal(0.8, lora.Apply.Strength);
        Assert.Equal("loras", lora.Apply.Dir);
        Assert.Equal(2, lora.Apply.MaxCount);
        Assert.Equal([".safetensors"], lora.Apply.Formats);

        // шаг 1 — датасет
        Assert.Equal(LoraTrainKinds.Process, lora.Train.Kind);
        Assert.Equal(LoraDatasetKinds.Dir, lora.Train.Dataset.Kind);
        Assert.Equal("lora/{object}/dataset", lora.Train.Dataset.Path);
        Assert.Equal("txt", lora.Train.Dataset.Captions);
        Assert.Equal(10, lora.Train.Dataset.MinItems);
        Assert.Equal(60, lora.Train.Dataset.MaxItems);
        Assert.Equal(768, lora.Train.Dataset.Width);
        Assert.Equal(512, lora.Train.Dataset.Height);

        // шаг 2 — запуск обучения
        Assert.Equal(LoraStartKinds.Process, lora.Train.Start.Kind);
        Assert.Equal("train.py", lora.Train.Start.Command);
        Assert.Equal("C:/tools", lora.Train.Start.WorkDir);
        Assert.Equal(2000, lora.Train.Start.Steps);

        // шаг 3 — ожидание
        Assert.Equal(LoraWaitKinds.Process, lora.Train.Wait.Kind);
        Assert.Equal(600, lora.Train.Wait.TimeoutMinutes);

        // шаг 4 — результат
        Assert.Equal(LoraResultKinds.File, lora.Train.Result.Kind);
        Assert.Equal("lora/{object}/out/adapter_model.safetensors", lora.Train.Result.Path);
        Assert.Equal("loras/{object}.safetensors", lora.Train.Result.Target);
    }

    /// <summary>Облачное обучение: ожидание опросом и скачивание результата по ссылке.</summary>
    [Fact]
    public void Cloud_Training_Is_Polling_And_A_Download()
    {
        var lora = LoraSettings.Parse("""
            {
              "lora": {
                "supported": true,
                "engine": "http",
                "apply": { "kind": "request-field", "field": "lora_url" },
                "train": {
                  "kind": "http",
                  "docUrl": "https://example.invalid/doc",
                  "dataset": { "kind": "upload", "uploadUrl": "https://example.invalid/files" },
                  "start": { "kind": "http", "url": "https://example.invalid/train" },
                  "wait": {
                    "kind": "poll",
                    "pollUrl": "https://example.invalid/train/{id}",
                    "intervalSec": 15,
                    "doneField": "status",
                    "doneValue": "COMPLETED"
                  },
                  "result": { "kind": "download", "urlField": "output.lora_url", "target": "loras/{object}.safetensors" }
                }
              }
            }
            """);
        Assert.True(lora.Supported);
        Assert.Equal(LoraApplyKinds.RequestField, lora.Apply.Kind);
        Assert.Equal("lora_url", lora.Apply.Field);
        Assert.Equal(LoraDatasetKinds.Upload, lora.Train.Dataset.Kind);
        Assert.Equal("https://example.invalid/files", lora.Train.Dataset.UploadUrl);
        Assert.Equal("https://example.invalid/train", lora.Train.Start.Url);
        Assert.Equal(LoraWaitKinds.Poll, lora.Train.Wait.Kind);
        Assert.Equal(15, lora.Train.Wait.IntervalSec);
        Assert.Equal("status", lora.Train.Wait.DoneField);
        Assert.Equal("COMPLETED", lora.Train.Wait.DoneValue);
        Assert.Equal(LoraResultKinds.Download, lora.Train.Result.Kind);
        Assert.Equal("output.lora_url", lora.Train.Result.UrlField);
    }

    /// <summary>
    /// Профайла нет, он повреждён или про LoRA в нём не сказано ни слова — это «неизвестно»,
    /// а не «провайдер не даёт». Настройку правит человек руками, поэтому разбор обязан
    /// пережить любую неполноту без исключения.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не json вовсе")]
    [InlineData("{ \"provider\": \"anthropic\" }")]
    [InlineData("{ \"lora\": \"да\" }")]
    public void A_Silent_Or_Broken_Profile_Means_Unknown(string profile)
    {
        var lora = LoraSettings.Parse(profile);
        Assert.False(lora.Supported);
        Assert.Equal(LoraReasons.Unknown, lora.Reason);
        Assert.Equal(LoraApplyKinds.None, lora.Apply.Kind);
        Assert.Equal(LoraTrainKinds.None, lora.Train.Kind);
        Assert.False(RefImageSettings.Parse(profile).Supported);
    }

    /// <summary>Отказ провайдера — это ОТВЕТ, и он отличается от незаполненной настройки.</summary>
    [Fact]
    public void A_Refusal_Of_The_Provider_Is_Not_The_Same_As_An_Empty_Setting()
    {
        var lora = LoraSettings.Parse("""{ "lora": { "supported": false, "reason": "provider" } }""");
        Assert.False(lora.Supported);
        Assert.Equal(LoraReasons.Provider, lora.Reason);
        // «поддерживается: false» без причины — всё-таки «не сказано»
        Assert.Equal(LoraReasons.Unknown,
            LoraSettings.Parse("""{ "lora": { "supported": false } }""").Reason);
    }

    /// <summary>Референсная картинка: способ передачи, форматы и обязательность.</summary>
    [Fact]
    public void Reference_Image_Setting_Says_How_The_Picture_Gets_Into_The_Model()
    {
        var image = RefImageSettings.Parse(ComfyProfile);
        Assert.True(image.Supported);
        Assert.Equal(RefImageKinds.Upload, image.Kind);
        Assert.Equal("{image}", image.Placeholder);
        Assert.Equal("/upload/image", image.UploadPath);
        Assert.Equal("image", image.Field);
        Assert.Equal(1, image.MaxCount);
        Assert.Equal(["image/png"], image.Formats);
        Assert.True(image.Required);

        var none = RefImageSettings.Parse("""{ "refImage": { "kind": "none" } }""");
        Assert.False(none.Supported);
    }

    // ---------- 2. сид справочника ----------

    /// <summary>
    /// Настройка есть у КАЖДОЙ записи дистрибутива: у одних это «умеет и вот как»,
    /// у других — «нельзя, потому что провайдер не даёт». Молчащих записей быть не должно —
    /// «не заполнено» это работа, а не ответ.
    /// </summary>
    [Fact]
    public void Every_Seeded_Model_Has_A_Lora_Setting()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var models = f.Models.ListWithSkills();
        Assert.True(models.Count >= 32, $"моделей дистрибутива {models.Count}");
        foreach (var model in models)
        {
            Assert.False(model.Lora.Supported && model.Lora.Reason.Length > 0,
                $"{model.Name}: поддерживает и одновременно объясняет отказ");
            Assert.True(model.Lora.Supported || model.Lora.Reason != LoraReasons.Unknown,
                $"{model.Name}: настройка LoRA не заполнена");
            Assert.NotNull(model.RefImage);
        }
        Assert.Contains(models, m => m.Lora.Supported);
        Assert.Contains(models, m => !m.Lora.Supported);
    }

    /// <summary>Медиа-модель ComfyUI: адаптер подключается узлом графа, обучение — процессом,
    /// и у обучения расписаны все четыре шага.</summary>
    [Fact]
    public void The_Media_Model_Trains_By_Its_Own_Process_And_Attaches_By_A_Graph_Node()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var kandinsky = f.Models.ListWithSkills()
            .Where(m => m.Name.StartsWith("Kandinsky", StringComparison.Ordinal))
            // ТОЛЬКО ВИДЕО: у картиночной записи Kandinsky 5.0 Image Lite (T-19-S0) тренера
            // нет вовсе — musubi-tuner прямо пишет в docs/kandinsky5.md, что Image Lite не
            // поддержан, — поэтому у неё обучение объявлено «external», и здесь ей не место.
            // Её саму проверяет T19S0Tests
            .Where(m => !m.Name.Contains("Image", StringComparison.Ordinal))
            .ToList();
        // две записи T-258 (текст → ролик и картинка → ролик) плюс пять вариантов линейки
        // Video Lite, заведённых T-15-S0 (10-секундные, no-CFG и distil16)
        Assert.Equal(7, kandinsky.Count);
        foreach (var model in kandinsky)
        {
            Assert.True(model.Lora.Supported, model.Name);
            Assert.Equal(LoraEngines.ComfyUi, model.Lora.Engine);
            Assert.Equal(LoraApplyKinds.Workflow, model.Lora.Apply.Kind);
            Assert.Equal("loras", model.Lora.Apply.Dir);
            Assert.Contains(".safetensors", model.Lora.Apply.Formats);
            Assert.Equal(LoraTrainKinds.Process, model.Lora.Train.Kind);
            Assert.NotEmpty(model.Lora.Train.DocUrl);
            Assert.Equal(LoraDatasetKinds.Dir, model.Lora.Train.Dataset.Kind);
            Assert.NotEmpty(model.Lora.Train.Dataset.Path);
            Assert.Equal(LoraStartKinds.Process, model.Lora.Train.Start.Kind);
            Assert.Equal(LoraWaitKinds.Process, model.Lora.Train.Wait.Kind);
            Assert.Equal(LoraResultKinds.File, model.Lora.Train.Result.Kind);
            Assert.NotEmpty(model.Lora.Train.Result.Target);
        }
    }

    /// <summary>
    /// Локальная текстовая модель (llama-server): адаптер подключается ключом запуска, а
    /// обучения у неё в системе нет — файл готовится вне её. Это ЧЕСТНОЕ «умеет»:
    /// колонка справочника отвечает на вопрос «можно ли подключить адаптер».
    /// </summary>
    [Fact]
    public void A_Local_Text_Model_Attaches_An_Adapter_By_A_Startup_Flag()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var local = f.Models.ListWithSkills()
            .Where(m => m.Name.EndsWith("-Local", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, local.Count);
        foreach (var model in local)
        {
            Assert.True(model.Lora.Supported, model.Name);
            Assert.Equal(LoraEngines.LlamaCpp, model.Lora.Engine);
            Assert.Equal(LoraApplyKinds.LaunchArg, model.Lora.Apply.Kind);
            Assert.Contains(".gguf", model.Lora.Apply.Formats);
            Assert.Equal(LoraTrainKinds.External, model.Lora.Train.Kind);
            Assert.NotEmpty(model.Lora.Train.Result.Target);
        }
    }

    /// <summary>Облачная текстовая модель своего адаптера не принимает — и говорит почему.</summary>
    [Fact]
    public void A_Cloud_Text_Model_Says_Why_There_Is_No_Lora()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var claude = f.Models.ListWithSkills().Single(m => m.Name == "Claude-Opus-5.0");
        Assert.False(claude.Lora.Supported);
        Assert.Equal(LoraReasons.Provider, claude.Lora.Reason);
        Assert.Equal(RefImageKinds.None, claude.RefImage.Kind);
    }

    /// <summary>
    /// Стартовый кадр: у записи «изображение → видео» он ОБЯЗАТЕЛЕН и заливается в движок
    /// (T-258), у записи «текст → видео» его передать нечем. Настройка обязана это различать —
    /// иначе форма предложит приложить картинку туда, где она будет молча выброшена.
    /// </summary>
    [Fact]
    public void Only_The_Image_To_Video_Record_Takes_A_Start_Frame()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var models = f.Models.ListWithSkills();
        var i2v = models.Single(m => m.Name == "Kandinsky-5.0-I2V-Lite-5s");
        var t2v = models.Single(m => m.Name == "Kandinsky-5.0-T2V-Lite-sft-5s");
        Assert.Equal(RefImageKinds.Upload, i2v.RefImage.Kind);
        Assert.True(i2v.RefImage.Required);
        Assert.Equal("{image}", i2v.RefImage.Placeholder);
        Assert.Equal("/upload/image", i2v.RefImage.UploadPath);
        Assert.False(t2v.RefImage.Supported);
    }

    // ---------- 3. справочник в UI ----------

    /// <summary>
    /// Отметка доезжает и до списка (колонка «LoRA»), и до одиночной записи (форма модели).
    /// Оба пути читают ПРОФАЙЛ, поэтому правка профайла видна сразу: пересчитывать и
    /// хранить признак в базе не нужно вовсе.
    /// </summary>
    [Fact]
    public void The_Mark_Reaches_Both_The_List_And_The_Single_Record()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var fromList = f.Models.ListWithSkills().Single(m => m.Name == "Kandinsky-5.0-I2V-Lite-5s");
        var single = f.Models.Get(fromList.Id)!;
        Assert.True(single.Lora.Supported);
        Assert.Equal(fromList.Lora.Apply.Kind, single.Lora.Apply.Kind);
        Assert.Equal(fromList.RefImage.Kind, single.RefImage.Kind);

        // правка профайла действует сразу — без пересчёта и без перезапуска
        var abs = Path.Combine(f.Db.DataDir, single.ProfilePath);
        var json = JsonDocument.Parse(File.ReadAllText(abs)).RootElement;
        Assert.True(json.TryGetProperty("lora", out _));
        File.WriteAllText(abs, """{ "provider": "comfyui", "lora": { "supported": false, "reason": "provider" } }""");
        Assert.False(f.Models.Get(single.Id)!.Lora.Supported);
    }

    /// <summary>Кастомная запись без настройки в профайле честно показывает «не заполнено».</summary>
    [Fact]
    public void A_Custom_Model_Without_The_Setting_Shows_It_Is_Not_Filled_In()
    {
        var f = new StorageFixture();
        var created = f.Models.Create(new AiModel { Name = "T13S1-своя", IsActive = false }, actorId: null);
        var model = f.Models.Get(created.Id)!;
        Assert.False(model.Lora.Supported);
        Assert.Equal(LoraReasons.Unknown, model.Lora.Reason);
    }

    // ---------- 4. словари ----------

    /// <summary>
    /// У каждого кода настройки есть текст в ОБОИХ языках: коды показываются человеку
    /// подсказкой колонки и строками формы, а недостающий ключ выглядит на экране как
    /// «models.lora.apply.workflow» (наука T-180).
    /// </summary>
    [Fact]
    public void Every_Code_Has_A_Text_In_Both_Languages()
    {
        string[] keys =
        [
            "models.lora", "models.lora.yes", "models.lora.applyLine", "models.lora.trainLine",
            "models.lora.targetLine", "models.lora.docLink", "models.refImage",
            .. LoraReasons.All.Select(r => "models.lora.reason." + r),
            .. new[] { LoraApplyKinds.None, LoraApplyKinds.Workflow, LoraApplyKinds.LaunchArg, LoraApplyKinds.RequestField }
                .Select(k => "models.lora.apply." + k),
            .. new[] { LoraTrainKinds.None, LoraTrainKinds.Process, LoraTrainKinds.Http, LoraTrainKinds.External }
                .Select(k => "models.lora.train." + k),
            .. new[] { RefImageKinds.None, RefImageKinds.Upload, RefImageKinds.RequestField, RefImageKinds.Message }
                .Select(k => "models.refImage.kind." + k),
        ];
        foreach (var key in keys)
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                var text = Loc.In(lang, key);
                Assert.False(text == key, $"нет перевода {key} ({lang})");
                Assert.False(text.Length == 0, $"пустой перевод {key} ({lang})");
            }
        }
    }
}
