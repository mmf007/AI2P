using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Storage.Services;

/// <summary>
/// ИНИЦИАЛИЗАЦИЯ И СНЯТИЕ ПЛАГИНА (T-112-S0): то, ради чего в жизненном цикле вообще есть
/// состояние «инициализирован» — ЗАПИСИ, которые плагин заводит в организации. Их две
/// породы, и обе реплицируются:
/// <list type="number">
/// <item>ДЕЙСТВИЕ СПРАВОЧНИКА НА КАЖДЫЙ ПУБЛИКУЕМЫЙ ИНСТРУМЕНТ. Без записи справочника
/// инструмент правилами безопасности не закрывается ВОВСЕ (2d3af8da): <c>CodeByTool</c>
/// возвращает null, и <c>AgentToolset.Authorize</c> вызов разрешает. Поэтому пока запись
/// не заведена — инструмент агенту не публикуется (<see cref="PublishedTools"/>), а для
/// плагинных инструментов в <c>Authorize</c> действует ОБРАТНАЯ политика: нет записи —
/// запрещено (<c>AgentToolset.PluginTools</c>). Для MCP запись заводится на каждый tool,
/// объявленный сервером: список у MCP динамический, и инструмент, появившийся в новой
/// версии сервера, обязан пройти регистрацию, а не проехать мимо правил молча.</item>
/// <item>ЗАПИСИ ОПЫТА как общие правила организации (IsGeneral, T-11-S0) с ОБЯЗАТЕЛЬНЫМ
/// навыком и автором <c>plugin:&lt;код&gt;</c> — по нему снятие плагина их и находит.
/// Навык обязателен не для красоты: общий предел на блоки опыта отбирается один раз по
/// всем источникам (a8a79272), и запись без навыка конкурирует за место в промпте у
/// КАЖДОЙ задачи организации. Пометка «загружать всегда» плагинным записям не ставится
/// по той же причине.</item>
/// </list>
///
/// ЗАПИСИ ДЕЙСТВИЙ — КАСТОМНЫЕ (<c>is_custom=1</c>, свой id, <c>seed_version=0</c>), и
/// заводятся они В БАЗЕ. В файлы сида <c>i18n/ActionCatalogService_&lt;язык&gt;.json</c>
/// плагин не пишет НИКОГДА: установка обновления кладёт файлы дистрибутива поверх,
/// дописанное пропадёт, а записи в базе останутся сиротами с чужим <c>seed_version</c>.
/// Сам <c>ActionCatalogService.Seed</c> кастомную строку не трогает ни при каком
/// seedVersion — он идёт по списку id из файлов, и уборки «лишних» записей в нём нет вовсе.
/// </summary>
public sealed class PluginSetupService
{
    private readonly Database _db;
    private readonly ExperienceService _experience;
    private readonly RefDataService _refData;
    private readonly PluginService? _plugins;

    /// <param name="plugins">Записи плагинов (T-111-S0); null — работать только с записями
    /// справочника и опыта, без смены состояния (так удобно проверкам).</param>
    public PluginSetupService(Database db, ExperienceService experience, RefDataService refData,
        PluginService? plugins = null)
    {
        _db = db;
        _experience = experience;
        _refData = refData;
        _plugins = plugins;
    }

    // ---------- жизненный цикл записи ----------

    /// <summary>
    /// ИНИЦИАЛИЗИРОВАТЬ ЗАПИСЬ: завести действия и опыт и перевести её в «инициализирован».
    /// Годится и для повторной инициализации снятого плагина — настройки и ручной путь
    /// снятие не трогало, вводить их заново не нужно.
    /// </summary>
    public PluginRecord InitializePlugin(PluginRecord plugin, PluginManifest manifest,
        IReadOnlyList<string>? mcpTools = null, string? actorId = null, string? lang = null,
        IReadOnlyList<McpToolInfo>? mcpDetails = null)
    {
        Initialize(manifest, mcpTools, lang, mcpDetails);
        return State(plugin, PluginStates.Initialized, actorId);
    }

