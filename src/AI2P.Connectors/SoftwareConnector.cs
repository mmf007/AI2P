using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ИСПОЛНИТЕЛЬ «АВТО ПО» (T-154-S0) — задание выполняет ВНЕШНЯЯ ПРОГРАММА, а не модель и не
/// человек. Третий вид исполнителя завела T-153-S0 (<see cref="ExecutorKind.Software"/>); здесь
/// — то, что за ним стоит: запуск программы плагина, живой вывод, артефакты и остановка.
///
/// <para>ЧТО ЗАПУСКАЕТСЯ. У исполнителя записана пара «плагин + операция»
/// (<see cref="Executor.PluginCode"/> и <see cref="Executor.PluginOp"/> — имя инструмента
/// действия манифеста). Командную строку собирает САМ ПЛАГИН (<see cref="ConvertLine.Build"/>):
/// аргументы лежат в файле дистрибутива, значения — в настройках записи плагина, а от задачи
/// приходят только ПУТИ и значения ОБЪЯВЛЕННЫХ настроек. Произвольной командной строки нет
/// нигде и ни у кого — это правило ветки (T-110-S0 §1.3.7), и «авто ПО» его не отменяет:
/// иначе задача с описанием от модели стала бы исполнением чужого кода.</para>
///
/// <para>ПОЧЕМУ ВЫВОД ЧИТАЕТСЯ НЕПРЕРЫВНО И ПИШЕТСЯ В ДВА МЕСТА. Перенесённые потоки надо
/// вычитывать всегда (буфер трубы 4 КБ, дальше программа встаёт на записи — беда T-152-S0),
/// поэтому чтением занят общий <see cref="ProcessOutputLog"/>. Консоль задания
/// (<see cref="JobConsole"/>) — кольцо В ПАМЯТИ процесса: перезапуск сервера её не переживает,
/// а обучение LoRA идёт часами. Поэтому тот же вывод одновременно ложится в файл артефакта
/// задания, и человек читает его и через сутки, и прямо во время работы.</para>
///
/// <para>ТАЙМ-АУТ — МОЛЧАНИЯ, А НЕ ДЛИТЕЛЬНОСТИ. Пока программа печатает, она жива, и убивать
/// её по общему сроку значит терять часы счёта. Поэтому главный предел — сколько она молчит
/// (<see cref="PluginAction.IdleTimeoutSec"/>), а общий стоит сверху и крупный
/// (<see cref="PluginAction.TimeoutSec"/>, у исполнителя его перебивает «Таймаут ответа»).</para>
///
/// <para>ОСТАНОВКА И ИЕРАРХИЯ СВОИХ МЕХАНИЗМОВ НЕ ЗАВОДЯТ: задачу останавливает штатный
/// <c>JobOrchestrator.StopTaskAsync</c> (T-263) и заявки <c>stop_requests</c> с другого сервера,
/// а в иерархии она идёт обычной очередью (T-159). Единственное, что здесь своё, — убийство
/// ДЕРЕВА процессов: тренер запускает питон, питон — свои дочерние, и погашенный родитель
/// оставил бы их считать на видеокарте.</para>
/// </summary>
public sealed class SoftwareConnector : IAgentConnector
{
    /// <summary>Вид коннектора; он же строка <see cref="ExecutorKind.Software"/> в БД.</summary>
    public const string KindName = "software";

    /// <summary>Сколько ждать молчания программы, если операция своего значения не назвала.
    /// Пятнадцать минут: столько молчит обучение между эпохами на медленной машине.</summary>
    public const int DefaultIdleSec = 900;

    /// <summary>Общий предел по умолчанию — СУТКИ: он про «считает, но бесконечно», а не про
    /// «не считает вовсе», и обучение LoRA законно идёт много часов.</summary>
    public const int DefaultTotalSec = 24 * 60 * 60;

    private static readonly ILogger Logger = Log.ForContext<SoftwareConnector>();

    private readonly JobService _jobs;
    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly FileStore _files;
    private readonly EventStore _events;
    private readonly JobConsole _console;

    private readonly ConcurrentDictionary<string, Run> _active = new(StringComparer.Ordinal);

