using AI2P.Core;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;

namespace AI2P.Connectors;

/// <summary>
/// Подбор коннектора для исполнителя (ТЗ п. 7.2): человек — HumanConnector,
/// ИИ — по полю provider профайла модели (п. 2.9): "anthropic" (Claude, этап 2.1),
/// "openai-compatible" (DeepSeek/Qwen/OpenAI/Ollama, этап 2.2); при transport "cli"
/// (todo29) — CLI-коннектор (сейчас только Claude Code для провайдера "anthropic").
/// </summary>
public sealed class ConnectorRegistry
{
    private readonly FileStore _files;
    private readonly HumanConnector _human;
    private readonly ClaudeCliConnector _claudeCli;
    private readonly IReadOnlyDictionary<string, IAgentConnector> _aiByProvider;

    /// <summary>Исполнитель «авто ПО» (T-154-S0): запуск внешней программы плагина; null —
    /// подключения нет (старая обвязка и тесты, которым программы не нужны).</summary>
    public SoftwareConnector? Software { get; }

    /// <param name="falAi">Облачные медиа-модели через шлюз fal.ai (T-14-S0): null —
    /// подключения нет (старая обвязка и тесты, которым медиа-шлюз не нужен).</param>
    /// <param name="software">Исполнитель «авто ПО» (T-154-S0).</param>
    public ConnectorRegistry(FileStore files, HumanConnector human, AnthropicConnector anthropic,
        OpenAiCompatibleConnector openAiCompatible, ClaudeCliConnector claudeCli,
        ComfyUiConnector comfyUi, FalAiConnector? falAi = null,
        SoftwareConnector? software = null)
    {
        _files = files;
        _human = human;
        _claudeCli = claudeCli;
        Software = software;
        var byProvider = new Dictionary<string, IAgentConnector>(StringComparer.OrdinalIgnoreCase)
        {
            [AnthropicConnector.Provider] = anthropic,
            [OpenAiCompatibleConnector.Provider] = openAiCompatible,
            // медиа-модели через локальный ComfyUI (ТЗ v1.41, todo36_4): Kandinsky 5.0 и др.
            [ComfyUiConnector.Provider] = comfyUi,
        };
        if (falAi is not null)
        {
            // ОБЛАЧНОЕ медиа (T-14-S0, отчёт T-213 §6.2–6.5): Seedance, Veo, Kling, GPT Image,
            // Nano Banana, ElevenLabs, Tripo, Meshy — своего OpenAI-совместимого входа у них
            // нет, и подключаются они одним протоколом очереди шлюза
            byProvider[FalAiConnector.Provider] = falAi;
        }
        _aiByProvider = byProvider;
    }

    /// <summary>
    /// ПОДПИСАТЬ ВСЕ ИИ-КОННЕКТОРЫ на завершение задания СУФЛЁРА (T-292-S0). Суфлёром может
    /// работать любой исполнитель — CLI-подписка, локальная модель, облачное API, — поэтому
    /// обработчик ставится не одному коннектору, а всем: какой из них отработает, решает
    /// профайл выбранного суфлёра.
    /// </summary>
    public void OnPrompterFinished(Func<Job, TaskItem, string, Task> handler)
    {
        foreach (var connector in _aiByProvider.Values.Append(_claudeCli))
        {
            if (connector is AiConnectorBase ai)
            {
                ai.PrompterFinished = handler;
            }
        }
    }

    /// <summary>Коннектор исполнителя; для ИИ с неподдержанным провайдером — ошибка с пояснением.</summary>
    public IAgentConnector Resolve(Executor executor)
    {
        if (executor.Kind == ExecutorKind.Human)
        {
            return _human;
        }
        if (executor.Kind == ExecutorKind.Software)
        {
            // «авто ПО» (T-153-S0, T-154-S0): запуск внешней программы плагина. Профайла
            // модели у программы нет и быть не должно, поэтому ветка своя и стоит до ProfileOf;
            // подключения нет вовсе (старая обвязка) — отказ внятный, а не «нет профайла»
            return Software ?? throw new InvalidOperationException(
                Loc.T("msg.connectorRegistry.4", executor.Nick));
        }
        var profile = ProfileOf(executor);
        if (profile.Provider.Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.connectorRegistry.1", executor.Nick));
        }
        // подключение по CLI (transport "cli", todo29): пока только Claude Code
        if (profile.IsCli)
        {
            if (!string.Equals(profile.Provider, AnthropicConnector.Provider,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    Loc.T("msg.connectorRegistry.2", AnthropicConnector.Provider, profile.Provider));
            }
            return _claudeCli;
        }
        if (!_aiByProvider.TryGetValue(profile.Provider, out var connector))
        {
            throw new InvalidOperationException(
                Loc.T("msg.connectorRegistry.3", profile.Provider, string.Join(", ", _aiByProvider.Keys)));
        }
        return connector;
    }

    /// <summary>Профайл подключения исполнителя-ИИ (файл модели из справочника, п. 2.9).</summary>
    public ModelProfile ProfileOf(Executor executor)
    {
        if (executor.ProfilePath.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.37", executor.Nick));
        }
        var json = _files.ReadText(executor.ProfilePath);
        if (json.Trim().Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.38", executor.ProfilePath));
        }
        return ModelProfile.Parse(json);
    }
}
