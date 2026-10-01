using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Server;
using AI2P.Server.Api;
using AI2P.Server.Org;
using AI2P.UI.Services;
using Microsoft.AspNetCore.Authentication;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// «--version»: напечатать версию и выйти (ТЗ гл. 4.3, этап 46). Ею пользуется скрипт
// установки, когда рядом с выкладкой нет version.json, — и просто человек в консоли
if (args.Contains("--version"))
{
    Console.WriteLine(AI2P.Core.AppInfo.Version);
    return;
}

// «cli …»: КЛИЕНТ КОМАНДНОЙ СТРОКИ ДЛЯ ИИ-АГЕНТА (T-34-S0). Его зовёт обёртка ai2p.cmd /
// ai2p из каталога установки, а её — сам агент, прямо в ходе своей работы. Ветка ранняя,
// как и «--version»: хост поднимать не нужно вовсе — клиент только разбирает командную
// строку и ходит в раздел /api/agent УЖЕ РАБОТАЮЩЕГО сервера по петле (адрес и токен
// задания приходят переменными окружения AI2P_URL и AI2P_JOB_TOKEN).
//
// Отдельного exe нет намеренно: buildRelease публикует ровно один проект, а в полной
// (self-contained) выкладке framework-dependent exe не запустился бы вовсе — рантайма
// на машине нет. Словари читаются здесь же: клиент разговаривает с агентом на языке
// команды задачи (T-190), он приезжает переменной AI2P_LANG
if (args.Length > 0
    && string.Equals(args[0], AI2P.Connectors.AgentCli.SubCommand, StringComparison.OrdinalIgnoreCase))
{
    AI2P.Core.Loc.Load(Path.Combine(AppContext.BaseDirectory, "i18n"), AI2P.Core.LocCatalog.BaseLanguage);
    if (Environment.GetEnvironmentVariable(AI2P.Connectors.AgentCli.LangVar) is { Length: > 0 } cliLang)
    {
        AI2P.Core.Loc.Lang = cliLang;
    }
    Environment.ExitCode = await AI2P.Server.Cli.AgentCliRunner.RunAsync(args[1..]);
    return;
}

// РЕЖИМ ЗАПУСКА (T-271): консоль (умолчание) или сервис ОС. Считается ПЕРВЫМ делом —
// от него зависит и открытие браузера, и поведение при занятом порте, и то, что вообще
// имеет смысл писать в консоль. Службу заводит скрипт makeAsServise.* из выкладки
var asService = ServiceRun.Init(args);

