using System.Text.Json;
using System.Text.Json.Serialization;
using AI2P.Core.Connectors;
using AI2P.Core.Entities;
using AI2P.Core.Events;

namespace AI2P.Core.Api;

/// <summary>
/// Контракты HTTP API (ТЗ гл. 3, принцип API-first): их используют и сервер, и UI,
/// и будущие плагины VS Code / Unity.
/// </summary>
public static class Ai2pJson
{
    /// <summary>camelCase-поля + snake_case-enum'ы, как в ТЗ (in_progress, waiting_human, …).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}

/// <summary>
/// Служебные заголовки HTTP API (ТЗ гл. 12, этап 39). UI (Blazor Server) вызывает собственное
/// API по петле своим HttpClient — cookie браузера в такой запрос не попадает, поэтому личность
/// вошедшего передаётся заголовками: одноразовый токен процесса (знает только код в этом же
/// процессе) + id аккаунта. Имена лежат в Core, чтобы сервер и клиент не разъезжались.
/// </summary>
public static class Ai2pHeaders
{
    public const string InternalToken = "X-AI2P-Internal";
    public const string InternalAccount = "X-AI2P-Account";

    /// <summary>Организация запроса (ТЗ п. 2.15, этап 40): у каждой организации своя БД и свои
    /// данные, поэтому запрос без организации выполнять не над чем. Браузер сообщает её
    /// сегментом пути (<c>/ai2p/&lt;код&gt;/api/…</c>), внутренний вызов UI и внешние клиенты —
    /// этим заголовком. Не задана нигде — берётся организация вошедшего по умолчанию.</summary>
    public const string Org = "X-AI2P-Org";

    /// <summary>Токен доступа сервер-сервер (ТЗ гл. 12, этап 41): им сервер кластера
    /// подтверждает, что он — тот, кому этот токен выдали при подключении. Токен привязан
    /// к записи сервера (его внутреннему ключу), а не к пользователю.</summary>
    public const string ServerToken = "X-AI2P-Server";

    /// <summary>
    /// Вошедший является ещё и admin'ом сервера (ТЗ гл. 12). Заголовок нужен по той же
    /// причине, что и <see cref="InternalAccount"/>: UI ходит в собственное API по петле
    /// своим HTTP-клиентом, и cookie браузера — в том числе ОТДЕЛЬНАЯ cookie администратора
    /// сервера — туда не попадает. Без этого признака настройки самого сервера оставались бы
    /// недоступными из UI даже после входа администратором. Значению верим на тех же
    /// условиях, что и аккаунту: правильный токен процесса и запрос с петлевого адреса.
    /// </summary>
    public const string ServerAdmin = "X-AI2P-Admin";

    /// <summary>
    /// СРЕДА ЗАПРОСА — АРХИВ (T-45-S0, выпуск 1.105): код (или id) архива, ДАННЫЕ КОТОРОГО
    /// надо читать вместо рабочей среды. У открытого архива своя база той же схемы и свой
    /// каталог файлов проектов, поэтому обработчикам ничего менять не пришлось: заголовок
    /// подменяет НАБОР СЕРВИСОВ запроса, а не сами обработчики.
    ///
    /// Пусто (заголовка нет) — рабочая среда, как раньше. То же значение приезжает
    /// параметром адреса <c>arc=&lt;код&gt;</c>: им помечены ссылки внутри архива
    /// (<see cref="ArchiveLinks"/>), и по ним файлы обязаны находиться в архиве, а не
    /// в рабочей среде.
    ///
    /// Архивная среда ТОЛЬКО ДЛЯ ЧТЕНИЯ: данные архива изменению не подлежат, поэтому
    /// запросы, меняющие данные, в ней отклоняются сервером (см. ApiEndpoints).
    /// </summary>
    public const string Archive = "X-AI2P-Archive";
}

/// <summary>
/// Имена схем аутентификации (ТЗ гл. 12). Лежат в Core, потому что нужны обеим сторонам:
/// сервер их регистрирует, а UI по ним узнаёт в личности вошедшего вход администратора
/// сервера — это отдельная вторая cookie, а не роль пользователя.
/// </summary>
public static class Ai2pSchemes
{
    /// <summary>Рабочий вход пользователя (аккаунт, п. 2.14).</summary>
    public const string User = "AI2P";

    /// <summary>Локальный вход admin'а сервера: им правятся настройки самого сервера.</summary>
    public const string ServerAdmin = "AI2P.ServerAdmin";
}

/// <summary>Текущее состояние UI: проект/команда по умолчанию (гл. 5) + вошедший пользователь (гл. 12).</summary>
public sealed class StateDto
{
    public string? CurrentProjectId { get; set; }
    public string? CurrentTeamId { get; set; }
    /// <summary>Исполнитель-человек вошедшего пользователя (актор всех его действий, гл. 12).</summary>
    public Executor LocalUser { get; set; } = new();
    /// <summary>Аккаунт вошедшего пользователя (ТЗ п. 2.14, этап 39).</summary>
    public Account Account { get; set; } = new();
    /// <summary>Роль вошедшего в системе (ТЗ п. 2.2) — по ней UI прячет недоступные действия.</summary>
    public SystemRole Role { get; set; } = SystemRole.Reader;
    /// <summary>Пользователь вошёл ещё и как admin сервера (отдельный локальный логин, гл. 12).</summary>
    public bool IsServerAdmin { get; set; }
    /// <summary>
    /// ВОШЕДШИЙ — ХОЗЯИН ЭТОГО КОМПЬЮТЕРА (T-201-S0): владелец системы (роль owner хотя бы
    /// в одной организации установки) либо локальный администратор сервера. По этому признаку
    /// показывается раздел «Организации» настроек: организации принадлежат УСТАНОВКЕ, а не
    /// открытой сейчас организации, и считать право по роли в ней значит терять раздел при
    /// переходе в организацию, приехавшую репликацией (там роль admin, T-186-S0).
    /// </summary>
    public bool CanManageOrgs { get; set; }
    public string Language { get; set; } = "ru";
    public string Version { get; set; } = "";
    /// <summary>Базовый URL для генерации ссылок на задачи: протокол + hostname + порт + basePath (гл. 11).</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Debug-режим работы (п. 2.9): в UI разрешает менять признак isCustom моделей.</summary>
    public bool Debug { get; set; }
    /// <summary>
    /// СЕРВЕР ЗАПУЩЕН КАК СЕРВИС ОС (T-271): <c>false</c> — обычный консольный запуск
    /// (умолчание приложения). Показывается в настройках рядом с версией: человеку,
    /// открывшему UI, иначе неоткуда узнать, чем поднят сервер, — а от этого зависит,
    /// как его останавливать и почему при старте не открылся браузер.
    /// </summary>
    public bool IsService { get; set; }
    /// <summary>
    /// Установка ПОМЕЧЕНА как сервис (<c>serviceMode</c> в config.json, T-271): записку
    /// ставит скрипт <c>makeAsServise.*</c>, и она переживает обновление версии. От
    /// <see cref="IsService"/> отличается тем, что говорит про УСТАНОВКУ, а не про этот
    /// запуск: службу можно остановить и запустить программу руками из консоли.
    /// </summary>
    public bool ServiceMode { get; set; }
    /// <summary>Вторая строка заголовка закладок «Задача»/«Проект» (ТЗ v1.18): name / code.</summary>
    public string TabTitleMode { get; set; } = "name";
    /// <summary>Ячейка календаря расписаний (ТЗ v1.37): код шаблона ("code") или заголовок ("name").</summary>
    public string ScheduleCellMode { get; set; } = "code";
    /// <summary>Сохранённый лейаут фреймов и закладок (JSON, todo22); null — лейаут по умолчанию.</summary>
    public string? UiLayout { get; set; }
    /// <summary>Сохранённые состояния представлений — вид/сортировка/фильтр списков (JSON, гл. 11);
    /// null — состояния по умолчанию.</summary>
    public string? UiComponentStates { get; set; }
    /// <summary>Базовый URL приложения БЕЗ организации (ТЗ гл. 11) — от него строятся адреса
    /// других организаций при переключении в тулбаре.</summary>
    public string AppBaseUrl { get; set; } = "";
    /// <summary>Тот же базовый URL, но по СОБСТВЕННОМУ адресу сервера — hostname/port (T-2-S1).
    /// Совпадает с <see cref="AppBaseUrl"/>, пока второй адрес (hostname2) не задан. Нужен,
    /// чтобы своя же ссылка, написанная по внутреннему адресу, узнавалась своей.</summary>
    public string AppOwnBaseUrl { get; set; } = "";
    /// <summary>Порт, который сервер слушает НА ЭТОМ компьютере (T-2-S1): от него строятся
    /// петлевые ссылки. У внешней ссылки порт бывает другой — проброшенный на роутере.</summary>
    public int LocalPort { get; set; }
    /// <summary>
    /// Адреса ОСТАЛЬНЫХ серверов организации (T-13-S0). Ссылку на созданный файл агент
    /// пишет от адреса своего сервера, а файла у нас нет — папка проекта не реплицируется.
    /// По этому списку показ Markdown узнаёт такую ссылку и сворачивает её в путь: файл
    /// принесёт от соседа наш сервер, у которого человек уже вошёл.
    /// </summary>
    public List<string> PeerBaseUrls { get; set; } = [];
    /// <summary>Организация запроса (ТЗ п. 2.15, этап 40): в ней лежат все данные ответа.</summary>
    public Organization Org { get; set; } = new();
    /// <summary>
    /// ОРГАНИЗАЦИЙ НА СЕРВЕРЕ ЕЩЁ НЕТ (T-148) — сервер подал заявку на подключение и ждёт
    /// решения. Интерфейс в этом состоянии открывается, но урезан: данных нет, доступны
    /// настройки и список серверов (в нём спрашивают решение по заявке и запускают
    /// репликацию вручную — дирижёр не в состоянии позвонить серверу за NAT сам).
    /// </summary>
    public bool NoOrg { get; set; }
    /// <summary>Организации, в которых вошедший — участник (переключатель в тулбаре, гл. 11).</summary>
    public List<Organization> Orgs { get; set; } = [];

    // --- кто мы в кластере (ТЗ гл. 6, этап 42) ---
    /// <summary>Внутренний ключ (unid) сервера, на котором открыт этот экран: по нему UI
    /// понимает, свою задачу он показывает или чужую (режим «только чтение»).</summary>
    public string ServerId { get; set; } = "";
    /// <summary>Код локального сервера в текущей организации: S0, S1, … Пусто — сервер
    /// с организацией не связан (одиночная установка): номера тогда без суффикса.</summary>
    public string ServerCode { get; set; } = "";
    public string ServerName { get; set; } = "";
    /// <summary>Локальный сервер — дирижёр текущей организации: он правит строки без сервера,
    /// ведёт шаблоны, справочники и иерархию задач между серверами (ТЗ гл. 6).</summary>
    public bool IsConductor { get; set; }
}

/// <summary>Создание/правка организации (вкладка «Организации» настроек, ТЗ п. 2.15).</summary>
public sealed class OrgSaveDto
{
    public string Name { get; set; } = "";
    /// <summary>Код в URL; пусто при создании — выводится из названия.</summary>
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Создание/правка аккаунта пользователя владельцем (вкладка «Пользователи», ТЗ п. 2.14).
/// Пароль передаётся только при создании (первичный) и при сбросе владельцем; пустая
/// строка означает «пароль не задан» — вход только локальный (гл. 12).
/// </summary>
public sealed class AccountSaveDto
{
    public string Name { get; set; } = "";
    /// <summary>Почта — уникальный идентификатор; при правке существующего не меняется.</summary>
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>Первичный пароль (при создании) либо сброс пароля владельцем; null — не менять.</summary>
    public string? Password { get; set; }
    /// <summary>Роль в системе привязанного исполнителя (ТЗ п. 2.2).</summary>
    public SystemRole Role { get; set; } = SystemRole.Editor;
}

/// <summary>Смена своего пароля (форма аккаунта, ТЗ п. 2.14): три поля формы.</summary>
public sealed class PasswordChangeDto
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

/// <summary>Правка своего аккаунта (форма по кнопке в правом верхнем углу): почту менять нельзя.</summary>
public sealed class AccountSelfDto
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
}

/// <summary>
/// Настройки САМОГО СЕРВЕРА — форма ЛОКАЛЬНОГО сервера в списке серверов (гл. 10, гл. 12;
/// этап 41). Правятся только admin'ом сервера и никуда не реплицируются: менять их может
/// лишь физический хозяин компьютера.
///
/// Пишутся они не в БД, а в <c>config.json</c>: от адреса и каталогов зависит сам запуск
/// приложения, поэтому исправить их нужно уметь и снаружи системы — например, когда порт
/// занят и сервер не поднимается (todo41 п. 3).
/// </summary>
public sealed class ServerSettingsDto
{
    public int Port { get; set; }
    public string Protocol { get; set; } = "http";
    /// <summary>Имя хоста или публичный IP: по нему сервер зовут снаружи и по нему же
    /// формируются ссылки на задачи (ТЗ гл. 11).</summary>
    public string Hostname { get; set; } = "localhost";
    /// <summary>ВТОРОЕ имя — под которым сервер видно снаружи (T-2-S1): публичное имя роутера
    /// с пробросом порта. Пусто — второго адреса нет, ссылки строятся по первой паре.</summary>
    public string Hostname2 { get; set; } = "";
    /// <summary>Порт второго адреса (T-2-S1); null — тот же, что и <see cref="Port"/>.
    /// Без <see cref="Hostname2"/> не используется и в форме не даётся заполнять.</summary>
    public int? Port2 { get; set; }
    /// <summary>Интерфейс, который слушает сервер: 0.0.0.0 — все (ТЗ гл. 6).</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    // --- СЕРТИФИКАТ ДЛЯ HTTPS (T-206) ---
    // Поля показываются в форме, только когда протокол «https»: у http-сервера сертификата
    // нет и говорить о нём незачем. Пустой пароль при сохранении означает «не менять» —
    // то же правило, что у пароля почты (T-272): значение наружу не отдаётся никогда.

    /// <summary>Откуда берётся сертификат: <c>file</c> — из файла, <c>store</c> — из
    /// хранилища сертификатов этого компьютера.</summary>
    public string HttpsSource { get; set; } = "file";

    /// <summary>Файл сертификата: <c>.pfx</c>/<c>.p12</c> либо PEM.</summary>
    public string HttpsCertFile { get; set; } = "";

    /// <summary>Файл закрытого ключа к PEM-сертификату; пусто — ключ в самом файле.</summary>
    public string HttpsKeyFile { get; set; } = "";

    /// <summary>Новый пароль файла <c>.pfx</c>; null или пусто — не менять.</summary>
    public string? HttpsCertPassword { get; set; }

    /// <summary>Пароль сертификата задан (только чтение).</summary>
    public bool HttpsCertPasswordSet { get; set; }

    /// <summary>Хранилище: <c>CurrentUser</c> либо <c>LocalMachine</c>.</summary>
    public string HttpsStoreLocation { get; set; } = "CurrentUser";

    /// <summary>Имя хранилища: <c>My</c> — личные сертификаты.</summary>
    public string HttpsStoreName { get; set; } = "My";

    /// <summary>Отбор в хранилище по имени (CN); пусто — по имени хоста сервера.</summary>
    public string HttpsSubject { get; set; } = "";

    /// <summary>Отбор в хранилище по отпечатку; задан — имя не смотрится.</summary>
    public string HttpsThumbprint { get; set; } = "";

    /// <summary>Доверять сертификату соседей по кластеру без проверки.</summary>
    public bool HttpsTrustAnyPeer { get; set; }

    /// <summary>Что получается с ТЕКУЩИМИ настройками (только чтение): описание найденного
    /// сертификата либо причина, по которой его нет. Пусто — протокол http.</summary>
    public string HttpsCertInfo { get; set; } = "";

    /// <summary>Сертификат по текущим настройкам читается (только чтение).</summary>
    public bool HttpsCertOk { get; set; }

    /// <summary>Чем ПРОЧИТАННЫЙ сертификат не устроит браузер (T-315, только чтение):
    /// он помечен как сертификат УЦ, либо имя сервера не названо в его SAN. Пусто —
    /// придраться не к чему либо сертификата нет вовсе.</summary>
    public string HttpsCertWarning { get; set; } = "";
    /// <summary>Префикс URL после порта (ТЗ гл. 11): /ai2p. Только для чтения.</summary>
    public string BasePath { get; set; } = "/ai2p";
    /// <summary>Репозиторий локальных моделей (ТЗ v1.40): каталог этого компьютера.
    /// Относительный путь считается от каталога рядом с установкой (T-291).</summary>
    public string ModelsRepo { get; set; } = "";
    /// <summary>Куда качать дистрибутивы пакетов (ТЗ v1.42).</summary>
    public string DistDir { get; set; } = "";
    /// <summary>Корневой каталог установки пакетов (ТЗ v1.42).</summary>
    public string PackagesDir { get; set; } = "";

    // ФАКТИЧЕСКИЕ ПУТИ (T-291, только чтение). Относительный путь сам по себе человеку
    // ничего не говорит — «./models» это где? Поэтому рядом с каждым полем формы
    // показывается то, во что оно разворачивается ПРЯМО СЕЙЧАС на этом компьютере.

    /// <summary>Репозиторий моделей полным путём.</summary>
    public string ModelsRepoPath { get; set; } = "";
    /// <summary>Каталог дистрибутивов полным путём; пусто — временный каталог пользователя.</summary>
    public string DistDirPath { get; set; } = "";
    /// <summary>Каталог установки пакетов полным путём; пусто — &lt;репозиторий&gt;/packages.</summary>
    public string PackagesDirPath { get; set; } = "";
    /// <summary>Пароль admin'а сервера задан (только чтение).</summary>
    public bool AdminPasswordSet { get; set; }
    /// <summary>Новый пароль admin'а сервера; null — не менять.</summary>
    public string? AdminPassword { get; set; }

    // --- только для чтения: запись о сервере в кластере (ТЗ гл. 6, этап 41) ---
    /// <summary>Внутренний ключ сервера (unid): им серверы узнают друг друга при репликации.</summary>
    public string ServerId { get; set; } = "";
    /// <summary>Код локального сервера в ТЕКУЩЕЙ организации: S0, S1, … (только чтение).</summary>
    public string Code { get; set; } = "";
    /// <summary>Локальный сервер — дирижёр текущей организации (ТЗ гл. 6).</summary>
    public bool IsConductor { get; set; }

    // --- репликация (ТЗ гл. 6, этап 43) ---
    // В отличие от адреса и каталогов, это НЕ настройка config.json: интервалы лежат
    // в записи сервера и реплицируются, потому что отсчёт ведут оба конца пары. Поэтому
    // и сохраняются они отдельным вызовом, и логин администратора сервера для них не нужен.

    /// <summary>Интервал автоматической репликации, с; null — только вручную.</summary>
    public int? ReplIntervalSec { get; set; }

    /// <summary>Интервал повтора после ошибки, с.</summary>
    public int? ReplRetrySec { get; set; }

    /// <summary>
    /// Серверу есть с кем реплицироваться: он рядовой участник хотя бы одной организации.
    /// Дирижёру своих интервалов не задают — реплицируются с ним, а не он (todo43), но
    /// признак дирижёра принадлежит ОРГАНИЗАЦИИ: в одной сервер дирижёр, в другой нет.
    /// </summary>
    public bool HasReplication { get; set; }
}

/// <summary>
/// СЕРТИФИКАТ, КОТОРЫЙ ПРОГРАММА ВЫПИСЫВАЕТ СЕБЕ САМА (T-315): запрос и ответ одним видом.
/// В запросе — имена сервера (пусто — берутся из настроек), в ответе — пути готовых файлов
/// и человеческое описание того, что получилось.
/// </summary>
public sealed class HttpsSelfCertDto
{
    /// <summary>Главное имя сервера; пусто — берётся из настроек сервера.</summary>
    public string Hostname { get; set; } = "";
    /// <summary>Второе (внешнее) имя сервера; пусто — второго адреса нет.</summary>
    public string Hostname2 { get; set; } = "";
    /// <summary>Файл серверного сертификата — путь для настроек (только чтение).</summary>
    public string CertFile { get; set; } = "";
    /// <summary>Файл закрытого ключа — путь для настроек (только чтение).</summary>
    public string KeyFile { get; set; } = "";
    /// <summary>Файл сертификата своего удостоверяющего центра — путь для настроек.</summary>
    public string CaFile { get; set; } = "";
    /// <summary>Он же полным путём: этот файл человек ставит в браузер.</summary>
    public string CaPath { get; set; } = "";
    /// <summary>Имена и адреса, на которые выписан сертификат (SAN), через запятую.</summary>
    public string Names { get; set; } = "";
    /// <summary>Что получилось — одной строкой для формы.</summary>
    public string Info { get; set; } = "";
}

/// <summary>Создание/правка удалённого сервера кластера (todo41 п. 5): протокол, имя хоста,
/// порт, активность. Код сервера в организации отсюда не меняется — его выдаёт дирижёр.</summary>
public sealed class ServerSaveDto
{
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "http";
    public string Hostname { get; set; } = "";
    public int Port { get; set; } = 5480;
    /// <summary>ВТОРОЙ (внешний) адрес сервера — по нему его видно снаружи его сети (T-50-S0).
    /// Обычно приезжает от самого сервера репликацией; здесь его можно задать руками, пока
    /// обмена ещё не было. Пусто — второго адреса нет.</summary>
    public string Hostname2 { get; set; } = "";
    /// <summary>Порт второго адреса (T-50-S0); null — тот же, что и <see cref="Port"/>.</summary>
    public int? Port2 { get; set; }
    public string BasePath { get; set; } = "/ai2p";
    public bool IsActive { get; set; } = true;
    /// <summary>Организации, в которых состоит сервер (ТЗ п. 2.15: связь многие-ко-многим).
    /// null — список не менять; иначе id организаций после правки формы.</summary>
    public List<string>? OrgIds { get; set; }
}

/// <summary>Список серверов организации из её формы (todo41 п. 11): та же связь
/// «многие ко многим» с другой стороны, плюс выбор дирижёра (он обязателен и ровно один).</summary>
public sealed class OrgServersSaveDto
{
    public List<string> ServerIds { get; set; } = [];
    /// <summary>Дирижёр организации; null — не менять.</summary>
    public string? ConductorServerId { get; set; }
}

/// <summary>Заявка на подключение сервера к организации, поданная ОТСЮДА (todo41 п. 7):
/// подключаются всегда к дирижёру, решение принимает человек на его стороне.</summary>
public sealed class ServerJoinDto
{
    /// <summary>Код организации на удалённом сервере.</summary>
    public string OrgCode { get; set; } = "";
    /// <summary>Комментарий к заявке — виден принимающему.</summary>
    public string Note { get; set; } = "";
}

/// <summary>
/// РЕШЕНИЕ ПО ВХОДЯЩЕЙ ЗАЯВКЕ (T-150): что именно делает «ПРИНЯТЬ». До T-150 решение было
/// одним словом в адресе, и заведение заявителя участником организации происходило молча —
/// а его-то как раз и забывали проверить. Теперь форма подтверждения показывает, есть ли
/// заявитель среди исполнителей организации, и предлагает завести его тут же.
/// </summary>
public sealed class ServerRequestDecisionDto
{
    /// <summary>Завести заявителя участником организации (исполнитель-человек с его аккаунтом).
    /// Снято — сервер подключается, а человек участником НЕ становится: у себя он останется
    /// в урезанном интерфейсе, пока исполнителя ему не заведут руками.</summary>
    public bool AddMember { get; set; } = true;
}

/// <summary>Результат обращения к другому серверу: связь и его ответ (todo41 п. 6).</summary>
public sealed class ServerCheckDto
{
    public bool Ok { get; set; }
    /// <summary>Внутренний ключ (unid) сервера, который ответил.</summary>
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Текст ошибки, если связи нет.</summary>
    public string Error { get; set; } = "";
    /// <summary>Ответивший сервер — тот же, что записан у нас (совпал unid).</summary>
    public bool SameServer { get; set; } = true;
}

/// <summary>Ответ «представься» другого сервера (протокол сервер-сервер, ТЗ гл. 6).
/// Ни организаций, ни аккаунтов здесь нет: адрес сервера может спросить кто угодно.</summary>
public sealed class ClusterHelloDto
{
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

/// <summary>Заявка на подключение, приходящая ОТ другого сервера (протокол сервер-сервер).</summary>
public sealed class ClusterJoinDto
{
    /// <summary>Внутренний ключ (unid) подключающегося сервера — им его и запомним.</summary>
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "http";
    public string Hostname { get; set; } = "";
    public int Port { get; set; } = 5480;
    public string BasePath { get; set; } = "/ai2p";
    /// <summary>Код организации, к которой просятся. ПУСТО допустимо (T-139): если на дирижёре
    /// организация одна, берётся она — код набирать незачем, а спросить его негде: заявка
    /// подаётся анонимно, и списка организаций дирижёр по сети не отдаёт.</summary>
    public string OrgCode { get; set; } = "";
    /// <summary>Кто подал заявку — имя и почта одной строкой, для показа принимающему.</summary>
    public string RequestedBy { get; set; } = "";
    public string Note { get; set; } = "";

