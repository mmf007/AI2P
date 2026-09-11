using System.Net;
using System.Text;
using AI2P.Core.Api;
using AI2P.UI.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-125 (версия 1.64): ПУСТОЙ ОТВЕТ API БОЛЬШЕ НЕ РОНЯЕТ ИНТЕРФЕЙС.
///
/// Жалоба: по кнопке «запустить» задача не запускалась, а circuit Blazor падал с
/// <c>JsonException: The input does not contain any JSON tokens</c>
/// (<c>TaskCardView.StartAsync</c> → <c>ApiClient.Get</c>). Причина: запрос остатка лимита
/// исполнителя (<c>api/tasks/{id}/limit</c>, T-121) отвечал <c>Results.Ok(null)</c> — это
/// 200 с ПУСТЫМ телом, — а клиент разбирал тело как JSON. Лимиты у исполнителя не указаны
/// (обычный случай), значит падало на КАЖДОМ запуске.
///
/// Исправление двустороннее: 1) сервер отвечает 204 (см. <c>ApiEndpoints</c>);
/// 2) клиент читает такой ответ как «ничего нет», а любое неразбираемое тело превращает
/// в <see cref="ApiException"/> — она ловится вызывающим кодом и уходит в снэкбар,
/// тогда как <c>JsonException</c> не ловил никто.
/// </summary>
public sealed class Todo64Tests
{
    /// <summary>Заглушка HTTP: отдаёт заданный ответ на любой запрос.</summary>
    private sealed class StubHandler(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(reply());
    }

    private static ApiClient ClientOf(Func<HttpResponseMessage> reply) =>
        new(new HttpClient(new StubHandler(reply)) { BaseAddress = new Uri("http://localhost:5480/") });

    private static HttpResponseMessage Reply(HttpStatusCode code, string body, bool json = true)
    {
        var response = new HttpResponseMessage(code);
        if (body.Length > 0 || code != HttpStatusCode.NoContent)
        {
            response.Content = new StringContent(body, Encoding.UTF8,
                json ? "application/json" : "text/plain");
        }
        return response;
    }

    // ---------- 1. остаток лимита: «лимитов нет» — это не ошибка ----------

    [Fact]
    public async Task Limit_NoContent_Is_Null_Not_Crash()
    {
        var api = ClientOf(() => Reply(HttpStatusCode.NoContent, ""));

        Assert.Null(await api.GetTaskLimitAsync("T-1"));
    }

    /// <summary>Так отвечал сервер до исправления — 200 с пустым телом; на этом падало.</summary>
    [Fact]
    public async Task Limit_Empty_Body_Is_Null_Not_Crash()
    {
        var api = ClientOf(() => Reply(HttpStatusCode.OK, ""));

        Assert.Null(await api.GetTaskLimitAsync("T-1"));
    }

    [Fact]
    public async Task Limit_Json_Is_Read()
    {
        var api = ClientOf(() => Reply(HttpStatusCode.OK,
            """{"executorId":"E-1","nick":"клод","limit":1000,"used":900,"remaining":100,"isLow":true}"""));

        var limit = await api.GetTaskLimitAsync("T-1");

        Assert.NotNull(limit);
        Assert.Equal("клод", limit.Nick);
        Assert.True(limit.IsLow);
        Assert.Equal(100, limit.Remaining);
    }

    // ---------- 2. общий случай: неразбираемое тело — ошибка API, а не крах ----------

    [Fact]
    public async Task Empty_Body_Where_Object_Expected_Is_ApiException()
    {
        var api = ClientOf(() => Reply(HttpStatusCode.OK, ""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
        Assert.Contains("Пустой ответ API", ex.Message);
    }

    [Fact]
    public async Task Not_Json_Body_Is_ApiException()
    {
        // страница ошибки прокси, обрыв ответа и прочий не-JSON: снэкбар, а не падение circuit
        var api = ClientOf(() => Reply(HttpStatusCode.OK, "<html>прокси не в духе</html>", json: false));

        await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
    }

    [Fact]
    public async Task Error_Status_Still_Reports_ProblemDetails_Text()
    {
        // поведение ошибок не изменилось: текст detail из ProblemDetails доезжает до снэкбара
        var api = ClientOf(() => Reply(HttpStatusCode.BadRequest,
            """{"detail":"У задачи должен быть назначен исполнитель"}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.StartTaskAsync("T-1"));
        Assert.Equal("У задачи должен быть назначен исполнитель", ex.Message);
    }
}
