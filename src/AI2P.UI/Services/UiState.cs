using System.Text.Json;
using System.Text.Json.Serialization;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using Microsoft.AspNetCore.Components.Authorization;

namespace AI2P.UI.Services;

/// <summary>Открытая закладка представления/формы (ТЗ гл. 11, п. 5 «закладки представлений»).</summary>
public sealed class UiTab
{
    public string Kind { get; init; } = "";          // board / projects / teams / … / task / project
    public string? EntityId { get; init; }           // для task/project — id сущности
    public string? TitleText { get; set; }           // название сущности; для представлений — из словаря
    /// <summary>Короткий код сущности (display_id) — вторая строка заголовка в режиме "code" (ТЗ v1.18).</summary>
    public string? Code { get; set; }
}

/// <summary>Разбиение рабочей области на фреймы (todo22): один, два (вертикальный или
/// горизонтальный делитель) или четыре (крест, приоритет у горизонтального делителя).</summary>
public enum FrameSplit
{
    Single,
    /// <summary>Вертикальный делитель: два фрейма рядом (левый и правый).</summary>
    Columns,
    /// <summary>Горизонтальный делитель: два фрейма друг над другом.</summary>
    Rows,
    /// <summary>Четыре фрейма 2×2.</summary>
    Quad,
}

/// <summary>Фрейм рабочей области — свой ряд закладок и активная закладка (todo22).
/// Фрейм 0 — основной (левый верхний): в нём открываются представления из экшен-бара.</summary>
public sealed class UiFrame
{
    public List<UiTab> Tabs { get; } = [];
    public int ActiveTabIndex { get; set; }

    public UiTab? ActiveTab =>
        ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count ? Tabs[ActiveTabIndex] : null;
}

/// <summary>
/// Состояние UI одного подключения (circuit): текущий проект/команда, кэш справочников,
/// открытые закладки. Данные — только через ApiClient (API-first).
/// </summary>
public sealed class UiState
{
    public const string ViewTasks = "tasks";
    public const string ViewTemplates = "templates";
    public const string ViewProjects = "projects";
    public const string ViewTeams = "teams";
    public const string ViewExecutors = "executors";
    public const string ViewHistory = "history";
    public const string ViewInbox = "inbox";
    public const string ViewBilling = "billing";
    /// <summary>Расписание запуска задач (ТЗ п. 2.12, todo34) — закладка с календарём.</summary>
    public const string ViewSchedule = "schedule";
    public const string ViewSettings = "settings";
    /// <summary>Чтение поставляемой документации (T-17-S1) — обычная закладка, не окно.</summary>
    public const string ViewDocs = "docs";
    public const string ViewTask = "task";
    /// <summary>Закладка одного проекта с вкладками основное/задачи/команда (ТЗ v1.18, todo21).</summary>
    public const string ViewProject = "project";

    /// <summary>
    /// Закладка одного ОБЪЕКТА проекта (T-99-S0) — то же, что закладка задачи: заводят объект
    /// оконной формой, а читают закладкой. Оконная форма при этом никуда не делась: её
    /// открывает кнопка «изменить» в тулбаре закладки.
    /// </summary>
    public const string ViewObject = "object";

    private readonly ApiClient _api;
    private readonly AuthenticationStateProvider _auth;

    public UiState(ApiClient api, AuthenticationStateProvider auth)
    {
        _api = api;
        _auth = auth;
    }

    public bool Initialized { get; private set; }
    public string? CurrentProjectId { get; private set; }
    public string? CurrentTeamId { get; private set; }

    /// <summary>Исполнитель-человек вошедшего пользователя — актор всех его действий.</summary>
    public Executor LocalUser { get; private set; } = new();

    /// <summary>Аккаунт вошедшего пользователя (ТЗ п. 2.14, этап 39).</summary>
    public Account Account { get; private set; } = new();

    /// <summary>Роль вошедшего в системе (ТЗ п. 2.2) — по ней прячутся недоступные действия.</summary>
    public SystemRole Role { get; private set; } = SystemRole.Reader;

    /// <summary>Пользователь вошёл ещё и как admin сервера (отдельный локальный логин, гл. 12).</summary>
    public bool IsServerAdmin { get; private set; }

    /// <summary>Вошедший — ХОЗЯИН ЭТОГО КОМПЬЮТЕРА (T-201-S0): владелец системы либо
    /// локальный администратор сервера. Считается на сервере и от организации запроса
    /// не зависит.</summary>
    public bool IsMachineOwner { get; private set; }

    /// <summary>Старшинство ролей (ТЗ п. 2.2): чем больше, тем больше прав.</summary>
    private static int Rank(SystemRole role) => role switch
    {
        SystemRole.Owner => 4,
        SystemRole.Admin => 3,
        SystemRole.ProjectAdmin => 2,
        SystemRole.Editor => 1,
        _ => 0,
    };

    /// <summary>Роли хватает: те же правила, что проверяет API (ApiPermissions) — UI просто
    /// не показывает то, что сервер всё равно не разрешит.
    /// <para>В АРХИВЕ (T-45-S0) прав не хватает никогда: данные архива изменению не подлежат,
    /// и это ровно то, что проверяет сервер (ApiEndpoints отклоняет там любую запись). Одно
    /// место вместо шестидесяти: <c>CanEdit</c>, <c>CanAdmin</c>, <c>CanManageProjects</c> и
    /// прочие считаются отсюда, и все они разом гаснут при переходе в архив.</para></summary>
    public bool Can(SystemRole required) => !InArchive && Rank(Role) >= Rank(required);

    /// <summary>Может править содержимое: задачи, чат, задания, файлы, опыт.</summary>
    public bool CanEdit => Can(SystemRole.Editor);

    /// <summary>Может править проекты, команды, расписания и правила безопасности.</summary>
    public bool CanManageProjects => Can(SystemRole.ProjectAdmin);

    /// <summary>Может править настройку системы: справочники, исполнителей, модели, действия.</summary>
    public bool CanAdmin => Can(SystemRole.Admin);

    /// <summary>Может управлять пользователями системы (ТЗ п. 2.14).</summary>
    public bool CanManageAccounts => Can(SystemRole.Owner);

