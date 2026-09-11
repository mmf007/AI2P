using AI2P.Connectors;
using AI2P.Server.Api;
using System.Text.Json;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-234: ПОДКАТАЛОГ КЛЮЧЕЙ ВИДЕН НА ДИСКЕ.
///
/// В T-228 ключи моделей и источников импорта переехали в подкаталог <c>secrets/</c> рядом
/// с config.json — по одному json на ссылку. Но заводился он ЛЕНИВО, в момент первой записи
/// файлового ключа, а ключ, введённый в форме, принадлежит организации и файла не создаёт
/// (<see cref="ModelKeyService"/>). На обычной установке каталога поэтому не было вовсе:
/// человеку негде было увидеть ни его самого, ни куда класть ключи руками.
///
/// Теперь: каталог заводится ПРИ СТАРТЕ вместе с пояснением <c>readme.txt</c>, путь файла
/// ключа показывается в форме модели (<c>ModelKeyStatusDto.KeyFile</c>), а ввод ключа в форме
/// обновляет уже существующий файл — новый по-прежнему не заводит.
/// </summary>
public sealed class T234Tests : IDisposable
{
    private const string FableId = "6f1a45e0-0d31-4c65-9a01-000000000001";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ai2p-t234-" + Guid.NewGuid().ToString("N"));

