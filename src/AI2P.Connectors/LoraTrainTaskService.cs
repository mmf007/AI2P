using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ОБУЧЕНИЕ LoRA — ЭТО ЗАДАЧА (T-157-S0). Здесь всё, что стоит за кнопкой «Обучить» ПОСЛЕ
/// проверки датасета: поиск подходящего узла шаблонов, заведение исполнителя «авто ПО» и
/// самой задачи по выбранному шаблону.
///
/// <para>ПОЧЕМУ ЧЕРЕЗ ШАБЛОН, А НЕ ПРЯМЫМ СОЗДАНИЕМ ЗАДАЧИ. Заказчик: «заголовок, описание,
/// исполнителя и тэги задачи писать не надо». Всё это уже умеет
/// <see cref="TaskService.InstantiateTemplate"/>: он переносит в копию исполнителей, запасных
/// исполнителей, навыки, тэги, «статус при завершении» и секцию оптимизации, а опыт узла
/// достаётся созданной задаче сам. Придумывать второй способ создания задачи ради одной кнопки
/// значило бы завести второй набор правил, который разъедется с первым при первой же правке.</para>
///
/// <para>ПОЧЕМУ У ЗАДАЧИ НЕТ СВОИХ ПОЛЕЙ С ПАРАМЕТРАМИ. Связь хранится с ДРУГОЙ стороны:
/// <see cref="ObjectLoraModel.TrainTaskId"/> (он нужен всё равно — ради ссылки на задачу), и
/// коннектор по идентификатору задачи находит строку обучения обратным поиском
/// (<see cref="ObjectService.LoraModelByTrainTask"/>). В строке уже есть объект, датасет,
/// модель и настройка <c>lora.train</c> профайла — то есть ВСЁ. Ни скрытых полей, ни разбора
/// текста описания не понадобилось.</para>
///
/// <para>ПЛАГИН-ТРЕНЕР ИЩЕТСЯ ПО ПАКЕТАМ — тем же правилом, что и у заведения записи при
/// установке модели (<see cref="TrainerPluginService.Covers"/>): годится плагин, который ведёт
/// ко ВСЕМ пакетам <c>lora.train.packages</c> модели и объявляет блок долгого запуска. Правило
/// одно на обе стороны намеренно: два разных правила отбора «того самого плагина» разъехались
/// бы молча, и человек получил бы исполнителя, которым обучать нельзя.</para>
/// </summary>
public sealed class LoraTrainTaskService
{
    private static readonly ILogger Logger = Log.ForContext<LoraTrainTaskService>();

    private readonly ObjectService _objects;
    private readonly AiModelService _models;
    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly PluginService _plugins;
    private readonly FileStore _files;

    public LoraTrainTaskService(ObjectService objects, AiModelService models, TaskService tasks,
        ExecutorService executors, PluginService plugins, FileStore files)
    {
        _objects = objects;
        _models = models;
        _tasks = tasks;
        _executors = executors;
        _plugins = plugins;
        _files = files;
    }

    /// <summary>Язык, на котором берутся названия из манифеста плагина; null — язык установки.</summary>
    public Func<string>? Language { get; set; }

    /// <summary>
    /// ЖИВЫЕ ПЛАГИНЫ ЭТОГО СЕРВЕРА (<c>OrgContext.LiveGateways</c>): манифест, годная запись
    /// организации и найденная ЗДЕСЬ программа. По ним считается «плагин готов»: без программы
    /// задача встала бы в очередь и не тронулась с места, а человек не знал бы почему.
    /// </summary>
    public Func<IReadOnlyList<GatewayPlugin>>? Gateways { get; set; }

    /// <summary>Запустить созданную задачу (ответ «создать и запустить») —
    /// <c>JobOrchestrator.StartTaskAsync</c>. Не задан — задача только создаётся.</summary>
    public Func<string, string?, Task>? StartTask { get; set; }

    /// <summary>О чём переспросить перед обучением (<c>LoraTrainService.Check</c>, T-274).
    /// Спрашивает форма, а ТРЕБУЕТ подтверждений сервер — здесь, при создании задачи: обойти
    /// переспрос, дёрнув создание напрямую, нельзя было и раньше.</summary>
    public Func<string, string?, LoraTrainCheck>? CheckTrain { get; set; }

