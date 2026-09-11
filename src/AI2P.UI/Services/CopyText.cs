using System.Text;

namespace AI2P.UI.Services;

/// <summary>
/// Кусок копируемого текста (T-67-S0): заголовок закладки карточки и её Markdown.
/// Пустой заголовок — копируется ОДНО поле, и подписывать его нечем и незачем.
/// </summary>
/// <param name="Title">Заголовок закладки («Описание», «Чат», …).</param>
/// <param name="Text">Содержимое в Markdown — как оно хранится.</param>
public sealed record CopyPart(string Title, string Text);

/// <summary>
/// Сборка текста задачи для буфера обмена (T-67-S0). Два вида одного и того же набора
/// закладок: ПРОСТОЙ ТЕКСТ (как выделение мышью и Ctrl+C) и ТЕКСТ С ФОРМАТИРОВАНИЕМ
/// (исходный Markdown). Правило одно на оба: пустая закладка не даёт ни текста, ни
/// заголовка.
/// </summary>
public static class CopyText
{
    /// <summary>Одно поле без заголовка — кнопка копирования у самого поля.</summary>
    public static IReadOnlyList<CopyPart> One(string text) => [new CopyPart("", text)];

    /// <summary>Есть ли что копировать (T-108-S0): у пустого поля кнопка копирования не
    /// показывается вовсе — нажимать её незачем. Считается по ИСХОДНОМУ тексту, а не по
    /// собранной строке: собирать её на каждую перерисовку карточки дорого, а пустота
    /// у обоих видов (простой текст и Markdown) одна и та же.</summary>
    public static bool HasText(IEnumerable<CopyPart> parts) =>
        parts.Any(part => !string.IsNullOrWhiteSpace(part.Text));

    /// <param name="parts">Закладки в том порядке, в каком их читает человек.</param>
    /// <param name="markdown">true — исходный Markdown с заголовками «# », false — простой текст.</param>
    public static string Build(IEnumerable<CopyPart> parts, bool markdown)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var body = (markdown ? part.Text : Md.ToPlainText(part.Text)).Trim();
            if (body.Length == 0)
            {
                continue; // «Если там пусто, то и заголовок не вставлять»
            }
            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }
            if (part.Title.Length > 0)
            {
                sb.Append(markdown ? "# " : "").Append(part.Title).Append("\n\n");
            }
            sb.Append(body);
        }
        return sb.ToString();
    }
}
