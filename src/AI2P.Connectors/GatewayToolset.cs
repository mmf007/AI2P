using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// ОДИН ПЛАГИН-ШЛЮЗ, ГОТОВЫЙ К РАБОТЕ НА ЭТОМ СЕРВЕРЕ: манифест (описание возможностей,
/// реплицируется вместе с записью) плюс НАЙДЕННЫЙ ЗДЕСЬ путь к программе (пер-серверная
/// величина из config.json, не реплицируется — правило T-110-S0 §1.3.4).
/// </summary>
/// <param name="Manifest">Манифест <c>plugins/&lt;код&gt;/plugin.json</c>.</param>
/// <param name="SoftwarePath">Путь к программе на этом компьютере; пусто — не нашли.</param>
/// <param name="SettingsJson">Настройки записи плагина (<c>PluginRecord.SettingsJson</c>).</param>
public sealed record GatewayPlugin(PluginManifest Manifest, string SoftwarePath = "",
    string SettingsJson = "");

/// <summary>
/// ИНСТРУМЕНТЫ ШЛЮЗОВ В ВИДЕОРЕДАКТОРЫ ДЛЯ ИИ-АГЕНТА (T-115-S0): собрать монтажный лист из
/// медиатеки проекта и посчитать по нему ролик внешней программой без окна.
///
/// <para>КОДА НА КАЖДЫЙ ШЛЮЗ ЗДЕСЬ НЕТ И НЕ БУДЕТ. Набор инструментов складывается из
/// МАНИФЕСТОВ живых записей плагинов: имя инструмента, его код в справочнике действий и
/// РОЛЬ (<see cref="PluginAction.RoleExport"/> / <see cref="PluginAction.RoleRender"/>)
/// приходят файлом, а работу за ролью делает общий движок — <see cref="TimelineExport"/> для
/// сборки, <see cref="RenderCommand"/> для рендера. Поэтому шлюзы в DaVinci Resolve
/// (T-117-S0), Blender VSE (T-118-S0) и OpenShot (T-120-S0) добавляются МАНИФЕСТОМ, а не
/// новым классом.</para>
///
/// <para>ПОЧЕМУ ИМЕНА ИНСТРУМЕНТОВ У КАЖДОГО ШЛЮЗА СВОИ (<c>shotcut_timeline_write</c>, а не
/// общий <c>timeline_write</c>): правила безопасности находят действие по имени инструмента
/// (<c>ActionCatalogService.CodeByTool</c>), и общее имя на четыре плагина означало бы одно
/// правило на все четыре сразу — запретить рендер Blender, оставив рендер Shotcut, стало бы
/// нечем. Роль при этом общая, и работа за ней одна.</para>
///
/// <para>ТРИ ПРАВИЛА, НАРУШАТЬ КОТОРЫЕ НЕЛЬЗЯ (T-110-S0):</para>
/// <list type="number">
/// <item><b>Свой файл.</b> Агент пишет ТОЛЬКО файл, названный в манифесте
/// (<c>ai2p_library.mlt</c>); файл человека, открытый сейчас в редакторе, не трогается
/// никогда — иначе сохранение из Shotcut и запись агента затирают друг друга.</item>
/// <item><b>Пути только относительные.</b> Абсолютный <c>resource</c> убивает переносимость
/// по кластеру. Путь клипа считается от каталога выходного файла, а ресурс хранилища
/// организации копируется рядом (<see cref="TimelineFormat.CopyStore"/>).</item>
/// <item><b>Ограничение каталогом.</b> Писать можно в папку проекта и в каталоги, открытые
/// правилами безопасности задачи. Здесь оно работает БУКВАЛЬНО (мы пишем XML и кода не
/// исполняем), поэтому проверяются ОБА рода путей: путь записываемого файла и каждый путь,
/// вписываемый ВНУТРЬ файла проекта.</item>
/// </list>
/// </summary>
public sealed class GatewayToolset
{
    private readonly IReadOnlyList<GatewayPlugin> _plugins;
    private readonly ObjectService? _objects;
    private readonly TaskItem _current;
    private readonly string? _projectFolder;
    private readonly string? _dataDir;
    private readonly string _projectTitle;
    private readonly Func<string, string, bool> _isPathPermitted;
    private readonly IReadOnlyList<string> _allowedDirs;

