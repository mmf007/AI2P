using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-163: ЗАКЛАДКИ ВЕРХНЕГО УРОВНЯ — одна строка с листанием.
///
/// Случай заказчика: когда закладок много, полоса закладок фрейма переносила их на вторую
/// и третью строку и отъедала высоту у рабочей области. Нужно как у подзакладок карточки
/// задачи (MudTabs): закладки стоят в ОДНУ строку, а не поместившиеся листаются кнопками
/// «влево»/«вправо».
///
/// Ширины меряет только браузер, поэтому здесь проверяется то, что проверяется без него —
/// правила стилей, разметка полосы, вызовы JS и подписи кнопок (файлами, как в
/// <see cref="T136Tests"/>). Само листание в настоящем браузере — <c>test/t163</c>.
/// </summary>
public sealed class T163Tests
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

    private static string Ui(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "AI2P_app", "src", "AI2P.UI", .. parts]));

    private static string Home() => Ui("Pages", "Home.razor");

    private static string Js() => Ui("wwwroot", "ai2p.js");

    /// <summary>Тело CSS-правила по его селектору (до первой закрывающей скобки).</summary>
    private static string Rule(string css, string selector)
    {
        var at = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(at > 0, $"нет правила {selector}");
        var body = css[(at + selector.Length)..];
        return body[..body.IndexOf('}')];
    }

    // --- 1. закладки в одну строку ---

    [Fact]
    public void Tabbar_Does_Not_Wrap_Tabs_To_Second_Line()
    {
        var bar = Rule(Home(), ".ai2p-tabbar");
        Assert.Contains("flex-wrap: nowrap", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("flex-wrap: wrap", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabs_Live_In_A_Scrollable_Strip()
    {
        var home = Home();
        var strip = Rule(home, ".ai2p-tabbar-strip");
        // лента листается по горизонтали и не растёт вниз
        Assert.Contains("overflow-x: auto", strip, StringComparison.Ordinal);
        Assert.Contains("overflow-y: hidden", strip, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: nowrap", strip, StringComparison.Ordinal);
        // закладка не сжимается: иначе вместо листания все влезут «в гармошку»
        Assert.Contains("flex: 0 0 auto", Rule(home, ".ai2p-tab"), StringComparison.Ordinal);
        // сами закладки лежат ВНУТРИ ленты
        var stripAt = home.IndexOf("class=\"ai2p-tabbar-strip\"", StringComparison.Ordinal);
        var tabAt = home.IndexOf("class=\"ai2p-tab @(index ==", StringComparison.Ordinal);
        Assert.True(stripAt > 0 && tabAt > stripAt, "закладки должны рисоваться внутри ленты");
    }

    // --- 2. кнопки листания ---

    [Fact]
    public void Tabbar_Has_Left_And_Right_Scroll_Buttons()
    {
        var home = Home();
        Assert.Contains("data-tab-scroll=\"prev\"", home, StringComparison.Ordinal);
        Assert.Contains("data-tab-scroll=\"next\"", home, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.ChevronLeft", home, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.ChevronRight", home, StringComparison.Ordinal);
        // «влево» листает в минус, «вправо» — в плюс
        Assert.Contains("ScrollTabsAsync(frameIndex, -1)", home, StringComparison.Ordinal);
        Assert.Contains("ScrollTabsAsync(frameIndex, 1)", home, StringComparison.Ordinal);
        // и то и другое — через JS: ширину полосы знает только браузер
        Assert.Contains("\"ai2p.scrollTabs\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Scroll_Buttons_Are_Hidden_Until_Tabs_Overflow()
    {
        var css = Home();
        Assert.Contains("display: none", Rule(css, ".ai2p-tabbar-arrow"), StringComparison.Ordinal);
        // видимыми их делает класс переполнения, который ставит JS
        Assert.Contains("display: inline-flex",
            Rule(css, ".ai2p-tabbar.ai2p-tabbar-scrolls > .ai2p-tabbar-arrow"), StringComparison.Ordinal);
        // на краю стрелка гаснет и не нажимается
        var off = Rule(css, ".ai2p-tabbar-arrow.ai2p-arrow-off");
        Assert.Contains("pointer-events: none", off, StringComparison.Ordinal);
        Assert.Contains("ai2p-tabbar-scrolls", Js(), StringComparison.Ordinal);
        Assert.Contains("ai2p-arrow-off", Js(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scroll_Arrows_Are_Direct_Children_Of_The_Tabbar()
    {
        // MudTooltip обернул бы кнопку своим контейнером и выбил её из полосы закладок,
        // поэтому подсказка — обычным атрибутом title
        var home = Home();
        var bar = home.IndexOf("class=\"ai2p-tabbar\" data-tabbar=", StringComparison.Ordinal);
        Assert.True(bar > 0, "у полосы закладок должен быть номер фрейма (data-tabbar)");
        var block = home[bar..(bar + 2600)];
        var arrows = Regex.Matches(block, "ai2p-tabbar-arrow");
        Assert.Equal(2, arrows.Count);
        Assert.DoesNotContain("<MudTooltip", block[..block.IndexOf("ai2p-tabbar-strip", StringComparison.Ordinal)],
            StringComparison.Ordinal);
        Assert.Contains("title=\"@L[\"tab.scrollPrev\"]\"", home, StringComparison.Ordinal);
        Assert.Contains("title=\"@L[\"tab.scrollNext\"]\"", home, StringComparison.Ordinal);
    }

    // --- 3. состояние листания пересчитывается после каждого рендера ---

    [Fact]
    public void Tab_Scroll_Is_Reinitialized_After_Every_Render()
    {
        var home = Home();
        var render = home.IndexOf("OnAfterRenderAsync", StringComparison.Ordinal);
        Assert.True(render > 0);
        var init = home.IndexOf("\"ai2p.initTabScroll\", \"ai2p-frames\"", StringComparison.Ordinal);
        Assert.True(init > render, "initTabScroll должен вызываться из OnAfterRenderAsync");
        // вызов не спрятан под firstRender: закладки добавляются и закрываются на ходу
        var body = home[render..init];
        Assert.DoesNotContain("if (firstRender)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Js_Exports_Tab_Scrolling_And_Keeps_Active_Tab_Visible()
    {
        var js = Js();
        Assert.Contains("initTabScroll: initTabScroll", js, StringComparison.Ordinal);
        Assert.Contains("scrollTabs: scrollTabs", js, StringComparison.Ordinal);
        // активная закладка не должна оставаться за краем полосы
        Assert.Contains("tabScrollToActive", js, StringComparison.Ordinal);
        Assert.Contains(".ai2p-tab.active", js, StringComparison.Ordinal);
        // переполнение перемеряется при изменении размеров фрейма и при прокрутке ленты
        Assert.Contains("ResizeObserver", js, StringComparison.Ordinal);
        Assert.Contains("'scroll'", js, StringComparison.Ordinal);
        // повторная инициализация слушателей исключена (как у остальных init* в этом файле).
        // Флаг переехал в общий движок листаемой полосы (T-194: на нём же тулбары), имя
        // стало общим — сама защита от повторной подписки на месте
        Assert.Contains("strip.dataset.ai2pStrip", js, StringComparison.Ordinal);
    }

    // --- 4. подписи кнопок на обоих языках ---

    [Fact]
    public void Scroll_Buttons_Have_Captions_In_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            foreach (var key in new[] { "tab.scrollPrev", "tab.scrollNext" })
            {
                Assert.True(map.TryGetValue(key, out var text) && text.Length > 0,
                    $"в {lang}.json нет подписи {key}");
            }
        }
    }
}
