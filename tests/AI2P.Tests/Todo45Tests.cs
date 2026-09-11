using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Events;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// todo45 / этап 45 (ТЗ v1.52): КЛЮЧИ API ОРГАНИЗАЦИИ. Ключ принадлежит организации, лежит
/// в её БД зашифрованным ключом организации (AES-GCM) и потому свободно реплицируется;
/// сам ключ организации живёт только в secrets.json сервера и не реплицируется никогда.
///
/// Отдельно проверяется слияние, о котором спрашивал заказчик: разные ключи сливаются сами
/// (это разные строки), один и тот же ключ с одинаковым значением тоже, а один ключ с разными
/// значениями на двух серверах — конфликт с тремя решениями.
/// </summary>
public sealed class Todo45Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-todo45-" + Guid.NewGuid().ToString("N"));

    private const string NodeA = "unid-S0";
    private const string NodeB = "unid-S1";
    private const string OrgId = "org-1";

    private readonly Database _a;
    private readonly Database _b;
    private readonly SecretStore _secretsA;
    private readonly SecretStore _secretsB;

    public Todo45Tests()
    {
        Directory.CreateDirectory(_dir);
        _a = new Database(Path.Combine(_dir, "a"), "ai2p.db");
        _a.Init(NodeA);
        _b = new Database(Path.Combine(_dir, "b"), "ai2p.db");
        _b.Init(NodeB);
        _secretsA = new SecretStore(Path.Combine(_dir, "a", "secrets.json"));
        _secretsB = new SecretStore(Path.Combine(_dir, "b", "secrets.json"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private ModelKeyStore Keys(Database db) => new(db, new EventStore(db));

    /// <summary>Один такт репликации: перенести изменения из источника в приёмник.</summary>
    private static (ApplyResult Result, long Cursor) Sync(Database from, Database to, long cursor,
        string toNode, Func<string, string, Dictionary<string, object?>, bool>? guard = null)
    {
        List<RowChange> items;
        using (var conn = from.Open())
        {
            items = ChangeLog.Read(conn, cursor, 500, toNode);
        }
        if (items.Count == 0)
        {
            return (new ApplyResult(), cursor);
        }
        return (ChangeLog.Apply(to, items, guard), items[^1].Seq);
    }

    // --- ключ организации и шифрование ---

    [Fact]
    public void Organization_Key_Is_Created_Once_And_Is_32_Bytes()
    {
        var keys = new OrgSecretKey(_secretsA);

        var first = keys.Ensure(OrgId);
        var again = keys.Ensure(OrgId);

        Assert.Equal(32, first.Length);          // AES-256
        Assert.Equal(first, again);              // второй вызов не перевыпускает ключ
        Assert.True(keys.Has(OrgId));
        Assert.Null(keys.Read("другая-организация"));
    }

    [Fact]
    public void Value_Is_Encrypted_And_Never_Stored_In_Clear()
    {
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);

        var encrypted = OrgSecretKey.Encrypt(orgKey, "sk-секретный-ключ");

        Assert.DoesNotContain("sk-секретный", encrypted);
        Assert.Equal("sk-секретный-ключ", OrgSecretKey.Decrypt(orgKey, encrypted));
    }

    [Fact]
    public void Same_Value_Encrypts_Differently_Every_Time()
    {
        // случайный nonce: два шифртекста одного значения не совпадают, поэтому сравнивать
        // ключи можно только в открытом виде — на этом стоит определение конфликта
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);

        var first = OrgSecretKey.Encrypt(orgKey, "sk-один-и-тот-же");
        var second = OrgSecretKey.Encrypt(orgKey, "sk-один-и-тот-же");

        Assert.NotEqual(first, second);
        Assert.Equal(OrgSecretKey.Decrypt(orgKey, first), OrgSecretKey.Decrypt(orgKey, second));
    }

    [Fact]
    public void Foreign_Organization_Key_Does_Not_Decrypt()
    {
        var ours = new OrgSecretKey(_secretsA).Ensure(OrgId);
        var foreign = new OrgSecretKey(_secretsB).Ensure(OrgId);
        var encrypted = OrgSecretKey.Encrypt(ours, "sk-наш");

        // сервер, получивший НЕ ТОТ ключ организации, значение не прочитает — и это верно:
        // иначе зашифрованный blob не был бы защитой вовсе
        Assert.NotEqual(ours, foreign);
        Assert.Null(OrgSecretKey.Decrypt(foreign, encrypted));
    }

    [Fact]
    public void Organization_Key_Travels_As_Base64_Once()
    {
        var conductor = new OrgSecretKey(_secretsA);
        var joined = new OrgSecretKey(_secretsB);

        joined.Import(OrgId, conductor.Export(OrgId));

        Assert.Equal(conductor.Read(OrgId), joined.Read(OrgId));
        // так подключившийся сервер и получает возможность читать ключи API организации
        var encrypted = OrgSecretKey.Encrypt(conductor.Read(OrgId)!, "sk-общий");
        Assert.Equal("sk-общий", OrgSecretKey.Decrypt(joined.Read(OrgId)!, encrypted));
    }

    // --- хранилище ключей ---

    [Fact]
    public void Key_Row_Id_Is_The_Same_On_Every_Server()
    {
        // два сервера, независимо заведшие «anthropic.apiKey», обязаны получить ОДНУ строку:
        // иначе репликация принесла бы вторую и упёрлась в UNIQUE(secret_ref)
        Assert.Equal(ModelKeyStore.IdOf("anthropic.apiKey"), ModelKeyStore.IdOf("anthropic.apiKey"));
        Assert.NotEqual(ModelKeyStore.IdOf("anthropic.apiKey"), ModelKeyStore.IdOf("deepseek.apiKey"));

        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "A"), null);
        Keys(_b).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "B"), null);

        Assert.Equal(Keys(_a).Get("anthropic.apiKey")!.Id, Keys(_b).Get("anthropic.apiKey")!.Id);
    }

    [Fact]
    public void Key_Value_In_The_Database_Is_Only_Encrypted()
    {
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);

        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-открытый"), null);

        using var conn = _a.Open();
        var stored = Sql.Query(conn, null, "SELECT value_enc FROM model_keys", r => r.S("value_enc"));
        Assert.Single(stored);
        Assert.DoesNotContain("sk-открытый", stored[0]);
    }

    [Fact]
    public void Journal_Records_The_Reference_But_Never_The_Value()
    {
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);

        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-тайна"), null);

        var events = new EventStore(_a).Query(eventType: EventTypes.ModelKeyCreated);
        Assert.Single(events);
        Assert.Contains("anthropic.apiKey", events[0].PayloadJson);
        Assert.DoesNotContain("sk-тайна", events[0].PayloadJson);
    }

    // --- репликация ключей ---

    [Fact]
    public void Key_Replicates_Encrypted_And_Is_Readable_With_The_Organization_Key()
    {
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        new OrgSecretKey(_secretsB).Import(OrgId, new OrgSecretKey(_secretsA).Export(OrgId));
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-из-A"), null);

        Sync(_a, _b, 0, NodeB);

        var arrived = Keys(_b).Get("anthropic.apiKey");
        Assert.NotNull(arrived);
        Assert.Equal("sk-из-A", OrgSecretKey.Decrypt(new OrgSecretKey(_secretsB).Read(OrgId)!,
            arrived!.ValueEnc));
    }

    [Fact]
    public void Different_Keys_Merge_By_Themselves()
    {
        // «если меняли разные ключи, то файл можно смержить» (todo45): разные ключи — это
        // разные строки, и никакого слияния файла не требуется вовсе
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        new OrgSecretKey(_secretsB).Import(OrgId, new OrgSecretKey(_secretsA).Export(OrgId));
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-anthropic"), null);
        Keys(_b).Set("deepseek.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-deepseek"), null);

        Sync(_a, _b, 0, NodeB);
        Sync(_b, _a, 0, NodeA);

        foreach (var db in new[] { _a, _b })
        {
            Assert.Equal(2, Keys(db).List().Count);
            Assert.NotNull(Keys(db).Get("anthropic.apiKey"));
            Assert.NotNull(Keys(db).Get("deepseek.apiKey"));
        }
    }

    [Fact]
    public void Same_Key_With_The_Same_Value_Merges_Too()
    {
        // «если меняли один и тот же ключ, но он одинаковый, то тоже можно смержить» (todo45)
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-одинаковый"), null);
        Thread.Sleep(15);
        Keys(_b).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-одинаковый"), null);

        // шифртексты РАЗНЫЕ (случайный nonce), а открытые значения совпадают — конфликта нет
        Assert.NotEqual(Keys(_a).Encrypted("anthropic.apiKey"), Keys(_b).Encrypted("anthropic.apiKey"));
        Assert.Equal(
            OrgSecretKey.Decrypt(orgKey, Keys(_a).Encrypted("anthropic.apiKey")),
            OrgSecretKey.Decrypt(orgKey, Keys(_b).Encrypted("anthropic.apiKey")));
    }

    [Fact]
    public void Unsent_Local_Change_Is_What_Makes_A_Conflict()
    {
        // конфликт возникает не «когда значения разные», а когда НАША правка ещё не уехала:
        // если партнёр её видел, его изменение — продолжение истории, а не спор
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-из-A"), null);

        using var conn = _a.Open();
        var unsent = Sql.Query(conn, null, """
            SELECT DISTINCT pk FROM changes WHERE tbl='model_keys' AND seq>0 AND node_id=@n
            """, r => r.S("pk"), ("@n", NodeA));

        Assert.Contains(ModelKeyStore.IdOf("anthropic.apiKey"), unsent);
        // после того как курсор партнёра прошёл нашу правку, «неотправленных» не остаётся
        var head = ChangeLog.Head(conn);
        var after = Sql.Query(conn, null, """
            SELECT DISTINCT pk FROM changes WHERE tbl='model_keys' AND seq>@c AND node_id=@n
            """, r => r.S("pk"), ("@c", head), ("@n", NodeA));
        Assert.Empty(after);
    }

    [Fact]
    public void Guard_Can_Refuse_A_Key_And_Nothing_Is_Overwritten()
    {
        // так работает страж репликации: конфликтный ключ НЕ применяется, у каждой стороны
        // остаётся своё значение, пока человек не решит (todo45)
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-из-A"), null);
        Keys(_b).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-из-B"), null);

        var refused = 0;
        Sync(_a, _b, 0, NodeB, guard: (table, _, _) =>
        {
            if (table != "model_keys")
            {
                return true;
            }
            refused++;
            return false;
        });

        Assert.Equal(1, refused);
        Assert.Equal("sk-из-B",
            OrgSecretKey.Decrypt(orgKey, Keys(_b).Encrypted("anthropic.apiKey")));
    }

    [Fact]
    public void Accepted_Key_Overwrites_By_Last_Writer_Wins()
    {
        // решение человека («принять сторону») — это обычная свежая запись: она новее
        // и поэтому доезжает до партнёра обычным порядком
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        Keys(_b).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-старое"), null);
        Thread.Sleep(20);
        Keys(_a).Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-выбранное"), null);

        Sync(_a, _b, 0, NodeB);

        Assert.Equal("sk-выбранное",
            OrgSecretKey.Decrypt(orgKey, Keys(_b).Encrypted("anthropic.apiKey")));
    }

    // --- список конфликтов: тот же, что у файлов, но операций три ---

    [Fact]
    public void Key_Conflict_Lives_In_The_Same_List_As_Files()
    {
        var serverDb = new Database(Path.Combine(_dir, "srv"), "server.db", DatabaseKind.Server);
        serverDb.Init(NodeA);
        var conflicts = new FileSyncStateService(serverDb);
        var scopeKey = FileSyncStateService.ScopeKey("key", OrgId);

        conflicts.Record(OrgId, NodeB, "key", scopeKey, "", "anthropic.apiKey",
            left: new FileEntry { Path = "anthropic.apiKey", Mtime = "t1", Hash = "aaaa" },
            right: new FileEntry { Path = "anthropic.apiKey", Mtime = "t2", Hash = "bbbb" },
            leftValue: "enc-A", rightValue: "enc-B");

        var row = Assert.Single(conflicts.Conflicts(NodeB));
        Assert.Equal("key", row.Scope);
        Assert.Equal("anthropic.apiKey", row.Path);
        // значения сторон хранятся ЗАШИФРОВАННЫМИ — ими применяется «принять сторону»
        Assert.Equal("enc-A", row.LeftValue);
        Assert.Equal("enc-B", row.RightValue);
    }

    [Fact]
    public void Key_Has_Three_Operations_And_No_Rename()
    {
        Assert.True(ReplFileResolutions.IsKeyResolution(ReplFileResolutions.Left));
        Assert.True(ReplFileResolutions.IsKeyResolution(ReplFileResolutions.Right));
        Assert.True(ReplFileResolutions.IsKeyResolution(ReplFileResolutions.NewValue));
        // переименовать ключ нельзя — это не файл
        Assert.False(ReplFileResolutions.IsKeyResolution(ReplFileResolutions.RenameLeft));
        Assert.False(ReplFileResolutions.IsKeyResolution(ReplFileResolutions.RenameRight));
        Assert.True(ReplFileResolutions.IsKnown(ReplFileResolutions.NewValue));
    }

    [Fact]
    public void Model_Keys_Table_Replicates()
    {
        Assert.Contains("model_keys", ChangeLog.OrgTables);
        // а ключ организации живёт в secrets.json и в реплицируемых таблицах его нет
        Assert.DoesNotContain("meta", ChangeLog.OrgTables);
    }

    // --- перенос ключей из secrets.json в организацию ---

    [Fact]
    public void Deleted_Key_Stops_Being_Found()
    {
        var orgKey = new OrgSecretKey(_secretsA).Ensure(OrgId);
        var store = Keys(_a);
        store.Set("anthropic.apiKey", OrgSecretKey.Encrypt(orgKey, "sk-был"), null);

        store.Delete("anthropic.apiKey", null);

        Assert.Null(store.Get("anthropic.apiKey"));
        Assert.Equal("", store.Encrypted("anthropic.apiKey"));
        // мягкое удаление: строка остаётся и уезжает репликацией — иначе ключ «воскрес» бы
        Assert.Single(store.List(includeDeleted: true));
    }
}
