using System.Text.Json;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// Профайл подключения модели из справочника (ТЗ п. 2.9, файл models/profile_&lt;ID&gt;.json):
/// провайдер, id модели у провайдера, ссылка на ключ в секретах, параметры вызова.
/// Коннекторы этапа 2 читают настройки отсюда (ТЗ гл. 15, этап 2).
/// </summary>
public sealed class ModelProfile
{
    /// <summary>Провайдер: "anthropic" (этап 2.1), "openai-compatible" (этап 2.2), …</summary>
    public string Provider { get; set; } = "";

    /// <summary>Транспорт подключения (todo29): "api" (по умолчанию, все старые записи) —
    /// HTTP API провайдера; "cli" — локальный CLI-агент (Claude Code), дешевле за счёт подписки.</summary>
    public string Transport { get; set; } = "api";

    /// <summary>Подключение по CLI? (transport == "cli", todo29).</summary>
    public bool IsCli => Transport.Equals("cli", StringComparison.OrdinalIgnoreCase);

    /// <summary>Команда запуска CLI при transport "cli": исполняемый файл + постоянные флаги
    /// (например "claude --permission-mode acceptEdits"); пусто — "claude" из PATH.</summary>
    public string CliCommand { get; set; } = "";

    /// <summary>Id модели у провайдера: "claude-fable-5", "claude-opus-4-8", …</summary>
    public string Model { get; set; } = "";

    /// <summary>Базовый URL API; пусто — стандартный URL провайдера.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Ссылка на ключ в секретах: "anthropic.apiKey" (см. SecretStore).
    /// Пусто — сервер без авторизации (локальный llama-server/Ollama).</summary>
    public string SecretRef { get; set; } = "";

    /// <summary>Команда запуска локального сервера модели при старте работы команды
    /// (например "…\llama-server.exe -m …gguf --port 8080 …"); пусто — ничего не запускать.
    /// Процесс останавливается кнопкой «остановить» работу команды (по запомненному pid).</summary>
    public string LaunchCommand { get; set; } = "";

    // --- params (параметры вызова) ---

    /// <summary>Путь workflow-шаблона ComfyUI относительно dataDir (провайдер comfyui,
    /// ТЗ v1.41): API-формат POST /prompt с плейсхолдерами {prompt}, {negative},
    /// "{seed}", "{width}", "{height}", "{length}", "{steps}", {job}.</summary>
    public string Workflow { get; set; } = "";

    /// <summary>
    /// ШАБЛОН ТЕЛА ЗАПРОСА облачного медиа-шлюза (провайдер fal-ai, T-14-S0, ключ
    /// <c>request</c> профайла) — сырой JSON с теми же плейсхолдерами, что у workflow
    /// ComfyUI: {prompt}, {negative}, {job}, {image} и числовые "{seed}", "{width}",
    /// "{height}", "{length}", "{steps}". Живёт в САМОМ профайле, а не отдельным файлом:
    /// у каждой из 600+ моделей шлюза свой набор полей («duration», «resolution»,
    /// «image_urls»), и это ровно та настройка, которую человек правит в форме модели.
    /// Пусто — телом уйдёт один промпт (<c>{"prompt": …}</c>).
    /// </summary>
    public string Request { get; set; } = "";

    /// <summary>Максимум токенов ответа (params.maxTokens).</summary>
    public int MaxTokens { get; set; } = 16000;

    // --- медиа-параметры (провайдер comfyui, ТЗ v1.41) ---

    /// <summary>Ширина кадра (params.width).</summary>
    public int MediaWidth { get; set; } = 768;

    /// <summary>Высота кадра (params.height).</summary>
    public int MediaHeight { get; set; } = 512;

    /// <summary>Число кадров (params.length): 121 ≈ 5 с при 24 fps.</summary>
    public int MediaLength { get; set; } = 121;

    /// <summary>Число шагов диффузии (params.steps).</summary>
    public int MediaSteps { get; set; } = 50;

    /// <summary>Negative prompt (params.negative).</summary>
    public string MediaNegative { get; set; } = "";

    /// <summary>Таймаут генерации в минутах (params.timeoutMinutes): на слабом GPU — часы.</summary>
    public int MediaTimeoutMinutes { get; set; } = 180;

    /// <summary>
    /// Фиксированный seed генерации (params.seed, T-258): null — случайный на каждое задание,
    /// как было до T-258. Число — этот же seed у всех кадров: половина одинаковости
    /// персонажа берётся именно отсюда (разбор T-251, п. 2.2), вторая половина — стартовый
    /// кадр. Ноль означает «случайный»: seed = 0 у ComfyUI ничем не лучше остальных, а
    /// пустое поле в форме профайла даёт именно ноль.
    /// </summary>
    public long? MediaSeed { get; set; }

