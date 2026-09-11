using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Server.Org;
using AI2P.Storage.Services;

namespace AI2P.Server.Api;

/// <summary>
/// ПЕРВЫЙ ПРОЕКТ, КОМАНДА И ИИ-ИСПОЛНИТЕЛИ — продолжение визарда первого старта (T-291).
///
/// Жалоба, с которой началась задача: «чтобы добраться до выполнения первой задачи, всё
/// очень запутано». И правда: заведя организацию, человек оказывался перед пустым
/// интерфейсом, где до первой работающей задачи надо самому пройти справочник моделей,
/// исполнителей, команду и проект — четыре формы, о существовании которых он ещё не знает.
///
/// Поэтому визард спрашивает ОДИН вопрос — чем вы будете заниматься, — и по ответу
/// собирает рабочее место: трёх ИИ-исполнителей на самых подходящих моделях (Jon, Bob,
/// Stiv), команду <c>&lt;организация&gt;_team</c>, где тимлид — сам человек, и первый
/// проект с его папкой на диске.
///
/// Делается это только на ПЕРВИЧНОМ сервере (новый дирижёр кластера): сервер, который
/// подключается к чужому, получает и проекты, и команды, и справочники репликацией —
/// заводить ему своё значило бы плодить дубли.
/// </summary>
public static class FirstSetup
{
    /// <summary>Написание кода программ или аналитика.</summary>
    public const string UsageCode = "code";

    /// <summary>Создание видео.</summary>
    public const string UsageVideo = "video";

    /// <summary>Создание картинок.</summary>
    public const string UsageImage = "image";

    /// <summary>Типового использования нет — визард заканчивается как раньше.</summary>
    public const string UsageNone = "none";

    /// <summary>Все варианты в порядке показа.</summary>
    public static readonly string[] Usages = [UsageCode, UsageVideo, UsageImage, UsageNone];

    /// <summary>
    /// ИМЕНА ПЕРВЫХ ИИ-ИСПОЛНИТЕЛЕЙ (по заданию T-291). Имя человеку нужнее названия модели:
    /// в задачах, в чате и в журнале работ видно «Jon», а не «Claude-Opus-5.0_cli», и это
    /// то, о чём человек думает — кто у него в команде.
    /// </summary>
    public static readonly string[] Names = ["Jon", "Bob", "Stiv"];

    /// <summary>Суффикс названия команды: <c>&lt;имя организации&gt;_team</c>.</summary>
    public const string TeamSuffix = "_team";

    /// <summary>
    /// НАВЫКИ, ПО КОТОРЫМ ПОДБИРАЮТСЯ МОДЕЛИ. Коды — из справочника навыков (ТЗ гл. 7):
    /// «кодирование и размышление» — это семейства code-* и analyze-* плюс работа с текстом,
    /// без которой ни отчёта, ни постановки задачи не написать; видео и картинки — свои
    /// семейства целиком. Уточняющих сегментов (code-write-cs) здесь нет намеренно: они
    /// сужают выбор до одного языка, а первым исполнителям нужна широта.
    /// </summary>
    public static IReadOnlyList<string> SkillsOf(string usage) => usage switch
    {
        UsageCode =>
        [
            "code-write", "code-review", "code-debug", "code-refactor", "code-test",
            "analyze-requirements", "analyze-plan", "analyze-data",
            "text-write", "text-docs",
        ],
        UsageVideo =>
        [
            "video-generate", "video-animate", "video-keyframes", "video-edit",
            "video-extend", "video-restyle", "video-lipsync",
        ],
        UsageImage =>
        [
            "image-generate", "image-edit", "image-photo", "image-concept",
            "image-inpaint", "image-text",
        ],
        _ => [],
    };

    /// <summary>Вариант из формы; незнакомое значение — «типового использования нет».</summary>
    public static string NormalizeUsage(string? value)
    {
        var usage = (value ?? "").Trim().ToLowerInvariant();
        return Usages.Contains(usage) ? usage : UsageNone;
    }

    /// <summary>
    /// Сервисы организации, нужные сборке рабочего места. Передаются явно, а не через
    /// <see cref="OrgContext"/>: контекст тянет за собой каталог организации, коннекторы
    /// и оркестратор, а здесь нужны ровно четыре сервиса — и такой набор проверяется
    /// обычной тестовой фикстурой хранилища.
    /// </summary>
    public sealed record Services(
        ExecutorService Executors,
        TeamService Teams,
        ProjectService Projects,
        AiModelService Models)
    {
        /// <summary>Набор из готового контекста организации (то, чем пользуется визард).</summary>
        public static Services Of(OrgContext context) =>
            new(context.Executors, context.Teams, context.Projects, context.Models);
    }

    /// <summary>Что получилось: сколько исполнителей заведено, как названы команда и проект.</summary>
    /// <param name="Executors">Ники заведённых ИИ-исполнителей (может быть меньше трёх —
    /// см. <see cref="PickModels"/>).</param>
    /// <param name="TeamName">Название созданной команды.</param>
    /// <param name="ProjectName">Название созданного проекта.</param>
    /// <param name="ProjectId">Идентификатор проекта — по нему визард уводит человека
    /// сразу в его карточку.</param>
    public sealed record Result(
        IReadOnlyList<string> Executors,
        string TeamName,
        string ProjectName,
        string ProjectId);

