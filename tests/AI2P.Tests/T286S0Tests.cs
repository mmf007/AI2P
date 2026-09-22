using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-286-S0 (ветка T-285-S0 «Вызов comfyUI для музыкальной модели»): МОДЕЛЬ-СУФЛЁР —
/// признак у медиамодели и поле у исполнителя.
///
/// Зачем это: медиа-задание уходит в модель ОДНИМ текстом, а у ACE-Step 1.5 длительность,
/// язык вокала, слова песни, темп и тональность — ОТДЕЛЬНЫЕ поля графа, которые сегодня
/// берутся из профайла и одинаковы во всех заданиях. Управляющий json для такой модели
/// готовит вторая, текстовая, модель — суфлёр. Здесь проверяется только ОБЪЯВЛЕНИЕ:
/// где помечено, что суфлёр нужен, и где он выбирается.
///
/// <list type="number">
/// <item>РАЗБОР секции <c>prompter</c> профайла: признак, путь правил и схема полей с
/// границами; секции нет, она пуста или в ней мусор — разбор не падает;</item>
/// <item>СПРАВОЧНИК: признак доезжает и до списка (колонка «Суфлёр»), и до одиночной
/// записи (форма модели), и правка профайла видна сразу;</item>
/// <item>СИД: у трёх вариантов ACE-Step 1.5 признак стоит, у остальных медиа-записей нет;</item>
/// <item>ИСПОЛНИТЕЛЬ: поле сохраняется, читается и уезжает репликацией (триггеры журнала
/// строятся по фактическому составу колонок);</item>
/// <item>ФОРМА: поле показывается только у медиамодели с признаком, а выбирать в суфлёры
/// можно только текстовую модель;</item>
/// <item>СЛОВАРИ: тексты есть во всех пяти языках.</item>
/// </list>
/// </summary>
public sealed class T286S0Tests
{
    private const string TurboName = "ACE-Step-1.5-XL-Turbo";
    private const string BaseName = "ACE-Step-1.5-XL-Base";
    private const string SftName = "ACE-Step-1.5-XL-SFT";

    // ---------- 1. разбор настройки ----------

    private const string ProfileWithPrompter = """
        {
          "provider": "comfyui",
          "model": "ace_step_1.5",
          "prompter": {
            "required": true,
            "rules": "models/prompter_test.md",
            "schema": [
              { "name": "tags", "type": "string", "required": true, "maxLength": 600,
                "description": "стилевые тэги" },
              { "name": "duration", "type": "int", "min": 10, "max": 240 },
              { "name": "language", "type": "enum", "values": ["unknown", "ru", "en"] }
            ]
          }
        }
        """;

    /// <summary>Секция читается целиком: признак, путь правил и схема полей с границами.</summary>
    [Fact]
    public void The_Prompter_Section_Is_Read_From_The_Profile()
    {
        var prompter = PrompterSettings.Parse(ProfileWithPrompter);

        Assert.True(prompter.Required);
        Assert.Equal("models/prompter_test.md", prompter.Rules);
        Assert.Equal(3, prompter.Schema.Count);

        var tags = prompter.Field("tags")!;
        Assert.Equal(PrompterFieldTypes.Str, tags.Type);
        Assert.True(tags.Required);
        Assert.Equal(600, tags.MaxLength);
        Assert.NotEmpty(tags.Description);

        var duration = prompter.Field("duration")!;
        Assert.Equal(PrompterFieldTypes.Int, duration.Type);
        Assert.False(duration.Required);
        Assert.Equal(10d, duration.Min);
        Assert.Equal(240d, duration.Max);

        var language = prompter.Field("language")!;
        Assert.Equal(PrompterFieldTypes.Enum, language.Type);
        Assert.Equal(new[] { "unknown", "ru", "en" }, language.Values);
        // границ у перечисления нет — «модель об этом не сказала», а не «ноль»
        Assert.Null(language.Min);
        Assert.Null(language.Max);
    }

