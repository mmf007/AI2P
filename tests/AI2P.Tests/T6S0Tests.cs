using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-6-S0: ЗНАЧОК ПРИЛОЖЕНИЯ.
///
/// Из пяти вариантов, нарисованных в T-288, заказчик выбрал третий — «Знак-надпись AI2P».
/// Значок подключается в четырёх местах, и все четыре держатся тут, потому что ни одно
/// из них не проверяется обычными тестами продукта:
///
///   1. <c>ApplicationIcon</c> в проекте сервера — значок встраивается в
///      <c>AI2P.Server.exe</c>, и от него его получают ярлык рабочего стола, окно
///      проводника и строка в «Установке и удалении программ»;
///   2. тот же файл едет В ВЫКЛАДКЕ — по нему <c>MakePackage.ps1</c> ставит значок
///      самому установщику (<c>SetupIconFile</c>): пакет собирается из каталога выкладки,
///      исходников рядом с ним может не быть;
///   3. значок вкладки браузера (<c>favicon.ico</c>) — и в приложении, и на страницах
///      входа и первого старта, у которых своя вёрстка;
///   4. состав самих файлов значка: набор размеров внутри <c>.ico</c>.
///
/// Рисунок пересобирается одной командой — <c>python test/t6s0/mkappicon.py</c>; шрифтов
/// в файлах нет (буквы — свои контуры), поэтому картинка одинакова на любой машине.
/// </summary>
public sealed class T6S0Tests
{
    /// <summary>Каталог <c>AI2P_app</c> (по <c>AI2P.sln</c>) — там проекты и скрипты.</summary>
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    /// <summary>Корень репозитория: там лежат doc/ с исходниками рисунка и test/.</summary>
    private static string RepoRoot() => Directory.GetParent(AppDir())!.FullName;

    private static string ServerIcon() => Path.Combine(AppDir(), "src", "AI2P.Server", "ai2p.ico");

    private static string WebIcon(string name) =>
        Path.Combine(AppDir(), "src", "AI2P.UI", "wwwroot", name);

    private static string Text(params string[] parts) =>
        File.ReadAllText(Path.Combine(AppDir(), Path.Combine(parts)));

