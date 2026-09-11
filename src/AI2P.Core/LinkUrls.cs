using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// ЛОКАЛЬНАЯ И ВНЕШНЯЯ ССЫЛКА (ТЗ гл. 11, T-142).
///
/// Ссылки система строит от имени сервера (<c>ui.hostname</c>, гл. 10). Имя это —
/// ВНЕШНЕЕ: по нему сервер зовут в сети, и только такую ссылку имеет смысл кому-то
/// пересылать. Но в повседневной работе она же и мешает: человек сидит на этом самом
/// сервере, к нему подключён по петле, а ссылка уводит его наружу — через DNS, NAT
/// и сеть обратно к себе же. Файл при этом ещё и качается по сети целиком.
///
/// Поэтому ссылок две:
/// <list type="bullet">
/// <item><b>локальная</b> — петлевая (<c>localhost</c>): работает на КАЖДОМ сервере
/// кластера, потому что задачи реплицируются и та же задача есть у каждого;</item>
/// <item><b>внешняя</b> — по имени сервера: её отправляют тем, кто снаружи.</item>
/// </list>
///
/// Если имя сервера и так петлевое (<c>localhost</c>, <c>127.*</c>, <c>::1</c>), ссылка
/// одна — вторая была бы её копией.
/// </summary>
public static class LinkUrls
{
    /// <summary>Петлевое имя, которое подставляется в локальные ссылки.</summary>
    public const string LocalHost = "localhost";

    /// <summary>Имя сервера в базовом URL петлевое — локальная и внешняя ссылки совпадут,
    /// показывать две незачем.</summary>
    public static bool IsLocalOnly(string? baseUrl) => ServerAddress.IsLoopback(HostOf(baseUrl));

