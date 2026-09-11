using System.Globalization;
using System.Text.RegularExpressions;

namespace AI2P.Connectors;

/// <summary>
/// Распознавание ответа Claude Code CLI «лимит подписки исчерпан» (T-121, повторный пуск).
///
/// ЗАЧЕМ. Раньше система узнавала об исчерпанном лимите CLI только косвенно — по молчанию
/// процесса дольше внутреннего таймаута (T-117). Но CLI, оказывается, сообщает об этом сам:
/// возвращает короткий текст с маркером лимита и временем сброса, например
/// <c>5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)</c> или (старая форма)
/// <c>Claude AI usage limit reached|1754409000</c>. Без распознавания это выглядело обычной
/// ошибкой: задача светилась «встал с ошибкой», исполнитель не помечался занятым и никакого
/// «ожидает до» не появлялось.
///
/// ЧТО ДЕЛАЕТ. Находит в тексте маркер лимита и, если получится, момент сброса окна:
/// метка времени Unix, ISO-дата, часы вида <c>4:10pm</c> (в указанной в скобках зоне IANA)
/// или «resets in 25 minutes». Момент не разобрался — вернётся null: вызывающий подставит
/// свой запас (час, ТЗ v1.37).
/// </summary>
public static class ClaudeCliLimit
{
    /// <summary>Признаки лимита в тексте CLI (регистр не важен). «limit reached» покрывает
    /// и «5-hour limit reached», и «weekly limit reached»; «session limit» и «hit your limit» —
    /// новая формулировка CLI «You've hit your session limit · resets 9:20pm (Europe/Moscow)»
    /// (T-166): её прежний список не ловил, и задание вставало обычной ошибкой.</summary>
    private static readonly string[] Markers =
    [
        "usage limit reached",
        "limit reached",
        "limit exceeded",
        "session limit",
        "usage limit",
        "hit your limit",
        "rate limit",
        "rate_limit_error",
        "quota exceeded",
        "out of usage",
    ];

    /// <summary>Длина текста, до которой ответ БЕЗ признака ошибки ещё можно считать
    /// сообщением о лимите: настоящий результат задания всегда длиннее.</summary>
    public const int ShortAnswerLimit = 300;

