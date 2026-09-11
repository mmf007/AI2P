using AI2P.Core.Api;

namespace AI2P.Core;

/// <summary>
/// ФАЙЛЫ ПАПКИ ПРОЕКТА (T-264): разбор относительного пути и содержимое каталога для
/// диалога выбора эталонного файла объекта.
///
/// Путь везде ОТНОСИТЕЛЬНЫЙ папке проекта и с разделителем «/» — ровно в таком виде он
/// хранится у объекта и стоит в наших ссылках на файлы (<see cref="FileLinks"/>): каталоги
/// проекта на разных серверах кластера разные, а путь внутри — один и тот же.
///
/// Здесь же и единственная проверка «не вышли ли за край папки проекта»: и отдача файла
/// наружу, и список, и превью спрашивают её одним и тем же методом — второй такой проверки,
/// которая разойдётся с первой при первой же правке, быть не должно.
/// </summary>
public static class ProjectFiles
{
    /// <summary>Сколько записей одного вида отдаём за раз: в папке проекта бывают десятки
    /// тысяч файлов, а список выбора рисуется целиком.</summary>
    public const int Limit = 500;

    /// <summary>Расширения, которые мы беремся показать картинкой (превью формы объекта
    /// и значок в списке файлов).</summary>
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"];

    /// <summary>
    /// Абсолютный путь внутри папки проекта; null — папки нет на этом сервере либо путь
    /// уводит за её край (<c>../</c>, другой диск, абсолютный путь). Пустой путь — сама
    /// папка проекта.
    /// </summary>
    public static string? Resolve(string? folderPath, string? relPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }
        var root = Path.GetFullPath(folderPath);
        string abs;
        try
        {
            abs = string.IsNullOrWhiteSpace(relPath) ? root : Path.GetFullPath(Path.Combine(root, relPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // путь с недопустимыми знаками вписывают руками — это не повод падать
            return null;
        }
        return abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(abs, root, StringComparison.OrdinalIgnoreCase)
            ? abs
            : null;
    }

    /// <summary>
    /// ГОДИТСЯ ЛИ ПУТЬ ДЛЯ ССЫЛКИ НА ФАЙЛ ПАПКИ ПРОЕКТА (T-13-S0): непустой, относительный
    /// и не уводящий вверх. Проверка нужна там, где папки проекта на ЭТОМ сервере нет вовсе,
    /// а файл всё равно надо спросить у соседа по кластеру: <see cref="Resolve"/> там
    /// ответить не может — ему не от чего считать, — и без отдельной проверки наружу уехал
    /// бы любой путь, включая «C:\» и «../».
    /// </summary>
    public static bool IsRelative(string? relPath)
    {
        var value = (relPath ?? "").Trim();
        if (value.Length == 0 || Path.IsPathRooted(value) || value.Contains(':'))
        {
            return false;
        }
        var parts = value.Replace('\\', '/').Split('/');
        return !parts.Contains("..") && !value.StartsWith('/');
    }

    /// <summary>Путь относительно папки проекта с разделителем «/»; сама папка — пустая строка.</summary>
    public static string Rel(string root, string abs)
    {
        var rel = Path.GetRelativePath(root, abs).Replace('\\', '/');
        return rel == "." ? "" : rel;
    }

    /// <summary>Картинка ли это по расширению. Хвост адреса («?…», «#…») отбрасывается:
    /// тем же методом форма решает, показывать ли превью удалённого адреса.</summary>
    public static bool IsImage(string? path)
    {
        var value = (path ?? "").Split('?', '#')[0];
        var ext = Path.GetExtension(value).ToLowerInvariant();
        return Array.IndexOf(ImageExtensions, ext) >= 0;
    }

    /// <summary>
    /// Содержимое каталога внутри папки проекта: подкаталоги и файлы. null — папки проекта
    /// нет, путь уводит за её край или это вообще не каталог. Скрытые и системные записи
    /// не показываются, как в выборе папки проекта.
    /// </summary>
    public static ProjectDirListDto? List(string? folderPath, string? relPath)
    {
        var abs = Resolve(folderPath, relPath);
        if (abs is null || !Directory.Exists(abs))
        {
            return null;
        }
        var root = Path.GetFullPath(folderPath!);
        var listing = new ProjectDirListDto
        {
            Path = Rel(root, abs),
            Root = root,
            // выше папки проекта не поднимаемся: путь объекта живёт только внутри неё
            Parent = string.Equals(abs, root, StringComparison.OrdinalIgnoreCase)
                ? null
                : Rel(root, Path.GetDirectoryName(abs)!),
        };
        try
        {
            var dir = new DirectoryInfo(abs);
            foreach (var sub in dir.EnumerateDirectories()
                         .Where(Visible)
                         .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (listing.Dirs.Count >= Limit)
                {
                    listing.Truncated = true;
                    break;
                }
                listing.Dirs.Add(new ProjectEntryDto { Name = sub.Name, Path = Rel(root, sub.FullName) });
            }
            foreach (var file in dir.EnumerateFiles()
                         .Where(Visible)
                         .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (listing.Files.Count >= Limit)
                {
                    listing.Truncated = true;
                    break;
                }
                listing.Files.Add(new ProjectEntryDto
                {
                    Name = file.Name,
                    Path = Rel(root, file.FullName),
                    IsImage = IsImage(file.Name),
                    Size = file.Length,
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // каталог без прав чтения — показываем пустым, как в выборе папки проекта
        }
        return listing;
    }

    /// <summary>
    /// Сведения о файле папки проекта — по ним форма объекта решает, показывать ли превью.
    /// null — папки нет или путь уводит за её край.
    /// </summary>
    public static ProjectFileInfoDto? Info(string? folderPath, string relPath)
    {
        var abs = Resolve(folderPath, relPath);
        if (abs is null)
        {
            return null;
        }
        var isDir = Directory.Exists(abs);
        var file = isDir ? null : new FileInfo(abs);
        return new ProjectFileInfoDto
        {
            Path = relPath,
            IsDirectory = isDir,
            Exists = isDir || file!.Exists,
            IsImage = file is { Exists: true } && IsImage(file.Name),
            Size = file is { Exists: true } ? file.Length : 0,
        };
    }

    private static bool Visible(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
