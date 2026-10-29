using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// Множественный выбор строк в списке задач (T-651-S0) — как в проводнике Windows:
/// щелчок выделяет одну строку, Ctrl+щелчок добавляет или снимает строку, Shift+щелчок —
/// всё от опорной строки до щелчка в ПОРЯДКЕ ПОКАЗА. Открывается задача теперь двойным
/// щелчком. У каждого представления свой экземпляр (дерево подзадач в карточке и список
/// проекта рядом — разные выборы); меню «…» получает его каскадом и при выборе больше
/// одной строки показывает меню для множественного выбора.
/// </summary>
public sealed class TaskSelection
{
    private readonly List<TaskItem> _items = [];
    private string? _anchor;

    /// <summary>Выбор изменился — представление перерисовывает подсветку строк.</summary>
    public event Action? Changed;

    public int Count => _items.Count;

    /// <summary>Выбранные задачи в порядке выбора.</summary>
    public IReadOnlyList<TaskItem> Items => _items;

    public bool Contains(string taskId) => _items.Any(t => t.Id == taskId);

    /// <summary>Класс строки: выбранная подсвечивается (цвет — MainLayout, на обе темы).</summary>
    public string Css(string taskId) => Contains(taskId) ? "ai2p-task-sel" : "";

    /// <summary>
    /// Щелчок по строке. <paramref name="ordered"/> — строки в порядке показа: по нему
    /// Shift выделяет диапазон. Опора диапазона — последняя строка, щёлкнутая без Shift.
    /// </summary>
    public void Click(TaskItem task, IEnumerable<TaskItem> ordered, bool shift, bool ctrl)
    {
        if (shift && _anchor is not null)
        {
            var list = ordered.ToList();
            var from = list.FindIndex(t => t.Id == _anchor);
            var to = list.FindIndex(t => t.Id == task.Id);
            if (from >= 0 && to >= 0)
            {
                if (!ctrl)
                {
                    _items.Clear();
                }
                foreach (var item in list.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1))
                {
                    if (!Contains(item.Id))
                    {
                        _items.Add(item);
                    }
                }
                Changed?.Invoke();
                return;
            }
        }
        if (ctrl)
        {
            if (_items.RemoveAll(t => t.Id == task.Id) == 0)
            {
                _items.Add(task);
            }
        }
        else
        {
            _items.Clear();
            _items.Add(task);
        }
        _anchor = task.Id;
        Changed?.Invoke();
    }

    /// <summary>Список перечитан: выбранные строки заменяются свежими (статус, сервер),
    /// пропавшие из списка — снимаются.</summary>
    public void Refresh(IEnumerable<TaskItem> tasks)
    {
        if (_items.Count == 0)
        {
            return;
        }
        var byId = tasks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var fresh = _items.Where(t => byId.ContainsKey(t.Id)).Select(t => byId[t.Id]).ToList();
        _items.Clear();
        _items.AddRange(fresh);
        if (_anchor is not null && !byId.ContainsKey(_anchor))
        {
            _anchor = null;
        }
    }

    public void Clear()
    {
        _items.Clear();
        _anchor = null;
        Changed?.Invoke();
    }
}
