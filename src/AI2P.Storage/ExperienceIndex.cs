using System.Text;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage;

/// <summary>
/// ЛЕКСИЧЕСКИЙ ИНДЕКС ПО ТЕКСТУ ЗАПИСЕЙ ОПЫТА (T-268-S0, ветка T-318 «Проект опыта»).
///
/// <para>Зачем: до этой версии достать запись опыта, не попавшую в задание, было НЕЧЕМ —
/// у агента есть только create_experience и update_experience, а подсказка про отброшенные
/// записи предлагала «спроси человека». Индекс — основа инструмента <c>search_experience</c>
/// и строки поиска в списках опыта.</para>
///
/// <para>ТАБЛИЦА ПРОИЗВОДНАЯ И НЕ РЕПЛИЦИРУЕТСЯ: в <see cref="ChangeLog.OrgTables"/> её нет
/// намеренно — это белый список, и виртуальная таблица FTS5 в него не попадает. Потеряется —
/// восстановится догоняющим проходом <see cref="CatchUp"/> при открытии организации.</para>
///
/// <para>ДЕГРАДАЦИЯ: FTS5 собран не во всякой сборке SQLite, поэтому доступность проверяется
/// ОДНОЙ строкой SQL (<see cref="Available"/>) — временная виртуальная таблица; не вышло —
/// поиск работает подстрочным сравнением по тексту записей (см.
/// <c>ExperienceService.Search</c>). В нашей сборке SQLitePCLRaw/e_sqlite3 FTS5 есть
/// (ENABLE_FTS5 в нативной библиотеке), но полагаться на это в коде нельзя: на Linux
/// и macOS бывает системный e_sqlite3.</para>
/// </summary>
public static class ExperienceIndex
{
    /// <summary>Имя виртуальной таблицы индекса.</summary>
    public const string Table = "experience_fts";

    /// <summary>Доступность FTS5 в НАТИВНОЙ БИБЛИОТЕКЕ — свойство процесса, а не базы,
    /// поэтому проверяется один раз.</summary>
    private static bool? _available;

    /// <summary>
    /// FTS5 доступен. Проверка — ОДНА строка SQL: временная виртуальная таблица. Полагаться
    /// на <c>sqlite_compileoption_used</c> нельзя: расширение бывает подгружено отдельно,
    /// и наоборот — объявлено, но не собрано.
    /// </summary>
    public static bool Available(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        if (_available is { } known)
        {
            return known;
        }
        try
        {
            // ТРАНЗАКЦИЯ ПЕРЕДАЁТСЯ НАСКВОЗЬ: индекс пополняется ВНУТРИ транзакции записи
            // опыта, а Microsoft.Data.Sqlite отказывает команде без Transaction, пока на
            // соединении открыта своя («Execute requires the command to have a transaction
            // object…») — то есть первая же запись опыта падала бы на проверке
            Sql.Exec(conn, tx, "CREATE VIRTUAL TABLE IF NOT EXISTS temp.ai2p_fts_probe USING fts5(t)");
            Sql.Exec(conn, tx, "DROP TABLE IF EXISTS temp.ai2p_fts_probe");
            _available = true;
        }
        catch (SqliteException)
        {
            _available = false;
        }
        return _available.Value;
    }

