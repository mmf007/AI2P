using System.Net.Security;

namespace AI2P.Server;

/// <summary>
/// ОБРАЩЕНИЯ К СОСЕДЯМ ПО КЛАСТЕРУ ПО HTTPS (T-206).
///
/// Браузеру сертификат своего УЦ человек ставит руками (об этом сказано в документации),
/// а серверы ходят друг к другу САМИ, и подтвердить исключение там некому: сертификат не
/// от глобального УЦ — и репликация встаёт с «The remote certificate is invalid», причём
/// навсегда, до правки настроек.
///
/// Поэтому есть настройка <c>ui.https.trustAnyPeer</c>. С T-215-S0 умолчание — ДА:
/// сертификат у наших серверов почти всегда самодельный, и выключенная настройка означала,
/// что первый же сервер, подключаемый к кластеру с https, встаёт с «удалённый сертификат
/// отклонён». Включённая, она снимает проверку сертификата ТОЛЬКО для вызовов сервер-сервер
/// (заявки, репликация, файлы) и не касается ни браузера, ни обращений к провайдерам
/// моделей — их клиенты свои.
///
/// Значение ставится при старте, до создания первого клиента, и ПОВТОРНО — при сохранении
/// формы локального сервера: клиенты сервер-сервер живут всё время работы процесса, поэтому
/// проверка спрашивает <see cref="TrustAny"/> в момент рукопожатия, а не при создании
/// обработчика (T-315). Иначе включённый флажок начинал бы действовать только после
/// перезапуска, а человек этого ниоткуда не узнаёт.
/// </summary>
public static class HttpsPeers
{
    /// <summary>Доверять сертификату соседа без проверки (<c>ui.https.trustAnyPeer</c>).</summary>
    public static bool TrustAny { get; set; }

    /// <summary>Обработчик для клиента, которым зовут ДРУГИЕ серверы кластера.
    /// <paramref name="connectTimeout"/> — своё ограничение подключения, если оно нужно
    /// вызывающему (файловая раздача отсекает выключенные серверы за секунды).</summary>
    public static SocketsHttpHandler Handler(TimeSpan? connectTimeout = null)
    {
        var handler = new SocketsHttpHandler();
        if (connectTimeout is { } timeout)
        {
            handler.ConnectTimeout = timeout;
        }
        // условие спрашивается В МОМЕНТ рукопожатия (T-315), а не здесь: обработчик заведён
        // один раз на весь процесс, и «снимок» настройки сделал бы флажок формы неработающим
        // до перезапуска
        handler.SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, errors) =>
                errors == SslPolicyErrors.None || TrustAny,
        };
        return handler;
    }

    /// <summary>
    /// ОБРАЩЕНИЕ К СОБСТВЕННОМУ API ПО ПЕТЛЕ (T-206). Зовут его двое: интерфейс (принцип
    /// API-first — Blazor ходит в своё же API) и клиент командной строки ИИ-агента, у
    /// которого токен задания и принимается-то только с петлевого адреса.
    ///
    /// По HTTPS такой вызов не проходил бы НИКОГДА, даже с настоящим сертификатом от
    /// глобального УЦ: адрес здесь <c>localhost</c>, а сертификат выписан на имя сервера,
    /// и проверка имени валится по определению. Поэтому на ПЕТЛЕВОЙ адрес проверка
    /// сертификата не действует — на том конце тот же самый компьютер, и подменить
    /// собеседника там некому.
    /// </summary>
    public static HttpMessageHandler LoopbackHandler() => Loopback;

    /// <summary>
    /// Правило проверки для <see cref="LoopbackHandler"/>: обычная проверка везде, кроме
    /// ПЕТЛЕВОГО адреса. Вынесено отдельно, чтобы его можно было проверить тестом.
    ///
    /// Взято <see cref="HttpClientHandler"/>, а не SocketsHttpHandler: только у него в
    /// проверку приходит САМ ЗАПРОС, а значит и адрес, по которому идёт обращение.
    /// У SocketsHttpHandler.SslOptions обработчик получает от SslStream совсем другого
    /// отправителя, и «этот адрес петлевой?» спросить не у чего — поймано живой проверкой:
    /// страница интерфейса отвечала 500 с «remote certificate was rejected».
    /// </summary>
    public static bool AllowLoopback(HttpRequestMessage? request, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || request?.RequestUri is { IsLoopback: true };

    private static readonly HttpClientHandler Loopback = new()
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            AllowLoopback(request, errors),
    };
}
