using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Опыт (ТЗ п. 2.11) двух видов:
/// <list type="bullet">
/// <item><b>по узлам шаблонов</b> (todo32) — записи накапливаются по задачам, созданным
/// из шаблона (<c>task.template_id</c>), и относятся к узлу шаблона: то, что полезно при
/// повторном выполнении именно его;</item>
/// <item><b>по проекту</b> (todo48) — обобщение работы по всему проекту: его получает
/// ЛЮБАЯ задача проекта, независимо от того, из шаблона она или нет;</item>
/// <item><b>ОБЩИЕ ПРАВИЛА РАБОТЫ</b> (T-11-S0) — правила, годные для любого проекта и любой
/// организации: их получает КАЖДАЯ задача. Ни узла шаблона, ни проекта у такой записи нет —
/// этим она и опознаётся, отдельной колонки для этого не заводилось.</item>
/// </list>
/// У записи есть НАВЫК (todo48): её читает только исполнитель, у которого этот навык есть —
/// медиа-модели незачем читать выводы кодировщиков. Навык не указан — запись общая.
///
/// С T-24-S0 у записи есть ещё ТЭГИ (те же ключевые слова, что у задач и шаблонов, — общий
/// набор без справочника) и признак ЗАГРУЖАТЬ ВСЕГДА: тэгами запись привязывается к теме
/// работы, а признак ставится правилам, которые обязаны попасть в задание независимо от
/// любого отбора.
///
/// Правятся руками (вкладка «Опыт» шаблона и проекта) и агентами (инструменты
/// create_experience / update_experience — под правилами безопасности, проверка в слое
/// коннекторов). Хранится, кем и когда создана и изменена каждая запись.
/// </summary>
public sealed class ExperienceService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public ExperienceService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>
    /// Навык записи «свой» для исполнителя, владеющего навыками <paramref name="owned"/>
    /// (todo48). Правило — ОДНА ВЕТКА иерархии навыков (ТЗ v1.39: сегменты через «-»):
    /// запись читается, если её навык совпадает с одним из имеющихся, уточняет его
    /// (<c>code-write</c> у исполнителя ⇒ читает опыт по <c>code-write-cpp</c>) или наоборот
    /// обобщает (<c>code-write-cpp</c> у исполнителя ⇒ читает общий опыт по <c>code-write</c>:
    /// выводы про написание кода полезны и ему). Разные ветки не пересекаются —
    /// <c>image-generate</c> опыт кодировщика не получит. Навык не указан — запись общая.
    /// </summary>
    public static bool SkillMatches(string? recordSkill, IReadOnlyCollection<string> owned)
    {
        if (string.IsNullOrWhiteSpace(recordSkill))
        {
            return true;   // общая запись — её читают все
        }
        var skill = recordSkill.Trim();
        return owned.Any(own => SameBranch(skill, own.Trim()));
    }

    /// <summary>
    /// ТЭГИ записи подходят задаче (T-29-S0). Тэг — это ТЕМА работы («сборка», «репликация»),
    /// и уточняет он отбор по навыкам, а не заменяет его. Правило:
    /// <list type="bullet">
    /// <item>у задачи тэгов нет — тэги записи ничего не ограничивают (подходит любая);</item>
    /// <item>у записи тэгов нет — она по теме не ограничена и подходит любой задаче. Иначе
    /// у задачи с проставленными тэгами разом исчез бы весь накопленный опыт: тэгов у старых
    /// записей нет ни у одной;</item>
    /// <item>тэги есть у обеих — довольно ОДНОГО совпадения.</item>
    /// </list>
    /// Сравнение без учёта регистра силами C#: <c>COLLATE NOCASE</c> в SQLite сворачивает
    /// только латиницу, а тэги пишет человек и почти всегда по-русски (T-259).
    /// </summary>
    public static bool TagsMatch(IReadOnlyCollection<string>? recordTags,
        IReadOnlyCollection<string>? taskTags)
    {
        // СЛУЖЕБНАЯ ПОМЕТКА ВЛАДЕЛЬЦА (plugin:<код>, T-112-S0) темой работы не является:
        // ею записи плагина помечены, чтобы снятие плагина их нашло. Считай её тэгом — и
        // записи плагина исчезли бы у каждой задачи, у которой тэги вообще проставлены
        recordTags = recordTags?.Where(t => !PluginCodes.IsPluginOwner(t)).ToList();
        if (taskTags is null or { Count: 0 } || recordTags is null or { Count: 0 })
        {
            return true;
        }
        return recordTags.Any(tag => taskTags.Any(
            t => string.Equals(t.Trim(), tag.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>
    /// Запись опыта идёт В ПРОМПТ этой задачи (T-29-S0). Порядок правил ровно такой:
    /// <list type="number">
    /// <item>помечена «ЗАГРУЖАТЬ ВСЕГДА» (T-24-S0) — идёт всегда, отбор её не касается.
    /// Этой пометкой заменена прежняя вставка записей БЕЗ НАВЫКА: раньше запись без навыка
    /// считалась общей и приходила каждому, отчего блок опыта рос без предела;</item>
    /// <item>иначе нужен НАВЫК, и он должен сойтись с навыками исполнителя (как было);</item>
    /// <item>и сверх того — совпасть по ТЕМЕ, то есть по тэгам задачи.</item>
    /// </list>
    /// Запись без навыка и без пометки «загружать всегда» не берётся вовсе: пометку живым
    /// записям без навыка проставляет разовый перенос при обновлении схемы (T-24-S0),
    /// поэтому накопленный опыт от этого правила не пропадает.
    ///
    /// <para><paramref name="skillRequired"/> — правило «нет навыка ⇒ не берём». Оно нужно
    /// там, где записей много и приходят они ВСЕМ подряд (опыт проекта, общие правила).
    /// У опыта УЗЛА ШАБЛОНА смысл другой: он относится ровно к этой работе и достаётся
    /// только задачам этого узла, поэтому там запись без навыка по-прежнему общая — иначе
    /// самый прицельный опыт исчезал бы первым.</para>
    /// </summary>
    public static bool GoesToPrompt(ExperienceRecord record, IReadOnlyCollection<string> ownedSkills,
        IReadOnlyCollection<string>? taskTags, bool skillRequired = true) =>
        record.AlwaysLoad
        || ((!skillRequired || !string.IsNullOrWhiteSpace(record.SkillName))
            && SkillMatches(record.SkillName, ownedSkills)
            && TagsMatch(record.Tags, taskTags));

    /// <summary>Один навык — продолжение другого по сегментам «-» (в любую сторону).</summary>
    private static bool SameBranch(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);
        return longer.StartsWith(shorter + "-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Запись опыта правится и удаляется ТОЛЬКО своим сервером (ТЗ гл. 6, этап 42): опыт
    /// накапливается везде, поэтому владельцем считается сервер, её создавший. Записи,
    /// приехавшие репликой с других серверов, видны, но не правятся.
    /// </summary>
    public bool CanWrite(ExperienceRecord record) => _scope.CanWrite(record.ServerId);

    private ExperienceRecord Decorate(ExperienceRecord record, bool isOrphan = false)
    {
        record.ServerCode = _scope.CodeOf(record.ServerId);
        record.IsReadOnly = !CanWrite(record);
        record.IsOrphan = isOrphan;
        return record;
    }

    /// <summary>
    /// Дочитать ТЭГИ записей (T-24-S0) — своей сущности у тэга нет, он живёт строками
    /// <c>experience_tags</c>. Читаются одним запросом на весь список: записей опыта в
    /// проекте бывают сотни, и запрос на каждую строку обошёлся бы дороже самой выборки.
    /// Порядок один и тот же при каждом чтении (ORDER BY tag) — иначе список в форме
    /// «дрожал» бы между открытиями (как у задач и объектов).
    /// </summary>
    private static void LoadTagsFor(SqliteConnection conn, IReadOnlyList<ExperienceRecord> records)
    {
        foreach (var record in records)
        {
            record.Tags = [];
        }
        if (records.Count == 0)
        {
            return;
        }
        var names = records.Select((_, i) => "@e" + i).ToList();
        var args = records.Select((r, i) => ("@e" + i, (object?)r.Id)).ToArray();
        var rows = Sql.Query(conn, null,
            "SELECT experience_id, tag FROM experience_tags WHERE experience_id IN ("
            + string.Join(", ", names) + ") ORDER BY tag",
            r => (Id: r.S("experience_id"), Tag: r.S("tag")), args);
        var byId = records.ToDictionary(r => r.Id);
        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Id, out var record))
            {
                record.Tags.Add(row.Tag);
            }
        }
    }

    /// <summary>Записать тэги записи: приведение то же, что у задач (запятая — разделитель,
    /// повторы без учёта регистра снимаются). Прежние строки сносятся и пишутся заново —
    /// связей у тэга нет, а часы одной строки журнала строго возрастают (T-3-S1).</summary>
    private static void SaveTags(SqliteConnection conn, SqliteTransaction tx,
        ExperienceRecord record)
    {
        Sql.Exec(conn, tx, "DELETE FROM experience_tags WHERE experience_id=@id", ("@id", record.Id));
        record.Tags = TaskTags.Normalize(record.Tags);
        foreach (var tag in record.Tags)
        {
            Sql.Exec(conn, tx, "INSERT OR IGNORE INTO experience_tags (experience_id, tag) VALUES (@e, @g)",
                ("@e", record.Id), ("@g", tag));
        }
    }

    /// <summary>
    /// Все тэги записей опыта организации (T-24-S0). Справочника у тэгов нет — это DISTINCT
    /// по живым записям; вместе с тэгами задач (<c>TaskService.ListTags</c>) они дают набор,
    /// который предлагается в форме записи и в фильтре списка: тэги у опыта и у задач
    /// НАМЕРЕННО одни и те же — по ним они и связываются между собой.
    /// </summary>
    public List<string> ListTags()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT DISTINCT g.tag FROM experience_tags g
            JOIN experience e ON e.id = g.experience_id
            WHERE e.deleted_at IS NULL
            """, r => r.S("tag"))
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Записи узла шаблона; includeSubtree — вместе с узлами-потомками
    /// (вкладка «Опыт» карточки шаблона показывает опыт всего поддерева).</summary>
    public List<ExperienceRecord> ListByTemplate(string templateTaskId, bool includeSubtree = false)
    {
        using var conn = _db.Open();
        var sql = includeSubtree
            ? $"""
              WITH RECURSIVE nodes(id) AS (
                SELECT id FROM tasks WHERE id=@id
                UNION ALL
                SELECT t.id FROM tasks t JOIN nodes n ON t.parent_id = n.id
                WHERE t.deleted_at IS NULL
              )
              {Select}
              WHERE e.template_task_id IN (SELECT id FROM nodes) AND e.deleted_at IS NULL
              ORDER BY e.created_at
              """
            : $"""
              {Select}
              WHERE e.template_task_id=@id AND e.deleted_at IS NULL
              ORDER BY e.created_at
              """;
        var records = Sql.Query(conn, null, sql, Map, ("@id", templateTaskId));
        foreach (var record in records)
        {
            Decorate(record);
        }
        LoadTagsFor(conn, records);
        return records;
    }

    /// <summary>
    /// Опыт ПРОЕКТА (todo48) — обобщение работы по всему проекту. Необязательный фильтр
    /// по навыкам (множественный выбор в UI): пустой список — все записи. Запись без навыка
    /// показывается всегда: она общая и фильтром по конкретному навыку не отсекается.
    /// </summary>
    public List<ExperienceRecord> ListByProject(string projectId, IReadOnlyCollection<string>? skillIds = null)
    {
        using var conn = _db.Open();
        var records = Sql.Query(conn, null, $"""
            {Select}
            WHERE e.project_id=@id AND e.deleted_at IS NULL
            ORDER BY e.created_at
            """, Map, ("@id", projectId));
        foreach (var record in records)
        {
            Decorate(record);
        }
        LoadTagsFor(conn, records);
        return skillIds is null or { Count: 0 }
            ? records
            : records.Where(r => r.SkillId is null || skillIds.Contains(r.SkillId)).ToList();
    }

    /// <summary>
    /// Опыт проекта, который ОТДАЁТСЯ ИСПОЛНИТЕЛЮ при запуске задачи (todo48): записи по
    /// навыкам, которые у него есть, уточнённые тэгами задачи, плюс всё, что помечено
    /// «загружать всегда» (T-29-S0). Навыки исполнителя приходят кодами из его декларации
    /// возможностей (п. 7.3), тэги — из самой задачи; тэгов нет — по тэгам не отсеиваем.
    /// </summary>
    public List<ExperienceRecord> ListForExecutor(string projectId, IReadOnlyCollection<string> ownedSkills,
        IReadOnlyCollection<string>? taskTags = null) =>
        ListByProject(projectId).Where(r => GoesToPrompt(r, ownedSkills, taskTags)).ToList();

    /// <summary>
    /// ОБЩИЕ ПРАВИЛА РАБОТЫ организации (T-11-S0): записи, не привязанные ни к проекту,
    /// ни к узлу шаблона. Их получает любая задача — независимо от проекта и от того,
    /// из шаблона она или нет. Необязательный фильтр по навыкам — как у опыта проекта:
    /// пустой список означает «все записи», запись без навыка показывается всегда.
    /// </summary>
    public List<ExperienceRecord> ListGeneral(IReadOnlyCollection<string>? skillIds = null)
    {
        using var conn = _db.Open();
        var records = Sql.Query(conn, null, $"""
            {Select}
            WHERE e.project_id IS NULL AND e.template_task_id IS NULL AND e.deleted_at IS NULL
            ORDER BY e.created_at
            """, Map);
        foreach (var record in records)
        {
            Decorate(record);
        }
        LoadTagsFor(conn, records);
        return skillIds is null or { Count: 0 }
            ? records
            : records.Where(r => r.SkillId is null || skillIds.Contains(r.SkillId)).ToList();
    }

    /// <summary>Общие правила работы, которые ОТДАЮТСЯ ИСПОЛНИТЕЛЮ при запуске задачи
    /// (T-11-S0): отбор такой же, как у опыта проекта (T-29-S0) — навык, тэги задачи и
    /// пометка «загружать всегда».</summary>
    public List<ExperienceRecord> ListGeneralForExecutor(IReadOnlyCollection<string> ownedSkills,
        IReadOnlyCollection<string>? taskTags = null) =>
        ListGeneral().Where(r => GoesToPrompt(r, ownedSkills, taskTags)).ToList();

    /// <summary>
    /// Записи узла шаблона, которые ОТДАЮТСЯ ИСПОЛНИТЕЛЮ при запуске задачи (T-29-S0).
    /// До этого опыт узла отдавался ЦЕЛИКОМ, без всякого отбора; теперь к нему применяются
    /// навыки исполнителя и тэги задачи — но БЕЗ правила «нет навыка ⇒ не берём»: опыт узла
    /// написан ровно про эту работу и достаётся только задачам этого узла, отсекать его
    /// не за что (навыка у таких записей обычно нет вовсе).
    /// </summary>
    public List<ExperienceRecord> ListByTemplateForExecutor(string templateTaskId,
        IReadOnlyCollection<string> ownedSkills, IReadOnlyCollection<string>? taskTags = null) =>
        ListByTemplate(templateTaskId)
            .Where(r => GoesToPrompt(r, ownedSkills, taskTags, skillRequired: false)).ToList();

    // --- первичное наполнение общих правил (T-11-S0) ---

    /// <summary>
    /// ПРАВИЛО ДИСТРИБУТИВА: ключ словаря (текст берётся на языке установки, как остальные
    /// сообщения человеку) и ФИКСИРОВАННЫЙ идентификатор записи.
    /// <para>Идентификатор постоянный по той же причине, что у записей справочника моделей
    /// (T-227): правило засеивает КАЖДЫЙ сервер сам, а таблица <c>experience</c> при этом
    /// реплицируется — со случайными идентификаторами в организации копился бы второй
    /// комплект правил, по одному на сервер. С постоянным строка у всех одна и та же.</para>
    /// </summary>
    public sealed record GeneralRule(string Id, string Key);

    /// <summary>
    /// Правила, которые получает КАЖДАЯ организация (T-11-S0). Список дописывается В КОНЕЦ:
    /// номер последнего засеянного правила запоминается в <c>meta</c>, поэтому добавленное
    /// потом правило доедет и до заведённых раньше организаций, а удалённое человеком
    /// обратно не возвращается.
    /// </summary>
    public static readonly GeneralRule[] GeneralSeed =
    [
        // одна подзадача — не подзадача (T-11-S0)
        new("a1e5b5c0-7d1a-4c31-9f01-0000000e0001", "seed.experience.1"),
        // «Время ↔ качество»: сколько проверок гонять (T-30-S0)
        new("a1e5b5c0-7d1a-4c31-9f01-0000000e0002", "seed.experience.2"),
        // список требуемых проверок пишется ВСЕГДА и первым разделом отчёта (T-30-S0)
        new("a1e5b5c0-7d1a-4c31-9f01-0000000e0003", "seed.experience.3"),
        // шкала степени нужности проверки (T-30-S0)
        new("a1e5b5c0-7d1a-4c31-9f01-0000000e0004", "seed.experience.4"),
    ];

    /// <summary>Ключи словаря у правил дистрибутива — в порядке засева.</summary>
    public static string[] GeneralSeedKeys => GeneralSeed.Select(r => r.Key).ToArray();

    /// <summary>Ключ в <c>meta</c> базы организации: сколько общих правил уже засеяно.</summary>
    public const string GeneralSeedMetaKey = "general_experience_seed";

    /// <summary>
    /// Время правки правила, заведённого СИДОМ (T-227): нарочно не «сейчас». Партнёр считает
    /// конфликт по «менялось ли у нас с прошлого удачного сеанса», и свежая отметка выдавала
    /// бы только что засеянное правило за местную правку — на каждом правиле был бы спор.
    /// У правил оно разное на минуту, чтобы порядок списка (ORDER BY created_at) совпадал
    /// с порядком объявления.
    /// </summary>
    public static readonly DateTime SeedStamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Засеять недостающие общие правила работы (T-11-S0). Зовётся при открытии организации.
    ///
    /// <para>СЕЕТ КАЖДЫЙ СЕРВЕР, а не только дирижёр (T-30-S0). До этого правила заводил
    /// один дирижёр, а остальным они должны были приехать репликацией — и на установке,
    /// где дирижёр сменился или отстал версией, поставляемые правила не появлялись вовсе:
    /// прежний дирижёр их уже не сеет, а новый считает себя репликой (пометка
    /// <c>replica_of</c> ставится при подключении и не снимается никогда). Второго комплекта
    /// при этом не возникает — идентификаторы правил ФИКСИРОВАННЫЕ (T-227).</para>
    ///
    /// <para>Идемпотентно ТРИЖДЫ: по отметке в <c>meta</c>, по идентификатору (в том числе
    /// у правила, УДАЛЁННОГО человеком, — оно не возвращается) и по совпадению текста —
    /// иначе сервер, у которого правило уже есть, завёл бы рядом второе.</para>
    /// </summary>
    /// <returns>Сколько правил добавлено.</returns>
    public int SeedGeneral()
    {
        var done = int.TryParse(_db.Meta(GeneralSeedMetaKey), out var stored) ? stored : 0;
        if (done >= GeneralSeed.Length)
        {
            return 0;
        }
        var existing = ListGeneral().Select(r => r.Text.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var added = 0;
        for (var i = done; i < GeneralSeed.Length; i++)
        {
            var rule = GeneralSeed[i];
            var text = Loc.T(rule.Key).Trim();
            // словаря нет (Loc отдал сам ключ) — сеять нечего, и отметку ставить нельзя:
            // иначе правило не появится уже никогда
            if (text.Length == 0 || text == rule.Key)
            {
                return added;
            }
            if (Exists(rule.Id) || !existing.Add(text))
            {
                continue;
            }
            CreateSeeded(rule, text, i);
            added++;
        }
        _db.SetMeta(GeneralSeedMetaKey, GeneralSeed.Length.ToString());
        return added;
    }

    /// <summary>Строка с таким идентификатором в таблице уже есть — В ТОМ ЧИСЛЕ удалённая
    /// человеком: удалённое правило сид возвращать не должен.</summary>
    private bool Exists(string id)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null, "SELECT COUNT(*) FROM experience WHERE id=@id",
            ("@id", id)) > 0;
    }

    /// <summary>
    /// Завести правило дистрибутива: свой идентификатор, постоянное время правки и пометка
    /// «ЗАГРУЖАТЬ ВСЕГДА» — без неё правило без навыка в задание не попадает вовсе (T-29-S0),
    /// а поставляемые правила обязаны получать все.
    /// <para>Сервер-владелец у такой записи НЕ указан: правило принадлежит организации,
    /// а не тому, кто первым её открыл, — правит его дирижёр. Иначе у каждого сервера
    /// получилась бы своя версия одной и той же строки.</para>
    /// </summary>
    private void CreateSeeded(GeneralRule rule, string text, int index)
    {
        using var conn = _db.Open();
        var stamp = SeedStamp.AddMinutes(index);
        Insert(conn, new ExperienceRecord
        {
            Id = rule.Id,
            Text = text,
            AlwaysLoad = true,
            ServerId = null,
            CreatedAt = stamp,
            UpdatedAt = stamp,
        }, ("", ""), null);
    }

    /// <summary>
    /// Осиротевшие записи опыта (ТЗ гл. 6, этап 42): шаблоны правятся на дирижёре, а записи
    /// по их узлам создаются на разных серверах и правятся только своим сервером. Узел
    /// удалили на дирижёре — чужие записи остались висеть: физически их не трогаем (удалить
    /// может только владелец), но в формах и в промпте агента не показываем.
    /// Здесь они перечисляются отдельно — чтобы владелец мог их убрать.
    /// </summary>
    public List<ExperienceRecord> ListOrphans()
    {
        using var conn = _db.Open();
        // опыт проекта (todo48) и общие правила работы (T-11-S0) сюда не относятся:
        // узла шаблона у них нет по определению, осиротеть им не от чего
        var records = Sql.Query(conn, null, $"""
            {Select}
            LEFT JOIN tasks t ON t.id = e.template_task_id
            WHERE e.deleted_at IS NULL AND e.project_id IS NULL AND e.template_task_id IS NOT NULL
              AND (t.id IS NULL OR t.deleted_at IS NOT NULL)
            ORDER BY e.created_at
            """, Map);
        foreach (var record in records)
        {
            Decorate(record, isOrphan: true);
        }
        LoadTagsFor(conn, records);
        return records;
    }

    public ExperienceRecord? Get(string id)
    {
        using var conn = _db.Open();
        var record = Sql.Query(conn, null, $"{Select} WHERE e.id=@id AND e.deleted_at IS NULL",
            Map, ("@id", id)).FirstOrDefault();
        if (record is null)
        {
            return null;
        }
        LoadTagsFor(conn, [record]);
        return Decorate(record);
    }

    /// <summary>Выборка записи вместе с КОДОМ навыка (в таблице лежит только его id).</summary>
    private const string Select = """
        SELECT e.*, (SELECT s.name FROM skills s WHERE s.id = e.skill_id) AS skill_name
        FROM experience e
        """;

    /// <summary>Новая запись опыта узла шаблона; actorId — кто создал (человек или агент).</summary>
    public ExperienceRecord Create(string templateTaskId, string text, string? actorId,
        string? skillId = null, bool alwaysLoad = false, IEnumerable<string>? tags = null)
    {
        text = Require(text);
        using var conn = _db.Open();
        var node = RequireTemplateNode(conn, templateTaskId);

        var skill = Skill(conn, skillId);
        var record = new ExperienceRecord
        {
            TemplateTaskId = templateTaskId,
            SkillId = skill.Id,
            SkillName = skill.Name,
            Text = text,
            AlwaysLoad = alwaysLoad,
            Tags = TaskTags.Normalize(tags?.ToList()),
            CreatedBy = actorId,
            UpdatedBy = actorId,
            // запись принадлежит серверу, который её создал (ТЗ гл. 6, этап 42)
            ServerId = _scope.ServerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        Insert(conn, record, node, actorId);
        return Get(record.Id)!;   // код навыка — из справочника, его знает только выборка
    }

    /// <summary>
    /// Новая запись опыта ПРОЕКТА (todo48): её получит любая задача проекта, исполнитель
    /// которой владеет указанным навыком. Навык не указан — запись общая, её читают все.
    /// </summary>
    public ExperienceRecord CreateForProject(string projectId, string text, string? actorId,
        string? skillId = null, bool alwaysLoad = false, IEnumerable<string>? tags = null)
    {
        text = Require(text);
        using var conn = _db.Open();
        var node = RequireProject(conn, projectId);

        var skill = Skill(conn, skillId);
        var record = new ExperienceRecord
        {
            ProjectId = projectId,
            SkillId = skill.Id,
            SkillName = skill.Name,
            Text = text,
            AlwaysLoad = alwaysLoad,
            Tags = TaskTags.Normalize(tags?.ToList()),
            CreatedBy = actorId,
            UpdatedBy = actorId,
            ServerId = _scope.ServerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        Insert(conn, record, node, actorId);
        return Get(record.Id)!;
    }

    /// <summary>
    /// Новое ОБЩЕЕ ПРАВИЛО РАБОТЫ организации (T-11-S0): ни проекта, ни узла шаблона —
    /// такую запись получает каждая задача. Навык не указан — правило читают все.
    /// </summary>
    public ExperienceRecord CreateGeneral(string text, string? actorId, string? skillId = null,
        bool alwaysLoad = false, IEnumerable<string>? tags = null)
    {
        text = Require(text);
        using var conn = _db.Open();
        var skill = Skill(conn, skillId);
        var record = new ExperienceRecord
        {
            SkillId = skill.Id,
            SkillName = skill.Name,
            Text = text,
            AlwaysLoad = alwaysLoad,
            Tags = TaskTags.Normalize(tags?.ToList()),
            CreatedBy = actorId,
            UpdatedBy = actorId,
            ServerId = _scope.ServerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        Insert(conn, record, ("", ""), actorId);
        return Get(record.Id)!;
    }

    /// <summary>Запись в таблицу и событие журнала — общее для всех видов записей.</summary>
    private void Insert(SqliteConnection conn, ExperienceRecord record,
        (string ProjectId, string DisplayId) node, string? actorId)
    {
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            INSERT INTO experience (id, template_task_id, project_id, skill_id, text, always_load,
                                    created_by, updated_by, server_id, created_at, updated_at)
            VALUES (@id, @template, @project, @skill, @text, @always, @by, @by, @server, @created, @created)
            """,
            ("@id", record.Id),
            ("@template", record.TemplateTaskId.Length > 0 ? record.TemplateTaskId : null),
            ("@project", record.ProjectId), ("@skill", record.SkillId), ("@text", record.Text),
            ("@always", record.AlwaysLoad ? 1 : 0),
            ("@by", actorId), ("@server", record.ServerId), ("@created", Sql.ToDb(record.CreatedAt)));
        SaveTags(conn, tx, record);
        Append(conn, tx, EventTypes.ExperienceRecorded, record, node, actorId);
        tx.Commit();
    }

    /// <summary>
    /// Правка текста и навыка записи; actorId — кто изменил (updated_by/updated_at).
    /// <para>Признак «загружать всегда» и тэги (T-24-S0) правятся только когда их передали
    /// (<paramref name="alwaysLoad"/> не null, <paramref name="tags"/> не null): правка
    /// текста из инструмента агента не должна снимать пометку, которую поставил человек.</para>
    /// </summary>
    public ExperienceRecord Update(string id, string text, string? actorId, string? skillId = null,
        bool changeSkill = false, bool? alwaysLoad = null, IEnumerable<string>? tags = null)
    {
        text = Require(text);
        var record = Get(id) ?? throw new InvalidOperationException(Loc.T("msg.experience.1", id));
        EnsureMine(record);
        using var conn = _db.Open();
        // общее правило работы (T-11-S0) ни к проекту, ни к узлу шаблона не привязано —
        // проверять у него нечего
        var node = record.IsGeneral
            ? ("", "")
            : record.IsProjectLevel
                ? RequireProject(conn, record.ProjectId!)
                : RequireTemplateNode(conn, record.TemplateTaskId);

        record.Text = text;
        if (changeSkill)
        {
            (record.SkillId, record.SkillName) = Skill(conn, skillId);
        }
        if (alwaysLoad is { } always)
        {
            record.AlwaysLoad = always;
        }
        record.UpdatedBy = actorId;
        record.UpdatedAt = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE experience SET text=@text, skill_id=@skill, always_load=@always,
                                  updated_by=@by, updated_at=@at
            WHERE id=@id
            """,
            ("@text", text), ("@skill", record.SkillId), ("@always", record.AlwaysLoad ? 1 : 0),
            ("@by", actorId), ("@at", Sql.ToDb(record.UpdatedAt)), ("@id", id));
        if (tags is not null)
        {
            record.Tags = tags.ToList();
            SaveTags(conn, tx, record);
        }
        Append(conn, tx, EventTypes.ExperienceUpdated, record, node, actorId);
        tx.Commit();
        return Get(id)!;   // код навыка перечитывается из справочника
    }

    /// <summary>Текст записи обязателен: пустой опыт не запись, а мусор в промпте агента.</summary>
    private static string Require(string text) =>
        text.Trim() is { Length: > 0 } trimmed ? trimmed : throw new ArgumentException(Loc.T("msg.experience.2"));

    /// <summary>
    /// Навык записи: пусто — общая запись; указан — должен быть в справочнике. Возвращается
    /// вместе с КОДОМ навыка: он идёт в событие журнала и в промпт агента.
    /// </summary>
    private static (string? Id, string Name) Skill(SqliteConnection conn, string? skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return (null, "");
        }
        var name = Sql.Query(conn, null, "SELECT name FROM skills WHERE id=@id AND deleted_at IS NULL",
            r => r.S("name"), ("@id", skillId)).FirstOrDefault();
        return name is null
            ? throw new ArgumentException(Loc.T("msg.experience.3", skillId))
            : (skillId, name);
    }

    /// <summary>Мягкое удаление записи (кнопка вкладки «Опыт»).</summary>
    public void Delete(string id, string? actorId)
    {
        var record = Get(id) ?? throw new InvalidOperationException(Loc.T("msg.experience.1", id));
        EnsureMine(record);
        using var conn = _db.Open();
        // осиротевшую запись (узел шаблона удалён на дирижёре) владелец должен уметь убрать,
        // поэтому существование узла здесь не требуется (ТЗ гл. 6, этап 42)
        var node = record.IsGeneral
            ? ("", "")
            : record.IsProjectLevel
                ? (record.ProjectId!, "")
                : TemplateNodeOrEmpty(conn, record.TemplateTaskId);
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, "UPDATE experience SET deleted_at=@at, updated_by=@by, updated_at=@at WHERE id=@id",
            ("@at", Sql.ToDb(DateTime.UtcNow)), ("@by", actorId), ("@id", id));
        Append(conn, tx, EventTypes.ExperienceDeleted, record, node, actorId);
        tx.Commit();
    }

    /// <summary>Чужую запись опыта править нельзя: её ведёт создавший сервер (ТЗ гл. 6).</summary>
    private void EnsureMine(ExperienceRecord record) =>
        _scope.EnsureCanWrite(record.ServerId, Loc.T("msg.experience.4"));

    /// <summary>Узел шаблона записи, если он ещё жив; иначе пустые значения (запись осиротела).</summary>
    private static (string ProjectId, string DisplayId) TemplateNodeOrEmpty(SqliteConnection conn,
        string templateTaskId)
    {
        var row = Sql.Query(conn, null,
                "SELECT project_id, display_id FROM tasks WHERE id=@id AND deleted_at IS NULL",
                r => (ProjectId: r.SN("project_id"), DisplayId: r.S("display_id")),
                ("@id", templateTaskId))
            .FirstOrDefault();
        return (row.ProjectId ?? "", row.DisplayId ?? "");
    }

    /// <summary>Узел шаблона записи: должен существовать и быть шаблоном.</summary>
    private static (string ProjectId, string DisplayId) RequireTemplateNode(SqliteConnection conn, string templateTaskId)
    {
        var row = Sql.Query(conn, null,
                "SELECT project_id, display_id, is_template FROM tasks WHERE id=@id AND deleted_at IS NULL",
                r => (ProjectId: r.SN("project_id"), DisplayId: r.S("display_id"),
                      IsTemplate: r.B("is_template")),
                ("@id", templateTaskId))
            .FirstOrDefault();
        if (row.DisplayId is null or "")
        {
            throw new InvalidOperationException(Loc.T("msg.experience.5", templateTaskId));
        }
        if (!row.IsTemplate)
        {
            throw new ArgumentException(Loc.T("msg.experience.6"));
        }
        return (row.ProjectId ?? "", row.DisplayId);
    }

    /// <summary>Проект записи опыта проекта: должен существовать (todo48).</summary>
    private static (string ProjectId, string DisplayId) RequireProject(SqliteConnection conn, string projectId)
    {
        var found = Sql.Query(conn, null,
                "SELECT display_id FROM projects WHERE id=@id AND deleted_at IS NULL",
                r => r.S("display_id"), ("@id", projectId))
            .FirstOrDefault();
        return found is null or ""
            ? throw new InvalidOperationException(Loc.T("msg.experience.7", projectId))
            : (projectId, found);
    }

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        ExperienceRecord record, (string ProjectId, string DisplayId) node, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            ProjectId = node.ProjectId.Length > 0 ? node.ProjectId : null,
            // у опыта проекта узла шаблона нет — событие привязано только к проекту,
            // у общего правила (T-11-S0) нет ни того, ни другого
            TaskId = record.TemplateTaskId.Length > 0 ? record.TemplateTaskId : null,
            EventType = eventType,
            EntityType = "experience",
            EntityId = record.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                scope = record.IsGeneral ? "general" : record.IsProjectLevel ? "project" : "template",
                template = record.IsProjectLevel ? "" : node.DisplayId,
                project = record.IsProjectLevel ? node.DisplayId : "",
                skill = record.SkillName,
                preview = record.Text.Length <= 120 ? record.Text : record.Text[..120] + "…",
            }),
        });

    private static ExperienceRecord Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        // у записи опыта ПРОЕКТА узла шаблона нет — колонка пуста (todo48)
        TemplateTaskId = r.SN("template_task_id") ?? "",
        ProjectId = r.Has("project_id") ? r.SN("project_id") : null,
        SkillId = r.Has("skill_id") ? r.SN("skill_id") : null,
        SkillName = r.Has("skill_name") ? r.SN("skill_name") ?? "" : "",
        Text = r.S("text"),
        // «загружать всегда» (T-24-S0): на базе, где колонки ещё нет, признак выключен
        AlwaysLoad = r.Has("always_load") && r.B("always_load"),
        CreatedBy = r.SN("created_by"),
        UpdatedBy = r.SN("updated_by"),
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