    /// <summary>
    /// Может смотреть и править СПИСОК СЕРВЕРОВ этой установки (T-148, T-174): администратор
    /// организации либо администратор самого сервера. Владельца тут мало: owner — хозяин всей
    /// организации (кластера целиком), а за свой компьютер в кластере отвечает admin, и список
    /// серверов своей машины, ручной пуск репликации и её диагностика нужны именно ему
    /// (T-174). Второе условие — потому, что человек, подключивший свой сервер к ЧУЖОЙ
    /// организации, получает там обычную роль: сеанс с сервером за NAT начинает он сам.
    /// <para>В АРХИВЕ — нет (T-45-S0): список серверов и репликация к архивной среде
    /// отношения не имеют, а «сменить сервер» у задачи запрещено прямо заданием.</para>
    /// </summary>
    public bool CanManageServers => !InArchive && (CanAdmin || IsServerAdmin);

    /// <summary>
    /// Может смотреть и править СПИСОК ОРГАНИЗАЦИЙ этой установки (T-201-S0) — раздел
    /// «Организации» настроек. Право у ХОЗЯИНА КОМПЬЮТЕРА (<see cref="IsMachineOwner"/>),
    /// а не у владельца открытой сейчас организации: организации лежат на этой машине,
    /// и роль в одной из них о правах на остальные ничего не говорит. До T-201-S0 условием
    /// была роль owner в организации запроса, и раздел исчезал, стоило перейти в организацию,
    /// приехавшую репликацией: там хозяин машины — admin (участником его делает
    /// вступление, T-186-S0).
    /// <para>В АРХИВЕ — нет (T-45-S0), как и у прочих прав: архив только читается.</para>
    /// </summary>
    public bool CanManageOrgs => !InArchive && IsMachineOwner;

    /// <summary>Обновить свой аккаунт в шапке после правки формы аккаунта.</summary>
    public void UpdateAccount(Account account)
    {
        Account = account;
        NotifyChanged();
    }

    // --- черновики чата задачи (T-117) ---
    // Поля ввода живут в компоненте карточки, а компонент пересоздаётся при переключении
    // закладок приложения — набранный текст пропадал. Черновики держатся здесь, в состоянии
    // подключения, по id задачи; очищаются при отправке.

    private readonly Dictionary<string, string> _chatDrafts = [];
    private readonly Dictionary<string, string> _replyDrafts = [];

    /// <summary>Черновик сообщения чата задачи (T-117): переживает переключение закладок.</summary>
    public string ChatDraft(string taskId) => _chatDrafts.GetValueOrDefault(taskId, "");

    public void SetChatDraft(string taskId, string text)
    {
        if (text.Length == 0)
        {
            _chatDrafts.Remove(taskId);
        }
        else
        {
            _chatDrafts[taskId] = text;
        }
    }

    /// <summary>Черновик ответа на вопрос агента (T-117): переживает переключение закладок.</summary>
    public string ReplyDraft(string taskId) => _replyDrafts.GetValueOrDefault(taskId, "");

    public void SetReplyDraft(string taskId, string text)
    {
        if (text.Length == 0)
        {
            _replyDrafts.Remove(taskId);
        }
        else
        {
            _replyDrafts[taskId] = text;
        }
    }

    // --- ЧТЕНИЕ ДОКУМЕНТАЦИИ (T-17-S1) ---
    // Закладка «Документация» показывает страницы поставляемой документации и ходит по их
    // внутренним ссылкам В ТОЙ ЖЕ закладке. Где мы сейчас и откуда пришли, живёт ЗДЕСЬ,
    // а не в компоненте: компонент пересоздаётся при каждом переключении закладок
    // приложения (так же, как пропадали черновики чата до T-117), и путь чтения вместе
    // с историей переходов терялся бы от одного взгляда на список задач.

    /// <summary>Страница, открытая в закладке «Документация»; пусто — первая (README.md).</summary>
    public string DocPath { get; private set; } = "";

    /// <summary>Пройденный путь: страницы, с которых уходили по внутренним ссылкам.</summary>
    private readonly List<string> _docHistory = [];

    /// <summary>Есть куда вернуться — кнопка «назад» активна только после первого перехода
    /// по внутренней ссылке (T-17-S1).</summary>
    public bool DocCanGoBack => _docHistory.Count > 0;

    /// <summary>Переход на страницу документации: текущая уходит в историю переходов.</summary>
    public void DocOpen(string relPath)
    {
        if (DocPath.Length > 0 && !string.Equals(DocPath, relPath, StringComparison.Ordinal))
        {
            _docHistory.Add(DocPath);
        }
        DocPath = relPath;
    }

    /// <summary>Страница показана — запомнить её под тем именем, каким она нашлась
    /// (пустой запрос приводит на README.md, и «назад» должно вести именно на неё).</summary>
    public void DocShown(string relPath) => DocPath = relPath;

    /// <summary>Шаг назад; null — возвращаться некуда.</summary>
    public string? DocBack()
    {
        if (_docHistory.Count == 0)
        {
            return null;
        }
        var previous = _docHistory[^1];
        _docHistory.RemoveAt(_docHistory.Count - 1);
        DocPath = previous;
        return previous;
    }

    /// <summary>Организация этого подключения (ТЗ п. 2.15, этап 40): в ней все данные экрана.</summary>
    public Organization Org { get; private set; } = new();

    /// <summary>Организации вошедшего — переключатель в тулбаре (ТЗ гл. 11).</summary>
    public List<Organization> Orgs { get; private set; } = [];

    /// <summary>
    /// ПЕРЕЧИТАТЬ ОРГАНИЗАЦИИ ВОШЕДШЕГО (T-148-S0). Список приходит один раз, при старте
    /// (<see cref="InitializeAsync"/>), а организацию заводят потом — из настроек. До этой
    /// правки только что созданная организация в форме смены не появлялась вовсе («Других
    /// организаций у вас нет»), хотя в таблице настроек она уже стояла: та таблица держит
    /// СВОЙ список, а переключатель — этот. Помогала только перезагрузка страницы.
    ///
    /// Берём ровно то же, что кладёт в состояние сервер: действующие организации, где
    /// вошедший — участник (есть исполнитель с его аккаунтом), И организации этого
    /// компьютера, в которые он может вступить сам (T-186-S0): организация, приехавшая
    /// репликацией, участников здесь не заводит, и без этого её в переключателе нет вовсе.
    /// </summary>
    public void SetOrgs(IEnumerable<Organization> orgs)
    {
        Orgs = orgs
            .Where(o => o is { IsActive: true, DeletedAt: null } && (o.IsMember || o.CanJoin))
            .ToList();
        NotifyChanged();
    }

