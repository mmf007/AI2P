using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// ЗАПИСИ ПЛАГИНОВ ОРГАНИЗАЦИИ (T-111-S0) — шлюзы в видеоредакторы, конверторы, подключения
/// к серверам MCP.
///
/// Здесь только ХРАНЕНИЕ: завести запись по манифесту, поправить настройки, сменить
/// состояние, удалить. Жизненный цикл (инициализация, регистрация действий в справочнике,
/// поиск софта) — соседняя работа T-112-S0, и она пользуется этим сервисом.
///
/// ГЛАВНОЕ ПРАВИЛО, РАДИ КОТОРОГО СЕРВИС ВЫГЛЯДИТ ИМЕННО ТАК: в базу организации не попадает
/// НИ ОДНОГО пер-серверного значения — ни пути к melt или ffmpeg, ни пометки «инициализировал
/// сервер такой-то». Такие значения живут в config.json сервера (раздел <c>plugins</c>), и
/// метода записать их сюда попросту нет: реплицированный чужой путь ломает установку у
/// соседа и «уже не уходит» (наука T-164 про платформенные умолчания).
/// </summary>
public sealed class PluginService
{
    private readonly Database _db;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public PluginService(Database db, EventStore events, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>Все записи организации; порядок — по коду, он одинаков на всех серверах.</summary>
    public List<PluginRecord> List(bool includeDisabled = true)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM plugins WHERE deleted_at IS NULL"
            + (includeDisabled ? "" : " AND state<>'" + PluginStates.Disabled + "'")
            + " ORDER BY code",
            Map);
    }

    public PluginRecord? Get(string id)
    {
        using var conn = _db.Open();
        return Load(conn, null, id);
    }

    /// <summary>Запись по коду плагина (<c>editor.shotcut</c>) — им плагин зовут отовсюду:
    /// каталог манифеста, раздел config.json, приставка кодов действий.</summary>
    public PluginRecord? ByCode(string code)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null,
            "SELECT * FROM plugins WHERE code=@code AND deleted_at IS NULL", Map,
            ("@code", (code ?? "").Trim())).FirstOrDefault();
    }

    public PluginRecord Create(PluginRecord plugin, string? actorId)
    {
        Validate(plugin);
        var now = DateTime.UtcNow;
        plugin.CreatedAt = now;
        plugin.UpdatedAt = now;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureCodeFree(conn, tx, plugin.Code, plugin.Id);
        plugin.DisplayId = Database.NextDisplayId(conn, tx, "PLG", _scope.Code);
        Sql.Exec(conn, tx, """
            INSERT INTO plugins (id, display_id, code, kind, name, manifest_version, state,
                                 settings_json, created_at, updated_at)
            VALUES (@id, @did, @code, @kind, @name, @ver, @state, @settings, @created, @updated)
            """,
            ("@id", plugin.Id), ("@did", plugin.DisplayId), ("@code", plugin.Code),
            ("@kind", plugin.Kind), ("@name", plugin.Name),
            ("@ver", plugin.ManifestVersion), ("@state", plugin.State),
            ("@settings", plugin.SettingsJson),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        Append(conn, tx, EventTypes.PluginCreated, plugin, actorId);
        tx.Commit();
        return plugin;
    }

    /// <summary>
    /// ЗАВЕСТИ ЗАПИСЬ ПО МАНИФЕСТУ (<c>plugins/&lt;код&gt;/plugin.json</c>): название, вид и
    /// версия берутся из файла, настройки — умолчаниями из его блока <c>settings</c>.
    /// Запись с этим кодом уже есть — она и возвращается, второй раз плагин не заводится:
    /// манифест читает каждый сервер при старте, а строка организации одна на всех.
    /// </summary>
    public PluginRecord Declare(PluginManifest manifest, string? actorId, string? lang = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.IsValid)
        {
            throw new ArgumentException(Loc.T("msg.plugin.5", manifest.Code));
        }
        var existing = ByCode(manifest.Code);
        if (existing is not null)
        {
            return existing;
        }
        return Create(new PluginRecord
        {
            Code = manifest.Code,
            Kind = manifest.Kind,
            Name = manifest.Name.IsEmpty ? manifest.Code : manifest.Name.Text(lang),
            ManifestVersion = manifest.Version,
            State = PluginStates.Declared,
            SettingsJson = DefaultSettings(manifest),
        }, actorId);
    }

    /// <summary>Настройки записи по умолчанию — из блока <c>settings</c> манифеста.</summary>
    public static string DefaultSettings(PluginManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var setting in manifest.Settings)
            {
                if (setting.Default.Length == 0)
                {
                    continue;
                }
                w.WritePropertyName(setting.Key);
                switch (setting.Type)
                {
                    case PluginSetting.TypeNumber
                        when double.TryParse(setting.Default,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var num):
                        w.WriteNumberValue(num);
                        break;
                    case PluginSetting.TypeBool when bool.TryParse(setting.Default, out var flag):
                        w.WriteBooleanValue(flag);
                        break;
                    default:
                        w.WriteStringValue(setting.Default);
                        break;
                }
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Правка записи: название, версия манифеста и настройки. КОД И ВИД правкой не меняются —
    /// по коду живут каталог манифеста, раздел config.json и коды зарегистрированных
    /// действий, и «переименовать» плагин значило бы молча оставить всё это сиротами.
    /// </summary>
    public PluginRecord Update(PluginRecord plugin, string? actorId)
    {
        Validate(plugin);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var old = Load(conn, tx, plugin.Id)
                  ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", plugin.Id));
        plugin.Code = old.Code;
        plugin.Kind = old.Kind;
        plugin.DisplayId = old.DisplayId;
        Sql.Exec(conn, tx, """
            UPDATE plugins SET name=@name, manifest_version=@ver, state=@state,
                               settings_json=@settings, updated_at=@updated
            WHERE id=@id
            """,
            ("@name", plugin.Name), ("@ver", plugin.ManifestVersion), ("@state", plugin.State),
            ("@settings", plugin.SettingsJson), ("@updated", Sql.ToDb(now)), ("@id", plugin.Id));
        Append(conn, tx, EventTypes.PluginUpdated, plugin, actorId);
        tx.Commit();
        plugin.UpdatedAt = now;
        return plugin;
    }

    /// <summary>
    /// Сменить состояние (объявлен / инициализирован / выключен / снят). Отдельным методом,
    /// а не полем в <see cref="Update"/>: состояние меняет и человек кнопкой, и жизненный
    /// цикл плагина (T-112-S0) — а настройки записи при этом трогать нельзя.
    /// </summary>
    public PluginRecord SetState(string id, string state, string? actorId)
    {
        if (!PluginStates.IsKnown(state))
        {
            throw new ArgumentException(Loc.T("msg.plugin.4", state));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var plugin = Load(conn, tx, id)
                     ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", id));
        plugin.State = state;
        Sql.Exec(conn, tx, "UPDATE plugins SET state=@state, updated_at=@now WHERE id=@id",
            ("@state", state), ("@now", Sql.ToDb(now)), ("@id", id));
        Append(conn, tx, EventTypes.PluginStateChanged, plugin, actorId);
        tx.Commit();
        plugin.UpdatedAt = now;
        return plugin;
    }

    public void Delete(string id, string? actorId)
    {
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var plugin = Load(conn, tx, id)
                     ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", id));
        Sql.Exec(conn, tx,
            "UPDATE plugins SET deleted_at=@now, updated_at=@now WHERE id=@id",
            ("@now", Sql.ToDb(now)), ("@id", id));
        Append(conn, tx, EventTypes.PluginDeleted, plugin, actorId);
        tx.Commit();
    }

    // --- проверки ---

    private static void Validate(PluginRecord plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        plugin.Code = (plugin.Code ?? "").Trim();
        plugin.Name = (plugin.Name ?? "").Trim();
        if (plugin.Code.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.plugin.2"));
        }
        if (!PluginKinds.IsKnown(plugin.Kind))
        {
            throw new ArgumentException(Loc.T("msg.plugin.3", plugin.Kind ?? ""));
        }
        if (!PluginStates.IsKnown(plugin.State))
        {
            plugin.State = PluginStates.Declared;
        }
        if (plugin.Name.Length == 0)
        {
            plugin.Name = plugin.Code;
        }
        plugin.ManifestVersion = Math.Max(1, plugin.ManifestVersion);
        if (string.IsNullOrWhiteSpace(plugin.SettingsJson))
        {
            plugin.SettingsJson = "{}";
        }
        try
        {
            JsonDocument.Parse(plugin.SettingsJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException(Loc.T("msg.plugin.6"));
        }
    }

    private static void EnsureCodeFree(SqliteConnection conn, SqliteTransaction tx, string code,
        string id)
    {
        var busy = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM plugins WHERE code=@code AND id<>@id AND deleted_at IS NULL",
            ("@code", code), ("@id", id));
        if (busy > 0)
        {
            throw new InvalidOperationException(Loc.T("msg.plugin.7", code));
        }
    }

    private static PluginRecord? Load(SqliteConnection conn, SqliteTransaction? tx, string id) =>
        Sql.Query(conn, tx, "SELECT * FROM plugins WHERE id=@id AND deleted_at IS NULL", Map,
            ("@id", id)).FirstOrDefault();

    private void Append(SqliteConnection conn, SqliteTransaction tx, string eventType,
        PluginRecord plugin, string? actorId) =>
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "plugin",
            EntityId = plugin.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                plugin.DisplayId,
                plugin.Code,
                plugin.Kind,
                plugin.State,
            }),
        });

    internal static PluginRecord Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Code = r.S("code"),
        Kind = r.S("kind"),
        Name = r.S("name"),
        ManifestVersion = (int)r.L("manifest_version"),
        State = r.S("state"),
        SettingsJson = r.S("settings_json"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}
