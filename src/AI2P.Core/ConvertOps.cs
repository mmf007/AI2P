using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// БЛОК <c>convert</c> МАНИФЕСТА — НАБОР ИМЕНОВАННЫХ ОПЕРАЦИЙ НАД ФАЙЛАМИ (T-116-S0, плагин
/// <c>tool.ffmpeg</c>).
///
/// <para>ЗАЧЕМ ОН ОТДЕЛЬНО ОТ <see cref="RenderCommand"/>: рендер — это ОДНА команда шлюза
/// («посчитай мой монтажный лист»), а конвертор — НЕСКОЛЬКО РАЗНЫХ работ над произвольным
/// файлом («перекодируй в промежуточный формат», «вынь звук», «склей по списку», «приведи к
/// общему кадру»). Разные работы — разные аргументы, разное расширение результата и разное
/// число входов, поэтому у каждой своя запись, а у каждой записи — своё имя
/// (<see cref="ConvertOp.Op"/>), на которое ссылается действие манифеста полем <c>op</c>.</para>
///
/// <para>ГЛАВНОЕ ПРАВИЛО БЕЗОПАСНОСТИ ВЕТКИ (T-110-S0 §1.3.7, требование заказчика к
/// T-116-S0): действия обязаны быть УЗКИМИ И ИМЕНОВАННЫМИ, а действия «выполнить командную
/// строку ffmpeg» быть не должно. Поэтому аргументы целиком лежат ЗДЕСЬ, в файле
/// дистрибутива, а от агента приходят только пути (они проверяются) и НИЧЕГО больше:
/// произвольная строка от модели — это исполнение чужого кода с правом писать файлы, и
/// сузить его правилом безопасности нечем (<c>-f lavfi</c>, <c>concat:</c>, <c>file:</c>
/// протоколы, <c>-y</c> поверх любого файла).</para>
///
/// <para>ПАРАМЕТРЫ РАБОТЫ (кодек, профиль, размер кадра) человек задаёт НАСТРОЙКОЙ ЗАПИСИ
/// плагина, а не правкой этого файла: файл дистрибутива перезаписывается обновлением. В
/// аргументах они стоят плейсхолдерами <c>{ключ настройки}</c> и подставляются из
/// <c>PluginRecord.SettingsJson</c>, а чего человек не задал — из умолчания
/// <see cref="PluginSetting.Default"/>.</para>
/// </summary>
public sealed class ConvertOps
{
    /// <summary>Сколько ждать конвертации по умолчанию, секунд (у операции может быть своё).</summary>
    public int TimeoutSec { get; set; } = 3600;

    /// <summary>Как часто отмечать ход работы в журнале вызовов, секунд.</summary>
    public int ProgressEverySec { get; set; } = 15;

    /// <summary>Операции по именам.</summary>
    public List<ConvertOp> Ops { get; set; } = [];

    /// <summary>Операция с этим именем; null — такой в манифесте нет.</summary>
    public ConvertOp? Find(string op) =>
        Ops.FirstOrDefault(o => string.Equals(o.Op, op, StringComparison.OrdinalIgnoreCase));

    /// <summary>Блок описан: есть хоть одна операция.</summary>
    public bool IsValid => Ops.Count > 0;

    internal static ConvertOps? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var ops = new ConvertOps
        {
            TimeoutSec = Math.Max(1, JsonRead.Int(v, "timeoutSec", 3600)),
            ProgressEverySec = Math.Max(1, JsonRead.Int(v, "progressEverySec", 15)),
        };
        if (v.TryGetProperty("ops", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            ops.Ops = [.. list.EnumerateArray().Select(ConvertOp.From)
                .Where(o => o is not null).Select(o => o!)];
        }
        return ops.IsValid ? ops : null;
    }
}

