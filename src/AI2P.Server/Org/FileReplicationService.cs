using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage;

namespace AI2P.Server.Org;

/// <summary>
/// РЕПЛИКАЦИЯ ФАЙЛОВ (ТЗ гл. 6, п. 7 плана; этап 44).
///
/// Идёт в ТОМ ЖЕ сеансе, что и репликация базы, но ПОСЛЕ неё (todo44): пока она не кончилась,
/// отсчёт до следующей репликации не запускается, а её ход виден в поле статуса сервера.
///
/// Реплицируются два вида каталогов:
/// <list type="bullet">
/// <item><b>каталог данных организации</b> — описания задач, критерии, артефакты, профайлы;
/// без корзины, журналов запросов к ИИ и самой БД (она едет журналом изменений строк);</item>
/// <item><b>папка <c>Common</c> проекта</b> — путь внутри каталога проекта, свой на каждом
/// сервере. Не заполнена хоть с одной стороны — не запускается. Внутри действует фильтр
/// <c>.repignore</c> с синтаксисом <c>.gitignore</c>.</item>
/// </list>
/// Каталог проекта целиком не реплицируется — он выравнивается git'ом или иными средствами.
///
/// Сравнение ТРЁХСТОРОННЕЕ: своё, чужое и <b>база сравнения</b> — снимок последнего согласия
/// (<see cref="AI2P.Storage.Services.FileSyncStateService"/>). Только она позволяет отличить
/// «изменился у меня» от «изменился у него», а значит и обнаружить конфликт: файл, изменённый
/// за интервал репликации на ДВУХ серверах, помечается конфликтным и не реплицируется вовсе,
/// пока человек не выберет одно из четырёх решений (todo44).
/// </summary>
public sealed class FileReplicationService
{
    /// <summary>Кусок переноса: 4 МБ. Меньше — лишние запросы на гигабайтных медиа,
    /// больше — грубее прогресс и дороже повтор после обрыва.</summary>
    public const int ChunkSize = 4 << 20;

    private readonly OrgRegistry _registry;
    private readonly ClusterClient _client;

    public FileReplicationService(OrgRegistry registry, ClusterClient client)
    {
        _registry = registry;
        _client = client;
    }