    /// <summary>
    /// МОДЕЛИ ПОД ТИПОВОЕ ИСПОЛЬЗОВАНИЕ — не больше <paramref name="count"/> штук и БЕЗ
    /// ПОВТОРОВ (задание T-291): три исполнителя на одной модели отличались бы только ником.
    ///
    /// Берутся только АКТИВНЫЕ записи справочника: неактивную подставить в нового исполнителя
    /// нельзя вовсе (ExecutorService.Validate), и это правило верное — облачная модель без
    /// ключа API и локальная без установленных файлов работать не могут. На чистой установке
    /// активны записи, которым ключ не нужен (подписка, суффикс _cli), — из них и набирается
    /// первая команда; остальное человек включит, введя ключ.
    ///
    /// Оценка модели — СУММА баллов её декларации по нужным навыкам: так вперёд выходит
    /// та, что и умеет лучше, и умеет БОЛЬШЕ (одинаково сильная в трёх навыках модель
    /// полезнее сильной в одном). При равенстве — по названию, чтобы порядок был
    /// воспроизводимым, а не случайным.
    /// </summary>
    public static List<AiModel> PickModels(AiModelService models, string usage, int count)
    {
        var skills = SkillsOf(usage);
        if (skills.Count == 0 || count <= 0)
        {
            return [];
        }
        return models.ListWithSkills()
            .Where(model => model.IsActive)
            .Select(model => new
            {
                Model = model,
                Score = model.Skills
                    .Where(skill => skills.Contains(skill.Name, StringComparer.OrdinalIgnoreCase))
                    .Sum(skill => skill.Score),
            })
            .Where(row => row.Score > 0)
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.Model.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(count)
            .Select(row => row.Model)
            .ToList();
    }

    /// <summary>
    /// Собрать рабочее место: ИИ-исполнители → команда → проект. Порядок именно такой:
    /// команду нельзя собрать из ещё не заведённых исполнителей, а проекту нужна готовая
    /// команда — она становится его командой по умолчанию, и первая же задача проекта
    /// получает исполнителя автоподбором.
    ///
    /// Владелец в команде — ТИМЛИД (верхний уровень иерархии), ИИ-исполнители — его
    /// подчинённые: вышестоящий вправе поручать работу подчинённым без отдельного
    /// разрешения (ТЗ v1.17), а это ровно то, чего человек ждёт от своей команды.
    /// </summary>
    /// <param name="services">Сервисы организации, только что созданной визардом.</param>
    /// <param name="orgName">Её название — из него складывается название команды.</param>
    /// <param name="owner">Исполнитель владельца (его заводит <c>CreateOrg</c>).</param>
    /// <param name="usage">Типовое использование (<see cref="Usages"/>).</param>
    /// <param name="projectName">Название первого проекта.</param>
    /// <param name="projectDir">Папка проекта на диске; пусто — без папки.</param>
    public static Result Apply(Services services, string orgName, Executor owner, string usage,
        string projectName, string projectDir)
    {
        var actorId = owner.Id;
        var models = PickModels(services.Models, usage, Names.Length);
        var agents = new List<Executor>();
        foreach (var (model, index) in models.Select((model, index) => (model, index)))
        {
            agents.Add(services.Executors.CreateMember(new Executor
            {
                Nick = FreeNick(services.Executors, Names[index]),
                Kind = ExecutorKind.Ai,
                // роль «редактор»: ИИ-исполнитель работает над задачами, но не правит
                // настройки организации — их хозяин человек (ТЗ п. 2.2)
                SystemRole = SystemRole.Editor,
                ModelId = model.Id,
                IsActive = true,
            }, actorId));
        }

        var team = services.Teams.Create(new Team
        {
            Name = TeamName(services.Teams, orgName),
            AgentLanguage = Loc.Lang,
            IsActive = true,
            Members =
            [
                new TeamMember { ExecutorId = owner.Id, IsLead = true },
                .. agents.Select(agent => new TeamMember
                {
                    ExecutorId = agent.Id,
                    ParentExecutorId = owner.Id,
                }),
            ],
        }, actorId);

        var project = services.Projects.Create(projectName, projectDir, team.Id, actorId);
        return new Result(agents.Select(a => a.Nick).ToList(), team.Name, project.Name, project.Id);
    }

    /// <summary>
    /// Название команды: <c>&lt;имя организации&gt;_team</c>. Название команды уникально
    /// в организации, поэтому занятое дополняется номером — визард не имеет права упасть
    /// на последнем шаге из-за того, что такая команда уже есть (человек мог вернуться
    /// сюда после ошибки в имени проекта).
    /// </summary>
    public static string TeamName(TeamService teams, string orgName)
    {
        var baseName = (orgName ?? "").Trim() + TeamSuffix;
        var taken = teams.List()
            .Select(team => team.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(baseName))
        {
            return baseName;
        }
        for (var number = 2; ; number++)
        {
            var candidate = $"{baseName}-{number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Свободный ник исполнителя: ники в организации уникальны, а имя человека
    /// могло совпасть с Jon (или визард уже отработал однажды).</summary>
    private static string FreeNick(ExecutorService executors, string name)
    {
        var taken = executors.List()
            .Select(executor => executor.Nick)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }
        for (var number = 2; ; number++)
        {
            var candidate = $"{name}-{number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
