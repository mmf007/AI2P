using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-271-S0 (ветка T-318 «Проект опыта»): ДЕЙСТВИЯ АГЕНТА ДЛЯ АНАЛИЗА ОПЫТА И ШАБЛОН
/// «АНАЛИЗ ОПЫТА».
///
/// Разбирать накопленный опыт агенту было нечем: он умел завести и поправить запись, но не
/// умел ни перечислить область пачками, ни спросить, берут ли запись в задания, ни погасить
/// устаревшую. Проверяется:
/// <list type="number">
/// <item>у КАЖДОГО нового инструмента есть запись справочника действий во всех пяти языках —
/// без неё правила безопасности инструмент не закрывают вовсе (наука 2d3af8da);</item>
/// <item><c>set_experience_active</c> гасит и оживляет запись, не трогая её текста, и НЕ
/// даёт погасить ни чужую запись, ни поставляемое правило дистрибутива;</item>
/// <item><c>update_experience</c> меняет тэги, навык и пометки — и НЕ сбрасывает того, чего
/// ему не передали;</item>
/// <item><c>list_experience</c> перечисляет область страницами и по умолчанию не показывает
/// погашенные записи;</item>
/// <item><c>experience_usage</c> считает то, что реально уходило в задания (след T-266-S0),
/// и считает по ЗАДАЧАМ, а не по запускам;</item>
/// <item>набор опыта «Анализ опыта» приносит УЗЛЫ ШАБЛОНА, повторная установка их не
/// дублирует, потомки получают текст родителя в задании;</item>
/// <item>корневой узел набора годится в расписание БЕЗ правок кода (Schedule.TemplateTaskId);</item>
/// <item>текст шаблона — готовая инструкция на всех пяти языках: что сделать, чего не делать
/// и по какому правилу гасить по статистике.</item>
/// </list>
/// </summary>
public sealed class T271S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    private static readonly string[] NewTools =
        ["set_experience_active", "list_experience", "experience_usage"];

    private static string I18nDir => Path.Combine(AppContext.BaseDirectory, "i18n");

    private const string OtherServerId = "unid-S1";

    private static ServerScope OtherScope =>
        new(() => OtherServerId, () => "S1", () => false,
            codeOf: id => id == OtherServerId ? "S1" : "",
            nameOf: id => id == OtherServerId ? "ноутбук" : "");

    private Project NewProject() =>
        _f.Projects.Create("Проект T-271-S0", Path.Combine(_f.Dir, "prj271"), null, null);

    private TaskItem NewTask(Project project) =>
        _f.Tasks.Create(new TaskItem { Title = "Разбор опыта", ProjectId = project.Id },
            "формулировка", "", null);

    private TaskToolset Toolset(TaskItem current) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current, _f.RefData, _f.Picker,
            null, _f.Experience);

    private static string Call(TaskToolset toolset, string tool, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        return toolset.ExecuteAsync(tool, doc.RootElement, default).GetAwaiter().GetResult();
    }

    private string SkillId(string code) => _f.RefData.Skills().First(s => s.Name == code).Id;

    // ---------- справочник действий ----------

    [Fact]
    public void Every_New_Action_Has_A_Catalog_Row_In_All_Five_Languages()
    {
        // ИНСТРУМЕНТ БЕЗ ЗАПИСИ СПРАВОЧНИКА правилами безопасности не закрывается ВОВСЕ:
        // CodeByTool вернёт null, и Authorize разрешит вызов (наука 2d3af8da)
        foreach (var lang in Langs)
        {
            var file = Path.Combine(I18nDir, $"ActionCatalogService_{lang}.json");
            using var seed = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(seed.RootElement.GetProperty("seedVersion").GetInt32() >= 23,
                $"seedVersion в ActionCatalogService_{lang}.json не поднят");
            foreach (var tool in NewTools)
            {
                var row = seed.RootElement.GetProperty("actions").EnumerateArray()
                    .FirstOrDefault(a => a.TryGetProperty("tool", out var t)
                                         && t.ValueKind == JsonValueKind.String
                                         && t.GetString() == tool);
                Assert.True(row.ValueKind == JsonValueKind.Object,
                    $"в ActionCatalogService_{lang}.json нет записи на инструмент {tool}");
                Assert.StartsWith("AI2P.Experience.", row.GetProperty("code").GetString());
                Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("prompt").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("hint").GetString()));
            }
        }

        _f.Actions.Seed();
        Assert.Equal("AI2P.Experience.SetActive", _f.Actions.CodeByTool("set_experience_active"));
        Assert.Equal("AI2P.Experience.List", _f.Actions.CodeByTool("list_experience"));
        Assert.Equal("AI2P.Experience.Usage", _f.Actions.CodeByTool("experience_usage"));
    }

    [Fact]
    public void The_New_Tools_Are_Published_To_The_Agent()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var agent = new AgentToolset(null, toolset, s => s);
        foreach (var tool in NewTools)
        {
            Assert.Contains(agent.Specs, s => s.Name == tool);
        }
    }

    // ---------- погасить и оживить ----------

    [Fact]
    public void Switching_A_Record_Off_Keeps_Its_Text_And_Takes_It_Out_Of_Jobs()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var record = _f.Experience.CreateForProject(project.Id, "устаревший вывод", null,
            tags: ["сборка"]);

        var answer = Call(toolset, "set_experience_active",
            $$"""{ "id": "{{record.Id}}", "active": false }""");

        Assert.Contains(record.Id, answer);
        var off = _f.Experience.Get(record.Id)!;
        Assert.False(off.IsActive);
        Assert.Equal("устаревший вывод", off.Text);          // текст не переписан
        Assert.Equal(["сборка"], off.Tags);                  // тэги не переписаны
        Assert.False(ExperienceService.GoesToPrompt(off, [], null));

        Call(toolset, "set_experience_active", $$"""{ "id": "{{record.Id}}", "active": true }""");
        Assert.True(_f.Experience.Get(record.Id)!.IsActive);
    }

    [Fact]
    public void The_Agent_Does_Not_Switch_Off_A_Rule_Shipped_With_The_Distribution()
    {
        // правила дистрибутива — это правила, ПО КОТОРЫМ РАБОТАЕТ САМ АГЕНТ: выключать их
        // решает человек (у него такая кнопка есть, T-265-S0)
        _f.Experience.SeedGeneral();
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var rule = ExperienceService.GeneralSeed[0].Id;

        var answer = Call(toolset, "set_experience_active",
            $$"""{ "id": "{{rule}}", "active": false }""");

        Assert.Contains(rule, answer);
        Assert.True(_f.Experience.Get(rule)!.IsActive);      // погасить не дали
    }

    [Fact]
    public void The_Agent_Does_Not_Switch_Off_A_Record_Of_Another_Server()
    {
        var project = NewProject();
        var alien = new ExperienceService(_f.Db, _f.Events, OtherScope);
        var foreign = alien.CreateForProject(project.Id, "вывод соседнего сервера", null);
        var toolset = Toolset(NewTask(project));

        Call(toolset, "set_experience_active", $$"""{ "id": "{{foreign.Id}}", "active": false }""");

        Assert.True(_f.Experience.Get(foreign.Id)!.IsActive);
    }

    // ---------- правка тэгов, навыка и пометок ----------

    [Fact]
    public void Update_Sets_Tags_And_Skill_And_Keeps_What_Was_Not_Passed()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var record = _f.Experience.CreateForProject(project.Id, "старый текст", null,
            SkillId("code-write"), alwaysLoad: true, tags: ["сборка"]);

        Call(toolset, "update_experience",
            $$"""
            { "id": "{{record.Id}}", "text": "новый текст",
              "skill": "code-review", "tags": ["ревью", "опыт"] }
            """);

        var updated = _f.Experience.Get(record.Id)!;
        Assert.Equal("новый текст", updated.Text);
        Assert.Equal("code-review", updated.SkillName);
        Assert.Equal(["опыт", "ревью"], updated.Tags);        // тэги читаются ORDER BY tag
        Assert.True(updated.AlwaysLoad);                      // пометку человека не сбросили
        Assert.True(updated.IsActive);

        // а не переданные тэги и навык правка текста не трогает вовсе
        Call(toolset, "update_experience", $$"""{ "id": "{{record.Id}}", "text": "ещё правка" }""");
        var again = _f.Experience.Get(record.Id)!;
        Assert.Equal("code-review", again.SkillName);
        Assert.Equal(["опыт", "ревью"], again.Tags);
        Assert.True(again.AlwaysLoad);
    }

    [Fact]
    public void Update_Can_Switch_The_Record_Off_Too()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var record = _f.Experience.CreateForProject(project.Id, "текст", null);

        Call(toolset, "update_experience",
            $$"""{ "id": "{{record.Id}}", "text": "сводная запись", "active": false }""");

        Assert.False(_f.Experience.Get(record.Id)!.IsActive);
    }

    // ---------- перечисление области ----------

    [Fact]
    public void The_List_Walks_The_Scope_In_Pages_And_Hides_The_Switched_Off()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var first = _f.Experience.CreateForProject(project.Id, "первый вывод", null);
        var second = _f.Experience.CreateForProject(project.Id, "второй вывод", null,
            SkillId("code-review"), tags: ["ревью"]);
        var third = _f.Experience.CreateForProject(project.Id, "третий вывод", null);
        _f.Experience.SetActive(third.Id, false, null);

        var live = Call(toolset, "list_experience", """{ "scope": "project" }""");
        Assert.Contains(first.Id, live);
        Assert.Contains(second.Id, live);
        Assert.DoesNotContain(third.Id, live);                // погашенные по умолчанию не видны

        var all = Call(toolset, "list_experience",
            """{ "scope": "project", "includeInactive": true }""");
        Assert.Contains(third.Id, all);

        // страницами: вторая страница из одной строки — это ВТОРАЯ запись, а не первая
        var page = Call(toolset, "list_experience",
            """{ "scope": "project", "limit": 1, "offset": 1 }""");
        Assert.Contains(second.Id, page);
        Assert.DoesNotContain(first.Id, page);

        // отбор по навыку и по тэгу
        var bySkill = Call(toolset, "list_experience",
            """{ "scope": "project", "skill": "code-review" }""");
        Assert.Contains(second.Id, bySkill);
        Assert.DoesNotContain(first.Id, bySkill);
        var byTag = Call(toolset, "list_experience", """{ "scope": "project", "tag": "ревью" }""");
        Assert.Contains(second.Id, byTag);
        Assert.DoesNotContain(first.Id, byTag);
    }

    [Fact]
    public void The_List_Reaches_The_General_Rules_Too()
    {
        var project = NewProject();
        var toolset = Toolset(NewTask(project));
        var rule = _f.Experience.CreateGeneral("общее правило работы", null);

        var answer = Call(toolset, "list_experience", """{ "scope": "general" }""");

        Assert.Contains(rule.Id, answer);
    }

    // ---------- статистика использования ----------

    [Fact]
    public void Usage_Counts_What_Really_Went_Into_Jobs_And_Counts_Tasks_Not_Runs()
    {
        var project = NewProject();
        var task = NewTask(project);
        var other = _f.Tasks.Create(new TaskItem { Title = "Вторая", ProjectId = project.Id },
            "т", "", null);
        var used = _f.Experience.CreateForProject(project.Id, "ходовой вывод", null);
        var never = _f.Experience.CreateForProject(project.Id, "невостребованный вывод", null);

        _f.Experience.NoteUsed(task.Id, [used.Id], "job-1");
        _f.Experience.NoteUsed(task.Id, [used.Id], "job-2");   // ПЕРЕЗАПУСК той же задачи
        _f.Experience.NoteUsed(other.Id, [used.Id], "job-3");

        var map = _f.Experience.UsageMap();
        Assert.Equal(2, map[used.Id].Count);                   // задачи, а не запуски
        Assert.False(map.ContainsKey(never.Id));

        var toolset = Toolset(task);
        var one = Call(toolset, "experience_usage", $$"""{ "id": "{{used.Id}}" }""");
        Assert.Contains(used.Id, one);
        Assert.Contains("2", one);

        var scope = Call(toolset, "experience_usage", """{ "scope": "project" }""");
        Assert.Contains(never.Id, scope);
        Assert.Contains(used.Id, scope);
        // самые невостребованные идут первыми: по ним и решают, что гасить
        Assert.True(scope.IndexOf(never.Id, StringComparison.Ordinal)
                    < scope.IndexOf(used.Id, StringComparison.Ordinal));
    }

    // ---------- набор с шаблоном «Анализ опыта» ----------

    private static ExperiencePack AnalysisPack =>
        ExperiencePack.Parse(ExperiencePackSeed.ExperienceAnalysisJson)!;

    private ExperiencePackService Packs =>
        new(_f.Files, _f.Experience, _f.RefData, _f.Tasks);

    [Fact]
    public void The_Analysis_Pack_Brings_The_Template_Tree_And_Does_Not_Double_It()
    {
        var project = NewProject();
        var pack = AnalysisPack;
        Assert.Equal(3, pack.Templates.Count);

        var (records, templates) = Packs.Install(pack, ExperienceScopes.Project, project.Id,
            null, null, "ru");

        Assert.Equal(1, records);
        Assert.Equal(3, templates);
        var root = _f.Tasks.Get(pack.Templates[0].Id)!;
        Assert.True(root.IsTemplate);
        Assert.Null(root.ParentId);                            // корень — иначе не встанет в расписание
        Assert.Equal(project.Id, root.ProjectId);
        Assert.False(root.ParentInPrompt);
        foreach (var child in pack.Templates.Skip(1))
        {
            var node = _f.Tasks.Get(child.Id)!;
            Assert.Equal(root.Id, node.ParentId);
            // порядок работы написан в родителе ОДИН раз — потомок получает его в задании
            Assert.True(node.ParentInPrompt);
            Assert.Contains("list_experience", _f.Tasks.ReadDescription(node));
        }

        // повторная установка второго дерева не делает
        var (_, again) = Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");
        Assert.Equal(0, again);
        Assert.All(pack.Templates, node => Assert.NotNull(_f.Tasks.Get(node.Id)));
    }

    [Fact]
    public void Without_A_Project_The_Pack_Installs_Its_Records_But_Not_Its_Nodes()
    {
        // у общих правил организации проекта нет вовсе, а шаблон задач без проекта не живёт:
        // это НЕ отказ установки — записи ставятся, узлов заведено ноль
        var (records, templates) = Packs.Install(AnalysisPack, ExperienceScopes.General,
            null, null, null, "ru");

        Assert.Equal(1, records);
        Assert.Equal(0, templates);
    }

    [Fact]
    public void The_Root_Node_Goes_Into_A_Schedule_Without_Code_Changes()
    {
        var project = NewProject();
        Packs.Install(AnalysisPack, ExperienceScopes.Project, project.Id, null, null, "ru");
        var root = AnalysisPack.Templates[0].Id;

        var schedule = _f.Schedules.Create(new Schedule
        {
            ProjectId = project.Id,
            TemplateTaskId = root,
            // недельный период уже умеет копировать шаблон сам — правок кода не нужно
            Kind = "periodic",
            PeriodJson = """{"type":"weekly","days":[1],"time":"03:00"}""",
            IsActive = true,
        }, null);

        Assert.Equal(root, schedule.TemplateTaskId);
    }

    [Fact]
    public void The_Analysis_Template_Is_A_Ready_Instruction_In_All_Five_Languages()
    {
        foreach (var lang in Langs)
        {
            var pack = AnalysisPack;
            foreach (var node in pack.Templates)
            {
                Assert.False(string.IsNullOrWhiteSpace(node.Title.Text(lang)),
                    $"нет заголовка узла {node.Id} на языке {lang}");
                Assert.False(string.IsNullOrWhiteSpace(node.Description.Text(lang)),
                    $"нет формулировки узла {node.Id} на языке {lang}");
                Assert.False(string.IsNullOrWhiteSpace(node.Acceptance.Text(lang)),
                    $"нет критериев приёмки узла {node.Id} на языке {lang}");
            }
            var root = pack.Templates[0].Description.Text(lang);
            // инструкция называет ВСЕ пять действий разбора и правило деактивации
            foreach (var tool in new[]
                     {
                         "experience_usage", "list_experience", "create_experience",
                         "update_experience", "move_experience", "set_experience_active",
                     })
            {
                Assert.Contains(tool, root);
            }
            Assert.Contains("3", root);   // срок правила «погасить по статистике»
            Assert.False(string.IsNullOrWhiteSpace(pack.Name.Text(lang)));
            Assert.False(string.IsNullOrWhiteSpace(pack.Description.Text(lang)));
        }
    }

    // ---------- подкоманды CLI ----------

    [Fact]
    public void The_Cli_Has_A_Subcommand_For_Every_New_Action()
    {
        // маркер заканчивает ход агента и стоит дорого, команда отвечает в том же ходе
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "AI2P.Server", "Cli",
            "AgentCliRunner.cs"));
        foreach (var pair in new[]
                 {
                     ("experience-active", "set_experience_active"),
                     ("experience-list", "list_experience"),
                     ("experience-usage", "experience_usage"),
                 })
        {
            Assert.Contains($"[\"{pair.Item1}\"] = new(\"{pair.Item2}\"", source);
        }
        // «--active false» обязан читаться как ЛОЖЬ, а не как строка «false»
        Assert.Contains("\"active\", \"alwaysLoad\"", source);
    }

    /// <summary>Корень репозитория приложения от каталога сборки тестов.</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
