using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AI2P.UI.Services;

/// <summary>
/// Обратное преобразование HTML → Markdown (todo30_2): предпросмотр MD-редактора —
/// contenteditable с HTML от Markdig; после правок браузер отдаёт innerHTML, этот класс
/// переводит его обратно в Markdown. Разбирается только HTML двух источников — нашего
/// пайплайна Markdig и contenteditable браузеров (innerHTML сериализуется браузером
/// дисциплинированно: атрибуты в кавычках, сущности экранированы), поэтому парсер
/// маленький и без внешних зависимостей.
/// </summary>
public static partial class MdHtml
{
    /// <summary>
    /// Жёсткий перенос строки (&lt;br&gt;) в Markdown — ДВА ПРОБЕЛА в конце строки, а не «\»
    /// (todo37): пользователь пишет переносы пробелами, и обратный «\» был видимой порчей
    /// текста при каждом переключении предпросмотра. Оба варианта равнозначны для CommonMark,
    /// но пробелы невидимы и совпадают с исходным текстом.
    /// </summary>
    private const string HardBreak = "  \n";

    // --- разбор HTML в дерево ---

    private abstract class Node;

    private sealed class TextNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class Elem(string tag) : Node
    {
        public string Tag { get; } = tag;
        public Dictionary<string, string> Attrs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Node> Children { get; } = [];

