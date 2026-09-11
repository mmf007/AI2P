using AI2P.Core;
using System.Net.Http.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;

namespace AI2P.Server.Api;

/// <summary>
/// Обращения к ДРУГИМ серверам кластера (ТЗ гл. 6, этап 41).
///
/// Протокол сервер-сервер — тот же HTTP API (принцип API-first), раздел <c>/api/cluster</c>.
/// Личность вызывающего подтверждается токеном, выданным при подключении, а не cookie
/// пользователя: серверы ходят друг к другу без человека за экраном.
///
/// Протокол берётся из записи сервера: сосед, у которого <c>protocol = https</c> (T-206),
/// зовётся по HTTPS. Сертификат не от глобального УЦ проверку не проходит, и подтвердить
/// исключение здесь некому — на этот случай есть настройка <c>ui.https.trustAnyPeer</c>
/// (<see cref="HttpsPeers"/>).
/// </summary>
public sealed class ClusterClient : IDisposable
{
    private readonly HttpClient _http = new(HttpsPeers.Handler()) { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Отдельный клиент для репликации (этап 43): пачка изменений при первичной
    /// репликации бывает тяжёлой, и двадцати секунд «проверки связи» ей мало.</summary>
    private readonly HttpClient _repl = new(HttpsPeers.Handler()) { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>«Представься»: связь есть и на том конце AI2P. Токена не требует —
    /// заявку на подключение подают ещё до того, как токен выдан.</summary>
    public async Task<ServerCheckDto> HelloAsync(ServerNode server, CancellationToken ct = default)
    {
        try
        {
            var response = await CallAsync(_http, server, "hello",
                url => new HttpRequestMessage(HttpMethod.Get, url), ct);
            await ThrowIfFailedAsync(response, ct, server, "hello");
            var hello = await response.Content.ReadFromJsonAsync<ClusterHelloDto>(Ai2pJson.Options, ct);
            if (hello is null)
            {
                return new ServerCheckDto { Error = Loc.T("msg.clusterClient.1") };
            }
            return new ServerCheckDto
            {
                Ok = true,
                ServerId = hello.ServerId,
                Name = hello.Name,
                Version = hello.Version,
                // сервер по этому адресу сменился (переустановка, чужой адрес) — репликация
                // узнаёт серверы по внутреннему ключу, поэтому расхождение важно показать
                SameServer = server.Id == hello.ServerId,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or NotSupportedException)
        {
            return new ServerCheckDto { Error = Explain(ex, server, "hello") };
        }
    }

    /// <summary>Подать заявку на подключение к организации чужого дирижёра (todo41 п. 7):
    /// решение принимает человек на той стороне, здесь мы получаем только ссылку на заявку.</summary>
    public async Task<ClusterJoinStatusDto> JoinAsync(ServerNode target, ClusterJoinDto request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await CallAsync(_http, target, "join", url => Json(HttpMethod.Post, url, request), ct);
            await ThrowIfFailedAsync(response, ct, target, "join");
            return await response.Content.ReadFromJsonAsync<ClusterJoinStatusDto>(Ai2pJson.Options, ct)
                   ?? throw new InvalidOperationException(Loc.T("msg.clusterClient.2"));
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, "join");
        }
    }

    /// <summary>Узнать решение по поданной заявке (её опрашивает подавший сервер).</summary>
    public async Task<ClusterJoinStatusDto> JoinStatusAsync(ServerNode target, string requestId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await CallAsync(_http, target, "join/" + Uri.EscapeDataString(requestId),
                url => new HttpRequestMessage(HttpMethod.Get, url), ct);
            await ThrowIfFailedAsync(response, ct, target, "join");
            return await response.Content.ReadFromJsonAsync<ClusterJoinStatusDto>(Ai2pJson.Options, ct)
                   ?? throw new InvalidOperationException(Loc.T("msg.clusterClient.3"));
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, "join/" + requestId);
        }
    }

    /// <summary>
    /// ОБЪЯВИТЬ СЕБЯ ДИРИЖЁРОМ организации (T-21-S1): новый дирижёр обходит остальные серверы
    /// кластера сам. Токена у этой пары может не быть вовсе — подлинность доказывает подпись
    /// ключом организации внутри тела запроса (<see cref="AI2P.Core.ConductorProof"/>),
    /// поэтому запрос идёт без заголовка токена, как заявка на подключение.
    /// </summary>
    public async Task<ClusterConductorAckDto> ConductorAnnounceAsync(ServerNode target,
        ClusterConductorDto dto, CancellationToken ct = default)
    {
        try
        {
            var response = await CallAsync(_http, target, "conductor/announce",
                url => Json(HttpMethod.Post, url, dto), ct);
            await ThrowIfFailedAsync(response, ct, target, "conductor/announce");
            return await response.Content.ReadFromJsonAsync<ClusterConductorAckDto>(Ai2pJson.Options, ct)
                   ?? throw new InvalidOperationException(Loc.T("msg.clusterClient.12"));
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, "conductor/announce");
        }
    }

    /// <summary>
    /// ПРИГЛАСИТЬ СЕРВЕР В ОРГАНИЗАЦИЮ (T-170-S0) — обратное направление подключения. Обычно
    /// сервер просится в организацию сам (<see cref="JoinAsync"/>), но человек вправе и просто
    /// отметить его галочкой в форме организации на дирижёре; тогда партнёр об этой организации
    /// не знает вовсе, и репликация с ним не началась бы никогда — сеанс отвергается словами
    /// «организация не найдена».
    ///
    /// Идёт ПО ТОКЕНУ: приглашать можно только сервер, который уже в нашем кластере и токен
    /// которого у нас есть. Тело — та же сводка, что и в ответе на подтверждённую заявку
    /// (<see cref="ClusterJoinStatusDto"/>): идентификаторы организации, коды обоих серверов,
    /// токен для обращения к нам и ключ организации.
    /// </summary>
    public async Task<ClusterConductorAckDto> OrgInviteAsync(ServerNode target, string token,
        ClusterJoinStatusDto invite, CancellationToken ct = default) =>
        await SendAsync<ClusterConductorAckDto>(target, token, HttpMethod.Post, "org/invite",
            invite, ct);

    /// <summary>
    /// СПРОСИТЬ, В КАКИЕ ОРГАНИЗАЦИИ НАС ВКЛЮЧИЛИ (T-312). Зеркало приглашения: приглашение
    /// уходит только когда дирижёр сам звонит, а до сервера за NAT он не дозванивается вовсе
    /// (T-141) — и тогда об организации, куда его отметили галочкой, партнёр не узнаёт никогда.
    /// Вопрос дешёвый (один GET по токену) и задаётся тем же ритмом, что и опрос решений
    /// по заявкам на подключение.
    ///
    /// Дирижёр прежней версии такого пути не знает и отвечает 404 — для звонящего это
    /// обычный отказ, и он молча идёт к следующему серверу.
    /// </summary>
    public async Task<ClusterOrgListDto> OrgsMineAsync(ServerNode target, string token,
        CancellationToken ct = default) =>
        await SendAsync<ClusterOrgListDto>(target, token, HttpMethod.Get, "orgs/mine", null, ct);

    /// <summary>Проверка связи по выданному нам токену: сервер отвечает, только если узнал нас.</summary>
    public async Task<ServerCheckDto> PingAsync(ServerNode server, string token,
        CancellationToken ct = default)
    {
        try
        {
            var response = await CallAsync(_http, server, "ping",
                url => Signed(HttpMethod.Get, url, token), ct);
            if (!response.IsSuccessStatusCode)
            {
                return new ServerCheckDto { Error = await ErrorTextAsync(response, ct, server, "ping") };
            }
            var hello = await response.Content.ReadFromJsonAsync<ClusterHelloDto>(Ai2pJson.Options, ct);
            return new ServerCheckDto
            {
                Ok = true,
                ServerId = hello?.ServerId ?? "",
                Name = hello?.Name ?? "",
                Version = hello?.Version ?? "",
                SameServer = hello is null || server.Id == hello.ServerId,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or NotSupportedException)
        {
            return new ServerCheckDto { Error = Explain(ex, server, "ping") };
        }
    }

    // --- репликация (ТЗ п. 6.1, гл. 6; этап 43) ---

    /// <summary>
    /// Начать сеанс репликации организации. Партнёр отвечает <c>Busy</c>, если сеанс с нами
    /// у него уже идёт: значит, отсчёт истёк на обоих концах, и наш запуск пропускается
    /// (todo43 «второй запуск игнорируется»).
    /// </summary>
    public async Task<ReplSessionDto> ReplBeginAsync(ServerNode target, string token, string orgCode,
        CancellationToken ct = default) =>
        await SendAsync<ReplSessionDto>(target, token, HttpMethod.Post, "repl/begin",
            new ReplBeginDto { OrgCode = orgCode }, ct);

    /// <summary>Закрыть сеанс: снять у партнёра замок пары.</summary>
    public async Task ReplEndAsync(ServerNode target, string token, string orgCode,
        CancellationToken ct = default) =>
        await SendAsync<ReplSessionDto>(target, token, HttpMethod.Post, "repl/end",
            new ReplBeginDto { OrgCode = orgCode }, ct);

    /// <summary>Забрать пачку изменений партнёра после курсора.</summary>
    public async Task<ReplChangesDto> ReplChangesAsync(ServerNode target, string token, string orgCode,
        string scope, long after, int limit, CancellationToken ct = default) =>
        await SendAsync<ReplChangesDto>(target, token, HttpMethod.Get,
            $"repl/changes?org={Uri.EscapeDataString(orgCode)}&scope={scope}&after={after}&limit={limit}",
            null, ct);

    /// <summary>Отдать партнёру пачку своих изменений.</summary>
    public async Task<ReplApplyResultDto> ReplApplyAsync(ServerNode target, string token,
        ReplApplyDto batch, CancellationToken ct = default) =>
        await SendAsync<ReplApplyResultDto>(target, token, HttpMethod.Post, "repl/apply", batch, ct);

    /// <summary>Передать партнёру токен, которым ОН будет ходить к нам (обратное направление
    /// обмена, ТЗ гл. 12): без него дирижёр не смог бы сам начать сеанс с этим сервером.</summary>
    public async Task ReplPeerTokenAsync(ServerNode target, string token, string ourToken,
        CancellationToken ct = default) =>
        await SendAsync<ReplSessionDto>(target, token, HttpMethod.Post, "repl/peer-token",
            new ClusterPeerTokenDto { Token = ourToken }, ct);

    // Логина на удалённый сервер ради списка его организаций больше нет (T-139, доработка):
    // он требовал отдать чужому серверу почту и пароль аккаунта дирижёра. Организацию
    // называют кодом в самой заявке, а на дирижёре с единственной организацией — не называют.

    // --- репликация ФАЙЛОВ (ТЗ гл. 6, п. 7 плана; этап 44) ---

    /// <summary>Манифест каталога партнёра: путь, размер, время и sha256 каждого файла.</summary>
    public async Task<ReplFileManifestDto> ReplFileManifestAsync(ServerNode target, string token,
        string orgCode, string scope, string key, CancellationToken ct = default) =>
        await SendAsync<ReplFileManifestDto>(target, token, HttpMethod.Get,
            $"repl/files/manifest?{FileQuery(orgCode, scope, key)}", null, ct);

    /// <summary>Прочитать кусок файла партнёра (докачка: смещение + длина).</summary>
    public async Task<byte[]> ReplFileReadAsync(ServerNode target, string token, string orgCode,
        string scope, string key, string path, long offset, int length, CancellationToken ct = default)
    {
        var path2 = $"repl/files/read?{FileQuery(orgCode, scope, key)}"
                    + $"&path={Uri.EscapeDataString(path)}&offset={offset}&length={length}";
        try
        {
            var response = await CallAsync(_repl, target, path2,
                url => Signed(HttpMethod.Get, url, token), ct);
            await ThrowIfFailedAsync(response, ct, target, path2);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, path2);
        }
    }

    /// <summary>Сколько байт файла партнёр уже принял (докачка после обрыва).</summary>
    public async Task<long> ReplFilePartAsync(ServerNode target, string token, string orgCode,
        string scope, string key, string path, CancellationToken ct = default)
    {
        var dto = await SendAsync<ReplFilePartDto>(target, token, HttpMethod.Get,
            $"repl/files/part?{FileQuery(orgCode, scope, key)}&path={Uri.EscapeDataString(path)}",
            null, ct);
        return dto.Offset;
    }

    /// <summary>Дописать кусок файла партнёру; последний кусок закрывает файл и ставит время.</summary>
    public async Task ReplFileWriteAsync(ServerNode target, string token, string orgCode, string scope,
        string key, string path, long offset, bool last, string mtime, ReadOnlyMemory<byte> chunk,
        CancellationToken ct = default)
    {
        var target2 = $"repl/files/write?{FileQuery(orgCode, scope, key)}"
                      + $"&path={Uri.EscapeDataString(path)}&offset={offset}"
                      + $"&last={(last ? "true" : "false")}&mtime={Uri.EscapeDataString(mtime)}";
        try
        {
            var response = await CallAsync(_repl, target, target2, url =>
            {
                var request = Signed(HttpMethod.Post, url, token);
                request.Content = new ReadOnlyMemoryContent(chunk);
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return request;
            }, ct);
            await ThrowIfFailedAsync(response, ct, target, target2);
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, target2);
        }
    }

    /// <summary>Удалить файл у партнёра (он удалён у нас и не менялся у него).</summary>
    public async Task ReplFileDeleteAsync(ServerNode target, string token, string orgCode, string scope,
        string key, string path, CancellationToken ct = default) =>
        await SendAsync<ReplFilePartDto>(target, token, HttpMethod.Post,
            $"repl/files/delete?{FileQuery(orgCode, scope, key)}&path={Uri.EscapeDataString(path)}",
            null, ct);

    /// <summary>Переименовать файл у партнёра — решение «переименовать» по конфликту (todo44).</summary>
    public async Task ReplFileRenameAsync(ServerNode target, string token, string orgCode, string scope,
        string key, string path, string to, CancellationToken ct = default) =>
        await SendAsync<ReplFilePartDto>(target, token, HttpMethod.Post,
            $"repl/files/rename?{FileQuery(orgCode, scope, key)}&path={Uri.EscapeDataString(path)}"
            + $"&to={Uri.EscapeDataString(to)}", null, ct);

    // --- СКАЧИВАНИЕ АРХИВА С ДРУГОГО СЕРВЕРА (T-47-S0) ---

    /// <summary>Попросить источник собрать архив в .zip и сказать его размер. Открытый
    /// у него архив он для этого упакует во временный файл — уезжает архив всегда закрытым.</summary>
    public async Task<ReplArcPackDto> ArcPackAsync(ServerNode target, string token, string orgCode,
        string archiveId, CancellationToken ct = default) =>
        await SendAsync<ReplArcPackDto>(target, token, HttpMethod.Get,
            $"repl/arc/pack?{ArcQuery(orgCode, archiveId)}", null, ct);

    /// <summary>Прочитать кусок собранного .zip (докачка: смещение + длина).</summary>
    public async Task<byte[]> ArcReadAsync(ServerNode target, string token, string orgCode,
        string archiveId, long offset, int length, CancellationToken ct = default)
    {
        var path = $"repl/arc/read?{ArcQuery(orgCode, archiveId)}&offset={offset}&length={length}";
        try
        {
            var response = await CallAsync(_repl, target, path,
                url => Signed(HttpMethod.Get, url, token), ct);
            await ThrowIfFailedAsync(response, ct, target, path);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, path);
        }
    }

    /// <summary>Передача кончилась — источник снимает временный свёрток.</summary>
    public async Task ArcDoneAsync(ServerNode target, string token, string orgCode, string archiveId,
        CancellationToken ct = default) =>
        await SendAsync<ReplFilePartDto>(target, token, HttpMethod.Post,
            $"repl/arc/done?{ArcQuery(orgCode, archiveId)}", null, ct);

    private static string ArcQuery(string orgCode, string archiveId) =>
        $"org={Uri.EscapeDataString(orgCode)}&archive={Uri.EscapeDataString(archiveId)}";

    private static string FileQuery(string orgCode, string scope, string key) =>
        $"org={Uri.EscapeDataString(orgCode)}&scope={Uri.EscapeDataString(scope)}"
        + $"&key={Uri.EscapeDataString(key)}";

    /// <summary>Запрос к разделу кластера под токеном сервера (ТЗ гл. 12).</summary>
    private async Task<T> SendAsync<T>(ServerNode target, string token, HttpMethod method, string path,
        object? body, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync(_repl, target, path, url =>
            {
                var request = Signed(method, url, token);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, options: Ai2pJson.Options);
                }
                return request;
            }, ct);
            await ThrowIfFailedAsync(response, ct, target, path);
            return await response.Content.ReadFromJsonAsync<T>(Ai2pJson.Options, ct)
                   ?? throw new InvalidOperationException(Loc.T("msg.clusterClient.4", path));
        }
        catch (Exception ex) when (IsNetwork(ex, ct))
        {
            throw Unreachable(ex, target, path);
        }
    }

    /// <summary>
    /// ОДИН ЗАПРОС, ДВА АДРЕСА (T-50-S0). У сервера за NAT адресов два: внутренний, по которому
    /// его зовут соседи в своей сети, и внешний, по которому его видно снаружи. Какой из них
    /// сегодня рабочий, заранее не знает никто — ноутбук уехал из офиса, и внутренний адрес
    /// перестал отвечать, — поэтому обращение пробует их ПО ОЧЕРЕДИ: сперва внутренний,
    /// и тут же, не дожидаясь следующего сеанса, внешний.
    ///
    /// Второй заход делается ТОЛЬКО по обрыву связи (<see cref="IsNetwork"/>): отказ самого
    /// AI2P (нет токена, чужая организация) от смены адреса не изменится, а повтор скрыл бы
    /// причину. Запрос строится заново на каждый заход — тело запроса второй раз не отправить.
    /// </summary>
    private static async Task<HttpResponseMessage> CallAsync(HttpClient http, ServerNode server,
        string path, Func<string, HttpRequestMessage> make, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(make(Url(server, path)), ct);
        }
        catch (Exception ex) when (IsNetwork(ex, ct) && server.HasAddress2)
        {
            Serilog.Log.Information("Кластер: сервер «{Server}» не отвечает по внутреннему адресу "
                + "{Url} ({Error}) — пробуем внешний {Url2} (T-50-S0)",
                server.Name, server.Url, Reason(ex), server.Url2);
            try
            {
                return await http.SendAsync(make(Url2(server, path)), ct);
            }
            catch (Exception ex2) when (IsNetwork(ex2, ct))
            {
                // ПРИЧИНЫ ОБЕИХ ПОПЫТОК (T-315). Сообщение об отказе называет ОБА адреса
                // (Where), а причину до сих пор показывало только от ПОСЛЕДНЕЙ попытки — от
                // внешнего адреса. Человек читал её как причину по первому адресу и искал
                // беду не там: у заказчика по внешнему адресу отвечал не AI2P, а роутер, и
                // «unexpected EOF» относился к нему, тогда как настоящая причина по адресу
                // локальной сети оставалась только в журнале строкой выше.
                //
                // Исключение бросается БЕЗ вложенного: Reason разворачивает цепочку до самого
                // внутреннего сообщения, и с inner собранный текст до человека не доехал бы
                throw new HttpRequestException(Loc.T("msg.clusterClient.13",
                    Reason(ex), Reason(ex2)));
            }
        }
    }

    /// <summary>Запрос с токеном сервера (ТЗ гл. 12) — им подписаны все обращения кластера,
    /// кроме заявки на подключение и объявления о смене дирижёра.</summary>
    private static HttpRequestMessage Signed(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(Ai2pHeaders.ServerToken, token);
        return request;
    }

    /// <summary>Запрос с телом JSON (заявка, объявление): токена у такой пары может не быть.</summary>
    private static HttpRequestMessage Json(HttpMethod method, string url, object body) =>
        new(method, url) { Content = JsonContent.Create(body, options: Ai2pJson.Options) };

    private static string Url(ServerNode server, string path) => $"{server.Url}/api/cluster/{path}";

    /// <summary>Тот же путь по ВТОРОМУ (внешнему) адресу сервера (T-50-S0).</summary>
    private static string Url2(ServerNode server, string path) => $"{server.Url2}/api/cluster/{path}";

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct,
        ServerNode? target = null, string path = "")
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ErrorTextAsync(response, ct, target, path));
        }
    }

    /// <summary>Текст ошибки удалённого сервера человеку: сообщение из ProblemDetails,
    /// иначе код ответа — иначе в снэкбаре оказывался бы кусок HTML.</summary>
    private static async Task<string> ErrorTextAsync(HttpResponseMessage response, CancellationToken ct,
        ServerNode? target = null, string path = "")
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(ct);
            if (problem?.Detail is { Length: > 0 } detail)
            {
                return detail;
            }
            if (problem?.Title is { Length: > 0 } title)
            {
                return title;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // ответ не ProblemDetails — покажем код
        }
        var code = (int)response.StatusCode;
        var where = target is null ? "" : " " + Where(target);
        var request = path.Length > 0 ? Loc.T("msg.clusterClient.5", path) : "";
        return Loc.T("msg.clusterClient.6", where, code, response.ReasonPhrase, request) + Advice(code);
    }

    /// <summary>
    /// ЧТО ЗНАЧИТ ОТВЕТ БЕЗ ОБЪЯСНЕНИЯ (T-160). Ответы 502/503/504 приходят не от AI2P —
    /// у него на каждый отказ есть текст (ProblemDetails). Так отвечает то, что стоит ПЕРЕД
    /// сервером: IIS, nginx, туннель хостера. Значит, до самого AI2P запрос не дошёл —
    /// приложение остановлено, перезапускается или не ответило прокси вовремя.
    /// </summary>
    private static string Advice(int code) => code switch
    {
        502 or 503 or 504 =>
            Loc.T("msg.clusterClient.7"),
        413 => Loc.T("msg.clusterClient.8"),
        _ => "",
    };

    /// <summary>
    /// Обрыв связи, а не отказ сервера: только такие ошибки превращаются в объяснение
    /// «до сервера не дозвониться» (T-153). Остановка приложения не в счёт — отменённый
    /// нами же запрос обязан остаться отменой, иначе выключение выглядело бы поломкой связи.
    /// </summary>
    private static bool IsNetwork(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException;

    /// <summary>
    /// ДО СЕРВЕРА НЕ ДОЗВОНИТЬСЯ (T-153). Раньше исключение уезжало наружу как есть, и человек
    /// видел в списке серверов «An error occurred while sending the request.»: ни адреса, ни
    /// причины, ни что с этим делать. Настоящая причина у <see cref="HttpRequestException"/>
    /// лежит во ВЛОЖЕННОМ исключении («No such host is known», «Connection refused»), поэтому
    /// цепочка разворачивается до самого внутреннего сообщения.
    /// </summary>
    private static InvalidOperationException Unreachable(Exception ex, ServerNode target, string path) =>
        new(Explain(ex, target, path), ex);

    /// <inheritdoc cref="Unreachable"/>
    private static string Explain(Exception ex, ServerNode target, string path)
    {
        var where = Where(target);
        // ОТКАЗ ПО СЕРТИФИКАТУ ЛЕЧИТСЯ ОДНИМ ФЛАЖКОМ (T-315), но текст .NET («The remote
        // certificate is invalid because of errors in the certificate chain: UntrustedRoot»)
        // об этом не говорит ни слова, и человек ищет беду в сети. Подсказку даём только
        // когда доверие ещё не включено: иначе она сбивала бы с толку
        var advice = Loc.T("msg.clusterClient.9")
            + (IsCertificateRefusal(ex) && !HttpsPeers.TrustAny ? " " + Loc.T("msg.clusterClient.14") : "");
        return ex switch
        {
            TaskCanceledException or OperationCanceledException =>
                Loc.T("msg.clusterClient.10", where, path) + advice,
            HttpRequestException or System.Net.Sockets.SocketException =>
                Loc.T("msg.clusterClient.11", where, Reason(ex)) + advice,
            _ => ex.Message,
        };
    }

    /// <summary>Связь оборвалась именно ПРОВЕРКОЙ СЕРТИФИКАТА соседа (T-315): такую беду
    /// SslStream объявляет <see cref="System.Security.Authentication.AuthenticationException"/>
    /// где-то в глубине цепочки — по тексту сообщения её узнавать нельзя, он от языка ОС.</summary>
    private static bool IsCertificateRefusal(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is System.Security.Authentication.AuthenticationException)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Как назвать сервер в сообщении об ошибке: имя и АДРЕСА, по которым мы его
    /// звали, — с T-50-S0 их бывает два, и человеку важно знать, что перебраны оба.</summary>
    private static string Where(ServerNode target) => target.HasAddress2
        ? $"«{target.Name}» ({target.Url}, {target.Url2})"
        : $"«{target.Name}» ({target.Url})";

    /// <summary>Самое внутреннее сообщение цепочки исключений: у HttpRequestException своё
    /// («An error occurred while sending the request») не говорит ничего.</summary>
    private static string Reason(Exception ex)
    {
        var reason = ex;
        while (reason.InnerException is { } inner)
        {
            reason = inner;
        }
        return reason.Message.Trim().TrimEnd('.');
    }

    public void Dispose()
    {
        _http.Dispose();
        _repl.Dispose();
    }
}