    /// <summary>МЯГКИЙ ВЫКЛЮЧАТЕЛЬ: записи остаются на месте, действия агенту не публикуются
    /// (<see cref="PluginLifecycle.Publishes"/>) — чтобы не терять настройки.</summary>
    public PluginRecord DisablePlugin(PluginRecord plugin, string? actorId = null) =>
        State(plugin, PluginStates.Disabled, actorId);

    /// <summary>Включить выключенный плагин обратно: записи на месте, заводить нечего.</summary>
    public PluginRecord EnablePlugin(PluginRecord plugin, string? actorId = null) =>
        State(plugin, PluginStates.Initialized, actorId);

    /// <summary>СНЯТЬ: убрать действия и записи опыта и перевести запись в «снят».</summary>
    public PluginRecord RemovePlugin(PluginRecord plugin, PluginManifest manifest,
        IReadOnlyList<string>? mcpTools = null, string? actorId = null)
    {
        Remove(manifest, mcpTools);
        return State(plugin, PluginStates.Removed, actorId);
    }

    private PluginRecord State(PluginRecord plugin, string state, string? actorId)
    {
        if (_plugins is null)
        {
            plugin.State = state;
            return plugin;
        }
        return _plugins.SetState(plugin.Id, state, actorId);
    }

    /// <summary>
    /// ИНИЦИАЛИЗАЦИЯ: заводит запись справочника действий на каждый публикуемый инструмент и
    /// записи опыта плагина. Идемпотентна — повторный вызов (переинициализация после снятия,
    /// приезд манифеста новой версии с новыми инструментами, новый tool у сервера MCP)
    /// добавляет недостающее и не плодит вторых экземпляров. Возвращает число заведённых
    /// записей действий.
    /// </summary>
    /// <param name="manifest">Манифест плагина (файл дистрибутива).</param>
    /// <param name="mcpTools">Инструменты, объявленные сервером MCP (у шлюза — null:
    /// его инструменты названы в манифесте).</param>
    /// <param name="lang">Язык, на котором берутся тексты манифеста; null — язык установки.</param>
    /// <param name="mcpDetails">Название и описание инструментов, как их назвал сервер MCP
    /// (T-119-S0): они ложатся в название и подсказку записи справочника. Не переданы —
    /// в справочнике встанет само имя инструмента.</param>
    public int Initialize(PluginManifest manifest, IReadOnlyList<string>? mcpTools = null,
        string? lang = null, IReadOnlyList<McpToolInfo>? mcpDetails = null)
    {
        var code = Code(manifest.Code);
        var added = ToolsOf(manifest, mcpTools, mcpDetails)
            .Count(action => AddAction(code, action, lang));
        var known = Owned(code).Select(r => r.Text).ToHashSet(StringComparer.Ordinal);
        foreach (var record in manifest.Experience)
        {
            var text = record.Text.Text(lang);
            if (record.Skill.Trim().Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.pluginSetup.1", code));
            }
            if (text.Length == 0 || known.Contains(text))
            {
                continue;
            }
            // пометка владельца — служебный ТЭГ записи: колонки под неё нет, а created_by
            // ссылается на исполнителей (внешний ключ), и строкой «plugin:…» его не занять.
            // Отбору опыта такой тэг не мешает: ExperienceService.TagsMatch его пропускает
            _experience.CreateGeneral(text, actorId: null, SkillId(record.Skill),
                tags: [PluginCodes.ExperienceOwner(code)]);
        }
        return added;
    }

    /// <summary>
    /// Инструменты плагина, которые МОЖНО публиковать агенту: только те, у кого есть запись
    /// в справочнике действий. Инструмент, появившийся у MCP-сервера в новой версии и ещё не
    /// зарегистрированный, сюда не попадает.
    /// </summary>
    public IReadOnlyList<string> PublishedTools(PluginManifest manifest,
        IReadOnlyList<string>? mcpTools = null)
    {
        var registered = RegisteredTools(manifest, mcpTools).ToHashSet(StringComparer.Ordinal);
        return [.. ToolsOf(manifest, mcpTools).Select(a => a.Tool).Where(registered.Contains)];
    }

