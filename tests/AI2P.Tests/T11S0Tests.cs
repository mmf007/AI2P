using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-11-S0 (версия 1.102): ОБЩИЕ ПРАВИЛА РАБОТЫ — третий вид записей опыта (ТЗ п. 2.11).
///
/// Есть правила, годные для ЛЮБОГО проекта и любой организации (первое из них: «одну
/// подзадачу не заводить — делать работу в рамках текущей задачи»). Раньше их можно было
/// записать только в опыт конкретного проекта, и в новом проекте они не действовали.
///
/// Теперь запись опыта БЕЗ проекта и БЕЗ узла шаблона — общее правило организации:
/// <list type="number">
/// <item>её получает КАЖДАЯ задача — блок «Общие правила работы» промпта задания;</item>
/// <item>показывается и правится на закладке «Настройки → Общий опыт»;</item>
/// <item>агент работает с ней так же, как с остальным опытом: create_experience со
/// scope=general и update_experience по id из блока (маркер AI2P_EXPERIENCE у CLI-агента);</item>
/// <item>часть правил ЗАСЕИВАЕТСЯ организации — один раз, с отметкой в meta, чтобы удалённое
/// человеком правило не возвращалось при следующем старте.</item>
/// </list>
///
/// Отдельной колонки в таблице для этого не заводилось (схема БД не менялась): у записи
/// опыта одно из двух полей привязки заполнено всегда, и пустые оба — сам по себе признак.
/// </summary>
public sealed class T11S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private TaskToolset ToolsetFor(TaskItem current, string? actorId = null) =>
        new(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
            actorExecutorId: actorId, experience: _f.Experience);

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

    // ---------- хранилище ----------

    [Fact]
    public void A_General_Record_Belongs_To_No_Project_And_No_Template()
    {
        var owner = _f.Executors.EnsureLocalOwner();
        var record = _f.Experience.CreateGeneral("одну подзадачу не заводить", owner.Id);

        Assert.True(record.IsGeneral);
        Assert.False(record.IsProjectLevel);
        Assert.Equal("", record.TemplateTaskId);
        Assert.Null(record.ProjectId);
        Assert.Equal(owner.Id, record.CreatedBy);

        Assert.Single(_f.Experience.ListGeneral());
        Assert.Equal(record.Id, _f.Experience.ListGeneral()[0].Id);
        // событие журнала помечено своей областью
        var recorded = _f.Events.Query(eventType: EventTypes.ExperienceRecorded).Single();
        Assert.Contains("\"scope\":\"general\"", recorded.PayloadJson);
        Assert.Null(recorded.TaskId);      // ни узла шаблона,
        Assert.Null(recorded.ProjectId);   // ни проекта у события нет
    }

    [Fact]
    public void General_Records_Do_Not_Mix_With_Project_And_Template_Experience()
    {
        var project = _f.Projects.Create("Проект", Path.Combine(_f.Dir, "prj"), null, null);
        var template = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        _f.Experience.CreateForProject(project.Id, "опыт проекта", null);
        _f.Experience.Create(template.Id, "опыт узла", null);
        var general = _f.Experience.CreateGeneral("общее правило", null);

        Assert.DoesNotContain(_f.Experience.ListByProject(project.Id), r => r.Id == general.Id);
        Assert.DoesNotContain(_f.Experience.ListByTemplate(template.Id, includeSubtree: true),
            r => r.Id == general.Id);
        Assert.Single(_f.Experience.ListGeneral());

        // ГЛАВНОЕ: общее правило не осиротевшая запись (узла у него нет по определению),
        // иначе оно попало бы в список «убрать» вкладки серверов
        Assert.Empty(_f.Experience.ListOrphans());
    }

    [Fact]
    public void A_General_Record_Is_Edited_And_Deleted_Like_The_Rest()
    {
        var owner = _f.Executors.EnsureLocalOwner();
        var record = _f.Experience.CreateGeneral("черновик правила", null);

        var updated = _f.Experience.Update(record.Id, "правило целиком", owner.Id);
        Assert.Equal("правило целиком", updated.Text);
        Assert.True(updated.IsGeneral);
        Assert.Equal(owner.Id, updated.UpdatedBy);

        _f.Experience.Delete(record.Id, owner.Id);
        Assert.Empty(_f.Experience.ListGeneral());
        Assert.Single(_f.Events.Query(eventType: EventTypes.ExperienceDeleted));
    }

    [Fact]
    public void A_Skill_On_A_General_Record_Narrows_Its_Readers()
    {
        _f.RefData.Seed();
        var coding = _f.RefData.Skills().First(s => s.Name == "code-write");
        // «для всех» с T-29-S0 означает пометку «загружать всегда» (T-24-S0): прежнее
        // правило «нет навыка ⇒ читают все» ею и заменено
        _f.Experience.CreateGeneral("правило для всех", null, alwaysLoad: true);
        _f.Experience.CreateGeneral("правило кодировщику", null, coding.Id);

        Assert.Equal(2, _f.Experience.ListGeneral().Count);
        Assert.Equal(2, _f.Experience.ListGeneralForExecutor(["code-write-cs"]).Count);
        Assert.Single(_f.Experience.ListGeneralForExecutor(["image-generate"]));
        Assert.Single(_f.Experience.ListGeneralForExecutor([]));   // навыков нет — только общие
        // фильтр списка (закладка настроек) работает так же, как у опыта проекта
        Assert.Equal(2, _f.Experience.ListGeneral([coding.Id]).Count);
    }

    // ---------- первичное наполнение организации ----------

    [Fact]
    public void The_Organization_Is_Seeded_With_The_General_Rules_Once()
    {
        var added = _f.Experience.SeedGeneral();
        Assert.Equal(ExperienceService.GeneralSeedKeys.Length, added);
        var seeded = _f.Experience.ListGeneral();
        Assert.Equal(ExperienceService.GeneralSeedKeys.Length, seeded.Count);
        // засеяно ТЕКСТОМ словаря, а не ключом
        Assert.Equal(Loc.T(ExperienceService.GeneralSeedKeys[0]), seeded[0].Text);
        Assert.Contains("подзадач", seeded[0].Text);
        Assert.Equal(ExperienceService.GeneralSeedKeys.Length.ToString(),
            _f.Db.Meta(ExperienceService.GeneralSeedMetaKey));

        // повторный старт организации ничего не добавляет
        Assert.Equal(0, _f.Experience.SeedGeneral());
        Assert.Equal(ExperienceService.GeneralSeedKeys.Length, _f.Experience.ListGeneral().Count);
    }

    [Fact]
    public void A_Rule_Deleted_By_A_Human_Does_Not_Come_Back()
    {
        _f.Experience.SeedGeneral();
        var rule = _f.Experience.ListGeneral().First();
        _f.Experience.Delete(rule.Id, null);

        Assert.Equal(0, _f.Experience.SeedGeneral());
        Assert.Equal(ExperienceService.GeneralSeed.Length - 1, _f.Experience.ListGeneral().Count);
        // и отметку в meta потеряли — правило всё равно не возвращается: строка с этим
        // идентификатором в таблице есть, просто удалена (T-30-S0)
        _f.Db.SetMeta(ExperienceService.GeneralSeedMetaKey, "0");
        Assert.Equal(0, _f.Experience.SeedGeneral());
        Assert.Equal(ExperienceService.GeneralSeed.Length - 1, _f.Experience.ListGeneral().Count);
    }

    [Fact]
    public void A_Rule_That_Arrived_By_Replication_Is_Not_Seeded_Twice()
    {
        // сервер, ставший дирижёром позже, отметки в своей meta не имеет, а правило у него
        // уже есть — приехало журналом изменений. Второй комплект правил заводить нельзя
        _f.Experience.CreateGeneral(Loc.T(ExperienceService.GeneralSeedKeys[0]), null);
        // первое правило узнаётся по тексту и второй раз не заводится, остальные заводятся
        Assert.Equal(ExperienceService.GeneralSeed.Length - 1, _f.Experience.SeedGeneral());
        Assert.Equal(ExperienceService.GeneralSeed.Length, _f.Experience.ListGeneral().Count);
    }

    [Fact]
    public void Seeded_Rules_Keep_Their_Identifiers_And_Go_To_Every_Task()
    {
        // ФИКСИРОВАННЫЙ идентификатор (T-30-S0, по образцу справочника моделей T-227): сеет
        // каждый сервер сам, а записи реплицируются — со случайным у организации копился бы
        // второй комплект правил, по одному на сервер
        _f.Experience.SeedGeneral();
        var seeded = _f.Experience.ListGeneral();
        Assert.Equal(ExperienceService.GeneralSeed.Select(r => r.Id).ToArray(),
            seeded.Select(r => r.Id).ToArray());
        // «загружать всегда»: правило без навыка иначе не попало бы в задание вовсе (T-29-S0)
        Assert.All(seeded, r => Assert.True(r.AlwaysLoad));
        Assert.All(seeded, r => Assert.Null(r.ServerId));
        // время правки постоянное — иначе партнёр объявил бы спор на каждом правиле (T-227)
        Assert.Equal(ExperienceService.SeedStamp, seeded[0].UpdatedAt);
        // без навыка и без тэгов — а в задание всё равно идут, любому исполнителю
        Assert.Equal(seeded.Count, _f.Experience.ListGeneralForExecutor([], ["сборка"]).Count);
    }

    // ---------- промпт задания ----------

    [Fact]
    public void The_Task_Prompt_Carries_The_General_Rules()
    {
        var record = _f.Experience.CreateGeneral("одну подзадачу не заводить", null, alwaysLoad: true);

        var block = JobOrchestrator.GeneralExperienceBlock(_f.Experience, []);
        Assert.Contains("Общие правила работы", block);
        Assert.Contains("одну подзадачу не заводить", block);
        Assert.Contains(record.Id, block);   // id — для update_experience

        // записей нет — блока нет вовсе (пустой заголовок в промпте только мешает)
        _f.Experience.Delete(record.Id, null);
        Assert.Equal("", JobOrchestrator.GeneralExperienceBlock(_f.Experience, []));
        Assert.Equal("", JobOrchestrator.GeneralExperienceBlock(null, []));
    }

    [Fact]
    public void The_Rules_Reach_A_Task_Outside_Any_Project()
    {
        // задача вне проекта и не из шаблона: опыта проекта у неё нет, а общие правила есть
        _f.Experience.CreateGeneral("правило для всех задач", null, alwaysLoad: true);
        var task = _f.Tasks.Create(new TaskItem { Title = "Без проекта" }, "", "", null);

        var request = (string)typeof(JobOrchestrator)
            .GetMethod("BuildAiRequest", System.Reflection.BindingFlags.NonPublic
                                         | System.Reflection.BindingFlags.Instance)!
            .Invoke(_f.Orchestrator, [task])!;

        Assert.Contains("## Общие правила работы", request);
        Assert.Contains("правило для всех задач", request);
    }

    // ---------- инструменты агента ----------

    [Fact]
    public async Task An_Agent_Writes_And_Corrects_A_General_Rule()
    {
        var project = _f.Projects.Create("Проект", Path.Combine(_f.Dir, "prj"), null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Работа", ProjectId = project.Id }, "", "", null);
        var agent = _f.Executors.Create(new Executor { Nick = "bot-11s0", Kind = ExecutorKind.Human }, null);
        var tools = ToolsetFor(task, agent.Id);

        var created = await tools.ExecuteAsync("create_experience",
            Args("""{"text":"одну подзадачу не заводить","scope":"general"}"""),
            CancellationToken.None);
        Assert.Contains("Общее правило", created);
        var record = _f.Experience.ListGeneral().Single();
        Assert.Equal("одну подзадачу не заводить", record.Text);
        Assert.Equal(agent.Id, record.CreatedBy);
        // в опыт проекта такая запись не попадает
        Assert.Empty(_f.Experience.ListByProject(project.Id));

        // правка идёт тем же update_experience по id из блока задания
        var updated = await tools.ExecuteAsync("update_experience",
            Args($$"""{"id":"{{record.Id}}","text":"правило уточнено"}"""), CancellationToken.None);
        Assert.Contains("обновлена", updated);
        Assert.Equal("правило уточнено", _f.Experience.Get(record.Id)!.Text);
    }

    [Fact]
    public async Task An_Unknown_Scope_Names_All_Three_Areas()
    {
        var project = _f.Projects.Create("Проект", Path.Combine(_f.Dir, "prj"), null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Работа", ProjectId = project.Id }, "", "", null);
        var answer = await ToolsetFor(task).ExecuteAsync("create_experience",
            Args("""{"text":"куда-то","scope":"org"}"""), CancellationToken.None);

        Assert.StartsWith("Ошибка", answer);
        Assert.Contains("project", answer);
        Assert.Contains("template", answer);
        Assert.Contains("general", answer);
    }

    [Fact]
    public void The_Prompt_Notes_Tell_About_The_General_Rules()
    {
        var project = _f.Projects.Create("Проект", Path.Combine(_f.Dir, "prj"), null, null);
        var task = _f.Tasks.Create(new TaskItem { Title = "Работа", ProjectId = project.Id }, "", "", null);
        var tools = ToolsetFor(task);

        Assert.Contains("ОБЩИЕ ПРАВИЛА РАБОТЫ", tools.ExperienceNote);
        Assert.Contains("scope=\"general\"", tools.ExperienceNote);
        // CLI-агенту то же самое рассказывается маркером — и все три области названы
        var marker = tools.ExperienceMarkerNote("AI2P_EXPERIENCE:");
        Assert.Contains("AI2P_EXPERIENCE:", marker);
        Assert.Contains("general", marker);
        Assert.Contains("project", marker);
        Assert.Contains("template", marker);
    }

    // ---------- разметка и словари ----------

    [Fact]
    public void The_Settings_Screen_Has_The_General_Experience_Tab()
    {
        var settings = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "SettingsView.razor"));
        Assert.Contains("settings.tab.experience", settings);
        Assert.Contains("<ExperienceTab General=\"true\"", settings);

        var tab = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI",
            "Components", "ExperienceTab.razor"));
        Assert.Contains("data-experience-scope", tab);
        Assert.Contains("GetGeneralExperienceAsync", tab);
    }

    [Fact]
    public void Both_Dictionaries_Hold_The_New_Texts()
    {
        var i18n = Path.Combine(RepoRoot(), "AI2P_app", "i18n");
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(Path.Combine(i18n, $"{lang}.json"));
            var keys = JsonDocument.Parse(text).RootElement;
            // ключи ВСЕХ правил дистрибутива обязаны быть в обоих словарях (T-30-S0): текст
            // берётся по языку установки, и без ключа сид молча не заводит ничего
            foreach (var key in new[]
                     {
                         "settings.tab.experience", "exp.general.title", "exp.general.hint",
                         "exp.empty.general", "prompt.job.21", "prompt.job.22",
                         "prompt.tasks.122", "prompt.tasks.123", "prompt.tasks.124",
                     }.Concat(ExperienceService.GeneralSeedKeys))
            {
                Assert.True(keys.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пуст {key}");
            }
        }
    }
}
