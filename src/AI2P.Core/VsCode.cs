using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI2P.Core;

/// <summary>Одна найденная копия VS Code или его родни (T-510-S0).</summary>
public sealed class VsCodeInstall
{
    /// <summary>Вид копии: code, code-insiders, codium, cursor, windsurf.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Имя для человека: «Visual Studio Code», «VSCodium» …</summary>
    public string Name { get; set; } = "";

    /// <summary>Версия; пусто — узнать не удалось.</summary>
    public string Version { get; set; } = "";

    /// <summary>Полный путь к команде code (code.cmd на Windows) — им и зовём копию.</summary>
    public string CodePath { get; set; } = "";

    /// <summary>Схема URI handler'а копии: vscode, vscode-insiders, vscodium, cursor, windsurf.</summary>
    public string UriScheme { get; set; } = "";

    /// <summary>ВНУТРЕННЯЯ копия — та, что подключена плагином «VS Code» (T-530-S0): поставлена
    /// его пакетом или выбрана в его форме. Её форма выбора берёт по умолчанию.</summary>
    public bool Internal { get; set; }
}

/// <summary>
/// Что поиск VS Code спрашивает у машины. Отдельным интерфейсом — чтобы проверять поиск на
/// поддельных путях и поддельном реестре (T-510-S0).
/// </summary>
public interface IVsCodeMachine
{
    bool IsWindows { get; }
    bool IsMac { get; }
    bool FileExists(string path);
    string? ReadText(string path);
    string? Env(string name);

    /// <summary>Ключи Uninstall реестра (HKCU и HKLM, 64 и 32 бита): имя, каталог, версия.</summary>
    IEnumerable<(string DisplayName, string InstallLocation, string DisplayVersion)> UninstallEntries();

    /// <summary>Первая строка вывода «code --version»; null — не ответил.</summary>
    string? RunVersion(string codePath);
}

