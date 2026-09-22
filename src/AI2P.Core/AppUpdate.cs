using System.Runtime.InteropServices;
using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// ПРАВИЛА ИМЕНОВАНИЯ ВЫПУСКА И РАЗБОР ВЕРСИЙ (T-208, автообновление).
///
/// Всё, что нужно знать, чтобы УЗНАТЬ СВОЙ файл среди файлов выпуска на GitHub, собрано
/// здесь и НЕ зависит ни от сети, ни от config.json: имя пакета складывают скрипты
/// <c>MakePackage.ps1</c>/<c>MakePackage.sh</c> (правило имени — T-234-S0), а разбирать его
/// обратно приходится приложению. Правило одно и то же, поэтому и живёт оно в одном месте.
///
/// Имя пакета: <c>AI2P_v_1_133_full_windows_x64.exe</c> — «full» стоит ПОСЛЕ номера версии
/// и есть только у полной выкладки (с рантаймом внутри), дальше система и архитектура.
/// Расширение: Windows — <c>.exe</c> (Inno Setup), Linux и macOS — <c>.run</c> (makeself).
///
/// Тэг выпуска специально разбирается ШИРОКО (<c>v1.133</c>, <c>1.133</c>, <c>v_1_133</c>):
/// имя файла — договорённость скриптов, а тэг человек ставит рукой, и жёсткое правило здесь
/// стоило бы «обновлений нет» на ровном месте. Настоящий ответ всё равно даёт ИМЯ ФАЙЛА.
/// </summary>
public static class AppUpdate
{
    /// <summary>Где лежат выпуски по умолчанию (адрес из задания T-208).</summary>
    public const string DefaultRepoUrl = "https://github.com/mmf007/ai2p";

    /// <summary>Время суточной проверки по умолчанию — 2:00 местного времени (T-208).</summary>
    public const string DefaultTime = "02:00";

    /// <summary>Начало имени любого пакета установки (T-234-S0).</summary>
    public const string NamePrefix = "AI2P_v_";

