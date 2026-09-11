using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-250 «Завершение задач ИИ» (версия 1.90): СТАТУС ПО ГОТОВНОСТИ.
///
/// Что было. Нормально завершив задачу, ИИ-агент всегда переводил её в «проверку» — код
/// состояния был вписан в коннектор. Человеку приходилось руками менять «проверку» на
/// «готово», иначе задачи, которые эта блокировала, не запускались: отпускает ждущих только
/// «готово» (T-6-S1).
///
/// Что стало. У задачи появилось своё поле — в какое состояние её переводит нормально
/// завершившийся агент. По умолчанию это «проверка» (прежнее поведение), в форме задачи
/// с исполнителем-ИИ значение можно сменить на «готово» — и тогда завершение задачи само
/// отпускает заблокированные ею задачи, в том числе при иерархическом запуске.
///
/// <list type="number">
/// <item>умолчание — «проверка»: у новой задачи, у задачи прошлой версии (строка без
/// колонки) и у пустого значения;</item>
/// <item>значение хранится, читается обратно и правится;</item>
/// <item>нормально завершённое задание ИИ переводит задачу именно в него — и в «проверку»
/// по умолчанию, и в «готово», когда так задано;</item>
/// <item>«готово» от агента отпускает задачи, которые эта блокировала, а «проверка» — нет;</item>
/// <item>ошибка агента статуса по готовности не читает вовсе: она по-прежнему даёт
/// «встал с ошибкой»;</item>
/// <item>поле переезжает из узла шаблона в созданную из него задачу.</item>
/// </list>
/// </summary>
public sealed class T250Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- обвязка ----------

    private Executor CreateAi(string nick, string cliJson)
    {
        var scopeRel = $"models/scope_{nick}.json";
        _f.Files.WriteText(scopeRel, """{ "skills": [ { "name": "code-write", "score": 90 } ] }""");
        var profileRel = $"models/profile_{nick}.json";
        _f.Files.WriteText(profileRel, $$"""
            {
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": {{System.Text.Json.JsonSerializer.Serialize($"\"{FakeCli(nick, cliJson)}\"")}},
              "model": "claude-fable-5",
              "secretRef": ""
            }
            """);
        return _f.Executors.Create(new Executor
        {
            Nick = nick,
            Kind = ExecutorKind.Ai,
            CapabilitiesPath = scopeRel,
            ProfilePath = profileRel,
        }, null);
    }

    private Executor CreateHuman(string nick) =>
        _f.Executors.Create(new Executor { Nick = nick, Kind = ExecutorKind.Human }, null);

    /// <summary>
    /// Поддельный CLI (приём CliSubtaskTests): собственный процесс вместо настоящего Claude
    /// Code — внешние процессы в тестах дают флаки, свой скрипт детерминирован. Вычитывает
    /// stdin (промпт), печатает готовый JSON headless-запуска и завершается. Каталог у
    /// каждого исполнителя свой: в одном тесте их бывает двое.
    /// </summary>
    private string FakeCli(string nick, string cliJson)
    {
        var dir = Path.Combine(_f.Dir, "fake-cli-" + nick);
        Directory.CreateDirectory(dir);
        var outPath = Path.Combine(dir, "out.json");
        File.WriteAllText(outPath, cliJson, new System.Text.UTF8Encoding(false));
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd, "@echo off\r\nfindstr \"^\" > nul\r\ntype \"%~dp0out.json\"\r\n");
            return cmd;
        }
        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh, "#!/bin/sh\ncat > /dev/null\ncat \"$(dirname \"$0\")/out.json\"\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    /// <summary>Удачный ответ CLI: обычный текст без маркеров — работа сделана целиком,
    /// вниз на подзадачи она не уходила (иначе задача ушла бы в «паузу», T-185).</summary>
    private static string SuccessJson(string answer) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            session_id = "s-250",
            result = answer,
            usage = new { input_tokens = 10, output_tokens = 20 },
        });

    /// <summary>Отказ CLI, не имеющий отношения к лимиту подписки (иначе задача ушла бы
    /// в «паузу» ждать сброса окна, T-166).</summary>
    private static string FailureJson() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "error",
            is_error = true,
            session_id = "s-250",
            result = "API Error: internal",
            api_error_status = 500,
        });

    private async Task<Job> RunJobAsync(TaskItem task, Executor ai)
    {
        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        await _f.Connectors.Resolve(ai).SubmitJobAsync(job, "сделай работу");
        for (var i = 0; i < 300; i++)
        {
            var current = _f.Jobs.Get(job.Id)!;
            if (current.State is not (JobState.Queued or JobState.Running))
            {
                return current;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Задание {job.Id} не завершилось за 30 с");
    }

    /// <summary>
    /// Дождаться ожидаемого статуса ЗАДАЧИ. Состояние задания коннектор ставит строкой раньше
    /// статуса задачи (AiConnectorBase: сначала SetState, потом ChangeStatus), поэтому
    /// дождавшийся задания тест успевает прочитать ещё прежний статус — под нагрузкой полного
    /// прогона это давало красноту на ровном месте (T-203).
    /// </summary>
    private async Task<string> WaitForTaskStatus(string taskId, string expected)
    {
        for (var i = 0; i < 200; i++)
        {
            var status = _f.Tasks.Get(taskId)!.Status;
            if (status == expected)
            {
                return status;
            }
            await Task.Delay(50);
        }
        return _f.Tasks.Get(taskId)!.Status;
    }

    /// <summary>Автозапуски идут фоном (Task.Run в JobOrchestrator): ждём результата,
    /// а не читаем сразу — иначе проверка меряет состояние до срабатывания.</summary>
    private async Task<bool> WaitFor(Func<bool> condition, int seconds = 10)
    {
        for (var i = 0; i < seconds * 20; i++)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(50);
        }
        return condition();
    }

    // ---------- 1. умолчание ----------

    [Fact]
    public void New_Task_Goes_To_Review_By_Default()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "Без выбора" }, "текст", "", null);

        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.AiDoneStatus);
    }

    [Fact]
    public void Task_Of_The_Previous_Version_Reads_As_Review()
    {
        // строка задачи, записанная БЕЗ новой колонки (как её писала версия 1.89): читается
        // как «проверка» — умолчание колонки, а не пустое значение. Именно поэтому шага
        // обновления данных этой правке не потребовалось
        using var conn = _f.Db.Open();
        var now = Sql.ToDb(DateTime.UtcNow);
        Sql.Exec(conn, null, """
            INSERT INTO tasks (id, display_id, kind, title, description_path, status,
                               priority, priority_num, launch_json, acceptance_path,
                               created_at, updated_at)
            VALUES ('old-1', 'T-900', 'task', 'Задача прошлой версии', '', 'pending',
                    1, 15, '{"mode":"manual"}', '', @now, @now)
            """, ("@now", now));

        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get("old-1")!.AiDoneStatus);
    }

    [Fact]
    public void Empty_Value_Means_Review()
    {
        // пусто хранится ровно одним значением: иначе каждое чтение обязано было бы помнить
        // про умолчание, а забытое место оставило бы задачу без статуса вовсе
        Assert.Equal(TaskStatuses.Review, TaskService.NormalizeAiDoneStatus(null));
        Assert.Equal(TaskStatuses.Review, TaskService.NormalizeAiDoneStatus(""));
        Assert.Equal(TaskStatuses.Review, TaskService.NormalizeAiDoneStatus("   "));
        Assert.Equal(TaskStatuses.Done, TaskService.NormalizeAiDoneStatus("  done "));

        var task = _f.Tasks.Create(new TaskItem { Title = "Пустое поле", AiDoneStatus = "  " },
            "текст", "", null);
        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.AiDoneStatus);
    }

    // ---------- 2. хранение и правка ----------

    [Fact]
    public void Value_Is_Stored_And_Changed()
    {
        var task = _f.Tasks.Create(
            new TaskItem { Title = "Готово без проверки", AiDoneStatus = TaskStatuses.Done },
            "текст", "", null);
        Assert.Equal(TaskStatuses.Done, _f.Tasks.Get(task.Id)!.AiDoneStatus);
        // AiDoneStatusOf читает то же самое, но напрямую из базы — им пользуется коннектор
        Assert.Equal(TaskStatuses.Done, _f.Tasks.AiDoneStatusOf(task.Id));

        var edited = _f.Tasks.Get(task.Id)!;
        edited.AiDoneStatus = TaskStatuses.Review;
        _f.Tasks.Update(edited, "текст", "", null);

        Assert.Equal(TaskStatuses.Review, _f.Tasks.Get(task.Id)!.AiDoneStatus);
        // статус САМОЙ задачи полем не трогается: это разные вещи
        Assert.Equal(TaskStatuses.Draft, _f.Tasks.Get(task.Id)!.Status);
    }

    [Fact]
    public void Custom_Status_Of_The_Reference_Book_Is_Allowed()
    {
        // справочник состояний пополняется пользователем (ТЗ v1.37), и код по готовности
        // со справочником не сверяется намеренно: задача приезжает и репликацией — с сервера,
        // где нужное состояние уже заведено, а здесь ещё нет
        var task = _f.Tasks.Create(new TaskItem { Title = "Своё состояние", AiDoneStatus = "accepted" },
            "текст", "", null);

        Assert.Equal("accepted", _f.Tasks.Get(task.Id)!.AiDoneStatus);
    }

    // ---------- 3. завершение задания ИИ ----------

    [Fact]
    public async Task Finished_Agent_Puts_The_Task_Into_Review_By_Default()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-проверка", SuccessJson("Сделано."));
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Обычная", ExecutorIds = [ai.Id],
        }, "сделай работу", "", null);

        var job = await RunJobAsync(task, ai);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(TaskStatuses.Review, await WaitForTaskStatus(task.Id, TaskStatuses.Review));
    }

    [Fact]
    public async Task Finished_Agent_Puts_The_Task_Into_The_Chosen_Status()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-готово", SuccessJson("Сделано."));
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Проверять нечего", ExecutorIds = [ai.Id],
            AiDoneStatus = TaskStatuses.Done,
        }, "сделай работу", "", null);

        var job = await RunJobAsync(task, ai);

        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(TaskStatuses.Done, await WaitForTaskStatus(task.Id, TaskStatuses.Done));
    }

    [Fact]
    public async Task Value_Changed_While_The_Agent_Worked_Is_Taken_Into_Account()
    {
        // задание идёт часами, и человек вполне мог поправить поле уже после старта агента —
        // коннектор обязан читать его заново, а не из снимка задачи
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-правка", SuccessJson("Сделано."));
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Правка по ходу", ExecutorIds = [ai.Id],
        }, "сделай работу", "", null);

        var stale = _f.Tasks.Get(task.Id)!;   // снимок с прежним значением, как у коннектора
        Assert.Equal(TaskStatuses.Review, stale.AiDoneStatus);
        var edited = _f.Tasks.Get(task.Id)!;
        edited.AiDoneStatus = TaskStatuses.Done;
        _f.Tasks.Update(edited, "сделай работу", "", null);

        await RunJobAsync(task, ai);

        Assert.Equal(TaskStatuses.Done, await WaitForTaskStatus(task.Id, TaskStatuses.Done));
    }

    [Fact]
    public async Task Failure_Still_Means_Error()
    {
        // статус по готовности — про НОРМАЛЬНОЕ завершение; отказ агента к нему отношения
        // не имеет и по-прежнему даёт «встал с ошибкой»
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-ошибка", FailureJson());
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Упадёт", ExecutorIds = [ai.Id],
            AiDoneStatus = TaskStatuses.Done,
        }, "сделай работу", "", null);

        var job = await RunJobAsync(task, ai);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(TaskStatuses.Error, await WaitForTaskStatus(task.Id, TaskStatuses.Error));
    }

    // ---------- 4. «готово» отпускает заблокированные задачи ----------

    [Fact]
    public async Task Done_From_The_Agent_Releases_The_Blocked_Task()
    {
        // ровно то, ради чего задача и делалась: человеку больше не надо руками менять
        // «проверку» на «готово», чтобы ждавшая задача тронулась с места
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-блокирующий", SuccessJson("Сделано."));
        var human = CreateHuman("человек");
        var blocker = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Блокирующая", ExecutorIds = [ai.Id],
            AiDoneStatus = TaskStatuses.Done,
        }, "сделай работу", "", null);
        var blocked = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Ждущая", ExecutorIds = [human.Id],
            Status = TaskStatuses.Pending, BlockerIds = [blocker.Id],
            LaunchJson = """{"mode":"auto"}""",
        }, "ждём", "", null);

        await RunJobAsync(blocker, ai);

        Assert.Equal(TaskStatuses.Done, await WaitForTaskStatus(blocker.Id, TaskStatuses.Done));
        // исполнитель ждущей задачи — человек: автозапуск переводит её в работу
        Assert.True(await WaitFor(() => _f.Tasks.Get(blocked.Id)!.Status == TaskStatuses.InProgress),
            $"ждущая задача осталась в {_f.Tasks.Get(blocked.Id)!.Status}");
    }

    [Fact]
    public async Task Review_From_The_Agent_Keeps_The_Blocked_Task_Waiting()
    {
        // обратная половина того же правила: «проверка» блокирующей ждущих не отпускает —
        // работа ещё не принята человеком (T-6-S1)
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-на-проверку", SuccessJson("Сделано."));
        var human = CreateHuman("человек-2");
        var blocker = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Блокирующая на проверку", ExecutorIds = [ai.Id],
        }, "сделай работу", "", null);
        var blocked = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Ждущая-2", ExecutorIds = [human.Id],
            Status = TaskStatuses.Pending, BlockerIds = [blocker.Id],
            LaunchJson = """{"mode":"auto"}""",
        }, "ждём", "", null);

        await RunJobAsync(blocker, ai);
        Assert.Equal(TaskStatuses.Review, await WaitForTaskStatus(blocker.Id, TaskStatuses.Review));

        // даём фоновым запускам время сработать — и убеждаемся, что не сработали
        await Task.Delay(500);
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(blocked.Id)!.Status);
    }

    [Fact]
    public async Task Done_From_The_Agent_Moves_The_Hierarchy_Run_On()
    {
        // формулировка задания дословно: «если ставится статус готово, то ПРИ ИЕРАРХИЧЕСКОМ
        // ПУСКЕ автоматически запускаются задачи, блокируемые этой задачей». Внутри открытой
        // очереди ждущую задачу запускает не поштучный автозапуск, а проход очереди (T-6-S1),
        // и режим запуска у неё при этом не спрашивается — здесь он «вручную»
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = CreateAi("бот-иерархия", SuccessJson("Сделано."));
        var human = CreateHuman("человек-3");
        var root = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Корень", ExecutorIds = [human.Id],
            Status = TaskStatuses.Pending,
        }, "корень", "", null);
        var blocker = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Блокирующая подзадача", ParentId = root.Id,
            ExecutorIds = [ai.Id], Status = TaskStatuses.Pending, PriorityNum = 25,
            AiDoneStatus = TaskStatuses.Done,
        }, "сделай работу", "", null);
        var blocked = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Ждущая подзадача", ParentId = root.Id,
            ExecutorIds = [human.Id], Status = TaskStatuses.Pending, PriorityNum = 5,
            BlockerIds = [blocker.Id], LaunchJson = """{"mode":"manual"}""",
        }, "ждём", "", null);

        await _f.Orchestrator.StartHierarchyAsync(root.Id, null);
        // очередь взяла блокирующую и пропустила ждущую — её блокирующая ещё не завершена
        Assert.True(await WaitFor(() => _f.Jobs.ListByTask(blocker.Id).Count > 0),
            "очередь иерархии должна была запустить блокирующую подзадачу");
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(blocked.Id)!.Status);

        Assert.Equal(TaskStatuses.Done, await WaitForTaskStatus(blocker.Id, TaskStatuses.Done));
        Assert.True(await WaitFor(() => _f.Tasks.Get(blocked.Id)!.Status == TaskStatuses.InProgress),
            $"ждущая подзадача осталась в {_f.Tasks.Get(blocked.Id)!.Status}");
    }

    // ---------- 5. шаблоны ----------

    [Fact]
    public void Value_Travels_From_The_Template_Node_To_The_Task()
    {
        var project = _f.Projects.Create("Проект", null, null, null);
        var node = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, Title = "Узел шаблона", IsTemplate = true,
            AiDoneStatus = TaskStatuses.Done,
        }, "делаем", "", null);

        var copy = _f.Tasks.InstantiateTemplate(node.Id, null, null);

        Assert.Equal(TaskStatuses.Done, _f.Tasks.Get(copy.Id)!.AiDoneStatus);
        // сам статус копии — «ожидает», как и прежде: путать эти два поля нельзя
        Assert.Equal(TaskStatuses.Pending, _f.Tasks.Get(copy.Id)!.Status);
    }
}
