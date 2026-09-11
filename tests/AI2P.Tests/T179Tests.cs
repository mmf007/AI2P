using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-179: URL, написанный в тексте просто так, при показе форматированного текста
/// становится ссылкой (Md.ToHtml). До этой задачи автоссылок Markdig не хватало ровно
/// на самое частое: адрес своего же сервера (<c>http://localhost:5480/…</c> — домен без
/// точки) и адрес сразу после знака препинания.
/// </summary>
public sealed class T179Tests
{
    /// <summary>Свой адрес: с ним ссылка на СЕБЯ остаётся в текущей вкладке (T-142).</summary>
    private const string OwnBase = "http://localhost:5480/ai2p";

    // --- голый URL становится ссылкой ---

    [Theory]
    [InlineData("простой https", "Смотри https://example.com/doc.pdf", "https://example.com/doc.pdf")]
    [InlineData("простой http", "Смотри http://example.com/doc.pdf", "http://example.com/doc.pdf")]
    [InlineData("в начале строки", "http://example.com уже работает", "http://example.com")]
    [InlineData("свой сервер, задача",
        "Открой http://localhost:5480/ai2p/mmfgrp/task/845e8eaa-0189-4a6c-b9ad-761f4e72891a",
        "http://localhost:5480/ai2p/mmfgrp/task/845e8eaa-0189-4a6c-b9ad-761f4e72891a")]
    // свой файл — ссылкой БЕЗ имени хоста (T-142), «&» в HTML — «&amp;»
    [InlineData("свой сервер, файл проекта",
        "Отчёт: http://localhost:5480/ai2p/mmfgrp/api/files/project?projectId=bd47a08f-cfe3-40e1-a60b-54cf362973a1&path=%D0%BE%D1%82%D1%87%D1%91%D1%82.md",
        "/ai2p/mmfgrp/api/files/project?projectId=bd47a08f-cfe3-40e1-a60b-54cf362973a1&amp;path=%D0%BE%D1%82%D1%87%D1%91%D1%82.md")]
    [InlineData("порт без схемы у известного домена", "Смотри http://example.com:8080/a", "http://example.com:8080/a")]
    [InlineData("после двоеточия без пробела", "Ссылка:http://example.com", "http://example.com")]
    [InlineData("после запятой", "раз,http://example.com", "http://example.com")]
    [InlineData("в кавычках-ёлочках", "текст «http://example.com» текст", "http://example.com")]
    [InlineData("в прямых кавычках", "текст \"http://example.com\" текст", "http://example.com")]
    [InlineData("точка в конце предложения", "Смотри http://example.com/doc. Дальше.", "http://example.com/doc")]
    [InlineData("в скобках", "текст (http://localhost:5480/ai2p/x) текст", "http://localhost:5480/ai2p/x")]
    [InlineData("скобка внутри адреса", "Смотри http://localhost:5480/a(b)c", "http://localhost:5480/a(b)c")]
    [InlineData("подчёркивания в пути", "Смотри http://localhost:5480/a_b_c/d", "http://localhost:5480/a_b_c/d")]
    [InlineData("кириллица в пути", "Смотри http://localhost:5480/отчёт.md", "http://localhost:5480/отчёт.md")]
    [InlineData("в ячейке таблицы", "| а | б |\n| --- | --- |\n| http://localhost:5480/a | 2 |", "http://localhost:5480/a")]
    [InlineData("в списке", "* http://localhost:5480/a", "http://localhost:5480/a")]
    [InlineData("в заголовке", "# http://localhost:5480/a", "http://localhost:5480/a")]
    [InlineData("в жирном", "**http://localhost:5480/a**", "http://localhost:5480/a")]
    [InlineData("после переноса строки", "первая строка\nhttp://localhost:5480/a", "http://localhost:5480/a")]
    [InlineData("угловые скобки", "Смотри <http://localhost:5480/a>", "http://localhost:5480/a")]
    public void Bare_url_becomes_link(string name, string markdown, string expectedHref)
    {
        var html = Md.ToHtml(markdown, OwnBase);
        Assert.True(html.Contains($"href=\"{expectedHref}\"", StringComparison.Ordinal),
            $"[{name}] ожидалась ссылка на {expectedHref}, вышло: {html}");
        Assert.Equal(1, Occurrences(html, "<a "));
    }

    [Fact]
    public void Bare_url_link_text_is_the_url_itself()
    {
        Assert.Equal(
            "<p>Смотри <a target=\"_blank\" rel=\"noopener\" href=\"http://localhost:5480/x\">http://localhost:5480/x</a>.</p>\n",
            Md.ToHtml("Смотри http://localhost:5480/x.", ""));
    }

