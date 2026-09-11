using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>Итог переноса (или восстановления): что и куда уехало (T-42-S0).</summary>
/// <param name="Target">Вид переноса (<see cref="ArchiveMoveTargets"/>).</param>
/// <param name="Id">Корневая запись, которую попросили перенести.</param>
/// <param name="Restore">true — обратный перенос, из архива в рабочую среду.</param>
/// <param name="ArchiveId">Архив, с которым шёл обмен (всегда ТЕКУЩИЙ).</param>
/// <param name="ArchiveCode">Его код — им же помечены переписанные ссылки.</param>
/// <param name="Rows">Сколько строк базы перенесено.</param>
/// <param name="FilesMoved">Файлов ПЕРЕНЕСЕНО (ссылка на них была единственной).</param>
/// <param name="FilesCopied">Файлов СКОПИРОВАНО (на них ссылается кто-то ещё).</param>
/// <param name="Tasks">Задачи и узлы шаблонов, попавшие в перенос.</param>
/// <param name="Objects">Объекты проектов.</param>
/// <param name="Experience">Записи опыта.</param>
/// <param name="Projects">Проекты (у переноса проекта целиком).</param>
public sealed record ArchiveTransferResult(string Target, string Id, bool Restore,
    string ArchiveId, string ArchiveCode, int Rows, int FilesMoved, int FilesCopied,
    IReadOnlyList<string> Tasks, IReadOnlyList<string> Objects,
    IReadOnlyList<string> Experience, IReadOnlyList<string> Projects);

/// <summary>
/// ПЕРЕНОС ДАННЫХ В АРХИВ И ВОССТАНОВЛЕНИЕ ОБРАТНО (T-42-S0, выпуск 1.105).
///
/// Это рабочая механика; формы, которые её зовут, — соседние задачи выпуска (ручная
/// архивация T-44-S0, автоматическая T-46-S0, просмотр архива T-45-S0). Опирается на ядро
/// архивации (T-40-S0): каталог <c>arc/&lt;код&gt;/</c> со своей базой ТОЙ ЖЕ схемы, что
/// у организации, и подкаталогом файлов проектов.
///
/// ТРИ РЕШЕНИЯ, НА КОТОРЫХ ВСЁ ДЕРЖИТСЯ.
/// <list type="number">
/// <item><b>Обе базы — одна схема, поэтому перенос идёт SQL-ом через ATTACH</b>, а не чтением
/// сущностей в объекты и записью их сервисами. Сервисы по дороге проставили бы новые даты,
/// новые номера и новые события — а архив обязан хранить то же самое, что лежало в работе,
/// вплоть до идентификаторов: только так восстановление возвращает ровно то, что уехало,
/// и только так ссылки между записями остаются целыми.</item>
/// <item><b>Справочники в архив КОПИРУЮТСЯ целиком</b> (проекты, исполнители, команды,
/// навыки, состояния задач, модели). Без них архивная задача сослалась бы на исполнителя,
/// которого в архивной базе нет, и внешний ключ не дал бы её записать; а с ними архив
/// самодостаточен и читается готовыми страницами. В ОБРАТНУЮ сторону справочники едут
/// только <c>INSERT OR IGNORE</c>: живой справочник рабочей среды перезаписывать архивной
/// копией нельзя — она старая.</item>
/// <item><b>Файл переносится, если ссылка на него единственная, и копируется, если нет</b> —
/// общее правило задания. Считается это по описи ссылок (<see cref="ScanRefs"/>): у каждого
/// файла собираются ВСЕ владельцы (задача, объект, запись опыта), и файл уезжает насовсем
/// ровно тогда, когда все его владельцы уезжают тоже.</item>
/// </list>
/// </summary>
public sealed class ArchiveTransferService
{
    private readonly Database _db;
    private readonly ArchiveService _archives;
    private readonly EventStore _events;

    /// <summary>Псевдоним присоединённой базы архива в SQL.</summary>
    private const string Arc = "arc";

    /// <summary>Псевдоним базы рабочей среды.</summary>
    private const string Main = "main";

