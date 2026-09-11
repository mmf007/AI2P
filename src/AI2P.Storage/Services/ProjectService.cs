using AI2P.Core;
using System.Text;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Проекты (ТЗ п. 2.7): CRUD через event log + проекцию в одной транзакции.
///
/// В кластере (ТЗ гл. 6, этап 42) проект реплицируется целиком, но привязан к каталогу на
/// диске, а каталоги на разных серверах разные. Поэтому у проекта появилась ПЕР-СЕРВЕРНАЯ
/// часть (<c>project_servers</c>): каталог, папка <c>Common</c> и активность. Менять её может
/// только пользователь этого сервера, и наружу проект отдаётся уже с подставленными
/// значениями своего сервера — остальному коду (коннекторы, файловые инструменты, UI)
/// про эту таблицу знать не нужно.
/// </summary>
public sealed class ProjectService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;
    private readonly ServerScope _scope;

    public ProjectService(Database db, EventStore events, FileStore files, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _files = files;
        _scope = scope ?? ServerScope.Standalone();
    }

    public List<Project> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM projects" + (includeDeleted ? "" : " WHERE deleted_at IS NULL") + " ORDER BY name";
        var projects = Sql.Query(conn, null, sql, Map);
        var local = LocalParts(conn);
        foreach (var project in projects)
        {
            ApplyLocal(project, local.GetValueOrDefault(project.Id));
        }
        return projects;
    }

    public Project? Get(string id)
    {
        using var conn = _db.Open();
        var project = Sql.Query(conn, null, "SELECT * FROM projects WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (project is not null)
        {
            ApplyLocal(project, LocalPart(conn, null, id));
        }
        return project;
    }

    // --- пер-серверная часть проекта (ТЗ гл. 6, этап 42) ---

    /// <summary>Пер-серверные части всех проектов для ЭТОГО сервера (одним запросом на список).</summary>
    private Dictionary<string, ProjectServer> LocalParts(SqliteConnection conn) =>
        Sql.Query(conn, null, """
            SELECT * FROM project_servers WHERE server_id=@s AND deleted_at IS NULL
            """, MapPart, ("@s", _scope.ServerId))
            .ToDictionary(p => p.ProjectId, StringComparer.Ordinal);

    private ProjectServer? LocalPart(SqliteConnection conn, SqliteTransaction? tx, string projectId) =>
        Sql.Query(conn, tx, """
            SELECT * FROM project_servers
            WHERE project_id=@p AND server_id=@s AND deleted_at IS NULL
            """, MapPart, ("@p", projectId), ("@s", _scope.ServerId)).FirstOrDefault();

    /// <summary>Подставить в проект значения ЭТОГО сервера. Строки ещё нет (проект приехал
    /// репликой либо создан до этапа 42) — каталога тут нет, а активность берётся общая.</summary>
    private static void ApplyLocal(Project project, ProjectServer? part)
    {
        project.FolderPath = part?.FolderPath ?? "";
        project.CommonPath = part?.CommonPath ?? "";
        if (part is not null)
        {
            project.IsActive = part.IsActive;
        }
    }

    /// <summary>
    /// Записать пер-серверную часть проекта (форма проекта на ЭТОМ сервере, ТЗ гл. 6):
    /// каталог, папка <c>Common</c> и активность. Правило «активным можно сделать только
    /// проект с указанным здесь каталогом» защищает от запуска задач в никуда: у проекта,
    /// созданного с каталогом на другом сервере, здесь каталога нет (разд. 4.1 плана).
    /// </summary>
    public Project SaveLocalPart(string projectId, string folderPath, string commonPath,
        bool isActive, string? actorId)
    {
        // каталог проекта на ЭТОМ компьютере: «~/work/проект» раскрывается в полный путь
        // и хранится раскрытым (T-135) — им пользуются инструменты агента, песочница CLI
        // и репликация файлов, и все они ждут настоящий путь
        folderPath = AI2P.Core.PathHome.Expand(folderPath);
        commonPath = EnsureCommonPath(commonPath);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var exists = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM projects WHERE id=@id AND deleted_at IS NULL", ("@id", projectId)) > 0;
        if (!exists)
        {
            throw new InvalidOperationException(Loc.T("msg.experience.7", projectId));
        }
        // проект с каталогом хотя бы на одном сервере нельзя включать там, где каталог не задан
        if (isActive && folderPath.Length == 0 && HasFolderElsewhere(conn, tx, projectId))
        {
            throw new ArgumentException(
                Loc.T("msg.project.1"));
        }
        var part = LocalPart(conn, tx, projectId);
        if (part is null)
        {
            part = new ProjectServer
            {
                ProjectId = projectId,
                ServerId = _scope.ServerId,
                CreatedAt = now,
            };
            Sql.Exec(conn, tx, """
                INSERT INTO project_servers (id, project_id, server_id, folder_path, common_rel_path,
                                             is_active, created_at, updated_at)
                VALUES (@id, @p, @s, @f, @c, @a, @now, @now)
                """,
                ("@id", part.Id), ("@p", projectId), ("@s", _scope.ServerId), ("@f", folderPath),
                ("@c", commonPath), ("@a", isActive ? 1 : 0), ("@now", Sql.ToDb(now)));
        }
        else
        {
            Sql.Exec(conn, tx, """
                UPDATE project_servers SET folder_path=@f, common_rel_path=@c, is_active=@a, updated_at=@now
                WHERE id=@id
                """,
                ("@f", folderPath), ("@c", commonPath), ("@a", isActive ? 1 : 0),
                ("@now", Sql.ToDb(now)), ("@id", part.Id));
        }
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = projectId,
            EventType = EventTypes.ProjectServerUpdated,
            EntityType = "project_server",
            EntityId = part.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                server = _scope.Code,
                folderPath,
                commonPath,
                isActive,
            }),
        });
        tx.Commit();
        var project = Sql.Query(conn, null, "SELECT * FROM projects WHERE id=@id", Map, ("@id", projectId))
            .First();
        ApplyLocal(project, LocalPart(conn, null, projectId));
        return project;
    }

    /// <summary>
    /// Папка <c>Common</c> — путь ВНУТРИ каталога проекта (ТЗ гл. 6, разд. 7 плана), поэтому
    /// он обязан быть относительным. Проверка добавлена в T-135 вместе с раскрытием «~»:
    /// полный путь (в том числе «~/…») и выход наверх «..» репликация файлов молча отвергает
    /// (<c>FileManifest.Resolve</c> вернёт null), и человек видел бы «папка не настроена»
    /// вместо причины.
    /// </summary>
    private static string EnsureCommonPath(string commonPath)
    {
        var path = commonPath.Trim();
        if (path.Length == 0)
        {
            return "";
        }
        if (AI2P.Core.PathHome.StartsWithHome(path) || Path.IsPathRooted(path)
            || path.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                Loc.T("msg.project.2", path));
        }
        return path;
    }

    /// <summary>Каталог проекта задан на каком-то ДРУГОМ сервере (правило активности, разд. 4.1).</summary>
    private bool HasFolderElsewhere(SqliteConnection conn, SqliteTransaction? tx, string projectId) =>
        Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM project_servers
            WHERE project_id=@p AND server_id<>@s AND folder_path<>'' AND deleted_at IS NULL
            """, ("@p", projectId), ("@s", _scope.ServerId)) > 0;

    /// <summary>
    /// Разовая миграция при старте (ТЗ гл. 6, этап 42): каталог проекта переехал из настроек
    /// проекта (`settings_json.folderPath`) в пер-серверную строку — каталоги на разных
    /// серверах разные. Здесь для КАЖДОГО проекта, у которого пер-серверной строки ещё нет,
    /// она создаётся со старым каталогом и общей активностью. Без этого пользователь после
    /// обновления обнаружил бы пустые каталоги у всех своих проектов.
    ///
    /// Идемпотентно: у проекта, чья строка уже есть, ничего не делается. Значение в настройках
    /// остаётся нетронутым — оно просто больше не читается.
    /// </summary>
    public void MigrateFolderPathsToServerParts()
    {
        using var conn = _db.Open();
        var now = DateTime.UtcNow;
        foreach (var project in Sql.Query(conn, null, "SELECT * FROM projects", Map))
        {
            if (LocalPart(conn, null, project.Id) is not null)
            {
                continue;
            }
            Sql.Exec(conn, null, """
                INSERT INTO project_servers (id, project_id, server_id, folder_path, common_rel_path,
                                             is_active, created_at, updated_at)
                VALUES (@id, @p, @s, @f, '', @a, @now, @now)
                """,
                ("@id", Guid.NewGuid().ToString()), ("@p", project.Id), ("@s", _scope.ServerId),
                ("@f", FolderFromSettings(project.SettingsJson)),
                ("@a", project.IsActive ? 1 : 0), ("@now", Sql.ToDb(now)));
        }
    }

    /// <summary>Каталог проекта из старых настроек (до этапа 42); пусто — не задан.</summary>
    private static string FolderFromSettings(string settingsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("folderPath", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Все пер-серверные части проекта (форма проекта показывает каталоги остальных
    /// серверов только для чтения — так видно, где проект уже развёрнут).</summary>
    public List<ProjectServer> ServerParts(string projectId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT * FROM project_servers WHERE project_id=@p AND deleted_at IS NULL ORDER BY created_at
            """, MapPart, ("@p", projectId));
    }

    public Project Create(string name, string? folderPath, string? defaultTeamId, string? actorId,
        bool isActive = true, double qualityBias = 0.5, string? objRefFormat = null,
        string? defaultResponsibleId = null,
        double timeQuality = ProjectSettings.TimeQualityDefault,
        int experienceLimitChars = ProjectSettings.ExperienceLimitDefault,
        int recheckLimit = ProjectSettings.RecheckLimitDefault,
        double defaultTaskHours = ProjectSettings.DefaultTaskHoursDefault)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Name = name.Trim(),
            // путь до папки проекта сюда больше не входит: он пер-серверный (ТЗ гл. 6, этап 42)
            SettingsJson = JsonSerializer.Serialize(new
            {
                defaultTeamId,
                qualityBias = Math.Clamp(qualityBias, 0.0, 1.0),
                // форма ссылки на объект проекта (T-267): незнакомое значение — умолчание
                objRefFormat = ObjectRefFormats.Normalize(objRefFormat),
                // ответственный по умолчанию (T-5-S0): пусто — новые задачи как раньше
                defaultResponsibleId = string.IsNullOrWhiteSpace(defaultResponsibleId)
                    ? null
                    : defaultResponsibleId,
                // «время ↔ качество» по умолчанию (T-23-S0): подставляется в новую задачу
                // проекта и берётся у задачи, где поле не заполнено
                timeQuality = Math.Clamp(timeQuality, 0.0, 1.0),
                // предел подстановки опыта в промпт задания (T-29-S0): суммарно по трём
                // блокам опыта; не больше нуля — умолчание
                experienceLimitChars = experienceLimitChars > 0
                    ? experienceLimitChars
                    : ProjectSettings.ExperienceLimitDefault,
                // предел кругов повторной проверки (T-31-S0): столько раз задача-прогонщик
                // может вернуть соседей в доработку и уйти их ждать
                recheckLimit = recheckLimit > 0 ? recheckLimit : ProjectSettings.RecheckLimitDefault,
                // длительность задачи по умолчанию (T-132-S0): ширина квадрата на диаграмме
                // подзадач у задачи без плановой длительности
                defaultTaskHours = defaultTaskHours > 0
                    ? defaultTaskHours
                    : ProjectSettings.DefaultTaskHoursDefault,
            }),
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (project.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.project.3"));
        }

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, project.Name, project.Id);

        // неактивная команда не подставляется в новые проекты (ТЗ п. 2.8)
        if (defaultTeamId is not null)
        {
            var teamActive = Sql.Scalar<long?>(conn, tx,
                "SELECT is_active FROM teams WHERE id=@id AND deleted_at IS NULL", ("@id", defaultTeamId));
            if (teamActive is null or 0)
            {
                throw new ArgumentException(Loc.T("msg.project.4"));
            }
        }

        // номер проекта с кодом создавшего сервера (ТЗ гл. 6 «Номера задач»): проекты
        // создаются на любом сервере, и без суффикса два сервера без связи завели бы два PRJ-3
        project.DisplayId = Database.NextDisplayId(conn, tx, "PRJ", _scope.IsConductor ? "" : _scope.Code);
        // каталог хранилища именуется ВНЕШНИМ КОДОМ проекта — как у задачи (todo37):
        // код не меняется никогда, поэтому переименование проекта каталог больше не трогает
        project.Slug = project.DisplayId;

        Sql.Exec(conn, tx, """
            INSERT INTO projects (id, display_id, name, slug, settings_json, is_active, created_at, updated_at)
            VALUES (@id, @did, @name, @slug, @settings, @active, @created, @updated)
            """,
            ("@id", project.Id), ("@did", project.DisplayId), ("@name", project.Name),
            ("@slug", project.Slug), ("@settings", project.SettingsJson),
            ("@active", project.IsActive ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = project.Id,
            EventType = EventTypes.ProjectCreated,
            EntityType = "project",
            EntityId = project.Id,
            PayloadJson = JsonSerializer.Serialize(new { project.DisplayId, project.Name, project.Slug }),
        });

        // пер-серверная часть создаётся сразу: каталог указан на ЭТОМ сервере (ТЗ гл. 6)
        Sql.Exec(conn, tx, """
            INSERT INTO project_servers (id, project_id, server_id, folder_path, common_rel_path,
                                         is_active, created_at, updated_at)
            VALUES (@id, @p, @s, @f, '', @a, @now, @now)
            """,
            ("@id", Guid.NewGuid().ToString()), ("@p", project.Id), ("@s", _scope.ServerId),
            ("@f", AI2P.Core.PathHome.Expand(folderPath)), ("@a", project.IsActive ? 1 : 0),
            ("@now", Sql.ToDb(now)));

        tx.Commit();
        _files.EnsureProjectDirs(project.Slug);
        // «~/work/проект» из формы хранится раскрытым полным путём (T-135)
        project.FolderPath = AI2P.Core.PathHome.Expand(folderPath);
        return project;
    }

    public Project Update(Project project, string? actorId)
    {
        var now = DateTime.UtcNow;
        project.Name = project.Name.Trim();
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, project.Name, project.Id);

        // каталог хранилища назван внешним кодом проекта (todo37) — переименование проекта
        // его не трогает: слаг остаётся прежним, файлы и пути в БД не двигаются
        var old = Sql.Query(conn, tx, "SELECT * FROM projects WHERE id=@id", Map, ("@id", project.Id))
                      .FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.experience.7", project.Id));
        project.Slug = old.Slug;

        // РЕПЛИЦИРУЕМАЯ часть строки проекта: имя и настройки (команда, цена↔качество).
        // Признак is_active здесь остаётся общим значением по умолчанию для серверов, где
        // пер-серверной строки ещё нет; действующая активность — пер-серверная (ТЗ гл. 6)
        Sql.Exec(conn, tx,
            "UPDATE projects SET name=@name, slug=@slug, settings_json=@settings, is_active=@active, updated_at=@updated WHERE id=@id",
            ("@name", project.Name), ("@slug", project.Slug), ("@settings", project.SettingsJson),
            ("@active", project.IsActive ? 1 : 0),
            ("@updated", Sql.ToDb(now)), ("@id", project.Id));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = project.Id,
            EventType = EventTypes.ProjectUpdated,
            EntityType = "project",
            EntityId = project.Id,
            PayloadJson = JsonSerializer.Serialize(new { project.Name }),
        });

        tx.Commit();
        project.UpdatedAt = now;
        // каталог, папка Common и активность — пер-серверные: они пишутся отдельной строкой
        // и только пользователем ЭТОГО сервера (ТЗ гл. 6, этап 42)
        return SaveLocalPart(project.Id, project.FolderPath, project.CommonPath, project.IsActive, actorId);
    }

    /// <summary>
    /// МЯГКОЕ УДАЛЕНИЕ ПРОЕКТА (T-44-S0, выпуск 1.105) — «удалить совсем» на форме выбора
    /// «перенести в архив или удалить». До этой правки удалить проект было нечем вовсе,
    /// а форма обязана предлагать оба исхода: иначе «перенести в архив» становится не
    /// выбором, а единственным способом убрать проект с экрана.
    ///
    /// Проект уходит ВМЕСТЕ СО СВОИМ СОДЕРЖИМЫМ — задачи и шаблоны, объекты, записи опыта:
    /// это ровно тот же состав, что уносит в архив <c>ArchiveTransferService</c> с видом
    /// переноса «проект целиком». Оставить их без проекта значило бы получить строки,
    /// которых ни в одном списке не видно, а связи из них ведут в никуда.
    ///
    /// Удаление мягкое (<c>deleted_at</c>), как у задачи: файлы на диске не трогаются,
    /// журнал событий остаётся — по нему видно, что и когда убрали.
    /// </summary>
    public void Delete(string id, string? actorId)
    {
        var now = DateTime.UtcNow;
        var stamp = Sql.ToDb(now);
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var project = Sql.Query(conn, tx, "SELECT * FROM projects WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (project is null || project.DeletedAt is not null)
        {
            return;
        }

        Sql.Exec(conn, tx, "UPDATE projects SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", stamp), ("@id", id));
        foreach (var table in new[] { "tasks", "objects", "experience" })
        {
            Sql.Exec(conn, tx,
                $"UPDATE {table} SET deleted_at=@d, updated_at=@d "
                + "WHERE project_id=@id AND deleted_at IS NULL",
                ("@d", stamp), ("@id", id));
        }

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = id,
            EventType = EventTypes.ProjectDeleted,
            EntityType = "project",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { project.Name, project.DisplayId }),
        });

        tx.Commit();
    }

    /// <summary>
    /// Перевод каталогов хранилища на внешний код проекта (todo37, разовая миграция при старте):
    /// projects/&lt;слаг из имени&gt; → projects/&lt;display_id&gt;. Каталог переносится, относительные
    /// пути задач и заданий в БД правятся, в журнал пишется `project.renamed`.
    /// Идемпотентно: у проекта, чей слаг уже равен коду, ничего не делается.
    /// </summary>
    public void MigrateSlugsToDisplayId(string? actorId = null)
    {
        var now = DateTime.UtcNow;
        foreach (var project in List(includeDeleted: true))
        {
            if (project.DisplayId.Length == 0
                || project.Slug.Equals(project.DisplayId, StringComparison.Ordinal))
            {
                continue;
            }
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            RenameStoragePaths(conn, tx, now, project.Slug, project.DisplayId);
            Sql.Exec(conn, tx, "UPDATE projects SET slug=@slug, updated_at=@updated WHERE id=@id",
                ("@slug", project.DisplayId), ("@updated", Sql.ToDb(now)), ("@id", project.Id));
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                ProjectId = project.Id,
                EventType = EventTypes.ProjectRenamed,
                EntityType = "project",
                EntityId = project.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    reason = "storage dir by display id (todo37)",
                    oldSlug = project.Slug,
                    newSlug = project.DisplayId,
                }),
            });
            // каталог переносится до фиксации БД: не удалось перенести — транзакция откатится
            _files.RenameProjectDir(project.Slug, project.DisplayId);
            try
            {
                tx.Commit();
            }
            catch
            {
                _files.RenameProjectDir(project.DisplayId, project.Slug); // откат переноса
                throw;
            }
        }
    }

    /// <summary>Обновить относительные пути файлов проекта в БД при смене слага (ТЗ v1.17):
    /// tasks.description_path / acceptance_path, jobs.request_path / result_path.</summary>
    private static void RenameStoragePaths(SqliteConnection conn, SqliteTransaction tx, DateTime now,
        string oldSlug, string newSlug)
    {
        var oldPrefix = $"projects/{oldSlug}/";
        var newPrefix = $"projects/{newSlug}/";
        Sql.Exec(conn, tx, """
            UPDATE tasks SET
              description_path = CASE WHEN description_path LIKE @old || '%'
                                      THEN @new || substr(description_path, length(@old) + 1)
                                      ELSE description_path END,
              acceptance_path  = CASE WHEN acceptance_path LIKE @old || '%'
                                      THEN @new || substr(acceptance_path, length(@old) + 1)
                                      ELSE acceptance_path END,
              updated_at = @updated
            WHERE description_path LIKE @old || '%' OR acceptance_path LIKE @old || '%'
            """,
            ("@old", oldPrefix), ("@new", newPrefix), ("@updated", Sql.ToDb(now)));
        Sql.Exec(conn, tx, """
            UPDATE jobs SET
              request_path = CASE WHEN request_path LIKE @old || '%'
                                  THEN @new || substr(request_path, length(@old) + 1)
                                  ELSE request_path END,
              result_path  = CASE WHEN result_path LIKE @old || '%'
                                  THEN @new || substr(result_path, length(@old) + 1)
                                  ELSE result_path END,
              updated_at = @updated
            WHERE request_path LIKE @old || '%' OR result_path LIKE @old || '%'
            """,
            ("@old", oldPrefix), ("@new", newPrefix), ("@updated", Sql.ToDb(now)));
    }

    /// <summary>Цена ↔ качество автоподбора исполнителя из настроек проекта (ТЗ v1.26):
    /// 0.0 — приоритет цене, 1.0 — качеству; по умолчанию 0.5.</summary>
    public static double QualityBias(Project? project)
    {
        if (project is null)
        {
            return 0.5;
        }
        try
        {
            using var doc = JsonDocument.Parse(project.SettingsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("qualityBias", out var v)
                   && v.TryGetDouble(out var bias)
                ? Math.Clamp(bias, 0.0, 1.0)
                : 0.5;
        }
        catch (JsonException)
        {
            return 0.5;
        }
    }

    /// <summary>Имя проекта уникально (ТЗ гл. 11, форма ввода проекта; правка v1.13).</summary>
    private static void EnsureUniqueName(SqliteConnection conn, SqliteTransaction tx, string name, string selfId)
    {
        var taken = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM projects WHERE name=@name AND id<>@id AND deleted_at IS NULL",
            ("@name", name), ("@id", selfId)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.project.5", name));
        }
    }

    // Транслитерация имени в слаг каталога убрана в todo37: каталог хранилища проекта
    // называется его внешним кодом (projects/PRJ-3), как каталог задачи — её кодом.

    private static ProjectServer MapPart(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        ProjectId = r.S("project_id"),
        ServerId = r.S("server_id"),
        FolderPath = r.S("folder_path"),
        CommonPath = r.S("common_rel_path"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };

    private static Project Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Slug = r.S("slug"),
        SettingsJson = r.S("settings_json"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
