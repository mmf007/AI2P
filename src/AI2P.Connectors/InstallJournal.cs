using System.Collections.Concurrent;
using System.Text.Json;
using AI2P.Core.Api;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ЖУРНАЛ УСТАНОВОК ЭТОГО КОМПЬЮТЕРА (T-728-S0): закладка «Настройки → Ход установок».
/// Одна запись — одна установка модели, установка пакета или обновление пакета; пакеты,
/// которые ставятся ВНУТРИ установки модели, — записи-дети с <see cref="InstallLogEntryDto.GroupId"/>
/// записи модели. Журнал машинный, а не организации: он лежит файлом в репозитории моделей
/// (там же, где сами веса и пакеты) и не реплицируется. Экземпляр один на файл — установщиков
/// по числу открытых организаций, а репозиторий у них общий.
/// </summary>
public sealed class InstallJournal
{
    public const string FileName = "ai2p-installs.json";

    /// <summary>
    /// СКОЛЬКО ХРАНИМ (T-735-S0): журнал — это лог, и живёт он столько же, сколько файлы
    /// лога сервера (Program.cs, retainedFileCountLimit: 31 — по файлу на день). Предела по
    /// числу записей нет: прежний предел в 500 строк вместе с разбором файла «с нуля» при
    /// любой ошибке чтения давал «на следующий день записи исчезли».
    /// </summary>
    public const int KeepDays = 31;

    private static readonly ConcurrentDictionary<string, InstallJournal> ByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ILogger Logger = Log.ForContext<InstallJournal>();

    private readonly string _path;
    private readonly object _lock = new();
    private readonly List<InstallLogEntryDto> _entries;

    private InstallJournal(string path)
    {
        _path = path;
        _entries = Load(path);
        Prune(_entries, DateTime.UtcNow);
        // прогоны живут в памяти процесса: «идёт» в файле после перезапуска значит «прервана»,
        // скачанное лежит на диске и докачается кнопкой «продолжить»
        foreach (var e in _entries.Where(e => e.State == InstallLogStates.Running))
        {
            e.State = InstallLogStates.Paused;
        }
    }

    public static InstallJournal For(string repoDir) =>
        ByPath.GetOrAdd(Path.GetFullPath(Path.Combine(repoDir, FileName)), p => new InstallJournal(p));

    private static List<InstallLogEntryDto> Load(string path)
    {
        // запись оборвалась между временным файлом и подменой — берём временный
        var tmp = path + ".tmp";
        if (!File.Exists(path) && File.Exists(tmp))
        {
            path = tmp;
        }
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<InstallLogEntryDto>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // испорченный файл не затираем первой же записью — откладываем рядом (T-735-S0)
            var bad = path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try
            {
                File.Copy(path, bad, true);
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                bad = "";
            }
            Logger.Warning(ex, "Журнал установок {Path} не прочитан — начат заново, прежний файл: {Bad}",
                path, bad);
            return [];
        }
    }

    /// <summary>Убрать записи старше срока хранения лога; идущие и прерванные не трогаем.</summary>
    public static bool Prune(List<InstallLogEntryDto> entries, DateTime nowUtc)
    {
        var edge = nowUtc.AddDays(-KeepDays);
        var gone = entries.Where(e => e.GroupId.Length == 0
                                      && e.State is InstallLogStates.Installed or InstallLogStates.Error
                                      && (e.FinishedAt ?? e.StartedAt) < edge)
            .Select(e => e.Id).ToHashSet();
        // группа уходит целиком — пакеты модели без своей строки-головы не оставляем
        return entries.RemoveAll(e => gone.Contains(e.Id) || gone.Contains(e.GroupId)) > 0;
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // атомарно: обрыв процесса посреди записи (автообновление, перезапуск) раньше
            // оставлял обрезанный файл, а он при следующем старте читался как «журнала нет»
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries));
            File.Move(tmp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(ex, "Журнал установок {Path} не записан", _path);
        }
    }

    public InstallLogEntryDto Add(string kind, string target, string name, string groupId = "")
    {
        var entry = new InstallLogEntryDto
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Kind = kind,
            Target = target,
            Name = name.Length > 0 ? name : target,
            State = InstallLogStates.Running,
            StartedAt = DateTime.UtcNow,
        };
        lock (_lock)
        {
            _entries.Insert(0, entry);
            Prune(_entries, DateTime.UtcNow);
            SaveLocked();
        }
        return entry;
    }

    /// <summary>
    /// ЗАПИСЬ ДЛЯ НОВОГО ЗАПУСКА (T-732-S0): незаконченная запись той же установки (пауза
    /// или ошибка, в группе — запись пакета в любом состоянии) продолжается, а не плодит
    /// вторую строку. Без этого повторный запуск из окна установки модели после ошибки давал
    /// две записи — упавшую и новую — с одним и тем же ходом загрузки.
    /// </summary>
    public InstallLogEntryDto Reuse(string kind, string target, string name, string groupId = "")
    {
        lock (_lock)
        {
            var old = _entries.FirstOrDefault(e => e.Kind == kind && e.Target == target
                && e.GroupId == groupId
                && (groupId.Length > 0
                    ? e.State != InstallLogStates.Running
                    : e.State is InstallLogStates.Paused or InstallLogStates.Error))
                // T-735-S0: пакет, упавший внутри установки модели, и запущенный потом сам по
                // себе (кнопкой окна установки) — продолжение той же записи, а не новая строка
                ?? (groupId.Length == 0 && kind != "model"
                    ? _entries.FirstOrDefault(e => e.Kind == kind && e.Target == target
                        && e.GroupId.Length > 0
                        && e.State is InstallLogStates.Paused or InstallLogStates.Error)
                    : null);
            if (old is null)
            {
                return Add(kind, target, name, groupId);
            }
            Restart(old);
            return old;
        }
    }

    /// <summary>Снова в работу (кнопка «продолжить»): запись та же, время начала новое.</summary>
    public void Restart(InstallLogEntryDto entry)
    {
        lock (_lock)
        {
            entry.State = InstallLogStates.Running;
            entry.StartedAt = DateTime.UtcNow;
            entry.FinishedAt = null;
            entry.Error = "";
            // продолженная запись снова последняя — поднимаем наверх
            _entries.Remove(entry);
            _entries.Insert(0, entry);
            SaveLocked();
        }
    }

    public void Finish(InstallLogEntryDto? entry, string state, string error = "")
    {
        if (entry is null)
        {
            return;
        }
        lock (_lock)
        {
            entry.State = state;
            entry.Error = error;
            entry.FinishedAt = DateTime.UtcNow;
            SaveLocked();
        }
    }

    public InstallLogEntryDto? Get(string id)
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.Id == id);
        }
    }

    /// <summary>Дети записи-группы (пакеты установки модели).</summary>
    public List<InstallLogEntryDto> Children(string groupId)
    {
        lock (_lock)
        {
            return _entries.Where(e => e.GroupId.Length > 0 && e.GroupId == groupId).ToList();
        }
    }

    public void Remove(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        lock (_lock)
        {
            _entries.RemoveAll(e => set.Contains(e.Id));
            SaveLocked();
        }
    }

    /// <summary>Копия списка — последние сверху (порядок вставки).</summary>
    public List<InstallLogEntryDto> List()
    {
        lock (_lock)
        {
            return _entries.Select(e => e.Clone()).ToList();
        }
    }
}