    /// <summary>Уровень усилий (params.effort): low / medium / high / xhigh / max; пусто — по умолчанию провайдера.</summary>
    public string? Effort { get; set; }

    /// <summary>Модели подстраховки при отказе классификаторов (params.fallbacks, Claude Fable 5).</summary>
    public List<string> Fallbacks { get; set; } = [];

    /// <summary>
    /// Настройка работы с адаптером LoRA (T-13-S1, ключ <c>lora</c>): поддерживается ли,
    /// чем подключается, куда кладётся обученный файл. Отсюда коннектор узнаёт, КАК грузить
    /// адаптер объекта проекта в модель (T-14-S1).
    /// </summary>
    public LoraSettings Lora { get; set; } = new();

    /// <summary>
    /// Настройка референсной картинки (T-13-S1, ключ <c>refImage</c>): чем эталонный кадр
    /// попадает в модель — заливкой в движок, полем запроса или ничем.
    /// </summary>
    public RefImageSettings RefImage { get; set; } = new();

    /// <summary>
    /// Настройка референсного АУДИО (T-249-S0, ключ <c>refAudio</c>): чем образец голоса
    /// или звука попадает в модель. У аудио-моделей это и есть замена адаптера — постоянный
    /// тембр даётся записью, а не обучением.
    /// </summary>
    public RefAudioSettings RefAudio { get; set; } = new();

    /// <summary>
    /// Настройка МОДЕЛИ-СУФЛЁРА (T-286-S0, ключ <c>prompter</c>): нужен ли модели суфлёр и
    /// какие поля несёт его управляющий json. Коннектору отсюда нужна СХЕМА (T-287-S0): по
    /// ней значение приводится к виду поля узла — «90» в поле duration уходит числом, а
    /// значение не из перечня не уходит вовсе.
    /// </summary>
    public PrompterSettings Prompter { get; set; } = new();

    public static ModelProfile Parse(string json)
    {
        var profile = new ModelProfile();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        profile.Provider = Str(root, "provider");
        var transport = Str(root, "transport");
        profile.Transport = transport.Length > 0 ? transport : "api";
        profile.CliCommand = Str(root, "cliCommand");
        profile.Model = Str(root, "model");
        profile.BaseUrl = Str(root, "baseUrl");
        profile.SecretRef = Str(root, "secretRef");
        profile.LaunchCommand = Str(root, "launchCommand");
        profile.Workflow = Str(root, "workflow");
        // тело запроса облачного шлюза (T-14-S0): объект как есть, без разбора по полям —
        // поля у каждой модели свои, а подставлять в него надо только плейсхолдеры
        profile.Request = root.TryGetProperty("request", out var request) &&
                          request.ValueKind == JsonValueKind.Object
            ? request.GetRawText()
            : "";
        // работа с адаптером и с картинкой — из справочника моделей (T-13-S1): профайл
        // читается коннектором один раз, и настройка едет вместе с остальным подключением
        profile.Lora = LoraSettings.FromProfile(root);
        profile.RefImage = RefImageSettings.FromProfile(root);
        profile.RefAudio = RefAudioSettings.FromProfile(root);
        profile.Prompter = PrompterSettings.FromProfile(root);

        if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("maxTokens", out var mt) && mt.TryGetInt32(out var maxTokens) && maxTokens > 0)
            {
                profile.MaxTokens = maxTokens;
            }
            // медиа-параметры (провайдер comfyui, ТЗ v1.41)
            profile.MediaWidth = Int(p, "width", profile.MediaWidth);
            profile.MediaHeight = Int(p, "height", profile.MediaHeight);
            profile.MediaLength = Int(p, "length", profile.MediaLength);
            profile.MediaSteps = Int(p, "steps", profile.MediaSteps);
            profile.MediaTimeoutMinutes = Int(p, "timeoutMinutes", profile.MediaTimeoutMinutes);
            if (p.TryGetProperty("seed", out var sd) && sd.TryGetInt64(out var mediaSeed) && mediaSeed > 0)
            {
                profile.MediaSeed = mediaSeed;
            }
            if (p.TryGetProperty("negative", out var neg) && neg.ValueKind == JsonValueKind.String)
            {
                profile.MediaNegative = neg.GetString()!;
            }
            var effort = Str(p, "effort");
            profile.Effort = effort.Length > 0 ? effort : null;
            if (p.TryGetProperty("fallbacks", out var fb) && fb.ValueKind == JsonValueKind.Array)
            {
                profile.Fallbacks = fb.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }
        return profile;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim()
            : "";

    private static int Int(JsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var value) && value > 0
            ? value
            : fallback;
}
