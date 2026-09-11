using System.Globalization;
using System.Text.RegularExpressions;
using AI2P.UI;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-209 (версия 1.82): ВНЕШНИЙ ВИД ЗНАЧКОВ.
///
/// 1. Кнопка «новая задача» и кнопки «добавить» справочников рисовались залитым
///    кружком (<c>Variant="Variant.Filled" Color="Color.Primary"</c>) и выбивались
///    из ряда соседних кнопок-иконок («обновить», «править», «удалить»). Теперь
///    они такие же, как все: обычная кнопка-иконка без заливки и без цвета.
///    Исключения оставлены осознанно и перечислены в <see cref="AllowedFilled"/>:
///    это главные кнопки формы, а не кнопки панели.
/// 2. «Запустить иерархию» — свой значок <see cref="Ai2pIcons.TripleArrowRight"/>:
///    ТРИ стрелки, направленные ВПРАВО (был Material с двумя стрелками вниз).
///
/// Разметка проверяется по файлам (как в <see cref="T157Tests"/>), внешний вид —
/// живой проверкой в браузере (<c>test/t209</c>).
/// </summary>
public sealed class T209Tests
{
    /// <summary>Кнопки-иконки, которым заливка оставлена: главное действие формы.</summary>
    private static readonly string[] AllowedFilled =
    [
        "Icons.Material.Filled.Send",   // отправка сообщения в чат — рядом с полем ввода
        "Icons.Material.Filled.Search", // «показать» в панели отбора расходов
    ];

    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string UiRoot() =>
        Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.UI");

    /// <summary>Все элементы &lt;MudIconButton … /&gt; файла разметки, целиком.</summary>
    private static IEnumerable<string> IconButtons(string markup) =>
        Regex.Matches(markup, "<MudIconButton\\b.*?/>", RegexOptions.Singleline)
             .Select(m => m.Value);

    [Fact]
    public void Icon_Buttons_Of_Panels_Are_Not_Filled()
    {
        var bad = new List<string>();
        foreach (var file in Directory.GetFiles(UiRoot(), "*.razor", SearchOption.AllDirectories))
        {
            foreach (var button in IconButtons(File.ReadAllText(file)))
            {
                if (!button.Contains("Variant=\"Variant.Filled\"")) continue;
                if (AllowedFilled.Any(button.Contains)) continue;
                bad.Add($"{Path.GetFileName(file)}: {button.Split('\n')[0].Trim()}");
            }
        }

        Assert.True(bad.Count == 0,
            "кнопки-иконки с заливкой (должны быть как все остальные):\n" + string.Join("\n", bad));
    }

    /// <summary>«Новая задача» и «добавить» справочников — без цвета: как «обновить» рядом.</summary>
    [Theory]
    [InlineData("Components/TasksView.razor", "data-newtask")]
    [InlineData("Components/TaskCardView.razor", "data-subtask-add")]
    [InlineData("Components/TemplatesView.razor", "NewTemplateAsync")]
    [InlineData("Components/ProjectsView.razor", "OnClick=\"AddAsync\"")]
    [InlineData("Components/ExecutorsView.razor", "EditAsync(null)")]
    [InlineData("Components/SettingsView.razor", "EditModelAsync(null)")]
    [InlineData("Components/SettingsView.razor", "AddSkillAsync")]
    [InlineData("Components/SettingsView.razor", "EditStatusAsync(null)")]
    [InlineData("Components/SettingsView.razor", "EditActionAsync(null)")]
    public void The_Add_Button_Looks_Like_The_Others(string file, string marker)
    {
        var path = Path.Combine(UiRoot(), file.Replace('/', Path.DirectorySeparatorChar));
        var button = IconButtons(File.ReadAllText(path)).SingleOrDefault(b => b.Contains(marker));

        Assert.NotNull(button);
        Assert.DoesNotContain("Variant=", button);
        Assert.DoesNotContain("Color=", button);
    }

    // ---------- значок «запустить иерархию» ----------

    /// <summary>Точки одного &lt;path d="M … L … Z"&gt; значка.</summary>
    private static List<(double X, double Y)> Points(string path)
    {
        var numbers = Regex.Matches(path, "-?\\d+(?:\\.\\d+)?")
                           .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
                           .ToList();
        Assert.True(numbers.Count % 2 == 0, "координаты идут парами");
        return Enumerable.Range(0, numbers.Count / 2)
                         .Select(i => (numbers[i * 2], numbers[i * 2 + 1]))
                         .ToList();
    }

    [Fact]
    public void The_Hierarchy_Icon_Has_Three_Arrows_Pointing_Right()
    {
        var paths = Regex.Matches(Ai2pIcons.TripleArrowRight, "d=\"([^\"]+)\"")
                         .Select(m => m.Groups[1].Value)
                         .ToList();

        // три стрелки, а не две
        Assert.Equal(3, paths.Count);

        var tips = new List<double>();
        foreach (var path in paths)
        {
            var points = Points(path);
            var maxX = points.Max(p => p.X);

            // остриё стрелки — самая правая точка и стоит на средней линии:
            // у стрелки, смотрящей вниз, остриё было бы самым НИЖНИМ, а не правым
            Assert.Contains(points, p => p.X == maxX && Math.Abs(p.Y - 12) < 0.01);
            Assert.True(points.Max(p => p.Y) < 24 && points.Min(p => p.Y) > 0, "значок влезает в поле 24×24");
            Assert.True(maxX <= 24, "значок влезает в поле 24×24");
            tips.Add(maxX);
        }

        // стрелки стоят одна за другой слева направо
        Assert.Equal(tips.OrderBy(x => x).ToList(), tips);
        Assert.True(tips.Distinct().Count() == 3, "остриё у каждой стрелки своё");
    }

    [Fact]
    public void The_Card_Uses_The_Three_Arrow_Icon()
    {
        var card = File.ReadAllText(Path.Combine(UiRoot(), "Components", "TaskCardView.razor"));

        Assert.Contains("Ai2pIcons.TripleArrowRight", card);
        Assert.DoesNotContain("KeyboardDoubleArrowDown", card);
    }
}
