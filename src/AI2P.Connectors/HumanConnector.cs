using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// Человек — тоже коннектор (ТЗ п. 7.2): задание переводится в waiting_human и появляется
/// в Inbox человека (гл. 5, экран 7); система ждёт его ответа — часы и дни это норма.
/// Ответ человека завершает задание (см. Answer).
/// </summary>
public sealed class HumanConnector : IAgentConnector
{
    private readonly JobService _jobs;

    public HumanConnector(JobService jobs) => _jobs = jobs;

    public string Kind => "human";

    public Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default)
    {
        // текст задания уже лежит в request_path; человеку достаточно смены состояния —
        // Inbox выбирает все waiting_human из очереди
        _jobs.SetState(job.Id, JobState.WaitingHuman, actorId: null);
        return Task.CompletedTask;
    }

    public Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        _jobs.SetState(jobId, JobState.Cancelled, actorId: null);
        return Task.CompletedTask;
    }
}
