using System.Collections.Concurrent;

namespace AI2P.Connectors;

/// <summary>
/// Консоль задания (ТЗ v1.45, todo37_3): живой построчный вывод хода работы — что делает
/// исполнитель прямо сейчас. Нужна для ВСЕХ моделей: у медиа-модели сюда сливается консоль
/// самого ComfyUI (с прогресс-баром генерации), у текстовых — этапы вызова провайдера и
/// вызовы инструментов. Буфер держится в памяти процесса (после перезапуска приложения
/// консоль завершённых заданий не восстанавливается — постоянная история задания живёт
/// в журнале работ и артефактах, п. 6.3).
///
/// Чтение — по номеру последней прочитанной строки (Seq): UI дочитывает «хвост» и не гоняет
/// весь буфер. Номера сквозные и не переиспользуются, поэтому по выпавшим из кольца строкам
/// видно, что часть вывода потеряна.
/// </summary>
public sealed class JobConsole
{
    /// <summary>Сколько последних строк вывода держать по одному заданию.</summary>
    public const int MaxLines = 1000;

    /// <summary>Сколько заданий держать в памяти (старые вытесняются по времени записи).</summary>
    public const int MaxJobs = 50;

    /// <summary>Строка консоли: сквозной номер, время и текст.</summary>
    public readonly record struct Line(long Seq, DateTime Ts, string Text);

    private sealed class Buffer
    {
        public readonly Queue<Line> Lines = new();
        public long NextSeq = 1;
        public DateTime LastWriteUtc = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Buffer> _buffers = new();

    /// <summary>
    /// Записать вывод задания: многострочный текст разбивается на строки, «\r» (перерисовка
    /// прогресс-бара tqdm в консоли ComfyUI) считается концом строки — иначе весь прогресс
    /// генерации слипся бы в одну бесконечную строку.
    /// </summary>
    public void Write(string jobId, string text)
    {
        if (jobId.Length == 0 || text.Length == 0)
        {
            return;
        }
        var buffer = _buffers.GetOrAdd(jobId, _ =>
        {
            Evict();
            return new Buffer();
        });
        lock (buffer)
        {
            foreach (var raw in text.Split('\n', '\r'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }
                buffer.Lines.Enqueue(new Line(buffer.NextSeq++, DateTime.UtcNow, line));
                while (buffer.Lines.Count > MaxLines)
                {
                    buffer.Lines.Dequeue();
                }
            }
            buffer.LastWriteUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Строки, появившиеся после номера <paramref name="after"/> (0 — весь буфер).
    /// NextSeq — с чем прийти в следующий раз; Lost — сколько строк выпало из кольца.</summary>
    public (List<Line> Lines, long NextSeq, long Lost) Read(string jobId, long after)
    {
        if (!_buffers.TryGetValue(jobId, out var buffer))
        {
            return ([], after, 0);
        }
        lock (buffer)
        {
            var lines = buffer.Lines.Where(l => l.Seq > after).ToList();
            // первая оставшаяся строка новее ожидаемой — часть вывода вытеснена из кольца
            var lost = lines.Count > 0 && after > 0 ? Math.Max(0, lines[0].Seq - after - 1) : 0;
            return (lines, buffer.NextSeq - 1, lost);
        }
    }

    /// <summary>Есть ли вообще консоль по заданию (для UI: показывать ли панель).</summary>
    public bool Has(string jobId) => _buffers.ContainsKey(jobId);

    /// <summary>Забыть консоль задания (задача удалена).</summary>
    public void Forget(string jobId) => _buffers.TryRemove(jobId, out _);

    /// <summary>Вытеснить самые старые консоли, когда заданий накопилось больше лимита.</summary>
    private void Evict()
    {
        while (_buffers.Count >= MaxJobs)
        {
            var oldest = _buffers.OrderBy(p => p.Value.LastWriteUtc).FirstOrDefault();
            if (oldest.Key is null || !_buffers.TryRemove(oldest.Key, out _))
            {
                return;
            }
        }
    }
}
