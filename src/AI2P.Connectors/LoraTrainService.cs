using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// ОБУЧЕНИЕ АДАПТЕРА LoRA (T-12-S1, версия 1.95) — исполнитель того, что справочник моделей
/// объявил настройкой <c>lora.train</c> (T-13-S1).
///
/// Здесь нет НИ ОДНОГО решения о том, «как обучать»: все пять шагов — где собрать датасет,
/// чем запустить, как дождаться, откуда забрать файл и куда его положить — приходят из
/// профайла модели. Сервис только исполняет их по порядку и записывает, чем кончилось.
/// Так и должно быть: обучающие скрипты у каждой модели свои и меняются чаще, чем наш код,
/// а вписанный в код путь пришлось бы править выпуском версии.
///
/// ЧТО СЧИТАЕТСЯ ОШИБКОЙ И ПОЧЕМУ ЭТО ВАЖНО: любая неудача — незаполненная команда,
/// маленький датасет, ненулевой код возврата, оборванный ответ провайдера — кладётся
/// текстом в строку обучения (<see cref="ObjectLoraModel.Error"/>) и показывается
/// подсказкой на слове «ошибка». Молчаливое «не получилось» здесь недопустимо: обучение
/// идёт часами, и человек возвращается к списку через сутки — по одному слову «ошибка»
/// чинить нечего.
///
/// КУДА ВЕДУТ ОТНОСИТЕЛЬНЫЕ ПУТИ настройки (правило одно на весь сервис):
/// <list type="bullet">
/// <item><c>train.dataset.path</c> и <c>train.result.path</c> — от ПАПКИ ПРОЕКТА: это
/// рабочие данные обучения конкретного персонажа конкретного проекта;</item>
/// <item><c>train.result.target</c> — от РЕПОЗИТОРИЯ МОДЕЛЕЙ: готовый адаптер кладётся
/// туда, откуда его берёт сам движок модели (у ComfyUI это <c>&lt;репозиторий&gt;/loras</c>).</item>
/// </list>
/// Абсолютный путь в настройке используется как есть — он и написан затем, чтобы увести
/// обучение на другой диск.
/// </summary>
public sealed class LoraTrainService
{
    private readonly ObjectService _objects;
    private readonly AiModelService _models;
    private readonly ProjectService _projects;
    private readonly Func<string> _modelsRepo;
    private readonly HttpClient _http;

    /// <summary>Каталог данных организации: кадры датасета лежат в нём (T-98-S0), а не в
    /// папке проекта. Может быть null в тестах, которые обучение не запускают.</summary>
    private readonly FileStore? _files;

    /// <summary>Живой вывод обучения (T-152-S0); null — вывод идёт только в файл журнала.</summary>
    private readonly JobConsole? _console;