    /// <summary>
    /// Молчание и мусор. Профайла нет, секции нет, она не объект, границы записаны словами —
    /// настройка обязана пережить всё это без исключения: профайл правит человек руками.
    /// Отсутствие секции значит «суфлёр не нужен» — это обычный путь всех медиа-моделей.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не json вовсе")]
    [InlineData("""{ "provider": "comfyui" }""")]
    [InlineData("""{ "prompter": "да" }""")]
    [InlineData("""{ "prompter": { "schema": "много" } }""")]
    public void Silence_And_Garbage_Mean_No_Prompter(string? profileJson)
    {
        var prompter = PrompterSettings.Parse(profileJson);

        Assert.False(prompter.Required);
        Assert.Empty(prompter.Rules);
        Assert.Empty(prompter.Schema);
        Assert.Null(prompter.Field("tags"));
    }

    /// <summary>
    /// Мусор ВНУТРИ схемы: поле без имени выбрасывается, «много» вместо числа границы не
    /// даёт (а не роняет разбор — JsonElement.TryGetInt32 на строке БРОСАЕТ, и вид значения
    /// обязан проверяться отдельно), неизвестный тип остаётся как есть.
    /// </summary>
    [Fact]
    public void A_Broken_Field_Of_The_Schema_Does_Not_Break_The_Rest()
    {
        var prompter = PrompterSettings.Parse("""
            {
              "prompter": {
                "required": true,
                "schema": [
                  { "type": "int" },
                  "строка вместо поля",
                  { "name": "bpm", "type": "int", "min": "много", "max": 200 },
                  { "name": "mood", "type": "чего-то-своё" }
                ]
              }
            }
            """);

        Assert.True(prompter.Required);
        Assert.Equal(2, prompter.Schema.Count);
        var bpm = prompter.Field("bpm")!;
        Assert.Null(bpm.Min);
        Assert.Equal(200d, bpm.Max);
        Assert.Equal("чего-то-своё", prompter.Field("mood")!.Type);
    }

    // ---------- 2–3. справочник и сид ----------

    /// <summary>
    /// Признак есть у ТРЁХ вариантов ACE-Step 1.5 и только у них: остальным медиа-записям
    /// хватает одного текста промпта, и просить у них суфлёра значило бы спрашивать у
    /// человека лишнюю модель. У помеченных названы и правила, и схема — объявление без
    /// них бесполезно: суфлёру нечего было бы сказать, а ответ нечем проверить.
    /// </summary>
    [Fact]
    public void Only_The_Three_Ace_Step_Records_Ask_For_A_Prompter()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var models = f.Models.ListWithSkills();

        var asking = models.Where(m => m.Prompter.Required).Select(m => m.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { BaseName, SftName, TurboName }, asking);

