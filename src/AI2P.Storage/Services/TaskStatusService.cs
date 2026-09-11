using AI2P.Core;
using AI2P.Core.Entities;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Справочник состояний задач (ТЗ v1.37, todo34_3; настройки → справочники).
/// Встроенные состояния заполняются сидом (порядковые номера через 10), пользователи
/// добавляют кастомные через UI. Названия хранятся по языкам (task_status_texts):
/// правка пользователя сохраняется на его языке; для языков без правки — сид/код.
/// Признак «кастом» у существующей записи меняется только в debug-режиме (как у ИИ-моделей).
/// </summary>
public sealed class TaskStatusService
{
    /// <summary>Сид встроенных состояний: код, цвет надписи, ключ названия в словарях.
    /// Названия кладутся в task_status_texts СРАЗУ НА ВСЕ языки словарей, поэтому берутся
    /// не по языку установки (Loc.T), а по каждому языку отдельно (Loc.In) — T-180.
    /// До T-180 русские названия были вписаны в код, а английские — списком рядом.</summary>
    private static readonly (string Id, string Color, string Key)[] Seeds =
    [
        (TaskStatuses.Draft, "#90a4ae", "msg.taskStatus.1"),
        (TaskStatuses.Pending, "#ffb74d", "msg.taskStatus.2"),
        (TaskStatuses.InProgress, "#42a5f5", "msg.taskStatus.3"),
        (TaskStatuses.Paused, "#ab47bc", "msg.taskStatus.4"),
        (TaskStatuses.Error, "#ef5350", "msg.taskStatus.5"),
        (TaskStatuses.Review, "#26c6da", "msg.taskStatus.6"),
        (TaskStatuses.NeedsFix, "#ff7043", "msg.taskStatus.7"),
        (TaskStatuses.Done, "#66bb6a", "msg.taskStatus.8"),
        (TaskStatuses.Cancelled, "#bdbdbd", "msg.taskStatus.9"),
    ];

    /// <summary>Языки, на которых сеются названия состояний: все словари приложения, но
    /// «ru» и «en» обязаны быть всегда — на них смотрит выборка List (язык → en → код).</summary>
    private static IEnumerable<string> SeedLanguages() =>
        Loc.Languages.Count > 0 ? Loc.Languages.Union(["ru", "en"]) : ["ru", "en"];

    private readonly Database _db;

    public TaskStatusService(Database db) => _db = db;

    /// <summary>Стартовое наполнение встроенными состояниями (идемпотентно): порядковые
    /// номера через 10; тексты пользователя (уже существующие строки) не трогаются.</summary>
    public void Seed()
    {
        using var conn = _db.Open();
        var now = Sql.ToDb(DateTime.UtcNow);
        for (var i = 0; i < Seeds.Length; i++)
        {
            var (id, color, key) = Seeds[i];
            Sql.Exec(conn, null, """
                INSERT INTO task_statuses (id, color, is_custom, is_active, sort_order, created_at, updated_at)
                VALUES (@id, @color, 0, 1, @order, @now, @now)
                ON CONFLICT(id) DO NOTHING
                """,
                ("@id", id), ("@color", color), ("@order", (i + 1) * 10), ("@now", now));
            foreach (var lang in SeedLanguages())
            {
                Sql.Exec(conn, null, """
                    INSERT INTO task_status_texts (status_id, lang, name) VALUES (@id, @lang, @name)
                    ON CONFLICT(status_id, lang) DO NOTHING
                    """, ("@id", id), ("@lang", lang), ("@name", Loc.In(lang, key)));
            }
        }
    }

