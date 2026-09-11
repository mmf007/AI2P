using System.Text.Json;
using System.Text.Json.Serialization;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Server.Api;

/// <summary>
/// ДЕЙСТВИЯ AI2P ДЛЯ CLI-АГЕНТА ВЫЗОВОМ, А НЕ МАРКЕРОМ (T-34-S0) — раздел
/// <c>/api/agent</c>, к которому ходит клиент командной строки <c>ai2p</c>.
///
/// <para>Сюда ходит АГЕНТ, а не человек, поэтому раздел живёт отдельной группой и не
/// проходит проверку прав пользователя (<see cref="ApiPermissions"/>) — ровно как
/// протокол сервер-сервер (<see cref="ClusterEndpoints"/>). Личность подтверждается
/// ТОКЕНОМ ЗАДАНИЯ (<see cref="AgentSessionRegistry"/>): он живёт только в памяти
/// процесса, только пока задание идёт, и принимается только с петлевого адреса
/// (<see cref="Ai2pAuth.IsLocalRequest"/>).</para>
///
/// <para>ЛОГИКА ДЕЙСТВИЙ ЗДЕСЬ НЕ ДУБЛИРУЕТСЯ. Раздел достаёт из сессии ТОТ ЖЕ
/// <see cref="AgentToolset"/>, которым работает само задание, и зовёт его
/// <c>Authorize</c> + <c>ExecuteAsync</c>. Отсюда главное следствие, ради которого всё
/// и делается: каждый вызов клиента проходит правила безопасности задачи
/// (<c>ActionCatalogService.CodeByTool</c> → <c>SecurityEvaluator</c>), попадает в журнал
/// работ (agent.tool_calls) и в консоль задания — до T-34-S0 ядро у CLI-агента правила
/// для его собственных средств не проверяло вовсе.</para>
/// </summary>
public static class AgentEndpoints
{
    /// <summary>Заголовок с токеном задания.</summary>
    public const string TokenHeader = "X-AI2P-Job";

    public static void MapAi2pAgent(this WebApplication app)
    {
        var agent = app.MapGroup("/api/agent").AllowAnonymous();

        // список доступных этому заданию действий — им живёт «ai2p help»
        agent.MapGet("/actions", (HttpContext ctx) =>
        {
            if (Caller(ctx) is not { } session)
            {
                return Denied(ctx);
            }
            return Results.Ok(new AgentActionsDto
            {
                Task = session.TaskCode,
                Job = session.JobCode,
                Tools = session.Tools.Specs.Select(spec => new AgentActionDto
                {
                    Tool = spec.Name,
                    Allowed = session.Tools.ActionAllowed(spec.Name),
                    Description = spec.Description,
                }).ToList(),
            });
        });

        // сам вызов действия
        agent.MapPost("/call", async (HttpContext ctx, AgentCallDto dto, CancellationToken ct) =>
        {
            if (Caller(ctx) is not { } session)
            {
                return Denied(ctx);
            }
            var tool = (dto.Tool ?? "").Trim();
            if (tool.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, Loc.T("msg.agentEndpoints.3"));
            }
            var tools = session.Tools;
            // инструмент должен быть ОПУБЛИКОВАН этому заданию: набор зависит от задачи
            // (у задачи без проекта нет шаблонов, у уже разбитой — создания подзадач)
            if (!tools.Specs.Any(s => string.Equals(s.Name, tool, StringComparison.Ordinal)))
            {
                return Problem(StatusCodes.Status400BadRequest,
                    Loc.T("msg.agentEndpoints.4", tool,
                        string.Join(", ", tools.Specs.Select(s => s.Name))));
            }
            var args = dto.Args ?? AgentSession.NoArgs;
            if (args.ValueKind != JsonValueKind.Object)
            {
                return Problem(StatusCodes.Status400BadRequest, Loc.T("msg.agentEndpoints.5"));
            }
            // вызовы одного задания идут по очереди: журнал вызовов набора инструментов —
            // обычный список, а агент может позвать клиента сразу из нескольких подпроцессов
            await session.EnterAsync(ct);
            try
            {
                var check = tools.Authorize(tool, args);
                if (check.Decision != SecurityDecision.Allow)
                {
                    // отказ правила безопасности — не ошибка сервера, а осмысленный ответ:
                    // текст отказа читает сам агент и решает, что делать дальше
                    return Problem(StatusCodes.Status403Forbidden, check.Message);
                }
                var result = await tools.ExecuteAsync(tool, args, ct);
                return Results.Ok(new AgentResultDto { Ok = true, Tool = tool, Result = result });
            }
            catch (Exception ex)
            {
                return Problem(StatusCodes.Status400BadRequest,
                    Loc.T("msg.agentEndpoints.6", tool, ex.Message));
            }
            finally
            {
                session.Leave();
            }
        });
    }

    /// <summary>
    /// Кто звонит: токен задания из заголовка И петлевой адрес. Оба условия обязательны —
    /// токен в памяти процесса надёжен ровно настолько, насколько недоступен снаружи.
    /// null — токена нет, он не тот либо задание уже закончилось.
    /// </summary>
    private static AgentSession? Caller(HttpContext ctx)
    {
        if (!Ai2pAuth.IsLocalRequest(ctx))
        {
            return null;
        }
        var token = ctx.Request.Headers[TokenHeader].ToString();
        return AgentSessionRegistry.Resolve(token);
    }

    private static IResult Denied(HttpContext ctx) =>
        Problem(StatusCodes.Status401Unauthorized,
            Ai2pAuth.IsLocalRequest(ctx)
                ? Loc.T("msg.agentEndpoints.1")
                : Loc.T("msg.agentEndpoints.2"));

    private static IResult Problem(int status, string message) =>
        Results.Json(new AgentResultDto { Ok = false, Error = message }, statusCode: status);
}

/// <summary>Вызов действия клиентом <c>ai2p</c> (T-34-S0).</summary>
public sealed class AgentCallDto
{
    [JsonPropertyName("tool")] public string? Tool { get; set; }

    /// <summary>Аргументы вызова — тот же JSON-объект, что у инструмента и у маркера.</summary>
    [JsonPropertyName("args")] public JsonElement? Args { get; set; }
}

/// <summary>Ответ раздела <c>/api/agent</c>.</summary>
public sealed class AgentResultDto
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";
    [JsonPropertyName("result")] public string Result { get; set; } = "";
    [JsonPropertyName("error")] public string Error { get; set; } = "";
}

/// <summary>Действия, доступные этому заданию (<c>ai2p help</c>).</summary>
public sealed class AgentActionsDto
{
    [JsonPropertyName("task")] public string Task { get; set; } = "";
    [JsonPropertyName("job")] public string Job { get; set; } = "";
    [JsonPropertyName("tools")] public List<AgentActionDto> Tools { get; set; } = [];
}

public sealed class AgentActionDto
{
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";

    /// <summary>Разрешают ли правила безопасности задачи это действие (ТЗ гл. 12).</summary>
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }

    [JsonPropertyName("description")] public string Description { get; set; } = "";
}
