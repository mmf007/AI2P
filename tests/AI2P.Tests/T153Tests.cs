using System.Net.Sockets;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-153 (версия 1.75): НЕ ИДЁТ РЕПЛИКАЦИЯ — продолжение первичной установки после T-146,
/// T-148 и T-150. Сервер и человек подключились к организации, а репликация всё равно
/// не начиналась. Причин оказалось три, и все три разные:
/// <list type="number">
/// <item>у новой записи сервера интервал репликации ПУСТ, а пустой интервал означает «только
/// по кнопке»: подключённая пара молчала, пока человек не откроет форму сервера;</item>
/// <item>подавший сервер не спросил у дирижёра решение по своей заявке — организации у него
/// нет, и ручной пуск отвечал «он не подключён ни к одной вашей организации», хотя заявку
/// давно приняли;</item>
/// <item>ошибка связи с сервером показывалась собственным текстом .NET («An error occurred
/// while sending the request»): ни адреса, ни причины — причина лежит во ВЛОЖЕННОМ
/// исключении.</item>
/// </list>
///
/// Сюда же — кнопка «Далее» на шаге 4/4 визарда: уйти с него можно было только текстовой
/// ссылкой, и экран выглядел зависшим. Весь обмен целиком проверяется живой проверкой
/// двух серверов (<c>test/t153/</c>).
/// </summary>
public sealed class T153Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Дирижёр (локальный сервер) с организацией и подключённым к ней вторым
    /// сервером — та самая пара, которая у заказчика молчала.</summary>
    private (Organization Org, ServerNode Peer, OrgServer Link) Joined(string status)
    {
        var org = _f.CreateOrgWithLocalServer("Фирма", "acme");
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "mmf_u", Protocol = "http", Hostname = "10.0.0.5", Port = 5480,
            BasePath = "/ai2p", IsActive = false,
        }, null);
        var link = _f.Servers.Attach(org.Id, peer.Id, status, "mmf_u <mmf_u@mail>", null,
            applicant: new JoinApplicant("acc-1", "mmf_u", "mmf_u@mail", "hash"));
        return (org, peer, link);
    }

    // ---------- 1. подключение включает автоматическую репликацию ----------

    /// <summary>Новая запись сервера живёт «только по кнопке»: это и есть причина, по которой
    /// пара не реплицировалась сама ни разу.</summary>
    [Fact]
    public void A_New_Server_Record_Has_No_Interval_At_All()
    {
        var (_, peer, _) = Joined(OrgServerStatus.Pending);

        Assert.Null(_f.Servers.Get(peer.Id)!.ReplIntervalSec);
    }

    [Fact]
    public void Joining_Turns_The_Automatic_Replication_On()
    {
        var (_, peer, _) = Joined(OrgServerStatus.Pending);

        _f.Servers.EnsureReplication(peer.Id, null);

        Assert.Equal(ServerService.JoinedReplIntervalSec,
            _f.Servers.Get(peer.Id)!.ReplIntervalSec);
    }

    /// <summary>Заданное человеком не трогаем — в том числе «только по кнопке», выбранное
    /// им ПОСЛЕ подключения: там уже стоит не пустое значение, а его решение.</summary>
    [Fact]
    public void An_Interval_Set_By_A_Human_Is_Kept()
    {
        var (_, peer, _) = Joined(OrgServerStatus.Pending);
        _f.Servers.SetReplication(peer.Id, 45, 20, null);

        _f.Servers.EnsureReplication(peer.Id, null);

        var stored = _f.Servers.Get(peer.Id)!;
        Assert.Equal(45, stored.ReplIntervalSec);
        Assert.Equal(20, stored.ReplRetrySec);
    }

    /// <summary>Интервал принадлежит записи РЯДОВОГО сервера пары и реплицируется (ТЗ гл. 6),
    /// поэтому его задают обе стороны — и обе получают одно и то же значение.</summary>
    [Fact]
    public void Both_Sides_Set_The_Same_Interval()
    {
        var (_, peer, _) = Joined(OrgServerStatus.Pending);

        var byConductor = _f.Servers.EnsureReplication(peer.Id, null)!.ReplIntervalSec;
        var byPeerItself = _f.Servers.EnsureReplication(peer.Id, null)!.ReplIntervalSec;

        Assert.Equal(byConductor, byPeerItself);
        Assert.Equal(ServerService.JoinedReplIntervalSec, byConductor);
    }

    [Fact]
    public void Ensuring_A_Missing_Server_Changes_Nothing() =>
        Assert.Null(_f.Servers.EnsureReplication("нет такого", null));

    // ---------- 2. заявка, не доведённая до конца ----------

    /// <summary>
    /// Подключение НЕ доведено до конца: заявка ждёт решения. Пока это так, организации
    /// у подавшего сервера нет — реплицировать нечего, и это надо объяснять словами,
    /// а не утверждением «он не подключён ни к одной вашей организации».
    /// </summary>
    [Fact]
    public void A_Pending_Request_Means_There_Is_Nothing_To_Replicate_Yet()
    {
        var (org, peer, link) = Joined(OrgServerStatus.Pending);

        Assert.True(OrgServerStatus.IsRequest(_f.Servers.LinkById(link.Id)!.Status));
        // пары ещё нет: связь не активна
        Assert.DoesNotContain(_f.Servers.ServersOf(org.Id, includeRequests: false),
            l => l.ServerId == peer.Id && l.Status == OrgServerStatus.Active);
    }

    /// <summary>Решение спрашивают только по ЖДУЩИМ заявкам: у отклонённой и у доведённой
    /// до конца спрашивать нечего, иначе фоновый обход дёргал бы дирижёра вечно.</summary>
    [Fact]
    public void Only_Waiting_Requests_Are_Polled()
    {
        Assert.True(OrgServerStatus.IsRequest(OrgServerStatus.Pending));
        Assert.True(OrgServerStatus.IsRequest(OrgServerStatus.Deferred));
        Assert.False(OrgServerStatus.IsRequest(OrgServerStatus.Active));
        Assert.False(OrgServerStatus.IsRequest(OrgServerStatus.Rejected));
        Assert.False(OrgServerStatus.IsRequest(""));
    }

    // ---------- 3. обрыв связи называет адрес и причину ----------

    /// <summary>
    /// Сервер по адресу не отвечает (порт закрыт). Ответ обязан назвать ИМЯ, АДРЕС и причину:
    /// собственное сообщение .NET «An error occurred while sending the request» не говорит
    /// ни того, ни другого, ни третьего. Адрес берётся локальный и заведомо свободный —
    /// внешние вызовы в тестах флаки (T-138).
    /// </summary>
    [Fact]
    public async Task An_Unreachable_Server_Is_Explained_With_Address_And_Reason()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // порт освободили: теперь по нему никого нет

        var dead = new ServerNode
        {
            Id = "peer", Name = "mmf_u", Protocol = "http", Hostname = "127.0.0.1",
            Port = port, BasePath = "/ai2p",
        };
        using var client = new ClusterClient();

        var check = await client.HelloAsync(dead);

        Assert.False(check.Ok);
        Assert.Contains("mmf_u", check.Error);
        Assert.Contains(port.ToString(), check.Error);
        Assert.DoesNotContain("An error occurred while sending the request", check.Error);
        // и подсказка, что делать: сервер за NAT отсюда недоступен вовсе
        Assert.Contains("начинает он сам", check.Error);
    }

    // ---------- 4. шаг обновления билда 75 ----------

    /// <summary>Шаг есть и он про репликацию: на установке заказчика пара уже подключена,
    /// и включить ей автоматические сеансы можно только здесь.</summary>
    [Fact]
    public void Upgrade_Step_75_Turns_Replication_On()
    {
        var step = Assert.Single(AI2P.Server.Upgrade.Steps, s => s.Build == 75);

        Assert.Contains("репликац", step.Title);
        Assert.True(AppInfo.Build >= 75, "шаг обновления не должен опережать билд");
    }
}