    /// <summary>Язык ответов (T-190) — их читает агент.</summary>
    public string? Language { get; set; }

    /// <summary>Лог вызовов — в журнал работ (agent.tool_calls), как у остальных наборов.</summary>
    public List<string> CallLog { get; } = [];

    /// <param name="plugins">Живые записи шлюзов этого сервера (софт уже найден).</param>
    /// <param name="objects">Объекты проекта — из них читается медиатека.</param>
    /// <param name="current">Текущая задача: от неё берётся проект.</param>
    /// <param name="projectFolder">Папка проекта — корень относительных путей.</param>
    /// <param name="dataDir">Каталог данных организации — корень путей <c>store:</c>.</param>
    /// <param name="isPathPermitted">Правила доступа к каталогам (те же, что у файловых
    /// инструментов); null — запретов нет.</param>
    /// <param name="allowedDirs">ВНЕШНИЕ каталоги, открытые правилами безопасности задачи
    /// (<c>SecurityEvaluator.AllowedFullPathDirs</c>). Полный путь годится, только если он
    /// внутри папки проекта либо внутри одного из них: «писать можно в каталог проекта, а
    /// если в правилах указаны внешние каталоги — то и в них» (T-110-S0 §1.3.7).</param>
    public GatewayToolset(IReadOnlyList<GatewayPlugin> plugins, ObjectService? objects,
        TaskItem current, string? projectFolder, string? dataDir, string projectTitle = "",
        Func<string, string, bool>? isPathPermitted = null,
        IReadOnlyList<string>? allowedDirs = null)
    {
        _allowedDirs = allowedDirs ?? [];
        _plugins = plugins;
        _objects = objects;
        _current = current;
        _projectFolder = projectFolder;
        _dataDir = dataDir;
        _projectTitle = projectTitle;
        _isPathPermitted = isPathPermitted ?? ((_, _) => true);
    }

    /// <summary>Шлюзы работают у задач ПРОЕКТА: медиатека и папка проекта — у проекта.</summary>
    public bool CanUse => _plugins.Count > 0 && _objects is not null
                          && _current.ProjectId is { Length: > 0 }
                          && _projectFolder is { Length: > 0 };

    /// <summary>
    /// ИНСТРУМЕНТЫ, ОПУБЛИКОВАННЫЕ АГЕНТУ. Публикуется действие, у которого есть известная
    /// роль, а для рендера — ещё и найденная программа: показывать агенту инструмент, который
    /// заведомо ответит отказом, значит тратить его ход на выяснение этого (то же правило,
    /// по которому медиатека не публикуется у задачи без проекта).
    /// </summary>
    public IEnumerable<FileToolset.ToolSpec> Specs =>
        !CanUse ? [] : _plugins.SelectMany(p => p.Manifest.Actions
            .Where(a => Available(p, a))
            .Select(a => new FileToolset.ToolSpec(a.Tool, "", SchemaOf(p, a))));

    /// <summary>Инструмент этого набора (для диспетчеризации в AgentToolset).</summary>
    public bool Handles(string name) => Find(name) is not null;

