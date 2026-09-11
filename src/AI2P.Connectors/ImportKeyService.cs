using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// КЛЮЧ И ТОКЕН ИСТОЧНИКА ИМПОРТА (ТЗ v1.65, T-123).
///
/// Раньше ключ API и токен Trello вписывались руками в <c>secrets.json</c>, а в форме
/// источника пользователь задавал лишь ССЫЛКИ на них (<c>trello.apiKey</c>, <c>trello.token</c>).
/// Теперь значения вводятся в интерфейсе — двумя кнопками, каждая спрашивает ровно одно поле, —
/// и ложатся туда же, куда ключи API моделей: в БД ОРГАНИЗАЦИИ, зашифрованными её ключом
/// (<see cref="ModelKeyService"/>, ТЗ гл. 10). Поэтому они доезжают до всех серверов
/// организации репликацией, а <c>secrets.json</c> перестаёт быть обязательным: он остаётся
/// откатом для уже существующих установок (порядок чтения — организация → файл → окружение).
///
/// Правило активности повторяет правило моделей: источник импорта БЕЗ ключа и токена активным
/// быть не может, а как только введено последнее из двух значений — он активируется сам.
/// Дальше активность переключает пользователь.
/// </summary>
public sealed class ImportKeyService
{
    private static readonly ILogger Logger = Log.ForContext<ImportKeyService>();

    /// <summary>Поле ключа API — им параметризованы форма и эндпоинт.</summary>
    public const string FieldKey = "key";

    /// <summary>Поле токена.</summary>
    public const string FieldToken = "token";

    private readonly ImportSourceService _sources;
    private readonly ModelKeyService _keys;

    public ImportKeyService(ImportSourceService sources, ModelKeyService keys)
    {
        _sources = sources;
        _keys = keys;
    }

    /// <summary>Ключ организации известен этому серверу (ТЗ гл. 10); без него значения
    /// ни прочитать, ни записать.</summary>
    public bool HasOrgKey => _keys.HasOrgKey;

    /// <summary>
    /// Виды источников, у которых ОДНО значение секрета — личный токен доступа (T-249):
    /// ключа API у них нет вовсе, поэтому форма не показывает кнопку ключа, а активным
    /// такой источник становится по одному введённому токену. Trello в этот список
    /// не входит: у него ключ интеграции и токен — два разных значения (T-134).
    /// </summary>
    private static readonly string[] TokenOnlyKinds = [GitLabImporter.Kind, GitHubImporter.Kind];