// словари приложения (ТЗ гл. 9, T-180): читаются ПЕРВЫМИ — тексты сообщений хранилища,
// коннекторов и API берутся отсюда, а первые из них выходят ещё до чтения config.json.
// Язык установки известен только из config.json и проставляется ниже, сразу за его чтением
AI2P.Core.Loc.Load(Path.Combine(AppContext.BaseDirectory, "i18n"), AI2P.Core.LocCatalog.BaseLanguage);
// РАБОЧИЙ КАТАЛОГ УСТАНОВКИ (T-287): config.json, data/, logs/ и secrets/ лежат рядом
// с приложением — но только если туда можно писать. Установка «для всех пользователей»
// кладёт программу в C:\Program Files\AI2P, и там писать нельзя ничем, кроме прав
// администратора: рабочие файлы уходят в каталог данных компьютера (см. AppHome).
// Путь по-прежнему переопределяется аргументом --config (ТЗ гл. 10)
//
// ВЕСЬ ЭТОТ КУСОК — под перехватом: он идёт ДО настройки логов, и его исключение улетало
// в консоль необработанным. Запущенное ярлыком окно при этом закрывается мгновенно, и
// человек видит только то, что «программа не запускается» (жалоба T-287)
AI2P.Server.AppHomeInfo home;
string configPath;
bool configMerged;
Ai2pConfig config;
try
{
    home = AppHome.Resolve(args);
    configPath = home.ConfigPath;
    // обновление поверх старой версии (ТЗ гл. 4.3, этап 46): скрипт установки кладёт рядом
    // config.new.json и рабочий config.json не трогает — слияние делаем здесь, ДО чтения:
    // основа новая (в ней все новые параметры), поверх ложатся рабочие значения пользователя
    configMerged = ConfigMerge.ApplyPending(configPath, home.DistDir);
    config = Ai2pConfig.Load(configPath);
}
catch (Exception ex)
{
    StartupFailed(ex, asService);
    return;
}
AI2P.Core.Loc.Lang = config.Language;
// о слиянии и о каталоге сообщаем ПОСЛЕ чтения config.json: язык установки лежит в нём же,
// а до чтения сообщение вышло бы на базовом языке независимо от настройки (ТЗ гл. 9)
if (home.Relocated)
{
    Console.WriteLine(AI2P.Core.Loc.T("msg.appHome.1", home.DistDir, home.Dir));
}
if (home.SeededFrom.Length > 0)
{
    Console.WriteLine(AI2P.Core.Loc.T("msg.appHome.4", home.SeededFrom, configPath));
}
if (home.LegacyDataDir.Length > 0)
{
    Console.WriteLine(AI2P.Core.Loc.T("msg.appHome.2", home.LegacyDataDir, home.Dir));
}
if (configMerged)
{
    Console.WriteLine(AI2P.Core.Loc.T("msg.program.1", configPath));
}
// каталоги этого компьютера под его платформу (T-164): до T-164 в config.json дистрибутива
// был зашит виндовый «C:\ai», и первичная установка на Linux получала репозиторий моделей
// с этим путём — каталогом он там не становится никогда. Правится ОДИН раз при старте
// (правки записываются в config.json), иначе значение переживало бы и обновление версии:
// ConfigMerge кладёт значения пользователя поверх новых умолчаний
var platformFixes = config.NormalizePlatformPaths();
if (platformFixes.Count > 0)
{
    // запись конфигурации не обязана удаваться (T-287): каталог мог оказаться закрыт
    // на запись — правки тогда действуют на этот запуск и не переживают перезапуск,
    // но ронять из-за этого работающую установку незачем
    try
    {
        config.Save(configPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine(AI2P.Core.Loc.T("msg.appHome.5", configPath, ex.Message));
    }
    foreach (var fix in platformFixes)
    {
        Console.WriteLine($"AI2P: {fix}");
    }
}
// ОТ ЧЕГО СЧИТАЮТСЯ КАТАЛОГИ ЖЕЛЕЗА (T-291): репозиторий моделей, дистрибутивы и пакеты
// задаются относительными путями и считаются от каталога РЯДОМ с установкой — так веса
// и распакованные пакеты не попадают внутрь каталога программы, который переписывает
// обновление версии. Установка в каталог программ системы (T-287) — исключение: там
// считаем от рабочего каталога данных, шаг вверх привёл бы в C:\Program Files
var machineRoot = Ai2pConfig.StorageSettings.MachineRoot(AppContext.BaseDirectory, home.Dir);

// адрес ВНУТРЕННИХ вызовов UI — всегда петля: UI ходит в собственное API по localhost,
// и токен задания у клиента командной строки принимается тоже только с петли
var url = $"{config.Ui.Protocol}://localhost:{config.Ui.Port}";
// адрес, который сервер СЛУШАЕТ (ТЗ гл. 6, этап 41): по умолчанию все интерфейсы — иначе
// снаружи к серверу не подключиться и кластер невозможен
var bindAddress = ParseBind(config.Ui.BindAddress);
var bindHost = bindAddress.AddressFamily == AddressFamily.InterNetworkV6
    ? $"[{bindAddress}]"
    : bindAddress.ToString();
var listenUrl = $"{config.Ui.Protocol}://{bindHost}:{config.Ui.Port}";
// префикс URL после порта (ТЗ гл. 11): http://localhost:5480/ai2p
var basePath = config.Ui.NormalizedBasePath();
var uiUrl = url + basePath;
// АДРЕС ДЛЯ БРАУЗЕРА (T-315). До этой правки браузер открывался по петле ВСЕГДА, не глядя
// на настройки сервера. По HTTP это безобидно, а по HTTPS — прямая беда: сертификат выписан
// на ИМЯ сервера (CN/SAN), «localhost» в нём обычно не назван вовсе, и браузер отвергает
// соединение по несовпадению имени — человек видит «вы соединены с сайтом небезопасно»
// и считает, что HTTPS в программе не работает. Поэтому по HTTPS открываем СВОЙ адрес
// (hostname/port из настроек), а по HTTP оставляем петлю: она не зависит ни от DNS,
// ни от того, что имя сервера человек написал наугад
var browserUrl = config.Ui.IsHttps && config.Ui.Hostname.Trim().Length > 0
    ? config.Ui.OwnBaseUrl()
    : uiUrl;
// адрес, по которому ИИ-агент зовёт действия AI2P клиентом командной строки (T-34-S0):
// токен задания принимается ТОЛЬКО с петлевого адреса, значит и ходить надо по петле —
// внешнее имя сервера тут не годится (у него бывает свой, проброшенный порт, T-2-S1)
AI2P.Connectors.AgentSessionRegistry.LocalBaseUrl = uiUrl;
// ДОВЕРИЕ СЕРТИФИКАТУ СОСЕДЕЙ ПО КЛАСТЕРУ (T-206): ставится ДО того, как заведён первый
// клиент сервер-сервер — обработчик HttpClient создаётся один раз и потом не меняется
AI2P.Server.HttpsPeers.TrustAny = config.Ui.Https.TrustAnyPeer;

// один экземпляр приложения на компьютере (ТЗ гл. 3, принцип 5):
// порт занят → открыть браузер на уже работающем экземпляре и завершиться
if (IsPortBusy(bindAddress, config.Ui.Port))
{
    // СЕРВИСУ ОТКРЫВАТЬ НЕЧЕГО (T-271): браузера на рабочем столе у него нет, а тихий
    // выход с кодом 0 менеджер служб считает нормальным завершением и молчит. Поэтому
    // в сервисном запуске занятый порт — это ошибка, и она видна в журнале службы
    if (asService)
    {
        Console.Error.WriteLine(AI2P.Core.Loc.T("msg.program.3", config.Ui.Port));
        Environment.ExitCode = 1;
        return;
    }
    Console.WriteLine(AI2P.Core.Loc.T("msg.program.2", browserUrl));
    OpenBrowser(browserUrl);
    return;
}

// технические логи: .jsonl с ротацией по дням, Serilog (ТЗ п. 6.3)
var logDir = config.ResolveDir(configPath, config.Logging.Dir);
// уровень вывода в лог (T-136): один параметр на технический лог и на журнал работ,
// правится в UI без перезапуска — поэтому ControlledBy, а не MinimumLevel.Is
LogSwitch.Apply(config.Logging.Level);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(LogSwitch.Serilog)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // Rendered-форматтер (правка v1.14): в файл пишется готовое сообщение "@m" с подставленными
    // значениями (у CompactJsonFormatter — только шаблон "@mt", файл нечитаем глазами)
    .WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(logDir, "ai2p-.jsonl"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31)
    .CreateLogger();

try
{
    // ContentRoot = каталог приложения, а не текущий каталог процесса: при запуске
    // ярлыком, планировщиком или из другого каталога иначе теряется wwwroot
    // релизной выкладки и статика (MudBlazor, ai2p.js) не отдаётся
    // СВОИ КЛЮЧИ ИЗ АРГУМЕНТОВ УБИРАЮТСЯ (T-271). Args уходят в конфигурацию хоста, а её
    // разбор понимает только пары «--ключ значение»: одинокий «--service» съедает СЛЕДУЮЩИЙ
    // аргумент как своё значение, и «AI2P.Server.exe --service --config D:\a\config.json»
    // падает FormatException ещё до старта. Наши ключи уже разобраны выше и хосту не нужны
    var hostArgs = args.Where(a =>
        !string.Equals(a, ServiceRun.ServiceArg, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(a, ServiceRun.ConsoleArg, StringComparison.OrdinalIgnoreCase)).ToArray();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = hostArgs,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.WebHost.UseUrls(listenUrl);
    builder.Host.UseSerilog();

    // ЖИЗНЕННЫЙ ЦИКЛ СЕРВИСА ОС (T-271). Обе обёртки сами проверяют, под кем идёт процесс,
    // и в консольном запуске не делают НИЧЕГО — поэтому звать их можно всегда, а «по
    // умолчанию консоль» получается само собой. Что они дают: доклад менеджеру служб
    // «я поднялся» (иначе Windows через 30 секунд считает службу зависшей) и остановку
    // по его команде вместо Ctrl+C. ContentRoot у службы иначе был бы system32 — он
    // задан выше явно и от текущего каталога процесса не зависит
    if (OperatingSystem.IsWindows())
    {
        builder.Host.UseWindowsService(options => options.ServiceName = ServiceRun.ServiceName);
    }
    else if (OperatingSystem.IsLinux())
    {
        builder.Host.UseSystemd();
    }

    // при запуске из каталога сборки статика RCL (_content/MudBlazor) берётся из манифеста;
    // в publish-выкладке манифеста нет — файлы лежат в wwwroot физически
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, "AI2P.Server.staticwebassets.runtime.json")))
    {
        builder.WebHost.UseStaticWebAssets();
    }

    // --- хранилище (ТЗ гл. 6, п. 2.15): серверная БД + по БД и каталогу на организацию ---
    // До этапа 40 здесь создавались 36 синглтонов вокруг одной Database. Организация — это
    // отдельная БД и отдельный каталог data/orgs/<ORG-N>/, поэтому весь набор сервисов стал
    // пер-организационным и живёт в OrgContext; здесь остаётся только то, что принадлежит
    // САМОМУ СЕРВЕРУ: секреты, процессы локальных моделей, настройки и реестр организаций.
    var dataDir = config.ResolveDir(configPath, config.Storage.DataDir);
    // секреты (ТЗ гл. 10): подкаталог secrets/ рядом с config.json — по одному json на ключ
    // модели или источника импорта (T-228), плюс сам secrets.json с ключами организаций и
    // учёткой администратора сервера. В dataDir, репликацию, выкладку и опись установки
    // не входят ни файл, ни подкаталог
    var secrets = new SecretStore(Path.Combine(Path.GetDirectoryName(configPath)!, "secrets.json"));
    // подкаталог заводится СРАЗУ, даже когда файловых ключей нет ни одного (T-234): ключ,
    // введённый в форме, принадлежит организации и файла не создаёт, поэтому иначе каталога
    // на обычной установке не было вовсе и человеку негде было увидеть, куда класть ключи
    // руками. Внутри — пояснение readme.txt
    secrets.EnsureDir();
    // ключи из СТАРОГО secrets.json раскладываются по отдельным файлам при старте: перенос
    // идемпотентен, ключи организаций и учётка администратора сервера остаются на месте
    secrets.MigrateFileToDir();
    // СЕРТИФИКАТ СЕРВЕРА (T-206). Читается ЗДЕСЬ, а не внутри Kestrel, по двум причинам:
    // пароль .pfx лежит в секретах (в config.json секретов нет никогда, гл. 10), а отказ
    // должен объясняться словами — «сертификат не задан», «файла нет», «нет закрытого
    // ключа». Своей ошибкой Kestrel сказал бы только «no server certificate», и человек
    // остался бы с установкой, которая не поднимается и ничего не объясняет.
    // Исключение ловит общий перехват старта (StartupFailed): окно не закроется молча
    if (config.Ui.IsHttps)
    {
        var certificate = HttpsCertificate.Load(config.Ui.Https, config.Ui.Hostname, configPath,
            secrets.Read(config.Ui.Https.PasswordRefOrDefault()) ?? "");
        Console.WriteLine($"AI2P: {HttpsCertificate.Describe(certificate)}");
        builder.WebHost.ConfigureKestrel(options =>
            options.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
    }
    // локальные серверы моделей (llama-server и т.п., ТЗ п. 2.9 launchCommand): процессы
    // физически на этом компьютере, поэтому сервис общий для всех организаций
    var localModels = new LocalModelProcessService();
    // вход в Claude CLI из AI2P (todo96): процесс `claude auth login` физически на этом
    // компьютере и вход у него один на машину — сервис общий для всех организаций
    var claudeLogin = new ClaudeLoginService();
    // реестр организаций нужен самим настройкам организации (признак дирижёра), а создаётся
    // после них — поэтому ссылка проставляется сразу за созданием
    OrgRegistry? registry = null;
    // АВТООБНОВЛЕНИЕ ПРИЛОЖЕНИЯ (T-208): проверка выпусков, скачивание пакета и перезапуск.
    // Приложение останавливается ШТАТНО (Lifetime), а не Environment.Exit: иначе оборвались
    // бы запись базы и выгрузка локальных моделей. Ссылка на Lifetime проставляется после
    // сборки приложения — сервис нужен раньше, его делегат уходит в контексты организаций
    IHostApplicationLifetime? lifetime = null;
    var appUpdate = new AppUpdateService(() => config, () =>
    {
        if (lifetime is not null)
        {
            lifetime.StopApplication();
        }
        else
        {
            Environment.Exit(0);
        }
    });
    var orgDeps = new OrgDeps(
        DbFile: config.Storage.DbFile,
        I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
        Secrets: secrets,
        LocalModels: localModels,
        // язык, адреса и каталоги — делегатами: настройки меняются «на лету» из UI (гл. 9, 10)
        Language: () => config.Language,
        PublicBaseUrl: () => config.Ui.PublicBaseUrl(),
        // КАТАЛОГИ ЖЕЛЕЗА (T-291) считаются не от config.json, а от каталога РЯДОМ
        // с установкой (Ai2pConfig.StorageSettings.MachineRoot): веса моделей и пакеты
        // переживают обновление версии, поэтому лежат снаружи каталога программы
        ModelsRepo: () => config.ModelsRepoPath(machineRoot),
        DistDir: () => config.DistDirPath(machineRoot),
        PackagesDir: () => config.PackagesDirPath(machineRoot),
        // дирижёр организации (ТЗ гл. 6, этап 41): признак спрашивается у реестра на ходу —
        // дирижёра переназначают, а контексты организаций живут до конца работы приложения
        IsConductor: orgId => registry!.Servers.IsLocalConductor(orgId),
        // раздельное владение (ТЗ гл. 6, этап 42): кто мы в кластере — внутренний ключ
        // локального сервера, его код в организации и справочник «кто есть кто».
        // Серверы лежат в СЕРВЕРНОЙ БД, а сервисы работают с БД организации, поэтому связь
        // идёт делегатами; коды и имена в ServerService кэшируются — их спрашивают на каждую
        // строку списка задач
        LocalServerId: () => registry!.Servers.LocalId(),
        LocalCode: orgId => registry!.Servers.LocalCode(orgId),
        ServerCode: (orgId, serverId) => registry!.Servers.CodeOf(orgId, serverId),
        ServerName: serverId => registry!.Servers.NameOf(serverId),
        // почта уведомлений «из логина пользователя» (T-272): логин в AI2P — это почта
        // аккаунта, а аккаунты живут в СЕРВЕРНОЙ базе, о которой организация не знает
        Account: accountId => registry!.Accounts.Get(accountId) is { } account
            ? new AI2P.Storage.Services.MemberAccount(account.Email, account.DeletedAt is null)
            : null,
        // почтовый сервер — настройка этого компьютера (T-272), правится «на лету» из UI
        Mail: () => config.Mail.ToOptions(),
        // ручной путь к программе плагина (T-115-S0): он лежит в config.json этого сервера
        // и НЕ реплицируется — реплицированный чужой путь ломает установку у соседа (T-164)
        PluginPath: code => config.Plugins.TryGetValue(code, out var plugin) ? plugin.Path : "",
        // список инструментов сервера MCP (T-119-S0): он снят с самого сервера явным
        // действием человека и лежит в config.json этого компьютера — у соседа версия
        // сервера MCP может быть другой, поэтому в базу организации список не едет
        PluginTools: code => config.Plugins.TryGetValue(code, out var mcp) ? mcp.Tools : [],
        // ПУТЬ К ПРОГРАММЕ ПЛАГИНА, НАЙДЕННОЙ УСТАНОВКОЙ МОДЕЛИ (T-156-S0). Пишем ровно так же,
        // как форма плагина (PUT /plugins/{код}/path): главная программа — в поле path,
        // остальные — в словарь paths по имени записи софта (T-146-S0). ТОЛЬКО В ПУСТОЕ ПОЛЕ:
        // человек мог показать свой экземпляр программы, и перетирать его установкой нельзя
        SavePluginPath: (code, soft, path) =>
        {
            if (!config.Plugins.TryGetValue(code, out var settings))
            {
                settings = new Ai2pConfig.PluginSettings();
                config.Plugins[code] = settings;
            }
            if (soft.Length == 0)
            {
                if (settings.Path.Trim().Length > 0)
                {
                    return;
                }
                settings.Path = path;
            }
            else
            {
                if (settings.Paths.TryGetValue(soft, out var own) && own.Trim().Length > 0)
                {
                    return;
                }
                settings.Paths[soft] = path;
            }
            settings.CheckedAt = DateTime.UtcNow;
            config.Save(configPath);
        },
        // ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ МОДЕЛИ (T-190-S0): флажки окна установки. Храним
        // ВЫКЛЮЧЕННОЕ — отсутствие записи значит «ставить всё», как и обещано человеку
        ModelOptionsOff: modelId =>
            config.ModelInstallOptions.TryGetValue(modelId, out var off) ? off : [],
        SaveModelOptionsOff: (modelId, off) =>
        {
            if (off.Count == 0)
            {
                config.ModelInstallOptions.Remove(modelId);
            }
            else
            {
                config.ModelInstallOptions[modelId] = [.. off];
            }
            config.Save(configPath);
        },
        // автообновление приложения (T-208): расписание-действие назначает ему время,
        // а работу делает сервер — обновляется установка целиком, а не организация
        AppUpdate: install => appUpdate.RunAuto(install));
    // реестр организаций (ТЗ п. 2.15): серверная БД (аккаунты, организации, серверы) и
    // живые контексты организаций. Аккаунт и организация при старте НЕ создаются — их
    // заводит экран первого старта (гл. 11); до входа UI недоступен
    var orgs = new OrgRegistry(dataDir, config.Storage.ServerDbFile, orgDeps);
    registry = orgs;
    // запись о самом себе в списке серверов кластера (ТЗ гл. 6, этап 41): её внутренний ключ
    // (unid) — то, чем сервер представляется другим; адрес берётся из config.json и
    // обновляется при каждом старте
    orgs.EnsureLocalServer(config.Ui.Hostname, config.Ui.Protocol, config.Ui.Hostname,
        config.Ui.Port, basePath, config.Ui.Hostname2, config.Ui.Port2);

    // репликация баз данных (ТЗ п. 6.1, гл. 6; этап 43): журнал изменений строк, курсоры,
    // pull-протокол и таймер автоматических сеансов. Один на установку: сеанс идёт
    // по организации, и организации обрабатываются по очереди
    var replication = new ReplicationService(orgs);
    var replicationRunner = new ReplicationRunner(orgs, replication, secrets);

    builder.Services.AddSingleton(orgs);
    builder.Services.AddSingleton(replication);
    builder.Services.AddSingleton(replicationRunner);
    builder.Services.AddSingleton(secrets);
    builder.Services.AddSingleton(localModels);
    builder.Services.AddSingleton(claudeLogin);
    builder.Services.AddSingleton(new ConfigHolder(config, configPath, machineRoot));
    builder.Services.AddSingleton(appUpdate);
    // документация, поставляемая с приложением (ТЗ гл. 14, todo47): каталог doc/ целиком
    // копируется в релизную выкладку, приложение показывает документы моделей в UI
    builder.Services.AddSingleton(new DocStore(
        () => config.Storage.DocDir.Trim().Length > 0
            ? config.ResolveDir(configPath, config.Storage.DocDir)
            : "",
        () => config.Language));

    // --- аутентификация (ТЗ гл. 12, todo39) ---
    // две независимые cookie-схемы: рабочий вход пользователя и отдельный локальный
    // логин admin'а сервера (настройки самого сервера правит только хозяин компьютера)
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<CurrentUserAccessor>();
    builder.Services.AddAuthentication(Ai2pAuth.Scheme)
        .AddCookie(Ai2pAuth.Scheme, options =>
        {
            options.Cookie.Name = "ai2p.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // пути без basePath: обработчик cookie сам подставляет PathBase запроса
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/login";
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            // API отвечает кодом, а не редиректом на страницу входа: иначе клиент
            // (ApiClient, будущие плагины) получал бы HTML вместо ошибки
            options.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
            // COOKIE ОТ ПРЕЖНЕЙ УСТАНОВКИ (T-141). Ключи шифрования cookie лежат в профиле
            // пользователя, а не в каталоге сервера: снеся каталог и поставив сервер заново,
            // браузер по-прежнему присылает cookie со СТАРЫМ аккаунтом — и приложение считало,
            // что человек вошёл, хотя в новой базе нет ни одного пользователя. Первый старт
            // проходил мимо, лечилось только чисткой кэша браузера. Проверяем на каждый
            // запрос: аккаунта нет или он отключён — cookie недействительна (это же убирает
            // вход по cookie у удалённого пользователя, T-140)
            options.Events.OnValidatePrincipal = async ctx =>
            {
                var id = ctx.Principal?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var known = id is { Length: > 0 }
                            && ctx.HttpContext.RequestServices.GetRequiredService<OrgRegistry>()
                                .Accounts.Get(id) is { IsActive: true, DeletedAt: null };
                if (!known)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(Ai2pAuth.Scheme);
                }
            };
        })
        .AddCookie(Ai2pAuth.AdminScheme, options =>
        {
            options.Cookie.Name = "ai2p.admin";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/server-admin";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // мультиязычность (ТЗ гл. 9): JSON-словари i18n/, язык из config.json
    builder.Services.AddSingleton(new I18nService(Path.Combine(AppContext.BaseDirectory, "i18n"), config.Language));

    // HTTP API: camelCase + snake_case-enum'ы (общие настройки в Ai2pJson)
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        foreach (var converter in Ai2pJson.Options.Converters)
        {
            options.SerializerOptions.Converters.Add(converter);
        }
    });

    // API-first (ТЗ гл. 3, принцип 1): UI работает только через HTTP API;
    // базовый адрес — с префиксом basePath и завершающим «/» для относительных путей "api/…"
    // токен процесса (ТЗ гл. 12, todo39): им UI подтверждает, что запрос по петле пришёл
    // из этого же приложения, и передаёт аккаунт вошедшего — cookie браузера сюда не доходит
    // организация circuit'а (ТЗ п. 2.15, этап 40) берётся из base href страницы: в разных
    // вкладках браузера один и тот же пользователь может работать в разных организациях,
    // поэтому она принадлежит подключению, а не приложению
    builder.Services.AddScoped(sp =>
    {
        var nav = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var path = new Uri(nav.BaseUri).AbsolutePath;
        if (basePath.Length > 0 && path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            path = path[basePath.Length..];
        }
        // вход администратора сервера (гл. 12) — ОТДЕЛЬНАЯ вторая cookie, и до собственного
        // API по петле она не доходит. Здесь запрос, которым открыт этот circuit, ещё под
        // рукой, и middleware уже пометил в нём вход администратора — запоминаем признак
        var admin = sp.GetService<IHttpContextAccessor>()?.HttpContext?
            .Items.ContainsKey(Ai2pAuth.AdminScheme) == true;
        // обработчик ОБЩИЙ и не выбрасывается вместе с клиентом (T-206): по HTTPS проверка
        // сертификата на петлевом адресе не действует — там тот же компьютер (HttpsPeers)
        return new ApiClient(new HttpClient(HttpsPeers.LoopbackHandler(), disposeHandler: false)
            { BaseAddress = new Uri(uiUrl + "/") })
        {
            InternalToken = Ai2pAuth.InternalToken,
            OrgCode = path.Trim('/'),
            IsServerAdmin = admin,
        };
    });
    builder.Services.AddScoped<UiState>();

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents()
        // ВСТАВКА КАРТИНКИ ИЗ БУФЕРА (Ctrl+V, T-136): картинка едет от браузера к серверу
        // одним вызовом JS→.NET по каналу Blazor, а у него предел размера сообщения по
        // умолчанию 32 КБ — любой снимок экрана его превышает, вызов не доходит, и связь
        // страницы с сервером обрывается («перестало вставлять картинку»). Поднимаем предел
        // до 32 МБ: это касается ТОЛЬКО служебного канала UI, файлы через диалог выбора
        // по-прежнему идут потоком (MarkdownEditor.MaxUploadBytes)
        .AddHubOptions(options => options.MaximumReceiveMessageSize = 32 * 1024 * 1024);
    builder.Services.AddMudServices();

    var app = builder.Build();

    // префикс URL /ai2p (ТЗ гл. 11): всё приложение живёт под basePath;
    // заход на корень без префикса перенаправляется на UI
    if (basePath.Length > 0)
    {
        app.UsePathBase(basePath);
        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.PathBase.HasValue && ctx.Request.Path == "/")
            {
                ctx.Response.Redirect(basePath + "/");
                return;
            }
            await next();
        });
    }

    // организация сегментом URL (ТЗ гл. 11, этап 40): /ai2p/<код>/… Ставится ДО маршрутизации —
    // у служебных разделов (api, ресурсы Blazor, вход) сегмент организации отрезается,
    // а страницам путь не меняется: Blazor сопоставляет маршруты относительно base href
    app.UseAi2pOrgPath(basePath);
    // МЕДЛЕННЫЕ ЗАПРОСЫ В ЖУРНАЛ (T-363-S0). Жалобу «открытие задачи идёт 7–12 секунд»
    // разбирать было нечем: замеров у сервера не было вовсе, и виновника приходилось искать
    // чтением кода. Пишется только то, что и правда идёт долго, поэтому журнал обычной работы
    // от этой записи не растёт, а у следующей жалобы сразу есть путь и время
    const double slowRequestMs = 1000; // порог «долго» — секунда: человек это уже замечает
    app.Use(async (ctx, next) =>
    {
        var started = Stopwatch.GetTimestamp();
        await next();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= slowRequestMs)
        {
            Log.Warning("Долгий запрос {Method} {Path} — {Elapsed:F0} мс",
                ctx.Request.Method, ctx.Request.Path.Value ?? "", elapsed);
        }
    });
    app.UseStaticFiles();
    // UseRouting вызывается явно: иначе WebApplication поставит её в самое начало конвейера,
    // и правка пути выше уже не повлияла бы на выбор эндпойнта
    app.UseRouting();
    app.UseAuthentication();
    // вторая схема (admin сервера) не разбирается автоматически — она не схема по умолчанию;
    // разбираем её здесь, чтобы CurrentUserAccessor.IsServerAdmin видел вход без await
    app.Use(async (ctx, next) =>
    {
        var admin = await ctx.AuthenticateAsync(Ai2pAuth.AdminScheme);
        if (admin.Succeeded)
        {
            ctx.Items[Ai2pAuth.AdminScheme] = true;
            // личность администратора добавляется в личность запроса — оттуда её берёт
            // AuthenticationState circuit'а, и UI узнаёт про вход администратора сервера
            // (в собственное API он ходит по петле, куда cookie администратора не доходит).
            // ТОЛЬКО когда пользователь уже вошёл обычным входом: иначе одна лишь cookie
            // администратора открывала бы доступ ко всему UI — она не заменяет вход (гл. 12)
            if (ctx.User.Identity?.IsAuthenticated == true)
            {
                foreach (var identity in admin.Principal.Identities)
                {
                    ctx.User.AddIdentity(identity);
                }
            }
        }
        await next();
    });
    // заход на {basePath}/ без организации — увести в организацию вошедшего по умолчанию
    app.UseAi2pOrgHome(basePath);
    app.UseAuthorization();
    app.UseAntiforgery();

    // страницы входа (ТЗ гл. 11 «Первый старт», гл. 12) — доступны без аутентификации
    app.MapAi2pAuthPages();
    // протокол сервер-сервер (ТЗ гл. 6, этап 41): сюда ходят другие серверы кластера,
    // личность подтверждается токеном сервера, а не cookie пользователя
    app.MapAi2pCluster();
    // действия AI2P для ИИ-агента вызовом, а не маркером (T-34-S0): сюда ходит клиент
    // командной строки ai2p. Личность — токен ЗАДАНИЯ, а не cookie человека, поэтому
    // раздел отдельной группой, как и протокол сервер-сервер
    app.MapAi2pAgent();
    app.MapAi2pApi();
    // до входа UI не отдаётся: неаутентифицированный запрос уводится на страницу входа
    app.MapRazorComponents<AI2P.Server.Components.App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(AI2P.UI.Pages.Home).Assembly)
        .RequireAuthorization();

    app.Logger.LogInformation("AI2P: конфигурация {ConfigPath}, данные {DataDir}, UI {Url}",
        configPath, dataDir, browserUrl);
    // режим запуска — в лог отдельной строкой (T-271): у службы консоли не видно вовсе,
    // и «почему не открылся браузер» отвечает именно она
    app.Logger.LogInformation("AI2P: запуск {RunMode}{ServiceName}",
        asService ? "как сервис ОС" : "консольный",
        asService ? $" ({ServiceRun.ServiceName})" : "");
    // ЗАПИСКА «эта установка настроена сервисом» (T-271): её ставит makeAsServise.*, и она
    // переживает обновление (config.json скрипт установки не трогает, ConfigMerge кладёт
    // значения пользователя поверх новых умолчаний). Настоящее состояние службы знает ОС —
    // сюда смотрят install.* и человек; расхождение бывает, если службу удалили руками
    if (config.ServiceMode && !asService)
    {
        app.Logger.LogInformation(
            "AI2P: установка помечена как сервис (serviceMode), но этот запуск консольный");
    }

    // браузер открывается только у консольного запуска: у службы нет рабочего стола,
    // а на Windows она вдобавок работает в отдельном сеансе — окно всё равно не увидят
    if (config.Ui.OpenBrowserOnStart && !asService)
    {
        app.Lifetime.ApplicationStarted.Register(() => OpenBrowser(browserUrl));
    }

    // ПРОВЕРКА ОБНОВЛЕНИЙ ПРИ СТАРТЕ (T-208) — только у КОНСОЛЬНОГО запуска: его включают
    // и выключают, и старт для него единственный понятный момент «раз в день». Служба
    // работает сутками, и у неё проверка идёт записью расписания (2:00 местного времени).
    // Задержка в полминуты — чтобы сеть и сам сервер успели подняться, а человек увидел
    // экран, а не паузу; отказ проверки в журнале, на старте приложения он ничего не рвёт
    lifetime = app.Lifetime;
    if (!asService && (config.Update.AutoCheck || config.Update.AutoUpdate))
    {
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            var summary = appUpdate.RunAuto(config.Update.AutoUpdate);
            app.Logger.LogInformation("AI2P: проверка обновлений — {Summary}", summary);
        }));
    }

    // при остановке приложения выгрузить запущенные нами локальные серверы моделей
    app.Lifetime.ApplicationStopping.Register(localModels.StopAll);
    app.Lifetime.ApplicationStopping.Register(orgs.Dispose);
    app.Lifetime.ApplicationStopping.Register(replicationRunner.Dispose);
    app.Lifetime.ApplicationStopping.Register(replication.Dispose);

    // ДОВЕРИЕ СЕРТИФИКАТУ СОСЕДЕЙ СТАЛО УМОЛЧАНИЕМ (T-215-S0). Новому умолчанию в коде
    // и в config.json дистрибутива обновление НЕ подчиняется: слияние конфигураций кладёт
    // значения пользователя поверх умолчаний (ConfigMerge), а ключ trustAnyPeer лежит
    // в файле с самой версии 1.114 со значением false. Поэтому один раз — при первом старте
    // билда 129 — поднимаем его и обновляемым установкам: иначе после включения HTTPS
    // на одном сервере кластер встаёт с «удалённый сертификат отклонён», и почему —
    // не видно. Выключить настройку по-прежнему можно галочкой в форме своего сервера;
    // повторно шаг не сработает — он привязан к отметке билда в серверной БД
    if (Upgrade.UpgradingFrom(orgs) is { } upgradingFrom && upgradingFrom < 129
        && !config.Ui.Https.TrustAnyPeer)
    {
        config.Ui.Https.TrustAnyPeer = true;
        AI2P.Server.HttpsPeers.TrustAny = true;
        try
        {
            config.Save(configPath);
            app.Logger.LogInformation(
                "AI2P: включено доверие сертификату соседей по кластеру (умолчание с 1.129)");
        }
        catch (Exception ex)
        {
            // не записалось (права на каталог) — настройка действует до перезапуска,
            // и это лучше, чем не запуститься вовсе
            app.Logger.LogWarning(ex, "AI2P: не удалось сохранить {ConfigPath}", configPath);
        }
    }

    // одноразовые шаги обновления данных (ТЗ гл. 4.3, этап 46): выполняются при ПЕРВОМ
    // старте новой версии, до открытия организаций — иначе шаг чинил бы данные под уже
    // работающими сервисами
    Upgrade.Run(orgs);

    // открыть все активные организации (ТЗ п. 2.15): досеять справочники и запустить их
    // расписания (п. 2.12) — иначе расписание «просыпалось» бы только после захода в UI
    orgs.StartAll();
    // таймер репликации (ТЗ гл. 6, этап 43): сервер с заданным интервалом реплицируется
    // с дирижёром сам; без интервала — только по кнопке
    replicationRunner.Start();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AI2P: аварийное завершение");
    StartupFailed(ex, asService);
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// АВАРИЯ НА СТАРТЕ, ВИДНАЯ ЧЕЛОВЕКУ (T-287). Прежде исключение старта улетало
/// необработанным: в консоли оставался стек .NET, а запущенное ярлыком окно закрывалось
/// вместе с ним — со стороны «щёлкнул по значку, мигнуло чёрное окно, ничего не работает».
///
/// Поэтому: причина печатается словами, стек — следом (он нужен разбору), код возврата
/// ненулевой, а окно КОНСОЛЬНОГО запуска держится, пока не нажмут клавишу, но не дольше
/// минуты. Ждём только когда ввод действительно чей-то: у службы консоли нет вовсе,
/// а у запуска из скрипта ввод перенаправлен — там ожидание повесило бы проверку.
/// </summary>
static void StartupFailed(Exception ex, bool asService)
{
    Environment.ExitCode = 1;
    Console.Error.WriteLine(AI2P.Core.Loc.T("msg.program.4", ex.Message));
    Console.Error.WriteLine(ex.ToString());
    if (asService)
    {
        return;
    }
    try
    {
        if (Console.IsInputRedirected || Console.IsErrorRedirected)
        {
            return;
        }
        Console.Error.WriteLine(AI2P.Core.Loc.T("msg.program.5"));
        var until = DateTime.UtcNow.AddMinutes(1);
        while (DateTime.UtcNow < until && !Console.KeyAvailable)
        {
            Thread.Sleep(200);
        }
        if (Console.KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }
    }
    catch (Exception)
    {
        // консоли нет (запуск без окна) — ждать нечего и некого
    }
}

