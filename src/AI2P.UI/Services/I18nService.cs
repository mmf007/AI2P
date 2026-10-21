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
public sealed class I18nService : IDisposable
{
    private readonly LocCatalog _catalog;
    private string _lang;

    /// <summary>Смена языка «на лету» — страницы перерисовываются по этому событию.</summary>
    public event Action? OnChange;

    /// <summary>ОБЩИЙ экземпляр сервера (язык установки) — у экземпляра circuit'а, см. <see cref="ForCircuit"/>.</summary>
    private readonly I18nService? _root;

    /// <summary>Язык ЭТОГО circuit'а, отличный от языка установки (встроенный режим VS Code,
    /// T-507-S0); null — circuit говорит на языке установки, как раньше.</summary>
    private string? _override;

    public I18nService(string dir, string language)
    {
        _catalog = new LocCatalog(dir);
        _lang = language;
    }

    private I18nService(I18nService root)
    {
        _root = root;
        _catalog = root._catalog;
        _lang = "";
        root.OnChange += Relay;
    }

    /// <summary>
    /// Экземпляр на один circuit (T-507-S0). Без своего языка он во всём повторяет общий:
    /// читает его язык, а смену языка отдаёт ему — поэтому смена языка в настройках, как и
    /// прежде, доходит до всех открытых окон и до <see cref="Loc"/>. Свой язык появляется
    /// только у встроенного режима (<see cref="UseOwnLanguage"/>): панель VS Code говорит на
    /// языке VS Code и не перекрашивает этим язык обычного браузера и сервера.
    /// </summary>
    public I18nService ForCircuit() => new(_root ?? this);

    /// <summary>Свой язык circuit'а, не сохраняемый никуда (встроенный режим).</summary>
    public void UseOwnLanguage(string lang)
    {
        if (_root is null || _override == lang)
        {
            return;
        }
        _override = lang;
        OnChange?.Invoke();
    }

    private void Relay() => OnChange?.Invoke();

    public void Dispose()
    {
        if (_root is not null)
        {
            _root.OnChange -= Relay;
        }
    }

    public IReadOnlyCollection<string> Languages => _catalog.Languages;

    /// <summary>Языки в порядке показа человеку (T-70-S0) — по названию на самом языке.</summary>
    public IReadOnlyList<string> LanguagesByName => _catalog.LanguagesByName;

    /// <summary>Название языка на нём самом («Русский», «English», «简体中文»).</summary>
    public string Name(string? lang) => _catalog.Name(lang);

    public string Lang
    {
        get => _root is null ? _lang : _override ?? _root.Lang;
        set
        {
            if (_root is not null)
            {
                // свой язык встроенного режима меняется только у себя; иначе — общий
                if (_override is null)
                {
                    _root.Lang = value;
                }
                else if (_override != value)
                {
                    _override = value;
                    OnChange?.Invoke();
                }
                return;
            }
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
    public string this[string key] => _catalog.Text(Lang, key);

    /// <summary>Перевод с подстановкой значений {0}, {1}, … (тексты сообщений — общие с сервером).</summary>
    public string this[string key, params object?[] args] => _catalog.Text(Lang, key, args);
}

/// <summary>Общий экземпляр <see cref="I18nService"/> сервера (язык установки) — то, что
/// до T-507-S0 было единственным экземпляром. В DI под <see cref="I18nService"/> теперь
/// лежит экземпляр circuit'а (<see cref="I18nService.ForCircuit"/>); код вне запроса
/// (страницы входа при старте) берёт общий отсюда.</summary>
public sealed record I18nRoot(I18nService Value);
