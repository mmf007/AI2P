using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;

namespace AI2P.Storage.Services;

/// <summary>
/// АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ (T-46-S0, выпуск 1.105) — «отобрать по правилам текущего архива
/// и перенести».
///
/// Своего отбора и своего переноса у неё нет: отбор делает <see cref="ArchiveCandidateService"/>
/// (T-41-S0), перенос — <see cref="ArchiveTransferService"/> (T-42-S0). Здесь живёт ровно то,
/// чего нет ни у того, ни у другого: КОГДА этому происходить, ГДЕ это разрешено и КАК
/// рассказать о сделанном.
///
/// ТОЛЬКО НА ДИРИЖЁРЕ. Реестр архивов — общее имущество организации, и текущий архив у неё
/// один; запустись автоархивация на каждом сервере, одни и те же корневые задачи поехали бы
/// в архив с нескольких машин сразу, а хозяин строки всё равно один. Поэтому проверка стоит
/// в самом сервисе, а не только в расписании: расписание без сервера и так ведёт дирижёр
/// (ТЗ гл. 6), но вызвать архивацию можно и из формы добавления архива, и с чужой машины
/// отказ должен быть внятным, а не молчаливым «ничего не произошло».
///
/// ОТКАЗ — НЕ ИСКЛЮЧЕНИЕ. У сервиса два читателя (расписание и форма), и обоим нужен ОТВЕТ,
/// а не падение: расписание пишет его в журнал, форма показывает человеку. Поэтому «не
/// дирижёр», «текущего архива нет» и «архив закрыт» возвращаются сводкой с
/// <see cref="AutoArchiveResultDto.Ran"/> = false и внятной причиной.
/// </summary>
public sealed class AutoArchiveService
{
    private readonly ArchiveService _archives;
    private readonly ArchiveRuleService _rules;
    private readonly ArchiveCandidateService _candidates;
    private readonly ArchiveTransferService _transfer;
    private readonly EventStore _events;
    private readonly ServerScope _scope;

    public AutoArchiveService(ArchiveService archives, ArchiveRuleService rules,
        ArchiveCandidateService candidates, ArchiveTransferService transfer, EventStore events,
        ServerScope? scope = null)
    {
        _archives = archives;
        _rules = rules;
        _candidates = candidates;
        _transfer = transfer;
        _events = events;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>
    /// Прогон автоматической архивации по правилам ТЕКУЩЕГО архива.
    /// </summary>
    /// <param name="actorId">Кто запустил; null — сработало расписание.</param>
    /// <param name="now">Момент, от которого считается возраст записей; null — сейчас.
    /// Задаётся снаружи по той же причине, что и в отборе кандидатов: иначе календарный
    /// срок проверялся бы только ожиданием смены месяца.</param>
    public AutoArchiveResultDto Run(string? actorId, DateTime? now = null)
    {
        if (!_scope.IsConductor)
        {
            return Refused(Loc.T("msg.autoarc.1"));
        }
        var archive = _archives.Current();
        if (archive is null)
        {
            return Refused(Loc.T("msg.arcmove.1"));
        }
        if (_archives.StateOf(archive.Code) != ArchiveStates.Open)
        {
            return Refused(Loc.T("msg.arcmove.2", archive.Code));
        }

        // правила ТЕКУЩЕГО архива; выключенные отбрасывает сам отбор кандидатов
        var rules = _rules.List(archive.Id).Where(r => r.IsActive).ToList();
        var candidates = _candidates.Select(rules, now);
        var result = new AutoArchiveResultDto
        {
            Ran = true,
            ArchiveId = archive.Id,
            ArchiveCode = archive.Code,
            Rules = rules.Count,
            Candidates = candidates.Count,
        };

        foreach (var candidate in candidates)
        {
            var target = MoveTargetOf(candidate.Target);
            var item = new AutoArchiveItemDto
            {
                Target = candidate.Target,
                Id = candidate.Id,
                DisplayId = candidate.DisplayId,
                Title = candidate.Title,
            };
            if (target.Length == 0)
            {
                // ПРАВИЛА БЕЗОПАСНОСТИ И ЛОГИ поодиночке движком не переносятся (T-42-S0):
                // у них нет ни иерархии, ни файлов, и уносить их построчно смысла нет.
                // Молчать об этом нельзя — иначе правило выглядит невыполнимым навсегда
                item.Error = Loc.T("msg.autoarc.2", candidate.Target);
                result.Skipped++;
                result.Items.Add(item);
                continue;
            }
            try
            {
                var moved = _transfer.Move(target, candidate.Id, actorId);
                item.Moved = true;
                result.Moved++;
                result.Rows += moved.Rows;
                result.Files += moved.FilesMoved + moved.FilesCopied;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or IOException)
            {
                // запись занята работой, уехала вместе с чужой иерархией, файл не отдался —
                // остальные кандидаты всё равно едут, а причина попадает в сводку поимённо
                item.Error = ex.Message;
                result.Failed++;
            }
            result.Items.Add(item);
        }

        // СВОДКА В ЖУРНАЛ. Их два, и пишутся оба: журнал организации — событием ниже (его
        // видно на экране рядом с остальными событиями архивации), журнал приложения —
        // у вызывающего (ScheduleRunner, слой коннекторов: Serilog живёт там, а не здесь)
        result.Summary = Loc.T("msg.autoarc.3", archive.Code, result.Candidates, result.Moved,
            result.Skipped, result.Failed);
        _events.Append(new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ArchiveAutoRun,
            EntityType = "archive",
            EntityId = archive.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                archive.Code,
                result.Rules,
                result.Candidates,
                result.Moved,
                result.Skipped,
                result.Failed,
                result.Rows,
                result.Files,
            }),
        });
        return result;
    }

    /// <summary>Отказ со внятной причиной: сводка, по которой видно, что архивация не шла.
    /// Именно сводкой, а не исключением: расписанию её надо записать в журнал, а форме —
    /// показать человеку, и падение не годится ни тому, ни другому.</summary>
    private static AutoArchiveResultDto Refused(string reason) =>
        new() { Ran = false, Reason = reason, Summary = reason };

    /// <summary>
    /// Вид ПЕРЕНОСА (<see cref="ArchiveMoveTargets"/>) по виду ПРАВИЛА
    /// (<see cref="ArchiveRuleTargets"/>). Имена намеренно не совпадают: правило говорит
    /// «какие данные отбирать», перенос — «что уносить одной операцией», и у правил есть
    /// два вида, которые поодиночке не переносятся вовсе.
    /// </summary>
    public static string MoveTargetOf(string ruleTarget) => ruleTarget switch
    {
        ArchiveRuleTargets.Tasks => ArchiveMoveTargets.Task,
        ArchiveRuleTargets.Templates => ArchiveMoveTargets.Template,
        ArchiveRuleTargets.Objects => ArchiveMoveTargets.Object,
        ArchiveRuleTargets.Experience => ArchiveMoveTargets.Experience,
        _ => "",
    };
}