    /// <summary>
    /// Перенести файлы организации с партнёром: сначала каталог данных, потом папки
    /// <c>Common</c> всех проектов, где они настроены с обеих сторон.
    /// </summary>
    /// <returns>Файлы, которые перенести не удалось (T-50-S0): сеанс из-за них не прерывается,
    /// но сообщить о них человеку надо — их список уходит в состояние пары.</returns>
    public async Task<List<string>> RunAsync(ServerNode target, string token, Organization org,
        OrgContext context, ReplicationState state, ReplicationProgress progress,
        CancellationToken ct)
    {
        // левая сторона конфликта — всегда дирижёр (todo44): пара выглядит одинаково,
        // на каком бы из двух серверов её ни открыли
        var weAreConductor = context.IsConductor;
        progress.Phase = ReplPhases.FilesOrg;
        progress.Total = 1;
        progress.Done = 0;

        var roots = new List<Root>
        {
            new(ReplScopes.OrgFiles, org.Id, ProjectId: "", context.Files.DataDir,
                SkipService: true, RepIgnore.Empty),
        };
        // папка Common — своя у каждого проекта и своя на каждом сервере (ТЗ гл. 6, этап 42)
        foreach (var project in context.Projects.List())
        {
            var root = CommonRoot(project);
            if (root is null)
            {
                continue;   // «если поле common не заполнено, то не запускается» (todo44)
            }
            roots.Add(new Root(ReplScopes.CommonFiles, project.Id, project.Id, root,
                SkipService: false, RepIgnore.Load(root)));
        }

        var failed = new List<string>();
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            progress.Phase = root.Scope == ReplScopes.CommonFiles ? ReplPhases.FilesCommon : ReplPhases.FilesOrg;
            failed.AddRange(await SyncRootAsync(target, token, org, context, state, progress, root,
                weAreConductor, ct));
        }
        return failed;
    }

    /// <summary>Корень папки <c>Common</c> проекта на ЭТОМ сервере; null — не настроена.</summary>
    private static string? CommonRoot(Project project)
    {
        if (project.FolderPath.Trim().Length == 0 || project.CommonPath.Trim().Length == 0)
        {
            return null;
        }
        var full = FileManifest.Resolve(project.FolderPath.Trim(), project.CommonPath.Trim());
        return full is not null && Directory.Exists(full) ? full : null;
    }

    /// <summary>
    /// Перенести ОДИН каталог (T-47-S0) — им пользуется репликация архивов: у архива свой
    /// корень файлов, и заводить ради него отдельный проход по всей организации незачем.
    /// </summary>
    public Task<List<string>> SyncOneAsync(ServerNode target, string token, Organization org,
        OrgContext context, ReplicationState state, ReplicationProgress progress, Root root,
        CancellationToken ct) =>
        SyncRootAsync(target, token, org, context, state, progress, root, context.IsConductor, ct);

    private async Task<List<string>> SyncRootAsync(ServerNode target, string token, Organization org,
        OrgContext context, ReplicationState state, ReplicationProgress progress, Root root,
        bool weAreConductor, CancellationToken ct)
    {
        var scopeKey = AI2P.Storage.Services.FileSyncStateService.ScopeKey(root.Scope, root.Key);
        var remote = await _client.ReplFileManifestAsync(target, token, org.Code, root.Scope, root.Key, ct);
        if (remote.NotConfigured)
        {
            return [];   // у партнёра папка Common не настроена — реплицировать нечего (todo44)
        }
        var sync = _registry.FileSync;
        // решения по конфликтам применяются ПЕРЕД сверкой (они правят базу сравнения)
        // и действуют один сеанс (todo44)
        await ApplyResolutionsAsync(target, token, org, context, root, scopeKey, weAreConductor, ct);
        var baseline = sync.Baseline(target.Id, scopeKey);
        var local = Local(root, baseline);
        var remoteByPath = remote.Items.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var localByPath = local.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        paths.UnionWith(localByPath.Keys);
        paths.UnionWith(remoteByPath.Keys);
        paths.UnionWith(baseline.Keys);
        // СЛУЖЕБНЫЕ ПУТИ НЕ СВЕРЯЮТСЯ ВООБЩЕ (T-203-S0). Свой манифест их и не содержит,
        // но называет их ПАРТНЁР — и партнёр прежней версии назовёт то, чего у нас уже нет
        // (пути внутри «arc»). Сверка приняла бы это за «у меня удалили» и вычистила бы
        // архивы у него. Заодно из базы сравнения выбрасываются пути, накопленные до этой
        // версии: иначе они остались бы там навсегда
        //
        // ТУДА ЖЕ — ФАЙЛЫ, КОТОРЫЕ КАЖДЫЙ СЕРВЕР ПИШЕТ СЕБЕ САМ (T-261-S0): профайлы и
        // workflow записей справочника дистрибутива, справочник пакетов, манифесты плагинов
        // и наборы опыта (см. FileManifest.IsDistributionFile). Они расходятся у двух серверов
        // ровно на время, пока версии программы у них разные, и давали конфликт, в котором
        // человеку нечего выбирать
        List<string> dropped = root.SkipService
            ? paths.Where(p => FileManifest.IsServicePath(p) || FileManifest.IsDistributionFile(p)).ToList()
            : [];
        paths.ExceptWith(dropped);
        ForgetConflicts(org, target, root, scopeKey, dropped);

        // прогресс считаем в байтах: у медиа один файл весит больше всех остальных вместе
        var plan = new List<Step>();
        foreach (var path in paths)
        {
            var step = Decide(path, localByPath.GetValueOrDefault(path), remoteByPath.GetValueOrDefault(path),
                baseline.GetValueOrDefault(path));
            if (step is not null)
            {
                plan.Add(step);
            }
        }
        progress.Total = Math.Max(1, progress.Total + plan.Sum(s => Math.Max(1, s.Bytes)));

        var settled = new List<FileEntry>();
        var forgotten = new List<string>(dropped);
        var failed = new List<string>();
        foreach (var step in plan)
        {
            ct.ThrowIfCancellationRequested();
            // ОДИН ФАЙЛ НЕ ДОЛЖЕН ОСТАНАВЛИВАТЬ ВЕСЬ ОБМЕН (T-50-S0, повторный заход).
            // Прежде любая беда с одним файлом (его удалили под нами, он занят чужой
            // программой, отказано в доступе, пустой файл — см. PullFileAsync) вылетала
            // наружу и рвала сеанс целиком: не доезжали ни остальные файлы, ни архивы,
            // ни закрытие сеанса. Причём НАВСЕГДА — следующий сеанс упирался в тот же файл.
            // Теперь такой файл пропускается: он не попадает ни в базу сравнения, ни в
            // «забытые», значит следующий сеанс попробует его снова, а человек видит
            // список неудач в состоянии пары
            try
            {
                await ApplyStepAsync(target, token, org, state, root, scopeKey, step,
                    weAreConductor, settled, forgotten, ct);
            }
            catch (OperationCanceledException)
            {
                throw;   // приложение останавливается либо сеанс отменён человеком
            }
            catch (Exception ex)
            {
                failed.Add(step.Path);
                Serilog.Log.Warning(ex, "Репликация {Org} ↔ {Server}: файл «{Path}» перенести не "
                    + "удалось ({Error}) — пропущен, сеанс продолжается (T-50-S0)",
                    org.Code, target.Name, step.Path, ex.Message);
            }
            progress.Done += Math.Max(1, step.Bytes);
        }
        if (settled.Count > 0)
        {
            sync.RememberAll(target.Id, scopeKey, settled);
        }
        if (forgotten.Count > 0)
        {
            sync.Forget(target.Id, scopeKey, forgotten);
        }
        return failed;
    }

    /// <summary>
    /// Снять УЖЕ ЗАПИСАННЫЕ конфликты по путям, которые больше не сверяются (T-261-S0).
    /// Без этого конфликт, записанный прежней версией на <c>models/profile_…json</c>, висел бы
    /// в списке вечно: сверка такой путь теперь не смотрит, а значит и не закроет его сама,
    /// и человеку остался бы выбор из четырёх решений, ни одно из которых ничего не делает.
    /// </summary>
    private void ForgetConflicts(Organization org, ServerNode target, Root root, string scopeKey,
        List<string> dropped)
    {
        if (dropped.Count == 0)
        {
            return;
        }
        var gone = new HashSet<string>(dropped, StringComparer.Ordinal);
        foreach (var conflict in _registry.FileSync.Conflicts(target.Id, org.Id)
                     .Where(c => c.Scope == root.Scope && gone.Contains(c.Path)))
        {
            _registry.FileSync.Clear(org.Id, target.Id, scopeKey, conflict.Path);
        }
    }

    /// <summary>Выполнить ОДИН шаг плана переноса. Вынесено отдельно ради обработки неудачи:
    /// файл, который не удалось перенести, пропускается, а сеанс идёт дальше (T-50-S0).</summary>
    private async Task ApplyStepAsync(ServerNode target, string token, Organization org,
        ReplicationState state, Root root, string scopeKey, Step step,
        bool weAreConductor, List<FileEntry> settled, List<string> forgotten, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case StepKind.Pull:
                await PullFileAsync(target, token, org, root, step.Remote!, ct);
                state.FilesReceived++;
                state.BytesReceived += step.Remote!.Size;
                settled.Add(step.Remote);
                break;
            case StepKind.Push:
                await PushFileAsync(target, token, org, root, step.Local!, ct);
                state.FilesSent++;
                state.BytesSent += step.Local!.Size;
                settled.Add(step.Local);
                break;
            case StepKind.DeleteLocal:
                DeleteLocal(root, step.Path);
                forgotten.Add(step.Path);
                break;
            case StepKind.DeleteRemote:
                await _client.ReplFileDeleteAsync(target, token, org.Code, root.Scope, root.Key,
                    step.Path, ct);
                forgotten.Add(step.Path);
                break;
            case StepKind.Settle:
                settled.Add(step.Local ?? step.Remote!);
                break;
            case StepKind.Forget:
                forgotten.Add(step.Path);
                break;
            case StepKind.Conflict:
                // конфликтный файл НЕ реплицируется (todo44) — только записывается
                var left = weAreConductor ? step.Local! : step.Remote!;
                var right = weAreConductor ? step.Remote! : step.Local!;
                _registry.FileSync.Record(org.Id, target.Id, root.Scope, scopeKey, root.ProjectId,
                    step.Path, left, right);
                break;
        }
        if (step.Kind != StepKind.Conflict)
        {
            _registry.FileSync.Clear(org.Id, target.Id, scopeKey, step.Path);
        }
    }

    /// <summary>Местный манифест с переиспользованием известных хэшей: считать sha256
    /// гигабайтного файла каждый сеанс нельзя (см. <see cref="FileManifest"/>).</summary>
    private static List<FileEntry> Local(Root root, Dictionary<string, FileEntry> baseline)
    {
        var entries = FileManifest.Scan(root.Path, root.Ignore, root.SkipService);
        FileManifest.FillHashes(root.Path, entries, (path, size, mtime) =>
            baseline.TryGetValue(path, out var known) && known.Size == size && known.Mtime == mtime
                ? known.Hash
                : null);
        return entries;
    }

    /// <summary>
    /// Трёхстороннее сравнение одного пути. Правило простое: менялась одна сторона — переносим
    /// в другую; менялись обе и содержимое разошлось — конфликт; не менялся никто — ничего.
    /// </summary>
    private static Step? Decide(string path, FileEntry? local, FileEntry? remote, FileEntry? based)
    {
        var localChanged = local is null ? based is not null : local.Hash != based?.Hash;
        var remoteChanged = remote is null ? based is not null : remote.Hash != based?.Hash;

        if (!localChanged && !remoteChanged)
        {
            // обе стороны совпадают с базой сравнения; если базы не было, а файлы одинаковы —
            // просто запоминаем согласие
            return local is not null && remote is not null && based is null
                ? new Step(StepKind.Settle, path, local, remote, 0)
                : null;
        }
        if (localChanged && !remoteChanged)
        {
            return local is null
                ? new Step(StepKind.DeleteRemote, path, null, remote, 0)
                : new Step(StepKind.Push, path, local, remote, local.Size);
        }
        if (!localChanged)
        {
            return remote is null
                ? new Step(StepKind.DeleteLocal, path, local, null, 0)
                : new Step(StepKind.Pull, path, local, remote, remote.Size);
        }
        // менялись обе стороны
        if (local is null && remote is null)
        {
            return new Step(StepKind.Forget, path, null, null, 0);   // удалили оба — согласие
        }
        if (local is not null && remote is not null && local.Hash == remote.Hash)
        {
            return new Step(StepKind.Settle, path, local, remote, 0);   // пришли к одному сами
        }
        // один удалил, другой изменил — тоже конфликт: молча стирать чужую работу нельзя
        return new Step(StepKind.Conflict, path, local ?? Missing(path), remote ?? Missing(path), 0);
    }

    /// <summary>Отсутствующая сторона конфликта: нулевой размер и пустой хэш —
    /// в списке конфликтов это видно как «файла нет».</summary>
    private static FileEntry Missing(string path) => new() { Path = path, Size = 0, Mtime = "", Hash = "" };

    // --- решения по конфликтам (todo44) ---

    /// <summary>
    /// Применить выбранные человеком решения — перед сверкой и ровно на один сеанс.
    /// «Принять сторону» = стереть базу сравнения для этого файла и позволить победителю
    /// доехать обычным порядком; «переименовать» = дать проигравшей копии суффикс кода
    /// её сервера, после чего обе версии живут рядом и обе реплицируются.
    /// </summary>
    private async Task ApplyResolutionsAsync(ServerNode target, string token, Organization org,
        OrgContext context, Root root, string scopeKey, bool weAreConductor, CancellationToken ct)
    {
        var conflicts = _registry.FileSync.Conflicts(target.Id, org.Id)
            .Where(c => c.Resolution.Length > 0 && c.Scope == root.Scope
                        && (root.Scope != ReplScopes.CommonFiles || c.ProjectId == root.ProjectId))
            .ToList();
        foreach (var conflict in conflicts)
        {
            ct.ThrowIfCancellationRequested();
            var ourCode = context.Scope.Code.Length > 0 ? context.Scope.Code : "S";
            var peerCode = context.Scope.CodeOf(target.Id) is { Length: > 0 } code ? code : "S?";
            switch (conflict.Resolution)
            {
                case ReplFileResolutions.RenameLeft:
                    // левая сторона — дирижёр: переименовывает тот, кто им является
                    await RenameAsync(target, token, org, root, conflict.Path,
                        mine: weAreConductor, ownerCode: weAreConductor ? ourCode : peerCode, ct);
                    // база сравнения по этому пути забывается: под старым именем осталась
                    // ровно одна версия — победитель, и сверка должна увидеть её как обычный
                    // новый файл, а не как «у одного удалили, у другого изменили»
                    _registry.FileSync.Forget(target.Id, scopeKey, [conflict.Path]);
                    break;
                case ReplFileResolutions.RenameRight:
                    await RenameAsync(target, token, org, root, conflict.Path,
                        mine: !weAreConductor, ownerCode: weAreConductor ? peerCode : ourCode, ct);
                    _registry.FileSync.Forget(target.Id, scopeKey, [conflict.Path]);
                    break;
                default:
                    // «принять левый/правый»: в базу сравнения кладём ПРОИГРАВШУЮ версию —
                    // тогда обычная сверка увидит «изменился только победитель» и перенесёт
                    // его копию поверх. Отдельного механизма перезаписи не нужно
                    var winnerIsLeft = conflict.Resolution == ReplFileResolutions.Left;
                    var loserIsConductorSide = !winnerIsLeft;
                    _registry.FileSync.Remember(target.Id, scopeKey, new FileEntry
                    {
                        Path = conflict.Path,
                        Size = loserIsConductorSide ? conflict.LeftSize : conflict.RightSize,
                        Mtime = loserIsConductorSide ? conflict.LeftMtime : conflict.RightMtime,
                        Hash = loserIsConductorSide ? conflict.LeftHash : conflict.RightHash,
                    });
                    break;
            }
            // решение действует ОДИН сеанс (todo44)
            _registry.FileSync.Clear(org.Id, target.Id, scopeKey, conflict.Path);
        }
    }

    /// <summary>Переименовать проигравшую копию, добавив к имени суффикс кода её сервера.</summary>
    private async Task RenameAsync(ServerNode target, string token, Organization org, Root root,
        string path, bool mine, string ownerCode, CancellationToken ct)
    {
        var renamed = WithSuffix(path, ownerCode);
        if (mine)
        {
            var from = FileManifest.Resolve(root.Path, path);
            var to = FileManifest.Resolve(root.Path, renamed);
            if (from is not null && to is not null && File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to, overwrite: true);
            }
            return;
        }
        await _client.ReplFileRenameAsync(target, token, org.Code, root.Scope, root.Key, path, renamed, ct);
    }

    /// <summary>«render.mp4» + «S1» → «render~S1.mp4»: обе версии живут рядом и видно, чья.</summary>
    public static string WithSuffix(string path, string code)
    {
        var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var renamed = $"{name}~{code}{ext}";
        return dir.Length > 0 ? dir + "/" + renamed : renamed;
    }

    // --- перенос одного файла ---

    /// <summary>Забрать файл у партнёра кусками с докачкой; время изменения выставляется
    /// таким же, как у источника, — иначе следующий сеанс счёл бы файл изменившимся у обоих.</summary>
    private async Task PullFileAsync(ServerNode target, string token, Organization org, Root root,
        FileEntry entry, CancellationToken ct)
    {
        var full = FileManifest.Resolve(root.Path, entry.Path)
                   ?? throw new InvalidOperationException(Loc.T("msg.fileReplication.1", entry.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var part = full + ".ai2p-part";
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > entry.Size)
        {
            File.Delete(part);
            have = 0;
        }
        // ПУСТОЙ ФАЙЛ (T-50-S0, повторный заход). Файл нулевой длины — обычное дело: пустое
        // описание задачи, заготовка критериев приёмки. Цикл докачки для него не выполняется
        // НИ РАЗУ (have = 0 = Size), временного файла никто не заводит, и File.Move падал
        // FileNotFoundException. Исключение рвало ВЕСЬ сеанс репликации — и файлы, и архивы,
        // и закрытие сеанса, — то есть один пустой файл у партнёра останавливал обмен насовсем
        if (!File.Exists(part))
        {
            File.WriteAllBytes(part, []);
        }
        while (have < entry.Size)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = await _client.ReplFileReadAsync(target, token, org.Code, root.Scope, root.Key,
                entry.Path, have, ChunkSize, ct);
            if (chunk.Length == 0)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.fileReplication.2", entry.Path, have));
            }
            await using (var stream = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(chunk, ct);
            }
            have += chunk.Length;
        }
        File.Move(part, full, overwrite: true);
        SetMtime(full, entry.Mtime);
    }

    /// <summary>Отдать файл партнёру кусками с докачкой (offset берётся у него же).</summary>
    private async Task PushFileAsync(ServerNode target, string token, Organization org, Root root,
        FileEntry entry, CancellationToken ct)
    {
        var full = FileManifest.Resolve(root.Path, entry.Path);
        if (full is null || !File.Exists(full))
        {
            return;   // файл исчез между обходом и переносом — не беда, поймаем в следующий раз
        }
        var offset = await _client.ReplFilePartAsync(target, token, org.Code, root.Scope, root.Key,
            entry.Path, ct);
        if (offset > entry.Size)
        {
            offset = 0;
        }
        await using var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        source.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[ChunkSize];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, ct);
            var last = offset + read >= entry.Size;
            if (read == 0 && !last)
            {
                break;   // файл укоротили под нами
            }
            await _client.ReplFileWriteAsync(target, token, org.Code, root.Scope, root.Key, entry.Path,
                offset, last, entry.Mtime, buffer.AsMemory(0, read), ct);
            offset += read;
            if (last)
            {
                break;
            }
        }
    }

    private static void DeleteLocal(Root root, string path)
    {
        var full = FileManifest.Resolve(root.Path, path);
        if (full is not null && File.Exists(full))
        {
            File.Delete(full);
        }
    }

    /// <summary>Выставить время изменения как у источника: по нему обе стороны потом узнают
    /// файл без пересчёта sha256.</summary>
    public static void SetMtime(string path, string mtime)
    {
        if (DateTime.TryParse(mtime, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            try
            {
                File.SetLastWriteTimeUtc(path, parsed.ToUniversalTime());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // не смогли — переживём: в следующий раз файл сверится по sha256
            }
        }
    }

    // --- ПАССИВНАЯ сторона сеанса: сюда ходит партнёр (раздел /api/cluster/repl/files) ---

    /// <summary>
    /// Корень реплицируемого каталога по имени и ключу; null — каталога нет или папка
    /// <c>Common</c> здесь не настроена (тогда репликация этого каталога не запускается).
    /// </summary>
    public Root? RootOf(Organization org, string scope, string key)
    {
        var context = _registry.Context(org);
        if (scope == ReplScopes.OrgFiles)
        {
            return new Root(scope, org.Id, "", context.Files.DataDir, SkipService: true, RepIgnore.Empty);
        }
        // ФАЙЛЫ АРХИВА (T-47-S0): ключом служит идентификатор архива, а корнем — подкаталог
        // projects внутри каталога архива. Каталог заводится, только если архив ТЕКУЩИЙ:
        // реплицируется ровно он, а нетекущий, которого здесь нет, никто заводить не должен.
        // Закрытый .zip'ом архив тоже не трогаем — состояние выбрал человек
        if (scope == ReplScopes.ArchiveFiles)
        {
            var archive = context.Archives.Get(key);
            if (archive is null || context.Archives.DbForReplication(archive) is null)
            {
                return null;
            }
            return new Root(scope, key, "", context.Archives.ProjectsDirOf(archive.Code),
                SkipService: false, RepIgnore.Empty);
        }
        if (scope != ReplScopes.CommonFiles)
        {
            return null;
        }
        var project = context.Projects.Get(key);
        var root = project is null ? null : CommonRoot(project);
        return root is null
            ? null
            : new Root(scope, key, key, root, SkipService: false, RepIgnore.Load(root));
    }

    /// <summary>Манифест своего каталога — с переиспользованием известных хэшей.</summary>
    public List<FileEntry> Manifest(Root root)
    {
        // партнёр у пассивной стороны не один, а база сравнения — пер-партнёрская; хэши
        // переиспользуем из ЛЮБОЙ известной записи с тем же размером и временем: содержимое
        // от того, с кем мы сверялись в прошлый раз, не зависит
        var scopeKey = AI2P.Storage.Services.FileSyncStateService.ScopeKey(root.Scope, root.Key);
        var known = _registry.FileSync.KnownHashes(scopeKey);
        var entries = FileManifest.Scan(root.Path, root.Ignore, root.SkipService);
        FileManifest.FillHashes(root.Path, entries,
            (path, size, mtime) => known.GetValueOrDefault(path + "|" + size + "|" + mtime));
        return entries;
    }

    /// <summary>Сколько байт файла уже принято (точка докачки).</summary>
    public long PartLength(Organization org, string scope, string key, string path)
    {
        var full = Target(org, scope, key, path);
        var part = full + ".ai2p-part";
        return File.Exists(part) ? new FileInfo(part).Length : 0;
    }

    /// <summary>Отдать кусок файла партнёру.</summary>
    public byte[] ReadChunk(Organization org, string scope, string key, string path, long offset, int length)
    {
        var full = Target(org, scope, key, path);
        if (!File.Exists(full))
        {
            return [];
        }
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset >= stream.Length)
        {
            return [];
        }
        stream.Seek(offset, SeekOrigin.Begin);
        var size = (int)Math.Min(Math.Clamp(length, 1, ChunkSize), stream.Length - offset);
        var buffer = new byte[size];
        var read = stream.Read(buffer, 0, size);
        return read == size ? buffer : buffer[..read];
    }

    /// <summary>Принять кусок файла; последний закрывает файл и выставляет время источника.</summary>
    public void WriteChunk(Organization org, string scope, string key, string path, long offset,
        bool last, string mtime, byte[] chunk)
    {
        var full = Target(org, scope, key, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var part = full + ".ai2p-part";
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset == 0 && have > 0)
        {
            File.Delete(part);
            have = 0;
        }
        if (offset != have)
        {
            throw new ArgumentException(
                Loc.T("msg.fileReplication.3", path, offset, have));
        }
        using (var stream = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            stream.Write(chunk, 0, chunk.Length);
        }
        if (last)
        {
            File.Move(part, full, overwrite: true);
            SetMtime(full, mtime);
        }
    }

    public void Delete(Organization org, string scope, string key, string path)
    {
        var full = Target(org, scope, key, path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    public void Rename(Organization org, string scope, string key, string path, string to)
    {
        var from = Target(org, scope, key, path);
        var target = Target(org, scope, key, to);
        if (!File.Exists(from))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(from, target, overwrite: true);
    }

    /// <summary>Абсолютный путь внутри реплицируемого каталога с проверкой выхода наружу
    /// (ТЗ гл. 12): пути называет партнёр, и доверять им нельзя.</summary>
    private string Target(Organization org, string scope, string key, string path)
    {
        var root = RootOf(org, scope, key)
                   ?? throw new ArgumentException(Loc.T("msg.fileReplication.4"));
        // СЛУЖЕБНЫЙ ПУТЬ ПАРТНЁРУ НЕ ОТДАЁТСЯ И ОТ НЕГО НЕ ПРИНИМАЕТСЯ (T-203-S0): корзина,
        // журналы запросов к ИИ, архивы и сама база в нашем манифесте не значатся, и просить
        // о них может только партнёр прежней версии — а он этим записал бы файл в чужой
        // архив или снёс бы его
        if (root.SkipService && FileManifest.IsServicePath(path))
        {
            throw new ArgumentException(Loc.T("msg.fileReplication.5", path));
        }
        return FileManifest.Resolve(root.Path, path)
               ?? throw new ArgumentException(Loc.T("msg.fileReplication.5", path));
    }

    // --- описание одного реплицируемого каталога ---

    /// <summary>Один реплицируемый каталог: каталог данных организации либо папка
    /// <c>Common</c> проекта на ЭТОМ сервере.</summary>
    public sealed record Root(string Scope, string Key, string ProjectId, string Path,
        bool SkipService, RepIgnore Ignore);

    private enum StepKind
    {
        Pull, Push, DeleteLocal, DeleteRemote, Settle, Forget, Conflict,
    }

    private sealed record Step(StepKind Kind, string Path, FileEntry? Local, FileEntry? Remote, long Bytes);
}