    // --- ЗАЯВИТЕЛЬ (T-139, доработка): заявка подаётся АНОНИМНО — реквизитов дирижёра
    // у подающего нет и быть не должно. Поэтому заявка называет человека сама: при
    // подтверждении дирижёр заводит его участником организации со ССЫЛКОЙ НА ЭТОТ аккаунт,
    // а сам аккаунт (вместе с хэшем пароля) приезжает репликацией с подключаемого сервера.
    // Так у человека в кластере остаётся ОДИН аккаунт и один пароль — выравнивать нечего.

    /// <summary>Внутренний ключ аккаунта заявителя на подключаемом сервере.</summary>
    public string ApplicantId { get; set; } = "";
    /// <summary>Имя заявителя (как он назвался).</summary>
    public string ApplicantName { get; set; } = "";
    /// <summary>Почта заявителя — она же логин его аккаунта; по ней дирижёр видит, кто просится.</summary>
    public string ApplicantEmail { get; set; } = "";
    /// <summary>
    /// ХЭШ ПАРОЛЯ аккаунта заявителя (открытого пароля система не хранит нигде). С ним
    /// дирижёр заводит того же человека С ТЕМ ЖЕ ПАРОЛЕМ — поэтому «выравнивать пароли»
    /// не нужно: аккаунт у человека в кластере один, и заводить его второй раз руками вредно.
    /// </summary>
    public string ApplicantPasswordHash { get; set; } = "";

    /// <summary>
    /// ПАРОЛЬ ЗАЯВИТЕЛЯ НА ДИРИЖЁРЕ (T-150) — открытым текстом и только в одном случае:
    /// дирижёр ответил «человек с этой почтой у меня уже есть» и попросил доказать, что
    /// это тот же человек (<see cref="ClusterJoinStatusDto.PasswordAsk"/>). По сути это
    /// вход на дирижёр: пароль там задан, и человек его знает. Дирижёр только проверяет
    /// его своим хэшем и никуда не сохраняет; не знает пароль — ключ чужого аккаунта
    /// не выдаётся, и подключиться под чужой почтой нельзя.
    ///
    /// Во всех остальных заявках поле ПУСТОЕ: заявка по-прежнему анонимна (T-139).
    /// </summary>
    public string ApplicantPassword { get; set; } = "";
}

/// <summary>Состояние заявки на подключение (её опрашивает подавший сервер, todo41 п. 7).</summary>
public sealed class ClusterJoinStatusDto
{
    /// <summary>Ссылка на заявку — по ней спрашивают её состояние.</summary>
    public string RequestId { get; set; } = "";
    /// <summary>pending / deferred / active / rejected (<c>OrgServerStatus</c>).</summary>
    public string Status { get; set; } = "";
    /// <summary>Код, выданный дирижёром при подтверждении: S1, S2, … (пусто — ещё не выдан).</summary>
    public string Code { get; set; } = "";
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    /// <summary>Внутренний ключ (unid) дирижёра — им мы его и запомним.</summary>
    public string ServerId { get; set; } = "";
    /// <summary>Токен доступа к дирижёру; выдаётся только вместе с подтверждением.</summary>
    public string Token { get; set; } = "";

    // --- всё, что нужно для ЗАВЕДЕНИЯ организации у себя (первичная репликация, этап 43) ---
    // Организация должна получить у подключающегося сервера ТЕ ЖЕ идентификаторы, что
    // у дирижёра: иначе после первой же репликации в базе оказались бы две организации,
    // а связи «организация ↔ сервер» задвоились бы по UNIQUE(org_id, server_id).

    /// <summary>Идентификатор организации у дирижёра — заводим её у себя с ним же.</summary>
    public string OrgId { get; set; } = "";
    /// <summary>Внешний код организации (<c>ORG-1</c>) — он же имя её подкаталога.</summary>
    public string OrgDisplayId { get; set; } = "";
    /// <summary>Идентификатор связи «организация ↔ дирижёр» у дирижёра.</summary>
    public string ConductorLinkId { get; set; } = "";
    /// <summary>Код дирижёра в организации: обычно <c>S0</c>.</summary>
    public string ConductorCode { get; set; } = "";

    /// <summary>
    /// КЛЮЧ ОРГАНИЗАЦИИ в base64 (ТЗ гл. 10, разд. 8 плана; этап 45) — им расшифровываются
    /// ключи API моделей. Выдаётся ОДИН РАЗ и только вместе с подтверждением заявки, то есть
    /// после решения человека на дирижёре: сам ключ не реплицируется никогда, реплицируется
    /// лишь зашифрованный им результат. Пусто — заявка ещё не подтверждена.
    /// </summary>
    public string OrgKey { get; set; } = "";

    /// <summary>
    /// ПЕРЕЧЕНЬ ОРГАНИЗАЦИЙ ДИРИЖЁРА (T-141) — заполняется, только когда организацию выбрать
    /// не удалось: код в заявке не назван, а организаций у дирижёра несколько
    /// (<see cref="ChooseOrg"/>). Тогда заявка не заводится вовсе, а подающая сторона
    /// показывает человеку список и даёт отметить одну или НЕСКОЛЬКО: сервер может работать
    /// сразу в нескольких организациях (ТЗ п. 2.15), и заявка подаётся в каждую отмеченную.
    ///
    /// Списком дирижёр отвечает только в этом случае: спрашивать «покажи все свои организации»
    /// у него нельзя — состав организаций не должен быть виден каждому, кто дотянулся
    /// по сети (T-139).
    /// </summary>
    public List<ClusterOrgBriefDto> Orgs { get; set; } = [];

    /// <summary>Значение <see cref="Status"/> «выберите организацию»: заявка НЕ подана,
    /// в <see cref="Orgs"/> лежит перечень организаций дирижёра (T-141).</summary>
    public const string ChooseOrg = "choose";

    /// <summary>
    /// Значение <see cref="Status"/> «человек с этой почтой у меня уже есть» (T-148): заявка
    /// НЕ подана, а в <see cref="ApplicantId"/> лежит внутренний ключ, под которым дирижёр
    /// знает этого человека. Ответ на ПОВТОРНОЕ подключение: сервер переставили заново, его
    /// аккаунт получил другой ключ, а на дирижёре человек остался с прежним — почта же у него
    /// та же, а почта в системе и есть логин. Подключающийся сервер берёт ключ дирижёра себе
    /// (<c>AccountService.Rekey</c>) и подаёт заявку заново — уже от того же человека.
    ///
    /// КЛЮЧ ВЫДАЁТСЯ НЕ СРАЗУ (T-150): сначала дирижёр просит пароль этого человека
    /// (<see cref="PasswordCheck"/>), и только доказавший получает ключ. Иначе назваться
    /// чужой почтой мог бы кто угодно, кто дотянулся до дирижёра по сети.
    /// </summary>
    public const string SameEmail = "same-email";

    /// <summary>Внутренний ключ человека с этой почтой У ДИРИЖЁРА (T-148); заполняется
    /// только вместе со статусом <see cref="SameEmail"/> и только ПОСЛЕ проверки пароля
    /// (T-150) — до неё поле пустое.</summary>
    public string ApplicantId { get; set; } = "";

    /// <summary>Имя, под которым дирижёр знает этого человека (T-148): показывается
    /// подключающемуся, чтобы он понял, кого именно узнал дирижёр.</summary>
    public string ApplicantName { get; set; } = "";

    /// <summary>
    /// ЧТО ДИРИЖЁР ОТВЕТИЛ ПРО ПАРОЛЬ (T-150) — только при статусе <see cref="SameEmail"/>:
    /// <see cref="PasswordAsk"/> — пароль нужен, а прислан не был; <see cref="PasswordWrong"/> —
    /// прислан и не подошёл; <see cref="PasswordUnset"/> — у этого человека пароля на дирижёре
    /// нет вовсе, и доказать личность нечем. Пусто — пароль проверен (или не требовался),
    /// в <see cref="ApplicantId"/> лежит ключ.
    /// </summary>
    public string PasswordCheck { get; set; } = "";

    /// <summary>Дирижёр ждёт пароль этого человека — заявка не подана (T-150).</summary>
    public const string PasswordAsk = "ask";

    /// <summary>Присланный пароль не подошёл: это либо не тот человек, либо он его забыл.</summary>
    public const string PasswordWrong = "wrong";

    /// <summary>У человека на дирижёре пароль не задан (вход только с его же компьютера,
    /// гл. 11): доказать личность нечем, и ключ не выдаётся. Лечится заданием пароля
    /// на дирижёре.</summary>
    public const string PasswordUnset = "unset";
}

/// <summary>
/// ОРГАНИЗАЦИИ, В КОТОРЫЕ НАС ВКЛЮЧИЛИ (T-312) — ответ дирижёра на вопрос «где я у тебя
/// значусь». Спрашивает рядовой сервер ПО СВОЕМУ ТОКЕНУ, и перечислены здесь только те
/// организации, где связь именно с ним активна: «покажи все свои организации» у дирижёра
/// по-прежнему спросить нельзя (T-139).
///
/// Каждая запись — та же сводка, что уезжает подавшему заявку в ответ на её подтверждение
/// (<see cref="ClusterJoinStatusDto"/>): по ней организация заводится у себя теми же
/// идентификаторами, что у дирижёра.
/// </summary>
public sealed class ClusterOrgListDto
{
    public List<ClusterJoinStatusDto> Orgs { get; set; } = [];
}

/// <summary>Организация дирижёра в ответе «выберите организацию» (T-141): код и название —
/// больше подающей стороне ничего не нужно, чтобы человек мог выбрать.</summary>
public sealed class ClusterOrgBriefDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Токен, которым партнёр будет ходить К НАМ (обратное направление обмена, гл. 12):
/// без него дирижёр не смог бы сам начать сеанс репликации с подключившимся сервером.</summary>
public sealed class ClusterPeerTokenDto
{
    public string Token { get; set; } = "";
}

// Логина на удалённый сервер (ClusterLoginDto) больше нет (T-139, доработка): ради списка
// организаций дирижёра подающий сервер спрашивал у человека ПОЧТУ И ПАРОЛЬ аккаунта НА ЧУЖОМ
// сервере — то есть требовал передать реквизиты постороннему серверу, да ещё и по открытому
// HTTP. Заявка подаётся анонимно, а организацию называют кодом (или она на дирижёре одна).

// --- протокол репликации (ТЗ гл. 6, п. 6.1; этап 43) ---

/// <summary>Начало сеанса репликации. Сеанс идёт по ОДНОЙ организации целиком; вести его
/// может любая из сторон, и вторая попытка на то же время просто отклоняется (todo43).</summary>
public sealed class ReplBeginDto
{
    public string OrgCode { get; set; } = "";
}

/// <summary>Ответ на начало сеанса: наш взгляд на пару и то, докуда мы дочитали партнёра.</summary>
public sealed class ReplSessionDto
{
    /// <summary>Внутренний ключ (unid) отвечающего сервера.</summary>
    public string ServerId { get; set; } = "";
    /// <summary>Идентификатор организации на той стороне.</summary>
    public string OrgId { get; set; } = "";
    /// <summary>Сеанс уже идёт — второй запуск игнорируется (todo43).</summary>
    public bool Busy { get; set; }
    /// <summary>Голова журнала организации у партнёра: знаменатель прогресса.</summary>
    public long OrgHead { get; set; }
    /// <summary>Голова журнала серверной БД у партнёра.</summary>
    public long ServerHead { get; set; }
}

/// <summary>Пачка изменений строк с указанием, сколько ещё осталось (для прогресс-бара).</summary>
public sealed class ReplChangesDto
{
    public List<RowChange> Items { get; set; } = [];
    /// <summary>Курсор после этой пачки: с ним приходят за следующей.</summary>
    public long NextCursor { get; set; }
    /// <summary>Сколько изменений осталось ПОСЛЕ этой пачки.</summary>
    public long Remaining { get; set; }
}

/// <summary>Пачка изменений, отдаваемая партнёру (обратное направление того же сеанса).</summary>
public sealed class ReplApplyDto
{
    public string OrgCode { get; set; } = "";
    /// <summary>org — база организации, srv — серверная БД (аккаунты и топология).</summary>
    public string Scope { get; set; } = ReplScopes.Org;
    public List<RowChange> Items { get; set; } = [];
    /// <summary>Наш курсор, до которого отправлена эта пачка: партнёр его запоминает,
    /// чтобы после смены инициатора не перечитывать журнал заново.</summary>
    public long SourceCursor { get; set; }
}

/// <summary>Итог применения пачки (ТЗ гл. 6): попадает на экран диагностики.</summary>
public sealed class ReplApplyResultDto
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Conflicts { get; set; }
    public string LastConflict { get; set; } = "";

    /// <summary>Строк, отвергнутых базой по ограничению целостности (T-160): они отложены
    /// на той стороне в очередь повтора, а не потеряны — пачка из-за них не отклоняется.</summary>
    public int Failed { get; set; }

    /// <summary>Последний отказ словами: таблица, ключ строки и причина.</summary>
    public string LastFailed { get; set; } = "";
}

/// <summary>Что именно реплицируется в этом обмене (ТЗ п. 6.4.1: баз две).</summary>
public static class ReplScopes
{
    /// <summary>База организации: проекты, задачи, справочники, журнал работ.</summary>
    public const string Org = "org";

    /// <summary>Серверная БД в части этой организации: аккаунты участников, сама
    /// организация и её серверы. Настройки серверов и токены не реплицируются никогда.</summary>
    public const string Server = "srv";

    /// <summary>ФАЙЛЫ каталога данных организации (правка v1.51, этап 44): описания задач,
    /// критерии, артефакты, профайлы. Без корзины, журналов запросов к ИИ и самой БД.</summary>
    public const string OrgFiles = "orgdata";

    /// <summary>ФАЙЛЫ папки <c>Common</c> проекта (правка v1.51, этап 44): путь внутри
    /// каталога проекта, свой на каждом сервере; пусто — не реплицируется.</summary>
    public const string CommonFiles = "common";

    /// <summary>
    /// БАЗА АРХИВА (T-47-S0, выпуск 1.105). У каждого архива своя база ТОЙ ЖЕ схемы, что
    /// у организации, значит и свой журнал изменений: реплицируется он тем же механизмом,
    /// но со своим курсором. Архив называется в самом виде обмена — <c>arc:&lt;id архива&gt;</c>
    /// (<see cref="ArchiveScope"/>), поэтому ни одного нового вызова протокола не нужно.
    /// </summary>
    public const string Archive = "arc";

    /// <summary>ФАЙЛЫ архива (T-47-S0): подкаталог <c>projects</c> внутри каталога архива.
    /// Ключом переноса файлов служит идентификатор архива.</summary>
    public const string ArchiveFiles = "arcdata";

    /// <summary>Вид обмена для базы конкретного архива: <c>arc:&lt;id&gt;</c>.</summary>
    public static string ArchiveScope(string archiveId) => Archive + ":" + archiveId;

    /// <summary>Идентификатор архива из вида обмена; пусто — это не архивный вид.</summary>
    public static string ArchiveOf(string? scope) =>
        scope is not null && scope.StartsWith(Archive + ":", StringComparison.Ordinal)
            ? scope[(Archive.Length + 1)..]
            : "";
}

/// <summary>
/// Запись манифеста реплицируемого каталога (ТЗ гл. 6, п. 7 плана; этап 44): чем сервер
/// описывает свой файл партнёру. Сравниваются по хэшу — время изменения на разных машинах
/// живёт своей жизнью, а размер совпадает слишком часто.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Путь относительно корня реплицируемого каталога, прямые слэши.</summary>
    public string Path { get; set; } = "";

    public long Size { get; set; }

    /// <summary>Время изменения, UTC ISO 8601. После переноса выставляется у копии таким же —
    /// иначе следующий сеанс счёл бы файл изменившимся на обеих сторонах.</summary>
    public string Mtime { get; set; } = "";

    /// <summary>sha256 в hex; пусто — файл не удалось прочитать.</summary>
    public string Hash { get; set; } = "";
}

/// <summary>Сколько байт файла партнёр уже принял — точка докачки после обрыва (этап 44).</summary>
public sealed class ReplFilePartDto
{
    public long Offset { get; set; }
}

/// <summary>
/// ГОТОВЫЙ К ПЕРЕДАЧЕ .ZIP АРХИВА (T-47-S0): ответ сервера-источника на «собери мне архив».
/// Архив ВСЕГДА уезжает закрытым, даже если у источника он открыт: там он для этого
/// упаковывается во временный файл, который снимается по окончании передачи.
/// </summary>
public sealed class ReplArcPackDto
{
    /// <summary>Размер собранного .zip в байтах.</summary>
    public long Size { get; set; }

    /// <summary>Код архива — им называется файл у получателя.</summary>
    public string Code { get; set; } = "";

    /// <summary>Архива у источника нет вовсе (его там удалили, пока мы собирались).</summary>
    public bool Missing { get; set; }
}

/// <summary>Просьба скачать архив с другого сервера организации (T-47-S0): кнопка стоит
/// у архива, удалённого с ЭТОГО сервера, источник выбирает человек.</summary>
public sealed class ArchiveDownloadDto
{
    public string ServerId { get; set; } = "";
}

/// <summary>Манифест каталога партнёра (ответ на «покажи, что у тебя есть»).</summary>
public sealed class ReplFileManifestDto
{
    public List<FileEntry> Items { get; set; } = [];

    /// <summary>Каталог на той стороне не настроен (папка <c>Common</c> не заполнена) —
    /// репликация этого каталога не запускается (todo44).</summary>
    public bool NotConfigured { get; set; }
}

/// <summary>
/// Конфликт файла (todo44): файл за интервал репликации изменился НА ДВУХ серверах.
/// Такой файл помечается конфликтным и не реплицируется, пока человек не выберет, что делать.
/// Левая сторона — всегда дирижёр, правая — рядовой сервер: так пара выглядит одинаково,
/// на каком бы из двух серверов её ни открыли.
/// </summary>
public sealed class ReplFileConflictDto
{
    public string Id { get; set; } = "";
    public string OrgId { get; set; } = "";
    public string OrgCode { get; set; } = "";
    /// <summary>Партнёр, с которым обнаружено расхождение.</summary>
    public string ServerId { get; set; } = "";
    public string ServerCode { get; set; } = "";
    /// <summary>orgdata — каталог данных организации, common — папка Common проекта,
    /// key — КЛЮЧ API организации (правка v1.52, этап 45).</summary>
    public string Scope { get; set; } = "";
    /// <summary>Проект (для папки <c>Common</c>); пусто — каталог данных организации.</summary>
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    /// <summary>Путь внутри реплицируемого каталога.</summary>
    public string Path { get; set; } = "";

    // --- две версии файла: левая — дирижёра, правая — рядового сервера ---
    public long LeftSize { get; set; }
    public string LeftMtime { get; set; } = "";
    /// <summary>sha256 версии дирижёра; пусто — файла на той стороне нет (его удалили).</summary>
    public string LeftHash { get; set; } = "";
    public long RightSize { get; set; }
    public string RightMtime { get; set; } = "";
    public string RightHash { get; set; } = "";

