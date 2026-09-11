using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo_bugfix_2: обрыв генерации по лимиту выходных токенов (finishReason «length» /
/// «max_tokens») — задание должно завершаться ошибкой с подсказкой, а не пустым
/// результатом со статусом «проверка».
/// </summary>
public class TodoBugfix2Tests
{
    private sealed class Probe : AiConnectorBase
    {
        private Probe() : base(null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null, null!, () => "ru", "")
        {
        }

        public override string Kind => "probe";

        protected override Task<AiCallOutcome> CallAsync(ModelProfile profile, string apiKey,
            string requestText, AgentToolset tools, AgentResume? resume, CancellationToken ct)
            => throw new NotSupportedException();

        protected override Task<string?> ProbeAsync(ModelProfile profile, string apiKey,
            CancellationToken ct) => throw new NotSupportedException();

        public static bool Truncated(string finishReason) => IsTruncatedOutput(finishReason);
    }

    [Theory]
    [InlineData("length")]          // OpenAI-совместимые (llama-server, DeepSeek, Qwen)
    [InlineData("Length")]
    [InlineData("max_tokens")]      // Anthropic (snake_case)
    [InlineData("MaxTokens")]       // Anthropic SDK (значение enum StopReason)
    public void Truncated_FinishReasons_Detected(string reason) =>
        Assert.True(Probe.Truncated(reason));

    [Theory]
    [InlineData("stop")]
    [InlineData("end_turn")]
    [InlineData("tool_calls")]
    [InlineData("")]
    public void Normal_FinishReasons_Not_Truncated(string reason) =>
        Assert.False(Probe.Truncated(reason));
}
