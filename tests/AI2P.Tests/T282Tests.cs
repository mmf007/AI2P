using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-282: ВЫВОД .md — ГИПЕРССЫЛКА СИНЯЯ И ВИДНА В ОБЕИХ ТЕМАХ.
///
/// Жалоба заказчика: ссылка в показанном Markdown не отличалась от обычного текста.
/// Причина — MudBlazor красит любую ссылку цветом текста (<c>a { color:
/// var(--mud-palette-text-primary) }</c>), а своего правила у нас не было вовсе.
///
/// Здесь проверяется то, что можно проверить без браузера, и главное — НЕ ЛИТЕРАЛЫ, а
/// СВОЙСТВА ЦВЕТА: тест сам считает яркость и контраст по формуле WCAG и требует, чтобы
/// оба оттенка были синими и читались на поверхности своей темы (норма AA для обычного
/// текста — 4.5). Поэтому подбор другого синего тест не сломает, а незаметная порча
/// (например, «чуть притушить») — сломает. Как это выглядит на экране, доказывает живая
/// проверка test/t282/ui282.py (22/22 в обеих темах; на выпущенной 1.98 — 15/22).
/// </summary>
public sealed class T282Tests
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

    private static string MainLayout() => File.ReadAllText(Path.Combine(UiRoot(), "MainLayout.razor"));

    /// <summary>Поверхности тем MudBlazor по умолчанию (своей темы у приложения нет):
    /// светлая — белое поле карточки, тёмная — #373740.</summary>
    private const string LightSurface = "#ffffff";
    private const string DarkSurface = "#373740";

    /// <summary>Значение свойства-цвета из @code: «private string ИМЯ =&gt; State.DarkMode
    /// ? "#тёмная" : "#светлая";». Первым идёт цвет ТЁМНОЙ темы — так написано условие.</summary>
    private static (string Dark, string Light) Colors(string name)
    {
        var m = Regex.Match(MainLayout(),
            name + @"\s*=>\s*State\.DarkMode\s*\?\s*""(#[0-9a-fA-F]{6})""\s*:\s*""(#[0-9a-fA-F]{6})""");
        Assert.True(m.Success, $"в MainLayout не найдено свойство {name} с цветом на каждую тему");
        return (m.Groups[1].Value, m.Groups[2].Value);
    }

    private static (double R, double G, double B) Rgb(string hex) => (
        int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber),
        int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber),
        int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));

    /// <summary>Относительная яркость по WCAG 2.1.</summary>
    private static double Luma(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Part(double v)
        {
            v /= 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Part(r) + 0.7152 * Part(g) + 0.0722 * Part(b);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luma(a), lb = Luma(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // --- правило есть, и оно одно ---

    [Theory]
    [InlineData(".ai2p-md-preview a, .ai2p-md a")]
    [InlineData(".ai2p-md-preview a:hover, .ai2p-md a:hover")]
    public void The_Link_Rule_Lives_In_MainLayout_Only(string rule)
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

    /// <summary>Оба класса показа .md покрашены: .ai2p-md-preview (карточка, чат, доска,
    /// Inbox, документация, предпросмотр) и .ai2p-md (документ модели по кнопке «i»).</summary>
    [Fact]
    public void Both_Markdown_Containers_Get_The_Link_Colour()
    {
        var css = MainLayout();
        Assert.Contains(".ai2p-md-preview a, .ai2p-md a { color: @LinkColor; }", css,
            StringComparison.Ordinal);
        Assert.Contains("text-decoration: underline", css, StringComparison.Ordinal);
    }

    // --- цвет: синий и читаемый (считаем, а не сверяем литералы) ---

    [Fact]
    public void The_Link_Colour_Is_Blue_In_Both_Themes()
    {
        var (dark, light) = Colors("LinkColor");
        foreach (var (hex, theme) in new[] { (light, "светлая"), (dark, "тёмная") })
        {
            var (r, g, b) = Rgb(hex);
            Assert.True(b >= 120 && b - Math.Max(r, g) >= 40,
                $"{theme} тема: цвет {hex} не выглядит синим (r={r}, g={g}, b={b})");
        }
    }

    [Fact]
    public void The_Link_Is_Readable_On_Its_Own_Theme_Surface()
    {
        var (dark, light) = Colors("LinkColor");
        Assert.True(Contrast(light, LightSurface) >= 4.5,
            $"светлая тема: контраст {Contrast(light, LightSurface):F2} ниже нормы 4.5");
        Assert.True(Contrast(dark, DarkSurface) >= 4.5,
            $"тёмная тема: контраст {Contrast(dark, DarkSurface):F2} ниже нормы 4.5");
    }

    /// <summary>Ссылку надо ОТЛИЧАТЬ от обычного текста — ровно этого и не было (до T-282
    /// цвета СОВПАДАЛИ до байта). Меряется здесь не контраст, а РАССТОЯНИЕ ПО ЦВЕТУ: синий
    /// и серый бывают одной яркости, и контраст между ними близок к единице, хотя на экране
    /// они разные. Цвет текста у MudBlazor: #424242 на светлой теме и белый на тёмной.</summary>
    [Theory]
    [InlineData("light", "#424242")]
    [InlineData("dark", "#ffffff")]
    public void The_Link_Differs_From_Ordinary_Text(string theme, string textColour)
    {
        var (dark, light) = Colors("LinkColor");
        var link = theme == "dark" ? dark : light;
        var (lr, lg, lb) = Rgb(link);
        var (tr, tg, tb) = Rgb(textColour);
        var distance = Math.Abs(lr - tr) + Math.Abs(lg - tg) + Math.Abs(lb - tb);
        Assert.True(distance >= 60,
            $"{theme}: ссылка {link} почти сливается с текстом {textColour} (расстояние {distance})");
    }

    /// <summary>Цвет под указателем — тот же синий, только заметно другой яркости, и он
    /// обязан оставаться читаемым: иначе наведение «гасит» ссылку.</summary>
    [Fact]
    public void The_Hover_Colour_Is_A_Readable_Shade_Of_The_Same_Blue()
    {
        var (dark, light) = Colors("LinkColor");
        var (hoverDark, hoverLight) = Colors("LinkHoverColor");
        Assert.NotEqual(light, hoverLight);
        Assert.NotEqual(dark, hoverDark);
        // светлая тема — темнее обычного, тёмная — светлее: указатель виден в обеих
        Assert.True(Luma(hoverLight) < Luma(light), "на светлой теме наведение обязано быть темнее");
        Assert.True(Luma(hoverDark) > Luma(dark), "на тёмной теме наведение обязано быть светлее");
        Assert.True(Contrast(hoverLight, LightSurface) >= 4.5,
            $"светлая тема: наведённая ссылка {hoverLight} читается хуже нормы");
        Assert.True(Contrast(hoverDark, DarkSurface) >= 4.5,
            $"тёмная тема: наведённая ссылка {hoverDark} читается хуже нормы");
    }

    /// <summary>Переключатель темы помечен для живых проверок (раньше приметы не было
    /// вовсе, и проверка искала кнопку перебором всей шапки).</summary>
    [Fact]
    public void The_Theme_Switch_Is_Marked_For_Live_Checks()
    {
        var home = File.ReadAllText(Path.Combine(UiRoot(), "Pages", "Home.razor"));
        Assert.Contains("data-theme-toggle", home, StringComparison.Ordinal);
    }
}
