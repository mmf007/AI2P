using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// ЗАГРУЗКА ОБЪЕКТА ПРОЕКТА В МОДЕЛЬ (T-14-S1, версия 1.95) — решение о том, ЧТО именно
/// уедет в модель вместе с промптом, когда в описании задачи стоит ссылка <c>@obj:…</c>.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ШАГ. До T-14-S1 ссылка на объект давала модели только ТЕКСТ: паспорт
/// персонажа и пути его эталонных кадров (T-259). Для паспорта этого достаточно, а для
/// двух вещей — нет:
/// <list type="bullet">
/// <item>РЕФЕРЕНСНАЯ КАРТИНКА должна быть залита в движок и подставлена в граф — путь
/// в тексте промпта модель прочитать не может, файловых инструментов у неё нет;</item>
/// <item>АДАПТЕР LoRA должен быть подключён к самой модели — это уже не промпт, а часть
/// подключения (<see cref="LoraSettings"/>).</item>
/// </list>
/// Отсюда и правило задания: перед запуском смотрим, что за объекты названы в описании,
/// и сверяем их с тем, что модель исполнителя умеет принять.
///
/// ПОЧЕМУ ОШИБКА, А НЕ МОЛЧАНИЕ. Молча сгенерированный кадр без адаптера выглядит нормально
/// и уходит человеку как готовый результат — а персонаж на нём чужой. Поэтому несовпадение
/// это ОШИБКА задания, и в её тексте названы объект, чего ему не хватило и почему
/// (<see cref="ObjectLoadProblem"/>), а не общее «не удалось загрузить объект».
///
/// ЧТО СЧИТАЕТСЯ ТРЕБОВАНИЕМ ЗАГРУЗКИ. Решает ВИД названного объекта и его собственное
/// состояние — то есть намерение человека, а не догадка:
/// <list type="table">
/// <listheader><term>ссылка ведёт на</term><description>что делаем</description></listheader>
/// <item><term>объект вида «адаптер LoRA»</term><description>адаптер обязателен: модель
/// обязана его принимать, а сам он — быть обученным. Иначе ошибка</description></item>
/// <item><term>объект любого вида с ГОТОВЫМ адаптером</term><description>адаптер
/// подключается: человек его для этого и обучал. Модель не принимает — ошибка</description></item>
/// <item><term>объект с адаптером в работе (запланирован, обучается)</term>
/// <description>замечание в консоли: кадр делается по паспорту, адаптера ещё нет</description></item>
/// <item><term>объект вида «эталонный кадр»</term><description>картинка обязана уехать
/// в модель. Модель картинку не принимает или файла нет — ошибка</description></item>
/// <item><term>персонаж, локация, стиль, реквизит</term><description>как и было (T-259):
/// паспорт в промпт, эталонные кадры детей — кандидаты в стартовый кадр</description></item>
/// </list>
/// Последняя строка важна отдельно: ею НЕЛЬЗЯ ошибаться в сторону строгости, иначе любая
/// уже заведённая задача с персонажем перестала бы запускаться на t2v-модели.
///
/// КАК ГРУЗИТЬ — из справочника моделей: <see cref="LoraSettings.Apply"/> и
/// <see cref="RefImageSettings"/> профайла. Здесь только РЕШЕНИЕ, сама загрузка — в коннекторе.
/// </summary>
public sealed record ObjectLoadRef(
    string Code,
    string Name,
    string Kind,
    string ImagePath,
    string LoraPath,
    string LoraStatus,
    double? LoraStrength = null);

/// <summary>Коды несовпадений. Код, а не текст: по нему коннектор решает, что писать
/// в чат (какую замену предлагать), а текст переводится словарём.</summary>
public static class ObjectLoadProblems
{
    /// <summary>Объекту нужен адаптер, а модель с адаптерами не работает вовсе.</summary>
    public const string LoraNotSupported = "lora-not-supported";

    /// <summary>Объект — адаптер, но он ещё не обучен.</summary>
    public const string LoraNotTrained = "lora-not-trained";

    /// <summary>Состояние «готов», а путь файла не заполнен.</summary>
    public const string LoraNoPath = "lora-no-path";

    /// <summary>Путь заполнен, а файла по нему нет.</summary>
    public const string LoraFileMissing = "lora-file-missing";

