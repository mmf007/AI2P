using System.IO.Compression;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ЯДРО АРХИВАЦИИ (T-40-S0, выпуск 1.105): реестр архивов организации, их состояние на
/// ЭТОМ сервере и четыре операции с файлами — создать, открыть, закрыть, удалить с сервера.
///
/// Назначение архивации — уменьшить оперативный объём задач и всего, что с ними связано:
/// данные переносятся из рабочей среды в архивную и там уже не меняются, их можно только
/// посмотреть либо скопировать обратно. Сам ПЕРЕНОС данных — соседняя задача выпуска
/// (T-42-S0); здесь только хранилище архивов и их жизненный цикл.
///
/// Две вещи, которые здесь важно не спутать:
/// <list type="bullet">
/// <item><b>«текущий»</b> — свойство САМОГО архива: он ровно один, он последний, только
/// в него идёт перенос и только из него возможно восстановление. Едет репликацией
/// (таблица <c>archives</c>), потому что кластер обязан считать текущим один и тот же архив;</item>
/// <item><b>открыт / закрыт / удалён</b> — свойство архива НА КОНКРЕТНОМ СЕРВЕРЕ. Не
/// реплицируется НИКОГДА (таблица <c>archive_states</c> отсутствует в
/// <c>ChangeLog.OrgTables</c>): распаковать архив для просмотра, упаковать обратно
/// или убрать с диска каждый сервер решает сам.</item>
/// </list>
///
/// Раскладка на диске: <c>&lt;каталог организации&gt;/arc/&lt;код&gt;/</c> у открытого архива
/// (внутри — своя база SQLite ТОЙ ЖЕ схемы, что у организации, и подкаталог <c>projects</c>
/// с файлами проектов; то есть каталог архива устроен как каталог организации, и готовые
/// сервисы чтения работают с ним без переделки) и один файл
/// <c>&lt;каталог организации&gt;/arc/&lt;код&gt;.zip</c> у закрытого. Упаковка и распаковка —
/// штатными средствами .NET (<see cref="ZipFile"/> и <see cref="ArchiveExtractor"/>).
/// </summary>
public sealed class ArchiveService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ArchiveService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>
    /// ПРАВИЛА АРХИВАЦИИ (T-41-S0). Ставится снаружи (контекстом организации), потому что
    /// ядру архивации правила не нужны ни для чего, кроме одного шага создания: новый архив
    /// получает правила по выбору человека — общие правила организации, копию правил прежнего
    /// текущего архива или ничего. Не заполнено (в старых тестах ядра) — шаг просто не идёт.
    /// </summary>
    public ArchiveRuleService? Rules { get; set; }

    /// <summary>Каталог всех архивов организации: <c>&lt;каталог организации&gt;/arc</c>.</summary>
    public string ArcDir => Path.Combine(_db.DataDir, Archives.Dir);

    /// <summary>Каталог ОТКРЫТОГО архива с таким кодом (существует он или нет).</summary>
    public string DirOf(string code) => Path.Combine(ArcDir, code);

    /// <summary>Файл ЗАКРЫТОГО архива с таким кодом (существует он или нет).</summary>
    public string ZipOf(string code) => Path.Combine(ArcDir, code + Archives.ZipExt);

    /// <summary>Файл базы внутри открытого архива — той же схемы, что база организации.</summary>
    public string DbFileOf(string code) => Path.Combine(DirOf(code), Archives.DbFile);

    /// <summary>Каталог файлов проектов внутри открытого архива.</summary>
    public string ProjectsDirOf(string code) => Path.Combine(DirOf(code), Archives.ProjectsDir);

    // --- чтение ---

    /// <summary>Все архивы организации — с состоянием НА ЭТОМ СЕРВЕРЕ; новые сверху.</summary>
    public List<Archive> List()
    {
        SettleHalfClosed();
        using var conn = _db.Open();
        var list = Sql.Query(conn, null,
            "SELECT * FROM archives WHERE deleted_at IS NULL ORDER BY created_at DESC", Map);
        foreach (var archive in list)
        {
            Decorate(conn, null, archive);
        }
        return list;
    }

    public Archive? Get(string id)
    {
        using var conn = _db.Open();
        var archive = Load(conn, null, id);
        return archive is null ? null : Decorate(conn, null, archive);
    }

    /// <summary>ТЕКУЩИЙ архив организации — тот, в который идёт перенос; null — архивов ещё нет.</summary>
    public Archive? Current()
    {
        using var conn = _db.Open();
        var archive = Sql.Query(conn, null,
            "SELECT * FROM archives WHERE deleted_at IS NULL AND is_current=1 ORDER BY created_at DESC",
            Map).FirstOrDefault();
        return archive is null ? null : Decorate(conn, null, archive);
    }

    /// <summary>
    /// Состояние архива на ЭТОМ сервере. Отметка в <c>archive_states</c> — главная, но её
    /// может не быть вовсе (архив приехал репликацией, а файлов здесь никто не создавал),
    /// и тогда состояние выводится из файловой системы: есть каталог — открыт, есть .zip —
    /// закрыт, нет ничего — удалён. Отметка, разошедшаяся с диском (человек унёс каталог
    /// руками), тоже уступает диску: показывать надо то, что есть на самом деле.
    /// </summary>
    public string StateOf(string code)
    {
        if (Directory.Exists(DirOf(code)))
        {
            return ArchiveStates.Open;
        }
        return File.Exists(ZipOf(code)) ? ArchiveStates.Closed : ArchiveStates.Deleted;
    }

    // --- создание ---

    /// <summary>
    /// СОЗДАТЬ АРХИВ. Работает ТОЛЬКО на сервере-дирижёре организации: реестр архивов —
    /// её общее имущество, и заводить архив на каждом сервере отдельно значило бы получить
    /// столько «текущих» архивов, сколько в кластере машин.
    ///
    /// Новый архив всегда становится ТЕКУЩИМ, а предыдущий текущий — просто открытым
    /// (закрывать его никто не обязан: закрыть или удалить его теперь можно, а раньше
    /// было нельзя).
    /// </summary>
    /// <param name="rulesMode">Способ переноса правил архивации
    /// (<see cref="ArchiveRuleModes"/>) — выбор человека; сами правила заводит T-41-S0.</param>
    public Archive Create(string code, string name, string rulesMode, string? actorId)
    {
        if (!_scope.IsConductor)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.1"));
        }
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        rulesMode = (rulesMode ?? "").Trim();
        rulesMode = rulesMode.Length == 0 ? ArchiveRuleModes.Common : rulesMode;
        if (!Archives.IsValidCode(code))
        {
            throw new ArgumentException(Loc.T("msg.arc.3"));
        }
        if (name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.arc.5"));
        }
        if (!ArchiveRuleModes.All.Contains(rulesMode))
        {
            throw new ArgumentException(Loc.T("msg.arc.6", rulesMode));
        }

        var now = DateTime.UtcNow;
        var archive = new Archive
        {
            Code = code,
            Name = name,
            RulesMode = rulesMode,
            IsCurrent = true,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using var conn = _db.Open();
        var taken = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM archives WHERE code=@c AND deleted_at IS NULL", ("@c", code));
        if (taken > 0 || Directory.Exists(DirOf(code)) || File.Exists(ZipOf(code)))
        {
            throw new ArgumentException(Loc.T("msg.arc.4", code));
        }

        // КАТАЛОГ ЗАВОДИТСЯ ДО ЗАПИСИ В РЕЕСТР: запись без каталога человеку не починить
        // (архив есть, а открыть нечего), а каталог без записи — просто мусор на диске,
        // который следующая попытка с тем же кодом честно отвергнет
        CreateStorage(code);

        using var tx = conn.BeginTransaction();
        archive.DisplayId = Database.NextDisplayId(conn, tx, "ARC", _scope.Code);
        // прежний текущий перестаёт быть текущим — он остаётся открытым архивом, из которого
        // теперь можно только читать (перенос и восстановление идут в текущий)
        var previous = Sql.Query(conn, tx,
            "SELECT * FROM archives WHERE deleted_at IS NULL AND is_current=1", Map);
        Sql.Exec(conn, tx,
            "UPDATE archives SET is_current=0, updated_at=@now WHERE is_current=1 AND deleted_at IS NULL",
            ("@now", Sql.ToDb(now)));
        Sql.Exec(conn, tx, """
            INSERT INTO archives (id, display_id, code, name, is_current, rules_mode,
                                  created_by, created_at, updated_at)
            VALUES (@id, @did, @code, @name, 1, @rules, @by, @created, @updated)
            """,
            ("@id", archive.Id), ("@did", archive.DisplayId), ("@code", archive.Code),
            ("@name", archive.Name), ("@rules", archive.RulesMode), ("@by", actorId),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        SetState(conn, tx, archive.Id, ArchiveStates.Open, now);
        foreach (var old in previous)
        {
            // прежний текущий у себя на диске распакован (закрыть текущий нельзя было),
            // поэтому отметка состояния у него — «открыт»
            SetState(conn, tx, old.Id, StateOf(old.Code), now);
        }
        SetPresence(conn, tx, archive.Id, has: true, now);
        // ПОРУЧЕНИЕ «СМЕНИ ТЕКУЩИЙ АРХИВ» (T-47-S0) — своей таблицей, а не видом строки
        // в archives: сервер прежней версии разобрал бы незнакомый вид через else-ветку,
        // а строку незнакомой таблицы пропускает молча (наука T-263). Партнёр исполнит его
        // не в момент приезда строки, а в конце сеанса — после того как ПРЕЖНИЙ текущий
        // архив дошлёт свои данные в последний раз
        Sql.Exec(conn, tx, """
            INSERT INTO archive_handovers (id, archive_id, prev_archive_id, created_at, updated_at)
            VALUES (@id, @a, @p, @at, @at)
            """,
            ("@id", Guid.NewGuid().ToString()), ("@a", archive.Id),
            ("@p", previous.FirstOrDefault()?.Id ?? ""), ("@at", Sql.ToDb(now)));
        Append(conn, tx, EventTypes.ArchiveCreated, archive, actorId);
        Append(conn, tx, EventTypes.ArchiveCurrentChanged, archive, actorId);
        tx.Commit();
        // ПРАВИЛА АРХИВАЦИИ нового архива (T-41-S0) — уже после фиксации записи: правила
        // ссылаются на архив, и до commit его для них ещё нет. Способ выбрал человек в форме
        // создания, здесь он только исполняется
        Rules?.Replace(archive.Id, previous.FirstOrDefault()?.Id, rulesMode, actorId);
        return Decorate(conn, null, archive);
    }

    /// <summary>
    /// Каталог архива: своя база ТОЙ ЖЕ схемы, что у организации, и подкаталог файлов
    /// проектов. Схема — обычным <c>Database.Init</c>: архив читается теми же сервисами,
    /// что и рабочая среда, значит и заводиться должен ими же, а не своим скриптом,
    /// который разъедется с организацией на первой же миграции.
    /// </summary>
    private void CreateStorage(string code) => EnsureStorage(code);

    /// <summary>
    /// Завести каталог архива, если его тут ещё нет, и вернуть его базу. Нужно не только
    /// создателю архива: на сервере-ПОЛУЧАТЕЛЕ запись реестра приезжает репликацией, а
    /// каталога с базой нет вовсе — и завести его надо до того, как поедут данные (T-47-S0).
    /// Вызов идемпотентен: <c>Database.Init</c> на существующей базе просто догоняет схему.
    /// </summary>
    public Database EnsureStorage(string code)
    {
        var dir = DirOf(code);
        var file = DbFileOf(code);
        // ГОТОВУЮ базу заводим ОДИН РАЗ на процесс. Database.Init идемпотентен, но недёшев
        // (снимает и ставит заново триггеры журнала), а сюда приходят с каждым куском файла
        // репликации: пересобирать триггеры под идущей записью нельзя
        if (_ready.TryGetValue(file, out var known) && File.Exists(file))
        {
            return known;
        }
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, Archives.ProjectsDir));
        var db = new Database(dir, Archives.DbFile);
        db.Init(_db.NodeId);
        _ready[file] = db;
        return db;
    }

    /// <summary>Базы архивов, уже приведённые к схеме в этом процессе (см. <see cref="EnsureStorage"/>).
    /// Общий на процесс: каталог архива один, а сервисов организации бывает несколько.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Database> _ready
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// АРХИВ, КОТОРЫЙ ЭТОМУ СЕРВЕРУ ПОЛОЖЕНО ИМЕТЬ (T-47-S0) — текущий либо тот, который
    /// текущим вот-вот станет (цель последнего поручения о смене, ещё не исполненного здесь).
    /// Только такой архив репликация вправе завести на диске заново: все прочие человек
    /// удаляет с сервера сам, и возвращать их против его воли нельзя — для этого есть
    /// отдельная кнопка «скачать с другого сервера».
    /// </summary>
    public bool ShouldHost(string archiveId)
    {
        using var conn = _db.Open();
        var current = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM archives WHERE id=@id AND is_current=1 AND deleted_at IS NULL",
            ("@id", archiveId));
        if (current > 0)
        {
            return true;
        }
        var newest = Sql.Scalar<string>(conn, null,
            "SELECT archive_id FROM archive_handovers WHERE deleted_at IS NULL "
            + "ORDER BY created_at DESC, id DESC LIMIT 1");
        return newest == archiveId;
    }

    /// <summary>
    /// База архива для РЕПЛИКАЦИИ (T-47-S0). null означает «данными этот архив здесь
    /// не обменивается»:
    /// <list type="bullet">
    /// <item>закрытый .zip'ом — распаковывать его ради обмена значило бы менять состояние,
    /// которое человек выбрал сам;</item>
    /// <item>удалённый отсюда — кроме случая, когда это текущий (или вот-вот текущий) архив:
    /// именно так текущий архив и приезжает на сервер, где его ещё не было.</item>
    /// </list>
    /// </summary>
    public Database? DbForReplication(Archive archive) => StateOf(archive.Code) switch
    {
        ArchiveStates.Closed => null,
        ArchiveStates.Open => EnsureStorage(archive.Code),
        _ => ShouldHost(archive.Id) ? EnsureStorage(archive.Code) : null,
    };

    // --- открыть / закрыть / удалить (операции ЭТОГО сервера) ---

    /// <summary>
    /// ОТКРЫТЬ АРХИВ — распаковать <c>&lt;код&gt;.zip</c> в каталог <c>&lt;код&gt;/</c>
    /// и убрать .zip. После этого на архив можно переключиться для просмотра (T-45-S0).
    /// </summary>
    public Archive Open(string id, string? actorId)
    {
        var archive = Require(id);
        using var busy = Working(archive.Code);
        var state = StateOf(archive.Code);
        if (state == ArchiveStates.Open)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.9", archive.DisplayId));
        }
        var zip = ZipOf(archive.Code);
        if (state == ArchiveStates.Deleted || !File.Exists(zip))
        {
            throw new InvalidOperationException(Loc.T("msg.arc.11", archive.DisplayId, zip));
        }
        var dir = DirOf(archive.Code);
        if (!ArchiveExtractor.TryExtract(zip, dir, out var error))
        {
            // распаковали наполовину — недоделанный каталог убираем: иначе архив будет
            // числиться открытым, а внутри окажется половина базы
            Delete(dir);
            throw new InvalidOperationException(Loc.T("msg.arc.12", archive.DisplayId,
                error.Length > 0 ? error : Archives.ZipExt));
        }
        // .zip убираем ПОСЛЕ распаковки, и отказ здесь тоже обязан быть словами: у Close
        // такая защита была с самого начала (msg.arc.13), а у Open её не было — на Linux
        // (служба под своей учёткой, каталог с чужими правами) File.Delete кидал IOException
        // мимо всех обработчиков, и человек получал пустое «500:» (T-206-S0)
        try
        {
            File.Delete(zip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.12", archive.DisplayId, ex.Message));
        }
        Mark(archive, ArchiveStates.Open, EventTypes.ArchiveOpened, actorId);
        return archive;
    }

    /// <summary>
    /// ЗАКРЫТЬ АРХИВ — упаковать каталог в <c>&lt;код&gt;.zip</c> и убрать каталог.
    /// ТЕКУЩИЙ архив закрывать нельзя: в него идёт перенос данных.
    /// </summary>
    public Archive Close(string id, string? actorId)
    {
        var archive = Require(id);
        if (archive.IsCurrent)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.7", archive.DisplayId));
        }
        using var busy = Working(archive.Code);
        var state = StateOf(archive.Code);
        if (state == ArchiveStates.Closed)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.10", archive.DisplayId));
        }
        var dir = DirOf(archive.Code);
        if (state == ArchiveStates.Deleted || !Directory.Exists(dir))
        {
            throw new InvalidOperationException(Loc.T("msg.arc.11", archive.DisplayId, dir));
        }
        var zip = ZipOf(archive.Code);
        try
        {
            File.Delete(zip);   // остаток прошлой неудачной попытки
            ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal,
                includeBaseDirectory: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or NotSupportedException)
        {
            Delete(zip);
            throw new InvalidOperationException(Loc.T("msg.arc.13", archive.DisplayId, ex.Message));
        }
        Delete(dir);
        Mark(archive, ArchiveStates.Closed, EventTypes.ArchiveClosed, actorId);
        return archive;
    }

    /// <summary>
    /// ДОВЕСТИ ДО КОНЦА НЕЗАКОНЧЕННОЕ ЗАКРЫТИЕ (T-203-S0). На диске у архива бывает либо
    /// каталог, либо <c>&lt;код&gt;.zip</c>; ОБА СРАЗУ означают, что закрытие не дошло до
    /// конца, и такой архив приходится чинить, а не показывать как открытый:
    /// <list type="bullet">
    /// <item>у заказчика (жалоба T-203-S0) .zip приезжал с партнёра общей репликацией файлов
    /// организации, потому что каталог архивов не был из неё исключён: файл появлялся, а
    /// каталог оставался — уже вычищенный той же репликацией, то есть архив на этом сервере
    /// числился открытым, а внутри у него не было ничего. Приезжать .zip с этой версии
    /// перестал (<c>FileManifest</c>), но у тех, у кого он уже приехал, лежит на диске;</item>
    /// <item>то же состояние остаётся, если <see cref="Close"/> упаковал каталог, а удалить
    /// его не смог — файл был занят просмотром.</item>
    /// </list>
    /// Решение всегда одно: .zip уже собран, значит архив закрыт — каталог убирается,
    /// состояние и наличие отмечаются. Зовётся при открытии организации и при показе списка
    /// архивов: починка нужна тем, у кого беспорядок УЖЕ на диске (наука T-148-S0).
    /// </summary>
    /// <returns>Сколько архивов пришлось дочистить.</returns>
    public int SettleHalfClosed()
    {
        List<Archive> archives;
        using (var conn = _db.Open())
        {
            archives = Sql.Query(conn, null,
                "SELECT * FROM archives WHERE deleted_at IS NULL", Map);
        }
        var fixedUp = 0;
        foreach (var archive in archives)
        {
            var dir = DirOf(archive.Code);
            if (!Directory.Exists(dir) || !File.Exists(ZipOf(archive.Code)))
            {
                continue;
            }
            // ИДУЩАЯ РАСПАКОВКА — ЭТО НЕ НЕЗАКОНЧЕННОЕ ЗАКРЫТИЕ (T-206-S0). Пока Open
            // распаковывает .zip, на диске законно лежат ОБА — и растущий каталог, и сам
            // .zip, который убирается последним шагом. Список архивов при этом обновляется
            // сам (экран настроек опрашивает сервер), а List() зовёт эту починку — и она
            // сносила каталог из-под работающей распаковки. На Windows это было незаметно:
            // Directory.Delete не трогает каталог с открытыми файлами. На Linux открытый
            // файл удалению не мешает, поэтому там распаковка гибла посреди работы —
            // ровно жалоба «на дирижёре (ubuntu) 500, на другом сервере (windows) всё
            // проходит». Свои операции помечают архив занятым, и починка их не трогает
            if (IsBusy(archive.Code))
            {
                continue;
            }
            if (archive.IsCurrent)
            {
                // ТЕКУЩИЙ архив закрытым не бывает: в него идёт перенос, и его каталог
                // репликация заведёт заново. Лишний .zip рядом с ним — не наше дело,
                // убрать его человек может кнопкой «удалить с сервера» и скачать заново
                continue;
            }
            Delete(dir);
            if (Directory.Exists(dir))
            {
                continue;   // каталог занят — попробуем в следующий раз, состояние не врём
            }
            RefreshPresence(archive.Id, archive.Code);
            fixedUp++;
        }
        return fixedUp;
    }

    /// <summary>
    /// УДАЛИТЬ АРХИВ С ЭТОГО СЕРВЕРА — снести каталог либо .zip и отметить состояние
    /// «удалён». ЗАПИСЬ РЕЕСТРА ОСТАЁТСЯ: архив есть на других серверах организации,
    /// и оттуда его можно скачать обратно (T-47-S0). Текущий архив удалять нельзя.
    /// </summary>
    public Archive Remove(string id, string? actorId)
    {
        var archive = Require(id);
        if (archive.IsCurrent)
        {
            throw new InvalidOperationException(Loc.T("msg.arc.8", archive.DisplayId));
        }
        using var busy = Working(archive.Code);
        Delete(DirOf(archive.Code));
        Delete(ZipOf(archive.Code));
        Mark(archive, ArchiveStates.Deleted, EventTypes.ArchiveRemoved, actorId);
        return archive;
    }

    // --- ОТДАЧА АРХИВА СОСЕДУ (T-47-S0) ---

    /// <summary>Временный файл, которым архив отдаётся соседу. Имя нарочно НЕ совпадает
    /// с <c>&lt;код&gt;.zip</c>: тот файл означает «архив здесь закрыт», а этот — просто
    /// свёрток на время передачи.</summary>
    public string ShareZipOf(string code) => Path.Combine(ArcDir, code + ".share" + Archives.ZipExt);

    /// <summary>
    /// СОБРАТЬ АРХИВ ДЛЯ ПЕРЕДАЧИ соседу. Архив всегда уезжает ЗАКРЫТЫМ (.zip), даже если
    /// здесь он открыт: у получателя нет причин наследовать наше состояние, а .zip — самая
    /// дешёвая форма переноса. Закрытый отдаётся как есть, открытый упаковывается во
    /// временный файл. Архива здесь нет — null.
    /// </summary>
    public string? PackForShare(string code)
    {
        var state = StateOf(code);
        if (state == ArchiveStates.Closed)
        {
            return ZipOf(code);
        }
        if (state != ArchiveStates.Open)
        {
            return null;
        }
        var share = ShareZipOf(code);
        Delete(share);   // остаток прошлой передачи: архив мог с тех пор поменяться
        ZipFile.CreateFromDirectory(DirOf(code), share, CompressionLevel.Optimal,
            includeBaseDirectory: false);
        return share;
    }

    /// <summary>Снять временный свёрток после передачи; закрытый архив (свой .zip) не трогаем.</summary>
    public void DropShare(string code) => Delete(ShareZipOf(code));

    /// <summary>Путь, по которому получатель складывает скачанный архив: он ВСЕГДА приходит
    /// закрытым, поэтому это <c>&lt;код&gt;.zip</c>.</summary>
    public string IncomingZipOf(string code) => ZipOf(code);

    // --- ЗАНЯТОСТЬ АРХИВА ФАЙЛОВОЙ ОПЕРАЦИЕЙ (T-206-S0) ---

    /// <summary>
    /// Архивы, с файлами которых прямо сейчас идёт работа этого процесса: ключ — каталог
    /// архивов и код (у разных организаций коды повторяются). Счётчик, а не флаг: операции
    /// с одним архивом человек умеет запускать подряд. Нужно это одному месту —
    /// <see cref="SettleHalfClosed"/>, которое иначе принимает идущую распаковку за
    /// незаконченное закрытие и убирает каталог из-под неё.
    /// </summary>
    private static readonly Dictionary<string, int> BusyCodes = new(StringComparer.Ordinal);

    private string BusyKeyOf(string code) => ArcDir + "|" + code;

    private bool IsBusy(string code)
    {
        lock (BusyCodes)
        {
            return BusyCodes.ContainsKey(BusyKeyOf(code));
        }
    }

    private IDisposable Working(string code) => new BusyScope(BusyKeyOf(code));

    private sealed class BusyScope : IDisposable
    {
        private readonly string _key;

        public BusyScope(string key)
        {
            _key = key;
            lock (BusyCodes)
            {
                BusyCodes[key] = BusyCodes.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        public void Dispose()
        {
            lock (BusyCodes)
            {
                if (!BusyCodes.TryGetValue(_key, out var count))
                {
                    return;
                }
                if (count <= 1)
                {
                    BusyCodes.Remove(_key);
                }
                else
                {
                    BusyCodes[_key] = count - 1;
                }
            }
        }
    }

    // --- внутреннее ---

    private Archive Require(string id) =>
        Get(id) ?? throw new InvalidOperationException(Loc.T("msg.arc.2", id));

    /// <summary>Записать состояние архива на этом сервере и событие о нём.</summary>
    private void Mark(Archive archive, string state, string eventType, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        SetState(conn, tx, archive.Id, state, now);
        // наличие архива ЗДЕСЬ объявляется соседям (T-47-S0): открыть и закрыть их не
        // касается, а вот «удалён с этого сервера» — касается: по этому списку выбирают,
        // откуда архив можно скачать обратно
        SetPresence(conn, tx, archive.Id, state != ArchiveStates.Deleted, now);
        Append(conn, tx, eventType, archive, actorId);
        tx.Commit();
        Decorate(conn, null, archive);
    }

    private void SetState(SqliteConnection conn, SqliteTransaction? tx, string archiveId,
        string state, DateTime now) =>
        Sql.Exec(conn, tx, """
            INSERT INTO archive_states (archive_id, server_id, state, updated_at)
            VALUES (@a, @s, @st, @at)
            ON CONFLICT(archive_id, server_id) DO UPDATE SET state=@st, updated_at=@at
            """,
            ("@a", archiveId), ("@s", _scope.ServerId), ("@st", state), ("@at", Sql.ToDb(now)));

    private static Archive? Load(SqliteConnection conn, SqliteTransaction? tx, string id) =>
        Sql.Query(conn, tx, "SELECT * FROM archives WHERE id=@id AND deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();

    /// <summary>Состояние, путь и объём архива НА ЭТОМ СЕРВЕРЕ — в таблице реестра их нет.</summary>
    private Archive Decorate(SqliteConnection conn, SqliteTransaction? tx, Archive archive)
    {
        archive.State = StateOf(archive.Code);
        archive.Path = archive.State switch
        {
            ArchiveStates.Open => DirOf(archive.Code),
            ArchiveStates.Closed => ZipOf(archive.Code),
            _ => "",
        };
        archive.SizeBytes = archive.State switch
        {
            ArchiveStates.Open => DirSize(archive.Path),
            ArchiveStates.Closed => FileSize(archive.Path),
            _ => 0,
        };
        // отметка на диске главнее записи: человек мог унести каталог руками, и держать
        // после этого «открыт» значило бы предлагать переключиться на пустоту
        var known = Sql.Scalar<string>(conn, tx,
            "SELECT state FROM archive_states WHERE archive_id=@a AND server_id=@s",
            ("@a", archive.Id), ("@s", _scope.ServerId));
        if (known != archive.State)
        {
            SetState(conn, tx, archive.Id, archive.State, DateTime.UtcNow);
            SetPresence(conn, tx, archive.Id, archive.State != ArchiveStates.Deleted,
                DateTime.UtcNow);
        }
        // СЕРВЕРЫ, У КОТОРЫХ АРХИВ ЕСТЬ (T-43-S0, T-47-S0): экран показывает их в списке
        // архивов, и по ним же выбирается, откуда качать удалённый здесь архив. Берётся это
        // из РЕПЛИЦИРУЕМОЙ таблицы archive_servers, а не из локальной archive_states: своё
        // наличие каждый сервер объявляет сам, и только так соседи узнают, где архив лежит.
        // Своя строка при этом приводится в соответствие с диском тут же, выше
        archive.Servers = Sql.Query(conn, tx, """
            SELECT server_id FROM archive_servers
            WHERE archive_id=@a AND has_copy=1 AND deleted_at IS NULL ORDER BY server_id
            """, r => r.S("server_id"), ("@a", archive.Id));
        return archive;
    }

    // --- ГДЕ АРХИВ ЕСТЬ (T-47-S0) ---

    /// <summary>
    /// Объявить соседям, есть ли архив на ЭТОМ сервере. Строка своя у каждого сервера
    /// (образец <c>ai_model_servers</c>) и реплицируется — в отличие от <c>archive_states</c>,
    /// где лежит «открыт здесь или закрыт»: это соседей не касается.
    /// </summary>
    private void SetPresence(SqliteConnection conn, SqliteTransaction? tx, string archiveId,
        bool has, DateTime now) =>
        Sql.Exec(conn, tx, """
            INSERT INTO archive_servers (id, archive_id, server_id, has_copy, created_at, updated_at)
            VALUES (@id, @a, @s, @h, @at, @at)
            ON CONFLICT(archive_id, server_id) DO UPDATE SET has_copy=@h, updated_at=@at,
                                                             deleted_at=NULL
            """,
            ("@id", archiveId + "|" + _scope.ServerId), ("@a", archiveId), ("@s", _scope.ServerId),
            ("@h", has ? 1 : 0), ("@at", Sql.ToDb(now)));

    // --- СМЕНА ТЕКУЩЕГО АРХИВА (T-47-S0) ---

    /// <summary>
    /// Поручения «смени текущий архив», ещё НЕ исполненные для этого партнёра. Отметка своя
    /// на каждого партнёра: пока он не получил последний досыл прежнего текущего архива,
    /// поручение для него не исполнено (ТЗ: сначала досылает, потом перестаёт быть текущим).
    /// Порядок — по времени: смен могло накопиться несколько.
    /// </summary>
    public List<ArchiveHandover> PendingHandovers(string peerId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT h.* FROM archive_handovers h
            WHERE h.deleted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM archive_handovers_applied a
                              WHERE a.handover_id=h.id AND a.peer_id=@p)
            ORDER BY h.created_at
            """, r => new ArchiveHandover
        {
            Id = r.S("id"),
            ArchiveId = r.S("archive_id"),
            PrevArchiveId = r.S("prev_archive_id"),
            CreatedAt = r.Dt("created_at"),
        }, ("@p", peerId));
    }

    /// <summary>
    /// ИСПОЛНИТЬ смену текущего архива: названный архив становится текущим, все остальные —
    /// нет (прежний текущий превращается в обычный ОТКРЫТЫЙ архив). Зовётся в конце сеанса
    /// репликации — после того как прежний текущий дошлёт свои данные в последний раз.
    /// Идемпотентно: на сервере, где архив и создавали, текущий уже проставлен.
    /// </summary>
    public void ApplyHandover(ArchiveHandover handover, string peerId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var exists = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM archives WHERE id=@id AND deleted_at IS NULL",
            ("@id", handover.ArchiveId));
        if (exists > 0)
        {
            Sql.Exec(conn, tx, """
                UPDATE archives SET is_current=CASE WHEN id=@id THEN 1 ELSE 0 END, updated_at=@at
                WHERE deleted_at IS NULL AND is_current<>CASE WHEN id=@id THEN 1 ELSE 0 END
                """, ("@id", handover.ArchiveId), ("@at", Sql.ToDb(now)));
        }
        Sql.Exec(conn, tx, """
            INSERT OR REPLACE INTO archive_handovers_applied (handover_id, peer_id, applied_at)
            VALUES (@h, @p, @at)
            """, ("@h", handover.Id), ("@p", peerId), ("@at", Sql.ToDb(now)));
        tx.Commit();
    }

    /// <summary>Отметить поручение исполненным для партнёра, не трогая признака «текущий»:
    /// так закрываются смены, случившиеся ДО первого сеанса с этим партнёром, — досылать
    /// ему нетекущие архивы прошлых лет незачем (реплицируется только текущий).</summary>
    public void SkipHandover(ArchiveHandover handover, string peerId)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT OR REPLACE INTO archive_handovers_applied (handover_id, peer_id, applied_at)
            VALUES (@h, @p, @at)
            """, ("@h", handover.Id), ("@p", peerId), ("@at", Sql.ToDb(DateTime.UtcNow)));
    }

    /// <summary>
    /// Привести к диску и ОБЪЯВИТЬ СОСЕДЯМ наличие архива на этом сервере (T-47-S0).
    /// Зовётся после скачивания архива и после операций с файлами: строка <c>archive_servers</c>
    /// реплицируется, и без неё сосед не узнает, что архив у нас появился или исчез.
    /// </summary>
    public void RefreshPresence(string archiveId, string code)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        SetState(conn, tx, archiveId, StateOf(code), now);
        SetPresence(conn, tx, archiveId, StateOf(code) != ArchiveStates.Deleted, now);
        tx.Commit();
    }

    private static long DirSize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Убрать каталог или файл, если он есть; отсутствие — не ошибка.</summary>
    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // файл занят просмотром архива — состояние всё равно отметим: повторное
            // удаление доберёт остаток, а молчаливый отказ человеку ничего не объяснит
        }
    }

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        Archive archive, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "archive",
            EntityId = archive.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                archive.DisplayId,
                archive.Code,
                archive.Name,
                archive.IsCurrent,
            }),
        });

    private static Archive Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Code = r.S("code"),
        Name = r.S("name"),
        IsCurrent = r.B("is_current"),
        RulesMode = r.S("rules_mode"),
        CreatedBy = r.SN("created_by"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
