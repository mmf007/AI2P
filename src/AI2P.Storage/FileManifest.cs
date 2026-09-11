using System.Security.Cryptography;
using AI2P.Core.Api;

namespace AI2P.Storage;

/// <summary>
/// Манифест реплицируемого каталога (ТЗ гл. 6, п. 7 плана; этап 44): путь, размер, время
/// изменения и sha256 каждого файла. Им серверы сравнивают своё содержимое с чужим —
/// добор недостающего идёт кусками с докачкой, как у загрузки моделей.
///
/// Хэш считается ЛЕНИВО. У медиа-каталога (папка <c>Common</c> — ради него всё и делается)
/// файлы измеряются гигабайтами, и хэшировать их каждый сеанс нельзя. Поэтому известный
/// по прошлой синхронизации хэш переиспользуется, пока совпадают размер и время изменения,
/// а считается заново только у изменившихся файлов.
/// </summary>
public static class FileManifest
{
    /// <summary>Каталоги, которые не реплицируются никогда (ТЗ гл. 6, разд. 7 плана):
    /// корзина, журналы запросов к ИИ и служебные файлы самой БД.
    ///
    /// АРХИВЫ (<c>arc</c>) — здесь с T-203-S0. Каталог архивов лежит ВНУТРИ каталога данных
    /// организации, и общая репликация файлов забирала его целиком, хотя у архивов свой
    /// механизм переноса (база — видом обмена <c>arc:&lt;id&gt;</c>, файлы проектов — видом
    /// <c>arcdata</c>, и только у ТЕКУЩЕГО архива). Из-за этого:
    /// <list type="bullet">
    /// <item>файлы текущего архива ехали ДВАЖДЫ, двумя разными базами сравнения;</item>
    /// <item>закрытие архива на одном сервере (каталог → .zip) выглядело для партнёра как
    /// «человек удалил файлы» и ТИХО ВЫЧИЩАЛО у него содержимое каталога архива, оставляя
    /// пустые папки и базу <c>ai2p.db</c> (она в <see cref="NeverFiles"/>, поэтому не
    /// удаляется) — то есть открытый архив у партнёра терял данные;</item>
    /// <item>рядом приезжал <c>&lt;код&gt;.zip</c>, и на партнёре оказывались сразу каталог
    /// и .zip — состояние, которого в замысле нет (жалоба T-203-S0);</item>
    /// <item>архив, удалённый человеком с этого сервера, репликация заводила обратно, хотя
    /// возвращать его положено отдельной кнопкой «скачать с другого сервера» (T-47-S0).</item>
    /// </list>
    /// </summary>
    private static readonly string[] NeverDirs = [".trash", "ai", AI2P.Core.Archives.Dir];

    /// <summary>Файлы, которые не реплицируются никогда: сама база и её журналы WAL —
    /// они едут журналом изменений строк (п. 6.1), а не побайтно.</summary>
    private static readonly string[] NeverFiles = ["ai2p.db", "ai2p.db-wal", "ai2p.db-shm",
        "server.db", "server.db-wal", "server.db-shm"];

