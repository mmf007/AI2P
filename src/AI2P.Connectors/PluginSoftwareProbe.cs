using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ЕСТЬ ЛИ НА ЭТОМ СЕРВЕРЕ ПРОГРАММА, К КОТОРОЙ ВЕДЁТ ПЛАГИН (T-112-S0) — melt, ffmpeg,
/// blender, kdenlive.
///
/// Механизм не новый: это тот же блок <c>"system"</c> справочника пакетов (T-4-S0) —
/// команда в PATH, ключ версии, допустимый диапазон, — и вся его механика переиспользуется
/// как есть (<see cref="SystemPackageProbe"/>): файлы нулевой длины пропускаются (это
/// псевдонимы магазина Windows, запуск которых открывает окно магазина), версия
/// разбирается запуском. ПРОВЕРКА ВЕРСИИ ОБЯЗАТЕЛЬНА: у ffmpeg набор ключей заметно менялся
/// между 4.x и 7.x, и «команда нашлась» ещё не значит «эта команда нам годится».
///
/// ЧЕГО В T-4-S0 НЕ БЫЛО И ЧТО ДОБАВЛЕНО ЗДЕСЬ — РУЧНОЙ ПУТЬ: «софт можно не устанавливать,
/// но тогда указать, где он уже стоит». Так живёт DaVinci Resolve: дистрибутив на 3–4 ГБ
/// отдаётся за формой регистрации, постоянной прямой ссылки нет, поставить его за человека
/// мы не можем. Ручной путь хранится в config.json СЕРВЕРА и не реплицируется никогда
/// (9fee2887): <c>C:\Program Files\Blackmagic Design\…</c> соседу-линуксоиду не годится, а
/// реплицированный чужой путь «уже не уходит».
///
/// Ответ живёт минуту, как у справочника пакетов: форма плагина спрашивает его часто, а
/// стоит он запуска программы.
/// </summary>
public static class PluginSoftwareProbe
{
    /// <summary>Сколько живёт ответ проверки (как у <c>ModelInstallService</c>).</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private static readonly Dictionary<string, (DateTime When, Result Value)> Cache = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    /// <summary>Итог поиска: состояние для человека, найденный путь и версия.</summary>
    public sealed record Result(PluginSoftwareStatus Status, string Path = "", string Version = "")
    {
        /// <summary>Действия плагина на этом сервере публиковать можно.</summary>
        public bool Ready => Status is PluginSoftwareStatus.Found or PluginSoftwareStatus.NotNeeded;
    }

    /// <summary>Программа плагину не нужна вовсе (блока <c>software</c> в манифесте нет).</summary>
    public static readonly Result NotNeeded = new(PluginSoftwareStatus.NotNeeded);

    /// <summary>
    /// Найти программу плагина на этом сервере.
    /// </summary>
    /// <param name="software">Блок <c>software</c> манифеста; null — программа не нужна.</param>
    /// <param name="manualPath">Ручной путь из config.json сервера: файл программы либо
    /// каталог, в котором она лежит. Пусто — искать только в PATH.</param>
    /// <param name="searchPath">Список каталогов через разделитель PATH; null — PATH процесса.</param>
    public static Result Find(PluginSoftware? software, string? manualPath = null,
        string? searchPath = null)
    {
        if (software is null)
        {
            return NotNeeded;
        }
        var key = Key(software, manualPath, searchPath);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.When < Ttl)
            {
                return cached.Value;
            }
        }
        var result = Probe(software, manualPath, searchPath);
        lock (Gate)
        {
            Cache[key] = (DateTime.UtcNow, result);
        }
        return result;
    }

    /// <summary>Забыть накопленные ответы (человек поставил программу и жмёт «Проверить»).</summary>
    public static void Forget()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }

    private static Result Probe(PluginSoftware software, string? manualPath, string? searchPath)
    {
        var probe = software.System;
        if (probe is null)
        {
            // искать нечем: годится ровно то, что человек показал руками
            var manual = ManualFile(manualPath, []);
            return manual is null
                ? new Result(NotFound(software))
                : new Result(PluginSoftwareStatus.Found, manual);
        }
        var system = new ModelPackage.SystemProbe
        {
            Commands = probe.Commands,
            VersionArgs = probe.VersionArgs,
            MinVersion = probe.MinVersion,
            MaxVersion = probe.MaxVersion,
        };
        // РУЧНОЙ ПУТЬ ПЕРВЫМ: человек указал его именно потому, что в PATH программы нет
        // (или в PATH лежит не та). Версия проверяется и у него — «показал руками» не значит
        // «годится»: указанный ffmpeg 4.x сорвал бы работу так же, как найденный в PATH
        var manualFile = ManualFile(manualPath, probe.Commands);
        if (manualFile is not null)
        {
            var version = Version(manualFile, system);
            if (version is not null)
            {
                return new Result(PluginSoftwareStatus.Found, manualFile, version);
            }
        }
        var found = SystemPackageProbe.Find(system, searchPath ?? Path());
        return found is null
            ? new Result(NotFound(software))
            : new Result(PluginSoftwareStatus.Found, found, Version(found, system) ?? "");
    }

    /// <summary>Версия найденной программы; null — версия не годится по диапазону манифеста.</summary>
    private static string? Version(string file, ModelPackage.SystemProbe probe)
    {
        if (probe.VersionArgs.Trim().Length == 0)
        {
            return "";  // версию не спрашивали — годится найденное
        }
        var version = SystemPackageProbe.VersionOf(file, probe.VersionArgs);
        return SystemPackageProbe.Accepts(version, probe.MinVersion, probe.MaxVersion) ? version : null;
    }

    /// <summary>
    /// Программа по ручному пути: путь бывает и файлом программы, и каталогом, куда её
    /// поставили (человек чаще показывает каталог — «вот сюда я поставил Resolve»).
    /// null — по этому пути программы нет.
    /// </summary>
    private static string? ManualFile(string? manualPath, IReadOnlyList<string> commands)
    {
        var path = (manualPath ?? "").Trim().Trim('"');
        if (path.Length == 0)
        {
            return null;
        }
        if (File.Exists(path))
        {
            return path;
        }
        return Directory.Exists(path) && commands.Count > 0
            ? SystemPackageProbe.Candidates(commands, path).FirstOrDefault()
            : null;
    }

    /// <summary>Не нашли: поставить можем (у плагина назван пакет) либо не можем вовсе.</summary>
    private static PluginSoftwareStatus NotFound(PluginSoftware software) =>
        software.Package.Trim().Length > 0
            ? PluginSoftwareStatus.CanInstall
            : PluginSoftwareStatus.NeedsManualPath;

    private static string Path() => Environment.GetEnvironmentVariable("PATH") ?? "";

    private static string Key(PluginSoftware software, string? manualPath, string? searchPath) =>
        string.Join('\u0001',
            software.Package,
            string.Join(',', software.System?.Commands ?? []),
            software.System?.VersionArgs ?? "",
            software.System?.MinVersion ?? "",
            software.System?.MaxVersion ?? "",
            manualPath ?? "",
            searchPath ?? "");
}
