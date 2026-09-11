using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ЗАГРУЗКА ОБЪЕКТОВ ЗАДАЧИ В МОДЕЛЬ (T-14-S1) — сторона хранилища и файлов: собрать объекты,
/// названные в описании задачи, свести их с настройкой модели исполнителя
/// (<see cref="ObjectLoadPlanner"/>) и приготовить всё, что коннектору нужно для загрузки.
///
/// Здесь же собираются ДВА текста для человека:
/// <list type="bullet">
/// <item>текст ошибки задания — по строке на каждое несовпадение, с номером объекта, названием
/// и причиной: «встало с ошибкой» без подробностей заставляет разбираться заново;</item>
/// <item>сообщение в чат задачи — какие обученные адаптеры в проекте есть и кто из исполнителей
/// может взять эту работу вместо назначенного. Это и есть ответ на вопрос «а что теперь
/// делать», который иначе человеку пришлось бы искать руками по справочнику.</item>
/// </list>
///
/// ГДЕ ЛЕЖИТ ФАЙЛ АДАПТЕРА. Поле объекта <c>LoraPath</c> заполняет обучение (T-12-S1), и путь
/// там может быть записан по-разному, поэтому файл ищется по нескольким местам подряд:
/// абсолютный путь как есть → папка проекта (там же лежат эталонные кадры, T-259) → каталог
/// весов моделей и его подкаталог <c>loras</c> (туда его кладёт настройка справочника,
/// <c>train.result.target</c>). Первый найденный и берётся.
/// </summary>
public sealed class ObjectLoadService
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", nameof(ObjectLoadService));

    /// <summary>Каталог адаптеров внутри каталога весов моделей — он же категория
    /// <c>loras</c> для ComfyUI (файл ai2p_extra_model_paths.yaml).</summary>
    public const string LorasCategory = "loras";

    private readonly ObjectService _objects;
    private readonly ExecutorService _executors;
    private readonly FileStore _files;
    private readonly Func<string>? _modelsRepo;

    /// <param name="modelsRepo">Каталог репозитория моделей (ТЗ v1.41): в нём живут веса и
    /// подкаталог адаптеров. null — адаптер ищется только по абсолютному пути и в папке
    /// проекта (так работают тесты).</param>
    public ObjectLoadService(ObjectService objects, ExecutorService executors, FileStore files,
        Func<string>? modelsRepo = null)
    {
        _objects = objects;
        _executors = executors;
        _files = files;
        _modelsRepo = modelsRepo;
    }

    /// <summary>
    /// Каталог адаптеров LoRA. Это <c>&lt;репозиторий моделей&gt;/loras</c> — ровно туда их
    /// кладёт обучение (T-12-S1: <c>train.result.target</c> считается от репозитория моделей,
    /// а у поставляемых записей он равен <c>loras/{object}.safetensors</c>).
    /// null — репозиторий не задан (тесты).
    /// </summary>
    public string? LorasDir() =>
        Repo() is { Length: > 0 } repo ? Path.Combine(repo, LorasCategory) : null;

    /// <summary>Каталог репозитория моделей с раскрытым «~» — как его читает обучение.</summary>
    private string? Repo() =>
        _modelsRepo?.Invoke() is { Length: > 0 } repo ? PathHome.Expand(repo) : null;

    /// <summary>
    /// Объекты, названные ссылками <c>@obj:…</c> в описании задачи, в порядке появления.
    /// Ссылки читаются в СЫРОМ описании — до подстановки паспортов (после неё маркеров уже нет).
    /// Неизвестная ссылка пропускается: она и в промпте остаётся как есть (T-259).
    /// </summary>
    public List<ObjectLoadRef> RefsOf(string? projectId, string? description, string? modelId = null)
    {
        var result = new List<ObjectLoadRef>();
        if (projectId is not { Length: > 0 })
        {
            return result;
        }
        foreach (var key in ObjectRefs.Find(description))
        {
            if (_objects.Resolve(projectId, key) is not { } item)
            {
                continue;
            }
            var (status, path) = AdapterOf(item, modelId);
            result.Add(new ObjectLoadRef(item.DisplayId, item.Name, item.Type, item.PathOrUrl,
                path, status, StrengthOf(item)));
        }
        return result;
    }

    /// <summary>
    /// АДАПТЕР ОБЪЕКТА ПОД ЭТУ МОДЕЛЬ (T-12-S1): у объекта-адаптера обучение ведётся по строке
    /// на каждую запись справочника — веса разные, и файл, обученный под одну модель, к другой
    /// не подходит. Поэтому «обучен ли адаптер» — вопрос ПРО МОДЕЛЬ ИСПОЛНИТЕЛЯ, а не про
    /// объект вообще.
    ///
    /// Строк обучения нет вовсе (объект завели раньше T-12-S1 либо файл вписали руками) —
    /// берём поле самого объекта: там лежит адаптер, обученный на стороне.
    /// </summary>
    public (string Status, string Path) AdapterOf(ObjectItem item, string? modelId)
    {
        var rows = _objects.LoraModels(item.Id);
        if (rows.Count == 0)
        {
            return (item.LoraStatus, item.LoraPath);
        }
        var mine = modelId is { Length: > 0 }
            ? rows.FirstOrDefault(r => r.ModelId == modelId)
            : rows.FirstOrDefault(r => r.Status == LoraModelStates.Ready && r.Path.Length > 0);
        if (mine is null)
        {
            // под ЭТУ модель адаптера нет; поле объекта тоже подойдёт, если человек вписал
            // готовый файл руками — иначе честное «не обучен»
            return item.LoraPath.Length > 0
                ? (item.LoraStatus, item.LoraPath)
                : (LoraModelStates.None, "");
        }
        return (mine.Status, mine.Path);
    }

    /// <summary>
    /// Что уедет в модель и что этому мешает. <paramref name="projectFolder"/> — папка проекта
    /// на этом компьютере (null — не задана: тогда файлов объектов взять неоткуда).
    /// </summary>
    public ObjectLoadPlan Plan(string? projectId, string? description, LoraSettings lora,
        RefImageSettings refImage, string modelName, string? projectFolder, string? modelId = null)
    {
        var refs = RefsOf(projectId, description, modelId);
        return ObjectLoadPlanner.Plan(refs, lora, refImage, modelName,
            path => FindLoraFile(path, projectFolder),
            // кадр датасета LoRA лежит в каталоге данных организации (T-98-S0), остальные
            // файлы объектов — в папке проекта; откуда его брать, говорит сам путь
            path => ObjectFiles.Resolve(path, projectFolder, _files.DataDir) is { } abs
                    && File.Exists(abs));
    }

    /// <summary>
    /// Текст ошибки задания: заголовок и по строке на каждое несовпадение. Читает его человек,
    /// поэтому язык — язык установки (артефакт <c>J-N-error.md</c> и консоль задания).
    /// </summary>
    public static string ErrorText(ObjectLoadPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.T("msg.objectLoad.13"));
        foreach (var problem in plan.Problems)
        {
            sb.Append('\n').Append("• ").Append(problem.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Сообщение в чат задачи: что есть в проекте и кем можно заменить исполнителя.
    /// Пишется РЯДОМ с ошибкой, а не вместо неё: ошибка объясняет, почему встали, а чат —
    /// что с этим делать дальше.
    /// </summary>
    /// <param name="currentExecutorId">Назначенный исполнитель — его в списке замены нет.</param>
    public string ChatText(ObjectLoadPlan plan, string? projectId, string? currentExecutorId)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ErrorText(plan));
        if (plan.LoraFailed)
        {
            sb.AppendLine();
            var ready = ReadyLoras(projectId);
            sb.AppendLine(ready.Count == 0
                ? Loc.T("msg.objectLoad.15")
                : Loc.T("msg.objectLoad.14"));
            foreach (var line in ready)
            {
                sb.AppendLine("* " + line);
            }
        }
        var candidates = Substitutes(currentExecutorId, plan.LoraFailed, plan.ImageFailed);
        sb.AppendLine();
        sb.AppendLine(candidates.Count == 0
            ? Loc.T("msg.objectLoad.17")
            : Loc.T("msg.objectLoad.16"));
        foreach (var (executor, why) in candidates)
        {
            sb.AppendLine($"* {executor.Nick} — {why}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// ОБУЧЕННЫЕ АДАПТЕРЫ ПРОЕКТА строками для чата: номер объекта, название и то, ПОД КАКУЮ
    /// МОДЕЛЬ адаптер обучен (T-12-S1) — без модели список бесполезен, файл под чужие веса
    /// подставлять некуда.
    /// </summary>
    public List<string> ReadyLoras(string? projectId)
    {
        var lines = new List<string>();
        if (projectId is not { Length: > 0 })
        {
            return lines;
        }
        foreach (var item in _objects.List(projectId).OrderBy(o => DisplayIds.Order(o.DisplayId)))
        {
            foreach (var row in _objects.LoraModels(item.Id)
                         .Where(r => r.Status == LoraModelStates.Ready && r.Path.Length > 0))
            {
                var model = row.ModelName.Length > 0 ? row.ModelName : row.ModelDisplayId;
                lines.Add($"{item.DisplayId} «{item.Name}» — {model} — {row.Path}");
            }
            // адаптер, вписанный руками в сам объект (T-259): строки обучения у него нет,
            // но подставить его можно так же
            if (item.LoraStatus == LoraStates.Ready && item.LoraPath.Length > 0)
            {
                lines.Add($"{item.DisplayId} «{item.Name}» — {item.LoraPath}");
            }
            if (lines.Count >= 20)
            {
                break;
            }
        }
        return lines;
    }

    /// <summary>
    /// Исполнители, чья модель умеет то, чего не хватило назначенному. Смотрим ПРОФАЙЛ
    /// исполнителя, а не запись справочника: у исполнителя профайл может быть свой
    /// (T-250), и решает именно он.
    /// </summary>
    public List<(Executor Executor, string Why)> Substitutes(string? currentExecutorId,
        bool needLora, bool needImage)
    {
        var result = new List<(Executor, string)>();
        // ничего не сорвалось — заменять некого и незачем: список «могут заменить», в котором
        // перечислены вообще все агенты, только сбивает с толку
        if (!needLora && !needImage)
        {
            return result;
        }
        foreach (var executor in _executors.List())
        {
            if (executor.Kind != ExecutorKind.Ai || !executor.IsActive || executor.Id == currentExecutorId
                || executor.ProfilePath.Length == 0)
            {
                continue;
            }
            var profileJson = _files.ReadText(executor.ProfilePath);
            if (profileJson.Trim().Length == 0)
            {
                continue;
            }
            var lora = LoraSettings.Parse(profileJson);
            var refImage = RefImageSettings.Parse(profileJson);
            if (needLora && !lora.Supported)
            {
                continue;
            }
            if (needImage && !refImage.Supported)
            {
                continue;
            }
            var why = needLora
                ? Loc.T("models.lora.apply." +
                        (Array.IndexOf(ApplyKinds, lora.Apply.Kind) >= 0 ? lora.Apply.Kind : LoraApplyKinds.None))
                : Loc.T("models.refImage.kind." +
                        (Array.IndexOf(RefKinds, refImage.Kind) >= 0 ? refImage.Kind : RefImageKinds.None));
            result.Add((executor, why));
            if (result.Count >= 10)
            {
                break;
            }
        }
        return result;
    }

    private static readonly string[] ApplyKinds =
        [LoraApplyKinds.None, LoraApplyKinds.Workflow, LoraApplyKinds.LaunchArg, LoraApplyKinds.RequestField];

    private static readonly string[] RefKinds =
        [RefImageKinds.None, RefImageKinds.Upload, RefImageKinds.RequestField, RefImageKinds.Message];

    /// <summary>
    /// Положить адаптер в каталог, из которого его видит ComfyUI, и вернуть путь ТАМ.
    /// Файл уже лежит в каталоге — копирование пропускается; каталога нет — файл остаётся
    /// на месте, и его путь возвращается как есть (тогда сработает свой extra_model_paths).
    /// </summary>
    public string PlaceInLorasDir(string absFile)
    {
        var dir = LorasDir();
        if (dir is not { Length: > 0 })
        {
            return absFile;
        }
        var target = Path.Combine(dir, Path.GetFileName(absFile));
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(absFile),
                StringComparison.OrdinalIgnoreCase))
        {
            return absFile;
        }
        try
        {
            Directory.CreateDirectory(dir);
            // копируем, только если файла нет или он другой: адаптеры весят сотни мегабайт,
            // и переписывать их на каждый кадр ролика незачем
            var source = new FileInfo(absFile);
            var existing = new FileInfo(target);
            if (!existing.Exists || existing.Length != source.Length ||
                existing.LastWriteTimeUtc != source.LastWriteTimeUtc)
            {
                File.Copy(absFile, target, overwrite: true);
                File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
                Logger.Information("Адаптер LoRA {File} положен в каталог моделей: {Target}",
                    absFile, target);
            }
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            Logger.Warning(ex, "Не удалось положить адаптер {File} в каталог моделей", absFile);
            return absFile;
        }
    }

    /// <summary>Вес адаптера у самого объекта (meta.loraStrength); нет — умолчание профайла.</summary>
    private static double? StrengthOf(ObjectItem item)
    {
        if (item.MetaJson.Trim().Length == 0)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(item.MetaJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("loraStrength", out var v) &&
                v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var value) && value > 0)
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // мусор в метаданных объекта не должен ронять запуск задания
        }
        return null;
    }

    /// <summary>Файл адаптера на этом компьютере; null — не нашли ни в одном из мест.</summary>
    public string? FindLoraFile(string path, string? projectFolder)
    {
        if (path.Trim().Length == 0)
        {
            return null;
        }
        var relative = path.Trim().Replace('\\', '/');
        foreach (var candidate in Candidates(relative, projectFolder))
        {
            if (candidate is { Length: > 0 } && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private IEnumerable<string?> Candidates(string relative, string? projectFolder)
    {
        if (Path.IsPathRooted(relative))
        {
            yield return relative;
            yield break;
        }
        // порядок не случайный: сначала место, куда адаптер кладёт обучение (T-12-S1 —
        // путь от репозитория моделей), потом привычные соседние каталоги, и только затем
        // папка проекта: там лежат эталонные кадры, а адаптер туда попадает лишь вручную
        if (Repo() is { Length: > 0 } repo)
        {
            yield return SafeCombine(repo, relative);
            yield return SafeCombine(Path.Combine(repo, LorasCategory), Path.GetFileName(relative));
            yield return SafeCombine(Path.Combine(repo, "models"), relative);
        }
        // ХРАНИЛИЩЕ ОРГАНИЗАЦИИ (T-102-S0): сюда обучение кладёт КОПИЮ готового адаптера, и
        // отсюда он приезжает репликацией с того сервера, где его обучили. Значит на этом
        // компьютере файла может не быть в репозитории моделей вовсе, а работать он должен —
        // ради этого объекты и привязаны к серверам: обучаем на одном, используем на другом.
        // Дальше найденный файл кладёт в свой репозиторий PlaceInLorasDir
        yield return SafeCombine(_files.Abs(ObjectFiles.LoraStoreDir), Path.GetFileName(relative));
        if (projectFolder is { Length: > 0 })
        {
            yield return SafeCombine(projectFolder, relative);
        }
    }

    /// <summary>Склейка, не выпускающая наружу: «..» и абсолютный путь дают null.</summary>
    private static string? SafeCombine(string root, string relative)
    {
        var segments = relative.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .ToList();
        if (segments.Count == 0 || segments.Any(s => s == ".." || s.Contains(':')))
        {
            return null;
        }
        return Path.Combine(root, Path.Combine([.. segments]));
    }
}
