using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Чат по задаче (ТЗ п. 7.2): переписка пишется в журнал работ.
/// Вопросы ИИ-агентов (ТЗ v1.17, todo20): kind=question с вариантами ответа; до ответа
/// вопрос «висит» (answered_at IS NULL), ответ — kind=answer со ссылкой на вопрос.
/// </summary>
public sealed class ChatService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public ChatService(Database db, EventStore events)
    {
        _db = db;
        _events = events;
    }

    public List<ChatMessage> ListByTask(string taskId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM chat_messages WHERE task_id=@id AND deleted_at IS NULL ORDER BY created_at",
            Map, ("@id", taskId));
    }

    public ChatMessage? Get(string id)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, "SELECT * FROM chat_messages WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
    }

    public ChatMessage Add(string taskId, string fromExecutorId, string? toExecutorId, string text) =>
        Insert(new ChatMessage
        {
            TaskId = taskId,
            FromExecutorId = fromExecutorId,
            ToExecutorId = toExecutorId,
            Text = text.Trim(),
        }, EventTypes.ChatMessage);

    /// <summary>Вопрос ИИ-агента (ТЗ v1.17): агент останавливается и ждёт ответа человека.</summary>
    /// <param name="toExecutorId">Адресат вопроса (T-185): исполнитель РОДСТВЕННОЙ задачи,
    /// когда агент спрашивает не человека, а другого агента. null — адресат любой человек,
    /// видящий задачу (обычно ответственный), как было до T-185. Вопрос агенту виден человеку
    /// так же, как обычный: не дождавшись собеседника, ответить всегда может он.</param>
    public ChatMessage AddQuestion(string taskId, string fromExecutorId, string jobId,
        string text, IReadOnlyCollection<string> options, string? toExecutorId = null)
    {
        return Insert(new ChatMessage
        {
            TaskId = taskId,
            FromExecutorId = fromExecutorId,
            ToExecutorId = toExecutorId,
            Text = text.Trim(),
            Kind = ChatMessageKind.Question,
            OptionsJson = JsonSerializer.Serialize(options),
            JobId = jobId,
        }, EventTypes.ChatQuestion);
    }

    /// <summary>
    /// Ответ АГЕНТА-собеседника на висящий вопрос (T-185): обычное сообщение, написанное
    /// в чат задачи тем исполнителем, которому вопрос адресован, ПОСЛЕ вопроса. Именно его
    /// сторож оркестратора засчитывает за ответ и продолжает работу спросившего агента.
    /// null — собеседник ещё не отвечал (тогда ждём дальше; ответить может и человек).
    /// </summary>
    public ChatMessage? AgentReplyTo(ChatMessage question)
    {
        if (question.ToExecutorId is not { Length: > 0 } addressee)
        {
            return null;
        }
        return ListByTask(question.TaskId)
            .FirstOrDefault(m => m.Kind == ChatMessageKind.Message
                                 && m.CreatedAt >= question.CreatedAt
                                 && string.Equals(m.FromExecutorId, addressee, StringComparison.Ordinal));
    }

    /// <summary>
    /// Ответ на висящий вопрос (ТЗ v1.17): пишется сообщение kind=answer, вопрос помечается
    /// отвеченным. Возвращает пару (вопрос, ответ) — вопрос нужен для продолжения задания.
    /// </summary>
    public (ChatMessage Question, ChatMessage Answer) Answer(string questionId, string fromExecutorId, string text)
    {
        var question = Get(questionId) ?? throw new InvalidOperationException(Loc.T("msg.chat.1", questionId));
        if (question.Kind != ChatMessageKind.Question)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.2"));
        }
        if (question.AnsweredAt is not null)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.3"));
        }
        var answer = Insert(new ChatMessage
        {
            TaskId = question.TaskId,
            FromExecutorId = fromExecutorId,
            ToExecutorId = question.FromExecutorId,
            Text = text.Trim(),
            Kind = ChatMessageKind.Answer,
            AnswerToId = question.Id,
        }, EventTypes.ChatAnswer);

        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE chat_messages SET answered_at=@ts, updated_at=@ts WHERE id=@id",
            ("@ts", Sql.ToDb(now)), ("@id", question.Id));
        question.AnsweredAt = now;
        return (question, answer);
    }

    /// <summary>
    /// Закрыть вопрос ответом АГЕНТА-собеседника (T-185): ответом служит уже написанное им
    /// сообщение чата, поэтому второй записи не заводим — вопрос помечается отвеченным, а
    /// сообщение-ответ привязывается к нему (в переписке видно, на что оно отвечает).
    /// </summary>
    public void CloseByAgentReply(string questionId, string replyId)
    {
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE chat_messages SET answered_at=@ts, updated_at=@ts
            WHERE id=@id AND kind='question' AND answered_at IS NULL
            """, ("@ts", now), ("@id", questionId));
        Sql.Exec(conn, tx, "UPDATE chat_messages SET answer_to_id=@q, updated_at=@ts WHERE id=@id",
            ("@q", questionId), ("@ts", now), ("@id", replyId));
        tx.Commit();
    }

    /// <summary>Сообщение обсуждения из внешней системы (T-152): автор — так, как он назван
    /// ТАМ («trello:&lt;логин&gt;»), время — время сообщения в источнике, внешний id — защита
    /// от дублей при повторном импорте.</summary>
    public sealed record ImportedMessage(string ExternalRef, string Author, string Text, DateTime Date);

    /// <summary>
    /// Перенос обсуждения внешней системы в чат задачи (T-152): комментарии карточки Trello
    /// становятся сообщениями чата в ТОМ ЖЕ порядке и с тем же временем, что в источнике —
    /// сортировка чата идёт по created_at, поэтому хронология сохраняется сама. Автор
    /// показывается строкой источника («trello:&lt;логин&gt;»), а from_executor_id — тот, кто
    /// импортировал: колонка ссылается на справочник исполнителей, и человека из Trello
    /// в нём нет. Повторный импорт той же карточки дублей не делает (external_ref).
    /// Возвращает число добавленных сообщений.
    /// </summary>
    public int Import(string taskId, string fromExecutorId, IEnumerable<ImportedMessage> messages)
    {
        var known = ListByTask(taskId)
            .Select(m => m.ExternalRef)
            .Where(r => !string.IsNullOrEmpty(r))
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var message in messages.OrderBy(m => m.Date))
        {
            if (message.Text.Trim().Length == 0
                || (message.ExternalRef.Length > 0 && !known.Add(message.ExternalRef)))
            {
                continue;
            }
            Insert(new ChatMessage
            {
                TaskId = taskId,
                FromExecutorId = fromExecutorId,
                ToExecutorId = null,
                Text = message.Text.Trim(),
                AuthorName = message.Author.Trim(),
                ExternalRef = message.ExternalRef,
            }, EventTypes.ChatMessage, message.Date);
            added++;
        }
        return added;
    }

    /// <summary>
    /// СНЯТЬ висящий вопрос (T-1-S1): ответа не будет — агента, который спрашивал, уже нет
    /// (задание упало, было остановлено или завершилось само). Такой вопрос до сих пор висел
    /// вечно: ответить нельзя («задание уже не ждёт ответа»), а закрыть — нечем, и он остался
    /// бы в счётчике задачи и в Inbox навсегда. Снятие проставляет answered_at БЕЗ ответного
    /// сообщения, поэтому вопрос разом уходит из всех признаков «висит вопрос» (счётчик
    /// pending_questions в списках, Inbox, причина паузы), а в чате остаётся с пометкой
    /// «снят»: отличить снятый вопрос от отвеченного можно по отсутствию ответа на него
    /// (сообщения с answer_to_id этого вопроса).
    /// </summary>
    public ChatMessage Dismiss(string questionId, string actorId)
    {
        var question = Get(questionId) ?? throw new InvalidOperationException(Loc.T("msg.chat.1", questionId));
        if (question.Kind != ChatMessageKind.Question)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.5"));
        }
        if (question.AnsweredAt is not null)
        {
            throw new InvalidOperationException(Loc.T("msg.chat.6"));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE chat_messages SET answered_at=@ts, updated_at=@ts WHERE id=@id",
            ("@ts", Sql.ToDb(now)), ("@id", question.Id));
        var projectId = Sql.Scalar<string>(conn, tx,
            "SELECT project_id FROM tasks WHERE id=@id", ("@id", question.TaskId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            TaskId = question.TaskId,
            EventType = EventTypes.ChatQuestionDismissed,
            EntityType = "chat_message",
            EntityId = question.Id,
            PayloadJson = JsonSerializer.Serialize(new { preview = Preview(question.Text) }),
        });
        tx.Commit();
        question.AnsweredAt = now;
        question.UpdatedAt = now;
        return question;
    }

    /// <summary>Отвечен ли вопрос по-настоящему (T-1-S1): есть сообщение, привязанное к нему
    /// (ответ человека kind=answer либо сообщение агента-собеседника, T-185). Снятый вопрос
    /// закрыт (answered_at), но такого сообщения у него нет.</summary>
    public bool HasReply(string questionId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM chat_messages WHERE answer_to_id=@id AND deleted_at IS NULL",
            ("@id", questionId)) > 0;
    }

    /// <summary>
    /// Закрыть висящие вопросы задания без ответа (задание отменено, ТЗ v1.17) —
    /// чтобы они не числились «ждущими» в списках задач.
    /// </summary>
    public void ClosePendingByJob(string jobId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            UPDATE chat_messages SET answered_at=@ts, updated_at=@ts
            WHERE job_id=@job AND kind='question' AND answered_at IS NULL
            """,
            ("@ts", Sql.ToDb(now)), ("@job", jobId));
    }

    /// <summary>Висящий (неотвеченный) вопрос задания — для восстановления ожидания после перезапуска.</summary>
    public ChatMessage? PendingQuestionByJob(string jobId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
                SELECT * FROM chat_messages
                WHERE job_id=@job AND kind='question' AND answered_at IS NULL AND deleted_at IS NULL
                ORDER BY created_at DESC
                """,
            Map, ("@job", jobId)).FirstOrDefault();
    }

    /// <summary>Последний вопрос задания — в том числе уже закрытый (T-196): по нему сторож
    /// понимает, на что пришёл ответ с другого сервера.</summary>
    public ChatMessage? LastQuestionByJob(string jobId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
                SELECT * FROM chat_messages
                WHERE job_id=@job AND kind='question' AND deleted_at IS NULL
                ORDER BY created_at DESC
                """,
            Map, ("@job", jobId)).FirstOrDefault();
    }

    /// <summary>
    /// ВСЕ ответы на вопрос (T-196), в порядке написания. Обычно он один, но в кластере их
    /// бывает два: пока ответ с одного сервера не доехал до другого, человек мог ответить и
    /// там. Оба ответа — настоящие сообщения чата, и агенту уходят оба; сойдутся — работает
    /// дальше, разойдутся — переспрашивает.
    /// </summary>
    public List<ChatMessage> AnswersTo(string questionId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM chat_messages
            WHERE answer_to_id=@id AND deleted_at IS NULL
            ORDER BY created_at
            """, Map, ("@id", questionId));
    }

    /// <param name="createdAt">Время сообщения; null — «сейчас». Задаётся только переносом
    /// обсуждения из внешней системы (T-152): чат сортируется по created_at, и хронология
    /// источника сохраняется именно этим значением.</param>
    private ChatMessage Insert(ChatMessage message, string eventType, DateTime? createdAt = null)
    {
        if (message.Text.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.chat.4"));
        }
        var now = DateTime.UtcNow;
        message.CreatedAt = createdAt?.ToUniversalTime() ?? now;
        message.UpdatedAt = now;

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        Sql.Exec(conn, tx, """
            INSERT INTO chat_messages (id, task_id, from_executor_id, to_executor_id, text,
                                       kind, options_json, answer_to_id, job_id, answered_at,
                                       author_name, external_ref, created_at, updated_at)
            VALUES (@id, @task, @from, @to, @text, @kind, @options, @answerTo, @job, @answered,
                    @author, @ref, @created, @updated)
            """,
            ("@id", message.Id), ("@task", message.TaskId), ("@from", message.FromExecutorId),
            ("@to", message.ToExecutorId), ("@text", message.Text),
            ("@kind", message.Kind), ("@options", message.OptionsJson),
            ("@answerTo", message.AnswerToId), ("@job", message.JobId),
            ("@answered", Sql.ToDbN(message.AnsweredAt)),
            ("@author", message.AuthorName),
            ("@ref", message.ExternalRef.Length == 0 ? DBNull.Value : message.ExternalRef),
            ("@created", Sql.ToDb(message.CreatedAt)), ("@updated", Sql.ToDb(now)));

        var projectId = Sql.Scalar<string>(conn, tx,
            "SELECT project_id FROM tasks WHERE id=@id", ("@id", message.TaskId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = message.FromExecutorId,
            ProjectId = projectId,
            TaskId = message.TaskId,
            EventType = eventType,
            EntityType = "chat_message",
            EntityId = message.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                message.ToExecutorId,
                preview = Preview(message.Text),
            }),
        });

        tx.Commit();
        return message;
    }

    private static string Preview(string text) => text.Length <= 120 ? text : text[..120] + "…";

    private static ChatMessage Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        TaskId = r.S("task_id"),
        FromExecutorId = r.S("from_executor_id"),
        ToExecutorId = r.SN("to_executor_id"),
        Text = r.S("text"),
        Kind = r.S("kind"),
        OptionsJson = r.S("options_json"),
        AnswerToId = r.SN("answer_to_id"),
        JobId = r.SN("job_id"),
        AnsweredAt = r.DtN("answered_at"),
        AuthorName = r.S("author_name"),
        ExternalRef = r.SN("external_ref") ?? "",
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