    /// <summary>Выполнить вызов; результат (или текст отказа) возвращается агенту.</summary>
    public async Task<string> ExecuteAsync(string name, JsonElement args, CancellationToken ct)
    {
        var found = Find(name);
        if (found is null)
        {
            return Loc.In(Language, "prompt.gateway.1", name);
        }
        var (plugin, action) = found.Value;
        try
        {
            var result = action.Role switch
            {
                PluginAction.RoleExport => Export(plugin, args),
                PluginAction.RoleRender => await RenderAsync(plugin, args, ct),
                PluginAction.RoleConvert => await ConvertAsync(plugin, action, args, ct),
                _ => Loc.In(Language, "prompt.gateway.2", name, action.Role),
            };
            CallLog.Add($"{name}({args.GetRawText()}) → {Preview(result)}");
            return result;
        }
        // Win32Exception здесь не роскошь: программа, записанная в config.json руками, может
        // не существовать или не быть программой вовсе, и Process.Start бросает именно её —
        // без этой ветки отказ внешней программы валил бы весь ход агента исключением
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            var error = Loc.In(Language, "prompt.gateway.3", name, ex.Message);
            CallLog.Add(error);
            return error;
        }
    }

    // --- СБОРКА МОНТАЖНОГО ЛИСТА ---

    /// <summary>
    /// Собрать файл проекта редактора из медиатеки. Порядок клипов — ТОТ ЖЕ, что у
    /// <c>media_list</c> (сцена, порядок, дубль, код объекта): человек, посмотревший список,
    /// обязан увидеть в редакторе ровно его, иначе список бесполезен.
    /// </summary>
    private string Export(GatewayPlugin plugin, JsonElement args)
    {
        var format = plugin.Manifest.Export;
        if (format is null)
        {
            return Loc.In(Language, "prompt.gateway.4", plugin.Manifest.Code);
        }
        format = WithSettings(format, plugin.SettingsJson);
        var target = Target(format.File, Str(args, "file"), out var refusal);
        if (target is null)
        {
            return refusal;
        }
        var clips = new List<TimelineClip>();
        var skipped = new List<string>();
        var dir = Path.GetDirectoryName(target)!;
        foreach (var (item, meta) in Library(args))
        {
            // ПУТЬ, ВПИСЫВАЕМЫЙ ВНУТРЬ ФАЙЛА ПРОЕКТА, ПРОВЕРЯЕТСЯ ОТДЕЛЬНО от пути записи:
            // абсолютный путь в медиатеке (заведённый в обход формы) уехал бы в .mlt и
            // сделал бы монтажный лист непереносимым
            var abs = ObjectFiles.Resolve(item.PathOrUrl, _projectFolder, _dataDir);
            if (abs is null || !_isPathPermitted(abs, "read"))
            {
                skipped.Add(item.DisplayId);
                continue;
            }
            var placed = Place(abs, item.PathOrUrl, dir, format, skipped, item.DisplayId);
            if (placed is null)
            {
                continue;
            }
            clips.Add(new TimelineClip
            {
                Path = placed,
                Kind = meta.Kind,
                Name = item.Name,
                Caption = meta.Caption,
                Scene = meta.Scene,
                Take = meta.Take,
                Order = meta.Order,
                DurationSec = meta.DurationSec,
            });
        }
        if (clips.Count == 0)
        {
            return Loc.In(Language, "prompt.gateway.5");
        }
        var text = TimelineExport.Build(format, clips,
            _projectTitle.Length > 0 ? _projectTitle : _current.DisplayId);
        Directory.CreateDirectory(dir);
        // БЕЗ BOM: melt и Kdenlive читают файл разборщиком XML, а BOM перед объявлением
        // <?xml ...?> делает документ негодным
        File.WriteAllText(target, text, new UTF8Encoding(false));
        var rel = ProjectFiles.Rel(_projectFolder!, target);
        return Loc.In(Language, "prompt.gateway.6", rel, clips.Count,
            skipped.Count == 0 ? "" : Loc.In(Language, "prompt.gateway.7", string.Join(", ", skipped)));
    }

    /// <summary>
    /// ПУТЬ КЛИПА ДЛЯ ФАЙЛА ПРОЕКТА — относительно каталога выходного файла.
    ///
    /// <para>Ресурс ХРАНИЛИЩА организации (<c>store:</c>) лежит вне папки проекта, и ссылка на
    /// него из монтажного листа была бы либо абсолютной, либо с <c>..</c> — то есть неверной
    /// на любом другом компьютере кластера. Поэтому он КОПИРУЕТСЯ в подкаталог рядом с
    /// выходным файлом; копия с тем же размером второй раз не делается — экспорт зовут после
    /// каждого нового дубля.</para>
    /// </summary>
    private string? Place(string abs, string stored, string dir, TimelineFormat format,
        List<string> skipped, string code)
    {
        // РЕСУРС ХРАНИЛИЩА ОРГАНИЗАЦИИ КОПИРУЕТСЯ ВСЕГДА, остальные — только если иначе их
        // не адресовать: путь, уходящий из каталога выходного файла «вверх» (<c>..</c>),
        // на другом компьютере кластера указывает не туда, а абсолютный не указывает никуда
        var relative = Rel(dir, abs);
        if ((ObjectFiles.IsStore(stored) || relative is null) && format.CopyStore
            && File.Exists(abs))
        {
            var copy = Path.Combine(dir, format.StoreDir, Path.GetFileName(abs));
            if (!_isPathPermitted(copy, "write"))
            {
                skipped.Add(code);
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            if (!File.Exists(copy) || new FileInfo(copy).Length != new FileInfo(abs).Length)
            {
                File.Copy(abs, copy, true);
            }
            relative = Rel(dir, copy);
        }
        if (relative is null)
        {
            skipped.Add(code);
        }
        return relative;
    }

    /// <summary>Путь относительно каталога выходного файла; null — так его не адресовать
    /// (уходит «вверх» или лежит на другом диске).</summary>
    private static string? Rel(string dir, string abs)
    {
        var rel = Path.GetRelativePath(dir, abs).Replace('\\', '/');
        return Path.IsPathRooted(rel) || rel.StartsWith("..", StringComparison.Ordinal)
            ? null
            : rel;
    }

    // --- РЕНДЕР БЕЗ ОКНА ---

    private async Task<string> RenderAsync(GatewayPlugin plugin, JsonElement args,
        CancellationToken ct)
    {
        var render = plugin.Manifest.Render;
        if (render is null)
        {
            return Loc.In(Language, "prompt.gateway.8", plugin.Manifest.Code);
        }
        if (plugin.SoftwarePath.Length == 0)
        {
            return Loc.In(Language, "prompt.gateway.9", plugin.Manifest.Code);
        }
        // ЧТО ИСПОЛНЯЕМ, У ШЛЮЗА-СКРИПТА ВЫБИРАЕТ НЕ АГЕНТ (T-118-S0): у Blender рендер это
        // «--python <файл>», и право назвать файл означало бы «выполни этот Python». Поэтому
        // при ownFileOnly параметр file не читается вовсе — берётся наш собранный файл
        var asked = plugin.Manifest.Render?.OwnFileOnly == true ? null : Str(args, "file");
        var source = Target(plugin.Manifest.Export?.File ?? "", asked, out var refusal);
        if (source is null)
        {
            return refusal;
        }
        if (!File.Exists(source))
        {
            return Loc.In(Language, "prompt.gateway.10", ProjectFiles.Rel(_projectFolder!, source));
        }
        var wanted = Str(args, "out");
        var target = Target(Path.GetFileNameWithoutExtension(source) + render.OutExt, wanted,
            out refusal);
        if (target is null)
        {
            return refusal;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var info = new ProcessStartInfo
        {
            FileName = plugin.SoftwarePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(source)!,
        };
        // ПРОИЗВОЛЬНОЙ КОМАНДНОЙ СТРОКИ У АГЕНТА НЕТ (правило T-110-S0 §1.3.7): аргументы
        // берутся из манифеста, а от агента приходят только два пути, и оба проверены выше
        foreach (var arg in render.Args)
        {
            info.ArgumentList.Add(arg.Replace("{file}", source).Replace("{out}", target));
        }
        if (render.EnvHere())
        {
            foreach (var (name, value) in render.Env)
            {
                info.Environment[name] = value;
            }
        }
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException(
                                Loc.In(Language, "prompt.gateway.11", plugin.SoftwarePath));
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(render.TimeoutSec));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            return Loc.In(Language, "prompt.gateway.12", render.TimeoutSec);
        }
        var tail = Tail(await output + "\n" + await errors);
        if (process.ExitCode != 0 || !File.Exists(target))
        {
            return Loc.In(Language, "prompt.gateway.13", process.ExitCode, tail);
        }
        return Loc.In(Language, "prompt.gateway.14", ProjectFiles.Rel(_projectFolder!, target),
            new FileInfo(target).Length);
    }

    // --- ИМЕНОВАННЫЕ ОПЕРАЦИИ КОНВЕРТОРА (T-116-S0) ---

    /// <summary>
    /// ВЫПОЛНИТЬ ОДНУ ИМЕНОВАННУЮ ОПЕРАЦИЮ БЛОКА <c>convert</c>: перекодировать в
    /// промежуточный формат монтажа, извлечь звук, склеить по списку, привести к общему кадру.
    ///
    /// <para>ЧТО ПРИХОДИТ ОТ АГЕНТА: только ПУТИ (<c>in</c> / <c>files</c> / <c>out</c>) и
    /// ничего больше. Аргументы командной строки целиком берутся из манифеста, параметры
    /// работы (кодек, профиль, размер кадра) — из настроек записи плагина. Действия
    /// «выполнить командную строку ffmpeg» в системе нет намеренно: это исполнение
    /// произвольного кода с правом писать файлы, и правилом безопасности его не сузить
    /// (требование заказчика к T-116-S0).</para>
    ///
    /// <para>ПУТИ ПРОВЕРЯЮТСЯ ВСЕ И ОБОИХ РОДОВ — и входные (чтение), и выходной с
    /// файлом-списком (запись): годится путь внутри папки проекта либо внутри каталога,
    /// открытого правилами безопасности задачи.</para>
    /// </summary>
    private async Task<string> ConvertAsync(GatewayPlugin plugin, PluginAction action,
        JsonElement args, CancellationToken ct)
    {
        var ops = plugin.Manifest.Convert;
        var op = ops?.Find(action.Op);
        if (ops is null || op is null)
        {
            return Loc.In(Language, "prompt.ffmpeg.1", plugin.Manifest.Code, action.Op);
        }
        if (plugin.SoftwarePath.Length == 0)
        {
            return Loc.In(Language, "prompt.gateway.9", plugin.Manifest.Code);
        }
        var sources = Sources(op, args, out var refusal);
        if (sources is null)
        {
            return refusal;
        }
        var first = sources[0];
        var ext = op.OutExt.Length > 0 ? op.OutExt : Path.GetExtension(first);
        var own = (op.OutName.Length > 0
            ? op.OutName
            : Path.GetFileNameWithoutExtension(first) + op.OutSuffix) + ext;
        // умолчание кладётся РЯДОМ С ИСХОДНИКОМ, а не в корень папки проекта: материал лежит
        // по папкам сцен, и результат, уехавший в корень, человек ищет руками
        var target = Target(ProjectFiles.Rel(_projectFolder!, Path.Combine(
            Path.GetDirectoryName(first)!, own)), Str(args, "out"), out refusal);
        if (target is null)
        {
            return refusal;
        }
        if (sources.Any(s => string.Equals(s, target, StringComparison.OrdinalIgnoreCase)))
        {
            // ffmpeg с ключом -y затёр бы исходник ПУСТЫМ файлом ещё до чтения
            return Loc.In(Language, "prompt.ffmpeg.2", ProjectFiles.Rel(_projectFolder!, target));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var listFile = "";
        if (op.IsList)
        {
            var list = Target(ProjectFiles.Rel(_projectFolder!, Path.Combine(
                Path.GetDirectoryName(target)!, op.ListFile)), null, out refusal);
            if (list is null)
            {
                return refusal;
            }
            File.WriteAllText(list, ConvertLine.ListText(op, sources), new UTF8Encoding(false));
            listFile = list;
        }
        var values = ConvertLine.Values(plugin.Manifest.Settings, plugin.SettingsJson);
        var line = ConvertLine.Build(op, first, target, listFile, values, out var unresolved);
        if (line is null)
        {
            // нераскрытый плейсхолдер — дефект манифеста; отдать его ffmpeg значит получить
            // невнятный отказ программы вместо внятного отказа плагина
            return Loc.In(Language, "prompt.ffmpeg.3", unresolved);
        }
        return await RunConvertAsync(plugin, op, ops, line, target, ct);
    }

    /// <summary>Запустить конвертор отдельным процессом, дождаться с отметками хода работы,
    /// отказать по таймауту.</summary>
    private async Task<string> RunConvertAsync(GatewayPlugin plugin, ConvertOp op,
        ConvertOps ops, List<string> line, string target, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = plugin.SoftwarePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(target)!,
        };
        foreach (var arg in line)
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException(
                                Loc.In(Language, "prompt.gateway.11", plugin.SoftwarePath));
        // ОТМЕТКИ ХОДА РАБОТЫ идут из потока «-progress pipe:1»: конвертация часового ролика
        // это десятки минут, и по таймауту надо знать, СТОЯЛ ли конвертор или просто не успел
        var marks = new List<string>();
        var reader = Progress(process, ops.ProgressEverySec, marks, ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        var seconds = op.TimeoutSec > 0 ? op.TimeoutSec : ops.TimeoutSec;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            CallLog.AddRange(marks);
            return Loc.In(Language, "prompt.ffmpeg.4", op.Op, seconds, Seconds(await reader));
        }
        var done = await reader;
        CallLog.AddRange(marks);
        if (process.ExitCode != 0 || !File.Exists(target))
        {
            return Loc.In(Language, "prompt.ffmpeg.5", op.Op, process.ExitCode,
                Tail(await errors));
        }
        return Loc.In(Language, "prompt.ffmpeg.6", op.Op,
            ProjectFiles.Rel(_projectFolder!, target), new FileInfo(target).Length, Seconds(done));
    }

    /// <summary>
    /// ЧИТАТЕЛЬ ПОТОКА ХОДА РАБОТЫ. ffmpeg с ключом <c>-progress pipe:1</c> печатает в вывод
    /// строки вида <c>out_time_us=1234567</c>; из них складывается отметка «обработано N с
    /// материала». Возвращается последняя достигнутая позиция — она же уходит в отказ по
    /// таймауту.
    /// </summary>
    private static async Task<double> Progress(Process process, int everySec,
        List<string> marks, CancellationToken ct)
    {
        var seconds = 0d;
        var next = TimeSpan.FromSeconds(everySec);
        var clock = Stopwatch.StartNew();
        while (await process.StandardOutput.ReadLineAsync(ct) is { } text)
        {
            var value = text.StartsWith("out_time_us=", StringComparison.Ordinal) ? 1_000_000d
                : text.StartsWith("out_time_ms=", StringComparison.Ordinal) ? 1_000d
                : 0d;
            if (value > 0 && double.TryParse(text[(text.IndexOf('=') + 1)..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var raw))
            {
                seconds = raw / value;
            }
            if (clock.Elapsed >= next)
            {
                marks.Add($"ffmpeg: {clock.Elapsed.TotalSeconds:F0} s → {seconds:F1} s");
                next = clock.Elapsed + TimeSpan.FromSeconds(everySec);
            }
        }
        return seconds;
    }

    /// <summary>
    /// ВХОДНЫЕ ФАЙЛЫ ОПЕРАЦИИ. Один файл — параметр <c>in</c>; список — параметр <c>files</c>,
    /// а если его нет, берётся МЕДИАТЕКА ПРОЕКТА тем же порядком и с тем же отбором, что
    /// печатает <c>media_list</c> (склеивать «по списку» удобнее всего именно её). null —
    /// отказ, его текст лежит в <paramref name="refusal"/>.
    /// </summary>
    private List<string>? Sources(ConvertOp op, JsonElement args, out string refusal)
    {
        refusal = "";
        var wanted = new List<string>();
        if (op.IsList)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("files", out var files)
                && files.ValueKind == JsonValueKind.Array)
            {
                wanted.AddRange(files.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0));
            }
            if (wanted.Count == 0)
            {
                wanted.AddRange(Library(args)
                    .Select(r => ObjectFiles.Resolve(r.Item.PathOrUrl, _projectFolder, _dataDir))
                    .Where(p => p is not null).Select(p => p!));
            }
        }
        else if (Str(args, "in") is { Length: > 0 } one)
        {
            wanted.Add(one);
        }
        if (wanted.Count == 0)
        {
            refusal = Loc.In(Language, "prompt.ffmpeg.7", op.Op);
            return null;
        }
        var found = new List<string>();
        foreach (var path in wanted)
        {
            var abs = Source(path, out refusal);
            if (abs is null)
            {
                return null;
            }
            found.Add(abs);
        }
        return found;
    }

    /// <summary>ВХОДНОЙ ПУТЬ: те же ограничения, что у выходного (папка проекта либо каталог,
    /// открытый правилами задачи), плюс право чтения и существование файла.</summary>
    private string? Source(string wanted, out string refusal)
    {
        refusal = "";
        string abs;
        if (Path.IsPathRooted(wanted))
        {
            abs = Path.GetFullPath(wanted);
            if (!Inside(abs, _projectFolder) && !_allowedDirs.Any(dir => Inside(abs, dir)))
            {
                refusal = Loc.In(Language, "prompt.gateway.16", wanted);
                return null;
            }
        }
        else
        {
            var inside = ProjectFiles.Resolve(_projectFolder, wanted);
            if (inside is null)
            {
                refusal = Loc.In(Language, "prompt.gateway.16", wanted);
                return null;
            }
            abs = inside;
        }
        if (!_isPathPermitted(abs, "read"))
        {
            refusal = Loc.In(Language, "prompt.gateway.17", wanted);
            return null;
        }
        if (!File.Exists(abs))
        {
            refusal = Loc.In(Language, "prompt.ffmpeg.8", wanted);
            return null;
        }
        return abs;
    }

    /// <summary>Обработанные секунды материала — В ОТВЕТ АГЕНТУ, с одним знаком после запятой:
    /// сырое double даёт «4,0100000000000002» и читается как поломка.</summary>
    private static string Seconds(double value) => value.ToString("F1");

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // процесс уже кончился сам — гасить нечего
        }
    }

    // --- ОБЩЕЕ ---

    /// <summary>
    /// КУДА ПИШЕМ. Пусто — свой файл манифеста в корне папки проекта. Относительный путь
    /// считается от папки проекта и за её край не выпускается; полный путь годится, только
    /// если его открыли правила безопасности задачи (внешние каталоги, T-110-S0 §1.3.7).
    /// </summary>
    private string? Target(string ownFile, string? asked, out string refusal)
    {
        refusal = "";
        var wanted = (asked ?? "").Trim();
        if (wanted.Length == 0)
        {
            wanted = ownFile;
        }
        if (wanted.Length == 0)
        {
            refusal = Loc.In(Language, "prompt.gateway.15");
            return null;
        }
        string abs;
        if (Path.IsPathRooted(wanted))
        {
            abs = Path.GetFullPath(wanted);
            // ПОЛНЫЙ ПУТЬ ГОДИТСЯ ТОЛЬКО ВНУТРИ ОТКРЫТОГО КАТАЛОГА. Проверять это одним лишь
            // правилом безопасности нельзя: у задачи без правил «запрета нет», и тогда шлюз
            // писал бы куда угодно на диске — а его ограничение работает БУКВАЛЬНО
            if (!Inside(abs, _projectFolder) && !_allowedDirs.Any(dir => Inside(abs, dir)))
            {
                refusal = Loc.In(Language, "prompt.gateway.16", wanted);
                return null;
            }
        }
        else
        {
            var inside = ProjectFiles.Resolve(_projectFolder, wanted);
            if (inside is null)
            {
                refusal = Loc.In(Language, "prompt.gateway.16", wanted);
                return null;
            }
            abs = inside;
        }
        if (!_isPathPermitted(abs, "write"))
        {
            refusal = Loc.In(Language, "prompt.gateway.17", wanted);
            return null;
        }
        return abs;
    }

    /// <summary>Путь лежит внутри каталога (или это он сам).</summary>
    private static bool Inside(string abs, string? dir)
    {
        if (dir is not { Length: > 0 })
        {
            return false;
        }
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        return abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(abs, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Медиатека проекта с отбором по сцене, виду и тэгу — тем же порядком, что
    /// печатает <c>media_list</c>.</summary>
    private List<(ObjectItem Item, MediaMeta Meta)> Library(JsonElement args)
    {
        var scene = (Str(args, "scene") ?? "").Trim();
        var kind = (Str(args, "kind") ?? "").Trim().ToLowerInvariant();
        var tag = (Str(args, "tag") ?? "").Trim();
        return [.. _objects!.List(_current.ProjectId!, kinds: [ObjectKinds.Media],
                tags: tag.Length > 0 ? [tag] : null)
            .Select(o => (Item: o, Meta: MediaMeta.Parse(o.MetaJson)))
            .Where(r => scene.Length == 0
                        || string.Equals(r.Meta.Scene, scene, StringComparison.CurrentCultureIgnoreCase))
            .Where(r => kind.Length == 0 || r.Meta.Kind == kind)
            .OrderBy(r => r.Meta.Scene, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Meta.Order == 0 ? int.MaxValue : r.Meta.Order)
            .ThenBy(r => r.Meta.Take)
            .ThenBy(r => DisplayIds.Order(r.Item.DisplayId))];
    }

    /// <summary>
    /// НАСТРОЙКИ ЗАПИСИ ПОВЕРХ ФОРМАТА: кадры в секунду и размер кадра человек задаёт в форме
    /// плагина, а не правкой файла дистрибутива (правка манифеста уехала бы при обновлении).
    /// Остальные поля формата — устройство самого формата, и настройкой их не открывают.
    /// </summary>
    private static TimelineFormat WithSettings(TimelineFormat format, string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return format;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return format;
            }
            format.Fps = JsonRead.Num(doc.RootElement, "fps", format.Fps);
            format.Width = JsonRead.Int(doc.RootElement, "width", format.Width);
            format.Height = JsonRead.Int(doc.RootElement, "height", format.Height);
        }
        catch (JsonException)
        {
            // настройки правит человек — испорченный JSON не повод отказать в экспорте
        }
        return format;
    }

    private (GatewayPlugin Plugin, PluginAction Action)? Find(string tool)
    {
        foreach (var plugin in _plugins)
        {
            var action = plugin.Manifest.Actions.FirstOrDefault(a =>
                string.Equals(a.Tool, tool, StringComparison.Ordinal) && Available(plugin, a));
            if (action is not null)
            {
                return (plugin, action);
            }
        }
        return null;
    }

    /// <summary>Действие можно выполнить здесь: роль известна, а рендеру нужна ещё и
    /// найденная программа.</summary>
    private static bool Available(GatewayPlugin plugin, PluginAction action) =>
        action.Role switch
        {
            PluginAction.RoleExport => plugin.Manifest.Export is not null,
            PluginAction.RoleRender => plugin.Manifest.Render is not null
                                       && plugin.SoftwarePath.Length > 0,
            // у конвертора программа нужна ВСЕГДА (перекодировать нечем), и операция обязана
            // быть названа: «сделать что-нибудь конвертором» — это произвольная команда
            PluginAction.RoleConvert => plugin.Manifest.Convert?.Find(action.Op) is not null
                                        && plugin.SoftwarePath.Length > 0,
            _ => false,
        };

    private static string SchemaOf(GatewayPlugin plugin, PluginAction action)
    {
        if (action.Role == PluginAction.RoleConvert)
        {
            return plugin.Manifest.Convert?.Find(action.Op)?.IsList == true
                ? """
                  {
                    "type": "object",
                    "properties": {
                      "files": { "type": "array", "items": { "type": "string" } },
                      "scene": { "type": "string" },
                      "kind":  { "type": "string" },
                      "tag":   { "type": "string" },
                      "out":   { "type": "string" }
                    }
                  }
                  """
                : """
                  {
                    "type": "object",
                    "properties": {
                      "in":  { "type": "string" },
                      "out": { "type": "string" }
                    },
                    "required": ["in"]
                  }
                  """;
        }
        return action.Role == PluginAction.RoleRender
        // ШЛЮЗ-СКРИПТ (Blender, T-118-S0) параметра «что запускать» агенту НЕ ОБЪЯВЛЯЕТ:
        // объявленный, он читался бы как «выполни этот Python». Исполняется только наш файл
        ? plugin.Manifest.Render?.OwnFileOnly == true
          ? """
            {
              "type": "object",
              "properties": {
                "out": { "type": "string" }
              }
            }
            """
          : """
          {
            "type": "object",
            "properties": {
              "file": { "type": "string" },
              "out":  { "type": "string" }
            }
          }
          """
        : """
          {
            "type": "object",
            "properties": {
              "scene": { "type": "string" },
              "kind":  { "type": "string" },
              "tag":   { "type": "string" },
              "file":  { "type": "string" }
            }
          }
          """;
    }

    private static string? Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Хвост вывода программы — в отказ агенту: причина отказа melt печатается
    /// последними строками, а весь его вывод в контекст агента не нужен.</summary>
    private static string Tail(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", lines.TakeLast(12));
    }

    private static string Preview(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