    /// <summary>Идущие прямо сейчас обучения — чтобы второй раз не запустить то же самое
    /// и чтобы остановка приложения не ждала их вечно.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    public LoraTrainService(ObjectService objects, AiModelService models, ProjectService projects,
        Func<string> modelsRepo, HttpClient? http = null, FileStore? files = null,
        JobConsole? console = null)
    {
        _objects = objects;
        _models = models;
        _projects = projects;
        _modelsRepo = modelsRepo;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _files = files;
        _console = console;
    }

    /// <summary>
    /// ПУТИ УСТАНОВКИ модели в подстановках настройки обучения (T-289): {modelsRepo},
    /// {groupDir}, {model:&lt;файл&gt;}, {package:&lt;код&gt;:&lt;файл&gt;} — то же, что у команды
    /// запуска локального сервера модели (ModelInstallService.ExpandPaths). Обучающему
    /// скрипту нужны ровно те же файлы, что и генерации: веса DiT, VAE, каталог пакета
    /// тренера — вписывать их в профайл руками значило бы держать пути в двух местах.
    ///
    /// Связь делегатом, а не зависимостью, потому что установщик моделей заводится ПОЗЖЕ
    /// этого сервиса (как ObjectService.ModelResolver). Не задан — подстановки остаются
    /// в тексте как есть, и обучение честно упадёт с ними в команде.
    /// </summary>
    public Func<AiModel, string, string>? ModelPaths { get; set; }

    /// <summary>
    /// ЧЕГО НЕ ХВАТАЕТ ДЛЯ ОБУЧЕНИЯ (T-4-S0): по списку кодов пакетов
    /// (<c>lora.train.packages</c>) — названия тех, что на этом сервере не установлены.
    ///
    /// Раньше (T-289) обучение ставило их само, первым своим шагом. Теперь пакеты приходят
    /// вместе с моделью — при её установке, где человек согласился ждать и где есть окно с
    /// ходом работы, — а обучение только СПРАШИВАЕТ. Разница видна на первом же нажатии
    /// кнопки: раньше оно молча уходило качать десятки мегабайт, теперь начинается сразу.
    ///
    /// Связь делегатом по той же причине, что и <see cref="ModelPaths"/>: установщик
    /// заводится позже. Не задан — считаем, что спросить некого, и обучение идёт как шло.
    /// </summary>
    public Func<IReadOnlyList<string>, IReadOnlyList<string>>? MissingPackages { get; set; }

    /// <summary>
    /// Сколько ждать конца обучения, когда в настройке не сказано. Обучение LoRA идёт часами,
    /// поэтому умолчание крупное — но не бесконечное: зависший процесс обязан когда-нибудь
    /// стать ошибкой, а не остаться навсегда в состоянии «обучается».
    /// </summary>
    public const int DefaultTimeoutMinutes = 720;

    /// <summary>
    /// СКОЛЬКО ТЕРПЕТЬ МОЛЧАНИЕ (T-152-S0), когда в настройке не сказано. Пока обучение
    /// печатает — оно живо, и общий предел выше про «считает слишком долго». Молчание —
    /// про другое: программа зависла либо ждёт чего-то, чего не дождётся, и ждать этого
    /// половину суток бессмысленно. Час взят с запасом на самый долгий молчаливый шаг
    /// тренера — ПЕРВУЮ закачку исходных кодировщиков с HuggingFace (Qwen2.5-VL-7B-Instruct,
    /// пять файлов на ~16 ГБ, наружу печатается только внешняя полоска «Fetching 5 files»):
    /// получасового предела на неё не хватало, и живая закачка резалась как зависание
    /// (T-313). Кому мало, ставит своё (<c>lora.train.wait.idleMinutes</c> в профайле модели).
    /// </summary>
    public const int DefaultIdleMinutes = 60;

    /// <summary>Ключ буфера консоли задания, под которым идёт вывод обучения: своего задания
    /// AI2P у обучения пока нет (оно станет задачей в T-157-S0), а вывод нужен уже сейчас.</summary>
    public static string ConsoleKey(string loraId) => "lora:" + loraId;

    /// <summary>
    /// Обучение сейчас идёт (по памяти процесса). Строка в БД переживает перезапуск, а этот
    /// список — нет, и это правильно: после перезапуска процесс обучения уже не наш.
    /// </summary>
    public bool IsRunning(string loraId) => _running.ContainsKey(loraId);

    /// <summary>
    /// ОБУЧЕНИЕ КАК ЗАДАНИЕ ЗАДАЧИ (T-157-S0) — единственный путь, которым обучение
    /// запускается человеком с версии 1.118. Зовёт коннектор «авто ПО»
    /// (<see cref="SoftwareConnector"/>), узнав по задаче свою строку обучения обратным
    /// поиском, и ЖДЁТ конца: жизнь задания обязана совпадать с жизнью тренера, иначе задача
    /// сдалась бы через секунду после запуска, а работа шла бы ещё двенадцать часов.
    ///
    /// <para>Отличий от прежнего фонового запуска ровно два, и оба ради задачи: вывод идёт в
    /// консоль ЗАДАНИЯ (ключ буфера — идентификатор задания, а не «lora:…»), а неудача не
    /// глохнет в поле <see cref="ObjectLoraModel.Error"/>, а поднимается исключением — из него
    /// коннектор делает артефакт ошибки задания. Поле <c>Error</c> при этом заполняется тоже:
    /// ошибка обязана быть видна в трёх местах — в консоли живьём, в артефакте навсегда и в
    /// строке списка.</para>
    ///
    /// <para>ПО КАКОМУ ДАТАСЕТУ: по ТЕКУЩЕМУ датасету объекта на момент запуска. Выбор человека,
    /// сделанный у кнопки «Обучить», доезжает сюда тем, что выбранный датасет становится
    /// текущим (<c>ObjectService.SetCurrentDataset</c>) — а не отдельным полем задачи: задача
    /// может запуститься через сутки, и запомненный в ней датасет к тому времени мог быть уже
    /// удалён.</para>
    /// </summary>
    /// <param name="consoleKey">Ключ буфера живого вывода — идентификатор задания.</param>
    /// <returns>Путь готового адаптера (относительный — от репозитория моделей).</returns>
    public async Task<string> RunForTaskAsync(string loraId, string consoleKey,
        CancellationToken token)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var plan = Plan(row, null);
        _objects.SetLoraModelState(loraId, LoraModelStates.Training, row.Path, "");
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_running)
        {
            // тот же сторож, что у прямого запуска: «Остановить» в списке моделей объекта
            // гасит идущее обучение и тогда, когда его завела задача
            _running[loraId] = cts;
        }
        try
        {
            return await RunAsync(loraId, plan, cts.Token, consoleKey, rethrow: true);
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// ЗАПУСТИТЬ ОБУЧЕНИЕ ФОНОМ — ДВИЖОК, а не путь человека. С T-157-S0 человек обучение
    /// так не запускает: кнопка «Обучить» заводит ЗАДАЧУ, и её задание зовёт
    /// <see cref="RunForTaskAsync"/>. Два пути к одной работе разъезжаются молча, поэтому
    /// снаружи (из API и из формы) сюда не ходит никто.
    ///
    /// Возвращает строку в состоянии «обучается» СРАЗУ: обучение идёт часами, и держать на нём
    /// запрос браузера нельзя — состояние человек видит в списке.
    ///
    /// <paramref name="force"/> — ответ «да» на переспрос «переобучить?»: уже обученную
    /// модель без него не трогаем, потому что переобучение затирает готовый файл адаптера,
    /// который мог быть уже подставлен в модель.
    /// </summary>
    /// <param name="datasetId">По какому датасету обучать (T-274); пусто — по текущему
    /// датасету объекта.</param>
    /// <param name="confirmDataset">Ответ «да» на переспрос «датасет не тот, по которому
    /// обучали в прошлый раз».</param>
    /// <param name="confirmLimits">Ответ «да» на переспрос «настройки датасета крупнее
    /// того, что объявила модель».</param>
    public ObjectLoraModel Start(string loraId, bool force, string? datasetId = null,
        bool confirmDataset = false, bool confirmLimits = false)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        if (row.Status == LoraModelStates.Training)
        {
            throw new ArgumentException(Loc.T("msg.lora.2"));
        }
        if (row.Status == LoraModelStates.Ready && !force)
        {
            throw new ArgumentException(Loc.T("msg.lora.3"));
        }
        var plan = Plan(row, datasetId);
        // ПЕРЕСПРОСЫ (T-274). Оба — про то, что обучение молча сделает не то, чего ждут:
        // возьмёт другой датасет либо кадры, которых модель не примет. Спрашивает их
        // форма, а проверяет сервер: обучение запускается и не из формы тоже
        var check = Check(row, plan);
        if (check.DatasetChanged && !confirmDataset)
        {
            throw new ArgumentException(Loc.T("msg.lora.30", check.TrainedDatasetName,
                check.DatasetName));
        }
        if (check.Warnings.Count > 0 && !confirmLimits)
        {
            throw new ArgumentException(Loc.T("msg.lora.31", string.Join("; ", check.Warnings)));
        }
        var started = _objects.SetLoraModelState(loraId, LoraModelStates.Training, row.Path, "")
                      ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var cts = new CancellationTokenSource();
        lock (_running)
        {
            _running[loraId] = cts;
        }
        _ = Task.Run(() => RunAsync(loraId, plan, cts.Token, ConsoleKey(loraId), rethrow: false),
            cts.Token);
        return started;
    }

    /// <summary>
    /// ЧТО СПРОСИТЬ ПЕРЕД ЗАПУСКОМ (T-274) — то же самое, что проверит
    /// <see cref="Start"/>, но без запуска: форма спрашивает по этому ответу, а сервер
    /// потом требует подтверждений. Ошибки подготовки (модель без LoRA, пустой датасет,
    /// незаданная команда) сюда не приходят исключением — форма показала бы их вместо
    /// вопроса; они возвращаются полем <see cref="LoraTrainCheck.Error"/>.
    /// </summary>
    public LoraTrainCheck Check(string loraId, string? datasetId = null)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        try
        {
            return Check(row, Plan(row, datasetId));
        }
        catch (ArgumentException ex)
        {
            // сам разбор датасета тоже может отказать (назвали чужой) — на вопрос
            // «о чём переспросить» это должно отвечать текстом, а не вторым исключением
            ObjectItem? dataset = null;
            try
            {
                dataset = ResolveDataset(row.ObjectId, datasetId);
            }
            catch (ArgumentException)
            {
                // датасета нет — ответим тем, что знаем: причиной отказа
            }
            return new LoraTrainCheck
            {
                DatasetId = dataset?.Id ?? "",
                DatasetName = dataset?.Name ?? "",
                TrainedDatasetId = row.DatasetId,
                TrainedDatasetName = row.DatasetName,
                Error = ex.Message,
            };
        }
    }

    private static LoraTrainCheck Check(ObjectLoraModel row, TrainPlan plan)
    {
        var check = new LoraTrainCheck
        {
            DatasetId = plan.Dataset?.Id ?? "",
            DatasetName = plan.Dataset?.Name ?? "",
            TrainedDatasetId = row.DatasetId,
            TrainedDatasetName = row.DatasetName,
            Frames = plan.Frames.Count,
        };
        // «датасет сменился» — только когда обучение УЖЕ БЫЛО по названному датасету:
        // у необученной строки сравнивать не с чем, а у обученной прежней версией
        // (датасета не было вовсе) переспрос был бы упрёком ни за что
        check.DatasetChanged = row.DatasetId.Length > 0 && check.DatasetId.Length > 0
                               && row.DatasetId != check.DatasetId;
        if (plan.Dataset is not null)
        {
            check.Warnings = LoraDatasetLimits.Parse(plan.Dataset.DatasetJson)
                .Compare(plan.Lora.Train.Dataset);
        }
        return check;
    }

    /// <summary>Снять идущее обучение: строка возвращается в «необучена» с пояснением.</summary>
    public void Cancel(string loraId)
    {
        CancellationTokenSource? cts;
        lock (_running)
        {
            _running.Remove(loraId, out cts);
        }
        cts?.Cancel();
        _objects.SetLoraModelState(loraId, LoraModelStates.None, null, Loc.T("msg.lora.4"));
    }

    /// <summary>
    /// ЧТО И ЧЕМ БУДЕМ ДЕЛАТЬ — собирается ДО перевода строки в «обучается», синхронно.
    /// Всё, что можно проверить заранее (модель работает с LoRA, команда задана, кадров
    /// хватает), проверяется здесь и отвечает человеку сразу отказом: «нажал обучить —
    /// строка полчаса повисела в обучении и упала на первой же строке» это не работа.
    /// </summary>
    private TrainPlan Plan(ObjectLoraModel row, string? datasetId)
    {
        var item = _objects.Get(row.ObjectId) ?? throw new ArgumentException(Loc.T("msg.object.6"));
        // ЧУЖОЙ ОБЪЕКТ ЗДЕСЬ НЕ ОБУЧАЕТСЯ (T-102-S0): обучение пишет в его строки состояние
        // и путь готового файла, а писатель у строки один — её сервер. Обучить адаптер и
        // ПОЛЬЗОВАТЬСЯ им на другом сервере при этом можно: готовый файл уезжает партнёру
        // хранилищем организации (ObjectFiles.LoraStoreDir)
        _objects.EnsureCanWrite(item);
        var model = _models.Get(row.ModelId) ?? throw new ArgumentException(Loc.T("msg.object.11"));
        _models.ReadProfileSettings(model);
        var lora = model.Lora;
        if (!lora.Supported)
        {
            throw new ArgumentException(Loc.T("msg.lora.5", model.Name));
        }
        if (lora.Train.Kind == LoraTrainKinds.External)
        {
            throw new ArgumentException(Loc.T("msg.lora.6", model.Name));
        }
        // ПАКЕТЫ ОБУЧЕНИЯ (T-4-S0) ставятся вместе с моделью, поэтому здесь их только
        // проверяют — и отвечают отказом сразу, а не через полчаса скачивания. Ответ
        // называет и пакеты, и то, где нажать «Установить»: сам человек про
        // lora.train.packages ничего не знает
        if (lora.Train.Packages.Count > 0 && MissingPackages is { } missingOf)
        {
            var missing = missingOf(lora.Train.Packages);
            if (missing.Count > 0)
            {
                throw new ArgumentException(Loc.T("msg.lora.33", string.Join(", ", missing), model.Name));
            }
        }
        var project = item.ProjectId is { Length: > 0 } pid ? _projects.Get(pid) : null;
        var projectDir = project?.FolderPath ?? "";
        if (projectDir.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.lora.7"));
        }
        // ДАТАСЕТ, по которому обучаем (T-274): названный либо текущий. Его кадры — это его
        // дети; у объекта прежней версии, у которого датасета нет вовсе, кадрами остаются
        // прямые дети объекта (см. ObjectService.DatasetFrames). Погашенные не берём:
        // «выключить кадр, не удаляя» — единственный способ выкинуть неудачный ракурс из
        // обучения, не теряя сам файл
        var dataset = ResolveDataset(row.ObjectId, datasetId);
        var frames = _objects.DatasetFrames(row.ObjectId, dataset?.Id)
            .Where(c => c.IsActive && c.PathOrUrl.Trim().Length > 0)
            .ToList();
        var min = lora.Train.Dataset.MinItems;
        if (min > 0 && frames.Count < min)
        {
            throw new ArgumentException(Loc.T("msg.lora.8", frames.Count, min));
        }
        if (frames.Count == 0)
        {
            throw new ArgumentException(Loc.T("msg.lora.8", 0, 1));
        }
        var max = lora.Train.Dataset.MaxItems;
        if (max > 0 && frames.Count > max)
        {
            frames = frames.Take(max).ToList();
        }
        if (lora.Train.Start.Kind == LoraStartKinds.Process
            && lora.Train.Start.Command.Trim().Length == 0)
        {
            // ровно тот случай, о котором предупредил справочник (T-13-S1): каталог
            // обучающего репозитория и окружение python у каждого свои, и выдуманная
            // команда была бы хуже пустой
            throw new ArgumentException(Loc.T("msg.lora.9", model.Name));
        }
        if (lora.Train.Start.Kind == LoraStartKinds.None)
        {
            throw new ArgumentException(Loc.T("msg.lora.9", model.Name));
        }
        if (lora.Train.Start.Kind == LoraStartKinds.Http && lora.Train.Start.Url.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.lora.9", model.Name));
        }
        return new TrainPlan(row.Id, item, model, lora, projectDir, frames, dataset);
    }

    /// <summary>
    /// Датасет обучения: названный (и только СВОЙ — чужой не берём, кадры другого персонажа
    /// дали бы чужой адаптер) либо текущий датасет объекта. Ни одного датасета нет — null,
    /// и обучение пойдёт по прямым детям объекта, как до версии 1.98.
    /// </summary>
    private ObjectItem? ResolveDataset(string objectId, string? datasetId)
    {
        var datasets = _objects.Datasets(objectId);
        if (datasets.Count == 0)
        {
            return null;
        }
        if (datasetId is { Length: > 0 } wanted)
        {
            return datasets.FirstOrDefault(d => d.Id == wanted)
                   ?? throw new ArgumentException(Loc.T("msg.lora.29"));
        }
        var owner = _objects.Get(objectId);
        return datasets.FirstOrDefault(d => d.Id == owner?.CurrentDatasetId) ?? datasets[0];
    }

    /// <summary>
    /// ДАТАСЕТ ПОД ТРЕБОВАНИЯ МОДЕЛИ (T-57-S0) — то, что стоит за кнопкой «Создать/переключить
    /// на нужный датасет» в переспросе о несовпадении настроек.
    ///
    /// Порядок ровно такой, как в задании: СНАЧАЛА ИЩЕМ среди уже заведённых датасетов объекта
    /// подходящий и, найдя, просто делаем его текущим — заводить второй такой же и пережимать
    /// картинки заново было бы пустой работой и лишними файлами в папке проекта. Подходящим
    /// считается датасет, чьи настройки модель принимает (<see cref="LoraDatasetLimits.Compare"/>
    /// молчит) И в котором лежат ТЕ ЖЕ кадры, что в исходном: датасет с правильными настройками,
    /// но с картинками другого ракурса — это другой датасет, и молча обучать по нему нельзя.
    /// Сверяются кадры по именам файлов без расширения: пережатие меняет расширение (PNG→JPEG),
    /// а имя кадра переносится как есть, поэтому имя и есть общий признак.
    ///
    /// Не нашли — заводим НОВЫЙ датасет с настройками из модели (он сразу становится текущим)
    /// и отдаём список кадров, которые надо в него пережать. Само пережатие делает браузер:
    /// библиотеки работы с изображениями в проекте нет намеренно (T-12-S1).
    /// </summary>
    /// <param name="datasetId">Исходный датасет; пусто — текущий датасет объекта.</param>
    public LoraDatasetFitDto FitDataset(string loraId, string? datasetId, string? actorId)
    {
        var row = _objects.LoraModel(loraId) ?? throw new ArgumentException(Loc.T("msg.lora.1"));
        var model = _models.Get(row.ModelId) ?? throw new ArgumentException(Loc.T("msg.object.11"));
        _models.ReadProfileSettings(model);
        if (!model.Lora.Supported)
        {
            throw new ArgumentException(Loc.T("msg.lora.5", model.Name));
        }
        var wanted = model.Lora.Train.Dataset;
        var source = ResolveDataset(row.ObjectId, datasetId)
                     ?? throw new ArgumentException(Loc.T("msg.lora.29"));
        var frames = _objects.DatasetFrames(row.ObjectId, source.Id)
            .Where(c => c.IsActive && c.PathOrUrl.Trim().Length > 0)
            .ToList();
        if (frames.Count == 0)
        {
            throw new ArgumentException(Loc.T("msg.lora.8", 0, 1));
        }
        var names = frames.Select(BaseName).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var candidate in _objects.Datasets(row.ObjectId))
        {
            if (candidate.Id == source.Id)
            {
                continue;
            }
            var limits = LoraDatasetLimits.Parse(candidate.DatasetJson);
            if (limits.Compare(wanted).Count > 0)
            {
                continue;
            }
            var its = _objects.DatasetFrames(row.ObjectId, candidate.Id)
                .Where(c => c.IsActive && c.PathOrUrl.Trim().Length > 0)
                .Select(BaseName)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            if (its.Count == 0 || !names.All(its.Contains))
            {
                continue;
            }
            _objects.SetCurrentDataset(row.ObjectId, candidate.Id, actorId);
            return new LoraDatasetFitDto
            {
                Found = true,
                DatasetId = candidate.Id,
                DatasetName = candidate.Name,
                Limits = LoraDatasetLimitsDto.Of(limits),
            };
        }
        var target = LoraDatasetLimits.FromModel(wanted, LoraDatasetLimits.Parse(source.DatasetJson));
        // название говорит, чем этот датасет отличается от исходного: через месяц «датасет 2»
        // не скажет ничего, а «Персонаж 768×512 PNG» — скажет всё
        var created = _objects.CreateDataset(row.ObjectId,
            Loc.T("msg.lora.34", source.Name, target.MaxWidth, target.MaxHeight,
                target.Format.ToUpperInvariant()),
            target, actorId);
        // кадров берём не больше, чем модель согласна взять: иначе новый датасет тут же
        // получил бы то же самое несовпадение, из-за которого его и заводят
        if (target.MaxItems > 0 && frames.Count > target.MaxItems)
        {
            frames = frames.Take(target.MaxItems).ToList();
        }
        return new LoraDatasetFitDto
        {
            Found = false,
            DatasetId = created.Id,
            DatasetName = created.Name,
            Limits = LoraDatasetLimitsDto.Of(target),
            Frames = frames.Select(f => new LoraFitFrameDto
            {
                Path = f.PathOrUrl,
                FileName = BaseName(f),
                Description = f.Description,
            }).ToList(),
        };
    }

    /// <summary>Имя файла кадра без расширения — общий признак «того же кадра» у датасетов
    /// с разным форматом картинок.</summary>
    private static string BaseName(ObjectItem frame) =>
        Path.GetFileNameWithoutExtension(frame.PathOrUrl.Replace('\\', '/'));

    /// <summary>
    /// Все пять шагов подряд. Любая неудача — состояние «ошибка» с текстом причины.
    /// <paramref name="rethrow"/> — поднять беду наружу (обучение задачей, T-157-S0): поле
    /// <c>Error</c> заполняется в обоих случаях, но заданию нужно ещё и упасть, иначе задача
    /// сдалась бы «выполненной» с несостоявшимся обучением.
    /// </summary>
    private async Task<string> RunAsync(string loraId, TrainPlan plan, CancellationToken token,
        string consoleKey, bool rethrow)
    {
        TrainHandle? handle = null;
        try
        {
            var dataset = PrepareDataset(plan);                       // шаг 1
            WriteTrainFiles(plan, dataset);                           // шаг 1½ (T-289)
            handle = await StartTrainingAsync(plan, dataset, consoleKey, token);  // шаг 2
            await WaitAsync(plan, handle, token);                     // шаг 3
            var file = await FetchResultAsync(plan, handle, token);    // шаг 4
            // датасет запоминается ВМЕСТЕ с удачным концом (T-274), а не при запуске: строка
            // помнит, чем получен лежащий в ней файл, а у упавшего обучения файла нет
            _objects.SetLoraModelState(loraId, LoraModelStates.Ready, file, "",
                plan.Dataset?.Id);
            // готовый адаптер становится ТЕКУЩИМ адаптером объекта: именно его подставляет
            // в модель работа с адаптером при генерации
            _objects.SetObjectLoraFile(plan.Item.Id, file);
            return file;
        }
        catch (OperationCanceledException)
        {
            // отмену уже записал Cancel — второй раз состояние не трогаем. А вот сам процесс
            // обучения снять надо: без этого «остановить» оставляло бы считать видеокарту
            if (handle?.Process is { } cancelled)
            {
                TryKill(cancelled);
            }
            if (rethrow)
            {
                throw;
            }
            return "";
        }
        catch (Exception ex)
        {
            // ПОСЛЕДНИЕ СТРОКИ ВЫВОДА В ТЕКСТ ОШИБКИ (T-152-S0): без них в строке остаётся
            // «обучение завершилось с кодом 1», по которому чинить нечего
            var text = WithTail(ex.Message, handle?.Log);
            _objects.SetLoraModelState(loraId, LoraModelStates.Error, null, text);
            // и та же причина последней строкой журнала: человек приходит к нему через сутки
            handle?.Log?.Line("[AI2P] " + Short(ex.Message));
            if (rethrow)
            {
                throw new ArgumentException(text, ex);
            }
            return "";
        }
        finally
        {
            handle?.Log?.Dispose();
            lock (_running)
            {
                _running.Remove(loraId);
            }
        }
    }

    // --- ШАГ 1: датасет ---

    /// <summary>
    /// Собрать датасет: кадры объекта складываются в каталог настройки, подписи — рядом
    /// одноимёнными <c>.txt</c> (так их читают обучающие скрипты) либо одним json.
    /// Каталог перед сборкой ОЧИЩАЕТСЯ от прошлого прогона: иначе снятый кадр продолжал бы
    /// участвовать в обучении, и никакая правка списка ничего бы не меняла.
    /// </summary>
    private string PrepareDataset(TrainPlan plan)
    {
        var dataset = plan.Lora.Train.Dataset;
        var dir = ResolveProjectPath(plan, ExpandAll(dataset.Path, plan, "", ""));
        if (dir.Trim().Length == 0)
        {
            dir = Path.Combine(plan.ProjectDir, "lora", plan.Item.DisplayId, "dataset");
        }
        if (dataset.Kind == LoraDatasetKinds.Upload)
        {
            // облачное обучение (архив кадров уезжает провайдеру) в этой версии не сделано:
            // ни одной записи справочника с таким способом нет, а «сделали вслепую» —
            // это обещание, а не работа
            throw new ArgumentException(Loc.T("msg.lora.10"));
        }
        if (Directory.Exists(dir))
        {
            foreach (var old in Directory.EnumerateFiles(dir))
            {
                File.Delete(old);
            }
        }
        Directory.CreateDirectory(dir);
        var captions = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var frame in plan.Frames)
        {
            // кадр датасета лежит в каталоге данных организации, кадр прежних версий — в
            // папке проекта (T-98-S0); откуда его брать, говорит сам путь
            var source = ObjectFiles.Resolve(frame.PathOrUrl, plan.ProjectDir, _files?.DataDir);
            if (source is null || !File.Exists(source))
            {
                throw new ArgumentException(Loc.T("msg.lora.11", frame.PathOrUrl));
            }
            index++;
            var name = $"{index:D3}{Path.GetExtension(source)}";
            File.Copy(source, Path.Combine(dir, name), overwrite: true);
            captions[name] = frame.Description.Trim();
            if (dataset.Captions == "txt")
            {
                File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + ".txt"),
                    captions[name], new UTF8Encoding(false));
            }
        }
        if (dataset.Captions == "json")
        {
            File.WriteAllText(Path.Combine(dir, "captions.json"),
                JsonSerializer.Serialize(captions, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        return dir;
    }

    // --- ШАГ 1½: файлы настройки обучения (T-289) ---

    /// <summary>
    /// Положить рядом с датасетом файлы настройки, объявленные моделью: конфигурацию тренера
    /// и запускающий скрипт. Пишутся ПЕРЕД каждым запуском и перезаписываются: это часть
    /// настройки модели, такая же, как команда, — и правится она в профайле, а не на диске.
    /// Кодировка UTF-8 БЕЗ BOM: конфигурации читает python и cmd.exe, а BOM в первой строке
    /// командного файла становится частью первой команды.
    /// </summary>
    private void WriteTrainFiles(TrainPlan plan, string dataset)
    {
        var output = ResolveProjectPath(plan,
            ExpandAll(OutputOf(plan.Lora.Train.Result.Path), plan, dataset, ""));
        foreach (var file in plan.Lora.Train.Files)
        {
            var path = ResolveProjectPath(plan, ExpandAll(file.Path, plan, dataset, output));
            if (path.Length == 0)
            {
                continue;
            }
            var dir = Path.GetDirectoryName(path);
            if (dir is { Length: > 0 })
            {
                Directory.CreateDirectory(dir);
            }
            try
            {
                File.WriteAllText(path, ExpandAll(file.Text, plan, dataset, output),
                    new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ArgumentException(Loc.T("msg.lora.32", path, ex.Message), ex);
            }
        }
    }

    // --- ШАГ 2: запуск ---

    private async Task<TrainHandle> StartTrainingAsync(TrainPlan plan, string dataset,
        string consoleKey, CancellationToken token)
    {
        var start = plan.Lora.Train.Start;
        var output = ResolveProjectPath(plan,
            ExpandAll(OutputOf(plan.Lora.Train.Result.Path), plan, dataset, ""));
        if (start.Kind == LoraStartKinds.Http)
        {
            var url = ExpandAll(start.Url, plan, dataset, output);
            var body = new
            {
                @object = plan.Item.DisplayId,
                model = plan.Model.Name,
                dataset,
                steps = start.Steps,
            };
            using var response = await _http.PostAsync(url,
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
                token);
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                // текст провайдера уходит человеку как есть: это единственное, что говорит,
                // почему обучение не началось
                throw new ArgumentException(Loc.T("msg.lora.12", (int)response.StatusCode, Short(text)));
            }
            return new TrainHandle(null, text, output);
        }

        var command = ExpandAll(start.Command, plan, dataset, output);
        var workDir = start.WorkDir.Trim().Length > 0
            ? ResolveProjectPath(plan, ExpandAll(start.WorkDir, plan, dataset, output))
            : plan.ProjectDir;
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = Directory.Exists(workDir) ? workDir : plan.ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // своё СКРЫТОЕ окно консоли: без него потомок унаследовал бы окно, а у службы
            // окна нет вовсе — и система выдала бы ему новое, пустое и видимое (T-237)
            CreateNoWindow = true,
            // вывод тренера — UTF-8: без явной кодировки он читается в кодировке консоли
            // родителя, и русские строки приходят нечитаемыми (то же, что у локальных
            // серверов моделей, todo37_2)
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // тренеры — это python, а python при перенаправленном выводе копит его блоками по
        // 8 КБ: без этих двух переменных прогресс обучения приходил бы редкими пачками, и
        // «молчание» считалось бы там, где программа на самом деле печатает
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUNBUFFERED"] = "1";
        if (OperatingSystem.IsWindows())
        {
            // cmd.exe разбирает командную строку ПО-СВОЕМУ и экранирования .NET
            // (ArgumentList заключает аргумент в кавычки и ставит перед внутренними
            // кавычками обратную косую) не понимает: команда с кавычками в путях
            // доезжает до него искалеченной. Поэтому здесь строка целиком
            info.Arguments = "/c " + command;
        }
        else
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
        }
        var process = Process.Start(info) ?? throw new ArgumentException(Loc.T("msg.lora.13", command));
        // ЧИТАТЬ ПОТОКИ НАДО СРАЗУ (T-152-S0), а не после конца процесса: труба вмещает
        // около 4 КБ, дальше программа встаёт на записи в консоль намертво. Журнал ложится
        // рядом с результатом обучения — файл переживает перезапуск сервера, буфер консоли нет
        var log = new ProcessOutputLog(LogPathOf(plan, output), _console, consoleKey);
        log.Line($"[AI2P] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {plan.Model.Name} → {plan.Item.DisplayId}");
        log.Line("[AI2P] " + info.WorkingDirectory + "> " + command);
        log.Attach(process);
        return new TrainHandle(process, "", output) { Log = log };
    }

    /// <summary>Файл журнала обучения: рядом с результатом (туда же пишет и сам тренер),
    /// а если каталог результата не задан — в папке проекта под кодом объекта.</summary>
    private static string LogPathOf(TrainPlan plan, string output)
    {
        var dir = output.Trim().Length > 0
            ? output
            : Path.Combine(plan.ProjectDir, "lora", plan.Item.DisplayId);
        return Path.Combine(dir, "train.log");
    }

    // --- ШАГ 3: ожидание ---

    private async Task WaitAsync(TrainPlan plan, TrainHandle handle, CancellationToken token)
    {
        var wait = plan.Lora.Train.Wait;
        var minutes = wait.TimeoutMinutes > 0 ? wait.TimeoutMinutes : DefaultTimeoutMinutes;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromMinutes(minutes));
        if (handle.Process is { } process)
        {
            // ДВА ПРЕДЕЛА, И ОНИ ПРО РАЗНОЕ (T-152-S0): общий — «считает, но слишком долго»
            // (остался крупным, обучение идёт часами), молчание — «не считает вовсе».
            // Двенадцать часов тишины должны кончаться минутами, а не половиной суток
            var idleMinutes = wait.IdleMinutes > 0 ? wait.IdleMinutes : DefaultIdleMinutes;
            var log = handle.Log ?? new ProcessOutputLog();
            bool exited;
            try
            {
                exited = await log.WaitForExitAsync(process,
                    TimeSpan.FromMinutes(idleMinutes), limit.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                TryKill(process);
                throw new ArgumentException(Loc.T("msg.lora.14", minutes));
            }
            if (!exited)
            {
                TryKill(process);
                throw new ArgumentException(Loc.T("msg.lora.35", idleMinutes));
            }
            if (process.ExitCode != 0)
            {
                // последние строки вывода — это и есть объяснение, зачем ещё нужен код возврата
                throw new ArgumentException(Loc.T("msg.lora.15", process.ExitCode, log.Tail()));
            }
            return;
        }
        if (wait.Kind != LoraWaitKinds.Poll || wait.PollUrl.Trim().Length == 0)
        {
            return;     // сказано не ждать — значит обучение отвечает синхронно
        }
        var interval = TimeSpan.FromSeconds(wait.IntervalSec > 0 ? wait.IntervalSec : 30);
        while (true)
        {
            limit.Token.ThrowIfCancellationRequested();
            await Task.Delay(interval, limit.Token);
            var text = await _http.GetStringAsync(ExpandAll(wait.PollUrl, plan, "", handle.Output),
                limit.Token);
            handle.LastPoll = text;
            if (IsDone(text, wait.DoneField, wait.DoneValue))
            {
                return;
            }
        }
    }

    // --- ШАГ 4: результат ---

    private async Task<string> FetchResultAsync(TrainPlan plan, TrainHandle handle,
        CancellationToken token)
    {
        var result = plan.Lora.Train.Result;
        var target = ExpandAll(result.Target, plan, "", handle.Output);
        if (target.Trim().Length == 0)
        {
            target = "loras/" + plan.Item.DisplayId + ".safetensors";
        }
        var targetAbs = ResolveRepoPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(targetAbs)!);
        if (result.Kind == LoraResultKinds.Download)
        {
            var url = UrlFromAnswer(handle.LastPoll.Length > 0 ? handle.LastPoll : handle.Answer,
                result.UrlField);
            if (url.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.lora.16", result.UrlField));
            }
            var bytes = await _http.GetByteArrayAsync(url, token);
            await File.WriteAllBytesAsync(targetAbs, bytes, token);
            StoreForReplication(targetAbs);
            return target;
        }
        var source = ResolveProjectPath(plan, ExpandAll(result.Path, plan, "", handle.Output));
        if (source.Trim().Length == 0 || !File.Exists(source))
        {
            throw new ArgumentException(Loc.T("msg.lora.17", source));
        }
        File.Copy(source, targetAbs, overwrite: true);
        StoreForReplication(targetAbs);
        return target;
    }

    /// <summary>
    /// КОПИЯ ГОТОВОГО АДАПТЕРА В ХРАНИЛИЩЕ ОРГАНИЗАЦИИ (T-102-S0) — чтобы обученный здесь
    /// файл уехал остальным серверам кластера: «обучаем на одном, используем на другом».
    ///
    /// Сам адаптер лежит в репозитории моделей, а репозиторий — железо этого компьютера и не
    /// реплицируется. Каталог данных организации реплицируется целиком, поэтому копии
    /// достаточно: кода передачи писать не надо вовсе, а на партнёре файл найдёт по имени
    /// <c>ObjectLoadService.FindLoraFile</c> и положит его в СВОЙ репозиторий моделей.
    ///
    /// Неудача копирования обучение не роняет: адаптер обучен и на этом сервере работает —
    /// а «не доехал до соседа» лечится повторным копированием при следующем обучении.
    /// </summary>
    private void StoreForReplication(string absFile)
    {
        if (_files is not { } files || !File.Exists(absFile))
        {
            return;
        }
        try
        {
            var abs = files.Abs(ObjectFiles.LoraStoreRel(absFile));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.Copy(absFile, abs, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            // молчать нельзя, но и падать не за что: файл обучен и лежит на месте
            Serilog.Log.Warning(ex, "AI2P: адаптер {File} не удалось положить в хранилище организации",
                absFile);
        }
    }

    // --- общее ---

    /// <summary>
    /// Подстановки настройки ЦЕЛИКОМ (T-289): свои ({object}, {dataset}, {output}, …) плюс
    /// пути установки модели ({modelsRepo}, {model:…}, {package:…}) — последние знает
    /// установщик, и он же их подставляет. Один и тот же набор во ВСЕХ полях настройки,
    /// включая содержимое файлов: иначе в каждом поле пришлось бы помнить, что в нём работает.
    /// </summary>
    private string ExpandAll(string value, TrainPlan plan, string dataset, string output)
    {
        var expanded = Expand(value, plan, dataset, output);
        return ModelPaths is { } paths ? paths(plan.Model, expanded) : expanded;
    }

    /// <summary>Свои подстановки настройки: код объекта, каталог датасета, каталог результата,
    /// число шагов, размер кадра.</summary>
    private static string Expand(string value, TrainPlan plan, string dataset, string output) =>
        value
            .Replace("{object}", plan.Item.DisplayId, StringComparison.Ordinal)
            .Replace("{name}", plan.Item.Name, StringComparison.Ordinal)
            // номер датасета (T-274): у объекта их несколько, и тому, кто хочет собирать
            // их в разные каталоги, нужно чем-то их различать
            .Replace("{datasetId}", plan.Dataset?.DisplayId ?? plan.Item.DisplayId,
                StringComparison.Ordinal)
            .Replace("{dataset}", dataset, StringComparison.Ordinal)
            .Replace("{output}", output, StringComparison.Ordinal)
            .Replace("{steps}", plan.Lora.Train.Start.Steps.ToString(), StringComparison.Ordinal)
            // размер кадра (T-289): конфигурации тренеров задают разрешение обучения числом,
            // и брать его надо оттуда же, откуда контроль картинок датасета, — из настройки
            .Replace("{width}", plan.Lora.Train.Dataset.Width.ToString(), StringComparison.Ordinal)
            .Replace("{height}", plan.Lora.Train.Dataset.Height.ToString(), StringComparison.Ordinal);

    /// <summary>Каталог, в который обучение кладёт результат: каталог файла из настройки.</summary>
    private static string OutputOf(string resultPath)
    {
        var at = resultPath.LastIndexOfAny(['/', '\\']);
        return at <= 0 ? resultPath : resultPath[..at];
    }

    /// <summary>Относительный путь настройки — от папки проекта; абсолютный — как есть.</summary>
    private static string ResolveProjectPath(TrainPlan plan, string path)
    {
        var value = path.Trim();
        if (value.Length == 0)
        {
            return "";
        }
        value = PathHome.Expand(value);
        return Path.IsPathRooted(value) ? value : Path.Combine(plan.ProjectDir, value);
    }

    /// <summary>Относительный путь готового адаптера — от репозитория моделей: туда за ним
    /// ходит сам движок модели.</summary>
    private string ResolveRepoPath(string path)
    {
        var value = PathHome.Expand(path.Trim());
        return Path.IsPathRooted(value) ? value : Path.Combine(PathHome.Expand(_modelsRepo()), value);
    }

    /// <summary>Ответ провайдера говорит «готово»: поле не задано — годится любой ответ.</summary>
    private static bool IsDone(string json, string field, string value)
    {
        if (field.Trim().Length == 0)
        {
            return true;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = JsonRead.Str(doc.RootElement, field);
            return value.Trim().Length == 0
                ? text.Length > 0
                : string.Equals(text, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Ссылка на готовый адаптер в ответе провайдера — поле из настройки.</summary>
    private static string UrlFromAnswer(string json, string field)
    {
        if (field.Trim().Length == 0)
        {
            return "";
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonRead.Str(doc.RootElement, field);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // процесс уже кончился сам — гасить нечего
        }
    }

    /// <summary>
    /// ЧТО СКАЗАЛ УПАВШИЙ ПРОЦЕСС (T-289, потоковое чтение — T-152-S0). Два правила, и оба
    /// нашлись на первом же настоящем запуске обучения:
    /// <list type="number">
    /// <item>берём ОБА потока, а не «stderr, если он не пуст». Оболочка пишет в stderr свою
    /// строку («системе не удаётся найти указанный путь»), а объяснение, что делать, скрипт
    /// печатает в stdout — по одному stderr человек чинить не может (потоки сливаются в один
    /// журнал <see cref="ProcessOutputLog"/> прямо во время работы);</item>
    /// <item>берём КОНЕЦ, а не начало. Обучение печатает тысячи строк, и причина падения
    /// всегда в последних; первые 1000 знаков — это приветствие тренера.</item>
    /// </list>
    /// Хвост приклеивается к ЛЮБОЙ неудаче, а не только к ненулевому коду возврата: молчание,
    /// общий тайм-аут и «обучение не оставило файла» без него одинаково нечинимы.
    /// </summary>
    private static string WithTail(string message, ProcessOutputLog? log)
    {
        var text = Short(message);
        var tail = log?.Tail() ?? "";
        return tail.Length == 0 || text.Contains(tail, StringComparison.Ordinal)
            ? text
            : text + "\n" + tail;
    }

    /// <summary>Текст ошибки в строку списка: подсказка, а не простыня в пол-экрана.</summary>
    private static string Short(string text)
    {
        var value = (text ?? "").Replace("\r", "").Trim();
        return value.Length <= 1000 ? value : value[..1000] + "…";
    }

    /// <summary>Что и чем обучаем — собрано ДО запуска, дальше не меняется.
    /// <paramref name="Dataset"/> — объект-датасет; null у объекта прежней версии, чьи кадры
    /// лежат прямыми детьми.</summary>
    private sealed record TrainPlan(string LoraId, ObjectItem Item, AiModel Model,
        LoraSettings Lora, string ProjectDir, List<ObjectItem> Frames, ObjectItem? Dataset);

    /// <summary>Идущее обучение: свой процесс либо ответ провайдера.</summary>
    private sealed class TrainHandle(Process? process, string answer, string output)
    {
        public Process? Process { get; } = process;
        public string Answer { get; } = answer;
        public string Output { get; } = output;
        public string LastPoll { get; set; } = "";

        /// <summary>Живой вывод процесса обучения (T-152-S0); null у обучения у провайдера.</summary>
        public ProcessOutputLog? Log { get; init; }
    }
}
