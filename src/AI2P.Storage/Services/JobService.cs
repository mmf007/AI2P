using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Вид элемента Inbox (T-165).</summary>
public static class InboxItemKinds
{
    /// <summary>Задание, выданное человеку: он делает работу и сдаёт результат.</summary>
    public const string Job = "job";

    /// <summary>Вопрос ИИ-агента в чате задачи: агент встал и ждёт ответа человека.</summary>
    public const string Question = "question";
}

/// <summary>Элемент Inbox: то, что ждёт человека, + данные задачи (ТЗ гл. 5, экран 7).</summary>
public sealed class InboxItem
{
    /// <summary>Вид элемента (<see cref="InboxItemKinds"/>): задание человеку или вопрос агента.</summary>
    public string Kind { get; set; } = InboxItemKinds.Job;
    public Job Job { get; set; } = new();
    public string TaskTitle { get; set; } = "";
    public string TaskDisplayId { get; set; } = "";
    public string? ProjectId { get; set; }
    /// <summary>Когда элемент появился: задание создано / вопрос задан — по этому полю Inbox сортируется.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Ник исполнителя задания (для вопроса — кто спрашивает).</summary>
    public string ExecutorNick { get; set; } = "";
    /// <summary>Вопрос агента (T-165): id сообщения чата; у задания человеку — null.</summary>
    public string? QuestionId { get; set; }
    /// <summary>Текст вопроса агента.</summary>
    public string QuestionText { get; set; } = "";
    /// <summary>Варианты ответа на вопрос (options_json сообщения чата).</summary>
    public List<string> Options { get; set; } = [];
    /// <summary>
    /// Можно ли ответить прямо сейчас: задание ещё ждёт (waiting_human). Вопрос, чьё задание
    /// уже остановлено или упало, из Inbox не прячется — иначе он потеряется совсем, — но
    /// отвечать на него нечему: продолжать некого.
    /// </summary>
    public bool CanAnswer { get; set; } = true;
}

