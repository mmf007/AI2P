using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-285: ИНСТАЛЯТОР — сборщик выкладки переименован, рядом появился сборщик пакета.
///
/// Две вещи сразу:
///
///   1. <c>publish.ps1/.cmd/.sh</c> стали <c>buildRelease.ps1/.cmd/.sh</c>. Имя сменилось,
///      поведение прежнее; старых файлов в каталоге остаться не должно, иначе рядом лежат
///      два сборщика и непонятно, который из них живой.
///   2. <c>MakePackage.ps1/.cmd</c> (Windows) и <c>MakePackage.sh</c> (Linux/macOS) делают
///      из ГОТОВОЙ выкладки один файл установщика. Запускаются ИЗ каталога выкладки —
///      значит их туда кладёт <c>buildRelease</c>, по тому же правилу платформы, что и
///      скрипты установки. Результат ложится в <c>../../packages</c>.
///
/// Имя пакета складывается само, из <c>version.json</c> выкладки:
///
///     releasefull -> AI2P_full_v_1_NN_win64.exe / _Linux.run / _MacOs.run
///     release     -> AI2P_v_1_NN_win64.exe      / _Linux.run / _MacOs.run
///
/// Скрипты — не C#, поэтому держатся тестом по своему тексту (как <see cref="T211Tests"/>
/// и <see cref="T243Tests"/>): договорённости выпуска не должны меняться молча.
/// </summary>
public sealed class T285Tests
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

    // тексты живут в каталоге (T-65-S0): в скрипте — ключ, фраза — в scripts.ru.txt
    private static string Catalogue() => File.ReadAllText(Path.Combine(AppDir(), "i18n", "scripts.ru.txt"));


    // ---------- 1. переименование ----------

    /// <summary>Сборщик выкладки называется buildRelease, а publish.* больше нет.</summary>
    [Fact]
    public void The_Release_Builder_Is_Called_BuildRelease()
    {
        var app = AppDir();
        foreach (var name in new[] { "buildRelease.ps1", "buildRelease.cmd", "buildRelease.sh" })
        {
            Assert.True(File.Exists(Path.Combine(app, name)), $"нет скрипта {name}");
        }
        foreach (var old in new[] { "publish.ps1", "publish.cmd", "publish.sh" })
        {
            Assert.False(File.Exists(Path.Combine(app, old)),
                $"старый {old} остался рядом с buildRelease — их станет два");
        }
    }

    /// <summary>Обёртка .cmd зовёт переименованный .ps1, а не прежний publish.ps1.</summary>
    [Fact]
    public void The_Cmd_Wrapper_Calls_The_Renamed_Script()
    {
        var cmd = Script("buildRelease.cmd");
        Assert.Contains("buildRelease.ps1", cmd);
        Assert.DoesNotContain("publish.ps1", cmd);
    }

    /// <summary>Ни один скрипт не ЗОВЁТ больше publish.* — иначе вызов молча не найдёт файла,
    /// а увидят это только на выпуске.
    ///
    /// Смотрим только на КОД: строку-пояснение «до T-285 назывался publish.ps1» такой сторож
    /// обязан пропускать, иначе он краснеет на собственном объяснении (наука T-244).</summary>
    [Fact]
    public void Nothing_Calls_The_Old_Publish_Scripts_Anymore()
    {
        var app = AppDir();
        var files = new List<string>();
        foreach (var mask in new[] { "*.ps1", "*.cmd", "*.sh", "*.bat" })
        {
            files.AddRange(Directory.GetFiles(app, mask));
        }

        foreach (var file in files)
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.TrimStart();
                if (line.StartsWith("#") || line.StartsWith("::")
                    || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // пояснение, а не вызов
                }
                foreach (var old in new[] { "publish.ps1", "publish.cmd", "publish.sh" })
                {
                    Assert.False(line.Contains(old),
                        $"{Path.GetFileName(file)} всё ещё зовёт {old}: {line}");
                }
            }
        }
    }

    // ---------- 2. сборщик пакета лежит в выкладке ----------

    /// <summary>MakePackage есть под обе платформы: без него из выкладки нечем сделать пакет.</summary>
    [Fact]
    public void The_Package_Builder_Exists_For_Both_Platforms()
    {
        var app = AppDir();
        foreach (var name in new[] { "MakePackage.ps1", "MakePackage.cmd", "MakePackage.sh" })
        {
            Assert.True(File.Exists(Path.Combine(app, name)), $"нет скрипта {name}");
        }
        // обёртка .cmd — по тому же поводу, что у install.cmd: у .ps1 в Windows
        // ассоциация «редактировать», по Enter из проводника он не выполняется
        Assert.Contains("MakePackage.ps1", Script("MakePackage.cmd"));
    }

    /// <summary>Сборщик пакета кладётся В ВЫКЛАДКУ и по тому же правилу платформы, что и
    /// скрипты установки: запускать его надо оттуда, значит он там и обязан оказаться.</summary>
    [Fact]
    public void BuildRelease_Puts_The_Package_Builder_Into_The_Package()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        Assert.Contains("@(\"MakePackage.ps1\", \"MakePackage.cmd\")", ps);
        Assert.Contains("@(\"MakePackage.sh\")", ps);
        Assert.Contains("PACKAGE_SCRIPTS=\"MakePackage.sh\"", sh);
        Assert.Contains("PACKAGE_SCRIPTS=\"MakePackage.ps1 MakePackage.cmd\"", sh);

        // чужой сборщик от прежней выкладки в каталоге остаться не должен —
        // то же правило, что у install.* (T-211)
        Assert.Contains("$allScripts -notcontains $script", ps);
        Assert.Contains("MakePackage.ps1 MakePackage.cmd MakePackage.sh", sh);
    }

    // ---------- 3. имя пакета ----------

    /// <summary>Имя складывается из вида выкладки, версии и системы — руками ничего не вводят.</summary>
    [Fact]
    public void The_Package_Name_Follows_The_Rule()
    {
        var ps = Script("MakePackage.ps1");
        var sh = Script("MakePackage.sh");

        foreach (var text in new[] { ps, sh })
        {
            Assert.Contains("AI2P_full_v_", text);   // releasefull
            Assert.Contains("AI2P_v_", text);        // release
            Assert.Contains("win64", text);
            Assert.Contains("Linux", text);
            Assert.Contains("MacOs", text);
        }
        // номер билда NN — это вторая часть версии; версия берётся из version.json выкладки
        Assert.Contains("version.json", ps);
        Assert.Contains("version.json", sh);
        Assert.Contains("$version -replace '\\.', '_'", ps);
        Assert.Contains("tr '.' '_'", sh);
    }

    /// <summary>Результат ложится в ../../packages от каталога выкладки — то есть builds/packages.</summary>
    [Fact]
    public void The_Package_Goes_To_The_Packages_Directory()
    {
        Assert.Contains("..\\..\\packages", Script("MakePackage.ps1"));
        Assert.Contains("$SRC_DIR/../../packages", Script("MakePackage.sh"));
    }

    /// <summary>Пакет собирается на СВОЕЙ системе: из Windows не сделать .run и наоборот.
    /// Скрипт обязан это заметить и сказать, а не собрать бессмыслицу.</summary>
    [Fact]
    public void A_Package_For_Another_Os_Is_Refused()
    {
        Assert.Contains("MakePackage.sh", Script("MakePackage.ps1"));
        Assert.Contains("MakePackage.cmd", Script("MakePackage.sh"));
        Assert.Contains("$osTag -notlike \"win*\"", Script("MakePackage.ps1"));
        // текст с T-65-S0 в каталоге, в скрипте — ключ
        Assert.Contains("scr.pkg.36", Script("MakePackage.sh"));
        Assert.Contains("Пакет для Windows собирается на Windows", Catalogue());
    }

    // ---------- 4. что в пакет не попадает ----------

    /// <summary>Данные пользователя в дистрибутив не попадают — тот же список, что у install.*
    /// (data, logs, secrets, secrets.json) плюс опись прошлой установки.</summary>
    [Fact]
    public void User_Data_Never_Gets_Into_The_Package()
    {
        var ps = Script("MakePackage.ps1");
        var sh = Script("MakePackage.sh");

        Assert.Contains("@(\"data\", \"logs\", \"secrets\")", ps);
        Assert.Contains("\"config.json\", \"secrets.json\", \"installed.json\"", ps);
        Assert.Contains("data|logs|secrets|secrets.json|installed.json", sh);
    }

    /// <summary>Рабочий config.json установщик не затирает: кладёт только если его нет,
    /// а рядом всегда кладёт config.new.json — слияние делает приложение (ConfigMerge).</summary>
    [Fact]
    public void The_Working_Config_Is_Not_Overwritten()
    {
        var ps = Script("MakePackage.ps1");
        Assert.Contains("onlyifdoesntexist", ps);
        Assert.Contains("config.new.json", ps);
        // каталоги дистрибутива переписываются начисто — как в install.ps1
        Assert.Contains("[InstallDelete]", ps);
        Assert.Contains("{app}\\doc", ps);
        Assert.Contains("{app}\\wwwroot", ps);
    }

    /// <summary>Вопрос о рантайме задаётся SuppressibleMsgBox: обычный MsgBox повесил бы
    /// ТИХУЮ установку (/VERYSILENT), нажать его там некому.</summary>
    [Fact]
    public void The_Runtime_Question_Does_Not_Hang_A_Silent_Install()
    {
        var ps = Script("MakePackage.ps1");
        Assert.Contains("SuppressibleMsgBox", ps);
        Assert.Contains("Microsoft.AspNetCore.App 8.", ps);
        // и задаётся он только обычной выкладке: полной рантайм не нужен вовсе (T-211)
        Assert.Contains("if (-not $selfContained)", ps);
    }

    // ---------- 5. чем это собирается ----------

    /// <summary>Инструменты сборки пакета прописаны в install_required.* — иначе на новой
    /// машине MakePackage не соберёт ничего, а человек не поймёт, чего ему не хватает.</summary>
    [Fact]
    public void Package_Tools_Are_Listed_In_Install_Required()
    {
        var bat = Script("install_required.bat");
        var sh = Script("install_required.sh");

        Assert.Contains("JRSoftware.InnoSetup", bat);
        Assert.Contains("MakePackage", bat);
        Assert.Contains("makeself", sh);
        Assert.Contains("MakePackage", sh);

        // и сами сборщики зовут именно их
        Assert.Contains("ISCC.exe", Script("MakePackage.ps1"));
        Assert.Contains("makeself", Script("MakePackage.sh"));
    }
}
