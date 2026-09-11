using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Server;
using AI2P.Server.Api;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-2-S1: ВТОРОЙ АДРЕС СЕРВЕРА — <c>hostname2</c>/<c>port2</c> (ТЗ гл. 10, гл. 11).
///
/// У сервера за NAT адресов два. Внутри сети его зовут по адресу локальной сети
/// (<c>hostname</c>) — этот же адрес видят соседи по кластеру, и заменить его публичным
/// нельзя: свой же трафик пошёл бы наружу через роутер и обратно. А человек заходит
/// с телефона по публичному имени роутера с пробросом порта, и ссылка, построенная
/// от внутреннего адреса, у него не открывается.
///
/// Отсюда правила:
/// <list type="number">
/// <item><c>hostname2</c> пусто (умолчание) — всё как раньше, одна пара имя/порт;</item>
/// <item>есть <c>hostname2</c>, нет <c>port2</c> — ссылка <c>protocol://hostname2:port</c>;</item>
/// <item>есть оба — ссылка <c>protocol://hostname2:port2</c>;</item>
/// <item><c>port2</c> без <c>hostname2</c> не значит ничего и не сохраняется;</item>
/// <item>ЛОКАЛЬНАЯ (петлевая) ссылка берёт порт ЭТОГО компьютера, а не проброшенный;</item>
/// <item>своей узнаётся ссылка по ЛЮБОМУ из двух адресов — иначе накопленные в задачах
/// ссылки по внутреннему имени уводили бы в сеть за файлом с этого же диска.</item>
/// </list>
/// </summary>
public sealed class T2S1Tests
{
    private static Ai2pConfig.UiSettings Ui(string hostname, int port, string hostname2 = "",
        int? port2 = null) => new()
    {
        Protocol = "http",
        Hostname = hostname,
        Port = port,
        Hostname2 = hostname2,
        Port2 = port2,
        BasePath = "/ai2p",
    };

    // --- внешний адрес: какая пара идёт в ссылку (ТЗ гл. 11) ---

    [Fact]
    public void Without_Second_Host_Links_Are_Built_As_Before()
    {
        var ui = Ui("192.168.1.7", 5480);
        Assert.False(ui.HasExternalAddress);
        Assert.Equal("http://192.168.1.7:5480/ai2p", ui.PublicBaseUrl());
        Assert.Equal(ui.PublicBaseUrl(), ui.OwnBaseUrl());
    }

    [Fact]
    public void Second_Host_Without_Second_Port_Keeps_The_First_Port()
    {
        var ui = Ui("192.168.1.7", 5480, hostname2: "home.example.com");
        Assert.True(ui.HasExternalAddress);
        Assert.Equal("http://home.example.com:5480/ai2p", ui.PublicBaseUrl());
    }

    [Fact]
    public void Second_Host_With_Second_Port_Uses_Both()
    {
        var ui = Ui("192.168.1.7", 5480, hostname2: "home.example.com", port2: 8080);
        Assert.Equal("http://home.example.com:8080/ai2p", ui.PublicBaseUrl());
        Assert.Equal("home.example.com", ui.ExternalHostname());
        Assert.Equal(8080, ui.ExternalPort());
    }

    [Fact]
    public void Second_Port_Without_Second_Host_Is_Ignored()
    {
        var ui = Ui("192.168.1.7", 5480, port2: 8080);
        Assert.False(ui.HasExternalAddress);
        Assert.Equal("http://192.168.1.7:5480/ai2p", ui.PublicBaseUrl());
        Assert.Equal(5480, ui.ExternalPort());
    }

    [Fact]
    public void Own_Base_Url_Is_Always_The_First_Pair()
    {
        // адрес для соседей по кластеру и для браузера в своей сети — внутренний:
        // публичное имя роутера завернуло бы местный трафик наружу
        var ui = Ui("192.168.1.7", 5480, hostname2: "home.example.com", port2: 8080);
        Assert.Equal("http://192.168.1.7:5480/ai2p", ui.OwnBaseUrl());
    }

    // --- локальная ссылка: порт ЭТОГО компьютера (T-142 + T-2-S1) ---

