using AI2P.Core;
using AI2P.Core.Entities;

namespace AI2P.Storage.Services;

/// <summary>
/// НАБОРЫ ОПЫТА — «БИБЛИОТЕКА СТИЛЕЙ РАБОТЫ» (T-270-S0, ветка T-318): установка, снятие и
/// выгрузка пачки записей опыта одним действием.
///
/// <para>Механизм вынут из плагинов: <c>PluginSetupService.Initialize</c> уже умел класть
/// записи манифеста в опыт с пометкой владельца и снимать их по ней же — здесь то же самое
/// отдано человеку отдельно, файлом <see cref="ExperiencePack"/> и своей закладкой настроек.</para>
///
/// <para>ЧТО ЗДЕСЬ ГЛАВНОЕ:</para>
/// <list type="bullet">
/// <item>ОБЛАСТЬ ВЫБИРАЕТ ЧЕЛОВЕК В МОМЕНТ УСТАНОВКИ (общий опыт / проект / узел шаблона),
/// а не файл: один и тот же стиль одному нужен на всю организацию, другому — в одном
/// проекте. Область без значения не бывает — <c>CreateWithId</c> отвергает пустую;</item>
/// <item>ВЛАДЕЛЕЦ — ТЭГ <c>pack:&lt;код&gt;</c>: по нему снятие находит свои записи в любой
/// области, а отбор опыта в задание такой тэг темой работы не считает
/// (<see cref="ExperienceService.IsOwnerTag"/>);</item>
/// <item>ПОВТОРНАЯ УСТАНОВКА ИДЕМПОТЕНТНА и не затирает правки человека: живая запись с тем
/// же идентификатором пропускается целиком, а совпадение по ТЕКСТУ (запись завели руками до
/// установки набора) не даёт завести второй экземпляр — ровно как у плагинов;</item>
/// <item>ВЫГРУЗКА — обратная кнопка: отобранные записи ложатся файлом набора, и он ставится
/// на другой установке. Обмена опытом между установками до этого не было вовсе.</item>
/// </list>
///
/// <para>ЧЕГО ЗДЕСЬ НЕТ: блок <c>templates</c> файла разбирается и отдаётся наружу
/// (<see cref="ExperiencePack.Templates"/>), но УЗЛЫ ШАБЛОНА при установке не заводятся —
/// это работа соседней подзадачи ветки T-271-S0 «Опыт: действия агента для анализа и шаблон
/// „Анализ опыта“», с которой согласован состав полей.</para>
/// </summary>
public sealed class ExperiencePackService
{
    private readonly FileStore _files;
    private readonly ExperienceService _experience;
    private readonly RefDataService _refData;
    private readonly TaskService _tasks;

    public ExperiencePackService(FileStore files, ExperienceService experience,
        RefDataService refData, TaskService tasks)
    {
        _files = files;
        _experience = experience;
        _refData = refData;
        _tasks = tasks;
    }

    /// <summary>Наборы, найденные в каталоге данных (<c>packs/*/pack.json</c>).</summary>
    public List<ExperiencePack> List() => ExperiencePack.ReadAll(_files.DataDir);

    /// <summary>Набор по коду; null — файла нет либо он негоден.</summary>
    public ExperiencePack? Get(string code) => ExperiencePack.Read(_files.DataDir, code);

    /// <summary>Записи, заведённые этим набором, — в любой области организации.</summary>
    public List<ExperienceRecord> InstalledRecords(string code) =>
        _experience.ListByTag(PackCodes.ExperienceOwner(code.Trim()));

    /// <summary>ГДЕ СТОИТ НАБОР: область и её адресат по первой же его записи. Записи набора
    /// ставятся разом в одну область, поэтому первой довольно; пусто — набор не установлен.</summary>
    public (string Scope, string ProjectId, string TemplateTaskId) Placement(string code)
    {
        var first = InstalledRecords(code).FirstOrDefault();
        return first is null
            ? ("", "", "")
            : (ExperienceService.ScopeOf(first), first.ProjectId ?? "", first.TemplateTaskId);
    }

