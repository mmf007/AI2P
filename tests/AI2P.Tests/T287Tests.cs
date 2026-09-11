using AI2P.Core;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-287: УСТАНОВКА ИЗ ПАКЕТА В КАТАЛОГ ПРОГРАММ.
///
/// Пакет установки (T-285) умеет ставить программу «для всех пользователей» — это
/// <c>C:\Program Files\AI2P</c>. Поставленная так версия 1.99 не запускалась вовсе:
///
/// <code>
/// C:\Program Files\AI2P>AI2P.Server.exe
/// Unhandled exception. System.UnauthorizedAccessException: Access to the path
/// 'C:\Program Files\AI2P\config.json' is denied.
///    at AI2P.Server.ConfigMerge.Write(String path, String json)
///    at AI2P.Server.ConfigMerge.ApplyPending(String configPath)
/// </code>
///
/// Причина не в слиянии конфигурации: рядом с программой лежат ВСЕ рабочие файлы —
/// <c>config.json</c>, <c>data/</c>, <c>logs/</c>, <c>secrets/</c>, — а в каталоге программ
/// писать нельзя ничем, кроме прав администратора. Значит запуск от обычного пользователя
/// упирался бы в это в любом случае, а не только в первой строке.
///
/// Исправление — <see cref="AppHome"/>: рабочие файлы лежат рядом с программой, только если
/// туда можно писать; иначе они уходят в каталог данных компьютера (<c>C:\ProgramData\AI2P</c>).
/// Отдать <c>{app}</c> на запись всем было бы хуже: любой пользователь смог бы подменить
/// <c>AI2P.Server.exe</c>, который потом запускает администратор или служба.
///
/// Здесь держатся три вещи: правило выбора каталога, слияние конфигурации из каталога
/// ТОЛЬКО ДЛЯ ЧТЕНИЯ и то, что старт этим правилом пользуется.
/// </summary>
public sealed class T287Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t287-" + Guid.NewGuid().ToString("N"));

    private string Dir(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Каталог <c>AI2P_app</c> — по <c>AI2P.sln</c> рядом с ним.</summary>
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

    // ---------- 1. правило выбора рабочего каталога ----------

    /// <summary>Обычный случай (распакованная выкладка, установка в свою папку, запуск из
    /// исходников) не меняется: рабочие файлы рядом с программой.</summary>
    [Fact]
    public void An_Ordinary_Installation_Keeps_Its_Files_Next_To_The_Program()
    {
        var choice = AppHome.Choose(@"D:\AI2P", envHome: null,
            usable: _ => true, isProgramDir: _ => false,
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.NotNull(choice);
        Assert.Equal(AppHomeKind.AppDir, choice!.Kind);
        Assert.Equal(@"D:\AI2P", choice.Dir);
    }

    /// <summary>Каталог программ — рабочие файлы уходят в каталог данных компьютера.
    /// Это ровно случай жалобы: <c>C:\Program Files\AI2P</c>.</summary>
    [Fact]
    public void An_Installation_Into_The_Programs_Directory_Moves_Its_Files_Out()
    {
        var choice = AppHome.Choose(@"C:\Program Files\AI2P", envHome: null,
            usable: _ => true, isProgramDir: dir => dir.Contains("Program Files"),
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.NotNull(choice);
        Assert.Equal(AppHomeKind.Machine, choice!.Kind);
        Assert.Equal(@"C:\ProgramData\AI2P", choice.Dir);
    }

    /// <summary>
    /// Про каталог программ спрашивается ОТДЕЛЬНО, а не через «можно ли писать»: под
    /// администратором в <c>Program Files</c> писать можно, и проверка одними правами дала бы
    /// у одной установки РАЗНЫЕ каталоги данных — свои у службы (она работает от системы),
    /// свои у человека. Здесь «писать можно везде», а ответ всё равно каталог данных.
    /// </summary>
    [Fact]
    public void Rights_Do_Not_Decide_It_The_Path_Does()
    {
        var elevated = AppHome.Choose(@"C:\Program Files\AI2P", envHome: null,
            usable: _ => true, isProgramDir: dir => dir.Contains("Program Files"),
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");
        var user = AppHome.Choose(@"C:\Program Files\AI2P", envHome: null,
            usable: dir => !dir.Contains("Program Files"),
            isProgramDir: dir => dir.Contains("Program Files"),
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.Equal(user!.Dir, elevated!.Dir);
    }

    /// <summary>Каталог программы просто закрыт на запись (сетевой диск, чужие права) —
    /// правило то же, даже если это не каталог программ системы.</summary>
    [Fact]
    public void A_Read_Only_Program_Directory_Moves_The_Files_Out_Too()
    {
        var choice = AppHome.Choose(@"E:\readonly\AI2P", envHome: null,
            usable: dir => !dir.StartsWith(@"E:\", StringComparison.Ordinal),
            isProgramDir: _ => false,
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.Equal(AppHomeKind.Machine, choice!.Kind);
    }

    /// <summary>Общий каталог данных закрыт (обычный пользователь, жёсткие права) —
    /// остаётся каталог данных пользователя.</summary>
    [Fact]
    public void The_User_Directory_Is_The_Last_Resort()
    {
        var choice = AppHome.Choose(@"C:\Program Files\AI2P", envHome: null,
            usable: dir => dir.Contains("AppData"),
            isProgramDir: dir => dir.Contains("Program Files"),
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.Equal(AppHomeKind.User, choice!.Kind);
        Assert.Equal(@"C:\Users\mike\AppData\Local\AI2P", choice.Dir);
    }

    /// <summary>Писать негде вовсе — ответа нет, и старт обязан сказать об этом словами,
    /// а не упасть где-то в середине.</summary>
    [Fact]
    public void Nowhere_To_Write_Is_An_Honest_Answer()
    {
        var choice = AppHome.Choose(@"C:\Program Files\AI2P", envHome: null,
            usable: _ => false, isProgramDir: _ => true,
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.Null(choice);
    }

    /// <summary>Переменная окружения сильнее любых правил: ею пользуются службы, контейнеры
    /// и проверки.</summary>
    [Fact]
    public void The_Environment_Variable_Wins()
    {
        var choice = AppHome.Choose(@"C:\Program Files\AI2P", envHome: _dir,
            usable: _ => true, isProgramDir: _ => true,
            machineHome: @"C:\ProgramData\AI2P", userHome: @"C:\Users\mike\AppData\Local\AI2P");

        Assert.Equal(AppHomeKind.Explicit, choice!.Kind);
        Assert.Equal(Path.GetFullPath(_dir), choice.Dir);
        Assert.Equal("AI2P_HOME", AppHome.EnvVar);
    }

    // ---------- 2. что считается каталогом программ ----------

    /// <summary>Сравниваются ПОЛНЫЕ сегменты пути: «C:\Program Files 2» каталогом программ
    /// не является, хотя и начинается так же.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\AI2P", true)]
    [InlineData(@"C:\Program Files", true)]
    [InlineData(@"C:\Program Files (x86)\AI2P", true)]
    [InlineData(@"C:\Program Files 2\AI2P", false)]
    [InlineData(@"D:\AI2P", false)]
    [InlineData(@"C:\Users\mike\AppData\Local\Programs\AI2P", false)]
    public void The_Windows_Programs_Directory_Is_Recognised(string dir, bool expected)
    {
        var roots = new[] { @"C:\Program Files", @"C:\Program Files (x86)" };
        Assert.Equal(expected, AppHome.IsProgramDir(dir, windows: true, roots));
    }

    /// <summary>Регистр имён на Windows значения не имеет.</summary>
    [Fact]
    public void The_Windows_Comparison_Ignores_Case()
    {
        Assert.True(AppHome.IsProgramDir(@"c:\program files\ai2p", windows: true,
            new[] { @"C:\Program Files" }));
    }

    /// <summary>На Linux/macOS каталоги программ — <c>/usr</c>, <c>/opt</c>, <c>/Applications</c>;
    /// установка в домашний каталог (<c>~/ai/AI2P</c>, ТЗ гл. 4.3) каталогом программ не является
    /// и ведёт себя как раньше.</summary>
    [Theory]
    [InlineData("/opt/ai2p", true)]
    [InlineData("/usr/local/ai2p", true)]
    [InlineData("/home/mike/ai/AI2P", false)]
    public void The_Unix_Programs_Directories_Are_Recognised(string dir, bool expected)
    {
        var roots = new[] { "/usr", "/opt", "/Applications", "/Library" };
        Assert.Equal(expected, AppHome.IsProgramDir(dir, windows: false, roots));
    }

    // ---------- 3. подготовка каталога и первая конфигурация ----------

    /// <summary>
    /// Первый <c>config.json</c> берётся из <c>config.new.json</c> дистрибутива — и сразу
    /// отмечается применённым: сливать целиком взятую конфигурацию не с чем, а без отметки
    /// первый же старт сообщал бы человеку об обновлении, которого не было.
    /// </summary>
    [Fact]
    public void The_First_Configuration_Comes_From_The_Distribution()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        File.WriteAllText(Path.Combine(dist, "config.new.json"), "{ \"ui\": { \"port\": 5480 } }");

        var info = AppHome.Prepare(new AppHomeChoice(home, AppHomeKind.Machine), dist);

        Assert.True(info.Relocated);
        Assert.Equal(Path.Combine(home, "config.json"), info.ConfigPath);
        Assert.True(File.Exists(info.ConfigPath), "конфигурация не положена");
        Assert.Contains("5480", File.ReadAllText(info.ConfigPath));
        Assert.False(ConfigMerge.ApplyPending(info.ConfigPath, dist),
            "первый же старт объявил бы об обновлении конфигурации");
        Assert.True(File.Exists(Path.Combine(dist, "config.new.json")),
            "файл дистрибутива трогать нельзя — каталог только для чтения");
    }

    /// <summary>
    /// Настройки ПРЕЖНЕЙ установки, оставшиеся рядом с программой (её ставили и запускали
    /// администратором — тогда писать в каталог было можно), переезжают вместе с ней: порт,
    /// имя хоста и каталоги человек задавал сам, и терять их нельзя. Новые параметры версии
    /// дописывает обычное слияние.
    /// </summary>
    [Fact]
    public void The_Settings_Of_The_Previous_Installation_Move_With_It()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        File.WriteAllText(Path.Combine(dist, "config.json"),
            "{ \"ui\": { \"port\": 5599, \"hostname\": \"stanok\" } }");
        File.WriteAllText(Path.Combine(dist, "config.new.json"),
            "{ \"ui\": { \"port\": 5480 }, \"mail\": { \"port\": 587 } }");

        var info = AppHome.Prepare(new AppHomeChoice(home, AppHomeKind.Machine), dist);
        Assert.Equal(Path.Combine(dist, "config.json"), info.SeededFrom);
        Assert.True(ConfigMerge.ApplyPending(info.ConfigPath, dist));

        var merged = File.ReadAllText(info.ConfigPath);
        Assert.Contains("5599", merged);        // настройка человека уцелела
        Assert.Contains("stanok", merged);
        Assert.Contains("587", merged);         // новый параметр версии приехал
    }

    /// <summary>Первая установка из пакета кладёт ОБА файла, и они одинаковы: сливать нечего,
    /// и первый старт не должен объявлять об обновлении конфигурации.</summary>
    [Fact]
    public void A_Fresh_Package_Installation_Reports_No_Update()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        var text = "{ \"ui\": { \"port\": 5480 } }";
        File.WriteAllText(Path.Combine(dist, "config.json"), text);
        File.WriteAllText(Path.Combine(dist, "config.new.json"), text);

        var info = AppHome.Prepare(new AppHomeChoice(home, AppHomeKind.Machine), dist);

        Assert.False(ConfigMerge.ApplyPending(info.ConfigPath, dist));
    }

    /// <summary>Данные прежней работы рядом с программой (её ставили и запускали
    /// администратором) сами не переносятся, но человеку про них говорится.</summary>
    [Fact]
    public void The_Data_Left_Next_To_The_Program_Is_Reported()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        Directory.CreateDirectory(Path.Combine(dist, "data"));

        var info = AppHome.Prepare(new AppHomeChoice(home, AppHomeKind.Machine), dist);

        Assert.Equal(Path.Combine(dist, "data"), info.LegacyDataDir);
        Assert.True(Directory.Exists(Path.Combine(dist, "data")), "данные трогать нельзя");
    }

    /// <summary>Рабочие файлы рядом с программой — прежнее поведение: ничего никуда
    /// не переносится и «переехавшим» такой запуск не считается.</summary>
    [Fact]
    public void Working_Next_To_The_Program_Is_Not_A_Relocation()
    {
        var dir = Dir("app");
        var info = AppHome.Prepare(new AppHomeChoice(dir, AppHomeKind.AppDir), dir);

        Assert.False(info.Relocated);
        Assert.Equal("", info.SeededFrom);
        Assert.Equal("", info.LegacyDataDir);
    }

    // ---------- 4. слияние конфигурации из каталога только для чтения ----------

    /// <summary>
    /// Обновление версии при разведённых каталогах: значения пользователя остаются, новые
    /// параметры приходят с умолчаниями дистрибутива — то же правило, что и раньше
    /// (<see cref="Todo46Tests"/>), только файл дистрибутива не переименовывается.
    /// </summary>
    [Fact]
    public void An_Update_Merges_From_A_Read_Only_Distribution()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        File.WriteAllText(Path.Combine(home, "config.json"),
            "{ \"ui\": { \"port\": 5599 }, \"language\": \"ru\" }");
        File.WriteAllText(Path.Combine(dist, "config.new.json"),
            "{ \"ui\": { \"port\": 5480 }, \"language\": \"ru\", \"mail\": { \"port\": 587 } }");

        Assert.True(ConfigMerge.ApplyPending(Path.Combine(home, "config.json"), dist));

        var merged = File.ReadAllText(Path.Combine(home, "config.json"));
        Assert.Contains("5599", merged);                  // порт человека уцелел
        Assert.Contains("587", merged);                   // новый параметр версии приехал
        Assert.True(File.Exists(Path.Combine(dist, "config.new.json")),
            "каталог дистрибутива только для чтения — переименовывать в нём нечего");
        Assert.True(File.Exists(ConfigMerge.AppliedMarker(home)), "нет отметки о слиянии");
    }

    /// <summary>Слияние идёт РОВНО ОДИН РАЗ на версию дистрибутива: второй старт ничего
    /// не делает, а следующая версия сливается снова.</summary>
    [Fact]
    public void The_Merge_Happens_Once_Per_Distribution_Version()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        var config = Path.Combine(home, "config.json");
        File.WriteAllText(config, "{ \"ui\": { \"port\": 5599 } }");
        File.WriteAllText(Path.Combine(dist, "config.new.json"), "{ \"ui\": { \"port\": 5480 } }");

        Assert.True(ConfigMerge.ApplyPending(config, dist));
        Assert.False(ConfigMerge.ApplyPending(config, dist), "слияние повторилось");

        // приехала новая версия дистрибутива — сливаем снова
        File.WriteAllText(Path.Combine(dist, "config.new.json"),
            "{ \"ui\": { \"port\": 5480 }, \"lora\": { \"imageMaxKb\": 2048 } }");
        Assert.True(ConfigMerge.ApplyPending(config, dist));
        var merged = File.ReadAllText(config);
        Assert.Contains("5599", merged);
        Assert.Contains("imageMaxKb", merged);
    }

    /// <summary>Рабочей конфигурации ещё нет: файл дистрибутива КОПИРУЕТСЯ (а не
    /// переименовывается — прав на это нет) и отмечается применённым.</summary>
    [Fact]
    public void The_First_Start_Copies_The_Distribution_Configuration()
    {
        var dist = Dir("dist");
        var home = Dir("home");
        File.WriteAllText(Path.Combine(dist, "config.new.json"), "{ \"ui\": { \"port\": 5480 } }");

        Assert.True(ConfigMerge.ApplyPending(Path.Combine(home, "config.json"), dist));

        Assert.True(File.Exists(Path.Combine(home, "config.json")));
        Assert.True(File.Exists(Path.Combine(dist, "config.new.json")));
        Assert.False(ConfigMerge.ApplyPending(Path.Combine(home, "config.json"), dist));
    }

    /// <summary>Прежнее поведение (один каталог на всё) не изменилось: файл новой версии
    /// переименовывается в <c>*.applied</c>, как и раньше.</summary>
    [Fact]
    public void The_Old_Single_Directory_Behaviour_Is_Untouched()
    {
        var dir = Dir("app");
        var config = Path.Combine(dir, "config.json");
        File.WriteAllText(config, "{ \"ui\": { \"port\": 5599 } }");
        File.WriteAllText(Path.Combine(dir, "config.new.json"), "{ \"ui\": { \"port\": 5480 } }");

        Assert.True(ConfigMerge.ApplyPending(config));

        Assert.False(File.Exists(Path.Combine(dir, "config.new.json")));
        Assert.True(File.Exists(Path.Combine(dir, "config.new.json.applied")));
        Assert.Contains("5599", File.ReadAllText(config));
    }

    // ---------- 5. настоящий каталог и настоящие права ----------

    /// <summary>Проверка «можно ли писать» — настоящей записью, и она же заводит каталог:
    /// вычислить ответ по атрибутам на Windows нельзя (ACL, наследование, маркер целостности).</summary>
    [Fact]
    public void Writability_Is_Checked_By_Actually_Writing()
    {
        var dir = Path.Combine(_dir, "new", "deep");
        Assert.True(AppHome.CanUse(dir));
        Assert.True(Directory.Exists(dir));
        Assert.Empty(Directory.GetFileSystemEntries(dir));   // проба за собой убирает

        // каталогом стать нельзя: по этому пути лежит файл — на обеих системах это отказ,
        // и старт обязан его пережить, а не упасть
        var busy = Path.Combine(dir, "занято");
        File.WriteAllText(busy, "файл, а не каталог");
        Assert.False(AppHome.CanUse(busy));
    }

    /// <summary>Каталоги данных этой системы — не выдуманные пути: на Windows это
    /// <c>ProgramData</c> и <c>LOCALAPPDATA</c>, и они разные.</summary>
    [Fact]
    public void The_Data_Directories_Belong_To_This_System()
    {
        Assert.NotEqual(AppHome.MachineHome(), AppHome.UserHome());
        Assert.EndsWith(OperatingSystem.IsWindows() ? "AI2P" : "ai2p", AppHome.MachineHome());
        Assert.NotEmpty(AppHome.ProgramRoots());
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("ProgramData", AppHome.MachineHome());
        }
    }

    /// <summary>Ключ <c>--config</c> сильнее правила и дистрибутивом считает СВОЙ каталог:
    /// иначе стенды проверок и вторые экземпляры на одном компьютере начали бы вычитывать
    /// <c>config.new.json</c> из каталога программы.</summary>
    [Fact]
    public void An_Explicit_Config_Path_Is_Taken_As_Is()
    {
        var dir = Dir("stand");
        var path = Path.Combine(dir, "config.json");
        var info = AppHome.Resolve(new[] { "--config", path });

        Assert.Equal(AppHomeKind.Explicit, info.Kind);
        Assert.Equal(path, info.ConfigPath);
        Assert.Equal(dir, info.DistDir);
        Assert.False(info.Relocated);
        Assert.Equal(path, Ai2pConfig.ResolvePath(new[] { "--config", path }));
    }

    /// <summary>
    /// Стык с T-271: службе рабочий каталог прибивают ключом <c>--config</c>, а программа
    /// при этом стоит в каталоге программ. Файл новой версии лежит У ПРОГРАММЫ, и без этой
    /// ветки обновление не донесло бы службе ни одного нового параметра. Всем остальным
    /// (стенды проверок, второй экземпляр) дистрибутивом остаётся свой каталог.
    /// </summary>
    [Fact]
    public void A_Service_Pinned_By_Config_Still_Gets_The_New_Version_Defaults()
    {
        var app = Dir("app");
        var home = Dir("home");
        File.WriteAllText(Path.Combine(app, ConfigMerge.NewConfigName), "{ }");

        // каталог программы закрыт (или это каталог программ) — дистрибутив у программы
        Assert.Equal(app, AppHome.DistDirFor(home, app, _ => true));
        // обычный каталог программы (стенд проверки, второй экземпляр) — дистрибутив свой
        Assert.Equal(home, AppHome.DistDirFor(home, app, _ => false));
        // свой config.new.json рядом с конфигурацией сильнее в любом случае
        File.WriteAllText(Path.Combine(home, ConfigMerge.NewConfigName), "{ }");
        Assert.Equal(home, AppHome.DistDirFor(home, app, _ => true));
        // и настоящий вызов на обычном каталоге ничего не меняет
        Assert.Equal(app, AppHome.DistDirFor(app, app));
    }

    // ---------- 6. этим пользуется старт, и об этом есть что сказать ----------

    /// <summary>Старт берёт каталог у <see cref="AppHome"/> и сливает конфигурацию ОТТУДА,
    /// где лежит дистрибутив. Сторож по тексту: правка, вернувшая старый вызов, тихо вернула
    /// бы и падение в <c>Program Files</c>.</summary>
    [Fact]
    public void The_Startup_Uses_The_Rule()
    {
        var program = File.ReadAllText(Path.Combine(AppDir(), "src", "AI2P.Server", "Program.cs"));
        Assert.Contains("AppHome.Resolve(args)", program);
        Assert.Contains("ConfigMerge.ApplyPending(configPath, home.DistDir)", program);
        Assert.Contains("StartupFailed(ex, asService)", program);
    }

    /// <summary>
    /// Установщик заводит общий каталог данных и открывает его на запись пользователям
    /// компьютера — иначе первый же второй пользователь (или служба, работающая от системы)
    /// не прочитал бы данные, заведённые первым. Делается это только у установки «для всех»:
    /// у установки «только для меня» программа лежит в каталоге пользователя, и общий
    /// каталог не нужен вовсе. Сценарий установщика — не C#, поэтому держится по тексту
    /// (как <see cref="T285Tests"/>).
    /// </summary>
    [Fact]
    public void The_Installer_Prepares_The_Shared_Data_Directory()
    {
        var script = File.ReadAllText(Path.Combine(AppDir(), "MakePackage.ps1"));
        Assert.Contains("[Dirs]", script);
        Assert.Contains("{commonappdata}\\AI2P", script);
        Assert.Contains("Permissions: users-modify", script);
        Assert.Contains("Check: IsAdminInstallMode", script);
        // данные удаление не трогает: каталог не вписан в [UninstallDelete]
        var uninstall = script[script.IndexOf("[UninstallDelete]", StringComparison.Ordinal)..];
        Assert.DoesNotContain("{commonappdata}", uninstall);
    }

    /// <summary>
    /// У установки «для всех» рабочего <c>config.json</c> рядом с программой не бывает вовсе:
    /// он ушёл в каталог данных. Класть его в <c>{app}</c> в этом случае нельзя — файл был бы
    /// МЁРТВЫМ: приложение его не читает, правки в нём ничего не меняют, а скрипты службы
    /// и установки (T-271) каталог программ при поиске рабочего конфига пропускают.
    /// У установки «только для меня» всё как раньше: там программа и данные в одном каталоге.
    /// </summary>
    [Fact]
    public void The_Installer_Puts_No_Dead_Configuration_Into_The_Programs_Directory()
    {
        var script = File.ReadAllText(Path.Combine(AppDir(), "MakePackage.ps1"));
        Assert.Contains("DestName: `\"config.json`\"; Flags: onlyifdoesntexist; Check: not IsAdminInstallMode",
            script);
        // config.new.json кладётся ВСЕГДА: из него каталог данных получает первую конфигурацию
        Assert.Contains("DestName: `\"config.new.json`\"; Flags: ignoreversion", script);
    }

    /// <summary>Сообщения о каталоге есть на обоих языках (ТЗ гл. 9): ключ вместо текста
    /// означает, что словарь не пополнили.</summary>
    [Theory]
    [InlineData("msg.appHome.1")]
    [InlineData("msg.appHome.2")]
    [InlineData("msg.appHome.3")]
    [InlineData("msg.appHome.4")]
    [InlineData("msg.appHome.5")]
    [InlineData("msg.program.4")]
    [InlineData("msg.program.5")]
    public void The_Messages_Are_Translated(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = Loc.In(lang, key, "1", "2", "3", "4");
            Assert.NotEqual(key, text);
            Assert.NotEmpty(text);
        }
    }
}
