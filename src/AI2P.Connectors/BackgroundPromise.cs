using System.Text;
using System.Text.RegularExpressions;
using AI2P.Core;

namespace AI2P.Connectors;

/// <summary>
/// Распознавание НЕДОДЕЛАННОЙ сдачи задания (T-138): агент запустил долгую работу в фоне
/// (прогон тестов, сборку, скачивание) и сдал результат словами «идёт в фоне, как только
/// закончится — доделаю». Задание при этом закрывается, а вместе с ходом агента обрывается
/// и сам фоновый процесс: продолжения не будет никогда — так на T-137 прогон тестов умер
/// на первых строках лога, а задача ушла в review.
///
/// Здесь только распознавание по тексту ответа; что делать дальше, решает коннектор
/// (<see cref="ClaudeCliConnector"/>): он возвращает агенту ещё один ход в ТОЙ ЖЕ сессии
/// с требованием довести работу до конца либо оформить отложенную проверочную подзадачу.
/// Цена ложного срабатывания — один лишний ход агента, поэтому набор признаков узкий:
/// ловятся обещания будущего действия и прямые указания «работа продолжается в фоне»,
/// а не любое упоминание фона.
/// </summary>
public static class BackgroundPromise
{
    /// <summary>Сколько образцов ищется в словарях: pattern.backgroundPromise.1…N.</summary>
    private const int PatternCount = 6;

    private static Regex[]? _patterns;

    /// <summary>
    /// Признаки незаконченной работы в тексте ответа. Каждый — отдельная причина,
    /// в сообщении агенту показывается найденный фрагмент (ему так понятнее, что именно
    /// сочли обещанием).
    ///
    /// Образцы лежат в словарях (pattern.backgroundPromise.N, T-191) и берутся сразу ПО ВСЕМ
    /// языкам: язык ответа агента задаётся командой (Team.AgentLanguage) и может отличаться
    /// от языка установки, а ответ на смеси языков — обычное дело. Отсюда же и запрет на
    /// <c>Loc.T</c> здесь: с ним английский ответ переставал бы распознаваться на русской
    /// установке. Список строится один раз при первом обращении — словари к этому моменту
    /// уже прочитаны (Program на старте, LocInit в тестах).
    /// </summary>
    private static Regex[] Patterns => _patterns ??= Build();

    private static Regex[] Build()
    {
        var languages = Loc.Languages.Count > 0
            ? Loc.Languages.Union([LocCatalog.BaseLanguage, "en"])
            : [LocCatalog.BaseLanguage, "en"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new List<Regex>();
        foreach (var language in languages)
        {
            for (var i = 1; i <= PatternCount; i++)
            {
                // язык без своего перевода откатывается на базовый — образец не двоим
                var pattern = Loc.In(language, $"pattern.backgroundPromise.{i}");
                if (!pattern.StartsWith("pattern.", StringComparison.Ordinal) && seen.Add(pattern))
                {
                    patterns.Add(new Regex(pattern, Options));
                }
            }
        }
        return [.. patterns];
    }

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>
    /// Похоже ли, что агент сдал задание недоделанным (работа продолжается в фоне либо
    /// обещана «потом»). <paramref name="evidence"/> — найденный фрагмент ответа, он идёт
    /// в сообщение агенту и в технический лог.
    /// </summary>
    public static bool Detect(string answer, out string evidence)
    {
        evidence = "";
        var text = WithoutCode(answer ?? "");
        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                evidence = Sentence(text, match.Index, match.Index + match.Length);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Текст без блоков кода и без inline-кода: в них лежат логи, команды и куски отчётов
    /// («… идёт в фоне …» строкой лога), и признаком незаконченной работы они не являются.
    /// </summary>
    private static string WithoutCode(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inFence = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                continue;
            }
            sb.AppendLine(Regex.Replace(line, "`[^`]*`", " "));
        }
        return sb.ToString();
    }

    /// <summary>Фраза вокруг совпадения — до границ предложения, не длиннее 200 символов.</summary>
    private static string Sentence(string text, int from, int to)
    {
        var start = from;
        while (start > 0 && text[start - 1] is not ('\n' or '.' or '!' or '?'))
        {
            start--;
        }
        var end = to;
        while (end < text.Length && text[end] is not ('\n' or '.' or '!' or '?'))
        {
            end++;
        }
        if (end < text.Length && text[end] is '.' or '!' or '?')
        {
            end++;
        }
        var phrase = text[start..end].Trim();
        return phrase.Length <= 200 ? phrase : phrase[..200] + "…";
    }
}
