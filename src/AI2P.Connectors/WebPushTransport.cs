using AI2P.Core;
using AI2P.Storage.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AI2P.Connectors;

/// <summary>
/// ДОСТАВКА WEB PUSH (T-422-S0, п. А4 плана T-412-S0) — уведомление в браузер телефона или
/// компьютера без отдельного приложения: сервер и так открывается в браузере, достаточно
/// манифеста, service worker и подписки (кнопка в «Мои уведомления»). Android — в любом
/// браузере, iOS 16.4+ — после «На экран Домой».
///
/// Протокол целиком своими силами, без пакетов: подпись VAPID (RFC 8292, JWT ES256) и
/// шифрование содержимого aes128gcm (RFC 8291) — всё есть в System.Security.Cryptography
/// (ECDiffieHellman, HKDF, AesGcm). Ключ VAPID — секрет ЭТОГО сервера
/// (<c>secrets/notify.vapid.json</c>), заводится сам; подписка делается под него, поэтому
/// слать в неё может только этот сервер (подписки и не реплицируются).
///
/// Ответ службы доставки 404/410 значит «подписки больше нет» (браузер её снял) — такая
/// подписка удаляется, иначе мы стучались бы в неё вечно.
/// </summary>
public sealed class WebPushTransport : INotificationTransport
{
    /// <summary>Ссылка на ключ VAPID в хранилище секретов.</summary>
    public const string VapidRef = "notify.vapid";

    /// <summary>Сколько служба доставки держит сообщение, пока устройство не в сети.</summary>
    private const int TtlSeconds = 24 * 3600;

    /// <summary>Предел текста: служба доставки принимает до ~4 КБ зашифрованного.</summary>
    private const int MaxBody = 1000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly NotificationPrefsService _subs;
    private readonly SecretStore _secrets;
    private readonly Func<string> _subject;
    private readonly object _gate = new();
    private ECParameters? _vapid;

    /// <param name="subject">Контакт отправителя для VAPID: <c>mailto:</c> либо https-адрес.</param>
    public WebPushTransport(NotificationPrefsService subs, SecretStore secrets, Func<string> subject)
    {
        _subs = subs;
        _secrets = secrets;
        _subject = subject;
    }

    public string Transport => NotificationChannels.WebPushTransport;

    /// <summary>Ключ заводится сам, внешних настроек нет — транспорт готов всегда.</summary>
    public bool Ready => true;

    public string NotReadyReason => "";

