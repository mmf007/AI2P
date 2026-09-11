using AI2P.Core;
using System.Collections.Concurrent;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Манифест установки локальной модели — секция "install" профайла (ТЗ v1.40, todo36_3):
/// группа (подкаталог репозитория моделей) и список файлов с URL и точными размерами.
/// Модель без секции install ставить не нужно (облачные и уже настроенные локальные).
/// </summary>
public sealed class ModelInstallManifest
{
    /// <summary>Группа моделей — подкаталог в &lt;репозиторий&gt;/models/, например "Kandinsky-5".
    /// Файлы группы общие: энкодер, скачанный для одной модели, виден всем моделям группы.</summary>
    public string Group { get; set; } = "";

    /// <summary>Коды пакетов, необходимых модели для запуска (ТЗ v1.42): comfyui, llama.cpp …
    /// Пакеты общие для моделей: уже установленный только проверяется, повторно не ставится.</summary>
    public List<string> Packages { get; set; } = [];

    /// <summary>
    /// Коды пакетов, необходимых ОБУЧЕНИЮ адаптера LoRA (<c>lora.train.packages</c>) —
    /// тренер и Python. С T-4-S0 они ставятся ЗДЕСЬ ЖЕ, при установке модели, а не при
    /// первом запуске обучения: установка — единственное место, где человек согласился
    /// ждать, у неё есть окно с ходом работы, а обучение должно начинаться сразу.
    /// Объявлены они по-прежнему в настройке LoRA (там всё про адаптеры, T-13-S1) —
    /// сюда только собираются.
    /// </summary>
    public List<string> TrainPackages { get; set; } = [];

    /// <summary>
    /// ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ (T-190-S0) — необязательные наборы пакетов. Сегодня
    /// опция одна: «Обучение LoRA» (её пакеты — <see cref="TrainPackages"/>). Список
    /// собирается разбором профайла, а включена опция или нет — дело ЭТОГО сервера
    /// (<see cref="OptionsOff"/>): в кластере адаптеры обучают обычно на одной машине.
    /// </summary>
    public List<ModelInstallOption> Options { get; set; } = [];

    /// <summary>
    /// Коды опций, ВЫКЛЮЧЕННЫХ на этом сервере (T-190-S0). Хранится это не в профайле
    /// модели, а в config.json компьютера: профайлы переписываются сидом при подъёме
    /// SeedFileVersion, и выбор человека молча возвращался бы к «ставить всё».
    /// </summary>
    public HashSet<string> OptionsOff { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Опция включена (неизвестная — да: умолчание «ставить всё»).</summary>
    public bool OptionOn(string id) => !OptionsOff.Contains(id.Trim());

    /// <summary>
    /// Все пакеты, которые ставит установка модели: обязательные и пакеты ВКЛЮЧЁННЫХ
    /// дополнительных опций, без повторов и в объявленном порядке. Именно от этого списка
    /// считается «модель установлена» — то есть выключив «Обучение LoRA», человек получает
    /// установленную модель без тренера, а не вечное «не установлена».
    /// </summary>
    public List<string> AllPackages => Packages
        .Concat(Options.Where(o => OptionOn(o.Id)).SelectMany(o => o.Packages))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Код опции, которая привела пакет; пусто — пакет обязательный. Пакет,
    /// названный и там и там (python нужен и запуску, и тренеру), — обязательный.</summary>
    public string OptionOf(string packageId) =>
        Packages.Contains(packageId, StringComparer.OrdinalIgnoreCase)
            ? ""
            : Options.FirstOrDefault(o =>
                o.Packages.Contains(packageId, StringComparer.OrdinalIgnoreCase))?.Id ?? "";

    /// <summary>
    /// НАСТРОЙКА ОБУЧЕНИЯ АДАПТЕРА из того же профайла (<c>lora.train</c>, T-13-S1); null —
    /// профайл её не объявляет вовсе. Разбирается она здесь всё равно (из неё берутся
    /// <see cref="TrainPackages"/>), а нужна целиком для одного вопроса: ЕСТЬ ЛИ У МОДЕЛИ
    /// ТРЕНЕР (<see cref="HasTrainer"/>).
    /// </summary>
    public LoraTrain? Train { get; set; }

    /// <summary>
    /// У МОДЕЛИ ЕСТЬ ЧЕМ ОБУЧАТЬ (T-156-S0) — именно по этому признаку установка модели заводит
    /// запись плагина-тренера. Два условия, и оба обязательны: названы пакеты обучения (их и
    /// ставит установка) И объявлено, ЧЕМ запускать. Второе условие не про запас: у записей,
    /// чей тренер не поддерживает модель, обучение объявлено <c>kind: external</c> с ПУСТОЙ
    /// командой (наука 39c9cc39) — «файл человек подкладывает сам». Такой модели тренера нет,
    /// и записи плагина от неё появляться не должно.
    /// </summary>
    /// <remarks>С T-190-S0 сюда добавлено третье условие: опция «Обучение LoRA» ВКЛЮЧЕНА
    /// на этом сервере. Выключил её человек — пакетов тренера установка не ставит, и
    /// записи плагина заводить не с чего.</remarks>
    public bool HasTrainer =>
        OptionOn(ModelInstallOption.Lora)
        && TrainPackages.Count > 0 && Train is { } train
        && train.Kind != LoraTrainKinds.None
        && !(train.Kind == LoraTrainKinds.External && train.Start.Command.Trim().Length == 0);

    /// <summary>
    /// Шаблон команды запуска локального сервера модели (ТЗ v1.42): после установки
    /// подставляются пути и результат пишется в профайл модели (поле launchCommand).
    /// Плейсхолдеры: {package:&lt;id&gt;:&lt;файл&gt;}, {model:&lt;файл&gt;}, {groupDir},
    /// {modelsRepo}, {extraModelPaths}.
    /// </summary>
    public string LaunchCommand { get; set; } = "";

    public List<ManifestFile> Files { get; set; } = [];

    public long TotalSize => Files.Sum(f => f.Size);

    public sealed class ManifestFile
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>Точный размер в байтах: по нему определяется «файл готов» и докачка.</summary>
        public long Size { get; set; }
        /// <summary>Категория весов ComfyUI (ТЗ v1.41): diffusion_models / text_encoders / vae …
        /// — для генерации ai2p_extra_model_paths.yaml; пусто — в yaml не попадает.</summary>
        public string Category { get; set; } = "";
    }

