using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Storage;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Ключи API облачных моделей (ТЗ v1.42, todo36_5; хранилище переработано в v1.52, этап 45).
///
/// Правило прежнее: облачной модели с подключением по API (не CLI) нужен ключ — без него
/// модель не может быть активной; как только ключ установлен, модель активируется.
///
/// А вот ХРАНИЛИЩЕ сменилось. Ключ теперь принадлежит ОРГАНИЗАЦИИ и лежит в её БД
/// (<see cref="ModelKeyStore"/>) в ЗАШИФРОВАННОМ виде — ключом организации, который живёт
/// только в secrets.json каждого сервера и не реплицируется. Поэтому ключ доезжает до всех
/// серверов организации сам, обычным журналом изменений, а прочитать его может лишь тот,
/// кому ключ организации выдан (ТЗ гл. 10, разд. 8 плана).
///
/// Порядок чтения: БД организации → файлы секретов → переменная окружения. Два последних
/// оставлены нарочно: ими пользуются существующие установки и запуск в контейнере, где
/// ключи задают окружением. Значения из файлов переносятся в организацию при старте
/// (<see cref="MigrateSecretsToOrg"/>) — «все введённые ключи это ключи организации».
/// Файлы с T-228 — это подкаталог <c>secrets/</c> (по одному json на ссылку) и старый
/// <c>secrets.json</c> за ним; чем именно ведает <see cref="SecretStore"/>.
/// </summary>
public sealed class ModelKeyService
{
    private static readonly ILogger Logger = Log.ForContext<ModelKeyService>();

    private readonly AiModelService _models;
    private readonly FileStore _files;
    private readonly SecretStore _secrets;
    private readonly ModelKeyStore _keys;
    private readonly OrgSecretKey _orgKeys;
    private readonly string _orgId;

    public ModelKeyService(AiModelService models, FileStore files, SecretStore secrets,
        ModelKeyStore keys, OrgSecretKey orgKeys, string orgId)
    {
        _models = models;
        _files = files;
        _secrets = secrets;
        _keys = keys;
        _orgKeys = orgKeys;
        _orgId = orgId;
    }

    /// <summary>Ключ организации известен этому серверу; без него зашифрованные значения
    /// прочитать нечем — сервер ещё не получил его от дирижёра (ТЗ гл. 10).</summary>
    public bool HasOrgKey => _orgKeys.Has(_orgId);

    /// <summary>
    /// Значение ключа по ссылке из профайла модели: организация → secrets.json → окружение.
    /// null — ключа нет либо его нечем расшифровать. Это ЕДИНСТВЕННАЯ точка чтения ключа
    /// вместе с <see cref="SetKey"/>: коннекторы ходят сюда, а не в secrets.json.
    /// </summary>
    public string? Resolve(string secretRef) => ResolveWithSource(secretRef).Value;

    /// <summary>Значение и источник — для диагностики (само значение в логи не пишется).</summary>
    public (string? Value, string Source) ResolveWithSource(string secretRef)
    {
        secretRef = secretRef.Trim();
        if (secretRef.Length == 0)
        {
            return (null, Loc.T("msg.secret.3"));
        }
        var encrypted = _keys.Encrypted(secretRef);
        if (encrypted.Length > 0)
        {
            var orgKey = _orgKeys.Read(_orgId);
            if (orgKey is null)
            {
                // ключ организации этому серверу ещё не выдан — расшифровать нечем
                return (null, Loc.T("msg.modelKey.2"));
            }
            var value = OrgSecretKey.Decrypt(orgKey, encrypted);
            return value is not null
                ? (value, Loc.T("msg.modelKey.3"))
                : (null, Loc.T("msg.modelKey.4"));
        }
        return _secrets.ResolveWithSource(secretRef);
    }

    /// <summary>Ключ по ссылке есть — облачную модель можно активировать (ТЗ v1.42).</summary>
    public bool Has(string secretRef) => !string.IsNullOrWhiteSpace(Resolve(secretRef));

    /// <summary>
    /// Перенести ключи из secrets.json в организацию (ТЗ гл. 10, этап 45): «все введённые
    /// ключи — ключи организации». Переносятся только значения ИЗ ФАЙЛА и только если ключа
    /// в организации ещё нет; переменные окружения не трогаются — их задают снаружи нарочно.
    /// Ключа организации нет (сервер только подключился) — перенос откладывается.
    /// </summary>
    public int MigrateSecretsToOrg(string? actorId = null) =>
        MigrateRefsToOrg(SecretRefs(), actorId);

