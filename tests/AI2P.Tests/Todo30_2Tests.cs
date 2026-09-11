using AI2P.Storage;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// md улучшенный, доработка (todo30_2): редактируемый предпросмотр без сплита —
/// обратное преобразование HTML → Markdown (MdHtml) и раунд-трип через Markdig (Md);
/// список «все файлы» — голые URL из чата (autolinks) и описание из квадратных скобок
/// (MdLinks.GetAllLinkRefs).
/// </summary>
public sealed class Todo30_2Tests
{
    // --- MdHtml: раунд-трип Markdown → HTML (Markdig) → Markdown ---

    [Theory]
    [InlineData("## Заголовок\n\nАбзац с **жирным** и *курсивом* и `кодом`.")]
    [InlineData("* один\n* два\n* три")]
    [InlineData("1. первый\n2. второй")]
    [InlineData("* родитель\n  * дитя")]
    [InlineData("> цитата")]
    [InlineData("```csharp\nvar x = 1;\n```")]
    [InlineData("[файл](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.txt)")]
    [InlineData("![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png)")]
    [InlineData("![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png){width=50%}")]
    [InlineData("Смотри https://example.com/doc.pdf")]
    [InlineData("| а | б |\n| --- | --- |\n| 1 | 2 |")]
    [InlineData("* [x] сделано\n* [ ] не сделано")]
    [InlineData("~~зачёркнуто~~ обычный")]
    [InlineData("звёздочка \\*не жирный\\* и \\_не курсив\\_")]
    [InlineData("# Раз\n\nТекст\n\n---\n\n## Два")]
    public void MdHtml_round_trips_markdown_through_markdig(string markdown)
    {
        var html = Md.ToHtml(markdown);
        Assert.Equal(markdown, MdHtml.ToMarkdown(html));
    }

    [Fact]
    public void MdHtml_round_trip_is_stable_after_first_normalization()
    {
        // после первой нормализации (потери типа title у ссылок допустимы) результат
        // обязан быть неподвижной точкой: повторный раунд-трип ничего не меняет
        const string source = "Мягкий\nперенос и [ссылка](url \"заголовок\")\n\n* пункт  с   пробелами";
        var once = MdHtml.ToMarkdown(Md.ToHtml(source));
        var twice = MdHtml.ToMarkdown(Md.ToHtml(once));
        Assert.Equal(once, twice);
    }

    // --- MdHtml: HTML от contenteditable (правки в предпросмотре) ---

    [Fact]
    public void MdHtml_converts_contenteditable_markup()
    {
        // Chrome: новые строки — div, execCommand bold/italic — b/i, пробелы — &nbsp;
        var markdown = MdHtml.ToMarkdown(
            "<div>строка <b>жирно</b>&nbsp;и <i>наклонно</i></div><div><br></div><div>второй абзац</div>");
        Assert.Equal("строка **жирно** и *наклонно*\n\nвторой абзац", markdown);
    }

    [Fact]
    public void MdHtml_escapes_markdown_specials_in_plain_text()
    {
        Assert.Equal("текст со \\*звёздочками\\* и \\[скобками\\]",
            MdHtml.ToMarkdown("<p>текст со *звёздочками* и [скобками]</p>"));
        // URL не экранируется, иначе сломается адрес
        Assert.Equal("см. https://a.b/c_d",
            MdHtml.ToMarkdown("<p>см. https://a.b/c_d</p>"));
    }

    [Fact]
    public void MdHtml_converts_video_player_back_to_image_link()
    {
        // плеер Markdig: числовые width/height по умолчанию не переносятся в Markdown,
        // процентная ширина (меню «ширина» в предпросмотре) — переносится
        Assert.Equal("![](api/files/raw?path=v.mp4)",
            MdHtml.ToMarkdown("""<video width="500" height="281" controls=""><source type="video/mp4" src="api/files/raw?path=v.mp4"></source></video>"""));
        Assert.Equal("![](api/files/raw?path=v.mp4){width=50%}",
            MdHtml.ToMarkdown("""<video width="50%" controls=""><source type="video/mp4" src="api/files/raw?path=v.mp4"></source></video>"""));
    }

    [Fact]
    public void MdHtml_unwraps_unknown_tags_and_skips_scripts()
    {
        Assert.Equal("обычный текст",
            MdHtml.ToMarkdown("<p><span style=\"color:red\">обычный</span> <u>текст</u></p>"));
        Assert.Equal("до после",
            MdHtml.ToMarkdown("<p>до <script>alert(1)</script>после</p>"));
    }

    // --- MdLinks.GetAllLinkRefs: описание и голые URL (чат) ---

    [Fact]
    public void GetAllLinkRefs_returns_bracket_text_as_description()
    {
        var refs = MdLinks.GetAllLinkRefs(
            "Смотри [отчёт за месяц](api/files/raw?path=r.pdf) и ![скриншот](api/files/raw?path=s.png)");
        Assert.Equal(2, refs.Count);
        Assert.Equal("отчёт за месяц", refs[0].Text);
        Assert.Equal("api/files/raw?path=r.pdf", refs[0].Url);
        Assert.Equal("скриншот", refs[1].Text);
    }

    [Fact]
    public void GetAllLinkRefs_finds_bare_urls_from_chat()
    {
        // в чате ссылки обычно вставляют голым адресом — рендер показывает их ссылками
        // (autolinks), список «все файлы» обязан их видеть (todo30_2)
        var refs = MdLinks.GetAllLinkRefs("глянь https://example.com/doc.pdf, и ещё <https://a.b/c>");
        Assert.Equal(2, refs.Count);
        Assert.Equal("https://example.com/doc.pdf", refs[0].Url);
        Assert.Equal("", refs[0].Text);
        Assert.Equal("https://a.b/c", refs[1].Url);
    }

    [Fact]
    public void GetAllLinkRefs_deduplicates_and_fills_empty_description()
    {
        var refs = MdLinks.GetAllLinkRefs(
            "https://a.b/f.txt и снова [файл](https://a.b/f.txt) и ![](x.png) и [имя](x.png)");
        Assert.Equal(2, refs.Count);
        Assert.Equal("файл", refs[0].Text);  // описание дополнено из второго вхождения
        Assert.Equal("имя", refs[1].Text);
    }

    [Fact]
    public void GetAllLinks_still_returns_urls_in_order_without_duplicates()
    {
        var links = MdLinks.GetAllLinks("""
            Текст ![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png) и
            голый http://localhost:5480/ai2p/api/files/raw?path=b.txt
            повтор ![пик](api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png)
            """);
        Assert.Equal(2, links.Count);
        Assert.Equal("api/files/raw?path=projects%2Fp%2Fuploads%2Fa.png", links[0]);
        Assert.Equal("http://localhost:5480/ai2p/api/files/raw?path=b.txt", links[1]);
    }
}
