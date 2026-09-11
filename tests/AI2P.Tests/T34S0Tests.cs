using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-34-S0 «AI2P_cli: действия AI2P вызовом, а не маркером».
///
/// До этой задачи CLI-агент не мог позвать ни одно действие AI2P напрямую: /api закрыто
/// входом по cookie, реквизитов у агента нет. Всё шло МАРКЕРАМИ в тексте ответа, а маркер —
/// это не вызов, а конец хода (процесс claude завершается, сессия продолжается через
/// --resume). Один ход по телеметрии T-288 стоит около 160 тыс. токенов чтения контекста —
/// отсюда и пределы «10 заданий / 20 файлов» за задание.
///
/// Здесь проверяется всё, что можно проверить без стенда: токен задания и его срок жизни,
/// новые действия (подзадачи, состояние чужой задачи), правила безопасности на них, формы
/// маркера-запаски и то, что коннектор действительно передаёт агенту реквизиты и разрешение
/// на запуск команды. Живая проверка — test/t34s0/live34s0.py.
/// </summary>
public sealed class T34S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose()
    {
        _f.Dispose();
        AgentSessionRegistry.LocalBaseUrl = "";
    }

    private const string StatusMarker = ClaudeCliConnector.StatusMarker;
    private const string TaskMarker = ClaudeCliConnector.TaskMarker;

    private TaskItem CreateTask(string title, string? parentId = null, string? executorId = null,
        string status = TaskStatuses.Pending) =>
        _f.Tasks.Create(new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = status,
            ExecutorIds = executorId is null ? [] : [executorId],
        }, Statement, "", null);

    /// <summary>Формулировка посеянных задач — нарочно приметная: список подзадач её содержать
    /// не должен (формулировок в нём нет намеренно).</summary>
    private const string Statement = "формулировка-НЕ-должна-попадать-в-список";

    /// <summary>Набор инструментов задания; actor — исполнитель, от чьего имени идут действия.</summary>
    private AgentToolset Toolset(TaskItem current, List<SecurityRule>? rules = null,
        string? actor = "exec-1") =>
        new(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, current,
                _f.RefData, _f.Picker, actor),
            security: rules is null ? null : new SecurityEvaluator(rules, null),
            actionCodeByTool: _f.Actions.CodeByTool);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static List<SecurityRule> Deny(string action) =>
    [
        new()
        {
            Scope = SecurityScope.Global, Target = SecurityTarget.Action,
            Pattern = action, Permission = SecurityPermission.Deny,
        },
    ];

    // ---------- 1. токен задания: жив, пока идёт задание ----------

    [Fact]
    public void A_Job_Token_Resolves_While_The_Job_Runs_And_Dies_With_It()
    {
        var task = CreateTask("текущая");
        var tools = Toolset(task);

        var session = AgentSessionRegistry.Register("job-1", "J-1", task.Id, task.DisplayId,
            "exec-1", tools);

        Assert.NotEqual("", session.Token);
        Assert.Same(session, AgentSessionRegistry.Resolve(session.Token));
        // личность вызова — ИСПОЛНИТЕЛЬ задания, а не человек: правила безопасности
        // считаются по паре задача+исполнитель (T-117), и считать их иначе не по чему
        Assert.Equal("exec-1", session.ExecutorId);
        Assert.Equal(task.DisplayId, session.TaskCode);
        // тот же набор инструментов, которым работает само задание — логика действий одна
        Assert.Same(tools, session.Tools);

        AgentSessionRegistry.Release(session);
        Assert.Null(AgentSessionRegistry.Resolve(session.Token));
    }

    [Fact]
    public void An_Unknown_Or_Empty_Token_Resolves_To_Nothing()
    {
        Assert.Null(AgentSessionRegistry.Resolve(null));
        Assert.Null(AgentSessionRegistry.Resolve(""));
        Assert.Null(AgentSessionRegistry.Resolve("не-наш-токен"));
    }

    [Fact]
    public void A_Call_From_Another_Address_Is_Not_Local_And_So_Is_Refused()
    {
        // токен живёт в памяти процесса и надёжен ровно настолько, насколько недоступен
        // снаружи: раздел /api/agent спрашивает Ai2pAuth.IsLocalRequest ДО токена
        var foreign = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        foreign.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");
        foreign.Connection.LocalIpAddress = System.Net.IPAddress.Parse("192.168.1.5");
        Assert.False(AI2P.Server.Api.Ai2pAuth.IsLocalRequest(foreign));

        var local = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        local.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        local.Connection.LocalIpAddress = System.Net.IPAddress.Loopback;
        Assert.True(AI2P.Server.Api.Ai2pAuth.IsLocalRequest(local));

        Assert.Equal("X-AI2P-Job", AI2P.Server.Api.AgentEndpoints.TokenHeader);
    }

    // ---------- 2. реквизиты и разрешение уходят процессу CLI ----------

    [Fact]
    public void Without_A_Session_The_Cli_Client_Is_Not_Offered_At_All()
    {
        var task = CreateTask("текущая");
        var tools = Toolset(task);

        // сессии нет — окружения нет, дописки в промпт нет: всё работает по-старому, маркерами
        Assert.Null(ClaudeCliConnector.AgentProcessEnv(tools));
        Assert.Equal("", ClaudeCliConnector.CliClientNote(tools));
    }

    [Fact]
    public void The_Agent_Process_Gets_The_Url_And_The_Job_Token()
    {
        var task = CreateTask("текущая");
        var tools = Toolset(task);
        tools.Language = "ru";
        var session = AgentSessionRegistry.Register("job-1", "J-1", task.Id, task.DisplayId,
            "exec-1", tools);
        tools.Session = session;
        AgentSessionRegistry.LocalBaseUrl = "http://localhost:5480/ai2p";
        var wrapper = Path.Combine(AppContext.BaseDirectory, AgentCli.WrapperName);
        var made = false;
        try
        {
            if (!File.Exists(wrapper))
            {
                // в каталоге сборки тестов выкладки нет — обёртку кладём сами: без файла
                // клиент заданию не предлагается вовсе (и это отдельно проверено выше)
                File.WriteAllText(wrapper, "echo ai2p");
                made = true;
            }
            var env = ClaudeCliConnector.AgentProcessEnv(tools);

            Assert.NotNull(env);
            Assert.Equal("http://localhost:5480/ai2p", env![AgentCli.UrlVar]);
            Assert.Equal(session.Token, env[AgentCli.TokenVar]);
            Assert.Equal(task.DisplayId, env[AgentCli.TaskVar]);
            Assert.Equal("ru", env[AgentCli.LangVar]);
            // и агенту рассказано, чем звать действия
            Assert.Contains("help", ClaudeCliConnector.CliClientNote(tools));
        }
        finally
        {
            AgentSessionRegistry.Release(session);
            if (made)
            {
                File.Delete(wrapper);
            }
        }
    }

    [Fact]
    public void The_Launch_Permission_Covers_Every_Way_The_Agent_May_Write_The_Command()
    {
        // текст промпта песочницу Claude Code НЕ расширяет (наука T-117): разрешение
        // передаётся флагом --allowedTools, а правило там префиксное
        var patterns = AgentCli.AllowedToolPatterns(@"C:\Program Files\AI2P\ai2p.cmd");

        Assert.Contains(@"Bash(C:\Program Files\AI2P\ai2p.cmd:*)", patterns);
        // путь с пробелом агент напишет в кавычках — начало команды будет другим
        Assert.Contains("Bash(\"C:\\Program Files\\AI2P\\ai2p.cmd\":*)", patterns);
        // и короткое имя — на случай, если каталог установки оказался в PATH
        Assert.Contains("Bash(ai2p:*)", patterns);
        Assert.Contains("Bash(ai2p.cmd:*)", patterns);
        Assert.All(patterns, p => Assert.StartsWith("Bash(", p));
    }

    [Fact]
    public void The_Wrapper_Lives_Next_To_The_Program()
    {
        // обёртка кладётся и в выходной каталог сборки, и в выкладку (csproj), иначе
        // в установке её не окажется и звать агенту будет нечего
        var repo = RepoRoot();
        Assert.True(File.Exists(Path.Combine(repo, "AI2P_app", "ai2p.cmd")));
        Assert.True(File.Exists(Path.Combine(repo, "AI2P_app", "ai2p")));
        var csproj = File.ReadAllText(Path.Combine(repo, "AI2P_app", "src", "AI2P.Server",
            "AI2P.Server.csproj"));
        Assert.Contains("ai2p.cmd", csproj);
        Assert.Contains("CopyToPublishDirectory", csproj);
        // и установка на Linux/macOS обязана дать обёртке право на запуск
        var installSh = File.ReadAllText(Path.Combine(repo, "AI2P_app", "install.sh"));
        Assert.Contains("chmod +x \"$TARGET_DIR/ai2p\"", installSh);
    }

    [Fact]
    public void The_Windows_Wrapper_Holds_No_Non_Ascii_Characters()
    {
        // ПОЙМАНО ЖИВОЙ ПРОВЕРКОЙ. cmd.exe декодирует .cmd в кодировке консоли, а позицию
        // чтения держит в БАЙТАХ: многобайтовый символ сдвигает позицию, cmd продолжает
        // разбор с середины следующей строки и печатает её хвост в stdout вместе с «… is not
        // recognized as an internal or external command». Этот stdout читает АГЕНТ — ответ
        // приезжал бы с мусором, приклеенным спереди
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "AI2P_app", "ai2p.cmd"));
        Assert.All(bytes, b => Assert.True(b < 0x80, $"не-ASCII байт 0x{b:X2} в ai2p.cmd"));
    }

    // ---------- 3. новое действие: подзадачи ----------

    [Fact]
    public async Task Child_Tasks_Are_Listed_Without_Their_Statements()
    {
        var root = CreateTask("выпуск");
        var first = CreateTask("первая подзадача", root.Id, status: TaskStatuses.Done);
        var second = CreateTask("вторая подзадача", root.Id);
        var grand = CreateTask("внучка", first.Id);
        var current = CreateTask("прогон тестов", root.Id);

        var answer = await Toolset(current).ExecuteAsync("get_child_tasks",
            Args($$"""{"code": "{{_f.Tasks.Get(root.Id)!.DisplayId}}"}"""), CancellationToken.None);

        Assert.Contains(_f.Tasks.Get(first.Id)!.DisplayId, answer);
        Assert.Contains(_f.Tasks.Get(second.Id)!.DisplayId, answer);
        Assert.Contains(TaskStatuses.Done, answer);
        // формулировок в списке нет намеренно: восемь полных карточек — это десятки КБ
        Assert.DoesNotContain(Statement, answer);
        // глубина по умолчанию 1 — внучка в список не попала
        Assert.DoesNotContain(_f.Tasks.Get(grand.Id)!.DisplayId, answer);

        var deep = await Toolset(current).ExecuteAsync("get_child_tasks",
            Args($$"""{"code": "{{_f.Tasks.Get(root.Id)!.DisplayId}}", "depth": 2}"""),
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(grand.Id)!.DisplayId, deep);
    }

    [Fact]
    public async Task Child_Tasks_Without_A_Code_Are_Your_Own_And_An_Empty_Branch_Is_A_Normal_Answer()
    {
        var current = CreateTask("родитель");
        var child = CreateTask("подзадача", current.Id);
        var lonely = CreateTask("одиночка");

        var mine = await Toolset(current).ExecuteAsync("get_child_tasks", Args("{}"),
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(child.Id)!.DisplayId, mine);

        var empty = await Toolset(lonely).ExecuteAsync("get_child_tasks", Args("{}"),
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(lonely.Id)!.DisplayId, empty);
        Assert.DoesNotContain(_f.Tasks.Get(child.Id)!.DisplayId, empty);
    }

    // ---------- 4. новое действие: состояние чужой задачи ----------

    [Fact]
    public async Task Another_Task_Is_Sent_Back_To_Work_With_The_Restart_Flags_Cleared()
    {
        var current = CreateTask("прогон тестов");
        var broken = CreateTask("сломанная", status: TaskStatuses.Review);
        // очередь уже пробовала перезапускать её после ошибки (T-186) и после доработки (T-210),
        // и у задачи стоит отложенный старт — без снятия всего этого перезапуск не состоится
        _f.Tasks.SetLaunchFlag(broken.Id, TaskService.HierarchyRetryFlag, true);
        _f.Tasks.SetLaunchFlag(broken.Id, TaskService.HierarchyNeedsFixRetryFlag, true);
        _f.Tasks.SetStartAfter(broken.Id, DateTime.UtcNow.AddHours(3));

        var answer = await Toolset(current).ExecuteAsync("set_task_status",
            Args($$"""
                {"code": "{{_f.Tasks.Get(broken.Id)!.DisplayId}}",
                 "status": "needs_fix", "restart": true}
                """), CancellationToken.None);

        var after = _f.Tasks.Get(broken.Id)!;
        Assert.Equal(TaskStatuses.NeedsFix, after.Status);
        Assert.False(TaskService.HasLaunchFlag(after.LaunchJson, TaskService.HierarchyRetryFlag));
        Assert.False(TaskService.HasLaunchFlag(after.LaunchJson, TaskService.HierarchyNeedsFixRetryFlag));
        Assert.Null(TaskService.LaunchStartAfter(after.LaunchJson));
        // без режима «auto» задача вне открытой очереди иерархии не тронулась бы с места
        Assert.Contains("\"mode\":\"auto\"", after.LaunchJson.Replace(" ", ""));
        Assert.Contains(after.DisplayId, answer);
    }

    [Fact]
    public async Task You_Cannot_Set_The_Status_Of_Your_Own_Task_Or_An_Unknown_Status()
    {
        var current = CreateTask("прогон тестов");
        var tools = Toolset(current);

        var self = await tools.ExecuteAsync("set_task_status",
            Args($$"""{"code": "{{_f.Tasks.Get(current.Id)!.DisplayId}}", "status": "done"}"""),
            CancellationToken.None);
        // своё состояние ставит система по итогу задания (T-250) — иначе гонка с коннектором
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(current.Id)!.Status);
        Assert.Contains(Loc.T("prompt.tasks.129"), self);

        var other = CreateTask("чужая");
        var bad = await tools.ExecuteAsync("set_task_status",
            Args($$"""{"code": "{{_f.Tasks.Get(other.Id)!.DisplayId}}", "status": "выполнено"}"""),
            CancellationToken.None);
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(other.Id)!.Status);
        Assert.Contains(TaskStatuses.Done, bad); // в отказе перечислены доступные коды
    }

    // ---------- 5. правила безопасности действуют на оба пути ----------

    [Fact]
    public async Task A_Security_Rule_Closes_The_New_Actions_Both_Ways()
    {
        var current = CreateTask("прогон тестов");
        var other = CreateTask("чужая");
        var code = _f.Tasks.Get(other.Id)!.DisplayId;
        var tools = Toolset(current, Deny("AI2P.Tasks.SetStatus"));

        // 1) вызов (клиент командной строки идёт этим же путём: Authorize → ExecuteAsync)
        var check = tools.Authorize("set_task_status", Args($$"""{"code": "{{code}}"}"""));
        Assert.Equal(SecurityDecision.Deny, check.Decision);
        Assert.Contains("AI2P.Tasks.SetStatus", check.Message);

        // 2) маркер — тот же Authorize
        var summary = await ClaudeCliConnector.ApplyMarkersAsync(
            [$$"""{"code": "{{code}}", "status": "done"}"""], tools,
            "## Смена состояния", _ => "set_task_status", CancellationToken.None);
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(other.Id)!.Status);
        Assert.Contains("AI2P.Tasks.SetStatus", summary);
        // про маркер такому агенту не рассказывают вовсе
        Assert.Equal("", tools.CliStatusNote(StatusMarker));
        Assert.NotEqual("", Toolset(current).CliStatusNote(StatusMarker));
    }

    [Fact]
    public void The_New_Actions_Are_In_The_Catalog_In_Both_Languages()
    {
        // без записи справочника правила безопасности инструмент не закрывают вовсе
        // (CodeByTool вернул бы null, а Authorize — «разрешено», наука выпуска 1.80)
        foreach (var (code, tool) in new[]
                 {
                     ("AI2P.Tasks.GetChildren", "get_child_tasks"),
                     ("AI2P.Tasks.SetStatus", "set_task_status"),
                 })
        {
            foreach (var lang in new[] { "ru", "en" })
            {
                var action = _f.Actions.List(lang).Single(a => a.Code == code);
                Assert.Equal(tool, action.ToolName);
                Assert.False(string.IsNullOrWhiteSpace(action.Prompt));
                Assert.False(string.IsNullOrWhiteSpace(action.Hint));
            }
            Assert.Equal(code, _f.Actions.CodeByTool(tool));
        }
    }

    // ---------- 6. маркеры остались запасным путём ----------

    [Fact]
    public async Task The_Status_Marker_Still_Works()
    {
        var current = CreateTask("прогон тестов");
        var broken = CreateTask("сломанная", status: TaskStatuses.Review);
        var text = "Тесты упали.\n\n" + StatusMarker
                   + $" {{\"code\": \"{_f.Tasks.Get(broken.Id)!.DisplayId}\", "
                   + "\"status\": \"needs_fix\", \"restart\": true}\n\nОтчёт готов.";

        var markers = ClaudeCliConnector.ParseMarkers(StatusMarker, text, out var cleaned);
        var summary = await ClaudeCliConnector.ApplyMarkersAsync(markers, Toolset(current),
            "## Смена состояния", _ => "set_task_status", CancellationToken.None);

        Assert.Single(markers);
        Assert.DoesNotContain(StatusMarker, cleaned);
        Assert.Contains("Тесты упали.", cleaned);
        Assert.Equal(TaskStatuses.NeedsFix, _f.Tasks.Get(broken.Id)!.Status);
        Assert.Contains(_f.Tasks.Get(broken.Id)!.DisplayId, summary);
    }

    [Fact]
    public async Task The_Read_Marker_Also_Gives_The_Parent_The_Siblings_And_The_Children()
    {
        // после T-29-S0 блоки родителя и соседей в промпт не подставляются, а КОДА РОДИТЕЛЯ
        // агент не знает ниоткуда, кроме самого блока: без этих форм он остался бы без
        // родителя навсегда. Формы живут внутри маркера чтения заданий — один общий предел
        var root = CreateTask("родитель");
        var neighbour = CreateTask("сосед", root.Id);
        var current = CreateTask("текущая", root.Id);
        var child = CreateTask("подзадача", current.Id);
        var tools = Toolset(current);

        var parent = await ClaudeCliConnector.ReadTasksAsync(["{\"parent\": true}"], tools,
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(root.Id)!.DisplayId, parent);

        var siblings = await ClaudeCliConnector.ReadTasksAsync(["{\"siblings\": true}"], tools,
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(neighbour.Id)!.DisplayId, siblings);

        var children = await ClaudeCliConnector.ReadTasksAsync(["{\"children\": \"\"}"], tools,
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(child.Id)!.DisplayId, children);

        var named = await ClaudeCliConnector.ReadTasksAsync(
            [$"{{\"children\": \"{_f.Tasks.Get(root.Id)!.DisplayId}\"}}"], tools,
            CancellationToken.None);
        Assert.Contains(_f.Tasks.Get(neighbour.Id)!.DisplayId, named);
    }

    [Fact]
    public void The_Move_Marker_Note_No_Longer_Shows_Doubled_Braces()
    {
        // Loc форматирует текст ТОЛЬКО когда переданы аргументы, а дописка про перенос
        // зовётся без них — агент видел «{{"code": …}}» и повторял удвоенные скобки
        var current = CreateTask("текущая");
        var note = Toolset(current).CliMoveNote(ClaudeCliConnector.MoveTaskMarker);

        Assert.Contains("{\"code\"", note);
        Assert.DoesNotContain("{{\"code\"", note);
    }

    [Fact]
    public void Questions_And_Waiting_Stay_Markers()
    {
        // это не вызовы, а пауза задания на часы: синхронная команда столько не проживёт
        // и упрётся в таймаут исполнителя
        Assert.DoesNotContain("ask_question", TaskToolset.Specs.Select(s => s.Name));
        Assert.Contains("wait_task_reply", TaskToolset.Specs.Select(s => s.Name));
        Assert.Equal("AI2P_QUESTION:", ClaudeCliConnector.QuestionMarker);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "AI2P_app")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }
}
