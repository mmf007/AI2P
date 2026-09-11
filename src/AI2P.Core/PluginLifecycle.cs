using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// ЧТО СЕЙЧАС С ПЛАГИНОМ НА ЭТОМ СЕРВЕРЕ (T-112-S0). Итог складывается из ДВУХ величин
/// разной природы, и в этом вся соль раздела:
/// <list type="bullet">
/// <item>состояние записи (<see cref="PluginStates"/>) — РЕШЕНИЕ ОРГАНИЗАЦИИ, оно
/// реплицируется: «мы монтируем через Shotcut», «этот плагин выключен»;</item>
/// <item>найден ли софт ЗДЕСЬ (<see cref="PluginSoftwareStatus"/>) — свойство КОМПЬЮТЕРА,
/// оно не реплицируется никогда: путь к Blender у соседа-линуксоида другой, а у половины
/// серверов Blender не стоит вовсе.</item>
/// </list>
/// Складывать их в одну колонку базы нельзя (9fee2887), а показывать человеку надо ОДНИМ
/// словом — вот это слово тут и считается.
/// </summary>
public static class PluginLifecycle
{
    /// <summary>ЖДЁТ РЕШЕНИЯ ПРО СОФТ (состояние 2 задания «софт ищется»): проверка на этом
    /// сервере прошла, и программы нет. В базе такого состояния нет намеренно — оно своё
    /// у каждого сервера и вычисляется на месте.</summary>
    public const string ProbingSoftware = "probing";

    /// <summary>Пять состояний жизненного цикла по порядку — для форм и проверок. Четыре из
    /// них хранятся в записи (<see cref="PluginStates"/>), «софт ищется» считается на месте.</summary>
    public static readonly string[] All =
    [
        PluginStates.Declared, ProbingSoftware, PluginStates.Initialized,
        PluginStates.Disabled, PluginStates.Removed,
    ];

    /// <summary>
    /// Состояние, которое человек видит в списке плагинов НА ЭТОМ сервере.
    /// Правило простое: решение организации главнее, а «инициализирован» без софта —
    /// это не «работает», а «софт ищется». Молчать об этом нельзя: иначе получится модель,
    /// которая «есть, а задание не идёт» (cd91ad4e).
    /// </summary>
    /// <param name="state">Состояние записи (<see cref="PluginStates"/>).</param>
    /// <param name="software">Итог поиска софта на этом сервере.</param>
    public static string Shown(string? state, PluginSoftwareStatus software) =>
        (state ?? "") switch
        {
            PluginStates.Disabled => PluginStates.Disabled,
            PluginStates.Removed => PluginStates.Removed,
            PluginStates.Initialized =>
                software is PluginSoftwareStatus.Found or PluginSoftwareStatus.NotNeeded
                    ? PluginStates.Initialized
                    : ProbingSoftware,
            _ => PluginStates.Declared,
        };

    /// <summary>
    /// ПУБЛИКУЮТСЯ ЛИ ДЕЙСТВИЯ ПЛАГИНА АГЕНТУ на этом сервере. Правило то же, что у локальной
    /// модели без установленных файлов (cd91ad4e): софта нет и ручной путь не указан —
    /// действия не публикуются, и задача с ними на такой сервер не попадает. Выключенный
    /// плагин не публикуется тоже, но записи его остаются (мягкий выключатель).
    /// </summary>
    public static bool Publishes(string? state, PluginSoftwareStatus software) =>
        state == PluginStates.Initialized
        && software is PluginSoftwareStatus.Found or PluginSoftwareStatus.NotNeeded;

    /// <summary>Название состояния для человека; неизвестное отдаётся как есть.</summary>
    public static string Title(string state, string? lang = null) =>
        Array.IndexOf(All, state) >= 0 ? Loc.In(lang, "plugin.state." + state) : state;
}

/// <summary>
/// ИТОГ ПОИСКА СОФТА ПЛАГИНА НА ЭТОМ СЕРВЕРЕ (T-112-S0). Три исхода задания разведены
/// намеренно: человеку в каждом из них нужна РАЗНАЯ кнопка.
/// </summary>
public enum PluginSoftwareStatus
{
    /// <summary>Внешняя программа плагину не нужна вовсе (блока <c>software</c> нет).</summary>
    NotNeeded,

    /// <summary>Нашли: программа есть в PATH либо человек указал ручной путь, и версия годная.</summary>
    Found,

    /// <summary>Не нашли, но поставить можем — у плагина назван пакет справочника
    /// (<c>models/packages.json</c>). Человеку — кнопка «Установить».</summary>
    CanInstall,

    /// <summary>Не нашли и поставить не можем: дистрибутив отдаётся за формой регистрации и
    /// постоянной прямой ссылки нет (случай DaVinci Resolve, ~3–4 ГБ). Человеку — поле
    /// «укажите, где он уже стоит».</summary>
    NeedsManualPath,
}

