using AI2P.Connectors;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-262: КАРТИНКА, ВСТАВЛЕННАЯ ТЕГОМ <c>&lt;img&gt;</c>. Задача, импортированная из GitHub,
/// приносит вставленные картинки не разметкой <c>![…](…)</c>, а HTML-тегом. Сырой HTML при
/// показе Markdown выключен намеренно, поэтому такой тег показывался ТЕКСТОМ — ни в
/// описании задачи, ни в чате картинки видно не было.
///
/// Вторая половина той же беды — испорченный адрес: для Markdown адрес внутри тега это
/// обычный текст, а подписанные адреса картинок GitHub (<c>…?jwt=eyJ0eXAi…</c>) написаны
/// base64url, где «_», «*» и «~~» встречаются постоянно. Такой текст разбирался как курсив
/// и зачёркивание, часть знаков уезжала в теги, а ссылка обрывалась сразу после «?jwt=» —
/// ровно то, на что пожаловался заказчик («он у нас обрезан по сравнению с оригиналом,
/// и картинка не открывается»).
/// </summary>
public sealed class T262Tests
{
    private const string OwnBase = "http://localhost:5480/ai2p";

    /// <summary>Подписанный адрес картинки GitHub: три части JWT, base64url — с «_» сразу
    /// после точки (на нём и ломался разбор), «*» и «~~».</summary>
    private const string SignedUrl =
        "https://private-user-images.githubusercontent.com/40434685/"
        + "544359630-89fc9e55-18d6-450a-8cd4-56be5f62511a.png"
        + "?jwt=eyJ0eXAiOiJKV1Qi._eyJpc3MiOiJnaXRodWIi_.a*bC-d~~eF";

    // --- картинка показывается картинкой ---

    [Theory]
    [InlineData("как пишет GitHub",
        "<img width=\"1728\" height=\"1117\" alt=\"Image\" src=\"URL\" />")]
    [InlineData("без закрывающей косой", "<img src=\"URL\">")]
    [InlineData("в строке текста", "до <img src=\"URL\"> после")]
    [InlineData("в списке", "* <img src=\"URL\">")]
    [InlineData("в ячейке таблицы", "| а |\n| --- |\n| <img src=\"URL\"> |")]
    [InlineData("в цитате", "> <img src=\"URL\">")]
    [InlineData("одинарные кавычки", "<img src='URL'>")]
    [InlineData("без кавычек", "<img src=URL>")]
    [InlineData("без кавычек, самозакрытый", "<img src=URL/>")]
    [InlineData("ИМЯ ТЕГА ЗАГЛАВНЫМИ", "<IMG SRC=\"URL\">")]
    public void Img_tag_becomes_a_picture(string name, string template)
    {
        var html = Md.ToHtml(template.Replace("URL", SignedUrl), OwnBase);
        Assert.True(html.Contains($"<img src=\"{SignedUrl}\"", StringComparison.Ordinal),
            $"[{name}] картинки нет или адрес испорчен: {html}");
    }

    /// <summary>Главная проверка жалобы: адрес доезжает до разметки ЦЕЛИКОМ, без потери
    /// знаков и без обрыва после «?jwt=».</summary>
    [Fact]
    public void Signed_github_url_is_not_broken_by_markdown()
    {
        var html = Md.ToHtml($"Скриншот:\n\n<img alt=\"Image\" src=\"{SignedUrl}\" />", OwnBase);
        Assert.Contains($"<img src=\"{SignedUrl}\"", html, StringComparison.Ordinal);
        // ни курсива, ни зачёркивания из знаков адреса
        Assert.DoesNotContain("<em>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<del>", html, StringComparison.Ordinal);
    }

