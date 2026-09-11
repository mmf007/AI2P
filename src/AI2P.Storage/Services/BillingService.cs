using AI2P.Core.Api;
using AI2P.Core.Entities;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Биллинг (ТЗ v1.15, todo18): сбор информации о стоимости работы. Запись = задание,
/// которое начинало выполняться (started_at заполнено): проект, команда, исполнитель,
/// внутреннее имя, дата-время начала, длительность, стоимость, валюта, токены (для ИИ).
/// Данные — проекция по jobs + tasks + executors + teams + projects; отдельной таблицы нет.
/// Позже добавятся записи по людям (пока стоимость 0).
/// </summary>
public sealed class BillingService
{
    private readonly Database _db;

    public BillingService(Database db) => _db = db;

    /// <summary>
    /// Записи биллинга с фильтрами (ТЗ v1.15): период по дате начала, проект, команда,
    /// исполнитель (ник), внутреннее имя ИИ = модель (modelId). Новые сверху.
    /// </summary>
    public List<BillingRecordDto> Query(DateTime? from = null, DateTime? to = null,
        string? projectId = null, string? teamId = null, string? executorId = null, string? modelId = null)
    {
        var where = new List<string> { "j.started_at IS NOT NULL", "j.deleted_at IS NULL" };
        var args = new List<(string, object?)>();
        void Add(string cond, string name, object? value)
        {
            where.Add(cond);
            args.Add((name, value));
        }

        if (from is not null) Add("j.started_at >= @from", "@from", Sql.ToDb(from.Value));
        if (to is not null) Add("j.started_at <= @to", "@to", Sql.ToDb(to.Value));
        if (projectId is not null) Add("t.project_id = @project", "@project", projectId);
        if (teamId is not null) Add("t.team_id = @team", "@team", teamId);
        if (executorId is not null) Add("j.executor_id = @executor", "@executor", executorId);
        if (modelId is not null) Add("e.model_id = @model", "@model", modelId);

        var sql = """
            SELECT j.id AS job_id, j.display_id AS job_display_id, j.executor_id, j.cost, j.currency,
                   j.input_tokens, j.output_tokens, j.started_at, j.finished_at,
                   t.display_id AS task_display_id, t.title AS task_title, t.project_id, t.team_id,
                   e.nick, e.internal_name, e.kind, e.model_id,
                   COALESCE(p.name, '') AS project_name, COALESCE(tm.name, '') AS team_name
            FROM jobs j
            JOIN tasks t     ON t.id = j.task_id
            JOIN executors e ON e.id = j.executor_id
            LEFT JOIN projects p ON p.id = t.project_id
            LEFT JOIN teams tm   ON tm.id = t.team_id
            WHERE
            """ + " " + string.Join(" AND ", where) + " ORDER BY j.started_at DESC";

        using var conn = _db.Open();
        return Sql.Query(conn, null, sql, Map, args.ToArray());
    }

    private static BillingRecordDto Map(SqliteDataReader r)
    {
        var started = r.Dt("started_at");
        var finished = r.DtN("finished_at");
        return new BillingRecordDto
        {
            JobId = r.S("job_id"),
            JobDisplayId = r.S("job_display_id"),
            ProjectId = r.SN("project_id"),
            ProjectName = r.S("project_name"),
            TeamId = r.SN("team_id"),
            TeamName = r.S("team_name"),
            ExecutorId = r.S("executor_id"),
            ExecutorNick = r.S("nick"),
            InternalName = r.S("internal_name"),
            Kind = EnumMap.ExecutorKindFromDb(r.S("kind")),
            ModelId = r.SN("model_id"),
            TaskDisplayId = r.S("task_display_id"),
            TaskTitle = r.S("task_title"),
            StartedAt = started,
            DurationSeconds = ((finished ?? DateTime.UtcNow) - started).TotalSeconds,
            Cost = r.D("cost"),
            Currency = r.S("currency"),
            InputTokens = r.L("input_tokens"),
            OutputTokens = r.L("output_tokens"),
        };
    }
}
