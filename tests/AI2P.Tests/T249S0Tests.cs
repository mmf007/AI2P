using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-249-S0 (выпуск 1.133): РЕФЕРЕНСНОЕ АУДИО — «LoRA для аудио» на деле.
///
/// Постоянный голос человека и постоянный звук объекта у аудио-моделей достигаются не
/// адаптером, а ОБРАЗЦОМ записи, поэтому недостающим звеном был не тренер, а ВХОД:
/// настройка справочника <c>refAudio</c>, вид объекта «эталонная запись», разбор пути
/// звука в описании задачи и загрузка файла обоими медиа-коннекторами.
/// </summary>
public sealed class T249S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. настройка справочника ----------

    [Fact]
    public void RefAudio_Is_Read_From_The_Profile_Section()
    {
        var settings = RefAudioSettings.Parse("""
            {
              "provider": "comfyui",
              "refAudio": {
                "kind": "upload",
                "placeholder": "{audio}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "maxSeconds": 30,
                "formats": ["audio/wav", "audio/mpeg"]
              }
            }
            """);
        Assert.True(settings.Supported);
        Assert.Equal(RefAudioKinds.Upload, settings.Kind);
        Assert.Equal("{audio}", settings.Placeholder);
        Assert.Equal("image", settings.Field);
        Assert.Equal(30, settings.MaxSeconds);
        Assert.Equal(2, settings.Formats.Count);
    }

    /// <summary>Ключа нет вовсе — «передать нечем»: так выглядят все сегодняшние записи
    /// справочника, и ни одна из них от появления настройки не меняется.</summary>
    [Fact]
    public void A_Profile_Without_The_Section_Takes_No_Audio()
    {
        Assert.False(RefAudioSettings.Parse("""{"provider": "comfyui"}""").Supported);
        Assert.False(RefAudioSettings.Parse("не JSON вовсе").Supported);
        Assert.False(RefAudioSettings.Parse(null).Supported);
        Assert.False(new ModelProfile().RefAudio.Supported);
    }

    /// <summary>Профайл читается коннектором целиком — настройка едет вместе с остальным
    /// подключением, отдельного чтения файла для неё нет.</summary>
    [Fact]
    public void ModelProfile_Carries_RefAudio()
    {
        var profile = ModelProfile.Parse("""
            {
              "provider": "comfyui",
              "model": "ace",
              "refAudio": { "kind": "request-field", "field": "reference_audio_url" }
            }
            """);
        Assert.True(profile.RefAudio.Supported);
        Assert.Equal("reference_audio_url", profile.RefAudio.Field);
    }

    // ---------- 2. разбор описания задачи ----------

    [Fact]
    public void The_Directive_Names_The_Voice_Sample()
    {
        var (prompt, path) = MediaInputDirective.ParseAudio("""
            Образец голоса refs/vera.wav
            Спой куплет про дождь.
            """);
        Assert.Equal("refs/vera.wav", path);
        Assert.Equal("Спой куплет про дождь.", prompt);
    }

    /// <summary>Раскрытие ссылки «@obj:…» (T-259) кладёт путь файла в НАЧАЛО строки, а хвост
    /// фразы остаётся промптом — правило то же, что у кадра (T-258).</summary>
    [Fact]
    public void A_Line_Starting_With_An_Audio_Path_Is_Taken_And_Removed()
    {
        var (prompt, path) = MediaInputDirective.ParseAudio("""
            Вера — низкий хрипловатый голос.
            refs/vera.mp3, дубль второй
            """);
        Assert.Equal("refs/vera.mp3", path);
        Assert.DoesNotContain("refs/vera.mp3", prompt);
        Assert.Contains("дубль второй", prompt);
    }

    /// <summary>Кадр и запись разбираются ПОРОЗНЬ и не мешают друг другу: наборы расширений
    /// не пересекаются, поэтому одно описание несёт оба файла сразу.</summary>
    [Fact]
    public void Image_And_Audio_Are_Parsed_By_Two_Calls_In_A_Row()
    {
        var (afterImage, image) = MediaInputDirective.Parse("""
            Исходный кадр refs/hero.png
            Образец голоса refs/vera.wav
            Герой поёт под дождём.
            """);
        var (prompt, audio) = MediaInputDirective.ParseAudio(afterImage);
        Assert.Equal("refs/hero.png", image);
        Assert.Equal("refs/vera.wav", audio);
        Assert.Equal("Герой поёт под дождём.", prompt);
    }

    /// <summary>Выход за пределы папки проекта и чужие расширения не разбираются вовсе.</summary>
    [Fact]
    public void Escapes_And_Foreign_Extensions_Are_Not_Audio()
    {
        Assert.Null(MediaInputDirective.ParseAudio("образец голоса ../secret.wav").Path);
        Assert.Null(MediaInputDirective.ParseAudio(@"образец голоса C:\secret.wav").Path);
        Assert.Null(MediaInputDirective.ParseAudio("исходный файл scene.mp4").Path);
        // картинка записью не становится, и наоборот
        Assert.Null(MediaInputDirective.ParseAudio("исходный кадр refs/hero.png").Path);
        Assert.Null(MediaInputDirective.Parse("образец голоса refs/vera.wav").Path);
    }

    // ---------- 3. решение о загрузке объекта ----------

    private static ObjectLoadPlan Plan(RefAudioSettings audio, ObjectLoadRef item,
        params string[] existing) =>
        ObjectLoadPlanner.Plan([item], new LoraSettings(), new RefImageSettings(), "ace-step-1.5-xl",
            _ => null, path => existing.Contains(path), audio);

    private static ObjectLoadRef Voice(string path) =>
        new("OBJ-7", "Голос Веры", ObjectKinds.Audio, path, "", "");

    private static RefAudioSettings Upload() => new()
    {
        Kind = RefAudioKinds.Upload,
        Placeholder = "{audio}",
        Formats = ["audio/wav"],
    };

    [Fact]
    public void A_Model_Without_RefAudio_Refuses_A_Reference_Recording()
    {
        var plan = Plan(new RefAudioSettings(), Voice("refs/vera.wav"), "refs/vera.wav");
        Assert.False(plan.Ok);
        Assert.True(plan.AudioFailed);
        Assert.False(plan.ImageFailed);
        Assert.False(plan.LoraFailed);
        Assert.Equal(ObjectLoadProblems.AudioNotSupported, plan.Problems[0].Code);
        Assert.Contains("ace-step-1.5-xl", plan.Problems[0].Text);
    }

    [Fact]
    public void A_Reference_Recording_Goes_To_The_Model()
    {
        var plan = Plan(Upload(), Voice("refs/vera.wav"), "refs/vera.wav");
        Assert.True(plan.Ok);
        Assert.Equal("refs/vera.wav", Assert.Single(plan.Audios).Path);
        Assert.Empty(plan.Images);
    }

    [Fact]
    public void A_Missing_File_And_A_Foreign_Format_Are_Named_Apart()
    {
        var missing = Plan(Upload(), Voice("refs/vera.wav"));
        Assert.Equal(ObjectLoadProblems.AudioFileMissing, missing.Problems[0].Code);

        var format = Plan(Upload(), Voice("refs/vera.mp3"), "refs/vera.mp3");
        Assert.Equal(ObjectLoadProblems.AudioFormat, format.Problems[0].Code);

        var noPath = Plan(Upload(), Voice(""));
        Assert.Equal(ObjectLoadProblems.AudioNoPath, noPath.Problems[0].Code);
    }

    /// <summary>Лишние сверх максимума модели — в несовпадения, а не в тишину (правило T-14-S1).</summary>
    [Fact]
    public void Extra_Recordings_Are_Reported_Not_Dropped()
    {
        var plan = ObjectLoadPlanner.Plan(
            [Voice("refs/a.wav"), new ObjectLoadRef("OBJ-8", "Второй", ObjectKinds.Audio,
                "refs/b.wav", "", "")],
            new LoraSettings(), new RefImageSettings(), "ace-step-1.5-xl",
            _ => null, _ => true, Upload());
        Assert.Single(plan.Audios);
        Assert.Equal(ObjectLoadProblems.AudioTooMany, plan.Problems[0].Code);
    }

    // ---------- 4. коннекторы ----------

    /// <summary>Признак «модель ждёт запись» — плейсхолдер в её шаблоне, а НЕ сама настройка:
    /// поставляемый граф с пустым именем файла ComfyUI считать откажется, поэтому узел
    /// загрузки звука вписывает тот, кто пишет свой шаблон (та же наука, что с узлом LoRA).</summary>
    [Fact]
    public void ComfyUi_Needs_Audio_Only_When_The_Workflow_Asks_For_It()
    {
        Assert.True(ComfyUiConnector.NeedsRefAudio(
            """{"prompt": {"1": {"inputs": {"audio": "{audio}"}}}}""", Upload()));
        Assert.False(ComfyUiConnector.NeedsRefAudio(
            """{"prompt": {"1": {"inputs": {"text": "{prompt}"}}}}""", Upload()));
        // своё имя плейсхолдера — из справочника, а не зашито
        Assert.True(ComfyUiConnector.NeedsRefAudio("""{"x": "{voice}"}""",
            new RefAudioSettings { Kind = RefAudioKinds.Upload, Placeholder = "{voice}" }));
    }

    [Fact]
    public void ComfyUi_BuildWorkflow_Substitutes_The_Uploaded_Audio_Name()
    {
        const string workflow = """
            {"prompt": {
              "1": {"class_type": "LoadAudio", "inputs": {"audio": "{audio}"}},
              "2": {"class_type": "TextEncodeAceStepAudio", "inputs": {"tags": "{prompt}"}}
            }}
            """;
        _f.Files.WriteText("models/workflow_t249s0.json", workflow);
        var profile = ModelProfile.Parse("""
            {
              "provider": "comfyui",
              "model": "ace",
              "workflow": "models/workflow_t249s0.json",
              "refAudio": { "kind": "upload", "placeholder": "{audio}" }
            }
            """);

        var built = _f.ComfyUi.BuildWorkflow(profile, prompt: "спой про дождь", seed: 42,
            jobDisplayId: "J-7", imageName: null, adapters: null, audioName: "ai2p_J-7.wav");

        Assert.DoesNotContain("{audio}", built);
        using var doc = JsonDocument.Parse(built);
        Assert.Equal("ai2p_J-7.wav",
            doc.RootElement.GetProperty("1").GetProperty("inputs").GetProperty("audio").GetString());
    }

    /// <summary>У шлюза каталога input нет — запись уходит в теле запроса адресом data:,
    /// ровно как картинка (T-14-S0).</summary>
    [Fact]
    public void FalAi_Puts_The_Recording_Into_The_Request_Body()
    {
        var profile = ModelProfile.Parse("""
            {
              "provider": "fal-ai",
              "model": "fal-ai/tts",
              "request": {"prompt": "{prompt}", "reference_audio_url": "{audio}"},
              "refAudio": { "kind": "request-field", "placeholder": "{audio}" }
            }
            """);
        Assert.True(FalAiConnector.NeedsRefAudio(
            """{"reference_audio_url": "{audio}"}""", profile.RefAudio));

        var body = _f.FalAi.BuildRequest(profile, prompt: "привет", seed: 1, jobDisplayId: "J-8",
            imageDataUri: null, audioDataUri: "data:audio/wav;base64,AAAA");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("data:audio/wav;base64,AAAA",
            doc.RootElement.GetProperty("reference_audio_url").GetString());
    }

    // ---------- 5. вид объекта и словари ----------

    [Fact]
    public void The_Audio_Object_Kind_Is_Known_And_Listed_After_The_Frame()
    {
        Assert.True(ObjectKinds.IsKnown(ObjectKinds.Audio));
        Assert.Equal(Array.IndexOf(ObjectKinds.All, ObjectKinds.Image) + 1,
            Array.IndexOf(ObjectKinds.All, ObjectKinds.Audio));
        Assert.Equal(["image", "audio"], ObjectKinds.Parse("audio,image"));
        Assert.NotEqual("audio", ObjectKinds.Title(ObjectKinds.Audio));
    }

    /// <summary>Составы словарей обязаны сходиться посчётно (T-180): новый текст заводится
    /// сразу во все пять языков, иначе человек с другим языком увидит ключ.</summary>
    [Fact]
    public void Every_New_Key_Is_In_Every_Dictionary()
    {
        string[] keys =
        [
            "object.kind.audio", "models.refAudio",
            "models.refAudio.kind.none", "models.refAudio.kind.upload",
            "models.refAudio.kind.request-field", "models.refAudio.kind.message",
            "msg.objectLoad.22", "msg.objectLoad.23", "msg.objectLoad.24",
            "msg.objectLoad.25", "msg.objectLoad.26",
            "msg.comfyUiConnector.47", "msg.comfyUiConnector.48",
            "msg.comfyUiConnector.49", "msg.comfyUiConnector.50",
            "msg.falAiConnector.26",
        ];
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            foreach (var key in keys)
            {
                var text = Loc.In(lang, key);
                Assert.False(text == key || text.Length == 0,
                    $"словарь {lang}: нет ключа {key}");
            }
        }
    }
}
