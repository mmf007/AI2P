using AI2P.Core;
using AI2P.Server.Api;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-510-S0 (плагин VS Code, этап 8.5): поиск установленных VS Code на поддельной машине,
/// выбор копии хранится локально и не попадает в журнал репликации, link.json без секретов,
/// без каталога проекта кнопок нет.
/// </summary>
public sealed class T510S0Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t510s0-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeMachine : IVsCodeMachine
    {
        public bool IsWindows { get; init; } = true;
        public bool IsMac { get; init; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string, string, string)> Registry { get; } = [];
        public int VersionRuns { get; private set; }
        public int RegistryReads { get; private set; }

        public bool FileExists(string path) => Files.Contains(Norm(path));
        public string? ReadText(string path) => Texts.TryGetValue(Norm(path), out var t) ? t : null;
        public string? Env(string name) => Vars.TryGetValue(name, out var v) ? v : null;

        public IEnumerable<(string DisplayName, string InstallLocation, string DisplayVersion)> UninstallEntries()
        {
            RegistryReads++;
            return Registry;
        }

        public string? RunVersion(string codePath)
        {
            VersionRuns++;
            // по договору IVsCodeMachine.RunVersion — только первая строка вывода «--version»
            return "9.9.9";
        }

        public static string Norm(string path) => Path.GetFullPath(path).Replace('\\', '/');
    }

    private static FakeMachine WindowsMachine()
    {
        var m = new FakeMachine();
        m.Vars["LOCALAPPDATA"] = @"C:\Users\u\AppData\Local";
        m.Vars["ProgramFiles"] = @"C:\Program Files";
        // обычный VS Code у пользователя, версия — из package.json без запуска
        m.Files.Add(FakeMachine.Norm(@"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd"));
        m.Texts[FakeMachine.Norm(@"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\resources\app\package.json")] =
            """{"name":"code-oss-dev","version":"1.105.1"}""";
        // Insiders в нестандартном каталоге — находится только по реестру, версия оттуда же
        m.Files.Add(FakeMachine.Norm(@"D:\Tools\Insiders\bin\code-insiders.cmd"));
        m.Registry.Add(("Microsoft Visual Studio Code Insiders (User)", @"D:\Tools\Insiders\", "1.106.0-insider"));
        m.Registry.Add(("Visual Studio Code - Insiders", @"D:\Tools\Insiders", "1.106.0-insider"));
        m.Registry.Add(("Notepad++", @"C:\Program Files\Notepad++", "8.0"));
        // Cursor: code.cmd лежит в resources\app\bin
        m.Files.Add(FakeMachine.Norm(@"C:\Users\u\AppData\Local\Programs\cursor\resources\app\bin\cursor.cmd"));
        // VSCodium из PATH — версию узнаём запуском «codium --version»
        m.Files.Add(FakeMachine.Norm(@"E:\VSCodium\bin\codium.cmd"));
        // в PATH ещё раз тот же VS Code: дубликатом он стать не должен
        m.Vars["PATH"] = @"C:\Windows;C:\Users\u\AppData\Local\Programs\Microsoft VS Code\bin;E:\VSCodium\bin";
        return m;
    }

    [Fact]
    public void Locator_Finds_Copies_On_Fake_Paths_And_Registry()
    {
        var machine = WindowsMachine();
        var found = new VsCodeLocator(machine).Find();

        Assert.Equal(["code", "code-insiders", "codium", "cursor"], found.Select(f => f.Kind).OrderBy(k => k));
        var code = found.Single(f => f.Kind == "code");
        Assert.Equal("1.105.1", code.Version);
        Assert.Equal("vscode", code.UriScheme);
        var insiders = found.Single(f => f.Kind == "code-insiders");
        Assert.Equal("1.106.0-insider", insiders.Version);
        Assert.Equal("vscode-insiders", insiders.UriScheme);
        Assert.Equal("9.9.9", found.Single(f => f.Kind == "codium").Version);
        Assert.Equal("cursor", found.Single(f => f.Kind == "cursor").UriScheme);
        // «code --version» звали только там, где версии иначе не узнать
        Assert.True(machine.VersionRuns <= 2);
    }

    [Fact]
    public void Locator_Caches_The_Answer_For_A_Short_Time()
    {
        var machine = WindowsMachine();
        var now = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
        var locator = new VsCodeLocator(machine, () => now);

        locator.Find();
        var runs = machine.VersionRuns;
        locator.Find();
        Assert.Equal(1, machine.RegistryReads);
        Assert.Equal(runs, machine.VersionRuns);

        now += VsCodeLocator.CacheLifetime + TimeSpan.FromSeconds(1);
        locator.Find();
        Assert.Equal(2, machine.RegistryReads);

        locator.Find(refresh: true);
        Assert.Equal(3, machine.RegistryReads);
    }

    [Fact]
    public void Locator_On_Linux_Uses_Path_Snap_And_Flatpak()
    {
        var m = new FakeMachine { IsWindows = false };
        m.Vars["PATH"] = "/usr/bin:/usr/local/bin";
        m.Vars["HOME"] = "/home/u";
        m.Files.Add(FakeMachine.Norm("/usr/bin/code"));
        m.Files.Add(FakeMachine.Norm("/snap/bin/codium"));
        m.Files.Add(FakeMachine.Norm("/var/lib/flatpak/exports/bin/com.visualstudio.code"));

        var found = new VsCodeLocator(m).Find();

        Assert.Equal(3, found.Count);
        Assert.Equal(2, found.Count(f => f.Kind == "code"));
        Assert.Single(found, f => f.Kind == "codium");
    }

    [Fact]
    public void Choice_Is_Stored_On_The_Server_And_Not_Replicated()
    {
        var db = new Database(_dir, "server.db", DatabaseKind.Server);
        db.Init();
        var service = new ProjectVsCodeService(db);

        Assert.Null(service.Get("org-a", "prj-1"));
        service.Set("org-a", "prj-1", @"C:\VS\bin\code.cmd");
        service.Set("org-a", "prj-1", @"D:\Insiders\bin\code-insiders.cmd");
        service.Set("org-a", "prj-2", @"C:\VS\bin\code.cmd");

        Assert.Equal(@"D:\Insiders\bin\code-insiders.cmd", service.Get("org-a", "prj-1"));
        Assert.Null(service.Get("org-b", "prj-1"));
        Assert.Equal(2, service.AllPaths().Count);
        Assert.DoesNotContain("project_vscode", ChangeLog.ServerTables);
        using (var conn = db.Open())
        {
            var hasChanges = Sql.Scalar<long>(conn, null,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='changes'") > 0;
            if (hasChanges)
            {
                Assert.Equal(0, Sql.Scalar<long>(conn, null,
                    "SELECT COUNT(*) FROM changes WHERE tbl='project_vscode'"));
            }
        }
        service.Clear("org-a", "prj-1");
        Assert.Null(service.Get("org-a", "prj-1"));
    }

    [Fact]
    public void Link_Json_Has_The_Fields_And_No_Secrets()
    {
        var link = VsCodeEndpoints.BuildLink(VsCodeLink.ServerUrl(5480, "/ai2p"), "/ai2p", "mmfgrp", "prj-1",
            @"C:\VS\bin\code.cmd");
        var json = link.ToJson();

        Assert.Equal("http://localhost:5480/ai2p", link.Server);
        Assert.Equal("http://localhost:5480", VsCodeLink.ServerUrl(5480, ""));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        foreach (var name in new[] { "server", "org", "projectId", "serverExe", "serverArgs", "version", "code" })
        {
            Assert.Contains(name, names);
        }
        Assert.Equal(AppInfo.Version, link.Version);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DeviceTokenService.Prefix, json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        // ссылка с токеном строится по схеме ВЫБРАННОЙ копии
        var uri = VsCodeLink.LinkUri("vscode-insiders", "ai2pd_x+y", link.Server, "mmfgrp", "prj-1");
        Assert.StartsWith("vscode-insiders://ai2p.ai2p/link?token=ai2pd_x%2By&server=", uri);
    }

    [Fact]
    public void Connected_State_And_GitIgnore_Follow_The_Files()
    {
        Directory.CreateDirectory(_dir);
        Assert.False(VsCodeLink.IsConnected(_dir));
        Assert.False(VsCodeLink.IsConnected(""));
        Directory.CreateDirectory(Path.Combine(_dir, VsCodeLink.DirName));
        File.WriteAllText(VsCodeLink.PathIn(_dir), "{}");
        Assert.True(VsCodeLink.IsConnected(_dir));

        File.WriteAllText(Path.Combine(_dir, ".gitignore"), "bin/");
        Assert.False(VsCodeLink.GitIgnoreHas(_dir));
        VsCodeLink.AddToGitIgnore(_dir);
        VsCodeLink.AddToGitIgnore(_dir);
        Assert.True(VsCodeLink.GitIgnoreHas(_dir));
        Assert.Single(File.ReadAllLines(Path.Combine(_dir, ".gitignore")), l => l == ".ai2p/");
    }

    [Fact]
    public void Cmd_Arguments_Are_Quoted_So_The_Ampersand_Stays_Inside()
    {
        var info = VsCodeRunner.StartInfo(@"C:\VS Code\bin\code.cmd", ["--open-url", "vscode://a?x=1&y=2"]);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("cmd.exe", info.FileName);
            Assert.Contains("\"vscode://a?x=1&y=2\"", info.Arguments);
            Assert.Contains("\"C:\\VS Code\\bin\\code.cmd\"", info.Arguments);
        }
        // кавычки и «%» внутри аргумента cmd.exe недопустимы — выбрасываются
        Assert.Equal("\"ab\"", VsCodeRunner.Quote("a\"%b"));
    }

    [Fact]
    public void Buttons_Are_Hidden_Without_Project_Folder_Or_VsCode()
    {
        var db = new Database(_dir, "server.db", DatabaseKind.Server);
        db.Init();
        var choices = new ProjectVsCodeService(db);
        var withCode = new VsCodeLocator(WindowsMachine());
        var withoutCode = new VsCodeLocator(new FakeMachine());
        var folder = Path.Combine(_dir, "project");
        Directory.CreateDirectory(folder);

        Assert.False(VsCodeEndpoints.State(withCode, choices, "org-a", "prj-1", "", false).Available);
        Assert.False(VsCodeEndpoints.State(withCode, choices, "org-a", "prj-1",
            Path.Combine(_dir, "нет-такого"), false).Available);
        Assert.False(VsCodeEndpoints.State(withoutCode, choices, "org-a", "prj-1", folder, false).Available);
        var state = VsCodeEndpoints.State(withCode, choices, "org-a", "prj-1", folder, false);
        Assert.True(state.Available);
        Assert.False(state.Connected);
        Assert.Equal(4, state.Installs.Count);
    }
}