static bool IsPortBusy(IPAddress address, int port)
{
    try
    {
        using var listener = new TcpListener(address, port);
        listener.Start();
        listener.Stop();
        return false;
    }
    catch (SocketException)
    {
        return true;
    }
}

/// <summary>Интерфейс из настроек (ТЗ гл. 6): пусто или мусор — все интерфейсы.</summary>
static IPAddress ParseBind(string value) =>
    IPAddress.TryParse(value.Trim(), out var address) ? address : IPAddress.Any;

static void OpenBrowser(string url)
{
    try
    {
        // НА WINDOWS БРАУЗЕР ОТКРЫВАЕТ ПРОВОДНИК, А НЕ МЫ (T-388-S0): при ShellExecute адреса
        // браузер стартует ДОЧЕРНИМ процессом сервера и наследует его маркер доступа и окружение.
        // Сервер, запущенный от администратора (установщик «для всех пользователей» запускает
        // программу с правами установщика), поднимал Firefox тоже с повышенными правами —
        // обычный экземпляр с ним разговаривать не может (UIPI), и получается «вторая копия»
        // браузера: сеанс не восстанавливается, вкладки теряются, наш адрес не открывается.
        // explorer.exe передаёт адрес уже работающему проводнику пользователя, и браузер
        // стартует так же, как по щелчку по ссылке на рабочем столе
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { url }, UseShellExecute = false, CreateNoWindow = true });
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Не удалось открыть браузер: {Url}", url);
    }
}
