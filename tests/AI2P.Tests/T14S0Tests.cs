using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-14-S0 (облачные медиа-модели): коннектор шлюза fal.ai и двенадцать записей справочника
/// по §6.2–6.5 отчёта <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c>. До этого задания
/// всё облачное медиа не подключалось вовсе — своего OpenAI-совместимого входа у него нет.
///
/// Проверяется то, что расходится молча: провайдер и разбираемость профайла, ОДИН ключ API
/// на все записи, коды навыков и форматов из справочников, согласие пары inputs/outputs с
/// режимом навыка (T-257: иначе i2v-задача уедет модели, которой картинку не передать),
/// разбираемость шаблона запроса и подстановка в него, честная секция lora, разбор ответа
/// шлюза (у каждой модели он свой формы) и наличие документов на обоих языках с разделом
/// «Лицензия».
/// </summary>
public sealed class T14S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием (они же — список документов ru и en).</summary>
    public static readonly string[] CloudMedia =
    [
        "Seedance-2.5", "Seedance-2.5-I2V", "Gemini-Omni-Flash", "Veo-3.1", "Kling-3.0",
        "Nano-Banana-Pro", "Nano-Banana-Pro-Edit", "GPT-Image-2",
        "ElevenLabs-Music", "ElevenLabs-TTS-v3", "Tripo-H3.1", "Meshy-7",
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in CloudMedia)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T14S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private AiModel Model(string name) => _f.Models.List().Single(m => m.Name == name);

    private string Profile(string name) => _f.Files.ReadText(Model(name).ProfilePath);

    private string Scope(string name) => _f.Files.ReadText(Model(name).CapabilitiesPath);

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

    // --- состав справочника -------------------------------------------------------------

    [Fact]
    public void Catalogue_Has_All_Twelve_Cloud_Media_Models()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in CloudMedia)
        {
            Assert.Contains(expected, names);
        }
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Record_Goes_Through_The_Gateway_And_Is_A_Cloud_One(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));

        Assert.Equal(FalAiConnector.Provider, profile.Provider);
        Assert.Equal("https://queue.fal.run", profile.BaseUrl);
        Assert.True(Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out _), profile.BaseUrl);
        // облачная: ни манифеста установки, ни команды запуска, ни петлевого адреса —
        // иначе форма модели предложила бы её «установить», а ключ перестал бы быть нужен
        Assert.False(AiModelService.IsLocalProfile(Profile(name)));
        Assert.False(Model(name).IsLocal);
    }

    [Fact]
    public void Cloud_Media_Model_Cannot_Be_Active_Without_An_Api_Key()
    {
        // критерий приёмки задания: без ключа модель активной быть не может. Правило общее
        // для всех облачных записей, но проверить его надо здесь: у медиа-моделей запись
        // заводится сразу активной (так делает сид), а гасит их сторож старта
        var keys = new ModelKeyService(_f.Models, _f.Files, _f.Secrets, _f.KeyStore, _f.OrgKeys,
            StorageFixture.OrgId);

        foreach (var name in CloudMedia)
        {
            Assert.NotNull(keys.ActivationError(Model(name).Id));
        }
        keys.EnforceActivationOnStartup();
        foreach (var name in CloudMedia)
        {
            Assert.False(Model(name).IsActive, name);
        }

        // ключ один на все записи шлюза: поставили у одной — препятствий не осталось ни у одной
        keys.SetKey(Model("Veo-3.1").Id, "fal-test-key", null);
        foreach (var name in CloudMedia)
        {
            Assert.Null(keys.ActivationError(Model(name).Id));
        }
        Assert.True(Model("Veo-3.1").IsActive);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void All_Records_Share_One_Api_Key(string name)
    {
        // общий ключ — обещание документа: поставил один раз, включились все двенадцать
        var profile = ModelProfile.Parse(Profile(name));

        Assert.Equal("fal.apiKey", profile.SecretRef);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Declaration_Names_The_Same_Model_As_The_Profile(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        using var scope = JsonDocument.Parse(Scope(name));

        Assert.Equal(profile.Model, scope.RootElement.GetProperty("id").GetString());
        // идентификатор шлюза — путь эндпойнта: «vendor/model[/режим]»
        Assert.Contains('/', profile.Model);
        Assert.DoesNotContain(' ', profile.Model);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Skill_Codes_And_Io_Formats_Come_From_The_Reference(string name)
    {
        // код навыка мимо справочника подбор исполнителя не увидит вовсе, и ошибки не будет
        var skills = _f.RefData.Skills("en").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var formats = _f.RefData.IoFormats("en").Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;

        var codes = root.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!).ToList();
        Assert.NotEmpty(codes);
        foreach (var element in root.GetProperty("skills").EnumerateArray())
        {
            Assert.Contains(element.GetProperty("name").GetString()!, skills);
            Assert.InRange(element.GetProperty("score").GetInt32(), 0, 100);
        }
        foreach (var key in new[] { "inputs", "outputs" })
        {
            var declared = root.GetProperty(key).EnumerateArray().Select(f => f.GetString()!).ToList();
            Assert.NotEmpty(declared);
            foreach (var format in declared)
            {
                Assert.Contains(format, formats);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Declared_Skills_Fit_The_Declared_Inputs_And_Outputs(string name)
    {
        // T-257: пара inputs/outputs реально участвует в подборе. Запись, объявившая навык,
        // который её форматам не подходит, ПРОСТО НЕ БУДЕТ БРАТЬСЯ — и это выглядит как
        // «модель есть, а задание не идёт»
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        var inputs = root.GetProperty("inputs").EnumerateArray().Select(f => f.GetString()!).ToList();
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(f => f.GetString()!).ToList();

        foreach (var element in root.GetProperty("skills").EnumerateArray())
        {
            var code = element.GetProperty("name").GetString()!;
            Assert.True(SkillIo.Fits(code, inputs, outputs),
                $"{name}: навык {code} не сходится с объявленными форматами");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Lora_Is_Honestly_Declared_As_Unsupported(string name)
    {
        // обучение адаптера у облачных медиа недоступно — и в справочнике должно стоять
        // именно это, а не пустая секция: по ней ObjectLoadService решает, что сказать человеку
        var lora = ModelProfile.Parse(Profile(name)).Lora;

        Assert.False(lora.Supported);
        Assert.Equal("provider", lora.Reason);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Cost_Is_Either_Per_Token_Or_Per_Unit(string name)
    {
        // у медиа-моделей счёт идёт за картинку/секунду/минуту: цена обязана быть хоть
        // в каком-то виде, иначе документ не о чем предупреждать
        using var scope = JsonDocument.Parse(Scope(name));
        var cost = scope.RootElement.GetProperty("cost");
        var perToken = cost.GetProperty("in_per_1m").GetDouble() > 0 ||
                       cost.GetProperty("out_per_1m").GetDouble() > 0;
        var perUnit = cost.TryGetProperty("per_unit", out var value) && value.GetDouble() > 0;

        if (name == "ElevenLabs-TTS-v3")
        {
            // единственное исключение: цену этой записи провайдер в каталоге не публикует,
            // и выдуманная цифра была бы хуже честного нуля (так и написано в документе)
            Assert.False(perToken || perUnit);
            return;
        }
        Assert.True(perToken || perUnit, name);
        if (perUnit)
        {
            Assert.Contains(cost.GetProperty("unit").GetString(),
                new[] { "image", "second", "minute", "model" });
        }
    }

    // --- шаблон запроса -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Names))]
    public void Request_Template_Is_Json_And_Uses_Known_Placeholders(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.NotEqual("", profile.Request);
        using (JsonDocument.Parse(profile.Request))
        {
            // шаблон обязан быть разбираемым JSON ДО подстановки: сломанный виден только
            // на исполнителе, уже во время платного задания
        }

        var known = new[]
        {
            "{prompt}", "{negative}", "{job}", "{image}",
            "\"{seed}\"", "\"{width}\"", "\"{height}\"", "\"{length}\"", "\"{steps}\"",
        };
        var text = profile.Request;
        foreach (var placeholder in known)
        {
            text = text.Replace(placeholder, "");
        }
        // незнакомый плейсхолдер уедет в шлюз КАК ЕСТЬ («{duration}» строкой) и вернётся
        // отказом проверки — искать надо ровно фигурные скобки со словом внутри, а не
        // скобки вообще: сам JSON шаблона тоже в фигурных скобках
        var left = System.Text.RegularExpressions.Regex.Match(text, @"\{[A-Za-z][A-Za-z0-9_]*\}");
        Assert.False(left.Success, $"{name}: неизвестная подстановка {left.Value}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Start_Image_Placeholder_And_RefImage_Agree(string name)
    {
        // расхождение здесь молчаливое: настройка говорит «картинку принимаю», а подставить
        // её некуда — задание уходит в шлюз без картинки и возвращает не то, что просили
        var profile = ModelProfile.Parse(Profile(name));
        var needs = FalAiConnector.NeedsStartImage(profile.Request, profile.RefImage);

        Assert.Equal(profile.RefImage.Supported, needs);
        if (!profile.RefImage.Supported)
        {
            return;
        }
        Assert.Equal(RefImageKinds.RequestField, profile.RefImage.Kind);
        Assert.True(profile.RefImage.Required);
        Assert.NotEqual("", profile.RefImage.Field);
        Assert.Contains(profile.RefImage.Field, profile.Request, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_Only_Models_Ask_For_No_Picture()
    {
        // рисование и озвучка исходной картинки не требуют, а «оживление» без неё
        // бессмысленно: задание обязано отказываться сразу
        foreach (var name in new[] { "Seedance-2.5", "Gemini-Omni-Flash", "Veo-3.1",
                     "Nano-Banana-Pro", "GPT-Image-2", "ElevenLabs-Music",
                     "ElevenLabs-TTS-v3", "Meshy-7" })
        {
            Assert.Equal(RefImageKinds.None, ModelProfile.Parse(Profile(name)).RefImage.Kind);
        }
        foreach (var name in new[] { "Seedance-2.5-I2V", "Kling-3.0",
                     "Nano-Banana-Pro-Edit", "Tripo-H3.1" })
        {
            Assert.True(ModelProfile.Parse(Profile(name)).RefImage.Required, name);
        }
    }

    [Fact]
    public void Tts_Puts_The_Description_Into_Its_Own_Field()
    {
        // у ElevenLabs поле текста называется text, а не prompt: ровно ради таких различий
        // шаблон запроса и лежит в справочнике, а не в коде коннектора
        var profile = ModelProfile.Parse(Profile("ElevenLabs-TTS-v3"));

        Assert.Contains("\"text\": \"{prompt}\"", profile.Request, StringComparison.Ordinal);
        Assert.DoesNotContain("\"prompt\"", profile.Request, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Built_Request_Is_Valid_Json_With_The_Prompt_Inside(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        var prompt = "кадр «дождь»: герой в плаще, \"крупный план\"\nвторая строка";

        var body = _f.FalAi.BuildRequest(profile, prompt, seed: 42, jobDisplayId: "J-1",
            imageDataUri: profile.RefImage.Supported ? "data:image/png;base64,AAAA" : null);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // кавычки и перевод строки промпта не должны рвать тело запроса
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        var raw = root.GetRawText();
        if (name != "Tripo-H3.1")   // единственная запись, которая текст не принимает вовсе
        {
            Assert.Contains("крупный план", raw, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("{prompt}", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Seed_Goes_Into_The_Request_As_A_Number()
    {
        // "{seed}" подставляется ВМЕСТЕ с кавычками: строка вместо числа — HTTP 422 у шлюза
        var profile = ModelProfile.Parse(Profile("Veo-3.1"));

        var body = _f.FalAi.BuildRequest(profile, "кадр", seed: 12345, jobDisplayId: "J-1");

        using var doc = JsonDocument.Parse(body);
        var seed = doc.RootElement.GetProperty("seed");
        Assert.Equal(JsonValueKind.Number, seed.ValueKind);
        Assert.Equal(12345, seed.GetInt64());
    }

    [Fact]
    public void Start_Image_Goes_Into_The_Field_Named_By_The_Reference()
    {
        var profile = ModelProfile.Parse(Profile("Kling-3.0"));

        var body = _f.FalAi.BuildRequest(profile, "оживи кадр", seed: 1, jobDisplayId: "J-1",
            imageDataUri: "data:image/png;base64,QUJD");

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("data:image/png;base64,QUJD",
            doc.RootElement.GetProperty("start_image_url").GetString());
    }

    [Fact]
    public void Edit_Model_Takes_The_Picture_As_A_List()
    {
        // у правки Nano Banana поле — МАССИВ адресов: подстановка обязана уложиться внутрь
        var profile = ModelProfile.Parse(Profile("Nano-Banana-Pro-Edit"));

        var body = _f.FalAi.BuildRequest(profile, "замени вывеску", seed: 1, jobDisplayId: "J-1",
            imageDataUri: "data:image/png;base64,QUJD");

        using var doc = JsonDocument.Parse(body);
        var urls = doc.RootElement.GetProperty("image_urls");
        Assert.Equal(JsonValueKind.Array, urls.ValueKind);
        Assert.Equal("data:image/png;base64,QUJD", urls[0].GetString());
    }

    [Fact]
    public void Data_Uri_Is_Hidden_In_The_Saved_Request()
    {
        // файл запроса читают ГЛАЗАМИ: мегабайт base64 в нём делает артефакт бесполезным
        var body = """{"prompt": "кадр", "image_url": "data:image/png;base64,QUJDREVG"}""";

        var hidden = FalAiConnector.HideDataUris(body);

        Assert.DoesNotContain("QUJDREVG", hidden, StringComparison.Ordinal);
        Assert.Contains("data:…", hidden, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(hidden);          // остаётся разбираемым
        Assert.Equal("кадр", doc.RootElement.GetProperty("prompt").GetString());
    }

    // --- разбор ответа шлюза ------------------------------------------------------------

    [Fact]
    public void Files_Are_Collected_From_Any_Shape_Of_The_Answer()
    {
        // у каждой модели шлюза ответ своей формы, но ФАЙЛ везде один и тот же объект
        var images = FalAiConnector.CollectFiles(
            """{"images":[{"url":"https://x/a.png","content_type":"image/png"}],"description":"…"}""");
        var video = FalAiConnector.CollectFiles(
            """{"video":{"url":"https://x/b.mp4","content_type":"video/mp4"},"seed":7}""");
        var audio = FalAiConnector.CollectFiles(
            """{"audio":{"url":"https://x/c.mp3","content_type":"audio/mpeg"}}""");

        Assert.Equal("https://x/a.png", Assert.Single(images).Url);
        Assert.Equal("https://x/b.mp4", Assert.Single(video).Url);
        Assert.Equal("https://x/c.mp3", Assert.Single(audio).Url);
    }

    [Fact]
    public void Plain_String_Urls_Are_Not_Taken_Twice()
    {
        // у 3D-моделей рядом с файлом лежит model_urls — ПРОСТЫЕ строки на те же файлы:
        // без этого правила один и тот же файл скачался бы дважды
        var files = FalAiConnector.CollectFiles(
            """
            {"model_mesh":{"url":"https://x/m.glb","content_type":"model/gltf-binary"},
             "model_urls":{"glb":"https://x/m.glb","fbx":"https://x/m.fbx"}}
            """);

        Assert.Equal("https://x/m.glb", Assert.Single(files).Url);
    }

    [Fact]
    public void Result_File_Name_Is_Safe_And_Guessed_By_Type()
    {
        Assert.Equal("a.png", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/a.png", "a.png", "image/png"), 0));
        // имя с путём внутри пришло бы прямо в каталог артефактов — берём только имя файла
        Assert.Equal("evil.png", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/a.png", "../../evil.png", "image/png"), 0));
        // имени нет — по типу содержимого, а второму файлу свой номер
        Assert.Equal("result.mp4", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/b", "", "video/mp4"), 0));
        Assert.Equal("result-2.glb", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/c", "", "model/gltf-binary"), 1));
        // ни имени, ни типа — по расширению адреса, а мусор в адресе даёт .bin
        Assert.Equal("result.webp", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/d.webp?token=1", "", ""), 0));
        Assert.Equal("result.bin", FalAiConnector.SafeName(
            new FalAiConnector.OutputFile("https://x/d", "", ""), 0));
    }

    [Fact]
    public void Gateway_Error_Is_Turned_Into_Readable_Text()
    {
        Assert.Equal("Unauthorized", FalAiConnector.ErrorText("""{"detail":"Unauthorized"}"""));
        // отказ проверки — массив объектов: без разбора человек видит бессмыслицу
        Assert.Contains("duration", FalAiConnector.ErrorText(
            """{"detail":[{"loc":["body","duration"],"msg":"value is not a valid enumeration member"}]}"""),
            StringComparison.Ordinal);
        Assert.Contains("<html>", FalAiConnector.ErrorText("<html>502</html>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Model_Output_Lines_Reach_The_Job_Console()
    {
        using var doc = JsonDocument.Parse(
            """{"status":"IN_PROGRESS","logs":[{"message":"step 1\nstep 2"},{"message":"  "}]}""");

        var lines = FalAiConnector.LogLines(doc.RootElement);

        Assert.Equal(["step 1", "step 2"], lines);
    }

    // --- подключение --------------------------------------------------------------------

    [Fact]
    public void Registry_Resolves_The_Gateway_Provider()
    {
        var model = Model("Veo-3.1");
        var executor = new Executor
        {
            Nick = "видео",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            ProfilePath = model.ProfilePath,
            CapabilitiesPath = model.CapabilitiesPath,
        };

        Assert.Same(_f.FalAi, _f.Connectors.Resolve(executor));
        Assert.Equal("fal-ai", _f.FalAi.Kind);
    }

    [Fact]
    public void Without_A_Key_The_Job_Fails_Before_Any_Money_Is_Spent()
    {
        // облачная модель без ключа активной быть не может, но задание можно запустить и
        // мимо активности: отказ обязан быть понятным и НЕ уходить в сеть
        var model = Model("Nano-Banana-Pro");
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "картинки-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            ProfilePath = model.ProfilePath,
            CapabilitiesPath = model.CapabilitiesPath,
        }, null);

        var error = _f.FalAi.TestConnectionAsync(executor).GetAwaiter().GetResult();

        Assert.NotNull(error);
        Assert.Contains("fal.apiKey", error!, StringComparison.Ordinal);
    }

    // --- задание целиком на поддельном шлюзе ---------------------------------------------

    /// <summary>
    /// Исполнитель на записи справочника, у которой baseUrl переставлен на тестовый сервер,
    /// а ключ положен в файловое хранилище: без ключа коннектор до сети не доходит вовсе.
    /// </summary>
    private Executor CreateGatewayExecutor(string name, string baseUrl)
    {
        var model = Model(name);
        var profile = JsonNodeOf(Profile(name));
        profile["baseUrl"] = baseUrl.TrimEnd('/');
        _f.Files.WriteText(model.ProfilePath, profile.ToJsonString());
        _f.Secrets.Write("fal.apiKey", "fal-test-key");
        return _f.Executors.Create(new Executor
        {
            Nick = "шлюз-" + Guid.NewGuid().ToString("N")[..6],
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
        }, null);
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeOf(string json) =>
        (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!;

    /// <summary>Задача с описанием-промптом → задание в FalAiConnector → дождаться финала.</summary>
    private async Task<(TaskItem Task, Job Job)> RunGatewayJobAsync(string name, string baseUrl,
        string description, string? projectFolder = null)
    {
        var executor = CreateGatewayExecutor(name, baseUrl);
        var project = _f.Projects.Create("Медиа-" + Guid.NewGuid().ToString("N")[..6],
            projectFolder, null, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Ролик про кота",
            ExecutorIds = [executor.Id],
        }, description, "Критерии: ролик 5 секунд", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.FalAi.SubmitJobAsync(job, requestText: "");
        // поллинг коннектора — раз в 3 с; под нагрузкой полного прогона ждать приходится
        // долго, поэтому предел большой, а цикл выходит сразу по смене состояния (наука
        // Todo36_4Tests: медиа-тест с коротким ожиданием — источник флаки-падений)
        for (var i = 0; i < 3600 && _f.Jobs.Get(job.Id)!.State == JobState.Running; i++)
        {
            await Task.Delay(100);
        }
        // статус ЗАДАЧИ коннектор ставит строкой ПОЗЖЕ состояния задания — ждём и его
        for (var i = 0; i < 300 && _f.Tasks.Get(task.Id)!.Status == TaskStatuses.Draft; i++)
        {
            await Task.Delay(100);
        }
        return (task, _f.Jobs.Get(job.Id)!);
    }

    [Fact]
    public async Task Media_Job_Runs_Through_The_Whole_Queue_And_Brings_The_File()
    {
        // главная проверка задания: постановка в очередь → опрос → забор результата →
        // файл в артефактах задачи. Всё это на поддельном шлюзе, без единого рубля
        var clip = Encoding.ASCII.GetBytes("FAKE-MP4-BYTES");
        string? submitted = null;
        string? auth = null;
        var statuses = 0;
        // адреса состояния и ответа шлюз присылает СВОИ — сервер обязан назвать себя сам,
        // поэтому обработчик смотрит на уже созданный сервер (Url известен после запуска)
        Todo36_4Tests.FakeHttpServer? gateway = null;
        using var server = gateway = new Todo36_4Tests.FakeHttpServer(request =>
        {
            var url = gateway!.Url.TrimEnd('/');
            if (request.Method == "POST")
            {
                submitted = request.Body;
                auth = request.Auth;
                return (200, "application/json", Encoding.UTF8.GetBytes($$"""
                    {"request_id":"r-1","status_url":"{{url}}/q/r-1/status",
                     "response_url":"{{url}}/q/r-1","cancel_url":"{{url}}/q/r-1/cancel"}
                    """));
            }
            if (request.Path.StartsWith("/q/r-1/status"))
            {
                // первый опрос — очередь, второй — счёт, третий — готово: так и выглядит
                // работа шлюза, и по дороге в консоль задания уходят строки вывода модели
                statuses++;
                var state = statuses switch
                {
                    1 => """{"status":"IN_QUEUE","queue_position":2}""",
                    2 => """{"status":"IN_PROGRESS","logs":[{"message":"rendering 50%"}]}""",
                    _ => """{"status":"COMPLETED"}""",
                };
                return (200, "application/json", Encoding.UTF8.GetBytes(state));
            }
            if (request.Path.StartsWith("/q/r-1"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes($$"""
                    {"video":{"url":"{{url}}/files/out.mp4","content_type":"video/mp4",
                     "file_name":"out.mp4"},"seed":7}
                    """));
            }
            if (request.Path.StartsWith("/files/"))
            {
                return (200, "video/mp4", clip);
            }
            return (404, "text/plain", []);
        });

        var (task, job) = await RunGatewayJobAsync("Seedance-2.5", server.Url,
            "пушистый кот играет с клубком шерсти");

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.Status);
        // ключ API уходит заголовком «Key …» — так его ждёт шлюз
        Assert.Equal("Key fal-test-key", auth);
        // промптом служит ОПИСАНИЕ задачи, критерии приёмки до модели не доезжают
        Assert.Contains("пушистый кот", submitted!);
        Assert.DoesNotContain("Критерии", submitted);
        // поля шаблона запроса из справочника уехали как есть
        using (var body = JsonDocument.Parse(submitted!))
        {
            Assert.Equal("720p", body.RootElement.GetProperty("resolution").GetString());
        }
        // файл результата — в артефактах задачи, ответ шлюза сохранён рядом
        var artifacts = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!);
        var videoRel = artifacts.Single(a => a.EndsWith("out.mp4", StringComparison.Ordinal));
        Assert.Equal(clip, File.ReadAllBytes(Path.Combine(_f.Dir, videoRel)));
        var summary = File.ReadAllText(Path.Combine(_f.Dir,
            artifacts.Single(a => a.EndsWith("-result.md", StringComparison.Ordinal))));
        Assert.Contains("r-1", summary, StringComparison.Ordinal);
        Assert.Contains("0.473", summary.Replace(',', '.'), StringComparison.Ordinal);
        // счёт за медиа ведёт провайдер: цена задания остаётся нулём намеренно
        Assert.Equal(0, job.Cost, 6);
    }

    [Fact]
    public async Task Start_Image_Is_Sent_Inside_The_Request()
    {
        // «оживление картинки»: файл берётся в папке проекта и уходит адресом data: —
        // своего хранилища у нас нет, и это единственный способ передать кадр шлюзу
        var folder = Path.Combine(_f.Dir, "project-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(folder, "refs"));
        var picture = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(Path.Combine(folder, "refs", "hero.png"), picture);
        string? submitted = null;
        Todo36_4Tests.FakeHttpServer? gateway = null;
        using var server = gateway = new Todo36_4Tests.FakeHttpServer(request =>
        {
            var url = gateway!.Url.TrimEnd('/');
            if (request.Method == "POST")
            {
                submitted = request.Body;
                return (200, "application/json", Encoding.UTF8.GetBytes($$"""
                    {"request_id":"r-2","status_url":"{{url}}/q/r-2/status","response_url":"{{url}}/q/r-2"}
                    """));
            }
            if (request.Path.StartsWith("/q/r-2/status"))
            {
                return (200, "application/json", Encoding.UTF8.GetBytes("""{"status":"COMPLETED"}"""));
            }
            if (request.Path.StartsWith("/q/r-2"))
            {
                // три «$»: тело кончается ДВУМЯ фигурными скобками, и при двух знаках
                // компилятор считает их закрытием подстановки (CS9007)
                return (200, "application/json", Encoding.UTF8.GetBytes($$$"""
                    {"video":{"url":"{{{url}}}/f.mp4","content_type":"video/mp4"}}
                    """));
            }
            return (200, "video/mp4", Encoding.ASCII.GetBytes("MP4"));
        });

        var (_, job) = await RunGatewayJobAsync("Seedance-2.5-I2V", server.Url,
            "оживи кадр, дождь\nвзять за основу refs/hero.png", folder);

        Assert.Equal(JobState.Done, job.State);
        using var body = JsonDocument.Parse(submitted!);
        var image = body.RootElement.GetProperty("image_url").GetString()!;
        Assert.StartsWith("data:image/png;base64,", image, StringComparison.Ordinal);
        Assert.Equal(picture, Convert.FromBase64String(image["data:image/png;base64,".Length..]));
    }

    [Fact]
    public async Task Job_Fails_With_A_Readable_Text_When_The_Gateway_Refuses()
    {
        // отказ проверки полей приходит массивом объектов: без разбора человек увидел бы
        // бессмыслицу вместо «duration не из перечисления»
        using var server = new Todo36_4Tests.FakeHttpServer(_ => (422, "application/json",
            Encoding.UTF8.GetBytes("""
                {"detail":[{"loc":["body","duration"],"msg":"value is not a valid enumeration member"}]}
                """)));

        var (task, job) = await RunGatewayJobAsync("Veo-3.1", server.Url, "кадр с котом");

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);
        var errorMd = _f.Tasks.Artifacts(_f.Tasks.Get(task.Id)!)
            .Single(a => a.EndsWith("-error.md", StringComparison.Ordinal));
        var text = await File.ReadAllTextAsync(Path.Combine(_f.Dir, errorMd));
        Assert.Contains("duration", text, StringComparison.Ordinal);
        Assert.Contains("422", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_Tells_A_Bad_Key_From_A_Live_Gateway()
    {
        using var ok = new Todo36_4Tests.FakeHttpServer(_ =>
            (404, "application/json", Encoding.UTF8.GetBytes("""{"detail":"Request not found"}""")));
        var executor = CreateGatewayExecutor("Nano-Banana-Pro", ok.Url);

        // «запроса нет» — шлюз отвечает и ключ принят: настоящую генерацию проба не пускает
        Assert.Null(await _f.FalAi.TestConnectionAsync(executor));

        using var forbidden = new Todo36_4Tests.FakeHttpServer(_ =>
            (401, "application/json", Encoding.UTF8.GetBytes("""{"detail":"Unauthorized"}""")));
        var bad = CreateGatewayExecutor("GPT-Image-2", forbidden.Url);

        var error = await _f.FalAi.TestConnectionAsync(bad);

        Assert.NotNull(error);
        Assert.Contains("401", error!, StringComparison.Ordinal);
    }

    // --- документы ----------------------------------------------------------------------

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
        // требование задания: раздел лицензии стоит там же, где стоит стоимость. У облачной
        // модели лицензировать нечего — условия относятся к РЕЗУЛЬТАТУ генерации
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("## Лицензия", ru, StringComparison.Ordinal);
        Assert.Contains("## Licence", en, StringComparison.Ordinal);
        Assert.Contains("## Ограничения и стоимость", ru, StringComparison.Ordinal);
        Assert.Contains("## Limits and price", en, StringComparison.Ordinal);
        Assert.Contains("результату генерации", ru, StringComparison.Ordinal);
        Assert.Contains("result of the generation", en, StringComparison.Ordinal);
        Assert.Contains("licenseType: commercial", ru, StringComparison.Ordinal);
        Assert.Contains("licenseType: commercial", en, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Answers_The_Two_Obligatory_Questions(string name)
    {
        // правило проекта: как получить ключ (по шагам, со ссылкой на консоль провайдера)
        // и какие требования к железу
        var ru = File.ReadAllText(DocPath("ru", name));
        var en = File.ReadAllText(DocPath("en", name));

        Assert.Contains("## Как получить ключ", ru, StringComparison.Ordinal);
        Assert.Contains("## How to obtain the key", en, StringComparison.Ordinal);
        Assert.Contains("https://fal.ai/dashboard/keys", ru, StringComparison.Ordinal);
        Assert.Contains("https://fal.ai/dashboard/keys", en, StringComparison.Ordinal);
        Assert.Contains("## Требования к железу", ru, StringComparison.Ordinal);
        Assert.Contains("## Hardware requirements", en, StringComparison.Ordinal);
        Assert.Contains("LoRA", ru, StringComparison.Ordinal);
        Assert.Contains("LoRA", en, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Repeats_The_Identifier_And_The_Key_Reference(string name)
    {
        // документ и справочник расходятся молча (наука T-214): id и ссылка на ключ — ровно
        // то, по чему человек ищет, почему задание не идёт
        var profile = ModelProfile.Parse(Profile(name));

        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(DocPath(lang, name));
            Assert.Contains(profile.Model, text, StringComparison.Ordinal);
            Assert.Contains(profile.SecretRef, text, StringComparison.Ordinal);
            Assert.Contains(profile.BaseUrl, text, StringComparison.Ordinal);
            // оговорка про непроверенный живьём идентификатор (правило T-214)
            Assert.Contains("/models", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_Languages_List_The_New_Models_In_The_Catalogue_Readme()
    {
        var ru = File.ReadAllText(Path.Combine(AppDir(), "doc", "ru", "models", "README.md"));
        var en = File.ReadAllText(Path.Combine(AppDir(), "doc", "en", "models", "README.md"));

        foreach (var name in CloudMedia)
        {
            Assert.Contains($"({name}.md)", ru, StringComparison.Ordinal);
            Assert.Contains($"({name}.md)", en, StringComparison.Ordinal);
        }
    }
}
