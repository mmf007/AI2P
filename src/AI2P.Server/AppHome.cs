using AI2P.Core;

namespace AI2P.Server;

/// <summary>Откуда взялся рабочий каталог (нужно и сообщению человеку, и проверкам).</summary>
public enum AppHomeKind
{
    /// <summary>Задан снаружи: ключ <c>--config</c> или переменная окружения
    /// <see cref="AppHome.EnvVar"/>. Приложение в такой путь не вмешивается.</summary>
    Explicit,

    /// <summary>Каталог самой программы — обычный случай (распакованная выкладка,
    /// установка в свою папку, запуск из исходников). Поведение прежнее.</summary>
    AppDir,

    /// <summary>Общий каталог данных ЭТОГО КОМПЬЮТЕРА: Windows — <c>C:\ProgramData\AI2P</c>,
    /// Linux/macOS — <c>/var/lib/ai2p</c>. Сюда уходит установка в каталог программ.</summary>
    Machine,

    /// <summary>Каталог данных ПОЛЬЗОВАТЕЛЯ: Windows — <c>%LOCALAPPDATA%\AI2P</c>,
    /// Linux/macOS — <c>~/.local/share/ai2p</c>. Запасной путь: общий каталог оказался
    /// закрыт на запись (обычный пользователь, жёсткие права).</summary>
    User,
}

/// <summary>Выбранный каталог и то, чем он оказался (результат <see cref="AppHome.Choose"/>).</summary>
/// <param name="Dir">Каталог рабочих файлов.</param>
/// <param name="Kind">Откуда он взялся.</param>
public sealed record AppHomeChoice(string Dir, AppHomeKind Kind);

/// <summary>
/// Всё, что нужно знать старту про рабочие файлы установки.
/// </summary>
/// <param name="ConfigPath">Рабочий <c>config.json</c>.</param>
/// <param name="Dir">Каталог рабочих файлов: рядом с ним лягут <c>data/</c>, <c>logs/</c>,
/// <c>secrets/</c> — они считаются от <c>config.json</c> (ТЗ гл. 10).</param>
/// <param name="DistDir">Каталог ДИСТРИБУТИВА: <c>config.new.json</c>, словари, документация.
/// Совпадает с <paramref name="Dir"/> в обычном случае.</param>
/// <param name="Kind">Чем оказался рабочий каталог.</param>
/// <param name="SeededFrom">Файл дистрибутива, из которого взят первый <c>config.json</c>
/// (пусто — конфигурация уже была).</param>
/// <param name="LegacyDataDir">Каталог <c>data/</c>, оставшийся РЯДОМ С ПРОГРАММОЙ от прежней
/// работы (пусто — такого нет). Его надо показать человеку: сам он туда больше не смотрит.</param>
public sealed record AppHomeInfo(
    string ConfigPath,
    string Dir,
    string DistDir,
    AppHomeKind Kind,
    string SeededFrom,
    string LegacyDataDir)
{
    /// <summary>Рабочие файлы лежат НЕ рядом с программой.</summary>
    public bool Relocated => !AppHome.SamePath(Dir, DistDir);
}

