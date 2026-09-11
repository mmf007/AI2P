using AI2P.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI2P.Storage;
using AI2P.Storage.Services;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using ApiRole = Anthropic.Models.Beta.Messages.Role;

namespace AI2P.Connectors;

/// <summary>
/// AnthropicConnector (ТЗ п. 7.2, этапы 2.1 и 2.3): исполнитель-ИИ Claude через Messages API
/// официального SDK. Жизненный цикл задания — в AiConnectorBase; здесь — вызов провайдера,
/// цикл инструментов (файловые + ask_question, этап 2.3, todo21) и проба подключения
/// (Models API). Контекст диалога для вопросов агента сериализуется в wire-формате SDK
/// (классы моделей SDK сериализуемы System.Text.Json) — ожидание переживает перезапуск.
/// Для Claude Fable 5 поддерживается серверная подстраховка при отказе классификаторов
/// (params.fallbacks профайла).
/// </summary>
public sealed class AnthropicConnector : AiConnectorBase
{
    public const string Provider = "anthropic";

    /// <summary>Предохранитель от зацикливания на вызовах инструментов (ТЗ гл. 8 — лимиты).</summary>
    private const int MaxToolIterations = 20;

    public AnthropicConnector(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, SecretStore secrets,
        ChatService chat, ActionCatalogService actions, SecurityRuleService security,
        RefDataService refData, ExecutorPickService picker, ExperienceService experience,
        JobConsole console, LocalModelProcessService? localModels, TeamService teams,
        Func<string> language, string publicBaseUrl)
        : base(jobs, tasks, executors, projects, files, events, secrets, chat, actions, security,
            refData, picker, experience, console, localModels, teams, language, publicBaseUrl)
    {
    }

    public override string Kind => Provider;

    /// <summary>Вопросы агента (ask_question, ТЗ v1.17) поддерживаются с этапа 2.3 (todo21).</summary>
    protected override bool SupportsQuestions => true;

