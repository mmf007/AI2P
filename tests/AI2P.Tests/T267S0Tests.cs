using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-267-S0 (ветка T-318 «Проект опыта»): ПОЧИНКА ОТБОРА ОПЫТА В ЗАДАНИЕ (вариант А).
///
/// Замер на живом промпте (16.09.2026) показал, что опыт проекта съедает предел целиком
/// (≈80 записей показано, 358 отброшено), какие именно доедут — решает дата правки, а опыт
/// узлов-предков шаблона не доезжает вовсе. Правка — четыре вещи:
/// <list type="number">
/// <item>ТЭГИ — СИГНАЛ, А НЕ ФИЛЬТР: <see cref="ExperienceService.GoesToPrompt"/> их больше
/// не проверяет, запись с непересекающимися тэгами берётся и ранжируется ниже
/// (<see cref="ExperienceService.TagsOverlap"/>);</item>
/// <item>КВОТЫ УРОВНЕЙ в бюджете (<see cref="ExperienceBudget.SharePercent"/>) — 50 % узлу
/// шаблона, 30 % проекту, 20 % общим правилам, неиспользованная квота перетекает соседям;</item>
/// <item>опыт УЗЛОВ-ПРЕДКОВ шаблона доезжает до задачи узла-потомка
/// (<see cref="ExperienceService.TemplateChain"/>), поддерево — по-прежнему нет;</item>
/// <item>«ЗАГРУЖАТЬ ВСЕГДА» — вне конкурса и вне квот, а порядок ПЕЧАТИ прежний
/// (хронологический внутри блока: он читается как история работы).</item>
/// </list>
/// </summary>
public sealed class T267S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static readonly DateTime Base = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Запись ровно на 10 знаков строки промпта: так бюджет считается в уме.</summary>
    private static ExperienceRecord Rec(string id, int level, int ageDays = 0,
        bool alwaysLoad = false, IEnumerable<string>? tags = null) => new()
        {
            Id = id,
            TemplateTaskId = level == 0 ? "node" : "",
            ProjectId = level == 1 ? "prj" : null,
            Text = "0123456789",
            AlwaysLoad = alwaysLoad,
            Tags = tags?.ToList() ?? [],
            CreatedAt = Base.AddDays(-ageDays),
            UpdatedAt = Base.AddDays(-ageDays),
        };

    private static string Line(ExperienceRecord r) => r.Text;

    private static int Rank(ExperienceRecord r) => ExperienceBudget.LevelOf(r);

    private Project NewProject() =>
        _f.Projects.Create("Проект T-267-S0", Path.Combine(_f.Dir, "prj267"), null, null);

    private string SkillId(string code) => _f.RefData.Skills().First(s => s.Name == code).Id;

    private TaskItem Node(string title, string? parentId = null) =>
        _f.Tasks.Create(new TaskItem { Title = title, IsTemplate = true, ParentId = parentId },
            "", "", null);

    // ---------- 1. тэги: сигнал, а не фильтр ----------

    [Fact]
    public void A_Record_With_Foreign_Tags_Still_Goes_To_The_Prompt()
    {
        var project = NewProject();
        var mine = _f.Experience.CreateForProject(project.Id, "про сборку", null,
            SkillId("code-write"), tags: ["сборка"]);

        string[] owned = ["code-write"];
        // до T-267-S0 здесь было false: TagsMatch стоял жёстким условием
        Assert.True(ExperienceService.GoesToPrompt(_f.Experience.Get(mine.Id)!, owned, ["репликация"]));
        Assert.Single(_f.Experience.ListForExecutor(project.Id, owned, ["репликация"]));

        // и сам счёт совпадения тэгов поблажек больше не знает: «тэгов нет ни у кого» —
        // это не совпадение темы, а отсутствие сведений о ней
        Assert.True(ExperienceService.TagsMatch(["сборка"], null));
        Assert.False(ExperienceService.TagsOverlap(["сборка"], null));
        Assert.False(ExperienceService.TagsOverlap(null, ["сборка"]));
        Assert.True(ExperienceService.TagsOverlap(["Сборка"], ["сборка"]));
        // служебная пометка владельца (plugin:<код>) тэгом не считается и здесь
        Assert.False(ExperienceService.TagsOverlap(["plugin:editor.shotcut"], ["editor.shotcut"]));
    }

    [Fact]
    public void A_Record_On_The_Topic_Of_The_Task_Wins_The_Place()
    {
        // места ровно на одну запись; по свежести победила бы запись НЕ по теме
        var budget = new ExperienceBudget(10);
        var offTopic = Rec("свежая-не-по-теме", level: 1, ageDays: 0, tags: ["репликация"]);
        var onTopic = Rec("старая-по-теме", level: 1, ageDays: 30, tags: ["сборка"]);

        var (taken, skipped) = budget.Fit([offTopic, onTopic], Line, Rank,
            r => ExperienceService.TagsOverlap(r.Tags, ["сборка"]));

        Assert.Equal("старая-по-теме", Assert.Single(taken).Id);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void The_Topic_Bonus_Does_Not_Outweigh_A_Whole_Level()
    {
        // прибавка за тэги стоит ПОЛУУРОВНЯ: опыт узла шаблона не по теме всё равно
        // важнее опыта проекта по теме
        var budget = new ExperienceBudget(10);
        var node = Rec("узел-не-по-теме", level: 0, tags: ["репликация"]);
        var project = Rec("проект-по-теме", level: 1, tags: ["сборка"]);

        var (taken, _) = budget.Fit([node, project], Line, Rank,
            r => ExperienceService.TagsOverlap(r.Tags, ["сборка"]));

        Assert.Equal("узел-не-по-теме", Assert.Single(taken).Id);
    }

    // ---------- 2. квоты уровней ----------

    [Fact]
    public void The_General_Rules_Are_Not_Squeezed_Out_By_The_Project_Experience()
    {
        // 100 знаков на 23 записи по 10: опыта проекта хватит, чтобы съесть всё
        var budget = new ExperienceBudget(100);
        var records = Enumerable.Range(0, 20).Select(i => Rec("prj" + i, level: 1, ageDays: i))
            .Concat(Enumerable.Range(0, 3).Select(i => Rec("gen" + i, level: 2, ageDays: i)))
            .ToList();

        var (taken, skipped) = budget.Fit(records, Line, Rank);

        // общим правилам досталась их квота (20 % = 20 знаков = две записи)
        Assert.Equal(2, taken.Count(r => r.Id.StartsWith("gen")));
        // а весь предел при этом потрачен: неиспользованная квота узла шаблона перетекла
        Assert.Equal(10, taken.Count);
        Assert.Equal(13, skipped);
        Assert.False(budget.HasRoom);
    }

    [Fact]
    public void An_Unused_Quota_Flows_To_The_Neighbours()
    {
        // ни узла шаблона, ни общих правил: опыт проекта обязан занять ВЕСЬ предел,
        // а не свои 30 %
        var budget = new ExperienceBudget(100);
        var records = Enumerable.Range(0, 20).Select(i => Rec("prj" + i, level: 1, ageDays: i)).ToList();

        var (taken, _) = budget.Fit(records, Line, Rank);

        Assert.Equal(10, taken.Count);
    }

    // ---------- 3. «загружать всегда» и порядок печати ----------

    [Fact]
    public void Always_Load_Stays_Out_Of_The_Competition_And_Out_Of_The_Quotas()
    {
        // места ровно на одну запись, и её берёт пометка «загружать всегда» — при том,
        // что уровень у неё самый дальний (общее правило организации)
        var budget = new ExperienceBudget(10);
        var records = new List<ExperienceRecord>
        {
            Rec("узел", level: 0),
            Rec("проект", level: 1),
            Rec("правило", level: 2, alwaysLoad: true),
        };

        var (taken, skipped) = budget.Fit(records, Line, Rank);

        Assert.Equal("правило", Assert.Single(taken).Id);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void Every_Always_Load_Record_Reaches_The_Task_Even_Past_The_Limit()
    {
        // ВНЕ БЮДЖЕТА, а не «первыми в очереди» (сведение ветки T-318): до квот помеченные
        // записи шли тем же проходом, что остальные, и при тесном пределе часть из них
        // молча пропадала — пометка «загружать всегда» этого и не означает. Теперь предел
        // они превышают: правило, которое велено вставлять при любом раскладе, доезжает
        var budget = new ExperienceBudget(10);
        var records = new List<ExperienceRecord>
        {
            Rec("первое", level: 2, alwaysLoad: true),
            Rec("второе", level: 2, alwaysLoad: true),
            Rec("третье", level: 2, alwaysLoad: true),
            Rec("обычное", level: 1),
        };

        var (taken, skipped) = budget.Fit(records, Line, Rank);

        Assert.Equal(new[] { "первое", "второе", "третье" }, taken.Select(r => r.Id).ToArray());
        Assert.Equal(1, skipped);
        Assert.False(budget.HasRoom);
    }

    [Fact]
    public void The_Print_Order_Is_Still_The_Order_Of_The_List()
    {
        // отбор идёт по прицельности и свежести, а печатается взятое в ИСХОДНОМ порядке
        // списка (он хронологический) — блок читается как история работы, а не как рейтинг
        var budget = new ExperienceBudget(20);
        var records = new List<ExperienceRecord>
        {
            Rec("общее", level: 2, ageDays: 0),
            Rec("проектное-старое", level: 1, ageDays: 30),
            Rec("узловое", level: 0, ageDays: 40),
        };

        var (taken, _) = budget.Fit(records, Line, Rank);

        Assert.Equal(new[] { "проектное-старое", "узловое" }, taken.Select(r => r.Id).ToArray());
    }

    // ---------- 4. опыт узлов-предков ----------

    [Fact]
    public void The_Experience_Of_An_Ancestor_Node_Reaches_The_Task_Of_Its_Child()
    {
        var root = Node("Выпуск версии");
        var child = Node("Собрать выкладку", root.Id);
        var other = Node("Перевести документацию", root.Id);
        _f.Experience.Create(root.Id, "наука всей ветки выпуска", null);
        _f.Experience.Create(child.Id, "наука про выкладку", null);
        _f.Experience.Create(other.Id, "наука про переводы", null);

        Assert.Equal(new[] { child.Id, root.Id }, _f.Experience.TemplateChain(child.Id).ToArray());

        var forChild = _f.Experience.ListByTemplateForExecutor(child.Id, ["code-write"]);
        // доехали свой узел И его предок, а опыт СОСЕДНЕЙ ветки — нет (поддерево не берём)
        Assert.Equal(2, forChild.Count);
        Assert.Contains(forChild, r => r.Text == "наука всей ветки выпуска");
        Assert.Contains(forChild, r => r.Text == "наука про выкладку");
        Assert.DoesNotContain(forChild, r => r.Text == "наука про переводы");

        // у самого корня опыт потомков по-прежнему не берётся
        Assert.Single(_f.Experience.ListByTemplateForExecutor(root.Id, ["code-write"]));
    }

    [Fact]
    public void An_Inactive_Record_Of_An_Ancestor_Node_Does_Not_Reach_The_Task()
    {
        var root = Node("Выпуск версии 2");
        var child = Node("Собрать выкладку 2", root.Id);
        var rule = _f.Experience.Create(root.Id, "погашенная наука ветки", null);
        _f.Experience.SetActive(rule.Id, false, null);

        Assert.Empty(_f.Experience.ListByTemplateForExecutor(child.Id, ["code-write"]));
    }

    [Fact]
    public void A_Record_Of_The_Own_Node_Is_More_Targeted_Than_A_Record_Of_Its_Parent()
    {
        // прицельность внутри уровня — РАССТОЯНИЕ до узла: сам узел важнее родителя,
        // родитель важнее деда (так же считает JobOrchestrator по TemplateChain)
        var budget = new ExperienceBudget(10);
        var own = Rec("свой-узел", level: 0, ageDays: 40);
        var parent = Rec("родитель", level: 0, ageDays: 0);
        parent.TemplateTaskId = "parent";

        var (taken, _) = budget.Fit([parent, own], Line,
            r => r.TemplateTaskId == "node" ? 0 : 1);

        Assert.Equal("свой-узел", Assert.Single(taken).Id);
    }
}