        public string Attr(string name) => Attrs.GetValueOrDefault(name, "");
    }

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li",
        "blockquote", "pre", "table", "thead", "tbody", "tr", "hr", "figure",
    };

    public static string ToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        var root = new Elem("#root");
        Parse(html, root);
        var markdown = RenderBlocks(root.Children);
        return markdown.Trim('\n');
    }

    private static void Parse(string html, Elem root)
    {
        var stack = new List<Elem> { root };
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                AddText(stack, html[i..]);
                break;
            }
            if (lt > i)
            {
                AddText(stack, html[i..lt]);
            }
            if (html.AsSpan(lt).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", lt, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }
            if (lt + 1 < html.Length && (html[lt + 1] == '!' || html[lt + 1] == '?'))
            {
                var end = html.IndexOf('>', lt);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }
            var gt = html.IndexOf('>', lt);
            if (gt < 0)
            {
                AddText(stack, html[lt..]);
                break;
            }
            var inner = html[(lt + 1)..gt].Trim();
            i = gt + 1;
            if (inner.StartsWith('/'))
            {
                CloseTag(stack, inner[1..].Trim().ToLowerInvariant());
                continue;
            }
            var selfClosed = inner.EndsWith('/');
            if (selfClosed)
            {
                inner = inner[..^1].TrimEnd();
            }
            var elem = ParseTag(inner);
            if (elem is null)
            {
                continue;
            }
            stack[^1].Children.Add(elem);
            if (!selfClosed && !VoidTags.Contains(elem.Tag))
            {
                if (elem.Tag is "script" or "style")
                {
                    // содержимое не разбирается и не попадает в результат
                    var close = html.IndexOf("</" + elem.Tag, i, StringComparison.OrdinalIgnoreCase);
                    var closeEnd = close < 0 ? -1 : html.IndexOf('>', close);
                    i = closeEnd < 0 ? html.Length : closeEnd + 1;
                }
                else
                {
                    stack.Add(elem);
                }
            }
        }
    }

    private static void AddText(List<Elem> stack, string raw)
    {
        if (raw.Length > 0)
        {
            stack[^1].Children.Add(new TextNode(WebUtility.HtmlDecode(raw)));
        }
    }

    private static void CloseTag(List<Elem> stack, string tag)
    {
        for (var s = stack.Count - 1; s >= 1; s--)
        {
            if (stack[s].Tag == tag)
            {
                stack.RemoveRange(s, stack.Count - s);
                return;
            }
        }
        // закрывающий тег без открывающего — игнорируется
    }

    [GeneratedRegex("""([a-zA-Z][a-zA-Z0-9-]*)\s*(?:=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?""")]
    private static partial Regex AttrRegex();

    private static Elem? ParseTag(string inner)
    {
        var nameEnd = 0;
        while (nameEnd < inner.Length && !char.IsWhiteSpace(inner[nameEnd]))
        {
            nameEnd++;
        }
        var name = inner[..nameEnd].ToLowerInvariant();
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
        {
            return null;
        }
        var elem = new Elem(name);
        foreach (Match m in AttrRegex().Matches(inner[nameEnd..]))
        {
            var value = m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value
                : m.Groups[4].Value;
            elem.Attrs[m.Groups[1].Value] = WebUtility.HtmlDecode(value);
        }
        return elem;
    }

    // --- рендер дерева в Markdown ---

    private static bool IsBlock(Node node) => node is Elem e && BlockTags.Contains(e.Tag);

    /// <summary>Дети контейнера: блочные элементы — сверху вниз, подряд идущие инлайновые
    /// узлы собираются в абзац; блоки разделяются пустой строкой.</summary>
    private static string RenderBlocks(List<Node> children)
    {
        var blocks = new List<string>();
        var inline = new List<Node>();
        void Flush()
        {
            if (inline.Count == 0)
            {
                return;
            }
            var text = TrimHardBreaks(RenderInline(inline).Trim());
            if (text.Length > 0)
            {
                blocks.Add(EscapeLineStarts(text));
            }
            inline.Clear();
        }
        foreach (var child in children)
        {
            if (IsBlock(child))
            {
                Flush();
                var block = RenderBlock((Elem)child);
                if (block.Length > 0)
                {
                    blocks.Add(block);
                }
            }
            else if (child is TextNode t && t.Text.Trim().Length == 0 && inline.Count == 0)
            {
                // межблочные переводы строк из Markdig — не абзац
            }
            else
            {
                inline.Add(child);
            }
        }
        Flush();
        return string.Join("\n\n", blocks);
    }

    private static string RenderBlock(Elem e) => e.Tag switch
    {
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" =>
            HeadingOf(e.Tag[1] - '0', e),
        "p" or "div" or "figure" =>
            e.Children.Any(IsBlock) ? RenderBlocks(e.Children) : ParagraphOf(e),
        "blockquote" => PrefixLines(RenderBlocks(e.Children), "> "),
        "ul" => RenderList(e, ordered: false),
        "ol" => RenderList(e, ordered: true),
        "pre" => RenderCodeBlock(e),
        "table" => RenderTable(e),
        "hr" => "---",
        // thead/tbody/tr/li вне своего контейнера (обрезки contenteditable) — как контейнер
        _ => RenderBlocks(e.Children),
    };

    private static string HeadingOf(int level, Elem e)
    {
        var text = TrimHardBreaks(RenderInline(e.Children).Trim());
        return text.Length == 0 ? "" : new string('#', level) + " " + text;
    }

    private static string ParagraphOf(Elem e)
    {
        var text = TrimHardBreaks(RenderInline(e.Children).Trim());
        return text.Length == 0 ? "" : EscapeLineStarts(text);
    }

    private static string RenderList(Elem list, bool ordered)
    {
        var items = new List<string>();
        var index = 0;
        foreach (var child in list.Children)
        {
            if (child is not Elem li || li.Tag != "li")
            {
                continue;
            }
            index++;
            var marker = ordered ? $"{index}. " : "* ";
            // задача-чекбокс (Markdig task lists): первый input[type=checkbox] → [x]/[ ]
            var checkbox = li.Children.OfType<Elem>()
                .FirstOrDefault(c => c.Tag == "input" &&
                    c.Attr("type").Equals("checkbox", StringComparison.OrdinalIgnoreCase));
            var lead = "";
            if (checkbox is not null)
            {
                lead = checkbox.Attrs.ContainsKey("checked") ? "[x] " : "[ ] ";
            }
            var nested = li.Children.Where(c => c is Elem { Tag: "ul" or "ol" }).Cast<Elem>().ToList();
            var own = li.Children.Where(c => !nested.Contains(c) && c != checkbox).ToList();
            var body = RenderBlocks(own);
            if (body.Length == 0)
            {
                body = "";
            }
            var indent = new string(' ', marker.Length);
            var lines = new StringBuilder(marker + lead + IndentContinuation(body, indent));
            foreach (var sub in nested)
            {
                var subMd = RenderList(sub, sub.Tag == "ol");
                if (subMd.Length > 0)
                {
                    lines.Append('\n').Append(PrefixLines(subMd, indent));
                }
            }
            items.Add(lines.ToString());
        }
        return string.Join("\n", items);
    }

    private static string RenderCodeBlock(Elem pre)
    {
        var code = pre.Children.OfType<Elem>().FirstOrDefault(c => c.Tag == "code");
        var lang = "";
        if (code is not null)
        {
            var m = Regex.Match(code.Attr("class"), @"language-(\S+)");
            lang = m.Success ? m.Groups[1].Value : "";
        }
        var content = TextContent(code ?? pre).Replace('\u00A0', ' ').TrimEnd('\n');
        var fence = content.Contains("```") ? "````" : "```";
        return fence + lang + "\n" + content + "\n" + fence;
    }

    private static string RenderTable(Elem table)
    {
        var rows = new List<List<string>>();
        void CollectRows(Elem container)
        {
            foreach (var child in container.Children.OfType<Elem>())
            {
                if (child.Tag == "tr")
                {
                    rows.Add(child.Children.OfType<Elem>()
                        .Where(c => c.Tag is "td" or "th")
                        // переносов внутри ячейки таблица Markdown не держит: жёсткий
                        // перенос («  \n») и мягкий сводятся к пробелу (todo37)
                        .Select(c => RenderInline(c.Children).Trim()
                            .Replace(HardBreak, " ").Replace("\n", " ").Replace("|", "\\|"))
                        .ToList());
                }
                else if (child.Tag is "thead" or "tbody" or "tfoot")
                {
                    CollectRows(child);
                }
            }
        }
        CollectRows(table);
        if (rows.Count == 0)
        {
            return "";
        }
        var cols = rows.Max(r => r.Count);
        var sb = new StringBuilder();
        sb.Append("| ").AppendJoin(" | ", rows[0].Concat(Enumerable.Repeat("", cols - rows[0].Count))).Append(" |\n");
        sb.Append("|").AppendJoin("|", Enumerable.Repeat(" --- ", cols)).Append('|');
        foreach (var row in rows.Skip(1))
        {
            sb.Append("\n| ").AppendJoin(" | ", row.Concat(Enumerable.Repeat("", cols - row.Count))).Append(" |");
        }
        return sb.ToString();
    }

    private static string RenderInline(IEnumerable<Node> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    sb.Append(EscapeText(CollapseSpaces(t.Text)));
                    break;
                case Elem e:
                    RenderInlineElem(sb, e);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void RenderInlineElem(StringBuilder sb, Elem e)
    {
        switch (e.Tag)
        {
            case "strong" or "b":
                WrapEmphasis(sb, e, "**");
                break;
            case "em" or "i":
                WrapEmphasis(sb, e, "*");
                break;
            case "del" or "s" or "strike":
                WrapEmphasis(sb, e, "~~");
                break;
            case "code":
                AppendInlineCode(sb, TextContent(e).Replace('\u00A0', ' '));
                break;
            case "a":
            {
                var href = e.Attr("href");
                var text = TrimHardBreaks(RenderInline(e.Children).Trim());
                // «www.имя» без схемы: в ссылке схема дописана (T-179), в тексте её нет —
                // иначе редактор превращал бы голый адрес в [www.имя](http://www.имя)
                var plain = Unescape(text);
                if (text.Length == 0 || text == href || plain == href)
                {
                    sb.Append(href); // автоссылка: голый URL остаётся голым
                }
                else if ("http://" + plain == href)
                {
                    sb.Append(plain); // «www.имя» — без дописанной схемы, как человек и написал
                }
                else
                {
                    sb.Append('[').Append(text).Append("](").Append(href).Append(')');
                }
                break;
            }
            case "img":
                sb.Append("![").Append(e.Attr("alt").Replace("[", "").Replace("]", ""))
                  .Append("](").Append(e.Attr("src")).Append(')');
                AppendWidth(sb, e.Attr("width"), percentOnly: false);
                break;
            case "video":
            {
                var src = e.Attr("src");
                if (src.Length == 0)
                {
                    src = e.Children.OfType<Elem>().FirstOrDefault(c => c.Tag == "source")?.Attr("src") ?? "";
                }
                sb.Append("![](").Append(src).Append(')');
                // у плеера Markdig всегда есть числовые width/height — маркер ширины
                // пишем только для процентной (выставленной пользователем)
                AppendWidth(sb, e.Attr("width"), percentOnly: true);
                break;
            }
            case "iframe":
                sb.Append("![](").Append(e.Attr("src")).Append(')');
                break;
            case "br":
                sb.Append(HardBreak);
                break;
            case "input":
                break; // чекбоксы обрабатывает список
            default:
                // span/u/font и незнакомые инлайны (вставка извне) — разворачиваются в текст;
                // блочный элемент во «встроенном» контексте — тоже содержимым
                sb.Append(e.Children.Any(IsBlock)
                    ? RenderBlocks(e.Children).Replace("\n\n", "\n")
                    : RenderInline(e.Children));
                break;
        }
    }

    private static void WrapEmphasis(StringBuilder sb, Elem e, string marker)
    {
        var inner = RenderInline(e.Children);
        var trimmed = inner.Trim();
        if (trimmed.Length == 0)
        {
            sb.Append(inner);
            return;
        }
        if (inner.Length > 0 && char.IsWhiteSpace(inner[0]))
        {
            sb.Append(' ');
        }
        sb.Append(marker).Append(trimmed).Append(marker);
        if (inner.Length > 0 && char.IsWhiteSpace(inner[^1]))
        {
            sb.Append(' ');
        }
    }

    private static void AppendInlineCode(StringBuilder sb, string raw)
    {
        if (raw.Length == 0)
        {
            return;
        }
        var fence = raw.Contains('`') ? "``" : "`";
        var pad = raw.Contains('`') ? " " : "";
        sb.Append(fence).Append(pad).Append(raw).Append(pad).Append(fence);
    }

    private static void AppendWidth(StringBuilder sb, string width, bool percentOnly)
    {
        if (width.Length == 0 || (percentOnly && !width.EndsWith('%')))
        {
            return;
        }
        sb.Append("{width=").Append(width).Append('}');
    }

    private static string TextContent(Elem e)
    {
        var sb = new StringBuilder();
        void Walk(Node n)
        {
            switch (n)
            {
                case TextNode t:
                    sb.Append(t.Text);
                    break;
                case Elem { Tag: "br" }:
                    sb.Append('\n');
                    break;
                case Elem el:
                    el.Children.ForEach(Walk);
                    break;
            }
        }
        e.Children.ForEach(Walk);
        return sb.ToString();
    }

    // --- текст: пробелы и экранирование ---

    private static string CollapseSpaces(string text) =>
        Regex.Replace(text.Replace('\u00A0', ' '), @"\s+", " ");

    /// <summary>Экранирование спецсимволов Markdown в обычном тексте; слова-URL
    /// (http…, www…) не трогаются, чтобы не ломать адреса.</summary>
    private static string EscapeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ')
            {
                sb.Append(' ');
                i++;
                continue;
            }
            var end = text.IndexOf(' ', i);
            if (end < 0)
            {
                end = text.Length;
            }
            var word = text[i..end];
            i = end;
            if (word.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(word);
                continue;
            }
            for (var k = 0; k < word.Length; k++)
            {
                var ch = word[k];
                // «_» ВНУТРИ слова курсива не даёт (CommonMark: intraword emphasis отключён
                // именно для подчёркивания), поэтому не экранируется — иначе каждое
                // переключение предпросмотра портило snake_case добавленным «\» (todo37)
                var intrawordUnderscore = ch == '_' && k > 0 && k < word.Length - 1;
                if ((ch is '\\' or '`' or '*' or '_' or '[' or ']' or '~' or '{') && !intrawordUnderscore)
                {
                    sb.Append('\\');
                }
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string Unescape(string text) => Regex.Replace(text, @"\\(.)", "$1");

    /// <summary>Символы, значимые только в начале строки (#, &gt;, маркеры списков, ---).</summary>
    private static string EscapeLineStarts(string block)
    {
        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"^#{1,6}( |$)") || line.StartsWith("> ")
                || line.StartsWith("- ") || line.StartsWith("+ ")
                || Regex.IsMatch(line, @"^(-+|=+)\s*$"))
            {
                lines[i] = "\\" + line;
            }
            else
            {
                lines[i] = Regex.Replace(line, @"^(\d+)([.)] )", "$1\\$2");
            }
        }
        return string.Join('\n', lines);
    }

    private static string TrimHardBreaks(string text)
    {
        // пробел после жёсткого переноса — мусор рендера (в HTML это перевод строки исходника)
        text = text.Replace(HardBreak + " ", HardBreak);
        // висящий жёсткий перенос в начале/конце блока смысла не несёт
        while (text.EndsWith(HardBreak) || text.EndsWith('\\'))
        {
            text = text.EndsWith(HardBreak) ? text[..^HardBreak.Length].TrimEnd() : text[..^1].TrimEnd();
        }
        while (text.StartsWith(HardBreak))
        {
            text = text[HardBreak.Length..].TrimStart();
        }
        return text;
    }

    private static string PrefixLines(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(l => l.Length == 0 ? prefix.TrimEnd() : prefix + l));

    private static string IndentContinuation(string text, string indent)
    {
        var lines = text.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            lines[i] = lines[i].Length == 0 ? "" : indent + lines[i];
        }
        return string.Join('\n', lines);
    }
}
