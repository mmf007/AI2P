using System.Net;
using System.Net.Sockets;
using System.Text;
using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-50-S0 (экстренный выпуск 1.106): настройки серверов и репликация.
///
/// Жалоба заказчика: на рядовом сервере в списке серверов у дирижёра ВИСЕЛА ошибка «Нет связи
/// с сервером …», хотя тот же адрес открывался в браузере, а строка списка при этом бодро
/// показывала «скоро». Плюс четыре замечания по форме сервера: внешний адрес соседа не виден
/// нигде, времена репликации стоят не в той форме, у сервера с петлевым адресом живая кнопка
/// ручного пуска (звонить ему некуда) и переход по ссылке не спрашивает, каким из двух
/// адресов идти.
///
/// Здесь проверяется то, что проверяется без двух компьютеров: хранение второго адреса,
/// перебор адресов при обращении и текст ошибки. Формы и строка списка — живой проверкой.
/// </summary>
public sealed class T50S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. второй (внешний) адрес хранится у записи сервера ----------

    /// <summary>Свой второй адрес приходит из config.json и ложится в запись сервера: оттуда
    /// он уедет соседям репликацией — иначе им неоткуда узнать, как звать нас снаружи.</summary>
    [Fact]
    public void The_Local_Record_Keeps_The_Second_Address()
    {
        _f.Servers.EnsureLocal("этот", "http", "192.168.0.20", 5480, "/ai2p", "mmf.example.com", 8443);

        var local = _f.Servers.Local()!;
        Assert.Equal("mmf.example.com", local.Hostname2);
        Assert.Equal(8443, local.Port2);
        Assert.True(local.HasAddress2);
        Assert.Equal("http://192.168.0.20:5480/ai2p", local.Url);
        Assert.Equal("http://mmf.example.com:8443/ai2p", local.Url2);
    }

    /// <summary>Порт без имени — не адрес: он не хранится и второго адреса не делает.</summary>
    [Fact]
    public void A_Port_Without_A_Host_Name_Is_Not_An_Address()
    {
        _f.Servers.EnsureLocal("этот", "http", "192.168.0.20", 5480, "/ai2p", "", 8443);

        var local = _f.Servers.Local()!;
        Assert.Null(local.Port2);
        Assert.False(local.HasAddress2);
        Assert.Equal("", local.Url2);
    }

    /// <summary>Второй адрес СОСЕДА правится и руками — формой его сервера: пока обмена
    /// не было, приехать этому значению неоткуда.</summary>
    [Fact]
    public void The_Second_Address_Of_A_Peer_Is_Editable()
    {
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "S1", Protocol = "http", Hostname = "192.168.0.20", Port = 5480,
            BasePath = "/ai2p", Hostname2 = "mmf.example.com", Port2 = 8443,
        }, null);

        Assert.Equal("http://mmf.example.com:8443/ai2p", _f.Servers.Get(peer.Id)!.Url2);

        _f.Servers.Update(peer.Id, new ServerSaveInput
        {
            Name = "S1", Protocol = "http", Hostname = "192.168.0.20", Port = 5480,
            BasePath = "/ai2p", Hostname2 = "", Port2 = 8443,
        }, null);

        var cleared = _f.Servers.Get(peer.Id)!;
        Assert.False(cleared.HasAddress2);
        Assert.Null(cleared.Port2);
    }

    /// <summary>Пустой второй порт значит «тот же, что основной»: у роутера без проброса
    /// на другой порт заполнять его незачем.</summary>
    [Fact]
    public void An_Empty_Second_Port_Means_The_Same_Port()
    {
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "S1", Protocol = "http", Hostname = "10.0.0.5", Port = 5490,
            BasePath = "/ai2p", Hostname2 = "mmf.example.com",
        }, null);

        Assert.Equal("http://mmf.example.com:5490/ai2p", _f.Servers.Get(peer.Id)!.Url2);
    }

    /// <summary>Колонки добавляет идемпотентная миграция серверной БД (v23 → v24), и журнал
    /// изменений строится по фактическому составу колонок — репликация подхватит их сама.</summary>
    [Fact]
    public void The_Second_Address_Is_In_The_Server_Schema()
    {
        using var conn = _f.ServerDb.Open();
        var columns = Sql.Query(conn, null, "PRAGMA table_info(servers)", r => r.S("name"));

        Assert.Contains("hostname2", columns);
        Assert.Contains("port2", columns);
        Assert.Equal("24", Sql.Scalar<string>(conn, null,
            "SELECT value FROM meta WHERE key='schema_version'"));
    }

    // ---------- 2. обращение перебирает адреса ----------

    /// <summary>ГЛАВНОЕ ПО ЖАЛОБЕ: внутренний адрес не отвечает — обращение тут же идёт
    /// по внешнему, не дожидаясь следующего сеанса.</summary>
    [Fact]
    public async Task A_Call_Falls_Back_To_The_Second_Address()
    {
        var (port, serve) = Answer("""{"serverId":"peer","name":"S1","version":"1.106"}""");
        var node = new ServerNode
        {
            Id = "peer", Name = "S1", Protocol = "http", Hostname = "127.0.0.1",
            Port = Dead(), Hostname2 = "127.0.0.1", Port2 = port, BasePath = "/ai2p",
        };
        using var client = new ClusterClient();

        var check = await client.HelloAsync(node);

        Assert.True(check.Ok, check.Error);
        Assert.Equal("S1", check.Name);
        Assert.True(check.SameServer);
        await serve;
    }

    /// <summary>Второго адреса нет — перебирать нечего, и ошибка та же, что была.</summary>
    [Fact]
    public async Task Without_A_Second_Address_Nothing_Changes()
    {
        var port = Dead();
        var node = new ServerNode
        {
            Id = "peer", Name = "S1", Protocol = "http", Hostname = "127.0.0.1",
            Port = port, BasePath = "/ai2p",
        };
        using var client = new ClusterClient();

        var check = await client.HelloAsync(node);

        Assert.False(check.Ok);
        Assert.Contains(port.ToString(), check.Error);
    }

    /// <summary>Не отвечают оба адреса — в ошибке названы ОБА: человеку важно знать, что
    /// перебраны все, а не что «сервер недоступен» по неизвестно какому адресу.</summary>
    [Fact]
    public async Task Both_Addresses_Are_Named_In_The_Error()
    {
        int inner = Dead(), outer = Dead();
        var node = new ServerNode
        {
            Id = "peer", Name = "S1", Protocol = "http", Hostname = "127.0.0.1",
            Port = inner, Hostname2 = "127.0.0.1", Port2 = outer, BasePath = "/ai2p",
        };
        using var client = new ClusterClient();

        var check = await client.HelloAsync(node);

        Assert.False(check.Ok);
        Assert.Contains(inner.ToString(), check.Error);
        Assert.Contains(outer.ToString(), check.Error);
    }

    // ---------- вспомогательное ----------

    /// <summary>Свободный порт, на котором заведомо никого нет: соединение по нему отвергается
    /// сразу, а не висит до таймаута.</summary>
    private static int Dead()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Простейший «сервер»: отвечает одним заготовленным телом на один запрос.
    /// Настоящий AI2P поднимать незачем — проверяется перебор адресов, а не протокол.</summary>
    private static (int Port, Task Serve) Answer(string json)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var buffer = new byte[8192];
                await stream.ReadAsync(buffer);   // запрос без тела — читаем и забываем
                var body = Encoding.UTF8.GetBytes(json);
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                    + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, serve);
    }
}
