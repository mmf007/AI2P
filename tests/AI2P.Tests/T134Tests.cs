using System.Net;
using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-134: ОШИБКА ПРИ ИМПОРТЕ ВИДНА И ПОНЯТНА.
///
/// Импорт карточки не проходил, а в логе оставалась одна строка — сам запрос к Trello;
/// отказ не логировался вовсе, а человеку доставался хинт «Trello ответил 401: invalid key».
/// По нему нельзя понять ни какой ключ ушёл в запрос, ни откуда он взят, ни что «invalid key»
/// означает именно ключ API, а не токен.
///
/// Теперь: отказ Trello пишется в лог, наружу уходит объяснение (что не принято, по какой
/// ссылке и в каком хранилище лежит значение, какой оно формы и где взять правильное),
/// перепутанные местами ключ и токен распознаются по форме значения, а в форме источника
/// есть кнопка «Проверить подключение» — вердикт виден там же, где значения вводятся.
/// </summary>
public sealed class T134Tests : IDisposable
{
    private const string RealKey = "0123456789abcdef0123456789abcdef";              // 32 знака
    private const string RealToken = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly StorageFixture _f = new();
    private readonly ImportKeyService _keys;

    public T134Tests()
    {
        var modelKeys = new ModelKeyService(_f.Models, _f.Files, _f.Secrets, _f.KeyStore,
            _f.OrgKeys, StorageFixture.OrgId);
        _keys = new ImportKeyService(_f.Imports, modelKeys);
        _f.Imports.ActivationGuard = _keys.ActivationError;
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Ответ вместо живого Trello: тела по порядку обращений.</summary>
    private sealed class FakeTrello(params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            var (status, body) = responses[Math.Min(_next++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private ImportSource NewSource(string login = "mmffr", bool active = false)
    {
        var source = new ImportSource { Name = "Trello", Kind = "trello", IsActive = active };
        var (keyRef, tokenRef) = ImportSource.DefaultRefs(source.Kind, source.Id);
        source.ParamsJson = JsonSerializer.Serialize(new { login, filter = "", keyRef, tokenRef });
        return source;
    }

    private TrelloImporter Importer(FakeTrello handler, string key, string token) =>
        new(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files)
        {
            HttpOverride = new HttpClient(handler),
            KeyResolver = secretRef => secretRef.EndsWith(".token", StringComparison.Ordinal)
                ? (token, "организация")
                : (key, "организация"),
        };

    // ---------- 1. форма значения: ключ и токен различимы ----------

    [Fact]
    public void Key_and_token_are_told_apart_by_their_shape()
    {
        Assert.True(TrelloImporter.LooksLikeKey(RealKey));
        Assert.False(TrelloImporter.LooksLikeKey(RealToken));
        Assert.True(TrelloImporter.LooksLikeToken(RealToken));
        Assert.True(TrelloImporter.LooksLikeToken("ATTA" + new string('x', 60)));
        Assert.False(TrelloImporter.LooksLikeToken(RealKey));

        // правильные значения замечаний не вызывают
        Assert.Equal("", TrelloImporter.SecretHint("key", RealKey));
        Assert.Equal("", TrelloImporter.SecretHint("token", RealToken));
        // пустое значение — не тема этой проверки (о нём говорит «не установлен»)
        Assert.Equal("", TrelloImporter.SecretHint("key", "   "));
    }

    [Fact]
    public void Swapped_key_and_token_are_named_as_swapped()
    {
        var keyField = TrelloImporter.SecretHint("key", RealToken);
        Assert.Contains("ТОКЕН", keyField);
        Assert.Contains("trello.com/power-ups/admin", keyField);

        var tokenField = TrelloImporter.SecretHint("token", RealKey);
        Assert.Contains("КЛЮЧ API", tokenField);

        // непохожее ни на что значение — тоже замечание, но без обвинения в путанице
        var strange = TrelloImporter.SecretHint("key", "мой-ключ");
        Assert.Contains("не похоже", strange);
        Assert.DoesNotContain("ТОКЕН", strange);
    }

    [Fact]
    public void Value_itself_never_leaks_into_shape_or_hint()
    {
        const string secret = "0123456789abcdef0123456789abcdeSECRET";
        Assert.DoesNotContain(secret, TrelloImporter.Shape(secret));
        Assert.DoesNotContain(secret, TrelloImporter.SecretHint("key", secret));
        Assert.Contains("37 знаков", TrelloImporter.Shape(secret));
    }

    // ---------- 2. отказ Trello объяснён словами ----------

    [Fact]
    public void Invalid_key_answer_names_the_key_its_ref_and_where_it_lies()
    {
        var auth = new TrelloImporter.Auth(RealToken, RealToken, "trello.x.apiKey", "trello.x.token",
            "организация", "secrets.json");
        var message = TrelloImporter.Explain(
            new TrelloImporter.TrelloApiException(401, "invalid key"), auth);

        Assert.Contains("КЛЮЧ API", message);
        Assert.Contains("trello.x.apiKey", message);
        Assert.Contains("организация", message);
        Assert.Contains("ТОКЕН", message);              // ключ и токен перепутаны — сказано прямо
        Assert.DoesNotContain(RealToken, message);      // само значение наружу не уходит
    }

    [Fact]
    public void Invalid_token_and_access_denied_are_different_answers()
    {
        var auth = new TrelloImporter.Auth(RealKey, RealToken, "trello.x.apiKey", "trello.x.token",
            "организация", "организация");

        var token = TrelloImporter.Explain(
            new TrelloImporter.TrelloApiException(401, "invalid token"), auth);
        Assert.Contains("ТОКЕН", token);
        Assert.Contains("trello.x.token", token);

        var denied = TrelloImporter.Explain(
            new TrelloImporter.TrelloApiException(401, "unauthorized card permission requested"), auth);
        Assert.Contains("Ключ и токен приняты", denied);

        var missing = TrelloImporter.Explain(
            new TrelloImporter.TrelloApiException(404, "The requested resource was not found."), auth);
        Assert.Contains("не нашёл", missing);
    }

    [Fact]
    public async Task Card_import_answers_with_the_explanation_not_with_the_raw_body()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var project = _f.Projects.Create("Проект", null, null, null);
        var handler = new FakeTrello((HttpStatusCode.Unauthorized, "invalid key"));
        var importer = Importer(handler, RealToken, RealToken);   // в поле ключа лежит токен

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportCardContentAsync(project, "https://trello.com/c/tYY9LhmR", source.Id));

        Assert.Contains("не принял КЛЮЧ API", error.Message);
        Assert.Contains("Проверить подключение", error.Message);
        Assert.DoesNotContain(RealToken, error.Message);
        // запрос всё-таки ушёл — и ключ в нём был (иначе Trello ответил бы иначе)
        Assert.Contains("key=", handler.Urls[0]);
    }

    [Fact]
    public async Task Import_by_source_explains_the_refusal_too()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var project = _f.Projects.Create("Проект", null, null, null);
        var importer = Importer(new FakeTrello((HttpStatusCode.Unauthorized, "invalid key")),
            RealToken, RealToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportAsync(project, source.Id, true, false, null));
        Assert.Contains("не принял КЛЮЧ API", error.Message);
    }

    [Fact]
    public async Task Spaces_around_the_value_do_not_reach_trello()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var handler = new FakeTrello((HttpStatusCode.OK, """{"username":"mmffr"}"""));
        // значение из secrets.json или переменной окружения приходит как записано — с переводом строки
        var importer = Importer(handler, $" {RealKey}\r\n", $"\t{RealToken} ");

        var (ok, _) = await importer.CheckAsync(source.Id);
        Assert.True(ok);
        Assert.Contains($"key={RealKey}&token={RealToken}", handler.Urls[0]);
    }

