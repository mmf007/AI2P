using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-477-S0: импорт области макета из Figma. Проверяются чистые части импортёра — разбор
/// ссылки, ссылка импорта с отметками выбора, раскладка на экраны и элементы, порядок
/// подписей и вид описания, а также то, что у вида figma один секрет и он есть во всех
/// пяти словарях. Сеть не трогается.
/// </summary>
public class T477S0Tests
{
    private const string Sample =
        "https://www.figma.com/design/rqN1QJdU35Q18H5mpUmlFd/%F0%9F%8F%A0-Home-Planner-App?node-id=54965-55076&t=dTmjsqzWZefe69ZK-1";

    [Fact]
    public void Url_With_Node_Is_Parsed_And_Node_Uses_Colon()
    {
        var r = FigmaImporter.ParseUrl(Sample);
        Assert.NotNull(r);
        Assert.Equal("rqN1QJdU35Q18H5mpUmlFd", r!.FileKey);
        Assert.Equal("54965:55076", r.NodeId);
        Assert.True(r.HasNode);
        Assert.False(r.WholeMark);
        Assert.False(r.ElementsMark);
        Assert.Equal("figma:rqN1QJdU35Q18H5mpUmlFd:54965:55076", FigmaImporter.ExternalRefOf(r));
    }

    [Theory]
    [InlineData("https://www.figma.com/file/rqN1QJdU35Q18H5mpUmlFd/Name")]
    [InlineData("https://figma.com/proto/rqN1QJdU35Q18H5mpUmlFd/Name?node-id=1-2")]
    [InlineData("https://www.figma.com/design/AAAAAAAAAAAAAAAAAAAAAA/Name/branch/rqN1QJdU35Q18H5mpUmlFd/Name")]
    public void Other_Figma_Url_Forms_Are_Accepted(string url)
    {
        var r = FigmaImporter.ParseUrl(url);
        Assert.NotNull(r);
        Assert.Equal("rqN1QJdU35Q18H5mpUmlFd", r!.FileKey);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/issues/1")]
    [InlineData("https://trello.com/c/abc")]
    [InlineData("https://www.figma.com/community/file/123")]
    [InlineData("https://notfigma.com/design/rqN1QJdU35Q18H5mpUmlFd/x")]
    [InlineData("")]
    public void Foreign_Urls_Are_Not_Figma(string url)
    {
        Assert.Null(FigmaImporter.ParseUrl(url));
        Assert.False(FigmaImporter.CanRefresh(url));
    }

    [Fact]
    public void Import_Url_Keeps_Node_And_Choice_And_Drops_Tail()
    {
        var r = FigmaImporter.ParseUrl(Sample)!;
        var url = FigmaImporter.ImportUrlOf(r, wholeFile: false, elements: true);
        Assert.DoesNotContain("t=", url);
        Assert.Contains("node-id=54965-55076", url);
        Assert.Contains(FigmaImporter.ElementsMark + "=1", url);
        // по сохранённой ссылке обновление повторит тот же выбор
        var again = FigmaImporter.ParseUrl(url)!;
        Assert.True(again.ElementsMark);
        Assert.Equal("54965:55076", again.NodeId);
    }

    [Fact]
    public void Whole_File_Choice_Is_Remembered_Only_Without_Node()
    {
        var r = FigmaImporter.ParseUrl("https://www.figma.com/design/rqN1QJdU35Q18H5mpUmlFd/Name")!;
        Assert.False(r.HasNode);
        var url = FigmaImporter.ImportUrlOf(r, wholeFile: true, elements: false);
        Assert.True(FigmaImporter.ParseUrl(url)!.WholeMark);
        Assert.Equal("figma:rqN1QJdU35Q18H5mpUmlFd:*", FigmaImporter.ExternalRefOf(r));
    }

    private static FigmaImporter.Node N(string id, string type, double x, double y, double w = 100,
        double h = 100, string text = "", bool visible = true, string component = "",
        params FigmaImporter.Node[] children) =>
        new()
        {
            Id = id, Name = "n" + id, Type = type, X = x, Y = y, Width = w, Height = h,
            Characters = text, Visible = visible, ComponentId = component, Children = children.ToList(),
        };

    [Fact]
    public void Link_To_A_Frame_Is_One_Screen_Even_With_Nested_Frames()
    {
        var root = N("1", "FRAME", 0, 0, children:
        [
            N("2", "FRAME", 0, 0, children: [N("3", "TEXT", 0, 0, text: "Header")]),
            N("4", "FRAME", 0, 200, children: [N("5", "TEXT", 0, 200, text: "Body")]),
        ]);
        var frames = FigmaImporter.BuildFrames(root, elements: false);
        Assert.Single(frames);
        Assert.Equal("1", frames[0].Node.Id);
        Assert.Equal(new[] { "Header", "Body" }, frames[0].Texts);
        Assert.Empty(frames[0].Elements);
    }

    [Fact]
    public void Section_Gives_Its_Top_Level_Frames_As_Screens_Through_Groups()
    {
        var root = N("1", "SECTION", 0, 0, children:
        [
            N("2", "FRAME", 500, 0),
            N("3", "GROUP", 0, 0, children: [N("4", "FRAME", 0, 0)]),
            N("5", "FRAME", 0, 900, visible: false),
            N("6", "TEXT", 0, 0, text: "note"),
        ]);
        var ids = FigmaImporter.BuildFrames(root, elements: false).Select(f => f.Node.Id).ToList();
        Assert.Equal(new[] { "4", "2" }, ids);
    }

    [Fact]
    public void Elements_Are_Instances_And_Nested_Frames_Deduplicated_Small_Skipped()
    {
        var screen = N("1", "FRAME", 0, 0, 400, 800, children:
        [
            N("2", "INSTANCE", 0, 0, component: "C1", children: [N("3", "TEXT", 0, 0, text: "OK")]),
            N("4", "INSTANCE", 0, 100, component: "C1", children: [N("5", "TEXT", 0, 100, text: "OK")]),
            N("6", "INSTANCE", 0, 200, component: "C1", children: [N("7", "TEXT", 0, 200, text: "Cancel")]),
            N("8", "FRAME", 0, 300, children: [N("9", "INSTANCE", 0, 300, component: "C2")]),
            N("10", "FRAME", 0, 500, 4, 4),
        ]);
        var frames = FigmaImporter.BuildFrames(screen, elements: true);
        var ids = frames.Single().Elements.Select(e => e.Node.Id).ToList();
        Assert.Equal(new[] { "2", "6", "8", "9" }, ids);
    }

    [Fact]
    public void Texts_Go_Top_To_Bottom_Then_Left_To_Right_And_Skip_Hidden()
    {
        var root = N("1", "FRAME", 0, 0, children:
        [
            N("2", "TEXT", 50, 10, text: "B"),
            N("3", "TEXT", 0, 10.2, text: "A"),
            N("4", "TEXT", 0, 0, text: "Top"),
            N("5", "TEXT", 0, 50, text: "Hidden", visible: false),
            N("6", "TEXT", 0, 60, text: "   "),
        ]);
        Assert.Equal(new[] { "Top", "A", "B" }, FigmaImporter.TextsOf(root));
    }

    [Fact]
    public void Description_Puts_Captions_Under_Each_Image()
    {
        var root = N("1", "FRAME", 0, 0, children: [N("2", "TEXT", 0, 0, text: "Sign in")]);
        var frames = FigmaImporter.BuildFrames(root, elements: false);
        var text = FigmaImporter.DescriptionOf("Home Planner", "Login", "https://www.figma.com/design/k/x",
            frames, new Dictionary<string, string> { ["1"] = "api/files/raw?path=a.png" }, limited: false);
        var image = text.IndexOf("](api/files/raw?path=a.png)", StringComparison.Ordinal);
        var caption = text.IndexOf("- Sign in", StringComparison.Ordinal);
        Assert.True(image > 0 && caption > image);
        Assert.Contains("https://www.figma.com/design/k/x", text);
    }

    [Fact]
    public void Node_Is_Parsed_From_Figma_Json()
    {
        using var doc = JsonDocument.Parse("""
            {"id":"1:2","name":"Screen","type":"FRAME","absoluteBoundingBox":{"x":1,"y":2,"width":390,"height":844},
             "children":[{"id":"1:3","name":"t","type":"TEXT","characters":"Hello","visible":false,
                          "absoluteBoundingBox":{"x":1,"y":2,"width":10,"height":10}}]}
            """);
        var node = FigmaImporter.ParseNode(doc.RootElement);
        Assert.Equal("1:2", node.Id);
        Assert.Equal(390, node.Width);
        Assert.False(node.Children[0].Visible);
        Assert.Equal("Hello", node.Children[0].Characters);
    }

    [Fact]
    public void Figma_Source_Has_One_Secret_And_Token_Hint()
    {
        Assert.False(ImportKeyService.NeedsKey(ImportKinds.Figma));
        var source = new ImportSource { Kind = "figma", ParamsJson = "{}" };
        Assert.Equal(("", FigmaImporter.DefaultTokenRef), ImportKeyService.RefsOf(source));
        Assert.Equal("", FigmaImporter.SecretHint("figd_0123456789abcdefghijKLMN"));
        Assert.NotEqual("", FigmaImporter.SecretHint("ghp_notafigmatoken000000"));
    }

    [Fact]
    public void Every_Language_Has_The_Figma_Keys_And_Doc()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var lang in new[] { "ru", "en", "es", "pt", "zh-cn" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, lang + ".json")));
            for (var i = 1; i <= 28; i++)
            {
                Assert.True(doc.RootElement.TryGetProperty($"msg.figmaImporter.{i}", out _),
                    $"{lang}: нет msg.figmaImporter.{i}");
            }
            foreach (var key in new[] { "prompt.tasks.187", "prompt.tasks.188", "imports.figma.wholeFile",
                         "imports.figma.elements", "imports.figma.elementsHint", "imports.figma.noNode",
                         "imports.figma.byUrlOnly" })
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out _), $"{lang}: нет {key}");
            }
        }
    }
}
