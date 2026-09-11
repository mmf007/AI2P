using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-131 (подзадача T-118.4): ВЫВОД .md — ЗАМЕТНОЕ ВЫДЕЛЕНИЕ КОДА.
///
/// Жалоба заказчика: программный код и текст в одиночных апострофах в отрендеренном Markdown
/// не отличались от обычного текста — у inline-<c>code</c> стиля не было вовсе, а фон блока
/// кода совпадал с фоном карточки. Стили при этом были продублированы в трёх местах
/// (MainLayout, Home, MarkdownEditor) и расходились между собой.
///
/// Здесь проверяется то, что можно проверить без браузера: 1) правила
/// <c>.ai2p-md-preview</c> лежат ровно в ОДНОМ файле (MainLayout — общий лейаут всех
/// маршрутов), 2) inline-код и блок кода вообще доезжают до HTML тегами
/// <c>code</c>/<c>pre &gt; code</c> — иначе стилизовать нечего, 3) оформление не мешает
/// обратной конвертации HTML → Markdown в предпросмотре редактора (правка в предпросмотре
/// не должна портить текст).
/// </summary>
public sealed class MdCodeStyleTests
{
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

    private static string UiRoot() => Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI");

    // --- стили MD-превью не продублированы (T-131) ---

    [Theory]
    [InlineData("ai2p-md-preview code")]
    [InlineData("ai2p-md-preview pre")]
    [InlineData("ai2p-md-preview blockquote")]
    [InlineData("ai2p-md-preview table")]
    [InlineData("ai2p-md-preview img")]
    public void Md_Preview_Rule_Is_Declared_In_Exactly_One_File(string rule)
    {
        var files = Directory.GetFiles(UiRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(rule, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToList();
        Assert.True(files.Count == 1,
            $"правило «{rule}» должно быть объявлено ровно один раз, найдено в: {string.Join(", ", files)}");
        Assert.Equal("MainLayout.razor", files[0]);
    }

    [Fact]
    public void MainLayout_Styles_Inline_Code_And_Code_Block()
    {
        var css = File.ReadAllText(Path.Combine(UiRoot(), "MainLayout.razor"));
        // inline-код: своя гарнитура, фон, рамка и скругление — иначе он неотличим от текста
        Assert.Contains("monospace", css, StringComparison.Ordinal);
        Assert.Contains(".ai2p-md-preview code", css, StringComparison.Ordinal);
        Assert.Contains(".ai2p-md-preview pre code", css, StringComparison.Ordinal);
        // блок кода: левая цветная полоса и горизонтальная прокрутка длинных строк
        Assert.Contains("border-left: 4px solid var(--mud-palette-primary)", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", css, StringComparison.Ordinal);
        // цвета — только из палитры MudBlazor: тогда обе темы работают сами, без ветвлений
        var style = css[css.IndexOf("<style>", StringComparison.Ordinal)..
                        css.IndexOf("</style>", StringComparison.Ordinal)];
        Assert.DoesNotContain("rgb(", style, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba(", style, StringComparison.Ordinal);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(style, "#[0-9a-fA-F]{3,8}"),
            "цвет задан шестнадцатеричным литералом — нужен var(--mud-palette-*)");
    }

    // --- код доезжает до HTML тегами, которые стилизуются ---

    [Fact]
    public void Inline_Code_And_Fenced_Block_Render_As_Code_Tags()
    {
        var html = Md.ToHtml("Вызов `Md.ToHtml(x)` в тексте.\n\n```csharp\nvar a = 1;\n```\n");
        Assert.Contains("<code>Md.ToHtml(x)</code>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-csharp\">", html, StringComparison.Ordinal);
    }

    // --- оформление не ломает обратную конвертацию предпросмотра (HTML → Markdown) ---

    [Fact]
    public void Code_Survives_Preview_Roundtrip()
    {
        const string markdown = """
            Инлайн-код `Md.ToHtml(markdown)` и путь `src/AI2P.UI/MainLayout.razor`.

            ```csharp
            public static string ToHtml(string markdown) =>
                Markdown.ToHtml(markdown, Pipeline);
            ```

            Текст после блока.
            """;
        // предпросмотр редактора: Markdown → HTML (contenteditable) → Markdown
        Assert.Equal(markdown.Replace("\r\n", "\n"), MdHtml.ToMarkdown(Md.ToHtml(markdown)));
    }

    [Fact]
    public void Code_Block_Without_Language_Survives_Preview_Roundtrip()
    {
        const string markdown = """
            ```
            dotnet test --filter FullyQualifiedName~MdCodeStyleTests
            ```
            """;
        Assert.Equal(markdown.Replace("\r\n", "\n"), MdHtml.ToMarkdown(Md.ToHtml(markdown)));
    }
}
