using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-194. РАБОТА НА ТЕЛЕФОНЕ (Firefox на Android), три жалобы заказчика.
///
/// 1. «Ввод текста в задачи постоянно удаляет последний введённый символ». Поле текста
///    MD-редактора рисовалось как &lt;textarea value="@Value" @oninput=…&gt;: каждое нажатие
///    уезжало на сервер, а Blazor записывал значение ОБРАТНО в элемент. На своей машине
///    ответ успевал прийти раньше следующего символа, с телефона — нет, и запись
///    устаревшего значения съедала набранное. Лечение то же, что у редактируемого
///    предпросмотра: содержимое поля ведёт JS (ai2p.mdSync), Blazor его не трогает, а
///    записывать значение можно ТОЛЬКО когда оно сменилось извне.
/// 2. «Верхний тулбар: включить скролирование если не влезает (значки ‹ ›)» — на экране
///    телефона правый конец полосы меню уходил за край и был недосягаем (.ai2p-shell
///    с overflow: hidden).
/// 3. «Проект — задачи: тулбар должен скролироваться отдельно» — тулбар списка задач шире
///    фрейма утаскивал за собой вбок ВСЁ содержимое представления, доску вместе с собой.
///
/// Само поведение проверяется браузером (test/t194/ui194.py — настоящий Chrome, экран
/// телефона, канал с задержкой); здесь — контракт разметки, стилей, словарей и interop,
/// на котором оно держится.
/// </summary>
public sealed class T194Tests
{
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

    private static string AppRoot() => Path.Combine(RepoRoot(), "AI2P_app");