    /// <summary>Значения сторон у конфликта КЛЮЧА API — ЗАШИФРОВАННЫЕ (этап 45): их не
    /// показывают, ими применяют решение «принять сторону». У файлов пусто.</summary>
    public string LeftValue { get; set; } = "";

    public string RightValue { get; set; } = "";

    /// <summary>Выбранное решение (<see cref="ReplFileResolutions"/>); пусто — не выбрано.
    /// Применяется при СЛЕДУЮЩЕЙ репликации и только на один сеанс (todo44).</summary>
    public string Resolution { get; set; } = "";

    public DateTime DetectedAt { get; set; }
}

/// <summary>Что делать с конфликтным файлом (todo44). Применяется один раз, на ближайший сеанс.</summary>
public static class ReplFileResolutions
{
    /// <summary>Принять левый — версию дирижёра; правая перезаписывается.</summary>
    public const string Left = "left";

    /// <summary>Принять правый — версию рядового сервера; левая перезаписывается.</summary>
    public const string Right = "right";

    /// <summary>Переименовать левый: версия дирижёра получает суффикс своего кода сервера,
    /// после чего обе версии живут рядом и обе реплицируются.</summary>
    public const string RenameLeft = "rename_left";

    /// <summary>Переименовать правый: суффикс получает версия рядового сервера.</summary>
    public const string RenameRight = "rename_right";

    /// <summary>Ввести НОВЫЙ ключ (только для конфликта ключа API, todo45): обе спорные
    /// версии отбрасываются, вместо них записывается введённое человеком значение.
    /// У файлов такой операции нет — файл не наберёшь руками.</summary>
    public const string NewValue = "new";

    public static bool IsKnown(string value) =>
        value is Left or Right or RenameLeft or RenameRight or NewValue;

    /// <summary>Операции, применимые к конфликту КЛЮЧА API: переименования тут нет,
    /// зато есть «ввести новый ключ» (todo45).</summary>
    public static bool IsKeyResolution(string value) => value is Left or Right or NewValue;
}

/// <summary>Решение по конфликту файла или ключа API (todo44, todo45).</summary>
public sealed class ReplFileResolveDto
{
    public string Resolution { get; set; } = "";

    /// <summary>Новое значение ключа API — только для <c>new</c> (todo45). В журнал и логи
    /// не попадает, в базе сохраняется зашифрованным ключом организации.</summary>
    public string Value { get; set; } = "";
}

/// <summary>Содержимое <c>.repignore</c> папки <c>Common</c> проекта (todo44): правится
/// кнопкой-фильтром рядом с полем <c>Common</c> в форме проекта.</summary>
public sealed class RepIgnoreDto
{
    public string Text { get; set; } = "";

    /// <summary>Папка <c>Common</c> на этом сервере не настроена — править нечего.</summary>
    public bool NotConfigured { get; set; }

    /// <summary>Полный путь до файла — показывается в подсказке редактора.</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// Состояние репликации сервера для списка серверов и экрана диагностики (ТЗ гл. 6, этап 43).
/// Одна строка на пару «организация — сервер»; у дирижёра видны все, у рядового сервера —
/// только своя.
/// </summary>
public sealed class ReplicationStatusDto
{
    public string OrgId { get; set; } = "";
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    /// <summary>Сервер, с которым идёт обмен (для рядового сервера это всегда дирижёр).</summary>
    public string ServerId { get; set; } = "";
    public string ServerCode { get; set; } = "";
    public string ServerName { get; set; } = "";
    /// <summary>Интервал автоматической репликации, с; null — только вручную.</summary>
    public int? IntervalSec { get; set; }
    public int? RetrySec { get; set; }
    /// <summary>idle / running / error (<c>ReplicationStatuses</c>).</summary>
    public string Status { get; set; } = "idle";
    /// <summary>Секунд до следующего автоматического запуска; null — «ручная» либо идёт сеанс
    /// (во время репликации отсчёт останавливается, todo43).</summary>
    public int? SecondsLeft { get; set; }
    /// <summary>Процент выполнения текущего сеанса (0–100).</summary>
    public int Percent { get; set; }
    /// <summary>Что делается прямо сейчас — попадает в подсказку.</summary>
    public string Phase { get; set; } = "";
    public string LastError { get; set; } = "";
    public DateTime? LastOkAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int Conflicts { get; set; }
    public string LastConflict { get; set; } = "";
    public int Received { get; set; }
    public int Sent { get; set; }
    // --- файлы за последний сеанс (этап 44) ---
    public int FilesReceived { get; set; }
    public int FilesSent { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    /// <summary>Сколько файлов ждут решения человека (кнопка с числом в списке серверов, todo44).</summary>
    public int FileConflicts { get; set; }
    /// <summary>Строк в очереди повтора: база их не приняла (T-160). Не ноль — часть данных
    /// ещё не доехала, но репликация идёт дальше и пробует их каждый сеанс.</summary>
    public int Pending { get; set; }
    /// <summary>Последний отказ базы словами: таблица, ключ строки и причина.</summary>
    public string LastPending { get; set; } = "";
    /// <summary>ВСЯ очередь повтора построчно (T-20-S1): одной «последней причины» человеку
    /// мало — по ней не понять, три это разные строки или одна и та же, и что именно
    /// не доехало. Пусто, когда очередь пуста.</summary>
    public List<ReplPendingRowDto> PendingRows { get; set; } = [];
    /// <summary>Почему сеанс с этим партнёром начинаем не мы, а он (T-20-S1): его адрес
    /// петлевой (сервер за NAT) либо встречный токен ещё не получен. Пусто — звонить можно.
    /// Без этой строки «Последняя попытка» на экране диагностики выглядит бессмыслицей:
    /// время показано, а попыток мы не делаем вовсе.</summary>
    public string CannotCall { get; set; } = "";
    // --- курсоры и отставание: собственно экран диагностики ---
    public long PullCursor { get; set; }
    public long PushCursor { get; set; }
    public long ServerPullCursor { get; set; }
    public long ServerPushCursor { get; set; }
    /// <summary>Сколько наших изменений партнёр ещё не забрал (отставание).</summary>
    public long OutgoingLag { get; set; }
}

/// <summary>Строка очереди повтора репликации (экран диагностики, T-20-S1): какую именно
/// строку какой базы партнёр прислал, почему она не применилась и сколько раз пробовали.</summary>
public sealed class ReplPendingRowDto
{
    /// <summary>Какая база: <c>org</c> — организации, <c>server</c> — серверная (ReplScopes).</summary>
    public string Scope { get; set; } = "";
    public string Table { get; set; } = "";
    public string Pk { get; set; } = "";
    /// <summary>Отказ базы словами: «FOREIGN KEY constraint failed», «UNIQUE …».</summary>
    public string Reason { get; set; } = "";
    public int Tries { get; set; }
}

/// <summary>Интервалы репликации сервера (форма сервера, todo43).</summary>
public sealed class ReplSettingsDto
{
    /// <summary>Интервал автоматической репликации, с; пусто — только вручную. Минимум 30.</summary>
    public int? IntervalSec { get; set; }
    /// <summary>Интервал повтора после ошибки, с. Минимум 15.</summary>
    public int? RetrySec { get; set; }
}

/// <summary>Сохранение лейаута фреймов пользователя (todo22): JSON как есть.</summary>
public sealed class UiLayoutDto
{
    public string? Json { get; set; }
}

/// <summary>Просроченное срабатывание расписания (ТЗ п. 2.12): пока приложение не работало;
/// человек запускает либо удаляет из списка.</summary>
public sealed class ScheduleOverdueDto
{
    public string ScheduleId { get; set; } = "";
    public string ScheduleDisplayId { get; set; } = "";
    public string TemplateTaskId { get; set; } = "";
    /// <summary>Код и заголовок шаблона — подставляет API (для показа в списке).</summary>
    public string TemplateDisplayId { get; set; } = "";
    public string TemplateTitle { get; set; } = "";
    public int MissedCount { get; set; }
    public DateTime LastMissedAt { get; set; }
}

/// <summary>Срабатывание расписания в интервале (календарь расписаний, v1.36).</summary>
public sealed class ScheduleOccurrenceDto
{
    public string ScheduleId { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>Действие справочника действий (ТЗ v1.21, todo24) — тексты в языке запроса.</summary>
public sealed class ActionDto
{
    public string Id { get; set; } = "";
    /// <summary>Внешний иерархический код (как классы через точку): AI2P.Files.Read …</summary>
    public string Code { get; set; } = "";
    /// <summary>Имя инструмента function calling (read_file …); null — не публикуется агенту.</summary>
    public string? ToolName { get; set; }
    /// <summary>false — встроенное (редактируется только промпт), true — кастомное.</summary>
    public bool IsCustom { get; set; }
    /// <summary>Заголовок (description).</summary>
    public string Title { get; set; } = "";
    /// <summary>Описание-подсказка для человека.</summary>
    public string Hint { get; set; } = "";
    /// <summary>Действующий промпт для агента (кастомная правка либо значение по умолчанию).</summary>
    public string Prompt { get; set; } = "";
    /// <summary>Промпт по умолчанию (для кнопки «откатить», у встроенных).</summary>
    public string DefaultPrompt { get; set; } = "";
    /// <summary>Промпт исправлен пользователем (показать кнопку «откатить по умолчанию»).</summary>
    public bool PromptModified { get; set; }
}

/// <summary>Создание/изменение действия справочника (ТЗ v1.21, todo24).</summary>
public sealed class ActionSaveDto
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Prompt { get; set; } = "";
}

/// <summary>
/// ДИАГРАММА ПОДЗАДАЧ (T-132-S0) — третье представление вкладки «Подзадачи» карточки задачи,
/// сделанное по образцу диаграммы BPMN: по горизонтали время, по вертикали дорожки
/// исполнителей, задачи — прямоугольники.
///
/// Всё считается на сервере одним вызовом: клиенту нужны и задания (реально затраченное
/// время лежит в jobs), и команда задачи (порядок дорожек — иерархия команды), а спрашивать
/// это по задаче отдельно значило бы десятки запросов на каждую перерисовку.
/// </summary>
public sealed class TaskDiagramDto
{
    /// <summary>Дорожки — исполнители, у которых в поддереве есть хотя бы одна задача.
    /// Порядок: тимлид первым, дальше по иерархии команды (ТЗ п. 2.7).</summary>
    public List<TaskDiagramLaneDto> Lanes { get; set; } = [];

    /// <summary>Задачи поддерева в том порядке, в каком их будет выполнять запуск иерархии
    /// (T-159): потомки раньше родителя, внутри уровня — по убыванию числового приоритета.</summary>
    public List<TaskDiagramNodeDto> Nodes { get; set; } = [];

    /// <summary>Длительность задачи без плановой (часы) — настройка проекта
    /// <see cref="ProjectSettings.DefaultTaskHoursKey"/>; клиент показывает её в подсказке.</summary>
    public double DefaultHours { get; set; } = ProjectSettings.DefaultTaskHoursDefault;

    /// <summary>Иерархия запущена: диаграмму надо перестраивать сама собой, пока идёт работа.</summary>
    public bool Running { get; set; }
}

/// <summary>Дорожка диаграммы — исполнитель (T-132-S0).</summary>
public sealed class TaskDiagramLaneDto
{
    public string ExecutorId { get; set; } = "";
    public string Nick { get; set; } = "";
    /// <summary>«ai» или «human» — значок дорожки (человек или ИИ).</summary>
    public string Kind { get; set; } = "human";
    /// <summary>Тимлид команды задачи: всегда верхняя дорожка (если он занят хоть где-то).</summary>
    public bool IsLead { get; set; }
    /// <summary>Уровень подчинения в команде (0 — верхний): порядок дорожек.</summary>
    public int Level { get; set; }
}

/// <summary>
/// ТИП ЗАДАЧИ и его параметры (T-298-S0) — разобранные ключи <c>launch_json</c>
/// (<see cref="TaskFlow"/>). Отдаётся готовым в <see cref="TaskItem.Flow"/> и в
/// <see cref="TaskDiagramNodeDto.Flow"/>, чтобы движок иерархии и диаграмма не разбирали
/// json сами. Поля чужого типа всегда пусты: у линейной задачи всё по умолчанию.
/// </summary>
public sealed class TaskFlowDto
{
    /// <summary>linear | if | loop | do-loop (<see cref="TaskFlow.Types"/>).</summary>
    public string Type { get; set; } = TaskFlow.Linear;

    /// <summary>Условие: задача при «Да» — идентификатор ПРЯМОГО потомка; null — не указана.</summary>
    public string? IfTrueTaskId { get; set; }
    /// <summary>Условие: задача при «Нет» — идентификатор ПРЯМОГО потомка; null — не указана.</summary>
    public string? IfFalseTaskId { get; set; }
    /// <summary>Ветка «Да» без задачи: «создавать задачи».</summary>
    public bool IfTrueCreateTasks { get; set; }
    /// <summary>Ветка «Нет» без задачи: «создавать задачи».</summary>
    public bool IfFalseCreateTasks { get; set; }
    /// <summary>Ветка «Да» без задачи и без создания задач: «завершить выполнение иерархии».</summary>
    public bool IfTrueStopHierarchy { get; set; }
    /// <summary>Ветка «Нет» без задачи и без создания задач: «завершить выполнение иерархии».</summary>
    public bool IfFalseStopHierarchy { get; set; }

    /// <summary>Циклы: лимит кругов. null — не задан (при создании задачи не из шаблона
    /// подставляется <see cref="ProjectSettings.RecheckLimit"/> проекта).</summary>
    public int? RecheckLimit { get; set; }
    /// <summary>Циклы: «остановить выполнение всей иерархии при превышении лимита».</summary>
    public bool LoopStopHierarchy { get; set; }
}

/// <summary>Квадрат диаграммы — задача (T-132-S0).</summary>
public sealed class TaskDiagramNodeDto
{
    /// <summary>Тип задачи и параметры ветвей/цикла (T-298-S0) — для развилок и циклов диаграммы.</summary>
    public TaskFlowDto Flow { get; set; } = new();

    // --- ФАКТ хода условия и цикла (T-301-S0, ключи TaskFlowRun в launch_json) ---
    /// <summary>Решение агента у «Условия»: true — «Да», false — «Нет», null — не сообщено
    /// (у цикла движок снимает решение после чтения, для диаграммы оно там не нужно).</summary>
    public bool? FlowDecision { get; set; }
    /// <summary>У «Условия»: переход по решению выполнен движком — стрелки зелёная/серая.</summary>
    public bool FlowApplied { get; set; }
    /// <summary>Выполнение иерархии остановлено на этой задаче — знак Stop.</summary>
    public bool FlowStopped { get; set; }
    /// <summary>У цикла: пройдено кругов.</summary>
    public int LoopPass { get; set; }
    /// <summary>У цикла: предел кругов, по которому работает движок (свой recheckLimit либо
    /// проекта); у прочих задач 0.</summary>
    public int LoopLimit { get; set; }

    public string Id { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    /// <summary>Состояние РАБОТЫ агента (T-187): «in_progress» / «paused»; пусто — работы нет.
    /// Печатается вторым полем внизу квадрата, как вторая колонка «в работе у ИИ».</summary>
    public string WorkStatus { get; set; } = "";
    public string? ParentId { get; set; }
    /// <summary>Чья дорожка: у идущей и законченной задачи — тот, кто РЕАЛЬНО её делает
    /// или делал (jobs / actual_executor_id), у остальных — назначенный.</summary>
    public string ExecutorId { get; set; } = "";
    /// <summary>Начало задачи в часах от начала диаграммы (считается порядком запуска).</summary>
    public double StartHours { get; set; }
    /// <summary>ФАКТИЧЕСКОЕ начало работы (первое задание задачи, jobs.started_at); пусто —
    /// работа ещё не начиналась. Раскладке нужно, чтобы уже случившееся стояло на дорожке
    /// в том порядке, в каком оно случилось, а плановое — после него (T-164-S0).</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>Начало ЖИВОГО задания (T-312-S0): от него внизу квадрата считается время
    /// работы, как в представлении «в работе у ИИ»; пусто — работы сейчас нет.</summary>
    public DateTime? WorkStartedAt { get; set; }
    /// <summary>Чего ждёт работа по задаче (T-312-S0) — тот же расчёт, что в «в работе у ИИ»;
    /// null — ждать нечего.</summary>
    public AiPauseDto? Pause { get; set; }
    /// <summary>Длительность в часах: реально затраченное время у законченной задачи,
    /// у идущей — не меньше прошедшего, у остальных — плановая либо умолчание проекта.</summary>
    public double DurationHours { get; set; }
    /// <summary>Длительность взята из ЗАТРАЧЕННОГО времени (заданий), а не из плана.</summary>
    public bool Actual { get; set; }
    /// <summary>Блокирующие задачи (ТЗ v1.35) — красные стрелки.</summary>
    public List<string> BlockerIds { get; set; } = [];
    /// <summary>Задача ВНЕ этой иерархии (T-135-S0): сама блокирующая задача либо её
    /// подзадача. Рисуется красной вместе со своим исполнителем — так видно, кого и чего
    /// ждёт иерархия. Своей рамкой задача, у которой блокирующая снаружи, больше не
    /// красится: красным помечается тот, кто держит, а не тот, кого держат.</summary>
    public bool Outside { get; set; }
    /// <summary>Корень иерархии (T-135-S0): рисуется всегда последним, рамка жирнее,
    /// перетаскиванию не подлежит — но на него можно бросать другие задачи.</summary>
    public bool IsRoot { get; set; }
    /// <summary>Задача другого сервера — правка и запуск закрыты (ТЗ гл. 6).</summary>
    public bool IsReadOnly { get; set; }
    /// <summary>Задача сдана (проверка/готово/отмена) — у неё нет кнопок запуска.</summary>
    public bool Settled { get; set; }
    /// <summary>Живое задание есть: у квадрата вместо «запустить» показывается «остановить».</summary>
    public bool HasActiveJob { get; set; }
    /// <summary>Числовой приоритет задачи (чем больше, тем раньше её делать). Раскладке нужен,
    /// чтобы отличать задачи, которые очередь и правда ставит по-разному, от равных по
    /// приоритету соседей: только среди последних взятая в работу обгоняет ждущую (T-192-S0).</summary>
    public int PriorityNum { get; set; }
}

/// <summary>Карточка задачи: сущность + тела файлов + задания.</summary>
public sealed class TaskDetailsDto
{
    public TaskItem Task { get; set; } = new();
    public string Description { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public List<string> Artifacts { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];

    /// <summary>Зависшее running-задание (T-117): в БД «выполняется», а живого вызова
    /// у коннектора нет (перезапуск приложения). UI показывает кнопку «продолжить».</summary>
    public string? StalledJobId { get; set; }

    /// <summary>Отложенный старт (T-121): задача ждёт этого момента — старт перенесли,
    /// потому что у исполнителя почти не осталось лимита. null — переноса нет.</summary>
    public DateTime? StartAfter { get; set; }

    /// <summary>Код родительской задачи (T-130) — для кнопки «к родителю» на карточке.
    /// null — родителя нет либо он удалён.</summary>
    public string? ParentDisplayId { get; set; }

    /// <summary>Заголовок родительской задачи (T-130) — подсказка кнопки «к родителю».</summary>
    public string? ParentTitle { get; set; }

    /// <summary>Чего ждёт работа ИИ-агента над задачей (T-187): лимит, вопросы в чате,
    /// подзадачи. null — не ждёт ничего. Считается тем же кодом, что и в представлении
    /// «в работе у ИИ», — вывод в форме задачи и в списке одинаковый.</summary>
    public AiPauseDto? Pause { get; set; }

    /// <summary>Код корня ОТКРЫТОЙ иерархии над задачей (T-263): сама задача или её предок
    /// с пометкой runHierarchy. null — задача не в иерархическом запуске. По нему кнопка
    /// «остановить» решает, надо ли переспрашивать про всю иерархию: снятия одного задания
    /// мало — очередь запустит задачу снова, как только до неё дойдёт.</summary>
    public string? HierarchyRootDisplayId { get; set; }

    /// <summary>Идентификатор того же корня (T-263): останавливать иерархию надо от него.</summary>
    public string? HierarchyRootId { get; set; }

    /// <summary>Пометка «автозапуск подзадач выключен» стоит на САМОЙ задаче (T-54-S0):
    /// по ней кнопка на карточке меняется на «включить автозапуск».</summary>
    public bool AutoStartOff { get; set; }

    /// <summary>Код задачи, ВЫКЛЮЧИВШЕЙ автозапуск для этой ветки (T-54-S0): сама задача или
    /// её предок. null — автозапуск разрешён. Нужен подсказке: у потомка пометки нет, а
    /// автоматических стартов у него всё равно не будет, и это надо назвать прямо.</summary>
    public string? AutoStartOffRoot { get; set; }

    /// <summary>Путь файла РУЧНОГО результата задачи (T-130-S0): по нему карточка заводит
    /// результат у задачи, у которой его ещё нет вовсе (иконка правки во вкладке
    /// «Результат» стоит и при пустом результате — копировать там нечего, а писать есть что).
    /// Файла по этому пути может не быть; у шаблона поле пустое — результатов у него не бывает.</summary>
    public string ManualResultPath { get; set; } = "";
}

/// <summary>
/// ИТОГ ОСТАНОВКИ (T-263). Кнопка «остановить» снимает не только текущее задание: если
/// задача идёт в открытом иерархическом запуске, снятие одного задания ничего не решает —
/// очередь поднимет её на следующем же проходе. Поэтому останавливать приходится очередь,
/// а по желанию человека — и работающих потомков; ответом человеку надо сказать, что
/// именно остановлено.
/// <para>Тем же ответом описывается и остановка ЧУЖОЙ задачи: здесь её снять нечем —
/// задание идёт на сервере-владельце, туда уезжает заявка (как у запуска, T-196).</para>
/// </summary>
public sealed class TaskStopDto
{
    public string TaskId { get; set; } = "";

    /// <summary>Номер задачи, по которой нажали «остановить».</summary>
    public string TaskDisplayId { get; set; } = "";

    /// <summary>Коды задач, чьи задания сняты здесь.</summary>
    public List<string> Stopped { get; set; } = [];

    /// <summary>Сколько заданий снято.</summary>
    public int Jobs { get; set; }

    /// <summary>Очередь иерархического запуска закрыта (пометка с корня снята).</summary>
    public bool HierarchyStopped { get; set; }

