using System.Net.Http;
using AI2P.Server;
using AI2P.UI.Components;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-273. РЕДАКТОР LoRA: ДОРАБОТКА.
///
/// Задание заказчика — три вещи:
/// <list type="number">
/// <item>отдельная инструкция по редактору LoRA в поставляемой документации;</item>
/// <item>вызов этой инструкции ИЗ ФОРМЫ редактора;</item>
/// <item>выбор файла на диске начинается из папки проекта, а при повторном вызове —
/// с каталога прошлого выбора.</item>
/// </list>
///
/// Что проверяется здесь, а что живой проверкой. Здесь — то, что ломается молча:
/// страница справки существует на обоих языках и находится ПО ТОМУ ПУТИ, который зашит
/// в кнопку (разъехаться им ничего не мешает: путь — строка в компоненте, файл — на диске);
/// порядок каталогов выбора файла; память последнего каталога. Сам показ окна справки и то,
/// что диалог действительно открывается в нужном каталоге, доказывается браузером
/// (test/t273/ui273.py) и живой проверкой (test/t273/live273.py).
/// </summary>
public sealed class T273Tests
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

    private static string DocRoot() => Path.Combine(AppRoot(), "doc");

    /// <summary>
    /// Языковые каталоги документации. Каталог БЕЗ единого .md языком не считается
    /// (T-51-S0 завела <c>doc/images/</c> — картинки, общие для всех языков; требовать от
    /// него справки редактора LoRA бессмысленно). Такое же правило стоит в T18S1Tests.
    /// </summary>
    private static string[] Languages() =>
        Directory.GetDirectories(DocRoot())
            .Where(d => Directory.GetFiles(d, "*.md", SearchOption.AllDirectories).Length > 0)
            .Select(Path.GetFileName).OfType<string>()
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([AppRoot(), "src", .. parts]));

    private static UiState NewState() => new(new ApiClient(new HttpClient()), null!);

    // ---------- 1. страница справки ----------

    /// <summary>Путь страницы зашит в кнопку формы — файл обязан лежать именно там,
    /// и на каждом языке: иначе кнопка открывает «такой страницы нет».</summary>
    [Fact]
    public void The_Editor_Help_Page_Exists_In_Every_Language()
    {
        var rel = LoraEditorDialog.DocPath.Replace('/', Path.DirectorySeparatorChar);
        foreach (var lang in Languages())
        {
            var path = Path.Combine(DocRoot(), lang, rel);
            Assert.True(File.Exists(path), $"нет справки редактора LoRA: doc/{lang}/{LoraEditorDialog.DocPath}");
            Assert.True(new FileInfo(path).Length > 4000,
                $"справка doc/{lang}/{LoraEditorDialog.DocPath} подозрительно короткая");
        }
    }

    /// <summary>Страница достаётся ТЕМ ЖЕ путём, каким её просит кнопка (DocStore ищет файл
    /// без учёта регистра и по языкам отката — проверяем, что она действительно находится).</summary>
    [Fact]
    public void The_Help_Page_Is_Found_By_The_Path_The_Button_Asks_For()
    {
        var docs = new DocStore(DocRoot, () => "ru");
        var page = docs.Page(LoraEditorDialog.DocPath);
        Assert.True(page.Found, "DocStore не нашёл справку редактора LoRA: " + page.Hint);
        Assert.Equal("ru", page.Language);
        Assert.Contains("LoRA", page.Text, StringComparison.Ordinal);
    }

    /// <summary>Инструкция обязана отвечать на вопросы задания: что писать в описаниях,
    /// какой из текстов уходит в обучение, как запускать обучение, важен ли порядок кадров.
    /// Раздел, выпавший при правке, иначе замечает только читатель.</summary>
    [Theory]
    [InlineData("ru", "Описание", "Датасет", "последовательность", "обучени", "@obj:", "captions")]
    [InlineData("en", "Description", "Dataset", "order", "training", "@obj:", "captions")]
    public void The_Help_Page_Answers_The_Questions_Of_The_Task(string lang, params string[] parts)
    {
        var path = Path.Combine(DocRoot(), lang,
            LoraEditorDialog.DocPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return;     // языки проверяет отдельный тест — здесь не дублируем
        }
        var text = File.ReadAllText(path);
        foreach (var part in parts)
        {
            Assert.True(text.Contains(part, StringComparison.OrdinalIgnoreCase),
                $"в справке {lang} нет упоминания «{part}»");
        }
        // подпись кадра уезжает в обучение файлом .txt — это главный ответ на вопрос
        // «какие поля уходят в промпт обучения», и он обязан быть назван дословно
        Assert.Contains(".txt", text, StringComparison.Ordinal);
    }

    // ---------- 2. вызов справки из формы ----------

    /// <summary>Кнопка справки в форме редактора: своя пометка для проверок и открытие
    /// окна страницы документации, а не переход в закладку (форма осталась бы позади).</summary>
    [Fact]
    public void The_Editor_Form_Calls_The_Help()
    {
        var editor = Source("AI2P.UI", "Components", "LoraEditorDialog.razor");
        Assert.Contains("data-lora-doc=\"1\"", editor, StringComparison.Ordinal);
        Assert.Contains("DocPageDialog", editor, StringComparison.Ordinal);
        Assert.Contains("\"man/LoRAEditor.md\"", editor, StringComparison.Ordinal);
    }

    /// <summary>Окно справки ходит по внутренним ссылкам само (как закладка T-17-S1)
    /// и НЕ трогает состояние закладки: у неё своя страница чтения.</summary>
    [Fact]
    public void The_Help_Window_Walks_The_Internal_Links_Without_Touching_The_Tab()
    {
        var dialog = Source("AI2P.UI", "Components", "DocPageDialog.razor");
        Assert.Contains("DocLinks.Mark", dialog, StringComparison.Ordinal);
        Assert.Contains("ai2p.initDocLinks", dialog, StringComparison.Ordinal);
        Assert.Contains("OnDocLinkAsync", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("UiState", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("State.Doc", dialog, StringComparison.Ordinal);
    }

    // ---------- 3. откуда начинается выбор файла ----------

    [Fact]
    public void The_Typed_Directory_Wins_Over_Everything()
    {
        Assert.Equal(@"D:\typed",
            LocalFilePickerDialog.FirstPath(@"D:\typed", @"D:\last", @"D:\project"));
    }

    [Fact]
    public void The_Directory_Of_The_Previous_Pick_Wins_Over_The_Project_Folder()
    {
        Assert.Equal(@"D:\last", LocalFilePickerDialog.FirstPath(null, @"D:\last", @"D:\project"));
        Assert.Equal(@"D:\last", LocalFilePickerDialog.FirstPath("  ", @"D:\last", @"D:\project"));
    }

    [Fact]
    public void Without_A_Previous_Pick_The_Walk_Starts_From_The_Project_Folder()
    {
        Assert.Equal(@"D:\project", LocalFilePickerDialog.FirstPath(null, null, @"D:\project"));
        Assert.Equal(@"D:\project", LocalFilePickerDialog.FirstPath(null, "", @"D:\project"));
    }

    /// <summary>Ни одного каталога нет — прежнее поведение, список дисков.</summary>
    [Fact]
    public void With_Nothing_At_All_The_Walk_Starts_From_The_Drives()
    {
        Assert.Null(LocalFilePickerDialog.FirstPath(null, null, null));
        Assert.Null(LocalFilePickerDialog.FirstPath("", "  ", ""));
    }

    // ---------- 4. память последнего каталога ----------

    [Fact]
    public void There_Is_No_Remembered_Directory_At_The_Start()
    {
        Assert.Null(NewState().LastLocalDir);
    }

    [Fact]
    public void The_Directory_Of_The_Pick_Is_Remembered()
    {
        var state = NewState();
        state.SetLastLocalDir(@"D:\photo\heroes");
        Assert.Equal(@"D:\photo\heroes", state.LastLocalDir);
        // повторный выбор в другом каталоге переписывает память, а не копит её
        state.SetLastLocalDir(@"D:\photo\props");
        Assert.Equal(@"D:\photo\props", state.LastLocalDir);
    }

    [Fact]
    public void An_Empty_Directory_Clears_The_Memory()
    {
        var state = NewState();
        state.SetLastLocalDir(@"D:\photo");
        state.SetLastLocalDir(null);
        Assert.Null(state.LastLocalDir);
    }

    /// <summary>Память живёт в состояниях представлений — то есть переживает не только
    /// закрытие окна, но и следующий вход (гл. 11). Проверяется тем, что состояние
    /// сохраняется под своим ключом.</summary>
    [Fact]
    public void The_Memory_Lives_In_The_View_States()
    {
        var source = Source("AI2P.UI", "Services", "UiState.cs");
        Assert.Contains("localFilePicker", source, StringComparison.Ordinal);
        Assert.Contains("PersistComponentStates()", source, StringComparison.Ordinal);
    }

    // ---------- 5. папка проекта как запасной каталог ----------

    /// <summary>Форма кадра обязана отдать выбору файла папку проекта: без этого обход
    /// начинался бы со списка дисков, как раньше.</summary>
    [Fact]
    public void The_Frame_Form_Hands_The_Project_Folder_To_The_Picker()
    {
        var frame = Source("AI2P.UI", "Components", "LoraFrameDialog.razor");
        Assert.Contains("d.FallbackPath", frame, StringComparison.Ordinal);
        Assert.Contains("ProjectFolderAsync", frame, StringComparison.Ordinal);
        Assert.Contains("GetProjectAsync", frame, StringComparison.Ordinal);
    }

    /// <summary>Каталог, которого больше нет (папку переименовали, диск отключили), не должен
    /// оставлять человека в тупике: список каталогов поднимается к ближайшему существующему
    /// предку. Правило живёт в ApiEndpoints.ListDirs, живой проверкой оно закрыто вызовом
    /// /api/fs/dirs — здесь сторожим, что ветку не выбросили.</summary>
    [Fact]
    public void A_Vanished_Directory_Falls_Back_To_Its_Nearest_Parent()
    {
        var endpoints = Source("AI2P.Server", "Api", "ApiEndpoints.cs");
        Assert.Contains("if (!Directory.Exists(full))", endpoints, StringComparison.Ordinal);
    }
}
