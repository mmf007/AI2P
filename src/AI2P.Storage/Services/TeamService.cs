using AI2P.Core;
using AI2P.Core.Api;
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
            FillUnits(conn, team);
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
            FillUnits(conn, team);
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

        PrepareHierarchy(team, unitsRule: team.Units.Count > 0);
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
        SaveUnits(conn, tx, team);

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
        using var conn = _db.Open();
        var unitsRule = team.Units.Count > 0;
        if (!unitsRule)
        {
            // единиц в запросе нет (форма участника, API прежнего вида): берём сохранённые и
            // согласуем их с присланным составом
            team.Units = LoadUnits(conn, null, team.Id);
        }
        PrepareHierarchy(team, unitsRule);
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
        SaveUnits(conn, tx, team);

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

    // ---------------- правка одной оргединицы (T-629-S0) ----------------

    /// <summary>Добавить или изменить ОДНУ единицу (форма участника). Исполнитель единицы
    /// входит в состав; ушедший с неё — выбывает.</summary>
    public Team SaveUnit(string teamId, TeamUnitEditDto dto, string? actorId) =>
        MutateUnits(teamId, actorId, (conn, team) =>
        {
            var unit = dto.Unit;
            var existing = team.Units.FirstOrDefault(u => u.Id == unit.Id);
            var executorId = unit.ExecutorId is { Length: > 0 } e ? e : null;
            if (executorId is not null && team.Members.All(m => m.ExecutorId != executorId))
            {
                // неактивный исполнитель в команду не подставляется (ТЗ п. 2.2)
                var active = Sql.Scalar<long?>(conn, null,
                    "SELECT is_active FROM executors WHERE id=@id AND deleted_at IS NULL", ("@id", executorId));
                if (active is null or 0)
                {
                    throw new ArgumentException(Loc.T("msg.team.2"));
                }
                team.Members.Add(new TeamMember { ExecutorId = executorId, IsActive = unit.IsActive });
            }
            if (existing is null)
            {
                existing = new TeamUnit
                {
                    Id = unit.Id,
                    Sort = team.Units.Count == 0 ? 0 : team.Units.Max(u => u.Sort) + 1,
                };
                team.Units.Add(existing);
            }
            if (unit.ParentUnitId is { } p && (p == existing.Id || FindIn(team, p) is not { } parent
                                               || IsInside(team.Units, parent, existing)))
            {
                throw new ArgumentException(Loc.T("msg.team.12"));
            }
            existing.ParentUnitId = unit.ParentUnitId is { Length: > 0 } ? unit.ParentUnitId : null;
            existing.Kind = unit.Kind;
            existing.Name = unit.Name;
            existing.ExecutorId = executorId;
            existing.IsActive = unit.IsActive;
            existing.SkillIds = unit.SkillIds.Distinct().ToList();
            if (team.Members.FirstOrDefault(m => m.ExecutorId == executorId) is { } member)
            {
                member.RoleId = dto.RoleId;
                member.IsActive = unit.IsActive;
                member.IsLead = dto.IsLead && existing.ParentUnitId is null;
                if (member.IsLead)
                {
                    foreach (var other in team.Members.Where(m => m != member))
                    {
                        other.IsLead = false; // тимлид один (ТЗ v1.17)
                    }
                }
            }
        });

    /// <summary>Удалить единицу; вложенные поднимаются на её уровень.</summary>
    public Team DeleteUnit(string teamId, string unitId, string? actorId) =>
        MutateUnits(teamId, actorId, (_, team) =>
        {
            var unit = FindIn(team, unitId) ?? throw new ArgumentException(Loc.T("msg.team.11"));
            team.Units.Remove(unit);
            foreach (var child in team.Units.Where(u => u.ParentUnitId == unit.Id))
            {
                child.ParentUnitId = unit.ParentUnitId;
            }
        });

    /// <summary>Сменить активность единицы и её исполнителя в этой команде (T-129).</summary>
    public Team SetUnitActive(string teamId, string unitId, bool active, string? actorId) =>
        MutateUnits(teamId, actorId, (_, team) =>
        {
            var unit = FindIn(team, unitId) ?? throw new ArgumentException(Loc.T("msg.team.11"));
            unit.IsActive = active;
            if (team.Members.FirstOrDefault(m => m.ExecutorId == unit.ExecutorId) is { } member)
            {
                member.IsActive = active;
            }
        });

    /// <summary>Перенос в иерархии: сменить вышестоящую единицу. Внутрь своего же поддерева
    /// нельзя — понятный отказ, а не цикл.</summary>
    public Team MoveUnit(string teamId, string unitId, string? parentUnitId, string? actorId) =>
        MutateUnits(teamId, actorId, (_, team) =>
        {
            var unit = FindIn(team, unitId) ?? throw new ArgumentException(Loc.T("msg.team.11"));
            if (parentUnitId is not null)
            {
                var parent = FindIn(team, parentUnitId) ?? throw new ArgumentException(Loc.T("msg.team.11"));
                if (IsInside(team.Units, parent, unit))
                {
                    throw new ArgumentException(Loc.T("msg.team.12"));
                }
            }
            unit.ParentUnitId = parentUnitId;
        });

    private static TeamUnit? FindIn(Team team, string id) => team.Units.FirstOrDefault(u => u.Id == id);

    /// <summary>Общий ход правки одной единицы: прочитать команду (у старой команды единицы
    /// достраиваются), применить правку, вывести подчинение из единиц и записать ТОЛЬКО
    /// изменившиеся строки единиц и состава — строку самой команды не трогаем.</summary>
    private Team MutateUnits(string teamId, string? actorId, Action<SqliteConnection, Team> change)
    {
        var team = Get(teamId) ?? throw new ArgumentException(Loc.T("msg.team.11"));
        using var conn = _db.Open();
        change(conn, team);
        // состав команды — исполнители единиц: ушедший с единицы выбывает
        team.Members.RemoveAll(m => team.Units.All(u => u.ExecutorId != m.ExecutorId));
        PrepareHierarchy(team, unitsRule: true);
        using var tx = conn.BeginTransaction();
        SaveMembersByRow(conn, tx, team);
        SaveUnits(conn, tx, team);
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
        return Get(teamId)!;
    }

    /// <summary>Состав по строкам: удаляются выбывшие, пишутся только изменившиеся.</summary>
    private static void SaveMembersByRow(SqliteConnection conn, SqliteTransaction tx, Team team)
    {
        var stored = Sql.Query(conn, tx,
            "SELECT executor_id, role_id, parent_executor_id, is_lead, is_active FROM team_members WHERE team_id=@id",
            r => new TeamMember
            {
                ExecutorId = r.S("executor_id"),
                RoleId = r.SN("role_id"),
                ParentExecutorId = r.SN("parent_executor_id"),
                IsLead = r.B("is_lead"),
                IsActive = r.B("is_active"),
            },
            ("@id", team.Id)).ToDictionary(m => m.ExecutorId);
        foreach (var gone in stored.Keys.Where(e => team.Members.All(m => m.ExecutorId != e)))
        {
            Sql.Exec(conn, tx, "DELETE FROM team_members WHERE team_id=@t AND executor_id=@e",
                ("@t", team.Id), ("@e", gone));
        }
        foreach (var member in team.Members)
        {
            if (stored.TryGetValue(member.ExecutorId, out var old) && old.RoleId == member.RoleId
                && old.ParentExecutorId == member.ParentExecutorId && old.IsLead == member.IsLead
                && old.IsActive == member.IsActive)
            {
                continue;
            }
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

    // ---------------- оргединицы (T-628-S0) ----------------

    /// <summary>Проверка состава и единиц перед записью: присланный состав проверяется
    /// прежними правилами (понятные ошибки про руководителя вне команды и циклы), потом
    /// единицы согласуются с ним, и итоговое подчинение проверяется ещё раз.</summary>
    private static void PrepareHierarchy(Team team, bool unitsRule)
    {
        if (!unitsRule)
        {
            NormalizeHierarchy(team);
        }
        ReconcileUnits(team, unitsRule);
        NormalizeHierarchy(team);
    }

    /// <summary>Навыки (id) оргединицы, которую исполнитель занимает в команде; пусто — нет такой.</summary>
    public static List<string> UnitSkillIdsOf(Team team, string executorId) =>
        team.Units.FirstOrDefault(u => u.ExecutorId == executorId && u.IsActive)?.SkillIds ?? [];

    /// <summary>
    /// Согласовать единицы команды с её составом. Два режима:
    /// <list type="bullet">
    /// <item><paramref name="unitsRule"/> = true — правит редактор единиц: единицы главные,
    /// участник без единицы получает должность без названия верхнего уровня, подчинение
    /// участников выводится из единиц (руководитель — исполнитель ближайшей вышестоящей
    /// единицы с исполнителем).</item>
    /// <item>false — главный состав (старая команда без единиц, правка участника из другой
    /// формы, API прежнего вида, строки, приехавшие от сервера прежней версии): единицы
    /// достраиваются и переставляются так, чтобы подчинение совпало с составом.</item>
    /// </list>
    /// </summary>
    public static void ReconcileUnits(Team team, bool unitsRule)
    {
        if (unitsRule)
        {
            // исполнитель, поставленный на единицу, входит в состав команды
            foreach (var executorId in team.Units.Select(u => u.ExecutorId).OfType<string>()
                         .Where(e => e.Length > 0 && team.Members.All(m => m.ExecutorId != e)).Distinct().ToList())
            {
                team.Members.Add(new TeamMember { ExecutorId = executorId });
            }
        }
        var members = team.Members.Where(m => m.ExecutorId.Length > 0)
            .GroupBy(m => m.ExecutorId).Select(g => g.First()).ToDictionary(m => m.ExecutorId);
        var units = team.Units;
        ValidateUnits(units);
        if (!unitsRule)
        {
            // исполнитель ушёл из состава: безымянная должность исчезает, остальные пустеют
            var parents = units.Select(u => u.ParentUnitId).OfType<string>().ToHashSet();
            units.RemoveAll(u => u.ExecutorId is { } e && !members.ContainsKey(e)
                && u.Kind == TeamUnit.Position && u.Name.Length == 0 && !parents.Contains(u.Id));
            foreach (var unit in units.Where(u => u.ExecutorId is { } e && !members.ContainsKey(e)))
            {
                unit.ExecutorId = null;
            }
        }
        var byExecutor = units.Where(u => u.ExecutorId is not null).ToDictionary(u => u.ExecutorId!);
        var created = new HashSet<string>();
        foreach (var member in members.Values.Where(m => !byExecutor.ContainsKey(m.ExecutorId)))
        {
            created.Add(member.ExecutorId);
            var unit = new TeamUnit
            {
                Id = $"{team.Id}:{member.ExecutorId}",
                ExecutorId = member.ExecutorId,
                Sort = units.Count,
            };
            units.Add(unit);
            byExecutor[member.ExecutorId] = unit;
        }
        // подчинение по составу: единица участника переезжает под единицу руководителя. У
        // редактора единиц так ставится только участник, добавленный мимо единиц (форма
        // участника на вкладке проекта)
        foreach (var member in members.Values.Where(m => !unitsRule || created.Contains(m.ExecutorId)))
        {
            var unit = byExecutor[member.ExecutorId];
            if (HeadOf(units, unit) == member.ParentExecutorId)
            {
                continue;
            }
            unit.ParentUnitId = member.ParentExecutorId is { } p && byExecutor.TryGetValue(p, out var pu)
                && !IsInside(units, pu, unit) ? pu.Id : null;
        }
        foreach (var member in members.Values)
        {
            member.ParentExecutorId = HeadOf(units, byExecutor[member.ExecutorId]);
        }
    }

    /// <summary>Руководитель единицы — исполнитель ближайшей вышестоящей единицы с исполнителем.</summary>
    private static string? HeadOf(List<TeamUnit> units, TeamUnit unit)
    {
        var seen = new HashSet<string> { unit.Id };
        for (var up = Find(units, unit.ParentUnitId); up is not null && seen.Add(up.Id); up = Find(units, up.ParentUnitId))
        {
            if (up.ExecutorId is not null)
            {
                return up.ExecutorId;
            }
        }
        return null;
    }

    /// <summary>Лежит ли <paramref name="unit"/> внутри <paramref name="ancestor"/> (или совпадает).</summary>
    private static bool IsInside(List<TeamUnit> units, TeamUnit unit, TeamUnit ancestor)
    {
        var seen = new HashSet<string>();
        for (var up = unit; up is not null && seen.Add(up.Id); up = Find(units, up.ParentUnitId))
        {
            if (up.Id == ancestor.Id)
            {
                return true;
            }
        }
        return false;
    }

    private static TeamUnit? Find(List<TeamUnit> units, string? id) =>
        id is null ? null : units.FirstOrDefault(u => u.Id == id);

    /// <summary>Проверка единиц: вид, ссылки на родителя, циклы, один исполнитель — одна единица.</summary>
    private static void ValidateUnits(List<TeamUnit> units)
    {
        foreach (var unit in units)
        {
            unit.Name = unit.Name.Trim();
            if (unit.ExecutorId is { Length: 0 })
            {
                unit.ExecutorId = null;
            }
            if (unit.Kind is not (TeamUnit.Department or TeamUnit.Position))
            {
                throw new ArgumentException(Loc.T("msg.team.10"));
            }
        }
        if (units.GroupBy(u => u.Id).Any(g => g.Count() > 1)
            || units.Where(u => u.ExecutorId is not null).GroupBy(u => u.ExecutorId).Any(g => g.Count() > 1))
        {
            throw new ArgumentException(Loc.T("msg.team.5"));
        }
        foreach (var unit in units)
        {
            if (unit.ParentUnitId is { } p && Find(units, p) is null)
            {
                unit.ParentUnitId = null;   // вышестоящую удалили — единица поднимается наверх
            }
        }
        foreach (var unit in units)
        {
            var seen = new HashSet<string> { unit.Id };
            for (var up = Find(units, unit.ParentUnitId); up is not null; up = Find(units, up.ParentUnitId))
            {
                if (!seen.Add(up.Id))
                {
                    throw new ArgumentException(Loc.T("msg.team.8"));
                }
            }
        }
    }

    private static List<TeamUnit> LoadUnits(SqliteConnection conn, SqliteTransaction? tx, string teamId)
    {
        var units = Sql.Query(conn, tx, """
            SELECT id, parent_unit_id, kind, name, executor_id, sort, is_active
            FROM team_units WHERE team_id=@id ORDER BY sort, name
            """,
            r => new TeamUnit
            {
                Id = r.S("id"),
                ParentUnitId = r.SN("parent_unit_id"),
                Kind = r.S("kind"),
                Name = r.S("name"),
                ExecutorId = r.SN("executor_id"),
                Sort = (int)r.L("sort"),
                IsActive = r.B("is_active"),
            },
            ("@id", teamId));
        if (units.Count == 0)
        {
            return units;
        }
        var skills = Sql.Query(conn, tx, """
            SELECT s.unit_id, s.skill_id FROM team_unit_skills s
            JOIN team_units u ON u.id = s.unit_id WHERE u.team_id=@id
            """, r => (Unit: r.S("unit_id"), Skill: r.S("skill_id")), ("@id", teamId));
        foreach (var unit in units)
        {
            unit.SkillIds = skills.Where(s => s.Unit == unit.Id).Select(s => s.Skill).ToList();
        }
        return units;
    }

    /// <summary>Единицы команды из базы, согласованные с составом (старая команда без единиц
    /// получает их в памяти — должность без названия на каждого участника).</summary>
    private static void FillUnits(SqliteConnection conn, Team team)
    {
        team.Units = LoadUnits(conn, null, team.Id);
        try
        {
            ReconcileUnits(team, unitsRule: false);
        }
        catch (ArgumentException)
        {
            // испорченные строки (например, цикл от двух серверов сразу) не роняют чтение команды
            team.Units = [];
            ReconcileUnits(team, unitsRule: false);
        }
    }

    /// <summary>Сохранение единиц ПО СТРОКАМ (а не «удалить всё и вставить», наука T-3-S1):
    /// меняются только действительно изменившиеся строки.</summary>
    private static void SaveUnits(SqliteConnection conn, SqliteTransaction tx, Team team)
    {
        var stored = LoadUnits(conn, tx, team.Id).ToDictionary(u => u.Id);
        var keep = team.Units.Select(u => u.Id).ToHashSet();
        foreach (var gone in stored.Values.Where(u => !keep.Contains(u.Id)))
        {
            Sql.Exec(conn, tx, "DELETE FROM team_unit_skills WHERE unit_id=@id", ("@id", gone.Id));
            Sql.Exec(conn, tx, "DELETE FROM team_units WHERE id=@id", ("@id", gone.Id));
        }
        foreach (var unit in team.Units)
        {
            stored.TryGetValue(unit.Id, out var old);
            if (old is null || old.ParentUnitId != unit.ParentUnitId || old.Kind != unit.Kind
                || old.Name != unit.Name || old.ExecutorId != unit.ExecutorId
                || old.Sort != unit.Sort || old.IsActive != unit.IsActive)
            {
                Sql.Exec(conn, tx, """
                    INSERT INTO team_units (id, team_id, parent_unit_id, kind, name, executor_id, sort, is_active)
                    VALUES (@id, @team, @parent, @kind, @name, @executor, @sort, @active)
                    ON CONFLICT(id) DO UPDATE SET parent_unit_id=excluded.parent_unit_id,
                        kind=excluded.kind, name=excluded.name, executor_id=excluded.executor_id,
                        sort=excluded.sort, is_active=excluded.is_active
                    """,
                    ("@id", unit.Id), ("@team", team.Id), ("@parent", unit.ParentUnitId),
                    ("@kind", unit.Kind), ("@name", unit.Name), ("@executor", unit.ExecutorId),
                    ("@sort", unit.Sort), ("@active", unit.IsActive ? 1 : 0));
            }
            var oldSkills = old?.SkillIds ?? [];
            var newSkills = unit.SkillIds.Distinct().ToList();
            foreach (var skill in oldSkills.Except(newSkills))
            {
                Sql.Exec(conn, tx, "DELETE FROM team_unit_skills WHERE unit_id=@u AND skill_id=@s",
                    ("@u", unit.Id), ("@s", skill));
            }
            foreach (var skill in newSkills.Except(oldSkills))
            {
                Sql.Exec(conn, tx, "INSERT OR IGNORE INTO team_unit_skills (unit_id, skill_id) VALUES (@u, @s)",
                    ("@u", unit.Id), ("@s", skill));
            }
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
