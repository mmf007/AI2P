using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-144: агент записывает ОПЫТ и правит ШАБЛОНЫ, когда его об этом просят. Раньше не мог:
/// инструменты опыта публиковались только задачам из шаблона (опыт ПРОЕКТА агенту был
/// доступен лишь на чтение — блоком в задании), шаблонов не касалось ни одно действие,
/// а у CLI-агента инструментов AI2P нет вовсе — на прямую просьбу «заполни опыт проекта
/// и шаблоны» он переносил выводы в файлы репозитория и писал, что поля опыта ему
/// недоступны. Права тут ни при чём: правила безопасности на действие по умолчанию
/// разрешающие, канала записи просто не было.
///
/// Теперь: create_experience/update_experience работают и с опытом проекта (scope),
/// появились list_templates / create_template / update_template, а CLI-агент делает то же
/// маркерами AI2P_EXPERIENCE и AI2P_TEMPLATE в ответе.
/// </summary>
public sealed class T144Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string ExpMarker = ClaudeCliConnector.ExperienceMarker;
    private const string TemplateMarker = ClaudeCliConnector.TemplateMarker;

    private Project _project = null!;

    private Project Project => _project ??= _f.Projects.Create("Проект", null, null, null);

    /// <summary>Обычная задача проекта — не из шаблона (именно с такой всё и не получалось).</summary>
    private TaskItem ProjectTask(string title = "Задача") =>
        _f.Tasks.Create(new TaskItem { ProjectId = Project.Id, Title = title }, "текст", "", null);

    private TaskToolset Tools(TaskItem current, string? actorId = null) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current, _f.RefData, _f.Picker, actorId,
            _f.Experience, project: Project);

    private AgentToolset Agent(TaskItem current, List<SecurityRule>? rules = null) =>
        new(null, Tools(current),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: _f.Actions.CodeByTool);

    private static System.Text.Json.JsonElement Args(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    // ---------- опыт проекта ----------

    [Fact]
    public async Task Project_Task_Writes_Project_Experience()
    {
        var task = ProjectTask();
        var tools = Tools(task);
        Assert.True(tools.CanExperience);   // до T-144 здесь было false

        var created = await tools.ExecuteAsync("create_experience",
            Args("""{"text":"тесты гоняем фильтром","skill":"code-test"}"""), CancellationToken.None);

        Assert.Contains("ПРОЕКТА", created);
        var record = Assert.Single(_f.Experience.ListByProject(Project.Id));
        Assert.Equal("тесты гоняем фильтром", record.Text);
        Assert.Equal("code-test", record.SkillName);
        Assert.Equal(Project.Id, record.ProjectId);   // запись проектная, а не по узлу шаблона
    }

    [Fact]
    public async Task Project_Experience_Is_Updated_By_Id_From_Prompt_Block()
    {
        var task = ProjectTask();
        // пометка «загружать всегда» (T-24-S0): с T-29-S0 запись без навыка иначе в блок
        // промпта не попадает, а тест здесь про id записи, а не про отбор
        var record = _f.Experience.CreateForProject(Project.Id, "старая формулировка", null,
            alwaysLoad: true);
        var block = JobOrchestrator.ProjectExperienceBlock(_f.Experience, Project.Id, []);
        Assert.Contains(record.Id, block);   // id в блоке — иначе править нечем

        var updated = await Tools(task).ExecuteAsync("update_experience",
            Args($$"""{"id":"{{record.Id}}","text":"новая формулировка"}"""), CancellationToken.None);

        Assert.Contains("обновлена", updated);
        Assert.Equal("новая формулировка", _f.Experience.Get(record.Id)!.Text);
    }

    [Fact]
    public async Task Experience_Of_Another_Project_Is_Not_Touched()
    {
        var stranger = _f.Projects.Create("Чужой", null, null, null);
        var alien = _f.Experience.CreateForProject(stranger.Id, "чужая запись", null);

        var answer = await Tools(ProjectTask()).ExecuteAsync("update_experience",
            Args($$"""{"id":"{{alien.Id}}","text":"перехват"}"""), CancellationToken.None);

        Assert.StartsWith("Ошибка", answer);
        Assert.Equal("чужая запись", _f.Experience.Get(alien.Id)!.Text);
    }

    [Fact]
    public async Task Unknown_Skill_Is_Reported_Instead_Of_Silent_Common_Record()
    {
        var answer = await Tools(ProjectTask()).ExecuteAsync("create_experience",
            Args("""{"text":"урок","skill":"нет-такого"}"""), CancellationToken.None);

        Assert.Contains("нет в справочнике", answer);
        Assert.Empty(_f.Experience.ListByProject(Project.Id));
    }

    // ---------- опыт узла шаблона ----------

    [Fact]
    public async Task Template_Experience_Is_Written_By_Node_Code()
    {
        var node = _f.Tasks.Create(
            new TaskItem { ProjectId = Project.Id, Title = "Выпуск версии", IsTemplate = true },
            "как выпускать", "", null);
        var tools = Tools(ProjectTask());

        var created = await tools.ExecuteAsync("create_experience",
            Args($$"""{"text":"поднять билд руками","scope":"template","template":"{{node.DisplayId}}"}"""),
            CancellationToken.None);

        Assert.Contains(node.DisplayId, created);
        Assert.Equal("поднять билд руками", _f.Experience.ListByTemplate(node.Id).Single().Text);

        // узел можно назвать и заголовком: код только что созданного узла агент не знает
        await tools.ExecuteAsync("create_experience",
            Args("""{"text":"вторая запись","scope":"template","template":"Выпуск версии"}"""),
            CancellationToken.None);
        Assert.Equal(2, _f.Experience.ListByTemplate(node.Id).Count);

        // несуществующий узел — понятная ошибка, а не молчаливая запись в проект
        var missing = await tools.ExecuteAsync("create_experience",
            Args("""{"text":"мимо","scope":"template","template":"T-999"}"""), CancellationToken.None);
        Assert.StartsWith("Ошибка", missing);
    }

    [Fact]
    public async Task Templated_Task_Still_Writes_To_Its_Own_Node_By_Default()
    {
        // поведение todo32 сохранено: у задачи из шаблона умолчание — её узел, а не проект
        var head = _f.Tasks.Create(
            new TaskItem { ProjectId = Project.Id, Title = "Шаблон", IsTemplate = true }, "т", "", null);
        var task = _f.Tasks.InstantiateTemplate(head.Id, null, null);

        await Tools(task).ExecuteAsync("create_experience",
            Args("""{"text":"урок узла"}"""), CancellationToken.None);

        Assert.Single(_f.Experience.ListByTemplate(head.Id));
        Assert.Empty(_f.Experience.ListByProject(Project.Id));
    }

    // ---------- шаблоны ----------

    [Fact]
    public async Task Templates_Are_Listed_Created_And_Updated()
    {
        var tools = Tools(ProjectTask());

        var empty = await tools.ExecuteAsync("list_templates", Args("{}"), CancellationToken.None);
        Assert.Contains("нет шаблонов", empty);

        var created = await tools.ExecuteAsync("create_template",
            Args("""
                 {"title":"Выпуск версии","description":"поднять билд, собрать выкладку",
                  "acceptance":"тесты зелёные","skills":["code-write-cs"]}
                 """), CancellationToken.None);
        Assert.Contains("Создан узел шаблона", created);
        var head = _f.Tasks.List(Project.Id, templatesOnly: true).Single();
        Assert.True(head.IsTemplate);
        Assert.Equal("поднять билд, собрать выкладку", _f.Tasks.ReadDescription(head));
        Assert.Equal("тесты зелёные", _f.Tasks.ReadAcceptance(head));
        Assert.Single(head.SkillIds);

        // потомок узла: parent — код головы
        await tools.ExecuteAsync("create_template",
            Args($$"""{"title":"Прогон тестов","description":"dotnet test","parent":"{{head.DisplayId}}"}"""),
            CancellationToken.None);
        var child = _f.Tasks.List(Project.Id, templatesOnly: true).Single(t => t.Title == "Прогон тестов");
        Assert.Equal(head.Id, child.ParentId);

        var list = await tools.ExecuteAsync("list_templates", Args("{}"), CancellationToken.None);
        Assert.Contains(head.DisplayId, list);
        Assert.Contains("Прогон тестов", list);

        var updated = await tools.ExecuteAsync("update_template",
            Args($$"""{"code":"{{child.DisplayId}}","description":"dotnet test --filter"}"""),
            CancellationToken.None);
        Assert.Contains("обновлён", updated);
        var reloaded = _f.Tasks.Get(child.Id)!;
        Assert.Equal("dotnet test --filter", _f.Tasks.ReadDescription(reloaded));
        Assert.Equal("Прогон тестов", reloaded.Title);   // не переданное поле не меняется

        // узел шаблона теперь читается и обычным чтением задания
        var card = await tools.ExecuteAsync("get_task_by_code",
            Args($$"""{"code":"{{head.DisplayId}}"}"""), CancellationToken.None);
        Assert.Contains("УЗЕЛ ШАБЛОНА", card);
        Assert.Contains("поднять билд", card);
    }

    [Fact]
    public async Task Unknown_Parent_Template_Is_Reported()
    {
        var answer = await Tools(ProjectTask()).ExecuteAsync("create_template",
            Args("""{"title":"X","description":"д","parent":"T-999"}"""), CancellationToken.None);

        Assert.StartsWith("Ошибка", answer);
        Assert.Empty(_f.Tasks.List(Project.Id, templatesOnly: true));
    }

    // ---------- публикация инструментов ----------

    [Fact]
    public void Tools_Are_Published_To_Project_Task_And_Hidden_Outside_Project()
    {
        var inProject = Agent(ProjectTask());
        foreach (var name in new[]
                 { "create_experience", "update_experience", "list_templates", "create_template", "update_template" })
        {
            Assert.Contains(inProject.Specs, s => s.Name == name);
        }

        // задача вне проекта и не из шаблона: писать опыт некуда, шаблонов нет
        var loose = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files,
                _f.Tasks.Create(new TaskItem { Title = "Без проекта" }, "т", "", null),
                _f.RefData, _f.Picker, null, _f.Experience));
        Assert.DoesNotContain(loose.Specs, s => s.Name == "create_experience");
        Assert.DoesNotContain(loose.Specs, s => s.Name == "create_template");
        Assert.Equal("", loose.ExperienceNote);
    }

    [Fact]
    public void Prompt_Note_Explains_Project_Experience_And_Templates()
    {
        var note = Tools(ProjectTask()).ExperienceNote;

        Assert.Contains("Опыт ПРОЕКТА", note);
        Assert.Contains("scope=\"project\"", note);
        Assert.Contains("list_templates", note);
        Assert.Contains("прямо просят", note);   // без просьбы человека агент не лезет
    }

    [Fact]
    public void Actions_Are_Seeded_For_Security_Rules()
    {
        Assert.Equal("AI2P.Templates.List", _f.Actions.CodeByTool("list_templates"));
        Assert.Equal("AI2P.Templates.Create", _f.Actions.CodeByTool("create_template"));
        Assert.Equal("AI2P.Templates.Update", _f.Actions.CodeByTool("update_template"));
        // промпт действия берётся из справочника, а не из кода
        Assert.Contains("scope", _f.Actions.PromptByTool("create_experience", "ru"));
    }

    // ---------- маркеры CLI-агента ----------

    [Fact]
    public void Cli_Notes_Describe_Both_Markers()
    {
        var tools = Agent(ProjectTask());

        var experience = tools.CliExperienceNote(ExpMarker);
        Assert.Contains(ExpMarker, experience);
        Assert.Contains("scope", experience);
        var templates = tools.CliTemplateNote(TemplateMarker, "AI2P_GET_TASK");
        Assert.Contains(TemplateMarker, templates);
        Assert.Contains("AI2P_GET_TASK {\"templates\": true}", templates);
    }

    [Fact]
    public async Task Cli_Markers_Create_Template_And_Its_Experience()
    {
        var task = ProjectTask();
        var tools = Agent(task);
        var text = "Опыт перенесён в AI2P.\n"
                   + TemplateMarker + " {\"title\": \"Выпуск версии\", \"description\": \"как выпускать\"}\n"
                   + ExpMarker + " {\"text\": \"билд правится руками\", \"scope\": \"template\", "
                   + "\"template\": \"Выпуск версии\"}\n"
                   + ExpMarker + " {\"text\": \"полный прогон ~8 минут\", \"skill\": \"code-test\"}\n"
                   + "Готово.";

        var templateMarkers = ClaudeCliConnector.ParseMarkers(TemplateMarker, text, out var afterTemplates);
        var templateSummary = await ClaudeCliConnector.ApplyMarkersAsync(templateMarkers, tools,
            "## Шаблоны", _ => "create_template", CancellationToken.None);
        var expMarkers = ClaudeCliConnector.ParseMarkers(ExpMarker, afterTemplates, out var cleaned);
        var expSummary = await ClaudeCliConnector.ApplyMarkersAsync(expMarkers, tools,
            "## Опыт", _ => "create_experience", CancellationToken.None);

        var node = _f.Tasks.List(Project.Id, templatesOnly: true).Single();
        Assert.Equal("билд правится руками", _f.Experience.ListByTemplate(node.Id).Single().Text);
        var projectRecord = Assert.Single(_f.Experience.ListByProject(Project.Id));
        Assert.Equal("code-test", projectRecord.SkillName);
        Assert.Contains(node.DisplayId, templateSummary);
        Assert.Contains("Опыт", expSummary);
        Assert.Equal("Опыт перенесён в AI2P.\nГотово.", cleaned);
    }

    [Fact]
    public async Task Denied_Rule_Hides_Note_And_Rejects_Marker()
    {
        List<SecurityRule> deny =
        [
            new()
            {
                Target = SecurityTarget.Action,
                Permission = SecurityPermission.Deny,
                Pattern = "AI2P.Experience.*",
            },
        ];
        var tools = Agent(ProjectTask(), deny);

        Assert.Equal("", tools.CliExperienceNote(ExpMarker));
        var markers = ClaudeCliConnector.ParseMarkers(ExpMarker,
            ExpMarker + " {\"text\": \"запрещённая запись\"}", out _);
        var summary = await ClaudeCliConnector.ApplyMarkersAsync(markers, tools, "## Опыт",
            _ => "create_experience", CancellationToken.None);

        Assert.Contains("Запрещено правилами безопасности", summary);
        Assert.Empty(_f.Experience.ListByProject(Project.Id));
        Assert.Single(tools.SecurityTriggers);
    }

    // ---------- сквозной прогон задания через CLI-коннектор ----------

    /// <summary>Поддельный CLI (как в CliSubtaskTests): свой процесс вместо Claude Code —
    /// внешние процессы в тестах дают флаки.</summary>
    private string FakeCli(string cliJson)
    {
        var dir = Path.Combine(_f.Dir, "fake-cli");
        Directory.CreateDirectory(dir);
        var outPath = Path.Combine(dir, "out.json");
        File.WriteAllText(outPath, cliJson, new System.Text.UTF8Encoding(false));
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd, "@echo off\r\nfindstr \"^\" > nul\r\ntype \"%~dp0out.json\"\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh, "#!/bin/sh\ncat > /dev/null\ncat \"$(dirname \"$0\")/out.json\"\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Fact]
    public async Task Whole_Cli_Job_Writes_Experience_And_Cleans_Result()
    {
        var answer = "Опыт занесён.\n"
                     + ExpMarker + " {\"text\": \"опыт проекта из CLI\"}\n"
                     + "Всё.";
        var cliJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = "s-144",
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });
        const string profileRel = "models/profile_cli_t144.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{FakeCli(cliJson)}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод-cli-144", Kind = ExecutorKind.Ai, ProfilePath = profileRel,
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = Project.Id, Title = "Заполни опыт", ExecutorIds = [ai.Id],
        }, "заполни опыт проекта", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "заполни опыт проекта");
        var done = await WaitForJob(job.Id);

        Assert.Equal(JobState.Done, done.State);
        Assert.Equal("опыт проекта из CLI", _f.Experience.ListByProject(Project.Id).Single().Text);
        var result = _f.Files.ReadText(done.ResultPath!);
        Assert.DoesNotContain(ExpMarker, result);
        Assert.Contains("## Опыт", result);
        Assert.Contains("Опыт занесён.", result);
    }

    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 300; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Задание {jobId} не завершилось за 30 с");
    }
}
