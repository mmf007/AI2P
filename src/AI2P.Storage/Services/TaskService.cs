using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Задачи, процессы и шаблоны (ТЗ пп. 2.1, 2.3): проекция tasks + task_executors + task_skills,
/// описание — файл .md (в БД — путь), история — события.
/// </summary>
public sealed class TaskService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;
    private readonly ServerScope _scope;

    public TaskService(Database db, EventStore events, FileStore files, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _files = files;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>Кто мы в кластере (ТЗ гл. 6, этап 42): от этого зависит, какие задачи здесь
    /// правятся, а какие видны только для чтения.</summary>
    public ServerScope Scope => _scope;

    /// <summary>
    /// Сервер-владелец НОВОЙ строки (ТЗ гл. 6, этап 42; T-1-S0). Это ВСЕГДА сервер, на котором
    /// задачу создают, — в том числе дирижёр.
    ///
    /// Раньше на дирижёре владелец не проставлялся вовсе: он и так единственный писатель
    /// бесхозных строк. Но «бесхозная» значит «принадлежит тому, кто сегодня дирижёр», и при
    /// СМЕНЕ ДИРИЖЁРА все такие задачи разом уезжали к новому — на прежнем сервере они
    /// становились только для чтения, их задания переставали запускаться, а починить это можно
    /// было лишь вернув дирижёрство назад. Владелец, записанный явно, смены дирижёра не
    /// замечает: задача остаётся у своего сервера.
    ///
    /// Номер при этом не меняется (см. <see cref="CodeForNew"/>): у задач дирижёра суффикса
    /// по-прежнему нет, и одиночная установка выглядит ровно как раньше — T-18, а не T-18-S0.
    ///
    /// Пусто возвращается в одном случае: записи о локальном сервере ещё нет (её заводит старт
    /// приложения). Выдумывать владельца тут нечего, и правило «бесхозную правит дирижёр»
    /// оставляет такую задачу рабочей.
    /// </summary>
    private string? DefaultOwner() => _scope.ServerId is { Length: > 0 } id ? id : null;

    /// <summary>Код сервера для суффикса номера (ТЗ гл. 6 «Номера задач»): у задач ДИРИЖЁРА
    /// суффикса нет — он один, и его номера исторически идут без кода (T-18, а не T-18-S0).
    /// Сервер-владелец теперь проставляется и у них (T-1-S0), но нумерация от этого не
    /// поехала: суффикс считается по роли сервера, а не по тому, пусто ли поле.</summary>
    private string CodeForNew(string? ownerServerId) =>
        _scope.IsConductor && (_scope.IsMine(ownerServerId) || ownerServerId is not { Length: > 0 })
            ? ""
            : _scope.CodeOf(ownerServerId);

    /// <summary>Подставить сервер-владельца в выдачу наружу (код, имя, «только чтение»).
    /// В таблице этих полей нет — они нужны спискам и карточке задачи (ТЗ гл. 6).</summary>
    private TaskItem Decorate(TaskItem task)
    {
        task.ServerCode = _scope.CodeOf(task.ServerId);
        task.ServerName = _scope.NameOf(task.ServerId);
        task.IsReadOnly = !_scope.CanWrite(task.ServerId);
        return task;
    }

    private List<TaskItem> Decorate(List<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            Decorate(task);
        }
        return tasks;
    }

    /// <summary>
    /// Задача правится на этом сервере (ТЗ гл. 6): свой сервер либо дирижёр у бесхозной.
    ///
    /// С T-36-S0 правило ОДНО и для шаблонов: у шаблона тоже есть сервер-владелец, и его
    /// можно передать другому серверу той же кнопкой, что и задачу. Прежде шаблоны жили
    /// «на дирижёре» — то есть принадлежали не компьютеру, а РОЛИ, и смена дирижёра уводила
    /// их с того компьютера, где с ними работали (та же мина, что чинил T-1-S0 у задач).
    /// У шаблона прежних версий сервера ещё нет — такой по общему правилу правит дирижёр,
    /// пока сторож <see cref="ClaimTasksWithoutServer"/> не запишет владельца явно.
    /// </summary>
    public bool CanWrite(TaskItem task) => _scope.CanWrite(task.ServerId);

    /// <summary>Проверка владения перед записью; чужая задача — понятная ошибка (ТЗ гл. 6).</summary>
    public void EnsureCanWrite(TaskItem task)
    {
        if (CanWrite(task))
        {
            return;
        }
        // «Шаблон T-1» / «Задача T-1» — чтобы сообщение называло вещи своими именами
        _scope.EnsureCanWrite(task.ServerId,
            Loc.T(task.IsTemplate ? "msg.task.35" : "msg.task.2", task.DisplayId));
    }

    /// <summary>Смена статуса задачи (после фиксации транзакции): задача, старый статус, актор.
    /// На событии строится автозапуск потомков (todo22, подготовка к этапу 3) — подписчик
    /// живёт в слое коннекторов, Storage о нём не знает.</summary>
    public event Action<TaskItem, string, string?>? StatusChanged;

    /// <summary>Задача ЗАВЕДЕНА (после фиксации транзакции): сама задача и актор. Нужно
    /// очереди иерархического запуска (T-16-S0): подзадачу, созданную агентом ПРЯМО В ХОДЕ
    /// работы (маркер AI2P_SUBTASK, действие create_task), очередь обязана увидеть, не
    /// дожидаясь конца задания родителя — иначе она ждёт сторожа или человека. Подписчик,
    /// как и у StatusChanged, живёт в слое коннекторов, Storage о нём не знает.</summary>
    public event Action<TaskItem, string?>? Created;

    /// <summary>Рассказать подписчикам о задаче, заведённой с <c>notify: false</c> (T-16-S0).
    /// Нужно там, где задача после Create ещё дописывается: копия шаблона достраивает дерево
    /// и блокирующие связи, а подзадача агента получает отложенный старт (T-138). Пока это не
    /// сделано, очередь иерархии видела бы её недостроенной и могла запустить не то и не
    /// тогда.</summary>
    public void NotifyCreated(TaskItem task, string? actorId) => Created?.Invoke(task, actorId);

    /// <summary>
    /// Задачи для списков (доска/таблица/иерархия) с фильтром (ТЗ гл. 5, экран 1) и
    /// полнотекстным поиском по заголовку и описанию. Шаблоны исключаются
    /// (templatesOnly=true — наоборот, только шаблоны: списки шаблонов).
    /// </summary>
    /// <param name="serverIds">Фильтр по серверам-владельцам (ТЗ гл. 6, этап 42): пусто —
    /// все серверы; значение <c>none</c> — задачи без сервера (их ведёт дирижёр).</param>
    /// <param name="tags">Фильтр по тэгам (T-222): пусто — все задачи; несколько тэгов
    /// соединяются по ИЛИ — человек отбирает «покажи всё про UI или про сборку», а не
    /// пересечение (пересечение почти всегда пусто и выглядит как поломка фильтра).</param>
    public List<TaskItem> List(string? projectId = null, bool includeTemplates = false,
        bool templatesOnly = false, string? teamId = null, string? executorId = null,
        string? mineId = null, string? status = null,
        DateTime? dueFrom = null, DateTime? dueTo = null, string? search = null,
        int? priorityMin = null, int? priorityMax = null, IReadOnlyList<string>? serverIds = null,
        IReadOnlyList<string>? tags = null)
    {
        using var conn = _db.Open();
        var where = new List<string> { "t.deleted_at IS NULL" };
        var args = new List<(string, object?)>();
        // фильтр по серверам (ТЗ гл. 6): в кластере задачи всех серверов лежат в одной реплике,
        // и без фильтра списки смешивали бы свои задачи с чужими
        if (serverIds is { Count: > 0 })
        {
            var clauses = new List<string>();
            var index = 0;
            foreach (var serverId in serverIds)
            {
                if (serverId == NoServer)
                {
                    clauses.Add("t.server_id IS NULL");
                    continue;
                }
                var name = $"@srv{index++}";
                clauses.Add($"t.server_id={name}");
                args.Add((name, serverId));
            }
            where.Add("(" + string.Join(" OR ", clauses) + ")");
        }
        if (projectId is not null)
        {
            where.Add("t.project_id=@project");
            args.Add(("@project", projectId));
        }
        if (teamId is not null)
        {
            where.Add("t.team_id=@team");
            args.Add(("@team", teamId));
        }
        if (executorId is not null)
        {
            where.Add("EXISTS (SELECT 1 FROM task_executors te WHERE te.task_id=t.id AND te.executor_id=@executor)");
            args.Add(("@executor", executorId));
        }
        if (mineId is not null)
        {
            // «мои задачи»: текущий пользователь — исполнитель или ответственный
            where.Add("""
                (t.responsible_id=@mine
                 OR EXISTS (SELECT 1 FROM task_executors tm WHERE tm.task_id=t.id AND tm.executor_id=@mine))
                """);
            args.Add(("@mine", mineId));
        }
        if (status is not null)
        {
            where.Add("t.status=@status");
            args.Add(("@status", status));
        }
        if (dueFrom is not null)
        {
            where.Add("t.due_date >= @from");
            args.Add(("@from", Sql.ToDb(dueFrom.Value)));
        }
        if (dueTo is not null)
        {
            where.Add("t.due_date <= @to");
            args.Add(("@to", Sql.ToDb(dueTo.Value)));
        }
        if (priorityMin is not null)
        {
            where.Add("t.priority_num >= @prioMin");
            args.Add(("@prioMin", priorityMin.Value));
        }
        if (priorityMax is not null)
        {
            where.Add("t.priority_num <= @prioMax");
            args.Add(("@prioMax", priorityMax.Value));
        }
        // фильтр по тэгам (T-222): несколько выбранных тэгов — по ИЛИ
        if (tags is { Count: > 0 })
        {
            var names = new List<string>();
            var index = 0;
            foreach (var tag in tags)
            {
                var name = $"@tag{index++}";
                names.Add(name);
                args.Add((name, tag));
            }
            where.Add("EXISTS (SELECT 1 FROM task_tags g WHERE g.task_id=t.id AND g.tag IN ("
                      + string.Join(", ", names) + "))");
        }
        if (templatesOnly)
        {
            where.Add("t.is_template=1");
        }
        else if (!includeTemplates)
        {
            where.Add("t.is_template=0");
        }
        var tasks = Sql.Query(conn, null,
            $"SELECT t.*, {PendingQuestionsSql} FROM tasks t " +
            $"WHERE {string.Join(" AND ", where)} ORDER BY t.priority_num DESC, t.created_at",
            Map, args.ToArray());
        foreach (var task in tasks)
        {
            LoadLinks(conn, task);
        }
        // полнотекстный поиск: заголовок — по проекции, описание — по файлу .md (объёмы локальные)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            tasks = tasks.Where(t =>
                    t.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                    || ReadDescription(t).Contains(needle, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }
        return Decorate(tasks);
    }

    /// <summary>Значение фильтра «задачи без сервера» (их ведёт дирижёр, ТЗ гл. 6).</summary>
    public const string NoServer = "none";

    /// <summary>Вычисляемая колонка «висящие вопросы агентов» (ТЗ v1.17) для списков задач.</summary>
    private const string PendingQuestionsSql = """
        (SELECT COUNT(*) FROM chat_messages c
         WHERE c.task_id=t.id AND c.kind='question' AND c.answered_at IS NULL AND c.deleted_at IS NULL)
        AS pending_questions
        """;

    public TaskItem? Get(string id)
    {
        using var conn = _db.Open();
        var task = Sql.Query(conn, null,
                $"SELECT t.*, {PendingQuestionsSql} FROM tasks t WHERE t.id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (task is not null)
        {
            LoadLinks(conn, task);
            Decorate(task);
        }
        return task;
    }

    public string ReadDescription(TaskItem task) =>
        task.DescriptionPath.Length == 0 ? "" : _files.ReadText(task.DescriptionPath);

    public string ReadAcceptance(TaskItem task) =>
        task.AcceptancePath.Length == 0 ? "" : _files.ReadText(task.AcceptancePath);

    public List<string> Artifacts(TaskItem task)
    {
        using var conn = _db.Open();
        return _files.ListTaskArtifacts(ProjectSlug(conn, task.ProjectId), task.DisplayId);
    }

    /// <summary>
    /// Путь файла РУЧНОГО результата задачи (T-130-S0): <c>&lt;код&gt;-result.md</c> в её
    /// artifacts/. Файла может ещё не быть — путь считается по имени, иначе карточка не
    /// смогла бы завести результат у задачи, у которой результата нет вовсе. Окончание
    /// «-result.md» намеренно то же, что у результата задания: такой файл наравне с ними
    /// уезжает в обсуждение источника (<c>ResultExport</c>).
    /// </summary>
    public string ManualResultPath(TaskItem task)
    {
        using var conn = _db.Open();
        return _files.TaskArtifactRel(ProjectSlug(conn, task.ProjectId), task.DisplayId,
            $"{task.DisplayId}-result.md");
    }

    /// <summary>Текст артефакта задачи по относительному пути из <see cref="Artifacts"/>
    /// (T-107-S0: выкладка результата в обсуждение источника). Файла нет — пустая строка.</summary>
    public string ReadArtifact(string relativePath) => _files.ReadText(relativePath);

    /// <summary>
    /// Файл принадлежит самой задаче (ТЗ v1.45, todo37_3): её каталог
    /// (artifacts/, ai/, description.md) либо вставки MD-редактора в uploads/ проекта.
    /// Только такие файлы разрешено удалять из окна «все файлы»; внешние ссылки,
    /// файлы папки проекта и чужих задач — нет.
    /// </summary>
    public bool OwnsFile(TaskItem task, string relPath)
    {
        if (relPath.Trim().Length == 0 || !_files.IsInside(relPath))
        {
            return false;
        }
        using var conn = _db.Open();
        var slug = ProjectSlug(conn, task.ProjectId);
        var abs = _files.Abs(relPath);
        bool Under(string dirRel) =>
            abs.StartsWith(_files.Abs(dirRel) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        return Under(_files.TaskDirRel(slug, task.DisplayId))
               || Under(Path.Combine("projects", slug ?? "_no_project", "uploads"));
    }

    /// <summary>
    /// Удалить файл задачи (кнопка окна «все файлы», ТЗ v1.45, todo37_3). Удаление
    /// физическое, а не в .trash: смысл кнопки — освободить место под многогигабайтными
    /// видеорезультатами медиа-моделей, из артефактов иначе не удаляемыми.
    /// </summary>
    public void DeleteFile(TaskItem task, string relPath, string? actorId)
    {
        // файлы задачи принадлежат её серверу (ТЗ гл. 6): у чужой задачи они только читаются
        EnsureCanWrite(task);
        if (!OwnsFile(task, relPath))
        {
            throw new ArgumentException(Loc.T("msg.task.3"));
        }
        var abs = _files.Abs(relPath);
        if (!File.Exists(abs))
        {
            throw new ArgumentException(Loc.T("msg.task.4", relPath));
        }
        try
        {
            File.Delete(abs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(Loc.T("msg.task.5", ex.Message), ex);
        }
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskFileDeleted,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new { path = relPath }),
        });
    }

    /// <param name="notify">Поднимать ли событие <see cref="Created"/> (T-16-S0). false —
    /// когда дерево заводится ЦЕЛИКОМ и по частям оно недостроено: копия шаблона создаёт
    /// узлы сверху вниз, а блокирующие связи проставляет вообще после всех, — очередь
    /// иерархии, узнав о голове раньше её потомков, запустила бы голову первой.</param>
    public TaskItem Create(TaskItem task, string descriptionMd, string acceptanceMd, string? actorId,
        bool notify = true)
    {
        if (task.Title.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.task.6"));
        }
        task.Title = task.Title.Trim();
        var now = DateTime.UtcNow;
        task.CreatedAt = now;
        task.UpdatedAt = now;

        // владелец новой задачи (ТЗ гл. 6, этап 42; T-1-S0): ВСЕГДА сервер, на котором её
        // создают, — и на рядовом сервере, и на дирижёре. Пустым это поле не остаётся:
        // «без сервера» означает «у того, кто сегодня дирижёр», и смена дирижёра уводила
        // такие задачи на другой компьютер. С T-36-S0 то же правило действует и у ШАБЛОНОВ:
        // шаблон заводится на своём сервере и передаётся другому кнопкой «сменить сервер».
        task.ServerId = task.ServerId is { Length: > 0 } ? task.ServerId : DefaultOwner();
        EnsureCanWrite(task);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        Validate(conn, tx, task, isNew: true);
        task.DisplayId = Database.NextDisplayId(conn, tx, task.Kind == TaskKind.Process ? "P" : "T",
            CodeForNew(task.ServerId));

        var slug = ProjectSlug(conn, task.ProjectId, tx);
        task.DescriptionPath = _files.WriteTaskDescription(slug, task.DisplayId, descriptionMd);
        task.AcceptancePath = acceptanceMd.Trim().Length > 0
            ? _files.WriteTaskAcceptance(slug, task.DisplayId, acceptanceMd)
            : "";

        Sql.Exec(conn, tx, """
            INSERT INTO tasks (id, display_id, project_id, team_id, parent_id, kind, title,
                               description_path, status, ai_done_status,
                               parent_in_prompt, siblings_in_prompt, time_quality,
                               priority, priority_num,
                               is_not_split, due_date, planned_hours,
                               responsible_id, launch_json, acceptance_path, is_template, template_id,
                               external_ref, import_url, server_id, created_at, updated_at)
            VALUES (@id, @did, @project, @team, @parent, @kind, @title, @descr, @status, @doneStatus,
                    @parentInPrompt, @siblingsInPrompt, @timeQuality,
                    @priority, @prioNum, @notSplit, @due, @hours, @responsible, @launch, @acceptance,
                    @template, @templateId, @externalRef, @importUrl, @server, @created, @updated)
            """,
            ("@server", task.ServerId),
            ("@id", task.Id), ("@did", task.DisplayId), ("@project", task.ProjectId),
            ("@team", task.TeamId), ("@parent", task.ParentId), ("@kind", task.Kind.ToDb()),
            ("@title", task.Title), ("@descr", task.DescriptionPath), ("@status", task.Status),
            // статус по готовности (T-250): пусто и мусор приводятся к «проверке» — так же,
            // как это делает умолчание колонки у задач, заведённых до версии 1.90
            ("@doneStatus", NormalizeAiDoneStatus(task.AiDoneStatus)),
            // секция «оптимизация» (T-23-S0): галочки пишутся как есть, «время ↔ качество»
            // приводится к отрезку 0..1, а пусто остаётся пустым — это «взять из проекта»
            ("@parentInPrompt", task.ParentInPrompt ? 1 : 0),
            ("@siblingsInPrompt", task.SiblingsInPrompt ? 1 : 0),
            ("@timeQuality", NormalizeTimeQuality(task.TimeQuality)),
            ("@priority", task.Priority), ("@prioNum", task.PriorityNum),
            ("@notSplit", task.IsNotSplit ? 1 : 0), ("@due", Sql.ToDbN(task.DueDate)),
            ("@hours", task.PlannedHours), ("@responsible", task.ResponsibleId),
            ("@launch", task.LaunchJson), ("@acceptance", task.AcceptancePath),
            ("@template", task.IsTemplate ? 1 : 0), ("@templateId", task.TemplateId),
            ("@externalRef", task.ExternalRef), ("@importUrl", NormalizeImportUrl(task.ImportUrl)),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        SaveLinks(conn, tx, task);

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskCreated,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                task.DisplayId,
                task.Title,
                status = task.Status,
                kind = task.Kind.ToDb(),
            }),
        });
        if (task.ExecutorIds.Count > 0)
        {
            AppendAssigned(conn, tx, task, actorId);
        }

        tx.Commit();
        var created = Decorate(task);
        if (notify)
        {
            // событие поднимается ПОСЛЕ фиксации — подписчик читает задачу из базы (T-16-S0)
            Created?.Invoke(created, actorId);
        }
        return created;
    }

    public TaskItem Update(TaskItem task, string descriptionMd, string acceptanceMd, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var old = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id", Map, ("@id", task.Id)).FirstOrDefault()
                  ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", task.Id));
        LoadLinks(conn, old, tx);
        // чужая задача правится только на своём сервере (ТЗ гл. 6): сервер и номер задачи
        // формой не меняются — для смены сервера есть отдельная операция ChangeServer
        EnsureCanWrite(old);

        // номер и сервер восстанавливаются из базы ДО проверок (T-220-S0): форма задачи их
        // не несёт (сервер меняется отдельной операцией ChangeServer), и проверка
        // «исполнитель с локальной моделью — на сервере задачи» видела пустое поле. На
        // сервере, который не дирижёр, «пусто» означает «задача дирижёра», и свой же
        // локальный исполнитель объявлялся чужим: «Исполнитель „Bill“ работает на модели
        // сервера S0 — передайте задачу туда», хотя задача уже здесь
        task.DisplayId = old.DisplayId;
        task.ServerId = old.ServerId;
        Validate(conn, tx, task);
        var slug = ProjectSlug(conn, task.ProjectId, tx);
        task.DescriptionPath = _files.WriteTaskDescription(slug, task.DisplayId, descriptionMd);
        task.AcceptancePath = acceptanceMd.Trim().Length > 0
            ? _files.WriteTaskAcceptance(slug, task.DisplayId, acceptanceMd)
            : old.AcceptancePath;

        Sql.Exec(conn, tx, """
            UPDATE tasks SET project_id=@project, team_id=@team, parent_id=@parent, title=@title,
                             description_path=@descr, status=@status, ai_done_status=@doneStatus,
                             parent_in_prompt=@parentInPrompt, siblings_in_prompt=@siblingsInPrompt,
                             time_quality=@timeQuality,
                             priority=@priority,
                             priority_num=@prioNum, is_not_split=@notSplit, due_date=@due,
                             planned_hours=@hours, responsible_id=@responsible, launch_json=@launch,
                             acceptance_path=@acceptance, is_template=@template,
                             external_ref=COALESCE(@externalRef, external_ref),
                             import_url=@importUrl, updated_at=@updated
            WHERE id=@id
            """,
            ("@project", task.ProjectId), ("@team", task.TeamId), ("@parent", task.ParentId),
            ("@title", task.Title), ("@descr", task.DescriptionPath), ("@status", task.Status),
            // статус по готовности (T-250) пишется КАК ЕСТЬ: форма несёт его всегда, а те,
            // кто зовёт Update, правят задачу, прочитанную из базы, — там он уже стоит
            ("@doneStatus", NormalizeAiDoneStatus(task.AiDoneStatus)),
            // секция «оптимизация» (T-23-S0) пишется КАК ЕСТЬ — по той же причине, что и
            // статус по готовности: форма несёт все три поля всегда, а остальные, кто зовёт
            // Update, правят задачу, прочитанную из базы, — там они уже стоят
            ("@parentInPrompt", task.ParentInPrompt ? 1 : 0),
            ("@siblingsInPrompt", task.SiblingsInPrompt ? 1 : 0),
            ("@timeQuality", NormalizeTimeQuality(task.TimeQuality)),
            ("@priority", task.Priority), ("@prioNum", task.PriorityNum),
            ("@notSplit", task.IsNotSplit ? 1 : 0), ("@due", Sql.ToDbN(task.DueDate)),
            ("@hours", task.PlannedHours), ("@responsible", task.ResponsibleId),
            ("@launch", task.LaunchJson), ("@acceptance", task.AcceptancePath),
            ("@template", task.IsTemplate ? 1 : 0), ("@externalRef", task.ExternalRef),
            // ссылка импорта (T-246) пишется КАК ЕСТЬ, без COALESCE: её надо уметь очистить
            // из формы — «поле пустое» и есть намерение человека. Поэтому все, кто зовёт
            // Update, обязаны нести её в задаче; форма несёт (TaskDialog), остальные правят
            // задачу, прочитанную из базы
            ("@importUrl", NormalizeImportUrl(task.ImportUrl)),
            ("@updated", Sql.ToDb(now)), ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_executors WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_alt_executors WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_skills WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_blockers WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_tags WHERE task_id=@id", ("@id", task.Id));
        SaveLinks(conn, tx, task);

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskUpdated,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new { task.DisplayId, task.Title }),
        });
        if (old.Status != task.Status)
        {
            AppendStatusChanged(conn, tx, task, old.Status, actorId);
        }
        if (!old.ExecutorIds.SequenceEqual(task.ExecutorIds))
        {
            AppendAssigned(conn, tx, task, actorId);
        }

        tx.Commit();
        task.UpdatedAt = now;
        if (old.Status != task.Status)
        {
            StatusChanged?.Invoke(task, old.Status, actorId);
        }
        return Decorate(task);
    }

    /// <summary>
    /// Смена сервера-владельца задачи (кнопка «сменить сервер», ТЗ гл. 6, этап 42).
    /// Доступна на сервере, которому задача принадлежит сейчас, и на дирижёре — если сервер
    /// у задачи не указан. Запись делает ПРЕЖНИЙ владелец: единственный писатель строки
    /// сохраняется, новый владелец её просто принимает после ближайшего цикла репликации.
    ///
    /// Номер задачи НЕ меняется — это идентификатор и имя каталога её файлов, он вставлен
    /// в тексты описаний и чатов (ТЗ гл. 6 «Номера задач»).
    ///
    /// Новый владелец обязан быть НАЗВАН (T-1-S0): вариант «без сервера» убран, потому что
    /// он привязывает задачу к роли дирижёра, а не к компьютеру.
    ///
    /// ШАБЛОН передаётся той же операцией (T-36-S0), и передаётся ВСЕМ ПОДДЕРЕВОМ: узлы
    /// шаблона — это одна иерархия, из которой копируется бизнес-процесс целиком, и половина
    /// дерева, оставшаяся у прежнего сервера, была бы там нередактируемой. Уезжают только
    /// узлы, принадлежащие сейчас тому же серверу, что и голова (чужие подмешанные узлы
    /// остаются своим владельцам).
    /// </summary>
    /// <param name="serverId">Новый владелец; пусто — ошибка: задача без сервера не остаётся.</param>
    public TaskItem ChangeServer(string taskId, string? serverId, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id AND deleted_at IS NULL",
                       Map, ("@id", taskId)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        EnsureCanWrite(task);
        // «БЕЗ СЕРВЕРА» БОЛЬШЕ НЕ ПРЕДЛАГАЕТСЯ (T-1-S0): такая задача принадлежит не серверу,
        // а РОЛИ — тому, кто сегодня дирижёр, — и при смене дирижёра уезжает на другой
        // компьютер вместе со своими заданиями. Отдать задачу дирижёру по-прежнему можно,
        // но НАЗВАВ его сервер: тогда владение переживает любую смену роли
        if (serverId is not { Length: > 0 })
        {
            throw new ArgumentException(Loc.T("msg.task.32"));
        }
        if (serverId == task.ServerId)
        {
            tx.Commit();
            return Decorate(task);
        }
        var from = task.ServerId;
        if (task.IsTemplate)
        {
            // шаблон уезжает деревом (T-36-S0): голова и все её узлы того же владельца
            Sql.Exec(conn, tx, """
                WITH RECURSIVE sub(id) AS (
                    SELECT id FROM tasks WHERE id=@id
                    UNION ALL
                    SELECT t.id FROM tasks t JOIN sub ON t.parent_id=sub.id
                     WHERE t.deleted_at IS NULL AND t.is_template=1
                )
                UPDATE tasks SET server_id=@s, updated_at=@u
                 WHERE id IN (SELECT id FROM sub) AND deleted_at IS NULL
                   AND (server_id=@from OR (@from IS NULL AND server_id IS NULL))
                """,
                ("@s", serverId), ("@u", Sql.ToDb(now)), ("@id", taskId), ("@from", from));
        }
        else
        {
            Sql.Exec(conn, tx, "UPDATE tasks SET server_id=@s, updated_at=@u WHERE id=@id",
                ("@s", serverId), ("@u", Sql.ToDb(now)), ("@id", taskId));
        }
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskServerChanged,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                task.DisplayId,
                fromServer = _scope.CodeOf(from),
                toServer = _scope.CodeOf(serverId),
            }),
        });
        tx.Commit();
        task.ServerId = serverId;
        task.UpdatedAt = now;
        LoadLinks(conn, task);
        return Decorate(task);
    }

    /// <summary>
    /// Передать дирижёру задачи сервера, выведенного из кластера (ТЗ гл. 6, этап 42):
    /// кнопка «передать задачи дирижёру» у неактивного сервера. Это ЕДИНСТВЕННЫЙ случай,
    /// когда владельца задачи меняет не её сервер — иначе задачи выбывшего сервера остались
    /// бы нередактируемыми навсегда. Возвращает число переданных задач.
    /// </summary>
    public int TakeOverTasksOf(string serverId, string? actorId)
    {
        if (!_scope.IsConductor)
        {
            throw new ArgumentException(
                Loc.T("msg.task.10"));
        }
        if (serverId.Trim().Length == 0 || serverId == _scope.ServerId)
        {
            throw new ArgumentException(Loc.T("msg.task.11"));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var count = (int)Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM tasks WHERE server_id=@s AND deleted_at IS NULL", ("@s", serverId));
        if (count == 0)
        {
            tx.Commit();
            return 0;
        }
        Sql.Exec(conn, tx,
            "UPDATE tasks SET server_id=@to, updated_at=@u WHERE server_id=@s AND deleted_at IS NULL",
            ("@to", _scope.ServerId), ("@u", Sql.ToDb(now)), ("@s", serverId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.TasksTakenOver,
            EntityType = "server",
            EntityId = serverId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                fromServer = _scope.CodeOf(serverId),
                toServer = _scope.Code,
                tasks = count,
            }),
        });
        tx.Commit();
        return count;
    }

    /// <summary>
    /// ЗАДАЧ БЕЗ СЕРВЕРА НЕ ОСТАЁТСЯ (T-1-S0): дирижёр забирает себе задачи, у которых
    /// сервер-владелец не указан, — те, что он и так правит сегодня по правилу «бесхозную
    /// правит дирижёр».
    ///
    /// Зачем это нужно. «Без сервера» — не свойство задачи, а ссылка на РОЛЬ: сменили
    /// дирижёра — и все такие задачи разом стали чужими на том компьютере, где с ними
    /// работали (правка запрещена, задания не запускаются). Так накопились все задачи,
    /// заведённые до этой версии. Записав владельца явно, мы закрепляем сегодняшнее
    /// положение дел: кто правит — тот и записан.
    ///
    /// Это НЕ разовый шаг обновления, а СТОРОЖ: он зовётся при открытии организации на
    /// каждом старте, потому что бесхозные строки появляются и после обновления — их
    /// присылает партнёр прежней версии, а после смены дирижёра новому достаются те, до
    /// которых прежний не добрался. Идемпотентен: находить нечего — ничего и не делает.
    ///
    /// С T-36-S0 сторож забирает и ШАБЛОНЫ: у шаблона теперь тоже есть сервер-владелец,
    /// и заведённые прежними версиями бесхозные шаблоны закрепляются за сегодняшним
    /// дирижёром — тем, кто их и так правит.
    ///
    /// Не трогает: удалённые строки и чужие задачи. На рядовом сервере не делает ничего:
    /// писать бесхозные строки он не вправе.
    /// </summary>
    /// <returns>Сколько задач получило сервер; 0 — таких не было.</returns>
    public int ClaimTasksWithoutServer()
    {
        if (!_scope.IsConductor || _scope.ServerId is not { Length: > 0 } me)
        {
            return 0;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var count = (int)Sql.Scalar<long>(conn, tx, """
            SELECT COUNT(*) FROM tasks
            WHERE server_id IS NULL AND deleted_at IS NULL
            """);
        if (count == 0)
        {
            tx.Commit();
            return 0;
        }
        Sql.Exec(conn, tx, """
            UPDATE tasks SET server_id=@me, updated_at=@u
            WHERE server_id IS NULL AND deleted_at IS NULL
            """, ("@me", me), ("@u", Sql.ToDb(DateTime.UtcNow)));
        tx.Commit();
        return count;
    }

    /// <summary>Создать бизнес-процесс из шаблона (ТЗ пп. 2.3, 2.4): копируется вся иерархия —
    /// поля, исполнители, skills, описания и критерии приёмки; признак шаблона снимается,
    /// template_id копий указывает на исходные узлы шаблона; статусы копий — pending.
    /// При копировании система может переспрашивать данные (задел) — пока только базовая дата:
    /// если у головы шаблона задан срок и base_date передана, сроки всех узлов смещаются
    /// на разницу (base_date − срок головы).</summary>
    /// <summary>Поддерево шаблона в ширину — родители всегда раньше потомков; связи загружены.</summary>
    private List<TaskItem> TemplateNodes(TaskItem head)
    {
        var nodes = new List<TaskItem> { head };
        using var conn = _db.Open();
        for (var i = 0; i < nodes.Count; i++)
        {
            var children = Sql.Query(conn, null,
                "SELECT * FROM tasks WHERE parent_id=@p AND deleted_at IS NULL ORDER BY created_at",
                Map, ("@p", nodes[i].Id));
            foreach (var child in children)
            {
                LoadLinks(conn, child);
            }
            nodes.AddRange(children);
        }
        return nodes;
    }

    /// <summary>
    /// Анализ иерархии шаблона перед созданием задачи (todo31): есть ли сроки (переспросить
    /// базовую дату) и есть ли узлы со skills без активного исполнителя (переспросить вариант
    /// автоподбора). Указанный активный исполнитель автоподбором не заменяется.
    /// </summary>
    public TemplateInfoDto AnalyzeTemplate(string templateId)
    {
        var head = Get(templateId) ?? throw new InvalidOperationException(Loc.T("msg.task.12", templateId));
        if (!head.IsTemplate)
        {
            throw new ArgumentException(Loc.T("msg.task.13"));
        }
        var nodes = TemplateNodes(head);
        var info = new TemplateInfoDto();
        using var conn = _db.Open();
        foreach (var node in nodes)
        {
            if (node.DueDate is { } due)
            {
                info.HasDueDates = true;
                info.MinDueDate = info.MinDueDate is null || due < info.MinDueDate ? due : info.MinDueDate;
            }
            if (node.SkillIds.Count > 0
                && !node.ExecutorIds.Any(id => IsRowActive(conn, null, "executors", id)))
            {
                info.NeedsExecutorPick = true;
            }
        }
        return info;
    }

    /// <summary>Исполнители копии узла шаблона (todo31): неактивные снимаются; если активных
    /// не осталось, а skills заданы — автоподбор по варианту переспроса (без варианта или без
    /// кандидатов — узел остаётся без исполнителя). Неактивный ответственный снимается.
    /// <para>Список «могут заменить исполнителя» (T-221) копируется по тому же правилу:
    /// выключенные исполнители из него снимаются, остальные едут в копию как есть.
    /// Автоподбором он не достраивается — это ЯВНЫЙ выбор человека, и придумывать за него
    /// запасных незачем.</para></summary>
    private (List<string> ExecutorIds, List<string> AltExecutorIds, string? ResponsibleId) ResolveExecutors(
        TaskItem node, PickMode? pickMode, ExecutorPickService? picker)
    {
        List<string> active;
        List<string> alt;
        string? responsible;
        using (var conn = _db.Open())
        {
            active = node.ExecutorIds.Where(id => IsRowActive(conn, null, "executors", id)).ToList();
            alt = node.AltExecutorIds.Where(id => IsRowActive(conn, null, "executors", id)).ToList();
            responsible = node.ResponsibleId is { } resp && IsRowActive(conn, null, "executors", resp)
                ? resp
                : null;
        }
        if (active.Count == 0 && node.SkillIds.Count > 0 && pickMode is { } mode && picker is not null)
        {
            var picked = picker.Pick(node.ProjectId, node.TeamId, node.SkillIds, mode);
            if (picked.ExecutorId is not null)
            {
                active.Add(picked.ExecutorId);
            }
        }
        return (active, alt, responsible);
    }

    /// <summary>
    /// ОТВЕТСТВЕННЫЙ ПО УМОЛЧАНИЮ проекта (T-5-S0) — годный к подстановке в новую задачу.
    ///
    /// В настройках проекта лежит только идентификатор, и он мог протухнуть: исполнителя
    /// выключили, удалили или это вовсе ИИ (ответственным может быть только человек —
    /// то же правило проверяет <see cref="Validate"/>). Поэтому здесь настройка и приводится
    /// к делу: негодное умолчание — это не ошибка, а просто «умолчания нет», иначе
    /// выключенный ответственный ронял бы создание задач по всему проекту.
    /// </summary>
    public string? DefaultResponsibleOf(string? projectId)
    {
        if (projectId is not { Length: > 0 })
        {
            return null;
        }
        using var conn = _db.Open();
        return DefaultResponsibleOf(conn, null, projectId);
    }

    private static string? DefaultResponsibleOf(SqliteConnection conn, SqliteTransaction? tx, string? projectId)
    {
        if (projectId is not { Length: > 0 })
        {
            return null;
        }
        var settings = Sql.Scalar<string>(conn, tx,
            "SELECT settings_json FROM projects WHERE id=@id AND deleted_at IS NULL", ("@id", projectId));
        if (ProjectSettings.DefaultResponsibleId(settings) is not { } responsibleId)
        {
            return null;
        }
        var kind = Sql.Scalar<string>(conn, tx,
            "SELECT kind FROM executors WHERE id=@id AND deleted_at IS NULL AND is_active=1",
            ("@id", responsibleId));
        return kind == "human" ? responsibleId : null;
    }

    /// <summary>
    /// КОМАНДА КОПИИ ШАБЛОНА (T-63-S0). Копия, разворачиваемая ПОДЗАДАЧЕЙ, переезжает в проект
    /// родителя, а команда узла шаблона к этому проекту относиться не обязана: общий шаблон
    /// (у него проекта нет вовсе) вправе нести команду любого проекта, и она молча уезжала
    /// в чужую задачу — жалоба «подставилась команда не из этого проекта».
    ///
    /// Правило: команда годится, если она общая (<c>teams.project_id</c> пуст) либо привязана
    /// к проекту копии. Чужая отбрасывается, и вместо неё берётся команда родителя, а если и
    /// она не годится — команда по умолчанию проекта копии (п. 2.7); ничего не подошло — пусто.
    /// </summary>
    private string? TeamForCopy(string? nodeTeamId, string? parentTeamId, string? projectId)
    {
        using var conn = _db.Open();
        if (TeamFits(conn, nodeTeamId, projectId))
        {
            return nodeTeamId;
        }
        if (TeamFits(conn, parentTeamId, projectId))
        {
            return parentTeamId;
        }
        var byProject = DefaultTeamOf(conn, projectId);
        return TeamFits(conn, byProject, projectId) ? byProject : null;
    }

    /// <summary>Команда годится задаче этого проекта (T-63-S0): она жива, активна и либо
    /// общая (без проекта), либо привязана к этому же проекту. Проекта у задачи нет —
    /// проверять не по чему, годится любая живая команда.</summary>
    private static bool TeamFits(SqliteConnection conn, string? teamId, string? projectId)
    {
        if (teamId is not { Length: > 0 })
        {
            return false;
        }
        // COALESCE: пустая строка означает «команда без проекта», а null — «строки нет вовсе»
        var teamProject = Sql.Scalar<string>(conn, null,
            "SELECT COALESCE(project_id,'') FROM teams WHERE id=@id AND deleted_at IS NULL AND is_active=1",
            ("@id", teamId));
        if (teamProject is null)
        {
            return false;
        }
        return projectId is not { Length: > 0 } || teamProject.Length == 0 || teamProject == projectId;
    }

    /// <summary>Команда по умолчанию проекта (ТЗ п. 2.7) — из настроек проекта.</summary>
    private static string? DefaultTeamOf(SqliteConnection conn, string? projectId)
    {
        if (projectId is not { Length: > 0 })
        {
            return null;
        }
        var settings = Sql.Scalar<string>(conn, null,
            "SELECT settings_json FROM projects WHERE id=@id AND deleted_at IS NULL", ("@id", projectId));
        return ProjectSettings.DefaultTeamId(settings);
    }

    /// <summary>
    /// Статистика шаблона (ТЗ п. 2.11, todo32): смены состояния задач, привязанных
    /// к узлам поддерева шаблона (template_id), из журнала работ — состояние, дата-время,
    /// актор смены. Ник актора подставляет вызывающий (API).
    /// </summary>
    public List<TemplateStatusStatDto> TemplateStatusStats(string templateId)
    {
        using var conn = _db.Open();
        var rows = Sql.Query(conn, null, """
            WITH RECURSIVE nodes(id) AS (
              SELECT id FROM tasks WHERE id=@id
              UNION ALL
              SELECT t.id FROM tasks t JOIN nodes n ON t.parent_id = n.id
              WHERE t.deleted_at IS NULL
            )
            SELECT e.ts, e.actor_id, e.payload_json, e.task_id, t.display_id
            FROM events e
            JOIN tasks t ON t.id = e.task_id
            WHERE e.event_type = @type AND t.template_id IN (SELECT id FROM nodes)
            ORDER BY e.ts DESC
            """,
            r => new TemplateStatusStatDto
            {
                TaskId = r.S("task_id"),
                TaskDisplayId = r.S("display_id"),
                At = r.Dt("ts"),
                ExecutorId = r.SN("actor_id"),
                Status = StatusFromPayload(r.S("payload_json")),
            },
            ("@type", EventTypes.TaskStatusChanged), ("@id", templateId));
        return rows;
    }

    /// <summary>Новое состояние из payload события task.status_changed ({from, to}).</summary>
    private static string StatusFromPayload(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String
                ? to.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Создание задачи из шаблона (ТЗ п. 2.4). parentId (T-201) — задача, к которой
    /// подцепляется голова копии: так шаблон разворачивается ПОДЗАДАЧЕЙ из карточки задачи.
    /// Вся копия при этом переезжает в проект родителя: подзадача обязана лежать в его
    /// проекте (то же правило у переноса по иерархии, ChangeParent).</summary>
    public TaskItem InstantiateTemplate(string templateId, DateTime? baseDate, string? actorId,
        PickMode? pickMode = null, ExecutorPickService? picker = null, string? parentId = null)
    {
        var head = Get(templateId) ?? throw new InvalidOperationException(Loc.T("msg.task.12", templateId));
        if (!head.IsTemplate)
        {
            throw new ArgumentException(Loc.T("msg.task.14"));
        }

        TaskItem? parent = null;
        if (parentId is { Length: > 0 })
        {
            parent = Get(parentId) ?? throw new ArgumentException(Loc.T("msg.task.16", parentId));
            if (parent.IsTemplate)
            {
                // копия шаблона — обычная задача, а она не может висеть под узлом шаблона
                throw new ArgumentException(Loc.T("msg.task.18"));
            }
        }

        var nodes = TemplateNodes(head);

        // пересчёт сроков (todo31): базовая дата встаёт на место САМОЙ РАННЕЙ даты иерархии,
        // остальные сдвигаются на ту же дельту — интервалы между сроками сохраняются
        var minDue = nodes.Min(n => n.DueDate);
        TimeSpan? shift = baseDate is not null && minDue is not null
            ? baseDate.Value - minDue.Value
            : null;

        var idMap = new Dictionary<string, string>();
        TaskItem? newHead = null;
        // команда копии (T-63-S0): считается по проекту КОПИИ, а он у подзадачи — проект
        // родителя. Решение кэшируется по паре «команда узла + проект копии»: узлов в
        // шаблоне бывают десятки, а команд в них одна-две
        var teamOfCopy = new Dictionary<string, string?>();
        // ответственный по умолчанию берётся у проекта КОПИИ (T-5-S0), а он у разных узлов
        // может быть разным: голова переезжает в проект родителя, а узлы общего шаблона
        // проекта не имеют вовсе. Читается по проекту один раз — узлов в шаблоне бывают десятки
        var defaultResponsible = new Dictionary<string, string?>();
        foreach (var node in nodes)
        {
            // чат узлов шаблона не копируется (todo31) — у копии своя переписка
            var (executorIds, altExecutorIds, responsibleId) = ResolveExecutors(node, pickMode, picker);
            var copyProjectId = parent is not null ? parent.ProjectId : node.ProjectId;
            if (responsibleId is null && copyProjectId is { Length: > 0 } copyProject)
            {
                // умолчание проекта подставляется ТОЛЬКО в незаполненное поле: ответственный,
                // расписанный в шаблоне, — явный выбор человека, и он сильнее умолчания
                if (!defaultResponsible.TryGetValue(copyProject, out var byProject))
                {
                    byProject = DefaultResponsibleOf(copyProject);
                    defaultResponsible[copyProject] = byProject;
                }
                responsibleId = byProject;
            }
            var teamKey = (node.TeamId ?? "") + "|" + (copyProjectId ?? "");
            if (!teamOfCopy.TryGetValue(teamKey, out var copyTeamId))
            {
                copyTeamId = TeamForCopy(node.TeamId, parent?.TeamId, copyProjectId);
                teamOfCopy[teamKey] = copyTeamId;
            }
            var copy = new TaskItem
            {
                // при создании подзадачей (T-201) проект берётся у родителя — в том числе
                // у общего шаблона, у которого проекта нет вовсе; команда узла шаблона
                // сильнее родительской, но пустую (и ЧУЖУЮ проекту копии, T-63-S0)
                // заменяет команда родителя
                ProjectId = copyProjectId,
                TeamId = copyTeamId,
                ParentId = node.ParentId is not null && idMap.TryGetValue(node.ParentId, out var mapped)
                    ? mapped
                    : parent?.Id,
                Kind = node.Kind,
                Title = node.Title,
                Status = TaskStatuses.Pending,
                // статус по готовности (T-250) переезжает из узла шаблона: им расписано, надо
                // ли проверять результат этой работы человеку, — и в копии это ровно то же
                AiDoneStatus = node.AiDoneStatus,
                // секция «оптимизация» (T-23-S0) переезжает из узла шаблона целиком: ею
                // расписано, сколько контекста нужно этой работе и насколько тщательно её
                // делать, — а работа у копии ровно та же. «Время ↔ качество» переносится
                // КАК ЕСТЬ, включая «не задано»: тогда копия возьмёт умолчание СВОЕГО
                // проекта, а он у копии может быть не тот, что у шаблона
                ParentInPrompt = node.ParentInPrompt,
                SiblingsInPrompt = node.SiblingsInPrompt,
                TimeQuality = node.TimeQuality,
                Priority = node.Priority,
                PriorityNum = node.PriorityNum,
                IsNotSplit = node.IsNotSplit,
                DueDate = node.DueDate is { } due && shift is { } delta ? due + delta : node.DueDate,
                PlannedHours = node.PlannedHours,
                ResponsibleId = responsibleId,
                LaunchJson = node.LaunchJson,
                IsTemplate = false,
                TemplateId = node.Id,
                ExecutorIds = executorIds,
                // «могут заменить исполнителя» (T-221) обязан переехать вместе с задачей:
                // иначе копия шаблона, запущенная при занятом исполнителе, встала бы ждать,
                // хотя в шаблоне замена как раз и была расписана
                AltExecutorIds = altExecutorIds,
                SkillIds = node.SkillIds.ToList(),
                // тэги узла шаблона переезжают в созданную задачу (T-222): ими размечают
                // тему работы, а тема у копии та же самая
                Tags = node.Tags.ToList(),
            };
            // о копии шаблона очередь иерархии узнаёт ОДИН раз и в самом конце (T-16-S0):
            // узлы заводятся сверху вниз, блокирующие связи ставятся после всех, и проход
            // по недостроенному дереву запустил бы голову раньше её подзадач
            copy = Create(copy, ReadDescription(node), ReadAcceptance(node), actorId, notify: false);
            idMap[node.Id] = copy.Id;
            newHead ??= copy;
        }

        // блокирующие задачи (ТЗ v1.35): ссылки на узлы шаблона заменяются на созданные
        // из них задачи; ссылка на узел чужого шаблона (не скопирован) отбрасывается —
        // иначе задача была бы заблокирована шаблоном навсегда
        using (var conn = _db.Open())
        {
            foreach (var node in nodes.Where(n => n.BlockerIds.Count > 0))
            {
                foreach (var blockerId in node.BlockerIds)
                {
                    if (idMap.TryGetValue(blockerId, out var mappedBlocker))
                    {
                        Sql.Exec(conn, null,
                            "INSERT OR IGNORE INTO task_blockers (task_id, blocker_task_id) VALUES (@t, @b)",
                            ("@t", idMap[node.Id]), ("@b", mappedBlocker));
                    }
                }
            }
        }

        // правила безопасности узлов шаблона копируются в новые задачи (ТЗ пп. 2.4, гл. 12, todo25)
        using (var conn = _db.Open())
        {
            foreach (var (templateNodeId, copyId) in idMap)
            {
                // правило доступа к ФС в копии принадлежит ЭТОМУ серверу (ТЗ гл. 6, этап 42):
                // пути свои на каждом компьютере, и править копию должен тот, кто её создал.
                // Остальные правила от сервера не зависят и остаются общими
                Sql.Exec(conn, null, """
                    INSERT INTO security_rules (id, scope, scope_id, kind, rule_json, server_id,
                                                created_at, updated_at)
                    SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' ||
                           substr(lower(hex(randomblob(2))), 2) || '-a' || substr(lower(hex(randomblob(2))), 2) ||
                           '-' || lower(hex(randomblob(6))),
                           scope, @copyId, kind, rule_json,
                           CASE WHEN rule_json LIKE '%"dir_access"%' THEN @server ELSE NULL END,
                           @now, @now
                    FROM security_rules
                    WHERE scope='task' AND scope_id=@templateId AND deleted_at IS NULL
                    """,
                    ("@copyId", copyId), ("@templateId", templateNodeId), ("@server", _scope.ServerId),
                    ("@now", Sql.ToDb(DateTime.UtcNow)));
            }
        }

        using (var conn = _db.Open())
        using (var tx = conn.BeginTransaction())
        {
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                ProjectId = newHead!.ProjectId,
                TaskId = newHead.Id,
                EventType = EventTypes.TemplateApplied,
                EntityType = "task",
                EntityId = newHead.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    template = head.DisplayId,
                    created = newHead.DisplayId,
                    tasks = idMap.Count,
                    baseDate = baseDate?.ToString("O"),
                    pick = pickMode?.ToString(),
                    parent = parent?.DisplayId,
                }),
            });
            tx.Commit();
        }
        // дерево достроено целиком — теперь о нём можно рассказать очереди иерархии (T-16-S0):
        // копия шаблона, заведённая подзадачей внутри идущей иерархии, тоже должна пойти в работу
        NotifyCreated(newHead, actorId);
        return newHead;
    }

    /// <summary>Смена статуса (drag-n-drop на доске).</summary>
    public TaskItem ChangeStatus(string taskId, string status, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id", Map, ("@id", taskId)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        // смена статуса — тоже правка задачи, значит только на её сервере (ТЗ гл. 6)
        EnsureCanWrite(task);
        if (task.Status == status)
        {
            tx.Commit();
            return Decorate(task);
        }

        Sql.Exec(conn, tx, "UPDATE tasks SET status=@status, updated_at=@updated WHERE id=@id",
            ("@status", status), ("@updated", Sql.ToDb(now)), ("@id", taskId));
        var oldStatus = task.Status;
        task.Status = status;
        AppendStatusChanged(conn, tx, task, oldStatus, actorId);

        tx.Commit();
        StatusChanged?.Invoke(task, oldStatus, actorId);
        return Decorate(task);
    }

    /// <summary>
    /// Перенос задачи по иерархии (todo37): смена родителя перетаскиванием в представлении
    /// «иерархия». parentId=null — задача становится корневой. Проверки: родитель существует,
    /// это не сама задача и не её потомок (иначе ветка отвалилась бы от дерева), шаблон и
    /// задача не смешиваются, проект тот же (переносить задачу в чужой проект нельзя —
    /// её файлы лежат в каталоге своего проекта).
    /// </summary>
    public TaskItem ChangeParent(string taskId, string? parentId, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id AND deleted_at IS NULL",
                       Map, ("@id", taskId)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        // иерархия задач МЕЖДУ серверами меняется на дирижёре (ТЗ гл. 6, этап 42): он
        // единственный, кому разрешено писать строки, ему не принадлежащие. На остальных
        // серверах действует обычное правило владения
        if (!CanWrite(task) && !_scope.IsConductor)
        {
            EnsureCanWrite(task);
        }
        if (parentId is { Length: 0 })
        {
            parentId = null;
        }
        if (parentId == task.ParentId)
        {
            tx.Commit();
            return Decorate(task);
        }

        TaskItem? parent = null;
        if (parentId is not null)
        {
            if (parentId == taskId)
            {
                throw new ArgumentException(Loc.T("msg.task.15"));
            }
            parent = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id AND deleted_at IS NULL",
                         Map, ("@id", parentId)).FirstOrDefault()
                     ?? throw new ArgumentException(Loc.T("msg.task.16", parentId));
            if (parent.IsTemplate != task.IsTemplate)
            {
                throw new ArgumentException(task.IsTemplate
                    ? Loc.T("msg.task.17")
                    : Loc.T("msg.task.18"));
            }
            if (parent.ProjectId != task.ProjectId)
            {
                throw new ArgumentException(Loc.T("msg.task.19"));
            }
            if (IsDescendant(conn, tx, ancestorId: taskId, candidateId: parentId))
            {
                throw new ArgumentException(Loc.T("msg.task.20"));
            }
        }

        Sql.Exec(conn, tx, "UPDATE tasks SET parent_id=@parent, updated_at=@updated WHERE id=@id",
            ("@parent", parentId), ("@updated", Sql.ToDb(now)), ("@id", taskId));
        var oldParentCode = task.ParentId is null
            ? null
            : Sql.Scalar<string>(conn, tx, "SELECT display_id FROM tasks WHERE id=@id", ("@id", task.ParentId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskMoved,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                task.DisplayId,
                fromParent = oldParentCode,
                toParent = parent?.DisplayId,
            }),
        });

        tx.Commit();
        task.ParentId = parentId;
        task.UpdatedAt = now;
        LoadLinks(conn, task);
        return Decorate(task);
    }

    /// <summary>
    /// СМЕНА ИСПОЛНИТЕЛЯ ПЕРЕТАСКИВАНИЕМ (T-135-S0): на диаграмме подзадач задачу тащат на
    /// дорожку другого исполнителя. Отдельный вызов, а не сохранение всей записи, по той же
    /// причине, что и перенос по иерархии: меняется одно поле, и переписывать описанием,
    /// приёмкой и тэгами из давно прочитанного списка нечего.
    ///
    /// Правило задания: менять исполнителя можно, только пока задача НЕ ВЫПОЛНЕНА (сданной
    /// задаче исполнитель уже не нужен, а её дорожка — след того, кто её на самом деле
    /// делал). Пустой executorId — «без исполнителя», нижняя дорожка диаграммы.
    /// </summary>
    public TaskItem ChangeExecutor(string taskId, string? executorId, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id AND deleted_at IS NULL",
                       Map, ("@id", taskId)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        EnsureCanWrite(task);
        LoadLinks(conn, task, tx);
        if (TaskStatuses.Settled(task.Status))
        {
            throw new ArgumentException(Loc.T("msg.task.36"));
        }
        if (executorId is { Length: 0 })
        {
            executorId = null;
        }
        if (task.ExecutorIds.Count == (executorId is null ? 0 : 1)
            && (executorId is null || task.ExecutorIds[0] == executorId))
        {
            tx.Commit();
            return Decorate(task); // уже он: сброс на свою же дорожку
        }

        task.ExecutorIds = executorId is null ? [] : [executorId];
        // запасные (T-221) чистятся от нового основного: «заменил сам себя» не бывает
        task.AltExecutorIds = NormalizeAlt(task.AltExecutorIds, task.ExecutorIds);
        Validate(conn, tx, task);

        Sql.Exec(conn, tx, "DELETE FROM task_executors WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_alt_executors WHERE task_id=@id", ("@id", task.Id));
        if (executorId is not null)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_executors (task_id, executor_id) VALUES (@t, @e)",
                ("@t", task.Id), ("@e", executorId));
        }
        for (var i = 0; i < task.AltExecutorIds.Count; i++)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_alt_executors (task_id, executor_id, ord) VALUES (@t, @e, @o)",
                ("@t", task.Id), ("@e", task.AltExecutorIds[i]), ("@o", i));
        }
        Sql.Exec(conn, tx, "UPDATE tasks SET updated_at=@updated WHERE id=@id",
            ("@updated", Sql.ToDb(now)), ("@id", task.Id));
        AppendAssigned(conn, tx, task, actorId);

        tx.Commit();
        task.UpdatedAt = now;
        return Decorate(task);
    }

    /// <summary>
    /// ПРАВКА НАЗНАЧЕНИЙ ЗАДАЧИ (T-160-S0): исполнитель, список «могут заменить»,
    /// ответственный, навыки и тэги — одним вызовом. Отдельный метод, а не сохранение всей
    /// записи через <see cref="Update"/>, по той же причине, что <see cref="ChangeExecutor"/>
    /// и <see cref="ChangeParent"/>: правятся несколько полей, а описание, приёмка и сроки
    /// переписываться значениями из давно прочитанного списка не должны.
    ///
    /// <para>Каждый параметр НЕОБЯЗАТЕЛЕН: <c>null</c> — «поле не трогать», пустой список
    /// (и пустая строка у ответственного) — «очистить». Иначе действие агента, которому дали
    /// только тэги, снесло бы исполнителя.</para>
    ///
    /// <para>Правила остаются общими (<see cref="Validate"/>): исполнитель и запасные —
    /// участники команды задачи и не с чужого сервера, ответственный — только человек,
    /// у задачи один основной исполнитель. Права — обычные: чужую задачу правит её сервер.</para>
    /// </summary>
    public TaskItem ChangeAssignment(string taskId, List<string>? executorIds,
        List<string>? altExecutorIds, string? responsibleId, List<string>? skillIds,
        List<string>? tags, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id AND deleted_at IS NULL",
                       Map, ("@id", taskId)).FirstOrDefault()
                   ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        EnsureCanWrite(task);
        LoadLinks(conn, task, tx);
        var oldExecutors = task.ExecutorIds.ToList();

        if (executorIds is not null)
        {
            task.ExecutorIds = executorIds;
        }
        if (altExecutorIds is not null)
        {
            task.AltExecutorIds = altExecutorIds;
        }
        if (skillIds is not null)
        {
            task.SkillIds = skillIds;
        }
        if (tags is not null)
        {
            task.Tags = tags;
        }
        if (responsibleId is not null)
        {
            // пустая строка — «снять ответственного»: null здесь означает «не трогать»
            task.ResponsibleId = responsibleId.Length == 0 ? null : responsibleId;
        }
        Validate(conn, tx, task);

        Sql.Exec(conn, tx, "DELETE FROM task_executors WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_alt_executors WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_skills WHERE task_id=@id", ("@id", task.Id));
        Sql.Exec(conn, tx, "DELETE FROM task_tags WHERE task_id=@id", ("@id", task.Id));
        // блокирующие не трогаются вовсе: их этот вызов не правит, а SaveLinks вставляет
        // связи INSERT OR IGNORE — уже стоящие строки остаются как есть
        SaveLinks(conn, tx, task);
        Sql.Exec(conn, tx, "UPDATE tasks SET responsible_id=@resp, updated_at=@updated WHERE id=@id",
            ("@resp", task.ResponsibleId), ("@updated", Sql.ToDb(now)), ("@id", task.Id));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskUpdated,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new { task.DisplayId, task.Title }),
        });
        if (!oldExecutors.SequenceEqual(task.ExecutorIds))
        {
            AppendAssigned(conn, tx, task, actorId);
        }

        tx.Commit();
        task.UpdatedAt = now;
        return Decorate(task);
    }

    /// <summary>Кандидат лежит в поддереве предка (защита от цикла при переносе, todo37).</summary>
    private static bool IsDescendant(SqliteConnection conn, SqliteTransaction tx,
        string ancestorId, string candidateId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = candidateId;
        while (current is not null && seen.Add(current))
        {
            if (current == ancestorId)
            {
                return true;
            }
            current = Sql.Scalar<string>(conn, tx,
                "SELECT parent_id FROM tasks WHERE id=@id", ("@id", current));
        }
        return false;
    }

    /// <summary>
    /// Пометить задачу разбитой на подзадачи (ТЗ v1.26, todo28): is_not_split=1 (защита от
    /// повторного разбиения) + флаг autoSplit в launch_json (по нему оркестратор ведёт
    /// подзадачи: очередь запуска, финальный анализ). Идемпотентно.
    /// </summary>
    public void MarkSplit(string taskId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        Sql.Exec(conn, tx,
            "UPDATE tasks SET is_not_split=1, launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", WithLaunchFlag(launchJson, "autoSplit")),
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
    }

    /// <summary>
    /// Атомарно пометить, что финальный анализ разбитой задачи запущен (флаг finalStarted
    /// в launch_json); false — уже был помечен (защита от двойного запуска финального задания).
    /// </summary>
    public bool TryMarkFinalStarted(string taskId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        if (HasLaunchFlag(launchJson, "finalStarted"))
        {
            tx.Commit();
            return false;
        }
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", WithLaunchFlag(launchJson, "finalStarted")),
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
        return true;
    }

    /// <summary>Булев флаг в launch_json задачи (autoSplit / finalStarted, ТЗ v1.26).</summary>
    public static bool HasLaunchFlag(string launchJson, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(name, out var v)
                   && v.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string WithLaunchFlag(string launchJson, string name, bool on = true)
    {
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(launchJson) as System.Text.Json.Nodes.JsonObject
                   ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        if (on)
        {
            root[name] = true;
        }
        else
        {
            root.Remove(name);
        }
        return root.ToJsonString();
    }

    // --- иерархический запуск (T-159) ---

    /// <summary>Имя флага иерархического запуска в launch_json (T-159): у КОРНЯ иерархии,
    /// пока очередь запуска её поддерева не отработала. По нему очередь продолжается после
    /// завершения каждой подзадачи и переживает перезапуск приложения.</summary>
    public const string HierarchyFlag = "runHierarchy";

    /// <summary>Флаг «запускать и задачи, вставшие с ошибкой» (T-186): у КОРНЯ иерархии,
    /// рядом с <see cref="HierarchyFlag"/> — по нему очередь перезапускает подзадачи в
    /// состоянии «встал с ошибкой». Ставится галочкой в переспросе и живёт до конца
    /// очереди, поэтому его видят и проходы сторожа после перезапуска приложения.</summary>
    public const string HierarchyErrorsFlag = "hierarchyWithErrors";

    /// <summary>Флаг «эта очередь уже перезапускала задачу после ошибки» (T-186): у САМОЙ
    /// задачи. Ошибка повторилась — очередь больше её не трогает и ждёт человека, иначе
    /// падающая задача крутилась бы в перезапусках. Снимается новым нажатием кнопки.</summary>
    public const string HierarchyRetryFlag = "hierarchyErrorRetried";

    /// <summary>Флаг «запускать и задачи в состоянии „доработка“» (T-210): у КОРНЯ иерархии,
    /// рядом с <see cref="HierarchyErrorsFlag"/> — вторая галочка переспроса. Живёт до конца
    /// очереди по той же причине: очередь идёт сама, и переспросить человека в ней негде.</summary>
    public const string HierarchyNeedsFixFlag = "hierarchyWithNeedsFix";

    /// <summary>Флаг «эта очередь уже запускала задачу из доработки» (T-210): у САМОЙ задачи.
    /// Задачу вернули в доработку снова — очередь больше её не трогает и ждёт человека
    /// (иначе получился бы круг «доработка → запуск → доработка»). Снимается новым
    /// нажатием кнопки, как и попытка после ошибки.</summary>
    public const string HierarchyNeedsFixRetryFlag = "hierarchyNeedsFixRetried";

    /// <summary>
    /// Флаг «АВТОЗАПУСК ПОДЗАДАЧ ВЫКЛЮЧЕН» (T-54-S0): у любой задачи ветки, действует на неё
    /// саму и на ВСЁ её поддерево (ищется по цепочке предков, как <see cref="HierarchyFlag"/>).
    ///
    /// <para>Появился по жалобе: человек переводил разобранные подзадачи в «готово», и каждый
    /// такой перевод сам запускал работу дальше — завершение задачи трогает с места и
    /// автозапуск потомков (todo22), и задачи, ждавшие её как блокирующую (ТЗ п. 2.12), и
    /// очередь авторазбиения. Остановить это было нечем: кнопка «остановить» снимает ОДНО
    /// задание, а кнопка остановки очереди показывается только при ОТКРЫТОЙ очереди
    /// иерархии — которой в этом случае и нет.</para>
    ///
    /// <para>Явную кнопку («запустить задачу», «запустить иерархию») флаг не отменяет: он
    /// про запуски, которые делает система сама. Нажатие «запустить иерархию» его снимает —
    /// человек прямо сказал «беги».</para>
    /// </summary>
    public const string NoAutoStartFlag = "noAutoStart";

    /// <summary>Предел глубины обхода дерева задач вверх и вниз (T-159): закольцованный
    /// parent_id не должен превращать обход в вечный цикл.</summary>
    public const int MaxHierarchyDepth = 32;

    /// <summary>
    /// КОРЕНЬ ОТКРЫТОЙ ОЧЕРЕДИ иерархического запуска (T-159) для задачи: сама задача либо
    /// ближайший её предок с пометкой <see cref="HierarchyFlag"/>; null — задача не внутри
    /// открытой очереди. Пометка стоит у КОРНЯ, а спрашивают о ней про любую задачу ветки —
    /// поэтому обход идёт вверх по цепочке предков, а не по флагу самой задачи.
    ///
    /// <para>Живёт здесь, а не в оркестраторе (где этим пользуются очередь и автозапуск),
    /// потому что то же самое нужно инструментам агента: перезапуск чужой задачи (T-31-S0)
    /// обязан поднять у корня галочку «запускать и задачи в доработке», иначе очередь
    /// пройдёт мимо перезапущенной задачи и это будет выглядеть как «система молчит».</para>
    /// </summary>
    public TaskItem? HierarchyRootOf(TaskItem task) => NearestWithFlag(task, HierarchyFlag);

    /// <summary>
    /// Задача, ВЫКЛЮЧИВШАЯ АВТОЗАПУСК для этой ветки (T-54-S0): сама задача либо ближайший её
    /// предок с пометкой <see cref="NoAutoStartFlag"/>; null — автозапуск разрешён.
    /// Обход тот же, что у <see cref="HierarchyRootOf"/>: пометку ставят на родителе, а
    /// спрашивают о ней про любую задачу поддерева.
    /// </summary>
    public TaskItem? AutoStartOffRootOf(TaskItem task) => NearestWithFlag(task, NoAutoStartFlag);

    /// <summary>Ближайшая задача цепочки «сама задача → предки» с заданным флагом launch_json;
    /// null — такой нет. Общий обход для пометок, которые ставят у корня ветки, а
    /// спрашивают про любую задачу под ним.</summary>
    private TaskItem? NearestWithFlag(TaskItem task, string flag)
    {
        var current = task;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; current is not null && depth <= MaxHierarchyDepth && seen.Add(current.Id); depth++)
        {
            if (HasLaunchFlag(current.LaunchJson, flag))
            {
                return current;
            }
            current = current.ParentId is null ? null : Get(current.ParentId);
        }
        return null;
    }

    /// <summary>
    /// Пометить задачу корнем иерархического запуска (T-159) либо снять пометку, когда
    /// запускать в иерархии больше нечего. Идемпотентно; остальные поля launch_json
    /// (режим запуска, отложенный старт) не трогаются.
    /// </summary>
    public void SetHierarchyRun(string taskId, bool on) => SetLaunchFlag(taskId, HierarchyFlag, on);

    /// <summary>
    /// Поставить или снять булев флаг launch_json задачи (T-186; T-159 — тот же приём).
    /// Идемпотентно, остальные поля launch_json не трогаются.
    /// </summary>
    public void SetLaunchFlag(string taskId, string name, bool on)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", WithLaunchFlag(launchJson, name, on)),
            ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
    }

    /// <summary>
    /// РЕЖИМ ЗАПУСКА задачи в launch_json (ТЗ п. 2.1): manual / auto / schedule.
    /// Остальные поля launch_json не трогаются.
    ///
    /// <para>Нужен перезапуску задачи агентом (set_task_status с restart, T-34-S0): задачу,
    /// возвращённую в pending ВНЕ открытой очереди иерархии, поднимает только автозапуск
    /// (<c>JobOrchestrator.AutoStartUnblockedAsync</c>), а он берёт задачи с режимом «auto».
    /// Без этого «упало — перезапустил — жду» кончалось бы тем, что задача спит навсегда.</para>
    /// </summary>
    public void SetLaunchMode(string taskId, string mode)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(launchJson) as System.Text.Json.Nodes.JsonObject
                   ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        root["mode"] = mode;
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", root.ToJsonString()), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
    }

    /// <summary>Корни незакрытых иерархических запусков (T-159): их очередь раз в минуту
    /// проверяет сторож оркестратора — так подзадача трогается с места и тогда, когда
    /// исполнитель освободился на задаче ИЗ ДРУГОЙ иерархии.</summary>
    public List<TaskItem> ListHierarchyRuns()
    {
        using var conn = _db.Open();
        var tasks = Sql.Query(conn, null, $"""
            SELECT * FROM tasks
            WHERE deleted_at IS NULL AND is_template=0
              AND launch_json LIKE '%{HierarchyFlag}%'
            ORDER BY priority_num DESC, created_at
            """, Map)
            .Where(t => HasLaunchFlag(t.LaunchJson, HierarchyFlag))
            .ToList();
        foreach (var task in tasks)
        {
            LoadLinks(conn, task);
            Decorate(task);
        }
        return tasks;
    }

    // --- круги повторной проверки (T-31-S0) ---

    /// <summary>Флаг «по концу задания уйти в ОЖИДАНИЕ» (T-31-S0): у САМОЙ задачи-прогонщика.
    /// Ставится её же агентом (действие wait_for_recheck) и означает, что нормально
    /// завершённое задание уводит задачу не в «статус при завершении» (T-250), а в «ожидает» —
    /// исполнитель при этом освобождается и может взять перезапущенные задачи. Снимается
    /// коннектором при завершении: флаг живёт ровно один заход.</summary>
    public const string RecheckWaitFlag = "recheckWait";

    /// <summary>Счётчик кругов повторной проверки в launch_json (T-31-S0): сколько раз задача
    /// уже уходила в ожидание перезапущенных задач. Сравнивается с пределом проекта
    /// (<see cref="ProjectSettings.RecheckLimit"/>), обнуляется, когда задание завершилось
    /// БЕЗ ожидания — то есть круг замкнулся и работа сдана.</summary>
    public const string RecheckPassField = "recheckPass";

    /// <summary>Сколько кругов повторной проверки задача уже сделала (T-31-S0); 0 — ни одного
    /// либо поле испорчено.</summary>
    public static int RecheckPass(string launchJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(RecheckPassField, out var v)
                   && v.ValueKind == JsonValueKind.Number
                   && v.TryGetInt32(out var value) && value > 0
                ? value
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Пометить задачу «по концу задания уйти в ожидание» и записать номер круга (T-31-S0).
    /// Оба поля пишутся ОДНОЙ правкой launch_json: разными вызовами второй перечитал бы
    /// строку и потерял первый.
    /// </summary>
    public void SetRecheckWait(string taskId, int pass)
    {
        WithLaunch(taskId, root =>
        {
            root[RecheckWaitFlag] = true;
            root[RecheckPassField] = pass;
        });
    }

    /// <summary>
    /// ПРОЧИТАТЬ И СНЯТЬ пометку ожидания (T-31-S0) — одной транзакцией: коннектор
    /// спрашивает «уходим в ожидание?» ровно в тот момент, когда решает, куда девать
    /// завершённую задачу, и пометка не должна пережить это решение (иначе следующее
    /// задание той же задачи опять ушло бы в ожидание, ничего не перезапустив).
    /// <paramref name="clearPass"/> — заодно обнулить счётчик кругов: так делается, когда
    /// ожидания нет, то есть работа сдана и следующему разбору полагаются свои три круга.
    /// </summary>
    public bool TakeRecheckWait(string taskId, bool clearPass = true)
    {
        var waiting = false;
        WithLaunch(taskId, root =>
        {
            // значение читается щадяще: в launch_json могло лечь что угодно (правка руками,
            // старая версия), а исключение здесь уронило бы завершение задания
            waiting = root[RecheckWaitFlag] is System.Text.Json.Nodes.JsonValue value
                      && value.TryGetValue<bool>(out var on) && on;
            root.Remove(RecheckWaitFlag);
            if (clearPass && !waiting)
            {
                root.Remove(RecheckPassField);
            }
        });
        return waiting;
    }

    /// <summary>Правка launch_json задачи одной транзакцией (T-31-S0): прочитали, изменили
    /// объект, записали. Испорченный JSON заменяется пустым объектом — как в соседних
    /// правках launch_json.</summary>
    private void WithLaunch(string taskId, Action<System.Text.Json.Nodes.JsonObject> edit)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(launchJson) as System.Text.Json.Nodes.JsonObject
                   ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        edit(root);
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", root.ToJsonString()), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
    }

    /// <summary>
    /// ПРЕДЕЛ КРУГОВ повторной проверки для задачи (T-31-S0): настройка её проекта, а нет
    /// проекта (или настройки) — <see cref="ProjectSettings.RecheckLimitDefault"/>. Читается
    /// из базы заново, как и «статус при завершении»: задание идёт часами, и человек мог
    /// поправить настройку уже после старта агента.
    /// </summary>
    public int RecheckLimitOf(string taskId)
    {
        using var conn = _db.Open();
        return ProjectSettings.RecheckLimit(Sql.Scalar<string>(conn, null, """
            SELECT p.settings_json FROM tasks t JOIN projects p ON p.id = t.project_id WHERE t.id=@id
            """, ("@id", taskId)));
    }

    // --- отложенный старт задачи (T-121) ---

    /// <summary>Имя поля отложенного старта в launch_json (T-121): момент UTC в ISO 8601,
    /// раньше которого задачу запускать не надо.</summary>
    private const string StartAfterField = "startAfter";

    /// <summary>
    /// Отложенный старт задачи (T-121): задача ждёт указанного момента — так переносится
    /// запуск, когда у ИИ-исполнителя почти не осталось лимита. null — переноса нет.
    /// </summary>
    public static DateTime? LaunchStartAfter(string launchJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(StartAfterField, out var v)
                && v.ValueKind == JsonValueKind.String
                && DateTime.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            {
                return at.ToUniversalTime();
            }
        }
        catch (JsonException)
        {
            // испорченный launch_json — переноса просто нет
        }
        return null;
    }

    /// <summary>
    /// Запомнить, кто РЕАЛЬНО ведёт задачу сейчас (T-221): проставляется при каждом запуске
    /// задания — и когда работу взял назначенный исполнитель, и когда его заменил свободный
    /// из списка «могут заменить исполнителя». Основной исполнитель (task_executors) при этом
    /// не меняется: он остаётся тем, кого назначил человек.
    ///
    /// Отдельным запросом, а не через <see cref="Update"/>: запуск не правит задачу целиком
    /// (описание, состав исполнителей, файлы), а Update ещё и требует прав на правку формы.
    /// </summary>
    public void SetActualExecutor(string taskId, string? executorId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null,
            "UPDATE tasks SET actual_executor_id=@e, updated_at=@now WHERE id=@id",
            ("@e", executorId), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
    }

    /// <summary>
    /// Перенести старт задачи на указанный момент UTC (T-121); null — снять перенос
    /// (задача запущена вручную либо сторожем). Возвращает предыдущий момент переноса:
    /// по нему видно, менялось ли время (повторное предупреждение в чат не пишется).
    /// </summary>
    public DateTime? SetStartAfter(string taskId, DateTime? atUtc)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        var previous = LaunchStartAfter(launchJson);
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(launchJson) as System.Text.Json.Nodes.JsonObject
                   ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        if (atUtc is { } at)
        {
            root[StartAfterField] = Sql.ToDb(at);
        }
        else
        {
            root.Remove(StartAfterField);
        }
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", root.ToJsonString()), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
        return previous;
    }

    // --- продолжение работы после сброса лимита (T-166) ---

    /// <summary>Имя поля в launch_json: id задания, чью сессию агента надо ПРОДОЛЖИТЬ, когда
    /// наступит отложенный старт (T-166). Лимит подписки не рвёт связь с CLI-агентом —
    /// у задания сохранён контекст живой сессии, и работа доделывается, а не начинается заново.</summary>
    private const string ResumeJobField = "resumeJob";

    /// <summary>Задание, чью сессию продолжают после сброса лимита (T-166); null — продолжать
    /// нечем, задача запустится новым заданием с начала.</summary>
    public static string? LaunchResumeJob(string launchJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(ResumeJobField, out var v)
                && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } jobId)
            {
                return jobId;
            }
        }
        catch (JsonException)
        {
            // испорченный launch_json — продолжать просто нечего
        }
        return null;
    }

    /// <summary>Запомнить (или забыть, null) задание для продолжения после сброса лимита
    /// (T-166). Остальные поля launch_json не трогаются.</summary>
    public void SetResumeJob(string taskId, string? jobId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var launchJson = Sql.Scalar<string>(conn, tx,
                             "SELECT launch_json FROM tasks WHERE id=@id", ("@id", taskId))
                         ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.15", taskId));
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(launchJson) as System.Text.Json.Nodes.JsonObject
                   ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        if (jobId is { Length: > 0 })
        {
            root[ResumeJobField] = jobId;
        }
        else
        {
            root.Remove(ResumeJobField);
        }
        Sql.Exec(conn, tx, "UPDATE tasks SET launch_json=@launch, updated_at=@now WHERE id=@id",
            ("@launch", root.ToJsonString()), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        tx.Commit();
    }

    // --- ожидание входа в CLI (todo96) ---

    /// <summary>Флаг «задача ждёт входа в Claude CLI» (todo96): у САМОЙ задачи, рядом
    /// с отложенным стартом. Сеанс CLI истёк — ждать нечего, нужен человек: он входит
    /// в AI2P (Настройки → Модели → «Вход в Claude CLI»), и вход отпускает такие задачи
    /// разом. Отложенный старт при этом всё равно ставится — как запасной путь на случай,
    /// если человек войдёт мимо AI2P (в терминале).</summary>
    public const string WaitAuthFlag = "waitAuth";

    /// <summary>Задачи, ждущие входа в CLI (todo96): их отпускает успешный вход. Отбор тот же,
    /// что у отложенного старта, — только ждущие запуска (черновик / ожидает / пауза).</summary>
    public List<TaskItem> ListWaitAuth()
    {
        using var conn = _db.Open();
        var tasks = Sql.Query(conn, null, $"""
            SELECT * FROM tasks
            WHERE deleted_at IS NULL AND is_template=0
              AND launch_json LIKE '%{WaitAuthFlag}%'
              AND status IN ('draft','pending','paused')
            ORDER BY priority_num DESC, created_at
            """, Map)
            .Where(t => HasLaunchFlag(t.LaunchJson, WaitAuthFlag))
            .ToList();
        foreach (var task in tasks)
        {
            LoadLinks(conn, task);
            Decorate(task);
        }
        return tasks;
    }

    /// <summary>
    /// Задачи с наступившим отложенным стартом (T-121): их запускает сторож оркестратора.
    /// Отбираются только ждущие запуска (черновик / ожидает / пауза) — задача, которую уже
    /// ведут вручную, второго старта не получит. «Пауза» здесь с T-185: именно в неё уходит
    /// задача, у исполнителя которой кончился лимит, — не будь её в отборе, перенесённый
    /// старт не наступил бы никогда.
    /// </summary>
    public List<TaskItem> ListStartAfterDue(DateTime nowUtc)
    {
        using var conn = _db.Open();
        var tasks = Sql.Query(conn, null, """
            SELECT * FROM tasks
            WHERE deleted_at IS NULL AND is_template=0
              AND launch_json LIKE '%startAfter%'
              AND status IN ('draft','pending','paused')
            ORDER BY priority_num DESC, created_at
            """, Map)
            .Where(t => LaunchStartAfter(t.LaunchJson) is { } at && at <= nowUtc)
            .ToList();
        foreach (var task in tasks)
        {
            LoadLinks(conn, task);
            Decorate(task);
        }
        return tasks;
    }

    /// <summary>
    /// Задачи, над которыми ИИ работает ПРЯМО СЕЙЧАС (T-187): у задачи есть незакрытое задание
    /// ИИ-исполнителя (в очереди, выполняется, на паузе) либо перенесённый на будущее старт —
    /// такая задача работы не потеряла, у исполнителя просто кончился лимит.
    ///
    /// СДАННАЯ задача (проверка, готово, отмена — <see cref="TaskStatuses.Settled"/>) в список
    /// не попадает вовсе, даже если у неё осталась открытая очередь иерархии или перенесённый
    /// старт: работы агента по ней уже нет, а человек видел бы её тут «на паузе» и не понимал,
    /// почему в карточке стоит «проверка» (T-2-S0). Живое задание — исключение: агент занят
    /// задачей прямо сейчас, что бы ни стояло у неё в статусе.
    ///
    /// Отбор идёт по ПОСЛЕДНЕМУ заданию задачи: оно и говорит, чей это агент и когда он взялся
    /// за работу. Фильтра у представления нет — сюда попадают задачи всех проектов и всех
    /// серверов организации, потому что смысл списка ровно в этом: одним взглядом увидеть, чем
    /// заняты агенты.
    /// </summary>
    public List<AiWorkItemDto> AiWork()
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        var rows = Sql.Query(conn, null, $"""
            SELECT t.*, {PendingQuestionsSql},
                   j.id AS job_id, j.state AS job_state, j.wait_kind AS job_wait,
                   j.started_at AS job_started, j.created_at AS job_created,
                   je.id AS job_executor_id, je.nick AS job_executor_nick, je.kind AS job_executor_kind
            FROM tasks t
            LEFT JOIN jobs j ON j.id = (SELECT x.id FROM jobs x
                                        WHERE x.task_id = t.id AND x.deleted_at IS NULL
                                        ORDER BY x.created_at DESC LIMIT 1)
            LEFT JOIN executors je ON je.id = j.executor_id
            WHERE t.deleted_at IS NULL AND t.is_template = 0
              AND (
                    (je.kind = 'ai' AND j.state IN ('queued','running','waiting_human'))
                    OR (t.status NOT IN ('review','done','cancelled') AND t.launch_json LIKE '%startAfter%')
                    OR (t.status NOT IN ('review','done','cancelled')
                        AND t.launch_json LIKE '%{HierarchyFlag}%')
                  )
            ORDER BY t.priority_num DESC, t.created_at
            """, r =>
        {
            var task = Map(r);
            var jobId = r.SN("job_id") ?? "";
            var jobState = r.SN("job_state") ?? "";
            var isAiJob = jobId.Length > 0 && r.SN("job_executor_kind") == "ai";
            var waiting = isAiJob && jobState == "waiting_human";
            // очередь иерархии открыта, а живого задания у задачи нет (T-224): она ждёт
            // своей очереди. Задача, которую агент уже взял, ждёт не очереди, а агента —
            // там причина считается по заданию, как и раньше
            var hierarchy = HasLaunchFlag(task.LaunchJson, HierarchyFlag)
                            && !(isAiJob && jobState is "queued" or "running");
            var item = new AiWorkItemDto
            {
                Task = task,
                JobId = jobId,
                ExecutorId = isAiJob ? r.SN("job_executor_id") ?? "" : "",
                ExecutorNick = isAiJob ? r.SN("job_executor_nick") ?? "" : "",
                StartedAt = jobId.Length == 0 ? null : (r.DtN("job_started") ?? r.Dt("job_created")),
                Pause = AiPause.Of(waiting, r.SN("job_wait") ?? "",
                    LaunchStartAfter(task.LaunchJson), task.PendingQuestions, now, hierarchy),
            };
            // задание живое: агент им занят прямо сейчас (в очереди, работает, на паузе)
            var active = isAiJob && jobState is "queued" or "running" or "waiting_human";
            return (Item: item, Active: active);
        });

        var items = new List<AiWorkItemDto>();
        foreach (var (item, active) in rows)
        {
            // перенос старта, время которого уже прошло, работой не считается: задачу
            // вот-вот поднимет сторож, а до тех пор ИИ ею не занят. Задача с открытой
            // очередью иерархии (T-224) остаётся в списке и без живого задания: работа по
            // ней идёт — просто сейчас в её подзадачах
            if (!active && item.Pause is not { Kind: AiPauseKinds.Limit or AiPauseKinds.Hierarchy })
            {
                continue;
            }
            if (item.ExecutorId.Length == 0)
            {
                // ИИ-задания у задачи ещё не было: старт перенесли до первого запуска (T-121),
                // либо последним по ней работал человек. Исполнителем такой задачи числится
                // назначенный ей ИИ-агент; работа не начиналась — времени работы нет
                var executor = Sql.Query(conn, null, """
                    SELECT e.id AS id, e.nick AS nick FROM task_executors te
                    JOIN executors e ON e.id = te.executor_id
                    WHERE te.task_id = @task AND e.kind = 'ai' LIMIT 1
                    """, r => (Id: r.S("id"), Nick: r.S("nick")), ("@task", item.Task.Id))
                    .FirstOrDefault();
                if (executor.Id is null)
                {
                    continue; // ИИ-исполнителя нет — это не работа агента
                }
                item.ExecutorId = executor.Id;
                item.ExecutorNick = executor.Nick;
                item.JobId = "";
                item.StartedAt = null;
            }
            else if (!active)
            {
                // работа была начата и прервана лимитом (T-166): задание уже закрыто, но
                // и агент, и время начала работы — те же, продолжать будет он
                item.JobId = "";
            }
            item.Paused = item.Pause is not null;
            LoadLinks(conn, item.Task);
            Decorate(item.Task);
            items.Add(item);
        }
        return items;
    }

    /// <summary>
    /// ДИАГРАММА ПОДЗАДАЧ (T-132-S0): всё поддерево задачи, разложенное по дорожкам
    /// исполнителей и по времени так, как его будет выполнять запуск иерархии (T-159).
    ///
    /// Считается целиком здесь по трём причинам: реально затраченное время лежит в заданиях
    /// (jobs), порядок дорожек — в иерархии команды, а сама последовательность повторяет
    /// очередь иерархии. Клиенту всё это по отдельности стоило бы десятков запросов на
    /// каждую перерисовку, а перерисовывается диаграмма, пока идёт работа, сама собой.
    ///
    /// ДЛИТЕЛЬНОСТЬ квадрата: у сданной задачи — реально затраченное время заданий, у идущей
    /// — не меньше уже прошедшего (пока меньше плановой, ширина не меняется), у остальных —
    /// плановая либо умолчание проекта (<see cref="ProjectSettings.DefaultTaskHours"/>).
    /// Отдельного хранилища затраченному времени не понадобилось: начало и конец каждого
    /// задания уже записаны в <c>jobs.started_at/finished_at</c>.
    ///
    /// ИСПОЛНИТЕЛЬ дорожки: у задачи, которую делают или уже делали, — тот, кто РЕАЛЬНО
    /// работал (задание, иначе <c>actual_executor_id</c>), у остальных — назначенный.
    ///
    /// T-135-S0 добавил к поддереву ещё два рода квадратов:
    /// <list type="bullet">
    /// <item>САМ КОРЕНЬ иерархии — задача, чью карточку человек открыл. Очередь выполняет её
    /// последней, поэтому и на диаграмме она последняя; двигать её отсюда нельзя (иерархию
    /// она и задаёт), а бросать на неё другие задачи можно;</item>
    /// <item>БЛОКИРУЮЩАЯ ЗАДАЧА ВНЕ ИЕРАРХИИ вместе со своим поддеревом. Раньше её не было
    /// вовсе, а красной красилась рамка того, кого она держит, — то есть на экране было
    /// видно, что «кто-то мешает», но не видно, кто и когда освободится. Теперь рисуется
    /// сама блокирующая (красной, вместе со своим исполнителем — у него появляется дорожка),
    /// её подзадачи тоже красные, а красная стрелка ведёт от неё к задержанной задаче.</item>
    /// </list>
    /// </summary>
    public TaskDiagramDto Diagram(string taskId)
    {
        var dto = new TaskDiagramDto();
        var root = Get(taskId);
        if (root is null)
        {
            return dto;
        }
        using var conn = _db.Open();
        dto.DefaultHours = ProjectSettings.DefaultTaskHours(Sql.Query(conn, null,
                "SELECT settings_json FROM projects WHERE id=@p",
                r => r.SN("settings_json"), ("@p", (object?)root.ProjectId)).FirstOrDefault());

        // всё поддерево: шаблоны и удалённые в очередь иерархии не попадают вовсе
        var all = Sql.Query(conn, null, $"""
            SELECT t.*, {PendingQuestionsSql} FROM tasks t
            WHERE t.deleted_at IS NULL AND t.is_template = 0
            """, Map);
        foreach (var task in all)
        {
            LoadLinks(conn, task);
            Decorate(task);
        }
        var byParent = all.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!);
        var ordered = new List<TaskItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // порядок — ровно тот, которым идёт очередь иерархии (JobOrchestrator): потомки
        // раньше родителя, внутри уровня по убыванию числового приоритета. Корень тоже
        // рисуется (T-135-S0) и оказывается последним сам собой: очередь выполняет его
        // после всего поддерева
        void Walk(TaskItem node, int depth, List<TaskItem> into)
        {
            if (depth > 32 || !seen.Add(node.Id))
            {
                return; // закольцованный parent_id: обход обязан кончиться
            }
            foreach (var child in byParent[node.Id]
                         .OrderByDescending(c => c.PriorityNum).ThenBy(c => c.CreatedAt))
            {
                Walk(child, depth + 1, into);
            }
            into.Add(node);
        }
        Walk(root, 0, ordered);
        if (ordered.Count <= 1)
        {
            return dto; // подзадач нет: один корень — это не диаграмма
        }

        // БЛОКИРУЮЩИЕ ВНЕ ИЕРАРХИИ (T-135-S0): каждая рисуется сама, со своим поддеревом и
        // своим исполнителем. Дальше вглубь (блокирующие блокирующей) не идём намеренно:
        // диаграмма показывает, кто держит ЭТУ иерархию, а не всю сеть зависимостей проекта
        var byId = all.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var outside = new List<TaskItem>();
        foreach (var blockerId in ordered.SelectMany(t => t.BlockerIds)
                     .Distinct(StringComparer.Ordinal).ToList())
        {
            if (!seen.Contains(blockerId) && byId.TryGetValue(blockerId, out var blocker))
            {
                Walk(blocker, 0, outside);
            }
        }
        var outsideIds = outside.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        // блокирующие идут ПЕРЕД иерархией: очередь и правда ждёт их первыми
        ordered.InsertRange(0, outside);

        // задания поддерева: по ним считаются затраченное время, реальный исполнитель и
        // состояние работы агента (те же правила, что во «в работе у ИИ», T-187)
        var ids = ordered.Select(t => t.Id).ToList();
        var args = ids.Select((id, i) => ($"@t{i}", (object?)id)).ToArray();
        var list = string.Join(",", ids.Select((_, i) => $"@t{i}"));
        var jobs = Sql.Query(conn, null, $"""
            SELECT task_id, executor_id, state, started_at, finished_at, created_at
            FROM jobs WHERE deleted_at IS NULL AND task_id IN ({list})
            ORDER BY created_at
            """, r => (
                Task: r.S("task_id"),
                Executor: r.SN("executor_id") ?? "",
                State: r.S("state"),
                Started: r.DtN("started_at") ?? r.Dt("created_at"),
                Finished: r.DtN("finished_at")), args);

        var executors = Sql.Query(conn, null,
                "SELECT id, nick, kind FROM executors WHERE deleted_at IS NULL",
                r => (Id: r.S("id"), Nick: r.S("nick"), Kind: r.S("kind")))
            .ToDictionary(e => e.Id, e => (e.Nick, e.Kind), StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        foreach (var task in ordered)
        {
            var taskJobs = jobs.Where(j => j.Task == task.Id).ToList();
            var active = taskJobs.Any(j => j.State is "queued" or "running" or "waiting_human");
            var spent = taskJobs.Sum(j =>
                Math.Max(((j.Finished ?? now) - j.Started).TotalHours, 0));
            var planned = task.PlannedHours is > 0 ? task.PlannedHours.Value : dto.DefaultHours;
            var settled = TaskStatuses.Settled(task.Status);
            var duration = settled && spent > 0 ? spent : Math.Max(planned, active ? spent : 0);
            var lastExecutor = taskJobs.Count > 0 ? taskJobs[^1].Executor : "";
            var lastState = taskJobs.Count > 0 ? taskJobs[^1].State : "";
            // кто РЕАЛЬНО делает или делал задачу (по заданию): последнее задание, иначе
            // отметка о замене исполнителя (T-221), иначе назначенный
            var executorId = (lastExecutor.Length > 0 ? lastExecutor : task.ActualExecutorId)
                ?? task.ExecutorIds.FirstOrDefault() ?? "";
            dto.Nodes.Add(new TaskDiagramNodeDto
            {
                Id = task.Id,
                DisplayId = task.DisplayId,
                Title = task.Title,
                Status = task.Status,
                WorkStatus = !active ? "" :
                    lastState == "waiting_human" ? TaskStatuses.Paused : TaskStatuses.InProgress,
                ParentId = task.ParentId,
                ExecutorId = executorId,
                // когда работа НАЧАЛАСЬ на самом деле (первое задание): по этому времени
                // раскладка ставит уже случившееся в настоящем порядке (T-164-S0)
                StartedAt = taskJobs.Count > 0 ? taskJobs[0].Started : null,
                DurationHours = duration,
                Actual = settled && spent > 0,
                BlockerIds = task.BlockerIds.ToList(),
                // задача вне иерархии (T-135-S0): красная — она и её подзадачи
                Outside = outsideIds.Contains(task.Id),
                IsRoot = task.Id == taskId,
                IsReadOnly = task.IsReadOnly,
                Settled = settled,
                HasActiveJob = active,
                // приоритет нужен раскладке: незаконченные задачи стоят в порядке очереди,
                // и взятая в работу обгоняет ждущую только среди равных ей (T-192-S0)
                PriorityNum = task.PriorityNum,
            });
        }
        TaskDiagram.Layout(dto.Nodes);

        // дорожки: только занятые исполнители (по заданию — незанятых не выводим).
        // Порядок — иерархия команды задачи: тимлид сверху, дальше по подчинению
        var members = root.TeamId is { Length: > 0 } teamId
            ? Sql.Query(conn, null, """
                SELECT executor_id, parent_executor_id, is_lead FROM team_members WHERE team_id=@t
                """, r => (Id: r.S("executor_id"), Parent: r.SN("parent_executor_id"),
                    Lead: r.B("is_lead")), ("@t", teamId))
                .ToDictionary(m => m.Id, StringComparer.Ordinal)
            : new Dictionary<string, (string Id, string? Parent, bool Lead)>(StringComparer.Ordinal);
        int LevelOf(string executorId)
        {
            var level = 0;
            var current = executorId;
            while (members.TryGetValue(current, out var member) && member.Parent is { Length: > 0 } parent
                   && level < 16)
            {
                level++;
                current = parent;
            }
            // не участник команды задачи — под всеми, кто в ней состоит
            return members.ContainsKey(executorId) ? level : 100;
        }
        dto.Lanes = dto.Nodes
            .Select(n => n.ExecutorId).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal)
            .Select(id => new TaskDiagramLaneDto
            {
                ExecutorId = id,
                Nick = executors.TryGetValue(id, out var e) ? e.Nick : id,
                Kind = executors.TryGetValue(id, out var kind) ? kind.Kind : "human",
                IsLead = members.TryGetValue(id, out var member) && member.Lead,
                Level = LevelOf(id),
            })
            .OrderByDescending(l => l.IsLead) // тимлид всегда наверху (по заданию)
            .ThenBy(l => l.Level)
            .ThenBy(l => l.Nick, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        dto.Running = HasLaunchFlag(root.LaunchJson, HierarchyFlag)
                      || dto.Nodes.Any(n => n.HasActiveJob);
        return dto;
    }

    /// <summary>Прямые потомки задачи (для автозапуска, todo22).</summary>
    public List<TaskItem> ListChildren(string parentId)
    {
        using var conn = _db.Open();
        var children = Sql.Query(conn, null,
            "SELECT * FROM tasks WHERE parent_id=@p AND deleted_at IS NULL ORDER BY created_at",
            Map, ("@p", parentId));
        foreach (var child in children)
        {
            LoadLinks(conn, child);
        }
        return children;
    }

    /// <summary>
    /// Ссылка импорта к записи в базу (T-246): пробелы по краям снимаются, пустая строка
    /// становится NULL. Отличать «поле не заполнено» от «в поле пробел» в базе не нужно,
    /// а вот отличать «есть ссылка» от «нет ссылки» приходится в каждом втором месте —
    /// поэтому пусто хранится ровно одним значением.
    /// </summary>
    public static string? NormalizeImportUrl(string? url) =>
        url?.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>
    /// Статус по готовности к записи в базу и обратно (T-250): пробелы по краям снимаются,
    /// пустое значение становится «проверкой». Пусто здесь означает ровно то же, что и
    /// умолчание колонки у задач, заведённых до версии 1.90, — прежнее поведение, при котором
    /// нормально завершённая ИИ-задача уходила на проверку человеку. Хранить это «пусто»
    /// отдельным значением нельзя: тогда каждое чтение обязано было бы помнить про умолчание,
    /// а забытое место молча оставило бы задачу без статуса.
    /// <para>Код НЕ сверяется со справочником состояний намеренно: справочник пополняется
    /// пользователем, а задача может приехать репликацией с сервера, где нужное состояние
    /// уже заведено, а здесь ещё нет (то же правило, что и у самого статуса задачи).</para>
    /// </summary>
    public static string NormalizeAiDoneStatus(string? status) =>
        status?.Trim() is { Length: > 0 } value ? value : TaskStatuses.Review;

    /// <summary>
    /// Статус по готовности задачи (T-250), прочитанный из базы ЗАНОВО. Именно заново:
    /// задание идёт часами, и человек вполне мог поправить поле уже после старта агента —
    /// снимок задачи, с которым работает коннектор, к концу работы устарел. Задачи нет
    /// (удалена по ходу задания) — «проверка», прежнее поведение.
    /// </summary>
    public string AiDoneStatusOf(string taskId)
    {
        using var conn = _db.Open();
        return NormalizeAiDoneStatus(Sql.Scalar<string>(conn, null,
            "SELECT ai_done_status FROM tasks WHERE id=@id", ("@id", taskId)));
    }

    /// <summary>
    /// Поставить задаче СТАТУС ПО ГОТОВНОСТИ (T-250) одним полем — не переписывая запись
    /// целиком (T-31-S0).
    ///
    /// <para>Отдельный метод, а не <see cref="Update"/>, по той же причине, что у режима
    /// запуска и отложенного старта: полная перезапись затирает всё, что человек поправил
    /// в форме, пока шла работа (наука T-274, T-12-S1), и требует прав на правку формы.
    /// Нужен перезапуску задачи для повторной проверки: для блокирующей «завершена»
    /// означает только «готово» (T-6-S1), поэтому перезапущенная задача, оставленная
    /// с «проверкой», встала бы в проверку и ждущий её прогон не проснулся бы никогда.</para>
    /// </summary>
    public void SetAiDoneStatus(string taskId, string? status)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null,
            "UPDATE tasks SET ai_done_status=@s, updated_at=@now WHERE id=@id",
            ("@s", NormalizeAiDoneStatus(status)), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
    }

    /// <summary>
    /// «Время ↔ качество» задачи (T-23-S0) к записи в базу: значение приводится к отрезку
    /// 0..1, а «пусто» остаётся пустым — это не ноль, а «взять умолчание проекта».
    /// </summary>
    public static double? NormalizeTimeQuality(double? value) =>
        value is { } number && !double.IsNaN(number) ? Math.Clamp(number, 0.0, 1.0) : null;

    /// <summary>
    /// ВРЕМЯ ↔ КАЧЕСТВО задачи (T-23-S0), прочитанное из базы ЗАНОВО и уже с подстановкой
    /// умолчаний: пусто у задачи — умолчание её проекта, пусто и там (или задачи вовсе нет,
    /// или проекта нет) — <see cref="ProjectSettings.TimeQualityDefault"/>.
    /// <para>Заново — по той же причине, что и у <see cref="AiDoneStatusOf"/>: задание идёт
    /// часами, и человек мог поправить поле уже после старта агента.</para>
    /// </summary>
    public double TimeQualityOf(string taskId)
    {
        using var conn = _db.Open();
        var own = Sql.Scalar<double?>(conn, null,
            "SELECT time_quality FROM tasks WHERE id=@id", ("@id", taskId));
        if (NormalizeTimeQuality(own) is { } value)
        {
            return value;
        }
        var settings = Sql.Scalar<string>(conn, null, """
            SELECT p.settings_json FROM tasks t JOIN projects p ON p.id = t.project_id WHERE t.id=@id
            """, ("@id", taskId));
        return ProjectSettings.TimeQuality(settings);
    }

    /// <summary>
    /// Адрес карточки Trello в тексте описания (T-246): импорт всегда дописывает в описание
    /// строку «Импортировано из Trello: доска «…», [карточка](https://trello.com/c/…)»,
    /// и это единственное место, где у уже импортированных задач сохранился ЧЕЛОВЕЧЕСКИЙ
    /// адрес источника — внешний ключ (external_ref) хранит только id карточки.
    /// Пусто — адреса в тексте нет.
    /// </summary>
    public static string? TrelloUrlInText(string text) =>
        System.Text.RegularExpressions.Regex.Match(text,
                @"https?://(?:www\.)?trello\.com/c/[A-Za-z0-9]+") is { Success: true } m
            ? m.Value
            : null;

    /// <summary>
    /// Проставить ссылку импорта уже импортированным задачам (T-246, шаг обновления билда 89).
    /// Задачи, приехавшие из Trello до этой версии, знают только внутренний ключ карточки —
    /// кнопке «обновить из источника» этого мало. Адрес берётся из описания, куда импорт
    /// его и дописал. Шаг ИДЕМПОТЕНТЕН: задачи с уже заполненной ссылкой не трогаются,
    /// и при повторном старте находить нечего. Возвращает число проставленных ссылок.
    /// </summary>
    public int FillImportUrlsFromDescriptions()
    {
        using var conn = _db.Open();
        var candidates = Sql.Query(conn, null, """
            SELECT * FROM tasks
            WHERE external_ref LIKE 'trello:%' AND deleted_at IS NULL
              AND (import_url IS NULL OR import_url = '')
            """, Map);
        var filled = 0;
        foreach (var task in candidates)
        {
            if (TrelloUrlInText(ReadDescription(task)) is not { } url)
            {
                continue;
            }
            Sql.Exec(conn, null, "UPDATE tasks SET import_url=@url, updated_at=@now WHERE id=@id",
                ("@url", url), ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", task.Id));
            filled++;
        }
        return filled;
    }

    /// <summary>Задача проекта по внешнему id импорта (ТЗ v1.28, todo30): защита от дублей.</summary>
    public TaskItem? FindByExternalRef(string projectId, string externalRef)
    {
        using var conn = _db.Open();
        var task = Sql.Query(conn, null,
                "SELECT * FROM tasks WHERE project_id=@p AND external_ref=@r AND deleted_at IS NULL",
                Map, ("@p", projectId), ("@r", externalRef))
            .FirstOrDefault();
        if (task is not null)
        {
            LoadLinks(conn, task);
        }
        return task;
    }

    /// <summary>Мягкое удаление: deleted_at + перенос файлов в .trash (ТЗ пп. 6.4.2, 6.4.4).
    /// Каскад (ТЗ v1.28, todo30): вставленные в .md и чат задачи файлы uploads/ тоже уходят
    /// в корзину, если на них не ссылаются другие живые задачи проекта.</summary>
    public void Delete(string taskId, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();

        var task = Sql.Query(conn, tx, "SELECT * FROM tasks WHERE id=@id", Map, ("@id", taskId)).FirstOrDefault();
        if (task is null || task.DeletedAt is not null)
        {
            return;
        }
        // удаление — тоже запись в строку, значит только на сервере-владельце (ТЗ гл. 6)
        EnsureCanWrite(task);

        // ссылки на файлы uploads/ читаются ДО переноса каталога задачи в корзину
        var orphanUploads = OrphanUploads(conn, tx, task);

        Sql.Exec(conn, tx, "UPDATE tasks SET deleted_at=@deleted, updated_at=@deleted WHERE id=@id",
            ("@deleted", Sql.ToDb(now)), ("@id", taskId));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskDeleted,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new { task.DisplayId, task.Title }),
        });

        var slug = ProjectSlug(conn, task.ProjectId, tx);
        tx.Commit();
        _files.TrashTaskDir(slug, task.DisplayId);
        _files.TrashUploads(slug, orphanUploads);
    }

    /// <summary>Файлы uploads/, вставленные в .md (описание, приёмка) и чат удаляемой задачи
    /// (MdLinks.GetAllLinks, todo30), НЕ используемые другими живыми задачами проекта —
    /// шаблоны и копии из шаблонов ссылаются на те же файлы, их трогать нельзя.</summary>
    private List<string> OrphanUploads(SqliteConnection conn, SqliteTransaction tx, TaskItem task)
    {
        var candidates = MdLinks.UploadPathsOf(TaskLinks(conn, tx, task));
        if (candidates.Count == 0)
        {
            return [];
        }
        var others = Sql.Query(conn, tx,
            "SELECT * FROM tasks WHERE deleted_at IS NULL AND id<>@id", Map, ("@id", task.Id));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in others)
        {
            used.UnionWith(MdLinks.UploadPathsOf(TaskLinks(conn, tx, other)));
        }
        return candidates.Where(c => !used.Contains(c)).ToList();
    }

    /// <summary>Все ссылки из текстов задачи: описание + критерии приёмки + сообщения чата.</summary>
    private List<string> TaskLinks(SqliteConnection conn, SqliteTransaction tx, TaskItem task)
    {
        var links = new List<string>();
        links.AddRange(MdLinks.GetAllLinks(ReadDescription(task)));
        links.AddRange(MdLinks.GetAllLinks(ReadAcceptance(task)));
        var chatTexts = Sql.Query(conn, tx,
            "SELECT text FROM chat_messages WHERE task_id=@id AND deleted_at IS NULL",
            r => r.S("text"), ("@id", task.Id));
        foreach (var text in chatTexts)
        {
            links.AddRange(MdLinks.GetAllLinks(text));
        }
        return links;
    }

    /// <summary>Проверки ядра (ТЗ пп. 2.1, 6.4.2); у шаблонов поля могут быть пустыми (п. 2.3).</summary>
    private void Validate(SqliteConnection conn, SqliteTransaction tx, TaskItem task,
        bool isNew = false)
    {
        EnsureExecutorsOnSameServer(conn, tx, task);
        if (task.Kind == TaskKind.Task && task.ExecutorIds.Count > 1)
        {
            throw new ArgumentException(Loc.T("msg.task.21"));
        }
        // блокирующие задачи (ТЗ v1.35): существуют, не сама задача; у шаблона — только
        // узлы шаблонов (при копировании заменяются на созданные задачи), у задачи — задачи
        foreach (var blockerId in task.BlockerIds)
        {
            if (blockerId == task.Id)
            {
                throw new ArgumentException(Loc.T("msg.task.22"));
            }
            var isTemplate = Sql.Scalar<long?>(conn, tx,
                "SELECT is_template FROM tasks WHERE id=@id AND deleted_at IS NULL", ("@id", blockerId));
            if (isTemplate is null)
            {
                throw new ArgumentException(Loc.T("msg.task.23", blockerId));
            }
            if ((isTemplate == 1) != task.IsTemplate)
            {
                throw new ArgumentException(task.IsTemplate
                    ? Loc.T("msg.task.24")
                    : Loc.T("msg.task.25"));
            }
        }
        if (task.IsTemplate)
        {
            return;
        }

        // неактивные проект/команда/исполнители не подставляются в новые задачи (ТЗ пп. 2.2, 2.7, 2.8)
        if (isNew)
        {
            // активность проекта — ПЕР-СЕРВЕРНАЯ (ТЗ гл. 6, этап 42): она зависит от каталога,
            // а каталоги на серверах разные
            if (task.ProjectId is not null && !IsProjectActiveHere(conn, tx, task.ProjectId))
            {
                throw new ArgumentException(Loc.T("msg.task.26"));
            }
            if (task.TeamId is not null && !IsRowActive(conn, tx, "teams", task.TeamId))
            {
                throw new ArgumentException(Loc.T("msg.task.27"));
            }
            // запасные исполнители (T-221) — по тем же правилам: выключенного нет смысла
            // держать в списке замены, он всё равно не возьмёт работу
            foreach (var executorId in task.ExecutorIds.Concat(task.AltExecutorIds).Concat(
                         task.ResponsibleId is null ? Array.Empty<string>() : new[] { task.ResponsibleId }))
            {
                if (!IsRowActive(conn, tx, "executors", executorId))
                {
                    throw new ArgumentException(Loc.T("msg.task.28"));
                }
            }
        }

        if (task.TeamId is not null)
        {
            // и основной, и запасные (T-221) — участники команды задачи (ТЗ п. 2.1)
            foreach (var executorId in task.ExecutorIds.Concat(task.AltExecutorIds))
            {
                var isMember = Sql.Scalar<long>(conn, tx,
                    "SELECT COUNT(*) FROM team_members WHERE team_id=@t AND executor_id=@e",
                    ("@t", task.TeamId), ("@e", executorId)) > 0;
                if (!isMember)
                {
                    throw new ArgumentException(Loc.T("msg.task.29"));
                }
            }
        }
        if (task.ResponsibleId is not null)
        {
            var kind = Sql.Scalar<string>(conn, tx,
                "SELECT kind FROM executors WHERE id=@id", ("@id", task.ResponsibleId));
            if (kind != "human")
            {
                throw new ArgumentException(Loc.T("msg.task.30"));
            }
        }
    }

    /// <summary>
    /// ИИ-исполнитель с ЛОКАЛЬНОЙ моделью привязан к серверу физически: модель, её файлы и
    /// команда запуска лежат на конкретном компьютере (ТЗ гл. 6, этап 42). Назначать такого
    /// исполнителя задаче ДРУГОГО сервера бессмысленно — задача назначилась бы и не
    /// запустилась. Отвечаем понятной ошибкой вместо тихого сбоя при запуске.
    /// </summary>
    private void EnsureExecutorsOnSameServer(SqliteConnection conn, SqliteTransaction? tx, TaskItem task)
    {
        // запасные (T-221) проверяются наравне с основным: заменить занятого исполнителя
        // сможет только тот, кто физически способен взять работу на этом сервере
        if (task.IsTemplate || (task.ExecutorIds.Count == 0 && task.AltExecutorIds.Count == 0))
        {
            return; // у шаблона сервера нет: он получит его при создании задачи
        }
        // задача без сервера выполняется на ДИРИЖЁРЕ (правит её только он), поэтому для
        // сравнения владельцем считается он же — иначе локальная модель самого дирижёра
        // не назначалась бы на его собственные задачи
        var owner = task.ServerId is { Length: > 0 } explicitOwner
            ? explicitOwner
            : _scope.IsConductor ? _scope.ServerId : null;
        foreach (var executorId in task.ExecutorIds.Concat(task.AltExecutorIds))
        {
            var executorServer = Sql.Scalar<string>(conn, tx,
                "SELECT server_id FROM executors WHERE id=@id", ("@id", executorId));
            if (executorServer is not { Length: > 0 } || executorServer == owner)
            {
                continue;
            }
            var nick = Sql.Scalar<string>(conn, tx,
                "SELECT nick FROM executors WHERE id=@id", ("@id", executorId)) ?? executorId;
            throw new ArgumentException(
                Loc.T("msg.task.31", nick, _scope.Describe(executorServer)));
        }
    }

    private void AppendStatusChanged(SqliteConnection conn, SqliteTransaction tx, TaskItem task,
        string oldStatus, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskStatusChanged,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                task.DisplayId,
                from = oldStatus,
                to = task.Status,
            }),
        });

    private void AppendAssigned(SqliteConnection conn, SqliteTransaction tx, TaskItem task, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = EventTypes.TaskAssigned,
            EntityType = "task",
            EntityId = task.Id,
            PayloadJson = JsonSerializer.Serialize(new { task.DisplayId, executors = task.ExecutorIds }),
        });

    /// <summary>
    /// Привести список «могут заменить исполнителя» к виду, в котором он ложится в базу
    /// (T-221): повторы снимаются, ОСНОВНОЙ исполнитель из списка выбрасывается. Второе —
    /// не косметика: замена ищется среди запасных, и основной, попавший туда же, дал бы
    /// «заменил сам себя» и лишнюю строку в объяснении, кто взял работу.
    /// </summary>
    private static List<string> NormalizeAlt(IEnumerable<string> altIds, IReadOnlyCollection<string> executorIds)
    {
        var seen = new HashSet<string>(executorIds, StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var id in altIds)
        {
            if (id is { Length: > 0 } && seen.Add(id))
            {
                result.Add(id);
            }
        }
        return result;
    }

    private static void SaveLinks(SqliteConnection conn, SqliteTransaction tx, TaskItem task)
    {
        foreach (var executorId in task.ExecutorIds)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_executors (task_id, executor_id) VALUES (@t, @e)",
                ("@t", task.Id), ("@e", executorId));
        }
        // «могут заменить исполнителя» (T-221): порядок в списке — порядок предпочтения,
        // поэтому он и хранится (ord), а не восстанавливается сортировкой по нику
        task.AltExecutorIds = NormalizeAlt(task.AltExecutorIds, task.ExecutorIds);
        for (var i = 0; i < task.AltExecutorIds.Count; i++)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_alt_executors (task_id, executor_id, ord) VALUES (@t, @e, @o)",
                ("@t", task.Id), ("@e", task.AltExecutorIds[i]), ("@o", i));
        }
        foreach (var skillId in task.SkillIds)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_skills (task_id, skill_id) VALUES (@t, @s)",
                ("@t", task.Id), ("@s", skillId));
        }
        foreach (var blockerId in task.BlockerIds)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_blockers (task_id, blocker_task_id) VALUES (@t, @b)",
                ("@t", task.Id), ("@b", blockerId));
        }
        // тэги (T-222): в базу уходит уже приведённый список — пустые отброшены, повторы сняты
        task.Tags = TaskTags.Normalize(task.Tags);
        foreach (var tag in task.Tags)
        {
            Sql.Exec(conn, tx, "INSERT OR IGNORE INTO task_tags (task_id, tag) VALUES (@t, @g)",
                ("@t", task.Id), ("@g", tag));
        }
    }

    /// <summary>
    /// Все тэги организации (T-222) — то, что предлагается в форме задачи и в фильтре.
    /// Справочника у тэгов нет: набор — это DISTINCT по task_tags живых задач, поэтому тэг,
    /// снятый с последней задачи, из списка исчезает сам. Шаблоны считаются наравне с задачами:
    /// тэг, заведённый на узле шаблона, должен предлагаться и созданной из него задаче.
    /// </summary>
    public List<string> ListTags()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT DISTINCT g.tag FROM task_tags g
            JOIN tasks t ON t.id = g.task_id
            WHERE t.deleted_at IS NULL
            """, r => r.S("tag"))
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void LoadLinks(SqliteConnection conn, TaskItem task, SqliteTransaction? tx = null)
    {
        task.ExecutorIds = Sql.Query(conn, tx,
            "SELECT executor_id FROM task_executors WHERE task_id=@id", r => r.S("executor_id"), ("@id", task.Id));
        // запасные исполнители (T-221) читаются В ПОРЯДКЕ ПРЕДПОЧТЕНИЯ: первым берётся
        // первый свободный, поэтому порядок здесь — часть смысла, а не оформление
        task.AltExecutorIds = Sql.Query(conn, tx,
            "SELECT executor_id FROM task_alt_executors WHERE task_id=@id ORDER BY ord, executor_id",
            r => r.S("executor_id"), ("@id", task.Id));
        task.SkillIds = Sql.Query(conn, tx,
            "SELECT skill_id FROM task_skills WHERE task_id=@id", r => r.S("skill_id"), ("@id", task.Id));
        task.BlockerIds = Sql.Query(conn, tx,
            "SELECT blocker_task_id FROM task_blockers WHERE task_id=@id",
            r => r.S("blocker_task_id"), ("@id", task.Id));
        // тэги (T-222): порядок один и тот же при каждом чтении — иначе список в форме
        // и в карточке «дрожал» бы между открытиями
        task.Tags = Sql.Query(conn, tx,
            "SELECT tag FROM task_tags WHERE task_id=@id ORDER BY tag", r => r.S("tag"), ("@id", task.Id));
    }

    /// <summary>
    /// Состояние блокирующих задач (ТЗ п. 2.12, T-6-S1): готовы / ждём / одна из них отменена.
    /// Удалённая блокирующая держать задачу не может — она считается пройденной.
    /// <para>ДО версии 1.88 отменённая блокирующая считалась завершённой и отпускала ждущие
    /// задачи. Теперь отмена — отдельный исход: работа по этой ветке не состоится, значит
    /// ждущие задачи запускать не за чем (<see cref="BlockersState.Cancelled"/>).</para>
    /// </summary>
    public BlockersState BlockersStateOf(TaskItem task)
    {
        if (task.BlockerIds.Count == 0)
        {
            return BlockersState.Done;
        }
        using var conn = _db.Open();
        var waiting = false;
        foreach (var blockerId in task.BlockerIds)
        {
            var status = Sql.Scalar<string>(conn, null,
                "SELECT status FROM tasks WHERE id=@id AND deleted_at IS NULL", ("@id", blockerId));
            if (status == TaskStatuses.Cancelled)
            {
                // отмена сильнее ожидания: сколько бы блокирующих ни осталось в работе,
                // запускать ждущую задачу автоматически уже не будут
                return BlockersState.Cancelled;
            }
            if (status is not (null or TaskStatuses.Done))
            {
                waiting = true;
            }
        }
        return waiting ? BlockersState.Waiting : BlockersState.Done;
    }

    /// <summary>
    /// ПРОПИСАТЬ ЗАДАЧЕ ОДНУ БЛОКИРУЮЩУЮ (T-31-S0) — не переписывая запись целиком.
    /// Возвращает true, если связь появилась; false — она уже была, задача сама себе
    /// блокирующей не бывает, либо одной из задач нет.
    ///
    /// <para>Отдельным запросом по той же причине, что и «статус при завершении»: полная
    /// перезапись задачи затирает правки человека, сделанные пока идёт задание. Связь
    /// реплицируется сама — журнал изменений ведут триггеры (<c>task_blockers</c> входит
    /// в <c>ChangeLog.OrgTables</c>).</para>
    ///
    /// <para>Прямая петля (A ждёт A) отсекается здесь; более длинных колец не проверяем —
    /// как и форма задачи: ожидание по кругу видно человеку в карточке, а обходы дерева
    /// защищены пределом глубины.</para>
    /// </summary>
    public bool AddBlocker(string taskId, string blockerTaskId)
    {
        if (string.Equals(taskId, blockerTaskId, StringComparison.Ordinal))
        {
            return false;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var exists = Sql.Scalar<long?>(conn, tx, """
            SELECT COUNT(*) FROM tasks WHERE id IN (@t, @b) AND deleted_at IS NULL
            """, ("@t", taskId), ("@b", blockerTaskId));
        if (exists is not 2)
        {
            return false;
        }
        var had = Sql.Scalar<long?>(conn, tx, """
            SELECT COUNT(*) FROM task_blockers WHERE task_id=@t AND blocker_task_id=@b
            """, ("@t", taskId), ("@b", blockerTaskId)) > 0;
        if (!had)
        {
            Sql.Exec(conn, tx,
                "INSERT OR IGNORE INTO task_blockers (task_id, blocker_task_id) VALUES (@t, @b)",
                ("@t", taskId), ("@b", blockerTaskId));
            Sql.Exec(conn, tx, "UPDATE tasks SET updated_at=@now WHERE id=@id",
                ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", taskId));
        }
        tx.Commit();
        return !had;
    }

    /// <summary>Все блокирующие задачи переведены в «готово» — задачу можно запускать
    /// автоматически (ТЗ п. 2.12; с T-6-S1 отменённая блокирующая запуск НЕ отпускает).</summary>
    public bool BlockersDone(TaskItem task) => BlockersStateOf(task) == BlockersState.Done;

    /// <summary>Хотя бы одна блокирующая задача отменена (T-6-S1): автоматических запусков
    /// по этой задаче больше не будет, а открытая очередь иерархии останавливается.</summary>
    public bool BlockersCancelled(TaskItem task) => BlockersStateOf(task) == BlockersState.Cancelled;

    /// <summary>Задачи, у которых указанная — в списке блокирующих (для автозапуска
    /// после её завершения, ТЗ v1.35).</summary>
    public List<TaskItem> ListBlockedBy(string blockerTaskId)
    {
        using var conn = _db.Open();
        var tasks = Sql.Query(conn, null, """
            SELECT t.* FROM tasks t
            JOIN task_blockers b ON b.task_id = t.id
            WHERE b.blocker_task_id=@id AND t.deleted_at IS NULL
            """, Map, ("@id", blockerTaskId));
        foreach (var task in tasks)
        {
            LoadLinks(conn, task);
        }
        return tasks;
    }

    /// <summary>Проект активен НА ЭТОМ СЕРВЕРЕ (ТЗ гл. 6, этап 42): пер-серверная строка,
    /// а если её ещё нет (проект приехал репликой) — общий признак самой строки проекта.</summary>
    private bool IsProjectActiveHere(SqliteConnection conn, SqliteTransaction? tx, string projectId)
    {
        var here = Sql.Scalar<long?>(conn, tx, """
            SELECT is_active FROM project_servers
            WHERE project_id=@p AND server_id=@s AND deleted_at IS NULL
            """, ("@p", projectId), ("@s", _scope.ServerId));
        return here is not null
            ? here == 1
            : IsRowActive(conn, tx, "projects", projectId);
    }

    private static bool IsRowActive(SqliteConnection conn, SqliteTransaction? tx, string table, string id) =>
        Sql.Scalar<long?>(conn, tx,
            $"SELECT is_active FROM {table} WHERE id=@id AND deleted_at IS NULL", ("@id", id)) == 1;

    /// <summary>Слаг проекта для файловых путей; null — задача без проекта (шаблон).</summary>
    private static string? ProjectSlug(SqliteConnection conn, string? projectId, SqliteTransaction? tx = null) =>
        projectId is null
            ? null
            : Sql.Scalar<string>(conn, tx, "SELECT slug FROM projects WHERE id=@id", ("@id", projectId));

    private static TaskItem Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        ProjectId = r.SN("project_id"),
        TeamId = r.SN("team_id"),
        ParentId = r.SN("parent_id"),
        Kind = EnumMap.TaskKindFromDb(r.S("kind")),
        Title = r.S("title"),
        DescriptionPath = r.S("description_path"),
        Status = r.S("status"),
        // статус по готовности (T-250): куда ИИ-агент переводит задачу, нормально её завершив.
        // r.Has — выборки, собранные не из tasks.* (их немного), колонки не привозят
        AiDoneStatus = r.Has("ai_done_status")
            ? NormalizeAiDoneStatus(r.SN("ai_done_status"))
            : TaskStatuses.Review,
        // секция «оптимизация» (T-23-S0): вставлять ли в промпт задания родителя и соседей
        // и насколько тщательно делать работу. r.Has — по той же причине, что и выше
        ParentInPrompt = r.Has("parent_in_prompt") && r.B("parent_in_prompt"),
        SiblingsInPrompt = r.Has("siblings_in_prompt") && r.B("siblings_in_prompt"),
        TimeQuality = r.Has("time_quality") && !r.IsDBNull(r.GetOrdinal("time_quality"))
            ? r.D("time_quality")
            : null,
        Priority = (int)r.L("priority"),
        PriorityNum = (int)r.L("priority_num"),
        IsNotSplit = r.B("is_not_split"),
        DueDate = r.DtN("due_date"),
        PlannedHours = r.IsDBNull(r.GetOrdinal("planned_hours")) ? null : r.D("planned_hours"),
        ResponsibleId = r.SN("responsible_id"),
        LaunchJson = r.S("launch_json"),
        AcceptancePath = r.S("acceptance_path"),
        IsTemplate = r.B("is_template"),
        TemplateId = r.SN("template_id"),
        ExternalRef = r.Has("external_ref") ? r.SN("external_ref") : null,
        // ссылка импорта — откуда задача приехала (T-246); по ней работает обновление из
        // источника. Пусто — задача заведена в системе
        ImportUrl = r.Has("import_url") ? r.SN("import_url") : null,
        // кто реально ведёт задачу сейчас (T-221): при замене занятого исполнителя запасным
        // основной не меняется, поэтому фактический хранится отдельной колонкой
        ActualExecutorId = r.Has("actual_executor_id") ? r.SN("actual_executor_id") : null,
        // сервер-владелец (ТЗ гл. 6, этап 42); код, имя и «только чтение» подставляет Decorate
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
        // вычисляемое: есть только в выборках списков/карточки (SELECT t.*, … AS pending_questions)
        PendingQuestions = r.Has("pending_questions") ? (int)r.L("pending_questions") : 0,
    };
}
