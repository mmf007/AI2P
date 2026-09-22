using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-269-S0 (ветка T-318 «Проект опыта»): РАЗГРАНИЧЕНИЕ ОБЛАСТЕЙ И ПЕРЕНОС ЗАПИСИ.
///
/// Записи трёх областей (общие правила организации / опыт проекта / опыт узла шаблона)
/// смешивались, а исправить это было НЕЧЕМ: перенести запись не умели ни сервис, ни API,
/// ни UI — только удалить и завести заново, потеряв id, историю и авторство (а чужую
/// нельзя и удалить). Проверяется:
/// <list type="number">
/// <item>перенос сохраняет идентификатор, текст, тэги, навык и авторство и меняет область
/// во всех трёх направлениях;</item>
/// <item>перенесённая запись перестаёт приходить задачам ПРЕЖНЕЙ области и начинает
/// приходить задачам новой;</item>
/// <item>чужую (созданную другим сервером) запись перенести нельзя;</item>
/// <item>поставляемое правило дистрибутива перенести нельзя;</item>
/// <item>проверка «выглядит проектным» ловит код задачи, путь файла и имя проекта
/// и не ругается на нормальное общее правило — в том числе на поставляемые;</item>
/// <item>ревизия общего опыта перечисляет ровно проектные записи;</item>
/// <item>у инструмента <c>move_experience</c> ЕСТЬ запись справочника действий во всех пяти
/// языках — без неё правила безопасности его не закрывают вовсе (наука 2d3af8da);</item>
/// <item>правило «общее / проект / узел шаблона» сформулировано одной фразой в промпте
/// агента и в форме записи — во всех пяти словарях.</item>
/// </list>
/// </summary>
public sealed class T269S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string OtherServerId = "unid-S1";

    private static ServerScope OtherScope =>
        new(() => OtherServerId, () => "S1", () => false,
            codeOf: id => id == OtherServerId ? "S1" : "",
            nameOf: id => id == OtherServerId ? "ноутбук" : "");

    private Project NewProject() =>
        _f.Projects.Create("Проект T-269-S0", Path.Combine(_f.Dir, "prj269"), null, null);

    private TaskItem NewTemplate() =>
        _f.Tasks.Create(new TaskItem { Title = "Узел шаблона", IsTemplate = true }, "", "", null);

    private string SkillId(string code) => _f.RefData.Skills().First(s => s.Name == code).Id;

    private static string I18nDir => Path.Combine(AppContext.BaseDirectory, "i18n");

    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    // ---------- перенос ----------

    [Fact]
    public void A_Move_Keeps_The_Id_The_Tags_The_Skill_And_The_Authorship()
    {
        var project = NewProject();
        var owner = _f.Executors.Create(new Executor { Nick = "Билл" }, null);
        var record = _f.Experience.CreateForProject(project.Id, "вывод про сборку", owner.Id,
            SkillId("code-write"), alwaysLoad: true, tags: ["сборка", "выпуск"]);

        var moved = _f.Experience.Move(record.Id, ExperienceScopes.General, null, null, owner.Id);

        Assert.Equal(record.Id, moved.Id);                 // ТОТ ЖЕ идентификатор
        Assert.Equal("вывод про сборку", moved.Text);
        Assert.Equal(["выпуск", "сборка"], moved.Tags);    // тэги читаются ORDER BY tag
        Assert.Equal("code-write", moved.SkillName);
        Assert.Equal(owner.Id, moved.CreatedBy);           // авторство — прежнее
        Assert.True(moved.AlwaysLoad);
        Assert.True(moved.IsActive);
        Assert.Equal(record.CreatedAt, moved.CreatedAt);
        Assert.True(moved.IsGeneral);
        // событие журнала у переноса своё: «запись изменена» не сказало бы главного —
        // что сменился её адресат
        Assert.NotEmpty(_f.Events.Query(eventType: EventTypes.ExperienceMoved));
    }

    [Fact]
    public void A_Record_Walks_All_Three_Scopes()
    {
        var project = NewProject();
        var template = NewTemplate();
        var record = _f.Experience.CreateGeneral("правило, заведённое не туда", null);

        var toProject = _f.Experience.Move(record.Id, ExperienceScopes.Project, project.Id, null, null);
        Assert.True(toProject.IsProjectLevel);
        Assert.Equal(project.Id, toProject.ProjectId);
        Assert.Equal("", toProject.TemplateTaskId);

        var toNode = _f.Experience.Move(record.Id, ExperienceScopes.Template, null, template.Id, null);
        Assert.False(toNode.IsProjectLevel);
        Assert.False(toNode.IsGeneral);
        Assert.Equal(template.Id, toNode.TemplateTaskId);
        Assert.Null(toNode.ProjectId);

        var back = _f.Experience.Move(record.Id, ExperienceScopes.General, null, null, null);
        Assert.True(back.IsGeneral);
        Assert.Equal(record.Id, back.Id);
    }

    [Fact]
    public void The_Moved_Record_Leaves_The_Old_Audience_And_Joins_The_New_One()
    {
        var project = NewProject();
        var template = NewTemplate();
        string[] owned = ["code-write"];
        var record = _f.Experience.CreateGeneral("вывод, годный только этому проекту", null,
            SkillId("code-write"));

        Assert.Single(_f.Experience.ListGeneralForExecutor(owned));
        Assert.Empty(_f.Experience.ListForExecutor(project.Id, owned));

        _f.Experience.Move(record.Id, ExperienceScopes.Project, project.Id, null, null);
        Assert.Empty(_f.Experience.ListGeneralForExecutor(owned));
        Assert.Equal(record.Id, _f.Experience.ListForExecutor(project.Id, owned).Single().Id);

        _f.Experience.Move(record.Id, ExperienceScopes.Template, null, template.Id, null);
        Assert.Empty(_f.Experience.ListForExecutor(project.Id, owned));
        Assert.Equal(record.Id,
            _f.Experience.ListByTemplateForExecutor(template.Id, owned).Single().Id);
    }

    [Fact]
    public void A_Move_Needs_A_Live_Project_And_A_Live_Template_Node()
    {
        var record = _f.Experience.CreateGeneral("правило про отчётность", null);
        // проект-получатель обязателен и должен существовать
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Move(record.Id, ExperienceScopes.Project, null, null, null));
        Assert.Throws<InvalidOperationException>(() =>
            _f.Experience.Move(record.Id, ExperienceScopes.Project, "нет-такого", null, null));
        // узел шаблона — тоже, и он обязан быть ШАБЛОНОМ, а не обычной задачей
        var plain = _f.Tasks.Create(new TaskItem { Title = "Обычная задача" }, "", "", null);
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Move(record.Id, ExperienceScopes.Template, null, plain.Id, null));
        // и незнакомая область — понятный отказ, а не молчаливое «никуда»
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Move(record.Id, "всюду", null, null, null));
        Assert.True(_f.Experience.Get(record.Id)!.IsGeneral);
    }

    [Fact]
    public void A_Record_Of_Another_Server_Cannot_Be_Moved()
    {
        var project = NewProject();
        var mine = _f.Experience.CreateForProject(project.Id, "моя запись", null);

        // мусор, приехавший с другого сервера, чистится ТОЛЬКО на нём (решение заказчика):
        // EnsureCanWrite бросает ArgumentException, а не InvalidOperationException
        var alien = new ExperienceService(_f.Db, _f.Events, OtherScope);
        Assert.Throws<ArgumentException>(() =>
            alien.Move(mine.Id, ExperienceScopes.General, null, null, null));
        Assert.True(_f.Experience.Get(mine.Id)!.IsProjectLevel);
    }

    [Fact]
    public void A_Rule_Shipped_With_The_Distribution_Cannot_Be_Moved()
    {
        var project = NewProject();
        _f.Experience.SeedGeneral();
        var seeded = _f.Experience.ListGeneral()
            .First(r => ExperienceService.IsDistributionRule(r.Id));

        // его заводит КАЖДЫЙ сервер сам по постоянному идентификатору: перенеси — и на
        // следующем старте правило появится в общих снова, вторым экземпляром
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Move(seeded.Id, ExperienceScopes.Project, project.Id, null, null));
        Assert.True(_f.Experience.Get(seeded.Id)!.IsGeneral);
        // погасить его при этом МОЖНО — это и есть способ убрать ненужное правило (T-265-S0)
        Assert.False(_f.Experience.SetActive(seeded.Id, false, null).IsActive);
    }

    // ---------- проверка области ----------

    [Fact]
    public void The_Check_Catches_A_Task_Code_A_File_Path_And_A_File_Name()
    {
        Assert.Equal(ExperienceScopeCheck.KindTaskCode,
            ExperienceScopeCheck.Signs("правку сделали в T-241, см. отчёт")[0].Kind);
        Assert.Contains(ExperienceScopeCheck.Signs("правку сделали в T-12-S0"),
            s => s.Kind == ExperienceScopeCheck.KindTaskCode);
        Assert.Contains(ExperienceScopeCheck.Signs("лежит в src/AI2P.Core/Loc.cs"),
            s => s.Kind == ExperienceScopeCheck.KindPath);
        Assert.Contains(ExperienceScopeCheck.Signs("каталог C:\\ai\\AI2P всегда на месте"),
            s => s.Kind == ExperienceScopeCheck.KindPath);
        Assert.Contains(ExperienceScopeCheck.Signs("правится ApiEndpoints.cs целиком"),
            s => s.Kind == ExperienceScopeCheck.KindExtension);
        // имя проекта — четвёртый признак, и его знает только сервер
        Assert.Contains(ExperienceScopeCheck.Signs("в Планировщике так делать нельзя",
                ["Планировщик"]),
            s => s.Kind == ExperienceScopeCheck.KindName);
        // короткое имя признаком не считается вовсе: «АИ» нашлось бы внутри любого слова
        Assert.Empty(ExperienceScopeCheck.Signs("работаем по процессу", ["АИ"]));
    }

    [Fact]
    public void A_Normal_General_Rule_Passes_The_Check()
    {
        Assert.Empty(ExperienceScopeCheck.Signs(
            "В отчёте всегда отдельным разделом писать, что НЕ сделано: умолчание "
            + "о непроверенном дороже признания."));
        Assert.Empty(ExperienceScopeCheck.Signs(
            "Список требуемых проверок пиши в отчёт всегда — и когда прогнал их сам, "
            + "и когда не гонял ничего."));
        // и ПОСТАВЛЯЕМЫЕ правила дистрибутива проходят её все: в seed.experience.2 стоит
        // «Время/качество», и правило «путь — это два звена через косую» ловило бы его
        foreach (var key in ExperienceService.GeneralSeedKeys)
        {
            foreach (var lang in Langs)
            {
                var text = Loc.In(lang, key);
                Assert.True(ExperienceScopeCheck.Signs(text).Count == 0,
                    $"поставляемое правило {key} ({lang}) не проходит свою же проверку: "
                    + ExperienceScopeCheck.Samples(ExperienceScopeCheck.Signs(text)));
            }
        }
    }

    [Fact]
    public void A_Project_Looking_Text_Does_Not_Get_Into_The_General_Rules()
    {
        // агенту — отказ: молчаливая подмена области сбила бы его, он потом ищет запись по id
        var refused = Assert.Throws<ArgumentException>(() =>
            _f.Experience.CreateGeneral("в T-241 выяснилось, что publish.ps1 падает", null));
        Assert.Contains("T-241", refused.Message);
        Assert.Empty(_f.Experience.ListGeneral());

        // человек подтверждает — и запись заводится (force ставит кнопка «всё равно сохранить»)
        var forced = _f.Experience.CreateGeneral("в T-241 выяснилось, что publish.ps1 падает",
            null, force: true);
        Assert.True(forced.IsGeneral);

        // ПРАВКОЙ проверку тоже не обойти: завели пустое правило и дописали проектное
        var plain = _f.Experience.CreateGeneral("правило про дисциплину проверок", null);
        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Update(plain.Id, "чинится в src/AI2P.Core/Loc.cs", null));
        Assert.Equal("правило про дисциплину проверок", _f.Experience.Get(plain.Id)!.Text);

        // а в опыте ПРОЕКТА код задачи и путь файла как раз уместны — там проверки нет
        var project = NewProject();
        Assert.NotNull(_f.Experience.CreateForProject(project.Id,
            "в T-241 выяснилось, что publish.ps1 падает", null));
    }

    [Fact]
    public void The_Move_Into_The_General_Rules_Is_Checked_The_Same_Way()
    {
        var project = NewProject();
        var record = _f.Experience.CreateForProject(project.Id,
            "ключ лежит в config.json рядом с программой", null);

        Assert.Throws<ArgumentException>(() =>
            _f.Experience.Move(record.Id, ExperienceScopes.General, null, null, null));
        Assert.True(_f.Experience.Get(record.Id)!.IsProjectLevel);

        // с подтверждением человека — переносится
        Assert.True(_f.Experience.Move(record.Id, ExperienceScopes.General, null, null, null,
            force: true).IsGeneral);
    }

    // ---------- ревизия общего опыта ----------

    [Fact]
    public void The_Review_Lists_Exactly_The_Project_Looking_Rules()
    {
        var project = NewProject();
        _f.Experience.SeedGeneral();
        var clean = _f.Experience.CreateGeneral("правило про отчётность", null);
        var dirty = _f.Experience.CreateGeneral("см. src/AI2P.Core/Loc.cs", null, force: true);
        var byName = _f.Experience.CreateGeneral("в Проект T-269-S0 так нельзя", null, force: true);
        // запись опыта ПРОЕКТА в ревизию общего не попадает — она уже на своём месте
        _f.Experience.CreateForProject(project.Id, "правка в ApiEndpoints.cs", null);

        var suspects = _f.Experience.GeneralSuspects();
        var ids = suspects.Select(s => s.Record.Id).ToList();
        Assert.Contains(dirty.Id, ids);
        Assert.Contains(byName.Id, ids);
        Assert.DoesNotContain(clean.Id, ids);
        // поставляемые правила в ревизии не висят: перенести их всё равно нельзя
        Assert.DoesNotContain(ids, ExperienceService.IsDistributionRule);
        Assert.All(suspects, row => Assert.NotEmpty(row.Signs));
        Assert.Contains(suspects.Single(s => s.Record.Id == byName.Id).Signs,
            s => s.Kind == ExperienceScopeCheck.KindName);

        // перенос пачкой (кнопка ревизии зовёт перенос по одной записи)
        foreach (var id in ids)
        {
            _f.Experience.Move(id, ExperienceScopes.Project, project.Id, null, null);
        }
        Assert.Empty(_f.Experience.GeneralSuspects());
        Assert.Equal(3, _f.Experience.ListByProject(project.Id).Count);
    }

    // ---------- справочник действий и словари ----------

    [Fact]
    public void The_Move_Tool_Has_Its_Action_Catalog_Row_In_All_Five_Languages()
    {
        // ИНСТРУМЕНТ БЕЗ ЗАПИСИ СПРАВОЧНИКА правилами безопасности не закрывается ВОВСЕ:
        // CodeByTool вернёт null, и Authorize разрешит вызов (наука 2d3af8da)
        foreach (var lang in Langs)
        {
            var file = Path.Combine(I18nDir, $"ActionCatalogService_{lang}.json");
            using var seed = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(seed.RootElement.GetProperty("seedVersion").GetInt32() >= 23,
                $"seedVersion в ActionCatalogService_{lang}.json не поднят");
            var row = seed.RootElement.GetProperty("actions").EnumerateArray()
                .FirstOrDefault(a => a.TryGetProperty("tool", out var t)
                                     && t.ValueKind == JsonValueKind.String
                                     && t.GetString() == "move_experience");
            Assert.True(row.ValueKind == JsonValueKind.Object,
                $"в ActionCatalogService_{lang}.json нет записи на инструмент move_experience");
            Assert.Equal("AI2P.Experience.Move", row.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("prompt").GetString()));
        }

        _f.Actions.Seed();
        Assert.Equal("AI2P.Experience.Move", _f.Actions.CodeByTool("move_experience"));
    }

    [Fact]
    public void The_Rule_Of_Three_Scopes_Is_Spelled_Out_In_All_Five_Dictionaries()
    {
        foreach (var lang in Langs)
        {
            // форма записи и окно переноса
            Assert.False(string.IsNullOrWhiteSpace(Loc.In(lang, "exp.scope.rule")));
            Assert.NotEqual("exp.scope.rule", Loc.In(lang, "exp.scope.rule"));
            // промпт агента: опыт проекта и общие правила — оба ключа названы в задании
            foreach (var key in new[] { "prompt.tasks.78", "prompt.tasks.122" })
            {
                var text = Loc.In(lang, key);
                Assert.Contains("move_experience", text);
                Assert.NotEqual(key, text);
            }
        }
        // правило в русском тексте названо теми же словами, что в задании заказчика
        Assert.Contains("КАК МЫ РАБОТАЕМ", Loc.In("ru", "exp.scope.rule"));
        Assert.Contains("HOW WE WORK", Loc.In("en", "exp.scope.rule"));
    }

    [Fact]
    public void The_Cli_Has_The_Experience_Move_Subcommand()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "src", "AI2P.Server",
            "Cli", "AgentCliRunner.cs"));
        Assert.Contains("\"experience-move\"", source);
        Assert.Contains("move_experience", source);
        foreach (var lang in Langs)
        {
            Assert.NotEqual("msg.agentCli.46", Loc.In(lang, "msg.agentCli.46"));
        }
    }

    /// <summary>Корень дерева исходников — от каталога сборки тестов вверх до AI2P_app.</summary>
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AI2P.Server")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("не найден корень исходников");
    }
}
