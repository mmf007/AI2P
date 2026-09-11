using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-10-S1. СКРЫТИЕ ЛЕВОГО ТУЛБАРА.
///
/// Заказчик: «в верхнем тулбаре нужна кнопка (иконка) скрыть/показать левый тулбар,
/// в самой левой части верхнего тулбара; по умолчанию левый тулбар виден».
///
/// «Левый тулбар» — вертикальный экшен-бар (панель 2 по ТЗ гл. 11, .ai2p-actionbar):
/// столбец значков представлений слева от эксплорера. Кнопка стоит ПЕРЕД кнопкой
/// показа/скрытия эксплорера (todo23) — порядок кнопок повторяет порядок панелей
/// на экране.
///
/// Здесь — состояние (умолчание, переключение, память лейаута) и контракт разметки;
/// сам вид проверяется браузером (test/t10s1/ui10s1.py, настоящий Chrome по CDP).
/// </summary>
public sealed class T10S1Tests
{
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

    private static string AppRoot() => Path.Combine(RepoRoot(), "AI2P_app");

    private static string Home() =>
        File.ReadAllText(Path.Combine(AppRoot(), "src", "AI2P.UI", "Pages", "Home.razor"));

    private static string StateSource() =>
        File.ReadAllText(Path.Combine(AppRoot(), "src", "AI2P.UI", "Services", "UiState.cs"));

    /// <summary>UiState без сервера: пока Initialized = false, лейаут никуда не сохраняется,
    /// поэтому ни API, ни личность вошедшего для этих проверок не нужны.</summary>
    private static UiState NewState() => new(new ApiClient(new HttpClient()), null!);

    private static bool Restore(UiState state, string json) =>
        (bool)typeof(UiState)
            .GetMethod("RestoreLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(state, [json])!;

    // ---------- 1. состояние ----------

    [Fact]
    public void Left_Toolbar_Is_Visible_By_Default()
    {
        // главное требование задания: без всякой настройки тулбар на месте.
        // У эксплорера умолчание обратное (todo23) — эти две кнопки не одинаковые
        var state = NewState();
        Assert.True(state.ActionBarVisible);
        Assert.False(state.ExplorerVisible);
    }

    [Fact]
    public void Button_Hides_And_Shows_It_Back()
    {
        var state = NewState();
        state.ToggleActionBar();
        Assert.False(state.ActionBarVisible);
        state.ToggleActionBar();
        Assert.True(state.ActionBarVisible);
        // кнопка эксплорера — сама по себе: одна панель не прячет другую
        Assert.False(state.ExplorerVisible);
    }

    [Fact]
    public void Hidden_Toolbar_Is_Remembered_By_The_Layout()
    {
        var state = NewState();
        var restored = Restore(state,
            """{"split":"single","v":50,"h":50,"explorer":true,"actionbar":false,"frames":[{"active":0,"tabs":[{"kind":"tasks"}]}]}""");

        Assert.True(restored);
        Assert.False(state.ActionBarVisible);
        Assert.True(state.ExplorerVisible);
    }

    [Fact]
    public void Layout_Of_An_Older_Version_Keeps_The_Toolbar_Visible()
    {
        // лейаут, сохранённый до этой правки, поля «actionbar» не содержит вовсе —
        // и тулбар обязан остаться видимым, а не исчезнуть у всех разом
        var state = NewState();
        Restore(state,
            """{"split":"single","v":50,"h":50,"explorer":false,"frames":[{"active":0,"tabs":[{"kind":"tasks"}]}]}""");

        Assert.True(state.ActionBarVisible);
    }

    [Fact]
    public void Visibility_Goes_Into_The_Saved_Layout()
    {
        var source = StateSource();
        // сохраняется тем же местом, что и остальной лейаут (SaveLayout)
        Assert.Contains("ActionBar = ActionBarVisible,", source);
        Assert.Contains("[JsonPropertyName(\"actionbar\")] public bool ActionBar { get; set; } = true;",
            source);
        // переключение уходит в сохранение лейаута, а не остаётся в памяти страницы
        var toggle = source[source.IndexOf("public void ToggleActionBar()", StringComparison.Ordinal)..];
        Assert.Contains("LayoutChanged();", toggle[..toggle.IndexOf('}')]);
    }

    // ---------- 2. разметка ----------

    [Fact]
    public void Button_Is_The_Leftmost_One_In_The_Top_Toolbar()
    {
        var home = Home();
        var strip = home.IndexOf("data-strip-inner=\"menubar\"", StringComparison.Ordinal);
        Assert.True(strip > 0, "в полосе меню нет ленты data-strip-inner=\"menubar\"");

        var toggle = home.IndexOf("data-actionbar-toggle=\"1\"", strip, StringComparison.Ordinal);
        var explorer = home.IndexOf("State.ToggleExplorer()", strip, StringComparison.Ordinal);
        Assert.True(toggle > 0, "в полосе меню нет кнопки показа/скрытия левого тулбара");
        Assert.True(explorer > 0, "в полосе меню нет кнопки эксплорера");
        // «самая левая часть верхнего тулбара»: раньше кнопки эксплорера, то есть первой
        // в ленте. Стрелки листания (T-194) стоят вне ленты и видны только при переполнении
        Assert.True(toggle < explorer,
            "кнопка левого тулбара обязана стоять ЛЕВЕЕ кнопки эксплорера");
    }

    [Fact]
    public void Button_Toggles_The_State_And_Shows_It()
    {
        var home = Home();
        Assert.Contains("State.ToggleActionBar()", home);
        // нажатое состояние видно по цвету — как у соседней кнопки эксплорера
        Assert.Contains("Color=\"@(State.ActionBarVisible ? Color.Primary : Color.Default)\"", home);
        // и по значку: открытая панель / закрытая
        Assert.Contains("Icon=\"@(State.ActionBarVisible ? Icons.Material.Filled.MenuOpen : Icons.Material.Filled.Menu)\"",
            home);
        Assert.Contains("Text=\"@L[\"appbar.actionbar\"]\"", home);
    }

    [Fact]
    public void Action_Bar_Is_Not_Rendered_When_Hidden()
    {
        var home = Home();
        var condition = home.IndexOf("@if (State.ActionBarVisible)", StringComparison.Ordinal);
        Assert.True(condition > 0, "экшен-бар рисуется без условия — скрыть его нечем");

        var bar = home.IndexOf("<div class=\"ai2p-actionbar\"", condition, StringComparison.Ordinal);
        Assert.True(bar > condition && bar - condition < 600,
            "условие показа стоит не перед экшен-баром");
        // скрытый тулбар пропадает из разметки целиком: место в flex-разметке
        // достаётся рабочей области, а не остаётся пустой полосой
        Assert.DoesNotContain("ai2p-actionbar\" style=\"display", home);
    }

    // ---------- 3. словари ----------

    [Fact]
    public void Hint_Is_In_Both_Dictionaries()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(AppRoot(), "i18n", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty("appbar.actionbar", out var value),
                $"{lang}.json: нет ключа appbar.actionbar");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()),
                $"{lang}.json: пустой текст у appbar.actionbar");
        }
    }
}
