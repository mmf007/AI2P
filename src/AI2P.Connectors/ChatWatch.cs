using System.Text;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// Перебивка агента чатом (T-161): пока агент работает, человек может написать в ЧАТ задачи —
/// уточнение, встречный вопрос или экстренное «стой, не то делаешь». До T-161 такое сообщение
/// агент не видел вовсе: оно просто ложилось в переписку и читалось человеком уже после того,
/// как задание закрывалось (у CLI-агента инструментов AI2P нет, а API-агент читает чат только
/// если сам догадается позвать get_task_chat — и только в начале работы).
///
/// Теперь на время работы задания за чатом следит этот класс: новые сообщения ЛЮДЕЙ
/// доставляются агенту прямо в ходе работы (CLI-коннектор для этого прерывает процесс и
/// продолжает ту же сессию, API-коннекторы подставляют сообщение на ближайшем витке цикла
/// инструментов), а ответ агента уходит обратно в чат — маркером AI2P_CHAT у CLI-агента и
/// инструментом send_chat_message у остальных.
///
/// Свои же сообщения (ответы агента, служебные записи от его имени) перебивкой не считаются,
/// иначе агент перебивал бы сам себя бесконечно. Вопросы и ответы механики v1.17 сюда тоже
/// не попадают: там своя пауза (job → waiting_human) и своё продолжение.
/// </summary>
public sealed class ChatWatch
{
    /// <summary>Как часто заглядывать в чат, пока агент работает.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Сколько от начала хода агента работу НЕ прерывают (CLI-коннектор): сессия Claude Code
    /// появляется на диске не мгновенно, и прерывание в первые секунды оставило бы задание
    /// без продолжения (`--resume` нечего возобновлять). Сообщение при этом не теряется —
    /// оно доедет следующей проверкой или в конце хода.
    /// </summary>
    public static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(10);

    /// <summary>Сколько раз за одно задание работу можно прервать чатом: защита от
    /// бесконечной перебивки (человек пишет — агент перезапускается — человек пишет).
    /// Предел исчерпан — сообщения всё равно доставляются, но уже без прерывания:
    /// на ближайшем разрыве работы (конец хода агента).</summary>
    public const int MaxInterrupts = 20;

    private readonly ChatService _chat;
    private readonly ExecutorService _executors;
    private readonly string _taskId;
    private readonly string _agentExecutorId;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <param name="agentExecutorId">Исполнитель задания — его собственные сообщения
    /// перебивкой не считаются.</param>
    /// <param name="since">Продолжение работы, оборванной лимитом (T-166): контекст задания —
    /// переписка до этого момента (агент её уже видел в своей сессии), а всё, что человек
    /// написал ПОКА задача ждала сброса лимита, — новое и должно доехать до агента. null —
    /// обычный запуск: контекст задания — вся прежняя переписка.</param>
    public ChatWatch(ChatService chat, ExecutorService executors, string taskId, string agentExecutorId,
        DateTime? since = null)
    {
        _chat = chat;
        _executors = executors;
        _taskId = taskId;
        _agentExecutorId = agentExecutorId;
        // всё, что в чате уже есть на момент старта, — не перебивка: это контекст задания.
        // Наблюдатель строится ДО начала работы агента, поэтому сбой чтения тут гасится:
        // задание из-за чата начаться не должно (в худшем случае старая переписка приедет
        // агенту как новая — это лучше, чем несостоявшееся задание)
        foreach (var message in Take(peek: false))
        {
            if (since is { } at && message.CreatedAt > at)
            {
                _seen.Remove(message.Id); // написано во время ожидания лимита — агент это ещё не видел
            }
        }
    }

    /// <summary>Сколько раз работу уже прерывали чатом (для предела и для журнала).</summary>
    public int Interrupts { get; private set; }

    /// <summary>Предел прерываний исчерпан — дальше сообщения доставляются без прерывания.</summary>
    public bool LimitReached => Interrupts >= MaxInterrupts;

    /// <summary>Есть ли непрочитанные агентом сообщения (проверка в опросе — без выборки).</summary>
    public bool HasNew => Take(peek: true).Count > 0;

    /// <summary>Забрать новые сообщения; каждое отдаётся ровно один раз.</summary>
    public List<ChatMessage> Take() => Take(peek: false);

    /// <param name="peek">Только посмотреть — сообщения остаются непрочитанными.</param>
    private List<ChatMessage> Take(bool peek)
    {
        lock (_lock)
        {
            List<ChatMessage> fresh;
            try
            {
                fresh = Deliverable().Where(m => !_seen.Contains(m.Id)).ToList();
            }
            catch (Exception ex)
            {
                // чат читается фоновым опросом, пока агент работает: сбой чтения (занятая
                // база, гонка с удалением задачи) не должен ронять само задание — сообщение
                // доедет следующей проверкой
                Serilog.Log.ForContext<ChatWatch>()
                    .Warning(ex, "Чат задачи {TaskId} не прочитался — перебивка пропущена", _taskId);
                return [];
            }
            if (!peek)
            {
                foreach (var message in fresh)
                {
                    _seen.Add(message.Id);
                }
            }
            return fresh;
        }
    }