    // --- что вообще можно предложить ---

    /// <summary>
    /// ЧТО ПОКАЗАТЬ ЧЕЛОВЕКУ ПОСЛЕ НАЖАТИЯ «ОБУЧИТЬ»: годные шаблоны, подходящий исполнитель и
    /// плагин-тренер. Всё три сразу — форма по одному ответу решает, что показать: список
    /// шаблонов, предложение завести шаблон, предложение завести исполнителя или окно установки
    /// плагина. Отказы (модель не работает с LoRA, тренера нет) приходят ПОЛЕМ, а не
    /// исключением: это ответ на вопрос «что дальше», а не поломка.
    /// </summary>
    public LoraTrainOptionsDto Options(string loraId)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var answer = new LoraTrainOptionsDto();
        var model = _models.Get(row.ModelId);
        if (model is null)
        {
            answer.Error = Loc.T("msg.object.11");
            return answer;
        }
        _models.ReadProfileSettings(model);
        if (!model.Lora.Supported)
        {
            answer.Error = Loc.T("msg.lora.5", model.Name);
            return answer;
        }
        var manifest = TrainerOf(model);
        if (manifest is null)
        {
            answer.Error = Loc.T("msg.lora.36", model.Name);
            return answer;
        }
        answer.PluginCode = manifest.Code;
        answer.PluginName = manifest.Name.Text(Language?.Invoke());
        answer.PluginReady = Live(manifest.Code) is not null;

        // подходящий исполнитель — «авто ПО», который работает ЭТИМ плагином. Чужой плагин не
        // годится: у него другая программа и другие операции
        var executor = _executors.List()
            .FirstOrDefault(e => e.Kind == ExecutorKind.Software && e.IsActive
                                 && string.Equals(e.PluginCode, manifest.Code,
                                     StringComparison.OrdinalIgnoreCase));
        if (executor is not null)
        {
            answer.ExecutorId = executor.Id;
            answer.ExecutorNick = executor.Nick;
        }

        // ГОДНЫЙ ШАБЛОН — тот, у кого в исполнителях (или запасных) стоит «авто ПО» с этим
        // плагином. Проект узла: свой проекту объекта либо ничей (общий шаблон организации —
        // он годится любому проекту)
        var item = _objects.Get(row.ObjectId);
        var byPlugin = _executors.List()
            .Where(e => e.Kind == ExecutorKind.Software
                        && string.Equals(e.PluginCode, manifest.Code, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Id, e => e.Nick, StringComparer.Ordinal);
        foreach (var node in _tasks.List(templatesOnly: true))
        {
            if (node.ProjectId is { Length: > 0 } nodeProject
                && item?.ProjectId is { Length: > 0 } objectProject
                && !string.Equals(nodeProject, objectProject, StringComparison.Ordinal))
            {
                continue;
            }
            var nick = node.ExecutorIds.Concat(node.AltExecutorIds)
                .Select(id => byPlugin.GetValueOrDefault(id))
                .FirstOrDefault(n => n is { Length: > 0 });
            if (nick is null)
            {
                continue;
            }
            answer.Templates.Add(new LoraTrainTemplateDto
            {
                Id = node.Id,
                DisplayId = node.DisplayId,
                Title = node.Title,
                ExecutorNick = nick,
            });
        }
        return answer;
    }

    // --- заведение недостающего ---