/// <summary>
/// РАБОЧИЙ КАТАЛОГ УСТАНОВКИ (T-287): где лежат <c>config.json</c>, <c>data/</c>, <c>logs/</c>
/// и <c>secrets/</c>.
///
/// До T-287 ответ был один: рядом с программой. Так и работает распакованная выкладка,
/// установка в свою папку (<c>D:\AI2P</c>) и запуск из исходников — и так это остаётся.
/// Но пакет установки (T-285) умеет ставить программу «для всех пользователей», а это
/// <c>C:\Program Files\AI2P</c>, куда обычный пользователь писать не может. Программа при
/// первом же старте падала на записи <c>config.json</c> (<c>UnauthorizedAccessException</c>),
/// а запущенная ярлыком — падала вместе с исчезающим окном консоли, то есть молча.
///
/// Отдавать <c>{app}</c> на запись всем (права на каталог в <c>Program Files</c>) нельзя:
/// любой пользователь компьютера смог бы подменить <c>AI2P.Server.exe</c>, который потом
/// запускает администратор или служба. Правильное поведение на Windows — держать данные
/// ВНЕ каталога программ, и именно это здесь и делается.
///
/// ПРАВИЛО ВЫБОРА (по порядку):
/// <list type="number">
/// <item>каталог задан снаружи — <c>--config &lt;путь&gt;</c> или переменная окружения
/// <c>AI2P_HOME</c>: берём его как есть, ничего не выдумываем;</item>
/// <item>программа стоит в каталоге ПРОГРАММ системы (<c>%ProgramFiles%</c>, <c>/usr</c>,
/// <c>/opt</c>, <c>/Applications</c>) ИЛИ её каталог закрыт на запись — рабочие файлы
/// уходят в общий каталог данных компьютера, а если и он закрыт — в каталог данных
/// пользователя;</item>
/// <item>иначе — рядом с программой, как раньше.</item>
/// </list>
///
/// Про каталог программ спрашивается ОТДЕЛЬНО, а не только «можно ли туда писать»: под
/// администратором в <c>Program Files</c> писать МОЖНО, и проверка правами дала бы разные
/// каталоги данных у одной и той же установки — свои у службы (она работает от системы),
/// свои у человека. Один и тот же ответ при любых правах даёт только путь.
/// </summary>
public static class AppHome
{
    /// <summary>Переменная окружения, задающая рабочий каталог целиком. Ею пользуются
    /// службы, контейнеры и проверки: она сильнее любых правил.</summary>
    public const string EnvVar = "AI2P_HOME";

    /// <summary>Имя рабочего файла конфигурации.</summary>
    public const string ConfigName = "config.json";

    /// <summary>Имя каталога данных: его ищут рядом с программой, чтобы сказать человеку
    /// про данные прежней работы.</summary>
    public const string DataName = "data";

    /// <summary>
    /// ПРАВИЛО ВЫБОРА в чистом виде — без файловой системы и без платформы, поэтому его
    /// можно проверить тестом целиком (сравните <see cref="ServiceRun.Detect"/>).
    /// </summary>
    /// <param name="appDir">Каталог программы.</param>
    /// <param name="envHome">Значение <see cref="EnvVar"/> (пусто — не задано).</param>
    /// <param name="usable">«В этот каталог можно писать» (при надобности создав его).</param>
    /// <param name="isProgramDir">«Это каталог программ системы».</param>
    /// <param name="machineHome">Общий каталог данных компьютера.</param>
    /// <param name="userHome">Каталог данных пользователя.</param>
    /// <returns>Выбранный каталог; <c>null</c> — писать некуда вовсе.</returns>
    public static AppHomeChoice? Choose(
        string appDir,
        string? envHome,
        Func<string, bool> usable,
        Func<string, bool> isProgramDir,
        string machineHome,
        string userHome)
    {
        var env = (envHome ?? "").Trim();
        if (env.Length > 0)
        {
            return new AppHomeChoice(Path.GetFullPath(PathHome.Expand(env)), AppHomeKind.Explicit);
        }
        if (!isProgramDir(appDir) && usable(appDir))
        {
            return new AppHomeChoice(appDir, AppHomeKind.AppDir);
        }
        if (usable(machineHome))
        {
            return new AppHomeChoice(machineHome, AppHomeKind.Machine);
        }
        if (usable(userHome))
        {
            return new AppHomeChoice(userHome, AppHomeKind.User);
        }
        return null;
    }

    /// <summary>
    /// Выбрать рабочий каталог этого запуска и подготовить его: завести, если нужно, и
    /// положить первый <c>config.json</c> из дистрибутива.
    ///
    /// Ключ <c>--config</c> сильнее всего: указанный путь берётся как есть, и дистрибутивом
    /// для него считается его же каталог — иначе стенды проверок и вторые экземпляры на
    /// одном компьютере начали бы вычитывать <c>config.new.json</c> из каталога программы.
    /// Исключение одно, и оно про службу — см. <see cref="DistDirFor"/>.
    /// </summary>
    public static AppHomeInfo Resolve(string[] args)
    {
        var appDir = Path.GetFullPath(AppContext.BaseDirectory);
        var explicitConfig = ConfigArg(args);
        if (explicitConfig.Length > 0)
        {
            var dir = Path.GetDirectoryName(explicitConfig)!;
            return new AppHomeInfo(explicitConfig, dir, DistDirFor(dir, appDir),
                AppHomeKind.Explicit, "", "");
        }
        var choice = Choose(appDir, Environment.GetEnvironmentVariable(EnvVar), CanUse,
                            dir => IsProgramDir(dir, OperatingSystem.IsWindows(), ProgramRoots()),
                            MachineHome(), UserHome())
            ?? throw new UnauthorizedAccessException(
                Loc.T("msg.appHome.3", appDir, MachineHome(), UserHome(), EnvVar));
        return Prepare(choice, appDir);
    }