/// <summary>
/// СБОРКА КОМАНДНОЙ СТРОКИ КОНВЕРТОРА — отдельно от запуска процесса (T-116-S0).
///
/// <para>ПОЧЕМУ ОТДЕЛЬНО: строка, которую получает внешняя программа, — это и есть граница
/// безопасности плагина, и проверять её надо БЕЗ запуска ffmpeg. Эталонная строка на каждое
/// действие лежит в <c>T116S0Tests</c>: подставленный не туда ключ («-c:a» вместо «-c:v»)
/// иначе замечается только по испорченному файлу через полчаса счёта.</para>
///
/// <para>ЗДЕСЬ НЕТ И НЕ ДОЛЖНО БЫТЬ НИ ОДНОЙ СТРОКИ ОТ МОДЕЛИ: аргументы приходят из
/// манифеста, значения — из настроек записи плагина, а от агента только пути, и они
/// подставляются готовыми (проверку путей делает вызывающий).</para>
/// </summary>
public static class ConvertLine
{
    /// <summary>
    /// ЗНАЧЕНИЯ НАСТРОЕК ЗАПИСИ ПЛАГИНА для подстановки в аргументы: что задал человек, а чего
    /// не задал — умолчание из манифеста. Значение поля-перечисления, которого нет среди
    /// объявленных (<see cref="PluginSetting.Choices"/>), заменяется умолчанием: опечатка в
    /// имени кодека иначе стала бы отказом ffmpeg через полчаса счёта, а испорченный человеком
    /// JSON настроек — не повод отказать в работе вовсе.
    /// </summary>
    public static Dictionary<string, string> Values(IReadOnlyList<PluginSetting> declared,
        string? settingsJson)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in declared)
        {
            values[setting.Key] = setting.Default;
        }
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return values;
        }
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return values;
            }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var text = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                        p.Value.GetRawText(),
                    _ => "",
                };
                var setting = declared.FirstOrDefault(s => s.Key == p.Name);
                if (text.Length == 0 || setting is null)
                {
                    continue;
                }
                if (setting.Choices.Count > 0
                    && !setting.Choices.Contains(text, StringComparer.Ordinal))
                {
                    continue;
                }
                values[p.Name] = text;
            }
        }
        catch (JsonException)
        {
            // настройки правит человек — испорченный JSON не повод отказать в работе
        }
        return values;
    }

    /// <summary>
    /// Собрать аргументы запуска: <c>{in}</c>, <c>{out}</c>, <c>{list}</c> — пути (уже
    /// проверенные вызывающим), остальные фигурные скобки — ключи настроек. null — в манифесте
    /// остался НЕРАСКРЫТЫЙ плейсхолдер (его текст ложится в <paramref name="unresolved"/>):
    /// отдать такой аргумент ffmpeg значит получить невнятный отказ программы вместо внятного
    /// отказа плагина.
    /// </summary>
    public static List<string>? Build(ConvertOp op, string input, string output, string listFile,
        IReadOnlyDictionary<string, string> values, out string unresolved) =>
        Build(op.Args, input, output, listFile, values, out unresolved);

    /// <summary>
    /// То же по ГОТОВОМУ СПИСКУ АРГУМЕНТОВ — им собирается командная строка операции долгого
    /// запуска (<see cref="RunOp"/>, T-155-S0): правила подстановки и проверка нераскрытых
    /// плейсхолдеров у обоих блоков манифеста одни и те же, и разводить их по двум разным
    /// сборщикам значило бы чинить одну и ту же беду дважды.
    /// <paramref name="workDir"/> подставляется под <c>{work}</c>.
    /// </summary>
    public static List<string>? Build(IReadOnlyList<string> args, string input, string output,
        string listFile, IReadOnlyDictionary<string, string> values, out string unresolved,
        string workDir = "")
    {
        unresolved = "";
        var line = new List<string>();
        foreach (var arg in args)
        {
            var one = Fill(arg.Replace("{in}", input).Replace("{out}", output)
                .Replace("{list}", listFile).Replace("{work}", workDir), values);
            if (one.Contains('{') && one.Contains('}'))
            {
                unresolved = arg;
                return null;
            }
            line.Add(one);
        }
        return line;
    }

    /// <summary>Строки файла-списка склейки: <c>file '…'</c> с экранированием апострофа —
    /// имя файла с кавычкой иначе оборвало бы строку и склеило бы не то.</summary>
    public static string ListText(ConvertOp op, IEnumerable<string> paths) =>
        string.Join("\n", paths.Select(p => op.ListLine.Replace("{path}",
            p.Replace("'", @"'\''")))) + "\n";

    /// <summary>Подставить значения настроек записи плагина в строку («{fps}» → «25»);
    /// значений нет — строка возвращается как есть. Тем же подставляются рабочий каталог и
    /// маска результата операции долгого запуска (T-155-S0).</summary>
    public static string Fill(string arg, IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || !arg.Contains('{'))
        {
            return arg;
        }
        foreach (var (key, value) in values)
        {
            arg = arg.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }
        return arg;
    }
}

