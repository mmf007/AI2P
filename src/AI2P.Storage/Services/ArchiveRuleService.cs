using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ПРАВИЛА АРХИВАЦИИ (T-41-S0, выпуск 1.105): что и какой давности пора уносить в архив.
///
/// Групп правил ДВЕ, а состав полей записи у них ОДИН И ТОТ ЖЕ, поэтому и таблица одна,
/// и сервис один:
/// <list type="bullet">
/// <item><b>общие правила организации</b> — закладка «Настройки → Справочники». Это ОБРАЗЕЦ:
/// с него копируются правила нового архива. У такой записи <c>archive_id</c> пуст;</item>
/// <item><b>правила архива</b> — свои у каждого архива, правятся с его записи в списке
/// архивов. У такой записи <c>archive_id</c> заполнен.</item>
/// </list>
/// Признак группы ПРОИЗВОДНЫЙ (<see cref="ArchiveRule.IsCommon"/>), отдельной колонки нет:
/// колонка рядом со ссылкой разъезжается с ней молча — та же наука, что у третьего вида
/// записей опыта (T-11-S0).
///
/// Отбором кандидатов на архивацию сервис НЕ занимается: это отдельный класс
/// <see cref="ArchiveCandidateService"/> — он нужен и автоматической архивации (T-46-S0),
/// и форме добавления архива (T-43-S0), которая проверяет, выполнены ли правила у прежнего
/// текущего архива.
/// </summary>
public sealed class ArchiveRuleService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ArchiveRuleService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>
    /// Правила одной группы. <paramref name="archiveId"/> пуст — ОБЩИЕ правила организации,
    /// иначе правила этого архива.
    /// </summary>
    public List<ArchiveRule> List(string? archiveId = null, bool includeInactive = true)
    {
        using var conn = _db.Open();
        return Select(conn, null, archiveId, includeInactive);
    }

    public ArchiveRule? Get(string id)
    {
        using var conn = _db.Open();
        return Load(conn, null, id);
    }

    public ArchiveRule Create(ArchiveRule rule, string? actorId)
    {
        Validate(rule);
        var now = DateTime.UtcNow;
        rule.CreatedAt = now;
        rule.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureArchive(conn, tx, rule.ArchiveId);
        rule.DisplayId = Database.NextDisplayId(conn, tx, "ARR", _scope.Code);
        Insert(conn, tx, rule, now);
        Append(conn, tx, EventTypes.ArchiveRuleCreated, rule, actorId);
        tx.Commit();
        return rule;
    }

    public ArchiveRule Update(ArchiveRule rule, string? actorId)
    {
        Validate(rule);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var old = Load(conn, tx, rule.Id)
                  ?? throw new InvalidOperationException(Loc.T("msg.arcrule.1", rule.Id));
        // правила НЕ текущего архива не правятся вовсе (T-202-S0)
        EnsureArchive(conn, tx, old.ArchiveId);
        // группу правила правкой не меняют: общее правило — образец организации, правило
        // архива принадлежит архиву, и «переехать» из одной группы в другую значило бы
        // молча испортить образец
        rule.DisplayId = old.DisplayId;
        rule.ArchiveId = old.ArchiveId;
        Sql.Exec(conn, tx, """
            UPDATE archive_rules SET target=@target, task_status=@status,
                                     child_statuses=@children, activity=@activity,
                                     date_field=@date, age_kind=@kind, age_days=@days,
                                     age_months=@months, age_years=@years, is_active=@active,
                                     updated_at=@updated
            WHERE id=@id
            """,
            ("@target", rule.Target), ("@status", rule.TaskStatus),
            ("@children", Pack(rule.ChildStatuses)), ("@activity", rule.Activity),
            ("@date", rule.DateField), ("@kind", rule.AgeKind), ("@days", rule.AgeDays),
            ("@months", rule.AgeMonths), ("@years", rule.AgeYears),
            ("@active", rule.IsActive ? 1 : 0), ("@updated", Sql.ToDb(now)), ("@id", rule.Id));
        Append(conn, tx, EventTypes.ArchiveRuleUpdated, rule, actorId);
        tx.Commit();
        rule.UpdatedAt = now;
        return rule;
    }

    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var rule = Load(conn, tx, id)
                   ?? throw new InvalidOperationException(Loc.T("msg.arcrule.1", id));
        EnsureArchive(conn, tx, rule.ArchiveId);   // только у текущего архива (T-202-S0)
        Remove(conn, tx, rule, DateTime.UtcNow);
        Append(conn, tx, EventTypes.ArchiveRuleDeleted, rule, actorId);
        tx.Commit();
    }

    // --- перенос правил ---

    /// <summary>
    /// «ВЕРНУТЬ УМОЛЧАНИЕ» на форме правил архива — снести все правила архива и скопировать
    /// общие. Кнопка обязательна по заданию: правила архива человек правит руками, и вернуть
    /// их к образцу иначе нечем (общее правило проекта: у служебного состояния, которое
    /// человек видит, обязана быть кнопка, которая его убирает — T-20-S1).
    /// </summary>
    public List<ArchiveRule> ResetToCommon(string archiveId, string? actorId) =>
        Replace(archiveId, null, ArchiveRuleModes.Common, actorId);

    /// <summary>
    /// ПЕРЕНОС ПРАВИЛ В НОВЫЙ АРХИВ — то, что выбирает человек в форме создания архива
    /// (T-43-S0): взять общие правила организации, скопировать правила ПРЕЖНЕГО текущего
    /// архива или не копировать ничего. Метод публичный и самодостаточный именно затем,
    /// чтобы его звала форма; сервер зовёт его же из <see cref="ArchiveService.Create"/>,
    /// так что форме достаточно передать выбранный способ в теле создания архива.
    /// </summary>
    /// <param name="archiveId">Новый архив — получатель правил.</param>
    /// <param name="sourceArchiveId">Прежний текущий архив; нужен только способу
    /// <see cref="ArchiveRuleModes.Copy"/>. Пусто — копировать не с чего, правил не будет.</param>
    public List<ArchiveRule> Replace(string archiveId, string? sourceArchiveId, string mode,
        string? actorId)
    {
        mode = (mode ?? "").Trim();
        mode = mode.Length == 0 ? ArchiveRuleModes.Common : mode;
        if (!ArchiveRuleModes.All.Contains(mode))
        {
            throw new ArgumentException(Loc.T("msg.arc.6", mode));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureArchive(conn, tx, archiveId);
        // прежние правила архива уходят ЦЕЛИКОМ: и «вернуть умолчание», и перенос при
        // создании — замена набора, а не дополнение его сверху
        foreach (var old in Select(conn, tx, archiveId, includeInactive: true))
        {
            Remove(conn, tx, old, now);
        }
        var source = mode switch
        {
            ArchiveRuleModes.Common => Select(conn, tx, null, includeInactive: true),
            ArchiveRuleModes.Copy when sourceArchiveId is { Length: > 0 } =>
                Select(conn, tx, sourceArchiveId, includeInactive: true),
            _ => [],
        };
        var copies = new List<ArchiveRule>();
        foreach (var rule in source)
        {
            var copy = Copy(rule, archiveId, now);
            copy.DisplayId = Database.NextDisplayId(conn, tx, "ARR", _scope.Code);
            Insert(conn, tx, copy, now);
            copies.Add(copy);
        }
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ArchiveRulesReset,
            EntityType = "archive",
            EntityId = archiveId,
            PayloadJson = JsonSerializer.Serialize(new { Mode = mode, Count = copies.Count }),
        });
        tx.Commit();
        return copies;
    }

    // --- внутреннее ---

    private static ArchiveRule Copy(ArchiveRule rule, string archiveId, DateTime now) => new()
    {
        ArchiveId = archiveId,
        Target = rule.Target,
        TaskStatus = rule.TaskStatus,
        ChildStatuses = [.. rule.ChildStatuses],
        Activity = rule.Activity,
        DateField = rule.DateField,
        AgeKind = rule.AgeKind,
        AgeDays = rule.AgeDays,
        AgeMonths = rule.AgeMonths,
        AgeYears = rule.AgeYears,
        IsActive = rule.IsActive,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Правило про задачи обязано называть состояние задачи, остальные виды — активность
    /// данных; срок обязан быть заданным, иначе правило означало бы «унести всё сразу».
    /// </summary>
    private static void Validate(ArchiveRule rule)
    {
        rule.Target = (rule.Target ?? "").Trim();
        if (!ArchiveRuleTargets.IsKnown(rule.Target))
        {
            throw new ArgumentException(Loc.T("msg.arcrule.2", rule.Target));
        }
        if (!ArchiveRuleDates.IsKnown(rule.DateField))
        {
            throw new ArgumentException(Loc.T("msg.arcrule.3", rule.DateField));
        }
        if (!ArchiveRuleAges.IsKnown(rule.AgeKind))
        {
            throw new ArgumentException(Loc.T("msg.arcrule.4", rule.AgeKind));
        }
        if (!ArchiveRuleActivity.IsKnown(rule.Activity))
        {
            throw new ArgumentException(Loc.T("msg.arcrule.5", rule.Activity));
        }
        rule.TaskStatus = (rule.TaskStatus ?? "").Trim();
        if (ArchiveRuleTargets.HasTaskStatus(rule.Target))
        {
            if (rule.TaskStatus.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.arcrule.6"));
            }
        }
        else
        {
            // состояние задачи спрашивается только у правил про задачи — у остальных видов
            // его чистим, чтобы поле не осталось от прежнего выбора человека в форме
            rule.TaskStatus = "";
        }
        rule.ChildStatuses = [.. (rule.ChildStatuses ?? [])
            .Select(s => (s ?? "").Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)];
        rule.AgeDays = Math.Max(0, rule.AgeDays);
        rule.AgeMonths = Math.Max(0, rule.AgeMonths);
        rule.AgeYears = Math.Max(0, rule.AgeYears);
        var age = rule.AgeKind == ArchiveRuleAges.Days
            ? rule.AgeDays
            : rule.AgeMonths + rule.AgeYears;
        if (age <= 0)
        {
            throw new ArgumentException(Loc.T("msg.arcrule.7"));
        }
    }

    /// <summary>
    /// Архив правила обязан существовать и быть ТЕКУЩИМ (T-202-S0). Правила отвечают на
    /// вопрос «что пора унести в архив», а перенос идёт только в текущий архив — у прочих
    /// настраивать нечего, и кнопки правки правил у них в списке архивов нет. Проверка стоит
    /// и на сервере: спрятанная кнопка не закрывает прямой вызов API.
    /// </summary>
    private void EnsureArchive(SqliteConnection conn, SqliteTransaction? tx, string? archiveId)
    {
        if (archiveId is not { Length: > 0 })
        {
            return;     // общее правило организации — архива у него нет вовсе
        }
        var display = Sql.Scalar<string>(conn, tx,
            "SELECT display_id FROM archives WHERE id=@id AND deleted_at IS NULL",
            ("@id", archiveId));
        if (display is null)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.2", archiveId));
        }
        var current = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM archives WHERE id=@id AND is_current=1 AND deleted_at IS NULL",
            ("@id", archiveId));
        if (current == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.arcrule.8", display));
        }
    }

    private static List<ArchiveRule> Select(SqliteConnection conn, SqliteTransaction? tx,
        string? archiveId, bool includeInactive)
    {
        var own = archiveId is { Length: > 0 };
        (string, object?)[] args = own ? [("@a", archiveId)] : [];
        return Sql.Query(conn, tx,
            "SELECT * FROM archive_rules WHERE deleted_at IS NULL AND "
            + (own ? "archive_id=@a" : "archive_id IS NULL")
            + (includeInactive ? "" : " AND is_active=1")
            + " ORDER BY created_at",
            Map, args);
    }

    private static ArchiveRule? Load(SqliteConnection conn, SqliteTransaction? tx, string id) =>
        Sql.Query(conn, tx, "SELECT * FROM archive_rules WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();

    private static void Insert(SqliteConnection conn, SqliteTransaction tx, ArchiveRule rule,
        DateTime now) =>
        Sql.Exec(conn, tx, """
            INSERT INTO archive_rules (id, display_id, archive_id, target, task_status,
                                       child_statuses, activity, date_field, age_kind,
                                       age_days, age_months, age_years, is_active,
                                       created_at, updated_at)
            VALUES (@id, @did, @archive, @target, @status, @children, @activity, @date,
                    @kind, @days, @months, @years, @active, @created, @updated)
            """,
            ("@id", rule.Id), ("@did", rule.DisplayId),
            ("@archive", rule.ArchiveId is { Length: > 0 } ? rule.ArchiveId : null),
            ("@target", rule.Target), ("@status", rule.TaskStatus),
            ("@children", Pack(rule.ChildStatuses)), ("@activity", rule.Activity),
            ("@date", rule.DateField), ("@kind", rule.AgeKind), ("@days", rule.AgeDays),
            ("@months", rule.AgeMonths), ("@years", rule.AgeYears),
            ("@active", rule.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));

    private static void Remove(SqliteConnection conn, SqliteTransaction tx, ArchiveRule rule,
        DateTime now) =>
        Sql.Exec(conn, tx,
            "UPDATE archive_rules SET deleted_at=@now, updated_at=@now WHERE id=@id",
            ("@now", Sql.ToDb(now)), ("@id", rule.Id));

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        ArchiveRule rule, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "archive_rule",
            EntityId = rule.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                rule.DisplayId,
                rule.Target,
                rule.ArchiveId,
            }),
        });

    /// <summary>Состояния потомков одной строкой: список коротких кодов без запятых внутри.</summary>
    private static string Pack(List<string> statuses) => string.Join(",", statuses);

    internal static List<string> Unpack(string packed) =>
        [.. packed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    internal static ArchiveRule Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        ArchiveId = r.SN("archive_id"),
        Target = r.S("target"),
        TaskStatus = r.S("task_status"),
        ChildStatuses = Unpack(r.S("child_statuses")),
        Activity = r.S("activity"),
        DateField = r.S("date_field"),
        AgeKind = r.S("age_kind"),
        AgeDays = (int)r.L("age_days"),
        AgeMonths = (int)r.L("age_months"),
        AgeYears = (int)r.L("age_years"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
