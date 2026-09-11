using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-141 (версия 1.71): ОШИБКИ ПРИ ПОДКЛЮЧЕНИИ ДРУГОГО СЕРВЕРА.
///
/// Продолжение T-139. Первое подключение по-прежнему не проходило, и причин было несколько:
/// <list type="number">
/// <item>серверы сравнивались ПО АДРЕСУ. За NAT публичный адрес есть у одного сервера
/// кластера, остальные называются «localhost» — и это нормально: с таким сервером сеанс
/// репликации начать нельзя, а сам он начинает его когда угодно. Различать серверы нужно
/// по внутреннему ключу (<see cref="ServerAddress"/>);</item>
/// <item>дубль почты или имени заявителя вскрывался только при ПОДТВЕРЖДЕНИИ заявки —
/// то есть на дирижёре, где исправить его некому. Теперь он ловится при подаче, а визард
/// первого старта умеет шаг назад — на правку своей почты и имени;</item>
/// <item>организаций у дирижёра бывает несколько: он отвечает их перечнем, и выбор
/// множественный — заявка уходит в каждую отмеченную;</item>
/// <item>заявка не появлялась в списке серверов, пока настройки не закрыть и не открыть;</item>
/// <item>запись сервера нельзя было УДАЛИТЬ: неудачное первое подключение висело в списке
/// навсегда и съедало выданный код (S1);</item>
/// <item>cookie от прежней установки считалась входом, хотя в новой базе нет ни одного
/// пользователя, — первый старт проходил мимо (проверяется в <see cref="T141Tests"/>
/// косвенно: аккаунт визарда правится, а сама cookie отвергается в Program.cs).</item>
/// </list>
/// </summary>
public sealed class T141Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. серверы различаются ключом, а не адресом ----------

    /// <summary>Адрес, означающий «я сам»: по нему к ЧУЖОМУ серверу не обратиться.</summary>
    [Fact]
    public void Loopback_Addresses_Are_Recognized()
    {
        foreach (var loopback in new[] { "localhost", "LOCALHOST", "127.0.0.1", "127.1.2.3", "::1", "[::1]", "", "  " })
        {
            Assert.True(ServerAddress.IsLoopback(loopback), loopback);
        }
        foreach (var real in new[] { "ubuntu.local", "192.168.1.10", "77.88.8.8", "мойпк" })
        {
            Assert.False(ServerAddress.IsLoopback(real), real);
        }
    }

    /// <summary>
    /// ДВА СЕРВЕРА С ОДНИМ АДРЕСОМ ТЕПЕРЬ ЗАКОННЫ (в этом суть правки): у сервера за NAT
    /// постоянного адреса нет, и себя он называет «localhost» — как и дирижёр.
    /// </summary>
    [Fact]
    public void Servers_With_The_Same_Address_Are_Allowed()
    {
        var local = _f.Servers.EnsureLocal("дирижёр", "http", "localhost", 5480, "/ai2p");

        var other = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Protocol = "http", Hostname = "localhost", Port = 5480, BasePath = "/ai2p",
        }, null);

        Assert.NotEqual(local.Id, other.Id);
        Assert.Equal(2, _f.Servers.List().Count);
        // адрес занят — но это справка для сообщений, а не отказ
        Assert.Equal(local.Id, _f.Servers.AddressOwner("localhost", 5480, "/ai2p", other.Id)?.Id);
    }

    /// <summary>К серверу с петлевым именем не ходят: он «почти клиент» — сеанс начинает сам.
    /// Своей записи это не касается: локальный сервер — это мы, и звонить ему незачем.</summary>
    [Fact]
    public void Loopback_Peer_Is_Unreachable_But_Local_Is_Fine()
    {
        var local = _f.Servers.EnsureLocal("дирижёр", "http", "localhost", 5480, "/ai2p");
        var behindNat = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Hostname = "localhost", Port = 5480, BasePath = "/ai2p",
        }, null);
        var reachable = _f.Servers.Create(new ServerSaveInput
        {
            Name = "публичный", Hostname = "77.88.8.8", Port = 5480, BasePath = "/ai2p",
        }, null);

        Assert.Null(ServerAddress.Unreachable(local));
        Assert.Null(ServerAddress.Unreachable(reachable));
        var problem = ServerAddress.Unreachable(behindNat);
        Assert.NotNull(problem);
        Assert.Contains("ubuntu", problem);
        Assert.Contains("начинает ОН САМ", problem);
    }

    // ---------- 2. несколько заявок к одному дирижёру ----------

    /// <summary>Организаций у дирижёра несколько — заявка на каждую своя, и все они
    /// помнятся: иначе решение по первой потерялось бы (выбор множественный).</summary>
    [Fact]
    public void Join_References_Accumulate()
    {
        var conductor = _f.Servers.Create(new ServerSaveInput
        {
            Name = "дирижёр", Hostname = "77.88.8.8", Port = 5480, BasePath = "/ai2p",
        }, null);

        _f.Servers.SetJoin(conductor.Id, OrgServerStatus.Pending, "alpha", "req-1");
        _f.Servers.AddJoin(conductor.Id, OrgServerStatus.Pending, "beta", "req-2");
        _f.Servers.AddJoin(conductor.Id, OrgServerStatus.Pending, "beta", "req-2");   // повтор

        var saved = _f.Servers.Get(conductor.Id)!;
        Assert.Equal("req-1,req-2", saved.JoinRef);
        Assert.Equal("alpha,beta", saved.JoinOrgCode);
        Assert.Equal(["req-1", "req-2"], ServerService.Split(saved.JoinRef));
        Assert.Empty(ServerService.Split(""));
    }

    // ---------- 3. дубль почты и имени заявителя ----------

    /// <summary>Почта — логин: два аккаунта с одной почтой в кластере невозможны.
    /// Отказ приходит В ОТВЕТ НА ЗАЯВКУ, чтобы правил его подключающийся сервер.</summary>
    [Fact]
    public void Applicant_With_A_Taken_Email_Is_Refused()
    {
        _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Иван Петров", Email = "ivan@localhost", Password = "x", IsActive = true,
        }, null);

        var clash = AI2P.Server.Org.OrgRegistry.ApplicantClash(_f.Accounts, new JoinApplicant("acc-другой", "Иван Сидоров",
            "ivan@localhost"));

        Assert.NotNull(clash);
        Assert.Contains("почтой «ivan@localhost»", clash);
        Assert.Contains("Смените почту", clash);
    }

    /// <summary>Имя тоже спорное: после подтверждения обоих не различить в списке участников.</summary>
    [Fact]
    public void Applicant_With_A_Taken_Name_Is_Refused()
    {
        _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Иван Петров", Email = "ivan@localhost", Password = "x", IsActive = true,
        }, null);

        var clash = AI2P.Server.Org.OrgRegistry.ApplicantClash(_f.Accounts, new JoinApplicant("acc-другой", "иван петров",
            "ivan2@localhost"));

        Assert.NotNull(clash);
        Assert.Contains("именем «Иван Петров»", clash);
    }

    /// <summary>ТОТ ЖЕ человек (тот же ключ аккаунта) спором не считается: он приезжает
    /// репликацией, и это нормальный порядок вещей.</summary>
    [Fact]
    public void The_Same_Account_Is_Not_A_Clash()
    {
        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Иван Петров", Email = "ivan@localhost", Password = "x", IsActive = true,
        }, null);

        Assert.Null(AI2P.Server.Org.OrgRegistry.ApplicantClash(_f.Accounts, 
            new JoinApplicant(account.Id, account.Name, account.Email)));
        // заявка без заявителя (старые связи) — тоже не спор
        Assert.Null(AI2P.Server.Org.OrgRegistry.ApplicantClash(_f.Accounts, new JoinApplicant("", "", "")));
    }

    // ---------- 4. шаг назад визарда: правка своей почты и имени ----------

    /// <summary>
    /// Из-за этого визард и сделан: дирижёр сказал «почта занята» — человек возвращается
    /// шагом назад и правит её у себя. Обычная правка аккаунта почту не меняет (она
    /// идентификатор), а на первом старте — меняет.
    /// </summary>
    [Fact]
    public void First_Start_Account_Can_Change_Its_Email_And_Name()
    {
        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Иван", Email = "ivan@localhost", Password = "секрет", IsActive = true,
        }, null);

        var renamed = _f.Accounts.Rename(account.Id, "Иван Петров", "ivan.petrov@localhost",
            "+7 000", account.Id);

        Assert.Equal("Иван Петров", renamed.Name);
        Assert.Equal("ivan.petrov@localhost", renamed.Email);
        Assert.Equal(account.Id, renamed.Id);
        // пароль правка не трогает — по нему человек и войдёт
        Assert.NotNull(_f.Accounts.Authenticate("ivan.petrov@localhost", "секрет", false).Account);
    }

    /// <summary>Занятая почта не принимается и здесь: уникальность логина не отменяется.</summary>
    [Fact]
    public void First_Start_Account_Cannot_Take_A_Busy_Email()
    {
        var first = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Иван", Email = "ivan@localhost", IsActive = true,
        }, null);
        _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Пётр", Email = "petr@localhost", IsActive = true,
        }, null);

        Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Rename(first.Id, "Иван", "petr@localhost", "", null));
    }

    // ---------- 5. удаление сервера ----------

    /// <summary>
    /// Неудачное первое подключение убирается целиком — вместе со связью, — и КОД
    /// ОСВОБОЖДАЕТСЯ: следующая попытка получит тот же S1, а не S2.
    /// </summary>
    [Fact]
    public void Deleting_A_Server_Frees_Its_Code()
    {
        var org = _f.Orgs.Create("Тест", "test", null);
        var local = _f.Servers.EnsureLocal("дирижёр", "http", "77.88.8.8", 5480, "/ai2p");
        _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null);
        var failed = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Hostname = "192.168.1.10", Port = 5480, BasePath = "/ai2p",
        }, null);
        var link = _f.Servers.Attach(org.Id, failed.Id, OrgServerStatus.Pending, "Иван", null);
        Assert.Equal("S1", link.Code);

        Assert.Null(_f.Servers.DeleteProblem(failed.Id));
        _f.Servers.Delete(failed.Id, null);

        Assert.Null(_f.Servers.Get(failed.Id));
        Assert.DoesNotContain(_f.Servers.ServersOf(org.Id), l => l.ServerId == failed.Id);
        // повторная попытка получает ТОТ ЖЕ код — номер не съеден
        var again = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu снова", Hostname = "192.168.1.10", Port = 5480, BasePath = "/ai2p",
        }, null);
        Assert.Equal("S1", _f.Servers.Attach(org.Id, again.Id, OrgServerStatus.Pending, "Иван", null).Code);
    }

    /// <summary>Себя, дирижёра и уже реплицировавшийся сервер не удаляют — и каждый отказ
    /// объясняется словами: кнопка в списке погашена именно этим текстом.</summary>
    [Fact]
    public void Deleting_Is_Refused_With_An_Explanation()
    {
        var org = _f.Orgs.Create("Тест", "test", null);
        var local = _f.Servers.EnsureLocal("дирижёр", "http", "77.88.8.8", 5480, "/ai2p");
        _f.Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, "", null);
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Hostname = "192.168.1.10", Port = 5480, BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org.Id, peer.Id, OrgServerStatus.Active, "", null);

        Assert.Contains("сам данный сервер", _f.Servers.DeleteProblem(local.Id));
        Assert.Equal("Сервер не найден", _f.Servers.DeleteProblem("нет такого"));
        Assert.Null(_f.Servers.DeleteProblem(peer.Id));

        // была удачная репликация — запись уже разошлась по кластеру
        var state = new ReplicationStateService(_f.ServerDb);
        var pair = state.Get(org.Id, peer.Id);
        pair.LastOkAt = DateTime.UtcNow;
        state.Save(pair);

        Assert.Contains("уже была репликация", _f.Servers.DeleteProblem(peer.Id));
        Assert.Throws<ArgumentException>(() => _f.Servers.Delete(peer.Id, null));
    }

    /// <summary>Дирижёра организации удалить нельзя: сначала передают дирижёрство.</summary>
    [Fact]
    public void Conductor_Cannot_Be_Deleted()
    {
        var org = _f.Orgs.Create("Тест", "test", null);
        var conductor = _f.Servers.Create(new ServerSaveInput
        {
            Name = "дирижёр", Hostname = "77.88.8.8", Port = 5480, BasePath = "/ai2p",
        }, null);
        _f.Servers.Attach(org.Id, conductor.Id, OrgServerStatus.Active, "", null);

        Assert.Contains("дирижёр организации", _f.Servers.DeleteProblem(conductor.Id));
    }

    // ---------- 6. перечень организаций в ответе на заявку ----------

    /// <summary>Перечень организаций едет ТОЛЬКО в ответе «выберите организацию»: отдельной
    /// двери «покажи свои организации» в протоколе нет (T-139) и не появилось.</summary>
    [Fact]
    public void Choose_Org_Answer_Carries_The_List()
    {
        var answer = new ClusterJoinStatusDto
        {
            Status = ClusterJoinStatusDto.ChooseOrg,
            Orgs =
            [
                new ClusterOrgBriefDto { Code = "alpha", Name = "Альфа" },
                new ClusterOrgBriefDto { Code = "beta", Name = "Бета" },
            ],
        };

        Assert.Equal("choose", ClusterJoinStatusDto.ChooseOrg);
        Assert.Equal("choose", answer.Status);
        Assert.Equal(["alpha", "beta"], answer.Orgs.Select(o => o.Code));
        // обычный ответ перечня не несёт
        Assert.Empty(new ClusterJoinStatusDto().Orgs);
        Assert.Null(typeof(ClusterClient).GetMethod("ListOrgsAsync"));
    }
}
