using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-294: УСТАНОВКА НА UBUNTU ОБРЫВАЛАСЬ НА ПУСТОЙ ПЕРЕМЕННОЙ.
///
/// Жалоба заказчика (обновление 1.99 → 1.101 на Ubuntu):
/// <code>
///   ./install.sh: 147: AI2P_HOME: parameter not set
/// </code>
/// и после этого версия оставалась прежней.
///
/// Причина. У всех скриптов выкладки стоит <c>set -u</c>, а <c>/bin/sh</c> на Ubuntu —
/// это dash: под <c>set -u</c> подстановка НЕЗАДАННОЙ переменной не даёт пустую строку,
/// а обрывает скрипт. Переменная <c>AI2P_HOME</c> задаёт рабочий каталог руками (T-287)
/// и в обычной установке не задана НИКОГДА — значит обрывалось ЛЮБОЕ обновление
/// на Linux/macOS, и то же самое было у <c>makeAsServise.sh</c>.
///
/// Вторая находка того же разбора: <c>${TARGET#~/}</c> тильду в ОБРАЗЦЕ раскрывает сама
/// оболочка (и dash, и bash), образец превращается в <c>/home/вы/</c>, префикс не совпадает
/// и в пути остаётся сама тильда — <c>~/ai/AI2P</c>, дошедший до скрипта буквально
/// (кавычки, юнит systemd, пусковой скрипт пакета), ставил программу
/// в <c>/home/вы/~/ai/AI2P</c>.
///
/// Скрипты — не C#, поэтому держатся ПО ТЕКСТУ (так же, как в <see cref="T271Tests"/>
/// и T285Tests). Главный здесь — сторож <see cref="Shell_Scripts_Do_Not_Read_Unset_Variables"/>:
/// он ловит не конкретную переменную, а само правило, поэтому следующая такая же
/// («у нас же всегда есть XDG_DATA_HOME») будет поймана до выпуска.
/// Живая проверка на настоящей dash — <c>test/t294/live294.py</c>.
/// </summary>
public sealed class T294Tests
{
    /// <summary>Каталог <c>AI2P_app</c>: рядом с ним лежат скрипты сборки и установки.</summary>
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string[] ShellScripts() =>
        Directory.GetFiles(AppDir(), "*.sh", SearchOption.TopDirectoryOnly);

    private static string Script(string name) => File.ReadAllText(Path.Combine(AppDir(), name));

    // тексты живут в каталоге (T-65-S0): в скрипте — ключ, фраза — в scripts.ru.txt
    private static string Catalogue() => File.ReadAllText(Path.Combine(AppDir(), "i18n", "scripts.ru.txt"));


    // ---------- разбор скрипта ----------

