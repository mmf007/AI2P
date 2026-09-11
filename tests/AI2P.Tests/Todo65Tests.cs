using System.Net;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-123 (версия 1.65): КЛЮЧ И ТОКЕН ИМПОРТА ВВОДЯТСЯ В ИНТЕРФЕЙСЕ.
///
/// Раньше ключ API и токен Trello вписывались руками в <c>secrets.json</c>, а форма источника
/// спрашивала лишь ССЫЛКИ на них. Теперь значения вводятся двумя кнопками — каждая просит
/// ровно одно поле — и ложатся туда же, куда ключи API моделей: в БД организации,
/// зашифрованными её ключом (ТЗ гл. 10). Наружу значение не отдаётся никогда.
///
/// Правило активности повторяет правило моделей: источник импорта БЕЗ ключа и токена активным
/// быть не может; как только введено последнее из двух значений — он активируется сам,
/// а дальше активность переключает человек.
/// </summary>
public sealed class Todo65Tests : IDisposable
{
    private readonly StorageFixture _f = new();
    private readonly ImportKeyService _keys;

    public Todo65Tests()
    {
        var modelKeys = new ModelKeyService(_f.Models, _f.Files, _f.Secrets, _f.KeyStore,
            _f.OrgKeys, StorageFixture.OrgId);
        _keys = new ImportKeyService(_f.Imports, modelKeys);
        // в приложении связь ставит контекст организации (OrgContext)
        _f.Imports.ActivationGuard = _keys.ActivationError;
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Источник справочника с заданными ссылками на ключ и токен.</summary>
    private ImportSource NewSource(string name = "Trello основной", bool active = false)
    {
        var source = new ImportSource { Name = name, Kind = "trello", IsActive = active };
        var (keyRef, tokenRef) = ImportSource.DefaultRefs(source.Kind, source.Id);
        source.ParamsJson = JsonSerializer.Serialize(new
        {
            login = "mmffr",
            filter = "",
            keyRef,
            tokenRef,
        });
        return source;
    }

    // ---------- 1. ссылки на секреты у каждого источника свои ----------

    [Fact]
    public void Default_refs_are_own_for_every_source()
    {
        var first = new ImportSource { Kind = "trello" };
        var second = new ImportSource { Kind = "trello" };
        var (key1, token1) = ImportSource.DefaultRefs(first.Kind, first.Id);
        var (key2, token2) = ImportSource.DefaultRefs(second.Kind, second.Id);

        Assert.StartsWith("trello.", key1);
        Assert.EndsWith(".apiKey", key1);
        Assert.EndsWith(".token", token1);
        Assert.NotEqual(key1, key2);     // два аккаунта Trello не делят один ключ
        Assert.NotEqual(token1, token2);
        // повторный вызов даёт то же самое — ссылка выводится из идентификатора, а не случайна
        Assert.Equal(key1, ImportSource.DefaultRefs(first.Kind, first.Id).KeyRef);
    }

    [Fact]
    public void Refs_of_old_sources_are_kept_as_they_were()
    {
        // источник, заведённый до v1.65: ссылок в params_json нет — работают прежние умолчания
        var source = _f.Imports.Create(new ImportSource
        {
            Name = "Старый источник",
            IsActive = false,
            ParamsJson = """{"login":"mmffr","filter":""}""",
        }, null);
        var (keyRef, tokenRef) = ImportKeyService.RefsOf(source);
        Assert.Equal("trello.apiKey", keyRef);
        Assert.Equal("trello.token", tokenRef);
    }

    // ---------- 2. активность источника ----------

    [Fact]
    public void Source_without_key_and_token_cannot_be_active()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _f.Imports.Create(NewSource(active: true), null));
        Assert.Contains("ключ API и токен", error.Message);

