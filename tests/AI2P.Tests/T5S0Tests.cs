using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-5-S0 (версия 1.101): ОТВЕТСТВЕННЫЙ ПО УМОЛЧАНИЮ У ПРОЕКТА.
///
/// В настройках проекта появилось поле «ответственный по умолчанию» — исполнитель-человек
/// из команды проекта. Пусто (и так у всех уже заведённых проектов) — ввод задач идёт
/// ровно как раньше. Заполнено — умолчание подставляется в двух местах:
/// <list type="number">
/// <item>ВВОД НОВОЙ ЗАДАЧИ — поле «ответственный» формы задачи открывается заполненным
/// (человек волен тут же его снять: это умолчание, а не обязательство);</item>
/// <item>КОПИЯ ШАБЛОНА — только в те узлы, где ответственный НЕ задан; расписанный
/// в шаблоне ответственный сильнее умолчания проекта.</item>
/// </list>
///
/// Главное правило проверок ниже: настройка хранит только идентификатор, а исполнителя
/// могли выключить, удалить или это вовсе ИИ — тогда умолчания просто НЕТ. Иначе протухшее
/// значение роняло бы создание задач по всему проекту: ответственным может быть только
/// живой активный человек (правило <c>TaskService.Validate</c>).
///
/// Поведение форм в браузере — живая проверка <c>test/t5s0</c>; здесь разметка проверяется
/// файлами (приём T-209/T-267).
/// </summary>
public sealed class T5S0Tests : IDisposable
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

    private Executor Human(string nick = "маша") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    /// <summary>Шаблон из головы и одного потомка; ответственный ставится голове,
    /// если он передан.</summary>
    private (TaskItem Head, TaskItem Child) Template(string? projectId, string? responsibleId = null)
    {
        var head = _f.Tasks.Create(new TaskItem
        {
            Title = "Шаблон выпуска",
            IsTemplate = true,
            ProjectId = projectId,
            ResponsibleId = responsibleId,
        }, "голова", "", null);
        var child = _f.Tasks.Create(new TaskItem
        {
            Title = "Узел шаблона",
            IsTemplate = true,
            ProjectId = projectId,
            ParentId = head.Id,
        }, "потомок", "", null);
        return (head, child);
    }

    // ---------- 1. разбор настройки ----------

    /// <summary>Настройки нет вовсе — умолчания нет (так у всех проектов до T-5-S0).</summary>
    [Fact]
    public void Without_A_Setting_There_Is_No_Default()
    {
        Assert.Null(ProjectSettings.DefaultResponsibleId(null));
        Assert.Null(ProjectSettings.DefaultResponsibleId(""));
        Assert.Null(ProjectSettings.DefaultResponsibleId("{}"));
        Assert.Null(ProjectSettings.DefaultResponsibleId(
            """{"defaultTeamId":null,"qualityBias":0.5,"objRefFormat":"code"}"""));
    }

    /// <summary>Заданная настройка читается.</summary>
    [Fact]
    public void A_Set_Default_Is_Read()
    {
        Assert.Equal("EX-1", ProjectSettings.DefaultResponsibleId(
            """{"qualityBias":0.5,"defaultResponsibleId":"EX-1"}"""));
    }

    /// <summary>Настройки едут между серверами кластера и пишутся разными версиями
    /// приложения: испорченный JSON, не та форма записи и пустая строка — это «умолчания
    /// нет», а не падение посреди отрисовки формы.</summary>
    [Fact]
    public void A_Broken_Or_Empty_Setting_Is_No_Default()
    {
        Assert.Null(ProjectSettings.DefaultResponsibleId("не json вовсе"));
        Assert.Null(ProjectSettings.DefaultResponsibleId("[1,2,3]"));
        Assert.Null(ProjectSettings.DefaultResponsibleId("""{"defaultResponsibleId":null}"""));
        Assert.Null(ProjectSettings.DefaultResponsibleId("""{"defaultResponsibleId":7}"""));
        Assert.Null(ProjectSettings.DefaultResponsibleId("""{"defaultResponsibleId":""}"""));
        Assert.Null(ProjectSettings.DefaultResponsibleId("""{"defaultResponsibleId":"   "}"""));
    }

    // ---------- 2. хранение настройки ----------

    /// <summary>Настройка принадлежит ПРОЕКТУ: у разных проектов она разная, и новый проект
    /// заводится сразу с ней (форма нового проекта → <c>ProjectCreateDto</c>).</summary>
    [Fact]
    public void The_Default_Belongs_To_The_Project()
    {
        var human = Human();
        var withDefault = _f.Projects.Create("Ролик про кота", null, null, null,
            defaultResponsibleId: human.Id);
        var without = _f.Projects.Create("Ролик про пса", null, null, null);

        Assert.Equal(human.Id, ProjectSettings.DefaultResponsibleId(withDefault.SettingsJson));
        Assert.Null(ProjectSettings.DefaultResponsibleId(without.SettingsJson));
        // и то же самое после перечитывания из базы
        Assert.Equal(human.Id,
            ProjectSettings.DefaultResponsibleId(_f.Projects.Get(withDefault.Id)!.SettingsJson));
    }

    /// <summary>Пустая строка в настройки не попадает: «не выбрано» — это отсутствие
    /// значения, иначе разбор пришлось бы делать снисходительным дважды.</summary>
    [Fact]
    public void An_Empty_Choice_Is_Not_Stored()
    {
        var project = _f.Projects.Create("Ролик", null, null, null, defaultResponsibleId: "  ");

        Assert.Null(ProjectSettings.DefaultResponsibleId(project.SettingsJson));
    }

    /// <summary>Правка проекта переписывает настройки ЦЕЛИКОМ (обе формы собирают их
    /// заново), поэтому проверяем, что вместе с командой и ценой↔качеством доезжает
    /// и ответственный по умолчанию.</summary>
    [Fact]
    public void Saving_The_Project_Keeps_The_Default()
    {
        var human = Human();
        var project = _f.Projects.Create("Ролик", null, null, null, defaultResponsibleId: human.Id);

        project.Name = "Ролик про кота";
        project.SettingsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            defaultTeamId = (string?)null,
            qualityBias = 0.75,
            objRefFormat = ObjectRefFormats.Code,
            defaultResponsibleId = human.Id,
        });
        _f.Projects.Update(project, null);

        var saved = _f.Projects.Get(project.Id)!;
        Assert.Equal(human.Id, ProjectSettings.DefaultResponsibleId(saved.SettingsJson));
        Assert.Equal(0.75, AI2P.Storage.Services.ProjectService.QualityBias(saved), 3);
    }

    // ---------- 3. годность умолчания к подстановке ----------

    /// <summary>Живой активный человек — годное умолчание.</summary>
    [Fact]
    public void A_Live_Human_Is_A_Usable_Default()
    {
        var human = Human();
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: human.Id);

        Assert.Equal(human.Id, _f.Tasks.DefaultResponsibleOf(project.Id));
    }

    /// <summary>Выключенный исполнитель умолчанием не работает: хранилище не примет его
    /// в новую задачу (ТЗ п. 2.2), и подстановка ломала бы ввод по всему проекту.</summary>
    [Fact]
    public void A_Disabled_Executor_Is_Not_Substituted()
    {
        var human = Human();
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: human.Id);

        human.IsActive = false;
        _f.Executors.Update(human, null);

        Assert.Null(_f.Tasks.DefaultResponsibleOf(project.Id));
    }

    /// <summary>Ответственным может быть только ЧЕЛОВЕК: ИИ, попавший в настройки мимо
    /// формы (правка чужой версией, другой сервер кластера), умолчанием не становится.</summary>
    [Fact]
    public void An_Ai_Executor_Is_Not_Substituted()
    {
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод",
            Kind = ExecutorKind.Ai,
            ProfilePath = "profile.json",
            CapabilitiesPath = "scope.json",
        }, null);
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: ai.Id);

        Assert.Null(_f.Tasks.DefaultResponsibleOf(project.Id));
    }

    /// <summary>Несуществующий исполнитель (удалён после того, как его записали) и задача
    /// без проекта — тоже «умолчания нет».</summary>
    [Fact]
    public void A_Missing_Executor_Or_Project_Is_No_Default()
    {
        var project = _f.Projects.Create("Проект", null, null, null,
            defaultResponsibleId: "ex-которого-нет");

        Assert.Null(_f.Tasks.DefaultResponsibleOf(project.Id));
        Assert.Null(_f.Tasks.DefaultResponsibleOf(null));
        Assert.Null(_f.Tasks.DefaultResponsibleOf(""));
    }

    // ---------- 4. копия шаблона ----------

    /// <summary>Узел шаблона без ответственного получает умолчание проекта — ради этого
    /// настройка и заводилась.</summary>
    [Fact]
    public void A_Template_Node_Without_A_Responsible_Gets_The_Project_Default()
    {
        var human = Human();
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: human.Id);
        var (template, _) = Template(project.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Equal(human.Id, head.ResponsibleId);
        var child = Assert.Single(_f.Tasks.List(projectId: project.Id),
            t => !t.IsTemplate && t.ParentId == head.Id);
        Assert.Equal(human.Id, child.ResponsibleId);
    }

    /// <summary>Ответственный, расписанный В ШАБЛОНЕ, сильнее умолчания проекта: это явный
    /// выбор человека, и подменять его настройкой нельзя.</summary>
    [Fact]
    public void A_Responsible_Set_In_The_Template_Wins()
    {
        var byTemplate = Human("шаблонная");
        var byProject = Human("проектная");
        var project = _f.Projects.Create("Проект", null, null, null,
            defaultResponsibleId: byProject.Id);
        var (template, _) = Template(project.Id, responsibleId: byTemplate.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Equal(byTemplate.Id, head.ResponsibleId);
        // а потомок шаблона поля не имел — ему досталось умолчание проекта
        var child = Assert.Single(_f.Tasks.List(projectId: project.Id),
            t => !t.IsTemplate && t.ParentId == head.Id);
        Assert.Equal(byProject.Id, child.ResponsibleId);
    }

    /// <summary>Проект без умолчания — копия шаблона как раньше, поле пустое.</summary>
    [Fact]
    public void Without_A_Default_The_Copy_Is_As_Before()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var (template, _) = Template(project.Id);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Null(head.ResponsibleId);
    }

    /// <summary>Выключенный ответственный шаблона снимается (правило todo31) и умолчанием
    /// проекта НЕ заменяется вслепую… — заменяется, потому что поле копии оказалось пустым:
    /// это ровно тот случай, ради которого настройка и нужна.</summary>
    [Fact]
    public void A_Disabled_Template_Responsible_Falls_Back_To_The_Default()
    {
        var stale = Human("уволенный");
        var byProject = Human("проектная");
        var project = _f.Projects.Create("Проект", null, null, null,
            defaultResponsibleId: byProject.Id);
        var (template, _) = Template(project.Id, responsibleId: stale.Id);

        stale.IsActive = false;
        _f.Executors.Update(stale, null);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Equal(byProject.Id, head.ResponsibleId);
    }

    /// <summary>Копия, создаваемая ПОДЗАДАЧЕЙ (T-201), переезжает в проект родителя —
    /// значит и умолчание берётся у проекта РОДИТЕЛЯ, а не у проекта шаблона. Для общего
    /// шаблона (проекта нет вовсе) это единственный источник умолчания.</summary>
    [Fact]
    public void The_Default_Comes_From_The_Project_Of_The_Parent()
    {
        var human = Human();
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: human.Id);
        var parent = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Родитель" },
            "р", "", null);
        var (template, _) = Template(projectId: null);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null, null, null, parent.Id);

        Assert.Equal(project.Id, head.ProjectId);
        Assert.Equal(human.Id, head.ResponsibleId);
    }

    /// <summary>Общий шаблон, развёрнутый БЕЗ родителя, проекта не имеет — брать умолчание
    /// неоткуда, и задача создаётся без ответственного (а не падает).</summary>
    [Fact]
    public void A_Common_Template_Without_A_Parent_Has_No_Default()
    {
        Human();
        var (template, _) = Template(projectId: null);

        var head = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Null(head.ProjectId);
        Assert.Null(head.ResponsibleId);
    }

    /// <summary>Сам ШАБЛОН умолчания не получает: подставляется оно в КОПИЮ и по проекту
    /// копии — иначе узел общего шаблона обзавёлся бы ответственным чужого проекта.</summary>
    [Fact]
    public void The_Template_Node_Itself_Is_Left_Alone()
    {
        var human = Human();
        var project = _f.Projects.Create("Проект", null, null, null, defaultResponsibleId: human.Id);
        var (template, _) = Template(project.Id);

        Assert.Null(_f.Tasks.Get(template.Id)!.ResponsibleId);
    }

    // ---------- 5. формы ----------

    /// <summary>Настройки проекта переписываются ЦЕЛИКОМ обеими формами (наука T-267),
    /// поэтому поле обязано быть в каждой из них — и в сборке настроек, и в чтении.</summary>
    [Theory]
    [InlineData("ProjectDialog.razor")]
    [InlineData("ProjectCardView.razor")]
    public void Both_Project_Forms_Know_The_Setting(string file)
    {
        var text = Ui("Components", file);

        Assert.Contains("data-project-responsible", text);
        Assert.Contains("projects.defaultResponsible", text);
        Assert.Contains("ProjectSettings.DefaultResponsibleId(", text);
        Assert.Contains("defaultResponsibleId = _defaultResponsibleId", text);
        // список — люди команды проекта: ответственным может быть только человек
        Assert.Contains("ExecutorKind.Human", text);
    }

    /// <summary>Форма задачи подставляет умолчание в НОВУЮ задачу и идёт за сменой проекта
    /// в этой же форме; ник вместо GUID показывает ToStringFunc (наука todo34_3).</summary>
    [Fact]
    public void The_Task_Form_Prefills_A_New_Task()
    {
        var text = Ui("Components", "TaskDialog.razor");

        Assert.Contains("DefaultResponsibleOf(", text);
        Assert.Contains("_task.ResponsibleId = DefaultResponsibleOf(_task.ProjectId);", text);
        Assert.Contains("_task.ResponsibleId = DefaultResponsibleOf(projectId);", text);
        Assert.Contains("data-task-responsible", text);
        Assert.Contains("ToStringFunc=\"@ExecutorNick\"", text);
    }

    /// <summary>Новый проект заводится с умолчанием прямо через API (форма нового проекта
    /// уходит на сервер целиком, <c>ProjectCreateDto</c>).</summary>
    [Fact]
    public void The_Api_Carries_The_Default_On_Create()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));

        Assert.Contains("dto.DefaultResponsibleId", endpoints);
    }

    /// <summary>Подпись и пояснение — в обоих словарях (правило мультиязычности T-180).</summary>
    [Theory]
    [InlineData("projects.defaultResponsible")]
    [InlineData("projects.defaultResponsible.hint")]
    public void The_Labels_Exist_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустая подпись {key}");
        }
    }
}
