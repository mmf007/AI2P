using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo35_2 (ТЗ v1.39): заполнение справочника навыков (иерархия категория-действие[-язык],
/// 89 навыков), переименование старых кодов с сохранением id, миграция кодов в декларациях
/// возможностей и правило третьей категории при автоподборе (code-write покрывает
/// code-write-cpp, обратное неверно).
/// </summary>
public sealed class Todo35_2Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private string SkillId(string name) => _f.RefData.Skills().First(s => s.Name == name).Id;

    private Executor CreateAi(string nick, string scopeJson)
    {
        var scopeRel = $"models/scope_test_{nick}.json";
        _f.Files.WriteText(scopeRel, scopeJson);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel,
        }, null);
    }

    private Team CreateTeam(params Executor[] members) =>
        _f.Teams.Create(new Team
        {
            Name = "Команда-" + Guid.NewGuid().ToString("N")[..6],
            Members = members.Select(e => new TeamMember { ExecutorId = e.Id }).ToList(),
        }, null);

    [Fact]
    public void Seed_Fills_Full_Skill_Catalog()
    {
        _f.RefData.Seed(); // повторный прогон (фикстура уже сидила) — без дублей

        var skills = _f.RefData.Skills();
        Assert.Equal(94, skills.Count); // 5 code × (1 + 12 языков) + 29 остальных (T-257: +5 режимов)
        // базовые, языковые варианты и новые 3d-навыки задания todo35_2
        foreach (var name in new[]
                 {
                     "code-write", "code-write-cpp", "code-write-cs", "code-debug-py",
                     "code-refactor-kt", "text-translate", "analyze-plan", "image-photo",
                     "video-animate", "audio-speech", "3d-animation", "3d-environment",
                 })
        {
            var skill = skills.SingleOrDefault(s => s.Name == name);
            Assert.NotNull(skill);
            Assert.False(skill!.IsCustom);
        }
        // старых кодов больше нет
        Assert.DoesNotContain(skills, s => s.Name is "write-code" or "review-code"
            or "test-run" or "write-docs" or "draw-concept");
    }

    [Fact]
    public void Seed_Renames_Old_Codes_Preserving_Ids()
    {
        // имитация старой БД: у записи справочника прежний код и ссылка из задачи
        var skill = _f.RefData.Skills().Single(s => s.Name == "code-write");
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, "UPDATE skills SET name='write-code' WHERE id=@id", ("@id", skill.Id));
        }
        var task = _f.Tasks.Create(new TaskItem { Title = "Задача", SkillIds = [skill.Id] }, "т", "", null);

        _f.RefData.Seed();

        var renamed = _f.RefData.Skills().Single(s => s.Name == "code-write");
        Assert.Equal(skill.Id, renamed.Id); // id сохранён — ссылка задачи жива
        Assert.Equal([skill.Id], _f.Tasks.Get(task.Id)!.SkillIds.ToArray());
        Assert.DoesNotContain(_f.RefData.Skills(), s => s.Name == "write-code");
        Assert.Equal(94, _f.RefData.Skills().Count);
    }

    [Fact]
    public void MigrateSkillCodes_Rewrites_Scope_Files()
    {
        _f.Files.WriteText("models/scope_custom1.json",
            """{ "skills": [ { "name": "write-code", "score": 86 }, { "name": "write-docs", "score": 75 } ] }""");
        _f.Files.WriteText("executors/scope_h1.json",
            """{ "skills": [ { "name": "draw-concept", "score": 80 }, { "name": "review-code", "score": 60 } ] }""");
        _f.Files.WriteText("models/scope_untouched.json",
            """{ "skills": [ { "name": "code-write", "score": 90 } ] }""");

        RefDataService.MigrateSkillCodes(_f.Files);

        var custom = _f.Files.ReadText("models/scope_custom1.json");
        Assert.Contains("\"code-write\"", custom);
        Assert.Contains("\"text-docs\"", custom);
        Assert.DoesNotContain("\"write-code\"", custom);
        Assert.DoesNotContain("\"write-docs\"", custom);
        var human = _f.Files.ReadText("executors/scope_h1.json");
        Assert.Contains("\"image-concept\"", human);
        Assert.Contains("\"code-review\"", human);

        // повторный прогон идемпотентен
        RefDataService.MigrateSkillCodes(_f.Files);
        Assert.Equal(custom, _f.Files.ReadText("models/scope_custom1.json"));
    }

    [Fact]
    public void Model_Seed_Declarations_Use_New_Codes()
    {
        _f.Models.Seed();
        var fable = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var scope = _f.Files.ReadText(fable.CapabilitiesPath);
        Assert.Contains("\"code-write\"", scope);
        Assert.Contains("\"analyze-data\"", scope);
        Assert.DoesNotContain("\"write-code\"", scope);
    }

    [Fact]
    public void Pick_Base_Skill_Covers_Language_Variants()
    {
        // у исполнителя code-write без языка — задача с code-write-cpp ему подходит
        var ai = CreateAi("generalist", """{ "skills": [ { "name": "code-write", "score": 80 } ] }""");
        var team = CreateTeam(ai);

        var picked = _f.Picker.Pick(null, team.Id, [SkillId("code-write-cpp")], PickMode.AiFirst);
        Assert.Equal(ai.Id, picked.ExecutorId);
        Assert.Equal(80, picked.Candidates.Single().Quality);
    }

    [Fact]
    public void Pick_Exact_Language_Score_Wins_Over_Base()
    {
        // точная оценка языка важнее общей
        var ai = CreateAi("cppguru", """
            { "skills": [ { "name": "code-write", "score": 60 }, { "name": "code-write-cpp", "score": 95 } ] }
            """);
        var team = CreateTeam(ai);

        var picked = _f.Picker.Pick(null, team.Id, [SkillId("code-write-cpp")], PickMode.AiFirst);
        Assert.Equal(95, picked.Candidates.Single().Quality);
    }

    [Fact]
    public void Pick_Language_Variant_Does_Not_Cover_Base_Skill()
    {
        // обратное правило не действует: только code-write-cpp не покрывает code-write
        var ai = CreateAi("cpponly", """{ "skills": [ { "name": "code-write-cpp", "score": 95 } ] }""");
        var team = CreateTeam(ai);

        var picked = _f.Picker.Pick(null, team.Id, [SkillId("code-write")], PickMode.AiFirst);
        Assert.Null(picked.ExecutorId);
        Assert.Contains("не", picked.Reason);
    }
}
