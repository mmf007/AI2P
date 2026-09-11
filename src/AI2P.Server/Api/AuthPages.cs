using AI2P.Core;
using System.Text;
using System.Text.Encodings.Web;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using AI2P.Server.Org;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace AI2P.Server.Api;

/// <summary>
/// Страницы входа (ТЗ гл. 11 «Первый старт», гл. 12; этап 39).
///
/// Сделаны обычными серверными HTML-страницами, а не компонентами Blazor: вход должен
/// работать ДО появления circuit'а (cookie ставится в ответ на обычный POST формы, из
/// интерактивного компонента это невозможно), и страница обязана открываться, даже если
/// интерактивная часть UI не поднялась.
/// </summary>
public static class AuthPages
{
    /// <summary>
    /// ВИЗАРД ПЕРВОГО СТАРТА (T-141), см. ниже: сервер → пользователь → организация либо
    /// подключение к кластеру. Прежде это были два разных экрана без дороги назад.
    /// </summary>
    private const string SetupPath = "/setup";

    /// <summary>Прежний адрес страницы подключения к кластеру (T-135): теперь это шаг
    /// визарда, но ссылки на него остались в документации и закладках — уводим.</summary>
    private const string JoinPath = "/join-cluster";

    // Шаги визарда. Их порядок и есть весь его сценарий (T-141; язык добавлен в T-148).
    private const string StepLang = "lang";
    private const string StepServer = "server";
    private const string StepUser = "user";
    private const string StepOrg = "org";
    private const string StepJoin = "join";

    // ПРОДОЛЖЕНИЕ ВИЗАРДА У НОВОГО ДИРИЖЁРА КЛАСТЕРА (T-291): организация создана — теперь
    // спрашиваем, чем человек будет заниматься, и собираем ему рабочее место (см. FirstSetup)
    private const string StepUsage = "usage";
    private const string StepProject = "project";

    public static void MapAi2pAuthPages(this WebApplication app)
    {
        var services = app.Services;
        var registry = services.GetRequiredService<OrgRegistry>();
        var accounts = registry.Accounts;
        var secrets = services.GetRequiredService<SecretStore>();
        var i18n = services.GetRequiredService<I18nService>();
        var antiforgery = services.GetRequiredService<IAntiforgery>();
        var configHolder = services.GetRequiredService<ConfigHolder>();
        var basePath = configHolder.Config.Ui.NormalizedBasePath();

        // --- вход пользователя; первый старт — отдельный визард (T-141) ---

        app.MapGet("/login", (HttpContext ctx) =>
            accounts.IsEmpty()
                ? Results.Redirect(basePath + SetupPath)
                : Results.Content(LoginPage(ctx, i18n, antiforgery, basePath, "", ReturnUrl(ctx)),
                    "text/html; charset=utf-8"));

        app.MapPost("/login", async (HttpContext ctx) =>
        {
            // форма читается ДО проверки antiforgery: при ошибке страница показывается
            // заново С ВВЕДЁННЫМ логином — набирать его повторно незачем
            var form = await ctx.Request.ReadFormAsync();
            if (!await ValidAsync(ctx, antiforgery))
            {
                return Results.Content(LoginPage(ctx, i18n, antiforgery,
                    basePath, i18n["login.error.expired"], ReturnUrl(ctx), form),
                    "text/html; charset=utf-8");
            }
            var returnUrl = Safe(form["returnUrl"].ToString(), basePath);
            if (accounts.IsEmpty())
            {
                return Results.Redirect(basePath + SetupPath);
            }

            var login = form["login"].ToString();
            var (account, error) = accounts.Authenticate(login, form["password"].ToString(),
                Ai2pAuth.IsLocalRequest(ctx));
            if (account is null)
            {
                accounts.LogAuth(EventTypes.AccountLoginFailed, null, null, new { login, reason = error });
                return Results.Content(LoginPage(ctx, i18n, antiforgery,
                    basePath, error, returnUrl, form), "text/html; charset=utf-8");
            }
            await SignInAsync(ctx, account);
            accounts.LogAuth(EventTypes.AccountLogin, account.Id,
                registry.AnyMemberOf(account.Id)?.Id, new { account.Email });
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        // --- выход (обе схемы: и пользователь, и admin сервера) ---

        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            var accountId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (accountId is not null)
            {
                accounts.LogAuth(EventTypes.AccountLogout, accountId,
                    registry.AnyMemberOf(accountId)?.Id, new { });
            }
            await ctx.SignOutAsync(Ai2pAuth.Scheme);
            await ctx.SignOutAsync(Ai2pAuth.AdminScheme);
            return Results.Redirect(basePath + "/login");
        }).AllowAnonymous();

        // --- ВИЗАРД ПЕРВОГО СТАРТА (T-141) ---
        //
        // Экранов первого старта было два: форма «создать владельца и организацию» на входе
        // и отдельная страница подключения к кластеру. Дороги назад не было ни там, ни там,
        // а она нужна: дирижёр отвечает на заявку «такая почта (или имя) у меня уже есть»,
        // и поправить их должен ПОДКЛЮЧАЮЩИЙСЯ — то есть тот, кто стоит перед этим экраном.
        //
        // Поэтому теперь это один визард с кнопками «Далее» и «Назад»:
        //   1. Язык        — им говорят все следующие экраны (T-148); предлагается язык
        //      браузера, а если такого словаря у нас нет — английский;
        //   2. Сервер      — имя, протокол, порт и флажок «первичный сервер кластера»;
        //   3. Пользователь — он же администратор этого сервера (шагом назад правится);
        //   4. Организация  — у первичного сервера; иначе Подключение: адрес дирижёра
        //      и организации (выбор множественный). Подав заявку, человек уходит В ИНТЕРФЕЙС
        //      и ждёт решения там (T-148): экран визарда его больше не держит.
        //
        // Визард живёт РОВНО до появления первой организации: дальше всё это делается из UI.
        // Сервер, поставленный НЕ первичным, организаций у себя не имеет, а весь UI живёт
        // внутри организации — вкладка «Серверы» ему недоступна, и подключиться оттуда
        // он не может (ТЗ гл. 11, п. 11.2).

        app.MapGet(SetupPath, (HttpContext ctx, CurrentUserAccessor current) =>
        {
            if (SetupDenied(registry, accounts, current, basePath) is { } denied)
            {
                return denied;
            }
            var start = StartSetup(accounts, configHolder, i18n, ctx);
            // ПЕРВЫЙ ЭКРАН ГОВОРИТ НА ТОМ ЖЕ ЯЗЫКЕ, КОТОРЫЙ В НЁМ ПРЕДЛОЖЕН (T-214-S0).
            // Язык предлагается по браузеру (T-148), а страница до 1.129 рисовалась языком
            // УСТАНОВКИ (config.json, в дистрибутиве «ru»): человек с английским браузером
            // видел русский визард, в котором выбран «English». Дальше это делает каждая
            // отправка формы (ApplyLang в POST) — теперь так же и первый показ.
            // Без save: выбор языка человек подтверждает кнопкой «Далее» (шаг StepLang),
            // и только тогда он уходит в config.json
            ApplyLang(i18n, configHolder, start.Lang, save: false);
            return Results.Content(SetupPage(ctx, i18n, antiforgery, basePath, start),
                "text/html; charset=utf-8");
        });

        app.MapPost(SetupPath, async (HttpContext ctx, CurrentUserAccessor current) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var view = ReadSetup(form);
            // ПОСЛЕДНИЕ ШАГИ ИДУТ УЖЕ ПРИ СОЗДАННОЙ ОРГАНИЗАЦИИ (T-291), поэтому обычное
            // правило «визард живёт, пока организаций нет» их бы не пустило. Разрешаются
            // ровно эти два шага и только вошедшему; что организация та самая и человек
            // в ней состоит — проверяет сам шаг (SetupOrg)
            if (!(AfterOrgStep(view.Step) && current.AccountId is not null)
                && SetupDenied(registry, accounts, current, basePath) is { } denied)
            {
                return denied;
            }
            // язык выбран на первом шаге и едет скрытым полем — применяем его к КАЖДОЙ
            // отправке, чтобы на выбранном языке была и следующая страница, и ошибка (T-148)
            ApplyLang(i18n, configHolder, view.Lang, save: false);
            if (!await ValidAsync(ctx, antiforgery))
            {
                view.Error = i18n["login.error.expired"];
                return Results.Content(SetupPage(ctx, i18n, antiforgery, basePath, view),
                    "text/html; charset=utf-8");
            }
            try
            {
                if (await SetupStepAsync(registry, accounts, secrets, configHolder, i18n, ctx,
                        form, view, basePath) is { } finished)
                {
                    return finished;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or HttpRequestException or TaskCanceledException)
            {
                view.Error = ex.Message;
            }
            // шаг сделан (или не удался) — страница показывает следующий шаг либо ошибку
            return Results.Content(SetupPage(ctx, i18n, antiforgery, basePath, view),
                "text/html; charset=utf-8");
        }).DisableAntiforgery();

        // прежний адрес страницы подключения (T-135) остался в документации и закладках
        app.MapGet(JoinPath, () => Results.Redirect(basePath + SetupPath));

        // --- вошёл, но не участник ни одной организации (ТЗ п. 2.15) ---
        // Без объяснения это выглядело бы пустым экраном без причины: данные в системе
        // есть только внутри организаций, а доступ в организацию даёт исполнитель с
        // аккаунтом вошедшего — завести его может владелец организации.
        // Сюда же приходит UI, если организация в адресе оказалась ему недоступна (истекла
        // cookie, аккаунт отключён, чужая организация в ссылке). Страница разбирает случай:
        // не вошёл — на вход, есть своя организация — в неё, иначе объясняет ситуацию.
        app.MapGet("/no-org", (HttpContext ctx, CurrentUserAccessor current) =>
        {
            if (current.AccountId is not { } accountId)
            {
                return Results.Redirect(basePath + "/login");
            }
            if (current.Default(accountId) is { } mine)
            {
                return Results.Redirect(basePath + "/" + mine.Code + "/");
            }
            // организаций на сервере нет вовсе — это НЕ первичный сервер кластера (T-135):
            // ему нужна не «попросите доступ у владельца», а дорога к подключению
            var join = registry.Orgs.List(includeInactive: false).Count == 0
                ? $"<p class=\"note\"><a href=\"{H(basePath)}{SetupPath}\">{H(i18n["join.link"])}</a></p>"
                : "";
            var body = $"<h1>{H(i18n["app.title"])}</h1>"
                       + $"<p class=\"sub\">{H(i18n["login.noOrg"])}</p>"
                       + join
                       + $"<p class=\"note\"><a href=\"{H(basePath)}/logout\">{H(i18n["account.logout"])}</a></p>";
            return Results.Content(Shell(i18n["app.title"], body), "text/html; charset=utf-8");
        }).AllowAnonymous();

        // --- вход admin'а сервера (локальный, не реплицируется; ТЗ гл. 12) ---

        app.MapGet("/server-admin", (HttpContext ctx) =>
            Results.Content(AdminPage(ctx, i18n, antiforgery, secrets, basePath, ""),
                "text/html; charset=utf-8"));

        app.MapPost("/server-admin", async (HttpContext ctx) =>
        {
            if (!await ValidAsync(ctx, antiforgery))
            {
                return Results.Content(AdminPage(ctx, i18n, antiforgery, secrets, basePath,
                    i18n["login.error.expired"]), "text/html; charset=utf-8");
            }
            // настройки сервера меняет только физический хозяин компьютера (ТЗ гл. 12)
            if (!Ai2pAuth.IsLocalRequest(ctx))
            {
                return Results.Content(AdminPage(ctx, i18n, antiforgery, secrets, basePath,
                    i18n["login.error.adminRemote"]), "text/html; charset=utf-8");
            }
            var form = await ctx.Request.ReadFormAsync();
            var password = form["password"].ToString();
            // пароль ещё не задан — первый вход его задаёт (переспрос двумя полями)
            if (!Ai2pAuth.AdminPasswordSet(secrets))
            {
                if (password.Trim().Length == 0 || password != form["password2"].ToString())
                {
                    return Results.Content(AdminPage(ctx, i18n, antiforgery, secrets, basePath,
                        i18n["login.error.mismatch"]), "text/html; charset=utf-8");
                }
                Ai2pAuth.SetAdminPassword(secrets, password);
            }
            else if (!Ai2pAuth.VerifyAdminPassword(secrets, password))
            {
                return Results.Content(AdminPage(ctx, i18n, antiforgery, secrets, basePath,
                    i18n["login.error.badPassword"]), "text/html; charset=utf-8");
            }
            await SignInAdminAsync(ctx, secrets);
            return Results.Redirect(basePath + "/");
        }).DisableAntiforgery();
    }