    /// <summary>Открытый ключ VAPID (несжатая точка P-256, base64url) — его браузер
    /// передаёт в pushManager.subscribe как applicationServerKey.</summary>
    public string PublicKey
    {
        get
        {
            var key = Vapid();
            return NotificationReplyTokens.Base64Url([0x04, .. key.Q.X!, .. key.Q.Y!]);
        }
    }

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var subscriptions = _subs.Subscriptions(message.ExecutorId);
        if (subscriptions.Count == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.notify.18", message.ExecutorNick));
        }
        var body = message.Body.Length > MaxBody ? message.Body[..MaxBody] + "…" : message.Body;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            title = message.Subject,
            body,
            url = message.TaskLink,
            tag = message.QuestionId.Length > 0 ? message.QuestionId : message.TaskId,
        });
        var errors = new List<string>();
        var delivered = 0;
        foreach (var sub in subscriptions)
        {
            try
            {
                var status = await PostAsync(sub, payload, ct);
                if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    _subs.Unsubscribe(sub.Endpoint);   // браузер подписку снял
                    continue;
                }
                if ((int)status is >= 200 and < 300)
                {
                    delivered++;
                }
                else
                {
                    errors.Add($"{(int)status} {new Uri(sub.Endpoint).Host}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                           or CryptographicException or FormatException)
            {
                errors.Add(ex.Message);
            }
        }
        if (delivered == 0)
        {
            throw new InvalidOperationException(errors.Count > 0
                ? Loc.T("msg.notify.19", string.Join("; ", errors))
                : Loc.T("msg.notify.18", message.ExecutorNick));
        }
    }

    private async Task<HttpStatusCode> PostAsync(PushSubscription sub, byte[] payload, CancellationToken ct)
    {
        var endpoint = new Uri(sub.Endpoint);
        var body = Encrypt(payload,
            NotificationReplyTokens.FromBase64Url(sub.P256dh),
            NotificationReplyTokens.FromBase64Url(sub.Auth));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", TtlSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", "high");
        request.Headers.TryAddWithoutValidation("Authorization",
            $"vapid t={Jwt(endpoint.GetLeftPart(UriPartial.Authority))}, k={PublicKey}");
        using var response = await Http.SendAsync(request, ct);
        return response.StatusCode;
    }

    /// <summary>JWT VAPID (RFC 8292): aud — источник службы доставки, exp — 12 часов, sub — контакт.</summary>
    private string Jwt(string audience)
    {
        var header = NotificationReplyTokens.Base64Url(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var subject = _subject();
        if (!subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            subject = "mailto:ai2p@localhost";
        }
        var claims = NotificationReplyTokens.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = audience,
            exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
            sub = subject,
        }));
        var unsigned = header + "." + claims;
        using var ecdsa = ECDsa.Create(Vapid());
        // .NET отдаёт подпись в формате IEEE P1363 (r||s, 64 байта) — ровно то, что нужно JWS
        var signature = ecdsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
        return unsigned + "." + NotificationReplyTokens.Base64Url(signature);
    }

    /// <summary>
    /// Шифрование aes128gcm по RFC 8291: разовая пара ключей сервера, общий секрет ECDH
    /// с ключом браузера, два шага HKDF (с секретом auth и с солью) — ключ и nonce AES-GCM.
    /// Одна запись: содержимое + разделитель 0x02.
    /// </summary>
    public static byte[] Encrypt(byte[] payload, byte[] uaPublic, byte[] authSecret)
    {
        if (uaPublic.Length != 65 || uaPublic[0] != 0x04)
        {
            throw new FormatException("p256dh");
        }
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asParams = ephemeral.ExportParameters(false);
        byte[] asPublic = [0x04, .. asParams.Q.X!, .. asParams.Q.Y!];
        using var ua = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublic[1..33], Y = uaPublic[33..65] },
        });
        var ecdhSecret = ephemeral.DeriveRawSecretAgreement(ua.PublicKey);
        byte[] keyInfo = [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. uaPublic, .. asPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        var salt = RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt,
            Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt,
            Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        byte[] plain = [.. payload, 0x02];
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        // заголовок записи: соль, размер записи (4096, big-endian), длина и сам ключ сервера
        var result = new List<byte>(16 + 4 + 1 + 65 + cipher.Length + 16);
        result.AddRange(salt);
        result.AddRange([0x00, 0x00, 0x10, 0x00]);
        result.Add((byte)asPublic.Length);
        result.AddRange(asPublic);
        result.AddRange(cipher);
        result.AddRange(tag);
        return [.. result];
    }

    /// <summary>Ключ VAPID сервера; нет — заводится и кладётся в хранилище секретов.</summary>
    private ECParameters Vapid()
    {
        lock (_gate)
        {
            if (_vapid is { } cached)
            {
                return cached;
            }
            var stored = _secrets.Read(VapidRef);
            if (stored is { Length: > 0 })
            {
                try
                {
                    using var doc = JsonDocument.Parse(stored);
                    var root = doc.RootElement;
                    var parameters = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        D = NotificationReplyTokens.FromBase64Url(root.GetProperty("d").GetString() ?? ""),
                        Q = new ECPoint
                        {
                            X = NotificationReplyTokens.FromBase64Url(root.GetProperty("x").GetString() ?? ""),
                            Y = NotificationReplyTokens.FromBase64Url(root.GetProperty("y").GetString() ?? ""),
                        },
                    };
                    parameters.Validate();
                    _vapid = parameters;
                    return parameters;
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                               or FormatException or CryptographicException
                                               or InvalidOperationException)
                {
                    // испорченный ключ заменяется новым: подписки браузеров придётся сделать заново
                }
            }
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var created = ecdsa.ExportParameters(true);
            _secrets.Write(VapidRef, JsonSerializer.Serialize(new
            {
                d = NotificationReplyTokens.Base64Url(created.D!),
                x = NotificationReplyTokens.Base64Url(created.Q.X!),
                y = NotificationReplyTokens.Base64Url(created.Q.Y!),
            }));
            _vapid = created;
            return created;
        }
    }
}