/// <summary>
/// ПОИСК УСТАНОВЛЕННЫХ VS CODE (T-510-S0, этап 8.5 плагина) — по образцу блока "system"
/// справочника пакетов (T-4-S0): сначала известные места, потом PATH, версия по возможности
/// без запуска программы. Windows: %LOCALAPPDATA%\Programs, Program Files, ключи Uninstall
/// реестра, PATH (то же, что «where code»); Linux и macOS: PATH (то же, что «which»),
/// /Applications/*.app, snap, flatpak. Версия берётся из ключа реестра или resources/app/
/// package.json; «code --version» зовётся только когда иначе нельзя, и ответ живёт
/// <see cref="CacheLifetime"/>: окно карточки проекта спрашивает список при каждом открытии.
/// </summary>
public sealed class VsCodeLocator
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    private sealed record Flavor(string Kind, string Name, string Cmd, string Scheme, string[] WinDirs,
        string[] RegistryNames, string[] MacApps, string[] Flatpaks);

    // порядок — порядок показа человеку: настоящий VS Code первым
    private static readonly Flavor[] Flavors =
    [
        new("code", "Visual Studio Code", "code", "vscode", ["Microsoft VS Code"],
            ["Microsoft Visual Studio Code", "Visual Studio Code"], ["Visual Studio Code.app"],
            ["com.visualstudio.code"]),
        new("code-insiders", "Visual Studio Code Insiders", "code-insiders", "vscode-insiders",
            ["Microsoft VS Code Insiders"],
            ["Microsoft Visual Studio Code Insiders", "Visual Studio Code - Insiders", "Visual Studio Code Insiders"],
            ["Visual Studio Code - Insiders.app"], ["com.visualstudio.code.insiders"]),
        new("codium", "VSCodium", "codium", "vscodium", ["VSCodium"], ["VSCodium"], ["VSCodium.app"],
            ["com.vscodium.codium"]),
        new("cursor", "Cursor", "cursor", "cursor", ["cursor", "Cursor"], ["Cursor"], ["Cursor.app"], []),
        new("windsurf", "Windsurf", "windsurf", "windsurf", ["Windsurf"], ["Windsurf"], ["Windsurf.app"], []),
    ];

    private readonly IVsCodeMachine _machine;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private List<VsCodeInstall>? _cached;
    private DateTime _cachedAt;

    public VsCodeLocator(IVsCodeMachine? machine = null, Func<DateTime>? now = null)
    {
        _machine = machine ?? new RealVsCodeMachine();
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Найденные копии (из кеша, если он свеж); refresh — искать заново.</summary>
    public List<VsCodeInstall> Find(bool refresh = false)
    {
        lock (_lock)
        {
            if (!refresh && _cached is not null && _now() - _cachedAt < CacheLifetime)
            {
                return [.. _cached];
            }
            _cached = Scan();
            _cachedAt = _now();
            return [.. _cached];
        }
    }

    /// <summary>Копия по пути к code; null — такой среди найденных нет.</summary>
    public VsCodeInstall? ByPath(string? codePath) =>
        codePath is { Length: > 0 }
            ? Find().FirstOrDefault(x => SamePath(x.CodePath, codePath))
            : null;

    /// <summary>
    /// Найденные копии плюс копия плагина «VS Code» (T-530-S0), помеченная внутренней. Копия,
    /// поставленная пакетом плагина, лежит в каталоге пакетов — ни в PATH, ни в известных местах
    /// её нет, поэтому она добавляется сюда по пути из настройки плагина.
    /// </summary>
    public List<VsCodeInstall> FindWith(string? internalPath, bool refresh = false)
    {
        // копии записей: объекты кеша общие, пометка «внутренняя» на них не должна оседать
        var list = Find(refresh).Select(x => new VsCodeInstall
        {
            Kind = x.Kind, Name = x.Name, Version = x.Version, CodePath = x.CodePath, UriScheme = x.UriScheme,
        }).ToList();
        if (internalPath is not { Length: > 0 } || !_machine.FileExists(internalPath))
        {
            return list;
        }
        var hit = list.FirstOrDefault(x => SamePath(x.CodePath, internalPath));
        if (hit is null)
        {
            var flavor = Flavors.FirstOrDefault(f => Path.GetFileNameWithoutExtension(internalPath)
                .Equals(f.Cmd, StringComparison.OrdinalIgnoreCase)) ?? Flavors[0];
            hit = new VsCodeInstall
            {
                Kind = flavor.Kind,
                Name = flavor.Name,
                CodePath = internalPath,
                UriScheme = flavor.Scheme,
                Version = PackageVersion(internalPath) ?? "",
            };
            list.Insert(0, hit);
        }
        hit.Internal = true;
        return list;
    }

    private List<VsCodeInstall> Scan()
    {
        var found = new List<VsCodeInstall>();
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(Flavor flavor, string codePath, string version = "")
        {
            if (!_machine.FileExists(codePath) || found.Any(x => SamePath(x.CodePath, codePath)))
            {
                return;
            }
            found.Add(new VsCodeInstall
            {
                Kind = flavor.Kind,
                Name = flavor.Name,
                CodePath = codePath,
                UriScheme = flavor.Scheme,
                Version = version,
            });
        }

        if (_machine.IsWindows)
        {
            var roots = new[]
            {
                _machine.Env("LOCALAPPDATA") is { Length: > 0 } local ? Path.Combine(local, "Programs") : null,
                _machine.Env("ProgramFiles"),
                _machine.Env("ProgramFiles(x86)"),
            };
            foreach (var flavor in Flavors)
            {
                foreach (var root in roots.OfType<string>().Where(r => r.Length > 0))
                {
                    foreach (var dir in flavor.WinDirs)
                    {
                        foreach (var cmd in WinCommands(Path.Combine(root, dir), flavor))
                        {
                            Add(flavor, cmd);
                        }
                    }
                }
            }
            // ключи Uninstall: копия, поставленная в нестандартный каталог; заодно — версия
            foreach (var (name, location, version) in _machine.UninstallEntries())
            {
                if (location.Trim().Length == 0 || FlavorByDisplayName(name) is not { } flavor)
                {
                    continue;
                }
                foreach (var cmd in WinCommands(location.Trim().TrimEnd('\\', '/'), flavor))
                {
                    Add(flavor, cmd);
                    if (version.Length > 0)
                    {
                        versions[Normalize(cmd)] = version;
                    }
                }
            }
        }
        else
        {
            foreach (var flavor in Flavors)
            {
                if (_machine.IsMac)
                {
                    foreach (var app in flavor.MacApps)
                    {
                        Add(flavor, Path.Combine("/Applications", app, "Contents/Resources/app/bin", flavor.Cmd));
                        if (_machine.Env("HOME") is { Length: > 0 } home)
                        {
                            Add(flavor, Path.Combine(home, "Applications", app, "Contents/Resources/app/bin",
                                flavor.Cmd));
                        }
                    }
                }
                Add(flavor, "/snap/bin/" + flavor.Cmd);
                foreach (var pak in flavor.Flatpaks)
                {
                    Add(flavor, "/var/lib/flatpak/exports/bin/" + pak);
                    if (_machine.Env("HOME") is { Length: > 0 } home)
                    {
                        Add(flavor, Path.Combine(home, ".local/share/flatpak/exports/bin", pak));
                    }
                }
            }
        }

        // PATH — то же, что «where code» / «which code»
        var pathDirs = (_machine.Env("PATH") ?? "")
            .Split(_machine.IsWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var flavor in Flavors)
        {
            foreach (var dir in pathDirs)
            {
                Add(flavor, Path.Combine(dir, _machine.IsWindows ? flavor.Cmd + ".cmd" : flavor.Cmd));
            }
        }

        foreach (var install in found)
        {
            install.Version = versions.TryGetValue(Normalize(install.CodePath), out var v) ? v
                : PackageVersion(install.CodePath) ?? _machine.RunVersion(install.CodePath)?.Trim() ?? "";
        }
        return found;
    }

    /// <summary>Где у копии лежит code.cmd: bin\ у VS Code, resources\app\bin у Cursor.</summary>
    private static IEnumerable<string> WinCommands(string installDir, Flavor flavor)
    {
        yield return Path.Combine(installDir, "bin", flavor.Cmd + ".cmd");
        yield return Path.Combine(installDir, "resources", "app", "bin", flavor.Cmd + ".cmd");
    }

    private static Flavor? FlavorByDisplayName(string displayName)
    {
        // побеждает САМОЕ ДЛИННОЕ совпавшее начало: «Microsoft Visual Studio Code Insiders (User)»
        // начинается и с «Microsoft Visual Studio Code», но это Insiders
        return Flavors
            .SelectMany(f => f.RegistryNames.Select(n => (Flavor: f, Name: n)))
            .Where(x => displayName.StartsWith(x.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name.Length)
            .Select(x => x.Flavor)
            .FirstOrDefault();
    }

    /// <summary>Версия из resources/app/package.json рядом с bin — без запуска программы.</summary>
    private string? PackageVersion(string codePath)
    {
        var bin = Path.GetDirectoryName(codePath);
        if (bin is null)
        {
            return null;
        }
        foreach (var candidate in new[]
                 {
                     Path.Combine(bin, "..", "resources", "app", "package.json"), // Windows: <копия>\bin
                     Path.Combine(bin, "..", "package.json"),                    // macOS, Cursor: app\bin
                 })
        {
            if (_machine.ReadText(Path.GetFullPath(candidate)) is not { } text)
            {
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (JsonRead.Str(doc.RootElement, "version") is { Length: > 0 } version)
                {
                    return version;
                }
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}

/// <summary>Настоящая машина для <see cref="VsCodeLocator"/>.</summary>
public sealed class RealVsCodeMachine : IVsCodeMachine
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMac => OperatingSystem.IsMacOS();

    // файл нулевой длины — псевдоним магазина приложений Windows (наука T-4-S0)
    public bool FileExists(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && (info.Length > 0 || !OperatingSystem.IsWindows());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return false;
        }
    }

    public string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? Env(string name) => Environment.GetEnvironmentVariable(name);

    public IEnumerable<(string DisplayName, string InstallLocation, string DisplayVersion)> UninstallEntries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        var list = new List<(string, string, string)>();
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                try
                {
                    using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                    {
                        continue;
                    }
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is string display)
                        {
                            list.Add((display, key.GetValue("InstallLocation") as string ?? "",
                                key.GetValue("DisplayVersion") as string ?? ""));
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or System.Security.SecurityException)
                {
                }
            }
        }
        return list;
    }

    public string? RunVersion(string codePath)
    {
        var result = VsCodeRunner.Run(codePath, ["--version"], TimeSpan.FromSeconds(20));
        return result.ExitCode == 0
            ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault()
            : null;
    }
}