    /// <summary>
    /// ПЛАГИНЫ ЭТОГО СЕРВЕРА — те же живые записи, что публикуются агенту
    /// (<c>OrgContext.LiveGateways</c>): манифест на диске + запись организации в годном
    /// состоянии + найденный ЗДЕСЬ путь к программе. Поставщик, а не готовый список: и записи,
    /// и найденная программа меняются, пока приложение работает.
    /// </summary>
    public Func<IReadOnlyList<GatewayPlugin>>? Gateways { get; set; }

    /// <summary>
    /// ОБУЧЕНИЕ LoRA ЗАДАЧЕЙ (T-157-S0): по задаче найти строку обучения (обратный поиск,
    /// <c>ObjectService.LoraModelByTrainTask</c>) — null означает «это обычная работа программы».
    /// Ровно из-за этого обратного поиска у задачи нет и не нужно ни одного своего поля с
    /// параметрами: объект, датасет, модель и настройка <c>lora.train</c> лежат в строке.
    /// </summary>
    public Func<string, string?>? LoraOfTask { get; set; }

    /// <summary>
    /// Провести обучение по строке (<c>LoraTrainService.RunForTaskAsync</c>): все пять шагов
    /// профайла модели, живой вывод в консоль ЭТОГО задания, неудача — исключением. Возвращает
    /// путь готового адаптера.
    ///
    /// <para>Почему не «собрать командную строку операции и запустить», как у обычной работы:
    /// обучение — это ТРИ запуска подряд (кэш латентов → кэш текстовых кодировщиков → сам
    /// тренер) плюс сборка датасета до и раскладка адаптера после, и всё это уже описано
    /// настройкой <c>lora.train</c> ПРОФАЙЛА МОДЕЛИ, а не одной операцией плагина. Плагин здесь
    /// даёт другое, и это тоже нужно: привязку к серверу, флаг «один экземпляр» и правило
    /// безопасности «Плагины и MCP».</para>
    /// </summary>
    public Func<string, string, CancellationToken, Task<string>>? RunLoraAsync { get; set; }

    public SoftwareConnector(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, JobConsole console)
    {
        _jobs = jobs;
        _tasks = tasks;
        _executors = executors;
        _projects = projects;
        _files = files;
        _events = events;
        _console = console;
    }

    public string Kind => KindName;

