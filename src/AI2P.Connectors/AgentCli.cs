namespace AI2P.Connectors;

/// <summary>
/// КЛИЕНТ КОМАНДНОЙ СТРОКИ <c>ai2p</c> (T-34-S0) — сторона ЗАПУСКА: где лежит обёртка,
/// какими переменными окружения передаются реквизиты задания и какое разрешение надо
/// выдать самому Claude Code, чтобы он мог эту команду позвать.
///
/// <para>Зачем клиент вообще: у CLI-агента инструментов AI2P нет, а <c>/api</c> закрыто
/// входом по cookie — всё, что он делает с системой, идёт МАРКЕРАМИ в тексте ответа.
/// Маркер — это не вызов, а КОНЕЦ ХОДА: процесс <c>claude</c> завершается, ответ
/// подставляется в промпт, сессия продолжается через <c>--resume</c>. По телеметрии T-288
/// один такой ход стоит около 160 тыс. токенов чтения контекста — отсюда и пределы
/// «10 чтений заданий» и «20 файлов» за задание. Синхронный вызов снимает и цену, и пределы.
/// Маркеры при этом остаются запасным путём и не удаляются.</para>
///
/// <para>Отдельного exe НЕТ намеренно: <c>buildRelease.ps1</c> публикует ровно один проект,
/// и второй пришлось бы отдельно вносить в выкладку, установку, опись <c>installed.json</c>
/// и <c>MakePackage</c>; хуже того, в ПОЛНОЙ (self-contained) выкладке framework-dependent
/// exe не запустился бы вовсе — рантайма на машине нет. Поэтому клиент — это подкоманда
/// <c>AI2P.Server.exe cli …</c> плюс тонкая обёртка <c>ai2p.cmd</c> / <c>ai2p</c> рядом
/// с программой (по образцу <c>install.cmd</c> → <c>install.ps1</c>).</para>
/// </summary>
public static class AgentCli
{
    /// <summary>Подкоманда запуска клиента: <c>AI2P.Server.exe cli get-task T-15</c>.</summary>
    public const string SubCommand = "cli";

    /// <summary>Переменная окружения с базовым адресом сервера ПО ПЕТЛЕ.</summary>
    public const string UrlVar = "AI2P_URL";

    /// <summary>Переменная окружения с токеном задания (живёт только пока задание идёт).</summary>
    public const string TokenVar = "AI2P_JOB_TOKEN";

    /// <summary>Переменная окружения с кодом задачи — клиент подписывает ею свои сообщения.</summary>
    public const string TaskVar = "AI2P_TASK";

    /// <summary>Язык, на котором клиент разговаривает с агентом (язык команды задачи, T-190).</summary>
    public const string LangVar = "AI2P_LANG";

    /// <summary>Имя обёртки рядом с программой: на Windows — .cmd (у .ps1 ассоциация
    /// «редактировать», и по Enter он не выполняется), на прочих — файл без расширения.</summary>
    public static string WrapperName => OperatingSystem.IsWindows() ? "ai2p.cmd" : "ai2p";

    /// <summary>
    /// Полный путь обёртки; null — её рядом нет (запуск из тестового каталога, где
    /// содержимого выкладки не бывает). Тогда клиент агенту не предлагается вовсе,
    /// и он работает по-старому, маркерами.
    /// </summary>
    public static string? WrapperPath(string? baseDir = null)
    {
        var dir = baseDir is { Length: > 0 } ? baseDir : AppContext.BaseDirectory;
        var path = Path.Combine(dir, WrapperName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Как агенту писать команду в консоли: путь с пробелами — в кавычках.
    /// </summary>
    public static string CommandLine(string wrapperPath) =>
        wrapperPath.Contains(' ') ? $"\"{wrapperPath}\"" : wrapperPath;

    /// <summary>
    /// РАЗРЕШЕНИЕ НА ЗАПУСК КОМАНДЫ В САМОМ CLAUDE CODE (T-34-S0). Без него вызов упирается
    /// в песочницу: агент получает отказ и теряет ход (в телеметрии T-288 таких отказов
    /// подряд было пять). Правило Claude Code для Bash — префиксное: <c>Bash(&lt;начало
    /// команды&gt;:*)</c>. Начало команды зависит от того, как агент её напишет, поэтому
    /// разрешений выдаётся несколько сразу: полный путь как есть, полный путь в кавычках
    /// (так его пишут, когда в пути есть пробел) и короткое имя — на случай, если каталог
    /// установки оказался в PATH.
    /// </summary>
    public static IReadOnlyList<string> AllowedToolPatterns(string wrapperPath)
    {
        var names = new List<string>
        {
            wrapperPath,
            $"\"{wrapperPath}\"",
            Path.GetFileNameWithoutExtension(wrapperPath),
            Path.GetFileName(wrapperPath),
        };
        return names
            .Where(n => n.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(n => $"Bash({n}:*)")
            .ToList();
    }
}
