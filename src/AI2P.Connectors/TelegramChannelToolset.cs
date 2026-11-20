using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// НАСТРОЙКА ПУБЛИКАЦИИ В СВОЙ TELEGRAM-КАНАЛ (T-827-S0) на этом сервере: токен бота
/// (@BotFather) из <c>secrets/plugin.telegram.channel.token.json</c> и список РАЗРЕШЁННЫХ
/// каналов из config.json (<c>plugins["telegram.channel"].channels</c>). Оба пер-серверные и
/// не реплицируются — как ключи сайтов SaaS (T-567-S0).
/// </summary>
/// <param name="Token">Токен бота; бот — администратор канала с правом публикации.</param>
/// <param name="Channels">Разрешённые каналы: <c>@имя</c> или числовой id (<c>-100…</c>).</param>
public sealed record TelegramChannelConfig(string Token, IReadOnlyList<string> Channels)
{
    /// <summary>Код «плагина» — для config.json, ссылки на секрет и правил «плагины и MCP».</summary>
    public const string PluginCode = "telegram.channel";

    /// <summary>Ссылка на секрет токена бота.</summary>
    public static string SecretRef => PluginCodes.SecretRef(PluginCode);
}

/// <summary>
/// ИНСТРУМЕНТ АГЕНТА <c>telegram_channel_post</c> (T-827-S0): пост в СВОЙ канал проекта через
/// Bot API — <c>sendMessage</c> (parse_mode=HTML, до 4096 знаков) или <c>sendPhoto</c> с подписью
/// (до 1024 знаков, multipart). Чужие тематические чаты остаются человеку: канал обязан быть
/// в списке разрешённых, «любой чат, куда добавлен бот» не годится. Текст и картинка — файлы
/// ПАПКИ ПРОЕКТА под правилами доступа к каталогам. Ответ — ссылка на сообщение
/// <c>https://t.me/&lt;канал&gt;/&lt;message_id&gt;</c> (у закрытого канала по id — t.me/c/…).
/// </summary>
public sealed class TelegramChannelToolset
{
    public const string PostTool = "telegram_channel_post";

    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(StringComparer.Ordinal) { PostTool };

    public const int MaxText = 4096;
    public const int MaxCaption = 1024;

    private static readonly string[] ImageExts = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly TelegramChannelConfig _config;
    private readonly string? _projectFolder;
    private readonly Func<string, string, bool> _isPathPermitted;
    private readonly HttpClient _http;

    /// <summary>Адрес Bot API; подменяется проверками.</summary>
    public string ApiBase { get; set; } = "https://api.telegram.org";

    public string? Language { get; set; }

    public List<string> CallLog { get; } = [];

    public TelegramChannelToolset(TelegramChannelConfig config, string? projectFolder,
        Func<string, string, bool>? isPathPermitted = null, HttpClient? http = null)
    {
        _config = config;
        _projectFolder = projectFolder;
        _isPathPermitted = isPathPermitted ?? ((_, _) => true);
        _http = http ?? SharedHttp;
    }

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Публикуется только при токене, хотя бы одном канале и папке проекта:
    /// инструмент, который заведомо откажет, — потраченный ход агента.</summary>
    public bool CanUse => _config.Token.Length > 0 && _config.Channels.Count > 0
                          && _projectFolder is { Length: > 0 };

    public bool Handles(string name) => CanUse && Names.Contains(name);

    /// <summary>Описание — из справочника действий (AI2P.Telegram.ChannelPost), здесь схема.</summary>
    public IEnumerable<FileToolset.ToolSpec> Specs
    {
        get
        {
            if (!CanUse)
            {
                yield break;
            }
            yield return new(PostTool, "Post to the project's own Telegram channel.",
                """{"type":"object","properties":{"channel":{"type":"string"},"path":{"type":"string"},"image":{"type":"string"}},"required":["channel","path"]}""");
        }
    }

    public async Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        string result;
        try
        {
            result = name == PostTool
                ? await PostAsync(args, ct)
                : Loc.In(Language, "prompt.tools.5", name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                       or TaskCanceledException or IOException or JsonException
                                       or UnauthorizedAccessException)
        {
            result = Loc.In(Language, "prompt.telegram.1", ex.Message);
        }
        CallLog.Add($"{name}({Str(args, "channel")}, {Str(args, "path")}) → {Preview(result)}");
        return result;
    }