    // --- СРЕДА: РАБОЧАЯ ИЛИ АРХИВ (T-45-S0, выпуск 1.105) ---
    //
    // Архив — это ВТОРАЯ СРЕДА той же организации: своя база той же схемы и свои файлы
    // проектов. Переключение среды идёт заголовком запроса (ApiClient.SetArchive), поэтому
    // все готовые представления показывают архив без единой правки — меняется только то,
    // ЧТО им отдаёт API. В архиве ничего не правится: см. Can() выше.

    /// <summary>Архивы организации, ОТКРЫТЫЕ на этом сервере, — только на них можно
    /// переключиться. Порядок списка: текущий архив первым, дальше по новизне. Пусто —
    /// поля среды в тулбаре нет вовсе.</summary>
    public List<Archive> Archives { get; private set; } = [];

    /// <summary>Выбранный архив; null — рабочая среда (умолчание при старте всегда).</summary>
    public Archive? SelectedArchive { get; private set; }

    /// <summary>Открыт архив, а не рабочая среда: данные только читаются.</summary>
    public bool InArchive => SelectedArchive is not null;

    /// <summary>Выбранный архив — ТЕКУЩИЙ архив организации: только из него возможно
    /// восстановление (T-42-S0), поэтому кнопка «Восстановить» есть лишь у его задач.</summary>
    public bool InCurrentArchive => SelectedArchive?.IsCurrent == true;

    /// <summary>Название выбранной среды для тулбара.</summary>
    public string EnvName => SelectedArchive?.Name ?? "";

    /// <summary>Перечитать список открытых архивов организации; молча пустеет, если архивов
    /// нет или прав на реестр не хватает — поле среды тогда просто не показывается.</summary>
    public async Task RefreshArchivesAsync()
    {
        List<Archive> all;
        try
        {
            all = await _api.GetArchivesAsync();
        }
        catch (ApiException)
        {
            all = [];
        }
        await ApplyArchivesAsync(all);
    }

    /// <summary>Список архивов уже прочитан (закладка «Архивы» настроек) — поставить его
    /// в поле среды, не спрашивая API второй раз. Зовётся после КАЖДОЙ правки реестра
    /// (T-200-S0): до этого список среды читался только при входе, и добавленный архив
    /// не появлялся в тулбаре, а закрытый не исчезал, пока человек не переключит
    /// организацию туда и обратно.</summary>
    public async Task ApplyArchivesAsync(IEnumerable<Archive> all)
    {
        Archives = all.Where(a => a.State == ArchiveStates.Open)
            .OrderByDescending(a => a.IsCurrent)
            .ToList();
        // выбранный архив закрыли или удалили с этого сервера — возвращаемся в рабочую среду
        if (SelectedArchive is not null && Archives.All(a => a.Id != SelectedArchive.Id))
        {
            await SwitchEnvAsync(null);
        }
        NotifyChanged();
    }

    /// <summary>
    /// СМЕНА СРЕДЫ. Все закладки закрываются: они показывают данные прежней среды, и оставить
    /// их значило бы держать на экране карточки задач, которых в новой среде нет. Дальше
    /// навигация идёт по выбранной среде — открывается список проектов, как при первом входе.
    /// </summary>
    public async Task SwitchEnvAsync(string? archiveId)
    {
        var archive = archiveId is { Length: > 0 }
            ? Archives.FirstOrDefault(a => a.Id == archiveId)
            : null;
        SelectedArchive = archive;
        _api.SetArchive(archive?.Code);
        CloseAllTabs();
        // справочники, проекты и списки — теперь из выбранной среды
        await RefreshReferenceDataAsync();
        // проект прежней среды в новой обычно не существует: начинаем со списка проектов
        OpenView(CurrentProject is null ? ViewProjects : ViewTasks);
        NotifyChanged();
    }

    /// <summary>Закрыть все закладки во всех фреймах (смена среды).</summary>
    public void CloseAllTabs()
    {
        for (var f = 0; f < Frames.Length; f++)
        {
            Frames[f].Tabs.Clear();
            Frames[f].ActiveTabIndex = 0;
        }
    }

    /// <summary>
    /// СЕРВЕР ЕЩЁ НЕ ПОДКЛЮЧЁН К ОРГАНИЗАЦИИ (T-148): заявка подана и ждёт решения человека
    /// на дирижёре. Интерфейс в этом состоянии открывается, но работает лишь настройками
    /// и списком серверов — данных нет, они приедут репликацией. Остальные представления
    /// (задачи, проекты, команды, исполнители, шаблоны) погашены.
    /// </summary>
    public bool NoOrg { get; private set; }

    // --- кто мы в кластере (ТЗ гл. 6, этап 42) ---

    /// <summary>Внутренний ключ (unid) сервера, на котором открыт экран.</summary>
    public string ServerId { get; private set; } = "";

    /// <summary>Код локального сервера в организации: S0, S1, … Пусто — сервер с организацией
    /// не связан (одиночная установка): суффиксов в номерах тогда нет, фильтры не нужны.</summary>
    public string ServerCode { get; private set; } = "";

    public string ServerName { get; private set; } = "";

    /// <summary>Локальный сервер — дирижёр организации: он ведёт шаблоны, справочники,
    /// строки без сервера и иерархию задач между серверами (ТЗ гл. 6).</summary>
    public bool IsConductor { get; private set; }

    /// <summary>Серверы организации (фильтры списков и выбор владельца задачи); заполняется
    /// вместе со справочниками. Меньше двух — кластера нет, элементы UI прячутся.</summary>
    public List<OrgServerBriefDto> OrgServers { get; private set; } = [];

    /// <summary>В организации больше одного сервера — показывать фильтры по серверам,
    /// колонку сервера и кнопку «сменить сервер» (ТЗ гл. 6, этап 42).</summary>
    public bool IsCluster => OrgServers.Count > 1;

    /// <summary>Код сервера по его внутреннему ключу; пусто — сервер не указан.</summary>
    public string ServerCodeOf(string? serverId) =>
        serverId is { Length: > 0 } id
            ? OrgServers.FirstOrDefault(s => s.ServerId == id)?.Code ?? ""
            : "";

    /// <summary>Задача (расписание, запись) правится на этом сервере (ТЗ гл. 6, этап 42).
    /// В архиве — никогда (T-45-S0).</summary>
    public bool CanWriteOn(string? ownerServerId) =>
        !InArchive && Ownership.CanWrite(ownerServerId, ServerId, IsConductor);

    /// <summary>Базовый URL приложения БЕЗ организации — от него строятся адреса других
    /// организаций при переключении.</summary>
    public string AppBaseUrl { get; private set; } = "";

