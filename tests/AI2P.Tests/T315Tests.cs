using System.Security.Cryptography.X509Certificates;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-315 (третий проход): СЕРТИФИКАТ, КОТОРЫЙ ПРОГРАММА ВЫПИСЫВАЕТ СЕБЕ САМА.
///
/// Рецепт с <c>openssl</c> человек выполняет руками, и первый же заход заказчика дал
/// сертификат УДОСТОВЕРЯЮЩЕГО ЦЕНТРА вместо серверного: браузер отверг его молча, и вся
/// затея выглядела как «HTTPS в программе не работает». Кнопка «Создать сертификат» делает
/// ровно то, что требует браузер, и проверяется это здесь:
/// <list type="number">
/// <item>сертификатов ДВА — свой центр и подписанный им серверный;</item>
/// <item>серверный НЕ помечен как УЦ, а все имена сервера названы в SAN — то есть
/// <see cref="HttpsCertificate.BrowserWarning"/> к нему не придирается;</item>
/// <item>пара читается ТЕМ ЖЕ загрузчиком, которым её возьмёт Kestrel;</item>
/// <item>цепочка сходится к своему центру, и перевыпуск центр ПЕРЕИСПОЛЬЗУЕТ — иначе после
/// каждой правки имени пришлось бы обходить все браузеры кластера заново.</item>
/// </list>
/// </summary>
public sealed class T315Tests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t315-" + Guid.NewGuid().ToString("N"));

    public T315Tests() => Directory.CreateDirectory(_dir);

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

    private string ConfigPath => Path.Combine(_dir, "config.json");

    /// <summary>Настройки «сертификат из файлов» на только что выписанную пару.</summary>
    private static Ai2pConfig.HttpsSettings FileSettings(HttpsSelfCert.Made made) => new()
    {
        Source = Ai2pConfig.HttpsSettings.SourceFile,
        CertFile = made.CertFile,
        KeyFile = made.KeyFile,
    };

    [Fact]
    public void Self_Signed_Pair_Is_Written_And_Read_Back()
    {
        var made = HttpsSelfCert.Create("ai2pS1.local", "178.252.197.47", ConfigPath);

        Assert.Equal("certs/ai2p.crt", made.CertFile);
        Assert.Equal("certs/ai2p.key", made.KeyFile);
        Assert.True(File.Exists(Path.Combine(_dir, "certs", "ai2p.crt")));
        Assert.True(File.Exists(Path.Combine(_dir, "certs", "ai2p.key")));
        Assert.True(File.Exists(made.CaPath));
        Assert.Equal(made.CaPath, HttpsSelfCert.CaPath(ConfigPath));

        // читается тем же путём, каким сертификат достаётся Kestrel'ю
        using var leaf = HttpsCertificate.Load(FileSettings(made), "ai2pS1.local", ConfigPath, "");
        Assert.True(leaf.HasPrivateKey);
    }

    [Fact]
    public void Server_Certificate_Suits_The_Browser()
    {
        var made = HttpsSelfCert.Create("ai2pS1.local", "178.252.197.47", ConfigPath);
        using var leaf = HttpsCertificate.Load(FileSettings(made), "ai2pS1.local", ConfigPath, "");

        // ни «помечен как УЦ», ни «нет SAN», ни «имени сервера в SAN нет»
        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "ai2pS1.local"));
        // петля названа тоже: по ней открывается интерфейс на самой машине
        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "localhost"));
        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "127.0.0.1"));
        // второе (внешнее) имя сервера
        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "178.252.197.47"));
        // чужое имя — по-прежнему беда, иначе проверка ничего не значит
        Assert.NotNull(HttpsCertificate.BrowserWarning(leaf, "example.org"));

        // а вот сам ЦЕНТР сертификатом сайта быть не может — ровно то, на чём споткнулся заказчик
        using var ca = new X509Certificate2(made.CaPath);
        Assert.NotNull(HttpsCertificate.BrowserWarning(ca, "ai2pS1.local"));
    }

    [Fact]
    public void Chain_Ends_At_Our_Own_Authority()
    {
        var made = HttpsSelfCert.Create("ai2pS1.local", "", ConfigPath);
        using var leaf = HttpsCertificate.Load(FileSettings(made), "ai2pS1.local", ConfigPath, "");
        using var ca = new X509Certificate2(made.CaPath);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(leaf),
            string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim())));
    }

    [Fact]
    public void Reissue_Keeps_The_Authority()
    {
        var first = HttpsSelfCert.Create("ai2pS1.local", "", ConfigPath);
        using var leaf1 = HttpsCertificate.Load(FileSettings(first), "ai2pS1.local", ConfigPath, "");
        using var ca1 = new X509Certificate2(first.CaPath);

        // второе нажатие кнопки: имя сервера сменилось, сертификат нужен новый
        var second = HttpsSelfCert.Create("ai2pS2.local", "", ConfigPath);
        using var leaf2 = HttpsCertificate.Load(FileSettings(second), "ai2pS2.local", ConfigPath, "");
        using var ca2 = new X509Certificate2(second.CaPath);

        // центр ТОТ ЖЕ — браузеры обходить заново не надо
        Assert.Equal(ca1.Thumbprint, ca2.Thumbprint);
        // а серверный сертификат новый и выписан уже на новое имя
        Assert.NotEqual(leaf1.Thumbprint, leaf2.Thumbprint);
        Assert.Null(HttpsCertificate.BrowserWarning(leaf2, "ai2pS2.local"));
    }

    [Fact]
    public void Server_Without_A_Name_Is_Refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => HttpsSelfCert.Create("  ", "", ConfigPath));
        Assert.NotEmpty(ex.Message);
    }

    /// <summary>
    /// ЧЕТВЁРТЫЙ ПРОХОД: посторонние файлы под нашими именами не ломают кнопку навсегда.
    /// Человек делает сертификат по рецепту <c>openssl</c> и кладёт получившиеся файлы в тот
    /// же каталог; если такой «центр» нельзя прочитать или им нельзя подписывать, кнопка
    /// обязана отодвинуть его и выписать свой, а не отвечать отказом при каждом нажатии.
    /// </summary>
    [Fact]
    public void Foreign_Authority_Files_Do_Not_Block_The_Button()
    {
        var certs = Path.Combine(_dir, "certs");
        Directory.CreateDirectory(certs);
        File.WriteAllText(Path.Combine(certs, "ai2pCA.crt"), "не сертификат вовсе");
        File.WriteAllText(Path.Combine(certs, "ai2pCA.key"), "и не ключ");

        var made = HttpsSelfCert.Create("ai2pS1.local", "", ConfigPath);

        using var leaf = HttpsCertificate.Load(FileSettings(made), "ai2pS1.local", ConfigPath, "");
        Assert.True(leaf.HasPrivateKey);
        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "ai2pS1.local"));
        // негодные файлы не пропали, а отодвинуты
        Assert.True(File.Exists(Path.Combine(certs, "ai2pCA.crt.bak")));
        Assert.True(File.Exists(Path.Combine(certs, "ai2pCA.key.bak")));
    }

    /// <summary>
    /// ЧЕТВЁРТЫЙ ПРОХОД: предупреждение формы смотрит на ОБА имени сервера. По второму
    /// (внешнему) имени в браузер заходят снаружи, и сертификат, не назвавший его, откажет
    /// ровно так же, как если бы не было названо главное имя.
    /// </summary>
    [Fact]
    public void Both_Server_Names_Are_Checked()
    {
        var made = HttpsSelfCert.Create("ai2pS1.local", "", ConfigPath);
        using var leaf = HttpsCertificate.Load(FileSettings(made), "ai2pS1.local", ConfigPath, "");

        Assert.Null(HttpsCertificate.BrowserWarning(leaf, "ai2pS1.local", "localhost"));
        var warning = HttpsCertificate.BrowserWarning(leaf, "ai2pS1.local", "ai2p.example.org");
        Assert.NotNull(warning);
        Assert.Contains("ai2p.example.org", warning);
    }
}