    // ---------- 3. проверка подключения ----------

    [Fact]
    public async Task Check_tells_whose_account_the_token_belongs_to()
    {
        var source = _f.Imports.Create(NewSource(login: "mmffr"), null);
        var importer = Importer(new FakeTrello((HttpStatusCode.OK,
            """{"id":"1","username":"mmffr","fullName":"Mike"}""")), RealKey, RealToken);

        var (ok, message) = await importer.CheckAsync(source.Id);
        Assert.True(ok);
        Assert.Contains("@mmffr", message);
    }

    [Fact]
    public async Task Check_warns_when_the_token_belongs_to_another_account()
    {
        var source = _f.Imports.Create(NewSource(login: "другой-логин"), null);
        var importer = Importer(new FakeTrello((HttpStatusCode.OK,
            """{"username":"mmffr"}""")), RealKey, RealToken);

        var (ok, message) = await importer.CheckAsync(source.Id);
        Assert.True(ok);
        Assert.Contains("другой-логин", message);
    }

    [Fact]
    public async Task Check_does_not_throw_on_refusal_and_explains_it()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var importer = Importer(new FakeTrello((HttpStatusCode.Unauthorized, "invalid key")),
            RealToken, RealToken);

        var (ok, message) = await importer.CheckAsync(source.Id);
        Assert.False(ok);
        Assert.Contains("не принял КЛЮЧ API", message);
    }

    [Fact]
    public async Task Check_without_values_says_what_to_do()
    {
        var source = _f.Imports.Create(NewSource(), null);
        var importer = new TrelloImporter(_f.Tasks, _f.Imports, _f.Secrets, _f.Events, _f.Files)
        {
            HttpOverride = new HttpClient(new FakeTrello((HttpStatusCode.OK, "{}"))),
            KeyResolver = _ => (null, "организация"),
        };

        var (ok, message) = await importer.CheckAsync(source.Id);
        Assert.False(ok);
        Assert.Contains("форме источника импорта", message);
    }

    // ---------- 4. замечание видно в форме источника ----------

    [Fact]
    public void Status_carries_the_hint_for_a_value_of_the_wrong_shape()
    {
        var source = _f.Imports.Create(NewSource(), null);
        _keys.SetValue(source.Id, ImportKeyService.FieldKey, RealToken, null);   // токен в поле ключа
        _keys.SetValue(source.Id, ImportKeyService.FieldToken, RealToken, null);

        var status = _keys.Status(source.Id);
        Assert.True(status is { HasKey: true, HasToken: true });
        Assert.Contains("ТОКЕН", status.KeyHint);
        Assert.Equal("", status.TokenHint);
        Assert.DoesNotContain(RealToken, JsonSerializer.Serialize(status));

        // правильный ключ снимает замечание
        _keys.SetValue(source.Id, ImportKeyService.FieldKey, RealKey, null);
        Assert.Equal("", _keys.Status(source.Id).KeyHint);
    }

    // ---------- 5. маршрут проверки (граница HTTP) ----------

    private sealed class RouteSpy(Action<HttpRequestMessage> spy) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            spy(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new ImportCheckDto { Ok = true, Message = "готово" }),
                    Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task ApiClient_asks_the_check_route()
    {
        HttpRequestMessage? seen = null;
        var client = new ApiClient(new HttpClient(new RouteSpy(r => seen = r))
        {
            BaseAddress = new Uri("http://localhost:5480/ai2p/mmfgrp/"),
        });

        var result = await client.CheckImportAsync("src-1");
        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Get, seen!.Method);
        Assert.Equal("/ai2p/mmfgrp/api/imports/src-1/check", seen.RequestUri!.AbsolutePath);
    }
}
