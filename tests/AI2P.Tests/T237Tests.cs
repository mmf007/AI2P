using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-237: ПУСТАЯ КОНСОЛЬ НА РАБОЧЕМ СТОЛЕ ВО ВРЕМЯ РАБОТЫ АГЕНТОВ.
///
/// Жалоба заказчика: пока работают ИИ-исполнители, на рабочем столе периодически
/// появляется чёрное окно консоли, в котором ничего не выводится, и висит там долго.
///
/// Разбор (см. doc/T-237_пустая_консоль.md): у консольного приложения Windows окно
/// берётся так — если при запуске не задано ни <c>CREATE_NO_WINDOW</c>, ни
/// <c>DETACHED_PROCESS</c>, процесс НАСЛЕДУЕТ консоль родителя, а если у родителя
/// консоли НЕТ ВОВСЕ — система выдаёт ему СВОЮ НОВУЮ И ВИДИМУЮ консоль. Отсюда два
/// правила, которые тут и сторожатся:
///
/// 1) продукт: любой запуск процесса с <c>UseShellExecute = false</c> обязан нести
///    <c>CreateNoWindow = true</c> — тогда у процесса есть СКРЫТАЯ консоль, и её
///    наследуют все его потомки (агент CLI → git bash → что угодно);
/// 2) скрипты живой проверки (<c>test/**/*.py</c>) не должны пускать стенды и прогоны
///    флагом <c>DETACHED_PROCESS</c>: у такого процесса консоли нет, и первый же его
///    потомок (dotnet test → testhost, powershell → dotnet publish) получает видимое
///    пустое окно на все минуты прогона. Замена — <c>CREATE_NO_WINDOW</c>
///    (см. test/lib/bg.py, опыты test/t237/exp.py и exp2.py).
/// </summary>
public sealed class T237Tests
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

    private static string SrcRoot() => Path.Combine(RepoRoot(), "AI2P_app", "src");

    /// <summary>Блок инициализатора <c>{ … }</c>, начинающийся после указанной позиции.</summary>
    private static string InitializerAt(string text, int from)
    {
        var open = text.IndexOf('{', from);
        if (open < 0)
        {
            return "";
        }
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[open..(i + 1)];
                }
            }
        }
        return text[open..];
    }

    /// <summary>Все места, где в продукте заводится ProcessStartInfo, — файл, строка, тело.</summary>
    private static List<(string File, int Line, string Body)> StartInfos()
    {
        var found = new List<(string, int, string)>();
        foreach (var file in Directory.GetFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin", StringComparison.Ordinal))
            {
                continue;
            }
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "ProcessStartInfo"))
            {
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                var lineStart = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                var head = text[lineStart..m.Index];
                if (head.Contains("//", StringComparison.Ordinal))
                {
                    continue; // упоминание в комментарии
                }
                var body = InitializerAt(text, m.Index);
                if (body.Contains("FileName", StringComparison.Ordinal))
                {
                    found.Add((Path.GetFileName(file), line, body));
                }
            }
        }
        return found;
    }

    [Fact]
    public void Every_Process_Start_In_The_Product_Hides_Its_Console()
    {
        var infos = StartInfos();
        Assert.NotEmpty(infos); // разбор сломался бы молча, если бы не нашлось ничего
        var bad = infos
            .Where(i => !i.Body.Contains("UseShellExecute = true", StringComparison.Ordinal))
            .Where(i => !Regex.IsMatch(i.Body, @"CreateNoWindow\s*=\s*true"))
            .Select(i => $"{i.File}:{i.Line}")
            .ToList();
        Assert.True(bad.Count == 0,
            "запуск процесса без CreateNoWindow = true даёт пустое окно консоли на рабочем столе "
            + "(T-237), поправьте: " + string.Join(", ", bad));
    }

    [Fact]
    public void Cli_Connector_Starts_Claude_Without_A_Console_Window()
    {
        // запуск Claude CLI живёт в CliProcess (todo96): им пользуются и коннектор агента,
        // и вход в CLI из AI2P — окна консоли не должно давать ни то, ни другое
        var file = Path.Combine(SrcRoot(), "AI2P.Connectors", "CliProcess.cs");
        var text = File.ReadAllText(file);
        Assert.Contains("CreateNoWindow = true", text);
        // и claude, и фолбэк через cmd.exe строятся ОДНИМ BuildStartInfo — иначе один
        // из двух путей запуска однажды окажется без CreateNoWindow
        Assert.Single(Regex.Matches(text, @"UseShellExecute\s*="));
        // а сам коннектор своего запуска процессов больше не имеет
        Assert.DoesNotContain("UseShellExecute",
            File.ReadAllText(Path.Combine(SrcRoot(), "AI2P.Connectors", "ClaudeCliConnector.cs")));
    }

    [Fact]
    public void Live_Check_Scripts_Do_Not_Use_Detached_Process()
    {
        var testDir = Path.Combine(RepoRoot(), "test");
        if (!Directory.Exists(testDir))
        {
            return; // в выкладке скриптов живой проверки нет — проверять нечего
        }
        var bad = Directory.GetFiles(testDir, "*.py", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("bg.py", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}t237{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            // ЧУЖОЙ код внутри стенда сторожит не этот тест (T-4-S0): стенд установки моделей
            // распаковывает к себе настоящие пакеты, и в стандартной библиотеке Python есть
            // свой subprocess.py со словом DETACHED_PROCESS. Правило — про НАШИ скрипты
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}packages{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("DETACHED_PROCESS", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(testDir, f))
            .ToList();
        Assert.True(bad.Count == 0,
            "стенд, пущенный DETACHED_PROCESS, выдаёт своим потомкам пустое ВИДИМОЕ окно консоли "
            + "(T-237); берите CREATE_NO_WINDOW (test/lib/bg.py). Файлы: " + string.Join(", ", bad));
    }
}