    /// <summary>
    /// Путь приложения ОТ КОРНЯ САЙТА — «/ai2p» (T-199-S0). Считается из
    /// <see cref="AppBaseUrl"/> тем же разбором, что и относительные ссылки на задачи
    /// (T-196-S0): имя сервера, порт и протокол из него выброшены.
    /// </summary>
    public string AppBasePath { get; private set; } = "";

    /// <summary>
    /// Адрес другой организации: смена организации — это переход по адресу и полная
    /// перезагрузка экрана, потому что меняются вообще все данные (ТЗ гл. 11).
    ///
    /// ССЫЛКА ОТНОСИТЕЛЬНАЯ, ОТ КОРНЯ САЙТА (T-199-S0). Раньше она строилась от
    /// <see cref="AppBaseUrl"/>, то есть от ВНЕШНЕГО адреса сервера (hostname2/port2,
    /// T-2-S1), и переключение организации уводило человека с того адреса, по которому
    /// он работает: зашёл по внутреннему имени или по петле — а после смены организации
    /// оказался на внешнем, куда изнутри сети дороги может не быть вовсе. Относительный
    /// адрес оставляет протокол, имя и порт теми, что стоят в адресной строке.
    /// </summary>
    public string OrgUrl(Organization org) => OrgUrlOf(org.Code);

    /// <summary>Тот же адрес по КОДУ организации (T-199-S0): переход в только что появившуюся
    /// организацию делается ещё до того, как её запись доедет до UI.</summary>
    public string OrgUrlOf(string code) => $"{AppBasePath}/{code}/";

    /// <summary>Базовый URL для ссылок на задачи (ТЗ гл. 11):
    /// {протокол}://{hostname}:{порт}{basePath}/{код организации}.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>Ссылка на задачу для просмотра и копирования (ТЗ гл. 11) — ВНЕШНЯЯ:
    /// по имени сервера из настроек, её отправляют тем, кто снаружи.</summary>
    public string TaskUrl(string taskId) => $"{BaseUrl}/task/{taskId}";

    /// <summary>Порт, который сервер слушает на этом компьютере (T-2-S1): у внешней ссылки
    /// порт бывает другой — проброшенный на роутере, и в петлевую ссылку он не годится.</summary>
    public int LocalPort { get; private set; }

    /// <summary>Базовый URL с петлевым именем — от него строятся ЛОКАЛЬНЫЕ ссылки (T-142);
    /// порт берётся тот, который сервер слушает здесь (T-2-S1).</summary>
    public string LocalBaseUrl => LinkUrls.ToLocal(BaseUrl, LocalPort);

    /// <summary>Локальная ссылка на задачу (T-142): работает на каждом сервере кластера —
    /// задачи реплицируются, и та же задача есть у каждого.</summary>
    public string TaskLocalUrl(string taskId) => $"{LocalBaseUrl}/task/{taskId}";

    /// <summary>Ссылок на задачу две — локальная и внешняя (T-142). Имя сервера петлевое —
    /// ссылка одна: вторая была бы её копией.</summary>
    public bool HasExternalUrl => !LinkUrls.IsLocalOnly(BaseUrl);

    /// <summary>
    /// ВНЕШНЯЯ ССЫЛКА НА ОБЪЕКТ (T-99-S0): <c>…/object/{id}</c>. Устроена в точности как
    /// ссылка на задачу, и по той же причине — её пересылают людям, а не подставляют в промпт
    /// (для промпта есть ссылка <c>@obj:OBJ-3</c>, и это разные вещи: одна открывает карточку,
    /// вторая раскрывается в паспорт). Объект адресуется своим uuid, поэтому ссылка работает
    /// и между проектами: при открытии закладка сама узнаёт, из какого объект проекта.
    /// </summary>
    public string ObjectUrl(string objectId) => $"{BaseUrl}/object/{objectId}";

    /// <summary>Локальная (петлевая) ссылка на объект — как у задачи (T-142).</summary>
    public string ObjectLocalUrl(string objectId) => $"{LocalBaseUrl}/object/{objectId}";

    /// <summary>
    /// ВНЕШНЯЯ ССЫЛКА НА СТРАНИЦУ ДОКУМЕНТАЦИИ (T-207-S0): <c>…/doc/man/https.md</c>.
    /// Устроена как ссылка на задачу, только адресуется не идентификатором, а ПУТЁМ
    /// страницы внутри языкового каталога — тем же, каким её зовёт <c>api/doc</c>.
    /// Языка в ссылке нет намеренно: у получателя интерфейс может быть на другом языке,
    /// и открыть ему надо ту же страницу на ЕГО языке (а нет перевода — как обычно,
    /// с предупреждением).
    /// </summary>
    public string DocUrl(string relPath) => $"{BaseUrl}/doc/{DocLinkPath(relPath)}";

    /// <summary>Локальная (петлевая) ссылка на страницу документации — как у задачи (T-142).</summary>
    public string DocLocalUrl(string relPath) => $"{LocalBaseUrl}/doc/{DocLinkPath(relPath)}";

