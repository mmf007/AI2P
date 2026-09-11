using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo47_2 (ТЗ v1.55, гл. 14): ИСПРАВЛЕНИЯ ПЕРВОГО ЭТАПА ДОКУМЕНТИРОВАНИЯ.
/// Поставляемая документация переехала из корня репозитория в <c>AI2P_app/doc</c> — туда,
/// где лежит код, который её показывает, и откуда её целиком забирает <c>buildRelease</c>.
/// Внутри языка появились РАЗДЕЛЫ: <c>models/</c> (по названию модели) и <c>import/</c>
/// (по коду вида источника) — у справочника импортов тоже есть кнопка «i».
/// </summary>
public sealed class Todo47_2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo47-2-" + Guid.NewGuid().ToString("N"));

    public Todo47_2Tests() => Directory.CreateDirectory(_dir);

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

    private void Write(string lang, string section, string fileName, string text)
    {
        var dir = Path.Combine(_dir, lang, section);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), text);
    }

    /// <summary>Каталог поставляемой документации так, как его находит само приложение.</summary>
    private static string ShippedRoot() => new DocStore(() => "", () => "ru").Root();

    // --- документация переехала в AI2P_app/doc ---

    [Fact]
    public void Shipped_Documentation_Lives_Next_To_The_Code()
    {
        // она поставляется с программой, а не описывает проект: её место — рядом с кодом,
        // в AI2P_app/doc, откуда publish забирает её целиком в корень выкладки
        var root = ShippedRoot();

        Assert.NotEqual("", root);
        var appDir = Directory.GetParent(root)!.FullName;
        Assert.True(File.Exists(Path.Combine(appDir, "AI2P.sln")),
            $"каталог документации найден не в AI2P_app: {root}");
    }

    [Fact]
    public void Publish_Copies_The_Documentation_Of_The_Application()
    {
        // скрипты выкладки должны брать doc РЯДОМ С СОБОЙ (AI2P_app/doc), а не из корня
        // репозитория: в корне лежат ТЗ и отчёты, они к работе с программой не относятся
        var appDir = Directory.GetParent(ShippedRoot())!.FullName;

        var ps = File.ReadAllText(Path.Combine(appDir, "buildRelease.ps1"));
        var sh = File.ReadAllText(Path.Combine(appDir, "buildRelease.sh"));

        Assert.Contains("Join-Path $PSScriptRoot \"doc\"", ps);
        Assert.DoesNotContain("../doc", ps);
        Assert.Contains("DOC_SOURCE=\"$SCRIPT_DIR/doc\"", sh);
        Assert.DoesNotContain("$SCRIPT_DIR/../doc", sh);
    }

    // --- разделы документации ---

    [Fact]
    public void Sections_Are_Separate_Directories()
    {
        Write("ru", "models", "M.md", "модель");
        Write("ru", "import", "trello.md", "импорт");

        Assert.Equal("модель", Store().ModelDoc("M").Text);
        Assert.Equal("импорт", Store().ImportDoc("trello").Text);
        // документ одного раздела не подставляется вместо другого
        Assert.False(Store().ImportDoc("M").Found);
    }

    [Fact]
    public void Import_Document_Falls_Back_By_Language_Too()
    {
        Write("ru", "import", "trello.md", "русский текст");

        var doc = Store("en").ImportDoc("trello");

        Assert.True(doc.Found);
        Assert.Equal("ru", doc.Language);
    }

    [Fact]
    public void Missing_Import_Document_Explains_Where_To_Put_It()
    {
        var doc = Store().ImportDoc("jira");

        Assert.False(doc.Found);
        Assert.Contains(Path.Combine("ru", "import", "jira.md"), doc.Hint);
    }

    [Fact]
    public void Section_And_Kind_Cannot_Escape_The_Documentation_Directory()
    {
        // и раздел, и имя приходят в путь, который собирает сервер (ТЗ гл. 12)
        var secret = Path.Combine(_dir, "secret.md");
        File.WriteAllText(secret, "не отдавать");

        Assert.False(Store().Doc("..", "secret").Found);
        Assert.False(Store().Doc("import", "../../secret").Found);
        Assert.False(Store().ImportDoc("..\\..\\secret").Found);
    }

    // --- документ Trello переименован по коду вида ---

    [Fact]
    public void Trello_Document_Is_Named_After_The_Kind_Code()
    {
        // имя файла — код вида источника (как у моделей — название модели): по нему
        // документ и ищется, когда в форме выбран вид
        var doc = new DocStore(() => "", () => "ru").ImportDoc("trello");

        Assert.True(doc.Found, doc.Hint);
        Assert.Equal("trello.md", doc.FileName);
        Assert.EndsWith(Path.Combine("ru", "import", "trello.md"), doc.Path);
    }

    [Fact]
    public void Trello_Document_Tells_How_To_Get_The_Keys()
    {
        // главное, ради чего документ существует у источника импорта
        var text = new DocStore(() => "", () => "ru").ImportDoc("trello").Text;

        Assert.Contains("API key", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://", text);
    }

    [Fact]
    public void Model_Documents_Survived_The_Move()
    {
        // переезд не должен был потерять то, что написано в todo47
        var store = new DocStore(() => "", () => "ru");

        foreach (var name in new[] { "Claude-Fable-5", "Qwen3.6-35B-A3B-Local" })
        {
            Assert.True(store.ModelDoc(name).Found, $"после переезда нет документа {name}");
        }
    }

    [Fact]
    public void English_Directories_Exist_For_Future_Translations()
    {
        var root = ShippedRoot();

        Assert.True(Directory.Exists(Path.Combine(root, "en", "models")));
        Assert.True(Directory.Exists(Path.Combine(root, "en", "import")));
    }
}
