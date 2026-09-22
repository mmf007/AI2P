using System.Text.RegularExpressions;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Connectors;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-272-S0 (ветка T-318 «Проект опыта»): СОДЕРЖИМОЕ ПОСТАВЛЯЕМЫХ СТИЛЕЙ РАБОТЫ.
///
/// Механику наборов проверяет T270S0Tests; здесь проверяется ТЕКСТ — то, что ломается молча
/// и обнаруживается уже у чужой организации:
/// <list type="number">
/// <item>стилей работы в дистрибутиве ТРИ и у каждого 5–15 записей (меньше — набор ничего не
/// меняет, больше — он один съедает предел опыта задания);</item>
/// <item>у записи проставлен НАВЫК либо пометка «загружать всегда»: запись без того и другого
/// в задание не попадёт вовсе, и набор молча окажется пустым;</item>
/// <item>пометка «загружать всегда» во всём дистрибутиве ОДНА — она приходит каждой задаче
/// организации, и вторая такая запись удваивает этот налог;</item>
/// <item>в записях нет НИЧЕГО ПРОЕКТНОГО — ни имени продукта, ни пути файла, ни кода задачи:
/// набор ставится в любую организацию (правило разграничения областей, T-269-S0);</item>
/// <item>тексты на всех пяти языках интерфейса;</item>
/// <item>записи набора ДОЕЗЖАЮТ до промпта задания подходящего навыка — тем же путём, каким
/// туда попадает обычный опыт проекта.</item>
/// </list>
/// </summary>
public sealed class T272S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ExperiencePackService Packs =>
        new(_f.Files, _f.Experience, _f.RefData, _f.Tasks);

    /// <summary>Стили работы дистрибутива. «Анализ опыта» сюда не входит: это шаблон задач,
    /// а не стиль, и у него своя подзадача ветки (T-271-S0).</summary>
    private static readonly string[] StyleCodes =
        [ExperiencePackSeed.StrictReviewCode, ExperiencePackSeed.TddCode,
         ExperiencePackSeed.ResearchReportCode];

    private static List<ExperiencePack> Styles() =>
        [.. ExperiencePackSeed.All.Select(ExperiencePack.Parse).OfType<ExperiencePack>()
            .Where(p => StyleCodes.Contains(p.Code))];

    [Fact]
    public void The_Distribution_Ships_Three_Working_Styles_With_Five_To_Fifteen_Records()
    {
        var styles = Styles();
        Assert.Equal(StyleCodes.Length, styles.Count);
        foreach (var pack in styles)
        {
            Assert.InRange(pack.Records.Count, 5, 15);
            Assert.NotEmpty(pack.Doc);
        }
    }

    [Fact]
    public void Every_Record_Carries_A_Skill_And_Tags()
    {
        foreach (var pack in Styles())
        {
            foreach (var record in pack.Records)
            {
                // без навыка и без пометки «загружать всегда» запись не попадёт в задание
                // ВООБЩЕ — поставленный стиль оказался бы пустым и молча
                Assert.True(record.Skill.Length > 0 || record.AlwaysLoad,
                    $"{pack.Code}: у записи {record.Id} нет ни навыка, ни пометки «загружать всегда»");
                Assert.NotEmpty(record.Tags);
                // пометка владельца проставляется установкой, в файле её быть не должно
                Assert.DoesNotContain(record.Tags, PackCodes.IsPackOwner);
            }
        }
    }

    [Fact]
    public void The_Skills_Of_A_Record_Exist_In_The_Reference_Book()
    {
        var known = _f.RefData.Skills().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in Styles())
        {
            foreach (var record in pack.Records.Where(r => r.Skill.Length > 0))
            {
                Assert.True(known.Contains(record.Skill),
                    $"{pack.Code}: навыка «{record.Skill}» нет в справочнике");
            }
        }
    }

    [Fact]
    public void Only_One_Shipped_Record_Is_Marked_Always_Load()
    {
        // «загружать всегда» приходит КАЖДОЙ задаче организации: это налог на весь промпт,
        // и платить его дважды за поставляемые наборы незачем
        var always = ExperiencePackSeed.All.Select(ExperiencePack.Parse).OfType<ExperiencePack>()
            .SelectMany(p => p.Records).Where(r => r.AlwaysLoad).ToList();
        var one = Assert.Single(always);
        Assert.Equal("", one.Skill);   // правило верно любому исполнителю — навыка у неё нет
    }

    [Fact]
    public void A_Record_Names_No_Product_No_File_And_No_Task_Code()
    {
        // набор ставится в ЧУЖУЮ организацию: проектное правило, приехавшее туда, опознать
        // некому — оно становится мусором (правило разграничения областей, T-269-S0)
        var taskCode = new Regex(@"\bT-\d+", RegexOptions.IgnoreCase);
        var filePath = new Regex(@"[A-Za-z_][A-Za-z0-9_\-]*\.(cs|py|md|json|ps1|sh|razor)\b");
        foreach (var pack in Styles())
        {
            foreach (var record in pack.Records)
            {
                foreach (var lang in Loc.Languages)
                {
                    var text = record.Text.Text(lang);
                    Assert.DoesNotContain("AI2P", text, StringComparison.OrdinalIgnoreCase);
                    Assert.False(taskCode.IsMatch(text),
                        $"{pack.Code}/{record.Id} ({lang}): в тексте код задачи");
                    Assert.False(filePath.IsMatch(text),
                        $"{pack.Code}/{record.Id} ({lang}): в тексте имя файла");
                }
            }
        }
    }

    [Fact]
    public void Every_Text_Is_Written_In_All_Five_Languages()
    {
        foreach (var pack in Styles())
        {
            foreach (var lang in Loc.Languages)
            {
                Assert.NotEqual("", pack.Name.Text(lang));
                Assert.NotEqual("", pack.Description.Text(lang));
                foreach (var record in pack.Records)
                {
                    var text = record.Text.Text(lang);
                    Assert.NotEqual("", text);
                    Assert.True(text.Length <= 2000,
                        $"{pack.Code}/{record.Id} ({lang}): запись длиннее 2000 знаков");
                    // перевод — это ПЕРЕВОД ПРАВИЛА, а не тот же текст под другим ключом
                    if (!lang.Equals("ru", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.NotEqual(record.Text.Text("ru"), text);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_Document_Of_Every_Style_Is_Shipped_In_Every_Language()
    {
        foreach (var pack in Styles())
        {
            foreach (var lang in Loc.Languages)
            {
                var page = Path.Combine(DocRoot(), lang,
                    ExperiencePack.DocPageOf(pack.Code).Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(page), $"нет документа набора: doc/{lang}/packs/{pack.Code}.md");
            }
        }
    }

    [Fact]
    public void The_Records_Of_An_Installed_Style_Reach_The_Job_Prompt()
    {
        // ГЛАВНАЯ проверка задания: поставленный стиль виден агенту подходящего навыка
        var project = _f.Projects.Create("Проект T-272-S0", Path.Combine(_f.Dir, "prj272"), null, null);
        ExperiencePackSeed.Write(_f.Files);
        var pack = Packs.Get(ExperiencePackSeed.TddCode)!;

        Packs.Install(pack, ExperienceScopes.Project, project.Id, null, null, "ru");

        var block = JobOrchestrator.ProjectExperienceBlock(
            _f.Experience, project.Id, ["code-test"], "ru");
        Assert.Contains("Опыт проекта", block);
        Assert.Contains("Проверка пишется ПЕРВОЙ", block);
        // правило для того, кто пишет код, исполнителю с навыком code-test не достаётся
        Assert.DoesNotContain("проходит свою правку сам", block);
        // а исполнителю с навыком code-write — достаётся
        var forWriter = JobOrchestrator.ProjectExperienceBlock(
            _f.Experience, project.Id, ["code-write"], "ru");
        Assert.Contains("САМАЯ МАЛАЯ правка", forWriter);
    }

    /// <summary>Каталог поставляемой документации: <c>AI2P_app/doc</c>.</summary>
    private static string DocRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return Path.Combine(dir.FullName, "doc");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }
}
