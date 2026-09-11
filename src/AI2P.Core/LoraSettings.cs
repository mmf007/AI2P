using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАСТРОЙКА РАБОТЫ С LoRA у записи справочника моделей (T-13-S1). Лежит в ПРОФАЙЛЕ
/// подключения (models/profile_&lt;ID&gt;.json, ключ <c>lora</c>) — там же, где провайдер,
/// команда запуска и workflow: работа с адаптером это часть подключения, а не оценка
/// способностей модели. Источник один намеренно: держать «поддерживается» в декларации
/// возможностей, а «как именно» в профайле — значит завести два места, которые разойдутся
/// молча (так уже было с описаниями справочников, T-257).
///
/// Отметка <see cref="Supported"/> — то, что показывает колонка «LoRA» справочника.
/// Остальное описывает ПЯТЬ шагов работы с адаптером, каждый своим блоком:
/// <list type="number">
/// <item>как загружать датасет на обучение — <see cref="LoraTrain.Dataset"/>;</item>
/// <item>как запускать обучение — <see cref="LoraTrain.Start"/>;</item>
/// <item>как ждать результата — <see cref="LoraTrain.Wait"/>;</item>
/// <item>как выкачивать результат — <see cref="LoraTrain.Result"/>;</item>
/// <item>как загружать адаптер в модель при работе — <see cref="Apply"/>.</item>
/// </list>
///
/// Настройка ДЕКЛАРАТИВНА: здесь только данные (пути, команды, плейсхолдеры, коды способов),
/// исполняют их коннекторы. Модель, у которой настройки нет вовсе, считается не
/// поддерживающей LoRA — как и запись с <c>"supported": false</c>.
/// </summary>
public sealed class LoraSettings
{
    /// <summary>Модель умеет работать с адаптером LoRA (колонка «LoRA» справочника).</summary>
    public bool Supported { get; set; }

    /// <summary>
    /// Почему не поддерживается — КОД (<see cref="LoraReasons"/>), а не текст: он
    /// переводится ключом <c>models.lora.reason.&lt;код&gt;</c>. У поддерживающей модели пусто.
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>Чем работа с адаптером исполняется: <see cref="LoraEngines"/>.</summary>
    public string Engine { get; set; } = LoraEngines.None;

    /// <summary>Шаг 5: как адаптер попадает в модель во время работы.</summary>
    public LoraApply Apply { get; set; } = new();

    /// <summary>Шаги 1–4: обучение адаптера.</summary>
    public LoraTrain Train { get; set; } = new();

    /// <summary>Настройка модели, у которой работы с LoRA нет (и это известно).</summary>
    public static LoraSettings NotSupported(string reason) => new() { Reason = reason };

    /// <summary>
    /// Разобрать настройку из ТЕКСТА профайла. Профайла нет, он повреждён или ключа
    /// <c>lora</c> в нём нет — «не поддерживается» с причиной <see cref="LoraReasons.Unknown"/>:
    /// молчание профайла это не отказ провайдера, а незаполненная настройка.
    /// </summary>
    public static LoraSettings Parse(string? profileJson)
    {
        if (profileJson is not { Length: > 0 } || profileJson.Trim().Length == 0)
        {
            return NotSupported(LoraReasons.Unknown);
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            return FromProfile(doc.RootElement);
        }
        catch (JsonException)
        {
            return NotSupported(LoraReasons.Unknown);
        }
    }

    /// <summary>Разобрать настройку из уже прочитанного профайла.</summary>
    public static LoraSettings FromProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("lora", out var lora) || lora.ValueKind != JsonValueKind.Object)
        {
            return NotSupported(LoraReasons.Unknown);
        }
        var result = new LoraSettings
        {
            Supported = JsonRead.Bool(lora, "supported", false),
            Reason = JsonRead.Str(lora, "reason"),
            Engine = JsonRead.Str(lora, "engine", LoraEngines.None),
        };
        if (lora.TryGetProperty("apply", out var apply) && apply.ValueKind == JsonValueKind.Object)
        {
            result.Apply = LoraApply.FromJson(apply);
        }
        if (lora.TryGetProperty("train", out var train) && train.ValueKind == JsonValueKind.Object)
        {
            result.Train = LoraTrain.FromJson(train);
        }
        if (!result.Supported && result.Reason.Length == 0)
        {
            result.Reason = LoraReasons.Unknown;
        }
        return result;
    }
}