    /// <summary>Файл адаптера не того формата, какой модель принимает.</summary>
    public const string LoraFormat = "lora-format";

    /// <summary>Адаптеров названо больше, чем модель берёт разом.</summary>
    public const string LoraTooMany = "lora-too-many";

    /// <summary>Назван эталонный кадр, а модель картинку на вход не принимает.</summary>
    public const string ImageNotSupported = "image-not-supported";

    /// <summary>У объекта-кадра не задан файл.</summary>
    public const string ImageNoPath = "image-no-path";

    /// <summary>Файла картинки нет в папке проекта.</summary>
    public const string ImageFileMissing = "image-file-missing";

    /// <summary>Картинка не того формата, какой модель принимает.</summary>
    public const string ImageFormat = "image-format";

    /// <summary>Кадров названо больше, чем модель берёт разом.</summary>
    public const string ImageTooMany = "image-too-many";

    public static readonly string[] All =
    [
        LoraNotSupported, LoraNotTrained, LoraNoPath, LoraFileMissing, LoraFormat, LoraTooMany,
        ImageNotSupported, ImageNoPath, ImageFileMissing, ImageFormat, ImageTooMany,
    ];

    /// <summary>Несовпадение из-за АДАПТЕРА (а не из-за картинки) — по этому признаку
    /// в чат подбирается замена: исполнители, чья модель адаптеры принимает.</summary>
    public static bool IsLora(string code) => code.StartsWith("lora-", StringComparison.Ordinal);
}

/// <summary>Одно несовпадение: код, объект, готовый текст для человека.</summary>
public sealed record ObjectLoadProblem(string Code, string ObjectCode, string ObjectName, string Text);

/// <summary>Адаптер, который надо подключить: путь файла и вес.</summary>
/// <param name="File">Абсолютный путь файла адаптера на этом компьютере.</param>
/// <param name="Strength">Вес адаптера: свой у объекта либо умолчание профайла.</param>
public sealed record ObjectLoadLora(string ObjectCode, string ObjectName, string File, double Strength);

/// <summary>Картинка, которую надо передать модели: путь относительно папки проекта.</summary>
public sealed record ObjectLoadImage(string ObjectCode, string ObjectName, string Path);

/// <summary>
/// Что уедет в модель и что этому помешало. Пустой список несовпадений — можно запускать;
/// непустой — задание встаёт с ошибкой, а её текст собирается из <see cref="Problems"/>.
/// </summary>
public sealed class ObjectLoadPlan
{
    public List<ObjectLoadLora> Loras { get; } = [];

    public List<ObjectLoadImage> Images { get; } = [];

    public List<ObjectLoadProblem> Problems { get; } = [];

    /// <summary>Замечания — то, о чём надо сказать, но из-за чего останавливаться не нужно
    /// (адаптер объекта ещё обучается; модель картинку не берёт, а объект её не требовал).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Всё сошлось — задание можно запускать.</summary>
    public bool Ok => Problems.Count == 0;

    /// <summary>Есть несовпадение из-за адаптера — значит в чат надо предлагать модели с LoRA.</summary>
    public bool LoraFailed => Problems.Any(p => ObjectLoadProblems.IsLora(p.Code));

    /// <summary>Есть несовпадение из-за картинки.</summary>
    public bool ImageFailed => Problems.Any(p => !ObjectLoadProblems.IsLora(p.Code));
}

