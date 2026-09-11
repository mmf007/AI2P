namespace AI2P.Storage.Services;

/// <summary>
/// Состояние приложения (key-value в БД): текущий проект и команда по умолчанию (ТЗ гл. 5) —
/// «если он выбрал проект, его выбор запоминается» (гл. 11).
///
/// Состояние ПЕР-ПОЛЬЗОВАТЕЛЬСКОЕ (ТЗ v1.46, todo39): ключ — (аккаунт, имя ключа), иначе
/// несколько пользователей на одном сервере затирали бы лейауты друг друга.
/// </summary>
public sealed class AppStateService
{
    public const string CurrentProjectKey = "ui.currentProjectId";
    public const string CurrentTeamKey = "ui.currentTeamId";
    /// <summary>Лейаут фреймов и закладок пользователя (JSON, todo22).</summary>
    public const string UiLayoutKey = "ui.layout";
    /// <summary>Состояния представлений — вид/сортировка/фильтр списков (JSON, гл. 11).</summary>
    public const string UiComponentStatesKey = "ui.componentStates";

    private readonly Database _db;

    public AppStateService(Database db) => _db = db;

    public string? Get(string accountId, string key)
    {
        using var conn = _db.Open();
        return Sql.Scalar<string>(conn, null,
            "SELECT value FROM app_state WHERE account_id=@a AND key=@k",
            ("@a", accountId), ("@k", key));
    }

    public void Set(string accountId, string key, string? value)
    {
        using var conn = _db.Open();
        if (value is null)
        {
            Sql.Exec(conn, null, "DELETE FROM app_state WHERE account_id=@a AND key=@k",
                ("@a", accountId), ("@k", key));
        }
        else
        {
            Sql.Exec(conn, null, """
                INSERT INTO app_state(account_id, key, value) VALUES (@a, @k, @v)
                ON CONFLICT(account_id, key) DO UPDATE SET value=@v
                """, ("@a", accountId), ("@k", key), ("@v", value));
        }
    }
}
