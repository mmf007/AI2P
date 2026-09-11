using System.Text.RegularExpressions;
using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Connectors;

/// <summary>Решение правил безопасности для конкретного действия (ТЗ гл. 12, todo25).</summary>
public enum SecurityDecision
{
    Allow,
    /// <summary>Переспросить: подтверждение человеком до выполнения.</summary>
    Confirm,
    Deny,
}

/// <summary>
/// Оценка эффективного набора правил безопасности (ТЗ гл. 12, todo25) — ядро проверяет
/// ДО выполнения действия (п. 12.3). Набор уже сложен по уровням (SecurityRuleService.
/// EffectiveForTask). Паттерны: вилдкард (* и ?) без учёта регистра; с префиксом «re:» —
/// регулярное выражение. Если подходит несколько разных правил — побеждает самое строгое
/// (запрещено &gt; переспросить &gt; разрешено).
/// Значения по умолчанию (нет подходящих правил): действия AI2P — разрешены; каталог
/// проекта — полный доступ, вне каталога проекта — запрещено (todo25); команды OS —
/// переспросить (задел: инструментов OS пока нет).
/// </summary>
public sealed class SecurityEvaluator
{
    private readonly IReadOnlyList<SecurityRule> _rules;
    private readonly string? _projectRoot;

    /// <param name="rules">Эффективный набор (SecurityRuleService.EffectiveForTask).</param>
    /// <param name="projectRoot">Абсолютный путь папки проекта; null — папка не задана.</param>
    public SecurityEvaluator(IReadOnlyList<SecurityRule> rules, string? projectRoot)
    {
        _rules = rules;
        // «~» в пути раскрывается везде, где путь приходит от человека (T-135)
        _projectRoot = projectRoot is null ? null : Path.GetFullPath(AI2P.Core.PathHome.Expand(projectRoot));
    }

    /// <summary>Решение для действия AI2P по его коду (AI2P.Files.Write, …).</summary>
    public (SecurityDecision Decision, SecurityRule? Rule) ForAction(string actionCode) =>
        Strictest(_rules.Where(r =>
                r.Target == SecurityTarget.Action && PatternMatches(r.Pattern, actionCode)),
            SecurityDecision.Allow);

    /// <summary>Решение для доступа к пути (op: read / write / delete).
    /// По умолчанию: внутри папки проекта — разрешено, вне — запрещено (todo25).</summary>
    public (SecurityDecision Decision, SecurityRule? Rule) ForPath(string absPath, string op)
    {
        absPath = Path.GetFullPath(absPath);
        var matched = _rules.Where(r =>
            r.Target == SecurityTarget.Directory
            && CoversOp(r, op)
            && PatternMatches(ResolvePattern(r), Normalize(absPath), isPath: true));
        var fallback = IsInsideProject(absPath) ? SecurityDecision.Allow : SecurityDecision.Deny;
        return Strictest(matched, fallback);
    }

    /// <summary>
    /// РЕШЕНИЕ ПО ПЛАГИНУ (T-155-S0): <paramref name="op"/> — <see cref="PluginUse"/> (агент
    /// зовёт инструмент плагина) либо <see cref="PluginRun"/> (задача «авто ПО» запускает его
    /// программу). Паттерн правила сравнивается и с кодом плагина, и с парой
    /// «код:операция» — правило <c>trainer.musubi</c> закрывает плагин целиком, а
    /// <c>trainer.musubi:lora.train</c> — одну его операцию.
    /// <para>ПО УМОЛЧАНИЮ РАЗРЕШЕНО, как у действий AI2P: отсутствие правила запрета — это
    /// разрешение (требование заказчика к T-155-S0). Обратная политика T-112-S0 («нет записи
    /// справочника — запрещено») этим не отменяется: она про другое — про инструмент, которого
    /// человек не разрешал вовсе.</para>
    /// </summary>
    /// <param name="pluginCode">Код плагина (<c>tool.ffmpeg</c>).</param>
    /// <param name="op">Операция правила: <see cref="PluginUse"/> / <see cref="PluginRun"/>.</param>
    /// <param name="opName">Имя операции плагина (инструмент действия); пусто — правило
    /// сравнивается только с кодом плагина.</param>
    public (SecurityDecision Decision, SecurityRule? Rule) ForPlugin(string pluginCode, string op,
        string opName = "")
    {
        if (string.IsNullOrWhiteSpace(pluginCode))
        {
            return (SecurityDecision.Allow, null);
        }
        var full = opName.Length > 0 ? pluginCode + ":" + opName : "";
        return Strictest(_rules.Where(r =>
                r.Target == SecurityTarget.Plugin
                && CoversOp(r, op)
                && (PatternMatches(r.Pattern, pluginCode)
                    || (full.Length > 0 && PatternMatches(r.Pattern, full)))),
            SecurityDecision.Allow);
    }

