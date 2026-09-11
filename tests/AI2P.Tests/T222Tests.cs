using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-222 (версия 1.84): ТЭГИ — КЛЮЧЕВЫЕ СЛОВА ЗАДАЧ.
///
/// 1. Форма задачи: в разделе «Расширенное» поле множественного выбора тэгов с вводом новых.
/// 2. Список задач: представление «тэги» — категория по тэгу, внутри обычный список
///    как в таблице; ПРИ ЗАГРУЗКЕ ВСЕ КАТЕГОРИИ СВЁРНУТЫ.
/// 3. Фильтр списка задач: множественный выбор тэгов.
/// 4. Эксплорер: ветки «проект/задачи/тэги» и «проект/шаблоны/тэги», далее список.
///
/// Справочника у тэгов нет намеренно: тэг заводится тем, что его написали в форме, и
/// исчезает, когда его не осталось ни у одной задачи. Набор тэгов организации — DISTINCT
/// по таблице <c>task_tags</c>.
///
/// Хранилище и раскладка по категориям проверяются здесь по-настоящему; разметка — по файлу
/// (приём <see cref="T209Tests"/> и <see cref="T217Tests"/>), поведение в браузере —
/// живой проверкой <c>test/t222</c>.
/// </summary>
public sealed class T222Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private TaskItem NewTask(string title, IEnumerable<string> tags, bool isTemplate = false) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            IsTemplate = isTemplate,
            Tags = tags.ToList(),
        }, title, "", null);

    // ---------- 1. приведение тэгов (общее правило ядра) ----------

    /// <summary>Пробелы по краям снимаются, пустые отбрасываются.</summary>
    [Fact]
    public void Normalize_Trims_And_Drops_Empty()
    {
        Assert.Equal(["ui", "сборка"], TaskTags.Normalize(["  ui ", "", "   ", "сборка"]));
    }

    /// <summary>Запятая — РАЗДЕЛИТЕЛЬ: «ui, сборка» в одном поле даёт два тэга, а тэга
    /// с запятой внутри не существует вовсе (иначе его нельзя было бы передать фильтром).</summary>
    [Fact]
    public void A_Comma_Separates_Tags()
    {
        Assert.Equal(["ui", "сборка", "выпуск"], TaskTags.Normalize("ui, сборка,выпуск"));
        Assert.DoesNotContain(TaskTags.Normalize("a,b"), t => t.Contains(','));
    }

    /// <summary>Повтор снимается без учёта регистра, а ПЕРВОЕ написание сохраняется:
    /// тэг пишет человек, и «UI» не должен превращаться в «ui».</summary>
    [Fact]
    public void A_Duplicate_Is_Dropped_Ignoring_Case_But_The_Spelling_Survives()
    {
        Assert.Equal(["UI"], TaskTags.Normalize(["UI", "ui", " Ui "]));
    }

    // ---------- 2. хранилище ----------

    [Fact]
    public void Tags_Are_Saved_And_Read_Back()
    {
        var task = NewTask("Задача с тэгами", ["ui", "сборка"]);

        var loaded = _f.Tasks.Get(task.Id)!;
        // порядок один и тот же при каждом чтении: ORDER BY tag — побайтовый, латиница раньше
        Assert.Equal(["ui", "сборка"], loaded.Tags);
    }

    /// <summary>Правка задачи ЗАМЕНЯЕТ набор тэгов, а не дополняет его: снятый в форме тэг
    /// обязан исчезнуть, иначе снять его было бы нечем.</summary>
    [Fact]
    public void Update_Replaces_The_Set_Of_Tags()
    {
        var task = NewTask("Задача", ["ui", "сборка"]);

        task.Tags = ["выпуск"];
        _f.Tasks.Update(task, "текст", "", null);

        Assert.Equal(["выпуск"], _f.Tasks.Get(task.Id)!.Tags);
    }

    /// <summary>В базу тэги уходят уже приведёнными — правило одно и для формы, и для записи.</summary>
    [Fact]
    public void Saving_Normalizes_The_Tags()
    {
        var task = NewTask("Задача", ["  ui  ", "UI", "", "сборка, выпуск"]);

        Assert.Equal(["ui", "выпуск", "сборка"],
                     _f.Tasks.Get(task.Id)!.Tags.OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>Набор тэгов организации — это DISTINCT по живым задачам: своего справочника
    /// у тэгов нет, поэтому «удалить тэг» — это снять его с последней задачи.</summary>
    [Fact]
    public void The_Tag_List_Is_Distinct_Over_Live_Tasks()
    {
        var first = NewTask("Первая", ["ui", "сборка"]);
        NewTask("Вторая", ["ui"]);
        NewTask("Узел шаблона", ["выпуск"], isTemplate: true);

        // порядок списка — алфавитный по культуре установки, поэтому здесь сверяется СОСТАВ
        Assert.Equal(["ui", "выпуск", "сборка"],
                     _f.Tasks.ListTags().OrderBy(t => t, StringComparer.Ordinal));

        // снят с последней задачи — тэга больше нет
        first.Tags = ["ui"];
        _f.Tasks.Update(first, "текст", "", null);
        Assert.Equal(["ui", "выпуск"],
                     _f.Tasks.ListTags().OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>Удалённая задача из списка тэгов уходит вместе со своими тэгами.</summary>
    [Fact]
    public void A_Deleted_Task_Takes_Its_Tags_Out_Of_The_List()
    {
        var task = NewTask("Задача", ["одинокий"]);
        Assert.Contains("одинокий", _f.Tasks.ListTags());

        _f.Tasks.Delete(task.Id, null);

        Assert.DoesNotContain("одинокий", _f.Tasks.ListTags());
    }

    // ---------- 3. фильтр по тэгам ----------

    [Fact]
    public void The_Filter_Keeps_Only_Tasks_With_The_Tag()
    {
        NewTask("Про интерфейс", ["ui"]);
        NewTask("Про сборку", ["сборка"]);
        NewTask("Без тэгов", []);

        var found = _f.Tasks.List(tags: ["ui"]);

        Assert.Equal("Про интерфейс", Assert.Single(found).Title);
    }

    /// <summary>Несколько тэгов соединяются по ИЛИ: человек отбирает «всё про UI или про
    /// сборку», а пересечение почти всегда пусто и выглядело бы как поломка фильтра.</summary>
    [Fact]
    public void Several_Tags_Are_Joined_By_Or()
    {
        NewTask("Про интерфейс", ["ui"]);
        NewTask("Про сборку", ["сборка"]);
        NewTask("Про оба", ["ui", "сборка"]);
        NewTask("Без тэгов", []);

        var found = _f.Tasks.List(tags: ["ui", "сборка"]);

        Assert.Equal(["Про интерфейс", "Про оба", "Про сборку"],
                     found.Select(t => t.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>Пустой фильтр тэгов ничего не отсекает — иначе список задач пустел бы
    /// у всех, кто тэгами не пользуется.</summary>
    [Fact]
    public void An_Empty_Tag_Filter_Filters_Nothing()
    {
        NewTask("Про интерфейс", ["ui"]);
        NewTask("Без тэгов", []);

        Assert.Equal(2, _f.Tasks.List(tags: []).Count);
        Assert.Equal(2, _f.Tasks.List().Count);
    }

    // ---------- 4. тэги узла шаблона переезжают в созданную задачу ----------

    [Fact]
    public void Tags_Of_A_Template_Node_Go_Into_The_Created_Task()
    {
        var template = NewTask("Шаблон выпуска", ["выпуск", "ci"], isTemplate: true);

        var created = _f.Tasks.InstantiateTemplate(template.Id, null, null);

        Assert.Equal(["ci", "выпуск"], _f.Tasks.Get(created.Id)!.Tags);
    }

    // ---------- 5. раскладка по категориям (представление «Тэги») ----------

    private static TaskItem Card(string title, params string[] tags) =>
        new() { Id = "id-" + title, Title = title, Tags = tags.ToList() };

    /// <summary>Категории — по алфавиту без учёта регистра, «без тэгов» — последней:
    /// иначе задачи без тэгов терялись бы, и список был бы короче остальных представлений.</summary>
    [Fact]
    public void Categories_Go_Alphabetically_And_No_Tag_Is_Last()
    {
        var groups = TaskTagGroups.Build([
            Card("Без тэгов"),
            Card("Про сборку", "сборка"),
            Card("Про интерфейс", "ui"),
        ]);

        Assert.Equal(["сборка", "ui", TaskTagGroups.NoTag], groups.Select(g => g.Tag));
        Assert.Equal("Без тэгов", Assert.Single(groups[^1].Tasks).Title);
    }

    /// <summary>Задача с несколькими тэгами попадает в КАЖДУЮ свою категорию: тэги —
    /// ключевые слова, а не взаимоисключающие корзины.</summary>
    [Fact]
    public void A_Task_Lands_In_Every_Category_Of_Its_Tags()
    {
        var groups = TaskTagGroups.Build([Card("Про оба", "ui", "сборка")]);

        Assert.Equal(["сборка", "ui"], groups.Select(g => g.Tag));
        Assert.All(groups, g => Assert.Equal("Про оба", Assert.Single(g.Tasks).Title));
    }

    /// <summary>Порядок задач внутри категории — тот, в котором они пришли: это порядок
    /// выбранной сортировки тулбара, и второй раз пересортировывать его нельзя.</summary>
    [Fact]
    public void Inside_A_Category_The_Incoming_Order_Is_Kept()
    {
        var groups = TaskTagGroups.Build([
            Card("Я", "ui"), Card("А", "ui"), Card("М", "ui"),
        ]);

        Assert.Equal(["Я", "А", "М"], Assert.Single(groups).Tasks.Select(t => t.Title));
    }

    [Fact]
    public void An_Empty_List_Gives_No_Categories() => Assert.Empty(TaskTagGroups.Build([]));

    // ---------- 6. разметка ----------

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

    private static string Ui(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "AI2P_app", "src", "AI2P.UI", .. parts]));

    /// <summary>Поле тэгов стоит именно в «Расширенном» — так просило задание.</summary>
    [Fact]
    public void The_Tag_Field_Is_In_The_Extended_Section()
    {
        var form = Ui("Components", "TaskDialog.razor");
        var extended = form.IndexOf("L[\"task.form.extended\"]", StringComparison.Ordinal);
        Assert.True(extended > 0, "в форме нет раздела «Расширенное»");
        var field = form.IndexOf("L[\"task.tags\"]", StringComparison.Ordinal);
        Assert.True(field > extended, "поле тэгов не в разделе «Расширенное»");

        // множественный выбор + ввод нового тэга рядом
        Assert.Contains("MultiSelection=\"true\" SelectedValues=\"@_tags\"", form);
        Assert.Contains("data-tag-new=\"1\"", form);
        Assert.Contains("data-tag-add=\"1\"", form);
        // набранное, но не добавленное «+» не теряется при сохранении
        Assert.Contains("TaskTags.Normalize(_tags.Concat([_newTag]))", form);
    }

    /// <summary>Представление «тэги» есть в списке представлений и рисуется своим компонентом.</summary>
    [Fact]
    public void The_Tasks_List_Has_A_Tags_View()
    {
        var view = Ui("Components", "TasksView.razor");

        Assert.Contains("L[\"tasks.view.tags\"]", view);
        Assert.Contains("<TaskTagsView", view);
        Assert.Contains("TagsKey = \"tags\"", view);
    }

    /// <summary>Фильтр по тэгам — множественный выбор, и он попадает и в сводку свёрнутого
    /// фильтра, и в кнопку «очистить», и в сохраняемое состояние (как остальные поля).</summary>
    [Fact]
    public void The_Filter_Has_A_Multi_Select_Of_Tags()
    {
        var view = Ui("Components", "TasksView.razor");

        Assert.Contains("L[\"tasks.filter.tags\"]", view);
        Assert.Contains("data-filter-tags=\"1\"", view);
        Assert.Contains("SelectedValuesChanged=\"OnTagsFilterChanged\"", view);
        Assert.Contains("tags: _tags.Count == 0 ? null : _tags", view);
        Assert.Contains("_tags.Clear()", view);          // очистка всего фильтра
        Assert.Contains("_saved.Tags = _tags.ToList()", view);  // переживает перезапуск
    }

    /// <summary>Все категории закрыты при загрузке: набор открытых живёт в компоненте и
    /// НЕ кладётся в сохраняемое состояние — иначе «при загрузке» зависело бы от прошлого
    /// входа (то же правило, что у представления «навыки», T-217).</summary>
    [Fact]
    public void All_Tag_Categories_Start_Collapsed()
    {
        var view = Ui("Components", "TaskTagsView.razor");

        Assert.Contains("_open = new(StringComparer.Ordinal)", view);
        Assert.DoesNotContain("ComponentState", view);
        Assert.Contains("data-tag-open=", view);
    }

    /// <summary>Эксплорер: «тэги» есть и под задачами проекта, и под его шаблонами.</summary>
    [Fact]
    public void The_Explorer_Has_Tags_Under_Tasks_And_Under_Templates()
    {
        var panel = Ui("Components", "ExplorerPanel.razor");

        Assert.Contains("AddTags(r3, \"tasks:\" + project.Id, key + \"/tasks/tags\", 3, KindTask)", panel);
        Assert.Contains("AddTags(r3, \"tpl:\" + project.Id, key + \"/templates/tags\", 3, KindTemplate)", panel);
        Assert.Contains("L[\"explorer.tags\"]", panel);
        // под тэгом — простой список, поэтому дерево по parent_id здесь не строится
        Assert.Contains("TaskTagGroups.Build(loaded)", panel);
    }

    // ---------- 7. схема и репликация ----------

    /// <summary>Тэги едут репликацией сами: таблица объявлена реплицируемой, а журнал
    /// изменений ставится триггерами по фактическому составу колонок.</summary>
    [Fact]
    public void The_Tag_Table_Is_Replicated() =>
        Assert.Contains("task_tags", ChangeLog.OrgTables);

    // ---------- 8. словари ----------

    /// <summary>Новые тексты есть в обоих словарях: составы ru и en сходятся посчётно
    /// (проверяет T192Tests), но своё сообщение стоит назвать явно.</summary>
    [Theory]
    [InlineData("task.tags")]
    [InlineData("task.tags.hint")]
    [InlineData("task.tags.new")]
    [InlineData("task.tags.add")]
    [InlineData("tasks.view.tags")]
    [InlineData("tasks.filter.tags")]
    [InlineData("tasks.tags.none")]
    [InlineData("explorer.tags")]
    public void The_New_Texts_Are_In_Both_Dictionaries(string key)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en" })
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(dir, lang + ".json")))!;
            Assert.True(dict.TryGetValue(key, out var text) && text.Length > 0,
                        $"{lang}.json: нет текста {key}");
        }
    }
}
