using AI2P.Core;

namespace AI2P.UI.Services;

/// <summary>
/// Мультиязычность UI (ТЗ гл. 9): key-value словари, JSON-файл на язык (i18n/ru.json, i18n/en.json).
/// Добавление языка = добавление файла; правка переводов — без пересборки (файлы читаются на старте).
/// resx не используется намеренно.
/// С T-180 чтение словарей живёт в <see cref="LocCatalog"/> (AI2P.Core) — тем же словарём
/// пользуются хранилище, коннекторы и API; здесь остаётся только состояние UI: выбранный язык
/// и событие перерисовки. Экземпляр свой у каждого сервиса (визард первого старта поднимает
/// его на своём каталоге), поэтому это не обёртка над статическим <see cref="Loc"/>.
/// </summary>
public sealed class I18nService
{
    private readonly LocCatalog _catalog;
    private string _lang;

    /// <summary>Смена языка «на лету» — страницы перерисовываются по этому событию.</summary>
    public event Action? OnChange;

    public I18nService(string dir, string language)
    {
        _catalog = new LocCatalog(dir);
        _lang = language;
    }

    public IReadOnlyCollection<string> Languages => _catalog.Languages;

    /// <summary>Языки в порядке показа человеку (T-70-S0) — по названию на самом языке.</summary>
    public IReadOnlyList<string> LanguagesByName => _catalog.LanguagesByName;

    /// <summary>Название языка на нём самом («Русский», «English», «简体中文»).</summary>
    public string Name(string? lang) => _catalog.Name(lang);

    public string Lang
    {
        get => _lang;
        set
        {
            if (_lang != value)
            {
                _lang = value;
                // язык установки один на сервер: тексты сообщений вне UI (хранилище,
                // коннекторы, API) берут его из Loc, поэтому смена «на лету» доходит и туда
                Loc.Lang = value;
                OnChange?.Invoke();
            }
        }
    }

    /// <summary>Перевод по ключу; нет перевода — вернуть сам ключ (заметно при отладке словарей).</summary>
    public string this[string key] => _catalog.Text(_lang, key);

    /// <summary>Перевод с подстановкой значений {0}, {1}, … (тексты сообщений — общие с сервером).</summary>
    public string this[string key, params object?[] args] => _catalog.Text(_lang, key, args);
}
