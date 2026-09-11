using System.Collections.Concurrent;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;

namespace AI2P.Server.Org;

/// <summary>
/// ФАЙЛ, КОТОРОГО НА ЭТОМ СЕРВЕРЕ НЕТ, БЕРЁТСЯ У ТОГО, ГДЕ ОН ЛЕЖИТ (T-13-S0).
///
/// Задание выполняется там, где лежат файлы задачи (межсерверного запуска нет), и агент
/// кладёт созданное — картинки, отчёты — в ПАПКУ ПРОЕКТА своего сервера. Папка проекта
/// целиком не реплицируется намеренно (её выравнивают git'ом), а путь к ней свой на каждом
/// сервере. Поэтому картинка, положенная агентом в чат на S0, у соседа не открывалась
/// вовсе: ссылка есть, задача реплицировалась, а файла на этом диске нет.
///
/// Правило теперь такое: сервер, у которого файла нет, спрашивает его у ОСТАЛЬНЫХ серверов
/// организации по кластерному каналу (<c>/api/cluster/files/get</c>) и отдаёт браузеру
/// потоком. Файл при этом никуда не копируется — репликация папки проекта как была не
/// нужна, так и не нужна.
///
/// Чтобы не звонить всем при каждой картинке, ответ запоминается: кто отдал — на
/// <see cref="FoundTtl"/>, «ни у кого нет» — на <see cref="MissingTtl"/> (короче: файл
/// на соседе может появиться в любой момент — его туда только что положил агент).
///
/// Дальше первого шага запрос не идёт: партнёр отвечает ТОЛЬКО своим диском и сам ни у
/// кого не спрашивает — иначе два сервера пересылали бы вопрос друг другу по кругу.
/// </summary>
public sealed class RemoteFileService
{
    /// <summary>Сколько помним, у кого лежит файл.</summary>
    public static readonly TimeSpan FoundTtl = TimeSpan.FromMinutes(10);

    /// <summary>Сколько помним, что файла нет ни у кого.</summary>
    public static readonly TimeSpan MissingTtl = TimeSpan.FromSeconds(30);

    /// <summary>Сколько ждём ПОДКЛЮЧЕНИЯ к соседу. Отдельно от общего срока намеренно:
    /// сервер, которого нет в сети (выключен, за NAT), обязан отсекаться быстро — иначе
    /// каждая картинка страницы ждала бы его больше двадцати секунд (столько по умолчанию
    /// длится неудачное подключение в Windows), и карточка выглядела бы зависшей.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Ждём ответа партнёра: файл бывает и в гигабайты (видео медиа-модели),
    /// поэтому счёт идёт не на секунды. Сервером, которого нет в сети, занимается
    /// репликация — здесь он просто пропускается.</summary>
    private static readonly HttpClient Http = new(HttpsPeers.Handler(ConnectTimeout))
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>Сколько файлов помним. Память — подсказка, а не хранилище: упёрлись
    /// в предел — забываем всё и спрашиваем заново.</summary>
    private const int MaxHints = 5000;

    private readonly OrgRegistry _registry;

    /// <summary>Память «у кого лежит»; пустой ServerId — «ни у кого нет».</summary>
    private readonly ConcurrentDictionary<string, Hint> _hints = new(StringComparer.Ordinal);

    public RemoteFileService(OrgRegistry registry) => _registry = registry;

    /// <summary>
    /// Найти файл у соседей и начать его отдачу; null — ни один сервер организации файла
    /// не отдал (или соседей нет вовсе). Поток закрывает вызывающий — вместе с ответом.
    /// </summary>
    /// <param name="org">Организация запроса: у каждой свой состав серверов.</param>
    /// <param name="kind"><see cref="FileLinks.Raw"/> (каталог данных) либо
    /// <see cref="FileLinks.Project"/> (папка проекта).</param>
    /// <param name="projectId">Проект — только для файла папки проекта.</param>
    /// <param name="path">Путь файла в том же виде, в каком он стоит в ссылке.</param>
    public async Task<RemoteFile?> FetchAsync(Organization org, string kind, string projectId,
        string path, CancellationToken ct = default)
    {
        var key = Key(org.Id, kind, projectId, path);
        var hint = Hinted(key);
        if (hint is { ServerId.Length: 0 })
        {
            return null;   // недавно спрашивали всех — файла нет ни у кого
        }
        foreach (var node in Peers(org, hint?.ServerId))
        {
            var token = _registry.Servers.PeerToken(node.Id);
            if (token.Length == 0)
            {
                continue;   // токена для обращения к нему нет — подключение не завершено
            }
            var file = await AskAsync(node, token, org, kind, projectId, path, ct);
            if (file is not null)
            {
                Remember(key, node.Id, FoundTtl);
                return file;
            }
        }
        Remember(key, "", MissingTtl);
        return null;
    }