    /// <summary>
    /// Довести выбранный каталог до рабочего состояния: завести его и, если конфигурации
    /// в нём ещё нет, взять её из каталога программы.
    ///
    /// Порядок источников важен. Первым берётся РАБОЧИЙ <c>config.json</c>, если он остался
    /// рядом с программой: у прежней установки (её ставили и запускали администратором) в нём
    /// лежат настройки человека — порт, имя хоста, каталоги, — и терять их при переезде
    /// нельзя. Новые параметры версии допишет обычное слияние с <c>config.new.json</c>.
    ///
    /// Если рабочего нет (обычная установка из пакета), берётся <c>config.new.json</c> —
    /// конфигурация ИМЕННО этой версии. Раз она взята целиком, сливать её потом не с чем:
    /// отметка «применено» ставится сразу, иначе первый же старт сообщал бы человеку об
    /// обновлении конфигурации, которого не было.
    /// </summary>
    public static AppHomeInfo Prepare(AppHomeChoice choice, string distDir)
    {
        Directory.CreateDirectory(choice.Dir);
        var configPath = Path.Combine(choice.Dir, ConfigName);
        var seededFrom = "";
        if (!SamePath(choice.Dir, distDir) && !File.Exists(configPath))
        {
            var working = Path.Combine(distDir, ConfigName);
            var incoming = Path.Combine(distDir, ConfigMerge.NewConfigName);
            if (File.Exists(working))
            {
                File.Copy(working, configPath);
                seededFrom = working;
                // установка из пакета кладёт ОБА файла, и на первом старте они одинаковы —
                // сливать нечего, и объявлять человеку об обновлении конфигурации не о чем
                if (File.Exists(incoming) &&
                    File.ReadAllText(incoming) == File.ReadAllText(working))
                {
                    ConfigMerge.MarkApplied(choice.Dir, File.ReadAllText(incoming));
                }
            }
            else if (File.Exists(incoming))
            {
                File.Copy(incoming, configPath);
                ConfigMerge.MarkApplied(choice.Dir, File.ReadAllText(incoming));
                seededFrom = incoming;
            }
        }
        // данные прежней работы рядом с программой (её ставили под администратором и запускали
        // им же). Сами их не трогаем: перенос базы за спиной человека — не то, что делают молча
        var legacy = "";
        if (!SamePath(choice.Dir, distDir))
        {
            var oldData = Path.Combine(distDir, DataName);
            if (Directory.Exists(oldData))
            {
                legacy = oldData;
            }
        }
        return new AppHomeInfo(configPath, choice.Dir, distDir, choice.Kind, seededFrom, legacy);
    }

    /// <summary>
    /// Где искать <c>config.new.json</c> для конфигурации, заданной ключом <c>--config</c>.
    ///
    /// Обычно — в её же каталоге: так это работало всегда, и стенды проверок, у которых свой
    /// <c>config.json</c> в стороне, не должны вдруг начать вычитывать конфигурацию из
    /// каталога программы.
    ///
    /// Исключение ровно одно — СЛУЖБА, прибитая к рабочему каталогу ключом <c>--config</c>
    /// (T-271: у службы своя учётная запись, и запасной каталог данных пользователя у неё был
    /// бы свой). Программа при этом стоит там, откуда рабочие файлы и уехали, и файл новой
    /// версии лежит у неё; без этой ветки обновление версии не донесло бы службе ни одного
    /// нового параметра конфигурации.
    /// </summary>
    public static string DistDirFor(string configDir, string appDir) =>
        DistDirFor(configDir, appDir,
            dir => IsProgramDir(dir, OperatingSystem.IsWindows(), ProgramRoots()) || !CanUse(dir));

