using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-206 (выпуск 1.110): HTTPS — СЕРТИФИКАТ СЕРВЕРА.
///
/// До этой задачи раздел <c>ui.https</c> в config.json был, а читать его было некому:
/// выбрав в форме сервера «https», человек получал установку, которая после перезапуска
/// не поднималась вовсе — Kestrel не знал, каким сертификатом отвечать.
///
/// Теперь сертификат берётся ДВУМЯ способами — из файла (<c>.pfx</c> либо пара PEM)
/// и из хранилища сертификатов компьютера, — а любой отказ объясняется словами:
/// «сертификат не задан», «файла нет», «нет закрытого ключа». Тем же разбором форма
/// локального сервера проверяет настройки ДО сохранения (<see cref="HttpsCertificate"/>).
/// </summary>
public sealed class T206Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t206-" + Guid.NewGuid().ToString("N"));

    public T206Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог мог остаться занятым — на проверку это не влияет
        }
    }

    /// <summary>Путь к «config.json» стенда: от его каталога считаются относительные пути
    /// к сертификату и ключу, как в настоящей установке.</summary>
    private string ConfigPath => Path.Combine(_dir, "config.json");

    /// <summary>Самоподписанный сертификат на имя <paramref name="cn"/> — ровно такой,
    /// какой человек делает себе openssl'ом или New-SelfSignedCertificate.</summary>
    private static X509Certificate2 SelfSigned(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(cn);
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>Положить .pfx в каталог стенда и вернуть его ИМЯ (путь относительный —
    /// он и должен считаться от config.json).</summary>
    private string WritePfx(string cn, string password)
    {
        using var certificate = SelfSigned(cn);
        var name = cn + ".pfx";
        File.WriteAllBytes(Path.Combine(_dir, name), certificate.Export(X509ContentType.Pkcs12, password));
        return name;
    }

    /// <summary>Положить пару PEM «сертификат + ключ» и вернуть их имена.</summary>
    private (string Cert, string Key) WritePem(string cn)
    {
        using var certificate = SelfSigned(cn);
        var certName = cn + ".crt";
        var keyName = cn + ".key";
        File.WriteAllText(Path.Combine(_dir, certName), certificate.ExportCertificatePem());
        File.WriteAllText(Path.Combine(_dir, keyName),
            certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());
        return (certName, keyName);
    }

    // --- 1. файл .pfx ---

    /// <summary>Сертификат из .pfx с паролем читается, у него есть закрытый ключ, и путь
    /// к файлу считается ОТ config.json (в настройке он относительный).</summary>
    [Fact]
    public void Pfx_With_Password_Is_Loaded_By_A_Path_Relative_To_Config()
    {
        var file = WritePfx("ai2p.local", "секрет");
        var https = new Ai2pConfig.HttpsSettings { CertFile = file };

        using var loaded = HttpsCertificate.Load(https, "ai2p.local", ConfigPath, "секрет");

        Assert.True(loaded.HasPrivateKey);
        Assert.Contains("ai2p.local", loaded.Subject);
        Assert.Null(HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "секрет"));
    }

    /// <summary>Неверный пароль .pfx — отказ с внятной причиной, а не пустая ссылка.</summary>
    [Fact]
    public void Pfx_With_A_Wrong_Password_Is_Refused_With_A_Reason()
    {
        var https = new Ai2pConfig.HttpsSettings { CertFile = WritePfx("ai2p.local", "секрет") };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "не тот");

        Assert.NotNull(problem);
        Assert.Contains("HTTPS", problem);
    }

    // --- 2. пара PEM ---

    /// <summary>Пара PEM «сертификат + ключ» читается так же, как .pfx: именно в таком виде
    /// сертификат отдаёт Let's Encrypt и выписывает openssl.</summary>
    [Fact]
    public void Pem_Pair_Is_Loaded_And_Keeps_The_Private_Key()
    {
        var (cert, key) = WritePem("ai2p.local");
        var https = new Ai2pConfig.HttpsSettings { CertFile = cert, KeyFile = key };

        using var loaded = HttpsCertificate.Load(https, "ai2p.local", ConfigPath, "");

        // ключ обязан пережить пересборку через PKCS#12 — без неё на Windows SslStream
        // отвечает «no credentials», и HTTPS из PEM не работает вовсе
        Assert.True(loaded.HasPrivateKey);
        Assert.Contains("ai2p.local", loaded.Subject);
    }

    /// <summary>Файла ключа нет — причина называет ИМЕННО ключ, а не сертификат.</summary>
    [Fact]
    public void A_Missing_Key_File_Is_Named_In_The_Reason()
    {
        var (cert, _) = WritePem("ai2p.local");
        var https = new Ai2pConfig.HttpsSettings { CertFile = cert, KeyFile = "нет-такого.key" };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "");

        Assert.NotNull(problem);
        Assert.Contains("нет-такого.key", problem);
    }

    /// <summary>Файла сертификата нет — в причине полный путь, по которому его искали.</summary>
    [Fact]
    public void A_Missing_Certificate_File_Is_Named_With_Its_Full_Path()
    {
        var https = new Ai2pConfig.HttpsSettings { CertFile = "нет-такого.pfx" };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "");

        Assert.NotNull(problem);
        Assert.Contains(Path.Combine(_dir, "нет-такого.pfx"), problem);
    }

    /// <summary>ПУСТАЯ настройка — самый частый случай и самый непонятный до T-206:
    /// протокол https, а сертификата нет. Причина называет config.json, где это правится.</summary>
    [Fact]
    public void An_Empty_Certificate_Setting_Points_At_The_Config_File()
    {
        var problem = HttpsCertificate.Problem(new Ai2pConfig.HttpsSettings(), "ai2p.local",
            ConfigPath, "");

        Assert.NotNull(problem);
        Assert.Contains(ConfigPath, problem);
    }

    /// <summary>Сертификат БЕЗ закрытого ключа (один .crt, ключа рядом нет) отвергается:
    /// им проверяют чужой сертификат, а отвечать сервер таким не может.</summary>
    [Fact]
    public void A_Certificate_Without_A_Private_Key_Is_Refused()
    {
        using var certificate = SelfSigned("ai2p.local");
        var name = "public-only.crt";
        File.WriteAllText(Path.Combine(_dir, name), certificate.ExportCertificatePem());
        var https = new Ai2pConfig.HttpsSettings { CertFile = name };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "");

        Assert.NotNull(problem);
    }

    // --- 3. хранилище сертификатов ---

    /// <summary>Незнакомое хранилище — отказ с перечнем допустимых значений, а не
    /// исключение разбора где-то в глубине.</summary>
    [Fact]
    public void An_Unknown_Store_Location_Lists_The_Allowed_Values()
    {
        var https = new Ai2pConfig.HttpsSettings
        {
            Source = Ai2pConfig.HttpsSettings.SourceStore,
            StoreLocation = "МоёХранилище",
        };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "");

        Assert.NotNull(problem);
        Assert.Contains("LocalMachine", problem);
        Assert.Contains("CurrentUser", problem);
    }

    /// <summary>Признак отбора в хранилище: отпечаток главнее имени, а нет ни того ни
    /// другого — ищем по имени самого сервера (сертификат выписывают именно на него).</summary>
    [Fact]
    public void The_Store_Is_Searched_By_Thumbprint_Then_Subject_Then_Hostname()
    {
        var https = new Ai2pConfig.HttpsSettings();
        Assert.Equal("ai2p.local", https.StoreQuery("ai2p.local"));

        https.Subject = "CN=свой";
        Assert.Equal("CN=свой", https.StoreQuery("ai2p.local"));

        https.Thumbprint = "AABBCC";
        Assert.Equal("AABBCC", https.StoreQuery("ai2p.local"));
    }

    /// <summary>В хранилище такого сертификата нет — причина называет и хранилище,
    /// и признак, по которому искали.</summary>
    [Fact]
    public void A_Certificate_Missing_From_The_Store_Is_Explained()
    {
        var https = new Ai2pConfig.HttpsSettings
        {
            Source = Ai2pConfig.HttpsSettings.SourceStore,
            Thumbprint = "00112233445566778899AABBCCDDEEFF00112233",
        };

        var problem = HttpsCertificate.Problem(https, "ai2p.local", ConfigPath, "");

        Assert.NotNull(problem);
        Assert.Contains("00112233445566778899AABBCCDDEEFF00112233", problem);
    }

    // --- 4. настройки: умолчания, совместимость, ссылка на пароль ---

    /// <summary>Умолчания раздела: источник — файл, сертификату соседей по кластеру
    /// доверяем (умолчание сменилось в T-215-S0, см. <see cref="T215S0Tests"/>).</summary>
    [Fact]
    public void Https_Settings_Default_To_A_File_Source_And_Trusted_Peers()
    {
        var https = new Ai2pConfig.HttpsSettings();

        Assert.False(https.FromStore);
        Assert.True(https.TrustAnyPeer);
        Assert.Equal("https.certPassword", https.PasswordRefOrDefault());
    }

    /// <summary>Конфигурация ПРЕЖНИХ версий (в разделе только два пути) читается как
    /// «источник — файл»: у переживших обновление установок поля <c>source</c> нет вовсе.</summary>
    [Fact]
    public void A_Config_From_Older_Versions_Reads_As_A_File_Source()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path,
            """{"ui":{"protocol":"https","https":{"certFile":"cert.pfx","keyFile":"cert.key"}}}""");

        var config = Ai2pConfig.Load(path);

        Assert.True(config.Ui.IsHttps);
        Assert.False(config.Ui.Https.FromStore);
        Assert.Equal("cert.pfx", config.Ui.Https.CertFile);
        Assert.Equal("https.certPassword", config.Ui.Https.PasswordRefOrDefault());
    }

    /// <summary>Протокол http — сертификат не нужен и не спрашивается.</summary>
    [Fact]
    public void The_Http_Protocol_Needs_No_Certificate()
    {
        var config = new Ai2pConfig();

        Assert.False(config.Ui.IsHttps);

        config.Ui.Protocol = "HTTPS";
        Assert.True(config.Ui.IsHttps);
    }

    /// <summary>В config.json дистрибутива раздел HTTPS есть целиком и БЕЗ пароля:
    /// секретов в конфигурации не бывает (ТЗ гл. 10), там только ссылка на секрет.</summary>
    [Fact]
    public void The_Shipped_Config_Holds_The_Whole_Https_Section_And_No_Password()
    {
        var path = Path.Combine(RepoRoot(), "src", "AI2P.Server", "config.json");
        var https = JsonNode.Parse(File.ReadAllText(path))!["ui"]!["https"]!.AsObject();

        foreach (var key in new[]
                 {
                     "source", "certFile", "keyFile", "passwordRef",
                     "storeLocation", "storeName", "subject", "thumbprint", "trustAnyPeer",
                 })
        {
            Assert.True(https.ContainsKey(key), $"в config.json дистрибутива нет ui.https.{key}");
        }
        Assert.Equal("file", (string)https["source"]!);
        Assert.Equal("https.certPassword", (string)https["passwordRef"]!);
        Assert.False(https.ContainsKey("password"));
    }

    /// <summary>Раздел читается ЦЕЛИКОМ и переживает запись файла: настройки правятся
    /// формой сервера, и потерять при сохранении хотя бы одно поле нельзя.</summary>
    [Fact]
    public void The_Whole_Https_Section_Survives_A_Save_And_Load()
    {
        var path = Path.Combine(_dir, "roundtrip.json");
        var config = new Ai2pConfig();
        config.Ui.Protocol = "https";
        config.Ui.Https = new Ai2pConfig.HttpsSettings
        {
            Source = Ai2pConfig.HttpsSettings.SourceStore,
            CertFile = "cert.pfx",
            KeyFile = "cert.key",
            StoreLocation = "LocalMachine",
            StoreName = "My",
            Subject = "CN=ai2p.local",
            Thumbprint = "AABB",
            TrustAnyPeer = true,
        };
        config.Save(path);

        var again = Ai2pConfig.Load(path);

        Assert.True(again.Ui.Https.FromStore);
        Assert.Equal("cert.pfx", again.Ui.Https.CertFile);
        Assert.Equal("cert.key", again.Ui.Https.KeyFile);
        Assert.Equal("LocalMachine", again.Ui.Https.StoreLocation);
        Assert.Equal("CN=ai2p.local", again.Ui.Https.Subject);
        Assert.Equal("AABB", again.Ui.Https.Thumbprint);
        Assert.True(again.Ui.Https.TrustAnyPeer);
        var text = File.ReadAllText(path);
        // пароля в файле нет никогда — только ссылка на него
        Assert.DoesNotContain("\"password\"", text);
        // производные признаки в файл не пишутся: человек принял бы их за настройку
        Assert.DoesNotContain("fromStore", text);
        Assert.DoesNotContain("isHttps", text);
    }

    // --- 5. клиент к соседям и к самому себе ---

    /// <summary>Проверка сертификата соседей снимается ТОЛЬКО настройкой; по умолчанию
    /// обращения сервер-сервер идут с обычной проверкой. С T-315 настройка спрашивается
    /// В МОМЕНТ рукопожатия, а не при создании обработчика: обработчик заведён один раз
    /// на весь процесс, и снимок настройки сделал бы флажок формы неработающим до
    /// перезапуска. Поэтому проверяется не наличие обработчика, а его ОТВЕТ.</summary>
    [Fact]
    public void Peer_Certificate_Checking_Is_Only_Dropped_By_The_Setting()
    {
        var was = HttpsPeers.TrustAny;
        try
        {
            using var handler = HttpsPeers.Handler();
            var check = handler.SslOptions.RemoteCertificateValidationCallback;
            Assert.NotNull(check);
            HttpsPeers.TrustAny = false;
            Assert.False(check(this, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.True(check(this, null, null, System.Net.Security.SslPolicyErrors.None));
            // тот же самый обработчик, без пересоздания клиента: настройка действует сразу
            HttpsPeers.TrustAny = true;
            Assert.True(check(this, null, null, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            HttpsPeers.TrustAny = was;
        }
    }

    /// <summary>ОБРАЩЕНИЕ К СЕБЕ ПО ПЕТЛЕ сертификат не проверяет — иначе интерфейс
    /// не смог бы позвать собственное API ни с каким сертификатом: адрес localhost имени
    /// в сертификате не совпадает никогда. Чужой адрес при этом проверяется как обычно.</summary>
    [Fact]
    public void The_Loopback_Handler_Skips_The_Check_Only_For_Loopback()
    {
        var bad = System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch;

        using var local = new HttpRequestMessage(HttpMethod.Get, "https://localhost:5480/ai2p/api/agent/call");
        using var remote = new HttpRequestMessage(HttpMethod.Get, "https://ai2p.example.com/ai2p/api/agent/call");

        Assert.True(HttpsPeers.AllowLoopback(local, bad));
        Assert.False(HttpsPeers.AllowLoopback(remote, bad));
        Assert.True(HttpsPeers.AllowLoopback(remote, System.Net.Security.SslPolicyErrors.None));
        // проверка обязана приходить в клиент ИМЕННО с запросом: у SocketsHttpHandler
        // отправитель другой, и адрес спросить не у чего (живая проверка, 500 на странице)
        var handler = Assert.IsType<HttpClientHandler>(HttpsPeers.LoopbackHandler());
        Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
    }

    // --- 6. документация и скрипты установки ---

    /// <summary>Глава про HTTPS есть в документации и на неё ведёт оглавление: сертификат,
    /// выписанный не глобальным УЦ, требует настройки БРАУЗЕРА, и рассказать об этом
    /// больше негде.</summary>
    [Fact]
    public void The_Documentation_Has_An_Https_Chapter_Linked_From_The_Index()
    {
        var doc = Path.Combine(RepoRoot(), "doc", "ru", "man", "https.md");
        Assert.True(File.Exists(doc), "нет главы doc/ru/man/https.md");

        var text = File.ReadAllText(doc);
        // браузерная часть — то, ради чего глава и написана: сертификат своего УЦ надо
        // один раз разрешить в каждом браузере, иначе экран встречает предупреждением
        Assert.Contains("браузер", text, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("man/https.md",
            File.ReadAllText(Path.Combine(RepoRoot(), "doc", "ru", "index.md")), StringComparison.Ordinal);
        Assert.Contains("(https.md)",
            File.ReadAllText(Path.Combine(RepoRoot(), "doc", "ru", "man", "README.md")),
            StringComparison.Ordinal);
    }

    /// <summary>Инструменты для сертификата ставятся скриптами окружения — на ВСЕХ трёх
    /// системах (Windows, Linux, macOS), как сказано в задании.</summary>
    [Fact]
    public void The_Environment_Scripts_Install_The_Certificate_Tools()
    {
        var bat = File.ReadAllText(Path.Combine(RepoRoot(), "install_required.bat"));
        var sh = File.ReadAllText(Path.Combine(RepoRoot(), "install_required.sh"));

        Assert.Contains("openssl", bat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openssl", sh, StringComparison.OrdinalIgnoreCase);
        // .cmd/.bat читается консолью побайтно — кириллица в нём сдвигает разбор (T-34-S0)
        Assert.All(File.ReadAllBytes(Path.Combine(RepoRoot(), "install_required.bat")),
            b => Assert.True(b < 128, "install_required.bat обязан быть чистым ASCII"));
    }

    /// <summary>Корень AI2P_app: тесты идут из bin/…, поэтому каталог ищется вверх по дереву
    /// по опознавательному файлу решения.</summary>
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir.Length > 0 && !File.Exists(Path.Combine(dir, "AI2P.sln")))
        {
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        }
        Assert.True(dir.Length > 0, "не найден корень AI2P_app (AI2P.sln)");
        return dir;
    }
}
