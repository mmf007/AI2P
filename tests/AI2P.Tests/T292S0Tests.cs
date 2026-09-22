using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// СУФЛЁР — ИСПОЛНИТЕЛЬ, А НЕ МОДЕЛЬ (T-292-S0, ветка T-285-S0).
///
/// <para>ЗАЧЕМ ПЕРЕДЕЛКА. В T-288-S0 суфлёра звал свой маленький HTTP-клиент на два
/// провайдера, и поэтому подписка CLI с локальными моделями суфлёрами работать не могли
/// вовсе: на старте в консоль писалось «можно использовать только api модели», а задание
/// уходило в генерацию без управляющего json — то есть молча делало не то, о чём просили.
/// Теперь суфлёр — обычный исполнитель: у него свой коннектор, свой профайл, свои правила
/// безопасности и своя занятость, и фаза суфлёра это ОТДЕЛЬНОЕ ЗАДАНИЕ той же задачи
/// (<c>jobs.role = "prompter"</c>).</para>
///
/// <para>ЧТО ЗДЕСЬ ПРОВЕРЯЕТСЯ: хранение (колонки, таблица замены, репликация, роль
/// задания), правило повторного запуска по состоянию задачи и разметка форм. Сам ход
/// «суфлёр → генерация» проверками набора не воспроизводится — это фоновая работа
/// коннектора с живой моделью, сети в наборе нет.</para>
/// </summary>
public sealed class T292S0Tests
{
    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", Path.Combine(parts)));

    // ---------- 1. хранение ----------

    /// <summary>Колонки и таблица на месте, и обе колонки уезжают репликацией сами: триггеры
    /// журнала изменений перечисляют колонки ПО ФАКТУ (ChangeLog.Install → ColumnsOf).</summary>
    [Fact]
    public void The_Schema_Knows_The_Prompter_Of_An_Executor_A_Task_And_A_Job()
    {
        var f = new StorageFixture();
        using var conn = f.Db.Open();

        Assert.Contains("prompter_executor_id",
            Sql.Query(conn, null, "PRAGMA table_info(executors)", r => r.S("name")));
        Assert.Contains("prompter_executor_id",
            Sql.Query(conn, null, "PRAGMA table_info(tasks)", r => r.S("name")));
        Assert.Contains("role",
            Sql.Query(conn, null, "PRAGMA table_info(jobs)", r => r.S("name")));
        Assert.NotEmpty(Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='table' AND name='task_prompter_alt_executors'",
            r => r.S("name")));

