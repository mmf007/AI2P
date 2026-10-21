using AI2P.Core.Entities;
using AI2P.Server.Api;
using AI2P.Storage;
using AI2P.Storage.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-508-S0 (плагин VS Code, этап 8.3): токен устройства и вход встроенного режима.
/// Выпуск, хэширование, отзыв, одноразовость и срок embedToken, атрибуты cookie встроенного
/// режима (обычная осталась Lax), разграничение по человеку и организации.
/// </summary>
public sealed class T508S0Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "ai2p-t508s0-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;
    private readonly DeviceTokenService _tokens;

    public T508S0Tests()
    {
        _db = new Database(_dir, "server.db", DatabaseKind.Server);
        _db.Init();
        _tokens = new DeviceTokenService(_db, new EventStore(_db));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Issue_Stores_Only_The_Hash_And_Verifies_The_Token()
    {
        var (record, token) = _tokens.Issue("acc-1", "org-a", "prj-1", "VS Code, ноутбук");

        Assert.StartsWith(DeviceTokenService.Prefix, token);
        Assert.True(token.Length > 40);
        using (var conn = _db.Open())
        {
            var stored = Sql.Scalar<string>(conn, null, "SELECT token_hash FROM device_tokens WHERE id=@id",
                ("@id", record.Id));
            Assert.Equal(DeviceTokenService.Hash(token), stored);
            Assert.NotEqual(token, stored);
            // самого токена в базе нет нигде, в том числе в журнале
            var leaked = Sql.Scalar<long>(conn, null,
                "SELECT COUNT(*) FROM events WHERE payload_json LIKE @t", ("@t", "%" + token + "%"));
            Assert.Equal(0, leaked);
        }
        var found = _tokens.Verify(token);
        Assert.NotNull(found);
        Assert.Equal(record.Id, found!.Id);
        Assert.NotNull(_tokens.Get(record.Id)!.LastUsedAt);
        Assert.Null(_tokens.Verify(token + "x"));
        Assert.Null(_tokens.Verify("чужое"));
    }

    [Fact]
    public void Revoked_Token_Is_Not_Accepted()
    {
        var (record, token) = _tokens.Issue("acc-1", "org-a", "prj-1", "VS Code");

        Assert.True(_tokens.Revoke(record.Id, "acc-1"));

        Assert.Null(_tokens.Verify(token));
        Assert.False(_tokens.IsActive(record.Id));
        Assert.Empty(_tokens.List("acc-1", "org-a"));
        Assert.Single(_tokens.List("acc-1", "org-a", includeRevoked: true));
        Assert.False(_tokens.Revoke(record.Id, "acc-1")); // второй раз — нечего отзывать
    }

    [Fact]
    public void Revoke_Project_Revokes_Only_Own_Tokens_Of_That_Project()
    {
        var (a, _) = _tokens.Issue("acc-1", "org-a", "prj-1", "окно 1");
        var (b, _) = _tokens.Issue("acc-1", "org-a", "prj-1", "окно 2");
        var (other, _) = _tokens.Issue("acc-1", "org-a", "prj-2", "другой проект");
        var (foreign, _) = _tokens.Issue("acc-2", "org-a", "prj-1", "чужой");

        Assert.Equal(2, _tokens.RevokeProject("acc-1", "org-a", "prj-1"));

        Assert.False(_tokens.IsActive(a.Id));
        Assert.False(_tokens.IsActive(b.Id));
        Assert.True(_tokens.IsActive(other.Id));
        Assert.True(_tokens.IsActive(foreign.Id));
    }

    [Fact]
    public void Foreign_Token_Is_Neither_Listed_Nor_Revocable()
    {
        var (record, _) = _tokens.Issue("acc-1", "org-a", "prj-1", "VS Code");

        Assert.Empty(_tokens.List("acc-2", "org-a"));
        Assert.Empty(_tokens.List("acc-1", "org-b"));
        Assert.False(_tokens.Revoke(record.Id, "acc-2"));
        Assert.True(_tokens.IsActive(record.Id));
    }

    [Fact]
    public void Device_Tokens_Are_Not_Replicated()
    {
        Assert.DoesNotContain("device_tokens", ChangeLog.Tables(DatabaseKind.Server));
    }

    [Fact]
    public void Embed_Token_Is_One_Time()
    {
        var store = new EmbedTokenStore();
        var device = new DeviceToken { AccountId = "acc-1", OrgId = "org-a", ProjectId = "prj-1" };
        var token = store.Issue(device);

        var first = store.Redeem(token);
        Assert.NotNull(first);
        Assert.Equal("acc-1", first!.AccountId);
        Assert.Equal(device.Id, first.DeviceTokenId);
        Assert.Null(store.Redeem(token));
        Assert.Null(store.Redeem(""));
    }

    [Fact]
    public void Expired_Embed_Token_Is_Not_Accepted()
    {
        var now = DateTime.UtcNow;
        var store = new EmbedTokenStore(() => now);
        var token = store.Issue(new DeviceToken { AccountId = "acc-1", OrgId = "org-a", ProjectId = "p" });

        now += EmbedTokenStore.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(store.Redeem(token));
    }

    [Fact]
    public void Embed_Cookie_Is_None_Secure_HttpOnly_And_Login_Cookie_Stays_Lax()
    {
        var options = new CookieAuthenticationOptions();
        Ai2pAuth.ConfigureEmbedCookie(options, "/ai2p");

        Assert.Equal(Ai2pAuth.EmbedCookie, options.Cookie.Name);
        Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal("/ai2p", options.Cookie.Path);

        // обычная cookie входа настраивается в Program.cs — она обязана остаться Lax
        var program = File.ReadAllText(FindProgram());
        var start = program.IndexOf("options.Cookie.Name = \"ai2p.auth\";", StringComparison.Ordinal);
        Assert.True(start > 0);
        var block = program.Substring(start, 200);
        Assert.Contains("options.Cookie.SameSite = SameSiteMode.Lax;", block);
    }

    [Fact]
    public void Scheme_Is_Selected_By_Cookie()
    {
        Assert.Equal(Ai2pAuth.Scheme, Ai2pAuth.SelectScheme(WithCookies()));
        Assert.Equal(Ai2pAuth.EmbedScheme, Ai2pAuth.SelectScheme(WithCookies(Ai2pAuth.EmbedCookie)));
        Assert.Equal(Ai2pAuth.Scheme, Ai2pAuth.SelectScheme(WithCookies("ai2p.auth", Ai2pAuth.EmbedCookie)));
    }

    [Fact]
    public void Embed_Principal_Is_Bound_To_The_Token_Organization()
    {
        var account = new Account { Id = "acc-1", Name = "Майк", Email = "m@x" };
        var device = new DeviceToken { AccountId = "acc-1", OrgId = "org-a", ProjectId = "p" };

        var principal = Ai2pAuth.EmbedPrincipal(account, device);

        Assert.Equal("org-a", Ai2pAuth.EmbedOrgId(principal));
        Assert.Equal(device.Id, principal.FindFirst(Ai2pAuth.DeviceClaim)?.Value);
        Assert.Null(Ai2pAuth.EmbedOrgId(Ai2pAuth.Principal(account, Ai2pAuth.Scheme)));
    }

    [Theory]
    [InlineData("/acme/task/T-5?embed=vscode", "/acme/task/T-5?embed=vscode")]
    [InlineData("/task/T-5", "/acme/task/T-5")]
    [InlineData("", "/acme/")]
    [InlineData("//evil.example/x", "/acme/")]
    [InlineData("https://evil.example/", "/acme/")]
    [InlineData("/\\evil.example", "/acme/")]
    public void Enter_Target_Stays_Inside_The_Token_Organization(string to, string expected)
    {
        Assert.Equal(expected, EmbedEndpoints.EnterTarget(to, "acme"));
    }

    private static HttpContext WithCookies(params string[] names)
    {
        var ctx = new DefaultHttpContext();
        if (names.Length > 0)
        {
            ctx.Request.Headers.Cookie = string.Join("; ", names.Select(n => n + "=1"));
        }
        return ctx;
    }

    private static string FindProgram()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "src", "AI2P.Server", "Program.cs");
            if (File.Exists(path))
            {
                return path;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Program.cs");
    }
}
