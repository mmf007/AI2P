using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Api;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Справочник действий агентов (ТЗ v1.21, todo24): промпты (описания инструментов для агента),
/// заголовки и подсказки для человека — НЕ в коде, а в настройке системы. Действия иерархичны
/// (внешний код через точку: AI2P.Files.Read), делятся на встроенные (из дистрибутива;
/// редактируется только промпт, правка хранится отдельно и откатывается к сиду) и кастомные.
/// Первичная загрузка встроенных — из файлов дистрибутива i18n/ActionCatalogService_&lt;lang&gt;.json
/// (по файлу на язык; поддержка нового языка = файл перевода, правка todo24). Недостающий язык
/// падает на другой. Внутренний код подставляет промпты отсюда (AiConnectorBase → PromptByTool).
/// </summary>
public sealed class ActionCatalogService
{
    /// <summary>Префикс файлов сида в каталоге словарей: ActionCatalogService_ru.json, …</summary>
    private const string SeedFilePrefix = "ActionCatalogService_";

    private readonly Database _db;
    private readonly string _seedDir;

    /// <summary>Языки, обнаруженные по файлам сида (после Seed); для новых кастомных записей.</summary>
    private string[] _languages = ["en", "ru"];

    /// <param name="seedDir">Каталог файлов сида (i18n рядом с приложением, ТЗ гл. 9).</param>
    public ActionCatalogService(Database db, string seedDir)
    {
        _db = db;
        _seedDir = seedDir;
    }

    /// <summary>Файл сида одного языка: версия + действия.</summary>
    private sealed class SeedFile
    {
        public int SeedVersion { get; set; }
        public List<SeedEntry> Actions { get; set; } = [];
    }

