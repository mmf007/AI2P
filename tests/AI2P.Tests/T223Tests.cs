using System.Text.Json;
using AI2P.Core.Entities;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-223 (версия 1.84): СОРТИРОВКА В ИЕРАРХИИ ЗАДАЧ ПО ВРЕМЕНИ ИЗМЕНЕНИЯ.
///
/// 1. К вариантам сортировки добавлены два: «по времени изменения, возрастание» и
///    «по времени изменения, убывание» (<c>updated_asc</c> / <c>updated_desc</c>).
///    Опора — <see cref="Entity.UpdatedAt"/> (колонка <c>tasks.updated_at</c>), которую
///    двигает любая правка задачи, включая смену статуса и перенос по иерархии.
/// 2. Варианты и компараторы жили ДВАЖДЫ — своим <c>switch</c> в <c>TasksView.razor</c>
///    и в <c>TemplatesView.razor</c>; теперь они общие (<see cref="TaskSort"/>), поэтому
///    новый вариант появляется сразу в обоих списках и разойтись они не могут.
///
/// Меню и порядок строк на экране проверяются живой проверкой в браузере (<c>test/t223</c>).
/// </summary>
public sealed class T223Tests
{
    private static TaskItem Task(string title, DateTime updated, DateTime? created = null) => new()
    {
        Title = title,
        UpdatedAt = updated,
        CreatedAt = created ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static string NoNick(TaskItem _) => "";

    /// <summary>Список задач в порядке выбранного варианта — как его увидит иерархия.</summary>
    private static List<string> Order(string? key, params TaskItem[] tasks) =>
        TaskSort.Sorted([.. tasks], key, NoNick).Select(t => t.Title).ToList();

    // ---------- два новых варианта ----------

    [Fact]
    public void Updated_Asc_Puts_The_Oldest_Change_First()
    {
        var order = Order("updated_asc",
            Task("вчера", new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc)),
            Task("давно", new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc)),
            Task("только что", new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(["давно", "вчера", "только что"], order);
    }

    [Fact]
    public void Updated_Desc_Puts_The_Freshest_Change_First()
    {
        var order = Order("updated_desc",
            Task("вчера", new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc)),
            Task("давно", new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc)),
            Task("только что", new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(["только что", "вчера", "давно"], order);
    }

    /// <summary>Время ИЗМЕНЕНИЯ — не время создания: у старой, но недавно правленной задачи
    /// они расходятся, и на этом видно, что сортируется именно нужное поле.</summary>
    [Fact]
    public void Update_Time_Is_Not_Creation_Time()
    {
        var old = Task("старая, правлена сегодня",
            updated: new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            created: new DateTime(2025, 3, 1, 9, 0, 0, DateTimeKind.Utc));
        var fresh = Task("новая, больше не трогали",
            updated: new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
            created: new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(["старая, правлена сегодня", "новая, больше не трогали"],
                     Order("updated_desc", old, fresh));
        Assert.Equal(["новая, больше не трогали", "старая, правлена сегодня"],
                     Order("created_desc", old, fresh));
    }

    [Fact]
    public void Both_New_Options_Are_In_The_Menu_Right_After_Creation_Time()
    {
        Assert.Contains("updated_asc", TaskSort.Keys);
        Assert.Contains("updated_desc", TaskSort.Keys);
        // рядом с «по времени создания»: два варианта времени стоят вместе
        Assert.Equal(Array.IndexOf(TaskSort.Keys, "created_desc") + 1,
                     Array.IndexOf(TaskSort.Keys, "updated_asc"));
        Assert.Equal(Array.IndexOf(TaskSort.Keys, "updated_asc") + 1,
                     Array.IndexOf(TaskSort.Keys, "updated_desc"));
    }

    // ---------- прежние варианты не сломаны ----------

    [Fact]
    public void Unknown_Key_And_No_Sorting_Keep_The_Server_Order()
    {
        Assert.Null(TaskSort.Comparison(null, NoNick));
        Assert.Null(TaskSort.Comparison("нет такого варианта", NoNick));
        var tasks = new List<TaskItem> { Task("б", DateTime.UtcNow), Task("а", DateTime.UtcNow) };
        // «без сортировки» — тот же список, а не копия в другом порядке
        Assert.Same(tasks, TaskSort.Sorted(tasks, null, NoNick));
    }

    [Fact]
    public void Every_Option_Of_The_Menu_Has_A_Comparator()
    {
        foreach (var key in TaskSort.Keys)
        {
            Assert.True(TaskSort.Comparison(key, NoNick) is not null, $"нет компаратора: {key}");
        }
    }

    /// <summary>Задачи без срока стоят в конце при ЛЮБОМ направлении (правило ТЗ v1.17):
    /// вынос компараторов в общий класс его не изменил.</summary>
    [Fact]
    public void Tasks_Without_A_Due_Date_Stay_Last_In_Both_Directions()
    {
        var withDue = new TaskItem { Title = "со сроком", DueDate = new DateTime(2026, 5, 1) };
        var noDue = new TaskItem { Title = "без срока" };

        Assert.Equal(["со сроком", "без срока"], Order("due_asc", noDue, withDue));
        Assert.Equal(["со сроком", "без срока"], Order("due_desc", noDue, withDue));
    }

    // ---------- подписи и общий список у обоих представлений ----------

    [Theory]
    [InlineData("tasks.sort.updated_asc")]
    [InlineData("tasks.sort.updated_desc")]
    public void Labels_Exist_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустая подпись {key}");
        }
    }

    /// <summary>У КАЖДОГО варианта меню есть подпись в обоих словарях: пропущенный ключ
    /// показал бы в меню сам ключ (Loc возвращает ключ, если текста нет).</summary>
    [Fact]
    public void Every_Option_Has_A_Label_In_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in TaskSort.Keys.Append("none"))
            {
                Assert.True(doc.RootElement.TryGetProperty("tasks.sort." + key, out _),
                            $"{lang}: нет подписи tasks.sort.{key}");
            }
        }
    }

    /// <summary>Оба представления с иерархией берут варианты из общего списка, а не из своего:
    /// иначе новый вариант появлялся бы только в одном из них.</summary>
    [Theory]
    [InlineData("TasksView.razor")]
    [InlineData("TemplatesView.razor")]
    public void Views_Take_The_Options_From_The_Shared_List(string file)
    {
        var text = File.ReadAllText(
            Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI", "Components", file));
        Assert.Contains("TaskSort.Keys", text);
        Assert.Contains("TaskSort.Comparison(", text);
        // своего списка ключей у представления не осталось
        Assert.DoesNotContain("\"created_desc\" =>", text);
    }

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }
}
