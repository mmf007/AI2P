using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AI2P.Connectors;

/// <summary>
/// ССЫЛКИ БЫСТРОГО ОТВЕТА (T-422-S0, п. А2 плана T-412-S0). Письмо о вопросе агента несёт
/// ссылку на лёгкую страницу ответа БЕЗ ВХОДА: ссылка подписана ключом сервера (HMAC-SHA256)
/// и несёт id вопроса, id человека и срок жизни. Хранить выданные ссылки не нужно:
/// одноразовость даёт сам вопрос — ответ его закрывает (answered_at), и вторая попытка
/// получает «на вопрос уже ответили».
///
/// Ключ — секрет ЭТОГО сервера (<c>secrets/notify.reply.key.json</c>), заводится сам при
/// первой ссылке. Ссылка ведёт на адрес того же сервера, что её подписал
/// (<c>publicBaseUrl</c>), поэтому делить ключ с соседями по кластеру не нужно.
/// </summary>
public sealed class NotificationReplyTokens
{
    /// <summary>Ссылка на ключ подписи в хранилище секретов.</summary>
    public const string KeyRef = "notify.reply.key";

    /// <summary>Сколько живёт ссылка: неделя — вопрос агента дольше обычно не висит.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly Func<byte[]> _key;
    private byte[]? _cached;
    private readonly object _gate = new();

    public NotificationReplyTokens(SecretStore secrets)
        : this(() => LoadOrCreate(secrets))
    {
    }

    /// <summary>Ключ задан явно (проверки).</summary>
    public NotificationReplyTokens(Func<byte[]> key)
    {
        _key = key;
    }

    /// <summary>Подписанная ссылка на ответ человека <paramref name="executorId"/> на вопрос
    /// <paramref name="questionId"/>.</summary>
    public string Issue(string questionId, string executorId, DateTime? nowUtc = null)
    {
        var expires = new DateTimeOffset(nowUtc ?? DateTime.UtcNow).Add(Lifetime).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes(
            string.Join('|', "q", questionId, executorId, expires.ToString(CultureInfo.InvariantCulture)));
        return Base64Url(payload) + "." + Base64Url(Sign(payload));
    }

    /// <summary>Проверить ссылку: null — подпись не та, ссылка испорчена или просрочена.</summary>
    public (string QuestionId, string ExecutorId)? Verify(string token, DateTime? nowUtc = null)
    {
        var parts = (token ?? "").Split('.');
        if (parts.Length != 2)
        {
            return null;
        }
        byte[] payload;
        byte[] signature;
        try
        {
            payload = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload)))
        {
            return null;
        }
        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 4 || fields[0] != "q"
            || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires))
        {
            return null;
        }
        var now = new DateTimeOffset(nowUtc ?? DateTime.UtcNow).ToUnixTimeSeconds();
        return now > expires ? null : (fields[1], fields[2]);
    }

    private byte[] Sign(byte[] payload)
    {
        lock (_gate)
        {
            _cached ??= _key();
        }
        return HMACSHA256.HashData(_cached, payload);
    }

    private static byte[] LoadOrCreate(SecretStore secrets)
    {
        var stored = secrets.Read(KeyRef);
        if (stored is { Length: >= 32 })
        {
            try
            {
                return Convert.FromHexString(stored.Trim());
            }
            catch (FormatException)
            {
                // испорченный ключ заменяется новым: прежние ссылки перестанут работать
            }
        }
        var key = RandomNumberGenerator.GetBytes(32);
        secrets.Write(KeyRef, Convert.ToHexString(key));
        return key;
    }

    internal static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string text)
    {
        var s = text.Trim().Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
