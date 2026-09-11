using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Справочники: профессиональные роли (ТЗ п. 2.8), skills и io formats (пп. 7.3, v1.17) —
/// единые для людей и ИИ. Пополняемые списки; стартовый набор заполняется при первом запуске.
/// Внутренние коды skills/io formats — en, названия и описания хранятся ПО ЯЗЫКАМ
/// (role_texts / skill_texts / io_format_texts).
///
/// Стартовый набор приезжает из файлов дистрибутива i18n/RefDataService_&lt;lang&gt;.json
/// (T-191, как у <see cref="ActionCatalogService"/>): вписывать названия в код нельзя —
/// значения уезжают В БАЗУ ОРГАНИЗАЦИИ и реплицируются, то есть язык установки, на которой
/// сеяли первой, стал бы языком всего кластера навсегда. Поддержка нового языка = файл
/// перевода рядом; сеяние идемпотентно и повторяется при росте seedVersion.
/// </summary>
public sealed class RefDataService
{
    /// <summary>Префикс файлов сида в каталоге словарей: RefDataService_ru.json, …</summary>
    private const string SeedFilePrefix = "RefDataService_";

    /// <summary>Ключ в meta: до какой версии сида справочники уже доведены.</summary>
    private const string SeedVersionKey = "refdata_seed_version";

    /// <summary>
    /// Переименование кодов навыков при заполнении справочника (ТЗ v1.39, todo35_2):
    /// старые стартовые коды → иерархические (категория-действие). Ссылки задач
    /// (task_skills) идут по id — переименование их не ломает; в декларациях
    /// возможностей коды заменяет <see cref="MigrateSkillCodes"/>.
    /// </summary>
    public static readonly (string Old, string New)[] SkillRenames =
    [
        ("write-code", "code-write"),
        ("review-code", "code-review"),
        ("test-run", "code-test"),
        ("write-docs", "text-docs"),
        ("draw-concept", "image-concept"),
    ];

    /// <summary>
    /// Языки третьей категории code-навыков (ТЗ v1.39): код — стандартное расширение
    /// файла исходника, 12 самых популярных. Навык без языка означает «любой язык»
    /// и при автоподборе покрывает все языковые уточнения (ExecutorPickService).
    /// </summary>
    public static readonly (string Ext, string Lang)[] CodeLanguages =
    [
        ("py", "Python"), ("js", "JavaScript"), ("ts", "TypeScript"), ("java", "Java"),
        ("cs", "C#"), ("cpp", "C++"), ("c", "C"), ("go", "Go"),
        ("rs", "Rust"), ("php", "PHP"), ("swift", "Swift"), ("kt", "Kotlin"),
    ];

    private readonly Database _db;
    private readonly string _seedDir;

    /// <param name="seedDir">Каталог файлов сида (i18n рядом с приложением, ТЗ гл. 9).</param>
    public RefDataService(Database db, string seedDir)
    {
        _db = db;
        _seedDir = seedDir;
    }

    /// <summary>Файл сида одного языка: версия + названия справочников на этом языке.</summary>
    private sealed class SeedFile
    {
        public int SeedVersion { get; set; }
        public List<SeedRole> Roles { get; set; } = [];

        /// <summary>Базовые code-навыки: каждый получает языковые варианты «код-язык» (todo35_2).</summary>
        public List<SeedItem> CodeSkills { get; set; } = [];

        /// <summary>Навыки остальных категорий (ТЗ v1.39: text, analyze, image, video, audio, 3d).</summary>
        public List<SeedItem> Skills { get; set; } = [];

        /// <summary>Стартовые io formats (ТЗ пп. 7.3, v1.17): MIME-подобные коды.</summary>
        public List<SeedItem> IoFormats { get; set; } = [];
    }

