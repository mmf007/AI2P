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
/// С T-265-S0 у записи есть ПРИЗНАК АКТИВНОСТИ (колонка <c>is_active</c>, схема v47) —
/// промежуточное состояние между «есть» и «удалена»: неактивную запись агент не получает
/// ни при каком раскладе, а человек видит её в списке и может вернуть.
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
    ///
    /// <para>С T-267-S0 это УЖЕ НЕ ФИЛЬТР, а сигнал: в <see cref="GoesToPrompt"/> тэги
    /// жёстким условием не стоят, запись с непересекающимися тэгами берётся и просто
    /// ранжируется ниже (<see cref="ExperienceBudget"/>). Причина: обе поблажки выше делали
    /// фильтр почти пустым, а <c>create_experience</c> с T-24-S0 проставляет новым записям
    /// тэги задачи — значит со временем фильтр начал бы отсекать нужное по формальному
    /// несовпадению слова. Метод остался ровно тем же и зовётся теперь из отбора по рангу.</para>
    /// </summary>
    public static bool TagsMatch(IReadOnlyCollection<string>? recordTags,
        IReadOnlyCollection<string>? taskTags)
    {
        // СЛУЖЕБНАЯ ПОМЕТКА ВЛАДЕЛЬЦА (plugin:<код>, T-112-S0; pack:<код>, T-270-S0) темой
        // работы не является: ею помечены записи плагина и записи НАБОРА ОПЫТА, чтобы снятие
        // их нашло. Считай её тэгом — и записи набора исчезли бы у каждой задачи, у которой
        // тэги вообще проставлены
        recordTags = recordTags?.Where(t => !IsOwnerTag(t)).ToList();
        if (taskTags is null or { Count: 0 } || recordTags is null or { Count: 0 })
        {
            return true;
        }
        return recordTags.Any(tag => taskTags.Any(
            t => string.Equals(t.Trim(), tag.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>
    /// ТЭГИ ЗАПИСИ И ЗАДАЧИ ДЕЙСТВИТЕЛЬНО ПЕРЕСЕКЛИСЬ (T-267-S0) — прибавка к рангу при
    /// дележе бюджета. В отличие от <see cref="TagsMatch"/>, поблажек здесь нет: «тэгов
    /// нет ни у кого» — это не совпадение темы, а отсутствие сведений о ней, и двигать
    /// такую запись вперёд не за что. Служебная пометка владельца (<c>plugin:&lt;код&gt;</c>,
    /// T-112-S0) тэгом по-прежнему не считается — как и в <see cref="TagsMatch"/>; с T-270-S0
    /// тем же местом отсеивается пометка НАБОРА ОПЫТА (<c>pack:&lt;код&gt;</c>).
    /// </summary>
    public static bool TagsOverlap(IReadOnlyCollection<string>? recordTags,
        IReadOnlyCollection<string>? taskTags)
    {
        if (taskTags is null or { Count: 0 } || recordTags is null or { Count: 0 })
        {
            return false;
        }
        return recordTags
            .Where(t => !IsOwnerTag(t))
            .Any(tag => taskTags.Any(
                t => string.Equals(t.Trim(), tag.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>
    /// ТЭГ — СЛУЖЕБНАЯ ПОМЕТКА ВЛАДЕЛЬЦА, а не тема работы: <c>plugin:&lt;код&gt;</c> у записей
    /// плагина (T-112-S0) и <c>pack:&lt;код&gt;</c> у записей набора опыта (T-270-S0). Обе нужны
    /// ровно для того, чтобы снятие нашло свои записи, и обе обязаны проходить мимо отбора —
    /// иначе поставленный стиль работы исчезал бы у каждой задачи с проставленными тэгами.
    /// </summary>
    public static bool IsOwnerTag(string? tag) =>
        PluginCodes.IsPluginOwner(tag) || PackCodes.IsPackOwner(tag);

    /// <summary>
    /// Запись опыта идёт В ПРОМПТ этой задачи (T-29-S0). Порядок правил ровно такой:
    /// <list type="number">
    /// <item>запись АКТИВНА (T-265-S0) — первым условием и БЕЗ исключений: неактивная
    /// не идёт в задание ни при каком раскладе, в том числе помеченная «загружать всегда».
    /// В этом весь смысл признака — выключить запись, не удаляя её;</item>
    /// <item>помечена «ЗАГРУЖАТЬ ВСЕГДА» (T-24-S0) — идёт всегда, отбор её не касается.
    /// Этой пометкой заменена прежняя вставка записей БЕЗ НАВЫКА: раньше запись без навыка
    /// считалась общей и приходила каждому, отчего блок опыта рос без предела;</item>
    /// <item>иначе нужен НАВЫК, и он должен сойтись с навыками исполнителя (как было).</item>
    /// </list>
    /// <para>ТЭГОВ В ЭТОМ СПИСКЕ БОЛЬШЕ НЕТ (T-267-S0): жёстким условием стоял
    /// <see cref="TagsMatch"/> с двумя поблажками («нет тэгов у задачи ИЛИ у записи =
    /// совпадение»), то есть почти ничего не отсекал — зато <c>create_experience</c>
    /// проставляет новым записям тэги задачи, и со временем фильтр начал бы отсекать нужное
    /// по формальному несовпадению слова. Теперь тэги — СИГНАЛ: совпадение двигает запись
    /// вперёд при дележе бюджета (<see cref="TagsOverlap"/> и <see cref="ExperienceBudget"/>),
    /// несовпадение её не убирает. Параметр <paramref name="taskTags"/> оставлен: он часть
    /// подписи, зовущейся отовсюду, и понадобится следующему правилу отбора.</para>
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
        record.IsActive
        && (record.AlwaysLoad
            || ((!skillRequired || !string.IsNullOrWhiteSpace(record.SkillName))
                && SkillMatches(record.SkillName, ownedSkills)));

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

    /// <summary>
    /// ЦЕПОЧКА ПРЕДКОВ УЗЛА ШАБЛОНА (T-267-S0), начиная с самого узла: индекс в списке —
    /// это расстояние до узла (0 — он сам, 1 — родитель, 2 — дед). По ней берётся опыт
    /// задания (<see cref="ListByTemplateForExecutor"/>) и считается прицельность записи
    /// при дележе бюджета. Цепочка идёт до корня шаблона целиком: записей у узлов немного,
    /// а место теперь делится квотами уровней, и общая наука по ветке процесса обязана
    /// доезжать до задачи.
    /// </summary>
    public List<string> TemplateChain(string templateTaskId)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            WITH RECURSIVE chain(id, parent_id, depth) AS (
              SELECT id, parent_id, 0 FROM tasks WHERE id=@id
              UNION ALL
              SELECT t.id, t.parent_id, c.depth + 1 FROM tasks t JOIN chain c ON t.id = c.parent_id
              WHERE t.deleted_at IS NULL
            )
            SELECT id FROM chain ORDER BY depth
            """, r => r.S("id"), ("@id", templateTaskId));
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
    ///
    /// <para>С T-267-S0 берётся ЦЕПОЧКА ПРЕДКОВ узла (<see cref="TemplateChain"/>), а не
    /// один узел: общая наука по ветке процесса («так у нас делается выпуск») записана
    /// у узла-родителя, и до задачи узла-потомка она не доезжала вовсе. Поддерево при этом
    /// НЕ берётся — опыт соседних веток к этой работе не относится.</para>
    /// </summary>
    public List<ExperienceRecord> ListByTemplateForExecutor(string templateTaskId,
        IReadOnlyCollection<string> ownedSkills, IReadOnlyCollection<string>? taskTags = null) =>
        ListByTemplateChain(templateTaskId)
            .Where(r => GoesToPrompt(r, ownedSkills, taskTags, skillRequired: false)).ToList();

    /// <summary>Записи узла и всех его узлов-ПРЕДКОВ (T-267-S0), в том же хронологическом
    /// порядке, что и у одного узла: блок опыта читается как история работы.</summary>
    private List<ExperienceRecord> ListByTemplateChain(string templateTaskId)
    {
        using var conn = _db.Open();
        var records = Sql.Query(conn, null, $"""
            WITH RECURSIVE chain(id, parent_id) AS (
              SELECT id, parent_id FROM tasks WHERE id=@id
              UNION ALL
              SELECT t.id, t.parent_id FROM tasks t JOIN chain c ON t.id = c.parent_id
              WHERE t.deleted_at IS NULL
            )
            {Select}
            WHERE e.template_task_id IN (SELECT id FROM chain) AND e.deleted_at IS NULL
            ORDER BY e.created_at
            """, Map, ("@id", templateTaskId));
        foreach (var record in records)
        {
            Decorate(record);
        }
        LoadTagsFor(conn, records);
        return records;
    }

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
    /// <para>С T-269-S0 текст с признаками ПРОЕКТНОГО (код задачи, путь файла, расширение
    /// исходника, имя проекта) сюда не проходит: <paramref name="force"/> снимает проверку
    /// и ставится там, где решение уже принял человек (кнопка «всё равно сохранить» в форме,
    /// пачка записей опыта из манифеста плагина).</para>
    /// </summary>
    public ExperienceRecord CreateGeneral(string text, string? actorId, string? skillId = null,
        bool alwaysLoad = false, IEnumerable<string>? tags = null, bool force = false)
    {
        text = Require(text);
        EnsureGeneralEnough(text, force);
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
                                    is_active, created_by, updated_by, server_id, created_at, updated_at)
            VALUES (@id, @template, @project, @skill, @text, @always, @active, @by, @by, @server, @created, @created)
            """,
            ("@active", record.IsActive ? 1 : 0),
            ("@id", record.Id),
            ("@template", record.TemplateTaskId.Length > 0 ? record.TemplateTaskId : null),
            ("@project", record.ProjectId), ("@skill", record.SkillId), ("@text", record.Text),
            ("@always", record.AlwaysLoad ? 1 : 0),
            ("@by", actorId), ("@server", record.ServerId), ("@created", Sql.ToDb(record.CreatedAt)));
        // лексический индекс (T-268-S0) — в той же транзакции: запись и её строка в индексе
        // появляются вместе либо не появляются вовсе
        ExperienceIndex.Put(conn, tx, record.Id, record.Text);
        SaveTags(conn, tx, record);
        Append(conn, tx, EventTypes.ExperienceRecorded, record, node, actorId);
        tx.Commit();
    }

    /// <summary>
    /// Правка текста и навыка записи; actorId — кто изменил (updated_by/updated_at).
    /// <para>Признак «загружать всегда», тэги (T-24-S0) и АКТИВНОСТЬ (T-265-S0) правятся
    /// только когда их передали (<paramref name="alwaysLoad"/>, <paramref name="isActive"/>
    /// не null, <paramref name="tags"/> не null): правка текста из инструмента агента не
    /// должна снимать пометку, которую поставил человек.</para>
    /// <para>ПРАВКА НЕАКТИВНОЙ ЗАПИСИ разрешена и активности не возвращает (решение по
    /// T-265-S0): агент правит текст, не зная о признаке, и молчаливое «воскрешение»
    /// отменяло бы работу того, кто запись погасил.</para>
    /// </summary>
    public ExperienceRecord Update(string id, string text, string? actorId, string? skillId = null,
        bool changeSkill = false, bool? alwaysLoad = null, IEnumerable<string>? tags = null,
        bool? isActive = null, bool force = false)
    {
        text = Require(text);
        var record = Get(id) ?? throw new InvalidOperationException(Loc.T("msg.experience.1", id));
        EnsureMine(record);
        // ПРАВКОЙ проектный текст в общие правила тоже не попадает (T-269-S0): иначе
        // проверку при создании обходил бы любой, кто завёл пустое правило и дописал его
        if (record.IsGeneral)
        {
            EnsureGeneralEnough(text, force);
        }
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
        if (isActive is { } active)
        {
            record.IsActive = active;
        }
        record.UpdatedBy = actorId;
        record.UpdatedAt = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE experience SET text=@text, skill_id=@skill, always_load=@always,
                                  is_active=@active, updated_by=@by, updated_at=@at
            WHERE id=@id
            """,
            ("@text", text), ("@skill", record.SkillId), ("@always", record.AlwaysLoad ? 1 : 0),
            ("@active", record.IsActive ? 1 : 0),
            ("@by", actorId), ("@at", Sql.ToDb(record.UpdatedAt)), ("@id", id));
        ExperienceIndex.Put(conn, tx, id, text);   // текст сменился — индекс тоже (T-268-S0)
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

    /// <summary>
    /// ПЕРЕКЛЮЧИТЬ АКТИВНОСТЬ записи (T-265-S0) — отдельным действием, а не правкой всей
    /// записи: кнопка в строке списка меняет одно поле и не переписывает текст, навык и тэги
    /// значениями давно прочитанного списка (та же наука, что у переноса объекта, T-266).
    /// Правит только сервер-владелец записи, как правка и удаление (ТЗ гл. 6).
    /// <para>ПОСТАВЛЯЕМОЕ ОБЩЕЕ ПРАВИЛО (фиксированные id <c>a1e5b5c0-…</c>) погасить МОЖНО
    /// и намеренно: удалять его жалко, а вернуть удалённое сид не умеет — «неактивно» и есть
    /// тот способ убрать правило, которого раньше не было.</para>
    /// </summary>
    public ExperienceRecord SetActive(string id, bool isActive, string? actorId)
    {
        var record = Get(id) ?? throw new InvalidOperationException(Loc.T("msg.experience.1", id));
        EnsureMine(record);
        using var conn = _db.Open();
        var node = record.IsGeneral
            ? ("", "")
            : record.IsProjectLevel
                ? (record.ProjectId!, "")
                : TemplateNodeOrEmpty(conn, record.TemplateTaskId);
        record.IsActive = isActive;
        record.UpdatedBy = actorId;
        record.UpdatedAt = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx,
            "UPDATE experience SET is_active=@active, updated_by=@by, updated_at=@at WHERE id=@id",
            ("@active", isActive ? 1 : 0), ("@by", actorId),
            ("@at", Sql.ToDb(record.UpdatedAt)), ("@id", id));
        Append(conn, tx, EventTypes.ExperienceActivity, record, node, actorId);
        tx.Commit();
        return Get(id)!;
    }

    // --- НАБОРЫ ОПЫТА (T-270-S0) ---

    /// <summary>
    /// ЖИВЫЕ ЗАПИСИ С ЭТИМ ТЭГОМ — ПО ВСЕЙ ОРГАНИЗАЦИИ, любой области. Ею снятие набора
    /// опыта находит свои записи по служебной пометке владельца <c>pack:&lt;код&gt;</c>:
    /// область выбирал человек при установке, и искать записи «в общем опыте» или «в проекте
    /// N» пришлось бы наугад.
    /// </summary>
    public List<ExperienceRecord> ListByTag(string tag)
    {
        using var conn = _db.Open();
        var records = Sql.Query(conn, null, $"""
            {Select}
            WHERE e.deleted_at IS NULL AND EXISTS (
              SELECT 1 FROM experience_tags g
              WHERE g.experience_id = e.id AND g.tag = @tag COLLATE NOCASE)
            ORDER BY e.created_at
            """, Map, ("@tag", tag));
        foreach (var record in records)
        {
            Decorate(record);
        }
        LoadTagsFor(conn, records);
        return records;
    }

    /// <summary>Строка с таким идентификатором есть и НЕ УДАЛЕНА.</summary>
    public bool HasLiveRecord(string id)
    {
        using var conn = _db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM experience WHERE id=@id AND deleted_at IS NULL", ("@id", id)) > 0;
    }

    /// <summary>
    /// ЗАВЕСТИ ЗАПИСЬ С ЗАДАННЫМ ИДЕНТИФИКАТОРОМ В ВЫБРАННОЙ ОБЛАСТИ (T-270-S0) — этим
    /// ставится набор опыта. Идентификатор приходит ИЗ ФАЙЛА набора и постоянен: набор
    /// ставит каждый сервер сам, а таблица реплицируется — со случайными идентификаторами
    /// в организации копился бы второй комплект (та же наука, что у <see cref="GeneralSeed"/>
    /// и у записей справочника моделей, T-227).
    ///
    /// <para>СНЯТУЮ РАНЬШЕ ЗАПИСЬ ОЖИВЛЯЕТ, а не вставляет второй раз: снятие набора —
    /// мягкое удаление, и строка с этим идентификатором на месте; обычный INSERT упёрся бы
    /// в первичный ключ, и повторно поставить снятый набор стало бы нельзя вовсе.</para>
    ///
    /// <para>Проверку «текст выглядит проектным» (T-269-S0) записи набора проходят мимо, как
    /// и записи манифеста плагина: решение уже принял человек, нажав «Установить», а отказ
    /// посреди установки оставил бы набор поставленным наполовину.</para>
    /// </summary>
    public ExperienceRecord CreateWithId(string id, string scope, string? projectId,
        string? templateTaskId, string text, string? actorId, string? skillId = null,
        bool alwaysLoad = false, IEnumerable<string>? tags = null)
    {
        text = Require(text);
        scope = (scope ?? "").Trim().ToLowerInvariant();
        using var conn = _db.Open();
        (string ProjectId, string DisplayId) node = ("", "");
        string? newProject = null;
        var newTemplate = "";
        switch (scope)
        {
            case ScopeProject:
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    throw new ArgumentException(Loc.T("msg.experience.11"));
                }
                node = RequireProject(conn, projectId);
                newProject = projectId;
                break;
            case ScopeTemplate:
                if (string.IsNullOrWhiteSpace(templateTaskId))
                {
                    throw new ArgumentException(Loc.T("msg.experience.12"));
                }
                node = RequireTemplateNode(conn, templateTaskId);
                newTemplate = templateTaskId;
                break;
            case ScopeGeneral:
                break;
            default:
                throw new ArgumentException(Loc.T("msg.experience.9", scope,
                    ScopeProject, ScopeTemplate, ScopeGeneral));
        }
        var skill = Skill(conn, skillId);
        var record = new ExperienceRecord
        {
            Id = id,
            ProjectId = newProject,
            TemplateTaskId = newTemplate,
            SkillId = skill.Id,
            SkillName = skill.Name,
            Text = text,
            AlwaysLoad = alwaysLoad,
            IsActive = true,
            Tags = TaskTags.Normalize(tags?.ToList()),
            CreatedBy = actorId,
            UpdatedBy = actorId,
            ServerId = _scope.ServerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var known = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM experience WHERE id=@id", ("@id", id)) > 0;
        if (!known)
        {
            Insert(conn, record, node, actorId);
            return Get(record.Id)!;
        }
        using (var tx = conn.BeginTransaction())
        {
            Sql.Exec(conn, tx, """
                UPDATE experience SET template_task_id=@template, project_id=@project,
                                      skill_id=@skill, text=@text, always_load=@always,
                                      is_active=1, deleted_at=NULL, updated_by=@by,
                                      updated_at=@at, server_id=@server
                WHERE id=@id
                """,
                ("@template", newTemplate.Length > 0 ? newTemplate : null),
                ("@project", newProject), ("@skill", record.SkillId), ("@text", record.Text),
                ("@always", record.AlwaysLoad ? 1 : 0), ("@by", actorId),
                ("@at", Sql.ToDb(record.UpdatedAt)), ("@server", record.ServerId), ("@id", id));
            ExperienceIndex.Put(conn, tx, id, record.Text);
            SaveTags(conn, tx, record);
            Append(conn, tx, EventTypes.ExperienceRecorded, record, node, actorId);
            tx.Commit();
        }
        return Get(record.Id)!;
    }

    // --- ИСПОЛЬЗОВАННЫЙ ОПЫТ ЗАДАЧИ (T-266-S0) ---

    /// <summary>
    /// ЗАПОМНИТЬ, ЧТО ЗАПИСИ УШЛИ В ЗАДАНИЕ (T-266-S0). Зовётся из
    /// <c>JobOrchestrator.ExperienceSection</c> ровно с теми идентификаторами, которые
    /// напечатаны в блоках опыта, — после бюджета <c>ExperienceBudget.Fit</c> и без
    /// повторного отбора. Хранятся ТОЛЬКО идентификаторы: текст записи не копируется.
    ///
    /// <para>Задание по задаче запускают многократно (перезапуск, доработка), поэтому набор
    /// ДОПОЛНЯЕТСЯ: ключ «задача + запись», у уже известной пары обновляются <c>used_at</c>
    /// и задание. Отсюда и смысл счётчика использований — число ЗАДАЧ, а не запусков.</para>
    ///
    /// <para>Сбой записи следа НЕ СРЫВАЕТ задание: это журнал для человека и для анализа
    /// опыта, а не часть работы агента.</para>
    /// </summary>
    /// <returns>Сколько пар записано (новых и обновлённых).</returns>
    public int NoteUsed(string taskId, IEnumerable<string> experienceIds, string? jobId = null)
    {
        var ids = experienceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (string.IsNullOrWhiteSpace(taskId) || ids.Count == 0)
        {
            return 0;
        }
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            var at = Sql.ToDb(DateTime.UtcNow);
            foreach (var id in ids)
            {
                Sql.Exec(conn, tx, """
                    INSERT INTO task_experience_used (task_id, experience_id, job_id, used_at)
                    VALUES (@task, @exp, @job, @at)
                    ON CONFLICT(task_id, experience_id)
                    DO UPDATE SET job_id=@job, used_at=@at
                    """,
                    ("@task", taskId), ("@exp", id),
                    ("@job", string.IsNullOrWhiteSpace(jobId) ? null : jobId), ("@at", at));
            }
            tx.Commit();
            return ids.Count;
        }
        catch (SqliteException)
        {
            return 0;   // задача уже удалена, база занята — задание из-за журнала не срывается
        }
    }

    /// <summary>
    /// ЧТО СИСТЕМА ПОДСТАВИЛА В ЗАДАНИЕ ЭТОЙ ЗАДАЧИ (T-266-S0) — вкладка «Использованный
    /// опыт» карточки. Свежие использования сверху. Сама запись дочитывается из
    /// <c>experience</c> и бывает пустой (удалена, унесена в архив) — строка при этом
    /// остаётся: идентификатор и есть то, что мы обещали хранить.
    /// </summary>
    public List<ExperienceUse> ListUsedByTask(string taskId)
    {
        using var conn = _db.Open();
        var uses = Sql.Query(conn, null, """
            SELECT task_id, experience_id, job_id, used_at
            FROM task_experience_used WHERE task_id=@id
            ORDER BY used_at DESC, experience_id
            """,
            r => new ExperienceUse
            {
                TaskId = r.S("task_id"),
                ExperienceId = r.S("experience_id"),
                JobId = r.SN("job_id") ?? "",
                UsedAt = r.Dt("used_at"),
            }, ("@id", taskId));
        foreach (var use in uses)
        {
            use.Record = Get(use.ExperienceId);
        }
        return uses;
    }

    /// <summary>
    /// СТАТИСТИКА ИСПОЛЬЗОВАНИЯ записи (T-266-S0): сколько задач получило её в задании и
    /// когда это было в последний раз. Ею пользуется шаблон «Анализ опыта» — запись,
    /// которую не брали ни разу, кандидат в неактивные. Записи, которой не пользовались,
    /// отдаётся честный ноль, а не пустота: «не брали» — это тоже ответ.
    /// </summary>
    public ExperienceUsageStat UsageOf(string experienceId)
    {
        using var conn = _db.Open();
        var row = Sql.Query(conn, null, """
            SELECT COUNT(*) AS cnt, MAX(used_at) AS last_at
            FROM task_experience_used WHERE experience_id=@id
            """,
            r => new ExperienceUsageStat
            {
                ExperienceId = experienceId,
                Count = (int)r.L("cnt"),
                LastUsedAt = r.DtN("last_at"),
            }, ("@id", experienceId)).FirstOrDefault();
        return row ?? new ExperienceUsageStat { ExperienceId = experienceId };
    }

    /// <summary>
    /// СТАТИСТИКА СРАЗУ ПО ВСЕМ ЗАПИСЯМ (T-271-S0) — одним запросом, а не вызовом
    /// <see cref="UsageOf"/> на каждую строку: шаблон «Анализ опыта» разбирает опыт проекта
    /// пачками по сотне записей, и поход в базу на каждую обошёлся бы дороже самой выборки
    /// (та же причина, по которой тэги дочитываются одним запросом на список).
    /// <para>Записи, которая не уходила НИ В ОДНО задание, в ответе нет вовсе: «нуль» видно
    /// по её отсутствию, и таблица следа не растёт пустыми строками.</para>
    /// </summary>
    public Dictionary<string, ExperienceUsageStat> UsageMap()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT experience_id, COUNT(*) AS cnt, MAX(used_at) AS last_at
            FROM task_experience_used GROUP BY experience_id
            """,
            r => new ExperienceUsageStat
            {
                ExperienceId = r.S("experience_id"),
                Count = (int)r.L("cnt"),
                LastUsedAt = r.DtN("last_at"),
            }).ToDictionary(s => s.ExperienceId, StringComparer.Ordinal);
    }

    // --- ПОИСК ПО ОПЫТУ (T-268-S0) ---

    /// <summary>Область записи: опыт ПРОЕКТА (todo48). Сами слова — в
    /// <see cref="ExperienceScopes"/> (Core, T-269-S0): их одинаково называют хранилище,
    /// API, инструмент агента и UI.</summary>
    public const string ScopeProject = ExperienceScopes.Project;

    /// <summary>Область записи: опыт УЗЛА ШАБЛОНА (todo32).</summary>
    public const string ScopeTemplate = ExperienceScopes.Template;

    /// <summary>Область записи: ОБЩИЕ ПРАВИЛА РАБОТЫ организации (T-11-S0).</summary>
    public const string ScopeGeneral = ExperienceScopes.General;

    /// <summary>Сколько лексических попаданий берётся в разбор до отбора и слияния рангов.
    /// Больше незачем: выдаётся человеку и агенту десяток строк, а переставить их местами
    /// свежесть и тэги могут только внутри этого набора.</summary>
    private const int CandidateCap = 200;

    /// <summary>Постоянная слияния рангов (RRF). 60 — общепринятое значение: оно сглаживает
    /// разницу между первым и вторым местом, поэтому одинокое совпадение тэга не выталкивает
    /// наверх запись, у которой лексическое попадание слабое.</summary>
    private const double RrfK = 60;

    /// <summary>Область записи, как её называет поиск.</summary>
    public static string ScopeOf(ExperienceRecord record) =>
        record.IsGeneral ? ScopeGeneral : record.IsProjectLevel ? ScopeProject : ScopeTemplate;

    /// <summary>Лексический индекс на этой базе работает (FTS5 собран). Ложь — поиск идёт
    /// подстрочным сравнением: хуже, но работает.</summary>
    public bool SearchIndexReady
    {
        get
        {
            using var conn = _db.Open();
            return ExperienceIndex.Ready(conn);
        }
    }

    /// <summary>ДОГОНЯЮЩИЙ ПРОХОД по индексу (зовётся при открытии организации): записи
    /// приезжают репликацией прямо в таблицу, мимо этого сервиса, а индекс не реплицируется
    /// вовсе.</summary>
    /// <returns>Сколько записей добавлено в индекс.</returns>
    public int CatchUpSearchIndex()
    {
        using var conn = _db.Open();
        return ExperienceIndex.CatchUp(conn);
    }

    /// <summary>
    /// ПОИСК ПО ЗАПИСЯМ ОПЫТА (T-268-S0). Кандидаты берутся ТОЛЬКО из лексического попадания
    /// (порог отсечения), а порядок выдачи — слияние трёх рангов (RRF): BM25, свежесть
    /// правки и число совпавших с задачей тэгов. Подробности — в <see cref="ExperienceHit"/>.
    /// </summary>
    /// <param name="query">Слова запроса.</param>
    /// <param name="scope">Область: project / template / general; пусто — все.</param>
    /// <param name="limit">Сколько строк отдать (1..50).</param>
    /// <param name="includeInactive">Вместе с неактивными записями; по умолчанию ищет
    /// ТОЛЬКО ПО АКТИВНЫМ — неактивную запись агент не получает ни при каком раскладе,
    /// и находить её по умолчанию значило бы вернуть её в работу через чёрный ход.</param>
    /// <param name="projectId">Проект: берутся его записи, записи узлов его шаблонов и общие
    /// правила организации (их получает любая задача). Пусто — вся организация.</param>
    /// <param name="taskTags">Тэги задачи — прибавка за совпадение темы.</param>
    public List<ExperienceHit> Search(string query, string? scope = null, int limit = 10,
        bool includeInactive = false, string? projectId = null,
        IReadOnlyCollection<string>? taskTags = null)
    {
        query = (query ?? "").Trim();
        limit = Math.Clamp(limit <= 0 ? 10 : limit, 1, 50);
        if (query.Length == 0)
        {
            return [];
        }
        using var conn = _db.Open();
        var hits = ExperienceIndex.Ready(conn)
            ? ExperienceIndex.Match(conn, query, CandidateCap)
            : ExperienceIndex.MatchByScan(
                Sql.Query(conn, null, "SELECT id, text FROM experience WHERE deleted_at IS NULL",
                    r => (Id: r.S("id"), Text: r.S("text"))),
                query, CandidateCap);
        if (hits.Count == 0)
        {
            return [];
        }
        var bm25 = hits.ToDictionary(h => h.Id, h => h.Bm25, StringComparer.Ordinal);
        var names = hits.Select((_, i) => "@h" + i).ToList();
        var args = hits.Select((h, i) => ("@h" + i, (object?)h.Id)).ToArray();
        var loaded = Sql.Query(conn, null, $"""
            SELECT e.*, (SELECT s.name FROM skills s WHERE s.id = e.skill_id) AS skill_name,
                   (SELECT t.project_id FROM tasks t WHERE t.id = e.template_task_id) AS node_project_id
            FROM experience e
            WHERE e.id IN ({string.Join(", ", names)}) AND e.deleted_at IS NULL
            """, r => (Record: Map(r), NodeProject: r.SN("node_project_id") ?? ""), args);

        var scopeWanted = (scope ?? "").Trim().ToLowerInvariant();
        var kept = new List<ExperienceHit>();
        foreach (var (record, nodeProject) in loaded)
        {
            if (!includeInactive && !record.IsActive)
            {
                continue;
            }
            if (scopeWanted is ScopeProject or ScopeTemplate or ScopeGeneral
                && ScopeOf(record) != scopeWanted)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(projectId) && !record.IsGeneral
                && (record.IsProjectLevel ? record.ProjectId : nodeProject) != projectId)
            {
                continue;
            }
            kept.Add(new ExperienceHit { Record = Decorate(record), Bm25 = bm25[record.Id] });
        }
        LoadTagsFor(conn, kept.Select(h => h.Record).ToList());
        foreach (var hit in kept)
        {
            hit.TagHits = taskTags is null or { Count: 0 }
                ? 0
                : hit.Record.Tags.Count(tag => taskTags.Any(
                    t => string.Equals(t.Trim(), tag.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        }

        // СЛИЯНИЕ РАНГАМИ: каждый список даёт 1/(k + место). Список тэгов НЕПОЛНЫЙ — в нём
        // только записи, у которых совпадение есть: иначе «нет тэгов» тоже оказалось бы
        // местом в списке и приносило бы баллы
        var score = kept.ToDictionary(h => h.Record.Id, _ => 0.0, StringComparer.Ordinal);
        Fuse(kept.OrderBy(h => h.Bm25).ToList());
        Fuse(kept.OrderByDescending(h => h.Record.UpdatedAt).ToList());
        Fuse(kept.Where(h => h.TagHits > 0).OrderByDescending(h => h.TagHits).ToList());
        foreach (var hit in kept)
        {
            hit.Score = score[hit.Record.Id];
        }
        return kept
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Bm25)
            .Take(limit)
            .ToList();

        void Fuse(List<ExperienceHit> ordered)
        {
            for (var place = 0; place < ordered.Count; place++)
            {
                score[ordered[place].Record.Id] += 1.0 / (RrfK + place + 1);
            }
        }
    }

    /// <summary>
    /// СХОЖЕСТЬ ДВУХ ТЕКСТОВ (T-268-S0) — доля общих слов (Жаккар) по огрублённым основам.
    /// Эмбеддингов у нас нет (вариант В отложен), поэтому сравнение чисто лексическое:
    /// пересказ той же мысли ДРУГИМИ СЛОВАМИ оно не ловит и поймать не может.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        var left = ExperienceIndex.Stems(a).ToHashSet(StringComparer.Ordinal);
        var right = ExperienceIndex.Stems(b).ToHashSet(StringComparer.Ordinal);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }
        var common = left.Count(right.Contains);
        return (double)common / (left.Count + right.Count - common);
    }

    /// <summary>Порог «это та же самая запись»: новую заводить не надо, надо править старую.
    /// Взят СТРОГИМ намеренно — ложный отказ завести запись хуже дубля.</summary>
    public const double DuplicateRatio = 0.75;

    /// <summary>Порог «похоже на уже имеющуюся»: запись заводится, но помечается для ревизии
    /// человеком. Ниже порога отказа намеренно и заметно: мера Жаккара сурова к текстам
    /// разной длины — у записи вдвое длиннее общих слов не может быть больше половины,
    /// даже если она дословно включает первую.</summary>
    public const double SimilarRatio = 0.35;

    /// <summary>Тэг-пометка «похоже на запись …» (T-268-S0): по нему человек находит
    /// кандидатов на слияние. Вид <c>similar:&lt;id&gt;</c> — как служебная пометка владельца
    /// у записей плагина (<c>plugin:&lt;код&gt;</c>).</summary>
    public static string SimilarTag(string otherId) => "similar:" + otherId;

    /// <summary>Похожая запись и мера схожести.</summary>
    public sealed record SimilarFound(ExperienceRecord Record, double Ratio);

    /// <summary>
    /// САМАЯ ПОХОЖАЯ ЗАПИСЬ ТОЙ ЖЕ ОБЛАСТИ (§4.9 проекта опыта) — чем пользуется
    /// <c>create_experience</c> перед тем, как завести новую. Ищется по тексту самой записи:
    /// кандидатов даёт тот же лексический поиск, а решение принимается по <see
    /// cref="Similarity"/>. Неактивные тоже считаются: дубль погашенной записи — дубль.
    /// </summary>
    public SimilarFound? FindSimilar(string text, string? scope = null, string? projectId = null)
    {
        var best = Search(text, scope, limit: 10, includeInactive: true, projectId: projectId)
            .Select(hit => new SimilarFound(hit.Record, Similarity(text, hit.Record.Text)))
            .OrderByDescending(found => found.Ratio)
            .FirstOrDefault();
        return best is null || best.Ratio < SimilarRatio ? null : best;
    }

    // --- РАЗГРАНИЧЕНИЕ ОБЛАСТЕЙ И ПЕРЕНОС ЗАПИСИ (T-269-S0) ---

    /// <summary>
    /// НАЗВАНИЯ ПРОЕКТОВ организации — ими проверка «выглядит проектным»
    /// (<see cref="ExperienceScopeCheck"/>) ловит четвёртый признак. Имя ОРГАНИЗАЦИИ в её
    /// же базе не хранится вовсе (оно в реестре серверов), поэтому здесь только проекты.
    /// </summary>
    public List<string> ProjectNames()
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT name FROM projects WHERE deleted_at IS NULL", r => r.S("name"));
    }

    /// <summary>
    /// ПРИЗНАКИ ПРОЕКТНОГО в тексте, который хотят положить в ОБЩИЕ ПРАВИЛА РАБОТЫ
    /// (T-269-S0). Пустой список — текст на общее правило похож. Проверка дешёвая и без
    /// модели: она стоит на пути каждой записи в общий опыт, в том числе из инструмента
    /// агента.
    /// </summary>
    public List<ExperienceScopeCheck.Sign> GeneralSigns(string text) =>
        ExperienceScopeCheck.Signs(text, ProjectNames());

    /// <summary>
    /// Не пустить проектный текст в общие правила. <paramref name="force"/> — подтверждение
    /// человека из формы: ему предупреждение показывается с кнопкой «всё равно сохранить»,
    /// а агенту отказ окончательный (решение заказчика по T-269-S0: молчаливая подмена
    /// области сбила бы агента, который потом ищет запись по id).
    /// </summary>
    private void EnsureGeneralEnough(string text, bool force)
    {
        if (force)
        {
            return;
        }
        var signs = GeneralSigns(text);
        if (signs.Count > 0)
        {
            throw new ArgumentException(Loc.T("msg.experience.8",
                ExperienceScopeCheck.Samples(signs)));
        }
    }

    /// <summary>Запись — ПОСТАВЛЯЕМОЕ ПРАВИЛО ДИСТРИБУТИВА (<see cref="GeneralSeed"/>):
    /// её сеет каждый сервер сам по фиксированному идентификатору, поэтому перенести её
    /// в проект нельзя — на следующем же старте правило появилось бы в общих снова,
    /// и в организации оказалось бы два экземпляра одной строки.</summary>
    public static bool IsDistributionRule(string id) =>
        GeneralSeed.Any(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ПЕРЕНЕСТИ ЗАПИСЬ МЕЖДУ ОБЛАСТЯМИ (T-269-S0) — главное этой задачи. Меняется ТОЛЬКО
    /// привязка (проект / узел шаблона / ничто): идентификатор, текст, навык, тэги,
    /// авторство, время создания и признаки «загружать всегда» и «активна» остаются теми же.
    /// До этого перенести запись было нечем ни в сервисе, ни в API, ни в UI — только удалить
    /// и завести заново, потеряв id, историю и авторство; отсюда и мусор в общем опыте,
    /// который приходит КАЖДОЙ задаче организации.
    ///
    /// <para>Ограничения: переносит только сервер-ВЛАДЕЛЕЦ записи (как правка и удаление,
    /// ТЗ гл. 6) — мусор, приехавший с другого сервера, чистится на нём (решение заказчика
    /// по T-269-S0); поставляемое правило дистрибутива не переносится вовсе; перенос в проект
    /// требует ЖИВОГО проекта, в узел шаблона — живого узла-шаблона, а в общие правила —
    /// текста без признаков проектного (<paramref name="force"/> снимает эту проверку,
    /// её подтверждает человек в форме).</para>
    /// </summary>
    /// <param name="id">Запись.</param>
    /// <param name="scope">Куда: project / template / general.</param>
    /// <param name="projectId">Проект-получатель (scope=project).</param>
    /// <param name="templateTaskId">Узел шаблона-получатель (scope=template).</param>
    /// <param name="actorId">Кто переносит.</param>
    /// <param name="force">Подтверждение человека для переноса проектного текста в общие.</param>
    public ExperienceRecord Move(string id, string scope, string? projectId,
        string? templateTaskId, string? actorId, bool force = false)
    {
        var record = Get(id) ?? throw new InvalidOperationException(Loc.T("msg.experience.1", id));
        EnsureMine(record);
        scope = (scope ?? "").Trim().ToLowerInvariant();
        if (scope is not (ScopeProject or ScopeTemplate or ScopeGeneral))
        {
            throw new ArgumentException(Loc.T("msg.experience.9", scope,
                ScopeProject, ScopeTemplate, ScopeGeneral));
        }
        if (IsDistributionRule(record.Id))
        {
            throw new ArgumentException(Loc.T("msg.experience.10"));
        }
        using var conn = _db.Open();
        string? newProject = null;
        var newTemplate = "";
        (string ProjectId, string DisplayId) node = ("", "");
        switch (scope)
        {
            case ScopeProject:
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    throw new ArgumentException(Loc.T("msg.experience.11"));
                }
                node = RequireProject(conn, projectId);
                newProject = projectId;
                break;
            case ScopeTemplate:
                if (string.IsNullOrWhiteSpace(templateTaskId))
                {
                    throw new ArgumentException(Loc.T("msg.experience.12"));
                }
                node = RequireTemplateNode(conn, templateTaskId);
                newTemplate = templateTaskId;
                break;
            default:
                EnsureGeneralEnough(record.Text, force);
                break;
        }
        record.ProjectId = newProject;
        record.TemplateTaskId = newTemplate;
        record.UpdatedBy = actorId;
        record.UpdatedAt = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            UPDATE experience SET project_id=@project, template_task_id=@template,
                                  updated_by=@by, updated_at=@at
            WHERE id=@id
            """,
            ("@project", newProject), ("@template", newTemplate.Length > 0 ? newTemplate : null),
            ("@by", actorId), ("@at", Sql.ToDb(record.UpdatedAt)), ("@id", id));
        Append(conn, tx, EventTypes.ExperienceMoved, record, node, actorId);
        tx.Commit();
        return Get(id)!;
    }

    /// <summary>
    /// РЕВИЗИЯ ОБЩЕГО ОПЫТА (T-269-S0): общие правила, которые выглядят ПРОЕКТНЫМИ, —
    /// той же проверкой, что стоит на пути записи. Один проход по этому списку с переносом
    /// пачкой в выбранный проект — и общий опыт чистый.
    /// <para>Поставляемые правила дистрибутива в список не попадают: перенести их всё равно
    /// нельзя, и висеть в ревизии вечным упрёком им незачем.</para>
    /// </summary>
    public List<(ExperienceRecord Record, List<ExperienceScopeCheck.Sign> Signs)> GeneralSuspects()
    {
        var names = ProjectNames();
        return ListGeneral()
            .Where(r => !IsDistributionRule(r.Id))
            .Select(r => (Record: r, Signs: ExperienceScopeCheck.Signs(r.Text, names)))
            .Where(row => row.Signs.Count > 0)
            .ToList();
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
        ExperienceIndex.Remove(conn, tx, id);   // удалённую запись поиск не находит (T-268-S0)
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
        // АКТИВНА (T-265-S0): на базе, где колонки ещё нет (приехала от партнёра прежней
        // версии), запись считается АКТИВНОЙ — иначе обновление молча погасило бы весь опыт
        IsActive = !r.Has("is_active") || r.B("is_active"),
        CreatedBy = r.SN("created_by"),
        UpdatedBy = r.SN("updated_by"),
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
