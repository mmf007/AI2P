using AI2P.Connectors;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Доработки todo37_2 по итогам живой проверки: диагностика аварийного завершения
/// локального сервера модели (ComfyUI упал с кодом -1073741819, а причину было не видно —
/// вывод процесса уходил только в технический лог уровня Debug).
/// </summary>
public sealed class Todo37_2LocalServerTests
{
    /// <summary>Команда, которая что-то печатает и падает с ненулевым кодом.</summary>
    private static string FailingCommand(string message, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"cmd /c echo {message}& exit /b {exitCode}"
            : $"/bin/sh -c \"echo {message}; exit {exitCode}\"";

    private static async Task<LocalModelProcessService> RunUntilExitAsync(string command)
    {
        var service = new LocalModelProcessService();
        // baseUrl заведомо никем не занят — сервис решит, что внешнего сервера нет, и запустит процесс
        await service.EnsureStartedAsync(command, "http://127.0.0.1:1", "team-1");
        for (var i = 0; i < 100 && service.ProcessState(command) is { Alive: true }; i++)
        {
            await Task.Delay(50);
        }
        return service;
    }

    [Fact]
    public async Task Exit_report_contains_process_output()
    {
        var command = FailingCommand("server-crashed-here", 5);
        var service = await RunUntilExitAsync(command);

        var state = service.ProcessState(command);
        Assert.NotNull(state);
        Assert.False(state!.Value.Alive);
        Assert.Equal(5, state.Value.ExitCode);

        // именно вывод процесса объясняет падение — он обязан попасть в сообщение
        var report = service.ExitReport(command, state.Value.ExitCode);
        Assert.Contains("server-crashed-here", report);
        Assert.Contains("код 5", report);
        Assert.Contains("прожил", report);
    }

    [Fact]
    public async Task Exit_report_says_there_was_no_output()
    {
        // процесс упал молча — сообщение должно об этом честно сказать, а не молчать само
        var command = OperatingSystem.IsWindows()
            ? "cmd /c exit /b 7"
            : "/bin/sh -c \"exit 7\"";
        var service = await RunUntilExitAsync(command);

        var report = service.ExitReport(command, service.ProcessState(command)!.Value.ExitCode);
        Assert.Contains("вывода в stdout/stderr не было", report);
    }

    [Theory]
    // 0xC0000005 — именно с ним упал ComfyUI у заказчика: код должен быть расшифрован
    [InlineData(unchecked((int)0xC0000005), "0xC0000005")]
    [InlineData(unchecked((int)0xC0000135), "0xC0000135")]
    public void Known_windows_crash_codes_are_explained(int exitCode, string expected)
    {
        var service = new LocalModelProcessService();
        var report = service.ExitReport("несуществующая команда", exitCode);
        Assert.Contains(expected, report);
        Assert.Contains(exitCode.ToString(), report);
    }

    [Fact]
    public async Task Cuda_init_failure_in_output_is_diagnosed()
    {
        // разобранный случай: ComfyUI падает access violation'ом внутри инициализации CUDA,
        // потому что драйвер NVIDIA старее CUDA-сборки torch — сообщение обязано это назвать
        var command = FailingCommand("cudaGetDeviceCount returned cudaErrorNotSupported", 5);
        var service = await RunUntilExitAsync(command);

        var report = service.ExitReport(command, service.ProcessState(command)!.Value.ExitCode);
        Assert.Contains("ДРАЙВЕР NVIDIA СТАРЕЕ", report);
        Assert.Contains("nvidia-smi", report);
    }

    [Fact]
    public async Task Ordinary_output_gets_no_invented_diagnosis()
    {
        // на обычном падении никаких догадок про CUDA быть не должно
        var command = FailingCommand("port already in use", 1);
        var service = await RunUntilExitAsync(command);

        var report = service.ExitReport(command, service.ProcessState(command)!.Value.ExitCode);
        Assert.DoesNotContain("ДРАЙВЕР NVIDIA", report);
        Assert.Contains("port already in use", report);
    }
}