    /// <summary>Размеры образов внутри .ico. Разбираем сами: заголовок ICONDIR — шесть байт
    /// (0, тип 1, число образов), дальше по 16 байт на образ, где ширина и высота
    /// однобайтовые, а ноль означает 256.</summary>
    private static List<int> IcoSizes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 22, "файл значка пуст: " + path);
        Assert.Equal(0, bytes[0] + bytes[1]);                       // reserved
        Assert.Equal(1, bytes[2] + bytes[3] * 256);                 // тип 1 = значок
        var count = bytes[4] + bytes[5] * 256;
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var w = bytes[entry] == 0 ? 256 : bytes[entry];
            var h = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            Assert.Equal(w, h);                                     // значки квадратные
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            Assert.True(length > 0 && offset + length <= bytes.Length,
                        $"образ {w} px выходит за пределы файла {path}");
            sizes.Add(w);
        }
        return sizes;
    }

    // ---------- 1. значок программы ----------

    /// <summary>Проект сервера объявляет значок, и файл лежит рядом с проектом.</summary>
    [Fact]
    public void The_Server_Project_Declares_The_Application_Icon()
    {
        var csproj = Text("src", "AI2P.Server", "AI2P.Server.csproj");
        Assert.Contains("<ApplicationIcon>ai2p.ico</ApplicationIcon>", csproj);
        Assert.True(File.Exists(ServerIcon()), "нет файла значка: " + ServerIcon());
    }

    /// <summary>Значок несёт все размеры, которые спрашивает Windows: от плитки 256
    /// до 16 px в панели задач. 24 и 16 нарисованы отдельно (упрощённо), поэтому они
    /// обязаны быть В ФАЙЛЕ: подставить их уменьшением система не должна.</summary>
    [Fact]
    public void The_Application_Icon_Holds_Every_Size()
    {
        var sizes = IcoSizes(ServerIcon());
        foreach (var expected in new[] { 256, 128, 64, 48, 32, 24, 16 })
        {
            Assert.Contains(expected, sizes);
        }
    }

    /// <summary>Тот же файл едет в выкладке — иначе установщику нечем взять значок.</summary>
    [Fact]
    public void The_Icon_Travels_Inside_The_Release()
    {
        var csproj = Text("src", "AI2P.Server", "AI2P.Server.csproj");
        var line = csproj.Split('\n').FirstOrDefault(l => l.Contains("Include=\"ai2p.ico\""));
        Assert.NotNull(line);
        Assert.Contains("<Content", line);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", csproj);
    }

    // ---------- 2. значок установщика ----------

    /// <summary>Пакет установки берёт значок ИЗ ВЫКЛАДКИ и подставляет его в сценарий
    /// Inno Setup. Выкладка старее этой версии значка не содержит — тогда строки
    /// в сценарии просто нет, и пакет собирается как раньше.</summary>
    [Fact]
    public void The_Package_Gives_The_Installer_Its_Icon()
    {
        var script = Text("MakePackage.ps1");
        Assert.Contains("@SETUPICON@", script);                       // место в сценарии
        Assert.Contains("$iss.Replace(\"@SETUPICON@\", $setupIcon)", script);
        Assert.Contains("Join-Path $stage \"ai2p.ico\"", script);      // источник — выкладка
        Assert.Contains("SetupIconFile=$iconStaged", script);
        // без значка сборка пакета не падает
        Assert.Contains("$setupIcon = \"\"", script);
        // строка «Установка и удаление программ» берёт значок из самой программы
        Assert.Contains("UninstallDisplayIcon={app}\\AI2P.Server.exe", script);
    }

    // ---------- 3. значок вкладки браузера ----------

    /// <summary>Значок вкладки лежит в статике библиотеки интерфейса — оттуда его отдаёт
    /// сервер по <c>_content/AI2P.UI/…</c>, как скрипт и стили.</summary>
    [Fact]
    public void The_Web_Icon_Files_Are_In_Place()
    {
        Assert.True(File.Exists(WebIcon("favicon.ico")), "нет favicon.ico");
        Assert.True(File.Exists(WebIcon("apple-touch-icon.png")), "нет apple-touch-icon.png");
        foreach (var expected in new[] { 48, 32, 24, 16 })
        {
            Assert.Contains(expected, IcoSizes(WebIcon("favicon.ico")));
        }
    }

    /// <summary>Ссылка на значок стоит на ОБЕИХ страницах: в приложении и на своей вёрстке
    /// входа и первого старта (T-291) — первое, что видит человек, это как раз она.
    /// Путь относительный: он верен и с префиксом приложения, и с кодом организации.</summary>
    [Fact]
    public void Both_Page_Shells_Point_To_The_Web_Icon()
    {
        var app = Text("src", "AI2P.Server", "Components", "App.razor");
        Assert.Contains("rel=\"icon\" href=\"_content/AI2P.UI/favicon.ico\"", app);
        Assert.Contains("rel=\"apple-touch-icon\" href=\"_content/AI2P.UI/apple-touch-icon.png\"", app);
        Assert.DoesNotContain("href=\"/_content/AI2P.UI/favicon.ico\"", app);

        var auth = Text("src", "AI2P.Server", "Api", "AuthPages.cs");
        Assert.Contains("rel=\"icon\" href=\"_content/AI2P.UI/favicon.ico\"", auth);
    }

    // ---------- 4. рисунок воспроизводим ----------

    /// <summary>Рисунок собирается из исходника выбранного варианта одной командой.
    /// Проверяем, что и исходник, и сборщик на месте: без них значок нельзя ни поправить,
    /// ни повторить — а шрифтов в файлах нет намеренно (буквы — свои контуры).</summary>
    [Fact]
    public void The_Drawing_Can_Be_Rebuilt()
    {
        var icons = Path.Combine(RepoRoot(), "doc", "icons");
        Assert.True(File.Exists(Path.Combine(icons, "ai2p-icon-3-wordmark.svg")),
                    "нет исходника выбранного варианта");
        Assert.True(File.Exists(Path.Combine(icons, "ai2p-icon-3-wordmark-small.svg")),
                    "нет упрощённого рисунка для мелких размеров");
        var maker = Path.Combine(RepoRoot(), "test", "t6s0", "mkappicon.py");
        Assert.True(File.Exists(maker), "нет сборщика значка");
        var text = File.ReadAllText(maker);
        Assert.Contains("ai2p.ico", text);
        Assert.Contains("favicon.ico", text);
    }
}
