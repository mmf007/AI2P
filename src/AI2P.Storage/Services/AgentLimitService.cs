using AI2P.Core.Entities;

namespace AI2P.Storage.Services;

/// <summary>
/// Остаток лимита ИИ-исполнителя за скользящее окно (T-121). Считается по токенам заданий,
/// поэтому годится и для подписки Claude Code, которая остатка не сообщает.
/// </summary>
public sealed class AgentLimitStatus
{
    public string ExecutorId { get; set; } = "";
    public string Nick { get; set; } = "";
    /// <summary>Лимит токенов за окно (поле исполнителя).</summary>
    public long Limit { get; set; }
    /// <summary>Длина окна в часах (поле исполнителя).</summary>
    public double WindowHours { get; set; }
    /// <summary>Израсходовано за окно: сумма входных и выходных токенов заданий.</summary>
    public long Used { get; set; }
    /// <summary>Начало окна (момент расчёта минус длина окна).</summary>
    public DateTime WindowStart { get; set; }
    /// <summary>Когда окно освободится: самое раннее задание окна + длина окна;
    /// null — в окне заданий нет (расход нулевой).</summary>
    public DateTime? ResetAt { get; set; }
    /// <summary>Остаток токенов (не меньше нуля).</summary>
    public long Remaining => Math.Max(0, Limit - Used);
    /// <summary>Использовано, % от лимита (0–100).</summary>
    public double Percent => Limit <= 0 ? 0 : Math.Min(100, 100.0 * Used / Limit);
    /// <summary>Остатка «слишком мало» — запускать задание уже рискованно (T-121).</summary>
    public bool IsLow { get; set; }
}

/// <summary>
/// Оценка остатка лимита ИИ-исполнителя (T-121). У подписки Claude Code остаток измерить
/// нечем: ни CLI, ни его JSON-вывод его не отдают. Поэтому счёт ведёт сама система — по
/// токенам выполненных заданий за скользящее окно: у исполнителя задаются «лимит токенов
/// за окно» и «длина окна» (у подписки — 5 часов), а расход берётся из журнала заданий.
///
/// ГЛАВНОЕ ПРАВИЛО (T-121): лимиты у исполнителя НЕ УКАЗАНЫ — вычисление игнорируется
/// целиком (<see cref="Status"/> возвращает null), и всё работает как до этой доработки.
/// </summary>
public sealed class AgentLimitService
{
    /// <summary>Доля лимита, ниже которой остаток считается «слишком малым» (T-121):
    /// меньше пятой части окна — задание, скорее всего, оборвётся на середине.</summary>
    public const double LowRemainderShare = 0.2;

    private readonly Database _db;

    public AgentLimitService(Database db) => _db = db;

    /// <summary>Лимиты исполнителя заданы: оба поля положительны (T-121).</summary>
    public static bool HasLimits(Executor executor) =>
        executor.TokenLimit is > 0 && executor.LimitWindowHours is > 0;

    /// <summary>
    /// Расход и остаток лимита исполнителя на момент <paramref name="nowUtc"/> (по умолчанию
    /// сейчас). null — лимиты не указаны: считать нечего и предупреждать не о чем.
    /// </summary>
    public AgentLimitStatus? Status(Executor executor, DateTime? nowUtc = null)
    {
        if (!HasLimits(executor))
        {
            return null;
        }
        var now = nowUtc ?? DateTime.UtcNow;
        var window = TimeSpan.FromHours(executor.LimitWindowHours!.Value);
        var from = now - window;

        // момент задания — завершение, иначе старт, иначе создание: незавершённое задание
        // тоже расходует лимит, а его токены уже накоплены посегментно (ТЗ v1.17)
        using var conn = _db.Open();
        var row = Sql.Query(conn, null, """
            SELECT COALESCE(SUM(input_tokens + output_tokens), 0) AS used,
                   MIN(COALESCE(finished_at, started_at, created_at)) AS first_at
            FROM jobs
            WHERE executor_id=@executor AND deleted_at IS NULL
              AND COALESCE(finished_at, started_at, created_at) >= @from
            """,
            r => (Used: r.L("used"), FirstAt: r.SN("first_at")),
            ("@executor", executor.Id), ("@from", Sql.ToDb(from))).First();

        var limit = executor.TokenLimit!.Value;
        var status = new AgentLimitStatus
        {
            ExecutorId = executor.Id,
            Nick = executor.Nick,
            Limit = limit,
            WindowHours = executor.LimitWindowHours.Value,
            Used = row.Used,
            WindowStart = from,
            ResetAt = row.FirstAt is null
                ? null
                : DateTime.Parse(row.FirstAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime() + window,
        };
        status.IsLow = status.Remaining < limit * LowRemainderShare;
        return status;
    }
}