    /// <summary>
    /// ОТНОСИТЕЛЬНАЯ ССЫЛКА (T-196-S0): тот же адрес ОТ КОРНЯ САЙТА — <c>/ai2p/org/task/T-18</c>.
    ///
    /// Ни протокола, ни имени сервера, ни порта в ней нет, поэтому она переживает то, от чего
    /// ломается полная: переход с <c>http</c> на <c>https</c>, смену имени сервера и порта,
    /// проброс на роутере. Работает она внутри того же сайта — её ставят в тексты заданий
    /// и документы, которые читают в этом же приложении; тому, кто снаружи, по-прежнему
    /// отправляют внешнюю.
    ///
    /// Пусто — адрес не разбирается (не http(s) и не путь от корня).
    /// </summary>
    public static string RelativeOf(string? url)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0)
        {
            return "";
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" ? uri.PathAndQuery + uri.Fragment : "";
        }
        // уже относительная от корня — отдаём как есть; адрес, относительный СТРАНИЦЫ
        // («api/files/raw?path=…»), от корня достроить нечем, и выдумывать корень нельзя
        return value.StartsWith('/') ? value : "";
    }

    /// <summary>Имя хоста из базового URL; пусто — разобрать не удалось.</summary>
    public static string HostOf(string? baseUrl) =>
        Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri) ? uri.Host : "";

    /// <summary>
    /// Тот же базовый URL с петлевым именем: <c>http://мойпк:5480/ai2p/org</c> →
    /// <c>http://localhost:5480/ai2p/org</c>. Протокол и путь сохраняются, имя хоста
    /// заменяется петлевым. Неразбираемый URL возвращается как есть.
    /// </summary>
    /// <param name="baseUrl">Базовый URL (обычно внешний).</param>
    /// <param name="localPort">
    /// Порт, который слушает сервер НА ЭТОМ компьютере; <c>null</c> или 0 — оставить порт
    /// исходного URL (так было до T-2-S1). Параметр появился вместе со вторым адресом
    /// сервера: у внешней ссылки порт бывает свой (проброшенный на роутере), и подставлять
    /// его в петлевую ссылку нельзя — на этом компьютере такого порта никто не слушает.
    /// </param>
    public static string ToLocal(string? baseUrl, int? localPort = null)
    {
        var value = (baseUrl ?? "").Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }
        var builder = new UriBuilder(uri) { Host = LocalHost };
        // UriBuilder сам добавляет порт по умолчанию (80/443) — в исходном URL его не было
        if (uri.IsDefaultPort && localPort is not (> 0))
        {
            builder.Port = -1;
        }
        if (localPort is > 0 and <= 65535)
        {
            builder.Port = localPort.Value;
        }
        return builder.Uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// Наш ли это адрес и, если да, путь от корня сайта (<c>/ai2p/org/api/files/…</c>).
    /// null — URL не наш (другой сервер, внешний сайт) либо не абсолютный.
    ///
    /// Нужно для ссылок на файлы, которые ИИ-агент записал в текст задания: он строит их
    /// от ВНЕШНЕГО имени своего сервера, и клик уводил бы в сеть за файлом, который лежит
    /// на этом же диске. Путь без имени хоста браузер откроет по тому адресу, по которому
    /// человек уже подключён.
    /// </summary>
    /// <param name="url">Ссылка из текста.</param>
    /// <param name="ownBaseUrls">
    /// Собственные базовые адреса ЭТОГО сервера (без кода организации) — их несколько
    /// (T-2-S1): внешний (<c>hostname2</c>), внутренний (<c>hostname</c>) и петлевой.
    /// Ссылка, написанная по любому из них, ведёт сюда же, и своей она быть не перестала —
    /// иначе ссылки, накопленные в задачах до появления второго адреса, уводили бы в сеть
    /// за файлом с этого же диска. Пустые значения пропускаются.
    /// </param>
    /// <summary>
    /// ВСЕ АДРЕСА, ПО КОТОРЫМ ЭТОТ СЕРВЕР — МЫ САМИ (T-2-S1), для <see cref="OwnPathOf"/>:
    /// внешний (по нему ссылки строятся), собственный (<c>hostname</c> — по нему написаны
    /// ссылки в накопленных заданиях и по нему сервер зовут в своей сети) и петлевой (такие
    /// ссылки пишут система и ИИ-агент, работая на этом же компьютере).
    ///
    /// Петлевой считается от СОБСТВЕННОГО адреса: у внешней ссылки порт бывает проброшенный
    /// на роутере, и на этом компьютере его никто не слушает.
    /// </summary>
    /// <param name="externalBaseUrl">Адрес, по которому строятся ссылки (<c>hostname2</c>).</param>
    /// <param name="ownBaseUrl">Собственный адрес сервера; пусто — второго адреса нет
    /// и собственный совпадает с внешним.</param>
    public static string?[] OwnBases(string? externalBaseUrl, string? ownBaseUrl = null)
    {
        var own = string.IsNullOrWhiteSpace(ownBaseUrl) ? externalBaseUrl : ownBaseUrl;
        return [externalBaseUrl, ownBaseUrl, ToLocal(own)];
    }

    public static string? OwnPathOf(string? url, params string?[] ownBaseUrls)
    {
        foreach (var ownBaseUrl in ownBaseUrls)
        {
            if (!string.IsNullOrWhiteSpace(ownBaseUrl) && PathOf(url, ownBaseUrl) is { } path)
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// ССЫЛКА НА ФАЙЛ, НАПИСАННАЯ НА ДРУГОМ СЕРВЕРЕ КЛАСТЕРА (T-13-S0), — путь на ЭТОМ.
    ///
    /// Ссылку на созданный файл пишет агент, и пишет он её от адреса СВОЕГО сервера. Раньше
    /// такая ссылка не трогалась: файла здесь может и не быть, а сломать работающую ссылку
    /// хуже, чем сходить за файлом по сети. Теперь ходить по сети незачем — сервер, у
    /// которого файла нет, спрашивает его у соседа сам (<c>RemoteFileService</c>), — а вот
    /// прежнее поведение оказалось хуже, чем казалось, и по двум причинам сразу:
    /// <list type="bullet">
    /// <item>ПЕТЛЕВОЙ адрес (<c>http://localhost:5480/…</c>) — а такими ссылки и выходят,
    /// пока внешнее имя сервера не задано, — на другом компьютере означает СОВСЕМ ДРУГУЮ
    /// машину: у соседа там его собственный сервер, а с телефона — сам телефон;</item>
    /// <item>адрес соседа по кластеру уводит в его сеть, где у человека нет входа (cookie
    /// выдаёт каждый сервер свой), и вместо картинки приходит отказ.</item>
    /// </list>
    /// Поэтому такие ссылки сворачиваются в ПУТЬ: браузер попросит файл у того сервера,
    /// к которому человек уже подключён и на котором он вошёл, а тот принесёт его от соседа.
    /// </summary>
    /// <param name="url">Ссылка из текста.</param>
    /// <param name="ownBaseUrls">Свои адреса (<see cref="OwnBases"/>).</param>
    /// <param name="peerBaseUrls">Адреса ОСТАЛЬНЫХ серверов организации; пусто — соседей нет
    /// либо они неизвестны (тогда сворачиваются только свои и петлевые адреса).</param>
    public static string? ClusterPathOf(string? url, string?[] ownBaseUrls,
        IReadOnlyCollection<string>? peerBaseUrls = null)
    {
        if (OwnPathOf(url, ownBaseUrls) is { } own)
        {
            return own;
        }
        if (peerBaseUrls is { Count: > 0 }
            && OwnPathOf(url, [.. peerBaseUrls.Cast<string?>()]) is { } peer)
        {
            return peer;
        }
        return LoopbackPathOf(url);
    }

    /// <summary>Путь ссылки с ПЕТЛЕВЫМ именем хоста (любой порт); null — имя не петлевое
    /// или адрес не разбирается. Петлевое имя в накопленных ссылках означает «тот сервер,
    /// где ссылку написали», и на любом другом компьютере ведёт не туда.</summary>
    private static string? LoopbackPathOf(string? url) =>
        Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && ServerAddress.IsLoopback(uri.Host)
            ? uri.PathAndQuery + uri.Fragment
            : null;

    /// <summary>
    /// Путь ссылки, если она ведёт на <paramref name="ownBaseUrl"/>; null — адрес чужой.
    ///
    /// ПОИСК СВОЕГО АДРЕСА ИНВАРИАНТЕН К ПРОТОКОЛУ И К КОРНЮ САЙТА (T-196-S0): сравниваются
    /// только имя хоста и порт, а <c>http</c>/<c>https</c> и базовый путь — нет. Сервер,
    /// переведённый на https (T-315), остаётся собой, а ссылки, накопленные в заданиях,
    /// написаны по http: сравнение по схеме объявляло бы их чужими, и человек уходил бы
    /// в сеть за файлом, который лежит у него на диске. Один порт слушает один протокол,
    /// поэтому подмены адреса такое послабление не даёт.
    /// </summary>
    private static string? PathOf(string? url, string? ownBaseUrl)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)
            || !Uri.TryCreate((ownBaseUrl ?? "").Trim(), UriKind.Absolute, out var own)
            || uri.Scheme is not ("http" or "https")
            || own.Scheme is not ("http" or "https"))
        {
            return null;
        }
        // «свой» — совпали имя хоста и порт; петлевое имя считается своим тоже,
        // иначе ссылка, набранная через localhost, осталась бы абсолютной без нужды.
        // Сравнение по IdnHost: имя из кириллицы рендер Markdown приводит к punycode
        // («мойпк» → «xn--i1aceig»), и по Host такая ссылка своей бы не опозналась
        var sameHost = string.Equals(uri.IdnHost, own.IdnHost, StringComparison.OrdinalIgnoreCase)
                       || (ServerAddress.IsLoopback(uri.Host) && ServerAddress.IsLoopback(own.Host));
        // порт сравнивается, но у http и https УМОЛЧАНИЯ разные (80 и 443): адрес без порта
        // остаётся тем же самым и после смены протокола, поэтому «оба по умолчанию» — совпало
        var samePort = uri.Port == own.Port || (uri.IsDefaultPort && own.IsDefaultPort);
        return sameHost && samePort ? uri.PathAndQuery + uri.Fragment : null;
    }
}