    /// <summary>
    /// То же для ПРОИЗВОЛЬНОГО набора ссылок (ТЗ v1.65, T-123): ключи есть не только у моделей —
    /// ключ и токен источника импорта переезжают в организацию тем же порядком.
    /// </summary>
    public int MigrateRefsToOrg(IEnumerable<string> secretRefs, string? actorId = null)
    {
        var orgKey = _orgKeys.Read(_orgId);
        if (orgKey is null)
        {
            return 0;
        }
        var moved = 0;
        foreach (var secretRef in secretRefs)
        {
            if (_keys.Encrypted(secretRef).Length > 0)
            {
                continue;
            }
            // только значения ИЗ ХРАНИЛИЩА (подкаталог secrets/ или secrets.json): заданное
            // переменной окружения не переносится — его задают снаружи нарочно
            var value = _secrets.ReadStored(secretRef);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            _keys.Set(secretRef, OrgSecretKey.Encrypt(orgKey, value.Trim()), actorId);
            moved++;
            Logger.Information("Ключ API {SecretRef} перенесён в организацию (значение зашифровано)",
                secretRef);
        }
        return moved;
    }

    /// <summary>Все ссылки на ключи, встречающиеся в профайлах моделей организации.</summary>
    public List<string> SecretRefs()
    {
        var refs = new List<string>();
        foreach (var model in _models.List())
        {
            var (secretRef, _) = SecretRefOf(ReadProfile(model.ProfilePath));
            if (secretRef.Length > 0 && !refs.Contains(secretRef, StringComparer.Ordinal))
            {
                refs.Add(secretRef);
            }
        }
        return refs;
    }

    /// <summary>Название модели по ссылке на ключ — для списка конфликтов (todo45).</summary>
    public string ModelNameOf(string secretRef)
    {
        foreach (var model in _models.List())
        {
            if (SecretRefOf(ReadProfile(model.ProfilePath)).SecretRef == secretRef)
            {
                return model.Name;
            }
        }
        return "";
    }

    /// <summary>
    /// Состояние ключа модели: нужен ли, есть ли, ссылка на него в секретах. Само значение
    /// ключа наружу не отдаётся — при обновлении старое значение не показывается.
    /// </summary>
    public ModelKeyStatusDto Status(string modelId)
    {
        var status = new ModelKeyStatusDto { ModelId = modelId, HasOrgKey = HasOrgKey };
        var model = _models.Get(modelId);
        if (model is null)
        {
            return status;
        }
        var profileJson = ReadProfile(model.ProfilePath);
        var (secretRef, isCli) = SecretRefOf(profileJson);
        status.SecretRef = secretRef;
        // ключ нужен: облачная модель, подключение по API, в профайле указана ссылка на ключ
        status.Required = !model.IsLocal && !isCli && secretRef.Length > 0;
        if (secretRef.Length == 0)
        {
            return status;
        }
        // путь файла ключа на ЭТОМ компьютере (T-234): показывается и когда ключа нет — это
        // ответ на вопрос «куда положить ключ руками»; самого файла по пути может не быть
        status.KeyFile = _secrets.LocationOf(secretRef);
        var (value, source) = ResolveWithSource(secretRef);
        status.HasKey = !string.IsNullOrWhiteSpace(value);
        status.Source = source;
        return status;
    }

    /// <summary>
    /// Установить/обновить ключ API модели и активировать её (ТЗ v1.42): после появления
    /// ключа модель переводится в активные — дальше пользователь волен выключить её руками.
    /// </summary>
    public ModelKeyStatusDto SetKey(string modelId, string value, string? actorId)
    {
        var model = _models.Get(modelId)
            ?? throw new ArgumentException(Loc.T("msg.modelKey.5", modelId));
        var (secretRef, _) = SecretRefOf(ReadProfile(model.ProfilePath));
        if (secretRef.Length == 0)
        {
            throw new ArgumentException(
                Loc.T("msg.modelKey.6"));
        }
        if (value.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.modelKey.7"));
        }

        SetByRef(secretRef, value.Trim(), actorId);
        Logger.Information("Ключ API модели {Name} обновлён (ссылка {SecretRef})", model.Name, secretRef);

        // Ключ принадлежит ОРГАНИЗАЦИИ и ставится с любого её сервера, а сама запись модели —
        // дирижёру (ТЗ гл. 6, этап 42). Поэтому активацию пробуем, но не роняем на ней
        // установку ключа: ключ сохранён, а активность подхватит владелец записи
        if (!model.IsActive)
        {
            model.IsActive = true;
            if (TrySetActive(model, actorId))
            {
                Logger.Information("Модель {Name} активирована: ключ API установлен", model.Name);
            }
        }
        return Status(modelId);
    }