    /// <summary>У вида источника есть отдельный ключ API (T-249): у Trello — да,
    /// у GitLab и GitHub — нет, там всё решает один токен.</summary>
    public static bool NeedsKey(string kind) =>
        !TokenOnlyKinds.Contains(kind.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// Ссылки на ключ и токен источника: из params_json, а чего там нет — по умолчанию вида
    /// (<c>trello.apiKey</c>, <c>trello.token</c>). Разбор общий с импортёром — чтобы
    /// «где лежит ключ» было записано в одном месте. У источника с одним токеном ссылка
    /// на ключ пуста: спрашивать нечего (T-249).
    /// </summary>
    public static (string KeyRef, string TokenRef) RefsOf(ImportSource source)
    {
        if (string.Equals(source.Kind, GitLabImporter.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return ("", GitLabImporter.SourceParams.Parse(source.ParamsJson).TokenRef);
        }
        if (string.Equals(source.Kind, GitHubImporter.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return ("", GitHubImporter.SourceParams.Parse(source.ParamsJson).TokenRef);
        }
        var p = TrelloImporter.SourceParams.Parse(source.ParamsJson);
        return (NeedsKey(source.Kind) ? p.KeyRef : "", p.TokenRef);
    }

    /// <summary>Состояние ключа и токена источника; сами значения наружу не отдаются.</summary>
    public ImportKeyStatusDto Status(string sourceId)
    {
        var source = _sources.Get(sourceId)
                     ?? throw new ArgumentException(Loc.T("msg.importKey.1", sourceId));
        return StatusOf(source);
    }

    /// <summary>То же по объекту источника (он может быть ещё не сохранён).</summary>
    public ImportKeyStatusDto StatusOf(ImportSource source)
    {
        var (keyRef, tokenRef) = RefsOf(source);
        var (key, keySource) = keyRef.Length > 0 ? _keys.ResolveWithSource(keyRef) : (null, "");
        var (token, tokenSource) = _keys.ResolveWithSource(tokenRef);
        // вид проверки формата свой у каждой внешней системы (T-134)
        var isTrello = string.Equals(source.Kind, "trello", StringComparison.OrdinalIgnoreCase);
        var isGitLab = string.Equals(source.Kind, GitLabImporter.Kind, StringComparison.OrdinalIgnoreCase);
        var isGitHub = string.Equals(source.Kind, GitHubImporter.Kind, StringComparison.OrdinalIgnoreCase);
        var needsKey = NeedsKey(source.Kind);
        return new ImportKeyStatusDto
        {
            SourceId = source.Id,
            KeyRef = keyRef,
            TokenRef = tokenRef,
            // у источника с одним токеном ключа нет вовсе, и «не заполнен» здесь означало бы
            // «источник неисправен»: для него отсутствующий ключ — это заполненное состояние
            HasKey = !needsKey || !string.IsNullOrWhiteSpace(key),
            HasToken = !string.IsNullOrWhiteSpace(token),
            KeySource = keySource,
            TokenSource = tokenSource,
            HasOrgKey = HasOrgKey,
            IsActive = source.IsActive,
            NeedsKey = needsKey,
            // значение не похоже на то, что просят (T-134): у Trello чаще всего ключ и токен
            // оказываются перепутаны местами, у GitLab — вместо токена копируют что-то ещё
            KeyHint = isTrello ? TrelloImporter.SecretHint(FieldKey, key ?? "") : "",
            TokenHint = isTrello ? TrelloImporter.SecretHint(FieldToken, token ?? "")
                : isGitLab ? GitLabImporter.SecretHint(FieldToken, token ?? "")
                // у GitHub видно по префиксу (ghp_, github_pat_), что в поле токена
                // оказалось что-то другое — например, ключ Trello (T-247)
                : isGitHub ? GitHubImporter.SecretHint(token ?? "")
                : "",
        };
    }

    /// <summary>
    /// Установить ключ ЛИБО токен источника (ТЗ v1.65): одно поле за раз, старое значение
    /// не показывается и не возвращается. Введено последнее из двух — источник активируется.
    /// </summary>
    public ImportKeyStatusDto SetValue(string sourceId, string field, string value, string? actorId)
    {
        var source = _sources.Get(sourceId)
                     ?? throw new ArgumentException(Loc.T("msg.importKey.1", sourceId));
        var (keyRef, tokenRef) = RefsOf(source);
        var secretRef = field.Trim().ToLowerInvariant() switch
        {
            // у источника с одним токеном (GitLab, GitHub — T-249) ключа API нет вовсе:
            // класть значение некуда, и молча положить его «куда-нибудь» нельзя
            FieldKey when keyRef.Length == 0 => throw new ArgumentException(
                Loc.T("msg.importKey.10", source.Kind)),
            FieldKey => keyRef,
            FieldToken => tokenRef,
            _ => throw new ArgumentException(
                Loc.T("msg.importKey.2", field, FieldKey, FieldToken)),
        };
        if (value.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.importKey.3"));
        }

        _keys.SetByRef(secretRef, value.Trim(), actorId);
        Logger.Information("Источник импорта {Name}: обновлено поле {Field} (ссылка {SecretRef})",
            source.Name, field, secretRef);

        var status = StatusOf(source);
        // введено последнее из двух значений — источник становится активным сам (ТЗ v1.65)
        if (status is { HasKey: true, HasToken: true } && !source.IsActive)
        {
            source.IsActive = true;
            _sources.Update(source, actorId);
            status.IsActive = true;
            Logger.Information("Источник импорта {Name} активирован: ключ и токен установлены",
                source.Name);
        }
        return status;
    }

    /// <summary>
    /// Текст ошибки активации для <c>ImportSourceService.ActivationGuard</c>: импорт без ключа
    /// и токена работать не может, значит и активным быть не должен. null — можно активировать.
    /// </summary>
    public string? ActivationError(ImportSource source)
    {
        var status = StatusOf(source);
        if (status is { HasKey: true, HasToken: true })
        {
            return null;
        }
        var missing = (status.HasKey, status.HasToken) switch
        {
            (false, false) => Loc.T("msg.importKey.4"),
            (false, true) => Loc.T("msg.importKey.9"),
            _ => Loc.T("msg.importKey.5"),
        };
        return Loc.T("msg.importKey.6", source.Name, missing);
    }

    /// <summary>
    /// Имя источника, которому принадлежит ссылка — для списка конфликтов репликации
    /// (там же, где ключи моделей, todo45): пусто — ссылка не от источника импорта.
    /// </summary>
    public string SourceNameOf(string secretRef)
    {
        foreach (var source in _sources.List())
        {
            var (keyRef, tokenRef) = RefsOf(source);
            if (secretRef == keyRef)
            {
                return Loc.T("msg.importKey.7", source.Name);
            }
            if (secretRef == tokenRef)
            {
                return Loc.T("msg.importKey.8", source.Name);
            }
        }
        return "";
    }

    /// <summary>Все ссылки на ключи и токены источников справочника — для переноса из файла.</summary>
    public List<string> SecretRefs()
    {
        var refs = new List<string>();
        foreach (var source in _sources.List())
        {
            var (keyRef, tokenRef) = RefsOf(source);
            foreach (var secretRef in new[] { keyRef, tokenRef })
            {
                if (secretRef.Length > 0 && !refs.Contains(secretRef, StringComparer.Ordinal))
                {
                    refs.Add(secretRef);
                }
            }
        }
        return refs;
    }

    /// <summary>
    /// Перенести ключ и токен источников из secrets.json в организацию (ТЗ гл. 10): то же,
    /// что делается для ключей моделей. Идемпотентно, переменные окружения не трогаются.
    /// </summary>
    public int MigrateSecretsToOrg(string? actorId = null) =>
        _keys.MigrateRefsToOrg(SecretRefs(), actorId);
}
