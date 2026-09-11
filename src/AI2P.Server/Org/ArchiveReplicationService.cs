using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Api;
using AI2P.Storage;

namespace AI2P.Server.Org;

/// <summary>
/// РЕПЛИКАЦИЯ АРХИВОВ (T-47-S0, выпуск 1.105).
///
/// Правила ТЗ, которые здесь исполняются:
/// <list type="bullet">
/// <item>текущий архив реплицируется ТАК ЖЕ, как рабочая среда, — и только он: у архива своя
/// база ТОЙ ЖЕ схемы, значит и свой журнал изменений, и свои курсоры (вид обмена
/// <c>arc:&lt;id&gt;</c>), а файлы его проектов едут обычной репликацией файлов
/// (вид <c>arcdata</c>, ключ — идентификатор архива);</item>
/// <item>при создании нового текущего архива СНАЧАЛА прежний текущий досылает свои данные
/// в последний раз, и только ПОТОМ перестаёт быть текущим. Порядок обеспечивается тем, что
/// поручение о смене (<c>archive_handovers</c>) исполняется не в момент приезда строки,
/// а в самом конце сеанса — после того как оба архива обменялись данными;</item>
/// <item>открыт архив здесь или закрыт и удалён ли он отсюда — дело каждого сервера и
/// не реплицируется вовсе. Реплицируется только «есть / нет» (<c>archive_servers</c>),
/// потому что иначе неоткуда взять список серверов, откуда архив можно скачать;</item>
/// <item>удалённый здесь архив качается с соседа и приходит ВСЕГДА закрытым (.zip), даже
/// если у соседа он открыт.</item>
/// </list>
///
/// ПОЧЕМУ СМЕНА ТЕКУЩЕГО — ОТДЕЛЬНАЯ ТАБЛИЦА, а не новый вид строки в уже существующей
/// (наука T-263): сервер прежней версии разбирает незнакомый вид через else-ветку и делает
/// НЕ ТО, а строку незнакомой таблицы приёмник журнала пропускает молча
/// (<c>ChangeLog.ApplyRow</c>). Отметки об исполнении (<c>archive_handovers_applied</c>)
/// не реплицируются — образец <c>run_requests_applied</c>.
/// </summary>
public sealed class ArchiveReplicationService
{
    /// <summary>Кусок передачи .zip — тот же, что у репликации файлов.</summary>
    public const int ChunkSize = FileReplicationService.ChunkSize;

    private readonly OrgRegistry _registry;
    private readonly ClusterClient _client;
    private readonly FileReplicationService _files;

    public ArchiveReplicationService(OrgRegistry registry, ClusterClient client,
        FileReplicationService files)
    {
        _registry = registry;
        _client = client;
        _files = files;
    }

    // --- сеанс: что реплицируем и в каком порядке ---

    /// <summary>
    /// Архивы, которые едут в этом сеансе с этим партнёром, В ПОРЯДКЕ передачи.
    ///
    /// Обычно это один архив — текущий. Но если с прошлого сеанса с этим партнёром текущий
    /// сменился, первым идёт ПРЕЖНИЙ текущий (последний досыл), а вторым — новый. Досылается
    /// прежний ровно один — тот, что стоял перед последней сменой: архивы, переставшие быть
    /// текущими раньше, свои данные досылали тогда же.
    /// </summary>
    public List<Archive> Plan(OrgContext context, string peerId)
    {
        var pending = context.Archives.PendingHandovers(peerId);
        var order = new List<string>();
        if (pending.Count > 0)
        {
            var last = pending[^1];
            if (last.PrevArchiveId.Length > 0)
            {
                order.Add(last.PrevArchiveId);
            }
            order.Add(last.ArchiveId);
        }
        else if (context.Archives.Current() is { } current)
        {
            order.Add(current.Id);
        }
        var list = new List<Archive>();
        foreach (var id in order.Distinct(StringComparer.Ordinal))
        {
            if (context.Archives.Get(id) is { } archive)
            {
                list.Add(archive);
            }
        }
        return list;
    }

