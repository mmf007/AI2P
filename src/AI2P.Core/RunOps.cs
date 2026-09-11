using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// БЛОК <c>run</c> МАНИФЕСТА — ИМЕНОВАННЫЕ ОПЕРАЦИИ ДОЛГОГО ЗАПУСКА (T-155-S0): обучение
/// LoRA, пересчёт индекса, длинная конвертация — то, что считается часами и оформляется
/// ОТДЕЛЬНОЙ ЗАДАЧЕЙ с исполнителем «авто ПО» (<see cref="Entities.ExecutorKind"/>.Software,
/// T-153-S0), а не вызовом инструмента внутри хода агента.
///
/// <para>ЗАЧЕМ ОТДЕЛЬНО ОТ <see cref="ConvertOps"/>. Конвертор — это короткая работа над
/// файлом, у него нет ни рабочего каталога, ни понятия «программа молчит, но жива», ни
/// правила «какой файл считать результатом»: у ffmpeg результат — это ровно тот файл, что
/// назван в <c>-o</c>. У долгого запуска всё три вопроса стоят: тренер пишет в свой каталог,
/// молчит между эпохами по четверть часа и кладёт результат файлом, имя которого заранее
/// неизвестно (<c>epoch-0007.safetensors</c>). Поэтому у операции есть
/// <see cref="RunOp.WorkDir"/>, два предела (<see cref="RunOp.IdleTimeoutSec"/> и
/// <see cref="RunOp.TimeoutSec"/>) и правило разбора результата
/// (<see cref="RunResult"/>).</para>
///
/// <para>ГРАНИЦА БЕЗОПАСНОСТИ ТА ЖЕ, ЧТО У КОНВЕРТОРА (T-110-S0 §1.3.7): аргументы целиком
/// лежат в файле дистрибутива, значения приходят из настроек записи плагина, а от задачи —
/// только пути и значения ОБЪЯВЛЕННЫХ настроек. Действия «выполни эту командную строку» нет
/// и здесь: произвольная строка от модели — это исполнение чужого кода.</para>
///
/// <para>КТО ЧИТАЕТ: <c>SoftwareConnector</c> (запуск, тайм-ауты, результат) и очередь
/// запуска — флаг <see cref="RunOp.SingleInstance"/> (<c>JobOrchestrator.IsBusy</c>).</para>
/// </summary>
public sealed class RunOps
{
    /// <summary>Тайм-аут МОЛЧАНИЯ по умолчанию, секунд (у операции может быть свой);
    /// 0 — умолчание коннектора.</summary>
    public int IdleTimeoutSec { get; set; }

    /// <summary>Общий предел длительности по умолчанию, секунд; 0 — умолчание коннектора.</summary>
    public int TimeoutSec { get; set; }

    /// <summary>Операции по именам.</summary>
    public List<RunOp> Ops { get; set; } = [];

    /// <summary>Операция с этим именем; null — такой в манифесте нет.</summary>
    public RunOp? Find(string op) =>
        Ops.FirstOrDefault(o => string.Equals(o.Op, op, StringComparison.OrdinalIgnoreCase));

    /// <summary>Блок описан: есть хоть одна операция.</summary>
    public bool IsValid => Ops.Count > 0;

    internal static RunOps? From(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var ops = new RunOps
        {
            IdleTimeoutSec = Math.Max(0, JsonRead.Int(v, "idleTimeoutSec", 0)),
            TimeoutSec = Math.Max(0, JsonRead.Int(v, "timeoutSec", 0)),
        };
        if (v.TryGetProperty("ops", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            ops.Ops = [.. list.EnumerateArray().Select(e => RunOp.From(e, ops))
                .Where(o => o is not null).Select(o => o!)];
        }
        return ops.IsValid ? ops : null;
    }
}

/// <summary>
/// ОДНА ОПЕРАЦИЯ ДОЛГОГО ЗАПУСКА: «обучить адаптер LoRA», «посчитать индекс».
/// Ссылается на неё действие манифеста полем <c>op</c> (<see cref="PluginAction.Op"/>).
/// </summary>
public sealed class RunOp
{
    /// <summary>Имя операции: на него ссылается действие манифеста полем <c>op</c>.</summary>
    public string Op { get; set; } = "";