    /// <summary>Серверы организации, кроме нас самих; первым — тот, что отдавал файл
    /// в прошлый раз (обычно он же отдаст и сейчас).</summary>
    private List<ServerNode> Peers(Organization org, string? firstServerId)
    {
        var result = new List<ServerNode>();
        foreach (var link in _registry.Servers.ServersOf(org.Id, includeRequests: false))
        {
            if (link.Status != OrgServerStatus.Active || !link.IsActive)
            {
                continue;
            }
            var node = _registry.Servers.Get(link.ServerId);
            if (node is null || node.IsLocal || !node.IsActive)
            {
                continue;
            }
            // ЗВОНИТЬ ЕМУ НЕКУДА (T-141): сервер за NAT называет себя петлевым адресом, и
            // запрос ушёл бы НА ЭТОТ ЖЕ компьютер — к нам самим. Репликация такие пары
            // пропускает молча (ReplicationService.CannotCall), и файл у них берётся не
            // звонком, а копией из каталога данных (LinkedFileShare)
            if (ServerAddress.Unreachable(node, _registry.Servers.Local()) is not null)
            {
                continue;
            }
            if (node.Id == firstServerId)
            {
                result.Insert(0, node);
            }
            else
            {
                result.Add(node);
            }
        }
        return result;
    }

    /// <summary>Спросить файл у одного сервера; null — у него его нет либо он не ответил.
    /// Ошибка связи здесь НЕ показывается человеку и не красит запись сервера: этим занята
    /// репликация, а картинка в чате просто не нарисуется.</summary>
    private static async Task<RemoteFile?> AskAsync(ServerNode node, string token, Organization org,
        string kind, string projectId, string path, CancellationToken ct)
    {
        var url = $"{node.Url}/api/cluster/files/get" +
                  $"?org={Uri.EscapeDataString(org.Code)}&kind={Uri.EscapeDataString(kind)}" +
                  (projectId.Length > 0 ? $"&projectId={Uri.EscapeDataString(projectId)}" : "") +
                  $"&path={Uri.EscapeDataString(path)}";
        HttpResponseMessage? response = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(Ai2pHeaders.ServerToken, token);
            // ResponseHeadersRead: тело поедет потоком мимо памяти — файл бывает гигабайтным
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }
            return new RemoteFile(response, node.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or InvalidOperationException or UriFormatException
                                       or NotSupportedException)
        {
            response?.Dispose();
            return null;
        }
    }

    private static string Key(string orgId, string kind, string projectId, string path) =>
        $"{orgId}|{kind}|{projectId}|{path}";

    /// <summary>Запомнить ответ; переполнение памяти просто сбрасывает её целиком.</summary>
    private void Remember(string key, string serverId, TimeSpan ttl)
    {
        if (_hints.Count >= MaxHints)
        {
            _hints.Clear();
        }
        _hints[key] = new Hint(serverId, DateTime.UtcNow + ttl);
    }

    private Hint? Hinted(string key)
    {
        if (!_hints.TryGetValue(key, out var hint))
        {
            return null;
        }
        if (hint.Until > DateTime.UtcNow)
        {
            return hint;
        }
        _hints.TryRemove(key, out _);
        return null;
    }

    /// <summary>Память об одном файле: у кого он лежит (пусто — ни у кого) и до каких пор
    /// мы этому верим.</summary>
    private sealed record Hint(string ServerId, DateTime Until);
}

/// <summary>Файл, который отдаёт сосед по кластеру (T-13-S0): ответ живёт до тех пор, пока
/// его тело не переписано в свой ответ, поэтому закрывается он вызывающим.</summary>
public sealed class RemoteFile : IDisposable
{
    private readonly HttpResponseMessage _response;

    public RemoteFile(HttpResponseMessage response, string serverName)
    {
        _response = response;
        ServerName = serverName;
    }

    /// <summary>Имя сервера, отдавшего файл, — для журнала.</summary>
    public string ServerName { get; }

    /// <summary>Тип содержимого, как его назвал сосед; пусто — не назвал.</summary>
    public string ContentType => _response.Content.Headers.ContentType?.ToString() ?? "";

    /// <summary>Длина файла; null — сосед её не назвал.</summary>
    public long? Length => _response.Content.Headers.ContentLength;

    /// <summary>Переписать содержимое в свой ответ.</summary>
    public Task CopyToAsync(Stream target, CancellationToken ct = default) =>
        _response.Content.CopyToAsync(target, ct);

    public void Dispose() => _response.Dispose();
}
