using System.Diagnostics;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;
using Xunit.Abstractions;

namespace AI2P.Tests;

/// <summary>
/// T-363-S0 «Работа стала замедляться»: открытие карточки задачи шло 7–12 секунд.
///
/// Причина — чтение связей задачи ПОШТУЧНО: шесть запросов (исполнители, запасные, запасные
/// суфлёры, навыки, блокирующие, тэги) на КАЖДУЮ строку списка. Список задач проекта читают
/// и карточка, и форма правки, а диаграмма подзадач читала так вообще ВСЕ задачи организации,
/// хотя рисует одно поддерево.
///
/// Здесь проверяется и то, что правка ничего не потеряла (связи читаются те же), и то, ради
/// чего она сделана, — время. Порог намеренно мягкий (втрое): на разных машинах и под чужой
/// нагрузкой абсолютные числа пляшут, а разница между «одним запросом на таблицу» и «шестью
/// на задачу» на полутысяче задач исчисляется разами.
/// </summary>
public sealed class T363S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();
    private readonly ITestOutputHelper _out;

    public T363S0Tests(ITestOutputHelper output) => _out = output;

    public void Dispose() => _f.Dispose();

    /// <summary>Проект с задачами: <paramref name="count"/> штук, у каждой исполнитель и тэг,
    /// у части — родитель из уже созданных (получается настоящее дерево, как в работе).</summary>
    private (string ProjectId, List<TaskItem> Tasks) Seed(int count)
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var executor = _f.Executors.Create(
            new Executor { Nick = "исполнитель", Kind = ExecutorKind.Human }, null);
        var tasks = new List<TaskItem>(count);
        for (var i = 0; i < count; i++)
        {
            // каждая пятая — подзадача одной из предыдущих: дерево, а не плоский список
            var parent = i > 0 && i % 5 == 0 ? tasks[i / 5 - 1].Id : null;
            tasks.Add(_f.Tasks.Create(new TaskItem
            {
                Title = "Задача " + i,
                ProjectId = project.Id,
                ParentId = parent,
                Status = TaskStatuses.Pending,
                PriorityNum = 15,
                ExecutorIds = [executor.Id],
                Tags = ["тэг" + i % 7],
            }, "текст", "", null));
        }
        return (project.Id, tasks);
    }

    /// <summary>Сколько времени идёт действие (среднее по нескольким заходам — первый заход
    /// включает прогрев страниц базы и в замер не годится).</summary>
    private static double Millis(int times, Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < times; i++)
        {
            action();
        }
        return watch.Elapsed.TotalMilliseconds / times;
    }

    /// <summary>ПРЕЖНИЙ способ чтения связей — шесть запросов на задачу. Оставлен здесь
    /// нарочно: без него «стало быстрее» нечем доказать, а сравнение двух способов на одной
    /// и той же базе не зависит ни от машины, ни от нагрузки соседних прогонов.</summary>
    private int ReadLinksOneByOne(List<string> ids)
    {
        using var conn = _f.Db.Open();
        var rows = 0;
        foreach (var id in ids)
        {
            foreach (var (table, column) in new[]
                     {
                         ("task_executors", "executor_id"), ("task_alt_executors", "executor_id"),
                         ("task_prompter_alt_executors", "executor_id"), ("task_skills", "skill_id"),
                         ("task_blockers", "blocker_task_id"), ("task_tags", "tag"),
                     })
            {
                rows += Sql.Query(conn, null,
                    $"SELECT {column} AS val FROM {table} WHERE task_id=@id",
                    r => r.S("val"), ("@id", id)).Count;
            }
        }
        return rows;
    }

    [Fact]
    public void The_List_Of_A_Project_Reads_Links_By_Table_And_Not_By_Task()
    {
        var (projectId, tasks) = Seed(500);
        var ids = tasks.Select(t => t.Id).ToList();

        // 1. связи те же, что у поштучного чтения одной задачи
        var list = _f.Tasks.List(projectId);
        Assert.Equal(tasks.Count, list.Count);
        foreach (var item in list.Take(20))
        {
            var single = _f.Tasks.Get(item.Id)!;
            Assert.Equal(single.ExecutorIds, item.ExecutorIds);
            Assert.Equal(single.Tags, item.Tags);
            Assert.Equal(single.SkillIds, item.SkillIds);
            Assert.Equal(single.BlockerIds, item.BlockerIds);
        }

        // 2. время: весь список против одного лишь поштучного чтения связей
        var listMs = Millis(3, () => _f.Tasks.List(projectId));
        var oneByOneMs = Millis(3, () => ReadLinksOneByOne(ids));
        _out.WriteLine($"список {tasks.Count} задач: {listMs:F0} мс; "
                       + $"одни только связи поштучно: {oneByOneMs:F0} мс");
        // список ЦЕЛИКОМ (строки, счётчик вопросов, связи, сервер-владелец) обязан обходиться
        // дешевле, чем одни лишь связи прежним способом: иначе правка не состоялась
        Assert.True(listMs < oneByOneMs,
            $"список {listMs:F0} мс против поштучного чтения связей {oneByOneMs:F0} мс");
    }

    /// <summary>
    /// «Мы ли дирижёр» — это поход в СЕРВЕРНУЮ базу (ServerService.Conductor), а спрашивался
    /// он на КАЖДУЮ строку списка: из него считается пометка «только чтение». На боевой
    /// установке это и была самая дорогая часть открытия карточки — у фикстуры сервер один
    /// и ответ даётся без базы, поэтому цена в миллисекундах здесь не видна вовсе, и считать
    /// надо не время, а ОБРАЩЕНИЯ.
    /// </summary>
    [Fact]
    public void A_List_Does_Not_Ask_Who_Is_The_Conductor_On_Every_Row()
    {
        var (projectId, tasks) = Seed(60);
        var asked = 0;
        var scope = new ServerScope(() => "local", () => "", () =>
        {
            asked++;
            return true;
        });
        var service = new TaskService(_f.Db, _f.Events, _f.Files, scope);

        var list = service.List(projectId);

        Assert.Equal(tasks.Count, list.Count);
        Assert.All(list, item => Assert.False(item.IsReadOnly));
        _out.WriteLine($"дирижёрство спрошено {asked} раз на {list.Count} задач");
        Assert.True(asked <= 1, $"дирижёрство спрошено {asked} раз на {list.Count} задач");
    }

    [Fact]
    public void The_Diagram_Reads_The_Subtree_And_Not_Every_Task_Of_The_Org()
    {
        var (projectId, tasks) = Seed(400);
        // отдельный корень с небольшим поддеревом — его и рисует диаграмма
        var root = _f.Tasks.Create(new TaskItem
        {
            Title = "Корень", ProjectId = projectId, Status = TaskStatuses.Pending,
        }, "текст", "", null);
        var children = new List<TaskItem>();
        for (var i = 0; i < 5; i++)
        {
            children.Add(_f.Tasks.Create(new TaskItem
            {
                Title = "Подзадача " + i, ProjectId = projectId, ParentId = root.Id,
                Status = TaskStatuses.Pending, PriorityNum = 15 - i,
            }, "текст", "", null));
        }

        var diagram = _f.Tasks.Diagram(root.Id);
        Assert.Equal(children.Count + 1, diagram.Nodes.Count);
        Assert.Contains(diagram.Nodes, n => n.IsRoot && n.Id == root.Id);

        // БЛОКИРУЮЩАЯ ВНЕ ПОДДЕРЕВА (T-135-S0) обязана рисоваться по-прежнему — вместе со
        // своим поддеревом, хотя вся таблица задач больше не читается
        var blocker = tasks[0];
        var save = children[0];
        save.BlockerIds = [blocker.Id];
        _f.Tasks.Update(save, "текст", "", null);
        var withBlocker = _f.Tasks.Diagram(root.Id);
        Assert.Contains(withBlocker.Nodes, n => n.Id == blocker.Id && n.Outside);
        // потомки блокирующей (их даёт тот же добор поддерева) тоже на месте
        var blockerChild = tasks.First(t => t.ParentId == blocker.Id);
        Assert.Contains(withBlocker.Nodes, n => n.Id == blockerChild.Id);

        // в диаграмме — ТОЛЬКО поддерево корня и поддерево блокирующей: посторонняя задача
        // организации (ни родителя, ни связи с ними) в неё не попадает, а прежде все задачи
        // организации читались вместе со связями каждой
        var stranger = tasks.First(t => t.ParentId is null && t.Id != blocker.Id);
        Assert.DoesNotContain(withBlocker.Nodes, n => n.Id == stranger.Id);
        Assert.True(withBlocker.Nodes.Count < tasks.Count / 2,
            $"в диаграмме {withBlocker.Nodes.Count} узлов при {tasks.Count} задачах организации");

        var diagramMs = Millis(3, () => _f.Tasks.Diagram(root.Id));
        _out.WriteLine($"диаграмма поддерева из {diagram.Nodes.Count} узлов "
                       + $"при {tasks.Count} задачах в организации: {diagramMs:F0} мс");
    }
}
