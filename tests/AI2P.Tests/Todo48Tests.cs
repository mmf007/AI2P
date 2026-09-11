using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo48 (ТЗ v1.56, п. 2.11): ОПЫТ НА УРОВНЕ ПРОЕКТА. Записи опыта теперь бывают двух видов:
/// по узлу шаблона (todo32) и по ПРОЕКТУ — обобщение работы, которое получает любая задача
/// проекта. У записи появился НАВЫК: агент читает только опыт по своим навыкам —
/// медиа-модели незачем читать выводы кодировщиков. Опыт реплицируется между серверами.
/// </summary>
public sealed class Todo48Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Project CreateProject(string name = "Проект") =>
        _f.Projects.Create(name, Path.Combine(_f.Dir, "prj"), null, null);

    private string SkillId(string code) =>
        _f.RefData.Skills().First(s => s.Name == code).Id;

    /// <summary>ИИ-исполнитель с декларацией возможностей: перечисленные навыки — его.</summary>
    private Executor CreateAi(string nick, params string[] skills)
    {
        var entries = string.Join(",", skills.Select(s => $"{{\"name\":\"{s}\",\"score\":80}}"));
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, $"{{\"skills\":[{entries}]}}");
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
        }, null);
    }

    // ---------- записи опыта проекта ----------

    [Fact]
    public void Project_Experience_Is_Created_Listed_And_Deleted()
    {
        var project = CreateProject();
        var owner = _f.Executors.EnsureLocalOwner();

        var record = _f.Experience.CreateForProject(project.Id, "дизайнер отдаёт psd, а не png",
            owner.Id, SkillId("image-concept"));

        Assert.True(record.IsProjectLevel);
        Assert.Equal(project.Id, record.ProjectId);
        Assert.Equal("", record.TemplateTaskId);
        Assert.Equal(owner.Id, record.CreatedBy);

        var list = _f.Experience.ListByProject(project.Id);
        Assert.Single(list);
        Assert.Equal("image-concept", list[0].SkillName);   // код навыка приезжает из справочника

        _f.Experience.Delete(record.Id, owner.Id);
        Assert.Empty(_f.Experience.ListByProject(project.Id));

        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceRecorded));
        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceDeleted));
    }

    [Fact]
    public void Created_Record_Comes_Back_With_The_Skill_Code()
    {
        // поймано живой проверкой: код навыка знает только справочник, а созданная запись
        // возвращалась «как есть» — UI показывал новую строку без навыка до перезагрузки
        var project = CreateProject();
        var template = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);

        Assert.Equal("code-write",
            _f.Experience.CreateForProject(project.Id, "текст", null, SkillId("code-write")).SkillName);
        Assert.Equal("code-review",
            _f.Experience.Create(template.Id, "текст", null, SkillId("code-review")).SkillName);
    }

    [Fact]
    public void Project_Experience_Requires_Project_And_Text()
    {
        var project = CreateProject();

        Assert.Throws<ArgumentException>(() => _f.Experience.CreateForProject(project.Id, "   ", null));
        Assert.Throws<InvalidOperationException>(() =>
            _f.Experience.CreateForProject("нет-такого", "текст", null));
        // навык — из справочника; выдумывать коды нельзя, иначе фильтр по навыкам врёт
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.CreateForProject(project.Id, "текст", null, "нет-такого-навыка"));
    }

    [Fact]
    public void Skill_Is_Editable_And_Can_Be_Cleared()
    {
        var project = CreateProject();
        var record = _f.Experience.CreateForProject(project.Id, "текст", null, SkillId("code-write"));

        var updated = _f.Experience.Update(record.Id, "текст 2", null, SkillId("code-review"),
            changeSkill: true);
        Assert.Equal("code-review", updated.SkillName);

        var cleared = _f.Experience.Update(record.Id, "текст 3", null, null, changeSkill: true);
        Assert.Equal("", cleared.SkillName);   // общая запись — её читают все

        // правка без указания навыка (старый вызов из инструментов агента) его не трогает
        var kept = _f.Experience.Update(record.Id, "текст 4", null);
        Assert.Equal("", kept.SkillName);
    }

    [Fact]
    public void Two_Kinds_Of_Records_Do_Not_Mix()
    {
        var project = CreateProject();
        var template = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);

        _f.Experience.CreateForProject(project.Id, "опыт проекта", null);
        _f.Experience.Create(template.Id, "опыт узла", null);

        Assert.Equal("опыт проекта", Assert.Single(_f.Experience.ListByProject(project.Id)).Text);
        Assert.Equal("опыт узла", Assert.Single(_f.Experience.ListByTemplate(template.Id)).Text);
        // опыт проекта не «осиротевший»: узла шаблона у него нет по определению
        Assert.Empty(_f.Experience.ListOrphans());
    }

    // ---------- фильтр по навыкам ----------

    [Fact]
    public void List_Filters_By_Selected_Skills_And_Keeps_Common_Records()
    {
        var project = CreateProject();
        _f.Experience.CreateForProject(project.Id, "про код", null, SkillId("code-write"));
        _f.Experience.CreateForProject(project.Id, "про картинки", null, SkillId("image-concept"));
        _f.Experience.CreateForProject(project.Id, "общее правило", null);

        var filtered = _f.Experience.ListByProject(project.Id, [SkillId("code-write")]);

        Assert.Equal(2, filtered.Count);                       // выбранный навык + общая запись
        Assert.Contains(filtered, r => r.Text == "про код");
        Assert.Contains(filtered, r => r.Text == "общее правило");
        Assert.DoesNotContain(filtered, r => r.Text == "про картинки");

        // множественный выбор
        Assert.Equal(3, _f.Experience
            .ListByProject(project.Id, [SkillId("code-write"), SkillId("image-concept")]).Count);
        // фильтр не задан — всё
        Assert.Equal(3, _f.Experience.ListByProject(project.Id).Count);
    }

    // ---------- правило «читаю только свои навыки» ----------

    [Theory]
    // точное совпадение и обе стороны иерархии навыков (ТЗ v1.39: сегменты через «-»)
    [InlineData("code-write", "code-write", true)]
    [InlineData("code-write-cpp", "code-write", true)]    // умеет писать код вообще
    [InlineData("code-write", "code-write-cpp", true)]    // пишет на C++ — общий опыт полезен
    [InlineData("code-write", "code-review", false)]      // соседняя ветка
    [InlineData("image-generate", "code-write", false)]   // ради этого всё и затевалось
    [InlineData("", "code-write", true)]                  // запись без навыка — общая
    public void Skill_Matching_Follows_One_Branch(string recordSkill, string owned, bool expected) =>
        Assert.Equal(expected, ExperienceService.SkillMatches(recordSkill, [owned]));

    [Fact]
    public void Executor_Reads_Only_Experience_Of_His_Skills()
    {
        var project = CreateProject();
        _f.Experience.CreateForProject(project.Id, "про код", null, SkillId("code-write"));
        _f.Experience.CreateForProject(project.Id, "про видео", null, SkillId("video-generate"));
        // «общая» запись с T-29-S0 — это запись, помеченная «загружать всегда»: пометка
        // ЗАМЕНИЛА прежнее правило «нет навыка ⇒ читают все» (живым записям без навыка её
        // проставляет разовый перенос при обновлении схемы)
        _f.Experience.CreateForProject(project.Id, "общее", null, alwaysLoad: true);
        _f.Experience.CreateForProject(project.Id, "ничьё", null);

        var coder = _f.Experience.ListForExecutor(project.Id, ["code-write", "code-review"]);
        Assert.Equal(["про код", "общее"], coder.Select(r => r.Text));

        var media = _f.Experience.ListForExecutor(project.Id, ["video-generate"]);
        Assert.Equal(["про видео", "общее"], media.Select(r => r.Text));

        // навыков не знаем (нет декларации) — только общие записи, но не чужие
        Assert.Equal(["общее"], _f.Experience.ListForExecutor(project.Id, []).Select(r => r.Text));
        // запись без навыка и без пометки в промпт не идёт вовсе (T-29-S0): именно так
        // блок опыта и переставал расти — раньше её получал каждый исполнитель
        Assert.DoesNotContain("ничьё", coder.Select(r => r.Text));
    }

    [Fact]
    public void Declared_Skills_Are_Read_From_The_Capabilities_File()
    {
        var executor = CreateAi("coder-48", "code-write", "code-review");

        Assert.Equal(["code-write", "code-review"], _f.Picker.DeclaredSkills(executor));
        // декларации нет — пусто, а не исключение
        Assert.Empty(_f.Picker.DeclaredSkills(new Executor { Nick = "x", CapabilitiesPath = "" }));
    }

    // ---------- опыт проекта в промпте задания ----------

    [Fact]
    public void Project_Experience_Goes_Into_The_Prompt_Of_Any_Task()
    {
        var project = CreateProject();
        _f.Experience.CreateForProject(project.Id, "заказчик принимает только mp4", null,
            SkillId("video-generate"));
        // «загружать всегда» (T-24-S0) — то, чем с T-29-S0 помечается запись «для всех»
        _f.Experience.CreateForProject(project.Id, "сроки согласуются в чате", null, alwaysLoad: true);

        var block = JobOrchestrator.ProjectExperienceBlock(_f.Experience, project.Id,
            ["video-generate"]);

        Assert.Contains("Опыт проекта", block);
        Assert.Contains("заказчик принимает только mp4", block);
        Assert.Contains("сроки согласуются в чате", block);
        Assert.Contains("[video-generate]", block);   // навык виден агенту

        // у кодировщика в промпте только общая запись
        var coderBlock = JobOrchestrator.ProjectExperienceBlock(_f.Experience, project.Id, ["code-write"]);
        Assert.DoesNotContain("mp4", coderBlock);
        Assert.Contains("сроки согласуются в чате", coderBlock);

        // задача вне проекта и проект без опыта — блока нет вовсе
        Assert.Equal("", JobOrchestrator.ProjectExperienceBlock(_f.Experience, null, ["code-write"]));
        Assert.Equal("", JobOrchestrator.ProjectExperienceBlock(_f.Experience, CreateProject("Пустой").Id,
            ["code-write"]));
    }

    // ---------- репликация ----------

    [Fact]
    public void Project_Experience_Replicates()
    {
        var project = CreateProject();
        var skill = SkillId("code-write");

        var record = _f.Experience.CreateForProject(project.Id, "едет на другой сервер", null, skill);

        Assert.Contains("experience", ChangeLog.OrgTables);
        using var conn = _f.Db.Open();
        // предел выборки с запасом: сид справочников пишет в журнал и тексты по языкам
        // (skill_texts, io_format_texts, role_texts — T-191), а запись опыта идёт после них
        var change = ChangeLog.Read(conn, 0, 5000, exceptNode: "")
            .Last(c => c.Table == "experience");
        var values = ChangeLog.Parse(change.PayloadJson);

        Assert.Equal(record.Id, change.Pk);
        // новые колонки едут вместе со строкой — триггеры строятся по фактическому составу
        Assert.Equal(project.Id, values["project_id"]);
        Assert.Equal(skill, values["skill_id"]);
    }

    [Fact]
    public void Schema_Version_Is_Current()
    {
        using var conn = _f.Db.Open();
        var version = Sql.Query(conn, null, "SELECT value FROM meta WHERE key='schema_version'",
            r => r.S("value")).Single();

        // v22 — опыт проекта (todo48, эта задача); v23 — лимиты агентов (T-121);
        // v24 — таймаут ответа исполнителя (T-124); v25 — активность участника в команде (T-129);
        // v26 — автор вне системы и внешний id у сообщения чата (T-152);
        // v27 — названия справочников по языкам (T-191);
        // v28 — чего ждёт приостановленное задание, jobs.wait_kind (T-185);
        // v29 — заявки на запуск задачи с другого сервера, run_requests (T-196);
        // v30 — вторая галочка переспроса иерархии «и задачи в доработке» у заявки (T-210);
        // v31 — тэги задач, task_tags (T-222);
        // v32 — замена исполнителя: task_alt_executors и tasks.actual_executor_id (T-221);
        // v33 — ссылка импорта у задачи, tasks.import_url (T-246);
        // v34 — статус по готовности, tasks.ai_done_status (T-250);
        // v35 — объекты проекта, objects.* (T-259);
        // v36 — пер-серверная активность локальных моделей, ai_model_servers (T-8-S1);
        // v37 — заявки на остановку с другого сервера, stop_requests (T-263);
        // v38 — обучение адаптера LoRA под модель, object_loras (T-12-S1);
        // v39 — заявки на смену дирижёра, conductor_requests (T-21-S1)
        Assert.Equal("46", version);
    }
}
