using AI2P.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Коннектор медиа-моделей через локальный ComfyUI (ТЗ п. 7.2, v1.41, todo36_4) — провайдер
/// "comfyui". Первая модель — Kandinsky 5.0 T2V Lite (видео по тексту). Жизненный цикл
/// отличается от чат-коннекторов: агентного цикла и инструментов нет — описание задачи
/// служит промптом генерации; workflow-шаблон (API-формат ComfyUI) с плейсхолдерами
/// берётся из файла профайла (поле workflow), результат — бинарные файлы в artifacts/
/// задачи + сводка J-N-result.md. Протокол: POST /prompt → поллинг GET /history/{id} →
/// скачивание GET /view; отмена — POST /interrupt + удаление из очереди.
/// Запуск самого ComfyUI — штатный launchCommand профайла (как llama-server, п. 2.9).
/// </summary>
public sealed class ComfyUiConnector : IAgentConnector
{
    public const string Provider = "comfyui";

    /// <summary>Общий HTTP-клиент: генерация занимает десятки минут — таймаут запросов
    /// поллинга короткий, ожидание результата ограничено params.timeoutMinutes профайла.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(3);

    /// <summary>Как часто вычитывать консоль самого ComfyUI в консоль задания (todo37_3).</summary>
    private static readonly TimeSpan ConsolePollDelay = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions DumpOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JobService _jobs;
    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly FileStore _files;
    private readonly EventStore _events;
    private readonly JobConsole _console;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    /// <summary>prompt_id очереди ComfyUI по id задания — для отмены (/interrupt + очередь).</summary>
    private readonly ConcurrentDictionary<string, (string BaseUrl, string PromptId)> _queued = new();

    private static readonly ILogger Logger = Log.ForContext<ComfyUiConnector>();

    /// <param name="console">Консоль задания (ТЗ v1.45, todo37_3): сюда сливается вывод самого
    /// ComfyUI — по нему видно, генерация идёт или зависла.</param>
    /// <summary>
    /// Объекты проекта (T-259): ссылка <c>@obj:OBJ-3</c> в описании задачи разворачивается
    /// в дословный паспорт персонажа. Это единственный способ дать медиа-модели постоянный
    /// образ: промптом ей служит ТОЛЬКО описание задачи, ни опыт проекта, ни критерии
    /// приёмки до неё не доезжают (разбор T-251). null — подстановки нет (тесты).
    /// </summary>
    private readonly ObjectService? _objects;

    /// <summary>
    /// ЗАГРУЗКА ОБЪЕКТОВ В МОДЕЛЬ (T-14-S1): решает, что из названных в описании объектов
    /// уедет в модель адаптером LoRA и картинкой, и что этому мешает. null — проверки нет
    /// (тесты, старая обвязка): тогда всё работает как до T-14-S1.
    /// </summary>
    private readonly ObjectLoadService? _objectLoads;

    /// <summary>Чат задачи: туда уходит предложение замены, когда объект загрузить нечем.</summary>
    private readonly ChatService? _chat;

    /// <param name="objects">Объекты проекта (T-259); null — ссылки <c>@obj:</c> остаются
    /// в промпте как есть.</param>
    /// <param name="objectLoads">Загрузка объектов в модель (T-14-S1).</param>
    /// <param name="chat">Чат задачи (T-14-S1): предложение замены исполнителя.</param>
    public ComfyUiConnector(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, JobConsole console,
        ObjectService? objects = null, ObjectLoadService? objectLoads = null, ChatService? chat = null)
    {
        _objects = objects;
        _objectLoads = objectLoads;
        _chat = chat;
        _jobs = jobs;
        _tasks = tasks;
        _executors = executors;
        _projects = projects;
        _files = files;
        _events = events;
        _console = console;
    }

    public string Kind => Provider;

