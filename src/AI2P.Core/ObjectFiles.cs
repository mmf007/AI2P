namespace AI2P.Core;

/// <summary>
/// ФАЙЛЫ ОБЪЕКТА: ПАПКА ПРОЕКТА ИЛИ ХРАНИЛИЩЕ ОРГАНИЗАЦИИ (T-98-S0).
///
/// У объекта путь файла (<c>PathOrUrl</c>) исторически один и тот же — ОТНОСИТЕЛЬНО ПАПКИ
/// ПРОЕКТА: человек сам показывает свою картинку в своей папке, и на соседнем сервере тот
/// же путь показывает его собственную копию. Для эталонного кадра, который человек выбрал
/// на диске, это верно и менять здесь нечего.
///
/// А вот КАДРЫ ДАТАСЕТА LoRA так лежать не должны. Их не выбирают — их делает сама
/// программа: браузер режет и пережимает картинку, сервер кладёт готовый файл. Папка
/// проекта не реплицируется (она у каждого сервера своя и живёт вне каталога данных),
/// поэтому на соседнем сервере такого кадра просто нет — датасет там пустой, а обучение
/// с него не запустить. Значит место кадра — в КАТАЛОГЕ ДАННЫХ ОРГАНИЗАЦИИ
/// (<c>data/orgs/ORG-N/projects/PRJ-N/objects/OBJ-владелец/OBJ-датасет/</c>): он реплицируется
/// целиком (<c>ReplScopes.OrgFiles</c>), и кадр уезжает партнёру сам, вместе со строкой
/// объекта.
///
/// ЧЕМ ОТЛИЧАЮТСЯ ЭТИ ДВА ПУТИ В ОДНОМ ПОЛЕ: приставкой <c>store:</c>. Разбирать путь по
/// его виду («начинается с projects/ — значит хранилище») нельзя: в папке проекта такой
/// подкаталог тоже бывает, и ошибка была бы молчаливой. Приставка говорит о себе сама —
/// ровно как <c>http://</c> в том же поле, — и её видно и в форме, и в журнале.
/// </summary>
public static class ObjectFiles
{
    /// <summary>Приставка пути в каталоге данных организации (реплицируемое хранилище).</summary>
    public const string StorePrefix = "store:";

    /// <summary>Лежит ли файл в хранилище организации, а не в папке проекта.</summary>
    public static bool IsStore(string? path) =>
        (path ?? "").TrimStart().StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Путь без приставки — такой, каким его понимает каталог данных
    /// (<c>api/files/raw</c>). Путь не из хранилища возвращается как есть.</summary>
    public static string Rel(string? path)
    {
        var value = (path ?? "").Trim();
        return IsStore(value) ? value[StorePrefix.Length..].TrimStart('/', '\\') : value;
    }

    /// <summary>Путь хранилища с приставкой — в таком виде он ложится в <c>PathOrUrl</c>.</summary>
    public static string Store(string rel) => StorePrefix + rel.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Каталог кадров датасета в хранилище: <c>projects/PRJ-N/objects/OBJ-владелец/OBJ-датасет</c>.
    /// У каждого датасета свой подкаталог — датасеты собирают под разные модели, и
    /// одноимённые кадры двух датасетов затирали бы друг друга.
    /// </summary>
    public static string DatasetDirRel(string projectSlug, string ownerDisplayId,
        string datasetDisplayId) =>
        $"projects/{projectSlug}/objects/{ownerDisplayId}/{datasetDisplayId}";

    /// <summary>
    /// КАТАЛОГ ОБУЧЕННЫХ АДАПТЕРОВ LoRA В ХРАНИЛИЩЕ ОРГАНИЗАЦИИ (T-102-S0).
    ///
    /// Сам адаптер обучение кладёт в РЕПОЗИТОРИЙ МОДЕЛЕЙ (<c>&lt;репозиторий&gt;/loras</c>) —
    /// туда за ним ходит движок модели, — но репозиторий это железо ЭТОГО компьютера, он не
    /// реплицируется, и на соседнем сервере обученного файла нет. Поэтому копия готового
    /// файла ложится ещё и сюда, в каталог данных организации: он уезжает партнёру целиком
    /// (<c>ReplScopes.OrgFiles</c>), и «обучили на одном сервере — используем на другом»
    /// работает без единой строки кода передачи.
    ///
    /// Каталог ПЛОСКИЙ, имена файлов приходят из настройки <c>train.result.target</c> и по
    /// умолчанию равны номеру объекта (<c>OBJ-7.safetensors</c>), а номер уникален в
    /// организации — этого достаточно, чтобы адаптеры не затирали друг друга.
    /// </summary>
    public const string LoraStoreDir = "objects/loras";

    /// <summary>Путь копии адаптера в хранилище: <c>objects/loras/&lt;имя файла&gt;</c>.</summary>
    public static string LoraStoreRel(string fileName) =>
        LoraStoreDir + "/" + Path.GetFileName(fileName.Replace('\\', '/'));

    /// <summary>
    /// Ссылка на файл объекта: хранилище отдаёт <c>api/files/raw</c>, папку проекта —
    /// <c>api/files/project</c>. null — показывать нечего (пусто) либо это внешний адрес,
    /// который надо брать как есть.
    /// </summary>
    public static FileLink? Link(string? path, string? projectId)
    {
        var value = (path ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }
        if (IsStore(value))
        {
            return new FileLink(FileLinks.Raw, "", Rel(value));
        }
        return projectId is { Length: > 0 } project
            ? new FileLink(FileLinks.Project, project, value)
            : null;
    }

    /// <summary>
    /// Путь файла объекта НА ЭТОМ КОМПЬЮТЕРЕ: у хранилища считается от каталога данных
    /// организации, у остальных — от папки проекта. null — считать не от чего (каталог не
    /// задан) либо путь уводит за край корня.
    /// </summary>
    public static string? Resolve(string? path, string? projectFolder, string? dataDir)
    {
        var value = (path ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }
        if (!IsStore(value))
        {
            return ProjectFiles.Resolve(projectFolder, value);
        }
        var rel = Rel(value);
        if (dataDir is not { Length: > 0 } || !ProjectFiles.IsRelative(rel))
        {
            return null;
        }
        var root = Path.GetFullPath(dataDir);
        string abs;
        try
        {
            abs = Path.GetFullPath(Path.Combine(root, rel));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        return abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? abs
            : null;
    }
}
