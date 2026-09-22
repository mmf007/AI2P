using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-287-S0: ПОЛЯ ГРАФА ComfyUI ИЗ JSON МОДЕЛИ-СУФЛЁРА (первый случай — ACE-Step 1.5).
///
/// До этой правки коннектор знал восемь плейсхолдеров, и все, кроме <c>{prompt}</c>, брались
/// из ПРОФАЙЛА, то есть были одинаковы во всех заданиях: у ACE-Step 1.5 всё описание задачи
/// оседало в поле стилевых тэгов, слова песни были пусты всегда, длительность — 120 с,
/// язык — «unknown». Теперь значения приносит управляющий json суфлёра (T-286-S0 объявил
/// секцию <c>prompter</c>, T-288-S0 запускает саму модель), а коннектор подставляет их по
/// имени: <c>{p:имя}</c>.
///
/// Что проверяется:
/// <list type="number">
/// <item>ПОДСТАНОВКА: значения суфлёра встают в нужные поля узла, число уходит числом,
/// строка — строкой в кавычках;</item>
/// <item>УСТОЙЧИВОСТЬ: без суфлёра, с мусором вместо json и с неназванным полем шаблон
/// собирается ровно как до правки, а незаполненных <c>{p:…}</c> в графе не остаётся вовсе
/// (иначе это «required input is missing» уже во время задания);</item>
/// <item>ДЛИТЕЛЬНОСТЬ В ДВУХ МЕСТАХ: <c>seconds</c> пустого латента равен <c>duration</c>
/// планировщика — расхождение даёт обрезанный или растянутый трек;</item>
/// <item>ЭКРАНИРОВАНИЕ: слова песни с кавычками и переводами строк не ломают json, а текст
/// от суфлёра не толкуется как плейсхолдер;</item>
/// <item>СХЕМА: имена полей и перечни значений совпадают с тем, что отдаёт ЖИВОЙ ComfyUI
/// (<c>GET /object_info/TextEncodeAceStepAudio1.5</c> и <c>/EmptyAceStep1.5LatentAudio</c>,
/// снято 18.09.2026 — <c>test/t287s0/objinfo.py</c>).</item>
/// </list>
/// </summary>
public sealed class T287S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public T287S0Tests() => _f.Models.Seed();

    public void Dispose() => _f.Dispose();

    /// <summary>Варианты ACE-Step 1.5 XL — у всех троих один шаблон и одна схема.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in T18S0Tests.AudioModels)
        {
            data.Add(name);
        }
        return data;
    }

    private ModelProfile Profile(string name = "ACE-Step-1.5-XL-Turbo") =>
        ModelProfile.Parse(_f.Files.ReadText(_f.Models.List().Single(m => m.Name == name).ProfilePath));

    /// <summary>Собрать граф и разобрать его — то же, что уедет в ComfyUI.</summary>
    private JsonDocument Build(ModelProfile profile, string? prompterJson,
        string prompt = "спокойный эмбиент про осень", bool noLeftovers = true)
    {
        var built = _f.ComfyUi.BuildWorkflow(profile, prompt, 42, "J-7",
            prompterJson: prompterJson);
        if (noLeftovers)
        {
            // незаполненный плейсхолдер в графе — «required input is missing» уже во
            // время задания, поэтому проверяется в КАЖДОЙ сборке этого класса
            Assert.DoesNotContain("{p:", built, StringComparison.Ordinal);
        }
        return JsonDocument.Parse(built);
    }

    private static JsonElement Node(JsonDocument graph, string classType) =>
        graph.RootElement.EnumerateObject()
            .Single(n => n.Value.GetProperty("class_type").GetString() == classType)
            .Value.GetProperty("inputs");

    private static JsonElement Planner(JsonDocument graph) =>
        Node(graph, "TextEncodeAceStepAudio1.5");

    private static JsonElement Latent(JsonDocument graph) =>
        Node(graph, "EmptyAceStep1.5LatentAudio");

    /// <summary>Управляющий json «как от суфлёра» — все семь полей ACE-Step.</summary>
    private const string FullAnswer = """
        {
          "tags": "dream pop, женский вокал, синтезатор, дождь",
          "lyrics": "Первый куплет\nвторой куплет",
          "duration": 90,
          "language": "ru",
          "bpm": 84,
          "keyscale": "A minor",
          "timesignature": "3"
        }
        """;

    // ---------- 1. подстановка ----------

    [Theory]
    [MemberData(nameof(Names))]
    public void The_Values_Of_The_Prompter_Reach_The_Fields_Of_The_Node(string name)
    {
        var profile = Profile(name);

        using var graph = Build(profile, FullAnswer);

        var planner = Planner(graph);
        Assert.Equal("dream pop, женский вокал, синтезатор, дождь", planner.GetProperty("tags").GetString());
        Assert.Equal("Первый куплет\nвторой куплет", planner.GetProperty("lyrics").GetString());
        Assert.Equal(90.0, planner.GetProperty("duration").GetDouble());
        Assert.Equal("ru", planner.GetProperty("language").GetString());
        Assert.Equal(84, planner.GetProperty("bpm").GetInt32());
        Assert.Equal("A minor", planner.GetProperty("keyscale").GetString());
        Assert.Equal("3", planner.GetProperty("timesignature").GetString());
        // число уехало ЧИСЛОМ, а размер такта — строкой: перепутай форму, и ComfyUI
        // откажет в счёте, а выглядело бы это поломкой движка
        Assert.Equal(JsonValueKind.Number, planner.GetProperty("bpm").ValueKind);
        Assert.Equal(JsonValueKind.Number, planner.GetProperty("duration").ValueKind);
        Assert.Equal(JsonValueKind.String, planner.GetProperty("timesignature").ValueKind);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_Empty_Latent_Gets_The_Same_Duration(string name)
    {
        var profile = Profile(name);

        using var told = Build(profile, FullAnswer);
        using var silent = Build(profile, null);

        Assert.Equal(90.0, Latent(told).GetProperty("seconds").GetDouble());
        Assert.Equal(Planner(told).GetProperty("duration").GetDouble(),
            Latent(told).GetProperty("seconds").GetDouble());
        // и без суфлёра обе величины те же и обе — из профайла
        Assert.Equal(profile.MediaLength, Latent(silent).GetProperty("seconds").GetInt32());
        Assert.Equal(profile.MediaLength, Planner(silent).GetProperty("duration").GetInt32());
    }

    // ---------- 2. устойчивость ----------

    [Theory]
    [MemberData(nameof(Names))]
    public void Without_A_Prompter_The_Template_Is_Built_As_Before(string name)
    {
        var profile = Profile(name);

        using var graph = Build(profile, null, "спокойный эмбиент, 72 удара в минуту");

        var planner = Planner(graph);
        // главное: описание задачи по-прежнему уходит в тэги, остальное — умолчания шаблона
        Assert.Equal("спокойный эмбиент, 72 удара в минуту", planner.GetProperty("tags").GetString());
        Assert.Equal("", planner.GetProperty("lyrics").GetString());
        Assert.Equal(profile.MediaLength, planner.GetProperty("duration").GetInt32());
        Assert.Equal("unknown", planner.GetProperty("language").GetString());
        Assert.Equal(120, planner.GetProperty("bpm").GetInt32());
        Assert.Equal("C major", planner.GetProperty("keyscale").GetString());
        Assert.Equal("4", planner.GetProperty("timesignature").GetString());
        Assert.Equal(42, planner.GetProperty("seed").GetInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("вот вам json: нет")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"tags\": {\"жанр\": \"рок\"}, \"duration\": [90]}")]
    public void A_Broken_Answer_Of_The_Prompter_Leaves_The_Defaults(string answer)
    {
        var profile = Profile();

        using var graph = Build(profile, answer, "марш");

        var planner = Planner(graph);
        Assert.Equal("марш", planner.GetProperty("tags").GetString());
        Assert.Equal(profile.MediaLength, planner.GetProperty("duration").GetInt32());
        Assert.Equal("unknown", planner.GetProperty("language").GetString());
    }

    [Fact]
    public void A_Field_The_Prompter_Did_Not_Name_Keeps_Its_Default()
    {
        var profile = Profile();

        using var graph = Build(profile, """{"tags": "марш, духовой оркестр", "duration": 45}""");

        var planner = Planner(graph);
        Assert.Equal("марш, духовой оркестр", planner.GetProperty("tags").GetString());
        Assert.Equal(45.0, planner.GetProperty("duration").GetDouble());
        Assert.Equal(45.0, Latent(graph).GetProperty("seconds").GetDouble());
        Assert.Equal("", planner.GetProperty("lyrics").GetString());
        Assert.Equal(120, planner.GetProperty("bpm").GetInt32());
        Assert.Equal("unknown", planner.GetProperty("language").GetString());
    }

    [Fact]
    public void A_Value_Of_The_Wrong_Kind_Is_Read_By_The_Schema_Or_Skipped()
    {
        var profile = Profile();

        using var graph = Build(profile,
            """{"duration": "много", "bpm": "84", "generate_audio_codes": "как хочешь"}""");

        var planner = Planner(graph);
        // «много» числом не станет — осталось умолчание; «84» строкой схема читает числом
        Assert.Equal(profile.MediaLength, planner.GetProperty("duration").GetInt32());
        Assert.Equal(84, planner.GetProperty("bpm").GetInt32());
        // поля, которого нет в графе, суфлёр назвать не может — граф от этого не меняется
        Assert.True(planner.GetProperty("generate_audio_codes").GetBoolean());
    }

    [Fact]
    public void A_Value_Beyond_The_Node_Limits_Is_Pulled_To_The_Border()
    {
        var profile = Profile();

        using var graph = Build(profile, """{"duration": 5000, "bpm": 1}""");

        // пределы узла: seconds 1…1000, bpm 10…300 (сняты с живого ComfyUI). За пределом
        // ComfyUI не «немного ошибается», а отказывается считать
        Assert.Equal(1000.0, Planner(graph).GetProperty("duration").GetDouble());
        Assert.Equal(1000.0, Latent(graph).GetProperty("seconds").GetDouble());
        Assert.Equal(10, Planner(graph).GetProperty("bpm").GetInt32());
    }

    [Fact]
    public void A_Value_Outside_The_List_Of_The_Node_Is_Skipped()
    {
        var profile = Profile();

        using var graph = Build(profile,
            """{"language": "эльфийский", "timesignature": "7", "keyscale": "H major"}""");

        var planner = Planner(graph);
        Assert.Equal("unknown", planner.GetProperty("language").GetString());
        Assert.Equal("4", planner.GetProperty("timesignature").GetString());
        Assert.Equal("C major", planner.GetProperty("keyscale").GetString());
    }

    // ---------- 3. экранирование ----------

    [Fact]
    public void Quotes_And_Line_Breaks_Of_The_Lyrics_Do_Not_Break_The_Json()
    {
        var profile = Profile();
        const string lyrics = "Он сказал: \"беги\"\r\nи \\ ушёл\tв туман";

        using var graph = Build(profile, JsonSerializer.Serialize(new { lyrics }));

        Assert.Equal(lyrics, Planner(graph).GetProperty("lyrics").GetString());
    }

    [Fact]
    public void The_Text_Of_The_Prompter_Is_Not_Read_As_A_Placeholder()
    {
        var profile = Profile();

        using var graph = Build(profile,
            """{"tags": "песня про {prompt} и \"{seed}\"", "lyrics": "{p:tags}"}""",
            "ОПИСАНИЕ ЗАДАЧИ", noLeftovers: false);

        var planner = Planner(graph);
        Assert.Equal("песня про {prompt} и \"{seed}\"", planner.GetProperty("tags").GetString());
        Assert.Equal("{p:tags}", planner.GetProperty("lyrics").GetString());
    }

    // ---------- 4. свой шаблон: формы плейсхолдера ----------

    /// <summary>Профайл со своим шаблоном — проверять формы подстановки на поставляемом
    /// графе негде: в нём нет ни встроенного текста, ни полей вне схемы.</summary>
    private ModelProfile Custom(string workflow, string prompterSection = "\"prompter\": {}")
    {
        _f.Files.WriteText("models/t287s0_workflow.json", workflow);
        return ModelProfile.Parse($$"""
            {
              "provider": "comfyui",
              "model": "t287s0",
              "workflow": "models/t287s0_workflow.json",
              "params": { "length": 7, "steps": 4 },
              {{prompterSection}}
            }
            """);
    }

    [Fact]
    public void A_Placeholder_Inside_A_Longer_String_Stays_Text()
    {
        var profile = Custom("""
            {"1": {"class_type": "N", "inputs": {"text": "стиль: {p:tags}; язык: {p:language|тот же}"}}}
            """);

        using var graph = Build(profile, """{"tags": "рок \"тяжёлый\""}""");

        Assert.Equal("стиль: рок \"тяжёлый\"; язык: тот же",
            graph.RootElement.GetProperty("1").GetProperty("inputs").GetProperty("text").GetString());
    }

    [Fact]
    public void Without_A_Schema_The_Kind_Comes_From_The_Value_Itself()
    {
        var profile = Custom("""
            {"1": {"class_type": "N", "inputs": {"n": "{p:n|0}", "flag": "{p:flag|false}", "s": "{p:s|}"}}}
            """);

        using var graph = Build(profile, """{"n": 7.5, "flag": true, "s": "текст"}""");

        var inputs = graph.RootElement.GetProperty("1").GetProperty("inputs");
        Assert.Equal(7.5, inputs.GetProperty("n").GetDouble());
        Assert.True(inputs.GetProperty("flag").GetBoolean());
        Assert.Equal("текст", inputs.GetProperty("s").GetString());
    }

    [Fact]
    public void A_Placeholder_Without_A_Value_And_Without_A_Default_Leaves_An_Empty_Field()
    {
        // шаблон недописан — но {p:…} в графе оставлять нельзя ни в каком случае:
        // ComfyUI ответил бы «required input is missing», и это выглядело бы поломкой движка
        var profile = Custom("""
            {"1": {"class_type": "N", "inputs": {"s": "{p:s}", "n": "{p:n}"}}}
            """,
            """
            "prompter": { "required": true, "schema": [ { "name": "n", "type": "int" } ] }
            """);

        using var graph = Build(profile, null);

        var inputs = graph.RootElement.GetProperty("1").GetProperty("inputs");
        Assert.Equal("", inputs.GetProperty("s").GetString());
        Assert.Equal(0, inputs.GetProperty("n").GetInt32());
    }

    [Fact]
    public void A_Template_Without_Named_Placeholders_Is_Built_As_It_Was()
    {
        var profile = Custom("""
            {"1": {"class_type": "N", "inputs": {"text": "{prompt}", "len": "{length}", "steps": "{steps}"}}}
            """);

        using var graph = Build(profile, FullAnswer, "описание");

        var inputs = graph.RootElement.GetProperty("1").GetProperty("inputs");
        Assert.Equal("описание", inputs.GetProperty("text").GetString());
        Assert.Equal(7, inputs.GetProperty("len").GetInt32());
        Assert.Equal(4, inputs.GetProperty("steps").GetInt32());
    }

    // ---------- 5. схема профайла против живого ComfyUI ----------

    [Theory]
    [MemberData(nameof(Names))]
    public void The_Schema_Names_The_Inputs_Of_The_Live_Node(string name)
    {
        var prompter = Profile(name).Prompter;

        Assert.True(prompter.Required);
        Assert.Equal(
            new[] { "tags", "lyrics", "duration", "language", "bpm", "keyscale", "timesignature" },
            prompter.Schema.Select(f => f.Name).ToArray());
        // границы и перечни — со снимка /object_info живого ComfyUI (18.09.2026):
        // duration FLOAT 0…2000 у планировщика и seconds FLOAT 1…1000 у латента, значит
        // общий предел — 1…1000; bpm INT 10…300
        Assert.Equal(1.0, prompter.Field("duration")!.Min!.Value);
        Assert.Equal(1000.0, prompter.Field("duration")!.Max!.Value);
        Assert.Equal(10.0, prompter.Field("bpm")!.Min!.Value);
        Assert.Equal(300.0, prompter.Field("bpm")!.Max!.Value);
        Assert.Equal(new[] { "2", "3", "4", "6" }, prompter.Field("timesignature")!.Values.ToArray());
        var languages = prompter.Field("language")!.Values;
        Assert.Equal(51, languages.Count);
        Assert.Contains("unknown", languages);
        Assert.Contains("ru", languages);
        Assert.Contains("yue", languages);
        var keyscales = prompter.Field("keyscale")!.Values;
        Assert.Equal(34, keyscales.Count); // 17 тоник × два лада
        Assert.Equal("C major", keyscales[0]);
        Assert.Contains("A minor", keyscales);
        // тип решает форму подстановки: перечень уходит строкой, темп — числом
        Assert.Equal(PrompterFieldTypes.Enum, prompter.Field("timesignature")!.Type);
        Assert.Equal(PrompterFieldTypes.Int, prompter.Field("bpm")!.Type);
        Assert.Equal(PrompterFieldTypes.Num, prompter.Field("duration")!.Type);
        Assert.True(prompter.Field("tags")!.Required);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_Field_Of_The_Schema_Stands_In_The_Graph(string name)
    {
        // разъехаться схема и шаблон могут молча: поле, объявленное суфлёру, но не
        // подставляемое никуда, — это обещание, которого система не держит
        var profile = Profile(name);
        var template = _f.Files.ReadText(profile.Workflow);

        foreach (var field in profile.Prompter.Schema)
        {
            Assert.Contains("{p:" + field.Name, template, StringComparison.Ordinal);
        }
    }
}
