using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-289-S0 (ветка T-285-S0): ПРАВИЛА СОСТАВЛЕНИЯ УПРАВЛЯЮЩЕГО JSON для ACE-Step 1.5 —
/// поставляются вместе с моделью.
///
/// Правила читает МОДЕЛЬ-СУФЛЁР: <c>PrompterService</c> берёт файл, названный в
/// <c>prompter.rules</c> профайла рабочей модели, и кладёт его текст в промпт. Поэтому
/// «правила есть» значит ровно три вещи: файл пишется сидом, лежит там, куда показывает
/// профайл, и не реплицируется (иначе на двух серверах разных версий он даст конфликт,
/// наука T-261-S0).
///
/// В опыт проекта эти правила уносить НЕЛЬЗЯ: медиа-модель опыта не видит вовсе
/// (наука 15ccab72), а суфлёру правила передаются явно, файлом.
///
/// <list type="number">
/// <item>СИД: у всех трёх записей ACE-Step файл правил написан и лежит рядом с профайлом;</item>
/// <item>ПУТЬ: <c>prompter.rules</c> показывает на существующий файл, а не на выдумку;</item>
/// <item>СОДЕРЖАНИЕ: названо каждое поле схемы, и про <c>tags</c> сказано главное —
/// это стилевые тэги, а не пересказ задания;</item>
/// <item>РЕПЛИКАЦИЯ: файл дистрибутива не переносится, а такой же файл КАСТОМНОЙ записи —
/// переносится (его партнёру взять больше неоткуда);</item>
/// <item>ПОВТОРНЫЙ СИД не затирает правку человека: версия файла живёт отметкой
/// <c>&lt;!-- _seed: N --&gt;</c>, а не полем json, которого в markdown не бывает.</item>
/// </list>
/// </summary>
public sealed class T289S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public T289S0Tests() => _f.Models.Seed();

    public void Dispose() => _f.Dispose();

    /// <summary>Поля управляющего json ACE-Step 1.5 — те же, что в схеме профайла (T-286-S0)
    /// и в узле <c>TextEncodeAceStepAudio1.5</c> живого ComfyUI (T-287-S0).</summary>
    private static readonly string[] Fields =
        ["tags", "lyrics", "duration", "language", "bpm", "keyscale", "timesignature"];

    private List<AiModel> AskingPrompter() =>
        _f.Models.ListWithSkills().Where(m => m.Prompter.Required).ToList();

    [Fact]
    public void Every_Ace_Step_Record_Ships_Its_Prompter_Rules()
    {
        var models = AskingPrompter();
        Assert.Equal(3, models.Count);

        foreach (var model in models)
        {
            var rules = model.Prompter.Rules;
            Assert.Equal(AiModelService.PrompterRulesPathOf(model.Id), rules);
            // файл лежит РЯДОМ С ПРОФАЙЛОМ, в том же каталоге данных
            Assert.Equal(Path.GetDirectoryName(model.ProfilePath), Path.GetDirectoryName(rules));

            var abs = Path.Combine(_f.Db.DataDir, rules.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(abs), $"правила суфлёра не написаны сидом: {rules}");

            var text = File.ReadAllText(abs);
            Assert.True(text.Length > 500, $"правила суфлёра пусты или обрывок: {rules}");
            foreach (var field in Fields)
            {
                Assert.Contains(field, text, StringComparison.Ordinal);
                Assert.NotNull(model.Prompter.Field(field));
            }
            // главное, ради чего правила и заведены: описание задачи в tags пересказывать не надо
            Assert.Contains("СТИЛЕВЫЕ", text, StringComparison.Ordinal);
            // и вариант назван — файлов три, и перепутать их человеку нельзя
            Assert.Contains(model.Name, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Файл ДИСТРИБУТИВА не реплицируется: его пишет сид на каждом сервере сам, а пока
    /// версии программы разные, у двух серверов он разный — и в конфликте по нему человеку
    /// нечего выбирать. Кастомная запись исключением не затронута намеренно.
    /// </summary>
    [Fact]
    public void The_Rules_Of_A_Shipped_Record_Are_Not_Replicated()
    {
        foreach (var model in AskingPrompter())
        {
            Assert.True(FileManifest.IsDistributionFile(model.Prompter.Rules),
                $"правила суфлёра поедут репликацией и дадут конфликт: {model.Prompter.Rules}");
        }
        Assert.False(FileManifest.IsDistributionFile(
            AiModelService.PrompterRulesPathOf("11111111-2222-3333-4444-555555555555")));
    }

    /// <summary>
    /// Повторный сид (он идёт при каждом открытии организации) правку человека не трогает:
    /// версия файла прочитана из отметки. Без отметки markdown читался бы как «версии нет»,
    /// и файл переписывался бы при каждом старте.
    /// </summary>
    [Fact]
    public void A_Second_Seed_Keeps_The_Edited_Rules()
    {
        var model = AskingPrompter()[0];
        var abs = Path.Combine(_f.Db.DataDir,
            model.Prompter.Rules.Replace('/', Path.DirectorySeparatorChar));
        var edited = File.ReadAllText(abs) + "\n\nМоя приписка: всегда минор.\n";
        File.WriteAllText(abs, edited);

        _f.Models.Seed();

        Assert.Equal(edited, File.ReadAllText(abs));
    }
}