/// <summary>
/// ИТОГ ПОИСКА СОФТА СТРОКОЙ (T-114-S0) — тем же способом, каким наружу отдаются все прочие
/// перечисления API: экран сравнивает значение с константой, а не с числом, и разбор ответа
/// не ломается от перестановки членов <see cref="PluginSoftwareStatus"/>.
/// </summary>
public static class PluginSoftwareCodes
{
    public const string NotNeeded = "notNeeded";
    public const string Found = "found";
    public const string CanInstall = "canInstall";
    public const string NeedsManualPath = "needsManualPath";

    public static string Of(PluginSoftwareStatus status) => status switch
    {
        PluginSoftwareStatus.Found => Found,
        PluginSoftwareStatus.CanInstall => CanInstall,
        PluginSoftwareStatus.NeedsManualPath => NeedsManualPath,
        _ => NotNeeded,
    };
}

/// <summary>
/// ПОМЕТКИ ВЛАДЕЛЬЦА У ВСЕГО, ЧТО ПОРОДИЛ ПЛАГИН (T-112-S0). Новых колонок под это НЕ
/// заводится: схему БД в ветке поднимает одна подзадача (T-111-S0), а владелец одинаково
/// хорошо узнаётся по уже существующим полям — коду записи справочника действий и
/// служебному ТЭГУ записи опыта. На этих двух пометках держится обязательное правило
/// 9d3a337a: у всего, что плагин породил, есть кнопка «убрать» — по ним снятие находит
/// свои записи.
///
/// ПОЧЕМУ У ОПЫТА ТЭГ, А НЕ <c>created_by</c>: колонка <c>experience.created_by</c> —
/// внешний ключ на исполнителей, и строкой <c>plugin:editor.shotcut</c> её не занять
/// (SQLite отвечает «FOREIGN KEY constraint failed»). Тэг же на отбор опыта не влияет:
/// <c>ExperienceService.TagsMatch</c> служебную пометку пропускает — иначе записи плагина
/// исчезли бы у каждой задачи, у которой тэги вообще проставлены.
/// </summary>
public static class PluginCodes
{
    /// <summary>Приставка кода действия, если манифест кода не назвал:
    /// <c>Plugin.editor.shotcut.render</c>.</summary>
    public const string ActionPrefix = "Plugin.";

    /// <summary>Приставка служебного тэга-владельца записи опыта: <c>plugin:editor.shotcut</c>.</summary>
    public const string ExperienceOwnerPrefix = "plugin:";

    /// <summary>
    /// ССЫЛКА НА СЕКРЕТ ПЛАГИНА ПО УМОЛЧАНИЮ (T-119-S0): <c>plugin.&lt;код&gt;.token</c> —
    /// имя файла в подкаталоге <c>secrets/</c> этого сервера. Манифест вправе назвать свою
    /// ссылку (<see cref="PluginConnection.SecretRef"/>), но САМ ТОКЕН в манифесте не лежит
    /// никогда: манифест — файл дистрибутива, он одинаков у всех, а секрет пер-серверный и
    /// не реплицируется (как пароль почты, T-272).
    /// </summary>
    public static string SecretRef(string pluginCode, string? fromManifest = null) =>
        fromManifest is { Length: > 0 } && fromManifest.Trim().Length > 0
            ? fromManifest.Trim()
            : "plugin." + pluginCode.Trim() + ".token";

    /// <summary>Код действия справочника для инструмента плагина. Код из манифеста
    /// (<see cref="PluginAction.Code"/>) главнее: автор плагина вправе назвать своё действие
    /// в общей иерархии кодов (<c>AI2P.Plugins.Shotcut.Export</c>).</summary>
    public static string Action(string pluginCode, PluginAction action) =>
        action.Code.Trim().Length > 0 ? action.Code.Trim() : Action(pluginCode, action.Tool);

    /// <summary>Код действия по умолчанию — когда манифест своего кода не назвал.</summary>
    public static string Action(string pluginCode, string tool) =>
        ActionPrefix + pluginCode.Trim() + "." + tool.Trim();

    /// <summary>Тэг-владелец записей опыта плагина.</summary>
    public static string ExperienceOwner(string pluginCode) =>
        ExperienceOwnerPrefix + pluginCode.Trim();

    /// <summary>Тэг — служебная пометка владельца, а не тема работы.</summary>
    public static bool IsPluginOwner(string? tag) =>
        tag is not null && tag.StartsWith(ExperienceOwnerPrefix, StringComparison.Ordinal);

    /// <summary>Плагин-владелец записи опыта; null — тэг не плагинный.</summary>
    public static string? OwnerPlugin(string? tag) =>
        IsPluginOwner(tag) ? tag![ExperienceOwnerPrefix.Length..] : null;
}