/// <summary>
/// Решение о загрузке — чистая логика без файловой системы и без базы: проверки файлов
/// приходят делегатами. Так правило целиком проверяется тестами, а коннектору остаётся
/// исполнение.
/// </summary>
public static class ObjectLoadPlanner
{
    /// <summary>Расширения, которые считаем картинкой, если модель форматы не перечислила.</summary>
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <param name="refs">Объекты, названные в описании задачи, в порядке появления.</param>
    /// <param name="lora">Настройка работы с адаптером у модели исполнителя (профайл).</param>
    /// <param name="refImage">Настройка референсной картинки у модели исполнителя.</param>
    /// <param name="modelName">Как модель зовут в справочнике — для текста ошибки.</param>
    /// <param name="findLoraFile">Путь из объекта → абсолютный путь файла либо null.</param>
    /// <param name="imageExists">Путь из объекта (относительно папки проекта) → есть ли файл.</param>
    public static ObjectLoadPlan Plan(
        IReadOnlyList<ObjectLoadRef> refs,
        LoraSettings lora,
        RefImageSettings refImage,
        string modelName,
        Func<string, string?> findLoraFile,
        Func<string, bool> imageExists)
    {
        var plan = new ObjectLoadPlan();
        foreach (var item in refs)
        {
            PlanLora(plan, item, lora, modelName, findLoraFile);
            PlanImage(plan, item, refImage, modelName, imageExists);
        }
        // сколько адаптеров и картинок модель берёт РАЗОМ — тоже из справочника: лишние
        // не отбрасываем молча, иначе часть названных объектов исчезла бы без следа
        TrimByMaxCount(plan.Loras, Math.Max(1, lora.Apply.MaxCount), plan,
            ObjectLoadProblems.LoraTooMany, "msg.objectLoad.8", modelName);
        TrimByMaxCount(plan.Images, Math.Max(1, refImage.MaxCount), plan,
            ObjectLoadProblems.ImageTooMany, "msg.objectLoad.11", modelName);
        return plan;
    }

    /// <summary>Нужен ли объекту адаптер и можно ли его подключить.</summary>
    private static void PlanLora(ObjectLoadPlan plan, ObjectLoadRef item, LoraSettings lora,
        string modelName, Func<string, string?> findLoraFile)
    {
        var isAdapter = item.Kind.Equals(ObjectKinds.Lora, StringComparison.OrdinalIgnoreCase);
        // намерение подключить адаптер: либо объект САМ адаптер, либо у объекта заведён
        // адаптер (состояние не «нет» или вписан путь готового файла)
        var intended = isAdapter
                       || (item.LoraStatus.Length > 0 && item.LoraStatus != LoraStates.None)
                       || item.LoraPath.Length > 0;
        if (!intended)
        {
            return;
        }
        if (item.LoraStatus != LoraStates.Ready && item.LoraPath.Length == 0)
        {
            // адаптера ещё нет. У объекта-адаптера подключать нечего — это ошибка; у персонажа
            // остаётся паспорт, и останавливать работу из-за незаконченного обучения нельзя
            var state = StateText(item.LoraStatus);
            if (isAdapter)
            {
                Add(plan, ObjectLoadProblems.LoraNotTrained, item, "msg.objectLoad.2", modelName, state);
            }
            else
            {
                plan.Notes.Add(Loc.T("msg.objectLoad.12", item.Code, item.Name, modelName, state));
            }
            return;
        }
        if (!lora.Supported)
        {
            Add(plan, ObjectLoadProblems.LoraNotSupported, item, "msg.objectLoad.1", modelName,
                Loc.T("models.lora.reason." +
                      (Array.IndexOf(LoraReasons.All, lora.Reason) >= 0 ? lora.Reason : LoraReasons.Unknown)));
            return;
        }
        if (item.LoraPath.Length == 0)
        {
            Add(plan, ObjectLoadProblems.LoraNoPath, item, "msg.objectLoad.3");
            return;
        }
        if (lora.Apply.Formats.Count > 0 &&
            !lora.Apply.Formats.Any(f => item.LoraPath.EndsWith(f, StringComparison.OrdinalIgnoreCase)))
        {
            Add(plan, ObjectLoadProblems.LoraFormat, item, "msg.objectLoad.9",
                System.IO.Path.GetExtension(item.LoraPath), string.Join(", ", lora.Apply.Formats));
            return;
        }
        if (findLoraFile(item.LoraPath) is not { Length: > 0 } file)
        {
            Add(plan, ObjectLoadProblems.LoraFileMissing, item, "msg.objectLoad.4", item.LoraPath);
            return;
        }
        var strength = item.LoraStrength ?? lora.Apply.Strength;
        plan.Loras.Add(new ObjectLoadLora(item.Code, item.Name, file, strength));
    }