    /// <summary>Разобрать манифест из JSON профайла; null — секции install нет.</summary>
    public static ModelInstallManifest? Parse(string profileJson)
    {
        using var doc = JsonDocument.Parse(profileJson);
        if (!doc.RootElement.TryGetProperty("install", out var install) ||
            install.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var manifest = new ModelInstallManifest();
        if (install.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String)
        {
            manifest.Group = g.GetString()!.Trim();
        }
        if (install.TryGetProperty("launchCommand", out var lc) && lc.ValueKind == JsonValueKind.String)
        {
            manifest.LaunchCommand = lc.GetString()!.Trim();
        }
        if (install.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
        {
            manifest.Packages = packages.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
        // пакеты обучения объявлены в настройке LoRA — читаем их тем же разбором, что и всё
        // остальное про адаптеры (T-13-S1): второе место для того же списка разошлось бы молча
        manifest.Train = LoraSettings.FromProfile(doc.RootElement).Train;
        manifest.TrainPackages = manifest.Train.Packages;
        // пакеты обучения — ДОПОЛНИТЕЛЬНАЯ опция установки (T-190-S0): для генерации по
        // готовому адаптеру они не нужны, а весят они больше самой модели
        if (manifest.TrainPackages.Count > 0)
        {
            manifest.Options.Add(new ModelInstallOption
            {
                Id = ModelInstallOption.Lora,
                Packages = manifest.TrainPackages,
            });
        }
        if (install.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in files.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var file = new ManifestFile
                {
                    Name = f.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()!.Trim() : "",
                    Url = f.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                        ? u.GetString()!.Trim() : "",
                    Size = f.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0,
                    Category = f.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()!.Trim() : "",
                };
                if (file.Name.Length > 0 && file.Url.Length > 0 && file.Size > 0)
                {
                    manifest.Files.Add(file);
                }
            }
        }
        return manifest.Files.Count > 0 || manifest.AllPackages.Count > 0 ? manifest : null;
    }
}

/// <summary>
/// НЕОБЯЗАТЕЛЬНЫЙ НАБОР ПАКЕТОВ УСТАНОВКИ (T-190-S0): флажок в окне установки модели.
/// Заказчик: «если не нужно — не ставить LoRA у модели; может быть ситуация, что LoRA
/// обучают только на одном сервере в кластере, а на остальных просто их используют».
/// </summary>
public sealed class ModelInstallOption
{
    /// <summary>Обучение адаптеров LoRA — пакеты <c>lora.train.packages</c> профайла
    /// (у Kandinsky это тренер, Python и два репозитория базовых весов) плюс запись
    /// плагина-тренера в «Плагинах и MCP» (T-156-S0).</summary>
    public const string Lora = "lora";

    public string Id { get; set; } = "";

    /// <summary>Коды пакетов справочника, которые добавляет включённая опция.</summary>
    public List<string> Packages { get; set; } = [];
}

/// <summary>
/// Установка локальных моделей в репозиторий моделей (ТЗ v1.40, todo36_3) — общий механизм
/// для всех скачиваемых моделей: статус по файлам манифеста (существование + точный размер),
/// фоновая загрузка недостающего с докачкой (HTTP Range от текущей длины файла), отмена.
/// Частично скачанные файлы (в т.ч. начатые вручную curl-ом) продолжаются с места обрыва.
/// </summary>
public sealed class ModelInstallService
{
    // веса моделей качаются десятками минут — таймаут только на подключение и чтение чанка,
    // общий таймаут HttpClient отключён
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly ILogger Logger = Log.ForContext<ModelInstallService>();

    /// <summary>Имя файла путей весов для ComfyUI в корне репозитория моделей (ТЗ v1.41).</summary>
    public const string ExtraModelPathsFile = "ai2p_extra_model_paths.yaml";

    private readonly AiModelService _models;
    private readonly FileStore _files;
    private readonly EventStore _events;
    private readonly Func<string> _repoDir;
    private readonly Func<string> _distDir;
    private readonly Func<string> _packagesDir;
    private readonly ConcurrentDictionary<string, InstallRun> _runs = new();
    /// <summary>Замки на код пакета: один и тот же пакет двух моделей ставится один раз.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PackageLocks = new();

    /// <summary>Разобранный дистрибутив пакета: имя файла, ссылка и размер (0 — неизвестен).</summary>
    private sealed record ResolvedDist(string Name, string Url, long Size);

    /// <summary>
    /// Кэш дистрибутивов пакетов (ТЗ v1.43): ссылки и размеры, выясненные запросом к серверу
    /// раздачи. Нужен, чтобы окно установки показывало размер пакета и ход его загрузки,
    /// не обращаясь к сети на каждое обновление прогресса. Живёт в экземпляре сервиса
    /// (в приложении он один) — каталоги дистрибутивов у разных экземпляров разные.
    /// </summary>
    private readonly ConcurrentDictionary<string, List<ResolvedDist>> _distCache = new();

    /// <summary>Текущая загрузка модели: прогресс для UI и токен отмены.</summary>
    private sealed class InstallRun
    {
        public CancellationTokenSource Cts { get; } = new();
        public volatile string CurrentFile = "";
        /// <summary>Код пакета, который ставится сейчас; пусто — качаются веса (ТЗ v1.43).</summary>
        public volatile string CurrentPackage = "";
        public volatile string Error = "";
        public volatile bool Finished;
    }

    /// <param name="repoDir">Репозиторий моделей (config.storage.modelsRepo).</param>
    /// <param name="distDir">Каталог дистрибутивов пакетов (config.storage.distDir);
    /// пусто/не задан — временный каталог пользователя, дистрибутивы удаляются после распаковки.</param>
    /// <param name="packagesDir">Корень установки пакетов (config.storage.packagesDir);
    /// пусто/не задан — &lt;репозиторий моделей&gt;/packages.</param>
    public ModelInstallService(AiModelService models, FileStore files, EventStore events,
        Func<string> repoDir, Func<string>? distDir = null, Func<string>? packagesDir = null)
    {
        _models = models;
        _files = files;
        _events = events;
        _repoDir = repoDir;
        _distDir = distDir ?? (() => "");
        _packagesDir = packagesDir ?? (() => "");
    }

    /// <summary>
    /// ЧТО СДЕЛАТЬ ПОСЛЕ УДАЧНОЙ УСТАНОВКИ МОДЕЛИ (T-156-S0): завести записи плагинов по её
    /// пакетам обучения (<see cref="TrainerPluginService"/>). Делегатом, а не зависимостью
    /// конструктора: записи плагинов принадлежат ОРГАНИЗАЦИИ, а установщик — этому компьютеру,
    /// и он заводится раньше сервиса плагинов (<c>OrgContext</c>). null — заводить некому,
    /// установка идёт как прежде (так её поднимают проверки).
    /// </summary>
    public Action<ModelInstallManifest>? AfterModelInstalled { get; set; }

    /// <summary>
    /// ВЫКЛЮЧЕННЫЕ ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ ЭТОЙ МОДЕЛИ (T-190-S0), «идентификатор
    /// модели → коды опций». Живёт это в config.json компьютера (о нём организация не
    /// знает — отсюда делегаты), потому что решение пер-серверное: в кластере адаптеры
    /// обучают на одной машине, а применяют на всех. null — выбора никто не хранит,
    /// ставится всё (так установку поднимают проверки).
    /// </summary>
    public Func<string, IReadOnlyList<string>>? OptionsOff { get; set; }

    /// <summary>Запомнить выключенные опции модели (пустой список — «ставить всё»).</summary>
    public Action<string, IReadOnlyList<string>>? SaveOptionsOff { get; set; }

    /// <summary>Каталог файлов группы: &lt;репозиторий&gt;/models/&lt;группа&gt;.</summary>
    public string TargetDir(ModelInstallManifest manifest) =>
        Path.Combine(_repoDir(), "models", manifest.Group);

    /// <summary>Корень установки пакетов (ТЗ v1.42): настройка либо &lt;репозиторий&gt;/packages.</summary>
    public string PackagesRoot()
    {
        var configured = _packagesDir().Trim();
        return configured.Length > 0 ? configured : Path.Combine(_repoDir(), "packages");
    }

    /// <summary>Каталог установки пакета: &lt;корень пакетов&gt;/&lt;каталог пакета&gt;.</summary>
    public string PackageDir(ModelPackage package) => Path.Combine(PackagesRoot(), package.Dir);

    /// <summary>Каталог дистрибутивов (ТЗ v1.42): настройка либо временный каталог пользователя.</summary>
    public string DistRoot()
    {
        var configured = _distDir().Trim();
        return configured.Length > 0 ? configured : Path.Combine(Path.GetTempPath(), "ai2p-dist");
    }

    /// <summary>Каталог дистрибутивов временный — скачанные архивы удаляются после распаковки.</summary>
    public bool DistIsTemporary() => _distDir().Trim().Length == 0;

    /// <summary>Справочник пакетов из файла дистрибутива models/packages.json (ТЗ v1.42).</summary>
    public ModelPackageCatalog Packages()
    {
        var abs = _files.Abs(AiModelService.PackagesPath);
        return ModelPackageCatalog.Parse(File.Exists(abs) ? File.ReadAllText(abs) : "");
    }

    /// <summary>Манифест установки из профайла модели; null — модель не ставится локально.</summary>
    public ModelInstallManifest? Manifest(string modelId)
    {
        var model = _models.Get(modelId);
        if (model is null || !File.Exists(_files.Abs(model.ProfilePath)))
        {
            return null;
        }
        try
        {
            var manifest = ModelInstallManifest.Parse(_files.ReadText(model.ProfilePath));
            // выбор человека по дополнительным опциям (T-190-S0) лежит не в профайле, а
            // в config.json этого компьютера — накладываем его на разобранный манифест
            foreach (var option in OptionsOff?.Invoke(modelId) ?? [])
            {
                manifest?.OptionsOff.Add(option);
            }
            return manifest;
        }
        catch (JsonException)
        {
            return null; // повреждённый профайл — модель считается не устанавливаемой
        }
    }

    /// <summary>
    /// ВКЛЮЧИТЬ ИЛИ ВЫКЛЮЧИТЬ ДОПОЛНИТЕЛЬНУЮ ОПЦИЮ УСТАНОВКИ (T-190-S0) — флажок окна
    /// установки. Выключенная опция не ставится и не участвует в подсчёте «модель
    /// установлена»; уже поставленные её пакеты не удаляются — они общие с другими
    /// моделями, и удаление тут значило бы «снести ComfyUI соседу».
    /// </summary>
    public ModelInstallStatusDto SetOption(string modelId, string optionId, bool on)
    {
        var manifest = Manifest(modelId)
            ?? throw new ArgumentException(Loc.T("msg.modelInstall.1"));
        var id = optionId.Trim();
        var option = manifest.Options.FirstOrDefault(o =>
                         string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ArgumentException(Loc.T("msg.modelInstall.19", id));
        if (_runs.TryGetValue(modelId, out var run) && !run.Finished)
        {
            // на ходу менять состав нельзя: пакеты уже ставятся, и «отменил галочку»
            // выглядело бы как «установка сама поставила лишнее»
            throw new InvalidOperationException(Loc.T("msg.modelInstall.20"));
        }
        var off = manifest.Options.Select(o => o.Id)
            .Where(code => string.Equals(code, option.Id, StringComparison.OrdinalIgnoreCase)
                ? !on
                : !manifest.OptionOn(code))
            .ToList();
        SaveOptionsOff?.Invoke(modelId, off);
        return Status(modelId);
    }

    /// <summary>Статусы установки всех моделей справочника, у которых есть манифест.</summary>
    public List<ModelInstallStatusDto> StatusAll() =>
        _models.List()
            .Select(m => Status(m.Id))
            .Where(s => s.HasManifest)
            .ToList();

    /// <summary>
    /// Состояние установки: по каждому файлу манифеста — есть ли на диске и сколько байт
    /// (размер совпал — готов; меньше — частично, докачаем; больше — битый, качается заново).
    /// </summary>
    public ModelInstallStatusDto Status(string modelId)
    {
        var manifest = Manifest(modelId);
        if (manifest is null)
        {
            return new ModelInstallStatusDto { ModelId = modelId };
        }
        var dir = TargetDir(manifest);
        var status = new ModelInstallStatusDto
        {
            ModelId = modelId,
            HasManifest = true,
            TargetDir = dir,
            TotalSize = manifest.TotalSize,
        };
        foreach (var file in manifest.Files)
        {
            var onDisk = LengthOf(Path.Combine(dir, file.Name));
            var done = Math.Clamp(onDisk, 0, file.Size);
            status.Files.Add(new ModelInstallFileDto
            {
                Name = file.Name,
                Size = file.Size,
                Downloaded = done,
                State = onDisk == file.Size ? "done" : onDisk > 0 ? "partial" : "missing",
            });
            status.DownloadedSize += done;
        }
        // пакеты, необходимые для запуска (ТЗ v1.42) и для обучения адаптера (T-4-S0):
        // ComfyUI, llama.cpp, musubi-tuner, Python …
        _runs.TryGetValue(modelId, out var run);
        var catalog = Packages();
        foreach (var id in manifest.AllPackages)
        {
            var option = manifest.OptionOf(id);
            var package = catalog.Find(id);
            if (package is null)
            {
                status.Packages.Add(new ModelPackageStatusDto
                {
                    Id = id, Name = id, Unknown = true, Option = option,
                });
                continue;
            }
            var dto = PackageDto(package, run);
            dto.Option = option;
            status.Packages.Add(dto);
        }
        // дополнительные опции (T-190-S0): флажки окна установки. Отдаются ВСЕ, в том числе
        // выключенные, — иначе выключенную опцию нечем было бы включить обратно
        foreach (var option in manifest.Options)
        {
            status.Options.Add(new ModelInstallOptionDto
            {
                Id = option.Id,
                Enabled = manifest.OptionOn(option.Id),
                Packages = option.Packages.Count,
            });
        }
        // «установлена» = все файлы весов на месте и все нужные пакеты стоят
        status.Installed = status.Files.All(f => f.State == "done")
                           && status.Packages.All(p => p.Installed);
        // команда запуска могла не сформироваться: файлы и пакеты появились не через
        // наш установщик (скачаны руками, поставлены раньше) — тогда DownloadAllAsync
        // не отрабатывал и профайл остался без launchCommand (todo37_3). Проверяем это
        // при каждом обращении к состоянию — в том числе по кнопке «Обновить».
        status.LaunchCommand = status.Installed
            ? EnsureLaunchCommand(modelId, manifest)
            : LaunchCommandOf(modelId);
        if (run is not null)
        {
            status.Running = !run.Finished;
            status.CurrentFile = run.CurrentFile;
            status.Error = run.Error;
        }
        return status;
    }

    /// <summary>
    /// СТРОКА ПАКЕТА ДЛЯ ОКНА УСТАНОВКИ: установлен ли, где лежит, размер дистрибутива и
    /// сколько его уже скачано. Считается одинаково у пакета модели и у пакета плагина
    /// (T-136-S0) — иначе окно установки плагина показывало бы «то же самое, но иначе».
    /// </summary>
    /// <param name="run">Идущая установка, к которой относится пакет; null — не идёт.</param>
    private ModelPackageStatusDto PackageDto(ModelPackage package, InstallRun? run)
    {
        // пакет мог уже стоять на этом компьютере (Python, Blender): тогда каталог пакета
        // пуст, а показать надо то, чем мы будем пользоваться, — найденную программу
        var system = SystemFile(package);
        var installed = (system is not null || FindInPackage(package, package.Check) is not null)
                        && SetupDone(package);
        // размер дистрибутивов и скачанное (ТЗ v1.43): размер известен после запроса
        // к серверу раздачи (ResolveSizesAsync), скачанное — по файлам в каталоге дистрибутивов
        var (size, downloaded) = DistProgress(package);
        return new ModelPackageStatusDto
        {
            Id = package.Id,
            Name = package.Name.Length > 0 ? package.Name : package.Id,
            Dir = system ?? PackageDir(package),
            Hint = package.Hint,
            Installed = installed,
            System = system is not null,
            Size = size,
            Downloaded = installed && size > 0 ? size : downloaded,
            State = installed ? "done" : downloaded > 0 ? "partial" : "missing",
            Running = run is { Finished: false } && run.CurrentPackage == package.Id,
        };
    }

    /// <summary>
    /// Размер дистрибутивов пакета и сколько из них уже лежит в каталоге дистрибутивов
    /// (ТЗ v1.43): размер берётся из разбора последнего запроса к серверу раздачи
    /// (см. <see cref="ResolveSizesAsync"/>); неизвестен — 0.
    /// </summary>
    private (long Size, long Downloaded) DistProgress(ModelPackage package)
    {
        if (!_distCache.TryGetValue(DistCacheKey(package), out var dists))
        {
            return (0, 0);
        }
        var distDir = DistRoot();
        long size = 0;
        long downloaded = 0;
        foreach (var dist in dists)
        {
            size += dist.Size;
            var onDisk = LengthOf(Path.Combine(distDir, dist.Name));
            downloaded += dist.Size > 0 ? Math.Clamp(onDisk, 0, dist.Size) : onDisk;
        }
        return (size, downloaded);
    }

    /// <summary>
    /// Выяснить размеры дистрибутивов пакетов модели (ТЗ v1.43, todo36_6): один запрос к
    /// серверу раздачи на файл (ассет релиза GitHub либо HEAD по прямой ссылке); результат
    /// кэшируется в памяти и дальше показывается в окне установки без обращений к сети.
    /// Ошибка сети не мешает установке — размер просто останется неизвестным.
    /// </summary>
    public async Task<ModelInstallStatusDto> ResolveSizesAsync(string modelId, CancellationToken ct = default)
    {
        var manifest = Manifest(modelId);
        if (manifest is not null)
        {
            var catalog = Packages();
            var flavor = BuildFlavor();
            foreach (var package in manifest.AllPackages.Select(catalog.Find).OfType<ModelPackage>())
            {
                if (_distCache.ContainsKey(DistCacheKey(package)))
                {
                    continue; // уже выясняли
                }
                try
                {
                    var dists = new List<ResolvedDist>();
                    foreach (var file in package.Files.Where(f => f.MatchesFlavor(flavor)))
                    {
                        var (url, name, size) = await ResolveDistAsync(file, flavor, ct);
                        if (size <= 0)
                        {
                            size = await RemoteLengthAsync(url, ct);
                        }
                        dists.Add(new ResolvedDist(name, url, size));
                    }
                    _distCache[DistCacheKey(package)] = dists;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                               or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    Logger.Warning(ex, "Не удалось выяснить размер пакета {Package}", package.Id);
                }
            }
        }
        return Status(modelId);
    }

    /// <summary>Ключ кэша дистрибутивов: пакет + сборка под железо (у llama.cpp они разные).</summary>
    private static string DistCacheKey(ModelPackage package) => package.Id + "|" + BuildFlavor();

    /// <summary>
    /// Запустить загрузку недостающих файлов в фоне; уже идущая загрузка не дублируется.
    /// Возвращает состояние на момент запуска.
    /// </summary>
    public ModelInstallStatusDto Start(string modelId)
    {
        var manifest = Manifest(modelId)
            ?? throw new ArgumentException(Loc.T("msg.modelInstall.1"));
        var run = new InstallRun();
        var current = _runs.AddOrUpdate(modelId,
            run,
            (_, existing) => existing.Finished ? run : existing);
        if (current == run)
        {
            _events.Append(new EventRecord
            {
                EventType = EventTypes.ModelInstallStarted,
                EntityType = "ai_model",
                EntityId = modelId,
                PayloadJson = JsonSerializer.Serialize(new { group = manifest.Group, totalSize = manifest.TotalSize }),
            });
            _ = Task.Run(() => DownloadAllAsync(modelId, manifest, run));
        }
        return Status(modelId);
    }

    /// <summary>Прервать идущую загрузку; скачанное остаётся — продолжится докачкой.</summary>
    public ModelInstallStatusDto Cancel(string modelId)
    {
        if (_runs.TryGetValue(modelId, out var run) && !run.Finished)
        {
            run.Cts.Cancel();
        }
        return Status(modelId);
    }

    private async Task DownloadAllAsync(string modelId, ModelInstallManifest manifest, InstallRun run)
    {
        var dir = TargetDir(manifest);
        try
        {
            // сначала пакеты, необходимые для запуска (ТЗ v1.42): без них модель не работает,
            // а качать 20 ГБ весов, чтобы потом упереться в отсутствующий ComfyUI, бессмысленно
            await InstallPackagesAsync(manifest, run);
            Directory.CreateDirectory(dir);
            foreach (var file in manifest.Files)
            {
                run.CurrentFile = file.Name;
                await DownloadFileAsync(Path.Combine(dir, file.Name), file, run.Cts.Token);
            }
            run.CurrentFile = "";
            // донастройка после установки (ТЗ v1.41, todo36_4): файл путей весов для ComfyUI
            // и автоматическая активация модели — теперь её можно выбирать исполнителям
            WriteExtraModelPaths();
            // команда запуска локального сервера модели по шаблону манифеста (ТЗ v1.42)
            WriteLaunchCommand(modelId, manifest);
            ActivateInstalled(modelId);
            // ЗАПИСЬ ПЛАГИНА-ТРЕНЕРА (T-156-S0): установка модели поставила пакеты обучения —
            // значит на этом компьютере появилась программа, и в списке «Плагины и MCP» ей
            // положена запись. Отказ здесь установку не роняет: модель уже установлена, а
            // запись плагина человек заведёт кнопкой «Инициализировать»
            try
            {
                AfterModelInstalled?.Invoke(manifest);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Модель {ModelId}: запись плагина по пакетам обучения не заведена",
                    modelId);
            }
            _events.Append(new EventRecord
            {
                EventType = EventTypes.ModelInstallFinished,
                EntityType = "ai_model",
                EntityId = modelId,
                PayloadJson = JsonSerializer.Serialize(new { group = manifest.Group }),
            });
            Logger.Information("Установка модели {ModelId} завершена: {Dir}", modelId, dir);
        }
        catch (OperationCanceledException)
        {
            run.Error = ""; // отмена пользователем — не ошибка, докачается при следующем запуске
            Logger.Information("Загрузка модели {ModelId} прервана пользователем", modelId);
        }
        catch (Exception ex)
        {
            run.Error = ex.Message;
            _events.Append(new EventRecord
            {
                EventType = EventTypes.ModelInstallFailed,
                EntityType = "ai_model",
                EntityId = modelId,
                PayloadJson = JsonSerializer.Serialize(new { file = run.CurrentFile, error = ex.Message }),
            });
            Logger.Warning(ex, "Ошибка загрузки модели {ModelId}, файл {File}", modelId, run.CurrentFile);
        }
        finally
        {
            run.Finished = true;
        }
    }

    /// <summary>
    /// Скачать один файл с докачкой: файл готов — пропуск; частичный — Range от текущей длины;
    /// длиннее ожидаемого (битый) — заново. После загрузки размер сверяется с манифестом.
    /// </summary>
    private static async Task DownloadFileAsync(string path, ModelInstallManifest.ManifestFile file,
        CancellationToken ct)
    {
        var existing = LengthOf(path);
        if (existing == file.Size)
        {
            return; // уже скачан (в т.ч. вручную) — помечается готовым без перекачивания
        }
        if (existing > file.Size)
        {
            File.Delete(path);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (existing > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        }
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.2", file.Name, (int)response.StatusCode, response.ReasonPhrase));
        }
        // сервер не умеет Range (ответ 200 вместо 206) — файл пишется с нуля
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;

        await using (var target = new FileStream(path, append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write, FileShare.Read))
        {
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await source.CopyToAsync(target, 1 << 20, ct);
        }

        var final = LengthOf(path);
        if (final != file.Size)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.3", file.Name, final, file.Size));
        }
    }

    // --- пакеты, необходимые модели для запуска (ТЗ v1.42, todo36_5) ---

    /// <summary>
    /// Пакет установлен: файл-признак (check) есть в каталоге пакета ЛИБО программа пакета
    /// уже стоит в системе (T-4-S0 — так проверяется Python). Имя без разделителей ищется
    /// рекурсивно — архивы кладут внутрь свой корневой каталог, его имя меняется от версии
    /// к версии. Пакет без признака считается установленным по наличию каталога.
    /// </summary>
    public bool IsPackageInstalled(ModelPackage package) =>
        FindInPackage(package, package.Check) is not null && SetupDone(package);

    /// <summary>
    /// НАСТРОЙКА ПАКЕТА СДЕЛАНА (T-185-S0): у пакета либо нет блока setup, либо на месте его
    /// файл-признак. Пакет с неготовым окружением обязан считаться НЕустановленным: иначе
    /// «Установить» больше не предложат, а обучение упадёт на импорте torch через час.
    /// </summary>
    public bool SetupDone(ModelPackage package)
    {
        if (package.Setup is not { Steps.Count: > 0 } setup || setup.Check.Length == 0)
        {
            return true;
        }
        var dir = SetupDir(package);
        if (dir.Length == 0)
        {
            return false; // рабочий каталог не опознан — распаковка ещё не кончилась
        }
        var path = Path.Combine(dir, setup.Check.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>
    /// Рабочий каталог шагов настройки: объявленный путь (плейсхолдеры разворачиваются),
    /// а если он указывает на ФАЙЛ — каталог этого файла; пусто — каталог пакета.
    /// Пустой ответ означает «каталог ещё не опознан» (файла-ориентира нет на диске).
    /// </summary>
    private string SetupDir(ModelPackage package)
    {
        var declared = package.Setup?.WorkDir ?? "";
        if (declared.Length == 0)
        {
            return PackageDir(package);
        }
        string path;
        try
        {
            path = ExpandPackages(declared);
        }
        catch (InvalidOperationException)
        {
            return ""; // ориентир ещё не распакован
        }
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(PackageDir(package), path.Replace('/', Path.DirectorySeparatorChar));
        }
        return Directory.Exists(path) ? path
            : File.Exists(path) ? Path.GetDirectoryName(path) ?? ""
            : "";
    }

    /// <summary>
    /// Путь файла внутри каталога пакета: относительный путь — как есть, просто имя —
    /// поиск по всему каталогу (берётся самый верхний по вложенности). Не нашлось в
    /// каталоге — отвечает УСТАНОВЛЕННАЯ В СИСТЕМЕ программа пакета (T-4-S0): для
    /// {package:python:python.exe} важно не «где лежит наш экземпляр», а «чем запускать».
    /// null — не найден.
    /// </summary>
    public string? FindInPackage(ModelPackage package, string file)
    {
        return InDir() ?? SystemFile(package, file);

        string? InDir()
        {
            var dir = PackageDir(package);
            if (!Directory.Exists(dir))
            {
                return null;
            }
            if (file.Trim().Length == 0)
            {
                return dir; // признака нет — достаточно каталога
            }
            if (file.Contains('/') || file.Contains('\\'))
            {
                var direct = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(direct) || Directory.Exists(direct) ? direct : null;
            }
            return ShallowestFile(dir, file);
        }
    }

    /// <summary>
    /// Каталоги, в которые при поиске файла-признака НЕ заходим (T-137-S0). Тренер LoRA
    /// создаёт себе окружение с torch прямо в каталоге пакета (&lt;корень тренера&gt;/.venv) —
    /// это десятки тысяч файлов с очень длинными именами. Обход по ним стоит секунды на
    /// каждый вопрос «пакет установлен?» (а его задают раз в секунду, пока открыто окно
    /// установки), а один путь длиннее 260 знаков ронял весь обход исключением — и пакет
    /// молча объявлялся НЕустановленным. Искомого признака в окружении не бывает никогда.
    /// </summary>
    private static readonly string[] SkipDirs =
        [".venv", "venv", "env", "__pycache__", ".git", "node_modules", "site-packages"];

    /// <summary>
    /// Самый верхний по вложенности файл с таким именем: обход в ШИРИНУ, первый найденный
    /// и есть ответ — полного перечисления дерева не делается вовсе. Недоступный каталог
    /// пропускается, а не роняет поиск: «сюда не пустили» это не «файла нет».
    /// </summary>
    private static string? ShallowestFile(string root, string file)
    {
        var level = new List<string> { root };
        for (var depth = 0; depth < 16 && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var dir in level)
            {
                try
                {
                    var hit = Directory.EnumerateFiles(dir, file).FirstOrDefault();
                    if (hit is not null)
                    {
                        return hit;
                    }
                    next.AddRange(Directory.EnumerateDirectories(dir)
                        .Where(d => !SkipDirs.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or ArgumentException or NotSupportedException)
                {
                    // каталог не читается — идём дальше по соседям
                }
            }
            level = next;
        }
        return null;
    }

    // --- пакет, уже установленный на этом компьютере (T-4-S0) ---

    /// <summary>
    /// Где искать установленные в системе программы; null — переменная окружения PATH.
    /// Своё значение подставляют проверки: правка PATH процесса задела бы соседние тесты
    /// (его же читает <see cref="BuildFlavor"/>).
    /// </summary>
    public Func<string>? SystemSearchPath { get; set; }

    /// <summary>
    /// Сколько живёт ответ «стоит ли пакет в системе». Спрашивают его на каждое обновление
    /// окна установки (раз в секунду), а стоит он запуска программы с «--version» — но и
    /// вечным быть не должен: человек ставит Python и жмёт «Обновить», не перезапуская AI2P.
    /// </summary>
    private static readonly TimeSpan SystemProbeTtl = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (DateTime When, string? Path)> _systemCache = new();

    /// <summary>Программа пакета, уже установленная в системе; null — пакет так не ищется
    /// или не нашёлся (в том числе когда версия не годится).</summary>
    public string? SystemFile(ModelPackage package)
    {
        if (package.System is not { Commands.Count: > 0 } probe)
        {
            return null;
        }
        var key = package.Id + "|" + (SystemSearchPath?.Invoke() ?? "");
        if (_systemCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.When < SystemProbeTtl)
        {
            return cached.Path;
        }
        var path = SystemPackageProbe.Find(probe,
            SystemSearchPath?.Invoke() ?? Environment.GetEnvironmentVariable("PATH") ?? "");
        var known = _systemCache.TryGetValue(key, out var previous);
        _systemCache[key] = (DateTime.UtcNow, path);
        // в журнал — только НОВОСТЬ: ответ протухает раз в минуту, а окно установки
        // спрашивает его каждую секунду, и одна и та же строка залила бы весь журнал
        if (path is not null && (!known || previous.Path != path))
        {
            Logger.Information("Пакет {Package} уже установлен в системе: {Path}", package.Id, path);
        }
        return path;
    }

    /// <summary>Системная программа пакета, когда спрашивают ИМЕННО её файл: имя без
    /// расширения совпадает с командой пакета либо с его признаком установленности.</summary>
    private string? SystemFile(ModelPackage package, string file)
    {
        if (package.System is not { Commands.Count: > 0 } probe)
        {
            return null;
        }
        var wanted = Path.GetFileNameWithoutExtension(file.Trim());
        var names = probe.Commands.Append(package.Check)
            .Select(n => Path.GetFileNameWithoutExtension(n.Trim()));
        return wanted.Length > 0 && names.Contains(wanted, StringComparer.OrdinalIgnoreCase)
            ? SystemFile(package)
            : null;
    }

    /// <summary>
    /// Установить пакеты манифеста: уже установленный пропускается целиком; иначе дистрибутивы
    /// качаются в каталог дистрибутивов (готовый файл не перекачивается) и распаковываются
    /// в каталог пакета. Один и тот же пакет двух моделей ставится один раз (замок по коду).
    /// </summary>
    private Task InstallPackagesAsync(ModelInstallManifest manifest, InstallRun run) =>
        InstallPackagesAsync(manifest.AllPackages, run);

    /// <summary>
    /// ЧЕГО НЕ ХВАТАЕТ ДЛЯ ОБУЧЕНИЯ (T-4-S0): названия пакетов из списка, которые на этом
    /// сервере не установлены. Ставить их обучение больше не берётся — их ставит установка
    /// модели, — но спросить перед запуском обязано: обучение идёт часами, и «не нашёлся
    /// тренер» человек должен узнать в момент нажатия кнопки, а не в артефакте ошибки.
    /// Пустой ответ — всё на месте.
    /// </summary>
    public List<string> MissingPackages(IReadOnlyList<string> ids)
    {
        var catalog = Packages();
        var missing = new List<string>();
        foreach (var id in ids)
        {
            var package = catalog.Find(id);
            if (package is null)
            {
                missing.Add(id);    // пакета нет в справочнике — назвать его всё равно надо
            }
            else if (!IsPackageInstalled(package))
            {
                missing.Add(package.Name.Length > 0 ? package.Name : package.Id);
            }
        }
        return missing;
    }

    // --- ОДИН ПАКЕТ СПРАВОЧНИКА БЕЗ МОДЕЛИ (плагины: T-114-S0, окно установки — T-136-S0) ---
    //
    // Механика та же, что у пакетов модели: замок на код пакета общий, уже установленный
    // пакет пропускается, ход загрузки виден по каталогу дистрибутивов. Отличие ровно одно —
    // прогон заводится не на модель, а НА ПАКЕТ: у плагина модели нет вовсе, а окну установки
    // нужен тот же прогресс, что у модели (заказчик так и просил — «как у моделей»).

    /// <summary>Ключ прогона установки одного пакета: моделей с таким идентификатором нет.</summary>
    private static string PackageRunKey(string packageId) => "package:" + packageId.Trim();

    /// <summary>Состояние одного пакета справочника; неизвестный пакет отдаётся с
    /// <see cref="ModelPackageStatusDto.Unknown"/> — так же, как в окне модели.</summary>
    public ModelPackageStatusDto PackageStatus(string packageId)
    {
        var id = packageId.Trim();
        var package = Packages().Find(id);
        if (package is null)
        {
            return new ModelPackageStatusDto { Id = id, Name = id, Unknown = true };
        }
        _runs.TryGetValue(PackageRunKey(id), out var run);
        return PackageDto(package, run);
    }

    /// <summary>Что сейчас с установкой пакета: идёт ли, какой файл качается, чем кончилось.</summary>
    public (bool Running, string CurrentFile, string Error) PackageRunState(string packageId) =>
        _runs.TryGetValue(PackageRunKey(packageId), out var run)
            ? (!run.Finished, run.CurrentFile, run.Error)
            : (false, "", "");

    /// <summary>
    /// ПОСТАВИТЬ ОДИН ПАКЕТ В ФОНЕ и сразу вернуть состояние: окно установки опрашивает
    /// прогресс, как у модели. Уже идущая установка того же пакета не дублируется, отказ
    /// не бросается наружу (запрос уже ответил), а ложится в <see cref="PackageRunState"/> —
    /// иначе ошибка установки исчезла бы вместе с фоновой задачей.
    /// </summary>
    public ModelPackageStatusDto StartPackageInstall(string packageId)
    {
        var id = packageId.Trim();
        if (Packages().Find(id) is null)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.4", id, AiModelService.PackagesPath));
        }
        var run = new InstallRun();
        var current = _runs.AddOrUpdate(PackageRunKey(id), run,
            (_, existing) => existing.Finished ? run : existing);
        if (current == run)
        {
            _ = Task.Run(() => InstallOnePackageAsync(id, run));
        }
        return PackageStatus(id);
    }

    /// <summary>Прервать идущую установку пакета; скачанное остаётся — продолжится докачкой.</summary>
    public ModelPackageStatusDto CancelPackageInstall(string packageId)
    {
        if (_runs.TryGetValue(PackageRunKey(packageId), out var run) && !run.Finished)
        {
            run.Cts.Cancel();
        }
        return PackageStatus(packageId);
    }

    /// <summary>Выяснить размер дистрибутива пакета запросом к серверу раздачи (ТЗ v1.43):
    /// без него окно показывает «—» вместо размера. Ошибка сети установке не мешает.</summary>
    public async Task<ModelPackageStatusDto> ResolvePackageSizeAsync(string packageId,
        CancellationToken ct = default)
    {
        var package = Packages().Find(packageId.Trim());
        if (package is not null && !_distCache.ContainsKey(DistCacheKey(package)))
        {
            try
            {
                var flavor = BuildFlavor();
                var dists = new List<ResolvedDist>();
                foreach (var file in package.Files.Where(f => f.MatchesFlavor(flavor)))
                {
                    var (url, name, size) = await ResolveDistAsync(file, flavor, ct);
                    if (size <= 0)
                    {
                        size = await RemoteLengthAsync(url, ct);
                    }
                    dists.Add(new ResolvedDist(name, url, size));
                }
                _distCache[DistCacheKey(package)] = dists;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                           or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warning(ex, "Не удалось выяснить размер пакета {Package}", package.Id);
            }
        }
        return PackageStatus(packageId);
    }

    private async Task InstallOnePackageAsync(string packageId, InstallRun run)
    {
        try
        {
            await InstallPackagesAsync([packageId], run);
        }
        catch (OperationCanceledException)
        {
            run.Error = ""; // отмена человеком — не ошибка, докачается при следующем запуске
        }
        catch (Exception ex)
        {
            run.Error = ex.Message;
            Logger.Warning(ex, "Ошибка установки пакета {Package}", packageId);
        }
        finally
        {
            run.Finished = true;
        }
    }

    private async Task InstallPackagesAsync(IReadOnlyList<string> ids, InstallRun run)
    {
        if (ids.Count == 0)
        {
            return;
        }
        var catalog = Packages();
        foreach (var id in ids)
        {
            var package = catalog.Find(id)
                ?? throw new InvalidOperationException(
                    Loc.T("msg.modelInstall.4", id, AiModelService.PackagesPath));
            if (!package.MatchesOs())
            {
                throw new InvalidOperationException(
                    Loc.T("msg.modelInstall.5", package.Id) + package.Hint);
            }
            var gate = PackageLocks.GetOrAdd(package.Id, _ => new SemaphoreSlim(1, 1));
            run.CurrentPackage = package.Id; // окно установки показывает, какой пакет ставится
            await gate.WaitAsync(run.Cts.Token);
            try
            {
                if (IsPackageInstalled(package))
                {
                    Logger.Information("Пакет {Package} уже установлен — пропуск", package.Id);
                    continue;
                }
                await InstallPackageAsync(package, run);
            }
            finally
            {
                gate.Release();
                run.CurrentPackage = "";
            }
        }
    }

    private async Task InstallPackageAsync(ModelPackage package, InstallRun run)
    {
        var targetDir = PackageDir(package);
        var distDir = DistRoot();
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(distDir);
        var flavor = BuildFlavor();

        // разбор ссылок и размеров — сразу все, чтобы окно установки показывало размер пакета
        // и ход загрузки с первой же секунды (ТЗ v1.43)
        var dists = new List<ResolvedDist>();
        foreach (var file in package.Files.Where(f => f.MatchesFlavor(flavor)))
        {
            var (url, name, size) = await ResolveDistAsync(file, flavor, run.Cts.Token);
            if (size <= 0)
            {
                size = await RemoteLengthAsync(url, run.Cts.Token);
            }
            dists.Add(new ResolvedDist(name, url, size));
        }
        _distCache[DistCacheKey(package)] = dists;

        foreach (var (file, dist) in package.Files.Where(f => f.MatchesFlavor(flavor)).Zip(dists))
        {
            var distPath = Path.Combine(distDir, dist.Name);
            run.CurrentFile = dist.Name;
            // ФАЙЛ УЖЕ НА МЕСТЕ (T-185-S0): дистрибутив, который кладётся как есть, из
            // временного каталога ПЕРЕНОСИТСЯ — значит после обрыва на десятом файле из
            // четырнадцати его в каталоге дистрибутивов уже нет, и без этой проверки
            // повтор установки качал бы все 16 ГБ заново
            if (file.Unpack.Length == 0 && dist.Size > 0
                && LengthOf(Path.Combine(targetDir, Path.GetFileName(dist.Name))) == dist.Size)
            {
                continue;
            }
            await DownloadDistAsync(distPath, dist.Url, dist.Size, run.Cts.Token);
            // файл, который кладётся как есть (веса HuggingFace — T-185-S0), из ВРЕМЕННОГО
            // каталога переносится, а не копируется: на 18 ГБ копия это лишние минуты и
            // лишние 18 ГБ на диске
            Unpack(distPath, targetDir, file.Unpack, package,
                move: DistIsTemporary() && file.Unpack.Length == 0);
            // временный каталог дистрибутивов чистим за собой; указанный пользователем — нет
            if (DistIsTemporary() && file.Unpack.Length > 0)
            {
                TryDelete(distPath);
            }
        }
        run.CurrentFile = "";

        if (FindInPackage(package, package.Check) is null)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.6", package.Name, targetDir, package.Check, package.Hint));
        }
        await RunSetupAsync(package, run);
        Logger.Information("Пакет {Package} установлен: {Dir}", package.Id, targetDir);
    }

    /// <summary>
    /// НАСТРОЙКА ПАКЕТА ПОСЛЕ РАСПАКОВКИ (T-185-S0) — шаги блока setup по порядку: окружение
    /// Python, torch, зависимости тренера из его файла требований. Идёт это в установке
    /// пакета, а не в первом запуске обучения: получасовая установка, начавшаяся по кнопке
    /// «Обучить», выглядит зависанием и обрывается тайм-аутом молчания (T-313).
    /// Вывод программ пишется в журнал каталога пакета и вычитывается непрерывно
    /// (<see cref="ProcessOutputLog"/>): pip печатает больше, чем помещается в трубу.
    /// </summary>
    private async Task RunSetupAsync(ModelPackage package, InstallRun run)
    {
        if (package.Setup is not { Steps.Count: > 0 } setup || SetupDone(package))
        {
            return;
        }
        var workDir = SetupDir(package);
        if (workDir.Length == 0)
        {
            workDir = PackageDir(package);
        }
        var idle = TimeSpan.FromMinutes(setup.IdleMinutes > 0 ? setup.IdleMinutes : 30);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(run.Cts.Token);
        limit.CancelAfter(TimeSpan.FromMinutes(setup.TimeoutMinutes > 0 ? setup.TimeoutMinutes : 120));
        // журнал ОДИН на всю настройку: у каждого шага свой файл затирал бы предыдущий,
        // а причина неудачи четвёртого шага обычно видна в выводе третьего
        using var log = new ProcessOutputLog(Path.Combine(PackageDir(package), "ai2p-setup.log"));

        foreach (var step in setup.Steps)
        {
            var exe = ExpandPackages(step.Run.Replace("{dir}", PackageDir(package)));
            if (!Path.IsPathRooted(exe))
            {
                exe = Path.Combine(workDir, exe.Replace('/', Path.DirectorySeparatorChar));
            }
            var args = ExpandPackages(step.Args.Replace("{dir}", PackageDir(package)));
            var title = step.Name.Length > 0 ? step.Name : Path.GetFileName(exe);
            run.CurrentFile = title;
            Logger.Information("Пакет {Package}: настройка — {Step} ({Exe} {Args})",
                package.Id, title, exe, args);

            log.Line($"[AI2P] {package.Id}: {title}");
            log.Line($"[AI2P] {exe} {args}");
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // pip и python копят перенаправленный вывод блоками: без этого «молчание»
            // считается там, где программа на самом деле печатает (T-152-S0)
            process.StartInfo.Environment["PYTHONUNBUFFERED"] = "1";
            process.StartInfo.Environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
            try
            {
                process.Start();
                log.Attach(process);
                var finished = await log.WaitForExitAsync(process, idle, limit.Token);
                if (!finished)
                {
                    TryKill(process);
                    throw new InvalidOperationException(Loc.T("msg.modelInstall.18",
                        package.Name.Length > 0 ? package.Name : package.Id, title,
                        idle.TotalMinutes, log.FilePath));
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(Loc.T("msg.modelInstall.17",
                        package.Name.Length > 0 ? package.Name : package.Id, title,
                        process.ExitCode, log.Tail()));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!step.Optional)
                {
                    throw;
                }
                Logger.Warning(ex, "Пакет {Package}: необязательный шаг {Step} не удался",
                    package.Id, title);
            }
        }
        run.CurrentFile = "";
        if (!SetupDone(package))
        {
            throw new InvalidOperationException(Loc.T("msg.modelInstall.6",
                package.Name, workDir, setup.Check, package.Hint));
        }
        Logger.Information("Пакет {Package}: окружение готово ({Dir})", package.Id, workDir);
    }

    /// <summary>Снять зависший шаг настройки вместе с потомками; неудача не важна.</summary>
    private static void TryKill(System.Diagnostics.Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // процесс уже кончился сам
        }
    }