    /// <summary>
    /// Перевод старого состояния «ждёт ответа» в «пауза» (T-185, шаг обновления билда 80):
    /// код <c>waiting_reply</c> сменился на <c>paused</c> — смысл шире (ждём не только ответа
    /// человека, но и агента-соседа, и завершения подзадач). Идемпотентно: задачи со старым
    /// кодом переводятся, встроенная запись справочника со старым кодом и её тексты убираются
    /// (новая уже посеяна <see cref="Seed"/>). Пользовательскую запись с таким кодом (is_custom)
    /// не трогаем — её завёл человек, и она его. Возвращает число переведённых задач.
    /// </summary>
    public int MigrateWaitingReplyToPaused()
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var moved = Sql.Exec(conn, tx, """
            UPDATE tasks SET status=@new, updated_at=@now WHERE status=@old
            """,
            ("@new", TaskStatuses.Paused), ("@old", TaskStatuses.LegacyWaitingReply),
            ("@now", Sql.ToDb(DateTime.UtcNow)));
        var custom = Sql.Scalar<long?>(conn, tx,
            "SELECT is_custom FROM task_statuses WHERE id=@id", ("@id", TaskStatuses.LegacyWaitingReply));
        if (custom == 0)
        {
            Sql.Exec(conn, tx, "DELETE FROM task_status_texts WHERE status_id=@id",
                ("@id", TaskStatuses.LegacyWaitingReply));
            Sql.Exec(conn, tx, "DELETE FROM task_statuses WHERE id=@id AND is_custom=0",
                ("@id", TaskStatuses.LegacyWaitingReply));
        }
        tx.Commit();
        return moved;
    }

    /// <summary>Список состояний на языке lang (название: язык → en → код), сортировка
    /// по порядковому номеру. includeInactive — для справочника в настройках.</summary>
    public List<TaskStatusDef> List(string lang, bool includeInactive = false)
    {
        using var conn = _db.Open();
        var where = includeInactive ? "" : "WHERE s.is_active=1";
        return Sql.Query(conn, null, $"""
            SELECT s.*, COALESCE(t.name, te.name, s.id) AS name
            FROM task_statuses s
            LEFT JOIN task_status_texts t ON t.status_id = s.id AND t.lang = @lang
            LEFT JOIN task_status_texts te ON te.status_id = s.id AND te.lang = 'en'
            {where}
            ORDER BY s.sort_order, s.id
            """, Map, ("@lang", lang));
    }

    /// <summary>Есть ли состояние с таким кодом (для валидации tasks.status).</summary>
    public bool Exists(string id)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM task_statuses WHERE id=@id", ("@id", id)) > 0;
    }

    /// <summary>Добавить кастомное состояние (ТЗ v1.37): код уникален и не меняется потом;
    /// признак «кастом» у новых через UI — всегда истина.</summary>
    public TaskStatusDef Create(TaskStatusDef def, string lang)
    {
        var id = def.Id.Trim();
        if (id.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.taskStatus.10"));
        }
        if (Exists(id))
        {
            throw new ArgumentException(Loc.T("msg.taskStatus.11", id));
        }
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO task_statuses (id, color, is_custom, is_active, sort_order, created_at, updated_at)
            VALUES (@id, @color, 1, @active, @order, @now, @now)
            """,
            ("@id", id), ("@color", def.Color), ("@active", def.IsActive ? 1 : 0),
            ("@order", def.SortOrder), ("@now", now));
        Sql.Exec(conn, null, """
            INSERT INTO task_status_texts (status_id, lang, name) VALUES (@id, @lang, @name)
            """, ("@id", id), ("@lang", lang), ("@name", def.Name.Trim()));
        def.Id = id;
        def.IsCustom = true;
        return def;
    }

    /// <summary>Правка состояния: цвет/активность/порядок — всегда; название — на языке lang;
    /// код не меняется; признак «кастом» меняется только в debug-режиме (в релизе игнорируется).</summary>
    public TaskStatusDef Update(string id, TaskStatusDef def, string lang)
    {
        using var conn = _db.Open();
        var isCustom = Sql.Scalar<long?>(conn, null,
            "SELECT is_custom FROM task_statuses WHERE id=@id", ("@id", id))
            ?? throw new ArgumentException(Loc.T("msg.taskStatus.12", id));
        var custom = AppInfo.IsDebug ? (def.IsCustom ? 1 : 0) : isCustom;
        Sql.Exec(conn, null, """
            UPDATE task_statuses
            SET color=@color, is_custom=@custom, is_active=@active, sort_order=@order, updated_at=@now
            WHERE id=@id
            """,
            ("@id", id), ("@color", def.Color), ("@custom", custom),
            ("@active", def.IsActive ? 1 : 0), ("@order", def.SortOrder),
            ("@now", Sql.ToDb(DateTime.UtcNow)));
        var name = def.Name.Trim();
        if (name.Length > 0)
        {
            Sql.Exec(conn, null, """
                INSERT INTO task_status_texts (status_id, lang, name) VALUES (@id, @lang, @name)
                ON CONFLICT(status_id, lang) DO UPDATE SET name=@name
                """, ("@id", id), ("@lang", lang), ("@name", name));
        }
        def.Id = id;
        def.IsCustom = custom != 0;
        return def;
    }

    private static TaskStatusDef Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        Name = r.S("name"),
        Color = r.S("color"),
        IsCustom = r.B("is_custom"),
        IsActive = r.B("is_active"),
        SortOrder = (int)r.L("sort_order"),
    };
}