    public ArchiveTransferService(Database db, ArchiveService archives, EventStore events)
    {
        _db = db;
        _archives = archives;
        _events = events;
    }

    // --- то, ради чего сервис есть ---

    /// <summary>ПЕРЕНЕСТИ данные в текущий архив.</summary>
    /// <param name="target">Вид переноса (<see cref="ArchiveMoveTargets"/>).</param>
    /// <param name="id">Идентификатор записи: задачи, узла шаблона, объекта, опыта, проекта.</param>
    public ArchiveTransferResult Move(string target, string id, string? actorId) =>
        Transfer(target, id, restore: false, actorId);

    /// <summary>ВОССТАНОВИТЬ данные из текущего архива обратно в рабочую среду. Состав тот же,
    /// что переносился, включая файлы и обратную правку ссылок.</summary>
    public ArchiveTransferResult Restore(string target, string id, string? actorId) =>
        Transfer(target, id, restore: true, actorId);

    /// <summary>
    /// ЧТО УЕДЕТ — без переноса: те же проверки и тот же состав, только считанный. Этим
    /// формы соседей показывают человеку список перед нажатием кнопки, а автоматическая
    /// архивация — сверяет отбор правил со своими возможностями.
    /// </summary>
    public ArchiveTransferResult Preview(string target, string id, bool restore = false)
    {
        var archive = CurrentOpen();
        using var conn = OpenBoth(archive.Code);
        var plan = BuildPlan(conn, restore ? Arc : Main, target, id, validate: !restore);
        return Result(target, id, restore, archive, 0, 0, 0, plan);
    }

    // --- перенос ---

    private ArchiveTransferResult Transfer(string target, string id, bool restore, string? actorId)
    {
        if (!ArchiveMoveTargets.IsKnown(target))
        {
            throw new ArgumentException(Loc.T("msg.arcmove.3", target ?? ""));
        }
        var archive = CurrentOpen();
        var from = restore ? Arc : Main;
        var to = restore ? Main : Arc;
        var srcRoot = restore ? _archives.DirOf(archive.Code) : _db.DataDir;
        var dstRoot = restore ? _db.DataDir : _archives.DirOf(archive.Code);

        using var conn = OpenBoth(archive.Code);
        // ПРОВЕРКИ ДЕЛАЮТСЯ ТОЛЬКО ПРИ ПЕРЕНОСЕ В АРХИВ: в архиве данные уже не работают,
        // и требовать от них «подходящего состояния» было бы бессмысленно
        var plan = BuildPlan(conn, from, target, id, validate: !restore);

        // ФАЙЛЫ СЧИТАЮТСЯ ДО ПРАВКИ БАЗЫ: опись ссылок строится по исходной базе, а после
        // удаления строк считать по ней уже нечего
        var files = PlanFiles(conn, from, srcRoot, plan);

        var rows = 0;
        using (var tx = conn.BeginTransaction())
        {
            // внешние ключи проверяются на КОММИТЕ: строки едут пачками по таблицам, и
            // подзадача законно оказывается в базе раньше своего родителя
            Sql.Exec(conn, tx, "PRAGMA defer_foreign_keys=ON");
            Reference(conn, tx, from, to, restore, plan);
            // задача, ЗАВЕДЁННАЯ ПО уезжающему шаблону, остаётся в работе, и ссылка на узел
            // шаблона у неё станет висячей: снимаем её заранее, иначе внешний ключ не даст
            // удалить сам шаблон
            if (plan.Tasks.Count > 0)
            {
                Sql.Exec(conn, tx, $"UPDATE {from}.tasks SET template_id=NULL WHERE "
                                   + In("template_id", plan.Tasks) + " AND NOT "
                                   + In("id", plan.Tasks));
            }
            foreach (var (table, copyWhere, _) in Tables(plan))
            {
                if (copyWhere.Length > 0)
                {
                    rows += Sql.Exec(conn, tx,
                        $"INSERT OR REPLACE INTO {to}.{table} SELECT * FROM {from}.{table} WHERE {copyWhere}");
                }
            }
            foreach (var (table, _, deleteWhere) in Enumerable.Reverse(Tables(plan)))
            {
                if (deleteWhere.Length > 0)
                {
                    Sql.Exec(conn, tx, $"DELETE FROM {from}.{table} WHERE {deleteWhere}");
                }
            }
            tx.Commit();
        }

        // ССЫЛКИ переписываются УЖЕ В ПРИЁМНИКЕ: при переносе к своим адресам добавляется
        // пометка архива, при восстановлении — снимается
        var code = restore ? null : archive.Code;
        RewriteTexts(conn, to, plan, code);

        var (moved, copied) = MoveFiles(files, srcRoot, dstRoot, code);
        var result = Result(target, id, restore, archive, rows, moved, copied, plan);
        Append(result, actorId);
        return result;
    }

