using System.Text.Json;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-370-S0 «Настройки для claude-opus-5-5_cli»: запись справочника под Opus 5.5,
/// запускаемый через Claude Code CLI. В выпуске 1.144 (T-359-S0) записи намеренно не было —
/// установленный тогда Claude Code 2.1.278 отвечал отказом
/// «[claude-code:unrecognized_model]: … version 2.1.280 or newer is required». CLI обновлён,
/// и живой запуск 24.09.2026 на 2.1.281 (<c>claude -p --output-format json --model
/// claude-opus-5-5 «ok»</c>) дал код возврата 0 и <c>modelUsage: ["claude-opus-5-5"]</c>.
///
/// Тем же запуском проверено, что алиас <c>opus</c> уже означает claude-opus-5-5 (в 1.144 он
/// означал claude-opus-5) — поэтому в записи стоит полное имя версии, а не алиас.
///
/// Проверяется то, что расходится молча: запись есть и настроена на свою модель, а прежняя
/// запись Claude-Opus-5.0_cli осталась на claude-opus-5 (версии сосуществуют) и документ
/// написан на всех языках дистрибутива.
/// </summary>
public sealed class T370S0Tests : IDisposable
{
    private const string Name = "Claude-Opus-5.5_cli";
    private const string ModelId = "claude-opus-5-5";

    private readonly StorageFixture _f = new();

    public T370S0Tests()
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

    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    [Fact]
    public void Opus_5_5_Is_Available_Over_The_Cli()
    {
        var model = Model(Name);
        var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
        using var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));

        Assert.True(model.IsActive);                 // ключ не нужен — активна сразу
        Assert.Equal("anthropic", profile.Provider);
        Assert.Equal("cli", profile.Transport);
        Assert.Equal("claude --permission-mode acceptEdits", profile.CliCommand);
        Assert.Equal("", profile.SecretRef);         // авторизация сессией CLI, не ключом
        Assert.Equal(ModelId, profile.Model);        // полное имя версии, НЕ алиас «opus»
        Assert.Equal(ModelId, scope.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void The_Older_Cli_Entry_Keeps_Its_Own_Model()
    {
        var older = Model("Claude-Opus-5.0_cli");

        Assert.True(older.IsActive, "Claude-Opus-5.0_cli погашена, а должна сосуществовать с 5.5");
        Assert.Equal("claude-opus-5", ModelProfile.Parse(_f.Files.ReadText(older.ProfilePath)).Model);
    }

    [Fact]
    public void The_Model_Has_A_Document_In_Every_Language()
    {
        var app = AppDir();
        Assert.NotEqual("", app);

        foreach (var lang in Langs)
        {
            var path = Path.Combine(app, "doc", lang, "models", Name + ".md");
            Assert.True(File.Exists(path), $"нет документа модели: {path}");

            var readme = File.ReadAllText(Path.Combine(app, "doc", lang, "models", "README.md"));
            Assert.Contains(Name + ".md", readme);
        }
    }
}