/// <summary>
/// ШАГ 5 — как обученный адаптер попадает в модель при работе. У ComfyUI это узел графа
/// (<c>LoraLoaderModelOnly</c>) либо плейсхолдеры шаблона workflow, у llama.cpp — ключ
/// команды запуска сервера (<c>--lora-scaled файл:вес</c>) и горячая замена через
/// <c>POST /lora-adapters</c>, у облачного API — поле запроса генерации.
/// </summary>
public sealed class LoraApply
{
    /// <summary>Способ: <see cref="LoraApplyKinds"/>.</summary>
    public string Kind { get; set; } = LoraApplyKinds.None;

    /// <summary>Плейсхолдер имени файла адаптера в шаблоне workflow (ComfyUI).</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Плейсхолдер веса адаптера в шаблоне workflow (подставляется числом).</summary>
    public string StrengthPlaceholder { get; set; } = "";

    /// <summary>Вес адаптера по умолчанию (0…2).</summary>
    public double Strength { get; set; } = 1.0;

    /// <summary>Узел графа, которым адаптер подключается, когда плейсхолдера в шаблоне нет.</summary>
    public string Node { get; set; } = "";

    /// <summary>Куда класть файл адаптера: категория каталога ComfyUI («loras») либо путь.</summary>
    public string Dir { get; set; } = "";

    /// <summary>Сколько адаптеров можно подключить разом.</summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>Расширения файла адаптера, которые модель принимает («.safetensors», «.gguf»).</summary>
    public List<string> Formats { get; set; } = [];

    /// <summary>Поле запроса генерации, в которое уходит адаптер (облачный API).</summary>
    public string Field { get; set; } = "";

    internal static LoraApply FromJson(JsonElement e) => new()
    {
        Kind = JsonRead.Str(e, "kind", LoraApplyKinds.None),
        Placeholder = JsonRead.Str(e, "placeholder"),
        StrengthPlaceholder = JsonRead.Str(e, "strengthPlaceholder"),
        Strength = JsonRead.Num(e, "strength", 1.0),
        Node = JsonRead.Str(e, "node"),
        Dir = JsonRead.Str(e, "dir"),
        MaxCount = JsonRead.Int(e, "maxCount", 1),
        Formats = JsonRead.Strings(e, "formats"),
        Field = JsonRead.Str(e, "field"),
    };
}

/// <summary>ШАГИ 1–4 — обучение адаптера: датасет, запуск, ожидание, результат.</summary>
public sealed class LoraTrain
{
    /// <summary>Способ обучения: <see cref="LoraTrainKinds"/>.</summary>
    public string Kind { get; set; } = LoraTrainKinds.None;

    /// <summary>Документ обучения этой модели (репозиторий, инструкция провайдера).</summary>
    public string DocUrl { get; set; } = "";

    /// <summary>
    /// Коды пакетов (models/packages.json), нужных ОБУЧЕНИЮ (T-289) — обучающий репозиторий
    /// и всё, чего нет у генерации. В манифесте установки модели им не место: от него
    /// зависит признак «модель установлена», и у всех, у кого модель уже работает, она бы
    /// погасла из-за пакета, которым не пользуется. Ставятся при первом запуске обучения.
    /// </summary>
    public List<string> Packages { get; set; } = [];

    /// <summary>ШАГ 1: как собрать и куда положить датасет.</summary>
    public LoraDataset Dataset { get; set; } = new();

