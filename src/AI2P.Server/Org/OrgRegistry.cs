using AI2P.Core;
using System.Collections.Concurrent;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;

namespace AI2P.Server.Org;

/// <summary>
/// Реестр организаций (ТЗ п. 2.15, этап 40): серверная БД, аккаунты, организации и живые
/// контексты (<see cref="OrgContext"/>) — по одному на организацию.
///
/// Контекст создаётся по требованию и кэшируется: заводить 30 сервисов и открывать БД на
/// каждый запрос нельзя, а держать их синглтонами, как было до этапа 40, — нельзя тем более.
/// При старте приложения контексты всех активных организаций создаются сразу: им нужно
/// досеять справочники и запустить расписание (ТЗ п. 2.12) — иначе расписания «просыпались»
/// бы только после первого открытия организации в браузере.
/// </summary>
public sealed class OrgRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, OrgContext> _contexts = new();
    private readonly string _dataDir;
    private readonly OrgDeps _deps;
    private readonly object _lock = new();

    /// <summary>Серверная БД: аккаунты, организации, серверы, node_id (ТЗ п. 6.4.1).</summary>
    public Database ServerDb { get; }

    public EventStore ServerEvents { get; }
    public AccountService Accounts { get; }
    public OrgService Orgs { get; }

    /// <summary>Серверы кластера и их связь с организациями (ТЗ гл. 6, этап 41).</summary>
    public ServerService Servers { get; }

    /// <summary>Курсоры и состояние репликации по парам «организация — сервер» (этап 43).</summary>
    public ReplicationStateService ReplState { get; }

    /// <summary>База сравнения и конфликты репликации ФАЙЛОВ (этап 44).</summary>
    public FileSyncStateService FileSync { get; }

    /// <summary>Очередь повтора: строки, отвергнутые базой по ограничению целостности (T-160).</summary>
    public ReplPendingService ReplPending { get; }

    public OrgRegistry(string dataDir, string serverDbFile, OrgDeps deps)
    {
        _dataDir = Path.GetFullPath(dataDir);
        _deps = deps;
        ServerDb = new Database(_dataDir, serverDbFile, DatabaseKind.Server);
        ServerDb.Init();
        ServerEvents = new EventStore(ServerDb);
        Accounts = new AccountService(ServerDb, ServerEvents);
        Orgs = new OrgService(ServerDb, ServerEvents);
        Servers = new ServerService(ServerDb, ServerEvents);
        ReplState = new ReplicationStateService(ServerDb);
        FileSync = new FileSyncStateService(ServerDb);
        ReplPending = new ReplPendingService(ServerDb);
    }

    /// <summary>
    /// Запись о ЛОКАЛЬНОМ сервере по настройкам config.json (ТЗ гл. 6, этап 41): она есть
    /// всегда и обновляется при каждом старте — адрес мог смениться, а внутренний ключ (unid),
    /// которым сервер узнают в кластере, остаётся прежним.
    /// </summary>
    public ServerNode EnsureLocalServer(string name, string protocol, string hostname, int port,
        string basePath, string hostname2 = "", int? port2 = null)
    {
        var local = Servers.EnsureLocal(name, protocol, hostname, port, basePath, hostname2, port2);
        // узел и сервер — одно и то же (ТЗ гл. 6, этап 43): авторство в журнале изменений
        // подписывается внутренним ключом сервера, иначе гашение эха не работало бы.
        // На установках, заведённых до этапа 43, значения разошлись — выравниваем
        ServerDb.AlignNodeId(local.Id);
        return local;
    }

    /// <summary>
    /// Связать организацию с локальным сервером: первому серверу достаётся код <c>S0</c>,
    /// и он же становится дирижёром — дирижёр обязателен и ровно один (ТЗ гл. 6).
    ///
    /// Исключение — компьютер, который в кластере УЖЕ зовут по-своему (T-148-S0): в новой
    /// организации он оставляет себе тот же код (<see cref="ServerService.KnownCodeOf"/>),
    /// иначе сервер <c>S1</c> заводил бы у себя организации, где он значится <c>S0</c>, —
    /// а этим кодом в кластере зовут соседнюю машину.
    /// </summary>
    public OrgServer LinkLocalServer(string orgId, string? actorId)
    {
        var local = Servers.Local() ?? throw new InvalidOperationException(
            Loc.T("msg.clusterJoinFlow.1"));
        var code = Servers.KnownCodeOf(local.Id, orgId);
        return Servers.Attach(orgId, local.Id, OrgServerStatus.Active, requestedBy: "", actorId,
            code: code.Length > 0 ? code : null);
    }

    /// <summary>
    /// Каталог хранилища организации: <c>data/orgs/&lt;ORG-N&gt;/</c> (ТЗ п. 6.4.4). Имя выбирается
    /// один раз и запоминается локально (<see cref="OrgService.DirName"/>): с этапа 43
    /// организация может приехать с чужого сервера вместе со своим номером, и две <c>ORG-1</c>
    /// на одной установке не должны попадать в один каталог.
    /// </summary>
    public string DirOf(Organization org) => Path.Combine(_dataDir, "orgs", Orgs.DirName(org));

    // Коды организаций проверяются на КАЖДОМ запросе (сегмент пути, ТЗ гл. 11), включая
    // статику и вебсокет Blazor, поэтому держим их в памяти: иначе на ровном месте
    // получился бы запрос к БД на каждую картинку.
    private volatile string[] _codes = [];

    /// <summary>Первый сегмент пути — код существующей организации (ТЗ гл. 11).</summary>
    public bool IsOrgCode(string segment)
    {
        foreach (var code in _codes)
        {
            if (string.Equals(code, segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ОТКРЫТЫЕ СЕЙЧАС организации (T-208): их контексты подняты, их сторожа работают,
    /// их агенты заняты. Ровно они и отвечают на вопрос «кого снесёт перезапуск сервера»:
    /// организация, в которую с этого старта никто не заходил, заданий не выполняет.
    /// </summary>
    public IReadOnlyList<OrgContext> OpenContexts => _contexts.Values.ToList();

    /// <summary>Перечитать коды организаций (после создания или переименования).</summary>
    public void RefreshCodes() =>
        _codes = Orgs.List(includeInactive: false).Select(o => o.Code).ToArray();

    /// <summary>Живой контекст организации; создаётся и наполняется при первом обращении.</summary>
    public OrgContext Context(Organization org)
    {
        if (_contexts.TryGetValue(org.Id, out var existing))
        {
            existing.Refresh(org);
            return existing;
        }
        lock (_lock)
        {
            if (_contexts.TryGetValue(org.Id, out existing))
            {
                existing.Refresh(org);
                return existing;
            }
            var dir = DirOf(org);
            Directory.CreateDirectory(dir);
            var context = new OrgContext(org, dir, ServerDb.NodeId, _deps);
            // общий каталог импорта моделей уровня сервера (туда пишут инсталляторы)
            context.SeedAndMigrate(Path.Combine(_dataDir, "models"));
            context.ScheduleRunner.Start();
            // сторож сроков уведомлений (T-272): раз в минуту смотрит задачи ЭТОГО сервера
            context.NotificationRunner.Start();
            // сторож зависших заданий (T-117): running-задания без живого вызова
            // (после перезапуска приложения) толкаются автоматически; он же запускает
            // задачи с наступившим отложенным стартом (T-121) — поэтому перенос старта
            // переживает выключение компьютера
            context.Orchestrator.StartWatchdog();
            _contexts[org.Id] = context;
            return context;
        }
    }

    // --- ПРОСМОТР ОТКРЫТОГО АРХИВА (T-45-S0, выпуск 1.105) ---
    //
    // Каталог открытого архива устроен ровно как каталог организации: своя база SQLite ТОЙ ЖЕ
    // схемы и подкаталог projects с файлами. Поэтому «переключиться на архив» — это не новые
    // экраны и не второй набор обработчиков API, а ВТОРОЙ НАБОР СЕРВИСОВ, построенный над
    // каталогом архива: тот же OrgContext, только другой каталог. Все готовые страницы
    // (дерево задач, карточка, шаблоны, объекты, опыт, файлы) начинают показывать архив
    // сами собой.
    //
    // Три отличия от контекста рабочей среды, и все три обязательны:
    //   1) сеять и мигрировать данные архива нечем и не за чем — SeedAndMigrate не зовётся;
    //   2) НИ ОДИН сторож не запускается (расписание, сроки уведомлений, зависшие задания):
    //      архив — это снимок УЖЕ СДАННОЙ работы, и запускать по нему задачи заново нельзя;
    //   3) дирижёром такой контекст не считается никогда — это второй замок к тому же:
    //      автозапуски, расписание и правка «строк без сервера» идут только у дирижёра.
    // Запись в архив закрыта отдельно, на входе в API (ApiEndpoints): архивная среда — только
    // для чтения, а не «почти для чтения».

    private readonly ConcurrentDictionary<string, OrgContext> _archives = new();

    /// <summary>
    /// Контекст ОТКРЫТОГО архива организации: тот же набор сервисов, построенный над каталогом
    /// архива. <paramref name="codeOrId"/> — код архива (так он назван в ссылках, параметр
    /// <c>arc=</c>) либо его идентификатор. null — архива нет, он не открыт на этом сервере
    /// или назван неверно: вызывающий тогда работает с рабочей средой.
    /// </summary>
    public OrgContext? ArchiveContext(OrgContext home, string? codeOrId)
    {
        var asked = (codeOrId ?? "").Trim();
        if (asked.Length == 0)
        {
            return null;
        }
        var archive = home.Archives.List()
            .FirstOrDefault(a => string.Equals(a.Code, asked, StringComparison.OrdinalIgnoreCase)
                                 || a.Id == asked);
        // закрытый архив — это .zip, читать из него нечего: переключиться можно только
        // на открытый (в списке среды закрытых нет вовсе)
        if (archive is null || archive.State != ArchiveStates.Open)
        {
            return null;
        }
        var key = home.Org.Id + "/" + archive.Id;
        if (_archives.TryGetValue(key, out var existing))
        {
            return existing;
        }
        lock (_lock)
        {
            if (_archives.TryGetValue(key, out existing))
            {
                return existing;
            }
            var dir = home.Archives.DirOf(archive.Code);
            if (!Directory.Exists(dir))
            {
                return null;
            }
            var deps = _deps with { IsConductor = _ => false };
            var context = new OrgContext(home.Org, dir, ServerDb.NodeId, deps);
            _archives[key] = context;
            return context;
        }
    }

    /// <summary>Забыть контексты архивов организации: архив закрыли, удалили или переоткрыли,
    /// и держать открытым файл его базы нельзя (иначе .zip не соберётся).</summary>
    public void ForgetArchiveContexts(string orgId)
    {
        foreach (var key in _archives.Keys.Where(k => k.StartsWith(orgId + "/", StringComparison.Ordinal)))
        {
            if (_archives.TryRemove(key, out var ctx))
            {
                ctx.Dispose();
            }
        }
    }

    /// <summary>
    /// ПОДКЛЮЧЕНИЕ К СУЩЕСТВУЮЩЕЙ ОРГАНИЗАЦИИ (ТЗ гл. 11, п. 11.2; этап 43): завести её
    /// у себя с ТЕМИ ЖЕ идентификаторами, что у дирижёра, и подготовить к первичной репликации.
    ///
    /// Идентификаторы обязаны совпадать: организация, её связи с серверами и сами серверы
    /// приезжают сюда журналом изменений, и своя нумерация превратила бы одну организацию
    /// в две, а связи задвоила бы по <c>UNIQUE(org_id, server_id)</c>.
    ///
    /// Справочники этой организации НЕ засеиваются: роли, навыки, состояния, действия
    /// и модели принадлежат организации и приедут от дирижёра. Иначе на каждом сервере
    /// появился бы свой комплект встроенных записей с другими идентификаторами.
    /// </summary>
    public Organization AdoptOrg(ClusterJoinStatusDto join, string conductorServerId, string? actorId)
    {
        // ЗАВЕДЕНИЕ ИДЁТ ПОД ТЕМ ЖЕ ЗАМКОМ, ЧТО И СОЗДАНИЕ КОНТЕКСТА (T-20-S1).
        //
        // Пометку «это реплика» ставят первой (T-3-S1), но раньше неё организация уже
        // появлялась в реестре — Orgs.Adopt, а дальше несколько сотен миллисекунд уходило
        // на каталог и Database.Init. В это окно успевал влезть входящий запрос дирижёра
        // (/repl/begin приходит сразу после подтверждения заявки) или сторож репликации:
        // они создавали контекст организации, а он видел базу БЕЗ пометки и БЕЗ связей —
        // то есть «своя организация одиночной установки», где локальный сервер считается
        // дирижёром (ServerService.IsLocalConductor), — и засеивал справочники.
        //
        // У навыков и форматов идентификаторы СЛУЧАЙНЫЕ, поэтому второй комплект встроенных
        // записей делает строки обеих сторон навсегда неприменимыми: очередь повтора у пары
        // набирала по три сотни строк «UNIQUE constraint failed: skills.name» и «FOREIGN KEY
        // constraint failed» (skill_texts) и не рассасывалась никогда. Замок закрывает окно
        // целиком: контекст этой организации может быть создан только ПОСЛЕ того, как
        // пометка и обе связи с серверами уже записаны.
        lock (_lock)
        {
            return AdoptOrgLocked(join, conductorServerId, actorId);
        }
    }

    private Organization AdoptOrgLocked(ClusterJoinStatusDto join, string conductorServerId,
        string? actorId)
    {
        var org = Orgs.Adopt(join.OrgId, join.OrgDisplayId, join.OrgName, join.OrgCode, actorId);
        // ПОМЕТКА «ЭТО РЕПЛИКА» СТАВИТСЯ ПЕРВОЙ (T-3-S1). Раньше она ставилась в конце,
        // и между заведением организации и ею успевал вклиниться кто угодно — сторож
        // репликации, запрос из браузера: контекст организации создавался БЕЗ пометки,
        // засеивал справочники своими навыками и форматами (у них идентификаторы случайные,
        // в отличие от ролей и состояний), и дальше обе стороны навсегда отвергали строки
        // друг друга по UNIQUE(name). Ловилось это только живой проверкой и через раз
        var dir = DirOf(org);
        Directory.CreateDirectory(dir);
        var db = new Database(dir, _deps.DbFile);
        db.Init(ServerDb.NodeId);
        db.SetMeta(OrgContext.ReplicaMetaKey, conductorServerId);
        RefreshCodes();
        var local = Servers.Local() ?? throw new InvalidOperationException(
            Loc.T("msg.clusterJoinFlow.1"));
        // дирижёр организации — тот сервер, к которому подключились; его связь заводим
        // с его же идентификатором, чтобы репликация не создала вторую
        Servers.Attach(org.Id, conductorServerId, OrgServerStatus.Active, requestedBy: "", actorId,
            code: join.ConductorCode.Length > 0 ? join.ConductorCode : "S0",
            id: join.ConductorLinkId, isConductor: true);
        Servers.Attach(org.Id, local.Id, OrgServerStatus.Active, requestedBy: "", actorId,
            code: join.Code, id: join.RequestId, isConductor: false);
        Context(org);
        return org;
    }

    /// <summary>
    /// ПОДТВЕРДИТЬ ЗАЯВКУ на подключение сервера (T-139, доработка) — решение человека
    /// на дирижёре. Кроме самой связи оно заводит ЗАЯВИТЕЛЯ участником организации: аккаунт
    /// с тем же внутренним ключом, той же почтой и ТЕМ ЖЕ хэшем пароля (всё это приехало
    /// в заявке) плюс исполнитель-человек с этим аккаунтом. Поэтому заводить того же человека
    /// на дирижёре руками НЕ НУЖНО и вредно: в кластере у него один аккаунт и один пароль,
    /// выравнивать нечего.
    ///
    /// Ждать, пока аккаунт приедет репликацией, нельзя (поймано живой проверкой): его строку
    /// журнала подключаемый сервер отдаёт только для участников организации, а до решения
    /// человека заявитель им не был — курсор ушёл вперёд, и строка не вернулась бы никогда.
    ///
    /// Единственный отказ — почта заявителя уже занята ДРУГИМ аккаунтом этого сервера:
    /// после репликации в базе оказались бы два аккаунта с одной почтой, и какой из них
    /// спросят при входе, стало бы делом случая.
    /// </summary>
    /// <param name="addMember">Завести заявителя участником организации (T-150). Флажок
    /// формы подтверждения; по умолчанию — да, и почти всегда так и нужно: без исполнителя
    /// человек участником не станет, а значит, не увидит организацию и после репликации.
    /// Снят — подтверждается ТОЛЬКО связь серверов, а человека заводят потом руками.</param>
    public OrgServer AcceptJoinRequest(string linkId, string? actorId, bool addMember = true)
    {
        var link = Servers.LinkById(linkId) ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.8"));
        var applicant = new JoinApplicant(link.ApplicantId, link.ApplicantName, link.ApplicantEmail,
            link.ApplicantHash);
        // тот же дубль ловится и при ПОДАЧЕ заявки (T-141) — там его исправляют, — но
        // аккаунт мог появиться и после неё, пока заявка ждала решения
        if (ApplicantClash(applicant) is { } clash)
        {
            throw new ArgumentException(clash);
        }
        // ЗАЯВИТЕЛЯ ЗАВОДИМ ДО ПОДТВЕРЖДЕНИЯ СВЯЗИ (T-146). Раньше связь подтверждалась первой,
        // и любая заминка на заведении человека оставляла подключение НАПОЛОВИНУ сделанным:
        // сервер уже принят и из списка заявок исчез, а заявитель не участник организации —
        // на своём сервере он видел «вы не состоите ни в одной организации», и повторить
        // решение было нечем. Теперь порядок обратный: сначала человек, потом связь. Сорвётся
        // что-нибудь — заявка останется в списке, и «ПРИНЯТЬ» можно нажать ещё раз
        if (addMember)
        {
            AdmitApplicant(link, applicant, actorId);
        }
        var accepted = Servers.AcceptRequest(linkId, actorId);
        // ПОДКЛЮЧЁННЫЙ СЕРВЕР РЕПЛИЦИРУЕТСЯ САМ (T-153): у новой записи интервал пуст, то есть
        // «только по кнопке», и пара молчала бы, пока человек не откроет форму сервера
        Servers.EnsureReplication(link.ServerId, actorId);
        // хэш пароля в заявке больше не нужен: он сделал своё дело и не должен лежать
        // в реплицируемой строке связи дольше необходимого
        Servers.ForgetApplicantSecret(linkId);
        return accepted;
    }

    /// <summary>
    /// ЗАЯВИТЕЛЬ УЖЕ УЧАСТНИК ЭТОЙ ОРГАНИЗАЦИИ (T-150): имя его исполнителя, иначе пусто.
    ///
    /// Спрашивается формой подтверждения заявки: человек, принимающий решение, должен видеть,
    /// заведётся ли исполнитель или он уже есть. Участие в организации — это и есть
    /// исполнитель-человек с аккаунтом заявителя (ТЗ п. 2.15): без него человек организацию
    /// не увидит, сколько бы репликаций ни прошло.
    /// </summary>
    public string ApplicantMemberName(OrgServer link)
    {
        if (link.ApplicantId.Trim().Length == 0 || Orgs.Get(link.OrgId) is not { } org)
        {
            return "";
        }
        var member = Context(org).Executors.ByAccount(link.ApplicantId);
        return member is null ? "" : (member.InternalName.Length > 0 ? member.InternalName : member.Nick);
    }

    /// <summary>
    /// Завести заявителя участником организации (T-139, порядок изменён в T-146): аккаунт
    /// с его же ключом и паролем плюс исполнитель-человек с этим аккаунтом. Ничего не делает
    /// у старой заявки без заявителя. Идемпотентна: повторный вызов на уже заведённом человеке
    /// возвращает его же — на этом держится и повтор «ПРИНЯТЬ», и починка недоделанных
    /// подключений (<see cref="FinishAcceptedJoins"/>).
    /// </summary>
    private void AdmitApplicant(OrgServer link, JoinApplicant applicant, string? actorId)
    {
        if (applicant.IsEmpty || Orgs.Get(link.OrgId) is not { } org)
        {
            return;   // старая заявка без заявителя — только связь, как раньше
        }
        // аккаунт заявителя — с его же ключом и паролем; уже есть такой ключ — берётся он
        var account = Accounts.Import(applicant.Id, applicant.Name, applicant.Email, phone: "",
            applicant.PasswordHash, actorId);
        // роль обычная (Editor): заявитель — участник чужой организации, а не её владелец;
        // расширить права ему может владелец организации (ТЗ п. 2.2)
        AddMember(Context(org), account, SystemRole.Editor, actorId);
    }

    /// <summary>
    /// ДОВЕСТИ ДО КОНЦА ПОДТВЕРЖДЁННЫЕ ЗАЯВКИ (T-146): связь принята, а заявитель участником
    /// организации так и не стал. До T-146 это оставалось после любой ошибки на приёме заявки
    /// — например, когда имя заявителя совпало с оставшимся исполнителем удалённого
    /// пользователя. Чинится только здесь: заявки в списке уже нет, и нажать «ПРИНЯТЬ» ещё
    /// раз человеку негде.
    ///
    /// Вызывается разовым шагом обновления (<c>Upgrade</c>) на установке, которая уже успела
    /// сломаться. Хэш пароля заявителя при неудачном приёме не стирался, поэтому аккаунт
    /// заводится с настоящим паролем человека; если он всё-таки пуст, а аккаунт уже есть —
    /// не трогаем пароль вовсе (<see cref="AccountService.Import"/>).
    /// </summary>
    /// <returns>Сколько подключений доделано.</returns>
    public int FinishAcceptedJoins(string? actorId = null)
    {
        var done = 0;
        foreach (var org in Orgs.List(includeInactive: true))
        {
            if (!Servers.IsLocalConductor(org.Id))
            {
                continue;   // исполнителей организации заводит её дирижёр (ТЗ гл. 6): на
                            // остальных серверах участник приезжает репликацией
            }
            foreach (var link in Servers.ServersOf(org.Id))
            {
                var applicant = new JoinApplicant(link.ApplicantId, link.ApplicantName,
                    link.ApplicantEmail, link.ApplicantHash);
                if (link.Status != OrgServerStatus.Active || link.DeletedAt is not null
                    || applicant.IsEmpty)
                {
                    continue;
                }
                if (Context(org).Executors.ByAccount(applicant.Id) is not null)
                {
                    continue;   // человек на месте — подключение доделано
                }
                AdmitApplicant(link, applicant, actorId);
                Servers.ForgetApplicantSecret(link.Id);
                done++;
            }
        }
        return done + AdoptStrayAccounts(actorId);
    }

    /// <summary>
    /// АККАУНТ БЕЗ ЕДИНОЙ ОРГАНИЗАЦИИ — вторая, более грубая половина той же починки (T-146).
    ///
    /// Запись заявки на связи живёт недолго: хэш пароля стирается сразу после подтверждения,
    /// а до T-146 первая же репликация затирала и остальные её колонки пустотой партнёра
    /// (<c>ReplicationService.KeepRequestOfLink</c>). Поэтому у установки, которая сломалась
    /// раньше и успела отреплицироваться, точного следа заявителя может уже не быть — остаётся
    /// сам аккаунт, не состоящий НИ В ОДНОЙ организации. Такой человек видит «вы не состоите
    /// ни в одной организации» и работать не может; он же — след и второй поломки, когда
    /// заведение пользователя руками падало на совпадении внутренних имён.
    ///
    /// Делается только там, где выбора нет: организация на сервере ОДНА и её дирижёр — мы.
    /// При нескольких организациях угадывать, в какую пускать человека, нельзя — это решение
    /// владельца, и в журнал пишется предупреждение.
    /// </summary>
    private int AdoptStrayAccounts(string? actorId)
    {
        var mine = Orgs.List(includeInactive: false).Where(o => Servers.IsLocalConductor(o.Id)).ToList();
        var stray = Accounts.List()
            .Where(a => Orgs.List(includeInactive: true).All(o => Context(o).Executors.ByAccount(a.Id) is null))
            .ToList();
        if (stray.Count == 0)
        {
            return 0;
        }
        if (mine.Count == 0)
        {
            // мы не дирижёр ни одной организации: участники приезжают сюда репликацией,
            // и заводить их руками нельзя (ТЗ гл. 6)
            return 0;
        }
        if (mine.Count != 1)
        {
            Serilog.Log.Warning(
                "AI2P: пользователи {Names} не состоят ни в одной организации, а организаций "
                + "на сервере {Count} — в какую их пускать, решает владелец (ТЗ п. 2.15)",
                string.Join(", ", stray.Select(a => a.Email)), mine.Count);
            return 0;
        }
        var context = Context(mine[0]);
        foreach (var account in stray)
        {
            // роль обычная: поднять её может владелец организации (ТЗ п. 2.2)
            AddMember(context, account, SystemRole.Editor, actorId);
            Serilog.Log.Information("AI2P: {Email} возвращён в организацию {Org}",
                account.Email, mine[0].Code);
        }
        return stray.Count;
    }

    /// <summary>
    /// ЗАЯВИТЕЛЬ СПОРИТ С НАШИМ ПОЛЬЗОВАТЕЛЕМ (T-141): почта или имя уже заняты ДРУГИМ
    /// аккаунтом этого сервера. Возвращает объяснение для человека; null — всё чисто.
    ///
    /// Спрашивается ДВАЖДЫ: при подаче заявки (тогда её отклоняют сразу — исправить дубль
    /// должен подключающийся сервер, там человек стоит у экрана и может вернуться на шаг
    /// назад) и при подтверждении (аккаунт мог появиться, пока заявка ждала решения).
    ///
    /// Почта — логин, два аккаунта с одной почтой в кластере невозможны в принципе. Имя
    /// формально уникальным не обязано быть, но после подтверждения оба человека окажутся
    /// в одном списке участников, и различить их будет нельзя — поэтому имя тоже спорное,
    /// и поправить его проще на той стороне.
    /// </summary>
    public string? ApplicantClash(JoinApplicant applicant) => ApplicantClash(Accounts, applicant);

    /// <inheritdoc cref="ApplicantClash(JoinApplicant)"/>
    public static string? ApplicantClash(AccountService accounts, JoinApplicant applicant)
    {
        if (applicant.IsEmpty)
        {
            return null;   // старая заявка без заявителя — как раньше, только связь
        }
        if (accounts.FindByLogin(applicant.Email) is { } byEmail && byEmail.Id != applicant.Id)
        {
            return Loc.T("msg.orgRegistry.1", applicant.Email, byEmail.Name);
        }
        var name = applicant.Name.Trim();
        if (name.Length > 0 && accounts.List().FirstOrDefault(a =>
                a.Id != applicant.Id && string.Equals(a.Name.Trim(), name,
                    StringComparison.OrdinalIgnoreCase)) is { } byName)
        {
            return Loc.T("msg.orgRegistry.2", byName.Name, byName.Email);
        }
        return null;
    }

    public OrgContext? ById(string? orgId) =>
        orgId is { Length: > 0 } && Orgs.Get(orgId) is { DeletedAt: null } org ? Context(org) : null;

    public OrgContext? ByCode(string? code) =>
        code is { Length: > 0 } && Orgs.ByCode(code) is { } org ? Context(org) : null;

    /// <summary>
    /// Организации, в которых аккаунт — участник (ТЗ п. 2.15): участие = исполнитель-человек
    /// с этим аккаунтом в БД организации. Список показывается переключателем в тулбаре.
    ///
    /// К ним добавляются организации, В КОТОРЫЕ ХОЗЯИН ЭТОГО КОМПЬЮТЕРА МОЖЕТ ВСТУПИТЬ САМ
    /// (T-186-S0, <see cref="Organization.CanJoin"/>): организация, приехавшая репликацией,
    /// участников здесь не заводит, и без этого она не видна в переключателе вовсе — жалоба
    /// «на S0 организация есть, а переключиться в неё нечем». Вступление всё равно остаётся
    /// отдельным действием человека (<see cref="AddMember"/> из эндпойнта join).
    /// </summary>
    /// <param name="canJoinForeign">Вошедший — хозяин этого компьютера (владелец системы
    /// либо локальный администратор сервера): ему показываем и чужие организации сервера.</param>
    public List<Organization> OrgsOf(string accountId, bool canJoinForeign = false)
    {
        var result = new List<Organization>();
        foreach (var org in Orgs.List(includeInactive: false))
        {
            org.IsMember = Context(org).Executors.ByAccount(accountId) is not null;
            org.CanJoin = !org.IsMember && canJoinForeign;
            if (org.IsMember || org.CanJoin)
            {
                result.Add(org);
            }
        }
        return result;
    }

    /// <summary>
    /// Аккаунт — ВЛАДЕЛЕЦ системы (T-140): его исполнитель имеет роль owner хотя бы в одной
    /// организации этого сервера. Такого пользователя удалять нельзя — управлять
    /// пользователями станет некому. Роль принадлежит исполнителю ОРГАНИЗАЦИИ (ТЗ п. 2.2),
    /// поэтому вопрос решается здесь, а не в сервисе аккаунтов: он организаций не знает.
    /// Смотрим действующие организации — так же, как <see cref="OrgsOf"/>.
    /// </summary>
    public bool IsOwnerAccount(string accountId)
    {
        foreach (var org in Orgs.List(includeInactive: false))
        {
            if (Context(org).Executors.ByAccount(accountId) is { SystemRole: SystemRole.Owner })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Создать организацию (ТЗ п. 2.15): запись в серверной БД + каталог + БД + справочники,
    /// а создающий становится её первым участником — исполнителем-человеком с ролью owner.
    /// </summary>
    public (Organization Org, OrgContext Context) CreateOrg(string name, string code,
        Account owner, string? actorId)
    {
        var org = Orgs.Create(name, code, actorId);
        RefreshCodes();
        // сервер у организации есть всегда — локальный, он же её дирижёр (ТЗ гл. 6, этап 41)
        LinkLocalServer(org.Id, actorId);
        var context = Context(org);
        AddMember(context, owner, SystemRole.Owner, actorId);
        return (org, context);
    }

    /// <summary>
    /// Сделать аккаунт участником организации: завести исполнителя-человека с этим аккаунтом.
    /// Актор всех действий в журнале — исполнитель (п. 6.4.3), поэтому у участника он есть
    /// всегда. Уже участник — вернуть существующего.
    ///
    /// Идёт мимо правила «исполнители правятся на дирижёре» (ТЗ гл. 6, этап 42): без
    /// исполнителя у вошедшего нет актора действий, и работать в организации он не смог бы
    /// вовсе. Исключений НЕ БРОСАЕТ (T-146): совпадение имён разбирает
    /// <see cref="ExecutorService.EnsureMember"/>, которому мы даём справку об аккаунтах —
    /// они живут в серверной БД, и организация про них ничего не знает (ТЗ п. 6.4.1).
    /// </summary>
    public Executor AddMember(OrgContext context, Account account, SystemRole role, string? actorId) =>
        context.Executors.EnsureMember(account.Id, account.Name, account.Email, role, actorId,
            AccountBrief);

    /// <summary>Почта аккаунта и жив ли он; null — такого аккаунта на этом сервере нет.</summary>
    private MemberAccount? AccountBrief(string accountId) =>
        Accounts.Get(accountId) is { } found
            ? new MemberAccount(found.Email, found.DeletedAt is null)
            : null;

    /// <summary>
    /// УЧАСТИЕ УДАЛЁННОГО ПОЛЬЗОВАТЕЛЯ ГАСНЕТ ВО ВСЕХ ОРГАНИЗАЦИЯХ (T-146). Аккаунт удаляют
    /// на уровне установки (T-140), а участие — это исполнитель в базе каждой организации:
    /// без этого удалённый человек оставался бы в подборе исполнителей и в составе команд.
    /// Сам исполнитель не вычёркивается: он актор прошлых действий в журнале работ (ТЗ п. 6.4.3).
    /// </summary>
    public void SuspendMemberships(string accountId, string? actorId)
    {
        foreach (var org in Orgs.List(includeInactive: true))
        {
            Context(org).Executors.SuspendMember(accountId, actorId);
        }
    }

    /// <summary>Исполнитель аккаунта в любой из его организаций — актор для событий, которые
    /// происходят ДО выбора организации (вход и выход, ТЗ п. 6.4.3); null — не участник.</summary>
    public Executor? AnyMemberOf(string accountId)
    {
        foreach (var org in Orgs.List(includeInactive: false))
        {
            if (Context(org).Executors.ByAccount(accountId) is { } executor)
            {
                return executor;
            }
        }
        return null;
    }

    /// <summary>Открыть все активные организации при старте: досеять справочники и запустить
    /// их расписания (ТЗ п. 2.12) — до первого захода пользователя.</summary>
    public void StartAll()
    {
        RefreshCodes();
        foreach (var org in Orgs.List(includeInactive: false))
        {
            // организация, заведённая до появления серверов (этап 40), связи ещё не имеет:
            // локальный сервер получает в ней S0 и становится дирижёром (ТЗ гл. 6)
            LinkLocalServer(org.Id, actorId: null);
        }
        // ПОЧИНКА ИДЁТ ДО ОТКРЫТИЯ ОРГАНИЗАЦИЙ (T-148-S0, второй заход): номер и код сервера
        // попадают в номера задач и в подписи владельца, и чинить их под уже работающими
        // сервисами — значит оставить их разными в памяти и в базе
        FixDuplicateNumbers();
        FixLocalServerCodes();
        foreach (var org in Orgs.List(includeInactive: false))
        {
            Context(org);
        }
    }

    /// <summary>
    /// ДВЕ ОРГАНИЗАЦИИ С ОДНИМ НОМЕРОМ (T-148-S0) — починка уже сложившихся установок.
    ///
    /// Номер выдавался счётчиком, а организация чужого дирижёра приезжает сюда со своим
    /// (<see cref="OrgService.Adopt"/>) и счётчик не двигает, — поэтому сервер, подключённый
    /// к чужой <c>ORG-1</c>, выдавал своей первой организации ту же <c>ORG-1</c>. Новые
    /// номера столкнуться уже не могут, а вот выданные раньше так и остались в базе.
    ///
    /// Правило: номер остаётся за организацией, ПРИЕХАВШЕЙ ОТ ЧУЖОГО ДИРИЖЁРА (он общий
    /// на кластер, и менять его у себя нельзя), а перенумеровывается заведённая здесь —
    /// независимо от того, какая из них появилась на этом компьютере раньше. Каталог с данными
    /// не переименовывается: он уже записан (<c>org_dirs</c>), в нём лежат файлы, и его имя
    /// не обязано совпадать с номером — человеку каталог показывается в списке организаций.
    ///
    /// Признак «приехала» — отметка <c>replica_of</c> в её базе. Прежнее условие «дирижёр — мы»
    /// оказалось хрупким: у организации, записавшей локальный сервер под ЧУЖИМ кодом
    /// (жалоба № 1), дирижёр вычислялся неверно, и починка молча пропускала ровно тот случай,
    /// ради которого заведена.
    /// </summary>
    private void FixDuplicateNumbers()
    {
        var all = Orgs.List(includeInactive: true);
        var replica = all.ToDictionary(o => o.Id, IsReplica);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in all.OrderBy(o => replica[o.Id] ? 0 : 1).ThenBy(o => o.CreatedAt))
        {
            if (org.DisplayId.Length == 0 || seen.Add(org.DisplayId))
            {
                continue;
            }
            if (replica[org.Id])
            {
                Serilog.Log.Warning("AI2P: организации {Code} и соседняя носят один номер {Id}, "
                    + "но она приехала от чужого дирижёра — номер общий на кластер (ТЗ гл. 6)",
                    org.Code, org.DisplayId);
                continue;
            }
            var number = Orgs.Renumber(org.Id, actorId: null);
            seen.Add(number);
            Serilog.Log.Information("AI2P: организация {Code} перенумерована {Was} → {Now}",
                org.Code, org.DisplayId, number);
        }
    }

    /// <summary>
    /// ОРГАНИЗАЦИЯ ПРИЕХАЛА ОТ ЧУЖОГО ДИРИЖЁРА (отметка <c>replica_of</c> в её базе).
    /// Спрашивается ДО открытия организации, поэтому база читается напрямую, а не через
    /// контекст: починка обязана идти раньше, чем поднимутся сервисы. Базы ещё нет
    /// (организация ни разу не открывалась) — значит, её завели здесь.
    /// </summary>
    private bool IsReplica(Organization org)
    {
        var dir = DirOf(org);
        if (!File.Exists(Path.Combine(dir, _deps.DbFile)))
        {
            return false;
        }
        try
        {
            return new Database(dir, _deps.DbFile).Meta(OrgContext.ReplicaMetaKey)
                is { Length: > 0 };
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            // базу не прочитать — считаем организацию своей: хуже не переименовать вовсе
            Serilog.Log.Warning("AI2P: базу организации {Code} не прочитать: {Why}",
                org.Code, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// ЛОКАЛЬНЫЙ СЕРВЕР ЗАПИСАН В ОРГАНИЗАЦИИ ПОД ЧУЖИМ КОДОМ (T-148-S0, второй заход) —
    /// починка уже сложившихся установок.
    ///
    /// До первого захода новая организация всегда выдавала первому своему серверу <c>S0</c>,
    /// поэтому на машине, которую в кластере зовут <c>S1</c>, все заведённые здесь организации
    /// записали её как <c>S0</c> — код СОСЕДНЕЙ машины. Со стороны это выглядит так, будто
    /// организация уехала на другой компьютер, а заодно ломает починку номеров: у такой
    /// организации дирижёр вроде бы «не мы».
    ///
    /// Чинится только там, где код выдали МЫ САМИ: в организации ровно один сервер (наш)
    /// и дирижёр — он же. Код организации, где серверов несколько, выдал дирижёр кластера,
    /// и менять его у себя нельзя ни при каких обстоятельствах.
    /// </summary>
    private void FixLocalServerCodes()
    {
        if (Servers.Local() is not { } local)
        {
            return;
        }
        var known = Servers.ClusterCodeOf(local.Id);
        if (known.Length == 0)
        {
            return;
        }
        foreach (var org in Orgs.List(includeInactive: true))
        {
            var links = Servers.ServersOf(org.Id, includeRequests: false);
            var mine = links.FirstOrDefault(l => l.ServerId == local.Id);
            if (mine is null || mine.Code == known)
            {
                continue;
            }
            if (links.Count > 1 || !mine.IsConductor || IsReplica(org))
            {
                Serilog.Log.Warning("AI2P: в организации {Code} этот сервер зовут {Was}, "
                    + "а в кластере — {Now}; код выдаёт дирижёр (ТЗ гл. 6)",
                    org.Code, mine.Code, known);
                continue;
            }
            try
            {
                Servers.SetCode(org.Id, local.Id, known, actorId: null);
                Serilog.Log.Information("AI2P: код этого сервера в организации {Code} "
                    + "исправлен {Was} → {Now}", org.Code, mine.Code, known);
            }
            catch (ArgumentException ex)
            {
                Serilog.Log.Warning("AI2P: код сервера в организации {Code} не исправлен: {Why}",
                    org.Code, ex.Message);
            }
        }
    }

    // --- УДАЛЕНИЕ ОРГАНИЗАЦИИ С ЭТОГО СЕРВЕРА (T-148-S0, второй заход) ---
    //
    // Удаление идёт НА КАЖДОМ СЕРВЕРЕ ПО ОТДЕЛЬНОСТИ и не реплицируется: соседи про наше
    // решение не узнают, а мы про их. Поэтому оно и не мягкое — снимать надо всё: строки
    // серверной БД (связи с серверами, имя каталога, состояние и очередь репликации),
    // саму базу организации со всеми шаблонами, правилами и справочниками и её каталог
    // с файлами проектов.
    //
    // Разрешено только у ПУСТОЙ организации: ни одной задачи и ни одного архива (условие
    // заказчика). Шаблоны, правила безопасности, справочники, опыт и настройки удаляются
    // вместе с базой — они мусор ровно в том же смысле, что и сама организация.

    /// <summary>
    /// ПОЧЕМУ ОРГАНИЗАЦИЮ НЕЛЬЗЯ УДАЛИТЬ — словами для человека; null — можно.
    /// Спрашивается и формой (кнопка гасится с объяснением), и самим удалением.
    /// </summary>
    public string? DeleteProblem(string orgId)
    {
        if (Orgs.Get(orgId) is not { DeletedAt: null } org)
        {
            return Loc.T("msg.org.3");
        }
        if (Orgs.List(includeInactive: true).Count <= 1)
        {
            // последнюю удалять нельзя: без организаций система показывает экран первого
            // старта, и человек, нажавший «удалить» в настройках, оказался бы в мастере
            return Loc.T("msg.org.7");
        }
        var context = Context(org);
        var tasks = TaskCount(context);
        if (tasks > 0)
        {
            return Loc.T("msg.org.8", tasks);
        }
        if (context.Archives.List().Count is var archives and > 0)
        {
            return Loc.T("msg.org.9", archives);
        }
        return null;
    }

    /// <summary>Сколько в организации задач — шаблоны не считаются: шаблон живёт в той же
    /// таблице, но это не работа, а заготовка, и он приходит с дистрибутивом (TemplateSeed).</summary>
    private static long TaskCount(OrgContext context)
    {
        using var conn = context.Db.Open();
        return Sql.Scalar<long>(conn, null,
            "SELECT COUNT(*) FROM tasks WHERE is_template=0 AND deleted_at IS NULL");
    }

    /// <summary>
    /// УДАЛИТЬ ОРГАНИЗАЦИЮ С ЭТОГО СЕРВЕРА: записи серверной БД, база и каталог с файлами.
    /// Отказ (<see cref="DeleteProblem"/>) — <see cref="ArgumentException"/>.
    ///
    /// Порядок обязателен: сперва закрываются живые сервисы организации (у SQLite нельзя
    /// удалить файл открытой базы, а расписание и сторожа продолжали бы её открывать),
    /// потом строки серверной БД и только в конце каталог. Каталог берётся по
    /// <c>org_dirs</c>, а НЕ по номеру <c>ORG-N</c>: у организации, чей номер когда-то
    /// столкнулся с приехавшей, каталог называется иначе (<c>ORG-1-2</c>), и удаление
    /// «по номеру» снесло бы ЧУЖИЕ данные.
    /// </summary>
    public void DeleteOrg(string orgId, string? actorId)
    {
        if (DeleteProblem(orgId) is { Length: > 0 } problem)
        {
            throw new ArgumentException(problem);
        }
        var org = Orgs.Get(orgId) ?? throw new ArgumentException(Loc.T("msg.org.3"));
        var dir = DirOf(org);
        lock (_lock)
        {
            ForgetArchiveContexts(orgId);
            if (_contexts.TryRemove(orgId, out var context))
            {
                context.Dispose();
            }
            Orgs.Purge(orgId, actorId);
        }
        RefreshCodes();
        // ключ организации (ТЗ гл. 10) хранится вне базы — с ним уходит и он
        new AI2P.Connectors.OrgSecretKey(_deps.Secrets).Forget(orgId);
        // пул соединений у нас выключен (Database), поэтому закрытый контекст файл базы
        // уже отпустил — каталог удаляется сразу
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // записей уже нет, и организация с сервера ушла; каталог остался — это видно
            // человеку и убирается руками, а падать после удаления записей нельзя
            Serilog.Log.Warning("AI2P: каталог организации {Dir} не удалён: {Why}", dir, ex.Message);
        }
    }

    public void Dispose()
    {
        foreach (var context in _contexts.Values)
        {
            context.Dispose();
        }
        _contexts.Clear();
    }
}