    // --- ВИЗАРД ПЕРВОГО СТАРТА (T-141): состояние, шаги, переходы ---

    /// <summary>Состояние визарда между отправками формы. Всё, что человек уже ввёл, ездит
    /// скрытыми полями: шаг назад не должен стирать набранное.</summary>
    private sealed class SetupView
    {
        public string Step { get; set; } = StepLang;

        // шаг 0 — язык интерфейса (T-148): им говорят все следующие экраны и всё приложение
        public string Lang { get; set; } = "en";

        // шаг 1 — сервер
        public string ServerName { get; set; } = "";
        public string ServerProtocol { get; set; } = "http";
        public string ServerPort { get; set; } = "5480";
        /// <summary>Первичный сервер кластера (T-135): создаёт свою организацию. Снят —
        /// подключается к уже работающему, организация приедет репликацией.</summary>
        public bool Primary { get; set; } = true;

        // КАТАЛОГИ ЭТОГО КОМПЬЮТЕРА (T-291) — на том же шаге, что и адрес: это тоже про
        // железо. Спрашиваются здесь, а не остаются умолчанием config.json, потому что
        // именно здесь человек ещё думает про свой компьютер, а не про задачи
        public string ModelsRepo { get; set; } = "";
        public string DistDir { get; set; } = "";
        public string PackagesDir { get; set; } = "";

        // шаг 2 — пользователь (он же администратор этого сервера)
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        /// <summary>Аккаунт уже заведён (вернулись шагом назад): пароль не переспрашивается,
        /// а имя и почту можно поправить — например, дирижёр сказал, что они заняты.</summary>
        public bool AccountExists { get; set; }

        /// <summary>ПАРОЛЬ ТОТ ЖЕ, ЧТО У АДМИНИСТРАТОРА СЕРВЕРА (T-291): по умолчанию да —
        /// на одиночной установке это один человек, и двух паролей ему не нужно.</summary>
        public bool AdminSame { get; set; } = true;

        // шаг 3а — организация
        public string OrgName { get; set; } = "";
        public string OrgCode { get; set; } = "";

        /// <summary>Организация СОЗДАНА (T-291): дальше идут шаги «типовое использование»
        /// и «первый проект», и они работают уже внутри неё.</summary>
        public string OrgId { get; set; } = "";

        // шаги 5 и 6 — рабочее место у нового дирижёра кластера (T-291)

        /// <summary>Типовое использование: <see cref="FirstSetup.Usages"/>.</summary>
        public string Usage { get; set; } = FirstSetup.UsageCode;

        public string ProjectName { get; set; } = "";
        public string ProjectDir { get; set; } = "";

        // шаг 3б — подключение к кластеру
        /// <summary>Адрес сервера-дирижёра, как его набрал человек.</summary>
        public string Address { get; set; } = "";
        /// <summary>Заведённая запись сервера-дирижёра.</summary>
        public string ServerId { get; set; } = "";
        /// <summary>Коды организаций на дирижёре через запятую; пусто — у него она одна.</summary>
        public string JoinCodes { get; set; } = "";
        /// <summary>Перечень организаций дирижёра: пришёл в ответ на заявку без кода (T-141).
        /// Выбор МНОЖЕСТВЕННЫЙ — сервер может работать сразу в нескольких.</summary>
        public List<ClusterOrgBriefDto> Choices { get; set; } = [];
        public string Note { get; set; } = "";
        /// <summary>Состояние заявки: pending / deferred / active / rejected; пусто — не подана.</summary>
        public string Status { get; set; } = "";
        public string OrgTitle { get; set; } = "";
        /// <summary>Заявка принята и организация заведена — подключение завершено.</summary>
        public bool Done { get; set; }
        /// <summary>Организация не подключена, а СОЗДАНА здесь (передумали).</summary>
        public bool Created { get; set; }
        /// <summary>Предупреждение шага (T-291): не ошибка — работа продолжается, но человек
        /// должен знать. Сейчас так сообщается, что готовых к работе моделей под выбранное
        /// использование нет и ИИ-исполнителей заводить не из чего.</summary>
        public string Warning { get; set; } = "";

        /// <summary>Мы взяли себе внутренний ключ этого человека с дирижёра (T-148): он там
        /// уже есть с той же почтой. Об этом надо сказать — пароль дальше будет ЕГО.</summary>
        public bool Adopted { get; set; }

        /// <summary>ДИРИЖЁР СПРАШИВАЕТ ПАРОЛЬ (T-150): человек с этой почтой у него уже есть,
        /// и ключ он отдаст только тому, кто знает пароль этого человека. Значение —
        /// <c>ClusterJoinStatusDto.PasswordAsk / PasswordWrong / PasswordUnset</c>; пусто —
        /// не спрашивает. Сам пароль в состоянии визарда НЕ живёт: он приходит с формой,
        /// уходит на дирижёр и забывается — обратно в разметку его писать нельзя.</summary>
        public string AskPassword { get; set; } = "";

        /// <summary>Имя, под которым дирижёр знает человека с этой почтой (T-150): его надо
        /// показать — человек должен понимать, чей пароль у него спрашивают.</summary>
        public string KnownName { get; set; } = "";

        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Кому доступен визард: пока на сервере НЕТ НИ ОДНОЙ организации. Появилась — первый
    /// старт закончился, и всё то же самое делается из UI. Аккаунт уже заведён, а человек
    /// не вошёл — сначала вход: правит себя и подаёт заявку только он сам.
    /// null — доступ разрешён.
    /// </summary>
    private static IResult? SetupDenied(OrgRegistry registry, AccountService accounts,
        CurrentUserAccessor current, string basePath)
    {
        if (registry.Orgs.List(includeInactive: false).Count > 0)
        {
            return Results.Redirect(basePath + "/");
        }
        return !accounts.IsEmpty() && current.AccountId is null
            ? Results.Redirect(basePath + "/login?ReturnUrl="
                               + Uri.EscapeDataString(basePath + SetupPath))
            : null;
    }

    /// <summary>Шаг идёт ПОСЛЕ создания организации (T-291): «типовое использование»
    /// и «первый проект». Такие шаги работают внутри уже созданной организации.</summary>
    private static bool AfterOrgStep(string step) => step is StepUsage or StepProject;

    /// <summary>Начальное состояние визарда. Аккаунт уже есть (сервер поставили не первичным
    /// и вернулись сюда позже) — начинаем сразу с подключения.</summary>
    private static SetupView StartSetup(AccountService accounts, ConfigHolder holder,
        I18nService i18n, HttpContext ctx)
    {
        var user = Environment.UserName.Trim();
        var existing = accounts.List().FirstOrDefault();
        return new SetupView
        {
            Step = existing is null ? StepLang : StepJoin,
            // ЯЗЫК ПРЕДЛАГАЕТСЯ ПО БРАУЗЕРУ (T-148), а нет такого словаря — английский.
            // Аккаунт уже есть — шаг языка пройден: берём выбранный, а не браузерный
            Lang = existing is null ? BrowserLang(i18n, ctx) : i18n.Lang,
            // ИМЯ СЕРВЕРА предлагается ГОТОВЫМ (T-211): раньше поле было пустым (T-139),
            // и первый старт упирался в вопрос, на который у одиночной установки ответ один.
            // Подставляется имя из config.json, а его умолчание — «localhost»: совпадение имён
            // в кластере разрешено (T-141), так что это законный ответ и для сервера за NAT,
            // а тому, у кого сервер зовётся в сети иначе, поле остаётся поправить
            ServerName = DefaultServerName(holder.Config),
            ServerProtocol = holder.Config.Ui.Protocol,
            ServerPort = holder.Config.Ui.Port.ToString(),
            Primary = true,
            // каталоги железа (T-291): предлагаются те, что стоят в config.json, — умолчание
            // относительное («./models»), то есть рядом с установкой
            ModelsRepo = holder.Config.Storage.ModelsRepo,
            DistDir = holder.Config.Storage.DistDir,
            PackagesDir = holder.Config.Storage.PackagesDir,
            AdminSame = holder.Config.ServerAdmin.SameAsUser,
            Name = existing?.Name ?? user,
            Email = existing?.Email
                    ?? (user.Length > 0 ? user.ToLowerInvariant().Replace(' ', '.') : "owner") + "@localhost",
            Phone = existing?.Phone ?? "",
            AccountExists = existing is not null,
            OrgName = user.Length > 0 ? user : "AI2P",
            OrgCode = Organization.NormalizeCode(user.Length > 0 ? user : "AI2P"),
        };
    }

