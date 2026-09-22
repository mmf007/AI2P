using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ОБЪЕКТЫ ПРОЕКТА (ТЗ пп. 2.5–2.6; T-259, версия 1.91) — персонажи, локации, реквизит,
/// стиль, эталонные кадры и адаптеры LoRA, а по ТЗ п. 2.6 ещё и файлы, оборудование и
/// программы с MCP-интерфейсом.
///
/// Устройство намеренно повторяет задачи, а не справочники: у объекта есть проект, номер
/// (<c>OBJ-7</c>), иерархия и своя группа тэгов. Так и должно быть — объект это не строка
/// настройки, а рабочее содержимое проекта, которое человек заводит и правит десятками.
///
/// СЕРВЕР-ВЛАДЕЛЕЦ (T-102-S0) — всё по образцу задач (ТЗ гл. 6): у объекта есть свой сервер,
/// правится объект только на нём, а остальным серверам он едет обычным журналом изменений и
/// виден только для чтения. Передаётся объект кнопкой «сменить сервер»
/// (<see cref="ChangeServer"/>) и передаётся ВСЕМ ПОДДЕРЕВОМ: датасеты и кадры — это дети
/// объекта, и половина дерева, оставшаяся у прежнего сервера, была бы нередактируемой.
/// До этой версии сервера у объекта не было вовсе и правил его дирижёр — то есть объект
/// принадлежал РОЛИ, а не компьютеру, и смена дирижёра уводила его на другую машину.
///
/// ЧЕГО ЗДЕСЬ НЕТ И ПОЧЕМУ:
/// <list type="bullet">
/// <item><b>отдельной таблицы файлов</b> — эталонный кадр это такой же объект, только
/// вида <c>image</c> и с родителем; вторая сущность была бы копией первой;</item>
/// <item><b>справочника тэгов</b> — как у задач (T-222): тэг заводится тем, что его
/// написали в форме, и исчезает, когда его не осталось ни у одного объекта.</item>
/// </list>
/// </summary>
public sealed class ObjectService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ObjectService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>Кто мы в кластере (ТЗ гл. 6): нужен API и живым проверкам.</summary>
    public ServerScope Scope => _scope;

    /// <summary>Сервер-владелец нового объекта — ЭТОТ сервер (T-102-S0, правило T-1-S0):
    /// «без сервера» означало бы «у того, кто сегодня дирижёр». Пусто — записи о локальном
    /// сервере ещё нет (её заводит старт приложения).</summary>
    private string? DefaultOwner() => _scope.ServerId is { Length: > 0 } id ? id : null;

    /// <summary>Подставить сервер-владельца в выдачу наружу (код, имя, «только чтение»):
    /// в таблице этих полей нет, а списку и карточке они нужны.</summary>
    private ObjectItem Decorate(ObjectItem item)
    {
        item.ServerCode = _scope.CodeOf(item.ServerId);
        item.ServerName = _scope.NameOf(item.ServerId);
        item.IsReadOnly = !_scope.CanWrite(item.ServerId);
        return item;
    }

    private List<ObjectItem> Decorate(List<ObjectItem> items)
    {
        foreach (var item in items)
        {
            Decorate(item);
        }
        return items;
    }

    /// <summary>Объект правится на этом сервере: свой сервер либо дирижёр у бесхозного.</summary>
    public bool CanWrite(ObjectItem item) => _scope.CanWrite(item.ServerId);

    /// <summary>Проверка владения перед записью; чужой объект — понятная ошибка (ТЗ гл. 6).</summary>
    public void EnsureCanWrite(ObjectItem item)
    {
        if (CanWrite(item))
        {
            return;
        }
        _scope.EnsureCanWrite(item.ServerId, Loc.T("msg.object.13", item.DisplayId));
    }

    /// <summary>
    /// Объекты проекта. <paramref name="projectId"/> обязателен: общего списка объектов
    /// у организации нет — «у каждого проекта свой список». Порядок — по номеру ЧИСЛОМ
    /// (<see cref="DisplayIds.Order"/>), а не строкой: иначе OBJ-10 стояло бы раньше OBJ-2.
    /// </summary>
    /// <param name="tags">Отбор по тэгам (по ИЛИ, как в фильтре задач); пусто — все.</param>
    /// <param name="search">Подстрока названия или паспорта; пусто — все.</param>
    /// <param name="kinds">Отбор по ВИДАМ объекта (T-276): несколько видов — по ИЛИ, как
    /// тэги; пусто — все. Неизвестные коды сюда не доезжают (<see cref="ObjectKinds.Parse"/>),
    /// поэтому отбор по опечатке не превращается в пустой список.</param>
    public List<ObjectItem> List(string projectId, IEnumerable<string>? tags = null,
        string? search = null, bool includeInactive = true, IEnumerable<string>? kinds = null)
    {
        using var conn = _db.Open();
        var where = new List<string> { "o.project_id=@p", "o.deleted_at IS NULL" };
        var args = new List<(string, object?)> { ("@p", projectId) };
        if (!includeInactive)
        {
            where.Add("o.is_active=1");
        }
        var tagList = TaskTags.Normalize(tags?.ToList());
        if (tagList.Count > 0)
        {
            var names = tagList.Select((_, i) => "@tag" + i).ToList();
            where.Add("EXISTS (SELECT 1 FROM object_tags g WHERE g.object_id=o.id AND g.tag IN ("
                      + string.Join(", ", names) + "))");
            for (var i = 0; i < tagList.Count; i++)
            {
                args.Add(("@tag" + i, tagList[i]));
            }
        }
        // отбор по видам (T-276): виды перечислены явно, поэтому в SQL уезжают параметрами,
        // а не склейкой строки — вид приходит из запроса браузера
        var kindList = ObjectKinds.Parse(kinds is null ? null : string.Join(',', kinds));
        if (kindList.Count > 0)
        {
            var names = kindList.Select((_, i) => "@kind" + i).ToList();
            where.Add("o.type IN (" + string.Join(", ", names) + ")");
            for (var i = 0; i < kindList.Count; i++)
            {
                args.Add(("@kind" + i, kindList[i]));
            }
        }
        if (search is { Length: > 0 } text)
        {
            where.Add("(o.name LIKE @s OR o.description LIKE @s OR o.path_or_url LIKE @s)");
            args.Add(("@s", "%" + text.Trim() + "%"));
        }
        var items = Sql.Query(conn, null,
            "SELECT * FROM objects o WHERE " + string.Join(" AND ", where), Map, args.ToArray());
        // тэги и число детей — одним запросом на весь список: у проекта объектов бывают
        // сотни, и запрос на каждую строку превратил бы открытие списка в «подождите»
        LoadTagsFor(conn, items);
        LoadChildCounts(conn, projectId, items);
        return Decorate(items.OrderBy(o => DisplayIds.Order(o.DisplayId)).ToList());
    }

    /// <summary>Объект по идентификатору — вместе с тэгами и числом детей.</summary>
    public ObjectItem? Get(string id)
    {
        using var conn = _db.Open();
        var item = Sql.Query(conn, null, "SELECT * FROM objects WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (item is null)
        {
            return null;
        }
        LoadTagsFor(conn, [item]);
        item.ChildCount = (int)Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM objects WHERE parent_id=@id AND deleted_at IS NULL", ("@id", id));
        return Decorate(item);
    }

    /// <summary>
    /// Объект по НОМЕРУ либо по НАЗВАНИЮ в пределах проекта — то, чем разрешается ссылка
    /// <c>@obj:…</c> из описания задачи (<see cref="ObjectRefs"/>). Номер ищется точно,
    /// название — без учёта регистра: человек пишет ссылку руками.
    /// </summary>
    public ObjectItem? Resolve(string projectId, string reference)
    {
        var key = reference.Trim();
        if (key.Length == 0)
        {
            return null;
        }
        using var conn = _db.Open();
        // СРАВНЕНИЕ РЕГИСТРА — В C#, А НЕ В SQL. COLLATE NOCASE у SQLite сворачивает только
        // латиницу (A–Z): «ночная улица» не нашла бы «Ночную улицу», и ссылка по названию
        // работала бы через раз — на английских названиях да, на русских нет
        var all = Sql.Query(conn, null,
            "SELECT * FROM objects WHERE project_id=@p AND deleted_at IS NULL", Map,
            ("@p", projectId));
        var item = all.FirstOrDefault(o => string.Equals(o.DisplayId, key,
                                          StringComparison.CurrentCultureIgnoreCase))
                   ?? all.FirstOrDefault(o => string.Equals(o.Name, key,
                                          StringComparison.CurrentCultureIgnoreCase));
        if (item is null)
        {
            return null;
        }
        LoadTagsFor(conn, [item]);
        return Decorate(item);
    }

    /// <summary>Дети объекта — его внутренний список (эталонные кадры персонажа).</summary>
    public List<ObjectItem> Children(string parentId)
    {
        using var conn = _db.Open();
        var items = Sql.Query(conn, null,
            "SELECT * FROM objects WHERE parent_id=@id AND deleted_at IS NULL", Map, ("@id", parentId));
        LoadTagsFor(conn, items);
        return Decorate(items.OrderBy(o => DisplayIds.Order(o.DisplayId)).ToList());
    }

    // --- ДАТАСЕТЫ ОБЪЕКТА (T-274, версия 1.98) ---
    //
    // Датасет — это дочерний объект вида «датасет», а кадры обучения — его дети. До T-274
    // кадры были прямыми детьми объекта, и у персонажа со своими логическими детьми
    // иерархия превращалась в кашу: полсотни картинок вперемешку с осмысленными объектами.
    // Датасетов у объекта бывает несколько — у разных моделей разные требования к кадрам, —
    // и один из них ТЕКУЩИЙ: в него ложатся новые кадры, по нему идёт обучение.

    /// <summary>Датасеты объекта в порядке номеров; <see cref="ObjectItem.ChildCount"/> —
    /// сколько в каждом кадров.</summary>
    public List<ObjectItem> Datasets(string objectId)
    {
        using var conn = _db.Open();
        var rows = Sql.Query(conn, null, """
            SELECT * FROM objects
            WHERE parent_id=@id AND type=@kind AND deleted_at IS NULL
            """, Map, ("@id", objectId), ("@kind", ObjectKinds.Dataset));
        foreach (var row in rows)
        {
            row.ChildCount = (int)Sql.Scalar<long>(conn, null,
                "SELECT COUNT(*) FROM objects WHERE parent_id=@id AND deleted_at IS NULL",
                ("@id", row.Id));
        }
        return Decorate(rows.OrderBy(o => DisplayIds.Order(o.DisplayId)).ToList());
    }

    /// <summary>
    /// Завести датасет и СРАЗУ сделать его текущим: заводят его затем, чтобы сложить туда
    /// кадры, и «завёл, а кадры уехали в прежний» было бы ловушкой.
    /// </summary>
    /// <param name="name">Название; пусто — умолчание из словаря.</param>
    /// <param name="limits">Настройки контроля картинок — снимок общих настроек приложения
    /// либо то, что подставили из модели.</param>
    public ObjectItem CreateDataset(string objectId, string? name, LoraDatasetLimits limits,
        string? actorId)
    {
        var owner = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        var wanted = (name ?? "").Trim();
        if (wanted.Length == 0)
        {
            wanted = Loc.T("msg.lora.28");
        }
        var dataset = Create(new ObjectItem
        {
            ProjectId = owner.ProjectId,
            ParentId = owner.Id,
            Type = ObjectKinds.Dataset,
            // название объекта уникально в пределах ПРОЕКТА, а «датасет» — самое ожидаемое
            // название на свете: у второго персонажа оно занято, поэтому к нему добавляется
            // код объекта, и только если и это занято — номер
            Name = UniqueName(wanted, owner),
            Description = "",
            DatasetJson = limits.Sane().ToJson(),
        }, actorId);
        SetCurrentDataset(objectId, dataset.Id, actorId);
        return dataset;
    }

    /// <summary>
    /// ТЕКУЩИЙ ДАТАСЕТ ОБЪЕКТА, а нет ни одного — завести его умолчанием. Это и есть
    /// «при вводе и редактировании автоматически создавать дочерний объект типа датасет»
    /// из задания: отдельной кнопки «создайте датасет, прежде чем добавить кадр» быть не
    /// должно, первый датасет заводится сам.
    /// </summary>
    public ObjectItem EnsureDataset(string objectId, LoraDatasetLimits limits, string? actorId)
    {
        var owner = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        var datasets = Datasets(objectId);
        // текущий, если он ещё жив; иначе первый по номеру; иначе заводим
        var current = datasets.FirstOrDefault(d => d.Id == owner.CurrentDatasetId);
        if (current is not null)
        {
            return current;
        }
        if (datasets.Count > 0)
        {
            SetCurrentDataset(objectId, datasets[0].Id, actorId);
            return datasets[0];
        }
        var created = CreateDataset(objectId, null, limits, actorId);
        // ПЕРВЫЙ датасет забирает себе кадры, лежавшие прямыми детьми объекта (до версии
        // 1.98 они лежали именно так). Без этого редактор, открытый на таком объекте, завёл
        // бы пустой датасет и показал бы пустой список — прежние кадры исчезли бы с экрана,
        // хотя они на месте. Разом их переносит шаг обновления билда 98, но полагаться на
        // него нельзя: объект приезжает и репликацией с сервера, который ещё не обновили
        AdoptDirectFrames(objectId, created.Id);
        return Get(created.Id) ?? created;
    }

    /// <summary>Перевесить кадры, лежащие прямыми детьми объекта, на его датасет.</summary>
    private void AdoptDirectFrames(string objectId, string datasetId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE objects SET parent_id=@d, updated_at=@u
            WHERE parent_id=@o AND type=@image AND deleted_at IS NULL
            """,
            ("@d", datasetId), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@o", objectId),
            ("@image", ObjectKinds.Image));
        tx.Commit();
    }

    /// <summary>
    /// Выбрать текущий датасет. Правится одним UPDATE, а не сохранением всей записи: объект
    /// в это же время могли открыть в форме, и переписывать его целиком нельзя.
    /// </summary>
    public ObjectItem SetCurrentDataset(string objectId, string? datasetId, string? actorId)
    {
        var owner = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        EnsureCanWrite(owner);
        var id = (datasetId ?? "").Trim();
        if (id.Length > 0)
        {
            var dataset = Get(id) ?? throw new ArgumentException(Loc.T("msg.lora.29"));
            // чужой датасет текущим не станет: кадры ушли бы не тому объекту, а обучение
            // пошло бы по картинкам другого персонажа — и заметно это стало бы только
            // на готовом ролике
            if (dataset.ParentId != objectId || dataset.Type != ObjectKinds.Dataset)
            {
                throw new ArgumentException(Loc.T("msg.lora.29"));
            }
        }
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            Sql.Exec(conn, tx,
                "UPDATE objects SET current_dataset_id=@d, updated_at=@u WHERE id=@id",
                ("@d", id.Length > 0 ? id : null), ("@u", Sql.ToDb(DateTime.UtcNow)),
                ("@id", objectId));
            Append(conn, tx, owner, EventTypes.ObjectUpdated, actorId);
            tx.Commit();
        }
        return Get(objectId)!;
    }

    /// <summary>Записать настройки контроля картинок датасета — тем же приёмом «одно поле,
    /// одним UPDATE».</summary>
    public ObjectItem SetDatasetLimits(string datasetId, LoraDatasetLimits limits, string? actorId)
    {
        var dataset = Get(datasetId) ?? throw new ArgumentException(Loc.T("msg.lora.29"));
        if (dataset.Type != ObjectKinds.Dataset)
        {
            throw new ArgumentException(Loc.T("msg.lora.29"));
        }
        EnsureCanWrite(dataset);
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            Sql.Exec(conn, tx, "UPDATE objects SET dataset_json=@j, updated_at=@u WHERE id=@id",
                ("@j", limits.Sane().ToJson()), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", datasetId));
            Append(conn, tx, dataset, EventTypes.ObjectUpdated, actorId);
            tx.Commit();
        }
        return Get(datasetId)!;
    }

    /// <summary>
    /// КАДРЫ, ПО КОТОРЫМ ИДЁТ ОБУЧЕНИЕ. Датасет назван — его дети; не назван — текущий
    /// датасет объекта.
    ///
    /// А если датасетов у объекта нет ВОВСЕ, кадрами считаются прямые дети объекта: так
    /// датасет лежал до версии 1.98, и объект, заведённый прежней версией и не открытый
    /// с тех пор в редакторе, обязан обучаться по-прежнему. Разово переносит такие кадры
    /// в датасет шаг обновления билда 98, но полагаться на него нельзя: объекты приезжают
    /// и репликацией с сервера, который ещё не обновили.
    /// </summary>
    public List<ObjectItem> DatasetFrames(string objectId, string? datasetId = null)
    {
        var owner = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        var id = (datasetId ?? "").Trim();
        if (id.Length == 0)
        {
            id = owner.CurrentDatasetId ?? "";
        }
        if (id.Length == 0)
        {
            var datasets = Datasets(objectId);
            if (datasets.Count > 0)
            {
                id = datasets[0].Id;
            }
        }
        var parent = id.Length > 0 ? id : objectId;
        // ЗАПИСИ (T-250-S0) — такие же файлы датасета, как кадры: у звукового датасета
        // дети имеют вид «эталонная запись», и не взять их значило бы показать пустой
        // датасет. Прочие виды (вложенные датасеты, адаптеры) по-прежнему не берутся
        return Children(parent)
            .Where(c => c.Type is ObjectKinds.Image or ObjectKinds.Audio)
            .ToList();
    }

    /// <summary>
    /// РАЗОВЫЙ ПЕРЕНОС НАКОПЛЕННЫХ КАДРОВ В ДАТАСЕТ (T-274, шаг обновления билда 98).
    /// Берётся только объект-АДАПТЕР (<see cref="ObjectKinds.Lora"/>), у которого есть
    /// прямые дети-кадры и нет ни одного датасета: у персонажа и локации прямые
    /// эталонные кадры — это их собственные картинки (T-259/T-258), и трогать их нельзя.
    ///
    /// Идемпотентен: объект, у которого датасет уже есть, пропускается — при повторном
    /// старте находить нечего. Возвращает число объектов, которым завели датасет.
    /// </summary>
    /// <param name="defaultName">Название датасета умолчанием — на языке установки.</param>
    public int MoveLegacyFramesToDatasets(string defaultName)
    {
        List<ObjectItem> candidates;
        using (var conn = _db.Open())
        {
            candidates = Sql.Query(conn, null, """
                SELECT * FROM objects o
                WHERE o.type=@lora AND o.deleted_at IS NULL
                  AND EXISTS (SELECT 1 FROM objects f
                              WHERE f.parent_id=o.id AND f.type=@image AND f.deleted_at IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM objects d
                                  WHERE d.parent_id=o.id AND d.type=@dataset AND d.deleted_at IS NULL)
                """, Map, ("@lora", ObjectKinds.Lora), ("@image", ObjectKinds.Image),
                ("@dataset", ObjectKinds.Dataset));
        }
        var fixedUp = 0;
        foreach (var owner in candidates)
        {
            if (!CanWrite(owner))
            {
                continue;   // чужой объект чинит его собственный сервер (T-102-S0)
            }
            // настройки контроля картинок у перенесённого датасета — умолчания: чем на
            // самом деле резали эти кадры, теперь уже не узнать, а подставить их из модели
            // человек может одной кнопкой
            var dataset = CreateDataset(owner.Id, defaultName, new LoraDatasetLimits(), null);
            AdoptDirectFrames(owner.Id, dataset.Id);
            fixedUp++;
        }
        return fixedUp;
    }

    /// <summary>
    /// РАЗОВЫЙ ПЕРЕНОС КАДРОВ ДАТАСЕТОВ В ХРАНИЛИЩЕ ОРГАНИЗАЦИИ (T-98-S0, шаг обновления
    /// билда 112).
    ///
    /// До этой версии подрезанные кадры ложились в ПАПКУ ПРОЕКТА, а она у каждого сервера
    /// своя и не реплицируется: на соседнем сервере датасет выглядел пустым. Теперь кадры
    /// живут в каталоге данных организации (<see cref="ObjectFiles.DatasetDirRel"/>) и
    /// уезжают партнёру вместе с остальными файлами организации.
    ///
    /// Файл КОПИРУЕТСЯ, а не переносится: папка проекта принадлежит человеку, и удалять
    /// оттуда что-либо шагом обновления мы не вправе. Кадр, файла которого на этом сервере
    /// нет (папки проекта здесь может не быть вовсе), НЕ ТРОГАЕТСЯ — иначе путь указывал бы
    /// на пустое место. Шаг идемпотентен: кадр, уже лежащий в хранилище, пропускается.
    /// </summary>
    /// <returns>Число перенесённых кадров.</returns>
    public int MoveFramesToStore(FileStore files, ProjectService projects)
    {
        var moved = 0;
        foreach (var project in projects.List(includeDeleted: true))
        {
            List<ObjectItem> frames;
            using (var conn = _db.Open())
            {
                frames = Sql.Query(conn, null, """
                    SELECT f.* FROM objects f
                    JOIN objects d ON d.id = f.parent_id AND d.type=@dataset AND d.deleted_at IS NULL
                    WHERE f.project_id=@p AND f.type=@image AND f.deleted_at IS NULL
                    """, Map, ("@p", project.Id), ("@image", ObjectKinds.Image),
                    ("@dataset", ObjectKinds.Dataset));
            }
            foreach (var frame in frames)
            {
                var path = frame.PathOrUrl.Trim();
                if (path.Length == 0 || ObjectFiles.IsStore(path)
                    || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (ProjectFiles.Resolve(project.FolderPath, path) is not { } source
                    || !File.Exists(source))
                {
                    continue;
                }
                if (frame.ParentId is not { Length: > 0 } datasetId || Get(datasetId) is not { } dataset
                    || dataset.ParentId is not { Length: > 0 } ownerId || Get(ownerId) is not { } owner)
                {
                    continue;
                }
                var rel = ObjectFiles.DatasetDirRel(project.Slug, owner.DisplayId, dataset.DisplayId)
                          + "/" + Path.GetFileName(source);
                var abs = files.Abs(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                File.Copy(source, abs, overwrite: true);
                using var write = _db.Open();
                Sql.Exec(write, null,
                    "UPDATE objects SET path_or_url=@p, updated_at=@u WHERE id=@id",
                    ("@p", ObjectFiles.Store(rel)), ("@u", Sql.ToDb(DateTime.UtcNow)),
                    ("@id", frame.Id));
                moved++;
            }
        }
        return moved;
    }

    /// <summary>
    /// Название, свободное в проекте: сначала как попросили, потом с кодом объекта-хозяина,
    /// дальше с номером. Отказывать в заведении датасета из-за занятого названия нельзя —
    /// «датасет» занят у любого второго персонажа, и человек тут ни при чём.
    /// </summary>
    private string UniqueName(string wanted, ObjectItem owner)
    {
        using var conn = _db.Open();
        var taken = Sql.Query(conn, null,
            "SELECT name FROM objects WHERE project_id=@p AND deleted_at IS NULL",
            r => r.S("name"), ("@p", owner.ProjectId));
        bool Free(string name) =>
            !taken.Any(t => string.Equals(t, name, StringComparison.CurrentCultureIgnoreCase));
        if (Free(wanted))
        {
            return wanted;
        }
        var withCode = $"{wanted} {owner.DisplayId}";
        if (Free(withCode))
        {
            return withCode;
        }
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{withCode} {i}";
            if (Free(candidate))
            {
                return candidate;
            }
        }
        return $"{withCode} {Guid.NewGuid().ToString("N")[..6]}";
    }

    /// <summary>
    /// Все тэги объектов ПРОЕКТА (T-259) — то, что предлагается в форме и в фильтре.
    /// Группа своя, отдельная от тэгов задач: набор — DISTINCT по <c>object_tags</c>
    /// живых объектов этого проекта.
    /// </summary>
    public List<string> ListTags(string projectId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT DISTINCT g.tag FROM object_tags g
            JOIN objects o ON o.id = g.object_id
            WHERE o.project_id=@p AND o.deleted_at IS NULL
            """, r => r.S("tag"), ("@p", projectId))
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ObjectItem Create(ObjectItem item, string? actorId)
    {
        Validate(item);
        var now = DateTime.UtcNow;
        item.CreatedAt = now;
        item.UpdatedAt = now;
        // СЕРВЕР-ВЛАДЕЛЕЦ (T-102-S0): у ребёнка — тот же, что у родителя (датасет и его кадры
        // принадлежат объекту и уезжают вместе с ним), у корневого — этот сервер. Названный
        // явно не трогаем: так объект приезжает репликацией и так его заводит перенос
        if (item.ParentId is { Length: > 0 } parentId && Get(parentId) is { } parentItem)
        {
            // завести кадр или субобъект внутри ЧУЖОГО объекта нельзя: он уехал бы к нему
            // в поддерево и здесь стал бы нередактируемым сразу после заведения
            EnsureCanWrite(parentItem);
            if (item.ServerId is not { Length: > 0 })
            {
                item.ServerId = parentItem.ServerId;
            }
        }
        if (item.ServerId is not { Length: > 0 })
        {
            item.ServerId = DefaultOwner();
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureParent(conn, tx, item);
        EnsureUniqueName(conn, tx, item);
        // номер объекта с кодом сервера (как у проектов): без суффикса два сервера завели бы
        // по объекту OBJ-5, и ссылка @obj:OBJ-5 указывала бы на разные вещи. У дирижёра
        // суффикса нет — его номера исторически идут без кода
        item.DisplayId = Database.NextDisplayId(conn, tx, "OBJ",
            _scope.IsConductor && (_scope.IsMine(item.ServerId) || item.ServerId is not { Length: > 0 })
                ? ""
                : _scope.CodeOf(item.ServerId));
        Sql.Exec(conn, tx, """
            INSERT INTO objects (id, display_id, project_id, parent_id, server_id, name, type,
                                 path_or_url,
                                 description, meta_json, is_active, lora_path, lora_status,
                                 current_dataset_id, dataset_json,
                                 transport, command_or_url, tools_cache_json, rules_json,
                                 created_at, updated_at)
            VALUES (@id, @num, @project, @parent, @server, @name, @type, @path, @descr, @meta,
                    @active,
                    @lora, @loraState, @dataset, @datasetJson,
                    @transport, @command, @tools, @rules, @created, @updated)
            """,
            ("@server", item.ServerId),
            ("@dataset", item.CurrentDatasetId), ("@datasetJson", item.DatasetJson),
            ("@id", item.Id), ("@num", item.DisplayId), ("@project", item.ProjectId),
            ("@parent", item.ParentId), ("@name", item.Name), ("@type", item.Type),
            ("@path", item.PathOrUrl), ("@descr", item.Description), ("@meta", item.MetaJson),
            ("@active", item.IsActive ? 1 : 0), ("@lora", item.LoraPath),
            ("@loraState", item.LoraStatus), ("@transport", item.Transport),
            ("@command", item.CommandOrUrl), ("@tools", item.ToolsCacheJson),
            ("@rules", item.RulesJson),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        SaveTags(conn, tx, item);
        Append(conn, tx, item, EventTypes.ObjectCreated, actorId);
        tx.Commit();
        return item;
    }

    public ObjectItem Update(ObjectItem item, string? actorId)
    {
        Validate(item);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var existing = Sql.Query(conn, tx, "SELECT * FROM objects WHERE id=@id", Map, ("@id", item.Id))
            .FirstOrDefault() ?? throw new ArgumentException(Loc.T("msg.object.6"));
        // ЧУЖОЙ ОБЪЕКТ ЗДЕСЬ НЕ ПРАВИТСЯ (T-102-S0, ТЗ гл. 6): у строки один писатель
        EnsureCanWrite(existing);
        // проект объекта не меняется правкой формы: вместе с ним пришлось бы переносить
        // детей и рвать ссылки @obj: из описаний задач прежнего проекта
        item.ProjectId = existing.ProjectId;
        // сервер-владелец формой не меняется — для этого есть ChangeServer
        item.ServerId = existing.ServerId;
        // ТЕКУЩИЙ ДАТАСЕТ И ЕГО НАСТРОЙКИ формой объекта не правятся вовсе (T-274): их
        // ведёт редактор LoRA своими вызовами, и правит он их, пока форма объекта открыта.
        // Приняв их из формы, мы возвращали бы датасет к тому, что прочитали до правки, —
        // ровно та же беда, из-за которой состояние обучения живёт своей строкой
        item.CurrentDatasetId = existing.CurrentDatasetId;
        item.DatasetJson = existing.DatasetJson;
        EnsureParent(conn, tx, item);
        EnsureUniqueName(conn, tx, item);
        Sql.Exec(conn, tx, """
            UPDATE objects SET parent_id=@parent, name=@name, type=@type, path_or_url=@path,
                               description=@descr, meta_json=@meta, is_active=@active,
                               lora_path=@lora, lora_status=@loraState, transport=@transport,
                               command_or_url=@command, tools_cache_json=@tools,
                               rules_json=@rules, updated_at=@updated
            WHERE id=@id
            """,
            ("@parent", item.ParentId), ("@name", item.Name), ("@type", item.Type),
            ("@path", item.PathOrUrl), ("@descr", item.Description), ("@meta", item.MetaJson),
            ("@active", item.IsActive ? 1 : 0), ("@lora", item.LoraPath),
            ("@loraState", item.LoraStatus), ("@transport", item.Transport),
            ("@command", item.CommandOrUrl), ("@tools", item.ToolsCacheJson),
            ("@rules", item.RulesJson), ("@updated", Sql.ToDb(now)), ("@id", item.Id));
        // тэги переписываются целиком (как у задачи): снятый в форме тэг обязан исчезнуть
        Sql.Exec(conn, tx, "DELETE FROM object_tags WHERE object_id=@id", ("@id", item.Id));
        SaveTags(conn, tx, item);
        Append(conn, tx, item, EventTypes.ObjectUpdated, actorId);
        tx.Commit();
        item.UpdatedAt = now;
        item.DisplayId = existing.DisplayId;
        return Decorate(item);
    }

    /// <summary>
    /// ПЕРЕНОС ОБЪЕКТА ПО ИЕРАРХИИ (T-266) — то же, что <c>TaskService.ChangeParent</c> у
    /// задач: перетаскивание строки в представлении «иерархия». <paramref name="parentId"/>
    /// пуст — объект становится корневым.
    ///
    /// Отдельный метод, а не <see cref="Update"/> с новым родителем, нужен по той же причине,
    /// что и у задач: перенос меняет ОДНО поле и не должен зависеть от того, что лежит в
    /// форме. Пришедший из представления объект нёс бы с собой весь свой паспорт, тэги и
    /// состояние LoRA — и любой перенос переписывал бы их значениями списка, а список
    /// читался задолго до переноса.
    ///
    /// Проверки родителя — те же, что в форме (<see cref="EnsureParent"/>): тот же проект,
    /// не сам объект и не его потомок. Уникальность названия здесь не проверяется вовсе:
    /// название переносом не меняется, а вот повторная проверка отказала бы в переносе
    /// объекта, чей тёзка завёлся раньше него (данные могли приехать репликацией).
    /// </summary>
    public ObjectItem ChangeParent(string id, string? parentId, string? actorId)
    {
        var now = DateTime.UtcNow;
        // соединение закрывается ДО чтения готовой строки (Get открывает своё): держать
        // открытым соединение с только что закрытой транзакцией незачем
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            var item = Sql.Query(conn, tx,
                           "SELECT * FROM objects WHERE id=@id AND deleted_at IS NULL", Map, ("@id", id))
                           .FirstOrDefault()
                       ?? throw new ArgumentException(Loc.T("msg.object.6"));
            EnsureCanWrite(item);   // чужой объект переносить нельзя (T-102-S0)
            if (parentId is { Length: 0 })
            {
                parentId = null;
            }
            if (parentId != item.ParentId)   // бросили туда, где объект и был, — делать нечего
            {
                item.ParentId = parentId;
                EnsureParent(conn, tx, item);
                Sql.Exec(conn, tx,
                    "UPDATE objects SET parent_id=@parent, updated_at=@updated WHERE id=@id",
                    ("@parent", item.ParentId), ("@updated", Sql.ToDb(now)), ("@id", id));
                Append(conn, tx, item, EventTypes.ObjectUpdated, actorId);
            }
            tx.Commit();
        }
        return Get(id)!;
    }

    /// <summary>
    /// СМЕНА СЕРВЕРА-ВЛАДЕЛЬЦА ОБЪЕКТА (T-102-S0, кнопка «сменить сервер» карточки объекта) —
    /// то же, что <c>TaskService.ChangeServer</c> у задач. Доступна на сервере, которому
    /// объект принадлежит сейчас, и на дирижёре — если сервера у объекта нет вовсе. Запись
    /// делает ПРЕЖНИЙ владелец: единственный писатель строки сохраняется, а новый владелец
    /// принимает её после ближайшего цикла репликации.
    ///
    /// Уезжает ВСЁ ПОДДЕРЕВО объекта: его субобъекты, датасеты и кадры датасетов — это его
    /// содержимое, и половина дерева, оставшаяся у прежнего сервера, была бы там
    /// нередактируемой (то же правило, что у шаблонов задач, T-36-S0). Дети, принадлежащие
    /// сейчас другому серверу, не трогаются — они уже чужие.
    ///
    /// Номер объекта НЕ меняется: на него ссылаются описания задач (<c>@obj:OBJ-3</c>).
    /// </summary>
    /// <param name="serverId">Новый владелец; пусто — ошибка: объект без сервера не остаётся
    /// (правило T-1-S0 — «без сервера» значит «у того, кто сегодня дирижёр»).</param>
    public ObjectItem ChangeServer(string id, string? serverId, string? actorId)
    {
        var now = DateTime.UtcNow;
        using (var conn = _db.Open())
        {
            using var tx = conn.BeginTransaction();
            var item = Sql.Query(conn, tx,
                           "SELECT * FROM objects WHERE id=@id AND deleted_at IS NULL", Map, ("@id", id))
                           .FirstOrDefault()
                       ?? throw new ArgumentException(Loc.T("msg.object.6"));
            EnsureCanWrite(item);
            if (serverId is not { Length: > 0 })
            {
                throw new ArgumentException(Loc.T("msg.task.32"));
            }
            if (serverId == item.ServerId)
            {
                tx.Commit();
                return Decorate(item);
            }
            var from = item.ServerId;
            Sql.Exec(conn, tx, """
                WITH RECURSIVE sub(id) AS (
                    SELECT id FROM objects WHERE id=@id
                    UNION ALL
                    SELECT o.id FROM objects o JOIN sub ON o.parent_id=sub.id
                     WHERE o.deleted_at IS NULL
                )
                UPDATE objects SET server_id=@s, updated_at=@u
                 WHERE id IN (SELECT id FROM sub) AND deleted_at IS NULL
                   AND (server_id=@from OR (@from IS NULL AND server_id IS NULL))
                """,
                ("@s", serverId), ("@u", Sql.ToDb(now)), ("@id", id), ("@from", from));
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                ProjectId = item.ProjectId,
                EventType = EventTypes.ObjectUpdated,
                EntityType = "object",
                EntityId = item.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    item.DisplayId,
                    fromServer = _scope.CodeOf(from),
                    toServer = _scope.CodeOf(serverId),
                }),
            });
            tx.Commit();
        }
        return Get(id)!;
    }

    /// <summary>
    /// ОБЪЕКТОВ БЕЗ СЕРВЕРА НЕ ОСТАЁТСЯ (T-102-S0) — сторож при открытии организации, тот же,
    /// что <c>TaskService.ClaimTasksWithoutServer</c> у задач. Дирижёр записывает себя
    /// владельцем явно: он и так сегодня правит бесхозные строки, но при смене дирижёра они
    /// уехали бы на другой компьютер. Сторож, а не разовый шаг обновления: бесхозные объекты
    /// продолжают приезжать от партнёра прежней версии. На рядовом сервере не делает ничего.
    /// </summary>
    /// <returns>Сколько объектов получили владельца.</returns>
    public int ClaimObjectsWithoutServer()
    {
        if (!_scope.IsConductor || _scope.ServerId is not { Length: > 0 } me)
        {
            return 0;
        }
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var count = (int)Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM objects WHERE server_id IS NULL AND deleted_at IS NULL");
        if (count == 0)
        {
            tx.Commit();
            return 0;
        }
        Sql.Exec(conn, tx, """
            UPDATE objects SET server_id=@me, updated_at=@u
            WHERE server_id IS NULL AND deleted_at IS NULL
            """, ("@me", me), ("@u", Sql.ToDb(DateTime.UtcNow)));
        tx.Commit();
        return count;
    }

    /// <summary>
    /// Мягкое удаление. Объект с детьми не удаляется: иначе его эталонные кадры остались
    /// бы в проекте без родителя и без способа их найти — сначала удаляют или переносят их.
    /// </summary>
    public void Delete(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var item = Sql.Query(conn, tx, "SELECT * FROM objects WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (item is null || item.DeletedAt is not null)
        {
            return;
        }
        EnsureCanWrite(item);   // чужой объект удаляется на своём сервере (T-102-S0)
        var children = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM objects WHERE parent_id=@id AND deleted_at IS NULL", ("@id", id));
        if (children > 0)
        {
            throw new ArgumentException(Loc.T("msg.object.5", children));
        }
        Sql.Exec(conn, tx, "UPDATE objects SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        Append(conn, tx, item, EventTypes.ObjectDeleted, actorId);
        tx.Commit();
    }

    /// <summary>
    /// ПОДСТАНОВКА ОБЪЕКТОВ В ТЕКСТ (T-259) — то, ради чего объекты и заводились: ссылка
    /// <c>@obj:OBJ-3</c> в описании задачи превращается в дословный паспорт персонажа и
    /// пути его эталонных файлов. Зовётся при запуске задания (медиа-коннектор и сборка
    /// задания текстовому агенту), поэтому правка паспорта действует на следующий же кадр —
    /// переписывать описания задач не нужно.
    ///
    /// <paramref name="kindName"/> переводит код вида на язык читателя; null — вид
    /// подставляется кодом. Сам сервис языка не знает: тексты справочников живут в i18n,
    /// а язык у медиа-модели и у агента разный (T-190).
    /// </summary>
    public string Expand(string? projectId, string? text, Func<string, string>? kindName = null)
    {
        if (projectId is not { Length: > 0 } || text is null
            || !text.Contains(ObjectRefs.Prefix, StringComparison.Ordinal))
        {
            return text ?? "";
        }
        return ObjectRefs.Expand(text, key =>
        {
            if (Resolve(projectId, key) is not { } item)
            {
                return null;
            }
            // файлы объекта — свой и всех детей: персонаж описывается не одной картинкой,
            // и в промпт должны попасть все его эталоны, а не только первый
            var files = new List<string> { item.PathOrUrl };
            var children = Children(item.Id).Where(c => c.IsActive).ToList();
            files.AddRange(children.Where(c => c.Type != ObjectKinds.Dataset).Select(c => c.PathOrUrl));
            // кадры ТЕКУЩЕГО датасета (T-274): с версии 1.98 эталонные кадры лежат не прямыми
            // детьми объекта, а детьми датасета, и без этого прохода промпт объекта, у которого
            // датасет уже заведён, остался бы вовсе без картинок. Датасет берётся один —
            // текущий: их у объекта несколько, и вываливать в промпт все значило бы смешать
            // кадры, собранные под разные модели
            var current = children.FirstOrDefault(c => c.Type == ObjectKinds.Dataset
                                                       && c.Id == item.CurrentDatasetId)
                          ?? children.FirstOrDefault(c => c.Type == ObjectKinds.Dataset);
            if (current is not null)
            {
                files.AddRange(Children(current.Id).Where(c => c.IsActive).Select(c => c.PathOrUrl));
            }
            return new ObjectRefs.ObjectCard(item.DisplayId, item.Name,
                kindName?.Invoke(item.Type) ?? item.Type, item.Description, files);
        });
    }

    /// <summary>
    /// ЧТО УЙДЁТ В МОДЕЛЬ ПРИ ОБУЧЕНИИ (T-99-S0) — только у объекта-АДАПТЕРА; у остальных
    /// пусто, обучать нечего.
    ///
    /// Данные тут другие, чем при использовании, и в этом весь смысл второго поля формы:
    /// в промпт генерации уходит паспорт со ссылками на файлы, а тренер получает КАДРЫ
    /// ТЕКУЩЕГО ДАТАСЕТА, и у каждого кадра — его подпись (<c>LoraTrainService.PrepareDataset</c>
    /// кладёт подпись рядом с картинкой одноимённым <c>.txt</c> либо одним <c>captions.json</c>).
    /// Кадр без подписи уезжает в обучение пустой подписью — и это ровно то, что человек
    /// должен увидеть ДО того, как запустит счёт на часы.
    ///
    /// Слов-подписей в тексте нет намеренно — по той же причине, что и в
    /// <see cref="ObjectRefs.ObjectCard.ToPrompt"/>: язык читателя здесь неизвестен.
    /// </summary>
    public string TrainPrompt(string objectId)
    {
        var item = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        if (item.Type != ObjectKinds.Lora)
        {
            return "";
        }
        var datasets = Datasets(objectId);
        var current = datasets.FirstOrDefault(d => d.Id == item.CurrentDatasetId)
                      ?? datasets.FirstOrDefault();
        var sb = new System.Text.StringBuilder();
        if (current is not null)
        {
            sb.Append(current.DisplayId).Append(' ').Append(current.Name).Append('\n');
        }
        // кадры берутся тем же вызовом, что и обучение (включая откат «датасетов нет —
        // кадрами считаются прямые дети», T-274): показывать надо ровно то, что уедет
        foreach (var frame in DatasetFrames(objectId).Where(f => f.IsActive))
        {
            sb.Append('\n').Append(frame.PathOrUrl.Trim());
            if (frame.Description.Trim() is { Length: > 0 } caption)
            {
                sb.Append('\n').Append(caption);
            }
        }
        return sb.ToString().Trim();
    }

    // --- ОБУЧЕНИЕ АДАПТЕРА ПОД МОДЕЛЬ (T-12-S1, вкладка «Модели» редактора LoRA) ---

    /// <summary>
    /// Запись справочника моделей по её идентификатору. Подставляется контекстом организации
    /// (как <c>ActivationGuard</c> у справочника моделей): справочник — не дело этого сервиса,
    /// но список обучений обязан показывать НОМЕР и КОД модели, а не голый uuid, и говорить,
    /// работает ли модель с LoRA сегодня, а не в день, когда строку завели.
    /// </summary>
    public Func<string, AiModel?>? ModelResolver { get; set; }

    /// <summary>Номер задачи по её идентификатору (T-157-S0): строка обучения показывает
    /// ссылку на задачу, которая её обучает. Не задан — ссылка остаётся без номера.</summary>
    public Func<string, string?>? TaskResolver { get; set; }

    /// <summary>Строки обучения объекта — по одной на модель, в порядке заведения.</summary>
    public List<ObjectLoraModel> LoraModels(string objectId)
    {
        using var conn = _db.Open();
        var rows = Sql.Query(conn, null,
            "SELECT * FROM object_loras WHERE object_id=@o AND deleted_at IS NULL ORDER BY created_at",
            MapLora, ("@o", objectId));
        foreach (var row in rows)
        {
            Describe(row);
        }
        return rows;
    }

    /// <summary>
    /// СТРОКА ОБУЧЕНИЯ ПО ЗАДАЧЕ, КОТОРАЯ ЕЁ ОБУЧАЕТ (T-157-S0) — обратный поиск, которым
    /// коннектор «авто ПО» узнаёт, что за работу ему дали. Ровно из-за него у задачи нет и не
    /// нужно ни одного своего поля с параметрами запуска: объект, датасет, модель и настройка
    /// <c>lora.train</c> лежат здесь, а разбирать текст описания не приходится вовсе.
    /// </summary>
    public ObjectLoraModel? LoraModelByTrainTask(string taskId)
    {
        if (taskId.Trim().Length == 0)
        {
            return null;
        }
        using var conn = _db.Open();
        var row = Sql.Query(conn, null,
                "SELECT * FROM object_loras WHERE train_task_id=@t AND deleted_at IS NULL",
                MapLora, ("@t", taskId))
            .FirstOrDefault();
        if (row is not null)
        {
            Describe(row);
        }
        return row;
    }

    /// <summary>
    /// Запомнить (или забыть — пустым значением) задачу обучения. Правится ОДНО поле прямым
    /// UPDATE: обучение идёт часами, и переписывать строку целиком по дороге нельзя — её
    /// состояние в это же время пишет сам ход обучения.
    /// </summary>
    public ObjectLoraModel? SetLoraTrainTask(string loraId, string? taskId)
    {
        using (var conn = _db.Open())
        {
            Sql.Exec(conn, null,
                "UPDATE object_loras SET train_task_id=@t, updated_at=@u WHERE id=@id",
                ("@t", taskId ?? ""), ("@u", Sql.ToDb(DateTime.UtcNow)), ("@id", loraId));
        }
        return LoraModel(loraId);
    }

    /// <summary>Строка обучения по идентификатору; null — нет такой или снята.</summary>
    public ObjectLoraModel? LoraModel(string id)
    {
        using var conn = _db.Open();
        var row = Sql.Query(conn, null,
                "SELECT * FROM object_loras WHERE id=@id AND deleted_at IS NULL", MapLora, ("@id", id))
            .FirstOrDefault();
        if (row is not null)
        {
            Describe(row);
        }
        return row;
    }

    /// <summary>
    /// Завести обучение адаптера под модель. Пара «объект + модель» одна: обучать один и тот
    /// же адаптер дважды под одни веса незачем — для этого есть переобучение той же строки.
    /// Отмеченная снятой строка при повторном заведении ОЖИВАЕТ: иначе у объекта копились бы
    /// невидимые строки, а уникальность пары мешала бы завести модель заново.
    /// </summary>
    public ObjectLoraModel AddLoraModel(string objectId, string modelId, string? actorId)
    {
        var item = Get(objectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        EnsureCanWrite(item);   // обучение чужого объекта заводится на его сервере (T-102-S0)
        if (modelId.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.object.11"));
        }
        if (ModelResolver?.Invoke(modelId) is null)
        {
            throw new ArgumentException(Loc.T("msg.object.11"));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var existing = Sql.Query(conn, tx,
                "SELECT * FROM object_loras WHERE object_id=@o AND model_id=@m", MapLora,
                ("@o", objectId), ("@m", modelId))
            .FirstOrDefault();
        if (existing is not null)
        {
            if (existing.DeletedAt is null)
            {
                throw new ArgumentException(Loc.T("msg.object.12"));
            }
            Sql.Exec(conn, tx, """
                UPDATE object_loras SET deleted_at=NULL, status='none', path='', error='',
                                        dataset_id=NULL, started_at=NULL, finished_at=NULL,
                                        updated_at=@u
                WHERE id=@id
                """, ("@u", Sql.ToDb(now)), ("@id", existing.Id));
            tx.Commit();
            return LoraModel(existing.Id)!;
        }
        var row = new ObjectLoraModel
        {
            ObjectId = objectId,
            ModelId = modelId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Sql.Exec(conn, tx, """
            INSERT INTO object_loras (id, object_id, model_id, status, path, error,
                                      created_at, updated_at)
            VALUES (@id, @o, @m, 'none', '', '', @c, @u)
            """,
            ("@id", row.Id), ("@o", objectId), ("@m", modelId),
            ("@c", Sql.ToDb(now)), ("@u", Sql.ToDb(now)));
        Append(conn, tx, item, EventTypes.ObjectUpdated, actorId);
        tx.Commit();
        Describe(row);
        return row;
    }

    /// <summary>
    /// Снять строку обучения. Мягко, как и всё остальное: строка едет партнёру журналом
    /// изменений, и «её просто не стало» он бы не отличил от «она ко мне не доехала».
    /// Файл обученного адаптера при этом НЕ удаляется — его могли уже подставить в модель.
    /// </summary>
    public void DeleteLoraModel(string id, string? actorId)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var row = Sql.Query(conn, tx, "SELECT * FROM object_loras WHERE id=@id", MapLora, ("@id", id))
            .FirstOrDefault();
        if (row is null || row.DeletedAt is not null)
        {
            return;
        }
        var now = DateTime.UtcNow;
        var item = Sql.Query(conn, tx, "SELECT * FROM objects WHERE id=@id", Map, ("@id", row.ObjectId))
            .FirstOrDefault();
        if (item is not null)
        {
            EnsureCanWrite(item);   // строка обучения чужого объекта снимается у него (T-102-S0)
        }
        Sql.Exec(conn, tx, "UPDATE object_loras SET deleted_at=@d, updated_at=@d WHERE id=@id",
            ("@d", Sql.ToDb(now)), ("@id", id));
        if (item is not null)
        {
            Append(conn, tx, item, EventTypes.ObjectUpdated, actorId);
        }
        tx.Commit();
    }

    /// <summary>
    /// Записать состояние обучения. Зовётся из обучающего сервиса, в том числе ФОНОМ, пока
    /// человек правит ту же форму, — поэтому правятся ровно четыре поля состояния и ничего
    /// больше: перезаписывать строку целиком значило бы затирать её тем, что прочитали до
    /// начала обучения.
    /// </summary>
    /// <param name="datasetId">Датасет, по которому обучали (T-274); null — не менять.
    /// Проставляется вместе с удачным концом обучения: строка обязана помнить, ЧЕМ получен
    /// лежащий в ней файл, иначе следующий запуск подменит датасет молча.</param>
    public ObjectLoraModel? SetLoraModelState(string id, string status, string? path = null,
        string? error = null, string? datasetId = null)
    {
        if (!LoraModelStates.IsKnown(status))
        {
            status = LoraModelStates.None;
        }
        var now = DateTime.UtcNow;
        using (var conn = _db.Open())
        {
            var exists = Sql.Query(conn, null, "SELECT * FROM object_loras WHERE id=@id", MapLora,
                ("@id", id)).FirstOrDefault();
            if (exists is null)
            {
                return null;
            }
            // время начала ставится при переходе В обучение, время конца — при выходе из него:
            // иначе список показывал бы «идёт 0 минут» у давно упавшего обучения
            var started = status == LoraModelStates.Training ? now : exists.StartedAt;
            var finished = status is LoraModelStates.Ready or LoraModelStates.Error
                ? now
                : status == LoraModelStates.Training ? null : exists.FinishedAt;
            Sql.Exec(conn, null, """
                UPDATE object_loras SET status=@s, path=@p, error=@e, dataset_id=@ds,
                                        started_at=@sa, finished_at=@fa, updated_at=@u
                WHERE id=@id
                """,
                ("@s", status), ("@p", path ?? exists.Path), ("@e", error ?? ""),
                ("@ds", datasetId ?? (exists.DatasetId.Length > 0 ? exists.DatasetId : null)),
                ("@sa", started is null ? null : Sql.ToDb(started.Value)),
                ("@fa", finished is null ? null : Sql.ToDb(finished.Value)),
                ("@u", Sql.ToDb(now)), ("@id", id));
        }
        return LoraModel(id);
    }

    /// <summary>
    /// Обученный адаптер стал ТЕКУЩИМ адаптером объекта: путь и состояние в самой записи
    /// объекта — это то, что читает подстановка адаптера в модель при генерации. Правятся
    /// два поля прямым UPDATE, а не через <see cref="Update"/>: обучение идёт часами, и
    /// записывать по дороге весь объект целиком нельзя — его правят в это же время.
    /// </summary>
    public void SetObjectLoraFile(string objectId, string path)
    {
        using var conn = _db.Open();
        Sql.Exec(conn, null,
            "UPDATE objects SET lora_path=@p, lora_status=@s, updated_at=@u WHERE id=@id",
            ("@p", path), ("@s", LoraStates.Ready), ("@u", Sql.ToDb(DateTime.UtcNow)),
            ("@id", objectId));
    }

    /// <summary>Подставить в строку то, что живёт не в ней: номер, код и сегодняшнюю
    /// настройку LoRA у записи справочника моделей.</summary>
    private void Describe(ObjectLoraModel row)
    {
        // название запомненного датасета (T-274): переспрос «обучали по „датасет 768“,
        // а сейчас выбран „датасет 1024“» без названий не переспрос, а загадка
        if (row.DatasetId.Length > 0)
        {
            row.DatasetName = Get(row.DatasetId)?.Name ?? "";
        }
        // номер задачи обучения (T-157-S0): в списке состояние «обучается» — ссылка, а ссылку
        // с голым uuid человеку показывать нечего. Задачи — не дело сервиса объектов, отсюда
        // делегат, как ModelResolver
        if (row.TrainTaskId.Length > 0 && TaskResolver is { } tasks)
        {
            row.TrainTaskDisplayId = tasks(row.TrainTaskId) ?? "";
        }
        if (ModelResolver?.Invoke(row.ModelId) is not { } model)
        {
            return;
        }
        row.ModelDisplayId = model.DisplayId;
        row.ModelName = model.Name;
        row.LoraSupported = model.Lora.Supported;
        row.LoraReason = model.Lora.Reason;
    }

    private static ObjectLoraModel MapLora(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        ObjectId = r.S("object_id"),
        ModelId = r.S("model_id"),
        Status = r.S("status"),
        Path = r.S("path"),
        Error = r.S("error"),
        DatasetId = r.SN("dataset_id") ?? "",
        TrainTaskId = r.SN("train_task_id") ?? "",
        StartedAt = r.DtN("started_at"),
        FinishedAt = r.DtN("finished_at"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };

    private static void Validate(ObjectItem item)
    {
        item.Name = item.Name.Trim();
        item.Type = item.Type.Trim();
        item.PathOrUrl = item.PathOrUrl.Trim();
        item.LoraPath = item.LoraPath.Trim();
        item.LoraStatus = item.LoraStatus.Trim();
        if (item.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.object.1"));
        }
        // объект без проекта потерялся бы навсегда: общего списка объектов нет, а показывают
        // их только во вкладке проекта — ровно та же ловушка, что у подзадачи чужого проекта
        if (item.ProjectId is not { Length: > 0 })
        {
            throw new ArgumentException(Loc.T("msg.object.2"));
        }
        if (!ObjectKinds.IsKnown(item.Type))
        {
            throw new ArgumentException(Loc.T("msg.object.3", item.Type));
        }
        if (!LoraStates.IsKnown(item.LoraStatus))
        {
            item.LoraStatus = LoraStates.None;
        }
        // МЕДИАРЕСУРС ОБЯЗАН ССЫЛАТЬСЯ ОТНОСИТЕЛЬНЫМ ПУТЁМ (T-111-S0): либо путь в папке
        // проекта, либо store:-путь каталога данных. Абсолютный путь в монтажном листе
        // убивает переносимость по кластеру — у соседа тот же ролик лежит по другому пути,
        // и собранный проект редактора у него не открывается. Проверка стоит здесь, а не
        // в медиатеке, ровно затем, чтобы её нельзя было обойти мимо формы — через API,
        // инструмент агента (T-113-S0) или шлюз плагина
        if (item.Type == ObjectKinds.Media && !MediaLibrary.IsValidPath(item.PathOrUrl))
        {
            throw new ArgumentException(Loc.T("msg.media.1", item.PathOrUrl));
        }
        try
        {
            JsonDocument.Parse(item.MetaJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException(Loc.T("msg.object.4"));
        }
    }

    /// <summary>
    /// Родитель обязан лежать в ТОМ ЖЕ проекте и не может быть самим объектом или его
    /// потомком: иначе список проекта показывал бы чужих детей, а цикл «родитель сам себе
    /// предок» подвесил бы обход дерева.
    /// </summary>
    private static void EnsureParent(SqliteConnection conn, SqliteTransaction tx, ObjectItem item)
    {
        if (item.ParentId is not { Length: > 0 } parentId)
        {
            item.ParentId = null;
            return;
        }
        if (parentId == item.Id)
        {
            throw new ArgumentException(Loc.T("msg.object.7"));
        }
        var parentProject = Sql.Scalar<string>(conn, tx,
            "SELECT project_id FROM objects WHERE id=@id AND deleted_at IS NULL", ("@id", parentId));
        if (parentProject is null)
        {
            throw new ArgumentException(Loc.T("msg.object.8"));
        }
        if (parentProject != item.ProjectId)
        {
            throw new ArgumentException(Loc.T("msg.object.9"));
        }
        // подъём по цепочке родителей: длина дерева объектов невелика, но защита от цикла
        // обязана быть — один раз замкнув её правкой формы, список уже не открыть
        var current = parentId;
        var seen = new HashSet<string>(StringComparer.Ordinal) { item.Id };
        while (current is { Length: > 0 } && seen.Add(current))
        {
            if (current == item.Id)
            {
                throw new ArgumentException(Loc.T("msg.object.7"));
            }
            current = Sql.Scalar<string>(conn, tx,
                "SELECT parent_id FROM objects WHERE id=@id", ("@id", current));
        }
        if (current is { Length: > 0 })
        {
            throw new ArgumentException(Loc.T("msg.object.7"));
        }
    }

    /// <summary>
    /// Название объекта уникально в пределах проекта (без учёта регистра): по названию
    /// разрешается ссылка <c>@obj:[Герой Вася]</c>, и двух «Героев Вась» в одном проекте
    /// быть не должно — иначе в промпт попадал бы случайный из них.
    /// </summary>
    private static void EnsureUniqueName(SqliteConnection conn, SqliteTransaction tx, ObjectItem item)
    {
        // сравнение регистра — в C# (см. Resolve): COLLATE NOCASE у SQLite сворачивает
        // только латиницу, и «герой вася» рядом с «Герой Вася» прошёл бы как другое название
        var taken = Sql.Query(conn, tx,
                "SELECT name FROM objects WHERE project_id=@p AND id<>@id AND deleted_at IS NULL",
                r => r.S("name"), ("@p", item.ProjectId), ("@id", item.Id))
            .Any(name => string.Equals(name, item.Name, StringComparison.CurrentCultureIgnoreCase));
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.object.10", item.Name));
        }
    }

    private static void SaveTags(SqliteConnection conn, SqliteTransaction tx, ObjectItem item)
    {
        // правило приведения тэгов одно на всю систему (T-222): пустые отброшены,
        // повторы сняты без учёта регистра, первое написание побеждает
        item.Tags = TaskTags.Normalize(item.Tags);
        foreach (var tag in item.Tags)
        {
            Sql.Exec(conn, tx, "INSERT OR IGNORE INTO object_tags (object_id, tag) VALUES (@o, @g)",
                ("@o", item.Id), ("@g", tag));
        }
    }

    /// <summary>Тэги сразу всем объектам списка — один запрос вместо запроса на строку.</summary>
    private static void LoadTagsFor(SqliteConnection conn, IReadOnlyList<ObjectItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        var byId = items.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var item in items)
        {
            item.Tags = [];
        }
        // порядок тэгов один и тот же при каждом чтении (ORDER BY tag) — иначе список
        // в форме «дрожал» бы между открытиями, как это было у задач до T-222
        var names = items.Select((_, i) => "@o" + i).ToList();
        var args = items.Select((o, i) => ("@o" + i, (object?)o.Id)).ToArray();
        var rows = Sql.Query(conn, null,
            "SELECT object_id, tag FROM object_tags WHERE object_id IN ("
            + string.Join(", ", names) + ") ORDER BY tag",
            r => (Id: r.S("object_id"), Tag: r.S("tag")), args);
        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Id, out var item))
            {
                item.Tags.Add(row.Tag);
            }
        }
    }

    /// <summary>Сколько у каждого объекта детей — чтобы список показывал внутренний список
    /// числом, не читая дерево по строке за раз.</summary>
    private static void LoadChildCounts(SqliteConnection conn, string projectId,
        IReadOnlyList<ObjectItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        var byId = items.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var rows = Sql.Query(conn, null, """
            SELECT parent_id, COUNT(*) AS n FROM objects
            WHERE project_id=@p AND deleted_at IS NULL AND parent_id IS NOT NULL
            GROUP BY parent_id
            """, r => (Parent: r.S("parent_id"), Count: r.GetInt64(r.GetOrdinal("n"))),
            ("@p", projectId));
        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Parent, out var item))
            {
                item.ChildCount = (int)row.Count;
            }
        }
    }

    private void Append(SqliteConnection conn, SqliteTransaction tx, ObjectItem item,
        string eventType, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = item.ProjectId,
            EventType = eventType,
            EntityType = "object",
            EntityId = item.Id,
            PayloadJson = JsonSerializer.Serialize(new { item.Name, item.Type, item.DisplayId }),
        });

    private static ObjectItem Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        ProjectId = r.SN("project_id"),
        ParentId = r.SN("parent_id"),
        // колонка появилась в v44 (T-102-S0): у базы, ещё не прошедшей миграцию (так читают
        // старую реплику), её может не быть — тогда объект бесхозный, и правит его дирижёр
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        Name = r.S("name"),
        Type = r.S("type"),
        PathOrUrl = r.S("path_or_url"),
        Description = r.S("description"),
        MetaJson = r.S("meta_json"),
        IsActive = r.B("is_active"),
        LoraPath = r.S("lora_path"),
        LoraStatus = r.S("lora_status"),
        CurrentDatasetId = r.SN("current_dataset_id"),
        DatasetJson = r.S("dataset_json"),
        Transport = r.SN("transport"),
        CommandOrUrl = r.SN("command_or_url"),
        ToolsCacheJson = r.SN("tools_cache_json"),
        RulesJson = r.SN("rules_json"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