    /// <summary>Аргументы командной строки. Плейсхолдеры те же, что у конвертора:
    /// <c>{in}</c> — входной файл, <c>{out}</c> — файл результата, <c>{work}</c> — рабочий
    /// каталог, остальные фигурные скобки — ключи настроек записи плагина.</summary>
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// РАБОЧИЙ КАТАЛОГ программы: относительный считается от папки проекта, полный берётся
    /// как есть. Пусто — папка проекта, а нет и её — каталог файла результата (так работал
    /// запуск до появления этого блока). Тренеру он важен: относительные пути в его
    /// конфигурации и каталог с промежуточными эпохами считаются именно отсюда.
    /// </summary>
    public string WorkDir { get; set; } = "";

    /// <summary>Тайм-аут МОЛЧАНИЯ, секунд: пока программа печатает, она жива, и обрывать её
    /// по длительности значит терять часы счёта. 0 — значение блока, а нет и его — умолчание
    /// коннектора.</summary>
    public int IdleTimeoutSec { get; set; }

    /// <summary>Общий предел длительности, секунд — крупный, «считает, но бесконечно».
    /// 0 — значение блока, а нет и его — умолчание коннектора.</summary>
    public int TimeoutSec { get; set; }

    /// <summary>ОДИН ЭКЗЕМПЛЯР: пока эта программа работает по одному заданию, второй запуск
    /// ЖДЁТ (а не падает). Обучение занимает видеокарту целиком, и второй запуск не «идёт
    /// медленнее», а отказывает по памяти. Тот же флаг есть у действия
    /// (<see cref="PluginAction.SingleInstance"/>) — довольно любого из двух.</summary>
    public bool SingleInstance { get; set; }

    /// <summary>Правило разбора результата: какой файл считать результатом работы.</summary>
    public RunResult Result { get; set; } = new();

    /// <summary>Имя файла результата по умолчанию БЕЗ расширения; пусто — имя операции.</summary>
    public string OutName { get; set; } = "";

    /// <summary>Расширение файла результата (<c>.safetensors</c>); пусто — <c>.out</c>.</summary>
    public string OutExt { get; set; } = "";

    /// <summary>Операция описана: есть имя и есть аргументы.</summary>
    public bool IsValid => Op.Length > 0 && Args.Count > 0;

    /// <summary>Тайм-аут молчания с учётом умолчаний: своё → блока → 0 («решает коннектор»).</summary>
    public int IdleOr(RunOps? block) => IdleTimeoutSec > 0 ? IdleTimeoutSec : block?.IdleTimeoutSec ?? 0;

    /// <summary>Общий предел с учётом умолчаний: своё → блока → 0 («решает коннектор»).</summary>
    public int TotalOr(RunOps? block) => TimeoutSec > 0 ? TimeoutSec : block?.TimeoutSec ?? 0;

    /// <summary>
    /// ОПЕРАЦИЯ КОНВЕРТОРА КАК ДОЛГИЙ ЗАПУСК — совместимость с плагинами, написанными до
    /// появления блока <c>run</c> (T-116-S0): исполнитель «авто ПО» умел запускать именно их,
    /// и отнимать эту возможность нельзя. Рабочего каталога и правила разбора результата у
    /// конвертора нет — берутся умолчания («папка проекта» и «результат там, куда велели»).
    /// </summary>
    public static RunOp FromConvert(ConvertOp op) => new()
    {
        Op = op.Op,
        Args = op.Args,
        TimeoutSec = op.TimeoutSec,
        OutName = op.OutName,
        OutExt = op.OutExt,
    };

    internal static RunOp? From(JsonElement e, RunOps block)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var op = new RunOp
        {
            Op = JsonRead.Str(e, "op"),
            Args = JsonRead.Strings(e, "args"),
            WorkDir = JsonRead.Str(e, "workDir"),
            IdleTimeoutSec = Math.Max(0, JsonRead.Int(e, "idleTimeoutSec", 0)),
            TimeoutSec = Math.Max(0, JsonRead.Int(e, "timeoutSec", 0)),
            SingleInstance = JsonRead.Bool(e, "singleInstance", false),
            OutName = JsonRead.Str(e, "outName"),
            OutExt = JsonRead.Str(e, "outExt"),
            Result = RunResult.From(e, "result"),
        };
        // умолчания блока проставляются здесь же: читающему не надо помнить про два уровня
        op.IdleTimeoutSec = op.IdleOr(block);
        op.TimeoutSec = op.TotalOr(block);
        return op.IsValid ? op : null;
    }
}

