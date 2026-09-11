using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-212. ЭКСПЛОРЕР — ОДНО ИЕРАРХИЧЕСКОЕ ПРЕДСТАВЛЕНИЕ.
///
/// Было: левая панель показывала РАЗНОЕ в зависимости от нажатой кнопки экшен-бара —
/// пять несвязанных списков (задачи, шаблоны, проекты, команды, исполнители), и от проекта
/// к его задачам в панели было не перейти. Стало: панель одна и от выбранного представления
/// не зависит вовсе, а уровни такие:
///
///     Проекты · Проекты/задачи · Проекты/шаблоны · Проекты/команда
///     Исполнители
///     Шаблоны · Шаблоны/проекты
///     Команды
///
/// Все верхние уровни по умолчанию закрыты, содержимое ветки читается при первом раскрытии.
///
/// Здесь — контракт разметки и логики построения дерева; само поведение в браузере
/// проверяется живой проверкой (test/t212/ui212.py, настоящий Chrome по CDP).
/// </summary>
public sealed class T212Tests
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

    private static string UiRoot() => Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI");

    private static string Panel() =>
        File.ReadAllText(Path.Combine(UiRoot(), "Components", "ExplorerPanel.razor"));

    private static string Home() => File.ReadAllText(Path.Combine(UiRoot(), "Pages", "Home.razor"));

    private static string Dictionary(string lang) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json"));

    /// <summary>Тело метода панели — от его заголовка до следующего метода того же уровня.</summary>
    private static string Method(string name)
    {
        var text = Panel();
        var start = text.IndexOf(name, StringComparison.Ordinal);
        Assert.True(start >= 0, $"в эксплорере нет метода «{name}»");
        var open = text.IndexOf('{', start);
        Assert.True(open > 0, $"у метода «{name}» нет тела");
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return text[open..(i + 1)];
            }
        }
        throw new InvalidOperationException($"тело метода «{name}» не закрыто");
    }

    // ---------- 1. панель больше не переключается кнопками экшен-бара ----------

    [Fact]
    public void Explorer_Does_Not_Depend_On_The_Action_Bar_Section()
    {
        var panel = Panel();
        Assert.DoesNotContain("ExplorerSection", panel);
        Assert.DoesNotContain("@switch", panel);
        // и заголовок панели — общий, а не название секции
        Assert.Contains("L[\"explorer.title\"]", panel);
    }

    [Fact]
    public void Explorer_Is_Drawn_By_The_Single_Panel_Component()
    {
        var home = Home();
        Assert.Contains("<ExplorerPanel />", home);
        Assert.Single(Regex.Matches(home, "<ExplorerPanel"));
    }

    // ---------- 2. состав уровней ----------

    [Fact]
    public void Four_Top_Levels_In_The_Order_From_The_Task()
    {
        var rows = Method("private List<Node> Rows()");
        int At(string call)
        {
            var index = rows.IndexOf(call, StringComparison.Ordinal);
            Assert.True(index >= 0, $"в дереве нет верхнего уровня «{call}»");
            return index;
        }
        var projects = At("AddProjects(rows)");
        var executors = At("KeyExecutors");
        var templates = At("AddTemplates(rows)");
        var teams = At("KeyTeams");
        Assert.True(projects < executors && executors < templates && templates < teams,
            "порядок верхних уровней: Проекты, Исполнители, Шаблоны, Команды");
    }

    [Fact]
    public void Top_Levels_Are_Named_By_The_Dictionary()
    {
        var panel = Panel();
        foreach (var key in new[] { "menu.projects", "menu.executors", "menu.templates", "menu.teams" })
        {
            Assert.Contains("L[\"" + key + "\"]", panel);
        }
    }

    [Fact]
    public void Project_Expands_Into_Tasks_Templates_And_Team()
    {
        var branch = Method("private void AddProjects(List<Node> rows)");
        Assert.Contains("\"/tasks\"", branch);
        Assert.Contains("\"/templates\"", branch);
        Assert.Contains("\"/team\"", branch);
        Assert.Contains("L[\"projects.tab.tasks\"]", branch);
        Assert.Contains("L[\"projects.tab.templates\"]", branch);
        Assert.Contains("L[\"projects.tab.team\"]", branch);
        // задачи и шаблоны проекта читаются РАЗНЫМИ запросами (шаблоны — templatesOnly)
        Assert.Contains("\"tasks:\" + project.Id", branch);
        Assert.Contains("\"tpl:\" + project.Id", branch);
    }

    [Fact]
    public void Templates_Branch_Has_A_Projects_Level()
    {
        var branch = Method("private void AddTemplates(List<Node> rows)");
        Assert.Contains("KeyTemplates + \"/projects\"", branch);
        Assert.Contains("L[\"menu.projects\"]", branch);
        // и общие шаблоны — те, что не принадлежат проекту
        Assert.Contains("t.ProjectId is null", branch);
    }

    [Fact]
    public void Project_Team_Is_The_Default_Team_Of_The_Project()
    {
        var branch = Method("private void AddProjectTeam(");
        Assert.Contains("ReadDefaultTeam(project.SettingsJson)", branch);
        Assert.Contains("L[\"projects.team.none\"]", branch);
        Assert.Contains("team.Members", branch);
    }

    // ---------- 3. по умолчанию всё закрыто, ветка читается при раскрытии ----------

    [Fact]
    public void Everything_Is_Collapsed_Until_The_User_Opens_It()
    {
        var panel = Panel();
        Assert.Contains("private readonly HashSet<string> _expanded = [];", panel);
        // единственное место, где узел становится раскрытым, — переключатель строки:
        // никакой ветки «открыть по умолчанию» нет
        Assert.Single(Regex.Matches(panel, @"_expanded\.Add\("));
        Assert.Contains("_expanded.Add(node.Key);", Method("private async Task ToggleAsync(Node node)"));
    }

    [Fact]
    public void Branch_Content_Is_Read_On_The_First_Expand_Only()
    {
        var toggle = Method("private async Task ToggleAsync(Node node)");
        Assert.Contains("_lists.ContainsKey(cache)", toggle);
        Assert.Contains("await LoadAsync(cache)", toggle);
        // закрытие узла ничего не читает
        Assert.Contains("if (_expanded.Remove(node.Key))", toggle);
    }

    [Fact]
    public void Only_Opened_Branches_Are_Refreshed()
    {
        var reload = Method("private async Task ReloadAsync()");
        Assert.Contains("_lists.Keys", reload);
        Assert.Contains("await LoadAsync(cache)", reload);
    }

    [Fact]
    public void Loader_Tells_Tasks_Templates_And_Common_Templates_Apart()
    {
        var load = Method("private async Task<List<TaskItem>> LoadAsync(string cache)");
        Assert.Contains("CacheAllTemplates", load);
        Assert.Contains("templatesOnly: true", load);
        Assert.Contains("\"tasks:\"", load);
    }

    // ---------- 4. клики ----------

    [Fact]
    public void Clicks_Open_The_Same_Tabs_As_The_Lists()
    {
        var click = Method("private async Task OnClickAsync(Node node)");
        Assert.Contains("await ToggleAsync(node)", click);          // группа — разворачивается
        Assert.Contains("State.OpenProject(", click);               // проект — карточка проекта
        Assert.Contains("State.OpenTask(", click);                  // задача и шаблон — карточка задачи
        Assert.Contains("State.SetCurrentTeamAsync(", click);       // команда — становится текущей
    }

    [Fact]
    public void Task_Subtree_Follows_Parent_Id()
    {
        var tree = Method("private void AddTaskTree(");
        Assert.Contains("t.ParentId is not null && ids.Contains(t.ParentId)", tree);
        Assert.Contains("t.ParentId is null || !ids.Contains(t.ParentId)", tree);
        Assert.Contains("_expanded.Contains(key)", tree);
    }

    // ---------- 5. словари ----------

    [Fact]
    public void Every_Text_Of_The_Panel_Is_In_Both_Dictionaries()
    {
        var keys = Regex.Matches(Panel(), @"L\[""([a-zA-Z0-9_.]+)""\]")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(keys);
        var ru = Dictionary("ru");
        var en = Dictionary("en");
        foreach (var key in keys)
        {
            Assert.Contains("\"" + key + "\":", ru);
            Assert.Contains("\"" + key + "\":", en);
        }
    }
}