        var trigger = Sql.Scalar<string>(conn, null,
            "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='ai2p_chg_tasks_ins'") ?? "";
        Assert.Contains("prompter_executor_id", trigger);
    }

    /// <summary>Замена суфлёра реплицируется: задания идут на разных серверах кластера, и
    /// список замены обязан быть у каждого — иначе задача встанет ждать там, где у неё
    /// запасных «нет».</summary>
    [Fact]
    public void The_Substitutes_Table_Is_Replicated()
    {
        var f = new StorageFixture();
        using var conn = f.Db.Open();
        var trigger = Sql.Scalar<string>(conn, null,
            "SELECT sql FROM sqlite_master WHERE type='trigger' "
            + "AND name='ai2p_chg_task_prompter_alt_executors_ins'") ?? "";
        Assert.NotEqual("", trigger);
    }

    /// <summary>Суфлёр и его замена сохраняются у задачи и читаются обратно В ПОРЯДКЕ
    /// ПРЕДПОЧТЕНИЯ: первым берётся первый свободный, поэтому порядок — часть смысла.</summary>
    [Fact]
    public void A_Task_Keeps_Its_Prompter_And_The_Substitutes_In_Order()
    {
        var f = new StorageFixture();
        var first = f.Executors.CreateMember(Ai("Суфлёр-1"), null);
        var second = f.Executors.CreateMember(Ai("Суфлёр-2"), null);
        var third = f.Executors.CreateMember(Ai("Суфлёр-3"), null);

        var task = f.Tasks.Create(new TaskItem
        {
            Title = "Песня",
            PrompterExecutorId = first.Id,
            PrompterAltExecutorIds = [second.Id, third.Id],
        }, "", "", null);

        var saved = f.Tasks.Get(task.Id)!;
        Assert.Equal(first.Id, saved.PrompterExecutorId);
        Assert.Equal([second.Id, third.Id], saved.PrompterAltExecutorIds);

        // назначенный суфлёр в списке замены сам себя не заменяет
        saved.PrompterAltExecutorIds = [first.Id, third.Id];
        f.Tasks.Update(saved, "", "", null);
        Assert.Equal([third.Id], f.Tasks.Get(task.Id)!.PrompterAltExecutorIds);

        // снятие суфлёра: пустая строка — это «суфлёра нет», а не «суфлёр с пустым id»
        f.Tasks.SetPrompter(task.Id, "");
        Assert.Null(f.Tasks.Get(task.Id)!.PrompterExecutorId);
    }

    /// <summary>РОЛЬ задания пишется и читается: без неё завершение задания суфлёра выглядело
    /// бы как завершение самой задачи — результат ушёл бы человеку на проверку, а генерация
    /// не запустилась бы никогда.</summary>
    [Fact]
    public void A_Job_Remembers_That_It_Is_A_Prompter_Run()
    {
        var f = new StorageFixture();
        var executor = f.Executors.CreateMember(Ai("Суфлёр"), null);
        var task = f.Tasks.Create(new TaskItem { Title = "Песня" }, "", "", null);

        var prompter = f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null,
            JobRoles.Prompter);
        var work = f.Jobs.Create(task.Id, executor.Id, task.DescriptionPath, null);

        Assert.Equal(JobRoles.Prompter, f.Jobs.Get(prompter.Id)!.Role);
        Assert.Equal("", f.Jobs.Get(work.Id)!.Role);   // обычное задание — прежнее поведение
    }

    // ---------- 2. правило повторного запуска ----------

    /// <summary>
    /// Готовый управляющий json берётся ГОТОВЫМ только у задачи, работа над которой
    /// продолжается («пауза», «ошибка»); «черновик», «ожидание» и «доработка» означают новую
    /// работу, и json готовится заново, что бы ни лежало в артефактах.
    /// </summary>
    [Fact]
    public void The_Ready_Json_Is_Reused_Only_When_The_Work_Is_Being_Continued()
    {
        var source = Src("AI2P.Connectors", "JobOrchestrator.cs");
        Assert.Contains("private string? ReadyPrompterJson(TaskItem task, string statusOnStart)", source);
        Assert.Contains("if (statusOnStart is TaskStatuses.Draft or TaskStatuses.Pending "
                        + "or TaskStatuses.NeedsFix)", source);
        // состояние запоминается ДО перевода задачи «в работу» — иначе правило не сработает
        Assert.Contains("var statusOnStart = task.Status;", source);
        // фаза суфлёра — отдельное задание, а не вызов внутри запуска генерации
        Assert.Contains("return await StartPrompterJobAsync(task, executor, actorId);", source);
        Assert.Contains("JobRoles.Prompter", source);
    }

    /// <summary>Три исхода фазы суфлёра: нет суфлёра — ошибка запуска, все заняты — ожидание
    /// на паузе, ответ не json — ошибка задачи.</summary>
    [Fact]
    public void The_Three_Outcomes_Of_The_Prompter_Phase_Are_Spelled_Out()
    {
        var source = Src("AI2P.Connectors", "JobOrchestrator.cs");
        Assert.Contains("throw PrompterFailed(task, main, Loc.T(\"msg.prompter.17\", main.Nick));", source);
        Assert.Contains("throw WaitForFree(task, Loc.T(\"msg.prompter.18\"", source);
        Assert.Contains("_tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: job.ExecutorId);", source);
        // ожидание — это пауза плюс отложенный старт: поднимет задачу сторож
        Assert.Contains("_tasks.SetStartAfter(task.Id, DateTime.UtcNow.AddMinutes(1));", source);
        Assert.Contains("_tasks.ChangeStatus(task.Id, TaskStatuses.Paused, actorId: null);", source);
    }

    /// <summary>Суфлёром работает ЛЮБОЙ исполнитель: отбора по провайдеру в коде больше нет,
    /// задание уходит обычным коннектором — именно это и чинит жалобу про CLI и локальные
    /// модели.</summary>
    [Fact]
    public void Any_Executor_Can_Be_A_Prompter()
    {
        var orchestrator = Src("AI2P.Connectors", "JobOrchestrator.cs");
        Assert.Contains("connector = _connectors.Resolve(chosen);", orchestrator);
        Assert.Contains("await connector.SubmitJobAsync(job, requestText);", orchestrator);

        // прежнего своего HTTP-клиента с отбором по провайдеру не осталось вовсе
        var service = Src("AI2P.Connectors", "PrompterService.cs");
        Assert.DoesNotContain("CanCall", service);
        Assert.DoesNotContain("chat/completions", service);
        Assert.Contains("public static class PrompterService", service);
    }

    // ---------- 3. формы ----------

    [Fact]
    public void The_Task_Form_Asks_For_A_Prompter_And_Its_Substitutes()
    {
        var dialog = Src("AI2P.UI", "Components", "TaskDialog.razor");
        Assert.Contains("data-task-prompter=\"1\"", dialog);
        Assert.Contains("data-task-prompter-alt=\"1\"", dialog);
        Assert.Contains("_task.PrompterExecutorId", dialog);
        Assert.Contains("_task.PrompterAltExecutorIds", dialog);
        // подбор исполнителя подбирает и суфлёра — иначе задачу нельзя было бы запустить
        Assert.Contains("picked.PrompterExecutorId", dialog);
    }

    [Fact]
    public void The_Auto_Pick_Looks_For_A_Prompter_Among_Analysts()
    {
        var picker = Src("AI2P.Storage", "Services", "ExecutorPickService.cs");
        Assert.Contains("public const string PrompterSkill = \"analyze-data\";", picker);
        Assert.Contains("result.PrompterExecutorId = prompter.Id;", picker);
    }

    // ---------- 4. словари ----------

    /// <summary>Новые тексты заведены на ВСЕХ пяти языках: состав словарей обязан сходиться
    /// поимённо (наука T-180).</summary>
    [Fact]
    public void Every_New_Message_Exists_In_All_Five_Languages()
    {
        string[] keys =
        [
            "msg.prompter.17", "msg.prompter.18", "msg.prompter.19", "msg.prompter.20",
            "msg.prompter.21", "msg.prompter.22", "msg.prompter.23", "msg.executorPick.11",
            "executors.prompterExecutor", "executors.prompterExecutor.hint",
            "task.prompter", "task.prompter.hint", "task.prompterAlt", "task.prompterAlt.hint",
        ];
        foreach (var lang in Langs)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "i18n", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var value)
                            && value.GetString() is { Length: > 0 },
                    $"{lang}: нет ключа {key}");
            }
        }
    }

    private static Executor Ai(string nick) => new()
    {
        Nick = nick,
        InternalName = nick,
        Kind = ExecutorKind.Ai,
        SystemRole = SystemRole.Editor,
    };
}
