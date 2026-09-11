using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-243: СБОРЩИК БИЛДОВ — каталог операционной системы в выкладке.
///
/// Между <c>builds</c> и <c>release</c>/<c>releasefull</c> добавлен подкаталог той системы,
/// под которую собрана выкладка:
///
///     builds/windows/release        builds/windows/releasefull
///     builds/linux/release          builds/linux/releasefull
///     builds/macos/release          builds/macos/releasefull
///
/// Зачем: выкладка привязана к платформе не только рантаймом (полная), но и скриптами
/// установки — с T-211 в неё кладутся <c>install.ps1</c>+<c>install.cmd</c> ЛИБО
/// <c>install.sh</c>, а чужие удаляются. Пока каталог был один, сборка под Linux затирала
/// в нём выкладку для Windows, и на диске оставалась смесь, у которой уже не спросишь,
/// чья она. Теперь каждая система живёт в своём каталоге.
///
/// Система определяется ОДИН раз и служит сразу двум решениям — каталогу и набору скриптов
/// установки: у полной выкладки её задаёт RID (<c>-Runtime</c>/<c>--runtime</c>), у обычной —
/// та система, на которой идёт сборка.
///
/// Скрипты — не C#, поэтому проверяются по своему тексту (так же, как в <see cref="T211Tests"/>):
/// тест держит договорённости, на которые опирается выпуск, и краснеет, если каталог
/// переименуют молча.
/// </summary>
public sealed class T243Tests
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

    private static string Script(string name) => File.ReadAllText(Path.Combine(AppDir(), name));

    /// <summary>Каталог по умолчанию — с подкаталогом ОС между builds и release.</summary>
    [Fact]
    public void Default_Package_Directory_Has_The_Os_Level()
    {
        Assert.Contains("\"builds/$targetOs/release\"", Script("buildRelease.ps1"));
        Assert.Contains("\"builds/$targetOs/releasefull\"", Script("buildRelease.ps1"));
        Assert.Contains("\"builds/$TARGET_OS/release\"", Script("buildRelease.sh"));
        Assert.Contains("\"builds/$TARGET_OS/releasefull\"", Script("buildRelease.sh"));
    }

    /// <summary>Прежнего пути без каталога ОС в скриптах не осталось: иначе часть выкладок
    /// уезжала бы мимо (и молча — каталог создаётся сам, ошибки не будет).</summary>
    [Fact]
    public void The_Old_Flat_Path_Is_Gone()
    {
        foreach (var name in new[] { "buildRelease.ps1", "buildRelease.sh" })
        {
            var text = Script(name);
            Assert.DoesNotContain("\"builds/release\"", text);
            Assert.DoesNotContain("\"builds/releasefull\"", text);
            // и прежнего каталога СНАРУЖИ рабочего тоже не осталось (T-131-S0)
            Assert.DoesNotContain("\"../builds/", text);
        }
    }

    /// <summary>Имена каталогов ровно те, что названы в задании: windows, linux, macos.
    /// В частности macos, а не osx: RID у Apple — osx-*, и назвать каталог по RID
    /// было бы естественной ошибкой.</summary>
    [Fact]
    public void The_Os_Directories_Are_Named_Windows_Linux_Macos()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        foreach (var os in new[] { "windows", "linux", "macos" })
        {
            Assert.Contains($"\"{os}\"", ps);
            Assert.Contains($"TARGET_OS=\"{os}\"", sh);
        }

        // RID у Apple — osx-*, а каталог называется macos
        Assert.Contains("\"osx-*\" { \"macos\"", ps);
        Assert.Contains("osx-*) TARGET_OS=\"macos\"", sh);
    }

    /// <summary>У ПОЛНОЙ выкладки систему задаёт RID, у обычной — система сборки.
    /// Иначе выкладка под чужую платформу легла бы в каталог своей.</summary>
    [Fact]
    public void The_Os_Comes_From_The_Rid_For_A_Full_Package()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        // ветка «полная выкладка» разбирает именно $Runtime / $RUNTIME
        Assert.Matches(new Regex(@"if \(\$SelfContained\) \{\s*\$targetOs = switch -Wildcard \(\$Runtime\)"), ps);
        Assert.Matches(new Regex("if \\[ \"\\$SELF_CONTAINED\" -eq 1 \\]; then\\s*case \"\\$RUNTIME\" in"), sh);

        // ветка «обычная выкладка» смотрит на систему, где идёт сборка
        Assert.Contains("elseif ($IsLinux) { $targetOs = \"linux\" }", ps);
        Assert.Contains("elseif ($IsMacOS) { $targetOs = \"macos\" }", ps);
        Assert.Contains("case \"$(uname -s)\" in", sh);
        Assert.Contains("Darwin)", sh);
    }

    /// <summary>Каталог ОС и набор скриптов установки считаются от ОДНОЙ величины: разъехавшись,
    /// они дали бы выкладку в каталоге linux со скриптами Windows внутри.</summary>
    [Fact]
    public void The_Same_Os_Decides_The_Directory_And_The_Install_Scripts()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        Assert.Contains("$targetWindows = ($targetOs -eq \"windows\")", ps);
        Assert.Contains("case \"$TARGET_OS\" in\n    windows) INSTALL_SCRIPTS=\"install.ps1 install.cmd\"",
            sh.Replace("\r\n", "\n"));

        // прежний разбор RID в этом месте убран — он повторял бы вычисление ОС третий раз
        Assert.DoesNotContain("$targetWindows = if ($SelfContained)", ps);
    }

    /// <summary>Система выкладки печатается в консоль: человек, запустивший сборку, должен
    /// видеть, куда она легла, не вычисляя это по ключам.</summary>
    [Fact]
    public void The_Script_Says_Which_Os_It_Builds_For()
    {
        Assert.Contains("Операционная система выкладки: $targetOs", Script("buildRelease.ps1"));
        Assert.Contains("Операционная система выкладки: $TARGET_OS", Script("buildRelease.sh"));
    }
}
