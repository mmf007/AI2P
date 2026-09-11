using System.Collections.Concurrent;
using System.Text.Json;

namespace AI2P.Connectors;

/// <summary>
/// ТОКЕН ЗАДАНИЯ (T-34-S0) — реквизиты, под которыми ИИ-агент зовёт действия AI2P
/// синхронно, прямо в ходе своей работы (клиент командной строки <c>ai2p</c>).
///
/// <para>Почему не «от текущего пользователя»: авторство в чате, опыте и журнале работ
/// стало бы человеческим, а правила безопасности считаются по паре задача+исполнитель
/// (T-117) — считать их было бы не по чему. Поэтому личность запроса — ИСПОЛНИТЕЛЬ
/// задания, и берётся она не из cookie, а из самой сессии.</para>
///
/// <para>Устройство — по двум готовым образцам: межсерверный токен <c>X-AI2P-Server</c>
/// (<c>ClusterEndpoints</c>) и внутренний токен процесса <c>Ai2pAuth.InternalToken</c>.
/// Токен случайный, живёт ТОЛЬКО В ПАМЯТИ процесса, на диск и в БД не пишется,
/// принимается лишь с петлевого адреса и только пока задание идёт: сессия заводится
/// вместе с набором инструментов задания и снимается в его <c>finally</c>.</para>
///
/// <para>ОДНА РЕАЛИЗАЦИЯ ДЕЙСТВИЙ. В сессии лежит ТОТ ЖЕ <see cref="AgentToolset"/>,
/// которым работают API-коннекторы и маркеры CLI-агента: раздел <c>/api/agent</c> зовёт
/// его <c>Authorize</c> и <c>ExecuteAsync</c>, поэтому вызов через клиент проходит те же
/// правила безопасности, попадает в тот же журнал работ (agent.tool_calls) и в ту же
/// консоль задания. Дублировать логику инструментов в клиенте не нужно и нельзя.</para>
/// </summary>
public static class AgentSessionRegistry
{
    private static readonly ConcurrentDictionary<string, AgentSession> Sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Базовый адрес ЭТОГО сервера по петле (<c>http://localhost:5480/ai2p</c>) — его получает
    /// агент переменной окружения <c>AI2P_URL</c>. Ставится один раз при старте приложения
    /// (Program.cs): токен принимается только с петлевого адреса, поэтому и ходить клиенту
    /// надо по петле — внешнее имя сервера тут не годится (T-2-S1: у него бывает свой порт).
    /// Пусто — приложение не поднято (тесты): клиенту тогда некуда обращаться.
    /// </summary>
    public static string LocalBaseUrl { get; set; } = "";

    /// <summary>Завести сессию задания; возвращённый объект надо снять
    /// <see cref="Release"/> в <c>finally</c> задания.</summary>
    public static AgentSession Register(string jobId, string jobCode, string taskId, string taskCode,
        string executorId, AgentToolset tools)
    {
        var session = new AgentSession(
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            jobId, jobCode, taskId, taskCode, executorId, tools);
        Sessions[session.Token] = session;
        return session;
    }

    /// <summary>Сессия по токену; null — токена нет либо задание уже закончилось.</summary>
    public static AgentSession? Resolve(string? token) =>
        token is { Length: > 0 } && Sessions.TryGetValue(token, out var session) ? session : null;

    /// <summary>Снять сессию: с этой минуты токен не работает (ТЗ гл. 12).</summary>
    public static void Release(AgentSession? session)
    {
        if (session is not null)
        {
            Sessions.TryRemove(session.Token, out _);
            session.Dispose();
        }
    }

    /// <summary>Сколько сессий живо — для диагностики и тестов.</summary>
    public static int Count => Sessions.Count;
}

/// <summary>
/// Живая сессия задания (T-34-S0): токен, кто и над чем работает, и набор инструментов.
/// Вызовы клиента сериализуются <see cref="EnterAsync"/>: набор инструментов копит журнал
/// вызовов списком и рассчитан на один вызов за раз, а агент может позвать клиента
/// из нескольких своих подпроцессов сразу.
/// </summary>
public sealed class AgentSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal AgentSession(string token, string jobId, string jobCode, string taskId, string taskCode,
        string executorId, AgentToolset tools)
    {
        Token = token;
        JobId = jobId;
        JobCode = jobCode;
        TaskId = taskId;
        TaskCode = taskCode;
        ExecutorId = executorId;
        Tools = tools;
    }

    public string Token { get; }
    public string JobId { get; }
    public string JobCode { get; }
    public string TaskId { get; }

    /// <summary>Код задачи задания (T-15) — им клиент подписывает свои сообщения об ошибках.</summary>
    public string TaskCode { get; }

    /// <summary>Исполнитель задания — ЛИЧНОСТЬ вызова: под ним пишутся чат, опыт и журнал.</summary>
    public string ExecutorId { get; }

    /// <summary>Тот же набор инструментов, которым работает задание.</summary>
    public AgentToolset Tools { get; }

    /// <summary>Взять сессию под свой вызов (см. пояснение к классу).</summary>
    public Task EnterAsync(CancellationToken ct) => _gate.WaitAsync(ct);

    public void Leave() => _gate.Release();

    public void Dispose() => _gate.Dispose();

    /// <summary>Действия, доступные этому заданию (для <c>ai2p help</c>): имя инструмента
    /// и его описание из справочника действий на языке команды задачи.</summary>
    public IEnumerable<(string Tool, string Prompt)> AvailableTools() =>
        Tools.Specs.Select(s => (s.Name, s.Description));

    /// <summary>Пустые аргументы вызова — их место в JSON-теле запроса.</summary>
    public static readonly JsonElement NoArgs = JsonDocument.Parse("{}").RootElement.Clone();
}