    /// <summary>Та же картинка в СООБЩЕНИИ ЧАТА: чат рисуется тем же Md.ToHtml, что и
    /// описание, — обсуждение задачи GitHub переносится в чат как есть (T-152).</summary>
    [Fact]
    public void Img_tag_in_chat_message_becomes_a_picture()
    {
        var comment = new GitHubImporter.IssueComment("77", "mike",
            $"Вот что видно:\n\n<img width=\"800\" alt=\"Image\" src=\"{SignedUrl}\" />",
            new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
        var message = Assert.Single(GitHubImporter.ChatOf([comment]));
        Assert.Contains($"<img src=\"{SignedUrl}\"", Md.ToHtml(message.Text, OwnBase),
            StringComparison.Ordinal);
    }

    /// <summary>Описание задачи, собранное импортом GitHub (текст задачи + приписка-источник),
    /// показывает картинку — то есть путь работает от начала и до конца.</summary>
    [Fact]
    public void Imported_issue_description_shows_the_picture()
    {
        var issue = new GitHubImporter.Issue("1", 42, "Задача",
            $"Экран после нажатия:\n\n<img width=\"1728\" alt=\"Image\" src=\"{SignedUrl}\" />",
            null, "mike/repo", "https://github.com/mike/repo/issues/42");
        Assert.Contains($"<img src=\"{SignedUrl}\"",
            Md.ToHtml(GitHubImporter.DescriptionOf(issue), OwnBase), StringComparison.Ordinal);
    }

    // --- что переносится из тега, а что отбрасывается ---

    [Fact]
    public void Size_and_caption_are_kept()
    {
        var html = Md.ToHtml("<img width=\"1728\" height=\"1117\" alt=\"Экран\" src=\"https://ex.com/a.png\" />", "");
        Assert.Contains("alt=\"Экран\"", html, StringComparison.Ordinal);
        Assert.Contains("width=\"1728\"", html, StringComparison.Ordinal);
        Assert.Contains("height=\"1117\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Percent_width_is_kept_and_nonsense_size_is_dropped()
    {
        Assert.Contains("width=\"50%\"",
            Md.ToHtml("<img src=\"https://ex.com/a.png\" width=\"50%\" />", ""), StringComparison.Ordinal);
        Assert.DoesNotContain("width=",
            Md.ToHtml("<img src=\"https://ex.com/a.png\" width=\"onerror\" />", ""), StringComparison.Ordinal);
    }

    /// <summary>Тег не «включается обратно», а рисуется своей разметкой: любой другой
    /// атрибут исходного тега в HTML не попадает вовсе.</summary>
    [Theory]
    [InlineData("onerror")]
    [InlineData("onload")]
    [InlineData("style")]
    [InlineData("srcset")]
    public void Other_attributes_do_not_reach_the_page(string attr)
    {
        var html = Md.ToHtml($"<img src=\"https://ex.com/a.png\" {attr}=\"alert(1)\" />", "");
        Assert.Contains("<img src=\"https://ex.com/a.png\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(attr, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Caption_with_quotes_and_brackets_is_escaped()
    {
        var html = Md.ToHtml("<img src=\"https://ex.com/a.png\" alt='&quot;&lt;b&gt;' />", "");
        Assert.Contains("alt=\"&quot;&lt;b&gt;\"", html, StringComparison.Ordinal);
    }

    // --- чем картинка быть не может ---

    [Theory]
    [InlineData("javascript", "javascript:alert(1)")]
    [InlineData("javascript с пробелом", " javascript:alert(1)")]
    [InlineData("vbscript", "vbscript:msgbox")]
    [InlineData("data", "data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("пусто", "")]
    public void Foreign_scheme_stays_text(string name, string src)
    {
        var html = Md.ToHtml($"<img src=\"{src}\" />", "");
        Assert.False(html.Contains("<img ", StringComparison.Ordinal),
            $"[{name}] такой адрес картинкой рисоваться не должен: {html}");
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    /// <summary>В блоке кода и в строчном коде тег написан как ПРИМЕР — он обязан остаться
    /// текстом; именно это и решается тем, что картинки возвращаются в ГОТОВЫЙ HTML.</summary>
    [Theory]
    [InlineData("блок кода", "```\n<img src=\"https://ex.com/a.png\" />\n```")]
    [InlineData("строчный код", "текст `<img src=\"https://ex.com/a.png\" />` текст")]
    [InlineData("блок кода с языком", "```html\n<img src=\"https://ex.com/a.png\" />\n```")]
    public void Img_tag_inside_code_stays_text(string name, string markdown)
    {
        var html = Md.ToHtml(markdown, "");
        Assert.False(html.Contains("<img ", StringComparison.Ordinal),
            $"[{name}] в коде картинки быть не должно: {html}");
        Assert.Contains("&lt;img src=&quot;https://ex.com/a.png&quot; /&gt;", html, StringComparison.Ordinal);
    }

    /// <summary>Послабление сделано ТОЛЬКО для картинки: остальной сырой HTML по-прежнему
    /// показывается текстом (описания приходят и от ИИ).</summary>
    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;")]
    [InlineData("<b>жирный</b>", "&lt;b&gt;")]
    [InlineData("<iframe src=\"https://ex.com\"></iframe>", "&lt;iframe")]
    [InlineData("<a href=\"https://ex.com\">тык</a>", "&lt;a href")]
    [InlineData("<div onclick=\"alert(1)\">x</div>", "&lt;div")]
    public void Other_raw_html_is_still_shown_as_text(string markdown, string expected)
    {
        Assert.Contains(expected, Md.ToHtml(markdown, ""), StringComparison.Ordinal);
    }

    /// <summary>Символ служебной метки, написанный человеком, картинкой не становится:
    /// он вычищается из текста до разбора.</summary>
    [Fact]
    public void Counterfeit_marker_does_not_become_a_picture()
    {
        var html = Md.ToHtml("\uE2620\uE262 и <img src=\"https://ex.com/a.png\" />", "");
        Assert.Equal(1, Occurrences(html, "<img "));
        Assert.DoesNotContain("\uE262", html, StringComparison.Ordinal);
    }

    // --- общие правила показа, которые картинка обязана соблюдать ---

    /// <summary>Картинка, лежащая у нас же, показывается ОТНОСИТЕЛЬНЫМ путём (T-142): иначе
    /// браузер шёл бы за файлом наружу — через DNS и NAT обратно к этому же серверу.</summary>
    [Fact]
    public void Own_file_picture_becomes_a_local_path()
    {
        var html = Md.ToHtml(
            "<img src=\"http://localhost:5480/ai2p/mmfgrp/api/files/project?projectId=1&amp;path=a.png\" />",
            OwnBase);
        Assert.Contains("<img src=\"/ai2p/mmfgrp/api/files/project?projectId=1&amp;path=a.png\"",
            html, StringComparison.Ordinal);
    }

    /// <summary>Голый URL рядом с картинкой по-прежнему становится ссылкой (T-179), и вторая
    /// ссылка у самой картинки не появляется.</summary>
    [Fact]
    public void Bare_url_next_to_a_picture_still_becomes_a_link()
    {
        var html = Md.ToHtml("<img src=\"https://ex.com/a.png\" /> см. http://localhost:5480/ai2p/x", "");
        Assert.Equal(1, Occurrences(html, "<img "));
        Assert.Equal(1, Occurrences(html, "<a "));
        Assert.Contains("href=\"http://localhost:5480/ai2p/x\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_pictures_in_a_row()
    {
        var html = Md.ToHtml("<img src=\"https://ex.com/1.png\" /> и <img src=\"https://ex.com/2.png\" />", "");
        Assert.Contains("<img src=\"https://ex.com/1.png\"", html, StringComparison.Ordinal);
        Assert.Contains("<img src=\"https://ex.com/2.png\"", html, StringComparison.Ordinal);
    }

    /// <summary>«Конвертация в наш .md»: правка описания в предпросмотре редактора
    /// (HTML → Markdown, todo30_2) превращает тег в обычную разметку картинки — с тем же
    /// адресом, без потери знаков.</summary>
    [Fact]
    public void Editing_in_the_preview_turns_the_tag_into_markdown()
    {
        var markdown = MdHtml.ToMarkdown(Md.ToHtml($"<img alt=\"Image\" src=\"{SignedUrl}\" />", ""));
        Assert.Equal($"![Image]({SignedUrl})", markdown);
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
