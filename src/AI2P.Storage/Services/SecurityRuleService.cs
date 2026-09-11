using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Правила безопасности (ТЗ гл. 12, todo25): три уровня — глобальные (настройки),
/// проекта (вкладка «безопасность» проекта), задачи (вкладка «безопасность» задачи).
/// Для конкретной задачи правила складываются: глобальные → проекта → цепочка задач
/// по иерархии (от корня к самой задаче); одинаковые (target+pattern+ops) перекрываются
/// нижним уровнем. Проверяет ядро ДО выполнения действия (п. 12.3) — см. SecurityEvaluator.
/// </summary>
public sealed class SecurityRuleService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public SecurityRuleService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>Правила одного уровня (для вкладок UI) — вместе с чужими: они видны,
    /// но не правятся и в оценке доступа на этом сервере не участвуют (ТЗ гл. 6, этап 42).</summary>
    public List<SecurityRule> List(string scope, string? scopeId)
    {
        using var conn = _db.Open();
        var rules = Sql.Query(conn, null, """
                SELECT * FROM security_rules
                WHERE deleted_at IS NULL AND scope=@scope
                  AND (@scopeId IS NULL AND scope_id IS NULL OR scope_id=@scopeId)
                ORDER BY created_at
                """, Map, ("@scope", scope), ("@scopeId", scopeId));
        foreach (var rule in rules)
        {
            Decorate(rule);
        }
        return rules;
    }

    public SecurityRule? Get(string id)
    {
        using var conn = _db.Open();
        var rule = Sql.Query(conn, null, "SELECT * FROM security_rules WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();
        return rule is null ? null : Decorate(rule);
    }

    /// <summary>
    /// Владелец правила (ТЗ гл. 6, этап 42). Правила доступа к ФАЙЛОВОЙ СИСТЕМЕ зависят от
    /// сервера — пути на разных компьютерах разные, — поэтому у них свой сервер, и правятся
    /// они только на нём. Все остальные правила безопасности от сервера не зависят и
    /// правятся на дирижёре (владельца нет).
    /// </summary>
    private string? OwnerFor(SecurityRule rule) =>
        rule.Target == SecurityTarget.Directory ? _scope.ServerId : null;

    private SecurityRule Decorate(SecurityRule rule)
    {
        rule.ServerCode = _scope.CodeOf(rule.ServerId);
        rule.IsReadOnly = !_scope.CanWrite(rule.ServerId);
        return rule;
    }

    public SecurityRule Create(SecurityRule rule, string? actorId)
    {
        Validate(rule);
        // правила доступа к ФС — свои на каждом сервере, остальные ведёт дирижёр (ТЗ гл. 6)
        rule.ServerId = OwnerFor(rule);
        _scope.EnsureCanWrite(rule.ServerId, Loc.T("msg.securityRule.1"));
        var now = DateTime.UtcNow;
        rule.CreatedAt = now;
        rule.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            INSERT INTO security_rules (id, scope, scope_id, kind, rule_json, server_id,
                                        created_at, updated_at)
            VALUES (@id, @scope, @scopeId, @kind, @rule, @server, @now, @now)
            """,
            ("@id", rule.Id), ("@scope", rule.Scope), ("@scopeId", rule.ScopeId),
            ("@kind", rule.Permission), ("@rule", RuleJson(rule)), ("@server", rule.ServerId),
            ("@now", Sql.ToDb(now)));
        AppendEvent(conn, tx, rule, "created", actorId);
        tx.Commit();
        return Decorate(rule);
    }

    public SecurityRule Update(SecurityRule rule, string? actorId)
    {
        Validate(rule);
        var now = DateTime.UtcNow;
        rule.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var old = Sql.Query(conn, tx, "SELECT * FROM security_rules WHERE id=@id AND deleted_at IS NULL",
                      Map, ("@id", rule.Id)).FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.securityRule.2", rule.Id));
        _scope.EnsureCanWrite(old.ServerId, Loc.T("msg.securityRule.1"));
        rule.ServerId = OwnerFor(rule);
        _scope.EnsureCanWrite(rule.ServerId, Loc.T("msg.securityRule.1"));
        Sql.Exec(conn, tx, """
            UPDATE security_rules SET kind=@kind, rule_json=@rule, server_id=@server, updated_at=@now
            WHERE id=@id
            """,
            ("@kind", rule.Permission), ("@rule", RuleJson(rule)), ("@server", rule.ServerId),
            ("@now", Sql.ToDb(now)), ("@id", rule.Id));
        AppendEvent(conn, tx, rule, "updated", actorId);
        tx.Commit();
        return Decorate(rule);
    }

    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var rule = Sql.Query(conn, tx, "SELECT * FROM security_rules WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();
        if (rule is null)
        {
            return;
        }
        _scope.EnsureCanWrite(rule.ServerId, Loc.T("msg.securityRule.1"));
        Sql.Exec(conn, tx, "UPDATE security_rules SET deleted_at=@now WHERE id=@id",
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        AppendEvent(conn, tx, rule, "deleted", actorId);
        tx.Commit();
    }

    /// <summary>
    /// Эффективный набор правил для задачи (ТЗ п. 12.1, todo25): глобальные → проекта →
    /// цепочка задач по иерархии от корня к самой задаче. Одинаковые правила
    /// (target + pattern + ops, без учёта регистра) перекрываются нижним уровнем.
    /// </summary>
    public List<SecurityRule> EffectiveForTask(TaskItem task)
    {
        using var conn = _db.Open();
        var layers = new List<List<SecurityRule>> { ListIn(conn, SecurityScope.Global, null) };
        if (task.ProjectId is not null)
        {
            layers.Add(ListIn(conn, SecurityScope.Project, task.ProjectId));
        }
        // цепочка задач: от корня иерархии к самой задаче (нижний уровень — последним)
        var chain = new List<string> { task.Id };
        var parentId = task.ParentId;
        while (parentId is not null && !chain.Contains(parentId))
        {
            chain.Add(parentId);
            parentId = Sql.Scalar<string>(conn, null,
                "SELECT parent_id FROM tasks WHERE id=@id", ("@id", parentId));
        }
        chain.Reverse();
        foreach (var taskId in chain)
        {
            layers.Add(ListIn(conn, SecurityScope.Task, taskId));
        }

        // складывание: нижний уровень перезаписывает одинаковое правило верхнего.
        // ЧУЖИЕ правила доступа к ФС отбрасываются (ТЗ гл. 6, этап 42): они описывают пути
        // другого компьютера, и применять их здесь бессмысленно и опасно
        var effective = new Dictionary<string, SecurityRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in layers.SelectMany(l => l).Where(r => IsForThisServer(r)))
        {
            effective[IdentityOf(rule)] = rule;
        }
        return effective.Values.ToList();
    }

    /// <summary>
    /// Правило действует на ЭТОМ сервере (ТЗ гл. 6, этап 42): правило доступа к ФС — только
    /// своё, остальные — любые (они от сервера не зависят). Правило без сервера считается
    /// общим: так выглядят правила, заведённые до появления кластера.
    /// </summary>
    private bool IsForThisServer(SecurityRule rule) =>
        rule.Target != SecurityTarget.Directory
        || rule.ServerId is not { Length: > 0 }
        || rule.ServerId == _scope.ServerId;

    /// <summary>Идентичность правила для перекрытия уровней: target + pattern + ops.</summary>
    private static string IdentityOf(SecurityRule rule) =>
        $"{rule.Target}|{rule.Pattern.Trim()}|{(rule.OpRead ? "r" : "")}{(rule.OpWrite ? "w" : "")}"
        + $"{(rule.OpDelete ? "d" : "")}{(rule.OpUse ? "u" : "")}{(rule.OpRun ? "x" : "")}";

    private static void Validate(SecurityRule rule)
    {
        if (rule.Scope is not (SecurityScope.Global or SecurityScope.Project or SecurityScope.Task))
        {
            throw new ArgumentException(Loc.T("msg.securityRule.3", rule.Scope));
        }
        if (rule.Scope != SecurityScope.Global && string.IsNullOrWhiteSpace(rule.ScopeId))
        {
            throw new ArgumentException(Loc.T("msg.securityRule.4"));
        }
        if (rule.Scope == SecurityScope.Global)
        {
            rule.ScopeId = null;
        }
        if (rule.Target is not (SecurityTarget.Action or SecurityTarget.OsCommand
            or SecurityTarget.Directory or SecurityTarget.Plugin))
        {
            throw new ArgumentException(Loc.T("msg.securityRule.5", rule.Target));
        }
        if (rule.Permission is not (SecurityPermission.Allow or SecurityPermission.Confirm or SecurityPermission.Deny))
        {
            throw new ArgumentException(Loc.T("msg.securityRule.6", rule.Permission));
        }
        if (rule.Pattern.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.securityRule.7"));
        }
        rule.Pattern = rule.Pattern.Trim();
        if (rule.Target == SecurityTarget.Directory)
        {
            if (!rule.OpRead && !rule.OpWrite && !rule.OpDelete)
            {
                throw new ArgumentException(Loc.T("msg.securityRule.8"));
            }
            // буква диска кириллицей («с:» вместо «c:») — паттерн никогда не совпадёт (todo25-фикс)
            if (SecurityRulePaths.HasCyrillicDriveLetter(rule.Pattern))
            {
                throw new ArgumentException(
                    Loc.T("msg.securityRule.9", rule.Pattern.Trim()[0]));
            }
            // на глобальном уровне относительного пути не может быть (todo25);
            // «полный путь» — независимо от ОС: X:\…, /…, \\… (SecurityRulePaths)
            if (rule.Scope == SecurityScope.Global && !SecurityRulePaths.IsFullPath(rule.Pattern))
            {
                throw new ArgumentException(
                    Loc.T("msg.securityRule.10"));
            }
        }
        else
        {
            rule.OpRead = rule.OpWrite = rule.OpDelete = false;
        }
        // ПЛАГИНЫ И MCP (T-155-S0): у правила две операции — использовать (вызов инструмента
        // агентом) и запускать (работа программы заданием «авто ПО»). Правило без единой
        // операции не значит ничего и молча не срабатывало бы — отказываем сразу
        if (rule.Target == SecurityTarget.Plugin)
        {
            if (!rule.OpUse && !rule.OpRun)
            {
                throw new ArgumentException(Loc.T("msg.securityRule.11"));
            }
        }
        else
        {
            rule.OpUse = rule.OpRun = false;
        }
    }

    private static string RuleJson(SecurityRule rule)
    {
        var ops = new List<string>();
        if (rule.OpRead) { ops.Add("read"); }
        if (rule.OpWrite) { ops.Add("write"); }
        if (rule.OpDelete) { ops.Add("delete"); }
        if (rule.OpUse) { ops.Add("use"); }
        if (rule.OpRun) { ops.Add("run"); }
        return JsonSerializer.Serialize(new { target = rule.Target, pattern = rule.Pattern, ops });
    }

    /// <summary>Срабатывания и правки правил — события журнала (ТЗ п. 12.3).</summary>
    private void AppendEvent(SqliteConnection conn, SqliteTransaction tx, SecurityRule rule,
        string operation, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = rule.Scope == SecurityScope.Project ? rule.ScopeId : null,
            TaskId = rule.Scope == SecurityScope.Task ? rule.ScopeId : null,
            EventType = "rule." + operation,
            EntityType = "security_rule",
            EntityId = rule.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                rule.Scope,
                rule.Target,
                rule.Permission,
                rule.Pattern,
                op = operation,
            }),
        });

    private static List<SecurityRule> ListIn(SqliteConnection conn, string scope, string? scopeId) =>
        Sql.Query(conn, null, """
            SELECT * FROM security_rules
            WHERE deleted_at IS NULL AND scope=@scope
              AND (@scopeId IS NULL AND scope_id IS NULL OR scope_id=@scopeId)
            ORDER BY created_at
            """, Map, ("@scope", scope), ("@scopeId", scopeId));

    private static SecurityRule Map(SqliteDataReader r)
    {
        var rule = new SecurityRule
        {
            Id = r.S("id"),
            Scope = r.S("scope"),
            ScopeId = r.SN("scope_id"),
            Permission = r.S("kind"),
            ServerId = r.Has("server_id") ? r.SN("server_id") : null,
            CreatedAt = r.Dt("created_at"),
            UpdatedAt = r.Dt("updated_at"),
            DeletedAt = r.DtN("deleted_at"),
        };
        try
        {
            using var doc = JsonDocument.Parse(r.S("rule_json"));
            var root = doc.RootElement;
            rule.Target = root.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
            rule.Pattern = root.TryGetProperty("pattern", out var p) ? p.GetString() ?? "" : "";
            if (root.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var op in ops.EnumerateArray())
                {
                    switch (op.GetString())
                    {
                        case "read": rule.OpRead = true; break;
                        case "write": rule.OpWrite = true; break;
                        case "delete": rule.OpDelete = true; break;
                        case "use": rule.OpUse = true; break;
                        case "run": rule.OpRun = true; break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // повреждённый rule_json — правило без области действия (не сработает)
        }
        return rule;
    }
}
