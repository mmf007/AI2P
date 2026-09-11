using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// МАНИФЕСТ ПЛАГИНА (T-111-S0) — файл дистрибутива <c>plugins/&lt;код&gt;/plugin.json</c>
/// в каталоге данных, по образцу <c>models/packages.json</c> и профайлов моделей.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ФАЙЛ, А НЕ КОЛОНКИ В БАЗЕ: манифест — это ОПИСАНИЕ ВОЗМОЖНОСТЕЙ, оно
/// приходит вместе с программой и меняется вместе с ней. В базе организации лежит только
/// то, что человек решил (<see cref="Entities.PluginRecord"/>: завёл, настроил, выключил).
/// Ровно так же разделены декларация модели и её запись в справочнике: правка файла видна
/// сразу, миграции схемы на новый плагин не нужно (наука T-13-S1 про профайлы моделей).
///
/// ПОЧЕМУ РАЗБОР ПЕРЕЖИВАЕТ ЛЮБУЮ НЕПОЛНОТУ: файл правят руками, и опечатка в необязательном
/// блоке не должна ронять старт сервера. Обязательны ровно два поля — <c>code</c> и
/// <c>kind</c>; всё остальное имеет умолчание. Негодный манифест возвращается как
/// <c>null</c> из <see cref="Parse"/>, а разбирающий его решает, писать ли о нём в лог.
///
/// СЕРИАЛИЗАЦИЯ — camelCase, как у всего API (<c>Ai2pJson.Options</c>): манифест читают и
/// пишут те же люди, что смотрят в тело запроса, и два разных написания полей в одной
/// системе — источник молчаливых ошибок.
///
/// СОСТАВ (полное описание с примерами — <c>doc/T-111-S0_манифест_плагина.md</c>):
/// <code>
/// {
///   "code": "editor.shotcut",          // обязательно, он же имя каталога
///   "kind": "gateway",                 // обязательно: gateway | mcp
///   "version": 1,                      // версия САМОГО манифеста
///   "name":  { "ru": "Shotcut", "en": "Shotcut" },   // строка или словарь языков
///   "description": { … },
///   "software": { … },                 // где взять программу; нет блока — софт не нужен
///   "actions":  [ … ],                 // только у gateway
///   "connection": { … },               // только у mcp
///   "experience": [ … ],
///   "settings":   [ … ],
///   "doc": { "ru": "plugins/editor.shotcut/doc.ru.md", … }
/// }
/// </code>
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Имя файла манифеста внутри каталога плагина.</summary>
    public const string FileName = "plugin.json";

    /// <summary>Каталог плагинов в каталоге данных (относительный путь).</summary>
    public const string Dir = "plugins";

    /// <summary>Код плагина: <c>editor.shotcut</c>. Он же имя каталога и ключ раздела
    /// <c>plugins</c> в config.json сервера.</summary>
    public string Code { get; set; } = "";

    /// <summary>Вид: <see cref="Entities.PluginKinds"/> — <c>gateway</c> или <c>mcp</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Версия манифеста; она же ложится в запись плагина при заведении.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Название по языкам.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>Одна фраза о том, что этот плагин делает.</summary>
    public LocalizedText Description { get; set; } = new();

    /// <summary>
    /// ПРОГРАММЫ ПЛАГИНА (T-146-S0). Блок <c>software</c> манифеста бывает и объектом («одна
    /// программа», как было у всех плагинов до 1.116), и МАССИВОМ объектов: плагин вправе
    /// вести к нескольким программам сразу (конвертор + плеер, редактор + рендер-движок), и
    /// тогда у каждой свой пакет, свой поиск в PATH и СВОЙ ручной путь в config.json этого
    /// сервера. Разбор обоих написаний даёт один и тот же список.
    /// </summary>
    public List<PluginSoftware> SoftwareList { get; set; } = [];

    /// <summary>ГЛАВНАЯ программа плагина — первая в списке; null — программа не нужна вовсе.
    /// Ею работают шлюзовые действия (<c>GatewayPlugin.SoftwarePath</c>), и она же отвечает за
    /// старое поле <c>path</c> в config.json.</summary>
    public PluginSoftware? Software => SoftwareList.Count > 0 ? SoftwareList[0] : null;

    /// <summary>Что плагин умеет — только у <c>gateway</c>. Каждое действие обязано
    /// получить запись в справочнике действий при инициализации: инструмент БЕЗ записи
    /// справочника правилами безопасности не закрывается вовсе (наука 2d3af8da).</summary>
    public List<PluginAction> Actions { get; set; } = [];

    /// <summary>Подключение к серверу MCP — только у <c>mcp</c>; у шлюза null.</summary>
    public PluginConnection? Connection { get; set; }

    /// <summary>Записи опыта, которые инициализация кладёт в общий опыт организации.</summary>
    public List<PluginExperience> Experience { get; set; } = [];

    /// <summary>Состав настроек записи (<c>PluginRecord.SettingsJson</c>).</summary>
    public List<PluginSetting> Settings { get; set; } = [];

    /// <summary>Документ плагина по языкам: путь <c>.md</c> относительно каталога
    /// <c>doc/&lt;язык&gt;/</c> либо относительно каталога данных — как у документов моделей.</summary>
    public Dictionary<string, string> Doc { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ВИД ФАЙЛА ПРОЕКТА, который шлюз собирает из медиатеки (блок <c>export</c>, T-115-S0):
    /// шаблоны документа, клипа и дорожек плюс способ экранирования. Генератор при этом ОДИН
    /// на все шлюзы (<see cref="TimelineExport"/>) — новый редактор это манифест, а не класс.
    /// null — шлюз файла проекта не собирает (так у ffmpeg-конвертора T-116-S0).
    /// </summary>
    public TimelineFormat? Export { get; set; }

    /// <summary>Как звать внешнюю программу на рендер без окна (блок <c>render</c>);
    /// null — рендера у шлюза нет (так у DaVinci Resolve и OpenShot).</summary>
    public RenderCommand? Render { get; set; }

    /// <summary>
    /// НАБОР ИМЕНОВАННЫХ ОПЕРАЦИЙ НАД ФАЙЛАМИ (блок <c>convert</c>, T-116-S0): перекодировать
    /// в промежуточный формат монтажа, извлечь звук, склеить по списку, привести к общему
    /// кадру. null — плагин файлов не перекодирует (так у всех четырёх шлюзов в редакторы).
    /// Действие ссылается на операцию полем <c>op</c> при <c>"role": "convert"</c>.
    /// </summary>
    public ConvertOps? Convert { get; set; }

    /// <summary>
    /// НАБОР ИМЕНОВАННЫХ ОПЕРАЦИЙ ДОЛГОГО ЗАПУСКА (блок <c>run</c>, T-155-S0): обучение LoRA
    /// и прочая работа на часы, которая оформляется отдельной задачей с исполнителем
    /// «авто ПО». От <see cref="Convert"/> отличается тем, что у операции есть рабочий
    /// каталог, тайм-аут МОЛЧАНИЯ отдельно от общего предела и правило разбора результата.
    /// null — плагин долгих запусков не объявляет.
    /// </summary>
    public RunOps? Run { get; set; }

    /// <summary>Манифест годен: код есть, вид известен, и блок соответствует виду.</summary>
    public bool IsValid =>
        Code.Length > 0 && Entities.PluginKinds.IsKnown(Kind)
        && (Kind != Entities.PluginKinds.Mcp || Connection is not null);

    /// <summary>
    /// Разобрать манифест из текста; null — текст не JSON-объект либо манифест негоден
    /// (нет кода, неизвестный вид, у mcp нет блока подключения).
    /// </summary>
    public static PluginManifest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var manifest = new PluginManifest
            {
                Code = JsonRead.Str(root, "code"),
                Kind = JsonRead.Str(root, "kind"),
                Version = Math.Max(1, JsonRead.Int(root, "version", 1)),
                Name = LocalizedText.From(root, "name"),
                Description = LocalizedText.From(root, "description"),
                SoftwareList = PluginSoftware.ListFrom(root, "software"),
                Connection = PluginConnection.From(root, "connection"),
                Export = TimelineFormat.From(root, "export"),
                Render = RenderCommand.From(root, "render"),
                Convert = ConvertOps.From(root, "convert"),
                Run = RunOps.From(root, "run"),
            };
            manifest.Actions = Each(root, "actions", PluginAction.From);
            manifest.Experience = Each(root, "experience", PluginExperience.From);
            manifest.Settings = Each(root, "settings", PluginSetting.From);
            if (root.TryGetProperty("doc", out var docs) && docs.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in docs.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String &&
                        p.Value.GetString() is { Length: > 0 } path)
                    {
                        manifest.Doc[p.Name] = path.Trim();
                    }
                }
            }
            // у шлюза блока подключения быть не должно, у MCP — списка действий: инструменты
            // сервер MCP называет сам при инициализации, и список в файле разъехался бы с ним
            if (manifest.Kind == Entities.PluginKinds.Mcp)
            {
                manifest.Actions = [];
                // сборка файла проекта, рендер и конвертор — свойства ШЛЮЗА: у сервера MCP
                // свои инструменты, и наш экспортёр к ним отношения не имеет
                manifest.Export = null;
                manifest.Render = null;
                manifest.Convert = null;
                manifest.Run = null;
            }
            else
            {
                manifest.Connection = null;
            }
            return manifest.IsValid ? manifest : null;
        }
    }

    /// <summary>Путь манифеста относительно каталога данных: <c>plugins/&lt;код&gt;/plugin.json</c>.</summary>
    public static string PathOf(string code) => $"{Dir}/{code}/{FileName}";

    /// <summary>
    /// Прочитать манифест с диска: <c>&lt;каталог данных&gt;/plugins/&lt;код&gt;/plugin.json</c>.
    /// null — файла нет либо он негоден.
    /// </summary>
    public static PluginManifest? Read(string dataDir, string code)
    {
        if (string.IsNullOrWhiteSpace(dataDir) || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }
        var file = Path.Combine(dataDir, Dir, code, FileName);
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Все манифесты каталога данных, по одному на подкаталог <c>plugins/*</c>. Негодные
    /// пропускаются молча: один сломанный файл не должен прятать остальные плагины.
    /// Порядок — по коду, чтобы список был одинаков на всех серверах.
    /// </summary>
    public static List<PluginManifest> ReadAll(string dataDir)
    {
        var root = string.IsNullOrWhiteSpace(dataDir) ? "" : Path.Combine(dataDir, Dir);
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }
        var found = new List<PluginManifest>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var manifest = Read(dataDir, Path.GetFileName(dir));
            // код внутри файла главнее имени каталога — по нему живут запись и config.json,
            // а каталог человек мог переименовать
            if (manifest is not null)
            {
                found.Add(manifest);
            }
        }
        return [.. found.OrderBy(m => m.Code, StringComparer.Ordinal)];
    }

    private static List<T> Each<T>(JsonElement root, string name, Func<JsonElement, T?> from)
        where T : class =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Select(from).Where(v => v is not null).Select(v => v!)]
            : [];
}

/// <summary>
/// ТЕКСТ ПО ЯЗЫКАМ в файле дистрибутива. В JSON это либо строка («одинаково на всех
/// языках»), либо словарь <c>{ "ru": …, "en": … }</c>: заставлять автора плагина заводить
/// пять переводов ради слова «ffmpeg» незачем, а без словаря вовсе название пришлось бы
/// хранить в общих словарях интерфейса — то есть править код на каждый новый плагин.
///
/// Отбор языка: точное совпадение → английский → первый попавшийся. Английский посередине
/// не случайно — это базовый язык скриптов и документации (T-65-S0).
/// </summary>
public sealed class LocalizedText
{
    private readonly Dictionary<string, string> _byLang = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Пусто — текста нет ни на одном языке.</summary>
    public bool IsEmpty => _byLang.Count == 0;

    /// <summary>Языки, на которых текст задан.</summary>
    public IReadOnlyCollection<string> Languages => _byLang.Keys;

    /// <summary>Текст на этом языке; нет — английский; нет и его — любой; совсем нет — пусто.</summary>
    public string Text(string? lang)
    {
        if (lang is { Length: > 0 } && _byLang.TryGetValue(lang, out var own))
        {
            return own;
        }
        return _byLang.TryGetValue("en", out var en) ? en : _byLang.Values.FirstOrDefault() ?? "";
    }

    public void Set(string lang, string text) => _byLang[lang] = text;

    internal static LocalizedText From(JsonElement obj, string name)
    {
        var text = new LocalizedText();
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
        {
            return text;
        }
        if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } plain)
        {
            // строка без языков: одна и та же на всех — так пишут имена программ
            foreach (var lang in Loc.Languages)
            {
                text.Set(lang, plain.Trim());
            }
            text.Set("en", plain.Trim());
            return text;
        }
        if (v.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in v.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String &&
                    p.Value.GetString() is { Length: > 0 } s)
                {
                    text.Set(p.Name, s.Trim());
                }
            }
        }
        return text;
    }
}

/// <summary>
/// БЛОК <c>software</c> МАНИФЕСТА — где взять программу, к которой ведёт шлюз.
///
/// Поля <see cref="System"/> намеренно повторяют блок <c>system</c> справочника пакетов
/// (<c>models/packages.json</c>, T-4-S0): melt, ffmpeg и Blender человек чаще всего ставит
/// сам, и первым делом их надо ИСКАТЬ, а не качать рядом второй экземпляр.
///
/// НАЙДЕННЫЙ ПУТЬ В БАЗУ НЕ ПОПАДАЕТ НИКОГДА — он ложится в config.json этого сервера
/// (<see cref="PathKey"/> — имя поля ручного пути в форме плагина).
/// </summary>
public sealed class PluginSoftware
{
    /// <summary>Имя главной (первой) записи софта по умолчанию: под ним и под старым полем
    /// <c>path</c> в config.json живёт путь к программе, которой работают действия шлюза.</summary>
    public const string MainId = "main";

    /// <summary>
    /// ИМЯ ЗАПИСИ СОФТА ВНУТРИ ПЛАГИНА (T-146-S0): под ним лежит ручной путь в config.json
    /// этого сервера, когда программ у плагина несколько. В манифесте поле <c>id</c>
    /// необязательно — разбор берёт <c>pathKey</c>, потом <c>package</c>, потом
    /// <see cref="MainId"/>. Имя обязано быть ПОСТОЯННЫМ: смена имени означает потерю уже
    /// указанного человеком пути.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Название программы для человека (строка блока <c>name</c>); пусто — покажем
    /// код пакета либо <see cref="Id"/>.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>Код пакета справочника (<c>models/packages.json</c>); пусто — пакета нет,
    /// программу человек ставит сам и показывает путь руками (так у DaVinci Resolve).</summary>
    public string Package { get; set; } = "";

    /// <summary>Без этой программы плагин не работает вовсе. false — программа нужна части
    /// действий (рендер), а остальное (экспорт проекта) работает и без неё.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Чем искать уже установленную программу; null — искать нечем, только руками.</summary>
    public SystemProbe? System { get; set; }

    /// <summary>Имя поля ручного пути в разделе <c>plugins</c> файла config.json:
    /// <c>meltPath</c>, <c>ffmpegPath</c>. Пусто — ручного пути у плагина нет.</summary>
    public string PathKey { get; set; } = "";

    /// <summary>Что делать человеку, если программа не нашлась.</summary>
    public LocalizedText Hint { get; set; } = new();

    /// <summary>Название программы для человека: имя из манифеста, иначе код пакета, иначе
    /// <see cref="Id"/> — пустой строки в таблице быть не должно.</summary>
    public string Title(string? lang)
    {
        var own = Name.Text(lang);
        return own.Length > 0 ? own : Package.Length > 0 ? Package : Id;
    }

    /// <summary>
    /// Разобрать блок <c>software</c>: объект («одна программа») либо массив объектов
    /// (несколько). Имена записей проставляются здесь же и обязаны быть РАЗНЫМИ — по ним
    /// расходятся ручные пути в config.json, и два одинаковых имени означали бы один путь
    /// на две разные программы.
    /// </summary>
    internal static List<PluginSoftware> ListFrom(JsonElement root, string name)
    {
        var list = new List<PluginSoftware>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v))
        {
            return list;
        }
        if (v.ValueKind == JsonValueKind.Array)
        {
            list.AddRange(v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object).Select(One));
        }
        else if (v.ValueKind == JsonValueKind.Object)
        {
            list.Add(One(v));
        }
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var id = list[i].Id.Length > 0 ? list[i].Id
                : list[i].PathKey.Length > 0 ? list[i].PathKey
                : list[i].Package.Length > 0 ? list[i].Package
                : i == 0 ? MainId : MainId + (i + 1);
            var unique = id;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = id + n;
            }
            list[i].Id = unique;
        }
        return list;
    }

    private static PluginSoftware One(JsonElement v) => new()
    {
        Id = JsonRead.Str(v, "id"),
        Name = LocalizedText.From(v, "name"),
        Package = JsonRead.Str(v, "package"),
        Required = JsonRead.Bool(v, "required", true),
        System = SystemProbe.From(v, "system"),
        PathKey = JsonRead.Str(v, "pathKey"),
        Hint = LocalizedText.From(v, "hint"),
    };

    /// <summary>Как узнать уже установленную программу: команда в PATH и годные версии.
    /// Поля те же, что у одноимённого блока справочника пакетов (T-4-S0).</summary>
    public sealed class SystemProbe
    {
        /// <summary>Имена команд в PATH по порядку: melt, qmelt …</summary>
        public List<string> Commands { get; set; } = [];

        /// <summary>Чем спросить версию («--version»); пусто — версия не проверяется.</summary>
        public string VersionArgs { get; set; } = "";

        /// <summary>Наименьшая годная версия вида «7.0»; пусто — снизу не ограничена.</summary>
        public string MinVersion { get; set; } = "";

        /// <summary>Наибольшая годная версия; пусто — сверху не ограничена.</summary>
        public string MaxVersion { get; set; } = "";

        internal static SystemProbe? From(JsonElement obj, string name)
        {
            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)
                || v.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var probe = new SystemProbe
            {
                Commands = JsonRead.Strings(v, "commands"),
                VersionArgs = JsonRead.Str(v, "versionArgs"),
                MinVersion = JsonRead.Str(v, "minVersion"),
                MaxVersion = JsonRead.Str(v, "maxVersion"),
            };
            return probe.Commands.Count > 0 ? probe : null;
        }
    }
}

/// <summary>
/// ДЕЙСТВИЕ ШЛЮЗА — то, что плагин умеет: «собрать проект Shotcut», «отрендерить melt»,
/// «перекодировать ffmpeg».
///
/// <see cref="Code"/> это код записи СПРАВОЧНИКА ДЕЙСТВИЙ, которую инициализация плагина
/// обязана завести. Без такой записи правило безопасности не закрывает инструмент вовсе:
/// <c>ActionCatalogService.CodeByTool</c> вернёт null, а <c>AgentToolset.Authorize</c> в этом
/// случае разрешает вызов (наука 2d3af8da). Поэтому поле обязательное — действие без кода
/// разбор выбрасывает.
/// </summary>
public sealed class PluginAction
{
    /// <summary>Код записи справочника действий: <c>AI2P.Plugins.Shotcut.Export</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>Имя инструмента, которым его зовёт агент: <c>shotcut_export</c>.</summary>
    public string Tool { get; set; } = "";

    public LocalizedText Title { get; set; } = new();
    public LocalizedText Description { get; set; } = new();

    /// <summary>Действию нужна программа из блока <c>software</c> (рендеру — нужна,
    /// сборке файла проекта — нет).</summary>
    public bool NeedsSoftware { get; set; }

    /// <summary>Собрать файл проекта из медиатеки (<see cref="PluginManifest.Export"/>).</summary>
    public const string RoleExport = "export";

    /// <summary>Посчитать ролик внешней программой без окна (<see cref="PluginManifest.Render"/>).</summary>
    public const string RoleRender = "render";

    /// <summary>
    /// ВЫПОЛНИТЬ ИМЕНОВАННУЮ ОПЕРАЦИЮ КОНВЕРТОРА (<see cref="PluginManifest.Convert"/>,
    /// T-116-S0) — какую именно, сказано полем <see cref="Op"/>. Роль отдельная от
    /// <see cref="RoleRender"/> потому, что рендер у шлюза ОДИН и работает с монтажным
    /// листом, а операций конвертора несколько и работают они с любым файлом.
    /// </summary>
    public const string RoleConvert = "convert";

    /// <summary>
    /// ЧТО ЭТО ДЕЙСТВИЕ ДЕЛАЕТ НА САМОМ ДЕЛЕ (T-115-S0) — <see cref="RoleExport"/> или
    /// <see cref="RoleRender"/>. Имя инструмента у каждого шлюза СВОЁ (иначе четыре шлюза
    /// объявили бы один и тот же <c>timeline_write</c>, и <c>CodeByTool</c> перестал бы
    /// различать, чьё правило безопасности сработало), а работа за ним одна и та же — вот
    /// она и названа ролью. Пусто — действие исполняет сам плагин, движку оно неизвестно.
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>Имя операции блока <c>convert</c> (<see cref="ConvertOp.Op"/>) — только у
    /// роли <see cref="RoleConvert"/>. Действие БЕЗ имени операции не публикуется: «сделать
    /// что-нибудь конвертором» — это и есть произвольная командная строка.</summary>
    public string Op { get; set; } = "";

    /// <summary>
    /// ОДИН ЭКЗЕМПЛЯР (T-154-S0): двух таких работ разом на одном компьютере быть не должно —
    /// обучение LoRA занимает видеокарту целиком, и второй запуск не «работает медленнее», а
    /// падает по нехватке памяти. Очередь запуска смотрит на этот флаг ДО старта, и задача от
    /// него ЖДЁТ, а не встаёт с ошибкой (<c>SoftwareConnector.SingleInstanceBusy</c>).
    /// Сторожится ПЛАГИН, а не исполнитель: исполнителей у одной программы бывает несколько.
    /// </summary>
    public bool SingleInstance { get; set; }

    /// <summary>
    /// ТАЙМ-АУТ МОЛЧАНИЯ, секунд (T-154-S0): пока программа печатает, она жива, и обрывать её
    /// по длительности значит терять часы счёта. 0 — умолчание коннектора
    /// (<c>SoftwareConnector.DefaultIdleSec</c>).
    /// </summary>
    public int IdleTimeoutSec { get; set; }

    /// <summary>Общий предел длительности, секунд (T-154-S0) — крупный, «считает, но
    /// бесконечно». 0 — своё время операции <c>convert</c>, а нет и его — сутки.</summary>
    public int TimeoutSec { get; set; }

    internal static PluginAction? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var action = new PluginAction
        {
            Code = JsonRead.Str(e, "code"),
            Tool = JsonRead.Str(e, "tool"),
            Title = LocalizedText.From(e, "title"),
            Description = LocalizedText.From(e, "description"),
            NeedsSoftware = JsonRead.Bool(e, "needsSoftware", false),
            Role = JsonRead.Str(e, "role").ToLowerInvariant(),
            Op = JsonRead.Str(e, "op"),
            SingleInstance = JsonRead.Bool(e, "singleInstance", false),
            IdleTimeoutSec = Math.Max(0, JsonRead.Int(e, "idleTimeoutSec", 0)),
            TimeoutSec = Math.Max(0, JsonRead.Int(e, "timeoutSec", 0)),
        };
        return action.Code.Length > 0 && action.Tool.Length > 0 ? action : null;
    }
}

/// <summary>
/// ПОДКЛЮЧЕНИЕ К СЕРВЕРУ MCP (<c>kind: "mcp"</c>). Транспорта два: <c>stdio</c> — сервер
/// запускается процессом (команда и аргументы), <c>http</c> — сервер уже где-то работает
/// (адрес и заголовки).
///
/// ТОКЕНА ЗДЕСЬ НЕТ И БЫТЬ НЕ МОЖЕТ: в манифесте лежит только ССЫЛКА на секрет
/// (<see cref="SecretRef"/>), а сам он — в <c>secrets/*.json</c> этого сервера, как пароль
/// почты (T-272). Манифест — файл дистрибутива, он одинаков у всех.
/// </summary>
public sealed class PluginConnection
{
    public const string Stdio = "stdio";
    public const string Http = "http";

    /// <summary>Транспорт: <c>stdio</c> или <c>http</c>.</summary>
    public string Transport { get; set; } = Stdio;

    /// <summary>Команда запуска сервера MCP (транспорт stdio).</summary>
    public string Command { get; set; } = "";

    /// <summary>Аргументы команды (транспорт stdio).</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>Адрес сервера MCP (транспорт http).</summary>
    public string Url { get; set; } = "";

    /// <summary>Ссылка на секрет в хранилище: <c>plugin.&lt;код&gt;.token</c>.</summary>
    public string SecretRef { get; set; } = "";

    /// <summary>Заголовок, в который подставляется секрет (транспорт http):
    /// <c>Authorization</c>. Пусто — секрет не нужен.</summary>
    public string SecretHeader { get; set; } = "";

    /// <summary>Подключение описано целиком: у stdio есть команда, у http — адрес.</summary>
    public bool IsComplete => Transport == Stdio ? Command.Length > 0 : Url.Length > 0;

    internal static PluginConnection? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var transport = JsonRead.Str(v, "transport", Stdio).ToLowerInvariant();
        return new PluginConnection
        {
            Transport = transport == Http ? Http : Stdio,
            Command = JsonRead.Str(v, "command"),
            Args = JsonRead.Strings(v, "args"),
            Url = JsonRead.Str(v, "url"),
            SecretRef = JsonRead.Str(v, "secretRef"),
            SecretHeader = JsonRead.Str(v, "secretHeader"),
        };
    }
}

/// <summary>
/// ЗАПИСЬ ОПЫТА, которую плагин приносит с собой: «монтажный лист Shotcut собирается так-то».
/// Инициализация кладёт её в ОБЩИЙ опыт организации (T-11-S0) с этим навыком — так она
/// доедет до каждого исполнителя нужного навыка и не будет привязана к одному проекту.
/// </summary>
public sealed class PluginExperience
{
    /// <summary>Код навыка справочника (<c>video-edit</c>); пусто — запись общая для всех.</summary>
    public string Skill { get; set; } = "";

    /// <summary>Текст записи по языкам: опыт уезжает в промпт на языке команды.</summary>
    public LocalizedText Text { get; set; } = new();

    internal static PluginExperience? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var record = new PluginExperience
        {
            Skill = JsonRead.Str(e, "skill"),
            Text = LocalizedText.From(e, "text"),
        };
        return record.Text.IsEmpty ? null : record;
    }
}

/// <summary>
/// ПОЛЕ НАСТРОЙКИ ЗАПИСИ ПЛАГИНА: то, что человек задаёт в форме, а хранится в
/// <c>PluginRecord.SettingsJson</c>. Здесь — только ОПИСАНИЕ поля; значение живёт в записи.
///
/// Что сюда класть НЕЛЬЗЯ: путь к программе и любой признак «установлено здесь» — они
/// пер-серверные и лежат в config.json (см. <see cref="PluginSoftware.PathKey"/>).
/// </summary>
public sealed class PluginSetting
{
    public const string TypeText = "text";
    public const string TypeNumber = "number";
    public const string TypeBool = "bool";
    public const string TypeChoice = "choice";

    public static readonly string[] Types = [TypeText, TypeNumber, TypeBool, TypeChoice];

    /// <summary>Имя поля в <c>SettingsJson</c>: <c>fps</c>.</summary>
    public string Key { get; set; } = "";

    /// <summary>Вид поля: <see cref="Types"/>; неизвестный превращается в текст.</summary>
    public string Type { get; set; } = TypeText;

    public LocalizedText Title { get; set; } = new();

    /// <summary>Значение по умолчанию, как оно ляжет в JSON записи (строкой).</summary>
    public string Default { get; set; } = "";

    /// <summary>Допустимые значения (вид <c>choice</c>).</summary>
    public List<string> Choices { get; set; } = [];

    internal static PluginSetting? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var type = JsonRead.Str(e, "type", TypeText);
        var setting = new PluginSetting
        {
            Key = JsonRead.Str(e, "key"),
            Type = Array.IndexOf(Types, type) >= 0 ? type : TypeText,
            Title = LocalizedText.From(e, "title"),
            Choices = JsonRead.Strings(e, "choices"),
        };
        // умолчание бывает числом и флажком, а не только строкой: приводим к строке —
        // значения всё равно ложатся в свободный JSON записи
        if (e.TryGetProperty("default", out var def))
        {
            setting.Default = def.ValueKind switch
            {
                JsonValueKind.String => def.GetString()!.Trim(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                    def.GetRawText(),
                _ => "",
            };
        }
        return setting.Key.Length > 0 ? setting : null;
    }
}
