using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Подключение по CLI (todo29): поле transport профайла ("api" по умолчанию / "cli"),
/// команда cliCommand, маршрутизация реестра коннекторов на ClaudeCliConnector,
/// сид-модели «*_cli» в справочнике и разбор итогового JSON headless-запуска CLI.
/// </summary>
public sealed class Todo29Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void ModelProfile_parses_cli_transport_and_command()
    {
        var profile = ModelProfile.Parse("""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-fable-5",
              "secretRef": "",
              "params": {}
            }
            """);
        Assert.True(profile.IsCli);
        Assert.Equal("cli", profile.Transport);
        Assert.Equal("claude --permission-mode acceptEdits", profile.CliCommand);
        Assert.Equal("", profile.SecretRef);
    }

    [Fact]
    public void ModelProfile_without_transport_defaults_to_api()
    {
        var profile = ModelProfile.Parse("""{ "provider": "anthropic", "model": "m" }""");
        Assert.Equal("api", profile.Transport);
        Assert.False(profile.IsCli);
        Assert.Equal("", profile.CliCommand);
    }

    [Fact]
    public void Registry_resolves_cli_transport_to_claude_cli_connector()
    {
        _f.Models.Seed();
        var cliModel = _f.Models.List().Single(m => m.Name == "Claude-Fable-5_cli");
        var apiModel = _f.Models.List().Single(m => m.Name == "Claude-Fable-5");
        var cli = _f.Executors.Create(new Executor
        {
            Nick = "клод-cli", Kind = ExecutorKind.Ai, ModelId = cliModel.Id,
        }, null);
        var api = _f.Executors.Create(new Executor
        {
            Nick = "клод-api", Kind = ExecutorKind.Ai, ModelId = apiModel.Id,
        }, null);

        Assert.Equal(ClaudeCliConnector.ProviderKind, _f.Connectors.Resolve(cli).Kind);
        Assert.Equal("anthropic", _f.Connectors.Resolve(api).Kind);
    }

    [Fact]
    public void Registry_rejects_cli_transport_for_non_anthropic_provider()
    {
        var model = _f.Models.Create(new AiModel { Name = "DeepSeek-CLI-Test" }, null);
        _f.Files.WriteText(model.ProfilePath,
            """{ "provider": "openai-compatible", "transport": "cli", "model": "deepseek-v4-pro" }""");
        var executor = _f.Executors.Create(new Executor
        {
            Nick = "дипсик-cli", Kind = ExecutorKind.Ai, ModelId = model.Id,
        }, null);

        var ex = Assert.Throws<InvalidOperationException>(() => _f.Connectors.Resolve(executor));
        Assert.Contains("cli", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openai-compatible", ex.Message);
    }

    [Fact]
    public void Seed_creates_cli_models_with_cli_transport_and_zero_cost()
    {
        _f.Models.Seed();
        foreach (var name in new[] { "Claude-Fable-5_cli", "Claude-Opus-5.0_cli" })
        {
            var model = _f.Models.List().SingleOrDefault(m => m.Name == name);
            Assert.NotNull(model);
            Assert.False(model!.IsCustom);
            var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
            Assert.True(profile.IsCli);
            Assert.Equal("anthropic", profile.Provider);
            Assert.StartsWith("claude", profile.CliCommand); // + флаги прав (acceptEdits)
            Assert.Equal("", profile.SecretRef); // авторизация сессией CLI, не ключом
            Assert.Contains("\"in_per_1m\": 0.0", _f.Files.ReadText(model.CapabilitiesPath));
        }
    }

    [Fact]
    public void ParseCliOutput_reads_result_tokens_and_model()
    {
        var output = ClaudeCliConnector.ParseCliOutput("""
            {
              "type": "result",
              "subtype": "success",
              "is_error": false,
              "result": "# Готово\n\nОтвет агента.",
              "usage": {
                "input_tokens": 10,
                "cache_creation_input_tokens": 200,
                "cache_read_input_tokens": 3000,
                "output_tokens": 456
              },
              "modelUsage": {
                "claude-haiku-4-5-20251001": { "inputTokens": 526, "outputTokens": 17 },
                "claude-fable-5": { "inputTokens": 10, "cacheReadInputTokens": 3000, "outputTokens": 456 }
              },
              "session_id": "6ef91a72-e364-45b8-84e6-e248f0fa67d2",
              "total_cost_usd": 0.0123
            }
            """, "fallback-model");
        Assert.False(output.IsError);
        Assert.Equal("# Готово\n\nОтвет агента.", output.Text);
        Assert.Equal(3210, output.InputTokens); // input + кэш (создание и чтение)
        Assert.Equal(456, output.OutputTokens);
        Assert.Equal("claude-fable-5", output.Model);
        Assert.Equal("success", output.StopReason);
        Assert.Equal("6ef91a72-e364-45b8-84e6-e248f0fa67d2", output.SessionId);
    }

    [Fact]
    public void FileLinkNote_gives_external_url_template_of_file_url_format()
    {
        var files = new FileToolset(_f.Dir, "proj-1", "http://localhost:5480/ai2p");
        var note = ClaudeCliConnector.FileLinkNote(files);
        Assert.Contains("http://localhost:5480/ai2p/api/files/project?projectId=proj-1&path=", note);
        Assert.Contains("НЕ придумывай URL", note);

        // без проекта/базового URL (ссылки построить нельзя) — подсказки нет
        Assert.Equal("", ClaudeCliConnector.FileLinkNote(new FileToolset(_f.Dir)));
    }

    [Fact]
    public void TryParseQuestionMarker_parses_question_with_options()
    {
        var text = "Промежуточный отчёт.\n\n" +
                   """AI2P_QUESTION: {"question": "Сохранить файл?", "options": ["да", "нет"]}""";
        Assert.True(ClaudeCliConnector.TryParseQuestionMarker(text, out var q, out var opts));
        Assert.Equal("Сохранить файл?", q);
        Assert.Equal(["да", "нет"], opts);
    }

    [Fact]
    public void TryParseQuestionMarker_tolerates_code_fence_and_plain_text()
    {
        // маркер внутри код-блока — JSON читается до конца объекта, хвост ``` игнорируется
        var fenced = "```\nAI2P_QUESTION: {\"question\": \"Можно?\"}\n```";
        Assert.True(ClaudeCliConnector.TryParseQuestionMarker(fenced, out var q1, out var opts1));
        Assert.Equal("Можно?", q1);
        Assert.Empty(opts1);

        // невалидный JSON — вопросом становится остаток строки после маркера
        Assert.True(ClaudeCliConnector.TryParseQuestionMarker(
            "AI2P_QUESTION: сохранить в корень?", out var q2, out _));
        Assert.Equal("сохранить в корень?", q2);

        // маркера нет — не вопрос
        Assert.False(ClaudeCliConnector.TryParseQuestionMarker("Обычный ответ.", out _, out _));
    }

    [Fact]
    public void ParseCliOutput_reports_error_result_and_bad_json()
    {
        var output = ClaudeCliConnector.ParseCliOutput(
            """{ "type": "result", "subtype": "error_during_execution", "is_error": true, "result": "beda" }""",
            "claude-fable-5");
        Assert.True(output.IsError);
        Assert.Equal("error_during_execution", output.StopReason);
        Assert.Equal("claude-fable-5", output.Model); // modelUsage нет — модель из профайла

        Assert.Throws<InvalidOperationException>(
            () => ClaudeCliConnector.ParseCliOutput("Not logged in", "m"));
        Assert.Throws<InvalidOperationException>(
            () => ClaudeCliConnector.ParseCliOutput("{ оборванный", "m"));
    }
}