/// <summary>
/// Запуск команды code. На Windows code.cmd идёт через cmd.exe, и КАЖДЫЙ аргумент берётся в
/// кавычки вручную: в URI handler'а есть «&amp;», и без кавычек cmd разрезал бы строку на
/// две команды (а вторую выполнил бы).
/// </summary>
public static class VsCodeRunner
{
    public sealed record Result(int ExitCode, string Output);

    public static ProcessStartInfo StartInfo(string codePath, IReadOnlyList<string> args)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows() && codePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            info = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /s /c \"" + string.Join(" ", new[] { codePath }.Concat(args).Select(Quote)) + "\"",
            };
        }
        else
        {
            info = new ProcessStartInfo(codePath);
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }
        }
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        return info;
    }

    /// <summary>Аргумент cmd.exe в кавычках; кавычки и «%» внутри недопустимы — выбрасываются.</summary>
    public static string Quote(string arg) => "\"" + arg.Replace("\"", "").Replace("%", "") + "\"";

    public static Result Run(string codePath, IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            using var process = Process.Start(StartInfo(codePath, args));
            if (process is null)
            {
                return new Result(-1, "");
            }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                }
                return new Result(-1, Loc.T("msg.vscode.1"));
            }
            return new Result(process.ExitCode, (stdout.Result + "\n" + stderr.Result).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                       or InvalidOperationException)
        {
            return new Result(-1, ex.Message);
        }
    }
}

