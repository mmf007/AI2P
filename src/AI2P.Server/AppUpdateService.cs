using AI2P.Core;
using AI2P.Core.Api;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Serilog;

namespace AI2P.Server;

/// <summary>
/// АВТООБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208).
///
/// Три шага, и каждый умеет отказать внятно: УЗНАТЬ (список выпусков репозитория, отбор
/// файла по системе, архитектуре и способу установки — правила <see cref="AppUpdate"/>),
/// СКАЧАТЬ (во временный каталог) и ПОСТАВИТЬ.
///
/// ПОЧЕМУ ОБНОВЛЕНИЕ СТАВИТ НЕ САМО ПРИЛОЖЕНИЕ, А ОТДЕЛЬНЫЙ СКРИПТ: файлы установки держит
/// работающий процесс — тот самый, который эти файлы и заменяет. Поэтому приложение пишет
/// рядом с пакетом маленький скрипт, запускает его ОТДЕЛЬНО от себя и заканчивает работу:
/// скрипт дожидается конца нашего процесса, ставит пакет молча и поднимает сервер обратно
/// (консоль — запуском программы, служба — <c>net start</c> / <c>systemctl restart</c>).
///
/// Пакет установки Windows (Inno Setup, T-285) службу останавливает и запускает сам —
/// но только когда успевает: скрипт всё равно ждёт конца процесса, иначе установка
/// столкнулась бы с занятыми файлами консольного запуска.
/// </summary>
public sealed class AppUpdateService
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<AppUpdateService>();

    private static readonly HttpClient Http = NewClient();

    private readonly Func<Ai2pConfig> _config;
    private readonly Action _stopApp;
    private readonly string _appDir;

    /// <param name="config">Настройки этого компьютера (правятся на ходу — отсюда делегат).</param>
    /// <param name="stopApp">Закончить работу приложения (перезапуск ведёт скрипт обновления).</param>
    /// <param name="appDir">Каталог программы; пусто — каталог этой сборки.</param>
    public AppUpdateService(Func<Ai2pConfig> config, Action stopApp, string? appDir = null)
    {
        _config = config;
        _stopApp = stopApp;
        _appDir = appDir is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;
    }

    private static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // GitHub без User-Agent отвечает 403 — это его требование, а не вежливость
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Последний ответ проверки; null — в этот запуск ещё не проверяли.</summary>
    public UpdateCheckDto? Last { get; private set; }

    /// <summary>
    /// ПРОВЕРКА: есть ли в репозитории выпуск новее нашего билда С ФАЙЛОМ ДЛЯ ЭТОЙ УСТАНОВКИ.
    /// Сеть и чужой сервер отказывают часто, поэтому отказ — не исключение, а поле Error.
    /// </summary>
    public async Task<UpdateCheckDto> CheckAsync(CancellationToken ct = default)
    {
        var (full, runtime) = AppUpdate.InstallKind(_appDir);
        var os = AppUpdate.OsName();
        var arch = AppUpdate.ArchOf(runtime);
        var result = new UpdateCheckDto
        {
            CurrentVersion = AppInfo.Version,
            CheckedAt = DateTime.UtcNow,
            Full = full,
            Platform = os + "/" + arch + (full ? " full" : ""),
        };
        var api = AppUpdate.ReleasesApiUrl(_config().Update.RepoUrl());
        if (api.Length == 0)
        {
            result.Error = Loc.T("msg.update.1", _config().Update.RepoUrl());
            Last = result;
            return result;
        }
        try
        {
            var json = await Http.GetStringAsync(api, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                result.Error = Loc.T("msg.update.2");
                Last = result;
                return result;
            }
            var best = 0;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }
                if (!release.TryGetProperty("assets", out var assets)
                    || assets.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!AppUpdate.Matches(name, full, os, arch))
                    {
                        continue;
                    }
                    // номер берётся из ИМЕНИ ФАЙЛА, а тэг — только запасной вариант:
                    // имя складывает скрипт сборки по правилу, тэг ставит человек рукой
                    var build = AppUpdate.BuildOfAsset(name);
                    if (build == 0 && release.TryGetProperty("tag_name", out var tag))
                    {
                        build = AppUpdate.BuildOfTag(tag.GetString() ?? "");
                    }
                    if (build <= AppInfo.Build || build <= best)
                    {
                        continue;
                    }
                    best = build;
                    result.Available = true;
                    result.Version = AppUpdate.VersionOf(build);
                    result.AssetName = name;
                    result.AssetUrl = asset.TryGetProperty("browser_download_url", out var url)
                        ? url.GetString() ?? "" : "";
                    result.ReleaseUrl = release.TryGetProperty("html_url", out var page)
                        ? page.GetString() ?? "" : "";
                }
            }
            if (result.Available && result.AssetUrl.Length == 0)
            {
                result.Available = false;
                result.Error = Loc.T("msg.update.3", result.AssetName);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or JsonException or IOException)
        {
            result.Error = ex.Message;
            Logger.Warning("Проверка обновлений не удалась: {Error}", ex.Message);
        }
        Last = result;
        return result;
    }

    /// <summary>
    /// ОБНОВИТЬСЯ: скачать пакет, положить рядом скрипт установки, запустить его отдельно
    /// от себя и закончить работу приложения. Проверку вызывающий делает сам —
    /// <paramref name="found"/> это её ответ.
    /// </summary>
    public async Task<UpdateStartDto> StartAsync(UpdateCheckDto found, CancellationToken ct = default)
    {
        if (!found.Available || found.AssetUrl.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.update.4"));
        }
        var dir = Path.Combine(Path.GetTempPath(), "ai2p_update");
        Directory.CreateDirectory(dir);
        var package = Path.Combine(dir, found.AssetName);
        Logger.Information("Обновление {Version}: качаю {Asset}", found.Version, found.AssetName);
        await using (var source = await Http.GetStreamAsync(found.AssetUrl, ct))
        await using (var target = File.Create(package))
        {
            await source.CopyToAsync(target, ct);
        }
        var script = WriteScript(dir, package);
        Logger.Information("Обновление {Version}: пакет {File}, скрипт {Script} — перезапуск",
            found.Version, package, script);
        Launch(script);
        var result = new UpdateStartDto
        {
            AssetName = found.AssetName,
            Version = found.Version,
            Restarts = true,
        };
        // приложение заканчивает работу НЕ СРАЗУ: ответ обязан доехать до браузера,
        // иначе человек видит оборванный запрос вместо «обновляюсь, зайдите через минуту»
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            _stopApp();
        }, CancellationToken.None);
        return result;
    }

    /// <summary>Каталог данных сервера — там лежит запись о начатом обновлении (T-756-S0).
    /// Пусто — временный каталог обновления.</summary>
    public string StateDir { get; init; } = "";

    /// <summary>Запустить скрипт обновления; подменяется проверками (они не должны ставить
    /// и останавливать ничего).</summary>
    public Action<string> LaunchScript { get; init; } = Launch;

    /// <summary>Пауза перед остановкой: ответ инструмента должен успеть доехать до агента.</summary>
    public TimeSpan StopDelay { get; init; } = TimeSpan.FromSeconds(3);

    private string PendingPath => Path.Combine(
        StateDir.Length > 0 ? StateDir : Path.Combine(Path.GetTempPath(), "ai2p_update"),
        AppUpdate.PendingFile);

    /// <summary>
    /// ОБНОВИТЬСЯ ИЗ ЛОКАЛЬНОГО ПАКЕТА (T-756-S0): файл уже лежит на диске (публикация собрала
    /// его сама), скачивать нечего. Проверяется, что пакет годится ЭТОЙ установке (вид, система,
    /// архитектура) и что билд не ниже текущего; дальше тот же путь, что у обновления с GitHub:
    /// скрипт рядом, установка молча, подъём сервера.
    /// <para>ГЛАВНОЕ — <paramref name="closeJob"/>: задание, позвавшее обновление, закрывается
    /// ДО остановки сервера, в своём «статусе при завершении». Иначе после подъёма оно числилось
    /// бы running и сторож подал бы его заново — агент снова позвал бы обновление, и так по кругу.
    /// Пока идёт перезапуск, новых заданий оркестратор не запускает (<see cref="AppUpdate.Restarting"/>).</para>
    /// </summary>
    /// <param name="package">Полный путь к пакету (разрешённость пути проверил вызывающий).</param>
    /// <param name="pending">Кто позвал обновление: организация, задача, задание, исполнитель.</param>
    /// <param name="closeJob">Закрыть задание с этим текстом результата; null — закрывать нечего.</param>
    public UpdateStartDto StartFromFile(string package, AppUpdate.Pending pending, Action<string>? closeJob)
    {
        if (!File.Exists(package))
        {
            throw new InvalidOperationException(Loc.T("msg.update.12", package));
        }
        var (full, runtime) = AppUpdate.InstallKind(_appDir);
        var os = AppUpdate.OsName();
        var arch = AppUpdate.ArchOf(runtime);
        var (build, error) = AppUpdate.CheckLocal(package, full, os, arch, AppInfo.Build);
        if (error.Length > 0)
        {
            throw new InvalidOperationException(Loc.T(error, Path.GetFileName(package),
                os + "/" + arch + (full ? " full" : ""), AppInfo.Version));
        }
        var version = AppUpdate.VersionOf(build);
        var dir = Path.Combine(Path.GetTempPath(), "ai2p_update");
        Directory.CreateDirectory(dir);
        pending.FromVersion = AppInfo.Version;
        pending.FromBuild = AppInfo.Build;
        pending.ToBuild = build;
        pending.Package = package;
        pending.LogFile = Path.Combine(dir, "install.log");
        pending.StartedAt = DateTime.UtcNow;
        AppUpdate.Restarting = true;
        try
        {
            closeJob?.Invoke(Loc.T("msg.update.13", AppInfo.Version, version, Path.GetFileName(package)));
            Directory.CreateDirectory(Path.GetDirectoryName(PendingPath)!);
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(pending), new UTF8Encoding(false));
            var script = WriteScript(dir, package);
            Logger.Information("Обновление {From} → {Version} из файла {File}, скрипт {Script} — перезапуск",
                AppInfo.Version, version, package, script);
            LaunchScript(script);
        }
        catch
        {
            AppUpdate.Restarting = false;
            throw;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(StopDelay, CancellationToken.None);
            _stopApp();
        }, CancellationToken.None);
        return new UpdateStartDto
        {
            AssetName = Path.GetFileName(package),
            Version = version,
            Restarts = true,
        };
    }

    /// <summary>
    /// Запись о начатом обновлении, оставшаяся от прошлого запуска (T-756-S0); файл при этом
    /// удаляется — сообщение «было X, стало Y» уходит ровно один раз. null — записи нет.
    /// </summary>
    public AppUpdate.Pending? TakePending()
    {
        var path = PendingPath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var pending = JsonSerializer.Deserialize<AppUpdate.Pending>(File.ReadAllText(path));
            File.Delete(path);
            return pending;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Warning("Запись о начатом обновлении не прочитана: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>Текст для чата задачи после подъёма: «было X, стало Y» либо ошибка.</summary>
    public static string PendingOutcome(AppUpdate.Pending pending, int currentBuild) =>
        pending.Succeeded(currentBuild)
            ? Loc.T("msg.update.14", pending.FromVersion, AppUpdate.VersionOf(currentBuild))
            : Loc.T("msg.update.15", pending.FromVersion, AppUpdate.VersionOf(pending.ToBuild),
                AppUpdate.VersionOf(currentBuild), pending.LogFile);

    /// <summary>
    /// ПРОВЕРИТЬ (и, если велено, ПОСТАВИТЬ) — один вызов для автоматических путей:
    /// старт консольного запуска и срабатывание расписания у службы. Отвечает строкой
    /// для журнала: исключений автоматический путь бросать не должен.
    /// </summary>
    public string RunAuto(bool install)
    {
        try
        {
            var found = CheckAsync().GetAwaiter().GetResult();
            if (found.Error.Length > 0)
            {
                return Loc.T("msg.update.5", found.Error);
            }
            if (!found.Available)
            {
                return Loc.T("msg.update.6", AppInfo.Version);
            }
            if (!install)
            {
                return Loc.T("msg.update.7", found.Version);
            }
            StartAsync(found).GetAwaiter().GetResult();
            return Loc.T("msg.update.8", found.Version);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Автообновление не удалось: {Error}", ex.Message);
            return Loc.T("msg.update.5", ex.Message);
        }
    }

    /// <summary>
    /// Скрипт обновления: ждёт конца НАШЕГО процесса, ставит пакет молча и поднимает сервер
    /// обратно. Кириллицы в нём нет вовсе — .cmd читается консолью в OEM-кодировке
    /// (правило проекта), а разбирать потом «кракозябры» в чужом логе некому.
    /// </summary>
    private string WriteScript(string dir, string package)
    {
        var pid = Environment.ProcessId;
        var service = ServiceRun.IsService;
        if (OperatingSystem.IsWindows())
        {
            var exe = Path.Combine(_appDir, "AI2P.Server.exe");
            var script = Path.Combine(dir, "ai2p_update.cmd");
            var text = new StringBuilder();
            text.AppendLine("@echo off");
            text.AppendLine("rem AI2P self-update (T-208). Generated file, safe to delete.");
            text.AppendLine(":wait");
            text.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
            text.AppendLine("if not errorlevel 1 (");
            text.AppendLine("  ping -n 2 127.0.0.1 >nul");
            text.AppendLine("  goto wait");
            text.AppendLine(")");
            text.AppendLine($"\"{package}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL " +
                            $"/DIR=\"{_appDir.TrimEnd('\\')}\" /LOG=\"{Path.Combine(dir, "install.log")}\"");
            text.AppendLine(service
                ? $"net start {ServiceRun.ServiceName}"
                : $"start \"\" \"{exe}\"");
            File.WriteAllText(script, text.ToString(), new UTF8Encoding(false));
            return script;
        }
        var run = Path.Combine(dir, "ai2p_update.sh");
        var posix = new StringBuilder();
        posix.AppendLine("#!/bin/sh");
        posix.AppendLine("# AI2P self-update (T-208). Generated file, safe to delete.");
        posix.AppendLine($"while kill -0 {pid} 2>/dev/null; do sleep 1; done");
        posix.AppendLine($"chmod +x \"{package}\" 2>/dev/null");
        if (service)
        {
            posix.AppendLine($"systemctl stop {ServiceRun.ServiceName}.service 2>/dev/null");
        }
        posix.AppendLine($"\"{package}\" -- \"{_appDir}\" --force --no-runtime-check " +
                         $">\"{Path.Combine(dir, "install.log")}\" 2>&1");
        posix.AppendLine(service
            ? $"systemctl start {ServiceRun.ServiceName}.service 2>/dev/null"
            : $"nohup \"{Path.Combine(_appDir, "AI2P.Server")}\" >/dev/null 2>&1 &");
        File.WriteAllText(run, posix.ToString(), new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(run, UnixFileMode.UserRead | UnixFileMode.UserWrite
                                      | UnixFileMode.UserExecute);
        }
        return run;
    }

    /// <summary>Запустить скрипт ОТДЕЛЬНО от себя: он обязан пережить конец нашего процесса.</summary>
    private static void Launch(string script)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c start \"AI2P update\" /min \"{script}\"")
            : new ProcessStartInfo("/bin/sh", $"\"{script}\"");
        info.UseShellExecute = OperatingSystem.IsWindows();
        info.CreateNoWindow = true;
        info.WorkingDirectory = Path.GetDirectoryName(script)!;
        Process.Start(info);
    }
}
