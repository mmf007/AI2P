using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-554-S0: близость записи опыта к тексту задачи. Отбор опыта до этой правки на саму
/// задачу не смотрел, и любая задача проекта получала одни и те же записи по свежести.
/// </summary>
public class T554S0Tests
{
    private static ExperienceRecord Rec(string id, string text) => new() { Id = id, Text = text };

    private static List<ExperienceRecord> Noise(int count) => Enumerable.Range(0, count)
        .Select(i => Rec("n" + i, "Задача и тест: файл проекта номер " + i)).ToList();

    [Fact]
    public void A_Record_Sharing_Rare_Words_With_The_Task_Is_On_Topic()
    {
        var records = Noise(20);
        records.Add(Rec("hit", "Репликация журнала изменений между серверами кластера"));
        var fit = ExperienceRelevance.Score(records, "Починить репликацию журнала на втором сервере");
        Assert.True(fit["hit"].OnTopic);
        Assert.True(fit["hit"].Score > fit["n3"].Score);
    }

    [Fact]
    public void Common_Words_Do_Not_Make_A_Record_On_Topic()
    {
        var records = Noise(20);
        var fit = ExperienceRelevance.Score(records, "Задача: тест файла проекта");
        Assert.All(fit.Values, f => Assert.False(f.OnTopic));
    }

    [Fact]
    public void A_Task_Without_Words_Gives_No_Verdict()
    {
        Assert.Empty(ExperienceRelevance.Score(Noise(5), "  — ! "));
    }
}
