using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// ПУБЛИКАЦИЯ СТАТЬИ НА DEV.TO (T-826-S0) — работа за ролью <see cref="PluginAction.RolePublish"/>
/// плагина <c>publish.devto</c>. Forem API: новая статья — <c>POST /api/articles</c>, та же
/// статья повторно — <c>PUT /api/articles/{id}</c>; ключ API уходит заголовком <c>api-key</c>.
///
/// <para>ОТКУДА БЕРЁТСЯ id ПОВТОРНОЙ ПУБЛИКАЦИИ: из шапки (front matter) самого файла —
/// после первого успешного вызова туда дописываются <c>devto_id</c> и <c>devto_url</c>.
/// Наружу эти строки не уходят: перед отправкой они вырезаются из шапки, иначе Forem
/// принял бы их за свои поля.</para>
///
/// <para>ФЛАГ published БЕРЁТСЯ ИЗ ВЫЗОВА, А НЕ ИЗ ФАЙЛА: Forem читает шапку статьи раньше
/// поля запроса, и строка «published: true», оставленная в файле автором, опубликовала бы
/// черновик молча. Поэтому строка published в отправляемой шапке переписывается значением
/// флага (умолчание — false, черновик).</para>
///
/// <para>КАРТИНКИ — ТОЛЬКО АДРЕСАМИ: загрузки файлов API dev.to не принимает, поэтому
/// cover_image и картинки в тексте обязаны быть публичными https-ссылками.</para>
/// </summary>
internal static class DevToPublish
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Служебные поля шапки, которые пишем МЫ и которые наружу не уходят.</summary>
    private static readonly string[] OwnKeys = ["devto_id", "devto_url"];

    public static async Task<string> RunAsync(GatewayToolset tools, GatewayPlugin plugin,
        JsonElement args, CancellationToken ct)
    {
        var lang = tools.Language;
        var connection = plugin.Manifest.Connection!;
        var file = Str(args, "file");
        if (file.Length == 0)
        {
            return Loc.In(lang, "prompt.devto.1");
        }
        var abs = tools.Source(file, out var refusal);
        if (abs is null)
        {
            return refusal;
        }
        var secretRef = PluginCodes.SecretRef(plugin.Manifest.Code, connection.SecretRef);
        var key = (plugin.Secret?.Invoke(secretRef) ?? "").Trim();
        if (key.Length == 0)
        {
            return Loc.In(lang, "prompt.devto.2", plugin.Manifest.Code);
        }
        var published = args.ValueKind == JsonValueKind.Object
                        && args.TryGetProperty("published", out var p)
                        && p.ValueKind == JsonValueKind.True;

        var text = (await File.ReadAllTextAsync(abs, ct)).Replace("\r\n", "\n");
        if (!Split(text, out var head, out var body)
            || !head.Any(l => KeyOf(l) == "title" && ValueOf(l).Length > 0))
        {
            return Loc.In(lang, "prompt.devto.3", file);
        }
        var id = head.Where(l => KeyOf(l) == "devto_id").Select(ValueOf).FirstOrDefault() ?? "";
        var outHead = head.Where(l => !OwnKeys.Contains(KeyOf(l))).ToList();
        SetKey(outHead, "published", published ? "true" : "false");
        var markdown = "---\n" + string.Join("\n", outHead) + "\n---\n" + body;

        var payload = JsonSerializer.Serialize(new
        {
            article = new { body_markdown = markdown, published },
        });
        var baseUrl = connection.Url.TrimEnd('/');
        var update = long.TryParse(id, out var articleId) && articleId > 0;
        using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post,
            update ? $"{baseUrl}/articles/{articleId}" : $"{baseUrl}/articles")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(connection.SecretHeader, key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.forem.api-v1+json"));
        // без User-Agent dev.to отвечает 403
        request.Headers.UserAgent.ParseAdd("AI2P/1.0");
        using var response = await (tools.Http ?? Shared).SendAsync(request, ct);
        var answer = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Loc.In(lang, "prompt.devto.4", (int)response.StatusCode,
                answer.Length <= 600 ? answer : answer[..600] + "…");
        }
        using var doc = JsonDocument.Parse(answer);
        var root = doc.RootElement;
        var newId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : id;
        var url = root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
            ? urlEl.GetString() ?? ""
            : "";

        // id и адрес — в шапку файла, чтобы следующий вызов обновил ту же статью
        var note = "";
        if (tools.Target("", file, out _) is { } writable)
        {
            var newHead = head.Where(l => !OwnKeys.Contains(KeyOf(l))).ToList();
            SetKey(newHead, "published", published ? "true" : "false");
            newHead.Add("devto_id: " + newId);
            if (url.Length > 0)
            {
                newHead.Add("devto_url: " + url);
            }
            await File.WriteAllTextAsync(writable,
                "---\n" + string.Join("\n", newHead) + "\n---\n" + body,
                new UTF8Encoding(false), ct);
        }
        else
        {
            note = Loc.In(lang, "prompt.devto.8");
        }
        return Loc.In(lang, "prompt.devto.5", url, newId,
            Loc.In(lang, published ? "prompt.devto.6" : "prompt.devto.7"),
            Loc.In(lang, update ? "prompt.devto.9" : "prompt.devto.10"), note).Trim();
    }

    /// <summary>Разделить файл на строки шапки и тело. false — шапки «---…---» нет.</summary>
    internal static bool Split(string text, out List<string> head, out string body)
    {
        head = [];
        body = text;
        var t = text.TrimStart('﻿');
        if (!t.StartsWith("---\n", StringComparison.Ordinal))
        {
            return false;
        }
        var end = t.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }
        head = [.. t[4..end].Split('\n')];
        var rest = t[(end + 4)..];
        body = rest.StartsWith('\n') ? rest[1..] : rest;
        return true;
    }

    internal static string KeyOf(string line)
    {
        var colon = line.IndexOf(':');
        return colon <= 0 || line.StartsWith(' ') ? "" : line[..colon].Trim().ToLowerInvariant();
    }

    internal static string ValueOf(string line)
    {
        var colon = line.IndexOf(':');
        return colon < 0 ? "" : line[(colon + 1)..].Trim().Trim('"', '\'');
    }

    private static void SetKey(List<string> head, string key, string value)
    {
        var i = head.FindIndex(l => KeyOf(l) == key);
        if (i >= 0)
        {
            head[i] = key + ": " + value;
        }
        else
        {
            head.Add(key + ": " + value);
        }
    }

    private static string Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim()
            : "";
}
