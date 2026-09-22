using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-251-S0 (записи справочника с референсным аудио). Механизм секции <c>refAudio</c>
/// сделан в T-249-S0, но НИ ОДНА поставляемая запись его не объявляла — «принимает образец
/// голоса» не умела ни одна модель. Здесь заведены первые две: <c>Chatterbox-TTS</c>
/// (fal-ai/chatterbox/text-to-speech, веса MIT) и <c>Zonos-2-TTS</c> (fal-ai/zonos2,
/// веса Apache 2.0).
///
/// Проверяется то, что расходится молча:
/// <list type="bullet">
/// <item>ИМЯ ПОЛЯ образца в секции <c>refAudio</c> совпадает с именем поля в шаблоне
/// <c>request</c> — незнакомое поле шлюз отвергает HTTP 422 уже во время ПЛАТНОГО задания,
/// а имена у эндпойнтов РАЗНЫЕ (<c>audio_url</c> и <c>reference_audio_url</c>);</item>
/// <item>плейсхолдер <c>{audio}</c> стоит в шаблоне запроса — иначе
/// <see cref="FalAiConnector.NeedsRefAudio"/> решит, что запись модели не нужна, и задание
/// уйдёт на шлюз БЕЗ образца, чужим голосом;</item>
/// <item>пара <c>inputs</c>/<c>outputs</c> совместима с навыком по правилам
/// <see cref="SkillIo"/>: при несовпадении <c>ExecutorPickService</c> обнуляет оценку, и
/// запись остаётся в справочнике, не берясь ни на одну задачу (наука T-257);</item>
/// <item>документы модели есть на ВСЕХ ПЯТИ языках и в каждом — раздел лицензии.</item>
/// </list>
///
/// Живьём ни одна модель не вызывалась: генерация платная.
/// </summary>
public sealed class T251S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием, и имя поля образца у их эндпойнта.</summary>
    public static readonly (string Name, string Endpoint, string Field)[] Records =
    [
        ("Chatterbox-TTS", "fal-ai/chatterbox/text-to-speech", "audio_url"),
        ("Zonos-2-TTS", "fal-ai/zonos2", "reference_audio_url"),
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var r in Records)
        {
            data.Add(r.Name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T251S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private AI2P.Core.Entities.AiModel Model(string name) =>
        _f.Models.List().Single(m => m.Name == name);

    private string Profile(string name) => _f.Files.ReadText(Model(name).ProfilePath);

    private string Scope(string name) => _f.Files.ReadText(Model(name).CapabilitiesPath);

    private static string DocPath(string lang, string name) =>
        Path.Combine(AppDir(), "doc", lang, "models", name + ".md");

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

    // --- справочник ---

    [Fact]
    public void Catalogue_Has_Both_Voice_Clone_Records()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var r in Records)
        {
            Assert.Contains(r.Name, names);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Record_Declares_Reference_Audio_As_A_Request_Field(string name)
    {
        var refAudio = RefAudioSettings.Parse(Profile(name));

        Assert.True(refAudio.Supported);
        Assert.Equal(RefAudioKinds.RequestField, refAudio.Kind);
        Assert.Equal("{audio}", refAudio.Placeholder);
        Assert.Equal(1, refAudio.MaxCount);
        Assert.Equal(30, refAudio.MaxSeconds);
        Assert.True(refAudio.Required);
        Assert.Contains("audio/wav", refAudio.Formats);
        Assert.Contains("audio/mpeg", refAudio.Formats);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Reference_Audio_Field_Is_The_One_The_Endpoint_Really_Has(string name)
    {
        // имена полей сняты со схемы OpenAPI очереди шлюза 14.09.2026 и у эндпойнтов РАЗНЫЕ:
        // выдуманное имя шлюз отвергает HTTP 422 уже во время платного задания
        var expected = Records.Single(r => r.Name == name);
        var profile = ModelProfile.Parse(Profile(name));
        var refAudio = RefAudioSettings.Parse(Profile(name));

        Assert.Equal(expected.Endpoint, profile.Model);
        Assert.Equal(expected.Field, refAudio.Field);
        Assert.Contains($"\"{expected.Field}\": \"{{audio}}\"", profile.Request, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Connector_Sees_That_The_Model_Waits_For_A_Recording(string name)
    {
        // признак «модель ждёт запись» — ПЛЕЙСХОЛДЕР В ШАБЛОНЕ, а не сама настройка
        // (T-249-S0): без него задание ушло бы на шлюз без образца и чужим голосом
        var profile = ModelProfile.Parse(Profile(name));
        var refAudio = RefAudioSettings.Parse(Profile(name));

        Assert.True(FalAiConnector.NeedsRefAudio(profile.Request, refAudio));
        Assert.False(FalAiConnector.NeedsStartImage(profile.Request, profile.RefImage));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Declared_Skills_Survive_The_Executor_Pick_Rules(string name)
    {
        // при несовпадении inputs/outputs с носителями навыка ExecutorPickService обнуляет
        // оценку, и запись не берётся НИ НА ОДНУ задачу — молча (наука T-257)
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        var inputs = root.GetProperty("inputs").EnumerateArray().Select(i => i.GetString()!).ToList();
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(o => o.GetString()!).ToList();

        Assert.Contains("text/plain", inputs);
        var skills = root.GetProperty("skills").EnumerateArray().ToList();
        Assert.NotEmpty(skills);
        foreach (var skill in skills)
        {
            var code = skill.GetProperty("name").GetString()!;
            Assert.Equal("audio-speech", code);
            Assert.NotNull(SkillIo.For(code));
            Assert.True(SkillIo.Fits(code, inputs, outputs), $"{name}: {code}");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Gateway_Records_Refuse_Lora_Honestly(string name)
    {
        var lora = LoraSettings.Parse(Profile(name));

        Assert.False(lora.Supported);
    }

    [Fact]
    public void Plain_Speech_Still_Goes_To_The_Record_Without_A_Sample()
    {
        // обе новые записи ТРЕБУЮТ образец, поэтому обычную озвучку должен брать
        // ElevenLabs-TTS-v3: его оценка audio-speech обязана остаться выше
        var eleven = ScoreOf("ElevenLabs-TTS-v3");

        foreach (var r in Records)
        {
            Assert.True(ScoreOf(r.Name) < eleven, r.Name);
        }
    }

    private int ScoreOf(string name)
    {
        using var scope = JsonDocument.Parse(Scope(name));
        return scope.RootElement.GetProperty("skills").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "audio-speech")
            .GetProperty("score").GetInt32();
    }

    // --- документы ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Exists_In_All_Five_Languages(string name)
    {
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            Assert.True(File.Exists(DocPath(lang, name)), DocPath(lang, name));
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Has_The_Licence_Section_In_Every_Language(string name)
    {
        var headings = new[]
        {
            ("ru", "## Лицензия"), ("en", "## Licence"), ("es", "## Licencia"),
            ("pt", "## Licença"), ("zh-cn", "## 许可证"),
        };

        foreach (var (lang, heading) in headings)
        {
            var text = File.ReadAllText(DocPath(lang, name));
            Assert.Contains(heading, text, StringComparison.Ordinal);
            Assert.Contains("licenseType: commercial", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Document_Repeats_The_Identifier_And_The_Sample_Field(string name)
    {
        // документ и справочник расходятся молча (наука T-214)
        var expected = Records.Single(r => r.Name == name);

        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            var text = File.ReadAllText(DocPath(lang, name));
            Assert.Contains(expected.Endpoint, text, StringComparison.Ordinal);
            Assert.Contains(expected.Field, text, StringComparison.Ordinal);
            Assert.Contains("fal.apiKey", text, StringComparison.Ordinal);
        }
    }
}