    /// <summary>Текущий архив, распакованный на этом сервере: только с таким можно работать.</summary>
    private Archive CurrentOpen()
    {
        var archive = _archives.Current()
                      ?? throw new InvalidOperationException(Loc.T("msg.arcmove.1"));
        if (_archives.StateOf(archive.Code) != ArchiveStates.Open)
        {
            throw new InvalidOperationException(Loc.T("msg.arcmove.2", archive.DisplayId));
        }
        return archive;
    }

    /// <summary>Соединение с рабочей базой, к которому присоединена база архива.</summary>
    private SqliteConnection OpenBoth(string code)
    {
        var conn = _db.Open();
        try
        {
            Sql.Exec(conn, null, $"ATTACH DATABASE @p AS {Arc}",
                ("@p", _archives.DbFileOf(code)));
        }
        catch
        {
            conn.Dispose();
            throw;
        }
        return conn;
    }

    // --- состав переноса ---

    /// <summary>Что именно едет: задачи (и узлы шаблонов), объекты, записи опыта, проекты.</summary>
    private sealed class Plan
    {
        public List<string> Tasks { get; } = [];
        public List<string> Objects { get; } = [];
        public List<string> Experience { get; } = [];
        public List<string> Projects { get; } = [];
    }

    private static Plan BuildPlan(SqliteConnection conn, string db, string target, string id,
        bool validate)
    {
        var plan = new Plan();
        switch (target)
        {
            case ArchiveMoveTargets.Task:
            case ArchiveMoveTargets.Template:
                var template = target == ArchiveMoveTargets.Template;
                var root = Sql.Query(conn, null,
                    $"SELECT id, display_id, status, parent_id, is_template FROM {db}.tasks "
                    + "WHERE id=@id AND deleted_at IS NULL",
                    r => (Id: r.S("id"), Did: r.S("display_id"), Status: r.S("status"),
                        Parent: r.SN("parent_id"), Template: r.B("is_template")),
                    ("@id", id)).FirstOrDefault();
                if (root.Id is null || root.Template != template)
                {
                    throw new InvalidOperationException(Loc.T("msg.arcmove.4", id));
                }
                // ИЕРАРХИЯ ЦЕЛИКОМ: переносится только задача верхнего уровня — оставить
                // подзадачу без родителя значило бы оставить в работе то, чего не открыть
                if (validate && root.Parent is { Length: > 0 })
                {
                    throw new InvalidOperationException(Loc.T("msg.arcmove.5", root.Did));
                }
                plan.Tasks.AddRange(SubTree(conn, db, id));
                if (validate && !template)
                {
                    CheckStatuses(conn, db, plan.Tasks);
                }
                if (validate && template)
                {
                    CheckSchedules(conn, db, plan.Tasks);
                }
                break;

            case ArchiveMoveTargets.Object:
                if (!Exists(conn, db, "objects", id))
                {
                    throw new InvalidOperationException(Loc.T("msg.arcmove.4", id));
                }
                plan.Objects.AddRange(ObjectTree(conn, db, id));
                break;

            case ArchiveMoveTargets.Experience:
                if (!Exists(conn, db, "experience", id))
                {
                    throw new InvalidOperationException(Loc.T("msg.arcmove.4", id));
                }
                plan.Experience.Add(id);
                break;

            case ArchiveMoveTargets.Project:
                if (!Exists(conn, db, "projects", id))
                {
                    throw new InvalidOperationException(Loc.T("msg.arcmove.4", id));
                }
                plan.Projects.Add(id);
                plan.Tasks.AddRange(Sql.Query(conn, null,
                    $"SELECT id FROM {db}.tasks WHERE project_id=@p", r => r.S("id"), ("@p", id)));
                plan.Objects.AddRange(Sql.Query(conn, null,
                    $"SELECT id FROM {db}.objects WHERE project_id=@p", r => r.S("id"), ("@p", id)));
                if (validate)
                {
                    CheckStatuses(conn, db, plan.Tasks);
                }
                break;

            default:
                throw new ArgumentException(Loc.T("msg.arcmove.3", target));
        }
        // ЗАПИСИ ОПЫТА едут вместе со своим узлом шаблона и вместе со своим проектом —
        // иначе опыт остался бы висеть на записи, которой в рабочей среде уже нет
        if (plan.Tasks.Count > 0 || plan.Projects.Count > 0)
        {
            plan.Experience.AddRange(Sql.Query(conn, null,
                $"SELECT id FROM {db}.experience WHERE {In("template_task_id", plan.Tasks)} "
                + $"OR {In("project_id", plan.Projects)}", r => r.S("id")));
        }
        return plan;
    }

