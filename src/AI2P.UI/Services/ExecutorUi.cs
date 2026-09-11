using AI2P.Core.Entities;
using MudBlazor;

namespace AI2P.UI.Services;

/// <summary>
/// ОДИН ЗНАЧОК НА ВСЕ СПИСКИ ИСПОЛНИТЕЛЕЙ (T-153-S0). До третьего типа значок выбирался
/// на месте выражением <c>Kind == Ai ? SmartToy : Person</c> — в пяти файлах, и «авто ПО»
/// в каждом из них молча показалось бы человечком. Правило теперь одно и лежит в одном месте.
/// </summary>
public static class ExecutorUi
{
    /// <summary>Значок типа исполнителя: человек, агент, «авто ПО» (внешняя программа).</summary>
    public static string Icon(ExecutorKind kind) => kind switch
    {
        ExecutorKind.Ai => Icons.Material.Filled.SmartToy,
        ExecutorKind.Software => Icons.Material.Filled.Terminal,
        _ => Icons.Material.Filled.Person,
    };

    /// <summary>Ключ словаря с названием типа: <c>executors.kind.human|ai|software</c>.</summary>
    public static string KindKey(ExecutorKind kind) => kind switch
    {
        ExecutorKind.Ai => "executors.kind.ai",
        ExecutorKind.Software => "executors.kind.software",
        _ => "executors.kind.human",
    };
}
