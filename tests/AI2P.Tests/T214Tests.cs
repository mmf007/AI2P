using System.Text.Json;
using AI2P.Connectors;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-214 (подключение кодовых моделей): справочник моделей дистрибутива пополнен по
/// таблицам §5.1 и §5.2 отчёта <c>doc/T-213_современное_состояние_ИИ_отчёт.md</c> —
/// закрытые флагманы, открытые веса, дешёвые API, российские и локальные модели.
///
/// Проверяется то, что ломается молча: состав справочника, разбираемость профайлов и
/// деклараций, коды навыков и форматов из справочников (иначе декларация не сойдётся с
/// подбором исполнителя), ссылка на ключ у облачных, манифест и СВОЙ порт у локальных,
/// документ модели на обоих языках.
/// </summary>
public sealed class T214Tests : IDisposable
{
    /// <summary>
    /// Записи справочника дистрибутива: имя → «модель из таблицы §5» отчёта. Список
    /// намеренно задан здесь целиком: он же — список документов, которые обязаны быть
    /// написаны на ru и en.
    /// </summary>
    public static readonly string[] SeededModels =
    [
        // было до T-214
        "Claude-Fable-5", "Claude-Fable-5_cli", "Claude-Opus-5.0", "Claude-Opus-5.0_cli",
        "DeepSeek-V4-Pro", "DeepSeek-V4-Flash", "Kandinsky-5.0-T2V-Lite-sft-5s",
        "Qwen3.6-35B-A3B-Local",
        // §5.1 закрытые флагманы
        "Claude-Sonnet-5", "Claude-Sonnet-5_cli", "Claude-Haiku-4.5",
        "GPT-5.6-Sol", "GPT-5.6-Terra",
        "Gemini-3-Ultra", "Gemini-3.1-Pro", "Gemini-3.7-Flash",
        "Grok-4.6", "Qwen3.8-Max", "Muse-Spark-1.2",
        // §5.2 открытые веса, дешёвые API, российские
        "Kimi-K3", "GLM-5.2", "MiniMax-M3", "Inkling-975B", "Nemotron-3-Ultra",
        "Ling-3.0-Flash", "Mistral-Large-3", "GigaChat-3.5-Ultra", "YandexGPT-5.1-Pro",
        // локальные (открытые веса на своём компьютере)
        "Qwen3.8-27B-Local", "Qwen3.6-27B-Local", "Muse-Glimmer-30B-Local",
        // T-258: вторая медиа-модель — та же Kandinsky 5, но «изображение → видео»
        "Kandinsky-5.0-I2V-Lite-5s",
    ];