    private static bool Exists(SqliteConnection conn, string db, string table, string id) =>
        Sql.Scalar<long>(conn, null,
            $"SELECT COUNT(*) FROM {db}.{table} WHERE id=@id AND deleted_at IS NULL",
            ("@id", id)) > 0;

    /// <summary>Задача и ВСЕ её потомки на любой глубине.</summary>
    private static List<string> SubTree(SqliteConnection conn, string db, string rootId) =>
        Sql.Query(conn, null, $"""
            WITH RECURSIVE sub(id) AS (
                SELECT id FROM {db}.tasks WHERE id=@root
                UNION ALL
                SELECT t.id FROM {db}.tasks t JOIN sub ON t.parent_id = sub.id
            )
            SELECT id FROM sub
            """, r => r.S("id"), ("@root", rootId));

    /// <summary>Объект и все его дочерние объекты (эталонные кадры, датасеты).</summary>
    private static List<string> ObjectTree(SqliteConnection conn, string db, string rootId) =>
        Sql.Query(conn, null, $"""
            WITH RECURSIVE sub(id) AS (
                SELECT id FROM {db}.objects WHERE id=@root
                UNION ALL
                SELECT o.id FROM {db}.objects o JOIN sub ON o.parent_id = sub.id
            )
            SELECT id FROM sub
            """, r => r.S("id"), ("@root", rootId));

    /// <summary>
    /// СОСТОЯНИЯ ЗАДАЧ: архивировать можно только черновик, проверку, готово и отмену.
    /// Один потомок в работе отменяет перенос ВСЕЙ иерархии (и всего проекта) — про него
    /// и говорится в отказе, иначе человеку негде взять причину.
    /// </summary>
    private static void CheckStatuses(SqliteConnection conn, string db, List<string> taskIds)
    {
        var bad = Sql.Query(conn, null,
            $"SELECT display_id, status FROM {db}.tasks WHERE is_template=0 AND deleted_at IS NULL "
            + $"AND {In("id", taskIds)}",
            r => (Did: r.S("display_id"), Status: r.S("status")))
            .FirstOrDefault(t => !ArchiveMoveTargets.CanArchive(t.Status));
        if (bad.Did is not null)
        {
            throw new InvalidOperationException(Loc.T("msg.arcmove.6", bad.Did, bad.Status));
        }
    }

