using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ЗАПИСЬ ПЛАГИНА ПОЯВЛЯЕТСЯ ВМЕСТЕ С ПРОГРАММОЙ (T-156-S0). Заказчик: «при установке софта
/// обучения для модели Кандинский из справочника моделей в списке „плагинов и mcp“ появляется
/// соответствующая запись».
///
/// <para>ПОЧЕМУ ЭТО НЕ ПРОТИВОРЕЧИТ ПРАВИЛУ «ЗАПИСИ ЗАВОДИТ ЧЕЛОВЕК» (T-115-S0). Правило было
/// про ЧТЕНИЕ СПИСКА: запись не должен заводить всякий, кто просто открыл вкладку (её видит и
/// читатель), иначе первый же зашедший наплодил бы записей на весь кластер. Установка модели —
/// не открытие вкладки, а ЯВНОЕ РЕШЕНИЕ ЧЕЛОВЕКА поставить программу на этот компьютер: он
/// нажал «Установить» и дождался. Поэтому запись заводится в состоянии «объявлен»
/// (<see cref="PluginStates.Declared"/>) — действия справочника и опыт по-прежнему заводит
/// инициализация, и правило «инструмент без записи справочника не публикуется» не нарушено.</para>
///
/// <para>ПОЧЕМУ ЗАПИСЬ ВООБЩЕ НУЖНА, ЕСЛИ МАНИФЕСТ И ТАК ВИДЕН В СПИСКЕ. Манифест без записи —
/// это строка «объявлен» без номера PLG-N и без настроек; на него нельзя сослаться исполнителю
/// «авто ПО» (T-153-S0) и его не видно на соседних серверах. Установка тренера — ровно тот
/// момент, когда организации есть что решить: «эта программа у нас есть».</para>
///
/// <para>ИДЕМПОТЕНТНОСТЬ ДЕРЖИТСЯ НА ДВУХ ВЕЩАХ И НОВЫХ ТАБЛИЦ НЕ ТРЕБУЕТ:
/// <c>PluginService.Declare</c> заводит запись ТОЛЬКО если кода ещё нет (иначе возвращает
/// существующую), а ручной путь дописывается лишь в ПУСТОЕ поле config.json — указанное
/// человеком значение повторная установка не перетирает. Повторный старт сервера записей не
/// заводит вовсе: сюда ходит только установка модели.</para>
///
/// <para>КАК ПЛАГИН НАХОДИТСЯ ПО МОДЕЛИ: по ПАКЕТАМ. У модели пакеты обучения объявлены в
/// <c>lora.train.packages</c> (у обеих записей Kandinsky это <c>musubi-tuner</c> и
/// <c>python</c>), у плагина — в блоке <c>software</c> каждой его программы. Годится плагин,
/// который ведёт ко ВСЕМ пакетам модели и объявляет долгий запуск (блок <c>run</c>): это и
/// значит «тренер», а не «конвертор, которому случайно нужен тот же python». Тем же способом
/// плагин ищет и форма обучения (T-157-S0) — правило одно на обе стороны.</para>
/// </summary>
public sealed class TrainerPluginService
{
    private static readonly ILogger Logger = Log.ForContext<TrainerPluginService>();

    private readonly ModelInstallService _installs;
    private readonly FileStore _files;
    private readonly PluginService _plugins;

    public TrainerPluginService(ModelInstallService installs, FileStore files, PluginService plugins)
    {
        _installs = installs;
        _files = files;
        _plugins = plugins;
    }

    /// <summary>
    /// ЗАПОМНИТЬ ПУТЬ К ПРОГРАММЕ ПЛАГИНА (код плагина, имя записи софта — пусто у ГЛАВНОЙ,
    /// путь). Путь пер-серверный и живёт в config.json (9fee2887), о котором организация не
    /// знает, — отсюда делегат. null — путей не пишем: плагин просто покажет «софт ищется»,
    /// пока человек не укажет программу руками.
    /// </summary>
    public Action<string, string, string>? SavePath { get; set; }

    /// <summary>Язык, на котором берётся название плагина из манифеста; null — язык установки.</summary>
    public Func<string>? Language { get; set; }

    /// <summary>
    /// Завести записи плагинов, к которым ведут пакеты обучения этой модели. Возвращает коды
    /// заведённых (или уже существовавших) плагинов — пусто, если у модели тренера нет.
    /// </summary>
    public List<string> DeclareForModel(ModelInstallManifest manifest, string? actorId = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var found = new List<string>();
        if (!manifest.HasTrainer)
        {
            return found;   // обучения у модели нет либо тренер не объявлен — записи не нужно
        }
        foreach (var plugin in PluginManifest.ReadAll(_files.DataDir))
        {
            if (!Covers(plugin, manifest.TrainPackages))
            {
                continue;
            }
            var record = _plugins.Declare(plugin, actorId, Language?.Invoke());
            found.Add(record.Code);
            Logger.Information("Плагин {Plugin} ({DisplayId}) заведён по пакетам обучения модели: {Packages}",
                record.Code, record.DisplayId, string.Join(", ", manifest.TrainPackages));
            RememberPaths(plugin);
        }
        return found;
    }

    /// <summary>
    /// ПЛАГИН — ТРЕНЕР ЭТОЙ МОДЕЛИ: он объявляет долгий запуск и ведёт ко ВСЕМ пакетам, которые
    /// модель назвала пакетами обучения. Условие «ко всем» намеренно строгое: python нужен и
    /// другим плагинам, и одного совпадения хватило бы, чтобы завести запись чужого плагина.
    /// </summary>
    public static bool Covers(PluginManifest plugin, IReadOnlyList<string> trainPackages)
    {
        if (plugin.Run is null || trainPackages.Count == 0)
        {
            return false;
        }
        var packages = plugin.SoftwareList
            .Select(s => s.Package.Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return packages.Count > 0
               && trainPackages.All(p => packages.Contains(p.Trim()));
    }

    /// <summary>
    /// ПОКАЗАТЬ ПЛАГИНУ УЖЕ ПОСТАВЛЕННЫЕ ПРОГРАММЫ. Установка модели кладёт пакеты в
    /// &lt;корень пакетов&gt;/&lt;каталог пакета&gt;, а поиск программы плагина туда не заглядывает —
    /// он смотрит PATH и ручной путь (<c>PluginSoftwareProbe</c>). Поэтому найденный файл
    /// пакета записывается ручным путём: иначе только что поставленный тренер показывался бы
    /// как «не найден», и человеку пришлось бы искать его «Обзором» самому.
    ///
    /// <para>Пустое поле — обязательное условие записи: путь, указанный человеком, главнее
    /// нашего, и повторная установка его не трогает.</para>
    /// </summary>
    private void RememberPaths(PluginManifest plugin)
    {
        if (SavePath is null)
        {
            return;
        }
        var catalog = _installs.Packages();
        var written = false;
        foreach (var soft in plugin.SoftwareList)
        {
            var package = catalog.Find(soft.Package.Trim());
            if (package is null)
            {
                continue;
            }
            var file = _installs.FindInPackage(package, package.Check);
            if (file is null)
            {
                continue;   // пакет не поставился — писать нечего
            }
            // главная (первая) программа живёт в старом поле path, остальные — в словаре
            // paths по имени записи софта (T-146-S0): у формы плагина то же правило
            SavePath(plugin.Code,
                ReferenceEquals(plugin.Software, soft) ? "" : soft.Id, file);
            written = true;
        }
        if (written)
        {
            // накопленный ответ проверки уже не про эти пути
            PluginSoftwareProbe.Forget();
        }
    }
}
