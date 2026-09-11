using System.Text;
using System.Text.RegularExpressions;

namespace AI2P.Storage;

/// <summary>
/// Фильтр файлов репликации — <c>.repignore</c> (ТЗ гл. 6, этап 44; todo44).
///
/// Синтаксис и назначение те же, что у <c>.gitignore</c>, и по той же причине: человек уже
/// знает эти правила, а придумывать свой формат ради того же самого незачем. Поддержано:
/// <list type="bullet">
/// <item>комментарии (<c>#</c>) и пустые строки;</item>
/// <item>отрицание (<c>!шаблон</c>) — вернуть обратно то, что скрыто более ранним правилом;</item>
/// <item>привязка к корню (<c>/сборка</c>) против «в любом месте» (<c>*.tmp</c>);</item>
/// <item>каталоги (<c>кэш/</c>) — вместе со всем содержимым;</item>
/// <item>подстановки <c>*</c> (в пределах одного сегмента), <c>?</c> и <c>**</c> (через сегменты);</item>
/// <item>экранирование <c>\#</c> и <c>\!</c> в начале строки.</item>
/// </list>
///
/// Решает ПОСЛЕДНЕЕ подошедшее правило — как в git. Раз каталог исключён, его содержимое
/// не просматривается вовсе: иначе на большом каталоге сборки фильтр обходил бы десятки тысяч
/// файлов ради того, чтобы их выбросить.
///
/// Сам файл <c>.repignore</c> реплицируется как обычный, если не исключён собственным
/// правилом (todo44): он часть договорённости между серверами, а не локальная настройка.
/// </summary>
public sealed class RepIgnore
{
    /// <summary>Имя файла фильтра в корне реплицируемого каталога.</summary>
    public const string FileName = ".repignore";

    private readonly List<Rule> _rules = [];

    /// <summary>Правил нет — фильтр пропускает всё.</summary>
    public bool IsEmpty => _rules.Count == 0;

    private RepIgnore()
    {
    }

    /// <summary>Пустой фильтр: файла нет — реплицируется всё.</summary>
    public static RepIgnore Empty { get; } = new();

    /// <summary>Разобрать текст фильтра (содержимое <c>.repignore</c>).</summary>
    public static RepIgnore Parse(string text)
    {
        var ignore = new RepIgnore();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            var negate = false;
            if (line[0] == '!')
            {
                negate = true;
                line = line[1..];
            }
            else if (line.StartsWith("\\#", StringComparison.Ordinal)
                     || line.StartsWith("\\!", StringComparison.Ordinal))
            {
                line = line[1..];
            }
            if (line.Length == 0)
            {
                continue;
            }
            var dirOnly = line.EndsWith('/');
            line = line.Trim('/');
            if (line.Length == 0)
            {
                continue;
            }
            // шаблон без слэша внутри действует в ЛЮБОМ каталоге (как в git):
            // «*.tmp» скрывает и «a.tmp», и «под/каталог/a.tmp»
            var anywhere = !raw.TrimEnd().TrimStart('!', '\\').TrimEnd('/').Contains('/');
            ignore._rules.Add(new Rule(ToRegex(line, anywhere), negate, dirOnly));
        }
        return ignore;
    }

    /// <summary>Прочитать <c>.repignore</c> из корня каталога; файла нет — пустой фильтр.</summary>
    public static RepIgnore Load(string rootDir)
    {
        var path = Path.Combine(rootDir, FileName);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (IOException)
        {
            return Empty;   // файл занят или недоступен — фильтровать нечем, реплицируем всё
        }
    }

    /// <summary>
    /// Путь исключён из репликации. <paramref name="relativePath"/> — относительно корня
    /// реплицируемого каталога, разделители — прямые слэши, без ведущего слэша.
    /// </summary>
    public bool IsIgnored(string relativePath, bool isDirectory = false)
    {
        var path = relativePath.Replace('\\', '/').Trim('/');
        if (path.Length == 0)
        {
            return false;
        }
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirOnly && !isDirectory)
            {
                // «кэш/» скрывает сам каталог; его содержимое отсекается тем, что в такой
                // каталог мы не заходим (см. FileManifest), поэтому файлу правило не подходит
                continue;
            }
            if (rule.Pattern.IsMatch(path))
            {
                ignored = !rule.Negate;   // решает ПОСЛЕДНЕЕ подошедшее правило, как в git
            }
        }
        return ignored;
    }

    /// <summary>Шаблон git-подобной маски в регулярное выражение.</summary>
    private static Regex ToRegex(string pattern, bool anywhere)
    {
        var sb = new StringBuilder("^");
        if (anywhere)
        {
            // «*.tmp» подходит и в подкаталогах: разрешаем любой префикс из сегментов
            sb.Append("(?:.*/)?");
        }
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            switch (ch)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        // «**» — через любое число сегментов; «**/» съедает и сам разделитель
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '/':
                    sb.Append('/');
                    break;
                default:
                    sb.Append(Regex.Escape(ch.ToString()));
                    break;
            }
        }
        // шаблон подходит и всему, что лежит внутри найденного каталога
        sb.Append("(?:/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private sealed record Rule(Regex Pattern, bool Negate, bool DirOnly);
}
