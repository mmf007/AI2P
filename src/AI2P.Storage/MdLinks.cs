using System.Text.RegularExpressions;

namespace AI2P.Storage;

/// <summary>Ссылка из Markdown-текста: URL и описание — текст в квадратных скобках
/// перед ссылкой (todo30_2); у голых URL и автоссылок описания нет.</summary>
public sealed record MdLinkRef(string Url, string Text);

/// <summary>
/// Извлечение ссылок из Markdown-текстов (ТЗ v1.28, todo30) без зависимости от Markdig:
/// один внутренний метод GetAllLinks — на нём построены каскадное удаление вставленных
/// файлов при удалении задачи и кнопка «все файлы» карточки задачи.
/// Помимо [текст](url) и ![alt](url) собираются автоссылки &lt;url&gt; и голые URL
/// (todo30_2): в чате ссылки обычно вставляют голым адресом, рендер показывает их
/// ссылками (autolinks) — список «все файлы» должен их видеть.
/// </summary>
public static partial class MdLinks
{
    /// <summary>Префикс ссылок на файлы, закачанные MD-редактором (uploads/ проекта, гл. 11).</summary>
    private const string RawPrefix = "api/files/raw?path=";

    /// <summary>Знаки препинания, прилипающие к голому URL в конце предложения.</summary>
    private const string TrailingPunctuation = ".,;:!?'\"»«";

    /// <summary>Три вида ссылок, в порядке приоритета на одной позиции:
    /// инлайн [текст](url) и ![alt](url) — URL до пробела или закрывающей скобки
    /// (необязательный title и атрибуты {width=…} отбрасываются); автоссылка &lt;url&gt;;
    /// голый URL (http/https/www) — как автолинки Markdig.</summary>
    [GeneratedRegex(@"!?\[(?<text>[^\]]*)\]\(\s*<?(?<url>[^)\s>]+)>?[^)]*\)|<(?<angle>https?://[^>\s]+)>|(?<bare>(?:https?://|www\.)[^\s<>()\[\]]+)")]
    private static partial Regex LinkRef();

    /// <summary>Все ссылки текста с описаниями, в порядке появления, без дублей по URL;
    /// у дубля с пустым описанием описание дополняется из следующих вхождений.</summary>
    public static List<MdLinkRef> GetAllLinkRefs(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }
        var byUrl = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<MdLinkRef>();
        foreach (Match match in LinkRef().Matches(markdown))
        {
            string url;
            var text = "";
            if (match.Groups["url"].Success)
            {
                url = match.Groups["url"].Value.Trim();
                text = match.Groups["text"].Value.Trim();
            }
            else if (match.Groups["angle"].Success)
            {
                url = match.Groups["angle"].Value;
            }
            else
            {
                url = match.Groups["bare"].Value.TrimEnd(TrailingPunctuation.ToCharArray());
            }
            if (url.Length == 0)
            {
                continue;
            }
            if (byUrl.TryGetValue(url, out var at))
            {
                if (result[at].Text.Length == 0 && text.Length > 0)
                {
                    result[at] = result[at] with { Text = text };
                }
                continue;
            }
            byUrl[url] = result.Count;
            result.Add(new MdLinkRef(url, text));
        }
        return result;
    }

    /// <summary>Все URL из ссылок и картинок Markdown-текста, в порядке появления, без дублей.</summary>
    public static List<string> GetAllLinks(string markdown) =>
        GetAllLinkRefs(markdown).Select(r => r.Url).ToList();

    /// <summary>
    /// Относительные пути файлов uploads/ (dataDir), на которые указывают ссылки:
    /// «api/files/raw?path=&lt;encoded&gt;» → «projects/&lt;slug&gt;/uploads/…». Прочие ссылки
    /// (внешние URL, файлы проекта, задачи) отбрасываются — удалять их нельзя.
    /// </summary>
    public static List<string> UploadPathsOf(IEnumerable<string> links)
    {
        var result = new List<string>();
        foreach (var link in links)
        {
            var at = link.IndexOf(RawPrefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }
            var encoded = link[(at + RawPrefix.Length)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }
            string path;
            try
            {
                path = Uri.UnescapeDataString(encoded).Replace('\\', '/').Trim();
            }
            catch (UriFormatException)
            {
                continue;
            }
            if (path.StartsWith("projects/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/uploads/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(".."))
            {
                result.Add(path);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
