using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ЗАЯВКИ НА ЗАПУСК ЗАДАЧИ С ДРУГОГО СЕРВЕРА (T-196).
///
/// Задание выполняется там, где лежит задача, — это правило кластера (ТЗ гл. 6) и меняться
/// оно не должно: на сервере-владельце и файлы проекта, и исполнитель. Но НАЖАТЬ «запустить»
/// человек должен уметь с любого сервера: до T-196 кнопка на чужой задаче просто отвечала
/// «задача правится на S1», и запуск приходилось идти делать на другой компьютер.
///
/// Решение — заявка вместо действия: строка едет владельцу обычной репликацией, и он
/// запускает задачу у себя (сторож оркестратора). Владение соблюдено буквально: заявку
/// пишет и удаляет её АВТОР, а владелец задачи только читает. То, что он уже отработал,
/// он помнит В СВОЕЙ таблице <c>run_requests_applied</c> — она не реплицируется.
///
/// Отметки живут дольше заявок нарочно: заявку автор убирает через сутки, а удаление едет
/// репликацией — без «долгой памяти» не доехавшее вовремя удаление воскресило бы запуск.
/// </summary>
public sealed class RunRequestService
{
    /// <summary>Сколько автор держит свою заявку, прежде чем убрать: сутки — с запасом на
    /// выключенный на ночь сервер-владелец (интервал репликации бывает в часы).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>Сколько владелец помнит, что заявку отработал: заведомо дольше её жизни,
    /// иначе запоздавшее удаление заявки означало бы повторный запуск.</summary>
    public static readonly TimeSpan MemoryTime = TimeSpan.FromDays(30);

    /// <summary>Таблица заявок на ЗАПУСК (T-196).</summary>
    private const string RunTable = "run_requests";

    /// <summary>
    /// Таблица заявок на ОСТАНОВКУ (T-263) — отдельная, и это не косметика.
    ///
    /// Сервер прежней версии разбирает <see cref="RunTable"/> по правилу «вид „иерархия“ —
    /// запустить иерархию, ЛЮБОЙ другой — запустить задачу»: заявка на остановку, приехав
    /// к нему репликацией, ЗАПУСТИЛА БЫ то, что просили остановить. А строку таблицы,
    /// которой у него нет, приёмник пропускает молча (ChangeLog.ApplyRow) — то есть старый
    /// партнёр остановку просто не выполнит, и это единственное безопасное поведение.
    /// </summary>
    private const string StopTable = "stop_requests";

    /// <summary>Где лежит заявка такого вида.</summary>
    private static string TableOf(string kind) =>
        RunRequestKinds.IsStop(kind) ? StopTable : RunTable;

    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public RunRequestService(Database db, EventStore events, ServerScope scope)
    {
        _db = db;
        _events = events;
        _scope = scope;
    }

