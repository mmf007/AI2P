using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-511-S0 (плагин VS Code 8.6): имя и версия файла расширения, сравнение версий,
/// разбор вывода <c>code --list-extensions --show-versions</c>, выбор копий VS Code для
/// обновления при старте сервера и наличие шага сборки .vsix в скриптах выпуска.
/// </summary>
public class T511S0Tests
{
    [Fact]
    public void Vsix_Name_And_Extension_Version_Follow_App_Version()
    {
        Assert.Equal("ai2p-1.168.vsix", VsCodeExtension.VsixName("1.168"));
        Assert.Equal("1.168.0", VsCodeExtension.ExtensionVersion("1.168"));
        Assert.Equal("1.168.0", VsCodeExtension.ExtensionVersion("1.168.0"));
        Assert.Equal(VsCodeExtension.VsixName(AppInfo.Version), "ai2p-" + AppInfo.Version + ".vsix");
    }

    [Theory]
    [InlineData("1.166.0", "1.167", true)]
    [InlineData("1.167.0", "1.167", false)]
    [InlineData("1.168.0", "1.167", false)]
    [InlineData("1.99.0", "1.100", true)]      // числами, а не строкой
    [InlineData("1.167.0-pre", "1.167", false)]
    [InlineData("", "1.167", true)]
    public void IsOlder_Compares_Numbers(string installed, string target, bool older)
    {
        Assert.Equal(older, VsCodeExtension.IsOlder(installed, target));
    }

    [Fact]
    public void Installed_Version_Is_Found_In_List_Output()
    {
        var output = "ms-python.python@2026.1.0\r\nAI2P.ai2p@1.166.0\r\nai2p.other@9.9.9\n";
        Assert.Equal("1.166.0", VsCodeExtension.InstalledVersion(output));
        Assert.Null(VsCodeExtension.InstalledVersion("ms-python.python@2026.1.0\n"));
        Assert.Null(VsCodeExtension.InstalledVersion(""));
        Assert.Null(VsCodeExtension.InstalledVersion(null));
    }

    [Fact]
    public void Update_Only_When_Installed_And_Older()
    {
        Assert.True(VsCodeExtension.NeedsUpdate("ai2p.ai2p@1.166.0", "1.167"));
        Assert.False(VsCodeExtension.NeedsUpdate("ai2p.ai2p@1.167.0", "1.167"));
        Assert.False(VsCodeExtension.NeedsUpdate("ai2p.ai2p@1.170.0", "1.167"));
        // расширения нет вовсе — ставит «Подключить VS Code», а не старт сервера
        Assert.False(VsCodeExtension.NeedsUpdate("ms-python.python@1.0.0", "1.167"));
    }

    [Fact]
    public void Copies_Are_Deduplicated_And_Missing_Are_Dropped()
    {
        var root = Path.Combine(Path.GetTempPath(), "t511s0-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "VS Code", "bin", "code.cmd");
        var b = Path.Combine(root, "Insiders", "bin", "code-insiders.cmd");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { a, b };
        var picked = VsCodeExtension.SelectCopies(
            [a, "", null, "  ", "\"" + a + "\"", b, Path.Combine(root, "gone", "code.cmd")],
            existing.Contains);
        Assert.Equal(new[] { a, b }, picked);
    }

    [Fact]
    public void Vsix_Path_Is_Null_Without_File_And_Found_With_It()
    {
        var dir = Path.Combine(Path.GetTempPath(), "t511s0-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(VsCodeExtension.VsixPath(dir));
            File.WriteAllText(Path.Combine(dir, "ai2p-1.1.vsix"), "old");   // чужая версия не годится
            Assert.Null(VsCodeExtension.VsixPath(dir));
            var own = Path.Combine(dir, VsCodeExtension.VsixName(AppInfo.Version));
            File.WriteAllText(own, "zip");
            Assert.Equal(own, VsCodeExtension.VsixPath(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Install_Args_Force_Replace()
    {
        Assert.Equal(new[] { "--install-extension", "x.vsix", "--force" }, VsCodeExtension.InstallArgs("x.vsix"));
        Assert.Equal(new[] { "--list-extensions", "--show-versions" }, VsCodeExtension.ListArgs);
    }

    [Fact]
    public void Updater_Without_Copies_Does_Nothing()
    {
        var saved = VsCodeExtension.RememberedCopies;
        try
        {
            VsCodeExtension.RememberedCopies = () => [];
            Assert.Equal(0, AI2P.Server.VsCodeExtensionUpdater.Run(Path.GetTempPath(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        }
        finally
        {
            VsCodeExtension.RememberedCopies = saved;
        }
    }

    [Fact]
    public void Release_Scripts_Pack_The_Vsix()
    {
        var app = AppRoot();
        Assert.Contains("vscode/pack-vsix.ps1", File.ReadAllText(Path.Combine(app, "buildRelease.ps1")));
        Assert.Contains("vscode/pack-vsix.sh", File.ReadAllText(Path.Combine(app, "buildRelease.sh")));
        Assert.True(File.Exists(Path.Combine(app, "vscode", "ai2p", "vsix", "extension.vsixmanifest")));
        Assert.True(File.Exists(Path.Combine(app, "vscode", "ai2p", "vsix", "[Content_Types].xml")));
        var manifest = File.ReadAllText(Path.Combine(app, "vscode", "ai2p", "vsix", "extension.vsixmanifest"));
        Assert.Contains("@VERSION@", manifest);
        Assert.Contains("Id=\"ai2p\"", manifest);
        Assert.Contains("Publisher=\"ai2p\"", manifest);
    }

    /// <summary>Каталог AI2P_app: от выходного каталога тестов вверх до buildRelease.ps1.</summary>
    static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "buildRelease.ps1")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
