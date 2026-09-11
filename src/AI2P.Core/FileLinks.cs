namespace AI2P.Core;

/// <summary>Разобранная ссылка на файл, который отдаёт САМ AI2P (T-142).</summary>
/// <param name="Kind">Вид эндпойнта: <see cref="FileLinks.Raw"/> или <see cref="FileLinks.Project"/>.</param>
/// <param name="ProjectId">Проект — только у ссылок вида <see cref="FileLinks.Project"/>.</param>
/// <param name="Path">Путь файла: у <c>raw</c> — от каталога данных, у <c>project</c> —
/// от каталога проекта (каталог на каждом сервере свой, а путь внутри него одинаков).</param>
public sealed record FileLink(string Kind, string ProjectId, string Path);

/// <summary>
/// НАШИ ССЫЛКИ НА ФАЙЛЫ (ТЗ гл. 11, T-142).
///
/// Ссылку на файл в тексте задания пишет ИИ-агент, и строит он её от ВНЕШНЕГО имени
/// своего сервера (<c>http://мойпк:5480/ai2p/org/api/files/project?…</c>). Такая ссылка
/// годится, чтобы отправить её наружу, но не годится для повседневной работы: человек,
/// сидящий на этом же сервере, уходил бы по ней в сеть за файлом, который лежит у него
/// на диске, — а файлы бывают в гигабайты.
///
/// Разбор ссылки нужен, чтобы понять, наш ли это файл и где он лежит ЗДЕСЬ. Важно, что
/// путь в ссылке — <b>относительный</b> (от каталога данных или от каталога проекта):
/// каталоги проекта на разных серверах разные, а путь внутри — один и тот же, поэтому
/// та же ссылка на соседнем сервере покажет его собственную копию файла.
/// </summary>
public static class FileLinks
{
    /// <summary>Файл хранилища AI2P (каталог данных): артефакты задания, вставки редактора.</summary>
    public const string Raw = "raw";

    /// <summary>Файл в каталоге ПРОЕКТА: то, что создал агент своими инструментами.</summary>
    public const string Project = "project";

    private const string RawPath = "api/files/raw";
    private const string ProjectPath = "api/files/project";

    /// <summary>Разобрать ссылку; null — ссылка не наша (внешний сайт, <c>file:</c>, почта…).
    /// Адрес может быть и относительным, и полным — с именем любого сервера кластера:
    /// эндпойнт у всех один, и отвечает на него тот сервер, к которому обратились.</summary>
    public static FileLink? Parse(string? url)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }
        var kind = value.Contains(ProjectPath, StringComparison.OrdinalIgnoreCase) ? Project
            : value.Contains(RawPath, StringComparison.OrdinalIgnoreCase) ? Raw
            : null;
        if (kind is null)
        {
            return null;
        }
        var path = Param(value, "path");
        if (path.Length == 0)
        {
            return null;
        }
        var projectId = Param(value, "projectId");
        return kind == Project && projectId.Length == 0 ? null : new FileLink(kind, projectId, path);
    }

    /// <summary>Относительный адрес того же файла на ЭТОМ сервере: браузер достроит его
    /// адресом, по которому человек уже подключён (базовый путь страницы — организация).</summary>
    public static string Relative(FileLink link) =>
        link.Kind == Project
            ? $"{ProjectPath}?projectId={Uri.EscapeDataString(link.ProjectId)}" +
              $"&path={Uri.EscapeDataString(link.Path)}"
            : $"{RawPath}?path={Uri.EscapeDataString(link.Path)}";

    /// <summary>Полный адрес того же файла по внешнему имени сервера — ссылка «наружу».</summary>
    public static string External(FileLink link, string? publicBaseUrl) =>
        $"{(publicBaseUrl ?? "").TrimEnd('/')}/{Relative(link)}";

    /// <summary>Значение параметра запроса; пусто — параметра нет или он нечитаем.</summary>
    private static string Param(string url, string name)
    {
        var at = url.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return "";
        }
        // «path=» встречается и как хвост другого имени (projectId=…&path=…) — здесь это
        // не мешает: перед именем всегда «?» или «&», а иных параметров у нас нет
        var encoded = url[(at + name.Length + 1)..];
        var amp = encoded.IndexOfAny(['&', '#']);
        if (amp >= 0)
        {
            encoded = encoded[..amp];
        }
        try
        {
            return Uri.UnescapeDataString(encoded).Trim();
        }
        catch (UriFormatException)
        {
            return "";
        }
    }
}