    /// <summary>
    /// ССЫЛКИ В БУДУЩЕМ РАСПИСАНИИ: шаблон, по которому расписание ещё будет заводить
    /// задачи, уносить нельзя — срабатывание не нашло бы своего узла. Будущим считается
    /// действующее периодическое расписание (оно сработает всегда) и разовое, срок
    /// которого ещё не наступил.
    /// </summary>
    private static void CheckSchedules(SqliteConnection conn, string db, List<string> taskIds)
    {
        var busy = Sql.Query(conn, null,
            $"SELECT display_id FROM {db}.schedules WHERE deleted_at IS NULL AND is_active=1 "
            + $"AND {In("template_task_id", taskIds)} "
            + "AND (kind='periodic' OR (start_at IS NOT NULL AND start_at > @now))",
            r => r.S("display_id"), ("@now", Sql.ToDb(DateTime.UtcNow))).FirstOrDefault();
        if (busy is not null)
        {
            throw new InvalidOperationException(Loc.T("msg.arcmove.7", busy));
        }
    }

    // --- таблицы ---

    /// <summary>
    /// Раскладка «что копировать и что удалять» по таблицам, В ПОРЯДКЕ ЗАВИСИМОСТЕЙ
    /// (родители раньше детей): в этом порядке идёт вставка, в обратном — удаление.
    /// Одной описью, а не двумя десятками запросов: новый вид сопутствующих данных
    /// добавляется одной строкой и не забывается ни в одной из двух сторон.
    /// </summary>
    private static List<(string Table, string Copy, string Delete)> Tables(Plan plan)
    {
        var tasks = In("task_id", plan.Tasks);
        var list = new List<(string, string, string)>
        {
            ("projects", In("id", plan.Projects), In("id", plan.Projects)),
            ("project_servers", In("project_id", plan.Projects), In("project_id", plan.Projects)),
            ("tasks", In("id", plan.Tasks), In("id", plan.Tasks)),
            ("task_executors", tasks, tasks),
            ("task_alt_executors", tasks, tasks),
            ("task_skills", tasks, tasks),
            ("task_tags", tasks, tasks),
            // блокирующая связь уезжает вместе со ЗДАЧЕЙ-ХОЗЯИНОМ, а вот вычищается она
            // с обеих сторон: строка «задача X ждёт уехавшую Y» держала бы X навсегда
            ("task_blockers", In("task_id", plan.Tasks),
                $"({In("task_id", plan.Tasks)} OR {In("blocker_task_id", plan.Tasks)})"),
            ("task_links", In("process_id", plan.Tasks), In("process_id", plan.Tasks)),
            ("cycles", In("process_id", plan.Tasks), In("process_id", plan.Tasks)),
            ("schedules", In("template_task_id", plan.Tasks), In("template_task_id", plan.Tasks)),
            ("jobs", tasks, tasks),
            ("chat_messages", tasks, tasks),
            ("objects", In("id", plan.Objects), In("id", plan.Objects)),
            ("object_tags", In("object_id", plan.Objects), In("object_id", plan.Objects)),
            ("object_loras", In("object_id", plan.Objects), In("object_id", plan.Objects)),
            ("experience", In("id", plan.Experience), In("id", plan.Experience)),
            ("experience_tags", In("experience_id", plan.Experience),
                In("experience_id", plan.Experience)),
            // ЛОГИ едут с проектом (по заданию) и с задачей — иначе журнал ссылался бы
            // на записи, которых в рабочей среде уже нет
            ("events", Or(In("task_id", plan.Tasks), In("project_id", plan.Projects)),
                Or(In("task_id", plan.Tasks), In("project_id", plan.Projects))),
            // ЗАЯВКИ И ОТМЕТКИ — только вычистить: это переписка серверов о том, что надо
            // сделать с задачей, и в архиве ей делать нечего, а внешний ключ на уехавшую
            // задачу не дал бы её удалить
            ("run_requests", "", tasks),
            ("stop_requests", "", tasks),
            ("notification_marks", "", tasks),
        };
        // пустой список идентификаторов даёт «0=1» — такие таблицы просто не трогаем
        return [.. list.Select(e => (e.Item1, Blank(e.Item2), Blank(e.Item3)))];
    }

    /// <summary>Условие «ничего не подходит» — таблицу не трогаем вовсе.</summary>
    private static string Blank(string where) =>
        string.Equals(where, "0=1", StringComparison.Ordinal) ? "" : where;

