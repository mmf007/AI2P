using AI2P.Core;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// TextConnector (ТЗ п. 7.2, этап 2.2): исполнители-ИИ через OpenAI-совместимый протокол
/// chat/completions — DeepSeek, Qwen, YandexGPT, OpenAI, локальные Ollama/llama.cpp и т.д.
/// Реализован тонким HTTP-клиентом (протокол простой и стабильный, совместимые серверы
/// по-разному трактуют тонкости SDK). Базовый URL — из профайла модели (п. 2.9):
/// запросы идут на {baseUrl}/chat/completions, проба подключения — на {baseUrl}/models.
/// Жизненный цикл задания — в AiConnectorBase.
/// </summary>
public sealed class OpenAiCompatibleConnector : AiConnectorBase
{
    public const string Provider = "openai-compatible";

    /// <summary>Общий HttpClient: таймаут per-request задаётся токеном отмены (задание —
    /// «Таймаут ответа» исполнителя, T-124; проба — 30 с).</summary>
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Предохранитель от зацикливания на вызовах инструментов (ТЗ гл. 8 — лимиты).</summary>
    private const int MaxToolIterations = 20;

    public OpenAiCompatibleConnector(JobService jobs, TaskService tasks, ExecutorService executors,
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

    /// <summary>Вопросы агента (ask_question, ТЗ v1.17) поддерживаются.</summary>
    protected override bool SupportsQuestions => true;

    protected override async Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
        string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
    {
        var url = BaseUrl(profile) + "/chat/completions";
        // инструменты заданий (todo23) и ask_question (ТЗ v1.17) публикуются всегда,
        // файловые — при заданной папке проекта (todo17)
        var toolsJson = ToolsJson(tools);
        // диалог: system + user, растёт tool call / tool result до финального ответа (ТЗ п. 2.4);
        // при продолжении после вопроса (ТЗ v1.17) диалог восстанавливается из контекста
        List<JsonNode> messages;
        if (resume is null)
        {
            messages =
            [
                new JsonObject
                {
                    ["role"] = "system",
                    // + create_task (ТЗ v1.26) + правила безопасности задачи (todo26)
                    ["content"] = SystemPrompt(tools.Language, tools.HasFileTools, true)
                                  + tools.SplitNote + tools.ExperienceNote + tools.SecurityNote,
                },
                new JsonObject { ["role"] = "user", ["content"] = requestText },
            ];
        }
        else
        {
            messages = await RestoreContextAsync(resume, tools, ct);
        }

        long inputTokens = 0, outputTokens = 0;
        var model = profile.Model;
        double? limitPercent = null;

        for (var iteration = 1; ; iteration++)
        {
            if (iteration > MaxToolIterations)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.anthropicConnector.1", MaxToolIterations));
            }

            var body = new JsonObject
            {
                ["model"] = profile.Model,
                ["max_tokens"] = profile.MaxTokens,
                ["stream"] = false,
                ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
                ["tools"] = toolsJson.DeepClone(),
            };
            var bodyText = body.ToJsonString();
            Logger.Information("POST {Url} (итерация {Iteration}): модель {Model}, тело {BodyLength} байт",
                url, iteration, profile.Model, bodyText.Length);

            // таймаут ожидания ответа на один запрос — из формы исполнителя (T-124);
            // до этого было зашитых 10 минут
            using var timeout = CallTimeoutCts(tools, ct);
            using var response = await SendAsync(HttpMethod.Post, url, apiKey, bodyText, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            Logger.Information("POST {Url} → HTTP {Status}, ответ {ResponseLength} байт",
                url, (int)response.StatusCode, text.Length);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("POST {Url} → HTTP {Status}, тело ошибки: {Body}",
                    url, (int)response.StatusCode, Truncate(text, 2000));
                if ((int)response.StatusCode == 429)
                {
                    // исчерпан лимит провайдера (ТЗ v1.37): «занят до» — из Retry-After /
                    // заголовков сброса; фиксируется у исполнителя в AiConnectorBase
                    throw new ProviderLimitException(
                        Loc.T("msg.anthropicConnector.3", ErrorMessageOf(text)),
                        RetryAtOf(response), UsedLimitPercentOf(response));
                }
                throw new InvalidOperationException(
                    Loc.T("msg.openAiCompatibleConnector.1", (int)response.StatusCode, ErrorMessageOf(text)));
            }
            limitPercent = UsedLimitPercentOf(response) ?? limitPercent;

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt))
                {
                    inputTokens += pt.GetInt64();
                }
                if (usage.TryGetProperty("completion_tokens", out var cot))
                {
                    outputTokens += cot.GetInt64();
                }
            }
            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
            {
                model = m.GetString()!;
            }

            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() ?? "" : "";

            // модель просит вызвать инструменты — выполняем и продолжаем диалог
            if (message.TryGetProperty("tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array
                && toolCalls.GetArrayLength() > 0)
            {
                messages.Add(JsonNode.Parse(message.GetRawText())!); // ассистентский ход с tool_calls — как есть
                (string Id, string Text, List<string> Options)? question = null;
                // отложенный вызов, требующий подтверждения человеком (ТЗ гл. 12, todo25)
                (string Id, string Name, string ArgsRaw, string Question)? confirmation = null;
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var fn = call.GetProperty("function");
                    var name = fn.GetProperty("name").GetString() ?? "";
                    var argsRaw = fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";

                    // вопрос человеку (ТЗ v1.17): диалог прерывается до ответа; результат
                    // этого tool call'а подставится при продолжении (ResumeJobAsync)
                    if (name == AskQuestionSpec.Name)
                    {
                        if (question is null && confirmation is null
                            && TryParseQuestion(argsRaw, out var qText, out var qOptions))
                        {
                            question = (id, qText, qOptions);
                            continue;
                        }
                        messages.Add(ToolResult(id, Loc.In(tools.Language,
                            question is null && confirmation is null
                                ? "prompt.agent.7"
                                : "prompt.agent.8")));
                        continue;
                    }

                    // проверка правил безопасности ДО выполнения (ТЗ п. 12.3, todo25)
                    var check = tools.Authorize(name, ParseArgs(argsRaw));
                    if (check.Decision == SecurityDecision.Deny)
                    {
                        messages.Add(ToolResult(id, check.Message));
                        continue;
                    }
                    if (check.Decision == SecurityDecision.Confirm)
                    {
                        if (question is null && confirmation is null)
                        {
                            confirmation = (id, name, argsRaw, check.Message);
                            continue;
                        }
                        messages.Add(ToolResult(id, Loc.In(tools.Language, "prompt.agent.9")));
                        continue;
                    }

                    messages.Add(ToolResult(id, await ExecuteToolAsync(tools, name, argsRaw, ct)));
                }
                if (question is { } q)
                {
                    // контекст: полный диалог + id ожидающего tool call'а — файлом (ТЗ v1.17)
                    var context = new JsonObject
                    {
                        ["provider"] = Provider,
                        ["pendingToolCallId"] = q.Id,
                        ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
                    };
                    return AiCallOutcome.Of(new AgentQuestion(q.Text, q.Options,
                        context.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                        inputTokens, outputTokens, model));
                }
                if (confirmation is { } pending)
                {
                    // подтверждение человеком (ТЗ гл. 12, todo25): тот же механизм вопросов;
                    // при ответе «да» отложенный инструмент выполнится при продолжении
                    var context = new JsonObject
                    {
                        ["provider"] = Provider,
                        ["pendingToolCallId"] = pending.Id,
                        ["confirm"] = new JsonObject { ["tool"] = pending.Name, ["args"] = pending.ArgsRaw },
                        ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
                    };
                    return AiCallOutcome.Of(new AgentQuestion(pending.Question,
                        [Loc.T("msg.agentConfirm.1"), Loc.T("msg.agentConfirm.2")],
                        context.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                        inputTokens, outputTokens, model, IsConfirmation: true));
                }
                // перебивка чатом (T-161): человек написал в чат задачи, пока агент работал —
                // сообщение уходит агенту отдельным ходом человека сразу после результатов
                // инструментов, то есть на ближайшем витке, а не после закрытия задания
                if (ChatDelivery(tools) is { Length: > 0 } chatDelivery)
                {
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = chatDelivery });
                }
                continue;
            }

            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()!
                : "";
            return AiCallOutcome.Of(new AiCallResult(content, inputTokens, outputTokens, model, finishReason,
                limitPercent));
        }
    }

    /// <summary>Восстановить диалог из контекста вопроса и подставить ответ человека (ТЗ v1.17).
    /// Для подтверждения правила безопасности (todo25): «да» — отложенный инструмент
    /// выполняется сейчас, его результат подставляется как tool result; иначе — отказ.</summary>
    private static async Task<List<JsonNode>> RestoreContextAsync(AgentResume resume,
        AgentToolset tools, CancellationToken ct)
    {
        JsonObject context;
        try
        {
            context = JsonNode.Parse(resume.ContextJson) as JsonObject
                      ?? throw new InvalidOperationException(Loc.T("msg.openAiCompatibleConnector.2"));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Loc.T("msg.anthropicConnector.7", ex.Message));
        }
        if (context["messages"] is not JsonArray saved)
        {
            throw new InvalidOperationException(Loc.T("msg.anthropicConnector.8"));
        }
        var pendingId = context["pendingToolCallId"]?.GetValue<string>() ?? "";
        var messages = saved.Select(m => m!.DeepClone()).ToList();

        if (context["confirm"] is JsonObject confirm)
        {
            var tool = confirm["tool"]?.GetValue<string>() ?? "";
            var args = confirm["args"]?.GetValue<string>() ?? "{}";
            if (IsApproval(resume.AnswerText))
            {
                // человек разрешил — выполняем отложенный инструмент (без повторной проверки)
                var result = await ExecuteToolAsync(tools, tool, args, ct);
                messages.Add(ToolResult(pendingId,
                    Loc.In(tools.Language, "prompt.agent.10", result)));
            }
            else
            {
                messages.Add(ToolResult(pendingId,
                    Loc.In(tools.Language, "prompt.agent.11", resume.AnswerText)));
            }
            return messages;
        }

        messages.Add(ToolResult(pendingId, Loc.In(tools.Language, "prompt.agent.12", resume.AnswerText)));
        return messages;
    }

    /// <summary>Аргументы tool call'а как JsonElement для проверки правил (todo25).</summary>
    private static JsonElement ParseArgs(string argsRaw)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsRaw) ? "{}" : argsRaw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }

    private static JsonNode ToolResult(string toolCallId, string content) => new JsonObject
    {
        ["role"] = "tool",
        ["tool_call_id"] = toolCallId,
        ["content"] = content,
    };

    /// <summary>Разобрать аргументы ask_question: question обязателен, options — массив строк.</summary>
    private static bool TryParseQuestion(string argsRaw, out string text, out List<string> options)
    {
        text = "";
        options = [];
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsRaw) ? "{}" : argsRaw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("question", out var q)
                || q.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(q.GetString()))
            {
                return false;
            }
            text = q.GetString()!.Trim();
            if (doc.RootElement.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                options = opts.EnumerateArray()
                    .Where(o => o.ValueKind == JsonValueKind.String)
                    .Select(o => o.GetString()!.Trim())
                    .Where(o => o.Length > 0)
                    .ToList();
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Разобрать аргументы (строка JSON) и выполнить инструмент; ошибки — текстом агенту.</summary>
    private static async Task<string> ExecuteToolAsync(AgentToolset tools, string name, string argsRaw,
        CancellationToken ct)
    {
        JsonDocument argsDoc;
        try
        {
            argsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsRaw) ? "{}" : argsRaw);
        }
        catch (JsonException)
        {
            return Loc.In(tools.Language, "prompt.agent.13", name, argsRaw);
        }
        using (argsDoc)
        {
            return await tools.ExecuteAsync(name, argsDoc.RootElement, ct);
        }
    }

    /// <summary>Инструменты в формате OpenAI (tools: [{type:function, function:{...}}]):
    /// файловые (если есть папка проекта) + задания (todo23) + ask_question (всегда, ТЗ v1.17).</summary>
    private static JsonArray ToolsJson(AgentToolset tools)
    {
        var arr = new JsonArray();
        foreach (var spec in tools.Specs.Append(tools.Resolve(AskQuestionSpec)))
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = spec.Name,
                    ["description"] = spec.Description,
                    ["parameters"] = JsonNode.Parse(spec.ParametersJson),
                },
            });
        }
        return arr;
    }

    /// <summary>
    /// Проба: GET {baseUrl}/models — ключ действует; если модели профайла нет в списке,
    /// в тексте ошибки перечисляются доступные id (помощь в настройке профайла).
    /// </summary>
    protected override async Task<string?> ProbeAsync(ModelProfile profile, string apiKey, CancellationToken ct)
    {
        var url = BaseUrl(profile) + "/models";
        try
        {
            Logger.Information("Проба: GET {Url} (модель профайла {Model})", url, profile.Model);
            using var response = await SendAsync(HttpMethod.Get, url, apiKey, body: null, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            Logger.Information("Проба: GET {Url} → HTTP {Status}, тело: {Body}",
                url, (int)response.StatusCode, Truncate(text, 2000));
            if (!response.IsSuccessStatusCode)
            {
                return Loc.T("msg.openAiCompatibleConnector.1", (int)response.StatusCode, ErrorMessageOf(text));
            }

            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                Logger.Information("Проба: {Url} не отдаёт список моделей (нет поля data) — ключ принят, успех", url);
                return null; // ключ принят, а список моделей сервер не отдаёт — считаем успехом
            }
            var ids = data.EnumerateArray()
                .Select(e => e.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();
            Logger.Information("Проба: провайдер отдал {Count} моделей: {Ids}", ids.Count, string.Join(", ", ids));
            if (ids.Count > 0 && !ids.Contains(profile.Model, StringComparer.OrdinalIgnoreCase))
            {
                return Loc.T("msg.openAiCompatibleConnector.3", profile.Model) +
                       string.Join(", ", ids.Take(10));
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            return Loc.T("msg.openAiCompatibleConnector.4");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Проба: GET {Url} — исключение", url);
            return ex.Message;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>Базовый URL из профайла без завершающего «/»; для OpenAI указывается с /v1.</summary>
    private static string BaseUrl(ModelProfile profile)
    {
        if (profile.BaseUrl.Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.openAiCompatibleConnector.5"));
        }
        return profile.BaseUrl.TrimEnd('/');
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url,
        string apiKey, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (apiKey.Length > 0) // пустой ключ — локальный сервер без авторизации (llama-server/Ollama)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
    }

    /// <summary>Человекочитаемый текст из тела ошибки OpenAI-формата ({"error":{"message":…}}).</summary>
    private static string ErrorMessageOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? body;
                }
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }
            }
        }
        catch (JsonException)
        {
            // не JSON — вернём как есть
        }
        return body.Length > 500 ? body[..500] : body;
    }

    /// <summary>Когда лимит отпустит (ТЗ v1.37): Retry-After (секунды или дата) либо
    /// x-ratelimit-reset-requests/-tokens OpenAI-формата («1s», «6m12s», «250ms»);
    /// null — провайдер не сообщил.</summary>
    private static DateTime? RetryAtOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return DateTime.UtcNow + delta;
        }
        if (retryAfter?.Date is { } date)
        {
            return date.UtcDateTime;
        }
        foreach (var name in new[] { "x-ratelimit-reset-requests", "x-ratelimit-reset-tokens" })
        {
            if (Header(response, name) is { } raw && TryParseResetSpan(raw, out var span))
            {
                return DateTime.UtcNow + span;
            }
        }
        return null;
    }

    /// <summary>Текущий % использования лимита из заголовков x-ratelimit-limit-* /
    /// x-ratelimit-remaining-* (по запросам и токенам берётся больший); null — заголовков нет.</summary>
    private static double? UsedLimitPercentOf(HttpResponseMessage response)
    {
        double? percent = null;
        foreach (var kind in new[] { "requests", "tokens" })
        {
            if (double.TryParse(Header(response, $"x-ratelimit-limit-{kind}"),
                    System.Globalization.CultureInfo.InvariantCulture, out var total) && total > 0
                && double.TryParse(Header(response, $"x-ratelimit-remaining-{kind}"),
                    System.Globalization.CultureInfo.InvariantCulture, out var remaining))
            {
                var used = Math.Clamp((1.0 - remaining / total) * 100.0, 0, 100);
                percent = percent is null ? used : Math.Max(percent.Value, used);
            }
        }
        return percent;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>Интервал сброса OpenAI-формата: «6m12s», «1s», «250ms», «1h2m».</summary>
    private static bool TryParseResetSpan(string raw, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        var matches = System.Text.RegularExpressions.Regex.Matches(raw.Trim(), @"(\d+(?:\.\d+)?)(ms|h|m|s)");
        if (matches.Count == 0)
        {
            return false;
        }
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var value = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            span += m.Groups[2].Value switch
            {
                "ms" => TimeSpan.FromMilliseconds(value),
                "h" => TimeSpan.FromHours(value),
                "m" => TimeSpan.FromMinutes(value),
                _ => TimeSpan.FromSeconds(value),
            };
        }
        return true;
    }
}
