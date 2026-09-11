using System.Text.Json.Nodes;
using AI2P.Core;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-215-S0 (выпуск 1.129): ДОВЕРИЕ СЕРТИФИКАТУ СОСЕДЕЙ ПО КЛАСТЕРУ — УМОЛЧАНИЕ «ДА».
///
/// Жалоба: третий сервер (macOS) не подключался к кластеру, где первый уже переведён на
/// https, — «удалённый сертификат отклонён». Сертификат у наших серверов почти всегда
/// самодельный (кнопка «Создать сертификат», T-315), подтвердить исключение в обмене
/// сервер-сервер некому, и снятый по умолчанию флажок означал, что первое же включение
/// HTTPS в кластере останавливает репликацию, пока человек не догадается, где искать.
///
/// Отсюда две части правки:
/// <list type="number">
/// <item>умолчание — включено (и в коде, и в <c>config.json</c> дистрибутива);</item>
/// <item>РАЗОВОЕ включение при обновлении: у переживших обновление установок ключ уже лежит
///   в файле со значением <c>false</c>, а слияние конфигураций кладёт значения человека
///   ПОВЕРХ новых умолчаний, — поэтому новое умолчание само до них не доедет никогда.</item>
/// </list>
/// Выключить настройку по-прежнему можно галочкой в форме своего сервера, и выключение
/// переживает следующее обновление: разовый шаг привязан к отметке билда в серверной БД.
/// </summary>
public sealed class T215S0Tests
{
    /// <summary>Билд, начиная с которого умолчание включено; он же — порог разового шага
    /// в <c>Program.cs</c>.</summary>
    private const int TrustDefaultBuild = 129;

    // --- 1. умолчание ---

    /// <summary>Настройки, о которых в файле не сказано ничего, доверяют соседям.</summary>
    [Fact]
    public void Peers_Are_Trusted_By_Default()
    {
        Assert.True(new Ai2pConfig.HttpsSettings().TrustAnyPeer);
        Assert.True(new Ai2pConfig().Ui.Https.TrustAnyPeer);
    }

    /// <summary>Умолчание названо и в <c>config.json</c> дистрибутива: файл человек читает
    /// глазами, и значение в нём обязано совпадать с поведением кода.</summary>
    [Fact]
    public void The_Shipped_Config_Trusts_Peers_Too()
    {
        var path = Path.Combine(RepoRoot(), "src", "AI2P.Server", "config.json");
        var https = JsonNode.Parse(File.ReadAllText(path))!["ui"]!["https"]!.AsObject();

        Assert.True((bool)https["trustAnyPeer"]!);
    }

    /// <summary>Выключенное человеком значение переживает обновление: слияние кладёт его
    /// поверх умолчания дистрибутива. Это и есть причина, по которой одного нового
    /// умолчания мало.</summary>
    [Fact]
    public void An_Upgrade_Keeps_The_Value_From_The_Old_Config()
    {
        var shipped = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.Server", "config.json"));

        var merged = ConfigMerge.Merge(shipped,
            """{"ui":{"protocol":"https","https":{"trustAnyPeer":false}}}""");

        Assert.False(JsonNode.Parse(merged)!["ui"]!["https"]!["trustAnyPeer"]!.GetValue<bool>());
    }

    // --- 2. разовое включение при обновлении ---

    /// <summary>Билд, с которого обновляются данные: чистая установка — обновлять нечего,
    /// отметка старой версии — она сама, данные новее запущенной версии — тоже нечего.</summary>
    [Fact]
    public void The_Build_We_Are_Upgrading_From_Is_Told_Apart_From_A_Fresh_Install()
    {
        using var fixture = new StorageFixture();
        var registry = TestRegistry(fixture);

        // чистая установка: ни аккаунтов, ни организаций
        Assert.Null(Upgrade.UpgradingFrom(registry));

        registry.ServerDb.SetMeta(Upgrade.BuildKey, "120");
        Assert.Equal(120, Upgrade.UpgradingFrom(registry));

        // данные уже доведены до текущего билда — и то же самое для версии постарше
        registry.ServerDb.SetMeta(Upgrade.BuildKey, AppInfo.Build.ToString());
        Assert.Null(Upgrade.UpgradingFrom(registry));
        registry.ServerDb.SetMeta(Upgrade.BuildKey, "999");
        Assert.Null(Upgrade.UpgradingFrom(registry));
    }

    /// <summary>Разовое включение стоит в <c>Program.cs</c> ДО шагов обновления: они
    /// перезаписывают отметку билда, и после них признак «мы обновляемся» уже потерян.</summary>
    [Fact]
    public void Program_Turns_The_Setting_On_Once_Before_The_Upgrade_Steps()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.Server", "Program.cs"));

        var switchOn = program.IndexOf("Upgrade.UpgradingFrom(orgs)", StringComparison.Ordinal);
        var steps = program.IndexOf("Upgrade.Run(orgs)", StringComparison.Ordinal);

        Assert.True(switchOn > 0, "в Program.cs нет разового включения доверия соседям");
        Assert.True(steps > switchOn, "включение обязано стоять до Upgrade.Run");
        Assert.Contains($"upgradingFrom < {TrustDefaultBuild}", program, StringComparison.Ordinal);
        // настройка действует сразу, без перезапуска: клиенты сервер-сервер спрашивают её
        // в момент рукопожатия (T-315)
        Assert.Contains("HttpsPeers.TrustAny = true", program, StringComparison.Ordinal);
    }

    /// <summary>Корень <c>AI2P_app</c>: тесты идут из bin/…, поэтому каталог ищется вверх
    /// по дереву по файлу решения.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static AI2P.Server.Org.OrgRegistry TestRegistry(StorageFixture fixture) =>
        new(Path.Combine(fixture.Dir, "registry"), "server.db", new AI2P.Server.Org.OrgDeps(
            DbFile: "ai2p.db",
            I18nDir: Path.Combine(AppContext.BaseDirectory, "i18n"),
            Secrets: fixture.Secrets,
            LocalModels: fixture.LocalModels,
            Language: () => "ru",
            PublicBaseUrl: () => "http://localhost:5480/ai2p",
            ModelsRepo: () => fixture.Dir,
            DistDir: () => "",
            PackagesDir: () => "",
            IsConductor: _ => true,
            LocalServerId: () => "local",
            LocalCode: _ => "",
            ServerCode: (_, _) => "",
            ServerName: _ => ""));
}