    public Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default)
    {
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        var cts = new CancellationTokenSource();
        _active[job.Id] = cts;
        _ = Task.Run(() => RunAsync(job, cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Живой фоновый вызов есть (T-117): без этого сторож зависших заданий счёл бы
    /// идущую генерацию зависшей и перезапустил бы её поверх работающей.</summary>
    public bool HasActiveRun(string jobId) => _active.ContainsKey(jobId);

    public async Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (_active.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        // прервать текущую генерацию и убрать из очереди ComfyUI (не критично при неудаче)
        if (_queued.TryRemove(jobId, out var queued))
        {
            try
            {
                await Http.PostAsync($"{queued.BaseUrl}/interrupt", content: null, ct);
                await Http.PostAsync($"{queued.BaseUrl}/queue",
                    new StringContent($$"""{"delete":["{{queued.PromptId}}"]}""", Encoding.UTF8,
                        "application/json"), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Logger.Warning(ex, "Задание {JobId}: не удалось прервать генерацию в ComfyUI", jobId);
            }
        }
        Logger.Information("Задание {JobId} остановлено", jobId);
        _jobs.SetState(jobId, JobState.Cancelled, actorId: null);
    }

    /// <summary>Проба подключения (запуск работы команды, ТЗ v1.14): GET /system_stats.</summary>
    public async Task<string?> TestConnectionAsync(Executor executor, CancellationToken ct = default)
    {
        try
        {
            var profile = LoadProfile(executor);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await Http.GetAsync($"{BaseUrl(profile)}/system_stats", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Loc.T("msg.comfyUiConnector.1", (int)response.StatusCode);
            }
            Logger.Information("Проба подключения ComfyUI: исполнитель {Nick} подключен ({BaseUrl})",
                executor.Nick, BaseUrl(profile));
            return null;
        }
        catch (OperationCanceledException)
        {
            return Loc.T("msg.comfyUiConnector.2");
        }
        catch (Exception ex)
        {
            return Loc.T("msg.comfyUiConnector.3", ex.Message);
        }
    }

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
        try
        {
            var executor = _executors.Get(job.ExecutorId)
                           ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.4"));
            var profile = LoadProfile(executor);
            // промпт генерации — описание задачи как есть (без обвязки агентного промпта):
            // у медиа-модели заголовок и критерии приёмки в промпт не попадают.
            // Указание «результат положить в … файл …» (todo37_3) исполняет коннектор:
            // оно вынимается из описания и в промпт генерации не идёт
            // ССЫЛКИ НА ОБЪЕКТЫ РАСКРЫВАЮТСЯ ПЕРЕД РАЗБОРОМ (T-259): «@obj:OBJ-3» в описании
            // превращается в дословный паспорт персонажа и пути его эталонных кадров.
            // Раскрытие идёт при КАЖДОМ запуске, поэтому правка паспорта действует на
            // следующий же кадр — описания задач переписывать не нужно
            var rawDescription = _tasks.ReadDescription(task);
            var description = _objects is null
                ? rawDescription
                : _objects.Expand(task.ProjectId, rawDescription, kind => ObjectKinds.Title(kind));
            // ЗАГРУЗКА ОБЪЕКТОВ В МОДЕЛЬ (T-14-S1): ссылка на объект — это не только паспорт
            // в промпте. Эталонный кадр надо залить в движок, адаптер LoRA — подключить
            // к самой модели, и то и другое модель может не уметь. Проверяем ДО постановки
            // в очередь: генерация без адаптера выглядит успешной, а персонаж на ней чужой
            // модель исполнителя нужна и как ИМЯ для сообщений, и как КЛЮЧ: адаптер обучается
            // под конкретные веса (T-12-S1), и «обучен ли он» — вопрос про эту запись справочника
            var plan = _objectLoads?.Plan(task.ProjectId, rawDescription, profile.Lora,
                profile.RefImage, ModelTitle(executor, profile), ProjectFolder(project),
                executor.ModelId);
            if (plan is not null)
            {
                foreach (var note in plan.Notes)
                {
                    _console.Write(job.Id, note);
                }
                if (!plan.Ok)
                {
                    // в чат — что делать дальше: какие адаптеры в проекте есть и кто из
                    // исполнителей может взять эту работу. Сообщение уходит ДО ошибки:
                    // упавшее задание человек открывает уже вместе с ответом на «а теперь что»
                    _chat?.Add(task.Id, job.ExecutorId, task.ResponsibleId,
                        _objectLoads!.ChatText(plan, task.ProjectId, job.ExecutorId));
                    throw new InvalidOperationException(ObjectLoadService.ErrorText(plan));
                }
            }
            // СТАРТОВЫЙ КАДР разбирается ПЕРВЫМ (T-258): его указание («взять за основу
            // refs/hero.png») и строки-пути раскрытой ссылки на объект уходят из промпта
            // раньше, чем описание попадёт в разбор указания о результате — иначе строка
            // «исходный файл refs/hero.png» досталась бы разбору результата по слову «файл»
            var (afterInput, inputRel) = MediaInputDirective.Parse(description);
            var (prompt, outputRel) = MediaOutputDirective.Parse(afterInput);
            if (prompt.Length == 0)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.comfyUiConnector.5"));
            }
            // ЭТАЛОННЫЙ КАДР, названный ссылкой на объект (T-14-S1), сильнее догадки разбора:
            // человек указал его сам, а разбор берёт первый путь, какой попался в тексте
            if (plan is { Images.Count: > 0 })
            {
                inputRel = plan.Images[0].Path;
            }
            // модель «изображение → видео» узнаётся по плейсхолдеру картинки в её workflow
            // (имя плейсхолдера — из справочника моделей, T-13-S1): стартовый кадр заливается
            // в ComfyUI, а в граф идёт имя файла в его каталоге input
            var template = ReadWorkflow(profile);
            string? imageName = null;
            if (NeedsStartImage(template, profile.RefImage))
            {
                if (inputRel is null)
                {
                    throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.39"));
                }
                imageName = await UploadStartImageAsync(profile, project, inputRel, job.DisplayId, ct);
                _console.Write(job.Id, Loc.T("msg.comfyUiConnector.45", imageName));
            }
            else if (inputRel is not null)
            {
                // t2v-модели стартовый кадр передать некуда — молчать об этом нельзя:
                // со стороны это выглядело бы как «картинку взяли, а персонаж другой»
                _console.Write(job.Id, Loc.T("msg.comfyUiConnector.46", inputRel));
            }
            // АДАПТЕРЫ LoRA (T-14-S1): файл кладётся в каталог, из которого его видит ComfyUI,
            // и подставляется в граф — узлом настройки либо плейсхолдером шаблона
            var adapters = PlaceAdapters(plan, job.Id);
            // seed: пусто в профайле — случайный (как было), число — этот же у всех кадров
            // (одинаковость персонажа, разбор T-251 п. 2.2)
            var seed = profile.MediaSeed ?? Random.Shared.NextInt64(0, uint.MaxValue);
            var workflow = BuildWorkflow(profile, prompt, seed, job.DisplayId, imageName, adapters);

            var startLine =
                Loc.T("msg.comfyUiConnector.6", BaseUrl(profile), profile.Model,
                    profile.MediaWidth, profile.MediaHeight, profile.MediaLength,
                    profile.MediaSteps, seed, prompt.Length) +
                (inputRel is null ? "" : Loc.T("msg.comfyUiConnector.43", inputRel)) +
                (outputRel is null ? "" : Loc.T("msg.comfyUiConnector.7", outputRel));
            Logger.Information("Задание {JobDisplayId} по задаче {TaskDisplayId}: {Line}",
                job.DisplayId, task.DisplayId, startLine);
            _console.Write(job.Id, startLine);

            // полный запрос — файлом (п. 6.3), в журнале — путь; параметры генерации пишутся
            // и в само событие (todo37_3) — во вкладках «История» задачи и проекта видно,
            // на чём и с какими параметрами запускалось задание, без открытия файла
            var requestDump = JsonSerializer.Serialize(new
            {
                provider = Kind,
                model = profile.Model,
                baseUrl = BaseUrl(profile),
                seed,
                width = profile.MediaWidth,
                height = profile.MediaHeight,
                length = profile.MediaLength,
                steps = profile.MediaSteps,
                negative = profile.MediaNegative,
                startImage = inputRel,
                loras = adapters.Select(a => new { a.ObjectCode, file = Path.GetFileName(a.File), a.Strength }),
                prompt,
                workflow = JsonDocument.Parse(workflow).RootElement,
            }, DumpOptions);
            var requestPath = WriteAiFile(slug, task.DisplayId, $"{job.DisplayId}-request.json", requestDump);
            AppendEvent(task, job, EventTypes.AgentRequest, new
            {
                model = profile.Model,
                path = requestPath,
                baseUrl = BaseUrl(profile),
                seed,
                width = profile.MediaWidth,
                height = profile.MediaHeight,
                length = profile.MediaLength,
                steps = profile.MediaSteps,
                promptLength = prompt.Length,
                startImage = inputRel,
                loras = adapters.Select(a => a.ObjectCode),
                outputFile = outputRel,
            });

            var promptId = await SubmitPromptAsync(profile, workflow, job.Id, ct);
            _queued[job.Id] = (BaseUrl(profile), promptId);
            Logger.Information("Задание {JobDisplayId}: поставлено в очередь ComfyUI, prompt_id {PromptId}",
                job.DisplayId, promptId);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.8", promptId));
            // постановка в очередь — тоже событие журнала (todo37_3): по нему видно, что
            // задание дошло до ComfyUI, и с каким prompt_id его искать в его очереди
            AppendEvent(task, job, EventTypes.JobQueued, new
            {
                promptId,
                baseUrl = BaseUrl(profile),
                model = profile.Model,
            });

            // пока идёт генерация, консоль ComfyUI переливается в консоль задания (todo37_3):
            // именно там виден прогресс-бар шагов — без него задание выглядит зависшим
            List<(string Filename, string Subfolder, string Type)> outputs;
            using (var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var pump = PumpConsoleAsync(profile, job.Id, pumpCts.Token);
                try
                {
                    // сколько ждать генерацию: «Таймаут ответа» исполнителя (T-124), а если
                    // он не задан — params.timeoutMinutes профайла, как было до T-124
                    outputs = await WaitResultAsync(profile, promptId,
                        AgentTimeout.Of(executor.ResponseTimeoutMinutes,
                            TimeSpan.FromMinutes(profile.MediaTimeoutMinutes)), ct);
                }
                finally
                {
                    pumpCts.Cancel();
                    try
                    {
                        await pump;
                    }
                    catch (Exception ex)
                    {
                        // сбой чтения консоли не должен подменять собой исход генерации
                        Logger.Warning(ex, "Задание {JobDisplayId}: чтение консоли ComfyUI прервано",
                            job.DisplayId);
                    }
                }
            }
            _queued.TryRemove(job.Id, out _);

            // скачивание готовых файлов в artifacts/ задачи (бинарные артефакты, ТЗ v1.41)
            var artifactPaths = new List<string>();
            foreach (var (filename, subfolder, type) in outputs)
            {
                var bytes = await DownloadOutputAsync(profile, filename, subfolder, type, ct);
                var rel = _files.WriteTaskArtifactBytes(slug, task.DisplayId,
                    $"{job.DisplayId}-{Path.GetFileName(filename)}", bytes);
                artifactPaths.Add(rel);
                Logger.Information("Задание {JobDisplayId}: сохранён результат {Path} ({Size} байт)",
                    job.DisplayId, rel, bytes.Length);
                _console.Write(job.Id, Loc.T("msg.comfyUiConnector.9", Path.GetFileName(rel), bytes.Length));
            }
            if (artifactPaths.Count == 0)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.comfyUiConnector.10"));
            }

            // указание «положить результат в файл …» (todo37_3): первый файл результата
            // копируется в папку проекта — сам по себе артефакт лежит только в хранилище AI2P
            var copiedTo = CopyToProject(project, outputRel, artifactPaths[0], job.Id);

            // сводка-результат (артефакт .md, как у текстовых коннекторов) + метаданные
            var summary = new StringBuilder();
            summary.AppendLine(Loc.T("msg.comfyUiConnector.11"));
            summary.AppendLine();
            summary.AppendLine(Loc.T("msg.comfyUiConnector.12", profile.Model));
            summary.AppendLine(Loc.T("msg.comfyUiConnector.13", profile.MediaWidth,
                profile.MediaHeight, profile.MediaLength, profile.MediaSteps));
            summary.AppendLine(Loc.T("msg.comfyUiConnector.14", seed));
            summary.AppendLine(Loc.T("msg.comfyUiConnector.15", sw.Elapsed));
            summary.AppendLine();
            summary.AppendLine(Loc.T("msg.comfyUiConnector.16"));
            foreach (var rel in artifactPaths)
            {
                summary.AppendLine($"* {Path.GetFileName(rel)}");
            }
            if (copiedTo is not null)
            {
                summary.AppendLine();
                summary.AppendLine(Loc.T("msg.comfyUiConnector.17", copiedTo));
            }
            var resultPath = _files.WriteTaskArtifact(slug, task.DisplayId,
                $"{job.DisplayId}-result.md", summary.ToString());

            // биллинг: локальная генерация бесплатна (cost по декларации — нули), токенов нет
            _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost: 0,
                inputTokens: 0, outputTokens: 0, "USD");
            if (!task.IsTemplate)
            {
                // статус по готовности (T-250): по умолчанию «проверка» — прежнее поведение;
                // у медиа-задачи он ровно тот же, что и у текстовой
                _tasks.ChangeStatus(task.Id, _tasks.AiDoneStatusOf(task.Id), actorId: job.ExecutorId);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new
            {
                model = profile.Model,
                files = artifactPaths,
                projectFile = copiedTo,
                seed,
                elapsedMs = sw.ElapsedMilliseconds,
            });
            Logger.Information("Задание {JobDisplayId} выполнено за {Elapsed}: файлов {Count}",
                job.DisplayId, sw.Elapsed, artifactPaths.Count);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.18", sw.Elapsed, artifactPaths.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // остановлено кнопкой «остановить» — состояния уже переведены в CancelAsync
            Logger.Information("Задание {JobDisplayId} отменено после {Elapsed}", job.DisplayId, sw.Elapsed);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.19", sw.Elapsed));
        }
        catch (Exception exRaw)
        {
            // OperationCanceledException БЕЗ отмены кнопкой — внутренний таймаут HTTP-вызова
            // (T-117): раньше он выглядел как «остановлено» и оставлял задачу «в работе»
            var ex = exRaw is OperationCanceledException
                ? new InvalidOperationException(
                    Loc.T("msg.comfyUiConnector.20", sw.Elapsed))
                : exRaw;
            Logger.Error(ex, "Задание {JobDisplayId} по задаче {TaskDisplayId} завершилось ошибкой за {Elapsed}",
                job.DisplayId, task.DisplayId, sw.Elapsed);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.21") + ex.Message);
            var errorPath = _files.WriteTaskArtifact(slug, task.DisplayId,
                $"{job.DisplayId}-error.md", Loc.T("msg.comfyUiConnector.22", ex.Message));
            _jobs.SetState(job.Id, JobState.Failed, actorId: null, errorPath);
            if (!task.IsTemplate)
            {
                _tasks.ChangeStatus(task.Id, TaskStatuses.Error, actorId: null);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new { error = ex.Message });
        }
        finally
        {
            _queued.TryRemove(job.Id, out _);
            if (_active.TryRemove(job.Id, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Собрать workflow из шаблона профайла: {prompt}/{negative} — текст внутри кавычек
    /// (JSON-экранируется), "{seed}"/"{width}"/"{height}"/"{length}"/"{steps}" — числа
    /// вместе с кавычками, {job} — код задания (имя выходного файла ComfyUI),
    /// {image} — имя стартового кадра в каталоге input ComfyUI (T-258; у t2v-шаблона
    /// такого плейсхолдера нет вовсе).
    /// </summary>
    public string BuildWorkflow(ModelProfile profile, string prompt, long seed, string jobDisplayId,
        string? imageName = null, IReadOnlyList<ObjectLoadLora>? adapters = null)
    {
        var template = ReadWorkflow(profile);
        // шаблон хранится обёрткой {"_seed": N, "prompt": {…}} (версии сида, п. 2.9) —
        // в ComfyUI уходит только сам граф
        string graph;
        using (var doc = JsonDocument.Parse(template))
        {
            graph = doc.RootElement.TryGetProperty("prompt", out var p)
                ? p.GetRawText()
                : doc.RootElement.GetRawText();
        }
        graph = ApplyLoras(graph, profile, adapters ?? []);
        var imagePlaceholder = ImagePlaceholder(profile.RefImage);
        return graph
            .Replace("{prompt}", JsonEscape(prompt))
            .Replace("{negative}", JsonEscape(profile.MediaNegative))
            .Replace("{job}", JsonEscape(jobDisplayId))
            .Replace(imagePlaceholder, JsonEscape(imageName ?? ""))
            .Replace("\"{seed}\"", seed.ToString())
            .Replace("\"{width}\"", profile.MediaWidth.ToString())
            .Replace("\"{height}\"", profile.MediaHeight.ToString())
            .Replace("\"{length}\"", profile.MediaLength.ToString())
            .Replace("\"{steps}\"", profile.MediaSteps.ToString());
    }

    /// <summary>Текст workflow-шаблона профайла (файл хранилища); пустой шаблон — ошибка.</summary>
    private string ReadWorkflow(ModelProfile profile)
    {
        if (profile.Workflow.Length == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.comfyUiConnector.23"));
        }
        var template = _files.ReadText(profile.Workflow);
        if (template.Trim().Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.24", profile.Workflow));
        }
        return template;
    }

    /// <summary>Модель ждёт стартовый кадр (T-258): в её шаблоне есть плейсхолдер {image}.</summary>
    public static bool NeedsStartImage(string workflowTemplate) =>
        workflowTemplate.Contains("{image}", StringComparison.Ordinal);

    /// <summary>
    /// То же, но плейсхолдер берётся из СПРАВОЧНИКА МОДЕЛЕЙ (T-13-S1, ключ
    /// <c>refImage.placeholder</c>): свой шаблон вправе назвать его иначе, и «как грузить»
    /// система обязана узнавать из настройки, а не из зашитой строки (T-14-S1).
    /// </summary>
    public static bool NeedsStartImage(string workflowTemplate, RefImageSettings refImage) =>
        workflowTemplate.Contains(ImagePlaceholder(refImage), StringComparison.Ordinal);

    /// <summary>Плейсхолдер картинки: из настройки, а нет её — прежний {image}.</summary>
    private static string ImagePlaceholder(RefImageSettings refImage) =>
        refImage.Placeholder.Length > 0 ? refImage.Placeholder : "{image}";

    /// <summary>
    /// Подключить адаптеры LoRA к графу (T-14-S1). Шаблон с плейсхолдером обслуживается
    /// подстановкой, без плейсхолдера — вставкой узла (<see cref="ComfyLoraGraph"/>).
    /// Шаблон ТРЕБУЕТ адаптер (плейсхолдер есть), а его не назвали — это ошибка: с пустым
    /// именем файла ComfyUI откажется считать граф, и разбираться пришлось бы по его логу.
    /// </summary>
    private static string ApplyLoras(string graph, ModelProfile profile,
        IReadOnlyList<ObjectLoadLora> adapters)
    {
        var apply = profile.Lora.Apply;
        if (ComfyLoraGraph.HasPlaceholder(graph, apply))
        {
            if (adapters.Count == 0)
            {
                throw new InvalidOperationException(Loc.T("msg.objectLoad.19", apply.Placeholder));
            }
            return ComfyLoraGraph.FillPlaceholders(graph, apply, adapters);
        }
        return ComfyLoraGraph.Insert(graph, apply, adapters);
    }

    /// <summary>
    /// Положить файлы адаптеров туда, где их видит ComfyUI, и сказать об этом в консоль
    /// задания: подключённый адаптер должен быть виден человеку так же, как стартовый кадр.
    /// </summary>
    private IReadOnlyList<ObjectLoadLora> PlaceAdapters(ObjectLoadPlan? plan, string jobId)
    {
        if (plan is not { Loras.Count: > 0 } || _objectLoads is null)
        {
            return [];
        }
        var placed = new List<ObjectLoadLora>();
        foreach (var adapter in plan.Loras)
        {
            var file = _objectLoads.PlaceInLorasDir(adapter.File);
            placed.Add(adapter with { File = file });
            _console.Write(jobId, Loc.T("msg.objectLoad.18", adapter.ObjectCode, adapter.ObjectName,
                Path.GetFileName(file), adapter.Strength));
        }
        return placed;
    }

    /// <summary>Как называть модель исполнителя в сообщениях человеку (T-14-S1).</summary>
    private static string ModelTitle(Executor executor, ModelProfile profile) =>
        profile.Model.Length > 0 ? profile.Model : executor.Nick;

    /// <summary>
    /// Залить стартовый кадр в ComfyUI (T-258): файл берётся В ПАПКЕ ПРОЕКТА по
    /// относительному пути из описания задачи (там же лежат эталонные кадры объектов,
    /// T-259) либо в каталоге данных организации, если это кадр датасета LoRA (T-98-S0),
    /// уходит POST /upload/image в каталог input и возвращается ИМЕНЕМ, которое
    /// подставляется в узел LoadImage графа.
    /// </summary>
    private async Task<string> UploadStartImageAsync(ModelProfile profile,
        Core.Entities.Project? project, string inputRel, string jobDisplayId, CancellationToken ct)
    {
        string source;
        if (ObjectFiles.IsStore(inputRel))
        {
            // кадр датасета LoRA лежит в каталоге данных организации (T-98-S0), а не в папке
            // проекта: выход за край корня стережёт сам разбор пути
            source = ObjectFiles.Resolve(inputRel, null, _files.DataDir)
                     ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.42", inputRel));
        }
        else
        {
            var folder = ProjectFolder(project)
                         ?? throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.40", inputRel));
            source = Path.GetFullPath(Path.Combine(folder, inputRel));
            // страховка: читаем строго внутри папки проекта (разбор пути её тоже стережёт)
            if (!source.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.42", inputRel));
            }
        }
        if (!File.Exists(source))
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.41", inputRel));
        }
        // имя в каталоге input своё у каждого задания: чужие файлы не перетираются,
        // а повторный запуск того же задания перезаписывает свой (overwrite)
        var name = $"ai2p_{jobDisplayId}{Path.GetExtension(source).ToLowerInvariant()}";
        // ИМЯ ПОЛЯ и АДРЕС ЗАГРУЗКИ — из справочника моделей (T-13-S1/T-14-S1), а не зашиты:
        // свой движок вправе принимать картинку иначе. Пусто в настройке — прежние значения.
        var field = profile.RefImage.Field.Length > 0 ? profile.RefImage.Field : "image";
        var uploadPath = profile.RefImage.UploadPath.Length > 0
            ? "/" + profile.RefImage.UploadPath.TrimStart('/')
            : "/upload/image";
        // тело — как у браузера (T-277: штатный Add() добавляет ещё и filename*, из-за
        // которого ComfyUI получал имя файла вместе с кавычками и отвечал HTTP 500)
        using var form = ComfyUploadForm.Build(field, name,
            await File.ReadAllBytesAsync(source, ct), ContentTypeOf(source));
        using var response = await Http.PostAsync($"{BaseUrl(profile)}{uploadPath}", form, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.comfyUiConnector.44", inputRel, (int)response.StatusCode, ErrorText(text)));
        }
        Logger.Information("Задание {JobDisplayId}: стартовый кадр {Path} загружен в ComfyUI",
            jobDisplayId, inputRel);
        // ComfyUI отвечает {"name":…,"subfolder":…,"type":"input"}; LoadImage ждёт имя
        // вместе с подкаталогом, если тот не пуст
        using var doc = JsonDocument.Parse(text);
        var uploaded = doc.RootElement.TryGetProperty("name", out var n) &&
                       n.ValueKind == JsonValueKind.String ? n.GetString()! : name;
        var subfolder = doc.RootElement.TryGetProperty("subfolder", out var sf) &&
                        sf.ValueKind == JsonValueKind.String ? sf.GetString()! : "";
        return subfolder.Length > 0 ? $"{subfolder}/{uploaded}" : uploaded;
    }

    /// <summary>Тип содержимого стартового кадра по расширению файла.</summary>
    private static string ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };

    /// <summary>Текст для подстановки внутрь JSON-строки шаблона (кавычки остаются в шаблоне);
    /// кириллица не эскейпится — промпт в workflow остаётся читаемым.</summary>
    private static string JsonEscape(string text)
    {
        var serialized = JsonSerializer.Serialize(text, DumpOptions);
        return serialized[1..^1]; // без обрамляющих кавычек
    }

    /// <summary>POST /prompt: поставить генерацию в очередь; возвращает prompt_id.</summary>
    private static async Task<string> SubmitPromptAsync(ModelProfile profile, string workflow,
        string clientId, CancellationToken ct)
    {
        var body = $$"""{"prompt": {{workflow}}, "client_id": "{{clientId}}"}""";
        using var response = await Http.PostAsync($"{BaseUrl(profile)}/prompt",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.comfyUiConnector.25", (int)response.StatusCode, ErrorText(text)));
        }
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("error", out var error) &&
            error.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.26", ErrorText(text)));
        }
        return doc.RootElement.TryGetProperty("prompt_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()!
            : throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.27", Preview(text)));
    }

    /// <summary>
    /// Ожидание результата: GET /history/{prompt_id} раз в несколько секунд, пока генерация
    /// не попадёт в историю (значит — завершена). Возвращает файлы результата из outputs.
    /// </summary>
    private static async Task<List<(string Filename, string Subfolder, string Type)>> WaitResultAsync(
        ModelProfile profile, string promptId, TimeSpan? timeout, CancellationToken ct)
    {
        // таймаут не задан («Таймаут ответа» исполнителя = 0, T-124) — ждём генерацию сколько
        // угодно: обрывает только кнопка «остановить»
        var deadline = timeout is { } limit ? DateTime.UtcNow + limit : DateTime.MaxValue;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await Http.GetAsync($"{BaseUrl(profile)}/history/{promptId}", ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty(promptId, out var entry))
                {
                    return ParseHistoryEntry(entry);
                }
            }
            await Task.Delay(PollDelay, ct);
        }
        throw new InvalidOperationException(
            Loc.T("msg.comfyUiConnector.28", AgentTimeout.Describe(timeout)));
    }

    /// <summary>Запись истории ComfyUI: ошибка генерации — исключение; успех — файлы результата.</summary>
    private static List<(string Filename, string Subfolder, string Type)> ParseHistoryEntry(JsonElement entry)
    {
        if (entry.TryGetProperty("status", out var status))
        {
            var statusStr = status.TryGetProperty("status_str", out var s) ? s.GetString() : null;
            if (string.Equals(statusStr, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.29") + HistoryError(status));
            }
        }
        var files = new List<(string, string, string)>();
        if (!entry.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Object)
        {
            return files;
        }
        // выходные файлы ищутся по всем узлам: любые массивы объектов с полем filename
        // (SaveVideo кладёт их в "images"/"video" в зависимости от версии ComfyUI)
        foreach (var node in outputs.EnumerateObject())
        {
            foreach (var prop in node.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                    {
                        files.Add((fn.GetString()!,
                            item.TryGetProperty("subfolder", out var sf) && sf.ValueKind == JsonValueKind.String
                                ? sf.GetString()! : "",
                            item.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String
                                ? tp.GetString()! : "output"));
                    }
                }
            }
        }
        return files;
    }

    /// <summary>
    /// Переливать консоль ComfyUI в консоль задания (ТЗ v1.45, todo37_3), пока идёт генерация:
    /// GET /internal/logs/raw отдаёт кольцевой буфер его вывода целиком — новые строки
    /// вычисляет ComfyLogTail. Эндпойнта нет (старая сборка ComfyUI) или он отвалился —
    /// пишем об этом одну строку и прекращаем: сама генерация от этого не страдает.
    /// </summary>
    private async Task PumpConsoleAsync(ModelProfile profile, string jobId, CancellationToken ct)
    {
        var tail = new ComfyLogTail();
        var reported = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await Http.GetAsync($"{BaseUrl(profile)}/internal/logs/raw", ct);
                if (!response.IsSuccessStatusCode)
                {
                    if (!reported)
                    {
                        reported = true;
                        _console.Write(jobId,
                            Loc.T("msg.comfyUiConnector.30", (int)response.StatusCode));
                    }
                    return;
                }
                foreach (var line in tail.Advance(ParseLogEntries(await response.Content.ReadAsStringAsync(ct))))
                {
                    _console.Write(jobId, line);
                }
            }
            catch (OperationCanceledException)
            {
                return; // задание завершилось или остановлено — это штатный конец опроса
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                if (!reported)
                {
                    reported = true;
                    _console.Write(jobId, Loc.T("msg.comfyUiConnector.31") + ex.Message + ")");
                }
                return;
            }
            try
            {
                await Task.Delay(ConsolePollDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Строки вывода из ответа /internal/logs/raw: {"entries":[{"t":"stdout","m":"…"}]}.</summary>
    public static List<string> ParseLogEntries(string json)
    {
        var lines = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }
        foreach (var entry in entries.EnumerateArray())
        {
            var text = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object when entry.TryGetProperty("m", out var m)
                                          && m.ValueKind == JsonValueKind.String => m.GetString(),
                _ => null,
            };
            if (text is null)
            {
                continue;
            }
            // прогресс-бар перерисовывается через «\r» — каждый кадр становится своей строкой
            lines.AddRange(text.Split('\n', '\r')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0));
        }
        return lines;
    }

    /// <summary>
    /// Скопировать готовый файл в папку проекта по указанию из описания задачи (todo37_3):
    /// возвращает путь относительно папки проекта либо null (указания нет / папка проекта
    /// не задана). Неудача копирования не валит задание — результат уже лежит в артефактах,
    /// о проблеме сообщается строкой консоли и в техническом логе.
    /// </summary>
    private string? CopyToProject(Core.Entities.Project? project, string? outputRel,
        string artifactRel, string jobId)
    {
        if (outputRel is null)
        {
            return null;
        }
        var folder = ProjectFolder(project);
        if (folder is null)
        {
            _console.Write(jobId,
                Loc.T("msg.comfyUiConnector.32", outputRel));
            return null;
        }
        try
        {
            var destination = Path.GetFullPath(Path.Combine(folder, outputRel));
            // страховка: копируем строго внутрь папки проекта
            if (!destination.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                _console.Write(jobId, Loc.T("msg.comfyUiConnector.33", outputRel));
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(_files.Abs(artifactRel), destination, overwrite: true);
            Logger.Information("Задание {JobId}: результат скопирован в папку проекта: {Path}", jobId, destination);
            _console.Write(jobId, Loc.T("msg.comfyUiConnector.34", outputRel));
            return outputRel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            Logger.Warning(ex, "Задание {JobId}: не удалось скопировать результат в папку проекта", jobId);
            _console.Write(jobId, Loc.T("msg.comfyUiConnector.35", outputRel, ex.Message));
            return null;
        }
    }

    /// <summary>Папка проекта на ЭТОМ сервере (ТЗ п. 2.7, гл. 6); null — не задана/не существует.</summary>
    private static string? ProjectFolder(Core.Entities.Project? project)
    {
        var folder = project?.FolderPath;
        return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? null : folder;
    }

    /// <summary>GET /view — скачать готовый файл из выходного каталога ComfyUI.</summary>
    private static async Task<byte[]> DownloadOutputAsync(ModelProfile profile, string filename,
        string subfolder, string type, CancellationToken ct)
    {
        var url = $"{BaseUrl(profile)}/view?filename={Uri.EscapeDataString(filename)}" +
                  $"&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
        using var response = await Http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.comfyUiConnector.36", filename, (int)response.StatusCode));
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private ModelProfile LoadProfile(Executor executor)
    {
        if (executor.ProfilePath.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.37", executor.Nick));
        }
        var json = _files.ReadText(executor.ProfilePath);
        if (json.Trim().Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.38", executor.ProfilePath));
        }
        return ModelProfile.Parse(json);
    }

    private static string BaseUrl(ModelProfile profile) =>
        (profile.BaseUrl.Length > 0 ? profile.BaseUrl : "http://127.0.0.1:8188").TrimEnd('/');

    /// <summary>Ошибки истории ComfyUI (status.messages) — в читаемый текст.</summary>
    private static string HistoryError(JsonElement status)
    {
        try
        {
            if (status.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                var texts = new List<string>();
                foreach (var message in messages.EnumerateArray())
                {
                    // сообщение — пара ["execution_error", {…детали…}]
                    if (message.ValueKind == JsonValueKind.Array && message.GetArrayLength() >= 2 &&
                        message[1].ValueKind == JsonValueKind.Object &&
                        message[1].TryGetProperty("exception_message", out var em))
                    {
                        texts.Add(em.GetString() ?? "");
                    }
                }
                if (texts.Count > 0)
                {
                    return string.Join("; ", texts.Where(t => t.Length > 0));
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IndexOutOfRangeException)
        {
            // неожиданный формат сообщений — вернём сырой статус ниже
        }
        return Preview(status.GetRawText());
    }

    /// <summary>Текст ошибки ComfyUI из тела ответа: error.message либо усечённое тело.</summary>
    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message))
                {
                    var text = message.GetString() ?? "";
                    // node_errors содержат конкретную причину (нет файла модели и т.п.)
                    if (doc.RootElement.TryGetProperty("node_errors", out var nodeErrors) &&
                        nodeErrors.ValueKind == JsonValueKind.Object && nodeErrors.GetRawText().Length > 2)
                    {
                        text += " " + Preview(nodeErrors.GetRawText());
                    }
                    return text;
                }
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // не JSON — вернём как есть
        }
        return Preview(body);
    }

    private static string Preview(string text) =>
        text.Length <= 500 ? text : text[..500] + "…";

    private string WriteAiFile(string? slug, string taskDisplayId, string fileName, string content)
    {
        var rel = Path.Combine(_files.TaskDirRel(slug, taskDisplayId), "ai", fileName);
        _files.WriteText(rel, content);
        return rel.Replace('\\', '/');
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
}