    /// <summary>Отметить, что работа была прервана (счётчик предела).</summary>
    public void CountInterrupt() => Interrupts++;

    /// <summary>
    /// Ждать сообщения в чате: true — пришли, false — ждать перестали (задание закончилось
    /// или его остановили). Опрос БД задачи раз в <see cref="PollInterval"/>: своего канала
    /// «сервер → фоновое задание» в приложении нет, а запись в чат идёт через API любого
    /// сервера кластера.
    /// </summary>
    /// <param name="grace">Сколько НЕ трогать агента в начале хода: сессия CLI появляется
    /// на диске не в первую миллисекунду, и прерывать работу раньше опасно — продолжать
    /// (--resume) будет нечего. Ноль — следить сразу (агенты с инструментами AI2P: там
    /// прерывания процесса нет вовсе).</param>
    public async Task<bool> WaitAsync(CancellationToken ct, TimeSpan grace = default)
    {
        if (grace > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(grace, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        while (!ct.IsCancellationRequested)
        {
            if (HasNew)
            {
                return true;
            }
            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Текст доставки сообщений агенту. Говорится прямо: это система, а не человек-собеседник;
    /// на сообщения нужно ОТВЕТИТЬ (ответ уйдёт человеку в чат) и продолжить работу с учётом
    /// сказанного — сообщение может и менять задание («не то делаешь»), и просто уточнять.
    /// </summary>
    /// <param name="messages">Что доставляем (Take).</param>
    /// <param name="replyHow">Чем отвечать: маркер AI2P_CHAT (CLI-агент) или инструмент
    /// send_chat_message (агент с инструментами AI2P).</param>
    /// <param name="interrupted">Работа была прервана на середине (CLI-агент) — об этом
    /// агенту стоит знать: незаконченный шаг придётся повторить.</param>
    /// <param name="language">Язык блока — язык команды задачи (T-190): текст читает агент.</param>
    public string Block(IReadOnlyList<ChatMessage> messages, string replyHow, bool interrupted,
        string? language = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.In(language, interrupted ? "prompt.chat.1" : "prompt.chat.2"));
        sb.AppendLine();
        foreach (var message in messages)
        {
            sb.AppendLine($"--- [{message.CreatedAt:yyyy-MM-dd HH:mm} UTC] {Author(message)}:");
            sb.AppendLine(Clip(message.Text));
            sb.AppendLine();
        }
        sb.AppendLine(Loc.In(language, "prompt.chat.3"));
        sb.AppendLine(Loc.In(language, "prompt.chat.4", replyHow));
        sb.AppendLine(Loc.In(language, interrupted ? "prompt.chat.5" : "prompt.chat.6"));
        sb.AppendLine();
        sb.Append(Loc.In(language, "prompt.chat.7"));
        return sb.ToString();
    }

    /// <summary>Ответ агента в чат задачи (T-161) — от его имени, обычным сообщением.</summary>
    public ChatMessage Post(string text) => _chat.Add(_taskId, _agentExecutorId, null, text);

    /// <summary>
    /// Сообщения чата, которые вообще могут быть перебивкой: обычные и не от самого агента.
    /// У вопросов и ответов механики v1.17 своя пауза и своё продолжение, поэтому обычный
    /// ответ перебивкой НЕ считается — иначе он пришёл бы агенту дважды (T-161).
    ///
    /// Исключение одно и появилось в T-196: ВТОРОЙ ответ на тот же вопрос. Так бывает в
    /// кластере — на разных серверах ответили, пока ответ не доехал. Продолжение работы
    /// закрывает вопрос ровно один раз, и без этого второй ответ не дошёл бы до агента
    /// вовсе; теперь он доезжает обычной перебивкой, и агент решает сам: согласуется
    /// с первым — работает дальше, противоречит — переспрашивает.
    /// </summary>
    private IEnumerable<ChatMessage> Deliverable()
    {
        var all = _chat.ListByTask(_taskId);
        return all.Where(m =>
            !string.Equals(m.FromExecutorId, _agentExecutorId, StringComparison.Ordinal)
            && (m.Kind == ChatMessageKind.Message || IsSecondAnswer(m, all)));
    }

    /// <summary>Ответ, который пришёл к УЖЕ отвеченному вопросу (T-196): на тот же вопрос
    /// раньше ответили в другом месте кластера.</summary>
    private static bool IsSecondAnswer(ChatMessage message, List<ChatMessage> all) =>
        message.Kind == ChatMessageKind.Answer
        && message.AnswerToId is { Length: > 0 } questionId
        && all.Any(other => other.Id != message.Id
                            && other.AnswerToId == questionId
                            && other.CreatedAt <= message.CreatedAt);

    /// <summary>Автор: ник исполнителя, а у перенесённого извне сообщения (T-152) — его
    /// имя в источнике («trello:&lt;логин&gt;»).</summary>
    private string Author(ChatMessage message) =>
        message.AuthorName is { Length: > 0 } external
            ? external
            : _executors.Get(message.FromExecutorId)?.Nick ?? message.FromExecutorId;

    private static string Clip(string text) =>
        text.Length <= 4000 ? text : text[..4000] + "…";
}