    [Fact]
    public void Www_without_scheme_gets_http()
    {
        var html = Md.ToHtml("Смотри www.example.com/doc", OwnBase);
        Assert.Contains("href=\"http://www.example.com/doc\"", html, StringComparison.Ordinal);
        Assert.Contains(">www.example.com/doc</a>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("два адреса подряд", "http://localhost:5480/a и http://localhost:5480/b", 2)]
    [InlineData("тот же адрес дважды", "http://localhost:5480/a, http://localhost:5480/a", 2)]
    public void Every_url_of_the_text_becomes_a_link(string name, string markdown, int expected)
    {
        var html = Md.ToHtml(markdown, OwnBase);
        Assert.True(Occurrences(html, "<a ") == expected, $"[{name}] вышло: {html}");
    }

    // --- что ссылкой НЕ становится ---

    [Theory]
    [InlineData("код инлайн", "текст `http://localhost:5480/a` текст")]
    [InlineData("блок кода", "```\nhttp://localhost:5480/a\n```")]
    [InlineData("блок кода с языком", "```csharp\nvar u = \"http://localhost:5480/a\";\n```")]
    [InlineData("отступом в четыре пробела", "    http://localhost:5480/a")]
    [InlineData("домен без схемы", "Смотри example.com/doc")]
    [InlineData("хост с портом без схемы", "Открой localhost:5480/ai2p/mmfgrp")]
    [InlineData("почта", "Пиши mike@example.com")]
    [InlineData("имя файла с точкой", "Правил Md.cs и config.json")]
    public void Plain_text_stays_plain(string name, string markdown)
    {
        var html = Md.ToHtml(markdown, OwnBase);
        Assert.True(Occurrences(html, "<a ") == 0, $"[{name}] появилась лишняя ссылка: {html}");
    }

    [Theory]
    [InlineData("готовая ссылка", "[имя](http://localhost:5480/a)", ">имя</a>")]
    [InlineData("картинка", "![пик](http://localhost:5480/a.png)", "<img")]
    public void Existing_markup_is_untouched(string name, string markdown, string expected)
    {
        var html = Md.ToHtml(markdown, OwnBase);
        Assert.True(html.Contains(expected, StringComparison.Ordinal), $"[{name}] вышло: {html}");
        Assert.True(Occurrences(html, "<a ") <= 1, $"[{name}] ссылка задвоилась: {html}");
    }

    // --- своё в текущей вкладке, чужое — в новой (T-142 + T-179) ---

    [Fact]
    public void Foreign_url_opens_in_new_tab()
    {
        var html = Md.ToHtml("Смотри https://example.com/doc", OwnBase);
        Assert.Contains("<a target=\"_blank\" rel=\"noopener\" href=\"https://example.com/doc\"", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Own_url_stays_in_the_same_tab()
    {
        var html = Md.ToHtml("Смотри http://localhost:5480/ai2p/mmfgrp/task/1", OwnBase);
        Assert.Contains("<a href=\"http://localhost:5480/ai2p/mmfgrp/task/1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("_blank", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Own_bare_file_url_becomes_local_link_in_new_tab()
    {
        // свой файл: имя хоста убирается (T-142), вкладка новая — файл открывает браузер
        var html = Md.ToHtml("Файл http://localhost:5480/ai2p/mmfgrp/api/files/raw?path=a.txt", OwnBase);
        Assert.Contains("<a target=\"_blank\" rel=\"noopener\" href=\"/ai2p/mmfgrp/api/files/raw?path=a.txt\"", html,
            StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "target=\"_blank\""));
    }

    // --- список «все файлы» видит те же ссылки (todo30_2) ---

    [Fact]
    public void Files_dialog_sees_the_same_bare_url()
    {
        const string text = "Отчёт http://localhost:5480/ai2p/mmfgrp/api/files/raw?path=projects%2Fp%2Fuploads%2Fa.txt.";
        var links = MdLinks.GetAllLinks(text);
        Assert.Equal(["http://localhost:5480/ai2p/mmfgrp/api/files/raw?path=projects%2Fp%2Fuploads%2Fa.txt"], links);
        Assert.Contains($"href=\"/ai2p/mmfgrp/api/files/raw?path=projects%2Fp%2Fuploads%2Fa.txt\"",
            Md.ToHtml(text, OwnBase), StringComparison.Ordinal);
    }

    // --- раунд-трип MD-редактора (todo30_2): разметка адреса не портит исходный текст ---

    [Theory]
    [InlineData("Смотри http://localhost:5480/ai2p/mmfgrp/task/1")]
    [InlineData("Смотри http://localhost:5480/a. Дальше текст.")]
    [InlineData("текст «http://localhost:5480/a» текст")]
    [InlineData("Ссылка:http://localhost:5480/a")]
    [InlineData("Смотри www.example.com/doc")]
    [InlineData("`http://localhost:5480/a` в коде")]
    public void Round_trip_through_editor_keeps_the_text(string markdown)
    {
        Assert.Equal(markdown, MdHtml.ToMarkdown(Md.ToHtml(markdown, "")));
    }

    private static int Occurrences(string text, string what)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += what.Length;
        }
        return count;
    }
}
