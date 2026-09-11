using System.Text.Json;
using AI2P.Connectors;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-216-S0 «Актуализация моделей» (сверка рынка 11.09.2026). Обход производителей из опыта
/// узла шаблона дал девять записей, вышедших после прошлого обхода (T-241 и подзадачи,
/// 27.08.2026), и две записи, которые рынок обогнал.
///
/// Проверяется то, что расходится молча:
/// <list type="bullet">
/// <item>новые записи ЕСТЬ в справочнике, у каждой разбираются профайл и декларация;</item>
/// <item>идентификатор в профайле СОВПАДАЕТ с идентификатором декларации — расхождение
/// означает, что задание уйдёт одной модели, а счёт выпишется другой;</item>
/// <item>у каждой новой записи есть документ на ВСЕХ языках интерфейса и строка в оглавлении
/// раздела: документ открывает кнопка «i» формы модели, и его отсутствие видно только там;</item>
/// <item>устаревшие записи ПОГАШЕНЫ, но НЕ удалены.</item>
/// </list>
///
/// Живьём ни одна модель не вызывалась: у нас нет ключей API этих провайдеров. Идентификатор,
/// длина контекста, цена и модальности каждой сверены запросом к публичным каталогам
/// (openrouter.ai/api/v1/models и fal.ai/api/models), что и требует задание.
/// </summary>
public sealed class T216S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием, и их идентификатор у провайдера.</summary>
    public static readonly (string Name, string Id)[] Added =
    [
        ("GPT-6-Astra", "gpt-6-astra"),
        ("Claude-Fable-5.1", "claude-fable-5-1"),
        ("Gemini-3.8-Flash", "gemini-3.8-flash"),
        ("Muse-Spark-1.3", "meta/muse-spark-1.3"),
        ("DeepSeek-V4.1-Flash", "deepseek-v4.1-flash"),
        ("GLM-5.3", "glm-5.3"),
        ("GPT-Image-2.5", "openai/gpt-image-2.5/flare/text-to-image"),
        ("Wan-3.0-Prime", "alibaba/wan-3.0-prime/text-to-video"),
        ("MiniMax-H3-Max", "minimax/h3-max/text-to-video"),
    ];

    /// <summary>Записи, погашенные этим заданием.</summary>
    public static readonly string[] Retired = ["Gemini-3-Ultra", "Gemini-3.1-Pro"];

    /// <summary>Языки интерфейса, на которых обязан быть документ модели.</summary>
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

    public T216S0Tests()
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

    // --- состав справочника ---

    [Fact]
    public void Catalogue_Has_Every_Model_Found_By_The_Sweep()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var (name, _) in Added)
        {
            Assert.Contains(name, names);
        }
        Assert.Equal(names.Count, names.Distinct().Count());
    }

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

            // ссылка на документ обязана стоять в оглавлении раздела — иначе документ
            // открывается только кнопкой «i» и в закладке «Документация» его не найти
            var readme = File.ReadAllText(Path.Combine(app, "doc", lang, "models", "README.md"));
            Assert.Contains(name + ".md", readme);
        }
    }

    // --- погашенные записи ---

    [Fact]
    public void Outdated_Models_Are_Switched_Off_But_Kept()
    {
        var models = _f.Models.List();

        foreach (var name in Retired)
        {
            var model = models.SingleOrDefault(m => m.Name == name);
            Assert.NotNull(model);                       // запись НЕ удалена
            Assert.False(model!.IsActive, $"{name} должна быть погашена");
        }
        // всё остальное гасить не собирались
        Assert.All(models.Where(m => !Retired.Contains(m.Name) && !m.IsLocal),
            m => Assert.True(m.IsActive, $"{m.Name} погашена по ошибке"));
    }

    [Fact]
    public void Retired_List_Matches_The_Records_It_Names()
    {
        var models = _f.Models.List();

        foreach (var id in AiModelService.RetiredSeedModels)
        {
            var model = models.SingleOrDefault(m => m.Id == id);
            Assert.NotNull(model);
            Assert.Contains(model!.Name, Retired);
        }
        Assert.Equal(Retired.Length, AiModelService.RetiredSeedModels.Count);
    }

    [Fact]
    public void Retiring_Twice_Changes_Nothing()
    {
        // шаг обновления идемпотентен: на уже погашенных записях он не делает ничего
        Assert.Equal(0, _f.Models.RetireOutdatedSeedModels());
    }
}
