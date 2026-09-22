using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-270-S0 (ветка T-318 «Проект опыта»): НАБОРЫ ОПЫТА — БИБЛИОТЕКА СТИЛЕЙ РАБОТЫ.
///
/// Стиль работы — это пачка записей опыта, которую ставят и снимают ЦЕЛИКОМ и возят между
/// установками. Проверяется:
/// <list type="number">
/// <item>установка заводит записи с ТЕМИ ЖЕ идентификаторами, что в файле, и в ВЫБРАННУЮ
/// область (проект / общие правила);</item>
/// <item>повторная установка ничего не задваивает и НЕ ЗАТИРАЕТ правку человека;</item>
/// <item>снятие убирает только записи набора, соседние записи остаются;</item>
/// <item>снятый набор ставится ОБРАТНО (мягкое удаление оставляет строку с тем же id,
/// и обычный INSERT упёрся бы в первичный ключ);</item>
/// <item>выгрузка даёт файл, который ставится обратно и даёт те же идентификаторы;</item>
/// <item>ЗАПИСИ НАБОРА ДОЕЗЖАЮТ ДО ЗАДАЧ наравне с остальным опытом — служебная пометка
/// владельца <c>pack:&lt;код&gt;</c> тэгом не считается (грабля п. 2 задания: считай её
/// тэгом — и поставленный стиль исчез бы у КАЖДОЙ задачи с проставленными тэгами);</item>
/// <item>область установки в файле не хранится и пустой не бывает;</item>
/// <item>набор дистрибутива раскладывается файлом и НИКОГО не ставит сам.</item>
/// </list>
/// </summary>
public sealed class T270S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string PackCode = "test.style";
    private const string FirstId = "b1e5a0c0-7d1a-4c31-9f01-0000000t0001";
    private const string SecondId = "b1e5a0c0-7d1a-4c31-9f01-0000000t0002";

    private ExperiencePackService Packs =>
        new(_f.Files, _f.Experience, _f.RefData, _f.Tasks);

    private Project NewProject() =>
        _f.Projects.Create("Проект T-270-S0", Path.Combine(_f.Dir, "prj270"), null, null);

    /// <summary>Набор из двух записей: у первой СВОИХ тэгов нет вовсе — на ней и проверяется
    /// грабля с пометкой владельца, у второй тэги есть.</summary>
    private const string PackJson = """
        {
          "code": "test.style",
          "version": 1,
          "name": { "ru": "Пробный стиль", "en": "Test style" },
          "description": { "ru": "Набор для проверок", "en": "A pack for tests" },
          "records": [
            { "id": "b1e5a0c0-7d1a-4c31-9f01-0000000t0001", "skill": "code-review",
              "tags": [], "alwaysLoad": false,
              "text": { "ru": "смотреть сначала на сценарий отказа", "en": "look at the failure first" } },
            { "id": "b1e5a0c0-7d1a-4c31-9f01-0000000t0002", "skill": "code-review",
              "tags": ["ревью"], "alwaysLoad": false,
              "text": { "ru": "у каждого дефекта есть имя проверки", "en": "every defect names a test" } }
          ],
          "templates": [
            { "id": "b1e5a0c0-7d1a-4c31-9f01-0000000t1001", "parent": null,
              "skills": ["code-review"],
              "title": { "ru": "Код-ревью", "en": "Code review" },
              "description": { "ru": "описание", "en": "description" },
              "acceptance": { "ru": "критерии", "en": "criteria" } }
          ]
        }
        """;

    private ExperiencePack WritePack(string json = PackJson, string code = PackCode)
    {
        _f.Files.WriteText(ExperiencePack.PathOf(code), json);
        return Packs.Get(code)!;
    }

    // ---------- формат файла ----------

    [Fact]
    public void A_Pack_File_Keeps_Localized_Texts_Fixed_Ids_And_The_Templates_Block()
    {
        var pack = WritePack();

        Assert.Equal(PackCode, pack.Code);
        Assert.Equal("Пробный стиль", pack.Name.Text("ru"));
        Assert.Equal("Test style", pack.Name.Text("en"));
        Assert.Equal(2, pack.Records.Count);
        Assert.Equal(FirstId, pack.Records[0].Id);
        Assert.Equal("code-review", pack.Records[0].Skill);
        // блок шаблонов нужен соседней подзадаче ветки (T-271-S0): состав полей согласован
        var node = Assert.Single(pack.Templates);
        Assert.Equal("b1e5a0c0-7d1a-4c31-9f01-0000000t1001", node.Id);
        Assert.Equal("", node.Parent);
        Assert.Equal("Код-ревью", node.Title.Text("ru"));
        Assert.Equal(["code-review"], node.Skills);
        // ОБЛАСТИ УСТАНОВКИ В ФАЙЛЕ НЕТ И БЫТЬ НЕ ДОЛЖНО: её выбирает человек
        Assert.DoesNotContain("\"scope\"", PackJson);
    }

    [Fact]
    public void A_Record_Without_An_Id_Is_Refused()
    {
        // случайный идентификатор при установке означал бы второй комплект записей
        // у каждого сервера организации — набор без id негоден целиком
        var pack = ExperiencePack.Parse("""
            { "code": "x", "records": [ { "text": { "ru": "текст" } } ] }
            """);
        Assert.Null(pack);
    }

    // ---------- установка ----------

    [Fact]
    public void B_Install_Puts_The_Same_Ids_Into_The_Chosen_Scope()
    {
        var project = NewProject();
        var pack = WritePack();

        var added = Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru").Records;

        Assert.Equal(2, added);
        var records = _f.Experience.ListByProject(project.Id);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Id == FirstId);
        Assert.Contains(records, r => r.Id == SecondId);
        // пометка владельца — служебный ТЭГ: по ней набор снимается
        Assert.All(records, r => Assert.Contains(PackCodes.ExperienceOwner(PackCode), r.Tags));
        Assert.Equal("смотреть сначала на сценарий отказа",
            records.First(r => r.Id == FirstId).Text);
        // навык из файла доехал до записи
        Assert.Equal("code-review", records.First(r => r.Id == FirstId).SkillName);

        var (scope, projectId, templateTaskId) = Packs.Placement(PackCode);
        Assert.Equal(ExperienceScopes.Project, scope);
        Assert.Equal(project.Id, projectId);
        Assert.Equal("", templateTaskId);
    }

    [Fact]
    public void B_Install_Into_The_General_Rules_Is_The_Same_Mechanism()
    {
        var pack = WritePack();
        var before = _f.Experience.ListGeneral().Count;

        Assert.Equal(2, Packs.Install(pack, ExperienceScopes.General, null, null, null, "ru").Records);

        Assert.Equal(before + 2, _f.Experience.ListGeneral().Count);
        Assert.Equal(ExperienceScopes.General, Packs.Placement(PackCode).Scope);
    }

    [Fact]
    public void B_An_Empty_Scope_Is_Refused()
    {
        // интерфейс не даёт области не выбрать, но сервер обязан отвергать её сам
        var pack = WritePack();
        Assert.Throws<ArgumentException>(() => Packs.Install(pack, "", null, null));
        Assert.Throws<ArgumentException>(
            () => Packs.Install(pack, ExperienceScopes.Project, null, null));
    }

    [Fact]
    public void C_A_Second_Install_Adds_Nothing_And_Keeps_The_Human_Edit()
    {
        var project = NewProject();
        var pack = WritePack();
        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");
        _f.Experience.Update(FirstId, "правка человека", null);

        var added = Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru").Records;

        Assert.Equal(0, added);
        Assert.Equal(2, _f.Experience.ListByProject(project.Id).Count);
        Assert.Equal("правка человека", _f.Experience.Get(FirstId)!.Text);
    }

    [Fact]
    public void C_A_Record_The_Human_Wrote_By_Hand_Is_Not_Doubled()
    {
        // сверка по ТЕКСТУ, как у записей манифеста плагина: тот же вывод, заведённый
        // руками до установки набора, второй раз появляться не должен
        var project = NewProject();
        _f.Experience.CreateForProject(project.Id, "у каждого дефекта есть имя проверки", null);
        var pack = WritePack();

        Assert.Equal(1, Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru").Records);
        Assert.Equal(2, _f.Experience.ListByProject(project.Id).Count);
    }

    // ---------- снятие ----------

    [Fact]
    public void D_Remove_Takes_Only_The_Records_Of_The_Pack()
    {
        var project = NewProject();
        var own = _f.Experience.CreateForProject(project.Id, "свой вывод про сборку", null);
        var pack = WritePack();
        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");

        Assert.Equal(2, Packs.Remove(PackCode));

        var left = _f.Experience.ListByProject(project.Id);
        Assert.Equal(own.Id, Assert.Single(left).Id);
        Assert.Empty(Packs.InstalledRecords(PackCode));
        Assert.Equal("", Packs.Placement(PackCode).Scope);
    }

    [Fact]
    public void D_A_Removed_Pack_Installs_Back()
    {
        // снятие мягкое: строка с тем же идентификатором остаётся, и обычный INSERT
        // упёрся бы в первичный ключ — повторно поставить набор стало бы нельзя вовсе
        var project = NewProject();
        var pack = WritePack();
        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");
        Packs.Remove(PackCode);

        Assert.Equal(2, Packs.Install(pack, ExperienceScopes.General, null, null, null, "ru").Records);

        var record = _f.Experience.Get(FirstId)!;
        Assert.True(record.IsGeneral);
        Assert.True(record.IsActive);
    }

    // ---------- отбор в задание ----------

    [Fact]
    public void E_Pack_Records_Reach_A_Task_That_Has_Tags()
    {
        // ГРАБЛЯ п. 2 задания: считай пометку владельца тэгом — и записи набора исчезли бы
        // у любой задачи с проставленными тэгами
        var project = NewProject();
        var pack = WritePack();
        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");

        var forTask = _f.Experience.ListForExecutor(project.Id, ["code-review"], ["сборка"]);

        Assert.Contains(forTask, r => r.Id == FirstId);
        Assert.True(ExperienceService.TagsMatch([PackCodes.ExperienceOwner(PackCode)], ["сборка"]));
        // и она НЕ считается совпадением темы: рангу прибавлять за неё не за что
        Assert.False(ExperienceService.TagsOverlap(
            [PackCodes.ExperienceOwner(PackCode)], [PackCodes.ExperienceOwner(PackCode)]));
        Assert.True(ExperienceService.IsOwnerTag(PackCodes.ExperienceOwner(PackCode)));
        Assert.False(ExperienceService.IsOwnerTag("ревью"));
    }

    // ---------- выгрузка ----------

    [Fact]
    public void F_Export_Gives_A_File_That_Installs_Back()
    {
        var project = NewProject();
        var one = _f.Experience.CreateForProject(project.Id, "первый вывод", null,
            _f.RefData.Skills().First(s => s.Name == "code-review").Id, tags: ["ревью"]);
        var two = _f.Experience.CreateForProject(project.Id, "второй вывод", null);

        var path = Packs.Export("my.style", "Мой стиль", "описание", [one.Id, two.Id], "ru");

        Assert.Equal(ExperiencePack.PathOf("my.style"), path);
        var saved = Packs.Get("my.style")!;
        Assert.Equal(2, saved.Records.Count);
        // идентификаторы СОХРАНЕНЫ: обратная установка даёт те же строки, а не их копии
        Assert.Contains(saved.Records, r => r.Id == one.Id);
        Assert.Equal("первый вывод", saved.Records.First(r => r.Id == one.Id).Text.Text("ru"));
        Assert.Equal("code-review", saved.Records.First(r => r.Id == one.Id).Skill);
        Assert.Contains("ревью", saved.Records.First(r => r.Id == one.Id).Tags);

        // ставится обратно — в другую область, и записи находятся по своим id
        _f.Experience.Delete(one.Id, null);
        _f.Experience.Delete(two.Id, null);
        Assert.Equal(2, Packs.Install(saved, ExperienceScopes.General, null, null, null, "ru").Records);
        Assert.True(_f.Experience.Get(one.Id)!.IsGeneral);
    }

    [Fact]
    public void F_Export_Does_Not_Carry_Someone_Elses_Owner_Tag()
    {
        var project = NewProject();
        var pack = WritePack();
        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");

        Packs.Export("my.style", "Мой стиль", "", [SecondId], "ru");

        var saved = Packs.Get("my.style")!;
        var record = Assert.Single(saved.Records);
        Assert.Contains("ревью", record.Tags);
        Assert.DoesNotContain(record.Tags, PackCodes.IsPackOwner);
    }

    [Fact]
    public void F_An_Empty_Export_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => Packs.Export("my.style", "Мой стиль", "", [], "ru"));
    }

    // ---------- поставка в дистрибутиве ----------

    [Fact]
    public void G_The_Distribution_Pack_Is_Written_To_The_Data_Directory_And_Installs_Nobody()
    {
        ExperiencePackSeed.Write(_f.Files);

        var pack = Packs.Get(ExperiencePackSeed.StrictReviewCode);
        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.Records);
        // тексты поставляемого набора — на ВСЕХ пяти языках интерфейса (ответ заказчика)
        foreach (var lang in Loc.Languages)
        {
            Assert.NotEqual("", pack.Name.Text(lang));
            Assert.All(pack.Records, r => Assert.NotEqual("", r.Text.Text(lang)));
        }
        // сид кладёт ФАЙЛ и не ставит набор: иначе поставляемый опыт начал бы приходить
        // всем задачам молча (решение заказчика по T-270-S0)
        Assert.Empty(Packs.InstalledRecords(ExperiencePackSeed.StrictReviewCode));
        // повторный проход ничего не ломает
        ExperiencePackSeed.Write(_f.Files);
        Assert.Single(Packs.List().Where(p => p.Code == ExperiencePackSeed.StrictReviewCode));
    }
}
