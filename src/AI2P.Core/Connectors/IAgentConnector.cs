using AI2P.Core.Entities;

namespace AI2P.Core.Connectors;

/// <summary>
/// Единый интерфейс исполнителя для ядра (ТЗ п. 7.2): человек — тоже коннектор.
/// Асинхронная модель: задание → статус → результат; ядро не знает о конкретных ИИ.
/// </summary>
public interface IAgentConnector
{
    /// <summary>Вид коннектора: "human", "anthropic"; далее "openai-compatible", … (этап 2).</summary>
    string Kind { get; }

    /// <summary>
    /// Принять задание. Коннектор переводит job в своё рабочее состояние
    /// (ИИ — running, человек — waiting_human) и возвращает управление —
    /// результат придёт асинхронно (у ИИ — фоновый вызов провайдера, у человека — ответ в UI).
    /// </summary>
    Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default);

    /// <summary>Отменить задание.</summary>
    Task CancelAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// Есть ли по заданию живой фоновый вызов в этом процессе (T-117). Running-задание,
    /// у которого коннектор ничего не выполняет (после перезапуска приложения или старого
    /// обрыва), — зависшее: его продолжают повторной подачей. У человека всегда false —
    /// его задания ждут в waiting_human, а не выполняются процессом.
    /// </summary>
    bool HasActiveRun(string jobId) => false;

    /// <summary>
    /// Продолжить задание после ответа человека на вопрос агента (ТЗ v1.17):
    /// контекст диалога восстанавливается, ответ подставляется, работа продолжается.
    /// Поддерживается только ИИ-коннекторами.
    /// </summary>
    Task ResumeJobAsync(Job job, string answerText, CancellationToken ct = default) =>
        throw new NotSupportedException(Loc.T("msg.iAgentConnector.1"));

    /// <summary>
    /// Продолжить задание, оборванное лимитом подписки, когда лимит отпустил (T-166):
    /// связь с агентом не рвалась — оживает та же сессия, агенту уходит «Continue».
    /// Поддерживается только ИИ-коннекторами, у которых сессия сохраняется (Claude CLI).
    /// </summary>
    /// <param name="requestText">Промпт задания на случай, если сессии агента уже нет:
    /// тогда работа начинается заново, а не встаёт ошибкой.</param>
    Task ContinueAfterLimitAsync(Job job, string requestText = "", CancellationToken ct = default) =>
        throw new NotSupportedException(Loc.T("msg.iAgentConnector.2"));

    /// <summary>
    /// Проверка подключения исполнителя (ТЗ v1.14, «Запуск работы команды»):
    /// null — подключение успешно, иначе текст ошибки. Для человека всегда успех.
    /// </summary>
    Task<string?> TestConnectionAsync(Executor executor, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
