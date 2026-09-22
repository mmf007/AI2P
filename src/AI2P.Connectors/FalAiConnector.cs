using AI2P.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
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
/// Коннектор ОБЛАЧНЫХ медиа-моделей через шлюз fal.ai (T-14-S0, отчёт T-213 §6.2–6.5) —
/// провайдер "fal-ai". До него всё облачное медиа (Seedance, Veo, Kling, GPT Image,
/// Nano Banana, ElevenLabs, Tripo, Meshy) не подключалось вовсе: своего
/// OpenAI-совместимого входа у этих моделей нет, а у каждой свой протокол. Шлюз
/// закрывает 600+ моделей ОДНИМ протоколом очереди и одним ключом.
///
/// Жизненный цикл — как у <see cref="ComfyUiConnector"/> (агентного цикла и инструментов
/// нет, промптом служит описание задачи, результат ложится в artifacts/ задачи и по
/// указанию — в папку проекта). Отличий три: тело запроса собирается из ШАБЛОНА В
/// ПРОФАЙЛЕ (<c>request</c>), а не из файла workflow — у каждой модели шлюза свой набор
/// полей; в каждый запрос идёт ключ API (<c>Authorization: Key …</c>); результат
/// приезжает не файлами на диске движка, а ССЫЛКАМИ, которые надо скачать.
///
/// Протокол очереди fal.ai: POST {baseUrl}/{model} → {request_id, status_url,
/// response_url, cancel_url} → поллинг GET status_url?logs=1 до COMPLETED →
/// GET response_url → в ответе объекты-файлы {url, content_type, file_name}.
/// Отмена — PUT cancel_url.
/// </summary>
public sealed class FalAiConnector : IAgentConnector
{
    public const string Provider = "fal-ai";

    /// <summary>Адрес очереди шлюза по умолчанию (профайл вправе назвать свой).</summary>
    public const string DefaultBaseUrl = "https://queue.fal.run";

    /// <summary>Генерация идёт минутами, но каждый ОТДЕЛЬНЫЙ запрос короткий: ожидание
    /// держится поллингом, а не открытым соединением (общий ограничитель — params.timeoutMinutes).</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions DumpOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Сколько файлов результата забирать максимум: у 3D-моделей в ответе кроме
    /// самой сетки лежат превью и текстуры, и качать их без предела незачем.</summary>
    private const int MaxOutputs = 8;

    private readonly JobService _jobs;
    private readonly TaskService _tasks;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly FileStore _files;
    private readonly EventStore _events;
    private readonly JobConsole _console;
    private readonly SecretStore _secrets;
    private readonly ObjectService? _objects;
    private readonly ObjectLoadService? _objectLoads;
    private readonly ChatService? _chat;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();

    /// <summary>Адрес отмены очереди по id задания — отмена работает и до начала счёта.</summary>
    private readonly ConcurrentDictionary<string, string> _queued = new();

    private static readonly ILogger Logger = Log.ForContext<FalAiConnector>();

    /// <param name="secrets">Файловое хранилище секретов — запасной источник ключа
    /// (ТЗ гл. 10): у установки без организации и в тестах <see cref="KeyResolver"/> не подставлен.</param>
    /// <param name="objects">Объекты проекта (T-259): ссылка <c>@obj:</c> разворачивается
    /// в паспорт персонажа — промптом медиа-модели служит только описание задачи.</param>
    /// <param name="objectLoads">Загрузка объектов в модель (T-14-S1): эталонный кадр и адаптер.</param>
    /// <param name="chat">Чат задачи: предложение замены, когда объект загрузить нечем.</param>
    public FalAiConnector(JobService jobs, TaskService tasks, ExecutorService executors,
        ProjectService projects, FileStore files, EventStore events, JobConsole console,
        SecretStore secrets, ObjectService? objects = null, ObjectLoadService? objectLoads = null,
        ChatService? chat = null)
    {
        _jobs = jobs;
        _tasks = tasks;
        _executors = executors;
        _projects = projects;
        _files = files;
        _events = events;
        _console = console;
        _secrets = secrets;
        _objects = objects;
        _objectLoads = objectLoads;
        _chat = chat;
    }

