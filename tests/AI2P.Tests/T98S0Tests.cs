using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-98-S0 (версия 1.112): МЕСТО ХРАНЕНИЯ КАДРОВ ДАТАСЕТА LoRA.
///
/// Подрезанные кадры лежали в ПАПКЕ ПРОЕКТА, а она у каждого сервера своя и не
/// реплицируется — на соседнем сервере датасет выглядел пустым. Теперь они живут в каталоге
/// данных организации (<c>projects/PRJ-N/objects/OBJ-владелец/OBJ-датасет/</c>), который
/// реплицируется целиком, и путь такого кадра носит приставку <c>store:</c>.
///
/// Что проверяется:
/// <list type="number">
/// <item>РАЗБОР ПУТИ: приставка узнаётся, каталог датасета складывается по правилу задания,
/// путь считается от каталога данных и не выпускает наружу через «..»;</item>
/// <item>ССЫЛКИ ПРЕДСТАВЛЕНИЙ: кадр хранилища отдаёт <c>api/files/raw</c>, а файл в папке
/// проекта — по-прежнему <c>api/files/project</c>;</item>
/// <item>ПЕРЕНОС НАКОПЛЕННЫХ КАДРОВ (шаг обновления билда 112): файл копируется в хранилище,
/// путь переписывается, повторный проход не делает ничего, кадр без файла не трогается;</item>
/// <item>РЕПЛИКАЦИЯ: каталог, в который лёг кадр, попадает в манифест файлов организации.</item>
/// </list>
/// </summary>
public sealed class T98S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // --- 1. Разбор пути ---

    [Fact]
    public void A_Store_Path_Tells_About_Itself()
    {
        var rel = ObjectFiles.DatasetDirRel("PRJ-1", "OBJ-2", "OBJ-3") + "/001.png";
        Assert.Equal("projects/PRJ-1/objects/OBJ-2/OBJ-3/001.png", rel);

        var stored = ObjectFiles.Store(rel);
        Assert.StartsWith(ObjectFiles.StorePrefix, stored);
        Assert.True(ObjectFiles.IsStore(stored));
        Assert.Equal(rel, ObjectFiles.Rel(stored));

        // путь папки проекта остаётся тем же, чем был: приставки у него нет
        Assert.False(ObjectFiles.IsStore("refs/hero.png"));
        Assert.Equal("refs/hero.png", ObjectFiles.Rel("refs/hero.png"));
    }

    [Fact]
    public void A_Store_Path_Is_Counted_From_The_Data_Dir_And_Cannot_Escape_It()
    {
        var data = _f.Files.DataDir;
        var rel = ObjectFiles.DatasetDirRel("PRJ-1", "OBJ-2", "OBJ-3") + "/001.png";

        var abs = ObjectFiles.Resolve(ObjectFiles.Store(rel), projectFolder: null, data);
        Assert.Equal(Path.GetFullPath(Path.Combine(data, rel.Replace('/', Path.DirectorySeparatorChar))),
            abs);

        // наружу не выпускаем: путь называет партнёр и он же приезжает репликацией
        Assert.Null(ObjectFiles.Resolve("store:../secrets/mail.password.json", null, data));
        Assert.Null(ObjectFiles.Resolve("store:C:/windows/win.ini", null, data));
        // каталога данных не назвали — считать не от чего
        Assert.Null(ObjectFiles.Resolve(ObjectFiles.Store(rel), null, null));
    }

    // --- 2. Ссылки представлений ---

    [Fact]
    public void The_Frame_Of_A_Dataset_Is_Served_From_The_Store_And_Other_Files_From_The_Project()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewObject(project.Id, "Вася-LoRA", ObjectKinds.Lora, null, "");
        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        var rel = ObjectFiles.DatasetDirRel(project.Slug, lora.DisplayId, dataset.DisplayId)
                  + "/001.png";
        var frame = NewObject(project.Id, "Кадр 1", ObjectKinds.Image, dataset.Id,
            ObjectFiles.Store(rel));
        var hero = NewObject(project.Id, "Вася", ObjectKinds.Character, null, "refs/hero.png");

        var frameUrl = ObjectPics.Own(frame, project.Id);
        Assert.Contains("api/files/raw", frameUrl);
        Assert.Contains(Uri.EscapeDataString(rel), frameUrl);
        // приставка — наша, наружу она не уходит: эндпойнт данных её не знает
        Assert.DoesNotContain(ObjectFiles.StorePrefix, frameUrl);

        Assert.Contains("api/files/project", ObjectPics.Own(hero, project.Id));

        // превью персонажа — первая картинка внутреннего списка: кадр датасета годится
        var pics = ObjectPics.Build([lora, dataset, frame, hero], project.Id);
        Assert.Equal(frameUrl, pics[lora.Id]);
    }

    // --- 3. Перенос накопленных кадров (шаг обновления билда 112) ---

    [Fact]
    public void The_Upgrade_Moves_Old_Frames_Into_The_Store_And_Repeats_Nothing()
    {
        var folder = ProjectDir();
        var project = _f.Projects.Create("Ролик", folder, null, null);
        var lora = NewObject(project.Id, "Вася-LoRA", ObjectKinds.Lora, null, "");
        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);

        // кадр прежних версий: файл лежит в папке проекта, путь без приставки
        Directory.CreateDirectory(Path.Combine(folder, "lora", lora.DisplayId, dataset.DisplayId));
        var old = Path.Combine(folder, "lora", lora.DisplayId, dataset.DisplayId, "001.png");
        File.WriteAllBytes(old, [1, 2, 3]);
        var frame = NewObject(project.Id, "Кадр 1", ObjectKinds.Image, dataset.Id,
            $"lora/{lora.DisplayId}/{dataset.DisplayId}/001.png");
        // кадр, файла которого на этом сервере нет вовсе
        var lost = NewObject(project.Id, "Кадр 2", ObjectKinds.Image, dataset.Id, "lora/нет.png");

        Assert.Equal(1, _f.Objects.MoveFramesToStore(_f.Files, _f.Projects));

        var moved = _f.Objects.Get(frame.Id)!;
        var rel = ObjectFiles.DatasetDirRel(project.Slug, lora.DisplayId, dataset.DisplayId)
                  + "/001.png";
        Assert.Equal(ObjectFiles.Store(rel), moved.PathOrUrl);
        Assert.True(File.Exists(_f.Files.Abs(rel)));
        // папка проекта принадлежит человеку — оттуда шаг обновления ничего не удаляет
        Assert.True(File.Exists(old));
        // файла нет — путь не трогаем: иначе он указывал бы на пустое место
        Assert.Equal("lora/нет.png", _f.Objects.Get(lost.Id)!.PathOrUrl);

        // второй проход (обновление через несколько версий) находить нечего
        Assert.Equal(0, _f.Objects.MoveFramesToStore(_f.Files, _f.Projects));
    }

    // --- 4. Репликация ---

    [Fact]
    public void The_Frame_Travels_To_The_Partner_With_The_Files_Of_The_Organization()
    {
        var rel = ObjectFiles.DatasetDirRel("PRJ-1", "OBJ-2", "OBJ-3") + "/001.png";
        var abs = _f.Files.Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, [1, 2, 3]);

        // манифест каталога данных организации — то, чем серверы сверяют файлы (ТЗ гл. 6)
        var entries = FileManifest.Scan(_f.Files.DataDir, RepIgnore.Empty, skipService: true);
        Assert.Contains(entries, e => e.Path == rel);
        // и сама строка объекта едет обычным журналом изменений
        Assert.Contains("objects", ChangeLog.OrgTables);
    }

    // --- общее ---

    private string ProjectDir()
    {
        var dir = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private ObjectItem NewObject(string projectId, string name, string kind, string? parentId,
        string path) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = name,
            Type = kind,
            PathOrUrl = path,
        }, null);
}
