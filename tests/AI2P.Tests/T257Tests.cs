using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Правка справочника навыков по разбору T-251, п. 1.2 (T-257): аббревиатуры режимов
/// (t2i/i2i/t2v/i2v) — в ОПИСАНИЯХ навыков, а не третьей категорией кодов; недостающие
/// действия-режимы вторым уровнем; пара inputs/outputs декларации начинает участвовать
/// в автоподборе; справочник io formats дополнен медийными кодами.
/// </summary>
public sealed class T257Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private string SkillId(string name) => _f.RefData.Skills().First(s => s.Name == name).Id;

    private Executor CreateAi(string nick, string scopeJson) => CreateExecutor(nick, scopeJson, ExecutorKind.Ai);

    private Executor CreateExecutor(string nick, string scopeJson, ExecutorKind kind)
    {
        var scopeRel = $"models/scope_t257_{nick}.json";
        _f.Files.WriteText(scopeRel, scopeJson);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = kind, CapabilitiesPath = scopeRel,
        }, null);
    }

    private Team CreateTeam(params Executor[] members) =>
        _f.Teams.Create(new Team
        {
            Name = "Команда-" + Guid.NewGuid().ToString("N")[..6],
            Members = members.Select(e => new TeamMember { ExecutorId = e.Id }).ToList(),
        }, null);

    /// <summary>Декларация t2v-модели: как у Kandinsky в дистрибутиве — вход только текст.</summary>
    private const string T2VScope = """
        {
          "inputs":  ["text/plain"],
          "outputs": ["video/*"],
          "skills":  [ { "name": "video-generate", "score": 76 }, { "name": "video-animate", "score": 72 } ]
        }
        """;

    /// <summary>Та же модель, но принимающая изображение (i2v).</summary>
    private const string I2VScope = """
        {
          "inputs":  ["text/plain", "image/*"],
          "outputs": ["video/*"],
          "skills":  [ { "name": "video-generate", "score": 76 }, { "name": "video-animate", "score": 72 } ]
        }
        """;

    // ── 1. Аббревиатуры в описаниях ───────────────────────────────────────────────

    /// <summary>Аббревиатура режима видна в описании навыка на ОБОИХ языках: человек,
    /// ищущий «i2v», находит навык поиском, а коды и декларации не тронуты.</summary>
    [Theory]
    [InlineData("image-generate", "t2i")]
    [InlineData("image-edit", "i2i")]
    [InlineData("video-generate", "t2v")]
    [InlineData("video-animate", "i2v")]
    [InlineData("audio-speech", "t2s")]
    [InlineData("3d-image", "i23d")]
    public void Mode_Abbreviations_Are_In_Skill_Descriptions(string code, string abbreviation)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var skill = _f.RefData.Skills(lang).Single(s => s.Name == code);
            Assert.Contains(abbreviation, skill.Description);
        }
    }

    /// <summary>Третьей категории под режимы НЕ заведено: кодов вида image-generate-t2i нет
    /// (они были бы синонимами базового навыка — автоподбор снимает уточняющие сегменты).</summary>
    [Fact]
    public void No_Third_Level_Codes_For_Modes()
    {
        var codes = _f.RefData.Skills().Select(s => s.Name).ToList();
        foreach (var mode in new[] { "t2i", "i2i", "t2v", "i2v", "flf2v", "a2v", "v2v" })
        {
            Assert.DoesNotContain(codes, c => c.EndsWith("-" + mode, StringComparison.Ordinal));
        }
    }

    // ── 2. Новые действия второго уровня ──────────────────────────────────────────

    /// <summary>Режимы, которые раньше сваливались в video-edit/image-edit, стали своими
    /// действиями второго уровня — тем же уровнем, что video-animate.</summary>
    [Theory]
    [InlineData("video-extend")]
    [InlineData("video-keyframes")]
    [InlineData("video-restyle")]
    [InlineData("video-lipsync")]
    [InlineData("image-inpaint")]
    public void New_Mode_Skills_Are_Builtin(string code)
    {
        var skill = _f.RefData.Skills().SingleOrDefault(s => s.Name == code);
        Assert.NotNull(skill);
        Assert.False(skill!.IsCustom);
        Assert.NotEqual("", _f.RefData.Skills("ru").Single(s => s.Name == code).Description);
        Assert.NotEqual("", _f.RefData.Skills("en").Single(s => s.Name == code).Description);
    }

    // ── 4. io formats ─────────────────────────────────────────────────────────────

    /// <summary>Справочник форматов дополнен медийными кодами (для деклараций медиа-моделей).</summary>
    [Theory]
    [InlineData("video/mp4")]
    [InlineData("audio/wav")]
    [InlineData("audio/mpeg")]
    [InlineData("image/webp")]
    [InlineData("model/glb")]
    public void New_Io_Formats_Are_Builtin(string code)
    {
        var format = _f.RefData.IoFormats().SingleOrDefault(f => f.Name == code);
        Assert.NotNull(format);
        Assert.False(format!.IsCustom);
        Assert.NotEqual("", _f.RefData.IoFormats("ru").Single(f => f.Name == code).Description);
        Assert.NotEqual("", _f.RefData.IoFormats("en").Single(f => f.Name == code).Description);
    }

    /// <summary>
    /// Рост seedVersion обязателен: без него описания на уже работающих установках остались бы
    /// старыми (RefDataService.Seed переписывает тексты только при version &gt; applied).
    /// Здесь БД доведена «до прошлой версии» и с описанием без аббревиатуры — сеяние её чинит.
    /// </summary>
    [Fact]
    public void Higher_Seed_Version_Refreshes_Existing_Descriptions()
    {
        var id = SkillId("video-animate");
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE meta SET value='1' WHERE key='refdata_seed_version'");
            Sql.Exec(conn, null,
                "UPDATE skill_texts SET description='оживление изображения' WHERE skill_id=@id AND lang='ru'",
                ("@id", id));
        }
        Assert.DoesNotContain("i2v", _f.RefData.Skills("ru").Single(s => s.Name == "video-animate").Description);

        _f.RefData.Seed();

        Assert.Contains("i2v", _f.RefData.Skills("ru").Single(s => s.Name == "video-animate").Description);
        Assert.Equal(id, SkillId("video-animate"));   // строка та же — ссылки задач целы
    }

    // ── 3. inputs/outputs участвуют в подборе ─────────────────────────────────────

    [Theory]
    [InlineData("video-generate", SkillIo.Text, SkillIo.Video)]
    [InlineData("video-animate", SkillIo.Image, SkillIo.Video)]
    [InlineData("video-lipsync", SkillIo.Audio, SkillIo.Video)]
    [InlineData("image-edit", SkillIo.Image, SkillIo.Image)]
    [InlineData("3d-image", SkillIo.Image, SkillIo.Model)]
    public void Skill_Mode_Is_Known(string code, string input, string output)
    {
        var mode = SkillIo.For(code);
        Assert.NotNull(mode);
        Assert.Equal(input, mode!.Value.In);
        Assert.Equal(output, mode.Value.Out);
    }

    /// <summary>Немедийные навыки режимом не описаны — их подбор проверять не должен.</summary>
    [Theory]
    [InlineData("code-write")]
    [InlineData("code-write-cs")]
    [InlineData("text-docs")]
    [InlineData("analyze-plan")]
    public void Non_Media_Skills_Have_No_Mode(string code) => Assert.Null(SkillIo.For(code));

    /// <summary>Уточняющий сегмент снимается так же, как в автоподборе.</summary>
    [Fact]
    public void Mode_Falls_Back_To_Base_Skill_Code()
    {
        Assert.Equal(SkillIo.Image, SkillIo.For("image-generate-anime")!.Value.Out);
        Assert.Null(SkillIo.For("unknown-skill"));
    }

    /// <summary>Совместимость меряется ТИПОМ формата: модель, принимающая только image/png,
    /// годится там, где нужно изображение; «не объявлено» — не повод отбраковывать.</summary>
    [Fact]
    public void Format_Matching_Is_By_Carrier_Type()
    {
        Assert.True(SkillIo.Fits("video-animate", ["image/png"], ["video/mp4"]));
        Assert.True(SkillIo.Fits("video-animate", [], []));                 // декларация пуста
        Assert.True(SkillIo.Fits("video-animate", ["*/*"], ["video/*"]));
        Assert.False(SkillIo.Fits("video-animate", ["text/plain"], ["video/*"]));
        Assert.False(SkillIo.Fits("video-animate", ["image/*"], ["text/markdown"]));
        Assert.True(SkillIo.Fits("code-write", ["text/plain"], ["text/markdown"]));
    }

    /// <summary>t2v-модель на задачу t2v подбирается — прежнее поведение не изменилось.</summary>
    [Fact]
    public void T2V_Model_Still_Fits_A_T2V_Task()
    {
        var ai = CreateAi("t2v", T2VScope);
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id, [SkillId("video-generate")], PickMode.AiFirst);

        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(76, picked.Candidates.Single().Quality);
    }

    /// <summary>
    /// Ради этого всё и затевалось: у t2v-модели навык video-animate (i2v) объявлен, но входа
    /// «изображение» у неё нет — на i2v-задачу она не предлагается вовсе.
    /// </summary>
    [Fact]
    public void T2V_Model_Is_Not_Offered_For_An_I2V_Task()
    {
        var ai = CreateAi("t2vonly", T2VScope);
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id, [SkillId("video-animate")], PickMode.AiOnly);

        Assert.Null(picked.ExecutorId);
        Assert.Equal(0, picked.Candidates.Single().Quality);
    }

    /// <summary>Та же модель, но с входом image/* — подбирается со своей оценкой.</summary>
    [Fact]
    public void I2V_Model_Fits_An_I2V_Task()
    {
        var ai = CreateAi("i2v", I2VScope);
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id, [SkillId("video-animate")], PickMode.AiOnly);

        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(72, picked.Candidates.Single().Quality);
    }

    /// <summary>Из двух моделей на i2v-задачу берётся та, что принимает изображение,
    /// даже если у неё оценка навыка ниже.</summary>
    [Fact]
    public void Between_Two_Models_The_One_With_A_Matching_Input_Wins()
    {
        var text2video = CreateAi("t2vrival", T2VScope);
        var image2video = CreateAi("i2vrival", """
            {
              "inputs":  ["image/*"],
              "outputs": ["video/*"],
              "skills":  [ { "name": "video-animate", "score": 40 } ]
            }
            """);
        var picked = _f.Picker.Pick(null, CreateTeam(text2video, image2video).Id,
            [SkillId("video-animate")], PickMode.AiOnly);

        Assert.Equal(image2video.Id, picked.ExecutorId);
    }

    /// <summary>Декларация без inputs/outputs (так заполнено у большинства записей) под
    /// правило не попадает — подбор работает как раньше.</summary>
    [Fact]
    public void Declaration_Without_Formats_Is_Not_Rejected()
    {
        var ai = CreateAi("nofmt", """{ "skills": [ { "name": "video-animate", "score": 72 } ] }""");
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id, [SkillId("video-animate")], PickMode.AiOnly);

        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(72, picked.Candidates.Single().Quality);
    }

    /// <summary>У ЧЕЛОВЕКА форматы декларации — условность (он и картинку принесёт, и снимет
    /// видео), поэтому правило на него не распространяется.</summary>
    [Fact]
    public void Human_Is_Not_Rejected_By_Formats()
    {
        var human = CreateExecutor("человек", """
            {
              "inputs":  ["text/markdown"],
              "outputs": ["text/markdown"],
              "skills":  [ { "name": "video-animate", "score": 55 } ]
            }
            """, ExecutorKind.Human);
        var picked = _f.Picker.Pick(null, CreateTeam(human).Id, [SkillId("video-animate")], PickMode.HumanFirst);

        Assert.Equal(human.Id, picked.ExecutorId);
        Assert.Equal(55, picked.Candidates.Single().Quality);
    }

    /// <summary>Немедийные навыки правило не трогает: текстовая модель с обычной декларацией
    /// по-прежнему подбирается на code-write.</summary>
    [Fact]
    public void Text_Model_Is_Not_Affected()
    {
        var ai = CreateAi("textmodel", """
            {
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown"],
              "skills":  [ { "name": "code-write", "score": 90 } ]
            }
            """);
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id, [SkillId("code-write-cs")], PickMode.AiOnly);

        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(90, picked.Candidates.Single().Quality);
    }

    /// <summary>Смешанная задача: несовместимый по форматам навык даёт 0 в среднее,
    /// но кандидата целиком не выбрасывает — половину работы он делает.</summary>
    [Fact]
    public void Incompatible_Skill_Only_Lowers_The_Average()
    {
        var ai = CreateAi("mixed", """
            {
              "inputs":  ["text/plain"],
              "outputs": ["video/*"],
              "skills":  [ { "name": "video-generate", "score": 80 }, { "name": "video-animate", "score": 80 } ]
            }
            """);
        var picked = _f.Picker.Pick(null, CreateTeam(ai).Id,
            [SkillId("video-generate"), SkillId("video-animate")], PickMode.AiOnly);

        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(40, picked.Candidates.Single().Quality);   // (80 + 0) / 2
    }

    /// <summary>
    /// СТОРОЖ ВТОРОЙ КОПИИ ОПИСАНИЙ. Описание встроенного навыка и формата лежит в системе
    /// дважды: в файле сида (уезжает В БАЗУ организации) и в словаре интерфейса ключом
    /// <c>skill.&lt;код&gt;</c> / <c>ioformat.&lt;код&gt;</c> — именно его показывает UI
    /// (SettingsView, ScopeEditorDialog, TaskDialog, ExperienceTab: у встроенной записи
    /// описание из БД не показывается вовсе). Правка одного сида доезжает до API, но НЕ до
    /// экрана — на этом первый заход T-257 и попался (браузер показывал прежние описания).
    /// Поэтому: у каждого кода сида обязан быть ключ словаря с ТЕМ ЖЕ текстом, на обоих языках.
    /// </summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Ui_Dictionary_Repeats_Seed_Descriptions(string lang)
    {
        var i18n = Path.Combine(AppContext.BaseDirectory, "i18n");
        using var seed = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(i18n, $"RefDataService_{lang}.json")));
        using var dict = JsonDocument.Parse(File.ReadAllText(Path.Combine(i18n, $"{lang}.json")));

        foreach (var (section, prefix) in new[] { ("skills", "skill."), ("ioFormats", "ioformat.") })
        {
            foreach (var item in seed.RootElement.GetProperty(section).EnumerateArray())
            {
                var code = item.GetProperty("name").GetString()!;
                var text = item.GetProperty("description").GetString()!;
                Assert.True(dict.RootElement.TryGetProperty(prefix + code, out var shown),
                    $"в i18n/{lang}.json нет ключа {prefix}{code} — интерфейс покажет старое описание");
                Assert.Equal(text, shown.GetString());
            }
        }
    }

    /// <summary>Языковые варианты code-навыков в словаре интерфейса тоже обязаны быть:
    /// их коды собираются сидом (code-write + 12 языков), а не лежат в файле готовыми.</summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Ui_Dictionary_Covers_Every_Seeded_Skill(string lang)
    {
        var i18n = Path.Combine(AppContext.BaseDirectory, "i18n");
        using var dict = JsonDocument.Parse(File.ReadAllText(Path.Combine(i18n, $"{lang}.json")));
        foreach (var skill in _f.RefData.Skills(lang))
        {
            Assert.True(dict.RootElement.TryGetProperty("skill." + skill.Name, out _),
                $"в i18n/{lang}.json нет ключа skill.{skill.Name}");
        }
    }

    /// <summary>Записи справочника моделей дистрибутива остаются согласованными со справочником:
    /// коды навыков и форматов из деклараций обязаны в нём быть (сторож T-214 — здесь на
    /// пополненном наборе), а объявленные режимы — сходиться с inputs/outputs.</summary>
    [Fact]
    public void Seeded_Model_Declarations_Stay_Consistent()
    {
        _f.Models.Seed();
        var skills = _f.RefData.Skills("en").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var formats = _f.RefData.IoFormats("en").Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var model in _f.Models.List().Where(m => m.CapabilitiesPath.Length > 0))
        {
            var scope = JsonDocument.Parse(_f.Files.ReadText(model.CapabilitiesPath));
            var root = scope.RootElement;
            foreach (var skill in root.GetProperty("skills").EnumerateArray())
            {
                Assert.Contains(skill.GetProperty("name").GetString()!, skills);
            }
            foreach (var key in new[] { "inputs", "outputs" })
            {
                foreach (var format in root.GetProperty(key).EnumerateArray())
                {
                    Assert.Contains(format.GetString()!, formats);
                }
            }
        }
    }
}
