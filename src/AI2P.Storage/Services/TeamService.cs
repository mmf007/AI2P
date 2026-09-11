using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Команды и их состав (ТЗ п. 2.8).</summary>
public sealed class TeamService
{
    private readonly Database _db;
    private readonly EventStore _events;

    public TeamService(Database db, EventStore events)
    {
        _db = db;
        _events = events;
    }

    public List<Team> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM teams" + (includeDeleted ? "" : " WHERE deleted_at IS NULL") + " ORDER BY name";
        var teams = Sql.Query(conn, null, sql, Map);
        foreach (var team in teams)
        {
            team.Members = LoadMembers(conn, team.Id);
        }
        return teams;
    }

    public Team? Get(string id)
    {
        using var conn = _db.Open();
        var team = Sql.Query(conn, null, "SELECT * FROM teams WHERE id=@id", Map, ("@id", id)).FirstOrDefault();
        if (team is not null)
        {
            team.Members = LoadMembers(conn, team.Id);
        }
        return team;
    }

    public Team Create(Team team, string? actorId)
    {
        if (team.Name.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.team.1"));
        }
        var now = DateTime.UtcNow;
        team.Name = team.Name.Trim();
        team.CreatedAt = now;
        team.UpdatedAt = now;

        NormalizeHierarchy(team);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, team.Name, team.Id);

        // неактивный исполнитель не подставляется в новые команды (ТЗ п. 2.2)
        foreach (var member in team.Members)
        {
            var active = Sql.Scalar<long?>(conn, tx,
                "SELECT is_active FROM executors WHERE id=@id AND deleted_at IS NULL",
                ("@id", member.ExecutorId));
            if (active is null or 0)
            {
                throw new ArgumentException(Loc.T("msg.team.2"));
            }
        }

        team.DisplayId = Database.NextDisplayId(conn, tx, "TM");

        Sql.Exec(conn, tx, """
            INSERT INTO teams (id, display_id, name, project_id, agent_language, is_active, created_at, updated_at)
            VALUES (@id, @did, @name, @project, @lang, @active, @created, @updated)
            """,
            ("@id", team.Id), ("@did", team.DisplayId), ("@name", team.Name),
            ("@project", team.ProjectId), ("@lang", team.AgentLanguage),
            ("@active", team.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        SaveMembers(conn, tx, team);

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = team.ProjectId,
            EventType = EventTypes.TeamCreated,
            EntityType = "team",
            EntityId = team.Id,
            PayloadJson = JsonSerializer.Serialize(new { team.DisplayId, team.Name, members = team.Members.Count }),
        });

        tx.Commit();
        return team;
    }

    public Team Update(Team team, string? actorId)
    {
        var now = DateTime.UtcNow;
        team.Name = team.Name.Trim();
        NormalizeHierarchy(team);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, team.Name, team.Id);

        Sql.Exec(conn, tx, """
            UPDATE teams SET name=@name, project_id=@project, agent_language=@lang, is_active=@active,
                             updated_at=@updated
            WHERE id=@id
            """,
            ("@name", team.Name), ("@project", team.ProjectId), ("@lang", team.AgentLanguage),
            ("@active", team.IsActive ? 1 : 0),
            ("@updated", Sql.ToDb(now)), ("@id", team.Id));
        Sql.Exec(conn, tx, "DELETE FROM team_members WHERE team_id=@id", ("@id", team.Id));
        SaveMembers(conn, tx, team);

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = team.ProjectId,
            EventType = EventTypes.TeamUpdated,
            EntityType = "team",
            EntityId = team.Id,
            PayloadJson = JsonSerializer.Serialize(new { team.Name, members = team.Members.Count }),
        });

        tx.Commit();
        team.UpdatedAt = now;
        return team;
    }

    /// <summary>Имя команды уникально (ТЗ гл. 11, форма ввода команды; правка v1.13).</summary>
    private static void EnsureUniqueName(SqliteConnection conn, SqliteTransaction tx, string name, string selfId)
    {
        var taken = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM teams WHERE name=@name AND id<>@id AND deleted_at IS NULL",
            ("@name", name), ("@id", selfId)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.team.3", name));
        }
    }

    /// <summary>Входит ли исполнитель в команду — проверка ядра при назначении (ТЗ п. 6.4.2).</summary>
    public bool IsMember(string teamId, string executorId)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM team_members WHERE team_id=@t AND executor_id=@e",
            ("@t", teamId), ("@e", executorId)) > 0;
    }

    /// <summary>
    /// Проверка и нормализация иерархии команды (ТЗ v1.17, todo20):
    /// руководитель — участник этой же команды, циклы запрещены;
    /// тимлид — только участник верхнего уровня, не более одного;
    /// единственный участник верхнего уровня становится тимлидом автоматически.
    /// </summary>
    public static void NormalizeHierarchy(Team team)
    {
        // дубликаты и пустые исполнители — понятная ошибка (багфикс v1.18: раньше дубликат
        // ронял ToDictionary и в UI, и на сервере)
        if (team.Members.Any(m => m.ExecutorId.Trim().Length == 0))
        {
            throw new ArgumentException(Loc.T("msg.team.4"));
        }
        if (team.Members.GroupBy(m => m.ExecutorId).Any(g => g.Count() > 1))
        {
            throw new ArgumentException(Loc.T("msg.team.5"));
        }
        var ids = team.Members.Select(m => m.ExecutorId).ToHashSet();
        var byId = team.Members.ToDictionary(m => m.ExecutorId);
        foreach (var member in team.Members)
        {
            if (member.ParentExecutorId == member.ExecutorId)
            {
                throw new ArgumentException(Loc.T("msg.team.6"));
            }
            if (member.ParentExecutorId is { } parent && !ids.Contains(parent))
            {
                throw new ArgumentException(Loc.T("msg.team.7"));
            }
        }
        foreach (var member in team.Members)
        {
            // проверка циклов: подъём по руководителям не должен вернуться в исходного
            var seen = new HashSet<string> { member.ExecutorId };
            for (var up = member.ParentExecutorId; up is not null; up = byId[up].ParentExecutorId)
            {
                if (!seen.Add(up))
                {
                    throw new ArgumentException(Loc.T("msg.team.8"));
                }
            }
        }

        var roots = team.Members.Where(m => m.ParentExecutorId is null).ToList();
        foreach (var member in team.Members.Where(m => m.IsLead && m.ParentExecutorId is not null))
        {
            // тимлид — только верхний уровень (ТЗ v1.17): у подчинённого признак снимается
            member.IsLead = false;
        }
        if (roots.Count == 1)
        {
            roots[0].IsLead = true; // единственный на верхнем уровне — тимлид автоматически
        }
        if (team.Members.Count(m => m.IsLead) > 1)
        {
            throw new ArgumentException(Loc.T("msg.team.9"));
        }
    }

    private static void SaveMembers(SqliteConnection conn, SqliteTransaction tx, Team team)
    {
        foreach (var member in team.Members)
        {
            Sql.Exec(conn, tx, """
                INSERT OR REPLACE INTO team_members
                    (team_id, executor_id, role_id, parent_executor_id, is_lead, is_active)
                VALUES (@team, @executor, @role, @parent, @lead, @active)
                """,
                ("@team", team.Id), ("@executor", member.ExecutorId), ("@role", member.RoleId),
                ("@parent", member.ParentExecutorId), ("@lead", member.IsLead ? 1 : 0),
                ("@active", member.IsActive ? 1 : 0));
        }
    }

    private static List<TeamMember> LoadMembers(SqliteConnection conn, string teamId) =>
        Sql.Query(conn, null,
            "SELECT executor_id, role_id, parent_executor_id, is_lead, is_active FROM team_members WHERE team_id=@id",
            r => new TeamMember
            {
                ExecutorId = r.S("executor_id"),
                RoleId = r.SN("role_id"),
                ParentExecutorId = r.SN("parent_executor_id"),
                IsLead = r.B("is_lead"),
                IsActive = r.B("is_active"),
            },
            ("@id", teamId));

    private static Team Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        ProjectId = r.SN("project_id"),
        AgentLanguage = r.S("agent_language"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
