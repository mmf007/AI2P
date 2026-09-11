using System.Text.RegularExpressions;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-18-S1: ПЕРВИЧНАЯ ПОДГОТОВКА ПОСТАВЛЯЕМОЙ ДОКУМЕНТАЦИИ.
///
/// Читать документацию из UI будет окно документации (T-17-S1): оно открывается на
/// <c>doc/&lt;язык&gt;/README.md</c> и дальше ходит по ВНУТРЕННИМ ссылкам. Поэтому у структуры
/// каталога появились правила, которых раньше не было, и все они молчаливые — ломаются они
/// не ошибкой, а пустым экраном у пользователя:
/// <list type="number">
/// <item>у каждого языка есть <c>README.md</c> — это точка входа чтения;</item>
/// <item>у каждого подкаталога с документами есть свой <c>README.md</c> — оглавление раздела;</item>
/// <item>состав языков совпадает файл в файл: чего нет в переводе, того пользователь
/// другого языка не увидит вовсе;</item>
/// <item>относительная ссылка ведёт в существующий файл и не уводит за пределы <c>doc/</c>.</item>
/// </list>
///
/// Отдельно сторожится обратное правило (T-214): документы, которые открываются кнопкой
/// «i» ВНУТРИ страницы приложения (модели, импорты), относительных ссылок содержать НЕ
/// должны — там они мертвы и уводят со страницы. Относительные ссылки живут только в
/// оглавлениях, которые показывает окно документации.
///
/// Сами оглавления собираются скриптом <c>test/t18s1/mktoc.py</c> по фактическому составу
/// каталогов; здесь проверяется результат, а не скрипт.
/// </summary>
public sealed class T18S1Tests
{
    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string DocRoot() => Path.Combine(RepoRoot(), "AI2P_app", "doc");

    /// <summary>
    /// Языковые каталоги поставляемой документации. Каталог БЕЗ единого .md языком не
    /// считается (T-51-S0): рядом с языками лежит <c>doc/images/</c> — картинки, общие для
    /// всех языков, — и требовать от него оглавления и переводов было бы бессмысленно.
    /// </summary>
    private static string[] Languages() =>
        Directory.GetDirectories(DocRoot())
            .Where(d => Directory.GetFiles(d, "*.md", SearchOption.AllDirectories).Length > 0)
            .Select(Path.GetFileName).OfType<string>()
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    /// <summary>Все .md языка, путями относительно языкового каталога, с «/».</summary>
    private static string[] Docs(string lang)
    {
        var root = Path.Combine(DocRoot(), lang);
        return Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Ссылки Markdown вида [текст](адрес).</summary>
    private static readonly Regex Link = new(@"\[[^\]]*\]\(([^)\s]+)\)", RegexOptions.Compiled);

    private static bool IsExternal(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith('#');

    // --- 1. точка входа и оглавления разделов ---

    [Fact]
    public void Every_Language_Has_A_Readme_At_Its_Root()
    {
        var langs = Languages();
        Assert.NotEmpty(langs);
        foreach (var lang in langs)
        {
            var path = Path.Combine(DocRoot(), lang, "README.md");
            Assert.True(File.Exists(path), $"нет точки входа чтения: doc/{lang}/README.md");
        }
    }

    [Fact]
    public void Every_Section_Directory_Has_Its_Own_Readme()
    {
        foreach (var lang in Languages())
        {
            var root = Path.Combine(DocRoot(), lang);
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                if (Directory.GetFiles(dir, "*.md").Length == 0)
                {
                    continue;
                }
                var rel = Path.GetRelativePath(DocRoot(), dir).Replace('\\', '/');
                Assert.True(File.Exists(Path.Combine(dir, "README.md")),
                    $"у раздела нет оглавления: {rel}/README.md");
            }
        }
    }

    [Fact]
    public void The_Language_Readme_Mentions_Every_Section()
    {
        foreach (var lang in Languages())
        {
            var root = Path.Combine(DocRoot(), lang);
            var text = File.ReadAllText(Path.Combine(root, "README.md"));
            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (Directory.GetFiles(dir, "*.md").Length == 0)
                {
                    continue;
                }
                Assert.True(text.Contains($"{name}/README.md", StringComparison.Ordinal),
                    $"в оглавлении doc/{lang}/README.md не отражён подкаталог {name}/");
            }
        }
    }

    // --- 2. переводы: состав языков совпадает ---

    [Fact]
    public void Languages_Hold_The_Same_Set_Of_Documents()
    {
        var langs = Languages();
        var all = langs.SelectMany(Docs).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        foreach (var doc in all)
        {
            foreach (var lang in langs)
            {
                Assert.True(File.Exists(Path.Combine(DocRoot(), lang, doc.Replace('/', Path.DirectorySeparatorChar))),
                    $"нет перевода: doc/{lang}/{doc}");
            }
        }
    }

    // --- 3. внутренние ссылки живые ---

    [Fact]
    public void Every_Relative_Link_Leads_To_An_Existing_File_Inside_Doc()
    {
        var root = DocRoot();
        var checkedLinks = 0;
        foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(path)!;
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            foreach (Match m in Link.Matches(File.ReadAllText(path)))
            {
                var target = m.Groups[1].Value;
                if (IsExternal(target))
                {
                    continue;
                }
                var clean = target.Split('#')[0];
                if (clean.Length == 0)
                {
                    continue;
                }
                checkedLinks++;
                var full = Path.GetFullPath(Path.Combine(dir, clean));
                Assert.True(full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                    $"ссылка уводит за пределы doc/: {rel} -> {target}");
                Assert.True(File.Exists(full), $"ссылка в никуда: {rel} -> {target}");
            }
        }
        Assert.True(checkedLinks > 0, "относительных ссылок не найдено вовсе — оглавления пусты?");
    }

    /// <summary>
    /// T-214: документ, который показывается кнопкой «i» внутри страницы приложения,
    /// относительной ссылки содержать не может — href остаётся относительным, разворачивается
    /// по адресу страницы организации и уводит на 404, убивая окно вместе с формой. Оглавления
    /// (README.md) из правила исключены: их показывает окно документации T-17-S1.
    /// </summary>
    [Fact]
    public void Documents_Shown_By_The_I_Button_Carry_No_Relative_Links()
    {
        foreach (var lang in Languages())
        {
            // «plugins» (T-114-S0) — документ плагина показывается той же кнопкой «i»
            // ВНУТРИ окна приложения, значит относительных ссылок в нём быть не должно
            foreach (var section in new[] { "models", "import", "plugins" })
            {
                var dir = Path.Combine(DocRoot(), lang, section);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var path in Directory.GetFiles(dir, "*.md"))
                {
                    var name = Path.GetFileName(path);
                    if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    foreach (Match m in Link.Matches(File.ReadAllText(path)))
                    {
                        var target = m.Groups[1].Value;
                        Assert.True(IsExternal(target),
                            $"относительная ссылка в документе кнопки «i»: {lang}/{section}/{name} -> {target}");
                    }
                }
            }
        }
    }
}
