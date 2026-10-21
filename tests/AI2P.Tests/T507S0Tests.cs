using AI2P.Server.Api;
using AI2P.UI.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-507-S0: ВСТРОЕННЫЙ РЕЖИМ ИНТЕРФЕЙСА ДЛЯ ПЛАГИНА VS CODE (этап 8.2). Разбор параметров
/// embed/view/theme/lang/project (<see cref="EmbedParams"/>), заголовки встраивания только
/// у встроенного режима (<see cref="EmbedMode.Apply"/>), язык circuit'а без записи в общий
/// (<see cref="I18nService.ForCircuit"/>) и заглушка вместо формы входа.
/// </summary>
public class T507S0Tests
{
    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    [Fact]
    public void Plain_Address_Is_Not_Embed()
    {
        Assert.Null(EmbedParams.Parse("http://localhost:5480/ai2p/acme/", Langs));
        Assert.Null(EmbedParams.Parse("http://localhost:5480/ai2p/acme/task/T-5?theme=dark", Langs));
        Assert.Null(EmbedParams.Parse("/ai2p/acme/?embed=other&view=settings", Langs));
    }

    [Fact]
    public void Embed_Address_Is_Parsed()
    {
        var p = EmbedParams.Parse(
            "http://localhost:5480/ai2p/acme/?embed=vscode&view=settings&theme=dark&lang=zh-CN&project=p1#x", Langs);
        Assert.NotNull(p);
        Assert.Equal(EmbedParams.ViewSettings, p!.View);
        Assert.Equal("dark", p.Theme);
        Assert.Equal("zh-cn", p.Lang); // имя языка — как в словарях, регистр неважен
        Assert.Equal("p1", p.ProjectId);
    }

    /// <summary>T-540-S0: плагин VS Code шлёт projectId, а не project — без этого вид project
    /// открывал текущий проект человека вместо проекта папки.</summary>
    [Fact]
    public void Embed_Address_Takes_ProjectId_From_Plugin()
    {
        Assert.Equal("p2", EmbedParams.Parse("/?embed=vscode&view=project&theme=dark&projectId=p2", Langs)!.ProjectId);
        Assert.Equal("p1", EmbedParams.Parse("/?embed=vscode&project=p1&projectId=p2", Langs)!.ProjectId);
    }

    [Theory]
    [InlineData("project", "project")]
    [InlineData("settings", "settings")]
    [InlineData("DOCS", "docs")]
    [InlineData("env", "env")]
    [InlineData("board", "project")]
    [InlineData("", "project")]
    public void View_Defaults_To_Project(string view, string expected)
    {
        var p = EmbedParams.Parse("/?embed=VSCode&view=" + view, Langs);
        Assert.Equal(expected, p!.View);
    }

    [Fact]
    public void Unknown_Theme_And_Language_Are_Dropped()
    {
        var p = EmbedParams.Parse("/task/T-3?embed=vscode&theme=blue&lang=de", Langs);
        Assert.NotNull(p);
        Assert.Null(p!.Theme);
        Assert.Null(p.Lang);
        Assert.Null(p.ProjectId);
        Assert.Equal("light", EmbedParams.Parse("/?embed=vscode&theme=Light", Langs)!.Theme);
    }

    private static DefaultHttpContext Html(string query)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString(query);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        return ctx;
    }

    [Fact]
    public void Embed_Request_Drops_XFrameOptions_And_Allows_VsCode_Frame()
    {
        var ctx = Html("?embed=vscode&view=project");
        ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN"; // так ставит Antiforgery
        EmbedMode.Apply(ctx.Request, ctx.Response);
        Assert.False(ctx.Response.Headers.ContainsKey("X-Frame-Options"));
        Assert.Equal("frame-ancestors 'self' vscode-webview: vscode-file:",
            ctx.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public void Plain_Page_Keeps_XFrameOptions_And_Gets_No_Frame_Ancestors()
    {
        var ctx = Html("?theme=dark");
        ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        EmbedMode.Apply(ctx.Request, ctx.Response);
        Assert.Equal("SAMEORIGIN", ctx.Response.Headers["X-Frame-Options"].ToString());
        Assert.False(ctx.Response.Headers.ContainsKey("Content-Security-Policy"));

        // HTML без заголовка Antiforgery всё равно получает защиту от встраивания
        var bare = Html("");
        EmbedMode.Apply(bare.Request, bare.Response);
        Assert.Equal("SAMEORIGIN", bare.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public void Need_Auth_Stub_Is_Localized_And_Posts_NeedAuth()
    {
        var html = EmbedMode.NeedAuthPage("en", dark: true);
        Assert.Contains("ai2p.needAuth", html);
        Assert.Contains("VS Code needs to be connected", html);
        Assert.DoesNotContain("<form", html);
        Assert.Contains("Нужно подключить VS Code", EmbedMode.NeedAuthPage("ru", dark: false));
    }

    [Fact]
    public void Circuit_Language_Overrides_Only_Itself()
    {
        // общий язык здесь НЕ меняется: его сеттер трогает статический Loc.Lang всех тестов
        var root = new I18nService(Path.Combine(AppContext.BaseDirectory, "i18n"), "ru");
        var plain = root.ForCircuit();
        var embed = root.ForCircuit();
        embed.UseOwnLanguage("en");
        Assert.Equal("en", embed.Lang);
        Assert.Equal("ru", plain.Lang);
        Assert.Equal("ru", root.Lang);
        Assert.Equal("VS Code needs to be connected", embed["embed.needAuth.title"]);
        Assert.Equal("Нужно подключить VS Code", plain["embed.needAuth.title"]);
        // смена языка в самой встроенной панели остаётся у неё
        embed.Lang = "es";
        Assert.Equal("es", embed.Lang);
        Assert.Equal("ru", root.Lang);
        embed.Dispose();
        plain.Dispose();
    }
}
