using System.Globalization;
using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// Словарь одного набора языков: JSON-файл на язык (i18n/ru.json, i18n/en.json).
/// Общая часть мультиязычности (ТЗ гл. 9) для ВСЕХ слоёв: до T-180 словари умел читать
/// только UI (<c>I18nService</c>), а тексты сообщений хранилища, коннекторов и API были
/// вписаны в код по-русски. Класс намеренно без зависимостей — им пользуются Core,
/// Storage, Connectors, Server и UI.
/// </summary>
public sealed class LocCatalog
{
    private readonly Dictionary<string, Dictionary<string, string>> _dictionaries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Язык, на который откатываемся, если в запрошенном языке ключа нет.</summary>
    public const string BaseLanguage = "ru";

    public LocCatalog()
    {
    }

    public LocCatalog(string dir) => Load(dir);

    /// <summary>
    /// Читает каталог со словарями. Файлы с «_» в имени — сиды сервисов
    /// (ActionCatalogService_ru.json), языками не считаются. Повреждённый словарь не валит
    /// приложение: язык просто недоступен.
    /// </summary>
    public void Load(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            if (lang.Contains('_'))
            {
                continue;
            }
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (dict is not null)
                {
                    _dictionaries[lang] = dict;
                }
            }
            catch (JsonException)
            {
                // повреждённый словарь не должен валить приложение — язык просто недоступен
            }
            catch (IOException)
            {
            }
        }
    }

    public IReadOnlyCollection<string> Languages => _dictionaries.Keys;

    /// <summary>Ключ, под которым в словаре лежит НАЗВАНИЕ САМОГО ЯЗЫКА (T-70-S0).</summary>
    public const string NameKey = "lang.name";

    /// <summary>
    /// Название языка, написанное НА НЁМ САМОМ («Русский», «English», «简体中文»). Лежит в
    /// его же словаре ключом <see cref="NameKey"/> — отдельной таблицы «код → название» нет
    /// намеренно: язык добавляется одним файлом <c>i18n/&lt;код&gt;.json</c>, и название
    /// обязано приезжать вместе с ним, иначе новый язык показывался бы кодом.
    /// Нет словаря или ключа — возвращается сам код.
    /// </summary>
    public string Name(string? lang)
    {
        if (!string.IsNullOrWhiteSpace(lang)
            && _dictionaries.TryGetValue(lang, out var dict)
            && dict.TryGetValue(NameKey, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        return lang ?? "";
    }

    /// <summary>Языки в порядке показа человеку — по названию на самом языке.</summary>
    public IReadOnlyList<string> LanguagesByName =>
        _dictionaries.Keys.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public bool Has(string lang, string key) =>
        _dictionaries.TryGetValue(lang ?? "", out var dict) && dict.ContainsKey(key);

    /// <summary>
    /// Перевод по ключу. Порядок поиска: запрошенный язык → базовый (ru) → сам ключ.
    /// Откат на базовый язык важен для СООБЩЕНИЙ: недописанный перевод обязан показать
    /// человеку осмысленный текст, а не «err.task.notFound» (в UI ключ виден при отладке
    /// словарей — там ключ и остаётся, если нет ни перевода, ни базового текста).
    /// </summary>
    public string Text(string lang, string key)
    {
        if (_dictionaries.TryGetValue(lang ?? "", out var dict) && dict.TryGetValue(key, out var value))
        {
            return value;
        }
        if (_dictionaries.TryGetValue(BaseLanguage, out var basic) && basic.TryGetValue(key, out var fallback))
        {
            return fallback;
        }
        return key;
    }

    /// <summary>Перевод с подстановкой значений: в тексте словаря — {0}, {1}, … (string.Format).</summary>
    public string Text(string lang, string key, params object?[] args)
    {
        var text = Text(lang, key);
        if (args.Length == 0)
        {
            return text;
        }
        try
        {
            // культура ТЕКУЩАЯ, как у интерполяции строк ($"…{x:0.#}"): тексты переехали
            // из кода в словарь, и числа с датами обязаны выглядеть ровно так же
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // кривой перевод (лишняя фигурная скобка) не должен ронять работу
            return text;
        }
    }
}

/// <summary>
/// Словари приложения как единая точка доступа для кода вне UI (ТЗ гл. 9, T-180).
/// Язык здесь — язык УСТАНОВКИ (config.json «language»): он один на сервер, его же
/// показывает UI. Тексты, у которых язык свой (промпты агента — язык команды), берутся
/// вызовом <see cref="In"/> с явным языком.
/// </summary>
public static class Loc
{
    private static LocCatalog _catalog = new();
    private static string _lang = LocCatalog.BaseLanguage;

    /// <summary>Словарь приложения; подменяется целиком при загрузке (см. <see cref="Load"/>).</summary>
    public static LocCatalog Catalog => _catalog;

    /// <summary>Читает i18n/ каталога приложения. Вызывается один раз при старте.</summary>
    public static void Load(string dir, string language)
    {
        var catalog = new LocCatalog(dir);
        _catalog = catalog;
        Lang = language;
    }

    /// <summary>Язык установки; меняется «на лету» вместе с настройкой (гл. 9).</summary>
    public static string Lang
    {
        get => _lang;
        set => _lang = string.IsNullOrWhiteSpace(value) ? LocCatalog.BaseLanguage : value;
    }

    public static IReadOnlyCollection<string> Languages => _catalog.Languages;

    /// <summary>Языки в порядке показа человеку (T-70-S0) — по названию на самом языке.</summary>
    public static IReadOnlyList<string> LanguagesByName => _catalog.LanguagesByName;

    /// <summary>Название языка на нём самом («Русский», «English», «简体中文»).</summary>
    public static string Name(string? lang) => _catalog.Name(lang);

    /// <summary>Текст на языке установки.</summary>
    public static string T(string key) => _catalog.Text(_lang, key);

    /// <summary>Текст на языке установки с подстановкой {0}, {1}, …</summary>
    public static string T(string key, params object?[] args) => _catalog.Text(_lang, key, args);

    /// <summary>Текст на ЗАДАННОМ языке (промпты агента, письма, ответы чужому серверу).</summary>
    public static string In(string? lang, string key) =>
        _catalog.Text(string.IsNullOrWhiteSpace(lang) ? _lang : lang, key);

    public static string In(string? lang, string key, params object?[] args) =>
        _catalog.Text(string.IsNullOrWhiteSpace(lang) ? _lang : lang, key, args);
}
