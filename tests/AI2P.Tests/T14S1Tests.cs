using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-14-S1 (версия 1.95): ЗАГРУЗКА ОБЪЕКТА ПРОЕКТА В МОДЕЛЬ.
///
/// Что проверяется:
/// <list type="number">
/// <item>РЕШЕНИЕ (<see cref="ObjectLoadPlanner"/>): что из названных в описании объектов
/// обязано уехать в модель, и какое несовпадение с настройкой модели чем кончается —
/// ошибкой с конкретной причиной либо замечанием;</item>
/// <item>ГРАНИЦА: персонаж без адаптера и с адаптером в обучении задание НЕ роняет —
/// иначе перестали бы запускаться все уже заведённые задачи роликов;</item>
/// <item>ПОДКЛЮЧЕНИЕ АДАПТЕРА к графу ComfyUI (<see cref="ComfyLoraGraph"/>): вставка узла
/// в поставляемый шаблон, цепочка из нескольких адаптеров, шаблон с плейсхолдером;</item>
/// <item>СПРАВОЧНИК: «как грузить» читается из профайла модели (lora/refImage), а не
/// зашито в коннектор;</item>
/// <item>СЕРВИС: поиск файла адаптера, текст ошибки, сообщение в чат с заменой;</item>
/// <item>ЗАДАНИЕ ЦЕЛИКОМ: медиа-задание встаёт с ошибкой, в артефакте — причина, в чате —
/// предложение замены;</item>
/// <item>СЛОВАРИ: у каждого кода несовпадения и каждого состояния адаптера есть текст
/// в ОБОИХ языках.</item>
/// </list>
/// </summary>
public sealed class T14S1Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string I2VId = "6f1a45e0-0d31-4c65-9a01-000000000032";
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    /// <summary>Настройка модели, которая с адаптерами работает (узел графа ComfyUI).</summary>
    private static LoraSettings WithLora(int maxCount = 1, double strength = 1.0) =>
        LoraSettings.Parse($$"""
            {
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "strength": {{strength.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                  "dir": "loras",
                  "maxCount": {{maxCount}},
                  "formats": [".safetensors"]
                }
              }
            }
            """);

    private static LoraSettings NoLora(string reason = LoraReasons.Provider) =>
        LoraSettings.Parse("{\"lora\": {\"supported\": false, \"reason\": \"" + reason + "\"}}");

    private static RefImageSettings WithImage(int maxCount = 1) =>
        RefImageSettings.Parse($$"""
            {
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": {{maxCount}},
                "formats": ["image/png", "image/jpeg"]
              }
            }
            """);

    private static RefImageSettings NoImage() => new();

    private static ObjectLoadPlan Plan(IReadOnlyList<ObjectLoadRef> refs, LoraSettings lora,
        RefImageSettings image, params string[] existing) =>
        ObjectLoadPlanner.Plan(refs, lora, image, "kandinsky5lite_t2v_sft_5s",
            path => existing.Contains(path) ? "C:/loras/" + Path.GetFileName(path) : null,
            path => existing.Contains(path));

    // ---------- 1. решение: адаптер LoRA ----------

    /// <summary>Модель адаптеры не принимает — задание встаёт, и в тексте названы объект,
    /// модель и ПРИЧИНА отказа из справочника, а не «не удалось загрузить объект».</summary>
    [Fact]
    public void A_Model_Without_Lora_Refuses_An_Adapter_Object_With_The_Reason_Named()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/OBJ-3.safetensors",
                LoraStates.Ready)],
            NoLora(), NoImage(), "loras/OBJ-3.safetensors");

        Assert.False(plan.Ok);
        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ObjectLoadProblems.LoraNotSupported, problem.Code);
        Assert.Equal("OBJ-3", problem.ObjectCode);
        Assert.Contains("OBJ-3", problem.Text);
        Assert.Contains("Вася-LoRA", problem.Text);
        Assert.Contains("kandinsky5lite_t2v_sft_5s", problem.Text);
        Assert.Contains(Loc.T("models.lora.reason.provider"), problem.Text);
    }

    /// <summary>Объект-адаптер не обучен — подключать нечего, это ошибка; в тексте состояние.</summary>
    [Fact]
    public void An_Untrained_Adapter_Object_Stops_The_Job()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "", "training")],
            WithLora(), NoImage());

        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ObjectLoadProblems.LoraNotTrained, problem.Code);
        Assert.Contains(Loc.T("msg.objectLoad.state.training"), problem.Text);
        Assert.Empty(plan.Loras);
    }

    /// <summary>Состояние «готов», а файла по пути нет: это не «модель не умеет», а именно
    /// пропавший файл — и путь обязан быть в тексте, иначе искать нечего.</summary>
    [Fact]
    public void A_Missing_Adapter_File_Is_Named_By_Its_Path()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/OBJ-3.safetensors",
                LoraStates.Ready)],
            WithLora(), NoImage());

        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ObjectLoadProblems.LoraFileMissing, problem.Code);
        Assert.Contains("loras/OBJ-3.safetensors", problem.Text);
    }

    /// <summary>Всё сошлось — адаптер уходит в загрузку; вес берётся из справочника,
    /// а свой вес объекта его перекрывает.</summary>
    [Fact]
    public void A_Trained_Adapter_Is_Loaded_With_The_Strength_From_The_Catalogue()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/OBJ-3.safetensors",
                LoraStates.Ready)],
            WithLora(strength: 0.8), NoImage(), "loras/OBJ-3.safetensors");

        Assert.True(plan.Ok);
        var lora = Assert.Single(plan.Loras);
        Assert.Equal("OBJ-3", lora.ObjectCode);
        Assert.Equal(0.8, lora.Strength);

        var own = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/OBJ-3.safetensors",
                LoraStates.Ready, LoraStrength: 0.5)],
            WithLora(strength: 0.8), NoImage(), "loras/OBJ-3.safetensors");
        Assert.Equal(0.5, Assert.Single(own.Loras).Strength);
    }

    /// <summary>Файл не того формата — отказ до постановки в очередь: ComfyUI сказал бы то же
    /// самое, но своим языком и в своём логе.</summary>
    [Fact]
    public void An_Adapter_Of_A_Foreign_Format_Is_Refused()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/OBJ-3.gguf",
                LoraStates.Ready)],
            WithLora(), NoImage(), "loras/OBJ-3.gguf");

        Assert.Equal(ObjectLoadProblems.LoraFormat, Assert.Single(plan.Problems).Code);
    }

    /// <summary>Адаптеров названо больше, чем модель берёт: лишние не отбрасываются молча.</summary>
    [Fact]
    public void More_Adapters_Than_The_Model_Takes_Is_A_Problem_Not_A_Silent_Cut()
    {
        var plan = Plan(
            [
                new ObjectLoadRef("OBJ-3", "Вася", ObjectKinds.Lora, "", "loras/a.safetensors", LoraStates.Ready),
                new ObjectLoadRef("OBJ-4", "Маша", ObjectKinds.Lora, "", "loras/b.safetensors", LoraStates.Ready),
            ],
            WithLora(), NoImage(), "loras/a.safetensors", "loras/b.safetensors");

        Assert.Single(plan.Loras);
        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ObjectLoadProblems.LoraTooMany, problem.Code);
        Assert.Equal("OBJ-4", problem.ObjectCode);
    }

    // ---------- 2. граница: чего трогать нельзя ----------

    /// <summary>ПЕРСОНАЖ БЕЗ АДАПТЕРА — это обычная задача T-259: паспорт в промпт, и никаких
    /// проверок. Сломать это правило значит остановить все уже заведённые задачи роликов.</summary>
    [Fact]
    public void A_Plain_Character_Never_Blocks_The_Job()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-1", "Вася", ObjectKinds.Character, "refs/vasya.png", "",
                LoraStates.None)],
            NoLora(), NoImage());

        Assert.True(plan.Ok);
        Assert.Empty(plan.Loras);
        Assert.Empty(plan.Images);
        Assert.Empty(plan.Notes);
    }

    /// <summary>Адаптер персонажа ещё обучается — это ЗАМЕЧАНИЕ: паспорт работает, а ждать
    /// конца обучения, чтобы сделать хоть один пробный кадр, человека заставлять нельзя.</summary>
    [Fact]
    public void A_Character_With_An_Adapter_In_Training_Only_Gets_A_Note()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-1", "Вася", ObjectKinds.Character, "refs/vasya.png", "",
                LoraStates.Training)],
            WithLora(), NoImage());

        Assert.True(plan.Ok);
        var note = Assert.Single(plan.Notes);
        Assert.Contains("OBJ-1", note);
        Assert.Contains(Loc.T("msg.objectLoad.state.training"), note);
    }

    /// <summary>Готовый адаптер персонажа подключается сам: человек его для этого и обучал.</summary>
    [Fact]
    public void A_Character_With_A_Ready_Adapter_Loads_It()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-1", "Вася", ObjectKinds.Character, "refs/vasya.png",
                "loras/vasya.safetensors", LoraStates.Ready)],
            WithLora(), NoImage(), "loras/vasya.safetensors");

        Assert.True(plan.Ok);
        Assert.Equal("OBJ-1", Assert.Single(plan.Loras).ObjectCode);
    }

    // ---------- 3. решение: референсная картинка ----------

    /// <summary>Названный ЭТАЛОННЫЙ КАДР обязан уехать в модель. Модель картинку не берёт —
    /// ошибка: молча сгенерированный без неё кадр выглядит готовым результатом.</summary>
    [Fact]
    public void A_Reference_Frame_Object_Needs_A_Model_That_Takes_Images()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "refs/frame1.png", "",
                LoraStates.None)],
            NoLora(), NoImage(), "refs/frame1.png");

        var problem = Assert.Single(plan.Problems);
        Assert.Equal(ObjectLoadProblems.ImageNotSupported, problem.Code);
        Assert.Contains("kandinsky5lite_t2v_sft_5s", problem.Text);
    }

    [Fact]
    public void A_Reference_Frame_Is_Loaded_When_The_Model_Takes_Images()
    {
        var plan = Plan(
            [new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "refs/frame1.png", "",
                LoraStates.None)],
            NoLora(), WithImage(), "refs/frame1.png");

        Assert.True(plan.Ok);
        Assert.Equal("refs/frame1.png", Assert.Single(plan.Images).Path);
    }

    [Fact]
    public void A_Missing_Or_Foreign_Reference_Frame_File_Is_Refused()
    {
        var missing = Plan(
            [new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "refs/frame1.png", "", LoraStates.None)],
            NoLora(), WithImage());
        Assert.Equal(ObjectLoadProblems.ImageFileMissing, Assert.Single(missing.Problems).Code);

        var foreign = Plan(
            [new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "refs/frame1.bmp", "", LoraStates.None)],
            NoLora(), WithImage(), "refs/frame1.bmp");
        Assert.Equal(ObjectLoadProblems.ImageFormat, Assert.Single(foreign.Problems).Code);

        var empty = Plan(
            [new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "", "", LoraStates.None)],
            NoLora(), WithImage());
        Assert.Equal(ObjectLoadProblems.ImageNoPath, Assert.Single(empty.Problems).Code);
    }

    // ---------- 4. подключение адаптера к графу ComfyUI ----------

    /// <summary>
    /// Узел адаптера ВСТАВЛЯЕТСЯ в поставляемый шаблон: заранее вписать его туда нельзя
    /// (с пустым именем файла ComfyUI откажет в генерации без LoRA, T-13-S1). После вставки
    /// загрузчик весов остаётся на месте, а его потребитель переключается на адаптер.
    /// </summary>
    [Fact]
    public void The_Adapter_Node_Is_Inserted_Between_The_Loader_And_Its_Consumer()
    {
        _f.Models.Seed();
        var graph = GraphOf(T2VId);
        var apply = WithLora().Apply;

        var result = ComfyLoraGraph.Insert(graph,
            apply, [new ObjectLoadLora("OBJ-3", "Вася", "C:/ai/loras/OBJ-3.safetensors", 0.8)]);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        // сам загрузчик не тронут
        Assert.Equal("UNETLoader", root.GetProperty("4").GetProperty("class_type").GetString());
        // его потребитель (ModelSamplingSD3) теперь берёт модель у нового узла
        var newId = root.GetProperty("3").GetProperty("inputs").GetProperty("model")[0].GetString()!;
        Assert.NotEqual("4", newId);
        var lora = root.GetProperty(newId);
        Assert.Equal("LoraLoaderModelOnly", lora.GetProperty("class_type").GetString());
        Assert.Equal("OBJ-3.safetensors", lora.GetProperty("inputs").GetProperty("lora_name").GetString());
        Assert.Equal(0.8, lora.GetProperty("inputs").GetProperty("strength_model").GetDouble());
        // а сам адаптер берёт модель у загрузчика — цепочка не замкнулась на себя
        Assert.Equal("4", lora.GetProperty("inputs").GetProperty("model")[0].GetString());
    }

    /// <summary>Несколько адаптеров выстраиваются в цепочку — так же, как их соединяют руками.</summary>
    [Fact]
    public void Several_Adapters_Make_A_Chain()
    {
        _f.Models.Seed();
        var result = ComfyLoraGraph.Insert(GraphOf(T2VId), WithLora(maxCount: 2).Apply,
        [
            new ObjectLoadLora("OBJ-3", "Вася", "C:/ai/loras/a.safetensors", 1.0),
            new ObjectLoadLora("OBJ-4", "Маша", "C:/ai/loras/b.safetensors", 0.5),
        ]);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        var lastId = root.GetProperty("3").GetProperty("inputs").GetProperty("model")[0].GetString()!;
        var last = root.GetProperty(lastId);
        Assert.Equal("b.safetensors", last.GetProperty("inputs").GetProperty("lora_name").GetString());
        var firstId = last.GetProperty("inputs").GetProperty("model")[0].GetString()!;
        var first = root.GetProperty(firstId);
        Assert.Equal("a.safetensors", first.GetProperty("inputs").GetProperty("lora_name").GetString());
        Assert.Equal("4", first.GetProperty("inputs").GetProperty("model")[0].GetString());
    }

    /// <summary>Свой шаблон с плейсхолдером обслуживается подстановкой — узел в нём уже стоит
    /// там, куда его поставил автор шаблона.</summary>
    [Fact]
    public void A_Template_With_A_Placeholder_Is_Filled_Not_Rewritten()
    {
        const string graph = """
            {"1":{"class_type":"UNETLoader","inputs":{"unet_name":"w.safetensors"}},
             "2":{"class_type":"LoraLoaderModelOnly","inputs":{"lora_name":"{lora}","strength_model":"{loraStrength}","model":["1",0]}}}
            """;
        var apply = LoraSettings.Parse("""
            {"lora":{"supported":true,"apply":{"kind":"workflow","placeholder":"{lora}",
              "strengthPlaceholder":"{loraStrength}","strength":1.0}}}
            """).Apply;
        Assert.True(ComfyLoraGraph.HasPlaceholder(graph, apply));

        var result = ComfyLoraGraph.FillPlaceholders(graph, apply,
            [new ObjectLoadLora("OBJ-3", "Вася", "C:/ai/loras/OBJ-3.safetensors", 0.7)]);

        using var doc = JsonDocument.Parse(result);
        var inputs = doc.RootElement.GetProperty("2").GetProperty("inputs");
        Assert.Equal("OBJ-3.safetensors", inputs.GetProperty("lora_name").GetString());
        Assert.Equal(0.7, inputs.GetProperty("strength_model").GetDouble());
        // лишних узлов не появилось
        Assert.Equal(2, doc.RootElement.EnumerateObject().Count());
    }

    /// <summary>Подключать адаптер не к чему — это отказ, а не молчаливая генерация без него.</summary>
    [Fact]
    public void A_Graph_Without_A_Model_Loader_Refuses_The_Adapter()
    {
        const string graph = """{"1":{"class_type":"SaveVideo","inputs":{"format":"mp4"}}}""";

        var error = Assert.Throws<InvalidOperationException>(() =>
            ComfyLoraGraph.Insert(graph, WithLora().Apply,
                [new ObjectLoadLora("OBJ-3", "Вася", "C:/ai/loras/a.safetensors", 1.0)]));

        Assert.Contains("UNETLoader", error.Message);
    }

    // ---------- 5. «как грузить» — из справочника моделей ----------

    /// <summary>Профайл подключения отдаёт коннектору обе настройки: без этого «как грузить»
    /// пришлось бы зашивать в код (T-13-S1 завёл настройку, T-14-S1 её читает).</summary>
    [Fact]
    public void The_Connection_Profile_Carries_Both_Settings()
    {
        _f.Models.Seed();
        var i2v = ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(I2VId)!.ProfilePath));
        Assert.True(i2v.Lora.Supported);
        Assert.Equal(LoraApplyKinds.Workflow, i2v.Lora.Apply.Kind);
        Assert.Equal("LoraLoaderModelOnly", i2v.Lora.Apply.Node);
        Assert.Equal(RefImageKinds.Upload, i2v.RefImage.Kind);
        Assert.Equal("{image}", i2v.RefImage.Placeholder);
        Assert.True(i2v.RefImage.Required);

        var t2v = ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(T2VId)!.ProfilePath));
        Assert.True(t2v.Lora.Supported);
        Assert.False(t2v.RefImage.Supported);

        // облачная запись: адаптеров нет и причина названа
        var cloud = ModelProfile.Parse(
            _f.Files.ReadText(_f.Models.Get("6f1a45e0-0d31-4c65-9a01-000000000002")!.ProfilePath));
        Assert.False(cloud.Lora.Supported);
        Assert.Equal(LoraReasons.Provider, cloud.Lora.Reason);
    }

    /// <summary>Плейсхолдер картинки берётся из настройки, а не из зашитой строки.</summary>
    [Fact]
    public void The_Image_Placeholder_Comes_From_The_Catalogue()
    {
        var own = RefImageSettings.Parse("""{"refImage":{"kind":"upload","placeholder":"{startFrame}"}}""");
        Assert.True(ComfyUiConnector.NeedsStartImage("""{"1":{"image":"{startFrame}"}}""", own));
        Assert.False(ComfyUiConnector.NeedsStartImage("""{"1":{"image":"{image}"}}""", own));
        // настройки нет — прежний {image} (T-258)
        Assert.True(ComfyUiConnector.NeedsStartImage("""{"1":{"image":"{image}"}}""", new RefImageSettings()));
    }

    /// <summary>Собранный workflow несёт и картинку, и адаптер — то есть в ComfyUI уезжает
    /// ровно то, что решено загрузить.</summary>
    [Fact]
    public void BuildWorkflow_Carries_The_Image_And_The_Adapter()
    {
        _f.Models.Seed();
        var profile = ModelProfile.Parse(_f.Files.ReadText(_f.Models.Get(I2VId)!.ProfilePath));

        var workflow = _f.ComfyUi.BuildWorkflow(profile, "герой машет рукой", 42, "J-7",
            "ai2p_J-7.png", [new ObjectLoadLora("OBJ-3", "Вася", "C:/ai/loras/OBJ-3.safetensors", 0.9)]);

        Assert.DoesNotContain("{image}", workflow);
        Assert.Contains("LoraLoaderModelOnly", workflow);
        Assert.Contains("OBJ-3.safetensors", workflow);
        using var doc = JsonDocument.Parse(workflow);
        Assert.Equal("ai2p_J-7.png",
            doc.RootElement.GetProperty("12").GetProperty("inputs").GetProperty("image").GetString());
    }

    // ---------- 6. сервис: объекты задачи, файлы, тексты ----------

    [Fact]
    public void Refs_Are_Read_From_The_Raw_Description_Of_The_Task()
    {
        var project = _f.Projects.Create("Ролик", null, null, null).Id;
        var hero = NewObject(project, "Вася", ObjectKinds.Character);
        var frame = NewObject(project, "Кадр 1", ObjectKinds.Image, "refs/frame1.png");

        var refs = _f.ObjectLoads.RefsOf(project,
            $"Крупный план: {ObjectRefs.Marker(hero.DisplayId)}, дождь\n@obj:[Кадр 1]\n@obj:OBJ-999");

        Assert.Equal(2, refs.Count);
        Assert.Equal(hero.DisplayId, refs[0].Code);
        Assert.Equal(frame.DisplayId, refs[1].Code);
        Assert.Equal("refs/frame1.png", refs[1].ImagePath);
    }

    /// <summary>Файл адаптера ищется там, куда его кладёт обучение (T-12-S1): путь настройки
    /// <c>train.result.target</c> считается от репозитория моделей.</summary>
    [Fact]
    public void The_Adapter_File_Is_Found_In_The_Models_Repository()
    {
        var file = Path.Combine(_f.ModelsRepo, "loras", "OBJ-3.safetensors");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "weights");

        Assert.Equal(Path.GetFullPath(file),
            _f.ObjectLoads.FindLoraFile("loras/OBJ-3.safetensors", null));
        // и по одному имени файла — на случай, если путь записан иначе
        Assert.Equal(Path.GetFullPath(file), _f.ObjectLoads.FindLoraFile("OBJ-3.safetensors", null));
        Assert.Null(_f.ObjectLoads.FindLoraFile("loras/нет-такого.safetensors", null));
        // наружу не выпускаем
        Assert.Null(_f.ObjectLoads.FindLoraFile("../../secrets.json", null));
    }

    /// <summary>Текст ошибки перечисляет ВСЕ несовпадения: иначе после починки первого
    /// задание упало бы снова, и так по кругу.</summary>
    [Fact]
    public void The_Error_Text_Lists_Every_Problem()
    {
        var plan = Plan(
            [
                new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/a.safetensors",
                    LoraStates.Ready),
                new ObjectLoadRef("OBJ-2", "Кадр 1", ObjectKinds.Image, "refs/frame1.png", "",
                    LoraStates.None),
            ],
            NoLora(), NoImage(), "loras/a.safetensors", "refs/frame1.png");

        var text = ObjectLoadService.ErrorText(plan);
        Assert.StartsWith(Loc.T("msg.objectLoad.13"), text);
        Assert.Contains("OBJ-3", text);
        Assert.Contains("OBJ-2", text);
        Assert.Equal(2, text.Split('•').Length - 1);
    }

    /// <summary>
    /// Сообщение в чат отвечает на «а что теперь делать»: перечисляет обученные адаптеры
    /// проекта и исполнителей, чья модель умеет то, чего не хватило.
    /// </summary>
    [Fact]
    public void The_Chat_Message_Offers_Adapters_And_Substitutes()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null).Id;
        var ready = NewObject(project, "Маша-LoRA", ObjectKinds.Lora);
        ready.LoraStatus = LoraStates.Ready;
        ready.LoraPath = "loras/masha.safetensors";
        _f.Objects.Update(ready, null);

        var comfy = NewAiExecutor("comfy-1", I2VId);
        var cloud = NewAiExecutor("cloud-1", "6f1a45e0-0d31-4c65-9a01-000000000002");

        var plan = Plan(
            [new ObjectLoadRef("OBJ-3", "Вася-LoRA", ObjectKinds.Lora, "", "loras/a.safetensors",
                LoraStates.Ready)],
            NoLora(), NoImage(), "loras/a.safetensors");

        var text = _f.ObjectLoads.ChatText(plan, project, cloud.Id);

        Assert.Contains(Loc.T("msg.objectLoad.14"), text);
        Assert.Contains("Маша-LoRA", text);
        Assert.Contains("loras/masha.safetensors", text);
        Assert.Contains(Loc.T("msg.objectLoad.16"), text);
        // предлагается тот, чья модель адаптеры принимает, и НЕ предлагается сам назначенный
        Assert.Contains(comfy.Nick, text);
        Assert.DoesNotContain(cloud.Nick, text);
    }

    // ---------- 7. задание целиком ----------

    /// <summary>
    /// Медиа-задание с недоступным объектом ВСТАЁТ С ОШИБКОЙ до всякого обращения к ComfyUI:
    /// в артефакте — причина с номером объекта, в чате задачи — предложение замены.
    /// </summary>
    [Fact]
    public async Task A_Media_Job_Fails_With_A_Named_Reason_And_Writes_To_The_Chat()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewObject(project.Id, "Вася-LoRA", ObjectKinds.Lora);

        // исполнитель на t2v-модели: адаптеры она принимает, но обученного файла нет
        var executor = NewAiExecutor("comfy-t2v", T2VId);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Кадр 1",
            ExecutorIds = [executor.Id],
        }, $"Герой машет рукой. {ObjectRefs.Marker(lora.DisplayId)}", "", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.ComfyUi.SubmitJobAsync(job, "не важно");
        await WaitState(job.Id, JobState.Failed);

        var failed = _f.Jobs.Get(job.Id)!;
        var error = _f.Files.ReadText(failed.ResultPath!);
        Assert.Contains(lora.DisplayId, error);
        Assert.Contains(Loc.T("msg.objectLoad.state.none"), error);
        Assert.Equal(TaskStatuses.Error, _f.Tasks.Get(task.Id)!.Status);

        var message = Assert.Single(_f.Chat.ListByTask(task.Id));
        Assert.Contains(lora.DisplayId, message.Text);
        Assert.Contains(Loc.T("msg.objectLoad.13"), message.Text);
    }

    /// <summary>Та же задача, но с персонажем без адаптера, доходит до ComfyUI: остановить
    /// её проверкой объектов нельзя — это обычный ролик по паспорту (T-259).</summary>
    [Fact]
    public async Task A_Plain_Character_Job_Reaches_The_Engine()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var hero = NewObject(project.Id, "Вася", ObjectKinds.Character, passport: "рыжий, в плаще");
        var executor = NewAiExecutor("comfy-t2v", T2VId);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            Title = "Кадр 1",
            ExecutorIds = [executor.Id],
        }, $"Герой машет рукой. {ObjectRefs.Marker(hero.DisplayId)}", "", null);

        var job = _f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);
        await _f.ComfyUi.SubmitJobAsync(job, "не важно");
        await WaitState(job.Id, JobState.Failed);

        // ComfyUI на 127.0.0.1:8188 в тестах не поднят, поэтому задание всё равно упадёт —
        // но уже НА ОБРАЩЕНИИ К ДВИЖКУ, а не на проверке объектов
        var error = _f.Files.ReadText(_f.Jobs.Get(job.Id)!.ResultPath!);
        Assert.DoesNotContain(Loc.T("msg.objectLoad.13"), error);
    }

    // ---------- 8. словари ----------

    /// <summary>У каждого кода несовпадения и каждого состояния адаптера есть текст в обоих
    /// языках: непереведённый код вылезает человеку прямо в ошибку задания.</summary>
    [Fact]
    public void Every_Problem_Code_And_State_Has_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            for (var i = 1; i <= 21; i++)
            {
                var text = Loc.In(lang, "msg.objectLoad." + i);
                Assert.NotEqual("msg.objectLoad." + i, text);
                Assert.NotEmpty(text);
            }
            foreach (var state in ObjectLoadPlanner.States)
            {
                var key = "msg.objectLoad.state." + state;
                Assert.NotEqual(key, Loc.In(lang, key));
            }
        }
        // коды несовпадений делятся на два лагеря: из-за адаптера и из-за картинки —
        // по этому признаку в чат подбирается замена
        Assert.All(ObjectLoadProblems.All, code => Assert.Contains('-', code));
        Assert.True(ObjectLoadProblems.IsLora(ObjectLoadProblems.LoraTooMany));
        Assert.False(ObjectLoadProblems.IsLora(ObjectLoadProblems.ImageTooMany));
    }

    // ---------- обвязка ----------

    private ObjectItem NewObject(string projectId, string name, string kind = ObjectKinds.Character,
        string path = "", string passport = "") =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = kind,
            PathOrUrl = path,
            Description = passport,
        }, null);

    private Executor NewAiExecutor(string nick, string modelId)
    {
        var model = _f.Models.Get(modelId)!;
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            IsActive = true,
            ModelId = modelId,
            ProfilePath = model.ProfilePath,
            CapabilitiesPath = model.CapabilitiesPath,
            SystemRole = SystemRole.Editor,
        }, null);
    }

    private string GraphOf(string modelId)
    {
        var template = _f.Files.ReadText($"models/workflow_{modelId}.json");
        using var doc = JsonDocument.Parse(template);
        return doc.RootElement.GetProperty("prompt").GetRawText();
    }

    private async Task WaitState(string jobId, JobState state)
    {
        for (var i = 0; i < 600 && _f.Jobs.Get(jobId)!.State != state; i++)
        {
            await Task.Delay(100);
        }
        Assert.Equal(state, _f.Jobs.Get(jobId)!.State);
    }
}