    /// <summary>
    /// ШАГ 1½ (T-289): ФАЙЛЫ НАСТРОЙКИ обучения, которые кладутся рядом с датасетом перед
    /// запуском — конфигурация тренера (dataset.toml, config.yaml) и сам запускающий скрипт.
    /// Без них настройка «как обучать» была бы неполной: у всякого обучающего репозитория
    /// половина ответа лежит не в командной строке, а в файле конфигурации, и вписать её
    /// в одну строку <see cref="LoraTrainStart.Command"/> нельзя.
    ///
    /// Файлы переписываются на КАЖДОМ запуске обучения: они принадлежат настройке модели,
    /// как и команда, а не человеку. Своё пишите в свой файл — его никто не тронет.
    /// </summary>
    public List<LoraTrainFile> Files { get; set; } = [];

    /// <summary>ШАГ 2: чем запускается обучение.</summary>
    public LoraTrainStart Start { get; set; } = new();

    /// <summary>ШАГ 3: как дождаться конца обучения.</summary>
    public LoraTrainWait Wait { get; set; } = new();

    /// <summary>ШАГ 4: откуда забрать обученный адаптер.</summary>
    public LoraTrainResult Result { get; set; } = new();

    internal static LoraTrain FromJson(JsonElement e)
    {
        var train = new LoraTrain
        {
            Kind = JsonRead.Str(e, "kind", LoraTrainKinds.None),
            DocUrl = JsonRead.Str(e, "docUrl"),
            Packages = JsonRead.Strings(e, "packages"),
        };
        if (e.TryGetProperty("dataset", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            train.Dataset = LoraDataset.FromJson(d);
        }
        if (e.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            train.Files = files.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(LoraTrainFile.FromJson)
                .Where(f => f.Path.Length > 0)
                .ToList();
        }
        if (e.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            train.Start = LoraTrainStart.FromJson(s);
        }
        if (e.TryGetProperty("wait", out var w) && w.ValueKind == JsonValueKind.Object)
        {
            train.Wait = LoraTrainWait.FromJson(w);
        }
        if (e.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            train.Result = LoraTrainResult.FromJson(r);
        }
        return train;
    }
}

/// <summary>
/// ШАГ 1 — датасет обучения. Эталонные кадры объекта (T-259) складываются по
/// <see cref="Path"/>: у локального обучения это каталог на диске, у облачного — то, что
/// уедет архивом. Подписи кадров (<see cref="Captions"/>) обучающие скрипты берут
/// одноимёнными .txt-файлами либо одним .json.
/// </summary>
public sealed class LoraDataset
{
    /// <summary>Способ: <see cref="LoraDatasetKinds"/>.</summary>
    public string Kind { get; set; } = LoraDatasetKinds.None;

    /// <summary>Куда собирается датасет; <c>{object}</c> — код объекта LoRA (OBJ-3).</summary>
    public string Path { get; set; } = "";

    /// <summary>Подписи кадров: «txt» (файл рядом с кадром), «json» (один файл), «none».</summary>
    public string Captions { get; set; } = "none";

    /// <summary>Куда уходит датасет при облачном обучении (адрес загрузки архива).</summary>
    public string UploadUrl { get; set; } = "";

    /// <summary>Сколько кадров нужно как минимум, чтобы обучение имело смысл.</summary>
    public int MinItems { get; set; }

    /// <summary>Сколько кадров имеет смысл брать максимум.</summary>
    public int MaxItems { get; set; }

    /// <summary>Разрешение, к которому кадры приводятся перед обучением.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Наибольший вес одного кадра в килобайтах; 0 — модель об этом ничего не сказала
    /// (T-274). Вместе с <see cref="Width"/>, <see cref="Height"/>, <see cref="MinItems"/>,
    /// <see cref="MaxItems"/> и <see cref="Formats"/> это и есть «контрольные настройки
    /// картинок» справочника: то, что кнопка «подставить из модели» переписывает в датасет,
    /// и то, с чем датасет сверяется перед обучением.
    /// </summary>
    public int MaxKb { get; set; }

