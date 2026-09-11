using AI2P.Core;
using System.Text;
using System.Text.Json;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage;

/// <summary>Итог применения пачки изменений (для диагностики репликации, ТЗ гл. 6).</summary>
public sealed class ApplyResult
{
    /// <summary>Изменений применено к таблицам.</summary>
    public int Applied { get; set; }

    /// <summary>Пропущено: наша версия строки не старше пришедшей (в том числе эхо).</summary>
    public int Skipped { get; set; }

    /// <summary>Конфликтов: пришедшее изменение проиграло LWW правке ДРУГОГО узла.
    /// При раздельном владении (ТЗ гл. 6) это редкость — сигнал разбираться.</summary>
    public int Conflicts { get; set; }

    /// <summary>Курсор источника после применения (максимальный принятый <see cref="RowChange.Seq"/>).</summary>
    public long Cursor { get; set; }

    /// <summary>Последний конфликт словами — показывается на экране диагностики.</summary>
    public string LastConflict { get; set; } = "";

    /// <summary>
    /// СТРОКИ, КОТОРЫЕ БАЗА НЕ ПРИНЯЛА (T-160): нарушение ограничения целостности.
    /// Пачка из-за них больше не теряется — они откладываются в очередь повтора
    /// (<see cref="AI2P.Storage.Services.ReplPendingService"/>) и пробуются снова.
    /// </summary>
    public List<RowFailure> Failed { get; } = [];

    /// <summary>
    /// СТРОКИ, ПРИНЯТЫЕ БЕЗ РОДИТЕЛЯ (T-169-S0): база отказала по ссылке
    /// («FOREIGN KEY constraint failed»), и они применены вторым проходом, с выключенной
    /// проверкой ссылок. Для журнала работ: ссылка висит в пустоту, пока родитель не приедет
    /// (а у записи справочника моделей он может не приехать никогда — T-227).
    /// </summary>
    public List<RowFailure> Adopted { get; } = [];
}

/// <summary>
/// ЖУРНАЛ ИЗМЕНЕНИЙ СТРОК — основа репликации (ТЗ п. 6.1, гл. 6; этап 43).
///
/// Механизм: каждая запись в реплицируемую таблицу дополнительно кладёт строку в
/// <c>changes</c> — <c>(seq, tbl, pk, op, ts, node_id, payload_json)</c>. Репликация после
/// этого сводится к «отдай мне изменения после курсора», а применение — к вставке или
/// обновлению строки. Редкий конфликт (правка одной строки на двух серверах) разрешается
/// по большему <c>(ts, node_id)</c> — «выиграл последний», LWW.
///
/// Почему не event sourcing, как обещал принцип №3 ТЗ: полные payload'ы и обработчик
/// применения на каждый из полусотни типов событий означали бы переписывание всех методов
/// записи всех сервисов и постоянный риск «проекция разъехалась с журналом». Журнал
/// изменений СТРОК даёт одну точку записи, а новые сущности подхватываются сами.
///
/// Эта «одна точка» — ТРИГГЕРЫ SQLite: они ставятся на все реплицируемые таблицы по их
/// фактическому составу колонок и переустанавливаются при каждом старте. Сервисы хранилища
/// (их семнадцать) не правились вовсе — и не будут правиться при добавлении новых.
///
/// Применение чужих изменений идёт под пометкой в таблице <c>repl_apply</c>: триггер берёт
/// автора и время оттуда, а не «себя и сейчас». Иначе транзитный сервер выдавал бы чужие
/// правки за свои, и в звезде из трёх серверов изменения ходили бы по кругу.
/// </summary>
public static class ChangeLog
{
    /// <summary>
    /// Реплицируемые таблицы БД ОРГАНИЗАЦИИ — в порядке зависимостей (родители раньше детей):
    /// в этом же порядке идёт первичное наполнение журнала.
    ///
    /// Не реплицируются: <c>meta</c> (там node_id — он у каждого узла свой), <c>app_state</c>
    /// (состояние UI пер-пользователя: лейаут фреймов зависит от экрана, а не от организации),
    /// сам <c>changes</c> и служебный <c>repl_apply</c>.
    /// </summary>
    public static readonly string[] OrgTables =
    [
        "counters", "roles", "role_texts", "skills", "skill_texts", "io_formats",
        "io_format_texts", "task_statuses", "task_status_texts",
        "actions", "action_texts", "ai_models",
        // пер-серверная часть записи справочника моделей (T-8-S1): у каждого сервера своя
        // строка, пишет её только он — партнёру она едет, чтобы в справочнике было видно,
        // на каких серверах локальная модель включена
        "ai_model_servers",
        "projects", "project_servers", "objects",
        // тэги объектов (T-259): своей сущности у тэга нет — он живёт строками этой
        // таблицы и едет партнёру вместе с объектом, как task_tags вместе с задачей
        "object_tags",
        // обучение адаптера LoRA под модель (T-12-S1): едет вместе с объектом — на соседнем
        // сервере должно быть видно, под какие модели персонаж обучен и чем это кончилось
        "object_loras",
        "executors", "teams", "team_members", "tasks", "task_executors",
        // «могут заменить исполнителя» (T-221): запасные исполнители задачи — такая же
        // связь, как task_executors, и едет она тем же журналом
        "task_alt_executors", "task_skills",
        // тэги задач (T-222): своей сущности у тэга нет — он живёт строками этой таблицы,
        // поэтому и реплицируется вместе с задачей обычным журналом изменений
        "task_tags",
        "task_blockers", "task_links", "cycles", "schedules", "jobs", "chat_messages",
        // заявка на запуск задачи с другого сервера (T-196): едет владельцу задачи, он
        // запускает. Отметки об исполнении (run_requests_applied) — пер-серверные, их здесь нет
        "run_requests",
        // заявка на ОСТАНОВКУ задачи или иерархии (T-263): едет так же, но своей таблицей —
        // сервер прежней версии принял бы её за просьбу запустить, а незнакомую таблицу
        // он пропускает молча
        "stop_requests",
        // заявка на СМЕНУ ДИРИЖЁРА организации (T-21-S1): едет второй стороне тем же
        // журналом, решение возвращается им же. Своя таблица — по той же причине, что
        // и у остановки: незнакомую строку старый партнёр пропустит, а незнакомый ВИД
        // заявки на запуск он выполнил бы наоборот
        "conductor_requests",
        "security_rules", "import_sources", "experience",
        // тэги записи опыта (T-24-S0): своей сущности у тэга нет — он живёт строками этой
        // таблицы и едет партнёру вместе с записью, как task_tags вместе с задачей
        "experience_tags", "model_keys",
        // УВЕДОМЛЕНИЯ ПОЛЬЗОВАТЕЛЯ (T-272): правило заводят на одном сервере, а список
        // принадлежит организации — значит, видно его должно быть на всех. Связи с
        // исполнителями и проектами едут тем же журналом, как task_executors вместе
        // с задачей. Отметок об отправке (notification_marks) здесь НЕТ намеренно: письмо
        // шлёт владелец задачи, и отметка — его собственная память (ср. run_requests_applied)
        "notification_rules", "notification_executors", "notification_projects",
        // РЕЕСТР АРХИВОВ (T-40-S0): сами архивы принадлежат организации, значит видеть их
        // должны все её серверы — в том числе чтобы скачать архив у соседа (T-47-S0).
        // Признак «текущий» едет вместе со строкой: перенос данных идёт только в текущий
        // архив, и он обязан быть одним и тем же на всём кластере. А вот archive_states
        // здесь НЕТ намеренно: открыть, закрыть и удалить каждый сервер решает сам —
        // на одном архив распакован для просмотра, на другом лежит .zip, на третьем его нет
        "archives",
        // ГДЕ АРХИВ ЕСТЬ (T-47-S0): удаление архива не реплицируется, но в общем списке надо
        // отметить, на каких серверах архив есть, — оттуда же берётся список источников для
        // скачивания. Своя строка на каждый сервер, как ai_model_servers; открыт он там или
        // закрыт, соседей не касается (archive_states по-прежнему не реплицируется)
        "archive_servers",
        // СМЕНА ТЕКУЩЕГО АРХИВА (T-47-S0) — поручение, и потому СВОЯ таблица, а не новый вид
        // строки в archives: сервер прежней версии разобрал бы незнакомый вид через
        // else-ветку, а строку незнакомой таблицы он пропускает молча (наука T-263).
        // Отметки об исполнении (archive_handovers_applied) здесь НЕТ намеренно
        "archive_handovers",
        // ПРАВИЛА АРХИВАЦИИ (T-41-S0) — и общие правила организации, и правила каждого
        // архива лежат одной таблицей и едут вместе с реестром: правило принадлежит
        // организации, а не машине (в отличие от archive_states выше). Автоматическая
        // архивация идёт на дирижёре, а завести правило человек может с любого сервера
        "archive_rules",
        // ПЛАГИНЫ (T-111-S0): шлюз в видеоредактор, конвертор, подключение MCP. Запись
        // принадлежит организации — плагин объявили на одном сервере, видно его на всех.
        // А вот пути к софту и признака «установлен здесь» в этой таблице НЕТ вовсе: они
        // пер-серверные и живут в config.json, потому что реплицированный чужой путь
        // ломает установку у соседа и «уже не уходит» (наука T-164)
        "plugins",
        "events",
    ];

