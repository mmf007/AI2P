using AI2P.Core;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Connectors;

/// <summary>
/// Секреты (ТЗ гл. 10). Хранилище ДВУХСОСТАВНОЕ (T-228):
/// <list type="bullet">
/// <item><b>подкаталог <c>secrets/</c></b> рядом с config.json — ключи МОДЕЛЕЙ и ИСТОЧНИКОВ
/// ИМПОРТА, по ОДНОМУ json на ссылку (<c>secrets/anthropic.apikey.json</c>). Запись одного
/// ключа не перечитывает и не переписывает чужие;</item>
/// <item><b>файл <c>secrets.json</c></b> рядом с config.json — то, что не про модели и не
/// реплицируется никогда: ключи организаций (<c>orgKeys.*</c>, <see cref="OrgSecretKey"/>)
/// и учётка администратора сервера (<c>server.*</c>). Плюс СОВМЕСТИМОСТЬ: старые файлы с
/// ключами моделей продолжают читаться, а при старте их значения переезжают в подкаталог
/// (<see cref="MigrateFileToDir"/>).</item>
/// </list>
///
/// Профайл модели ссылается на ключ строкой secretRef вида "anthropic.apiKey" (п. 2.9);
/// файл заводится на КАЖДУЮ ССЫЛКУ, а не на каждую запись справочника — несколько моделей
/// одного провайдера ссылаются на один ключ и продолжают делить один файл.
///
/// Порядок поиска: подкаталог → secrets.json (вложенный объект или плоский ключ) →
/// переменная окружения ("anthropic.apiKey" → ANTHROPIC_API_KEY; окружением ключи задают
/// в контейнере нарочно). Значения секретов не пишутся в логи и журнал (п. 6.3) — только
/// ссылка и путь файла.
///
/// РЕГИСТР ССЫЛКИ. Имя файла приводится к нижнему регистру, поэтому ссылки, различающиеся
/// только регистром, — это ОДИН ключ на любой системе: иначе на Windows (файловая система
/// регистронезависима) и на Linux получалось бы разное поведение из одного и того же
/// secrets.json.
/// </summary>
public sealed class SecretStore
{
    private static readonly ILogger Logger = Log.ForContext<SecretStore>();

    /// <summary>Имя подкаталога с ключами — рядом с config.json и secrets.json.</summary>
    public const string DirName = "secrets";

    /// <summary>Пояснение в подкаталоге ключей: что это за каталог и куда класть ключи (T-234).
    /// Не .json — ключом не считается ни при каком имени ссылки.</summary>
    public const string ReadmeName = "readme.txt";

    /// <summary>Раздел учётки администратора сервера: остаётся в secrets.json (см. Ai2pAuth).</summary>
    public const string ServerSection = "server";

    /// <summary>Имена, недопустимые в Windows даже с расширением (nul.json — то же устройство).</summary>
    private static readonly string[] ReservedNames =
    [
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    ];

    private readonly string _path;
    private readonly string _dir;

