using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Server.Cli;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-96-S0: ОШИБКИ РАБОТЫ АГЕНТОВ — две беды, найденные по выпуску 1.111.
///
/// 1. МАРКЕР ДЕЙСТВИЯ, ВЫВЕДЕННЫЙ НЕ В ПОСЛЕДНЕМ ВИТКЕ ХОДА, ПРОПАДАЛ МОЛЧА.
///    Маркеры (AI2P_SUBTASK, AI2P_EXPERIENCE, AI2P_TEMPLATE, AI2P_MOVE_TASK,
///    AI2P_SET_STATUS, AI2P_RESTART_TASK) разбирались один раз — после выхода из цикла
///    вызовов CLI, из ПОСЛЕДНЕГО ответа. А витков у хода много: чтение чужих заданий,
///    получение файлов, перебивка чатом (T-161), доставка сообщения к концу хода, переспрос
///    недоделанной сдачи (T-138), — и каждый следующий виток затирал текст предыдущего.
///    Так у T-230 пропали шесть подзадач перевода doc/es: агент видел свои маркеры в истории
///    сессии, написал в отчёте «работа передана шести подзадачам», а в дереве их не было.
///
/// 2. ТЕКСТ, ПОДАННЫЙ КЛИЕНТУ ai2p СО СТАНДАРТНОГО ВВОДА, ЧИТАЛСЯ КОДИРОВКОЙ КОНСОЛИ
///    (на русской Windows cp866): заголовки подзадач уезжали в базу мусором
///    «╨Я╨╡╤А╨╡╨▓╨╛╨┤» при коде возврата 0 — восемь заведённых так задач (T-79-S0…T-86-S0)
///    пришлось отменять. Ввод теперь читается как UTF-8, а испорченный текст отбивается
///    внятным отказом с подсказкой про «--имя-file».
/// </summary>
public sealed class T96S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string Marker = ClaudeCliConnector.SubtaskMarker;

    /// <summary>Сдача с обещанием доделать позже — по ней система даёт ещё один виток (T-138).</summary>
    private const string Unfinished =
        "Прогон тестов идёт в фоне. Как только он закончится, соберу выкладку и подведу итог.";

    // ---------- 1. маркеры промежуточного витка ----------

    [Fact]
    public async Task Subtask_Marker_Of_An_Intermediate_Turn_Is_Not_Lost()
    {
        // первый ответ агента: подзадача заведена маркером И работа сдана недоделанной —
        // система даёт второй виток той же сессии, и текст первого ответа заменяется
        var (job, task) = await RunCliJobAsync("cli-t96s0",
            "Разбил работу на части.\n"
            + Marker + " {\"title\": \"Перевод doc/es: man\", \"description\": \"перевести раздел\"}\n"
            + Unfinished,
            "Готово, итог подведён.");

        // задача ушла на подзадачи (T-185): задание ждёт их завершения той же сессией
        Assert.Equal(JobState.WaitingHuman, job.State);
        var child = Assert.Single(_f.Tasks.ListChildren(task.Id));
        Assert.Equal("Перевод doc/es: man", child.Title);
        // сводка о выполненном маркере доезжает до человека вместе с итоговым ответом
        var result = _f.Files.ReadText(job.ResultPath!);
        Assert.Contains("Готово, итог подведён.", result);
        Assert.Contains("Перевод doc/es: man", result);
        Assert.DoesNotContain(Marker, result);
    }

    [Fact]
    public async Task Subtask_Marker_Of_The_Last_Turn_Is_Applied_Once()
    {
        // тот же маркер в ИТОГОВОМ ответе: подзадача ровно одна, двойного создания нет
        var (job, task) = await RunCliJobAsync("cli-t96s0-last",
            "Готово.\n" + Marker + " {\"title\": \"Одна\", \"description\": \"д\"}",
            "не должно понадобиться");

        Assert.Equal(JobState.WaitingHuman, job.State);
        var child = Assert.Single(_f.Tasks.ListChildren(task.Id));
        Assert.Equal("Одна", child.Title);
    }

    // ---------- 2. испорченная кодировка у клиента ai2p ----------

    [Fact]
    public void Broken_Encoding_Is_Recognised()
    {
        // «Перевод doc/zh-cn: man» в UTF-8, прочитанный как cp866 — ровно то, что уехало
        // в базу заголовками восьми отменённых задач
        Assert.True(AgentCliRunner.LooksLikeBrokenEncoding("╨Я╨╡╤А╨╡╨▓╨╛╨┤ doc/zh-cn: man"));
        Assert.True(AgentCliRunner.LooksLikeBrokenEncoding("ÐŸÐµÑ€ÐµÐ²Ð¾Ð´ doc/es"));
    }

    [Fact]
    public void Healthy_Text_Is_Not_Refused()
    {
        Assert.False(AgentCliRunner.LooksLikeBrokenEncoding(""));
        Assert.False(AgentCliRunner.LooksLikeBrokenEncoding("Перевод doc/es: раздел man"));
        Assert.False(AgentCliRunner.LooksLikeBrokenEncoding("Translate doc/es, part 1"));
        Assert.False(AgentCliRunner.LooksLikeBrokenEncoding("翻译 doc/zh-cn 手册"));
        // рамка в таблице — законный знак, отказывать из-за неё нельзя
        Assert.False(AgentCliRunner.LooksLikeBrokenEncoding(
            "Описание задачи с рамкой ═══ внутри длинного человеческого текста про перевод."));
    }

    // ---------- фикстура поддельного CLI (устройство — как в T138Tests) ----------

    private string FakeCli(string dirName, string out1Json, string out2Json)
    {
        var dir = Path.Combine(_f.Dir, dirName);
        Directory.CreateDirectory(dir);
        var utf8 = new System.Text.UTF8Encoding(false);
        File.WriteAllText(Path.Combine(dir, "out1.json"), out1Json, utf8);
        File.WriteAllText(Path.Combine(dir, "out2.json"), out2Json, utf8);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd,
                "@echo off\r\n" +
                "if exist \"%~dp0prompt1.txt\" goto second\r\n" +
                "findstr \"^\" > \"%~dp0prompt1.txt\"\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "exit /b 0\r\n" +
                ":second\r\n" +
                "findstr \"^\" > \"%~dp0prompt2.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "d=\"$(dirname \"$0\")\"\n" +
            "if [ -f \"$d/prompt1.txt\" ]; then cat > \"$d/prompt2.txt\"; cat \"$d/out2.json\";\n" +
            "else cat > \"$d/prompt1.txt\"; cat \"$d/out1.json\"; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string CliJson(string answer) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = "s-96s0",
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    private async Task<(Job Job, TaskItem Task)> RunCliJobAsync(string dirName, string first, string second)
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var scopeRel = $"models/scope_{dirName}.json";
        _f.Files.WriteText(scopeRel, """
            {
              "skills": [ { "name": "code-write", "score": 90 } ],
              "cost": { "in_per_1m": 1, "out_per_1m": 2 }
            }
            """);
        var ai = _f.Executors.Create(new Executor
        {
            Nick = "клод-" + dirName, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel,
        }, null);
        var cliPath = FakeCli(dirName, CliJson(first), CliJson(second));
        var profileRel = $"models/profile_{dirName}.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{cliPath}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        ai.ProfilePath = profileRel;
        ai = _f.Executors.Update(ai, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Перевод", ExecutorIds = [ai.Id],
        }, "переведи документацию", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "переведи документацию");
        return (await WaitForJob(job.Id), task);
    }

    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 300; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Задание {jobId} не завершилось за 30 с");
    }
}
