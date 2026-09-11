using AI2P.Core;
using AI2P.Server;
using AI2P.Server.Api;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-211 (версия 1.82): БИЛД И УСТАНОВКА.
///
/// Три правки, связанные одной темой «поставить и запустить»:
///
/// 1. ВЫКЛАДКА С РАНТАЙМОМ ВНУТРИ — ключ скрипта выкладки (<c>-SelfContained</c> / <c>full</c>,
///    в sh <c>--self-contained</c>). Результат идёт в ОТДЕЛЬНЫЙ каталог
///    <c>builds/releasefull</c>, а <c>builds/release</c> остаётся прежней выкладкой без
///    рантайма. Смешивать их в одном каталоге нельзя: рантайм полной остался бы мусором
///    в обычной. Признак вида выкладки едет в <c>version.json</c> (<c>selfContained</c>) —
///    по нему установка решает, проверять ли .NET.
///
/// 2. СКРИПТЫ УСТАНОВКИ — только своей платформы: сборка под Windows не кладёт в выкладку
///    <c>install.sh</c>, сборка под Linux/macOS — <c>install.ps1</c> и <c>install.cmd</c>.
///
/// 3. ПРОВЕРКА РАНТАЙМА в установке обычной выкладки: нет ASP.NET Core 8.x — предложить
///    поставить (winget на Windows, apt/dnf либо dotnet-install.sh на Linux/macOS).
///    В полной выкладке проверки нет вовсе.
///
/// 4. ФОРМА ПЕРВОГО СТАРТА: поле «имя сервера» приходит заполненным (<c>localhost</c>),
///    а не пустым, как было с T-139.
///
/// Скрипты — не C#, поэтому проверяются по своему тексту: тест держит договорённости,
/// на которые опирается выпуск, и краснеет, если ключ или каталог переименуют молча.
/// </summary>
public sealed class T211Tests
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


    // ---------- 1. выкладка с рантаймом внутри ----------

    /// <summary>Вид выкладки — КЛЮЧ запуска, а не отдельный скрипт: и в Windows, и в POSIX.</summary>
    [Fact]
    public void Publish_Takes_The_Runtime_Inside_As_A_Switch()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        Assert.Contains("[switch]$SelfContained", ps);
        Assert.Contains("--self-contained|--full", sh);

        // ключ доходит до dotnet publish, иначе рантайма внутри не окажется
        Assert.Contains("--self-contained", ps);
        Assert.Contains("\"-r\", $Runtime", ps);
        Assert.Contains("-r \"$RUNTIME\" --self-contained true", sh);
    }

    /// <summary>Каталоги РАЗНЫЕ: releasefull для полной, release — как было.
    /// С T-243 оба лежат внутри каталога своей ОС (<c>builds/&lt;ос&gt;/…</c>), но правило
    /// «вид выкладки выбирает каталог» от этого не изменилось.</summary>
    [Fact]
    public void Full_Package_Goes_To_Releasefull_And_Plain_One_Stays_In_Release()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        Assert.Contains("/releasefull\"", ps);
        Assert.Contains("/release\"", ps);
        Assert.Contains("/releasefull\"", sh);
        Assert.Contains("/release\"", sh);

        // выбор каталога зависит именно от вида выкладки, а не задан жёстко
        // с T-131-S0 каталог builds лежит ВНУТРИ рабочего каталога (рядом с AI2P.sln),
        // а не на уровень выше него — «..» из пути ушли
        Assert.Contains("if ($SelfContained) { \"builds/$targetOs/releasefull\" } else { \"builds/$targetOs/release\" }", ps);
        Assert.Contains("if [ \"$SELF_CONTAINED\" -eq 1 ]; then OUTPUT=\"builds/$TARGET_OS/releasefull\"", sh);
    }

    /// <summary>Обёртка .cmd умеет то же самое (у .ps1 в Windows ассоциация «редактировать»).</summary>
    [Fact]
    public void Cmd_Wrapper_Forwards_The_Full_Switch()
    {
        var cmd = Script("buildRelease.cmd");

        Assert.Contains("buildRelease.ps1", cmd);
        Assert.Contains("\"%~1\"==\"full\"", cmd);
        Assert.Contains("set \"PUBFULL=-SelfContained\"", cmd);
        Assert.Contains("%PUBFULL%", cmd);
    }

    /// <summary>Вид выкладки записан в version.json — иначе установка о нём не узнает.</summary>
    [Fact]
    public void Package_Says_Whether_The_Runtime_Is_Inside()
    {
        Assert.Contains("selfContained = [bool]$SelfContained", Script("buildRelease.ps1"));
        Assert.Contains("\"selfContained\": $SC_JSON", Script("buildRelease.sh"));
    }

    /// <summary>
    /// ВЫКЛАДКА ПОД ЧУЖУЮ ПЛАТФОРМУ. Номер версии <c>buildRelease</c> спрашивает у собранной
    /// программы — но собранную для linux-x64 на Windows не запустить (и наоборот),
    /// а у самодостаточной выкладки и <c>dotnet AI2P.Server.dll</c> не годится: dll привязана
    /// к своему рантайму. Без запасного пути выкладка под чужую платформу обрывается на
    /// version.json — поймано живым прогоном. Запасной путь — тот же исходник, из которого
    /// только что собрали; регулярное выражение обоих скриптов обязано его разбирать.
    /// </summary>
    [Fact]
    public void Version_Falls_Back_To_The_Source_For_A_Foreign_Platform()
    {
        Assert.Contains("AppInfo.cs", Script("buildRelease.ps1"));
        Assert.Contains("AppInfo.cs", Script("buildRelease.sh"));

        var appInfo = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Core", "AppInfo.cs"));

        // ровно то выражение, что стоит в buildRelease.ps1
        var ps = System.Text.RegularExpressions.Regex.Match(appInfo, "Version\\s*=\\s*\"([^\"]+)\"");
        Assert.True(ps.Success, "buildRelease.ps1 не нашёл бы версию в AppInfo.cs");
        Assert.Equal(AppInfo.Version, ps.Groups[1].Value);

        // и то, что стоит в buildRelease.sh (sed берёт ПЕРВОЕ совпадение — оно же должно быть верным)
        var first = System.Text.RegularExpressions.Regex.Matches(appInfo, "Version\\s*=\\s*\"([^\"]+)\"")[0];
        Assert.Equal(AppInfo.Version, first.Groups[1].Value);
    }

    // ---------- 2. скрипты установки только своей платформы ----------

    [Fact]
    public void Package_Carries_Only_Its_Own_Platform_Install_Scripts()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        // Windows: .ps1 + .cmd, ни одного .sh
        Assert.Contains("@(\"install.ps1\", \"install.cmd\")", ps);
        Assert.Contains("@(\"install.sh\")", ps);

        // платформа считается ОДИН раз (T-243) и служит и каталогу ОС, и выбору скриптов
        Assert.Contains("$targetWindows = ($targetOs -eq \"windows\")", ps);

        // Linux/macOS: только .sh, а под win-RID — наоборот
        Assert.Contains("INSTALL_SCRIPTS=\"install.sh\"", sh);
        Assert.Contains("INSTALL_SCRIPTS=\"install.ps1 install.cmd\"", sh);
        Assert.Contains("case \"$TARGET_OS\" in", sh);
    }

    /// <summary>Чужой скрипт от прошлой выкладки в каталоге остаться не должен: каталог
    /// переиспользуется, и install.sh рядом с install.cmd — ровно то, что убрали.</summary>
    [Fact]
    public void Foreign_Install_Scripts_Are_Removed_From_The_Package()
    {
        // с T-285 в том же списке ходят и MakePackage.* — правило одно на оба вида скриптов
        Assert.Contains("if ($allScripts -notcontains $script)", Script("buildRelease.ps1"));
        Assert.Contains("rm -f \"$OUT_DIR/$script\"", Script("buildRelease.sh"));
    }

    // ---------- 3. проверка рантайма в установке ----------

    /// <summary>Обычная выкладка без рантайма не запустится — установка это проверяет
    /// и предлагает поставить, а не молчит.</summary>
    [Fact]
    public void Install_Checks_The_Runtime_And_Offers_To_Install_It()
    {
        var ps = Script("install.ps1");
        var sh = Script("install.sh");

        // проверяется ровно ветка 8.x: на рантайме 9/10 интерактивность Blazor молча умирает
        Assert.Contains("Microsoft.AspNetCore.App 8.", ps);
        Assert.Contains("Microsoft\\.AspNetCore\\.App 8\\.", sh);

        // предложение поставить. Текст с T-65-S0 лежит в каталоге, в скрипте стоит ключ
        Assert.Contains("scr.inst.13", ps);
        Assert.Contains("scr.inst.13", sh);
        Assert.Contains("Поставить его сейчас?", Catalogue());
        Assert.Contains("Microsoft.DotNet.AspNetCore.8", ps);   // winget
        Assert.Contains("aspnetcore-runtime-8.0", sh);          // apt/dnf
        Assert.Contains("dotnet-install.sh", sh);               // всё остальное

        // проверку можно выключить
        Assert.Contains("[switch]$NoRuntimeCheck", ps);
        Assert.Contains("--no-runtime-check", sh);
    }

    /// <summary>В полной выкладке проверять нечего — рантайм лежит внутри неё.</summary>
    [Fact]
    public void Full_Package_Install_Does_Not_Check_The_Runtime()
    {
        var ps = Script("install.ps1");
        var sh = Script("install.sh");

        Assert.Contains("if ($info.selfContained -eq $true)", ps);
        Assert.Contains("scr.inst.8", ps);

        Assert.Contains("\"selfContained\"[[:space:]]*:[[:space:]]*true", sh);
        Assert.Contains("scr.inst.8", sh);
        Assert.Contains("Рантайм внутри выкладки — проверка .NET не нужна.", Catalogue());
    }

    /// <summary>Проверка идёт ДО копирования: узнать о недостающем рантайме полезнее до того,
    /// как файлы легли в приёмник, а под -WhatIf ничего не ставится вовсе.</summary>
    [Fact]
    public void Runtime_Is_Checked_Before_Copying_And_Never_Installed_Under_WhatIf()
    {
        var ps = Script("install.ps1");
        var sh = Script("install.sh");

        Assert.True(ps.IndexOf("Confirm-Runtime $newInfo", StringComparison.Ordinal)
            < ps.IndexOf("--- копирование ---", StringComparison.Ordinal));
        Assert.True(sh.IndexOf("confirm_runtime", StringComparison.Ordinal)
            < sh.IndexOf("--- копирование ---", StringComparison.Ordinal));

        // один ключ на оба скрипта: имя ключа подставляется в текст ({0})
        Assert.Contains("scr.inst.12' '-WhatIf'", ps);
        Assert.Contains("scr.inst.12 \"--what-if\"", sh);
        Assert.Contains("установка рантайма не запускается.", Catalogue());
    }

    // ---------- 4. форма первого старта ----------

    /// <summary>Поле «имя сервера» приходит заполненным. Пустое <c>ui.hostname</c> в конфиге —
    /// всё равно <c>localhost</c>: пустым поле не остаётся ни при каких настройках.</summary>
    [Fact]
    public void First_Start_Suggests_The_Server_Name()
    {
        Assert.Equal("localhost", AuthPages.DefaultServerName(new Ai2pConfig()));

        var blank = new Ai2pConfig();
        blank.Ui.Hostname = "   ";
        Assert.Equal("localhost", AuthPages.DefaultServerName(blank));

        // своё имя из config.json сильнее умолчания — оно и предлагается
        var named = new Ai2pConfig();
        named.Ui.Hostname = " windows-pc ";
        Assert.Equal("windows-pc", AuthPages.DefaultServerName(named));
    }

    /// <summary>Предложенное имя должно ПРОХОДИТЬ проверку формы: подставить в поле значение,
    /// на которое сервер потом ответит ошибкой, — хуже, чем оставить поле пустым.</summary>
    [Fact]
    public void The_Suggested_Name_Passes_The_Form_Check()
    {
        var suggested = AuthPages.DefaultServerName(new Ai2pConfig());

        Assert.True(AuthPages.TryReadServer(suggested, "http", "5480",
            out var host, out var protocol, out var port, out var error));
        Assert.Equal(("localhost", "http", 5480, ""), (host, protocol, port, error));

        // обязательность поля НЕ снята: стереть предложенное и отправить пусто по-прежнему нельзя
        Assert.False(AuthPages.TryReadServer("", "http", "5480", out _, out _, out _, out var empty));
        Assert.Equal("login.error.serverName", empty);
    }

    /// <summary>Подсказка под полем больше не обещает пустое поле — иначе она врёт человеку.</summary>
    [Fact]
    public void The_Hint_Under_The_Field_Matches_What_Is_Shown()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var hint = Loc.In(lang, "login.server.hint");

            Assert.DoesNotContain("поле пустое", hint);
            Assert.DoesNotContain("the field is empty", hint);
            Assert.Contains("localhost", hint);
        }
    }
}
