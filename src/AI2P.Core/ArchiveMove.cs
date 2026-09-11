using AI2P.Core.Entities;

namespace AI2P.Core;

/// <summary>
/// ЧТО ИМЕННО ПЕРЕНОСИТСЯ В АРХИВ (T-42-S0, выпуск 1.105) — виды переноса, общие на все слои.
///
/// Перенос всегда идёт в ТЕКУЩИЙ архив, и обратный перенос (восстановление) — тоже только
/// из текущего: в прочих архивах данные уже не меняются, их можно лишь смотреть.
/// </summary>
public static class ArchiveMoveTargets
{
    /// <summary>Задача ВЕРХНЕГО УРОВНЯ со всеми потомками. Иерархия архивируется целиком:
    /// оставить подзадачу без родителя значило бы оставить в работе то, чего не открыть.</summary>
    public const string Task = "task";

    /// <summary>Узел шаблона верхнего уровня со всеми дочерними узлами.</summary>
    public const string Template = "template";

    /// <summary>Объект проекта со всеми дочерними объектами (эталонные кадры, датасеты).</summary>
    public const string Object = "object";

    /// <summary>Одна запись опыта со всем сопутствующим (тэги, файлы ссылок).</summary>
    public const string Experience = "experience";

    /// <summary>ПРОЕКТ ЦЕЛИКОМ: его задачи, шаблоны, объекты, опыт, логи и сам проект
    /// с его настройками.</summary>
    public const string Project = "project";

    public static readonly string[] All = [Task, Template, Object, Experience, Project];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);

    /// <summary>
    /// СОСТОЯНИЕ, В КОТОРОМ ЗАДАЧУ МОЖНО АРХИВИРОВАТЬ (по заданию): черновик, проверка,
    /// готово, отменено. Три последних — это <see cref="TaskStatuses.Settled"/> («работа
    /// сдана»), четвёртое — черновик, до которого работа так и не дошла. Всё остальное
    /// означает незаконченную работу, и уносить её нельзя ни одной задачей, ни в составе
    /// иерархии, ни в составе проекта.
    /// </summary>
    public static bool CanArchive(string? status) =>
        status is TaskStatuses.Draft || (status is not null && TaskStatuses.Settled(status));
}

/// <summary>
/// ССЫЛКА, ПЕРЕПИСАННАЯ НА АРХИВ (T-42-S0).
///
/// Ссылка в тексте задачи ведёт на другую задачу или на файл РАБОЧЕЙ среды
/// (<c>api/files/project?…</c>, <c>api/files/raw?…</c>, <c>/task/&lt;id&gt;</c>). После
/// переноса такой текст лежит в архиве, а ссылки из него по-прежнему уводили бы в рабочую
/// среду — на задачу, которой там уже нет. Менять их на лету при просмотре дорого и хрупко
/// (просмотр архива идёт теми же готовыми страницами), поэтому они переписываются ОДИН РАЗ,
/// при переносе: к своему адресу добавляется параметр <c>arc=&lt;код архива&gt;</c>.
///
/// Параметр выбран вместо отдельного пути намеренно: адрес остаётся тем же самым, разбор
/// (<see cref="FileLinks"/>, <see cref="LinkUrls"/>) не меняется вовсе, а страница просмотра
/// архива (T-45-S0) читает из него, в какой среде искать задачу или файл. При восстановлении
/// параметр снимается тем же кодом — ссылка возвращается к рабочему виду.
/// </summary>
public static class ArchiveLinks
{
    /// <summary>Имя параметра, которым в адресе назван архив.</summary>
    public const string Param = "arc";

    /// <summary>Наша ли это ссылка — на файл AI2P либо на карточку задачи. Только такие
    /// ссылки переписываются: внешний адрес переписывать нечем и незачем.</summary>
    public static bool IsOurs(string? url)
    {
        var value = (url ?? "").Trim();
        return value.Length > 0
               && (FileLinks.Parse(value) is not null
                   || value.Contains("/task/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Код архива, названный в ссылке; пусто — ссылка рабочей среды.</summary>
    public static string CodeOf(string? url)
    {
        var value = (url ?? "").Trim();
        var at = IndexOfParam(value);
        if (at < 0)
        {
            return "";
        }
        var tail = value[(at + Param.Length + 2)..];
        var end = tail.IndexOfAny(['&', '#']);
        try
        {
            return Uri.UnescapeDataString(end < 0 ? tail : tail[..end]).Trim();
        }
        catch (UriFormatException)
        {
            return "";
        }
    }

    /// <summary>
    /// Тот же адрес, отнесённый к архиву <paramref name="code"/>; пустой код — адрес
    /// рабочей среды (так снимается пометка при восстановлении). Прежняя пометка убирается
    /// всегда, поэтому повторный перенос не даёт двух параметров подряд.
    /// </summary>
    public static string With(string? url, string? code)
    {
        var value = (url ?? "").Trim();
        var hash = value.IndexOf('#');
        var fragment = hash >= 0 ? value[hash..] : "";
        var head = Strip(hash >= 0 ? value[..hash] : value);
        if (string.IsNullOrWhiteSpace(code))
        {
            return head + fragment;
        }
        var sep = head.Contains('?') ? '&' : '?';
        return head + sep + Param + "=" + Uri.EscapeDataString(code.Trim()) + fragment;
    }

    /// <summary>Адрес без пометки архива.</summary>
    private static string Strip(string url)
    {
        var at = IndexOfParam(url);
        if (at < 0)
        {
            return url;
        }
        var tail = url[(at + Param.Length + 2)..];
        var amp = tail.IndexOf('&');
        // «?arc=…&x=1» → «?x=1», «?x=1&arc=…» → «?x=1», «?arc=…» → адрес без запроса вовсе
        var rest = amp < 0 ? "" : tail[(amp + 1)..];
        var head = url[..at];
        return rest.Length == 0 ? head : head + (url[at] == '?' ? "?" : "&") + rest;
    }

    /// <summary>Позиция разделителя перед «arc=»; -1 — пометки нет.</summary>
    private static int IndexOfParam(string url)
    {
        foreach (var sep in new[] { '?', '&' })
        {
            var at = url.IndexOf(sep + Param + "=", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                return at;
            }
        }
        return -1;
    }
}
