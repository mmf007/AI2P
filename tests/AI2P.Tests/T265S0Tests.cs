using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-265-S0 (ветка T-318 «Проект опыта»): ПРИЗНАК АКТИВНОСТИ У ЗАПИСИ ОПЫТА.
///
/// До этого запись опыта можно было только удалить. Неактивная запись — промежуточное
/// состояние: её не видит агент, но человек видит и может вернуть:
/// <list type="number">
/// <item>колонка <c>experience.is_active</c>, схема организации v47; у ВСЕХ накопленных
/// записей она 1 — обновление ничего не гасит;</item>
/// <item>неактивная запись не идёт в задание НИ ПРИ КАКОМ раскладе — в том числе
/// помеченная «загружать всегда»: <c>IsActive</c> стоит первым условием
/// <see cref="ExperienceService.GoesToPrompt"/>;</item>
/// <item>список опыта фильтруется тремя значениями — активные / неактивные / все;</item>
/// <item>активность переключается отдельным действием <see cref="ExperienceService.SetActive"/>,
/// и чужую запись переключить нельзя (ТЗ гл. 6);</item>
/// <item>правило архивации вида «опыт» отбирает записи ПО ЭТОЙ КОЛОНКЕ и умеет быть
/// точно «архивировать все неактивные» — срок «возраст не важен» (T-265-S0).</item>
/// </list>
/// </summary>
public sealed class T265S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string OtherServerId = "unid-S1";

    private static ServerScope OtherScope =>
        new(() => OtherServerId, () => "S1", () => false,
            codeOf: id => id == OtherServerId ? "S1" : "",
            nameOf: id => id == OtherServerId ? "ноутбук" : "");

    private ArchiveCandidateService Candidates() => new(_f.Db);

    private ArchiveRuleService Rules() => new(_f.Db, _f.Events);

    private static ArchiveRule ExperienceRule(string activity, string ageKind = ArchiveRuleAges.Any,
        int days = 0) => new()
        {
            Target = ArchiveRuleTargets.Experience,
            Activity = activity,
            DateField = ArchiveRuleDates.Updated,
            AgeKind = ageKind,
            AgeDays = days,
        };

    private Project NewProject() =>
        _f.Projects.Create("Проект T-265-S0", Path.Combine(_f.Dir, "prj265"), null, null);

    private string SkillId(string code) => _f.RefData.Skills().First(s => s.Name == code).Id;

    // ---------- схема и накопленные записи ----------

    [Fact]
    public void The_Migration_Gives_Every_Accumulated_Record_The_Active_Flag()
    {
        var project = NewProject();
        _f.Experience.CreateForProject(project.Id, "накопленная запись 1", null);
        _f.Experience.CreateForProject(project.Id, "накопленная запись 2", null);

        // откатываем базу к состоянию ДО правки: колонки нет, версия схемы прежняя.
        // Триггеры журнала изменений перечисляют ВСЕ колонки таблицы, поэтому их снимаем
        // первыми — тем же способом, каким это делает сама инициализация перед миграциями
        using (var conn = _f.Db.Open())
        {
            ChangeLog.DropTriggers(conn);
            Sql.Exec(conn, null, "ALTER TABLE experience DROP COLUMN is_active");
            Sql.Exec(conn, null, "UPDATE meta SET value='46' WHERE key='schema_version'");
        }
        _f.Db.Init();

        using var check = _f.Db.Open();
        var columns = Sql.Query(check, null, "PRAGMA table_info(experience)", r => r.S("name"));
        Assert.Contains("is_active", columns);
        Assert.Equal("48", _f.Db.Meta("schema_version"));
        // ни одной погашенной записи: умолчание колонки — 1
        Assert.Equal(0L, Sql.Scalar<long>(check, null,
            "SELECT COUNT(*) FROM experience WHERE is_active=0"));
        Assert.Equal(2L, Sql.Scalar<long>(check, null,
            "SELECT COUNT(*) FROM experience WHERE is_active=1"));
        Assert.All(_f.Experience.ListByProject(project.Id), r => Assert.True(r.IsActive));
        // и признак УЕЗЖАЕТ РЕПЛИКАЦИЕЙ: триггеры журнала перечисляют колонки по факту,
        // поэтому новая колонка попадает в них сама — отдельной правки ChangeLog не нужно
        Assert.Contains("is_active", Sql.Scalar<string>(check, null,
            "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='ai2p_chg_experience_ins'") ?? "");
    }

    [Fact]
    public void A_New_Record_Is_Active_And_The_Flag_Survives_Reading()
    {
        var project = NewProject();
        var record = _f.Experience.CreateForProject(project.Id, "новая запись", null);
        Assert.True(record.IsActive);

        var off = _f.Experience.SetActive(record.Id, false, null);
        Assert.False(off.IsActive);
        Assert.False(_f.Experience.Get(record.Id)!.IsActive);
        Assert.False(_f.Experience.ListByProject(project.Id).Single().IsActive);

        // и обратно — тем же действием: в этом весь смысл признака
        Assert.True(_f.Experience.SetActive(record.Id, true, null).IsActive);
        // событие журнала у переключения своё
        Assert.NotEmpty(_f.Events.Query(eventType: EventTypes.ExperienceActivity));
    }

    // ---------- отбор в задание ----------

    [Fact]
    public void An_Inactive_Record_Never_Goes_To_The_Prompt_Even_With_Always_Load()
    {
        var project = NewProject();
        var always = _f.Experience.CreateForProject(project.Id, "правило мимо отбора", null,
            alwaysLoad: true);
        var bySkill = _f.Experience.CreateForProject(project.Id, "вывод по навыку", null,
            SkillId("code-write"));

        string[] owned = ["code-write"];
        Assert.Equal(2, _f.Experience.ListForExecutor(project.Id, owned).Count);

        _f.Experience.SetActive(always.Id, false, null);
        _f.Experience.SetActive(bySkill.Id, false, null);

        // ни «загружать всегда», ни совпавший навык неактивную запись не вытаскивают
        Assert.Empty(_f.Experience.ListForExecutor(project.Id, owned));
        Assert.All(_f.Experience.ListByProject(project.Id),
            r => Assert.False(ExperienceService.GoesToPrompt(r, owned, null)));
    }

    [Fact]
    public void The_Rule_Holds_For_General_Rules_And_For_A_Template_Node()
    {
        var template = _f.Tasks.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var node = _f.Experience.Create(template.Id, "опыт узла", null);
        var general = _f.Experience.CreateGeneral("общее правило", null, alwaysLoad: true);

        string[] owned = ["code-write"];
        Assert.Single(_f.Experience.ListByTemplateForExecutor(template.Id, owned));
        Assert.Single(_f.Experience.ListGeneralForExecutor(owned));

        _f.Experience.SetActive(node.Id, false, null);
        _f.Experience.SetActive(general.Id, false, null);

        Assert.Empty(_f.Experience.ListByTemplateForExecutor(template.Id, owned));
        Assert.Empty(_f.Experience.ListGeneralForExecutor(owned));
    }

    [Fact]
    public void An_Active_Record_Keeps_Its_Former_Behaviour()
    {
        var project = NewProject();
        var skill = SkillId("code-write");
        var mine = _f.Experience.CreateForProject(project.Id, "мой навык", null, skill,
            tags: ["сборка"]);
        var alien = _f.Experience.CreateForProject(project.Id, "чужой навык", null,
            SkillId("image-generate"));

        string[] owned = ["code-write"];
        // отбор по навыку работает ровно как до правки
        Assert.True(ExperienceService.GoesToPrompt(_f.Experience.Get(mine.Id)!, owned, ["сборка"]));
        Assert.False(ExperienceService.GoesToPrompt(_f.Experience.Get(alien.Id)!, owned, null));
        // а вот тэги с T-267-S0 жёстким условием НЕ стоят: запись с непересекающимися тэгами
        // берётся и просто ранжируется ниже при дележе бюджета
        Assert.True(ExperienceService.GoesToPrompt(_f.Experience.Get(mine.Id)!, owned, ["репликация"]));
    }

    [Fact]
    public void Editing_An_Inactive_Record_Does_Not_Switch_It_Back_On()
    {
        var project = NewProject();
        var record = _f.Experience.CreateForProject(project.Id, "текст", null);
        _f.Experience.SetActive(record.Id, false, null);

        // правка агента (update_experience) активности не передаёт вовсе — и не воскрешает
        var updated = _f.Experience.Update(record.Id, "исправленный текст", null);
        Assert.Equal("исправленный текст", updated.Text);
        Assert.False(updated.IsActive);
    }

    // ---------- владение ----------

    [Fact]
    public void A_Record_Of_Another_Server_Cannot_Be_Switched()
    {
        var project = NewProject();
        var mine = _f.Experience.CreateForProject(project.Id, "моя запись", null);

        var alien = new ExperienceService(_f.Db, _f.Events, OtherScope);
        Assert.Throws<ArgumentException>(() => alien.SetActive(mine.Id, false, null));
        Assert.True(_f.Experience.Get(mine.Id)!.IsActive);
    }

    // ---------- список и фильтр ----------

    [Fact]
    public void The_List_Filter_Gives_Three_Different_Sets()
    {
        var project = NewProject();
        var on = _f.Experience.CreateForProject(project.Id, "активная", null);
        var off = _f.Experience.CreateForProject(project.Id, "погашенная", null);
        _f.Experience.SetActive(off.Id, false, null);

        // список отдаёт ОБЕ записи — фильтр применяется на клиенте (ExperienceTab),
        // и здесь проверяется его правило: активные / неактивные / все
        var all = _f.Experience.ListByProject(project.Id);
        Assert.Equal(2, all.Count);
        Assert.Equal(on.Id, all.Where(r => r.IsActive).Select(r => r.Id).Single());
        Assert.Equal(off.Id, all.Where(r => !r.IsActive).Select(r => r.Id).Single());
    }

    // ---------- архивация ----------

    [Fact]
    public void The_Rule_Archive_All_Inactive_Takes_Exactly_The_Inactive_Records()
    {
        var project = NewProject();
        var on = _f.Experience.CreateForProject(project.Id, "активная запись", null);
        var off = _f.Experience.CreateForProject(project.Id, "неактивная запись", null);
        _f.Experience.SetActive(off.Id, false, null);

        // «опыт + только неактивные + возраст не важен»: запись погашена только что,
        // и никакого срока ждать от неё не надо — требование заказчика точное
        var inactive = Candidates().Select(ExperienceRule(ArchiveRuleActivity.Inactive));
        Assert.Equal(off.Id, inactive.Select(c => c.Id).Single());

        var active = Candidates().Select(ExperienceRule(ArchiveRuleActivity.Active));
        Assert.Equal(on.Id, active.Select(c => c.Id).Single());

        var any = Candidates().Select(ExperienceRule(ArchiveRuleActivity.Any));
        Assert.Equal(2, any.Count);
    }

    [Fact]
    public void A_Rule_Without_An_Age_Is_Accepted_And_Saved()
    {
        var rule = Rules().Create(ExperienceRule(ArchiveRuleActivity.Inactive), null);
        Assert.Equal(ArchiveRuleAges.Any, rule.AgeKind);
        Assert.Equal(0, rule.AgeDays);
        Assert.Equal(ArchiveRuleAges.Any, Rules().List().Single().AgeKind);

        // граница такого правила — бесконечность: по возрасту подходит любая запись
        Assert.Equal(DateTime.MaxValue,
            ArchiveAge.Threshold(ArchiveRuleAges.Any, 0, 0, 0, DateTime.UtcNow));
        // у прежних видов срока ничего не изменилось
        Assert.True(ArchiveAge.Threshold(ArchiveRuleAges.Days, 30, 0, 0, DateTime.UtcNow)
                    < DateTime.UtcNow);
    }

    [Fact]
    public void Deleted_Records_Still_Count_As_Inactive()
    {
        var project = NewProject();
        var deleted = _f.Experience.CreateForProject(project.Id, "удалённая запись", null);
        _f.Experience.Delete(deleted.Id, null);

        // «неактивная» у опыта означает И погашенную, И помеченную удалённой:
        // прежнее поведение правила сохранено
        var inactive = Candidates().Select(ExperienceRule(ArchiveRuleActivity.Inactive));
        Assert.Equal(deleted.Id, inactive.Select(c => c.Id).Single());
    }
}
