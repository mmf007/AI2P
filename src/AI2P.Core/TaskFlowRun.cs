using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Core;

/// <summary>
/// СОСТОЯНИЕ ВЫПОЛНЕНИЯ задач типа «Условие» и «Цикл» в очереди иерархии (T-299-S0, ветка
/// T-297-S0). Сам тип и параметры задачи (ветви, предел, флажки) — <see cref="TaskFlow"/>
/// (T-298-S0); здесь то, что пишут агент и движок ПО ХОДУ работы. Всё — ключи
/// <c>launch_json</c> самой задачи: реплицируется вместе с задачей, схемы БД не требует,
/// а сервер прежней версии незнакомые ключи просто не читает.
///
/// <para>ГЛАВНОЕ ПРАВИЛО ВЕТКИ: условия вычисляет АГЕНТ, AI2P их не анализирует. Решение агент
/// сообщает действием (подзадача T-300-S0), и действие записывает его ключом
/// <see cref="DecisionKey"/> — булевым значением JSON, третьего исхода нет. Для «Условия» это
/// «пошли по ветке Да / Нет», для обоих циклов — «условия цикла выполнены (идём на круг) /
/// не выполнены (выходим)».</para>
///
/// <para>Счётчик кругов цикла — <see cref="LoopPassKey"/>, а НЕ <c>recheckPass</c> кругов
/// повторной проверки (T-31-S0): тот обнуляется коннектором при каждом завершении задания без
/// ожидания (<c>TaskService.TakeRecheckWait</c>), а задача-анализатор цикла завершается так
/// после каждого круга — счёт сбрасывался бы в ноль, и предел не сработал бы никогда.</para>
/// </summary>
public static class TaskFlowRun
{
    /// <summary>Решение агента: true — «Да» / «на круг», false — «Нет» / «выход».</summary>
    public const string DecisionKey = "flowDecision";

    /// <summary>У «Условия»: решение уже применено движком (непройденная ветка отменена,
    /// остановка выполнена) — второй проход очереди его не повторяет.</summary>
    public const string AppliedKey = "flowApplied";

    /// <summary>У цикла: идёт тело цикла — выполняются потомки, сама задача на паузе
    /// «ждёт окончания цикла».</summary>
    public const string LoopBodyKey = "loopBody";

    /// <summary>У цикла: сколько кругов тела уже пройдено.</summary>
    public const string LoopPassKey = "loopPass";

    /// <summary>У цикла: цикл окончен (условие не выполнено либо превышен предел).</summary>
    public const string LoopDoneKey = "loopDone";

    /// <summary>Выполнение иерархии ОСТАНОВЛЕНО на этой задаче (T-301-S0): предел цикла с
    /// флажком «остановить всю иерархию», цикл или условие без решения агента, ветка условия
    /// «завершить выполнение». Пишет движок в момент остановки — диаграмме нужен ФАКТ для
    /// знака Stop, а восстановить его по остальным ключам нельзя: решение агента у цикла
    /// движок стирает после чтения.</summary>
    public const string StoppedKey = "flowStopped";

    /// <summary>Все ключи состояния — снимаются разом при новом запуске цикла/условия.</summary>
    public static readonly string[] Keys = [DecisionKey, AppliedKey, LoopBodyKey, LoopPassKey, LoopDoneKey, StoppedKey];

    /// <summary>Решение агента; null — агент решения не сообщил (или json испорчен).</summary>
    public static bool? Decision(string? launchJson) =>
        Element(launchJson, DecisionKey) is { } v
            ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    public static bool Applied(string? launchJson) => Flag(launchJson, AppliedKey);

    public static bool InLoopBody(string? launchJson) => Flag(launchJson, LoopBodyKey);

    public static bool LoopDone(string? launchJson) => Flag(launchJson, LoopDoneKey);

    public static bool Stopped(string? launchJson) => Flag(launchJson, StoppedKey);

    /// <summary>Пройдено кругов цикла; 0 — ни одного либо поле испорчено. Число проверяется
    /// ОТДЕЛЬНО: TryGetInt32 на строке бросает, а не отвечает false.</summary>
    public static int LoopPass(string? launchJson) =>
        Element(launchJson, LoopPassKey) is { ValueKind: JsonValueKind.Number } v
        && v.TryGetInt32(out var n) && n > 0
            ? n
            : 0;

    /// <summary>
    /// Предел кругов, по которому работает движок. У ЛИНЕЙНОЙ задачи он всегда 1 (указание
    /// T-297-S0): даже если в её описании по сути написаны условия цикла, цикла у неё нет —
    /// круги ведёт только тип «цикл». У цикла — свой <c>recheckLimit</c> задачи, а если его
    /// нет (задача заведена мимо формы) — предел проекта.
    /// </summary>
    public static int LoopLimit(Api.TaskFlowDto flow, int projectLimit)
    {
        if (!TaskFlow.IsLoop(TaskFlow.Normalize(flow.Type)))
        {
            return 1;
        }
        if (flow.RecheckLimit is int own && own > 0)
        {
            return own;
        }
        return projectLimit > 0 ? projectLimit : ProjectSettings.RecheckLimitDefault;
    }

    /// <summary>Записать решение агента в launch_json (не трогая остальных ключей).</summary>
    public static void WriteDecision(JsonObject root, bool decision) => root[DecisionKey] = decision;

    private static bool Flag(string? launchJson, string key) =>
        Element(launchJson, key) is { ValueKind: JsonValueKind.True };

    private static JsonElement? Element(string? launchJson, string key)
    {
        if (string.IsNullOrWhiteSpace(launchJson))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(key, out var v)
                ? v.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
