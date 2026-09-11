using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-264 (версия 1.94): ФОРМА ОБЪЕКТА — раздел адаптера LoRA, выбор эталонного файла
/// и превью картинки.
///
/// Что проверяется:
/// <list type="number">
/// <item>ПАПКА ПРОЕКТА — разбор относительного пути (<see cref="ProjectFiles"/>): за край
/// папки не выпускаем, список каталога отдаёт пути в том же виде, в каком они хранятся
/// у объекта, предел на число записей соблюдается;</item>
/// <item>ПРЕВЬЮ — сведения о файле: есть ли он, картинка ли это, каталог ли;</item>
/// <item>РАЗМЕТКА — превью слева от поля пути, кнопка файлового диалога, раздел LoRA только
/// у вида «адаптер LoRA», загрузка превью после первой отрисовки (проверка по файлу, приём
/// T-209/T-217; поведение в браузере — живой проверкой);</item>
/// <item>СЛОВАРИ — новые тексты в обоих языках, раздел переименован.</item>
/// </list>
/// </summary>
public sealed class T264Tests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ai2p-t264-" + Guid.NewGuid().ToString("N"));

    public T264Tests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "refs", "hero"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "проект");
        File.WriteAllBytes(Path.Combine(_root, "refs", "hero.png"), new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(Path.Combine(_root, "refs", "hero.safetensors"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_root, "refs", "hero", "face.JPG"), new byte[] { 1, 2 });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог мог остаться занятым — прогону это не мешает
        }
    }

    // ---------- 1. путь внутри папки проекта ----------

    /// <summary>Пустой путь — сама папка проекта, обычный путь раскрывается внутри неё.</summary>
    [Fact]
    public void A_Relative_Path_Resolves_Inside_The_Project_Folder()
    {
        Assert.Equal(Path.GetFullPath(_root), ProjectFiles.Resolve(_root, null));
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "refs", "hero.png")),
            ProjectFiles.Resolve(_root, "refs/hero.png"));
    }

    /// <summary>За край папки проекта путь не выпускается: иначе полем формы читался бы
    /// любой файл компьютера — той же строкой, что отдаёт файлы наружу.</summary>
    [Fact]
    public void A_Path_Out_Of_The_Project_Folder_Is_Refused()
    {
        Assert.Null(ProjectFiles.Resolve(_root, "../secrets.json"));
        Assert.Null(ProjectFiles.Resolve(_root, "refs/../../secrets.json"));
        Assert.Null(ProjectFiles.Resolve(_root, OperatingSystem.IsWindows() ? @"C:\Windows\win.ini" : "/etc/passwd"));
        // папки у проекта может не быть вовсе (её задают на каждом сервере отдельно)
        Assert.Null(ProjectFiles.Resolve(null, "refs/hero.png"));
        Assert.Null(ProjectFiles.Resolve(Path.Combine(_root, "нет-такой-папки"), "hero.png"));
    }

    /// <summary>Список каталога: подкаталоги отдельно от файлов, пути ОТНОСИТЕЛЬНЫЕ и с «/»
    /// (ровно так путь хранится у объекта), картинки помечены.</summary>
    [Fact]
    public void The_Listing_Gives_Relative_Paths_And_Marks_Images()
    {
        var root = ProjectFiles.List(_root, null)!;

        Assert.Equal("", root.Path);
        Assert.Null(root.Parent); // выше папки проекта не поднимаемся
        Assert.Equal(Path.GetFullPath(_root), root.Root);
        Assert.Equal(["refs"], root.Dirs.Select(d => d.Path));
        Assert.Equal(["readme.md"], root.Files.Select(f => f.Path));

        var refs = ProjectFiles.List(_root, "refs")!;

        Assert.Equal("refs", refs.Path);
        Assert.Equal("", refs.Parent);
        Assert.Equal(["refs/hero"], refs.Dirs.Select(d => d.Path));
        Assert.Equal(["refs/hero.png", "refs/hero.safetensors"], refs.Files.Select(f => f.Path));
        Assert.True(refs.Files.Single(f => f.Path == "refs/hero.png").IsImage);
        Assert.False(refs.Files.Single(f => f.Path == "refs/hero.safetensors").IsImage);
        Assert.Equal(4, refs.Files.Single(f => f.Path == "refs/hero.png").Size);
        Assert.False(refs.Truncated);
    }

    /// <summary>Каталога нет или путь уводит за край — списка нет (форма покажет отказ),
    /// а не пустой список: пустой означал бы «каталог пуст».</summary>
    [Fact]
    public void There_Is_No_Listing_For_A_Foreign_Or_Missing_Path()
    {
        Assert.Null(ProjectFiles.List(_root, "../"));
        Assert.Null(ProjectFiles.List(_root, "нет-такого-каталога"));
        Assert.Null(ProjectFiles.List(_root, "readme.md")); // это файл, а не каталог
        Assert.Null(ProjectFiles.List(null, null));
    }

    /// <summary>В папке проекта бывают десятки тысяч файлов: список обрезается по пределу
    /// и ЧЕСТНО об этом говорит — молча показанная часть читалась бы как «это всё».</summary>
    [Fact]
    public void A_Huge_Folder_Is_Cut_And_Says_So()
    {
        var many = Path.Combine(_root, "many");
        Directory.CreateDirectory(many);
        for (var i = 0; i <= ProjectFiles.Limit; i++)
        {
            File.WriteAllText(Path.Combine(many, $"f{i:0000}.txt"), "");
        }

        var listing = ProjectFiles.List(_root, "many")!;

        Assert.Equal(ProjectFiles.Limit, listing.Files.Count);
        Assert.True(listing.Truncated);
    }

    // ---------- 2. превью: сведения о файле ----------

    /// <summary>Картинка — показываем; чужой файл и каталог — нет; путь за краем папки
    /// сведений не даёт вовсе.</summary>
    [Fact]
    public void The_Preview_Knows_What_Can_Be_Shown()
    {
        var image = ProjectFiles.Info(_root, "refs/hero.png")!;
        Assert.True(image.Exists);
        Assert.True(image.IsImage);
        Assert.False(image.IsDirectory);
        Assert.Equal(4, image.Size);

        var other = ProjectFiles.Info(_root, "refs/hero.safetensors")!;
        Assert.True(other.Exists);
        Assert.False(other.IsImage);

        var missing = ProjectFiles.Info(_root, "refs/нет.png")!;
        Assert.False(missing.Exists);
        Assert.False(missing.IsImage);

        var dir = ProjectFiles.Info(_root, "refs")!;
        Assert.True(dir.Exists);
        Assert.True(dir.IsDirectory);
        Assert.False(dir.IsImage);

        Assert.Null(ProjectFiles.Info(_root, "../secrets.json"));
    }

    /// <summary>Картинку узнаём по расширению без учёта регистра, а у удалённого адреса
    /// отбрасываем хвост запроса — тем же методом форма решает, показывать ли превью
    /// вписанного руками https://…</summary>
    [Fact]
    public void An_Image_Is_Recognised_By_Its_Extension()
    {
        Assert.True(ProjectFiles.IsImage("refs/hero.PNG"));
        Assert.True(ProjectFiles.IsImage("https://example.org/img/hero.jpg?v=3"));
        Assert.True(ProjectFiles.IsImage("refs/hero/face.JPG"));
        Assert.False(ProjectFiles.IsImage("refs/hero.safetensors"));
        Assert.False(ProjectFiles.IsImage("https://example.org/gallery/hero"));
        Assert.False(ProjectFiles.IsImage(null));
    }

    /// <summary>Адрес превью — наша обычная ссылка на файл ПАПКИ ПРОЕКТА: на соседнем
    /// сервере кластера тот же путь покажет его собственную копию файла.</summary>
    [Fact]
    public void The_Preview_Url_Is_Our_Project_File_Link()
    {
        var url = FileLinks.Relative(new FileLink(FileLinks.Project, "P1", "refs/герой.png"));

        Assert.StartsWith("api/files/project?projectId=P1&path=", url);
        var parsed = FileLinks.Parse(url)!;
        Assert.Equal("refs/герой.png", parsed.Path);
    }

    // ---------- 3. разметка форм ----------

    /// <summary>Раздел адаптера показывается ТОЛЬКО у объекта вида «адаптер LoRA»
    /// (по заданию T-264): у персонажа и локации своего адаптера нет.</summary>
    [Fact]
    public void The_Lora_Section_Belongs_To_The_Lora_Kind_Only()
    {
        var markup = Ui("ObjectDialog.razor");

        Assert.Contains("@if (_item.Type == ObjectKinds.Lora)", markup);
        // раздел стоит внутри этого условия, а не рядом с ним
        var at = markup.IndexOf("@if (_item.Type == ObjectKinds.Lora)", StringComparison.Ordinal);
        Assert.InRange(markup.IndexOf("data-object-lora-panel", StringComparison.Ordinal), at, markup.Length);
        // с T-12-S1 вместо трёх кнопок-заглушек одна — вход в редактор LoRA; проверяем
        // по-прежнему то, ради чего этот тест и написан: раздел целиком стоит ВНУТРИ условия
        foreach (var probe in new[]
                 {
                     "data-object-lora-status", "data-object-lora-path", "data-object-lora-setup",
                 })
        {
            Assert.InRange(markup.IndexOf(probe, StringComparison.Ordinal), at, markup.Length);
        }
    }

    /// <summary>У поля эталонного файла есть кнопка файлового диалога, а СЛЕВА от него —
    /// окно превью. Превью спрашивается после первой отрисовки: форма не ждёт ответа
    /// сервера, чтобы показаться.</summary>
    [Fact]
    public void The_Reference_File_Has_A_Picker_And_A_Preview()
    {
        var markup = Ui("ObjectDialog.razor");

        Assert.Contains("data-object-path-browse", markup);
        Assert.Contains("ProjectFilePickerDialog", markup);
        Assert.Contains("data-object-preview-box", markup);
        Assert.Contains("data-object-preview-img", markup);
        // превью стоит ПЕРЕД полем пути — то есть слева от него в строке
        Assert.True(markup.IndexOf("data-object-preview-box", StringComparison.Ordinal)
                    < markup.IndexOf("data-object-path=", StringComparison.Ordinal));
        Assert.Contains("OnAfterRenderAsync", markup);
        // поле пути НЕ мгновенное (T-194): на медленном канале такое поле теряет символы
        var pathField = markup[markup.IndexOf("data-object-path=", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Immediate", pathField[..Math.Min(400, pathField.Length)]);
    }

    /// <summary>Диалог выбора файла ходит по ПАПКЕ ПРОЕКТА: каталоги открываются, файл
    /// выбирается щелчком, выше папки проекта подниматься нечем.</summary>
    [Fact]
    public void The_File_Picker_Walks_The_Project_Folder()
    {
        var markup = Ui("ProjectFilePickerDialog.razor");

        Assert.Contains("data-projectfile-dir", markup);
        Assert.Contains("data-projectfile-file", markup);
        Assert.Contains("data-projectfile-up", markup);
        Assert.Contains("GetProjectDirAsync", markup);
        Assert.Contains("Dialog.Close(DialogResult.Ok(file.Path))", markup);
        // у проекта может не быть папки на этом сервере: отказ показывается В ОКНЕ,
        // а не вечной полосой прогресса
        Assert.Contains("data-projectfile-error", markup);
    }

    /// <summary>Эндпойнты папки проекта заведены (их зовёт форма объекта).</summary>
    [Fact]
    public void The_Endpoints_Are_Declared()
    {
        var api = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.Server", "Api", "ApiEndpoints.cs"));

        Assert.Contains("\"/fs/project\"", api);
        Assert.Contains("\"/fs/project-file\"", api);
        Assert.Contains("ProjectFiles.List", api);
        Assert.Contains("ProjectFiles.Info", api);
    }

    // ---------- 4. словари ----------

    /// <summary>Раздел называется «адаптер LoRA» (переименование по заданию), и все новые
    /// тексты есть в ОБОИХ словарях: иначе английский интерфейс покажет сам ключ.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        var ru = Dictionary("ru");
        var en = Dictionary("en");

        Assert.Equal("адаптер LoRA", ru["objects.lora"]);
        Assert.Equal("LoRA adapter", en["objects.lora"]);
        // название вида и заголовок раздела теперь совпадают: раздел и есть этот вид
        Assert.Equal(ru["object.kind.lora"], ru["objects.lora"]);

        foreach (var key in new[]
                 {
                     "objects.path.browse", "objects.preview.empty", "objects.preview.missing",
                     "objects.preview.notImage", "objects.files.title", "objects.files.up",
                     "objects.files.empty", "objects.files.truncated", "msg.apiEndpoints.38",
                 })
        {
            Assert.True(ru.ContainsKey(key), $"нет в ru.json: {key}");
            Assert.True(en.ContainsKey(key), $"нет в en.json: {key}");
            Assert.NotEqual("", ru[key]);
            Assert.NotEqual("", en[key]);
        }
    }

    private static Dictionary<string, string> Dictionary(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "i18n", lang + ".json")))!;

    private static string Ui(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }
}
