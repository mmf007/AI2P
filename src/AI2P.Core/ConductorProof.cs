using System.Security.Cryptography;
using System.Text;

namespace AI2P.Core;

/// <summary>
/// ДОКАЗАТЕЛЬСТВО ПРАВА ОБЪЯВИТЬ СЕБЯ ДИРИЖЁРОМ (T-21-S1).
///
/// Смена дирижёра обязана дойти до ВСЕХ серверов кластера, а не только до второй стороны
/// заявки. Обычная репликация для этого не годится сама по себе: топология — звезда, все
/// реплицируются только с дирижёром, и токены доступа выданы попарно «дирижёр ↔ участник».
/// Новый дирижёр с рядовым сервером до сих пор мог ни разу не разговаривать, а в аварийном
/// случае (прежний дирижёр физически потерян) посредника нет вовсе — и остальные серверы
/// остались бы навсегда звонить по мёртвому адресу.
///
/// Поэтому новый дирижёр ОБЪЯВЛЯЕТ о себе напрямую (<c>/api/cluster/conductor/announce</c>),
/// а получатель должен как-то убедиться, что звонящий — свой. Токена у них может не быть,
/// значит нужен другой общий секрет; он есть — КЛЮЧ ОРГАНИЗАЦИИ (ТЗ гл. 10): 32 байта,
/// которые получает каждый сервер при подтверждении своей заявки на подключение, и которые
/// никогда не реплицируются. Знание ключа организации и есть доказательство членства в ней.
///
/// Подпись — HMAC-SHA256 по строке «организация|сервер|заявка|токен|время». Время входит
/// в подпись и проверяется на свежесть: перехваченное объявление нельзя проиграть заново
/// через месяц. Сравнение подписей — постоянного времени (<see cref="CryptographicOperations"/>).
///
/// Границы доверия честные: ключ организации общий для всех её серверов, поэтому объявить
/// себя дирижёром вправе ЛЮБОЙ её сервер. Это и требуется — одностороннее назначение при
/// потере дирижёра иначе неисполнимо; защита здесь от чужого, а не от своего.
/// </summary>
public static class ConductorProof
{
    /// <summary>Сколько объявление считается свежим: сервер-получатель может быть выключен,
    /// но не месяцами. Час в обе стороны покрывает и расхождение часов между машинами.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromHours(24);

    /// <summary>Строка, которую подписывают: все значащие поля объявления по порядку.</summary>
    public static string Payload(string orgId, string serverId, string requestId, string token,
        string timestamp) =>
        string.Join("|", orgId, serverId, requestId, token, timestamp);

    /// <summary>Подписать объявление ключом организации; результат — base64.</summary>
    public static string Sign(byte[] orgKey, string payload) =>
        Convert.ToBase64String(HMACSHA256.HashData(orgKey, Encoding.UTF8.GetBytes(payload)));

    /// <summary>
    /// Подпись верна и объявление свежее. Пустой ключ, пустая подпись, испорченный base64
    /// и просроченное время — всё это «нет», а не исключение: объявление приходит по сети
    /// от кого угодно.
    /// </summary>
    /// <param name="timestamp">Время объявления в формате «o» (как его подписал автор).</param>
    /// <param name="now">Текущее время UTC — параметром, чтобы правило можно было проверить.</param>
    public static bool Verify(byte[]? orgKey, string payload, string signature, string timestamp,
        DateTime now)
    {
        if (orgKey is not { Length: > 0 } || signature.Trim().Length == 0)
        {
            return false;
        }
        if (!DateTime.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var stamp))
        {
            return false;
        }
        if ((now - stamp.ToUniversalTime()).Duration() > Freshness)
        {
            return false;
        }
        byte[] given;
        try
        {
            given = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(orgKey, Encoding.UTF8.GetBytes(payload));
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }
}
