using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;

namespace AI2P.Connectors;

/// <summary>
/// ВЫГРУЗКА РЕЗУЛЬТАТА ЗАДАЧИ В ОБСУЖДЕНИЕ ИСТОЧНИКА (T-107-S0). Обратное направление
/// к переносу обсуждения в чат (T-152): задача, приехавшая из Trello, GitHub или GitLab,
/// при ОБНОВЛЕНИИ ИЗ ИСТОЧНИКА (T-246) выкладывает свои результаты в конец обсуждения
/// карточки — по одной записи на результат. Результатов у задачи бывает несколько
/// (задание запускали не раз: J-1-result, J-2-result, …), и каждый уезжает своей записью
/// в том порядке, в каком его делали.
///
/// ЗАЩИТА ОТ ПОВТОРА не хранит ничего у себя: у каждой выложенной записи в первой строке
/// стоит МАРКЕР <c>[AI2P:&lt;задача&gt;:&lt;результат&gt;]</c>, а обсуждение мы и так
/// перечитываем тем же сеансом обновления — уже выложенное узнаётся по маркеру в тексте.
/// Поэтому повтор не появится ни после второго нажатия «обновить», ни на другом сервере
/// кластера, ни после переустановки: память о выложенном лежит в самом обсуждении.
/// Маркер намеренно НЕ локализуется — иначе установка с другим языком выложила бы всё заново.
///
/// ЧТО НЕ ВЫКЛАДЫВАЕТСЯ: технические записи. Во-первых, по имени файла — уезжают только
/// <c>*-result.md</c>, а <c>*-error.md</c> (в том числе запись об ОЖИДАНИИ СБРОСА ЛИМИТА
/// исполнителя, T-121, и об истёкшем входе, todo96) остаются в задаче: человеку по ту
/// сторону они не говорят ничего. Во-вторых, по тексту — на случай, если такая запись
/// всё-таки окажется в файле результата (<see cref="IsTechnical"/>).
/// </summary>
public static class ResultExport
{
    /// <summary>Окончание имени файла результата задания: только такие файлы и уезжают.</summary>
    public const string ResultSuffix = "-result.md";

    /// <summary>Предел длины одной записи обсуждения: у Trello это 16 384 знака, у остальных
    /// больше. Длинный результат уезжает началом с припиской — лучше обрезанный, чем отказ.</summary>
    public const int MaxCommentChars = 15000;

    /// <summary>Один результат задачи: имя файла без расширения (J-3-result) и его текст.</summary>
    public sealed record Piece(string Name, string Text);

    /// <summary>Маркер записи — по нему повторная выкладка узнаёт уже выложенное.</summary>
    public static string Marker(string taskDisplayId, string name) =>
        $"[AI2P:{taskDisplayId}:{name}]";

    /// <summary>Текст записи обсуждения: маркер первой строкой, дальше сам результат.</summary>
    public static string Compose(string taskDisplayId, Piece piece)
    {
        var text = piece.Text.Trim();
        if (text.Length > MaxCommentChars)
        {
            text = text[..MaxCommentChars] + "\n\n…";
        }
        return Marker(taskDisplayId, piece.Name) + "\n\n" + text;
    }

    /// <summary>Эта запись результата уже лежит в обсуждении?</summary>
    public static bool AlreadyPosted(IEnumerable<string?> discussion, string taskDisplayId,
        string name)
    {
        var marker = Marker(taskDisplayId, name);
        return discussion.Any(t => t is not null && t.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Результаты задачи, годные к выкладке: файлы <c>*-result.md</c> её каталога artifacts/,
    /// непустые и не технические. Порядок — по номеру задания (J-2 идёт перед J-10:
    /// по имени они отсортировались бы наоборот).
    /// </summary>
    public static List<Piece> Collect(TaskService tasks, TaskItem task)
    {
        var pieces = new List<(int Order, Piece Piece)>();
        foreach (var rel in tasks.Artifacts(task))
        {
            if (!rel.EndsWith(ResultSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var text = tasks.ReadArtifact(rel).Trim();
            if (text.Length == 0 || IsTechnical(text))
            {
                continue;
            }
            var name = Path.GetFileNameWithoutExtension(rel);
            pieces.Add((JobNumberOf(name), new Piece(name, text)));
        }
        return pieces.OrderBy(p => p.Order).ThenBy(p => p.Piece.Name, StringComparer.Ordinal)
            .Select(p => p.Piece).ToList();
    }

    /// <summary>Результаты, которых в обсуждении ещё нет (в порядке их появления).</summary>
    public static List<Piece> Pending(TaskService tasks, TaskItem task,
        IEnumerable<string?> discussion)
    {
        var texts = discussion.ToList();
        return Collect(tasks, task)
            .Where(p => !AlreadyPosted(texts, task.DisplayId, p.Name))
            .ToList();
    }

    /// <summary>Номер задания из имени файла результата («J-12-result» → 12); не разобрали —
    /// в конец списка.</summary>
    private static int JobNumberOf(string name)
    {
        var digits = new string(name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    /// <summary>Ключи технических сообщений, которые наружу не уезжают: ожидание сброса
    /// лимита исполнителя (T-121), истёкший вход в CLI (todo96) и шапка записи об ошибке.</summary>
    private static readonly string[] TechnicalKeys =
        ["msg.aiConnectorBase.15", "msg.aiConnectorBase.27", "msg.aiConnectorBase.13"];

    /// <summary>
    /// Техническая запись? Сравнивается НАЧАЛО текста с началом сообщения — до первой
    /// подстановки — и притом на ВСЕХ языках установки сразу: запись мог сделать сервер
    /// кластера с другим языком, а выкладывает её тот, у кого нажали «обновить».
    /// </summary>
    public static bool IsTechnical(string text)
    {
        var head = text.TrimStart();
        foreach (var lang in Loc.Languages)
        {
            foreach (var key in TechnicalKeys)
            {
                var prefix = LiteralHead(Loc.In(lang, key));
                if (prefix.Length >= 8 && head.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Начало шаблона сообщения до первой подстановки {0} — по нему и узнаём запись.</summary>
    private static string LiteralHead(string template)
    {
        var brace = template.IndexOf('{');
        return (brace < 0 ? template : template[..brace]).Trim();
    }
}
