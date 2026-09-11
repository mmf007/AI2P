using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Api;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-150 (версия 1.74): ПРОДОЛЖЕНИЕ ПОПЫТКИ ПОДКЛЮЧИТЬ ДРУГОЙ СЕРВЕР — доработка T-148.
///
/// Два требования заказчика:
/// <list type="number">
/// <item>ЧЕЛОВЕК С ЭТОЙ ПОЧТОЙ НА ДИРИЖЁРЕ УЖЕ ЕСТЬ — сказать об этом и СПРОСИТЬ ЕГО ПАРОЛЬ
/// на дирижёре (по сути вход туда). Знает — пускать, не знает — нет. До T-150 дирижёр отдавал
/// внутренний ключ живого участника всякому, кто назвал его почту (T-148), а почту человека
/// знает кто угодно: назвавшись чужой почтой, можно было получить чужую личность
/// в организации;</item>
/// <item>ЗАЯВИТЕЛЬ, КОТОРОГО НЕТ СРЕДИ ИСПОЛНИТЕЛЕЙ ОРГАНИЗАЦИИ, — предложить завести его
/// прямо в форме подтверждения заявки на дирижёре. Он заводится и сам (T-139/T-146), но молча,
/// и про участие забывали: без исполнителя человек организацию не увидит и после репликации
/// (ТЗ п. 2.15).</item>
/// </list>
///
/// Проверка пароля живёт в <see cref="ClusterEndpoints.PasswordCheck"/> и здесь проверяется
/// напрямую; весь обмен целиком — живой проверкой двух серверов (<c>test/t150/</c>).
/// </summary>
public sealed class T150Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Account Account(string email, string name, string? password) =>
        _f.Accounts.Create(new AccountSaveInput
        {
            Email = email, Name = name, Password = password, IsActive = true,
        }, null);

    // ---------- 1. ключ человека выдаётся только по его паролю ----------

    /// <summary>Пароль не прислан вовсе — дирижёр его ПРОСИТ, а ключа не даёт.</summary>
    [Fact]
    public void Without_A_Password_The_Conductor_Asks_For_It()
    {
        var petr = Account("petr@mail", "Пётр", "petr123");

        Assert.Equal(ClusterJoinStatusDto.PasswordAsk,
            ClusterEndpoints.PasswordCheck(_f.Accounts, petr, ""));
    }

    /// <summary>Пароль не тот: это либо не тот человек, либо он его забыл — не пускаем.</summary>
    [Fact]
    public void A_Wrong_Password_Is_Refused()
    {
        var petr = Account("petr@mail", "Пётр", "petr123");

        Assert.Equal(ClusterJoinStatusDto.PasswordWrong,
            ClusterEndpoints.PasswordCheck(_f.Accounts, petr, "не тот"));
    }

    /// <summary>Пароль верный — ответ пустой, и ЭТО значит «отдавай ключ».</summary>
    [Fact]
    public void The_Right_Password_Opens_The_Way()
    {
        var petr = Account("petr@mail", "Пётр", "petr123");

        Assert.Equal("", ClusterEndpoints.PasswordCheck(_f.Accounts, petr, "petr123"));
    }

    /// <summary>
    /// У человека на дирижёре пароля НЕТ (так заводится хозяин установки, гл. 11). Пустой
    /// пароль «знают» все, поэтому доказательством он быть не может: отказ с объяснением,
    /// а не молчаливый пропуск.
    /// </summary>
    [Fact]
    public void An_Account_Without_A_Password_Cannot_Be_Taken_Over()
    {
        var petr = Account("petr@mail", "Пётр", "");

        Assert.Equal(ClusterJoinStatusDto.PasswordUnset,
            ClusterEndpoints.PasswordCheck(_f.Accounts, petr, ""));
        // и подобранный «пустой» пароль тоже не помогает
        Assert.Equal(ClusterJoinStatusDto.PasswordUnset,
            ClusterEndpoints.PasswordCheck(_f.Accounts, petr, "что угодно"));
    }

    /// <summary>Неудачная попытка видна владельцу: она пишется в серверный журнал как
    /// неудачный вход (п. 6.3) — подбор чужой личности по сети не должен быть тихим.</summary>
    [Fact]
    public void A_Failed_Attempt_Goes_To_The_Journal()
    {
        var petr = Account("petr@mail", "Пётр", "petr123");

        ClusterEndpoints.PasswordCheck(_f.Accounts, petr, "подбор");

        var failed = Assert.Single(_f.ServerEvents.Query(eventType: EventTypes.AccountLoginFailed));
        Assert.Contains("cluster-join", failed.PayloadJson);
    }

    // ---------- 2. пароль на этом сервере становится тем же, что на дирижёре ----------

    /// <summary>
    /// Пароль проверен на дирижёре, значит он известен и доказан — здешний аккаунт получает
    /// его же. Иначе до первой репликации человек входил бы сюда одним паролем, а после неё
    /// (строку аккаунта пришлёт дирижёр) — другим.
    /// </summary>
    [Fact]
    public void The_Local_Password_Becomes_The_Conductor_One()
    {
        var petr = Account("petr@mail", "Пётр", "здешний");

        _f.Accounts.SetPassword(petr.Id, "дирижёрский", null);

        Assert.Null(_f.Accounts.Authenticate("petr@mail", "здешний", isLocal: false).Account);
        Assert.NotNull(_f.Accounts.Authenticate("petr@mail", "дирижёрский", isLocal: false).Account);
    }

    /// <summary>Событие смены пароля в журнале есть — иначе непонятно, откуда он поменялся.</summary>
    [Fact]
    public void Setting_The_Password_Is_Logged()
    {
        var petr = Account("petr@mail", "Пётр", "здешний");

        _f.Accounts.SetPassword(petr.Id, "дирижёрский", null);

        Assert.Contains(_f.ServerEvents.Query(eventType: EventTypes.AccountPasswordChanged),
            e => e.EntityId == petr.Id && e.PayloadJson.Contains("cluster"));
    }

    [Fact]
    public void Setting_A_Password_Of_A_Missing_Account_Is_An_Error() =>
        Assert.Throws<ArgumentException>(() => _f.Accounts.SetPassword("нет такого", "x", null));

    // ---------- 3. однофамилец с ДРУГОЙ почтой — по-прежнему другой человек ----------

    /// <summary>Пароль спрашивают только про ту же ПОЧТУ: совпадение имени — это спор имён
    /// (T-141), и решается он на подключающемся сервере, а не паролем.</summary>
    [Fact]
    public void A_Namesake_Is_Still_A_Different_Person()
    {
        Account("petr@mail", "Пётр", "petr123");
        var namesake = new JoinApplicant("другой-ключ", "Пётр", "petrov@mail");

        var clash = AI2P.Server.Org.OrgRegistry.ApplicantClash(_f.Accounts, namesake);

        Assert.NotNull(clash);
        Assert.Contains("именем", clash);
    }

    // ---------- 4. участие в организации: есть или заведётся ----------

    /// <summary>
    /// «Есть ли заявитель среди исполнителей организации» — то, что показывает форма
    /// подтверждения. Участие — это исполнитель-человек с его аккаунтом (ТЗ п. 2.15).
    /// </summary>
    [Fact]
    public void Membership_Is_The_Executor_Bound_To_The_Account()
    {
        var petr = Account("petr@mail", "Пётр", "petr123");
        Assert.Null(_f.Executors.ByAccount(petr.Id));

        var member = _f.AddMember(petr, SystemRole.Editor);

        Assert.NotNull(_f.Executors.ByAccount(petr.Id));
        Assert.Equal("Пётр", member.InternalName);
        // повторное подтверждение заявки ничего не задваивает: тот же аккаунт — тот же человек
        Assert.Equal(member.Id, _f.AddMember(petr, SystemRole.Editor).Id);
    }
}
