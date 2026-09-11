using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AI2P.Core;

namespace AI2P.Server;

/// <summary>
/// СЕРТИФИКАТ СЕРВЕРА ДЛЯ HTTPS (T-206): один вход, из которого Kestrel получает готовый
/// <see cref="X509Certificate2"/>, а форма сервера — понятную человеку причину отказа.
///
/// Почему это отдельный класс, а не пара строк в <c>Program.cs</c>: тот же разбор нужен
/// ТРЁМ местам, и расхождение между ними ровно то, из-за чего задача и появилась.
/// <list type="number">
/// <item>Старт сервера — без сертификата запускаться нельзя, но и падать невнятной ошибкой
/// Kestrel («no server certificate») тоже: причина печатается словами.</item>
/// <item>Форма локального сервера — «https» без годного сертификата не сохраняется вовсе:
/// иначе человек своими руками делает установку, в которую больше не войти.</item>
/// <item>Визард первого старта — то же самое на самом первом экране.</item>
/// </list>
///
/// Ошибки здесь — <see cref="InvalidOperationException"/> с текстом из словаря: он уходит
/// и в консоль, и в ответ API, и в подсказку формы.
/// </summary>
public static class HttpsCertificate
{
    /// <summary>
    /// Прочитать сертификат по настройкам. Бросает <see cref="InvalidOperationException"/>
    /// с внятным текстом, если сертификата нет, он не читается или у него нет закрытого
    /// ключа (таким сертификатом сервер отвечать не может — им только проверяют чужой).
    /// </summary>
    /// <param name="https">Раздел <c>ui.https</c> настроек.</param>
    /// <param name="hostname">Имя сервера: по нему ищется сертификат в хранилище, когда
    /// ни отпечаток, ни имя в настройках не заданы.</param>
    /// <param name="configPath">Путь к <c>config.json</c>: от его каталога считаются
    /// относительные пути к файлам сертификата и ключа.</param>
    /// <param name="password">Пароль файла <c>.pfx</c>; пусто — без пароля.</param>
    public static X509Certificate2 Load(Ai2pConfig.HttpsSettings https, string hostname,
        string configPath, string password)
    {
        var certificate = https.FromStore
            ? FromStore(https, hostname)
            : FromFile(https, configPath, password);
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(Loc.T("msg.https.6", Describe(certificate)));
        }
        return certificate;
    }

    /// <summary>
    /// ПРИЧИНА, ПО КОТОРОЙ HTTPS НЕ ЗАРАБОТАЕТ, либо <c>null</c>, если всё в порядке.
    /// Ею проверяют настройки ДО того, как их сохранить: перезапуск с негодным сертификатом
    /// оставил бы человека без работающей установки, а починить её можно было бы только
    /// правкой config.json руками.
    /// </summary>
    public static string? Problem(Ai2pConfig.HttpsSettings https, string hostname,
        string configPath, string password)
    {
        try
        {
            Load(https, hostname, configPath, password).Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Короткое описание сертификата для формы и лога: имя и срок годности.
    /// Пустая строка — сертификата нет (и это не ошибка: протокол может быть http).</summary>
    public static string Describe(X509Certificate2 certificate) =>
        Loc.T("msg.https.9", certificate.Subject, certificate.NotAfter.ToString("yyyy-MM-dd"),
            certificate.Thumbprint);

    /// <summary>
    /// ЧЕМ ЭТОТ СЕРТИФИКАТ НЕ УСТРОИТ БРАУЗЕР (T-315), либо <c>null</c>, если придраться
    /// не к чему. Сертификат может ЧИТАТЬСЯ (и форма честно писала «сертификат читается»),
    /// а браузер при этом отказывался бы открывать страницу — и человеку неоткуда узнать,
    /// почему: у Firefox это «вы соединены с сайтом небезопасно» без единой подробности.
    ///
    /// Две беды закрывают почти все случаи самодельного сертификата:
    /// <list type="number">
    /// <item><b>сертификат УЦ вместо серверного</b>: <c>openssl req -x509</c> по умолчанию
    /// ставит <c>basicConstraints=CA:TRUE</c>, и такой сертификат браузеры отвергают как
    /// сертификат САЙТА, даже когда он же добавлен в доверенные корневые
    /// (Firefox: <c>MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY</c>);</item>
    /// <item><b>имя сервера не названо в сертификате</b>: браузер сверяет адрес строки
    /// адреса со списком SAN, и обращение по имени, которого там нет (обычно это как раз
    /// <c>localhost</c>), отвергается по несовпадению имени.</item>
    /// </list>
    /// </summary>
    /// <param name="certificate">Прочитанный сертификат.</param>
    /// <param name="hostname">Имя сервера из настроек — то, по которому к нему заходят.</param>
    /// <param name="hostname2">Второе (внешнее) имя сервера: по нему в браузер заходят
    /// снаружи, и сертификат, не назвавший его, откажет ровно так же (T-315).</param>
    public static string? BrowserWarning(X509Certificate2 certificate, string hostname,
        string hostname2 = "")
    {
        var problems = new List<string>();
        if (IsCaCertificate(certificate))
        {
            problems.Add(Loc.T("msg.https.12"));
        }
        var names = SubjectAltNames(certificate);
        if (names.Count == 0)
        {
            problems.Add(Loc.T("msg.https.13"));
        }
        else
        {
            var missing = new[] { hostname.Trim(), hostname2.Trim() }
                .Where(h => h.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(h => !names.Any(n => MatchesHost(n, h)))
                .ToList();
            if (missing.Count > 0)
            {
                // имена перечисляются ВНУТРИ кавычек шаблона: у разных языков они свои
                problems.Add(Loc.T("msg.https.14", string.Join(", ", missing),
                    string.Join(", ", names)));
            }
        }
        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>Сертификат помечен как сертификат УЦ (<c>basicConstraints CA:TRUE</c>).</summary>
    private static bool IsCaCertificate(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509Extension>()
            .Where(e => e.Oid?.Value == "2.5.29.19")
            .Select(e => new X509BasicConstraintsExtension(new AsnEncodedData(e.RawData), e.Critical))
            .Any(e => e.CertificateAuthority);

    /// <summary>Имена и адреса из расширения SAN: именно их сверяет браузер.
    /// Пустой список — расширения нет вовсе (старый сертификат «только CN»).</summary>
    private static List<string> SubjectAltNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions.OfType<X509Extension>()
                     .Where(e => e.Oid?.Value == "2.5.29.17"))
        {
            var san = new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
            names.AddRange(san.EnumerateDnsNames());
            names.AddRange(san.EnumerateIPAddresses().Select(ip => ip.ToString()));
        }
        return names;
    }

    /// <summary>Совпадение имени с записью SAN, включая шаблон <c>*.example.org</c>.</summary>
    private static bool MatchesHost(string name, string host)
    {
        if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!name.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }
        var dot = host.IndexOf('.');
        return dot > 0 && string.Equals(name[2..], host[(dot + 1)..],
            StringComparison.OrdinalIgnoreCase);
    }

    // --- файл: .pfx (ключ внутри) либо PEM «сертификат + ключ» ---

    private static X509Certificate2 FromFile(Ai2pConfig.HttpsSettings https, string configPath,
        string password)
    {
        var certFile = https.CertFile.Trim();
        if (certFile.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.https.1", configPath));
        }
        var certPath = ResolveFile(configPath, certFile);
        if (!File.Exists(certPath))
        {
            throw new InvalidOperationException(Loc.T("msg.https.2", certPath));
        }
        var keyFile = https.KeyFile.Trim();
        var keyPath = keyFile.Length > 0 ? ResolveFile(configPath, keyFile) : "";
        if (keyPath.Length > 0 && !File.Exists(keyPath))
        {
            throw new InvalidOperationException(Loc.T("msg.https.3", keyPath));
        }
        try
        {
            var extension = Path.GetExtension(certPath).ToLowerInvariant();
            if (extension is ".pfx" or ".p12")
            {
                // Exportable нужен PEM-ветке ниже, а здесь он безвреден и делает поведение
                // обеих веток одинаковым: сертификат можно переложить, не читая файл снова
                return new X509Certificate2(certPath, password,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            }
            // PEM: ключ либо отдельным файлом, либо в том же файле (так его отдают
            // некоторые УЦ). ПЕРЕСБОРКА ЧЕРЕЗ PKCS#12 ОБЯЗАТЕЛЬНА: сертификат, собранный
            // из PEM, на Windows держит ключ отдельным объектом, и SslStream отвечает им
            // «no credentials» — работающего HTTPS из такого сертификата не выходит
            using var fromPem = keyPath.Length > 0
                ? X509Certificate2.CreateFromPemFile(certPath, keyPath)
                : X509Certificate2.CreateFromPemFile(certPath);
            return new X509Certificate2(fromPem.Export(X509ContentType.Pkcs12), (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(Loc.T("msg.https.4", certPath, ex.Message), ex);
        }
    }

    /// <summary>Путь к файлу настроек по тем же правилам, что у каталогов (T-135):
    /// абсолютный и «~/…» берутся как есть, относительный считается от <c>config.json</c>.</summary>
    private static string ResolveFile(string configPath, string file)
    {
        var path = PathHome.Expand(file);
        return PathHome.IsRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, path));
    }

    // --- хранилище сертификатов этого компьютера ---

    private static X509Certificate2 FromStore(Ai2pConfig.HttpsSettings https, string hostname)
    {
        var locationText = https.StoreLocation.Trim();
        var nameText = https.StoreName.Trim();
        if (!Enum.TryParse<StoreLocation>(locationText, ignoreCase: true, out var location))
        {
            throw new InvalidOperationException(Loc.T("msg.https.7", locationText, nameText,
                string.Join(", ", Enum.GetNames<StoreLocation>())));
        }
        var query = https.StoreQuery(hostname);
        if (query.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.https.8"));
        }
        using var store = nameText.Length > 0
                          && Enum.TryParse<StoreName>(nameText, ignoreCase: true, out var known)
            ? new X509Store(known, location)
            : new X509Store(nameText.Length > 0 ? nameText : "My", location);
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                Loc.T("msg.https.10", locationText, nameText, ex.Message), ex);
        }
        // ОТБОР: отпечаток точнее имени; по имени берём самый ПОЗДНИЙ годный — на одно имя
        // сертификатов обычно несколько (старый и продлённый), и брать первый попавшийся
        // значит однажды поднять сервер с просроченным
        var found = https.Thumbprint.Trim().Length > 0
            ? store.Certificates.Find(X509FindType.FindByThumbprint, query, validOnly: false)
            : store.Certificates.Find(X509FindType.FindBySubjectName, query, validOnly: false);
        var best = found.OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();
        if (best is null)
        {
            throw new InvalidOperationException(Loc.T("msg.https.5", locationText, nameText, query));
        }
        return best;
    }
}
