using AI2P.Core;

namespace AI2P.UI.Services;

/// <summary>
/// Причина паузы задачи, над которой работает ИИ (T-187), одной строкой. Живёт отдельно от
/// разметки, потому что показывается в двух местах — в представлении «в работе у ИИ» и в форме
/// задачи за статусом, — а выглядеть обязана одинаково.
/// </summary>
public static class AiPauseText
{
    /// <summary>Текст причины; пусто — паузы нет (агент работает).</summary>
    public static string Of(I18nService l, AiPauseDto? pause) => pause?.Kind switch
    {
        // лимит: главное — когда работа продолжится сама (T-121)
        AiPauseKinds.Limit => l["limit.startAfter"] + " " + Time(pause.Until),
        AiPauseKinds.Question => l["aiwork.pause.questions", pause.Questions],
        AiPauseKinds.Subtasks => l["aiwork.pause.subtasks"],
        // очередь иерархии открыта (T-224): задача ждёт своей очереди, статус у неё «ожидает»
        AiPauseKinds.Hierarchy => l["aiwork.pause.hierarchy"],
        // блокирующие задачи (T-6-S1): «ждём» — работа впереди, «отменена» — её не будет
        AiPauseKinds.Blocked => l["aiwork.pause.blocked"],
        AiPauseKinds.BlockerCancelled => l["aiwork.pause.blockerCancelled"],
        // круг тела цикла (T-299-S0)
        AiPauseKinds.Loop => l["aiwork.pause.loop"],
        _ => "",
    };

    /// <summary>Значок причины — тот же и в списке, и в форме задачи.</summary>
    public static string Icon(AiPauseDto? pause) => pause?.Kind switch
    {
        AiPauseKinds.Limit => MudBlazor.Icons.Material.Filled.Schedule,
        AiPauseKinds.Question => MudBlazor.Icons.Material.Filled.HelpOutline,
        AiPauseKinds.Subtasks => MudBlazor.Icons.Material.Filled.AccountTree,
        // тот же значок, что у кнопки «запустить иерархию» (T-209): пометка и кнопка,
        // которая её поставила, узнаются друг в друге с одного взгляда
        AiPauseKinds.Hierarchy => Ai2pIcons.TripleArrowRight,
        // замок — «задачу держит другая задача»; отменённая блокирующая рисуется запретом:
        // это не ожидание, а тупик, и отличаться от ожидания должно с одного взгляда (T-6-S1)
        AiPauseKinds.Blocked => MudBlazor.Icons.Material.Filled.Lock,
        AiPauseKinds.BlockerCancelled => MudBlazor.Icons.Material.Filled.Block,
        AiPauseKinds.Loop => MudBlazor.Icons.Material.Filled.Loop,
        _ => "",
    };

    /// <summary>Момент времени по-человечески: «14.08 09:30» в местной зоне.</summary>
    public static string Time(DateTime? utc) =>
        utc is { } at ? at.ToLocalTime().ToString("dd.MM HH:mm") : "";
}
