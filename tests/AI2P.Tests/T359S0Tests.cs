using System.Text.Json;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-359-S0 «Переключение модели Claude CLI»: записи справочника под версии и защита от
/// подмены. Переключать модель у Claude Code CLI умеет само поле <c>model</c> профайла
/// (оно уходит флагом <c>--model</c>), поэтому задача свелась к трём вещам: завести по
/// записи на версию, научить пробу проверять идентификатор и сделать подмену модели видимой.
///
/// Идентификаторы сверены ЖИВЫМ ЗАПУСКОМ 24.09.2026 (<c>claude -p --output-format json
/// --model &lt;id&gt; «ok»</c>, смотрели <c>modelUsage</c> ответа): алиасы fable/opus/sonnet/haiku
/// отвечают claude-fable-5-1 / claude-opus-5 / claude-sonnet-5 / claude-haiku-4-5-20251001,
/// полные имена отвечают сами собой, а неизвестный id даёт код возврата 1 и
/// «[claude-code:unrecognized_model]» — молчаливой подмены не случилось ни разу.
///
/// Проверяется то, что расходится молча:
/// <list type="bullet">
/// <item>новые записи ЕСТЬ, у каждой свой <c>model</c> и совпадающий id декларации;</item>
/// <item>прежние записи версий НЕ погашены и НЕ переписаны — весь смысл в сосуществовании;</item>
/// <item>у каждой новой записи документ на всех языках и строка в оглавлении раздела;</item>
/// <item>правило сверки моделей: алиас и уточнение датой — не подмена, чужая модель — подмена.</item>
/// </list>
/// </summary>
public sealed class T359S0Tests : IDisposable
{
    /// <summary>Записи этого задания и идентификатор модели у провайдера.</summary>
    public static readonly (string Name, string Id)[] Added =
    [
        ("Claude-Fable-5.1_cli", "claude-fable-5-1"),
        ("Claude-Haiku-4.5_cli", "claude-haiku-4-5"),
    ];

    /// <summary>Записи прежних версий: они обязаны остаться как были.</summary>
    public static readonly (string Name, string Id)[] Kept =
    [
        ("Claude-Fable-5_cli", "claude-fable-5"),
        ("Claude-Opus-5.0_cli", "claude-opus-5"),
        ("Claude-Sonnet-5_cli", "claude-sonnet-5"),
    ];

    public static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in Added)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T359S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private AI2P.Core.Entities.AiModel Model(string name) =>
        _f.Models.List().Single(m => m.Name == name);

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

    // --- записи справочника ---

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_Version_Has_Its_Own_Entry_With_Its_Own_Model(string name)
    {
        var model = Model(name);
        var expected = Added.Single(a => a.Name == name).Id;

        var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
        using var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));

        Assert.Equal("cli", profile.Transport);
        Assert.Equal("claude --permission-mode acceptEdits", profile.CliCommand);
        Assert.Equal("", profile.SecretRef);
        Assert.Equal(expected, profile.Model);
        Assert.Equal(expected, scope.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void Older_Versions_Stay_Alive_With_Their_Own_Model()
    {
        foreach (var (name, id) in Kept)
        {
            var model = Model(name);
            Assert.True(model.IsActive, $"{name} погашена, а должна сосуществовать с новыми");
            Assert.Equal(id, ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath)).Model);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Model_Has_A_Document_In_Every_Language(string name)
    {
        var app = AppDir();
        Assert.NotEqual("", app);

        foreach (var lang in Langs)
        {
            var path = Path.Combine(app, "doc", lang, "models", name + ".md");
            Assert.True(File.Exists(path), $"нет документа модели: {path}");

            var readme = File.ReadAllText(Path.Combine(app, "doc", lang, "models", "README.md"));
            Assert.Contains(name + ".md", readme);
        }
    }

    // --- сверка запрошенной и фактической модели ---

    [Theory]
    // полное совпадение
    [InlineData("claude-sonnet-5", "claude-sonnet-5", true)]
    // уточнение датой: так отвечает claude-haiku-4-5
    [InlineData("claude-haiku-4-5", "claude-haiku-4-5-20251001", true)]
    // алиасы последней версии (живой запуск 24.09.2026)
    [InlineData("fable", "claude-fable-5-1", true)]
    [InlineData("opus", "claude-opus-5", true)]
    [InlineData("haiku", "claude-haiku-4-5-20251001", true)]
    // модель не задана либо провайдер её не назвал — ругаться не на что
    [InlineData("", "claude-opus-5", true)]
    [InlineData("claude-opus-5", "", true)]
    // ПОДМЕНА: просили старшую, ответила младшая
    [InlineData("claude-opus-5-5", "claude-sonnet-5", false)]
    [InlineData("claude-fable-5-1", "claude-fable-5", false)]
    [InlineData("opus", "claude-sonnet-5", false)]
    public void Substitution_Is_Told_Apart_From_An_Alias(string want, string got, bool ok) =>
        Assert.Equal(ok, ModelIdMatch.Matches(want, got));
}