    /// <summary>
    /// УСТАНОВИТЬ НАБОР В ВЫБРАННУЮ ОБЛАСТЬ. Возвращает, сколько записей заведено: ноль
    /// означает «всё уже стоит», а не отказ.
    /// </summary>
    /// <param name="pack">Набор (файл каталога данных).</param>
    /// <param name="scope">Область: project / template / general — выбор человека.</param>
    /// <param name="projectId">Проект-получатель (scope=project).</param>
    /// <param name="templateTaskId">Узел шаблона-получатель (scope=template).</param>
    /// <param name="actorId">Кто ставит.</param>
    /// <param name="lang">Язык текстов набора; null — язык установки.</param>
    /// <returns>Сколько записей опыта заведено и сколько узлов шаблона (T-271-S0).</returns>
    public (int Records, int Templates) Install(ExperiencePack pack, string scope, string? projectId,
        string? templateTaskId, string? actorId = null, string? lang = null)
    {
        var code = Code(pack.Code);
        var owner = PackCodes.ExperienceOwner(code);
        // сверка по ТЕКСТУ идёт по той же области, куда ставим: запись, заведённая человеком
        // руками, второй раз появляться не должна (та же защита, что у плагинов)
        var known = ScopeRecords(scope, projectId, templateTaskId)
            .Select(r => r.Text.Trim())
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var added = 0;
        foreach (var record in pack.Records)
        {
            var text = record.Text.Text(lang).Trim();
            if (text.Length == 0 || _experience.HasLiveRecord(record.Id) || !known.Add(text))
            {
                continue;
            }
            _experience.CreateWithId(record.Id, scope, projectId, templateTaskId, text, actorId,
                SkillId(record.Skill), record.AlwaysLoad,
                // пометка владельца ДОПИСЫВАЕТСЯ к тэгам записи: по ней снятие её и найдёт
                [.. record.Tags, owner]);
            added++;
        }
        return (added, InstallTemplates(pack, scope, projectId, templateTaskId, actorId, lang));
    }

    /// <summary>
    /// ЗАВЕСТИ УЗЛЫ ШАБЛОНА, КОТОРЫЕ ПРИНЁС НАБОР (T-271-S0). Шаблон «Анализ опыта»
    /// поставляется ВМЕСТЕ со стилями — прямое требование заказчика: стиль работы это не
    /// только записи опыта, но и готовый порядок работы, который человек ставит в расписание.
    ///
    /// <para>ИДЕНТИФИКАТОРЫ ПОСТОЯННЫЕ, как у записей: узел, уже заведённый на этом сервере
    /// (или приехавший репликацией), пропускается — повторная установка набора второй копии
    /// дерева не делает.</para>
    ///
    /// <para>ПРОЕКТ узлов шаблона выбирается НЕ ЧЕЛОВЕКОМ ОТДЕЛЬНО, а из области установки:
    /// scope=project — этот проект, scope=template — проект узла-получателя. У области
    /// «общие правила организации» проекта нет вовсе, и узлы шаблона тогда НЕ ЗАВОДЯТСЯ:
    /// шаблон задач без проекта в AI2P не живёт. Это не отказ установки — записи опыта
    /// ставятся как обычно, а в ответе видно, что узлов заведено ноль.</para>
    ///
    /// <para>Порядок обхода — как в файле: родитель обязан идти ПЕРЕД потомком, иначе ссылка
    /// на родителя не найдётся (то же правило, что у копии шаблона в дерево задач).</para>
    /// </summary>
    private int InstallTemplates(ExperiencePack pack, string scope, string? projectId,
        string? templateTaskId, string? actorId, string? lang)
    {
        if (pack.Templates.Count == 0)
        {
            return 0;
        }
        var project = (scope ?? "").Trim().ToLowerInvariant() switch
        {
            ExperienceScopes.Project => projectId,
            ExperienceScopes.Template when templateTaskId is { Length: > 0 } =>
                _tasks.Get(templateTaskId)?.ProjectId,
            _ => null,
        };
        if (project is not { Length: > 0 })
        {
            return 0;
        }
        var added = 0;
        foreach (var node in pack.Templates)
        {
            if (_tasks.Get(node.Id) is not null)
            {
                continue;   // узел уже стоит — повторная установка ничего не дублирует
            }
            var title = node.Title.Text(lang).Trim();
            if (title.Length == 0)
            {
                continue;   // без заголовка узел не заводится (то же правило, что у задач)
            }
            _tasks.Create(new TaskItem
            {
                Id = node.Id,
                ProjectId = project,
                ParentId = node.Parent is { Length: > 0 } ? node.Parent : null,
                Kind = TaskKind.Task,
                Title = title,
                Status = TaskStatuses.Draft,
                IsTemplate = true,
                // ПОТОМОК ПОЛУЧАЕТ ТЕКСТ РОДИТЕЛЯ В ЗАДАНИИ: общий порядок работы набор
                // пишет в узле-родителе ОДИН РАЗ, а потомки добавляют к нему своё отличие
                // (у «Анализа опыта» это область разбора). Без этой пометки исполнителю
                // потомка достался бы огрызок задания без правил и запретов
                ParentInPrompt = node.Parent is { Length: > 0 },
                SkillIds = [.. node.Skills.Select(SkillId).Where(id => id is not null).Select(id => id!)],
            }, node.Description.Text(lang), node.Acceptance.Text(lang), actorId, notify: false);
            added++;
        }
        return added;
    }

