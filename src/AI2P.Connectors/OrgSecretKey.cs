using AI2P.Core;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AI2P.Connectors;

/// <summary>
/// КЛЮЧ ОРГАНИЗАЦИИ (ТЗ гл. 10, разд. 8 плана; этап 45) — 32 случайных байта, которыми
/// шифруются ключи API моделей этой организации.
///
/// Живёт ТОЛЬКО локально, в <c>secrets.json</c> рядом с config.json, и не реплицируется
/// никогда: реплицируется зашифрованный результат, а не то, чем он расшифровывается.
/// Создаётся вместе с организацией; подключающийся к чужой организации сервер получает его
/// один раз — вместе с подтверждением заявки, то есть после решения человека на дирижёре.
///
/// На Windows значение дополнительно защищено DPAPI (областью текущего пользователя):
/// файл секретов лежит в профиле пользователя, и другой пользователь того же компьютера
/// не должен читать чужие ключи. На остальных системах защита — права на файл.
///
/// Компрометация сервера = ротация ключа организации и перешифровка ключей моделей;
/// сама ротация — отдельная будущая работа.
/// </summary>
public sealed class OrgSecretKey
{
    /// <summary>Раздел secrets.json, где лежат ключи организаций: <c>orgKeys.&lt;orgId&gt;</c>.</summary>
    public const string Section = "orgKeys";

    /// <summary>Префикс значения, защищённого DPAPI, — по нему отличают его от обычного base64.</summary>
    private const string DpapiPrefix = "dpapi:";

    private readonly SecretStore _secrets;

    public OrgSecretKey(SecretStore secrets) => _secrets = secrets;

    /// <summary>Ссылка на ключ организации в secrets.json.</summary>
    public static string RefOf(string orgId) => $"{Section}.{orgId}";

    /// <summary>Ключ организации известен этому серверу.</summary>
    public bool Has(string orgId) => Read(orgId) is not null;

    /// <summary>Забыть ключ организации (T-148-S0): её удалили с этого сервера, и держать
    /// ключ к данным, которых больше нет, незачем. Значение затирается пустым — <see cref="Read"/>
    /// после этого отвечает «ключа нет», как до подключения.</summary>
    public void Forget(string orgId) => _secrets.Write(RefOf(orgId), "");

    /// <summary>
    /// Ключ организации; null — этот сервер его ещё не получил. Тогда ключи API моделей
    /// нечем расшифровать: их значения приехали репликацией зашифрованными.
    /// </summary>
    public byte[]? Read(string orgId)
    {
        var stored = _secrets.Read(RefOf(orgId));
        if (stored is not { Length: > 0 })
        {
            return null;
        }
        try
        {
            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                var blob = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
                return Unprotect(blob);
            }
            return Convert.FromBase64String(stored);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                Loc.T("msg.orgSecretKey.1", RefOf(orgId)));
        }
    }

    /// <summary>Записать ключ организации (создание организации либо подключение к чужой).</summary>
    public void Write(string orgId, byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException(Loc.T("msg.orgSecretKey.2"), nameof(key));
        }
        var value = Protect(key) is { } protectedBlob
            ? DpapiPrefix + Convert.ToBase64String(protectedBlob)
            : Convert.ToBase64String(key);
        _secrets.Write(RefOf(orgId), value);
    }

    /// <summary>Ключ организации, создавая его при первом обращении (новая организация).</summary>
    public byte[] Ensure(string orgId)
    {
        if (Read(orgId) is { } existing)
        {
            return existing;
        }
        var key = RandomNumberGenerator.GetBytes(32);
        Write(orgId, key);
        return key;
    }

    /// <summary>Ключ организации в base64 — в таком виде он один раз едет подключающемуся
    /// серверу (по подтверждённой человеком заявке, ТЗ гл. 12).</summary>
    public string Export(string orgId) => Convert.ToBase64String(Ensure(orgId));

    /// <summary>Принять ключ организации от дирижёра (подключение к чужой организации).</summary>
    public void Import(string orgId, string base64)
    {
        if (base64.Trim().Length == 0)
        {
            return;
        }
        Write(orgId, Convert.FromBase64String(base64.Trim()));
    }

    // --- шифрование значений (AES-GCM) ---

    /// <summary>
    /// Зашифровать значение ключа API: <c>base64(nonce | tag | шифртекст)</c>. Именно этот
    /// blob лежит в БД и свободно реплицируется — в открытом виде ключ в базу не попадает.
    /// </summary>
    public static string Encrypt(byte[] orgKey, string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(orgKey, tag.Length))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        var blob = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, nonce.Length);
        cipher.CopyTo(blob, nonce.Length + tag.Length);
        return Convert.ToBase64String(blob);
    }

    /// <summary>Расшифровать значение; null — blob повреждён либо зашифрован другим ключом
    /// организации (сервер получил не тот ключ).</summary>
    public static string? Decrypt(byte[] orgKey, string encrypted)
    {
        try
        {
            var blob = Convert.FromBase64String(encrypted);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (blob.Length < nonceSize + tagSize)
            {
                return null;
            }
            var nonce = blob.AsSpan(0, nonceSize);
            var tag = blob.AsSpan(nonceSize, tagSize);
            var cipher = blob.AsSpan(nonceSize + tagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(orgKey, tagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException
                                       or ArgumentException)
        {
            return null;
        }
    }

    // --- DPAPI: только Windows, только текущий пользователь ---

    private static byte[]? Protect(byte[] key)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }
        try
        {
            return ProtectedData.Protect(key, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            return null;   // DPAPI недоступен — храним обычным base64, права на файл остаются
        }
    }

    private static byte[] Unprotect(byte[] blob)
    {
        // префикс DPAPI ставит только Windows-ветка Protect; встретить его на другой ОС
        // можно лишь у secrets.json, принесённого с Windows-машины, — расшифровать его
        // здесь нечем, и молчать об этом нельзя
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new CryptographicException(
                Loc.T("msg.orgSecretKey.3"));
        }
        return ProtectedData.Unprotect(blob, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}