    /// <summary>Система для имени пакета: windows / linux / macos.</summary>
    public static string OsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }
        return OperatingSystem.IsMacOS() ? "macos" : "linux";
    }

    /// <summary>Архитектура для имени пакета так, как её называет .NET: x64, arm64, x86.</summary>
    public static string ArchName() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    /// <summary>Расширение пакета установки: Windows — exe (Inno Setup), остальные — run (makeself).</summary>
    public static string ExtensionOf(string os) =>
        string.Equals(os, "windows", StringComparison.OrdinalIgnoreCase) ? "exe" : "run";

    /// <summary>Имя пакета установки по правилу T-234-S0.</summary>
    public static string PackageName(string version, bool full, string os, string arch) =>
        NamePrefix + version.Replace('.', '_') + (full ? "_full" : "")
        + "_" + os + "_" + arch + "." + ExtensionOf(os);

    /// <summary>
    /// Это ли НАШ файл: та же система, та же архитектура и тот же способ установки
    /// (полная выкладка или обычная). Номер версии здесь не проверяется — он берётся
    /// из имени отдельно (<see cref="BuildOfAsset"/>).
    /// </summary>
    public static bool Matches(string assetName, bool full, string os, string arch)
    {
        var name = (assetName ?? "").Trim();
        if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!name.EndsWith("." + ExtensionOf(os), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var lower = name.ToLowerInvariant();
        // «_full_» ищется ИМЕННО с подчёркиваниями по краям: иначе «full» нашлось бы
        // в любом будущем имени, где это слово оказалось частью другого
        var isFull = lower.Contains("_full_");
        return isFull == full
               && lower.Contains("_" + os.ToLowerInvariant() + "_")
               && lower.Contains("_" + arch.ToLowerInvariant() + ".");
    }

    /// <summary>
    /// Номер билда из ИМЕНИ ФАЙЛА выпуска: <c>AI2P_v_1_133_windows_x64.exe</c> → 133.
    /// 0 — имя не наше либо номер не разобран.
    /// </summary>
    public static int BuildOfAsset(string assetName)
    {
        var name = (assetName ?? "").Trim();
        if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        var rest = name[NamePrefix.Length..].Split('_');
        // «1_133_...» — первая часть major, вторая и есть билд
        return rest.Length >= 2 && int.TryParse(rest[1], out var build) ? build : 0;
    }

    /// <summary>
    /// Номер билда из ТЭГА выпуска: <c>v1.133</c>, <c>1.133</c>, <c>v_1_133</c>, <c>release-1.133</c>
    /// — всё это 133. 0 — номера в тэге нет.
    /// </summary>
    public static int BuildOfTag(string tag)
    {
        var digits = new List<int>();
        var current = "";
        foreach (var ch in (tag ?? "").Trim() + " ")
        {
            if (char.IsDigit(ch))
            {
                current += ch;
                continue;
            }
            if (current.Length > 0 && int.TryParse(current, out var value))
            {
                digits.Add(value);
            }
            current = "";
        }
        // «1.133» — второе число и есть билд; одинокое число считаем билдом
        return digits.Count switch
        {
            0 => 0,
            1 => digits[0],
            _ => digits[1],
        };
    }

    /// <summary>Версия приложения по номеру билда: 133 → «1.133».</summary>
    public static string VersionOf(int build) => "1." + build;

    /// <summary>
    /// Адрес API выпусков GitHub по адресу репозитория. Хвост вида <c>/releases</c>,
    /// <c>/release</c> и <c>.git</c> отбрасывается — человек копирует адрес страницы,
    /// а не «правильный» адрес репозитория. Пусто — адрес разобрать не удалось.
    /// </summary>
    public static string ReleasesApiUrl(string repoUrl)
    {
        var url = (repoUrl ?? "").Trim();
        if (url.Length == 0)
        {
            return "";
        }
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            url = url[(scheme + 3)..];
        }
        var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // <хост>/<владелец>/<репозиторий>[/...]
        if (parts.Length < 3)
        {
            return "";
        }
        var owner = parts[1];
        var repo = parts[2];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }
        return $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=30";
    }

    /// <summary>Страница выпусков для человека (кнопка «открыть страницу выпусков»).</summary>
    public static string ReleasesPageUrl(string repoUrl)
    {
        var url = (repoUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            return "";
        }
        if (url.EndsWith("/releases", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        if (url.EndsWith("/release", StringComparison.OrdinalIgnoreCase))
        {
            return url + "s";
        }
        return url + "/releases";
    }

    /// <summary>
    /// СПОСОБ УСТАНОВКИ ЭТОЙ КОПИИ приложения: полная выкладка (рантайм внутри) или обычная.
    /// Читается из <c>version.json</c> выкладки — его кладёт <c>buildRelease</c> и переносит
    /// установка. Файла нет (запуск из исходников) — считаем обычной выкладкой, и это
    /// безопасное умолчание: полный пакет ставится поверх обычного, обратное тоже верно,
    /// но подсовывать человеку лишние 80 МБ рантайма без нужды не стоит.
    /// </summary>
    /// <param name="appDir">Каталог программы (<c>AppContext.BaseDirectory</c>).</param>
    public static (bool Full, string Runtime) InstallKind(string appDir)
    {
        try
        {
            var file = Path.Combine(appDir, "version.json");
            if (!File.Exists(file))
            {
                return (false, "");
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var full = root.TryGetProperty("selfContained", out var sc)
                       && sc.ValueKind == JsonValueKind.True;
            var runtime = root.TryGetProperty("runtime", out var rt) ? rt.GetString() ?? "" : "";
            return (full, runtime);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (false, "");
        }
    }

    /// <summary>
    /// Архитектура этой копии: у полной выкладки она записана рантаймом (<c>win-x64</c> →
    /// <c>x64</c>), у обычной рантайма нет — тогда берётся архитектура самой системы.
    /// </summary>
    public static string ArchOf(string runtime)
    {
        var rid = (runtime ?? "").Trim();
        var dash = rid.LastIndexOf('-');
        return dash > 0 && dash < rid.Length - 1 ? rid[(dash + 1)..].ToLowerInvariant() : ArchName();
    }

    /// <summary>
    /// Время суточной проверки «ЧЧ:ММ» → период расписания (T-208): «каждый день в это время».
    /// Ежедневного вида у расписания нет вовсе — ежедневное это недельное со всеми семью днями.
    /// </summary>
    public static string DailyPeriodJson(string time) =>
        "{\"type\":\"weekly\",\"days\":[1,2,3,4,5,6,7],\"time\":\"" + NormalizeTime(time) + "\"}";

    /// <summary>Время «ЧЧ:ММ»; мусор и пустое — <see cref="DefaultTime"/>.</summary>
    public static string NormalizeTime(string? time)
    {
        var value = (time ?? "").Trim();
        return TimeSpan.TryParse(value, out var parsed)
               && parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1)
            ? $"{parsed.Hours:00}:{parsed.Minutes:00}"
            : DefaultTime;
    }
}