    /// <summary>Операция правила плагина: агент ЗОВЁТ его инструмент.</summary>
    public const string PluginUse = "use";

    /// <summary>Операция правила плагина: задание «авто ПО» ЗАПУСКАЕТ его программу.</summary>
    public const string PluginRun = "run";

    /// <summary>Решение для команды OS (задел todo25: инструментов OS пока нет).
    /// По умолчанию — переспросить.</summary>
    public (SecurityDecision Decision, SecurityRule? Rule) ForOsCommand(string command) =>
        Strictest(_rules.Where(r =>
                r.Target == SecurityTarget.OsCommand && PatternMatches(r.Pattern, command)),
            SecurityDecision.Confirm);

    /// <summary>
    /// Описание правил задачи для системного промпта агента (todo26): без него агент считает,
    /// что кроме каталога проекта ему ничего не доступно, и не пытается использовать каталоги,
    /// открытые правилами (наблюдалось вживую: «другие пути мне недоступны»). Перечисляются
    /// правила каталогов (вкл. разрешающие) и не-разрешающие правила действий; пусто — "".
    /// Блок уходит в ПРОМПТ, поэтому язык — язык команды (T-190, T-191), а не установки.
    /// </summary>
    /// <param name="language">Язык общения с агентом; null — язык установки.</param>
    public string DescribeForAgent(string? language = null)
    {
        var lines = new List<string>();
        foreach (var rule in _rules)
        {
            if (rule.Target == SecurityTarget.Directory)
            {
                var ops = new List<string>();
                if (rule.OpRead) { ops.Add(Loc.In(language, "prompt.security.1")); }
                if (rule.OpWrite) { ops.Add(Loc.In(language, "prompt.security.2")); }
                if (rule.OpDelete) { ops.Add(Loc.In(language, "prompt.security.3")); }
                lines.Add(Loc.In(language, "prompt.security.4", rule.Pattern,
                    PermissionText(rule.Permission, language), string.Join(", ", ops)));
            }
            else if (rule.Target is SecurityTarget.Action or SecurityTarget.Plugin
                     && rule.Permission != SecurityPermission.Allow)
            {
                lines.Add(Loc.In(language, "prompt.security.5", rule.Pattern,
                    PermissionText(rule.Permission, language)));
            }
        }
        if (lines.Count == 0)
        {
            return "";
        }
        return Loc.In(language, "prompt.security.6")
               + string.Join("\n", lines)
               + Loc.In(language, "prompt.security.7");
    }

    /// <summary>
    /// Полные пути каталогов, открытых РАЗРЕШАЮЩИМИ правилами (T-117): CLI-коннектор
    /// транслирует их флагами <c>--add-dir</c> — текстовое правило в промпте песочницу
    /// Claude Code не расширяет (наблюдалось на T-66). Берутся только правила с полным путём;
    /// хвостовой вилдкард «/*» отрезается (он означает «каталог и всё внутри»), правила
    /// с другими вилдкардами и «re:» в конкретный каталог не превращаются и пропускаются.
    /// Относительные пути — внутри папки проекта, её песочница CLI и так покрывает.
    /// </summary>
    public List<string> AllowedFullPathDirs()
    {
        var dirs = new List<string>();
        foreach (var rule in _rules)
        {
            if (rule.Target != SecurityTarget.Directory
                || ToDecision(rule.Permission) != SecurityDecision.Allow)
            {
                continue;
            }
            var path = rule.Pattern.Trim();
            if (path.StartsWith("re:", StringComparison.OrdinalIgnoreCase)
                || !SecurityRulePaths.IsFullPath(path))
            {
                continue;
            }
            // CLI-агенту каталог передаётся флагом --add-dir: «~» он не раскрывает (T-135)
            path = AI2P.Core.PathHome.Expand(path);
            if (path.EndsWith("/*", StringComparison.Ordinal)
                || path.EndsWith("\\*", StringComparison.Ordinal))
            {
                path = path[..^2];
            }
            path = path.TrimEnd('/', '\\');
            if (path.Length == 0 || path.Contains('*') || path.Contains('?'))
            {
                continue;
            }
            if (!dirs.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                dirs.Add(path);
            }
        }
        return dirs;
    }