    /// <summary>
    /// Форматы файлов кадра, которые принимает обучение этой модели («png», «jpeg»); пусто —
    /// не сказано, и тогда годится любой. Если названо несколько, датасету подставляется
    /// PNG (<see cref="LoraDatasetLimits.PickFormat"/>): он без потерь.
    /// </summary>
    public List<string> Formats { get; set; } = [];

    internal static LoraDataset FromJson(JsonElement e) => new()
    {
        Kind = JsonRead.Str(e, "kind", LoraDatasetKinds.None),
        Path = JsonRead.Str(e, "path"),
        Captions = JsonRead.Str(e, "captions", "none"),
        UploadUrl = JsonRead.Str(e, "uploadUrl"),
        MinItems = JsonRead.Int(e, "minItems", 0),
        MaxItems = JsonRead.Int(e, "maxItems", 0),
        Width = JsonRead.Int(e, "width", 0),
        Height = JsonRead.Int(e, "height", 0),
        MaxKb = JsonRead.Int(e, "maxKb", 0),
        Formats = JsonRead.Strings(e, "formats"),
    };

    /// <summary>
    /// У модели заполнены КОНТРОЛЬНЫЕ НАСТРОЙКИ картинок — есть что подставлять в датасет
    /// (T-274). Путь сбора датасета и способ подписей сюда не входят: это «куда складывать»,
    /// а не «какими быть кадрам».
    /// </summary>
    public bool HasLimits => Width > 0 || Height > 0 || MaxKb > 0
                             || MinItems > 0 || MaxItems > 0 || Formats.Count > 0;
}

/// <summary>
/// ФАЙЛ НАСТРОЙКИ обучения (T-289): что положить и куда. Путь — относительно ПАПКИ ПРОЕКТА
/// (как <see cref="LoraDataset.Path"/>), абсолютный используется как есть.
///
/// Содержимое в профайле разрешено писать МАССИВОМ СТРОК: конфигурация тренера и командный
/// файл — это десятки строк, и одной строкой с «\n» их не прочитать и не поправить.
/// Строки склеиваются переводом строки Windows: файл читает не наш код, а cmd.exe и python,
/// и .cmd с одними переводами строки Unix ведёт себя непредсказуемо.
/// </summary>
public sealed class LoraTrainFile
{
    /// <summary>Куда положить; поддерживаются те же подстановки, что и в команде.</summary>
    public string Path { get; set; } = "";

    /// <summary>Что положить; подстановки те же.</summary>
    public string Text { get; set; } = "";

    internal static LoraTrainFile FromJson(JsonElement e) => new()
    {
        Path = JsonRead.Str(e, "path"),
        Text = e.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.Array
            // строки массива берутся КАК ЕСТЬ: у скрипта пустая строка и отступ значимы,
            // а JsonRead.Strings подрезает и выбрасывает пустые
            ? string.Join("\r\n", t.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!))
            : JsonRead.Str(e, "text"),
    };
}

/// <summary>ШАГ 2 — запуск обучения: свой процесс на этом компьютере либо запрос к провайдеру.</summary>
public sealed class LoraTrainStart
{
    /// <summary>Способ: <see cref="LoraStartKinds"/>.</summary>
    public string Kind { get; set; } = LoraStartKinds.None;

    /// <summary>Команда запуска обучения; плейсхолдеры <c>{dataset}</c>, <c>{output}</c>,
    /// <c>{object}</c>, <c>{steps}</c>. Пусто — команда не задана: обучение настраивает
    /// человек (у обучающих скриптов нет ни одного «правильного по умолчанию» пути).</summary>
    public string Command { get; set; } = "";

    /// <summary>Рабочий каталог команды (каталог обучающего репозитория).</summary>
    public string WorkDir { get; set; } = "";

    /// <summary>Адрес запуска обучения у провайдера (облачное обучение).</summary>
    public string Url { get; set; } = "";

    /// <summary>Сколько шагов обучения по умолчанию.</summary>
    public int Steps { get; set; }

