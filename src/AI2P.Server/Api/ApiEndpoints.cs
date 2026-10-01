using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Org;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Server.Api;

/// <summary>Конфигурация с путём файла — для записи изменений из UI обратно в config.json.</summary>
/// <param name="Config">Настройки этого запуска.</param>
/// <param name="Path">Рабочий config.json.</param>
/// <param name="MachineRoot">От чего считаются ОТНОСИТЕЛЬНЫЕ каталоги железа — репозиторий
/// моделей, дистрибутивы, пакеты (T-291, <see cref="Ai2pConfig.StorageSettings.MachineRoot"/>).
/// Считается один раз при старте: каталог программы и рабочий каталог за время работы
/// не меняются.</param>
public sealed record ConfigHolder(Ai2pConfig Config, string Path, string MachineRoot);

/// <summary>
/// HTTP API — единственная точка входа для всех клиентов (ТЗ гл. 3, API-first):
/// Blazor UI ходит только сюда, будущие плагины VS Code / Unity — тем же API.
/// </summary>
public static class ApiEndpoints
{
    public static void MapAi2pApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // --- уровень СЕРВЕРА: одно на установку, организаций не знает (ТЗ п. 6.4.1) ---
        var services = app.Services;
        var registry = services.GetRequiredService<OrgRegistry>();
        var secrets = services.GetRequiredService<SecretStore>();
        var configHolder = services.GetRequiredService<ConfigHolder>();
        // ФАЙЛ, КОТОРОГО НА ЭТОМ СЕРВЕРЕ НЕТ (T-13-S0), берётся у того сервера кластера,
        // где его создал агент: папка проекта не реплицируется и своя на каждом сервере
        var remoteFiles = new RemoteFileService(registry);

        // Личность запроса (ТЗ гл. 12, этап 39). Прежде актор вычислялся ОДИН РАЗ при старте
        // приложения и был зашит в замыкание всех эндпойнтов; теперь он берётся из контекста
        // запроса — иначе на одном сервере не может работать больше одного человека.
        var current = services.GetRequiredService<CurrentUserAccessor>();
        string Actor() => current.Executor?.Id
            ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.1"));

        // АКТОР ВНЕ ОРГАНИЗАЦИИ (T-148): исполнитель принадлежит организации, а на сервере,
        // который сам ещё не подключён, организаций нет вовсе — и всё-таки человек за экраном
        // есть, и он спрашивает решение по своей заявке и правит список серверов. Для таких
        // действий актор не обязателен: они пишутся в серверный журнал, где исполнителей нет
        string? ServerActor() => current.Executor?.Id;
        string AccountId() => current.AccountId
            ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.2"));

        // ХОЗЯИН ЭТОГО КОМПЬЮТЕРА (T-186-S0): владелец системы либо локальный администратор
        // сервера. Ему показываются и организации, в которых он пока не участник, — те, что
        // приехали сюда репликацией: их база лежит на его же диске, и вступить в них он
        // вправе сам (Organization.CanJoin), иначе переключиться в них нечем вовсе.
        // Тот же признак решает, кому виден РАЗДЕЛ ОРГАНИЗАЦИЙ в настройках (T-201-S0):
        // он про эту установку, а не про открытую сейчас организацию.
        bool CanJoinForeignOrgs() => current.IsMachineOwner;

        // --- уровень ОРГАНИЗАЦИИ (ТЗ п. 2.15, этап 40) ---
        // Прежде тут лежали 30 синглтонов, извлечённых при старте приложения. Организация —
        // отдельная БД и отдельный каталог, поэтому набор сервисов теперь берётся из контекста
        // ЗАПРОСА: организацию задаёт сегмент URL, заголовок X-AI2P-Org или участие вошедшего.
        // Обращение оформлено функциями, а не переменными, ровно по этой причине.
        OrgContext Ctx() => current.Org
            ?? throw new InvalidOperationException(
                Loc.T("msg.apiEndpoints.3"));
        ProjectService Projects() => Ctx().Projects;
        TeamService Teams() => Ctx().Teams;
        ExecutorService Executors() => Ctx().Executors;
        RefDataService RefData() => Ctx().RefData;
        AiModelService Models() => Ctx().Models;
        TaskService Tasks() => Ctx().Tasks;
        ObjectService Objects() => Ctx().Objects;
        JobService Jobs() => Ctx().Jobs;
        ChatService Chat() => Ctx().Chat;
        RunRequestService RunRequests() => Ctx().RunRequests;
        BillingService Billing() => Ctx().Billing;
        EventStore Journal() => Ctx().Events;
        // ВЫБОР ПРОЕКТА, ЛЕЙАУТ ЗАКЛАДОК И ВИДЫ СПИСКОВ — это настройки ЧЕЛОВЕКА, а не данные
        // среды (T-45-S0): пишутся они всегда в рабочую среду, даже когда на экране открыт
        // архив. Иначе переключение на архив либо упиралось бы в запрет записи, либо
        // складывало настройки экрана в архив, где их никто больше не прочитает
        AppStateService AppState() => Home().AppState;
        JobOrchestrator Orchestrator() => Ctx().Orchestrator;
        TeamWorkService TeamWork() => Ctx().TeamWork;
        FileStore Files() => Ctx().Files;
        ExecutorPickService Picker() => Ctx().ExecutorPick;
        ModelInstallService ModelInstalls() => Ctx().ModelInstalls;
        ModelKeyService ModelKeys() => Ctx().ModelKeys;
        ActionCatalogService Actions() => Ctx().Actions;
        TaskStatusService Statuses() => Ctx().TaskStatuses;
        SecurityRuleService SecurityRules() => Ctx().SecurityRules;
        ExperienceService Experience() => Ctx().Experience;
        ScheduleService Schedules() => Ctx().Schedules;
        // правила уведомлений организации (T-272): закладка «Настройки → Уведомления»
        NotificationRuleService NotificationRules() => Ctx().NotificationRules;
        // РАБОЧАЯ СРЕДА запроса (T-45-S0): при просмотре архива Ctx() отдаёт сервисы АРХИВА,
        // а реестр архивов, правила и перенос данных принадлежат самой организации — их
        // всегда берут отсюда. Заодно так работает «восстановить»: оно пишет в рабочую среду
        OrgContext Home() => current.Home
            ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.3"));
        // архивы организации (T-40-S0): реестр реплицируется, а открыт/закрыт/удалён —
        // состояние ЭТОГО сервера
        ArchiveService ArchiveList() => Home().Archives;
        AI2P.Connectors.ScheduleRunner Runner() => Ctx().ScheduleRunner;
        AI2P.Connectors.JobConsole Consoles() => Ctx().JobConsole;
        ImportSourceService Imports() => Ctx().Imports;
        AI2P.Connectors.ImportKeyService ImportKeys() => Ctx().ImportKeys;
        TrelloImporter Trello() => Ctx().Trello;
        GitLabImporter GitLab() => Ctx().GitLab;
        // вид источника решает, какой импортёр берётся за работу (T-249): запись
        // справочника знает свой вид, а ссылка — свою форму
        bool IsGitLabSource(string sourceId) =>
            Imports().Get(sourceId) is { } s
            && string.Equals(s.Kind, GitLabImporter.Kind, StringComparison.OrdinalIgnoreCase);
        GitHubImporter GitHub() => Ctx().GitHub;
        bool IsGitHubSource(string sourceId) =>
            Imports().Get(sourceId) is { } s
            && string.Equals(s.Kind, GitHubImporter.Kind, StringComparison.OrdinalIgnoreCase);
        AccountService Accounts() => registry.Accounts;
        // вход в Claude CLI (todo96): процесс `claude auth login` живёт на КОМПЬЮТЕРЕ,
        // а не в организации — поэтому сервис общий, синглтон приложения
        AI2P.Connectors.ClaudeLoginService ClaudeLogin() =>
            services.GetRequiredService<AI2P.Connectors.ClaudeLoginService>();
        // команда CLI из профайла ИИ-исполнителя: входить надо в ТОТ CLI, которым потом
        // работает агент (он может лежать не в PATH). Организации нет — «claude» из PATH
        string CliCommand() => current.Org is null ? "" : Orchestrator().ClaudeCliCommand();