/// <summary>
/// ПРАВИЛО РАЗБОРА РЕЗУЛЬТАТА — какой файл считать результатом долгого запуска.
///
/// <para>Три вида, и все три встречаются у настоящих программ:</para>
/// <list type="bullet">
/// <item><b>out</b> (умолчание) — результат ровно там, куда мы велели писать (<c>{out}</c>);
/// так работает конвертор.</item>
/// <item><b>file</b> — путь назван манифестом (<see cref="Path"/>): относительный считается
/// от рабочего каталога, плейсхолдеры настроек подставляются.</item>
/// <item><b>newest</b> — САМЫЙ СВЕЖИЙ файл каталога <see cref="Dir"/> по маске
/// <see cref="Pattern"/>: имя результата заранее неизвестно (тренер пишет
/// <c>epoch-0007.safetensors</c>), и назвать его иначе нечем.</item>
/// </list>
/// </summary>
public sealed class RunResult
{
    public const string KindOut = "out";
    public const string KindFile = "file";
    public const string KindNewest = "newest";

    /// <summary>Вид правила: <see cref="KindOut"/> / <see cref="KindFile"/> / <see cref="KindNewest"/>.</summary>
    public string Kind { get; set; } = KindOut;

    /// <summary>Путь файла результата (вид <c>file</c>): относительный — от рабочего каталога.</summary>
    public string Path { get; set; } = "";

    /// <summary>Каталог поиска (вид <c>newest</c>): относительный — от рабочего каталога;
    /// пусто — сам рабочий каталог.</summary>
    public string Dir { get; set; } = "";

    /// <summary>Маска имени (вид <c>newest</c>): <c>*.safetensors</c>; пусто — любой файл.</summary>
    public string Pattern { get; set; } = "*";

    /// <summary>Искать во вложенных каталогах (вид <c>newest</c>).</summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// НАЙТИ ФАЙЛ РЕЗУЛЬТАТА после удачного завершения программы; null — не нашли (тогда
    /// вызывающий берёт <paramref name="outPath"/> и решает сам, есть ли что отдавать).
    /// Значения настроек подставляются в путь и в маску: у тренера каталог эпох зависит
    /// от настройки записи плагина.
    /// </summary>
    /// <param name="workDir">Рабочий каталог запуска (корень относительных путей).</param>
    /// <param name="outPath">Файл, куда велели писать (<c>{out}</c>).</param>
    /// <param name="values">Значения настроек записи плагина.</param>
    public string? Resolve(string workDir, string outPath,
        IReadOnlyDictionary<string, string>? values = null)
    {
        switch (Kind)
        {
            case KindFile:
                var named = Full(workDir, ConvertLine.Fill(Path, values));
                return named.Length > 0 && File.Exists(named) ? named : null;
            case KindNewest:
                var dir = Full(workDir, ConvertLine.Fill(Dir, values));
                if (dir.Length == 0 || !Directory.Exists(dir))
                {
                    return null;
                }
                var mask = ConvertLine.Fill(Pattern, values);
                try
                {
                    return new DirectoryInfo(dir)
                        .EnumerateFiles(mask.Length > 0 ? mask : "*",
                            Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault()?.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or ArgumentException)
                {
                    return null;   // каталог исчез или маска негодная — результата просто нет
                }
            default:
                return File.Exists(outPath) ? outPath : null;
        }
    }

    private static string Full(string workDir, string path)
    {
        path = path.Trim();
        if (path.Length == 0)
        {
            return workDir;
        }
        return System.IO.Path.IsPathRooted(path) || workDir.Length == 0
            ? path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(workDir, path));
    }

    internal static RunResult From(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Object)
        {
            return new RunResult();
        }
        var kind = JsonRead.Str(v, "kind", KindOut).ToLowerInvariant();
        return new RunResult
        {
            Kind = kind is KindFile or KindNewest ? kind : KindOut,
            Path = JsonRead.Str(v, "path"),
            Dir = JsonRead.Str(v, "dir"),
            Pattern = JsonRead.Str(v, "pattern", "*"),
            Recursive = JsonRead.Bool(v, "recursive", false),
        };
    }
}