    /// <param name="path">Полный путь к secrets.json (рядом с config.json).</param>
    /// <param name="dir">Подкаталог ключей; не задан — <c>secrets</c> рядом с secrets.json.</param>
    public SecretStore(string path, string? dir = null)
    {
        _path = path;
        _dir = dir ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".", DirName);
    }

    /// <summary>Путь к secrets.json.</summary>
    public string Path => _path;

    /// <summary>Путь к подкаталогу с ключами (по одному json на ссылку).</summary>
    public string Dir => _dir;

    /// <summary>Найти секрет по ссылке из профайла; null — не найден.</summary>
    public string? Resolve(string secretRef) => ResolveWithSource(secretRef).Value;

    /// <summary>
    /// Чтение секрета (ТЗ v1.42, todo36_5) — единственная точка чтения ключей вместе с
    /// <see cref="Write"/>: когда ключи переедут в другое хранилище, правится только
    /// эта пара методов.
    /// </summary>
    public string? Read(string secretRef) => Resolve(secretRef);

    /// <summary>
    /// Значение ИЗ ХРАНИЛИЩА (подкаталог либо secrets.json), без переменных окружения —
    /// им пользуется перенос ключей в организацию (<c>ModelKeyService.MigrateRefsToOrg</c>):
    /// заданное окружением переносить нельзя, его задают снаружи нарочно.
    /// </summary>
    public string? ReadStored(string secretRef)
    {
        secretRef = secretRef.Trim();
        return secretRef.Length == 0 ? null : FromDir(secretRef) ?? FromFile(secretRef);
    }

    /// <summary>Ключ по ссылке есть (и не пустой) — облачную модель можно активировать (ТЗ v1.42).</summary>
    public bool Has(string secretRef) =>
        secretRef.Trim().Length > 0 && !string.IsNullOrWhiteSpace(Resolve(secretRef));

    /// <summary>
    /// Ссылка принадлежит САМОМУ СЕРВЕРУ, а не моделям: ключи организаций (<c>orgKeys.*</c>)
    /// и учётка администратора сервера (<c>server.*</c>). Такие значения живут в secrets.json
    /// и не переезжают в подкаталог — они не про модели и не реплицируются никогда.
    /// </summary>
    public static bool IsServerSecret(string secretRef)
    {
        var reference = secretRef.Trim();
        return reference.StartsWith(OrgSecretKey.Section + ".", StringComparison.OrdinalIgnoreCase)
               || reference.StartsWith(ServerSection + ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Имя файла ключа по ссылке. В ссылке может быть что угодно, поэтому: нижний регистр,
    /// всё, кроме <c>a-z 0-9 . - _</c>, заменяется на <c>_</c>, длина ограничена. Если при
    /// этом что-то изменилось (были недопустимые символы или обрезалась длина), к имени
    /// добавляется хвост из хэша ИСХОДНОЙ ссылки — иначе две разные ссылки
    /// («a/b» и «a_b») получили бы один файл.
    /// </summary>
    public static string FileNameOf(string secretRef)
    {
        var lowered = secretRef.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            sb.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_'
                ? ch
                : '_');
        }
        // Windows не хранит имена, оканчивающиеся точкой или пробелом
        var safe = sb.ToString().Trim('.');
        var changed = !string.Equals(safe, lowered, StringComparison.Ordinal);
        if (safe.Length > 80)
        {
            safe = safe[..80];
            changed = true;
        }
        if (safe.Length == 0)
        {
            safe = "secret";
            changed = true;
        }
        if (ReservedNames.Contains(safe.Split('.')[0], StringComparer.Ordinal))
        {
            safe = "_" + safe;
        }
        return changed ? $"{safe}~{ShortHash(lowered)}.json" : safe + ".json";
    }

    /// <summary>Полный путь к файлу ключа по ссылке (файла может ещё не быть).</summary>
    public string PathOf(string secretRef) =>
        System.IO.Path.Combine(_dir, FileNameOf(secretRef));

    /// <summary>
    /// Где ключ по этой ссылке ЛЕЖИТ ИЛИ БУДЕТ ЛЕЖАТЬ — для сообщений человеку («добавьте
    /// ключ туда-то»): ключи моделей и импорта — свой файл в подкаталоге, ключи сервера
    /// и организаций — secrets.json.
    /// </summary>
    public string LocationOf(string secretRef) =>
        IsServerSecret(secretRef) ? _path : PathOf(secretRef);

    /// <summary>
    /// Записать секрет по ссылке. Ключ модели или источника импорта кладётся в СВОЙ файл
    /// подкаталога (чужие файлы при этом не читаются и не переписываются), ключ организации
    /// и учётка администратора сервера — в secrets.json, как раньше. Значение в логи
    /// не пишется (п. 6.3).
    /// </summary>
    public void Write(string secretRef, string value)
    {
        secretRef = secretRef.Trim();
        if (secretRef.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.secret.1"));
        }
        if (IsServerSecret(secretRef))
        {
            WriteToFile(secretRef, value);
            return;
        }
        WriteToDir(secretRef, value);
    }

    /// <summary>
    /// ПЕРЕНОС старого хранилища в подкаталог (T-228), делается при старте. Значения ключей
    /// моделей и источников импорта из secrets.json раскладываются по одному файлу на ссылку,
    /// а из secrets.json убираются; ключи организаций и учётка администратора сервера
    /// остаются в нём нетронутыми.
    ///
    /// Идемпотентно: ссылка, у которой файл в подкаталоге уже есть, не переносится и из
    /// secrets.json НЕ убирается — файл подкаталога и так старше по приоритету, а терять
    /// значение, вписанное человеком руками, нельзя. Возвращает число перенесённых ключей.
    /// </summary>
    public int MigrateFileToDir()
    {
        if (!File.Exists(_path))
        {
            return 0;
        }
        JsonObject root;
        try
        {
            root = ReadRoot();
        }
        catch (InvalidOperationException ex)
        {
            // повреждённый файл не повод не стартовать: ключи из него просто не переедут
            Logger.Warning("Перенос ключей из файла секретов пропущен: {Reason}", ex.Message);
            return 0;
        }

        var moved = new List<string>();
        foreach (var (secretRef, value) in Flatten(root))
        {
            // «_comment» и прочие строки, начинающиеся с подчёркивания, — пояснения человека
            // в файле (так же помечены комментарии в config.json), а не ключи
            if (IsServerSecret(secretRef) || secretRef.StartsWith('_') || value.Trim().Length == 0)
            {
                continue;
            }
            var target = PathOf(secretRef);
            if (File.Exists(target))
            {
                continue;   // уже перенесён (или задан здесь) — файл подкаталога главнее
            }
            WriteToDir(secretRef, value);
            moved.Add(secretRef);
            Logger.Information("Ключ {SecretRef} перенесён из файла секретов в {File}",
                secretRef, target);
        }
        if (moved.Count == 0)
        {
            return 0;
        }
        foreach (var secretRef in moved)
        {
            Remove(root, secretRef);
        }
        SaveRoot(root);
        Logger.Information("Ключи разложены по отдельным файлам ({Count}) в {Dir}",
            moved.Count, _dir);
        return moved.Count;
    }

    /// <summary>
    /// ЗАВЕСТИ подкаталог ключей при старте (T-234), даже если ни одного ключа в файлах нет.
    ///
    /// До этого каталог появлялся только в момент первой записи файлового ключа, а ключ,
    /// введённый в форме, принадлежит организации и файла не создаёт (см. <c>ModelKeyService</c>) —
    /// поэтому на обычной установке каталога не было вовсе, и человеку было негде увидеть,
    /// куда класть ключи руками. Внутрь кладётся пояснение <see cref="ReadmeName"/>: что это
    /// за каталог, какого вида файл и в каком порядке ключ ищется.
    ///
    /// Идемпотентно: существующий каталог не трогается, пояснение не перезаписывается (человек
    /// мог дописать в него своё). Возвращает true, если каталога до этого не было.
    /// </summary>
    public bool EnsureDir()
    {
        var created = !Directory.Exists(_dir);
        try
        {
            Directory.CreateDirectory(_dir);
            ProtectDir(_dir);
            var readme = System.IO.Path.Combine(_dir, ReadmeName);
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme, Loc.T("msg.secret.5", _path, DirName),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // каталог только для удобства человека: не завёлся (диск только для чтения,
            // нет прав) — старт продолжается, ключи всё так же читаются из organization/файла
            Logger.Warning("Каталог ключей {Dir} не заведён: {Reason}", _dir, ex.Message);
            return false;
        }
        if (created)
        {
            Logger.Information("Заведён каталог ключей {Dir}", _dir);
        }
        return created;
    }

    /// <summary>
    /// Обновить УЖЕ СУЩЕСТВУЮЩИЙ файл ключа и ничего не создавать (T-234). Ключ, введённый
    /// в форме, живёт в БД организации и файла не заводит — но если файл по этой ссылке
    /// на диске есть, он обязан ехать следом: иначе в нём молча остаётся прежнее значение,
    /// и на сервере, которому ключ организации ещё не выдан, работает старый ключ.
    /// Возвращает true, если файл был и обновлён.
    /// </summary>
    public bool UpdateIfPresent(string secretRef, string value)
    {
        secretRef = secretRef.Trim();
        if (secretRef.Length == 0 || IsServerSecret(secretRef) || !File.Exists(PathOf(secretRef)))
        {
            return false;
        }
        WriteToDir(secretRef, value);
        return true;
    }

    /// <summary>
    /// Найти секрет и сообщить источник (для диагностики в логах, ТЗ v1.14):
    /// "secrets/…json (путь)" / "secrets.json (путь)" / "env:ИМЯ" / "не найден".
    /// Само значение в логи не пишется (п. 6.3).
    /// </summary>
    public (string? Value, string Source) ResolveWithSource(string secretRef)
    {
        secretRef = secretRef.Trim();
        if (secretRef.Length == 0)
        {
            return (null, Loc.T("msg.secret.3"));
        }
        var fromDir = FromDir(secretRef);
        if (fromDir is not null)
        {
            return (fromDir, $"{DirName}/{FileNameOf(secretRef)} ({PathOf(secretRef)})");
        }
        var fromFile = FromFile(secretRef);
        if (fromFile is not null)
        {
            return (fromFile, $"secrets.json ({_path})");
        }
        var envName = EnvNameOf(secretRef);
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        return fromEnv is not null
            ? (fromEnv, $"env:{envName}")
            : (null, Loc.T("msg.secret.4", LocationOf(secretRef), envName));
    }

    /// <summary>
    /// Имя переменной окружения по ссылке: сегменты через точку, camelCase → SNAKE_CASE.
    /// "anthropic.apiKey" → ANTHROPIC_API_KEY, "deepseek.apiKey" → DEEPSEEK_API_KEY.
    /// </summary>
    public static string EnvNameOf(string secretRef)
    {
        var sb = new StringBuilder();
        foreach (var ch in secretRef)
        {
            if (ch == '.')
            {
                sb.Append('_');
            }
            else if (char.IsUpper(ch))
            {
                sb.Append('_').Append(ch);
            }
            else
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }
        return sb.ToString();
    }

    // --- подкаталог: один json на ссылку -------------------------------------------------

    /// <summary>Чтение ключа из своего файла; файла нет или он повреждён — null.</summary>
    private string? FromDir(string secretRef)
    {
        var path = PathOf(secretRef);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            // свой формат — { "ref": …, "value": … }; голая строка тоже принимается:
            // файл ключа правят и руками
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("value", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Записать ключ в свой файл подкаталога; чужие файлы не читаются и не трогаются.</summary>
    private void WriteToDir(string secretRef, string value)
    {
        Directory.CreateDirectory(_dir);
        ProtectDir(_dir);
        var path = PathOf(secretRef);
        var node = new JsonObject
        {
            // исходная ссылка сохраняется рядом со значением: имя файла приведено к нижнему
            // регистру и очищено от недопустимых символов, и по нему ссылку не восстановить
            ["ref"] = secretRef,
            ["value"] = value,
        };
        File.WriteAllText(path, node.ToJsonString(JsonOut),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ProtectFile(path);
    }

    // --- secrets.json: ключи организаций и учётка администратора сервера ------------------

    private void WriteToFile(string secretRef, string value)
    {
        var root = ReadRoot();
        var segments = secretRef.Split('.');
        // ключ уже лежит вложенным объектом — обновляем там же, иначе плоский ключ
        if (segments.Length > 1 && root[segments[0]] is JsonObject)
        {
            var node = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (node[segments[i]] is not JsonObject child)
                {
                    node[segments[i]] = child = new JsonObject();
                }
                node = child;
            }
            node[segments[^1]] = value;
        }
        else
        {
            root[secretRef] = value;
        }
        SaveRoot(root);
    }

    /// <summary>Содержимое secrets.json как объект; файла нет — пустой объект, повреждён — ошибка.</summary>
    private JsonObject ReadRoot()
    {
        if (!File.Exists(_path))
        {
            return new JsonObject();
        }
        try
        {
            return JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            // повреждённый файл секретов не затираем молча — иначе можно потерять чужие ключи
            throw new InvalidOperationException(
                Loc.T("msg.secret.2", _path));
        }
    }

    private void SaveRoot(JsonObject root)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        // файл секретов правят и руками — пишем читаемо, без \uXXXX-эскейпов
        File.WriteAllText(_path, root.ToJsonString(JsonOut),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ProtectFile(_path);
    }

    /// <summary>Чтение из secrets.json: вложенный путь ("anthropic" → "apiKey") либо плоский ключ.</summary>
    private string? FromFile(string secretRef)
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            var root = doc.RootElement;

            // плоский ключ: { "anthropic.apiKey": "sk-…" }
            if (root.TryGetProperty(secretRef, out var flat) && flat.ValueKind == JsonValueKind.String)
            {
                return flat.GetString();
            }

            // вложенный путь: { "anthropic": { "apiKey": "sk-…" } }
            var current = root;
            foreach (var segment in secretRef.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Все ссылки со значениями-строками: и плоские ключи, и вложенные объекты.</summary>
    private static IEnumerable<(string Ref, string Value)> Flatten(JsonObject root)
    {
        foreach (var (name, node) in root.ToList())
        {
            switch (node)
            {
                case JsonValue when node.GetValueKind() == JsonValueKind.String:
                    yield return (name, node.GetValue<string>());
                    break;
                case JsonObject child:
                    foreach (var (inner, value) in Flatten(child))
                    {
                        yield return ($"{name}.{inner}", value);
                    }
                    break;
            }
        }
    }

    /// <summary>Убрать ссылку из secrets.json: плоский ключ либо вложенный путь (опустевшие
    /// объекты по дороге убираются вместе с ним).</summary>
    private static void Remove(JsonObject root, string secretRef)
    {
        if (root.ContainsKey(secretRef))
        {
            root.Remove(secretRef);
            return;
        }
        var segments = secretRef.Split('.');
        var chain = new List<JsonObject> { root };
        var node = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node[segments[i]] is not JsonObject child)
            {
                return;
            }
            chain.Add(child);
            node = child;
        }
        node.Remove(segments[^1]);
        for (var i = chain.Count - 1; i > 0; i--)
        {
            if (chain[i].Count == 0)
            {
                chain[i - 1].Remove(segments[i - 1]);
            }
        }
    }

    // --- права -----------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Права на файл секретов: ТОЛЬКО владелец, чтение и запись (0600) — T-135. Применяется
    /// и к secrets.json, и к КАЖДОМУ файлу подкаталога (T-228).
    ///
    /// На Linux/macOS сервер работает от обычного пользователя, и файл, созданный с обычной
    /// umask (0644), читал бы любой другой пользователь того же компьютера. А здесь лежат
    /// пароль администратора сервера, КЛЮЧИ ОРГАНИЗАЦИЙ (<see cref="OrgSecretKey"/>) и ключи
    /// API моделей: на Windows их дополнительно закрывает DPAPI, на остальных системах
    /// защита — ровно эти права. Windows пропускается: там права файла задаёт ACL.
    /// </summary>
    private static void ProtectFile(string path) =>
        SetMode(new FileInfo(path), UnixFileMode.UserRead | UnixFileMode.UserWrite);

    /// <summary>Права на подкаталог ключей: 0700 — войти в него может только владелец.</summary>
    private static void ProtectDir(string dir) =>
        SetMode(new DirectoryInfo(dir),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    private static void SetMode(FileSystemInfo target, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            target.UnixFileMode = mode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or PlatformNotSupportedException)
        {
            // файловая система без прав POSIX (сетевой диск, контейнер) — записать удалось,
            // и ронять запись ключа из-за прав нельзя
        }
    }

    /// <summary>Короткий хвост из хэша ссылки — им расходятся имена файлов у ссылок,
    /// которые после очистки символов совпали бы.</summary>
    private static string ShortHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
}
