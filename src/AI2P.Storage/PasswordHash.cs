using System.Security.Cryptography;
using System.Text;

namespace AI2P.Storage;

/// <summary>
/// Хэш пароля аккаунта (ТЗ гл. 12, этап 39): PBKDF2-SHA256 — есть в .NET из коробки,
/// внешних пакетов не требует (Argon2id пришлось бы тянуть зависимостью).
/// Формат строки хранения: <c>pbkdf2$sha256$&lt;итераций&gt;$&lt;соль b64&gt;$&lt;хэш b64&gt;</c> —
/// параметры лежат рядом со значением, поэтому усиление стойкости в будущем не ломает
/// уже сохранённые пароли (старые проверяются своими параметрами).
///
/// Пустой пароль допустим (ТЗ гл. 11, первый старт): он хранится ПУСТОЙ СТРОКОЙ, а не хэшем —
/// так «пароль не задан» отличимо от «пароль задан», и правило «без пароля только локальный
/// вход» проверяется явно.
/// </summary>
public static class PasswordHash
{
    private const int Iterations = 210_000;   // рекомендация OWASP 2023 для PBKDF2-SHA256
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>Пароль не задан (пустая строка в БД) — вход разрешён только локально.</summary>
    public static bool IsEmpty(string? stored) => string.IsNullOrEmpty(stored);

    /// <summary>Хэш для нового пароля; пустой пароль хранится пустой строкой.</summary>
    public static string Hash(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "";
        }
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2$sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Проверка пароля. Пустой сохранённый пароль совпадает только с пустым введённым.
    /// Сравнение хэшей — в постоянном времени (FixedTimeEquals).
    /// </summary>
    public static bool Verify(string? stored, string? password)
    {
        if (IsEmpty(stored))
        {
            return string.IsNullOrEmpty(password);
        }
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }
        var parts = stored!.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256"
            || !int.TryParse(parts[2], out var iterations) || iterations <= 0)
        {
            return false; // повреждённая или чужая запись — вход не пускаем
        }
        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
