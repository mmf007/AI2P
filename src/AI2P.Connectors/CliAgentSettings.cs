using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Connectors;

/// <summary>
/// ЛОКАЛЬНЫЙ ФАЙЛ НАСТРОЕК CLI-АГЕНТА В ПАПКЕ ПРОЕКТА (T-423-S0). Клиент <c>ai2p</c> обязан
/// быть агенту доступен ВСЕГДА: что агенту можно, а что нет, решают уже правила безопасности
/// AI2P внутри клиента. Флаг запуска (<c>--allowedTools</c>) это даёт только заданию, а сессия,
/// которую человек открывает в той же папке руками, или агент другого вида его не получают —
/// поэтому разрешение пишется и в файл настроек проекта.
///
/// <para>УНИВЕРСАЛЬНО ПО ВИДУ АГЕНТА: у каждого CLI свой файл и свой формат, и выбирается
/// писатель по имени исполняемого файла (<c>claude</c>, <c>codex</c>, …). Нет писателя —
/// файл не трогается вовсе (лучше ничего, чем запись в чужом формате).</para>
///
/// <para>Каталоги, открытые правилами AI2P, пишутся в файл только тем агентам, которым их
/// нечем передать при запуске: Claude Code получает их флагами <c>--add-dir</c>, и запись
/// в файл ему не нужна — противоречия нет, делать ничего не надо.</para>
///
/// <para>Файл только ДОПОЛНЯЕТСЯ: чужие строки (их пишет человек или сам агент) не
/// удаляются и не переставляются; повторный вызов ничего не меняет.</para>
/// </summary>
public static class CliAgentSettings
{
    /// <summary>Писатель настроек одного вида CLI-агента.</summary>
    public interface IWriter
    {
        /// <summary>Подходит ли писатель исполняемому файлу агента (имя без пути и расширения).</summary>
        bool Handles(string executable);

        /// <summary>Дописать разрешения; вернуть true, если файл изменён.</summary>
        bool Ensure(string projectDir, string wrapperPath, IReadOnlyList<string> allowedDirs);
    }

    private static readonly IWriter[] Writers = [new ClaudeCode()];

    /// <summary>Писатель для команды запуска агента; null — вид агента нам не знаком.</summary>
    public static IWriter? For(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable.Trim().Trim('"'));
        return Writers.FirstOrDefault(w => w.Handles(name));
    }

    /// <summary>
    /// Привести настройки агента в папке проекта к правилам AI2P. Ошибка записи (файл занят,
    /// испорчен JSON) работу задания не роняет: агенту остаются флаги запуска.
    /// </summary>
    /// <returns>Текст ошибки для журнала; null — всё в порядке или делать нечего.</returns>
    public static string? Ensure(string executable, string? projectDir, string? wrapperPath,
        IReadOnlyList<string> allowedDirs)
    {
        if (projectDir is not { Length: > 0 } || wrapperPath is not { Length: > 0 }
            || !Directory.Exists(projectDir) || For(executable) is not { } writer)
        {
            return null;
        }
        try
        {
            writer.Ensure(projectDir, wrapperPath, allowedDirs);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    /// <summary>Добавить в массив недостающие строки; true — что-то добавлено.</summary>
    internal static bool AddMissing(JsonObject parent, string key, IEnumerable<string> values)
    {
        if (parent[key] is not JsonArray array)
        {
            array = [];
            parent[key] = array;
        }
        var present = array.Select(n => n?.GetValue<string>() ?? "").ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var value in values)
        {
            if (present.Add(value))
            {
                array.Add(value);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Claude Code: <c>.claude/settings.local.json</c> проекта, массив <c>permissions.allow</c>
    /// (Bash и, на Windows, PowerShell — <see cref="AgentCli.AllowedToolPatternsAllShells"/>).
    /// </summary>
    private sealed class ClaudeCode : IWriter
    {
        public bool Handles(string executable) =>
            executable.Equals("claude", StringComparison.OrdinalIgnoreCase);

        public bool Ensure(string projectDir, string wrapperPath, IReadOnlyList<string> allowedDirs)
        {
            var path = Path.Combine(projectDir, ".claude", "settings.local.json");
            var root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? []
                : [];
            if (root["permissions"] is not JsonObject permissions)
            {
                permissions = [];
                root["permissions"] = permissions;
            }
            if (!AddMissing(permissions, "allow", AgentCli.AllowedToolPatternsAllShells(wrapperPath)))
            {
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            return true;
        }
    }
}
