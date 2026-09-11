using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AI2P.Connectors;

/// <summary>
/// «ЭТОТ ПАКЕТ УЖЕ СТОИТ НА КОМПЬЮТЕРЕ?» (T-4-S0) — проверка для пакетов, которые человек
/// обычно ставит сам, задолго до нас: Python. Ставить рядом второй экземпляр незачем, а вот
/// СЧИТАТЬ, что раз команда нашлась, то она годится, нельзя: обучение LoRA идёт на
/// Python 3.10–3.12, и найденный 3.13 обернулся бы падением через несколько часов после
/// нажатия кнопки.
///
/// Поэтому проверок две: команда есть в PATH И её версия попадает в объявленный пакетом
/// диапазон (когда он объявлен). Обе дешёвые, обе делаются на этом сервере — «стоит ли он у
/// вас» это вопрос про компьютер, а не про организацию, и ответ у каждого сервера свой.
///
/// ЧТО ЗДЕСЬ НЕОЧЕВИДНО: файлы НУЛЕВОЙ ДЛИНЫ пропускаются. В Windows 10/11 в
/// %LOCALAPPDATA%\Microsoft\WindowsApps лежат пустые заглушки-псевдонимы (python.exe и
/// подобные): запуск такой заглушки открывает магазин приложений — то есть окно на рабочем
/// столе человека и висящий процесс (та же беда, что в T-271), а версии мы всё равно не
/// узнаем.
/// </summary>
public static class SystemPackageProbe
{
    /// <summary>Сколько ждать ответа на «--version»: столько ни одна программа не думает.</summary>
    private const int VersionTimeoutMs = 10_000;

    /// <summary>
    /// Путь к установленной в системе программе пакета; null — не нашлась либо её версия
    /// не годится. <paramref name="searchPath"/> — список каталогов через разделитель PATH.
    /// </summary>
    public static string? Find(ModelPackage.SystemProbe probe, string searchPath)
    {
        foreach (var path in Candidates(probe.Commands, searchPath))
        {
            if (probe.VersionArgs.Trim().Length == 0)
            {
                return path;    // версию не спрашивали — годится найденное
            }
            var version = VersionOf(path, probe.VersionArgs);
            if (Accepts(version, probe.MinVersion, probe.MaxVersion))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Файлы команд, найденные в каталогах пути (по порядку команд, затем каталогов).
    /// Команда без расширения на Windows ищется со всеми расширениями PATHEXT.
    /// </summary>
    public static IEnumerable<string> Candidates(IReadOnlyList<string> commands, string searchPath)
    {
        var dirs = searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim().Trim('"'))
            .Where(d => d.Length > 0)
            .ToList();
        foreach (var command in commands)
        {
            // порядок как у самой системы: сначала КАТАЛОГ, внутри него — расширения PATHEXT
            foreach (var dir in dirs)
            {
                foreach (var name in Names(command))
                {
                    // имя берётся С ДИСКА, а не складывается из маски: иначе путь, показанный
                    // человеку в окне установки, выглядел бы «python.EXE» — расширения
                    // приходят из PATHEXT заглавными
                    var full = FileIn(dir, name);
                    // пустой файл — псевдоним магазина приложений Windows, а не программа
                    if (full is not null && Length(full) > 0)
                    {
                        yield return full;
                    }
                }
            }
        }
    }

    /// <summary>Файл с таким именем в каталоге — как он записан НА ДИСКЕ; null — нет такого.</summary>
    private static string? FileIn(string dir, string name)
    {
        try
        {
            return Directory.GetFiles(dir, name).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return null;    // каталога нет либо в PATH мусор — просто пропускаем
        }
    }

    /// <summary>Имена файла команды: как есть, а без расширения на Windows — со всеми PATHEXT.</summary>
    private static IEnumerable<string> Names(string command)
    {
        if (!OperatingSystem.IsWindows() || Path.GetExtension(command).Length > 0)
        {
            return [command];
        }
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.StartsWith('.'))
            .ToList();
        return exts.Count > 0 ? exts.Select(e => command + e) : [command + ".exe"];
    }

    /// <summary>
    /// Версия программы: запуск с ключом и разбор первого числа вида 3.12.14 в её ответе.
    /// Читаются ОБА потока: Python 3 печатает версию в stdout, Python 2 — в stderr, и
    /// «не разобрали версию» здесь означает «не годится», а не «сойдёт».
    /// </summary>
    public static string VersionOf(string exePath, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return "";
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(VersionTimeoutMs))
            {
                Kill(process);
                return "";
            }
            var text = output.GetAwaiter().GetResult() + "\n" + errors.GetAwaiter().GetResult();
            var match = Regex.Match(text, @"(\d+)\.(\d+)(\.\d+)?");
            return match.Success ? match.Value : "";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or InvalidOperationException or IOException)
        {
            return "";  // программы нет, она не запускается или это не программа вовсе
        }
    }

    /// <summary>
    /// Версия попадает в объявленный диапазон. Сравниваются ПАРЫ «старшая.младшая»
    /// (3.12.14 — это 3.12), пределы включительные: у требований вида «Python 3.10–3.12»
    /// третье число не значит ничего. Версию разобрать не удалось — не годится.
    /// </summary>
    public static bool Accepts(string version, string min, string max)
    {
        var found = Pair(version);
        if (found is null)
        {
            return false;
        }
        var low = Pair(min);
        var high = Pair(max);
        return (low is null || Compare(found.Value, low.Value) >= 0)
               && (high is null || Compare(found.Value, high.Value) <= 0);
    }

    private static (int Major, int Minor)? Pair(string version)
    {
        var match = Regex.Match(version ?? "", @"(\d+)(?:\.(\d+))?");
        if (!match.Success)
        {
            return null;
        }
        var major = int.Parse(match.Groups[1].Value);
        var minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        return (major, minor);
    }

    private static int Compare((int Major, int Minor) a, (int Major, int Minor) b) =>
        a.Major != b.Major ? a.Major.CompareTo(b.Major) : a.Minor.CompareTo(b.Minor);

    private static long Length(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // процесс успел кончиться сам
        }
    }
}
