using System.Text.Json;
using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-466-S0 «Актуализация моделей» (сверка рынка 13.10.2026). Обход производителей из опыта
/// узла шаблона дал девять записей, вышедших после прошлого обхода (T-347-S0, 23.09.2026);
/// гасить по правилу «три более старших версии» в этот раз было нечего.
///
/// Проверяется то же, что у T347S0Tests: записи есть, профайл и декларация называют одну и
/// ту же модель, документ есть на всех языках и в оглавлении, суфлёра не просит ни одна
/// (все записи облачные, управляющий json суфлёра читает только ComfyUiConnector).
/// Идентификаторы и цены сверены запросом к каталогам openrouter.ai/api/v1/models и
/// fal.ai/api/models; живьём ни одна модель не вызывалась — ключей нет.
/// </summary>
public sealed class T466S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием, и их идентификатор у провайдера.</summary>
    public static readonly (string Name, string Id)[] Added =
    [
        ("Claude-Sonnet-5.5", "claude-sonnet-5-5"),
        ("Claude-Haiku-5.5", "claude-haiku-5-5"),
        ("GPT-6.1-Sol", "gpt-6.1-sol"),
        ("Mistral-Large-4", "mistral-large-4-0"),
        ("Muse-Wonder", "meta/muse-wonder"),
        ("Nano-Banana-2.1", "google/nano-banana-2.1"),
        ("Kandinsky-6.0-Pro", "fal-ai/kandinsky6-pro/text-to-video"),
        ("FLUX-3", "blackforestlabs/flux-3/text-to-image"),
        ("Ideogram-4.5", "ideogram/v4.5"),
    ];

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

    public T466S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private AI2P.Core.Entities.AiModel Model(string name) =>
        _f.Models.List().Single(m => m.Name == name);

    [Theory]
    [MemberData(nameof(Names))]
    public void Profile_And_Scope_Name_The_Same_Model(string name)
    {
        var model = Model(name);
        var expected = Added.Single(a => a.Name == name).Id;

        var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
        using var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));

        Assert.Equal(expected, profile.Model);
        Assert.Equal(expected, scope.RootElement.GetProperty("id").GetString());
        Assert.False(profile.Prompter.Required, $"{name}: суфлёр этой записи ни к чему");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Model_Has_A_Document_In_Every_Language(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir.Length > 0 && !Directory.Exists(Path.Combine(dir, "doc", "ru", "models")))
        {
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        Assert.NotEqual("", dir);

        foreach (var lang in T347S0Tests.Langs)
        {
            var path = Path.Combine(dir, "doc", lang, "models", name + ".md");
            Assert.True(File.Exists(path), $"нет документа модели: {path}");
            var readme = File.ReadAllText(Path.Combine(dir, "doc", lang, "models", "README.md"));
            Assert.Contains(name + ".md", readme);
        }
    }
}
