using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-63-S0 (выпуск 1.109): ОШИБКА ПОДСТАНОВКИ КОМАНДЫ В НОВУЮ ПОДЗАДАЧУ.
///
/// Жалоба: подзадача, заведённая из карточки задачи, получила команду, не относящуюся
/// к проекту родителя. Причина — <c>TaskService.InstantiateTemplate</c>: копия шаблона
/// переезжает в проект РОДИТЕЛЯ (T-201), а команда бралась у узла шаблона как есть.
/// У общего шаблона (проекта нет вовсе) команда узла вправе принадлежать какому угодно
/// проекту, и она молча уезжала в чужую задачу.
///
/// Правило теперь такое: команда годится, если она общая (<c>teams.project_id</c> пуст)
/// либо привязана к проекту копии; чужую заменяет команда родителя, а нет и её — команда
/// по умолчанию проекта (ТЗ п. 2.7).
/// </summary>
public sealed class T63S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Шаблон из одной головы; проект и команда — как передали.</summary>
    private TaskItem Template(string? projectId, string? teamId) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон",
            IsTemplate = true,
            ProjectId = projectId,
            TeamId = teamId,
        }, "голова", "", null);

    /// <summary>Команда узла шаблона, привязанная к ЧУЖОМУ проекту, в подзадачу не едет:
    /// подзадача лежит в проекте родителя, значит и команда должна быть его.</summary>
    [Fact]
    public void Team_Of_Another_Project_Is_Not_Copied_Into_The_Subtask()
    {
        var project = _f.Projects.Create("Наш проект", null, null, null);
        var other = _f.Projects.Create("Чужой проект", null, null, null);
        var parentTeam = _f.Teams.Create(new Team { Name = "Команда-родителя" }, null);
        var alienTeam = _f.Teams.Create(new Team { Name = "Команда-чужого-проекта", ProjectId = other.Id }, null);
        var parent = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = parentTeam.Id,
            Title = "Родитель",
        }, "р", "", null);
        var template = Template(projectId: null, teamId: alienTeam.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(project.Id, head.ProjectId);
        Assert.Equal(parentTeam.Id, head.TeamId);
    }

    /// <summary>Команды нет и у родителя — берётся команда по умолчанию проекта (п. 2.7).</summary>
    [Fact]
    public void Default_Team_Of_The_Project_Replaces_The_Alien_One()
    {
        var other = _f.Projects.Create("Чужой проект", null, null, null);
        var projectTeam = _f.Teams.Create(new Team { Name = "Команда-проекта" }, null);
        var project = _f.Projects.Create("Наш проект", null, projectTeam.Id, null);
        var alienTeam = _f.Teams.Create(new Team { Name = "Команда-чужого-проекта", ProjectId = other.Id }, null);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" },
            "р", "", null);
        var template = Template(projectId: null, teamId: alienTeam.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(projectTeam.Id, head.TeamId);
    }

    /// <summary>Своя команда ПРОЕКТА копии по-прежнему сильнее родительской: правка
    /// отбрасывает только чужую (сторож против «починили, запретив всё»).</summary>
    [Fact]
    public void Team_Of_The_Same_Project_Still_Wins_Over_The_Parent_Team()
    {
        var project = _f.Projects.Create("Наш проект", null, null, null);
        var parentTeam = _f.Teams.Create(new Team { Name = "Команда-родителя" }, null);
        var nodeTeam = _f.Teams.Create(new Team { Name = "Команда-узла", ProjectId = project.Id }, null);
        var parent = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = parentTeam.Id,
            Title = "Родитель",
        }, "р", "", null);
        var template = Template(project.Id, nodeTeam.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(nodeTeam.Id, head.TeamId);
    }
}
