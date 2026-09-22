using System.Text.RegularExpressions;

namespace AI2P.Connectors;

/// <summary>
/// Откуда взять ВХОДНОЙ ФАЙЛ медиа-задания — стартовый кадр (T-258, разбор T-251 п. 2.3.A)
/// либо эталонную запись голоса/звука (T-249-S0) — зеркало
/// <see cref="MediaOutputDirective"/>. Модель «изображение → видео» (Kandinsky 5.0 I2V)
/// рисует ролик от картинки: она задаёт персонажа и сцену, а описание задачи — движение.
/// Аудио-модель ровно так же получает образец голоса: постоянный тембр у звука даёт не
/// адаптер, а трёхсекундная запись.
/// Файловых инструментов у медиа-модели нет, поэтому файл ищет КОННЕКТОР — в самом
/// описании задачи, двумя способами:
///
/// 1. <b>Указание словами</b> — «взять за основу refs/hero.png», «стартовый кадр
///    refs/hero.png», «animate from refs/hero.png», «образец голоса refs/vera.wav».
///    Строка с именем файла и словом-указанием («исходн», «за основу», «стартов»,
///    «эталон», source/start/input/…) считается указанием и ИЗ ПРОМПТА УБИРАЕТСЯ.
/// 2. <b>Строка-путь</b> — строка, начинающаяся с пути к файлу. Ровно так
///    выглядит раскрытие ссылки на объект проекта «@obj:OBJ-3» (T-259): паспорт персонажа
///    и под ним пути его эталонных файлов, по одному в строке. Первый такой путь и есть
///    входной файл; все они из промпта убираются — это пути, а не текст генерации.
///
/// Указание словами сильнее строки-пути: человек, назвавший файл прямо, знает, чего хочет.
///
/// Путь всегда относительный ПАПКЕ ПРОЕКТА (там же лежат эталонные файлы объектов, T-259);
/// выход наружу («..», абсолютный путь, диск) не допускается — такое указание считается
/// неразобранным. Расширения — только своего рода: видео стартовым кадром не бывает, а
/// указание с .mp4 — это, скорее всего, указание на РЕЗУЛЬТАТ (его разбирает
/// <see cref="MediaOutputDirective"/>).
///
/// КАРТИНКА И ЗВУК РАЗБИРАЮТСЯ ПОРОЗНЬ и независимо (<see cref="Parse"/> и
/// <see cref="ParseAudio"/>): наборы расширений не пересекаются, поэтому одно описание
/// спокойно несёт и стартовый кадр, и образец голоса — их вынимают два вызова подряд,
/// каждый по своему набору.
/// </summary>
public static class MediaInputDirective
{
    /// <summary>Расширения файлов, которые годятся в стартовый кадр.</summary>
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>Расширения файлов, которые годятся в эталонную запись (T-249-S0).</summary>
    private static readonly string[] AudioExtensions = [".wav", ".mp3", ".flac", ".ogg", ".m4a", ".opus"];

    /// <summary>Слова-указания на ВХОДНОЙ файл. Список намеренно ДВУЯЗЫЧНЫЙ и остаётся
    /// в коде (T-191): это не сообщение человеку, а распознавание описания задачи.
    /// Общих слов («файл», «картинка», image, file) здесь нет намеренно — они означают
    /// и результат тоже, и строка «сохрани картинку в hero.png» перестала бы быть
    /// указанием на результат.</summary>
    private static readonly string[] Keywords =
    [
        "исходн", "за основу", "на основе", "основой", "стартов", "начальн", "первый кадр",
        "эталон", "референс", "оживи", "оживить", "анимируй", "анимировать", "вход",
        "source", "start", "input", "reference", "based on", "animate", "from image",
    ];

    /// <summary>
    /// Слова-указания, добавляемые к общим при разборе ЗВУКА (T-249-S0). «Голосом такого-то»
    /// и «voice sample» про картинку не говорят никогда, поэтому в общий список они не идут:
    /// там каждое лишнее слово увеличивает риск съесть строку промпта.
    /// </summary>
    private static readonly string[] AudioKeywords =
    [
        "голос", "образец", "тембр", "запись", "озвуч", "voice", "sample", "timbre", "speaker",
    ];

