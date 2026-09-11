using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AI2P.Core;
using Serilog;

namespace AI2P.Server.Org;

/// <summary>
/// ФАЙЛ, НА КОТОРЫЙ ССЫЛАЮТСЯ ЗАДАЧА ИЛИ ЧАТ, ЕДЕТ ВСЕМ СЕРВЕРАМ САМ (T-13-S0, повторный
/// заход).
///
/// В первой редакции T-13-S0 сервер, у которого файла нет, спрашивал его у соседа ЗВОНКОМ
/// (<see cref="RemoteFileService"/>). На стенде из двух серверов на одной машине это
/// работает, а у заказчика — нет, и причина не в коде: звонить можно не всякому. Рядовой
/// сервер сплошь и рядом стоит за NAT и назван петлевым адресом (ссылки его агента так и
/// выходят — <c>http://localhost:5480/…</c>); дирижёр до него не дозванивается ВООБЩЕ, и
/// репликация с такой парой держится на том, что сеансы ведёт сам рядовой сервер
/// (<c>ReplicationService.CannotCall</c>, T-141/T-284). Картинка при этом не появлялась
/// нигде, кроме того компьютера, где её создал агент.
///
/// Поэтому файл теперь едет ПО ТОМУ КАНАЛУ, КОТОРЫЙ И ТАК РАБОТАЕТ. Каталог данных
/// организации реплицируется в обе стороны и без всяких звонков со стороны дирижёра, значит
/// достаточно, чтобы владелец файла положил его копию туда: сервер, у которого файл ЕСТЬ,
/// раскладывает по <see cref="Root"/> те файлы папки проекта, на которые ссылаются тексты
/// задач и сообщений чата, а остальные получают их обычной файловой репликацией. Кто
/// звонит кому — уже не важно, и выключённый сосед тоже не мешает.
///
/// Раздаются только УПОМЯНУТЫЕ файлы и только небольшие (<see cref="MaxBytes"/>): папка
/// проекта как была не реплицируемой (её выравнивают git'ом), так и осталась, а гигабайтное
/// видео по-прежнему берётся звонком у того, у кого оно лежит.
/// </summary>
public sealed partial class LinkedFileShare
{
    /// <summary>Куда ложится копия — внутри каталога данных организации, поэтому едет
    /// обычной файловой репликацией.</summary>
    public const string Root = "shared/project";

    /// <summary>Больше этого не раздаём: копия занимает место на КАЖДОМ сервере, а
    /// медиа-файл задания измеряется гигабайтами. Крупный файл остаётся за
    /// <see cref="RemoteFileService"/> — там он едет потоком и на диск не ложится.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>Как часто перебирать тексты. Проход дешёвый (SQL по подстроке плюс уже
    /// прочитанные описания пропускаются по времени изменения), но и торопиться некуда:
    /// файл всё равно уедет ближайшим сеансом репликации.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(60);

    /// <summary>Что уже разложено: ключ — организация и путь копии, значение — размер и
    /// время исходного файла. Правка файла в папке проекта раздаётся заново.</summary>
    private readonly ConcurrentDictionary<string, string> _done = new(StringComparer.Ordinal);

