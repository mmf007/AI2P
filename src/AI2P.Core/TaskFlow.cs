using System.Text.Json;
using System.Text.Json.Nodes;
using AI2P.Core.Api;

namespace AI2P.Core;

/// <summary>
/// ТИП ЗАДАЧИ (T-298-S0, ветвление и циклы T-297-S0): линейная, условие, «цикл до»,
/// «цикл после» — и параметры каждого типа. Всё хранится ключами <c>launch_json</c>
/// задачи (и узла шаблона), рядом с <c>mode</c>, <c>startAfter</c>, <c>noAutoStart</c>.
///
/// <para>Почему НЕ <c>tasks.kind</c>: <c>EnumMap.TaskKindFromDb</c> разбирает его правилом
/// «<c>process</c> → процесс, иначе задача», и сервер прежней версии, получив репликой
/// <c>kind="if"</c>, принял бы условие за обычную задачу и выполнил ВСЕ ветки. Незнакомый
/// ключ launch_json старый сервер просто не читает — задача для него линейная, как и была.</para>
///
/// <para>Отсутствие ключа <see cref="TypeKey"/> — «линейная»: так читаются все задачи и
/// шаблоны, заведённые до T-298-S0, и отдельного шага обновления не нужно.</para>
///
/// <para>Запись нормализующая (<see cref="Write"/>): ключи чужого типа снимаются, поэтому
/// смена «условие → линейная» не оставляет в задаче висячих ссылок на ветви.</para>
/// </summary>
public static class TaskFlow
{
    /// <summary>Линейная задача — умолчание, прежнее поведение.</summary>
    public const string Linear = "linear";
    /// <summary>Условие: по итогу задачи выполняется ветка «Да» либо «Нет».</summary>
    public const string If = "if";
    /// <summary>«Цикл до» — проверка условия ПЕРЕД телом цикла.</summary>
    public const string Loop = "loop";
    /// <summary>«Цикл после» — проверка условия ПОСЛЕ тела цикла.</summary>
    public const string DoLoop = "do-loop";

    /// <summary>Все типы по порядку показа в форме.</summary>
    public static readonly string[] Types = [Linear, If, Loop, DoLoop];

    // --- ключи launch_json ---
    public const string TypeKey = "flowType";
    public const string IfTrueTaskKey = "ifTrueTaskId";
    public const string IfFalseTaskKey = "ifFalseTaskId";
    public const string IfTrueCreateKey = "ifTrueCreateTasks";
    public const string IfFalseCreateKey = "ifFalseCreateTasks";
    public const string IfTrueStopKey = "ifTrueStopHierarchy";
    public const string IfFalseStopKey = "ifFalseStopHierarchy";
    public const string LoopLimitKey = "recheckLimit";
    public const string LoopStopKey = "loopStopHierarchy";

    private static readonly string[] IfKeys =
        [IfTrueTaskKey, IfFalseTaskKey, IfTrueCreateKey, IfFalseCreateKey, IfTrueStopKey, IfFalseStopKey];
    private static readonly string[] LoopKeys = [LoopLimitKey, LoopStopKey];

    /// <summary>Тип — один из известных; незнакомое значение (задача от сервера более
    /// новой версии) читается как «линейная».</summary>
    public static string Normalize(string? type) =>
        type is not null && Types.Contains(type) ? type : Linear;

    public static bool IsLoop(string type) => type is Loop or DoLoop;

    /// <summary>Тип и параметры задачи из launch_json. Испорченный json — линейная.</summary>
    public static TaskFlowDto Read(string? launchJson)
    {
        var dto = new TaskFlowDto();
        if (string.IsNullOrWhiteSpace(launchJson))
        {
            return dto;
        }
        try
        {
            using var doc = JsonDocument.Parse(launchJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return dto;
            }
            dto.Type = Normalize(Str(root, TypeKey));
            if (dto.Type == If)
            {
                dto.IfTrueTaskId = Str(root, IfTrueTaskKey);
                dto.IfFalseTaskId = Str(root, IfFalseTaskKey);
                dto.IfTrueCreateTasks = Bool(root, IfTrueCreateKey);
                dto.IfFalseCreateTasks = Bool(root, IfFalseCreateKey);
                dto.IfTrueStopHierarchy = Bool(root, IfTrueStopKey);
                dto.IfFalseStopHierarchy = Bool(root, IfFalseStopKey);
            }
            else if (IsLoop(dto.Type))
            {
                // число проверяется ОТДЕЛЬНО: TryGetInt32 на строке бросает, а не отвечает false
                dto.RecheckLimit = root.TryGetProperty(LoopLimitKey, out var limit)
                                   && limit.ValueKind == JsonValueKind.Number
                                   && limit.TryGetInt32(out var value) && value > 0
                    ? value
                    : null;
                dto.LoopStopHierarchy = Bool(root, LoopStopKey);
            }
        }
        catch (JsonException)
        {
            return new TaskFlowDto();
        }
        return dto;
    }

    /// <summary>
    /// Записать тип и параметры в launch_json, не трогая остальных ключей (режим запуска,
    /// флаги очереди). Ключи чужого типа снимаются; у линейной задачи не пишется ничего —
    /// launch_json старой задачи после сохранения формы остаётся прежним.
    /// Флажок «завершить выполнение иерархии» у ветки без задачи имеет смысл только при
    /// выключенном «создавать задачи» — при включённом он снимается.
    /// </summary>
    public static string Write(string? launchJson, TaskFlowDto flow)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(launchJson) ? "{}" : launchJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }
        foreach (var key in IfKeys.Concat(LoopKeys))
        {
            root.Remove(key);
        }
        var type = Normalize(flow.Type);
        if (type == Linear)
        {
            root.Remove(TypeKey);
            return root.ToJsonString();
        }
        root[TypeKey] = type;
        if (type == If)
        {
            WriteBranch(root, flow.IfTrueTaskId, flow.IfTrueCreateTasks, flow.IfTrueStopHierarchy,
                IfTrueTaskKey, IfTrueCreateKey, IfTrueStopKey);
            WriteBranch(root, flow.IfFalseTaskId, flow.IfFalseCreateTasks, flow.IfFalseStopHierarchy,
                IfFalseTaskKey, IfFalseCreateKey, IfFalseStopKey);
        }
        else
        {
            if (flow.RecheckLimit is int limit && limit > 0)
            {
                root[LoopLimitKey] = limit;
            }
            if (flow.LoopStopHierarchy)
            {
                root[LoopStopKey] = true;
            }
        }
        return root.ToJsonString();
    }

    private static void WriteBranch(JsonObject root, string? taskId, bool create, bool stop,
        string taskKey, string createKey, string stopKey)
    {
        if (taskId is { Length: > 0 })
        {
            // задача ветви указана — флажки ветви без задачи не нужны
            root[taskKey] = taskId;
            return;
        }
        if (create)
        {
            root[createKey] = true;
        }
        else if (stop)
        {
            root[stopKey] = true;
        }
    }

    private static string? Str(JsonElement root, string key) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString() is { Length: > 0 } s ? s : null;

    private static bool Bool(JsonElement root, string key) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
