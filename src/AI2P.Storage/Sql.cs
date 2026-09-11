using Microsoft.Data.Sqlite;

namespace AI2P.Storage;

/// <summary>Короткие помощники для SQL без ORM (Microsoft.Data.Sqlite, ТЗ п. 6.1).</summary>
public static class Sql
{
    public static SqliteCommand Cmd(SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string Name, object? Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd;
    }

    public static int Exec(SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string, object?)[] args)
    {
        using var cmd = Cmd(conn, tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string, object?)[] args)
    {
        using var cmd = Cmd(conn, tx, sql, args);
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull)
        {
            return default;
        }
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(result, target);
    }

    /// <summary>Прочитать все строки, преобразуя каждую через map.</summary>
    public static List<T> Query<T>(SqliteConnection conn, SqliteTransaction? tx, string sql,
        Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = Cmd(conn, tx, sql, args);
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            list.Add(map(reader));
        }
        return list;
    }

    // --- чтение колонок по имени с учётом NULL ---

    public static string S(this SqliteDataReader r, string col) => r.GetString(r.GetOrdinal(col));

    /// <summary>Есть ли колонка в выборке — для необязательных вычисляемых колонок (SELECT t.*, … AS x).</summary>
    public static bool Has(this SqliteDataReader r, string col)
    {
        for (var i = 0; i < r.FieldCount; i++)
        {
            if (string.Equals(r.GetName(i), col, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string? SN(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long L(this SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col));

    public static long? LN(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }

    public static double D(this SqliteDataReader r, string col) => r.GetDouble(r.GetOrdinal(col));

    public static double? DN(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetDouble(i);
    }

    public static bool B(this SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col)) != 0;

    public static DateTime Dt(this SqliteDataReader r, string col) => ParseDate(r.S(col));

    public static DateTime? DtN(this SqliteDataReader r, string col)
    {
        var s = r.SN(col);
        return s is null ? null : ParseDate(s);
    }

    /// <summary>Дата в БД — TEXT, UTC, ISO 8601 (ТЗ п. 6.4.2).</summary>
    public static string ToDb(DateTime dt) =>
        dt.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

    public static object ToDbN(DateTime? dt) => dt is null ? DBNull.Value : ToDb(dt.Value);

    private static DateTime ParseDate(string s) =>
        DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
