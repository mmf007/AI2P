using System.Text.Json;
using System.Text.RegularExpressions;
using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-217 (версия 1.83): СПРАВОЧНИК МОДЕЛЕЙ — СВОЯ ВКЛАДКА И ДВА ПРЕДСТАВЛЕНИЯ.
///
/// 1. Справочник ИИ-моделей уехал из вкладки «Справочники» в собственную вкладку «Модели»
///    (после T-214 в дистрибутиве 31 запись, и в общем списке справочников он не читался).
/// 2. Представления два: «таблица» — как было, «навыки» — категоризация по навыкам
///    декларации возможностей; внутри категории модели идут по УБЫВАНИЮ оценки навыка
///    (первое поле сортировки — категория, второе — оценка).
/// 3. При открытии вкладки ВСЕ категории закрыты.
///
/// Раскладка по категориям — чистая логика <see cref="ModelSkillGroups"/> и проверяется
/// здесь по-настоящему; разметка — по файлу (как в <see cref="T209Tests"/>), внешний вид
/// в браузере — живой проверкой <c>test/t217</c>.
/// </summary>
public sealed class T217Tests
{
    // ---------- раскладка по навыкам ----------

    private static AiModel Model(string name, params (string Skill, int Score)[] skills) => new()
    {
        Id = "id-" + name,
        Name = name,
        Skills = skills.Select(s => new ModelSkill { Name = s.Skill, Score = s.Score }).ToList(),
    };