    /// <summary>
    /// Записать значение ключа по ссылке — в ОРГАНИЗАЦИЮ, зашифрованным. Ключа организации
    /// нет (сервер ещё не получил его от дирижёра) — понятная ошибка вместо записи в никуда.
    /// </summary>
    public void SetByRef(string secretRef, string value, string? actorId)
    {
        var orgKey = _orgKeys.Read(_orgId)
            ?? throw new InvalidOperationException(
                Loc.T("msg.modelKey.8"));
        _keys.Set(secretRef, OrgSecretKey.Encrypt(orgKey, value), actorId);
        // файл ключа НОВЫМ значением не заводится (ключ принадлежит организации и реплицируется),
        // но если он на этом компьютере уже есть — едет следом (T-234): иначе в нём молча
        // остаётся прежнее значение и им работает сервер, которому ключ организации не выдан
        if (_secrets.UpdateIfPresent(secretRef, value))
        {
            Logger.Information("Файл ключа {SecretRef} обновлён вслед за организацией", secretRef);
        }
    }

    /// <summary>Записать УЖЕ зашифрованное значение (разрешение конфликта ключа, todo45):
    /// blob зашифрован тем же ключом организации, перешифровывать его незачем.</summary>
    public void SetEncrypted(string secretRef, string encrypted, string? actorId) =>
        _keys.Set(secretRef, encrypted, actorId);

    /// <summary>
    /// Текст ошибки активации для AiModelService.ActivationGuard: облачная модель без ключа
    /// API активной быть не может; null — можно активировать.
    /// </summary>
    public string? ActivationError(string modelId)
    {
        var status = Status(modelId);
        return status is { Required: true, HasKey: false }
            ? Loc.T("msg.modelKey.9", status.SecretRef)
            : null;
    }

    /// <summary>
    /// Проверка при старте приложения: активная облачная модель без ключа деактивируется
    /// (работать она всё равно не может). Обратно модели не включаются — выбор пользователя
    /// уважается, активация только в момент установки ключа.
    /// </summary>
    public void EnforceActivationOnStartup(string? actorId = null)
    {
        foreach (var model in _models.List())
        {
            var status = Status(model.Id);
            if (!model.IsActive || !status.Required || status.HasKey)
            {
                continue;
            }
            model.IsActive = false;
            if (TrySetActive(model, actorId))
            {
                Logger.Information("Модель {Name} деактивирована: нет ключа API ({SecretRef})",
                    model.Name, status.SecretRef);
            }
        }
    }

    /// <summary>
    /// Записать активность модели, если запись принадлежит ЭТОМУ серверу (ТЗ гл. 6, этап 42).
    /// Чужую запись правит её владелец — и это не повод ронять ни установку ключа, ни старт
    /// приложения: на рядовом сервере справочник моделей приезжает репликацией целиком.
    /// </summary>
    private bool TrySetActive(AI2P.Core.Entities.AiModel model, string? actorId)
    {
        try
        {
            _models.Update(model, actorId);
            return true;
        }
        catch (ArgumentException ex)
        {
            Logger.Debug("Активность модели {Name} здесь не меняется: {Reason}", model.Name, ex.Message);
            return false;
        }
    }

    /// <summary>Ссылка на ключ и признак CLI-подключения из профайла модели.</summary>
    private static (string SecretRef, bool IsCli) SecretRefOf(string profileJson)
    {
        if (profileJson.Trim().Length == 0)
        {
            return ("", false);
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ("", false);
            }
            var secretRef = root.TryGetProperty("secretRef", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()!.Trim()
                : "";
            var transport = root.TryGetProperty("transport", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()!.Trim()
                : "";
            return (secretRef, transport.Equals("cli", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return ("", false);
        }
    }

    private string ReadProfile(string profilePath)
    {
        if (profilePath.Length == 0)
        {
            return "";
        }
        var abs = _files.Abs(profilePath);
        return File.Exists(abs) ? File.ReadAllText(abs) : "";
    }
}
