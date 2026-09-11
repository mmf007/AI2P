namespace AI2P.Core;

/// <summary>
/// ДОМАШНИЙ КАТАЛОГ В ПУТЯХ (T-135): «~» и «~/…».
///
/// На Linux и macOS каталог установки сервера — <c>~/ai/AI2P</c>, и человек набирает пути
/// именно так: в диалоге выбора папки проекта, в каталогах этого компьютера (репозиторий
/// моделей, дистрибутивы, пакеты), в правилах безопасности, в <c>config.json</c>.
/// Ни <see cref="Path.GetFullPath(string)"/>, ни <see cref="Path.Combine(string, string)"/>
/// «~» не понимают: без раскрытия получался бы каталог с именем «~» рядом с приложением.
///
/// Раскрывается ТОЛЬКО ведущий «~» своего пользователя: «~user/…» (чужой домашний каталог,
/// синтаксис оболочки POSIX) намеренно не поддерживается — на Windows его аналога нет,
/// а угадывать чужой каталог по имени нельзя.
/// </summary>
public static class PathHome
{
    /// <summary>Домашний каталог текущего пользователя; пусто — определить не удалось.</summary>
    public static string Home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            return home;
        }
        // сервер под службой/демоном (запуск не из сессии пользователя): переменные окружения
        return Environment.GetEnvironmentVariable("HOME")
               ?? Environment.GetEnvironmentVariable("USERPROFILE")
               ?? "";
    }

    /// <summary>
    /// Раскрыть ведущий «~» в домашний каталог: «~» → домашний, «~/ai/AI2P» и «~\ai\AI2P» →
    /// домашний + остаток. Остальные пути возвращаются без изменений (в том числе «~user/…»
    /// и «~» в середине пути — это обычный символ имени файла).
    /// </summary>
    public static string Expand(string? path)
    {
        var value = (path ?? "").Trim();
        if (value.Length == 0 || value[0] != '~')
        {
            return value;
        }
        if (value.Length > 1 && value[1] is not ('/' or '\\'))
        {
            return value;   // «~user/…» — чужой домашний каталог, не наше дело
        }
        var home = Home();
        if (home.Length == 0)
        {
            return value;   // домашнего каталога нет — пусть путь дойдёт до вызывающего как есть
        }
        if (value.Length == 1)
        {
            return home;
        }
        var tail = value[2..].TrimStart('/', '\\');
        // «~/ai/AI2P» на Windows должен дать «…\ai\AI2P»: Path.Combine разделители внутри
        // хвоста не трогает, а путь идёт дальше в сравнения строк (правила безопасности)
        return Path.Combine(home, OperatingSystem.IsWindows() ? tail.Replace('/', '\\') : tail);
    }

    /// <summary>Путь начинается с «~» (и это именно домашний каталог, а не имя файла).</summary>
    public static bool StartsWithHome(string? path)
    {
        var value = (path ?? "").Trim();
        return value == "~" || (value.Length > 1 && value[0] == '~' && value[1] is '/' or '\\');
    }

    /// <summary>Полный путь ПОСЛЕ раскрытия «~»: «~/ai» — полный, «ai» — нет.</summary>
    public static bool IsRooted(string? path)
    {
        var expanded = Expand(path);
        return expanded.Length > 0 && Path.IsPathRooted(expanded);
    }
}