    /// <summary>
    /// ЗАВЕСТИ ИСПОЛНИТЕЛЯ «АВТО ПО» из записи «Плагинов и MCP» — то самое «предложить создать,
    /// подставив запись из списка», которого просил заказчик. Запись плагина заводится заодно,
    /// если её ещё нет: манифест на диске без записи организации — это строка, на которую
    /// сослаться нельзя (T-156-S0).
    ///
    /// <para>Операция выбирается САМА и осмысленно: та, что помечена «один экземпляр» (у тренера
    /// это обучение — видеокарта одна), иначе та, чьё имя говорит про обучение, иначе первая с
    /// объявленным долгим запуском. Выбирать её человеку значило бы спрашивать про внутренности
    /// манифеста, которых он не видел.</para>
    /// </summary>
    public Executor CreateExecutor(string loraId, string? actorId)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var model = _models.Get(row.ModelId) ?? throw new ArgumentException(Loc.T("msg.object.11"));
        _models.ReadProfileSettings(model);
        var manifest = TrainerOf(model)
                       ?? throw new ArgumentException(Loc.T("msg.lora.36", model.Name));
        _plugins.Declare(manifest, actorId, Language?.Invoke());
        var action = TrainActionOf(manifest)
                     ?? throw new ArgumentException(Loc.T("msg.lora.37", manifest.Code));
        var lang = Language?.Invoke();
        var executor = new Executor
        {
            // ник виден в списках исполнителей и в дорожке диаграммы: он обязан говорить, ЧТО
            // это за работник, — «musubi-tuner: обучение LoRA», а не «PLG-3»
            Nick = Nick(manifest.Name.Text(lang), action.Title.Text(lang), manifest.Code),
            Kind = ExecutorKind.Software,
            PluginCode = manifest.Code,
            PluginOp = action.Tool,
            IsActive = true,
        };
        var created = _executors.Create(executor, actorId);
        Logger.Information("Исполнитель «авто ПО» {Nick} заведён под обучение LoRA моделью {Model}",
            created.Nick, model.Name);
        return created;
    }

    /// <summary>
    /// ЗАВЕСТИ УЗЕЛ ШАБЛОНА с уже подставленным исполнителем — второе предложение кнопки
    /// («исполнитель есть, шаблона нет»). Узел корневой и лежит в проекте объекта: из него
    /// потом приезжают заголовок, описание, исполнитель и тэги КАЖДОЙ созданной задачи, и
    /// править их человек будет в одном месте, а не в каждой задаче.
    /// </summary>
    public TaskItem CreateTemplate(string loraId, string executorId, string? actorId)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var item = _objects.Get(row.ObjectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        var executor = _executors.Get(executorId);
        if (executor is null || executor.Kind != ExecutorKind.Software)
        {
            throw new ArgumentException(Loc.T("msg.lora.38"));
        }
        var node = new TaskItem
        {
            ProjectId = item.ProjectId,
            Title = Loc.T("msg.lora.39"),
            IsTemplate = true,
            Status = TaskStatuses.Draft,
            ExecutorIds = [executor.Id],
            // проверять результат обучения человеку незачем: удача видна файлом адаптера,
            // неудача — ошибкой строки обучения и артефактом задания
            AiDoneStatus = TaskStatuses.Done,
        };
        return _tasks.Create(node, Loc.T("msg.lora.40"), Loc.T("msg.lora.41"), actorId);
    }

    // --- сама задача ---

    /// <summary>
    /// СОЗДАТЬ ЗАДАЧУ ОБУЧЕНИЯ по выбранному шаблону и (по ответу «создать и запустить»)
    /// поставить её в работу.
    ///
    /// <para>Порядок важен: сперва копия шаблона, потом ссылка на неё в строке обучения и
    /// только потом запуск. Запусти мы раньше — коннектор, найдя задачу быстрее, чем в строке
    /// появится <see cref="ObjectLoraModel.TrainTaskId"/>, не узнал бы обратным поиском, что
    /// обучать, и упал бы на исправном продукте.</para>
    ///
    /// <para>НЕУДАЧА ЗАПУСКА НЕ ОТМЕНЯЕТ СОЗДАНИЕ: занятая программа, чужой сервер и правило
    /// безопасности — это «задача подождёт», а не «работы не было». Причина возвращается полем,
    /// и человек видит её в том же ответе.</para>
    /// </summary>
    public async Task<LoraTrainTaskDto> CreateTaskAsync(string loraId, string templateId,
        LoraTrainTaskCreateDto wanted, string? actorId)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        var datasetId = wanted.DatasetId;
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var item = _objects.Get(row.ObjectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        _objects.EnsureCanWrite(item);      // обучение чужого объекта заводится на его сервере
        if (row.Status == LoraModelStates.Training)
        {
            throw new ArgumentException(Loc.T("msg.lora.2"));
        }
        if (row.Status == LoraModelStates.Ready && !wanted.Force)
        {
            throw new ArgumentException(Loc.T("msg.lora.3"));
        }
        // ПЕРЕСПРОСЫ T-274 ТРЕБУЮТСЯ ЗДЕСЬ — там же, где раньше их требовал прямой запуск:
        // задачу заводит не только форма, и «обучение молча взяло другой датасет» одинаково
        // непочинимо, каким бы путём оно ни началось
        if (CheckTrain is { } ask)
        {
            var check = ask(loraId, datasetId);
            if (check.DatasetChanged && !wanted.ConfirmDataset)
            {
                throw new ArgumentException(Loc.T("msg.lora.30", check.TrainedDatasetName,
                    check.DatasetName));
            }
            if (check.Warnings.Count > 0 && !wanted.ConfirmLimits)
            {
                throw new ArgumentException(Loc.T("msg.lora.31", string.Join("; ", check.Warnings)));
            }
        }
        // ВЫБРАННЫЙ ДАТАСЕТ СТАНОВИТСЯ ТЕКУЩИМ: задача может запуститься через сутки, и своего
        // поля с датасетом у неё намеренно нет — обучение возьмёт текущий на момент запуска
        if (datasetId is { Length: > 0 } picked && picked != item.CurrentDatasetId)
        {
            _objects.SetCurrentDataset(item.Id, picked, actorId);
        }
        var head = _tasks.InstantiateTemplate(templateId, baseDate: null, actorId);
        _objects.SetLoraTrainTask(loraId, head.Id);
        Logger.Information("Обучение {Lora} ведёт задача {Task}", loraId, head.DisplayId);

        var answer = new LoraTrainTaskDto
        {
            TaskId = head.Id,
            DisplayId = head.DisplayId,
            Title = head.Title,
        };
        if (!wanted.Run)
        {
            return answer;
        }
        if (StartTask is not { } start)
        {
            answer.StartError = Loc.T("msg.lora.42");
            return answer;
        }
        try
        {
            await start(head.Id, actorId);
            answer.Started = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            answer.StartError = ex.Message;
        }
        return answer;
    }

    // --- общее ---

    /// <summary>Плагин-тренер этой модели: манифест, ведущий ко ВСЕМ пакетам обучения и
    /// объявляющий долгий запуск. Правило общее с <see cref="TrainerPluginService"/>.</summary>
    private PluginManifest? TrainerOf(AiModel model)
    {
        var packages = model.Lora.Train.Packages;
        return packages.Count == 0
            ? null
            : PluginManifest.ReadAll(_files.DataDir)
                .FirstOrDefault(p => TrainerPluginService.Covers(p, packages));
    }

    private GatewayPlugin? Live(string code) =>
        Gateways?.Invoke().FirstOrDefault(p =>
            string.Equals(p.Manifest.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Действие плагина, которым обучают: помеченное «один экземпляр» (у тренера это
    /// обучение — видеокарта одна), иначе названное обучением, иначе первое с долгим запуском.</summary>
    private static PluginAction? TrainActionOf(PluginManifest manifest)
    {
        var withRun = manifest.Actions
            .Where(a => a.Op.Length > 0 && manifest.Run?.Find(a.Op) is not null)
            .ToList();
        if (withRun.Count == 0)
        {
            return null;
        }
        return withRun.FirstOrDefault(a => a.SingleInstance
                                           || manifest.Run?.Find(a.Op) is { SingleInstance: true })
               ?? withRun.FirstOrDefault(a =>
                   a.Op.Contains("train", StringComparison.OrdinalIgnoreCase))
               ?? withRun[0];
    }

    /// <summary>Ник исполнителя: «<плагин>: <действие>». Занятый ник — отказ уникальности,
    /// поэтому к повторному добавляется номер: человек нажал «создать» дважды, и отказ формы
    /// вместо исполнителя выглядел бы поломкой кнопки.</summary>
    private string Nick(string plugin, string action, string code)
    {
        var basis = (plugin.Length > 0 ? plugin : code)
                    + (action.Length > 0 ? ": " + action : "");
        var taken = _executors.List(includeDeleted: true)
            .Select(e => e.Nick)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(basis))
        {
            return basis;
        }
        for (var n = 2; n < 100; n++)
        {
            var candidate = basis + " " + n;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
        return basis + " " + Guid.NewGuid().ToString("N")[..6];
    }
}
