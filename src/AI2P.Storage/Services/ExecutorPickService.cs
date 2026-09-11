using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;

namespace AI2P.Storage.Services;

/// <summary>Порядок предпочтения видов исполнителей при автоподборе (ТЗ v1.26, todo28).</summary>
public enum PickMode
{
    /// <summary>Сначала ИИ, потом человек (кнопка формы задачи; авторазбиение).</summary>
    AiFirst,
    /// <summary>Сначала человек, потом ИИ (кнопка формы задачи).</summary>
    HumanFirst,
    /// <summary>Только ИИ (выбор агента-разбивателя).</summary>
    AiOnly,
}

/// <summary>
/// Автоподбор исполнителя под skills задачи (ТЗ v1.26, todo28). Кандидаты — активные
/// участники команды задачи (активность двойная, T-129: и у исполнителя в справочнике,
/// и у его участия в этой команде); владение skills и стоимость — из деклараций возможностей
/// (п. 7.3): quality — среднее по требуемым skills (0–100), цена — in_per_1m + out_per_1m.
/// Итоговый балл: bias × качество + (1 − bias) × дешевизна, где bias — поле «цена ↔ качество»
/// проекта (0.0 — приоритет цене, 1.0 — качеству). Из подходящих кандидатов предпочитаемого
/// вида (ИИ или человек) берётся лучший; нет подходящих — второй вид (кроме AiOnly).
///
/// ВИДОВ ЗДЕСЬ ДВА, А НЕ ТРИ (T-153-S0): исполнитель «авто ПО» (<see cref="ExecutorKind.Software"/>)
/// в кандидаты не попадает НИКОГДА — он отбраковывается по типу до счёта навыков и цены.
/// </summary>
public sealed class ExecutorPickService
{
    private readonly TeamService _teams;
    private readonly ExecutorService _executors;
    private readonly ProjectService _projects;
    private readonly RefDataService _refData;
    private readonly FileStore _files;

    /// <summary>Задания — чтобы узнать, ЗАНЯТ ЛИ исполнитель прямо сейчас (T-160-S0).
    /// Необязательная связь: без неё подбор работает как раньше, просто не отличает
    /// занятых от свободных.</summary>
    private readonly JobService? _jobs;

    public ExecutorPickService(TeamService teams, ExecutorService executors,
        ProjectService projects, RefDataService refData, FileStore files,
        JobService? jobs = null)
    {
        _teams = teams;
        _executors = executors;
        _projects = projects;
        _refData = refData;
        _files = files;
        _jobs = jobs;
    }

    /// <summary>Подбор исполнителя; startAt — момент запуска задачи (по умолчанию сейчас):
    /// исполнитель, занятый в этот момент (busy_until в будущем, ТЗ v1.37), не назначается —
    /// его можно взять только на запуск после освобождения.</summary>
    /// <param name="preferFree">
    /// СНАЧАЛА СВОБОДНЫЕ (T-160-S0): исполнитель, у которого прямо сейчас идёт задание,
    /// берётся только если подходящих свободных нет вовсе. Ставится, когда задача пойдёт
    /// в работу немедленно — то есть внутри ОТКРЫТОЙ ОЧЕРЕДИ ИЕРАРХИИ.
    /// <para>Зачем: подзадаче, созданной по ходу иерархического запуска, автоподбор выдал
    /// исполнителя, который в этот же момент вёл задачу «общий прогон тестов», а та ждала
    /// завершения всей иерархии. Ждать друг друга они могли бы вечно — при том, что
    /// свободные исполнители в команде были.</para>
    /// <para>Вне очереди этого правила нет намеренно: занятость — величина сиюминутная,
    /// а задача, которую запустят завтра, должна достаться лучшему по навыкам и цене.</para>
    /// </param>
    public PickedExecutorDto Pick(string? projectId, string? teamId,
        IReadOnlyCollection<string> skillIds, PickMode mode, DateTime? startAt = null,
        bool preferFree = false)
    {
        if (teamId is null)
        {
            return NotFound(Loc.T("msg.executorPick.1"));
        }
        var team = _teams.Get(teamId);
        if (team is null || team.Members.Count == 0)
        {
            return NotFound(Loc.T("msg.executorPick.2"));
        }

        var at = startAt ?? DateTime.UtcNow;
        var bias = ProjectService.QualityBias(projectId is null ? null : _projects.Get(projectId));
        var skillNames = SkillNames(skillIds);
        var candidates = new List<PickCandidateDto>();
        var kinds = new Dictionary<string, ExecutorKind>();
        var busySkipped = new List<string>();
        foreach (var member in team.Members)
        {
            if (!member.IsActive)
            {
                continue;   // выключен в составе именно этой команды (T-129)
            }
            var executor = _executors.Get(member.ExecutorId);
            if (executor is null || !executor.IsActive || executor.DeletedAt is not null)
            {
                continue;
            }
            if (executor.Kind == ExecutorKind.Software)
            {
                // «АВТО ПО» АВТОПОДБОРОМ НЕ НАЗНАЧАЕТСЯ ВОВСЕ (T-153-S0). Отбраковка стоит
                // ЗДЕСЬ — до счёта навыков и стоимости, — а не выражается порядком «сначала
                // ИИ, потом человек»: у программы декларация возможностей может быть какой
                // угодно, и обычная задача «сделай картинку» уехала бы на тренер LoRA.
                // Такого исполнителя назначает только человек, ИИ-агент или специальный
                // алгоритм (кнопка «Обучить» → задача обучения LoRA)
                continue;
            }
            if (executor.BusyUntil is { } busy && busy > at)
            {
                busySkipped.Add(Loc.T("msg.executorPick.3", executor.Nick, busy.ToLocalTime()));
                continue;
            }
            var (quality, cost) = ReadScope(executor, skillNames, executor.Kind);
            kinds[executor.Id] = executor.Kind;
            candidates.Add(new PickCandidateDto
            {
                ExecutorId = executor.Id,
                Nick = executor.Nick,
                Kind = executor.Kind,
                Quality = quality,
                CostPer1M = cost,
            });
        }
        if (candidates.Count == 0)
        {
            return NotFound(busySkipped.Count > 0
                ? Loc.T("msg.executorPick.4") + string.Join(", ", busySkipped)
                : Loc.T("msg.executorPick.5"));
        }

        // дешевизна нормируется по кандидатам: самый дешёвый — 1, самый дорогой — 0
        var maxCost = candidates.Max(c => c.CostPer1M);
        foreach (var candidate in candidates)
        {
            var priceScore = maxCost <= 0 ? 1.0 : 1.0 - candidate.CostPer1M / maxCost;
            candidate.Score = bias * (candidate.Quality / 100.0) + (1.0 - bias) * priceScore;
        }
        var ordered = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Nick).ToList();