    private static string PermissionText(string permission, string? language) => permission switch
    {
        SecurityPermission.Allow => Loc.In(language, "prompt.security.8"),
        SecurityPermission.Confirm => Loc.In(language, "prompt.security.9"),
        _ => Loc.In(language, "prompt.security.10"),
    };

    /// <summary>Путь внутри папки проекта (полный доступ по умолчанию, todo25).</summary>
    public bool IsInsideProject(string absPath)
    {
        if (_projectRoot is null)
        {
            return false;
        }
        absPath = Path.GetFullPath(absPath);
        return absPath.StartsWith(_projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(absPath, _projectRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Из нескольких подходящих правил побеждает самое строгое; нет правил — дефолт.</summary>
    private static (SecurityDecision, SecurityRule?) Strictest(IEnumerable<SecurityRule> matched,
        SecurityDecision fallback)
    {
        SecurityRule? winner = null;
        var decision = fallback;
        var found = false;
        foreach (var rule in matched)
        {
            var current = ToDecision(rule.Permission);
            if (!found || Rank(current) > Rank(decision))
            {
                decision = current;
                winner = rule;
            }
            found = true;
        }
        return (decision, winner);
    }

    private static int Rank(SecurityDecision d) => d switch
    {
        SecurityDecision.Deny => 2,
        SecurityDecision.Confirm => 1,
        _ => 0,
    };

    private static SecurityDecision ToDecision(string permission) => permission switch
    {
        SecurityPermission.Deny => SecurityDecision.Deny,
        SecurityPermission.Confirm => SecurityDecision.Confirm,
        _ => SecurityDecision.Allow,
    };

    private static bool CoversOp(SecurityRule rule, string op) => op switch
    {
        "read" => rule.OpRead,
        "write" => rule.OpWrite,
        "delete" => rule.OpDelete,
        PluginUse => rule.OpUse,
        PluginRun => rule.OpRun,
        _ => false,
    };

    /// <summary>Паттерн каталога: относительный путь — от папки проекта (уровни проекта и
    /// задачи, todo25); null — правило неприменимо (относительный путь без папки проекта).
    /// Полный путь распознаётся независимо от ОС (SecurityRulePaths, todo25-фикс).</summary>
    private string? ResolvePattern(SecurityRule rule)
    {
        var pattern = rule.Pattern.Trim();
        if (pattern.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            return pattern;
        }
        if (SecurityRulePaths.IsFullPath(pattern))
        {
            // «~/ai/AI2P/data» сравнивается с настоящим путём, поэтому раскрывается (T-135)
            return AI2P.Core.PathHome.Expand(pattern);
        }
        return _projectRoot is null ? null : Path.Combine(_projectRoot, pattern);
    }

    /// <summary>Единый вид пути для сопоставления: полный, слэши «/», без завершающего «/».</summary>
    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    /// <summary>Сопоставление паттерна: «re:…» — регулярное выражение, иначе вилдкард (* ?);
    /// без учёта регистра. Путь-паттерн без вилдкарда накрывает и сам путь, и всё внутри.</summary>
    private static bool PatternMatches(string? pattern, string value, bool isPath = false)
    {
        if (pattern is null)
        {
            return false;
        }
        pattern = pattern.Trim();
        if (pattern.Length == 0)
        {
            return false;
        }
        if (pattern.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Regex.IsMatch(value, pattern[3..],
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return false; // некорректное выражение — правило не срабатывает
            }
        }
        if (isPath)
        {
            pattern = Normalize(pattern);
            // «dir/*» накрывает и сам каталог — иначе обзор открытого каталога (list_files
            // c:\tmp при правиле c:\tmp\*) отваливался бы (todo26)
            if (pattern.EndsWith("/*", StringComparison.Ordinal)
                && value.Equals(pattern[..^2], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                   || (isPath && value.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase));
        }
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