        // тот же источник неактивным заводится спокойно
        var source = _f.Imports.Create(NewSource(), null);
        Assert.False(source.IsActive);
    }

    [Fact]
    public void Last_entered_value_activates_the_source()
    {
        var source = _f.Imports.Create(NewSource(), null);

        // введён только ключ — активировать всё ещё нельзя
        var afterKey = _keys.SetValue(source.Id, ImportKeyService.FieldKey, "trello-api-key", null);
        Assert.True(afterKey.HasKey);
        Assert.False(afterKey.HasToken);
        Assert.False(afterKey.IsActive);
        Assert.False(_f.Imports.Get(source.Id)!.IsActive);
        var error = Assert.Throws<ArgumentException>(() =>
        {
            var attempt = _f.Imports.Get(source.Id)!;
            attempt.IsActive = true;
            _f.Imports.Update(attempt, null);
        });
        Assert.Contains("токен", error.Message);

        // введён токен — последнее из двух значений включает источник САМО
        var afterToken = _keys.SetValue(source.Id, ImportKeyService.FieldToken, "trello-token", null);
        Assert.True(afterToken is { HasKey: true, HasToken: true, IsActive: true });
        Assert.True(_f.Imports.Get(source.Id)!.IsActive);
    }

    [Fact]
    public void User_can_switch_activity_off_and_on_afterwards()
    {
        var source = _f.Imports.Create(NewSource(), null);
        _keys.SetValue(source.Id, ImportKeyService.FieldKey, "k", null);
        _keys.SetValue(source.Id, ImportKeyService.FieldToken, "t", null);

        var stored = _f.Imports.Get(source.Id)!;
        stored.IsActive = false;
        _f.Imports.Update(stored, null);
        Assert.False(_f.Imports.Get(source.Id)!.IsActive);

        stored.IsActive = true;
        _f.Imports.Update(stored, null);
        Assert.True(_f.Imports.Get(source.Id)!.IsActive);

        // повторный ввод ключа у уже активного источника активность не трогает
        var status = _keys.SetValue(source.Id, ImportKeyService.FieldKey, "k2", null);
        Assert.True(status.IsActive);
    }

    // ---------- 3. значение наружу не отдаётся и лежит зашифрованным ----------

    [Fact]
    public void Value_is_encrypted_in_org_database_and_never_returned()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var (keyRef, _) = ImportKeyService.RefsOf(source);
        _keys.SetValue(source.Id, ImportKeyService.FieldKey, "секретное-значение", null);

        var stored = _f.KeyStore.Encrypted(keyRef);
        Assert.NotEmpty(stored);
        Assert.DoesNotContain("секретное-значение", stored);

        var status = _keys.Status(source.Id);
        Assert.True(status.HasKey);
        Assert.Equal("организация", status.KeySource);
        // в DTO значения нет ни одним полем
        Assert.DoesNotContain("секретное-значение", JsonSerializer.Serialize(status));
    }

    [Fact]
    public void Empty_value_and_unknown_field_are_rejected()
    {
        var source = _f.Imports.Create(NewSource(), null);
        Assert.Throws<ArgumentException>(() =>
            _keys.SetValue(source.Id, ImportKeyService.FieldKey, "   ", null));
        Assert.Throws<ArgumentException>(() =>
            _keys.SetValue(source.Id, "пароль", "значение", null));
        Assert.Throws<ArgumentException>(() =>
            _keys.SetValue("нет-такого", ImportKeyService.FieldKey, "значение", null));
    }

    // ---------- 4. переезд из secrets.json ----------

    [Fact]
    public void Values_written_into_secrets_json_move_to_the_organization()
    {
        // установка, жившая до v1.65: ключи вписаны в файл, ссылки — прежние умолчания
        _f.Secrets.Write("trello.apiKey", "ключ-из-файла");
        _f.Secrets.Write("trello.token", "токен-из-файла");
        var source = _f.Imports.Create(new ImportSource
        {
            Name = "Из файла",
            IsActive = false,
            ParamsJson = """{"login":"mmffr"}""",
        }, null);

        // до переезда значения читаются, но лежат в файле
        var before = _keys.Status(source.Id);
        Assert.True(before is { HasKey: true, HasToken: true });
        // с T-228 запись ключа кладёт его в свой json подкаталога secrets/, а не в общий файл
        Assert.StartsWith(SecretStore.DirName + "/", before.KeySource);

        Assert.Equal(2, _keys.MigrateSecretsToOrg());
        var after = _keys.Status(source.Id);
        Assert.Equal("организация", after.KeySource);
        Assert.Equal("организация", after.TokenSource);
        Assert.DoesNotContain("ключ-из-файла", _f.KeyStore.Encrypted("trello.apiKey"));
        // повторный вызов ничего не двигает — перенос идемпотентен
        Assert.Equal(0, _keys.MigrateSecretsToOrg());
    }

    // ---------- 5. импортёр берёт ключ у организации, а не из файла ----------

    [Fact]
    public async Task Importer_reads_secrets_through_the_resolver()
    {
        _f.Secrets.Write("trello.apiKey", "ключ-из-файла");
        _f.Secrets.Write("trello.token", "токен-из-файла");
        var source = _f.Imports.Create(new ImportSource
        {
            Name = "Trello",
            IsActive = false,
            ParamsJson = """{"login":"mmffr"}""",
        }, null);
        var project = _f.Projects.Create("Проект", null, null, null);

        // резолвер организации подставлен и значения не знает — файл в обход НЕ читается
        var importer = new TrelloImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files)
        {
            KeyResolver = _ => (null, "ключ организации не получен этим сервером"),
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportAsync(project, source.Id, true, false, null));
        Assert.Contains("trello.apiKey", error.Message);
        Assert.Contains("форме источника импорта", error.Message);
    }

    // ---------- 6. маршрут API (граница HTTP тестами иначе не покрыта) ----------

    private sealed class SpyHandler(Action<HttpRequestMessage> spy) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // тело читаем до ответа: после Dispose запроса содержимое недоступно
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync();
            }
            spy(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ImportKeyStatusDto
                {
                    HasKey = true,
                    HasToken = true,
                    IsActive = true,
                }), System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task ApiClient_puts_the_value_to_the_field_route()
    {
        HttpRequestMessage? seen = null;
        var client = new ApiClient(new HttpClient(new SpyHandler(r => seen = r))
        {
            BaseAddress = new Uri("http://localhost:5480/ai2p/mmfgrp/"),
        });

        var status = await client.SetImportKeyAsync("src-1", ImportKeyService.FieldToken, "значение");
        Assert.True(status is { HasKey: true, HasToken: true, IsActive: true });
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Put, seen!.Method);
        Assert.Equal("/ai2p/mmfgrp/api/imports/src-1/keys/token", seen.RequestUri!.AbsolutePath);
        // тело — сам вводимый секрет (кириллица уезжает \u-эскейпами, поэтому разбираем)
        var sent = JsonSerializer.Deserialize<ImportKeySaveDto>(
            await seen.Content!.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("значение", sent!.Value);
    }
}
