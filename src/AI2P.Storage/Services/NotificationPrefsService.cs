using AI2P.Core;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Подписка браузера на Web Push (T-422-S0): адрес службы доставки и ключи шифрования.</summary>
public sealed class PushSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ExecutorId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// ЛИЧНЫЕ НАСТРОЙКИ УВЕДОМЛЕНИЙ И ПОДПИСКИ WEB PUSH (T-422-S0).
///
/// Настройки (<c>notification_prefs</c>) принадлежат человеку и реплицируются: письмо шлёт
/// владелец задачи, и ему нужны настройки адресата. Строки нет — умолчания
/// <see cref="NotificationPrefs"/>. Подписки (<c>push_subscriptions</c>) — память ЭТОГО
/// сервера: браузер подписан под его ключ VAPID, и слать в подписку может только он.
/// </summary>
public sealed class NotificationPrefsService
{
    private readonly Database _db;

    public NotificationPrefsService(Database db)
    {
        _db = db;
    }

    /// <summary>Настройки человека; строки нет — умолчания.</summary>
    public NotificationPrefs Get(string executorId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM notification_prefs WHERE executor_id=@e",
                   Map, ("@e", executorId)).FirstOrDefault()
               ?? new NotificationPrefs { ExecutorId = executorId };
    }

    /// <summary>У кого включён набор «мои сроки — в календарь» — для сторожа сроков.</summary>
    public Dictionary<string, NotificationPrefs> WithDue()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM notification_prefs WHERE due=1", Map)
            .ToDictionary(p => p.ExecutorId, StringComparer.Ordinal);
    }

    /// <summary>Записать настройки (токен ленты не меняется — для него <see cref="ResetIcalToken"/>).</summary>
    public NotificationPrefs Save(NotificationPrefs prefs)
    {
        if (!NotificationPrefChannels.IsKnown(prefs.Channel))
        {
            throw new ArgumentException(Loc.T("msg.notify.15", prefs.Channel));
        }
        if (prefs.DueLeadMinutes < 0)
        {
            throw new ArgumentException(Loc.T("msg.notify.8"));
        }
        prefs.QuietFrom = NormalizeTime(prefs.QuietFrom);
        prefs.QuietTo = NormalizeTime(prefs.QuietTo);
        prefs.IcalToken = Get(prefs.ExecutorId).IcalToken;
        Write(prefs);
        return prefs;
    }

    /// <summary>Завести личной календарной ленте новый секрет (прежняя ссылка перестаёт
    /// работать). Пустой <paramref name="off"/>=true — ленту выключить вовсе.</summary>
    public NotificationPrefs ResetIcalToken(string executorId, bool off = false)
    {
        var prefs = Get(executorId);
        prefs.IcalToken = off ? "" : NewToken();
        Write(prefs);
        return prefs;
    }

    /// <summary>Чья лента по секрету; null — секрет не найден.</summary>
    public NotificationPrefs? ByIcalToken(string token)
    {
        if (token.Trim().Length < 16)
        {
            return null;
        }
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM notification_prefs WHERE ical_token=@t",
            Map, ("@t", token.Trim())).FirstOrDefault();
    }

    // --- подписки Web Push ---

    public List<PushSubscription> Subscriptions(string executorId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM push_subscriptions WHERE executor_id=@e ORDER BY created_at",
            MapSub, ("@e", executorId));
    }

    /// <summary>Записать подписку; тот же адрес службы доставки — обновить ключи и владельца.</summary>
    public PushSubscription Subscribe(PushSubscription sub)
    {
        if (!Uri.TryCreate(sub.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(Loc.T("msg.notify.16"));
        }
        if (sub.P256dh.Trim().Length == 0 || sub.Auth.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.notify.16"));
        }
        sub.CreatedAt = DateTime.UtcNow;
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO push_subscriptions (id, executor_id, endpoint, p256dh, auth, user_agent, created_at)
            VALUES (@id, @e, @ep, @p, @a, @ua, @at)
            ON CONFLICT(endpoint) DO UPDATE SET executor_id=@e, p256dh=@p, auth=@a, user_agent=@ua
            """,
            ("@id", sub.Id), ("@e", sub.ExecutorId), ("@ep", sub.Endpoint.Trim()),
            ("@p", sub.P256dh.Trim()), ("@a", sub.Auth.Trim()), ("@ua", sub.UserAgent),
            ("@at", Sql.ToDb(sub.CreatedAt)));
        return sub;
    }

    /// <summary>Снять подписку по адресу службы доставки (кнопка «выключить» или ответ 404/410).</summary>
    public void Unsubscribe(string endpoint)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM push_subscriptions WHERE endpoint=@ep", ("@ep", endpoint.Trim()));
    }

    /// <summary>Снять ВСЕ подписки человека на этом сервере — «забыть мои устройства».</summary>
    public void UnsubscribeAll(string executorId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM push_subscriptions WHERE executor_id=@e", ("@e", executorId));
    }

    // --- внутреннее ---

    private void Write(NotificationPrefs prefs)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO notification_prefs (executor_id, inbox, due, due_lead, review, channel,
                                            quiet_from, quiet_to, ical_token, ical_todo, updated_at)
            VALUES (@e, @inbox, @due, @lead, @review, @channel, @qf, @qt, @token, @todo, @at)
            ON CONFLICT(executor_id) DO UPDATE SET inbox=@inbox, due=@due, due_lead=@lead,
                review=@review, channel=@channel, quiet_from=@qf, quiet_to=@qt,
                ical_token=@token, ical_todo=@todo, updated_at=@at
            """,
            ("@e", prefs.ExecutorId), ("@inbox", prefs.Inbox ? 1 : 0), ("@due", prefs.Due ? 1 : 0),
            ("@lead", prefs.DueLeadMinutes), ("@review", prefs.Review ? 1 : 0),
            ("@channel", prefs.Channel), ("@qf", prefs.QuietFrom), ("@qt", prefs.QuietTo),
            ("@token", prefs.IcalToken), ("@todo", prefs.IcalTodo ? 1 : 0),
            ("@at", Sql.ToDb(DateTime.UtcNow)));
    }

    private static string NormalizeTime(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }
        return TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time)
            ? time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : throw new ArgumentException(Loc.T("msg.notify.17", text));
    }

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    private static NotificationPrefs Map(SqliteDataReader r) => new()
    {
        ExecutorId = r.S("executor_id"),
        Inbox = r.B("inbox"),
        Due = r.B("due"),
        DueLeadMinutes = (int)r.L("due_lead"),
        Review = r.B("review"),
        Channel = r.S("channel"),
        QuietFrom = r.S("quiet_from"),
        QuietTo = r.S("quiet_to"),
        IcalToken = r.S("ical_token"),
        IcalTodo = r.B("ical_todo"),
    };

    private static PushSubscription MapSub(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        ExecutorId = r.S("executor_id"),
        Endpoint = r.S("endpoint"),
        P256dh = r.S("p256dh"),
        Auth = r.S("auth"),
        UserAgent = r.S("user_agent"),
        CreatedAt = r.Dt("created_at"),
    };
}
