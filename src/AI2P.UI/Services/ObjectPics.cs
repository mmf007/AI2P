using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.UI.Services;

/// <summary>
/// ПРЕВЬЮ ОБЪЕКТОВ СПИСКА (T-276): id объекта → адрес картинки, которую показать слева
/// от строки.
///
/// Считается ОДИН РАЗ на прочитанный список, а не на каждую отрисовку: список объектов
/// перерисовывается на любое изменение состояния, а раскладка иерархии от этого не
/// меняется. Сама картинка при этом не читается вовсе — здесь строится только адрес,
/// тянет её потом браузер сам (<c>loading="lazy"</c>), поэтому список рисуется сразу,
/// а превью появляются по мере загрузки.
///
/// ЧТО СЧИТАЕТСЯ ПРЕВЬЮ ОБЪЕКТА:
/// <list type="number">
///   <item>его собственный файл, если это картинка (<c>PathOrUrl</c>);</item>
///   <item>иначе — первая картинка ВНУТРЕННЕГО СПИСКА: у персонажа своего файла обычно
///   нет, его внешность показывают эталонные кадры-дети (а с T-274 кадры лежат ещё уровнем
///   ниже, внутри датасета) — поэтому обход идёт вглубь, а не на один уровень.</item>
/// </list>
/// Картинка узнаётся ПО РАСШИРЕНИЮ (<see cref="ProjectFiles.IsImage"/>), существование
/// файла здесь не проверяется: на список в сотню строк это была бы сотня запросов к
/// серверу ради того, что браузер и так выяснит, забирая картинку. Файла нет — превью
/// гасит <c>ai2p.initPics</c> на стороне браузера.
/// </summary>
public static class ObjectPics
{
    /// <summary>Адреса превью для всего списка: ключ — id объекта, значение — адрес
    /// картинки. Объектов без картинки в словаре нет.</summary>
    public static Dictionary<string, string> Build(IEnumerable<ObjectItem> items, string projectId)
    {
        var all = items.ToList();
        var byParent = new Dictionary<string, List<ObjectItem>>(StringComparer.Ordinal);
        foreach (var item in all.Where(o => o.ParentId is { Length: > 0 }))
        {
            if (!byParent.TryGetValue(item.ParentId!, out var list))
            {
                byParent[item.ParentId!] = list = [];
            }
            list.Add(item);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        // «в работе» — защита от цикла в данных: хранилище его не пропускает, но список
        // рисуется и по данным, приехавшим репликацией со старой версии (та же оговорка,
        // что у ObjectRows)
        var walking = new HashSet<string>(StringComparer.Ordinal);
        // «посчитано» — вместе с result это память и о том, что картинки НЕТ: без неё
        // поддерево без картинок пересчитывалось бы заново для каждого своего предка
        var done = new HashSet<string>(StringComparer.Ordinal);

        string Pic(ObjectItem item)
        {
            if (done.Contains(item.Id))
            {
                return result.TryGetValue(item.Id, out var known) ? known : "";
            }
            if (!walking.Add(item.Id))
            {
                return "";
            }
            var url = Own(item, projectId);
            if (url.Length == 0 && byParent.TryGetValue(item.Id, out var children))
            {
                foreach (var child in children.OrderBy(c => DisplayIds.Order(c.DisplayId)))
                {
                    url = Pic(child);
                    if (url.Length > 0)
                    {
                        break;
                    }
                }
            }
            walking.Remove(item.Id);
            done.Add(item.Id);
            if (url.Length > 0)
            {
                result[item.Id] = url;
            }
            return url;
        }

        foreach (var item in all)
        {
            Pic(item);
        }
        return result;
    }

    /// <summary>Адрес СОБСТВЕННОЙ картинки объекта; пусто — своего изображения нет.</summary>
    public static string Own(ObjectItem item, string projectId)
    {
        var path = item.PathOrUrl.Trim();
        if (path.Length == 0 || !ProjectFiles.IsImage(path))
        {
            return "";
        }
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        // ОТКУДА БЕРЁТСЯ ФАЙЛ, ГОВОРИТ САМ ПУТЬ (T-98-S0): кадр датасета лежит в каталоге
        // данных организации (он реплицируется, и на соседнем сервере кадр тоже есть), а
        // остальные файлы объектов — в папке проекта, и там на соседнем сервере тот же путь
        // покажет его собственную копию (T-142)
        var project = item.ProjectId is { Length: > 0 } own ? own : projectId;
        return ObjectFiles.Link(path, project) is { } link ? FileLinks.Relative(link) : "";
    }
}
