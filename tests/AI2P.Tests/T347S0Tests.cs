using System.Text.Json;
using AI2P.Connectors;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-347-S0 «Актуализация моделей» (сверка рынка 23.09.2026). Обход производителей из опыта
/// узла шаблона дал девять записей, вышедших после прошлого обхода (T-216-S0, 11.09.2026),
/// плюс одну лёгкую локальную запись под роль СУФЛЁРА (дополнительная задача), и две записи,
/// которые рынок обогнал.
///
/// Проверяется то, что расходится молча:
/// <list type="bullet">
/// <item>новые записи ЕСТЬ в справочнике, у каждой разбираются профайл и декларация;</item>
/// <item>идентификатор профайла СОВПАДАЕТ с идентификатором декларации — расхождение
/// означает, что задание уйдёт одной модели, а счёт выпишется другой;</item>
/// <item>у каждой новой записи есть документ на ВСЕХ языках интерфейса и строка в оглавлении
/// раздела: документ открывает кнопка «i» формы модели, и его отсутствие видно только там;</item>
/// <item>устаревшие записи Wan 2.2 ПОГАШЕНЫ, но НЕ удалены;</item>
/// <item>суфлёра не просит НИ ОДНА новая запись: управляющий json читает только
/// ComfyUiConnector, у облачных записей он ушёл бы в никуда.</item>
/// </list>
///
/// Живьём ни одна модель не вызывалась: ключей API этих провайдеров у нас нет. Идентификатор,
/// длина контекста, цена и модальности каждой сверены запросом к публичным каталогам
/// (openrouter.ai/api/v1/models и fal.ai/api/models), размер файла весов локальной записи —
/// HEAD-запросом к HuggingFace.
/// </summary>
public sealed class T347S0Tests : IDisposable
{
    /// <summary>Записи, заведённые этим заданием, и их идентификатор у провайдера.</summary>
    public static readonly (string Name, string Id)[] Added =
    [
        ("GPT-6-Sol", "gpt-6-sol"),
        ("GPT-6-Luna", "gpt-6-luna"),
        ("Claude-Opus-5.5", "claude-opus-5-5"),
        ("Grok-4.7", "grok-4.7"),
        ("Qwen3.8-Omni-Flash", "qwen3.8-omni-flash"),
        ("Seedream-5-Flash", "bytedance/seedream/v5/flash/text-to-image"),
        ("Meshy-7.1", "meshy/v7.1/text-to-3d"),
        ("Tripo-P2", "tripo3d/p2/image-to-3d"),
        ("ElevenLabs-Music-v2.5", "elevenlabs/music/v2.5"),
        ("Qwen3.5-4B-Local", "qwen3.5-4b"),
    ];

    /// <summary>Записи, погашенные этим заданием (у Wan 2.2 вышли 2.6, 2.7 и 3.0).</summary>
    public static readonly string[] Retired = ["Wan-2.2-T2V-A14B", "Wan-2.2-I2V-A14B"];

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

    public T347S0Tests()
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

    /// <summary>
    /// СУФЛЁР новым записям не нужен, и это не забывчивость: управляющий json читает только
    /// ComfyUiConnector (JobOrchestrator → UsePrompter), а все десять записей идут через
    /// openai-compatible, anthropic или fal-ai. Поставленный им признак означал бы лишнее
    /// платное задание, ответ которого никто не прочитает.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void None_Of_The_New_Records_Asks_For_A_Prompter(string name)
    {
        var profile = ModelProfile.Parse(_f.Files.ReadText(Model(name).ProfilePath));
        Assert.False(profile.Prompter.Required, $"{name}: суфлёр этой записи ни к чему");
    }

    /// <summary>
    /// Лёгкая локальная запись под роль суфлёра: манифест установки с ТОЧНЫМ размером файла
    /// (снят HEAD-запросом), пакет llama.cpp и свой порт — без всего этого «Установить» не
    /// работает, а без установки локальная модель активной быть не может.
    /// </summary>
    [Fact]
    public void The_Prompter_Sized_Local_Model_Is_Installable()
    {
        var profileJson = _f.Files.ReadText(Model("Qwen3.5-4B-Local").ProfilePath);
        var manifest = ModelInstallManifest.Parse(profileJson);

        Assert.NotNull(manifest);
        Assert.Equal(2_590_430_368L, manifest!.TotalSize);   // ровно то, что отдаёт HuggingFace
        Assert.Contains("llama.cpp", manifest.AllPackages);
        // порт свой: у соседних локальных записей 8080–8083
        Assert.Contains("8084", profileJson);
    }

    // --- погашенные записи ---

    [Fact]
    public void Outdated_Wan_Records_Are_Switched_Off_But_Kept()
    {
        var models = _f.Models.List();

        foreach (var name in Retired)
        {
            var model = models.SingleOrDefault(m => m.Name == name);
            Assert.NotNull(model);                       // запись НЕ удалена
            Assert.False(model!.IsActive, $"{name} должна быть погашена");
            Assert.Contains(model.Id, AiModelService.RetiredSeedModels);
        }
    }

    [Fact]
    public void Retiring_Twice_Changes_Nothing()
    {
        // шаг обновления идемпотентен: на уже погашенных записях он не делает ничего
        Assert.Equal(0, _f.Models.RetireOutdatedSeedModels());
    }
}