/// <summary>
/// Очередь заданий (ТЗ п. 3 принцип 4): задание → статус → результат, для всех исполнителей.
/// </summary>
public sealed class JobService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public JobService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    public Job? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM jobs WHERE id=@id", Map, ("@id", id)).FirstOrDefault();
    }

    public List<Job> ListByTask(string taskId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM jobs WHERE task_id=@id AND deleted_at IS NULL ORDER BY created_at DESC",
            Map, ("@id", taskId));
    }

    /// <summary>Есть ли активное (не завершённое) задание по задаче.</summary>
    public bool HasActive(string taskId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM jobs WHERE task_id=@id AND state IN ('queued','running','waiting_human')",
            ("@id", taskId)) > 0;
    }

    /// <summary>Все running-задания (сторож зависших, T-117): после перезапуска приложения
    /// у такого задания нет живого вызова у коннектора — его толкают повторной подачей.</summary>
    public List<Job> ListRunning()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM jobs WHERE state='running' AND deleted_at IS NULL ORDER BY created_at", Map);
    }

    /// <summary>
    /// Все НЕЗАКРЫТЫЕ задания организации (T-188): в очереди, выполняются или ждут ответа.
    /// Ровно они снимаются принудительной остановкой агентов перед сменой организации —
    /// и ровно их показывают человеку в предупреждении, чтобы список совпадал с делом.
    /// </summary>
    public List<Job> ListActive()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM jobs
            WHERE state IN ('queued','running','waiting_human') AND deleted_at IS NULL
            ORDER BY created_at
            """, Map);
    }

    /// <summary>
    /// Есть ли активное задание у исполнителя (ТЗ v1.26, todo28): каждый ИИ-исполнитель
    /// работает над одним заданием; одна модель — несколько заданий только разными исполнителями.
    ///
    /// Задание, ждущее СВОИХ ПОДЗАДАЧ (T-185, wait_kind=subtasks), исполнителя не занимает:
    /// подзадачи разбитой задачи автоподбор часто отдаёт тому же агенту, и иначе очередь
    /// вставала бы намертво — родитель ждёт подзадач, подзадачи ждут родительского исполнителя.
    /// </summary>
    public bool HasActiveByExecutor(string executorId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null, """
            SELECT COUNT(*) FROM jobs
            WHERE executor_id=@id AND state IN ('queued','running','waiting_human')
              AND COALESCE(wait_kind,'') <> @subtasks
            """,
            ("@id", executorId), ("@subtasks", JobWaitKinds.Subtasks)) > 0;
    }

    /// <summary>
    /// Задания, приостановленные в ожидании ЧЕГО-ТО, кроме ответа человека (T-185):
    /// подзадач (<see cref="JobWaitKinds.Subtasks"/>) или ответа агента другой задачи
    /// (<see cref="JobWaitKinds.Agent"/>). По ним сторож оркестратора решает, не пора ли
    /// продолжать работу: ответ мог прийти, а подзадачи — завершиться.
    /// </summary>
    public List<Job> ListWaiting(string waitKind)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM jobs
            WHERE state='waiting_human' AND wait_kind=@kind AND deleted_at IS NULL
            ORDER BY created_at
            """, Map, ("@kind", waitKind));
    }

    /// <summary>
    /// Задания, ждущие ответа ЧЕЛОВЕКА (T-196): и явно помеченные (<see cref="JobWaitKinds.Human"/>),
    /// и старые, у которых wait_kind пуст, — до T-185 колонки не было вовсе, а «ждёт человека»
    /// было единственным смыслом состояния. По ним сторож проверяет, не ответили ли на вопрос
    /// НА ДРУГОМ СЕРВЕРЕ: такой ответ приезжает репликацией, без вызова API.
    /// </summary>
    public List<Job> ListWaitingHuman()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM jobs
            WHERE state='waiting_human' AND COALESCE(wait_kind,'') IN ('', @human)
              AND deleted_at IS NULL
            ORDER BY created_at
            """, Map, ("@human", JobWaitKinds.Human));
    }

    /// <summary>Приостановленное задание задачи (T-185): по нему видно, висит ли над задачей
    /// агент и чего он ждёт. null — активного ожидающего задания нет.</summary>
    public Job? WaitingByTask(string taskId, string? waitKind = null)
    {
        using var conn = _db.Open();
        var jobs = Sql.Query(conn, null, """
            SELECT * FROM jobs
            WHERE task_id=@task AND state='waiting_human' AND deleted_at IS NULL
            ORDER BY created_at DESC
            """, Map, ("@task", taskId));
        return jobs.FirstOrDefault(j => waitKind is null || j.WaitKind == waitKind);
    }

    /// <param name="role">РОЛЬ задания (T-292-S0, <see cref="JobRoles"/>): пусто — работа
    /// по задаче, "prompter" — подготовка управляющего json моделью-суфлёром. От роли
    /// зависит исход завершения: обычное задание сдаёт задачу, задание суфлёра — запускает
    /// следом основного исполнителя.</param>
    public Job Create(string taskId, string executorId, string requestPath, string? actorId,
        string role = JobRoles.Work)
    {
        var now = DateTime.UtcNow;
        var job = new Job
        {
            TaskId = taskId,
            ExecutorId = executorId,
            State = JobState.Queued,
            RequestPath = requestPath,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now,
        };

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // задание всегда выполняется у ВЛАДЕЛЬЦА задачи (ТЗ гл. 6, этап 42), поэтому его
        // номер получает код этого сервера: J-12-S1. Без суффикса — на дирижёре, как раньше
        job.DisplayId = Database.NextDisplayId(conn, tx, "J",
            _scope.IsConductor ? "" : _scope.Code);

        var projectId = Sql.Scalar<string>(conn, tx,
            "SELECT project_id FROM tasks WHERE id=@id", ("@id", taskId));

        Sql.Exec(conn, tx, """
            INSERT INTO jobs (id, display_id, task_id, executor_id, state, request_path, role,
                              created_at, updated_at)
            VALUES (@id, @did, @task, @executor, @state, @request, @role, @created, @updated)
            """,
            ("@id", job.Id), ("@did", job.DisplayId), ("@task", job.TaskId),
            ("@executor", job.ExecutorId), ("@state", job.State.ToDb()),
            ("@request", job.RequestPath), ("@role", job.Role),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            TaskId = taskId,
            EventType = EventTypes.JobSubmitted,
            EntityType = "job",
            EntityId = job.Id,
            PayloadJson = JsonSerializer.Serialize(new { job.DisplayId, executorId }),
        });

        tx.Commit();
        return job;
    }

    /// <summary>Смена состояния задания + события job.state_changed / job.completed;
    /// cost/usage — стоимость и токены выполнения (учёт стоимости и биллинг, ТЗ гл. 15, v1.15).</summary>
    /// <param name="waitKind">Чего задание ждёт (T-185, <see cref="JobWaitKinds"/>) — только
    /// при переходе в waiting_human; null — «ответа человека», как было всегда. Любой другой
    /// переход причину ожидания стирает: ждать больше нечего.</param>
    public Job SetState(string jobId, JobState state, string? actorId, string? resultPath = null,
        double? cost = null, long? inputTokens = null, long? outputTokens = null, string? currency = null,
        string? waitKind = null)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var job = Sql.Query(conn, tx, "SELECT * FROM jobs WHERE id=@id", Map, ("@id", jobId)).FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.job.1", jobId));
        var oldState = job.State;
        job.State = state;
        job.UpdatedAt = now;
        if (state is JobState.Running or JobState.WaitingHuman && job.StartedAt is null)
        {
            job.StartedAt = now;
        }
        var finished = state is JobState.Done or JobState.Failed or JobState.Cancelled;
        if (finished)
        {
            job.FinishedAt = now;
        }
        if (resultPath is not null)
        {
            job.ResultPath = resultPath;
        }
        if (cost is not null)
        {
            job.Cost = cost.Value;
        }
        if (inputTokens is not null)
        {
            job.InputTokens = inputTokens.Value;
        }
        if (outputTokens is not null)
        {
            job.OutputTokens = outputTokens.Value;
        }
        if (currency is not null)
        {
            job.Currency = currency;
        }
        // чего задание ждёт (T-185): осмысленно только в waiting_human, при любом другом
        // состоянии — стирается
        job.WaitKind = state == JobState.WaitingHuman ? waitKind ?? JobWaitKinds.Human : "";

        Sql.Exec(conn, tx, """
            UPDATE jobs SET state=@state, result_path=@result, cost=@cost,
                            input_tokens=@intok, output_tokens=@outtok, currency=@currency,
                            started_at=@started, finished_at=@finished, updated_at=@updated,
                            wait_kind=@wait
            WHERE id=@id
            """,
            ("@state", job.State.ToDb()), ("@result", job.ResultPath), ("@cost", job.Cost),
            ("@intok", job.InputTokens), ("@outtok", job.OutputTokens), ("@currency", job.Currency),
            ("@started", Sql.ToDbN(job.StartedAt)), ("@finished", Sql.ToDbN(job.FinishedAt)),
            ("@updated", Sql.ToDb(now)),
            ("@wait", job.WaitKind.Length == 0 ? DBNull.Value : job.WaitKind), ("@id", jobId));

        var projectId = Sql.Scalar<string>(conn, tx,
            "SELECT project_id FROM tasks WHERE id=@id", ("@id", job.TaskId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            TaskId = job.TaskId,
            EventType = EventTypes.JobStateChanged,
            EntityType = "job",
            EntityId = job.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                job.DisplayId,
                from = oldState.ToDb(),
                to = job.State.ToDb(),
            }),
        });
        if (finished)
        {
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                ProjectId = projectId,
                TaskId = job.TaskId,
                EventType = EventTypes.JobCompleted,
                EntityType = "job",
                EntityId = job.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    job.DisplayId,
                    state = job.State.ToDb(),
                    job.ResultPath,
                }),
            });
        }

        tx.Commit();
        return job;
    }

    /// <summary>
    /// Inbox (T-165): ВСЁ, что ждёт человека, в одном месте, новые сверху —
    /// задания исполнителей-людей в waiting_human и висящие вопросы ИИ-агентов из чатов задач.
    /// До T-165 вопросы агентов сюда не попадали (отвечать на них можно было только из чата
    /// задачи), и человек, не открывший карточку, о вставшей задаче не узнавал вовсе.
    /// executorId = null — все (однопользовательский режим, ТЗ п. 12.4); заданный executorId
    /// отбирает задания этого человека, а вопросы агентов адресованы любому человеку
    /// (ToExecutorId у них не проставлен) и приходят всем.
    /// </summary>
    public List<InboxItem> Inbox(string? executorId = null)
    {
        var items = HumanJobs(executorId);
        items.AddRange(PendingQuestions());
        return items.OrderByDescending(i => i.CreatedAt).ToList();
    }

    /// <summary>Задания исполнителей-людей, ожидающие ответа (waiting_human).</summary>
    private List<InboxItem> HumanJobs(string? executorId)
    {
        using var conn = _db.Open();
        var sql = """
            SELECT j.*, t.title AS task_title, t.display_id AS task_display_id,
                   t.project_id AS task_project_id, e.nick AS executor_nick
            FROM jobs j
            JOIN tasks t ON t.id = j.task_id
            JOIN executors e ON e.id = j.executor_id
            WHERE j.state = 'waiting_human' AND e.kind = 'human' AND j.deleted_at IS NULL
            """ + (executorId is null ? "" : " AND j.executor_id=@executor") + " ORDER BY j.created_at DESC";
        var args = executorId is null
            ? Array.Empty<(string, object?)>()
            : [("@executor", (object?)executorId)];
        return Sql.Query(conn, null, sql, r =>
        {
            var job = Map(r);
            return new InboxItem
            {
                Kind = InboxItemKinds.Job,
                Job = job,
                TaskTitle = r.S("task_title"),
                TaskDisplayId = r.S("task_display_id"),
                ProjectId = r.SN("task_project_id"),
                ExecutorNick = r.S("executor_nick"),
                CreatedAt = job.CreatedAt,
                CanAnswer = true,
            };
        }, args);
    }

    /// <summary>
    /// Висящие вопросы ИИ-агентов (T-165): сообщение чата kind=question без ответа.
    /// Задание вопроса берётся ЛЮБОЕ (не только waiting_human): если агент упал или его
    /// остановили, вопрос всё равно виден — но отвечать на него уже нечему (CanAnswer=false).
    /// </summary>
    private List<InboxItem> PendingQuestions()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT j.*, t.title AS task_title, t.display_id AS task_display_id,
                   t.project_id AS task_project_id, e.nick AS executor_nick,
                   c.id AS question_id, c.text AS question_text, c.options_json AS question_options,
                   c.created_at AS question_created
            FROM chat_messages c
            JOIN jobs j ON j.id = c.job_id
            JOIN tasks t ON t.id = c.task_id
            JOIN executors e ON e.id = j.executor_id
            WHERE c.kind = 'question' AND c.answered_at IS NULL AND c.deleted_at IS NULL
              AND j.deleted_at IS NULL
            ORDER BY c.created_at DESC
            """, r => new InboxItem
        {
            Kind = InboxItemKinds.Question,
            Job = Map(r),
            TaskTitle = r.S("task_title"),
            TaskDisplayId = r.S("task_display_id"),
            ProjectId = r.SN("task_project_id"),
            ExecutorNick = r.S("executor_nick"),
            QuestionId = r.S("question_id"),
            QuestionText = r.S("question_text"),
            Options = ParseOptions(r.S("question_options")),
            CreatedAt = r.Dt("question_created"),
            CanAnswer = EnumMap.JobStateFromDb(r.S("state")) == JobState.WaitingHuman,
        });
    }

    private static List<string> ParseOptions(string json)
    {
        if (json.Trim().Length == 0)
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Job Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        TaskId = r.S("task_id"),
        ExecutorId = r.S("executor_id"),
        State = EnumMap.JobStateFromDb(r.S("state")),
        RequestPath = r.S("request_path"),
        ResultPath = r.S("result_path"),
        Cost = r.D("cost"),
        InputTokens = r.L("input_tokens"),
        OutputTokens = r.L("output_tokens"),
        Currency = r.S("currency"),
        StartedAt = r.DtN("started_at"),
        FinishedAt = r.DtN("finished_at"),
        WaitKind = r.SN("wait_kind") ?? "",
        // роль задания (T-292-S0): пусто — работа, "prompter" — подготовка json суфлёром
        Role = r.Has("role") ? r.SN("role") ?? "" : "",
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
