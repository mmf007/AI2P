namespace AI2P.UI.Services;

/// <summary>
/// ПАРАМЕТРЫ ВСТРОЕННОГО РЕЖИМА (T-507-S0, плагин VS Code 8.2). Страница AI2P показывается
/// в iframe панели VS Code по адресу <c>…/?embed=vscode&amp;view=project|settings|docs|env
/// &amp;theme=dark|light&amp;lang=ru&amp;project=&lt;id&gt;</c> (или projectId=&lt;id&gt; — так шлёт плагин) либо <c>…/task/&lt;id или код&gt;?embed=vscode…</c>.
/// Разбор один на UI (признак в <see cref="UiState"/>) и сервер (заголовки встраивания
/// и заглушка вместо формы входа), поэтому он чистая функция без зависимостей.
/// </summary>
public sealed record EmbedParams(string View, string? Theme, string? Lang, string? ProjectId)
{
    public const string ViewProject = "project";
    public const string ViewSettings = "settings";
    public const string ViewDocs = "docs";
    public const string ViewEnv = "env";

    /// <summary>Значение параметра embed, которое включает режим. Других встраивающих
    /// хозяев пока нет; незнакомое значение режим НЕ включает.</summary>
    public const string HostVsCode = "vscode";

    /// <summary>Признак встроенного режима по значению параметра embed.</summary>
    public static bool IsEmbed(string? embed) =>
        string.Equals(embed?.Trim(), HostVsCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Разобрать адрес страницы (абсолютный или относительный); null — адрес не
    /// встроенного режима. Вид по умолчанию — project, неизвестная тема и язык не из
    /// <paramref name="languages"/> отбрасываются (тогда остаются сохранённая тема и язык
    /// установки).</summary>
    public static EmbedParams? Parse(string uri, IEnumerable<string> languages)
    {
        var q = uri.IndexOf('?');
        if (q < 0)
        {
            return null;
        }
        var end = uri.IndexOf('#', q);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (end < 0 ? uri[(q + 1)..] : uri[(q + 1)..end]).Split('&'))
        {
            var eq = pair.IndexOf('=');
            var key = Unescape(eq < 0 ? pair : pair[..eq]);
            // повтор параметра — берётся первый, как у QueryHelpers.ParseQuery().First()
            if (key.Length > 0 && !query.ContainsKey(key))
            {
                query[key] = eq < 0 ? "" : Unescape(pair[(eq + 1)..]);
            }
        }
        return Parse(key => query.TryGetValue(key, out var v) ? v : null, languages);
    }

    private static string Unescape(string part) => Uri.UnescapeDataString(part.Replace('+', ' '));

    /// <summary>То же по готовой коллекции параметров запроса (сервер).</summary>
    public static EmbedParams? Parse(Func<string, string?> get, IEnumerable<string> languages)
    {
        if (!IsEmbed(get("embed")))
        {
            return null;
        }
        var view = (get("view") ?? "").Trim().ToLowerInvariant() switch
        {
            ViewSettings => ViewSettings,
            ViewDocs => ViewDocs,
            ViewEnv => ViewEnv,
            _ => ViewProject,
        };
        var theme = (get("theme") ?? "").Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "light" => "light",
            _ => null,
        };
        var lang = (get("lang") ?? "").Trim();
        var known = languages.FirstOrDefault(l => string.Equals(l, lang, StringComparison.OrdinalIgnoreCase));
        // плагин VS Code шлёт projectId (panels.ts), а не project: без второго имени вид
        // project молча открывал текущий проект человека, а не проект папки (T-540-S0)
        var project = (get("project") ?? "").Trim();
        if (project.Length == 0)
        {
            project = (get("projectId") ?? "").Trim();
        }
        return new EmbedParams(view, theme, known, project.Length > 0 ? project : null);
    }
}