    /// <summary>
    /// Завести таблицу индекса (идемпотентно). Зовётся из <see cref="Database.Init"/> ПОСЛЕ
    /// схемы и миграций: индексируется колонка <c>text</c>, идентификатор записи лежит рядом
    /// без индексации (UNINDEXED) — искать по нему незачем, а отдавать надо.
    /// <para>Токенизатор <c>unicode61</c> с <c>remove_diacritics 2</c>: он сворачивает регистр
    /// и у кириллицы тоже. Морфологии у него нет никакой — её отчасти заменяет поиск
    /// по префиксу (<see cref="MatchExpression"/>).</para>
    /// </summary>
    public static void Install(SqliteConnection conn)
    {
        if (!Available(conn))
        {
            return;
        }
        try
        {
            Sql.Exec(conn, null, $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS {Table}
                USING fts5(id UNINDEXED, text, tokenize='unicode61 remove_diacritics 2')
                """);
        }
        catch (SqliteException)
        {
            _available = false;   // таблицу не завести — работаем без индекса
        }
    }

    /// <summary>Индекс на этой базе есть (таблица заведена и FTS5 доступен).</summary>
    public static bool Ready(SqliteConnection conn, SqliteTransaction? tx = null) =>
        Available(conn, tx)
        && Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n", ("@n", Table)) > 0;

    /// <summary>Положить (переложить) запись в индекс — при создании и при правке текста.</summary>
    public static void Put(SqliteConnection conn, SqliteTransaction? tx, string id, string text)
    {
        if (!Ready(conn, tx))
        {
            return;
        }
        try
        {
            Remove(conn, tx, id);
            Sql.Exec(conn, tx, $"INSERT INTO {Table} (id, text) VALUES (@id, @t)",
                ("@id", id), ("@t", text));
        }
        catch (SqliteException)
        {
            // индекс производный: его сбой не имеет права сорвать запись самого опыта
        }
    }

    /// <summary>Убрать запись из индекса — при удалении.</summary>
    public static void Remove(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        if (!Ready(conn, tx))
        {
            return;
        }
        try
        {
            Sql.Exec(conn, tx, $"DELETE FROM {Table} WHERE id=@id", ("@id", id));
        }
        catch (SqliteException)
        {
        }
    }

    /// <summary>
    /// ДОГОНЯЮЩИЙ ПРОХОД: заиндексировать всё, чего в индексе нет, и выбросить то, чего
    /// больше нет в опыте. Нужен потому, что записи приезжают РЕПЛИКАЦИЕЙ (журнал изменений
    /// пишет строку прямо в таблицу, мимо сервиса) и потому, что индекс не реплицируется.
    /// Зовётся при открытии организации.
    /// </summary>
    /// <returns>Сколько записей добавлено в индекс.</returns>
    public static int CatchUp(SqliteConnection conn)
    {
        if (!Ready(conn))
        {
            return 0;
        }
        try
        {
            Sql.Exec(conn, null, $"""
                DELETE FROM {Table}
                WHERE id NOT IN (SELECT id FROM experience WHERE deleted_at IS NULL)
                """);
            return Sql.Exec(conn, null, $"""
                INSERT INTO {Table} (id, text)
                SELECT e.id, e.text FROM experience e
                WHERE e.deleted_at IS NULL AND e.id NOT IN (SELECT id FROM {Table})
                """);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    /// <summary>Лексическое попадание: идентификатор записи и балл BM25 (у FTS5 он
    /// ОТРИЦАТЕЛЬНЫЙ, и чем меньше, тем лучше — порядок «ORDER BY rank» уже верный).</summary>
    public sealed record Hit(string Id, double Bm25);

    /// <summary>
    /// Поиск по индексу: кандидаты в порядке убывания качества совпадения. Пустой список —
    /// и попаданий нет (порог отсечения: кандидат без лексического попадания дальше не идёт).
    /// </summary>
    public static List<Hit> Match(SqliteConnection conn, string query, int take)
    {
        var expression = MatchExpression(query);
        if (expression.Length == 0 || !Ready(conn))
        {
            return [];
        }
        try
        {
            return Sql.Query(conn, null, $"""
                SELECT id, bm25({Table}) AS rank FROM {Table}
                WHERE {Table} MATCH @q ORDER BY rank LIMIT @take
                """,
                r => new Hit(r.S("id"), r.D("rank")), ("@q", expression), ("@take", take));
        }
        catch (SqliteException)
        {
            return [];   // запрос, который FTS5 не разобрал, — это «не нашлось», а не отказ
        }
    }

    /// <summary>
    /// Запрос человека → выражение MATCH: слова через OR, каждое ПРЕФИКСОМ. Слова берутся
    /// в кавычки (внутренняя кавычка удваивается) — иначе «AND», «NEAR», «*» и скобки из
    /// живого текста разбирались бы как операторы FTS5 и запрос падал бы.
    /// </summary>
    public static string MatchExpression(string query)
    {
        var parts = Stems(query).Select(t => "\"" + t.Replace("\"", "\"\"") + "\"*");
        return string.Join(" OR ", parts);
    }

    /// <summary>
    /// Слова запроса: только буквы и цифры, нижний регистр, слова короче двух знаков
    /// отброшены.
    /// </summary>
    public static List<string> Tokens(string query)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in query ?? "")
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
                continue;
            }
            Flush();
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length >= 2)
            {
                words.Add(current.ToString());
            }
            current.Clear();
        }
    }

    /// <summary>
    /// Окончания, которые срезаются с длинного слова перед поиском по префиксу. Настоящей
    /// морфологии у стандартного токенизатора нет: «репликация» и «репликации» для него
    /// разные слова, и поиск по слову из задания не нашёл бы половину записей. Срезаются
    /// ТОЛЬКО гласные, «й» и мягкий/твёрдый знак и только пока слово длиннее четырёх знаков —
    /// это не стеммер, а грубая защита от падежа.
    /// </summary>
    private const string Endings = "аеёиоуыэюяйьъ";

    /// <summary>Слова запроса, огрублённые до основы (см. <see cref="Endings"/>).</summary>
    public static List<string> Stems(string query) => Tokens(query).Select(Stem).ToList();

    /// <summary>Огрубить слово до основы: срезать до трёх «окончаний» с конца.</summary>
    public static string Stem(string token)
    {
        var stem = token;
        for (var cut = 0; cut < 3 && stem.Length > 4 && Endings.Contains(stem[^1]); cut++)
        {
            stem = stem[..^1];
        }
        return stem;
    }

    /// <summary>
    /// ЗАПАСНОЙ ПОИСК, когда FTS5 недоступен: подстрочное сравнение с основой слова. Балл —
    /// минус число найденных слов запроса, чтобы порядок сортировки совпадал с BM25 (меньше
    /// значит лучше). Хуже индекса ничем, кроме скорости: записей опыта в организации сотни.
    /// </summary>
    public static List<Hit> MatchByScan(IEnumerable<(string Id, string Text)> rows, string query, int take)
    {
        var stems = Stems(query);
        if (stems.Count == 0)
        {
            return [];
        }
        return rows
            .Select(row => new Hit(row.Id, -stems.Count(stem =>
                row.Text.Contains(stem, StringComparison.CurrentCultureIgnoreCase))))
            .Where(hit => hit.Bm25 < 0)
            .OrderBy(hit => hit.Bm25)
            .Take(take)
            .ToList();
    }
}
