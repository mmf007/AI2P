using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-70-S0: РЕГИСТРАЦИЯ ЯЗЫКОВ.
///
/// Язык добавляется в систему одним файлом <c>i18n/&lt;код&gt;.json</c> — списка языков в коде
/// нет намеренно (<see cref="LocCatalog"/>). Отсюда три молчаливых правила, каждое ломается
/// не ошибкой, а списком, в котором человек не находит свой язык:
/// <list type="number">
/// <item>у каждого словаря есть ключ <c>lang.name</c> — НАЗВАНИЕ ЯЗЫКА НА НЁМ САМОМ;</item>
/// <item>названия не повторяются (иначе два пункта списка выглядят одинаково);</item>
/// <item>языковой диспетчер <c>AI2P_app/README.md</c> называет каждый установленный язык
/// и ведёт на его документацию.</item>
/// </list>
/// Выбор языка стоит в двух местах — визард первого старта (<c>AuthPages</c>, шаг «язык»)
/// и «Настройки → Основное»; оба берут список одним свойством <c>LanguagesByName</c>.
/// </summary>
public sealed class T70S0Tests
{
    /// <summary>Каталог AI2P_app (в нём лежит AI2P.sln).</summary>
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static LocCatalog Catalogue() => new(Path.Combine(AppDir(), "i18n"));

    [Fact]
    public void Every_Language_Names_Itself_In_Its_Own_Dictionary()
    {
        var catalogue = Catalogue();
        Assert.NotEmpty(catalogue.Languages);
        foreach (var lang in catalogue.Languages)
        {
            Assert.True(catalogue.Has(lang, LocCatalog.NameKey),
                $"в словаре i18n/{lang}.json нет ключа {LocCatalog.NameKey} — названия языка на нём самом");
            Assert.NotEqual(lang, catalogue.Name(lang));
        }
    }

    [Fact]
    public void Language_Names_Do_Not_Repeat()
    {
        var catalogue = Catalogue();
        var names = catalogue.Languages.Select(catalogue.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_Language_List_Is_Ordered_By_Name()
    {
        var catalogue = Catalogue();
        var list = catalogue.LanguagesByName;
        Assert.Equal(catalogue.Languages.Count, list.Count);
        Assert.Equal(list.Select(catalogue.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase),
            list.Select(catalogue.Name));
    }

    [Fact]
    public void The_Root_Readme_Offers_Every_Installed_Language()
    {
        var catalogue = Catalogue();
        var text = File.ReadAllText(Path.Combine(AppDir(), "README.md"));
        foreach (var lang in catalogue.Languages)
        {
            Assert.True(text.Contains(catalogue.Name(lang), StringComparison.Ordinal),
                $"в AI2P_app/README.md нет языка {lang} ({catalogue.Name(lang)})");
            Assert.True(text.Contains($"doc/{lang}/README.md", StringComparison.Ordinal),
                $"в AI2P_app/README.md нет ссылки doc/{lang}/README.md");
        }
    }
}
