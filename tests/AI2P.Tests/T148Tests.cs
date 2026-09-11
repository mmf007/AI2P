using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Server.Org;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-148: ПРОДОЛЖЕНИЕ ПОПЫТКИ ПОДКЛЮЧИТЬ ДРУГОЙ СЕРВЕР.
///
/// Продолжение T-146. Человек застрял на шаге 3 визарда, а репликация с дирижёра падала:
/// <list type="number">
/// <item>ТРЕТЬЯ ПОПЫТКА УПИРАЛАСЬ В САМУ СЕБЯ: подключаемый сервер переставили начисто,
/// его аккаунт получил новый внутренний ключ, а на дирижёре человек с той же почтой остался
/// (его завело подтверждение прошлой заявки). Дирижёр отвечал «уже есть пользователь
/// с почтой…», и подключиться было нельзя ничем, кроме удаления живого участника руками.
/// Теперь дирижёр отвечает <see cref="ClusterJoinStatusDto.SameEmail"/> и отдаёт ключ,
/// а подключающийся сервер БЕРЁТ ЕГО СЕБЕ (<see cref="AccountService.Rekey"/>);</item>
/// <item>визард первого старта начинается с ВЫБОРА ЯЗЫКА, предложен язык браузера
/// (<see cref="AuthPages.BrowserLang"/>), а нет такого словаря — английский;</item>
/// <item>подав заявку, человек уходит В ИНТЕРФЕЙС и ждёт решения там: интерфейс без
/// организации урезан, но список серверов и настройки в нём есть — иначе ни решение
/// по заявке спросить, ни репликацию запустить руками;</item>
/// <item>петлевой адрес партнёра означает «мы сами» только при ТОМ ЖЕ порте: два экземпляра
/// на одной машине различаются портом, и звонить соседу законно.</item>
/// </list>
/// </summary>
public sealed class T148Tests : IDisposable
{
    private readonly StorageFixture _f = new();
    private readonly List<string> _temp = [];

