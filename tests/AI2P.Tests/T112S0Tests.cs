using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-112-S0 (ветка «Плагины и MCP»): ЯДРО ПЛАГИНОВ — жизненный цикл, регистрация действий,
/// безопасность, опыт, поиск софта.
///
/// Главное, ради чего этот набор написан, — ОДНА строка политики безопасности. Инструмент
/// агента, у которого НЕТ записи в справочнике действий, правилами не закрывается вовсе
/// (наука 2d3af8da): <c>CodeByTool</c> возвращает null, и <c>Authorize</c> вызов РАЗРЕШАЕТ.
/// Для инструментов, пришедших от плагина, это ровно наоборот опасно: список инструментов у
/// сервера MCP динамический, и первый же сервер, добавивший инструмент в новой версии,
/// протащил бы его мимо правил молча. Поэтому у плагинных инструментов политика обратная:
/// нет записи — ЗАПРЕЩЕНО. Контрольный опыт (обычный инструмент без записи разрешён) стоит
/// здесь же: без него проверка доказывала бы только то, что запрещено всё подряд.
/// </summary>
public sealed class T112S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private PluginSetupService Setup => new(_f.Db, _f.Experience, _f.RefData);

    private static JsonElement NoArgs => JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Манифест шлюза с двумя действиями и записью опыта — как у Shotcut.</summary>
    private static PluginManifest Gateway(string code = "editor.shotcut") =>
        PluginManifest.Parse($$"""
            {
              "code": "{{code}}",
              "kind": "gateway",
              "name": "Shotcut",
              "software": {
                "package": "shotcut",
                "pathKey": "meltPath",
                "system": { "commands": ["melt"], "versionArgs": "--version", "minVersion": "7.0" }
              },
              "actions": [
                { "code": "AI2P.Plugins.Shotcut.Export", "tool": "shotcut_export",
                  "title": "Собрать проект", "description": "Собирает .mlt из медиатеки" },
                { "code": "AI2P.Plugins.Shotcut.Render", "tool": "shotcut_render",
                  "title": "Отрендерить", "needsSoftware": true }
              ],
              "experience": [
                { "skill": "video-edit", "text": "пути в resource — только относительно папки проекта" }
              ]
            }
            """)!;

    private static PluginManifest Mcp(string code = "mcp.demo") =>
        PluginManifest.Parse($$"""
            {
              "code": "{{code}}",
              "kind": "mcp",
              "connection": { "transport": "stdio", "command": "node" },
              "experience": [ { "skill": "video-edit", "text": "сервер MCP отвечает не мгновенно" } ]
            }
            """)!;

    // ---------- 1. жизненный цикл: пять состояний ----------

    [Fact]
    public void The_Lifecycle_Has_Five_States_And_Software_Is_Counted_Per_Server()
    {
        Assert.Equal(5, PluginLifecycle.All.Length);
        Assert.Equal(
            [PluginStates.Declared, PluginLifecycle.ProbingSoftware, PluginStates.Initialized,
             PluginStates.Disabled, PluginStates.Removed],
            PluginLifecycle.All);

        // решение организации главнее: выключенный и снятый показываются как есть
        Assert.Equal(PluginStates.Disabled,
            PluginLifecycle.Shown(PluginStates.Disabled, PluginSoftwareStatus.Found));
        Assert.Equal(PluginStates.Removed,
            PluginLifecycle.Shown(PluginStates.Removed, PluginSoftwareStatus.Found));

        // а вот «инициализирован» без софта — это НЕ «работает», а «софт ищется»
        Assert.Equal(PluginStates.Initialized,
            PluginLifecycle.Shown(PluginStates.Initialized, PluginSoftwareStatus.Found));
        Assert.Equal(PluginStates.Initialized,
            PluginLifecycle.Shown(PluginStates.Initialized, PluginSoftwareStatus.NotNeeded));
        Assert.Equal(PluginLifecycle.ProbingSoftware,
            PluginLifecycle.Shown(PluginStates.Initialized, PluginSoftwareStatus.CanInstall));
        Assert.Equal(PluginLifecycle.ProbingSoftware,
            PluginLifecycle.Shown(PluginStates.Initialized, PluginSoftwareStatus.NeedsManualPath));
    }

    [Fact]
    public void Actions_Are_Published_Only_Where_The_Software_Is_Found()
    {
        // на сервере, где софта нет и путь не указан, действия плагина не публикуются —
        // ровно как локальная модель без установленных файлов не может быть активной
        Assert.True(PluginLifecycle.Publishes(PluginStates.Initialized, PluginSoftwareStatus.Found));
        Assert.True(PluginLifecycle.Publishes(PluginStates.Initialized, PluginSoftwareStatus.NotNeeded));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Initialized, PluginSoftwareStatus.CanInstall));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Initialized, PluginSoftwareStatus.NeedsManualPath));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Declared, PluginSoftwareStatus.Found));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Disabled, PluginSoftwareStatus.Found));
        Assert.False(PluginLifecycle.Publishes(PluginStates.Removed, PluginSoftwareStatus.Found));
    }

    // ---------- 2. регистрация действий ----------

    [Fact]
    public void Initialization_Registers_A_Custom_Action_For_Every_Tool()
    {
        var manifest = Gateway();
        Assert.Equal(2, Setup.Initialize(manifest));

        var actions = _f.Actions.List("ru");
        var export = actions.Single(a => a.ToolName == "shotcut_export");
        var render = actions.Single(a => a.ToolName == "shotcut_render");

        // код действия берётся ИЗ МАНИФЕСТА (без него разбор действие отбрасывает вовсе)
        Assert.Equal("AI2P.Plugins.Shotcut.Export", export.Code);
        Assert.Equal("AI2P.Plugins.Shotcut.Render", render.Code);
        Assert.True(export.IsCustom);
        Assert.True(render.IsCustom);
        Assert.Equal("Собрать проект", export.Title);

        // теперь правила безопасности видят инструмент по коду — до регистрации не видели
        Assert.Equal("AI2P.Plugins.Shotcut.Export", _f.Actions.CodeByTool("shotcut_export"));

        // повторная инициализация идемпотентна: вторых экземпляров нет
        Assert.Equal(0, Setup.Initialize(manifest));
        Assert.Equal(2, _f.Actions.List("ru").Count(a => a.ToolName?.StartsWith("shotcut_") == true));
    }

    [Fact]
    public void A_Tool_Without_A_Catalog_Entry_Is_Not_Published()
    {
        var manifest = Mcp();
        // сервер MCP объявил два инструмента, а зарегистрировали только один:
        // второй появился у сервера уже после инициализации
        Assert.Equal(1, Setup.Initialize(manifest, ["mcp_first"]));

        var published = Setup.PublishedTools(manifest, ["mcp_first", "mcp_second"]);
        Assert.Equal(["mcp_first"], published);
        Assert.Null(_f.Actions.CodeByTool("mcp_second"));
        // у инструментов MCP кода в манифесте нет — он складывается приставкой плагина
        Assert.Equal("Plugin.mcp.demo.mcp_first", _f.Actions.CodeByTool("mcp_first"));

        // регистрация нового инструмента его публикует
        Assert.Equal(1, Setup.Initialize(manifest, ["mcp_first", "mcp_second"]));
        Assert.Equal(["mcp_first", "mcp_second"], Setup.PublishedTools(manifest, ["mcp_first", "mcp_second"]));
    }

    [Fact]
    public void A_Plugin_Tool_Without_A_Catalog_Entry_Is_Denied_But_An_Ordinary_One_Is_Not()
    {
        var task = _f.Tasks.Create(new TaskItem { Title = "монтаж" }, "", "", actorId: null);
        var security = new SecurityEvaluator(_f.Security.EffectiveForTask(task), null);
        var tools = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            security: security, actionCodeByTool: _f.Actions.CodeByTool)
        {
            PluginTools = new HashSet<string>(StringComparer.Ordinal) { "mcp_second" },
        };

        // КОНТРОЛЬНЫЙ ОПЫТ: обычный инструмент без записи справочника по-прежнему разрешён —
        // иначе проверка ниже доказывала бы лишь то, что запрещено всё подряд
        Assert.Equal(SecurityDecision.Allow, tools.Authorize("no_such_tool", NoArgs).Decision);

        // а плагинный без записи — запрещён
        var check = tools.Authorize("mcp_second", NoArgs);
        Assert.Equal(SecurityDecision.Deny, check.Decision);
        Assert.Contains("mcp_second", check.Message);
        Assert.Single(tools.SecurityTriggers);
    }

    [Fact]
    public void A_Registered_Plugin_Tool_Obeys_The_Usual_Rules()
    {
        var manifest = Gateway();
        Setup.Initialize(manifest);
        var task = _f.Tasks.Create(new TaskItem { Title = "монтаж" }, "", "", actorId: null);
        var tools = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            security: new SecurityEvaluator(_f.Security.EffectiveForTask(task), null),
            actionCodeByTool: _f.Actions.CodeByTool)
        {
            PluginTools = new HashSet<string>(StringComparer.Ordinal) { "shotcut_export" },
        };
        // запись справочника есть, запрещающего правила нет — вызов разрешён
        Assert.Equal(SecurityDecision.Allow, tools.Authorize("shotcut_export", NoArgs).Decision);
    }

    [Fact]
    public void A_Custom_Action_Survives_A_Raised_Seed_Version()
    {
        Setup.Initialize(Gateway());
        var before = _f.Actions.List("ru").Single(a => a.ToolName == "shotcut_export");

        // сид дистрибутива проходит по списку id из файлов; уборки «лишних» записей в нём
        // нет вовсе, поэтому кастомная строка переживает любой seedVersion
        _f.Actions.Seed();

        var after = _f.Actions.List("ru").Single(a => a.ToolName == "shotcut_export");
        Assert.Equal(before.Id, after.Id);
        Assert.True(after.IsCustom);
        Assert.Equal("AI2P.Plugins.Shotcut.Export", after.Code);
    }

    [Fact]
    public void No_Custom_Action_Uses_An_Id_From_The_Seed_Files()
    {
        // СТОРОЖ: плагин пишет свои действия в базу через API справочника и НИКОГДА не
        // дописывает их в i18n/ActionCatalogService_<язык>.json — установка обновления кладёт
        // файлы дистрибутива поверх, дописанное пропадёт, а записи в базе останутся сиротами
        // с чужим seed_version
        Setup.Initialize(Gateway());
        var seeded = SeedActionIds();
        Assert.NotEmpty(seeded);

        using var conn = _f.Db.Open();
        var custom = Sql.Query(conn, null,
            "SELECT id FROM actions WHERE is_custom=1 AND deleted_at IS NULL",
            r => r.S("id"));
        Assert.NotEmpty(custom);
        Assert.DoesNotContain(custom, seeded.Contains);

        // и наоборот: у сидовых записей seed_version больше нуля, у плагинных — ноль
        var pluginSeed = Sql.Scalar<long>(conn, null,
            "SELECT seed_version FROM actions WHERE tool_name='shotcut_export'");
        Assert.Equal(0, pluginSeed);
    }

    // ---------- 3. записи опыта ----------

    [Fact]
    public void Experience_Records_Are_General_With_A_Skill_And_A_Plugin_Owner()
    {
        Setup.Initialize(Gateway());

        var records = Setup.ExperienceOf("editor.shotcut");
        var record = Assert.Single(records);
        Assert.True(record.IsGeneral);
        var owner = Assert.Single(record.Tags);
        Assert.Equal("plugin:editor.shotcut", owner);
        Assert.Equal("editor.shotcut", PluginCodes.OwnerPlugin(owner));
        // служебная пометка не сужает отбор: у задачи со СВОИМИ тэгами запись всё равно идёт
        Assert.True(ExperienceService.TagsMatch(record.Tags, ["сборка"]));
        Assert.True(ExperienceService.GoesToPrompt(record, ["video-edit"], ["сборка"]));
        // навык обязателен: без него запись конкурирует за место в промпте у КАЖДОЙ задачи
        Assert.Equal("video-edit", record.SkillName);
        // «загружать всегда» плагинным записям не ставится — по той же причине
        Assert.False(record.AlwaysLoad);

        // повторная инициализация второй такой же записи не заводит
        Setup.Initialize(Gateway());
        Assert.Single(Setup.ExperienceOf("editor.shotcut"));
    }

    [Fact]
    public void An_Experience_Record_Without_A_Skill_Is_Refused()
    {
        var manifest = PluginManifest.Parse("""
            {
              "code": "editor.bad", "kind": "gateway",
              "experience": [ { "text": "правило без навыка" } ]
            }
            """)!;
        Assert.Throws<ArgumentException>(() => Setup.Initialize(manifest));
        Assert.Empty(Setup.ExperienceOf("editor.bad"));
    }

    // ---------- 4. снятие ----------

    [Fact]
    public void Removing_A_Plugin_Takes_Away_Its_Actions_And_Its_Experience()
    {
        var manifest = Gateway();
        Setup.Initialize(manifest);
        Assert.Equal(2, Setup.RegisteredTools(manifest).Count);
        Assert.Single(Setup.ExperienceOf("editor.shotcut"));

        Setup.Remove(manifest);

        Assert.Empty(Setup.RegisteredTools(manifest));
        Assert.Empty(Setup.PublishedTools(manifest));
        Assert.Empty(Setup.ExperienceOf("editor.shotcut"));
        Assert.Null(_f.Actions.CodeByTool("shotcut_export"));
        Assert.DoesNotContain(_f.Actions.List("ru"), a => a.ToolName == "shotcut_render");

        // правило безопасности, ссылавшееся на снятый код, осталось с неизвестным кодом —
        // и это читается как «запрещено»: инструмент плагина без записи справочника закрыт
        var task = _f.Tasks.Create(new TaskItem { Title = "монтаж" }, "", "", actorId: null);
        var tools = new AgentToolset(null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            security: new SecurityEvaluator(_f.Security.EffectiveForTask(task), null),
            actionCodeByTool: _f.Actions.CodeByTool)
        {
            PluginTools = PluginSetupService.PluginToolNames([manifest]),
        };
        Assert.Equal(SecurityDecision.Deny, tools.Authorize("shotcut_export", NoArgs).Decision);

        // а настройки и ручной путь снятие не трогает: они в config.json сервера,
        // повторная инициализация не требует вводить всё заново
        Assert.Equal(2, Setup.Initialize(manifest));
    }

    [Fact]
    public void Another_Plugin_Cannot_Take_A_Tool_Name_That_Is_Already_Taken()
    {
        Setup.Initialize(Gateway());
        var other = PluginManifest.Parse("""
            {
              "code": "editor.other", "kind": "gateway",
              "actions": [ { "code": "AI2P.Plugins.Other.Export", "tool": "shotcut_export" } ]
            }
            """)!;
        // иначе правило безопасности на НАШЕ действие увело бы вызов в чужой плагин
        Assert.Throws<ArgumentException>(() => Setup.Initialize(other));
    }

    // ---------- 5. поиск софта ----------

    [Fact]
    public void Software_Is_Looked_Up_In_Path_With_A_Version_Check()
    {
        PluginSoftwareProbe.Forget();
        var dir = Path.Combine(_f.Dir, "bin");
        Directory.CreateDirectory(dir);
        var exe = Fake(dir, "melt");

        var manifest = PluginManifest.Parse($$"""
            {
              "code": "editor.probe", "kind": "gateway",
              "software": { "package": "shotcut", "pathKey": "meltPath",
                            "system": { "commands": ["{{Path.GetFileNameWithoutExtension(exe)}}"] } }
            }
            """)!;

        var found = PluginSoftwareProbe.Find(manifest.Software, manualPath: null, searchPath: dir);
        Assert.Equal(PluginSoftwareStatus.Found, found.Status);
        Assert.True(found.Ready);

        // версия обязательна: у ffmpeg набор ключей заметно менялся между 4.x и 7.x,
        // и найденная негодная версия — это «не нашли», а не «сойдёт»
        PluginSoftwareProbe.Forget();
        manifest.Software!.System!.VersionArgs = "--version";
        manifest.Software.System.MinVersion = "99.0";
        var refused = PluginSoftwareProbe.Find(manifest.Software, manualPath: null, searchPath: dir);
        Assert.NotEqual(PluginSoftwareStatus.Found, refused.Status);
    }

    [Fact]
    public void Software_That_Is_Not_Found_Offers_Install_Or_A_Manual_Path()
    {
        PluginSoftwareProbe.Forget();
        var empty = Path.Combine(_f.Dir, "nothing");
        Directory.CreateDirectory(empty);

        // пакет назван — можно поставить
        var withPackage = PluginManifest.Parse("""
            { "code": "p.1", "kind": "gateway",
              "software": { "package": "shotcut", "system": { "commands": ["no-such-tool-xyz"] } } }
            """)!;
        Assert.Equal(PluginSoftwareStatus.CanInstall,
            PluginSoftwareProbe.Find(withPackage.Software, null, empty).Status);

        // пакета нет — только «укажите, где он уже стоит» (случай DaVinci Resolve)
        PluginSoftwareProbe.Forget();
        var noPackage = PluginManifest.Parse("""
            { "code": "p.2", "kind": "gateway",
              "software": { "pathKey": "resolveDir", "system": { "commands": ["no-such-tool-xyz"] } } }
            """)!;
        Assert.Equal(PluginSoftwareStatus.NeedsManualPath,
            PluginSoftwareProbe.Find(noPackage.Software, null, empty).Status);

        // программы вовсе не нужно — плагин готов к работе и без неё
        Assert.Equal(PluginSoftwareStatus.NotNeeded, PluginSoftwareProbe.Find(null).Status);
    }

    [Fact]
    public void A_Manual_Path_Works_Both_As_A_File_And_As_A_Directory()
    {
        PluginSoftwareProbe.Forget();
        var dir = Path.Combine(_f.Dir, "resolve");
        Directory.CreateDirectory(dir);
        var exe = Fake(dir, "resolve");
        var empty = Path.Combine(_f.Dir, "empty-path");
        Directory.CreateDirectory(empty);

        var manifest = PluginManifest.Parse($$"""
            { "code": "editor.resolve", "kind": "gateway",
              "software": { "pathKey": "resolveDir",
                            "system": { "commands": ["{{Path.GetFileNameWithoutExtension(exe)}}"] } } }
            """)!;

        // человек показал КАТАЛОГ, куда поставил программу
        var byDir = PluginSoftwareProbe.Find(manifest.Software, dir, empty);
        Assert.Equal(PluginSoftwareStatus.Found, byDir.Status);

        // и он же мог показать сам файл
        PluginSoftwareProbe.Forget();
        var byFile = PluginSoftwareProbe.Find(manifest.Software, exe, empty);
        Assert.Equal(PluginSoftwareStatus.Found, byFile.Status);
        Assert.Equal(exe, byFile.Path);
    }

    /// <summary>Пустой файл-«программа» с расширением, годным для PATHEXT этой системы.
    /// НУЛЕВОЙ длины быть не должен: такие файлы проба пропускает намеренно (это псевдонимы
    /// магазина приложений Windows).</summary>
    private static string Fake(string dir, string name)
    {
        var file = Path.Combine(dir, OperatingSystem.IsWindows() ? name + ".cmd" : name);
        File.WriteAllText(file, "rem ai2p test\n");
        return file;
    }

    /// <summary>Идентификаторы действий из файлов сида дистрибутива (i18n рядом с набором).</summary>
    private static HashSet<string> SeedActionIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var path in Directory.EnumerateFiles(dir, "ActionCatalogService_*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("actions", out var actions))
            {
                foreach (var action in actions.EnumerateArray())
                {
                    if (action.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } value)
                    {
                        ids.Add(value);
                    }
                }
            }
        }
        return ids;
    }
}