    public static TheoryData<string> ModelNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in SeededModels)
        {
            data.Add(name);
        }
        return data;
    }

    private readonly StorageFixture _f = new();

    public T214Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private string Profile(string name) =>
        _f.Files.ReadText(_f.Models.List().Single(m => m.Name == name).ProfilePath);

    private string Scope(string name) =>
        _f.Files.ReadText(_f.Models.List().Single(m => m.Name == name).CapabilitiesPath);

    // --- состав справочника ---

    [Fact]
    public void Seed_Contains_Every_Model_Of_Section_5()
    {
        var names = _f.Models.List().Select(m => m.Name).ToList();

        foreach (var expected in SeededModels)
        {
            Assert.Contains(expected, names);
        }
        // справочник вырос втрое: до задания в дистрибутиве было 8 записей
        Assert.True(names.Count >= 31, $"записей в справочнике {names.Count}, ожидалось не меньше 31");
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Seed_Model_Ids_Are_Unique()
    {
        // фиксированные UUID: на всех установках у записи дистрибутива один и тот же id,
        // и повтор id молча превратил бы две модели в одну
        var ids = _f.Models.List().Select(m => m.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // --- профайлы и декларации ---

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Profile_And_Scope_Are_Readable(string name)
    {
        var profile = ModelProfile.Parse(Profile(name));
        Assert.NotEqual("", profile.Provider);
        Assert.NotEqual("", profile.Model);

        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("id").GetString());
        Assert.NotEmpty(root.GetProperty("skills").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Skill_Codes_And_Io_Formats_Come_From_The_Reference(string name)
    {
        // код навыка мимо справочника = навык, которого нет ни у одной задачи: подбор
        // исполнителя такую декларацию не увидит вовсе, а ошибки не будет
        var skills = _f.RefData.Skills("en").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var formats = _f.RefData.IoFormats("en").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        using var scope = JsonDocument.Parse(Scope(name));
        var root = scope.RootElement;

        foreach (var skill in root.GetProperty("skills").EnumerateArray())
        {
            var code = skill.GetProperty("name").GetString()!;
            Assert.Contains(code, skills);
            var score = skill.GetProperty("score").GetInt32();
            Assert.InRange(score, 0, 100);
        }
        foreach (var key in new[] { "inputs", "outputs" })
        {
            foreach (var format in root.GetProperty(key).EnumerateArray())
            {
                Assert.Contains(format.GetString()!, formats);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Cloud_Model_Refers_To_A_Key_Local_One_Does_Not(string name)
    {
        var model = _f.Models.List().Single(m => m.Name == name);
        var profile = ModelProfile.Parse(Profile(name));

        if (model.IsLocal || profile.IsCli)
        {
            // локальная модель работает без ключа, у CLI авторизация — сессия claude login
            Assert.Equal("", profile.SecretRef);
            return;
        }
        // ссылка на секрет — «<провайдер>.<что>»: ключ общий у всех моделей провайдера,
        // поэтому вводится один раз (у GigaChat это не ключ, а токен доступа на 30 минут)
        Assert.NotEqual("", profile.SecretRef);
        Assert.Contains(".", profile.SecretRef, StringComparison.Ordinal);
    }

    // --- локальные модели: манифест установки ---

    [Theory]
    [InlineData("Qwen3.8-27B-Local", "Qwen3.8-27B-UD-Q4_K_XL.gguf", 17923394624L)]
    [InlineData("Qwen3.6-27B-Local", "Qwen3.6-27B-UD-Q4_K_XL.gguf", 17612564704L)]
    [InlineData("Muse-Glimmer-30B-Local", "Muse-Glimmer-30B-UD-Q4_K_XL.gguf", 15878222368L)]
    public void Local_Model_Manifest_Names_The_File_With_Its_Exact_Size(
        string name, string file, long size)
    {
        // установка сверяет скачанное по ТОЧНОМУ размеру из манифеста (ТЗ v1.42):
        // размеры сняты HEAD-запросами к HuggingFace 2026-08-17, test/t214/head.py
        using var profile = JsonDocument.Parse(Profile(name));
        var install = profile.RootElement.GetProperty("install");

        Assert.Equal("llama.cpp", install.GetProperty("packages")[0].GetString());
        var only = Assert.Single(install.GetProperty("files").EnumerateArray().ToList());
        Assert.Equal(file, only.GetProperty("name").GetString());
        Assert.Equal(size, only.GetProperty("size").GetInt64());
        Assert.Contains("huggingface.co", only.GetProperty("url").GetString());
    }

    [Fact]
    public void Local_Models_Listen_On_Different_Ports()
    {
        // два llama-server на одном порту рядом не поднимутся: вторая модель молча
        // отвечала бы ответами первой
        var ports = new List<string>();
        foreach (var name in SeededModels)
        {
            var profile = ModelProfile.Parse(Profile(name));
            if (profile.BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                ports.Add(new Uri(profile.BaseUrl).Port.ToString());
            }
        }

        Assert.True(ports.Count >= 4, $"локальных моделей с портом {ports.Count}");
        Assert.Equal(ports.Count, ports.Distinct().Count());
    }

    [Fact]
    public void Local_Models_Are_Marked_As_Local_And_Cloud_Ones_Are_Not()
    {
        Assert.True(_f.Models.List().Single(m => m.Name == "Qwen3.8-27B-Local").IsLocal);
        Assert.True(_f.Models.List().Single(m => m.Name == "Muse-Glimmer-30B-Local").IsLocal);
        Assert.False(_f.Models.List().Single(m => m.Name == "Kimi-K3").IsLocal);
        Assert.False(_f.Models.List().Single(m => m.Name == "GPT-5.6-Sol").IsLocal);
    }

    // --- подключение известными коннекторами ---

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Provider_Is_One_Of_The_Known_Connectors(string name)
    {
        // запись справочника с чужим провайдером выглядит рабочей до первого задания,
        // а падает уже на исполнителе (ConnectorRegistry.Resolve)
        var profile = ModelProfile.Parse(Profile(name));

        Assert.Contains(profile.Provider, new[] { "anthropic", "openai-compatible", "comfyui" });
        if (profile.IsCli)
        {
            Assert.Equal("anthropic", profile.Provider);
        }
    }

    [Fact]
    public void Openai_Compatible_Models_Have_An_Absolute_Base_Url()
    {
        foreach (var name in SeededModels)
        {
            var profile = ModelProfile.Parse(Profile(name));
            if (profile.Provider != "openai-compatible")
            {
                continue;
            }
            Assert.True(Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out _),
                $"у модели {name} baseUrl не адрес: {profile.BaseUrl}");
        }
    }

    // --- документация модели (ТЗ гл. 14) ---

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Every_Seeded_Model_Has_A_Document_In_Russian(string name)
    {
        var doc = new DocStore(() => "", () => "ru").ModelDoc(name);

        Assert.True(doc.Found, $"нет документа ru для модели {name}: {doc.Hint}");
        Assert.Equal("ru", doc.Language);
        Assert.Contains(name, doc.Text);
    }

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Every_Seeded_Model_Has_A_Document_In_English(string name)
    {
        // язык интерфейса en: откат на ru означал бы, что перевода нет
        var doc = new DocStore(() => "", () => "en").ModelDoc(name);

        Assert.True(doc.Found, $"нет документа en для модели {name}: {doc.Hint}");
        Assert.Equal("en", doc.Language);
        Assert.Contains(name, doc.Text);
    }

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void Document_Answers_The_Two_Obligatory_Questions(string name)
    {
        // ради чего документ модели существует (todo47): как получить ключ у облачной
        // и какие требования к железу у локальной
        var model = _f.Models.List().Single(m => m.Name == name);
        var profile = ModelProfile.Parse(Profile(name));
        var text = new DocStore(() => "", () => "ru").ModelDoc(name).Text;

        if (model.IsLocal)
        {
            Assert.Contains("Требования к железу", text);
            Assert.Contains("ГБ", text);
            return;
        }
        if (profile.IsCli)
        {
            Assert.Contains("ключ API не нужен", text, StringComparison.OrdinalIgnoreCase);
            return;
        }
        Assert.Contains("Как получить ключ", text);
        Assert.Contains("https://", text);
    }
}