    /// <summary>Объединение условий, из которого выброшены заведомо пустые.</summary>
    private static string Or(params string[] parts)
    {
        var live = parts.Where(p => !string.Equals(p, "0=1", StringComparison.Ordinal)).ToArray();
        return live.Length == 0 ? "0=1" : "(" + string.Join(" OR ", live) + ")";
    }

    /// <summary>
    /// СПРАВОЧНИКИ, без которых архивная запись не читается и даже не записывается (внешний
    /// ключ на исполнителя, команду, навык, проект). В архив они копируются целиком и с
    /// перезаписью — архив обязан быть самодостаточным. ОБРАТНО — только те строки, которых
    /// в рабочей среде нет: живой справочник перезаписывать архивной копией нельзя.
    /// </summary>
    private static void Reference(SqliteConnection conn, SqliteTransaction tx, string from,
        string to, bool restore, Plan plan)
    {
        string[] tables =
        [
            "roles", "role_texts", "skills", "skill_texts", "io_formats", "io_format_texts",
            "task_statuses", "task_status_texts", "actions", "action_texts",
            "ai_models", "executors", "teams", "team_members",
        ];
        var verb = restore ? "INSERT OR IGNORE" : "INSERT OR REPLACE";
        foreach (var table in tables)
        {
            Sql.Exec(conn, tx, $"{verb} INTO {to}.{table} SELECT * FROM {from}.{table}");
        }
        if (!restore)
        {
            // в архив проекты едут ВСЕ: архивная задача обязана открываться со своим проектом
            Sql.Exec(conn, tx, $"INSERT OR REPLACE INTO {to}.projects SELECT * FROM {from}.projects");
            Sql.Exec(conn, tx,
                $"INSERT OR REPLACE INTO {to}.project_servers SELECT * FROM {from}.project_servers");
            return;
        }
        // ОБРАТНО — только проект возвращаемых записей и только если его в работе уже нет:
        // возвращать разом все архивные проекты никто не просил
        var owners = $"(SELECT project_id FROM {from}.tasks WHERE {In("id", plan.Tasks)} "
                     + $"UNION SELECT project_id FROM {from}.objects WHERE {In("id", plan.Objects)} "
                     + $"UNION SELECT id FROM {from}.projects WHERE {In("id", plan.Projects)})";
        Sql.Exec(conn, tx,
            $"INSERT OR IGNORE INTO {to}.projects SELECT * FROM {from}.projects WHERE id IN {owners}");
        Sql.Exec(conn, tx, $"INSERT OR IGNORE INTO {to}.project_servers "
                           + $"SELECT * FROM {from}.project_servers WHERE project_id IN {owners}");
    }

    /// <summary>Условие «колонка в списке»; пустой список — «0=1» (ничего не подходит).</summary>
    private static string In(string column, IReadOnlyCollection<string> ids) =>
        ids.Count == 0
            ? "0=1"
            : column + " IN (" + string.Join(",",
                ids.Distinct(StringComparer.Ordinal).Select(v => "'" + v.Replace("'", "''") + "'"))
              + ")";

    // --- файлы ---

    /// <summary>Файл, который надо перенести или скопировать; путь — относительно корня.</summary>
    private sealed record PlannedFile(string Path, bool Move);

