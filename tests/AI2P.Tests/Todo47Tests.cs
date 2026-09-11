using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo47 (ТЗ v1.54, гл. 14): ДОКУМЕНТИРОВАНИЕ, ЭТАП 1. Каталог <c>doc/</c> получил
/// подкаталоги по языкам, в каждом — <c>models/&lt;название модели&gt;.md</c> с ответом
/// на два вопроса: как получить ключ у облачной модели и какие требования к железу
/// у локальной. Каталог целиком копируется в релизную выкладку, а UI показывает документ
/// кнопкой «i» в форме модели.
/// </summary>
public sealed class Todo47Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo47-" + Guid.NewGuid().ToString("N"));

    public Todo47Tests() => Directory.CreateDirectory(_dir);

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

    private DocStore Store(string lang = "ru") => new(() => _dir, () => lang);

    private void Write(string lang, string fileName, string text)
    {
        var dir = Path.Combine(_dir, lang, "models");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), text);
    }

    // --- имя файла документа ---

    [Fact]
    public void Document_Is_Named_After_The_Model()
    {
        // имя файла — НАЗВАНИЕ модели: оно уникально, стабильно у моделей дистрибутива
        // и читается тем, кто документ пишет. Номер (M-3) для этого не годится — он
        // выдаётся счётчиком и на разных установках у одной модели разный
        Assert.Equal("Claude-Fable-5.md", DocStore.FileNameOf("Claude-Fable-5"));
        Assert.Equal("Qwen3.6-35B-A3B-Local.md", DocStore.FileNameOf("Qwen3.6-35B-A3B-Local"));
        Assert.Equal("Claude-Opus-5.0_cli.md", DocStore.FileNameOf("Claude-Opus-5.0_cli"));
    }

    [Fact]
    public void Model_Name_Cannot_Escape_The_Documentation_Directory()
    {
        // название приходит из справочника и может содержать что угодно; путь собирает
        // сервер, и уводить его наружу нельзя (ТЗ гл. 12)
        Assert.Equal(".._.._etc_passwd.md", DocStore.FileNameOf("../../etc/passwd"));
        Assert.DoesNotContain("/", DocStore.FileNameOf("a/b"));
        Assert.DoesNotContain("\\", DocStore.FileNameOf("a\\b"));
        Assert.Equal("model.md", DocStore.FileNameOf("   "));
    }

    // --- поиск документа и откат по языкам ---

    [Fact]
    public void Document_Is_Found_In_The_Interface_Language()
    {
        Write("ru", "Claude-Fable-5.md", "# Claude-Fable-5\nКак получить ключ…");

        var doc = Store("ru").ModelDoc("Claude-Fable-5");

        Assert.True(doc.Found);
        Assert.Equal("ru", doc.Language);
        Assert.Contains("Как получить ключ", doc.Text);
        Assert.EndsWith("Claude-Fable-5.md", doc.Path);
    }

    [Fact]
    public void Missing_Translation_Falls_Back_To_Russian()
    {
        // перевода ещё нет — кнопка «i» не должна приводить на пустой экран
        Write("ru", "Claude-Fable-5.md", "русский текст");

        var doc = Store("en").ModelDoc("Claude-Fable-5");

        Assert.True(doc.Found);
        Assert.Equal("ru", doc.Language);   // UI покажет, что язык другой
    }

    [Fact]
    public void Interface_Language_Wins_Over_The_Fallback()
    {
        Write("ru", "Claude-Fable-5.md", "русский текст");
        Write("en", "Claude-Fable-5.md", "english text");

        Assert.Equal("en", Store("en").ModelDoc("Claude-Fable-5").Language);
        Assert.Equal("ru", Store("ru").ModelDoc("Claude-Fable-5").Language);
    }

    [Fact]
    public void Missing_Document_Explains_Where_To_Put_It()
    {
        var doc = Store().ModelDoc("Моя-модель");

        Assert.False(doc.Found);
        Assert.Equal("", doc.Text);
        // пустой экран без объяснения бесполезен: показываем путь, куда положить файл
        Assert.Contains("Моя-модель.md", doc.Hint);
        Assert.Contains("Моя-модель.md", doc.Path);
    }

    [Fact]
    public void Missing_Documentation_Directory_Is_Reported()
    {
        var store = new DocStore(() => Path.Combine(_dir, "нет-такого"), () => "ru");

        var doc = store.ModelDoc("Claude-Fable-5");

        Assert.False(doc.Found);
        Assert.Contains("docDir", doc.Hint);
    }

    // --- каталог документации находится сам ---

    [Fact]
    public void Directory_Is_Found_By_Language_Subdirectories()
    {
        // без настройки каталог ищется сам: рядом с приложением (релизная выкладка),
        // затем вверх по дереву (запуск из исходников). Признак «наш» — языковые подкаталоги
        Directory.CreateDirectory(Path.Combine(_dir, "ru", "models"));
        var configured = new DocStore(() => _dir, () => "ru");

        Assert.Equal(Path.GetFullPath(_dir), configured.Root());

        // указан несуществующий каталог — пусто, а не молчаливый поиск где-то ещё
        Assert.Equal("", new DocStore(() => Path.Combine(_dir, "нет"), () => "ru").Root());
    }

    [Fact]
    public void Auto_Search_Finds_The_Repository_Documentation()
    {
        // при запуске из исходников doc/ лежит в корне репозитория, а приложение —
        // в AI2P_app/src/AI2P.Server/bin/…; поиск вверх обязан до него дойти
        var auto = new DocStore(() => "", () => "ru");

        var root = auto.Root();

        Assert.NotEqual("", root);
        Assert.True(Directory.Exists(Path.Combine(root, "ru", "models")),
            $"в найденном каталоге документации нет ru/models: {root}");
    }

    // --- документы существующих моделей написаны (задание: «пока на ru») ---

    [Theory]
    [InlineData("Claude-Fable-5")]
    [InlineData("Claude-Opus-5.0")]
    [InlineData("Claude-Fable-5_cli")]
    [InlineData("Claude-Opus-5.0_cli")]
    [InlineData("DeepSeek-V4-Pro")]
    [InlineData("DeepSeek-V4-Flash")]
    [InlineData("Kandinsky-5.0-T2V-Lite-sft-5s")]
    [InlineData("Qwen3.6-35B-A3B-Local")]
    public void Every_Seeded_Model_Has_A_Russian_Document(string modelName)
    {
        var doc = new DocStore(() => "", () => "ru").ModelDoc(modelName);

        Assert.True(doc.Found, $"нет документа для модели {modelName}: {doc.Hint}");
        Assert.Contains(modelName, doc.Text);
    }

    [Theory]
    [InlineData("Claude-Fable-5", "ключ")]
    [InlineData("Claude-Opus-5.0", "ключ")]
    [InlineData("DeepSeek-V4-Pro", "ключ")]
    [InlineData("DeepSeek-V4-Flash", "ключ")]
    public void Cloud_Model_Document_Tells_How_To_Get_The_Key(string modelName, string word)
    {
        // главное, ради чего документ существует у облачной модели (задание todo47)
        var text = new DocStore(() => "", () => "ru").ModelDoc(modelName).Text;

        Assert.Contains(word, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Как получить ключ", text);
        Assert.Contains("https://", text);   // ссылка на консоль провайдера
    }

    [Theory]
    [InlineData("Kandinsky-5.0-T2V-Lite-sft-5s")]
    [InlineData("Qwen3.6-35B-A3B-Local")]
    public void Local_Model_Document_Tells_The_Hardware_Requirements(string modelName)
    {
        // главное, ради чего документ существует у локальной модели (задание todo47)
        var text = new DocStore(() => "", () => "ru").ModelDoc(modelName).Text;

        Assert.Contains("Требования к железу", text);
        Assert.Contains("ГБ", text);   // память или место на диске названы числом
    }

    [Fact]
    public void Cli_Models_Say_That_No_Key_Is_Needed()
    {
        foreach (var name in new[] { "Claude-Fable-5_cli", "Claude-Opus-5.0_cli" })
        {
            var text = new DocStore(() => "", () => "ru").ModelDoc(name).Text;
            Assert.Contains("ключ API не нужен", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void English_Directory_Exists_For_Future_Translations()
    {
        // задание просит завести оба языковых каталога; документы пока только на ru
        var root = new DocStore(() => "", () => "ru").Root();

        Assert.True(Directory.Exists(Path.Combine(root, "en", "models")),
            "нет каталога en/models для будущих переводов");
    }
}
