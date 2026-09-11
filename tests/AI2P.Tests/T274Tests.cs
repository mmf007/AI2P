using System.Text;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-274 (версия 1.98): ДАТАСЕТ — ОТДЕЛЬНЫЙ ОБЪЕКТ, свои настройки контроля картинок,
/// обучение по выбранному датасету.
///
/// Что проверяется:
/// <list type="number">
/// <item>ВИД «датасет»: известен системе, назван словами в обоих языках;</item>
/// <item>ДАТАСЕТЫ ОБЪЕКТА: заводятся (первый — умолчанием), становятся текущими, название
/// свободно даже когда «датасет» в проекте занят, чужой датасет текущим не станет;</item>
/// <item>НАСТРОЙКИ КОНТРОЛЯ: хранятся у датасета, правятся своим вызовом и НЕ затираются
/// сохранением объекта; подставляются из модели — с выбором PNG, когда названо несколько;</item>
/// <item>СВЕРКА С МОДЕЛЬЮ: ругаемся только на превышение и на формат, «меньше» — законно;</item>
/// <item>ОБУЧЕНИЕ: идёт по кадрам ДАТАСЕТА, запоминает его, при смене датасета и при
/// превышении настроек требует подтверждения; объект без датасетов обучается по прямым
/// детям, как до версии 1.98;</item>
/// <item>СХЕМА, ПЕРЕНОС старых кадров и СЛОВАРИ.</item>
/// </list>
/// </summary>
public sealed class T274Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Kandinsky T2V из дистрибутива — запись, у которой работа с LoRA объявлена.</summary>
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    // --- 1. Вид объекта ---

    [Fact]
    public void The_Dataset_Kind_Is_Known_And_Named_In_Both_Languages()
    {
        Assert.True(ObjectKinds.IsKnown(ObjectKinds.Dataset));
        Assert.Contains(ObjectKinds.Dataset, ObjectKinds.All);
        foreach (var lang in new[] { "ru", "en" })
        {
            var title = ObjectKinds.Title(ObjectKinds.Dataset, lang);
            Assert.NotEqual("object.kind." + ObjectKinds.Dataset, title);
            Assert.NotEqual("", title);
        }
    }

    // --- 2. Датасеты объекта ---

    [Fact]
    public void The_First_Dataset_Appears_By_Itself_And_Becomes_Current()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");

        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        Assert.Equal(ObjectKinds.Dataset, dataset.Type);
        Assert.Equal(lora.Id, dataset.ParentId);
        // название умолчанием — то самое слово из словаря
        Assert.Equal(Loc.T("msg.lora.28"), dataset.Name);
        Assert.Equal(dataset.Id, _f.Objects.Get(lora.Id)!.CurrentDatasetId);

        // второй раз ничего не заводится: датасет уже есть — вернётся он
        Assert.Equal(dataset.Id, _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null).Id);
        Assert.Single(_f.Objects.Datasets(lora.Id));
    }

    [Fact]
    public void A_Second_Object_Gets_Its_Own_Dataset_Despite_The_Taken_Name()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var first = NewLora(project.Id, "Вася-LoRA");
        var second = NewLora(project.Id, "Петя-LoRA");

        var a = _f.Objects.EnsureDataset(first.Id, new LoraDatasetLimits(), null);
        // название объекта уникально в ПРОЕКТЕ, а «датасет» — самое ожидаемое название на
        // свете: отказать второму персонажу в датасете из-за занятого имени нельзя
        var b = _f.Objects.EnsureDataset(second.Id, new LoraDatasetLimits(), null);
        Assert.NotEqual(a.Name, b.Name);
        Assert.Contains(second.DisplayId, b.Name);
    }

    [Fact]
    public void A_Named_Dataset_Is_Created_And_Selected()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);

        var second = _f.Objects.CreateDataset(lora.Id, "датасет 1024", new LoraDatasetLimits(), null);
        Assert.Equal("датасет 1024", second.Name);
        // заведённый датасет СРАЗУ текущий: заводят его затем, чтобы сложить туда кадры
        Assert.Equal(second.Id, _f.Objects.Get(lora.Id)!.CurrentDatasetId);
        Assert.Equal(2, _f.Objects.Datasets(lora.Id).Count);
    }

    [Fact]
    public void A_Foreign_Dataset_Does_Not_Become_Current()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var mine = NewLora(project.Id, "Вася-LoRA");
        var other = NewLora(project.Id, "Петя-LoRA");
        var foreign = _f.Objects.EnsureDataset(other.Id, new LoraDatasetLimits(), null);

        // кадры ушли бы не тому объекту, а обучение пошло бы по картинкам другого персонажа
        Assert.Throws<ArgumentException>(() =>
            _f.Objects.SetCurrentDataset(mine.Id, foreign.Id, null));
    }

    // --- 3. Настройки контроля картинок ---

    [Fact]
    public void The_Limits_Live_In_The_Dataset_And_Survive_A_Save_Of_The_Object()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var dataset = _f.Objects.EnsureDataset(lora.Id,
            new LoraDatasetLimits { MaxWidth = 512, MaxHeight = 512, MaxKb = 300, Format = "jpeg" },
            null);

        var saved = LoraDatasetLimits.Parse(_f.Objects.Get(dataset.Id)!.DatasetJson);
        Assert.Equal(512, saved.MaxWidth);
        Assert.Equal("jpeg", saved.Format);

        _f.Objects.SetDatasetLimits(dataset.Id,
            new LoraDatasetLimits { MaxWidth = 768, MaxHeight = 512, MaxKb = 2048, Format = "png" },
            null);

        // форма объекта настройки датасета НЕ ведёт: сохранение объекта тем снимком, который
        // прочитали до правки, обязано их сохранить (то же правило, что у состояния обучения)
        var stale = _f.Objects.Get(dataset.Id)!;
        stale.DatasetJson = "";
        stale.Description = "правка паспорта";
        _f.Objects.Update(stale, null);

        var after = LoraDatasetLimits.Parse(_f.Objects.Get(dataset.Id)!.DatasetJson);
        Assert.Equal(768, after.MaxWidth);
        Assert.Equal("png", after.Format);
        Assert.Equal("правка паспорта", _f.Objects.Get(dataset.Id)!.Description);
    }

    [Fact]
    public void The_Current_Dataset_Survives_A_Save_Of_The_Object()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);

        var stale = _f.Objects.Get(lora.Id)!;
        stale.CurrentDatasetId = null;      // форма объекта о датасете ничего не знает
        _f.Objects.Update(stale, null);

        Assert.Equal(dataset.Id, _f.Objects.Get(lora.Id)!.CurrentDatasetId);
    }

    [Fact]
    public void Png_Wins_When_The_Model_Names_Several_Formats()
    {
        // правило задания: «если в требованиях к картинкам есть несколько форматов,
        // то ставить png, если он есть»
        Assert.Equal("png", LoraDatasetLimits.PickFormat(["jpg", "png", "webp"]));
        Assert.Equal("png", LoraDatasetLimits.PickFormat([".PNG"]));
        Assert.Equal("jpeg", LoraDatasetLimits.PickFormat(["image/jpeg"]));
        // формат не назван — остаётся прежний, а не выдуманный
        Assert.Equal("jpeg", LoraDatasetLimits.PickFormat([], "jpeg"));
        Assert.Equal("jpeg", LoraDatasetLimits.PickFormat(["webp", "avif"], "jpeg"));
    }

    [Fact]
    public void The_Limits_Are_Taken_From_The_Model_And_Fill_The_Gaps_From_The_Common_Ones()
    {
        var model = new LoraDataset
        {
            Width = 768,
            Height = 512,
            MinItems = 10,
            MaxItems = 60,
            Formats = ["jpg", "png"],
        };
        var common = new LoraDatasetLimits { MaxWidth = 1024, MaxHeight = 1024, MaxKb = 777 };

        var taken = LoraDatasetLimits.FromModel(model, common);
        Assert.Equal(768, taken.MaxWidth);
        Assert.Equal(512, taken.MaxHeight);
        Assert.Equal("png", taken.Format);
        Assert.Equal(10, taken.MinItems);
        Assert.Equal(60, taken.MaxItems);
        // о весе кадра модель не сказала ничего — берём то, что было: датасет без предела
        // размера нерабочий, а выдуманное ограничение хуже пустого
        Assert.Equal(777, taken.MaxKb);
    }

    [Fact]
    public void Only_The_Excess_And_The_Format_Are_Reported()
    {
        var model = new LoraDataset
        {
            Width = 768,
            Height = 512,
            MaxKb = 1024,
            MaxItems = 60,
            Formats = ["png"],
        };

        // ровно по модели — молчим
        Assert.Empty(new LoraDatasetLimits
        {
            MaxWidth = 768, MaxHeight = 512, MaxKb = 1024, MaxItems = 60, Format = "png",
        }.Compare(model));

        // МЕНЬШЕ — тоже молчим: обучать на кадрах поменьше никто не запрещает, а требовать
        // «ровно столько» значило бы ругаться на исправную работу
        Assert.Empty(new LoraDatasetLimits
        {
            MaxWidth = 512, MaxHeight = 384, MaxKb = 256, MaxItems = 20, Format = "png",
        }.Compare(model));

        // БОЛЬШЕ по каждому пределу и чужой формат — четыре превышения и формат
        var problems = new LoraDatasetLimits
        {
            MaxWidth = 1024, MaxHeight = 1024, MaxKb = 4096, MaxItems = 120, Format = "jpeg",
        }.Compare(model);
        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.Contains("1024"));
        Assert.Contains(problems, p => p.Contains("JPEG"));

        // модель ничего не объявила — сверять не с чем
        Assert.Empty(new LoraDatasetLimits { MaxWidth = 4096 }.Compare(new LoraDataset()));
    }

    [Fact]
    public void The_Catalog_Declares_The_Picture_Control_Of_Kandinsky()
    {
        _f.Models.Seed();
        var model = _f.Models.Get(T2VId)!;
        _f.Models.ReadProfileSettings(model);
        var dataset = model.Lora.Train.Dataset;

        // обучающий репозиторий kandinsky-5-lora-train берёт пары «*.png + *.txt»
        Assert.True(dataset.HasLimits);
        Assert.Contains("png", dataset.Formats);
        Assert.Equal(768, dataset.Width);
        Assert.Equal(512, dataset.Height);
        Assert.Equal("png", LoraDatasetLimits.FromModel(dataset).Format);
    }

    // --- 4. Обучение ---

    [Fact]
    public async Task Training_Goes_By_The_Frames_Of_The_Current_Dataset()
    {
        var projectDir = ProjectDir();
        WriteTrainProfile(T2VId, MakeAdapterCommand(), minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var first = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        AddFrame(project.Id, first.Id, "кадр1.png", "герой в плаще");
        AddFrame(project.Id, first.Id, "кадр2.png", "герой в профиль");
        // второй датасет — с одним кадром: по нему и запустим
        var second = _f.Objects.CreateDataset(lora.Id, "датасет 512", new LoraDatasetLimits(), null);
        AddFrame(project.Id, second.Id, "кадр3.png", "герой крупно");

        Assert.Equal(2, _f.Objects.DatasetFrames(lora.Id, first.Id).Count);
        Assert.Single(_f.Objects.DatasetFrames(lora.Id));    // текущий — второй

        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitAsync(row.Id);
        Assert.Equal(LoraModelStates.Ready, done.Status);

        // в собранном датасете ровно один кадр — тот, что лежит в текущем датасете
        var collected = Path.Combine(projectDir, "lora", lora.DisplayId, "dataset");
        Assert.Single(Directory.GetFiles(collected, "*.png"));
        // и строка запомнила, ЧЕМ получен лежащий в ней файл
        Assert.Equal(second.Id, done.DatasetId);
        Assert.Equal(second.Name, _f.Objects.LoraModel(row.Id)!.DatasetName);
    }

    [Fact]
    public async Task A_Changed_Dataset_Needs_A_Confirmation()
    {
        var projectDir = ProjectDir();
        WriteTrainProfile(T2VId, MakeAdapterCommand(), minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var first = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        AddFrame(project.Id, first.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        _f.LoraTrain.Start(row.Id, force: false);
        await WaitAsync(row.Id);

        // переключили датасет — и запускаем заново
        var second = _f.Objects.CreateDataset(lora.Id, "датасет 512", new LoraDatasetLimits(), null);
        AddFrame(project.Id, second.Id, "кадр2.png", "герой крупно");

        var check = _f.LoraTrain.Check(row.Id);
        Assert.True(check.DatasetChanged);
        Assert.Equal(first.Name, check.TrainedDatasetName);
        Assert.Equal(second.Name, check.DatasetName);

        // без подтверждения не пойдём: адаптер, обученный на других кадрах, — это другой
        // персонаж, и подменять его молча нельзя
        var error = Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: true));
        Assert.Contains(first.Name, error.Message);
        Assert.Equal(LoraModelStates.Ready, _f.Objects.LoraModel(row.Id)!.Status);

        // с подтверждением — идёт, и запоминается уже новый датасет
        _f.LoraTrain.Start(row.Id, force: true, datasetId: second.Id, confirmDataset: true);
        var done = await WaitAsync(row.Id);
        Assert.Equal(LoraModelStates.Ready, done.Status);
        Assert.Equal(second.Id, done.DatasetId);

        // а запуск по ЗАПОМНЕННОМУ датасету подтверждения не требует: датасет не менялся
        _f.LoraTrain.Start(row.Id, force: true, datasetId: second.Id);
        Assert.Equal(LoraModelStates.Ready, (await WaitAsync(row.Id)).Status);
    }

    [Fact]
    public async Task Limits_Larger_Than_The_Model_Declared_Need_A_Confirmation()
    {
        var projectDir = ProjectDir();
        // модель объявила кадр 768×512 и только PNG
        WriteTrainProfile(T2VId, MakeAdapterCommand(), minItems: 1,
            datasetExtra: """, "width": 768, "height": 512, "formats": ["png"]""");
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var dataset = _f.Objects.EnsureDataset(lora.Id,
            new LoraDatasetLimits { MaxWidth = 1024, MaxHeight = 1024, Format = "jpeg" }, null);
        AddFrame(project.Id, dataset.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        var check = _f.LoraTrain.Check(row.Id);
        Assert.Equal(3, check.Warnings.Count);   // ширина, высота и формат
        Assert.Equal("", check.Error);

        Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));
        _f.LoraTrain.Start(row.Id, force: false, confirmLimits: true);
        Assert.Equal(LoraModelStates.Ready, (await WaitAsync(row.Id)).Status);

        // подставили настройки из модели — и переспрашивать больше не о чем
        _f.Models.ReadProfileSettings(_f.Models.Get(T2VId)!);
        var model = _f.Models.Get(T2VId)!;
        _f.Models.ReadProfileSettings(model);
        _f.Objects.SetDatasetLimits(dataset.Id,
            LoraDatasetLimits.FromModel(model.Lora.Train.Dataset), null);
        Assert.Empty(_f.LoraTrain.Check(row.Id).Warnings);
    }

    [Fact]
    public async Task An_Object_Without_Datasets_Trains_By_Its_Own_Children()
    {
        // так датасет лежал до версии 1.98: объект, заведённый прежней версией и не открытый
        // с тех пор в редакторе, обязан обучаться по-прежнему — он приезжает и репликацией
        // с сервера, который ещё не обновили
        var projectDir = ProjectDir();
        WriteTrainProfile(T2VId, MakeAdapterCommand(), minItems: 1);
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        Assert.Single(_f.Objects.DatasetFrames(lora.Id));
        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitAsync(row.Id);
        Assert.Equal(LoraModelStates.Ready, done.Status);
        Assert.Equal("", done.DatasetId);
    }

    [Fact]
    public void The_Check_Names_The_Reason_Instead_Of_Falling()
    {
        // форме нужен ОТВЕТ, а не исключение: она спрашивает «о чём переспросить», а
        // «команда обучения не задана» — это не вопрос, это отказ, и показать его надо
        // так же, как раньше
        WriteTrainProfile(T2VId, command: "", minItems: 0);
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        var check = _f.LoraTrain.Check(row.Id);
        Assert.NotEqual("", check.Error);
        Assert.False(check.DatasetChanged);
    }

    // --- 5. Перенос старых кадров, схема и словари ---

    [Fact]
    public void The_Old_Frames_Move_Into_A_Dataset_Once()
    {
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        AddFrame(project.Id, lora.Id, "кадр2.png", "герой в профиль");
        // у ПЕРСОНАЖА прямые кадры — его собственные эталоны (T-259/T-258), их не трогаем
        var character = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "Вася",
            Type = ObjectKinds.Character,
        }, null);
        AddFrame(project.Id, character.Id, "вася.png", "герой");

        Assert.Equal(1, _f.Objects.MoveLegacyFramesToDatasets("датасет"));

        var datasets = _f.Objects.Datasets(lora.Id);
        var moved = Assert.Single(datasets);
        Assert.Equal(2, moved.ChildCount);
        Assert.Equal(moved.Id, _f.Objects.Get(lora.Id)!.CurrentDatasetId);
        Assert.DoesNotContain(_f.Objects.Children(lora.Id), c => c.Type == ObjectKinds.Image);
        // персонаж не тронут
        Assert.Empty(_f.Objects.Datasets(character.Id));
        Assert.Single(_f.Objects.Children(character.Id));

        // ИДЕМПОТЕНТНОСТЬ: повторный старт находить нечего
        Assert.Equal(0, _f.Objects.MoveLegacyFramesToDatasets("датасет"));
        Assert.Single(_f.Objects.Datasets(lora.Id));
    }

    [Fact]
    public void The_Prompt_Of_An_Object_Carries_The_Frames_Of_Its_Current_Dataset()
    {
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        AddFrame(project.Id, dataset.Id, "кадр1.png", "герой");

        // кадры уехали на уровень глубже, и без своего прохода подстановка осталась бы
        // вовсе без картинок
        var text = _f.Objects.Expand(project.Id, ObjectRefs.Marker(lora.DisplayId));
        Assert.Contains("lora-src/кадр1.png", text);
    }

    [Fact]
    public void The_Schema_Carries_The_New_Columns()
    {
        using var conn = _f.Db.Open();
        var objects = Sql.Query(conn, null, "PRAGMA table_info(objects)", r => r.S("name"));
        Assert.Contains("current_dataset_id", objects);
        Assert.Contains("dataset_json", objects);
        var loras = Sql.Query(conn, null, "PRAGMA table_info(object_loras)", r => r.S("name"));
        Assert.Contains("dataset_id", loras);
        // датасет и выбор текущего должны быть видны на соседнем сервере: обучение идёт там,
        // где лежат файлы, и «по какому датасету» партнёр обязан знать
        Assert.Contains("objects", ChangeLog.OrgTables);
    }

    [Fact]
    public void Every_New_Message_Has_A_Text_In_Both_Languages()
    {
        for (var i = 23; i <= 31; i++)
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Assert.NotEqual("msg.lora." + i, Loc.In(lang, "msg.lora." + i));
            }
        }
        foreach (var key in new[]
                 {
                     "lora.dataset.current", "lora.dataset.add", "lora.dataset.name",
                     "lora.dataset.changed", "lora.dataset.useCurrent", "lora.dataset.useTrained",
                     "lora.limits", "lora.limits.fromModel", "lora.limits.mismatch",
                     "lora.limits.trainAnyway", "common.ok", "common.saved",
                 })
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                Assert.NotEqual(key, Loc.In(lang, key));
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

    /// <summary>Кадр с настоящим файлом в папке проекта: обучение копирует файлы, а не записи.
    /// <paramref name="parentId"/> — датасет (а в проверке старых данных — сам объект).</summary>
    private ObjectItem AddFrame(string projectId, string parentId, string fileName, string caption)
    {
        var rel = "lora-src/" + fileName;
        var abs = Path.Combine(ProjectDir(), "lora-src", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, Png(16, 16));
        return _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = parentId[..4] + " " + Path.GetFileNameWithoutExtension(fileName),
            Type = ObjectKinds.Image,
            PathOrUrl = rel,
            Description = caption,
        }, null);
    }

    /// <summary>Профайл модели с настройкой обучения; <paramref name="datasetExtra"/> —
    /// контрольные настройки картинок, дописанные в блок датасета.</summary>
    private void WriteTrainProfile(string modelId, string command, int minItems,
        string datasetExtra = "")
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
                               "captions": "txt", "minItems": {{minItems}}{{datasetExtra}} },
                  "start": { "kind": "process", "command": "{{escaped}}", "steps": 10 },
                  "wait": { "kind": "process", "timeoutMinutes": 5 },
                  "result": { "kind": "file", "path": "lora/{object}/out/adapter.safetensors",
                              "target": "loras/{object}.safetensors" }
                }
              }
            }
            """);
    }

    /// <summary>Команда, которая делает вид, что обучила адаптер.</summary>
    private static string MakeAdapterCommand() => OperatingSystem.IsWindows()
        ? "mkdir \"{output}\" 2>nul & echo weights> \"{output}\\adapter.safetensors\""
        : "mkdir -p '{output}' && echo weights > '{output}/adapter.safetensors'";

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

    /// <summary>Наименьший настоящий PNG заданного размера.</summary>
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

    private static byte[] Be32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
