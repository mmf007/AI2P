using System.Diagnostics;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-152-S0 — потоковое чтение вывода внешней программы (<see cref="ProcessOutputLog"/>) и
/// тайм-аут МОЛЧАНИЯ вместо двенадцатичасового висяка.
///
/// Проверяется ровно то, на чём висело обучение LoRA: программа печатает БОЛЬШЕ БУФЕРА ТРУБЫ
/// (у Windows около 4 КБ), а мы её при этом дожидаемся. Пока потоки не вычитывались во время
/// работы, такая программа вставала на записи в консоль навсегда — и висела до общего предела
/// в 720 минут.
/// </summary>
public sealed class T152S0Tests
{
    /// <summary>Программа, печатающая заведомо больше 8 КБ: 400 строк по ~45 знаков.</summary>
    private static Process StartTalker()
    {
        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = "cmd.exe";
            info.Arguments =
                "/c for /L %i in (1,1,400) do @echo AI2P-0123456789012345678901234567890 %i";
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(
                "i=1; while [ $i -le 400 ]; do echo AI2P-0123456789012345678901234567890 $i;"
                + " i=$((i+1)); done");
        }
        return Process.Start(info)!;
    }

    /// <summary>Программа, которая молчит: ровно тот случай, ради которого заведён тайм-аут
    /// молчания.</summary>
    private static Process StartSilent()
    {
        var info = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = "cmd.exe";
            info.Arguments = "/c ping -n 60 127.0.0.1 > nul";
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("sleep 60");
        }
        return Process.Start(info)!;
    }

    /// <summary>Прочитать журнал, который прямо сейчас пишется: обычное чтение файла просит
    /// у системы монопольную запись и на открытом журнале отказывает.</summary>
    private static string[] ReadOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Talkative_Process_Does_Not_Hang_And_Its_Output_Is_Read_While_It_Runs()
    {
        var file = Path.Combine(Path.GetTempPath(), "ai2p-t152s0-" + Guid.NewGuid().ToString("N"),
            "train.log");
        var console = new JobConsole();
        using var log = new ProcessOutputLog(file, console, "lora:test");
        using var process = StartTalker();
        log.Attach(process);

        // тайм-аут молчания здесь ни при чём: программа печатает без остановки
        var exited = await log.WaitForExitAsync(process, TimeSpan.FromMinutes(1),
            new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);

        Assert.True(exited);
        Assert.Equal(0, process.ExitCode);
        // хвост — последние строки, а не первые: причина падения всегда в конце
        Assert.Contains("AI2P-", log.Tail());
        Assert.Contains("400", log.Tail());
        // журнал на диске переживает перезапуск сервера, буфер в памяти — нет. Читаем его,
        // не закрывая: смотреть журнал ИДУЩЕГО обучения человек должен уметь так же
        var lines = ReadOpen(file);
        Assert.True(lines.Length >= 400, $"строк в журнале {lines.Length}, ожидалось не меньше 400");
        // и то же самое видно живой консолью задания
        var (shown, _, _) = console.Read("lora:test", 0);
        Assert.NotEmpty(shown);
        Directory.Delete(Path.GetDirectoryName(file)!, true);
    }

    [Fact]
    public async Task Silent_Process_Ends_On_The_Idle_Timeout_And_Not_On_The_Total_One()
    {
        using var log = new ProcessOutputLog();
        using var process = StartSilent();
        log.Attach(process);
        var clock = Stopwatch.StartNew();

        // общий предел (как у обучения) — крупный; кончиться всё должно по МОЛЧАНИЮ
        var exited = await log.WaitForExitAsync(process, TimeSpan.FromSeconds(2),
            new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);

        Assert.False(exited);
        Assert.False(process.HasExited);     // гасит зависшую программу вызывающий, а не журнал
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60),
            $"молчание заметили через {clock.Elapsed.TotalSeconds:F0} с");
        process.Kill(entireProcessTree: true);
    }

    [Fact]
    public void Progress_Bar_Redrawn_By_Carriage_Return_Becomes_Separate_Lines()
    {
        using var log = new ProcessOutputLog();

        // так печатает tqdm: перерисовка через «\r» без перевода строки
        log.Write("шаг 1/3\rшаг 2/3\rшаг 3/3\n");
        log.Write("готово");     // кусок потока может кончиться посреди строки

        var tail = log.Tail();
        Assert.Contains("шаг 1/3", tail);
        Assert.Contains("шаг 3/3", tail);
        Assert.Contains("готово", tail);
    }

    [Fact]
    public void The_Tail_Keeps_The_End_Of_The_Output_And_Not_Its_Beginning()
    {
        using var log = new ProcessOutputLog();
        for (var i = 1; i <= 500; i++)
        {
            log.Write($"строка {i}\n");
        }

        var tail = log.Tail();
        Assert.True(tail.Length <= ProcessOutputLog.TailChars + 1, $"хвост {tail.Length} знаков");
        Assert.Contains("строка 500", tail);
        Assert.DoesNotContain("строка 1\n", tail);
    }

    [Fact]
    public void The_Idle_Timeout_Is_Configurable_And_Its_Default_Is_Far_Below_The_Total_Limit()
    {
        var wait = LoraSettings.Parse(
            """
            {
              "provider": "comfyui",
              "lora": {
                "supported": true,
                "train": {
                  "kind": "process",
                  "wait": { "kind": "process", "timeoutMinutes": 600, "idleMinutes": 7 }
                }
              }
            }
            """).Train.Wait;

        Assert.Equal(7, wait.IdleMinutes);
        Assert.Equal(600, wait.TimeoutMinutes);
        Assert.True(LoraTrainService.DefaultIdleMinutes < LoraTrainService.DefaultTimeoutMinutes / 10,
            "молчание должно кончаться минутами, а не половиной суток");
    }
}
