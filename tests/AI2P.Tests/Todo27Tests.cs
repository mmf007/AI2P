using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Локальная модель (todo27_2): поле launchCommand профайла, пустой secretRef
/// (сервер без авторизации), разбор команды запуска и импорт модели инсталлятором
/// (data/models/import_*.json → запись справочника + профайл + декларация).
/// </summary>
public sealed class Todo27Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void ModelProfile_parses_launch_command_and_allows_empty_secret_ref()
    {
        var profile = ModelProfile.Parse("""
            {
              "provider": "openai-compatible",
              "model": "qwen3.6-35b-a3b",
              "baseUrl": "http://localhost:8080/v1",
              "secretRef": "",
              "launchCommand": "C:\\ai\\llama\\llama-server.exe -m C:\\ai\\models\\q.gguf --port 8080",
              "params": { "maxTokens": 8192 }
            }
            """);
        Assert.Equal("openai-compatible", profile.Provider);
        Assert.Equal("", profile.SecretRef);
        Assert.StartsWith("C:\\ai\\llama\\llama-server.exe", profile.LaunchCommand);
        Assert.Equal(8192, profile.MaxTokens);
    }

    [Fact]
    public void ModelProfile_without_launch_command_is_empty()
    {
        var profile = ModelProfile.Parse("""{ "provider": "openai-compatible", "model": "m" }""");
        Assert.Equal("", profile.LaunchCommand);
    }

    [Theory]
    [InlineData("llama-server -m model.gguf --port 8080", "llama-server", "-m model.gguf --port 8080")]
    [InlineData("\"C:\\Program Files\\llama\\llama-server.exe\" -m q.gguf",
        "C:\\Program Files\\llama\\llama-server.exe", "-m q.gguf")]
    [InlineData("llama-server", "llama-server", "")]
    public void SplitCommand_separates_file_and_arguments(string command, string file, string args)
    {
        var (fileName, arguments) = LocalModelProcessService.SplitCommand(command);
        Assert.Equal(file, fileName);
        Assert.Equal(args, arguments);
    }

    [Fact]
    public void ImportPending_creates_custom_model_with_profile_and_scope()
    {
        var importPath = Path.Combine(_f.Files.Abs("models"), "import_qwen.json");
        Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
        File.WriteAllText(importPath, """
            {
              "name": "Qwen3.6-35B-A3B-Local",
              "profile": {
                "provider": "openai-compatible",
                "model": "qwen3.6-35b-a3b",
                "baseUrl": "http://localhost:8080/v1",
                "secretRef": "",
                "launchCommand": "llama\\llama-server.exe -m models\\q.gguf",
                "params": { "maxTokens": 8192 }
              },
              "scope": {
                "id": "qwen3.6-35b-a3b",
                "skills": [ { "name": "code-write", "score": 86 } ],
                "cost": { "in_per_1m": 0, "out_per_1m": 0 }
              }
            }
            """);

        _f.Models.ImportPending();

        var model = _f.Models.List().SingleOrDefault(m => m.Name == "Qwen3.6-35B-A3B-Local");
        Assert.NotNull(model);
        Assert.True(model!.IsCustom);
        var profile = ModelProfile.Parse(_f.Files.ReadText(model.ProfilePath));
        Assert.Equal("openai-compatible", profile.Provider);
        Assert.Equal("http://localhost:8080/v1", profile.BaseUrl);
        Assert.Contains("llama-server.exe", profile.LaunchCommand);
        Assert.Contains("qwen3.6-35b-a3b", _f.Files.ReadText(model.CapabilitiesPath));
        Assert.False(File.Exists(importPath));
        Assert.True(File.Exists(importPath + ".done"));

        // повторный импорт (файл снова подложен, модель уже есть) — без дубля и без ошибки
        File.Move(importPath + ".done", importPath);
        _f.Models.ImportPending();
        Assert.Single(_f.Models.List(), m => m.Name == "Qwen3.6-35B-A3B-Local");
        Assert.True(File.Exists(importPath + ".done"));
    }

    [Fact]
    public void ImportPending_with_broken_json_reports_clear_error()
    {
        var importPath = Path.Combine(_f.Files.Abs("models"), "import_broken.json");
        Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
        File.WriteAllText(importPath, "{ не json");
        var ex = Assert.Throws<InvalidOperationException>(() => _f.Models.ImportPending());
        Assert.Contains("import_broken.json", ex.Message);
        File.Delete(importPath);
    }
}