    public T234Tests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "secrets.json");

    private SecretStore Store() => new(FilePath);

    // ---------- 1. каталог заводится при старте ----------

    [Fact]
    public void The_Directory_Appears_At_Startup_Even_Without_A_Single_Key()
    {
        var store = Store();

        Assert.True(store.EnsureDir());

        Assert.True(Directory.Exists(store.Dir));
        Assert.Equal(Path.Combine(_dir, SecretStore.DirName), store.Dir);
        // ключей в нём нет — только пояснение
        Assert.Empty(Directory.GetFiles(store.Dir, "*.json"));
        var readme = Path.Combine(store.Dir, SecretStore.ReadmeName);
        Assert.True(File.Exists(readme));
        var text = File.ReadAllText(readme);
        // пояснение отвечает на три вопроса: что за файлы, куда девается ключ из формы,
        // в каком порядке ключ ищется
        Assert.Contains("secretRef", text);
        Assert.Contains(FilePath, text);                  // путь ЭТОЙ установки, а не общий текст
        Assert.Contains("ANTHROPIC_API_KEY", text);
        Assert.DoesNotContain("{0}", text);               // подстановки раскрыты
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public void A_Second_Start_Neither_Recreates_The_Directory_Nor_Rewrites_The_Readme()
    {
        var store = Store();
        store.EnsureDir();
        var readme = Path.Combine(store.Dir, SecretStore.ReadmeName);
        // человек дописал в пояснение своё
        File.WriteAllText(readme, "мои заметки про ключи");

        Assert.False(store.EnsureDir());                   // каталог уже был
        Assert.False(new SecretStore(FilePath).EnsureDir());

        Assert.Equal("мои заметки про ключи", File.ReadAllText(readme));
    }

    /// <summary>Пояснение ключом не является: ключи читаются только из .json по имени ссылки.</summary>
    [Fact]
    public void The_Readme_Is_Not_A_Key_And_Does_Not_Disturb_Reading()
    {
        var store = Store();
        store.EnsureDir();

        Assert.Null(store.Read("anthropic.apiKey"));
        Assert.False(store.Has("anthropic.apiKey"));

        store.Write("anthropic.apiKey", "sk-ant-1");

        Assert.Equal("sk-ant-1", store.Read("anthropic.apiKey"));
        Assert.Single(Directory.GetFiles(store.Dir, "*.json"));
        Assert.True(File.Exists(Path.Combine(store.Dir, SecretStore.ReadmeName)));
        // и перенос старого файла на пояснение не смотрит
        File.WriteAllText(FilePath, """{ "deepseek": { "apiKey": "sk-deep" } }""");
        Assert.Equal(1, store.MigrateFileToDir());
        Assert.Equal(2, Directory.GetFiles(store.Dir, "*.json").Length);
    }

    /// <summary>Каталог заводит САМ СТАРТ приложения, а не первая запись ключа.</summary>
    [Fact]
    public void The_Startup_Code_Creates_The_Directory_Before_Moving_The_Old_File()
    {
        var program = Source("src/AI2P.Server/Program.cs");

        var ensure = program.IndexOf("secrets.EnsureDir()", StringComparison.Ordinal);
        var migrate = program.IndexOf("secrets.MigrateFileToDir()", StringComparison.Ordinal);
        Assert.True(ensure > 0, "старт не заводит подкаталог ключей");
        Assert.True(migrate > ensure, "перенос старого файла идёт раньше, чем заводится каталог");
    }

    // ---------- 2. ключ из формы и файл на диске ----------

    [Fact]
    public void Updating_A_Key_Never_Creates_A_File_On_Its_Own()
    {
        var store = Store();
        store.EnsureDir();

        Assert.False(store.UpdateIfPresent("anthropic.apiKey", "sk-новый"));

        Assert.False(File.Exists(store.PathOf("anthropic.apiKey")));
        Assert.Empty(Directory.GetFiles(store.Dir, "*.json"));
    }

    [Fact]
    public void An_Existing_Key_File_Follows_The_New_Value()
    {
        var store = Store();
        store.Write("anthropic.apiKey", "sk-старый");

        Assert.True(store.UpdateIfPresent("anthropic.apiKey", "sk-новый"));

        Assert.Equal("sk-новый", store.Read("anthropic.apiKey"));
        using var doc = JsonDocument.Parse(File.ReadAllText(store.PathOf("anthropic.apiKey")));
        // ссылка в файле осталась исходной, значение — новое
        Assert.Equal("anthropic.apiKey", doc.RootElement.GetProperty("ref").GetString());
        Assert.Equal("sk-новый", doc.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void Server_Secrets_Are_Never_Written_Into_The_Directory()
    {
        var store = Store();
        store.Write(Ai2pAuth.AdminHashRef, "хэш");

        Assert.False(store.UpdateIfPresent(Ai2pAuth.AdminHashRef, "другой хэш"));
        Assert.False(store.UpdateIfPresent(OrgSecretKey.RefOf("ORG-1"), "ключ"));
        Assert.False(store.UpdateIfPresent("   ", "значение"));

        Assert.Equal("хэш", store.Read(Ai2pAuth.AdminHashRef));
    }

    /// <summary>
    /// Ключ, введённый В ФОРМЕ, по-прежнему уходит в БД организации зашифрованным и файла
    /// не заводит — но существующий файл едет следом, иначе в нём молча остаётся прежнее
    /// значение и им работает сервер, которому ключ организации ещё не выдан.
    /// </summary>
    [Fact]
    public void A_Key_From_The_Form_Goes_To_The_Organization_And_Updates_An_Existing_File()
    {
        using var f = new StorageFixture();
        f.Models.Seed();
        f.OrgKeys.Ensure(StorageFixture.OrgId);
        var keys = new ModelKeyService(f.Models, f.Files, f.Secrets, f.KeyStore,
            f.OrgKeys, StorageFixture.OrgId);
        f.Secrets.EnsureDir();

        keys.SetKey(FableId, "sk-из-формы", null);

        // файла нет: ключ принадлежит организации и реплицируется
        Assert.False(File.Exists(f.Secrets.PathOf("anthropic.apiKey")));
        Assert.Empty(Directory.GetFiles(f.Secrets.Dir, "*.json"));
        Assert.Equal("sk-из-формы", keys.Resolve("anthropic.apiKey"));

        // а вот если файл на этом компьютере уже есть — он обновляется вслед за организацией
        f.Secrets.Write("anthropic.apiKey", "sk-из-файла");
        keys.SetKey(FableId, "sk-второй", null);

        Assert.Equal("sk-второй", f.Secrets.Read("anthropic.apiKey"));
        Assert.Equal("sk-второй", keys.Resolve("anthropic.apiKey"));
        // значение в БД организации лежит только зашифрованным
        Assert.DoesNotContain("sk-второй", f.KeyStore.Encrypted("anthropic.apiKey"));
    }

    // ---------- 3. путь файла виден в форме ----------

    [Fact]
    public void The_Form_Is_Told_Where_The_Key_File_Is_Even_When_There_Is_No_Key()
    {
        using var f = new StorageFixture();
        f.Models.Seed();
        var keys = new ModelKeyService(f.Models, f.Files, f.Secrets, f.KeyStore,
            f.OrgKeys, StorageFixture.OrgId);

        var status = keys.Status(FableId);

        Assert.True(status.Required);
        Assert.False(status.HasKey);
        // путь показывается и без ключа: это ответ на вопрос «куда положить ключ руками»
        Assert.Equal(f.Secrets.PathOf("anthropic.apiKey"), status.KeyFile);
        Assert.Contains(SecretStore.DirName, status.KeyFile);
        Assert.EndsWith(".json", status.KeyFile);
        // значение ключа наружу по-прежнему не отдаётся
        f.OrgKeys.Ensure(StorageFixture.OrgId);
        keys.SetKey(FableId, "sk-секрет", null);
        Assert.DoesNotContain("sk-секрет", JsonSerializer.Serialize(keys.Status(FableId)));
    }

    /// <summary>Формы показывают путь файла: строка есть и в карточке модели, и в окне ключа.</summary>
    [Fact]
    public void Both_Model_Forms_Show_The_Key_File_Path()
    {
        foreach (var component in new[]
                 {
                     "src/AI2P.UI/Components/AiModelDialog.razor",
                     "src/AI2P.UI/Components/ModelKeyDialog.razor",
                 })
        {
            var text = Source(component);
            Assert.Contains("models.keyFile", text);
            Assert.Contains("data-model-key-file", text);
        }
    }

    /// <summary>Исходник проекта: каталог AI2P_app находится по AI2P.sln.</summary>
    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return File.ReadAllText(Path.Combine(dir.FullName,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }
}
