using System.Text.Json;
using System.Text.RegularExpressions;
using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-238: СПРАВОЧНИК ИИ-МОДЕЛЕЙ — ТРЕТЬЕ ПРЕДСТАВЛЕНИЕ И ПЕРЕНОС НАВЫКОВ.
///
/// 1. Представлений стало три: «таблица» (T-217), «краткий» — та же таблица БЕЗ колонки
///    навыков — и «навыки» (T-217).
/// 2. Переключаются они так же, как в списке задач: «Вид: выпадающий список»
///    (<c>MudSelect</c> с подписью <c>common.view</c>), а не полосой-переключателем.
/// 3. Колонка навыков переносится ТОЛЬКО после запятой: на телефоне она сжимается, и
///    браузер ломал строку внутри навыка — на строке оставалось одно слово. Каждый навык
///    рисуется своим неразрывным куском (<c>.ai2p-skill-chunk</c>), между кусками —
///    обычный пробел.
///
/// Разбиение на куски — чистая логика <see cref="ModelSkillGroups.SkillsParts"/> и
/// проверяется здесь по-настоящему; разметка — по файлу (как в <see cref="T217Tests"/>),
/// внешний вид на узком экране — живой проверкой <c>test/t238</c>.
/// </summary>
public sealed class T238Tests
{
    // ---------- строка навыков: перенос только после запятой ----------

    private static AiModel Model(params (string Skill, int Score)[] skills) => new()
    {
        Id = "id-1",
        Name = "M",
        Skills = skills.Select(s => new ModelSkill { Name = s.Skill, Score = s.Score }).ToList(),
    };

    /// <summary>Навыки идут КУСКАМИ «код - оценка,»: запятая остаётся у своего навыка,
    /// у последнего её нет.</summary>
    [Fact]
    public void Skills_Are_Split_Into_Chunks()
    {
        var parts = ModelSkillGroups.SkillsParts(Model(("code-write", 92), ("text-write", 80)));

        Assert.Equal(new[] { "code-write - 92,", "text-write - 80" }, parts);
    }

