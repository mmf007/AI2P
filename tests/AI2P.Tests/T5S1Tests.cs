using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-5-S1: РЕПЛИКАЦИЯ ТЭГОВ ЗАДАЧИ.
///
/// Жалоба: тэг введён на S0, а в задаче на S1 его нет.
///
/// Причина та же, что у пропадающего состава команды (T-3-S1): справочника у тэгов нет,
/// они живут строками <c>task_tags</c>, а сохранение задачи переписывает все её связи
/// приёмом «снести все строки и вставить заново». Удаление и следующая за ним вставка
/// ТОЙ ЖЕ строки попадали в одну миллисекунду, получали одинаковые часы, и приёмник
/// отбрасывал вставку как «не новее» — тэг пропадал у партнёра насовсем.
///
/// Здесь проверяется жизненный цикл тэга целиком, потому что каждый его шаг — это своя пара
/// «удалить/вставить»: добавить первый тэг к уже уехавшей задаче, добавить второй (первый при
/// этом переписывается), снять один, вернуть снятый, сохранить задачу не трогая тэги.
/// Плюс список тэгов организации: справочника нет, он считается по этой же таблице,
/// поэтому потерянная строка видна и там.
/// </summary>
public sealed class T5S1Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t5s1-" + Guid.NewGuid().ToString("N"));

    private readonly Database _a;
    private readonly Database _b;
    private long _cursor;

    private const string NodeA = "unid-S0";
    private const string NodeB = "unid-S1";

    public T5S1Tests()
    {
        _a = new Database(Path.Combine(_dir, "a"), "ai2p.db");
        _a.Init(NodeA);
        _b = new Database(Path.Combine(_dir, "b"), "ai2p.db");
        _b.Init(NodeB);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private TaskService Tasks(Database db, string serverId, string code, bool conductor) =>
        new(db, new EventStore(db), new FileStore(db.DataDir),
            new ServerScope(() => serverId, () => code, () => conductor));

    private TaskService TasksA => Tasks(_a, NodeA, "S0", conductor: true);

    private TaskService TasksB => Tasks(_b, NodeB, "S1", conductor: false);

    /// <summary>Такт репликации S0 → S1: перенести журнал источника в приёмник.</summary>
    private void Sync()
    {
        List<RowChange> items;
        using (var conn = _a.Open())
        {
            items = ChangeLog.Read(conn, _cursor, 500, NodeB);
        }
        if (items.Count == 0)
        {
            return;
        }
        ChangeLog.Apply(_b, items);
        _cursor = items[^1].Seq;
    }

    /// <summary>
    /// Такт репликации по ОДНОЙ записи: так журнал читала старая версия, у которой удаление
    /// и вставка одной строки расходились по разным пачкам (граница пачки), и схлопывание
    /// пачки (<c>ChangeLog.Collapse</c>) их спасти не могло.
    /// </summary>
    private void SyncOneByOne()
    {
        List<RowChange> items;
        using (var conn = _a.Open())
        {
            items = ChangeLog.Read(conn, _cursor, 500, NodeB);
        }
        foreach (var item in items)
        {
            ChangeLog.Apply(_b, [item]);
            _cursor = item.Seq;
        }
    }

    /// <summary>Тэги задачи так, как их видит партнёр (порядок — как у чтения карточки).</summary>
    private static List<string> TagsOf(Database db, string taskId)
    {
        using var conn = db.Open();
        return Sql.Query(conn, null,
            "SELECT tag FROM task_tags WHERE task_id=@t ORDER BY tag", r => r.S("tag"),
            ("@t", taskId));
    }

    /// <summary>Сохранить задачу с заданным набором тэгов — ровно то, что делает форма.</summary>
    private TaskItem Save(TaskItem task, IEnumerable<string> tags, string? title = null)
    {
        task.Tags = tags.ToList();
        if (title is not null)
        {
            task.Title = title;
        }
        return TasksA.Update(task, "описание", "", null);
    }

    private TaskItem TaskWithoutTags()
    {
        var task = TasksA.Create(new TaskItem { Title = "Задача без тэгов" }, "описание", "", null);
        Sync();
        Assert.Empty(TagsOf(_b, task.Id));
        return task;
    }

    // --- 1. жалоба задания: один тэг, добавленный к уже уехавшей задаче ---

    [Fact]
    public void A_Single_Tag_Added_To_An_Already_Replicated_Task_Reaches_The_Partner()
    {
        var task = TaskWithoutTags();

        Save(task, ["альфа"]);
        Sync();

        Assert.Equal(["альфа"], TagsOf(_b, task.Id));
    }

    // --- 2. второй тэг не сносит первый: первый переписывается «снести и вставить» ---

    [Fact]
    public void A_Second_Tag_Does_Not_Take_The_First_One_With_It()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа"]);
        Sync();

        Save(task, ["альфа", "бета"]);
        Sync();

        Assert.Equal(["альфа", "бета"], TagsOf(_b, task.Id));
    }

    // --- 3. снятый тэг уезжает как снятый ---

    [Fact]
    public void A_Removed_Tag_Leaves_The_Partner_Too()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа", "бета"]);
        Sync();

        Save(task, ["бета"]);
        Sync();

        Assert.Equal(["бета"], TagsOf(_b, task.Id));
    }

    // --- 4. снятый и заведённый заново тэг возвращается ---
    // у партнёра в журнале для этой строки уже лежит УДАЛЕНИЕ, поэтому вставка обязана
    // быть строго новее его — иначе тэг не вернётся никогда

    [Fact]
    public void A_Tag_Removed_And_Added_Again_Comes_Back()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа", "бета"]);
        Sync();
        Save(task, ["бета"]);
        Sync();

        Save(task, ["альфа", "бета"]);
        Sync();

        Assert.Equal(["альфа", "бета"], TagsOf(_b, task.Id));
    }

    // --- 5. сохранение задачи, не трогая тэги, их не теряет ---

    [Fact]
    public void Saving_A_Task_Without_Touching_Its_Tags_Keeps_Them_At_The_Partner()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа", "бета", "гамма"]);
        Sync();

        for (var i = 1; i <= 5; i++)
        {
            Save(task, ["альфа", "бета", "гамма"], "Задача (правка " + i + ")");
        }
        Sync();

        Assert.Equal(["альфа", "бета", "гамма"], TagsOf(_b, task.Id));
    }

    // --- 6. список тэгов организации: справочника нет, он считается по task_tags ---

    [Fact]
    public void The_Organisation_Tag_List_Matches_At_The_Partner()
    {
        var first = TaskWithoutTags();
        Save(first, ["выпуск", "репликация"]);
        var second = TasksA.Create(new TaskItem { Title = "Вторая", Tags = ["тэг"] },
            "описание", "", null);
        Save(second, ["тэг", "выпуск"]);
        Sync();

        Assert.Equal(["выпуск", "репликация", "тэг"], TasksA.ListTags());
        Assert.Equal(TasksA.ListTags(), TasksB.ListTags());
    }

    // --- 7. журнал: у строки тэга не бывает двух изменений одними часами ---

    [Fact]
    public void Two_Changes_Of_One_Tag_Row_Never_Share_The_Same_Clock()
    {
        var task = TaskWithoutTags();
        for (var i = 1; i <= 10; i++)
        {
            // каждое сохранение — это удаление строки тэга и её же вставка одной транзакцией
            Save(task, ["альфа"], "Задача (правка " + i + ")");
        }

        using var conn = _a.Open();
        var stamps = Sql.Query(conn, null,
            "SELECT ts, node_id FROM changes WHERE tbl='task_tags' ORDER BY seq",
            r => r.S("ts") + " " + r.S("node_id"));
        // 1 вставка + 9 пар «удаление/вставка» + удаление первой правки = 19 записей
        Assert.Equal(19, stamps.Count);
        Assert.Equal(stamps.Count, stamps.Distinct().Count());
    }

    // --- 8. пачка от партнёра СТАРОЙ версии: удаление и вставка тэга одними часами ---
    // на 1.85 так выглядит каждое сохранение задачи; пачка обязана пережить это

    [Fact]
    public void A_Tag_Batch_From_An_Old_Partner_Keeps_The_Tag()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа"]);
        Sync();

        // часы заведомо новее наших: иначе пачку отвергнет сравнение часов и проверка
        // пройдёт, ничего не проверив
        const string stamp = "2099-01-01T00:00:00.1230000Z";
        var pk = task.Id + "|альфа";
        var payload = "{\"task_id\":\"" + task.Id + "\",\"tag\":\"альфа\"}";
        ChangeLog.Apply(_b,
        [
            new RowChange
            {
                Seq = 1, Table = "task_tags", Pk = pk, Op = ChangeOps.Delete,
                Ts = stamp, NodeId = NodeA, PayloadJson = payload,
            },
            new RowChange
            {
                Seq = 2, Table = "task_tags", Pk = pk, Op = ChangeOps.Upsert,
                Ts = stamp, NodeId = NodeA, PayloadJson = payload,
            },
        ]);

        Assert.Equal(["альфа"], TagsOf(_b, task.Id));
    }

    // --- 9. тэг, УЖЕ потерянный партнёром, возвращается шагом обновления 86 ---
    // это состояние настоящей установки заказчика: тэг пропал месяц назад, курсор партнёра
    // давно ушёл вперёд, и сами по себе те записи он больше не прочитает

    [Fact]
    public void An_Already_Lost_Tag_Comes_Back_With_Upgrade_Step_86()
    {
        var task = TaskWithoutTags();
        Save(task, ["альфа", "бета"]);

        // журнал, накопленный версией 1.85: сохранение задачи переписало строку тэга, и
        // удаление со вставкой получили одни часы. Своими часами так уже не пишется, поэтому
        // пара подделывается; часы заведомо новее настоящих, иначе её отвергнет сравнение
        // часов и проверка пройдёт, ничего не проверив
        const string stamp = "2099-01-01T00:00:00.1230000Z";
        var pk = task.Id + "|альфа";
        var payload = "{\"task_id\":\"" + task.Id + "\",\"tag\":\"альфа\"}";
        using (var conn = _a.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO changes (tbl, pk, op, ts, node_id, payload_json) VALUES
                  ('task_tags', @pk, 'delete', @ts, @me, @p),
                  ('task_tags', @pk, 'upsert', @ts, @me, @p)
                """, ("@pk", pk), ("@ts", stamp), ("@me", NodeA), ("@p", payload));
        }

        SyncOneByOne();
        // ровно жалоба задания: у нас тэг есть, у партнёра его нет
        Assert.Equal(["альфа", "бета"], TagsOf(_a, task.Id));
        Assert.Equal(["бета"], TagsOf(_b, task.Id));

        Assert.Equal(1, ChangeLog.ResendTiedRows(_a));
        Sync();

        Assert.Equal(["альфа", "бета"], TagsOf(_b, task.Id));

        // повторный старт называет ту же строку тем же значением — состояние не меняется
        Assert.Equal(1, ChangeLog.ResendTiedRows(_a));
        Sync();
        Assert.Equal(["альфа", "бета"], TagsOf(_b, task.Id));
    }
}