    /// <summary>Контекст диалога при висящем вопросе (ТЗ v1.17): wire-формат SDK + id ожидающего
    /// tool_use + готовые результаты остальных инструментов того же хода (Anthropic требует
    /// tool_result на КАЖДЫЙ tool_use в следующем сообщении пользователя).</summary>
    private sealed class AgentContextDto
    {
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("pendingToolUseId")] public string PendingToolUseId { get; set; } = "";
        [JsonPropertyName("messages")] public List<BetaMessageParam> Messages { get; set; } = [];
        [JsonPropertyName("partialResults")] public List<BetaContentBlockParam> PartialResults { get; set; } = [];
        /// <summary>Подтверждение правила безопасности (todo25): отложенный инструмент —
        /// выполняется при ответе «да»; null — обычный вопрос агента (ask_question).</summary>
        [JsonPropertyName("confirmTool")] public string? ConfirmTool { get; set; }
        [JsonPropertyName("confirmArgs")] public string? ConfirmArgs { get; set; }
    }

    protected override async Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
        string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
    {
        var client = new AnthropicClient { ApiKey = apiKey };
        // инструменты заданий (todo23) и ask_question (ТЗ v1.17) публикуются всегда,
        // файловые — при заданной папке проекта (todo17)
        var toolUnions = BuildTools(tools);
        var messages = resume is null
            ? [new BetaMessageParam { Role = ApiRole.User, Content = requestText }]
            : await RestoreContextAsync(resume, tools, ct);
        var useFallbacks = profile.Fallbacks.Count > 0;

        long inputTokens = 0, outputTokens = 0;
        var model = profile.Model;

        for (var iteration = 1; ; iteration++)
        {
            if (iteration > MaxToolIterations)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.anthropicConnector.1", MaxToolIterations));
            }

            // ВАЖНО: SDK сериализует явно присвоенный null («fallbacks»: null уходит в тело
            // запроса), а без beta-заголовка API отвечает «fallbacks: Extra inputs are not
            // permitted» — поэтому Betas/Fallbacks задаются только при непустом списке.
            var outputConfig = profile.Effort is null
                ? null
                : new BetaOutputConfig { Effort = ParseEffort(profile.Effort) };
            var parameters = useFallbacks
                ? new MessageCreateParams
                {
                    Model = profile.Model,
                    MaxTokens = profile.MaxTokens,
                    System = SystemPrompt(tools.Language, tools.HasFileTools, true) + tools.SplitNote + tools.ExperienceNote + tools.SecurityNote,
                    Messages = messages,
                    Tools = toolUnions,
                    OutputConfig = outputConfig,
                    Betas = ["server-side-fallback-2026-06-01"],
                    Fallbacks = profile.Fallbacks.Select(m => new BetaFallbackParam { Model = m }).ToList(),
                }
                : new MessageCreateParams
                {
                    Model = profile.Model,
                    MaxTokens = profile.MaxTokens,
                    System = SystemPrompt(tools.Language, tools.HasFileTools, true) + tools.SplitNote + tools.ExperienceNote + tools.SecurityNote,
                    Messages = messages,
                    Tools = toolUnions,
                    OutputConfig = outputConfig,
                };

            Logger.Information(
                "Anthropic Messages API (итерация {Iteration}): модель {Model}, effort {Effort}, " +
                "fallbacks [{Fallbacks}], сообщений {MessageCount}, инструментов {ToolCount}",
                iteration, profile.Model, profile.Effort ?? Loc.T("msg.anthropicConnector.2"),
                string.Join(", ", profile.Fallbacks), messages.Count, toolUnions.Count);
            // таймаут ожидания ответа на один запрос — из формы исполнителя (T-124);
            // до этого было зашитых 10 минут
            using var timeout = CallTimeoutCts(tools, ct);
            BetaMessage response;
            try
            {
                response = await client.Beta.Messages.Create(parameters, cancellationToken: timeout.Token);
            }
            catch (Anthropic.Exceptions.AnthropicRateLimitException ex)
            {
                // исчерпан лимит провайдера (ТЗ v1.37): «занят до» фиксируется в AiConnectorBase;
                // SDK не отдаёт Retry-After — берётся значение по умолчанию (час)
                throw new ProviderLimitException(Loc.T("msg.anthropicConnector.3", ex.Message),
                    retryAt: null);
            }

            inputTokens += response.Usage.InputTokens;
            outputTokens += response.Usage.OutputTokens;
            model = response.Model.ToString() ?? profile.Model;
            var stopReason = response.StopReason?.ToString() ?? "";
            Logger.Information("Anthropic Messages API → модель {Model}, stopReason {StopReason}",
                model, stopReason);
            if (stopReason.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            {
                var explanation = response.StopDetails?.Explanation
                                  ?? Loc.T("msg.anthropicConnector.4");
                throw new InvalidOperationException(Loc.T("msg.anthropicConnector.5", explanation));
            }

            // запросы инструментов этого хода
            var toolUses = new List<BetaToolUseBlock>();
            foreach (var block in response.Content)
            {
                if (block.TryPickToolUse(out var use))
                {
                    toolUses.Add(use!);
                }
            }
            if (toolUses.Count == 0)
            {
                var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t!.Text : ""));
                return AiCallOutcome.Of(new AiCallResult(text, inputTokens, outputTokens, model, stopReason));
            }

            // ассистентский ход добавляется в диалог как есть (wire JSON блоков ответа)
            messages.Add(new BetaMessageParam
            {
                Role = ApiRole.Assistant,
                Content = response.Content.Select(b => new BetaContentBlockParam(b.Json)).ToList(),
            });

            var results = new List<BetaContentBlockParam>();
            (string Id, string Text, List<string> Options)? question = null;
            // отложенный вызов, требующий подтверждения человеком (ТЗ гл. 12, todo25)
            (string Id, string Name, string ArgsJson, string Question)? confirmation = null;
            foreach (var use in toolUses)
            {
                // вопрос человеку (ТЗ v1.17): диалог прерывается до ответа; tool_result
                // этого вызова подставится при продолжении (ResumeJobAsync)
                if (use.Name == AskQuestionSpec.Name)
                {
                    if (question is null && confirmation is null
                        && TryParseQuestion(use.Input, out var qText, out var qOptions))
                    {
                        question = (use.ID, qText, qOptions);
                        continue;
                    }
                    results.Add(ToolResult(use.ID, Loc.In(tools.Language,
                        question is null && confirmation is null
                            ? "prompt.agent.7"
                            : "prompt.agent.8")));
                    continue;
                }

                // проверка правил безопасности ДО выполнения (ТЗ п. 12.3, todo25)
                var argsElement = ToElement(use.Input);
                var check = tools.Authorize(use.Name, argsElement);
                if (check.Decision == SecurityDecision.Deny)
                {
                    results.Add(ToolResult(use.ID, check.Message));
                    continue;
                }
                if (check.Decision == SecurityDecision.Confirm)
                {
                    if (question is null && confirmation is null)
                    {
                        confirmation = (use.ID, use.Name, argsElement.GetRawText(), check.Message);
                        continue;
                    }
                    results.Add(ToolResult(use.ID, Loc.In(tools.Language, "prompt.agent.9")));
                    continue;
                }

                results.Add(ToolResult(use.ID, await tools.ExecuteAsync(use.Name, argsElement, ct)));
            }

            if (question is { } q)
            {
                var context = JsonSerializer.Serialize(new AgentContextDto
                {
                    Provider = Provider,
                    PendingToolUseId = q.Id,
                    Messages = messages,
                    PartialResults = results,
                }, new JsonSerializerOptions { WriteIndented = true });
                return AiCallOutcome.Of(new AgentQuestion(q.Text, q.Options, context,
                    inputTokens, outputTokens, model));
            }
            if (confirmation is { } c)
            {
                // подтверждение человеком (ТЗ гл. 12, todo25): тот же механизм вопросов;
                // при ответе «да» отложенный инструмент выполнится при продолжении
                var context = JsonSerializer.Serialize(new AgentContextDto
                {
                    Provider = Provider,
                    PendingToolUseId = c.Id,
                    Messages = messages,
                    PartialResults = results,
                    ConfirmTool = c.Name,
                    ConfirmArgs = c.ArgsJson,
                }, new JsonSerializerOptions { WriteIndented = true });
                return AiCallOutcome.Of(new AgentQuestion(c.Question,
                    [Loc.T("msg.agentConfirm.1"), Loc.T("msg.agentConfirm.2")], context,
                    inputTokens, outputTokens, model, IsConfirmation: true));
            }

            messages.Add(new BetaMessageParam { Role = ApiRole.User, Content = results });
            // перебивка чатом (T-161): человек написал в чат задачи, пока агент работал.
            // Сообщение идёт ОТДЕЛЬНЫМ ходом человека сразу за результатами инструментов —
            // на ближайшем витке цикла, а не после закрытия задания
            if (ChatDelivery(tools) is { Length: > 0 } chatDelivery)
            {
                messages.Add(new BetaMessageParam { Role = ApiRole.User, Content = chatDelivery });
            }
        }
    }

    /// <summary>Восстановить диалог из контекста вопроса (ТЗ v1.17): сохранённые сообщения +
    /// tool_result'ы остальных инструментов того же хода + ответ человека на вопрос.
    /// Для подтверждения правила безопасности (todo25): «да» — отложенный инструмент
    /// выполняется сейчас, его результат подставляется как tool result; иначе — отказ.</summary>
    private static async Task<List<BetaMessageParam>> RestoreContextAsync(AgentResume resume,
        AgentToolset tools, CancellationToken ct)
    {
        AgentContextDto context;
        try
        {
            context = JsonSerializer.Deserialize<AgentContextDto>(resume.ContextJson)
                      ?? throw new InvalidOperationException(Loc.T("msg.anthropicConnector.6"));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Loc.T("msg.anthropicConnector.7", ex.Message));
        }
        if (context.Messages.Count == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.anthropicConnector.8"));
        }
        string pendingResult;
        if (context.ConfirmTool is { Length: > 0 } confirmTool)
        {
            if (IsApproval(resume.AnswerText))
            {
                // человек разрешил — выполняем отложенный инструмент (без повторной проверки)
                using var argsDoc = JsonDocument.Parse(context.ConfirmArgs ?? "{}");
                var result = await tools.ExecuteAsync(confirmTool, argsDoc.RootElement, ct);
                pendingResult = Loc.In(tools.Language, "prompt.agent.10", result);
            }
            else
            {
                pendingResult = Loc.In(tools.Language, "prompt.agent.11", resume.AnswerText);
            }
        }
        else
        {
            pendingResult = Loc.In(tools.Language, "prompt.agent.12", resume.AnswerText);
        }
        var results = new List<BetaContentBlockParam>(context.PartialResults)
        {
            ToolResult(context.PendingToolUseId, pendingResult),
        };
        var messages = context.Messages;
        messages.Add(new BetaMessageParam { Role = ApiRole.User, Content = results });
        return messages;
    }

    private static BetaContentBlockParam ToolResult(string toolUseId, string content) =>
        new BetaToolResultBlockParam(toolUseId) { Content = content };

    /// <summary>Инструменты в формате Anthropic (BetaTool из JSON Schema спецификаций):
    /// файловые (если есть папка проекта) + задания (todo23) + ask_question (всегда, ТЗ v1.17).</summary>
    private static List<BetaToolUnion> BuildTools(AgentToolset tools)
    {
        var result = new List<BetaToolUnion>();
        foreach (var spec in tools.Specs.Append(tools.Resolve(AskQuestionSpec)))
        {
            using var doc = JsonDocument.Parse(spec.ParametersJson);
            var properties = new Dictionary<string, JsonElement>();
            if (doc.RootElement.TryGetProperty("properties", out var props))
            {
                foreach (var property in props.EnumerateObject())
                {
                    properties[property.Name] = property.Value.Clone();
                }
            }
            var required = doc.RootElement.TryGetProperty("required", out var req)
                ? req.EnumerateArray().Select(e => e.GetString()!).ToList()
                : [];
            result.Add(new BetaTool
            {
                Name = spec.Name,
                Description = spec.Description,
                InputSchema = new InputSchema { Properties = properties, Required = required },
            });
        }
        return result;
    }

    /// <summary>Аргументы tool_use (словарь SDK) → JsonElement для FileToolset.ExecuteAsync.</summary>
    private static JsonElement ToElement(IReadOnlyDictionary<string, JsonElement>? input) =>
        JsonSerializer.SerializeToElement(input ?? new Dictionary<string, JsonElement>());

    /// <summary>Разобрать аргументы ask_question: question обязателен, options — массив строк.</summary>
    private static bool TryParseQuestion(IReadOnlyDictionary<string, JsonElement>? input,
        out string text, out List<string> options)
    {
        text = "";
        options = [];
        if (input is null
            || !input.TryGetValue("question", out var q)
            || q.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(q.GetString()))
        {
            return false;
        }
        text = q.GetString()!.Trim();
        if (input.TryGetValue("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            options = opts.EnumerateArray()
                .Where(o => o.ValueKind == JsonValueKind.String)
                .Select(o => o.GetString()!.Trim())
                .Where(o => o.Length > 0)
                .ToList();
        }
        return true;
    }

    /// <summary>Проба: Models API — ключ действует, модель существует.</summary>
    protected override async Task<string?> ProbeAsync(ModelProfile profile, string apiKey, CancellationToken ct)
    {
        try
        {
            Logger.Information("Проба: Anthropic Models API retrieve {Model}", profile.Model);
            var client = new AnthropicClient { ApiKey = apiKey };
            await client.Models.Retrieve(profile.Model, cancellationToken: ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            return Loc.T("msg.anthropicConnector.9");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проба: Anthropic Models API retrieve {Model} — исключение", profile.Model);
            return ex.Message;
        }
    }

    /// <summary>Уровень усилий из профайла (params.effort): low / medium / high / xhigh / max.</summary>
    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new InvalidOperationException(Loc.T("msg.anthropicConnector.10", effort)),
    };
}