    /// <summary>
    /// ИМЯ СЕРВЕРА, ПРЕДЛОЖЕННОЕ НА ПЕРВОМ СТАРТЕ (T-211).
    ///
    /// До 1.82 поле было пустым и обязательным (T-139): считалось, что угадывать за человека
    /// сетевое имя его сервера нельзя. На практике первый старт одиночной установки — а это
    /// подавляющее большинство — упирался в вопрос с единственным разумным ответом. Теперь
    /// предлагается имя из <c>config.json</c> (<c>ui.hostname</c>), а его умолчание —
    /// <c>localhost</c>. Ответ законный: одинаковые имена в кластере разрешены (T-141),
    /// за NAT так называет себя половина серверов. Обязательность поля не снята — стереть
    /// предложенное и оставить пусто по-прежнему нельзя.
    /// </summary>
    public static string DefaultServerName(Ai2pConfig config) =>
        config.Ui.Hostname.Trim() is { Length: > 0 } host ? host : "localhost";

    /// <summary>
    /// ЯЗЫК, НА КОТОРОМ ГОВОРИТ БРАУЗЕР (T-148) — предложение первого экрана визарда.
    ///
    /// Берётся из <c>Accept-Language</c> по убыванию веса <c>q</c>: первый тег, словарь
    /// которого у нас есть (сначала целиком — «pt-BR», потом по основной части — «ru» из
    /// «ru-RU»). Ничего не подошло — английский; нет и его — первый словарь, какой есть.
    /// Языки — это файлы <c>i18n/*.json</c>, их набор задаётся установкой, а не кодом.
    /// </summary>
    public static string BrowserLang(I18nService i18n, HttpContext ctx)
    {
        var known = i18n.Languages.ToList();
        var fallback = known.FirstOrDefault(l => l.Equals("en", StringComparison.OrdinalIgnoreCase))
                       ?? known.FirstOrDefault() ?? "en";
        var header = ctx.Request.Headers.AcceptLanguage.ToString();
        var tags = header.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var pieces = part.Split(';');
                var weight = 1.0;
                foreach (var piece in pieces.Skip(1))
                {
                    var value = piece.Trim();
                    if (value.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(value[2..], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        weight = parsed;
                    }
                }
                return (Tag: pieces[0].Trim(), Weight: weight);
            })
            .Where(t => t.Tag.Length > 0 && t.Tag != "*")
            .OrderByDescending(t => t.Weight)
            .ToList();
        foreach (var (tag, _) in tags)
        {
            if (known.FirstOrDefault(l => l.Equals(tag, StringComparison.OrdinalIgnoreCase)) is { } exact)
            {
                return exact;
            }
            var primary = tag.Split('-')[0];
            if (known.FirstOrDefault(l => l.Equals(primary, StringComparison.OrdinalIgnoreCase)) is { } near)
            {
                return near;
            }
        }
        return fallback;
    }

    /// <summary>Прочитать состояние визарда из отправленной формы (скрытые поля + видимые).</summary>
    private static SetupView ReadSetup(IFormCollection form) => new()
    {
        Step = form["step"].ToString() is { Length: > 0 } step ? step : StepLang,
        Lang = form["lang"].ToString(),
        ServerName = form["serverName"].ToString(),
        ServerProtocol = form["serverProtocol"].ToString(),
        ServerPort = form["serverPort"].ToString(),
        // снятый checkbox в форму не отправляется вовсе — «не пришёл» значит «снят»,
        // поэтому рядом с ним едет скрытое поле-признак «флажок вообще был на странице»
        Primary = form["primary"].ToString().Length > 0,
        ModelsRepo = form["modelsRepo"].ToString(),
        DistDir = form["distDir"].ToString(),
        PackagesDir = form["packagesDir"].ToString(),
        Name = form["name"].ToString(),
        Email = form["login"].ToString(),
        Phone = form["phone"].ToString(),
        AccountExists = form["accountExists"].ToString() == "1",
        // тот же приём, что у «первичного сервера»: снятый флажок в форму не приходит
        AdminSame = form["adminSame"].ToString().Length > 0,
        OrgName = form["org"].ToString(),
        OrgCode = form["orgCode"].ToString(),
        OrgId = form["orgId"].ToString(),
        Usage = FirstSetup.NormalizeUsage(form["usage"].ToString()),
        ProjectName = form["projectName"].ToString(),
        ProjectDir = form["projectDir"].ToString(),
        Address = form["address"].ToString(),
        ServerId = form["serverId"].ToString(),
        // отмеченных организаций может быть несколько — все под одним именем поля (T-141)
        JoinCodes = string.Join(",", form["joinOrg"].ToArray()),
        Note = form["note"].ToString(),
        Status = form["status"].ToString(),
    };

    /// <summary>
    /// Один шаг визарда. Возвращает не-null, когда первый старт закончен и надо уходить
    /// со страницы (в созданную организацию); null — показать следующий шаг.
    /// </summary>
    private static async Task<IResult?> SetupStepAsync(OrgRegistry registry, AccountService accounts,
        SecretStore secrets, ConfigHolder configHolder, I18nService i18n, HttpContext ctx,
        IFormCollection form, SetupView view, string basePath)
    {
        var go = form["go"].ToString();
        // «ДАЛЕЕ» С ПОСЛЕДНЕГО ШАГА (T-153): заявка подана, решение принимают на дирижёре
        // когда угодно — держать человека на экране визарда нечем. Раньше уйти отсюда можно
        // было только текстовой ссылкой внизу страницы, и экран «Шаг 4/4» выглядел тупиком:
        // кнопка «Далее» есть на каждом шаге, а на последнем её не было.
        if (go == "enter")
        {
            return Results.Redirect(basePath + "/");
        }
        if (go == "back")
        {
            view.Step = view.Step switch
            {
                StepServer => StepLang,
                StepUser => StepServer,
                // ПОСЛЕ СОЗДАНИЯ ОРГАНИЗАЦИИ ДОРОГИ НАЗАД НЕТ (T-291): вернувшись на шаг
                // организации, человек создал бы вторую с тем же названием. Поэтому с шага
                // «первый проект» назад ведёт только к выбору типового использования,
                // а на самом выборе кнопки «Назад» нет вовсе
                StepProject => StepUsage,
                _ => StepUser,
            };
            Prefill(accounts, view);
            return null;
        }
        switch (view.Step)
        {
            case StepLang:
                // ЯЗЫК ИНТЕРФЕЙСА (T-148): выбранный записывается в config.json — на нём
                // говорят и остальные шаги визарда, и всё приложение дальше. Поменять его
                // потом можно в настройках
                ApplyLang(i18n, configHolder, view.Lang, save: true);
                view.Step = StepServer;
                return null;

            case StepServer:
                // АДРЕС ЭТОГО СЕРВЕРА (T-139): имя, протокол и порт спрашиваются здесь,
                // а не остаются умолчанием config.json
                if (!TryReadServer(view.ServerName, view.ServerProtocol, view.ServerPort,
                        out var hostname, out var protocol, out var port, out var errorKey))
                {
                    view.Error = i18n[errorKey];
                    return null;
                }
                // HTTPS НА ПЕРВОМ ЖЕ ЭКРАНЕ (T-206). Сертификата в визарде не спрашивают —
                // его кладут на диск или в хранилище системы заранее, а настройки правят
                // в форме локального сервера. Но выбрать «https», не имея сертификата,
                // здесь нельзя: следующий запуск не поднялся бы вовсе, а визард — это
                // самый первый экран, который человек видит в жизни
                if (protocol == "https"
                    && HttpsCertificate.Problem(configHolder.Config.Ui.Https, hostname,
                        configHolder.Path,
                        secrets.Read(configHolder.Config.Ui.Https.PasswordRefOrDefault()) ?? "")
                        is { } certProblem)
                {
                    view.Error = Loc.T("msg.https.11", certProblem);
                    return null;
                }
                // КАТАЛОГИ ЖЕЛЕЗА (T-291) — там же: репозиторий моделей, дистрибутивы
                // и пакеты. Проверяются тем же правилом, что и в форме локального сервера,
                // чтобы отказ был один и тот же в обоих местах
                try
                {
                    ApiEndpoints.EnsureDirSetting(view.ModelsRepo, Loc.T("msg.apiEndpoints.18"));
                    ApiEndpoints.EnsureDirSetting(view.DistDir, Loc.T("msg.apiEndpoints.19"));
                    ApiEndpoints.EnsureDirSetting(view.PackagesDir, Loc.T("msg.apiEndpoints.20"));
                }
                catch (ArgumentException ex)
                {
                    view.Error = ex.Message;
                    return null;
                }
                ApplyServerAddress(configHolder, registry, hostname, protocol, port);
                ApplyStorageDirs(configHolder, view);
                view.ServerName = hostname;
                view.ServerProtocol = protocol;
                view.ServerPort = port.ToString();
                view.Step = StepUser;
                Prefill(accounts, view);
                return null;

            case StepUser:
            {
                var account = accounts.List().FirstOrDefault();
                if (account is null)
                {
                    var password = form["password"].ToString();
                    if (password != form["password2"].ToString())
                    {
                        view.Error = i18n["login.error.mismatch"];
                        return null;
                    }
                    account = accounts.Create(new AccountSaveInput
                    {
                        Name = view.Name,
                        Email = view.Email,
                        Phone = view.Phone,
                        Password = password,
                        IsActive = true,
                    }, actorId: null);
                    // ОДИН ПАРОЛЬ НА ДВА ВХОДА (T-291). Паролей в системе два: пароль
                    // пользователя и пароль администратора СЕРВЕРА — отдельный локальный
                    // вход, которым открываются настройки компьютера (ТЗ гл. 12). На
                    // одиночной установке это один человек, и разбираться, почему форма
                    // своего сервера доступна «только для чтения», ему негде — с этого и
                    // начиналась жалоба T-291. Флажок отмечен по умолчанию: пароль тот же,
                    // вход администратором выдаётся сразу.
                    //
                    // Флажок снят — прежнее поведение (T-148): на ПОДКЛЮЧАЕМОМ сервере
                    // пароль администратора всё равно задаётся тем же — иначе человек
                    // потеряет доступ к серверам своей машины и не запустит репликацию
                    // руками, а в чужой организации у него обычная роль
                    if ((view.AdminSame || !view.Primary) && password.Trim().Length > 0)
                    {
                        Ai2pAuth.SetAdminPassword(secrets, password);
                        await SignInAdminAsync(ctx, secrets);
                    }
                    // связь паролей живёт дальше первого старта: пока она отмечена, смена
                    // пароля ЭТОГО пользователя меняет и пароль администратора сервера
                    // (иначе они молча разъедутся) — см. Ai2pConfig.ServerAdminSettings
                    ApplyAdminSame(configHolder, view.AdminSame, view.Email);
                    await SignInAsync(ctx, account);
                    accounts.LogAuth(EventTypes.AccountLogin, account.Id, null,
                        new { account.Email, first = true, primaryServer = view.Primary });
                }
                else
                {
                    // ВЕРНУЛИСЬ ШАГОМ НАЗАД (T-141): правим имя и почту. Только здесь это
                    // и возможно — организаций на сервере ещё нет, аккаунт один и никуда
                    // не уезжал. Ради этого шага назад визард и сделан: дубль почты или
                    // имени на дирижёре исправляет тот, кто подключается
                    account = accounts.Rename(account.Id, view.Name, view.Email, view.Phone,
                        account.Id);
                    await SignInAsync(ctx, account);   // в cookie лежат имя и почта
                }
                view.AccountExists = true;
                view.Step = view.Primary ? StepOrg : StepJoin;
                return null;
            }

            case StepOrg:
            {
                var account = CurrentAccount(registry, ctx);
                var (org, context) = registry.CreateOrg(view.OrgName, view.OrgCode, account,
                    actorId: null);
                accounts.LogAuth(EventTypes.AccountLogin, account.Id,
                    context.Executors.ByAccount(account.Id)?.Id,
                    new { account.Email, first = true, org = org.Code });
                // ВИЗАРД НА ЭТОМ БОЛЬШЕ НЕ КОНЧАЕТСЯ (T-291): у нового дирижёра кластера
                // спрашиваем типовое использование и собираем рабочее место. Организация
                // уже создана, поэтому дальше шаги работают внутри неё — и дороги назад
                // отсюда нет
                view.OrgId = org.Id;
                view.OrgCode = org.Code;
                view.OrgName = org.Name;
                view.Step = StepUsage;
                view.ProjectName = org.Name;
                return null;
            }

            case StepUsage:
            {
                // «нет типового использования» — заканчиваем как раньше, сразу в организацию
                view.Usage = FirstSetup.NormalizeUsage(view.Usage);
                if (view.Usage == FirstSetup.UsageNone)
                {
                    return Results.Redirect(basePath + "/" + view.OrgCode + "/");
                }
                // ГОТОВЫХ К РАБОТЕ МОДЕЛЕЙ МОЖЕТ НЕ ОКАЗАТЬСЯ ВОВСЕ: облачной нужен ключ
                // API, локальной — установка. Это не ошибка (команду и проект всё равно
                // заведём), но сказать об этом надо ЗДЕСЬ, а не оставлять человека гадать,
                // почему исполнителей нет
                var (usageContext, _) = SetupOrg(registry, ctx, view);
                if (FirstSetup.PickModels(usageContext.Models, view.Usage, FirstSetup.Names.Length)
                    .Count == 0)
                {
                    view.Warning = i18n["setup.usage.noModels"];
                }
                view.Step = StepProject;
                return null;
            }

            case StepProject:
            {
                if (view.ProjectName.Trim().Length == 0)
                {
                    view.Error = i18n["login.error.projectName"];
                    return null;
                }
                var (context, owner) = SetupOrg(registry, ctx, view);
                FirstSetup.Apply(FirstSetup.Services.Of(context), view.OrgName, owner, view.Usage,
                    view.ProjectName, view.ProjectDir);
                // рабочее место собрано — дальше человек работает в интерфейсе организации
                // (отдельного адреса у карточки проекта нет, deep link есть только у задачи)
                return Results.Redirect(basePath + "/" + view.OrgCode + "/");
            }

            default:
                return await JoinStepAsync(registry, secrets, i18n, ctx, form, view, basePath);
        }
    }