    public Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default)
    {
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        var cts = new CancellationTokenSource();
        // ЗАНЯТОСТЬ ПЛАГИНА ОТМЕЧАЕТСЯ ЗДЕСЬ, а не в фоновой работе: между «задание принято»
        // и «процесс пошёл» проходит время, и очередь, спросив в этот промежуток, увидела бы
        // программу свободной и запустила бы второй экземпляр
        _active[job.Id] = new Run(cts)
        {
            PluginCode = _executors.Get(job.ExecutorId)?.PluginCode ?? "",
        };
        _ = Task.Run(() => RunAsync(job, cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Живой процесс по заданию есть В ЭТОМ процессе приложения (T-117): без этого
    /// сторож зависших заданий счёл бы идущую работу зависшей и запустил бы её поверх.</summary>
    public bool HasActiveRun(string jobId) => _active.ContainsKey(jobId);

    /// <summary>
    /// ОСТАНОВИТЬ: гасим ДЕРЕВО процессов. Тренер запускает питон, тот — свои дочерние, и
    /// убитый родитель оставил бы их считать на видеокарте до перезагрузки машины.
    /// </summary>
    public Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (_active.TryRemove(jobId, out var run))
        {
            run.Cts.Cancel();
            KillTree(run.Process);
            run.Cts.Dispose();
        }
        Logger.Information("Задание {JobId} остановлено (авто ПО)", jobId);
        _jobs.SetState(jobId, JobState.Cancelled, actorId: null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Проба подключения (запуск работы команды): программа плагина найдена на этом сервере
    /// и операция исполнителя в манифесте есть. Ничего не запускаем — проба обязана быть
    /// дешёвой, а запуск тренера стоит часы.
    /// </summary>
    public Task<string?> TestConnectionAsync(Executor executor, CancellationToken ct = default)
    {
        try
        {
            _ = Resolve(executor);
            return Task.FromResult<string?>(null);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<string?>(ex.Message);
        }
    }

    /// <summary>
    /// ОДИН ЭКЗЕМПЛЯР: у операции стоит флаг, и эта программа уже работает по другому заданию.
    /// Спрашивает ОЧЕРЕДЬ ЗАПУСКА (<c>JobOrchestrator.IsBusy</c>) перед стартом — и задача от
    /// этого ЖДЁТ, а не падает с ошибкой: ровно так же она ждёт занятого исполнителя-ИИ.
    /// Сторожить приходится плагин, а не исполнителя: исполнителей у одной программы может
    /// быть несколько, а видеокарта одна (сегодня то же самое стережёт
    /// <c>LoraTrainService._running</c>).
    /// </summary>
    public bool SingleInstanceBusy(Executor executor)
    {
        if (executor.Kind != ExecutorKind.Software || executor.PluginCode.Length == 0)
        {
            return false;
        }
        var plugin = Find(executor.PluginCode);
        var action = ActionOf(plugin, executor.PluginOp);
        // флаг стоит либо у действия, либо у самой операции долгого запуска (T-155-S0):
        // довольно любого из двух — оба означают «двух таких работ разом быть не должно»
        var single = action is { SingleInstance: true }
                     || (action is not null
                         && plugin?.Manifest.Run?.Find(action.Op) is { SingleInstance: true });
        if (!single)
        {
            return false;
        }
        return _active.Values.Any(r => string.Equals(r.PluginCode, executor.PluginCode,
            StringComparison.OrdinalIgnoreCase));
    }

    // --- работа ---

    private async Task RunAsync(Job job, CancellationToken ct)
    {
        var task = _tasks.Get(job.TaskId);
        if (task is null)
        {
            Logger.Error("Задание {JobDisplayId}: задача {TaskId} не найдена", job.DisplayId, job.TaskId);
            _jobs.SetState(job.Id, JobState.Failed, actorId: null);
            return;
        }
        var project = task.ProjectId is null ? null : _projects.Get(task.ProjectId);
        var slug = project?.Slug;
        var sw = Stopwatch.StartNew();
        ProcessOutputLog? log = null;
        try
        {
            var executor = _executors.Get(job.ExecutorId)
                           ?? throw new InvalidOperationException(Loc.T("msg.softwareConnector.7"));
            var (plugin, action, op) = Resolve(executor);

            // ЭТО ЗАДАЧА ОБУЧЕНИЯ LoRA? (T-157-S0) Узнаём обратным поиском по задаче, а не по
            // полям задачи и не разбором её описания. Плагин при этом уже разобран выше
            // намеренно: обучению нужны те же три проверки — плагин здесь, программа найдена,
            // операция есть, — и без них задача уехала бы считать на сервер, где тренера нет
            if (LoraOfTask?.Invoke(task.Id) is { Length: > 0 } loraId && RunLoraAsync is { } train)
            {
                await RunLoraJobAsync(loraId, train, task, job, slug, plugin, action, sw, ct);
                return;
            }

            // ЧТО ПОДСТАВЛЯЕМ В АРГУМЕНТЫ: настройки записи плагина, поверх них — значения,
            // названные В ОПИСАНИИ ЗАДАЧИ, и только те ключи, что ОБЪЯВЛЕНЫ манифестом
            var values = ConvertLine.Values(plugin.Manifest.Settings, plugin.SettingsJson);
            var asked = TaskParams(_tasks.ReadDescription(task));
            ApplyTaskParams(values, plugin.Manifest.Settings, asked);

            var folder = ProjectFolder(project);
            var input = PathParam(asked, "in", folder);
            var target = PathParam(asked, "out", folder)
                         ?? _files.Abs(_files.TaskArtifactRel(slug, task.DisplayId,
                             $"{job.DisplayId}-{OutName(op)}"));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // РАБОЧИЙ КАТАЛОГ операции (T-155-S0): назван манифестом (относительный — от папки
            // проекта), а не назван — папка проекта, а нет и её — каталог результата
            var workDir = WorkDir(op, folder, target, values);
            Directory.CreateDirectory(workDir);
            var line = ConvertLine.Build(op.Args, input ?? "", target, listFile: "", values,
                           out var unresolved, workDir)
                       ?? throw new InvalidOperationException(
                           Loc.T("msg.softwareConnector.5", unresolved));

            // журнал вывода — файлом артефакта: консоль задания живёт в памяти процесса и
            // перезапуск сервера не переживает, а работа идёт часами
            var logRel = _files.TaskArtifactRel(slug, task.DisplayId, $"{job.DisplayId}-console.log");
            log = new ProcessOutputLog(_files.Abs(logRel), _console, job.Id);

            var startLine = Loc.T("msg.softwareConnector.8", plugin.SoftwarePath,
                string.Join(" ", line));
            Logger.Information("Задание {JobDisplayId} по задаче {TaskDisplayId}: {Line}",
                job.DisplayId, task.DisplayId, startLine);
            log.Line(startLine);
            AppendEvent(task, job, EventTypes.AgentRequest, new
            {
                plugin = plugin.Manifest.Code,
                op = action.Tool,
                program = plugin.SoftwarePath,
                args = line,
                path = logRel,
            });

            // тайм-ауты: своё значение действия → значение операции (и её блока) → умолчание
            var idle = TimeSpan.FromSeconds(action.IdleTimeoutSec > 0 ? action.IdleTimeoutSec
                : op.IdleTimeoutSec > 0 ? op.IdleTimeoutSec : DefaultIdleSec);
            // общий предел: «Таймаут ответа» исполнителя (T-124) главнее — его задал человек
            var total = AgentTimeout.Of(executor.ResponseTimeoutMinutes,
                TimeSpan.FromSeconds(action.TimeoutSec > 0 ? action.TimeoutSec
                    : op.TimeoutSec > 0 ? op.TimeoutSec : DefaultTotalSec));

            var code = await RunProcessAsync(plugin, line, target, workDir, job, log, idle, total, ct);
            if (code != 0)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.softwareConnector.11", code, log.Tail()));
            }

            // ЧТО СЧИТАТЬ РЕЗУЛЬТАТОМ (T-155-S0): правило разбора из манифеста. У тренера имя
            // файла заранее неизвестно (epoch-0007.safetensors), и без правила результат
            // работы на двенадцать часов пришлось бы искать человеку глазами
            var produced = op.Result.Resolve(workDir, target, values) ?? target;
            var artifacts = new List<string> { logRel };
            if (File.Exists(produced) && produced.StartsWith(_files.DataDir, StringComparison.OrdinalIgnoreCase))
            {
                artifacts.Add(Rel(produced));
            }
            var resultPath = _files.WriteTaskArtifact(slug, task.DisplayId,
                $"{job.DisplayId}-result.md",
                Summary(plugin, action, produced, sw.Elapsed, artifacts));
            log.Line(Loc.T("msg.softwareConnector.12", sw.Elapsed));

            // денег и токенов у программы нет: она считает на своём железе
            _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost: 0,
                inputTokens: 0, outputTokens: 0, "USD");
            if (!task.IsTemplate)
            {
                // «статус при завершении» — настройка задачи (T-250), общая с ИИ
                _tasks.ChangeStatus(task.Id, _tasks.AiDoneStatusOf(task.Id), actorId: job.ExecutorId);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new
            {
                plugin = plugin.Manifest.Code,
                op = action.Tool,
                files = artifacts,
                elapsedMs = sw.ElapsedMilliseconds,
            });
            Logger.Information("Задание {JobDisplayId} выполнено за {Elapsed} (авто ПО)",
                job.DisplayId, sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // остановлено кнопкой: состояния уже переведены в CancelAsync
            Logger.Information("Задание {JobDisplayId} отменено после {Elapsed}", job.DisplayId, sw.Elapsed);
            log?.Line(Loc.T("msg.softwareConnector.19", sw.Elapsed));
        }
        catch (Exception exRaw)
        {
            var ex = exRaw is OperationCanceledException
                ? new InvalidOperationException(Loc.T("msg.softwareConnector.10", sw.Elapsed))
                : exRaw;
            Logger.Error(ex, "Задание {JobDisplayId} по задаче {TaskDisplayId} завершилось ошибкой за {Elapsed}",
                job.DisplayId, task.DisplayId, sw.Elapsed);
            _console.Write(job.Id, Loc.T("msg.softwareConnector.20") + ex.Message);
            log?.Line("[AI2P] " + ex.Message);
            var errorPath = _files.WriteTaskArtifact(slug, task.DisplayId,
                $"{job.DisplayId}-error.md", Loc.T("msg.softwareConnector.18", ex.Message));
            _jobs.SetState(job.Id, JobState.Failed, actorId: null, errorPath);
            if (!task.IsTemplate)
            {
                _tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: null);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new { error = ex.Message });
        }
        finally
        {
            log?.Dispose();
            if (_active.TryRemove(job.Id, out var run))
            {
                run.Cts.Dispose();
            }
        }
    }

    /// <summary>
    /// ЗАДАНИЕ ОБУЧЕНИЯ LoRA (T-157-S0). Пять шагов ведёт <c>LoraTrainService</c> — они описаны
    /// профайлом модели и одной операцией плагина не выражаются, — а здесь то, ради чего
    /// обучение вообще стало задачей: живой вывод в консоль задания, артефакт-сводка, штатная
    /// остановка и «статус при завершении».
    ///
    /// <para>ОШИБКА ПОПАДАЕТ В ТРИ МЕСТА, и все три нужны разным людям в разное время: в консоль
    /// задания (видно живьём), в артефакт задания (навсегда, читается через сутки) и в поле
    /// <c>Error</c> строки обучения (подсказка в списке моделей объекта). Первое и третье пишет
    /// сам обучающий сервис, второе — общий обработчик ошибок этого коннектора: сюда исключение
    /// поднимается нарочно, а не глохнет.</para>
    /// </summary>
    private async Task RunLoraJobAsync(string loraId,
        Func<string, string, CancellationToken, Task<string>> train, TaskItem task, Job job,
        string? slug, GatewayPlugin plugin, PluginAction action, Stopwatch sw, CancellationToken ct)
    {
        Logger.Information("Задание {JobDisplayId}: обучение адаптера LoRA {LoraId} ({Plugin})",
            job.DisplayId, loraId, plugin.Manifest.Code);
        AppendEvent(task, job, EventTypes.AgentRequest, new
        {
            plugin = plugin.Manifest.Code,
            op = action.Tool,
            lora = loraId,
        });
        // ключ буфера живого вывода — идентификатор ЗАДАНИЯ: панель консоли карточки задачи
        // спрашивает вывод именно по нему, и обучение показывается там без единой правки в UI
        var file = await train(loraId, job.Id, ct);
        var summary = Loc.T("msg.lora.43", file, sw.Elapsed);
        var resultPath = _files.WriteTaskArtifact(slug, task.DisplayId,
            $"{job.DisplayId}-result.md", summary);
        _console.Write(job.Id, summary);
        _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost: 0,
            inputTokens: 0, outputTokens: 0, "USD");
        if (!task.IsTemplate)
        {
            _tasks.ChangeStatus(task.Id, _tasks.AiDoneStatusOf(task.Id), actorId: job.ExecutorId);
        }
        AppendEvent(task, job, EventTypes.AgentResponse, new
        {
            plugin = plugin.Manifest.Code,
            lora = loraId,
            file,
            elapsedMs = sw.ElapsedMilliseconds,
        });
        Logger.Information("Адаптер {File} обучен за {Elapsed} (задание {JobDisplayId})",
            file, sw.Elapsed, job.DisplayId);
    }

    /// <summary>
    /// ЗАПУСТИТЬ ПРОГРАММУ И ДОЖДАТЬСЯ ЕЁ. Оба потока перенаправлены и вычитываются
    /// непрерывно (иначе программа встанет на записи, T-152-S0), окна нет. Возвращает код
    /// возврата; молчание дольше <paramref name="idle"/> и общий предел
    /// <paramref name="total"/> — это исключения, а не код возврата: программа при этом
    /// гасится вместе со всем деревом.
    /// </summary>
    private async Task<int> RunProcessAsync(GatewayPlugin plugin, List<string> line, string target,
        string workDir, Job job, ProcessOutputLog log, TimeSpan idle, TimeSpan? total,
        CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = plugin.SoftwarePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir.Length > 0 ? workDir : Path.GetDirectoryName(target)!,
        };
        foreach (var arg in line)
        {
            info.ArgumentList.Add(arg);
        }
        // питон копит перенаправленный вывод блоками по 8 КБ, и «молчание» считалось бы там,
        // где программа на самом деле печатает (наука T-152-S0)
        info.Environment["PYTHONUNBUFFERED"] = "1";

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException(
                                Loc.T("msg.softwareConnector.21", plugin.SoftwarePath));
        if (_active.TryGetValue(job.Id, out var slot))
        {
            slot.Process = process;
        }
        log.Attach(process);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (total is { } cap)
        {
            limit.CancelAfter(cap);
        }
        try
        {
            if (!await log.WaitForExitAsync(process, idle, limit.Token))
            {
                KillTree(process);
                await log.DrainAsync(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException(
                    Loc.T("msg.softwareConnector.9", idle, log.Tail()));
            }
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
        return process.ExitCode;
    }

    /// <summary>Погасить ДЕРЕВО процессов: тренер запускает дочерние, и они переживают
    /// смерть родителя.</summary>
    private static void KillTree(Process? process)
    {
        if (process is null)
        {
            return;
        }
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // процесс уже кончился сам — гасить нечего
        }
    }

    // --- плагин, операция, параметры ---

    /// <summary>
    /// ЧТО ЗАПУСКАЕТ ЭТОТ ИСПОЛНИТЕЛЬ: живой плагин этого сервера, его действие с именем
    /// <see cref="Executor.PluginOp"/> и описание запуска (операция блока <c>convert</c>).
    /// Отказ внятный на каждом шаге: «плагина здесь нет», «операции нет», «программа не
    /// найдена» — три разных беды, и лечатся они по-разному.
    /// </summary>
    private (GatewayPlugin Plugin, PluginAction Action, RunOp Op) Resolve(Executor executor)
    {
        if (executor.PluginCode.Length == 0 || executor.PluginOp.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.softwareConnector.6", executor.Nick));
        }
        var plugin = Find(executor.PluginCode)
                     ?? throw new InvalidOperationException(
                         Loc.T("msg.softwareConnector.1", executor.Nick, executor.PluginCode));
        var action = ActionOf(plugin, executor.PluginOp)
                     ?? throw new InvalidOperationException(
                         Loc.T("msg.softwareConnector.2", plugin.Manifest.Code, executor.PluginOp));
        if (plugin.SoftwarePath.Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.softwareConnector.3", plugin.Manifest.Code));
        }
        // ОПЕРАЦИЯ ДОЛГОГО ЗАПУСКА ГЛАВНЕЕ КОНВЕРТОРА (T-155-S0): блок run описан ровно под
        // эту работу — у него есть рабочий каталог, тайм-аут молчания и правило разбора
        // результата. Операция конвертора с тем же именем остаётся годной: плагины, написанные
        // до появления блока, запускались именно ею
        var op = plugin.Manifest.Run?.Find(action.Op)
                 ?? (plugin.Manifest.Convert?.Find(action.Op) is { } convert
                     ? RunOp.FromConvert(convert)
                     : null)
                 ?? throw new InvalidOperationException(
                     Loc.T("msg.softwareConnector.4", action.Tool, plugin.Manifest.Code));
        return (plugin, action, op);
    }

    private GatewayPlugin? Find(string code) =>
        Gateways?.Invoke().FirstOrDefault(p =>
            string.Equals(p.Manifest.Code, code, StringComparison.OrdinalIgnoreCase));

    private static PluginAction? ActionOf(GatewayPlugin? plugin, string tool) =>
        plugin?.Manifest.Actions.FirstOrDefault(a =>
            string.Equals(a.Tool, tool, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ПАРАМЕТРЫ ЗАДАЧИ — строки её описания вида «ключ: значение» или «ключ = значение».
    /// Промпта программа не читает (T-153-S0), и другого способа сказать ей «сколько шагов»
    /// у постановщика задачи нет. Строки, не похожие на параметр, пропускаются молча:
    /// описание — это текст для человека, а не файл настроек.
    /// </summary>
    public static Dictionary<string, string> TaskParams(string? description)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (description ?? "").Split('\n'))
        {
            var text = raw.Trim();
            var at = text.IndexOfAny([':', '=']);
            if (at <= 0 || at > 40)
            {
                continue;
            }
            var key = text[..at].Trim().TrimStart('*', '-', ' ').Trim();
            var value = text[(at + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0 || key.Contains(' '))
            {
                continue;
            }
            found[key] = value;
        }
        return found;
    }

    /// <summary>
    /// Наложить параметры задачи на значения настроек — ТОЛЬКО объявленные манифестом ключи и
    /// только объявленные значения у полей-перечислений. Это и есть граница безопасности:
    /// текст описания пишет человек или модель, и подставлять из него что попало в аргументы
    /// внешней программы нельзя.
    /// </summary>
    private static void ApplyTaskParams(IDictionary<string, string> values,
        IReadOnlyList<PluginSetting> declared, IReadOnlyDictionary<string, string> asked)
    {
        foreach (var setting in declared)
        {
            if (!asked.TryGetValue(setting.Key, out var value) || value.Length == 0)
            {
                continue;
            }
            if (setting.Choices.Count > 0 && !setting.Choices.Contains(value, StringComparer.Ordinal))
            {
                continue;
            }
            values[setting.Key] = value;
        }
    }

    /// <summary>Путь-параметр задачи («in», «out») внутри ПАПКИ ПРОЕКТА; за её край он не
    /// выпускается, полного пути от задачи не принимаем вовсе.</summary>
    private static string? PathParam(IReadOnlyDictionary<string, string> asked, string name,
        string? folder) =>
        asked.TryGetValue(name, out var value) && value.Length > 0 && !Path.IsPathRooted(value)
            ? ProjectFiles.Resolve(folder, value)
            : null;

    /// <summary>Имя файла результата по умолчанию: то, что назвала операция манифеста.</summary>
    private static string OutName(RunOp op) =>
        (op.OutName.Length > 0 ? op.OutName : op.Op) + (op.OutExt.Length > 0 ? op.OutExt : ".out");

    /// <summary>
    /// РАБОЧИЙ КАТАЛОГ ЗАПУСКА (T-155-S0): назван операцией — относительный считается от папки
    /// проекта (за её край его не выпускаем, как и пути-параметры задачи), полный берётся как
    /// есть; не назван — папка проекта, а нет и её — каталог файла результата. В аргументы он
    /// же уходит плейсхолдером <c>{work}</c>.
    /// </summary>
    private static string WorkDir(RunOp op, string? folder, string target,
        IReadOnlyDictionary<string, string> values)
    {
        var named = ConvertLine.Fill(op.WorkDir, values).Trim();
        if (named.Length > 0)
        {
            return Path.IsPathRooted(named)
                ? Path.GetFullPath(named)
                : ProjectFiles.Resolve(folder, named) ?? folder ?? Path.GetDirectoryName(target)!;
        }
        return folder ?? Path.GetDirectoryName(target)!;
    }

    private string Summary(GatewayPlugin plugin, PluginAction action, string target,
        TimeSpan elapsed, IReadOnlyList<string> artifacts)
    {
        var text = new StringBuilder();
        text.AppendLine(Loc.T("msg.softwareConnector.13"));
        text.AppendLine();
        text.AppendLine(Loc.T("msg.softwareConnector.14", plugin.Manifest.Code, action.Tool));
        text.AppendLine(Loc.T("msg.softwareConnector.15", plugin.SoftwarePath));
        text.AppendLine(Loc.T("msg.softwareConnector.16", elapsed));
        text.AppendLine();
        text.AppendLine(Loc.T("msg.softwareConnector.17"));
        foreach (var rel in artifacts)
        {
            text.AppendLine($"* {Path.GetFileName(rel)}");
        }
        if (File.Exists(target) && !target.StartsWith(_files.DataDir, StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine($"* {target}");
        }
        return text.ToString();
    }

    private string Rel(string abs) =>
        Path.GetRelativePath(_files.DataDir, abs).Replace('\\', '/');

    /// <summary>Папка проекта на ЭТОМ сервере; null — не задана либо не существует.</summary>
    private static string? ProjectFolder(Project? project)
    {
        var folder = project?.FolderPath;
        return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? null : folder;
    }

    private void AppendEvent(TaskItem task, Job job, string eventType, object payload) =>
        _events.Append(new EventRecord
        {
            ActorId = job.ExecutorId,
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            EventType = eventType,
            EntityType = "job",
            EntityId = job.Id,
            PayloadJson = JsonSerializer.Serialize(payload),
        });

    /// <summary>Идущая работа: чем гасить и какой плагин занят (флаг «один экземпляр»).</summary>
    private sealed class Run(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public Process? Process { get; set; }
        public string PluginCode { get; set; } = "";
    }
}