    [Fact]
    public void Local_Url_Takes_The_Local_Port_Not_The_Forwarded_One()
    {
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp",
            LinkUrls.ToLocal("http://home.example.com:8080/ai2p/mmfgrp", 5480));
        // порта по умолчанию во внешней ссылке нет, а слушаем мы 5480 — он должен появиться
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp",
            LinkUrls.ToLocal("http://home.example.com/ai2p/mmfgrp", 5480));
    }

    [Fact]
    public void Local_Url_Without_Local_Port_Works_As_Before()
    {
        Assert.Equal("http://localhost:5480/ai2p/mmfgrp",
            LinkUrls.ToLocal("http://мойпк:5480/ai2p/mmfgrp"));
        Assert.Equal("https://localhost/ai2p/mmfgrp",
            LinkUrls.ToLocal("https://ai2p.example.com/ai2p/mmfgrp", 0));
    }

    // --- своя ссылка узнаётся по ОБОИМ адресам (T-2-S1) ---

    private const string External = "http://home.example.com:8080/ai2p";
    private const string Own = "http://192.168.1.7:5480/ai2p";

    [Theory]
    // ссылка по внешнему адресу — так их пишет ИИ-агент теперь
    [InlineData("http://home.example.com:8080/ai2p/org/api/files/raw?path=a.png")]
    // ссылка по внутреннему адресу — так они написаны во всех задачах до второго адреса
    [InlineData("http://192.168.1.7:5480/ai2p/org/api/files/raw?path=a.png")]
    // петлевая ссылка с портом ЭТОГО компьютера
    [InlineData("http://localhost:5480/ai2p/org/api/files/raw?path=a.png")]
    public void Both_Own_Addresses_Are_Recognized(string url) =>
        Assert.Equal("/ai2p/org/api/files/raw?path=a.png",
            LinkUrls.OwnPathOf(url, LinkUrls.OwnBases(External, Own)));

    [Theory]
    [InlineData("http://other:5480/ai2p/org/api/files/raw?path=a.png")]
    // тот же хост, но чужой порт — это другой сервер
    [InlineData("http://192.168.1.7:6480/ai2p/org/api/files/raw?path=a.png")]
    // петлевой адрес с ПРОБРОШЕННЫМ портом: на этом компьютере его никто не слушает
    [InlineData("http://localhost:8080/ai2p/org/api/files/raw?path=a.png")]
    public void Foreign_Address_Stays_Foreign(string url) =>
        Assert.Null(LinkUrls.OwnPathOf(url, LinkUrls.OwnBases(External, Own)));

    [Fact]
    public void Second_Own_Address_Is_Optional()
    {
        // второго адреса нет — поведение прежнее (T-142)
        Assert.Equal("/ai2p/org/api/files/raw?path=a.png",
            LinkUrls.OwnPathOf("http://мойпк:5480/ai2p/org/api/files/raw?path=a.png",
                "http://мойпк:5480/ai2p"));
        Assert.Null(LinkUrls.OwnPathOf("http://192.168.1.7:5480/ai2p/org/api/files/raw?path=a.png",
            "http://мойпк:5480/ai2p"));
    }

    [Fact]
    public void Md_Rewrites_Links_Of_Both_Own_Addresses()
    {
        var html = Md.ToHtml(
            "[внешняя](http://home.example.com:8080/ai2p/org/api/files/raw?path=a.png) " +
            "[внутренняя](http://192.168.1.7:5480/ai2p/org/api/files/raw?path=b.png) " +
            "[чужая](http://other:5480/ai2p/org/api/files/raw?path=c.png)",
            External, Own);
        Assert.Contains("href=\"/ai2p/org/api/files/raw?path=a.png\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ai2p/org/api/files/raw?path=b.png\"", html, StringComparison.Ordinal);
        Assert.Contains("http://other:5480/ai2p/org/api/files/raw?path=c.png", html, StringComparison.Ordinal);
    }

    // --- проверка значений формы локального сервера (ТЗ гл. 10) ---

    private static ServerSettingsDto Dto(string hostname2 = "", int? port2 = null) => new()
    {
        Port = 5480,
        Protocol = "http",
        Hostname = "192.168.1.7",
        Hostname2 = hostname2,
        Port2 = port2,
        BindAddress = "0.0.0.0",
    };

    [Fact]
    public void Empty_Second_Address_Is_Valid() =>
        ApiEndpoints.ValidateLocalServer(Dto());

    [Fact]
    public void Second_Address_Is_Validated_Like_The_First()
    {
        ApiEndpoints.ValidateLocalServer(Dto("home.example.com", 8080));
        // адрес, а не URL: слэш в имени хоста — мусор из автоподстановки браузера
        Assert.Throws<ArgumentException>(() =>
            ApiEndpoints.ValidateLocalServer(Dto("http://home.example.com/", 8080)));
        Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(Dto("home", 0)));
        Assert.Throws<ArgumentException>(() => ApiEndpoints.ValidateLocalServer(Dto("home", 70000)));
    }

    // --- config.json: параметр переживает запись и обновление версии ---

    [Fact]
    public void Config_Keeps_Second_Address()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai2p-t2s1-" + Guid.NewGuid().ToString("N"),
            "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var config = new Ai2pConfig();
            config.Ui.Hostname = "192.168.1.7";
            config.Ui.Hostname2 = "home.example.com";
            config.Ui.Port2 = 8080;
            config.Save(path);
            var loaded = Ai2pConfig.Load(path);
            Assert.Equal("home.example.com", loaded.Ui.Hostname2);
            Assert.Equal(8080, loaded.Ui.Port2);
            Assert.Equal("http://home.example.com:8080/ai2p", loaded.Ui.PublicBaseUrl());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Update_Brings_The_New_Keys_And_Keeps_User_Values()
    {
        // конфигурация ставится слиянием: новый файл — основа, значения пользователя сверху
        var merged = ConfigMerge.Merge(
            """{"ui":{"hostname":"localhost","hostname2":"","port2":null,"port":5480}}""",
            """{"ui":{"hostname":"192.168.1.7","port":5480}}""");
        Assert.Contains("\"hostname2\"", merged, StringComparison.Ordinal);
        Assert.Contains("192.168.1.7", merged, StringComparison.Ordinal);
        // а уже настроенный второй адрес обновление не теряет
        var kept = ConfigMerge.Merge(
            """{"ui":{"hostname":"localhost","hostname2":"","port2":null,"port":5480}}""",
            """{"ui":{"hostname":"192.168.1.7","hostname2":"home.example.com","port2":8080}}""");
        Assert.Contains("home.example.com", kept, StringComparison.Ordinal);
        Assert.Contains("8080", kept, StringComparison.Ordinal);
    }

    /// <summary>Тексты новых полей формы есть в ОБОИХ словарях (T-180): состав словарей
    /// обязан сходиться, иначе на другом языке в поле окажется сам ключ.</summary>
    [Theory]
    [InlineData("servers.hostname2")]
    [InlineData("servers.hostname2Hint")]
    [InlineData("servers.port2")]
    [InlineData("servers.port2Hint")]
    public void Field_Labels_Are_In_Both_Catalogs(string key)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var text = Loc.In(lang, key);
            Assert.False(string.IsNullOrWhiteSpace(text) || text == key,
                $"нет текста {key} в словаре {lang}");
        }
    }
}