    /// <summary>Нужна ли объекту передача картинки и можно ли её передать.</summary>
    private static void PlanImage(ObjectLoadPlan plan, ObjectLoadRef item, RefImageSettings refImage,
        string modelName, Func<string, bool> imageExists)
    {
        // требование передать картинку даёт только ПРЯМАЯ ссылка на эталонный кадр: у
        // персонажа кадры детей это подсказка, а не обязательство (T-259/T-258)
        if (!item.Kind.Equals(ObjectKinds.Image, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!refImage.Supported)
        {
            Add(plan, ObjectLoadProblems.ImageNotSupported, item, "msg.objectLoad.5", modelName);
            return;
        }
        if (item.ImagePath.Length == 0)
        {
            Add(plan, ObjectLoadProblems.ImageNoPath, item, "msg.objectLoad.6");
            return;
        }
        // сравниваем ТИП, а не строку: модель перечисляет форматы MIME-типами (image/png),
        // а у объекта на руках только имя файла
        var mime = MimeOf(item.ImagePath);
        if (mime is null)
        {
            Add(plan, ObjectLoadProblems.ImageFormat, item, "msg.objectLoad.10",
                System.IO.Path.GetExtension(item.ImagePath),
                refImage.Formats.Count > 0 ? string.Join(", ", refImage.Formats) : string.Join(", ", ImageExtensions));
            return;
        }
        if (refImage.Formats.Count > 0 && !refImage.Formats.Any(f => MimeMatches(f, mime)))
        {
            Add(plan, ObjectLoadProblems.ImageFormat, item, "msg.objectLoad.10", mime,
                string.Join(", ", refImage.Formats));
            return;
        }
        if (!imageExists(item.ImagePath))
        {
            Add(plan, ObjectLoadProblems.ImageFileMissing, item, "msg.objectLoad.7", item.ImagePath);
            return;
        }
        plan.Images.Add(new ObjectLoadImage(item.Code, item.Name, item.ImagePath));
    }

    /// <summary>Лишние сверх максимума модели — в несовпадения, а не в тишину.</summary>
    private static void TrimByMaxCount<T>(List<T> loaded, int max, ObjectLoadPlan plan,
        string code, string key, string modelName)
    {
        while (loaded.Count > max)
        {
            var extra = loaded[^1];
            loaded.RemoveAt(loaded.Count - 1);
            var (objectCode, objectName) = extra switch
            {
                ObjectLoadLora l => (l.ObjectCode, l.ObjectName),
                ObjectLoadImage i => (i.ObjectCode, i.ObjectName),
                _ => ("", ""),
            };
            plan.Problems.Add(new ObjectLoadProblem(code, objectCode, objectName,
                Loc.T(key, objectCode, objectName, modelName, max)));
        }
    }

    private static void Add(ObjectLoadPlan plan, string code, ObjectLoadRef item, string key,
        params object[] args) =>
        plan.Problems.Add(new ObjectLoadProblem(code, item.Code, item.Name,
            Loc.T(key, new object[] { item.Code, item.Name }.Concat(args).ToArray())));

    /// <summary>
    /// Название состояния адаптера словами. Ключи СВОИ (<c>msg.objectLoad.state.*</c>), а не
    /// взятые у формы объекта: состояний два набора — у объекта (T-259) и у строки обучения
    /// под модель (T-12-S1), — а текст в ошибке нужен один и тот же.
    /// </summary>
    public static string StateText(string? state)
    {
        var code = state is { Length: > 0 } ? state.Trim().ToLowerInvariant() : LoraStates.None;
        return Array.IndexOf(States, code) >= 0
            ? Loc.T("msg.objectLoad.state." + code)
            : Loc.T("msg.objectLoad.state." + LoraStates.None);
    }

    /// <summary>Все состояния обоих наборов: объекта (T-259) и обучения под модель (T-12-S1).</summary>
    public static readonly string[] States = ["none", "planned", "training", "ready", "error"];

    /// <summary>MIME-тип картинки по имени файла; null — это вообще не картинка.</summary>
    public static string? MimeOf(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null,
        };

    /// <summary>Объявленный моделью формат подходит: точное совпадение либо маска «image/*».</summary>
    private static bool MimeMatches(string declared, string mime) =>
        declared.Equals(mime, StringComparison.OrdinalIgnoreCase) ||
        (declared.EndsWith("/*", StringComparison.Ordinal) &&
         mime.StartsWith(declared[..^1], StringComparison.OrdinalIgnoreCase));
}
