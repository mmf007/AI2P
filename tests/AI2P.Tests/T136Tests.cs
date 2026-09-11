using AI2P.Core.Api;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.UI.Components;
using Xunit;
using LogLevel = AI2P.Core.Events.LogLevel;

namespace AI2P.Tests;

/// <summary>
/// T-136: ПРАВКИ ИНТЕРФЕЙСА 2 — карточка задачи, окно «все файлы», вывод .md,
/// уровень вывода в лог, таблица и иерархия задач.
///
/// Здесь проверяется то, что проверяется без браузера: правила стилей и разметка
/// компонентов (файлами, как в <see cref="MdCodeStyleTests"/>), деление заголовка колонки
/// на две строки и фильтр журнала работ по уровню логирования. Живая проверка интерфейса
/// в настоящем браузере — <c>test/t136</c>.
/// </summary>
public sealed class T136Tests
{
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

    private static string UiRoot() => Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI");

    private static string Ui(params string[] parts) =>
        File.ReadAllText(Path.Combine([UiRoot(), .. parts]));

    // --- 1. карточка задачи: проект слева, смена статуса у подзадачи ---

    [Fact]
    public void Task_Card_Shows_Project_Name_Styled_Apart()
    {
        var card = Ui("Components", "TaskCardView.razor");
        Assert.Contains("ai2p-task-project", card, StringComparison.Ordinal);
        // название проекта — ПЕРЕД кодом задачи в той же строке шапки
        var project = card.IndexOf("ai2p-task-project", StringComparison.Ordinal);
        var code = card.IndexOf("<b>@task.DisplayId</b>", StringComparison.Ordinal);
        Assert.True(project > 0 && code > project, "название проекта должно стоять слева от кода задачи");

        // стиль объявлен ровно один раз и только цветами палитры (обе темы)
        var files = Directory.GetFiles(UiRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(".ai2p-task-project", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["Home.razor"], files);
        var home = Ui("Pages", "Home.razor");
        var rule = home[home.IndexOf(".ai2p-task-project", StringComparison.Ordinal)..];
        rule = rule[..rule.IndexOf('}')];
        Assert.Contains("var(--mud-palette-", rule, StringComparison.Ordinal);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(rule, "#[0-9a-fA-F]{3,8}"),
            "цвет задан шестнадцатеричным литералом — нужен var(--mud-palette-*)");
    }

    [Fact]
    public void Subtask_Row_Has_Status_Button_With_The_Same_Icon()
    {
        var card = Ui("Components", "TaskCardView.razor");
        Assert.Contains("ChangeSubtaskStatusAsync", card, StringComparison.Ordinal);
        // иконка та же, что у кнопки смены статуса самой задачи
        Assert.Contains("OnClick=\"ChangeStatusAsync\"", card, StringComparison.Ordinal);
        var swaps = System.Text.RegularExpressions.Regex.Matches(card, "Icons.Material.Filled.SwapHoriz");
        Assert.Equal(2, swaps.Count); // задача и строка подзадачи
        // кнопка стоит рядом с «изменить» и «удалить» подзадачи
        var status = card.IndexOf("ChangeSubtaskStatusAsync(subtask)", StringComparison.Ordinal);
        var edit = card.IndexOf("EditSubtaskAsync(subtask)", StringComparison.Ordinal);
        var delete = card.IndexOf("DeleteSubtaskAsync(subtask)", StringComparison.Ordinal);
        Assert.True(status > 0 && status < edit && edit < delete);
    }

    // --- 2. окно «все файлы»: ни надписи об удалении всех, ни кнопок ОТМЕНА/УДАЛИТЬ ---

