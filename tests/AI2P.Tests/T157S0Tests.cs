using System.Text;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-157-S0 — ОБУЧЕНИЕ LoRA ИДЁТ ЗАДАЧЕЙ.
///
/// Что проверяется:
/// <list type="number">
/// <item>ССЫЛКА НА ЗАДАЧУ: колонка <c>object_loras.train_task_id</c> есть, пишется, читается
/// и — главное — по ней работает ОБРАТНЫЙ поиск, которым коннектор узнаёт, что обучать. Ровно
/// из-за него у задачи нет и не нужно ни одного своего поля с параметрами;</item>
/// <item>ЧЕМ ЗАПУСКАТЬ (<c>Options</c>): пока нет ни исполнителя, ни шаблона — ответ говорит об
/// этом полями, а не отказом; заведённый исполнитель и заведённый по нему узел шаблона в ответе
/// появляются;</item>
/// <item>СОЗДАНИЕ ЗАДАЧИ ПО ШАБЛОНУ: заголовок, исполнитель и тэги приезжают из узла, строка
/// обучения помнит задачу, «только создать» её не запускает;</item>
/// <item>ОБУЧЕНИЕ КАК ЗАДАНИЕ (<c>RunForTaskAsync</c>): доводит дело до конца и отдаёт путь
/// адаптера, а неудача поднимается ИСКЛЮЧЕНИЕМ (иначе задание сдалось бы «выполненным») и
/// одновременно ложится в поле <c>Error</c> строки — ошибка обязана быть видна в обоих местах.</item>
/// </list>
/// </summary>
public sealed class T157S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Kandinsky T2V из дистрибутива — запись, у которой работа с LoRA объявлена.</summary>
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    // ---------- 1. Ссылка на задачу и обратный поиск ----------

    [Fact]
    public void The_Training_Row_Remembers_Its_Task_And_Is_Found_Back_By_It()
    {
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        _f.Models.Seed();
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        // у нового обучения задачи нет: поле пустое, обратный поиск ничего не находит
        Assert.Equal("", row.TrainTaskId);
        Assert.Null(_f.Objects.LoraModelByTrainTask("нет-такой-задачи"));

        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Обучение" },
            "", "", null);
        // связь «идентификатор задачи → её номер» ставит сервер (OrgContext), как ModelResolver:
        // задачи — не дело сервиса объектов
        _f.Objects.TaskResolver = id => _f.Tasks.Get(id)?.DisplayId;
        var linked = _f.Objects.SetLoraTrainTask(row.Id, task.Id)!;

        Assert.Equal(task.Id, linked.TrainTaskId);
        // номер задачи — надпись ссылки; без него в списке был бы голый uuid
        Assert.Equal(task.DisplayId, linked.TrainTaskDisplayId);
        // ОБРАТНЫЙ ПОИСК — то, чем коннектор узнаёт, что за работу ему дали
        Assert.Equal(row.Id, _f.Objects.LoraModelByTrainTask(task.Id)!.Id);
    }

    [Fact]
    public void The_Task_Link_Survives_A_State_Change()
    {
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        _f.Models.Seed();
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);
        var task = _f.Tasks.Create(new TaskItem { ProjectId = project.Id, Title = "Обучение" },
            "", "", null);
        _f.Objects.SetLoraTrainTask(row.Id, task.Id);

        // состояние обучения пишется фоном по дороге, и затирать им ссылку нельзя: к упавшему
        // обучению человек приходит через сутки именно за логом задачи
        var training = _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Training)!;
        Assert.Equal(task.Id, training.TrainTaskId);
        var failed = _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Error, null, "беда")!;
        Assert.Equal(task.Id, failed.TrainTaskId);
    }

    // ---------- 2. Чем запускать ----------

    [Fact]
    public void Without_An_Executor_The_Answer_Says_So_Instead_Of_Refusing()
    {
        var (row, _) = Prepared();

        var options = Service().Options(row.Id);

        // плагин-тренер в дистрибутиве есть, а исполнителя и шаблона ещё нет — и это не отказ,
        // а ответ на вопрос «что дальше»: форма по нему предложит их завести
        Assert.Equal("", options.Error);
        Assert.Equal(PluginSeed.TrainerMusubiCode, options.PluginCode);
        Assert.Equal("", options.ExecutorId);
        Assert.Empty(options.Templates);
    }

    [Fact]
    public void The_Executor_Comes_From_The_Plugin_Record_With_The_Training_Operation()
    {
        var (row, _) = Prepared();

        var executor = Service().CreateExecutor(row.Id, null);

        Assert.Equal(ExecutorKind.Software, executor.Kind);
        Assert.Equal(PluginSeed.TrainerMusubiCode, executor.PluginCode);
        // операция выбрана САМА и осмысленно: та, что помечена «один экземпляр», — обучение
        Assert.Equal("musubi_lora_train", executor.PluginOp);
        // запись плагина заведена заодно: манифест без записи — строка, на которую не сослаться
        Assert.NotNull(new PluginService(_f.Db, _f.Events).ByCode(PluginSeed.TrainerMusubiCode));

        var options = Service().Options(row.Id);
        Assert.Equal(executor.Id, options.ExecutorId);
    }

    [Fact]
    public void A_Template_With_That_Executor_Becomes_The_Offered_One()
    {
        var (row, _) = Prepared();
        var service = Service();
        var executor = service.CreateExecutor(row.Id, null);

        var node = service.CreateTemplate(row.Id, executor.Id, null);

        Assert.True(node.IsTemplate);
        Assert.Contains(executor.Id, node.ExecutorIds);
        var offered = Assert.Single(service.Options(row.Id).Templates);
        Assert.Equal(node.Id, offered.Id);
        Assert.Equal(executor.Nick, offered.ExecutorNick);
    }

    // ---------- 3. Создание задачи по шаблону ----------

    [Fact]
    public async Task The_Task_Takes_Its_Title_Executor_And_Tags_From_The_Template()
    {
        var (row, _) = Prepared();
        var service = Service();
        var executor = service.CreateExecutor(row.Id, null);
        var node = service.CreateTemplate(row.Id, executor.Id, null);
        node.Tags = ["обучение"];
        _f.Tasks.Update(node, "описание узла", "приёмка узла", null);

        var created = await service.CreateTaskAsync(row.Id, node.Id,
            new LoraTrainTaskCreateDto { TemplateId = node.Id, Run = false }, null);

        // «только создать» — задача есть, но её никто не запускал
        Assert.False(created.Started);
        var task = _f.Tasks.Get(created.TaskId)!;
        Assert.False(task.IsTemplate);
        Assert.Equal(TaskStatuses.Pending, task.Status);
        // заголовок, исполнитель и тэги человек не писал — они приехали из узла
        Assert.Equal(node.Title, task.Title);
        Assert.Contains(executor.Id, task.ExecutorIds);
        Assert.Contains("обучение", task.Tags);
        Assert.Equal("описание узла", _f.Tasks.ReadDescription(task));

        // и строка обучения теперь знает свою задачу — в обе стороны
        Assert.Equal(created.TaskId, _f.Objects.LoraModel(row.Id)!.TrainTaskId);
        Assert.Equal(row.Id, _f.Objects.LoraModelByTrainTask(created.TaskId)!.Id);
    }

    [Fact]
    public async Task A_Second_Training_Is_Refused_While_The_First_One_Runs()
    {
        var (row, _) = Prepared();
        var service = Service();
        var executor = service.CreateExecutor(row.Id, null);
        var node = service.CreateTemplate(row.Id, executor.Id, null);
        _f.Objects.SetLoraModelState(row.Id, LoraModelStates.Training);

        // сторож «второй раз то же самое» стоит здесь, а не только у флага плагина: задача
        // создавалась бы молча, и обучений оказалось бы два
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTaskAsync(row.Id, node.Id,
            new LoraTrainTaskCreateDto { TemplateId = node.Id }, null));
    }

    // ---------- 4. Обучение как задание ----------

    [Fact]
    public async Task The_Job_Run_Trains_The_Adapter_And_Returns_Its_File()
    {
        var (row, lora) = Prepared(MakeAdapterCommand());

        var file = await _f.LoraTrain.RunForTaskAsync(row.Id, "job-1", CancellationToken.None);

        Assert.Equal("loras/" + lora.DisplayId + ".safetensors", file);
        var done = _f.Objects.LoraModel(row.Id)!;
        Assert.Equal(LoraModelStates.Ready, done.Status);
        Assert.Equal(file, done.Path);
        // и адаптер стал ТЕКУЩИМ у объекта: именно его подставляет генерация
        Assert.Equal(file, _f.Objects.Get(lora.Id)!.LoraPath);
        Assert.True(File.Exists(Path.Combine(_f.ModelsRepo, "loras", lora.DisplayId + ".safetensors")));
    }

    [Fact]
    public async Task A_Failed_Job_Run_Throws_And_Writes_The_Error_Into_The_Row()
    {
        var (row, _) = Prepared("ai2p-нет-такой-команды-157s0");

        // ИСКЛЮЧЕНИЕ ОБЯЗАТЕЛЬНО: без него задание сдалось бы «выполненным» при несостоявшемся
        // обучении, и артефакта ошибки у задачи не появилось бы вовсе
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            _f.LoraTrain.RunForTaskAsync(row.Id, "job-2", CancellationToken.None));
        Assert.NotEqual("", error.Message);

        // и та же причина — в строке списка: три места вместо одного, и это третье
        var failed = _f.Objects.LoraModel(row.Id)!;
        Assert.Equal(LoraModelStates.Error, failed.Status);
        Assert.NotEqual("", failed.Error);
    }

    // ---------- общее ----------

    private LoraTrainTaskService Service()
    {
        PluginSeed.Write(_f.Files);
        return new LoraTrainTaskService(_f.Objects, _f.Models, _f.Tasks, _f.Executors,
            new PluginService(_f.Db, _f.Events), _f.Files)
        {
            CheckTrain = (loraId, datasetId) => _f.LoraTrain.Check(loraId, datasetId),
        };
    }

    /// <summary>Объект-адаптер с кадром, профайлом обучения и заведённой строкой обучения.</summary>
    private (ObjectLoraModel Row, ObjectItem Lora) Prepared(string? command = null)
    {
        var project = _f.Projects.Create("Ролик", ProjectDir(), null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        WriteTrainProfile(command ?? MakeAdapterCommand());
        var dataset = _f.Objects.EnsureDataset(lora.Id, new LoraDatasetLimits(), null);
        AddFrame(project.Id, dataset.Id, "кадр1.png", "герой");
        return (_f.Objects.AddLoraModel(lora.Id, T2VId, null), lora);
    }

    private string ProjectDir()
    {
        var dir = Path.Combine(_f.Dir, "project");
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

    private void WriteTrainProfile(string command)
    {
        _f.Models.Seed();
        var model = _f.Models.Get(T2VId)!;
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
                  "packages": ["musubi-tuner", "python"],
                  "dataset": { "kind": "dir", "path": "lora/{object}/dataset",
                               "captions": "txt", "minItems": 1 },
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