    /// <summary>
    /// Откуда брать ключ API (ТЗ гл. 10, этап 45): значение лежит в БД организации
    /// зашифрованным. Подставляется контекстом организации; не подставлен — читаем
    /// файловое хранилище, как раньше (тесты и установки без организации).
    /// </summary>
    public Func<string, (string? Value, string Source)>? KeyResolver { get; set; }

    public string Kind => Provider;

    public Task SubmitJobAsync(Job job, string requestText, CancellationToken ct = default)
    {
        _jobs.SetState(job.Id, JobState.Running, actorId: null);
        var cts = new CancellationTokenSource();
        _active[job.Id] = cts;
        _ = Task.Run(() => RunAsync(job, cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Живой фоновый вызов есть (T-117): иначе сторож зависших заданий перезапустил
    /// бы идущую генерацию поверх работающей — а она у шлюза ещё и платная.</summary>
    public bool HasActiveRun(string jobId) => _active.ContainsKey(jobId);

    public async Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (_active.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        // снять задание из очереди шлюза: пока оно не начало считаться, отмена бесплатна
        if (_queued.TryRemove(jobId, out var cancelUrl))
        {
            await TryCancelAsync(cancelUrl, ct);
        }
        Logger.Information("Задание {JobId} остановлено", jobId);
        _jobs.SetState(jobId, JobState.Cancelled, actorId: null);
    }

    /// <summary>
    /// Проба подключения (запуск работы команды, ТЗ v1.14): спрашиваем состояние заведомо
    /// несуществующего запроса. Отказ по авторизации (401/403) — ключ негоден, «не найдено» —
    /// шлюз отвечает и ключ принят. Настоящую генерацию проба не запускает: она платная.
    /// </summary>
    public async Task<string?> TestConnectionAsync(Executor executor, CancellationToken ct = default)
    {
        try
        {
            var profile = LoadProfile(executor);
            var key = ResolveKey(profile);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var url = $"{BaseUrl(profile)}/{profile.Model.Trim('/')}/requests/" +
                      "00000000-0000-0000-0000-000000000000/status";
            using var response = await SendAsync(HttpMethod.Get, url, key, content: null, timeout.Token);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                return Loc.T("msg.falAiConnector.1", (int)response.StatusCode);
            }
            Logger.Information("Проба подключения fal.ai: исполнитель {Nick} подключен ({Model})",
                executor.Nick, profile.Model);
            return null;
        }
        catch (OperationCanceledException)
        {
            return Loc.T("msg.falAiConnector.2");
        }
        catch (Exception ex)
        {
            return Loc.T("msg.falAiConnector.3", ex.Message);
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
            var key = ResolveKey(profile);
            // промпт генерации — описание задачи как есть, со ссылками @obj:, раскрытыми
            // в паспорта объектов (T-259): обвязка агентного промпта до медиа-модели не едет
            var rawDescription = _tasks.ReadDescription(task);
            var description = _objects is null
                ? rawDescription
                : _objects.Expand(task.ProjectId, rawDescription, kind => ObjectKinds.Title(kind));
            // ЗАГРУЗКА ОБЪЕКТОВ В МОДЕЛЬ (T-14-S1) — до постановки в очередь: у облачной
            // модели неверно собранное задание стоит денег, а персонаж на нём всё равно чужой.
            // Адаптеры LoRA шлюз не принимает вовсе (в справочнике у этих записей стоит
            // честное "supported": false), и план об этом скажет словами
            var plan = _objectLoads?.Plan(task.ProjectId, rawDescription, profile.Lora,
                profile.RefImage, ModelTitle(executor, profile), ProjectFolder(project),
                executor.ModelId, profile.RefAudio);
            if (plan is not null)
            {
                foreach (var note in plan.Notes)
                {
                    _console.Write(job.Id, note);
                }
                if (!plan.Ok)
                {
                    _chat?.Add(task.Id, job.ExecutorId, task.ResponsibleId,
                        _objectLoads!.ChatText(plan, task.ProjectId, job.ExecutorId));
                    throw new InvalidOperationException(ObjectLoadService.ErrorText(plan));
                }
            }
            // стартовый кадр разбирается ПЕРВЫМ (T-258), иначе строка «исходный файл …»
            // достанется разбору указания о результате по слову «файл»
            var (afterImage, inputRel) = MediaInputDirective.Parse(description);
            // эталонная запись (T-249-S0) — тем же разбором, своим набором расширений
            var (afterInput, audioRel) = MediaInputDirective.ParseAudio(afterImage);
            var (prompt, outputRel) = MediaOutputDirective.Parse(afterInput);
            if (prompt.Length == 0)
            {
                throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.5"));
            }
            if (plan is { Images.Count: > 0 })
            {
                inputRel = plan.Images[0].Path;
            }
            if (plan is { Audios.Count: > 0 })
            {
                audioRel = plan.Audios[0].Path;
            }

            var template = ReadRequestTemplate(profile);
            string? imageDataUri = null;
            if (NeedsStartImage(template, profile.RefImage))
            {
                if (inputRel is null)
                {
                    throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.39"));
                }
                // у шлюза нет каталога input: картинка уходит В САМОМ ЗАПРОСЕ адресом
                // data: — так её принимают все поля *_image_url (документировано у fal.ai)
                imageDataUri = ReadStartImage(project, inputRel, out var imageBytes);
                _console.Write(job.Id, Loc.T("msg.falAiConnector.4", inputRel, imageBytes));
            }
            else if (inputRel is not null)
            {
                // молчать об этом нельзя: со стороны вышло бы «картинку взяли, а персонаж другой»
                _console.Write(job.Id, Loc.T("msg.falAiConnector.23", inputRel));
            }
            // ЭТАЛОННАЯ ЗАПИСЬ (T-249-S0): у шлюза каталога input нет, поэтому запись уходит
            // в теле запроса адресом data: — ровно как картинка
            string? audioDataUri = null;
            if (NeedsRefAudio(template, profile.RefAudio))
            {
                if (audioRel is null)
                {
                    throw new InvalidOperationException(Loc.T("msg.comfyUiConnector.47"));
                }
                audioDataUri = ReadInputFile(project, audioRel, out var audioBytes);
                _console.Write(job.Id, Loc.T("msg.falAiConnector.26", audioRel, audioBytes));
            }
            else if (audioRel is not null)
            {
                _console.Write(job.Id, Loc.T("msg.comfyUiConnector.49", audioRel));
            }

            var seed = profile.MediaSeed ?? Random.Shared.NextInt64(0, uint.MaxValue);
            var body = BuildRequest(profile, prompt, seed, job.DisplayId, imageDataUri, audioDataUri);
            var endpoint = $"{BaseUrl(profile)}/{profile.Model.Trim('/')}";

            var startLine = Loc.T("msg.falAiConnector.5", profile.Model, seed, prompt.Length) +
                            (inputRel is null ? "" : Loc.T("msg.comfyUiConnector.43", inputRel)) +
                            (audioRel is null ? "" : Loc.T("msg.comfyUiConnector.50", audioRel)) +
                            (outputRel is null ? "" : Loc.T("msg.comfyUiConnector.7", outputRel));
            Logger.Information("Задание {JobDisplayId} по задаче {TaskDisplayId}: {Line}",
                job.DisplayId, task.DisplayId, startLine);
            _console.Write(job.Id, startLine);
            // тариф провайдера — в консоль и в сводку: у медиа-моделей счёт идёт не за токены,
            // а за картинку/секунду/минуту, и задание своей цены не знает (её ведёт fal.ai)
            var tariff = TariffOf(executor);
            if (tariff.Length > 0)
            {
                _console.Write(job.Id, Loc.T("msg.falAiConnector.6", tariff));
            }

            // полный запрос — файлом (п. 6.3): картинка в теле заменена пометкой, иначе
            // файл запроса распухал бы на мегабайты base64 и не читался бы глазами
            var requestDump = JsonSerializer.Serialize(new
            {
                provider = Kind,
                model = profile.Model,
                endpoint,
                seed,
                startImage = inputRel,
                refAudio = audioRel,
                prompt,
                request = JsonDocument.Parse(HideDataUris(body)).RootElement,
            }, DumpOptions);
            var requestPath = WriteAiFile(slug, task.DisplayId, $"{job.DisplayId}-request.json", requestDump);
            AppendEvent(task, job, EventTypes.AgentRequest, new
            {
                model = profile.Model,
                path = requestPath,
                baseUrl = BaseUrl(profile),
                seed,
                promptLength = prompt.Length,
                startImage = inputRel,
                outputFile = outputRel,
            });

            var queued = await SubmitAsync(endpoint, key, body, ct);
            _queued[job.Id] = queued.CancelUrl;
            Logger.Information("Задание {JobDisplayId}: поставлено в очередь fal.ai, request_id {RequestId}",
                job.DisplayId, queued.RequestId);
            _console.Write(job.Id, Loc.T("msg.falAiConnector.7", queued.RequestId));
            AppendEvent(task, job, EventTypes.JobQueued, new
            {
                requestId = queued.RequestId,
                baseUrl = BaseUrl(profile),
                model = profile.Model,
            });

            // «Таймаут ответа» исполнителя (T-124), а не задан — params.timeoutMinutes профайла
            var timeout = AgentTimeout.Of(executor.ResponseTimeoutMinutes,
                TimeSpan.FromMinutes(profile.MediaTimeoutMinutes));
            await WaitCompletedAsync(queued, key, timeout, job.Id, ct);
            var result = await FetchResultAsync(queued.ResponseUrl, key, ct);
            _queued.TryRemove(job.Id, out _);

            var outputs = CollectFiles(result);
            if (outputs.Count == 0)
            {
                throw new InvalidOperationException(Loc.T("msg.falAiConnector.8", Preview(result)));
            }
            var artifactPaths = new List<string>();
            foreach (var file in outputs.Take(MaxOutputs))
            {
                var bytes = await DownloadAsync(file.Url, key, ct);
                var rel = _files.WriteTaskArtifactBytes(slug, task.DisplayId,
                    $"{job.DisplayId}-{SafeName(file, artifactPaths.Count)}", bytes);
                artifactPaths.Add(rel);
                Logger.Information("Задание {JobDisplayId}: сохранён результат {Path} ({Size} байт)",
                    job.DisplayId, rel, bytes.Length);
                _console.Write(job.Id, Loc.T("msg.comfyUiConnector.9", Path.GetFileName(rel), bytes.Length));
            }
            // сам ответ шлюза — тоже артефакт: в нём остаются seed, длительность и адреса,
            // по которым файл ещё сутки лежит на стороне провайдера
            WriteAiFile(slug, task.DisplayId, $"{job.DisplayId}-response.json", result);

            var copiedTo = CopyToProject(project, outputRel, artifactPaths[0], job.Id);

            var summary = new StringBuilder();
            summary.AppendLine(Loc.T("msg.falAiConnector.9"));
            summary.AppendLine();
            summary.AppendLine(Loc.T("msg.comfyUiConnector.12", profile.Model));
            summary.AppendLine(Loc.T("msg.falAiConnector.10", queued.RequestId));
            summary.AppendLine(Loc.T("msg.comfyUiConnector.14", seed));
            summary.AppendLine(Loc.T("msg.comfyUiConnector.15", sw.Elapsed));
            if (tariff.Length > 0)
            {
                summary.AppendLine(Loc.T("msg.falAiConnector.11", tariff));
            }
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

            // цена задания остаётся нулём НАМЕРЕННО: счёт у шлюза идёт за картинку, секунду
            // видео или минуту звука, токенов в ответе нет вовсе, а выдуманная цифра в отчётах
            // хуже честного нуля. Тариф записан словами в сводке и в документе модели
            _jobs.SetState(job.Id, JobState.Done, actorId: job.ExecutorId, resultPath, cost: 0,
                inputTokens: 0, outputTokens: 0, "USD");
            if (!task.IsTemplate)
            {
                _tasks.ChangeStatus(task.Id, _tasks.AiDoneStatusOf(task.Id), actorId: job.ExecutorId);
            }
            AppendEvent(task, job, EventTypes.AgentResponse, new
            {
                model = profile.Model,
                files = artifactPaths,
                projectFile = copiedTo,
                requestId = queued.RequestId,
                seed,
                elapsedMs = sw.ElapsedMilliseconds,
            });
            Logger.Information("Задание {JobDisplayId} выполнено за {Elapsed}: файлов {Count}",
                job.DisplayId, sw.Elapsed, artifactPaths.Count);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.18", sw.Elapsed, artifactPaths.Count));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Logger.Information("Задание {JobDisplayId} отменено после {Elapsed}", job.DisplayId, sw.Elapsed);
            _console.Write(job.Id, Loc.T("msg.comfyUiConnector.19", sw.Elapsed));
        }
        catch (Exception exRaw)
        {
            var ex = exRaw is OperationCanceledException
                ? new InvalidOperationException(Loc.T("msg.falAiConnector.24", sw.Elapsed))
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

    // --- сборка запроса -----------------------------------------------------------------

    /// <summary>
    /// Собрать тело запроса из шаблона профайла: {prompt}/{negative}/{job} — текст внутри
    /// кавычек (JSON-экранируется), "{seed}"/"{width}"/"{height}"/"{length}"/"{steps}" —
    /// числа вместе с кавычками, плейсхолдер картинки — адрес data: со стартовым кадром.
    /// Правила ровно те же, что у workflow ComfyUI: одна механика на оба медиа-коннектора.
    /// </summary>
    public string BuildRequest(ModelProfile profile, string prompt, long seed,
        string jobDisplayId, string? imageDataUri = null, string? audioDataUri = null)
    {
        var template = ReadRequestTemplate(profile);
        var imagePlaceholder = ImagePlaceholder(profile.RefImage);
        var audioPlaceholder = AudioPlaceholder(profile.RefAudio);
        return template
            .Replace("{prompt}", JsonEscape(prompt))
            .Replace("{negative}", JsonEscape(profile.MediaNegative))
            .Replace("{job}", JsonEscape(jobDisplayId))
            .Replace(imagePlaceholder, JsonEscape(imageDataUri ?? ""))
            .Replace(audioPlaceholder, JsonEscape(audioDataUri ?? ""))
            .Replace("\"{seed}\"", seed.ToString())
            .Replace("\"{width}\"", profile.MediaWidth.ToString())
            .Replace("\"{height}\"", profile.MediaHeight.ToString())
            .Replace("\"{length}\"", profile.MediaLength.ToString())
            .Replace("\"{steps}\"", profile.MediaSteps.ToString());
    }

    /// <summary>Шаблон тела запроса; не задан — уходит один промпт (минимум всех моделей шлюза).</summary>
    private static string ReadRequestTemplate(ModelProfile profile) =>
        profile.Request.Trim().Length > 0 ? profile.Request : """{"prompt": "{prompt}"}""";

    /// <summary>Модель ждёт картинку: в шаблоне запроса есть её плейсхолдер (T-13-S1).</summary>
    public static bool NeedsStartImage(string template, RefImageSettings refImage) =>
        template.Contains(ImagePlaceholder(refImage), StringComparison.Ordinal);

    private static string ImagePlaceholder(RefImageSettings refImage) =>
        refImage.Placeholder.Length > 0 ? refImage.Placeholder : "{image}";

    /// <summary>Модель ждёт эталонную запись: в шаблоне запроса есть её плейсхолдер (T-249-S0).</summary>
    public static bool NeedsRefAudio(string template, RefAudioSettings refAudio) =>
        template.Contains(AudioPlaceholder(refAudio), StringComparison.Ordinal);

    private static string AudioPlaceholder(RefAudioSettings refAudio) =>
        refAudio.Placeholder.Length > 0 ? refAudio.Placeholder : "{audio}";

    private string ReadStartImage(Core.Entities.Project? project, string inputRel, out long size) =>
        ReadInputFile(project, inputRel, out size);

    /// <summary>
    /// Входной файл (стартовый кадр либо эталонная запись, T-249-S0) берётся В ПАПКЕ ПРОЕКТА
    /// по относительному пути (там же лежат эталонные файлы объектов, T-259) и превращается
    /// в адрес <c>data:</c>: своего хранилища у нас нет, а публиковать файл наружу ссылкой
    /// мы не вправе.
    /// </summary>
    private string ReadInputFile(Core.Entities.Project? project, string inputRel, out long size)
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
        var bytes = File.ReadAllBytes(source);
        size = bytes.LongLength;
        return $"data:{ContentTypeOf(source)};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".mp4" => "video/mp4",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            // остальные звуковые расширения (T-249-S0) — по общему списку
            _ => ObjectLoadPlanner.AudioMimeOf(path) ?? "image/png",
        };

    /// <summary>Текст внутрь JSON-строки шаблона (кавычки остаются в шаблоне); кириллица
    /// не эскейпится — промпт в файле запроса остаётся читаемым.</summary>
    private static string JsonEscape(string text)
    {
        var serialized = JsonSerializer.Serialize(text, DumpOptions);
        return serialized[1..^1];
    }

    /// <summary>Заменить длинные адреса data: пометкой — для файла запроса и логов.</summary>
    public static string HideDataUris(string body)
    {
        const string marker = "\"data:";
        var result = new StringBuilder(body.Length);
        var from = 0;
        while (true)
        {
            var start = body.IndexOf(marker, from, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(body, from, body.Length - from);
                return result.ToString();
            }
            var end = body.IndexOf('"', start + marker.Length);
            if (end < 0)
            {
                result.Append(body, from, body.Length - from);
                return result.ToString();
            }
            result.Append(body, from, start - from);
            var length = end - start - marker.Length;
            result.Append("\"data:… (").Append(length).Append(" b)\"");
            from = end + 1;
        }
    }

    // --- протокол очереди fal.ai --------------------------------------------------------

    /// <summary>Ответ шлюза на постановку в очередь.</summary>
    public sealed record QueuedRequest(string RequestId, string StatusUrl, string ResponseUrl,
        string CancelUrl);

    /// <summary>POST {baseUrl}/{model}: поставить генерацию в очередь.</summary>
    private static async Task<QueuedRequest> SubmitAsync(string endpoint, string key, string body,
        CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, endpoint, key, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.falAiConnector.12", (int)response.StatusCode, ErrorText(text)) +
                HintFor(response.StatusCode));
        }
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var id = Str(root, "request_id");
        if (id.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.falAiConnector.13", Preview(text)));
        }
        // адреса состояния и ответа шлюз присылает сам: у него свои поддомены и версии пути,
        // и собирать их по кусочкам — способ отстать от провайдера на ровном месте
        var statusUrl = Str(root, "status_url");
        var responseUrl = Str(root, "response_url");
        return new QueuedRequest(id,
            statusUrl.Length > 0 ? statusUrl : $"{endpoint}/requests/{id}/status",
            responseUrl.Length > 0 ? responseUrl : $"{endpoint}/requests/{id}",
            Str(root, "cancel_url"));
    }

    /// <summary>
    /// Ждать окончания: GET status_url?logs=1 раз в несколько секунд. Пока модель считает,
    /// её собственный вывод переливается в консоль задания — без него задание выглядит
    /// зависшим (та же беда, что решал PumpConsole у ComfyUI, todo37_3).
    /// </summary>
    private async Task WaitCompletedAsync(QueuedRequest queued, string key, TimeSpan? timeout,
        string jobId, CancellationToken ct)
    {
        var deadline = timeout is { } limit ? DateTime.UtcNow + limit : DateTime.MaxValue;
        var url = queued.StatusUrl + (queued.StatusUrl.Contains('?') ? "&" : "?") + "logs=1";
        var seenLogs = 0;
        var lastPosition = -1;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await SendAsync(HttpMethod.Get, url, key, content: null, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.falAiConnector.14", (int)response.StatusCode, ErrorText(text)));
            }
            using (var doc = JsonDocument.Parse(text))
            {
                var root = doc.RootElement;
                var status = Str(root, "status").ToUpperInvariant();
                foreach (var line in LogLines(root).Skip(seenLogs))
                {
                    _console.Write(jobId, line);
                    seenLogs++;
                }
                switch (status)
                {
                    case "COMPLETED":
                        return;
                    case "IN_QUEUE":
                        var position = root.TryGetProperty("queue_position", out var p) &&
                                       p.TryGetInt32(out var value) ? value : -1;
                        if (position >= 0 && position != lastPosition)
                        {
                            lastPosition = position;
                            _console.Write(jobId, Loc.T("msg.falAiConnector.15", position));
                        }
                        break;
                    case "IN_PROGRESS":
                        break;
                    default:
                        // ERROR и всё незнакомое: продолжать поллинг бессмысленно
                        throw new InvalidOperationException(
                            Loc.T("msg.falAiConnector.16", status, Preview(text)));
                }
            }
            await Task.Delay(PollDelay, ct);
        }
        // не дождались — СНИМАЕМ запрос с очереди: у облачного шлюза брошенное задание
        // продолжает считаться и продолжает стоить денег
        await TryCancelAsync(queued.CancelUrl, ct);
        throw new InvalidOperationException(
            Loc.T("msg.falAiConnector.25", AgentTimeout.Describe(timeout)));
    }