        foreach (var model in models.Where(m => m.Prompter.Required))
        {
            Assert.StartsWith("models/prompter_", model.Prompter.Rules, StringComparison.Ordinal);
            Assert.NotEmpty(model.Prompter.Schema);
            // ровно те поля узла TextEncodeAceStepAudio1.5, которыми задаётся трек
            Assert.NotNull(model.Prompter.Field("tags"));
            Assert.NotNull(model.Prompter.Field("lyrics"));
            var duration = model.Prompter.Field("duration")!;
            // ДРОБНОЕ, а не целое: у узла TextEncodeAceStepAudio1.5 поле duration и у
            // EmptyAceStep1.5LatentAudio поле seconds объявлены FLOAT — это снято с живого
            // ComfyUI в T-287-S0 (test/t287s0/objinfo.json) и отменило «int» по смыслу,
            // записанное здесь до сверки. Правка сторожа — T-288-S0
            Assert.Equal(PrompterFieldTypes.Num, duration.Type);
            Assert.True(duration.Min > 0, "у длительности не названа нижняя граница");
            Assert.True(duration.Max >= duration.Min, "у длительности не названа верхняя граница");
            // и сама запись — медийная: суфлёр нужен именно тем, кто отвечает не текстом
            Assert.True(model.IsMedia, model.Name);
            Assert.False(model.GivesText, model.Name);
        }
    }

    /// <summary>
    /// Отметка доезжает и до списка (колонка «Суфлёр»), и до одиночной записи (форма
    /// модели), и правка профайла действует сразу: признак ПРОИЗВОДНЫЙ — ни колонки в БД,
    /// ни миграции, ни шага обновления у него нет.
    /// </summary>
    [Fact]
    public void The_Mark_Reaches_Both_The_List_And_The_Single_Record()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var fromList = f.Models.ListWithSkills().Single(m => m.Name == TurboName);
        var single = f.Models.Get(fromList.Id)!;

        Assert.True(single.Prompter.Required);
        Assert.Equal(fromList.Prompter.Rules, single.Prompter.Rules);
        Assert.Equal(fromList.Prompter.Schema.Count, single.Prompter.Schema.Count);

        var abs = Path.Combine(f.Db.DataDir, single.ProfilePath);
        File.WriteAllText(abs, """{ "provider": "comfyui", "prompter": { "required": false } }""");
        Assert.False(f.Models.Get(single.Id)!.Prompter.Required);
    }

    /// <summary>
    /// Текстовая модель и медийная различаются по ДЕКЛАРАЦИИ (outputs), а не по названию:
    /// ровно этим форма исполнителя отбирает кандидатов в суфлёры. Декларация молчит —
    /// запись не считается ни той, ни другой.
    /// </summary>
    [Fact]
    public void A_Text_Model_Is_Told_From_A_Media_One_By_Its_Declaration()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var models = f.Models.ListWithSkills();

        var text = models.Single(m => m.Name == "Claude-Opus-5.0");
        Assert.True(text.GivesText);
        Assert.False(text.IsMedia);
        Assert.False(text.Prompter.Required);

        var ace = models.Single(m => m.Name == TurboName);
        Assert.Contains("audio/mpeg", ace.Outputs);
        Assert.True(ace.IsMedia);

        var silent = new AiModel { Name = "без декларации" };
        Assert.False(silent.GivesText);
        Assert.False(silent.IsMedia);
    }

    // ---------- 4. поле исполнителя ----------

    /// <summary>Поле сохраняется при создании и при правке, читается обратно, а пустая
    /// строка из формы приводится к «суфлёра нет». С T-292-S0 в поле лежит ИСПОЛНИТЕЛЬ,
    /// а не модель: суфлёр работает своим коннектором, и поэтому им годится кто угодно —
    /// подписка CLI, локальная модель, облачное API.</summary>
    [Fact]
    public void The_Executor_Keeps_Its_Prompter()
    {
        var f = new StorageFixture();
        f.Models.Seed();
        var models = f.Models.ListWithSkills();
        var ace = models.Single(m => m.Name == SftName);
        var text = models.Single(m => m.Name == "Claude-Opus-5.0");

        var helper = f.Executors.CreateMember(new Executor
        {
            Nick = "Суфлёр",
            InternalName = text.Name,
            Kind = ExecutorKind.Ai,
            SystemRole = SystemRole.Editor,
            ModelId = text.Id,
        }, null);
        var agent = f.Executors.CreateMember(new Executor
        {
            Nick = "Композитор",
            InternalName = ace.Name,
            Kind = ExecutorKind.Ai,
            SystemRole = SystemRole.Editor,
            ModelId = ace.Id,
            PrompterExecutorId = helper.Id,
        }, null);

        Assert.Equal(helper.Id, f.Executors.Get(agent.Id)!.PrompterExecutorId);
        Assert.Equal(helper.Id, f.Executors.List().Single(e => e.Id == agent.Id).PrompterExecutorId);

        // сняли суфлёра — поле очищается, а пустая строка не превращается в «суфлёр с пустым id»
        agent.PrompterExecutorId = "";
        f.Executors.Update(agent, null);
        Assert.Null(f.Executors.Get(agent.Id)!.PrompterExecutorId);
    }

    /// <summary>
    /// Колонка УЕЗЖАЕТ РЕПЛИКАЦИЕЙ сама: триггеры журнала изменений перечисляют колонки
    /// по ФАКТУ (ChangeLog.Install → ColumnsOf), поэтому правки ChangeLog новой колонке
    /// не нужно (наука T-265-S0).
    /// </summary>
    [Fact]
    public void The_Column_Is_Replicated_Without_Any_Change_To_The_Log()
    {
        var f = new StorageFixture();
        using var conn = f.Db.Open();

        var columns = Sql.Query(conn, null, "PRAGMA table_info(executors)", r => r.S("name"));
        Assert.Contains("prompter_executor_id", columns);

        var trigger = Sql.Scalar<string>(conn, null,
            "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='ai2p_chg_executors_ins'") ?? "";
        Assert.Contains("prompter_executor_id", trigger);
    }

    // ---------- 5. форма исполнителя ----------

    /// <summary>
    /// Поле «Модель-суфлёр» показывается ровно при двух условиях сразу — модель МЕДИЙНАЯ
    /// и она сама попросила суфлёра, — а выбор сужен до активных ТЕКСТОВЫХ моделей.
    /// Разметка проверяется по файлу (приём T-217/T-238), сама же связка условий
    /// перепроверяется на записях справочника выше.
    /// </summary>
    [Fact]
    public void The_Form_Asks_For_A_Prompter_Only_Where_It_Is_Needed()
    {
        var dialog = ExecutorDialog();

        Assert.Contains("data-executor-prompter", dialog);
        Assert.Contains("L[\"executors.prompterExecutor\"]", dialog);
        Assert.Contains("@if (PrompterNeeded)", dialog);
        // оба условия вместе: медийная И с признаком
        Assert.Contains("SelectedModel is { IsMedia: true, Prompter.Required: true }", dialog);
        // кандидаты (T-292-S0) — активные ИИ-исполнители, кроме себя самого
        Assert.Contains("e.Kind == ExecutorKind.Ai && e.Id != _executor.Id", dialog);
        // поле уезжает на сервер и приезжает обратно вместе с записью
        Assert.Contains("PrompterExecutorId = Source.PrompterExecutorId", dialog);
    }

    /// <summary>Колонка «Суфлёр» есть в ОБОИХ представлениях справочника моделей —
    /// и в таблице (включая краткую), и в представлении «навыки».</summary>
    [Fact]
    public void The_Models_Table_Has_The_Prompter_Column()
    {
        var settings = SettingsView();

        Assert.Equal(2, CountOf(settings, "L[\"models.prompter\"]"));
        Assert.Equal(2, CountOf(settings, "@PrompterCell("));
        Assert.Contains("data-model-prompter=", settings);
        // в представлении «навыки» строка категории растягивается на все колонки
        Assert.Contains("SkillViewColumns => State.IsCluster ? 9 : 8", settings);
    }

    // ---------- 6. словари ----------

    /// <summary>Новые тексты есть во ВСЕХ пяти языках: составы словарей обязаны сходиться.</summary>
    [Fact]
    public void The_New_Texts_Are_In_Every_Dictionary()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(dir, lang + ".json")))!;
            foreach (var key in new[]
                     {
                         "models.prompter", "models.prompter.required", "models.prompter.none",
                         "executors.prompterModel", "executors.prompterModel.hint",
                     })
            {
                Assert.True(dict.TryGetValue(key, out var text) && text.Length > 0,
                    $"{lang}.json: нет текста {key}");
            }
        }
    }

    // ---------- вспомогательное ----------

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string Ui(string file) => File.ReadAllText(Path.Combine(
        RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", file));

    private static string ExecutorDialog() => Ui("ExecutorDialog.razor");

    private static string SettingsView() => Ui("SettingsView.razor");
}
