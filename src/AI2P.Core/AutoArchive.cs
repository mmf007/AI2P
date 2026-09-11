namespace AI2P.Core;

/// <summary>
/// ДЕЙСТВИЯ, КОТОРЫЕ УМЕЕТ ЗАПУСКАТЬ РАСПИСАНИЕ (T-46-S0, выпуск 1.105).
///
/// До этой версии расписание умело ровно одно: копировать шаблон задач. Автоматическая
/// архивация задачей не является — это работа самой системы над своей базой, ставить ради
/// неё пустой шаблон и исполнителя было бы обманом. Поэтому у расписания появился ВТОРОЙ вид
/// срабатывания: вместо шаблона в нём называется ДЕЙСТВИЕ, и тогда шаблон не указывается
/// вовсе (<c>schedules.template_task_id</c> стал необязательным).
///
/// Список намеренно закрытый: действие расписания это не произвольная команда, а запись
/// справочника действий (<c>i18n/ActionCatalogService_*.json</c>), у которой есть имя,
/// подсказка и — главное — код, по которому её закрывают правила безопасности.
/// </summary>
public static class ScheduleActions
{
    /// <summary>Запустить автоматическую архивацию по правилам текущего архива.</summary>
    public const string AutoArchive = "auto_archive";

    public static readonly string[] All = [AutoArchive];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);

    /// <summary>
    /// Код записи справочника действий по коду действия расписания. Инструмент без записи
    /// в справочнике правилами безопасности не закрывается вовсе (наука выпуска 1.80),
    /// поэтому связь названа явно, а не собирается по имени.
    /// </summary>
    public static string CatalogCodeOf(string? action) => action switch
    {
        AutoArchive => "AI2P.Archives.AutoRun",
        _ => "",
    };

    /// <summary>Ключ словаря интерфейса с названием действия («Автоматическая архивация»).</summary>
    public static string TitleKeyOf(string? action) => "schedule.action." + (action ?? "");
}
