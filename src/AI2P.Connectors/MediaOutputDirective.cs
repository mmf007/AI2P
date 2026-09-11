using System.Text.RegularExpressions;

namespace AI2P.Connectors;

/// <summary>
/// Куда положить готовый файл медиа-модели (ТЗ v1.45, todo37_3). У медиа-модели нет ни
/// агентного цикла, ни файловых инструментов: описание задачи целиком уходит промптом
/// генерации, поэтому написанное там «результат положить в корень проекта в файл trask1.mp4»
/// раньше просто попадало в промпт и никем не исполнялось.
///
/// Теперь такое указание разбирает КОННЕКТОР: строка с именем файла и словом-указанием
/// («положи», «сохрани», «файл», «результат», save/output/…) считается указанием, куда
/// скопировать результат в папке проекта, и ИЗ ПРОМПТА УБИРАЕТСЯ — иначе имя файла
/// попадает в текст генерации и портит результат.
///
/// Путь всегда относительный папке проекта; выход наружу («..», абсолютный путь, диск)
/// не допускается — такое указание считается неразобранным.
/// </summary>
public static class MediaOutputDirective
{
    /// <summary>Расширения файлов, которые может выдать медиа-модель.</summary>
    private static readonly string[] Extensions =
        [".mp4", ".webm", ".mov", ".mkv", ".avi", ".gif", ".png", ".jpg", ".jpeg", ".webp", ".wav", ".mp3"];

    /// <summary>Слова-указания: без них имя файла в тексте — просто упоминание, а не задание.
    /// Список намеренно ДВУЯЗЫЧНЫЙ и остаётся в коде (T-191): это не сообщение человеку,
    /// а распознавание описания задачи, и проверяется он целиком, независимо от языка
    /// установки — перенос в словарь по языку установки потерял бы вторую половину.</summary>
    private static readonly string[] Keywords =
    [
        "положи", "сохран", "запиши", "записать", "помест", "выложи", "файл",
        "результат", "назови", "назвать", "имя",
        "save", "store", "put", "write", "output", "result", "file", "name",
    ];

    /// <summary>Имя файла с известным расширением (возможно с подкаталогами через / или \).</summary>
    private static readonly Regex FileToken = new(
        @"[^\s""'«»`(),]*\.(?:mp4|webm|mov|mkv|avi|gif|png|jpe?g|webp|wav|mp3)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Границы предложений: точка перед пробелом (точка внутри имени файла не в счёт).</summary>
    private static readonly Regex SentenceBreak = new(@"(?<=[.;!?])\s+", RegexOptions.Compiled);

    /// <summary>Промпт генерации без указания и относительный путь файла результата (null — указания нет).</summary>
    public static (string Prompt, string? Path) Parse(string description)
    {
        var lines = description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var multiline = lines.Count(l => l.Trim().Length > 0) > 1;

        // с конца: указание обычно приписывают в хвосте задания
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var path = PathIn(lines[i]);
            if (path is null)
            {
                continue;
            }
            // многострочное описание — убираем строку целиком; всё уместилось в одну строку —
            // только предложение с указанием, иначе от промпта ничего не останется
            lines[i] = multiline ? "" : RemoveSentence(lines[i]);
            var prompt = string.Join("\n", lines).Trim();
            return (prompt.Length > 0 ? prompt : description.Trim(), path);
        }
        return (description.Trim(), null);
    }

    /// <summary>Путь из строки-указания; null — в строке нет имени файла или слова-указания.</summary>
    private static string? PathIn(string line)
    {
        var lower = line.ToLowerInvariant();
        if (!Keywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            return null;
        }
        var match = FileToken.Matches(line).LastOrDefault();
        return match is null ? null : Normalize(match.Value);
    }

    /// <summary>Убрать из строки предложение с указанием (описание в одну строку).</summary>
    private static string RemoveSentence(string line) =>
        string.Join(" ", SentenceBreak.Split(line).Where(s => PathIn(s) is null)).Trim();

    /// <summary>Привести к относительному пути внутри папки проекта; null — путь недопустим.</summary>
    private static string? Normalize(string token)
    {
        var segments = token.Trim().Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && s != ".")
            .ToList();
        if (segments.Count == 0)
        {
            return null;
        }
        var name = segments[^1];
        if (!Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        // выход за пределы папки проекта и абсолютные пути не разрешаем
        if (segments.Any(s => s == ".." || s.Contains(':')))
        {
            return null;
        }
        return string.Join("/", segments);
    }
}
