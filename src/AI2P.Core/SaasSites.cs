using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI2P.Core;

/// <summary>
/// САЙТ В SaaS-РЕДАКТОРЕ (T-567-S0): одна строка списка сайтов плагина вида <c>saas.*</c>
/// (Wix сегодня, другие конструкторы сайтов потом). Список лежит в config.json ЭТОГО сервера
/// (раздел <c>plugins.&lt;код&gt;.sites</c>), а ключ сайта — в <c>secrets/</c>
/// (<see cref="SecretRef"/>): ключ пер-серверный, как токен любого подключения MCP, и
/// значит список, к которому ключи привязаны, живёт там же.
///
/// <para><see cref="Code"/> — короткое имя сайта, по нему агент выбирает сайт, и по нему же
/// пишутся правила безопасности: <c>saas.wix:ai2p:*</c>. Поэтому имя постоянное и без
/// пробелов: смена имени означала бы молча слетевшее правило.</para>
/// </summary>
public sealed class SaasSite
{
    /// <summary>Короткое имя сайта: <c>ai2p</c>. Латиница, цифры, «-» и «_».</summary>
    public string Code { get; set; } = "";

    /// <summary>Название для человека.</summary>
    public string Name { get; set; } = "";

    /// <summary>Адрес сайта (для человека и агента; к вызовам API отношения не имеет).</summary>
    public string Url { get; set; } = "";

    /// <summary>Идентификатор сайта у SaaS (у Wix — Site ID, GUID из адреса панели).</summary>
    public string SiteId { get; set; } = "";

    /// <summary>Идентификатор аккаунта у SaaS (у Wix — Account ID, нужен ключу API).</summary>
    public string AccountId { get; set; } = "";

    /// <summary>Заметка человека: «тестовый», «не публиковать без меня».</summary>
    public string Note { get; set; } = "";

    private static readonly Regex CodeRule = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$");

    /// <summary>Годное короткое имя: оно попадает в правило безопасности через двоеточие,
    /// поэтому двоеточия, звёздочки и пробелы в нём недопустимы.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodeRule.IsMatch(code);

    /// <summary>Ссылка на ключ сайта в хранилище секретов этого сервера.</summary>
    public static string SecretRef(string pluginCode, string siteCode) =>
        "plugin." + pluginCode.Trim() + ".site." + siteCode.Trim().ToLowerInvariant() + ".token";

    /// <summary>Подставить поля сайта в шаблон заголовка: <c>{accountId}</c>, <c>{siteId}</c>,
    /// <c>{url}</c>, <c>{code}</c>.</summary>
    public string Fill(string template) =>
        template.Replace("{accountId}", AccountId.Trim())
            .Replace("{siteId}", SiteId.Trim())
            .Replace("{url}", Url.Trim())
            .Replace("{code}", Code.Trim());
}

/// <summary>
/// УНИВЕРСАЛЬНЫЕ ОПЕРАЦИИ НАД САЙТОМ (T-567-S0) — одни на все SaaS. Ими пишутся правила
/// безопасности (<c>saas.wix:ai2p:media</c>), а вызов конкретного инструмента SaaS относится
/// к операции правилами манифеста (<see cref="SaasSpec.Classify"/>). Инструмент, который
/// манифест не распознал, — <see cref="Api"/>: самая широкая операция, и закрыть её правилом
/// значит закрыть всё непонятное разом.
/// </summary>
public static class SaasOps
{
    /// <summary>Только чтение: списки, документация, GET-запросы.</summary>
    public const string Read = "read";
    /// <summary>Страницы и содержимое: страницы, блог, CMS-коллекции, меню.</summary>
    public const string Pages = "pages";
    /// <summary>Дизайн: тема, стили, правка сайта целиком.</summary>
    public const string Design = "design";
    /// <summary>Файлы: загрузка и удаление картинок, видео, документов.</summary>
    public const string Media = "media";
    /// <summary>SEO: заголовки, описания, переадресации, карта сайта, разметка.</summary>
    public const string Seo = "seo";
    /// <summary>Публикация сайта.</summary>
    public const string Publish = "publish";
    /// <summary>Всё остальное — любой вызов, который правила манифеста не распознали.</summary>
    public const string Api = "api";

    public static readonly string[] All = [Read, Pages, Design, Media, Seo, Publish, Api];

    public static bool IsKnown(string? op) => op is not null && Array.IndexOf(All, op) >= 0;
}

/// <summary>Правило отнесения вызова к операции: имя инструмента (маска с <c>*</c>) и,
/// при желании, регулярное выражение по тексту аргументов вызова.</summary>
public sealed class SaasRule
{
    public string Op { get; set; } = SaasOps.Api;
    public List<string> Tools { get; set; } = [];
    public string Args { get; set; } = "";