/// <summary>
/// Файл <c>.ai2p/link.json</c> в каталоге проекта (T-510-S0): по нему расширение VS Code
/// узнаёт, что проект подключён к AI2P, и по нему же AI2P считает проект подключённым.
/// Формат согласован с расширением (T-509-S0, src/link.ts): server — адрес С базовым путём,
/// serverExe/serverArgs — для автозапуска сервера. СЕКРЕТОВ В ФАЙЛЕ НЕТ: его могут закоммитить
/// в git, токен устройства живёт в SecretStorage расширения.
/// </summary>
public sealed class VsCodeLink
{
    public const string DirName = ".ai2p";
    public const string FileName = "link.json";

    [JsonPropertyName("server")] public string Server { get; set; } = "";
    [JsonPropertyName("org")] public string Org { get; set; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("serverExe")] public string ServerExe { get; set; } = "";
    [JsonPropertyName("serverArgs")] public List<string> ServerArgs { get; set; } = [];
    [JsonPropertyName("basePath")] public string BasePath { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string PathIn(string projectFolder) => Path.Combine(projectFolder, DirName, FileName);

    /// <summary>
    /// Адрес сервера для встроенного режима: localhost по HTTP — WebView не примет
    /// самоподписанный сертификат (T-215). Порт и базовый путь — из настроек сервера.
    /// </summary>
    public static string ServerUrl(int port, string basePath) =>
        "http://localhost:" + port + (basePath.Trim('/').Length > 0 ? "/" + basePath.Trim('/') : "");

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static VsCodeLink? Read(string projectFolder)
    {
        try
        {
            var path = PathIn(projectFolder);
            return File.Exists(path) ? JsonSerializer.Deserialize<VsCodeLink>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsConnected(string? projectFolder) =>
        projectFolder is { Length: > 0 } && File.Exists(PathIn(projectFolder));

    /// <summary>Ссылка URI handler'а с токеном устройства (этап 8.3): publisher ai2p, name ai2p.</summary>
    public static string LinkUri(string scheme, string token, string server, string org, string projectId,
        long? windowId = null) =>
        scheme + "://ai2p.ai2p/link?token=" + Uri.EscapeDataString(token)
        + "&server=" + QueryValue(server)
        + "&org=" + Uri.EscapeDataString(org)
        + "&projectId=" + Uri.EscapeDataString(projectId)
        + (windowId is { } w ? "&windowId=" + w : "");

    public const string WindowFileName = "window.json";

    /// <summary>Номер окна VS Code, открытого на папке проекта (T-344): его пишет расширение
    /// в .ai2p/window.json при активации и фокусе. <c>code --open-url</c> отдаёт ссылку последнему
    /// активному окну — обычно первому, а не только что открытому; ссылку с windowId VS Code
    /// направляет в окно с этим номером. Ждём до <paramref name="wait"/>, пока файл станет не старше
    /// <paramref name="notBefore"/> (окно, открытое только что, пишет его при активации); не дождались —
    /// берём то, что есть: окно с таким номером не нашлось — VS Code поступит как раньше.</summary>
    public static long? WindowId(string projectFolder, DateTime notBefore, TimeSpan wait)
    {
        var path = Path.Combine(projectFolder, DirName, WindowFileName);
        var until = DateTime.UtcNow + wait;
        while (File.Exists(path) && File.GetLastWriteTimeUtc(path) < notBefore && DateTime.UtcNow < until)
        {
            Thread.Sleep(300);
        }
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("windowId", out var w) && w.TryGetInt64(out var id) && id > 0
                ? id : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Значение запроса без «%» там, где можно (T-525-S0): на Windows ссылка уходит через
    /// cmd.exe, а <see cref="VsCodeRunner.Quote"/> выбрасывает «%» — «http%3A%2F%2F…» доезжал до
    /// расширения как «http3A2F2F…», и токен ложился под чужой ключ. «:» и «/» в запросе URI
    /// допустимы как есть.</summary>
    public static string QueryValue(string value) =>
        Uri.EscapeDataString(value).Replace("%3A", ":").Replace("%2F", "/");

    /// <summary>В .gitignore проекта уже есть строка, закрывающая .ai2p/.</summary>
    public static bool GitIgnoreHas(string projectFolder)
    {
        var path = Path.Combine(projectFolder, ".gitignore");
        try
        {
            return File.Exists(path) && File.ReadAllLines(path).Select(l => l.Trim())
                .Any(l => l is ".ai2p" or ".ai2p/" or "/.ai2p" or "/.ai2p/" or ".ai2p/*" or "/.ai2p/*");
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Дописать «.ai2p/» в .gitignore проекта (файла нет — создаётся).</summary>
    public static void AddToGitIgnore(string projectFolder)
    {
        if (GitIgnoreHas(projectFolder))
        {
            return;
        }
        var path = Path.Combine(projectFolder, ".gitignore");
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var prefix = text.Length > 0 && !text.EndsWith('\n') ? Environment.NewLine : "";
        File.AppendAllText(path, prefix + ".ai2p/" + Environment.NewLine);
    }
}