    /// <summary>Описания и критерии приёмки, которые уже просмотрены: они лежат файлами,
    /// и перечитывать их каждую минуту незачем.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _texts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ссылка на файл ПАПКИ ПРОЕКТА в тексте — в том виде, в каком её пишет агент
    /// и в каком она приходит из внешней системы (<c>&lt;img src='…'&gt;</c>). Адрес и код
    /// организации не важны: у каждого сервера они свои, а вот проект и путь общие.</summary>
    [GeneratedRegex("""api/files/project\?(?<q>[^\s"'<>)\]]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    /// <summary>Путь копии внутри каталога данных организации; null — проект или путь не
    /// годятся (<see cref="ProjectFiles.IsRelative"/>, чужие знаки в идентификаторе).</summary>
    public static string? RelOf(string? projectId, string? path)
    {
        var id = (projectId ?? "").Trim();
        if (id.Length == 0 || id.Length > 64
            || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
        {
            return null;
        }
        return ProjectFiles.IsRelative(path)
            ? $"{Root}/{id}/{(path ?? "").Trim().Replace('\\', '/')}"
            : null;
    }

    /// <summary>Ссылки на файлы папки проекта, найденные в тексте: пары «проект, путь».</summary>
    public static List<(string ProjectId, string Path)> LinksIn(string? text)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(text)
            || text.IndexOf("api/files/project", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return result;
        }
        foreach (Match match in LinkRegex().Matches(text))
        {
            var query = match.Groups["q"].Value.Split('#')[0].Replace("&amp;", "&");
            string projectId = "", path = "";
            foreach (var part in query.Split('&'))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                var name = part[..eq];
                var value = Unescape(part[(eq + 1)..]);
                if (name.Equals("projectId", StringComparison.OrdinalIgnoreCase))
                {
                    projectId = value;
                }
                else if (name.Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    path = value;
                }
            }
            if (projectId.Length > 0 && path.Length > 0)
            {
                result.Add((projectId, path));
            }
        }
        return result;
    }

    /// <summary>Разложить файлы, упомянутые в задачах и чате организации; возвращает число
    /// новых копий. Ошибки наружу не выпускаются: раздача — служба, а не сеанс репликации,
    /// и ронять из-за неё обход организаций нельзя.</summary>
    public int Run(OrgContext ctx)
    {
        var copied = 0;
        try
        {
            using var conn = ctx.Db.Open();
            using (var cmd = conn.CreateCommand())
            {
                // сообщения чата лежат текстом прямо в базе — берём только те, где ссылка есть
                cmd.CommandText = "SELECT text FROM chat_messages "
                                  + "WHERE deleted_at IS NULL AND text LIKE '%api/files/project%'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    copied += ShareText(ctx, reader.IsDBNull(0) ? "" : reader.GetString(0));
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                // описание и критерии приёмки лежат файлами каталога данных (ТЗ п. 6.4.4)
                cmd.CommandText = "SELECT description_path, acceptance_path FROM tasks "
                                  + "WHERE deleted_at IS NULL";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    copied += ShareStoredText(ctx, reader.IsDBNull(0) ? "" : reader.GetString(0));
                    copied += ShareStoredText(ctx, reader.IsDBNull(1) ? "" : reader.GetString(1));
                }
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException
                                       or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Раздача файлов ссылок {Org}: проход прерван", ctx.Org.Code);
        }
        if (copied > 0)
        {
            Log.Information("Раздача файлов ссылок {Org}: разложено {Count} файл(ов) — "
                            + "они уедут партнёрам обычной репликацией (T-13-S0)", ctx.Org.Code, copied);
        }
        return copied;
    }

    /// <summary>Разложить файлы, упомянутые в одном тексте.</summary>
    public int ShareText(OrgContext ctx, string? text)
    {
        var copied = 0;
        foreach (var (projectId, path) in LinksIn(text))
        {
            if (Copy(ctx, projectId, path))
            {
                copied++;
            }
        }
        return copied;
    }

    /// <summary>То же для текста, лежащего файлом хранилища; уже просмотренный файл
    /// пропускается по времени изменения.</summary>
    private int ShareStoredText(OrgContext ctx, string relPath)
    {
        if (relPath.Length == 0 || !ctx.Files.IsInside(relPath))
        {
            return 0;
        }
        var info = new FileInfo(ctx.Files.Abs(relPath));
        if (!info.Exists)
        {
            return 0;
        }
        var key = ctx.Org.Id + "|" + relPath;
        if (_texts.TryGetValue(key, out var seen) && seen == info.LastWriteTimeUtc)
        {
            return 0;
        }
        _texts[key] = info.LastWriteTimeUtc;
        try
        {
            return ShareText(ctx, File.ReadAllText(info.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Положить копию одного файла; false — раздавать нечего (файла здесь нет,
    /// он слишком велик или уже разложен).</summary>
    private bool Copy(OrgContext ctx, string projectId, string path)
    {
        var rel = RelOf(projectId, path);
        if (rel is null || !ctx.Files.IsInside(rel))
        {
            return false;
        }
        // файла здесь нет — значит его раздаст тот сервер, у которого он лежит
        var src = ProjectFiles.Resolve(ctx.Projects.Get(projectId)?.FolderPath, path);
        if (src is null || !File.Exists(src))
        {
            return false;
        }
        var info = new FileInfo(src);
        if (info.Length > MaxBytes)
        {
            return false;
        }
        var key = ctx.Org.Id + "|" + rel;
        var stamp = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        if (_done.TryGetValue(key, out var was) && was == stamp)
        {
            return false;
        }
        try
        {
            var dst = ctx.Files.Abs(rel);
            var already = File.Exists(dst) && new FileInfo(dst).Length == info.Length;
            if (!already)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            _done[key] = stamp;
            return !already;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException
                                       or PathTooLongException)
        {
            Log.Debug(ex, "Раздача файлов ссылок: {Path} не скопирован", rel);
            return false;
        }
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value).Trim();
        }
        catch (UriFormatException)
        {
            return value.Trim();
        }
    }
}