    [Fact]
    public void Files_Dialog_Has_No_Inline_Message_Box()
    {
        var dialog = Ui("Components", "TaskFilesDialog.razor");
        // MudMessageBox, объявленный в разметке формы-диалога, рисуется MudBlazor ВНУТРИ неё:
        // сверху висели чужой вопрос про удаление и кнопки «ОТМЕНА»/«УДАЛИТЬ»
        Assert.DoesNotContain("<MudMessageBox", dialog, StringComparison.Ordinal);
        // переспрос остался — отдельным окном
        Assert.Contains("ShowMessageBoxAsync", dialog, StringComparison.Ordinal);
        // удаление — только у каждого файла отдельно
        Assert.Contains("DeleteAsync(file)", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_Dialog_Is_Wide_And_Does_Not_Scroll_Sideways()
    {
        var card = Ui("Components", "TaskCardView.razor");
        var open = card.IndexOf("ShowAsync<TaskFilesDialog>", StringComparison.Ordinal);
        Assert.True(open > 0);
        Assert.Contains("MaxWidth.ExtraLarge", card[open..(open + 300)], StringComparison.Ordinal);
        var dialog = Ui("Components", "TaskFilesDialog.razor");
        Assert.Contains("table-layout: fixed", dialog, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", dialog, StringComparison.Ordinal);
    }

    // --- 3. вывод .md: перечисления не вылезают за поле; блок кода и отступы в тулбаре ---

    [Fact]
    public void Lists_Are_Indented_Inside_The_Field()
    {
        var files = Directory.GetFiles(UiRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(".ai2p-md-preview ul", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["MainLayout.razor"], files); // единое место стилей .md (T-131)
        var css = Ui("MainLayout.razor");
        var rule = css[css.IndexOf(".ai2p-md-preview ul", StringComparison.Ordinal)..];
        rule = rule[..rule.IndexOf('}')];
        Assert.Contains("padding-left", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Toolbar_Has_Code_Block_And_Indent_Buttons()
    {
        var editor = Ui("Components", "MarkdownEditor.razor");
        Assert.Contains("md.codeBlock", editor, StringComparison.Ordinal);
        Assert.Contains("md.indent", editor, StringComparison.Ordinal);
        Assert.Contains("md.outdent", editor, StringComparison.Ordinal);
        Assert.Contains("ai2p.mdCodeBlock", editor, StringComparison.Ordinal);
        Assert.Contains("ai2p.mdIndent", editor, StringComparison.Ordinal);
        // и в предпросмотре тоже
        Assert.Contains("ai2p.wysCodeBlock", editor, StringComparison.Ordinal);
        Assert.Contains("ai2p.wysIndent", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_Key_Indents_Instead_Of_Leaving_The_Field()
    {
        var js = Ui("wwwroot", "ai2p.js");
        // клавиша перехвачена и в текстовом поле, и в предпросмотре
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(js, @"e\.key !== 'Tab'").Count);
        Assert.Contains("mdIndent(textareaId, e.shiftKey)", js, StringComparison.Ordinal);
        Assert.Contains("execCommand(e.shiftKey ? 'outdent' : 'indent')", js, StringComparison.Ordinal);
        // новое значение поля уходит в .NET — иначе правка потерялась бы
        Assert.Contains("OnTextChangedAsync", js, StringComparison.Ordinal);
        Assert.Contains("OnTextChangedAsync", Ui("Components", "MarkdownEditor.razor"), StringComparison.Ordinal);
        // функции экспортированы наружу (иначе вызов из .NET не найдёт их)
        Assert.Contains("mdCodeBlock: mdCodeBlock", js, StringComparison.Ordinal);
        Assert.Contains("mdIndent: mdIndent", js, StringComparison.Ordinal);
        Assert.Contains("wysCodeBlock: wysCodeBlock", js, StringComparison.Ordinal);
        Assert.Contains("wysIndent: wysIndent", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Pasted_Image_Fits_Into_The_Ui_Channel()
    {
        // Ctrl+V: картинка едет к серверу одним сообщением канала Blazor; предел по умолчанию
        // (32 КБ) меньше любого снимка экрана — вызов не доходил, а связь страницы обрывалась
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "Program.cs"));
        Assert.Contains("MaximumReceiveMessageSize", program, StringComparison.Ordinal);
        var js = Ui("wwwroot", "ai2p.js");
        Assert.Contains("PasteMaxBytes", js, StringComparison.Ordinal);
        Assert.Contains("OnPasteTooBigAsync", js, StringComparison.Ordinal);
        Assert.Contains("OnPasteTooBigAsync", Ui("Components", "MarkdownEditor.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Indented_Line_Is_A_Code_Block_In_Markdown()
    {
        // «альтернативное выделение кода» табуляцией: строка с отступом — блок кода
        var html = AI2P.UI.Services.Md.ToHtml("текст\n\n\tvar a = 1;\n");
        Assert.Contains("<pre><code>", html, StringComparison.Ordinal);
        // блок из трёх апострофов (кнопка тулбара) переживает предпросмотр без потерь
        const string fenced = "```\nvar a = 1;\n```";
        Assert.Equal(fenced, AI2P.UI.Services.MdHtml.ToMarkdown(AI2P.UI.Services.Md.ToHtml(fenced)));
    }

    // --- 4. уровень вывода в лог ---

    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("Info", LogLevel.Info)]
    [InlineData("Information", LogLevel.Info)] // так уровень назывался в конфигурации до T-136
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("", LogLevel.Warning)]         // умолчание системы
    [InlineData("чепуха", LogLevel.Warning)]
    public void Log_Level_Is_Parsed(string value, LogLevel expected)
    {
        Assert.Equal(expected, EventLog.Parse(value));
        Assert.Equal(EventLog.Name(expected), EventLog.Name(EventLog.Parse(value)));
    }

    [Fact]
    public void Log_Level_Decides_What_Goes_To_The_Journal()
    {
        // Debug — всё
        Assert.True(EventLog.Allows(EventTypes.AgentToolCalls, LogLevel.Debug));
        Assert.True(EventLog.Allows(EventTypes.AgentRequest, LogLevel.Debug));
        // Info — без самых объёмных (вызовы инструментов агента, диагностика участников)
        Assert.False(EventLog.Allows(EventTypes.AgentToolCalls, LogLevel.Info));
        Assert.False(EventLog.Allows(EventTypes.TeamMemberState, LogLevel.Info));
        Assert.True(EventLog.Allows(EventTypes.AgentRequest, LogLevel.Info));
        // Warning — самое необходимое: доменная история остаётся, протокол обмена нет
        Assert.False(EventLog.Allows(EventTypes.AgentToolCalls, LogLevel.Warning));
        Assert.False(EventLog.Allows(EventTypes.AgentRequest, LogLevel.Warning));
        Assert.False(EventLog.Allows(EventTypes.AgentResponse, LogLevel.Warning));
        foreach (var kept in new[]
                 {
                     EventTypes.TaskCreated, EventTypes.TaskStatusChanged, EventTypes.JobCompleted,
                     EventTypes.ChatMessage, EventTypes.RuleTriggered, EventTypes.AccountLoginFailed,
                     EventTypes.ServerJoinAccepted, EventTypes.ImportRun,
                 })
        {
            Assert.True(EventLog.Allows(kept, LogLevel.Warning), kept);
        }
        // умолчание процесса — «пишем всё»: приложение выставляет уровень из настроек
        Assert.Equal(LogLevel.Debug, EventLog.Level);
    }

    [Fact]
    public void Journal_Skips_Bulk_Events_On_The_Default_Level()
    {
        using var fixture = new StorageFixture();
        // свой уровень у экземпляра: общий на процесс трогать нельзя — тесты идут параллельно
        var journal = new EventStore(fixture.Db)
        {
            Allows = type => EventLog.Allows(type, LogLevel.Warning),
        };
        journal.Append(new EventRecord { EventType = EventTypes.AgentToolCalls, PayloadJson = "{}" });
        journal.Append(new EventRecord { EventType = EventTypes.AgentRequest, PayloadJson = "{}" });
        journal.Append(new EventRecord { EventType = EventTypes.TaskCreated, PayloadJson = "{}" });

        var written = journal.Query(limit: 100).Select(e => e.EventType).ToList();
        Assert.Contains(EventTypes.TaskCreated, written);
        Assert.DoesNotContain(EventTypes.AgentToolCalls, written);
        Assert.DoesNotContain(EventTypes.AgentRequest, written);
    }

    [Fact]
    public void Log_Level_Travels_Through_Settings()
    {
        // настройка живёт в общем DTO настроек приложения (Настройки → Основное)
        Assert.Equal("Warning", new SettingsDto().LogLevel);
        var settings = Ui("Components", "SettingsView.razor");
        Assert.Contains("settings.logLevel", settings, StringComparison.Ordinal);
        Assert.Contains("_settings.LogLevel", settings, StringComparison.Ordinal);
        var api = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server",
            "Api", "ApiEndpoints.cs"));
        Assert.Contains("LogSwitch.Apply(dto.LogLevel)", api, StringComparison.Ordinal);
        // умолчание дистрибутива — Warning
        var config = File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "config.json"));
        Assert.Contains("\"level\": \"Warning\"", config, StringComparison.Ordinal);
    }

    // --- 5. таблица задач: заголовки в две строки, ширина «Заголовку» ---

    [Theory]
    [InlineData("Приоритет (число)", "Приоритет", "(число)")]
    [InlineData("Ответственный (человек)", "Ответственный", "(человек)")]
    [InlineData("Срок (план)", "Срок", "(план)")]
    [InlineData("Длительность (план) ч", "Длительность", "(план) ч")]
    [InlineData("Заголовок", "Заголовок", null)]
    [InlineData("ID", "ID", null)]
    public void Column_Title_Splits_Into_Two_Lines(string title, string first, string? second)
    {
        Assert.Equal((first, second), TaskTableView.TwoLines(title));
    }

    [Fact]
    public void Narrow_Columns_Give_Their_Width_To_The_Title()
    {
        var table = Ui("Components", "TaskTableView.razor");
        Assert.Contains("HeadStyle(column.Key)", table, StringComparison.Ordinal);
        Assert.Contains("width: 100%;", table, StringComparison.Ordinal); // «Заголовок»
        Assert.Contains("width: 1%; white-space: nowrap;", table, StringComparison.Ordinal);
    }

    // --- 6. иерархия: свернуть/развернуть все ---

    [Fact]
    public void Tree_Can_Be_Collapsed_And_Expanded_Entirely()
    {
        var tree = Ui("Components", "TaskTreeView.razor");
        Assert.Contains("public void CollapseAll()", tree, StringComparison.Ordinal);
        Assert.Contains("public void ExpandAll()", tree, StringComparison.Ordinal);
        var view = Ui("Components", "TasksView.razor");
        // кнопки — в тулбаре, правее иконки сортировки, и только у представления «иерархия»
        var sort = view.IndexOf("Icons.Material.Filled.SwapVert", StringComparison.Ordinal);
        var collapse = view.IndexOf("tasks.tree.collapseAll", StringComparison.Ordinal);
        var expand = view.IndexOf("tasks.tree.expandAll", StringComparison.Ordinal);
        Assert.True(sort > 0 && collapse > sort && expand > collapse);
        Assert.Contains("_tree?.CollapseAll()", view, StringComparison.Ordinal);
        Assert.Contains("_tree?.ExpandAll()", view, StringComparison.Ordinal);
    }

    // --- 7. подписи: новые ключи есть на обоих языках ---

    [Theory]
    [InlineData("md.codeBlock")]
    [InlineData("md.indent")]
    [InlineData("md.outdent")]
    [InlineData("tasks.tree.collapseAll")]
    [InlineData("tasks.tree.expandAll")]
    [InlineData("settings.logLevel")]
    [InlineData("settings.logLevel.debug")]
    [InlineData("settings.logLevel.info")]
    [InlineData("settings.logLevel.warning")]
    public void New_Labels_Exist_In_Both_Languages(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(doc.RootElement.TryGetProperty(key, out var value), $"{lang}: нет ключа {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{lang}: пустая подпись {key}");
        }
    }

    /// <summary>Подписи колонок, которые заказчик просил переносить на две строки, обязаны
    /// содержать уточнение в скобках — иначе делить нечего.</summary>
    [Theory]
    [InlineData("task.priorityNum")]
    [InlineData("task.responsible")]
    [InlineData("task.dueDate")]
    [InlineData("task.plannedHours")]
    public void Two_Line_Column_Labels_Have_A_Bracketed_Second_Line(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var title = doc.RootElement.GetProperty(key).GetString() ?? "";
            Assert.NotNull(TaskTableView.TwoLines(title).Second);
        }
    }
}