    /// <summary>
    /// Перенести архивы с партнёром: по каждому сначала база (журнал изменений), потом файлы
    /// проектов, и только в самом конце — исполнение поручений о смене текущего архива.
    /// Именно этот порядок и означает «сначала досылает, потом перестаёт быть текущим».
    /// </summary>
    public async Task RunAsync(ServerNode target, string token, Organization org, OrgContext context,
        ReplicationState state, ReplicationProgress progress, CancellationToken ct)
    {
        var pending = context.Archives.PendingHandovers(target.Id);
        foreach (var archive in Plan(context, target.Id))
        {
            ct.ThrowIfCancellationRequested();
            progress.Phase = ReplPhases.Archive;
            // ЗАКРЫТЫЙ .zip'ом архив данными не обменивается: состояние выбрал человек,
            // и распаковывать архив ради обмена нельзя
            var db = context.Archives.DbForReplication(archive);
            if (db is null)
            {
                continue;
            }
            await PullAsync(target, token, org, context, archive, db, progress, ct);
            await PushAsync(target, token, org, context, archive, db, progress, ct);
            progress.Phase = ReplPhases.ArchiveFiles;
            await _files.SyncOneAsync(target, token, org, context, state, progress,
                new FileReplicationService.Root(ReplScopes.ArchiveFiles, archive.Id, "",
                    context.Archives.ProjectsDirOf(archive.Code), SkipService: false, RepIgnore.Empty),
                ct);
            // архив у нас теперь есть — объявляем это соседям (archive_servers)
            context.Archives.RefreshPresence(archive.Id, archive.Code);
        }
        ApplyHandovers(context, target.Id, pending);
    }

    /// <summary>
    /// ИСПОЛНИТЬ поручения о смене текущего архива — последним шагом сеанса. Порядок
    /// поручений хронологический, поэтому текущим остаётся архив последнего из них.
    /// Идемпотентно: на сервере, где архив создавали, признак уже проставлен.
    /// </summary>
    public void ApplyHandovers(OrgContext context, string peerId, List<ArchiveHandover>? pending = null)
    {
        foreach (var handover in pending ?? context.Archives.PendingHandovers(peerId))
        {
            context.Archives.ApplyHandover(handover, peerId);
            Serilog.Log.Information("Архивы: текущим становится {Archive} (прежний {Prev}), "
                                    + "данные прежнего досланы партнёру {Peer} (T-47-S0)",
                handover.ArchiveId, handover.PrevArchiveId, peerId);
        }
    }

    // --- база архива: тот же журнал изменений, свой курсор ---