    /// <summary>
    /// Ссылка на дистрибутив: прямой URL либо ассет последнего релиза GitHub по маске
    /// (в маске {flavor} — сборка под железо). Возвращает URL, имя файла и размер (0 — неизвестен).
    /// </summary>
    private static async Task<(string Url, string Name, long Size)> ResolveDistAsync(
        ModelPackage.PackageFile file, string flavor, CancellationToken ct)
    {
        if (file.Url.Length > 0)
        {
            var name = file.Name.Length > 0
                ? file.Name
                : Path.GetFileName(new Uri(file.Url).LocalPath);
            // названный в справочнике размер избавляет от запроса HEAD на каждый файл (T-185-S0)
            return (file.Url, name, file.Size);
        }

        var pattern = file.AssetPattern.Replace("{flavor}", flavor);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{file.GitHubRepo}/releases/latest");
        request.Headers.UserAgent.ParseAdd("AI2P");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.7", file.GitHubRepo, (int)response.StatusCode, response.ReasonPhrase));
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(Loc.T("msg.modelInstall.8", file.GitHubRepo));
        }
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()! : "";
            if (name.Length == 0 || !MatchesMask(name, pattern))
            {
                continue;
            }
            var url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()! : "";
            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var value) ? value : 0;
            if (url.Length > 0)
            {
                return (url, name, size);
            }
        }
        throw new InvalidOperationException(
            Loc.T("msg.modelInstall.9", file.GitHubRepo, pattern));
    }

    /// <summary>Сопоставление имени файла с маской вида "llama-*-bin-win-cpu-x64.zip".</summary>
    private static bool MatchesMask(string name, string mask)
    {
        var regex = "^" + string.Join(".*",
            mask.Split('*').Select(System.Text.RegularExpressions.Regex.Escape)) + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Скачать дистрибутив пакета с докачкой. Готовый файл (совпал размер, а при неизвестном
    /// размере — просто есть) не перекачивается — требование «сначала проверить, нет ли уже
    /// скачанного дистрибутива» (ТЗ v1.42).
    /// </summary>
    private static async Task DownloadDistAsync(string path, string url, long expectedSize,
        CancellationToken ct)
    {
        // имя дистрибутива может содержать подкаталог: снимок репозитория HuggingFace кладётся
        // в свой каталог, иначе config.json двух разных пакетов сталкивались бы (T-185-S0)
        var parent = Path.GetDirectoryName(path);
        if (parent is { Length: > 0 })
        {
            Directory.CreateDirectory(parent);
        }
        var existing = LengthOf(path);
        if (existing > 0 && expectedSize <= 0)
        {
            // размер заранее неизвестен (ссылка на «последний релиз») — спрашиваем у сервера
            expectedSize = await RemoteLengthAsync(url, ct);
            if (expectedSize <= 0)
            {
                Logger.Information("Дистрибутив уже скачан, размер не проверить: {Path}", path);
                return;
            }
        }
        if (existing > 0 && existing == expectedSize)
        {
            Logger.Information("Дистрибутив уже скачан — пропуск загрузки: {Path}", path);
            return;
        }
        if (expectedSize > 0 && existing > expectedSize)
        {
            File.Delete(path);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        }
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.modelInstall.2", Path.GetFileName(path), (int)response.StatusCode, response.ReasonPhrase));
        }
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        await using (var target = new FileStream(path, append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write, FileShare.Read))
        {
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await source.CopyToAsync(target, 1 << 20, ct);
        }
        Logger.Information("Дистрибутив скачан: {Path}", path);
    }

    /// <summary>Размер файла на сервере (HEAD); 0 — узнать не удалось.</summary>
    private static async Task<long> RemoteLengthAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength ?? 0 : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return 0;
        }
    }

    /// <summary>
    /// Распаковать дистрибутив в каталог пакета. Порядок с T-292: СНАЧАЛА средствами .NET
    /// (<see cref="ArchiveExtractor"/> — zip, gzip, tar, tar.gz), и только то, чего в рантайме
    /// нет вовсе (7z, xz, bzip2, zstd), отдаётся внешним утилитам: сначала tar (в Windows 10/11
    /// системный bsdtar читает и 7z), затем 7-Zip — и ищется он не только в PATH, куда себя не
    /// прописывает вовсе, но и в обычных местах установки. Отсюда и была жалоба «потребовался
    /// внешний архиватор»: имя «tar» из PATH нередко приводит к GNU tar (Git for Windows, MSYS),
    /// а он про 7z не знает и отвечает кодом 128 — системный bsdtar при этом стоит на месте.
    /// Вид архива определяется по СОДЕРЖИМОМУ: объявление unpack в справочнике пакетов легко
    /// расходится с тем, что на самом деле лежит в ассете релиза.
    /// Пустой вид распаковки — файл просто копируется в каталог пакета.
    /// </summary>
    private static void Unpack(string distPath, string targetDir, string kind, ModelPackage package,
        bool move = false)
    {
        var name = Path.GetFileName(distPath);
        if (kind.Length == 0)
        {
            var target = Path.Combine(targetDir, name);
            if (move)
            {
                File.Move(distPath, target, overwrite: true);
            }
            else
            {
                File.Copy(distPath, target, overwrite: true);
            }
            return;
        }

        var actual = ArchiveExtractor.Detect(distPath);
        if (ArchiveExtractor.TryExtract(distPath, targetDir, out var error))
        {
            Logger.Information("Архив {File} ({Kind}) распакован средствами .NET", name, actual);
            return;
        }
        if (error.Length > 0)
        {
            Logger.Warning("Распаковка {File} средствами .NET не удалась: {Error}", name, error);
        }
        else
        {
            Logger.Information("Архив {File} ({Kind}) в .NET не распаковывается — ищем утилиту",
                name, actual.Length > 0 ? actual : kind);
        }

        foreach (var tool in UnpackTools())
        {
            var arguments = tool.SevenZip
                ? $"x -y -o\"{targetDir}\" \"{distPath}\""
                : $"-xf \"{distPath}\" -C \"{targetDir}\"";
            if (RunTool(tool.FileName, arguments))
            {
                Logger.Information("Архив {File} распакован утилитой {Tool}", name, tool.FileName);
                return;
            }
        }
        throw new InvalidOperationException(
            Loc.T("msg.modelInstall.10", name, targetDir, package.Hint));
    }

    /// <summary>
    /// Внешние распаковщики по порядку: tar (PATH и системный каталог Windows — там лежит
    /// bsdtar, понимающий 7z), затем 7-Zip. 7-Zip в PATH себя не прописывает, поэтому кроме
    /// имён команд перебираются обычные места установки.
    /// </summary>
    private static IEnumerable<(string FileName, bool SevenZip)> UnpackTools()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, sevenZip) in Candidates())
        {
            if (fileName.Length > 0 && seen.Add(fileName))
            {
                yield return (fileName, sevenZip);
            }
        }
        yield break;

        static IEnumerable<(string, bool)> Candidates()
        {
            yield return ("tar", false);
            if (OperatingSystem.IsWindows())
            {
                // системный bsdtar: под этим же именем в PATH может стоять GNU tar, который
                // 7z не знает вовсе — полный путь и есть вторая попытка
                yield return (Path.Combine(Environment.SystemDirectory, "tar.exe"), false);
            }
            yield return ("7z", true);
            yield return ("7za", true);
            yield return ("7zz", true);
            yield return ("7zr", true);
            if (!OperatingSystem.IsWindows())
            {
                yield break;  // в Linux и macOS 7-Zip ставится пакетом и живёт в PATH
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string?[] roots =
            [
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                local.Length > 0 ? Path.Combine(local, "Programs") : null,
            ];
            foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                yield return (Path.Combine(root!, "7-Zip", "7z.exe"), true);
            }
            if (local.Length > 0)
            {
                // winget кладёт в свой каталог ссылок «обёртку» с тем же именем
                yield return (Path.Combine(local, "Microsoft", "WinGet", "Links", "7z.exe"), true);
            }
        }
    }

    /// <summary>Запустить утилиту распаковки; false — её нет или она вернула ошибку.</summary>
    private static bool RunTool(string fileName, string arguments)
    {
        if (Path.IsPathRooted(fileName) && !File.Exists(fileName))
        {
            return false; // перебор известных мест установки — молча
        }
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return false;
            }
            // читать надо ОБА потока разом: 7-Zip пишет ход распаковки в stdout, и чтение
            // одного только stderr до конца упирается в переполненный буфер второго — процесс
            // встаёт навсегда (на архиве ComfyUI это «установка висит и ничего не происходит»)
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var error = errors.GetAwaiter().GetResult();
            output.GetAwaiter().GetResult();
            if (process.ExitCode == 0)
            {
                return true;
            }
            Logger.Warning("Распаковка через {Tool} не удалась (код {Code}): {Error}",
                fileName, process.ExitCode, error.Trim());
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false; // утилиты нет в системе
        }
    }

    /// <summary>Сборка под железо для масок ассетов: с NVIDIA — CUDA, иначе CPU (как в install_local_model).</summary>
    private static string BuildFlavor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "ubuntu-x64";
        }
        var hasNvidia = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => SafeExists(Path.Combine(dir.Trim(), "nvidia-smi.exe")));
        return hasNvidia ? "win-cuda-12.4-x64" : "win-cpu-x64";
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(ex, "Не удалось удалить временный дистрибутив {Path}", path);
        }
    }

    // --- команда запуска модели после установки (ТЗ v1.42) ---

    /// <summary>Текущая команда запуска из профайла модели; пусто — запускать нечего.</summary>
    private string LaunchCommandOf(string modelId)
    {
        var model = _models.Get(modelId);
        if (model is null || !File.Exists(_files.Abs(model.ProfilePath)))
        {
            return "";
        }
        try
        {
            using var doc = JsonDocument.Parse(_files.ReadText(model.ProfilePath));
            return doc.RootElement.TryGetProperty("launchCommand", out var v) &&
                   v.ValueKind == JsonValueKind.String
                ? v.GetString()!
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>
    /// Команда запуска установленной модели, а если её в профайле нет — сформировать
    /// (ТЗ v1.45, todo37_3). Так лечится случай «файлы скачаны и пакеты поставлены мимо
    /// установщика»: модель установлена, а запускать её нечем. Шаблона в манифесте нет
    /// либо пути не сходятся — молча оставляем как было: у модели просто нет своего сервера.
    /// </summary>
    public string EnsureLaunchCommand(string modelId, ModelInstallManifest manifest)
    {
        var current = LaunchCommandOf(modelId);
        if (current.Trim().Length > 0 || manifest.LaunchCommand.Length == 0)
        {
            return current;
        }
        try
        {
            WriteLaunchCommand(modelId, manifest);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            Logger.Warning(ex, "Модель {ModelId}: не удалось сформировать команду запуска", modelId);
        }
        return LaunchCommandOf(modelId);
    }

    /// <summary>
    /// Прописать в профайл модели команду запуска локального сервера (ТЗ v1.42): шаблон
    /// манифеста с подставленными путями установленных пакетов и весов. Шаблона нет —
    /// профайл не трогается (модель запускается не нами, например внешним ComfyUI).
    /// </summary>
    public void WriteLaunchCommand(string modelId, ModelInstallManifest manifest)
    {
        if (manifest.LaunchCommand.Length == 0)
        {
            return;
        }
        var model = _models.Get(modelId);
        if (model is null || !File.Exists(_files.Abs(model.ProfilePath)))
        {
            return;
        }
        var command = ResolveLaunchCommand(manifest);
        var node = System.Text.Json.Nodes.JsonNode.Parse(_files.ReadText(model.ProfilePath))
            as System.Text.Json.Nodes.JsonObject;
        if (node is null)
        {
            return;
        }
        if (node["launchCommand"]?.GetValue<string>() == command)
        {
            return; // уже прописана — файл не трогаем
        }
        node["launchCommand"] = command;
        // профайл читают и правят люди — пишем без \uXXXX-эскейпов (как JsonText.Readable, гл. 11)
        _files.WriteText(model.ProfilePath, node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        Logger.Information("Модели {ModelId} прописана команда запуска: {Command}", modelId, command);
    }

    /// <summary>
    /// Подставить пути в шаблон команды запуска: {package:&lt;id&gt;:&lt;файл&gt;} — файл
    /// установленного пакета, {model:&lt;файл&gt;} — файл весов из каталога группы,
    /// {groupDir}, {modelsRepo}, {extraModelPaths}.
    /// </summary>
    public string ResolveLaunchCommand(ModelInstallManifest manifest) =>
        ExpandPaths(manifest, manifest.LaunchCommand);

    /// <summary>
    /// Подставить пути установки модели в ЛЮБОЙ текст настройки (T-289) — не только в команду
    /// запуска: те же {package:…}, {model:…}, {groupDir}, {modelsRepo}, {extraModelPaths}
    /// нужны обучению адаптера LoRA (LoraTrainService.ModelPaths). Модель без манифеста
    /// установки подставлять нечем — текст возвращается как есть.
    /// </summary>
    public string ExpandPaths(string modelId, string value)
    {
        var manifest = Manifest(modelId);
        return manifest is null || value.Length == 0 ? value : ExpandPaths(manifest, value);
    }

    private string ExpandPaths(ModelInstallManifest manifest, string value)
    {
        var command = value
            .Replace("{groupDir}", TargetDir(manifest))
            .Replace("{modelsRepo}", _repoDir())
            .Replace("{extraModelPaths}", Path.Combine(_repoDir(), ExtraModelPathsFile));

        command = System.Text.RegularExpressions.Regex.Replace(command,
            @"\{model:([^}]+)\}", m => Path.Combine(TargetDir(manifest), m.Groups[1].Value));

        return ExpandPackages(command);
    }

    /// <summary>
    /// Подставить пути ПАКЕТОВ: {package:код:файл} — файл внутри каталога пакета (или уже
    /// установленная в системе программа), {packageDir:код} — сам каталог пакета. Второй
    /// нужен весам HuggingFace (T-185-S0): тренеру передаётся КАТАЛОГ снимка репозитория,
    /// а не файл в нём — иначе он полез бы качать те же 18 ГБ с huggingface.co сам.
    /// Модель тут ни при чём, поэтому манифест не нужен — этим же зовётся настройка пакета.
    /// </summary>
    public string ExpandPackages(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }
        var catalog = Packages();
        var command = System.Text.RegularExpressions.Regex.Replace(value,
            @"\{packageDir:([^}]+)\}", m => PackageDir(Required(catalog, m.Groups[1].Value)));

        return System.Text.RegularExpressions.Regex.Replace(command,
            @"\{package:([^:}]+):([^}]+)\}", m =>
            {
                var package = Required(catalog, m.Groups[1].Value);
                return FindInPackage(package, m.Groups[2].Value)
                       ?? throw new InvalidOperationException(
                           Loc.T("msg.modelInstall.12", package.Name, m.Groups[2].Value, PackageDir(package)));
            });

        static ModelPackage Required(ModelPackageCatalog catalog, string id) =>
            catalog.Find(id) ?? throw new InvalidOperationException(Loc.T("msg.modelInstall.11", id));
    }

    private static long LengthOf(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? info.Length : 0;
    }

    // --- активность = установленность (ТЗ v1.41, todo36_4) ---

    /// <summary>
    /// Текст ошибки активации для AiModelService.ActivationGuard: модель с манифестом
    /// установки нельзя делать активной, пока не установлена; null — можно.
    /// </summary>
    public string? ActivationError(string modelId)
    {
        var status = Status(modelId);
        if (!status.HasManifest || status.Installed)
        {
            return null;
        }
        var missingPackages = status.Packages.Where(p => !p.Installed).Select(p => p.Name).ToList();
        var what = missingPackages.Count > 0
            ? Loc.T("msg.modelInstall.13", string.Join(", ", missingPackages))
            : "";
        return Loc.T("msg.modelInstall.14", what);
    }

    /// <summary>
    /// Проверка при старте приложения: модель с манифестом, у которой файлы не на месте
    /// (удалены/не докачаны), не может оставаться активной — деактивируется; попутно
    /// обновляется файл путей весов ComfyUI. Установленные, но выключенные пользователем
    /// модели НЕ включаются — ручной сброс активности уважается (автоактивация только
    /// в момент завершения установки).
    /// </summary>
    public void EnforceActivationOnStartup()
    {
        foreach (var status in StatusAll())
        {
            var model = _models.Get(status.ModelId);
            if (model is { IsActive: true } && !status.Installed)
            {
                model.IsActive = false;
                try
                {
                    _models.Update(model, actorId: null);
                    Logger.Information(
                        "Модель {Name} деактивирована: файлы установки не на месте (репозиторий {Dir})",
                        model.Name, status.TargetDir);
                }
                catch (ArgumentException ex)
                {
                    // запись справочника принадлежит другому серверу (ТЗ гл. 6, этап 42):
                    // активность меняет её владелец, а старт приложения на этом падать не должен
                    Logger.Debug("Активность модели {Name} здесь не меняется: {Reason}",
                        model.Name, ex.Message);
                }
            }
        }
        WriteExtraModelPaths();
    }

    /// <summary>Активировать модель после успешной установки (ТЗ v1.41).</summary>
    private void ActivateInstalled(string modelId)
    {
        var model = _models.Get(modelId);
        if (model is { IsActive: false })
        {
            model.IsActive = true;
            _models.Update(model, actorId: null);
            Logger.Information("Модель {Name} активирована после установки", model.Name);
        }
    }

    /// <summary>
    /// Файл путей весов для ComfyUI (ТЗ v1.41): &lt;репозиторий&gt;/ai2p_extra_model_paths.yaml —
    /// категории (diffusion_models/text_encoders/vae/…) из манифестов УСТАНОВЛЕННЫХ моделей
    /// указывают на каталоги групп. Подключается ключом запуска ComfyUI
    /// --extra-model-paths-config; наши плоские каталоги групп становятся видны ComfyUI
    /// без копирования файлов. Нечего публиковать — файл не пишется.
    /// </summary>
    public void WriteExtraModelPaths()
    {
        var byCategory = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var model in _models.List())
        {
            var manifest = Manifest(model.Id);
            // достаточно скачанных весов: их видит и внешний ComfyUI, даже если наш пакет не ставился
            if (manifest is null || Status(model.Id).Files.Any(f => f.State != "done"))
            {
                continue;
            }
            foreach (var file in manifest.Files.Where(f => f.Category.Length > 0))
            {
                if (!byCategory.TryGetValue(file.Category, out var groups))
                {
                    byCategory[file.Category] = groups = new SortedSet<string>(StringComparer.Ordinal);
                }
                groups.Add(manifest.Group);
            }
        }
        if (byCategory.Count == 0)
        {
            return;
        }
        // КАТАЛОГ АДАПТЕРОВ LoRA (T-14-S1): весов у него в манифестах нет — файлы туда кладёт
        // обучение (T-12-S1, <репозиторий>/loras), — поэтому категория дописывается всегда,
        // иначе ComfyUI не нашёл бы обученный адаптер по имени и отказал бы в генерации.
        // Путь абсолютный: каталог лежит РЯДОМ с base_path, а не внутри него
        var lorasDir = Path.Combine(_repoDir(), ObjectLoadService.LorasCategory);
        Directory.CreateDirectory(lorasDir);
        // если каталог адаптеров вдруг объявлен и манифестом — обе строки уходят списком,
        // а не двумя ключами: второй ключ YAML молча затёр бы первый
        byCategory.TryGetValue(ObjectLoadService.LorasCategory, out var lorasGroups);
        byCategory.Remove(ObjectLoadService.LorasCategory);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Loc.T("msg.modelInstall.15"));
        sb.AppendLine(Loc.T("msg.modelInstall.16"));
        sb.AppendLine($"#   --extra-model-paths-config \"{Path.Combine(_repoDir(), ExtraModelPathsFile)}\"");
        sb.AppendLine("ai2p:");
        sb.AppendLine($"  base_path: {Path.Combine(_repoDir(), "models")}");
        sb.AppendLine($"  {ObjectLoadService.LorasCategory}: |");
        sb.AppendLine($"    {lorasDir}");
        foreach (var group in lorasGroups ?? [])
        {
            sb.AppendLine($"    {group}");
        }
        foreach (var (category, groups) in byCategory)
        {
            if (groups.Count == 1)
            {
                sb.AppendLine($"  {category}: {groups.First()}");
            }
            else
            {
                sb.AppendLine($"  {category}: |");
                foreach (var group in groups)
                {
                    sb.AppendLine($"    {group}");
                }
            }
        }
        var path = Path.Combine(_repoDir(), ExtraModelPathsFile);
        Directory.CreateDirectory(_repoDir());
        File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
        Logger.Information("Обновлён файл путей весов ComfyUI: {Path}", path);
    }
}
