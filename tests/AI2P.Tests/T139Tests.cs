using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-139 (версия 1.68): ПЕРВОЕ ПОДКЛЮЧЕНИЕ ДРУГОГО СЕРВЕРА.
///
/// Подключение второго сервера к кластеру не проходило вовсе: имя хоста у каждой установки
/// было умолчанием <c>localhost</c>, поэтому дирижёр, заводя запись подключающегося сервера
/// по присланному адресу, упирался в собственную запись — «сервер с адресом
/// http://localhost:5480 уже есть в списке».
///
/// Лечится это в трёх местах:
/// 1. экран первого старта спрашивает ИМЯ СЕРВЕРА (пустое), протокол и порт (предложены);
/// 2. имя записи локального сервера едет за адресом — иначе в списке кластера так и осталось
///    бы «localhost»;
/// 3. занятый адрес объясняется человеку словами: и на дирижёре (кто занял), и у подающего
///    заявку (петля вместо настоящего имени) — до обращения по сети.
///
/// Плюс кнопка «перейти на сервер» в списке серверов: пароли аккаунтов приезжают
/// репликацией, а меняет их человек на своём сервере — туда и ведёт кнопка (UI, проверено
/// живой проверкой).
/// </summary>
public sealed class T139Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // ---------- 1. имя сервера, протокол и порт на экране первого старта ----------

    [Fact]
    public void First_Start_Reads_Server_Name_Protocol_And_Port()
    {
        Assert.True(AuthPages.TryReadServer("  windows-pc  ", "http", "5480",
            out var host, out var protocol, out var port, out var error));
        Assert.Equal(("windows-pc", "http", 5480, ""), (host, protocol, port, error));

        Assert.True(AuthPages.TryReadServer("192.168.1.10", "https", "8443",
            out host, out protocol, out port, out _));
        Assert.Equal(("192.168.1.10", "https", 8443), (host, protocol, port));
    }

    /// <summary>Набранный целиком адрес тоже понимается: протокол и порт из него выигрывают
    /// у соседних полей (их человек указал явно), путь отбрасывается — префикс в config.json.</summary>
    [Fact]
    public void First_Start_Understands_Full_Address_In_The_Name()
    {
        Assert.True(AuthPages.TryReadServer("https://ubuntu.local:8443/ai2p", "http", "5480",
            out var host, out var protocol, out var port, out _));
        Assert.Equal(("ubuntu.local", "https", 8443), (host, protocol, port));

        Assert.True(AuthPages.TryReadServer("ubuntu.local/ai2p", "http", "5480",
            out host, out protocol, out port, out _));
        Assert.Equal(("ubuntu.local", "http", 5480), (host, protocol, port));
    }

    /// <summary>Имя ОБЯЗАТЕЛЬНО (в этом вся суть правки): пустое поле — ошибка, а не «localhost».</summary>
    [Fact]
    public void First_Start_Requires_Server_Name()
    {
        Assert.False(AuthPages.TryReadServer("   ", "http", "5480", out _, out _, out _, out var empty));
        Assert.Equal("login.error.serverName", empty);

        Assert.False(AuthPages.TryReadServer("мой сервер", "http", "5480", out _, out _, out _,
            out var spaced));
        Assert.Equal("login.error.serverHost", spaced);

        foreach (var wrong in new[] { "", "0", "70000", "порт" })
        {
            Assert.False(AuthPages.TryReadServer("windows-pc", "http", wrong, out _, out _,
                out var number, out var badPort));
            Assert.Equal("login.error.serverPort", badPort);
            Assert.Equal(0, number);
        }
    }

    // ---------- 2. имя записи локального сервера едет за адресом ----------

    [Fact]
    public void Local_Server_Name_Follows_The_Address()
    {
        // так запись заводится при старте до первого входа: имя = имя хоста из config.json
        _f.Servers.EnsureLocal("localhost", "http", "localhost", 5480, "/ai2p");

        var renamed = _f.Servers.EnsureLocal("windows-pc", "http", "windows-pc", 5480, "/ai2p");

        Assert.Equal("windows-pc", renamed.Name);
        Assert.Equal("http://windows-pc:5480/ai2p", renamed.Url);
        Assert.Equal("windows-pc", _f.Servers.Local()!.Name);
    }

    /// <summary>Имя, ЗАДАННОЕ человеком (оно не выведено из адреса), сменой адреса не трогается.</summary>
    [Fact]
    public void Explicit_Local_Server_Name_Survives_Address_Change()
    {
        _f.Servers.EnsureLocal("ноутбук", "http", "localhost", 5480, "/ai2p");

        var moved = _f.Servers.EnsureLocal("windows-pc", "http", "windows-pc", 5480, "/ai2p");

        Assert.Equal("ноутбук", moved.Name);
        Assert.Equal("windows-pc", moved.Hostname);
    }

    // ---------- 3. занятый адрес объясняется словами ----------

    /// <summary>Дирижёр видит, ЧЬЯ запись заняла адрес подключающегося: это он сам.
    /// С T-141 это только справка для сообщений — отказом занятый адрес больше не является.</summary>
    [Fact]
    public void Conductor_Knows_Who_Took_The_Address()
    {
        var local = _f.Servers.EnsureLocal("дирижёр", "http", "localhost", 5480, "/ai2p");

        var owner = _f.Servers.AddressOwner("localhost", 5480, "/ai2p");

        Assert.NotNull(owner);
        Assert.Equal(local.Id, owner!.Id);
        Assert.True(owner.IsLocal);
        // свою же запись проверка не считает занявшей адрес — иначе не сохранить сервер
        Assert.Null(_f.Servers.AddressOwner("localhost", 5480, "/ai2p", exceptId: local.Id));
        // свободный адрес
        Assert.Null(_f.Servers.AddressOwner("ubuntu.local", 5480, "/ai2p"));
    }

    // Запрета на два сервера с одним адресом больше НЕТ (T-141): серверы различаются
    // внутренним ключом, а за NAT половина кластера законно называется «localhost».
    // Что стало вместо него — T141Tests.

    // ---------- 4. пароли в кластере ----------

    /// <summary>
    /// Пароль аккаунта заводить на втором сервере ВТОРОЙ РАЗ не нужно: аккаунты участников
    /// организации реплицируются вместе с ней, и хэш пароля едет в той же строке (ТЗ п. 6.4.1).
    /// Здесь это зафиксировано на составе реплицируемых таблиц и колонок: колонка
    /// <c>password_hash</c> в число «не покидающих сервер» не входит.
    /// </summary>
    [Fact]
    public void Account_Passwords_Travel_With_Replication()
    {
        Assert.Contains("accounts", AI2P.Storage.ChangeLog.ServerTables);

        var account = _f.Accounts.Create(new AccountSaveInput
        {
            Name = "Хозяин", Email = "owner@localhost", Password = "secret", IsActive = true,
        }, null);
        using var conn = _f.ServerDb.Open();
        var payload = AI2P.Storage.Sql.Scalar<string>(conn, null, """
            SELECT payload_json FROM changes WHERE tbl='accounts' AND pk=@id ORDER BY seq DESC LIMIT 1
            """, ("@id", account.Id));

        Assert.NotNull(payload);
        Assert.Contains("password_hash", payload);
    }
}