    /// <summary>Снять запрос с очереди шлюза; неудача сама по себе задание не валит.</summary>
    private static async Task TryCancelAsync(string cancelUrl, CancellationToken ct)
    {
        if (cancelUrl.Length == 0)
        {
            return;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, cancelUrl);
            await Http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger.Warning(ex, "Не удалось снять запрос с очереди fal.ai: {Url}", cancelUrl);
        }
    }

    /// <summary>Строки вывода модели из ответа состояния: logs = [{"message": "…"}].</summary>
    public static List<string> LogLines(JsonElement status)
    {
        var lines = new List<string>();
        if (!status.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }
        foreach (var entry in logs.EnumerateArray())
        {
            var text = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object when entry.TryGetProperty("message", out var m)
                                          && m.ValueKind == JsonValueKind.String => m.GetString(),
                _ => null,
            };
            if (text is null)
            {
                continue;
            }
            lines.AddRange(text.Split('\n', '\r')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0));
        }
        return lines;
    }

    /// <summary>GET response_url — сам результат генерации (JSON со ссылками на файлы).</summary>
    private static async Task<string> FetchResultAsync(string responseUrl, string key,
        CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, responseUrl, key, content: null, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.falAiConnector.17", (int)response.StatusCode, ErrorText(text)));
        }
        return text;
    }

    /// <summary>Файл результата: адрес, имя и тип содержимого (как их отдаёт шлюз).</summary>
    public sealed record OutputFile(string Url, string FileName, string ContentType);

    /// <summary>
    /// Собрать файлы результата: у каждой модели шлюза свой вид ответа (images[], video{},
    /// audio{}, model_mesh{}), но ФАЙЛ везде один и тот же объект — {url, content_type,
    /// file_name}. Поэтому ищем по дереву любые объекты со строковым url; поля вроде
    /// model_urls (просто строки) при этом не берутся, иначе один и тот же файл скачался
    /// бы дважды.
    /// </summary>
    public static List<OutputFile> CollectFiles(string resultJson)
    {
        var files = new List<OutputFile>();
        using var doc = JsonDocument.Parse(resultJson);
        Walk(doc.RootElement, files);
        return files;

        static void Walk(JsonElement element, List<OutputFile> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("url", out var url) &&
                        url.ValueKind == JsonValueKind.String && url.GetString()!.Length > 0)
                    {
                        into.Add(new OutputFile(url.GetString()!, Str(element, "file_name"),
                            Str(element, "content_type")));
                        return; // вложенных файлов у файла не бывает
                    }
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value, into);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, into);
                    }
                    break;
            }
        }
    }

    /// <summary>Имя артефакта: своё имя файла от шлюза, а нет его — по типу содержимого.</summary>
    public static string SafeName(OutputFile file, int index)
    {
        var name = Path.GetFileName(file.FileName.Trim());
        if (name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            return name;
        }
        var extension = ExtensionOf(file.ContentType, file.Url);
        return index == 0 ? $"result{extension}" : $"result-{index + 1}{extension}";
    }

    private static string ExtensionOf(string contentType, string url)
    {
        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "audio/mpeg" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "model/gltf-binary" => ".glb",
            _ => ExtensionFromUrl(url),
        };
    }

    private static string ExtensionFromUrl(string url)
    {
        var cut = url.IndexOfAny(['?', '#']);
        var path = cut < 0 ? url : url[..cut];
        var extension = Path.GetExtension(path);
        return extension.Length is > 1 and <= 6 &&
               extension.All(c => char.IsLetterOrDigit(c) || c == '.')
            ? extension.ToLowerInvariant()
            : ".bin";
    }

    /// <summary>
    /// Скачать файл результата. Ключ идёт и сюда: часть моделей отдаёт файлы со своего
    /// хранилища за авторизацией. Адрес <c>data:</c> (sync_mode у некоторых моделей)
    /// раскрывается на месте — ходить за ним никуда не надо.
    /// </summary>
    private static async Task<byte[]> DownloadAsync(string url, string key, CancellationToken ct)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma < 0)
            {
                throw new InvalidOperationException(Loc.T("msg.falAiConnector.18", Preview(url)));
            }
            return Convert.FromBase64String(url[(comma + 1)..]);
        }
        using var response = await SendAsync(HttpMethod.Get, url, key, content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                Loc.T("msg.falAiConnector.19", url, (int)response.StatusCode));
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Запрос к шлюзу с ключом. Заголовок именно «Key …» — так у fal.ai.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url,
        string key, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (key.Length > 0)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
        }
        // умолчание HttpCompletionOption вычитывает тело целиком — ответ живёт дольше запроса,
        // поэтому запрос здесь и освобождается
        return await Http.SendAsync(request, ct);
    }

    private static string HintFor(System.Net.HttpStatusCode code) =>
        code is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? " " + Loc.T("msg.falAiConnector.20")
            : code == System.Net.HttpStatusCode.TooManyRequests
                ? " " + Loc.T("msg.falAiConnector.21")
                : "";

    // --- вспомогательное ----------------------------------------------------------------

    /// <summary>Тариф провайдера словами из декларации возможностей (cost.per_unit + cost.unit):
    /// у медиа-моделей счёт идёт не за токены, и «цена задания» ответить на это не может.</summary>
    private string TariffOf(Executor executor)
    {
        try
        {
            if (executor.CapabilitiesPath.Length == 0)
            {
                return "";
            }
            var json = _files.ReadText(executor.CapabilitiesPath);
            if (json.Trim().Length == 0)
            {
                return "";
            }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("cost", out var cost) ||
                cost.ValueKind != JsonValueKind.Object ||
                !cost.TryGetProperty("per_unit", out var perUnit) ||
                !perUnit.TryGetDouble(out var value) || value <= 0)
            {
                return "";
            }
            return Loc.T("msg.falAiConnector.22", value, Str(cost, "unit"));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return "";
        }
    }

    private static string ModelTitle(Executor executor, ModelProfile profile) =>
        profile.Model.Length > 0 ? profile.Model : executor.Nick;

    /// <summary>Скопировать готовый файл в папку проекта по указанию из описания задачи.</summary>
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
            _console.Write(jobId, Loc.T("msg.comfyUiConnector.32", outputRel));
            return null;
        }
        try
        {
            var destination = Path.GetFullPath(Path.Combine(folder, outputRel));
            if (!destination.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                _console.Write(jobId, Loc.T("msg.comfyUiConnector.33", outputRel));
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(_files.Abs(artifactRel), destination, overwrite: true);
            Logger.Information("Задание {JobId}: результат скопирован в папку проекта: {Path}",
                jobId, destination);
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

    private static string? ProjectFolder(Core.Entities.Project? project)
    {
        var folder = project?.FolderPath;
        return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? null : folder;
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
        var profile = ModelProfile.Parse(json);
        if (profile.Model.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.aiConnectorBase.20"));
        }
        return profile;
    }

    /// <summary>Ключ API по ссылке профайла; нет ключа — понятная ошибка с местом, куда его класть.</summary>
    private string ResolveKey(ModelProfile profile)
    {
        if (profile.SecretRef.Trim().Length == 0)
        {
            return "";
        }
        var (value, source) = KeyResolver is not null
            ? KeyResolver(profile.SecretRef)
            : _secrets.ResolveWithSource(profile.SecretRef);
        if (value is null)
        {
            Logger.Warning("Ключ API не найден: secretRef {SecretRef}, {Source}", profile.SecretRef, source);
            throw new InvalidOperationException(
                Loc.T("msg.aiConnectorBase.22", profile.SecretRef,
                    _secrets.LocationOf(profile.SecretRef), SecretStore.EnvNameOf(profile.SecretRef)));
        }
        return value;
    }

    private static string BaseUrl(ModelProfile profile) =>
        (profile.BaseUrl.Length > 0 ? profile.BaseUrl : DefaultBaseUrl).TrimEnd('/');

    /// <summary>Текст ошибки шлюза из тела ответа: detail/error/message либо усечённое тело.</summary>
    public static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "detail", "error", "message" })
                {
                    if (!root.TryGetProperty(name, out var value))
                    {
                        continue;
                    }
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString()!;
                    }
                    // detail валидации — массив объектов {loc, msg, type}: без разбора
                    // человек видит «[object Object]» и не понимает, какое поле не то
                    if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        return Preview(value.GetRawText());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // не JSON — вернём как есть
        }
        return Preview(body);
    }

    private static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim()
            : "";

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
