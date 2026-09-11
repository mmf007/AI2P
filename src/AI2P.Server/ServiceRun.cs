using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace AI2P.Server;

/// <summary>
/// РЕЖИМ ЗАПУСКА: консоль или СЕРВИС ОС (T-271).
///
/// Приложение одно и то же, отдельной «серверной» сборки нет. По умолчанию оно
/// КОНСОЛЬНОЕ: запустили <c>AI2P.Server.exe</c> — работает в окне, при закрытии окна
/// останавливается. Сервисом оно становится тогда, когда его так ЗАПУСТИЛИ:
///
/// <list type="bullet">
/// <item>Windows — процесс поднял менеджер служб (<c>WindowsServiceHelpers</c> узнаёт это
/// по тому, кто нас породил);</item>
/// <item>Linux — процесс поднял systemd (<c>SystemdHelpers</c> смотрит переменную
/// <c>INVOCATION_ID</c> и родителя);</item>
/// <item>ключи <c>--service</c> / <c>--console</c> — принудительно, для разбора ошибок
/// и для систем, где распознать себя нельзя (launchd на macOS такого признака не даёт).</item>
/// </list>
///
/// Службу заводит НЕ приложение, а отдельный скрипт выкладки <c>makeAsServise.*</c>
/// (имя службы — <see cref="ServiceName"/>). Приложение только узнаёт, как его запустили,
/// и ведёт себя соответственно: в сервисе нечего открывать браузером и некому отвечать
/// на вопросы в консоли.
/// </summary>
public static class ServiceRun
{
    /// <summary>Имя службы ОС. Задано заданием T-271 и одинаково на всех системах:
    /// его знают скрипты <c>makeAsServise.*</c>, <c>install.*</c> и пакет установки.</summary>
    public const string ServiceName = "AI2P";

    /// <summary>Ключ «считать этот запуск сервисным» (принудительно).</summary>
    public const string ServiceArg = "--service";

    /// <summary>Ключ «считать этот запуск консольным» (принудительно, сильнее всех).</summary>
    public const string ConsoleArg = "--console";

    private static bool _resolved;
    private static bool _isService;

    /// <summary>
    /// Режим ЭТОГО запуска: <c>true</c> — сервис ОС. До вызова <see cref="Init"/> — <c>false</c>
    /// (консоль): умолчание приложения именно такое.
    /// </summary>
    public static bool IsService => _isService;

    /// <summary>Определён ли режим (звался ли <see cref="Init"/>). Нужен тестам.</summary>
    public static bool Resolved => _resolved;

    /// <summary>
    /// Определить режим запуска один раз, на старте, и запомнить. Возвращает то же, что
    /// потом отдаёт <see cref="IsService"/>.
    /// </summary>
    public static bool Init(string[] args)
    {
        _isService = Detect(args,
            // обе проверки платформенные: на чужой системе они просто отвечают «нет»
            () => OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService(),
            () => OperatingSystem.IsLinux() && SystemdHelpers.IsSystemdService());
        _resolved = true;
        return _isService;
    }

    /// <summary>
    /// Правило выбора режима. Вынесено отдельно и берёт распознаватели параметрами —
    /// иначе его нельзя проверить: «мы под менеджером служб» в тесте не изобразить.
    ///
    /// Порядок именно такой: <c>--console</c> сильнее всего (им человек разбирает
    /// поведение службы руками), затем <c>--service</c>, и только потом — распознавание
    /// самой системой. Ничего не подошло — консоль, это умолчание приложения.
    /// </summary>
    public static bool Detect(IReadOnlyList<string> args, Func<bool> windowsService, Func<bool> systemdService)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], ConsoleArg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], ServiceArg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return windowsService() || systemdService();
    }
}
