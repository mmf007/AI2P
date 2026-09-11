using System.Text.Json;

namespace AI2P.Connectors;

/// <summary>
/// Справочник пакетов, необходимых локальным моделям для запуска (ТЗ v1.42, todo36_5):
/// ComfyUI для медиа-моделей, llama.cpp (llama-server) для GGUF-моделей. Файл дистрибутива —
/// models/packages.json в dataDir (сид AiModelService). Пакет общий для всех моделей: ставится
/// один раз, дальше только проверяется по признаку установленности.
/// </summary>
public sealed class ModelPackageCatalog
{
    public List<ModelPackage> Packages { get; } = [];

    public ModelPackage? Find(string id) =>
        Packages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Разобрать справочник пакетов; повреждённый файл — пустой справочник.</summary>
    public static ModelPackageCatalog Parse(string json)
    {
        var catalog = new ModelPackageCatalog();
        if (json.Trim().Length == 0)
        {
            return catalog;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("packages", out var packages) ||
                packages.ValueKind != JsonValueKind.Array)
            {
                return catalog;
            }
            foreach (var item in packages.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var package = new ModelPackage
                {
                    Id = Str(item, "id"),
                    Name = Str(item, "name"),
                    Dir = Str(item, "dir"),
                    Os = Str(item, "os"),
                    Check = Str(item, "check"),
                    Hint = Str(item, "hint"),
                };
                if (package.Id.Length == 0 || package.Dir.Length == 0)
                {
                    continue;
                }
                if (item.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.Object)
                {
                    package.System = ModelPackage.SystemProbe.FromJson(system);
                }
                if (item.TryGetProperty("setup", out var setup) && setup.ValueKind == JsonValueKind.Object)
                {
                    package.Setup = ModelPackage.PackageSetup.FromJson(setup);
                }
                if (item.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in files.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                    {
                        var file = new ModelPackage.PackageFile
                        {
                            Name = Str(f, "name"),
                            Url = Str(f, "url"),
                            GitHubRepo = Str(f, "github"),
                            AssetPattern = Str(f, "asset"),
                            Unpack = Str(f, "unpack"),
                            When = Str(f, "when"),
                            Size = Num(f, "size"),
                        };
                        if (file.Url.Length > 0 || file.GitHubRepo.Length > 0 && file.AssetPattern.Length > 0)
                        {
                            package.Files.Add(file);
                        }
                    }
                }
                catalog.Packages.Add(package);
            }
        }
        catch (JsonException)
        {
            return catalog; // повреждённый справочник пакетов — как будто пакетов нет
        }
        return catalog;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim()
            : "";

    private static long Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt64(out var value)
            ? value
            : 0;
}

/// <summary>Пакет справочника пакетов (ТЗ v1.42): что скачать и куда распаковать.</summary>
public sealed class ModelPackage
{
    /// <summary>Код пакета — на него ссылается манифест установки модели: comfyui, llama.cpp.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Подкаталог корня пакетов, куда распаковывается пакет.</summary>
    public string Dir { get; set; } = "";

    /// <summary>Для какой ОС пакет: windows / linux / macos; пусто — для любой.</summary>
    public string Os { get; set; } = "";

    /// <summary>
    /// Признак установленности: путь относительно каталога пакета либо просто имя файла —
    /// тогда файл ищется в каталоге пакета рекурсивно (архивы кладут свой корневой каталог).
    /// </summary>
    public string Check { get; set; } = "";

    /// <summary>Что делать человеку, если автоматическая установка не удалась.</summary>
    public string Hint { get; set; } = "";

    /// <summary>
    /// ПАКЕТ МОЖЕТ УЖЕ СТОЯТЬ В СИСТЕМЕ (T-4-S0): Python ставят на компьютер задолго до
    /// нас, и качать рядом второй экземпляр незачем. Здесь сказано, чем его узнать —
    /// имя команды в PATH и допустимые версии; null — пакет живёт только в своём каталоге
    /// (ComfyUI, llama.cpp, musubi-tuner).
    /// </summary>
    public SystemProbe? System { get; set; }