    /// <summary>Действие в файле сида: id/код/инструмент одинаковы во всех языках, тексты — свои.</summary>
    private sealed class SeedEntry
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string? Tool { get; set; }
        public string Title { get; set; } = "";
        public string Hint { get; set; } = "";
        public string Prompt { get; set; } = "";
    }

    /// <summary>Стартовое наполнение и обновление встроенных действий из файлов дистрибутива
    /// i18n/ActionCatalogService_&lt;lang&gt;.json (идемпотентно): prompt_default/title/hint
    /// обновляются только при росте seedVersion файла; кастомная правка prompt_custom и
    /// кастомные действия пользователя не трогаются. Поддержка нового языка = файл перевода.</summary>
    public void Seed()
    {
        var files = LoadSeedFiles();
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.actionCatalog.1") +
                Path.Combine(_seedDir, SeedFilePrefix + Loc.T("msg.actionCatalog.2")));
        }
        _languages = files.Keys.OrderBy(l => l, StringComparer.Ordinal).ToArray();

        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var ids = files.Values.SelectMany(f => f.Actions.Select(a => a.Id)).Distinct();
        foreach (var id in ids)
        {
            // тексты действия по языкам; код/инструмент одинаковы, версия — максимум по файлам
            var perLang = files
                .Where(kv => kv.Value.Actions.Any(a => a.Id == id))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Actions.First(a => a.Id == id));
            var main = perLang.Values.First();
            var version = files.Where(kv => perLang.ContainsKey(kv.Key)).Max(kv => kv.Value.SeedVersion);

            var seeded = Sql.Scalar<long?>(conn, tx,
                "SELECT seed_version FROM actions WHERE id=@id", ("@id", id));
            if (seeded is null)
            {
                Sql.Exec(conn, tx, """
                    INSERT INTO actions (id, type, title, code, tool_name, is_custom, seed_version,
                                         created_at, updated_at)
                    VALUES (@id, 'tool', @code, @code, @tool, 0, @seed, @now, @now)
                    """,
                    ("@id", id), ("@code", main.Code), ("@tool", main.Tool),
                    ("@seed", version), ("@now", Sql.ToDb(now)));
            }
            else if (seeded < version)
            {
                Sql.Exec(conn, tx,
                    "UPDATE actions SET code=@code, tool_name=@tool, seed_version=@seed, updated_at=@now WHERE id=@id",
                    ("@code", main.Code), ("@tool", main.Tool),
                    ("@seed", version), ("@now", Sql.ToDb(now)), ("@id", id));
            }
            foreach (var (lang, entry) in perLang)
            {
                if (seeded is null
                    || Sql.Scalar<long>(conn, tx,
                        "SELECT COUNT(*) FROM action_texts WHERE action_id=@id AND lang=@lang",
                        ("@id", id), ("@lang", lang)) == 0)
                {
                    Sql.Exec(conn, tx, """
                        INSERT OR REPLACE INTO action_texts (action_id, lang, title, hint, prompt_default, prompt_custom)
                        VALUES (@id, @lang, @title, @hint, @prompt,
                                (SELECT prompt_custom FROM action_texts WHERE action_id=@id AND lang=@lang))
                        """,
                        ("@id", id), ("@lang", lang), ("@title", entry.Title),
                        ("@hint", entry.Hint), ("@prompt", entry.Prompt));
                }
                else if (seeded < version)
                {
                    Sql.Exec(conn, tx, """
                        UPDATE action_texts SET title=@title, hint=@hint, prompt_default=@prompt
                        WHERE action_id=@id AND lang=@lang
                        """,
                        ("@title", entry.Title), ("@hint", entry.Hint), ("@prompt", entry.Prompt),
                        ("@id", id), ("@lang", lang));
                }
            }
        }
        tx.Commit();
    }

    /// <summary>Файлы сида по языкам: ActionCatalogService_ru.json → "ru" (todo24).
    /// Повреждённый файл — понятная ошибка на старте (дистрибутив чинится правкой файла).</summary>
    private Dictionary<string, SeedFile> LoadSeedFiles()
    {
        var result = new Dictionary<string, SeedFile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_seedDir))
        {
            return result;
        }
        foreach (var path in Directory.EnumerateFiles(_seedDir, SeedFilePrefix + "*.json"))
        {
            var lang = Path.GetFileNameWithoutExtension(path)[SeedFilePrefix.Length..]
                .Trim().ToLowerInvariant();
            if (lang.Length == 0)
            {
                continue;
            }
            SeedFile? file;
            try
            {
                file = JsonSerializer.Deserialize<SeedFile>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(Loc.T("msg.actionCatalog.3", path, ex.Message));
            }
            if (file is null || file.Actions.Count == 0)
            {
                continue;
            }
            if (file.Actions.Any(a => a.Id.Trim().Length == 0 || a.Code.Trim().Length == 0))
            {
                throw new InvalidOperationException(Loc.T("msg.actionCatalog.4", path));
            }
            result[lang] = file;
        }
        return result;
    }

    /// <summary>Язык справочника: любой код языка в нижнем регистре; пусто — en (todo24).</summary>
    private static string Norm(string lang) =>
        lang.Trim().ToLowerInvariant() is { Length: > 0 } normalized ? normalized : "en";

    /// <summary>Язык-фолбэк при отсутствии текстов: en (для en — ru).</summary>
    private static string Fallback(string lang) => lang == "en" ? "ru" : "en";

    /// <summary>Список действий с текстами языка (нет текста — язык-фолбэк), сортировка по коду.</summary>
    public List<ActionDto> List(string lang)
    {
        lang = Norm(lang);
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
                SELECT a.id, a.code, a.tool_name, a.is_custom,
                       COALESCE(t.title, f.title, '')                    AS title,
                       COALESCE(t.hint, f.hint, '')                      AS hint,
                       COALESCE(t.prompt_custom, t.prompt_default,
                                f.prompt_custom, f.prompt_default, '')   AS prompt,
                       COALESCE(t.prompt_default, f.prompt_default, '')  AS prompt_default,
                       (t.prompt_custom IS NOT NULL)                     AS prompt_modified
                FROM actions a
                LEFT JOIN action_texts t ON t.action_id = a.id AND t.lang = @lang
                LEFT JOIN action_texts f ON f.action_id = a.id AND f.lang = @fallback
                WHERE a.deleted_at IS NULL AND a.code <> ''
                ORDER BY a.code
                """, Map, ("@lang", lang), ("@fallback", Fallback(lang)));
    }

    /// <summary>Создать кастомное действие: код уникален, тексты пишутся во все поддерживаемые
    /// языки (другие языки — тем же текстом, пока пользователь не поправит их отдельно).</summary>
    public ActionDto Create(ActionSaveDto dto, string lang)
    {
        lang = Norm(lang);
        var code = dto.Code.Trim();
        if (code.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.actionCatalog.5"));
        }
        using var conn = _db.Open();
        var taken = Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM actions WHERE code=@code COLLATE NOCASE AND deleted_at IS NULL",
            ("@code", code)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.actionCatalog.6", code));
        }
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx, """
            INSERT INTO actions (id, type, title, code, tool_name, is_custom, created_at, updated_at)
            VALUES (@id, 'tool', @code, @code, NULL, 1, @now, @now)
            """, ("@id", id), ("@code", code), ("@now", Sql.ToDb(now)));
        foreach (var textLang in _languages)
        {
            Sql.Exec(conn, tx, """
                INSERT INTO action_texts (action_id, lang, title, hint, prompt_default)
                VALUES (@id, @lang, @title, @hint, @prompt)
                """, ("@id", id), ("@lang", textLang), ("@title", dto.Title.Trim()),
                ("@hint", dto.Hint), ("@prompt", dto.Prompt));
        }
        tx.Commit();
        return Get(conn, id, lang);
    }

    /// <summary>Сохранить действие. Встроенное: редактируется ТОЛЬКО промпт — правка хранится
    /// отдельно (prompt_custom); промпт, равный значению по умолчанию, правкой не считается.
    /// Кастомное: код/заголовок/подсказка/промпт языка запроса.</summary>
    public ActionDto Update(string id, ActionSaveDto dto, string lang)
    {
        lang = Norm(lang);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        var isCustom = Sql.Scalar<long?>(conn, null,
                           "SELECT is_custom FROM actions WHERE id=@id AND deleted_at IS NULL", ("@id", id))
                       ?? throw new InvalidOperationException(Loc.T("msg.actionCatalog.7", id));
        using var tx = conn.BeginTransaction();
        if (isCustom == 1)
        {
            var code = dto.Code.Trim();
            if (code.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.actionCatalog.5"));
            }
            var taken = Sql.Scalar<long>(conn, tx,
                "SELECT COUNT(*) FROM actions WHERE code=@code COLLATE NOCASE AND id<>@id AND deleted_at IS NULL",
                ("@code", code), ("@id", id)) > 0;
            if (taken)
            {
                throw new ArgumentException(Loc.T("msg.actionCatalog.6", code));
            }
            Sql.Exec(conn, tx, "UPDATE actions SET code=@code, title=@code, updated_at=@now WHERE id=@id",
                ("@code", code), ("@now", Sql.ToDb(now)), ("@id", id));
            Sql.Exec(conn, tx, """
                INSERT INTO action_texts (action_id, lang, title, hint, prompt_default)
                VALUES (@id, @lang, @title, @hint, @prompt)
                ON CONFLICT(action_id, lang) DO UPDATE SET title=@title, hint=@hint, prompt_default=@prompt
                """, ("@id", id), ("@lang", lang), ("@title", dto.Title.Trim()),
                ("@hint", dto.Hint), ("@prompt", dto.Prompt));
        }
        else
        {
            // встроенное: только промпт; совпадение с сидом — просто сброс правки
            var prompt = dto.Prompt;
            var defaultPrompt = Sql.Scalar<string>(conn, tx,
                "SELECT prompt_default FROM action_texts WHERE action_id=@id AND lang=@lang",
                ("@id", id), ("@lang", lang)) ?? "";
            Sql.Exec(conn, tx, """
                UPDATE action_texts SET prompt_custom=@custom WHERE action_id=@id AND lang=@lang
                """,
                ("@custom", prompt.Trim().Length == 0 || prompt == defaultPrompt ? null : prompt),
                ("@id", id), ("@lang", lang));
            Sql.Exec(conn, tx, "UPDATE actions SET updated_at=@now WHERE id=@id",
                ("@now", Sql.ToDb(now)), ("@id", id));
        }
        tx.Commit();
        return Get(conn, id, lang);
    }

    /// <summary>Откатить промпт встроенного действия к значению по умолчанию (кнопка в форме).</summary>
    public ActionDto ResetPrompt(string id, string lang)
    {
        lang = Norm(lang);
        using var conn = _db.Open();
        Sql.Exec(conn, null,
            "UPDATE action_texts SET prompt_custom=NULL WHERE action_id=@id AND lang=@lang",
            ("@id", id), ("@lang", lang));
        return Get(conn, id, lang);
    }

    /// <summary>Код действия по имени инструмента (для правил безопасности, гл. 12, todo25);
    /// null — инструмента нет в справочнике.</summary>
    public string? CodeByTool(string toolName)
    {
        using var conn = _db.Open();
        var code = Sql.Scalar<string>(conn, null,
            "SELECT code FROM actions WHERE tool_name=@tool AND deleted_at IS NULL", ("@tool", toolName));
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    /// <summary>Действующий промпт инструмента для агента (подстановка во внутреннем коде, todo24):
    /// кастомная правка либо значение по умолчанию; null — инструмента нет в справочнике.</summary>
    public string? PromptByTool(string toolName, string lang)
    {
        lang = Norm(lang);
        using var conn = _db.Open();
        var prompt = Sql.Scalar<string>(conn, null, """
            SELECT COALESCE(t.prompt_custom, t.prompt_default, f.prompt_custom, f.prompt_default)
            FROM actions a
            LEFT JOIN action_texts t ON t.action_id = a.id AND t.lang = @lang
            LEFT JOIN action_texts f ON f.action_id = a.id AND f.lang = @fallback
            WHERE a.tool_name = @tool AND a.deleted_at IS NULL
            """, ("@lang", lang), ("@fallback", Fallback(lang)), ("@tool", toolName));
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    private ActionDto Get(SqliteConnection conn, string id, string lang) =>
        Sql.Query(conn, null, """
            SELECT a.id, a.code, a.tool_name, a.is_custom,
                   COALESCE(t.title, f.title, '')                    AS title,
                   COALESCE(t.hint, f.hint, '')                      AS hint,
                   COALESCE(t.prompt_custom, t.prompt_default,
                            f.prompt_custom, f.prompt_default, '')   AS prompt,
                   COALESCE(t.prompt_default, f.prompt_default, '')  AS prompt_default,
                   (t.prompt_custom IS NOT NULL)                     AS prompt_modified
            FROM actions a
            LEFT JOIN action_texts t ON t.action_id = a.id AND t.lang = @lang
            LEFT JOIN action_texts f ON f.action_id = a.id AND f.lang = @fallback
            WHERE a.id = @id
            """, Map, ("@lang", lang), ("@fallback", Fallback(lang)), ("@id", id))
            .FirstOrDefault() ?? throw new InvalidOperationException(Loc.T("msg.actionCatalog.7", id));

    private static ActionDto Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        Code = r.S("code"),
        ToolName = r.SN("tool_name"),
        IsCustom = r.L("is_custom") == 1,
        Title = r.S("title"),
        Hint = r.S("hint"),
        Prompt = r.S("prompt"),
        DefaultPrompt = r.S("prompt_default"),
        PromptModified = r.L("prompt_modified") == 1,
    };
}
