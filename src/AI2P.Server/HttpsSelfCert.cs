using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AI2P.Core;

namespace AI2P.Server;

/// <summary>
/// СЕРТИФИКАТ ДЛЯ HTTPS, КОТОРЫЙ ПРОГРАММА ДЕЛАЕТ САМА (T-315, третий проход).
///
/// Почему это понадобилось. Рецепт с <c>openssl</c> в документации требует от человека
/// двух шагов и семи ключей командной строки, и ошибиться в нём проще, чем сделать верно:
/// первый же заход заказчика дал сертификат УЦ вместо серверного (<c>req -x509</c> ставит
/// <c>CA:TRUE</c>), браузер отверг его молча, и вся затея выглядела как «HTTPS в программе
/// не работает». Здесь то же самое делается кнопкой в форме сервера, и ошибиться негде.
///
/// ЧТО ИМЕННО ДЕЛАЕТСЯ — ровно то, что требует браузер:
/// <list type="number">
/// <item>свой удостоверяющий центр (<c>ai2pCA.crt</c> + <c>ai2pCA.key</c>) — его человек
/// один раз ставит в браузер; НИКАКОЙ РАБОТАЮЩЕЙ СЛУЖБЫ УЦ для этого не нужно: браузер
/// проверяет ПОДПИСЬ сертификата сайта локально и никуда не ходит;</item>
/// <item>серверный сертификат (<c>ai2p.crt</c> + <c>ai2p.key</c>), подписанный этим
/// центром: <c>CA:FALSE</c>, <c>extendedKeyUsage=serverAuth</c> и SAN со ВСЕМИ именами,
/// по которым к серверу заходят — имя из настроек, второе (внешнее) имя, имя машины,
/// её адреса в сети и петля.</item>
/// </list>
///
/// Центр ПЕРЕИСПОЛЬЗУЕТСЯ (если его файлы уже лежат и он ещё годен): перевыпуск серверного
/// сертификата после смены имени или адреса не должен заставлять обходить все браузеры
/// кластера заново.
///
/// ЧЕТВЁРТЫЙ ПРОХОД (жалоба «кнопка отвечает 500 и ничего не делает»): у каждого отказа
/// здесь есть СВОИ СЛОВА — права на каталог, отказ криптографии, посторонние файлы центра
/// под нашими именами. Кнопка, которая молча отвечает пустым «500», неотличима от сломанной
/// программы, и разобраться по такому ответу нельзя ни человеку, ни нам.
/// </summary>
public static class HttpsSelfCert
{
    /// <summary>Каталог файлов сертификата рядом с <c>config.json</c>.</summary>
    public const string DirName = "certs";

    /// <summary>Имена файлов: пара центра и пара сервера.</summary>
    public const string CaCertName = "ai2pCA.crt";
    public const string CaKeyName = "ai2pCA.key";
    public const string CertName = "ai2p.crt";
    public const string KeyName = "ai2p.key";

    /// <summary>Сколько лет годен свой центр (браузер ставится один раз — пусть живёт долго).</summary>
    private const int CaYears = 10;

    /// <summary>Сколько дней годен серверный сертификат. 825 — предел, который браузеры
    /// считают разумным для сертификата сайта; больше ставить незачем, перевыпуск здесь
    /// стоит одного нажатия.</summary>
    private const int LeafDays = 825;

    /// <summary>Готовые файлы: пути относительно <c>config.json</c> (их и пишем в настройки)
    /// и полный путь файла центра — его человеку ставить в браузер.</summary>
    public sealed record Made(string CertFile, string KeyFile, string CaFile, string CaPath,
        string Names, string Info);

    /// <summary>
    /// Выпустить пару «центр + серверный сертификат» рядом с <c>config.json</c>.
    /// </summary>
    /// <param name="hostname">Имя сервера из настроек — главное имя сертификата.</param>
    /// <param name="hostname2">Второе (внешнее) имя сервера; пусто — второго адреса нет.</param>
    /// <param name="configPath">Путь к <c>config.json</c>: файлы лягут в его каталог.</param>
    public static Made Create(string hostname, string hostname2, string configPath)
    {
        var host = (hostname ?? "").Trim();
        if (host.Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.https.15"));
        }
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, DirName);
        MakeDir(dir);
        var caCertPath = Path.Combine(dir, CaCertName);
        var caKeyPath = Path.Combine(dir, CaKeyName);
        var certPath = Path.Combine(dir, CertName);
        var keyPath = Path.Combine(dir, KeyName);