    /// <summary>
    /// Реплицируемые таблицы СЕРВЕРНОЙ БД (ТЗ п. 6.4.1). Их отдают не целиком, а по
    /// организации сеанса: аккаунты — только участников этой организации, серверы — только
    /// её серверов (<c>ReplicationScope</c>). Аккаунты обязаны ехать: иначе войти на второй
    /// сервер было бы нечем при недоступном сервере-владельце аккаунта (ТЗ п. 2.14).
    ///
    /// Не реплицируются: настройки самого сервера (адрес правится в config.json), токены
    /// доступа, счётчики SRV-номеров, серверный журнал событий и node_id.
    /// </summary>
    public static readonly string[] ServerTables = ["accounts", "orgs", "servers", "org_servers"];

    /// <summary>
    /// Колонки, которые НИКОГДА не покидают сервер (ТЗ гл. 12): токены доступа сервер-сервер,
    /// состояние собственной заявки, последняя ошибка связи и признак «это я». Вырезаются
    /// при отдаче — не «не применяются на той стороне», а именно не отправляются.
    /// </summary>
    private static readonly Dictionary<string, string[]> LocalOnlyColumns = new(StringComparer.Ordinal)
    {
        ["servers"] = ["token", "peer_token", "join_status", "join_org", "join_ref",
            "last_error", "is_local"],
    };

    /// <summary>Единственные колонки СВОЕЙ записи сервера, которые правятся извне (todo43):
    /// интервалы репликации — их задаёт дирижёр всем серверам кластера.</summary>
    public static readonly string[] ReplicationColumns = ["repl_interval_sec", "repl_retry_sec"];

    /// <summary>Журнал изменений и пометка «идёт применение чужих изменений».</summary>
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS changes (
          seq          INTEGER PRIMARY KEY AUTOINCREMENT,  -- локальный курсор репликации
          tbl          TEXT NOT NULL,
          pk           TEXT NOT NULL,                      -- составной ключ склеен через |
          op           TEXT NOT NULL,                      -- upsert / delete
          ts           TEXT NOT NULL,                      -- часы LWW: время У АВТОРА
          node_id      TEXT NOT NULL,                      -- узел-АВТОР, не пересылающий
          payload_json TEXT NOT NULL DEFAULT '{}'
        );
        CREATE INDEX IF NOT EXISTS ix_changes_row  ON changes(tbl, pk, seq);
        CREATE INDEX IF NOT EXISTS ix_changes_node ON changes(node_id, seq);

