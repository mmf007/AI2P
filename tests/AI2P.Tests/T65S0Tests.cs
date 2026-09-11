using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-65-S0 (ярус A разбора T-64-S0): ПЕРЕВОД ВЫВОДА СБОРОЧНЫХ И УСТАНОВОЧНЫХ СКРИПТОВ.
///
/// Скрипт по языкам НЕ дублируется: тексты вынесены в каталог <c>i18n/scripts.&lt;язык&gt;.txt</c>
/// (ключ=значение, UTF-8 без BOM), справка по <c>--help</c> — в отдельные файлы
/// <c>i18n/help/&lt;скрипт&gt;.&lt;язык&gt;.txt</c>. Читают их два тонких загрузчика:
/// <c>i18n/loc.ps1</c> (PowerShell) и <c>i18n/loc.sh</c> (POSIX sh). Базовый язык и
/// умолчание — <b>en</b>.
///
/// Сторожа здесь ровно три, и каждый ловит беду, которую иначе увидит только человек
/// на чужой машине:
/// 1) состав языков разошёлся — часть вывода молча уедет на английский;
/// 2) скрипт зовёт ключ, которого в каталоге нет — на экране появится «scr.inst.42»;
/// 3) в .cmd/.bat завелась кириллица — cmd.exe держит позицию чтения в БАЙТАХ, и
///    многобайтовый символ сдвигает разбор (наука T-34-S0).
/// </summary>
public class T65S0Tests
{
    /// <summary>Языки каталога обязаны сходиться посчётно: ключ, забытый в переводе,
    /// молча заменяется английским, и заметить это можно только глазами.</summary>
    [Fact]
    public void The_Script_Catalogues_Hold_The_Same_Set_Of_Keys()
    {
        var en = ReadCatalogue("en");
        Assert.NotEmpty(en);

        foreach (var file in Directory.GetFiles(I18nRoot(), "scripts.*.txt"))
        {
            var lang = Path.GetFileNameWithoutExtension(file)!.Substring("scripts.".Length);
            var map = ReadCatalogue(lang);
            var missing = en.Keys.Where(k => !map.ContainsKey(k)).OrderBy(k => k).ToList();
            var extra = map.Keys.Where(k => !en.ContainsKey(k)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"в scripts.{lang}.txt нет ключей: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"в scripts.{lang}.txt лишние ключи: {string.Join(", ", extra)}");
        }
    }

    /// <summary>Каждый ключ, который скрипт просит у каталога, в каталоге есть.
    /// Иначе на экране появится сам ключ — вывод не падает, но и не читается.</summary>
    [Fact]
    public void Every_Key_The_Scripts_Ask_For_Exists_In_The_Catalogue()
    {
        var en = ReadCatalogue("en");
        var used = new SortedSet<string>();

        foreach (var name in LocalizedScripts)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), name));
            // PowerShell: (L 'scr.inst.20' …) ; POSIX sh: L scr.inst.20 "…" / ai2p_text scr.pkg.8 …
            foreach (Match m in Regex.Matches(text, @"\b(?:L|ai2p_text)\s+'?(scr\.[a-z]+\.\d+)'?"))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(used);
        var unknown = used.Where(k => !en.ContainsKey(k)).ToList();
        Assert.True(unknown.Count == 0, $"скрипты зовут ключи, которых нет в scripts.en.txt: {string.Join(", ", unknown)}");
    }

    /// <summary>У каждого скрипта, у которого есть --help, лежит файл справки хотя бы
    /// на базовом языке: без него --help печатает «No help file for …».</summary>
    [Fact]
    public void Every_Script_With_Help_Has_A_Help_File_In_The_Base_Language()
    {
        foreach (var name in new[] { "install", "makeAsServise", "MakePackage",
                                     "ai2p", "build", "buildRelease", "install_required" })
        {
            var file = Path.Combine(I18nRoot(), "help", name + ".en.txt");
            Assert.True(File.Exists(file), $"нет файла справки {name}.en.txt");
            Assert.NotEmpty(File.ReadAllText(file).Trim());
        }
    }

    /// <summary>ВСЕ .cmd и .bat — чистый ASCII и по-английски. cmd.exe декодирует файл
    /// в кодировке консоли, а позицию чтения держит в БАЙТАХ: многобайтовый символ
    /// сдвигает разбор, и в stdout попадает хвост следующей строки (поймано живьём в
    /// T-34-S0). Опасны и комментарии rem — поэтому проверка побайтная, на весь файл.</summary>
    [Fact]
    public void Every_Cmd_And_Bat_Is_Pure_Ascii()
    {
        var files = Directory.GetFiles(RepoRoot(), "*.cmd").Concat(Directory.GetFiles(RepoRoot(), "*.bat")).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bad = File.ReadAllBytes(file).FirstOrDefault(b => b > 127);
            Assert.True(bad == 0, $"{Path.GetFileName(file)} обязан быть чистым ASCII (T-34-S0)");
        }
    }

    /// <summary>ОБЁРТКА .cmd/.bat ОБЯЗАНА ДОВЕЗТИ ЯЗЫК ДО СПРАВКИ. Первая редакция звала
    /// .ps1 одним ключом «-Help» и молча выбрасывала остаток командной строки, поэтому
    /// «buildRelease.cmd --help --Lang ru» печатал английскую справку (жалоба заказчика по
    /// T-65-S0). Ловится это только глазами: код возврата ноль, текст на месте — просто не
    /// на том языке. Сторож смотрит СТРОКУ ЗАПУСКА PowerShell: у ветки справки обязан быть
    /// хвост из переменной (%ARGS%, %PUBLANGOPT% и родня), а там, где справка печатается
    /// через -Command, рядом с Show-Ai2pHelp обязан стоять Set-Ai2pLang.</summary>
    [Fact]
    public void Every_Windows_Wrapper_Forwards_The_Language_To_Its_Help()
    {
        var files = Directory.GetFiles(RepoRoot(), "*.cmd").Concat(Directory.GetFiles(RepoRoot(), "*.bat")).ToList();
        Assert.NotEmpty(files);
        var checkedLines = 0;
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("rem", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.IndexOf("powershell", StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (line.IndexOf("Show-Ai2pHelp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    checkedLines++;
                    Assert.True(line.IndexOf("Set-Ai2pLang", StringComparison.OrdinalIgnoreCase) >= 0,
                        $"{name}: рядом с Show-Ai2pHelp нет Set-Ai2pLang — «--help -Lang ru» напечатает английский");
                }
                else if (Regex.IsMatch(line, @"-Help\b", RegexOptions.IgnoreCase))
                {
                    checkedLines++;
                    Assert.True(Regex.IsMatch(line, @"-Help\b\s*[^%]*%"),
                        $"{name}: справка зовётся без хвоста командной строки — «--help -Lang ru» напечатает английский");
                }
            }
        }
        Assert.True(checkedLines >= 7, $"проверено строк запуска справки: {checkedLines} — обёрток должно быть не меньше семи");
    }

    /// <summary>Загрузчики каталога лежат рядом с ним и сами тоже чистый ASCII:
    /// Windows PowerShell 5.1 читает файл без BOM в ANSI (опыт 72133885).</summary>
    [Fact]
    public void The_Catalogue_Loaders_Are_In_Place_And_Ascii()
    {
        foreach (var loader in new[] { "loc.ps1", "loc.sh" })
        {
            var file = Path.Combine(I18nRoot(), loader);
            Assert.True(File.Exists(file), $"нет загрузчика i18n/{loader}");
            var bad = File.ReadAllBytes(file).FirstOrDefault(b => b > 127);
            Assert.True(bad == 0, $"i18n/{loader} обязан быть чистым ASCII");
        }
        // каталог сообщений — UTF-8 БЕЗ BOM: BOM попал бы в первый ключ первой строки
        foreach (var file in Directory.GetFiles(I18nRoot(), "scripts.*.txt"))
        {
            var head = File.ReadAllBytes(file).Take(3).ToArray();
            Assert.False(head.Length == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF,
                $"{Path.GetFileName(file)} обязан быть без BOM");
        }
    }

    /// <summary>Скрипты яруса A, у которых тексты вынесены в каталог.</summary>
    private static readonly string[] LocalizedScripts =
    {
        "install.ps1", "install.sh", "makeAsServise.ps1", "makeAsServise.sh",
        "MakePackage.ps1", "MakePackage.sh", "ai2p"
    };

    private static Dictionary<string, string> ReadCatalogue(string lang)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var file = Path.Combine(I18nRoot(), $"scripts.{lang}.txt");
        Assert.True(File.Exists(file), $"нет каталога scripts.{lang}.txt");
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var i = line.IndexOf('=');
            if (i < 1) continue;
            map[line.Substring(0, i).Trim()] = line.Substring(i + 1);
        }
        return map;
    }

    private static string I18nRoot() => Path.Combine(RepoRoot(), "i18n");

    /// <summary>Корень AI2P_app: тесты идут из bin/…, поэтому каталог ищется вверх по дереву
    /// по опознавательному файлу решения.</summary>
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir.Length > 0 && !File.Exists(Path.Combine(dir, "AI2P.sln")))
        {
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        }
        Assert.True(dir.Length > 0, "не найден корень AI2P_app (AI2P.sln)");
        return dir;
    }
}
