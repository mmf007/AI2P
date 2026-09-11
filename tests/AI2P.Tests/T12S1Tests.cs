using System.Text;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-12-S1 (версия 1.95): РЕДАКТОР LoRA — датасет, модели и обучение.
///
/// Что проверяется:
/// <list type="number">
/// <item>СПИСОК МОДЕЛЕЙ объекта: пара «объект + модель» одна, снятая строка оживает,
/// номер и код модели подставляются из справочника, состояние правится отдельно от
/// остальной записи;</item>
/// <item>СОСТОЯНИЯ обучения: необучена → обучается → обучена/ошибка, время начала и конца
/// проставляются на переходах, текст ошибки сохраняется;</item>
/// <item>ОБУЧЕНИЕ: настоящий прогон процессом — датасет собирается из кадров объекта с
/// подписями, результат ложится в репозиторий моделей и становится текущим адаптером
/// объекта; отказы (модель без LoRA, пустой датасет, незаданная команда, переобучение
/// без подтверждения) называются словами;</item>
/// <item>РАЗМЕР КАРТИНКИ по заголовку файла (проверка присланного кадра);</item>
/// <item>СХЕМА и РЕПЛИКАЦИЯ: таблица заведена и едет партнёру;</item>
/// <item>СЛОВАРИ: у каждого состояния и у каждого нового сообщения есть текст в обоих
/// языках.</item>
/// </list>
/// </summary>
public sealed class T12S1Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Kandinsky T2V из дистрибутива — запись, у которой работа с LoRA объявлена.</summary>
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    // --- 1. Список моделей объекта ---

    [Fact]
    public void A_Model_Is_Added_Once_And_Carries_Its_Number_And_Code()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");

        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        Assert.Equal(LoraModelStates.None, row.Status);
        // номер и код модели живут в справочнике, а не в строке: показывать в списке
        // «6f1a45e0-…» вместо «M-7 Kandinsky» значило бы не показать ничего
        var model = _f.Models.Get(T2VId)!;
        Assert.Equal(model.DisplayId, row.ModelDisplayId);
        Assert.Equal(model.Name, row.ModelName);
        Assert.True(row.LoraSupported);

        // вторая такая же строка не заводится: обучать один адаптер дважды под одни веса
        // незачем — для этого есть переобучение
        Assert.Throws<ArgumentException>(() => _f.Objects.AddLoraModel(lora.Id, T2VId, null));
        Assert.Single(_f.Objects.LoraModels(lora.Id));
    }

    [Fact]
    public void A_Removed_Row_Comes_Back_Clean()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Error, null, "упало");

        _f.Objects.DeleteLoraModel(row.Id, null);
        Assert.Empty(_f.Objects.LoraModels(lora.Id));

        // заводим ту же пару заново: строка оживает ЧИСТОЙ — прошлая ошибка к новому
        // обучению отношения не имеет
        var again = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        Assert.Equal(LoraModelStates.None, again.Status);
        Assert.Equal("", again.Error);
        Assert.Single(_f.Objects.LoraModels(lora.Id));
    }

    [Fact]
    public void An_Unknown_Model_Is_Refused()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        Assert.Throws<ArgumentException>(() =>
            _f.Objects.AddLoraModel(lora.Id, Guid.NewGuid().ToString(), null));
        Assert.Throws<ArgumentException>(() => _f.Objects.AddLoraModel(lora.Id, "", null));
    }

    // --- 2. Состояния обучения ---

    [Fact]
    public void The_State_Carries_The_Times_And_The_Error()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        Assert.Null(row.StartedAt);

        var training = _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Training)!;
        Assert.Equal(LoraModelStates.Training, training.Status);
        Assert.NotNull(training.StartedAt);
        Assert.Null(training.FinishedAt);   // идёт — конца ещё нет

        var failed = _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Error, null, "нет команды")!;
        Assert.Equal(LoraModelStates.Error, failed.Status);
        Assert.Equal("нет команды", failed.Error);
        Assert.NotNull(failed.FinishedAt);
        Assert.NotNull(failed.StartedAt);   // начало не потерялось

        // неизвестное состояние приезжает только правкой базы руками — считаем «необучена»
        Assert.Equal(LoraModelStates.None,
            _f.Objects.SetLoraModelState(row.Id, "какое-то")!.Status);
    }

    /// <summary>
    /// Состояние правится ОТДЕЛЬНО от остальной записи: обучение идёт часами и меняет
    /// строку фоном, пока человек правит ту же форму. Если бы состояние лежало в объекте,
    /// сохранение формы возвращало бы его к тому, что прочитали до начала обучения.
    /// </summary>
    [Fact]
    public void Training_State_Survives_A_Save_Of_The_Object()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Training);

        // форма объекта сохраняет то, что прочитала ДО начала обучения
        var stale = _f.Objects.Get(lora.Id)!;
        stale.Description = "рыжий, в плаще";
        _f.Objects.Update(stale, null);

        Assert.Equal(LoraModelStates.Training, _f.Objects.LoraModel(row.Id)!.Status);
    }

    // --- 3. Обучение ---

    [Fact]
    public void A_Model_Without_Lora_Is_Refused_By_Name()
    {
        _f.Models.Seed();
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        // облачная запись дистрибутива: работы с адаптерами у неё нет и причина названа
        var cloudId = "6f1a45e0-0d31-4c65-9a01-000000000002";
        var row = _f.Objects.AddLoraModel(lora.Id, cloudId, null);

        var error = Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));
        Assert.Contains(_f.Models.Get(cloudId)!.Name, error.Message);
        // строка осталась необученной: отказ на запуске — это ответ человеку, а не состояние
        Assert.Equal(LoraModelStates.None, _f.Objects.LoraModel(row.Id)!.Status);
    }

    [Fact]
    public void An_Empty_Dataset_Is_Refused()
    {
        WriteTrainProfile(T2VId, command: "echo ok", minItems: 3);
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        var error = Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void A_Missing_Command_Is_Refused_By_Name()
    {
        WriteTrainProfile(T2VId, command: "", minItems: 0);
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой в плаще");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        var error = Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));
        // ровно тот случай, о котором предупредил справочник (T-13-S1): команду обучения
        // никто не заполнил, и выдуманный путь был бы хуже пустого
        Assert.Contains(_f.Models.Get(T2VId)!.Name, error.Message);
    }

    [Fact]
    public async Task A_Real_Training_Collects_The_Dataset_And_Brings_The_Adapter()
    {
        var projectDir = ProjectDir();
        WriteTrainProfile(T2VId, command: MakeAdapterCommand(), minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой в плаще");
        AddFrame(project.Id, lora.Id, "кадр2.png", "герой в профиль");
        // погашенный кадр в обучение не идёт: «выключить, не удаляя» — единственный способ
        // выкинуть неудачный ракурс, не теряя файл
        var off = AddFrame(project.Id, lora.Id, "кадр3.png", "смазанный");
        off.IsActive = false;
        _f.Objects.Update(off, null);

        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        var started = _f.LoraTrain.Start(row.Id, force: false);
        Assert.Equal(LoraModelStates.Training, started.Status);

        var done = await WaitAsync(row.Id);
        Assert.Equal(LoraModelStates.Ready, done.Status);
        Assert.Equal("", done.Error);

        // 1) датасет собран из живых кадров, подписи легли рядом .txt-файлами
        var dataset = Path.Combine(projectDir, "lora", lora.DisplayId, "dataset");
        Assert.Equal(2, Directory.GetFiles(dataset, "*.png").Length);
        Assert.Equal("герой в плаще",
            File.ReadAllText(Path.Combine(dataset, "001.txt")).Trim());

        // 2) готовый адаптер лежит там, откуда его берёт движок модели
        Assert.Equal("loras/" + lora.DisplayId + ".safetensors", done.Path);
        Assert.True(File.Exists(Path.Combine(_f.ModelsRepo, "loras", lora.DisplayId + ".safetensors")));

        // 3) и он же стал ТЕКУЩИМ адаптером объекта — это то, что подставляется в модель
        var item = await WaitObjectAsync(lora.Id);
        Assert.Equal(done.Path, item.LoraPath);
        Assert.Equal(LoraStates.Ready, item.LoraStatus);
    }

    [Fact]
    public async Task Retraining_Needs_A_Confirmation()
    {
        var projectDir = ProjectDir();
        WriteTrainProfile(T2VId, command: MakeAdapterCommand(), minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        _f.LoraTrain.Start(row.Id, force: false);
        await WaitAsync(row.Id);

        // без подтверждения обученную модель не трогаем: переобучение затирает готовый файл
        Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));
        // с подтверждением — идёт
        Assert.Equal(LoraModelStates.Training, _f.LoraTrain.Start(row.Id, force: true).Status);
        Assert.Equal(LoraModelStates.Ready, (await WaitAsync(row.Id)).Status);
    }

    [Fact]
    public async Task A_Failed_Command_Becomes_An_Error_With_Its_Own_Text()
    {
        var projectDir = ProjectDir();
        // команда, которой нет: процесс оболочки завершится ненулевым кодом
        WriteTrainProfile(T2VId, command: "ai2p-нет-такой-команды-12s1", minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitAsync(row.Id);
        Assert.Equal(LoraModelStates.Error, done.Status);
        // текст ошибки обязан быть: обучение идёт часами, и по одному слову «ошибка»
        // человеку через сутки чинить нечего
        Assert.NotEqual("", done.Error);
    }

    // --- 4. Размер картинки по заголовку ---

    [Fact]
    public void The_Picture_Size_Is_Read_From_The_Header()
    {
        var png = Png(120, 45);
        Assert.Equal("png", ImageProbe.Format(png));
        Assert.Equal((120, 45), ImageProbe.Size(png));

        var jpeg = Jpeg(300, 200);
        Assert.Equal("jpeg", ImageProbe.Format(jpeg));
        Assert.Equal((300, 200), ImageProbe.Size(jpeg));

        // не наш формат и обрывок заголовка — «размер неизвестен», а не падение
        Assert.Null(ImageProbe.Size(Encoding.ASCII.GetBytes("GIF89a not an image")));
        Assert.Null(ImageProbe.Size(png[..12]));
        Assert.Equal("", ImageProbe.Format([1, 2, 3]));
    }

    // --- 5. Схема и репликация ---

    [Fact]
    public void The_Table_Is_In_The_Schema_And_Travels_To_The_Partner()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(object_loras)", r => r.S("name"));
        foreach (var name in new[] { "id", "object_id", "model_id", "status", "path", "error" })
        {
            Assert.Contains(name, columns);
        }
        // на соседнем сервере должно быть видно, под какие модели персонаж обучен
        Assert.Contains("object_loras", ChangeLog.OrgTables);
    }

    // --- 6. Словари ---

    [Fact]
    public void Every_State_And_Message_Has_A_Text_In_Both_Languages()
    {
        foreach (var state in LoraModelStates.All)
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                var text = Loc.In(lang, "lora.state." + state);
                Assert.NotEqual("lora.state." + state, text);
                Assert.NotEqual("", text);
            }
        }
        // сообщения обучения: их читает человек, и «msg.lora.9» вместо текста — это отказ
        // без объяснения
        for (var i = 1; i <= 22; i++)
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Assert.NotEqual("msg.lora." + i, Loc.In(lang, "msg.lora." + i));
            }
        }
    }

    // --- общее ---

    private string ProjectDir()
    {
        var dir = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private ObjectItem NewLora(string projectId, string name) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = ObjectKinds.Lora,
        }, null);

    /// <summary>Кадр датасета — ребёнок объекта LoRA вида «эталонный кадр» с настоящим файлом
    /// в папке проекта: обучение копирует именно файлы, а не записи.</summary>
    private ObjectItem AddFrame(string projectId, string ownerId, string fileName, string caption)
    {
        var rel = "lora-src/" + fileName;
        var abs = Path.Combine(ProjectDir(), "lora-src", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, Png(16, 16));
        return _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = ownerId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Type = ObjectKinds.Image,
            PathOrUrl = rel,
            Description = caption,
        }, null);
    }

    /// <summary>Профайл модели с настройкой обучения: всё, что делает сервис, приходит отсюда.</summary>
    private void WriteTrainProfile(string modelId, string command, int minItems)
    {
        _f.Models.Seed();
        var model = _f.Models.Get(modelId)!;
        var escaped = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _f.Files.WriteText(model.ProfilePath, $$"""
            {
              "provider": "comfyui",
              "model": "test",
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": { "kind": "workflow", "node": "LoraLoaderModelOnly", "dir": "loras" },
                "train": {
                  "kind": "process",
                  "dataset": { "kind": "dir", "path": "lora/{object}/dataset",
                               "captions": "txt", "minItems": {{minItems}} },
                  "start": { "kind": "process", "command": "{{escaped}}", "steps": 10 },
                  "wait": { "kind": "process", "timeoutMinutes": 5 },
                  "result": { "kind": "file", "path": "lora/{object}/out/adapter.safetensors",
                              "target": "loras/{object}.safetensors" }
                }
              }
            }
            """);
    }

    /// <summary>Команда, которая делает вид, что обучила адаптер: кладёт файл туда, где его
    /// ждёт настройка. Оболочка своя у каждой системы, поэтому и команда своя.</summary>
    private static string MakeAdapterCommand() => OperatingSystem.IsWindows()
        ? "mkdir \"{output}\" 2>nul & echo weights> \"{output}\\adapter.safetensors\""
        : "mkdir -p '{output}' && echo weights > '{output}/adapter.safetensors'";

    /// <summary>Дождаться конца обучения: оно идёт фоном, синхронного ответа у него нет.</summary>
    private async Task<ObjectLoraModel> WaitAsync(string loraId)
    {
        for (var i = 0; i < 300; i++)
        {
            var row = _f.Objects.LoraModel(loraId)!;
            if (row.Status != LoraModelStates.Training)
            {
                return row;
            }
            await Task.Delay(100);
        }
        return _f.Objects.LoraModel(loraId)!;
    }

    /// <summary>Дождаться, пока обученный адаптер станет ТЕКУЩИМ адаптером объекта.
    /// Ждать одной только смены состояния строки мало: обучение пишет её и поле объекта
    /// ДВУМЯ соседними вызовами (<c>SetLoraModelState</c>, затем <c>SetObjectLoraFile</c>),
    /// и под нагрузкой полного прогона тест успевает прочитать объект в промежутке —
    /// красный «Expected loras/OBJ-1.safetensors, Actual """" » при полностью исправной
    /// работе (та же гонка, что у T166Tests, опыт выпуска 1.85).</summary>
    private async Task<ObjectItem> WaitObjectAsync(string objectId)
    {
        for (var i = 0; i < 300; i++)
        {
            var item = _f.Objects.Get(objectId)!;
            if (!string.IsNullOrEmpty(item.LoraPath))
            {
                return item;
            }
            await Task.Delay(100);
        }
        return _f.Objects.Get(objectId)!;
    }

    /// <summary>Наименьший настоящий PNG заданного размера: заголовок нам и нужен —
    /// размер картинки сервер читает именно из него.</summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange([0, 0, 0, 13]);
        bytes.AddRange(Encoding.ASCII.GetBytes("IHDR"));
        bytes.AddRange(Be32(width));
        bytes.AddRange(Be32(height));
        bytes.AddRange([8, 6, 0, 0, 0, 0, 0, 0, 0]);
        return bytes.ToArray();
    }

    /// <summary>Заголовок JPEG с маркером кадра: сначала APP0, потом SOF0.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 };
        bytes.AddRange(Encoding.ASCII.GetBytes("JFIF\0"));
        bytes.AddRange([1, 1, 0, 0, 1, 0, 1, 0, 0]);
        bytes.AddRange([0xFF, 0xC0, 0, 17, 8]);
        bytes.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        bytes.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);
        bytes.AddRange([3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        return bytes.ToArray();
    }

    private static byte[] Be32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