    /// <summary>Роль в файле сида: id одинаков во всех языках, название — своё.</summary>
    private sealed class SeedRole
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>Навык или формат: имя — внутренний код (en, один на все языки).</summary>
    private sealed class SeedItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>Список ролей на языке lang (название: язык → en → колонка roles.name).</summary>
    public List<Role> Roles(string? lang = null)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT r.id, r.display_id, r.created_at, r.updated_at,
                   COALESCE(t.name, te.name, r.name) AS name
            FROM roles r
            LEFT JOIN role_texts t  ON t.role_id = r.id AND t.lang = @lang
            LEFT JOIN role_texts te ON te.role_id = r.id AND te.lang = 'en'
            WHERE r.deleted_at IS NULL ORDER BY name
            """, MapRole, ("@lang", Norm(lang)));
    }

    /// <summary>Список навыков на языке lang (описание: язык → en → колонка skills.description).</summary>
    public List<Skill> Skills(string? lang = null)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT s.id, s.display_id, s.name, s.is_custom, s.created_at, s.updated_at,
                   COALESCE(t.description, te.description, s.description) AS description
            FROM skills s
            LEFT JOIN skill_texts t  ON t.skill_id = s.id AND t.lang = @lang
            LEFT JOIN skill_texts te ON te.skill_id = s.id AND te.lang = 'en'
            WHERE s.deleted_at IS NULL ORDER BY s.name
            """, MapSkill, ("@lang", Norm(lang)));
    }

    /// <summary>Справочник io formats (ТЗ v1.17) — для деклараций возможностей.</summary>
    public List<IoFormat> IoFormats(string? lang = null)
    {
        using var conn = _db.Open();
        return Sql.Query(conn, null, """
            SELECT f.id, f.display_id, f.name, f.is_custom, f.created_at, f.updated_at,
                   COALESCE(t.description, te.description, f.description) AS description
            FROM io_formats f
            LEFT JOIN io_format_texts t  ON t.format_id = f.id AND t.lang = @lang
            LEFT JOIN io_format_texts te ON te.format_id = f.id AND te.lang = 'en'
            WHERE f.deleted_at IS NULL ORDER BY f.name
            """, MapIoFormat, ("@lang", Norm(lang)));
    }

    /// <summary>Язык запроса: пусто — язык установки (ТЗ гл. 9).</summary>
    private static string Norm(string? lang) =>
        string.IsNullOrWhiteSpace(lang) ? Loc.Lang : lang.Trim().ToLowerInvariant();

    /// <summary>Добавить роль (название — на языке lang, он же уходит в role_texts).</summary>
    public Role AddRole(string name, string? lang = null)
    {
        var now = DateTime.UtcNow;
        var role = new Role { Name = name.Trim(), CreatedAt = now, UpdatedAt = now };
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO roles (id, name, created_at, updated_at) VALUES (@id, @name, @created, @updated)
            ON CONFLICT(name) DO NOTHING
            """,
            ("@id", role.Id), ("@name", role.Name), ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        var id = Sql.Scalar<string>(conn, null, "SELECT id FROM roles WHERE name=@name", ("@name", role.Name));
        if (id is { Length: > 0 })
        {
            role.Id = id;
            SetRoleText(conn, null, id, Norm(lang), role.Name, overwrite: true);
        }
        return role;
    }

    public Skill AddSkill(string name, string description, bool isCustom = true, string? lang = null)
    {
        var now = DateTime.UtcNow;
        var skill = new Skill
        {
            Name = name.Trim(), Description = description, IsCustom = isCustom,
            CreatedAt = now, UpdatedAt = now,
        };
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO skills (id, name, description, is_custom, created_at, updated_at)
            VALUES (@id, @name, @descr, @custom, @created, @updated)
            ON CONFLICT(name) DO NOTHING
            """,
            ("@id", skill.Id), ("@name", skill.Name), ("@descr", skill.Description),
            ("@custom", skill.IsCustom ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        var id = Sql.Scalar<string>(conn, null, "SELECT id FROM skills WHERE name=@name", ("@name", skill.Name));
        if (id is { Length: > 0 })
        {
            skill.Id = id;
            SetItemText(conn, null, "skill_texts", "skill_id", id, Norm(lang),
                skill.Description, overwrite: true);
        }
        return skill;
    }

    /// <summary>Добавить io format в справочник (ТЗ v1.17); имя (код) уникально.</summary>
    public IoFormat AddIoFormat(string name, string description, bool isCustom = true, string? lang = null)
    {
        if (name.Trim().Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.refData.1"));
        }
        var now = DateTime.UtcNow;
        var format = new IoFormat
        {
            Name = name.Trim(), Description = description, IsCustom = isCustom,
            CreatedAt = now, UpdatedAt = now,
        };
        using var conn = _db.Open();
        Sql.Exec(conn, null, """
            INSERT INTO io_formats (id, name, description, is_custom, created_at, updated_at)
            VALUES (@id, @name, @descr, @custom, @created, @updated)
            ON CONFLICT(name) DO NOTHING
            """,
            ("@id", format.Id), ("@name", format.Name), ("@descr", format.Description),
            ("@custom", format.IsCustom ? 1 : 0),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
        var id = Sql.Scalar<string>(conn, null, "SELECT id FROM io_formats WHERE name=@name",
            ("@name", format.Name));
        if (id is { Length: > 0 })
        {
            format.Id = id;
            SetItemText(conn, null, "io_format_texts", "format_id", id, Norm(lang),
                format.Description, overwrite: true);
        }
        return format;
    }

    /// <summary>
    /// Стартовое наполнение справочников из файлов дистрибутива (идемпотентно, T-191):
    /// названия ролей, навыков и форматов кладутся СРАЗУ НА ВСЕ языки, для которых есть файл
    /// сида. Тексты переписываются только при росте seedVersion — правка пользователя между
    /// версиями не затирается. На уже наполненной БД записи не задваиваются: роль ищется по
    /// названию на любом языке сида, навык и формат — по коду (он один на все языки), и
    /// тексты привязываются к СУЩЕСТВУЮЩЕЙ строке, поэтому ссылки задач и команд целы.
    /// </summary>
    public void Seed()
    {
        var files = LoadSeedFiles();
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                Loc.T("msg.refData.2", Path.Combine(_seedDir, SeedFilePrefix + "ru.json")));
        }
        var version = files.Values.Max(f => f.SeedVersion);
        using var conn = _db.Open();
        var applied = long.TryParse(
            Sql.Scalar<string>(conn, null, "SELECT value FROM meta WHERE key=@k", ("@k", SeedVersionKey)),
            out var parsed) ? parsed : 0;
        var refresh = version > applied;

        // одной транзакцией: строк со сроками по языкам втрое больше прежнего (89 навыков +
        // 12 форматов + 6 ролей на каждый язык), а сеяние идёт при КАЖДОМ старте организации
        using var tx = conn.BeginTransaction();
        SeedRoles(conn, tx, files, refresh);
        SeedSkills(conn, tx, files, refresh);
        SeedIoFormats(conn, tx, files, refresh);

        Sql.Exec(conn, tx, "INSERT OR REPLACE INTO meta(key, value) VALUES (@k, @v)",
            ("@k", SeedVersionKey), ("@v", version.ToString()));
        tx.Commit();
    }

    private void SeedRoles(SqliteConnection conn, SqliteTransaction tx,
        Dictionary<string, SeedFile> files, bool refresh)
    {
        // роли не сеются поверх непустого справочника (как было до T-191): пользователь мог
        // завести свой набор — но тексты уже существующим ролям проставляются всегда
        var empty = Sql.Scalar<long>(conn, tx, "SELECT COUNT(*) FROM roles") == 0;
        var byLang = files.ToDictionary(kv => kv.Key, kv => kv.Value.Roles);
        var ids = byLang.Values.SelectMany(r => r.Select(x => x.Id)).Distinct().ToList();
        var now = Sql.ToDb(DateTime.UtcNow);
        foreach (var id in ids)
        {
            var names = byLang
                .Select(kv => (Lang: kv.Key, Role: kv.Value.FirstOrDefault(r => r.Id == id)))
                .Where(x => x.Role is not null)
                .ToList();
            var baseName = names[0].Role!.Name;
            // строка роли: своя (по id) либо заведённая раньше — по названию на любом языке
            var roleId = Sql.Scalar<string>(conn, tx,
                "SELECT id FROM roles WHERE id=@id AND deleted_at IS NULL", ("@id", id));
            foreach (var (_, role) in names)
            {
                roleId ??= Sql.Scalar<string>(conn, tx,
                    "SELECT id FROM roles WHERE name=@name AND deleted_at IS NULL", ("@name", role!.Name));
            }
            if (roleId is null)
            {
                if (!empty)
                {
                    continue;
                }
                roleId = id;
                Sql.Exec(conn, tx, """
                    INSERT INTO roles (id, name, created_at, updated_at)
                    VALUES (@id, @name, @now, @now) ON CONFLICT(name) DO NOTHING
                    """, ("@id", id), ("@name", baseName), ("@now", now));
            }
            foreach (var (lang, role) in names)
            {
                SetRoleText(conn, tx, roleId, lang, role!.Name, refresh);
            }
        }
    }

    private void SeedSkills(SqliteConnection conn, SqliteTransaction tx,
        Dictionary<string, SeedFile> files, bool refresh)
    {
        // БД со старыми кодами навыков (ТЗ v1.39, todo35_2): переименование по месту —
        // id сохраняется, ссылки задач (task_skills) не рвутся; если новый код уже
        // занят (например, добавлен пользователем), старая запись не трогается
        foreach (var (oldName, newName) in SkillRenames)
        {
            Sql.Exec(conn, tx, """
                UPDATE skills SET name=@new, updated_at=@now
                WHERE name=@old AND deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM skills s2 WHERE s2.name=@new AND s2.deleted_at IS NULL)
                """,
                ("@new", newName), ("@old", oldName), ("@now", Sql.ToDb(DateTime.UtcNow)));
        }
        foreach (var (name, byLang) in SkillsByCode(files))
        {
            var id = EnsureRow(conn, tx, "skills", name, byLang);
            foreach (var (lang, description) in byLang)
            {
                SetItemText(conn, tx, "skill_texts", "skill_id", id, lang, description, refresh);
            }
            // БД со старой схемой: у стартовых skills признак «кастом» снимается (ТЗ v1.17)
            Sql.Exec(conn, tx, "UPDATE skills SET is_custom=0 WHERE id=@id", ("@id", id));
        }
    }

    private void SeedIoFormats(SqliteConnection conn, SqliteTransaction tx,
        Dictionary<string, SeedFile> files, bool refresh)
    {
        var codes = files.Values.SelectMany(f => f.IoFormats.Select(x => x.Name)).Distinct();
        foreach (var name in codes)
        {
            var byLang = files
                .Where(kv => kv.Value.IoFormats.Any(x => x.Name == name))
                .ToDictionary(kv => kv.Key,
                    kv => kv.Value.IoFormats.First(x => x.Name == name).Description);
            var id = EnsureRow(conn, tx, "io_formats", name, byLang);
            foreach (var (lang, description) in byLang)
            {
                SetItemText(conn, tx, "io_format_texts", "format_id", id, lang, description, refresh);
            }
            Sql.Exec(conn, tx, "UPDATE io_formats SET is_custom=0 WHERE id=@id", ("@id", id));
        }
    }

    /// <summary>Полный стартовый набор навыков (ТЗ v1.39, todo35_2): базовые code-навыки
    /// + языковые варианты по 12 языкам + остальные категории. Итого 89. Код навыка один
    /// на все языки, описание — своё на каждый.</summary>
    private static List<(string Name, Dictionary<string, string> ByLang)> SkillsByCode(
        Dictionary<string, SeedFile> files)
    {
        var order = new List<string>();
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        void Put(string name, string lang, string description)
        {
            if (!texts.TryGetValue(name, out var byLang))
            {
                texts[name] = byLang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                order.Add(name);
            }
            byLang[lang] = description;
        }

        foreach (var (lang, file) in files)
        {
            foreach (var skill in file.CodeSkills)
            {
                Put(skill.Name, lang, skill.Description);
                foreach (var (ext, codeLanguage) in CodeLanguages)
                {
                    Put($"{skill.Name}-{ext}", lang, $"{skill.Description} ({codeLanguage})");
                }
            }
            foreach (var skill in file.Skills)
            {
                Put(skill.Name, lang, skill.Description);
            }
        }
        return [.. order.Select(name => (name, texts[name]))];
    }

    /// <summary>Строка справочника по коду: заводится, если её нет; возвращается её id
    /// (у существующей — прежний, поэтому ссылки задач и деклараций целы).</summary>
    private static string EnsureRow(SqliteConnection conn, SqliteTransaction tx, string table,
        string name, Dictionary<string, string> byLang)
    {
        var id = Sql.Scalar<string>(conn, tx, $"SELECT id FROM {table} WHERE name=@name",
            ("@name", name));
        if (id is { Length: > 0 })
        {
            return id;
        }
        id = Guid.NewGuid().ToString();
        var now = Sql.ToDb(DateTime.UtcNow);
        // description — «запасное» описание строки: его показывают, если текстов языка нет
        var fallback = byLang.TryGetValue(LocCatalog.BaseLanguage, out var basic)
            ? basic : byLang.Values.First();
        Sql.Exec(conn, tx, $"""
            INSERT INTO {table} (id, name, description, is_custom, created_at, updated_at)
            VALUES (@id, @name, @descr, 0, @now, @now) ON CONFLICT(name) DO NOTHING
            """, ("@id", id), ("@name", name), ("@descr", fallback), ("@now", now));
        return Sql.Scalar<string>(conn, tx, $"SELECT id FROM {table} WHERE name=@name", ("@name", name))
               ?? id;
    }

    private static void SetRoleText(SqliteConnection conn, SqliteTransaction? tx, string roleId,
        string lang, string name, bool overwrite)
    {
        Sql.Exec(conn, tx, overwrite
            ? """
              INSERT INTO role_texts (role_id, lang, name) VALUES (@id, @lang, @text)
              ON CONFLICT(role_id, lang) DO UPDATE SET name=@text
              """
            : """
              INSERT INTO role_texts (role_id, lang, name) VALUES (@id, @lang, @text)
              ON CONFLICT(role_id, lang) DO NOTHING
              """,
            ("@id", roleId), ("@lang", lang), ("@text", name));
    }

    private static void SetItemText(SqliteConnection conn, SqliteTransaction? tx, string table,
        string idColumn, string rowId, string lang, string description, bool overwrite)
    {
        Sql.Exec(conn, tx, overwrite
            ? $"""
               INSERT INTO {table} ({idColumn}, lang, description) VALUES (@id, @lang, @text)
               ON CONFLICT({idColumn}, lang) DO UPDATE SET description=@text
               """
            : $"""
               INSERT INTO {table} ({idColumn}, lang, description) VALUES (@id, @lang, @text)
               ON CONFLICT({idColumn}, lang) DO NOTHING
               """,
            ("@id", rowId), ("@lang", lang), ("@text", description));
    }

    /// <summary>Файлы сида по языкам: RefDataService_ru.json → "ru" (T-191).
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
                throw new InvalidOperationException(Loc.T("msg.refData.3", path, ex.Message));
            }
            if (file is null || (file.Roles.Count == 0 && file.CodeSkills.Count == 0
                                 && file.Skills.Count == 0 && file.IoFormats.Count == 0))
            {
                continue;
            }
            result[lang] = file;
        }
        return result;
    }

    /// <summary>
    /// Миграция кодов навыков в декларациях возможностей (ТЗ v1.39, todo35_2): во всех
    /// models/scope_*.json и executors/scope_*.json старые коды (SkillRenames) заменяются
    /// новыми. Замена — только точных строк в кавычках, файл переписывается лишь при
    /// изменении; идемпотентно, вызывается при каждом старте. Сид-файлы дистрибутива
    /// и так перезаписываются новой версией сида — для них это no-op.
    /// </summary>
    public static void MigrateSkillCodes(FileStore files)
    {
        foreach (var dir in new[] { "models", "executors" })
        {
            var abs = files.Abs(dir);
            if (!Directory.Exists(abs))
            {
                continue;
            }
            foreach (var path in Directory.GetFiles(abs, "scope_*.json"))
            {
                var text = File.ReadAllText(path);
                var updated = text;
                foreach (var (oldName, newName) in SkillRenames)
                {
                    updated = updated.Replace($"\"{oldName}\"", $"\"{newName}\"");
                }
                if (updated != text)
                {
                    files.WriteText($"{dir}/{Path.GetFileName(path)}", updated);
                }
            }
        }
    }

    private static Role MapRole(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
    };

    private static Skill MapSkill(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Description = r.S("description"),
        IsCustom = r.B("is_custom"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
    };

    private static IoFormat MapIoFormat(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        Description = r.S("description"),
        IsCustom = r.B("is_custom"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
    };
}