/// <summary>
/// ОДНА ИМЕНОВАННАЯ ОПЕРАЦИЯ КОНВЕРТОРА: «перекодировать в промежуточный формат монтажа»,
/// «извлечь звук», «склеить по списку», «привести к общему кадру и частоте».
/// </summary>
public sealed class ConvertOp
{
    /// <summary>Один входной файл (параметр <c>in</c> у агента).</summary>
    public const string InputsOne = "one";

    /// <summary>СПИСОК входных файлов: они уходят не в аргументы, а в файл-список
    /// (<c>{list}</c>) — так устроен склеиватель <c>concat</c> у ffmpeg, и так же длинный
    /// список не упирается в предел длины командной строки.</summary>
    public const string InputsList = "list";

    /// <summary>Имя операции: на него ссылается действие манифеста полем <c>op</c>.</summary>
    public string Op { get; set; } = "";

    /// <summary>Что подаётся на вход: <see cref="InputsOne"/> или <see cref="InputsList"/>.</summary>
    public string Inputs { get; set; } = InputsOne;

    /// <summary>Аргументы командной строки. Плейсхолдеры: <c>{in}</c> — входной файл,
    /// <c>{out}</c> — файл результата, <c>{list}</c> — файл-список, остальные — ключи
    /// настроек записи плагина.</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>Расширение файла результата (<c>.mov</c>); ПУСТО — берётся расширение
    /// первого входного файла (так у склейки: что склеили, то и получилось).</summary>
    public string OutExt { get; set; } = "";

    /// <summary>Имя файла результата по умолчанию БЕЗ расширения; пусто — имя входного
    /// файла с приставкой <see cref="OutSuffix"/>.</summary>
    public string OutName { get; set; } = "";

    /// <summary>Хвост, который приписывается к имени входного файла у результата:
    /// <c>_edit</c>. Нужен, чтобы результат не лёг ПОВЕРХ исходника при совпадении
    /// расширений — молча потерянный исходник хуже отказа.</summary>
    public string OutSuffix { get; set; } = "_out";

    /// <summary>Строка файла-списка: <c>file '{path}'</c>.</summary>
    public string ListLine { get; set; } = "file '{path}'";

    /// <summary>Имя файла-списка, который кладётся рядом с результатом.</summary>
    public string ListFile { get; set; } = "ai2p_concat.txt";

    /// <summary>Своё время ожидания, секунд; 0 — общее из блока.</summary>
    public int TimeoutSec { get; set; }

    /// <summary>Операция описана: есть имя и есть аргументы.</summary>
    public bool IsValid => Op.Length > 0 && Args.Count > 0;

    /// <summary>Операция принимает список файлов, а не один файл.</summary>
    public bool IsList => Inputs == InputsList;

    internal static ConvertOp? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var inputs = JsonRead.Str(e, "inputs", InputsOne).ToLowerInvariant();
        var op = new ConvertOp
        {
            Op = JsonRead.Str(e, "op"),
            Inputs = inputs == InputsList ? InputsList : InputsOne,
            Args = JsonRead.Strings(e, "args"),
            OutExt = JsonRead.Str(e, "outExt"),
            OutName = JsonRead.Str(e, "outName"),
            OutSuffix = JsonRead.Str(e, "outSuffix", "_out"),
            ListLine = JsonRead.Str(e, "listLine", "file '{path}'"),
            ListFile = JsonRead.Str(e, "listFile", "ai2p_concat.txt"),
            TimeoutSec = Math.Max(0, JsonRead.Int(e, "timeoutSec", 0)),
        };
        return op.IsValid ? op : null;
    }
}
