using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo39 / этап 39 (ТЗ v1.46): аккаунты пользователей и вход в систему на одном сервере —
/// пароли, привязка исполнителя к аккаунту, пер-пользовательское состояние UI и права по ролям.
/// </summary>
public sealed class Todo39Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    // --- хэш пароля (ТЗ гл. 12): PBKDF2-SHA256, пустой пароль допустим ---

    [Fact]
    public void PasswordHash_Verifies_Only_Correct_Password()
    {
        var hash = PasswordHash.Hash("тайна-123");

        Assert.NotEqual("тайна-123", hash);
        Assert.StartsWith("pbkdf2$sha256$", hash);
        Assert.True(PasswordHash.Verify(hash, "тайна-123"));
        Assert.False(PasswordHash.Verify(hash, "тайна-124"));
        Assert.False(PasswordHash.Verify(hash, ""));
    }

    [Fact]
    public void PasswordHash_Empty_Password_Stays_Empty()
    {
        var hash = PasswordHash.Hash("");

        Assert.Equal("", hash);
        Assert.True(PasswordHash.IsEmpty(hash));
        Assert.True(PasswordHash.Verify(hash, ""));
        Assert.False(PasswordHash.Verify(hash, "что-то"));
    }

    [Fact]
    public void PasswordHash_Uses_Random_Salt()
    {
        Assert.NotEqual(PasswordHash.Hash("один"), PasswordHash.Hash("один"));
    }

    [Fact]
    public void PasswordHash_Rejects_Broken_Record()
    {
        Assert.False(PasswordHash.Verify("мусор", "мусор"));
        Assert.False(PasswordHash.Verify("pbkdf2$sha256$нечисло$c2FsdA==$aGFzaA==", "пароль"));
    }

    // --- аккаунты (ТЗ п. 2.14) ---

    [Fact]
    public void First_Account_Becomes_Owner_With_Executor()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "секрет"), null);
        // участие в организации — отдельный шаг (ТЗ п. 2.15, этап 40): аккаунт живёт в
        // серверной БД и организаций не знает, исполнителя заводит уровень приложения
        _f.AddMember(account, SystemRole.Owner);

        Assert.Equal("A-1", account.DisplayId);
        Assert.True(account.HasPassword);
        var executor = _f.Executors.ByAccount(account.Id);
        Assert.NotNull(executor);
        // первый аккаунт всегда владелец — иначе системой некому управлять
        Assert.Equal(SystemRole.Owner, executor!.SystemRole);
        Assert.Equal(ExecutorKind.Human, executor.Kind);
        Assert.Equal(account.Id, executor.AccountId);
    }

    [Fact]
    public void Second_Account_Gets_Requested_Role()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        _f.AddMember(owner, SystemRole.Owner);
        var account = _f.Accounts.Create(Input("reader@example.com", "Читатель", "2"), owner.Id);

        _f.AddMember(account, SystemRole.Reader, owner.Id);

        // роль принадлежит исполнителю ОРГАНИЗАЦИИ (п. 2.2): в разных организациях
        // у одного аккаунта роли разные, поэтому в самом аккаунте её нет
        Assert.Equal(SystemRole.Reader, _f.Executors.ByAccount(account.Id)!.SystemRole);
    }

    [Fact]
    public void Email_And_Phone_Are_Unique()
    {
        _f.Accounts.Create(Input("mike@example.com", "Майк", "1", phone: "+70000000001"), null);

        Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Create(Input("MIKE@example.com", "Двойник", "1"), null));
        Assert.Throws<ArgumentException>(() =>
            _f.Accounts.Create(Input("other@example.com", "Другой", "1", phone: "+70000000001"), null));
    }

    [Fact]
    public void Email_Must_Look_Like_Email()
    {
        Assert.Throws<ArgumentException>(() => _f.Accounts.Create(Input("mike", "Майк", "1"), null));
        Assert.Throws<ArgumentException>(() => _f.Accounts.Create(Input("", "Майк", "1"), null));
    }

    // --- вход (ТЗ гл. 12) ---

    [Fact]
    public void Authenticate_Accepts_Email_And_Phone()
    {
        _f.Accounts.Create(Input("mike@example.com", "Майк", "секрет", phone: "+70000000001"), null);

        Assert.NotNull(_f.Accounts.Authenticate("mike@example.com", "секрет", isLocal: true).Account);
        Assert.NotNull(_f.Accounts.Authenticate("+70000000001", "секрет", isLocal: true).Account);
        Assert.Null(_f.Accounts.Authenticate("mike@example.com", "не то", isLocal: true).Account);
        Assert.Null(_f.Accounts.Authenticate("нет@example.com", "секрет", isLocal: true).Account);
    }

    [Fact]
    public void Empty_Password_Is_Local_Only()
    {
        _f.Accounts.Create(Input("mike@example.com", "Майк", ""), null);

        // пустой пароль допустим (ТЗ гл. 11), но по сети с ним не пускаем: HTTPS ещё нет
        Assert.NotNull(_f.Accounts.Authenticate("mike@example.com", "", isLocal: true).Account);
        var remote = _f.Accounts.Authenticate("mike@example.com", "", isLocal: false);
        Assert.Null(remote.Account);
        Assert.Contains("сети", remote.Error);
    }

    [Fact]
    public void Inactive_Account_Cannot_Sign_In()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = _f.Accounts.Create(Input("user@example.com", "Юзер", "2"), owner.Id);

        var off = Input("user@example.com", "Юзер", null);
        off.IsActive = false;
        _f.Accounts.Update(user.Id, off, owner.Id);

        Assert.Null(_f.Accounts.Authenticate("user@example.com", "2", isLocal: true).Account);
    }

    [Fact]
    public void ChangePassword_Requires_Current_Password()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "старый"), null);

        Assert.Throws<ArgumentException>(() =>
            _f.Accounts.ChangePassword(account.Id, "не тот", "новый", null));

        _f.Accounts.ChangePassword(account.Id, "старый", "новый", null);
        Assert.NotNull(_f.Accounts.Authenticate("mike@example.com", "новый", isLocal: true).Account);
        Assert.Null(_f.Accounts.Authenticate("mike@example.com", "старый", isLocal: true).Account);
    }

    [Fact]
    public void Owner_Can_Reset_Password_Without_Knowing_It()
    {
        var owner = _f.Accounts.Create(Input("owner@example.com", "Владелец", "1"), null);
        var user = _f.Accounts.Create(Input("user@example.com", "Юзер", "старый"), owner.Id);

        _f.Accounts.Update(user.Id, Input("user@example.com", "Юзер", "сброшенный"), owner.Id);

        Assert.NotNull(_f.Accounts.Authenticate("user@example.com", "сброшенный", isLocal: true).Account);
    }

    [Fact]
    public void Self_Update_Keeps_Email_And_Password()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "секрет"), null);

        var updated = _f.Accounts.UpdateSelf(account.Id, "Михаил", "+70000000002", null);

        Assert.Equal("Михаил", updated.Name);
        Assert.Equal("+70000000002", updated.Phone);
        Assert.Equal("mike@example.com", updated.Email);
        Assert.NotNull(_f.Accounts.Authenticate("mike@example.com", "секрет", isLocal: true).Account);
    }

    // --- привязка исполнителя к аккаунту (ТЗ п. 2.14) ---

    [Fact]
    public void Account_Cannot_Be_Linked_To_Two_Executors()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "1"), null);
        _f.AddMember(account, SystemRole.Owner);

        Assert.Throws<ArgumentException>(() => _f.Executors.Create(new Executor
        {
            Nick = "второй",
            Kind = ExecutorKind.Human,
            AccountId = account.Id,
        }, null));
    }

    [Fact]
    public void Ai_Executor_Never_Keeps_Account()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "1"), null);
        _f.Models.Seed();
        var model = _f.Models.List().First(m => m.IsActive);

        var ai = _f.Executors.Create(new Executor
        {
            Nick = "агент",
            Kind = ExecutorKind.Ai,
            ModelId = model.Id,
            AccountId = account.Id,
        }, null);

        Assert.Null(_f.Executors.Get(ai.Id)!.AccountId);
    }

    // --- состояние UI пер-пользователя (ТЗ v1.46) ---

    [Fact]
    public void AppState_Is_Per_Account()
    {
        var one = _f.Accounts.Create(Input("one@example.com", "Один", "1"), null);
        var two = _f.Accounts.Create(Input("two@example.com", "Два", "2"), one.Id);

        _f.AppState.Set(one.Id, AppStateService.UiLayoutKey, "{\"split\":\"quad\"}");
        _f.AppState.Set(two.Id, AppStateService.UiLayoutKey, "{\"split\":\"single\"}");

        // лейауты не затирают друг друга — раньше ключ был один на всю базу
        Assert.Equal("{\"split\":\"quad\"}", _f.AppState.Get(one.Id, AppStateService.UiLayoutKey));
        Assert.Equal("{\"split\":\"single\"}", _f.AppState.Get(two.Id, AppStateService.UiLayoutKey));
        Assert.Null(_f.AppState.Get("нет-такого", AppStateService.UiLayoutKey));
    }

    [Fact]
    public void AppState_Set_Null_Removes_Value()
    {
        var account = _f.Accounts.Create(Input("one@example.com", "Один", "1"), null);
        _f.AppState.Set(account.Id, AppStateService.CurrentProjectKey, "PRJ");

        _f.AppState.Set(account.Id, AppStateService.CurrentProjectKey, null);

        Assert.Null(_f.AppState.Get(account.Id, AppStateService.CurrentProjectKey));
    }

    // --- журнал работ (ТЗ п. 6.4.3): вход и правки аккаунтов видны в истории ---

    [Fact]
    public void Account_Changes_Are_Logged()
    {
        var account = _f.Accounts.Create(Input("mike@example.com", "Майк", "старый"), null);
        _f.Accounts.ChangePassword(account.Id, "старый", "новый", null);

        // события аккаунтов пишутся в СЕРВЕРНЫЙ журнал (ТЗ п. 6.4.1, этап 40): вход
        // происходит до выбора организации, в её журнал он попасть не может
        var types = _f.ServerEvents.Query(limit: 200).Select(e => e.EventType).ToList();
        Assert.Contains("account.created", types);
        Assert.Contains("account.password_changed", types);
        // значение пароля в журнал не попадает (ТЗ п. 6.3)
        Assert.DoesNotContain(_f.ServerEvents.Query(limit: 200), e => e.PayloadJson.Contains("старый"));
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
