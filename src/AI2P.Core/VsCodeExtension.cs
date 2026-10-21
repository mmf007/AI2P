namespace AI2P.Core;

/// <summary>
/// Расширение AI2P для VS Code (этап 8.6, T-511-S0): имя файла <c>.vsix</c> в выкладке,
/// сравнение версий и выбор копий VS Code, которым нужно обновление. Здесь только чистые
/// функции — запуск <c>code</c> живёт в сервере (<c>VsCodeExtensionUpdater</c>).
///
/// ВЕРСИЯ РАСШИРЕНИЯ РАВНА ВЕРСИИ AI2P: <c>1.NN</c> → <c>1.NN.0</c> (VS Code требует трёх
/// чисел). Сборщик <c>vscode/pack-vsix.*</c> подставляет её в <c>package.json</c> сам, поэтому
/// вручную поднимать версию в исходниках расширения при выпуске не нужно.
/// </summary>
public static class VsCodeExtension
{
    /// <summary>Идентификатор расширения в VS Code: <c>publisher.name</c> из package.json.</summary>
    public const string Id = "ai2p.ai2p";

    /// <summary>
    /// Откуда сервер берёт запомненные копии VS Code (пути к <c>code</c>/<c>code.cmd</c>).
    /// Задаёт этап 8.5 (T-510-S0), который хранит выбор копии в локальной части проекта;
    /// пока не задано — обновлять при старте некого.
    /// </summary>
    public static Func<IEnumerable<string>> RememberedCopies { get; set; } = () => [];

    /// <summary>Имя файла в выкладке: <c>ai2p-1.168.vsix</c>.</summary>
    public static string VsixName(string appVersion) => "ai2p-" + appVersion.Trim() + ".vsix";

    /// <summary>Версия расширения для версии AI2P: <c>1.168</c> → <c>1.168.0</c>.</summary>
    public static string ExtensionVersion(string appVersion)
    {
        var parts = Parse(appVersion);
        return parts[0] + "." + parts[1] + "." + parts[2];
    }

    /// <summary>Файл расширения своей версии рядом с программой; null — его нет.</summary>
    public static string? VsixPath(string appDir)
    {
        var path = Path.Combine(appDir, VsixName(AppInfo.Version));
        return File.Exists(path) ? path : null;
    }

    /// <summary>«1.168» / «1.168.0» / «1.168.0-pre» → три числа; не разобранное — нули.</summary>
    public static int[] Parse(string? version)
    {
        var result = new int[3];
        var parts = (version ?? "").Trim().Split('.', '-', '+');
        for (var i = 0; i < 3 && i < parts.Length; i++)
        {
            int.TryParse(parts[i], out result[i]);
        }
        return result;
    }

    /// <summary>Версия <paramref name="installed"/> старше версии <paramref name="target"/>?</summary>
    public static bool IsOlder(string? installed, string target)
    {
        var a = Parse(installed);
        var b = Parse(target);
        for (var i = 0; i < 3; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] < b[i];
            }
        }
        return false;
    }

    /// <summary>
    /// Версия нашего расширения из вывода <c>code --list-extensions --show-versions</c>
    /// (строки вида <c>ai2p.ai2p@1.167.0</c>); null — расширение в этой копии не стоит.
    /// </summary>
    public static string? InstalledVersion(string? listOutput)
    {
        foreach (var raw in (listOutput ?? "").Split('\n'))
        {
            var line = raw.Trim();
            var at = line.LastIndexOf('@');
            if (at > 0 && string.Equals(line[..at], Id, StringComparison.OrdinalIgnoreCase))
            {
                return line[(at + 1)..];
            }
        }
        return null;
    }

    /// <summary>
    /// Нужно ли обновление по выводу списка расширений. Копию, где расширения нет вовсе,
    /// обновление не трогает: ставит его «Подключить VS Code» (этап 8.5), а не старт сервера.
    /// </summary>
    public static bool NeedsUpdate(string? listOutput, string appVersion) =>
        InstalledVersion(listOutput) is { } installed && IsOlder(installed, appVersion);

    /// <summary>
    /// Копии, которые стоит опросить: без пустых, без несуществующих файлов и без повторов
    /// (один VS Code бывает запомнен несколькими проектами). Регистр пути не различается
    /// на Windows и macOS.
    /// </summary>
    public static IReadOnlyList<string> SelectCopies(IEnumerable<string?> paths, Func<string, bool> exists)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        var result = new List<string>();
        foreach (var raw in paths)
        {
            var path = (raw ?? "").Trim().Trim('"');
            if (path.Length == 0 || !exists(path))
            {
                continue;
            }
            if (seen.Add(Path.GetFullPath(path)))
            {
                result.Add(path);
            }
        }
        return result;
    }

    /// <summary>Аргументы <c>code</c>: список расширений с версиями.</summary>
    public static readonly string[] ListArgs = ["--list-extensions", "--show-versions"];

    /// <summary>Аргументы <c>code</c>: поставить или заменить расширение из файла.</summary>
    public static string[] InstallArgs(string vsixPath) => ["--install-extension", vsixPath, "--force"];
}