    /// <summary>Имя файла-картинки (возможно с подкаталогами через / или \).</summary>
    private static readonly Regex FileToken = new(
        @"[^\s""'«»`(),]*\.(?:png|jpe?g|webp|bmp)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Имя файла-записи (T-249-S0).</summary>
    private static readonly Regex AudioToken = new(
        @"[^\s""'«»`(),]*\.(?:wav|mp3|flac|ogg|m4a|opus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Строка НАЧИНАЕТСЯ с пути к картинке — так выглядит раскрытие «@obj:…» (T-259):
    /// каждый эталонный файл объекта уходит с новой строки. Хвост после пути допускается
    /// и сохраняется: ссылка часто стоит внутри предложения («Крупный план: @obj:OBJ-3,
    /// дождь»), и тогда за последним путём остаётся продолжение фразы.
    /// </summary>
    private static readonly Regex LineStartsWithPath = new(
        @"^(?<path>[^\s""'«»`(),]+\.(?:png|jpe?g|webp|bmp))(?<rest>[\s,;.].*|)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>То же для записи (T-249-S0).</summary>
    private static readonly Regex LineStartsWithAudioPath = new(
        @"^(?<path>[^\s""'«»`(),]+\.(?:wav|mp3|flac|ogg|m4a|opus))(?<rest>[\s,;.].*|)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Границы предложений: точка перед пробелом (точка внутри имени файла не в счёт).</summary>
    private static readonly Regex SentenceBreak = new(@"(?<=[.;!?])\s+", RegexOptions.Compiled);

    /// <summary>Один набор правил разбора: свои расширения, слова, распознаватели.</summary>
    private sealed record Rules(
        string[] Extensions, string[] Keywords, Regex Token, Regex LineStart);

    private static readonly Rules ImageRules =
        new(Extensions, Keywords, FileToken, LineStartsWithPath);

    private static readonly Rules AudioRules =
        new(AudioExtensions, [.. Keywords, .. AudioKeywords], AudioToken, LineStartsWithAudioPath);

    /// <summary>Промпт генерации без указаний и относительный путь стартового кадра
    /// (null — стартового кадра в описании нет).</summary>
    public static (string Prompt, string? Path) Parse(string description) =>
        Parse(description, ImageRules);

    /// <summary>
    /// То же для ЭТАЛОННОЙ ЗАПИСИ (T-249-S0): промпт без указаний и относительный путь
    /// файла звука. Зовётся ВТОРЫМ, по результату <see cref="Parse"/> — иначе строка
    /// с обоими файлами сразу («исходный кадр hero.png, голос vera.wav») разобралась бы
    /// только наполовину.
    /// </summary>
    public static (string Prompt, string? Path) ParseAudio(string description) =>
        Parse(description, AudioRules);

    private static (string Prompt, string? Path) Parse(string description, Rules rules)
    {
        var lines = description.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var multiline = lines.Count(l => l.Trim().Length > 0) > 1;

        // указание словами — первое сверху: входной файл называют в начале описания,
        // до самого промпта движения
        var directiveLine = -1;
        string? path = null;
        for (var i = 0; i < lines.Length && path is null; i++)
        {
            path = PathIn(lines[i], rules);
            directiveLine = path is null ? -1 : i;
        }

        // строки-пути (раскрытие ссылки на объект, T-259): пути убираем из промпта,
        // первый берём входным файлом, если указания словами не было
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var match = i == directiveLine ? Match.Empty : rules.LineStart.Match(lines[i].Trim());
            if (!match.Success || Normalize(match.Groups["path"].Value, rules) is not { } filePath)
            {
                continue;
            }
            path ??= filePath;
            // хвост предложения после пути остаётся текстом промпта, сам путь уходит
            lines[i] = match.Groups["rest"].Value.TrimStart(' ', ',', ';', '.', '—', '-');
            changed = true;
        }

        if (directiveLine >= 0)
        {
            // многострочное описание — убираем строку целиком; всё уместилось в одну строку —
            // только предложение с указанием, иначе от промпта ничего не останется
            lines[directiveLine] = multiline ? "" : RemoveSentence(lines[directiveLine], rules);
            changed = true;
        }
        if (!changed)
        {
            return (description.Trim(), null);
        }
        var prompt = string.Join("\n", lines).Trim();
        return (prompt.Length > 0 ? prompt : description.Trim(), path);
    }

    /// <summary>Путь из строки-указания; null — в строке нет файла или слова-указания.</summary>
    private static string? PathIn(string line, Rules rules)
    {
        var lower = line.ToLowerInvariant();
        if (!rules.Keywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            return null;
        }
        var match = rules.Token.Matches(line).LastOrDefault();
        return match is null ? null : Normalize(match.Value, rules);
    }

    /// <summary>Убрать из строки предложение с указанием (описание в одну строку).</summary>
    private static string RemoveSentence(string line, Rules rules) =>
        string.Join(" ", SentenceBreak.Split(line).Where(s => PathIn(s, rules) is null)).Trim();

    /// <summary>Привести к относительному пути внутри папки проекта; null — путь недопустим.</summary>
    private static string? Normalize(string token, Rules rules)
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
        if (!rules.Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
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