    /// <summary>
    /// ОПИСЬ ССЫЛОК НА ФАЙЛЫ: путь → все, кто на него ссылается («task:id», «object:id»,
    /// «experience:id»). Строится по ВСЕЙ базе разом, а не по переносимым записям: вопрос
    /// «единственная ли ссылка» иначе не решить.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ScanRefs(SqliteConnection conn, string db,
        string root)
    {
        var refs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var files = new FileStore(root);
        void Add(string? path, string owner)
        {
            var value = (path ?? "").Replace('\\', '/').Trim();
            if (value.Length == 0 || value.Contains("..") || Path.IsPathRooted(value)
                || value.Contains("://"))
            {
                return;
            }
            if (!refs.TryGetValue(value, out var owners))
            {
                refs[value] = owners = new HashSet<string>(StringComparer.Ordinal);
            }
            owners.Add(owner);
        }
        void AddText(string? markdown, string owner)
        {
            foreach (var path in MdLinks.UploadPathsOf(MdLinks.GetAllLinks(markdown ?? "")))
            {
                Add(path, owner);
            }
        }

        var slugs = Sql.Query(conn, null, $"SELECT id, slug FROM {db}.projects",
            r => (Id: r.S("id"), Slug: r.S("slug"))).ToDictionary(p => p.Id, p => p.Slug,
            StringComparer.Ordinal);
        foreach (var task in Sql.Query(conn, null,
                     $"SELECT id, display_id, project_id, description_path, acceptance_path "
                     + $"FROM {db}.tasks",
                     r => (Id: r.S("id"), Did: r.S("display_id"), Project: r.SN("project_id"),
                         Desc: r.S("description_path"), Acc: r.S("acceptance_path"))))
        {
            var owner = "task:" + task.Id;
            Add(task.Desc, owner);
            Add(task.Acc, owner);
            AddText(files.ReadText(task.Desc), owner);
            AddText(files.ReadText(task.Acc), owner);
            // ВЕСЬ КАТАЛОГ ЗАДАЧИ принадлежит задаче: артефакты заданий на неё нигде не
            // записаны ссылкой, но уехать обязаны вместе с ней
            var dir = Path.Combine(root,
                files.TaskDirRel(task.Project is null ? null : slugs.GetValueOrDefault(task.Project),
                    task.Did));
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    Add(Path.GetRelativePath(root, file).Replace('\\', '/'), owner);
                }
            }
        }
        foreach (var job in Sql.Query(conn, null,
                     $"SELECT task_id, request_path, result_path FROM {db}.jobs",
                     r => (Task: r.S("task_id"), Req: r.S("request_path"), Res: r.S("result_path"))))
        {
            Add(job.Req, "task:" + job.Task);
            Add(job.Res, "task:" + job.Task);
        }
        foreach (var msg in Sql.Query(conn, null,
                     $"SELECT task_id, text FROM {db}.chat_messages",
                     r => (Task: r.S("task_id"), Text: r.S("text"))))
        {
            AddText(msg.Text, "task:" + msg.Task);
        }
        foreach (var obj in Sql.Query(conn, null,
                     $"SELECT id, path_or_url, lora_path, description FROM {db}.objects",
                     r => (Id: r.S("id"), Path: r.S("path_or_url"), Lora: r.S("lora_path"),
                         Text: r.S("description"))))
        {
            var owner = "object:" + obj.Id;
            Add(obj.Path, owner);
            Add(obj.Lora, owner);
            AddText(obj.Text, owner);
        }
        foreach (var lora in Sql.Query(conn, null,
                     $"SELECT object_id, path FROM {db}.object_loras",
                     r => (Object: r.S("object_id"), Path: r.S("path"))))
        {
            Add(lora.Path, "object:" + lora.Object);
        }
        foreach (var record in Sql.Query(conn, null, $"SELECT id, text FROM {db}.experience",
                     r => (Id: r.S("id"), Text: r.S("text"))))
        {
            AddText(record.Text, "experience:" + record.Id);
        }
        return refs;
    }

    /// <summary>
    /// ЧТО ДЕЛАТЬ С КАЖДЫМ ФАЙЛОМ. Правило задания: ссылки из переносимого ЕДИНСТВЕННЫЕ —
    /// файл переносится; ссылается кто-то ещё — копируется, а в рабочей среде остаётся.
    /// </summary>
    private static List<PlannedFile> PlanFiles(SqliteConnection conn, string db, string root,
        Plan plan)
    {
        var mine = new HashSet<string>(StringComparer.Ordinal);
        mine.UnionWith(plan.Tasks.Select(v => "task:" + v));
        mine.UnionWith(plan.Objects.Select(v => "object:" + v));
        mine.UnionWith(plan.Experience.Select(v => "experience:" + v));
        var planned = new List<PlannedFile>();
        foreach (var (path, owners) in ScanRefs(conn, db, root))
        {
            if (!owners.Overlaps(mine))
            {
                continue;
            }
            planned.Add(new PlannedFile(path, owners.IsSubsetOf(mine)));
        }
        return planned;
    }

    /// <summary>Разложить файлы по архиву (или обратно) и переписать ссылки в .md.</summary>
    private static (int Moved, int Copied) MoveFiles(List<PlannedFile> files, string srcRoot,
        string dstRoot, string? code)
    {
        var moved = 0;
        var copied = 0;
        foreach (var file in files)
        {
            var src = Path.Combine(srcRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            var dst = Path.Combine(dstRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src))
            {
                continue;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
                if (file.Move)
                {
                    File.Delete(src);
                    moved++;
                }
                else
                {
                    copied++;
                }
                RewriteFile(dst, code);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // файл занят чужим просмотром: запись базы уже уехала, и терять весь перенос
                // из-за одного файла хуже, чем оставить его копию в рабочей среде
            }
        }
        return (moved, copied);
    }

    // --- ссылки ---

    /// <summary>Переписать наши ссылки в тексте: пометить их архивом либо снять пометку.</summary>
    public static string Rewrite(string? markdown, string? code)
    {
        var text = markdown ?? "";
        if (text.Length == 0)
        {
            return text;
        }
        // длинные адреса раньше коротких: короткий бывает началом длинного, и замена
        // «в порядке появления» испортила бы его
        foreach (var url in MdLinks.GetAllLinks(text)
                     .Where(ArchiveLinks.IsOurs)
                     .OrderByDescending(u => u.Length))
        {
            var replacement = ArchiveLinks.With(url, code);
            if (!string.Equals(url, replacement, StringComparison.Ordinal))
            {
                text = text.Replace(url, replacement, StringComparison.Ordinal);
            }
        }
        return text;
    }

    private static void RewriteFile(string path, string? code)
    {
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var text = File.ReadAllText(path);
        var rewritten = Rewrite(text, code);
        if (!string.Equals(text, rewritten, StringComparison.Ordinal))
        {
            File.WriteAllText(path, rewritten, new System.Text.UTF8Encoding(false));
        }
    }

    /// <summary>Ссылки в текстовых колонках приёмника: чат, опыт, паспорт объекта.</summary>
    private static void RewriteTexts(SqliteConnection conn, string db, Plan plan, string? code)
    {
        void Fix(string table, string column, string where)
        {
            if (where.Length == 0)
            {
                return;
            }
            foreach (var row in Sql.Query(conn, null,
                         $"SELECT id, {column} AS val FROM {db}.{table} WHERE {where}",
                         r => (Id: r.S("id"), Value: r.S("val"))))
            {
                var rewritten = Rewrite(row.Value, code);
                if (!string.Equals(row.Value, rewritten, StringComparison.Ordinal))
                {
                    Sql.Exec(conn, null, $"UPDATE {db}.{table} SET {column}=@v WHERE id=@id",
                        ("@v", rewritten), ("@id", row.Id));
                }
            }
        }
        Fix("chat_messages", "text", Blank(In("task_id", plan.Tasks)));
        Fix("experience", "text", Blank(In("id", plan.Experience)));
        Fix("objects", "description", Blank(In("id", plan.Objects)));
    }

    // --- итог ---

    private static ArchiveTransferResult Result(string target, string id, bool restore,
        Archive archive, int rows, int moved, int copied, Plan plan) =>
        new(target, id, restore, archive.Id, archive.Code, rows, moved, copied,
            plan.Tasks, plan.Objects, plan.Experience, plan.Projects);

    private void Append(ArchiveTransferResult result, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = result.Restore ? EventTypes.ArchiveRestored : EventTypes.ArchiveMoved,
            EntityType = "archive",
            EntityId = result.ArchiveId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                result.Target,
                result.Id,
                result.ArchiveCode,
                result.Rows,
                result.FilesMoved,
                result.FilesCopied,
                Tasks = result.Tasks.Count,
                Objects = result.Objects.Count,
                Experience = result.Experience.Count,
                Projects = result.Projects.Count,
            }),
        });
        tx.Commit();
    }
}