        // подходит кандидат, владеющий хоть чем-то из требуемого (quality > 0)
        var preferAi = mode is PickMode.AiFirst or PickMode.AiOnly;
        // СНАЧАЛА СВОБОДНЫЕ (T-160-S0): тот же отбор гоняется дважды — по свободным, и, если
        // среди них подходящего нет, по всем. Человек занятым не бывает: у него очередь Inbox
        var busyNow = new HashSet<string>(StringComparer.Ordinal);
        if (preferFree && _jobs is not null)
        {
            foreach (var candidate in ordered)
            {
                if (kinds[candidate.ExecutorId] != ExecutorKind.Human
                    && _jobs.HasActiveByExecutor(candidate.ExecutorId))
                {
                    busyNow.Add(candidate.ExecutorId);
                }
            }
        }
        var best = Best(ordered.Where(c => !busyNow.Contains(c.ExecutorId)).ToList(), kinds, preferAi, mode);
        if (best is null && busyNow.Count > 0)
        {
            best = Best(ordered, kinds, preferAi, mode);
        }
        if (best is null)
        {
            var wanted = skillNames.Count > 0 ? string.Join(", ", skillNames) : Loc.T("msg.executorPick.6");
            var busyNote = busySkipped.Count > 0 ? Loc.T("msg.executorPick.7") + string.Join(", ", busySkipped) : "";
            return new PickedExecutorDto
            {
                Reason = (mode == PickMode.AiOnly
                    ? Loc.T("msg.executorPick.8", wanted)
                    : Loc.T("msg.executorPick.9", wanted)) + busyNote,
                Candidates = ordered,
            };
        }
        return new PickedExecutorDto
        {
            ExecutorId = best.ExecutorId,
            Nick = best.Nick,
            Kind = best.Kind,
            Reason = Loc.T("msg.executorPick.10", best.Quality, best.CostPer1M, best.Score, bias),
            Candidates = ordered,
        };
    }

    private static PickedExecutorDto NotFound(string reason) => new() { Reason = reason };

    /// <summary>Лучший из списка по правилу вида (сначала предпочитаемый вид, потом второй —
    /// кроме AiOnly). Вынесено из <see cref="Pick"/>, потому что отбор гоняется дважды:
    /// по свободным исполнителям и по всем (T-160-S0).</summary>
    private static PickCandidateDto? Best(List<PickCandidateDto> ordered,
        Dictionary<string, ExecutorKind> kinds, bool preferAi, PickMode mode)
    {
        var best = ordered.FirstOrDefault(c =>
            c.Quality > 0 && (kinds[c.ExecutorId] == ExecutorKind.Ai) == preferAi);
        if (best is null && mode != PickMode.AiOnly)
        {
            best = ordered.FirstOrDefault(c =>
                c.Quality > 0 && (kinds[c.ExecutorId] == ExecutorKind.Ai) != preferAi);
        }
        return best;
    }

    /// <summary>
    /// Коды навыков, которыми исполнитель ВЛАДЕЕТ по своей декларации возможностей (п. 7.3).
    /// Нужны опыту проекта (todo48): агент читает только записи по своим навыкам. Оценка
    /// здесь не важна — важен сам факт, что навык в декларации назван.
    /// </summary>
    public IReadOnlyCollection<string> DeclaredSkills(Executor executor)
    {
        try
        {
            var json = executor.CapabilitiesPath.Length == 0 ? "" : _files.ReadText(executor.CapabilitiesPath);
            if (json.Trim().Length == 0)
            {
                return [];
            }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("skills", out var skills)
                || skills.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            return skills.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object
                            && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(e => e.GetProperty("name").GetString()!)
                .Where(name => name.Trim().Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];   // файла декларации нет — навыков не знаем, опыт по навыкам не отдаём
        }
    }

    /// <summary>Имена (коды) skills по id из справочника — в декларациях skills хранятся по имени.</summary>
    private List<string> SkillNames(IReadOnlyCollection<string> skillIds)
    {
        if (skillIds.Count == 0)
        {
            return [];
        }
        var byId = _refData.Skills().ToDictionary(s => s.Id, s => s.Name);
        return skillIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>
    /// Качество и цена из декларации возможностей исполнителя (п. 7.3). Качество — среднее
    /// владение требуемыми skills (нет skill в декларации — 0 в среднее); требуемые не заданы —
    /// среднее по всем skills декларации. Цена — cost.in_per_1m + cost.out_per_1m (нет — 0).
    ///
    /// У ИИ-исполнителя оценка медиа-навыка вдобавок сверяется с парой inputs/outputs
    /// декларации (T-257, <see cref="SkillIo"/>): режим ввода у медиа-моделей (t2i, i2v, v2v)
    /// закодирован действием навыка, и модель, чей вход не принимает нужного носителя, этот
    /// навык не исполнит — сколько бы ни было объявлено в skills. Такой навык получает 0,
    /// то есть задача только с ним не отдаётся модели вовсе (t2v-модель не предлагается на
    /// i2v-задачу). У человека декларация форматов — условность, поэтому правило только для ИИ.
    /// </summary>
    private (double Quality, double Cost) ReadScope(Executor executor, List<string> skillNames,
        ExecutorKind kind)
    {
        try
        {
            var json = executor.CapabilitiesPath.Length == 0 ? "" : _files.ReadText(executor.CapabilitiesPath);
            if (json.Trim().Length == 0)
            {
                return (0, 0);
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in skills.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        && entry.TryGetProperty("score", out var s) && s.TryGetDouble(out var score))
                    {
                        scores[n.GetString()!] = score;
                    }
                }
            }
            var inputs = kind == ExecutorKind.Ai ? Formats(root, "inputs") : [];
            var outputs = kind == ExecutorKind.Ai ? Formats(root, "outputs") : [];

            double quality;
            if (skillNames.Count > 0)
            {
                quality = skillNames.Average(name => SkillIo.Fits(name, inputs, outputs)
                    ? ScoreFor(scores, name)
                    : 0);
            }
            else
            {
                quality = scores.Count > 0 ? scores.Values.Average() : 0;
            }

            double cost = 0;
            if (root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Object)
            {
                if (costEl.TryGetProperty("in_per_1m", out var inV) && inV.TryGetDouble(out var inCost))
                {
                    cost += inCost;
                }
                if (costEl.TryGetProperty("out_per_1m", out var outV) && outV.TryGetDouble(out var outCost))
                {
                    cost += outCost;
                }
            }
            return (quality, cost);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }

    /// <summary>Коды форматов из списка декларации (inputs/outputs); нет списка — пусто.</summary>
    private static List<string> Formats(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return list.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(code => code.Trim().Length > 0)
            .ToList();
    }

    /// <summary>
    /// Оценка владения требуемым навыком (ТЗ v1.39, todo35_2). Точного кода в декларации
    /// нет — уточняющие сегменты (третья категория и глубже, например язык в code-write-cpp)
    /// снимаются по одному: исполнитель с code-write покрывает любой code-write-«язык».
    /// Обратное неверно: code-write-cpp не покрывает code-write.
    /// </summary>
    private static double ScoreFor(Dictionary<string, double> scores, string name)
    {
        if (scores.TryGetValue(name, out var score))
        {
            return score;
        }
        var parts = name.Split('-');
        for (var take = parts.Length - 1; take >= 2; take--)
        {
            if (scores.TryGetValue(string.Join('-', parts.Take(take)), out score))
            {
                return score;
            }
        }
        return 0;
    }
}
