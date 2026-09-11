using AI2P.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Запуск и остановка локального сервера модели (llama-server, Ollama и т.п.) по команде
/// из профайла модели (launchCommand, ТЗ п. 2.9). Сервер поднимается при запуске работы
/// команды и выгружается при остановке — убивается именно запущенный нами процесс
/// (по запомненному pid), чужие процессы не трогаются: если сервер по baseUrl уже отвечает,
/// он считается внешним и не останавливается. Один и тот же launchCommand на несколько
/// исполнителей/команд — один процесс (учёт использующих команд, kill при освобождении всеми).
/// </summary>
public sealed class LocalModelProcessService
{
    /// <summary>Сколько последних строк вывода сервера держать (todo37_2): упавший процесс
    /// сам объясняет причину в stdout/stderr, но раньше вывод уходил только в технический лог
    /// уровня Debug — при штатном уровне Information он терялся совсем. Буфер увеличен
    /// в todo37_3: те же строки идут в консоль задания, и её читают порциями.</summary>
    private const int OutputBufferLines = 400;

    /// <summary>Сколько последних строк показывать в сообщении об аварийном завершении.</summary>
    private const int ExitTailLines = 60;

    private sealed class Entry
    {
        /// <summary>Запущенный нами процесс; null — внешний сервер (не наш, не убиваем).</summary>
        public Process? Process;
        public int Pid;
        /// <summary>Кто держит сервер; kill — когда все освободили. Ключ владельца —
        /// «команда:исполнитель» (T-129): останов ОДНОГО участника не должен выгружать
        /// сервер, которым пользуются другие участники той же или другой команды.</summary>
        public HashSet<string> Teams = new();
        /// <summary>Когда запустили — для «прожил N с» в сообщении об аварийном завершении.</summary>
        public DateTime StartedAt = DateTime.UtcNow;
        /// <summary>Последние строки stdout/stderr процесса (кольцевой буфер) со сквозными
        /// номерами — по ним консоль задания дочитывает только новое (todo37_3).</summary>
        public readonly Queue<(long Seq, string Line)> Output = new();
        public long NextSeq = 1;

        public void AddOutput(string line)
        {
            lock (Output)
            {
                Output.Enqueue((NextSeq++, line));
                while (Output.Count > OutputBufferLines)
                {
                    Output.Dequeue();
                }
            }
        }

        public string[] OutputTail(int count)
        {
            lock (Output)
            {
                return Output.TakeLast(count).Select(o => o.Line).ToArray();
            }
        }

        public (List<string> Lines, long NextSeq) Since(long after)
        {
            lock (Output)
            {
                return (Output.Where(o => o.Seq > after).Select(o => o.Line).ToList(), NextSeq - 1);
            }
        }
    }

    private static ILogger Logger => Log.ForContext("SourceContext", nameof(LocalModelProcessService));

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>Ключ — нормализованный launchCommand.</summary>
    private readonly Dictionary<string, Entry> _servers = new();
    private readonly object _lock = new();

    /// <summary>Ключ владельца запуска локального сервера (T-129): сервер держит не команда
    /// целиком, а КАЖДЫЙ её участник по отдельности — иначе останов одного участника выгрузил бы
    /// модель у остальных. Освобождение команды целиком идёт по префиксу этого ключа.</summary>
    public static string OwnerKey(string teamId, string executorId) => teamId + ":" + executorId;