    [Fact]
    public void Inside_A_Category_Models_Go_By_Score_Descending()
    {
        var groups = ModelSkillGroups.Build([
            Model("Weak", ("code-write", 40)),
            Model("Best", ("code-write", 95)),
            Model("Middle", ("code-write", 70)),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal("code-write", group.Skill);
        Assert.Equal(["Best", "Middle", "Weak"], group.Models.Select(m => m.Model.Name));
        Assert.Equal([95, 70, 40], group.Models.Select(m => m.Score));
    }

    /// <summary>Первое поле сортировки — категория: коды навыков по алфавиту,
    /// «без навыков» — последней, чтобы не разрывать список.</summary>
    [Fact]
    public void Categories_Go_By_Skill_Code_And_No_Skill_Is_Last()
    {
        var groups = ModelSkillGroups.Build([
            Model("NoSkills"),
            Model("A", ("text-write", 50), ("audio-song", 10), ("code-write", 80)),
        ]);

        Assert.Equal(["audio-song", "code-write", "text-write", ModelSkillGroups.NoSkill],
                     groups.Select(g => g.Skill));
        Assert.Equal("NoSkills", Assert.Single(groups[^1].Models).Model.Name);
    }

    /// <summary>Модель с несколькими навыками попадает в КАЖДУЮ свою категорию — со своей
    /// оценкой по этому навыку, а не с общей.</summary>
    [Fact]
    public void A_Model_Lands_In_Every_Category_Of_Its_Declaration()
    {
        var groups = ModelSkillGroups.Build([
            Model("Coder", ("code-write", 90), ("text-write", 30)),
            Model("Writer", ("code-write", 20), ("text-write", 85)),
        ]);

        Assert.Equal(["Coder", "Writer"], groups[0].Models.Select(m => m.Model.Name));   // code-write
        Assert.Equal([90, 20], groups[0].Models.Select(m => m.Score));
        Assert.Equal(["Writer", "Coder"], groups[1].Models.Select(m => m.Model.Name));   // text-write
        Assert.Equal([85, 30], groups[1].Models.Select(m => m.Score));
    }

    /// <summary>При равной оценке порядок задаётся именем: список не «дрожит» между
    /// перерисовками и его можно проверять живой проверкой.</summary>
    [Fact]
    public void Equal_Scores_Are_Ordered_By_Name()
    {
        var groups = ModelSkillGroups.Build([
            Model("Zeta", ("code-write", 50)),
            Model("alpha", ("code-write", 50)),
            Model("Mu", ("code-write", 50)),
        ]);

        Assert.Equal(["alpha", "Mu", "Zeta"], Assert.Single(groups).Models.Select(m => m.Model.Name));
    }

    /// <summary>Повторный навык в декларации не задваивает строку категории: берётся
    /// большая оценка (декларацию правит человек, и дубль там возможен).</summary>
    [Fact]
    public void A_Duplicate_Skill_Does_Not_Duplicate_The_Row()
    {
        var groups = ModelSkillGroups.Build([Model("Dup", ("code-write", 30), ("code-write", 77))]);

        var row = Assert.Single(Assert.Single(groups).Models);
        Assert.Equal(77, row.Score);
    }

    /// <summary>Пустой код навыка в декларации — это «навыка нет», а не категория с пустым
    /// именем: иначе рядом с «без навыков» появилась бы вторая такая же категория.</summary>
    [Fact]
    public void An_Empty_Skill_Code_Counts_As_No_Skill()
    {
        var groups = ModelSkillGroups.Build([Model("Empty", ("", 42))]);

        Assert.Equal(ModelSkillGroups.NoSkill, Assert.Single(groups).Skill);
    }

    [Fact]
    public void An_Empty_Catalog_Gives_No_Categories() =>
        Assert.Empty(ModelSkillGroups.Build([]));

    // ---------- разметка вкладки ----------

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
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

    private static string Settings() => File.ReadAllText(Path.Combine(
        RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", "SettingsView.razor"));

    /// <summary>Содержимое вкладки настроек по ключу её заголовка.</summary>
    private static string TabPanel(string key)
    {
        var markup = Settings();
        var start = markup.IndexOf($"<MudTabPanel Text=\"@L[\"{key}\"]\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"нет вкладки {key}");
        var end = markup.IndexOf("</MudTabPanel>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"вкладка {key} не закрыта");
        return markup[start..end];
    }

    /// <summary>Справочник моделей — на своей вкладке, и его больше нет в «Справочниках».</summary>
    [Fact]
    public void The_Model_Catalog_Moved_To_Its_Own_Tab()
    {
        var models = TabPanel("settings.tab.models");
        Assert.Contains("L[\"models.title\"]", models);
        Assert.Contains("EditModelAsync(null)", models);

        var refs = TabPanel("settings.tab.refs");
        Assert.DoesNotContain("models.title", refs);
        Assert.DoesNotContain("EditModelAsync", refs);
        // сами справочники навыков, форматов, состояний и импортов остались на месте
        Assert.Contains("refs.skills.title", refs);
        Assert.Contains("refs.ioformats.title", refs);
        Assert.Contains("statuses.title", refs);
        Assert.Contains("imports.title", refs);
    }

    /// <summary>Представления переключаются так же, как в остальных списках: с T-238 это
    /// выпадающий список «Вид», а представлений стало три (проверяет T238Tests).</summary>
    [Fact]
    public void The_Tab_Has_Switchable_Views()
    {
        var models = TabPanel("settings.tab.models");

        Assert.Contains("<MudSelect", models);
        Assert.Contains("L[\"models.view.table\"]", models);
        Assert.Contains("L[\"models.view.skills\"]", models);
        Assert.Contains("ModelSkillGroups.Build(State.Models)", models);
        // выбранное представление переживает перезапуск (гл. 11)
        Assert.Contains("ComponentState<ModelsViewState>(\"modelsView\")", Settings());
    }

    /// <summary>При старте все категории закрыты: набор открытых пуст и НЕ восстанавливается
    /// из состояния компонента — иначе «при старте» зависело бы от прошлого входа.</summary>
    [Fact]
    public void All_Categories_Start_Collapsed()
    {
        var markup = Settings();

        Assert.Contains("_openSkills = new(StringComparer.Ordinal)", markup);
        Assert.DoesNotContain("ComponentState<HashSet<string>>", markup);
        // в сохраняемом состоянии вкладки лежит только выбранное представление
        var state = Regex.Match(markup, @"class ModelsViewState\s*\{(.*?)\}", RegexOptions.Singleline);
        Assert.True(state.Success, "нет класса состояния вкладки");
        Assert.DoesNotContain("Skill", state.Groups[1].Value);
    }

    // ---------- словари ----------

    /// <summary>Новые тексты есть в обоих словарях: составы ru и en сходятся посчётно
    /// (проверяет T192Tests), но своё сообщение стоит назвать явно.</summary>
    [Theory]
    [InlineData("settings.tab.models")]
    [InlineData("models.view.table")]
    [InlineData("models.view.skills")]
    [InlineData("models.score")]
    [InlineData("models.skills.none")]
    public void The_New_Texts_Are_In_Both_Dictionaries(string key)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en" })
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(dir, lang + ".json")))!;
            Assert.True(dict.TryGetValue(key, out var text) && text.Length > 0,
                        $"{lang}.json: нет текста {key}");
        }
    }
}