        using var ca = ReuseCa(caCertPath, caKeyPath) ?? MakeCa(host, caCertPath, caKeyPath);
        var names = Names(host, (hostname2 ?? "").Trim());
        using var key = RSA.Create(2048);
        using var leaf = Sign(host, names, key, ca);
        WriteText(certPath, leaf.ExportCertificatePem());
        WriteSecret(keyPath, key.ExportPkcs8PrivateKeyPem());

        var nameList = string.Join(", ", names);
        return new Made(
            CertFile: $"{DirName}/{CertName}",
            KeyFile: $"{DirName}/{KeyName}",
            CaFile: $"{DirName}/{CaCertName}",
            CaPath: caCertPath,
            Names: nameList,
            Info: Loc.T("msg.https.16", nameList, caCertPath));
    }

    /// <summary>Полный путь файла сертификата своего центра (его отдаёт API на скачивание);
    /// null — центра ещё нет.</summary>
    public static string? CaPath(string configPath)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, DirName,
            CaCertName);
        return File.Exists(path) ? path : null;
    }

    // --- подпись серверного сертификата ---

    /// <summary>
    /// Серверный сертификат, подписанный центром. Вся работа с криптографией собрана здесь
    /// ради ОДНОГО: что бы ни отказало (негодное имя в <c>CN</c>, чужой центр без нужного
    /// расширения, отказ OpenSSL на Linux), человек обязан увидеть ПРИЧИНУ, а не пустой
    /// «500» в углу экрана — четвёртый проход T-315 начался ровно с этого.
    /// </summary>
    private static X509Certificate2 Sign(string host, List<string> names, RSA key,
        X509Certificate2 ca)
    {
        try
        {
            // имя строится СБОРЩИКОМ, а не строкой «CN=…»: строку разбирают по правилам
            // X.500, и имя сервера с запятой или знаком равенства роняло бы выпуск
            var subject = new X500DistinguishedNameBuilder();
            subject.AddCommonName(host);
            var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            // САЙТ, А НЕ ЦЕНТР: ровно то, чего не хватало сертификату из рецепта openssl
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));   // serverAuth
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            try
            {
                request.CertificateExtensions.Add(
                    X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
            }
            catch (CryptographicException)
            {
                // у центра нет своего опознавателя ключа (так бывает у сертификата,
                // сделанного чужой командой) — расширение необязательное, обходимся без него
            }
            request.CertificateExtensions.Add(SubjectNames(names));
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            return request.Create(ca, from, DateTimeOffset.UtcNow.AddDays(LeafDays), Serial());
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                Loc.T("msg.https.17", $"{ex.GetType().Name}: {ex.Message}"), ex);
        }
    }

    // --- удостоверяющий центр ---

    /// <summary>
    /// Готовый центр с ключом, если он уже выпускался и ГОДИТСЯ ДЛЯ ПОДПИСИ. Негодные файлы
    /// (чужой сертификат под нашим именем, центр без закрытого ключа, просроченный) не
    /// повод отказать человеку в кнопке: они отодвигаются в <c>*.bak</c>, и рядом выпускается
    /// новый центр. Иначе один раз положенный в каталог посторонний файл ломал бы выпуск
    /// сертификата навсегда, и починить это можно было бы только руками через файловую систему.
    /// </summary>
    private static X509Certificate2? ReuseCa(string certPath, string keyPath)
    {
        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return null;
        }
        try
        {
            var ca = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            if (CanIssue(ca))
            {
                return ca;
            }
            ca.Dispose();
        }
        catch (Exception)
        {
            // непрочитанный центр — не беда: выпустим новый рядом
        }
        MoveAside(certPath);
        MoveAside(keyPath);
        return null;
    }

    /// <summary>Этим центром можно подписать серверный сертификат: ключ на месте, срок ещё
    /// не вышел (с запасом в месяц), и он помечен центром с правом подписывать сертификаты.
    /// Ровно это и проверяет .NET перед подписью, только отказывает невнятно.</summary>
    private static bool CanIssue(X509Certificate2 ca)
    {
        if (!ca.HasPrivateKey || ca.NotAfter <= DateTime.Now.AddDays(30)
                              || ca.NotBefore > DateTime.Now)
        {
            return false;
        }
        var basics = ca.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (basics is null || !basics.CertificateAuthority)
        {
            return false;
        }
        var usage = ca.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return usage is null || usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign);
    }

    /// <summary>Отодвинуть негодный файл: он больше не мешает, но и не пропадает.</summary>
    private static void MoveAside(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + ".bak", overwrite: true);
            }
        }
        catch (Exception)
        {
            // не переименовался — не беда: выпуск всё равно перезапишет файл
        }
    }

    private static X509Certificate2 MakeCa(string host, string certPath, string keyPath)
    {
        string certPem;
        string keyPem;
        try
        {
            using var key = RSA.Create(2048);
            var subject = new X500DistinguishedNameBuilder();
            subject.AddCommonName($"AI2P Local CA {host}");
            var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var self = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(CaYears));
            certPem = self.ExportCertificatePem();
            keyPem = key.ExportPkcs8PrivateKeyPem();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                Loc.T("msg.https.17", $"{ex.GetType().Name}: {ex.Message}"), ex);
        }
        WriteText(certPath, certPem);
        WriteSecret(keyPath, keyPem);
        // подписывать надо сертификатом С КЛЮЧОМ, и брать его лучше из того же, что легло
        // на диск: дальше этой парой файлов пользуется и перевыпуск серверного сертификата
        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    // --- имена, серийный номер, файлы ---

    /// <summary>ВСЕ имена, по которым к серверу заходят. Петля здесь обязательна: по ней
    /// открывается интерфейс на самой машине, а имени, которого в SAN нет, браузер не верит
    /// (T-315, первый заход заказчика — именно этот случай).</summary>
    private static List<string> Names(string host, string host2)
    {
        var names = new List<string> { host };
        if (host2.Length > 0)
        {
            names.Add(host2);
        }
        names.Add("localhost");
        names.Add(Environment.MachineName);
        names.Add("127.0.0.1");
        names.Add("::1");
        try
        {
            names.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
                .Where(a => a.AddressFamily is AddressFamily.InterNetwork
                            or AddressFamily.InterNetworkV6)
                .Select(a => a.ToString()));
        }
        catch (Exception)
        {
            // имён машины в сети может не быть вовсе — не повод не выпускать сертификат
        }
        return names
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Расширение SAN: адрес кладётся адресом, имя — именем. Браузер сверяет строку
    /// адреса именно с этим списком.</summary>
    private static X509Extension SubjectNames(IEnumerable<string> names)
    {
        var builder = new SubjectAlternativeNameBuilder();
        foreach (var name in names)
        {
            try
            {
                if (IPAddress.TryParse(name, out var ip))
                {
                    builder.AddIpAddress(ip);
                }
                else
                {
                    builder.AddDnsName(name);
                }
            }
            catch (Exception)
            {
                // имя, которое в сертификат не лезет (буквы не латиницей у имени машины,
                // адрес с областью действия), пропускаем: остальные имена от этого страдать
                // не должны — сертификат нужен человеку целиком, а не «всё или ничего»
            }
        }
        return builder.Build();
    }

    /// <summary>Серийный номер: случайный и ПОЛОЖИТЕЛЬНЫЙ (старший бит снят — иначе число
    /// читается как отрицательное, и часть проверяющих такой сертификат бракует).</summary>
    private static byte[] Serial()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        return serial;
    }

    /// <summary>Каталог файлов сертификата. Отказ здесь — это почти всегда права на каталог
    /// установки (у службы своя учётная запись), и человеку надо назвать и путь, и причину:
    /// «500» без единого слова про каталог не подсказывает ничего.</summary>
    private static void MakeDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Loc.T("msg.https.18", dir, ex.Message), ex);
        }
    }

    private static void WriteText(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text + Environment.NewLine);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Loc.T("msg.https.18", path, ex.Message), ex);
        }
    }

    /// <summary>Закрытый ключ: на Unix — только владельцу (0600). Владелец здесь тот, под кем
    /// идёт сам сервер, поэтому беды «служба не читает свой ключ» (T-315, первый проход)
    /// это не создаёт: файл пишет тот же процесс, что потом его и читает.</summary>
    private static void WriteSecret(string path, string text)
    {
        WriteText(path, text);
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // права на файловой системе, которая их не держит, — не повод терять готовый
            // ключ: он уже записан, а сузить доступ можно и руками
        }
    }
}
