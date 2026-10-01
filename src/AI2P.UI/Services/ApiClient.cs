using AI2P.Core;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;

namespace AI2P.UI.Services;

/// <summary>Ошибка API с человекочитаемым текстом (для снэкбара).</summary>
public sealed class ApiException(string message) : Exception(message);

/// <summary>
/// Типизированный клиент HTTP API. UI работает ТОЛЬКО через него (ТЗ гл. 3, API-first) —
/// та же поверхность, что у будущих плагинов VS Code / Unity.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    /// <summary>
    /// Токен процесса (ТЗ гл. 12, этап 39): UI ходит в собственное API по петле, cookie
    /// браузера в такой запрос не попадает — личность передаётся заголовками.
    /// </summary>
    public string InternalToken { get; init; } = "";

    /// <summary>
    /// Организация этого подключения (ТЗ п. 2.15, этап 40) — код из адреса страницы.
    /// У каждой организации своя БД, поэтому запрос обязан её называть; в разных вкладках
    /// браузера один пользователь может работать в разных организациях.
    /// </summary>
    public string OrgCode { get; init; } = "";

    /// <summary>Аккаунт вошедшего пользователя этого circuit'а; проставляется UiState при входе.</summary>
    public string? AccountId { get; private set; }

    /// <summary>
    /// Вошедший является ещё и администратором сервера (ТЗ гл. 12). Проставляется при создании
    /// клиента — из запроса, которым открыт этот circuit: там cookie администратора ещё видна.
    /// <see cref="SetAccount"/> добавляет к этому признак из личности вошедшего; достаточно
    /// любого из двух — вход администратора живёт в отдельной cookie, и до собственного API,
    /// вызываемого по петле, она сама по себе не доходит.
    /// </summary>
    public bool IsServerAdmin { get; init; }

    /// <param name="isServerAdmin">Вошедший является ещё и администратором сервера
    /// (ТЗ гл. 12): отдельная cookie администратора живёт в браузере и до собственного API,
    /// вызываемого по петле, не доходит — признак передаётся заголовком.</param>
    /// <summary>
    /// СРЕДА ЭТОГО ПОДКЛЮЧЕНИЯ (T-45-S0): код архива, из которого читаются данные, либо пусто —
    /// рабочая среда. Живёт в клиенте, а не в UiState, по той же причине, что и организация:
    /// заголовок обязан стоять на КАЖДОМ запросе, включая те, что делают компоненты сами.
    /// </summary>
    public string ArchiveCode { get; private set; } = "";

    /// <summary>Переключить среду: дальше все запросы идут по данным этого архива (пусто —
    /// рабочая среда). Архивная среда только для чтения — запись сервер отклоняет.</summary>
    public void SetArchive(string? archiveCode)
    {
        ArchiveCode = (archiveCode ?? "").Trim();
        _http.DefaultRequestHeaders.Remove(Ai2pHeaders.Archive);
        if (ArchiveCode.Length > 0)
        {
            _http.DefaultRequestHeaders.Add(Ai2pHeaders.Archive, ArchiveCode);
        }
    }

    public void SetAccount(string? accountId, bool isServerAdmin = false)
    {
        AccountId = accountId;
        _http.DefaultRequestHeaders.Remove(Ai2pHeaders.InternalToken);
        _http.DefaultRequestHeaders.Remove(Ai2pHeaders.InternalAccount);
        _http.DefaultRequestHeaders.Remove(Ai2pHeaders.ServerAdmin);
        _http.DefaultRequestHeaders.Remove(Ai2pHeaders.Org);
        if (OrgCode.Length > 0)
        {
            _http.DefaultRequestHeaders.Add(Ai2pHeaders.Org, OrgCode);
        }
        if (InternalToken.Length > 0 && accountId is { Length: > 0 })
        {
            _http.DefaultRequestHeaders.Add(Ai2pHeaders.InternalToken, InternalToken);
            _http.DefaultRequestHeaders.Add(Ai2pHeaders.InternalAccount, accountId);
            if (isServerAdmin || IsServerAdmin)
            {
                _http.DefaultRequestHeaders.Add(Ai2pHeaders.ServerAdmin, "1");
            }
        }
    }

    // --- организации (ТЗ п. 2.15, этап 40) ---
    public Task<List<Organization>> GetOrgsAsync() => Get<List<Organization>>("api/orgs");

    public Task<List<OrgServer>> GetOrgServersAsync(string orgId) =>
        Get<List<OrgServer>>($"api/orgs/{orgId}/servers");

    /// <summary>Список серверов организации из её формы (ТЗ п. 2.15: связь многие-ко-многим).</summary>
    public Task<List<OrgServer>> SaveOrgServersAsync(string orgId, OrgServersSaveDto dto) =>
        Put<List<OrgServer>>($"api/orgs/{orgId}/servers", dto);

    public Task<Organization> CreateOrgAsync(OrgSaveDto dto) => Post<Organization>("api/orgs", dto);

    public Task<Organization> UpdateOrgAsync(string id, OrgSaveDto dto) =>
        Put<Organization>($"api/orgs/{id}", dto);

    /// <summary>Вступить в организацию ЭТОГО компьютера, в которой вошедший ещё не участник
    /// (T-186-S0): так открывается организация, приехавшая сюда репликацией. Заводит
    /// исполнителя-человека с ролью администратора; доступно хозяину компьютера.</summary>
    public Task<Organization> JoinOrgAsync(string id) =>
        Post<Organization>($"api/orgs/{id}/join", new { });

    /// <summary>Удалить организацию С ЭТОГО сервера насовсем (T-148-S0): записи, база
    /// и каталог. На других серверах она остаётся — удаление местное.</summary>
    public Task DeleteOrgAsync(string id) => Send(HttpMethod.Delete, $"api/orgs/{id}");

    // --- серверы кластера (ТЗ п. 2.15, гл. 6; этап 41) ---

    /// <summary>Все серверы установки; локальный всегда первый (todo41 п. 2).</summary>
    public Task<List<ServerNode>> GetServersAsync() => Get<List<ServerNode>>("api/servers");

    /// <summary>Заявки на подключение, ждущие решения человека (внизу списка серверов, п. 8).
    /// <paramref name="all"/> — «показать все заявки» (T-293): вместе с ними отклонённые.</summary>
    public Task<List<OrgServer>> GetServerRequestsAsync(bool all = false) =>
        Get<List<OrgServer>>("api/servers/requests" + (all ? "?all=true" : ""));

    public Task<ServerNode> CreateServerAsync(ServerSaveDto dto) => Post<ServerNode>("api/servers", dto);

    public Task<ServerNode> UpdateServerAsync(string id, ServerSaveDto dto) =>
        Put<ServerNode>($"api/servers/{id}", dto);

    /// <summary>Удалить запись сервера из списка (T-141): можно только до первой репликации
    /// с ним — тогда освобождается и выданный ему код (S1).</summary>
    public Task DeleteServerAsync(string id) => Send(HttpMethod.Delete, $"api/servers/{id}");

    /// <summary>«На связи ли сервер»: узнаём его по внутреннему ключу, а не по адресу (п. 6).</summary>
    public Task<ServerCheckDto> CheckServerAsync(string id) =>
        Post<ServerCheckDto>($"api/servers/{id}/check", new { });

    /// <summary>Подать заявку на подключение к организации чужого дирижёра (п. 7).</summary>
    public Task<ClusterJoinStatusDto> JoinServerAsync(string id, ServerJoinDto dto) =>
        Post<ClusterJoinStatusDto>($"api/servers/{id}/join", dto);

    /// <summary>Узнать решение по своей заявке (принимает человек на дирижёре).</summary>
    public Task<ClusterJoinStatusDto> GetJoinStatusAsync(string id) =>
        Get<ClusterJoinStatusDto>($"api/servers/{id}/join");

    /// <summary>Решение по входящей заявке: accept / defer / reject (todo41 п. 9).
    /// <paramref name="addMember"/> — завести заявителя участником организации (T-150):
    /// флажок формы подтверждения, по умолчанию да.</summary>
    public Task<OrgServer> DecideServerRequestAsync(string id, string decision,
        bool addMember = true) =>
        Post<OrgServer>($"api/servers/requests/{id}/{decision}",
            new ServerRequestDecisionDto { AddMember = addMember });

    // --- смена дирижёра (T-21-S1) ---

    /// <summary>Можно ли назначить этот сервер дирижёром: правило одно на кнопку и на API,
    /// поэтому форма спрашивает его у сервера, а не считает сама.</summary>
    public Task<ConductorInfoDto> GetConductorInfoAsync(string serverId) =>
        Get<ConductorInfoDto>($"api/servers/{serverId}/conductor");

    /// <summary>Подать заявку «назначить дирижёром» на сервер <paramref name="serverId"/>.</summary>
    public Task<ConductorRequestDto> RequestConductorAsync(string serverId, string note) =>
        Post<ConductorRequestDto>($"api/servers/{serverId}/conductor",
            new ConductorRequestCreateDto { Note = note });

    /// <summary>Заявки на смену дирижёра текущей организации (строка внизу списка серверов).
    /// <paramref name="all"/> — «показать все заявки» (T-293): вместе с решёнными и снятыми;
    /// без него отдаются только ждущие решения.</summary>
    public Task<List<ConductorRequestDto>> GetConductorRequestsAsync(bool all = false) =>
        Get<List<ConductorRequestDto>>("api/servers/conductor-requests" + (all ? "?all=true" : ""));

    /// <summary>Решение по заявке: accept / reject (вторая сторона), withdraw (подавший),
    /// takeover — одностороннее принятие по истечении 12 часов (T-21-S1).</summary>
    public Task<ConductorRequestDto> DecideConductorRequestAsync(string id, string decision) =>
        Post<ConductorRequestDto>($"api/servers/conductor-requests/{id}/{decision}", new { });

    /// <summary>Организации удалённого сервера по логину (ТЗ гл. 11, п. 11.2): выбрать
    /// организацию для подключения, не набирая её код руками.</summary>
    // Списка организаций удалённого сервера по логину больше нет (T-139, доработка): заявка
    // на подключение подаётся анонимно, реквизиты чужого сервера для неё не нужны.

    // --- репликация (ТЗ п. 6.1, гл. 6; этап 43) ---

    /// <summary>Состояние репликации по всем парам «организация — сервер» (todo43).</summary>
    public Task<List<ReplicationStatusDto>> GetReplicationAsync() =>
        Get<List<ReplicationStatusDto>>("api/servers/replication");

    /// <summary>Ручной пуск репликации с сервером (кнопка с круговой стрелкой).</summary>
    public Task<List<ReplicationStatusDto>> ReplicateAsync(string id) =>
        Post<List<ReplicationStatusDto>>($"api/servers/{id}/replicate", new { });

    /// <summary>Интервалы репликации сервера (форма сервера).</summary>
    public Task<ServerNode> SaveReplicationAsync(string id, ReplSettingsDto dto) =>
        Put<ServerNode>($"api/servers/{id}/replication", dto);

    /// <summary>Забыть курсоры пары и перечитать всё заново (экран диагностики).</summary>
    public Task<List<ReplicationStatusDto>> ResetReplicationAsync(string id) =>
        Post<List<ReplicationStatusDto>>($"api/servers/{id}/replication/reset", new { });

    // --- репликация ФАЙЛОВ (ТЗ гл. 6, этап 44; todo44) ---

    /// <summary>Конфликтные файлы пары: изменились за интервал репликации на двух серверах.</summary>
    public Task<List<ReplFileConflictDto>> GetFileConflictsAsync(string serverId) =>
        Get<List<ReplFileConflictDto>>($"api/servers/{serverId}/file-conflicts");

    /// <summary>Решение по конфликту: у файла — принять левый/правый либо переименовать один
    /// из них; у ключа API — принять левый/правый либо ввести новый (<paramref name="value"/>).</summary>
    public Task<ReplFileConflictDto> ResolveFileConflictAsync(string id, string resolution,
        string value = "") =>
        Post<ReplFileConflictDto>($"api/servers/file-conflicts/{id}/resolve",
            new ReplFileResolveDto { Resolution = resolution, Value = value });

    /// <summary>Документ модели из поставляемой документации (ТЗ гл. 14, todo47): кнопка «i»
    /// в форме модели. Язык — интерфейса, с откатом на русский и английский.</summary>
    public Task<ModelDocDto> GetModelDocAsync(string modelId) =>
        Get<ModelDocDto>($"api/models/{modelId}/doc");

    /// <summary>Документ вида источника импорта (ТЗ гл. 14, todo47_2): кнопка «i» в форме
    /// источника. Ключ — вид (<c>trello</c>), поэтому документ доступен и у новой записи.</summary>
    public Task<ModelDocDto> GetImportDocAsync(string kind) =>
        Get<ModelDocDto>($"api/imports/doc/{Uri.EscapeDataString(kind)}");

    /// <summary>Страница поставляемой документации по пути внутри языкового каталога
    /// (T-17-S1): пусто — первая страница (<c>README.md</c>). По таким же путям ходят
    /// внутренние ссылки документов, поэтому переход по ссылке — это тот же вызов.</summary>
    public Task<DocPageDto> GetDocPageAsync(string relPath) =>
        Get<DocPageDto>("api/doc?path=" + Uri.EscapeDataString(relPath));

    /// <summary>Содержимое <c>.repignore</c> папки Common проекта на этом сервере.</summary>
    public Task<RepIgnoreDto> GetRepIgnoreAsync(string projectId) =>
        Get<RepIgnoreDto>($"api/projects/{projectId}/repignore");

    public Task<RepIgnoreDto> SaveRepIgnoreAsync(string projectId, string text) =>
        Put<RepIgnoreDto>($"api/projects/{projectId}/repignore", new RepIgnoreDto { Text = text });

    // --- настройки локального сервера: config.json, только admin сервера (гл. 10, гл. 12) ---
    public Task<ServerSettingsDto> GetLocalServerAsync() =>
        Get<ServerSettingsDto>("api/servers/local");

    public Task SaveLocalServerAsync(ServerSettingsDto dto) =>
        Send(HttpMethod.Put, "api/servers/local", dto);

    /// <summary>Выписать сервер себе сертификат HTTPS сам (T-315): свой удостоверяющий центр
    /// плюс подписанный им серверный сертификат. В ответе — пути готовых файлов.</summary>
    public Task<HttpsSelfCertDto> MakeHttpsCertAsync(HttpsSelfCertDto dto) =>
        Post<HttpsSelfCertDto>("api/servers/local/https/selfsigned", dto);

    /// <summary>Адрес файла своего удостоверяющего центра: его ставят в браузер (T-315).</summary>
    public const string HttpsCaUrl = "api/servers/local/https/ca";

    // --- состояние UI ---
    public Task<StateDto> GetStateAsync() => Get<StateDto>("api/state");

    // --- аккаунты пользователей (ТЗ п. 2.14, этап 39) ---
    public Task<List<Account>> GetAccountsAsync() => Get<List<Account>>("api/accounts");

    public Task<Account> CreateAccountAsync(AccountSaveDto dto) => Post<Account>("api/accounts", dto);

    public Task<Account> UpdateAccountAsync(string id, AccountSaveDto dto) =>
        Put<Account>($"api/accounts/{id}", dto);

    /// <summary>Удалить пользователя (T-140): сервер примет только неактивного и не владельца.</summary>
    public Task DeleteAccountAsync(string id) => Send(HttpMethod.Delete, $"api/accounts/{id}");

    /// <summary>Свой аккаунт: имя и телефон (почта — идентификатор, не меняется).</summary>
    public Task<Account> UpdateSelfAccountAsync(string name, string phone) =>
        Put<Account>("api/account", new AccountSelfDto { Name = name, Phone = phone });

    /// <summary>Смена своего пароля (три поля формы аккаунта).</summary>
    public Task ChangePasswordAsync(string current, string @new) =>
        Send(HttpMethod.Put, "api/account/password",
            new PasswordChangeDto { CurrentPassword = current, NewPassword = @new });

    public Task SetCurrentProjectAsync(string? id) =>
        Send(HttpMethod.Put, "api/state/current-project" + (id is null ? "" : $"/{id}"));

    public Task SetCurrentTeamAsync(string? id) =>
        Send(HttpMethod.Put, "api/state/current-team" + (id is null ? "" : $"/{id}"));

    /// <summary>Сохранить лейаут фреймов и закладок (todo22).</summary>
    public Task SetUiLayoutAsync(string? json) =>
        Send(HttpMethod.Put, "api/state/ui-layout", new UiLayoutDto { Json = json });

    /// <summary>Сохранить состояния представлений — вид/сортировка/фильтр списков (гл. 11).</summary>
    public Task SetUiComponentStatesAsync(string? json) =>
        Send(HttpMethod.Put, "api/state/ui-component-states", new UiLayoutDto { Json = json });

    // --- проекты ---
    public Task<List<Project>> GetProjectsAsync() => Get<List<Project>>("api/projects");

    /// <summary>Один проект (T-273): нужен формам, которым от проекта нужна ПАПКА на этом
    /// сервере — тянуть ради неё весь список проектов организации незачем.</summary>
    public Task<Project> GetProjectAsync(string id) => Get<Project>($"api/projects/{id}");

    public Task<Project> CreateProjectAsync(ProjectCreateDto dto) => Post<Project>("api/projects", dto);

    public Task<Project> UpdateProjectAsync(Project project) =>
        Put<Project>($"api/projects/{project.Id}", project);

    /// <summary>Удалить проект со всем его содержимым — задачами, объектами и опытом
    /// (T-44-S0): «удалить совсем» на форме выбора «перенести в архив или удалить».</summary>
    public Task DeleteProjectAsync(string id) => Send(HttpMethod.Delete, $"api/projects/{id}");

    // --- команды ---
    public Task<List<Team>> GetTeamsAsync() => Get<List<Team>>("api/teams");

    public Task<Team> CreateTeamAsync(Team team) => Post<Team>("api/teams", team);

    public Task<Team> UpdateTeamAsync(Team team) => Put<Team>($"api/teams/{team.Id}", team);

    // --- запуск/остановка работы команды и статусы участников (ТЗ v1.14) ---
    public Task<TeamWorkStatusDto> StartTeamWorkAsync(string teamId) =>
        Post<TeamWorkStatusDto>($"api/teams/{teamId}/work/start", new { });

    public Task<TeamWorkStatusDto> StopTeamWorkAsync(string teamId) =>
        Post<TeamWorkStatusDto>($"api/teams/{teamId}/work/stop", new { });

    public Task<TeamWorkStatusDto> GetTeamWorkAsync(string teamId) =>
        Get<TeamWorkStatusDto>($"api/teams/{teamId}/work");

    // --- запуск/остановка ОДНОГО участника команды (T-129) ---
    public Task<TeamWorkStatusDto> StartTeamMemberAsync(string teamId, string executorId) =>
        Post<TeamWorkStatusDto>($"api/teams/{teamId}/work/start/{executorId}", new { });

    public Task<TeamWorkStatusDto> StopTeamMemberAsync(string teamId, string executorId) =>
        Post<TeamWorkStatusDto>($"api/teams/{teamId}/work/stop/{executorId}", new { });

    // --- исполнители ---
    public Task<List<Executor>> GetExecutorsAsync() => Get<List<Executor>>("api/executors");

    public Task<Executor> CreateExecutorAsync(Executor executor) => Post<Executor>("api/executors", executor);

    public Task<Executor> UpdateExecutorAsync(Executor executor) =>
        Put<Executor>($"api/executors/{executor.Id}", executor);

    // --- справочники ---
    public Task<List<Role>> GetRolesAsync() => Get<List<Role>>("api/roles");

    public Task<List<Skill>> GetSkillsAsync() => Get<List<Skill>>("api/skills");

    public Task<Skill> CreateSkillAsync(string name, string description) =>
        Post<Skill>("api/skills", new Skill { Name = name, Description = description });

    /// <summary>Справочник io formats (ТЗ v1.17) — для деклараций возможностей.</summary>
    public Task<List<IoFormat>> GetIoFormatsAsync() => Get<List<IoFormat>>("api/ioformats");

    // --- справочник состояний задач (ТЗ v1.37, todo34_3): названия в языке приложения ---
    public Task<List<TaskStatusDef>> GetTaskStatusesAsync(string? lang = null, bool includeInactive = false) =>
        Get<List<TaskStatusDef>>("api/task-statuses?includeInactive=" + includeInactive
            + (lang is null ? "" : $"&lang={Uri.EscapeDataString(lang)}"));

    public Task<TaskStatusDef> CreateTaskStatusAsync(TaskStatusSaveDto dto, string lang) =>
        Post<TaskStatusDef>($"api/task-statuses?lang={Uri.EscapeDataString(lang)}", dto);

    public Task<TaskStatusDef> UpdateTaskStatusAsync(string id, TaskStatusSaveDto dto, string lang) =>
        Put<TaskStatusDef>($"api/task-statuses/{id}?lang={Uri.EscapeDataString(lang)}", dto);

    // --- справочник действий агентов (ТЗ v1.21, todo24): тексты в языке приложения ---
    public Task<List<ActionDto>> GetActionsAsync(string lang) =>
        Get<List<ActionDto>>($"api/actions?lang={Uri.EscapeDataString(lang)}");

    public Task<ActionDto> CreateActionAsync(ActionSaveDto dto, string lang) =>
        Post<ActionDto>($"api/actions?lang={Uri.EscapeDataString(lang)}", dto);

    public Task<ActionDto> UpdateActionAsync(string id, ActionSaveDto dto, string lang) =>
        Put<ActionDto>($"api/actions/{id}?lang={Uri.EscapeDataString(lang)}", dto);

    /// <summary>Откатить промпт встроенного действия к значению по умолчанию (todo24).</summary>
    public Task<ActionDto> ResetActionPromptAsync(string id, string lang) =>
        Post<ActionDto>($"api/actions/{id}/reset-prompt?lang={Uri.EscapeDataString(lang)}", new { });

    // --- ПЛАГИНЫ И MCP (T-114-S0): закладка «Настройки → Плагины и MCP» ---
    // Плагин зовётся КОДОМ (editor.shotcut), а не идентификатором записи: записи может ещё
    // не быть вовсе — на диске лежит один манифест, и это законное состояние «объявлен».
    // Язык нужен каждому вызову: названия, описания и подсказки приходят из манифеста,
    // а он хранит тексты по языкам сам (словарей интерфейса плагин не правит)
    public Task<List<PluginListItemDto>> GetPluginsAsync(string lang) =>
        Get<List<PluginListItemDto>>($"api/plugins?lang={Uri.EscapeDataString(lang)}");

    public Task<PluginDetailsDto> GetPluginAsync(string code, string lang) =>
        Get<PluginDetailsDto>($"api/plugins/{Uri.EscapeDataString(code)}?lang={Uri.EscapeDataString(lang)}");

    public Task<PluginListItemDto> PluginActionAsync(string code, string action, string lang) =>
        Post<PluginListItemDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/{action}?lang={Uri.EscapeDataString(lang)}",
            new { });

    // ОКНО УСТАНОВКИ И НАСТРОЙКИ ПЛАГИНА (T-136-S0): состояние спрашивается опросом, пока
    // идёт установка пакета, — как у окна установки модели
    public Task<PluginInstallDto> GetPluginInstallAsync(string code, string lang) =>
        Get<PluginInstallDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/install?lang={Uri.EscapeDataString(lang)}");

    // soft — имя записи софта (T-146-S0): у плагина, ведущего к нескольким программам,
    // каждая ставится и настраивается своей строкой. Пусто — главная программа
    public Task<PluginInstallDto> StartPluginInstallAsync(string code, string lang,
        string soft = "") =>
        Post<PluginInstallDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/install?lang={Uri.EscapeDataString(lang)}"
            + SoftArg(soft),
            new { });

    public Task<PluginInstallDto> CancelPluginInstallAsync(string code, string lang,
        string soft = "") =>
        Post<PluginInstallDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/install/cancel?lang={Uri.EscapeDataString(lang)}"
            + SoftArg(soft),
            new { });

    /// <summary>Размер дистрибутива пакета — запросом к серверу раздачи (несколько секунд сети).</summary>
    public Task<PluginInstallDto> ResolvePluginInstallSizeAsync(string code, string lang,
        string soft = "") =>
        Post<PluginInstallDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/install/resolve?lang={Uri.EscapeDataString(lang)}"
            + SoftArg(soft),
            new { });

    public Task<PluginListItemDto> SetPluginPathAsync(string code, string path, string lang,
        string soft = "") =>
        Put<PluginListItemDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/path?lang={Uri.EscapeDataString(lang)}",
            new PluginPathDto { Path = path, Soft = soft });

    private static string SoftArg(string soft) =>
        soft.Length == 0 ? "" : "&soft=" + Uri.EscapeDataString(soft);

    /// <summary>ОБНОВИТЬ СПИСОК ИНСТРУМЕНТОВ сервера MCP (T-119-S0) — явное действие человека:
    /// ответ показывает, что добавилось и что исчезло.</summary>
    public Task<McpToolsDto> RefreshMcpToolsAsync(string code, string lang) =>
        Post<McpToolsDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/tools?lang={Uri.EscapeDataString(lang)}",
            new { });

    /// <summary>Секрет подключения MCP: уходит в secrets/ ЭТОГО сервера и обратно не
    /// отдаётся никогда. Пустая строка — «убрать секрет».</summary>
    public Task<PluginListItemDto> SetPluginSecretAsync(string code, string value, string lang) =>
        Put<PluginListItemDto>(
            $"api/plugins/{Uri.EscapeDataString(code)}/secret?lang={Uri.EscapeDataString(lang)}",
            new PluginSecretDto { Value = value });

    // --- правила безопасности (ТЗ гл. 12, todo25) ---
    public Task<List<SecurityRule>> GetSecurityRulesAsync(string scope, string? scopeId) =>
        Get<List<SecurityRule>>($"api/security-rules?scope={Uri.EscapeDataString(scope)}"
                                + (scopeId is null ? "" : $"&scopeId={Uri.EscapeDataString(scopeId)}"));

    public Task<SecurityRule> CreateSecurityRuleAsync(SecurityRule rule) =>
        Post<SecurityRule>("api/security-rules", rule);

    public Task<SecurityRule> UpdateSecurityRuleAsync(SecurityRule rule) =>
        Put<SecurityRule>($"api/security-rules/{rule.Id}", rule);

    public Task DeleteSecurityRuleAsync(string id) =>
        Send(HttpMethod.Delete, $"api/security-rules/{id}");

    public Task<IoFormat> CreateIoFormatAsync(string name, string description) =>
        Post<IoFormat>("api/ioformats", new IoFormat { Name = name, Description = description });

    // --- справочник ИИ-моделей (ТЗ п. 2.9) ---
    public Task<List<AiModel>> GetModelsAsync() => Get<List<AiModel>>("api/models");

    public Task<AiModel> CreateModelAsync(AiModel model) => Post<AiModel>("api/models", model);

    public Task<AiModel> UpdateModelAsync(AiModel model) => Put<AiModel>($"api/models/{model.Id}", model);

    /// <summary>Удалить кастомную запись справочника (ТЗ v1.43): файлы модели остаются.</summary>
    public Task DeleteModelAsync(string id) => Send(HttpMethod.Delete, $"api/models/{id}");

    // --- установка локальных моделей (ТЗ v1.40, todo36_3) ---
    public Task<List<ModelInstallStatusDto>> GetModelInstallsAsync() =>
        Get<List<ModelInstallStatusDto>>("api/models/installs");

    public Task<ModelInstallStatusDto> GetModelInstallAsync(string modelId) =>
        Get<ModelInstallStatusDto>($"api/models/{modelId}/install");

    public Task<ModelInstallStatusDto> StartModelInstallAsync(string modelId) =>
        Post<ModelInstallStatusDto>($"api/models/{modelId}/install/start", new { });

    public Task<ModelInstallStatusDto> CancelModelInstallAsync(string modelId) =>
        Post<ModelInstallStatusDto>($"api/models/{modelId}/install/cancel", new { });

    /// <summary>Включить или выключить дополнительную опцию установки (T-190-S0): флажок
    /// «Обучение LoRA». Выбор запоминается на ЭТОМ сервере и не реплицируется.</summary>
    public Task<ModelInstallStatusDto> SetModelInstallOptionAsync(string modelId, string code, bool on) =>
        Post<ModelInstallStatusDto>(
            $"api/models/{modelId}/install/option?code={Uri.EscapeDataString(code)}&on={(on ? "true" : "false")}",
            new { });

    /// <summary>Выяснить размеры дистрибутивов пакетов модели (ТЗ v1.43): запрос к серверам
    /// раздачи, результат кэшируется на сервере.</summary>
    public Task<ModelInstallStatusDto> ResolveModelInstallSizesAsync(string modelId) =>
        Post<ModelInstallStatusDto>($"api/models/{modelId}/install/resolve", new { });

    // --- ключи API облачных моделей (ТЗ v1.42, todo36_5): значение ключа наружу не отдаётся ---
    public Task<ModelKeyStatusDto> GetModelKeyAsync(string modelId) =>
        Get<ModelKeyStatusDto>($"api/models/{modelId}/key");

    public Task<ModelKeyStatusDto> SetModelKeyAsync(string modelId, string value) =>
        Put<ModelKeyStatusDto>($"api/models/{modelId}/key", new ModelKeySaveDto { Value = value });

    // --- вход в Claude CLI (todo96, версия 1.96): сеанс CLI протухает, и войти заново
    // теперь можно из AI2P, а не только в терминале сервера ---
    public Task<ClaudeAuthStatusDto> GetClaudeAuthAsync() =>
        Get<ClaudeAuthStatusDto>("api/models/claude-cli/auth");

    /// <summary>Начать вход: ответ содержит ссылку авторизации, которую надо открыть.</summary>
    public Task<ClaudeLoginStateDto> StartClaudeLoginAsync() =>
        Post<ClaudeLoginStateDto>("api/models/claude-cli/auth/login", new { });

    /// <summary>Дослать код авторизации со страницы входа; успех отпускает задачи,
    /// стоявшие в паузе «ждёт входа».</summary>
    public Task<ClaudeLoginStateDto> SubmitClaudeLoginCodeAsync(string code) =>
        Post<ClaudeLoginStateDto>("api/models/claude-cli/auth/code",
            new ClaudeLoginCodeDto { Code = code });

    public Task<ClaudeLoginStateDto> CancelClaudeLoginAsync() =>
        Post<ClaudeLoginStateDto>("api/models/claude-cli/auth/cancel", new { });

    // --- задачи: фильтр и полнотекстный поиск (ТЗ гл. 5, экран 1) ---
    public Task<List<TaskItem>> GetTasksAsync(string? projectId = null, bool includeTemplates = false,
        bool templatesOnly = false, string? teamId = null, string? executorId = null,
        string? mineId = null, string? status = null,
        DateTime? dueFrom = null, DateTime? dueTo = null, string? search = null,
        int? priorityMin = null, int? priorityMax = null, IEnumerable<string>? servers = null,
        IEnumerable<string>? tags = null)
    {
        var query = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }
        Add("projectId", projectId);
        Add("teamId", teamId);
        Add("executorId", executorId);
        Add("mineId", mineId);
        Add("status", status);
        Add("search", search);
        Add("dueFrom", dueFrom?.ToUniversalTime().ToString("o"));
        Add("dueTo", dueTo?.ToUniversalTime().ToString("o"));
        Add("priorityMin", priorityMin?.ToString());
        Add("priorityMax", priorityMax?.ToString());
        // фильтр по серверам-владельцам (ТЗ гл. 6, этап 42); «none» — задачи без сервера
        Add("servers", servers is null ? null : string.Join(",", servers));
        // фильтр по тэгам (T-222): запятая — разделитель, внутри тэга её не бывает
        Add("tags", tags is null ? null : string.Join(",", tags));
        if (includeTemplates)
        {
            query.Add("includeTemplates=true");
        }
        if (templatesOnly)
        {
            query.Add("templatesOnly=true");
        }
        return Get<List<TaskItem>>("api/tasks" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
    }

    /// <summary>Все тэги организации (T-222): справочника у тэгов нет — список собирается
    /// по задачам и шаблонам. Нужен полю формы задачи, фильтру и веткам «тэги» эксплорера.</summary>
    public Task<List<string>> GetTagsAsync() => Get<List<string>>("api/tags");

    public Task<TaskDetailsDto> GetTaskAsync(string id) => Get<TaskDetailsDto>($"api/tasks/{id}");

    // --- ОБЪЕКТЫ ПРОЕКТА (ТЗ пп. 2.5–2.6; T-259) ---
    //
    // Персонажи, локации, реквизит, стиль, эталонные кадры и адаптеры LoRA. Список всегда
    // по проекту: общего списка объектов у организации нет.

    /// <param name="kinds">Отбор по видам объекта (T-276); пусто — все виды.</param>
    public Task<List<ObjectItem>> GetObjectsAsync(string projectId,
        IEnumerable<string>? tags = null, string? search = null, IEnumerable<string>? kinds = null)
    {
        var query = new List<string> { "projectId=" + Uri.EscapeDataString(projectId) };
        if (tags is not null && tags.Any())
        {
            query.Add("tags=" + Uri.EscapeDataString(string.Join(TaskTags.Separator, tags)));
        }
        if (search is { Length: > 0 })
        {
            query.Add("search=" + Uri.EscapeDataString(search));
        }
        if (kinds is not null && kinds.Any())
        {
            query.Add("kinds=" + Uri.EscapeDataString(string.Join(',', kinds)));
        }
        return Get<List<ObjectItem>>("api/objects?" + string.Join("&", query));
    }

    /// <summary>Тэги объектов ПРОЕКТА (T-259) — своя группа, отдельная от тэгов задач.</summary>
    public Task<List<string>> GetObjectTagsAsync(string projectId) =>
        Get<List<string>>("api/objects/tags?projectId=" + Uri.EscapeDataString(projectId));

    public Task<ObjectItem> GetObjectAsync(string id) => Get<ObjectItem>($"api/objects/{id}");

    /// <summary>Внутренний список объекта: эталонные кадры персонажа, части локации.</summary>
    public Task<List<ObjectItem>> GetObjectChildrenAsync(string id) =>
        Get<List<ObjectItem>>($"api/objects/{id}/children");

    /// <summary>Что уйдёт в модель вместо ссылки на этот объект (T-259).</summary>
    public Task<ObjectPromptDto> GetObjectPromptAsync(string id) =>
        Get<ObjectPromptDto>($"api/objects/{id}/prompt");

    public Task<ObjectItem> CreateObjectAsync(ObjectItem item) =>
        Post<ObjectItem>("api/objects", item);

    public Task<ObjectItem> UpdateObjectAsync(ObjectItem item) =>
        Put<ObjectItem>($"api/objects/{item.Id}", item);

    /// <summary>Перенос объекта по иерархии (T-266): parentId=null — сделать корневым.</summary>
    public Task<ObjectItem> MoveObjectAsync(string id, string? parentId) =>
        Post<ObjectItem>($"api/objects/{id}/parent", new ObjectParentDto { ParentId = parentId });

    public Task DeleteObjectAsync(string id) => Send(HttpMethod.Delete, $"api/objects/{id}");

    /// <summary>Сменить сервер-владелец объекта (T-102-S0): объект уезжает всем поддеревом,
    /// здесь он становится только для чтения.</summary>
    public Task<ObjectItem> ChangeObjectServerAsync(string id, string? serverId) =>
        Post<ObjectItem>($"api/objects/{id}/server", new TaskServerDto { ServerId = serverId });

    // --- РЕДАКТОР LoRA (T-12-S1) ---
    //
    // Датасет — это дети объекта LoRA (вида «эталонный кадр»), поэтому правка подписи и
    // удаление кадра идут обычными UpdateObjectAsync/DeleteObjectAsync. Своего здесь два
    // вызова: добавление кадра (файл ложится в папку проекта и проверяется по настройкам)
    // и список обучений с его запуском.

    /// <summary>Кадры датасета: дети ДАТАСЕТА (T-274); датасет не назван — текущий.</summary>
    public Task<List<ObjectItem>> GetLoraDatasetAsync(string objectId, string? datasetId = null) =>
        Get<List<ObjectItem>>($"api/objects/{objectId}/lora/dataset"
                              + (datasetId is { Length: > 0 } d ? "?datasetId=" + Uri.EscapeDataString(d) : ""));

    // --- ДАТАСЕТЫ (T-274) ---

    /// <summary>Датасеты объекта: сколько в каждом кадров и с какими настройками собран.</summary>
    public Task<List<LoraDatasetDto>> GetLoraDatasetsAsync(string objectId) =>
        Get<List<LoraDatasetDto>>($"api/objects/{objectId}/lora/datasets");

    /// <summary>Текущий датасет объекта, а нет ни одного — завести умолчанием.</summary>
    public Task<LoraDatasetDto> EnsureLoraDatasetAsync(string objectId) =>
        Post<LoraDatasetDto>($"api/objects/{objectId}/lora/datasets/ensure", new { });

    /// <summary>Завести датасет (он сразу становится текущим); пустое имя — умолчание.</summary>
    public Task<LoraDatasetDto> CreateLoraDatasetAsync(string objectId, string name) =>
        Post<LoraDatasetDto>($"api/objects/{objectId}/lora/datasets",
            new LoraDatasetCreateDto { Name = name });

    /// <summary>Выбрать текущий датасет объекта.</summary>
    public Task<ObjectItem> SetCurrentLoraDatasetAsync(string objectId, string datasetId) =>
        Put<ObjectItem>($"api/objects/{objectId}/lora/dataset/current",
            new LoraDatasetPickDto { DatasetId = datasetId });

    /// <summary>Сохранить настройки контроля картинок датасета.</summary>
    public Task<LoraDatasetDto> SaveLoraDatasetLimitsAsync(string datasetId, LoraDatasetLimitsDto dto) =>
        Put<LoraDatasetDto>($"api/objects/lora/datasets/{datasetId}/limits", dto);

    /// <summary>Модели закладки «Модели», из которых есть что подставить в датасет.</summary>
    public Task<List<LoraModelLimitsDto>> GetLoraModelLimitsAsync(string objectId) =>
        Get<List<LoraModelLimitsDto>>($"api/objects/{objectId}/lora/model-limits");

    /// <summary>Добавить кадр: картинка уже обрезана и пережата браузером.</summary>
    public Task<ObjectItem> AddLoraFrameAsync(string objectId, LoraDatasetAddDto dto) =>
        Post<ObjectItem>($"api/objects/{objectId}/lora/dataset", dto);

    /// <summary>Положить исходник кадра в хранилище — из него режут рамкой кропа.</summary>
    public Task<LoraStageResultDto> StageLoraImageAsync(string? url, string? localPath,
        string media = LoraDatasetMedia.Image) =>
        Post<LoraStageResultDto>("api/objects/lora/stage",
            new LoraStageDto { Url = url ?? "", LocalPath = localPath ?? "", Media = media });

    /// <summary>Под какие модели адаптер обучали и чем это кончилось.</summary>
    public Task<List<ObjectLoraModel>> GetLoraModelsAsync(string objectId) =>
        Get<List<ObjectLoraModel>>($"api/objects/{objectId}/lora/models");

    public Task<ObjectLoraModel> AddLoraModelAsync(string objectId, string modelId) =>
        Post<ObjectLoraModel>($"api/objects/{objectId}/lora/models",
            new LoraModelAddDto { ModelId = modelId });

    public Task DeleteLoraModelAsync(string loraId) =>
        Send(HttpMethod.Delete, $"api/objects/lora/models/{loraId}");

    /// <summary>Подобрать датасет под требования модели (T-57-S0): подходящий среди
    /// заведённых становится текущим, а нет такого — заводится новый, и в ответе приходит
    /// список кадров, которые форме предстоит пережать.</summary>
    public Task<LoraDatasetFitDto> FitLoraDatasetAsync(string loraId, string? datasetId) =>
        Post<LoraDatasetFitDto>($"api/objects/lora/models/{loraId}/dataset/fit",
            new LoraDatasetPickDto { DatasetId = datasetId ?? "" });

    /// <summary>О чём спросить перед обучением (T-274): сменился ли датасет и не крупнее ли
    /// его настройки объявленных моделью.</summary>
    public Task<LoraTrainCheck> CheckLoraTrainAsync(string loraId, string? datasetId = null) =>
        Get<LoraTrainCheck>($"api/objects/lora/models/{loraId}/train/check"
                            + (datasetId is { Length: > 0 } d ? "?datasetId=" + Uri.EscapeDataString(d) : ""));

    /// <summary>ЧЕМ ЗАПУСКАТЬ ОБУЧЕНИЕ (T-157-S0): годные шаблоны задач, подходящий исполнитель
    /// «авто ПО» и плагин-тренер этой модели — одним ответом.</summary>
    public Task<LoraTrainOptionsDto> GetLoraTrainOptionsAsync(string loraId) =>
        Get<LoraTrainOptionsDto>($"api/objects/lora/models/{loraId}/train/options");

    /// <summary>Завести исполнителя «авто ПО» из записи «Плагинов и MCP» (T-157-S0).</summary>
    public Task<Executor> CreateLoraTrainExecutorAsync(string loraId) =>
        Post<Executor>($"api/objects/lora/models/{loraId}/train/executor", new { });

    /// <summary>Завести узел шаблона с уже подставленным исполнителем (T-157-S0).</summary>
    public Task<TaskItem> CreateLoraTrainTemplateAsync(string loraId, string executorId) =>
        Post<TaskItem>($"api/objects/lora/models/{loraId}/train/template"
                       + "?executorId=" + Uri.EscapeDataString(executorId), new { });

    /// <summary>Создать задачу обучения по шаблону; run — ответ «создать и запустить»
    /// (T-157-S0). Прямого запуска обучения у клиента больше нет: путь один — через задачу.</summary>
    public Task<LoraTrainTaskDto> CreateLoraTrainTaskAsync(string loraId, string templateId,
        string? datasetId, bool run, bool force, bool confirmDataset, bool confirmLimits) =>
        Post<LoraTrainTaskDto>($"api/objects/lora/models/{loraId}/train/task",
            new LoraTrainTaskCreateDto
            {
                TemplateId = templateId,
                DatasetId = datasetId ?? "",
                Run = run,
                Force = force,
                ConfirmDataset = confirmDataset,
                ConfirmLimits = confirmLimits,
            });

    public Task CancelLoraTrainAsync(string loraId) =>
        Send(HttpMethod.Post, $"api/objects/lora/models/{loraId}/train/cancel");

    /// <summary>Задачи, над которыми ИИ работает прямо сейчас (T-187): все проекты и серверы,
    /// фильтра у представления нет.</summary>
    public Task<List<AiWorkItemDto>> GetAiWorkAsync() =>
        Get<List<AiWorkItemDto>>("api/tasks/ai-work");

    /// <summary>Диаграмма подзадач (T-132-S0): поддерево задачи по дорожкам исполнителей
    /// и по времени — всё считает сервер одним вызовом.</summary>
    /// <param name="alone">Корень нужен и без подзадач (T-353-S0) — так диаграмму просит
    /// представление «Диаграммы в работе»: запущенная одиночная задача тоже рисуется.</param>
    public Task<TaskDiagramDto> GetTaskDiagramAsync(string id, bool alone = false) =>
        Get<TaskDiagramDto>($"api/tasks/{id}/diagram" + (alone ? "?alone=true" : ""));

    public Task<TaskItem> CreateTaskAsync(TaskSaveDto dto) => Post<TaskItem>("api/tasks", dto);

    public Task<TaskItem> UpdateTaskAsync(TaskSaveDto dto) => Put<TaskItem>($"api/tasks/{dto.Task.Id}", dto);

    public Task<TaskItem> ChangeTaskStatusAsync(string id, string status) =>
        Post<TaskItem>($"api/tasks/{id}/status", new StatusChangeDto { Status = status });

    /// <summary>Перенос задачи по иерархии (todo37): parentId=null — сделать корневой.</summary>
    public Task<TaskItem> MoveTaskAsync(string id, string? parentId) =>
        Post<TaskItem>($"api/tasks/{id}/parent", new TaskParentDto { ParentId = parentId });

    /// <summary>Смена исполнителя перетаскиванием на дорожку диаграммы (T-135-S0):
    /// пусто — задача остаётся без исполнителя.</summary>
    public Task<TaskItem> ChangeTaskExecutorAsync(string id, string? executorId) =>
        Post<TaskItem>($"api/tasks/{id}/executor", new TaskExecutorDto { ExecutorId = executorId });

    /// <summary>Сменить сервер-владелец задачи (ТЗ гл. 6, этап 42): после смены задача
    /// правится на новом сервере; номер задачи не меняется.</summary>
    public Task<TaskItem> ChangeTaskServerAsync(string id, string? serverId) =>
        Post<TaskItem>($"api/tasks/{id}/server", new TaskServerDto { ServerId = serverId });

    /// <summary>Кого остановит смена организации (T-188): идущие и ждущие задания ИИ-агентов
    /// и команды с живыми подключениями. Организация — та, что в адресе запроса.</summary>
    public Task<OrgAgentsDto> GetOrgAgentsAsync() => Get<OrgAgentsDto>("api/org/agents");

    /// <summary>Принудительно остановить агентов организации (T-188): снять задания,
    /// разорвать подключения команд и выгрузить их локальные серверы моделей.</summary>
    public Task<OrgStopResultDto> StopOrgAgentsAsync() =>
        Post<OrgStopResultDto>("api/org/agents/stop", new { });

    /// <summary>Серверы текущей организации для фильтров и выбора владельца (ТЗ гл. 6).</summary>
    public Task<List<OrgServerBriefDto>> GetOrgServersBriefAsync() =>
        Get<List<OrgServerBriefDto>>("api/org/servers");

    /// <summary>Каталоги проекта на других серверах — только для чтения (ТЗ гл. 6, этап 42).</summary>
    public Task<List<ProjectServerDto>> GetProjectServersAsync(string projectId) =>
        Get<List<ProjectServerDto>>($"api/projects/{projectId}/servers");

    /// <summary>Передать дирижёру задачи сервера, выведенного из кластера (ТЗ гл. 6, этап 42).</summary>
    public Task<TakeoverResultDto> TakeOverServerTasksAsync(string serverId) =>
        Post<TakeoverResultDto>($"api/servers/{serverId}/takeover", new { });

    public Task DeleteTaskAsync(string id) => Send(HttpMethod.Delete, $"api/tasks/{id}");

    // --- расписания запуска задач (ТЗ п. 2.12) ---
    public Task<List<Schedule>> GetSchedulesAsync() => Get<List<Schedule>>("api/schedules");

    public Task<Schedule> CreateScheduleAsync(Schedule schedule) =>
        Post<Schedule>("api/schedules", schedule);

    public Task<Schedule> UpdateScheduleAsync(Schedule schedule) =>
        Put<Schedule>($"api/schedules/{schedule.Id}", schedule);

    public Task DeleteScheduleAsync(string id) => Send(HttpMethod.Delete, $"api/schedules/{id}");

    /// <summary>Срабатывания расписаний в интервале — календарь (v1.36).</summary>
    public Task<List<ScheduleOccurrenceDto>> GetScheduleOccurrencesAsync(DateTime from, DateTime to) =>
        Get<List<ScheduleOccurrenceDto>>(
            $"api/schedules/occurrences?from={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&to={Uri.EscapeDataString(to.ToString("O"))}");

    /// <summary>Просроченные срабатывания расписаний (пока приложение не работало).</summary>
    public Task<List<ScheduleOverdueDto>> GetScheduleOverdueAsync() =>
        Get<List<ScheduleOverdueDto>>("api/schedules/overdue");

    public Task<TaskItem> RunScheduleOverdueAsync(string scheduleId) =>
        Post<TaskItem>($"api/schedules/overdue/{scheduleId}/run", new { });

    public Task DismissScheduleOverdueAsync(string scheduleId) =>
        Send(HttpMethod.Post, $"api/schedules/overdue/{scheduleId}/dismiss");

    // --- АРХИВЫ ОРГАНИЗАЦИИ (T-43-S0) ---
    // Реестр архивов общий на кластер, а состояние (открыт/закрыт/удалён) у каждого сервера
    // своё: список приходит с состоянием ЭТОГО сервера, а открыть/закрыть/удалить действуют
    // только здесь. Создание архива вдобавок требует дирижёра — отказ приходит с сервера

    public Task<List<Archive>> GetArchivesAsync() => Get<List<Archive>>("api/archives");

    public Task<Archive> CreateArchiveAsync(string code, string name, string rulesMode) =>
        Post<Archive>("api/archives",
            new ArchiveCreateDto { Code = code, Name = name, RulesMode = rulesMode });

    /// <summary>Открыть архив на ЭТОМ сервере — распаковать .zip в каталог.</summary>
    public Task<Archive> OpenArchiveAsync(string id) =>
        Post<Archive>($"api/archives/{id}/open", new { });

    /// <summary>Закрыть архив на ЭТОМ сервере — упаковать каталог в .zip.</summary>
    public Task<Archive> CloseArchiveAsync(string id) =>
        Post<Archive>($"api/archives/{id}/close", new { });

    /// <summary>Удалить архив С ЭТОГО СЕРВЕРА; запись реестра остаётся.</summary>
    public Task DeleteArchiveAsync(string id) => Send(HttpMethod.Delete, $"api/archives/{id}");

    /// <summary>Что осталось неархивированным по правилам архива — этим форма добавления
    /// архива проверяет прежний текущий архив.</summary>
    public Task<List<ArchiveCandidateDto>> GetArchiveCandidatesAsync(string archiveId) =>
        Get<List<ArchiveCandidateDto>>($"api/archives/{archiveId}/candidates");

    /// <summary>
    /// АВТОМАТИЧЕСКАЯ АРХИВАЦИЯ (T-46-S0): отобрать по правилам ТЕКУЩЕГО архива и перенести.
    /// То же самое действие ставится в расписание на дирижёре. В ответе — сводка: сколько
    /// отобрано, перенесено, пропущено и не удалось, плюс готовая фраза для человека.
    /// Отказ («не дирижёр», «текущий архив закрыт») приходит той же сводкой с Ran=false.
    /// </summary>
    public Task<AutoArchiveResultDto> RunAutoArchiveAsync() =>
        Post<AutoArchiveResultDto>("api/archives/auto", new { });

    /// <summary>
    /// ЧТО УЕДЕТ В АРХИВ — те же проверки без переноса (T-42-S0). Этим форма ручной
    /// архивации (T-44-S0) показывает состав до нажатия кнопки, а отказ («задача в работе»,
    /// «шаблон занят расписанием», «текущего архива нет») приходит <see cref="ApiException"/>
    /// с готовым текстом причины — его форма и показывает рядом с недоступным вариантом.
    /// </summary>
    public Task<ArchiveMovableDto> GetArchiveMovableAsync(string target, string id) =>
        Get<ArchiveMovableDto>("api/archives/movable?target=" + Uri.EscapeDataString(target)
                               + "&id=" + Uri.EscapeDataString(id));

    /// <summary>Перенести запись в ТЕКУЩИЙ архив (T-42-S0).</summary>
    public Task MoveToArchiveAsync(string target, string id) =>
        Send(HttpMethod.Post, "api/archives/move", new ArchiveMoveDto { Target = target, Id = id });

    /// <summary>
    /// ВОССТАНОВИТЬ запись из ТЕКУЩЕГО архива обратно в рабочую среду (T-42-S0) — кнопка
    /// «Восстановить» при просмотре архива (T-45-S0). Раздел /api/archives работает с рабочей
    /// средой всегда, поэтому вызов проходит и тогда, когда на экране открыт архив.
    /// </summary>
    public Task RestoreFromArchiveAsync(string target, string id) =>
        Send(HttpMethod.Post, "api/archives/restore",
            new ArchiveMoveDto { Target = target, Id = id });

    /// <summary>
    /// Скачать архив с другого сервера организации (передачу делает T-47-S0). Вызов стоит
    /// здесь потому, что кнопка живёт на моём экране; пока вызова на сервере нет, приходит
    /// внятный отказ, и он показывается человеку.
    /// </summary>
    public Task DownloadArchiveAsync(string id, string serverId) =>
        Send(HttpMethod.Post, $"api/archives/{id}/download", new { serverId });

    // --- ПРАВИЛА АРХИВАЦИИ (T-41-S0) ---
    // Групп правил две, а вызовы одни и те же: пустой archiveId означает ОБЩИЕ правила
    // организации (справочник настроек), заполненный — правила этого архива

    /// <summary>Правила архива; <paramref name="archiveId"/> пуст — общие правила организации.</summary>
    public Task<List<ArchiveRule>> GetArchiveRulesAsync(string? archiveId = null) =>
        Get<List<ArchiveRule>>("api/archives/rules"
            + (archiveId is { Length: > 0 } ? "?archiveId=" + Uri.EscapeDataString(archiveId) : ""));

    public Task<ArchiveRule> CreateArchiveRuleAsync(ArchiveRule rule) =>
        Post<ArchiveRule>("api/archives/rules", rule);

    public Task<ArchiveRule> UpdateArchiveRuleAsync(ArchiveRule rule) =>
        Put<ArchiveRule>($"api/archives/rules/{rule.Id}", rule);

    public Task DeleteArchiveRuleAsync(string id) =>
        Send(HttpMethod.Delete, $"api/archives/rules/{id}");

    /// <summary>
    /// Заменить набор правил архива целиком: «вернуть умолчание» (копия общих правил) на
    /// форме правил и перенос правил при создании архива — это одно и то же действие.
    /// </summary>
    public Task<List<ArchiveRule>> CopyArchiveRulesAsync(string archiveId, string mode,
        string? sourceArchiveId = null) =>
        Post<List<ArchiveRule>>($"api/archives/{archiveId}/rules/copy",
            new ArchiveRulesCopyDto { Mode = mode, SourceArchiveId = sourceArchiveId });

    // --- уведомления пользователя (T-272) ---
    public Task<List<NotificationRule>> GetNotificationRulesAsync() =>
        Get<List<NotificationRule>>("api/notifications");

    public Task<NotificationRule> CreateNotificationRuleAsync(NotificationRule rule) =>
        Post<NotificationRule>("api/notifications", rule);

    public Task<NotificationRule> UpdateNotificationRuleAsync(NotificationRule rule) =>
        Put<NotificationRule>($"api/notifications/{rule.Id}", rule);

    public Task DeleteNotificationRuleAsync(string id) =>
        Send(HttpMethod.Delete, $"api/notifications/{id}");

    /// <summary>Справочник макроподстановок для кнопки в редакторе текстов правила.</summary>
    public Task<List<NotificationMacroDto>> GetNotificationMacrosAsync() =>
        Get<List<NotificationMacroDto>>("api/notifications/macros");

    /// <summary>Настроен ли почтовый сервер: форма показывает причину, по которой письма
    /// не уходят, — иначе «уведомления не приходят» разбирать нечем.</summary>
    public Task<NotificationTransportDto> GetNotificationTransportAsync() =>
        Get<NotificationTransportDto>("api/notifications/transport");

    /// <summary>Пробное письмо; пустой адрес — на свой собственный.</summary>
    public Task<NotificationTestResultDto> SendNotificationTestAsync(string address) =>
        Post<NotificationTestResultDto>("api/notifications/test",
            new NotificationTestDto { Address = address });

    /// <summary>Запуск задачи; force (T-121) — не глядя на остаток лимита исполнителя;
    /// autoChildren (T-274-S0) — ответ переспроса «автоматически выполнять новых потомков»
    /// (null — не трогать пометку задачи).</summary>
    public Task<Job> StartTaskAsync(string id, bool force = false, bool? autoChildren = null)
    {
        var query = new List<string>();
        if (force)
        {
            query.Add("force=true");
        }
        if (autoChildren is { } auto)
        {
            query.Add($"autoChildren={(auto ? "true" : "false")}");
        }
        var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query);
        return Post<Job>($"api/tasks/{id}/start{suffix}", new { });
    }

    /// <summary>
    /// Остаток лимита исполнителя задачи (T-121); null — лимиты не указаны. Ответ ПУСТОЙ
    /// (204), когда лимитов нет, — читается <see cref="GetOrNull{T}"/>, а не <c>Get</c>:
    /// разбор пустого тела как JSON ронял circuit Blazor по кнопке «запустить» (T-125).
    /// </summary>
    public Task<AgentLimitDto?> GetTaskLimitAsync(string id) =>
        GetOrNull<AgentLimitDto>($"api/tasks/{id}/limit");

    /// <summary>Перенести старт задачи на сброс окна лимита (T-121).</summary>
    public Task DeferTaskAsync(string id) => Send(HttpMethod.Post, $"api/tasks/{id}/defer");

    /// <summary>Запуск всей иерархии задачи (T-159): подзадачи снизу вверх по убыванию
    /// приоритета, корневой родитель — последним. Возвращает итог первого прохода очереди.
    /// withErrors (галочка в переспросе, T-186) — перезапускать и вставшие с ошибкой,
    /// withNeedsFix (вторая галочка, T-210) — запускать и задачи в доработке.</summary>
    public Task<HierarchyRunDto> StartHierarchyAsync(string id, bool withErrors = false,
        bool withNeedsFix = false) =>
        Post<HierarchyRunDto>(
            $"api/tasks/{id}/start-hierarchy?withErrors={(withErrors ? "true" : "false")}"
            + $"&withNeedsFix={(withNeedsFix ? "true" : "false")}", new { });

    /// <summary>
    /// Запуск задачи ДРУГОГО сервера (T-196): те же адреса, что у обычного запуска, но задача
    /// чужая — сервер отвечает 202 и заявкой вместо задания. Задание появится у владельца,
    /// когда заявка доедет к нему репликацией.
    /// </summary>
    /// <param name="kind">task / hierarchy (<see cref="RunRequestKinds"/>).</param>
    public Task<RunRequestDto> RequestRunAsync(string id, string kind, bool withErrors = false,
        bool withNeedsFix = false) =>
        Post<RunRequestDto>(kind == RunRequestKinds.Hierarchy
            ? $"api/tasks/{id}/start-hierarchy?withErrors={(withErrors ? "true" : "false")}"
              + $"&withNeedsFix={(withNeedsFix ? "true" : "false")}"
            : $"api/tasks/{id}/start", new { });

    /// <summary>
    /// ОСТАНОВКА работы по задаче (T-263): снимает активное задание, а по просьбе — и очередь
    /// иерархического запуска, в которой задача идёт (иначе очередь запустит её снова).
    /// children — снять задания и у всех работающих задач поддерева. Задача (или корень)
    /// чужого сервера останавливается заявкой, ответ тот же — в нём назван её сервер.
    /// </summary>
    public Task<TaskStopDto> StopTaskAsync(string id, bool hierarchy = false, bool children = false) =>
        Post<TaskStopDto>($"api/tasks/{id}/stop?hierarchy={(hierarchy ? "true" : "false")}"
                          + $"&children={(children ? "true" : "false")}", new { });

    /// <summary>
    /// АВТОЗАПУСК ПОДЗАДАЧ ветки (T-54-S0): on=false — выключить, on=true — вернуть.
    /// Выключенный автозапуск закрывает все автоматические старты поддерева (потомки
    /// выполненной задачи, задачи, ждавшие её как блокирующую, очередь авторазбиения) и
    /// заодно закрывает открытую очередь иерархии; children — снять и идущие задания.
    /// </summary>
    public Task<TaskStopDto> SetAutoStartAsync(string id, bool on, bool children = false) =>
        Post<TaskStopDto>($"api/tasks/{id}/autostart?on={(on ? "true" : "false")}"
                          + $"&children={(children ? "true" : "false")}", new { });

    /// <summary>Заявки на запуск этой задачи, поданные с других серверов (T-196).</summary>
    public Task<List<RunRequestDto>> GetRunRequestsAsync(string id) =>
        Get<List<RunRequestDto>>($"api/tasks/{id}/run-requests");

    /// <summary>Анализ иерархии шаблона (todo31): что переспрашивать перед созданием.</summary>
    public Task<TemplateInfoDto> GetTemplateInfoAsync(string id) =>
        Get<TemplateInfoDto>($"api/tasks/{id}/template-info");

    // --- опыт и статистика по шаблону (ТЗ п. 2.11, todo32) ---
    /// <summary>Записи опыта узла шаблона и его поддерева (вкладка «Опыт»).</summary>
    public Task<List<ExperienceRecordDto>> GetExperienceAsync(string templateTaskId) =>
        Get<List<ExperienceRecordDto>>($"api/tasks/{templateTaskId}/experience");

    /// <summary>Тэги записей опыта организации вместе с тэгами задач и шаблонов (T-24-S0):
    /// набор у них общий — он предлагается в форме записи и в фильтре списка опыта.</summary>
    public Task<List<string>> GetExperienceTagsAsync() => Get<List<string>>("api/experience/tags");

    /// <summary>
    /// ПОИСК ПО ОПЫТУ (T-268-S0) — тот же самый, которым пользуется агент
    /// (<c>search_experience</c>): лексическое попадание плюс ранг из BM25, свежести и
    /// совпадения тэгов. Строка поиска в списках опыта.
    /// </summary>
    public Task<List<ExperienceHitDto>> SearchExperienceAsync(string query, string? scope = null,
        string? projectId = null, bool includeInactive = false, int limit = 50)
    {
        var url = $"api/experience/search?q={Uri.EscapeDataString(query)}&limit={limit}"
                  + $"&includeInactive={(includeInactive ? "true" : "false")}";
        if (!string.IsNullOrWhiteSpace(scope))
        {
            url += $"&scope={Uri.EscapeDataString(scope)}";
        }
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            url += $"&project={Uri.EscapeDataString(projectId)}";
        }
        return Get<List<ExperienceHitDto>>(url);
    }

    // запись отдаётся формой целиком (текст, навык, «загружать всегда», тэги — T-24-S0),
    // поэтому в API уходит тот же объект, что вернула форма
    public Task<ExperienceRecordDto> CreateExperienceAsync(string templateTaskId,
        ExperienceSaveDto saved) =>
        Post<ExperienceRecordDto>($"api/tasks/{templateTaskId}/experience", saved);

    public Task<ExperienceRecordDto> UpdateExperienceAsync(string id, ExperienceSaveDto saved) =>
        Put<ExperienceRecordDto>($"api/experience/{id}", saved);

    /// <summary>Опыт ПРОЕКТА (todo48); skillIds — фильтр по навыкам, пусто — все записи.</summary>
    public Task<List<ExperienceRecordDto>> GetProjectExperienceAsync(string projectId,
        IEnumerable<string>? skillIds = null)
    {
        var filter = string.Join(',', skillIds ?? []);
        var query = filter.Length > 0 ? $"?skills={Uri.EscapeDataString(filter)}" : "";
        return Get<List<ExperienceRecordDto>>($"api/projects/{projectId}/experience{query}");
    }

    public Task<ExperienceRecordDto> CreateProjectExperienceAsync(string projectId,
        ExperienceSaveDto saved) =>
        Post<ExperienceRecordDto>($"api/projects/{projectId}/experience", saved);

    /// <summary>ОБЩИЕ ПРАВИЛА РАБОТЫ организации (T-11-S0): записи опыта без проекта и без
    /// узла шаблона — их получает каждая задача. skillIds — фильтр, пусто — все записи.</summary>
    public Task<List<ExperienceRecordDto>> GetGeneralExperienceAsync(IEnumerable<string>? skillIds = null)
    {
        var filter = string.Join(',', skillIds ?? []);
        var query = filter.Length > 0 ? $"?skills={Uri.EscapeDataString(filter)}" : "";
        return Get<List<ExperienceRecordDto>>($"api/experience/general{query}");
    }

    public Task<ExperienceRecordDto> CreateGeneralExperienceAsync(ExperienceSaveDto saved) =>
        Post<ExperienceRecordDto>("api/experience/general", saved);

    public Task DeleteExperienceAsync(string id) => Send(HttpMethod.Delete, $"api/experience/{id}");

    /// <summary>ИСПОЛЬЗОВАННЫЙ ОПЫТ ЗАДАЧИ (T-266-S0): что система подставила агенту в текст
    /// задания. Удалённая или унесённая в архив запись приходит строкой с Available=false.</summary>
    public Task<List<ExperienceUsedDto>> GetUsedExperienceAsync(string taskId) =>
        Get<List<ExperienceUsedDto>>($"api/tasks/{taskId}/experience/used");

    /// <summary>Статистика использования записи опыта (T-266-S0): сколько задач её получило
    /// и когда последний раз.</summary>
    public Task<ExperienceUsageDto> GetExperienceUsageAsync(string id) =>
        Get<ExperienceUsageDto>($"api/experience/{id}/usage");

    /// <summary>Переключить АКТИВНОСТЬ записи опыта (T-265-S0): неактивная запись не идёт
    /// в задание ни при каком раскладе, но остаётся в списке и возвращается той же кнопкой.
    /// Отдельный вызов — чтобы не переписывать текст и тэги значениями старого списка.</summary>
    public Task<ExperienceRecordDto> SetExperienceActiveAsync(string id, bool isActive) =>
        Post<ExperienceRecordDto>($"api/experience/{id}/active",
            new ExperienceActiveDto { IsActive = isActive });

    /// <summary>
    /// ПЕРЕНЕСТИ ЗАПИСЬ ОПЫТА В ДРУГУЮ ОБЛАСТЬ (T-269-S0): общие правила ↔ опыт проекта ↔
    /// опыт узла шаблона. Идентификатор, текст, навык, тэги и авторство сохраняются —
    /// тем это и отличается от «удалить и завести заново». force — подтверждение человека
    /// при переносе проектного текста в общие правила.
    /// </summary>
    public Task<ExperienceRecordDto> MoveExperienceAsync(string id, string scope,
        string? projectId = null, string? templateTaskId = null, bool force = false) =>
        Post<ExperienceRecordDto>($"api/experience/{id}/move", new ExperienceMoveDto
        {
            Scope = scope,
            ProjectId = projectId,
            TemplateTaskId = templateTaskId,
            Force = force,
        });

    /// <summary>РЕВИЗИЯ ОБЩЕГО ОПЫТА (T-269-S0): общие правила, которые выглядят проектными,
    /// — кандидаты на перенос пачкой в выбранный проект.</summary>
    public Task<List<ExperienceSuspectDto>> GetGeneralExperienceSuspectsAsync() =>
        Get<List<ExperienceSuspectDto>>("api/experience/general/suspects");

    // --- НАБОРЫ ОПЫТА (T-270-S0): «библиотека стилей работы» ---

    /// <summary>Наборы, найденные в каталоге данных (packs/*/pack.json), вместе с тем,
    /// установлен ли каждый и в какую область.</summary>
    public Task<List<ExperiencePackDto>> GetExperiencePacksAsync(string lang) =>
        Get<List<ExperiencePackDto>>($"api/packs?lang={Uri.EscapeDataString(lang)}");

    /// <summary>Поставить набор в ВЫБРАННУЮ область: общий опыт / проект / узел шаблона.
    /// Повторная установка ничего не задваивает и правок человека не затирает.</summary>
    public Task<ExperiencePackResultDto> InstallExperiencePackAsync(string code, string lang,
        string scope, string? projectId = null, string? templateTaskId = null) =>
        Post<ExperiencePackResultDto>(
            $"api/packs/{Uri.EscapeDataString(code)}/install?lang={Uri.EscapeDataString(lang)}",
            new ExperiencePackInstallDto
            {
                Scope = scope,
                ProjectId = projectId,
                TemplateTaskId = templateTaskId,
            });

    /// <summary>Снять набор — по служебной пометке владельца pack:&lt;код&gt;.</summary>
    public Task<ExperiencePackResultDto> RemoveExperiencePackAsync(string code) =>
        Post<ExperiencePackResultDto>(
            $"api/packs/{Uri.EscapeDataString(code)}/remove", new { });

    /// <summary>Выгрузить отобранные записи опыта в файл набора: так наработанный стиль
    /// переносится в другую организацию или установку.</summary>
    public Task<ExperiencePackResultDto> ExportExperiencePackAsync(string code, string lang,
        string name, string description, IEnumerable<string> recordIds) =>
        Post<ExperiencePackResultDto>($"api/packs/export?lang={Uri.EscapeDataString(lang)}",
            new ExperiencePackExportDto
            {
                Code = code,
                Name = name,
                Description = description,
                RecordIds = [.. recordIds],
            });

    /// <summary>Статистика шаблона: смены состояния задач, привязанных к шаблону.</summary>
    public Task<List<TemplateStatusStatDto>> GetTemplateStatsAsync(string templateTaskId) =>
        Get<List<TemplateStatusStatDto>>($"api/tasks/{templateTaskId}/template-stats");

    /// <summary>Создать задачу из шаблона (ТЗ п. 2.4): базовая дата встаёт на место самой
    /// ранней даты иерархии (todo31); pick — вариант автоподбора ("ai_first"/"human_first");
    /// parentId (T-201) — создать подзадачей этой задачи.</summary>
    public Task<TaskItem> InstantiateTemplateAsync(string id, DateTime? baseDate, string? pick = null,
        string? parentId = null) =>
        Post<TaskItem>($"api/tasks/{id}/instantiate",
            new InstantiateTemplateDto { BaseDate = baseDate, Pick = pick, ParentId = parentId });

    /// <summary>Авторазбиение задачи на подзадачи ИИ-агентом (ТЗ v1.26, todo28).</summary>
    public Task<Job> SplitTaskAsync(string id) => Post<Job>($"api/tasks/{id}/split", new { });

    /// <summary>Автоподбор исполнителя под skills (ТЗ v1.26): mode — ai_first / human_first.</summary>
    public Task<PickedExecutorDto> PickExecutorAsync(string? projectId, string? teamId,
        IEnumerable<string> skillIds, string mode)
    {
        var query = new List<string> { $"mode={Uri.EscapeDataString(mode)}" };
        if (projectId is not null)
        {
            query.Add($"projectId={Uri.EscapeDataString(projectId)}");
        }
        if (teamId is not null)
        {
            query.Add($"teamId={Uri.EscapeDataString(teamId)}");
        }
        var skills = string.Join(",", skillIds);
        if (skills.Length > 0)
        {
            query.Add($"skills={Uri.EscapeDataString(skills)}");
        }
        return Get<PickedExecutorDto>("api/executors/pick?" + string.Join("&", query));
    }

    // --- чат по задаче ---
    public Task<List<ChatMessage>> GetChatAsync(string taskId) =>
        Get<List<ChatMessage>>($"api/tasks/{taskId}/chat");

    public Task<ChatMessage> PostChatAsync(string taskId, string text, string? toExecutorId = null) =>
        Post<ChatMessage>($"api/tasks/{taskId}/chat", new ChatPostDto { Text = text, ToExecutorId = toExecutorId });

    /// <summary>Перенос обсуждения внешней системы в чат задачи (T-152): вызывается сразу
    /// после создания задачи из карточки Trello; повторный вызов дублей не делает.</summary>
    public Task<ImportResultDto> ImportChatAsync(string taskId, List<ChatImportMessageDto> messages) =>
        Post<ImportResultDto>($"api/tasks/{taskId}/chat/import", new ChatImportDto { Messages = messages });

    /// <summary>Ответ на вопрос ИИ-агента (ТЗ v1.17): агент продолжает работу.</summary>
    public Task<ChatMessage> AnswerQuestionAsync(string questionId, string text) =>
        Post<ChatMessage>($"api/chat/questions/{questionId}/answer", new JobAnswerDto { Text = text });

    /// <summary>Снять вопрос агента, на который уже некому отвечать (T-1-S1): вопрос
    /// закрывается без ответа и уходит из счётчиков задачи и из Inbox.</summary>
    public Task<ChatMessage> DismissQuestionAsync(string questionId) =>
        Post<ChatMessage>($"api/chat/questions/{questionId}/dismiss", new { });

    /// <summary>Текст описания задачи — для ленивых MD-превью на доске (ТЗ v1.17).</summary>
    public async Task<string> GetTaskDescriptionAsync(string taskId)
    {
        var response = await _http.GetAsync($"api/tasks/{taskId}/description");
        await EnsureOk(response);
        return await response.Content.ReadAsStringAsync();
    }

    // --- Inbox и задания ---
    public Task<List<InboxItemDto>> GetInboxAsync() => Get<List<InboxItemDto>>("api/inbox");

    public Task<Job> AnswerJobAsync(string jobId, string text) =>
        Post<Job>($"api/jobs/{jobId}/answer", new JobAnswerDto { Text = text });

    public Task<Job> CancelJobAsync(string jobId) => Post<Job>($"api/jobs/{jobId}/cancel", new { });

    /// <summary>«Продолжить» зависшее running-задание (T-117): повторная подача агенту.</summary>
    public Task<Job> ContinueJobAsync(string jobId) => Post<Job>($"api/jobs/{jobId}/continue", new { });

    // --- журнал работ ---
    public Task<List<EventRecord>> GetEventsAsync(string? projectId = null, string? taskId = null,
        string? actorId = null, string? eventType = null, int limit = 200)
    {
        var query = new List<string> { $"limit={limit}" };
        if (projectId is not null) query.Add($"projectId={Uri.EscapeDataString(projectId)}");
        if (taskId is not null) query.Add($"taskId={Uri.EscapeDataString(taskId)}");
        if (actorId is not null) query.Add($"actorId={Uri.EscapeDataString(actorId)}");
        if (eventType is not null) query.Add($"eventType={Uri.EscapeDataString(eventType)}");
        return Get<List<EventRecord>>("api/events?" + string.Join("&", query));
    }

    // --- биллинг (ТЗ v1.15, todo18) ---
    public Task<List<BillingRecordDto>> GetBillingAsync(DateTime? from = null, DateTime? to = null,
        string? projectId = null, string? teamId = null, string? executorId = null, string? modelId = null)
    {
        var query = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }
        Add("from", from?.ToUniversalTime().ToString("o"));
        Add("to", to?.ToUniversalTime().ToString("o"));
        Add("projectId", projectId);
        Add("teamId", teamId);
        Add("executorId", executorId);
        Add("modelId", modelId);
        return Get<List<BillingRecordDto>>("api/billing" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
    }

    // --- файлы и настройки ---
    public async Task<string> GetFileTextAsync(string relativePath)
    {
        var response = await _http.GetAsync($"api/files?path={Uri.EscapeDataString(relativePath)}");
        await EnsureOk(response);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Запись текстового файла в dataDir — редакторы профайлов и деклараций (п. 2.9).</summary>
    public Task SaveFileTextAsync(string relativePath, string text) =>
        Send(HttpMethod.Put, "api/files/text", new FileTextDto { Path = relativePath, Text = text });

    public Task<SettingsDto> GetSettingsAsync() => Get<SettingsDto>("api/settings");

    public Task SaveSettingsAsync(SettingsDto dto) => Send(HttpMethod.Put, "api/settings", dto);

    // --- ОБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208) ---

    /// <summary>Сходить в репозиторий выпусков: есть ли версия новее этой.</summary>
    public Task<UpdateCheckDto> CheckUpdateAsync() => Get<UpdateCheckDto>("api/update/check");

    /// <summary>Кого остановит перезапуск сервера (агенты всех открытых организаций).</summary>
    public Task<UpdateAgentsDto> UpdateAgentsAsync() => Get<UpdateAgentsDto>("api/update/agents");

    /// <summary>Скачать пакет и обновиться: сервер перезапустится сам.</summary>
    public Task<UpdateStartDto> RunUpdateAsync() => Post<UpdateStartDto>("api/update/run", new { });

    /// <summary>Загрузка файла (картинка из буфера обмена в MD-редакторе, ТЗ гл. 11).</summary>
    public Task<FileUploadResultDto> UploadFileAsync(string? projectId, string fileName, string dataBase64) =>
        Post<FileUploadResultDto>("api/files/upload", new FileUploadDto
        {
            ProjectId = projectId,
            FileName = fileName,
            DataBase64 = dataBase64,
        });

    /// <summary>Относительный URL бинарной отдачи файла из dataDir (для ссылок в Markdown).</summary>
    public static string RawFileUrl(string relativePath) =>
        "api/files/raw?path=" + Uri.EscapeDataString(relativePath);

    // --- справочник импортов и импорт задач (ТЗ v1.28, todo30) ---
    public Task<List<ImportSource>> GetImportSourcesAsync() => Get<List<ImportSource>>("api/imports");

    public Task<ImportSource> CreateImportSourceAsync(ImportSource source) =>
        Post<ImportSource>("api/imports", source);

    public Task<ImportSource> UpdateImportSourceAsync(ImportSource source) =>
        Put<ImportSource>($"api/imports/{source.Id}", source);

    public Task DeleteImportSourceAsync(string id) => Send(HttpMethod.Delete, $"api/imports/{id}");

    /// <summary>Состояние ключа и токена источника импорта (ТЗ v1.65, T-123): сами значения
    /// не отдаются — только признак «заполнено» и где лежит.</summary>
    public Task<ImportKeyStatusDto> GetImportKeysAsync(string sourceId) =>
        Get<ImportKeyStatusDto>($"api/imports/{sourceId}/keys");

    /// <summary>Установка ключа («key») либо токена («token») источника импорта: одно поле
    /// за раз. Когда заполнены оба, источник активируется сам.</summary>
    public Task<ImportKeyStatusDto> SetImportKeyAsync(string sourceId, string field, string value) =>
        Put<ImportKeyStatusDto>($"api/imports/{sourceId}/keys/{field}",
            new ImportKeySaveDto { Value = value });

    /// <summary>Проверка подключения источника импорта (T-134): принят ли ключ, принят ли
    /// токен и чьим аккаунтом смотрит внешняя система.</summary>
    public Task<ImportCheckDto> CheckImportAsync(string sourceId) =>
        Get<ImportCheckDto>($"api/imports/{sourceId}/check");

    /// <summary>Импорт в проект по источнику справочника (кнопка списка задач).</summary>
    public Task<ImportResultDto> RunImportAsync(string projectId, ImportRunDto dto) =>
        Post<ImportResultDto>($"api/projects/{projectId}/import", dto);

    /// <summary>Импорт одиночного задания по URL (ТЗ v1.50, todo50): содержимое карточки
    /// для подстановки в форму новой задачи; файлы уже скачаны в uploads/ проекта.</summary>
    public Task<ImportedCardDto> ImportCardAsync(string projectId, ImportCardDto dto) =>
        Post<ImportedCardDto>($"api/projects/{projectId}/import-card", dto);

    /// <summary>Обновление задачи из источника импорта (T-246): карточка перечитывается
    /// по ссылке импорта, сохранённой в самой задаче, — заголовок, срок, описание, файлы
    /// и новые сообщения обсуждения.</summary>
    public Task<ImportRefreshResultDto> RefreshTaskImportAsync(string taskId,
        string? sourceId = null) =>
        Post<ImportRefreshResultDto>($"api/tasks/{taskId}/import/refresh",
            new ImportRefreshDto { SourceId = sourceId });

    /// <summary>Все файлы, на которые есть ссылки в .md задачи и чате (кнопка «все файлы»).</summary>
    public Task<List<TaskFileDto>> GetTaskFilesAsync(string taskId) =>
        Get<List<TaskFileDto>>($"api/tasks/{taskId}/files");

    /// <summary>Удалить файл задачи (ТЗ v1.45, todo37_3): только её собственные файлы —
    /// артефакты (видео весит гигабайты) и вставки MD-редактора.</summary>
    public Task DeleteTaskFileAsync(string taskId, string path) =>
        Send(HttpMethod.Delete, $"api/tasks/{taskId}/files?path={Uri.EscapeDataString(path)}");

    /// <summary>Консоль задания (ТЗ v1.45, todo37_3): строки после номера after.</summary>
    public Task<JobConsoleDto> GetJobConsoleAsync(string jobId, long after = 0) =>
        Get<JobConsoleDto>($"api/jobs/{jobId}/console?after={after}");

    /// <summary>
    /// Каталоги локального диска — диалог выбора папки проекта (ТЗ гл. 11).
    /// <paramref name="withFiles"/> — показать ещё и файлы: кадр датасета LoRA берут
    /// «от корня файловой системы» (T-12-S1), то есть тем же обходом дисков.
    /// </summary>
    public Task<DirListDto> GetDirsAsync(string? path, bool withFiles = false)
    {
        var query = new List<string>();
        if (path is not null)
        {
            query.Add("path=" + Uri.EscapeDataString(path));
        }
        if (withFiles)
        {
            query.Add("files=true");
        }
        return Get<DirListDto>("api/fs/dirs" + (query.Count == 0 ? "" : "?" + string.Join("&", query)));
    }

    /// <summary>Содержимое каталога внутри ПАПКИ ПРОЕКТА — выбор эталонного файла объекта
    /// (T-264). Пустой путь — корень папки проекта.</summary>
    public Task<ProjectDirListDto> GetProjectDirAsync(string projectId, string? path) =>
        Get<ProjectDirListDto>($"api/fs/project?projectId={Uri.EscapeDataString(projectId)}" +
                               (string.IsNullOrEmpty(path) ? "" : $"&path={Uri.EscapeDataString(path)}"));

    /// <summary>Сведения о файле папки проекта — превью формы объекта (T-264).</summary>
    public Task<ProjectFileInfoDto> GetProjectFileInfoAsync(string projectId, string path) =>
        Get<ProjectFileInfoDto>($"api/fs/project-file?projectId={Uri.EscapeDataString(projectId)}" +
                                $"&path={Uri.EscapeDataString(path)}");

    // --- внутреннее ---
    private async Task<T> Get<T>(string uri)
    {
        var response = await _http.GetAsync(uri);
        await EnsureOk(response);
        return await ReadJson<T>(response, uri);
    }

    /// <summary>
    /// GET, у которого ПУСТОЙ ответ — законный «ничего нет» (T-125): 204 либо тело нулевой
    /// длины превращаются в null, а не в исключение разбора JSON.
    /// </summary>
    private async Task<T?> GetOrNull<T>(string uri) where T : class
    {
        var response = await _http.GetAsync(uri);
        await EnsureOk(response);
        return await ReadJsonOrNull<T>(response);
    }

    private async Task<T> Post<T>(string uri, object body)
    {
        var response = await _http.PostAsJsonAsync(uri, body, Ai2pJson.Options);
        await EnsureOk(response);
        return await ReadJson<T>(response, uri);
    }

    private async Task<T> Put<T>(string uri, object body)
    {
        var response = await _http.PutAsJsonAsync(uri, body, Ai2pJson.Options);
        await EnsureOk(response);
        return await ReadJson<T>(response, uri);
    }

    /// <summary>
    /// Тело ответа → объект там, где объект ОБЯЗАН быть (T-125). Пустое или неразбираемое
    /// тело — <see cref="ApiException"/> с понятным текстом: он доедет до снэкбара, тогда как
    /// <c>JsonException</c> из глубины сериализатора не ловится нигде и рвёт circuit Blazor
    /// (наблюдалось на кнопке «запустить»: <c>api/tasks/{id}/limit</c> отдавал пустое тело).
    /// </summary>
    private static async Task<T> ReadJson<T>(HttpResponseMessage response, string uri)
    {
        var value = await ReadJsonOrNull<T>(response);
        if (value is null)
        {
            throw new ApiException(Loc.T("msg.apiClient.1", response.RequestMessage?.Method, uri));
        }
        return value;
    }

    /// <summary>Тело ответа → объект или null, если ответ пуст (204 либо нулевая длина).</summary>
    private static async Task<T?> ReadJsonOrNull<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Ai2pJson.Options);
        }
        catch (JsonException ex)
        {
            // тело не JSON (пустое без Content-Length, HTML страницы ошибки, обрыв ответа):
            // ошибка API, а не крах интерфейса
            throw new ApiException(Loc.T("msg.apiClient.2", ex.Message));
        }
    }

    private async Task Send(HttpMethod method, string uri, object? body = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Ai2pJson.Options);
        }
        var response = await _http.SendAsync(request);
        await EnsureOk(response);
    }

    /// <summary>Ошибка API → ApiException с текстом из ProblemDetails.detail.</summary>
    private static async Task EnsureOk(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                throw new ApiException(detail.GetString() ?? text);
            }
        }
        catch (JsonException)
        {
            // не ProblemDetails — отдаём как есть
        }
        throw new ApiException($"{(int)response.StatusCode}: {text}");
    }
}