    private async Task PullAsync(ServerNode target, string token, Organization org, OrgContext context,
        Archive archive, Database db, ReplicationProgress progress, CancellationToken ct)
    {
        var scope = ReplScopes.ArchiveScope(archive.Id);
        var cursor = _registry.ReplState.ArchiveCursors(org.Id, target.Id, archive.Id).Pull;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _client.ReplChangesAsync(target, token, org.Code, scope, cursor,
                ReplicationService.BatchSize, ct);
            if (page.Items.Count == 0 && page.NextCursor <= cursor)
            {
                break;
            }
            // страж пропускающий: в архиве нет ни ключей API, ни справочника моделей —
            // спорить не о чем, данные архива уже не меняются
            ChangeLog.Apply(db, page.Items, (_, _, _) => true);
            cursor = page.NextCursor;
            _registry.ReplState.AdvanceArchive(org.Id, target.Id, archive.Id, "pull_cursor", cursor);
            progress.Done += page.Items.Count;
            if (page.Remaining <= 0)
            {
                break;
            }
        }
    }

    private async Task PushAsync(ServerNode target, string token, Organization org, OrgContext context,
        Archive archive, Database db, ReplicationProgress progress, CancellationToken ct)
    {
        var scope = ReplScopes.ArchiveScope(archive.Id);
        var cursor = _registry.ReplState.ArchiveCursors(org.Id, target.Id, archive.Id).Push;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            List<RowChange> items;
            using (var conn = db.Open())
            {
                items = ChangeLog.Read(conn, cursor, ReplicationService.BatchSize, target.Id);
            }
            if (items.Count == 0)
            {
                break;
            }
            var last = items[^1].Seq;
            await _client.ReplApplyAsync(target, token, new ReplApplyDto
            {
                OrgCode = org.Code,
                Scope = scope,
                Items = items,
                SourceCursor = last,
            }, ct);
            cursor = last;
            _registry.ReplState.AdvanceArchive(org.Id, target.Id, archive.Id, "push_cursor", cursor);
            progress.Done += items.Count;
        }
    }

    // --- СКАЧАТЬ АРХИВ С ДРУГОГО СЕРВЕРА (ТЗ выпуска 1.105) ---

    /// <summary>
    /// Скачать архив с названного сервера организации. Архив приходит ВСЕГДА закрытым —
    /// одним файлом <c>&lt;код&gt;.zip</c>, даже если у источника он открыт: состояние
    /// на каждом сервере своё, и наследовать чужое не за чем.
    ///
    /// Передача идёт кусками с докачкой, как у репликации файлов: архив бывает гигабайтным.
    /// </summary>
    public async Task DownloadAsync(Organization org, OrgContext context, string archiveId,
        string sourceServerId, CancellationToken ct = default)
    {
        var archive = context.Archives.Get(archiveId)
                      ?? throw new InvalidOperationException(Loc.T("msg.arc.2", archiveId));
        if (context.Archives.StateOf(archive.Code) != ArchiveStates.Deleted)
        {
            throw new InvalidOperationException(Loc.T("msg.arcrepl.1", archive.DisplayId));
        }
        var target = _registry.Servers.Get(sourceServerId)
                     ?? throw new InvalidOperationException(Loc.T("msg.replication.2"));
        var token = _registry.Servers.PeerToken(target.Id);
        if (token.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.replication.1", target.Name));
        }
        var pack = await _client.ArcPackAsync(target, token, org.Code, archive.Id, ct);
        if (pack.Missing || pack.Size <= 0)
        {
            throw new InvalidOperationException(Loc.T("msg.arcrepl.2", archive.DisplayId, target.Name));
        }
        Directory.CreateDirectory(context.Archives.ArcDir);
        var full = context.Archives.IncomingZipOf(archive.Code);
        var part = full + ".ai2p-part";
        try
        {
            var have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > pack.Size)
            {
                File.Delete(part);
                have = 0;
            }
            while (have < pack.Size)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = await _client.ArcReadAsync(target, token, org.Code, archive.Id, have,
                    ChunkSize, ct);
                if (chunk.Length == 0)
                {
                    throw new InvalidOperationException(
                        Loc.T("msg.arcrepl.3", archive.DisplayId, have));
                }
                await using (var stream = new FileStream(part, FileMode.Append, FileAccess.Write,
                                 FileShare.None))
                {
                    await stream.WriteAsync(chunk, ct);
                }
                have += chunk.Length;
            }
            File.Move(part, full, overwrite: true);
        }
        finally
        {
            // временный свёрток у источника снимаем ВСЕГДА, в том числе после обрыва:
            // иначе он останется лежать у него на диске навсегда
            try
            {
                await _client.ArcDoneAsync(target, token, org.Code, archive.Id, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                           or TaskCanceledException)
            {
                Serilog.Log.Warning("Архивы: не удалось снять временный свёрток архива {Archive} "
                                    + "у сервера {Server}: {Error}", archive.DisplayId, target.Name,
                    ex.Message);
            }
        }
        // архив пришёл ЗАКРЫТЫМ: отмечаем состояние и объявляем соседям, что он у нас есть
        context.Archives.RefreshPresence(archive.Id, archive.Code);
        Serilog.Log.Information("Архивы: архив {Archive} скачан с сервера {Server} "
                                + "и лежит закрытым ({Size} байт)", archive.DisplayId, target.Name,
            pack.Size);
    }
}
