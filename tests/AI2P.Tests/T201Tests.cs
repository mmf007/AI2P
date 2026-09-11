using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-201 (версия 1.82): ДОБАВЛЕНИЕ ПОДЗАДАЧИ ТОЙ ЖЕ ФОРМОЙ, ЧТО И ЗАДАЧИ В ПРОЕКТ.
///
/// Кнопка «+» вкладки «Подзадачи» открывала сразу форму ввода задачи, а кнопка «новая
/// задача» в списке задач — диалог выбора «пустая задача или шаблон» (<c>NewTaskDialog</c>,
/// todo31). Теперь у подзадач та же форма: выбор «пустая или из шаблона», дальше либо
/// прежняя форма ввода с проставленным родителем, либо создание из шаблона —
/// с подцеплением головы копии к текущей задаче.
///
/// Ядро: у <c>TaskService.InstantiateTemplate</c> появился parentId. Правило проекта —
/// как у переноса по иерархии (<c>ChangeParent</c>): подзадача лежит в проекте родителя,
/// поэтому В ПРОЕКТ РОДИТЕЛЯ переезжает вся копия, включая общий шаблон без проекта.
/// Разметка проверяется файлами (как в <see cref="T157Tests"/>), живая проверка —
/// <c>test/t201</c>.
/// </summary>
public sealed class T201Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

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

    /// <summary>Шаблон из головы и одного потомка; проект — как передали (null — общий).</summary>
    private (TaskItem Head, TaskItem Child) Template(string? projectId, string? teamId = null)
    {
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон выпуска",
            IsTemplate = true,
            ProjectId = projectId,
            TeamId = teamId,
        }, "голова", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            Title = "Узел шаблона",
            IsTemplate = true,
            ProjectId = projectId,
            TeamId = teamId,
            ParentId = head.Id,
        }, "потомок", "", null);
        return (head, child);
    }

    // ---------- 1. голова копии становится подзадачей ----------

    [Fact]
    public void Instantiate_With_Parent_Hangs_The_Copy_Under_The_Task()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" },
            "р", "", null);
        var (template, _) = Template(project.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(parent.Id, head.ParentId);
        Assert.False(head.IsTemplate);
        // вся иерархия шаблона скопирована, внутренние связи сохранены
        var created = _f.Tasks.List(projectId: project.Id).Where(t => !t.IsTemplate).ToList();
        var copyChild = Assert.Single(created, t => t.ParentId == head.Id);
        Assert.Equal("Узел шаблона", copyChild.Title);
    }

    // ---------- 2. проект и команда — от родителя ----------

    /// <summary>Общий шаблон (проекта нет вовсе) едет В ПРОЕКТ РОДИТЕЛЯ: подзадача обязана
    /// лежать там же, где родитель, иначе она даже не попадёт в его список подзадач.</summary>
    [Fact]
    public void Common_Template_Moves_Into_The_Project_Of_The_Parent()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var team = _f.Teams.Create(new Team { Name = "Команда-родителя" }, null);
        var parent = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Родитель",
        }, "р", "", null);
        var (template, _) = Template(projectId: null);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(project.Id, head.ProjectId);
        Assert.Equal(team.Id, head.TeamId); // у узла шаблона команды нет — берётся родительская
        var copyChild = Assert.Single(_f.Tasks.List(projectId: project.Id),
            t => t.ParentId == head.Id);
        Assert.Equal(project.Id, copyChild.ProjectId);
        Assert.Equal(team.Id, copyChild.TeamId);
    }

    /// <summary>Своя команда узла шаблона сильнее родительской.</summary>
    [Fact]
    public void Team_Of_The_Template_Node_Wins_Over_The_Parent_Team()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var parentTeam = _f.Teams.Create(new Team { Name = "Команда-родителя" }, null);
        var templateTeam = _f.Teams.Create(new Team { Name = "Команда-шаблона" }, null);
        var parent = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = parentTeam.Id,
            Title = "Родитель",
        }, "р", "", null);
        var (template, _) = Template(project.Id, templateTeam.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(templateTeam.Id, head.TeamId);
    }

    // ---------- 3. отказы ----------

    [Fact]
    public void Unknown_Parent_And_Template_Parent_Are_Rejected()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var (template, _) = Template(project.Id);
        var otherTemplate = _f.Tasks.Create(new TaskItem { Title = "Чужой шаблон", IsTemplate = true },
            "ш", "", null);

        Assert.Throws<ArgumentException>(() =>
            _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, "нет-такого"));
        // копия шаблона — обычная задача, под узлом шаблона ей не место
        Assert.Throws<ArgumentException>(() =>
            _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, otherTemplate.Id));
    }

    // ---------- 4. прежнее поведение без родителя ----------

    [Fact]
    public void Without_Parent_The_Copy_Stays_A_Root_Task_In_Its_Own_Project()
    {
        var project = _f.Projects.Create("Проект шаблона", null, null, null);
        var (template, _) = Template(project.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Null(head.ParentId);
        Assert.Equal(project.Id, head.ProjectId);
    }

    // ---------- 5. кнопка «+» открывает ту же форму ----------

    /// <summary>Вкладка «подзадачи» карточки задачи зовёт NewTaskDialog — тот же диалог
    /// выбора, что и кнопка «новая задача» в списке задач (TasksView).</summary>
    [Fact]
    public void Subtask_Button_Opens_The_Same_New_Task_Dialog()
    {
        var card = Ui("Components", "TaskCardView.razor");

        Assert.Contains("DialogParameters<NewTaskDialog>", card);
        Assert.Contains("NewTaskDialog.Choice choice", card);
        // список шаблонов — по проекту ЭТОЙ задачи (правило T-157)
        Assert.Contains("{ d => d.ProjectId, _details.Task.ProjectId }", card);
        // выбранный шаблон разворачивается подзадачей: тот же флоу, что у «новой задачи»
        Assert.Contains("InstantiateFlow.RunAsync(Dialogs, Api, L, template.Id, TaskId)", card);
        // пустая задача — прежняя форма ввода с родителем
        Assert.Contains("{ d => d.Parent, _details.Task }", card);
        // опора живой проверки в браузере — как data-newtask у кнопки списка задач (T-157)
        Assert.Contains("data-subtask-add=\"1\"", card);
    }

    /// <summary>У ШАБЛОНА выбора нет: под узел шаблона можно завести только узел шаблона,
    /// а копия шаблона — обычная задача.</summary>
    [Fact]
    public void For_A_Template_The_Old_Form_Opens_Directly()
    {
        var card = Ui("Components", "TaskCardView.razor");
        var addSubtask = card[card.IndexOf("private async Task AddSubtaskAsync()", StringComparison.Ordinal)..];
        addSubtask = addSubtask[..addSubtask.IndexOf("private async Task AddSubtaskFromTemplateAsync",
            StringComparison.Ordinal)];

        Assert.Contains("if (!_details.Task.IsTemplate)", addSubtask);
        // проверка стоит ДО показа диалога выбора
        Assert.True(addSubtask.IndexOf("if (!_details.Task.IsTemplate)", StringComparison.Ordinal)
                    < addSubtask.IndexOf("NewTaskDialog", StringComparison.Ordinal));
    }

    /// <summary>Родитель доезжает до сервера: клиент кладёт его в тело запроса,
    /// эндпоинт передаёт в хранилище.</summary>
    [Fact]
    public void Parent_Is_Carried_Through_Api()
    {
        var client = Ui("Services", "ApiClient.cs");
        Assert.Contains("ParentId = parentId", client);

        var endpoints = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        Assert.Contains("dto.ParentId", endpoints);
    }
}
