using AI2P.Core;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-142 (версия 1.72): ссылки — локальная и внешняя.
///
/// Ссылки система строит от имени сервера (<c>ui.hostname</c>, ТЗ гл. 10) — оно ВНЕШНЕЕ,
/// и только такую ссылку имеет смысл кому-то пересылать. В повседневной работе она же
/// и мешает: человек сидит на этом сервере, подключён к нему по петле, а ссылка уводит
/// наружу — через DNS и NAT обратно к себе же; файл при этом качается по сети целиком.
///
/// Отсюда правила: 1) у задачи ДВЕ ссылки, локальная и внешняя, а у сервера с петлевым
/// именем — одна, как раньше; 2) свои же ссылки на файлы в выводе Markdown теряют имя
/// хоста и открываются по адресу, которым человек уже пользуется; 3) у файла локальная
/// ссылка есть, только если файл здесь действительно лежит.
/// </summary>
public sealed class T142Tests
{
    // --- одна ссылка или две (ТЗ гл. 11) ---

    [Theory]
    [InlineData("http://localhost:5480/ai2p/org")]
    [InlineData("http://127.0.0.1:5480/ai2p/org")]
    [InlineData("http://[::1]:5480/ai2p/org")]
    public void Loopback_Base_Has_No_External_Link(string baseUrl) =>
        Assert.True(LinkUrls.IsLocalOnly(baseUrl), "петлевое имя — ссылка одна, как раньше");

    [Theory]
    [InlineData("http://мойпк:5480/ai2p/org")]
    [InlineData("https://ai2p.example.com/ai2p/org")]
    [InlineData("http://192.168.1.7:5480/ai2p/org")]
    public void Named_Base_Has_Two_Links(string baseUrl) =>
        Assert.False(LinkUrls.IsLocalOnly(baseUrl), "имя не петлевое — нужны обе ссылки");

