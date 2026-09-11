using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-36-S0: ПЕРЕДАЧА ШАБЛОНА НА ДРУГОЙ СЕРВЕР. Прежде шаблоны принадлежали не компьютеру,
/// а РОЛИ: они заводились только на дирижёре, сервера не имели вовсе и вместе со сменой
/// дирижёра уезжали с того компьютера, где с ними работали (та же мина, что T-1-S0 чинил
/// у задач). Теперь у шаблона есть сервер-владелец, он показывается в списке и в карточке,
/// а кнопка «сменить сервер» передаёт шаблон ВСЕМ ЕГО ПОДДЕРЕВОМ узлов.
///
/// Оба сервера работают с одной базой (как в Todo42Tests): проверяются правила владения.
/// </summary>
public sealed class T36S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    private const string ConductorId = "unid-S0";
    private const string OtherId = "unid-S1";
    private const string ThirdId = "unid-S2";

    private ServerScope ConductorScope => new(
        () => ConductorId, () => "S0", () => true,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private ServerScope OtherScope => new(
        () => OtherId, () => "S1", () => false,
        codeOf: id => id == ConductorId ? "S0" : id == OtherId ? "S1" : "",
        nameOf: id => id == ConductorId ? "главный" : id == OtherId ? "ноутбук" : "");

    private TaskService Tasks(ServerScope scope) => new(_f.Db, _f.Events, _f.Files, scope);

    public void Dispose() => _f.Dispose();

    [Fact]
    public void A_Template_Has_A_Server_And_Is_Shown_With_It()
    {
        var head = Tasks(ConductorScope)
            .Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);

        // сервер проставлен и уезжает наружу вместе с кодом — из него список рисует колонку
        Assert.Equal(ConductorId, head.ServerId);
        Assert.Equal("S0", Tasks(ConductorScope).Get(head.Id)!.ServerCode);
        Assert.False(Tasks(ConductorScope).Get(head.Id)!.IsReadOnly);
        // на чужом сервере тот же шаблон — только для чтения
        Assert.True(Tasks(OtherScope).Get(head.Id)!.IsReadOnly);
    }

    [Fact]
    public void Changing_The_Server_Moves_The_Whole_Template_Subtree()
    {
        var conductor = Tasks(ConductorScope);
        var head = conductor.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var node = conductor.Create(
            new TaskItem { Title = "Узел", IsTemplate = true, ParentId = head.Id }, "", "", null);

        var moved = conductor.ChangeServer(head.Id, OtherId, null);

        Assert.Equal(OtherId, moved.ServerId);
        // номер не меняется — это идентификатор и имя каталога файлов
        Assert.Equal(head.DisplayId, moved.DisplayId);
        // узлы шаблона уехали вместе с головой: половина дерева у прежнего сервера была бы
        // там нередактируемой
        Assert.Equal(OtherId, conductor.Get(node.Id)!.ServerId);
        // право записи перешло: здесь только чтение, там — правка
        Assert.True(conductor.Get(head.Id)!.IsReadOnly);
        Assert.False(Tasks(OtherScope).Get(head.Id)!.IsReadOnly);
        Assert.Throws<ArgumentException>(() =>
            conductor.ChangeServer(head.Id, ConductorId, null));
    }

    [Fact]
    public void A_Foreign_Node_Inside_The_Subtree_Stays_With_Its_Owner()
    {
        var conductor = Tasks(ConductorScope);
        var head = conductor.Create(new TaskItem { Title = "Шаблон", IsTemplate = true }, "", "", null);
        var foreign = Tasks(OtherScope).Create(
            new TaskItem { Title = "Чужой узел", IsTemplate = true, ParentId = head.Id }, "", "", null);

        conductor.ChangeServer(head.Id, ThirdId, null);

        // переезжают только узлы прежнего владельца головы, чужой остаётся своему серверу
        Assert.Equal(ThirdId, conductor.Get(head.Id)!.ServerId);
        Assert.Equal(OtherId, conductor.Get(foreign.Id)!.ServerId);
    }
}
