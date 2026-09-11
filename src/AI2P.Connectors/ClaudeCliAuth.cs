using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Распознавание ответа Claude Code CLI «входа нет» (todo96, версия 1.96).
///
/// ЗАЧЕМ. Сеанс CLI живёт не вечно: токен OAuth протухает, и CLI начинает отвечать
/// на КАЖДЫЙ запуск коротким итоговым JSON с кодом возврата 1 и текстом
/// <c>Failed to authenticate: OAuth session expired and could not be refreshed</c>.
/// Для системы это выглядело обычной ошибкой задания: задача вставала «встал с ошибкой»,
/// человек читал в консоли простыню JSON, и догадаться, что нужно просто войти заново,
/// было неоткуда — а войти можно было только руками в терминале (<c>claude auth login</c>).
///
/// ЧТО ДЕЛАЕТ. Отличает «нет входа» от прочих ошибок CLI — по формулировке либо по коду
/// 401/403 в итоговом JSON — и разбирает ответ <c>claude auth status --json</c>.
/// Дальше коннектор бросает <see cref="ProviderAuthException"/>, задача уходит НЕ в ошибку,
/// а в паузу с предложением войти, и вход делается из AI2P (<see cref="ClaudeLoginService"/>).
///
/// Рядом с <see cref="ClaudeCliLimit"/> и по тем же правилам: маркеры ищутся только
/// в ОШИБОЧНЫХ исходах CLI, а в успешном ответе — лишь пока он короткий, иначе отчёт
/// агента, где написано про вход и токены, оборвал бы задание на ровном месте.
/// </summary>
public static class ClaudeCliAuth
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ClaudeCliAuth));

    /// <summary>Признаки «входа нет» в тексте CLI (регистр не важен). Формулировки у CLI
    /// меняются от версии к версии, поэтому их несколько, и рядом стоит проверка кода
    /// ответа провайдера (<see cref="ApiStatusRx"/>) — код не меняется.</summary>
    private static readonly string[] Markers =
    [
        "failed to authenticate",
        "oauth session expired",
        "could not be refreshed",
        "oauth token expired",
        "session expired",
        "not logged in",
        "not authenticated",
        "authentication_error",
        "authentication failed",
        "invalid api key",
        "invalid bearer token",
        "please run /login",
        "run /login",
        "claude login",
        "claude auth login",
        "log in with your claude account",
    ];

    /// <summary>Длина текста, до которой ответ БЕЗ признака ошибки ещё можно считать
    /// сообщением о входе: настоящий результат задания всегда длиннее (правило
    /// <see cref="ClaudeCliLimit.ShortAnswerLimit"/>).</summary>
    public const int ShortAnswerLimit = 300;

    private static readonly RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Отказ провайдера по авторизации в итоговом JSON CLI:
    /// <c>"api_error_status":401</c> (или 403).</summary>
    private static readonly Regex ApiStatusRx = new(@"""api_error_status""\s*:\s*40[13]", Opts);

    /// <summary>Текст похож на «войдите заново»: узнаётся по формулировке либо по коду
    /// 401/403 в итоговом JSON CLI.</summary>
    public static bool LooksLikeAuthGone(string? text) =>
        text is not null
        && (Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))
            || ApiStatusRx.IsMatch(text));

    /// <summary>
    /// То же для УСПЕШНОГО исхода CLI (код 0, is_error нет): сообщение о входе приходит
    /// вместо результата и всегда короткое, поэтому длинный текст входом не считается.
    /// </summary>
    public static bool LooksLikeAuthGoneInAnswer(string? text) =>
        (text ?? "").Trim().Length <= ShortAnswerLimit && LooksLikeAuthGone(text);

    /// <summary>
    /// Разобрать ответ <c>claude auth status --json</c>:
    /// <c>{"loggedIn":true,"authMethod":"claude.ai","email":"…","subscriptionType":"max"}</c>.
    /// Ответ не разобрался (старый CLI, который такой команды не знает) — <c>Known=false</c>:
    /// это НЕ «входа нет», а «проверить нечем», и ошибкой такое не считается.
    /// </summary>
    public static ClaudeAuthStatus ParseStatus(string stdout)
    {
        var start = stdout.IndexOf('{');
        if (start < 0)
        {
            return new ClaudeAuthStatus();
        }
        try
        {
            using var doc = JsonDocument.Parse(stdout[start..]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("loggedIn", out var logged)
                || logged.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new ClaudeAuthStatus();
            }
            return new ClaudeAuthStatus
            {
                Known = true,
                LoggedIn = logged.ValueKind == JsonValueKind.True,
                Email = Text(root, "email"),
                Method = Text(root, "authMethod"),
                Subscription = Text(root, "subscriptionType"),
                OrgName = Text(root, "orgName"),
            };
        }
        catch (JsonException)
        {
            return new ClaudeAuthStatus();
        }
    }

    /// <summary>
    /// Спросить у CLI состояние входа: <c>claude auth status --json</c>. Команда бесплатная
    /// и быстрая, поэтому ею пользуются и проба подключения исполнителя, и форма входа.
    /// Ничего не бросает: любая беда — это «выяснить не удалось» (<see cref="ClaudeAuthStatus.Known"/>
    /// = false), а не «входа нет».
    /// </summary>
    /// <param name="command">Команда CLI из профайла модели; берётся только исполняемый файл —
    /// флаги запуска агента команде <c>auth</c> не подходят.</param>
    public static async Task<ClaudeAuthStatus> StatusAsync(string? command, CancellationToken ct = default)
    {
        var exe = ClaudeLoginService.Executable(command);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ClaudeLoginService.StatusTimeout);
            var (exitCode, stdout, stderr) = await CliProcess.RunAsync(
                exe, "auth status --json", Environment.CurrentDirectory, stdin: null, timeout.Token);
            var status = ParseStatus(stdout);
            if (!status.Known)
            {
                // у «не вошёл» код возврата тоже бывает ненулевым — сначала смотрим текст
                if (LooksLikeAuthGone(stdout + "\n" + stderr))
                {
                    return new ClaudeAuthStatus { Known = true, LoggedIn = false };
                }
                Logger.Information("Claude CLI: состояние входа не выяснено (код {Code}): {Out}",
                    exitCode, (stdout + " " + stderr).Trim());
            }
            return status;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning("Claude CLI: auth status не ответил вовремя");
            return new ClaudeAuthStatus();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Claude CLI: auth status не запустился ({Exe})", exe);
            return new ClaudeAuthStatus();
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}

/// <summary>
/// Состояние входа Claude CLI на ЭТОМ компьютере (todo96): ответ <c>claude auth status
/// --json</c>. <see cref="Known"/> = false — спросить не удалось (CLI не установлен либо
/// команды <c>auth status</c> не знает); это не повод считать, что входа нет.
/// </summary>
public sealed class ClaudeAuthStatus
{
    /// <summary>CLI ответил понятным состоянием входа.</summary>
    public bool Known { get; set; }

    public bool LoggedIn { get; set; }

    /// <summary>Почта вошедшего аккаунта; пусто — CLI её не назвал.</summary>
    public string Email { get; set; } = "";

    /// <summary>Как вошли: claude.ai (подписка) либо console (оплата по API).</summary>
    public string Method { get; set; } = "";

    /// <summary>Вид подписки: max, pro, … Пусто — не назван.</summary>
    public string Subscription { get; set; } = "";

    /// <summary>Название организации Claude; пусто — не названа.</summary>
    public string OrgName { get; set; } = "";
}
