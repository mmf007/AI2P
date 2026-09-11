namespace AI2P.Connectors;

/// <summary>
/// Отслеживание «хвоста» консоли ComfyUI (ТЗ v1.45, todo37_3). ComfyUI отдаёт свой лог целиком
/// (GET /internal/logs/raw — кольцевой буфер последних строк), поэтому при каждом опросе нужно
/// понять, что в нём НОВОГО. Считать по количеству строк нельзя: когда кольцо заполнено, старые
/// строки выпадают и длина перестаёт расти — новые строки потерялись бы навсегда.
/// Поэтому запоминается несколько последних уже отданных строк и ищется их последнее вхождение
/// в свежем буфере; всё, что после него, — новое. Совпадений нет (буфер уехал целиком) — новым
/// считается весь буфер: лучше повтор, чем потерянный вывод.
/// </summary>
public sealed class ComfyLogTail
{
    /// <summary>Сколько последних отданных строк держать для поиска места стыковки.</summary>
    private const int TailSize = 5;

    private readonly List<string> _tail = [];

    /// <summary>Новые строки буфера с прошлого опроса; хвост запоминается для следующего.</summary>
    public List<string> Advance(IReadOnlyList<string> entries)
    {
        var fresh = _tail.Count == 0 ? [.. entries] : NewAfterTail(entries);
        if (fresh.Count == 0)
        {
            return fresh;
        }
        _tail.AddRange(fresh);
        if (_tail.Count > TailSize)
        {
            _tail.RemoveRange(0, _tail.Count - TailSize);
        }
        return fresh;
    }

    private List<string> NewAfterTail(IReadOnlyList<string> entries)
    {
        // Совпадать может не весь хвост: часть уже отданных строк могла выпасть из кольца
        // ComfyUI. Поэтому пробуем от самого длинного совпадения к короткому — чем длиннее
        // совпавший кусок, тем надёжнее место стыковки; при равной длине берём последнее
        // вхождение (повторяющиеся строки не должны отматывать нас назад).
        for (var k = Math.Min(_tail.Count, entries.Count); k >= 1; k--)
        {
            var suffix = _tail.Skip(_tail.Count - k).ToList();
            for (var start = entries.Count - k; start >= 0; start--)
            {
                var matched = true;
                for (var i = 0; i < k && matched; i++)
                {
                    matched = entries[start + i] == suffix[i];
                }
                if (matched)
                {
                    return [.. entries.Skip(start + k)];
                }
            }
        }
        return [.. entries];
    }
}