    /// <summary>То же с внешним ответом на вопрос «этот каталог программы отдал бы рабочие
    /// файлы каталогу данных» — иначе ветку не проверить: настоящий каталог программ в тесте
    /// не завести.</summary>
    public static string DistDirFor(string configDir, string appDir, Func<string, bool> wouldRelocate)
    {
        if (SamePath(configDir, appDir) ||
            File.Exists(Path.Combine(configDir, ConfigMerge.NewConfigName)) ||
            !File.Exists(Path.Combine(appDir, ConfigMerge.NewConfigName)))
        {
            return configDir;
        }
        return wouldRelocate(appDir) ? appDir : configDir;
    }

    /// <summary>Путь из ключа <c>--config</c> (пусто — ключа нет). Разбор тот же, что
    /// у <see cref="Ai2pConfig.ResolvePath"/>: «~» раскрывается (T-135).</summary>
    public static string ConfigArg(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--config")
            {
                return Path.GetFullPath(PathHome.Expand(args[i + 1]));
            }
        }
        return "";
    }

    /// <summary>Общий каталог данных ЭТОГО КОМПЬЮТЕРА: <c>C:\ProgramData\AI2P</c> на Windows,
    /// <c>/var/lib/ai2p</c> на Linux/macOS. Общий он намеренно: установка одна на компьютер,
    /// и служба, поднятая системой, обязана видеть те же данные, что и человек.</summary>
    public static string MachineHome() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AI2P")
        : "/var/lib/ai2p";

    /// <summary>Каталог данных ПОЛЬЗОВАТЕЛЯ — запасной путь, когда общий закрыт на запись.</summary>
    public static string UserHome()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI2P");
        }
        var xdg = (Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "").Trim();
        return xdg.Length > 0
            ? Path.Combine(xdg, "ai2p")
            : Path.Combine(PathHome.Home(), ".local", "share", "ai2p");
    }

    /// <summary>Каталоги ПРОГРАММ этой системы: то, что принадлежит установщику, а не
    /// данным. Пустые значения переменных окружения отбрасываются — иначе в список
    /// попадёт корень диска.</summary>
    public static IReadOnlyList<string> ProgramRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new[] { "/usr", "/opt", "/Applications", "/Library" };
        }
        return new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" }
            .Select(name => (Environment.GetEnvironmentVariable(name) ?? "").Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    /// <summary>Каталог лежит ВНУТРИ каталога программ (или сам им является). Сравнение
    /// на Windows без учёта регистра; «C:\Program Files 2» каталогом программ не считается —
    /// сравниваются полные сегменты пути.</summary>
    public static bool IsProgramDir(string dir, bool windows, IReadOnlyList<string> roots)
    {
        var cmp = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var value = Normalize(dir);
        foreach (var root in roots)
        {
            var prefix = Normalize(root);
            if (prefix.Length == 0)
            {
                continue;
            }
            if (value.Equals(prefix, cmp) ||
                value.StartsWith(prefix + Path.DirectorySeparatorChar, cmp))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>В каталог можно писать (заводя его при надобности). Проверяется НАСТОЯЩЕЙ
    /// записью: права на Windows складываются из ACL, наследования и маркера целостности —
    /// вычислить ответ по атрибутам нельзя, а ошибиться значит уронить старт.</summary>
    public static bool CanUse(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".ai2p-write-" + Guid.NewGuid().ToString("N")[..8]);
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Один и тот же каталог (с поправкой на разделители, хвостовой слэш и регистр
    /// имён на Windows).</summary>
    public static bool SamePath(string left, string right) =>
        Normalize(left).Equals(Normalize(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path)
    {
        var value = (path ?? "").Trim();
        if (value.Length == 0)
        {
            return "";
        }
        try
        {
            value = Path.GetFullPath(PathHome.Expand(value));
        }
        catch (Exception)
        {
            // путь может быть выдуманным (тесты, чужая платформа) — тогда чиним его руками
            value = value.Replace('/', Path.DirectorySeparatorChar);
        }
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
