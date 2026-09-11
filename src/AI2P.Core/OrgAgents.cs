using AI2P.Core.Api;

namespace AI2P.Core;

/// <summary>
/// Кого остановит смена организации (T-188).
///
/// Список для предупреждения и список для остановки — ОДИН И ТОТ ЖЕ: обещание «эти агенты
/// будут остановлены» обязано совпадать с делом. Поэтому отбор живёт здесь, а не двумя
/// похожими выражениями в двух местах обработчика.
///
/// Отличие от представления «в работе у ИИ» (T-187) ровно одно: туда попадают и задачи
/// с ПЕРЕНЕСЁННЫМ стартом (у исполнителя кончился лимит) — за ними не стоит ни живого
/// задания, ни подключения, останавливать там нечего, и в предупреждение они не идут.
/// </summary>
public static class OrgAgents
{
    /// <summary>
    /// Строки «в работе у ИИ», за которыми стоит живое задание.
    /// </summary>
    /// <param name="aiWork">Всё, чем заняты агенты (<c>TaskService.AiWork</c>).</param>
    /// <param name="activeJobIds">Незакрытые задания организации (<c>JobService.ListActive</c>).</param>
    public static List<AiWorkItemDto> Stoppable(IEnumerable<AiWorkItemDto> aiWork,
        IEnumerable<string> activeJobIds)
    {
        var active = activeJobIds.ToHashSet();
        return aiWork.Where(item => item.JobId.Length > 0 && active.Contains(item.JobId)).ToList();
    }
}
