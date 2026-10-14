using System.Diagnostics;
using System.Text;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// Запуск процесса Claude CLI: общая часть коннектора (<see cref="ClaudeCliConnector"/>)
/// и входа в CLI из AI2P (<see cref="ClaudeLoginService"/>, todo96). Вынесена сюда,
/// чтобы обе стороны одинаково находили <c>claude.cmd</c> на Windows и одинаково читали
/// UTF-8 из перенаправленного вывода.
/// </summary>
public static class CliProcess
{
    /// <summary>Старт процесса; на Windows npm-установка даёт claude.cmd, который CreateProcess
    /// не находит без расширения — фолбэк через cmd.exe /c.</summary>
    /// <param name="env">Переменные окружения процесса ПОВЕРХ унаследованных (T-34-S0):
    /// ими агенту передаются адрес сервера и токен задания для клиента <c>ai2p</c>.
    /// null — окружение как у нас.</param>
    public static Process Start(string fileName, string arguments, string workDir, bool redirectStdin,
        IReadOnlyDictionary<string, string>? env = null)
    {
        // ПО CLI, поставленное AI2P для пользователя (T-431-S0): каталоги в PATH процесса
        // и полный путь программы — сам по себе каталог пакетов в PATH не попадает
        var extra = ExtraDirs();
        if (extra.Count > 0)
        {
            var merged = new Dictionary<string, string>(env ?? new Dictionary<string, string>());
            var basePath = merged.TryGetValue("PATH", out var p) ? p : Environment.GetEnvironmentVariable("PATH") ?? "";
            merged["PATH"] = string.Join(Path.PathSeparator, extra) + Path.PathSeparator + basePath;
            env = merged;
            if (Resolve(fileName, extra) is { } full)
            {
                var ext = Path.GetExtension(full);
                if (OperatingSystem.IsWindows() && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                                                    || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
                {
                    arguments = $"/c \"\"{full}\" {arguments}\"";
                    fileName = "cmd.exe";
                }
                else
                {
                    fileName = full;
                }
            }
        }
        var psi = BuildStartInfo(fileName, arguments, workDir, redirectStdin, env);
        try
        {
            return Process.Start(psi)
                   ?? throw new InvalidOperationException(Loc.T("msg.claudeCliConnector.21", fileName));
        }
        catch (System.ComponentModel.Win32Exception)
            when (OperatingSystem.IsWindows() && !Path.HasExtension(fileName))
        {
            var wrapped = BuildStartInfo("cmd.exe", $"/c {fileName} {arguments}", workDir, redirectStdin, env);
            return Process.Start(wrapped)
                   ?? throw new InvalidOperationException(Loc.T("msg.claudeCliConnector.22", fileName));
        }
    }

    /// <summary>Запуск CLI-процесса до конца: stdin — текст (UTF-8) либо null, stdout/stderr
    /// вычитываются полностью (без дедлока на буферах), отмена — kill всего дерева процессов.</summary>
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName, string arguments, string workDir, string? stdin, CancellationToken ct,
        IReadOnlyDictionary<string, string>? env = null)
    {
        using var process = Start(fileName, arguments, workDir, redirectStdin: stdin is not null, env);
        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
                process.StandardInput.Close();
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    /// <summary>Убить процесс вместе с деревом; уже завершившийся — просто пропустить.</summary>
    public static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // процесс уже завершился — убивать нечего
        }
    }

    /// <summary>
    /// Каталоги ПО CLI, поставленного AI2P (T-431-S0): свой Node.js и программы CLI в
    /// каталоге пакетов. Задаёт установщик моделей; null — ничего не добавляется.
    /// </summary>
    public static Func<List<string>>? ExtraPath { get; set; }

    private static List<string> ExtraDirs()
    {
        try
        {
            return ExtraPath?.Invoke().Where(Directory.Exists).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.Text.Json.JsonException)
        {
            return [];   // каталог пакетов не читается — запускаем как раньше, по PATH
        }
    }

    /// <summary>Полный путь команды без каталога в дополнительных каталогах; null — не там.</summary>
    private static string? Resolve(string fileName, List<string> dirs)
    {
        if (fileName.Length == 0 || fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            return null;
        }
        string[] names = OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
            ? [fileName + ".exe", fileName + ".cmd", fileName + ".bat"]
            : [fileName];
        return dirs.SelectMany(d => names.Select(n => Path.Combine(d, n))).FirstOrDefault(File.Exists);
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string arguments, string workDir,
        bool redirectStdin, IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = redirectStdin
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                : null,
        };
        if (env is not null)
        {
            foreach (var (name, value) in env)
            {
                psi.Environment[name] = value;
            }
        }
        return psi;
    }
}
