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
