using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-157 (версия 1.76): СПИСОК ШАБЛОНОВ В ФОРМЕ ДОБАВЛЕНИЯ ЗАДАЧИ В ПРОЕКТ.
///
/// Форма новой задачи показывала подряд ВСЕ шаблоны организации: свои проекту, общие
/// и чужие вперемешку, по алфавиту. Теперь список строит <see cref="TaskTemplates.ForProject"/>:
/// сначала шаблоны проекта формы, потом общие (без проекта), чужих нет вовсе.
///
/// «Проект формы» — проект карточки из закладки «Проекты» (<c>TasksView.FixedProjectId</c>),
/// а не текущий проект пользователя: в карточке проекта задача заводится в неё.
/// Проверка разметки — файлами, как в <see cref="T136Tests"/>; живая проверка списка
/// в браузере — <c>test/t157</c>.
/// </summary>
public sealed class T157Tests
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

    private static TaskItem Template(string title, string? projectId, string? parentId = null) =>
        new() { Id = title, Title = title, ProjectId = projectId, ParentId = parentId, IsTemplate = true };

    /// <summary>Набор из жизни: два шаблона своего проекта, два общих, два чужих.</summary>
    private static List<TaskItem> Mixed() =>
    [
        Template("Чужой релиз", "p2"),
        Template("Общий разбор", null),
        Template("Наш релиз", "p1"),
        Template("Апрельский чужой", "p2"),
        Template("Аудит общий", ""),
        Template("Багфикс наш", "p1"),
    ];

    // ---------- 1. чужих шаблонов в списке нет ----------

    [Fact]
    public void Templates_Of_Other_Projects_Are_Not_In_The_List()
    {
        var list = TaskTemplates.ForProject(Mixed(), "p1");

        Assert.DoesNotContain(list, t => t.ProjectId == "p2");
        Assert.Equal(4, list.Count);
    }

    // ---------- 2. сначала свои, потом общие ----------

    [Fact]
    public void Own_Templates_Come_First_Then_The_Common_Ones()
    {
        var titles = TaskTemplates.ForProject(Mixed(), "p1").Select(t => t.Title).ToList();

        // внутри групп — по заголовку, без учёта регистра
        Assert.Equal(["Багфикс наш", "Наш релиз", "Аудит общий", "Общий разбор"], titles);
    }

    /// <summary>Пустая строка вместо null — тот же «шаблон без проекта».</summary>
    [Fact]
    public void Empty_Project_Means_The_Same_As_None()
    {
        var list = TaskTemplates.ForProject([Template("Пустой", ""), Template("Свой", "p1")], "p1");

        Assert.Equal(["Свой", "Пустой"], list.Select(t => t.Title));
    }

    // ---------- 3. форма без проекта ----------

    /// <summary>Общий список задач: проекта у формы нет, скрывать нечего — видно всё,
    /// общие сверху.</summary>
    [Fact]
    public void Without_A_Project_Nothing_Is_Hidden_And_Common_Go_First()
    {
        var titles = TaskTemplates.ForProject(Mixed(), null).Select(t => t.Title).ToList();

        Assert.Equal(6, titles.Count);
        Assert.Equal(["Аудит общий", "Общий разбор"], titles.Take(2));
    }

    [Fact]
    public void Empty_Form_Project_Is_The_Same_As_None()
    {
        Assert.Equal(TaskTemplates.ForProject(Mixed(), null).Select(t => t.Title),
            TaskTemplates.ForProject(Mixed(), "  ").Select(t => t.Title));
    }

    // ---------- 4. только головы шаблонов ----------

    /// <summary>Иерархия копируется целиком от головы, поэтому узлы внутри шаблона
    /// в списке выбора не нужны.</summary>
    [Fact]
    public void Only_Template_Heads_Are_Listed()
    {
        var list = TaskTemplates.ForProject(
            [Template("Голова", "p1"), Template("Подшаг", "p1", parentId: "Голова")], "p1");

        Assert.Equal(["Голова"], list.Select(t => t.Title));
    }

    [Fact]
    public void No_Templates_At_All_Is_Not_An_Error()
    {
        Assert.Empty(TaskTemplates.ForProject(null, "p1"));
        Assert.Empty(TaskTemplates.ForProject([], null));
    }

    // ---------- 5. разметка: проект доезжает до диалога ----------

    /// <summary>Диалог берёт список у общего правила, а не строит свой порядок.</summary>
    [Fact]
    public void Dialog_Builds_The_List_By_The_Shared_Rule()
    {
        var dialog = Ui("Components", "NewTaskDialog.razor");

        Assert.Contains("[Parameter] public string? ProjectId", dialog, StringComparison.Ordinal);
        Assert.Contains("TaskTemplates.ForProject(", dialog, StringComparison.Ordinal);
    }

    /// <summary>Проект диалогу передаёт вкладка «задачи» карточки проекта — своим
    /// FixedProjectId, а не текущим проектом из UiState.</summary>
    [Fact]
    public void Tasks_View_Passes_Its_Own_Project_Not_The_Current_One()
    {
        var view = Ui("Components", "TasksView.razor");
        var call = view.IndexOf("ShowAsync<NewTaskDialog>", StringComparison.Ordinal);

        Assert.True(call > 0, "форма новой задачи открывается из TasksView");
        var head = view[Math.Max(0, call - 400)..call];
        Assert.Contains("d.ProjectId, FixedProjectId", head, StringComparison.Ordinal);
        Assert.DoesNotContain("d.ProjectId, State.CurrentProjectId", view, StringComparison.Ordinal);
    }
}