    /// <summary>
    /// СНЯТЬ НАБОР: убрать записи, помеченные владельцем <c>pack:&lt;код&gt;</c>, — и только их.
    /// Записи, приехавшие с ДРУГОГО сервера, здесь не удаляются (правит только владелец,
    /// ТЗ гл. 6) — они пропускаются молча, а снять их надо там, где ставили.
    /// </summary>
    /// <returns>Сколько записей снято.</returns>
    public int Remove(string code, string? actorId = null)
    {
        var removed = 0;
        foreach (var record in InstalledRecords(Code(code)))
        {
            if (record.IsReadOnly)
            {
                continue;
            }
            _experience.Delete(record.Id, actorId);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// ВЫГРУЗИТЬ ОТОБРАННЫЕ ЗАПИСИ В ФАЙЛ НАБОРА (<c>packs/&lt;код&gt;/pack.json</c>) — способ
    /// перенести наработанный стиль в другую организацию или установку.
    ///
    /// <para>ИДЕНТИФИКАТОРЫ ЗАПИСЕЙ СОХРАНЯЮТСЯ: файл, поставленный обратно, даёт те же
    /// строки, а не их копии. Текст кладётся на ОДНОМ языке — том, на котором работает этот
    /// сервер: перевести накопленный опыт нам нечем, и выдумывать переводы нельзя
    /// (поставляемые наборы пишутся на всех пяти языках руками).</para>
    /// </summary>
    /// <param name="code">Код нового набора (он же имя каталога).</param>
    /// <param name="name">Название набора.</param>
    /// <param name="description">Описание набора.</param>
    /// <param name="recordIds">Идентификаторы записей, отобранных человеком.</param>
    /// <param name="lang">Язык, которым подписывается текст; null — язык установки.</param>
    /// <returns>Путь файла относительно каталога данных.</returns>
    public string Export(string code, string name, string description,
        IEnumerable<string> recordIds, string? lang = null)
    {
        code = Code(code);
        var language = lang is { Length: > 0 } ? lang : Loc.Lang;
        var pack = new ExperiencePack { Code = code, Version = 1 };
        pack.Name.Set(language, name.Trim().Length > 0 ? name.Trim() : code);
        if (description.Trim().Length > 0)
        {
            pack.Description.Set(language, description.Trim());
        }
        pack.Doc[language] = ExperiencePack.DocPageOf(code);
        foreach (var id in recordIds.Distinct(StringComparer.Ordinal))
        {
            var record = _experience.Get(id);
            if (record is null)
            {
                continue;
            }
            var item = new ExperiencePackRecord
            {
                Id = record.Id,
                Skill = record.SkillName,
                // служебные пометки владельца в выгрузку не идут: у нового набора свой код,
                // и чужая пометка увела бы его записи под снятие соседнего набора
                Tags = [.. record.Tags.Where(t => !ExperienceService.IsOwnerTag(t))],
                AlwaysLoad = record.AlwaysLoad,
            };
            item.Text.Set(language, record.Text);
            pack.Records.Add(item);
        }
        if (pack.Records.Count == 0)
        {
            throw new ArgumentException(Loc.T("msg.pack.2"));
        }
        var rel = ExperiencePack.PathOf(code);
        _files.WriteText(rel, pack.ToJson());
        return rel;
    }

    /// <summary>Записи выбранной области — ими идёт сверка по тексту при установке.</summary>
    private List<ExperienceRecord> ScopeRecords(string scope, string? projectId,
        string? templateTaskId) =>
        (scope ?? "").Trim().ToLowerInvariant() switch
        {
            ExperienceScopes.Project when projectId is { Length: > 0 } =>
                _experience.ListByProject(projectId),
            ExperienceScopes.Template when templateTaskId is { Length: > 0 } =>
                _experience.ListByTemplate(templateTaskId),
            ExperienceScopes.General => _experience.ListGeneral(),
            _ => [],
        };

    /// <summary>Идентификатор навыка по коду; пусто — запись набора общая для всех навыков.</summary>
    private string? SkillId(string skill)
    {
        var name = skill.Trim();
        if (name.Length == 0)
        {
            return null;
        }
        var found = _refData.Skills()
            .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return found?.Id ?? throw new ArgumentException(Loc.T("msg.pack.3", name));
    }

    private static string Code(string packCode) =>
        packCode.Trim().Length > 0
            ? packCode.Trim()
            : throw new ArgumentException(Loc.T("msg.pack.1"));
}
