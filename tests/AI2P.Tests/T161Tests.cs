using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-161: ПЕРЕБИВКА АГЕНТА ЧАТОМ. Пока агент работает, человек пишет в чат задачи —
/// уточнение, встречный вопрос или экстренное «стой, не то делаешь». До T-161 агент такого
/// сообщения не видел вовсе: у CLI-агента инструментов AI2P нет, а API-агент читает чат,
/// только если сам позовёт get_task_chat (и только в начале работы). Сообщение доходило
/// до агента никогда, а ответ на него человек получал в лучшем случае в итоговом результате.
///
/// Теперь: за чатом задачи следит <see cref="ChatWatch"/>; новое сообщение человека
/// доставляется агенту прямо в ходе работы (CLI-коннектор для этого ПРЕРЫВАЕТ процесс и
/// продолжает ту же сессию `--resume`, API-коннекторы подставляют текст на ближайшем витке
/// цикла инструментов), а ответ агента уходит обратно в чат — маркером AI2P_CHAT
/// у CLI-агента и инструментом send_chat_message у остальных.
/// </summary>
public sealed class T161Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private const string ChatMarker = ClaudeCliConnector.ChatMarker;

    private Executor Human(string nick = "человек") =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    private Executor CreateAi(string nick)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """
            {
              "skills": [ { "name": "code-test", "score": 90 } ],
              "cost": { "in_per_1m": 1, "out_per_1m": 2 }
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = nick, Kind = ExecutorKind.Ai, CapabilitiesPath = scopeRel,
        }, null);
    }

    private TaskItem Task(string title = "Задача") =>
        _f.Tasks.Create(new TaskItem { Title = title }, "текст задания", "", null);

    private AgentToolset Toolset(TaskItem current, string? actorId, List<SecurityRule>? rules = null) =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, actorId),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: _f.Actions.CodeByTool);

    private static System.Text.Json.JsonElement Args(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    // ---------- 1. что считается перебивкой ----------

    [Fact]
    public void Messages_Written_Before_The_Job_Are_Not_An_Interrupt()
    {
        var task = Task();
        var human = Human();
        var agent = CreateAi("клод-1");
        _f.Chat.Add(task.Id, human.Id, null, "это было до начала работы");

        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);

        Assert.False(watch.HasNew);      // прежняя переписка — контекст задания, а не перебивка
        Assert.Empty(watch.Take());
    }

    [Fact]
    public void New_Human_Message_Is_Taken_Once()
    {
        var task = Task();
        var human = Human();
        var agent = CreateAi("клод-2");
        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);

        _f.Chat.Add(task.Id, human.Id, null, "проверь ещё и линукс");

        Assert.True(watch.HasNew);
        var fresh = Assert.Single(watch.Take());
        Assert.Equal("проверь ещё и линукс", fresh.Text);
        Assert.False(watch.HasNew);      // второй раз то же сообщение не доставляется
        Assert.Empty(watch.Take());
    }

    [Fact]
    public void Agent_Does_Not_Interrupt_Itself()
    {
        var task = Task();
        var agent = CreateAi("клод-3");
        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);

        // ответ самого агента в чат (маркер AI2P_CHAT) перебивкой не считается —
        // иначе агент перебивал бы сам себя бесконечно
        watch.Post("отвечаю: линукс проверю после сборки");

        Assert.False(watch.HasNew);
        Assert.Single(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public void Question_And_Answer_Of_v117_Are_Not_Interrupts()
    {
        // у вопросов агента своя пауза (job → waiting_human) и своё продолжение —
        // перебивкой они быть не должны, иначе ответ человека придёт агенту дважды
        var task = Task();
        var human = Human();
        var agent = CreateAi("клод-4");
        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);

        var job = _f.Jobs.Create(task.Id, agent.Id, task.DescriptionPath, null);
        var question = _f.Chat.AddQuestion(task.Id, agent.Id, job.Id, "Какая версия?", ["1.76"]);
        _f.Chat.Answer(question.Id, human.Id, "1.76");

        Assert.False(watch.HasNew);
    }

    [Fact]
    public void Delivery_Block_Names_Author_Text_And_What_To_Do()
    {
        var task = Task();
        var human = Human("михаил");
        var agent = CreateAi("клод-5");
        var watch = new ChatWatch(_f.Chat, _f.Executors, task.Id, agent.Id);
        _f.Chat.Add(task.Id, human.Id, null, "СТОП: не трогай ветку релиза");

        var block = watch.Block(watch.Take(), "вызови инструмент send_chat_message", interrupted: true);

        Assert.Contains("михаил", block);
        Assert.Contains("СТОП: не трогай ветку релиза", block);
        Assert.Contains("прервана", block);
        Assert.Contains("ОТВЕТЬ", block);
        Assert.Contains("send_chat_message", block);
        Assert.Contains("Ответ системы AI2P", block);   // это не человек-собеседник, а доставка
    }

    [Fact]
    public void Interrupt_Limit_Protects_From_Endless_Restarts()
    {
        var watch = new ChatWatch(_f.Chat, _f.Executors, Task().Id, CreateAi("клод-6").Id);

        for (var i = 0; i < ChatWatch.MaxInterrupts; i++)
        {
            Assert.False(watch.LimitReached);
            watch.CountInterrupt();
        }

        Assert.True(watch.LimitReached);   // дальше сообщения доставляются без прерывания
    }

    // ---------- 2. инструмент send_chat_message ----------

    [Fact]
    public async Task Agent_Answers_Into_Task_Chat()
    {
        var task = Task();
        var agent = CreateAi("клод-7");

        var answer = await Toolset(task, agent.Id).ExecuteAsync("send_chat_message",
            Args("""{"text":"Понял, линукс проверю после сборки"}"""), CancellationToken.None);

        Assert.Contains("отправлено", answer);
        var message = Assert.Single(_f.Chat.ListByTask(task.Id));
        Assert.Equal("Понял, линукс проверю после сборки", message.Text);
        Assert.Equal(agent.Id, message.FromExecutorId);
        Assert.Equal(ChatMessageKind.Message, message.Kind);
    }

    [Fact]
    public async Task Empty_Message_Is_Rejected()
    {
        var task = Task();
        var answer = await Toolset(task, CreateAi("клод-8").Id).ExecuteAsync("send_chat_message",
            Args("""{"text":"   "}"""), CancellationToken.None);

        Assert.StartsWith("Ошибка", answer);
        Assert.Empty(_f.Chat.ListByTask(task.Id));
    }

    [Fact]
    public void Tool_Is_Published_Only_When_There_Is_Someone_To_Write_From()
    {
        var task = Task();
        Assert.Contains(Toolset(task, CreateAi("клод-9").Id).Specs, s => s.Name == "send_chat_message");
        Assert.DoesNotContain(Toolset(task, null).Specs, s => s.Name == "send_chat_message");
    }

    [Fact]
    public async Task Security_Rule_Deny_Closes_The_Answer_Channel()
    {
        var task = Task();
        var agent = CreateAi("клод-10");
        var rules = new List<SecurityRule>
        {
            new()
            {
                Scope = SecurityScope.Global, Target = SecurityTarget.Action,
                Pattern = "AI2P.Chat.Send", Permission = SecurityPermission.Deny,
            },
        };
        var tools = Toolset(task, agent.Id, rules);

        var lines = await ClaudeCliConnector.PostChatAsync(
            [ """{"text":"молчать нельзя"}""" ], tools, CancellationToken.None);

        Assert.Contains("Запрещено правилами безопасности", Assert.Single(lines));
        Assert.Empty(_f.Chat.ListByTask(task.Id));   // сообщение не ушло
        Assert.Equal("", tools.CliChatNote(ChatMarker));   // и про маркер агенту не рассказали
    }

    // ---------- 3. маркер AI2P_CHAT в ответе CLI-агента ----------

    [Fact]
    public void Chat_Marker_Is_Cut_Out_Of_The_Answer()
    {
        var text = "Работаю дальше.\n"
                   + ChatMarker + " {\"text\": \"Линукс проверю после сборки\"}\n"
                   + "Продолжаю прогон.";

        var markers = ClaudeCliConnector.ParseMarkers(ChatMarker, text, out var cleaned);

        Assert.Contains("Линукс проверю", Assert.Single(markers));
        Assert.DoesNotContain(ChatMarker, cleaned);
        Assert.Contains("Продолжаю прогон.", cleaned);
    }

    [Fact]
    public void Cli_Prompt_Explains_The_Interrupt_And_The_Marker()
    {
        var note = TaskToolset.ChatMarkerNote(ChatMarker);

        Assert.Contains("ВО ВРЕМЯ работы", note);
        Assert.Contains("перебивкой", note);
        Assert.Contains(ChatMarker, note);
        Assert.Contains("Молчать в ответ", note);
    }

    // ---------- 4. сквозной прогон задания через поддельный CLI ----------

    [Fact]
    public async Task Message_During_Work_Interrupts_Cli_And_Gets_An_Answer()
    {
        // первый запуск «работает» полминуты — за это время человек пишет в чат;
        // коннектор обязан остановить процесс и продолжить ТУ ЖЕ сессию его словами
        var (job, cliDir, task, human) = await RunCliJobAsync("cli-interrupt", sleepSeconds: 25,
            first: "не должно понадобиться — процесс будет прерван",
            second: "Готово: тесты 962/962, линукс проверен.\n"
                    + ChatMarker + " {\"text\": \"Проверил и линукс — всё зелено\"}",
            message: "проверь ещё и линукс, это важно");

        Assert.Equal(JobState.Done, job.State);

        // второй ход той же сессии получил слова человека и объяснение, что работа прервана
        var second = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt2.txt"));
        Assert.Contains("проверь ещё и линукс", second);
        Assert.Contains("прервана", second);
        Assert.Contains(ChatMarker, second);

        // сессия продолжена, а не начата заново: первый запуск задал свой id, второй его возобновил
        var args1 = await File.ReadAllTextAsync(Path.Combine(cliDir, "args1.txt"));
        var args2 = await File.ReadAllTextAsync(Path.Combine(cliDir, "args2.txt"));
        Assert.Contains("--session-id", args1);
        Assert.Contains("--resume", args2);

        // ответ агента ушёл человеку в чат, а результатом задания стал ВТОРОЙ ответ
        var chat = _f.Chat.ListByTask(task.Id);
        Assert.Equal(2, chat.Count);
        Assert.Equal(human.Id, chat[0].FromExecutorId);
        Assert.Equal("Проверил и линукс — всё зелено", chat[1].Text);
        var result = _f.Files.ReadText(job.ResultPath!);
        Assert.Contains("962/962", result);
        Assert.DoesNotContain(ChatMarker, result);   // маркер в результат не попадает
    }

    [Fact]
    public async Task Message_At_The_End_Of_A_Turn_Is_Delivered_Before_The_Job_Closes()
    {
        // сообщение пришло, когда прерывать уже нечего (агент вот-вот ответит): оно всё равно
        // доходит — ещё одним ходом той же сессии, до закрытия задания
        var (job, cliDir, task, _) = await RunCliJobAsync("cli-tail", sleepSeconds: 3,
            first: "Промежуточный итог: тесты прогнаны.",
            second: "Готово: тесты прогнаны, отчёт дописан.\n"
                    + ChatMarker + " {\"text\": \"Отчёт дописал, как просили\"}",
            message: "допиши в отчёт раздел «что не сделано»");

        Assert.Equal(JobState.Done, job.State);
        var second = await File.ReadAllTextAsync(Path.Combine(cliDir, "prompt2.txt"));
        Assert.Contains("что не сделано", second);
        Assert.DoesNotContain("прервана", second);   // прерывать было нечего — это не перебивка
        Assert.Contains("Отчёт дописал, как просили", _f.Chat.ListByTask(task.Id)[^1].Text);
        Assert.Contains("отчёт дописан", _f.Files.ReadText(job.ResultPath!));
    }

    /// <summary>
    /// Поддельный CLI: первый запуск сохраняет промпт и аргументы, «работает»
    /// <paramref name="sleepSeconds"/> секунд и печатает out1.json; второй и третий отвечают
    /// сразу out2.json. Свой процесс вместо настоящего Claude Code — внешние процессы
    /// в тестах источник флаки (и денег подписки).
    /// </summary>
    private string FakeCli(string dirName, int sleepSeconds, string out1Json, string out2Json)
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
                "if exist \"%~dp0prompt2.txt\" goto third\r\n" +
                "if exist \"%~dp0prompt1.txt\" goto second\r\n" +
                "echo %* > \"%~dp0args1.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt1.txt\"\r\n" +
                $"ping -n {sleepSeconds + 1} 127.0.0.1 > nul\r\n" +
                "type \"%~dp0out1.json\"\r\n" +
                "exit /b 0\r\n" +
                ":second\r\n" +
                "echo %* > \"%~dp0args2.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt2.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n" +
                ":third\r\n" +
                "echo %* > \"%~dp0args3.txt\"\r\n" +
                "findstr \"^\" > \"%~dp0prompt3.txt\"\r\n" +
                "type \"%~dp0out2.json\"\r\n" +
                "exit /b 0\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh,
            "#!/bin/sh\n" +
            "d=\"$(dirname \"$0\")\"\n" +
            "if [ -f \"$d/prompt2.txt\" ]; then echo \"$@\" > \"$d/args3.txt\"; " +
            "cat > \"$d/prompt3.txt\"; cat \"$d/out2.json\";\n" +
            "elif [ -f \"$d/prompt1.txt\" ]; then echo \"$@\" > \"$d/args2.txt\"; " +
            "cat > \"$d/prompt2.txt\"; cat \"$d/out2.json\";\n" +
            $"else echo \"$@\" > \"$d/args1.txt\"; cat > \"$d/prompt1.txt\"; sleep {sleepSeconds}; " +
            "cat \"$d/out1.json\"; fi\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static string CliJson(string answer, string sessionId) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = sessionId,
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    /// <summary>
    /// Задание ИИ-исполнителя на поддельном CLI: запускаем, дожидаемся старта процесса
    /// (появился prompt1.txt) и пишем в чат от имени человека — ровно так, как это делает
    /// человек из интерфейса, пока агент работает.
    /// </summary>
    private async Task<(Job Job, string CliDir, TaskItem Task, Executor Human)> RunCliJobAsync(
        string dirName, int sleepSeconds, string first, string second, string message)
    {
        var project = _f.Projects.Create("Проект " + dirName, null, null, null);
        var ai = CreateAi("клод-" + dirName);
        var human = Human("михаил-" + dirName);
        // id сессии поддельного CLI совпадать с нашим --session-id не обязан: продолжение
        // после прерывания идёт по НАШЕМУ id, после обычного хода — по тому, что вернул CLI
        var cliPath = FakeCli(dirName, sleepSeconds,
            CliJson(first, "s-161"), CliJson(second, "s-161"));
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
            ProjectId = project.Id, Title = "Прогон тестов", ExecutorIds = [ai.Id],
        }, "прогони тесты", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "прогони тесты");

        var cliDir = Path.GetDirectoryName(cliPath)!;
        for (var i = 0; i < 100 && !File.Exists(Path.Combine(cliDir, "prompt1.txt")); i++)
        {
            await System.Threading.Tasks.Task.Delay(100);
        }
        _f.Chat.Add(task.Id, human.Id, null, message);

        return (await WaitForJob(job.Id), cliDir, task, human);
    }

    /// <summary>Дождаться завершения фонового задания (ТЗ п. 3, принцип 4). Ждём дольше
    /// обычного: прерывание работы наступает не раньше <see cref="ChatWatch.InterruptGrace"/>.</summary>
    private async Task<Job> WaitForJob(string jobId)
    {
        for (var i = 0; i < 900; i++)
        {
            var job = _f.Jobs.Get(jobId)!;
            if (job.State is not (JobState.Queued or JobState.Running))
            {
                return job;
            }
            await System.Threading.Tasks.Task.Delay(100);
        }
        throw new TimeoutException($"Задание {jobId} не завершилось за 90 с");
    }
}