    internal static LoraTrainStart FromJson(JsonElement e) => new()
    {
        Kind = JsonRead.Str(e, "kind", LoraStartKinds.None),
        Command = JsonRead.Str(e, "command"),
        WorkDir = JsonRead.Str(e, "workDir"),
        Url = JsonRead.Str(e, "url"),
        Steps = JsonRead.Int(e, "steps", 0),
    };
}

/// <summary>
/// ШАГ 3 — как узнать, что обучение кончилось. Локально это конец процесса, у провайдера —
/// опрос состояния: <see cref="PollUrl"/> раз в <see cref="IntervalSec"/> секунд, готово —
/// когда поле <see cref="DoneField"/> равно <see cref="DoneValue"/>.
/// </summary>
public sealed class LoraTrainWait
{
    /// <summary>Способ: <see cref="LoraWaitKinds"/>.</summary>
    public string Kind { get; set; } = LoraWaitKinds.None;

    public string PollUrl { get; set; } = "";

    public int IntervalSec { get; set; } = 30;

    public string DoneField { get; set; } = "";

    public string DoneValue { get; set; } = "";

    /// <summary>Сколько ждать обучение всего; обучение LoRA идёт часами.</summary>
    public int TimeoutMinutes { get; set; } = 720;

    /// <summary>
    /// ТАЙМ-АУТ МОЛЧАНИЯ (T-152-S0): сколько ждать, пока обучение не напечатало НИ ОДНОЙ
    /// строки. Пока строки идут — процесс жив, и его меряет <see cref="TimeoutMinutes"/>;
    /// молчание значит, что он завис, и половину суток этого ждать незачем. 0 — умолчание
    /// кода (<c>LoraTrainService.DefaultIdleMinutes</c>).
    /// ПОЧЕМУ ЧАС, А НЕ ПОЛЧАСА (T-313): второй шаг тренера (кэш текстовых кодировщиков)
    /// при ПЕРВОМ запуске тянет с HuggingFace исходный Qwen2.5-VL-7B-Instruct — пять файлов
    /// на ~16 ГБ, — и делает это МОЛЧА: наружу идёт только внешняя полоска «Fetching 5 files»,
    /// пока не докачан целый файл. Получасовой предел резал живую закачку как зависание.
    /// </summary>
    public int IdleMinutes { get; set; } = 60;

    internal static LoraTrainWait FromJson(JsonElement e) => new()
    {
        Kind = JsonRead.Str(e, "kind", LoraWaitKinds.None),
        PollUrl = JsonRead.Str(e, "pollUrl"),
        IntervalSec = JsonRead.Int(e, "intervalSec", 30),
        DoneField = JsonRead.Str(e, "doneField"),
        DoneValue = JsonRead.Str(e, "doneValue"),
        TimeoutMinutes = JsonRead.Int(e, "timeoutMinutes", 720),
        IdleMinutes = JsonRead.Int(e, "idleMinutes", 60),
    };
}

/// <summary>
/// ШАГ 4 — откуда забрать обученный адаптер и куда его положить. <see cref="Target"/> —
/// место, из которого адаптер потом подключается при работе (<see cref="LoraApply.Dir"/>),
/// и именно этот путь ложится в объект проекта (<c>ObjectItem.LoraPath</c>).
/// </summary>
public sealed class LoraTrainResult
{
    /// <summary>Способ: <see cref="LoraResultKinds"/>.</summary>
    public string Kind { get; set; } = LoraResultKinds.None;

    /// <summary>Где обучение оставляет файл адаптера (локальное обучение).</summary>
    public string Path { get; set; } = "";

    /// <summary>Поле ответа провайдера, в котором лежит ссылка на адаптер (облачное обучение).</summary>
    public string UrlField { get; set; } = "";

    /// <summary>Куда адаптер кладётся у нас; <c>{object}</c> — код объекта LoRA.</summary>
    public string Target { get; set; } = "";

    internal static LoraTrainResult FromJson(JsonElement e) => new()
    {
        Kind = JsonRead.Str(e, "kind", LoraResultKinds.None),
        Path = JsonRead.Str(e, "path"),
        UrlField = JsonRead.Str(e, "urlField"),
        Target = JsonRead.Str(e, "target"),
    };
}

