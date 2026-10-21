using System.Diagnostics;
using AI2P.Core;

namespace AI2P.Server;

/// <summary>
/// ОБНОВЛЕНИЕ РАСШИРЕНИЯ VS CODE СО СТОРОНЫ СЕРВЕРА (этап 8.6, T-511-S0). При старте сервер
/// опрашивает каждую запомненную копию VS Code (<see cref="VsCodeExtension.RememberedCopies"/>)
/// командой <c>code --list-extensions --show-versions</c> и, если наше расширение отстаёт
/// от версии AI2P, ставит его заново из <c>ai2p-&lt;версия&gt;.vsix</c> рядом с программой
/// (<c>--install-extension … --force</c>). Работает ФОНОМ: <c>code</c> отвечает секундами,
/// а старт сервера ждать его не должен. Отказ любой копии — строка в журнале, не больше.
/// </summary>
public static class VsCodeExtensionUpdater
{
    static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Запустить проход фоном после старта приложения.</summary>
    public static void StartInBackground(IHostApplicationLifetime lifetime, ILogger logger) =>
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(() =>
        {
            try
            {
                Run(AppContext.BaseDirectory, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI2P: обновление расширения VS Code не удалось");
            }
        }));

    /// <summary>Один проход по всем копиям. Возвращает число обновлённых.</summary>
    public static int Run(string appDir, ILogger logger)
    {
        var copies = VsCodeExtension.SelectCopies(VsCodeExtension.RememberedCopies(), File.Exists);
        if (copies.Count == 0)
        {
            return 0;
        }
        if (VsCodeExtension.VsixPath(appDir) is not { } vsix)
        {
            logger.LogInformation("AI2P: файла {File} рядом с программой нет — расширение VS Code не обновляется",
                VsCodeExtension.VsixName(AppInfo.Version));
            return 0;
        }
        var updated = 0;
        foreach (var code in copies)
        {
            var list = RunCode(code, VsCodeExtension.ListArgs, ListTimeout);
            if (list.ExitCode != 0)
            {
                logger.LogWarning("AI2P: {Code} --list-extensions: код {Exit}, {Err}", code, list.ExitCode, list.Error);
                continue;
            }
            var installed = VsCodeExtension.InstalledVersion(list.Output);
            if (!VsCodeExtension.NeedsUpdate(list.Output, AppInfo.Version))
            {
                continue;
            }
            var install = RunCode(code, VsCodeExtension.InstallArgs(vsix), InstallTimeout);
            if (install.ExitCode == 0)
            {
                updated++;
                logger.LogInformation("AI2P: расширение VS Code {Old} → {New} обновлено в {Code}",
                    installed, VsCodeExtension.ExtensionVersion(AppInfo.Version), code);
            }
            else
            {
                logger.LogWarning("AI2P: {Code} --install-extension: код {Exit}, {Err}", code, install.ExitCode, install.Error);
            }
        }
        return updated;
    }

    /// <summary>Результат запуска <c>code</c>; ExitCode -1 — не запустился или не уложился в срок.</summary>
    public sealed record CodeResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Запустить <c>code</c> с аргументами и дождаться конца. На Windows <c>code.cmd</c> —
    /// пакетный файл, его исполняет <c>cmd.exe /d /s /c ""путь" аргументы"</c>: ключ /s снимает
    /// внешние кавычки, поэтому и путь, и путь к vsix с пробелами доходят целыми.
    /// </summary>
    public static CodeResult RunCode(string codePath, IReadOnlyList<string> args, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var ext = Path.GetExtension(codePath);
        if (OperatingSystem.IsWindows()
            && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = "/d /s /c \"" + Quote(codePath) + " " + string.Join(" ", args.Select(Quote)) + "\"";
        }
        else
        {
            psi.FileName = codePath;
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
        }
        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                return new CodeResult(-1, "", "process did not start");
            }
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout ?? ListTimeout))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return new CodeResult(-1, "", "timeout");
            }
            p.WaitForExit();
            return new CodeResult(p.ExitCode, output.Result, error.Result.Trim());
        }
        catch (Exception ex)
        {
            return new CodeResult(-1, "", ex.Message);
        }
    }

    static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";
}