    /// <summary>Кусок — это ровно ОДИН навык: внутри него запятых нет, кусков столько же,
    /// сколько навыков. Значит, единственное место переноса — пробел МЕЖДУ кусками.</summary>
    [Fact]
    public void A_Chunk_Is_Exactly_One_Skill()
    {
        var parts = ModelSkillGroups.SkillsParts(Model(
            ("analyze-data", 90), ("code-write-cs", 95), ("text-write", 80), ("image-generate", 70)));

        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.DoesNotContain(",", p.TrimEnd(',')));
        Assert.All(parts.Take(3), p => Assert.EndsWith(",", p));
        Assert.DoesNotContain(",", parts[^1]);
    }

    /// <summary>Модель без навыков — пустая ячейка, а не «- 0».</summary>
    [Fact]
    public void A_Model_Without_Skills_Gives_No_Chunks()
    {
        Assert.Empty(ModelSkillGroups.SkillsParts(Model()));
    }

    /// <summary>Кусок рисуется НЕРАЗРЫВНЫМ: класс есть в разметке ячейки и описан стилем
    /// <c>white-space: nowrap</c>. Дефис в коде навыка (<c>code-write</c>) — законное место
    /// переноса, поэтому без nowrap навык рвётся, сколько пробелов ни делай неразрывными
    /// (проверено живьём: 10 навыков из 13).</summary>
    [Fact]
    public void A_Chunk_Is_Rendered_Unbreakable()
    {
        Assert.Contains("<span class=\"ai2p-skill-chunk\">@part</span>", ModelsTab());

        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Pages", "Home.razor"));
        var rule = Regex.Match(css, @"\.ai2p-skill-chunk\s*\{([^}]*)\}");
        Assert.True(rule.Success, "нет стиля .ai2p-skill-chunk");
        Assert.Contains("white-space: nowrap", rule.Groups[1].Value);
    }

    /// <summary>Колонке нельзя разрешать разрыв ВНУТРИ слова: с <c>word-break: break-word</c>
    /// (было до T-238) неразрывный кусок ничего не даёт — браузер рвёт сам навык.</summary>
    [Fact]
    public void The_Skills_Cell_Does_Not_Break_Words()
    {
        var cell = Regex.Match(ModelsTab(), @"<td data-models-skills[\s\S]{0,120}?style=""([^""]*)""");

        Assert.True(cell.Success, "нет помеченной ячейки навыков");
        Assert.Contains("white-space: normal", cell.Groups[1].Value);
        Assert.DoesNotContain("word-break", cell.Groups[1].Value);
    }

    // ---------- три представления и переключатель ----------

    /// <summary>Переключатель — «Вид: выпадающий список», как у списка задач (T-238):
    /// полосы-переключателя (MudToggleGroup) на вкладке больше нет.</summary>
    [Fact]
    public void Views_Are_Switched_By_A_Select_Labelled_View()
    {
        var tab = ModelsTab();

        Assert.Contains("<MudSelect", tab);
        Assert.Contains("Label=\"@L[\"common.view\"]\"", tab);
        Assert.DoesNotContain("<MudToggleGroup", tab);   // в комментарии рядом упомянут — ищем тег
        Assert.DoesNotContain("<MudToggleItem", tab);

        // так же, как в списке задач: та же подпись
        var tasks = File.ReadAllText(Path.Combine(
            RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", "TasksView.razor"));
        Assert.Contains("Label=\"@L[\"common.view\"]\"", tasks);
    }

    /// <summary>Представлений три, и «краткий» стоит между «таблицей» и «навыками».</summary>
    [Fact]
    public void There_Are_Three_Views()
    {
        var tab = ModelsTab();
        var items = Regex.Matches(tab, @"<MudSelectItem T=""string"" Value=""@\(""(\w+)""\)""")
                         .Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(new[] { "table", "brief", "skills" }, items);
        Assert.Contains("L[\"models.view.brief\"]", tab);
    }

    /// <summary>«Краткий» — ТА ЖЕ таблица: своей копии разметки у него нет, отличается она
    /// ровно одной необязательной колонкой (заголовок и ячейка навыков).</summary>
    [Fact]
    public void Brief_Is_The_Table_Without_The_Skills_Column()
    {
        var tab = ModelsTab();

        // таблиц на вкладке две: «навыки» и общая «таблица»/«краткий»
        Assert.Equal(2, Regex.Matches(tab, "<MudSimpleTable").Count);
        // отличие ровно в двух местах: <th> навыков и <td> навыков
        Assert.Equal(2, Regex.Matches(tab, @"@if \(!ModelsBrief\)").Count);
        Assert.Contains("L[\"models.skills\"]", tab);
        Assert.Contains("ModelSkillGroups.SkillsParts(model)", tab);

        // признак представления считается по сохраняемому состоянию вкладки
        Assert.Contains("ModelsBrief => ModelsView == \"brief\"", Settings());
    }

    /// <summary>Прежние представления не тронуты: «таблица» — по умолчанию, «навыки»
    /// по-прежнему строятся раскладкой T-217, выбор переживает перезапуск.</summary>
    [Fact]
    public void The_Old_Views_Are_Intact()
    {
        var markup = Settings();

        Assert.Contains("public string View { get; set; } = \"table\";", markup);
        Assert.Contains("ModelSkillGroups.Build(State.Models)", markup);
        Assert.Contains("ComponentState<ModelsViewState>(\"modelsView\")", markup);
    }

    // ---------- словарь ----------

    /// <summary>Новый текст есть в обоих словарях (составы ru и en сходятся посчётно —
    /// это проверяет T192Tests, но своё сообщение стоит назвать явно).</summary>
    [Fact]
    public void The_New_Text_Is_In_Both_Dictionaries()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en" })
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(dir, lang + ".json")))!;
            Assert.True(dict.TryGetValue("models.view.brief", out var text) && text.Length > 0,
                        $"{lang}.json: нет текста models.view.brief");
        }
    }

    // ---------- вспомогательное ----------

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

    /// <summary>Содержимое вкладки «Модели».</summary>
    private static string ModelsTab()
    {
        var markup = Settings();
        var start = markup.IndexOf("<MudTabPanel Text=\"@L[\"settings.tab.models\"]\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "нет вкладки settings.tab.models");
        var end = markup.IndexOf("</MudTabPanel>", start, StringComparison.Ordinal);
        Assert.True(end > start, "вкладка settings.tab.models не закрыта");
        return markup[start..end];
    }
}
