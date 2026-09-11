using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-140 (версия 1.70): УДАЛЕНИЕ ПОЛЬЗОВАТЕЛЯ на вкладке «Настройки → Пользователи».
///
/// Правила задания: удалять можно только НЕАКТИВНОГО пользователя и нельзя — владельца
/// системы. Удаление мягкое (<c>deleted_at</c>): на аккаунт ссылаются исполнители организаций
/// и записи журнала работ, поэтому строка остаётся, но пользователь пропадает из списка,
/// в систему не пускается и освобождает свою почту и телефон.
/// </summary>
public sealed class T140Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Inactive_Account_Is_Deleted_And_Disappears_From_List()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        _f.AddMember(owner, SystemRole.Owner);
        var user = Deactivated(owner, "user@example.com", "Юзер");

        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        Assert.DoesNotContain(_f.Accounts.List(), a => a.Id == user.Id);
        // строка на месте — на неё ссылаются исполнители организаций и журнал работ
        var stored = _f.Accounts.Get(user.Id);
        Assert.NotNull(stored);
        Assert.NotNull(stored!.DeletedAt);
        Assert.Contains(_f.Accounts.List(includeDeleted: true), a => a.Id == user.Id);
    }

    [Fact]
    public void Active_Account_Is_Not_Deleted()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        _f.AddMember(owner, SystemRole.Owner);
        var user = _f.Accounts.Create(Input("user@example.com", "Юзер", "2"), owner.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Delete(user.Id, isOwner: false, owner.Id));

        // сначала снимают признак «активен» — вход закрывается сразу и обратимо
        Assert.Contains("активен", ex.Message);
        Assert.Contains(_f.Accounts.List(), a => a.Id == user.Id);
    }

    [Fact]
    public void Owner_Is_Never_Deleted_Even_When_Inactive()
    {
        var first = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        _f.AddMember(first, SystemRole.Owner);
        var second = _f.Accounts.Create(Input("second@example.com", "Второй", "2"), first.Id);
        _f.AddMember(second, SystemRole.Owner, first.Id);
        Deactivate(second, first.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Delete(second.Id, isOwner: true, first.Id));

        Assert.Contains("владелец", ex.Message);
        Assert.Contains(_f.Accounts.List(), a => a.Id == second.Id);
    }

    [Fact]
    public void Deleted_Account_Cannot_Sign_In()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер", phone: "+70000000001");

        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        Assert.Null(_f.Accounts.Authenticate("user@example.com", "пароль", isLocal: true).Account);
        Assert.Null(_f.Accounts.Authenticate("+70000000001", "пароль", isLocal: true).Account);
        Assert.Null(_f.Accounts.FindByLogin("user@example.com"));
    }

    [Fact]
    public void Deleted_Account_Frees_Its_Email_And_Phone()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер", phone: "+70000000001");
        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        // почта и телефон уникальны среди ЖИВЫХ аккаунтов — уволенный человек их не держит
        var again = _f.Accounts.Create(
            Input("user@example.com", "Новый", "3", phone: "+70000000001"), owner.Id);

        Assert.NotEqual(user.Id, again.Id);
        Assert.NotNull(_f.Accounts.Authenticate("user@example.com", "3", isLocal: true).Account);
    }

    [Fact]
    public void Second_Delete_Is_Refused()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер");
        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Delete(user.Id, isOwner: false, owner.Id));

        Assert.Contains("уже удалён", ex.Message);
    }

    [Fact]
    public void Unknown_Account_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => _f.Accounts.Delete("нет-такого", isOwner: false, null));
    }

    [Fact]
    public void Delete_Is_Logged_In_Server_Journal()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер");

        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        // события аккаунтов идут в СЕРВЕРНЫЙ журнал: аккаунт живёт вне организации (п. 6.4.1)
        var record = _f.ServerEvents.Query(limit: 200)
            .FirstOrDefault(e => e.EventType == "account.deleted" && e.EntityId == user.Id);
        Assert.NotNull(record);
        Assert.Contains("user@example.com", record!.PayloadJson);
    }

    [Fact]
    public void Accepting_Join_Request_Brings_Deleted_Account_Back()
    {
        // приём заявки на подключение сервера заводит заявителя ТЕМ ЖЕ ключом (T-139).
        // Если его тут когда-то удалили, «ПРИНЯТЬ» — это решение пустить обратно: иначе
        // человек стал бы участником организации, не имея возможности войти
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер");
        var hash = _f.Accounts.PasswordHashOf(user.Id);
        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);

        var back = _f.Accounts.Import(user.Id, "Юзер", "user@example.com", "", hash, owner.Id);

        Assert.Equal(user.Id, back.Id);
        Assert.Null(_f.Accounts.Get(user.Id)!.DeletedAt);
        Assert.Contains(_f.Accounts.List(), a => a.Id == user.Id);
        // пароль остался прежним — чужой пароль правкой по сети не меняют (T-139)
        Assert.NotNull(_f.Accounts.Authenticate("user@example.com", "пароль", isLocal: true).Account);
    }

    [Fact]
    public void Deleted_Account_Does_Not_Come_Back_Over_A_Live_Namesake()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = Deactivated(owner, "user@example.com", "Юзер");
        var hash = _f.Accounts.PasswordHashOf(user.Id);
        _f.Accounts.Delete(user.Id, isOwner: false, owner.Id);
        // почту удалённого занял новый живой аккаунт — вернуть старый нельзя
        _f.Accounts.Create(Input("user@example.com", "Новый человек", "3"), owner.Id);

        Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Import(user.Id, "Юзер", "user@example.com", "", hash, owner.Id));
        Assert.NotNull(_f.Accounts.Get(user.Id)!.DeletedAt);
    }

    /// <summary>Заведённый и тут же выключенный пользователь — исходное состояние для удаления.</summary>
    private Account Deactivated(Account owner, string email, string name, string phone = "")
    {
        var account = _f.Accounts.Create(Input(email, name, "пароль", phone), owner.Id);
        Deactivate(account, owner.Id);
        return account;
    }

    private void Deactivate(Account account, string? actorId)
    {
        var off = Input(account.Email, account.Name, null, account.Phone);
        off.IsActive = false;
        _f.Accounts.Update(account.Id, off, actorId);
    }

    private static AccountSaveInput Input(string email, string name, string? password, string phone = "") =>
        new()
        {
            Email = email,
            Name = name,
            Phone = phone,
            Password = password,
            IsActive = true,
            Role = SystemRole.Editor,
        };
}