/// <summary>Чем исполняется работа с адаптером.</summary>
public static class LoraEngines
{
    public const string None = "none";
    /// <summary>ComfyUI: узел графа и каталог <c>models/loras</c>.</summary>
    public const string ComfyUi = "comfyui";
    /// <summary>llama.cpp (llama-server): ключ запуска и <c>POST /lora-adapters</c>.</summary>
    public const string LlamaCpp = "llama-cpp";
    /// <summary>HTTP API провайдера.</summary>
    public const string Http = "http";
}

/// <summary>Почему модель не работает с LoRA (код переводится в UI).</summary>
public static class LoraReasons
{
    /// <summary>Провайдер не даёт подключить свой адаптер к своей модели.</summary>
    public const string Provider = "provider";
    /// <summary>Дообучение у провайдера есть, но файл адаптера наружу не отдаётся.</summary>
    public const string NoAdapter = "no-adapter";
    /// <summary>
    /// Адаптеров LoRA у этой архитектуры сегодня нет вовсе — ни тренера, ни готовых файлов
    /// (T-20-S0: 3D-модели; у musubi-tuner и прочих публичных тренеров только картинки и видео).
    /// Отличается от <see cref="Provider"/> тем, что дело не в запрете провайдера,
    /// и от <see cref="Unknown"/> тем, что это ОТВЕТ, а не пропущенная работа.
    /// </summary>
    public const string NoLora = "no-lora";
    /// <summary>Настройка не заполнена — про эту модель просто ничего не сказано.</summary>
    public const string Unknown = "unknown";

    public static readonly string[] All = [Provider, NoAdapter, NoLora, Unknown];
}

/// <summary>Способы подключения адаптера к модели (шаг 5).</summary>
public static class LoraApplyKinds
{
    public const string None = "none";
    /// <summary>Узел/плейсхолдер в шаблоне workflow (ComfyUI).</summary>
    public const string Workflow = "workflow";
    /// <summary>Ключ команды запуска локального сервера модели (llama.cpp).</summary>
    public const string LaunchArg = "launch-arg";
    /// <summary>Поле запроса генерации (облачный API).</summary>
    public const string RequestField = "request-field";
}

/// <summary>Способы обучения (шаг 2 в целом).</summary>
public static class LoraTrainKinds
{
    public const string None = "none";
    /// <summary>Обучение идёт своим процессом на этом компьютере.</summary>
    public const string Process = "process";
    /// <summary>Обучение заказывается у провайдера по HTTP.</summary>
    public const string Http = "http";
    /// <summary>Обучение делается вне системы, готовый файл человек подкладывает сам.</summary>
    public const string External = "external";
}

/// <summary>Способы сбора датасета (шаг 1).</summary>
public static class LoraDatasetKinds
{
    public const string None = "none";
    /// <summary>Кадры складываются в каталог на диске.</summary>
    public const string Dir = "dir";
    /// <summary>Кадры уезжают архивом к провайдеру.</summary>
    public const string Upload = "upload";
}

/// <summary>Способы запуска обучения (шаг 2).</summary>
public static class LoraStartKinds
{
    public const string None = "none";
    public const string Process = "process";
    public const string Http = "http";
}

/// <summary>Способы ожидания результата (шаг 3).</summary>
public static class LoraWaitKinds
{
    public const string None = "none";
    /// <summary>Ждать конца запущенного процесса.</summary>
    public const string Process = "process";
    /// <summary>Опрашивать состояние у провайдера.</summary>
    public const string Poll = "poll";
}

/// <summary>Способы получения результата (шаг 4).</summary>
public static class LoraResultKinds
{
    public const string None = "none";
    /// <summary>Файл уже лежит на диске — его надо перенести на место.</summary>
    public const string File = "file";
    /// <summary>Файл надо скачать по ссылке из ответа провайдера.</summary>
    public const string Download = "download";
}
