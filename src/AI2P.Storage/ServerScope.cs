using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Storage;

/// <summary>
/// Кто мы в кластере с точки зрения ОДНОЙ организации (ТЗ гл. 6, этап 42): внутренний ключ
/// локального сервера, его код в этой организации (<c>S0</c>, <c>S1</c>, …) и признак дирижёра.
///
/// Сервисы хранилища живут в БД организации, а сами серверы — в СЕРВЕРНОЙ БД, и связаны они
/// через границу баз. Поэтому сюда передаются не таблицы, а три делегата: значения
/// спрашиваются на каждом обращении, потому что дирижёра переназначают на ходу, а контекст
/// организации живёт до конца работы приложения.
///
/// Правило владения одно на все сущности: сервер у строки указан — правит только он;
/// сервер не указан — правит только дирижёр (<see cref="Ownership.CanWrite"/>).
/// </summary>
public sealed class ServerScope
{
    private readonly Func<string> _serverId;
    private readonly Func<string> _code;
    private readonly Func<bool> _isConductor;
    private readonly Func<string, string> _codeOf;
    private readonly Func<string, string> _nameOf;

    /// <param name="serverId">Внутренний ключ (unid) ЛОКАЛЬНОГО сервера.</param>
    /// <param name="code">Код локального сервера в этой организации: S0, S1, …</param>
    /// <param name="isConductor">Локальный сервер — дирижёр этой организации.</param>
    /// <param name="codeOf">Код произвольного сервера в этой организации по его unid.</param>
    /// <param name="nameOf">Имя произвольного сервера по его unid (для текстов ошибок).</param>
    public ServerScope(Func<string> serverId, Func<string> code, Func<bool> isConductor,
        Func<string, string>? codeOf = null, Func<string, string>? nameOf = null)
    {
        _serverId = serverId;
        _code = code;
        _isConductor = isConductor;
        _codeOf = codeOf ?? (_ => "");
        _nameOf = nameOf ?? (id => id);
    }

    /// <summary>Одиночная установка без кластера: сервер один, он же дирижёр, кода нет —
    /// номера остаются прежними (T-18). Используется тестами и как безопасное умолчание.</summary>
    public static ServerScope Standalone(string serverId = "local") =>
        new(() => serverId, () => "", () => true);

    /// <summary>Внутренний ключ локального сервера (unid).</summary>
    public string ServerId => _serverId();

    /// <summary>Код локального сервера в организации: S0, S1, … Пусто — сервер с организацией
    /// не связан (одиночная установка): номера тогда без суффикса.</summary>
    public string Code => _code();

    /// <summary>Локальный сервер — дирижёр организации (ТЗ гл. 6).</summary>
    public bool IsConductor => _isConductor();

    /// <summary>Код произвольного сервера организации по его unid; пусто — сервер не найден.</summary>
    public string CodeOf(string? serverId) =>
        serverId is { Length: > 0 } id ? _codeOf(id) : "";

    /// <summary>Имя произвольного сервера по его unid — для подсказок и текстов ошибок.</summary>
    public string NameOf(string? serverId) =>
        serverId is { Length: > 0 } id ? _nameOf(id) : "";

    /// <summary>Строку с таким владельцем можно править здесь (ТЗ гл. 6). Правило то же, что
    /// в <see cref="Ownership.CanWrite"/>, но дирижёрство спрашивается ТОЛЬКО у бесхозной
    /// строки (T-363-S0): у общего метода оба довода считаются до вызова, а «мы ли дирижёр» —
    /// это поход в серверную БД, и в списке задач он случался на каждую строку.</summary>
    public bool CanWrite(string? ownerServerId) =>
        ownerServerId is { Length: > 0 } owner ? owner == ServerId : IsConductor;

    /// <summary>Владелец строки — ЭТОТ сервер (не «дирижёр правит бесхозное», а именно свой).</summary>
    public bool IsMine(string? ownerServerId) =>
        ownerServerId is { Length: > 0 } owner && owner == ServerId;

    /// <summary>
    /// Проверка владения перед записью (ТЗ гл. 6): чужую строку править нельзя ни через UI,
    /// ни через API — сообщение подсказывает, на каком сервере она правится.
    /// </summary>
    /// <param name="what">Что правим — попадает в текст ошибки: «Задача», «Расписание», …</param>
    public void EnsureCanWrite(string? ownerServerId, string what)
    {
        if (CanWrite(ownerServerId))
        {
            return;
        }
        throw new ArgumentException(ownerServerId is { Length: > 0 }
            ? Loc.T("msg.serverScope.1", what, Describe(ownerServerId))
            : Loc.T("msg.serverScope.2", what));
    }

    /// <summary>Сервер для человека: «S1 (ноутбук)» либо просто код/ключ.</summary>
    public string Describe(string? serverId)
    {
        var code = CodeOf(serverId);
        var name = NameOf(serverId);
        if (code.Length > 0 && name.Length > 0)
        {
            return $"{code} ({name})";
        }
        return code.Length > 0 ? code : name;
    }
}