    /// <summary>
    /// Убедиться, что сервер модели запущен: если по baseUrl уже отвечает — внешний, не запускаем;
    /// иначе стартуем процесс команды launchCommand и запоминаем pid.
    /// <paramref name="owner"/> — ключ владельца (<see cref="OwnerKey"/>).
    /// Возвращает признак «жив»: false — процесс не удалось запустить/он сразу умер.
    /// </summary>
    public async Task EnsureStartedAsync(string launchCommand, string baseUrl, string owner)
    {
        launchCommand = launchCommand.Trim();
        if (launchCommand.Length == 0)
        {
            return;
        }

        // сервер уже отвечает (внешний или наш от предыдущего запуска) — не стартуем второй
        var alreadyUp = await IsListeningAsync(baseUrl);

        lock (_lock)
        {
            if (_servers.TryGetValue(launchCommand, out var existing))
            {
                if (existing.Process is { HasExited: false } || existing.Process is null && alreadyUp)
                {
                    existing.Teams.Add(owner);
                    Logger.Information("Локальный сервер модели уже запущен (pid {Pid}), владелец {Owner} добавлен",
                        existing.Process?.Id ?? 0, owner);
                    return;
                }
                _servers.Remove(launchCommand); // процесс умер — перезапустим
            }
            if (alreadyUp)
            {
                // отвечает, но запускали не мы — внешний сервер: работаем с ним, при остановке не трогаем
                Logger.Information("Сервер модели по {BaseUrl} уже отвечает — внешний, запуск не требуется", baseUrl);
                _servers[launchCommand] = new Entry { Process = null, Teams = { owner } };
                return;
            }

            var (fileName, arguments) = SplitCommand(launchCommand);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // вывод серверов моделей (llama.cpp, ComfyUI/Python) — UTF-8; без явной
                // кодировки он читался в кодировке консоли родителя, и русские сообщения
                // об ошибке приходили нечитаемыми (todo37_2)
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            // чтобы Python отдавал в перенаправленный вывод именно UTF-8 (иначе берёт
            // кодировку локали) — иначе traceback падения ComfyUI не прочитать
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            var exeDir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
            {
                psi.WorkingDirectory = exeDir;
            }
            var process = Process.Start(psi)
                ?? throw new InvalidOperationException(Loc.T("msg.localModelProcess.1", fileName));
            var entry = new Entry
            {
                Process = process,
                Pid = process.Id,
                Teams = { owner },
                StartedAt = DateTime.UtcNow,
            };
            // вывод сервера — в технические логи уровня Debug (llama-server пишет загрузку модели)
            // и в кольцевой буфер: если процесс упадёт, эти строки объяснят почему (todo37_2)
            void Capture(string? line)
            {
                if (line is null)
                {
                    return;
                }
                entry.AddOutput(line);
                Logger.Debug("[локальный сервер] {Line}", line);
            }
            process.OutputDataReceived += (_, e) => Capture(e.Data);
            process.ErrorDataReceived += (_, e) => Capture(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Logger.Information(
                "Запущен локальный сервер модели: pid {Pid}, рабочий каталог {WorkingDir}, команда: {Command}",
                process.Id, psi.WorkingDirectory, launchCommand);
            _servers[launchCommand] = entry;
        }
    }

    /// <summary>Жив ли запущенный нами процесс сервера (для ожидания готовности); null — не наш/не запускался.</summary>
    public (bool Alive, int ExitCode)? ProcessState(string launchCommand)
    {
        lock (_lock)
        {
            if (!_servers.TryGetValue(launchCommand.Trim(), out var entry) || entry.Process is null)
            {
                return null;
            }
            return entry.Process.HasExited ? (false, entry.Process.ExitCode) : (true, 0);
        }
    }

    /// <summary>
    /// Вывод локального сервера модели после строки с номером <paramref name="after"/>
    /// (ТЗ v1.45, todo37_3): по нему консоль задания показывает, чем занят llama-server,
    /// пока идёт генерация. 0 в NextSeq — сервер запускали не мы (внешний) либо он неизвестен.
    /// </summary>
    public (List<string> Lines, long NextSeq) ReadOutput(string launchCommand, long after)
    {
        lock (_lock)
        {
            return _servers.TryGetValue(launchCommand.Trim(), out var entry)
                ? entry.Since(after)
                : ([], 0);
        }
    }

    /// <summary>
    /// Разбор аварийного завершения сервера для сообщения пользователю (todo37_2):
    /// сколько процесс прожил, расшифровка кода выхода и последние строки его вывода —
    /// именно они объясняют, на чём он упал (или что вывода не было вовсе).
    /// </summary>
    public string ExitReport(string launchCommand, int exitCode)
    {
        string[] tail;
        TimeSpan lifetime;
        lock (_lock)
        {
            if (!_servers.TryGetValue(launchCommand.Trim(), out var entry))
            {
                return DescribeExitCode(exitCode);
            }
            tail = entry.OutputTail(ExitTailLines);
            lifetime = DateTime.UtcNow - entry.StartedAt;
        }
        var report = Loc.T("msg.localModelProcess.2", DescribeExitCode(exitCode), lifetime.TotalSeconds);
        // вывод упавшего процесса пишем и в технический лог: в UI он попадёт усечённым
        Logger.Warning("Локальный сервер модели завершился ({Report}). Последние строки вывода:\n{Tail}",
            report, tail.Length == 0 ? Loc.T("msg.localModelProcess.3") : string.Join("\n", tail));
        if (tail.Length == 0)
        {
            return report + Loc.T("msg.localModelProcess.4");
        }
        return report + Diagnosis(tail) + Loc.T("msg.localModelProcess.5") + string.Join("\n", tail);
    }

    /// <summary>
    /// Узнаваемые причины падения по выводу процесса (todo37_2). Первый разобранный случай:
    /// ComfyUI падает access violation'ом внутри инициализации CUDA, потому что драйвер
    /// NVIDIA старее, чем требует CUDA-сборка torch из пакета (у заказчика — драйвер 527.99
    /// с поддержкой CUDA 12.0 против torch 2.13.0+cu130, которому нужна CUDA 13). Сам torch
    /// в этом случае не выдаёт понятной ошибки, а срывается в нарушение доступа.
    /// </summary>
    private static string Diagnosis(string[] tail)
    {
        var text = string.Join("\n", tail);
        bool Has(string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);

        var cudaInitFailed = Has("cudaErrorNotSupported") || Has("cudaErrorInsufficientDriver")
                             || Has("CUDA not available") || Has("CUDA driver version is insufficient");
        if (cudaInitFailed)
        {
            return Loc.T("msg.localModelProcess.6");
        }
        if (Has("out of memory") || Has("CUDA out of memory"))
        {
            return Loc.T("msg.localModelProcess.7");
        }
        return "";
    }

    /// <summary>Понятная расшифровка типовых кодов выхода Windows (todo37_2).</summary>
    private static string DescribeExitCode(int exitCode) => exitCode switch
    {
        unchecked((int)0xC0000005) => Loc.T("msg.localModelProcess.8", exitCode),
        unchecked((int)0xC000001D) => Loc.T("msg.localModelProcess.9", exitCode),
        unchecked((int)0xC0000135) => Loc.T("msg.localModelProcess.10", exitCode),
        unchecked((int)0xC0000409) => Loc.T("msg.localModelProcess.11", exitCode),
        unchecked((int)0xC00000FD) => Loc.T("msg.localModelProcess.12", exitCode),
        _ => Loc.T("msg.localModelProcess.13", exitCode),
    };

    /// <summary>Команда остановила работу: освободить серверы ВСЕХ её участников
    /// (владельцы с префиксом «teamId:»); никем не используемые — выгрузить.</summary>
    public void ReleaseTeam(string teamId)
    {
        var prefix = teamId + ":";
        // старый ключ владельца — сама команда (совместимость с вызовами, где участника нет)
        Release(owner => owner == teamId || owner.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Остановлен ОДИН участник команды (T-129): освободить только его владение.
    /// Сервер модели может быть общим — он выгружается, лишь когда его отпустили все.</summary>
    public void ReleaseMember(string teamId, string executorId)
    {
        var owner = OwnerKey(teamId, executorId);
        Release(o => o == owner);
    }

    private void Release(Func<string, bool> isReleased)
    {
        lock (_lock)
        {
            foreach (var (command, entry) in _servers.ToList())
            {
                var released = entry.Teams.RemoveWhere(o => isReleased(o));
                if (released == 0 || entry.Teams.Count > 0)
                {
                    continue;   // держит кто-то ещё (или держали не мы) — сервер не трогаем
                }
                _servers.Remove(command);
                Kill(entry);
            }
        }
    }

    /// <summary>Выгрузить все запущенные нами серверы (остановка приложения).</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var entry in _servers.Values)
            {
                Kill(entry);
            }
            _servers.Clear();
        }
    }

    /// <summary>Убить именно наш процесс (по запомненному pid) вместе с потомками; внешний — не трогаем.</summary>
    private static void Kill(Entry entry)
    {
        if (entry.Process is null)
        {
            Logger.Information("Сервер модели внешний (запускали не мы) — не останавливаем");
            return;
        }
        try
        {
            if (!entry.Process.HasExited)
            {
                Logger.Information("Останавливаю локальный сервер модели: pid {Pid}", entry.Pid);
                entry.Process.Kill(entireProcessTree: true);
                entry.Process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Не удалось остановить локальный сервер модели: pid {Pid}", entry.Pid);
        }
        finally
        {
            entry.Process.Dispose();
        }
    }

    /// <summary>Быстрая проверка: отвечает ли сервер по {baseUrl}/models.</summary>
    private static async Task<bool> IsListeningAsync(string baseUrl)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            return false;
        }
        try
        {
            using var response = await Http.GetAsync(baseUrl + "/models");
            return true; // любой HTTP-ответ — сервер жив (даже 401/404)
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Разбор команды на исполняемый файл и аргументы; путь с пробелами — в кавычках.
    /// «~» в пути исполняемого файла раскрывается (T-135): процесс запускается без оболочки
    /// (<c>UseShellExecute=false</c>), и раскрывать «~» некому. В АРГУМЕНТАХ он остаётся как
    /// набран — их разбирает сама программа (llama-server, python), и трогать её разбор
    /// мы не вправе; пути в аргументах установщик подставляет полными.
    /// </summary>
    public static (string FileName, string Arguments) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            if (closing > 0)
            {
                return (AI2P.Core.PathHome.Expand(command[1..closing]),
                    command[(closing + 1)..].TrimStart());
            }
        }
        var space = command.IndexOf(' ');
        return space < 0
            ? (AI2P.Core.PathHome.Expand(command), "")
            : (AI2P.Core.PathHome.Expand(command[..space]), command[(space + 1)..].TrimStart());
    }
}