    /// <summary>Путь страницы в адресе: разделители остаются разделителями, а каждое имя
    /// кодируется — в именах файлов документации бывают и пробелы, и кириллица.</summary>
    private static string DocLinkPath(string? relPath) =>
        string.Join('/', (relPath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    public List<Project> Projects { get; private set; } = [];
    public List<Team> Teams { get; private set; } = [];
    public List<Executor> Executors { get; private set; } = [];
    public List<Role> Roles { get; private set; } = [];
    public List<Skill> Skills { get; private set; } = [];
    /// <summary>Справочник io formats (ТЗ v1.17) — для деклараций возможностей.</summary>
    public List<IoFormat> IoFormats { get; private set; } = [];
    /// <summary>Справочник ИИ-моделей (ТЗ п. 2.9).</summary>
    public List<AiModel> Models { get; private set; } = [];

    /// <summary>Справочник состояний задач (ТЗ v1.37, todo34_3): все, включая неактивные
    /// (для показа названия/цвета состояния уже назначенного задаче), сортировка по порядку.</summary>
    public List<TaskStatusDef> TaskStatuses { get; private set; } = [];

    /// <summary>Активные состояния по порядковому номеру — выпадающие списки и колонки доски.</summary>
    public IEnumerable<TaskStatusDef> ActiveTaskStatuses => TaskStatuses.Where(s => s.IsActive);

    public TaskStatusDef? StatusOf(string id) => TaskStatuses.FirstOrDefault(s => s.Id == id);

    /// <summary>Название состояния на языке UI; нет в справочнике — сам код.</summary>
    public string StatusName(string id) => StatusOf(id)?.Name ?? id;

    /// <summary>Стиль надписи состояния (ТЗ v1.37): фон — цвет из справочника,
    /// текст — чёрный/белый по яркости фона.</summary>
    public string StatusStyle(string id)
    {
        var color = StatusOf(id)?.Color ?? "#9e9e9e";
        return $"background:{color};color:{ContrastText(color)};";
    }

    /// <summary>Чёрный или белый текст по яркости фона (#rrggbb).</summary>
    public static string ContrastText(string color)
    {
        if (color.Length == 7 && color[0] == '#'
            && int.TryParse(color[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            var luminance = 0.299 * ((rgb >> 16) & 0xFF) + 0.587 * ((rgb >> 8) & 0xFF) + 0.114 * (rgb & 0xFF);
            return luminance > 150 ? "#000000" : "#ffffff";
        }
        return "#ffffff";
    }

    /// <summary>Debug-режим (п. 2.9): в UI разрешает менять признак isCustom моделей.</summary>
    public bool Debug { get; private set; }

    /// <summary>Версия приложения в формате <c>1.&lt;номер билда&gt;</c> (ТЗ гл. 4.3, этап 46):
    /// показывается в настройках — по ней видно, какая сборка стоит на этом компьютере.</summary>
    public string Version { get; private set; } = "";

    /// <summary>Сервер запущен как СЕРВИС ОС (T-271); <c>false</c> — консольный запуск.</summary>
    public bool IsService { get; private set; }

    /// <summary>Установка помечена как сервис (<c>serviceMode</c> в config.json, T-271):
    /// службу завёл скрипт <c>makeAsServise.*</c>. Про установку, а не про этот запуск.</summary>
    public bool ServiceMode { get; private set; }

    /// <summary>Вторая строка заголовка закладок «Задача»/«Проект» (ТЗ v1.18): name / code.</summary>
    public string TabTitleMode { get; private set; } = "name";

    /// <summary>Смена режима заголовков закладок «на лету» из настроек (ТЗ v1.18).</summary>
    public void SetTabTitleMode(string mode)
    {
        TabTitleMode = mode == "code" ? "code" : "name";
        NotifyChanged();
    }

    /// <summary>Ячейка календаря расписаний (ТЗ v1.37): после времени — код шаблона ("code")
    /// или начало заголовка шаблона ("name").</summary>
    public string ScheduleCellMode { get; private set; } = "code";

    /// <summary>Смена режима ячейки календаря расписаний «на лету» из настроек (ТЗ v1.37).</summary>
    public void SetScheduleCellMode(string mode)
    {
        ScheduleCellMode = mode == "name" ? "name" : "code";
        NotifyChanged();
    }

    // --- фреймы рабочей области (todo22): по умолчанию один, сплиты 2/4 ---

    /// <summary>Четыре фрейма всегда существуют; видимы первые VisibleFrameCount.
    /// Порядок в Quad: 0 — левый верхний (основной), 1 — правый верхний,
    /// 2 — левый нижний, 3 — правый нижний.</summary>
    public UiFrame[] Frames { get; } = [new(), new(), new(), new()];

    public FrameSplit Split { get; private set; } = FrameSplit.Single;

    /// <summary>Положение вертикального делителя, % ширины левой колонки (todo22).</summary>
    public int VerticalRatio { get; private set; } = 50;

    /// <summary>Положение горизонтального делителя, % высоты верхнего ряда (todo22).</summary>
    public int HorizontalRatio { get; private set; } = 50;

    public int VisibleFrameCount => Split switch
    {
        FrameSplit.Single => 1,
        FrameSplit.Columns or FrameSplit.Rows => 2,
        _ => 4,
    };

    /// <summary>Тёмная тема (ТЗ гл. 11: две темы, светлая и тёмная). Запоминается вместе
    /// с лейаутом (T-237-S0): до этого она жила только в памяти circuit и слетала при
    /// любой перезагрузке страницы, а тем более при перезапуске сервера.</summary>
    public bool DarkMode { get; private set; }

    /// <summary>Переключить тему (кнопка в шапке). Через лейаут — чтобы выбор сохранился.</summary>
    public void ToggleDarkMode()
    {
        DarkMode = !DarkMode;
        LayoutChanged();
    }

    /// <summary>Эксплорер (панель 3) показан; по умолчанию скрыт (todo23),
    /// состояние запоминается в лейауте и восстанавливается при следующем входе.</summary>
    public bool ExplorerVisible { get; private set; }

    /// <summary>Показать/скрыть эксплорер (кнопка в левом конце тулбара, todo23).</summary>
    public void ToggleExplorer()
    {
        ExplorerVisible = !ExplorerVisible;
        LayoutChanged();
    }

    /// <summary>Левый тулбар — вертикальный экшен-бар (панель 2, ТЗ гл. 11) — показан.
    /// По умолчанию ВИДЕН (T-10-S1), в отличие от эксплорера; состояние запоминается
    /// в лейауте и восстанавливается при следующем входе.</summary>
    public bool ActionBarVisible { get; private set; } = true;

    /// <summary>Показать/скрыть левый тулбар (кнопка в самом левом конце верхнего
    /// тулбара, T-10-S1).</summary>
    public void ToggleActionBar()
    {
        ActionBarVisible = !ActionBarVisible;
        LayoutChanged();
    }

    // --- состояния компонентов (todo23): подвкладка проекта, представление/фильтр/сортировка
    // списков задач — живут в circuit и переживают переключение/закрытие закладок;
    // сверх того сохраняются в app_state и восстанавливаются при следующем входе (гл. 11) ---

    private readonly Dictionary<string, object> _componentStates = new();

    /// <summary>Состояния прошлого входа (JSON по ключам): материализуются в объект
    /// при первом обращении ComponentState — тип известен только вызывающему компоненту.</summary>
    private Dictionary<string, JsonElement> _restoredComponentStates = new();

    /// <summary>Живой объект состояния компонента по ключу: правки видны при следующем открытии.</summary>
    public T ComponentState<T>(string key) where T : class, new()
    {
        if (_componentStates.TryGetValue(key, out var value) && value is T typed)
        {
            return typed;
        }
        if (_restoredComponentStates.TryGetValue(key, out var json))
        {
            try
            {
                if (json.Deserialize<T>() is { } restored)
                {
                    return (T)(_componentStates[key] = restored);
                }
            }
            catch (JsonException)
            {
                // несовместимое сохранённое состояние (например, после обновления версии) — сброс
            }
        }
        return (T)(_componentStates[key] = new T());
    }

    // --- ПОСЛЕДНИЙ КАТАЛОГ ВЫБОРА ФАЙЛА НА ДИСКЕ (T-273) ---
    // Выбор файла на диске (кадр датасета LoRA) начинается не от корня: первый раз — от папки
    // проекта, а дальше — от каталога, из которого файл взяли в прошлый раз. Кадры датасета
    // кладут пачкой из одной папки, и обход дисков заново на каждый кадр — это ровно та
    // работа, которую человек делает руками вместо программы.

    /// <summary>Ключ состояния: один на организацию, как у остальных состояний представлений.</summary>
    private const string LocalPickerKey = "localFilePicker";

    /// <summary>Состояние выбора файла на диске: только последний каталог.</summary>
    private sealed class LocalPickerState
    {
        public string Dir { get; set; } = "";
    }

    /// <summary>Каталог прошлого выбора файла на диске; null — выбора ещё не было.
    /// Переживает закрытие окна и следующий вход (состояние представлений, гл. 11).</summary>
    public string? LastLocalDir =>
        ComponentState<LocalPickerState>(LocalPickerKey).Dir is { Length: > 0 } dir ? dir : null;

    /// <summary>Запомнить каталог, из которого взяли файл.</summary>
    public void SetLastLocalDir(string? dir)
    {
        ComponentState<LocalPickerState>(LocalPickerKey).Dir = dir ?? "";
        PersistComponentStates();
    }

    /// <summary>Сохранить состояния представлений фоном (как лейаут): живые объекты +
    /// не материализованные состояния прошлого входа; ошибка сети не мешает работе UI.</summary>
    public void PersistComponentStates()
    {
        if (!Initialized || NoOrg)
        {
            return;   // без организации состояние UI хранить негде (T-148)
        }
        var merged = new Dictionary<string, object>();
        foreach (var (key, json) in _restoredComponentStates)
        {
            merged[key] = json;
        }
        foreach (var (key, state) in _componentStates)
        {
            merged[key] = state;
        }
        var json2 = JsonSerializer.Serialize(merged);
        _ = Task.Run(async () =>
        {
            try
            {
                await _api.SetUiComponentStatesAsync(json2);
            }
            catch (Exception)
            {
                // сохранение состояний — best effort
            }
        });
    }

    /// <summary>Восстановить состояния представлений прошлого входа (JSON из app_state).</summary>
    private void RestoreComponentStates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }
        try
        {
            _restoredComponentStates =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch (JsonException)
        {
            // повреждённое состояние — начинаем с чистого
        }
    }

    /// <summary>Последнее представление, открытое из экшен-бара (панель 2): по нему подсвечена
    /// его кнопка. С T-212 содержимое эксплорера от этого значения НЕ зависит — панель одна
    /// и всегда показывает одно и то же дерево.</summary>
    public string ExplorerSection { get; private set; } = ViewProjects;

    /// <summary>Перерисовка вкладок/шапки после изменения состояния.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public Project? CurrentProject => Projects.FirstOrDefault(p => p.Id == CurrentProjectId);
    public Team? CurrentTeam => Teams.FirstOrDefault(t => t.Id == CurrentTeamId);

    public async Task InitializeAsync()
    {
        if (Initialized)
        {
            return;
        }
        // личность вошедшего берётся из cookie-аутентификации (ТЗ гл. 12, этап 39) и
        // передаётся в ApiClient: UI ходит в собственное API по петле, куда cookie не доходит.
        // Вход администратора сервера — ОТДЕЛЬНАЯ вторая cookie, поэтому в личности запроса
        // он виден отдельной «личностью» со своей схемой; передаём и его, иначе настройки
        // самого сервера остались бы недоступными из UI даже после входа администратором
        var user = (await _auth.GetAuthenticationStateAsync()).User;
        var admin = user.Identities.Any(i => i.AuthenticationType == Ai2pSchemes.ServerAdmin);
        _api.SetAccount(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, admin);

        var state = await _api.GetStateAsync();
        CurrentProjectId = state.CurrentProjectId;
        CurrentTeamId = state.CurrentTeamId;
        LocalUser = state.LocalUser;
        Account = state.Account;
        Role = state.Role;
        IsServerAdmin = state.IsServerAdmin;
        IsMachineOwner = state.CanManageOrgs;
        Org = state.Org;
        Orgs = state.Orgs;
        NoOrg = state.NoOrg;
        ServerId = state.ServerId;
        ServerCode = state.ServerCode;
        ServerName = state.ServerName;
        IsConductor = state.IsConductor;
        AppBaseUrl = state.AppBaseUrl;
        // путь приложения от корня сайта (T-199-S0): по нему переключаются организации,
        // оставаясь на том адресе, по которому человек сюда пришёл
        AppBasePath = LinkUrls.RelativeOf(state.AppBaseUrl).TrimEnd('/');
        BaseUrl = state.BaseUrl;
        LocalPort = state.LocalPort;
        // свои же ссылки на файлы в выводе Markdown открываются локально (T-142).
        // Адресов у сервера бывает два (T-2-S1) — своей считается ссылка по любому из них
        Md.OwnBaseUrl = state.AppBaseUrl;
        Md.OwnBaseUrl2 = state.AppOwnBaseUrl;
        // ссылка на файл, написанная агентом на СОСЕДНЕМ сервере, открывается через нас
        // (T-13-S0): файла здесь нет, и сервер принесёт его от соседа сам
        Md.PeerBaseUrls = state.PeerBaseUrls;
        Debug = state.Debug;
        Version = state.Version;
        IsService = state.IsService;
        ServiceMode = state.ServiceMode;
        TabTitleMode = state.TabTitleMode;
        ScheduleCellMode = state.ScheduleCellMode;
        RestoreComponentStates(state.UiComponentStates);
        if (NoOrg)
        {
            // ОРГАНИЗАЦИИ ЕЩЁ НЕТ (T-148): справочники и списки живут в её базе, спрашивать
            // их нечем и незачем. Открываем сразу настройки — там список серверов, ради
            // которого урезанный интерфейс и открывается
            OpenView(ViewSettings);
            Initialized = true;
            return;
        }
        await RefreshReferenceDataAsync();
        // СРЕДА ПРИ СТАРТЕ — ВСЕГДА РАБОЧАЯ (T-45-S0): выбор архива не запоминается намеренно.
        // Архив это снимок для просмотра, а не место работы, и «вчера смотрел архив» не повод
        // открывать сегодняшний рабочий день в нём. Список нужен только для поля в тулбаре:
        // архивов нет — поля нет вовсе
        await RefreshArchivesAsync();

        // лейаут фреймов и закладок запоминается и восстанавливается при входе (todo22);
        // нет сохранённого — стартовый вид: список проектов либо списки задач (ТЗ гл. 11)
        if (!RestoreLayout(state.UiLayout))
        {
            OpenView(CurrentProjectId is null || CurrentProject is null ? ViewProjects : ViewTasks);
        }
        Initialized = true;
    }

    public async Task RefreshReferenceDataAsync()
    {
        Projects = await _api.GetProjectsAsync();
        Teams = await _api.GetTeamsAsync();
        Executors = await _api.GetExecutorsAsync();
        Roles = await _api.GetRolesAsync();
        Skills = await _api.GetSkillsAsync();
        IoFormats = await _api.GetIoFormatsAsync();
        Models = await _api.GetModelsAsync();
        TaskStatuses = await _api.GetTaskStatusesAsync(lang: null, includeInactive: true);
        // серверы организации (ТЗ гл. 6, этап 42): фильтры по серверам, колонка владельца
        // и выбор в кнопке «сменить сервер». Список короткий и меняется редко
        try
        {
            OrgServers = await _api.GetOrgServersBriefAsync();
        }
        catch (ApiException)
        {
            OrgServers = []; // организация ещё не выбрана либо нет прав — работаем без кластера
        }
    }

    /// <summary>Перечитать справочник состояний (после правок в настройках/смены языка, ТЗ v1.37).</summary>
    public async Task RefreshTaskStatusesAsync(string? lang = null)
    {
        TaskStatuses = await _api.GetTaskStatusesAsync(lang, includeInactive: true);
        NotifyChanged();
    }

    /// <summary>Выбор проекта запоминается (ТЗ гл. 11, «Список проектов»).</summary>
    public async Task SetCurrentProjectAsync(string? projectId)
    {
        CurrentProjectId = projectId;
        await _api.SetCurrentProjectAsync(projectId);
        // команда по умолчанию — из настроек проекта, если задана
        var defaultTeam = CurrentProject?.SettingsJson is { } json ? ReadDefaultTeam(json) : null;
        if (defaultTeam is not null && Teams.Any(t => t.Id == defaultTeam))
        {
            await SetCurrentTeamAsync(defaultTeam);
        }
        NotifyChanged();
    }

    public async Task SetCurrentTeamAsync(string? teamId)
    {
        CurrentTeamId = teamId;
        await _api.SetCurrentTeamAsync(teamId);
        NotifyChanged();
    }

    /// <summary>Открыть представление из экшен-бара/меню (todo22): уже открыто в любом видимом
    /// фрейме — активировать там (закладку можно перетащить в другой фрейм); иначе — в основном.</summary>
    public void OpenView(string kind)
    {
        if (kind is not (ViewTask or ViewObject))
        {
            ExplorerSection = kind;
        }
        if (TryActivate(t => t.Kind == kind))
        {
            return;
        }
        Frames[0].Tabs.Add(new UiTab { Kind = kind });
        Frames[0].ActiveTabIndex = Frames[0].Tabs.Count - 1;
        LayoutChanged();
    }

    /// <summary>Закладка «Задача»: открывается в фрейме, где кликнули по элементу списка (todo22).</summary>
    public void OpenTask(string taskId, string code, string title, int frameIndex = 0) =>
        OpenEntityTab(ViewTask, taskId, code, title, frameIndex);

    /// <summary>Закладка «Проект» (ТЗ v1.18, todo21): открывается кликом по проекту в списке.</summary>
    public void OpenProject(string projectId, string code, string name, int frameIndex = 0) =>
        OpenEntityTab(ViewProject, projectId, code, name, frameIndex);

    /// <summary>Закладка «Объект» (T-99-S0): открывается кликом по строке списка объектов,
    /// по строке субобъекта и переходом по ссылке <c>…/object/{id}</c>.</summary>
    public void OpenObject(string objectId, string code, string name, int frameIndex = 0) =>
        OpenEntityTab(ViewObject, objectId, code, name, frameIndex);

    private void OpenEntityTab(string kind, string entityId, string code, string title, int frameIndex)
    {
        // уже открыта в любом видимом фрейме — активируем там и освежаем заголовок
        for (var f = 0; f < VisibleFrameCount; f++)
        {
            var index = Frames[f].Tabs.FindIndex(t => t.Kind == kind && t.EntityId == entityId);
            if (index >= 0)
            {
                Frames[f].Tabs[index].TitleText = title;
                Frames[f].Tabs[index].Code = code;
                Frames[f].ActiveTabIndex = index;
                LayoutChanged();
                return;
            }
        }
        var frame = Frames[Math.Clamp(frameIndex, 0, VisibleFrameCount - 1)];
        frame.Tabs.Add(new UiTab { Kind = kind, EntityId = entityId, Code = code, TitleText = title });
        frame.ActiveTabIndex = frame.Tabs.Count - 1;
        LayoutChanged();
    }

    /// <summary>Активировать существующую закладку по условию в любом видимом фрейме.</summary>
    private bool TryActivate(Predicate<UiTab> match)
    {
        for (var f = 0; f < VisibleFrameCount; f++)
        {
            var index = Frames[f].Tabs.FindIndex(match);
            if (index >= 0)
            {
                Frames[f].ActiveTabIndex = index;
                LayoutChanged();
                return true;
            }
        }
        return false;
    }

    public void ActivateTab(int frameIndex, int tabIndex)
    {
        Frames[frameIndex].ActiveTabIndex = tabIndex;
        LayoutChanged();
    }

    /// <summary>Закрыть закладку сущности в любом фрейме (например, после удаления задачи).</summary>
    public void CloseEntityTab(string kind, string entityId)
    {
        for (var f = 0; f < Frames.Length; f++)
        {
            var index = Frames[f].Tabs.FindIndex(t => t.Kind == kind && t.EntityId == entityId);
            if (index >= 0)
            {
                CloseTab(f, index);
                return;
            }
        }
    }

    public void CloseTab(int frameIndex, int index)
    {
        var frame = Frames[frameIndex];
        if (index < 0 || index >= frame.Tabs.Count)
        {
            return;
        }
        frame.Tabs.RemoveAt(index);
        if (frame.ActiveTabIndex >= frame.Tabs.Count)
        {
            frame.ActiveTabIndex = Math.Max(0, frame.Tabs.Count - 1);
        }
        LayoutChanged();
    }

    /// <summary>Смена разбиения (todo22): закладки скрываемых фреймов переезжают в основной.</summary>
    public void SetSplit(FrameSplit split)
    {
        Split = split;
        for (var f = VisibleFrameCount; f < Frames.Length; f++)
        {
            Frames[0].Tabs.AddRange(Frames[f].Tabs);
            Frames[f].Tabs.Clear();
            Frames[f].ActiveTabIndex = 0;
        }
        LayoutChanged();
    }

    /// <summary>Перенести закладку в другой фрейм (drag-n-drop, todo22; DnD выполняется в JS —
    /// Firefox не начинает перетаскивание без dataTransfer.setData, багфикс todo23).</summary>
    public void MoveTab(int fromFrame, int fromTab, int toFrame)
    {
        if (fromFrame == toFrame
            || fromFrame < 0 || fromFrame >= Frames.Length
            || toFrame < 0 || toFrame >= VisibleFrameCount
            || fromTab < 0 || fromTab >= Frames[fromFrame].Tabs.Count)
        {
            return;
        }
        var tab = Frames[fromFrame].Tabs[fromTab];
        CloseTab(fromFrame, fromTab);
        var frame = Frames[toFrame];
        frame.Tabs.Add(tab);
        frame.ActiveTabIndex = frame.Tabs.Count - 1;
        LayoutChanged();
    }

    /// <summary>Позиция делителя после перетаскивания мышкой (JS interop, todo22).</summary>
    public void SetRatio(bool horizontal, int percent)
    {
        percent = Math.Clamp(percent, 10, 90);
        if (horizontal)
        {
            HorizontalRatio = percent;
        }
        else
        {
            VerticalRatio = percent;
        }
        SaveLayout(); // без перерисовки: DOM уже обновлён самим JS
    }

    private void LayoutChanged()
    {
        SaveLayout();
        // переключение/закрытие закладок — удобный момент дописать и состояния представлений
        PersistComponentStates();
        NotifyChanged();
    }

    // --- сохранение и восстановление лейаута (todo22): app_state «ui.layout» через API ---

    private sealed class LayoutDto
    {
        [JsonPropertyName("split")] public string Split { get; set; } = "single";
        [JsonPropertyName("v")] public int V { get; set; } = 50;
        [JsonPropertyName("h")] public int H { get; set; } = 50;
        /// <summary>Эксплорер показан (todo23); по умолчанию скрыт.</summary>
        [JsonPropertyName("explorer")] public bool Explorer { get; set; }
        /// <summary>Левый тулбар (экшен-бар) показан (T-10-S1); по умолчанию ВИДЕН —
        /// поэтому у лейаута прошлых версий, где поля нет вовсе, тулбар останется на месте.</summary>
        [JsonPropertyName("actionbar")] public bool ActionBar { get; set; } = true;
        /// <summary>Тёмная тема (T-237-S0); у лейаута прошлых версий поля нет — светлая.</summary>
        [JsonPropertyName("dark")] public bool Dark { get; set; }
        [JsonPropertyName("frames")] public List<FrameDto> Frames { get; set; } = [];
    }

    private sealed class FrameDto
    {
        [JsonPropertyName("active")] public int Active { get; set; }
        [JsonPropertyName("tabs")] public List<TabDto> Tabs { get; set; } = [];
    }

    private sealed class TabDto
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
    }

    /// <summary>Сохранить лейаут фоном; ошибка сети не мешает работе UI.</summary>
    private void SaveLayout()
    {
        if (!Initialized || NoOrg)
        {
            // восстановление ещё идёт — не перезаписываем сохранённое; без организации
            // хранить лейаут негде: он лежит в её базе (T-148)
            return;
        }
        var dto = new LayoutDto
        {
            Split = Split.ToString().ToLowerInvariant(),
            V = VerticalRatio,
            H = HorizontalRatio,
            Explorer = ExplorerVisible,
            ActionBar = ActionBarVisible,
            Dark = DarkMode,
            Frames = Frames.Select(f => new FrameDto
            {
                Active = f.ActiveTabIndex,
                Tabs = f.Tabs.Select(t => new TabDto
                {
                    Kind = t.Kind,
                    Id = t.EntityId,
                    Title = t.TitleText,
                    Code = t.Code,
                }).ToList(),
            }).ToList(),
        };
        var json = JsonSerializer.Serialize(dto);
        _ = Task.Run(async () =>
        {
            try
            {
                await _api.SetUiLayoutAsync(json);
            }
            catch (Exception)
            {
                // сохранение лейаута — best effort
            }
        });
    }

    /// <summary>Восстановить лейаут прошлого входа; false — сохранённого лейаута нет/он пуст.</summary>
    private bool RestoreLayout(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        LayoutDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LayoutDto>(json);
        }
        catch (JsonException)
        {
            return false;
        }
        if (dto is null)
        {
            return false;
        }
        Split = dto.Split switch
        {
            "columns" => FrameSplit.Columns,
            "rows" => FrameSplit.Rows,
            "quad" => FrameSplit.Quad,
            _ => FrameSplit.Single,
        };
        VerticalRatio = Math.Clamp(dto.V, 10, 90);
        HorizontalRatio = Math.Clamp(dto.H, 10, 90);
        ExplorerVisible = dto.Explorer; // состояние кнопки эксплорера запоминается (todo23)
        ActionBarVisible = dto.ActionBar; // то же у левого тулбара (T-10-S1), но умолчание — «виден»
        DarkMode = dto.Dark; // выбранная тема (T-237-S0)
        for (var f = 0; f < Frames.Length; f++)
        {
            Frames[f].Tabs.Clear();
            if (f >= dto.Frames.Count)
            {
                continue;
            }
            foreach (var tab in dto.Frames[f].Tabs.Where(t => t.Kind.Length > 0))
            {
                Frames[f].Tabs.Add(new UiTab
                {
                    Kind = tab.Kind,
                    EntityId = tab.Id,
                    TitleText = tab.Title,
                    Code = tab.Code,
                });
            }
            Frames[f].ActiveTabIndex = Math.Clamp(dto.Frames[f].Active, 0, Math.Max(0, Frames[f].Tabs.Count - 1));
        }
        // секция эксплорера — последнее представление в основном фрейме
        var view = Frames[0].ActiveTab?.Kind;
        if (view is not null and not (ViewTask or ViewProject or ViewObject))
        {
            ExplorerSection = view;
        }
        return Frames.Take(VisibleFrameCount).Any(f => f.Tabs.Count > 0);
    }

    private static string? ReadDefaultTeam(string settingsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty("defaultTeamId", out var value) ? value.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