    public bool Matches(string tool, string argsJson)
    {
        if (!Tools.Any(t => Glob(t, tool)))
        {
            return false;
        }
        if (Args.Length == 0)
        {
            return true;
        }
        try
        {
            return Regex.IsMatch(argsJson, Args, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return false; // кривое выражение в манифесте — правило просто не срабатывает
        }
    }

    private static bool Glob(string mask, string value)
    {
        mask = mask.Trim();
        if (mask == "*")
        {
            return true;
        }
        var regex = "^" + Regex.Escape(mask).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    internal static SaasRule? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var rule = new SaasRule
        {
            Op = JsonRead.Str(e, "op").ToLowerInvariant(),
            Tools = JsonRead.Strings(e, "tools"),
            Args = JsonRead.Str(e, "args"),
        };
        return SaasOps.IsKnown(rule.Op) && rule.Tools.Count > 0 ? rule : null;
    }
}

/// <summary>
/// БЛОК <c>saas</c> МАНИФЕСТА ПЛАГИНА MCP (T-567-S0): плагин ведёт к среде редактирования
/// сайтов, и агент работает с ним УНИВЕРСАЛЬНЫМИ действиями <c>site_list</c> /
/// <c>site_tools</c> / <c>site_call</c>, а не своим набором инструментов на каждый SaaS.
/// <code>
/// "saas": {
///   "code": "wix",               // имя SaaS для агента
///   "siteArg": "siteId",         // аргумент инструмента, в который подставляется сайт
///   "rules": [ { "op": "media", "tools": ["CallWixSiteAPI"], "args": "site-media" }, … ]
/// }
/// </code>
/// </summary>
public sealed class SaasSpec
{
    /// <summary>Имя SaaS для агента и правил: <c>wix</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>Аргумент инструмента с идентификатором сайта: <c>siteId</c>. Подставляется
    /// из списка сайтов; чужое значение в вызове — отказ, иначе правило «только сайт ai2p»
    /// обходилось бы одной подменой аргумента.</summary>
    public string SiteArg { get; set; } = "siteId";

    /// <summary>Правила отнесения вызовов к операциям; первое совпавшее решает.</summary>
    public List<SaasRule> Rules { get; set; } = [];

    /// <summary>Выкладка файлов сайта из папки проекта (T-582-S0); null — SaaS этого не умеет.</summary>
    public SaasUpload? Upload { get; set; }

    /// <summary>Операция вызова: первое подходящее правило, иначе <see cref="SaasOps.Api"/>.</summary>
    public string Classify(string tool, string argsJson) =>
        Rules.FirstOrDefault(r => r.Matches(tool, argsJson))?.Op ?? SaasOps.Api;

    internal static SaasSpec? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var spec = new SaasSpec
        {
            Code = JsonRead.Str(v, "code").ToLowerInvariant(),
            SiteArg = JsonRead.Str(v, "siteArg", "siteId"),
            Upload = SaasUpload.From(v),
        };
        if (v.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            spec.Rules = [.. rules.EnumerateArray().Select(SaasRule.From).Where(r => r is not null)
                .Select(r => r!)];
        }
        return spec.Code.Length > 0 ? spec : null;
    }
}

/// <summary>
/// ВЫКЛАДКА ФАЙЛОВ САЙТА (T-582-S0): блок <c>saas.upload</c> манифеста. Сайт «из файлов»
/// (у Wix — headless-сайт с адресом *.wix-site-host.com) принимает новые файлы только
/// загрузкой multipart, а инструменты MCP шлют JSON — поэтому выкладку делает AI2P сам,
/// прямым запросом с ключом сайта.
/// <code>
/// "upload": {
///   "url": "https://…/headless-business/{siteId}/drop?campaign=mcp&amp;agent=ai2p",
///   "part": "files",             // имя каждой части; filename — путь от корня сайта
///   "op": "publish",             // операция для правил безопасности
///   "skip": ["wix.config.json"], // файлы, которые не выкладываются
///   "index": "index.html",       // без него выкладка — отказ до запроса
///   "maxBytes": 20971520
/// }
/// </code>
/// ЗАГРУЗКА ЗАМЕНЯЕТ ВЕСЬ НАБОР ФАЙЛОВ: не попавший в папку файл с сайта пропадёт.
/// </summary>
public sealed class SaasUpload
{
    /// <summary>Адрес загрузки; <c>{siteId}</c>, <c>{accountId}</c> подставляются из сайта.</summary>
    public string Url { get; set; } = "";
    public string Part { get; set; } = "files";
    public string Op { get; set; } = SaasOps.Publish;
    public List<string> Skip { get; set; } = [];
    public string Index { get; set; } = "";
    public long MaxBytes { get; set; } = 20L * 1024 * 1024;

    internal static SaasUpload? From(JsonElement saas)
    {
        if (!saas.TryGetProperty("upload", out var v) || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var upload = new SaasUpload
        {
            Url = JsonRead.Str(v, "url"),
            Part = JsonRead.Str(v, "part", "files"),
            Op = JsonRead.Str(v, "op", SaasOps.Publish).ToLowerInvariant(),
            Skip = JsonRead.Strings(v, "skip"),
            Index = JsonRead.Str(v, "index"),
        };
        if (v.TryGetProperty("maxBytes", out var max) && max.TryGetInt64(out var bytes) && bytes > 0)
        {
            upload.MaxBytes = bytes;
        }
        if (!SaasOps.IsKnown(upload.Op))
        {
            upload.Op = SaasOps.Publish;
        }
        return upload.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? upload : null;
    }
}
