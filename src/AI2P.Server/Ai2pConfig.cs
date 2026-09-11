using AI2P.Core;
using System.Text;
using System.Text.Json;

namespace AI2P.Server;

/// <summary>
/// Настройки приложения из config.json (ТЗ гл. 10). Изменения из UI пишутся обратно в файл;
/// часть параметров (порт, протокол) применяется после перезапуска.
/// </summary>
public sealed class Ai2pConfig
{
    public UiSettings Ui { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>Работа с адаптерами LoRA (T-12-S1): пределы кадра датасета.</summary>
    public LoraSettings Lora { get; set; } = new();

    /// <summary>Почтовый сервер для уведомлений (T-272): свой у каждой установки.</summary>
    public MailSettings Mail { get; set; } = new();

    /// <summary>Пароль администратора сервера — общий с паролем пользователя (T-291).</summary>
    public ServerAdminSettings ServerAdmin { get; set; } = new();

    /// <summary>
    /// ПЛАГИНЫ НА ЭТОМ КОМПЬЮТЕРЕ (T-111-S0): по коду плагина — установлен ли он здесь,
    /// ручной путь к программе и время последней проверки. Ключ словаря — код плагина
    /// (<c>editor.shotcut</c>), тот же, что у записи организации и у каталога манифеста.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНО ОТ БАЗЫ: описание плагина принадлежит организации и реплицируется, а
    /// установка ВСЕГДА локальная — по образцу локальной модели. Реплицированный чужой путь
    /// ломает установку у соседа и «уже не уходит» (наука T-164): у него melt лежит в другом
    /// месте или не стоит вовсе.
    ///
    /// УМОЛЧАНИЙ ПУТЕЙ В ПОСТАВЛЯЕМОМ config.json БЫТЬ НЕ ДОЛЖНО — раздел там отсутствует
    /// целиком. Файл дистрибутива один на все системы, а обновление кладёт значения
    /// пользователя ПОВЕРХ новых умолчаний (ConfigMerge): раз попавший в файл путь уже
    /// не уходит. Пустой словарь означает «ни один плагин здесь ещё не настроен».
    /// </summary>
    public Dictionary<string, PluginSettings> Plugins { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ВЫКЛЮЧЕННЫЕ ДОПОЛНИТЕЛЬНЫЕ ОПЦИИ УСТАНОВКИ МОДЕЛЕЙ (T-190-S0), «идентификатор
    /// модели → коды опций» (<c>lora</c>). Хранится ВЫКЛЮЧЕННОЕ, а не выбранное: умолчание —
    /// «ставить всё», и отсутствие записи означает именно его. Настройка пер-серверная и
    /// НЕ реплицируется: адаптеры LoRA в кластере обучают обычно на одном компьютере, а
    /// на остальных только применяют. В поставляемом config.json раздела нет вовсе.
    /// </summary>
    public Dictionary<string, List<string>> ModelInstallOptions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string Language { get; set; } = "ru";
    public bool ServiceMode { get; set; }

    public sealed class UiSettings
    {
        public string Protocol { get; set; } = "http";
        public int Port { get; set; } = 5480;
        /// <summary>Префикс URL после порта (ТЗ гл. 11, «URL и ссылки на задачи»).</summary>
        public string BasePath { get; set; } = "/ai2p";
        /// <summary>Имя хоста для генерации ссылок на задачи (ТЗ гл. 11). Если у сервера есть
        /// настоящее DNS-имя или публичный IP — их указывают здесь: ссылки на задачи
        /// пересылают друг другу, и «localhost» в них бесполезен (todo41, замечание 2).</summary>
        public string Hostname { get; set; } = "localhost";

        /// <summary>
        /// ВТОРОЕ ИМЯ СЕРВЕРА — ТО, ПОД КОТОРЫМ ЕГО ВИДНО СНАРУЖИ (T-2-S1). Пусто (умолчание) —
        /// второго адреса нет, и всё работает как раньше, по одной паре имя/порт.
        ///
        /// Зачем: у сервера за NAT адресов ДВА. Внутри сети его зовут по адресу локальной сети
        /// (<see cref="Hostname"/>) — по нему же с ним разговаривают соседи по кластеру, и этот
        /// адрес обязан остаться внутренним: заменить его публичным значило бы гонять свой же
        /// трафик наружу через роутер и обратно. А человек заходит с телефона по публичному
        /// имени роутера с пробросом порта — и ссылка, построенная от внутреннего адреса, у него
        /// не открывается.
        ///
        /// Поэтому имён два: внутреннее — для кластера (запись сервера, <c>EnsureLocalServer</c>),
        /// внешнее — для ссылок, которые читает человек и пишет ИИ-агент
        /// (<see cref="PublicBaseUrl"/>).
        /// </summary>
        public string Hostname2 { get; set; } = "";

        /// <summary>
        /// Порт второго адреса (T-2-S1): на роутере проброшенный порт часто не совпадает
        /// с портом сервера. <c>null</c> — внешний порт тот же, что и <see cref="Port"/>.
        ///
        /// Без <see cref="Hostname2"/> не значит НИЧЕГО и не обрабатывается: порт без имени —
        /// это не адрес. В форме локального сервера поле в этом случае и не даётся заполнять.
        /// </summary>
        public int? Port2 { get; set; }

        /// <summary>
        /// Интерфейс, который слушает сервер (ТЗ гл. 6, этап 41). До этапа 41 сервер слушал
        /// только петлю и снаружи к нему было не подключиться вообще — кластер невозможен.
        /// <c>0.0.0.0</c> — все интерфейсы (умолчание), <c>127.0.0.1</c> — только этот
        /// компьютер, можно указать и конкретный адрес.
        ///
        /// Шифрование задаётся <see cref="Protocol"/> и разделом <see cref="Https"/> (T-206).
        /// </summary>
        public string BindAddress { get; set; } = "0.0.0.0";
        public HttpsSettings Https { get; set; } = new();

        /// <summary>Сервер работает по HTTPS (T-206): от этого зависит и то, что слушает
        /// Kestrel, и то, нужен ли сертификат вообще. В файл не пишется — это не настройка,
        /// а прочтение <see cref="Protocol"/>; лишнее поле в config.json человек принял бы
        /// за то, что можно править.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsHttps =>
            Protocol.Trim().Equals("https", StringComparison.OrdinalIgnoreCase);
        public bool OpenBrowserOnStart { get; set; } = true;
        /// <summary>Вторая строка заголовка закладок «Задача»/«Проект» (ТЗ v1.18, todo21):
        /// "name" — начало названия с «…», "code" — короткий код (display_id).</summary>
        public string TabTitleMode { get; set; } = "name";
        /// <summary>Ячейка календаря расписаний (ТЗ v1.37, todo34_3): после времени —
        /// "code" — код шаблона, "name" — начало заголовка шаблона.</summary>
        public string ScheduleCellMode { get; set; } = "code";

        /// <summary>Нормализованный префикс: с ведущим «/», без завершающего; пустая строка — без префикса.</summary>
        public string NormalizedBasePath()
        {
            var path = BasePath.Trim().Trim('/');
            return path.Length == 0 ? "" : "/" + path;
        }

        /// <summary>Второй адрес задан (T-2-S1): имя есть — значит ссылки строятся по нему.
        /// Порт без имени не адрес и не рассматривается.</summary>
        public bool HasExternalAddress => Hostname2.Trim().Length > 0;

        /// <summary>Имя сервера для ВНЕШНИХ ссылок: <c>hostname2</c>, если задано, иначе
        /// обычное <c>hostname</c> (T-2-S1).</summary>
        public string ExternalHostname() => HasExternalAddress ? Hostname2.Trim() : Hostname;

        /// <summary>Порт ВНЕШНИХ ссылок (T-2-S1): <c>port2</c> при заданном <c>hostname2</c>,
        /// иначе обычный порт. Без <c>hostname2</c> значение <c>port2</c> не используется.</summary>
        public int ExternalPort() => HasExternalAddress ? Port2 ?? Port : Port;

        /// <summary>
        /// Базовый URL для ссылок на задачи и файлы: протокол + ВНЕШНЕЕ имя + внешний порт +
        /// basePath. Внешняя пара — <c>hostname2</c>/<c>port2</c>, если она задана (T-2-S1);
        /// иначе, как и раньше, <c>hostname</c>/<c>port</c>.
        /// </summary>
        public string PublicBaseUrl() =>
            $"{Protocol}://{ExternalHostname()}:{ExternalPort()}{NormalizedBasePath()}";

        /// <summary>
        /// Базовый URL по СОБСТВЕННОМУ адресу этого сервера — <c>hostname</c>/<c>port</c>
        /// (T-2-S1). Внешние ссылки строятся не отсюда: это адрес, по которому сервер зовут
        /// в его сети (соседи по кластеру, браузер на этом же компьютере). Нужен там, где
        /// требуется УЗНАТЬ свою же ссылку: адрес у сервера теперь не один, и ссылка,
        /// написанная по внутреннему имени, своей быть не перестала.
        /// </summary>
        public string OwnBaseUrl() => $"{Protocol}://{Hostname}:{Port}{NormalizedBasePath()}";
    }

    /// <summary>
    /// СЕРТИФИКАТ СЕРВЕРА ДЛЯ HTTPS (T-206). Раздел был заведён пустышкой ещё в ранних
    /// версиях (<c>certFile</c>/<c>keyFile</c>) и не читался НИКЕМ: выбрав в форме сервера
    /// «https», человек получал установку, которая после перезапуска не поднималась вовсе —
    /// Kestrel не знал, каким сертификатом отвечать.
    ///
    /// Источников сертификата два, и они закрывают разные случаи:
    /// <list type="bullet">
    /// <item><b>файл</b> (<see cref="SourceFile"/>, умолчание) — <c>.pfx</c>/<c>.p12</c> либо
    /// пара PEM «сертификат + ключ»: так выглядит и сертификат от Let's Encrypt, и свой,
    /// выписанный openssl;</item>
    /// <item><b>хранилище системы</b> (<see cref="SourceStore"/>) — сертификат, УЖЕ
    /// установленный в этот компьютер: хранилище Windows (<c>certlm.msc</c>), связка ключей
    /// macOS, каталог хранилищ .NET на Linux. Файла с закрытым ключом тогда на диске нет
    /// вовсе, и это единственный способ на машине, где ключ выдаёт домен.</item>
    /// </list>
    ///
    /// Пароля здесь НЕТ по общему правилу гл. 10: <c>config.json</c> лежит открытым текстом
    /// и переживает обновление (ConfigMerge). Пароль <c>.pfx</c> хранится в секретах по
    /// ссылке <see cref="PasswordRef"/> — как пароль почты (T-272).
    /// </summary>
    public sealed class HttpsSettings
    {
        /// <summary>Сертификат берётся из ФАЙЛА (умолчание).</summary>
        public const string SourceFile = "file";

        /// <summary>Сертификат берётся из ХРАНИЛИЩА СЕРТИФИКАТОВ этого компьютера.</summary>
        public const string SourceStore = "store";

        /// <summary>Ссылка на пароль <c>.pfx</c> в хранилище секретов (<c>secrets/</c>).</summary>
        public const string DefaultPasswordRef = "https.certPassword";

        /// <summary>Откуда брать сертификат: <see cref="SourceFile"/> либо
        /// <see cref="SourceStore"/>. Пусто и любое незнакомое значение — «файл»: так
        /// конфигурация прежних версий (в ней были только два пути) читается как раньше.</summary>
        public string Source { get; set; } = SourceFile;

        /// <summary>Файл сертификата: <c>.pfx</c>/<c>.p12</c> (ключ внутри) либо PEM
        /// (<c>.pem</c>, <c>.crt</c>, <c>.cer</c>). Относительный путь считается от
        /// <c>config.json</c>, «~/…» — от домашнего каталога.</summary>
        public string CertFile { get; set; } = "";

        /// <summary>Файл закрытого ключа к PEM-сертификату; пусто — ключ лежит в самом
        /// <see cref="CertFile"/> (так отдают сертификат некоторые УЦ). Для <c>.pfx</c>
        /// не используется вовсе.</summary>
        public string KeyFile { get; set; } = "";

        /// <summary>Ссылка на пароль файла <c>.pfx</c>; само значение — в секретах.</summary>
        public string PasswordRef { get; set; } = DefaultPasswordRef;

        /// <summary>Хранилище: <c>CurrentUser</c> (умолчание) либо <c>LocalMachine</c>.
        /// На Linux/macOS <c>LocalMachine</c> обычно закрыт на чтение обычному пользователю —
        /// сертификат службы кладут в хранилище того пользователя, под которым она идёт.</summary>
        public string StoreLocation { get; set; } = "CurrentUser";

        /// <summary>Имя хранилища: <c>My</c> (личные сертификаты) — обычный случай.</summary>
        public string StoreName { get; set; } = "My";

        /// <summary>Отбор по имени (CN) — часть имени, как её показывает система. Пусто —
        /// ищем по <see cref="UiSettings.Hostname"/> сервера: именно на это имя сертификат
        /// и выписывают.</summary>
        public string Subject { get; set; } = "";

        /// <summary>Отбор по отпечатку — точнее имени и не путается, когда сертификатов
        /// на одно имя несколько (старый и продлённый). Задан — имя не смотрится.</summary>
        public string Thumbprint { get; set; } = "";

        /// <summary>
        /// ДОВЕРЯТЬ СЕРТИФИКАТУ СОСЕДЕЙ ПО КЛАСТЕРУ БЕЗ ПРОВЕРКИ (T-206). Браузеру
        /// сертификат своего УЦ ставят руками (см. документацию), а серверы ходят друг
        /// к другу сами — и при сертификате не от глобального УЦ обмен встал бы с ошибкой
        /// проверки, которую некому подтвердить.
        ///
        /// УМОЛЧАНИЕ — ДА (T-215-S0). Сертификат у наших серверов почти всегда самодельный
        /// (кнопка «Создать сертификат», T-315), и выключенная настройка означала одно:
        /// первый же сервер, подключаемый к кластеру с HTTPS, встаёт с «удалённый сертификат
        /// отклонён» — при том что в кластер он и так пускается только по общему секрету.
        /// Выключить проверку одной галочкой умеет тот, кому нужен строгий разбор цепочки.
        /// </summary>
        public bool TrustAnyPeer { get; set; } = true;

        /// <summary>Сертификат берётся из хранилища системы. В файл не пишется — это
        /// прочтение <see cref="Source"/>, а не отдельная настройка.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool FromStore =>
            Source.Trim().Equals(SourceStore, StringComparison.OrdinalIgnoreCase);

        /// <summary>Ссылка на пароль в том виде, в каком её читает хранилище секретов.</summary>
        public string PasswordRefOrDefault() =>
            PasswordRef.Trim().Length > 0 ? PasswordRef.Trim() : DefaultPasswordRef;

        /// <summary>Признак отбора в хранилище: отпечаток точнее имени, поэтому он и
        /// главнее; ни того ни другого не задано — ищем по имени самого сервера.</summary>
        public string StoreQuery(string hostname) =>
            Thumbprint.Trim().Length > 0 ? Thumbprint.Trim()
            : Subject.Trim().Length > 0 ? Subject.Trim()
            : (hostname ?? "").Trim();
    }

    public sealed class StorageSettings
    {
        public string DataDir { get; set; } = "./data";
        /// <summary>Имя файла БД ОРГАНИЗАЦИИ: <c>data/orgs/&lt;ORG-N&gt;/ai2p.db</c> (ТЗ п. 2.15).</summary>
        public string DbFile { get; set; } = "ai2p.db";
        /// <summary>Имя файла СЕРВЕРНОЙ БД в dataDir (ТЗ п. 6.4.1, v1.47): аккаунты,
        /// организации, серверы организаций, идентификатор узла.</summary>
        public string ServerDbFile { get; set; } = "server.db";

        /// <summary>Репозиторий локальных моделей (ТЗ v1.40, todo36_3): каталог весов
        /// скачиваемых моделей (Kandinsky и др.), файлы кладутся в &lt;репозиторий&gt;/models/&lt;группа&gt;/.
        /// Общий для всех установок каталог вне dataDir — веса большие и переживают переустановку.
        /// С T-291 умолчание ОТНОСИТЕЛЬНОЕ (см. <see cref="MachineRoot"/>): рядом с каталогом
        /// установки, а не «C:\ai» одинаковый у всех.</summary>
        public string ModelsRepo { get; set; } = DefaultModelsRepo;

        /// <summary>Куда качать дистрибутивы пакетов (ТЗ v1.42, todo36_5): пусто — во временный
        /// каталог пользователя, и после распаковки они удаляются; указан каталог — качать туда
        /// и НЕ удалять (повторная установка переиспользует скачанное). Умолчание с T-291 —
        /// <c>./distribs</c>, то есть скачанное сохраняется.</summary>
        public string DistDir { get; set; } = DefaultDistDir;

        /// <summary>Корневой каталог установки пакетов (ТЗ v1.42, todo36_5): ComfyUI, llama.cpp …
        /// Пусто — &lt;репозиторий моделей&gt;/packages (так это работало до T-291 и так остаётся
        /// у конфигураций, где поле пусто); умолчание новой установки — <c>./packages</c>.</summary>
        public string PackagesDir { get; set; } = DefaultPackagesDir;

        /// <summary>
        /// Каталог документации (ТЗ гл. 14, todo47): подкаталоги по языкам —
        /// <c>doc/&lt;язык&gt;/models/&lt;название модели&gt;.md</c>. Пусто — ищется сам:
        /// сначала рядом с приложением (так он лежит в релизной выкладке), затем вверх
        /// по дереву каталогов (так он лежит при запуске из исходников).
        /// </summary>
        public string DocDir { get; set; } = "";

        /// <summary>
        /// КАТАЛОГИ ЭТОГО КОМПЬЮТЕРА — УМОЛЧАНИЯ (T-291). До 1.101 репозиторий моделей был
        /// абсолютным и одинаковым у всех (Windows — <c>C:\ai</c>, Linux/macOS — <c>~/ai</c>),
        /// а каталоги дистрибутивов и пакетов пустыми. Человек, поставивший программу в свою
        /// папку, получал модели неизвестно где, и первая же установка модели упиралась
        /// в вопрос «куда это всё легло» (жалоба T-291).
        ///
        /// Теперь умолчания ОТНОСИТЕЛЬНЫЕ и считаются от каталога рядом с установкой
        /// (<see cref="MachineRoot"/>): поставили в <c>C:\ai\AI2P</c> — веса лягут
        /// в <c>C:\ai\models</c>, пакеты в <c>C:\ai\packages</c>, дистрибутивы
        /// в <c>C:\ai\distribs</c>. Умолчание платформонезависимо, поэтому его можно держать
        /// и в config.json дистрибутива (правило T-164 про ЧУЖИЕ АБСОЛЮТНЫЕ пути в силе).
        /// </summary>
        public const string DefaultModelsRepo = "./models";

        /// <inheritdoc cref="DefaultModelsRepo"/>
        public const string DefaultDistDir = "./distribs";

        /// <inheritdoc cref="DefaultModelsRepo"/>
        public const string DefaultPackagesDir = "./packages";

        /// <summary>Пример АБСОЛЮТНОГО пути этой системы — для сообщений человеку
        /// («путь другой системы; здесь он выглядит так»).</summary>
        public static string SamplePath() => SamplePath(OperatingSystem.IsWindows());

        /// <summary>То же для ЗАДАННОЙ системы. Параметр нужен проверкам (T-164): поведение
        /// на Linux иначе проверялось бы только на Linux, а ломается оно как раз там.</summary>
        public static string SamplePath(bool windows) => windows ? @"C:\ai" : "~/ai";

        /// <summary>
        /// Путь ЧУЖОЙ платформы (T-164): на Linux/macOS — путь с буквой диска
        /// (<c>C:\ai</c>) или сетевой (<c>\\сервер\общий</c>), на Windows — путь от корня
        /// POSIX (<c>/home/mike/ai</c>). Такой путь попадает в конфигурацию двумя способами:
        /// он лежал в config.json дистрибутива (до T-164 там было зашито <c>C:\ai</c>) либо
        /// приехал из конфигурации, скопированной с другого компьютера. Каталогом он не
        /// станет никогда: на Linux <c>C:\ai</c> — это ОДНО имя файла, и репозиторий моделей
        /// оказывался бы подкаталогом с именем «C:\ai» рядом с приложением.
        ///
        /// «~/ai» чужим не считается: домашний каталог понимают обе платформы
        /// (<see cref="AI2P.Core.PathHome"/>).
        /// </summary>
        public static bool IsForeignPath(string? path) =>
            IsForeignPath(path, OperatingSystem.IsWindows());

        /// <summary>То же для ЗАДАННОЙ системы (см. <see cref="SamplePath(bool)"/>).</summary>
        public static bool IsForeignPath(string? path, bool windows)
        {
            var value = (path ?? "").Trim();
            if (value.Length == 0 || AI2P.Core.PathHome.StartsWithHome(value))
            {
                return false;
            }
            if (windows)
            {
                // «//сервер/общий» — сетевой путь в прямых слэшах, он здесь свой
                return value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal);
            }
            return (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':')
                   || value.StartsWith(@"\\", StringComparison.Ordinal);
        }

        /// <summary>
        /// ОТ ЧЕГО СЧИТАЮТСЯ ОТНОСИТЕЛЬНЫЕ КАТАЛОГИ ЭТОГО КОМПЬЮТЕРА (T-291) — репозиторий
        /// моделей, дистрибутивы пакетов и каталог установки пакетов.
        ///
        /// Правило: НА ШАГ ВЫШЕ каталога запуска приложения. Программу ставят в свою папку
        /// (<c>C:\ai\AI2P</c>, <c>D:\AI2P</c>, <c>~/ai/AI2P</c>), и всё тяжёлое — веса
        /// моделей, распакованные ComfyUI и llama.cpp, скачанные архивы — должно лежать
        /// РЯДОМ с ней, а не внутри: обновление версии переписывает каталог программы, а
        /// десятки гигабайт весов переживают любую переустановку.
        ///
        /// Исключение — установка в каталог ПРОГРАММ системы (T-287): там рабочие файлы
        /// уезжают в каталог данных (<c>C:\ProgramData\AI2P</c>), а шаг вверх привёл бы
        /// в <c>C:\Program Files</c>, куда писать нельзя ничем, кроме прав администратора.
        /// В этом случае считаем от САМОГО рабочего каталога — там уже лежат data и logs,
        /// туда же лягут модели и пакеты.
        ///
        /// Абсолютный путь и путь от домашнего каталога («~/ai») этим правилом не трогаются
        /// вовсе — они и так полные (T-135).
        /// </summary>
        /// <param name="appDir">Каталог программы (<c>AppContext.BaseDirectory</c>).</param>
        /// <param name="homeDir">Рабочий каталог установки — где лежит config.json (T-287).</param>
        public static string MachineRoot(string appDir, string homeDir)
        {
            var app = Path.GetFullPath(appDir);
            var home = Path.GetFullPath(PathHome.Expand(homeDir));
            if (!AppHome.SamePath(app, home))
            {
                // рабочие файлы уехали от программы — считаем от них, вверх не идём
                return home;
            }
            return Path.GetDirectoryName(app.TrimEnd(Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar))
                   ?? app;   // программа в корне диска — шага вверх нет
        }

        /// <summary>
        /// Каталог этого компьютера из настроек (T-291): абсолютный путь и «~/…» берутся как
        /// есть, относительный считается от <paramref name="root"/>
        /// (<see cref="MachineRoot"/>), пустое значение — от <paramref name="fallback"/>.
        /// Пустой <paramref name="fallback"/> означает «каталог не задан» и возвращается
        /// пустой строкой: у дистрибутивов это «временный каталог», у пакетов —
        /// «&lt;репозиторий моделей&gt;/packages».
        /// </summary>
        public static string ResolveMachineDir(string root, string? value, string fallback = "")
        {
            var path = (value ?? "").Trim();
            if (path.Length == 0)
            {
                path = fallback.Trim();
                if (path.Length == 0)
                {
                    return "";
                }
            }
            return PathHome.IsRooted(path)
                ? Path.GetFullPath(PathHome.Expand(path))
                : Path.GetFullPath(Path.Combine(root, path));
        }
    }

    public sealed class LoggingSettings
    {
        /// <summary>Уровень вывода в лог (T-136): Debug / Info / Warning — правится
        /// в UI (Настройки → Основное). Умолчание — Warning («самое необходимое»):
        /// подробности нужны при разборе неполадок, а не каждый день.</summary>
        public string Level { get; set; } = "Warning";
        public string Dir { get; set; } = "./logs";
        public string Rotation { get; set; } = "day";
    }

    /// <summary>
    /// РАБОТА С АДАПТЕРАМИ LoRA (T-12-S1) — пока только пределы кадра датасета: до какого
    /// размера в пикселях и в килобайтах кадр ужимается и в каком формате хранится.
    ///
    /// Почему это НАСТРОЙКА, а не число в коде: правильного значения тут нет. Обучающие
    /// скрипты разных моделей ждут разного разрешения, а объём кадров упирается в диск и
    /// в время обучения — и то и другое у каждого своё. По заданию значения кладутся в
    /// настройки; когда обучение обрастёт своими требованиями, их подхватит справочник
    /// моделей (<c>lora.train.dataset.width/height</c>, T-13-S1), а это останется общим
    /// умолчанием на случай, когда в справочнике не сказано ничего.
    /// </summary>
    public sealed class LoraSettings
    {
        /// <summary>Наибольшая ширина кадра в пикселях.</summary>
        public int ImageMaxWidth { get; set; } = 1024;

        /// <summary>Наибольшая высота кадра в пикселях.</summary>
        public int ImageMaxHeight { get; set; } = 1024;

        /// <summary>Наибольший размер файла кадра в килобайтах.</summary>
        public int ImageMaxKb { get; set; } = 2048;

        /// <summary>Формат хранения кадра: «png» (умолчание) либо «jpeg».</summary>
        public string ImageFormat { get; set; } = "png";

        /// <summary>
        /// ЧЕМ ЗАПОЛНЯТЬ ПОЛЯ при автоматическом пережатии датасета (T-57-S0), «#RRGGBBAA».
        /// Картинку нельзя ни растянуть, ни обрезать — её вписывают в кадр целиком, и вокруг
        /// остаются поля. Умолчание «белый с полной прозрачностью»: у PNG поля выходят
        /// прозрачными, у JPEG (альфы нет вовсе) — белыми.
        /// </summary>
        public string ImagePadColor { get; set; } = "#FFFFFF00";
    }

    /// <summary>
    /// ПОЧТОВЫЙ СЕРВЕР ДЛЯ УВЕДОМЛЕНИЙ (T-272). Настройка уровня СЕРВЕРА, а не организации:
    /// ящик, из которого уходят письма, принадлежит компьютеру — как адрес, порт и каталоги.
    /// Правила уведомлений при этом принадлежат организации и реплицируются, а письмо
    /// отправляет тот сервер, на котором произошло событие, — своим почтовым сервером.
    ///
    /// Пароля здесь НЕТ: config.json лежит открытым текстом и переживает обновление
    /// (ConfigMerge). Пароль хранится в секретах по ссылке <see cref="PasswordRef"/>
    /// (подкаталог <c>secrets/</c>, ТЗ гл. 10) и вводится в форме настроек.
    /// </summary>
    /// <summary>
    /// ПЕР-СЕРВЕРНАЯ ЧАСТЬ ПЛАГИНА (T-111-S0) — всё, что у каждого компьютера своё.
    /// Ни одно из этих полей не попадает в базу организации и не реплицируется.
    /// </summary>
    public sealed class PluginSettings
    {
        /// <summary>Программа плагина найдена (или поставлена) НА ЭТОМ сервере.
        /// Это не то же самое, что состояние записи организации: там «инициализирован»
        /// значит «действия зарегистрированы», а здесь — «софт есть здесь».</summary>
        public bool Installed { get; set; }

        /// <summary>Путь к программе, указанный человеком руками: <c>C:\Shotcut\melt.exe</c>.
        /// Пусто — программа ищется сама (блок <c>software.system</c> манифеста).</summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// ПУТИ К ОСТАЛЬНЫМ ПРОГРАММАМ ПЛАГИНА (T-146-S0), «имя записи софта → путь»: у
        /// плагина, ведущего к нескольким программам, путь свой у каждой. Путь ГЛАВНОЙ
        /// (первой) программы по-прежнему лежит в <see cref="Path"/> — так config.json уже
        /// поставленных серверов продолжает работать без переноса значений.
        /// </summary>
        public Dictionary<string, string> Paths { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Когда программу проверяли в последний раз; <c>null</c> — ни разу.
        /// Проверка стоит запуска программы, поэтому её ответ живёт какое-то время.</summary>
        public DateTime? CheckedAt { get; set; }

        /// <summary>
        /// КЭШ СПИСКА ИНСТРУМЕНТОВ СЕРВЕРА MCP (T-119-S0) — имена, снятые с сервера при
        /// инициализации и при явном обновлении. Руками этот список не пишется: он ответ
        /// сервера. Лежит здесь, а не в базе организации, потому что подключение
        /// пер-серверное — программу транспорта <c>stdio</c> запускает тот компьютер, где
        /// она стоит, и у соседа версия сервера может быть другой. Реплицируется не список,
        /// а его следствие — записи справочника действий.
        /// </summary>
        public List<string> Tools { get; set; } = [];

        /// <summary>Когда список инструментов снимали в последний раз; <c>null</c> — ни разу.
        /// Обновление списка — ЯВНОЕ действие человека, само оно не происходит никогда.</summary>
        public DateTime? ToolsAt { get; set; }

        // ПОЛЯ ПОД ТОКЕН ЗДЕСЬ НЕТ И НЕ БУДЕТ: секрет подключения MCP живёт в
        // secrets/<ссылка>.json (PluginCodes.SecretRef), как пароль почты (T-272).
        // В config.json пароли не попадают никогда.
    }

    public sealed class MailSettings
    {
        /// <summary>Сервер SMTP; пусто (умолчание) — почта не настроена, уведомления не уходят.</summary>
        public string Host { get; set; } = "";

        /// <summary>Порт SMTP: 587 — STARTTLS (обычный случай), 465 — SSL, 25 — без шифрования.</summary>
        public int Port { get; set; } = 587;

        /// <summary>Шифровать соединение.</summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>Логин SMTP; пусто — сервер без авторизации (внутренний ретранслятор).</summary>
        public string User { get; set; } = "";

        /// <summary>Ссылка на пароль в хранилище секретов.</summary>
        public string PasswordRef { get; set; } = AI2P.Connectors.MailOptions.DefaultPasswordRef;

        /// <summary>Адрес отправителя; пусто — берётся <see cref="User"/>.</summary>
        public string From { get; set; } = "";

        /// <summary>Имя отправителя в письме.</summary>
        public string FromName { get; set; } = "AI2P";

        /// <summary>Настройки в том виде, в каком их читает транспорт уведомлений.</summary>
        public AI2P.Connectors.MailOptions ToOptions() => new(
            Host: Host,
            Port: Port,
            UseSsl: UseSsl,
            User: User,
            PasswordRef: PasswordRef.Trim().Length > 0
                ? PasswordRef.Trim()
                : AI2P.Connectors.MailOptions.DefaultPasswordRef,
            From: From,
            FromName: FromName);
    }

    /// <summary>
    /// ПАРОЛЬ АДМИНИСТРАТОРА СЕРВЕРА СОВПАДАЕТ С ПАРОЛЕМ ПОЛЬЗОВАТЕЛЯ (T-291).
    ///
    /// Паролей в системе два, и это сбивало с толку на первой же установке: пароль аккаунта
    /// (вход в организацию) и пароль администратора СЕРВЕРА — отдельный локальный вход,
    /// без которого не открывается форма своего сервера, каталоги и справочник моделей
    /// (ТЗ гл. 12). На одиночной установке это один и тот же человек, и держать ему два
    /// пароля незачем — визард первого старта предлагает флажок «пароль тот же»
    /// (по умолчанию отмечен), и тогда пароль администратора задаётся тем же значением,
    /// а вход администратором выдаётся сразу.
    ///
    /// Настройка живёт дальше первого старта: пока <see cref="SameAsUser"/> отмечено, смена
    /// пароля ЭТОГО пользователя (<see cref="UserEmail"/>) меняет и пароль администратора
    /// сервера — иначе они молча разъехались бы, и человек снова остался бы без доступа
    /// к настройкам своей машины. Снято — пароли независимы, как было до T-291.
    ///
    /// Почта здесь нужна именно потому, что аккаунтов на сервере может быть много:
    /// связан с администратором сервера ровно один — тот, кто эту связь и завёл.
    /// </summary>
    public sealed class ServerAdminSettings
    {
        /// <summary>Пароль администратора сервера совпадает с паролем пользователя
        /// <see cref="UserEmail"/>. Умолчание — да: так проще, а разделить пароли можно
        /// в любой момент, сняв флажок в визарде или задав свой пароль в форме сервера.</summary>
        public bool SameAsUser { get; set; } = true;

        /// <summary>Логин (почта) того самого пользователя; пусто — связь ещё не заведена
        /// (первый старт не проходили, либо флажок сняли).</summary>
        public string UserEmail { get; set; } = "";

        /// <summary>Пароль этого аккаунта тянет за собой пароль администратора сервера.</summary>
        public bool Follows(string? email) =>
            SameAsUser && UserEmail.Trim().Length > 0
            && UserEmail.Trim().Equals((email ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Путь к config.json: аргумент "--config &lt;путь&gt;" либо файл рядом с приложением.
    /// «~» в аргументе раскрывается (T-135): на Linux/macOS сервер живёт в <c>~/ai/AI2P</c>,
    /// и оболочка раскрывает «~» не всегда — например в кавычках или в юните systemd.</summary>
    public static string ResolvePath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config")
            {
                return Path.GetFullPath(AI2P.Core.PathHome.Expand(args[i + 1]));
            }
        }
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public static Ai2pConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(Loc.T("msg.ai2pConfig.1", path), path);
        }
        return JsonSerializer.Deserialize<Ai2pConfig>(File.ReadAllText(path), JsonOptions)
               ?? throw new InvalidDataException(Loc.T("msg.ai2pConfig.2", path));
    }

    /// <summary>
    /// КАТАЛОГИ ЭТОГО КОМПЬЮТЕРА ПОД ЕГО ПЛАТФОРМУ (T-164). Возвращает описания сделанных
    /// правок (пустой список — конфигурация в порядке, переписывать файл не нужно).
    ///
    /// Разбирается два случая:
    /// <list type="bullet">
    /// <item>репозиторий моделей пуст — ставится умолчание
    /// (<see cref="StorageSettings.DefaultModelsRepo"/>, с T-291 это <c>./models</c>);</item>
    /// <item>путь достался от ЧУЖОЙ платформы (<see cref="StorageSettings.IsForeignPath"/>) —
    /// репозиторий моделей заменяется умолчанием, каталоги дистрибутивов и пакетов
    /// очищаются (пусто у них и значит «умолчание»).</item>
    /// </list>
    ///
    /// Зачем это нужно: конфигурация переживает и первичную установку, и обновление
    /// (<see cref="ConfigMerge"/> кладёт значения пользователя ПОВЕРХ новых умолчаний),
    /// поэтому один раз попавший в файл <c>C:\ai</c> сам с Linux-установки уже не уйдёт.
    /// Каталоги, которые человек задал осознанно и которые платформе не противоречат,
    /// не трогаются — в том числе ОТНОСИТЕЛЬНЫЕ (T-291): чужими они не бывают.
    /// </summary>
    public IReadOnlyList<string> NormalizePlatformPaths() =>
        NormalizePlatformPaths(OperatingSystem.IsWindows());

    /// <summary>То же для ЗАДАННОЙ системы (см. <see cref="StorageSettings.SamplePath(bool)"/>).</summary>
    public IReadOnlyList<string> NormalizePlatformPaths(bool windows)
    {
        var changes = new List<string>();
        var repo = Storage.ModelsRepo.Trim();
        if (repo.Length == 0 || StorageSettings.IsForeignPath(repo, windows))
        {
            var fallback = StorageSettings.DefaultModelsRepo;
            if (repo != fallback)
            {
                changes.Add(repo.Length == 0
                    ? Loc.T("msg.ai2pConfig.3", fallback)
                    : Loc.T("msg.ai2pConfig.4", repo, fallback));
                Storage.ModelsRepo = fallback;
            }
        }
        foreach (var (label, value, set) in new (string, string, Action)[]
                 {
                     (Loc.T("msg.ai2pConfig.5"), Storage.DistDir, () => Storage.DistDir = ""),
                     (Loc.T("msg.ai2pConfig.6"), Storage.PackagesDir, () => Storage.PackagesDir = ""),
                 })
        {
            if (StorageSettings.IsForeignPath(value, windows))
            {
                changes.Add(Loc.T("msg.ai2pConfig.7", label, value.Trim()));
                set();
            }
        }
        return changes;
    }

    /// <summary>Записать настройки обратно в config.json (UTF-8 без BOM, гл. 13).</summary>
    public void Save(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Относительные пути конфига (dataDir, logs) считаются от каталога config.json.
    /// Путь с «~» (домашний каталог) раскрывается и считается полным (T-135): на Linux/macOS
    /// каталоги этого компьютера задают именно так — <c>~/ai</c>, <c>~/ai/AI2P/data</c>.
    ///
    /// Каталоги ЖЕЛЕЗА (репозиторий моделей, дистрибутивы, пакеты) считаются иначе —
    /// см. <see cref="ModelsRepoPath"/> и <see cref="StorageSettings.MachineRoot"/> (T-291).
    /// </summary>
    public string ResolveDir(string configPath, string dir) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!,
            AI2P.Core.PathHome.Expand(dir)));

    /// <summary>Репозиторий моделей полным путём (T-291): пусто в настройке — умолчание
    /// <c>./models</c>, относительный путь — от <paramref name="machineRoot"/>.</summary>
    public string ModelsRepoPath(string machineRoot) => StorageSettings.ResolveMachineDir(
        machineRoot, Storage.ModelsRepo, StorageSettings.DefaultModelsRepo);

    /// <summary>Каталог дистрибутивов пакетов полным путём; пусто — «временный каталог
    /// пользователя», и тогда возвращается пустая строка (прежнее поведение сохранено:
    /// у конфигураций, переживших обновление, поле пустое).</summary>
    public string DistDirPath(string machineRoot) =>
        StorageSettings.ResolveMachineDir(machineRoot, Storage.DistDir);

    /// <summary>Каталог установки пакетов полным путём; пусто — «&lt;репозиторий
    /// моделей&gt;/packages», и тогда возвращается пустая строка (её разбирает
    /// установщик пакетов — так это работало до T-291).</summary>
    public string PackagesDirPath(string machineRoot) =>
        StorageSettings.ResolveMachineDir(machineRoot, Storage.PackagesDir);
}