    /// <summary>Код корня той иерархии; пусто — задача не была в иерархическом запуске.</summary>
    public string HierarchyRoot { get; set; } = "";

    /// <summary>Коды задач, остановку которых пришлось передать их серверам заявкой (T-196):
    /// задание идёт там, снять его отсюда нечем.</summary>
    public List<string> Requested { get; set; } = [];

    /// <summary>Сервер, которому передана остановка: «S1 (ноутбук)» — для сообщения человеку.
    /// Пусто — ничего никуда не передавали.</summary>
    public string TargetServer { get; set; } = "";

    /// <summary>Такая заявка на остановку уже подана раньше и ещё не отработана: повторное
    /// нажатие ничего не меняет, надо дождаться сервера задачи.</summary>
    public bool AlreadyRequested { get; set; }

    /// <summary>Останавливать было нечего: заданий нет, очередь закрыта.</summary>
    public bool Nothing { get; set; }

    /// <summary>Задания, снять которые не удалось (ошибка коннектора и т. п.): остальное
    /// при этом остановлено — человек должен видеть, что осталось.</summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Строка представления «в работе у ИИ» (T-187): задача, которую ИИ-агент выполняет прямо
/// сейчас либо держит на паузе. Задача целиком, а не отдельные поля, — списку нужны и проект,
/// и сервер-владелец, и код, и те же правила показа, что в остальных представлениях.
/// </summary>
public sealed class AiWorkItemDto
{
    public TaskItem Task { get; set; } = new();

    /// <summary>Задание, по которому идёт работа; пусто — задание ещё не создано
    /// (старт перенесён по лимиту до первого запуска).</summary>
    public string JobId { get; set; } = "";

    /// <summary>ИИ-исполнитель: чей это агент.</summary>
    public string ExecutorId { get; set; } = "";

    public string ExecutorNick { get; set; } = "";

    /// <summary>Начало работы (UTC): от него считается время работы. null — работа ещё
    /// не начиналась (задание не создавалось).</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Работа стоит: <see cref="Pause"/> объясняет, чего ждём.</summary>
    public bool Paused { get; set; }

    /// <summary>Причина паузы (T-187); null — агент работает.</summary>
    public AiPauseDto? Pause { get; set; }
}

/// <summary>
/// ЧТО РАБОТАЕТ В ОРГАНИЗАЦИИ ПРЯМО СЕЙЧАС — форма смены организации (T-188).
///
/// Организация — отдельная база, отдельные агенты и отдельные процессы моделей на этом
/// компьютере, поэтому уйти из неё «незаметно» нельзя: сначала человеку показывают, кого
/// именно остановят, и только по его решению останавливают. В список попадает ровно то,
/// что будет остановлено, — иначе предупреждение обманывает.
/// </summary>
public sealed class OrgAgentsDto
{
    /// <summary>Задания ИИ-агентов, которые идут или ждут (те же строки, что в представлении
    /// «в работе у ИИ», T-187): у каждой видно исполнителя, задачу и причину ожидания.</summary>
    public List<AiWorkItemDto> Agents { get; set; } = [];

    /// <summary>Названия команд с живыми подключениями: у них разорвутся подключения ИИ,
    /// а поднятые ими локальные серверы моделей будут выгружены.</summary>
    public List<string> Teams { get; set; } = [];
}

/// <summary>Итог принудительной остановки агентов организации (T-188).</summary>
public sealed class OrgStopResultDto
{
    /// <summary>Снято заданий.</summary>
    public int Jobs { get; set; }

    /// <summary>Остановлено команд (с выгрузкой их локальных серверов моделей).</summary>
    public int Teams { get; set; }

    /// <summary>Закрыто открытых очередей иерархического запуска (T-263): без этого снятые
    /// задания через минуту поднял бы сторож — и «остановить всех» ничего не остановило бы.</summary>
    public int Hierarchies { get; set; }

    /// <summary>Задания, которые снять не удалось: чужой сервер-владелец (ТЗ гл. 6) или
    /// ошибка коннектора. Смену организации это не отменяет — человек видит, что осталось.</summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Остаток лимита ИИ-исполнителя за скользящее окно (T-121) — для окна предупреждения
/// перед запуском задачи. null вместо всего объекта значит «лимиты не указаны»:
/// вычисление игнорируется, задача запускается как обычно.
/// </summary>
public sealed class AgentLimitDto
{
    public string ExecutorId { get; set; } = "";
    public string Nick { get; set; } = "";
    /// <summary>Лимит токенов за окно.</summary>
    public long Limit { get; set; }
    /// <summary>Длина окна, часов.</summary>
    public double WindowHours { get; set; }
    /// <summary>Израсходовано за окно.</summary>
    public long Used { get; set; }
    /// <summary>Остаток токенов.</summary>
    public long Remaining { get; set; }
    /// <summary>Использовано, % лимита.</summary>
    public double Percent { get; set; }
    /// <summary>Когда окно освободится; null — в окне заданий нет.</summary>
    public DateTime? ResetAt { get; set; }
    /// <summary>Остатка слишком мало — запуск лучше перенести или разбить задачу.</summary>
    public bool IsLow { get; set; }
}

/// <summary>
/// Итог прохода очереди иерархического запуска (T-159): кнопка «запустить иерархию»
/// запускает не одну задачу, а всё поддерево — снизу вверх, по убыванию приоритета,
/// и большая часть подзадач в момент нажатия только становится в очередь.
/// </summary>
public sealed class HierarchyRunDto
{
    /// <summary>Коды задач, запущенных этим проходом (пусто — все ждут своей очереди).</summary>
    public List<string> Started { get; set; } = [];

    /// <summary>Сколько задач ждут: занят исполнитель, не готовы подзадачи, перенесён старт,
    /// задача уже выполняется или встала с ошибкой.</summary>
    public int Waiting { get; set; }

    /// <summary>Сколько задач из <see cref="Started"/> перезапущено после ошибки (T-186):
    /// человек попросил галочкой в переспросе.</summary>
    public int Restarted { get; set; }

    /// <summary>Сколько задач из <see cref="Started"/> запущено из «доработки» (T-210):
    /// человек попросил второй галочкой в переспросе.</summary>
    public int Reworked { get; set; }

    /// <summary>Сколько задач пропущено: уже выполнены (проверка/готово/отменено), нет
    /// исполнителя, чужой сервер либо запуск запрещён правилом безопасности.</summary>
    public int Skipped { get; set; }

    /// <summary>Всего задач в иерархии (вместе с корнем).</summary>
    public int Total { get; set; }

    /// <summary>Сколько задач ЧУЖИХ серверов очередь попросила запустить заявкой (T-196):
    /// запускает их владелец, а иерархия ждёт их завершения. Они же посчитаны
    /// в <see cref="Waiting"/> — здесь видно, сколько из ожидания ушло на другие серверы.</summary>
    public int Requested { get; set; }

    /// <summary>Запускать больше нечего — очередь закрыта (пометка с корня снята).</summary>
    public bool Finished { get; set; }

    /// <summary>Очередь ОСТАНОВЛЕНА (T-6-S1): у задачи иерархии отменена блокирующая задача,
    /// значит работа, которой она ждала, не состоится. Пометка с корня снята, как при
    /// завершении, но причина другая — и человеку её надо назвать.</summary>
    public bool Stopped { get; set; }

    /// <summary>Коды задач, у которых отменена блокирующая (T-6-S1) — из-за них
    /// <see cref="Stopped"/>.</summary>
    public List<string> StoppedBy { get; set; } = [];
}

/// <summary>
/// Заявка на запуск задачи, поданная с ДРУГОГО сервера (T-196): ответ кнопки «запустить»
/// на чужой задаче. Самого задания здесь нет — оно появится у владельца задачи, когда
/// заявка доедет к нему репликацией.
/// </summary>
public sealed class RunRequestDto
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    /// <summary>Номер задачи для сообщения человеку.</summary>
    public string TaskDisplayId { get; set; } = "";
    /// <summary>task / hierarchy (<see cref="AI2P.Core.Entities.RunRequestKinds"/>).</summary>
    public string Kind { get; set; } = "";
    /// <summary>Иерархия: перезапускать и вставшие с ошибкой (T-186).</summary>
    public bool WithErrors { get; set; }
    /// <summary>Иерархия: запускать и задачи в доработке (T-210).</summary>
    public bool WithNeedsFix { get; set; }
    /// <summary>Сервер, который выполнит запуск: «S1 (ноутбук)».</summary>
    public string TargetServer { get; set; } = "";
    /// <summary>Заявка не понадобилась: задача (или иерархия) уже запущена. Тогда
    /// <see cref="Id"/> пуст — делать ничего не нужно.</summary>
    public bool AlreadyRunning { get; set; }