    [Fact]
    public void Local_Url_Keeps_Protocol_Port_And_Path()
    {
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp",
            LinkUrls.ToLocal("http://мойпк:5480/ai2p/mmfgrp"));
        // порт по умолчанию в исходном адресе не назван — не должен появиться и в локальном
        Assert.Equal("https://localhost/ai2p/mmfgrp",
            LinkUrls.ToLocal("https://ai2p.example.com/ai2p/mmfgrp"));
    }

    [Fact]
    public void Task_Links_Differ_Only_By_Host()
    {
        const string baseUrl = "http://мойпк:5480/ai2p/mmfgrp";
        const string id = "377fa906-9929-4838-a380-25f49f7d2dd0";
        Assert.Equal($"http://localhost:5480/ai2p/mmfgrp/task/{id}",
            $"{LinkUrls.ToLocal(baseUrl)}/task/{id}");
    }

    // --- свой адрес узнаётся, чужой не трогается (T-142) ---

    [Fact]
    public void Own_Absolute_Url_Becomes_Path()
    {
        const string own = "http://мойпк:5480/ai2p";
        Assert.Equal("/ai2p/org/api/files/raw?path=a.png",
            LinkUrls.OwnPathOf("http://мойпк:5480/ai2p/org/api/files/raw?path=a.png", own));
        // петля — это тоже мы: ссылка, набранная через localhost, абсолютной остаться не должна
        Assert.Equal("/ai2p/org/api/files/raw?path=a.png",
            LinkUrls.OwnPathOf("http://localhost:5480/ai2p/org/api/files/raw?path=a.png",
                "http://localhost:5480/ai2p"));
    }

    [Theory]
    [InlineData("http://другойпк:5480/ai2p/org/api/files/raw?path=a.png")] // другой сервер
    [InlineData("http://мойпк:5481/ai2p/org/api/files/raw?path=a.png")]    // другой порт
    [InlineData("https://example.com/a.png")]                              // внешний сайт
    public void Foreign_Url_Is_Left_As_Is(string url) =>
        Assert.Null(LinkUrls.OwnPathOf(url, "http://мойпк:5480/ai2p"));

    // ПРОТОКОЛ БОЛЬШЕ НЕ РАЗЛИЧАЕТ СВОЁ И ЧУЖОЕ (T-196-S0): сервер, переведённый на https,
    // остаётся собой, а ссылки в накопленных заданиях написаны по http. До этой правки
    // адрес https на том же хосте и порту считался чужим (проверка стояла здесь же).
    [Theory]
    [InlineData("https://мойпк:5480/ai2p/org/api/files/raw?path=a.png", "http://мойпк:5480/ai2p")]
    [InlineData("http://мойпк:5480/ai2p/org/api/files/raw?path=a.png", "https://мойпк:5480/ai2p")]
    // порт по умолчанию у http и https разный (80 и 443), а адрес — тот же самый
    [InlineData("https://мойпк/ai2p/org/api/files/raw?path=a.png", "http://мойпк/ai2p")]
    // корень сайта в сравнении не участвует вовсе: путь ссылки может быть любым
    [InlineData("http://мойпк:5480/other/org/api/files/raw?path=a.png", "http://мойпк:5480/ai2p")]
    public void Own_Url_Is_Found_Regardless_Of_Protocol(string url, string own) =>
        Assert.NotNull(LinkUrls.OwnPathOf(url, own));

    // --- ОТНОСИТЕЛЬНАЯ ССЫЛКА (T-196-S0) ---

    [Theory]
    [InlineData("http://мойпк:5480/ai2p/org/task/T-18", "/ai2p/org/task/T-18")]
    [InlineData("https://ai2p.example.com/ai2p/org/api/files/raw?path=a.png",
        "/ai2p/org/api/files/raw?path=a.png")]
    // уже относительная от корня — отдаётся как есть
    [InlineData("/ai2p/org/task/T-18", "/ai2p/org/task/T-18")]
    // относительная СТРАНИЦЫ и не-http адрес: корень выдумывать нечем
    [InlineData("api/files/raw?path=a.png", "")]
    [InlineData("mailto:mike@example.com", "")]
    [InlineData("", "")]
    public void Relative_Link_Is_Taken_From_The_Site_Root(string url, string expected) =>
        Assert.Equal(expected, LinkUrls.RelativeOf(url));

    // --- разбор наших файловых ссылок (T-142) ---

    [Fact]
    public void Project_File_Link_Is_Parsed()
    {
        var link = FileLinks.Parse(
            "http://мойпк:5480/ai2p/org/api/files/project?projectId=P1&path=doc%2F%D0%BE%D1%82%D1%87%D1%91%D1%82.md");
        Assert.NotNull(link);
        Assert.Equal(FileLinks.Project, link!.Kind);
        Assert.Equal("P1", link.ProjectId);
        Assert.Equal("doc/отчёт.md", link.Path);
        // путь в ссылке ОТНОСИТЕЛЬНЫЙ (от каталога проекта) — потому та же ссылка и работает
        // на соседнем сервере, где каталог проекта другой
        Assert.Equal("api/files/project?projectId=P1&path=doc%2F%D0%BE%D1%82%D1%87%D1%91%D1%82.md",
            FileLinks.Relative(link));
        Assert.Equal(
            "http://мойпк:5480/ai2p/org/api/files/project?projectId=P1&path=doc%2F%D0%BE%D1%82%D1%87%D1%91%D1%82.md",
            FileLinks.External(link, "http://мойпк:5480/ai2p/org"));
    }

    [Fact]
    public void Raw_File_Link_Is_Parsed()
    {
        var link = FileLinks.Parse("api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png");
        Assert.NotNull(link);
        Assert.Equal(FileLinks.Raw, link!.Kind);
        Assert.Equal("projects/p/uploads/a.png", link.Path);
        Assert.Equal("", link.ProjectId);
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("https://trello.com/c/tYY9LhmR")]
    [InlineData("file:///C:/tmp/a.png")]
    [InlineData("api/files/project?projectId=P1")] // без пути — не файл
    [InlineData("")]
    public void Foreign_Link_Is_Not_Ours(string url) => Assert.Null(FileLinks.Parse(url));

    // --- вывод Markdown: свои ссылки открываются локально (T-142) ---

    // свой адрес передаётся параметром, а не подменой Md.OwnBaseUrl: классы тестов идут
    // параллельно, и подмена статического поля задела бы соседей

    [Fact]
    public void Md_Rewrites_Own_File_Links_Only()
    {
        var html = Md.ToHtml(
            "[своя](http://myhost:5480/ai2p/org/api/files/project?projectId=P&path=a.md) " +
            "[чужая](http://otherhost:5480/ai2p/org/api/files/project?projectId=P&path=a.md) " +
            "[сайт](https://example.com/a.md)",
            "http://myhost:5480/ai2p");
        // своя — без имени хоста: браузер достроит её адресом, по которому уже подключён
        Assert.Contains("href=\"/ai2p/org/api/files/project?projectId=P&amp;path=a.md\"", html);
        // чужая осталась как была: её файла здесь может не быть, ломать ссылку нельзя
        Assert.Contains("http://otherhost:5480/ai2p/org/api/files/project", html);
        Assert.Contains("https://example.com/a.md", html);
        // файловые ссылки по-прежнему открываются новой закладкой (todo19/todo29)
        Assert.Contains("target=\"_blank\"", html);
    }

    [Fact]
    public void Md_Without_Own_Base_Changes_Nothing()
    {
        var html = Md.ToHtml("[своя](http://myhost:5480/ai2p/org/api/files/raw?path=a.png)", "");
        Assert.Contains("http://myhost:5480/ai2p/org/api/files/raw?path=a.png", html);
    }

    [Fact]
    public void Md_Rewrites_Images_Too()
    {
        // имя из кириллицы рендер приводит к punycode — своим оно опознаётся всё равно
        var html = Md.ToHtml("![кадр](http://мойпк:5480/ai2p/org/api/files/raw?path=a.png)",
            "http://мойпк:5480/ai2p");
        Assert.Contains("src=\"/ai2p/org/api/files/raw?path=a.png\"", html);
    }
}
