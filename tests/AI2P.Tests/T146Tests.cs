using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-146 (версия 1.73): ОШИБКА ПРИ ПОДКЛЮЧЕНИИ СЕРВЕРА — третий заход (после T-139 и T-141).
///
/// Случай заказчика: пользователя удалили на дирижёре (T-140), потом завели ТОГО ЖЕ человека
/// на новом сервере и подали заявку на подключение. Заявка ушла нормально, а «ПРИНЯТЬ»
/// на дирижёре отвечало «Человек с внутренним именем «…» уже есть» — и это ломало всё сразу:
/// <list type="number">
/// <item>удаление аккаунта (T-140) оставляло его ИСПОЛНИТЕЛЯ в организации, а внутреннее имя
/// человека уникально — заведение того же человека падало и руками, и приёмом заявки;</item>
/// <item>приём заявки успевал подтвердить связь ДО заведения человека, поэтому обрыв оставлял
/// подключение наполовину сделанным: сервер принят, заявки в списке нет, а заявитель
/// не участник организации — на своём сервере он видел «вы не состоите ни в одной
/// организации» и работать не мог;</item>
/// <item>репликация с только что принятым сервером падала «не выдал нам токен доступа» —
/// встречный токен он присылает сам, и до этого звонить ему просто некуда;</item>
/// <item>попутно: первая же репликация затирала у дирижёра «кто просил подключить» и запись
/// о заявителе — партнёр этих колонок не знает и шлёт их пустыми.</item>
/// </list>
/// </summary>
public sealed class T146Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Account Account(string email, string name) =>
        _f.Accounts.Create(new AccountSaveInput { Email = email, Name = name, Password = "x" }, null);

    /// <summary>Удалить пользователя так, как это делает интерфейс (T-140 + T-146):
    /// сначала снять «активен», потом удалить, потом погасить участие в организации.</summary>
    private void Delete(Account account, Account actor)
    {
        _f.Accounts.Update(account.Id, new AccountSaveInput
        {
            Name = account.Name, Email = account.Email, Phone = "", IsActive = false,
        }, actor.Id);
        _f.Accounts.Delete(account.Id, isOwner: false, actor.Id);
        _f.Executors.SuspendMember(account.Id, actor.Id);
    }

    // ---------- 1. удаление гасит участие, но исполнителя не вычёркивает ----------

    [Fact]
    public void Deleting_Account_Suspends_Membership_But_Keeps_The_Person()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        var petr = Account("petr@a", "Пётр");
        var member = _f.AddMember(petr, SystemRole.Editor, owner.Id);

        Delete(petr, owner);

        // исполнитель на месте: он актор прошлых действий в журнале работ (ТЗ п. 6.4.3)
        var kept = _f.Executors.Get(member.Id);
        Assert.NotNull(kept);
        Assert.Null(kept!.DeletedAt);
        // но погашен: в подбор исполнителей и в состав команд удалённый больше не попадает
        Assert.False(kept.IsActive);
    }

    // ---------- 2. тот же человек возвращается к своему исполнителю ----------

    /// <summary>Ровно то, на чём падал заказчик: заводим удалённого человека заново.</summary>
    [Fact]
    public void The_Same_Person_Comes_Back_To_The_Same_Executor()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        var petr = Account("petr@a", "Пётр");
        var was = _f.AddMember(petr, SystemRole.Editor, owner.Id);
        Delete(petr, owner);

        // почта освободилась вместе с удалением (T-140), поэтому аккаунт заводится тот же
        var again = Account("petr@a", "Пётр");
        var now = _f.AddMember(again, SystemRole.Editor, owner.Id);

        Assert.Equal(was.Id, now.Id);            // исполнитель ТОТ ЖЕ — история осталась за ним
        Assert.Equal(again.Id, now.AccountId);   // и привязан к новому аккаунту
        Assert.True(now.IsActive);
        Assert.Equal(2, _f.Executors.List().Count);   // владелец и Пётр, без двойников
    }

    /// <summary>Тот же аккаунт (один ключ на весь кластер) — тем более тот же исполнитель.</summary>
    [Fact]
    public void The_Same_Account_Reuses_Its_Executor()
    {
        var owner = Account("owner@a", "Хозяин");
        var first = _f.AddMember(owner, SystemRole.Owner);

        var again = _f.AddMember(owner, SystemRole.Owner);

        Assert.Equal(first.Id, again.Id);
        Assert.Single(_f.Executors.List());
    }

    // ---------- 3. однофамильцы не склеиваются ----------

    [Fact]
    public void A_Namesake_With_Another_Email_Becomes_A_Separate_Person()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        var petr = Account("petr@a", "Пётр");
        var his = _f.AddMember(petr, SystemRole.Editor, owner.Id);
        Delete(petr, owner);

        // тёзка с ДРУГОЙ почтой — другой человек, чужую историю ему отдавать нельзя
        var other = Account("petrov@a", "Пётр");
        var second = _f.AddMember(other, SystemRole.Editor, owner.Id);

        Assert.NotEqual(his.Id, second.Id);
        Assert.Equal("Пётр 2", second.InternalName);   // имя уникально (ТЗ п. 2.2)
        Assert.Equal(petr.Id, _f.Executors.Get(his.Id)!.AccountId);   // прежний не тронут
    }

    /// <summary>ЖИВОЙ однофамилец подавно не отдаёт своего исполнителя — и заведение
    /// участника всё равно не падает: имя получает номер.</summary>
    [Fact]
    public void A_Living_Namesake_Never_Loses_His_Executor()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        var petr = Account("petr@a", "Пётр");
        var his = _f.AddMember(petr, SystemRole.Editor, owner.Id);

        var other = Account("petrov@a", "Пётр");
        var second = _f.AddMember(other, SystemRole.Editor, owner.Id);

        Assert.NotEqual(his.Id, second.Id);
        Assert.Equal("Пётр", _f.Executors.Get(his.Id)!.InternalName);
        Assert.Equal("Пётр 2", second.InternalName);
    }

    /// <summary>Человек, заведённый БЕЗ входа (исполнитель без аккаунта), получает аккаунт —
    /// а не второго себя: именно так людям выдают логины позже.</summary>
    [Fact]
    public void A_Person_Without_A_Login_Gets_One()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        var offline = _f.Executors.Create(new Executor
        {
            Nick = "сидор", InternalName = "Сидор Сидоров", Kind = ExecutorKind.Human,
            SystemRole = SystemRole.Editor,
        }, owner.Id);

        var account = Account("sidor@a", "Сидор Сидоров");
        var member = _f.AddMember(account, SystemRole.Editor, owner.Id);

        Assert.Equal(offline.Id, member.Id);
        Assert.Equal(account.Id, member.AccountId);
    }

    // ---------- 4. заведение участника не падает НИКОГДА ----------

    /// <summary>Главное свойство правки: <c>EnsureMember</c> не бросает исключений. На этом
    /// держится и приём заявки, и заведение пользователя в настройках.</summary>
    [Fact]
    public void Membership_Is_Always_Granted()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);
        for (var i = 0; i < 5; i++)
        {
            var twin = Account($"twin{i}@a", "Пётр");
            var member = _f.AddMember(twin, SystemRole.Editor, owner.Id);
            Assert.Equal(twin.Id, member.AccountId);
            Assert.NotNull(_f.Executors.ByAccount(twin.Id));
        }
        Assert.Equal(6, _f.Executors.List().Count);
        Assert.Equal(5, _f.Executors.List().Count(e => e.InternalName.StartsWith("Пётр")));
    }

    /// <summary>Свободное внутреннее имя ищется среди ЛЮДЕЙ: имя ИИ-исполнителя (оно берётся
    /// из справочника моделей) человеку не мешает.</summary>
    [Fact]
    public void Unique_Internal_Name_Counts_People_Only()
    {
        var owner = Account("owner@a", "Хозяин");
        _f.AddMember(owner, SystemRole.Owner);

        Assert.Equal("Пётр", _f.Executors.UniqueInternalName("Пётр"));
        // регистр не спасает от совпадения: имя сравнивается без учёта регистра
        Assert.Equal("хозяин 2", _f.Executors.UniqueInternalName("хозяин"));
        Assert.Equal("человек", _f.Executors.UniqueInternalName("   "));
    }

    // ---------- 5. приём заявки: сначала человек, потом связь ----------

    /// <summary>
    /// Порядок действий в <c>OrgRegistry.AcceptJoinRequest</c> проверяется живой проверкой
    /// (<c>test/t146/live.py</c>) — здесь стережём его следствие на уровне хранилища: пока
    /// решение не принято, связь остаётся заявкой и сервер в кластере не работает.
    /// </summary>
    [Fact]
    public void Pending_Request_Keeps_The_Server_Out_Of_The_Cluster()
    {
        var org = _f.CreateOrgWithLocalServer("Фирма", "acme");
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Protocol = "http", Hostname = "10.0.0.5", Port = 5480,
            BasePath = "/ai2p", IsActive = false,
        }, null);
        var link = _f.Servers.Attach(org.Id, peer.Id, OrgServerStatus.Pending, "Пётр <petr@a>", null,
            applicant: new JoinApplicant("acc-1", "Пётр", "petr@a", "hash"));

        Assert.Equal(OrgServerStatus.Pending, _f.Servers.LinkById(link.Id)!.Status);
        Assert.Contains(_f.Servers.Requests(), r => r.Id == link.Id);
        Assert.False(_f.Servers.Get(peer.Id)!.IsActive);

        _f.Servers.AcceptRequest(link.Id, null);

        Assert.Equal(OrgServerStatus.Active, _f.Servers.LinkById(link.Id)!.Status);
        Assert.DoesNotContain(_f.Servers.Requests(), r => r.Id == link.Id);
        Assert.True(_f.Servers.Get(peer.Id)!.IsActive);
    }

    /// <summary>Запись заявителя переживает подтверждение — по ней чинится недоделанное
    /// подключение (<c>OrgRegistry.FinishAcceptedJoins</c>); стирается только хэш пароля.</summary>
    [Fact]
    public void Applicant_Survives_The_Decision_Except_The_Password()
    {
        var org = _f.CreateOrgWithLocalServer("Фирма", "acme");
        var peer = _f.Servers.Create(new ServerSaveInput
        {
            Name = "ubuntu", Protocol = "http", Hostname = "10.0.0.5", Port = 5480, BasePath = "/ai2p",
        }, null);
        var link = _f.Servers.Attach(org.Id, peer.Id, OrgServerStatus.Pending, "Пётр <petr@a>", null,
            applicant: new JoinApplicant("acc-1", "Пётр", "petr@a", "hash"));

        _f.Servers.AcceptRequest(link.Id, null);
        _f.Servers.ForgetApplicantSecret(link.Id);

        var stored = _f.Servers.LinkById(link.Id)!;
        Assert.Equal("acc-1", stored.ApplicantId);
        Assert.Equal("petr@a", stored.ApplicantEmail);
        Assert.Equal("Пётр <petr@a>", stored.RequestedBy);
        Assert.Equal("", stored.ApplicantHash);
    }
}