    /// <summary>
    /// ЧТО СДЕЛАТЬ ПОСЛЕ РАСПАКОВКИ (T-185-S0): пакет бывает не готов к работе тем, что
    /// просто лёг на диск. Тренеру LoRA нужно окружение Python с torch и зависимостями
    /// из его же файла требований — до T-185-S0 это делал первый ЗАПУСК обучения, то есть
    /// человек узнавал о получасовой установке уже после нажатия кнопки «Обучить», а
    /// молчание pip выглядело зависанием. Теперь это шаги установки пакета, и идут они
    /// там, где человек согласился ждать и видит ход работы. null — пакету настройка не нужна.
    /// </summary>
    public PackageSetup? Setup { get; set; }

    public List<PackageFile> Files { get; set; } = [];

    /// <summary>Пакет предназначен для текущей ОС.</summary>
    public bool MatchesOs() => Os.Length == 0
        || Os.Equals("windows", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows()
        || Os.Equals("linux", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsLinux()
        || Os.Equals("macos", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsMacOS();

    /// <summary>
    /// КАК УЗНАТЬ УЖЕ УСТАНОВЛЕННЫЙ В СИСТЕМЕ ПАКЕТ (T-4-S0). Правило простое: команда
    /// ищется в PATH, и если у пакета названы допустимые версии — найденное проверяется
    /// запуском с <see cref="VersionArgs"/>. Версия здесь не придирчивость: musubi-tuner
    /// живёт на Python 3.10–3.12, а стоящий у человека 3.13 сорвал бы обучение через
    /// несколько часов после нажатия кнопки — и виноватым выглядел бы AI2P.
    /// </summary>
    public sealed class SystemProbe
    {
        /// <summary>Имена команд в PATH по порядку: python, python3 …</summary>
        public List<string> Commands { get; set; } = [];

        /// <summary>Чем спросить версию («--version»); пусто — версия не проверяется.</summary>
        public string VersionArgs { get; set; } = "";

        /// <summary>Наименьшая годная версия вида «3.10»; пусто — снизу не ограничена.</summary>
        public string MinVersion { get; set; } = "";

        /// <summary>Наибольшая годная версия вида «3.12» (включительно, по паре
        /// «старшая.младшая»); пусто — сверху не ограничена.</summary>
        public string MaxVersion { get; set; } = "";

        internal static SystemProbe FromJson(JsonElement e)
        {
            var probe = new SystemProbe
            {
                VersionArgs = Str(e, "versionArgs"),
                MinVersion = Str(e, "minVersion"),
                MaxVersion = Str(e, "maxVersion"),
            };
            if (e.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
            {
                probe.Commands = commands.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
            }
            return probe;
        }

        private static string Str(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";
    }

    /// <summary>
    /// НАСТРОЙКА ПАКЕТА ПОСЛЕ РАСПАКОВКИ (T-185-S0): несколько программ подряд, каждая
    /// в рабочем каталоге <see cref="WorkDir"/>. Готовность отмечается файлом
    /// <see cref="Check"/> — пока его нет, пакет считается неустановленным, и установка
    /// повторит настройку (скачанное при этом не перекачивается).
    /// </summary>
    public sealed class PackageSetup
    {
        /// <summary>Рабочий каталог шагов: путь либо ФАЙЛ, по которому каталог и опознаётся
        /// (архив кладёт свой корневой каталог, имя которого меняется от версии к версии);
        /// пусто — каталог пакета.</summary>
        public string WorkDir { get; set; } = "";

        /// <summary>Файл-признак «настройка сделана» относительно рабочего каталога.</summary>
        public string Check { get; set; } = "";

        /// <summary>Общий предел на все шаги, минуты (0 — умолчание).</summary>
        public int TimeoutMinutes { get; set; }

        /// <summary>Предел МОЛЧАНИЯ одного шага, минуты (0 — умолчание): pip умеет молчать,
        /// но не полчаса подряд, и разница между «идёт» и «встало» видна только так.</summary>
        public int IdleMinutes { get; set; }

        public List<SetupStep> Steps { get; set; } = [];

        internal static PackageSetup FromJson(JsonElement e)
        {
            var setup = new PackageSetup
            {
                WorkDir = Str(e, "workDir"),
                Check = Str(e, "check"),
                TimeoutMinutes = (int)Num(e, "timeoutMinutes"),
                IdleMinutes = (int)Num(e, "idleMinutes"),
            };
            if (e.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in steps.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                {
                    var step = new SetupStep
                    {
                        Name = Str(s, "name"),
                        Run = Str(s, "run"),
                        Args = Str(s, "args"),
                        Optional = s.TryGetProperty("optional", out var o)
                                   && o.ValueKind == JsonValueKind.True,
                    };
                    if (step.Run.Length > 0)
                    {
                        setup.Steps.Add(step);
                    }
                }
            }
            return setup;
        }

        /// <summary>Шаг настройки: что запустить и с какими ключами.</summary>
        public sealed class SetupStep
        {
            /// <summary>Как шаг называется в окне установки; пусто — показывается программа.</summary>
            public string Name { get; set; } = "";

            /// <summary>Программа: путь (плейсхолдеры {dir}, {package:код:файл}, {packageDir:код})
            /// либо путь относительно рабочего каталога.</summary>
            public string Run { get; set; } = "";

            public string Args { get; set; } = "";

            /// <summary>Неудача шага установку НЕ роняет (ускорители, необязательные колёса).</summary>
            public bool Optional { get; set; }
        }

        private static string Str(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";

        private static long Num(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var value)
                ? value
                : 0;
    }

    /// <summary>Дистрибутив пакета: прямая ссылка либо ассет последнего релиза GitHub.</summary>
    public sealed class PackageFile
    {
        /// <summary>Имя файла дистрибутива; пусто — берётся из имени ассета/URL. Может
        /// содержать подкаталог («Qwen2.5-VL-7B-Instruct/config.json»): снимки разных
        /// репозиториев HuggingFace иначе столкнулись бы одинаковыми именами в каталоге
        /// дистрибутивов. В каталог пакета файл кладётся БЕЗ подкаталога.</summary>
        public string Name { get; set; } = "";

        public string Url { get; set; } = "";

        /// <summary>Репозиторий GitHub вида "owner/repo" — ассет берётся из последнего релиза.</summary>
        public string GitHubRepo { get; set; } = "";

        /// <summary>Маска имени ассета релиза; {flavor} заменяется на сборку (cuda/cpu).</summary>
        public string AssetPattern { get; set; } = "";

        /// <summary>Как распаковать: zip / 7z / tar; пусто — файл кладётся как есть.</summary>
        public string Unpack { get; set; } = "";

        /// <summary>Условие: "cuda" — только для сборки с CUDA, "cpu" — только без неё; пусто — всегда.</summary>
        public string When { get; set; } = "";

        /// <summary>
        /// Размер файла, байт (0 — неизвестен, спрашивается у сервера запросом HEAD).
        /// Названный размер нужен не для проверки, а для ЧЕСТНОГО ОКНА УСТАНОВКИ (T-185-S0):
        /// у пакета из двух десятков файлов на 18 ГБ два десятка запросов HEAD стоят минуты,
        /// и всё это время человек видит «размер неизвестен» — как раз перед самой долгой
        /// закачкой во всей программе.
        /// </summary>
        public long Size { get; set; }

        /// <summary>Файл нужен при выбранной сборке (flavor вида win-cuda-12.4-x64 / win-cpu-x64).</summary>
        public bool MatchesFlavor(string flavor) => When.Length == 0
            || When.Equals("cuda", StringComparison.OrdinalIgnoreCase)
               && flavor.Contains("cuda", StringComparison.OrdinalIgnoreCase)
            || When.Equals("cpu", StringComparison.OrdinalIgnoreCase)
               && !flavor.Contains("cuda", StringComparison.OrdinalIgnoreCase);
    }
}