    /// <summary>
    /// Подать заявку: «запусти (или останови) у себя вот эту задачу». Вызывается на сервере,
    /// которому задача НЕ принадлежит; на своей задаче заявка не нужна — там действуют сразу.
    /// </summary>
    /// <param name="kind"><see cref="RunRequestKinds"/>: одиночный запуск, вся иерархия либо
    /// остановка — задания, очереди иерархии или всего её поддерева (T-263).</param>
    /// <param name="withErrors">Иерархия: перезапускать и вставшие с ошибкой (T-186).</param>
    /// <param name="withNeedsFix">Иерархия: запускать и задачи в доработке (T-210).</param>
    public RunRequest Add(TaskItem task, string kind, bool withErrors, bool withNeedsFix, string? actorId)
    {
        var normalized = RunRequestKinds.Normalize(kind);
        var request = new RunRequest
        {
            TaskId = task.Id,
            Kind = normalized,
            WithErrors = withErrors,
            WithNeedsFix = withNeedsFix,
            RequestedBy = actorId,
            // владелец строки — АВТОР заявки; у дирижёра, как у всех бесхозных строк, сервер
            // не проставляется: право записи на них и так только у него (ТЗ гл. 6)
            ServerId = _scope.IsConductor ? null : _scope.ServerId,
            TargetServerId = task.ServerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        if (RunRequestKinds.IsStop(normalized))
        {
            // у остановки нет галочек переспроса — их и колонок у неё нет
            request.WithErrors = false;
            request.WithNeedsFix = false;
            Sql.Exec(conn, tx, """
                INSERT INTO stop_requests (id, task_id, kind, requested_by,
                                           server_id, target_server_id, created_at, updated_at)
                VALUES (@id, @task, @kind, @by, @server, @target, @created, @updated)
                """,
                ("@id", request.Id), ("@task", request.TaskId), ("@kind", request.Kind),
                ("@by", request.RequestedBy),
                ("@server", request.ServerId), ("@target", request.TargetServerId),
                ("@created", Sql.ToDb(request.CreatedAt)), ("@updated", Sql.ToDb(request.UpdatedAt)));
        }
        else
        {
            Sql.Exec(conn, tx, """
                INSERT INTO run_requests (id, task_id, kind, with_errors, with_needs_fix, requested_by,
                                          server_id, target_server_id, created_at, updated_at)
                VALUES (@id, @task, @kind, @errors, @needsFix, @by, @server, @target, @created, @updated)
                """,
                ("@id", request.Id), ("@task", request.TaskId), ("@kind", request.Kind),
                ("@errors", request.WithErrors ? 1 : 0), ("@needsFix", request.WithNeedsFix ? 1 : 0),
                ("@by", request.RequestedBy),
                ("@server", request.ServerId), ("@target", request.TargetServerId),
                ("@created", Sql.ToDb(request.CreatedAt)), ("@updated", Sql.ToDb(request.UpdatedAt)));
        }
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            // остановка — своё событие (T-263): в журнале работ «запуск передан» у заявки
            // на остановку читалось бы ровно наоборот
            EventType = RunRequestKinds.IsStop(normalized)
                ? EventTypes.StopRequested
                : EventTypes.RunRequested,
            EntityType = RunRequestKinds.IsStop(normalized) ? "stop_request" : "run_request",
            EntityId = request.Id,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                request.Kind,
                request.WithErrors,
                request.WithNeedsFix,
                target = _scope.CodeOf(request.TargetServerId),
            }),
        });
        tx.Commit();
        return Decorate(request);
    }

    /// <summary>Заявки по задаче (для карточки: «запуск передан на сервер S1») — обе таблицы:
    /// человеку всё равно, чем передано поручение, ему важно, что оно передано.</summary>
    public List<RunRequest> ListByTask(string taskId)
    {
        using var conn = _db.Open();
        var rows = new List<RunRequest>();
        foreach (var table in new[] { RunTable, StopTable })
        {
            rows.AddRange(Sql.Query(conn, null,
                $"SELECT * FROM {table} WHERE task_id=@id AND deleted_at IS NULL ORDER BY created_at",
                (Func<SqliteDataReader, RunRequest>)(table == StopTable ? MapStop : Map), ("@id", taskId)));
        }
        return rows.OrderBy(r => r.CreatedAt).Select(Decorate).ToList();
    }

    /// <summary>
    /// Заявки, которые ЭТОТ сервер ещё не отрабатывал: их и разбирает сторож. Свои же заявки
    /// (поданные здесь) сюда не попадают — на своей задаче запускают сразу, без заявки, а
    /// заявка со своим авторством означала бы, что задача уехала на другой сервер.
    /// <para>Отметки об исполнении общие у запуска и остановки (<c>run_requests_applied</c>):
    /// идентификатор заявки уникален, а память об исполнении — своя у каждого сервера.</para>
    /// </summary>
    public List<RunRequest> Unapplied()
    {
        using var conn = _db.Open();
        var rows = new List<RunRequest>();
        foreach (var table in new[] { RunTable, StopTable })
        {
            rows.AddRange(Sql.Query(conn, null, $"""
                SELECT r.* FROM {table} r
                LEFT JOIN run_requests_applied a ON a.request_id = r.id
                WHERE r.deleted_at IS NULL AND a.request_id IS NULL
                ORDER BY r.created_at
                """, (Func<SqliteDataReader, RunRequest>)(table == StopTable ? MapStop : Map)));
        }
        return rows.OrderBy(r => r.CreatedAt).Select(Decorate).ToList();
    }

    /// <summary>Есть ли ещё не убранная заявка того же вида по задаче: повторное нажатие
    /// кнопки не должно плодить строки, а иерархию хватает попросить один раз.</summary>
    public bool HasOpen(string taskId, string kind)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null, $"""
            SELECT COUNT(*) FROM {TableOf(kind)}
            WHERE task_id=@task AND kind=@kind AND deleted_at IS NULL
            """, ("@task", taskId), ("@kind", kind)) > 0;
    }

    /// <summary>
    /// СНЯТЬ СВОИ ОТКРЫТЫЕ ЗАЯВКИ ТОГО ЖЕ ВИДА по задаче (T-160-S0) — то, с чего начинается
    /// ПОВТОРНОЕ нажатие «запустить» на чужой задаче.
    ///
    /// <para>Зачем это нужно. Отметка «заявка отработана» лежит в нереплицируемой таблице
    /// у ВЛАДЕЛЬЦА задачи, автору она не видна вовсе. Поэтому старая заявка, уже выполненная
    /// на том конце (запустили, потом остановили), для автора выглядит живой и по правилу
    /// «второй такой же не заводим» глушила новое нажатие: человеку писали «запуск передан
    /// на S0», а на S0 не происходило НИЧЕГО — заявка там давно отмечена отработанной.</para>
    ///
    /// <para>Поэтому повторное нажатие снимает прежнюю заявку и заводит новую: у неё новый
    /// идентификатор, отметки об исполнении на неё нет ни у кого, и владелец выполнит её как
    /// первую. Чужие заявки (поданные другим сервером) не трогаются — их автор снимет сам,
    /// а наша новая строка всё равно будет для владельца новой.</para>
    /// </summary>
    /// <returns>Сколько заявок снято.</returns>
    public int DropOpen(string taskId, string kind)
    {
        List<string> mine;
        using (var conn = _db.Open())
        {
            mine = Sql.Query(conn, null, $"""
                    SELECT id, server_id FROM {TableOf(kind)}
                    WHERE task_id=@task AND kind=@kind AND deleted_at IS NULL
                    """, r => (Id: r.S("id"), Server: r.SN("server_id")),
                    ("@task", taskId), ("@kind", kind))
                .Where(row => _scope.CanWrite(row.Server))
                .Select(row => row.Id)
                .ToList();
        }
        foreach (var id in mine)
        {
            Remove(id);
        }
        return mine.Count;
    }

    /// <summary>Отметить заявку отработанной — в СВОЕЙ таблице (строка заявки чужая, править
    /// её нельзя). Отметка ставится ДО запуска: иначе ошибка запуска крутила бы её вечно.</summary>
    /// <param name="result">Что получилось — попадает в диагностику, не в реплику.</param>
    public void MarkApplied(string requestId, string result)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO run_requests_applied (request_id, applied_at, result)
            VALUES (@id, @at, @result)
            ON CONFLICT(request_id) DO UPDATE SET applied_at=@at, result=@result
            """,
            ("@id", requestId), ("@at", Sql.ToDb(DateTime.UtcNow)), ("@result", result));
    }

    /// <summary>Заявка уже отработана здесь.</summary>
    public bool IsApplied(string requestId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM run_requests_applied WHERE request_id=@id", ("@id", requestId)) > 0;
    }

    /// <summary>Убрать заявку: это делает только её автор (ТЗ гл. 6). Заявка бывает в двух
    /// таблицах (запуск и остановка), поэтому ищется в обеих — идентификатор один.</summary>
    public void Remove(string requestId)
    {
        using var conn = _db.Open();
        foreach (var table in new[] { RunTable, StopTable })
        {
            var owner = Sql.Query(conn, null, $"SELECT server_id FROM {table} WHERE id=@id",
                r => r.SN("server_id"), ("@id", requestId)).ToList();
            if (owner.Count == 0)
            {
                continue;
            }
            _scope.EnsureCanWrite(owner[0], Loc.T("msg.runRequest.1"));
            // удаление физическое: заявка — разовое поручение, в истории её держать незачем,
            // а строка обязана исчезнуть и у партнёров (удаление едет журналом изменений)
            Sql.Exec(conn, null, $"DELETE FROM {table} WHERE id=@id", ("@id", requestId));
            return;
        }
    }

    /// <summary>
    /// Уборка (проход сторожа): автор убирает свои отжившие заявки, а сервер — свои слишком
    /// старые отметки. Заявка снимается, как только по ней уже нечего ждать: задача ушла из
    /// «ожидает» либо истёк срок <see cref="Lifetime"/> (сервер-владелец мог быть выключен).
    /// Возвращает, сколько заявок убрано.
    /// </summary>
    public int Cleanup()
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var request in Mine())
        {
            var stale = now - request.CreatedAt > Lifetime;
            if (!stale && !Handled(request))
            {
                continue;
            }
            Remove(request.Id);
            removed++;
        }
        using var conn = _db.Open();
        Sql.Exec(conn, null, "DELETE FROM run_requests_applied WHERE applied_at < @at",
            ("@at", Sql.ToDb(now - MemoryTime)));
        return removed;
    }

    /// <summary>Заявка сделала своё дело: задача уже не ждёт запуска (её взяли в работу,
    /// завершили или отменили). Для иерархии этого мало — там очередь идёт долго и статус
    /// корня меняется не сразу, такие заявки уходят по сроку.
    /// <para>У ОСТАНОВКИ (T-263) признак свой и обратный: задание задачи больше не активно,
    /// а для остановки иерархии — ещё и пометка очереди с задачи снята. Обе величины
    /// приезжают автору обычной репликацией (jobs и tasks реплицируются), поэтому заявку
    /// он снимает сам, не дожидаясь суток.</para></summary>
    private bool Handled(RunRequest request)
    {
        if (RunRequestKinds.IsStop(request.Kind))
        {
            return StopHandled(request);
        }
        if (request.Kind != RunRequestKinds.Task)
        {
            return false;
        }
        using var conn = _db.Open();
        var status = Sql.Scalar<string>(conn, null, "SELECT status FROM tasks WHERE id=@id",
            ("@id", request.TaskId));
        return status is not null
               && status is not (TaskStatuses.Draft or TaskStatuses.Pending or TaskStatuses.Paused);
    }

    /// <summary>Заявка на остановку (T-263) отработала: активных заданий у задачи нет,
    /// а у заявки на иерархию — и пометки очереди на задаче тоже.</summary>
    private bool StopHandled(RunRequest request)
    {
        using var conn = _db.Open();
        var active = Sql.Scalar<long>(conn, null, """
            SELECT COUNT(*) FROM jobs
            WHERE task_id=@id AND state IN ('queued','running','waiting_human')
            """, ("@id", request.TaskId)) > 0;
        if (active)
        {
            return false;
        }
        if (request.Kind == RunRequestKinds.Stop)
        {
            return true;
        }
        // пометка очереди хранится в launch_json корня — читаем её тем же разбором, что и
        // сама очередь (значение может быть и false, одной подстроки мало)
        var launch = Sql.Scalar<string>(conn, null, "SELECT launch_json FROM tasks WHERE id=@id",
            ("@id", request.TaskId)) ?? "";
        return !TaskService.HasLaunchFlag(launch, TaskService.HierarchyFlag);
    }

    /// <summary>Заявки, поданные ЭТИМ сервером: только их он вправе убрать.</summary>
    private List<RunRequest> Mine()
    {
        using var conn = _db.Open();
        var rows = new List<RunRequest>();
        foreach (var table in new[] { RunTable, StopTable })
        {
            rows.AddRange(Sql.Query(conn, null, $"SELECT * FROM {table} WHERE deleted_at IS NULL",
                (Func<SqliteDataReader, RunRequest>)(table == StopTable ? MapStop : Map)));
        }
        return rows.Where(r => _scope.CanWrite(r.ServerId)).ToList();
    }

    private RunRequest Decorate(RunRequest request)
    {
        request.ServerCode = _scope.CodeOf(request.ServerId);
        request.TargetServerCode = _scope.CodeOf(request.TargetServerId);
        return request;
    }

    private static RunRequest Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        TaskId = r.S("task_id"),
        Kind = r.S("kind"),
        WithErrors = r.L("with_errors") != 0,
        WithNeedsFix = r.L("with_needs_fix") != 0,
        RequestedBy = r.SN("requested_by"),
        ServerId = r.SN("server_id"),
        TargetServerId = r.SN("target_server_id"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };

    /// <summary>Заявка на остановку (T-263): та же сущность, но галочек переспроса у неё нет
    /// вовсе — нет и колонок, читать их отсюда нечем.</summary>
    private static RunRequest MapStop(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        TaskId = r.S("task_id"),
        Kind = r.S("kind"),
        RequestedBy = r.SN("requested_by"),
        ServerId = r.SN("server_id"),
        TargetServerId = r.SN("target_server_id"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
