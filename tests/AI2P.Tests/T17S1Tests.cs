using System.Net.Http;
using AI2P.Server;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-17-S1. ЧТЕНИЕ ДОКУМЕНТАЦИИ.
///
/// Заказчик: «в левом тулбаре над настройкой добавить кнопку (иконку) документация; при
/// вызове открывается просмотр документации начиная с readme.md текущего языка
/// (doc/&lt;язык&gt;/README.md); окно чтения не модальное, а стандартная закладка; все
/// переходы по внутренним гиперссылкам работают внутри одной закладки; вверху кнопка
/// (иконка) назад, активна после первого перехода по внутренней ссылке; вызов документации
/// добавить и в низ навигатора (эксплорера)».
///
/// Здесь проверяется всё, что можно проверить без экрана:
/// <list type="number">
/// <item>чтение страницы по пути (DocStore.Page): первая страница, подкаталоги, языковой
/// откат, попытки уйти из каталога документации;</item>
/// <item>внутренние ссылки (DocLinks): что считается переходом внутри документации,
/// куда он ведёт и как помечается готовый HTML;</item>
/// <item>история переходов и кнопка «назад» (UiState);</item>
/// <item>контракт разметки: кнопка тулбара над настройками, строка эксплорера, закладка.</item>
/// </list>
/// Сам вид и живые переходы кликом — браузером (test/t17s1/ui17s1.py).
/// </summary>
public sealed class T17S1Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t17s1-" + Guid.NewGuid().ToString("N"));

    public T17S1Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private DocStore Store(string lang = "ru") => new(() => _dir, () => lang);

    private void Write(string lang, string relPath, string text)
    {
        var path = Path.Combine(_dir, lang, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // ---------- 1. чтение страницы по пути ----------

    [Fact]
    public void Reading_Starts_With_The_Readme_Of_The_Interface_Language()
    {
        Write("ru", "README.md", "# Документация");
        Write("en", "README.md", "# Documentation");

        var page = Store().Page("");

        Assert.True(page.Found);
        Assert.Equal("ru", page.Language);
        Assert.Equal("README.md", page.RelPath);
        Assert.Contains("Документация", page.Text);
    }

    [Fact]
    public void The_Interface_Language_Wins_Over_The_Fallback()
    {
        Write("ru", "README.md", "# Документация");
        Write("en", "README.md", "# Documentation");

        var page = Store("en").Page(null);

        Assert.Equal("en", page.Language);
        Assert.Contains("Documentation", page.Text);
    }

    [Fact]
    public void A_Page_Missing_In_The_Interface_Language_Is_Shown_In_Another_One()
    {
        // ссылка на ещё не переведённую страницу обязана приводить к оригиналу, а не в пустоту
        Write("ru", "models/README.md", "# Модели");

        var page = Store("en").Page("models/README.md");

        Assert.True(page.Found);
        Assert.Equal("ru", page.Language);
        Assert.Equal("models/README.md", page.RelPath);
    }

    [Fact]
    public void A_Directory_Opens_Its_Readme()
    {
        // ссылка вида [Модели](models) — это каталог, показывать надо его первую страницу
        Write("ru", "models/README.md", "# Модели");

        var page = Store().Page("models");

        Assert.True(page.Found);
        Assert.Equal("models/README.md", page.RelPath);
    }

    [Fact]
    public void Readme_Is_Found_Whatever_The_Case_In_The_Link()
    {
        // на Linux регистр в имени файла значит всё, а в ссылке его пишут как придётся
        Write("ru", "README.md", "# Документация");

        Assert.True(Store().Page("readme.md").Found);
        Assert.Contains("README.md", DocStore.PageCandidates("readme.md"));
        Assert.Contains("models/README.md", DocStore.PageCandidates("models/readme.md"));
    }

    [Fact]
    public void The_Name_On_Disk_Wins_Over_The_One_Written_In_The_Link()
    {
        // ссылки пишут люди, а на Linux файловая система регистрозависима (T-18-S1):
        // имя ищется без учёта регистра и приводится к настоящему — от него дальше
        // считаются относительные ссылки самой страницы
        Write("ru", "models/README.md", "# Модели");

        var page = Store().Page("MODELS/readme.md");

        Assert.True(page.Found);
        Assert.Equal("models/README.md", page.RelPath);
    }

    [Fact]
    public void A_Page_Without_Extension_Is_Looked_For_As_A_File_Too()
    {
        Write("ru", "install.md", "# Установка");

        var page = Store().Page("install");

        Assert.True(page.Found);
        Assert.Equal("install.md", page.RelPath);
    }

    [Fact]
    public void A_Missing_Page_Explains_Where_It_Was_Looked_For()
    {
        // пустой экран без объяснения бесполезен: в подсказке — полный путь
        var page = Store().Page("no/such/page.md");

        Assert.False(page.Found);
        Assert.Equal("no/such/page.md", page.RelPath);
        Assert.Contains("page.md", page.Hint);
        Assert.Equal("", page.Text);
    }

    [Fact]
    public void No_Documentation_Directory_Is_Explained_Too()
    {
        var store = new DocStore(() => Path.Combine(_dir, "нет-такого"), () => "ru");

        var page = store.Page("");

        Assert.False(page.Found);
        Assert.NotEqual("", page.Hint);
    }

    // ---------- 2. путь наружу не выходит (ТЗ гл. 12) ----------

    [Fact]
    public void The_Path_Cannot_Leave_The_Documentation_Directory()
    {
        // путь приходит из ссылки документа и из запроса — собирает его сервер
        Assert.Equal("etc/passwd.md", DocStore.NormalizePath("../../etc/passwd.md"));
        Assert.Equal("etc/passwd.md", DocStore.NormalizePath("..\\..\\etc\\passwd.md"));
        Assert.DoesNotContain("..", DocStore.NormalizePath("a/../../b.md"));
        // абсолютный путь другого диска перестаёт быть абсолютным
        Assert.DoesNotContain(":", DocStore.NormalizePath("C:/Windows/win.ini"));
        Assert.Equal("", DocStore.NormalizePath("   "));
    }

    [Fact]
    public void A_Path_Outside_Is_Simply_Not_Found()
    {
        File.WriteAllText(Path.Combine(_dir, "secret.md"), "секрет");   // рядом с языками
        Write("ru", "README.md", "# Документация");

        var page = Store().Page("../secret.md");

        Assert.False(page.Found);
        Assert.DoesNotContain("секрет", page.Text);
    }

    [Fact]
    public void The_Anchor_And_The_Query_Are_Not_Part_Of_The_Path()
    {
        // «#раздел» адресует место ВНУТРИ страницы, а не другую страницу
        Assert.Equal("README.md", DocStore.NormalizePath("README.md#как-добавить"));
        Assert.Equal("models/README.md", DocStore.NormalizePath("models/README.md?x=1"));
    }

    // ---------- 3. внутренние ссылки ----------

    [Fact]
    public void A_Relative_Link_To_A_Page_Is_An_Internal_Transition()
    {
        Assert.Equal("models/README.md", DocLinks.Resolve("README.md", "models/README.md"));
        Assert.Equal("models", DocLinks.Resolve("README.md", "models"));
        // относительная ссылка считается от КАТАЛОГА показанной страницы
        Assert.Equal("models/Claude-Opus-5.0.md",
            DocLinks.Resolve("models/README.md", "Claude-Opus-5.0.md"));
        Assert.Equal("README.md", DocLinks.Resolve("models/README.md", "../README.md"));
        Assert.Equal("import/trello.md", DocLinks.Resolve("models/README.md", "../import/trello.md"));
        // якорь внутри страницы отбрасывается: страница та же
        Assert.Equal("models/README.md", DocLinks.Resolve("README.md", "models/README.md#ключ"));
    }

    [Fact]
    public void An_External_Link_Stays_External()
    {
        // подменять чужую ссылку своим переходом нельзя — она откроется новой вкладкой
        Assert.Null(DocLinks.Resolve("README.md", "https://console.anthropic.com/"));
        Assert.Null(DocLinks.Resolve("README.md", "http://localhost:5480/ai2p/"));
        Assert.Null(DocLinks.Resolve("README.md", "mailto:mike@example.com"));
        Assert.Null(DocLinks.Resolve("README.md", "javascript:alert(1)"));
        Assert.Null(DocLinks.Resolve("README.md", "/ai2p/mmfgrp/"));
        Assert.Null(DocLinks.Resolve("README.md", "#раздел-страницы"));
        // файл другого рода страницей документации не является
        Assert.Null(DocLinks.Resolve("README.md", "images/screen.png"));
    }

    [Fact]
    public void Internal_Links_Are_Marked_In_The_Rendered_Html()
    {
        var html = Md.ToHtml("[Модели](models/README.md) и [консоль](https://example.com/key)",
            ownBaseUrl: "");

        var marked = DocLinks.Mark(html, "README.md");

        // внутренняя ссылка обезврежена и помечена путём следующей страницы
        Assert.Contains("data-doc-link=\"models/README.md\"", marked);
        Assert.DoesNotContain("href=\"models/README.md\"", marked);
        // внешняя осталась как была — новой вкладкой
        Assert.Contains("https://example.com/key", marked);
        Assert.Contains("target=\"_blank\"", marked);
        Assert.DoesNotContain("data-doc-link=\"https", marked);
    }

    // ---------- 4. история переходов и кнопка «назад» ----------

    /// <summary>UiState без сервера: пока Initialized = false, лейаут никуда не сохраняется —
    /// ни API, ни личность вошедшего этим проверкам не нужны (приём T-10-S1).</summary>
    private static UiState NewState() => new(new ApiClient(new HttpClient()), null!);

    [Fact]
    public void Back_Is_Off_Until_The_First_Internal_Transition()
    {
        var state = NewState();

        Assert.False(state.DocCanGoBack);
        Assert.Null(state.DocBack());

        state.DocShown("README.md");         // первая страница показана — переходов ещё не было
        Assert.False(state.DocCanGoBack);

        state.DocOpen("models/README.md");   // первый переход по внутренней ссылке
        Assert.True(state.DocCanGoBack);
    }

    [Fact]
    public void Back_Walks_The_Path_That_Was_Read()
    {
        var state = NewState();
        state.DocShown("README.md");
        state.DocOpen("models/README.md");
        state.DocOpen("models/Claude-Opus-5.0.md");

        Assert.Equal("models/README.md", state.DocBack());
        Assert.Equal("models/README.md", state.DocPath);
        Assert.Equal("README.md", state.DocBack());
        Assert.False(state.DocCanGoBack);
        Assert.Null(state.DocBack());
    }

    [Fact]
    public void Reopening_The_Same_Page_Does_Not_Pile_Up_History()
    {
        var state = NewState();
        state.DocShown("README.md");
        state.DocOpen("README.md");

        Assert.False(state.DocCanGoBack);
    }

    // ---------- 5. контракт разметки ----------

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

    private static string UiFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppRoot(), "src", "AI2P.UI", .. parts]));

    [Fact]
    public void The_Toolbar_Button_Stands_Above_The_Settings_One()
    {
        var home = UiFile("Pages", "Home.razor");
        var docs = home.IndexOf("Kind=\"@UiState.ViewDocs\"", StringComparison.Ordinal);
        var settings = home.IndexOf("Kind=\"@UiState.ViewSettings\"", StringComparison.Ordinal);

        Assert.True(docs > 0, "в экшен-баре нет кнопки документации");
        Assert.True(settings > docs, "кнопка документации обязана стоять НАД настройками");
        // закладка чтения рисуется в рабочей области, а не окном
        Assert.Contains("case UiState.ViewDocs: <DocsView /> break;", home);
    }

    [Fact]
    public void Documentation_Is_Read_Without_An_Organization()
    {
        // документация лежит файлами рядом с приложением; на сервере, ждущем решения по
        // заявке, она нужна как раз больше всего (T-148)
        Assert.Contains("UiState.ViewSettings or UiState.ViewDocs",
            UiFile("Components", "ActionBarButton.razor"));
        var permissions = File.ReadAllText(Path.Combine(AppRoot(), "src", "AI2P.Server",
            "Api", "ApiPermissions.cs"));
        Assert.Contains("\"/api/doc\"", permissions);
    }

    [Fact]
    public void The_Explorer_Has_Documentation_As_Its_Last_Row()
    {
        var explorer = UiFile("Components", "ExplorerPanel.razor");
        var docs = explorer.IndexOf("Key = KeyDocs", StringComparison.Ordinal);
        var teams = explorer.IndexOf("Add(rows, Group(KeyTeams", StringComparison.Ordinal);

        Assert.True(docs > 0, "в эксплорере нет строки документации");
        Assert.True(docs > teams, "строка документации обязана быть ПОСЛЕДНЕЙ");
    }

    [Fact]
    public void The_Reading_View_Is_A_Tab_With_A_Back_Button()
    {
        var view = UiFile("Components", "DocsView.razor");

        Assert.DoesNotContain("<MudDialog", view);          // не модальное окно, а закладка
        Assert.Contains("data-doc-back", view);
        Assert.Contains("Disabled=\"@(!State.DocCanGoBack)\"", view);
        Assert.Contains("ai2p.initDocLinks", view);         // переходы — внутри этой закладки
    }

    [Fact]
    public void The_Shipped_Documentation_Has_A_Start_Page_In_Every_Language()
    {
        // с неё начинается чтение — без неё кнопка приводит на подсказку «положите файл»
        var doc = Path.Combine(AppRoot(), "doc");
        // каталог без единого .md языком не считается (T-51-S0): рядом с языками лежит
        // doc/images/ — картинки, общие для всех языков
        foreach (var lang in Directory.GetDirectories(doc)
                     .Where(d => Directory.GetFiles(d, "*.md", SearchOption.AllDirectories).Length > 0))
        {
            Assert.True(File.Exists(Path.Combine(lang, DocStore.StartPage)),
                $"нет первой страницы документации: {Path.Combine(lang, DocStore.StartPage)}");
        }
    }
}