    public void Dispose()
    {
        _f.Dispose();
        foreach (var dir in _temp)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // временный каталог словарей — не беда, если не удалился
            }
        }
    }

    // ---------- 1. язык браузера на первом экране визарда ----------

    /// <summary>Словари языков — файлы i18n/&lt;язык&gt;.json; больше про них ничего не нужно.</summary>
    private I18nService Dictionaries(params string[] languages)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai2p-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _temp.Add(dir);
        foreach (var lang in languages)
        {
            File.WriteAllText(Path.Combine(dir, lang + ".json"), """{"app.title":"AI2P"}""");
        }
        return new I18nService(dir, languages.FirstOrDefault() ?? "en");
    }

    private static HttpContext Browser(string acceptLanguage)
    {
        var ctx = new DefaultHttpContext();
        if (acceptLanguage.Length > 0)
        {
            ctx.Request.Headers.AcceptLanguage = acceptLanguage;
        }
        return ctx;
    }

    [Theory]
    // точное совпадение и совпадение по основной части тега
    [InlineData("ru-RU,ru;q=0.9,en;q=0.8", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("en-GB,en;q=0.9", "en")]
    // языка нет в словарях — английский
    [InlineData("fr-FR,fr;q=0.9", "en")]
    [InlineData("zh-CN", "en")]
    // заголовка нет вовсе либо он ничего не называет
    [InlineData("", "en")]
    [InlineData("*", "en")]
    // вес q решает, а не порядок в строке
    [InlineData("en;q=0.3,ru;q=0.9", "ru")]
    [InlineData("ru;q=0.2,en;q=0.8", "en")]
    public void Browser_Language_Is_Suggested(string header, string expected) =>
        Assert.Equal(expected, AuthPages.BrowserLang(Dictionaries("ru", "en"), Browser(header)));

    /// <summary>Английского словаря может не быть вовсе — тогда берётся любой установленный:
    /// экран первого старта обязан открыться на чём-то.</summary>
    [Fact]
    public void Without_English_Any_Installed_Language_Is_Suggested() =>
        Assert.Equal("ru", AuthPages.BrowserLang(Dictionaries("ru"), Browser("fr-FR")));

    // ---------- 2. тот же человек с другим внутренним ключом ----------

    /// <summary>
    /// ПОВТОРНОЕ ПОДКЛЮЧЕНИЕ (случай заказчика): сервер переставили, аккаунт завёлся заново
    /// и получил другой ключ. Дирижёр узнаёт человека по почте — она и есть логин.
    /// </summary>
    [Fact]
    public void Same_Email_Means_The_Same_Person()
    {
        var mine = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "petr123", IsActive = true,
        }, null);
        // заявка пришла от того же человека, но с ключом НОВОЙ установки
        var applicant = new JoinApplicant("другой-ключ", "Пётр", "petr@mail");

        var clash = OrgRegistry.ApplicantClash(_f.Accounts, applicant);

        Assert.NotNull(clash);                       // спор есть — и он про почту
        Assert.Contains("petr@mail", clash);
        Assert.Equal(mine.Id, _f.Accounts.FindByLogin("petr@mail")?.Id);
    }

    /// <summary>Взять себе ключ, под которым дирижёр знает этого человека: аккаунт остаётся
    /// тем же (почта, пароль, вход), меняется только внутренний ключ.</summary>
    [Fact]
    public void Rekey_Keeps_The_Account_And_Its_Password()
    {
        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "petr123", IsActive = true,
        }, null);
        var conductorKey = Guid.NewGuid().ToString();

        var moved = _f.Accounts.Rekey(account.Id, conductorKey, null);

        Assert.Equal(conductorKey, moved.Id);
        Assert.Null(_f.Accounts.Get(account.Id));                       // прежнего ключа нет
        Assert.Equal(conductorKey, _f.Accounts.Get(conductorKey)?.Id);
        Assert.Single(_f.Accounts.List());                              // и не задвоился
        var (signedIn, error) = _f.Accounts.Authenticate("petr@mail", "petr123", false);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.Equal(conductorKey, signedIn?.Id);
        // после смены ключа спора с заявителем больше нет — заявка пройдёт
        Assert.Null(OrgRegistry.ApplicantClash(_f.Accounts,
            new JoinApplicant(conductorKey, "Пётр", "petr@mail")));
    }

    /// <summary>Занятый ключ не отдаётся: два аккаунта с одним ключом — это потеря одного.</summary>
    [Fact]
    public void Rekey_Refuses_A_Busy_Key()
    {
        var first = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "x", IsActive = true,
        }, null);
        var second = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Павел", Email = "pavel@mail", Password = "y", IsActive = true,
        }, null);

        Assert.Throws<ArgumentException>(() => _f.Accounts.Rekey(first.Id, second.Id, null));
        Assert.Equal(2, _f.Accounts.List().Count);
    }

    /// <summary>Однофамилец с ДРУГОЙ почтой — по-прежнему спор: это разные люди, и в списке
    /// участников их будет не различить. Правится на подключающемся сервере.</summary>
    [Fact]
    public void Same_Name_With_Another_Email_Is_Still_A_Clash()
    {
        _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@mail", Password = "x", IsActive = true,
        }, null);

        var clash = OrgRegistry.ApplicantClash(_f.Accounts,
            new JoinApplicant("новый-ключ", "Пётр", "petr2@mail"));

        Assert.NotNull(clash);
        Assert.Contains("именем", clash);
    }

    // ---------- 3. петлевой адрес — «мы сами» только при том же порте ----------

    /// <summary>Два экземпляра на одном компьютере различаются портом: «localhost:5496»
    /// с нашего «localhost:5495» — законный адрес соседа, по нему он и отвечает.</summary>
    [Fact]
    public void Loopback_With_Another_Port_Is_Reachable()
    {
        var local = _f.Servers.EnsureLocal("наш", "http", "localhost", 5495, "/ai2p");
        var neighbour = _f.Servers.Create(new ServerSaveInput
        {
            Name = "сосед", Hostname = "localhost", Port = 5496, BasePath = "/ai2p",
        }, null);
        var sameAddress = _f.Servers.Create(new ServerSaveInput
        {
            Name = "за NAT", Hostname = "localhost", Port = 5495, BasePath = "/ai2p",
        }, null);
        // запись без имени хоста в базе не заводится (проверка ServerService), но прийти
        // такая может по сети — адреса у неё нет, звонить некуда
        var noAddress = new ServerNode
        {
            Name = "без адреса", Hostname = "", Port = 5496, BasePath = "/ai2p",
        };

        Assert.Null(ServerAddress.Unreachable(neighbour, local));
        Assert.NotNull(ServerAddress.Unreachable(sameAddress, local));
        Assert.NotNull(ServerAddress.Unreachable(noAddress, local));
        Assert.Null(ServerAddress.Unreachable(local, local));
        // своей записи не знаем — прежнее строгое правило (любой петлевой адрес чужой)
        Assert.NotNull(ServerAddress.Unreachable(neighbour));
    }

    // ---------- 4. права урезанного интерфейса ----------

    /// <summary>Список серверов — карта кластера, он закрыт администратором (с T-174: свой
    /// компьютер ведёт admin, а owner — хозяин всей организации); настройки самого приложения
    /// и состояние UI читает кто угодно вошедший.</summary>
    [Fact]
    public void Server_List_Is_Closed_To_Ordinary_Members()
    {
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("GET", "/api/servers"));
        Assert.Equal(SystemRole.Admin, ApiPermissions.Required("POST", "/api/servers/x/replicate"));
        Assert.Equal(SystemRole.Reader, ApiPermissions.Required("GET", "/api/settings"));
        Assert.Equal(SystemRole.Reader, ApiPermissions.Required("GET", "/api/state"));
    }
}