    /// <summary>
    /// Применить выбранный язык (T-148): к словарю интерфейса и, по <paramref name="save"/>,
    /// к config.json. Неизвестный язык игнорируется — словари задаются файлами <c>i18n</c>,
    /// а форма приходит от браузера и может назвать что угодно.
    /// </summary>
    private static void ApplyLang(I18nService i18n, ConfigHolder holder, string lang, bool save)
    {
        var known = i18n.Languages.FirstOrDefault(l =>
            l.Equals(lang.Trim(), StringComparison.OrdinalIgnoreCase));
        if (known is null)
        {
            return;
        }
        i18n.Lang = known;
        if (save && holder.Config.Language != known)
        {
            holder.Config.Language = known;
            holder.Config.Save(holder.Path);
        }
    }

    /// <summary>
    /// Записать каталоги этого компьютера в config.json (T-291). Значения сохраняются
    /// РОВНО ТАКИМИ, какими их набрал человек: относительный путь так и остаётся
    /// относительным — он и переносим между компьютерами, и понятен в файле («./models»
    /// рядом с установкой). Раскрытием занимается чтение (Ai2pConfig.ModelsRepoPath).
    /// </summary>
    private static void ApplyStorageDirs(ConfigHolder holder, SetupView view)
    {
        var storage = holder.Config.Storage;
        storage.ModelsRepo = view.ModelsRepo.Trim().Length > 0
            ? view.ModelsRepo.Trim()
            : Ai2pConfig.StorageSettings.DefaultModelsRepo;
        storage.DistDir = view.DistDir.Trim();
        storage.PackagesDir = view.PackagesDir.Trim();
        holder.Config.Save(holder.Path);
    }

    /// <summary>
    /// Запомнить решение «пароль администратора сервера тот же» (T-291). Почта нужна
    /// потому, что аккаунтов на сервере бывает много, а связан с администратором сервера
    /// ровно один — тот, кто эту связь завёл на первом старте. Снятый флажок стирает и
    /// почту: связи нет, пароли независимы.
    /// </summary>
    private static void ApplyAdminSame(ConfigHolder holder, bool same, string email)
    {
        holder.Config.ServerAdmin.SameAsUser = same;
        holder.Config.ServerAdmin.UserEmail = same ? email.Trim() : "";
        holder.Config.Save(holder.Path);
    }

    /// <summary>
    /// Организация, созданная этим же визардом, и исполнитель вошедшего в ней (T-291):
    /// шаги «типовое использование» и «первый проект» работают уже внутри неё.
    /// Проверяется и то, и другое: идентификатор организации приезжает СКРЫТЫМ ПОЛЕМ формы,
    /// то есть из браузера, и подставить туда чужую организацию не должно быть можно.
    /// </summary>
    private static (OrgContext Context, Executor Owner) SetupOrg(OrgRegistry registry,
        HttpContext ctx, SetupView view)
    {
        var account = CurrentAccount(registry, ctx);
        var org = registry.Orgs.Get(view.OrgId.Trim())
                  ?? throw new ArgumentException(Loc.T("msg.authPages.3"));
        var context = registry.Context(org);
        // ВЛАДЕЛЕЦ, и только он: шаги после организации заводят исполнителей, команду и
        // проект — то есть делают ровно то, что в интерфейсе закрыто ролями (ApiPermissions).
        // Организацию визард только что создал сам, и её владелец — тот, кто её создал;
        // всякий другой участник сюда попасть не должен, даже прислав чужой orgId руками
        var owner = context.Executors.ByAccount(account.Id) is { SystemRole: SystemRole.Owner } found
                    ? found
                    : throw new ArgumentException(Loc.T("msg.authPages.3"));
        view.OrgCode = org.Code;
        view.OrgName = org.Name;
        return (context, owner);
    }

    /// <summary>Подставить в шаг «Пользователь» то, что уже лежит в аккаунте: возвращаясь
    /// назад, человек должен видеть себя, а не то, что предлагалось изначально.</summary>
    private static void Prefill(AccountService accounts, SetupView view)
    {
        if (accounts.List().FirstOrDefault() is not { } account)
        {
            return;
        }
        view.AccountExists = true;
        view.Name = account.Name;
        view.Email = account.Email;
        view.Phone = account.Phone;
    }

    private static Account CurrentAccount(OrgRegistry registry, HttpContext ctx) =>
        registry.Accounts.Get(ctx.User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "")
        ?? throw new ArgumentException(Loc.T("msg.authPages.1"));

    // --- адрес этого сервера на экране первого старта (T-139) ---

