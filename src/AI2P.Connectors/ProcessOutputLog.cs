using System.Diagnostics;
using System.Text;

namespace AI2P.Connectors;

/// <summary>
/// ЖИВОЙ ВЫВОД ЗАПУЩЕННОЙ ПРОГРАММЫ (T-152-S0) — потоковое чтение stdout/stderr, пока процесс
/// идёт, плюс тайм-аут МОЛЧАНИЯ.
///
/// Зачем это отдельным классом, а не парой строк на месте. Перенаправленные потоки
/// (<c>RedirectStandardOutput</c>) НАДО вычитывать непрерывно: буфер трубы у Windows около
/// 4 КБ, и как только программа напечатает больше, её <c>write()</c> блокируется НАВСЕГДА —
/// процесс спит на записи в консоль, а мы стоим в <c>WaitForExitAsync</c>. Со стороны это
/// выглядит как «обучение висит 12 часов без нагрузки на процессор и падает по таймауту»
/// (жалоба заказчика по обучению LoRA); ни одна программа с прогресс-баром tqdm столько не
/// печатает молча — 4 КБ она набирает за первые секунды.
///
/// Отсюда же второе: пока строки идут, процесс ЖИВ, а молчание дольше заданного — это
/// зависание, и его надо кончать минутами, а не половиной суток. Общий предел длительности
/// остаётся сверху (он про «считает, но слишком долго»), а этот — про «не считает вовсе».
///
/// Прочитанное уходит в три места сразу: в <see cref="JobConsole"/> (она сама разбирает «\r»
/// перерисовки прогресс-бара), в файл журнала рядом с результатом (память процесса не
/// переживает перезапуск сервера, а файл переживает) и в кольцо последних строк — оно и есть
/// объяснение неудачи, которое кладётся человеку в текст ошибки.
///
/// Класс общий намеренно: тем же способом запускает внешние программы шлюз плагинов и
/// исполнитель «авто ПО» (T-154-S0).
/// </summary>
public sealed class ProcessOutputLog : IDisposable
{
    /// <summary>Сколько последних строк держать для объяснения неудачи.</summary>
    public const int TailLines = 200;

    /// <summary>Сколько знаков хвоста уходит в текст ошибки: подсказка, а не простыня.</summary>
    public const int TailChars = 1000;

    /// <summary>Длина строки, после которой она отдаётся как есть: программа может печатать
    /// прогресс без переводов строки вовсе, и копить его до бесконечности нельзя.</summary>
    private const int MaxLine = 8192;

    private readonly object _gate = new();
    private readonly Queue<string> _tail = new();
    private readonly StringBuilder _pending = new();
    private readonly JobConsole? _console;
    private readonly string _consoleKey;
    private readonly StreamWriter? _file;
    private long _lastTicks = DateTime.UtcNow.Ticks;
    private Task _pumps = Task.CompletedTask;

