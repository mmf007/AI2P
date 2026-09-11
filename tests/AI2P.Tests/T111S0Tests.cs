using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server;
using AI2P.Storage;
using AI2P.Storage.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-111-S0 — ФУНДАМЕНТ ВЕТКИ ПЛАГИНОВ: запись плагина уровня организации, медиатека
/// проекта представлением над объектами, пер-серверная часть в config.json и снятый вид
/// объекта «mcp».
///
/// Четыре вещи, ради которых проверки и написаны:
/// <list type="number">
/// <item>миграция схемы идемпотентна — база любой давности поднимается до v45 за один старт,
/// и повторный старт ничего не ломает;</item>
/// <item>запись плагина РЕПЛИЦИРУЕТСЯ (таблица в <c>ChangeLog.OrgTables</c>, строка попадает
/// в журнал изменений триггером);</item>
/// <item>путь к софту в базу НЕ ПОПАДАЕТ — ни колонкой, ни значением: он пер-серверный
/// и живёт в config.json;</item>
/// <item>вида объекта <c>mcp</c> в перечне больше нет, а колонки таблицы <c>objects</c>
/// остались на месте.</item>
/// </list>
/// </summary>
public sealed class T111S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t111s0-" + Guid.NewGuid().ToString("N"));

    private PluginService Plugins => new(_f.Db, _f.Events);

    public void Dispose()
    {
        _f.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    // ---------- 1. схема ----------

    /// <summary>Схема организации поднята на ОДИН шаг и завела таблицу плагинов.</summary>
    [Fact]
    public void The_Org_Schema_Is_V45_And_Has_The_Plugins_Table()
    {
        Assert.Equal("46", _f.Db.Meta("schema_version"));
        using var conn = _f.Db.Open();
        var tables = Sql.Query(conn, null,
            "SELECT name FROM sqlite_master WHERE type='table'", r => r.S("name"));
        Assert.Contains("plugins", tables);
    }

    /// <summary>
    /// МИГРАЦИЯ ИДЕМПОТЕНТНА: база прежней версии (v44, без таблицы плагинов) поднимается
    /// одним стартом, данные остаются на месте, а повторный старт ничего не меняет —
    /// миграции безусловны и на записанный в meta номер не смотрят.
    /// </summary>
    [Fact]
    public void The_Migration_Is_Idempotent()
    {
        Directory.CreateDirectory(_dir);
        var db = new Database(_dir, "old.db");
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(_dir, "old.db")};Pooling=false"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta(key, value) VALUES ('schema_version', '44');
                CREATE TABLE projects (
                  id TEXT PRIMARY KEY, display_id TEXT NOT NULL DEFAULT '',
                  name TEXT NOT NULL DEFAULT '',
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                INSERT INTO projects(id, display_id, name, created_at, updated_at)
                VALUES ('p1', 'PRJ-1', 'проект прошлой версии', '2020-01-01', '2020-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        db.Init();
        Assert.Equal("46", db.Meta("schema_version"));
        using (var check = db.Open())
        {
            var tables = Sql.Query(check, null,
                "SELECT name FROM sqlite_master WHERE type='table'", r => r.S("name"));
            Assert.Contains("plugins", tables);
            Assert.Equal("проект прошлой версии", Sql.Scalar<string>(check, null,
                "SELECT name FROM projects WHERE id='p1'"));
        }

        // второй старт: номер тот же, таблица одна, данные целы
        db.Init();
        Assert.Equal("46", db.Meta("schema_version"));
        using var again = db.Open();
        Assert.Equal(1, Sql.Scalar<long>(again, null,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='plugins'"));
        Assert.Equal("проект прошлой версии", Sql.Scalar<string>(again, null,
            "SELECT name FROM projects WHERE id='p1'"));
    }

    // ---------- 2. запись плагина ----------

    /// <summary>Запись заводится по манифесту, получает номер PLG-N и умолчания настроек.</summary>
    [Fact]
    public void A_Plugin_Is_Declared_From_Its_Manifest()
    {
        var manifest = PluginManifest.Parse(GatewayJson);
        Assert.NotNull(manifest);
        var plugin = Plugins.Declare(manifest!, null, "ru");

        Assert.Equal("editor.shotcut", plugin.Code);
        Assert.Equal(PluginKinds.Gateway, plugin.Kind);
        Assert.Equal("Shotcut / Kdenlive", plugin.Name);
        Assert.Equal(PluginStates.Declared, plugin.State);
        Assert.StartsWith("PLG-", plugin.DisplayId);
        Assert.Contains("\"fps\":25", plugin.SettingsJson);

        // повторное объявление тем же манифестом ВТОРОЙ записи не заводит: манифест читает
        // каждый сервер при старте, а строка организации одна на всех
        var again = Plugins.Declare(manifest!, null, "ru");
        Assert.Equal(plugin.Id, again.Id);
        Assert.Single(Plugins.List());
    }

    /// <summary>Код плагина уникален в организации.</summary>
    [Fact]
    public void The_Plugin_Code_Is_Unique()
    {
        Plugins.Create(new PluginRecord { Code = "conv.ffmpeg", Name = "ffmpeg" }, null);
        Assert.Throws<InvalidOperationException>(() =>
            Plugins.Create(new PluginRecord { Code = "conv.ffmpeg", Name = "второй" }, null));
    }

    /// <summary>Состояний четыре, и меняются они отдельным действием — настройки при этом
    /// остаются на месте (снятие плагина не должно заставлять вводить их заново).</summary>
    [Fact]
    public void The_State_Changes_Without_Touching_The_Settings()
    {
        var plugin = Plugins.Create(new PluginRecord
        {
            Code = "conv.ffmpeg",
            Name = "ffmpeg",
            SettingsJson = "{\"crf\":18}",
        }, null);

        foreach (var state in PluginStates.All)
        {
            var changed = Plugins.SetState(plugin.Id, state, null);
            Assert.Equal(state, changed.State);
            Assert.Equal("{\"crf\":18}", changed.SettingsJson);
        }
        Assert.Contains(PluginStates.Removed, PluginStates.All);
        Assert.Throws<ArgumentException>(() => Plugins.SetState(plugin.Id, "неизвестно", null));
    }

    /// <summary>Правкой не меняются код и вид: по коду живут каталог манифеста, раздел
    /// config.json и коды зарегистрированных действий.</summary>
    [Fact]
    public void The_Code_And_The_Kind_Are_Not_Changed_By_An_Update()
    {
        var plugin = Plugins.Create(new PluginRecord { Code = "conv.ffmpeg", Name = "ffmpeg" }, null);
        plugin.Code = "conv.other";
        plugin.Kind = PluginKinds.Mcp;
        plugin.Name = "новое имя";
        var saved = Plugins.Update(plugin, null);

        Assert.Equal("conv.ffmpeg", saved.Code);
        Assert.Equal(PluginKinds.Gateway, saved.Kind);
        Assert.Equal("новое имя", saved.Name);
        Assert.NotNull(Plugins.ByCode("conv.ffmpeg"));
    }

    // ---------- 3. репликация ----------

    /// <summary>Запись плагина реплицируется: таблица в списке журнала, и строка в него
    /// действительно попадает — триггером, а не кодом сервиса.</summary>
    [Fact]
    public void A_Plugin_Record_Replicates()
    {
        Assert.Contains("plugins", ChangeLog.OrgTables);

        var plugin = Plugins.Create(new PluginRecord { Code = "editor.blender", Name = "Blender" }, null);
        using var conn = _f.Db.Open();
        Assert.True(Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM changes WHERE tbl='plugins' AND pk=@id", ("@id", plugin.Id)) > 0);
    }

    /// <summary>
    /// ПУТЬ К СОФТУ В БАЗУ НЕ ПОПАДАЕТ — ни колонкой, ни значением. Установка плагина всегда
    /// локальная, а реплицированный чужой путь ломает установку у соседа и «уже не уходит»
    /// (наука T-164). Место таких значений — config.json сервера.
    /// </summary>
    [Fact]
    public void The_Software_Path_Never_Reaches_The_Database()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(plugins)", r => r.S("name"));
        foreach (var forbidden in new[] { "path", "exe_path", "installed", "install_path", "server_id" })
        {
            Assert.DoesNotContain(forbidden, columns);
        }

        // ... и ни одно поле записи не принимает путь: он живёт в разделе config.json
        var plugin = Plugins.Create(new PluginRecord
        {
            Code = "editor.shotcut",
            Name = "Shotcut",
            SettingsJson = "{}",
        }, null);
        var row = Sql.Query(conn, null,
            "SELECT * FROM plugins WHERE id=@id", r => string.Join("|",
                Enumerable.Range(0, r.FieldCount).Select(i => r.GetValue(i)?.ToString() ?? "")),
            ("@id", plugin.Id)).Single();
        Assert.DoesNotContain(":\\", row);
        Assert.DoesNotContain("melt", row);

        var config = new Ai2pConfig();
        config.Plugins["editor.shotcut"] = new Ai2pConfig.PluginSettings
        {
            Installed = true,
            Path = @"C:\Shotcut\melt.exe",
            CheckedAt = DateTime.UtcNow,
        };
        Assert.True(config.Plugins["editor.shotcut"].Installed);
        Assert.Equal(@"C:\Shotcut\melt.exe", config.Plugins["editor.shotcut"].Path);
    }

    /// <summary>В поставляемом config.json раздела плагинов нет вовсе: файл один на все
    /// системы, а обновление кладёт значения пользователя поверх новых умолчаний, и раз
    /// попавший в файл чужой путь уже не уходит (наука T-164).</summary>
    [Fact]
    public void The_Shipped_Config_Has_No_Plugin_Paths()
    {
        Assert.Empty(new Ai2pConfig().Plugins);
    }

    // ---------- 4. манифест ----------

    /// <summary>Манифест шлюза: софт, действия, опыт, настройки, документы.</summary>
    [Fact]
    public void A_Gateway_Manifest_Is_Parsed()
    {
        var manifest = PluginManifest.Parse(GatewayJson);
        Assert.NotNull(manifest);
        Assert.Equal("editor.shotcut", manifest!.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);
        Assert.Equal("Shotcut / Kdenlive", manifest.Name.Text("ru"));
        Assert.Equal("Shotcut / Kdenlive (MLT XML)", manifest.Name.Text("en"));

        Assert.NotNull(manifest.Software);
        Assert.Equal("shotcut", manifest.Software!.Package);
        Assert.Equal("meltPath", manifest.Software.PathKey);
        Assert.Contains("melt", manifest.Software.System!.Commands);

        var render = manifest.Actions.Single(a => a.Tool == "shotcut_render");
        Assert.Equal("AI2P.Plugins.Shotcut.Render", render.Code);
        Assert.True(render.NeedsSoftware);
        Assert.Equal(2, manifest.Actions.Count);

        Assert.Equal("video-edit", Assert.Single(manifest.Experience).Skill);
        Assert.Equal(PluginSetting.TypeNumber, Assert.Single(manifest.Settings).Type);
        Assert.Equal("plugins/editor.shotcut/doc.ru.md", manifest.Doc["ru"]);

        // у шлюза блока подключения нет — даже если его вписали руками
        Assert.Null(manifest.Connection);
    }

    /// <summary>Манифест подключения MCP: вместо действий — блок связи, а токена в файле
    /// нет, только ссылка на секрет.</summary>
    [Fact]
    public void An_Mcp_Manifest_Carries_A_Connection_And_No_Secret()
    {
        var manifest = PluginManifest.Parse(McpJson);
        Assert.NotNull(manifest);
        Assert.Equal(PluginKinds.Mcp, manifest!.Kind);
        Assert.NotNull(manifest.Connection);
        Assert.Equal(PluginConnection.Stdio, manifest.Connection!.Transport);
        Assert.Equal("npx", manifest.Connection.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem"], manifest.Connection.Args);
        Assert.Equal("plugin.mcp.files.token", manifest.Connection.SecretRef);
        Assert.True(manifest.Connection.IsComplete);
        // список инструментов сервер MCP называет сам при инициализации — в файле его нет
        Assert.Empty(manifest.Actions);
        Assert.DoesNotContain("secret\":", McpJson.Replace("secretRef", "ssss"));
    }

    /// <summary>Негодный манифест не разбирается: без кода, с неизвестным видом, у mcp без
    /// блока связи, да и просто не JSON. Разбор при этом не падает.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("не json вовсе")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"kind\": \"gateway\"}")]
    [InlineData("{\"code\": \"x\", \"kind\": \"неизвестно\"}")]
    [InlineData("{\"code\": \"x\", \"kind\": \"mcp\"}")]
    public void A_Broken_Manifest_Is_Rejected(string json) =>
        Assert.Null(PluginManifest.Parse(json));

    /// <summary>Манифесты читаются с диска: <c>plugins/&lt;код&gt;/plugin.json</c> каталога
    /// данных. Сломанный файл не прячет остальные плагины.</summary>
    [Fact]
    public void Manifests_Are_Read_From_The_Data_Directory()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "plugins", "editor.shotcut"));
        Directory.CreateDirectory(Path.Combine(_dir, "plugins", "mcp.files"));
        Directory.CreateDirectory(Path.Combine(_dir, "plugins", "сломанный"));
        File.WriteAllText(Path.Combine(_dir, "plugins", "editor.shotcut", "plugin.json"), GatewayJson);
        File.WriteAllText(Path.Combine(_dir, "plugins", "mcp.files", "plugin.json"), McpJson);
        File.WriteAllText(Path.Combine(_dir, "plugins", "сломанный", "plugin.json"), "{ мусор");

        var all = PluginManifest.ReadAll(_dir);
        Assert.Equal(["editor.shotcut", "mcp.files"], all.Select(m => m.Code));
        Assert.Equal("plugins/editor.shotcut/plugin.json", PluginManifest.PathOf("editor.shotcut"));
        Assert.Null(PluginManifest.Read(_dir, "нет-такого"));
    }

    // ---------- 5. медиатека ----------

    /// <summary>Медиатека — ПРЕДСТАВЛЕНИЕ над объектами проекта: свой вид объекта, отбор
    /// по нему, никакого второго списка файлов.</summary>
    [Fact]
    public void The_Media_Library_Is_A_View_Over_The_Project_Objects()
    {
        var project = _f.Projects.Create("Ролик", null, null, null);
        var hero = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "Вася",
            Type = ObjectKinds.Character,
        }, null);
        var take = _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "Сцена 3, дубль 2",
            Type = ObjectKinds.Media,
            PathOrUrl = "store:projects/PRJ-1/media/scene3-take2.mp4",
            MetaJson = new MediaMeta
            {
                Kind = MediaMeta.Video,
                Scene = "3",
                Take = 2,
                DurationSec = 12.5,
                SourceTask = "T-15",
                SourcePlugin = "editor.shotcut",
            }.Write("{}"),
        }, null);

        var media = _f.Objects.List(project.Id, kinds: [ObjectKinds.Media]);
        Assert.Equal(take.Id, Assert.Single(media).Id);
        Assert.DoesNotContain(media, o => o.Id == hero.Id);

        var meta = MediaMeta.Parse(_f.Objects.Get(take.Id)!.MetaJson);
        Assert.Equal(MediaMeta.Video, meta.Kind);
        Assert.Equal("3", meta.Scene);
        Assert.Equal(2, meta.Take);
        Assert.Equal(12.5, meta.DurationSec);
        Assert.Equal("T-15", meta.SourceTask);
        Assert.Equal("editor.shotcut", meta.SourcePlugin);
    }

    /// <summary>
    /// ПУТЬ МЕДИАРЕСУРСА ТОЛЬКО ОТНОСИТЕЛЬНЫЙ: папки проекта либо каталога данных
    /// (<c>store:</c>). Абсолютный путь убивает переносимость по кластеру, и отказ стоит
    /// в сервисе объектов — мимо формы его не обойти.
    /// </summary>
    [Fact]
    public void A_Media_Path_Must_Be_Relative()
    {
        Assert.True(MediaLibrary.IsValidPath("render/scene3.mp4"));
        Assert.True(MediaLibrary.IsValidPath("store:projects/PRJ-1/media/scene3.mp4"));
        Assert.False(MediaLibrary.IsValidPath(""));
        Assert.False(MediaLibrary.IsValidPath(@"C:\video\scene3.mp4"));
        Assert.False(MediaLibrary.IsValidPath("/var/video/scene3.mp4"));
        Assert.False(MediaLibrary.IsValidPath("../соседний/scene3.mp4"));
        Assert.False(MediaLibrary.IsValidPath("https://example.com/scene3.mp4"));

        var project = _f.Projects.Create("Ролик", null, null, null);
        Assert.Throws<ArgumentException>(() => _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "чужой диск",
            Type = ObjectKinds.Media,
            PathOrUrl = @"C:\video\scene3.mp4",
        }, null));

        // а обычному объекту абсолютный путь по-прежнему не запрещён: правило про медиатеку
        Assert.NotNull(_f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "оборудование",
            Type = ObjectKinds.Hardware,
            PathOrUrl = @"C:\camera",
        }, null));
    }

    /// <summary>Метаданные ложатся в <c>meta_json</c> рядом с чужими ключами и их не теряют:
    /// там же живёт <c>loraStrength</c> (T-14-S1).</summary>
    [Fact]
    public void Media_Meta_Keeps_The_Other_Keys_Of_Meta_Json()
    {
        var json = new MediaMeta { Kind = MediaMeta.Audio, Caption = "дорожка" }
            .Write("{\"loraStrength\": 0.8}");
        Assert.Contains("loraStrength", json);
        Assert.Equal(MediaMeta.Audio, MediaMeta.Parse(json).Kind);
        Assert.Equal("дорожка", MediaMeta.Parse(json).Caption);

        // мусор в метаданных не роняет разбор — список медиатеки обязан открыться
        Assert.Equal(MediaMeta.Video, MediaMeta.Parse("{ сломано").Kind);
        Assert.Equal(MediaMeta.Video, MediaMeta.Parse("{}").Kind);
    }

    /// <summary>Файл, который сделала САМА ПРОГРАММА, ложится в каталог данных организации:
    /// папка проекта пер-серверная и не реплицируется (T-98-S0).</summary>
    [Fact]
    public void What_The_Program_Makes_Goes_To_The_Org_Store()
    {
        var path = MediaLibrary.StorePath("PRJ-1", "scene3-take2.mp4");
        Assert.Equal("store:projects/PRJ-1/media/scene3-take2.mp4", path);
        Assert.True(ObjectFiles.IsStore(path));
        Assert.True(MediaLibrary.IsValidPath(path));
    }

    // ---------- 6. снятый вид «mcp» ----------

    /// <summary>Вида объекта <c>mcp</c> в перечне нет, а <c>media</c> есть.</summary>
    [Fact]
    public void The_Object_Kind_Mcp_Is_Gone()
    {
        Assert.DoesNotContain("mcp", ObjectKinds.All);
        Assert.False(ObjectKinds.IsKnown("mcp"));
        Assert.Contains(ObjectKinds.Media, ObjectKinds.All);
        Assert.True(ObjectKinds.IsKnown(ObjectKinds.Media));
        Assert.Empty(ObjectKinds.Parse("mcp"));

        var project = _f.Projects.Create("Ролик", null, null, null);
        Assert.Throws<ArgumentException>(() => _f.Objects.Create(new ObjectItem
        {
            ProjectId = project.Id,
            Name = "сервер MCP",
            Type = "mcp",
        }, null));
    }

    /// <summary>А колонки таблицы <c>objects</c> ОСТАВЛЕНЫ: удаление колонки в SQLite это
    /// пересоздание таблицы с переносом данных и правкой репликации ради четырёх пустых
    /// полей. Они просто перестают заполняться.</summary>
    [Fact]
    public void The_Object_Columns_Of_The_Old_Kind_Are_Kept()
    {
        using var conn = _f.Db.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(objects)", r => r.S("name"));
        foreach (var column in new[] { "transport", "command_or_url", "tools_cache_json", "rules_json" })
        {
            Assert.Contains(column, columns);
        }
    }

    /// <summary>Все виды объектов и все состояния плагинов названы в словарях интерфейса:
    /// вид без строки показался бы человеку своим кодом.</summary>
    [Fact]
    public void Every_Kind_And_State_Has_A_Title()
    {
        foreach (var kind in ObjectKinds.All)
        {
            Assert.NotEqual(kind, ObjectKinds.Title(kind, "ru"));
        }
        foreach (var kind in PluginKinds.All)
        {
            Assert.NotEqual(kind, PluginKinds.Title(kind, "ru"));
        }
        foreach (var state in PluginStates.All)
        {
            Assert.NotEqual(state, PluginStates.Title(state, "ru"));
        }
    }

    // ---------- образцы манифестов ----------

    private const string GatewayJson = """
        {
          "code": "editor.shotcut",
          "kind": "gateway",
          "version": 1,
          "name": { "ru": "Shotcut / Kdenlive", "en": "Shotcut / Kdenlive (MLT XML)" },
          "description": { "ru": "Сборка монтажного листа MLT и рендер через melt" },
          "software": {
            "package": "shotcut",
            "required": false,
            "pathKey": "meltPath",
            "system": { "commands": ["melt", "qmelt"], "versionArgs": "--version", "minVersion": "7.0" },
            "hint": { "ru": "Поставьте Shotcut и укажите путь к melt" }
          },
          "actions": [
            { "code": "AI2P.Plugins.Shotcut.Export", "tool": "shotcut_export",
              "title": { "ru": "Собрать монтажный лист" } },
            { "code": "AI2P.Plugins.Shotcut.Render", "tool": "shotcut_render",
              "title": { "ru": "Отрендерить" }, "needsSoftware": true }
          ],
          "experience": [
            { "skill": "video-edit", "text": { "ru": "Дорожки в MLT нумеруются с нуля" } }
          ],
          "settings": [
            { "key": "fps", "type": "number", "default": 25, "title": { "ru": "Кадров в секунду" } }
          ],
          "doc": { "ru": "plugins/editor.shotcut/doc.ru.md", "en": "plugins/editor.shotcut/doc.en.md" }
        }
        """;

    private const string McpJson = """
        {
          "code": "mcp.files",
          "kind": "mcp",
          "version": 1,
          "name": "MCP filesystem",
          "connection": {
            "transport": "stdio",
            "command": "npx",
            "args": ["-y", "@modelcontextprotocol/server-filesystem"],
            "secretRef": "plugin.mcp.files.token"
          },
          "actions": [
            { "code": "AI2P.Plugins.Mcp.Files.Read", "tool": "mcp_files_read" }
          ]
        }
        """;
}