    /// <summary>
    /// Разобрать имя сервера, протокол и порт с экрана первого старта (T-139).
    ///
    /// ИМЯ СЕРВЕРА — это имя хоста, по которому к нему обращаются ДРУГИЕ серверы кластера
    /// и по которому строятся ссылки на задачи (<c>ui.hostname</c>, ТЗ гл. 10). Поле
    /// намеренно предлагается ПУСТЫМ: прежнее умолчание «localhost» одинаково у всех
    /// установок, и дирижёр не мог завести запись второго сервера — адрес был занят им самим.
    ///
    /// Разбор прощающий: набранное «http://мойпк:5480/ai2p» тоже понимается — протокол
    /// и порт из адреса выигрывают у соседних полей (их человек указал явно и последними),
    /// путь отбрасывается (префикс задаётся в config.json). Ошибка возвращается КЛЮЧОМ
    /// словаря: страница входа переводит его сама.
    /// </summary>
    public static bool TryReadServer(string name, string protocol, string port,
        out string hostname, out string scheme, out int number, out string errorKey)
    {
        scheme = protocol.Trim().Equals("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        number = 0;
        errorKey = "";
        var value = name.Trim();
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator >= 0)
        {
            scheme = value[..separator].Trim().Equals("https", StringComparison.OrdinalIgnoreCase)
                ? "https"
                : "http";
            value = value[(separator + 3)..];
        }
        value = value.Split('/', 2)[0].Trim();
        var entered = port.Trim();
        // «хост:порт» — порт из имени; литерал IPv6 в скобках («[::1]:5480») не в счёт
        var colon = value.IndexOf(':', value.LastIndexOf(']') + 1);
        if (colon >= 0)
        {
            entered = value[(colon + 1)..].Trim();
            value = value[..colon].Trim();
        }
        hostname = value;
        if (hostname.Length == 0)
        {
            errorKey = "login.error.serverName";
            return false;
        }
        if (hostname.Contains(' '))
        {
            errorKey = "login.error.serverHost";
            return false;
        }
        if (!int.TryParse(entered, out number) || number is < 1 or > 65535)
        {
            number = 0;
            errorKey = "login.error.serverPort";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Записать адрес этого сервера в config.json и в его запись в списке серверов (T-139).
    /// Порт и протокол применяются к работающему процессу только после перезапуска — он уже
    /// слушает прежний порт (то же правило, что у формы локального сервера, ТЗ гл. 10).
    /// </summary>
    private static void ApplyServerAddress(ConfigHolder holder, OrgRegistry registry,
        string hostname, string protocol, int port)
    {
        var config = holder.Config;
        config.Ui.Hostname = hostname;
        config.Ui.Protocol = protocol;
        config.Ui.Port = port;
        config.Save(holder.Path);
        registry.EnsureLocalServer(hostname, protocol, hostname, port,
            config.Ui.NormalizedBasePath(), config.Ui.Hostname2, config.Ui.Port2);
    }

    /// <summary>
    /// Шаг подключения к кластеру по кнопке формы: <c>request</c> — заявка, <c>poll</c> —
    /// решение по ней, <c>create</c> — «передумали, организация заводится здесь». Сам обмен —
    /// общий с UI (<see cref="ClusterJoinFlow"/>). Почты и пароля НА ДИРИЖЁРЕ здесь не
    /// спрашивают: заявка подаётся анонимно и подписана аккаунтом этого сервера (T-139).
    /// </summary>
    private static async Task<IResult?> JoinStepAsync(OrgRegistry registry, SecretStore secrets,
        I18nService i18n, HttpContext ctx, IFormCollection form, SetupView view, string basePath)
    {
        var action = form["action"].ToString();
        if (action == "create")
        {
            // передумали: организация всё-таки заводится здесь (флажок первичного сервера
            // снят по ошибке либо этот сервер решили сделать самостоятельным). Без этой
            // двери сервер оказался бы в тупике: организаций нет, а создать их из UI
            // нельзя — UI живёт внутри организации
            var (created, _) = registry.CreateOrg(form["newOrgName"].ToString(),
                form["newOrgCode"].ToString(), CurrentAccount(registry, ctx), actorId: null);
            view.Done = true;
            view.Created = true;
            view.JoinCodes = created.Code;
            view.OrgTitle = created.Name;
            return null;
        }
        if (action == "request")
        {
            // адрес дирижёра набирают здесь же: запись о нём заводится (или находится)
            // по адресу — отдельного шага «войти на него» больше нет (T-139)
            if (!ClusterJoinFlow.TryParseAddress(view.Address, out var protocol, out var host,
                    out var port, out var prefix, out var error))
            {
                view.Error = error;
                return null;
            }
            var target = ClusterJoinFlow.EnsureByAddress(registry, protocol, host, port, prefix,
                actorId: null);
            view.ServerId = target.Id;
            // заявку подписывает вошедший на ЭТОМ сервере человек: на дирижёре он никто,
            // но решение там принимает человек, и он должен видеть, кто просится (T-139)
            var account = CurrentAccount(registry, ctx);
            var codes = AI2P.Storage.Services.ServerService.Split(view.JoinCodes);
            // ПАРОЛЬ НА ДИРИЖЁРЕ (T-150): в форме он появляется, только когда дирижёр его
            // попросил, и дальше этой отправки не живёт — ни в скрытых полях, ни в состоянии
            var conductorPassword = form["conductorPassword"].ToString();
            var requested = await ClusterJoinFlow.RequestManyAsync(registry, target, codes,
                new JoinApplicant(account.Id, account.Name, account.Email), actorId: null,
                view.Note.Trim(), conductorPassword);
            // ТОТ ЖЕ ЧЕЛОВЕК, ДРУГОЙ ВНУТРЕННИЙ КЛЮЧ (T-148) — обычное дело при ПОВТОРНОМ
            // подключении: сервер переставили, аккаунт завёлся заново, а на дирижёре человек
            // с этой почтой остался с прежним ключом. Берём ключ дирижёра себе и подаём
            // заявку заново — здесь это безопасно: организаций на сервере ещё нет, за
            // аккаунтом не стоит ни исполнителей, ни отреплицированных строк.
            //
            // НО ТОЛЬКО ПОСЛЕ ПАРОЛЯ (T-150): ключ дирижёр отдаёт лишь тому, кто доказал,
            // что он и есть тот человек. Не доказал — показываем поле пароля и объясняем.
            //
            // Организации на этом сервере уже есть — ключ менять нельзя вовсе, и пароль
            // спрашивать не за чем: сначала тупик, потом всё остальное
            if (requested.Status == ClusterJoinStatusDto.SameEmail
                && registry.Orgs.List(includeInactive: true).Count > 0)
            {
                view.Error = i18n["join.sameEmail.stuck"];
                return null;
            }
            if (requested.Status == ClusterJoinStatusDto.SameEmail
                && requested.PasswordCheck.Length > 0)
            {
                view.AskPassword = requested.PasswordCheck;
                view.KnownName = requested.ApplicantName;
                view.Error = requested.PasswordCheck switch
                {
                    ClusterJoinStatusDto.PasswordWrong => i18n["join.sameEmail.wrong"],
                    ClusterJoinStatusDto.PasswordUnset => i18n["join.sameEmail.unset"],
                    _ => "",
                };
                return null;
            }
            if (requested.Status == ClusterJoinStatusDto.SameEmail
                && requested.ApplicantId.Length > 0)
            {
                account = registry.Accounts.Rekey(account.Id, requested.ApplicantId, actorId: null);
                // ПАРОЛЬ ЗДЕСЬ СТАНОВИТСЯ ТЕМ ЖЕ, ЧТО НА ДИРИЖЁРЕ (T-150). Он только что
                // проверен там, и мы его знаем — значит, разъезжаться паролям незачем.
                // Иначе человек до первой репликации входил бы сюда одним паролем, а после
                // неё (строку аккаунта пришлёт дирижёр) — другим, и это выглядело бы поломкой
                registry.Accounts.SetPassword(account.Id, conductorPassword, actorId: null);
                await SignInAsync(ctx, account);   // в cookie лежал прежний ключ
                requested = await ClusterJoinFlow.RequestManyAsync(registry, target, codes,
                    new JoinApplicant(account.Id, account.Name, account.Email), actorId: null,
                    view.Note.Trim());
                view.Adopted = true;
            }
            // дирижёр представился своим настоящим внутренним ключом — запись сервера
            // переехала на него (AdoptId), и дальше решение спрашиваем уже по новому ключу
            if (requested.ServerId.Length > 0)
            {
                view.ServerId = requested.ServerId;
            }
            // ОРГАНИЗАЦИЙ У ДИРИЖЁРА НЕСКОЛЬКО (T-141): заявки нет, есть перечень — человек
            // отмечает нужные (можно несколько) и подаёт заявку повторно
            if (requested.Status == ClusterJoinStatusDto.ChooseOrg)
            {
                view.Choices = requested.Orgs;
                view.Error = i18n["join.orgs.choose"];
                return null;
            }
            if (requested.Status == ClusterJoinStatusDto.SameEmail)
            {
                // сюда попасть уже нечем: тупик и просьба пароля разобраны выше, а с ключом
                // заявка подаётся заново. Оставлено, чтобы «заявка не подана» не выглядела
                // как поданная, если протокол однажды научится отвечать этим статусом иначе
                view.Error = i18n["join.sameEmail.stuck"];
                return null;
            }
            view.Status = requested.Status;
            view.OrgTitle = requested.OrgName;
            if (requested.OrgCode.Length > 0 && view.JoinCodes.Trim().Length == 0)
            {
                view.JoinCodes = requested.OrgCode;
            }
            // ЗАЯВКА ПОДАНА — ДАЛЬШЕ ЖДУТ В ИНТЕРФЕЙСЕ (T-148). Держать человека на экране
            // визарда незачем: решение принимают на дирижёре и когда угодно, а здесь оно
            // спрашивается кнопкой в списке серверов. Интерфейс до подключения урезан
            // (см. UiState.NoOrg): доступны настройки и список серверов, всё остальное
            // погашено — данных пока нет, они приедут репликацией.
            //
            // Кроме случая, когда мы взяли себе ключ человека с дирижёра: об этом надо
            // сказать словами (пароль дальше будет тот, что задан у него), поэтому страница
            // показывается, а в интерфейс человек уходит сам — ссылкой внизу
            var live = OrgServerStatus.IsRequest(view.Status)
                       || view.Status == OrgServerStatus.Active;
            return view.Adopted || !live ? null : Results.Redirect(basePath + "/");
        }
        var server = registry.Servers.Get(view.ServerId)
                     ?? throw new ArgumentException(Loc.T("msg.authPages.2"));
        var status = await ClusterJoinFlow.PollAsync(registry, secrets, server, actorId: null);
        view.Status = status.Status;
        view.OrgTitle = status.OrgName;
        if (status.OrgCode.Length > 0)
        {
            view.JoinCodes = status.OrgCode;
        }
        view.Done = status.Status == OrgServerStatus.Active && status.Token.Length > 0;
        return null;
    }

    /// <summary>
    /// Cookie входа несёт только АККАУНТ: исполнитель и роль принадлежат организации,
    /// а их у аккаунта может быть несколько (ТЗ п. 2.15, этап 40).
    ///
    /// Личность назначается ещё и ТЕКУЩЕМУ запросу (T-141). Обычный вход сразу отвечает
    /// редиректом, и это неважно, а визард рисует следующий шаг тут же — с новой формой.
    /// Antiforgery-токен привязан к вошедшему: выданный «анониму» на следующем шаге уже
    /// не приняли бы, и человек получал бы «форма устарела» (поймано живой проверкой).
    /// </summary>
    private static async Task SignInAsync(HttpContext ctx, Account account)
    {
        var principal = Ai2pAuth.Principal(account, Ai2pAuth.Scheme);
        await ctx.SignInAsync(Ai2pAuth.Scheme, principal,
            new AuthenticationProperties { IsPersistent = true });
        ctx.User = principal;
    }

    /// <summary>Вход администратора сервера — вторая cookie (ТЗ гл. 12). Ставится и со своей
    /// страницы, и визардом первого старта подключаемого сервера (T-148): пароль там задаёт
    /// тот же человек, и список серверов своей машины должен остаться ему доступен.</summary>
    private static async Task SignInAdminAsync(HttpContext ctx, SecretStore secrets)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,
                Ai2pAuth.AdminLogin(secrets))],
            Ai2pAuth.AdminScheme);
        await ctx.SignInAsync(Ai2pAuth.AdminScheme,
            new System.Security.Claims.ClaimsPrincipal(identity));
    }

    private static async Task<bool> ValidAsync(HttpContext ctx, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(ctx);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    /// <summary>Куда вернуться после входа; чужие адреса не принимаем (открытый редирект).</summary>
    private static string ReturnUrl(HttpContext ctx) =>
        Safe(ctx.Request.Query["ReturnUrl"].ToString(), ctx.Request.PathBase);

    private static string Safe(string url, string fallback)
    {
        var home = fallback.Length > 0 ? fallback + "/" : "/";
        if (url.Length == 0 || !url.StartsWith('/') || url.StartsWith("//"))
        {
            return home;
        }
        return url.Contains("/login", StringComparison.OrdinalIgnoreCase) ? home : url;
    }

    // --- разметка страниц ---

    /// <summary>
    /// Страница входа. Первого старта здесь больше НЕТ — он стал визардом (T-141,
    /// <see cref="SetupPage"/>). <paramref name="entered"/> — значения предыдущей отправки:
    /// логин при неверном пароле набирать заново незачем.
    /// </summary>
    private static string LoginPage(HttpContext ctx, I18nService i18n, IAntiforgery antiforgery,
        string basePath, string error, string returnUrl, IFormCollection? entered = null)
    {
        var login = entered is not null && entered.TryGetValue("login", out var value)
            ? value.ToString()
            : "";
        var body = new StringBuilder();
        body.Append($"<h1>{H(i18n["app.title"])}</h1>");
        body.Append($"<p class=\"sub\">{H(i18n["login.hint"])}</p>");
        if (error.Length > 0)
        {
            body.Append($"<div class=\"err\">{H(error)}</div>");
        }
        body.Append($"<form method=\"post\" action=\"{H(basePath)}/login\">");
        body.Append(Token(ctx, antiforgery));
        body.Append($"<input type=\"hidden\" name=\"returnUrl\" value=\"{H(returnUrl)}\" />");
        // это единственная НАСТОЯЩАЯ форма входа: подсказки браузеру обычные,
        // и менеджер паролей работает как ожидается
        body.Append(Field("login", i18n["login.email"], login, "text",
            autofocus: true, autocomplete: "username"));
        body.Append(Field("password", i18n["login.password"], "", "password",
            autocomplete: "current-password"));
        body.Append($"<button type=\"submit\">{H(i18n["login.submit"])}</button>");
        body.Append("</form>");
        body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/server-admin\">{H(i18n["login.adminLink"])}</a></p>");
        return Shell(i18n["app.title"], body.ToString(), i18n.Lang);
    }

    // --- разметка визарда первого старта (T-141) ---

    /// <summary>
    /// ВИЗАРД ПЕРВОГО СТАРТА: один шаг на экран, кнопки «Назад» и «Далее».
    ///
    /// Своей вёрстки не заводит: та же карточка, что у входа (MudBlazor тут недоступен —
    /// страница не интерактивная и обязана открываться до появления circuit'а). Всё, что
    /// человек уже ввёл, ездит скрытыми полями: шаг назад ничего не теряет.
    /// </summary>
    private static string SetupPage(HttpContext ctx, I18nService i18n, IAntiforgery antiforgery,
        string basePath, SetupView view)
    {
        var body = new StringBuilder();
        var title = view.Step switch
        {
            StepLang => i18n["setup.step.lang"],
            StepServer => i18n["setup.step.server"],
            StepUser => i18n["setup.step.user"],
            StepOrg => i18n["setup.step.org"],
            StepUsage => i18n["setup.step.usage"],
            StepProject => i18n["setup.step.project"],
            _ => i18n["setup.step.join"],
        };
        var number = view.Step switch
        {
            StepLang => 1,
            StepServer => 2,
            StepUser => 3,
            StepUsage => 5,
            StepProject => 6,
            _ => 4,
        };
        // ШАГОВ У ПЕРВИЧНОГО СЕРВЕРА ШЕСТЬ (T-291): после организации идут «типовое
        // использование» и «первый проект». У подключаемого их по-прежнему четыре —
        // рабочее место ему приедет репликацией, заводить своё незачем
        var total = view.Primary ? 6 : 4;
        body.Append($"<h1>{H(i18n["setup.title"])}</h1>");
        body.Append($"<p class=\"sub\"><b>{H(i18n["setup.step"])} {number}/{total} · {H(title)}</b>"
                    + $"<br />{H(StepHint(i18n, view))}</p>");
        if (view.Error.Length > 0)
        {
            body.Append($"<div class=\"err\">{H(view.Error)}</div>");
        }
        // предупреждение шага (T-291): работа продолжается, но человеку надо сказать
        if (view.Warning.Length > 0)
        {
            body.Append($"<div class=\"ok\" data-setup-warning=\"1\">{H(view.Warning)}</div>");
        }
        // ПОДКЛЮЧЕНИЕ — самый сложный шаг: заявка, перечень организаций, ожидание решения
        if (view.Step == StepJoin)
        {
            return JoinStepPage(ctx, i18n, antiforgery, basePath, view, body);
        }
        body.Append($"<form method=\"post\" action=\"{H(basePath)}{SetupPath}\">");
        body.Append(Token(ctx, antiforgery));
        body.Append(SetupState(view));
        switch (view.Step)
        {
            case StepLang:
                // ЯЗЫК (T-148): выбор из установленных словарей i18n; предложен язык браузера.
                // Название языка — НА НЁМ САМОМ (T-70-S0): это первый экран системы, подписи
                // на чужом языке человеку здесь ничем не помогают
                body.Append(Select("lang", i18n["setup.lang"], view.Lang,
                    i18n.LanguagesByName.ToArray(), i18n.Name));
                body.Append($"<p class=\"note\">{H(i18n["setup.lang.hint"])}</p>");
                break;

            case StepServer:
                // ИМЯ СЕРВЕРА (T-139): пустое и обязательное — его знает только человек
                body.Append(Field("serverName", i18n["login.serverName"], view.ServerName, "text",
                    autofocus: true));
                body.Append(Select("serverProtocol", i18n["servers.protocol"], view.ServerProtocol,
                    ["http", "https"]));
                body.Append(Field("serverPort", i18n["settings.port"], view.ServerPort, "number"));
                body.Append($"<p class=\"note\">{H(i18n["login.server.hint"])}</p>");
                // ПЕРВИЧНЫЙ СЕРВЕР КЛАСТЕРА (T-135): снят — организация здесь не создаётся,
                // сервер подключится к уже работающему и получит её репликацией
                body.Append(Check("primary", i18n["login.primary"], view.Primary));
                body.Append($"<p class=\"note\">{H(i18n["login.primary.hint"])}</p>");
                // КАТАЛОГИ ЭТОГО КОМПЬЮТЕРА (T-291): веса моделей, архивы пакетов и сами
                // пакеты. Умолчания относительные — каталоги рядом с установкой
                body.Append(Field("modelsRepo", i18n["settings.modelsRepo"], view.ModelsRepo, "text"));
                body.Append(Field("distDir", i18n["settings.distDir"], view.DistDir, "text"));
                body.Append(Field("packagesDir", i18n["settings.packagesDir"], view.PackagesDir, "text"));
                body.Append($"<p class=\"note\">{H(i18n["setup.dirs.hint"])}</p>");
                break;

            case StepUser:
                body.Append(Field("name", i18n["login.name"], view.Name, "text", autofocus: true));
                // логин помечен явно: иначе браузер сочтёт «логином» соседнее с паролем поле
                body.Append(Field("login", i18n["login.email"], view.Email, "text",
                    autocomplete: "username"));
                body.Append(Field("phone", i18n["login.phone"], view.Phone, "text"));
                if (view.AccountExists)
                {
                    // вернулись назад: пароль уже задан и переспрашивать его незачем —
                    // здесь правят имя и почту, из-за которых дирижёр отклонил заявку
                    body.Append($"<p class=\"note\">{H(i18n["setup.user.exists"])}</p>");
                }
                else
                {
                    body.Append(Field("password", i18n["login.password"], "", "password",
                        autocomplete: "new-password"));
                    body.Append(Field("password2", i18n["login.password2"], "", "password",
                        autocomplete: "new-password"));
                    body.Append($"<p class=\"note\">{H(i18n["login.emptyPasswordNote"])}</p>");
                    // ОДИН ПАРОЛЬ НА ДВА ВХОДА (T-291): отмечен — пароль администратора
                    // сервера тот же, и человек сразу входит обоими
                    body.Append(Check("adminSame", i18n["setup.adminSame"], view.AdminSame));
                    body.Append($"<p class=\"note\">{H(i18n["setup.adminSame.hint"])}</p>");
                }
                break;

            case StepUsage:
            {
                // ТИПОВОЕ ИСПОЛЬЗОВАНИЕ (T-291): один вопрос вместо четырёх форм, которые
                // человеку иначе пришлось бы найти самому
                body.Append($"<div class=\"row\"><label>{H(i18n["setup.usage"])}</label></div>");
                foreach (var usage in FirstSetup.Usages)
                {
                    body.Append(Radio("usage", i18n["setup.usage." + usage], usage,
                        usage == view.Usage));
                }
                body.Append($"<p class=\"note\">{H(i18n["setup.usage.hint"])}</p>");
                break;
            }

            case StepProject:
                body.Append(Field("projectName", i18n["setup.project.name"], view.ProjectName,
                    "text", autofocus: true));
                body.Append(Field("projectDir", i18n["setup.project.dir"], view.ProjectDir, "text"));
                body.Append($"<p class=\"note\">{H(i18n["setup.project.dir.hint"])}</p>");
                break;

            default:
                body.Append(Field("org", i18n["login.org"], view.OrgName, "text", autofocus: true));
                body.Append(Field("orgCode", i18n["login.orgCode"], view.OrgCode, "text"));
                body.Append($"<p class=\"note\">{H(i18n["login.orgNote"])}</p>");
                break;
        }
        // «Назад» нет на первом шаге и на выборе типового использования: организация к тому
        // времени создана, и возвращаться к её созданию нельзя (T-291)
        body.Append(Buttons(i18n, back: view.Step is not (StepLang or StepUsage),
            nextLabel: view.Step == StepProject ? i18n["setup.finish"] : i18n["setup.next"]));
        body.Append("</form>");
        body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/server-admin\">{H(i18n["login.adminLink"])}</a></p>");
        return Shell(i18n["setup.title"], body.ToString(), i18n.Lang);
    }

    /// <summary>Подсказка шага: что именно сейчас произойдёт.</summary>
    private static string StepHint(I18nService i18n, SetupView view) => view.Step switch
    {
        StepLang => i18n["setup.hint.lang"],
        StepServer => i18n["setup.hint.server"],
        StepUser => i18n["setup.hint.user"],
        StepOrg => i18n["setup.hint.org"],
        StepUsage => i18n["setup.hint.usage"],
        StepProject => i18n["setup.hint.project"],
        // ДИРИЖЁР СПРАШИВАЕТ ПАРОЛЬ (T-150): обычная подсказка шага говорит «почта и пароль
        // на том сервере НЕ нужны» — здесь это ровно наоборот, и подсказка своя
        _ => view.AskPassword.Length > 0 ? i18n["join.hint.sameEmail"] : i18n["join.hint"],
    };

    /// <summary>Скрытые поля визарда: всё введённое на прежних шагах едет с каждой отправкой.</summary>
    private static string SetupState(SetupView view)
    {
        var state = new StringBuilder();
        state.Append(Hidden("step", view.Step));
        state.Append(Hidden("accountExists", view.AccountExists ? "1" : ""));
        if (view.Step != StepLang)
        {
            state.Append(Hidden("lang", view.Lang));
        }
        state.Append(Hidden("status", view.Status));
        state.Append(Hidden("serverId", view.ServerId));
        // поля, которых на текущем экране нет, — скрытыми: иначе шаг назад потерял бы их
        if (view.Step != StepServer)
        {
            state.Append(Hidden("serverName", view.ServerName));
            state.Append(Hidden("serverProtocol", view.ServerProtocol));
            state.Append(Hidden("serverPort", view.ServerPort));
            state.Append(Hidden("primary", view.Primary ? "1" : ""));
            // каталоги железа (T-291) — с того же шага
            state.Append(Hidden("modelsRepo", view.ModelsRepo));
            state.Append(Hidden("distDir", view.DistDir));
            state.Append(Hidden("packagesDir", view.PackagesDir));
        }
        if (view.Step != StepUser)
        {
            state.Append(Hidden("name", view.Name));
            state.Append(Hidden("login", view.Email));
            state.Append(Hidden("phone", view.Phone));
            state.Append(Hidden("adminSame", view.AdminSame ? "1" : ""));
        }
        if (view.Step != StepOrg)
        {
            state.Append(Hidden("org", view.OrgName));
            state.Append(Hidden("orgCode", view.OrgCode));
        }
        if (view.Step != StepJoin)
        {
            state.Append(Hidden("address", view.Address));
            state.Append(Hidden("note", view.Note));
        }
        // организация первого старта и ответы последних шагов (T-291)
        state.Append(Hidden("orgId", view.OrgId));
        if (view.Step != StepUsage)
        {
            state.Append(Hidden("usage", view.Usage));
        }
        if (view.Step != StepProject)
        {
            state.Append(Hidden("projectName", view.ProjectName));
            state.Append(Hidden("projectDir", view.ProjectDir));
        }
        return state.ToString();
    }

    /// <summary>Кнопки шага: «Назад» слева, действие справа (обе — обычные submit'ы формы).</summary>
    private static string Buttons(I18nService i18n, bool back, string nextLabel) =>
        "<div class=\"nav\">"
        + (back
            ? "<button type=\"submit\" name=\"go\" value=\"back\" class=\"back\">"
              + $"{H(i18n["setup.back"])}</button>"
            : "")
        + $"<button type=\"submit\" name=\"go\" value=\"next\">{H(nextLabel)}</button>"
        + "</div>";

    private static string Hidden(string name, string value) =>
        $"<input type=\"hidden\" name=\"{H(name)}\" value=\"{H(value)}\" />";

    /// <summary>
    /// Шаг «Подключение к кластеру» (T-135, доработан в T-139 и T-141): заявка → решение.
    /// Здесь же перечень организаций дирижёра с МНОЖЕСТВЕННЫМ выбором и дверь «передумали —
    /// создать организацию здесь». Кнопка «Назад» ведёт на шаг «Пользователь»: именно там
    /// правят почту и имя, если дирижёр ответил, что они у него заняты.
    /// </summary>
    private static string JoinStepPage(HttpContext ctx, I18nService i18n, IAntiforgery antiforgery,
        string basePath, SetupView view, StringBuilder body)
    {
        // подпись заявителя — вошедший на ЭТОМ сервере человек (T-139): заявка анонимна для
        // дирижёра, но безымянной быть не должна — её принимает человек
        var name = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        var email = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
        var applicant = name.Length > 0 && name != email ? $"{name} <{email}>" : email;

        // подключение состоялось: организация заведена и наполняется репликацией
        if (view.Done)
        {
            body.Append($"<div class=\"ok\">"
                        + $"{H(i18n[view.Created ? "join.created" : "join.accepted"])}</div>");
            if (!view.Created)
            {
                body.Append($"<p class=\"note\">{H(i18n["join.accepted.hint"])}</p>");
            }
            // «ДАЛЕЕ» ВЕДЁТ В КОРЕНЬ, А НЕ В ОРГАНИЗАЦИЮ (T-153). Заявку только что
            // подтвердили, но УЧАСТИЕ приезжает репликацией, и ссылка прямо в организацию
            // до её первого сеанса упиралась бы в «вы не состоите ни в одной организации».
            // Корень разбирает оба случая сам: участник — в организацию, ещё нет —
            // в урезанный интерфейс ожидания со списком серверов (T-148)
            body.Append($"<form method=\"post\" action=\"{H(basePath)}{SetupPath}\">");
            body.Append(Token(ctx, antiforgery));
            body.Append(SetupState(view));
            body.Append($"<button type=\"submit\" name=\"go\" value=\"enter\">{H(i18n["setup.next"])}</button>");
            body.Append("</form>");
            body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/logout\">{H(i18n["account.logout"])}</a></p>");
            return Shell(i18n["setup.title"], body.ToString(), i18n.Lang);
        }

        body.Append($"<form method=\"post\" action=\"{H(basePath)}{SetupPath}\">");
        body.Append(Token(ctx, antiforgery));
        body.Append(SetupState(view));

        // ЗАЯВКА УЖЕ ПОДАНА — остаётся узнать решение человека на дирижёре
        if (view.Status.Length > 0 && view.Status != OrgServerStatus.Rejected)
        {
            // ВЗЯЛИ КЛЮЧ ЧЕЛОВЕКА С ДИРИЖЁРА (T-148): пароль дальше будет тот, что задан
            // у него, — об этом надо сказать сразу, а не оставлять человека гадать
            if (view.Adopted)
            {
                body.Append($"<div class=\"ok\">{H(i18n["join.sameEmail"])}</div>");
            }
            body.Append(Hidden("address", view.Address));
            body.Append(Hidden("joinOrg", view.JoinCodes));
            body.Append($"<div class=\"row\"><label>{H(i18n["join.state"])}</label>"
                        + $"<div class=\"ro\">{H(StatusText(i18n, view))}</div></div>");
            // ДВЕ КНОПКИ, И ОБЕ НАСТОЯЩИЕ (T-153): «Узнать решение» остаётся здесь, а «Далее»
            // уводит в интерфейс — ждать решения можно и в нём (T-148): он до подключения
            // урезан, но список серверов и настройки в нём есть, этого и хватает
            body.Append("<div class=\"nav\">"
                        + "<button type=\"submit\" name=\"action\" value=\"poll\" class=\"back\">"
                        + $"{H(i18n["join.check"])}</button>"
                        + "<button type=\"submit\" name=\"go\" value=\"enter\">"
                        + $"{H(i18n["setup.next"])}</button></div>");
            body.Append("</form>");
            body.Append($"<p class=\"note\">{H(i18n["join.pending.hint"])}</p>");
            body.Append($"<p class=\"note\">{H(i18n["join.toUi.hint"])}</p>");
            return Shell(i18n["setup.title"], body.ToString(), i18n.Lang);
        }
        if (view.Status == OrgServerStatus.Rejected)
        {
            body.Append($"<div class=\"err\">{H(i18n["join.rejected"])}</div>");
        }

        // ЗАЯВКА ОДНИМ ШАГОМ (T-139): адрес дирижёра, организации и подпись заявителя.
        // Почты и пароля дирижёра не спрашивают — кроме одного случая (T-150, ниже)
        body.Append(Field("address", i18n["join.address"], view.Address, "text",
            autofocus: view.AskPassword.Length == 0));
        if (view.Choices.Count > 0)
        {
            // ПЕРЕЧЕНЬ ОРГАНИЗАЦИЙ ДИРИЖЁРА (T-141): выбор множественный — сервер вправе
            // работать сразу в нескольких, заявка уйдёт в каждую отмеченную
            var chosen = AI2P.Storage.Services.ServerService.Split(view.JoinCodes);
            body.Append($"<div class=\"row\"><label>{H(i18n["join.orgs"])}</label></div>");
            foreach (var org in view.Choices)
            {
                body.Append(CheckValue("joinOrg", $"{org.Name} ({org.Code})", org.Code,
                    chosen.Contains(org.Code)));
            }
            body.Append($"<p class=\"note\">{H(i18n["join.orgs.hint"])}</p>");
        }
        else
        {
            body.Append(Field("joinOrg", i18n["join.orgCode"], view.JoinCodes, "text"));
            body.Append($"<p class=\"note\">{H(i18n["join.orgCode.hint"])}</p>");
        }
        // кто подаёт: аккаунт, которым вошли ЗДЕСЬ
        body.Append($"<div class=\"row\"><label>{H(i18n["join.applicant"])}</label>"
                    + $"<div class=\"ro\">{H(applicant)}</div></div>");
        // ЧЕЛОВЕК С ЭТОЙ ПОЧТОЙ У ДИРИЖЁРА УЖЕ ЕСТЬ (T-150): дальше он пускает только по
        // паролю ТОГО человека — по сути это вход на дирижёр, и свой пароль человек знает.
        // Не знает — значит, это не он: остаётся вернуться шагом назад и назвать другую почту
        if (view.AskPassword.Length > 0)
        {
            body.Append($"<div class=\"ok\">{H(SameEmailAsk(i18n, view))}</div>");
            if (view.AskPassword != ClusterJoinStatusDto.PasswordUnset)
            {
                body.Append(Field("conductorPassword", i18n["join.password"], "", "password",
                    autofocus: true, autocomplete: "current-password"));
                body.Append($"<p class=\"note\">{H(i18n["join.password.hint"])}</p>");
            }
        }
        body.Append(Field("note", i18n["join.note"], view.Note, "text"));
        body.Append($"<p class=\"note\">{H(i18n["join.anonymous.hint"])}</p>");
        body.Append("<div class=\"nav\">"
                    + "<button type=\"submit\" name=\"go\" value=\"back\" class=\"back\">"
                    + $"{H(i18n["setup.back"])}</button>"
                    + "<button type=\"submit\" name=\"action\" value=\"request\">"
                    + $"{H(i18n["join.send"])}</button></div>");
        body.Append("</form>");

        // «передумали»: организация всё-таки создаётся здесь. Без этой двери сервер,
        // у которого флажок первичного сняли по ошибке, оказался бы в тупике
        var user = Environment.UserName.Trim();
        var suggested = user.Length > 0 ? user : "AI2P";
        body.Append("<hr style=\"border:none;border-top:1px solid var(--line);margin:18px 0;\" />");
        body.Append($"<p class=\"note\">{H(i18n["join.orCreate"])}</p>");
        body.Append($"<form method=\"post\" action=\"{H(basePath)}{SetupPath}\">");
        body.Append(Token(ctx, antiforgery));
        body.Append(SetupState(view));
        body.Append(Field("newOrgName", i18n["login.org"], suggested, "text"));
        body.Append(Field("newOrgCode", i18n["login.orgCode"],
            Organization.NormalizeCode(suggested), "text"));
        body.Append("<button type=\"submit\" name=\"action\" value=\"create\">"
                    + $"{H(i18n["join.create"])}</button>");
        body.Append("</form>");
        body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/logout\">{H(i18n["account.logout"])}</a></p>");
        return Shell(i18n["setup.title"], body.ToString(), i18n.Lang);
    }

    /// <summary>Просьба пароля словами (T-150): кого именно узнал дирижёр и что теперь
    /// нужно. Имя человека подставляется — иначе непонятно, чей пароль спрашивают.</summary>
    private static string SameEmailAsk(I18nService i18n, SetupView view)
    {
        var who = view.KnownName.Trim().Length > 0 ? view.KnownName.Trim() : view.Email.Trim();
        return string.Format(i18n["join.sameEmail.ask"], who, view.Email.Trim());
    }

    /// <summary>Состояние заявки словами (ТЗ гл. 6: active / pending / deferred).</summary>
    private static string StatusText(I18nService i18n, SetupView view) => view.Status switch
    {
        OrgServerStatus.Active => i18n["join.accepted"],
        OrgServerStatus.Deferred => i18n["join.deferred"],
        _ => i18n["join.pending"],
    };

    private static string AdminPage(HttpContext ctx, I18nService i18n, IAntiforgery antiforgery,
        SecretStore secrets, string basePath, string error)
    {
        var setup = !Ai2pAuth.AdminPasswordSet(secrets);
        var body = new StringBuilder();
        body.Append($"<h1>{H(i18n["login.admin.title"])}</h1>");
        body.Append($"<p class=\"sub\">{H(setup ? i18n["login.admin.setup"] : i18n["login.admin.hint"])}</p>");
        if (error.Length > 0)
        {
            body.Append($"<div class=\"err\">{H(error)}</div>");
        }
        // ЭТО ВХОД С ЭТОГО ЖЕ КОМПЬЮТЕРА (T-174). Отказ приходил только ПОСЛЕ ввода пароля,
        // и человек, попавший сюда по внешнему адресу, считал, что не подходит пароль.
        // Теперь причина названа сразу — и назван адрес, по которому вход возможен
        if (!Ai2pAuth.IsLocalRequest(ctx))
        {
            var port = ctx.Request.Host.Port is { } value ? ":" + value : "";
            var local = $"{ctx.Request.Scheme}://{Core.LinkUrls.LocalHost}{port}{basePath}/server-admin";
            body.Append($"<div class=\"err\">{H(i18n["login.error.adminRemote"])}</div>");
            body.Append($"<p class=\"note\">{H(string.Format(i18n["login.admin.remote"], local))}</p>");
            body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/\">{H(i18n["login.back"])}</a></p>");
            return Shell(i18n["login.admin.title"], body.ToString(), i18n.Lang);
        }
        body.Append($"<form method=\"post\" action=\"{H(basePath)}/server-admin\">");
        body.Append(Token(ctx, antiforgery));
        body.Append($"<div class=\"row\"><label>{H(i18n["login.admin.login"])}</label>"
                    + $"<div class=\"ro\">{H(Ai2pAuth.AdminLogin(secrets))}</div></div>");
        body.Append(Field("password", i18n["login.password"], "", "password", autofocus: true,
            autocomplete: setup ? "new-password" : "current-password"));
        if (setup)
        {
            body.Append(Field("password2", i18n["login.password2"], "", "password",
                autocomplete: "new-password"));
        }
        body.Append($"<button type=\"submit\">{H(setup ? i18n["login.admin.set"] : i18n["login.submit"])}</button>");
        body.Append("</form>");
        body.Append($"<p class=\"note\"><a href=\"{H(basePath)}/\">{H(i18n["login.back"])}</a></p>");
        return Shell(i18n["login.admin.title"], body.ToString(), i18n.Lang);
    }

    private static string Token(HttpContext ctx, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return $"<input type=\"hidden\" name=\"{H(tokens.FormFieldName)}\" value=\"{H(tokens.RequestToken ?? "")}\" />";
    }

    /// <summary>
    /// Поле формы. <paramref name="autocomplete"/> — подсказка браузеру, и она важна:
    /// менеджер паролей игнорирует <c>off</c> у форм с паролем и сам решает, какое поле
    /// считать логином — берёт ближайшее текстовое перед паролем. На экране первого старта
    /// это оказывался КОД ОРГАНИЗАЦИИ, и потом браузер подставлял его в поля других форм
    /// (нашлось живой проверкой). Поэтому поле логина помечается явно (<c>username</c>),
    /// а пароли — <c>new-password</c>: тогда браузеру нечего угадывать.
    /// </summary>
    private static string Field(string name, string label, string value, string type,
        bool autofocus = false, string autocomplete = "off") =>
        $"<div class=\"row\"><label for=\"{H(name)}\">{H(label)}</label>"
        + $"<input id=\"{H(name)}\" name=\"{H(name)}\" type=\"{H(type)}\" value=\"{H(value)}\""
        + (autofocus ? " autofocus" : "") + $" autocomplete=\"{H(autocomplete)}\" /></div>";

    /// <summary>Выбор из нескольких значений (протокол сервера, T-139).</summary>
    private static string Select(string name, string label, string value, string[] options,
        Func<string, string>? text = null)
    {
        var items = new StringBuilder();
        foreach (var option in options)
        {
            items.Append($"<option value=\"{H(option)}\"{(option == value ? " selected" : "")}>"
                         + $"{H(text is null ? option : text(option))}</option>");
        }
        return $"<div class=\"row\"><label for=\"{H(name)}\">{H(label)}</label>"
               + $"<select id=\"{H(name)}\" name=\"{H(name)}\">{items}</select></div>";
    }

    /// <summary>Флажок формы (T-135): снятый checkbox в форму не отправляется — по его
    /// отсутствию сервер и узнаёт, что флажок сняли.</summary>
    private static string Check(string name, string label, bool value) =>
        $"<div class=\"chk\"><input type=\"checkbox\" id=\"{H(name)}\" name=\"{H(name)}\" value=\"1\""
        + (value ? " checked" : "") + $" /><label for=\"{H(name)}\">{H(label)}</label></div>";

    /// <summary>Переключатель одного значения из нескольких (T-291): выбор типового
    /// использования. Именно радиокнопки, а не список: вариантов четыре, они длинные,
    /// и человек должен видеть их все разом — это ответ на вопрос «что мне выбрать».</summary>
    private static string Radio(string name, string label, string value, bool selected)
    {
        var id = name + "_" + value;
        return $"<div class=\"chk\"><input type=\"radio\" id=\"{H(id)}\" name=\"{H(name)}\" "
               + $"value=\"{H(value)}\"" + (selected ? " checked" : "")
               + $" /><label for=\"{H(id)}\">{H(label)}</label></div>";
    }

    /// <summary>Флажок ОДНОГО ЗНАЧЕНИЯ из нескольких под общим именем (T-141): так собирается
    /// множественный выбор организаций дирижёра — отмеченные приходят списком.</summary>
    private static string CheckValue(string name, string label, string value, bool selected)
    {
        var id = name + "_" + value;
        return $"<div class=\"chk\"><input type=\"checkbox\" id=\"{H(id)}\" name=\"{H(name)}\" "
               + $"value=\"{H(value)}\"" + (selected ? " checked" : "")
               + $" /><label for=\"{H(id)}\">{H(label)}</label></div>";
    }

    private static string H(string? s) => HtmlEncoder.Default.Encode(s ?? "");

    /// <summary>Обёртка страницы: своя мелкая вёрстка — MudBlazor тут недоступен (страница
    /// не интерактивная), а тянуть его CSS ради двух форм незачем. Тёмная тема — по системной.
    /// <paramref name="lang"/> — язык страницы для браузера (T-148): «ru» тут больше
    /// не зашит, экраны первого старта говорят на выбранном языке.</summary>
    private static string Shell(string title, string body, string lang = "ru") => $$"""
        <!DOCTYPE html>
        <html lang="{{H(lang)}}">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>{{H(title)}}</title>
        <!-- значок вкладки (T-6-S0): тот же, что у самой программы. Ссылка ОТНОСИТЕЛЬНАЯ:
             страницы входа и первого старта лежат прямо под префиксом приложения
             (/login, /setup, /server-admin), поэтому она верна и при своём basePath -->
        <link rel="icon" href="_content/AI2P.UI/favicon.ico" sizes="any" />
        <style>
          :root { color-scheme: light dark; --bg:#f5f5f5; --card:#fff; --fg:#222; --line:#ccc; --accent:#1976d2; }
          @media (prefers-color-scheme: dark) {
            :root { --bg:#121212; --card:#1e1e1e; --fg:#e0e0e0; --line:#444; --accent:#90caf9; }
          }
          body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center;
                 background:var(--bg); color:var(--fg); font-family:system-ui,Segoe UI,sans-serif; }
          .card { background:var(--card); padding:28px 32px; border-radius:8px; min-width:340px;
                  box-shadow:0 2px 12px rgba(0,0,0,.18); }
          h1 { margin:0 0 4px; font-size:1.4rem; }
          .sub { margin:0 0 18px; opacity:.7; font-size:.86rem; max-width:340px; }
          .row { display:flex; flex-direction:column; margin-bottom:12px; }
          label { font-size:.78rem; opacity:.75; margin-bottom:3px; }
          input, select { padding:8px 10px; border:1px solid var(--line); border-radius:4px;
                  background:transparent; color:var(--fg); font-size:.95rem; }
          input:focus, select:focus { outline:none; border-color:var(--accent); }
          option { background:var(--card); color:var(--fg); }
          .ro { padding:8px 0; font-size:.95rem; }
          button { margin-top:8px; width:100%; padding:10px; border:none; border-radius:4px;
                   background:var(--accent); color:#fff; font-size:.95rem; cursor:pointer; }
          button:hover { filter:brightness(1.1); }
          .err { background:#b71c1c; color:#fff; padding:8px 12px; border-radius:4px;
                 margin-bottom:14px; font-size:.86rem; }
          .ok { background:#1b5e20; color:#fff; padding:8px 12px; border-radius:4px;
                margin-bottom:14px; font-size:.86rem; }
          .nav { display:flex; gap:10px; }
          .nav button.back { background:transparent; color:var(--fg); border:1px solid var(--line); }
          .chk { display:flex; align-items:center; gap:8px; margin-bottom:10px; }
          .chk input { width:auto; }
          .chk label { margin:0; font-size:.9rem; opacity:1; }
          .note { font-size:.78rem; opacity:.7; margin:14px 0 0; max-width:340px; }
          a { color:var(--accent); }
        </style>
        </head>
        <body><div class="card">{{body}}</div></body>
        </html>
        """;
}
