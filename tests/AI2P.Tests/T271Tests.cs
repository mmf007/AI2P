using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-271: ЗАПУСК КАК СЕРВИСА ОС.
///
/// Приложение одно и то же: по умолчанию оно КОНСОЛЬНОЕ, а сервисом становится тогда,
/// когда его так запустили. Службу заводит отдельный скрипт выкладки
/// <c>makeAsServise.*</c> (имя службы — <c>AI2P</c>), и об этом надо ПОМНИТЬ при
/// следующих обновлениях: работающая служба держит свои файлы, и обновление поверх
/// сорвалось бы на копировании.
///
/// Здесь три группы проверок:
///
///   1. правило выбора режима (<see cref="ServiceRun.Detect"/>) — единственное место,
///      где решается «сервис или консоль»;
///   2. настройка <c>serviceMode</c> — записка «эта установка настроена сервисом»,
///      которая переживает обновление версии;
///   3. скрипты (не C#, поэтому держатся по тексту — как <see cref="T285Tests"/>):
///      makeAsServise кладётся в выкладку, install.* и пакет установки останавливают
///      службу и запускают её обратно.
/// </summary>
public sealed class T271Tests
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


    // ---------- 1. правило выбора режима ----------

    /// <summary>Умолчание приложения — КОНСОЛЬ: ни ключей, ни менеджера служб.</summary>
    [Fact]
    public void By_Default_The_Run_Is_A_Console_One()
    {
        Assert.False(ServiceRun.Detect([], () => false, () => false));
        Assert.False(ServiceRun.Detect(["--config", "x.json"], () => false, () => false));
    }

    /// <summary>Систему спрашивают, только когда своих ключей нет: под менеджером служб
    /// (Windows) и под systemd (Linux) запуск сервисный, и распознаётся он сам.</summary>
    [Fact]
    public void The_System_Is_Asked_When_There_Are_No_Keys()
    {
        Assert.True(ServiceRun.Detect([], () => true, () => false));    // менеджер служб Windows
        Assert.True(ServiceRun.Detect([], () => false, () => true));    // systemd
    }

    /// <summary>Ключ <c>--service</c> задаёт режим принудительно: launchd на macOS никакого
    /// признака процессу не даёт, и без ключа режим там определить нечем.</summary>
    [Fact]
    public void The_Service_Key_Forces_The_Service_Mode()
    {
        Assert.True(ServiceRun.Detect(["--service"], () => false, () => false));
        Assert.True(ServiceRun.Detect(["--config", "x.json", "--service"], () => false, () => false));
        // регистр ключа значения не имеет: его пишут руками в юните и в plist
        Assert.True(ServiceRun.Detect(["--SERVICE"], () => false, () => false));
    }

    /// <summary>А <c>--console</c> сильнее всего: им человек разбирает поведение службы
    /// руками, запуская ту же программу из консоли.</summary>
    [Fact]
    public void The_Console_Key_Beats_Everything()
    {
        Assert.False(ServiceRun.Detect(["--console"], () => true, () => true));
        Assert.False(ServiceRun.Detect(["--service", "--console"], () => false, () => false));
        Assert.False(ServiceRun.Detect(["--console", "--service"], () => false, () => false));
    }

    /// <summary>Имя службы одно на все системы: его знают скрипты, install.* и пакет.</summary>
    [Fact]
    public void The_Service_Name_Is_AI2P()
    {
        Assert.Equal("AI2P", ServiceRun.ServiceName);
        Assert.Equal("--service", ServiceRun.ServiceArg);
        Assert.Equal("--console", ServiceRun.ConsoleArg);
    }

    // ---------- 2. записка serviceMode ----------

    /// <summary>Умолчание — «не сервис»: пометку ставит только makeAsServise.</summary>
    [Fact]
    public void The_Service_Mark_Is_Off_By_Default()
    {
        Assert.False(new Ai2pConfig().ServiceMode);
    }

    /// <summary>Ключ есть в config.json дистрибутива со значением false. Он должен быть
    /// именно ТАМ: обновление кладёт значения пользователя поверх новых умолчаний
    /// (ConfigMerge), и поставленная пометка переживает смену версии.</summary>
    [Fact]
    public void The_Distributed_Config_Has_The_Service_Mark()
    {
        var text = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Server", "config.json"));
        Assert.Contains("\"serviceMode\"", text);
        Assert.Contains("\"serviceMode\": false", text);
    }

    /// <summary>Пометка читается из файла, а не подставляется кодом.</summary>
    [Fact]
    public void The_Service_Mark_Is_Read_From_The_File()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai2p-t271-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ \"language\": \"ru\", \"serviceMode\": true }");
            Assert.True(Ai2pConfig.Load(path).ServiceMode);
            File.WriteAllText(path, "{ \"language\": \"ru\" }");
            Assert.False(Ai2pConfig.Load(path).ServiceMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Режим запуска и записка уезжают в UI: человеку, открывшему интерфейс,
    /// иначе неоткуда узнать, чем поднят сервер.</summary>
    [Fact]
    public void The_Run_Mode_Reaches_The_Ui()
    {
        var dto = new AI2P.Core.Api.StateDto();
        Assert.False(dto.IsService);
        Assert.False(dto.ServiceMode);

        var api = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Server", "Api", "ApiEndpoints.cs"));
        // поле заполняется В ОБЕИХ ветках /api/state: без организации (сервер подал заявку
        // и ждёт решения) интерфейс тоже открывается, и настройки в нём доступны
        Assert.Equal(2, Count(api, "IsService = ServiceRun.IsService,"));
        Assert.Equal(2, Count(api, "ServiceMode = configHolder.Config.ServiceMode,"));

        var view = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.UI", "Components", "SettingsView.razor"));
        Assert.Contains("settings.runMode", view);
        Assert.Contains("data-settings-runmode", view);
    }

    /// <summary>Тексты сообщений — в словарях обоих языков (ТЗ гл. 9): у сервиса нет консоли,
    /// и «порт занят» он говорит в журнал, а не человеку в окно.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(Path.Combine(AppDir(), "i18n", lang + ".json"));
            Assert.Contains("\"msg.program.3\"", text);
            Assert.Contains("\"settings.runMode\"", text);
            Assert.Contains("\"settings.runMode.service\"", text);
            Assert.Contains("\"settings.runMode.console\"", text);
            Assert.Contains("\"settings.runMode.markOnly\"", text);
        }
    }

    // ---------- 3. скрипты ----------

    /// <summary>Скрипт настройки службы есть под обе платформы, и .cmd-обёртка зовёт .ps1
    /// (у .ps1 в Windows ассоциация «редактировать», по Enter он не выполняется).</summary>
    [Fact]
    public void The_Service_Script_Exists_For_Both_Platforms()
    {
        var app = AppDir();
        foreach (var name in new[] { "makeAsServise.ps1", "makeAsServise.cmd", "makeAsServise.sh" })
        {
            Assert.True(File.Exists(Path.Combine(app, name)), $"нет скрипта {name}");
        }
        Assert.Contains("makeAsServise.ps1", Script("makeAsServise.cmd"));
        // имя службы во всех трёх — одно и то же
        Assert.Contains("$serviceName = \"AI2P\"", Script("makeAsServise.ps1"));
        Assert.Contains("SERVICE_NAME=\"AI2P\"", Script("makeAsServise.sh"));
    }

    /// <summary>Скрипт кладётся В ВЫКЛАДКУ по тому же правилу платформы, что install.* —
    /// оттуда установка переносит его в саму установку, где его и зовут руками.</summary>
    [Fact]
    public void BuildRelease_Puts_The_Service_Script_Into_The_Release()
    {
        var ps = Script("buildRelease.ps1");
        var sh = Script("buildRelease.sh");

        Assert.Contains("@(\"makeAsServise.ps1\", \"makeAsServise.cmd\")", ps);
        Assert.Contains("@(\"makeAsServise.sh\")", ps);
        Assert.Contains("SERVICE_SCRIPTS=\"makeAsServise.sh\"", sh);
        Assert.Contains("SERVICE_SCRIPTS=\"makeAsServise.ps1 makeAsServise.cmd\"", sh);

        // чужой скрипт от прежней выкладки в каталоге остаться не должен (правило T-211)
        Assert.Contains("makeAsServise.ps1\", \"makeAsServise.cmd\", \"makeAsServise.sh\"", ps);
        Assert.Contains("makeAsServise.ps1 makeAsServise.cmd makeAsServise.sh", sh);

        // @(...) вокруг КАЖДОГО списка: у выкладки под Linux/macOS в каждом из них
        // ровно один элемент, PowerShell разворачивает такой массив в строку, и «+»
        // складывает СТРОКИ. Тогда копировать нечего, а уборка чужих скриптов сносит
        // из выкладки свои — до T-271 выкладка под Linux, собранная на Windows,
        // оставалась вообще без install.sh
        Assert.Contains("@($installScripts) + @($packageScripts) + @($serviceScripts)", ps);
    }

    /// <summary>СЛЕДУЮЩЕЕ ОБНОВЛЕНИЕ. Установка обязана сама остановить службу перед
    /// копированием и запустить её обратно: иначе обновление сервера-службы сорвётся
    /// на занятых файлах, а человеку придётся гасить её руками.
    ///
    /// Спрашивается СИСТЕМА (Get-Service / systemctl), а не пометка в конфиге: настоящее
    /// состояние знает только она. Пометка — повод сказать «службы нет, а помечено».</summary>
    [Fact]
    public void The_Installer_Stops_And_Starts_The_Service()
    {
        var ps = Script("install.ps1");
        Assert.Contains("Get-CimInstance -ClassName Win32_Service", ps);
        Assert.Contains("Stop-Service -Name $serviceName", ps);
        Assert.Contains("Start-Service -Name $serviceName", ps);
        Assert.Contains("$serviceWasRunning", ps);
        Assert.Contains("serviceMode", ps);
        Assert.Contains("makeAsServise.cmd", ps);

        var sh = Script("install.sh");
        Assert.Contains("find_service", sh);
        Assert.Contains("SERVICE_WAS_RUNNING", sh);
        Assert.Contains("systemctl --user", sh);
        Assert.Contains("launchctl", sh);
        Assert.Contains("makeAsServise.sh", sh);
    }

    /// <summary>Чужую службу (ведущую в другую установку) не трогает никто — ни установка,
    /// ни сам makeAsServise: служба одна на компьютер, и снести соседнюю нельзя молча.</summary>
    [Fact]
    public void A_Foreign_Service_Is_Never_Touched()
    {
        Assert.Contains("StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)", Script("install.ps1"));
        Assert.Contains("grep -qF \"$dir/AI2P.Server\"", Script("install.sh"));
        // текст с T-65-S0 в каталоге, в скрипте — ключ
        Assert.Contains("scr.svc.12", Script("makeAsServise.ps1"));
        Assert.Contains("ДРУГУЮ установку", Catalogue());
        Assert.Contains("Ai2pServiceIsOurs", Script("MakePackage.ps1"));
    }

    /// <summary>Пакет установки (Inno Setup) — тот же путь обновления, и он обязан вести
    /// себя так же: остановить службу до копирования, запустить после, снять при удалении.
    /// Разделов [Code] в сценарии должен быть РОВНО ОДИН: Inno их не складывает, и вторая
    /// функция InitializeSetup молча заменила бы первую.</summary>
    [Fact]
    public void The_Package_Handles_The_Service_Too()
    {
        var ps = Script("MakePackage.ps1");
        Assert.Contains("PrepareToInstall", ps);
        Assert.Contains("net stop AI2P", ps);
        Assert.Contains("net start AI2P", ps);
        Assert.Contains("CurUninstallStepChanged", ps);
        Assert.Contains("delete AI2P", ps);
        // остановить службу без прав администратора нечем — об этом надо сказать, а не
        // валиться потом на копировании файлов
        Assert.Contains("IsAdminInstallMode()", ps);
        // считаем ОТДЕЛЬНУЮ строку «[Code]», а не вхождения в тексте: в пояснениях выше
        // это же слово упоминается, и сторож краснел бы на собственном объяснении (T-244)
        Assert.Equal(1, ps.Split('\n').Count(line => line.Trim() == "[Code]"));
        Assert.Equal(1, Count(ps, "function InitializeSetup()"));
    }

    /// <summary>Жизненный цикл службы подключён в самом приложении: без него менеджер
    /// служб Windows через полминуты считает службу зависшей, а systemd — не знает,
    /// когда её останавливать.</summary>
    [Fact]
    public void The_Application_Knows_How_To_Live_As_A_Service()
    {
        var csproj = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Server", "AI2P.Server.csproj"));
        Assert.Contains("Microsoft.Extensions.Hosting.WindowsServices", csproj);
        Assert.Contains("Microsoft.Extensions.Hosting.Systemd", csproj);

        var program = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Server", "Program.cs"));
        Assert.Contains("builder.Host.UseWindowsService", program);
        Assert.Contains("builder.Host.UseSystemd()", program);
        // браузер открывается только консольному запуску: рабочего стола у службы нет
        Assert.Contains("config.Ui.OpenBrowserOnStart && !asService", program);
        // свои ключи не уезжают в разбор конфигурации хоста: «--service --config …»
        // иначе падает FormatException ещё до старта
        Assert.Contains("Args = hostArgs,", program);
    }

    /// <summary>РАБОЧИЙ КАТАЛОГ НЕ ОБЯЗАН БЫТЬ РЯДОМ С ПРОГРАММОЙ (стык с T-287).
    ///
    /// Установка в каталог программ системы уводит <c>config.json</c>, <c>data</c> и
    /// <c>secrets</c> в общий каталог данных компьютера, а если и он закрыт — в каталог
    /// данных ПОЛЬЗОВАТЕЛЯ. Отсюда две вещи, которые молча ломаются:
    ///
    ///   1. пометку <c>serviceMode</c> и ключи под DPAPI надо искать в РАБОЧЕМ конфиге,
    ///      а не рядом с <c>.exe</c> — иначе записка ложится в файл, который приложение
    ///      никогда не читает;
    ///   2. службе рабочий каталог надо ПРИБИТЬ ключом <c>--config</c>: она работает от
    ///      другой учётной записи, и запасной путь выбора (каталог данных пользователя)
    ///      у неё был бы свой — служба завела бы пустую базу вместо рабочей.
    ///
    /// Правило выбора каталога живёт в приложении (<c>AppHome</c>) и в скриптах НЕ
    /// повторяется: они лишь ищут уже существующий <c>config.json</c> в тех же местах.</summary>
    [Fact]
    public void The_Working_Dir_Is_Followed_Where_It_Went()
    {
        var ps = Script("makeAsServise.ps1");
        Assert.Contains("$env:AI2P_HOME", ps);
        Assert.Contains("$env:ProgramData", ps);
        Assert.Contains("$env:LOCALAPPDATA", ps);
        Assert.Contains("$configPath = Join-Path $homeDir \"config.json\"", ps);
        Assert.Contains("$secretsPath = Join-Path $homeDir \"secrets.json\"", ps);
        Assert.Contains("if ($relocated) { $binaryPath += ' --config \"' + $configPath + '\"' }", ps);

        var sh = Script("makeAsServise.sh");
        Assert.Contains("\"$AI2P_HOME\" \"$OWN_DIR\" \"/var/lib/ai2p\" \"$HOME/.local/share/ai2p\"", sh);
        Assert.Contains("CONFIG=\"$HOME_DIR/config.json\"", sh);
        Assert.Contains("EXEC_ARGS=\"--service --config $CONFIG\"", sh);
        Assert.Contains("ExecStart=$EXE $EXEC_ARGS", sh);

        // КАТАЛОГ ПРОГРАММ ПРОПУСКАЕТСЯ: пакет установки кладёт config.json и в
        // C:\Program Files\AI2P, а приложение оттуда его не читает — взяв тот файл,
        // скрипт поставил бы пометку в мёртвый конфиг
        Assert.Contains("function Test-ProgramDir", ps);
        Assert.Contains("$env:ProgramW6432", ps);
        Assert.Contains("if (-not (Test-ProgramDir $targetDir)) { $homeCandidates += $targetDir }", ps);
        Assert.Contains("/usr/*|/opt/*|/Applications/*", sh);

        // установка ищет пометку там же, а не только рядом с программой
        Assert.Contains("$env:ProgramData", Script("install.ps1"));
        Assert.Contains("$env:ProgramW6432", Script("install.ps1"));
        Assert.Contains("service_flag_set", Script("install.sh"));
        Assert.Contains("/usr/*|/opt/*|/Applications/*", Script("install.sh"));
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