        -- пока строка здесь есть, триггеры пишут в журнал ЧУЖОГО автора и ЧУЖОЕ время:
        -- применение реплики не должно выдавать чужие правки за свои
        CREATE TABLE IF NOT EXISTS repl_apply (
          node_id TEXT NOT NULL,
          ts      TEXT NOT NULL
        );
        DELETE FROM repl_apply;

        -- пока строка здесь есть, триггеры в журнал НЕ ПИШУТ ВОВСЕ (T-227): так заводится
        -- то, что каждая установка создаёт себе сама и одинаково — записи справочника
        -- моделей из дистрибутива и модели, подготовленные инсталлятором. Гонять их
        -- репликацией незачем: у них фиксированные идентификаторы, и на партнёре такие же
        -- строки уже есть. Пометка снимается при каждом старте — оборванный сид не должен
        -- заглушить журнал навсегда
        CREATE TABLE IF NOT EXISTS repl_mute (
          ts TEXT NOT NULL
        );
        DELETE FROM repl_mute;
        """;

    /// <summary>
    /// Поставить журнал изменений: таблицы, триггеры по фактическому составу колонок и —
    /// при первом создании — первичное наполнение существующими строками.
    ///
    /// Наполнение делает первичную репликацию частным случаем обычной: сервер с пустой базой
    /// просто просит «изменения после нуля» и получает снимок целиком, отдельного протокола
    /// для этого не нужно.
    /// </summary>
    /// <summary>Префикс имён триггеров журнала — по нему они снимаются перед миграциями.</summary>
    private const string TriggerPrefix = "ai2p_chg_";

    /// <summary>
    /// Снять триггеры журнала. Делается ПЕРЕД миграциями схемы: SQLite не даёт удалить
    /// или переименовать колонку, на которую ссылается триггер, а триггеры журнала
    /// перечисляют все колонки таблицы. Ставятся они заново в конце инициализации.
    /// </summary>
    public static void DropTriggers(SqliteConnection conn)
    {
        var triggers = Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE @p",
            r => r.S("name"), ("@p", TriggerPrefix + "%"));
        foreach (var trigger in triggers)
        {
            Sql.Exec(conn, null, $"DROP TRIGGER IF EXISTS {Safe(trigger)}");
        }
    }

    public static void Install(SqliteConnection conn, DatabaseKind kind)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Ddl;
            cmd.ExecuteNonQuery();
        }
        var tables = Tables(kind);
        var seeded = Sql.Scalar<string>(conn, null, "SELECT value FROM meta WHERE key='changes_seeded'");
        foreach (var table in tables)
        {
            var columns = ColumnsOf(conn, table);
            if (columns.Count == 0)
            {
                continue;   // таблицы нет в этой базе — пропускаем молча
            }
            var keys = KeysOf(conn, table);
            if (keys.Count == 0)
            {
                continue;   // без первичного ключа строку не адресовать, реплицировать нечего
            }
            InstallTriggers(conn, table, columns, keys);
            if (seeded is null)
            {
                Backfill(conn, table, columns, keys);
            }
        }
        if (seeded is null)
        {
            Sql.Exec(conn, null, "INSERT OR REPLACE INTO meta(key, value) VALUES ('changes_seeded', '1')");
        }
    }

    public static string[] Tables(DatabaseKind kind) =>
        kind == DatabaseKind.Server ? ServerTables : OrgTables;

    // --- пометка «не писать в журнал» (T-227) ---

    /// <summary>
    /// НЕ ПИСАТЬ В ЖУРНАЛ, пока пометка держится (T-227).
    ///
    /// Ею оборачивается то, что каждая установка делает себе САМА и одинаково: сид справочника
    /// моделей (фиксированные UUID), импорт моделей, подготовленных инсталлятором, и пересчёт
    /// производного признака размещения. Такие строки на партнёре уже есть — реплицировать их
    /// значит гонять по сети то, что и так совпадает, а заодно тащить туда местные мелочи
    /// вроде порядкового номера записи.
    ///
    /// Пометка ОБЩАЯ для базы, а не для соединения: триггер живёт в SQLite и о соединениях
    /// ничего не знает. Поэтому применяется она только на СТАРТЕ организации, когда её данные
    /// ещё никто не правит. Вложенность допустима — пометки считаются строками таблицы.
    ///
    /// Снимается пометка при ЛЮБОМ исходе (<c>using</c> / try-finally): иначе после ошибки
    /// сида перестала бы реплицироваться вся организация. Вторая страховка — <c>DELETE FROM
    /// repl_mute</c> в <see cref="Ddl"/>: он выполняется при каждом старте.
    /// </summary>
    public static IDisposable Mute(Database db) => new MuteScope(db);

    private sealed class MuteScope : IDisposable
    {
        private readonly Database _db;
        private bool _released;

        public MuteScope(Database db)
        {
            _db = db;
            using var conn = db.Open();
            Sql.Exec(conn, null, "INSERT INTO repl_mute (ts) VALUES (@ts)",
                ("@ts", Sql.ToDb(DateTime.UtcNow)));
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            using var conn = _db.Open();
            Sql.Exec(conn, null,
                "DELETE FROM repl_mute WHERE rowid=(SELECT MIN(rowid) FROM repl_mute)");
        }
    }

    /// <summary>Пометка «не писать в журнал» сейчас держится (для проверок).</summary>
    public static bool IsMuted(Database db)
    {
        using var conn = db.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM repl_mute") > 0;
    }

    /// <summary>
    /// ПОЧИНИТЬ ТО, ЧТО УЖЕ РАЗЪЕХАЛОСЬ (T-3-S1; шаг обновления билда 86).
    ///
    /// Исправленные часы (<see cref="StampSql"/>) спасают будущие правки, но не то, что
    /// партнёр не принял месяц назад: его курсор давно ушёл вперёд, и повторно те записи
    /// он не прочитает — состав команды у него так и остался без части исполнителей.
    ///
    /// Поэтому строки, у которых в журнале есть ДВА изменения с одинаковыми часами, надо
    /// назвать партнёру заново: их последняя запись копируется в журнал со свежими часами.
    /// Она несёт состояние строки целиком (или удаление), поэтому одной записи достаточно —
    /// и для «пропала у партнёра», и для «осталась у партнёра, хотя удалена у нас».
    ///
    /// Берутся ТОЛЬКО свои строки (автор — наш узел) и только те, которых с тех пор не
    /// касался партнёр: чужую строку мы знаем хуже владельца, и повторять её своим голосом
    /// значило бы вернуть партнёру его же устаревшее состояние. Поэтому чинить надо на ОБЕИХ
    /// сторонах пары — каждая называет заново своё.
    ///
    /// Идемпотентно: повторный запуск найдёт те же строки и назовёт их ещё раз тем же
    /// значением — состояние от этого не меняется.
    /// </summary>
    /// <returns>Сколько строк названо заново.</returns>
    public static int ResendTiedRows(Database db)
    {
        using var conn = db.Open();
        var me = Sql.Scalar<string>(conn, null, "SELECT value FROM meta WHERE key='node_id'") ?? "";
        if (me.Length == 0)
        {
            return 0;
        }
        var tied = Sql.Query(conn, null, """
            SELECT tbl, pk FROM changes WHERE node_id=@me
            GROUP BY tbl, pk, ts HAVING COUNT(*) > 1
            """,
            r => (Table: r.S("tbl"), Pk: r.S("pk")), ("@me", me)).Distinct().ToList();
        var done = 0;
        foreach (var (table, pk) in tied)
        {
            var last = Sql.Query(conn, null,
                "SELECT seq, node_id FROM changes WHERE tbl=@t AND pk=@p ORDER BY seq DESC LIMIT 1",
                r => (Seq: r.L("seq"), Node: r.S("node_id")), ("@t", table), ("@p", pk)).First();
            if (last.Node != me)
            {
                continue;   // строку с тех пор поправил партнёр — его правка уже разошлась
            }
            Sql.Exec(conn, null, $"""
                INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json)
                SELECT tbl, pk, op, {StampSql("@t", "@p")}, node_id, payload_json
                FROM changes WHERE seq=@seq
                """,
                ("@t", table), ("@p", pk), ("@seq", last.Seq));
            done++;
        }
        return done;
    }

    // --- чтение журнала (сторона-источник) ---

    /// <summary>Номер последнего изменения в журнале: «докуда» партнёр может дочитать.</summary>
    public static long Head(SqliteConnection conn) =>
        Sql.Scalar<long?>(conn, null, "SELECT MAX(seq) FROM changes") ?? 0;

    /// <summary>Сколько изменений ждёт партнёра — отставание для экрана диагностики
    /// и знаменатель процента в прогресс-баре.</summary>
    public static long CountAfter(SqliteConnection conn, long after, string exceptNode) =>
        Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM changes WHERE seq>@a AND node_id<>@n", ("@a", after), ("@n", exceptNode));

    /// <summary>
    /// Изменения после курсора. Свои автору не возвращаем (<paramref name="exceptNode"/>) —
    /// это гасит эхо: то, что приехало от него, у нас записано под его же авторством.
    /// </summary>
    public static List<RowChange> Read(SqliteConnection conn, long after, int limit, string exceptNode)
    {
        var rows = Sql.Query(conn, null, """
            SELECT seq, tbl, pk, op, ts, node_id, payload_json FROM changes
            WHERE seq>@a AND node_id<>@n ORDER BY seq LIMIT @l
            """,
            r => new RowChange
            {
                Seq = r.L("seq"),
                Table = r.S("tbl"),
                Pk = r.S("pk"),
                Op = r.S("op"),
                Ts = r.S("ts"),
                NodeId = r.S("node_id"),
                PayloadJson = r.S("payload_json"),
            },
            ("@a", after), ("@n", exceptNode), ("@l", limit));
        foreach (var row in rows)
        {
            Sanitize(row);
        }
        return rows;
    }

    /// <summary>Вырезать из payload колонки, которые не покидают сервер (ТЗ гл. 12).</summary>
    private static void Sanitize(RowChange change)
    {
        if (!LocalOnlyColumns.TryGetValue(change.Table, out var hidden))
        {
            return;
        }
        var values = Parse(change.PayloadJson);
        foreach (var column in hidden)
        {
            values.Remove(column);
        }
        change.PayloadJson = JsonSerializer.Serialize(values);
    }

    // --- применение (сторона-приёмник) ---

    /// <summary>
    /// Применить пачку изменений в ОДНОЙ транзакции (ТЗ п. 6.1: слияние идемпотентно).
    ///
    /// Для каждой строки сравниваем часы: пришедшее <c>(ts, node_id)</c> против нашего
    /// последнего изменения той же строки. Не новее — не применяем вовсе; это и
    /// идемпотентность, и гарантия, что изменение не пойдёт по кругу в звезде серверов.
    /// </summary>
    /// <param name="guard">Разрешение на строку: может поправить значения (например,
    /// не дать чужому серверу переписать нашу собственную запись в списке серверов)
    /// или вернуть false — тогда изменение пропускается.</param>
    public static ApplyResult Apply(Database db, IEnumerable<RowChange> changes,
        Func<string, string, Dictionary<string, object?>, bool>? guard = null)
    {
        var all = changes as IReadOnlyList<RowChange> ?? changes.ToList();
        var items = Collapse(all);
        ApplyResult result;
        try
        {
            result = ApplyInOneTransaction(db, items, guard);
        }
        catch (SqliteException)
        {
            // ОДНА СТРОКА НЕ ДОЛЖНА РОНЯТЬ ПАЧКУ (T-160) — см. ApplyOneByOne
            result = ApplyOneByOne(db, items, guard);
            // ...А СТРОКА БЕЗ РОДИТЕЛЯ НЕ ДОЛЖНА ПРОПАСТЬ ВОВСЕ (T-169-S0) — см. ApplyDangling
            ApplyDangling(db, guard, result);
        }
        result.Skipped += all.Count - items.Count;
        return result;
    }

    /// <summary>Признак отказа «нет строки, на которую ссылается эта» — по тексту SQLite.</summary>
    private static bool IsDanglingReference(RowFailure failure) =>
        failure.Reason.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// СТРОКА, У КОТОРОЙ ЗДЕСЬ НЕТ РОДИТЕЛЯ, ВСЁ РАВНО ДОЛЖНА ПРИЕХАТЬ (T-169-S0).
    ///
    /// Жалоба, из-за которой это появилось: заведённый на дирижёре исполнитель не приезжал
    /// на второй сервер, и вместе с ним не приезжал член команды с этим исполнителем. Причина
    /// не в журнале и не в курсорах: строка исполнителя ссылается на запись справочника моделей
    /// (<c>executors.model_id</c>), а записи справочника у партнёра может не быть ВОВСЕ —
    /// модели дистрибутива и модели, подготовленные инсталлятором, намеренно не реплицируются
    /// (T-227), и у второй установки они свои. База отвечала «FOREIGN KEY constraint failed»,
    /// строка уходила в очередь повтора (T-160) и пробовалась там вечно — у заказчика ровно
    /// так уже висела строка <c>org_servers</c> с 1597 попытками (T-20-S1). Родитель при этом
    /// не приедет никогда, а следом за исполнителем встаёт всё, что ссылается на него:
    /// состав команды, назначения задач, ответственный.
    ///
    /// Правило теперь такое: отказ по ССЫЛКЕ — не повод терять строку. У реплики нормальное
    /// состояние «ребёнок уже здесь, родителя ещё (или совсем) нет»: связь восстановится сама,
    /// когда родитель приедет, а до тех пор ссылка висит в пустоту — ровно так же, как
    /// <c>executors.account_id</c>, у которого FK нет вовсе (аккаунты в серверной БД).
    /// Поэтому такие строки применяются вторым проходом, на соединении с ВЫКЛЮЧЕННОЙ проверкой
    /// ссылок. Отказы ДРУГОГО рода (занятое уникальное значение, NOT NULL) остаются в очереди
    /// повтора: там ждать действительно есть чего.
    ///
    /// Проверка ссылок выключается только ВНЕ транзакции (<c>PRAGMA foreign_keys</c> внутри
    /// неё не делает ничего), поэтому проход идёт своим соединением и своей транзакцией.
    /// </summary>
    private static void ApplyDangling(Database db,
        Func<string, string, Dictionary<string, object?>, bool>? guard, ApplyResult result)
    {
        var orphans = result.Failed.Where(IsDanglingReference).ToList();
        if (orphans.Count == 0)
        {
            return;
        }
        using var conn = db.Open();
        Sql.Exec(conn, null, "PRAGMA foreign_keys=OFF");
        try
        {
            using var tx = conn.BeginTransaction();
            Sql.Exec(conn, tx, "DELETE FROM repl_apply");
            Sql.Exec(conn, tx, "INSERT INTO repl_apply(node_id, ts) VALUES ('', '')");
            var known = new Dictionary<string, (List<string> Columns, List<string> Keys)>(
                StringComparer.Ordinal);
            foreach (var failure in orphans)
            {
                Sql.Exec(conn, tx, "SAVEPOINT repl_orphan");
                try
                {
                    ApplyRow(conn, tx, failure.Change, guard, known, result);
                    Sql.Exec(conn, tx, "RELEASE repl_orphan");
                    result.Failed.Remove(failure);
                    result.Adopted.Add(failure);
                }
                catch (SqliteException)
                {
                    // не в ссылке дело — строка остаётся в очереди повтора
                    Sql.Exec(conn, tx, "ROLLBACK TO repl_orphan");
                    Sql.Exec(conn, tx, "RELEASE repl_orphan");
                }
            }
            Sql.Exec(conn, tx, "DELETE FROM repl_apply");
            tx.Commit();
        }
        finally
        {
            Sql.Exec(conn, null, "PRAGMA foreign_keys=ON");
        }
    }

    /// <summary>
    /// ОДНА СТРОКА — ОДНО ИЗМЕНЕНИЕ ЗА ПАЧКУ (T-3-S1).
    ///
    /// Журнал хранит СОСТОЯНИЕ строки целиком, а не разницу, поэтому промежуточные изменения
    /// внутри пачки применять незачем: итог тот же, работы меньше. А главное — так пачка
    /// переживает пару «удалили и вставили заново», пришедшую от партнёра СТАРОЙ версии: там
    /// обе записи ещё могут стоять одним временем, и вторая из них отбрасывалась бы как
    /// «не новее» (см. <see cref="StampSql"/>). Остаётся ПОСЛЕДНЯЯ запись строки — она же
    /// несёт наибольший номер, поэтому курсор пачки не сдвигается.
    /// </summary>
    private static IReadOnlyList<RowChange> Collapse(IReadOnlyList<RowChange> changes)
    {
        var last = new Dictionary<(string Table, string Pk), int>();
        for (var i = 0; i < changes.Count; i++)
        {
            last[(changes[i].Table, changes[i].Pk)] = i;
        }
        if (last.Count == changes.Count)
        {
            return changes;   // повторов нет — обычный случай, лишнего списка не строим
        }
        var kept = new List<RowChange>(last.Count);
        for (var i = 0; i < changes.Count; i++)
        {
            if (last[(changes[i].Table, changes[i].Pk)] == i)
            {
                kept.Add(changes[i]);
            }
        }
        return kept;
    }

    /// <summary>
    /// Быстрый путь: вся пачка одной транзакцией, целостность проверяется на коммит.
    /// Так — и ТОЛЬКО так — работало применение до T-160, поэтому отказ базы на одной строке
    /// откатывал всю пачку и выходил исключением наружу, обрывая сеанс. Метод открыт, чтобы
    /// это поведение можно было показать проверкой (<c>T160Tests</c>).
    /// </summary>
    public static ApplyResult ApplyInOneTransaction(Database db, IReadOnlyList<RowChange> changes,
        Func<string, string, Dictionary<string, object?>, bool>? guard = null)
    {
        var result = new ApplyResult();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        // порядок строк в пачке — порядок журнала источника, то есть родитель раньше ребёнка;
        // но внутри одной пачки ссылка может смотреть вперёд, поэтому целостность
        // проверяется один раз на коммит, а не на каждой строке
        Sql.Exec(conn, tx, "PRAGMA defer_foreign_keys=ON");
        Sql.Exec(conn, tx, "DELETE FROM repl_apply");
        Sql.Exec(conn, tx, "INSERT INTO repl_apply(node_id, ts) VALUES ('', '')");
        var known = new Dictionary<string, (List<string> Columns, List<string> Keys)>(StringComparer.Ordinal);
        try
        {
            foreach (var change in changes)
            {
                ApplyRow(conn, tx, change, guard, known, result);
            }
            Sql.Exec(conn, tx, "DELETE FROM repl_apply");
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        return result;
    }

    /// <summary>
    /// ПАЧКА ПО СТРОЧКЕ (T-160) — путь на случай, когда пачку целиком база не приняла.
    ///
    /// Так было: одно нарушение целостности откатывало все четыреста строк, сеанс кончался
    /// исключением «SQLite Error 19», курсор не двигался — и следующий сеанс упирался в ту
    /// же строку. Организация навсегда оставалась скопированной до этого места: до файлов
    /// (описания задач, критерии, результаты заданий) дело не доходило вовсе.
    ///
    /// Теперь каждая строка применяется в своей точке отката, и отвергнутая строка не мешает
    /// остальным. Проверка ссылок здесь НЕ откладывается до коммита нарочно: нарушение должно
    /// всплыть на самой строке, а не на всей пачке. Ссылку «вперёд» (ребёнок в пачке раньше
    /// родителя) это не ломает — отвергнутые строки пробуются заново, пока хоть одна проходит.
    /// Что не прошло и на последнем круге, уезжает в очередь повтора вызывающей стороне
    /// (<see cref="ApplyResult.Failed"/>): родитель может приехать следующей пачкой.
    /// </summary>
    private static ApplyResult ApplyOneByOne(Database db, IReadOnlyList<RowChange> changes,
        Func<string, string, Dictionary<string, object?>, bool>? guard)
    {
        var result = new ApplyResult();
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "DELETE FROM repl_apply");
        Sql.Exec(conn, tx, "INSERT INTO repl_apply(node_id, ts) VALUES ('', '')");
        var known = new Dictionary<string, (List<string> Columns, List<string> Keys)>(StringComparer.Ordinal);
        var queue = changes.ToList();
        var stuck = new List<RowFailure>();
        try
        {
            while (queue.Count > 0)
            {
                stuck = [];
                var moved = false;
                foreach (var change in queue)
                {
                    Sql.Exec(conn, tx, "SAVEPOINT repl_row");
                    try
                    {
                        ApplyRow(conn, tx, change, guard, known, result);
                        Sql.Exec(conn, tx, "RELEASE repl_row");
                        moved = true;
                    }
                    catch (SqliteException ex)
                    {
                        Sql.Exec(conn, tx, "ROLLBACK TO repl_row");
                        Sql.Exec(conn, tx, "RELEASE repl_row");
                        stuck.Add(new RowFailure { Change = change, Reason = Reason(ex) });
                    }
                }
                if (!moved || stuck.Count == 0)
                {
                    break;   // круг без единого успеха: остальное отсюда не поправить
                }
                queue = stuck.Select(f => f.Change).ToList();
            }
            Sql.Exec(conn, tx, "DELETE FROM repl_apply");
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        result.Failed.AddRange(stuck);
        return result;
    }

    /// <summary>Причина отказа словами: у SQLite она во внутреннем сообщении
    /// («UNIQUE constraint failed: skills.name»), а не в «SQLite Error 19».</summary>
    private static string Reason(SqliteException ex)
    {
        var message = ex.Message.Trim();
        var colon = message.IndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 && message.StartsWith("SQLite Error", StringComparison.Ordinal)
            ? message[(colon + 2)..].Trim('\'', ' ', '.')
            : message;
    }

    /// <summary>Применить ОДНО изменение (общее тело обоих путей).</summary>
    private static void ApplyRow(SqliteConnection conn, SqliteTransaction tx, RowChange change,
        Func<string, string, Dictionary<string, object?>, bool>? guard,
        Dictionary<string, (List<string> Columns, List<string> Keys)> known, ApplyResult result)
    {
        result.Cursor = Math.Max(result.Cursor, change.Seq);
        if (!known.TryGetValue(change.Table, out var meta))
        {
            meta = (ColumnsOf(conn, change.Table, tx), KeysOf(conn, change.Table, tx));
            known[change.Table] = meta;
        }
        if (meta.Columns.Count == 0 || meta.Keys.Count == 0)
        {
            result.Skipped++;   // такой таблицы у нас нет (разные версии) — не наша беда
            return;
        }
        // СТРАЖ СПРАШИВАЕТСЯ ПЕРВЫМ, до сравнения часов. Для большинства таблиц это
        // безразлично, но не для тех, где «выиграл последний» — неверное правило:
        // ключ API (этап 45) при расхождении значений обязан стать конфликтом
        // и подождать человека, а не молча уступить более поздней записи
        var values = Parse(change.PayloadJson);
        if (guard is not null && !guard(change.Table, change.Pk, values))
        {
            result.Skipped++;
            return;
        }
        var local = Sql.Query(conn, tx,
            "SELECT ts, node_id FROM changes WHERE tbl=@t AND pk=@p ORDER BY seq DESC LIMIT 1",
            r => (Ts: r.S("ts"), Node: r.S("node_id")),
            ("@t", change.Table), ("@p", change.Pk)).FirstOrDefault();
        if (local != default && !IsNewer(change.Ts, change.NodeId, local.Ts, local.Node))
        {
            result.Skipped++;
            // проиграла правка ДРУГОГО узла — это и есть конфликт (ТЗ гл. 6):
            // при раздельном владении такого быть не должно, поэтому его считают
            if (local.Node != change.NodeId)
            {
                result.Conflicts++;
                result.LastConflict = $"{change.Table} {change.Pk}: "
                                      + $"{change.Ts} ({change.NodeId}) < {local.Ts} ({local.Node})";
            }
            return;
        }
        // журнал должен запомнить АВТОРА и ЕГО время — иначе транзитный сервер
        // выдал бы чужую правку за свою и она бы «помолодела»
        Sql.Exec(conn, tx, "UPDATE repl_apply SET node_id=@n, ts=@t",
            ("@n", change.NodeId), ("@t", change.Ts));
        if (change.Op == ChangeOps.Delete)
        {
            Delete(conn, tx, change.Table, meta.Keys, values);
        }
        else
        {
            Upsert(conn, tx, change.Table, meta.Columns, meta.Keys, values);
        }
        result.Applied++;
    }

    /// <summary>«Выиграл последний»: сравнение часов, при равном времени решает узел —
    /// иначе два сервера с одинаковой отметкой времени разошлись бы навсегда.</summary>
    public static bool IsNewer(string ts, string node, string otherTs, string otherNode)
    {
        var byTime = string.CompareOrdinal(ts, otherTs);
        return byTime != 0 ? byTime > 0 : string.CompareOrdinal(node, otherNode) > 0;
    }

    private static void Upsert(SqliteConnection conn, SqliteTransaction tx, string table,
        List<string> columns, List<string> keys, Dictionary<string, object?> values)
    {
        // берём только те колонки, которые есть и у нас, и в payload: версии серверов
        // могут отличаться, и это не повод ронять репликацию
        var present = columns.Where(values.ContainsKey).ToList();
        foreach (var key in keys)
        {
            if (!present.Contains(key))
            {
                throw new InvalidOperationException(
                    Loc.T("msg.changeLog.1", table, key));
            }
        }
        var where = string.Join(" AND ", keys.Select(k => $"{k}=@{k}"));
        var exists = Sql.Scalar<long>(conn, tx, $"SELECT COUNT(*) FROM {table} WHERE {where}",
            keys.Select(k => ("@" + k, values[k])).ToArray()) > 0;
        if (exists)
        {
            // правим только присланные колонки: страж мог оставить лишь часть (например,
            // у СВОЕЙ записи сервера извне правятся только интервалы репликации)
            var updatable = present.Where(c => !keys.Contains(c)).ToList();
            if (updatable.Count == 0)
            {
                return;
            }
            Sql.Exec(conn, tx,
                $"UPDATE {table} SET " + string.Join(", ", updatable.Select(c => $"{c}=@{c}"))
                + " WHERE " + where,
                present.Select(c => ("@" + c, values[c])).ToArray());
            return;
        }
        var sql = new StringBuilder()
            .Append("INSERT INTO ").Append(table).Append(" (").Append(string.Join(", ", present))
            .Append(") VALUES (").Append(string.Join(", ", present.Select(c => "@" + c))).Append(')');
        Sql.Exec(conn, tx, sql.ToString(),
            present.Select(c => ("@" + c, values[c])).ToArray());
    }

    private static void Delete(SqliteConnection conn, SqliteTransaction tx, string table,
        List<string> keys, Dictionary<string, object?> values)
    {
        Sql.Exec(conn, tx,
            $"DELETE FROM {table} WHERE " + string.Join(" AND ", keys.Select(k => $"{k}=@{k}")),
            keys.Select(k => ("@" + k, values.GetValueOrDefault(k))).ToArray());
    }

    // --- триггеры: та самая «одна точка записи» ---

    /// <summary>
    /// ЧАСЫ ОДНОЙ СТРОКИ СТРОГО ВОЗРАСТАЮТ (T-3-S1) — выражение времени для журнала.
    ///
    /// Беда, ради которой это появилось: <c>strftime</c> считает время в МИЛЛИСЕКУНДАХ, а
    /// состав команды и связи задачи сохраняются приёмом «снести все строки и вставить заново»
    /// одной транзакцией. Удаление и следующая за ним вставка ТОЙ ЖЕ строки попадали в одну
    /// миллисекунду и получали одинаковые часы <c>(ts, node_id)</c>. Приёмник сравнивает часы
    /// («выиграл последний») и вставку, равную удалению, отбрасывал как «не новее» — строка
    /// пропадала у партнёра насовсем: на втором сервере часть исполнителей исчезала из состава
    /// команд, а у задач — из назначений, тэгов и навыков.
    ///
    /// Правило теперь такое: новое изменение строки обязано быть СТРОГО ПОЗЖЕ последнего
    /// известного изменения ЭТОЙ ЖЕ строки. Совпало (или часы отступили назад — перевод
    /// времени, правка партнёра из будущего) — берётся предыдущее значение плюс один тик.
    /// Тик кладётся в четыре знака, которыми время и так дополнялось до формата
    /// <c>Sql.ToDb</c> («o», семь знаков после точки против трёх у strftime), поэтому длина
    /// строки не меняется и посимвольное сравнение остаётся верным.
    ///
    /// Часы ЧУЖОЙ правки (идёт применение пачки) не трогаются вовсе: автор и его время
    /// берутся из <c>repl_apply</c>, иначе транзитный сервер выдавал бы чужую правку за свою.
    /// </summary>
    /// <param name="tableExpr">SQL-выражение имени таблицы: литерал или параметр.</param>
    /// <param name="pkExpr">SQL-выражение ключа строки — то же, что пишется в журнал.</param>
    private static string StampSql(string tableExpr, string pkExpr) => $"""
        COALESCE((SELECT ts FROM repl_apply), (SELECT
            CASE WHEN prev IS NULL OR prev < nw THEN nw
                 ELSE substr(prev, 1, 23)
                      || printf('%04d', MIN(CAST(substr(prev, 24, 4) AS INTEGER) + 1, 9999))
                      || 'Z'
            END
            FROM (SELECT strftime('%Y-%m-%dT%H:%M:%f','now') || '0000Z' AS nw,
                         (SELECT ts FROM changes WHERE tbl={tableExpr} AND pk={pkExpr}
                          ORDER BY seq DESC LIMIT 1) AS prev)))
        """;

    private static void InstallTriggers(SqliteConnection conn, string table,
        List<string> columns, List<string> keys)
    {
        var json = "json_object(" + string.Join(", ", columns.Select(c => $"'{c}', new.{c}")) + ")";
        var keyJson = "json_object(" + string.Join(", ", keys.Select(c => $"'{c}', old.{c}")) + ")";
        var newPk = Concat("new", keys);
        var oldPk = Concat("old", keys);
        const string author = """
            COALESCE((SELECT node_id FROM repl_apply), (SELECT value FROM meta WHERE key='node_id'), '')
            """;

        // условие WHEN — «пометка молчания снята» (T-227): под пометкой строка в журнал
        // не пишется вовсе, и запись остаётся сугубо местной
        void Trigger(string suffix, string when, string pk, string op, string payload) =>
            Sql.Exec(conn, null, $"""
                DROP TRIGGER IF EXISTS {TriggerPrefix}{table}_{suffix};
                CREATE TRIGGER {TriggerPrefix}{table}_{suffix} AFTER {when} ON {table}
                WHEN NOT EXISTS (SELECT 1 FROM repl_mute) BEGIN
                  INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json)
                  VALUES ('{table}', {pk}, '{op}', {StampSql($"'{table}'", pk)}, {author}, {payload});
                END;
                """);

        Trigger("ins", "INSERT", newPk, ChangeOps.Upsert, json);
        Trigger("upd", "UPDATE", newPk, ChangeOps.Upsert, json);
        Trigger("del", "DELETE", oldPk, ChangeOps.Delete, keyJson);
    }

    /// <summary>
    /// Первичное наполнение журнала существующими строками: после него «изменения после 0» —
    /// это полный снимок базы, и первичная репликация не требует отдельного протокола.
    /// Часы берём из строки (<c>updated_at</c>), чтобы уже существующие правки не оказались
    /// «сделанными сейчас» и не выиграли LWW у более свежих данных партнёра.
    /// </summary>
    private static void Backfill(SqliteConnection conn, string table,
        List<string> columns, List<string> keys)
    {
        var json = "json_object(" + string.Join(", ", columns.Select(c => $"'{c}', {c}")) + ")";
        var clock = columns.Contains("updated_at") ? "updated_at"
            : columns.Contains("created_at") ? "created_at"
            : columns.Contains("ts") ? "ts"
            : "";
        var stamp = clock.Length > 0
            ? $"COALESCE({clock}, strftime('%Y-%m-%dT%H:%M:%f','now') || '0000Z')"
            : "'0001-01-01T00:00:00.0000000Z'";
        Sql.Exec(conn, null, $"""
            INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json)
            SELECT '{table}', {Concat(null, keys)}, '{ChangeOps.Upsert}', {stamp},
                   (SELECT value FROM meta WHERE key='node_id'), {json}
            FROM {table}
            """);
    }

    /// <summary>Ключ строки для журнала: составной склеивается через <c>|</c>.</summary>
    private static string Concat(string? alias, List<string> keys)
    {
        var prefix = alias is null ? "" : alias + ".";
        return keys.Count == 1
            ? $"CAST({prefix}{keys[0]} AS TEXT)"
            : string.Join(" || '|' || ", keys.Select(k => $"CAST({prefix}{k} AS TEXT)"));
    }

    // --- сведения о таблицах ---

    public static List<string> ColumnsOf(SqliteConnection conn, string table, SqliteTransaction? tx = null) =>
        Sql.Query(conn, tx, $"PRAGMA table_info({Safe(table)})", r => r.S("name"));

    /// <summary>Колонки первичного ключа в его порядке.</summary>
    public static List<string> KeysOf(SqliteConnection conn, string table, SqliteTransaction? tx = null) =>
        Sql.Query(conn, tx, $"PRAGMA table_info({Safe(table)})",
                r => (Name: r.S("name"), Pk: r.L("pk")))
            .Where(c => c.Pk > 0).OrderBy(c => c.Pk).Select(c => c.Name).ToList();

    /// <summary>Имена таблиц приходят из наших же списков, но в SQL они подставляются
    /// текстом — на всякий случай отсекаем всё, кроме букв, цифр и подчёркивания.</summary>
    private static string Safe(string table) =>
        new(table.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    /// <summary>Значения строки из payload: SQLite типизирована динамически, поэтому
    /// достаточно строк, целых, дробных и NULL.</summary>
    public static Dictionary<string, object?> Parse(string payloadJson)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(payloadJson.Length == 0 ? "{}" : payloadJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return values;
        }
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var whole)
                    ? whole
                    : property.Value.GetDouble(),
                JsonValueKind.True => 1L,
                JsonValueKind.False => 0L,
                _ => null,
            };
        }
        return values;
    }
}
