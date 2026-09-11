using AI2P.Core;
using System.Text.Json;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Org;

namespace AI2P.Server.Api;

/// <summary>
/// Протокол сервер-сервер (ТЗ гл. 6, гл. 12; этап 41) — раздел <c>/api/cluster</c>.
///
/// Сюда ходят ДРУГИЕ серверы кластера, а не люди, поэтому раздел живёт отдельной группой
/// и не проходит проверку прав пользователя (<see cref="ApiPermissions"/>): личность
/// подтверждается токеном сервера в заголовке <c>X-AI2P-Server</c>, выданным при подключении.
///
/// Открыты без токена ровно две операции — «представься» и «подать заявку на подключение»:
/// до подтверждения человеком токена ещё нет, а получить его иначе неоткуда. Обе ничего
/// не меняют в данных организаций и наружу отдают только имя сервера и состояние заявки.
///
/// Подключение обязательно ДВУСТОРОННЕЕ (todo41 п. 7): заявка приходит сюда, а решение
/// принимает человек на этом сервере — заявка появляется отдельной строкой внизу списка
/// серверов и модальным окном у владельца (пп. 8–10). Подключаются всегда к ДИРИЖЁРУ:
/// коды серверов в организации выдаёт он.
/// </summary>
public static class ClusterEndpoints
{
    public static void MapAi2pCluster(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<OrgRegistry>();
        var servers = registry.Servers;
        // ключ организации (ТЗ гл. 10, этап 45): выдаётся подключившемуся серверу вместе
        // с токеном доступа, то есть только после подтверждения заявки человеком
        var orgKeys = new AI2P.Connectors.OrgSecretKey(
            app.Services.GetRequiredService<AI2P.Connectors.SecretStore>());
        var cluster = app.MapGroup("/api/cluster").AllowAnonymous();

        // --- «представься»: связь есть и на том конце AI2P (токена не требует) ---
        cluster.MapGet("/hello", () => new ClusterHelloDto
        {
            ServerId = servers.Local()?.Id ?? "",
            Name = servers.Local()?.Name ?? "",
            Version = Core.AppInfo.Version,
        });

        // --- проверка связи по выданному токену: отвечаем, только если узнали звонящего ---
        cluster.MapGet("/ping", (HttpContext ctx) =>
        {
            if (Caller(ctx, registry) is null)
            {
                return Results.Problem(Loc.T("msg.clusterEndpoints.1"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            return Results.Ok(new ClusterHelloDto
            {
                ServerId = servers.Local()?.Id ?? "",
                Name = servers.Local()?.Name ?? "",
                Version = Core.AppInfo.Version,
            });
        });

        // --- заявка на подключение сервера к организации (двусторонний обмен, п. 7) ---
        cluster.MapPost("/join", (HttpContext ctx, ClusterJoinDto dto) => ApiEndpoints.Handle(() =>
        {
            // ИМЯ ЭТОГО СЕРВЕРА БОЛЬШЕ НЕ ПРОВЕРЯЕТСЯ (T-141). До этого заявка ИЗВНЕ
            // отклонялась, если дирижёр назван «localhost»: считалось, что подключившийся
            // сервер получит этот адрес репликацией и станет ходить к самому себе. На деле
            // постоянного адреса у дирижёра может не быть вовсе (динамический IP за роутером),
            // и это законно: адрес дирижёра, набранный человеком, теперь не затирается
            // петлевым адресом из репликации (см. ReplicationService.Guard).
            //
            // КОД ОРГАНИЗАЦИИ НЕОБЯЗАТЕЛЕН (T-139): организация на дирижёре одна — берётся она.
            // Несколько — отвечаем ПЕРЕЧНЕМ (T-141), и человек на той стороне отмечает нужные
            var choices = new List<ClusterOrgBriefDto>();
            var org = dto.OrgCode.Trim().Length > 0
                ? registry.Orgs.ByCode(dto.OrgCode)
                  ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.2", dto.OrgCode))
                : TheOnlyOrg(registry, out choices);
            if (org is null)
            {
                return Results.Ok(new ClusterJoinStatusDto
                {
                    Status = ClusterJoinStatusDto.ChooseOrg,
                    ServerId = servers.Local()?.Id ?? "",
                    Orgs = choices,
                });
            }
            if (!servers.IsLocalConductor(org.Id))
            {
                // коды серверов выдаёт дирижёр — подключаться нужно именно к нему (п. 7)
                var conductor = servers.Conductor(org.Id);
                throw new ArgumentException(
                    Loc.T("msg.clusterEndpoints.3", org.Name)
                    + (conductor is null ? "" : Loc.T("msg.clusterEndpoints.4", conductor.Url)));
            }
            if (dto.ServerId.Trim().Length == 0)
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.5"));
            }
            if (servers.Local() is { } self && self.Id == dto.ServerId)
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.6"));
            }
            // ЗАЯВИТЕЛЬ (T-139, доработка): заявка анонимна — реквизитов дирижёра у подающего
            // нет, — но безымянной быть не должна: решение принимает человек, и он обязан
            // видеть, КОГО пускает. Почта заявителя — логин его аккаунта; при подтверждении
            // он станет участником организации, а его аккаунт приедет репликацией
            var applicant = new JoinApplicant(dto.ApplicantId, dto.ApplicantName,
                dto.ApplicantEmail, dto.ApplicantPasswordHash);
            if (applicant.IsEmpty)
            {
                throw new ArgumentException(
                    Loc.T("msg.clusterEndpoints.7"));
            }
            // ТОТ ЖЕ ЧЕЛОВЕК С ДРУГИМ КЛЮЧОМ (T-148) — обычное дело при ПОВТОРНОМ подключении:
            // сервер переставили, его аккаунт завёлся заново и получил новый внутренний ключ,
            // а здесь человек с этой почтой остался с прежним (его завело подтверждение
            // прошлой заявки). Почта — это логин, значит человек ОДИН: отказывать незачем,
            // отвечаем «он у меня уже есть, вот его ключ». Подключающийся сервер возьмёт этот
            // ключ себе и подаст заявку заново. До T-148 здесь был отказ, и подключиться
            // было нельзя ничем, кроме удаления живого участника на дирижёре руками.
            //
            // НО КЛЮЧ ВЫДАЁТСЯ ТОЛЬКО ПО ПАРОЛЮ (T-150). Отдавать ключ живого участника
            // всякому, кто назвал его почту, нельзя: это чужая личность в организации, а
            // почту человека знает кто угодно. Поэтому сначала спрашиваем пароль ЭТОГО
            // человека здесь — по сути вход на дирижёр, который он и так знает
            if (registry.Accounts.FindByLogin(applicant.Email) is { } known
                && known.Id != applicant.Id)
            {
                var check = PasswordCheck(registry.Accounts, known, dto.ApplicantPassword);
                return Results.Ok(new ClusterJoinStatusDto
                {
                    Status = ClusterJoinStatusDto.SameEmail,
                    ServerId = servers.Local()?.Id ?? "",
                    // ключ — только доказавшему; иначе пусто, и брать подающему нечего
                    ApplicantId = check.Length == 0 ? known.Id : "",
                    ApplicantName = known.Name,
                    PasswordCheck = check,
                });
            }
            // ДУБЛЬ ИМЕНИ (T-141): проверяется СРАЗУ, при подаче заявки, а не при её
            // подтверждении. Исправлять дубль должен подключающийся сервер — там человек
            // прямо сейчас стоит у экрана подключения и может вернуться шагом назад и
            // поправить своё имя. На дирижёре в этот момент нет никого
            if (registry.ApplicantClash(applicant) is { } clash)
            {
                throw new ArgumentException(clash);
            }
            // запись о сервере заводим сразу, но НЕАКТИВНОЙ: пока человек не подтвердил
            // заявку, сервер в кластере не работает. ПОСЛЕ проверок заявителя (T-148):
            // раньше отказ оставлял на дирижёре запись сервера, которой никто не просил,
            // и в списке висел «сервер», не подавший ни одной заявки.
            //
            // СОВПАДЕНИЕ АДРЕСОВ БОЛЬШЕ НЕ ОТКАЗ (T-141). Раньше запись подключающегося
            // не заводилась, если его адрес уже занят (обычно нами самими: у всех установок
            // имя хоста было «localhost»). Но адрес — не признак сервера: за NAT публичного
            // адреса нет ни у кого, кроме одного, и все остальные законно называются
            // «localhost». Различают серверы по внутреннему ключу (см. ServerAddress)
            var node = servers.Get(dto.ServerId);
            node ??= servers.Create(new AI2P.Storage.Services.ServerSaveInput
            {
                Name = dto.Name,
                Protocol = dto.Protocol,
                Hostname = dto.Hostname,
                Port = dto.Port,
                BasePath = dto.BasePath,
                IsActive = false,
            }, actorId: null, id: dto.ServerId);
            var link = servers.Attach(org.Id, node.Id, OrgServerStatus.Pending,
                dto.RequestedBy.Trim().Length > 0 ? dto.RequestedBy : applicant.Display(),
                actorId: null, applicant: applicant);
            // перечитываем: подавшему нужны название и код организации, а их подставляет
            // выборка со связкой, а не сам Attach
            return Results.Ok(Status(registry, servers.LinkById(link.Id) ?? link, orgKeys));
        }));

        // --- ОБЪЯВЛЕНИЕ О СМЕНЕ ДИРИЖЁРА (T-21-S1) ---
        //
        // Третья операция раздела, открытая без токена, — и по той же причине, что первые
        // две: токена у этой пары может не быть вовсе. Все реплицируются только с дирижёром
        // (звезда), поэтому с новым дирижёром рядовой сервер мог ни разу не разговаривать,
        // а в аварийном случае (прежний дирижёр физически потерян) посредника нет.
        //
        // Подлинность доказывает ПОДПИСЬ КЛЮЧОМ ОРГАНИЗАЦИИ (ТЗ гл. 10): 32 байта, которые
        // получает каждый сервер при подтверждении своей заявки на подключение и которые
        // не реплицируются никогда. Знание ключа = членство в организации; объявить себя
        // дирижёром вправе любой её сервер, и это ровно то, что требует одностороннее
        // назначение при потере дирижёра.
        cluster.MapPost("/conductor/announce", (ClusterConductorDto dto) => ApiEndpoints.Handle(() =>
        {
            var org = registry.Orgs.ByCode(dto.OrgCode)
                      ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.2", dto.OrgCode));
            if (dto.ServerId.Trim().Length == 0 || servers.Local()?.Id == dto.ServerId)
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.12"));
            }
            var payload = ConductorProof.Payload(org.Id, dto.ServerId, dto.RequestId, dto.Token,
                dto.Timestamp);
            if (!ConductorProof.Verify(orgKeys.Read(org.Id), payload, dto.Signature, dto.Timestamp,
                    DateTime.UtcNow))
            {
                // подпись не сходится либо объявление несвежее — это не «ошибка данных»,
                // а отказ в доверии: отвечаем так же, как на чужой токен
                return Results.Problem(Loc.T("msg.clusterEndpoints.13"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            // объявивший обязан быть заведён у нас и связан с этой организацией: иначе это
            // не смена дирижёра, а попытка ввести в кластер неизвестный сервер
            if (servers.Link(org.Id, dto.ServerId) is not { Status: OrgServerStatus.Active })
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.14"));
            }
            var token = ConductorFlow.AcceptAnnounce(registry, org, dto.ServerId, dto.Token,
                dto.RequestId);
            Serilog.Log.Information("Кластер {Org}: дирижёром объявил себя сервер {Server} — "
                                    + "запись обновлена (T-21-S1)", org.Code,
                servers.NameOf(dto.ServerId));
            return Results.Ok(new ClusterConductorAckDto
            {
                ServerId = servers.Local()?.Id ?? "",
                Token = token,
                Applied = true,
            });
        }));

        // --- ПРИГЛАШЕНИЕ В ОРГАНИЗАЦИЮ (T-170-S0) ---
        //
        // Обратное направление подключения. Обычно сервер просится в организацию сам (/join),
        // и человек на дирижёре подтверждает заявку. Но список серверов организации правится
        // и с другой стороны — галочкой в форме организации на дирижёре (todo41 п. 11), и
        // тогда партнёр об этой организации не знает вовсе: сеанс репликации он отвергает
        // словами «организация не найдена», и так навсегда (жалоба T-170-S0).
        //
        // Приглашение принимается ТОЛЬКО ПО ТОКЕНУ, то есть от сервера, который уже в нашем
        // кластере: заводить у себя организацию по слову неизвестного сервера нельзя. Тело —
        // та же сводка, что и в ответе на подтверждённую заявку, и заводится организация тем
        // же способом (AdoptOrg): идентификаторы обязаны совпасть с дирижёрскими.
        cluster.MapPost("/org/invite", (HttpContext ctx, ClusterJoinStatusDto dto) =>
            ApiEndpoints.Handle(() =>
        {
            var peer = Caller(ctx, registry)
                       ?? throw new UnauthorizedAccessException(Loc.T("msg.clusterEndpoints.1"));
            if (dto.OrgId.Trim().Length == 0 || dto.OrgCode.Trim().Length == 0
                || dto.Token.Trim().Length == 0 || dto.RequestId.Trim().Length == 0
                || dto.Status != OrgServerStatus.Active)
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.15"));
            }
            var local = servers.Local()?.Id ?? "";
            // ОРГАНИЗАЦИЯ УЖЕ ЕСТЬ — приглашение повторное, и это норма: дирижёр шлёт его
            // каждым сеансом, пока пара ни разу не отреплицировалась удачно. Ничего не
            // трогаем: связи и коды приедут журналом изменений, а не отсюда
            if (registry.Orgs.Get(dto.OrgId) is not null)
            {
                return Results.Ok(new ClusterConductorAckDto { ServerId = local, Applied = false });
            }
            // токен, которым мы будем ходить к пригласившему (его выдал он сам), и ключ
            // организации: без ключа её ключи API приехали бы нерасшифруемыми (ТЗ гл. 10)
            servers.SetPeerToken(peer.Id, dto.Token);
            orgKeys.Import(dto.OrgId, dto.OrgKey);
            var org = registry.AdoptOrg(dto, peer.Id, actorId: null);
            // автоматические сеансы со своей стороны (T-153): интервал принадлежит записи
            // рядового сервера, то есть НАШЕЙ, и до этого мог быть пуст — «только по кнопке»
            if (servers.Local() is { } self)
            {
                servers.EnsureReplication(self.Id, actorId: null);
            }
            Serilog.Log.Information("Кластер {Org}: сервер {Server} включил нас в организацию — "
                                    + "она заведена, начинается репликация (T-170-S0)",
                org.Code, peer.Name);
            return Results.Ok(new ClusterConductorAckDto
            {
                ServerId = local,
                // встречный токен: пригласивший обращается к нам им же (ТЗ гл. 12)
                Token = servers.IssueToken(peer.Id),
                Applied = true,
            });
        }));

        // --- ГДЕ Я У ТЕБЯ ЗНАЧУСЬ (T-312) ---
        //
        // Обратная сторона приглашения. Приглашение шлёт ДИРИЖЁР, и уходит оно только тогда,
        // когда дирижёр САМ звонит партнёру, — а звонит он далеко не всегда: сервер за NAT
        // назвался петлевым адресом, и до него не дозвониться вовсе (T-141). Тогда получался
        // тупик навсегда: дирижёр не звонит, потому что некуда, а партнёр не звонит, потому
        // что об организации не знает. Ровно это и видел человек — сервер отмечен галочкой
        // в форме организации, а репликация не начинается (жалоба T-312).
        //
        // Поэтому спрашивать может и рядовой сервер. Вопрос идёт ПО ТОКЕНУ, и в ответе только
        // те организации, где связь ИМЕННО С НИМ активна и дирижёр — мы: состав организаций
        // не должен быть виден каждому, кто дотянулся по сети (T-139).
        cluster.MapGet("/orgs/mine", (HttpContext ctx) => ApiEndpoints.Handle(() =>
        {
            var peer = Caller(ctx, registry)
                       ?? throw new UnauthorizedAccessException(Loc.T("msg.clusterEndpoints.1"));
            var list = new List<ClusterJoinStatusDto>();
            foreach (var org in registry.Orgs.List(includeInactive: false))
            {
                if (!servers.IsLocalConductor(org.Id))
                {
                    continue;   // коды и токены выдаёт дирижёр — за него не отвечаем
                }
                if (servers.Link(org.Id, peer.Id) is { Status: OrgServerStatus.Active } link
                    && link.IsActive)
                {
                    list.Add(Status(registry, link, orgKeys));
                }
            }
            return Results.Ok(new ClusterOrgListDto { Orgs = list });
        }));

        // --- решение по заявке: его опрашивает подавший сервер ---
        cluster.MapGet("/join/{requestId}", (string requestId) => ApiEndpoints.Handle(() =>
        {
            var link = servers.LinkById(requestId)
                       ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.8"));
            return Results.Ok(Status(registry, link, orgKeys));
        }));

        // Списка организаций по логину («POST /api/cluster/orgs») здесь больше НЕТ (T-139,
        // доработка): ради него подающий сервер спрашивал у человека почту и пароль аккаунта
        // НА ДИРИЖЁРЕ — то есть требовал отдать чужому серверу свои реквизиты, да ещё и
        // по открытому HTTP. Организацию называют кодом, а если она на дирижёре одна —
        // не называют вовсе (см. /join и TheOnlyOrg).

        // --- репликация (ТЗ п. 6.1, гл. 6; этап 43) ---
        var repl = app.Services.GetRequiredService<Org.ReplicationService>();

        // организация сеанса и звонящий сервер; иначе — исключение с понятным текстом
        (Organization Org, ServerNode Peer) Pair(HttpContext ctx, string orgCode)
        {
            var peer = Caller(ctx, registry)
                       ?? throw new UnauthorizedAccessException(
                           Loc.T("msg.clusterEndpoints.1"));
            var org = registry.Orgs.ByCode(orgCode)
                      ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.2", orgCode));
            if (servers.Link(org.Id, peer.Id) is not { Status: OrgServerStatus.Active })
            {
                throw new ArgumentException(
                    Loc.T("msg.clusterEndpoints.9", peer.Name, org.Name));
            }
            // СЕАНС ЖИВ (T-174): каждый запрос партнёра отодвигает срок протухания замка
            // пары. Пассивная сторона сама ничего не делает и о ходе сеанса узнаёт только
            // из этих запросов — по ним и видно, что ведущий на месте
            repl.Touch(org.Id, peer.Id);
            return (org, peer);
        }

        // То же опознание звонящего, но БЕЗ отметки «сеанс жив» (T-13-S0): за файлом сосед
        // приходит когда угодно — человек открыл карточку с картинкой, — и продлевать этим
        // замок сеанса репликации нельзя: оборванный сеанс не протух бы, пока кто-то
        // смотрит чужие картинки
        (Organization Org, ServerNode Peer) FilePair(HttpContext ctx, string orgCode)
        {
            var peer = Caller(ctx, registry)
                       ?? throw new UnauthorizedAccessException(Loc.T("msg.clusterEndpoints.1"));
            var org = registry.Orgs.ByCode(orgCode)
                      ?? throw new ArgumentException(Loc.T("msg.clusterEndpoints.2", orgCode));
            if (servers.Link(org.Id, peer.Id) is not { Status: OrgServerStatus.Active })
            {
                throw new ArgumentException(Loc.T("msg.clusterEndpoints.9", peer.Name, org.Name));
            }
            return (org, peer);
        }

        // начать сеанс: занимаем пару. Busy — сеанс уже идёт (его начали МЫ САМИ), и тогда
        // ЕГО запуск игнорируется (todo43 «второй запуск игнорируется»). Незакрытый прошлый
        // сеанс ЭТОГО ЖЕ партнёра «занято» не значит — он же и звонит (T-174)
        cluster.MapPost("/repl/begin", (HttpContext ctx, ReplBeginDto dto) => ApiEndpoints.Handle(() =>
        {
            var (org, peer) = Pair(ctx, dto.OrgCode);
            var mine = repl.HoldForPeer(org.Id, peer.Id, out _);
            if (mine)
            {
                // СЕАНС ПАРЫ НАЧАЛСЯ, ХОТЬ И НЕ НАМИ (T-20-S1). Пассивная сторона отмечала
                // только удачное окончание (LastOkAt), поэтому «Последняя попытка» на экране
                // диагностики жила своей жизнью: у сервера, которому звонить некуда (партнёр
                // за NAT назвался петлевым адресом) или который просто никогда не звонит
                // первым, там стояло давнее время ручного пуска — при том, что обмен шёл
                // каждые несколько минут. Теперь это время последнего СЕАНСА пары
                var begun = registry.ReplState.Get(org.Id, peer.Id);
                begun.LastRunAt = DateTime.UtcNow;
                registry.ReplState.Save(begun);
                // ЖАЛОБА НА СВЯЗЬ УСТАРЕЛА УЖЕ СЕЙЧАС (T-50-S0). Снимал её только удачный
                // КОНЕЦ сеанса (/repl/end), а партнёр, начавший сеанс, — это уже доказательство
                // связи: до нас дозвонились. Пока условия не было, «Нет связи с сервером …»
                // висела в списке и в форме даже там, где обмен идёт каждые несколько минут,
                // и строка списка при этом бодро показывала «скоро»
                servers.SetLastError(peer.Id, "");
            }
            var context = registry.Context(org);
            using var orgConn = context.Db.Open();
            using var srvConn = registry.ServerDb.Open();
            return Results.Ok(new ReplSessionDto
            {
                ServerId = servers.Local()?.Id ?? "",
                OrgId = org.Id,
                Busy = !mine,
                OrgHead = AI2P.Storage.ChangeLog.Head(orgConn),
                ServerHead = AI2P.Storage.ChangeLog.Head(srvConn),
            });
        }));

        cluster.MapPost("/repl/end", (HttpContext ctx, ReplBeginDto dto) => ApiEndpoints.Handle(() =>
        {
            var (org, peer) = Pair(ctx, dto.OrgCode);
            repl.Release(org.Id, peer.Id);
            // сеанс вёл партнёр, а отложенные строки разбирать всё равно надо (T-160):
            // родитель, которого не хватало, мог приехать этим самым сеансом
            repl.RetryPending(org, peer);
            // сеанс закрыт успешно — отмечаем это и у ПАССИВНОЙ стороны (этап 45): по времени
            // прошлого удачного сеанса она понимает, менялся ли ключ API у неё с тех пор,
            // а без отметки любое расхождение значений считалось бы конфликтом вечно
            var state = registry.ReplState.Get(org.Id, peer.Id);
            state.LastOkAt = DateTime.UtcNow;
            // и снимаем ошибку прошлой НАШЕЙ попытки (T-153): к серверу за NAT дирижёр
            // не дозванивается и каждый свой заход записывает ошибкой, а обмен при этом
            // идёт — его ведёт тот сервер сам. Пара только что отреплицировалась удачно,
            // и красная строка в списке серверов ей больше не соответствует
            state.Status = ReplicationStatuses.Idle;
            state.LastError = "";
            registry.ReplState.Save(state);
            servers.SetLastError(peer.Id, "");
            // сеанс вёл партнёр, но поручения приехали НАМ (T-196): заявка на запуск нашей
            // задачи и ответ на вопрос нашего агента. Разбираем их сразу, как и на активной
            // стороне, — иначе они ждали бы минутного прохода сторожа
            Org.ReplicationService.AfterReplication(registry.Context(org));
            // СМЕНА ТЕКУЩЕГО АРХИВА исполняется ЗДЕСЬ (T-47-S0), в самом конце сеанса, —
            // потому что к этой минуте прежний текущий архив уже дослал свои данные (архивы
            // ведущий переносит перед закрытием сеанса). Это и есть требование ТЗ «сначала
            // предыдущий архив реплицирует свои данные, и только потом перестаёт быть текущим»
            repl.Archives.ApplyHandovers(registry.Context(org), peer.Id);
            return Results.Ok(new ReplSessionDto { ServerId = servers.Local()?.Id ?? "", OrgId = org.Id });
        }));

        // отдать изменения после курсора. Свои звонящему не возвращаем — это гасит эхо;
        // из серверной БД отдаём только то, что относится к организации сеанса
        cluster.MapGet("/repl/changes", (HttpContext ctx, string org, string scope, long after, int limit) =>
            ApiEndpoints.Handle(() =>
            {
                var (organization, peer) = Pair(ctx, org);
                var context = registry.Context(organization);
                var server = scope == ReplScopes.Server;
                // видов обмена три: серверная БД, база организации и база АРХИВА (T-47-S0).
                // Архива здесь может не быть вовсе (он закрыт .zip'ом) — тогда отдавать
                // нечего, и партнёр получает пустую пачку, а не ошибку
                var archiveId = ReplScopes.ArchiveOf(scope);
                var db = repl.DbForScope(context, scope);
                if (db is null)
                {
                    return Results.Ok(new ReplChangesDto { NextCursor = after, Remaining = 0 });
                }
                using var conn = db.Open();
                var take = Math.Clamp(limit, 1, Org.ReplicationService.BatchSize);
                var read = AI2P.Storage.ChangeLog.Read(conn, after, take, peer.Id);
                var items = server ? repl.Visible(conn, read, organization.Id, context) : read;
                // КУРСОР СЧИТАЕТСЯ ПО ПРОЧИТАННОМУ, А НЕ ПО ОТДАННОМУ (T-160). Из серверной
                // БД партнёру видно не всё, и пачка бывает отсеяна фильтром целиком. Раньше
                // в этом случае курсор уезжал на ГОЛОВУ журнала — всё, что шло дальше, партнёр
                // не получал никогда (в том числе аккаунты участников). Теперь курсор идёт
                // ровно до последней прочитанной строки, и партнёр придёт за следующей пачкой
                var next = read.Count > 0
                    ? read[^1].Seq
                    : AI2P.Storage.ChangeLog.Head(conn);
                // партнёр дочитал нас до next — запоминаем и со своей стороны, чтобы после
                // смены ведущего не перечитывать журнал сначала. У архива курсор свой
                // (repl_arc_state): архивов у пары бывает два сразу — текущий и досылающий
                if (archiveId.Length > 0)
                {
                    registry.ReplState.AdvanceArchive(organization.Id, peer.Id, archiveId,
                        "push_cursor", next);
                }
                else
                {
                    registry.ReplState.Advance(organization.Id, peer.Id,
                        server ? "srv_push_cursor" : "push_cursor", next);
                }
                return Results.Ok(new ReplChangesDto
                {
                    Items = items,
                    NextCursor = next,
                    Remaining = AI2P.Storage.ChangeLog.CountAfter(conn, next, peer.Id),
                });
            }));

        // принять пачку изменений партнёра
        cluster.MapPost("/repl/apply", (HttpContext ctx, ReplApplyDto dto) => ApiEndpoints.Handle(() =>
        {
            var (organization, peer) = Pair(ctx, dto.OrgCode);
            var context = registry.Context(organization);
            var server = dto.Scope == ReplScopes.Server;
            var archiveId = ReplScopes.ArchiveOf(dto.Scope);
            // база АРХИВА (T-47-S0); архив закрыт .zip'ом — принимать некуда, и это не ошибка:
            // состояние выбрал человек, а данные приедут, когда он архив откроет
            var db = repl.DbForScope(context, dto.Scope);
            if (db is null)
            {
                return Results.Ok(new ReplApplyResultDto());
            }
            var result = repl.ApplyFrom(db, dto.Scope, organization, peer, dto.Items);
            var cursor = dto.SourceCursor > 0 ? dto.SourceCursor : result.Cursor;
            if (archiveId.Length > 0)
            {
                registry.ReplState.AdvanceArchive(organization.Id, peer.Id, archiveId,
                    "pull_cursor", cursor);
            }
            else
            {
                registry.ReplState.Advance(organization.Id, peer.Id,
                    server ? "srv_pull_cursor" : "pull_cursor", cursor);
            }
            registry.RefreshCodes();
            return Results.Ok(new ReplApplyResultDto
            {
                Applied = result.Applied,
                Skipped = result.Skipped,
                Conflicts = result.Conflicts,
                LastConflict = result.LastConflict,
                // отвергнутые базой строки партнёру называют, но пачку не отклоняют (T-160):
                // он их больше не пришлёт — они уже отложены здесь, в очереди повтора
                Failed = result.Failed.Count,
                LastFailed = result.Failed.Count > 0 ? result.Failed[^1].Text : "",
            });
        }));

        // --- репликация ФАЙЛОВ (ТЗ гл. 6, п. 7 плана; этап 44) ---
        // Партнёр называет пути сам, поэтому каждый из них проверяется на выход за пределы
        // реплицируемого каталога (FileManifest.Resolve, ТЗ гл. 12): иначе сервер кластера
        // мог бы прочитать или записать что угодно на этом компьютере.
        var files = repl.Files;

        cluster.MapGet("/repl/files/manifest", (HttpContext ctx, string org, string scope, string key) =>
            ApiEndpoints.Handle(() =>
            {
                var (organization, _) = Pair(ctx, org);
                var root = files.RootOf(organization, scope, key);
                if (root is null)
                {
                    // каталог не настроен (папка Common пуста) — репликация не запускается (todo44)
                    return Results.Ok(new ReplFileManifestDto { NotConfigured = true });
                }
                return Results.Ok(new ReplFileManifestDto { Items = files.Manifest(root) });
            }));

        cluster.MapGet("/repl/files/part", (HttpContext ctx, string org, string scope, string key,
            string path) => ApiEndpoints.Handle(() =>
        {
            var (organization, _) = Pair(ctx, org);
            return Results.Ok(new ReplFilePartDto { Offset = files.PartLength(organization, scope, key, path) });
        }));

        cluster.MapGet("/repl/files/read", (HttpContext ctx, string org, string scope, string key,
            string path, long offset, int length) => ApiEndpoints.Handle(() =>
        {
            var (organization, _) = Pair(ctx, org);
            var bytes = files.ReadChunk(organization, scope, key, path, offset, length);
            return Results.File(bytes, "application/octet-stream");
        }));

        cluster.MapPost("/repl/files/write", async (HttpContext ctx, string org, string scope, string key,
            string path, long offset, bool last, string mtime) =>
        {
            try
            {
                var (organization, _) = Pair(ctx, org);
                using var buffer = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(buffer);
                files.WriteChunk(organization, scope, key, path, offset, last, mtime, buffer.ToArray());
                return Results.Ok(new ReplFilePartDto { Offset = offset + buffer.Length });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cluster.MapPost("/repl/files/delete", (HttpContext ctx, string org, string scope, string key,
            string path) => ApiEndpoints.Handle(() =>
        {
            var (organization, _) = Pair(ctx, org);
            files.Delete(organization, scope, key, path);
            return Results.Ok(new ReplFilePartDto());
        }));

        cluster.MapPost("/repl/files/rename", (HttpContext ctx, string org, string scope, string key,
            string path, string to) => ApiEndpoints.Handle(() =>
        {
            var (organization, _) = Pair(ctx, org);
            files.Rename(organization, scope, key, path, to);
            return Results.Ok(new ReplFilePartDto());
        }));

        // --- ОТДАЧА АРХИВА СОСЕДУ (T-47-S0, выпуск 1.105) ---
        // Архив, удалённый у соседа, качается отсюда и приходит к нему ВСЕГДА ЗАКРЫТЫМ (.zip),
        // даже если здесь он открыт: состояние архива у каждого сервера своё, наследовать
        // чужое незачем. Открытый архив для передачи упаковывается во временный свёрток,
        // который снимается по её окончании (или по обрыву — просьбу «done» получатель шлёт
        // из finally). Передача кусками с докачкой: архив бывает гигабайтным

        cluster.MapGet("/repl/arc/pack", (HttpContext ctx, string org, string archive) =>
            ApiEndpoints.Handle(() =>
            {
                var (organization, _) = FilePair(ctx, org);
                var context = registry.Context(organization);
                var record = context.Archives.Get(archive);
                var zip = record is null ? null : context.Archives.PackForShare(record.Code);
                if (record is null || zip is null || !File.Exists(zip))
                {
                    return Results.Ok(new ReplArcPackDto { Missing = true });
                }
                return Results.Ok(new ReplArcPackDto
                {
                    Size = new FileInfo(zip).Length,
                    Code = record.Code,
                });
            }));

        cluster.MapGet("/repl/arc/read", (HttpContext ctx, string org, string archive, long offset,
            int length) =>
        {
            try
            {
                var (organization, _) = FilePair(ctx, org);
                var context = registry.Context(organization);
                var record = context.Archives.Get(archive);
                // свёрток уже собран запросом pack — здесь его только читают; сбор заново
                // на каждом куске означал бы перепаковку гигабайтов на каждые четыре мегабайта
                var zip = record is null ? null : ShareOrZip(context, record.Code);
                if (zip is null || !File.Exists(zip))
                {
                    return Results.File(Array.Empty<byte>(), "application/octet-stream");
                }
                using var stream = new FileStream(zip, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite);
                if (offset >= stream.Length)
                {
                    return Results.File(Array.Empty<byte>(), "application/octet-stream");
                }
                stream.Seek(offset, SeekOrigin.Begin);
                var size = (int)Math.Min(Math.Clamp(length, 1, Org.ArchiveReplicationService.ChunkSize),
                    stream.Length - offset);
                var buffer = new byte[size];
                var read = stream.Read(buffer, 0, size);
                return Results.File(read == size ? buffer : buffer[..read], "application/octet-stream");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or IOException)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cluster.MapPost("/repl/arc/done", (HttpContext ctx, string org, string archive) =>
            ApiEndpoints.Handle(() =>
            {
                var (organization, _) = FilePair(ctx, org);
                var context = registry.Context(organization);
                if (context.Archives.Get(archive) is { } record)
                {
                    context.Archives.DropShare(record.Code);
                }
                return Results.Ok(new ReplFilePartDto());
            }));

        // временный свёрток открытого архива либо его собственный .zip у закрытого
        static string? ShareOrZip(Org.OrgContext context, string code)
        {
            var share = context.Archives.ShareZipOf(code);
            return File.Exists(share) ? share
                : File.Exists(context.Archives.ZipOf(code)) ? context.Archives.ZipOf(code) : null;
        }

        // --- ФАЙЛ ПО ПРОСЬБЕ СОСЕДА (T-13-S0) ---
        // Картинку, положенную агентом в чат, сосед у себя не найдёт: папка проекта своя
        // на каждом сервере и не реплицируется. Поэтому он спрашивает файл здесь и отдаёт
        // его своему браузеру. Отвечаем ТОЛЬКО своим диском и сами ни у кого не спрашиваем —
        // иначе два сервера пересылали бы вопрос друг другу по кругу.
        //
        // Путь называет спрашивающий, поэтому он проверяется той же единственной проверкой,
        // что и обычная отдача файла (ТЗ гл. 12): за каталог данных и за папку проекта
        // выйти нельзя. Файла нет — 404, и сосед идёт к следующему серверу.
        cluster.MapGet("/files/get", (HttpContext ctx, string org, string kind, string? projectId,
            string path) => ApiEndpoints.Handle(() =>
        {
            var (organization, _) = FilePair(ctx, org);
            var context = registry.Context(organization);
            var abs = ApiEndpoints.LocalFilePath(context, kind, projectId ?? "", path);
            return abs is not null && File.Exists(abs)
                ? Results.File(abs, ApiEndpoints.ContentTypeOf(abs))
                : Results.NotFound();
        }));

        // токен для ОБРАТНОГО направления: подключившийся сервер выдаёт его дирижёру,
        // иначе дирижёр не смог бы сам начать сеанс репликации (ТЗ гл. 12)
        cluster.MapPost("/repl/peer-token", (HttpContext ctx, ClusterPeerTokenDto dto) =>
            ApiEndpoints.Handle(() =>
            {
                var peer = Caller(ctx, registry)
                           ?? throw new UnauthorizedAccessException(Loc.T("msg.clusterEndpoints.10"));
                servers.SetPeerToken(peer.Id, dto.Token);
                return Results.Ok(new ReplSessionDto { ServerId = servers.Local()?.Id ?? "" });
            }));
    }

    /// <summary>
    /// Организация, к которой просятся, когда код НЕ НАЗВАН (T-139): годится только та, где
    /// дирижёр — этот сервер, и только если она такая одна. Организаций несколько — возвращаем
    /// null и ПЕРЕЧЕНЬ (T-141): выбор делает человек на подключающемся сервере, и выбор
    /// множественный. Отдельного запроса «покажи свои организации» по-прежнему нет: перечень
    /// уезжает только в ответ на заявку, где уже названы и сервер, и заявитель.
    /// </summary>
    private static Organization? TheOnlyOrg(OrgRegistry registry, out List<ClusterOrgBriefDto> choices)
    {
        var mine = registry.Orgs.List(includeInactive: false)
            .Where(o => registry.Servers.IsLocalConductor(o.Id))
            .ToList();
        choices = mine.Select(o => new ClusterOrgBriefDto { Code = o.Code, Name = o.Name }).ToList();
        if (mine.Count == 0)
        {
            throw new ArgumentException(
                Loc.T("msg.clusterEndpoints.11"));
        }
        return mine.Count == 1 ? mine[0] : null;
    }

    /// <summary>
    /// ПАРОЛЬ ЧЕЛОВЕКА, КОТОРОГО МЫ УЖЕ ЗНАЕМ (T-150). Заявку подал человек с почтой нашего
    /// участника, но с другим внутренним ключом. Почта — логин, значит человек тот же самый,
    /// и после T-148 мы отдаём подающему серверу ключ этого человека. Отдавать его на одно
    /// лишь совпадение почты нельзя: почту знает кто угодно, а ключ — это личность участника
    /// организации со всеми её правами и историей. Поэтому подающий обязан прислать пароль
    /// этого человека НА ЭТОМ сервере: он его знает — это его собственный вход сюда.
    ///
    /// Пароль проверяется тем же способом, что и на странице входа, и никуда не сохраняется.
    /// Неудачная попытка пишется в серверный журнал как неудачный вход (п. 6.3): попытка
    /// подобрать чужую личность по сети должна быть видна владельцу.
    /// </summary>
    /// <returns>Пусто — пароль подошёл (или совпадения не было); иначе значение
    /// <c>ClusterJoinStatusDto.PasswordCheck</c>: ask / wrong / unset.</returns>
    public static string PasswordCheck(AI2P.Storage.Services.AccountService accounts,
        Account known, string password)
    {
        var stored = accounts.PasswordHashOf(known.Id);
        if (AI2P.Storage.PasswordHash.IsEmpty(stored))
        {
            // пароль у человека не задан — такой аккаунт и на странице входа пускают только
            // с этого же компьютера (гл. 11). Доказывать личность нечем: пустой пароль знают все
            return ClusterJoinStatusDto.PasswordUnset;
        }
        if (password.Length == 0)
        {
            return ClusterJoinStatusDto.PasswordAsk;
        }
        if (!AI2P.Storage.PasswordHash.Verify(stored, password))
        {
            accounts.LogAuth(EventTypes.AccountLoginFailed, known.Id, null,
                new { known.Email, reason = "cluster-join", cluster = true });
            return ClusterJoinStatusDto.PasswordWrong;
        }
        accounts.LogAuth(EventTypes.AccountLogin, known.Id, null,
            new { known.Email, cluster = true });
        return "";
    }

    /// <summary>Сервер, приславший запрос: узнаём его по токену, выданному при подключении.
    /// null — токена нет либо он не наш (ответ 401, ТЗ гл. 12).</summary>
    private static ServerNode? Caller(HttpContext ctx, OrgRegistry registry) =>
        registry.Servers.ByToken(ctx.Request.Headers[Ai2pHeaders.ServerToken].ToString());

    /// <summary>
    /// Состояние заявки для подавшего сервера. Токен доступа выдаётся ТОЛЬКО вместе
    /// с подтверждением: пока решение не принято, отдавать нечего.
    /// </summary>
    internal static ClusterJoinStatusDto Status(OrgRegistry registry, OrgServer link,
        AI2P.Connectors.OrgSecretKey? orgKeys = null)
    {
        var accepted = link.Status == OrgServerStatus.Active && link.DeletedAt is null;
        // «ОТКЛОНИТЬ» помечает связь удалённой — для подавшего это отказ, а не «нет заявки»
        var status = link.DeletedAt is not null ? OrgServerStatus.Rejected : link.Status;
        // при подтверждении отдаём всё, что нужно для заведения организации у себя
        // (первичная репликация, ТЗ гл. 11 п. 11.2): идентификаторы обязаны совпасть
        var conductor = accepted ? registry.Servers.Conductor(link.OrgId) : null;
        var org = accepted ? registry.Orgs.Get(link.OrgId) : null;
        return new ClusterJoinStatusDto
        {
            RequestId = link.Id,
            Status = status,
            Code = accepted ? link.Code : "",
            OrgCode = link.OrgCode,
            OrgName = link.OrgName,
            ServerId = registry.Servers.Local()?.Id ?? "",
            Token = accepted ? registry.Servers.IssueToken(link.ServerId) : "",
            OrgId = accepted ? link.OrgId : "",
            OrgDisplayId = org?.DisplayId ?? "",
            ConductorLinkId = conductor?.Id ?? "",
            ConductorCode = conductor?.Code ?? "",
            // ключ организации выдаётся вместе с токеном — то есть только по решению
            // человека (ТЗ гл. 10, этап 45); иначе ключи API остались бы нерасшифруемыми
            OrgKey = accepted && orgKeys is not null ? orgKeys.Export(link.OrgId) : "",
        };
    }

    /// <summary>Событие серверного журнала о заявке (ТЗ п. 6.4.3): пишется и при отказе —
    /// запись из списка исчезает, а след в журнале остаётся (todo41 п. 9).</summary>
    public static void LogCluster(this OrgRegistry registry, string eventType, string? actorId,
        string entityId, object payload)
    {
        using var conn = registry.ServerDb.Open();
        using var tx = conn.BeginTransaction();
        registry.ServerEvents.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = eventType,
            EntityType = "server",
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload),
        });
        tx.Commit();
    }
}