        // права по ролям (ТЗ п. 2.2, гл. 12): единая проверка на всю группу /api —
        // требуемая роль выводится из метода и пути (ApiPermissions), 401/403 отдаются
        // до вызова обработчика
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var problem = ApiPermissions.Check(ctx.HttpContext, current);
            return problem ?? await next(ctx);
        });

        // АРХИВ — ТОЛЬКО ДЛЯ ЧТЕНИЯ (T-45-S0). Данные архива изменению не подлежат: их можно
        // посмотреть либо скопировать обратно в рабочую среду. Интерфейс функций правки в
        // архиве не показывает вовсе, но полагаться на это нельзя — API открыто и плагинам,
        // и внешним клиентам, а «почти для чтения» означает архив, разошедшийся с рабочей
        // средой навсегда: журнал изменений архива никуда не едет и никем не сверяется.
        //
        // Раздел /api/archives сюда НЕ попадает намеренно: он работает с рабочей средой
        // (реестр архивов, правила, перенос и восстановление) — именно им и делается
        // «Восстановить» с открытой карточки архивной задачи.
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var request = ctx.HttpContext.Request;
            var readOnly = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            var toArchivesSection = request.Path.StartsWithSegments("/api/archives")
                                    // /api/state — настройки экрана самого человека (выбранный
                                    // проект, лейаут закладок, виды списков): они пишутся в
                                    // рабочую среду (AppState() → Home()), а не в архив
                                    || request.Path.StartsWithSegments("/api/state");
            if (!readOnly && !toArchivesSection && current.InArchive)
            {
                return Results.Problem(Loc.T("msg.arc.30"), statusCode: StatusCodes.Status403Forbidden);
            }
            return await next(ctx);
        });

        // --- состояние UI: текущие проект и команда (гл. 5, гл. 11 «выбор запоминается») ---
        //
        // ОРГАНИЗАЦИИ МОЖЕТ НЕ БЫТЬ ВОВСЕ (T-148): сервер подал заявку на подключение и ждёт
        // решения. Интерфейс в этом состоянии открывается урезанным, и состояние ему нужно —
        // без него не нарисовать даже шапку. Всё, что живёт в организации, отдаётся пустым.
        api.MapGet("/state", () => current.Org is null
            ? new StateDto
            {
                LocalUser = new Executor(),
                Account = current.Account ?? new Account(),
                Role = current.Role,
                IsServerAdmin = current.IsServerAdmin,
                Language = configHolder.Config.Language,
                Version = AI2P.Core.AppInfo.Version,
                BaseUrl = configHolder.Config.Ui.PublicBaseUrl(),
                AppBaseUrl = configHolder.Config.Ui.PublicBaseUrl(),
                AppOwnBaseUrl = configHolder.Config.Ui.OwnBaseUrl(),
                LocalPort = configHolder.Config.Ui.Port,
                Debug = AI2P.Core.AppInfo.IsDebug,
                // режим запуска и записка об установке сервисом (T-271)
                IsService = ServiceRun.IsService,
                ServiceMode = configHolder.Config.ServiceMode,
                TabTitleMode = configHolder.Config.Ui.TabTitleMode,
                ScheduleCellMode = configHolder.Config.Ui.ScheduleCellMode,
                ServerId = registry.Servers.LocalId(),
                ServerName = registry.Servers.Local()?.Name ?? "",
                NoOrg = true,
            }
            : new StateDto
        {
            CurrentProjectId = AppState().Get(AccountId(), AppStateService.CurrentProjectKey),
            CurrentTeamId = AppState().Get(AccountId(), AppStateService.CurrentTeamKey),
            LocalUser = current.Executor ?? new Executor(),
            Account = current.Account ?? new Account(),
            Role = current.Role,
            IsServerAdmin = current.IsServerAdmin,
            Language = configHolder.Config.Language,
            Version = AI2P.Core.AppInfo.Version,
            // ссылки на задачи ведут в организацию (ТЗ гл. 11, этап 40): /ai2p/<код>/task/<id>
            BaseUrl = Ctx().PublicBaseUrl,
            AppBaseUrl = configHolder.Config.Ui.PublicBaseUrl(),
            // свой ВТОРОЙ адрес и свой порт (T-2-S1): по внешнему имени строятся ссылки,
            // по собственному — узнаются свои же ссылки, по порту — петлевые
            AppOwnBaseUrl = configHolder.Config.Ui.OwnBaseUrl(),
            LocalPort = configHolder.Config.Ui.Port,
            Debug = AI2P.Core.AppInfo.IsDebug,
            // режим запуска и записка об установке сервисом (T-271)
            IsService = ServiceRun.IsService,
            ServiceMode = configHolder.Config.ServiceMode,
            TabTitleMode = configHolder.Config.Ui.TabTitleMode,
            ScheduleCellMode = configHolder.Config.Ui.ScheduleCellMode,
            UiLayout = AppState().Get(AccountId(), AppStateService.UiLayoutKey),
            UiComponentStates = AppState().Get(AccountId(), AppStateService.UiComponentStatesKey),
            Org = Ctx().Org,
            // в списке ещё и организации, куда хозяин этого компьютера может вступить сам
            // (T-186-S0): приехавшая репликацией организация участников здесь не заводит
            Orgs = registry.OrgsOf(AccountId(), CanJoinForeignOrgs()),
            // РАЗДЕЛ ОРГАНИЗАЦИЙ В НАСТРОЙКАХ (T-201-S0): виден хозяину этого компьютера,
            // а не владельцу открытой сейчас организации — иначе он пропадает при переходе
            // в организацию, приехавшую репликацией (там роль admin)
            CanManageOrgs = CanJoinForeignOrgs(),
            // кто мы в кластере (ТЗ гл. 6, этап 42): по этому UI отличает свои задачи
            // от чужих и показывает фильтры по серверам
            ServerId = Ctx().Scope.ServerId,
            ServerCode = Ctx().Scope.Code,
            ServerName = Ctx().Scope.NameOf(Ctx().Scope.ServerId),
            IsConductor = Ctx().IsConductor,
            // адреса ОСТАЛЬНЫХ серверов организации (T-13-S0): по ним показ Markdown узнаёт
            // ссылку на файл, написанную на соседнем сервере, и открывает её через нас
            PeerBaseUrls = PeerBaseUrls(registry, Ctx()),
        });
        // лейаут фреймов и закладок пользователя (todo22): запоминается и
        // восстанавливается при следующем входе (свой у каждого пользователя, v1.46)
        api.MapPut("/state/ui-layout", (UiLayoutDto dto) =>
        {
            AppState().Set(AccountId(), AppStateService.UiLayoutKey, dto.Json);
            return Results.NoContent();
        });
        // состояния представлений — вид/сортировка/фильтр списков (гл. 11):
        // запоминаются и восстанавливаются при следующем входе, как лейаут
        api.MapPut("/state/ui-component-states", (UiLayoutDto dto) =>
        {
            AppState().Set(AccountId(), AppStateService.UiComponentStatesKey, dto.Json);
            return Results.NoContent();
        });
        api.MapPut("/state/current-project/{id?}", (string? id) =>
        {
            AppState().Set(AccountId(), AppStateService.CurrentProjectKey, id);
            return Results.NoContent();
        });
        api.MapPut("/state/current-team/{id?}", (string? id) =>
        {
            AppState().Set(AccountId(), AppStateService.CurrentTeamKey, id);
            return Results.NoContent();
        });

        // --- организации (ТЗ п. 2.15, этап 40) ---
        // Организация — контейнер всех данных: своя БД, свой каталог, своя нумерация.
        // Записи о них лежат в серверной БД, поэтому раздел работает и без контекста
        // организации (иначе создать первую организацию было бы нечем).
        api.MapGet("/orgs", () =>
        {
            var accountId = AccountId();
            var canJoin = CanJoinForeignOrgs();
            var all = registry.Orgs.List();
            foreach (var org in all)
            {
                org.IsMember = org.DeletedAt is null
                               && registry.Context(org).Executors.ByAccount(accountId) is not null;
                // «можно вступить самому» (T-186-S0) — организация на этом компьютере есть,
                // а участия в ней у хозяина компьютера нет: так приезжает организация,
                // в которую нас включили галочкой на дирижёре
                org.CanJoin = org is { IsMember: false, DeletedAt: null, IsActive: true } && canJoin;
                // КАТАЛОГ НА ЭТОМ КОМПЬЮТЕРЕ (T-148-S0, второй заход): он не обязан совпадать
                // с номером — у организации, чей номер когда-то столкнулся с приехавшей,
                // каталог называется ORG-1-2. Человек, удаляющий организацию, должен видеть
                // ИМЕННО его, иначе «удалил ORG-1» читается как «удалил чужие данные»
                org.DirName = registry.Orgs.KnownDirName(org.Id);
                // «почему нельзя удалить» — как у сервера (T-141): кнопка гасится
                // с объяснением, а не исчезает молча
                org.DeleteProblem = org.DeletedAt is null ? registry.DeleteProblem(org.Id) ?? "" : "";
            }
            return all;
        });
        api.MapGet("/orgs/{id}/servers", (string id) => registry.Servers.ServersOf(id));

        // --- ВСТУПИТЬ В ОРГАНИЗАЦИЮ ЭТОГО КОМПЬЮТЕРА (T-186-S0) ---
        //
        // Организация приехала сюда репликацией (дирижёр включил наш сервер галочкой в своей
        // форме) — и не видна ни в переключателе, ни кнопкой «открыть»: участников она здесь
        // не заводит, а без исполнителя вошедший ей чужой (ApiPermissions). Заявкой такое
        // не чинится: заявка подаётся ДО подключения, а сервер уже подключён.
        //
        // Поэтому вступление делается здесь — но только хозяином этого компьютера (владелец
        // системы или локальный администратор сервера) и только в организацию, которая уже
        // лежит на его диске: прав это не расширяет, все её данные у него и так есть.
        // Роль — admin: хозяин ОДНОГО компьютера кластера (ТЗ п. 2.2), а не владелец чужой
        // организации. Исполнитель уедет дирижёру ближайшим сеансом — так же, как участник,
        // заведённый по подтверждённой заявке.
        api.MapPost("/orgs/{id}/join", (string id) => Handle(() =>
        {
            if (!CanJoinForeignOrgs())
            {
                return Results.Problem(Loc.T("msg.org.11"),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var account = current.Account
                          ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.2"));
            var org = registry.Orgs.Get(id) is { DeletedAt: null, IsActive: true } found
                ? found
                : throw new ArgumentException(Loc.T("msg.org.12"));
            var context = registry.Context(org);
            registry.AddMember(context, account, SystemRole.Admin, ServerActor());
            org.IsMember = true;
            org.CanJoin = false;
            return Results.Ok(org);
        }));

        // --- СМЕНА ОРГАНИЗАЦИИ (T-188) ---
        // Смена организации — не переключение фильтра, а уход из одной базы в другую: свои
        // проекты, свои задачи, свои агенты и свои процессы моделей на этом компьютере.
        // Поэтому она идёт в два шага: сначала «кого остановим» (GET), потом сама остановка
        // (POST). Организация запроса — из адреса, поэтому останавливается ровно ТА, из
        // которой уходят: на другой вкладке браузера может быть открыта другая, и её
        // агентов это не трогает.
        api.MapGet("/org/agents", () => Handle(() =>
        {
            // в списке только то, что реально будет остановлено (OrgAgents.Stoppable):
            // у представления «в работе у ИИ» (T-187) в выдаче есть ещё и задачи
            // с перенесённым стартом — агент ими не занят, останавливать нечего
            return Results.Ok(new OrgAgentsDto
            {
                Agents = OrgAgents.Stoppable(Tasks().AiWork(),
                    Jobs().ListActive().Select(j => j.Id)),
                Teams = TeamWork().ActiveTeams().Select(t => t.Name).ToList(),
            });
        }));
        api.MapPost("/org/agents/stop", () => HandleAsync(async () =>
        {
            var result = new OrgStopResultDto();
            var actor = Actor();
            // тот же отбор, что и в предупреждении: список и дело обязаны совпадать
            foreach (var item in OrgAgents.Stoppable(Tasks().AiWork(),
                         Jobs().ListActive().Select(j => j.Id)))
            {
                try
                {
                    await Orchestrator().CancelJobAsync(item.JobId, actor);
                    result.Jobs++;
                }
                catch (Exception ex)
                {
                    // задание чужого сервера снимается там же, где выполняется (гл. 6, этап 42):
                    // здесь его снять нельзя — но остальных это останавливать не должно
                    result.Errors.Add($"{item.Task.DisplayId}: {ex.Message}");
                }
            }
            // открытые очереди иерархического запуска закрываются здесь же (T-263): иначе
            // снятые задания сторож поднимет через минуту, и «остановить всех» не
            // останавливает ничего. Свои — чужие ведёт их сервер
            foreach (var root in Tasks().ListHierarchyRuns().Where(r => Tasks().CanWrite(r)))
            {
                try
                {
                    await Orchestrator().StopTaskAsync(root.Id, actor, withHierarchy: true);
                    result.Hierarchies++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{root.DisplayId}: {ex.Message}");
                }
            }
            // отключение агентов и выгрузка локальных серверов моделей — вместе с командами
            result.Teams = TeamWork().StopAllTeams(actor, actor ?? "");
            return Results.Ok(result);
        }));

        // --- серверы ТЕКУЩЕЙ организации для рядового участника (ТЗ гл. 6, этап 42) ---
        // Раздел /api/servers — карта кластера с адресами и токенами, он закрыт владельцем.
        // А здесь только то, что нужно каждому: код, имя и «это мы» — для фильтров по
        // серверам в списках и для выбора сервера в кнопке «сменить сервер».
        api.MapGet("/org/servers", () =>
        {
            var context = Ctx();
            var localId = registry.Servers.LocalId();
            return registry.Servers.ServersOf(context.Org.Id, includeRequests: false)
                .Select(link => new OrgServerBriefDto
                {
                    ServerId = link.ServerId,
                    Code = link.Code,
                    Name = link.ServerName,
                    IsLocal = link.ServerId == localId,
                    IsConductor = link.IsConductor,
                    IsActive = link.IsActive,
                })
                .ToList();
        });
        // серверы организации правятся из её формы (ТЗ п. 2.15: связь многие-ко-многим,
        // todo41 п. 11): у организации свой список серверов, у сервера — свой список организаций
        api.MapPut("/orgs/{id}/servers", (string id, OrgServersSaveDto dto) => Handle(() =>
        {
            SyncOrgServers(registry, id, dto.ServerIds, dto.ConductorServerId, Actor());
            return Results.Ok(registry.Servers.ServersOf(id));
        }));
        api.MapPost("/orgs", (OrgSaveDto dto) => Handle(() =>
        {
            var account = current.Account
                          ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.2"));
            var (org, _) = registry.CreateOrg(dto.Name, dto.Code, account, Actor());
            return Results.Ok(org);
        }));
        api.MapPut("/orgs/{id}", (string id, OrgSaveDto dto) => Handle(() =>
        {
            var org = registry.Orgs.Update(id, dto.Name, dto.Code, dto.IsActive, Actor());
            registry.RefreshCodes();
            return Results.Ok(org);
        }));
        // УДАЛИТЬ ОРГАНИЗАЦИЮ С ЭТОГО СЕРВЕРА (T-148-S0, второй заход): записи серверной БД,
        // база со всеми шаблонами и правилами, каталог с файлами. Удаление местное: на других
        // серверах организация остаётся, и там её удаляют так же — своей кнопкой.
        api.MapDelete("/orgs/{id}", (string id) => Handle(() =>
        {
            // ту организацию, в которой человек сейчас работает, удалять нельзя: страница
            // осталась бы открытой над несуществующей базой
            if (current.Org is { } open && open.Org.Id == id)
            {
                throw new ArgumentException(Loc.T("msg.org.10"));
            }
            registry.DeleteOrg(id, Actor());
            return Results.NoContent();
        }));

        // --- серверы кластера (ТЗ п. 2.15, гл. 6; этап 41) ---
        // Сервер — сущность уровня установки, с организациями связан «многие ко многим».
        // Локальный сервер в списке ВСЕГДА ПЕРВЫЙ, а его адрес и каталоги правятся не здесь,
        // а в config.json (раздел /api/servers/local): от них зависит запуск приложения.
        api.MapGet("/servers", () =>
        {
            var all = registry.Servers.List();
            foreach (var server in all)
            {
                // «почему нельзя удалить» (T-141) считается здесь: кнопка удаления в списке
                // должна быть погашена с объяснением, а не исчезать молча
                server.DeleteProblem = registry.Servers.DeleteProblem(server.Id) ?? "";
                // КОД В КЛАСТЕРЕ (T-170-S0): форма организации показывает его и у сервера,
                // который в эту организацию ещё не включён, — код принадлежит кластеру,
                // а не одной организации, и меняться от галочки в форме не должен
                server.ClusterCode = registry.Servers.ClusterCodeOf(server.Id);
            }
            return all;
        });
        // all=true — «показать все заявки» (T-293): к ждущим решения добавляются отклонённые.
        // Значение разбирается bool.TryParse, поэтому годится только слово true
        api.MapGet("/servers/requests", (bool? all) =>
        {
            var requests = registry.Servers.Requests(all == true);
            foreach (var request in requests)
            {
                // ЕСТЬ ЛИ ЗАЯВИТЕЛЬ СРЕДИ ИСПОЛНИТЕЛЕЙ ОРГАНИЗАЦИИ (T-150): форма подтверждения
                // показывает это прямо в строке и предлагает завести его — иначе про участие
                // человека забывают, а без него организацию он не увидит и после репликации
                request.ApplicantMember = registry.ApplicantMemberName(request);
            }
            return requests;
        });
        api.MapPost("/servers", (ServerSaveDto dto) => Handle(() =>
        {
            var server = registry.Servers.Create(ToServerInput(dto), ServerActor());
            if (dto.OrgIds is not null)
            {
                SyncServerOrgs(registry, server.Id, dto.OrgIds, ServerActor());
            }
            return Results.Ok(registry.Servers.Get(server.Id));
        }));
        api.MapPut("/servers/{id}", (string id, ServerSaveDto dto) => Handle(() =>
        {
            registry.Servers.Update(id, ToServerInput(dto), ServerActor());
            if (dto.OrgIds is not null)
            {
                SyncServerOrgs(registry, id, dto.OrgIds, ServerActor());
            }
            return Results.Ok(registry.Servers.Get(id));
        }));
        // УДАЛИТЬ СЕРВЕР ИЗ СПИСКА (T-141): только пока с ним не было ни одной репликации —
        // тогда запись вычёркивается начисто и его код (S1) освобождается. Так убирают
        // неудачное первое подключение, иначе повторная попытка заводила бы второй такой же
        api.MapDelete("/servers/{id}", (string id) => Handle(() =>
        {
            registry.Servers.Delete(id, ServerActor());
            return Results.Ok(registry.Servers.List());
        }));
        // «на связи ли сервер»: узнаём его по внутреннему ключу, а не по адресу (todo41 п. 6)
        api.MapPost("/servers/{id}/check", async (string id) =>
        {
            var server = registry.Servers.Get(id);
            if (server is null)
            {
                return Results.NotFound();
            }
            // до сервера за NAT не дозвониться — по его петлевому адресу мы попадём к себе
            // и приняли бы СЕБЯ за него (T-141)
            if (ServerAddress.Unreachable(server, registry.Servers.Local()) is { } unreachable)
            {
                registry.Servers.SetLastError(id, unreachable);
                return Results.Ok(new ServerCheckDto { Error = unreachable });
            }
            using var client = new ClusterClient();
            var result = server.IsLocal
                ? new ServerCheckDto { Ok = true, ServerId = server.Id, Name = server.Name,
                    Version = AI2P.Core.AppInfo.Version }
                : await client.HelloAsync(server);
            registry.Servers.SetLastError(id, result.Ok ? "" : result.Error);
            return Results.Ok(result);
        });
        // подать заявку на подключение к организации ЧУЖОГО дирижёра (двусторонний обмен, п. 7)
        api.MapPost("/servers/{id}/join", async (string id, ServerJoinDto dto) =>
        {
            var target = registry.Servers.Get(id);
            if (target is null || registry.Servers.Local() is null)
            {
                return Results.NotFound();
            }
            // заявка анонимна ДЛЯ ДИРИЖЁРА (реквизитов там у нас нет), но называет подавшего:
            // это вошедший здесь человек — его увидит тот, кто принимает решение (T-139)
            if (current.Account is not { } account)
            {
                return Results.Problem(Loc.T("msg.apiEndpoints.4"), statusCode: 400);
            }
            try
            {
                // сам обмен — общий с экраном подключения к кластеру (T-135, ClusterJoinFlow).
                // Кодов организаций может быть НЕСКОЛЬКО через запятую (T-141): выбор
                // в форме множественный, заявка уходит в каждую отмеченную организацию
                return Results.Ok(await ClusterJoinFlow.RequestManyAsync(registry, target,
                    AI2P.Storage.Services.ServerService.Split(dto.OrgCode),
                    new JoinApplicant(account.Id, account.Name, account.Email), ServerActor(),
                    dto.Note));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or HttpRequestException
                                           or TaskCanceledException)
            {
                registry.Servers.SetLastError(id, ex.Message);
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // узнать решение по своей заявке (её принимает человек на дирижёре)
        api.MapGet("/servers/{id}/join", async (string id) =>
        {
            var target = registry.Servers.Get(id);
            if (target is null)
            {
                return Results.NotFound();
            }
            try
            {
                // подтверждение заявки завершает подключение (токены, ключ организации,
                // заведение организации) — общий код с экраном подключения (T-135)
                return Results.Ok(await ClusterJoinFlow.PollAsync(registry, secrets, target,
                    ServerActor()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                           or TaskCanceledException)
            {
                registry.Servers.SetLastError(id, ex.Message);
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // передать задачи выбывшего сервера дирижёру (ТЗ гл. 6, этап 42): единственный случай,
        // когда владельца задачи меняет не её сервер — иначе задачи неактивного сервера
        // остались бы нередактируемыми навсегда. Кнопка есть только у неактивного сервера
        api.MapPost("/servers/{id}/takeover", (string id) => Handle(() =>
        {
            var context = Ctx();
            var server = registry.Servers.Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
            if (server.IsActive && registry.Servers.Link(context.Org.Id, id)?.IsActive == true)
            {
                throw new ArgumentException(
                    Loc.T("msg.apiEndpoints.6"));
            }
            return Results.Ok(new TakeoverResultDto { Tasks = context.Tasks.TakeOverTasksOf(id, Actor()) });
        }));

        // --- СМЕНА ДИРИЖЁРА (T-21-S1) ---
        //
        // Дирижёр обязателен и ровно один (ТЗ гл. 6), и от него зависит выдача кодов серверам,
        // ведение справочников, расписания и вся топология репликации. Поэтому передача
        // дирижёрства — не переставленный флажок, а ОТДЕЛЬНАЯ ДВУСТОРОННЯЯ операция, устроенная
        // как первое подключение сервера: заявка едет второй стороне обычной репликацией,
        // решение принимает человек там.
        //
        // Все четыре эндпойнта требуют ЛОКАЛЬНОГО ВХОДА АДМИНИСТРАТОРА СЕРВЕРА: дирижёрство —
        // это про сам компьютер в кластере, а не про роль в организации (T-174). Кто именно
        // вправе подавать заявку (только назначаемый сервер или сегодняшний дирижёр, но не
        // третий) решает ConductorFlow — общее правило для кнопки и для API.
        IResult? ServerAdminOnly() => current.IsServerAdmin
            ? null
            : Results.Problem(Loc.T("msg.apiEndpoints.39"),
                statusCode: StatusCodes.Status403Forbidden);

        api.MapGet("/servers/{id}/conductor", (string id) => Handle(() =>
        {
            var context = Ctx();
            var server = registry.Servers.Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
            var problem = ConductorFlow.ProblemWithRequest(registry, context.Org, server);
            var open = context.ConductorRequests.Open();
            // «форма настройки сервера НЕ ДИРИЖЁРА»: раздел показывается только у активного
            // участника организации, который сегодня дирижёром не является
            var link = registry.Servers.Link(context.Org.Id, server.Id);
            return Results.Ok(new ConductorInfoDto
            {
                Applicable = link is { Status: OrgServerStatus.Active, IsConductor: false },
                CanRequest = problem is null && open is null,
                Problem = problem ?? (open is null ? "" : Loc.T("msg.conductor.3")),
                IsServerAdmin = current.IsServerAdmin,
                Request = open is null
                    ? null
                    : ConductorFlow.ToDto(open, registry.Servers.LocalId(), DateTime.UtcNow),
            });
        }));

        // подать заявку «назначить дирижёром»: цель — сервер {id}, подающий — ЭТОТ сервер
        api.MapPost("/servers/{id}/conductor", (string id, ConductorRequestCreateDto? dto) =>
            ServerAdminOnly() ?? Handle(() =>
            {
                var context = Ctx();
                var server = registry.Servers.Get(id)
                             ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
                if (ConductorFlow.ProblemWithRequest(registry, context.Org, server) is { } problem)
                {
                    throw new ArgumentException(problem);
                }
                var request = context.ConductorRequests.Add(server.Id,
                    ConductorFlow.ConductorIdOf(registry, context.Org), dto?.Note ?? "",
                    current.Account is { } who ? $"{who.Name} <{who.Email}>" : "", ServerActor());
                return Results.Ok(ConductorFlow.ToDto(request, registry.Servers.LocalId(),
                    DateTime.UtcNow));
            }));

        // all=true — «показать все заявки» (T-293): к ждущей решения добавляются решённые
        // и снятые. Без него список отдаёт только то, с чем человеку ещё надо что-то делать
        api.MapGet("/servers/conductor-requests", (bool? all) => Handle(() =>
        {
            var localId = registry.Servers.LocalId();
            var now = DateTime.UtcNow;
            return Results.Ok(Ctx().ConductorRequests.List(all == true)
                .Select(r => ConductorFlow.ToDto(r, localId, now)).ToList());
        }));

        // решение по заявке: accept / reject — вторая сторона; withdraw — подавший;
        // takeover — ОДНОСТОРОННЕЕ принятие назначаемым сервером по истечении 12 часов
        api.MapPost("/servers/conductor-requests/{id}/{decision}", async (string id, string decision) =>
        {
            if (ServerAdminOnly() is { } denied)
            {
                return denied;
            }
            return await HandleAsync(async () =>
            {
                var context = Ctx();
                var localId = registry.Servers.LocalId();
                var request = context.ConductorRequests.Get(id)
                              ?? throw new ArgumentException(Loc.T("msg.conductor.4"));
                var (mine, canDecide) = ConductorFlow.SidesOf(request, localId);
                var who = current.Account is { } account
                    ? $"{account.Name} <{account.Email}>"
                    : "";
                var result = decision.ToLowerInvariant() switch
                {
                    // СНЯТЬ заявку вправе только подавший (ТЗ гл. 6: строку правит её автор)
                    "withdraw" when mine => context.ConductorRequests.Decide(id,
                        ConductorRequestStatus.Withdrawn, who, ServerActor()),
                    "withdraw" => throw new ArgumentException(Loc.T("msg.conductor.10")),
                    "reject" when canDecide => context.ConductorRequests.Decide(id,
                        ConductorRequestStatus.Rejected, who, ServerActor()),
                    "accept" when canDecide => context.ConductorRequests.Decide(id,
                        ConductorRequestStatus.Accepted, who, ServerActor()),
                    // ОДНОСТОРОННЕЕ ПРИНЯТИЕ (T-21-S1): дирижёр не отозвался за 12 часов —
                    // назначаемый сервер принимает СВОЮ ЖЕ заявку сам, иначе организация
                    // с физически потерянным дирижёром осталась бы без дирижёра навсегда
                    "takeover" when ConductorRequestService.CanTakeUnilaterally(request, localId,
                        DateTime.UtcNow) => context.ConductorRequests.Decide(id,
                        ConductorRequestStatus.Accepted, who, ServerActor(), unilateral: true),
                    "takeover" => throw new ArgumentException(Loc.T("msg.conductor.11")),
                    "accept" or "reject" => throw new ArgumentException(Loc.T("msg.conductor.12")),
                    _ => throw new ArgumentException(Loc.T("msg.apiEndpoints.9", decision)),
                };
                if (result.Status == ConductorRequestStatus.Accepted)
                {
                    await ConductorFlow.ApplyAsync(registry, secrets, context.Org,
                        result.TargetServerId, result.Id, ServerActor());
                }
                return Results.Ok(ConductorFlow.ToDto(result, localId, DateTime.UtcNow));
            });
        });

        // --- репликация (ТЗ п. 6.1, гл. 6; этап 43) ---
        // Все серверы реплицируются только с дирижёром, поэтому на дирижёре видны состояния
        // всех серверов и кнопки ручного пуска у каждого, а на рядовом сервере — только своё
        // (todo43 «список серверов»). Экран диагностики берёт данные отсюда же.
        var replication = services.GetRequiredService<ReplicationService>();
        api.MapGet("/servers/replication", () => Handle(() =>
            Results.Ok(ReplicationStatus(registry, replication))));

        // ручной пуск: «при ручном пуске всё переустанавливается точно так же» (todo43) —
        // отсчёт до следующей репликации начнётся после окончания этого сеанса
        api.MapPost("/servers/{id}/replicate", (string id) => HandleAsync(async () =>
        {
            // сеанс идёт в фоне: первичная репликация занимает минуты, и держать на ней
            // запрос браузера нельзя — UI показывает прогресс, опрашивая состояние
            await StartReplicationAsync(registry, secrets, replication, id,
                current.Account?.Email ?? ReplicationStarters.Manual, ServerActor());
            return Results.Ok(ReplicationStatus(registry, replication));
        }));

        // интервалы репликации сервера: на дирижёре — любому серверу, на остальных — только
        // своему (todo43 «форма сервера»)
        api.MapPut("/servers/{id}/replication", (string id, ReplSettingsDto dto) => Handle(() =>
        {
            var localId = registry.Servers.LocalId();
            var conductor = current.Org is { } org && registry.Servers.IsLocalConductor(org.Org.Id);
            if (id != localId && !conductor)
            {
                throw new ArgumentException(
                    Loc.T("msg.apiEndpoints.7"));
            }
            registry.Servers.SetReplication(id, dto.IntervalSec, dto.RetrySec, ServerActor());
            return Results.Ok(registry.Servers.Get(id));
        }));

        // --- конфликты файлов (ТЗ гл. 6, этап 44; todo44) ---
        // Файл, изменившийся за интервал репликации на ДВУХ серверах, не реплицируется вовсе,
        // пока человек не выберет одно из четырёх решений. Левая сторона пары — всегда дирижёр.
        api.MapGet("/servers/{id}/file-conflicts", (string id) => Handle(() =>
        {
            var conflicts = registry.FileSync.Conflicts(id);
            foreach (var conflict in conflicts)
            {
                conflict.OrgCode = registry.Orgs.Get(conflict.OrgId)?.Code ?? "";
                conflict.ServerCode = registry.Servers.CodeOf(conflict.OrgId, conflict.ServerId);
                var ctx = registry.ById(conflict.OrgId);
                if (conflict.ProjectId.Length > 0 && ctx is not null)
                {
                    conflict.ProjectName = ctx.Projects.Get(conflict.ProjectId)?.Name ?? "";
                }
                // у конфликта КЛЮЧА API вместо проекта показывается модель (этап 45),
                // а с версии 1.65 — ещё и источник импорта, чьи ключ и токен лежат там же;
                // сами значения наружу не отдаются — только отпечатки
                if (conflict.Scope == ReplicationService.KeyScope && ctx is not null)
                {
                    conflict.ProjectName = ctx.ModelKeys.ModelNameOf(conflict.Path) is { Length: > 0 } name
                        ? name
                        : ctx.ImportKeys.SourceNameOf(conflict.Path);
                }
                // у конфликта ЗАПИСИ СПРАВОЧНИКА МОДЕЛЕЙ (T-227) путь — идентификатор записи,
                // а человеку нужно имя модели: его и показываем в списке
                if (conflict.Scope == ReplicationService.ModelScope && ctx is not null)
                {
                    conflict.ProjectName = ctx.Models.Get(conflict.Path)?.Name ?? "";
                }
                conflict.LeftValue = "";
                conflict.RightValue = "";
            }
            return Results.Ok(conflicts);
        }));
        // Решение по конфликту. У ФАЙЛА оно откладывается до следующей репликации (todo44),
        // а у КЛЮЧА API применяется сразу: это обычная локальная запись, которая по правилу
        // «выиграл последний» доедет до партнёра сама, и держать её до сеанса незачем.
        api.MapPost("/servers/file-conflicts/{id}/resolve", (string id, ReplFileResolveDto dto) =>
            Handle(() =>
            {
                var conflict = registry.FileSync.Get(id)
                               ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.8"));
                // запись справочника моделей (T-227) — как ключ: решение применяется сразу
                if (conflict.Scope == ReplicationService.ModelScope)
                {
                    ResolveModelConflict(registry, conflict, dto, Actor());
                    return Results.Ok(new ReplFileConflictDto { Id = id, Resolution = dto.Resolution });
                }
                if (conflict.Scope != ReplicationService.KeyScope)
                {
                    registry.FileSync.Resolve(id, dto.Resolution);
                    return Results.Ok(registry.FileSync.Get(id));
                }
                ResolveKeyConflict(registry, conflict, dto, Actor());
                return Results.Ok(new ReplFileConflictDto { Id = id, Resolution = dto.Resolution });
            }));

        // забыть курсоры пары и перечитать всё заново (кнопка «первичная репликация»
        // на экране диагностики): лечение расхождения реплик без правки БД руками
        api.MapPost("/servers/{id}/replication/reset", (string id) => Handle(() =>
        {
            var context = Ctx();
            // ЭТА ЖЕ КНОПКА СНИМАЕТ ЗАВИСШИЙ СЕАНС (T-174): курсоры человек сбрасывал, а
            // замок пары оставался занятым — и «первоначальная репликация» не начиналась
            // ровно так же, как обычная. Это и есть ручной рычаг «отпустить пару»: сброс
            // курсоров посреди настоящего сеанса и без того ничего осмысленного не значит
            replication.Release(context.Org.Id, id);
            registry.ReplState.Reset(context.Org.Id, id);
            // И ОЧЕРЕДЬ ПОВТОРА ТОЖЕ (T-20-S1). Она задумана как временная — «родитель
            // приедет следующей пачкой», — но строка, которую применить нельзя в принципе,
            // висела в ней вечно: на экране диагностики человек видел «Ждут повтора строк 3»
            // и «попыток 1597», а убрать это было нечем — вызова Reset не было ни у одной
            // кнопки. Теперь «первичная репликация» забывает очередь вместе с курсорами:
            // журнал будет перечитан с нуля, и отложенное приедет заново — уже с родителями
            registry.ReplPending.Reset(context.Org.Id, id);
            // «первичная» относится и к файлам: база сравнения забывается, и следующий сеанс
            // сверит каталоги заново по хэшам (этап 44)
            registry.FileSync.Reset(id);
            return Results.Ok(ReplicationStatus(registry, replication));
        }));

        // Списка организаций удалённого сервера по логину («POST /api/servers/{id}/orgs»)
        // больше нет (T-139, доработка): он требовал ввести почту и пароль аккаунта НА ЧУЖОМ
        // сервере. Организацию называют кодом в заявке — либо не называют вовсе, если
        // на дирижёре она одна.

        // решение по ВХОДЯЩЕЙ заявке (todo41 пп. 9–10): принять / отложить / отклонить.
        // Тело необязательно (T-150): в нём приезжает только флажок «завести участника»,
        // и его отсутствие означает «завести» — так же, как было до T-150
        api.MapPost("/servers/requests/{id}/{decision}",
            (string id, string decision, ServerRequestDecisionDto? dto) => Handle(() =>
            Results.Ok(decision.ToLowerInvariant() switch
            {
                // «принять» — это ещё и «пустить заявителя в организацию» (T-139): его
                // аккаунт приедет репликацией, заводить человека руками не нужно
                "accept" => registry.AcceptJoinRequest(id, Actor(), dto?.AddMember ?? true),
                "defer" => registry.Servers.DeferRequest(id, Actor()),
                "reject" => registry.Servers.RejectRequest(id, Actor()),
                _ => throw new ArgumentException(Loc.T("msg.apiEndpoints.9", decision)),
            })));

        // --- настройки ЛОКАЛЬНОГО сервера (форма локального сервера, гл. 10, гл. 12) ---
        // Пишутся в config.json, а не в БД: от них зависит запуск приложения, и исправить
        // их нужно уметь снаружи системы — например, когда порт занят и сервер не поднялся.
        api.MapGet("/servers/local", () =>
        {
            var config = configHolder.Config;
            var local = registry.Servers.Local();
            // сертификат HTTPS (T-206): форма показывает не только сами настройки, но и то,
            // что по ним ПОЛУЧАЕТСЯ прямо сейчас — найденный сертификат со сроком годности
            // либо причину отказа. Без этого «сохранил и надеюсь» проверяется перезапуском
            var certInfo = "";
            var certOk = false;
            // сертификат может ЧИТАТЬСЯ и при этом не годиться браузеру (T-315): назван
            // сертификатом УЦ или выписан не на то имя — про это отдельная строка формы
            var certWarning = "";
            if (config.Ui.IsHttps)
            {
                try
                {
                    using var found = HttpsCertificate.Load(config.Ui.Https, config.Ui.Hostname,
                        configHolder.Path, secrets.Read(config.Ui.Https.PasswordRefOrDefault()) ?? "");
                    certInfo = HttpsCertificate.Describe(found);
                    certOk = true;
                    certWarning = HttpsCertificate.BrowserWarning(found, config.Ui.Hostname,
                        config.Ui.Hostname2) ?? "";
                }
                catch (Exception ex)
                {
                    certInfo = ex.Message;
                }
            }
            return new ServerSettingsDto
            {
                Port = config.Ui.Port,
                Protocol = config.Ui.Protocol,
                HttpsSource = config.Ui.Https.FromStore
                    ? Ai2pConfig.HttpsSettings.SourceStore
                    : Ai2pConfig.HttpsSettings.SourceFile,
                HttpsCertFile = config.Ui.Https.CertFile,
                HttpsKeyFile = config.Ui.Https.KeyFile,
                HttpsCertPasswordSet =
                    (secrets.Read(config.Ui.Https.PasswordRefOrDefault()) ?? "").Length > 0,
                HttpsStoreLocation = config.Ui.Https.StoreLocation,
                HttpsStoreName = config.Ui.Https.StoreName,
                HttpsSubject = config.Ui.Https.Subject,
                HttpsThumbprint = config.Ui.Https.Thumbprint,
                HttpsTrustAnyPeer = config.Ui.Https.TrustAnyPeer,
                HttpsCertOk = certOk,
                HttpsCertInfo = certInfo,
                HttpsCertWarning = certWarning,
                Hostname = config.Ui.Hostname,
                // второй адрес — под ним сервер видно снаружи (T-2-S1)
                Hostname2 = config.Ui.Hostname2,
                Port2 = config.Ui.Port2,
                BindAddress = config.Ui.BindAddress,
                BasePath = config.Ui.NormalizedBasePath(),
                ModelsRepo = config.Storage.ModelsRepo,
                DistDir = config.Storage.DistDir,
                PackagesDir = config.Storage.PackagesDir,
                // во что эти каталоги разворачиваются на самом деле (T-291): относительный
                // путь без этого ничего человеку не говорит
                ModelsRepoPath = config.ModelsRepoPath(configHolder.MachineRoot),
                DistDirPath = config.DistDirPath(configHolder.MachineRoot),
                PackagesDirPath = config.PackagesDirPath(configHolder.MachineRoot),
                AdminPasswordSet = Ai2pAuth.AdminPasswordSet(secrets),
                ServerId = local?.Id ?? "",
                Code = current.Org is { } org ? registry.Servers.LocalCode(org.Org.Id) : "",
                IsConductor = current.Org is { } own && registry.Servers.IsLocalConductor(own.Org.Id),
                // интервалы репликации — не настройка config.json, а поле записи сервера
                // (ТЗ гл. 6, этап 43): сохраняются отдельным вызовом и реплицируются
                ReplIntervalSec = local?.ReplIntervalSec,
                ReplRetrySec = local?.ReplRetrySec,
                // интервал принадлежит серверу, а признак дирижёра — организации: в одной
                // сервер дирижёр, в другой рядовой. Поля нужны, если он рядовой хоть где-то
                HasReplication = local is not null && registry.Servers
                    .OrgsOf(local.Id, includeRequests: false)
                    .Any(l => l.Status == OrgServerStatus.Active && !l.IsConductor),
            };
        });
        api.MapPut("/servers/local", (ServerSettingsDto dto) => Handle(() =>
        {
            ValidateLocalServer(dto);
            var config = configHolder.Config;
            var protocol = dto.Protocol.Trim().ToLowerInvariant() == "https" ? "https" : "http";
            // СЕРТИФИКАТ HTTPS (T-206). Новые значения собираются ОТДЕЛЬНО и проверяются
            // ДО того, как попасть в рабочую конфигурацию: «https» без годного сертификата —
            // это установка, которая после перезапуска не поднимется, и войти в неё, чтобы
            // исправить, будет уже нельзя. Пароль .pfx в config.json не попадает никогда
            // (гл. 10): он уходит в секреты по ссылке, а пустое поле значит «не менять»
            var https = new Ai2pConfig.HttpsSettings
            {
                Source = dto.HttpsSource.Trim().Equals(Ai2pConfig.HttpsSettings.SourceStore,
                    StringComparison.OrdinalIgnoreCase)
                    ? Ai2pConfig.HttpsSettings.SourceStore
                    : Ai2pConfig.HttpsSettings.SourceFile,
                CertFile = dto.HttpsCertFile.Trim(),
                KeyFile = dto.HttpsKeyFile.Trim(),
                PasswordRef = config.Ui.Https.PasswordRefOrDefault(),
                StoreLocation = dto.HttpsStoreLocation.Trim().Length > 0
                    ? dto.HttpsStoreLocation.Trim()
                    : "CurrentUser",
                StoreName = dto.HttpsStoreName.Trim().Length > 0 ? dto.HttpsStoreName.Trim() : "My",
                Subject = dto.HttpsSubject.Trim(),
                Thumbprint = dto.HttpsThumbprint.Trim(),
                TrustAnyPeer = dto.HttpsTrustAnyPeer,
            };
            var certPassword = dto.HttpsCertPassword is { Length: > 0 }
                ? dto.HttpsCertPassword
                : secrets.Read(https.PasswordRefOrDefault()) ?? "";
            if (protocol == "https"
                && HttpsCertificate.Problem(https, dto.Hostname.Trim(), configHolder.Path,
                    certPassword) is { } certProblem)
            {
                throw new ArgumentException(Loc.T("msg.https.11", certProblem));
            }
            if (dto.HttpsCertPassword is { Length: > 0 })
            {
                secrets.Write(https.PasswordRefOrDefault(), dto.HttpsCertPassword);
            }
            config.Ui.Https = https;
            // ДОВЕРИЕ СЕРТИФИКАТУ СОСЕДЕЙ применяется СРАЗУ (T-315): в отличие от порта и
            // протокола, ничего перезапускать для него не нужно — проверка спрашивает эту
            // настройку в момент рукопожатия. Репликация встала из-за недоверия сертификату
            // и чинится этим флажком, а требовать ради него перезапуск незачем
            HttpsPeers.TrustAny = https.TrustAnyPeer;
            // адрес применяется после перезапуска — процесс уже слушает прежний порт
            config.Ui.Port = dto.Port;
            config.Ui.Protocol = protocol;
            config.Ui.Hostname = dto.Hostname.Trim();
            // второй адрес (T-2-S1): порт без имени — не адрес, он и не хранится
            config.Ui.Hostname2 = dto.Hostname2.Trim();
            config.Ui.Port2 = config.Ui.Hostname2.Length > 0 ? dto.Port2 : null;
            config.Ui.BindAddress = dto.BindAddress.Trim().Length > 0 ? dto.BindAddress.Trim() : "0.0.0.0";
            // каталоги ЭТОГО компьютера: репозиторий моделей, дистрибутивы, пакеты (ТЗ v1.42)
            config.Storage.ModelsRepo = dto.ModelsRepo.Trim().Length > 0
                ? dto.ModelsRepo.Trim()
                : Ai2pConfig.StorageSettings.DefaultModelsRepo;
            config.Storage.DistDir = dto.DistDir.Trim();
            config.Storage.PackagesDir = dto.PackagesDir.Trim();
            config.Save(configHolder.Path);
            if (dto.AdminPassword is { Length: > 0 })
            {
                Ai2pAuth.SetAdminPassword(secrets, dto.AdminPassword);
            }
            // Запись о себе в списке серверов держится в согласии с конфигом. Адрес здесь
            // ВНУТРЕННИЙ (hostname/port), а не внешний (T-2-S1): по нему сервер зовут соседи
            // по кластеру, и заворачивать их трафик на публичное имя роутера незачем.
            // ВТОРОЙ АДРЕС УЕЗЖАЕТ СОСЕДЯМ (T-50-S0): внешнее имя знает только сам сервер,
            // а звонить по нему — соседям, у которых внутренний адрес из их сети не виден
            registry.EnsureLocalServer(config.Ui.Hostname, config.Ui.Protocol, config.Ui.Hostname,
                config.Ui.Port, config.Ui.NormalizedBasePath(), config.Ui.Hostname2, config.Ui.Port2);
            return Results.NoContent();
        }));

        // СЕРТИФИКАТ, КОТОРЫЙ ПРОГРАММА ВЫПИСЫВАЕТ СЕБЕ САМА (T-315, третий проход).
        // Рецепт с openssl из документации человек делает руками, и первый же заход дал
        // сертификат УЦ вместо серверного — браузер отверг его молча. Кнопка в форме делает
        // ровно то, что нужно браузеру: свой центр (его ставят в браузер один раз) и
        // подписанный им серверный сертификат со всеми именами сервера в SAN.
        // Настройки формы этим НЕ переписываются: пути возвращаются в ответе, форма кладёт
        // их в свои поля, и человек всё так же сохраняет их сам.
        api.MapPost("/servers/local/https/selfsigned", (HttpsSelfCertDto? dto) => Handle(() =>
        {
            var config = configHolder.Config;
            var host = (dto?.Hostname ?? "").Trim();
            var host2 = (dto?.Hostname2 ?? "").Trim();
            var made = HttpsSelfCert.Create(host.Length > 0 ? host : config.Ui.Hostname,
                host2.Length > 0 ? host2 : config.Ui.Hostname2, configHolder.Path);
            return Results.Ok(new HttpsSelfCertDto
            {
                Hostname = host.Length > 0 ? host : config.Ui.Hostname,
                Hostname2 = host2,
                CertFile = made.CertFile,
                KeyFile = made.KeyFile,
                CaFile = made.CaFile,
                CaPath = made.CaPath,
                Names = made.Names,
                Info = made.Info,
            });
        }));

        // ФАЙЛ СВОЕГО УЦ НА СКАЧИВАНИЕ (T-315): его ставят в браузер и в доверенные корневые
        // соседних компьютеров. Ничего секретного в нём нет — это открытая часть пары,
        // закрытый ключ центра остаётся на сервере и наружу не отдаётся никогда.
        api.MapGet("/servers/local/https/ca", () => Handle(() =>
            HttpsSelfCert.CaPath(configHolder.Path) is { } caPath
                ? Results.File(caPath, "application/x-x509-ca-cert", HttpsSelfCert.CaCertName)
                : Results.NotFound()));

        // --- аккаунты пользователей (ТЗ п. 2.14, этапы 39–40) ---
        // Список и правка — только owner (проверяется ApiPermissions); значение пароля
        // наружу не отдаётся никогда, только признак «пароль задан». Аккаунт живёт вне
        // организации, а роль и ник — у его исполнителя В ЭТОЙ организации, поэтому они
        // подставляются здесь: в разных организациях у одного аккаунта роли разные.
        Account WithMembership(Account account)
        {
            // организации может не быть вовсе (вошедший ещё не участник) — тогда роль reader
            var executor = current.Org?.Executors.ByAccount(account.Id);
            account.ExecutorNick = executor?.Nick ?? "";
            account.Role = executor?.SystemRole ?? SystemRole.Reader;
            return account;
        }
        api.MapGet("/accounts", () => Accounts().List().Select(WithMembership).ToList());
        api.MapPost("/accounts", (AccountSaveDto dto) => Handle(() =>
        {
            var account = Accounts().Create(ToAccountInput(dto), Actor());
            // новый пользователь сразу становится участником ТЕКУЩЕЙ организации: иначе он
            // вошёл бы в систему и не увидел ничего (актор действий — исполнитель, п. 6.4.3)
            registry.AddMember(Ctx(), account, dto.Role, Actor());
            return Results.Ok(WithMembership(account));
        }));
        api.MapPut("/accounts/{id}", (string id, AccountSaveDto dto) => Handle(() =>
        {
            var account = Accounts().Update(id, ToAccountInput(dto), Actor());
            // ПАРОЛЬ АДМИНИСТРАТОРА СЕРВЕРА ИДЁТ СЛЕДОМ (T-291), пока на первом старте
            // отмечено «пароль тот же»: иначе пароли молча разъехались бы, и человек,
            // сменив свой, потерял бы доступ к настройкам собственного компьютера
            FollowAdminPassword(configHolder, secrets, account.Email, dto.Password);
            // роль правится у исполнителя организации, а не у аккаунта (ТЗ п. 2.2);
            // отдельной операцией — правка исполнителей целиком идёт только на дирижёре
            // (ТЗ гл. 6, этап 42), а пользователей заводят на любом сервере
            if (Ctx().Executors.ByAccount(id) is { } executor && executor.SystemRole != dto.Role)
            {
                Ctx().Executors.SetRole(executor.Id, dto.Role, Actor());
            }
            return Results.Ok(WithMembership(account));
        }));
        // удаление пользователя (T-140): только неактивного и только не владельца системы.
        // Признак «владелец» считается по ВСЕМ организациям сервера, а не по текущей: роль
        // живёт у исполнителя организации, и в соседней организации человек может быть owner'ом
        api.MapDelete("/accounts/{id}", (string id) => Handle(() =>
        {
            if (id == current.AccountId)
            {
                throw new ArgumentException(Loc.T("msg.apiEndpoints.10"));
            }
            Accounts().Delete(id, registry.IsOwnerAccount(id), Actor());
            // участие в организациях гаснет вместе с аккаунтом (T-146): иначе удалённый
            // остаётся в подборе исполнителей и в составе команд, а завести его заново
            // мешает его же исполнитель — на этом и ломалось подключение второго сервера
            registry.SuspendMemberships(id, Actor());
            return Results.NoContent();
        }));

        // свой аккаунт: форма по кнопке в правом верхнем углу (почту менять нельзя)
        api.MapGet("/account", () =>
            current.Account is { } me ? Results.Ok(WithMembership(me)) : Results.Unauthorized());
        api.MapPut("/account", (AccountSelfDto dto) =>
            Handle(() => Results.Ok(
                WithMembership(Accounts().UpdateSelf(AccountId(), dto.Name, dto.Phone, Actor())))));
        api.MapPut("/account/password", (PasswordChangeDto dto) =>
            Handle(() =>
            {
                Accounts().ChangePassword(AccountId(), dto.CurrentPassword, dto.NewPassword, Actor());
                // и пароль администратора сервера, если он с этим и был связан (T-291)
                FollowAdminPassword(configHolder, secrets, current.Account?.Email ?? "",
                    dto.NewPassword);
                return Results.NoContent();
            }));

        // --- проекты (п. 2.7) ---
        api.MapGet("/projects", () => Projects().List());
        api.MapGet("/projects/{id}", (string id) =>
            Projects().Get(id) is { } p ? Results.Ok(p) : Results.NotFound());
        api.MapPost("/projects", (ProjectCreateDto dto) =>
            Handle(() => Results.Ok(Projects().Create(dto.Name, dto.FolderPath, dto.DefaultTeamId, Actor(),
                dto.IsActive, dto.QualityBias, dto.ObjRefFormat, dto.DefaultResponsibleId,
                dto.TimeQuality, dto.ExperienceLimitChars, dto.RecheckLimit, dto.DefaultTaskHours))));
        api.MapPut("/projects/{id}", (string id, Project project) =>
        {
            project.Id = id;
            return Handle(() => Results.Ok(Projects().Update(project, Actor())));
        });
        // УДАЛЕНИЕ ПРОЕКТА (T-44-S0, выпуск 1.105) — второй исход формы «перенести в архив
        // или удалить совсем». Мягкое, вместе с задачами, объектами и опытом проекта:
        // тот же состав, что уносит в архив перенос вида «проект целиком»
        api.MapDelete("/projects/{id}", (string id) =>
            Handle(() =>
            {
                Projects().Delete(id, Actor());
                return Results.Ok();
            }));
        // каталоги проекта на ОСТАЛЬНЫХ серверах (ТЗ гл. 6, этап 42) — только для чтения:
        // по ним видно, где проект уже развёрнут, а правит запись только свой сервер
        api.MapGet("/projects/{id}/servers", (string id) =>
        {
            var localId = registry.Servers.LocalId();
            var orgId = Ctx().Org.Id;
            return Projects().ServerParts(id).Select(part => new ProjectServerDto
            {
                ServerId = part.ServerId,
                Code = registry.Servers.CodeOf(orgId, part.ServerId),
                Name = registry.Servers.NameOf(part.ServerId),
                IsLocal = part.ServerId == localId,
                FolderPath = part.FolderPath,
                CommonPath = part.CommonPath,
                IsActive = part.IsActive,
            }).ToList();
        });

        // --- фильтр репликации папки Common: .repignore (ТЗ гл. 6, этап 44; todo44) ---
        // Файл лежит в корне папки Common ЭТОГО сервера; синтаксис и назначение — как
        // у .gitignore. Правится кнопкой-фильтром рядом с полем «Common» в форме проекта.
        api.MapGet("/projects/{id}/repignore", (string id) => Handle(() =>
        {
            var path = RepIgnorePath(Projects().Get(id));
            return Results.Ok(new RepIgnoreDto
            {
                NotConfigured = path is null,
                Path = path ?? "",
                Text = path is not null && File.Exists(path) ? File.ReadAllText(path) : "",
            });
        }));
        api.MapPut("/projects/{id}/repignore", (string id, RepIgnoreDto dto) => Handle(() =>
        {
            var path = RepIgnorePath(Projects().Get(id))
                       ?? throw new ArgumentException(
                           Loc.T("msg.apiEndpoints.11"));
            var text = dto.Text.Replace("\r\n", "\n");
            if (text.Trim().Length == 0 && File.Exists(path))
            {
                File.Delete(path);   // пустой фильтр = фильтра нет
                return Results.Ok(new RepIgnoreDto { Path = path });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            return Results.Ok(new RepIgnoreDto { Text = text, Path = path });
        }));

        // --- ОБЪЕКТЫ ПРОЕКТА (ТЗ пп. 2.5–2.6; T-259) ---
        //
        // Персонажи, локации, реквизит, стиль, эталонные кадры и адаптеры LoRA. Список
        // ВСЕГДА по проекту: общего списка объектов у организации нет — «у каждого проекта
        // свой список объектов». Права раздела — обычный editor (правило по умолчанию
        // ApiPermissions): объект это рабочее содержимое проекта, как задача, а не
        // настройка системы.
        // kinds (T-276) — отбор по видам объекта, коды через запятую («character,image»):
        // тот же вид отбора, что тэги, только по колонке type
        api.MapGet("/objects", (string projectId, string? tags, string? search, string? kinds) =>
            Handle(() => Results.Ok(Objects().List(projectId, TaskTags.Normalize(tags), search,
                kinds: ObjectKinds.Parse(kinds)))));
        // тэги объектов — СВОЯ группа, отдельная от тэгов задач (/api/tags): «персонаж» и
        // «локация» в списке тэгов задач были бы мусором, и наоборот
        api.MapGet("/objects/tags", (string projectId) =>
            Handle(() => Results.Ok(Objects().ListTags(projectId))));
        api.MapGet("/objects/{id}", (string id) =>
            Objects().Get(id) is { } o ? Results.Ok(o) : Results.NotFound());
        // внутренний список объекта: персонаж описывается не одной картинкой, и его
        // эталонные кадры — это его дети
        api.MapGet("/objects/{id}/children", (string id) =>
            Handle(() => Results.Ok(Objects().Children(id))));
        // ЧТО УЙДЁТ В МОДЕЛЬ (T-259): готовый текст подстановки этого объекта — тот самый,
        // который встанет вместо «@obj:OBJ-3» в описании задачи. Показывается в форме, чтобы
        // паспорт проверяли ДО генерации, а не по непохожему кадру
        api.MapGet("/objects/{id}/prompt", (string id) => Handle(() =>
        {
            var item = Objects().Get(id) ?? throw new ArgumentException(Loc.T("msg.object.6"));
            return Results.Ok(new ObjectPromptDto
            {
                Marker = ObjectRefs.Marker(item.DisplayId),
                Prompt = Objects().Expand(item.ProjectId, ObjectRefs.Marker(item.DisplayId),
                    kind => ObjectKinds.Title(kind)),
                // у адаптера LoRA в модель уходит ДВОЕ разных данных (T-99-S0): паспорт при
                // использовании и кадры датасета с подписями при обучении. У остальных
                // объектов поле пустое — обучать нечего
                TrainPrompt = Objects().TrainPrompt(id),
            });
        }));
        api.MapPost("/objects", (ObjectItem item) =>
            Handle(() => Results.Ok(Objects().Create(item, Actor()))));
        api.MapPut("/objects/{id}", (string id, ObjectItem item) =>
        {
            item.Id = id;
            return Handle(() => Results.Ok(Objects().Update(item, Actor())));
        });
        // перенос объекта по иерархии — drag-n-drop представления «иерархия» (T-266),
        // точно так же, как у задач (/tasks/{id}/parent). Отдельный вызов, а не PUT всей
        // записи: перенос меняет одно поле и не должен переписывать паспорт объекта тем,
        // что лежало в давно прочитанном списке
        api.MapPost("/objects/{id}/parent", (string id, ObjectParentDto dto) =>
            Handle(() => Results.Ok(Objects().ChangeParent(id, dto.ParentId, Actor()))));
        // смена сервера-владельца объекта (T-102-S0) — то же, что у задачи: после неё объект
        // правится уже на новом сервере, а здесь становится только для чтения. Уезжает всё
        // поддерево объекта: датасеты и кадры — его содержимое
        api.MapPost("/objects/{id}/server", (string id, TaskServerDto dto) =>
            Handle(() => Results.Ok(Objects().ChangeServer(id, dto.ServerId, Actor()))));
        api.MapDelete("/objects/{id}", (string id) => Handle(() =>
        {
            Objects().Delete(id, Actor());
            return Results.NoContent();
        }));

        // --- РЕДАКТОР LoRA (T-12-S1, версия 1.95) ---
        //
        // Две вкладки формы — датасет и модели — это две разные сущности, и здесь хорошо
        // видно, почему у них разные вызовы. ДАТАСЕТ хранится обычными объектами проекта:
        // кадр — это ребёнок объекта LoRA вида «эталонный кадр» (T-259), поэтому править
        // подпись и убирать кадр умеют уже заведённые /objects/{id} и DELETE /objects/{id},
        // а своего здесь только добавление: у него есть работа, которой больше нигде нет —
        // положить файл в папку проекта и проверить его по пределам настроек.
        // МОДЕЛИ — своя таблица: обучение идёт часами и правится фоном.

        // --- ДАТАСЕТЫ ОБЪЕКТА (T-274, версия 1.98) ---
        //
        // Датасет — дочерний объект вида «датасет», кадры — его дети. Своих вызовов у него
        // четыре, и все они правят ОДНО поле: список, заведение, выбор текущего и настройки
        // контроля картинок. Через PUT /objects/{id} это делать нельзя — форма объекта
        // открыта в это же время и вернула бы прочитанное до правки.

        api.MapGet("/objects/{id}/lora/datasets", (string id) => Handle(() =>
        {
            var item = Objects().Get(id) ?? throw new ArgumentException(Loc.T("msg.object.6"));
            return Results.Ok(Objects().Datasets(id).Select(d => DatasetDto(d, item)).ToList());
        }));
        // ЗАВЕСТИ ДАТАСЕТ и сразу сделать текущим. Настройки контроля картинок нового
        // датасета — снимок ОБЩИХ настроек приложения: их и переписывают потом под модель
        api.MapPost("/objects/{id}/lora/datasets", (string id, LoraDatasetCreateDto dto) =>
            Handle(() =>
            {
                var created = Objects().CreateDataset(id, dto.Name,
                    DefaultDatasetLimits(configHolder.Config), Actor());
                var item = Objects().Get(id)!;
                return Results.Ok(DatasetDto(created, item));
            }));
        // ДАТАСЕТ ЕСТЬ ВСЕГДА: редактор LoRA зовёт это при открытии — «при вводе и
        // редактировании автоматически создавать дочерний объект типа датасет». Второй раз
        // ничего не заводит: есть текущий — вернётся он
        api.MapPost("/objects/{id}/lora/datasets/ensure", (string id) => Handle(() =>
        {
            var dataset = Objects().EnsureDataset(id, DefaultDatasetLimits(configHolder.Config),
                Actor());
            return Results.Ok(DatasetDto(dataset, Objects().Get(id)));
        }));
        api.MapPut("/objects/{id}/lora/dataset/current", (string id, LoraDatasetPickDto dto) =>
            Handle(() => Results.Ok(Objects().SetCurrentDataset(id, dto.DatasetId, Actor()))));
        api.MapPut("/objects/lora/datasets/{datasetId}/limits",
            (string datasetId, LoraDatasetLimitsDto dto) => Handle(() =>
            {
                var saved = Objects().SetDatasetLimits(datasetId, dto.ToLimits(), Actor());
                var owner = saved.ParentId is { Length: > 0 } p ? Objects().Get(p) : null;
                return Results.Ok(DatasetDto(saved, owner));
            }));
        // ЧТО МОЖНО ПОДСТАВИТЬ ИЗ МОДЕЛИ: модели, уже стоящие на закладке «Модели» этого
        // объекта, у которых в справочнике заполнены контрольные настройки картинок.
        // Модель, ничего не сказавшая о кадрах, в списке не нужна — подставлять из неё нечего
        api.MapGet("/objects/{id}/lora/model-limits", (string id) => Handle(() =>
        {
            var rows = new List<LoraModelLimitsDto>();
            foreach (var row in Objects().LoraModels(id))
            {
                if (Models().Get(row.ModelId) is not { } model)
                {
                    continue;
                }
                Models().ReadProfileSettings(model);
                var dataset = model.Lora.Train.Dataset;
                if (!model.Lora.Supported || !dataset.HasLimits)
                {
                    continue;
                }
                rows.Add(new LoraModelLimitsDto
                {
                    ModelId = model.Id,
                    ModelDisplayId = model.DisplayId,
                    ModelName = model.Name,
                    Limits = LoraDatasetLimitsDto.Of(
                        LoraDatasetLimits.FromModel(dataset, DefaultDatasetLimits(configHolder.Config))),
                    Formats = dataset.Formats,
                });
            }
            return Results.Ok(rows);
        }));

        // кадры датасета: дети ДАТАСЕТА (до версии 1.98 — прямые дети объекта, и у объекта
        // без датасетов так и остаётся). Отдельный вызов, а не /objects/{id}/children,
        // потому что форме нужны только картинки и в порядке добавления
        api.MapGet("/objects/{id}/lora/dataset", (string id, string? datasetId) =>
            Handle(() => Results.Ok(Objects().DatasetFrames(id, datasetId))));

        // ДОБАВИТЬ КАДР. Картинка приезжает уже обрезанной и пережатой браузером (рамку
        // кропа тянут там же, и второй раз декодировать её на сервере незачем), но пределы
        // настроек сервер проверяет САМ: размер в килобайтах — по байтам, размер в пикселях —
        // по заголовку файла. Проверка на стороне, которая присылает данные, — не проверка.
        api.MapPost("/objects/{id}/lora/dataset", (string id, LoraDatasetAddDto dto) => Handle(() =>
        {
            var item = Objects().Get(id) ?? throw new ArgumentException(Loc.T("msg.object.6"));
            var project = Projects().Get(item.ProjectId ?? "");
            // КУДА кладём кадр: в названный датасет, а не названо — в текущий; нет ни одного —
            // датасет заводится САМ, умолчанием (T-274). Отдельного шага «сначала заведите
            // датасет» здесь быть не должно: человек нажал «добавить кадр», а не «настроить»
            var dataset = dto.DatasetId is { Length: > 0 } picked
                ? Objects().Get(picked) ?? throw new ArgumentException(Loc.T("msg.lora.29"))
                : Objects().EnsureDataset(id, DefaultDatasetLimits(configHolder.Config), Actor());
            if (dataset.ParentId != item.Id || dataset.Type != ObjectKinds.Dataset)
            {
                throw new ArgumentException(Loc.T("msg.lora.29"));
            }
            // пределы — СВОИ У ДАТАСЕТА: общие настройки приложения были его снимком в день
            // заведения, а требования моделей разные
            var setLimits = LoraDatasetLimits.Parse(dataset.DatasetJson);
            var limits = LoraLimitsDto(setLimits);
            // ЗВУКОВОЙ ДАТАСЕТ (T-250-S0) идёт мимо пережатия картинок целиком: запись
            // браузер не перекодирует (холст умеет только картинки), поэтому файл берётся
            // ИЗ ИСХОДНИКА в хранилище как есть — и ни вес в килобайтах, ни размер в точках
            // к нему неприменимы. Проверка у него своя: расширение из объявленных
            var audio = setLimits.IsAudio;
            byte[] bytes;
            if (audio)
            {
                if (dto.StagePath is not { Length: > 0 } src || !Files().IsInside(src)
                    || !File.Exists(Files().Abs(src)))
                {
                    throw new ArgumentException(Loc.T("msg.lora.21", dto.StagePath));
                }
                if (!ProjectFiles.IsAudio(dto.FileName) && !ProjectFiles.IsAudio(src))
                {
                    throw new ArgumentException(Loc.T("msg.lora.47", dto.FileName));
                }
                bytes = File.ReadAllBytes(Files().Abs(src));
            }
            else
            {
                bytes = Convert.FromBase64String(dto.DataBase64);
                if (bytes.Length > limits.MaxKb * 1024L)
                {
                    throw new ArgumentException(Loc.T("msg.lora.18", bytes.Length / 1024, limits.MaxKb));
                }
                if (ImageProbe.Size(bytes) is { } size
                    && (size.Width > limits.MaxWidth || size.Height > limits.MaxHeight))
                {
                    throw new ArgumentException(Loc.T("msg.lora.19", size.Width, size.Height,
                        limits.MaxWidth, limits.MaxHeight));
                }
            }
            // КАДРЫ ЛЕЖАТ В КАТАЛОГЕ ДАННЫХ ОРГАНИЗАЦИИ (T-98-S0), а не в папке проекта:
            // папка проекта у каждого сервера своя и не реплицируется, и датасет, собранный
            // здесь, на соседнем сервере был бы пустым. Каталог данных реплицируется целиком,
            // поэтому кадр уезжает партнёру сам — вместе со строкой объекта.
            // У каждого датасета свой подкаталог: датасеты собирают под разные модели, и
            // одноимённые кадры двух датасетов затирали бы друг друга
            // расширение ставит СЕРВЕР: у картинки — по формату настроек (её и пережали
            // в него), у записи — родное расширение исходника (перекодировать её некому)
            var ext = audio
                ? (Path.GetExtension(dto.FileName) is { Length: > 1 } own
                    ? own
                    : Path.GetExtension(dto.StagePath) is { Length: > 1 } from ? from : ".wav")
                : limits.Format == "jpeg" ? ".jpg" : ".png";
            var name = SafeFileName(dto.FileName, ext);
            var rel = ObjectFiles.DatasetDirRel(project?.Slug ?? "_no_project", item.DisplayId,
                dataset.DisplayId) + "/" + name;
            var abs = Files().Abs(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllBytes(abs, bytes);
            // название кадра обязано быть уникальным в проекте (правило объектов), поэтому
            // в него входит код датасета: «001.png» у второго датасета уже занято
            var frame = Objects().Create(new ObjectItem
            {
                ProjectId = item.ProjectId,
                ParentId = dataset.Id,
                Type = audio ? ObjectKinds.Audio : ObjectKinds.Image,
                Name = $"{dataset.DisplayId} {Path.GetFileNameWithoutExtension(name)}",
                PathOrUrl = ObjectFiles.Store(rel),
                Description = dto.Description,
            }, Actor());
            // исходник в хранилище больше не нужен — кадр уже лежит в папке проекта
            if (dto.StagePath is { Length: > 0 } stage && Files().IsInside(stage))
            {
                TryDelete(Files().Abs(stage));
            }
            return Results.Ok(frame);
        }));

        // ИСХОДНИК КАДРА: положить в хранилище то, из чего человек будет резать. Нужен
        // потому, что рамку кропа тянут в браузере, а исходник лежит либо на чужом сервере
        // (браузеру не дадут прочитать его в canvas — чужое происхождение), либо на диске
        // этого компьютера (браузер туда не ходит вовсе).
        api.MapPost("/objects/lora/stage", async (LoraStageDto dto) => await HandleAsync(async () =>
        {
            var url = dto.Url.Trim();
            var local = dto.LocalPath.Trim();
            byte[] bytes;
            string name;
            if (url.Length > 0)
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(Loc.T("msg.lora.20", url));
                }
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                bytes = await http.GetByteArrayAsync(url);
                name = Path.GetFileName(new Uri(url).AbsolutePath);
            }
            else
            {
                var path = AI2P.Core.PathHome.Expand(local);
                if (path.Length == 0 || !File.Exists(path))
                {
                    throw new ArgumentException(Loc.T("msg.lora.21", local));
                }
                // берём только КАРТИНКИ (а у звукового датасета — только ЗАПИСИ), и это не
                // косметика: путь приходит из формы, а прочитанный файл ложится в хранилище
                // и отдаётся наружу ссылкой — без этой проверки одной строкой запроса
                // выносился бы любой файл диска
                if (LoraDatasetMedia.Normalize(dto.Media) == LoraDatasetMedia.Audio)
                {
                    if (!ProjectFiles.IsAudio(path))
                    {
                        throw new ArgumentException(Loc.T("msg.lora.47", Path.GetFileName(path)));
                    }
                }
                else if (!ProjectFiles.IsImage(path))
                {
                    throw new ArgumentException(Loc.T("msg.lora.22", Path.GetFileName(path)));
                }
                bytes = await File.ReadAllBytesAsync(path);
                name = Path.GetFileName(path);
            }
            if (name.Trim().Length == 0)
            {
                name = LoraDatasetMedia.Normalize(dto.Media) == LoraDatasetMedia.Audio
                    ? "audio.wav"
                    : "image.png";
            }
            var rel = Files().SaveUpload("_lora", "stage-" + name, bytes);
            return Results.Ok(new LoraStageResultDto { Path = rel, Name = name, Size = bytes.Length });
        }));

        // строки обучения объекта: под какие модели адаптер уже обучали и чем это кончилось
        api.MapGet("/objects/{id}/lora/models", (string id) =>
            Handle(() => Results.Ok(Objects().LoraModels(id))));
        api.MapPost("/objects/{id}/lora/models", (string id, LoraModelAddDto dto) =>
            Handle(() => Results.Ok(Objects().AddLoraModel(id, dto.ModelId, Actor()))));
        api.MapDelete("/objects/lora/models/{loraId}", (string loraId) => Handle(() =>
        {
            Objects().DeleteLoraModel(loraId, Actor());
            return Results.NoContent();
        }));
        // ЗАПУСК ОБУЧЕНИЯ. Отвечает СРАЗУ строкой в состоянии «обучается»: обучение идёт
        // часами, держать на нём запрос браузера нельзя. Уже обученную модель без явного
        // «да» переспроса не трогаем — переобучение затирает готовый файл адаптера
        // ЧТО СПРОСИТЬ ПЕРЕД ЗАПУСКОМ (T-274): сменился ли датасет с прошлого обучения и не
        // крупнее ли его настройки того, что объявила модель. Форма спрашивает по этому
        // ответу, а сервер те же условия проверяет сам при запуске — пропустить вопрос,
        // дёрнув запуск напрямую, нельзя
        api.MapGet("/objects/lora/models/{loraId}/train/check", (string loraId, string? datasetId) =>
            Handle(() => Results.Ok(Ctx().LoraTrain.Check(loraId, datasetId))));
        // ОБУЧЕНИЕ ИДЁТ ЗАДАЧЕЙ (T-157-S0), а не фоном сервиса: у задачи есть консоль, лог,
        // кнопка остановки, планирование, блокировки и место в иерархии. Прежнего прямого
        // запуска (POST …/train) здесь больше НЕТ намеренно — два пути к одной работе
        // разъезжаются молча. Четыре шага кнопки «Обучить» после проверки датасета:
        //   options   — что вообще можно предложить (шаблоны, исполнитель, плагин);
        //   executor  — завести исполнителя «авто ПО» из записи «Плагинов и MCP»;
        //   template  — завести узел шаблона с этим исполнителем;
        //   task      — создать задачу по шаблону и (по ответу) запустить её
        api.MapGet("/objects/lora/models/{loraId}/train/options", (string loraId) =>
            Handle(() => Results.Ok(Ctx().LoraTrainTasks.Options(loraId))));
        api.MapPost("/objects/lora/models/{loraId}/train/executor", (string loraId) =>
            Handle(() => Results.Ok(Ctx().LoraTrainTasks.CreateExecutor(loraId, Actor()))));
        api.MapPost("/objects/lora/models/{loraId}/train/template",
            (string loraId, string executorId) => Handle(() =>
                Results.Ok(Ctx().LoraTrainTasks.CreateTemplate(loraId, executorId, Actor()))));
        api.MapPost("/objects/lora/models/{loraId}/train/task",
            async (string loraId, LoraTrainTaskCreateDto dto) => await HandleAsync(async () =>
                Results.Ok(await Ctx().LoraTrainTasks.CreateTaskAsync(loraId, dto.TemplateId,
                    dto, Actor()))));
        api.MapPost("/objects/lora/models/{loraId}/train/cancel", (string loraId) => Handle(() =>
        {
            Ctx().LoraTrain.Cancel(loraId);
            return Results.NoContent();
        }));
        // ДАТАСЕТ ПОД ТРЕБОВАНИЯ МОДЕЛИ (T-57-S0) — ответ на «Создать/переключить на нужный
        // датасет» из переспроса о несовпадении настроек. Подходящий среди заведённых
        // становится текущим; не нашлось — заводится новый, и форме возвращается список
        // кадров, которые ей предстоит пережать холстом браузера (сервер картинки не умеет)
        api.MapPost("/objects/lora/models/{loraId}/dataset/fit",
            (string loraId, LoraDatasetPickDto dto) => Handle(() =>
            {
                var fit = Ctx().LoraTrain.FitDataset(loraId, dto.DatasetId, Actor());
                fit.PadColor = PadColor(configHolder.Config);
                return Results.Ok(fit);
            }));

        // --- команды (п. 2.8) ---
        api.MapGet("/teams", () => Teams().List());
        api.MapGet("/teams/{id}", (string id) =>
            Teams().Get(id) is { } t ? Results.Ok(t) : Results.NotFound());
        api.MapPost("/teams", (Team team) => Handle(() => Results.Ok(Teams().Create(team, Actor()))));
        api.MapPut("/teams/{id}", (string id, Team team) =>
        {
            team.Id = id;
            return Handle(() => Results.Ok(Teams().Update(team, Actor())));
        });

        // --- запуск/остановка работы команды и статусы работы участников (ТЗ v1.14) ---
        api.MapPost("/teams/{id}/work/start", (string id) =>
            Handle(() => Results.Ok(TeamWork().Start(id, Actor(), Actor()))));
        api.MapPost("/teams/{id}/work/stop", (string id) =>
            Handle(() => Results.Ok(TeamWork().Stop(id, Actor(), Actor()))));
        // запуск/останов ОДНОГО участника (T-129): участника добавили в уже работающую команду
        // или он упал по ошибке — перезапускать всю команду ради него незачем
        api.MapPost("/teams/{id}/work/start/{executorId}", (string id, string executorId) =>
            Handle(() => Results.Ok(TeamWork().StartMember(id, executorId, Actor(), Actor()))));
        api.MapPost("/teams/{id}/work/stop/{executorId}", (string id, string executorId) =>
            Handle(() => Results.Ok(TeamWork().StopMember(id, executorId, Actor(), Actor()))));
        api.MapGet("/teams/{id}/work", (string id) =>
            Handle(() => Results.Ok(TeamWork().Status(id, Actor()))));

        // --- исполнители (п. 2.2) ---
        api.MapGet("/executors", () => Executors().List());
        api.MapGet("/executors/{id}", (string id) =>
            Executors().Get(id) is { } e ? Results.Ok(e) : Results.NotFound());
        api.MapPost("/executors", (Executor executor) =>
            Handle(() => Results.Ok(Executors().Create(executor, Actor()))));
        api.MapPut("/executors/{id}", (string id, Executor executor) =>
        {
            executor.Id = id;
            return Handle(() => Results.Ok(Executors().Update(executor, Actor())));
        });

        // автоподбор исполнителя под skills (ТЗ v1.26, todo28): кнопки формы задачи;
        // skills — id через запятую; mode: ai_first (по умолчанию) / human_first / ai_only
        api.MapGet("/executors/pick", (string? projectId, string? teamId, string? skills, string? mode) =>
            Handle(() =>
            {
                var skillIds = (skills ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                                        | StringSplitOptions.TrimEntries);
                var pickMode = mode switch
                {
                    "human_first" => PickMode.HumanFirst,
                    "ai_only" => PickMode.AiOnly,
                    _ => PickMode.AiFirst,
                };
                return Results.Ok(Picker().Pick(projectId, teamId, skillIds, pickMode));
            }));

        // --- справочник ИИ-моделей (п. 2.9) — часть настройки системы ---
        // список для UI (ТЗ v1.42): вместе с навыками из деклараций — колонка «навыки»
        api.MapGet("/models", () => Models().ListWithSkills());
        // установка локальных моделей в репозиторий моделей (ТЗ v1.40, todo36_3):
        // статус по манифесту install профайла, запуск/отмена фоновой загрузки с докачкой
        api.MapGet("/models/installs", () => ModelInstalls().StatusAll());
        api.MapGet("/models/{id}/install", (string id) =>
            Handle(() => Results.Ok(ModelInstalls().Status(id))));
        api.MapPost("/models/{id}/install/start", (string id) =>
            Handle(() => Results.Ok(ModelInstalls().Start(id))));
        api.MapPost("/models/{id}/install/cancel", (string id) =>
            Handle(() => Results.Ok(ModelInstalls().Cancel(id))));
        // ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ (T-190-S0): флажок «Обучение LoRA» в окне установки.
        // Выбор пер-серверный (в кластере адаптеры обучают на одной машине) и лежит в
        // config.json этого компьютера, поэтому и правится он здесь, а не в базе организации
        api.MapPost("/models/{id}/install/option", (string id, string code, bool on) =>
            Handle(() => Results.Ok(ModelInstalls().SetOption(id, code, on))));
        // размеры дистрибутивов пакетов (ТЗ v1.43, todo36_6): запрос к серверам раздачи,
        // результат кэшируется — окно установки показывает размер пакета и ход загрузки
        api.MapPost("/models/{id}/install/resolve", async (string id) =>
        {
            try
            {
                return Results.Ok(await ModelInstalls().ResolveSizesAsync(id));
            }
            catch (Exception ex) when (ex is ArgumentException
                or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // ВХОД В CLAUDE CLI (todo96, версия 1.96). Сеанс CLI протухает, и до 1.96 войти
        // заново можно было только руками в терминале того компьютера, где работает сервер:
        // AI2P об этом даже не знал и показывал очередную ошибку задания. Теперь состояние
        // входа видно в настройках, а сам вход делается отсюда: «начать» отдаёт ссылку
        // авторизации, «код» дописывает CLI код со страницы входа. Успешный вход отпускает
        // задачи, вставшие в паузу «ждёт входа»
        api.MapGet("/models/claude-cli/auth", async () =>
        {
            var status = await ClaudeLogin().StatusAsync(CliCommand());
            return Results.Ok(new ClaudeAuthStatusDto
            {
                Known = status.Known,
                LoggedIn = status.LoggedIn,
                Email = status.Email,
                Method = status.Method,
                Subscription = status.Subscription,
                Command = ClaudeLoginService.Executable(CliCommand()),
                LoginRunning = ClaudeLogin().Running,
                WaitingTasks = current.Org is null ? 0 : Orchestrator().WaitingForAuthCount(),
            });
        });
        api.MapPost("/models/claude-cli/auth/login", async () =>
            Results.Ok(await ClaudeLogin().StartAsync(CliCommand())));
        api.MapPost("/models/claude-cli/auth/code", async (ClaudeLoginCodeDto dto) =>
        {
            var state = await ClaudeLogin().SubmitCodeAsync(dto.Code);
            if (state.LoggedIn && current.Org is not null)
            {
                state.ReleasedTasks = Orchestrator().ReleaseWaitingForAuth();
            }
            return Results.Ok(state);
        });
        api.MapPost("/models/claude-cli/auth/cancel", () =>
        {
            ClaudeLogin().Cancel();
            return Results.Ok(new ClaudeLoginStateDto());
        });
        // ключи API облачных моделей (ТЗ v1.42, todo36_5): само значение наружу не отдаётся —
        // только признак «ключ есть»; установка ключа активирует модель
        api.MapGet("/models/{id}/key", (string id) =>
            Handle(() => Results.Ok(ModelKeys().Status(id))));
        api.MapPut("/models/{id}/key", (string id, ModelKeySaveDto dto) =>
            Handle(() => Results.Ok(ModelKeys().SetKey(id, dto.Value, Actor()))));
        // документ модели из поставляемой документации (ТЗ гл. 14, todo47): кнопка «i»
        // в форме модели. Файл ищется по НАЗВАНИЮ модели на языке интерфейса с откатом
        api.MapGet("/models/{id}/doc", (string id) => Handle(() =>
        {
            var model = Models().Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.12"));
            return Results.Ok(services.GetRequiredService<DocStore>().ModelDoc(model.Name));
        }));
        api.MapGet("/models/{id}", (string id) =>
            Models().Get(id) is { } m ? Results.Ok(m) : Results.NotFound());
        // ЛОКАЛЬНАЯ модель настраивается на КАЖДОМ сервере отдельно (T-8-S1), и правит её
        // настройку администратор ЭТОГО сервера (ТЗ гл. 6, гл. 12; этап 42): она описывает
        // файлы, пути и команды запуска конкретного компьютера, а менять их вправе только его
        // физический хозяин. Проверка «чья это запись» — внутри AiModelService.Update: общая
        // часть (название) остаётся у владельца, пер-серверная правится здесь
        void EnsureModelWriteAllowed(AiModel model)
        {
            if (model.IsLocal && !current.IsServerAdmin)
            {
                throw new ArgumentException(
                    Loc.T("msg.apiEndpoints.13"));
            }
        }
        api.MapPost("/models", (AiModel model) =>
            Handle(() =>
            {
                EnsureModelWriteAllowed(model);
                return Results.Ok(Models().Create(model, Actor()));
            }));
        api.MapPut("/models/{id}", (string id, AiModel model) =>
        {
            model.Id = id;
            return Handle(() =>
            {
                // «локальная» — производное от профайла: проверяем и то, что записано в БД
                EnsureModelWriteAllowed(model);
                if (Models().Get(id) is { IsLocal: true } stored)
                {
                    EnsureModelWriteAllowed(stored);
                }
                return Results.Ok(Models().Update(model, Actor()));
            });
        });
        // удаление кастомной записи справочника (ТЗ v1.43, todo36_6): удаляется только
        // запись — скачанные файлы модели и пакеты остаются на диске
        api.MapDelete("/models/{id}", (string id) =>
            Handle(() =>
            {
                Models().Delete(id, Actor());
                return Results.NoContent();
            }));

        // --- справочники: проф. роли (п. 2.8), skills и io formats (пп. 7.3, v1.17).
        // Названия встроенных записей хранятся по языкам (T-191): язык текстов — параметр
        // lang, по умолчанию язык приложения, как у справочников действий и состояний ---
        api.MapGet("/roles", (string? lang) => RefData().Roles(lang ?? configHolder.Config.Language));
        api.MapPost("/roles", (Role role, string? lang) =>
            Handle(() => Results.Ok(RefData().AddRole(role.Name, lang ?? configHolder.Config.Language))));
        api.MapGet("/skills", (string? lang) => RefData().Skills(lang ?? configHolder.Config.Language));
        api.MapPost("/skills", (Skill skill, string? lang) =>
            Handle(() => Results.Ok(RefData().AddSkill(skill.Name, skill.Description,
                lang: lang ?? configHolder.Config.Language))));
        api.MapGet("/ioformats", (string? lang) => RefData().IoFormats(lang ?? configHolder.Config.Language));
        api.MapPost("/ioformats", (IoFormat format, string? lang) =>
            Handle(() => Results.Ok(RefData().AddIoFormat(format.Name, format.Description,
                lang: lang ?? configHolder.Config.Language))));

        // --- справочник действий агентов (ТЗ v1.21, todo24): промпты инструментов в настройке;
        // язык текстов — параметр lang, по умолчанию язык приложения ---
        api.MapGet("/actions", (string? lang) =>
            Actions().List(lang ?? configHolder.Config.Language));
        api.MapPost("/actions", (ActionSaveDto dto, string? lang) =>
            Handle(() => Results.Ok(Actions().Create(dto, lang ?? configHolder.Config.Language))));
        api.MapPut("/actions/{id}", (string id, ActionSaveDto dto, string? lang) =>
            Handle(() => Results.Ok(Actions().Update(id, dto, lang ?? configHolder.Config.Language))));
        api.MapPost("/actions/{id}/reset-prompt", (string id, string? lang) =>
            Handle(() => Results.Ok(Actions().ResetPrompt(id, lang ?? configHolder.Config.Language))));

        // --- справочник состояний задач (ТЗ v1.37, todo34_3): названия — в языке запроса ---
        api.MapGet("/task-statuses", (string? lang, bool? includeInactive) =>
            Statuses().List(lang ?? configHolder.Config.Language, includeInactive ?? false));
        api.MapPost("/task-statuses", (TaskStatusSaveDto dto, string? lang) =>
            Handle(() => Results.Ok(Statuses().Create(ToStatusDef(dto), lang ?? configHolder.Config.Language))));
        api.MapPut("/task-statuses/{id}", (string id, TaskStatusSaveDto dto, string? lang) =>
            Handle(() => Results.Ok(Statuses().Update(id, ToStatusDef(dto), lang ?? configHolder.Config.Language))));

        // --- правила безопасности (ТЗ гл. 12, todo25): глобальные / проекта / задачи ---
        api.MapGet("/security-rules", (string scope, string? scopeId) =>
            Handle(() => Results.Ok(SecurityRules().List(scope, scopeId))));
        api.MapPost("/security-rules", (SecurityRule rule) =>
            Handle(() => Results.Ok(SecurityRules().Create(rule, Actor()))));
        api.MapPut("/security-rules/{id}", (string id, SecurityRule rule) =>
        {
            rule.Id = id;
            return Handle(() => Results.Ok(SecurityRules().Update(rule, Actor())));
        });
        api.MapDelete("/security-rules/{id}", (string id) =>
            Handle(() =>
            {
                SecurityRules().Delete(id, Actor());
                return Results.NoContent();
            }));

        // --- задачи (пп. 2.1, 2.3): фильтр и полнотекстный поиск (гл. 5, экран 1) ---
        // servers — фильтр по серверам-владельцам (ТЗ гл. 6, этап 42): id через запятую,
        // значение «none» — задачи без сервера (их ведёт дирижёр); пусто — все серверы
        api.MapGet("/tasks", (string? projectId, bool? includeTemplates, bool? templatesOnly,
                string? teamId, string? executorId, string? mineId, string? status,
                DateTime? dueFrom, DateTime? dueTo, string? search,
                int? priorityMin, int? priorityMax, string? servers, string? tags) =>
            Handle(() => Results.Ok(Tasks().List(projectId, includeTemplates ?? false,
                templatesOnly ?? false, teamId, executorId, mineId,
                status,
                dueFrom, dueTo, search, priorityMin, priorityMax,
                (servers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                           | StringSplitOptions.TrimEntries),
                // тэги (T-222): запятая — разделитель, внутри тэга её не бывает
                // (TaskTags.Normalize), поэтому список едет одной строкой
                TaskTags.Normalize(tags)))));
        // все тэги организации (T-222): справочника у тэгов нет — это DISTINCT по task_tags.
        // Список нужен полю формы задачи, фильтру списков и веткам «тэги» эксплорера
        api.MapGet("/tags", () => Handle(() => Results.Ok(Tasks().ListTags())));
        // «в работе у ИИ» (T-187): всё, чем агенты заняты прямо сейчас, — все проекты и все
        // серверы организации. Фильтра у представления нет: смысл в одном общем взгляде
        api.MapGet("/tasks/ai-work", () => Handle(() => Results.Ok(Tasks().AiWork())));
        // диаграмма подзадач (T-132-S0): поддерево задачи по дорожкам исполнителей и по
        // времени — считается целиком на сервере (задания, команда, порядок очереди)
        // alone=true — корень рисуется и без подзадач (T-353-S0): так его просят «Диаграммы
        // в работе», где запущенная одиночная задача давала пустое представление
        api.MapGet("/tasks/{id}/diagram", (string id, bool alone = false) =>
            Handle(() => Results.Ok(Tasks().Diagram(id, alone))));
        api.MapGet("/tasks/{id}", (string id) =>
        {
            var task = Tasks().Get(id);
            if (task is null)
            {
                return Results.NotFound();
            }
            var jobs = Jobs().ListByTask(id);
            // родитель (T-130): код и заголовок нужны кнопке «к родителю» на карточке
            var parent = task.ParentId is null ? null : Tasks().Get(task.ParentId);
            // приостановленное задание (T-185): по нему видно, чего именно ждёт агент
            var waitingJob = jobs.FirstOrDefault(j => j.State == JobState.WaitingHuman);
            var startAfter = TaskService.LaunchStartAfter(task.LaunchJson);
            // открытая очередь иерархического запуска (T-224): по задаче нажата кнопка
            // «запустить всю иерархию», работа идёт по её подзадачам, а сама задача ждёт
            // своей очереди и остаётся в «ожидает». Пока агент занят задачей сам (задание
            // в очереди или выполняется), причина ожидания — задание, а не очередь
            // сданной задаче (проверка/готово/отмена) пометка не пишется вовсе (T-2-S0):
            // очередь под таким корнем закрывается сама, но пока проход не состоялся —
            // а на чужом сервере он не состоится и вовсе — за статусом «проверка» стояло бы
            // «пауза: запуск иерархии»
            var hierarchyRun = TaskService.HasLaunchFlag(task.LaunchJson, TaskService.HierarchyFlag)
                               && !TaskStatuses.Settled(task.Status)
                               && !jobs.Any(j => j.State is JobState.Queued or JobState.Running);
            // блокирующие задачи (T-6-S1): считаются только у задачи, работа над которой ещё
            // не начата, — иначе у выполняющейся задачи за статусом писалось бы «ждёт
            // блокирующие», хотя ждать ей уже нечего
            var blockers = task.Status is TaskStatuses.Draft or TaskStatuses.Pending
                           && !jobs.Any(j => j.State is JobState.Queued or JobState.Running)
                ? Tasks().BlockersStateOf(task)
                : BlockersState.Done;
            // корень ОТКРЫТОЙ иерархии над задачей (T-263): по нему кнопка «остановить»
            // понимает, что снять одно задание мало — очередь запустит задачу снова.
            // Считается по всей цепочке предков, а не только по самой задаче: пометка
            // стоит у корня, а нажимают «остановить» на работающей подзадаче
            var hierarchyRoot = task.IsTemplate ? null : Orchestrator().HierarchyRootOf(task);
            // выключенный автозапуск ветки (T-54-S0): пометка стоит у задачи или у её предка —
            // по ней карточка показывает «включить автозапуск» вместо «выключить»
            var autoStartOff = task.IsTemplate ? null : Orchestrator().AutoStartOffRootOf(task);
            return Results.Ok(new TaskDetailsDto
            {
                Task = task,
                Description = Tasks().ReadDescription(task),
                Acceptance = Tasks().ReadAcceptance(task),
                Artifacts = Tasks().Artifacts(task),
                Jobs = jobs,
                // зависшее running-задание (T-117): по нему UI показывает кнопку «продолжить»
                StalledJobId = jobs.FirstOrDefault(j => Orchestrator().IsStalled(j))?.Id,
                // отложенный старт (T-121): задача ждёт сброса лимита исполнителя
                StartAfter = startAfter,
                // чего ждёт агент (T-187): тот же расчёт, что в представлении «в работе у ИИ», —
                // за статусом задачи пишется одно и то же и в форме, и в списке
                Pause = AiPause.Of(waitingJob is not null, waitingJob?.WaitKind ?? "",
                    startAfter, task.PendingQuestions, DateTime.UtcNow, hierarchyRun, blockers,
                    loopBody: task.Status == TaskStatuses.Paused && TaskFlowRun.InLoopBody(task.LaunchJson)),
                ParentDisplayId = parent?.DisplayId,
                ParentTitle = parent?.Title,
                HierarchyRootId = hierarchyRoot?.Id,
                HierarchyRootDisplayId = hierarchyRoot?.DisplayId,
                AutoStartOff = TaskService.HasLaunchFlag(task.LaunchJson, TaskService.NoAutoStartFlag),
                AutoStartOffRoot = autoStartOff?.DisplayId,
                // куда карточка кладёт РУЧНОЙ результат (T-130-S0): файла может ещё не быть,
                // и у задачи с пустым результатом это единственный способ его завести
                ManualResultPath = task.IsTemplate ? "" : Tasks().ManualResultPath(task),
            });
        });
        // текст описания задачи — для ленивых MD-превью на доске (ТЗ v1.17, todo20)
        api.MapGet("/tasks/{id}/description", (string id) =>
        {
            var task = Tasks().Get(id);
            return task is null ? Results.NotFound() : Results.Text(Tasks().ReadDescription(task));
        });
        api.MapPost("/tasks", (TaskSaveDto dto) =>
            Handle(() => Results.Ok(Tasks().Create(dto.Task, dto.Description, dto.Acceptance, Actor()))));
        api.MapPut("/tasks/{id}", (string id, TaskSaveDto dto) =>
        {
            dto.Task.Id = id;
            return Handle(() => Results.Ok(Tasks().Update(dto.Task, dto.Description, dto.Acceptance, Actor())));
        });
        api.MapPost("/tasks/{id}/status", (string id, StatusChangeDto dto) =>
            Handle(() =>
            {
                // код состояния должен существовать в справочнике состояний (ТЗ v1.37)
                if (!Statuses().Exists(dto.Status))
                {
                    throw new ArgumentException(Loc.T("msg.apiEndpoints.14", dto.Status));
                }
                return Results.Ok(Tasks().ChangeStatus(id, dto.Status, Actor()));
            }));
        // перенос задачи по иерархии — drag-n-drop представления «иерархия» (todo37)
        api.MapPost("/tasks/{id}/parent", (string id, TaskParentDto dto) =>
            Handle(() => Results.Ok(Tasks().ChangeParent(id, dto.ParentId, Actor()))));
        // смена исполнителя перетаскиванием на дорожку диаграммы подзадач (T-135-S0):
        // меняется одно поле, поэтому вызов свой, а не сохранение всей записи задачи
        api.MapPost("/tasks/{id}/executor", (string id, TaskExecutorDto dto) =>
            Handle(() => Results.Ok(Tasks().ChangeExecutor(id, dto.ExecutorId, Actor()))));
        // смена сервера-владельца задачи (кнопка «сменить сервер», ТЗ гл. 6, этап 42):
        // после неё задача правится уже на новом сервере, а здесь становится только для чтения
        api.MapPost("/tasks/{id}/server", (string id, TaskServerDto dto) =>
            Handle(() => Results.Ok(Tasks().ChangeServer(id, dto.ServerId, Actor()))));
        api.MapDelete("/tasks/{id}", (string id) =>
            Handle(() =>
            {
                Tasks().Delete(id, Actor());
                return Results.NoContent();
            }));
        // --- опыт по узлам шаблонов (ТЗ п. 2.11, todo32): вкладка «Опыт» карточки шаблона ---
        string NickOf(string? executorId) =>
            executorId is null ? "" : Executors().Get(executorId)?.Nick ?? executorId;
        ExperienceRecordDto ExpDto(Core.Entities.ExperienceRecord r) => new()
        {
            Id = r.Id,
            TemplateTaskId = r.TemplateTaskId,
            TemplateDisplayId = r.TemplateTaskId.Length == 0
                ? ""
                : Tasks().Get(r.TemplateTaskId)?.DisplayId ?? "",
            ProjectId = r.ProjectId ?? "",
            SkillId = r.SkillId ?? "",
            SkillName = r.SkillName,
            Text = r.Text,
            // «загружать всегда» и тэги записи (T-24-S0), активность (T-265-S0)
            AlwaysLoad = r.AlwaysLoad,
            IsActive = r.IsActive,
            Tags = r.Tags,
            CreatedBy = NickOf(r.CreatedBy),
            CreatedAt = r.CreatedAt,
            UpdatedBy = NickOf(r.UpdatedBy),
            UpdatedAt = r.UpdatedAt,
        };
        // опыт узла шаблона вместе с поддеревом (вкладка показывает и опыт подузлов)
        api.MapGet("/tasks/{id}/experience", (string id) =>
            Handle(() => Results.Ok(
                Experience().ListByTemplate(id, includeSubtree: true).Select(ExpDto).ToList())));
        api.MapPost("/tasks/{id}/experience", (string id, ExperienceSaveDto dto) =>
            Handle(() => Results.Ok(ExpDto(Experience().Create(id, dto.Text, Actor(), dto.SkillId,
                dto.AlwaysLoad, dto.Tags)))));
        // все тэги записей опыта организации ВМЕСТЕ С ТЭГАМИ ЗАДАЧ (T-24-S0): у опыта и
        // у задач набор тэгов намеренно один — по нему они и связываются. Список нужен
        // полю формы записи и фильтру списка опыта
        api.MapGet("/experience/tags", () => Handle(() => Results.Ok(
            Experience().ListTags().Concat(Tasks().ListTags())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList())));
        // --- опыт ПРОЕКТА (ТЗ п. 2.11, todo48): вкладка «Опыт» карточки проекта ---
        // фильтр по навыкам — множественный выбор в UI, коды через запятую; пусто — все записи
        api.MapGet("/projects/{id}/experience", (string id, string? skills) =>
            Handle(() => Results.Ok(Experience()
                .ListByProject(id, (skills ?? "").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(ExpDto).ToList())));
        api.MapPost("/projects/{id}/experience", (string id, ExperienceSaveDto dto) =>
            Handle(() => Results.Ok(ExpDto(
                Experience().CreateForProject(id, dto.Text, Actor(), dto.SkillId,
                    dto.AlwaysLoad, dto.Tags)))));
        // --- ОБЩИЕ ПРАВИЛА РАБОТЫ (T-11-S0): закладка «Общий опыт» настроек ---
        // записи без проекта и без узла шаблона: их получает КАЖДАЯ задача организации.
        // Фильтр по навыкам — тот же, что у опыта проекта (коды через запятую; пусто — все)
        api.MapGet("/experience/general", (string? skills) =>
            Handle(() => Results.Ok(Experience()
                .ListGeneral((skills ?? "").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(ExpDto).ToList())));
        // ПРОВЕРКА ОБЛАСТИ (T-269-S0): текст с признаками проектного (код задачи, путь файла,
        // расширение исходника, имя проекта) в общие правила не проходит — форма показывает
        // предупреждение и повторяет вызов с Force, инструменту агента отказ окончательный
        api.MapPost("/experience/general", (ExperienceSaveDto dto) =>
            Handle(() => Results.Ok(ExpDto(
                Experience().CreateGeneral(dto.Text, Actor(), dto.SkillId,
                    dto.AlwaysLoad, dto.Tags, dto.Force)))));
        // правка из формы шлёт запись целиком, поэтому «загружать всегда» и тэги идут вместе
        // с текстом; тэги, не переданные вовсе (null), остаются прежними — так правка текста
        // инструментом агента не сносит то, что проставил человек
        api.MapPut("/experience/{id}", (string id, ExperienceSaveDto dto) =>
            Handle(() => Results.Ok(ExpDto(
                Experience().Update(id, dto.Text, Actor(), dto.SkillId, changeSkill: true,
                    alwaysLoad: dto.AlwaysLoad, tags: dto.Tags, isActive: dto.IsActive,
                    force: dto.Force)))));
        // ПЕРЕНОС ЗАПИСИ МЕЖДУ ОБЛАСТЯМИ (T-269-S0): общие правила ↔ опыт проекта ↔ опыт узла
        // шаблона. Меняется только привязка — id, текст, тэги, навык, авторство и история
        // остаются теми же; до этого «перенести» значило удалить и завести заново. Переносит
        // только сервер-владелец записи, поставляемое правило дистрибутива не переносится
        api.MapPost("/experience/{id}/move", (string id, ExperienceMoveDto dto) =>
            Handle(() => Results.Ok(ExpDto(Experience().Move(id, dto.Scope, dto.ProjectId,
                dto.TemplateTaskId, Actor(), dto.Force)))));
        // РЕВИЗИЯ ОБЩЕГО ОПЫТА (T-269-S0): какие общие правила выглядят проектными — той же
        // проверкой, что стоит на пути записи. Один проход с переносом пачкой в выбранный
        // проект — и общий опыт чистый
        api.MapGet("/experience/general/suspects", () =>
            Handle(() => Results.Ok(Experience().GeneralSuspects().Select(row =>
                new ExperienceSuspectDto
                {
                    Record = ExpDto(row.Record),
                    Kinds = row.Signs.Select(s => s.Kind).Distinct().ToList(),
                    Samples = ExperienceScopeCheck.Samples(row.Signs),
                }).ToList())));
        // АКТИВНОСТЬ ЗАПИСИ (T-265-S0) отдельным вызовом: кнопка в строке списка меняет одно
        // поле и не переписывает текст, навык и тэги значениями давно прочитанного списка.
        // Чужую запись переключить нельзя — проверка владения внутри сервиса (ТЗ гл. 6)
        api.MapPost("/experience/{id}/active", (string id, ExperienceActiveDto dto) =>
            Handle(() => Results.Ok(ExpDto(Experience().SetActive(id, dto.IsActive, Actor())))));
        api.MapDelete("/experience/{id}", (string id) =>
            Handle(() =>
            {
                Experience().Delete(id, Actor());
                return Results.NoContent();
            }));
        // ПОИСК ПО ОПЫТУ (T-268-S0): строка поиска в списках опыта. Человек ищет ТЕМ ЖЕ
        // механизмом, что и агент инструментом search_experience, — один поиск на двоих,
        // иначе «у агента нашлось, а у меня нет» разбирать было бы нечем.
        // scope — область (project/template/general; пусто или «all» — все), project —
        // ограничение проектом, includeInactive — вместе с неактивными записями
        api.MapGet("/experience/search", (string? q, string? scope, string? project,
                bool? includeInactive, int? limit) =>
            Handle(() => Results.Ok(Experience()
                .Search(q ?? "", scope is "all" ? null : scope, limit ?? 30,
                    includeInactive ?? false, project)
                .Select(hit => new ExperienceHitDto
                {
                    Record = ExpDto(hit.Record),
                    Score = hit.Score,
                    Scope = ExperienceService.ScopeOf(hit.Record),
                }).ToList())));
        // работает ли лексический индекс: FTS5 собран не во всякой сборке SQLite, и человеку
        // стоит знать, что поиск идёт запасным способом
        api.MapGet("/experience/search/state", () =>
            Handle(() => Results.Ok(new ExperienceSearchStateDto
            {
                IndexReady = Experience().SearchIndexReady,
            })));
        // --- ИСПОЛЬЗОВАННЫЙ ОПЫТ ЗАДАЧИ (T-266-S0): вкладка «Использованный опыт» карточки ---
        // что система подставила агенту в задание. У задачи хранятся ТОЛЬКО идентификаторы,
        // поэтому текст и область дочитываются здесь; удалённая (унесённая в архив) запись
        // отдаётся строкой с Available=false — идентификатор при этом остаётся
        api.MapGet("/tasks/{id}/experience/used", (string id) =>
            Handle(() => Results.Ok(Experience().ListUsedByTask(id).Select(use => new ExperienceUsedDto
            {
                ExperienceId = use.ExperienceId,
                Available = use.Record is not null,
                Scope = use.Record is null
                    ? ""
                    : use.Record.IsGeneral ? "general" : use.Record.IsProjectLevel ? "project" : "template",
                TemplateDisplayId = use.Record is null || use.Record.TemplateTaskId.Length == 0
                    ? ""
                    : Tasks().Get(use.Record.TemplateTaskId)?.DisplayId ?? "",
                SkillName = use.Record?.SkillName ?? "",
                // код навыка и «загружать всегда» (T-293-S0): запись правится прямо из журнала,
                // и форме нужны те же поля, что на вкладке «Опыт»
                SkillId = use.Record?.SkillId ?? "",
                Text = use.Record?.Text ?? "",
                Tags = use.Record?.Tags ?? [],
                AlwaysLoad = use.Record?.AlwaysLoad ?? false,
                IsActive = use.Record?.IsActive ?? false,
                JobId = use.JobId,
                UsedAt = use.UsedAt,
            }).ToList())));
        // СТАТИСТИКА использования записи (T-266-S0): сколько задач её получило и когда
        // в последний раз. Задел для шаблона «Анализ опыта»: запись, которую не брали ни разу,
        // — кандидат в неактивные
        api.MapGet("/experience/{id}/usage", (string id) =>
            Handle(() =>
            {
                var stat = Experience().UsageOf(id);
                return Results.Ok(new ExperienceUsageDto
                {
                    ExperienceId = stat.ExperienceId,
                    Count = stat.Count,
                    LastUsedAt = stat.LastUsedAt,
                });
            }));

        // --- НАБОРЫ ОПЫТА (T-270-S0): закладка «Настройки → Наборы опыта» ---
        //
        // Стиль работы — это пачка записей опыта, которую ставят и снимают ЦЕЛИКОМ и возят
        // между установками. Файл набора лежит в каталоге данных рядом с plugins/ и models/,
        // а ОБЛАСТЬ установки (общий опыт / проект / узел шаблона) выбирает человек в момент
        // установки: в файле её нет и быть не должно.
        ExperiencePackService Packs() => Ctx().ExperiencePacks;
        ExperiencePackDto PackDto(ExperiencePack pack, string? lang)
        {
            var (scope, projectId, templateTaskId) = Packs().Placement(pack.Code);
            return new ExperiencePackDto
            {
                Code = pack.Code,
                Name = pack.Name.IsEmpty ? pack.Code : pack.Name.Text(lang),
                Description = pack.Description.Text(lang),
                Records = pack.Records.Count,
                Templates = pack.Templates.Count,
                Installed = Packs().InstalledRecords(pack.Code).Count,
                Scope = scope,
                ProjectId = projectId,
                ProjectName = projectId.Length > 0 ? Projects().Get(projectId)?.Name ?? "" : "",
                TemplateTaskId = templateTaskId,
                TemplateDisplayId = templateTaskId.Length > 0
                    ? Tasks().Get(templateTaskId)?.DisplayId ?? ""
                    : "",
                // документ показывается кнопкой «i» ВНУТРИ приложения, поэтому путь — страница
                // языкового каталога документации, а не файл каталога данных (как у плагинов)
                Doc = pack.Doc.TryGetValue(lang ?? Loc.Lang, out var page) && page.Length > 0
                    ? page
                    : ExperiencePack.DocPageOf(pack.Code),
            };
        }
        api.MapGet("/packs", (string? lang) => Handle(() =>
            Results.Ok(Packs().List().Select(p => PackDto(p, lang)).ToList())));
        api.MapPost("/packs/{code}/install", (string code, ExperiencePackInstallDto dto,
                string? lang) =>
            Handle(() =>
            {
                var pack = Packs().Get(code);
                if (pack is null)
                {
                    return Results.NotFound();
                }
                var (records, templates) = Packs().Install(pack, dto.Scope, dto.ProjectId,
                    dto.TemplateTaskId, Actor(), lang);
                return Results.Ok(new ExperiencePackResultDto
                {
                    Code = pack.Code, Count = records, Templates = templates,
                });
            }));
        // снятие — по той же пометке владельца pack:<код>, которой установка пометила записи
        api.MapPost("/packs/{code}/remove", (string code) =>
            Handle(() => Results.Ok(new ExperiencePackResultDto
            {
                Code = code,
                Count = Packs().Remove(code, Actor()),
            })));
        // ВЫГРУЗКА отобранных записей в файл набора: обмена опытом между установками
        // до этого не было вовсе
        api.MapPost("/packs/export", (ExperiencePackExportDto dto, string? lang) =>
            Handle(() => Results.Ok(new ExperiencePackResultDto
            {
                Code = dto.Code,
                Count = dto.RecordIds.Count,
                Path = Packs().Export(dto.Code, dto.Name, dto.Description, dto.RecordIds, lang),
            })));

        // статистика шаблона (ТЗ п. 2.11, todo32): смены состояния задач, привязанных к шаблону
        api.MapGet("/tasks/{id}/template-stats", (string id) =>
            Handle(() =>
            {
                var stats = Tasks().TemplateStatusStats(id);
                foreach (var row in stats)
                {
                    row.ExecutorNick = NickOf(row.ExecutorId);
                }
                return Results.Ok(stats);
            }));

        // анализ иерархии шаблона перед созданием (todo31): что переспрашивать в диалоге
        api.MapGet("/tasks/{id}/template-info", (string id) =>
            Handle(() => Results.Ok(Tasks().AnalyzeTemplate(id))));
        api.MapPost("/tasks/{id}/instantiate", (string id, InstantiateTemplateDto dto) =>
            Handle(() =>
            {
                PickMode? pick = dto.Pick switch
                {
                    "ai_first" => PickMode.AiFirst,
                    "human_first" => PickMode.HumanFirst,
                    _ => null,
                };
                // parentId (T-201): создание подзадачей из карточки задачи
                return Results.Ok(Tasks().InstantiateTemplate(id, dto.BaseDate, Actor(), pick, Picker(),
                    dto.ParentId));
            }));
        // --- расписания запуска задач (ТЗ п. 2.12, todo34) ---
        api.MapGet("/schedules", () => Results.Ok(Schedules().List()));
        api.MapPost("/schedules", (Core.Entities.Schedule dto) =>
            Handle(() => Results.Ok(Schedules().Create(dto, Actor()))));
        api.MapPut("/schedules/{id}", (string id, Core.Entities.Schedule dto) =>
        {
            dto.Id = id;
            return Handle(() => Results.Ok(Schedules().Update(dto, Actor())));
        });
        api.MapDelete("/schedules/{id}", (string id) =>
            Handle(() =>
            {
                Schedules().Delete(id, Actor());
                return Results.NoContent();
            }));
        // срабатывания в интервале — календарь расписаний (v1.36): когда что запустится
        api.MapGet("/schedules/occurrences", (DateTime from, DateTime to) =>
            Results.Ok(Schedules().List(includeInactive: false)
                .SelectMany(s => ScheduleService
                    .Occurrences(s, from.ToUniversalTime(), to.ToUniversalTime())
                    .Select(at => new ScheduleOccurrenceDto { ScheduleId = s.Id, At = at }))
                .OrderBy(o => o.At)
                .ToList()));
        // просроченные срабатывания (пока приложение не работало): запустить / удалить из списка
        api.MapGet("/schedules/overdue", () =>
        {
            var overdue = Runner().Overdue();
            foreach (var item in overdue)
            {
                var template = Tasks().Get(item.TemplateTaskId);
                item.TemplateDisplayId = template?.DisplayId ?? "";
                item.TemplateTitle = template?.Title ?? "";
            }
            return Results.Ok(overdue);
        });
        api.MapPost("/schedules/overdue/{id}/run", (string id) =>
            Handle(() => Results.Ok(Runner().RunOverdue(id, Actor()))));
        api.MapPost("/schedules/overdue/{id}/dismiss", (string id) =>
            Handle(() =>
            {
                Runner().DismissOverdue(id);
                return Results.NoContent();
            }));

        // --- УВЕДОМЛЕНИЯ ПОЛЬЗОВАТЕЛЯ (T-272) ---
        // Список свой у каждой организации; правило правится на своём сервере (ТЗ гл. 6),
        // а письмо шлёт владелец задачи — тот сервер, где событие произошло
        api.MapGet("/notifications", () => Results.Ok(NotificationRules().List()));
        api.MapPost("/notifications", (NotificationRule dto) =>
            Handle(() => Results.Ok(NotificationRules().Create(dto, Actor()))));
        api.MapPut("/notifications/{id}", (string id, NotificationRule dto) =>
        {
            dto.Id = id;
            return Handle(() => Results.Ok(NotificationRules().Update(dto, Actor())));
        });
        api.MapDelete("/notifications/{id}", (string id) =>
            Handle(() =>
            {
                NotificationRules().Delete(id, Actor());
                return Results.NoContent();
            }));
        // СПРАВОЧНИК МАКРОПОДСТАНОВОК (T-272) — его показывает кнопка в редакторе текстов
        // правила. Коды лежат в ядре (NotificationMacros), пояснения — в словаре языка
        api.MapGet("/notifications/macros", () => Results.Ok(
            NotificationMacros.Catalog
                .Select(m => new NotificationMacroDto
                {
                    Code = m.Code,
                    Marker = m.Marker,
                    Description = Loc.T(m.TextKey),
                })
                .ToList()));
        // НАСТРОЕН ЛИ ТРАНСПОРТ (T-272): форма правила и настройки показывают человеку,
        // почему письма не уходят, — иначе «уведомления не приходят» разбирать нечем
        api.MapGet("/notifications/transport", () =>
        {
            var transport = Ctx().Notifications.TransportFor(NotificationChannels.Mail);
            return Results.Ok(new NotificationTransportDto
            {
                Transport = transport?.Transport ?? NotificationChannels.EmailTransport,
                Ready = transport?.Ready ?? false,
                Reason = transport?.NotReadyReason ?? Loc.T("msg.notify.10"),
            });
        });
        // ПРОБНОЕ ПИСЬМО (T-272): единственный способ проверить настройки почты, не дожидаясь
        // подходящего события. Адрес не указан — свой собственный (адрес вошедшего)
        api.MapPost("/notifications/test", async (NotificationTestDto dto) =>
        {
            var transport = Ctx().Notifications.TransportFor(NotificationChannels.Mail);
            if (transport is null || !transport.Ready)
            {
                return Results.Problem(transport?.NotReadyReason ?? Loc.T("msg.notify.10"),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            var address = dto.Address.Trim().Length > 0
                ? dto.Address.Trim()
                : current.Executor is { } self ? Executors().NotifyAddressOf(self) : "";
            if (address.Length == 0)
            {
                return Results.Problem(Loc.T("msg.notify.11", current.Executor?.Nick ?? ""),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            try
            {
                await transport.SendAsync(new AI2P.Connectors.NotificationMessage
                {
                    Channel = NotificationChannels.Mail,
                    Address = address,
                    Subject = Loc.T("msg.notify.13"),
                    Body = Loc.T("msg.notify.14", Ctx().Org.Name),
                });
                return Results.Ok(new NotificationTestResultDto { Address = address });
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException
                                           or System.Net.Mail.SmtpException or IOException
                                           or System.Net.Sockets.SocketException
                                           or ArgumentException or TimeoutException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // --- АРХИВЫ ОРГАНИЗАЦИИ (T-40-S0, выпуск 1.105) ---
        // Реестр архивов реплицируется, а состояние (открыт/закрыт/удалён) у КАЖДОГО
        // сервера своё — поэтому список отдаётся с состоянием на ЭТОМ сервере, а операции
        // открыть/закрыть/удалить действуют только здесь. Права раздела — как у настроек
        // организации (ApiPermissions: admin)
        api.MapGet("/archives", () => Results.Ok(ArchiveList().List()));
        // СОЗДАНИЕ — ТОЛЬКО НА ДИРИЖЁРЕ: реестр архивов общий, и завести архив на каждом
        // сервере отдельно значило бы получить столько «текущих», сколько в кластере машин.
        // Отказ внятный (ArchiveService.Create, msg.arc.1), а не «ничего не произошло»
        api.MapPost("/archives", (ArchiveCreateDto dto) =>
            Handle(() => Results.Ok(
                ArchiveList().Create(dto.Code, dto.Name, dto.RulesMode, Actor()))));
        // ПЕРЕД ЛЮБОЙ ОПЕРАЦИЕЙ С ФАЙЛАМИ АРХИВА его контекст просмотра забывается (T-45-S0):
        // просмотр держит открытой базу архива, а закрытие и удаление эти файлы уносят
        api.MapPost("/archives/{id}/open", (string id) =>
            Handle(() =>
            {
                registry.ForgetArchiveContexts(Home().Org.Id);
                return Results.Ok(ArchiveList().Open(id, Actor()));
            }));
        // текущий архив закрывать нельзя — в него идёт перенос данных
        api.MapPost("/archives/{id}/close", (string id) =>
            Handle(() =>
            {
                registry.ForgetArchiveContexts(Home().Org.Id);
                return Results.Ok(ArchiveList().Close(id, Actor()));
            }));
        // удаление снимает архив С ЭТОГО СЕРВЕРА; запись реестра остаётся — архив есть
        // на других серверах и оттуда его можно скачать (T-47-S0). Текущий не удаляется
        api.MapDelete("/archives/{id}", (string id) =>
            Handle(() =>
            {
                registry.ForgetArchiveContexts(Home().Org.Id);
                return Results.Ok(ArchiveList().Remove(id, Actor()));
            }));

        // СКАЧАТЬ АРХИВ С ДРУГОГО СЕРВЕРА (T-47-S0). Удаление архива не реплицируется, поэтому
        // снесённый здесь архив спокойно лежит у соседей — список источников человек видит
        // в самой строке архива (Archive.Servers). Приходит архив ВСЕГДА ЗАКРЫТЫМ (.zip),
        // даже если у источника он открыт: состояние архива у каждого сервера своё.
        //
        // Работа идёт ФОНОМ: архив бывает гигабайтным, а форма должна ответить сразу.
        // Ошибка передачи попадает в журнал и в строку ошибки сервера-источника
        api.MapPost("/archives/{id}/download", (string id, ArchiveDownloadDto dto) =>
            Handle(() =>
            {
                var home = Home();
                var archive = home.Archives.Get(id)
                              ?? throw new InvalidOperationException(Loc.T("msg.arc.2", id));
                if (dto.ServerId.Trim().Length == 0)
                {
                    throw new ArgumentException(Loc.T("msg.arcrepl.4", archive.DisplayId));
                }
                var replication = app.Services.GetRequiredService<Org.ReplicationService>();
                var org = home.Org;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await replication.Archives.DownloadAsync(org, home, id, dto.ServerId.Trim());
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Архивы: скачать архив {Archive} с сервера "
                            + "{Server} не удалось: {Error}", archive.DisplayId, dto.ServerId,
                            ex.Message);
                    }
                });
                return Results.Ok(archive);
            }));

        // --- ПРАВИЛА АРХИВАЦИИ (T-41-S0) ---
        // Правил две группы с ОДИНАКОВЫМ составом полей: общие правила организации
        // (справочник настроек — образец, с которого копируются правила нового архива)
        // и правила конкретного архива. Различает их ровно один параметр: archiveId пуст —
        // общие. Поэтому и раздел один, и форма на клиенте одна с двумя режимами
        api.MapGet("/archives/rules", (string? archiveId) =>
            Results.Ok(Home().ArchiveRules.List(archiveId)));
        api.MapPost("/archives/rules", (ArchiveRule rule) =>
            Handle(() => Results.Ok(Home().ArchiveRules.Create(rule, Actor()))));
        api.MapPut("/archives/rules/{id}", (string id, ArchiveRule rule) =>
            Handle(() =>
            {
                rule.Id = id;
                return Results.Ok(Home().ArchiveRules.Update(rule, Actor()));
            }));
        api.MapDelete("/archives/rules/{id}", (string id) =>
            Handle(() =>
            {
                Home().ArchiveRules.Delete(id, Actor());
                return Results.Ok();
            }));
        // «ВЕРНУТЬ УМОЛЧАНИЕ» на форме правил архива и перенос правил при создании архива —
        // одно и то же действие: набор правил архива заменяется целиком
        api.MapPost("/archives/{id}/rules/copy", (string id, ArchiveRulesCopyDto dto) =>
            Handle(() => Results.Ok(Home().ArchiveRules
                .Replace(id, dto.SourceArchiveId, dto.Mode, Actor()))));
        // кандидаты на архивацию по правилам архива: ими автоматическая архивация (T-46-S0)
        // отбирает данные, а форма добавления архива (T-43-S0) проверяет, выполнены ли
        // правила у прежнего текущего архива
        api.MapGet("/archives/{id}/candidates", (string id) =>
            Handle(() => Results.Ok(
                Home().ArchiveCandidates.Select(Home().ArchiveRules.List(id)))));

        // --- АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ (T-46-S0) ---
        // Одно и то же действие зовут двое: расписание на дирижёре (ScheduleRunner) и форма
        // добавления архива, которой надо разобраться с прежним текущим архивом. Архив
        // в запросе не называется — архивация всегда идёт в ТЕКУЩИЙ, как и любой перенос.
        // Отказ («не дирижёр», «архив закрыт») приходит НЕ ошибкой, а сводкой с Ran=false:
        // форме его надо показать человеком читаемой строкой, а расписанию — записать
        // в журнал; ошибкой 400 отвечает только неисправность
        api.MapPost("/archives/auto", () =>
            Handle(() => Results.Ok(Home().AutoArchive.Run(Actor()))));

        // --- ПЕРЕНОС ДАННЫХ В АРХИВ И ОБРАТНО (T-42-S0) ---
        // Архив в запросе не называется НАМЕРЕННО: перенос всегда идёт в текущий архив,
        // и восстановление возможно только из него — в остальных данные уже не меняются.
        // Этими тремя вызовами работают формы соседей: ручная архивация при удалении
        // (T-44-S0), автоматическая по расписанию (T-46-S0) и просмотр архива (T-45-S0)
        api.MapPost("/archives/move", (ArchiveMoveDto dto) =>
            Handle(() => Results.Ok(Home().ArchiveTransfer.Move(dto.Target, dto.Id, Actor()))));
        api.MapPost("/archives/restore", (ArchiveMoveDto dto) =>
            Handle(() => Results.Ok(Home().ArchiveTransfer.Restore(dto.Target, dto.Id, Actor()))));
        // «что уедет» — те же проверки без переноса: форме надо показать состав человеку
        // ДО нажатия кнопки, и отказ («задача в работе») он должен увидеть там же
        api.MapGet("/archives/movable", (string target, string id, bool? restore) =>
            Handle(() => Results.Ok(
                Home().ArchiveTransfer.Preview(target, id, restore ?? false))));

        // остаток лимита исполнителя наружу (T-121); null — лимиты не указаны
        static AgentLimitDto? LimitDto(AgentLimitStatus? status) =>
            status is null
                ? null
                : new AgentLimitDto
                {
                    ExecutorId = status.ExecutorId,
                    Nick = status.Nick,
                    Limit = status.Limit,
                    WindowHours = status.WindowHours,
                    Used = status.Used,
                    Remaining = status.Remaining,
                    Percent = status.Percent,
                    ResetAt = status.ResetAt,
                    IsLow = status.IsLow,
                };

        // force=true (T-121) — запускать, не глядя на остаток лимита исполнителя: так
        // работает кнопка «запустить сейчас» в окне предупреждения о лимите
        // задача ЧУЖОГО сервера (T-196): запускать её здесь нечем — задание идёт там, где
        // лежат её файлы. Но нажать «запустить» человек вправе с любого сервера: заявка
        // едет владельцу репликацией, он и запускает. Ответ — 202 с заявкой вместо задания
        // сервер, который выполнит запуск, — для сообщения человеку. У задачи БЕЗ сервера
        // владельца нет вовсе: её ведёт дирижёр организации, так его и называем (иначе
        // в снэкбаре было бы «запуск передан на сервер » с пустым местом)
        string TargetServerName(TaskItem? task) =>
            Tasks().Scope.Describe(task?.ServerId) is { Length: > 0 } named
                ? named
                : Loc.T("msg.runRequest.4");

        IResult RunRequestResult(string taskId, string kind, bool withErrors, bool withNeedsFix = false)
        {
            var task = Tasks().Get(taskId);
            // ПОВТОРНОЕ НАЖАТИЕ ЗАВОДИТ ЗАЯВКУ ЗАНОВО (T-160-S0). До этого висящая заявка
            // отдавалась человеку как есть («запуск передан на S0»), а на S0 не происходило
            // ничего: отметка «отработана» лежит там же, у владельца, и повторно ту же строку
            // он не выполняет. Кнопку жмут второй раз именно потому, что первый не сработал
            var pending = RunRequests().ListByTask(taskId).FirstOrDefault(r => r.Kind == kind);
            var request = Orchestrator().RequestRun(taskId, kind, withErrors, Actor(), withNeedsFix,
                renew: true);
            return Results.Json(new RunRequestDto
            {
                Id = request?.Id ?? "",
                TaskId = taskId,
                TaskDisplayId = task?.DisplayId ?? "",
                Kind = kind,
                WithErrors = withErrors,
                WithNeedsFix = withNeedsFix,
                TargetServer = TargetServerName(task),
                AlreadyRunning = request is null,
                // «заявка уже подана» говорится, только если новой мы так и не завели
                // (задача уже в работе): иначе человек читал бы «ждите» про поручение,
                // которое только что ушло заново (T-160-S0)
                AlreadyRequested = request is null && pending is not null,
            }, statusCode: 202);
        }

        // autoChildren (T-274-S0) — ответ переспроса «автоматически выполнять новых потомков»:
        // true снимает с задачи запрет автозапуска, false ставит его. Параметра нет — пометка
        // не трогается (так зовут запуск автоматические проходы)
        api.MapPost("/tasks/{id}/start", async (string id, bool? force, bool? autoChildren) =>
        {
            try
            {
                if (Tasks().Get(id) is { IsTemplate: false } foreign && !Tasks().CanWrite(foreign))
                {
                    return RunRequestResult(id, RunRequestKinds.Task, withErrors: false);
                }
                return Results.Ok(await Orchestrator().StartTaskAsync(id, Actor(), force ?? false,
                    autoChildren));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // остаток лимита исполнителя задачи (T-121): UI спрашивает перед запуском.
        // Лимиты не указаны либо исполнитель не ИИ — проверок нет: ответ 204 (T-125).
        // Раньше здесь был Results.Ok(null), а это 200 с ПУСТЫМ телом: клиент разбирал его
        // как JSON и падал JsonException'ом, роняя circuit Blazor по кнопке «запустить»
        api.MapGet("/tasks/{id}/limit", (string id) =>
            Handle(() => LimitDto(Orchestrator().LimitForTask(id)) is { } limit
                ? Results.Ok(limit)
                : Results.NoContent()));
        // ЗАГРУЗКА ОБЪЕКТОВ В МОДЕЛЬ (T-14-S1): что уедет в модель вместе с промптом и что
        // этому мешает — ДО запуска. Тот же расчёт, что делает коннектор, поэтому здесь видно
        // ровно то, чем задание встанет с ошибкой; заодно отдаются обученные адаптеры проекта
        // и исполнители, которые могут заменить назначенного.
        // Задача без объектов, без исполнителя или не медиа-модели — 204: смотреть нечего.
        api.MapGet("/tasks/{id}/object-load", (string id) => Handle(() =>
        {
            var task = Tasks().Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.15", id));
            var executor = task.ExecutorIds.Count > 0 ? Executors().Get(task.ExecutorIds[0]) : null;
            if (executor is null || executor.Kind != ExecutorKind.Ai || executor.ProfilePath.Length == 0)
            {
                return Results.NoContent();
            }
            var profileJson = Files().ReadText(executor.ProfilePath);
            if (profileJson.Trim().Length == 0)
            {
                return Results.NoContent();
            }
            var lora = LoraSettings.Parse(profileJson);
            var refImage = RefImageSettings.Parse(profileJson);
            var refAudio = RefAudioSettings.Parse(profileJson);
            var modelName = Models().Get(executor.ModelId ?? "")?.Name ?? executor.Nick;
            var folder = task.ProjectId is null ? null : Projects().Get(task.ProjectId)?.FolderPath;
            var loads = Ctx().ObjectLoads;
            var plan = loads.Plan(task.ProjectId, Tasks().ReadDescription(task), lora, refImage,
                modelName, folder, executor.ModelId, refAudio);
            return Results.Ok(new
            {
                model = modelName,
                loraSupported = lora.Supported,
                loraReason = lora.Reason,
                refImageKind = refImage.Kind,
                refAudioKind = refAudio.Kind,
                ok = plan.Ok,
                loras = plan.Loras.Select(l => new
                {
                    l.ObjectCode, l.ObjectName, file = Path.GetFileName(l.File), l.Strength,
                }),
                images = plan.Images,
                audios = plan.Audios,
                problems = plan.Problems,
                notes = plan.Notes,
                readyLoras = loads.ReadyLoras(task.ProjectId),
                substitutes = loads
                    .Substitutes(executor.Id, plan.LoraFailed, plan.ImageFailed, plan.AudioFailed)
                    .Select(s => new { s.Executor.Id, s.Executor.Nick, s.Why }),
            });
        }));
        // перенести старт задачи на сброс окна лимита (кнопка «перенести», T-121)
        api.MapPost("/tasks/{id}/defer", (string id) =>
            Handle(() => Results.Ok(new { startAfter = Orchestrator().DeferTaskStart(id) })));
        // авторазбиение задачи на подзадачи ИИ-агентом (ТЗ v1.26, todo28)
        api.MapPost("/tasks/{id}/split", async (string id) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().StartSplitAsync(id, Actor()));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // запуск всей иерархии задачи (T-159): подзадачи снизу вверх по приоритету,
        // корневой родитель — последним
        // withErrors=true (галочка в переспросе, T-186) — перезапускать и подзадачи,
        // вставшие с ошибкой; на каждую такая очередь тратит одну попытку
        // withNeedsFix=true (вторая галочка, T-210) — то же для подзадач в доработке
        api.MapPost("/tasks/{id}/start-hierarchy", async (string id, bool? withErrors, bool? withNeedsFix) =>
        {
            try
            {
                // корень чужой (T-196) — иерархию ведёт его сервер: заявка едет к нему.
                // Чужие задачи ВНУТРИ своей иерархии очередь просит запустить сама
                if (Tasks().Get(id) is { IsTemplate: false } foreign && !Tasks().CanWrite(foreign))
                {
                    return RunRequestResult(id, RunRequestKinds.Hierarchy, withErrors ?? false,
                        withNeedsFix ?? false);
                }
                return Results.Ok(await Orchestrator().StartHierarchyAsync(id, Actor(),
                    withErrors ?? false, withNeedsFix ?? false));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // ОСТАНОВКА работы по задаче (T-263). Снять текущее задание мало, если задача идёт
        // в открытом иерархическом запуске: очередь поднимет её на ближайшем проходе, —
        // поэтому hierarchy=true закрывает и очередь (пометка с корня снимается), а
        // children=true снимает задания и у всех работающих задач её поддерева.
        // Задача (или корень) ЧУЖОГО сервера останавливается заявкой, как и запускается
        // (T-196): ответ тот же, в нём назван сервер, которому передана остановка
        api.MapPost("/tasks/{id}/stop", async (string id, bool? hierarchy, bool? children) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().StopTaskAsync(id, Actor(),
                    hierarchy ?? false, children ?? false));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // АВТОЗАПУСК ПОДЗАДАЧ ветки (T-54-S0): on=false — выключить, on=true — вернуть.
        // Выключенный автозапуск закрывает все автоматические старты поддерева: потомков
        // выполненной задачи, задач, ждавших её как блокирующую, и очередь авторазбиения, —
        // а заодно закрывает открытую очередь иерархии над задачей. children=true снимает и
        // задания уже работающих задач поддерева
        api.MapPost("/tasks/{id}/autostart", async (string id, bool? on, bool? children) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().SetAutoStartAsync(id, Actor(),
                    on ?? false, children ?? false));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        // путь нашего файла из ссылки api/files/raw?path=… (ТЗ v1.45, todo37_3);
        // null — внешняя ссылка или файл папки проекта (он не наш, не удаляем)
        static string? TaskFilePath(string url)
        {
            if (!url.Contains("api/files/raw", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var at = url.IndexOf("path=", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }
            var encoded = url[(at + "path=".Length)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }
            try
            {
                return Uri.UnescapeDataString(encoded);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        // ЛОКАЛЬНАЯ И ВНЕШНЯЯ ССЫЛКА НА ФАЙЛ (T-142). Ссылку на файл пишет ИИ-агент, и
        // строит он её от ВНЕШНЕГО имени своего сервера. Открывать по ней файл, лежащий
        // на этом же диске, незачем: файлы бывают в гигабайты, а идти пришлось бы через
        // сеть. Поэтому у файла две ссылки: локальная — относительный адрес нашего
        // эндпойнта (браузер достроит его тем адресом, по которому человек подключён),
        // и внешняя — по имени сервера, чтобы отправить наружу.
        //
        // Локальная заполняется, ТОЛЬКО если файл здесь действительно лежит: каталог
        // проекта на каждом сервере свой, и то, что есть у одного, у другого может
        // ещё не приехать репликацией. Проверяется это существованием файла на диске.
        void FillLinks(TaskFileDto file)
        {
            file.ExternalUrl = file.Url;
            if (FileLinks.Parse(file.Url) is not { } link)
            {
                return; // чужая ссылка (внешний сайт, file:) — трогать нечего
            }
            file.ExternalUrl = FileLinks.External(link, Ctx().PublicBaseUrl);
            var abs = link.Kind == FileLinks.Project
                ? ResolveProjectFile(Projects().Get(link.ProjectId), link.Path)
                : Files().IsInside(link.Path) ? Files().Abs(link.Path) : null;
            if (abs is not null && File.Exists(abs))
            {
                file.LocalUrl = FileLinks.Relative(link);
            }
        }

        // все файлы, на которые есть ссылки в .md задачи и чате (кнопка «все файлы»,
        // ТЗ v1.28, todo30); ссылки собирает MdLinks.GetAllLinkRefs — включая голые URL
        // (в чате ссылки обычно без [текст](…)); описание — текст в скобках (todo30_2)
        api.MapGet("/tasks/{id}/files", (string id) =>
        {
            var task = Tasks().Get(id);
            if (task is null)
            {
                return Results.NotFound();
            }
            var result = new List<TaskFileDto>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddLinks(string markdown, string source)
            {
                foreach (var link in MdLinks.GetAllLinkRefs(markdown))
                {
                    if (seen.Add(link.Url))
                    {
                        result.Add(new TaskFileDto
                        {
                            Name = LinkName(link.Url),
                            Description = link.Text,
                            Url = link.Url,
                            Source = source,
                        });
                    }
                }
            }
            AddLinks(Tasks().ReadDescription(task), "description");
            AddLinks(Tasks().ReadAcceptance(task), "acceptance");
            foreach (var message in Chat().ListByTask(id))
            {
                AddLinks(message.Text, "chat");
            }
            var artifacts = Tasks().Artifacts(task);
            foreach (var artifact in artifacts.Where(a => a.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            {
                AddLinks(Files().ReadText(artifact), "artifact");
            }
            // сами файлы результатов задачи (artifacts/) — источник «результат» (todo37):
            // видео и картинки медиа-моделей ссылками нигде не лежат, а в списке нужны
            foreach (var artifact in artifacts)
            {
                var url = "api/files/raw?path=" + Uri.EscapeDataString(artifact);
                if (seen.Add(url))
                {
                    result.Add(new TaskFileDto
                    {
                        Name = Path.GetFileName(artifact),
                        Url = url,
                        Source = "result",
                    });
                }
            }
            // путь в хранилище и признак «можно удалить» (ТЗ v1.45, todo37_3): удаляются
            // только наши файлы этой задачи — видеорезультаты весят гигабайты, а иначе
            // из артефактов их не убрать
            foreach (var file in result)
            {
                file.Path = TaskFilePath(file.Url) ?? "";
                file.CanDelete = file.Path.Length > 0 && Tasks().OwnsFile(task, file.Path);
                FillLinks(file);
            }
            return Results.Ok(result);
        });

        // удаление файла из окна «все файлы» (ТЗ v1.45, todo37_3): только файлы самой задачи
        // (artifacts/, ai/, вставки MD-редактора в uploads/ проекта); удаление физическое —
        // смысл кнопки именно в освобождении места под большими видеофайлами
        api.MapDelete("/tasks/{id}/files", (string id, string path) =>
        {
            var task = Tasks().Get(id);
            return task is null
                ? Results.NotFound()
                : Handle(() =>
                {
                    Tasks().DeleteFile(task, path, Actor());
                    return Results.NoContent();
                });
        });

        // --- консоль задания (ТЗ v1.45, todo37_3): живой вывод исполнителя ---
        // after — номер последней прочитанной строки; UI дочитывает только хвост
        api.MapGet("/jobs/{id}/console", (string id, long? after) =>
        {
            var job = Jobs().Get(id);
            if (job is null)
            {
                return Results.NotFound();
            }
            var (lines, nextSeq, lost) = Consoles().Read(id, after ?? 0);
            return Results.Ok(new JobConsoleDto
            {
                JobId = id,
                Running = job.State is JobState.Queued or JobState.Running or JobState.WaitingHuman,
                NextSeq = nextSeq,
                Lost = lost,
                Lines = lines.Select(l => new JobConsoleLineDto
                {
                    Seq = l.Seq,
                    Ts = l.Ts,
                    Text = l.Text,
                }).ToList(),
            });
        });

        // --- чат по задаче (п. 7.2) ---
        api.MapGet("/tasks/{id}/chat", (string id) => Chat().ListByTask(id));
        api.MapPost("/tasks/{id}/chat", (string id, ChatPostDto dto) =>
            Handle(() =>
            {
                // ЧАТ ОБЩИЙ НА ВЕСЬ КЛАСТЕР (T-196). До T-196 переписку на чужой задаче вести
                // было нельзя («задача правится на S1»), и человеку, сидящему за другим
                // сервером, приходилось идти к чужому компьютеру. Теперь сообщение пишется
                // здесь и едет владельцу обычной репликацией — а там его подхватывает
                // наблюдатель чата и доставляет работающему агенту (T-161)
                var task = Tasks().Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.15", id));
                return Results.Ok(Chat().Add(task.Id, Actor(), dto.ToExecutorId, dto.Text));
            }));

        // заявки на запуск задачи, поданные с других серверов (T-196): карточка показывает
        // ими, что запуск уже передан владельцу и ждать надо его, а не жать кнопку снова
        api.MapGet("/tasks/{id}/run-requests", (string id) =>
            Handle(() => Results.Ok(RunRequests().ListByTask(id).Select(r => new RunRequestDto
            {
                Id = r.Id,
                TaskId = r.TaskId,
                TaskDisplayId = Tasks().Get(r.TaskId)?.DisplayId ?? "",
                Kind = r.Kind,
                WithErrors = r.WithErrors,
                WithNeedsFix = r.WithNeedsFix,
                TargetServer = TargetServerName(Tasks().Get(r.TaskId)),
            }).ToList())));

        // перенос обсуждения внешней системы в чат задачи (T-152): вызывается сразу после
        // создания задачи из карточки Trello. Повторный вызов дублей не делает (external_ref)
        api.MapPost("/tasks/{id}/chat/import", (string id, ChatImportDto dto) =>
            Handle(() =>
            {
                var task = Tasks().Get(id) ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.15", id));
                Tasks().EnsureCanWrite(task);
                var added = Chat().Import(id, Actor(), dto.Messages.Select(m =>
                    new ChatService.ImportedMessage(m.ExternalRef, m.Author, m.Text, m.Date)));
                return Results.Ok(new ImportResultDto { Comments = added });
            }));

        // ответ на вопрос ИИ-агента (ТЗ v1.17, todo20): агент продолжает работу с того же места
        api.MapPost("/chat/questions/{id}/answer", async (string id, JobAnswerDto dto) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().AnswerQuestionAsync(id, dto.Text, Actor()));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        // снять вопрос агента, на который уже некому отвечать (T-1-S1): задание упало или
        // остановлено — вопрос закрывается без ответа и уходит из счётчиков задачи и Inbox
        api.MapPost("/chat/questions/{id}/dismiss", (string id) =>
            Handle(() => Results.Ok(Orchestrator().DismissQuestion(id, Actor()))));

        // --- очередь заданий и Inbox (гл. 5, экран 7) ---
        // Inbox — всё, что ждёт человека (T-165): задания людям И висящие вопросы агентов.
        // Текст задания читается только у задания человеку: у вопроса агента запрос — это
        // промпт задания целиком (десятки килобайт), человеку в Inbox нужен сам вопрос
        api.MapGet("/inbox", () => Jobs().Inbox().Select(item => new InboxItemDto
        {
            Kind = item.Kind,
            Job = item.Job,
            TaskTitle = item.TaskTitle,
            TaskDisplayId = item.TaskDisplayId,
            ProjectId = item.ProjectId,
            RequestText = item.Kind == InboxItemKinds.Job && item.Job.RequestPath.Length > 0
                ? Files().ReadText(item.Job.RequestPath)
                : "",
            CreatedAt = item.CreatedAt,
            ExecutorNick = item.ExecutorNick,
            QuestionId = item.QuestionId,
            QuestionText = item.QuestionText,
            Options = item.Options,
            CanAnswer = item.CanAnswer,
        }));
        api.MapPost("/jobs/{id}/answer", (string id, JobAnswerDto dto) =>
            Handle(() => Results.Ok(Orchestrator().AnswerJob(id, dto.Text, Actor()))));
        api.MapPost("/jobs/{id}/cancel", async (string id) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().CancelJobAsync(id, Actor()));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
        // «продолжить» зависшее running-задание (T-117): повторная подача тому же исполнителю;
        // то же самое делает сторож автоматически — кнопка нужна для ручной активации
        api.MapPost("/jobs/{id}/continue", async (string id) =>
        {
            try
            {
                return Results.Ok(await Orchestrator().ContinueJobAsync(id, Actor()));
            }
            catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        // --- справочник импортов и запуск импорта (ТЗ v1.28, todo30) ---
        api.MapGet("/imports", () => Imports().List());
        // документ вида источника (ТЗ гл. 14, todo47_2): кнопка «i» в форме источника.
        // Ключ — ВИД (trello), а не запись: как получить ключи Trello, нужно знать ДО того,
        // как запись сохранена, и для всех источников одного вида это один документ
        api.MapGet("/imports/doc/{kind}", (string kind) =>
            Handle(() => Results.Ok(services.GetRequiredService<DocStore>().ImportDoc(kind))));

        // --- ЧТЕНИЕ ДОКУМЕНТАЦИИ ПОДРЯД (T-17-S1) ---
        // Закладка «Документация»: страница адресуется ПУТЁМ внутри языкового каталога
        // (пусто — README.md), по такому же пути идут внутренние ссылки самих документов.
        // Документация лежит файлами рядом с приложением и организации не касается вовсе,
        // поэтому читается и на сервере, ждущем решения по заявке (см. ApiPermissions)
        api.MapGet("/doc", (string? path) =>
            Handle(() => Results.Ok(services.GetRequiredService<DocStore>().Page(path))));
        // КАРТИНКИ ДОКУМЕНТАЦИИ (T-97-S0): документ ссылается на картинку относительным
        // путём («../images/ai2p-logo.png»), а внутри приложения такой путь не ведёт никуда —
        // браузер разворачивает его по адресу страницы приложения. Поэтому адрес картинки
        // в готовом HTML переписывается сюда (DocLinks.MarkImages), а путь считается ОТ КОРНЯ
        // документации: картинки общие для всех языков и лежат выше языкового каталога
        api.MapGet("/doc/image", (string? path) => Handle(() =>
        {
            var abs = services.GetRequiredService<DocStore>().ImageFile(path);
            return abs is null ? Results.NotFound() : Results.File(abs, ContentTypeOf(abs));
        }));
        // ключ API и токен источника (ТЗ v1.65, T-123): значения наружу не отдаются никогда —
        // только признак «заполнено» и источник значения. Ставятся по одному, каждое своей
        // кнопкой; когда заполнены оба, источник активируется сам
        api.MapGet("/imports/{id}/keys", (string id) =>
            Handle(() => Results.Ok(ImportKeys().Status(id))));
        api.MapPut("/imports/{id}/keys/{field}", (string id, string field, ImportKeySaveDto dto) =>
            Handle(() => Results.Ok(ImportKeys().SetValue(id, field, dto.Value, Actor()))));
        // проверка подключения источника (T-134): ключ и токен уезжают во внешнюю систему,
        // обратно — вердикт словами. Отказ Trello здесь НЕ ошибка запроса: 200 и Ok=false,
        // иначе форма показала бы «что-то пошло не так» вместо объяснения, что именно
        api.MapGet("/imports/{id}/check", async (string id) =>
        {
            // проверку ведёт импортёр СВОЕГО вида (T-249): у каждой внешней системы свой
            // вопрос «кто я» и свой разбор отказа
            var (ok, message) = IsGitLabSource(id)
                ? await GitLab().CheckAsync(id)
                : IsGitHubSource(id)
                    ? await GitHub().CheckAsync(id)
                    : await Trello().CheckAsync(id);
            return Results.Ok(new ImportCheckDto { Ok = ok, Message = message });
        });
        api.MapPost("/imports", (ImportSource source) =>
            Handle(() => Results.Ok(Imports().Create(source, Actor()))));
        api.MapPut("/imports/{id}", (string id, ImportSource source) =>
        {
            source.Id = id;
            return Handle(() => Results.Ok(Imports().Update(source, Actor())));
        });
        api.MapDelete("/imports/{id}", (string id) =>
            Handle(() =>
            {
                Imports().Delete(id, Actor());
                return Results.NoContent();
            }));
        // импорт всегда идёт в указанный проект (кнопка списка задач, ТЗ v1.28)
        api.MapPost("/projects/{id}/import", async (string id, ImportRunDto dto) =>
        {
            var project = Projects().Get(id);
            if (project is null)
            {
                return Results.NotFound();
            }
            try
            {
                // импорт ведёт импортёр вида, названного в источнике (T-249)
                ImportResultDto result;
                if (IsGitLabSource(dto.SourceId))
                {
                    var gl = await GitLab().ImportAsync(project, dto.SourceId,
                        dto.AddNew, dto.UpdateExisting, Actor());
                    result = new ImportResultDto
                    {
                        Added = gl.Added,
                        Updated = gl.Updated,
                        Skipped = gl.Skipped,
                        Comments = gl.Comments,
                    };
                }
                else if (IsGitHubSource(dto.SourceId))
                {
                    var gh = await GitHub().ImportAsync(project, dto.SourceId,
                        dto.AddNew, dto.UpdateExisting, Actor());
                    result = new ImportResultDto
                    {
                        Added = gh.Added,
                        Updated = gh.Updated,
                        Skipped = gh.Skipped,
                        Comments = gh.Comments,
                    };
                }
                else
                {
                    var tr = await Trello().ImportAsync(project, dto.SourceId,
                        dto.AddNew, dto.UpdateExisting, Actor());
                    result = new ImportResultDto
                    {
                        Added = tr.Added,
                        Updated = tr.Updated,
                        Skipped = tr.Skipped,
                        Comments = tr.Comments,
                    };
                }
                return Results.Ok(result);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException
                or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        // импорт одиночного задания по URL (ТЗ v1.50, todo50): содержимое карточки
        // подставляется в форму новой задачи, файлы уже скачаны в uploads/ проекта
        api.MapPost("/projects/{id}/import-card", async (string id, ImportCardDto dto) =>
        {
            var project = Projects().Get(id);
            if (project is null)
            {
                return Results.NotFound();
            }
            try
            {
                // задача узнаётся по виду ССЫЛКИ (T-249, T-247): в форме импорта по URL
                // источник не выбирают, а вставленный адрес сам говорит, откуда он.
                // GITHUB СПРАШИВАЕТСЯ ПЕРВЫМ: адрес задачи GitHub по форме пути
                // (<владелец>/<репозиторий>/issues/<номер>) совпадает со СТАРЫМ видом
                // адреса GitLab — без разделителя «/-/», — и разбору GitLab подходит тоже
                if (GitHubImporter.CanRefresh(dto.Url))
                {
                    var issue = await GitHub().ImportIssueContentAsync(dto.Url, dto.SourceId);
                    return Results.Ok(new ImportedCardDto
                    {
                        Title = issue.Title,
                        Description = issue.Description,
                        DueDate = issue.Due,
                        ExternalRef = issue.ExternalRef,
                        ImportUrl = issue.Url,
                        // вложений отдельным списком у задачи GitHub нет: файлы живут
                        // ссылками внутри самого текста и ссылками остаются
                        Files = [],
                        Comments = GitHubImporter.ChatOf(issue.Comments)
                            .Select(m => new ChatImportMessageDto
                            {
                                Author = m.Author,
                                Text = m.Text,
                                Date = m.Date,
                                ExternalRef = m.ExternalRef,
                            }).ToList(),
                    });
                }
                if (GitLabImporter.CanRefresh(dto.Url))
                {
                    var issue = await GitLab().ImportIssueContentAsync(project, dto.Url, dto.SourceId);
                    return Results.Ok(new ImportedCardDto
                    {
                        Title = issue.Title,
                        Description = issue.Description,
                        DueDate = issue.Due,
                        ExternalRef = issue.ExternalRef,
                        ImportUrl = issue.Url,
                        Files = issue.Files,
                        Comments = GitLabImporter.ChatOf(issue.Notes)
                            .Select(m => new ChatImportMessageDto
                            {
                                Author = m.Author,
                                Text = m.Text,
                                Date = m.Date,
                                ExternalRef = m.ExternalRef,
                            }).ToList(),
                    });
                }
                var card = await Trello().ImportCardContentAsync(project, dto.Url, dto.SourceId);
                return Results.Ok(new ImportedCardDto
                {
                    Title = card.Title,
                    Description = card.Description,
                    DueDate = card.Due,
                    ExternalRef = card.ExternalRef,
                    // ссылка импорта (T-246): едет в форму вместе с содержимым и остаётся
                    // в задаче — по ней её потом обновляют из источника
                    ImportUrl = card.Url,
                    Files = card.Files,
                    // обсуждение карточки (T-152) едет в форму вместе с содержимым и попадает
                    // в чат сразу после того, как человек сохранит задачу
                    Comments = TrelloImporter.ChatOf(card.Comments)
                        .Select(m => new ChatImportMessageDto
                        {
                            Author = m.Author,
                            Text = m.Text,
                            Date = m.Date,
                            ExternalRef = m.ExternalRef,
                        }).ToList(),
                });
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException
                or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        // ОБНОВЛЕНИЕ ЗАДАЧИ ИЗ ИСТОЧНИКА ИМПОРТА (T-246): кнопка «обновить» карточки задачи,
        // когда у задачи заполнена ссылка импорта. Ссылка не передаётся — она хранится
        // в самой задаче; здесь по ней выбирается процедура обновления: карточка Trello
        // (T-246), задача GitHub (T-247) либо issue GitLab (T-249)
        api.MapPost("/tasks/{id}/import/refresh", async (string id, ImportRefreshDto? dto) =>
        {
            var task = Tasks().Get(id);
            if (task is null)
            {
                return Results.NotFound();
            }
            try
            {
                Tasks().EnsureCanWrite(task);
                if (TaskService.NormalizeImportUrl(task.ImportUrl) is not { } url)
                {
                    return Results.Problem(Loc.T("msg.apiEndpoints.34", task.DisplayId),
                        statusCode: 400);
                }
                // процедуру обновления выбирает ВИД ССЫЛКИ (T-249, T-247): Trello, GitLab
                // либо GitHub. Порядок веток не случаен: адрес задачи GitHub подходит и под
                // разбор GitLab (у того есть старый вид адреса без разделителя «/-/»),
                // поэтому GitHub проверяется РАНЬШЕ
                var isGitLab = GitLabImporter.CanRefresh(url);
                var isGitHub = GitHubImporter.CanRefresh(url);
                if (!isGitLab && !isGitHub && !TrelloImporter.CanRefresh(url))
                {
                    return Results.Problem(Loc.T("msg.apiEndpoints.35", url), statusCode: 400);
                }
                // карточка читается в проект задачи: туда же скачиваются приложенные файлы.
                // Задаче GitHub проект не нужен — вложений отдельным списком у неё нет,
                // файлы живут ссылками внутри текста (T-247), и скачивать нечего
                var project = task.ProjectId is { Length: > 0 } projectId
                    ? Projects().Get(projectId)
                    : null;
                if (project is null && !isGitHub)
                {
                    return Results.Problem(Loc.T("msg.apiEndpoints.36", task.DisplayId),
                        statusCode: 400);
                }
                ImportRefreshResultDto refreshed;
                if (isGitHub)
                {
                    var gh = await GitHub().RefreshTaskAsync(task, dto?.SourceId, Actor());
                    refreshed = new ImportRefreshResultDto
                    {
                        DisplayId = gh.DisplayId,
                        Title = gh.Title,
                        Comments = gh.Comments,
                        Files = 0,
                        Exported = gh.Exported,
                    };
                }
                else if (isGitLab)
                {
                    var gl = await GitLab().RefreshTaskAsync(project!, task, dto?.SourceId, Actor());
                    refreshed = new ImportRefreshResultDto
                    {
                        DisplayId = gl.DisplayId,
                        Title = gl.Title,
                        Comments = gl.Comments,
                        Files = gl.Files,
                        Exported = gl.Exported,
                    };
                }
                else
                {
                    var tr = await Trello().RefreshTaskAsync(project!, task, dto?.SourceId, Actor());
                    refreshed = new ImportRefreshResultDto
                    {
                        DisplayId = tr.DisplayId,
                        Title = tr.Title,
                        Comments = tr.Comments,
                        Files = tr.Files,
                        Exported = tr.Exported,
                    };
                }
                return Results.Ok(refreshed);
            }
            // сеть тоже разбираем здесь: кнопка «обновить» ходит к Trello, и «нет связи»
            // человек должен прочитать словами, а не увидеть 500 (так же сделано у заявки
            // в кластер). Иначе единственная подсказка — server.log
            catch (Exception ex) when (ex is ArgumentException or JsonException
                or HttpRequestException or TaskCanceledException
                or (InvalidOperationException and not ObjectDisposedException))
            {
                return Results.Problem(
                    ex is HttpRequestException or TaskCanceledException
                        ? Loc.T("msg.apiEndpoints.37", ex.Message)
                        : ex.Message,
                    statusCode: 400);
            }
        });

        // --- биллинг (ТЗ v1.15, todo18): фильтры по периоду, проекту, команде, исполнителю, модели ---
        api.MapGet("/billing", (DateTime? from, DateTime? to, string? projectId, string? teamId,
                string? executorId, string? modelId) =>
            Billing().Query(from, to, projectId, teamId, executorId, modelId));

        // --- журнал работ (п. 6.3): фильтры по задаче, исполнителю, периоду, типу ---
        api.MapGet("/events", (string? projectId, string? taskId, string? actorId, string? eventType,
                DateTime? from, DateTime? to, int? limit, int? offset) =>
            Journal().Query(projectId, taskId, actorId, eventType, from, to, limit ?? 200, offset ?? 0));

        // --- файлы (тела артефактов — только чтение текста) ---
        api.MapGet("/files", (string path) => Results.Text(Files().ReadText(path)));

        // запись текстового файла — редакторы профайлов и деклараций (п. 2.9, гл. 11);
        // только внутри dataDir и только json/md
        api.MapPut("/files/text", (FileTextDto dto) => Handle(() =>
        {
            var ext = Path.GetExtension(dto.Path).ToLowerInvariant();
            if (!Files().IsInside(dto.Path) || ext is not (".json" or ".md"))
            {
                return Results.BadRequest();
            }
            Files().WriteText(dto.Path, dto.Text);
            return Results.NoContent();
        }));

        // бинарная отдача файла из dataDir — картинки и видео в описаниях .md (гл. 11);
        // range — перемотка видео; download=true — скачивание штатным загрузчиком браузера
        // (todo30; параметр разбирается через bool.TryParse — «1» не понимается, todo37_2)
        api.MapGet("/files/raw", async (string path, bool? download) =>
        {
            if (!Files().IsInside(path))
            {
                return Results.BadRequest();
            }
            var abs = Files().Abs(path);
            return File.Exists(abs)
                ? Results.File(abs, ContentTypeOf(abs),
                    download == true ? Path.GetFileName(abs) : null, enableRangeProcessing: true)
                // файла здесь нет — он на том сервере, где работал агент (T-13-S0)
                : await FromPeerAsync(Ctx(), remoteFiles, FileLinks.Raw, "", path, download == true);
        });

        // отдача файла из ПАПКИ ПРОЕКТА (вне dataDir, ТЗ гл. 11, todo19): внешние ссылки
        // агента на созданные им файлы; путь ограничен каталогом проекта
        api.MapGet("/files/project", async (string projectId, string path, bool? download) =>
        {
            var abs = ResolveProjectFile(Projects().Get(projectId), path);
            if (abs is not null && File.Exists(abs))
            {
                return Results.File(abs, ContentTypeOf(abs),
                    download == true ? Path.GetFileName(abs) : null, enableRangeProcessing: true);
            }
            // ПАПКА ПРОЕКТА У КАЖДОГО СЕРВЕРА СВОЯ И НЕ РЕПЛИЦИРУЕТСЯ (T-13-S0): здесь её
            // может не быть вовсе (abs == null) либо в ней нет этого файла. Значит файл
            // лежит у того сервера, где его создал агент.
            // Путь при этом всё равно обязан быть относительным: абсолютный не наш
            if (!ProjectFiles.IsRelative(path))
            {
                return Results.BadRequest();
            }
            // 1) КОПИЯ, ПРИЕХАВШАЯ РЕПЛИКАЦИЕЙ (T-13-S0, повторный заход): владелец файла
            // раскладывает упомянутые в задачах и чате файлы по каталогу данных организации
            // (LinkedFileShare), и они доезжают сюда сами — в том числе от сервера за NAT,
            // которому мы не можем позвонить вовсе. Это главный путь: он не зависит ни от
            // того, кто кому дозванивается, ни от того, включён ли сейчас сосед
            var shared = SharedProjectFile(Ctx(), projectId, path);
            if (shared is not null)
            {
                return Results.File(shared, ContentTypeOf(shared),
                    download == true ? Path.GetFileName(shared) : null, enableRangeProcessing: true);
            }
            // 2) не приехала — спрашиваем звонком у того, у кого файл лежит
            return await FromPeerAsync(Ctx(), remoteFiles, FileLinks.Project, projectId, path,
                download == true);
        });

        // загрузка файла (картинка из буфера обмена в MD-редакторе, гл. 11)
        api.MapPost("/files/upload", (FileUploadDto dto) => Handle(() =>
        {
            var slug = dto.ProjectId is null ? null : Projects().Get(dto.ProjectId)?.Slug;
            var bytes = Convert.FromBase64String(dto.DataBase64);
            var rel = Files().SaveUpload(slug, dto.FileName, bytes);
            return Results.Ok(new FileUploadResultDto { Path = rel });
        }));

        // --- каталоги локального диска: диалог выбора папки проекта (гл. 11) ---
        // files=true (T-12-S1) — показать ещё и файлы: кадр датасета LoRA берут «от корня
        // файловой системы», то есть тем же обходом дисков, но с файлами. Без параметра —
        // ровно прежнее поведение, одни каталоги
        api.MapGet("/fs/dirs", (string? path, bool? files) =>
            Handle(() => Results.Ok(ListDirs(path, files == true))));

        // --- содержимое ПАПКИ ПРОЕКТА (T-264): диалог выбора эталонного файла объекта.
        // Пути только относительные и только внутри папки проекта — за её край не выпускаем
        api.MapGet("/fs/project", (string projectId, string? path) => Handle(() =>
        {
            var listing = ProjectFiles.List(Projects().Get(projectId)?.FolderPath, path);
            return listing is null
                ? Results.Problem(Loc.T("msg.apiEndpoints.38"), statusCode: 400)
                : Results.Ok(listing);
        }));

        // сведения о файле папки проекта — превью формы объекта (T-264). Отдельным запросом,
        // потому что форма спрашивает их АСИНХРОННО, уже нарисовавшись
        api.MapGet("/fs/project-file", (string projectId, string path) => Handle(() =>
        {
            var info = ProjectFiles.Info(Projects().Get(projectId)?.FolderPath, path);
            return info is null
                ? Results.Problem(Loc.T("msg.apiEndpoints.38"), statusCode: 400)
                : Results.Ok(info);
        }));

        // --- ПЛАГИНЫ И MCP (T-114-S0): закладка «Настройки → Плагины и MCP» ---
        //
        // СТРОКА СПИСКА СКЛАДЫВАЕТСЯ ИЗ ДВУХ ИСТОЧНИКОВ: манифест на диске ЭТОГО сервера
        // (plugins/<код>/plugin.json) и запись организации (реплицируется). Манифест без
        // записи — это состояние «объявлен»; сама запись заводится только когда человек нажал
        // «Инициализировать», иначе её плодил бы каждый, кто просто открыл вкладку (а чтение
        // списка разрешено и читателю). Обратный случай — запись без манифеста: плагин завели
        // на соседнем сервере, а файла у нас нет; такой строкой в списке видно честно.
        PluginService Plugins() => Ctx().Plugins;
        PluginSetupService PluginSetup() => Ctx().PluginSetup;
        PluginManifest? PluginManifestOf(string code) =>
            PluginManifest.Read(Files().DataDir, code);
        // ручной путь — пер-серверное значение, оно живёт в config.json и НЕ реплицируется
        string PluginManualPath(string code) =>
            configHolder.Config.Plugins.TryGetValue(code, out var s) ? s.Path : "";
        // ПУТЬ КОНКРЕТНОЙ ПРОГРАММЫ ПЛАГИНА (T-146-S0): у ГЛАВНОЙ (первой) он лежит в старом
        // поле path, у остальных — в словаре paths по имени записи софта. Так config.json уже
        // поставленных серверов продолжает работать без переноса значений
        string PluginPathFor(PluginManifest? manifest, string code, PluginSoftware soft)
        {
            // ВОСКЛИЦАТЕЛЬНЫЙ ЗНАК У configHolder (T-314) — не про данные, а про компилятор:
            // Roslyn 4.8 (SDK 8.0.1xx, им собирают под Linux) в теле ЭТОЙ локальной функции
            // считает захваченную переменную возможно пустой и выдаёт CS8602, хотя она
            // получена GetRequiredService и пустой не бывает. Roslyn 4.11 (SDK 8.0.4xx)
            // того же не говорит — предупреждение видно только на машине с ранним SDK.
            // Соседняя PluginManualPath пишет ту же цепочку без знака и не краснеет, так
            // что правка точечная: там, где компилятор действительно ошибается
            if (!configHolder!.Config.Plugins.TryGetValue(code, out var s))
            {
                return "";
            }
            if (s.Paths.TryGetValue(soft.Id, out var own) && own.Trim().Length > 0)
            {
                return own;
            }
            return ReferenceEquals(manifest?.Software, soft) ? s.Path : "";
        }
        // запись софта по имени; пусто или незнакомое имя — главная программа
        PluginSoftware? PluginSoftOf(PluginManifest? manifest, string? id)
        {
            var list = manifest?.SoftwareList ?? [];
            var wanted = (id ?? "").Trim();
            return wanted.Length == 0
                ? list.FirstOrDefault()
                : list.FirstOrDefault(s =>
                      string.Equals(s.Id, wanted, StringComparison.OrdinalIgnoreCase))
                  ?? list.FirstOrDefault();
        }
        // КЭШ ИНСТРУМЕНТОВ СЕРВЕРА MCP (T-119-S0) — тоже пер-серверный: список снимается с
        // сервера этим компьютером, а у соседа версия сервера может быть другой. Пустой
        // список для шлюза безвреден: у него инструменты названы манифестом
        List<string> PluginMcpCache(string code) =>
            configHolder.Config.Plugins.TryGetValue(code, out var s) ? s.Tools : [];
        // ПРОГРАММА, ПОСТАВЛЕННАЯ НАШИМ ЖЕ ПАКЕТОМ (T-161-S0). Пакет плагина — это
        // ПОРТАТИВНЫЙ архив: он распаковывается в <корень пакетов>/<каталог пакета>,
        // установщик системы при этом не запускается вовсе (из браузера его и нечем
        // запустить — он просит прав администратора и своего окна), и в PATH такая
        // программа не попадает никогда. А поиск программы плагина смотрит ровно две вещи —
        // PATH и ручной путь (PluginSoftwareProbe), — поэтому только что установленный
        // Blender показывался как «не найден», состояние записи оставалось «софт ищется», а
        // увидеть, КУДА он встал, человеку было негде. Найденный в каталоге пакета файл
        // записывается ручным путём — ТОЛЬКО В ПУСТОЕ ПОЛЕ (указанное человеком главнее
        // нашего), тем же правилом, каким это делает установка модели (T-156-S0).
        void PluginAdoptPackagePaths(PluginManifest? manifest, string code)
        {
            if (manifest is null)
            {
                return;
            }
            var installs = ModelInstalls();
            var catalog = installs.Packages();
            var config = configHolder.Config;
            var written = false;
            foreach (var soft in manifest.SoftwareList)
            {
                if (PluginPathFor(manifest, code, soft).Trim().Length > 0)
                {
                    continue;   // путь уже указан — он главнее нашего
                }
                var package = catalog.Find(soft.Package.Trim());
                if (package is null || installs.PackageRunState(package.Id).Running)
                {
                    continue;   // ставить нечем либо установка ещё идёт (архив распакован не весь)
                }
                var file = installs.FindInPackage(package, package.Check);
                // берём ТОЛЬКО свой каталог пакета: программу из PATH проверка найдёт и так,
                // а запись её ручным путём означала бы «человек показал этот экземпляр»
                var dir = installs.PackageDir(package);
                if (file is null || !File.Exists(file)
                    || !file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!config.Plugins.TryGetValue(code, out var settings))
                {
                    settings = new Ai2pConfig.PluginSettings();
                    config.Plugins[code] = settings;
                }
                // главная (первая) программа — в старое поле path, остальные — в словарь
                // paths по имени записи софта (T-146-S0)
                if (ReferenceEquals(manifest.Software, soft))
                {
                    settings.Path = file;
                }
                else
                {
                    settings.Paths[soft.Id] = file;
                }
                settings.CheckedAt = DateTime.UtcNow;
                written = true;
            }
            if (written)
            {
                // накопленный ответ проверки уже не про эти пути
                PluginSoftwareProbe.Forget();
                config.Save(configHolder.Path);
            }
        }
        // ОДНА ПРОГРАММА ПЛАГИНА: свой ручной путь, свой поиск в PATH, своя версия
        PluginSoftwareProbe.Result PluginProbeOne(PluginManifest? manifest, string code,
            PluginSoftware soft) =>
            PluginSoftwareProbe.Find(soft, PluginPathFor(manifest, code, soft));
        // ИТОГ ПО ПЛАГИНУ ЦЕЛИКОМ (T-146-S0): плагин готов, только если готовы ВСЕ его
        // программы — первая ненайденная и решает, что показать человеку и что делать
        // дальше (поставить пакет либо указать путь руками). У плагина с одной программой
        // это ровно прежнее поведение
        PluginSoftwareProbe.Result PluginProbe(PluginManifest? manifest, string code)
        {
            var list = manifest?.SoftwareList ?? [];
            if (list.Count == 0)
            {
                return PluginSoftwareProbe.NotNeeded;
            }
            var results = list.Select(s => PluginProbeOne(manifest, code, s)).ToList();
            return results.FirstOrDefault(r => !r.Ready) ?? results[0];
        }
        PluginListItemDto PluginDto(PluginManifest? manifest, PluginRecord? record, string code,
            string? lang)
        {
            var software = PluginProbe(manifest, code);
            var state = record?.State ?? PluginStates.Declared;
            return new PluginListItemDto
            {
                Id = record?.Id ?? "",
                DisplayId = record?.DisplayId ?? "",
                Code = code,
                Kind = manifest?.Kind ?? record?.Kind ?? "",
                Name = manifest is null || manifest.Name.IsEmpty
                    ? record?.Name ?? code
                    : manifest.Name.Text(lang),
                Description = manifest?.Description.Text(lang) ?? "",
                State = state,
                // одно слово, которое видит человек: решение организации плюс наличие софта ЗДЕСЬ
                ShownState = PluginLifecycle.Shown(state, software.Status),
                Software = PluginSoftwareCodes.Of(software.Status),
                SoftwarePath = software.Path,
                SoftwareVersion = software.Version,
                HasManifest = manifest is not null,
                Actions = manifest is null
                    ? 0
                    : PluginSetup().RegisteredTools(manifest, PluginMcpCache(code)).Count,
                Experience = PluginSetup().ExperienceOf(code).Count,
                // документ показывается кнопкой «i» ВНУТРИ приложения, поэтому путь — страница
                // языкового каталога документации, а не файл каталога данных (T-97-S0)
                Doc = "plugins/" + code + ".md",
                Publishes = PluginLifecycle.Publishes(state, software.Status),
            };
        }
        PluginRecord? PluginRecordOf(string code) => Plugins().ByCode(code);
        // ОПРОС СЕРВЕРА MCP: секрет берётся из хранилища и уходит ТОЛЬКО в заголовок запроса
        async Task<IReadOnlyList<McpToolInfo>> McpToolsAsync(PluginManifest manifest, string code,
            CancellationToken ct)
        {
            if (manifest.Kind != PluginKinds.Mcp || manifest.Connection is null)
            {
                throw new InvalidOperationException(Loc.T("msg.mcp.1", code));
            }
            var secret = secrets.Read(
                PluginCodes.SecretRef(code, manifest.Connection.SecretRef));
            return await McpClient.ListToolsAsync(manifest.Connection, secret, ct);
        }
        // кэш списка — пер-серверный, поэтому он в config.json, а не в базе организации
        void SavePluginTools(string code, List<string> tools)
        {
            var config = configHolder.Config;
            if (!config.Plugins.TryGetValue(code, out var settings))
            {
                settings = new Ai2pConfig.PluginSettings();
                config.Plugins[code] = settings;
            }
            settings.Tools = tools;
            settings.ToolsAt = DateTime.UtcNow;
            config.Save(configHolder.Path);
        }
        IResult PluginRow(string code, string? lang) =>
            Results.Ok(PluginDto(PluginManifestOf(code), PluginRecordOf(code), code, lang));

        api.MapGet("/plugins", (string? lang) => Handle(() =>
        {
            var records = Plugins().List();
            var manifests = PluginManifest.ReadAll(Files().DataDir);
            var byCode = records.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
            var list = manifests
                .Select(m => PluginDto(m, byCode.GetValueOrDefault(m.Code), m.Code, lang))
                .ToList();
            var known = manifests.Select(m => m.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            list.AddRange(records.Where(r => !known.Contains(r.Code))
                .Select(r => PluginDto(null, r, r.Code, lang)));
            return Results.Ok(list.OrderBy(p => p.Code, StringComparer.Ordinal).ToList());
        }));

        api.MapGet("/plugins/{code}", (string code, string? lang) => Handle(() =>
        {
            var manifest = PluginManifestOf(code);
            var record = PluginRecordOf(code);
            if (manifest is null && record is null)
            {
                return Results.NotFound();
            }
            var cache = PluginMcpCache(code);
            var registered = manifest is null
                ? []
                : PluginSetup().RegisteredTools(manifest, cache).ToHashSet(StringComparer.Ordinal);
            var connection = manifest?.Connection;
            return Results.Ok(new PluginDetailsDto
            {
                // подключение MCP: транспорт и то, куда мы звоним. Секрета здесь нет —
                // наружу отдаётся только признак «задан» и файл, куда его класть руками
                Transport = connection?.Transport ?? "",
                Endpoint = connection is null ? "" : McpClient.Describe(connection),
                SecretRef = connection is null
                    ? ""
                    : PluginCodes.SecretRef(code, connection.SecretRef),
                SecretHeader = connection?.SecretHeader ?? "",
                HasSecret = connection is not null
                            && secrets.Has(PluginCodes.SecretRef(code, connection.SecretRef)),
                SecretFile = connection is null
                    ? ""
                    : secrets.LocationOf(PluginCodes.SecretRef(code, connection.SecretRef)),
                ToolsAt = configHolder.Config.Plugins.TryGetValue(code, out var pluginCfg)
                    ? pluginCfg.ToolsAt
                    : null,
                Plugin = PluginDto(manifest, record, code, lang),
                Package = manifest?.Software?.Package ?? "",
                SoftwareRequired = manifest?.Software?.Required ?? false,
                SoftwareHint = manifest?.Software?.Hint.Text(lang) ?? "",
                ManualPath = PluginManualPath(code),
                // путь к программе указывается руками у ЛЮБОГО плагина, которому программа
                // нужна (T-136-S0): pathKey — это имя поля, а не разрешение его показывать
                HasManualPath = manifest?.Software is not null,
                // действия — ТОЛЬКО ЧТЕНИЕ: они приходят манифестом и правятся вместе с ним.
                // У MCP манифест их не называет вовсе (список динамический, он снят с самого
                // сервера) — тогда показывается кэш инструментов ЭТОГО сервера
                Actions = manifest?.Kind == PluginKinds.Mcp
                    ? [.. cache.Select(tool => new PluginActionDto
                    {
                        Code = PluginCodes.Action(code, tool),
                        Tool = tool,
                        Title = PluginSetup().ActionTitle(PluginCodes.Action(code, tool), lang),
                        Registered = registered.Contains(tool),
                    })]
                    : [.. (manifest?.Actions ?? []).Select(a => new PluginActionDto
                    {
                        Code = PluginCodes.Action(code, a),
                        Tool = a.Tool,
                        Title = a.Title.Text(lang),
                        Description = a.Description.Text(lang),
                        NeedsSoftware = a.NeedsSoftware,
                        Registered = registered.Contains(a.Tool),
                        // ДОЛГИЙ ЗАПУСК (T-155-S0): человеку в форме плагина надо видеть, что
                        // работа идёт часами и что второй такой запуск будет ЖДАТЬ
                        RunOp = manifest?.Run?.Find(a.Op) is not null ? a.Op : "",
                        SingleInstance = a.SingleInstance
                            || manifest?.Run?.Find(a.Op) is { SingleInstance: true },
                        IdleTimeoutSec = a.IdleTimeoutSec > 0 ? a.IdleTimeoutSec
                            : manifest?.Run?.Find(a.Op)?.IdleTimeoutSec ?? 0,
                        TimeoutSec = a.TimeoutSec > 0 ? a.TimeoutSec
                            : manifest?.Run?.Find(a.Op)?.TimeoutSec ?? 0,
                    })],
                Experience = [.. PluginSetup().ExperienceOf(code).Select(ExpDto)],
            });
        }));

        // ИНИЦИАЛИЗАЦИЯ: завести запись (если её ещё нет), зарегистрировать действия в
        // справочнике и положить записи опыта в общий опыт организации (T-112-S0)
        // У ЗАПИСИ ВИДА mcp ПЕРВЫМ ДЕЛОМ СНИМАЕТСЯ СПИСОК ИНСТРУМЕНТОВ (T-119-S0): руками он
        // не пишется нигде, а публиковать агенту можно только то, у чего есть запись
        // справочника. Порядок обязателен: подключились → получили список → завели записи →
        // и только теперь инструменты публикуются
        api.MapPost("/plugins/{code}/initialize", async (string code, string? lang,
            CancellationToken ct) => await HandleAsync(async () =>
        {
            var manifest = PluginManifestOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.8", code));
            var tools = manifest.Kind == PluginKinds.Mcp
                ? await McpToolsAsync(manifest, code, ct)
                : null;
            // программа могла быть поставлена нашим пакетом минуту назад: без этого запись
            // сразу после инициализации показывала бы «софт ищется» (T-161-S0)
            PluginAdoptPackagePaths(manifest, code);
            var record = Plugins().Declare(manifest, Actor(), lang);
            PluginSetup().InitializePlugin(record, manifest,
                tools?.Select(t => t.Name).ToList(), Actor(), lang, tools);
            if (tools is not null)
            {
                SavePluginTools(code, [.. tools.Select(t => t.Name)]);
            }
            return PluginRow(code, lang);
        }));
        // ОБНОВИТЬ СПИСОК ИНСТРУМЕНТОВ — ЯВНОЕ ДЕЙСТВИЕ ЧЕЛОВЕКА, и оно показывает, что
        // добавилось и что исчезло. Молча дозаводить действия нельзя: список у сервера MCP
        // динамический, и «тихо появившийся» инструмент — это возможность, которую человек
        // не разрешал. Исчезнувшему инструменту запись справочника снимается — с этой минуты
        // он и не публикуется, и запрещён обратной политикой
        api.MapPost("/plugins/{code}/tools", async (string code, string? lang,
            CancellationToken ct) => await HandleAsync(async () =>
        {
            var manifest = PluginManifestOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.8", code));
            var tools = await McpToolsAsync(manifest, code, ct);
            var names = tools.Select(t => t.Name).ToList();
            var before = PluginMcpCache(code);
            var added = names.Where(n => !before.Contains(n, StringComparer.Ordinal)).ToList();
            var removed = before.Where(n => !names.Contains(n, StringComparer.Ordinal)).ToList();
            // записи заводятся ВСЕМ (идемпотентно): инструмент мог быть в кэше, а записи у
            // него не быть — например, инициализацию делали, когда сервер его ещё не отдавал
            PluginSetup().Initialize(manifest, names, lang, tools);
            PluginSetup().RemoveTools(code, removed);
            SavePluginTools(code, names);
            var registered = PluginSetup().RegisteredTools(manifest, names)
                .ToHashSet(StringComparer.Ordinal);
            return Results.Ok(new McpToolsDto
            {
                Tools = [.. tools.Select(t => new PluginActionDto
                {
                    Code = PluginCodes.Action(code, t.Name),
                    Tool = t.Name,
                    Title = t.TitleOrName,
                    Description = t.Description,
                    Registered = registered.Contains(t.Name),
                })],
                Added = added,
                Removed = removed,
                Plugin = PluginDto(manifest, PluginRecordOf(code), code, lang),
            });
        }));
        // СЕКРЕТ ПОДКЛЮЧЕНИЯ (токен сервера MCP) — в secrets/ ЭТОГО сервера, как пароль почты
        // (T-272): в config.json и в базу организации он не попадает никогда, наружу не
        // отдаётся вовсе, в журнал не пишется. Пустое значение — «убрать секрет»
        api.MapPut("/plugins/{code}/secret", (string code, PluginSecretDto dto, string? lang) =>
            Handle(() =>
            {
                var manifest = PluginManifestOf(code)
                    ?? throw new InvalidOperationException(Loc.T("msg.plugin.8", code));
                if (manifest.Connection is null)
                {
                    throw new InvalidOperationException(Loc.T("msg.mcp.1", code));
                }
                secrets.Write(PluginCodes.SecretRef(code, manifest.Connection.SecretRef),
                    (dto.Value ?? "").Trim());
                return PluginRow(code, lang);
            }));
        // мягкий выключатель: записи остаются, действия агенту не публикуются
        api.MapPost("/plugins/{code}/disable", (string code, string? lang) => Handle(() =>
        {
            var record = PluginRecordOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", code));
            PluginSetup().DisablePlugin(record, Actor());
            return PluginRow(code, lang);
        }));
        api.MapPost("/plugins/{code}/enable", (string code, string? lang) => Handle(() =>
        {
            var record = PluginRecordOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", code));
            PluginSetup().EnablePlugin(record, Actor());
            return PluginRow(code, lang);
        }));
        // СНЯТЬ: убрать действия и записи опыта; настройки записи и ручной путь остаются,
        // повторная инициализация не требует вводить всё заново
        api.MapPost("/plugins/{code}/remove", (string code, string? lang) => Handle(() =>
        {
            var record = PluginRecordOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.1", code));
            var manifest = PluginManifestOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.8", code));
            // у MCP имена инструментов манифест не называет — их знает только кэш этого
            // сервера, и без него записи справочника остались бы висеть после снятия
            PluginSetup().RemovePlugin(record, manifest, PluginMcpCache(code), Actor());
            SavePluginTools(code, []);
            return PluginRow(code, lang);
        }));
        // РУЧНОЙ ПУТЬ К ПРОГРАММЕ — «моя машина»: config.json этого сервера, не база организации
        api.MapPut("/plugins/{code}/path", (string code, PluginPathDto dto, string? lang) =>
            Handle(() =>
            {
                var config = configHolder.Config;
                if (!config.Plugins.TryGetValue(code, out var settings))
                {
                    settings = new Ai2pConfig.PluginSettings();
                    config.Plugins[code] = settings;
                }
                var manifest = PluginManifestOf(code);
                var value = (dto.Path ?? "").Trim();
                var soft = PluginSoftOf(manifest, dto.Soft);
                // ГЛАВНАЯ программа пишется в старое поле path, остальные — в словарь paths
                // по имени записи софта (T-146-S0)
                if (soft is null || ReferenceEquals(manifest?.Software, soft))
                {
                    settings.Path = value;
                    settings.Paths.Remove(soft?.Id ?? "");
                }
                else if (value.Length > 0)
                {
                    settings.Paths[soft.Id] = value;
                }
                else
                {
                    settings.Paths.Remove(soft.Id);
                }
                settings.CheckedAt = DateTime.UtcNow;
                // путь сменился — накопленный ответ проверки уже не про него
                PluginSoftwareProbe.Forget();
                settings.Installed = PluginProbe(manifest, code).Ready;
                config.Save(configHolder.Path);
                return PluginRow(code, lang);
            }));
        // «Проверить»: человек поставил программу и хочет ответа сейчас, а не через минуту.
        // Заодно подхватывается программа, поставленная нашим же пакетом (T-161-S0)
        api.MapPost("/plugins/{code}/probe", (string code, string? lang) => Handle(() =>
        {
            PluginSoftwareProbe.Forget();
            PluginAdoptPackagePaths(PluginManifestOf(code), code);
            return PluginRow(code, lang);
        }));
        // ОКНО УСТАНОВКИ И НАСТРОЙКИ ПЛАГИНА (T-136-S0) — то же, что у модели: состояние
        // спрашивается опросом, установка идёт В ФОНЕ, а не в теле запроса. Прежний вариант
        // («Установить» ждала конца установки молча) выглядел как кнопка, которая ничего не
        // делает: у melt и ffmpeg это десятки мегабайт и минуты без единого признака работы
        IResult PluginInstall(string code, string? lang, ModelPackageStatusDto? status = null,
            string? softId = null)
        {
            var manifest = PluginManifestOf(code);
            // окно опрашивает это состояние раз в секунду — здесь же и подхватывается
            // программа, только что распакованная нашим пакетом (T-161-S0)
            PluginAdoptPackagePaths(manifest, code);
            var package = (manifest?.Software?.Package ?? "").Trim();
            var (running, currentFile, error) = package.Length > 0
                ? ModelInstalls().PackageRunState(package)
                : (false, "", "");
            // СТРОКА НА КАЖДУЮ ПРОГРАММУ ПЛАГИНА (T-146-S0): свой пакет, свой ход установки,
            // свой ручной путь и своя зелёная галка. Готовое состояние пакета (status)
            // относится к ОДНОЙ строке — той, чью установку только что тронули
            var touched = PluginSoftOf(manifest, softId);
            var items = (manifest?.SoftwareList ?? []).Select(soft =>
            {
                var found = PluginProbeOne(manifest, code, soft);
                var pkg = soft.Package.Trim();
                var (run, file, err) = pkg.Length > 0
                    ? ModelInstalls().PackageRunState(pkg)
                    : (false, "", "");
                return new PluginSoftwareItemDto
                {
                    Id = soft.Id,
                    Name = soft.Title(lang),
                    Package = pkg,
                    Required = soft.Required,
                    Status = PluginSoftwareCodes.Of(found.Status),
                    Ok = found.Ready,
                    Path = found.Path,
                    Version = found.Version,
                    ManualPath = PluginPathFor(manifest, code, soft),
                    Hint = soft.Hint.Text(lang),
                    PackageStatus = pkg.Length == 0
                        ? null
                        : ReferenceEquals(soft, touched) && status is not null
                            ? status
                            : ModelInstalls().PackageStatus(pkg),
                    Running = run,
                    CurrentFile = file,
                    Error = err,
                };
            }).ToList();
            return Results.Ok(new PluginInstallDto
            {
                SoftwareItems = items,
                Plugin = PluginDto(manifest, PluginRecordOf(code), code, lang),
                Package = package,
                PackageStatus = package.Length == 0
                    ? null
                    : status ?? ModelInstalls().PackageStatus(package),
                // «идёт установка» — про ЛЮБУЮ программу плагина: по этому полю окно решает,
                // опрашивать ли прогресс дальше
                Running = running || items.Any(i => i.Running),
                CurrentFile = currentFile.Length > 0
                    ? currentFile
                    : items.FirstOrDefault(i => i.Running)?.CurrentFile ?? "",
                Error = error,
                ManualPath = PluginManualPath(code),
                HasManualPath = manifest?.Software is not null,
                SoftwareRequired = manifest?.Software?.Required ?? false,
                SoftwareHint = manifest?.Software?.Hint.Text(lang) ?? "",
            });
        }
        // состояние установки (опрос окна): сети не касается, только диск
        api.MapGet("/plugins/{code}/install", (string code, string? lang) =>
            Handle(() => PluginInstall(code, lang)));
        // размер дистрибутива — запросом к серверу раздачи: несколько секунд сети, поэтому
        // отдельным вызовом, как у модели (POST /models/{id}/install/resolve)
        // soft — имя записи софта (T-146-S0): у плагина с несколькими программами каждая
        // ставится своей кнопкой в своей строке. Пусто — главная программа, как было
        api.MapPost("/plugins/{code}/install/resolve", async (string code, string? lang,
            string? soft, CancellationToken ct) => await HandleAsync(async () =>
        {
            var manifest = PluginManifestOf(code);
            var package = (PluginSoftOf(manifest, soft)?.Package ?? "").Trim();
            var status = package.Length > 0
                ? await ModelInstalls().ResolvePackageSizeAsync(package, ct)
                : null;
            return PluginInstall(code, lang, status, soft);
        }));
        // «Установить»: пакет справочника models/packages.json — тот же механизм, которым
        // ставятся пакеты моделей (T-4-S0), только в фоне и с прогрессом
        api.MapPost("/plugins/{code}/install", (string code, string? lang, string? soft) =>
            Handle(() =>
        {
            var manifest = PluginManifestOf(code)
                ?? throw new InvalidOperationException(Loc.T("msg.plugin.8", code));
            var package = (PluginSoftOf(manifest, soft)?.Package ?? "").Trim();
            if (package.Length == 0)
            {
                throw new InvalidOperationException(Loc.T("msg.plugin.9", code));
            }
            var status = ModelInstalls().StartPackageInstall(package);
            // накопленный ответ проверки софта уже не про новое состояние диска
            PluginSoftwareProbe.Forget();
            return PluginInstall(code, lang, status, soft);
        }));
        api.MapPost("/plugins/{code}/install/cancel", (string code, string? lang, string? soft) =>
            Handle(() =>
        {
            var package = (PluginSoftOf(PluginManifestOf(code), soft)?.Package ?? "").Trim();
            var status = package.Length > 0 ? ModelInstalls().CancelPackageInstall(package) : null;
            PluginSoftwareProbe.Forget();
            return PluginInstall(code, lang, status, soft);
        }));

        // --- настройки ПРИЛОЖЕНИЯ (гл. 10): изменение из UI пишет обратно в config.json ---
        // Всё, что относится к самому серверу (адрес, порт, каталоги этого компьютера,
        // пароль администратора), правится в форме локального сервера — /api/servers/local
        api.MapGet("/settings", () => new SettingsDto
        {
            Language = configHolder.Config.Language,
            OpenBrowserOnStart = configHolder.Config.Ui.OpenBrowserOnStart,
            TabTitleMode = configHolder.Config.Ui.TabTitleMode,
            ScheduleCellMode = configHolder.Config.Ui.ScheduleCellMode,
            // уровень вывода в лог (T-136): в UI три значения, поэтому наружу отдаётся
            // разобранное имя — «Information» из старого конфига покажется как «Info»
            LogLevel = EventLog.Name(EventLog.Parse(configHolder.Config.Logging.Level)),
            // пределы кадра датасета LoRA (T-12-S1): их читает и форма добавления кадра —
            // по ним она сжимает картинку прямо в браузере
            LoraImage = LoraLimits(configHolder.Config),
            // почта уведомлений (T-272): пароль наружу не отдаётся — только признак
            // «задан» и путь файла, куда его можно положить руками
            Mail = MailDto(configHolder.Config, secrets),
            // автообновление (T-208): флажки, время суточной проверки и коды заведённых
            // ими записей расписания — в настройках держится ссылка на расписание
            // расписания лежат в базе ОРГАНИЗАЦИИ, а настройки — про сервер: на сервере
            // без открытой организации кодов записей просто нет, и это не повод отказать
            Update = UpdateDto(configHolder.Config, current.Org?.Schedules),
        });
        api.MapPut("/settings", (SettingsDto dto) => Handle(() =>
        {
            var config = configHolder.Config;
            config.Language = dto.Language;
            // язык установки применяется СРАЗУ (T-180): сообщения хранилища, коннекторов и
            // API берут тексты из словарей по Loc.Lang, перезапуск ради смены языка не нужен
            AI2P.Core.Loc.Lang = dto.Language;
            config.Ui.OpenBrowserOnStart = dto.OpenBrowserOnStart;
            config.Ui.TabTitleMode = dto.TabTitleMode == "code" ? "code" : "name";
            config.Ui.ScheduleCellMode = dto.ScheduleCellMode == "name" ? "name" : "code";
            // уровень применяется СРАЗУ (T-136): и к техническому логу, и к журналу работ —
            // перезапуска приложения ради смены подробности лога не требуется
            config.Logging.Level = EventLog.Name(LogSwitch.Apply(dto.LogLevel));
            // пределы кадра датасета LoRA (T-12-S1). Ноль и мусор не сохраняем: пустое поле
            // формы означало бы «кадров размером ноль», то есть добавить нельзя ни одного
            if (dto.LoraImage is { } lora)
            {
                config.Lora.ImageMaxWidth = lora.MaxWidth > 0 ? lora.MaxWidth : config.Lora.ImageMaxWidth;
                config.Lora.ImageMaxHeight = lora.MaxHeight > 0 ? lora.MaxHeight : config.Lora.ImageMaxHeight;
                config.Lora.ImageMaxKb = lora.MaxKb > 0 ? lora.MaxKb : config.Lora.ImageMaxKb;
                config.Lora.ImageFormat = lora.Format.Trim().ToLowerInvariant() == "jpeg" ? "jpeg" : "png";
                // цвет полей (T-57-S0) приводится к «#RRGGBBAA» здесь же: мусор в config.json
                // означал бы пережатие с чёрными полями, а понять причину было бы нечем
                config.Lora.ImagePadColor = PadColorOf(lora.PadColor, config.Lora.ImagePadColor);
            }
            // ПОЧТА УВЕДОМЛЕНИЙ (T-272). Пароль в config.json не попадает никогда: он идёт
            // в хранилище секретов по ссылке, и только если его прислали (null — «не менять»,
            // иначе форма стирала бы пароль каждым сохранением настроек)
            if (dto.Mail is { } mail)
            {
                config.Mail.Host = mail.Host.Trim();
                config.Mail.Port = mail.Port is > 0 and < 65536 ? mail.Port : config.Mail.Port;
                config.Mail.UseSsl = mail.UseSsl;
                config.Mail.User = mail.User.Trim();
                config.Mail.From = mail.From.Trim();
                config.Mail.FromName = mail.FromName.Trim();
                if (mail.Password is { } password)
                {
                    secrets.Write(config.Mail.ToOptions().PasswordRef, password);
                }
            }
            // АВТООБНОВЛЕНИЕ (T-208). Флажки — настройка этого компьютера, а суточная
            // проверка у СЕРВИСНОГО запуска живёт записью расписания: её и заводим здесь,
            // снятый флажок — удаляем. Консольному запуску запись не нужна вовсе:
            // он проверяется при старте, и вечная запись в расписании путала бы человека
            if (dto.Update is { } update)
            {
                config.Update.AutoCheck = update.AutoCheck;
                config.Update.AutoUpdate = update.AutoUpdate;
                config.Update.Time = AppUpdate.NormalizeTime(update.Time);
                config.Update.Url = update.Url.Trim();
                SyncUpdateSchedules(current.Org?.Schedules, config, ServerActor());
            }
            config.Save(configHolder.Path);
            return Results.NoContent();
        }));

        // --- ОБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208) ---
        // Проверка ходит в сеть, поэтому она ЯВНАЯ (кнопка «Проверить обновления»), а не
        // побочное действие открытия настроек: чужой сервер отвечает не всегда и не быстро
        var appUpdate = services.GetRequiredService<AppUpdateService>();
        api.MapGet("/update/check", () => HandleAsync(async () =>
            Results.Ok(await appUpdate.CheckAsync())));
        // «кого снесёт перезапуск»: спрашивается ПЕРЕД обновлением и показывается человеку.
        // Организации — все ОТКРЫТЫЕ на этом сервере: перезапуск не выбирает, чьи агенты
        // остановить, и предупреждение обязано говорить о том же
        api.MapGet("/update/agents", () => Handle(() =>
        {
            var agents = new List<string>();
            foreach (var context in registry.OpenContexts)
            {
                var active = context.Jobs.ListActive().Select(j => j.Id);
                agents.AddRange(OrgAgents.Stoppable(context.Tasks.AiWork(), active)
                    .Select(item => $"{context.Org.Code}: {item.Task.DisplayId} — {item.Task.Title}"));
            }
            return Results.Ok(new UpdateAgentsDto { Agents = agents });
        }));
        api.MapPost("/update/run", () => HandleAsync(async () =>
        {
            // ответ прошлой проверки, а не новая: между «Проверить» и «Обновить» проходят
            // секунды, а лишний поход в сеть — это ещё один повод отказать на ровном месте
            var found = appUpdate.Last is { Available: true } last ? last : await appUpdate.CheckAsync();
            return Results.Ok(await appUpdate.StartAsync(found));
        }));
    }

    /// <summary>Настройки автообновления наружу (T-208): флажки, время, адрес выпусков
    /// и коды записей расписания, которыми живёт суточная проверка у службы.</summary>
    private static UpdateSettingsDto UpdateDto(Ai2pConfig config, ScheduleService? schedules)
    {
        // только СВОИ записи (ТЗ гл. 6): чужая, приехавшая репликацией, срабатывает у своего
        // сервера и обновляет ЕГО установку — показывать её кодом наших флажков нельзя
        var mine = schedules?.ListMine(includeInactive: true) ?? [];
        string CodeOf(string action) => mine
            .FirstOrDefault(s => s.Action == action && s.DeletedAt is null)?.DisplayId ?? "";
        return new UpdateSettingsDto
        {
            AutoCheck = config.Update.AutoCheck,
            AutoUpdate = config.Update.AutoUpdate,
            Time = AppUpdate.NormalizeTime(config.Update.Time),
            Url = config.Update.Url,
            ReleasesUrl = AppUpdate.ReleasesPageUrl(config.Update.RepoUrl()),
            IsService = ServiceRun.IsService,
            CheckScheduleCode = CodeOf(ScheduleActions.AppCheck),
            UpdateScheduleCode = CodeOf(ScheduleActions.AppUpdate),
        };
    }

    /// <summary>
    /// ЗАПИСИ РАСПИСАНИЯ ПО ФЛАЖКАМ АВТООБНОВЛЕНИЯ (T-208). Флажок поставлен — запись
    /// заводится (ежедневно, время из настройки), снят — удаляется: «чекбоксы убрали —
    /// записи удаляются из расписания» (задание). Время правится и здесь, и в самой
    /// записи расписания — она главнее, поэтому существующей записи мы меняем только
    /// период, а всё остальное в ней остаётся как поставил человек.
    ///
    /// Записи заводятся ТОЛЬКО у сервисного запуска: консольный проверяется при старте.
    /// </summary>
    private static void SyncUpdateSchedules(ScheduleService? schedules, Ai2pConfig config,
        string? actorId)
    {
        if (schedules is null)
        {
            return;
        }
        void Sync(string action, bool wanted)
        {
            var existing = schedules.ListMine(includeInactive: true)
                .FirstOrDefault(s => s.Action == action && s.DeletedAt is null);
            if (!wanted || !ServiceRun.IsService)
            {
                if (existing is not null && schedules.CanWrite(existing))
                {
                    schedules.Delete(existing.Id, actorId);
                }
                return;
            }
            var period = AppUpdate.DailyPeriodJson(config.Update.Time);
            if (existing is null)
            {
                schedules.Create(new Schedule
                {
                    Action = action,
                    Kind = "periodic",
                    PeriodJson = period,
                    IsActive = true,
                }, actorId);
                return;
            }
            if (existing.PeriodJson != period && schedules.CanWrite(existing))
            {
                existing.PeriodJson = period;
                existing.Kind = "periodic";
                schedules.Update(existing, actorId);
            }
        }
        Sync(ScheduleActions.AppCheck, config.Update.AutoCheck);
        Sync(ScheduleActions.AppUpdate, config.Update.AutoUpdate);
    }

    /// <summary>
    /// ПАРОЛЬ АДМИНИСТРАТОРА СЕРВЕРА ВСЛЕД ЗА ПАРОЛЕМ ПОЛЬЗОВАТЕЛЯ (T-291).
    ///
    /// Связь заводится на первом старте флажком «пароль совпадает с локальным админом»
    /// и хранится в config.json (<see cref="Ai2pConfig.ServerAdminSettings"/>): отмечено —
    /// и почта того самого человека. Смена его пароля (своим окном или из формы
    /// пользователя) тянет за собой пароль администратора сервера, иначе после первой же
    /// смены пароля человек перестал бы попадать в настройки своего компьютера, ничего
    /// об этом не зная.
    ///
    /// Пустой пароль ничего не меняет: в форме пользователя пустое поле пароля означает
    /// «не трогать», и трактовать это как «сделать пароль администратора пустым» нельзя.
    /// </summary>
    private static void FollowAdminPassword(ConfigHolder holder, SecretStore secrets,
        string email, string? password)
    {
        if (password is not { Length: > 0 } || !holder.Config.ServerAdmin.Follows(email))
        {
            return;
        }
        Ai2pAuth.SetAdminPassword(secrets, password);
    }

    /// <summary>
    /// Проверка настроек локального сервера перед записью в config.json (ТЗ гл. 10).
    /// От этих значений зависит запуск приложения, поэтому мусор в них попадать не должен:
    /// рядом в форме есть поле пароля, и браузер норовит подставить в соседнее текстовое
    /// поле сохранённый «логин» — без проверки он молча уехал бы в конфиг, и модели стали
    /// бы ставиться неизвестно куда (найдено живой проверкой).
    /// </summary>
    public static void ValidateLocalServer(ServerSettingsDto dto)
    {
        if (dto.Port is < 1 or > 65535)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.16", dto.Port));
        }
        if (dto.Hostname.Trim().Length == 0 || dto.Hostname.Contains('/'))
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.17", dto.Hostname));
        }
        // второй адрес (T-2-S1): пустой — второго адреса нет, это законно; заполненный
        // проверяется теми же правилами, что и первый. Порт без имени не адрес — он молча
        // не сохраняется (в форме такое поле и не даётся заполнить)
        if (dto.Hostname2.Trim().Length > 0 && dto.Hostname2.Contains('/'))
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.17", dto.Hostname2));
        }
        if (dto.Port2 is < 1 or > 65535)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.16", dto.Port2));
        }
        EnsureDirSetting(dto.ModelsRepo, Loc.T("msg.apiEndpoints.18"));
        EnsureDirSetting(dto.DistDir, Loc.T("msg.apiEndpoints.19"));
        EnsureDirSetting(dto.PackagesDir, Loc.T("msg.apiEndpoints.20"));
    }

    /// <summary>
    /// Каталог из настроек локального сервера (ТЗ гл. 10, правило переписано в T-291):
    /// пусто — умолчание; абсолютный путь (и путь от домашнего каталога, «~/ai») берётся
    /// как есть; ОТНОСИТЕЛЬНЫЙ считается от каталога рядом с установкой
    /// (<see cref="Ai2pConfig.StorageSettings.MachineRoot"/>).
    ///
    /// До T-291 относительный путь отвергался — «относительно чего» было неочевидно. Теперь
    /// ответ на этот вопрос один и записан в одном месте, поэтому именно относительные пути
    /// и стали умолчанием: они переносимы между компьютерами и не зависят от буквы диска.
    /// Путь от домашнего каталога по-прежнему считается полным (T-135) и в config.json
    /// сохраняется как есть — раскрывается при чтении.
    ///
    /// Отвергается только заведомо негодное: недопустимые для файловой системы символы,
    /// выход выше корня («../..») и абсолютный путь ЧУЖОЙ платформы (T-164).
    /// </summary>
    public static void EnsureDirSetting(string value, string label)
    {
        var path = value.Trim();
        if (path.Length == 0)
        {
            return;
        }
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.22", label, path));
        }
        // путь чужой платформы (T-164): на Linux «C:\ai» каталогом не станет, а Windows
        // не знает «/home/…». Относительного пути это не касается вовсе — он одинаково
        // годится обеим системам, за что T-291 его и выбрал умолчанием
        if (Ai2pConfig.StorageSettings.IsForeignPath(path))
        {
            throw new ArgumentException(
                Loc.T("msg.apiEndpoints.23", label, path, Ai2pConfig.StorageSettings.SamplePath()));
        }
        // относительный путь не должен уводить выше корня: «../..» от каталога установки —
        // это уже корень диска, и модели легли бы туда
        if (!AI2P.Core.PathHome.IsRooted(path) && LeavesRoot(path))
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.21", label,
                Ai2pConfig.StorageSettings.DefaultModelsRepo, path));
        }
    }

    /// <summary>Относительный путь уходит выше того каталога, от которого считается (T-291):
    /// «..» перевесили обычные шаги. Считается по сегментам, без файловой системы —
    /// значение проверяется до того, как каталог заведут.</summary>
    private static bool LeavesRoot(string path)
    {
        var depth = 0;
        foreach (var part in path.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }
            depth += part == ".." ? -1 : 1;
            if (depth < 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Форма сервера (todo41 п. 5) → входные данные сервиса.</summary>
    private static AI2P.Storage.Services.ServerSaveInput ToServerInput(ServerSaveDto dto) => new()
    {
        Name = dto.Name,
        Protocol = dto.Protocol,
        Hostname = dto.Hostname,
        Port = dto.Port,
        // второй (внешний) адрес сервера (T-50-S0)
        Hostname2 = dto.Hostname2,
        Port2 = dto.Port2,
        BasePath = dto.BasePath,
        IsActive = dto.IsActive,
    };

    /// <summary>
    /// Привести список организаций сервера к присланному формой (todo41 п. 11). Заявки,
    /// ждущие решения, не трогаются: они снимаются кнопками «ПРИНЯТЬ»/«ОТКЛОНИТЬ», а не
    /// правкой списка.
    /// </summary>
    private static void SyncServerOrgs(OrgRegistry registry, string serverId, List<string> orgIds,
        string? actorId)
    {
        var wanted = orgIds.ToHashSet(StringComparer.Ordinal);
        foreach (var link in registry.Servers.OrgsOf(serverId))
        {
            if (link.Status == OrgServerStatus.Active && !wanted.Contains(link.OrgId))
            {
                registry.Servers.Detach(link.OrgId, serverId, actorId);
            }
        }
        foreach (var orgId in wanted)
        {
            LinkKnownServer(registry, orgId, serverId, actorId);
        }
    }

    /// <summary>
    /// СВЯЗАТЬ СЕРВЕР С ОРГАНИЗАЦИЕЙ ПОД ЕГО КЛАСТЕРНЫМ КОДОМ (T-170-S0). Отметка галочкой
    /// в форме — это «включить в организацию сервер, который в кластере УЖЕ есть», а не
    /// «завести новый»: выдавать ему здесь очередной свободный код нельзя. Дирижёр S1,
    /// заведя новую организацию, отмечал в ней соседа S0 — и тот получал S2, код, которым
    /// в кластере не зовут никого (жалоба T-170-S0).
    ///
    /// Кода в кластере нет (сервер нигде не связан) либо он в этой организации уже занят —
    /// работает прежнее правило <c>NextCode</c>: код в организации обязан быть уникальным.
    /// </summary>
    private static void LinkKnownServer(OrgRegistry registry, string orgId, string serverId,
        string? actorId)
    {
        var known = registry.Servers.KnownCodeOf(serverId, orgId);
        registry.Servers.Attach(orgId, serverId, OrgServerStatus.Active, requestedBy: "", actorId,
            code: known.Length > 0 ? known : null);
    }

    /// <summary>
    /// Привести список серверов организации к присланному формой (todo41 п. 11):
    /// та же связь с другой стороны, плюс выбор дирижёра — он обязателен и ровно один.
    ///
    /// СОСТАВ ОРГАНИЗАЦИИ ВЕДЁТ ЕЁ ДИРИЖЁР (T-186-S0). На сервере, который дирижёром этой
    /// организации не является, правится ровно одна строка — его собственная: уйти из
    /// организации он вправе сам, а включать и выключать ЧУЖИЕ машины и назначать дирижёра
    /// (это отдельный обмен заявками, T-21-S1) — нет. Проверка стоит здесь, а не только
    /// в форме: гашёная галочка защищает от нечаянного нажатия, а не от запроса.
    /// </summary>
    private static void SyncOrgServers(OrgRegistry registry, string orgId, List<string> serverIds,
        string? conductorServerId, string? actorId)
    {
        var wanted = serverIds.ToHashSet(StringComparer.Ordinal);
        var links = registry.Servers.ServersOf(orgId);
        var weLead = registry.Servers.IsLocalConductor(orgId);
        var localId = registry.Servers.LocalId();
        if (!weLead)
        {
            foreach (var link in links)
            {
                var was = link.Status == OrgServerStatus.Active;
                if (was != wanted.Contains(link.ServerId) && link.ServerId != localId)
                {
                    throw new ArgumentException(Loc.T("msg.org.13"));
                }
            }
            // сервер, о котором связи ещё нет вовсе, — тем более чужая машина
            foreach (var serverId in wanted.Where(s => s != localId
                                                       && links.All(l => l.ServerId != s)))
            {
                throw new ArgumentException(Loc.T("msg.org.13"));
            }
            var conductor = links.FirstOrDefault(l => l.IsConductor)?.ServerId ?? "";
            if (conductorServerId is { Length: > 0 } && conductorServerId != conductor)
            {
                throw new ArgumentException(Loc.T("msg.org.14"));
            }
            conductorServerId = null;   // дирижёра оставляем как есть
        }
        foreach (var link in links)
        {
            if (link.Status == OrgServerStatus.Active && !wanted.Contains(link.ServerId))
            {
                registry.Servers.Detach(orgId, link.ServerId, actorId);
            }
        }
        foreach (var serverId in wanted)
        {
            LinkKnownServer(registry, orgId, serverId, actorId);
        }
        if (conductorServerId is { Length: > 0 })
        {
            registry.Servers.SetConductor(orgId, conductorServerId, actorId);
        }
    }

    /// <summary>
    /// Применить решение по конфликту КЛЮЧА API (ТЗ гл. 10, этап 45; todo45). Победившее
    /// значение записывается ЗДЕСЬ и сейчас — свежая запись по правилу «выиграл последний»
    /// доедет до партнёра ближайшим сеансом и снимет конфликт и у него. Операций три:
    /// принять левый (версию дирижёра), принять правый (версию рядового сервера) либо
    /// ввести новый ключ — переименования, как у файлов, тут быть не может.
    /// </summary>
    private static void ResolveKeyConflict(OrgRegistry registry, ReplFileConflictDto conflict,
        ReplFileResolveDto dto, string? actorId)
    {
        if (!ReplFileResolutions.IsKeyResolution(dto.Resolution))
        {
            throw new ArgumentException(
                Loc.T("msg.apiEndpoints.24", dto.Resolution));
        }
        var context = registry.ById(conflict.OrgId)
                      ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.25"));
        if (dto.Resolution == ReplFileResolutions.NewValue)
        {
            if (dto.Value.Trim().Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.apiEndpoints.26"));
            }
            context.ModelKeys.SetByRef(conflict.Path, dto.Value.Trim(), actorId);
        }
        else
        {
            var winner = dto.Resolution == ReplFileResolutions.Left
                ? conflict.LeftValue
                : conflict.RightValue;
            if (winner.Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.apiEndpoints.27"));
            }
            // значение уже зашифровано ключом ЭТОЙ организации — перешифровывать незачем
            context.ModelKeys.SetEncrypted(conflict.Path, winner, actorId);
        }
        registry.FileSync.Clear(conflict.OrgId, conflict.ServerId,
            AI2P.Storage.Services.FileSyncStateService.ScopeKey(ReplicationService.KeyScope,
                conflict.OrgId),
            conflict.Path);
    }

    /// <summary>
    /// Применить решение по конфликту ЗАПИСИ СПРАВОЧНИКА МОДЕЛЕЙ (T-227). Операций две —
    /// принять левую (версию дирижёра) или правую: переименований, как у файлов, тут быть
    /// не может, а «ввести новое» бессмысленно (запись правится обычной формой).
    ///
    /// Победившее значение записывается ЗДЕСЬ и сейчас: свежая запись уедет к партнёру
    /// ближайшим сеансом и снимет конфликт и у него — ровно как у ключей API.
    /// </summary>
    private static void ResolveModelConflict(OrgRegistry registry, ReplFileConflictDto conflict,
        ReplFileResolveDto dto, string? actorId)
    {
        if (dto.Resolution != ReplFileResolutions.Left && dto.Resolution != ReplFileResolutions.Right)
        {
            throw new ArgumentException(
                Loc.T("msg.apiEndpoints.32", dto.Resolution));
        }
        var context = registry.ById(conflict.OrgId)
                      ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.25"));
        var winner = dto.Resolution == ReplFileResolutions.Left
            ? conflict.LeftValue
            : conflict.RightValue;
        if (winner.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.apiEndpoints.33"));
        }
        context.Models.ApplyConflictResolution(conflict.Path, winner, actorId);
        registry.FileSync.Clear(conflict.OrgId, conflict.ServerId,
            AI2P.Storage.Services.FileSyncStateService.ScopeKey(ReplicationService.ModelScope,
                conflict.OrgId),
            conflict.Path);
    }

    /// <summary>Путь до <c>.repignore</c> в папке <c>Common</c> проекта НА ЭТОМ сервере
    /// (ТЗ гл. 6, этап 44); null — каталог проекта или папка <c>Common</c> здесь не заданы.</summary>
    private static string? RepIgnorePath(Project? project)
    {
        if (project is null || project.FolderPath.Trim().Length == 0
            || project.CommonPath.Trim().Length == 0)
        {
            return null;
        }
        var root = FileManifest.Resolve(project.FolderPath.Trim(), project.CommonPath.Trim());
        return root is null ? null : Path.Combine(root, RepIgnore.FileName);
    }

    // --- репликация (ТЗ п. 6.1, гл. 6; этап 43) ---

    /// <summary>
    /// Состояние репликации по всем парам «организация — сервер», которые ведёт этот сервер
    /// (todo43 «список серверов»): на дирижёре это все серверы организаций, на рядовом —
    /// только пара с дирижёром. Здесь же всё для экрана диагностики: курсоры, отставание,
    /// конфликты, последняя ошибка.
    /// </summary>
    private static List<ReplicationStatusDto> ReplicationStatus(OrgRegistry registry,
        ReplicationService replication)
    {
        var now = DateTime.UtcNow;
        var result = new List<ReplicationStatusDto>();
        foreach (var org in registry.Orgs.List(includeInactive: false))
        {
            foreach (var peer in replication.Peers(org.Id))
            {
                var state = registry.ReplState.Get(org.Id, peer.ServerId);
                var progress = replication.Progress(org.Id, peer.ServerId);
                var (interval, retry) = replication.Intervals(peer);
                // ИДЁТ ЛИ СЕАНС, ЗНАЕТ ЗАМОК ПАРЫ, А НЕ БАЗА (T-174). Признак «running»
                // в базе оставался от оборванного сеанса, и в списке серверов навсегда
                // повисал прогресс-бар с 0% — при том, что не происходило ничего
                var running = progress is not null;
                // очередь повтора читается ЖИВЬЁМ, а не берётся из снимка последнего сеанса
                // (T-20-S1): «первичная репликация» её теперь чистит, и счётчик обязан
                // погаснуть сразу, не дожидаясь следующего обмена
                var stuck = registry.ReplPending.List(org.Id, peer.ServerId);
                result.Add(new ReplicationStatusDto
                {
                    OrgId = org.Id,
                    OrgCode = org.Code,
                    OrgName = org.Name,
                    ServerId = peer.ServerId,
                    ServerCode = peer.Code,
                    ServerName = peer.ServerName,
                    IntervalSec = interval,
                    RetrySec = retry,
                    // сеанса нет, а в базе «running» — след оборванного сеанса (T-174):
                    // показываем «сеанса нет», иначе строка врёт до перезапуска сервера
                    Status = running
                        ? ReplicationStatuses.Running
                        : state.Status == ReplicationStatuses.Running
                            ? ReplicationStatuses.Idle
                            : state.Status,
                    // во время репликации отсчёт остановлен, поэтому времени «до следующей» нет
                    SecondsLeft = running || state.NextRunAt is null
                        ? null
                        : Math.Max(0, (int)(state.NextRunAt.Value - now).TotalSeconds),
                    Percent = progress?.Percent ?? 0,
                    Phase = progress?.Phase ?? "",
                    LastError = state.LastError,
                    LastOkAt = state.LastOkAt,
                    LastRunAt = state.LastRunAt,
                    Conflicts = state.Conflicts,
                    LastConflict = state.LastConflict,
                    Received = state.Received,
                    Sent = state.Sent,
                    FilesReceived = state.FilesReceived,
                    FilesSent = state.FilesSent,
                    BytesReceived = state.BytesReceived,
                    BytesSent = state.BytesSent,
                    FileConflicts = registry.FileSync.Count(peer.ServerId),
                    Pending = stuck.Count,
                    LastPending = state.LastPending,
                    // очередь повтора построчно и причина «звоним не мы» (T-20-S1): без них
                    // экран диагностики показывал вечные «Ждут повтора строк 3» без единого
                    // способа понять, что это за строки и почему попытки не помогают
                    PendingRows = stuck
                        .Select(row => new ReplPendingRowDto
                        {
                            Scope = row.Scope,
                            Table = row.Table,
                            Pk = row.Pk,
                            Reason = row.Reason,
                            Tries = row.Tries,
                        }).ToList(),
                    CannotCall = registry.Servers.Get(peer.ServerId) is { } node
                        ? replication.CannotCall(node) ?? ""
                        : "",
                    PullCursor = state.PullCursor,
                    PushCursor = state.PushCursor,
                    ServerPullCursor = state.ServerPullCursor,
                    ServerPushCursor = state.ServerPushCursor,
                    OutgoingLag = Lag(registry, org.Id, peer.ServerId, state),
                });
            }
        }
        return result;
    }

    /// <summary>Сколько наших изменений партнёр ещё не забрал — отставание реплики.</summary>
    private static long Lag(OrgRegistry registry, string orgId, string peerId, ReplicationState state)
    {
        try
        {
            var context = registry.ById(orgId);
            return context is null
                ? 0
                : ReplicationService.Outgoing(context.Db, state.PushCursor, peerId);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return 0;   // база занята сеансом — отставание покажем в следующий раз
        }
    }

    /// <summary>
    /// Ручной пуск репликации с одним сервером (кнопка с круговой стрелкой, todo43).
    /// «При ручном пуске всё переустанавливается точно так же»: сеанс идёт по организациям
    /// по очереди, а отсчёт до следующей репликации заводится заново после его окончания.
    /// Сам сеанс — в фоне: держать на нём запрос браузера нельзя, первичная репликация
    /// занимает минуты. Ошибки видны в состоянии пары (колонка «статус репликации»).
    /// </summary>
    private static async Task StartReplicationAsync(OrgRegistry registry, SecretStore secrets,
        ReplicationService replication, string serverId, string startedBy, string? actorId)
    {
        var pairs = ReplicationPairs(registry, replication, serverId);
        if (pairs.Count == 0)
        {
            // ПОДКЛЮЧЕНИЕ НЕ ДОВЕДЕНО ДО КОНЦА (T-153). Заявка подана, человек на дирижёре её,
            // может быть, уже подтвердил — но пока подавший сервер не спросил решение, он
            // не завёл у себя организацию, и реплицировать ему нечего. Человек в этот момент
            // жмёт «запустить репликацию» и получает «он не подключён ни к одной вашей
            // организации» — про сервер, который только что подключил. Поэтому спрашиваем
            // решение сами: это ровно то, что делает кнопка «Узнать решение», и подключение
            // на том же нажатии доводится до конца (токены, ключ организации, сама организация)
            await FinishJoinAsync(registry, secrets, serverId, actorId);
            pairs = ReplicationPairs(registry, replication, serverId);
        }
        if (pairs.Count == 0)
        {
            throw new ArgumentException(
                Loc.T("msg.apiEndpoints.28"));
        }
        // РУЧНОЙ ПУСК НЕ МОЛЧИТ (T-174). Сеанс с занятой парой не начинается — так и должно
        // быть, — но человек об этом не узнавал: кнопка отвечала «репликация запущена»,
        // и дальше не происходило ничего. Теперь занятость называется словами
        var busy = pairs
            .Where(pair => replication.IsRunning(pair.Org.Id, pair.Peer!.ServerId))
            .Select(pair => $"«{pair.Org.Name}»: "
                            + replication.Progress(pair.Org.Id, pair.Peer!.ServerId)?.Describe())
            .ToList();
        if (busy.Count == pairs.Count)
        {
            throw new ArgumentException(
                Loc.T("msg.apiEndpoints.29", string.Join("; ", busy)));
        }
        _ = Task.Run(async () =>
        {
            foreach (var (org, peer) in pairs)
            {
                try
                {
                    await replication.ReplicateAsync(org, peer!, startedBy);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Репликация {Org} ↔ {Server}: сеанс не удался",
                        org.Code, peer!.Code);
                }
            }
        });
    }

    /// <summary>Пары «организация — партнёр» этого сервера с одним конкретным сервером.</summary>
    private static List<(Organization Org, OrgServer? Peer)> ReplicationPairs(OrgRegistry registry,
        ReplicationService replication, string serverId) =>
        registry.Orgs.List(includeInactive: false)
            .Select(org => (Org: org, Peer: replication.Peers(org.Id)
                .FirstOrDefault(p => p.ServerId == serverId)))
            .Where(pair => pair.Peer is not null)
            .ToList();

    /// <summary>
    /// ДОВЕСТИ ПОДКЛЮЧЕНИЕ ДО КОНЦА ПЕРЕД РЕПЛИКАЦИЕЙ (T-153): спросить у дирижёра решение
    /// по своей заявке и, если она подтверждена, завести организацию у себя.
    ///
    /// Заявка ещё ждёт решения (или её отклонили) — объясняем это словами: «не подключён
    /// ни к одной организации» про сервер, заявку от которого только что приняли, человека
    /// сбивает с толку — он видит себя исполнителем на дирижёре и считает, что всё готово.
    /// </summary>
    private static async Task FinishJoinAsync(OrgRegistry registry, SecretStore secrets,
        string serverId, string? actorId)
    {
        var target = registry.Servers.Get(serverId)
                     ?? throw new ArgumentException(Loc.T("msg.apiEndpoints.5"));
        if (target.JoinStatus.Length == 0)
        {
            return;   // заявки этому серверу мы не подавали — репликации и правда нет
        }
        var status = await ClusterJoinFlow.PollAsync(registry, secrets, target, actorId);
        if (status.Status == OrgServerStatus.Active && status.Token.Length > 0)
        {
            return;   // подключение доведено до конца, пары появились
        }
        throw new ArgumentException(status.Status == OrgServerStatus.Rejected
            ? Loc.T("msg.apiEndpoints.30", target.Name)
            : Loc.T("msg.apiEndpoints.31", target.Name));
    }

    /// <summary>Форма аккаунта (ТЗ п. 2.14) → входные данные сервиса.</summary>
    private static AccountSaveInput ToAccountInput(AccountSaveDto dto) => new()
    {
        Name = dto.Name,
        Email = dto.Email,
        Phone = dto.Phone,
        IsActive = dto.IsActive,
        Password = dto.Password,
        Role = dto.Role,
    };

    /// <summary>Список подкаталогов для диалога выбора папки; пустой путь — корни (диски).</summary>
    private static DirListDto ListDirs(string? path, bool withFiles = false)
    {
        // «~/…» набирают руками в поле пути диалога (на Linux/macOS так пишут всё, T-135):
        // без раскрытия получился бы каталог «~» рядом с приложением
        path = AI2P.Core.PathHome.Expand(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            var roots = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name)
                : new[] { "/" }.AsEnumerable();
            return new DirListDto
            {
                Path = "",
                Parent = null,
                Dirs = roots.Select(r => new DirEntryDto { Name = r, Path = r }).ToList(),
            };
        }

        var full = Path.GetFullPath(path);
        // КАТАЛОГА МОГЛО НЕ СТАТЬ (T-273): начальный каталог теперь приходит из памяти
        // прошлого выбора и из папки проекта, а и то и другое живёт дольше самого каталога.
        // Пустой список с недостижимым путём в поле — это тупик; поднимаемся к ближайшему
        // существующему предку, а если и его нет — показываем диски.
        if (!Directory.Exists(full))
        {
            var up = Path.GetDirectoryName(full);
            while (up is { Length: > 0 } && !Directory.Exists(up))
            {
                up = Path.GetDirectoryName(up);
            }
            if (string.IsNullOrEmpty(up))
            {
                return ListDirs(null, withFiles);
            }
            full = up;
        }
        var dirs = new List<DirEntryDto>();
        var files = new List<DirEntryDto>();
        var truncated = false;
        try
        {
            dirs = new DirectoryInfo(full).EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new DirEntryDto { Name = d.Name, Path = d.FullName })
                .ToList();
            if (withFiles)
            {
                // предел тот же, что у списка файлов папки проекта: список рисуется целиком,
                // а в каталоге бывают десятки тысяч записей
                foreach (var file in new DirectoryInfo(full).EnumerateFiles()
                             .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                             .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (files.Count >= AI2P.Core.ProjectFiles.Limit)
                    {
                        truncated = true;
                        break;
                    }
                    files.Add(new DirEntryDto
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        IsImage = AI2P.Core.ProjectFiles.IsImage(file.Name),
                        Size = file.Length,
                    });
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // каталог без прав чтения или недоступен — показываем пустым
        }
        return new DirListDto
        {
            Path = full,
            Parent = Path.GetDirectoryName(full), // null у корня диска — шаг «вверх» вернёт список дисков
            Dirs = dirs,
            Files = files,
            Truncated = truncated,
        };
    }

    /// <summary>
    /// Абсолютный путь файла внутри папки проекта (ТЗ п. 2.7); null — нет проекта/папки либо
    /// путь выходит за каталог проекта (ТЗ гл. 12, todo19). Каталог ПЕР-СЕРВЕРНЫЙ (гл. 6,
    /// этап 42): подставляется для того сервера, на котором выполняется запрос.
    /// Пустой путь файлом не считается — сама папка проекта это каталог.
    /// </summary>
    private static string? ResolveProjectFile(AI2P.Core.Entities.Project? project, string relPath) =>
        string.IsNullOrWhiteSpace(relPath) ? null : ResolveProjectPath(project, relPath);

    /// <summary>
    /// То же, но для КАТАЛОГА тоже (T-264): пустой путь — сама папка проекта. Разбор пути
    /// и проверка «не вышли ли за край» живут в <see cref="ProjectFiles"/> — одни на отдачу
    /// файла, на список выбора и на превью.
    /// </summary>
    private static string? ResolveProjectPath(AI2P.Core.Entities.Project? project, string? relPath) =>
        ProjectFiles.Resolve(project?.FolderPath, relPath);

    /// <summary>
    /// Пределы кадра датасета LoRA из настроек приложения (T-12-S1). Собираются в одном
    /// месте: их читают и форма добавления кадра (чтобы сжать картинку в браузере), и
    /// проверка присланного — разойтись двум чтениям одного и того же нельзя.
    /// </summary>
    private static LoraImageLimitsDto LoraLimits(Ai2pConfig config) => new()
    {
        MaxWidth = config.Lora.ImageMaxWidth > 0 ? config.Lora.ImageMaxWidth : 1024,
        MaxHeight = config.Lora.ImageMaxHeight > 0 ? config.Lora.ImageMaxHeight : 1024,
        MaxKb = config.Lora.ImageMaxKb > 0 ? config.Lora.ImageMaxKb : 2048,
        Format = config.Lora.ImageFormat.Trim().ToLowerInvariant() == "jpeg" ? "jpeg" : "png",
        PadColor = PadColor(config),
    };

    /// <summary>Цвет полей при пережатии кадра (T-57-S0) в виде «#RRGGBBAA»: мусор в настройке
    /// не должен ломать пережатие — умолчание белый с полной прозрачностью.</summary>
    private static string PadColor(Ai2pConfig config) =>
        PadColorOf(config.Lora.ImagePadColor, "#FFFFFF00");

    /// <summary>Цвет к виду «#RRGGBBAA»; не разобрали — остаётся <paramref name="fallback"/>.
    /// Шесть знаков считаются непрозрачным цветом: так его пишут везде, кроме нашей настройки.</summary>
    private static string PadColorOf(string? value, string fallback)
    {
        var digits = (value ?? "").Trim().TrimStart('#');
        if ((digits.Length == 6 || digits.Length == 8) && digits.All(Uri.IsHexDigit))
        {
            return "#" + (digits.Length == 6 ? digits + "FF" : digits).ToUpperInvariant();
        }
        return fallback;
    }

    /// <summary>
    /// Настройки контроля картинок НОВОГО датасета (T-274) — снимок общих настроек
    /// приложения. Дальше датасет живёт своей жизнью: у разных моделей разные требования
    /// к кадрам, и общая настройка на всё приложение означала бы «пересобери датасет,
    /// когда сменишь модель».
    /// </summary>
    private static LoraDatasetLimits DefaultDatasetLimits(Ai2pConfig config)
    {
        var common = LoraLimits(config);
        return new LoraDatasetLimits
        {
            MaxWidth = common.MaxWidth,
            MaxHeight = common.MaxHeight,
            MaxKb = common.MaxKb,
            Format = common.Format,
        }.Sane();
    }

    /// <summary>Пределы датасета в том виде, в каком их проверяет добавление кадра.</summary>
    private static LoraImageLimitsDto LoraLimitsDto(LoraDatasetLimits limits) => new()
    {
        MaxWidth = limits.MaxWidth,
        MaxHeight = limits.MaxHeight,
        MaxKb = limits.MaxKb,
        Format = limits.Format,
    };

    /// <summary>Датасет наружу: сам объект, число кадров и разобранные настройки —
    /// разбирать JSON в разметке формы нечем.</summary>
    private static LoraDatasetDto DatasetDto(ObjectItem dataset, ObjectItem? owner) => new()
    {
        Id = dataset.Id,
        DisplayId = dataset.DisplayId,
        Name = dataset.Name,
        IsCurrent = owner?.CurrentDatasetId == dataset.Id,
        // число детей датасета уже посчитано чтением (Datasets/Get), а у только что
        // заведённого их и нет
        Frames = dataset.ChildCount,
        Limits = LoraDatasetLimitsDto.Of(LoraDatasetLimits.Parse(dataset.DatasetJson)),
    };

    /// <summary>
    /// Настройки почты наружу (T-272). Пароль НЕ отдаётся: только признак «задан» и путь
    /// файла секрета — по нему значение кладут руками там, где формой пользоваться неудобно
    /// (та же логика, что у ключей API моделей, T-234).
    /// </summary>
    private static MailSettingsDto MailDto(Ai2pConfig config, AI2P.Connectors.SecretStore secrets)
    {
        var options = config.Mail.ToOptions();
        return new MailSettingsDto
        {
            Host = config.Mail.Host,
            Port = config.Mail.Port,
            UseSsl = config.Mail.UseSsl,
            User = config.Mail.User,
            From = config.Mail.From,
            FromName = config.Mail.FromName,
            HasPassword = secrets.Has(options.PasswordRef),
            PasswordFile = secrets.LocationOf(options.PasswordRef),
        };
    }

    /// <summary>Имя файла кадра: только безобидные знаки и наше расширение. Имя приходит
    /// из формы, а ложится оно в папку проекта — «..\..\» здесь недопустимо.</summary>
    private static string SafeFileName(string name, string ext)
    {
        var bare = Path.GetFileNameWithoutExtension((name ?? "").Trim());
        var text = new StringBuilder();
        foreach (var ch in bare)
        {
            text.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }
        var result = text.ToString().Trim('_');
        return (result.Length == 0 ? "frame" : result) + ext;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // исходник не убрался — это мусор в хранилище, а не повод отказать в добавлении
        }
    }

    /// <summary>
    /// АДРЕСА ОСТАЛЬНЫХ СЕРВЕРОВ ОРГАНИЗАЦИИ (T-13-S0) — для показа Markdown: ссылка на файл,
    /// написанная на соседнем сервере, открывается через ЭТОТ сервер, а он принесёт файл сам.
    /// Свой сервер и неподтверждённые заявки в список не попадают.
    /// </summary>
    private static List<string> PeerBaseUrls(OrgRegistry registry, OrgContext ctx)
    {
        var result = new List<string>();
        foreach (var link in registry.Servers.ServersOf(ctx.Org.Id, includeRequests: false))
        {
            if (link.Status != OrgServerStatus.Active)
            {
                continue;
            }
            if (registry.Servers.Get(link.ServerId) is { IsLocal: false } node)
            {
                result.Add(node.Url);
            }
        }
        return result;
    }

    /// <summary>
    /// ФАЙЛ У СОСЕДА ПО КЛАСТЕРУ (T-13-S0). Здесь его нет — значит он лежит на том сервере,
    /// где работал агент: задание выполняется там, где лежат файлы задачи, а папка проекта
    /// не реплицируется. Содержимое переписывается в свой ответ ПОТОКОМ, на диск не ложится:
    /// копия здесь никому не нужна, а файл бывает и гигабайтным.
    ///
    /// Ни у кого нет — обычный 404, как и было: у ссылки на несуществующий файл ответ прежний.
    /// Перемотка (Range) через сосед пока не проходит — видео с чужого сервера играется
    /// с начала.
    /// </summary>
    private static async Task<IResult> FromPeerAsync(OrgContext ctx, RemoteFileService remote,
        string kind, string projectId, string path, bool download)
    {
        var file = await remote.FetchAsync(ctx.Org, kind, projectId, path);
        if (file is null)
        {
            return Results.NotFound();
        }
        var contentType = file.ContentType.Length > 0 ? file.ContentType : ContentTypeOf(path);
        var name = download ? Path.GetFileName(path.Replace('\\', '/')) : null;
        return Results.Stream(async stream =>
        {
            using (file)
            {
                await file.CopyToAsync(stream);
            }
        }, contentType, name);
    }

    /// <summary>
    /// Путь файла НА ЭТОМ СЕРВЕРЕ по виду ссылки (T-13-S0): <see cref="FileLinks.Raw"/> —
    /// каталог данных организации, <see cref="FileLinks.Project"/> — папка проекта. null —
    /// вид не тот, путь уводит за край каталога либо папки проекта здесь нет. Разбор один
    /// на оба входа — обычный запрос браузера и запрос соседа по кластеру: двум проверкам
    /// «не вышли ли за край» расходиться нельзя.
    /// </summary>
    internal static string? LocalFilePath(OrgContext ctx, string kind, string projectId, string path)
    {
        if (kind == FileLinks.Raw)
        {
            return ctx.Files.IsInside(path) ? ctx.Files.Abs(path) : null;
        }
        if (kind != FileLinks.Project)
        {
            return null;
        }
        // папка проекта, а нет её (или файла в ней) — копия, приехавшая репликацией
        return ResolveProjectFile(ctx.Projects.Get(projectId), path) is { } own && File.Exists(own)
            ? own
            : SharedProjectFile(ctx, projectId, path);
    }

    /// <summary>
    /// КОПИЯ ФАЙЛА ПАПКИ ПРОЕКТА, ПРИЕХАВШАЯ РЕПЛИКАЦИЕЙ (T-13-S0, повторный заход); null —
    /// такой копии здесь нет. Раскладывает копии сервер-ВЛАДЕЛЕЦ файла
    /// (<see cref="Org.LinkedFileShare"/>), а развозит их обычная файловая репликация — тем
    /// сеансом, который и так идёт. Нужно это затем, что звонок соседу (первая редакция
    /// T-13-S0) проходит не всегда: рядовой сервер часто стоит за NAT и назван петлевым
    /// адресом, и дирижёр до него не дозванивается вовсе.
    /// </summary>
    internal static string? SharedProjectFile(OrgContext ctx, string projectId, string path)
    {
        var rel = Org.LinkedFileShare.RelOf(projectId, path);
        if (rel is null || !ctx.Files.IsInside(rel))
        {
            return null;
        }
        var abs = ctx.Files.Abs(rel);
        return File.Exists(abs) ? abs : null;
    }

    internal static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        // видео и аудио — для показа в MD-превью (ТЗ v1.28, todo30)
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".ogv" or ".ogg" => "video/ogg",
        ".mov" => "video/quicktime",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".md" or ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    /// <summary>Отображаемое имя ссылки для списка «все файлы» (todo30): имя файла из
    /// path= (uploads/файлы проекта) либо последний сегмент URL; иначе сам URL.</summary>
    private static string LinkName(string url)
    {
        var at = url.IndexOf("path=", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            var encoded = url[(at + "path=".Length)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }
            try
            {
                var name = Path.GetFileName(Uri.UnescapeDataString(encoded).Replace('\\', '/'));
                if (name.Length > 0)
                {
                    return name;
                }
            }
            catch (UriFormatException)
            {
                // нечитаемый path — покажем URL целиком
            }
        }
        var tail = url.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        return tail.Length > 0 && tail.Length < url.Length ? Uri.UnescapeDataString(tail) : url;
    }

    /// <summary>Состояние справочника состояний из DTO формы (ТЗ v1.37).</summary>
    private static TaskStatusDef ToStatusDef(TaskStatusSaveDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Color = dto.Color,
        IsCustom = dto.IsCustom,
        IsActive = dto.IsActive,
        SortOrder = dto.SortOrder,
    };

    /// <summary>Ошибки валидации сервисов → HTTP 400 с текстом.</summary>
    /// <summary>Ошибка правил предметной области → 400 с человекочитаемым текстом.
    /// Тот же обработчик использует раздел кластера (<see cref="ClusterEndpoints"/>).</summary>
    internal static IResult Handle(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
        catch (UnauthorizedAccessException ex)
        {
            // раздел кластера: сервер не опознан по токену (ТЗ гл. 12, этап 43)
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
        // обрыв запроса самим браузером (человек ушёл со страницы) ошибкой не считаем:
        // отвечать уже некому, и в журнале это только шум
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unexpected(ex);
        }
    }

    /// <summary>То же для обработчиков, которые ходят по сети (T-153): пуск репликации
    /// сначала доводит до конца заявку на подключение, а это запрос к дирижёру.</summary>
    internal static async Task<IResult> HandleAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ArgumentException or (InvalidOperationException and not ObjectDisposedException))
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
        // обрыв запроса самим браузером (человек ушёл со страницы) ошибкой не считаем:
        // отвечать уже некому, и в журнале это только шум
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unexpected(ex);
        }
    }

    /// <summary>
    /// НЕОЖИДАННЫЙ ОТКАЗ — 500, но С ТЕКСТОМ И В ЖУРНАЛЕ (T-206-S0).
    ///
    /// Без этой ветки исключение, не попавшее в пару «ArgumentException /
    /// InvalidOperationException», уходило наружу мимо обработчика: ASP.NET отвечал пустым
    /// 500, а <c>ApiClient</c> показывал человеку ровно «500:» — без причины, без места и без
    /// следа в журнале приложения. Жалоба T-206-S0 («открыть архив на дирижёре — 500 в углу
    /// экрана и ничего не происходит») именно об этом: разобраться по такому сообщению
    /// нельзя ни человеку, ни нам.
    ///
    /// Текст ответа — вид и сообщение исключения (это диагностика, а не фраза для перевода);
    /// полная запись со стеком идёт в <c>server.log</c>.
    /// </summary>
    private static IResult Unexpected(Exception ex)
    {
        Serilog.Log.Error(ex, "AI2P: отказ обработчика API: {Kind}", ex.GetType().FullName);
        return Results.Problem($"{ex.GetType().Name}: {ex.Message}",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