    private static string UiRoot() => Path.Combine(AppRoot(), "src", "AI2P.UI");

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UiRoot() }.Concat(parts).ToArray()));

    private static string Js() => Read("wwwroot", "ai2p.js");
    private static string Home() => Read("Pages", "Home.razor");
    private static string Editor() => Read("Components", "MarkdownEditor.razor");
    private static string Tasks() => Read("Components", "TasksView.razor");

    private static string Rule(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"в стилях нет правила «{selector}»");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        Assert.True(close > open, $"правило «{selector}» не закрыто");
        return css[(open + 1)..close];
    }

    private static string Body(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"в файле нет «{from}»");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"после «{from}» нет «{to}»");
        return text[start..end];
    }

    // ---------- 1. ввод текста: значение назад в поле не пишется ----------

    [Fact]
    public void Text_Field_Value_Is_Not_Rendered_By_Blazor()
    {
        var editor = Editor();
        var textarea = Body(editor, "<textarea", "</textarea>");

        // ИМЕННО ЭТО и съедало символы: значение в разметке Blazor записывает в элемент
        // после каждого нажатия, а с телефона ответ приходит уже устаревшим
        Assert.DoesNotContain("value=\"@Value\"", textarea);
        Assert.Contains("@oninput=\"OnInput\"", textarea);
        // содержимое ставит JS — тем же приёмом, что у редактируемого предпросмотра
        Assert.Contains("ai2p.mdSync", editor);
        Assert.DoesNotContain("ai2p.mdInit", editor);
    }

    [Fact]
    public void Own_Edits_Never_Rewrite_The_Field()
    {
        var editor = Editor();
        var render = Body(editor, "protected override async Task OnAfterRenderAsync",
                          "private Task OnInput");

        // переписывать содержимое можно ТОЛЬКО когда значение сменилось извне
        Assert.Contains("var force = _taValue != Value;", render);
        Assert.Contains("\"ai2p.mdSync\", _id, _selfRef, Value, force", render);
        Assert.Contains("_taValue = Value;", render);

        // свои правки (набор, кнопки панели, Tab, вставка) идут через Set — и сдвигают
        // _taValue вместе со значением, поэтому записи не вызывают
        var set = Body(editor, "private Task Set(string value)", "/// <summary>Правка в предпросмотре");
        Assert.Contains("_taValue = value;", set);
    }

    [Fact]
    public void Js_Fills_A_New_Field_And_Keeps_A_Live_One()
    {
        var sync = Body(Js(), "function mdSync(", "function mdInsert(");

        // новый элемент (Blazor рисует поле пустым) — наполнить
        Assert.Contains("ta.dataset.ai2pMd = '1';", sync);
        Assert.Contains("ta.value = text || '';", sync);
        // живой — только по force и только если значение и правда другое
        Assert.Contains("if (force && ta.value !== (text || ''))", sync);
        // обработчики (Tab, вставка картинок) ставятся один раз, как и раньше
        Assert.Contains("mdRefs.set(ta, dotnetRef);", sync);
        Assert.Contains("addEventListener('paste'", sync);
        Assert.Contains("mdSync: mdSync,", Js());
    }

    // ---------- 2. общий движок листаемой полосы ----------

    [Fact]
    public void Strip_Engine_Shows_Arrows_Only_On_Overflow()
    {
        var js = Js();
        var update = Body(js, "function stripUpdate(", "function stripWire(");

        Assert.Contains("strip.scrollWidth - strip.clientWidth > 1", update);
        Assert.Contains("bar.classList.toggle(scrollsClass, overflow)", update);
        // доехали до края — стрелка гаснет и не нажимается
        Assert.Contains("prev.classList.toggle('ai2p-arrow-off', !overflow || left <= 1)", update);
        Assert.Contains("next.classList.toggle('ai2p-arrow-off'", update);

        var wire = Body(js, "function stripWire(", "function stripScroll(");
        Assert.Contains("addEventListener('scroll'", wire);   // листание пальцем — тоже событие
        Assert.Contains("addEventListener('wheel'", wire);
        Assert.Contains("ResizeObserver", wire);              // поворот экрана, ресайз фрейма
        Assert.Contains("initStrips: initStrips,", js);
        Assert.Contains("scrollStrip: scrollStrip,", js);
    }

    [Fact]
    public void Frame_Tabs_Use_The_Same_Engine_And_Still_Scroll()
    {
        var js = Js();
        // движок один: полоса закладок (T-163) не должна отстать от тулбаров
        var tabs = Body(js, "function tabScrollUpdate(", "// --- ТУЛБАРЫ");
        Assert.Contains("stripUpdate(bar, bar.querySelector('.ai2p-tabbar-strip'), 'ai2p-tabbar-scrolls'", tabs);
        var init = Body(js, "function initTabScroll(", "function scrollTabs(");
        Assert.Contains("stripWire(strip", init);
        Assert.Contains("tabScrollToActive(strip)", init);
        var scroll = Body(js, "function scrollTabs(", "// --- перетаскивание ПАЛЬЦЕМ");
        Assert.Contains("stripScroll(strip, dir)", scroll);
    }

    // ---------- 3. верхний тулбар ----------

    [Fact]
    public void Menu_Bar_Is_A_Scrollable_Strip()
    {
        var home = Home();

        Assert.Contains("class=\"ai2p-menubar ai2p-strip\"", home);
        Assert.Contains("data-strip-inner=\"menubar\"", home);
        Assert.Contains("data-strip-scroll=\"prev\"", home);
        Assert.Contains("data-strip-scroll=\"next\"", home);
        // подсказка обычным title: MudTooltip обернул бы кнопку своим контейнером и она
        // перестала бы быть элементом полосы (T-163)
        Assert.Contains("title=\"@L[\"strip.scrollPrev\"]\"", home);
        Assert.Contains("title=\"@L[\"strip.scrollNext\"]\"", home);
        Assert.Contains("ScrollStripAsync(\"menubar\", -1)", home);
        Assert.Contains("ScrollStripAsync(\"menubar\", 1)", home);
        // переполнение мерит браузер — и мерить надо после каждого рендера
        Assert.Contains("\"ai2p.initStrips\"", home);
    }

    [Fact]
    public void Strip_Content_Keeps_Its_Width_And_Scrolls()
    {
        var home = Home();
        var inner = Rule(home, ".ai2p-strip-inner");

        Assert.Contains("flex-wrap: nowrap", inner);   // в одну строку, без переноса вниз
        Assert.Contains("overflow-x: auto", inner);    // пальцем на телефоне — нативно
        Assert.Contains("overflow-y: hidden", inner);
        Assert.Contains("scrollbar-width: none", inner);

        // без этого кнопки и надписи сжимались бы, переполнения не возникало вовсе,
        // а «Организация: …» и «Проект: …» пропадали в многоточии
        Assert.Contains("flex: 0 0 auto", Rule(home, ".ai2p-strip-inner > *"));
        Assert.Contains("flex: 1 1 auto", Rule(home, ".ai2p-strip-inner > .flex-grow-1"));

        Assert.Contains("display: none", Rule(home, ".ai2p-strip-arrow"));
        Assert.Contains("display: inline-flex",
            Rule(home, ".ai2p-strip.ai2p-strip-scrolls > .ai2p-strip-arrow"));
        var off = Rule(home, ".ai2p-strip-arrow.ai2p-arrow-off");
        Assert.Contains("pointer-events: none", off);
    }

    // ---------- 4. тулбар списка задач ----------

    [Fact]
    public void Tasks_Toolbar_Scrolls_On_Its_Own()
    {
        var tasks = Tasks();

        Assert.Contains("<div class=\"ai2p-strip mb-2\">", tasks);
        Assert.Contains("data-strip-inner=\"@StripKey\"", tasks);
        Assert.Contains("Class=\"ai2p-strip-row\"", tasks);
        Assert.Contains("data-strip-scroll=\"prev\"", tasks);
        Assert.Contains("data-strip-scroll=\"next\"", tasks);
        Assert.Contains("ScrollToolbarAsync(-1)", tasks);
        Assert.Contains("ScrollToolbarAsync(1)", tasks);
        Assert.Contains("\"ai2p.initStrips\"", tasks);
        Assert.Contains("@inject IJSRuntime Js", tasks);
    }

    [Fact]
    public void Every_Task_List_Has_Its_Own_Strip_Key()
    {
        // списков задач на экране бывает несколько: фреймы рабочей области и вкладка
        // «Задачи» карточки проекта — общий ключ листал бы чужой тулбар
        Assert.Contains("private string StripKey => \"tasks:\" + (FixedProjectId ?? \"root\");",
            Tasks());
    }

    [Fact]
    public void Toolbar_Row_Fills_The_Strip_On_A_Wide_Screen()
    {
        var row = Rule(Home(), ".ai2p-strip-row");
        // иначе распорка (MudSpacer) внутри ряда перестаёт разводить группы кнопок
        // по краям, и на большом экране правая группа уезжает влево
        Assert.Contains("min-width: 100%", row);
        Assert.Contains("flex-shrink: 0", Rule(Home(), ".ai2p-strip-row > *"));
    }

    // ---------- 5. словари ----------

    [Fact]
    public void Arrow_Hints_Are_In_Both_Dictionaries()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var path = Path.Combine(AppRoot(), "i18n", lang + ".json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in new[] { "strip.scrollPrev", "strip.scrollNext" })
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var value),
                    $"{lang}.json: нет ключа {key}");
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()),
                    $"{lang}.json: пустой текст у {key}");
            }
        }
    }
}