    private async Task<string> PostAsync(JsonElement args, CancellationToken ct)
    {
        var channel = Normalize(Str(args, "channel"));
        var allowed = _config.Channels.Select(Normalize)
            .FirstOrDefault(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase));
        if (channel.Length == 0 || allowed is null)
        {
            return Loc.In(Language, "prompt.telegram.2", channel,
                string.Join(", ", _config.Channels.Select(Normalize)));
        }
        var textPath = ProjectFile(Str(args, "path"));
        var text = (await File.ReadAllTextAsync(textPath, ct)).Trim();
        if (text.Length == 0)
        {
            return Loc.In(Language, "prompt.telegram.3", Str(args, "path"));
        }
        var image = Str(args, "image");
        HttpResponseMessage response;
        if (image.Length > 0)
        {
            var imagePath = ProjectFile(image);
            if (!ImageExts.Contains(Path.GetExtension(imagePath).ToLowerInvariant()))
            {
                return Loc.In(Language, "prompt.telegram.4", image);
            }
            if (text.Length > MaxCaption)
            {
                return Loc.In(Language, "prompt.telegram.5", text.Length, MaxCaption);
            }
            using var form = new MultipartFormDataContent();
            form.Add(Part("chat_id", allowed));
            form.Add(Part("caption", text));
            form.Add(Part("parse_mode", "HTML"));
            var file = new ByteArrayContent(await File.ReadAllBytesAsync(imagePath, ct));
            file.Headers.ContentType = new MediaTypeHeaderValue(Mime(imagePath));
            // .NET по умолчанию не берёт имя части в кавычки — Bot API так тоже понимает,
            // но кавычки ставим явно, как в загрузке ComfyUI (T-258)
            file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"photo\"",
                FileName = "\"" + Path.GetFileName(imagePath) + "\"",
            };
            form.Add(file);
            response = await _http.PostAsync(Method("sendPhoto"), form, ct);
        }
        else
        {
            if (text.Length > MaxText)
            {
                return Loc.In(Language, "prompt.telegram.5", text.Length, MaxText);
            }
            var body = new JsonObject
            {
                ["chat_id"] = allowed,
                ["text"] = text,
                ["parse_mode"] = "HTML",
            };
            response = await _http.PostAsync(Method("sendMessage"),
                new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);
        }
        using (response)
        {
            var answer = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(answer.Length == 0 ? "{}" : answer);
            var root = doc.RootElement;
            if (!response.IsSuccessStatusCode
                || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : answer;
                return Loc.In(Language, "prompt.telegram.6", (int)response.StatusCode, desc);
            }
            var result = root.GetProperty("result");
            var messageId = result.GetProperty("message_id").GetInt64();
            var username = result.TryGetProperty("chat", out var chat)
                           && chat.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            return Loc.In(Language, "prompt.telegram.7", LinkOf(allowed, username, messageId));
        }
    }

    /// <summary>Ссылка на сообщение: публичный канал — t.me/&lt;имя&gt;/&lt;id&gt;, закрытый по
    /// числовому id -100XXXX — t.me/c/XXXX/&lt;id&gt;.</summary>
    public static string LinkOf(string channel, string username, long messageId)
    {
        if (username.Length > 0)
        {
            return $"https://t.me/{username}/{messageId}";
        }
        if (channel.StartsWith('@'))
        {
            return $"https://t.me/{channel[1..]}/{messageId}";
        }
        var id = channel.StartsWith("-100", StringComparison.Ordinal) ? channel[4..] : channel.TrimStart('-');
        return $"https://t.me/c/{id}/{messageId}";
    }

    /// <summary>«имя», «@имя», «t.me/имя», «https://t.me/имя» → «@имя»; числовой id как есть.</summary>
    public static string Normalize(string channel)
    {
        var c = channel.Trim();
        foreach (var prefix in new[] { "https://t.me/", "http://t.me/", "t.me/" })
        {
            if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                c = c[prefix.Length..].TrimEnd('/');
            }
        }
        if (c.Length == 0 || c.StartsWith('@') || long.TryParse(c, out _))
        {
            return c;
        }
        return "@" + c;
    }

    private string ProjectFile(string rel)
    {
        if (_projectFolder is not { Length: > 0 })
        {
            throw new InvalidOperationException(Loc.In(Language, "prompt.telegram.8"));
        }
        var root = Path.GetFullPath(_projectFolder);
        var abs = Path.GetFullPath(Path.Combine(root, rel));
        var rootSlash = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (rel.Trim().Length == 0 || Path.IsPathRooted(rel) || !abs.StartsWith(rootSlash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.In(Language, "prompt.telegram.9", rel));
        }
        if (!_isPathPermitted(abs, "read"))
        {
            throw new UnauthorizedAccessException(Loc.In(Language, "prompt.telegram.9", rel));
        }
        if (!File.Exists(abs))
        {
            throw new IOException(Loc.In(Language, "prompt.telegram.10", rel));
        }
        return abs;
    }

    private string Method(string method) => $"{ApiBase.TrimEnd('/')}/bot{_config.Token}/{method}";

    private static StringContent Part(string name, string value)
    {
        var part = new StringContent(value, System.Text.Encoding.UTF8);
        part.Headers.ContentType = null;
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + name + "\"" };
        return part;
    }

    private static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };

    private static string Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static string Preview(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