    /// <summary>Имена инструментов плагина, у которых запись справочника уже есть.</summary>
    public IReadOnlyList<string> RegisteredTools(PluginManifest manifest,
        IReadOnlyList<string>? mcpTools = null)
    {
        var codes = ToolsOf(manifest, mcpTools)
            .ToDictionary(a => PluginCodes.Action(manifest.Code, a), a => a.Tool, StringComparer.Ordinal);
        using var conn = _db.Open();
        return [.. Sql.Query(conn, null,
                "SELECT code, tool_name FROM actions WHERE deleted_at IS NULL AND tool_name IS NOT NULL",
                r => (Code: r.S("code"), Tool: r.S("tool_name")))
            .Where(row => codes.TryGetValue(row.Code, out var tool) && tool == row.Tool)
            .Select(row => row.Tool)];
    }

    /// <summary>
    /// ВСЕ ИМЕНА ИНСТРУМЕНТОВ, КОТОРЫЕ ПРИШЛИ ОТ ПЛАГИНОВ — набор для обратной политики
    /// <c>AgentToolset.PluginTools</c>. Считается по МАНИФЕСТАМ и по списку инструментов
    /// сервера MCP, а не по справочнику действий: смысл политики ровно в том, чтобы поймать
    /// инструмент, записи у которого ЕЩЁ (или уже) НЕТ.
    /// </summary>
    public static HashSet<string> PluginToolNames(IEnumerable<PluginManifest> manifests,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? mcpTools = null)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            var extra = mcpTools is not null && mcpTools.TryGetValue(manifest.Code, out var list)
                ? list
                : null;
            foreach (var action in ToolsOf(manifest, extra))
            {
                names.Add(action.Tool);
            }
        }
        return names;
    }

    /// <summary>
    /// ЧЕЙ ИНСТРУМЕНТ: имя инструмента → код плагина (T-155-S0). Тем же составом, что
    /// <see cref="PluginToolNames"/>, но с хозяином: правило безопасности вида «плагины и MCP»
    /// заводится на ПЛАГИН, а вызов приходит именем инструмента.
    /// </summary>
    public static Dictionary<string, string> PluginToolOwners(IEnumerable<PluginManifest> manifests,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? mcpTools = null)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            var extra = mcpTools is not null && mcpTools.TryGetValue(manifest.Code, out var list)
                ? list
                : null;
            foreach (var action in ToolsOf(manifest, extra))
            {
                owners[action.Tool] = manifest.Code;
            }
        }
        return owners;
    }

    /// <summary>
    /// СНЯТИЕ ПЛАГИНА: убирает всё, что он породил, — записи действий и записи опыта
    /// (9d3a337a: у порождённого обязана быть кнопка «убрать»). Ручной путь к софту и
    /// настройки записи здесь не трогаются: они лежат в config.json сервера и в самой
    /// записи, повторная инициализация не требует вводить всё заново. Правила безопасности,
    /// ссылавшиеся на снятые коды, остаются с неизвестным кодом — и это читается как
    /// «запрещено», а не как «разрешено»: инструмент без записи справочника плагин больше
    /// не публикует, а обратная политика <c>Authorize</c> закрывает его и в обход публикации.
    /// </summary>
    public void Remove(PluginManifest manifest, IReadOnlyList<string>? mcpTools = null)
    {
        var code = Code(manifest.Code);
        // коды из манифеста + всё, что заведено приставкой по умолчанию (динамика MCP)
        var codes = ToolsOf(manifest, mcpTools)
            .Select(a => PluginCodes.Action(code, a))
            .ToHashSet(StringComparer.Ordinal);
        var prefix = PluginCodes.ActionPrefix + code + ".";
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        var ids = Sql.Query(conn, null,
                "SELECT id, code FROM actions WHERE deleted_at IS NULL",
                r => (Id: r.S("id"), Code: r.S("code")))
            .Where(row => codes.Contains(row.Code) || row.Code.StartsWith(prefix, StringComparison.Ordinal))
            .Select(row => row.Id)
            .ToList();
        using (var tx = conn.BeginTransaction())
        {
            foreach (var id in ids)
            {
                // мягкое удаление: строка уезжает журналом изменений и снимается у всего кластера
                Sql.Exec(conn, tx,
                    "UPDATE actions SET deleted_at=@now, updated_at=@now WHERE id=@id",
                    ("@now", now), ("@id", id));
            }
            tx.Commit();
        }
        foreach (var record in Owned(code))
        {
            _experience.Delete(record.Id, actorId: null);
        }
    }

    /// <summary>
    /// НАЗВАНИЕ ЗАВЕДЁННОГО ДЕЙСТВИЯ на этом языке — форме плагина оно нужно у инструментов
    /// MCP: манифест их не называет, название пришло от самого сервера и лежит в справочнике.
    /// Пусто — записи ещё нет (инструмент не зарегистрирован).
    /// </summary>
    public string ActionTitle(string actionCode, string? lang)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT t.title AS title FROM actions a
            JOIN action_texts t ON t.action_id = a.id AND t.lang = @lang
            WHERE a.code = @code COLLATE NOCASE AND a.deleted_at IS NULL
            """, r => r.S("title"),
            ("@code", actionCode), ("@lang", lang is { Length: > 0 } ? lang : Loc.Lang))
            .FirstOrDefault() ?? "";
    }

    /// <summary>Записи опыта плагина (для формы и проверок).</summary>
    public List<ExperienceRecord> ExperienceOf(string pluginCode) => Owned(Code(pluginCode));

    /// <summary>Общие записи опыта, помеченные владельцем <c>plugin:&lt;код&gt;</c>.</summary>
    private List<ExperienceRecord> Owned(string pluginCode)
    {
        var owner = PluginCodes.ExperienceOwner(pluginCode);
        return [.. _experience.ListGeneral()
            .Where(r => r.Tags.Any(tag => TaskTags.Same(tag, owner)))];
    }

    /// <summary>Инструменты плагина: у шлюза — действия манифеста, у MCP — то, что объявил
    /// сервер (кодов в манифесте у них нет, они складываются приставкой).</summary>
    private static IEnumerable<PluginAction> ToolsOf(PluginManifest manifest,
        IReadOnlyList<string>? mcpTools, IReadOnlyList<McpToolInfo>? mcpDetails = null)
    {
        if (manifest.Kind != PluginKinds.Mcp)
        {
            return manifest.Actions.Where(a => a.Tool.Trim().Length > 0);
        }
        // имена берутся из списка (его хранит кэш сервера), а название и описание — из
        // подробностей, если их сняли только что; списка нет вовсе — значит инструменты
        // и есть подробности (так зовёт инициализация сразу после опроса сервера)
        var names = mcpTools ?? [.. (mcpDetails ?? []).Select(d => d.Name)];
        var byName = (mcpDetails ?? []).ToDictionary(d => d.Name, StringComparer.Ordinal);
        return names.Where(t => t.Trim().Length > 0).Select(t =>
        {
            var action = new PluginAction { Tool = t.Trim() };
            if (byName.TryGetValue(action.Tool, out var info))
            {
                // тексты сервера MCP одинаковы на всех языках: перевести их нам нечем,
                // а пустая строка в справочнике читалась бы как потерянное действие
                foreach (var lang in Loc.Languages)
                {
                    action.Title.Set(lang, info.TitleOrName);
                    if (info.Description.Length > 0)
                    {
                        action.Description.Set(lang, info.Description);
                    }
                }
            }
            return action;
        });
    }

    /// <summary>
    /// СНЯТЬ ЗАПИСИ СПРАВОЧНИКА У ПЕРЕЧИСЛЕННЫХ ИНСТРУМЕНТОВ (T-119-S0) — ИСЧЕЗНУВШИЕ
    /// инструменты сервера MCP. Мягкое удаление, как у снятия плагина целиком: строка уезжает
    /// журналом изменений и снимается у всего кластера. Возвращает число снятых записей.
    /// </summary>
    public int RemoveTools(string pluginCode, IEnumerable<string> tools)
    {
        var code = Code(pluginCode);
        var codes = tools.Where(t => t.Trim().Length > 0)
            .Select(t => PluginCodes.Action(code, t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0)
        {
            return 0;
        }
        var now = Sql.ToDb(DateTime.UtcNow);
        using var conn = _db.Open();
        var ids = Sql.Query(conn, null,
                "SELECT id, code FROM actions WHERE deleted_at IS NULL",
                r => (Id: r.S("id"), Code: r.S("code")))
            .Where(row => codes.Contains(row.Code))
            .Select(row => row.Id)
            .ToList();
        using var tx = conn.BeginTransaction();
        foreach (var id in ids)
        {
            Sql.Exec(conn, tx, "UPDATE actions SET deleted_at=@now, updated_at=@now WHERE id=@id",
                ("@now", now), ("@id", id));
        }
        tx.Commit();
        return ids.Count;
    }

    /// <summary>Запись действия на инструмент; false — она уже есть.</summary>
    private bool AddAction(string pluginCode, PluginAction action, string? lang)
    {
        var toolName = action.Tool.Trim();
        var code = PluginCodes.Action(pluginCode, action);
        using var conn = _db.Open();
        var exists = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM actions WHERE code=@code COLLATE NOCASE AND deleted_at IS NULL",
            ("@code", code)) > 0;
        if (exists)
        {
            return false;
        }
        // ЧУЖОЙ инструмент с таким именем (сидовое действие AI2P или другой плагин) забирать
        // нельзя: правило безопасности на наше действие увело бы вызов в плагин
        var taken = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM actions WHERE tool_name=@tool AND deleted_at IS NULL",
            ("@tool", toolName)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.pluginSetup.2", toolName, pluginCode));
        }
        var id = Guid.NewGuid().ToString();
        var now = Sql.ToDb(DateTime.UtcNow);
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            INSERT INTO actions (id, type, title, code, tool_name, is_custom, seed_version,
                                 created_at, updated_at)
            VALUES (@id, 'tool', @code, @code, @tool, 1, 0, @now, @now)
            """, ("@id", id), ("@code", code), ("@tool", toolName), ("@now", now));
        // тексты — на все языки словаря: манифест обычно называет один-два, а справочник
        // читают на языке команды задачи, и пустая строка выглядела бы как потерянное действие
        foreach (var textLang in Loc.Languages)
        {
            var title = action.Title.Text(textLang);
            var hint = action.Description.Text(textLang);
            Sql.Exec(conn, tx, """
                INSERT INTO action_texts (action_id, lang, title, hint, prompt_default)
                VALUES (@id, @lang, @title, @hint, @prompt)
                """, ("@id", id), ("@lang", textLang),
                ("@title", title.Length > 0 ? title : toolName),
                ("@hint", hint), ("@prompt", hint.Length > 0 ? hint : action.Title.Text(lang)));
        }
        tx.Commit();
        return true;
    }

    /// <summary>Идентификатор навыка по коду (<c>video-edit</c>).</summary>
    private string SkillId(string skill)
    {
        var name = skill.Trim();
        var found = _refData.Skills()
            .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return found?.Id ?? throw new ArgumentException(Loc.T("msg.pluginSetup.3", name));
    }

    private static string Code(string pluginCode) =>
        pluginCode.Trim().Length > 0
            ? pluginCode.Trim()
            : throw new ArgumentException(Loc.T("msg.pluginSetup.4"));
}