    /// <param name="filePath">Файл журнала; пусто — не писать на диск. Неудача открытия
    /// файла работу не роняет: журнал это удобство, а не условие запуска программы.</param>
    /// <param name="console">Консоль задания; null — вывод в неё не идёт.</param>
    /// <param name="consoleKey">Ключ буфера консоли (идентификатор задания).</param>
    public ProcessOutputLog(string? filePath = null, JobConsole? console = null,
        string consoleKey = "")
    {
        _console = console;
        _consoleKey = consoleKey;
        if (filePath is not { Length: > 0 })
        {
            return;
        }
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is { Length: > 0 })
            {
                Directory.CreateDirectory(dir);
            }
            // FileShare.ReadWrite: человек должен иметь возможность смотреть журнал (хоть
            // «tail -f», хоть блокнотом) ПОКА обучение идёт — ради этого файл и заводится
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            _file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            FilePath = filePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            _file = null;
        }
    }

    /// <summary>Файл журнала; пусто — на диск не пишем.</summary>
    public string FilePath { get; private set; } = "";

    /// <summary>Когда программа печатала в последний раз — от этого считается молчание.</summary>
    public DateTime LastWriteUtc => new(Interlocked.Read(ref _lastTicks), DateTimeKind.Utc);

    /// <summary>
    /// Начать откачку обоих потоков процесса. Зовётся СРАЗУ после <c>Process.Start</c>:
    /// каждая секунда без чтения — секунда, в которую программа может встать на записи.
    /// </summary>
    public void Attach(Process process)
    {
        _pumps = Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError));
    }

    /// <summary>
    /// ЖДАТЬ КОНЦА ПРОЦЕССА ИЛИ МОЛЧАНИЯ. Возвращает <c>true</c> — процесс кончился сам;
    /// <c>false</c> — молчал дольше <paramref name="idle"/> (сам процесс не тронут: гасит его
    /// вызывающий, он же решает, что написать человеку). Общий предел длительности —
    /// в <paramref name="token"/>, отмена приходит наружу исключением, как и раньше.
    /// </summary>
    public async Task<bool> WaitForExitAsync(Process process, TimeSpan idle,
        CancellationToken token)
    {
        var step = idle > TimeSpan.Zero && idle < TimeSpan.FromSeconds(5)
            ? idle
            : TimeSpan.FromSeconds(5);
        if (step < TimeSpan.FromMilliseconds(50))
        {
            step = TimeSpan.FromMilliseconds(50);
        }
        var exit = process.WaitForExitAsync(token);
        while (true)
        {
            var tick = Task.Delay(step, token);
            if (await Task.WhenAny(exit, tick) == exit)
            {
                await exit;
                // дочитать то, что программа успела напечатать перед выходом: конец вывода —
                // это и есть причина ненулевого кода возврата
                await DrainAsync(TimeSpan.FromSeconds(5));
                return true;
            }
            await tick;
            if (idle > TimeSpan.Zero && DateTime.UtcNow - LastWriteUtc >= idle)
            {
                return false;
            }
        }
    }

    /// <summary>Дождаться конца откачки, но не дольше <paramref name="limit"/>: трубу мог
    /// унаследовать внук, и тогда она не закроется никогда.</summary>
    public async Task DrainAsync(TimeSpan limit)
    {
        await Task.WhenAny(_pumps, Task.Delay(limit));
        Flush();
    }

    /// <summary>Кусок потока как есть (может кончаться посреди строки).</summary>
    public void Write(string chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }
        lock (_gate)
        {
            foreach (var ch in chunk)
            {
                if (ch is '\n' or '\r')
                {
                    Emit();
                }
                else
                {
                    _pending.Append(ch);
                }
            }
            if (_pending.Length > MaxLine)
            {
                Emit();
            }
        }
        Interlocked.Exchange(ref _lastTicks, DateTime.UtcNow.Ticks);
        // консоль задания сама разбирает «\r» прогресс-баров — отдаём ей кусок целиком
        _console?.Write(_consoleKey, chunk);
    }

    /// <summary>Своя пометка в журнал (команда, каталог, конец работы). Молчанием НЕ
    /// считается: пометки пишем мы, а следим за тем, печатает ли программа.</summary>
    public void Line(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        lock (_gate)
        {
            Emit();
            _pending.Append(text);
            Emit();
        }
        _console?.Write(_consoleKey, text + "\n");
    }

    /// <summary>Последние строки вывода — то, что кладётся человеку в текст ошибки.
    /// Берём КОНЕЦ, а не начало: причина падения всегда в последних строках, а первая
    /// тысяча знаков — приветствие программы.</summary>
    public string Tail()
    {
        lock (_gate)
        {
            // НЕДОПЕЧАТАННАЯ строка тоже идёт в хвост, но кольцо не трогает: у зависшей
            // программы последнее сказанное часто как раз без перевода строки — «Loading
            // checkpoint…», на котором она и встала. Потеряй мы её, объяснять было бы нечем
            var last = _pending.ToString().TrimEnd();
            var text = string.Join("\n", last.Length > 0 ? _tail.Append(last) : _tail);
            return text.Length <= TailChars ? text : "…" + text[^TailChars..];
        }
    }

    public void Dispose()
    {
        Flush();
        _file?.Dispose();
    }

    private async Task PumpAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                // ЧИТАЕМ КУСКАМИ, а не строками: прогресс-бар tqdm печатается через «\r» без
                // перевода строки, и построчное чтение показало бы его только в самом конце
                var read = await reader.ReadAsync(buffer.AsMemory());
                if (read <= 0)
                {
                    break;
                }
                Write(new string(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException
                                       or OperationCanceledException or InvalidOperationException)
        {
            // процесс погашен — труба закрыта, читать больше нечего
        }
        finally
        {
            Flush();
        }
    }

    private void Flush()
    {
        lock (_gate)
        {
            Emit();
            try
            {
                _file?.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // журнал на диске — удобство, а не условие работы
            }
        }
    }

    /// <summary>Отдать накопленную строку (под <see cref="_gate"/>).</summary>
    private void Emit()
    {
        if (_pending.Length == 0)
        {
            return;
        }
        var line = _pending.ToString().TrimEnd();
        _pending.Clear();
        if (line.Length == 0)
        {
            return;
        }
        _tail.Enqueue(line);
        while (_tail.Count > TailLines)
        {
            _tail.Dequeue();
        }
        try
        {
            _file?.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // место кончилось или файл унесли — вывод от этого теряться не должен
        }
    }
}