    private static readonly RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Старая форма CLI: «Claude AI usage limit reached|1754409000» (Unix-время
    /// сброса, секунды либо миллисекунды).</summary>
    private static readonly Regex EpochRx = new(@"\|\s*(?<epoch>\d{9,13})\b", Opts);

    /// <summary>«resets at 2026-08-05T16:10:00Z» — дата целиком.</summary>
    private static readonly Regex IsoRx = new(
        @"reset[a-z]*\s*(?:at\s+)?(?<iso>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:?\d{2})?)",
        Opts);

    /// <summary>«resets 4:10pm (Europe/Moscow)», «will reset at 16:10», «resets 4pm».</summary>
    private static readonly Regex ClockRx = new(
        @"reset[a-z]*\s*(?:at\s+)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\s*(?:\((?<tz>[^)]{1,60})\))?",
        Opts);

    /// <summary>«resets in 25 minutes», «resets in 3 hours».</summary>
    private static readonly Regex InRx = new(
        @"reset[a-z]*\s+in\s+(?<n>\d{1,4})\s*(?<u>hours?|hrs?|h|minutes?|mins?|m)\b", Opts);

    /// <summary>Отказ провайдера по лимиту в итоговом JSON CLI: <c>"api_error_status":429</c>
    /// (T-166). Слова у CLI меняются от версии к версии, а код 429 — нет, поэтому он считается
    /// признаком лимита сам по себе.</summary>
    private static readonly Regex ApiStatusRx = new(@"""api_error_status""\s*:\s*429", Opts);

    /// <summary>Текст похож на сообщение об исчерпанном лимите: узнаётся по формулировке
    /// либо по коду 429 в итоговом JSON CLI (T-166).</summary>
    public static bool LooksLikeLimit(string? text) =>
        text is not null
        && (Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
            || ApiStatusRx.IsMatch(text));

    /// <summary>
    /// Распознать сообщение о лимите. Вызывать на ОШИБОЧНЫХ исходах CLI (ненулевой код
    /// возврата, is_error, stderr): там текст лимита ни с чем не спутать.
    /// </summary>
    /// <param name="text">stdout/stderr или текст результата CLI.</param>
    /// <param name="nowUtc">Момент расчёта (для «4:10pm» — какой день имеется в виду).</param>
    /// <param name="resetAtUtc">Когда лимит отпустит (UTC); null — CLI времени не назвал.</param>
    public static bool TryDetect(string? text, DateTime nowUtc, out DateTime? resetAtUtc)
    {
        resetAtUtc = null;
        if (!LooksLikeLimit(text))
        {
            return false;
        }
        resetAtUtc = ResetAt(text!, nowUtc);
        return true;
    }

    /// <summary>
    /// То же для УСПЕШНОГО исхода CLI (код 0, is_error нет): сообщение о лимите приходит
    /// вместо результата и всегда короткое, поэтому длинный текст лимитом не считается —
    /// иначе отчёт агента, где написано про лимиты (как в этом самом задании), оборвал бы
    /// задачу на ровном месте.
    /// </summary>
    public static bool TryDetectInAnswer(string? text, DateTime nowUtc, out DateTime? resetAtUtc)
    {
        resetAtUtc = null;
        return (text ?? "").Trim().Length <= ShortAnswerLimit && TryDetect(text, nowUtc, out resetAtUtc);
    }

    /// <summary>Момент сброса окна из текста CLI; null — времени в тексте нет
    /// либо оно не разобралось.</summary>
    public static DateTime? ResetAt(string text, DateTime nowUtc)
    {
        var now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();

        if (EpochRx.Match(text) is { Success: true } epoch
            && long.TryParse(epoch.Groups["epoch"].Value, out var seconds))
        {
            // 13 знаков — миллисекунды (так пишет часть версий CLI)
            var at = epoch.Groups["epoch"].Value.Length >= 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(seconds)
                : DateTimeOffset.FromUnixTimeSeconds(seconds);
            return Sane(at.UtcDateTime, now);
        }

        if (IsoRx.Match(text) is { Success: true } iso
            && DateTime.TryParse(iso.Groups["iso"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var isoAt))
        {
            return Sane(isoAt, now);
        }

        if (InRx.Match(text) is { Success: true } after
            && int.TryParse(after.Groups["n"].Value, out var count))
        {
            var unit = after.Groups["u"].Value.ToLowerInvariant();
            var span = unit.StartsWith('h') ? TimeSpan.FromHours(count) : TimeSpan.FromMinutes(count);
            return Sane(now + span, now);
        }

        if (ClockRx.Match(text) is { Success: true } clock)
        {
            return FromClock(clock, now);
        }
        return null;
    }

    /// <summary>«4:10pm (Europe/Moscow)» → ближайший будущий момент UTC. Зона не указана
    /// или неизвестна системе — местная зона сервера (CLI печатает время в ней).</summary>
    private static DateTime? FromClock(Match clock, DateTime nowUtc)
    {
        if (!int.TryParse(clock.Groups["h"].Value, out var hour))
        {
            return null;
        }
        var minute = clock.Groups["m"].Success && int.TryParse(clock.Groups["m"].Value, out var m) ? m : 0;
        var ap = clock.Groups["ap"].Value.ToLowerInvariant();
        if (ap == "pm" && hour < 12)
        {
            hour += 12;
        }
        else if (ap == "am" && hour == 12)
        {
            hour = 0;
        }
        if (hour > 23 || minute > 59)
        {
            return null;
        }

        var zone = ResolveZone(clock.Groups["tz"].Value);
        var nowThere = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var at = nowThere.Date.AddHours(hour).AddMinutes(minute);
        if (at <= nowThere)
        {
            // время суток уже прошло — значит, речь о завтрашнем сбросе
            at = at.AddDays(1);
        }
        try
        {
            if (zone.IsInvalidTime(at))
            {
                at = at.AddHours(1); // перевод часов вперёд: такого времени в этот день нет
            }
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(at, DateTimeKind.Unspecified), zone);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Часовой пояс из скобок: идентификатор IANA («Europe/Moscow») либо UTC/GMT;
    /// неизвестное значение — местная зона.</summary>
    private static TimeZoneInfo ResolveZone(string raw)
    {
        var name = raw.Trim();
        if (name.Length == 0)
        {
            return TimeZoneInfo.Local;
        }
        if (name.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GMT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>Защита от бессмысленных значений: момент сброса — в будущем и не дальше
    /// восьми суток вперёд (недельное окно подписки укладывается, а разобранный из мусора
    /// «сброс через год» — нет: иначе исполнитель «занят» до скончания века).</summary>
    private static DateTime? Sane(DateTime atUtc, DateTime nowUtc) =>
        atUtc > nowUtc && atUtc < nowUtc.AddDays(8) ? atUtc : null;
}