    /// <summary>Такая заявка уже подана раньше и ещё не отработана: повторное нажатие ничего
    /// не меняет, надо просто дождаться сервера задачи. Здесь — та самая заявка.</summary>
    public bool AlreadyRequested { get; set; }
}

/// <summary>Создание/изменение задачи: сущность + текст описания и критериев приёмки.</summary>
public sealed class TaskSaveDto
{
    public TaskItem Task { get; set; } = new();
    public string Description { get; set; } = "";
    public string Acceptance { get; set; } = "";
}

public sealed class StatusChangeDto
{
    /// <summary>Код состояния из справочника состояний (ТЗ v1.37).</summary>
    public string Status { get; set; } = "";
}

/// <summary>Перенос задачи по иерархии — drag-n-drop в представлении «иерархия» (todo37).</summary>
public sealed class TaskParentDto
{
    /// <summary>Новый родитель; null или пусто — задача становится корневой.</summary>
    public string? ParentId { get; set; }
}

/// <summary>Смена исполнителя — drag-n-drop на дорожку диаграммы подзадач (T-135-S0).</summary>
public sealed class TaskExecutorDto
{
    /// <summary>Новый исполнитель; null или пусто — задача остаётся без исполнителя.</summary>
    public string? ExecutorId { get; set; }
}

/// <summary>
/// Смена сервера-владельца задачи (кнопка «сменить сервер», ТЗ гл. 6, этап 42). После неё
/// задача правится на новом сервере, а здесь становится только для чтения. Номер задачи
/// не меняется: это идентификатор и имя каталога её файлов.
/// </summary>
public sealed class TaskServerDto
{
    /// <summary>Внутренний ключ нового сервера; null или пусто — «без сервера» (правит дирижёр).</summary>
    public string? ServerId { get; set; }
}

/// <summary>
/// Сервер организации для рядового участника (ТЗ гл. 6, этап 42): фильтры по серверам
/// в списках задач и расписаний, выбор сервера в кнопке «сменить сервер». Адресов и токенов
/// здесь нет — карта кластера открыта только владельцу (раздел <c>/api/servers</c>).
/// </summary>
public sealed class OrgServerBriefDto
{
    /// <summary>Внутренний ключ сервера (unid).</summary>
    public string ServerId { get; set; } = "";
    /// <summary>Код в организации: S0, S1, … — он же суффикс номеров задач.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Это сервер, на котором мы работаем.</summary>
    public bool IsLocal { get; set; }
    public bool IsConductor { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// СМЕНА ДИРИЖЁРА (T-21-S1): что показывает форма заявки и строка внизу списка серверов.
/// Сама заявка — строка БД организации, она ездит второй стороне репликацией; здесь к ней
/// добавлено то, что считается на месте: можно ли эту заявку принять, снять и сколько
/// осталось до одностороннего принятия.
/// </summary>
public sealed class ConductorRequestDto
{
    public string Id { get; set; } = "";

    /// <summary>Кого назначают дирижёром: код и имя сервера.</summary>
    public string TargetServerId { get; set; } = "";
    public string TargetServerCode { get; set; } = "";
    public string TargetServerName { get; set; } = "";

    /// <summary>Сегодняшний дирижёр организации.</summary>
    public string FromServerId { get; set; } = "";
    public string FromServerCode { get; set; } = "";
    public string FromServerName { get; set; } = "";

    /// <summary>Кто подал заявку (внутренний ключ его сервера).</summary>
    public string InitiatorServerId { get; set; } = "";

    /// <summary>Заявку подал САМ назначаемый сервер: только у такой есть обратный отсчёт
    /// и одностороннее принятие.</summary>
    public bool ByTarget { get; set; }

    /// <summary><see cref="AI2P.Core.Entities.ConductorRequestStatus"/>.</summary>
    public string Status { get; set; } = "";
    public string Note { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>Принята в одностороннем порядке (дирижёр не отозвался за 12 часов).</summary>
    public bool Unilateral { get; set; }

    /// <summary>Заявку подали МЫ: значит, её можно снять, но нельзя принять или отклонить.</summary>
    public bool Mine { get; set; }

    /// <summary>Решение по заявке принимаем МЫ (мы — вторая сторона): кнопки «принять»
    /// и «отклонить» есть только здесь.</summary>
    public bool CanDecide { get; set; }

    /// <summary>Кнопка «Принять в одностороннем порядке» уже доступна (T-21-S1).</summary>
    public bool CanTakeOver { get; set; }

    /// <summary>Сколько секунд осталось до одностороннего принятия; 0 — уже можно либо
    /// такой возможности у этой заявки нет вовсе.</summary>
    public long CountdownSec { get; set; }
}

/// <summary>
/// Можно ли назначить этот сервер дирижёром (T-21-S1) — ответ на вопрос формы сервера.
/// Правило одно на кнопку и на эндпойнт: иначе кнопка окажется живой там, где действие
/// запрещено, и человек будет получать отказ уже после заполнения формы.
/// </summary>
public sealed class ConductorInfoDto
{
    /// <summary>
    /// Раздел смены дирижёра на форме этого сервера вообще имеет смысл: сервер подключён
    /// к текущей организации и НЕ является её дирижёром («форма настройки сервера
    /// не дирижёра»). У дирижёра и у чужого сервера раздела нет вовсе — там нечего делать,
    /// и кнопка была бы шумом.
    /// </summary>
    public bool Applicable { get; set; }

    /// <summary>Кнопка «назначить дирижёром» имеет смысл: заявку можно подать прямо сейчас.</summary>
    public bool CanRequest { get; set; }

    /// <summary>Почему нельзя (готовый текст для подсказки); пусто — можно.</summary>
    public string Problem { get; set; } = "";

    /// <summary>Вошедший — администратор ЭТОГО сервера: без этого входа заявку не подать
    /// и решение по ней не принять (ТЗ гл. 12).</summary>
    public bool IsServerAdmin { get; set; }

    /// <summary>Заявка, которая уже идёт по этой организации; null — заявки нет.</summary>
    public ConductorRequestDto? Request { get; set; }
}

/// <summary>Подача заявки на смену дирижёра (T-21-S1): всё, что вводит человек, — записка.</summary>
public sealed class ConductorRequestCreateDto
{
    public string Note { get; set; } = "";
}

/// <summary>
/// ОБЪЯВЛЕНИЕ О СМЕНЕ ДИРИЖЁРА остальным серверам кластера (T-21-S1). Едет напрямую,
/// а не репликацией: топология — звезда, и с новым дирижёром рядовой сервер мог ни разу
/// не разговаривать (а при потере прежнего дирижёра — и не сможет). Подлинность
/// доказывается подписью ключом организации (<see cref="AI2P.Core.ConductorProof"/>),
/// потому что общего токена у этой пары может не быть.
/// </summary>
public sealed class ClusterConductorDto
{
    /// <summary>Код организации, в которой сменился дирижёр.</summary>
    public string OrgCode { get; set; } = "";

    /// <summary>Внутренний ключ НОВОГО дирижёра — того, кто объявляет.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Заявка, по которой произошла смена (для журнала и для сверки).</summary>
    public string RequestId { get; set; } = "";

    /// <summary>Токен, которым получатель будет ходить к новому дирижёру: без него пара,
    /// никогда не обменивавшаяся токенами, реплицироваться не сможет (ТЗ гл. 12).</summary>
    public string Token { get; set; } = "";

    /// <summary>Время объявления в формате «o» — входит в подпись и проверяется на свежесть.</summary>
    public string Timestamp { get; set; } = "";

    /// <summary>Подпись ключом организации (HMAC-SHA256, base64).</summary>
    public string Signature { get; set; } = "";
}

/// <summary>Ответ на объявление о смене дирижёра (T-21-S1): встречный токен, чтобы новый
/// дирижёр тоже мог начинать сеансы с этим сервером.</summary>
public sealed class ClusterConductorAckDto
{
    /// <summary>Внутренний ключ ответившего сервера.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Токен, которым новый дирижёр будет ходить к нам.</summary>
    public string Token { get; set; } = "";

    /// <summary>Смена дирижёра принята и записана (у нас он теперь тот, кто объявил).</summary>
    public bool Applied { get; set; }
}

/// <summary>
/// Пер-серверная часть проекта (ТЗ гл. 6, этап 42): каталог, папка <c>Common</c> и активность
/// на конкретном сервере. Своя строка на каждом сервере — каталоги на разных компьютерах
/// разные; чужие показываются в форме проекта только для чтения.
/// </summary>
public sealed class ProjectServerDto
{
    public string ServerId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsLocal { get; set; }
    public string FolderPath { get; set; } = "";
    public string CommonPath { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>Итог передачи задач выбывшего сервера дирижёру (ТЗ гл. 6, этап 42).</summary>
public sealed class TakeoverResultDto
{
    /// <summary>Сколько задач сменило владельца.</summary>
    public int Tasks { get; set; }
}

/// <summary>Создание/правка состояния из справочника состояний (ТЗ v1.37, todo34_3):
/// название — на текущем языке UI (параметр lang запроса).</summary>
public sealed class TaskStatusSaveDto
{
    /// <summary>Внутренний код; при правке не меняется.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#9e9e9e";
    /// <summary>Менять можно только в debug-режиме (в релизе игнорируется).</summary>
    public bool IsCustom { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>«Создать из шаблона» (ТЗ п. 2.4): данные, переспрашиваемые при копировании, —
/// базовая дата (встаёт на место самой ранней даты иерархии, todo31) и вариант
/// автоподбора исполнителей.</summary>
public sealed class InstantiateTemplateDto
{
    public DateTime? BaseDate { get; set; }
    /// <summary>Автоподбор исполнителей (todo31): "ai_first" / "human_first"; null — без подбора.</summary>
    public string? Pick { get; set; }
    /// <summary>Задача-родитель (T-201): голова копии становится её подзадачей, а вся копия
    /// переезжает в проект родителя. Пусто — задача создаётся корневой, как раньше.</summary>
    public string? ParentId { get; set; }
}

/// <summary>Запись опыта по узлу шаблона для UI (вкладка «Опыт» шаблона, todo32):
/// ники вместо id исполнителей, display_id узла шаблона.</summary>
public sealed class ExperienceRecordDto
{
    public string Id { get; set; } = "";
    public string TemplateTaskId { get; set; } = "";
    /// <summary>Код узла шаблона (T-N) — в поддереве опыт разных узлов.</summary>
    public string TemplateDisplayId { get; set; } = "";
    /// <summary>Проект записи (todo48); пусто — это опыт узла шаблона.</summary>
    public string ProjectId { get; set; } = "";
    /// <summary>Навык записи (todo48); пусто — запись общая, её читают все.</summary>
    public string SkillId { get; set; } = "";
    /// <summary>Код навыка (<c>code-write</c>) — показывается в списке и фильтре.</summary>
    public string SkillName { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>«Загружать всегда» (T-24-S0): запись идёт в задание мимо отбора.</summary>
    public bool AlwaysLoad { get; set; }
    /// <summary>АКТИВНА (T-265-S0): неактивная запись не идёт в задание ни при каком раскладе,
    /// но человек видит её в списке — колонка «Активен» и фильтр.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Тэги записи (T-24-S0) — колонка и фильтр списка опыта.</summary>
    public List<string> Tags { get; set; } = [];
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// СТРОКА ВЫДАЧИ ПОИСКА ПО ОПЫТУ (T-268-S0): сама запись плюс то, чем она заслужила своё
/// место. Человек в списках опыта ищет ТЕМ ЖЕ механизмом, что и агент инструментом
/// <c>search_experience</c>, — <c>GET /api/experience/search</c>.
/// </summary>
public sealed class ExperienceHitDto
{
    public ExperienceRecordDto Record { get; set; } = new();

    /// <summary>Балл слияния рангов (BM25 + свежесть + совпадение тэгов); по нему
    /// упорядочена выдача.</summary>
    public double Score { get; set; }

    /// <summary>Область записи: project / template / general.</summary>
    public string Scope { get; set; } = "";
}

/// <summary>Состояние поиска по опыту (T-268-S0): работает ли лексический индекс FTS5.
/// Ложь — поиск идёт подстрочным сравнением (хуже, но работает).</summary>
public sealed class ExperienceSearchStateDto
{
    public bool IndexReady { get; set; }
}

/// <summary>Создание/правка записи опыта из UI (todo32, todo48).</summary>
public sealed class ExperienceSaveDto
{
    public string Text { get; set; } = "";
    /// <summary>Навык записи (todo48); пусто — запись общая, её читают все исполнители.</summary>
    public string? SkillId { get; set; }

    /// <summary>«Загружать всегда» (T-24-S0): запись получает каждый исполнитель, даже если
    /// отбор по навыку и тэгам её не выбрал. Умолчание — выключено.</summary>
    public bool AlwaysLoad { get; set; }

    /// <summary>АКТИВНА (T-265-S0): переключатель формы записи. Умолчание — включено, поэтому
    /// вызов, не знающий про поле вовсе, запись не гасит.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Тэги записи (T-24-S0); null — при правке оставить прежние.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>ПОДТВЕРЖДЕНИЕ человека (T-269-S0): текст с признаками проектного (код задачи,
    /// путь файла, расширение исходника, имя проекта) в ОБЩИЕ правила работы не проходит,
    /// а форма показывает предупреждение с кнопкой «всё равно сохранить» — она и ставит
    /// этот признак. Инструмент агента его не ставит никогда: ему отказ окончательный.</summary>
    public bool Force { get; set; }
}

/// <summary>
/// ПЕРЕНОС ЗАПИСИ ОПЫТА МЕЖДУ ОБЛАСТЯМИ (T-269-S0): POST /api/experience/{id}/move.
/// Меняется только привязка записи — идентификатор, текст, навык, тэги, авторство
/// и история остаются прежними.
/// </summary>
public sealed class ExperienceMoveDto
{
    /// <summary>Куда: project / template / general.</summary>
    public string Scope { get; set; } = "";

    /// <summary>Проект-получатель (scope=project).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Узел шаблона-получатель (scope=template).</summary>
    public string? TemplateTaskId { get; set; }

    /// <summary>Подтверждение человека при переносе проектного текста в общие правила.</summary>
    public bool Force { get; set; }
}

/// <summary>
/// СТРОКА РЕВИЗИИ ОБЩЕГО ОПЫТА (T-269-S0): общее правило, которое выглядит ПРОЕКТНЫМ,
/// и то, чем оно поймано. GET /api/experience/general/suspects.
/// </summary>
public sealed class ExperienceSuspectDto
{
    public ExperienceRecordDto Record { get; set; } = new();

    /// <summary>Виды найденных признаков: taskCode / path / ext / name.</summary>
    public List<string> Kinds { get; set; } = [];

    /// <summary>Образцы текста, которыми запись поймана, — через запятую.</summary>
    public string Samples { get; set; } = "";
}

/// <summary>Переключение активности записи опыта (T-265-S0): POST /api/experience/{id}/active.
/// Отдельным вызовом, а не правкой записи целиком, — кнопка в строке списка меняет одно поле
/// и не переписывает текст, навык и тэги значениями давно прочитанного списка.</summary>
public sealed class ExperienceActiveDto
{
    public bool IsActive { get; set; }
}

/// <summary>
/// НАБОР ОПЫТА в списке закладки «Настройки → Наборы опыта» (T-270-S0): GET /api/packs.
/// Состав файла (<c>packs/&lt;код&gt;/pack.json</c>) плюс то, что известно ТОЛЬКО базе, —
/// установлен ли он здесь и в какую область.
/// </summary>
public sealed class ExperiencePackDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Сколько записей опыта в файле набора.</summary>
    public int Records { get; set; }

    /// <summary>Сколько узлов шаблона задач приносит набор (блок <c>templates</c>).</summary>
    public int Templates { get; set; }

    /// <summary>Сколько записей набора стоит в организации сейчас.</summary>
    public int Installed { get; set; }

    /// <summary>Область, в которую набор поставлен: project / template / general; пусто —
    /// не установлен.</summary>
    public string Scope { get; set; } = "";

    /// <summary>Проект-получатель (Scope=project) и его название — для строки списка.</summary>
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";

    /// <summary>Узел шаблона-получатель (Scope=template) и его номер.</summary>
    public string TemplateTaskId { get; set; } = "";
    public string TemplateDisplayId { get; set; } = "";

    /// <summary>Страница поставляемой документации набора (окно <c>DocPageDialog</c>).</summary>
    public string Doc { get; set; } = "";
}

/// <summary>УСТАНОВКА НАБОРА (T-270-S0): POST /api/packs/{code}/install. Область ОБЯЗАТЕЛЬНА
/// — интерфейс не даёт её не выбрать (решение заказчика), а сервер пустую отвергает.</summary>
public sealed class ExperiencePackInstallDto
{
    /// <summary>Куда ставим: project / template / general.</summary>
    public string Scope { get; set; } = "";

    public string? ProjectId { get; set; }
    public string? TemplateTaskId { get; set; }
}

/// <summary>ВЫГРУЗКА ОТОБРАННЫХ ЗАПИСЕЙ В ФАЙЛ НАБОРА (T-270-S0): POST /api/packs/export.
/// Способ перенести наработанный стиль работы в другую организацию или установку.</summary>
public sealed class ExperiencePackExportDto
{
    /// <summary>Код нового набора — он же имя каталога <c>packs/&lt;код&gt;</c>.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Идентификаторы записей опыта, отобранных человеком.</summary>
    public List<string> RecordIds { get; set; } = [];
}

/// <summary>Итог установки, снятия или выгрузки набора (T-270-S0).</summary>
public sealed class ExperiencePackResultDto
{
    public string Code { get; set; } = "";

    /// <summary>Сколько записей заведено (установка) либо снято (снятие).</summary>
    public int Count { get; set; }

    /// <summary>Сколько УЗЛОВ ШАБЛОНА завела установка набора (T-271-S0): набор приносит
    /// с собой не только записи опыта, но и готовые узлы шаблона задач — например
    /// «Анализ опыта». Ноль означает и «узлов в наборе нет», и «они уже заведены».</summary>
    public int Templates { get; set; }

    /// <summary>Путь файла относительно каталога данных (выгрузка).</summary>
    public string Path { get; set; } = "";
}

/// <summary>
/// ИСПОЛЬЗОВАННАЯ ЗАПИСЬ ОПЫТА (T-266-S0): строка вкладки «Использованный опыт» карточки
/// задачи — что система подставила агенту в задание. У задачи хранится ТОЛЬКО идентификатор
/// записи, поэтому текст, область, навык и тэги дочитываются при выдаче и приходят ПУСТЫМИ,
/// если запись удалили или унесли в архив (<see cref="Available"/> = false): строка при этом
/// остаётся — идентификатор и есть то, что мы обещали хранить.
/// </summary>
public sealed class ExperienceUsedDto
{
    public string ExperienceId { get; set; } = "";

    /// <summary>Запись ещё жива; false — показывается строкой «запись недоступна».</summary>
    public bool Available { get; set; }

    /// <summary>Область записи: general / project / template; пусто — запись недоступна.</summary>
    public string Scope { get; set; } = "";

    /// <summary>Код узла шаблона (T-N) у записи узла — иначе пусто.</summary>
    public string TemplateDisplayId { get; set; } = "";

    public string SkillName { get; set; } = "";

    /// <summary>Навык записи кодом (T-293-S0): правка записи идёт прямо из этого журнала —
    /// по ссылке на запись, — и форме нужен код навыка, а не его название.</summary>
    public string SkillId { get; set; } = "";

    public string Text { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    /// <summary>«Загружать всегда» (T-293-S0): нужен форме правки — иначе сохранение
    /// из журнала молча сбрасывало бы признак.</summary>
    public bool AlwaysLoad { get; set; }

    /// <summary>Запись активна (T-265-S0): погашенную в следующее задание уже не подставят.</summary>
    public bool IsActive { get; set; }

    /// <summary>Задание последнего использования; пусто — неизвестно.</summary>
    public string JobId { get; set; } = "";

    /// <summary>Когда запись уходила в задание этой задачи в последний раз.</summary>
    public DateTime UsedAt { get; set; }
}

/// <summary>Статистика использования записи опыта (T-266-S0): GET /api/experience/{id}/usage —
/// сколько ЗАДАЧ получило запись в задании и когда это было в последний раз.</summary>
public sealed class ExperienceUsageDto
{
    public string ExperienceId { get; set; } = "";
    public int Count { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>Строка статистики шаблона (вкладка «Статистика», todo32): смена состояния
/// задачи, привязанной к шаблону, — состояние, дата-время, исполнитель.</summary>
public sealed class TemplateStatusStatDto
{
    public string TaskId { get; set; } = "";
    public string TaskDisplayId { get; set; } = "";
    /// <summary>Новое состояние (код статуса).</summary>
    public string Status { get; set; } = "";
    public DateTime At { get; set; }
    /// <summary>Кто сменил состояние (актор события); пусто — система.</summary>
    public string? ExecutorId { get; set; }
    public string ExecutorNick { get; set; } = "";
}

/// <summary>Анализ иерархии шаблона перед созданием задачи (todo31): что переспрашивать.</summary>
public sealed class TemplateInfoDto
{
    /// <summary>Есть задачи со сроками → спросить базовую дату.</summary>
    public bool HasDueDates { get; set; }
    /// <summary>Самая ранняя дата иерархии — начальное значение поля базовой даты.</summary>
    public DateTime? MinDueDate { get; set; }
    /// <summary>Есть задачи со skills без активного исполнителя → спросить вариант автоподбора.</summary>
    public bool NeedsExecutorPick { get; set; }
}

/// <summary>Элемент Inbox (гл. 5, экран 7): задание человеку либо вопрос агента (T-165).</summary>
public sealed class InboxItemDto
{
    /// <summary>«job» — задание человеку, «question» — висящий вопрос ИИ-агента (T-165).</summary>
    public string Kind { get; set; } = "job";
    public Job Job { get; set; } = new();
    public string TaskTitle { get; set; } = "";
    public string TaskDisplayId { get; set; } = "";
    public string? ProjectId { get; set; }
    /// <summary>Текст задания (описание задачи) — что человеку нужно сделать.</summary>
    public string RequestText { get; set; } = "";
    /// <summary>Когда элемент появился: задание создано / вопрос задан (сортировка Inbox).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Ник исполнителя: чьё задание, а у вопроса — кто спрашивает.</summary>
    public string ExecutorNick { get; set; } = "";
    /// <summary>Вопрос агента (T-165): id сообщения чата, на него отвечают.</summary>
    public string? QuestionId { get; set; }
    /// <summary>Текст вопроса агента (Markdown).</summary>
    public string QuestionText { get; set; } = "";
    /// <summary>Варианты ответа на вопрос — кнопками.</summary>
    public List<string> Options { get; set; } = [];
    /// <summary>Ответ ещё принимается: задание ждёт человека. false — агент уже остановлен.</summary>
    public bool CanAnswer { get; set; } = true;
}

public sealed class JobAnswerDto
{
    public string Text { get; set; } = "";
}

public sealed class ChatPostDto
{
    public string Text { get; set; } = "";
    public string? ToExecutorId { get; set; }
}

/// <summary>
/// Настройки ПРИЛОЖЕНИЯ, редактируемые из UI (гл. 10): пишутся обратно в config.json.
/// Всё, что относится к самому серверу — адрес, порт, каталоги этого компьютера, пароль
/// администратора сервера, — живёт в <see cref="ServerSettingsDto"/> и правится в форме
/// локального сервера на вкладке «Серверы» (todo41 п. 3).
/// </summary>
public sealed class SettingsDto
{
    public string Language { get; set; } = "ru";
    public bool OpenBrowserOnStart { get; set; } = true;
    /// <summary>Вторая строка заголовка закладок «Задача»/«Проект» (ТЗ v1.18): name / code.</summary>
    public string TabTitleMode { get; set; } = "name";
    /// <summary>Ячейка календаря расписаний (ТЗ v1.37): после времени — код шаблона ("code")
    /// или начало заголовка шаблона ("name").</summary>
    public string ScheduleCellMode { get; set; } = "code";

    /// <summary>Уровень вывода в лог (T-136): Debug / Info / Warning — один параметр
    /// на технический лог приложения и на журнал работ (ТЗ п. 6.3). Умолчание — Warning.</summary>
    public string LogLevel { get; set; } = "Warning";

    /// <summary>Пределы кадра датасета LoRA (T-12-S1): размер в пикселях, размер в
    /// килобайтах и формат. По заданию эти значения живут в настройках, а не в коде.</summary>
    public LoraImageLimitsDto LoraImage { get; set; } = new();

    /// <summary>Почтовый сервер для уведомлений (T-272): свой у каждой установки.</summary>
    public MailSettingsDto Mail { get; set; } = new();

    /// <summary>Автообновление приложения (T-208): свой у каждой установки.</summary>
    public UpdateSettingsDto Update { get; set; } = new();
}

/// <summary>
/// АВТООБНОВЛЕНИЕ — НАСТРОЙКА (T-208), раздел <c>update</c> в config.json плюс то, что
/// к ней прилагается на экране: коды записей расписания, заведённых флажками, и адрес
/// страницы выпусков.
/// </summary>
public sealed class UpdateSettingsDto
{
    /// <summary>Автоматическая проверка обновлений.</summary>
    public bool AutoCheck { get; set; }

    /// <summary>Автоматическая установка найденного обновления.</summary>
    public bool AutoUpdate { get; set; }

    /// <summary>Время суточной проверки, «ЧЧ:ММ» местного времени (умолчание 02:00).</summary>
    public string Time { get; set; } = "02:00";

    /// <summary>Репозиторий выпусков; пусто — адрес по умолчанию.</summary>
    public string Url { get; set; } = "";

    /// <summary>Страница выпусков для человека (только чтение).</summary>
    public string ReleasesUrl { get; set; } = "";

    /// <summary>Сервер запущен службой ОС (T-271): от этого зависит, откуда берётся
    /// суточная проверка — из расписания (служба) или со старта (консоль).</summary>
    public bool IsService { get; set; }

    /// <summary>Код записи расписания «проверять обновления» (SCH-7); пусто — записи нет.</summary>
    public string CheckScheduleCode { get; set; } = "";

    /// <summary>Код записи расписания «обновляться» (SCH-8); пусто — записи нет.</summary>
    public string UpdateScheduleCode { get; set; } = "";
}

/// <summary>
/// ОТВЕТ ПРОВЕРКИ ОБНОВЛЕНИЙ (T-208). Отказ — не исключение, а поле <see cref="Error"/>:
/// сеть недоступна и репозиторий не отвечает ЧАСТО, и «красная плашка» на весь экран
/// вместо строки под кнопкой была бы неправдой о важности события.
/// </summary>
public sealed class UpdateCheckDto
{
    /// <summary>Версия, которая работает сейчас.</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>Найдена версия новее текущей.</summary>
    public bool Available { get; set; }

    /// <summary>Найденная версия (1.134); пусто — новее ничего нет.</summary>
    public string Version { get; set; } = "";

    /// <summary>Имя файла выпуска, который подходит этой установке.</summary>
    public string AssetName { get; set; } = "";

    /// <summary>Адрес файла выпуска.</summary>
    public string AssetUrl { get; set; } = "";

    /// <summary>Страница выпуска на GitHub.</summary>
    public string ReleaseUrl { get; set; } = "";

    /// <summary>Момент проверки (UTC).</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>Почему проверить не удалось; пусто — проверка прошла.</summary>
    public string Error { get; set; } = "";

    /// <summary>Способ установки этой копии: полная выкладка (рантайм внутри).</summary>
    public bool Full { get; set; }

    /// <summary>Система и архитектура, по которым отбирался файл (для строки под кнопкой).</summary>
    public string Platform { get; set; } = "";
}

/// <summary>
/// ЧТО ОСТАНОВИТ ОБНОВЛЕНИЕ (T-208): обновление перезапускает сервер, поэтому работающие
/// на нём агенты будут сняты. Список тот же, что у смены организации (T-188), только
/// собранный по ВСЕМ открытым организациям этого сервера — перезапуск не выбирает.
/// </summary>
public sealed class UpdateAgentsDto
{
    /// <summary>Занятые агенты: организация и задача.</summary>
    public List<string> Agents { get; set; } = [];
}

/// <summary>Обновление запущено (T-208): что именно происходит — человеку в ответ.</summary>
public sealed class UpdateStartDto
{
    /// <summary>Имя скачанного файла установки.</summary>
    public string AssetName { get; set; } = "";

    /// <summary>Версия, которая ставится.</summary>
    public string Version { get; set; } = "";

    /// <summary>Сервер перезапустится сам (служба ОС) — человеку не нужно ничего делать.</summary>
    public bool Restarts { get; set; }
}

/// <summary>
/// НАСТРОЙКИ ПОЧТЫ ДЛЯ УВЕДОМЛЕНИЙ (T-272) — раздел <c>mail</c> в config.json.
/// Пароль наружу НЕ отдаётся: в ответе только признак «пароль задан»
/// (<see cref="HasPassword"/>), а в запросе — новое значение либо null («не менять»).
/// </summary>
public sealed class MailSettingsDto
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "AI2P";

    /// <summary>Пароль SMTP в хранилище секретов задан (само значение не отдаётся).</summary>
    public bool HasPassword { get; set; }

    /// <summary>Новый пароль SMTP; null — не менять, пустая строка — стереть.</summary>
    public string? Password { get; set; }

    /// <summary>Где лежит пароль — путь файла секрета; показывается человеку, чтобы
    /// значение можно было положить руками (T-234).</summary>
    public string PasswordFile { get; set; } = "";
}

/// <summary>Макроподстановка справочника уведомлений (T-272): код, вид в тексте, пояснение.</summary>
public sealed class NotificationMacroDto
{
    public string Code { get; set; } = "";
    /// <summary>Как макрос пишется в тексте: <c>${task.title}</c>.</summary>
    public string Marker { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Готовность транспорта уведомлений (T-272): настроен ли почтовый сервер.</summary>
public sealed class NotificationTransportDto
{
    public string Transport { get; set; } = "";
    public bool Ready { get; set; }
    /// <summary>Почему не готов; пусто — готов.</summary>
    public string Reason { get; set; } = "";
}

/// <summary>Запрос пробного письма (T-272): адрес; пусто — на свой собственный.</summary>
public sealed class NotificationTestDto
{
    public string Address { get; set; } = "";
}

/// <summary>Пробное письмо отправлено — на какой адрес (T-272).</summary>
public sealed class NotificationTestResultDto
{
    public string Address { get; set; } = "";
}

/// <summary>
/// СОЗДАНИЕ АРХИВА (T-40-S0, выпуск 1.105): код и имя задаёт человек, способ переноса
/// правил архивации — <see cref="AI2P.Core.ArchiveRuleModes"/>. Новый архив всегда
/// становится текущим, а предыдущий текущий — просто открытым; работает только на дирижёре.
/// </summary>
public sealed class ArchiveCreateDto
{
    /// <summary>Код архива — имя каталога и .zip (латиница, цифры, дефис, подчёркивание).</summary>
    public string Code { get; set; } = "";

    /// <summary>Имя архива для человека.</summary>
    public string Name { get; set; } = "";

    /// <summary>Способ переноса правил архивации (<see cref="AI2P.Core.ArchiveRuleModes"/>);
    /// пусто — общие правила организации.</summary>
    public string RulesMode { get; set; } = AI2P.Core.ArchiveRuleModes.Common;
}

/// <summary>
/// ПЕРЕНОС ПРАВИЛ АРХИВАЦИИ в архив (T-41-S0) — то же, что делает создание архива, но
/// отдельным вызовом: им работает кнопка «вернуть умолчание» на форме правил и им же форма
/// создания архива может перенести правила, если завела архив своим путём.
/// </summary>
public sealed class ArchiveRulesCopyDto
{
    /// <summary>Способ переноса (<see cref="AI2P.Core.ArchiveRuleModes"/>): общие правила
    /// организации, копия правил другого архива либо без правил.</summary>
    public string Mode { get; set; } = AI2P.Core.ArchiveRuleModes.Common;

    /// <summary>Архив-источник — нужен только способу «скопировать правила прежнего
    /// текущего архива»; пусто — копировать не с чего.</summary>
    public string? SourceArchiveId { get; set; }
}

/// <summary>
/// ПЕРЕНОС ДАННЫХ В АРХИВ И ОБРАТНО (T-42-S0): что переносим и какую именно запись.
/// Архив не назван вовсе — перенос всегда идёт в ТЕКУЩИЙ архив, и восстановление тоже
/// только из него.
/// </summary>
public sealed class ArchiveMoveDto
{
    /// <summary>Вид переноса (<see cref="AI2P.Core.ArchiveMoveTargets"/>): задача, шаблон,
    /// объект, опыт, проект целиком.</summary>
    public string Target { get; set; } = AI2P.Core.ArchiveMoveTargets.Task;

    /// <summary>Идентификатор записи; у задачи и шаблона — ВЕРХНЕГО УРОВНЯ (иерархия
    /// архивируется целиком).</summary>
    public string Id { get; set; } = "";
}

/// <summary>
/// ЧТО УЕДЕТ В АРХИВ (T-44-S0, выпуск 1.105) — ответ <c>GET /api/archives/movable</c>:
/// те же проверки, что у переноса, но без переноса. Форма ручной архивации показывает по
/// нему состав ДО нажатия кнопки («вместе с задачей уедут её подзадачи — столько-то»), а
/// отказ («задача в работе», «шаблон занят расписанием») приходит обычной ошибкой 400 с
/// готовым текстом причины.
///
/// Поля повторяют <c>ArchiveTransferService.ArchiveTransferResult</c> (слой хранилища):
/// экран видит только AI2P.Core, а имена совпадают, и разбор JSON идёт как есть.
/// </summary>
public sealed class ArchiveMovableDto
{
    /// <summary>Вид переноса (<see cref="AI2P.Core.ArchiveMoveTargets"/>).</summary>
    public string Target { get; set; } = "";

    /// <summary>Запись, о которой спрашивали.</summary>
    public string Id { get; set; } = "";

    /// <summary>Текущий архив, в который пойдёт перенос.</summary>
    public string ArchiveId { get; set; } = "";

    /// <summary>Его код.</summary>
    public string ArchiveCode { get; set; } = "";

    /// <summary>Задачи и узлы шаблонов, которые уедут (вместе с корнем).</summary>
    public List<string> Tasks { get; set; } = [];

    /// <summary>Объекты проектов.</summary>
    public List<string> Objects { get; set; } = [];

    /// <summary>Записи опыта.</summary>
    public List<string> Experience { get; set; } = [];

    /// <summary>Проекты (у переноса проекта целиком).</summary>
    public List<string> Projects { get; set; } = [];
}

/// <summary>
/// КАНДИДАТ НА АРХИВАЦИЮ (T-43-S0) — то, что отдаёт <c>GET /api/archives/{id}/candidates</c>
/// (отбор живёт в <c>ArchiveCandidateService</c>, слой хранилища). Здесь он повторён формой
/// записи, потому что экран видит только AI2P.Core: форме добавления архива надо показать,
/// что у прежнего текущего архива осталось неархивированным, и запустить перенос.
/// </summary>
public sealed class ArchiveCandidateDto
{
    /// <summary>Вид данных правила (<see cref="AI2P.Core.ArchiveRuleTargets"/>).</summary>
    public string Target { get; set; } = "";

    public string Id { get; set; } = "";

    /// <summary>Номер записи (T-15, OBJ-3); у видов без номера — пусто.</summary>
    public string DisplayId { get; set; } = "";

    /// <summary>Как запись зовётся на экране.</summary>
    public string Title { get; set; } = "";

    /// <summary>Дата, по которой считался возраст.</summary>
    public DateTime Date { get; set; }

    /// <summary>Сколько потомков уедет вместе с записью.</summary>
    public int Children { get; set; }
}

/// <summary>
/// ИТОГ АВТОМАТИЧЕСКОЙ АРХИВАЦИИ (T-46-S0, выпуск 1.105) — ответ <c>POST /api/archives/auto</c>
/// и то же самое, что уходит в журнал приложения.
///
/// Читателей у сводки двое, и обоим нужно одно и то же: расписание на дирижёре пишет её
/// в журнал, а форма добавления архива показывает человеку прямо в окне. Поэтому сводка
/// собирается на сервере (там же, где известен язык установки и коды видов данных), а не
/// пересказывается заново каждым читателем.
/// </summary>
public sealed class AutoArchiveResultDto
{
    /// <summary>Архивация ШЛА: правила отобраны, перенос выполнен. false — отказ, и тогда
    /// причина лежит в <see cref="Reason"/> (не дирижёр, нет текущего архива, он закрыт).</summary>
    public bool Ran { get; set; }

    /// <summary>Почему архивация не пошла; пусто — она шла.</summary>
    public string Reason { get; set; } = "";

    public string ArchiveId { get; set; } = "";

    /// <summary>Код текущего архива, в который шёл перенос.</summary>
    public string ArchiveCode { get; set; } = "";

    /// <summary>Сколько правил архива участвовало в отборе (только включённые).</summary>
    public int Rules { get; set; }

    /// <summary>Сколько записей отобрали правила.</summary>
    public int Candidates { get; set; }

    /// <summary>Сколько записей перенесено (с потомками и файлами).</summary>
    public int Moved { get; set; }

    /// <summary>Сколько пропущено: вид данных поодиночке не переносится.</summary>
    public int Skipped { get; set; }

    /// <summary>Сколько не удалось перенести (запись занята работой, исчезла и т. п.).</summary>
    public int Failed { get; set; }

    /// <summary>Сколько строк базы уехало в архив.</summary>
    public int Rows { get; set; }

    /// <summary>Сколько файлов уехало или скопировалось в архив.</summary>
    public int Files { get; set; }

    /// <summary>Готовая фраза для человека и для журнала — на языке установки.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Что именно случилось с каждой отобранной записью.</summary>
    public List<AutoArchiveItemDto> Items { get; set; } = [];
}

/// <summary>Одна запись в итоге автоматической архивации (T-46-S0).</summary>
public sealed class AutoArchiveItemDto
{
    /// <summary>Вид данных правила (<see cref="AI2P.Core.ArchiveRuleTargets"/>).</summary>
    public string Target { get; set; } = "";

    public string Id { get; set; } = "";

    /// <summary>Номер записи (T-15, OBJ-3); у видов без номера — пусто.</summary>
    public string DisplayId { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Запись уехала в архив.</summary>
    public bool Moved { get; set; }

    /// <summary>Почему не уехала; пусто — уехала.</summary>
    public string Error { get; set; } = "";
}

/// <summary>
/// Состояние установки локальной модели (ТЗ v1.40, todo36_3): манифест install из профайла
/// + фактические файлы в репозитории моделей. «Установлена» = все файлы манифеста на месте
/// с точными размерами и все необходимые пакеты установлены (ТЗ v1.42, todo36_5).
/// </summary>
public sealed class ModelInstallStatusDto
{
    public string ModelId { get; set; } = "";
    /// <summary>В профайле модели есть секция install (модель ставится локально).</summary>
    public bool HasManifest { get; set; }
    /// <summary>Установлено всё: и пакеты, и файлы весов.</summary>
    public bool Installed { get; set; }
    /// <summary>Пакеты, необходимые модели для запуска (ТЗ v1.42): ComfyUI, llama.cpp, …
    /// Пакеты ВЫКЛЮЧЕННЫХ дополнительных опций (T-190-S0) сюда не попадают вовсе.</summary>
    public List<ModelPackageStatusDto> Packages { get; set; } = [];
    /// <summary>Дополнительные опции установки (T-190-S0): «Обучение LoRA» и подобные.
    /// Пусто — у модели ставится только обязательное.</summary>
    public List<ModelInstallOptionDto> Options { get; set; } = [];
    /// <summary>Команда запуска, прописанная в профайл модели после установки (ТЗ v1.42);
    /// пусто — запускать нечего либо ещё не установлено.</summary>
    public string LaunchCommand { get; set; } = "";
    /// <summary>Загрузка идёт прямо сейчас.</summary>
    public bool Running { get; set; }
    /// <summary>Каталог, куда кладутся файлы: &lt;репозиторий&gt;/models/&lt;группа&gt;.</summary>
    public string TargetDir { get; set; } = "";
    public long TotalSize { get; set; }
    /// <summary>Сколько байт уже лежит на диске (частичные файлы учитываются — докачка).</summary>
    public long DownloadedSize { get; set; }
    /// <summary>Файл, который качается сейчас (при running).</summary>
    public string CurrentFile { get; set; } = "";
    /// <summary>Текст ошибки последней загрузки; пусто — ошибок нет.</summary>
    public string Error { get; set; } = "";
    public List<ModelInstallFileDto> Files { get; set; } = [];
}

/// <summary>
/// ДОПОЛНИТЕЛЬНАЯ ОПЦИЯ УСТАНОВКИ МОДЕЛИ (T-190-S0): набор пакетов, без которых модель
/// работает, но чего-то не умеет. Сегодня такая опция одна — «Обучение LoRA»: адаптеры
/// в кластере обучают обычно на ОДНОМ компьютере, а на остальных только применяют, и
/// тащить туда тренер с 18 ГБ базовых весов незачем. По умолчанию включены все.
/// </summary>
public sealed class ModelInstallOptionDto
{
    /// <summary>Код опции: lora. Название берётся из словаря — ключ install.option.&lt;код&gt;.</summary>
    public string Id { get; set; } = "";
    /// <summary>Опция включена на ЭТОМ сервере (флажок в окне установки).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Сколько пакетов добавляет опция — видно в подписи флажка.</summary>
    public int Packages { get; set; }
}

/// <summary>Файл манифеста установки: имя, ожидаемый размер, сколько скачано.</summary>
public sealed class ModelInstallFileDto
{
    public string Name { get; set; } = "";
    /// <summary>Источник: откуда файл качается (T-256-S0, строка «источник» подсказки «i»).</summary>
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public long Downloaded { get; set; }
    /// <summary>done — размер совпал; partial — есть частично (докачаем); missing — файла нет.</summary>
    public string State { get; set; } = "missing";
}

/// <summary>
/// Состояние пакета, необходимого модели для запуска (ТЗ v1.42, todo36_5): ComfyUI для
/// медиа-моделей, llama.cpp для GGUF-моделей. Пакеты общие: один и тот же пакет разных
/// моделей ставится один раз, дальше только проверяется.
/// </summary>
public sealed class ModelPackageStatusDto
{
    /// <summary>Код пакета из справочника пакетов: comfyui, llama.cpp, …</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Installed { get; set; }
    /// <summary>Каталог установки пакета: &lt;корень пакетов&gt;/&lt;каталог пакета&gt;.</summary>
    public string Dir { get; set; } = "";
    /// <summary>Подсказка на случай, если автоматическая установка не удалась (что скачать руками).</summary>
    public string Hint { get; set; } = "";
    /// <summary>Пакета нет в справочнике пакетов (повреждён или не для этой ОС).</summary>
    public bool Unknown { get; set; }
    /// <summary>Пакет уже стоит НА ЭТОМ КОМПЬЮТЕРЕ и качать его не надо (T-4-S0): так
    /// бывает с Python. Тогда в <see cref="Dir"/> лежит путь найденной программы.</summary>
    public bool System { get; set; }
    /// <summary>Размер дистрибутивов пакета в байтах (ТЗ v1.43, todo36_6); 0 — ещё не выяснен
    /// (выясняется запросом к серверу раздачи: POST /api/models/{id}/install/resolve).</summary>
    public long Size { get; set; }
    /// <summary>Сколько байт дистрибутива уже скачано; у установленного пакета — весь размер.</summary>
    public long Downloaded { get; set; }
    /// <summary>done — пакет установлен; partial — дистрибутив качается либо скачан; missing — ничего нет.</summary>
    public string State { get; set; } = "missing";
    /// <summary>Пакет ставится прямо сейчас (крутилка в окне установки).</summary>
    public bool Running { get; set; }
    /// <summary>Код дополнительной опции, которая привела этот пакет (T-190-S0); пусто —
    /// пакет обязательный. Выключенные опции своих пакетов в список не отдают вовсе.</summary>
    public string Option { get; set; } = "";

    /// <summary>Дистрибутивы пакета для подсказки «i» окна установки (T-238-S0): точные
    /// имена файлов, их размеры и лежат ли они в каталоге дистрибутивов. Пусто — у пакета
    /// нет файлов (системный) либо имя дистрибутива ещё неизвестно.</summary>
    public List<ModelPackageDistDto> Dists { get; set; } = [];
}

/// <summary>
/// ДИСТРИБУТИВ ПАКЕТА (T-238-S0): то, что скачивается в каталог дистрибутивов и оттуда
/// ставится. Показывается подсказкой «i» у строки пакета — человеку нужно точное имя
/// файла, чтобы положить его руками, когда автоматическая загрузка не удалась.
/// </summary>
public sealed class ModelPackageDistDto
{
    /// <summary>Точное имя файла дистрибутива (может содержать подкаталог).</summary>
    public string Name { get; set; } = "";
    /// <summary>Источник дистрибутива (T-256-S0): прямая ссылка, а до разбора ссылок у ассета
    /// релиза GitHub — страница последнего релиза репозитория; пусто — неизвестен.</summary>
    public string Url { get; set; } = "";
    /// <summary>Размер файла, байт; 0 — ещё не выяснен.</summary>
    public long Size { get; set; }
    /// <summary>Сколько уже лежит в каталоге дистрибутивов.</summary>
    public long Downloaded { get; set; }
    /// <summary>Файл виден в каталоге дистрибутивов.</summary>
    public bool Present { get; set; }
}

/// <summary>
/// Состояние ключа API модели (ТЗ v1.42, todo36_5): облачная модель без ключа не может быть
/// активной. Само значение ключа наружу не отдаётся никогда — только признак «ключ есть».
/// </summary>
public sealed class ModelKeyStatusDto
{
    public string ModelId { get; set; } = "";
    /// <summary>Модели нужен ключ API: облачная, подключение по API (не CLI), secretRef заполнен.</summary>
    public bool Required { get; set; }
    public bool HasKey { get; set; }
    /// <summary>Ссылка на ключ в секретах (anthropic.apiKey и т. п.) — показывается пользователю.</summary>
    public string SecretRef { get; set; } = "";
    /// <summary>Где хранится ключ (организация / файл секретов / переменная окружения) —
    /// для подсказки в форме.</summary>
    public string Source { get; set; } = "";

    /// <summary>Полный путь файла ключа НА ЭТОМ КОМПЬЮТЕРЕ (подкаталог <c>secrets/</c>,
    /// по одному json на ссылку; T-234). Показывается в форме всегда — и когда ключа нет:
    /// это ответ на вопрос «куда положить ключ руками». Файла по этому пути может не быть:
    /// ключ, введённый в форме, живёт в БД организации и файла не создаёт.</summary>
    public string KeyFile { get; set; } = "";

    /// <summary>Этот сервер получил ключ организации (ТЗ гл. 10, этап 45). Без него ключи API
    /// нечем ни записать, ни прочитать: они лежат в БД организации зашифрованными.</summary>
    public bool HasOrgKey { get; set; } = true;
}

/// <summary>
/// Документ модели из поставляемой документации (ТЗ гл. 14, todo47): открывается кнопкой
/// «i» в форме модели. Текст — Markdown; язык выбирается интерфейсом с откатом на русский,
/// затем на английский, поэтому кнопка не приводит на пустой экран, пока перевода нет.
/// </summary>
public sealed class ModelDocDto
{
    public string ModelName { get; set; } = "";
    /// <summary>Имя файла документа: &lt;название модели&gt;.md.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Документ найден; false — показывается подсказка, куда его положить.</summary>
    public bool Found { get; set; }
    /// <summary>Язык, на котором документ нашёлся (может отличаться от языка интерфейса).</summary>
    public string Language { get; set; } = "";
    /// <summary>Полный путь до файла — показывается в подсказке и при отсутствии документа.</summary>
    public string Path { get; set; } = "";
    /// <summary>Текст документа (Markdown); пусто — не найден.</summary>
    public string Text { get; set; } = "";
    /// <summary>Пояснение, если документа нет: куда его положить либо что пошло не так.</summary>
    public string Hint { get; set; } = "";
}

/// <summary>
/// СТРАНИЦА ПОСТАВЛЯЕМОЙ ДОКУМЕНТАЦИИ (T-17-S1): чтение документации отдельной закладкой,
/// начиная с <c>doc/&lt;язык&gt;/README.md</c>. В отличие от документа модели, страница
/// названа ПУТЁМ внутри языкового каталога (<c>models/README.md</c>) — по такому пути
/// ходят внутренние ссылки самих документов.
/// </summary>
public sealed class DocPageDto
{
    /// <summary>Путь страницы внутри языкового каталога, разделитель «/»: то, что найдено
    /// (а не то, что спросили): пустой запрос приводит на <c>README.md</c>.</summary>
    public string RelPath { get; set; } = "";
    /// <summary>Страница найдена; false — показывается подсказка <see cref="Hint"/>.</summary>
    public bool Found { get; set; }
    /// <summary>Язык, на котором страница нашлась (может отличаться от языка интерфейса).</summary>
    public string Language { get; set; } = "";
    /// <summary>Полный путь файла на диске — показывается под текстом и в подсказке.</summary>
    public string Path { get; set; } = "";
    /// <summary>Текст страницы (Markdown); пусто — не найдена.</summary>
    public string Text { get; set; } = "";
    /// <summary>Пояснение, если страницы нет: где её искали либо что пошло не так.</summary>
    public string Hint { get; set; } = "";
}

/// <summary>Установка/обновление ключа API модели (ТЗ v1.42): старое значение не показывается.</summary>
public sealed class ModelKeySaveDto
{
    public string Value { get; set; } = "";
}

/// <summary>
/// Состояние ключа и токена источника импорта (ТЗ v1.65, T-123). Значения наружу не отдаются
/// никогда — только признак «заполнено» и источник значения. Оба заполнены — источник может
/// быть активным; последнее из двух введённое значение включает его само.
/// </summary>
public sealed class ImportKeyStatusDto
{
    public string SourceId { get; set; } = "";
    /// <summary>Ссылка на ключ в хранилище секретов (<c>trello.apiKey</c> и т. п.).</summary>
    public string KeyRef { get; set; } = "";
    /// <summary>Ссылка на токен в хранилище секретов (<c>trello.token</c> и т. п.).</summary>
    public string TokenRef { get; set; } = "";
    public bool HasKey { get; set; }
    public bool HasToken { get; set; }
    /// <summary>Где лежит ключ (организация / файл секретов / переменная окружения).</summary>
    public string KeySource { get; set; } = "";
    /// <summary>Где лежит токен — то же для токена.</summary>
    public string TokenSource { get; set; } = "";
    /// <summary>Этот сервер получил ключ организации (ТЗ гл. 10): без него значения
    /// ни записать, ни прочитать — они лежат в БД организации зашифрованными.</summary>
    public bool HasOrgKey { get; set; } = true;
    /// <summary>Источник импорта активен (значение из справочника после возможной
    /// автоматической активации).</summary>
    public bool IsActive { get; set; }
    /// <summary>
    /// У этого вида источника есть ОТДЕЛЬНЫЙ ключ API (T-249). У Trello их два — ключ
    /// интеграции и токен, — а у GitLab и GitHub одно значение: личный токен доступа.
    /// Поэтому форма источника показывает кнопку ключа только здесь, и активность
    /// источника-однозначника решает один токен.
    /// </summary>
    public bool NeedsKey { get; set; } = true;
    /// <summary>Замечание к ключу (T-134): пусто — вопросов нет. Заполнено, когда значение
    /// не похоже на ключ API Trello — например, в поле ключа введён токен. Самого значения
    /// в замечании нет, только его длина и набор знаков.</summary>
    public string KeyHint { get; set; } = "";
    /// <summary>То же для токена (T-134).</summary>
    public string TokenHint { get; set; } = "";
}

/// <summary>
/// Итог проверки подключения источника импорта (T-134): кнопка «Проверить подключение»
/// в форме источника. Ключ и токен уезжают во внешнюю систему, ответ — вердикт словами:
/// принят ключ, принят токен, чей это аккаунт либо что именно не так.
/// </summary>
public sealed class ImportCheckDto
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Установка ключа либо токена источника импорта (ТЗ v1.65, T-123): одно поле
/// за раз, старое значение не показывается.</summary>
public sealed class ImportKeySaveDto
{
    public string Value { get; set; } = "";
}

/// <summary>Создание проекта (гл. 11, форма ввода проекта).</summary>
public sealed class ProjectCreateDto
{
    public string Name { get; set; } = "";
    public string? FolderPath { get; set; }
    public string? DefaultTeamId { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Цена ↔ качество при автоподборе исполнителя (ТЗ v1.26):
    /// 0.0 — приоритет цене, 1.0 — приоритет качеству.</summary>
    public double QualityBias { get; set; } = 0.5;
    /// <summary>В каком виде кнопка MD-редактора ставит ссылку на объект проекта (T-267):
    /// <c>code</c> — <c>@obj:OBJ-3</c>, <c>name</c> — <c>@obj:[Герой Вася]</c>.</summary>
    public string ObjRefFormat { get; set; } = ObjectRefFormats.Default;
    /// <summary>Ответственный по умолчанию (T-5-S0): подставляется в поле «ответственный»
    /// новой задачи проекта. Пусто — поле остаётся незаполненным, как раньше.</summary>
    public string? DefaultResponsibleId { get; set; }
    /// <summary>«Время ↔ качество» по умолчанию (T-23-S0): 0.0 — минимум времени и минимум
    /// качества, 1.0 — максимум качества и максимум времени. Подставляется в новую задачу
    /// проекта и берётся у задачи, где поле не заполнено.</summary>
    public double TimeQuality { get; set; } = ProjectSettings.TimeQualityDefault;
    /// <summary>Предел подстановки опыта в промпт задания (T-29-S0), знаков — суммарно по
    /// трём блокам опыта. Не больше нуля — умолчание.</summary>
    public int ExperienceLimitChars { get; set; } = ProjectSettings.ExperienceLimitDefault;
    /// <summary>Предел кругов повторной проверки (T-31-S0): сколько раз задача, гоняющая
    /// тесты за всю ветку, может вернуть соседей в доработку и уйти ждать их. Не больше
    /// нуля — умолчание.</summary>
    public int RecheckLimit { get; set; } = ProjectSettings.RecheckLimitDefault;
    /// <summary>Длительность задачи по умолчанию, часы (T-132-S0): ширина квадрата на
    /// диаграмме подзадач у задачи без плановой длительности. Не больше нуля — умолчание.</summary>
    public double DefaultTaskHours { get; set; } = ProjectSettings.DefaultTaskHoursDefault;
}

/// <summary>Кандидат автоподбора исполнителя (ТЗ v1.26) — для пояснения выбора.</summary>
public sealed class PickCandidateDto
{
    public string ExecutorId { get; set; } = "";
    public string Nick { get; set; } = "";
    public ExecutorKind Kind { get; set; }
    /// <summary>Среднее владение требуемыми skills по декларации (0–100).</summary>
    public double Quality { get; set; }
    /// <summary>Стоимость из декларации: in_per_1m + out_per_1m, USD за 1M токенов.</summary>
    public double CostPer1M { get; set; }
    /// <summary>Итоговый балл с учётом цена↔качество проекта (0–1).</summary>
    public double Score { get; set; }
}

/// <summary>Результат автоподбора исполнителя под skills задачи (ТЗ v1.26).</summary>
public sealed class PickedExecutorDto
{
    /// <summary>null — исполнитель не подобран (см. Reason).</summary>
    public string? ExecutorId { get; set; }
    public string Nick { get; set; } = "";
    public ExecutorKind Kind { get; set; }
    /// <summary>Пояснение выбора либо причина «не подобран».</summary>
    public string Reason { get; set; } = "";
    public List<PickCandidateDto> Candidates { get; set; } = [];

    /// <summary>
    /// СУФЛЁР подобранного исполнителя (T-292-S0): пусто — не нужен либо не нашёлся.
    /// Если у рабочей модели в профайле стоит «нужен суфлёр», подбор ищет его сам —
    /// сначала в самой записи исполнителя, а нет там — среди исполнителей команды
    /// с навыком <c>analyze-data</c>: без суфлёра такая задача не запустится вовсе.
    /// </summary>
    public string? PrompterExecutorId { get; set; }

    /// <summary>Ник подобранного суфлёра — для сообщения человеку.</summary>
    public string PrompterNick { get; set; } = "";
}

/// <summary>Загрузка файла (картинка из буфера обмена в MD-редакторе, гл. 11).</summary>
public sealed class FileUploadDto
{
    public string? ProjectId { get; set; }
    public string FileName { get; set; } = "";
    public string DataBase64 { get; set; } = "";
}

public sealed class FileUploadResultDto
{
    /// <summary>Относительный путь в dataDir — для ссылки в Markdown.</summary>
    public string Path { get; set; } = "";
}

/// <summary>Ссылка на файл из .md задачи или чата (кнопка «все файлы», ТЗ v1.28, todo30).</summary>
public sealed class TaskFileDto
{
    /// <summary>Отображаемое имя (имя файла из ссылки или сам URL).</summary>
    public string Name { get; set; } = "";
    /// <summary>Описание — текст в квадратных скобках перед ссылкой в .md (todo30_2).</summary>
    public string Description { get; set; } = "";
    /// <summary>URL ссылки как в тексте (относительный api/files/… или внешний).</summary>
    public string Url { get; set; } = "";
    /// <summary>Откуда ссылка: description / acceptance / chat / artifact / result.</summary>
    public string Source { get; set; } = "";
    /// <summary>Путь файла в хранилище AI2P (относительно dataDir); пусто — внешняя ссылка.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// ЛОКАЛЬНАЯ ссылка (T-142): относительный адрес нашего файлового эндпойнта, который
    /// отдаёт файл с диска ЭТОГО сервера. Заполняется, только если файл здесь ДЕЙСТВИТЕЛЬНО
    /// лежит: каталог проекта на каждом сервере свой, и то, что есть у одного, у другого
    /// может отсутствовать. Пусто — открывать надо по <see cref="Url"/>.
    ///
    /// Ссылка относительная нарочно: браузер достроит её тем адресом, по которому человек
    /// уже подключён, — файл поедет по петле, а не через сеть и обратно. Файлы бывают
    /// в гигабайты, и качать их по сети незачем.
    /// </summary>
    public string LocalUrl { get; set; } = "";

    /// <summary>
    /// ВНЕШНЯЯ ссылка (T-142): полный адрес по имени сервера из настроек — её отправляют
    /// тем, кто снаружи. У чужих (не наших) ссылок — сам исходный URL.
    /// </summary>
    public string ExternalUrl { get; set; } = "";
    /// <summary>Файл можно удалить прямо из окна (ТЗ v1.45, todo37_3): наш файл этой задачи
    /// (артефакты, вставки MD-редактора). Внешние ссылки и чужие файлы не удаляются.</summary>
    public bool CanDelete { get; set; }
}

/// <summary>
/// Консоль задания (ТЗ v1.45, todo37_3): «хвост» живого вывода работающего исполнителя.
/// Читается порциями по номеру последней прочитанной строки (NextSeq предыдущего ответа).
/// </summary>
public sealed class JobConsoleDto
{
    public string JobId { get; set; } = "";
    /// <summary>Задание ещё выполняется — UI продолжает дочитывать консоль.</summary>
    public bool Running { get; set; }
    /// <summary>Номер последней строки буфера — с ним приходить в следующий раз.</summary>
    public long NextSeq { get; set; }
    /// <summary>Сколько строк вытеснено из кольцевого буфера между чтениями (0 — ничего не потеряно).</summary>
    public long Lost { get; set; }
    public List<JobConsoleLineDto> Lines { get; set; } = [];
}

/// <summary>Строка консоли задания: сквозной номер, время и текст.</summary>
public sealed class JobConsoleLineDto
{
    public long Seq { get; set; }
    public DateTime Ts { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Запуск импорта в проект (ТЗ v1.28, todo30): источник + чекбоксы переспроса.</summary>
public sealed class ImportRunDto
{
    public string SourceId { get; set; } = "";
    public bool AddNew { get; set; } = true;
    public bool UpdateExisting { get; set; }
}

/// <summary>Итог импорта: сколько задач добавлено, обновлено и пропущено.</summary>
public sealed class ImportResultDto
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    /// <summary>Сколько сообщений обсуждения перенесено в чат задач (T-152).</summary>
    public int Comments { get; set; }
}

/// <summary>Импорт одиночного задания по URL (ТЗ v1.50, todo50): ссылка на карточку +
/// источник (пусто — единственный активный trello-источник).</summary>
public sealed class ImportCardDto
{
    public string Url { get; set; } = "";
    public string? SourceId { get; set; }
}

/// <summary>Содержимое импортированной карточки — подставляется в форму новой задачи (гл. 11):
/// файлы-вложения уже скачаны в uploads/ проекта, ссылки на них — в описании.</summary>
public sealed class ImportedCardDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime? DueDate { get; set; }
    /// <summary>«trello:&lt;cardId&gt;» — сохраняется в задаче для защиты от дублей импорта.</summary>
    public string ExternalRef { get; set; } = "";

    /// <summary>Ссылка импорта (T-246) — короткий адрес карточки: подставляется в задачу
    /// и по ней задачу потом обновляют из источника.</summary>
    public string ImportUrl { get; set; } = "";
    /// <summary>Имена скачанных файлов (для сообщения в UI).</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>Обсуждение карточки в хронологическом порядке (T-152): переносится
    /// в чат задачи сразу после её создания.</summary>
    public List<ChatImportMessageDto> Comments { get; set; } = [];
}

/// <summary>
/// Обновление задачи из источника импорта (T-246): POST /api/tasks/{id}/import/refresh.
/// Источник (запись справочника импортов) можно не указывать — берётся единственный
/// активный подходящий; ссылка не передаётся вовсе, она хранится в самой задаче.
/// </summary>
public sealed class ImportRefreshDto
{
    public string? SourceId { get; set; }
}

/// <summary>Итог обновления задачи из источника (T-246) — для сообщения в UI.</summary>
public sealed class ImportRefreshResultDto
{
    public string DisplayId { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Сколько НОВЫХ сообщений обсуждения приехало в чат задачи.</summary>
    public int Comments { get; set; }
    /// <summary>Сколько приложенных файлов скачано заново.</summary>
    public int Files { get; set; }
    /// <summary>Сколько РЕЗУЛЬТАТОВ задачи уехало обратно — записями в обсуждение
    /// источника (T-107-S0). Уже выложенные не повторяются.</summary>
    public int Exported { get; set; }
}

/// <summary>Сообщение обсуждения внешней системы (T-152) — перенос в чат задачи.</summary>
public sealed class ChatImportMessageDto
{
    /// <summary>Автор так, как он назван во внешней системе: «trello:&lt;логин&gt;».</summary>
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Время сообщения в источнике — по нему чат сохраняет хронологию.</summary>
    public DateTime Date { get; set; }
    /// <summary>Внешний id сообщения («trello:&lt;id действия&gt;») — защита от дублей.</summary>
    public string ExternalRef { get; set; } = "";
}

/// <summary>Перенос обсуждения в чат задачи (T-152): POST /api/tasks/{id}/chat/import.</summary>
public sealed class ChatImportDto
{
    public List<ChatImportMessageDto> Messages { get; set; } = [];
}

/// <summary>Запись текстового файла в dataDir — редакторы профайлов и деклараций (п. 2.9, гл. 11).</summary>
public sealed class FileTextDto
{
    /// <summary>Относительный путь в dataDir.</summary>
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// Статус работы команды (ТЗ v1.14, «Запуск работы команды»): признак «команда запущена»
/// + вычисляемый статус работы каждого участника.
/// </summary>
public sealed class TeamWorkStatusDto
{
    public string TeamId { get; set; } = "";
    public bool IsRunning { get; set; }
    public List<MemberWorkStatusDto> Members { get; set; } = [];
}

/// <summary>Статус работы участника команды: ИИ — подключение, человек — онлайн/офлайн.</summary>
public sealed class MemberWorkStatusDto
{
    public string ExecutorId { get; set; } = "";
    public string Nick { get; set; } = "";
    public ExecutorKind Kind { get; set; }
    public ExecutorWorkState State { get; set; }
    /// <summary>Текст ошибки подключения (ИИ, state = error).</summary>
    public string? ErrorText { get; set; }
}

/// <summary>
/// Запись биллинга (ТЗ v1.15, todo18): одно задание = одна строка сбора стоимости работы.
/// Позже сюда попадут и записи по людям (пока — стоимость 0, без токенов).
/// </summary>
public sealed class BillingRecordDto
{
    public string JobId { get; set; } = "";
    public string JobDisplayId { get; set; } = "";
    public string? ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string? TeamId { get; set; }
    public string TeamName { get; set; } = "";
    public string ExecutorId { get; set; } = "";
    public string ExecutorNick { get; set; } = "";
    public string InternalName { get; set; } = "";
    public ExecutorKind Kind { get; set; }
    public string? ModelId { get; set; }
    public string TaskDisplayId { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    /// <summary>Дата-время начала работы (UTC).</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>Длительность работы, секунды.</summary>
    public double DurationSeconds { get; set; }
    public double Cost { get; set; }
    public string Currency { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}

/// <summary>Элемент списка каталогов (диалог выбора папки проекта, гл. 11).</summary>
public sealed class DirEntryDto
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>Файл — картинка (по расширению): диалог выбора кадра датасета LoRA
    /// показывает такие уменьшенными (T-12-S1). У каталога всегда false.</summary>
    public bool IsImage { get; set; }

    /// <summary>Размер файла в байтах; у каталога — 0.</summary>
    public long Size { get; set; }
}

/// <summary>Содержимое каталога локального диска для диалога выбора папки.</summary>
public sealed class DirListDto
{
    public string Path { get; set; } = "";
    public string? Parent { get; set; }
    public List<DirEntryDto> Dirs { get; set; } = [];

    /// <summary>
    /// ФАЙЛЫ каталога (T-12-S1) — только когда их попросили (<c>files=true</c>): диалог
    /// выбора папки проекта показывает одни каталоги, а кадр датасета LoRA берут файлом
    /// «от корня файловой системы». Пустой список у старого вызова — прежнее поведение.
    /// </summary>
    public List<DirEntryDto> Files { get; set; } = [];

    /// <summary>Список обрезан по пределу (<see cref="AI2P.Core.ProjectFiles.Limit"/>):
    /// в каталоге бывают десятки тысяч файлов, а список рисуется целиком.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// ИСХОДНИК КАДРА ДАТАСЕТА LoRA (T-12-S1): откуда берётся картинка — по адресу в сети
/// либо файлом с диска этого компьютера. Ровно один из двух заполнен.
///
/// Зачем вообще промежуточный шаг «положить исходник», а не сразу «добавить кадр»: рамку
/// кропа человек тянет в БРАУЗЕРЕ, а исходник лежит либо на чужом сервере (браузеру его
/// не дадут прочитать в canvas — чужое происхождение), либо на диске сервера (браузер туда
/// не ходит вовсе). Поэтому сервер сначала кладёт исходник к себе в хранилище, отдаёт
/// ссылку на него — и только после кропа получает готовый кадр.
/// </summary>
public sealed class LoraStageDto
{
    /// <summary>Адрес картинки в сети (http/https).</summary>
    public string Url { get; set; } = "";

    /// <summary>Абсолютный путь файла на диске этого компьютера.</summary>
    public string LocalPath { get; set; } = "";

    /// <summary>
    /// ЧТО кладём в хранилище (T-250-S0): «image» либо «audio». От этого зависит, какие
    /// файлы вообще разрешено читать с диска: путь приходит из формы, а прочитанный файл
    /// отдаётся наружу ссылкой — список расширений здесь и есть защита.
    /// </summary>
    public string Media { get; set; } = LoraDatasetMedia.Image;
}

/// <summary>Исходник кадра, положенный в хранилище (T-12-S1).</summary>
public sealed class LoraStageResultDto
{
    /// <summary>Путь в хранилище (dataDir) — по нему браузер читает исходник
    /// через <c>api/files/raw</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>Имя исходного файла — из него предлагается имя кадра.</summary>
    public string Name { get; set; } = "";

    /// <summary>Размер исходника в байтах — показать человеку, из чего он режет.</summary>
    public long Size { get; set; }
}

/// <summary>
/// ДОБАВЛЕНИЕ КАДРА В ДАТАСЕТ (T-12-S1). Картинка приезжает уже обрезанной, сжатой и
/// переведённой в нужный формат — это делает браузер холстом (canvas): он и так рисует
/// рамку кропа, а второй раз декодировать картинку на сервере значило бы завести в проекте
/// целую библиотеку работы с изображениями ради того, что уже сделано.
///
/// Сервер при этом НЕ доверяет присланному: размер в пикселях он читает сам из заголовка
/// PNG, размер в килобайтах считает по байтам — и отказывает, если пределы настроек нарушены.
/// </summary>
public sealed class LoraDatasetAddDto
{
    /// <summary>Имя файла кадра (без каталога); расширение ставит сервер по формату настроек.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Подпись кадра — она уедет в обучение файлом <c>.txt</c> рядом с картинкой.</summary>
    public string Description { get; set; } = "";

    /// <summary>Сама картинка (base64), уже обрезанная и пережатая браузером.</summary>
    public string DataBase64 { get; set; } = "";

    /// <summary>Исходник в хранилище, который после добавления надо убрать; пусто — не убирать.</summary>
    public string StagePath { get; set; } = "";

    /// <summary>В какой датасет класть кадр (T-274); пусто — в текущий, а нет ни одного —
    /// он заводится сам.</summary>
    public string DatasetId { get; set; } = "";
}

/// <summary>
/// ПРЕДЕЛЫ КАДРА ДАТАСЕТА LoRA (T-12-S1) — из настроек приложения (config.json, раздел
/// <c>lora</c>). Читает их и форма добавления кадра (чтобы сжать в браузере), и сервер
/// (чтобы проверить присланное): одно значение, два читателя, разойтись им негде.
/// </summary>
public sealed class LoraImageLimitsDto
{
    public int MaxWidth { get; set; } = 1024;
    public int MaxHeight { get; set; } = 1024;

    /// <summary>Предел размера файла кадра в килобайтах.</summary>
    public int MaxKb { get; set; } = 2048;

    /// <summary>Формат, в который кадры переводятся: «png» либо «jpeg».</summary>
    public string Format { get; set; } = "png";

    /// <summary>
    /// ЧЕМ ЗАПОЛНЯТЬ ПОЛЯ при автоматическом пережатии кадра (T-57-S0) — «#RRGGBBAA».
    /// Соотношение сторон исходной картинки менять нельзя и обрезать её тоже нельзя,
    /// поэтому она вписывается в кадр целиком, а оставшиеся поля закрашиваются этим цветом.
    /// Умолчание «#FFFFFF00» — белый с полной прозрачностью: у PNG поля выходят прозрачными,
    /// а у JPEG, где альфы нет вовсе, — белыми.
    /// </summary>
    public string PadColor { get; set; } = "#FFFFFF00";
}

/// <summary>Заведение строки обучения: под какую модель обучать адаптер (T-12-S1).</summary>
public sealed class LoraModelAddDto
{
    public string ModelId { get; set; } = "";
}

// прежний LoraTrainStartDto (прямой запуск обучения, T-12-S1) убран в T-157-S0 вместе с самим
// прямым запуском: обучение идёт задачей, а ответы на переспросы приезжают полями
// LoraTrainTaskCreateDto

/// <summary>
/// ЧТО СПРОСИТЬ ПЕРЕД ОБУЧЕНИЕМ (T-274) — ответ на <c>GET …/train/check</c>. Форма по нему
/// решает, задать ли вопрос и какой; сервер те же условия проверяет сам при запуске, поэтому
/// пропустить вопрос, дёрнув запуск напрямую, нельзя.
/// </summary>
public sealed class LoraTrainCheck
{
    /// <summary>Датасет, по которому пойдёт обучение.</summary>
    public string DatasetId { get; set; } = "";

    public string DatasetName { get; set; } = "";

    /// <summary>Датасет, по которому обучали в прошлый раз; пусто — обучения не было.</summary>
    public string TrainedDatasetId { get; set; } = "";

    public string TrainedDatasetName { get; set; } = "";

    /// <summary>Датасет сменился с прошлого обучения — надо переспросить, по какому запускать.</summary>
    public bool DatasetChanged { get; set; }

    /// <summary>Сколько живых кадров в датасете.</summary>
    public int Frames { get; set; }

    /// <summary>Несовпадения настроек датасета с объявленными моделью — готовыми фразами;
    /// пусто значит «сверять было не с чем либо всё в порядке».</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Обучение не запустится вовсе (модель без LoRA, пустой датасет, незаданная
    /// команда) — текст отказа; пусто, если препятствий нет.</summary>
    public string Error { get; set; } = "";
}

/// <summary>Заведение датасета (T-274): название спрашивает форма, пусто — умолчание.</summary>
public sealed class LoraDatasetCreateDto
{
    public string Name { get; set; } = "";
}

/// <summary>Выбор текущего датасета объекта (T-274).</summary>
public sealed class LoraDatasetPickDto
{
    public string DatasetId { get; set; } = "";
}

/// <summary>
/// ЧЕМ ЗАПУСКАТЬ ОБУЧЕНИЕ (T-157-S0) — ответ на вопрос кнопки «Обучить» после проверки
/// датасета: какие ШАБЛОНЫ задач годятся, есть ли исполнитель «авто ПО» и есть ли вообще
/// плагин-тренер этой модели. Всё три сразу, одним запросом: форма по этому ответу решает,
/// показать список шаблонов, предложить завести шаблон, предложить завести исполнителя или
/// отправить в окно установки плагина.
/// </summary>
public sealed class LoraTrainOptionsDto
{
    /// <summary>Годные узлы шаблонов — те, у кого в исполнителях стоит подходящий «авто ПО».</summary>
    public List<LoraTrainTemplateDto> Templates { get; set; } = [];

    /// <summary>Подходящий исполнитель «авто ПО»; пусто — такого нет, его надо завести.</summary>
    public string ExecutorId { get; set; } = "";

    public string ExecutorNick { get; set; } = "";

    /// <summary>Код плагина-тренера этой модели (по пакетам <c>lora.train.packages</c>);
    /// пусто — плагина нет вовсе, и заводить исполнителя не из чего.</summary>
    public string PluginCode { get; set; } = "";

    public string PluginName { get; set; } = "";

    /// <summary>Плагин ГОТОВ работать на этом сервере: запись организации заведена и программа
    /// найдена. Не готов — форма отправляет человека в окно установки плагина.</summary>
    public bool PluginReady { get; set; }

    /// <summary>Почему обучать нечем (модель не работает с LoRA, пакеты не поставлены,
    /// тренера нет); пусто — всё в порядке.</summary>
    public string Error { get; set; } = "";
}

/// <summary>Узел шаблона, годный для обучения (T-157-S0): по нему создаётся задача, и из него
/// приезжают заголовок, описание, исполнитель, тэги и опыт узла.</summary>
public sealed class LoraTrainTemplateDto
{
    public string Id { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Ник исполнителя «авто ПО» этого узла — по нему человек и различает шаблоны.</summary>
    public string ExecutorNick { get; set; } = "";
}

/// <summary>Создание задачи обучения по шаблону (T-157-S0).</summary>
public sealed class LoraTrainTaskCreateDto
{
    /// <summary>Узел шаблона, по которому создаётся задача.</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>Датасет, по которому обучать: он же становится ТЕКУЩИМ датасетом объекта —
    /// задача запускается когда угодно, и запомненный в ней датасет мог бы к тому времени
    /// исчезнуть.</summary>
    public string DatasetId { get; set; } = "";

    /// <summary>«Создать и запустить» (иначе — «только создать»: задача ждёт в pending).</summary>
    public bool Run { get; set; }

    /// <summary>«Да» на переспрос «датасет не тот, по которому обучали» (T-274). Переспросы
    /// задаёт форма, а требует их СЕРВЕР: задача заводится и не из формы тоже.</summary>
    public bool ConfirmDataset { get; set; }

    /// <summary>«Да» на переспрос «настройки датасета крупнее объявленных моделью» (T-274).</summary>
    public bool ConfirmLimits { get; set; }

    /// <summary>«Да» на переспрос «переобучить»: у обученной строки файл адаптера уже мог быть
    /// подставлен в модель, и переобучение его затрёт.</summary>
    public bool Force { get; set; }
}

/// <summary>Созданная задача обучения (T-157-S0): её номер и есть ссылка в строке обучения.</summary>
public sealed class LoraTrainTaskDto
{
    public string TaskId { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Задача поставлена в работу (ответ «создать и запустить»).</summary>
    public bool Started { get; set; }

    /// <summary>Запустить не удалось (занят исполнитель, чужой сервер, правило безопасности) —
    /// задача при этом СОЗДАНА, и текст говорит, почему она стоит.</summary>
    public string StartError { get; set; } = "";
}

/// <summary>
/// ДАТАСЕТ В СПИСКЕ ФОРМЫ (T-274): сам объект-датасет, число кадров в нём и его настройки
/// контроля картинок разобранными — форме нужны все три, а лазить в JSON из разметки нечем.
/// </summary>
public sealed class LoraDatasetDto
{
    public string Id { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Это текущий датасет объекта.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Сколько в датасете кадров.</summary>
    public int Frames { get; set; }

    public LoraDatasetLimitsDto Limits { get; set; } = new();
}

/// <summary>
/// НАСТРОЙКИ КОНТРОЛЯ КАРТИНОК ДАТАСЕТА наружу (T-274) — то же, что
/// <see cref="LoraDatasetLimits"/>, отдельным DTO по общему правилу слоёв.
/// </summary>
public sealed class LoraDatasetLimitsDto
{
    /// <summary>Из чего собран датасет (T-250-S0): «image» либо «audio».</summary>
    public string Media { get; set; } = LoraDatasetMedia.Image;

    public int MaxWidth { get; set; } = 1024;
    public int MaxHeight { get; set; } = 1024;
    public int MaxKb { get; set; } = 2048;
    public string Format { get; set; } = "png";
    public int MinItems { get; set; }
    public int MaxItems { get; set; }

    /// <summary>Пределы ЗАПИСИ (T-250-S0): длительность, частота дискретизации, каналы.</summary>
    public int MinSeconds { get; set; }

    public int MaxSeconds { get; set; } = LoraDatasetLimits.DefaultMaxSeconds;

    public int SampleRate { get; set; } = LoraDatasetLimits.DefaultSampleRate;

    public int Channels { get; set; } = 2;

    /// <summary>Датасет собран из записей — форма показывает свои поля.</summary>
    [JsonIgnore]
    public bool IsAudio => LoraDatasetMedia.Normalize(Media) == LoraDatasetMedia.Audio;

    public static LoraDatasetLimitsDto Of(LoraDatasetLimits limits) => new()
    {
        Media = limits.Media,
        MaxWidth = limits.MaxWidth,
        MaxHeight = limits.MaxHeight,
        MaxKb = limits.MaxKb,
        Format = limits.Format,
        MinItems = limits.MinItems,
        MaxItems = limits.MaxItems,
        MinSeconds = limits.MinSeconds,
        MaxSeconds = limits.MaxSeconds,
        SampleRate = limits.SampleRate,
        Channels = limits.Channels,
    };

    public LoraDatasetLimits ToLimits() => new LoraDatasetLimits
    {
        Media = Media,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        MaxKb = MaxKb,
        Format = Format,
        MinItems = MinItems,
        MaxItems = MaxItems,
        MinSeconds = MinSeconds,
        MaxSeconds = MaxSeconds,
        SampleRate = SampleRate,
        Channels = Channels,
    }.Sane();
}

/// <summary>
/// ДАТАСЕТ ПОД ТРЕБОВАНИЯ МОДЕЛИ (T-57-S0) — ответ на «Создать/переключить на нужный
/// датасет» из переспроса о несовпадении настроек.
///
/// Сервер делает ровно то, что решить может только он: ищет среди УЖЕ ЗАВЕДЁННЫХ датасетов
/// объекта подходящий (настройки укладываются в объявленные моделью и кадры те же самые) и,
/// не найдя, заводит новый с настройками из модели. А вот пережимать картинки он не умеет:
/// библиотеки работы с изображениями в проекте нет намеренно (T-12-S1), и делает это холст
/// браузера — поэтому при <see cref="Found"/> = false сюда кладётся список кадров, которые
/// форме предстоит пережать и положить в новый датасет.
/// </summary>
public sealed class LoraDatasetFitDto
{
    /// <summary>Подходящий датасет уже был — он сделан текущим, пережимать нечего.</summary>
    public bool Found { get; set; }

    public string DatasetId { get; set; } = "";
    public string DatasetName { get; set; } = "";

    /// <summary>Настройки датасета: у найденного — его собственные, у заведённого — взятые
    /// из модели. По ним форма и ужимает картинки.</summary>
    public LoraDatasetLimitsDto Limits { get; set; } = new();

    /// <summary>Чем закрашивать поля — из настроек приложения (<c>lora.imagePadColor</c>).</summary>
    public string PadColor { get; set; } = "#FFFFFF00";

    /// <summary>Что пережать и положить в новый датасет; пусто, когда датасет нашёлся.</summary>
    public List<LoraFitFrameDto> Frames { get; set; } = [];
}

/// <summary>Кадр-исходник для пережатия (T-57-S0): откуда взять и как назвать.</summary>
public sealed class LoraFitFrameDto
{
    /// <summary>Путь картинки относительно ПАПКИ ПРОЕКТА — по нему форма строит ссылку.</summary>
    public string Path { get; set; } = "";

    /// <summary>Имя файла без расширения: расширение задаст формат нового датасета.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Подпись кадра — она уезжает в обучение и обязана переехать вместе с ним.</summary>
    public string Description { get; set; } = "";
}

/// <summary>
/// МОДЕЛЬ, ИЗ КОТОРОЙ МОЖНО ПОДСТАВИТЬ НАСТРОЙКИ (T-274) — строка списка кнопки
/// «подставить из модели». В список попадают только те модели, что уже стоят на закладке
/// «Модели» этого объекта И у которых в справочнике заполнены контрольные настройки:
/// подставлять из модели, которая о картинках ничего не сказала, нечего.
/// </summary>
public sealed class LoraModelLimitsDto
{
    public string ModelId { get; set; } = "";
    public string ModelDisplayId { get; set; } = "";
    public string ModelName { get; set; } = "";

    /// <summary>Что модель объявила требованием к кадрам — уже с выбранным форматом
    /// (PNG, если он есть среди названных).</summary>
    public LoraDatasetLimitsDto Limits { get; set; } = new();

    /// <summary>Форматы, которые модель принимает, — как они записаны в справочнике.</summary>
    public List<string> Formats { get; set; } = [];
}

/// <summary>
/// Элемент содержимого ПАПКИ ПРОЕКТА (T-264): диалог выбора эталонного файла объекта.
/// От <see cref="DirEntryDto"/> отличается тем, что путь здесь ОТНОСИТЕЛЬНЫЙ — ровно
/// в таком виде он и хранится у объекта, и на соседнем сервере тот же путь показывает
/// его собственную копию файла (то же правило, что у ссылок <see cref="FileLinks"/>).
/// </summary>
public sealed class ProjectEntryDto
{
    public string Name { get; set; } = "";

    /// <summary>Путь относительно папки проекта, разделитель «/».</summary>
    public string Path { get; set; } = "";

    /// <summary>Файл — картинка (по расширению): диалог показывает её уменьшенной.</summary>
    public bool IsImage { get; set; }

    /// <summary>Размер файла в байтах; у каталога — 0.</summary>
    public long Size { get; set; }
}

/// <summary>Содержимое одного каталога внутри папки проекта (T-264).</summary>
public sealed class ProjectDirListDto
{
    /// <summary>Текущий каталог относительно папки проекта; пусто — её корень.</summary>
    public string Path { get; set; } = "";

    /// <summary>Каталог на шаг вверх; null — мы в корне папки проекта, выше не пускаем.</summary>
    public string? Parent { get; set; }

    /// <summary>Абсолютный путь папки проекта НА ЭТОМ сервере — показать человеку, где он.</summary>
    public string Root { get; set; } = "";

    public List<ProjectEntryDto> Dirs { get; set; } = [];
    public List<ProjectEntryDto> Files { get; set; } = [];

    /// <summary>Список обрезан по пределу: в папке проекта бывают десятки тысяч файлов,
    /// и молча показать часть значило бы соврать.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Сведения о файле папки проекта (T-264): по ним форма объекта решает, показывать ли
/// превью. Спрашиваются АСИНХРОННО, уже после того как форма нарисована.
/// </summary>
public sealed class ProjectFileInfoDto
{
    public string Path { get; set; } = "";
    public bool Exists { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsImage { get; set; }
    public long Size { get; set; }
}

/// <summary>
/// ЧТО УЙДЁТ В МОДЕЛЬ вместо ссылки на объект (T-259). Отдаётся форме объекта, чтобы
/// паспорт персонажа проверяли ДО генерации: иначе непохожий кадр списывают на модель,
/// а причина — пустой или пересказанный своими словами паспорт.
/// </summary>
public sealed class ObjectPromptDto
{
    /// <summary>Готовая ссылка (<c>@obj:OBJ-3</c>) — её вставляют в описание задачи.</summary>
    public string Marker { get; set; } = "";

    /// <summary>Текст, который встанет вместо ссылки при запуске задания.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>
    /// ЧТО УЙДЁТ В МОДЕЛЬ ПРИ ОБУЧЕНИИ (T-99-S0) — у объекта-адаптера LoRA это СОВСЕМ ДРУГИЕ
    /// данные, чем при использовании: в промпт генерации уходит паспорт, а обучение получает
    /// кадры текущего датасета с их подписями (<c>LoraTrainService.PrepareDataset</c> кладёт
    /// рядом с каждой картинкой её подпись). Пусто у всех объектов, кроме адаптера, — тогда
    /// форма показывает одно поле, как раньше.
    /// </summary>
    public string TrainPrompt { get; set; } = "";
}

/// <summary>
/// ПЕРЕНОС ОБЪЕКТА ПО ИЕРАРХИИ (T-266) — представление «иерархия» списка объектов, тот же
/// приём, что у задач (<see cref="TaskParentDto"/>). Своя запись, а не общая с задачами:
/// у неё своё правило проверки (родитель — объект того же проекта) и своя точка входа.
/// </summary>
public sealed class ObjectParentDto
{
    /// <summary>Новый родитель; null или пусто — объект становится корневым.</summary>
    public string? ParentId { get; set; }
}

/// <summary>
/// СОСТОЯНИЕ ВХОДА В CLAUDE CLI на компьютере этого сервера (todo96, версия 1.96):
/// ответ <c>claude auth status --json</c> плюс признак «вход прямо сейчас идёт».
/// Показывается в настройках (Модели → «Вход в Claude CLI») и открывается само,
/// когда задание встало из-за истёкшего сеанса.
/// </summary>
public sealed class ClaudeAuthStatusDto
{
    /// <summary>CLI ответил понятным состоянием входа. false — спросить не удалось
    /// (CLI не установлен либо не знает команды <c>auth status</c>); это НЕ «входа нет».</summary>
    public bool Known { get; set; }

    public bool LoggedIn { get; set; }

    /// <summary>Почта вошедшего аккаунта Claude; пусто — CLI её не назвал.</summary>
    public string Email { get; set; } = "";

    /// <summary>Как вошли: claude.ai (подписка) либо console (оплата по API).</summary>
    public string Method { get; set; } = "";

    /// <summary>Вид подписки: max, pro, … Пусто — не назван.</summary>
    public string Subscription { get; set; } = "";

    /// <summary>Исполняемый файл CLI, у которого спрашивали (для подсказки в форме).</summary>
    public string Command { get; set; } = "";

    /// <summary>Вход уже начат и ждёт кода авторизации.</summary>
    public bool LoginRunning { get; set; }

    /// <summary>Сколько задач этого сервера стоят на паузе и ждут входа (todo96).</summary>
    public int WaitingTasks { get; set; }
}

/// <summary>
/// ХОД ВХОДА В CLAUDE CLI (todo96): ответ на «начать вход» и на «вот код авторизации».
/// Running — ссылка выдана, ждём кода; LoggedIn — вход выполнен; Error — не получилось.
/// </summary>
public sealed class ClaudeLoginStateDto
{
    /// <summary>Вход начат и ждёт кода авторизации.</summary>
    public bool Running { get; set; }

    /// <summary>Вход выполнен.</summary>
    public bool LoggedIn { get; set; }

    /// <summary>Ссылка авторизации: её надо открыть и вернуть оттуда код.</summary>
    public string Url { get; set; } = "";

    /// <summary>Что делать дальше — текст для человека.</summary>
    public string Message { get; set; } = "";

    /// <summary>Причина неудачи; пусто — всё в порядке.</summary>
    public string Error { get; set; } = "";

    /// <summary>Сколько задач, ждавших входа, отпущено после успешного входа (todo96).</summary>
    public int ReleasedTasks { get; set; }
}

/// <summary>Код авторизации Claude CLI, скопированный человеком со страницы входа (todo96).</summary>
public sealed class ClaudeLoginCodeDto
{
    public string Code { get; set; } = "";
}

/// <summary>
/// СТРОКА СПИСКА ПЛАГИНОВ (T-114-S0, закладка «Настройки → Плагины и MCP»).
///
/// В одной строке сходятся ДВЕ величины разной природы (T-112-S0): решение организации
/// (<see cref="State"/>, реплицируется) и свойство ЭТОГО компьютера (найден ли софт).
/// Показывается человеку одно слово — <see cref="ShownState"/>: «инициализирован» без
/// найденной программы это не «работает», а «софт ищется», и молчать об этом нельзя
/// (иначе выйдет «модель есть, а задание не идёт», cd91ad4e).
/// </summary>
public sealed class PluginListItemDto
{
    /// <summary>Идентификатор записи организации; ПУСТО — записи ещё нет, на диске лежит
    /// только манифест (такой плагин показывается как «объявлен»).</summary>
    public string Id { get; set; } = "";

    /// <summary>Номер записи PLG-N; пусто — записи ещё нет.</summary>
    public string DisplayId { get; set; } = "";

    /// <summary>Код плагина: <c>editor.shotcut</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>Вид: <c>gateway</c> | <c>mcp</c>.</summary>
    public string Kind { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Состояние ЗАПИСИ организации (declared/initialized/disabled/removed).</summary>
    public string State { get; set; } = "";

    /// <summary>Состояние, которое видит человек НА ЭТОМ сервере (плюс «probing»).</summary>
    public string ShownState { get; set; } = "";

    /// <summary>Итог поиска софта здесь: notNeeded | found | canInstall | needsManualPath.</summary>
    public string Software { get; set; } = "";

    /// <summary>Где нашли программу; пусто — не нашли либо она не нужна.</summary>
    public string SoftwarePath { get; set; } = "";

    /// <summary>Версия найденной программы; пусто — версию не спрашивали.</summary>
    public string SoftwareVersion { get; set; } = "";

    /// <summary>Манифест лежит на ЭТОМ сервере: без него плагин нельзя ни инициализировать,
    /// ни снять — нечего регистрировать.</summary>
    public bool HasManifest { get; set; }

    /// <summary>Сколько действий плагина заведено в справочнике действий.</summary>
    public int Actions { get; set; }

    /// <summary>Сколько записей опыта плагин положил в общий опыт организации.</summary>
    public int Experience { get; set; }

    /// <summary>Страница документа внутри языкового каталога: <c>plugins/&lt;код&gt;.md</c>.</summary>
    public string Doc { get; set; } = "";

    /// <summary>Действия плагина публикуются агенту на этом сервере.</summary>
    public bool Publishes { get; set; }
}

/// <summary>Действие плагина в форме записи (T-114-S0): только чтение — они из манифеста.</summary>
public sealed class PluginActionDto
{
    /// <summary>Код записи справочника действий: <c>AI2P.Plugins.Shotcut.Export</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>Имя инструмента, которым его зовёт агент.</summary>
    public string Tool { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Действию нужна программа из блока <c>software</c>.</summary>
    public bool NeedsSoftware { get; set; }

    /// <summary>Запись справочника уже заведена (то есть действие закрыто правилами).</summary>
    public bool Registered { get; set; }

    /// <summary>ОДИН ЭКЗЕМПЛЯР (T-155-S0): пока эта работа идёт, второй запуск ЖДЁТ. Флаг
    /// объявлен манифестом — у действия либо у его операции долгого запуска.</summary>
    public bool SingleInstance { get; set; }

    /// <summary>Операция долгого запуска (блок <c>run</c> манифеста), которую делает действие;
    /// пусто — действие долгим запуском не является.</summary>
    public string RunOp { get; set; } = "";

    /// <summary>Тайм-аут МОЛЧАНИЯ операции долгого запуска, секунд; 0 — умолчание коннектора.</summary>
    public int IdleTimeoutSec { get; set; }

    /// <summary>Общий предел длительности, секунд; 0 — умолчание коннектора.</summary>
    public int TimeoutSec { get; set; }
}

/// <summary>
/// ФОРМА ЗАПИСИ ПЛАГИНА (T-114-S0) — четыре блока задания: софт, действия, опыт, документ.
/// </summary>
public sealed class PluginDetailsDto
{
    public PluginListItemDto Plugin { get; set; } = new();

    /// <summary>Код пакета справочника (<c>models/packages.json</c>); пусто — ставить нечем,
    /// остаётся ручной путь.</summary>
    public string Package { get; set; } = "";

    /// <summary>Без этой программы плагин не работает вовсе.</summary>
    public bool SoftwareRequired { get; set; }

    /// <summary>Что делать человеку, если программа не нашлась (текст манифеста).</summary>
    public string SoftwareHint { get; set; } = "";

    /// <summary>Ручной путь из config.json ЭТОГО сервера; в базу организации он не попадает.</summary>
    public string ManualPath { get; set; } = "";

    /// <summary>Плагину нужна внешняя программа — значит, путь к ней можно указать руками
    /// (T-136-S0: «Blender у меня стоит, а указать негде»). Раньше поле показывалось только
    /// у плагина, назвавшего в манифесте <c>pathKey</c>, — но хранится путь всё равно в
    /// <c>config.json</c> этого сервера, и прятать поле было не от чего.</summary>
    public bool HasManualPath { get; set; }

    public List<PluginActionDto> Actions { get; set; } = [];

    /// <summary>Записи опыта плагина — те же записи общего опыта организации.</summary>
    public List<ExperienceRecordDto> Experience { get; set; } = [];

    // --- ПОДКЛЮЧЕНИЕ MCP (T-119-S0): только у записи вида mcp ---

    /// <summary>Транспорт подключения: <c>stdio</c> | <c>http</c>; пусто — это не MCP.</summary>
    public string Transport { get; set; } = "";

    /// <summary>Куда звоним: команда запуска (stdio) либо адрес (http). Показывается человеку
    /// и уходит в журнал — секрета в этой строке нет никогда.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Ссылка на секрет в хранилище: <c>plugin.&lt;код&gt;.token</c>.</summary>
    public string SecretRef { get; set; } = "";

    /// <summary>Заголовок, в который подставляется секрет; пусто — секрет не нужен.</summary>
    public string SecretHeader { get; set; } = "";

    /// <summary>Секрет задан НА ЭТОМ сервере. Само значение наружу не отдаётся никогда.</summary>
    public bool HasSecret { get; set; }

    /// <summary>Файл, в котором лежит секрет, — чтобы его можно было положить руками.</summary>
    public string SecretFile { get; set; } = "";

    /// <summary>Когда список инструментов снимали с сервера; пусто — ни разу.</summary>
    public DateTime? ToolsAt { get; set; }
}

/// <summary>
/// ОКНО УСТАНОВКИ И НАСТРОЙКИ ПЛАГИНА (T-136-S0) — то же, что окно установки модели, только
/// про одну внешнюю программу: где она нашлась, ручной путь к уже стоящей программе и ход
/// установки пакета справочника. Прогресс отдаётся отдельно от <see cref="PluginDetailsDto"/>
/// намеренно: его спрашивают раз в секунду, пока идёт загрузка.
/// </summary>
public sealed class PluginInstallDto
{
    /// <summary>Строка плагина после последнего действия — списку за окном нужна она.</summary>
    public PluginListItemDto Plugin { get; set; } = new();

    /// <summary>Код пакета справочника; пусто — ставить нечем, остаётся ручной путь.</summary>
    public string Package { get; set; } = "";

    /// <summary>Состояние пакета: размер, скачанное, идёт ли установка. null — пакета нет.</summary>
    public ModelPackageStatusDto? PackageStatus { get; set; }

    /// <summary>Установка пакета идёт прямо сейчас.</summary>
    public bool Running { get; set; }

    /// <summary>Файл, который качается сейчас.</summary>
    public string CurrentFile { get; set; } = "";

    /// <summary>Чем кончилась последняя установка; пусто — ошибки не было.</summary>
    public string Error { get; set; } = "";

    /// <summary>Ручной путь из config.json ЭТОГО сервера.</summary>
    public string ManualPath { get; set; } = "";

    /// <summary>Путь можно указать руками (плагину нужна внешняя программа).</summary>
    public bool HasManualPath { get; set; }

    /// <summary>Без этой программы плагин не работает вовсе.</summary>
    public bool SoftwareRequired { get; set; }

    /// <summary>Что делать человеку, если программа не нашлась (текст манифеста).</summary>
    public string SoftwareHint { get; set; } = "";

    /// <summary>
    /// ПРОГРАММЫ ПЛАГИНА ПОСТРОЧНО (T-146-S0). Плагин вправе вести к нескольким программам,
    /// и тогда у каждой свой пакет, свой ручной путь и свой ответ поиска — то есть своя
    /// строка со своими кнопками. У плагина с одной программой в списке ровно одна строка,
    /// и поля выше (<see cref="Package"/>, <see cref="ManualPath"/>) повторяют её.
    /// </summary>
    public List<PluginSoftwareItemDto> SoftwareItems { get; set; } = [];
}

/// <summary>
/// СТРОКА ПРОГРАММЫ ПЛАГИНА в окне установки и настройки (T-146-S0): что за программа, чем
/// её ставить, где она нашлась и где человек указал её руками. Зелёная галка
/// (<see cref="Ok"/>) означает ровно одно — программа найдена И версия годная.
/// </summary>
public sealed class PluginSoftwareItemDto
{
    /// <summary>Имя записи софта внутри плагина; под ним хранится ручной путь.</summary>
    public string Id { get; set; } = "";

    /// <summary>Название программы для человека.</summary>
    public string Name { get; set; } = "";

    /// <summary>Код пакета справочника; пусто — ставить нечем, остаётся ручной путь.</summary>
    public string Package { get; set; } = "";

    /// <summary>Без этой программы плагин не работает вовсе.</summary>
    public bool Required { get; set; }

    /// <summary>Итог поиска: notNeeded | found | canInstall | needsManualPath.</summary>
    public string Status { get; set; } = "";

    /// <summary>Найдена и проверена — человеку зелёная галка.</summary>
    public bool Ok { get; set; }

    /// <summary>Где нашли программу; пусто — не нашли.</summary>
    public string Path { get; set; } = "";

    /// <summary>Версия найденной программы; пусто — версию не спрашивали.</summary>
    public string Version { get; set; } = "";

    /// <summary>Ручной путь из config.json ЭТОГО сервера.</summary>
    public string ManualPath { get; set; } = "";

    /// <summary>Что делать человеку, если программа не нашлась (текст манифеста).</summary>
    public string Hint { get; set; } = "";

    /// <summary>Состояние пакета: размер, скачанное. null — пакета нет.</summary>
    public ModelPackageStatusDto? PackageStatus { get; set; }

    /// <summary>Установка этого пакета идёт прямо сейчас.</summary>
    public bool Running { get; set; }

    /// <summary>Файл, который качается сейчас.</summary>
    public string CurrentFile { get; set; } = "";

    /// <summary>Чем кончилась последняя установка этого пакета; пусто — ошибки не было.</summary>
    public string Error { get; set; } = "";
}

/// <summary>
/// ИТОГ ОБНОВЛЕНИЯ СПИСКА ИНСТРУМЕНТОВ СЕРВЕРА MCP (T-119-S0). Обновление — ЯВНОЕ действие
/// человека, и оно обязано показать, ЧТО изменилось: молча дозаводить действия нельзя —
/// это и есть тот случай, когда сервер добавляет себе возможности мимо правил безопасности.
/// </summary>
public sealed class McpToolsDto
{
    /// <summary>Полный список инструментов сервера после обновления.</summary>
    public List<PluginActionDto> Tools { get; set; } = [];

    /// <summary>Что добавилось по сравнению с прошлым снятием.</summary>
    public List<string> Added { get; set; } = [];

    /// <summary>Что исчезло (записи справочника у этих инструментов сняты).</summary>
    public List<string> Removed { get; set; } = [];

    /// <summary>Строка записи после обновления — списку за окном она и нужна.</summary>
    public PluginListItemDto Plugin { get; set; } = new();
}

/// <summary>Секрет подключения MCP: кладётся в <c>secrets/</c> этого сервера. Наружу не
/// отдаётся никогда — только признак «задан» и путь файла.</summary>
public sealed class PluginSecretDto
{
    public string Value { get; set; } = "";
}

/// <summary>Ручной путь к программе плагина: «софт можно не ставить, но покажите, где он стоит».</summary>
public sealed class PluginPathDto
{
    public string Path { get; set; } = "";

    /// <summary>Имя записи софта (<c>PluginSoftware.Id</c>), к которой относится путь;
    /// пусто — главная программа плагина (T-146-S0).</summary>
    public string Soft { get; set; } = "";
}
