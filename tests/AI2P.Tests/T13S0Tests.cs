using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-13-S0 «показ картинок»: картинка, положенная агентом в чат, видна ТОЛЬКО на том
/// сервере, где выполнялась задача.
///
/// Случай заказчика: агент отработал на S0 и положил в чат пять картинок ссылками вида
/// <c>http://localhost:5480/ai2p/org/api/files/project?projectId=…&amp;path=doc/icons/…</c>.
/// На дирижёре S1 их не видно, и с телефона, смотрящего на внешний адрес дирижёра, — тоже.
///
/// Причин ровно две, и чинятся они по отдельности:
/// <list type="number">
/// <item>ФАЙЛА ЗДЕСЬ НЕТ. Он лежит в ПАПКЕ ПРОЕКТА сервера S0: папка своя на каждом сервере
/// и целиком не реплицируется (её выравнивают git'ом). Теперь сервер, у которого файла нет,
/// спрашивает его у соседей по кластеру и отдаёт браузеру потоком;</item>
/// <item>ССЫЛКА ВЕДЁТ НЕ ТУДА. Петлевой адрес на другом компьютере означает совсем другую
/// машину (а с телефона — сам телефон), адрес соседа уводит в его сеть, где у человека нет
/// входа. Теперь такая ссылка сворачивается в ПУТЬ и открывается через тот сервер, к
/// которому человек уже подключён.</item>
/// </list>
/// </summary>
public sealed class T13S0Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t13s0-" + Guid.NewGuid().ToString("N"));

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

    private const string Own = "http://мойпк:5480/ai2p";
    private const string Peer = "http://conductor:5480/ai2p";

    /// <summary>Ссылка на файл папки проекта — ровно в том виде, в каком её пишет агент.</summary>
    private static string ProjectLink(string baseUrl) =>
        $"{baseUrl}/org/api/files/project?projectId=p-1&path=doc/icons/a.png";

    // ---------- 1. чей это адрес: свой, соседа, петлевой, чужой ----------

    [Fact]
    public void Own_Address_Is_Still_Collapsed_To_A_Path()
    {
        var path = LinkUrls.ClusterPathOf(ProjectLink(Own), LinkUrls.OwnBases(Own));
        Assert.Equal("/ai2p/org/api/files/project?projectId=p-1&path=doc/icons/a.png", path);
    }

    [Theory]
    [InlineData("http://localhost:5480")]
    [InlineData("http://localhost:9999")]      // порт соседа — не наш
    [InlineData("http://127.0.0.1:5480")]
    [InlineData("http://[::1]:5480")]
    public void A_Loopback_Address_Is_Collapsed_Whatever_The_Port(string baseUrl)
    {
        // «localhost» в накопленных ссылках означает тот сервер, ГДЕ ССЫЛКУ НАПИСАЛИ:
        // на любом другом компьютере она ведёт не туда, и оставлять её абсолютной нельзя
        var path = LinkUrls.ClusterPathOf(ProjectLink(baseUrl + "/ai2p"), LinkUrls.OwnBases(Own));
        Assert.Equal("/ai2p/org/api/files/project?projectId=p-1&path=doc/icons/a.png", path);
    }

    [Fact]
    public void A_Neighbour_Address_Is_Collapsed_Too()
    {
        var path = LinkUrls.ClusterPathOf(ProjectLink(Peer), LinkUrls.OwnBases(Own), [Peer]);
        Assert.Equal("/ai2p/org/api/files/project?projectId=p-1&path=doc/icons/a.png", path);
    }

    [Fact]
    public void An_Unknown_Server_Is_Left_Alone()
    {
        // сервер не наш и не из кластера — ссылку не трогаем: она может быть рабочей,
        // а файла такого у нас нет и не будет
        Assert.Null(LinkUrls.ClusterPathOf(ProjectLink("http://чужой:5480/ai2p"),
            LinkUrls.OwnBases(Own), [Peer]));
        Assert.Null(LinkUrls.ClusterPathOf("https://example.com/api/files/raw?path=a.png",
            LinkUrls.OwnBases(Own), [Peer]));
    }

    [Fact]
    public void Neighbours_Are_Not_Needed_To_Collapse_A_Loopback_Link()
    {
        // список соседей приезжает с состоянием и в первый миг пуст — петлевая ссылка
        // обязана сворачиваться и без него
        Assert.NotNull(LinkUrls.ClusterPathOf(ProjectLink("http://localhost:5480/ai2p"),
            LinkUrls.OwnBases(Own)));
    }

    // ---------- 2. показ Markdown: картинка соседа открывается через нас ----------

    [Fact]
    public void A_Picture_Written_On_Another_Server_Is_Shown_Through_This_One()
    {
        // дословно то, что положил в чат агент T-288: тег <img> с петлевым адресом
        var chat = $"<img src='{ProjectLink("http://localhost:5480/ai2p")}' alt='Вариант 1' width='256'>";
        var html = Md.ToHtml(chat, Own, "", [Peer]);

        Assert.Contains("src=\"/ai2p/org/api/files/project?projectId=p-1&amp;path=doc/icons/a.png\"", html);
        Assert.DoesNotContain("localhost:5480", html);
    }

    [Fact]
    public void A_Link_To_A_Neighbours_File_Is_Collapsed_Too()
    {
        var html = Md.ToHtml($"[1024 px]({ProjectLink(Peer)})", Own, "", [Peer]);

        Assert.Contains("href=\"/ai2p/org/api/files/project?projectId=p-1&amp;path=doc/icons/a.png\"", html);
        Assert.DoesNotContain("conductor", html);
    }

    [Fact]
    public void A_Neighbour_Named_In_Cyrillic_Is_Recognised_Too()
    {
        // рендер Markdown приводит имя хоста к punycode («дирижёр» → «xn--…»), поэтому
        // сравнение идёт по IdnHost — иначе такой сосед своим бы не опознался (T-142)
        var path = LinkUrls.ClusterPathOf("http://xn--d1aefa3bc2j/ai2p/org/api/files/raw?path=a.png",
            LinkUrls.OwnBases(Own), ["http://дирижёр/ai2p"]);

        Assert.Equal("/ai2p/org/api/files/raw?path=a.png", path);
    }

    [Fact]
    public void A_Foreign_Site_Is_Still_Opened_As_It_Is()
    {
        var html = Md.ToHtml("[скачать](https://example.com/api/files/raw?path=a.png)", Own, "", [Peer]);

        Assert.Contains("https://example.com/api/files/raw?path=a.png", html);
        Assert.Contains("target=\"_blank\"", html);
    }

    [Fact]
    public void Only_File_Links_Are_Touched()
    {
        // ссылка на ЗАДАЧУ соседнего сервера — не файловая: её мы не трогаем, задача
        // у соседа своя и открывать её надо там (T-142)
        var html = Md.ToHtml($"[задача]({Peer}/org/task/t-1)", Own, "", [Peer]);

        Assert.Contains($"{Peer}/org/task/t-1", html);
    }

    // ---------- 3. путь в ссылке на файл папки проекта ----------

    [Theory]
    [InlineData("doc/icons/a.png")]
    [InlineData("a.png")]
    [InlineData("doc\\icons\\a.png")]
    public void A_Relative_Path_Is_Good_Enough_To_Ask_A_Neighbour(string path) =>
        Assert.True(ProjectFiles.IsRelative(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../secrets.json")]
    [InlineData("doc/../../secrets.json")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\сервер\\общий\\a.png")]
    public void A_Path_Leading_Out_Is_Never_Asked_For(string path) =>
        // папки проекта на этом сервере может не быть вовсе, и тогда обычная проверка
        // «не вышли ли за край» ответить не может — ей не от чего считать
        Assert.False(ProjectFiles.IsRelative(path));

    // ---------- 4. поход к соседу ----------

    [Fact]
    public async Task Without_Neighbours_Nobody_Is_Called()
    {
        using var registry = Registry();
        var remote = new RemoteFileService(registry);

        // сервер один — спрашивать не у кого, и это не ошибка: ссылка на несуществующий
        // файл отвечает тем же 404, что и раньше
        var file = await remote.FetchAsync(new Organization { Id = "org-1", Code = "org" },
            FileLinks.Project, "p-1", "doc/icons/a.png");

        Assert.Null(file);
    }

    [Fact]
    public async Task The_Answer_Is_Remembered_So_The_Neighbours_Are_Not_Called_On_Every_Picture()
    {
        using var registry = Registry();
        var remote = new RemoteFileService(registry);
        var org = new Organization { Id = "org-1", Code = "org" };

        Assert.Null(await remote.FetchAsync(org, FileLinks.Project, "p-1", "doc/icons/a.png"));
        // второй раз ответ берётся из памяти — иначе страница с десятком картинок звонила бы
        // по кластеру десять раз подряд
        Assert.Null(await remote.FetchAsync(org, FileLinks.Project, "p-1", "doc/icons/a.png"));
        Assert.True(RemoteFileService.MissingTtl < RemoteFileService.FoundTtl,
            "«ни у кого нет» помним недолго: файл у соседа появляется в любой момент");
    }

    // ---------- 5. файл едет по тому каналу, который и так работает (повторный заход) ----------

    [Fact]
    public void A_Link_In_The_Text_Names_The_Project_And_The_Path()
    {
        // ровно то, что пишет агент: тег картинки в одинарных кавычках и ссылка рядом
        var text = "<img src='http://localhost:5480/ai2p/org/api/files/project"
                   + "?projectId=bd47a08f&path=doc/icons/a.png' width='256'>\n"
                   + "[1024 px](http://localhost:5480/ai2p/org/api/files/project"
                   + "?projectId=bd47a08f&path=doc/icons/b-1024.png)";

        var links = LinkedFileShare.LinksIn(text);

        Assert.Equal(2, links.Count);
        Assert.Equal(("bd47a08f", "doc/icons/a.png"), links[0]);
        Assert.Equal(("bd47a08f", "doc/icons/b-1024.png"), links[1]);
    }

    [Fact]
    public void An_Escaped_Ampersand_And_A_Percent_Encoded_Path_Are_Understood()
    {
        // «&amp;» — как ссылка выглядит в готовом HTML; кириллица в пути приходит закодированной
        var text = "<img src=\"/ai2p/org/api/files/project?projectId=p-1&amp;"
                   + "path=doc/%D0%B7%D0%BD%D0%B0%D1%87%D0%BE%D0%BA.png\" />";

        var links = LinkedFileShare.LinksIn(text);

        Assert.Single(links);
        Assert.Equal(("p-1", "doc/значок.png"), links[0]);
    }

    [Fact]
    public void A_Text_Without_Links_Costs_Nothing()
    {
        Assert.Empty(LinkedFileShare.LinksIn("обычное сообщение чата"));
        Assert.Empty(LinkedFileShare.LinksIn(null));
    }

    [Fact]
    public void The_Copy_Lies_In_The_Replicated_Data_Directory()
    {
        // каталог данных организации реплицируется в ОБЕ стороны и без звонков дирижёра —
        // на этом и держится починка: копия доезжает даже от сервера за NAT
        Assert.Equal("shared/project/p-1/doc/icons/a.png",
            LinkedFileShare.RelOf("p-1", "doc/icons/a.png"));
        Assert.Equal("shared/project/p-1/doc/icons/a.png",
            LinkedFileShare.RelOf("p-1", "doc\\icons\\a.png"));
    }

    [Theory]
    [InlineData("p-1", "../../secrets.json")]
    [InlineData("p-1", "C:\\Windows\\win.ini")]
    [InlineData("p-1", "")]
    [InlineData("../org", "a.png")]              // проект тоже приходит из ссылки
    [InlineData("p 1/../..", "a.png")]
    public void A_Path_Leading_Out_Is_Never_Shared(string projectId, string path) =>
        Assert.Null(LinkedFileShare.RelOf(projectId, path));

    [Fact]
    public void A_Big_File_Is_Not_Shared_But_Asked_For()
    {
        // копия ложится на КАЖДЫЙ сервер, а видео медиа-задания измеряется гигабайтами:
        // такие файлы остаются за походом к соседу — там они едут потоком мимо диска
        var limit = LinkedFileShare.MaxBytes;
        Assert.True(limit > 0 && limit <= 64L * 1024 * 1024);
    }

    /// <summary>Реестр «сервера» с пустой серверной БД (каркас T-174).</summary>
    private OrgRegistry Registry()
    {
        var deps = new OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: new SecretStore(Path.Combine(_dir, "secrets.json")),
            LocalModels: new LocalModelProcessService(),
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => "",
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => true,
            LocalServerId: () => "unid-S0",
            LocalCode: _ => "S0",
            ServerCode: (_, _) => "S1",
            ServerName: _ => "сервер");
        return new OrgRegistry(Path.Combine(_dir, "server"), "server.db", deps);
    }
}