    /// <summary>
    /// СЛУЖЕБНЫЙ ПУТЬ каталога данных организации — тот, который <see cref="Scan"/> при
    /// <c>skipService</c> не возвращает никогда: корзина, журналы запросов к ИИ, архивы,
    /// сама база и её журналы. Проверяется КАЖДЫЙ сегмент пути — ровно так же, как это
    /// делает обход каталога.
    ///
    /// Нужно на приёме (T-203-S0): манифест присылает ПАРТНЁР, и партнёр прежней версии
    /// назовёт в нём то, чего у нас в манифесте уже нет (пути внутри <c>arc</c>). Без этой
    /// проверки сверка увидела бы «у меня файла нет, у него есть, база сравнения его знает»
    /// и удалила бы файлы архива У ПАРТНЁРА — то есть обновлённый сервер вычистил бы архивы
    /// у необновлённого.
    /// </summary>
    public static bool IsServicePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            // не последний сегмент — это КАТАЛОГ, последний — имя файла: сравниваем с тем же
            // списком, с каким сверяется обход, иначе файл «arc» в корне считался бы служебным
            if ((!last && NeverDirs.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                || (last && NeverFiles.Contains(parts[i], StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Обойти каталог и собрать записи БЕЗ хэшей (путь, размер, время). Хэши проставляются
    /// отдельно (<see cref="FillHashes"/>) — так их считают только для изменившихся файлов.
    /// </summary>
    /// <param name="root">Корень реплицируемого каталога; не существует — пустой список.</param>
    /// <param name="ignore">Фильтр <c>.repignore</c>; исключённый каталог не просматривается.</param>
    /// <param name="skipService">Отсекать служебные каталоги и файлы AI2P (каталог данных
    /// организации); для папки <c>Common</c> пользователя — false, там всё принадлежит ему.</param>
    public static List<FileEntry> Scan(string root, RepIgnore ignore, bool skipService)
    {
        var entries = new List<FileEntry>();
        if (!Directory.Exists(root))
        {
            return entries;
        }
        Walk(root, root, ignore, skipService, entries);
        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }

    private static void Walk(string root, string dir, RepIgnore ignore, bool skipService,
        List<FileEntry> entries)
    {
        IEnumerable<string> files;
        IEnumerable<string> dirs;
        try
        {
            files = Directory.EnumerateFiles(dir);
            dirs = Directory.EnumerateDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;   // каталог исчез или закрыт правами — пропускаем, а не роняем сеанс
        }
        foreach (var file in files)
        {
            var rel = Rel(root, file);
            if (skipService && NeverFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (ignore.IsIgnored(rel))
            {
                continue;
            }
            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            entries.Add(new FileEntry
            {
                Path = rel,
                Size = info.Length,
                Mtime = Sql.ToDb(info.LastWriteTimeUtc),
            });
        }
        foreach (var sub in dirs)
        {
            var rel = Rel(root, sub);
            var name = Path.GetFileName(sub);
            if (skipService && NeverDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            // исключённый каталог не просматривается вовсе: иначе на каталоге сборки
            // фильтр обошёл бы десятки тысяч файлов ради того, чтобы их выбросить
            if (ignore.IsIgnored(rel, isDirectory: true))
            {
                continue;
            }
            Walk(root, sub, ignore, skipService, entries);
        }
    }

    /// <summary>
    /// Проставить хэши: известный по прошлой синхронизации берётся как есть, если совпали
    /// размер и время изменения, иначе считается заново. Это и есть та экономия, ради которой
    /// манифест разделён на два шага.
    /// </summary>
    /// <param name="known">(путь, размер, время) → хэш из базы сравнения; null — неизвестен.</param>
    public static void FillHashes(string root, List<FileEntry> entries,
        Func<string, long, string, string?> known)
    {
        foreach (var entry in entries)
        {
            entry.Hash = known(entry.Path, entry.Size, entry.Mtime)
                         ?? Hash(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    /// <summary>sha256 файла в hex; файл недоступен — пустая строка (он выпадет из сравнения).</summary>
    public static string Hash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 1 << 20, useAsync: false);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>Относительный путь от корня прямыми слэшами — таким он едет по сети
    /// и таким лежит в базе сравнения (переносимость между Windows и Linux).</summary>
    public static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// Абсолютный путь внутри корня по относительному из манифеста — с проверкой выхода
    /// наружу через «..». Партнёр называет пути сам, и без проверки он мог бы записать файл
    /// куда угодно на этом компьютере (ТЗ гл. 12).
    /// </summary>
    public static string? Resolve(string root, string relativePath)
    {
        if (relativePath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return null;
        }
        var full = Path.GetFullPath(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }
}