    /// <summary>Строки-пояснения выбрасываем: «AI2P_HOME=…» в комментарии не должно
    /// сходить за присваивание, иначе сторож ослепнет ровно там, где нужен.</summary>
    private static string CodeOnly(string text) =>
        string.Join("\n", text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static readonly Regex Read =
        new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)([^}]*)\}|\$([A-Za-z_][A-Za-z0-9_]*)");
    private static readonly Regex Assign = new(@"(?<![\w$-])([A-Za-z_][A-Za-z0-9_]*)=");
    private static readonly Regex For = new(@"\bfor\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b");
    private static readonly Regex ReadCmd = new(@"\bread\s+(?:-r\s+)?([A-Za-z_][A-Za-z0-9_]*)");

    /// <summary>Имена, которые скрипт ЧИТАЕТ, но нигде сам не задаёт и не подстраховывает
    /// умолчанием <c>${NAME:-…}</c>. Под <c>set -u</c> каждое такое имя — потенциальный обрыв.</summary>
    private static List<string> ExternalReads(string text)
    {
        var code = CodeOnly(text);
        var assigned = new HashSet<string>(Assign.Matches(code).Select(m => m.Groups[1].Value));
        assigned.UnionWith(For.Matches(code).Select(m => m.Groups[1].Value));
        assigned.UnionWith(ReadCmd.Matches(code).Select(m => m.Groups[1].Value));

        var bad = new SortedSet<string>();
        foreach (Match m in Read.Matches(code))
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value;
            if (name.Length == 0 || assigned.Contains(name))
            {
                continue;
            }
            var modifier = m.Groups[2].Value;
            if (modifier.StartsWith(":-") || modifier.StartsWith(":=") || modifier.StartsWith(":?"))
            {
                continue;                       // умолчание задано на месте
            }
            bad.Add(name);
        }
        return [.. bad];
    }

    // ---------- 1. само правило ----------

    /// <summary>ГЛАВНЫЙ СТОРОЖ: под <c>set -u</c> скрипт не вправе читать переменную,
    /// которой сам не задаёт. Либо нормализовать её в начале
    /// (<c>AI2P_HOME="${AI2P_HOME:-}"</c>), либо писать умолчание на месте
    /// (<c>${TMPDIR:-/tmp}</c>) — третьего не дано: иначе на машине, где переменной нет,
    /// скрипт молча обрывается на середине работы.</summary>
    [Fact]
    public void Shell_Scripts_Do_Not_Read_Unset_Variables()
    {
        var complaints = new List<string>();
        foreach (var path in ShellScripts())
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("set -u"))
            {
                continue;
            }
            var bad = ExternalReads(text);
            if (bad.Count > 0)
            {
                complaints.Add($"{Path.GetFileName(path)}: {string.Join(", ", bad)}");
            }
        }
        Assert.Empty(complaints);
    }

    /// <summary>Разбор сам по себе должен работать: на редакции «как было» (чтение
    /// <c>$AI2P_HOME</c> без нормализации) он обязан ругаться, иначе сторож зелен впустую.</summary>
    [Fact]
    public void The_Watchdog_Sees_The_Defect_It_Was_Written_For()
    {
        const string broken = """
                              set -u
                              own_dir="$1"
                              for place in "$AI2P_HOME" "$own_dir"; do
                                  echo "$place"
                              done
                              """;
        Assert.Equal(["AI2P_HOME"], ExternalReads(broken));

        const string fixedUp = """
                               set -u
                               AI2P_HOME="${AI2P_HOME:-}"
                               own_dir="$1"
                               for place in "$AI2P_HOME" "$own_dir"; do
                                   echo "$place"
                               done
                               """;
        Assert.Empty(ExternalReads(fixedUp));
    }

    // ---------- 2. то самое место жалобы ----------

    /// <summary>Установка и настройка службы нормализуют <c>AI2P_HOME</c> и <c>HOME</c>
    /// ОДИН РАЗ, в начале, — а не в каждом месте использования: строка поиска рабочего
    /// каталога (T-287) должна читаться как правило, а не как набор подстраховок.</summary>
    [Theory]
    [InlineData("install.sh")]
    [InlineData("makeAsServise.sh")]
    public void The_Working_Dir_Variables_Are_Normalized(string name)
    {
        var code = CodeOnly(Script(name));
        Assert.Contains("AI2P_HOME=\"${AI2P_HOME:-}\"", code);
        Assert.Contains("HOME=\"${HOME:-}\"", code);

        // нормализация идёт ДО первого чтения — иначе от неё нет никакого толку
        var normalized = code.IndexOf("AI2P_HOME=\"${AI2P_HOME:-}\"", StringComparison.Ordinal);
        var used = code.IndexOf("\"$AI2P_HOME\"", StringComparison.Ordinal);
        Assert.True(used > normalized, "нормализация должна стоять раньше первого чтения");
    }

    /// <summary>Каталог, куда ставим, по-прежнему ищется в тех же местах, что и у приложения
    /// (AppHome, T-287): нормализация переменной не должна была отменить сам поиск.</summary>
    [Fact]
    public void The_Search_For_The_Working_Config_Is_Intact()
    {
        var install = Script("install.sh");
        Assert.Contains("\"$AI2P_HOME\" \"$own_dir\" \"/var/lib/ai2p\" \"$HOME/.local/share/ai2p\"", install);
        Assert.Contains("/usr/*|/opt/*|/Applications/*", install);

        var service = Script("makeAsServise.sh");
        Assert.Contains("\"$AI2P_HOME\" \"$OWN_DIR\" \"/var/lib/ai2p\" \"$HOME/.local/share/ai2p\"", service);
    }

    // ---------- 3. вторая находка: тильда в образце ----------

    /// <summary>«~/ai/AI2P», дошедший до скрипта буквально, раскрывается в
    /// <c>$HOME/ai/AI2P</c>, а не в <c>$HOME/~/ai/AI2P</c>: тильду в ОБРАЗЦЕ
    /// (<c>${VAR#~/}</c>) оболочка раскрывает сама, поэтому её надо экранировать.</summary>
    [Fact]
    public void The_Tilde_In_The_Pattern_Is_Escaped()
    {
        foreach (var path in ShellScripts())
        {
            var code = CodeOnly(File.ReadAllText(path));
            Assert.DoesNotContain("#~/}", code);
        }
        Assert.Contains("${TARGET#\\~/}", Script("install.sh"));
        Assert.Contains("${TARGET#\\~/}", Script("makeAsServise.sh"));
        Assert.Contains("${OUTPUT#\\~/}", Script("buildRelease.sh"));
        Assert.Contains("${SRC_DIR#\\~/}", Script("MakePackage.sh"));
        Assert.Contains("${OUT_DIR#\\~/}", Script("MakePackage.sh"));
    }

    /// <summary>«~» без HOME раскрывать не от чего: вместо тихой установки в «/ai/AI2P»
    /// установка обязана сказать это словами и остановиться.</summary>
    [Fact]
    public void A_Tilde_Without_Home_Is_Refused_In_Words()
    {
        var install = CodeOnly(Script("install.sh"));
        Assert.Contains("\"~\"|\"~/\"*)", install);
        // текст с T-65-S0 в каталоге, в скрипте — ключ
        Assert.Contains("scr.inst.65", install);
        Assert.Contains("HOME не задана", Catalogue());
    }
}
