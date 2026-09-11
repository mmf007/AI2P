using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-10-S0: УБОРКА ЗА СОБОЙ — ЛОГИ ПРОГОНОВ.
///
/// Жалоба заказчика: проект забился мусором. К августу 2026 каталог <c>test</c> занимал
/// 9,7 ГБ при 130 000 файлов (копии сборок, установки, данные стендов), а в КОРНЕ
/// репозитория лежало 359 файлов <c>*.log</c> от прогонов — разобрать, чей это лог и
/// нужен ли он, было уже нельзя.
///
/// Правила (doc/ref/AI2P_AgentTest.md, п. 4a): артефакты прогона исполнитель удаляет,
/// закончив задание, а логи пишет в каталог <c>logs/</c> с кодом задачи в имени. Здесь
/// сторожится вторая половина правила — та, которую видно без прогона.
///
/// Почему сторож смотрит только на КОРЕНЬ: внутри <c>test/tNNN/</c> лог живёт законно,
/// пока задание идёт, и красный там мешал бы работе; а в корне репозитория логам не
/// место никогда. Правило проверяется дёшево и чинится переносом файла в <c>logs/</c>.
///
/// И почему сторож считает ВОЗРАСТ, а не сам факт: каркасы прошлых задач до сих пор
/// пишут лог в корень по умолчанию, и лог ИДУЩЕГО прогона там появляется законно —
/// сторож, краснеющий от него, красит прогон у всех параллельных агентов выпуска и
/// каждый разбирает чужую правку (так трижды обожглись на <c>T237Tests</c>: T-244,
/// T-4-S0, T-3-S0; на этом же поймал меня агент T-11-S0). Мусор — это лог, который
/// ОСТАВИЛИ: старше 6 часов (полный прогон под тройной нагрузкой идёт около часа).
/// </summary>
public sealed class T10S0Tests
{
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

    /// <summary>Лог идущего прогона в корне — не нарушение; нарушение — оставленный лог.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    [Fact]
    public void Logs_Do_Not_Pile_Up_In_The_Repository_Root()
    {
        var root = RepoRoot();
        var now = DateTime.UtcNow;
        var found = Directory.GetFiles(root, "*.log", SearchOption.TopDirectoryOnly)
                             .Concat(Directory.GetFiles(root, "*.stackdump", SearchOption.TopDirectoryOnly))
                             .Where(f => now - File.GetLastWriteTimeUtc(f) > Fresh)
                             .Select(Path.GetFileName)
                             .ToArray();

        Assert.True(found.Length == 0,
            "логи прогонов кладутся в logs/, а не в корень репозитория (T-10-S0); "
            + "в корне брошены (правились более 6 часов назад): " + string.Join(", ", found));
    }

    [Fact]
    public void The_Logs_Directory_Exists_And_Explains_The_Rule()
    {
        var logs = Path.Combine(RepoRoot(), "logs");
        Assert.True(Directory.Exists(logs), "нет каталога logs/ — логи прогонов класть некуда");

        var readme = Path.Combine(logs, "readme.txt");
        Assert.True(File.Exists(readme), "в logs/ нет readme.txt с правилом именования логов");
        Assert.Contains("<код>-<что это>.log", File.ReadAllText(readme));
    }
}
