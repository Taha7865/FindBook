using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FindBook.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindBook.Tests.Unit;

public sealed class HttpClientRetryTests
{
    [Theory]
    [InlineData("OpenLibraryApi", "GET")]
    [InlineData("GeminiApi", "POST")]
    [InlineData("AlternativeApi", "POST")]
    public async Task All_factory_clients_retry_and_preserve_request_content(string clientName, string method)
    {
        var attempts = 0;
        using var services = CreateServices(clientName, async (request, token) =>
        {
            Assert.Equal(new HttpMethod(method), request.Method);
            if (method == "POST")
                Assert.Equal("{\"query\":\"a dragon book\"}", await request.Content!.ReadAsStringAsync(token));
            attempts++;
            return new HttpResponseMessage(attempts switch
            {
                1 => HttpStatusCode.ServiceUnavailable,
                2 => HttpStatusCode.TooManyRequests,
                _ => HttpStatusCode.OK
            });
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        using var request = new HttpRequestMessage(new HttpMethod(method), "search");
        if (method == "POST")
            request.Content = JsonContent.Create(new { query = "a dragon book" });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Temporary_failures_stop_after_three_total_attempts(int status)
    {
        var attempts = 0;
        using var services = CreateServices("GeminiApi", (_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiApi");

        using var response = await client.GetAsync("search");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Successful_responses_and_permanent_errors_are_not_retried(int status)
    {
        var attempts = 0;
        using var services = CreateServices("OpenLibraryApi", (_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("OpenLibraryApi");

        using var response = await client.GetAsync("search");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Network_failures_are_retried_but_remain_bounded()
    {
        var attempts = 0;
        using var services = CreateServices("OpenLibraryApi", (_, _) =>
        {
            attempts++;
            throw new HttpRequestException("Connection failed");
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("OpenLibraryApi");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("search"));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_after_is_respected_and_caller_cancellation_stops_the_wait()
    {
        var attempts = 0;
        using var services = CreateServices("GeminiApi", (_, _) =>
        {
            attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiApi");
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("search", cancellation.Token);

        // The normal test delay is zero. Retry-After must prevent an immediate second attempt.
        Assert.NotSame(request, await Task.WhenAny(request, Task.Delay(100)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Client_timeout_includes_retry_delays()
    {
        var attempts = 0;
        using var services = CreateServices("GeminiApi", (_, _) =>
        {
            attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiApi");
        client.Timeout = TimeSpan.FromMilliseconds(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("search"));

        Assert.Equal(1, attempts);
    }

    private static ServiceProvider CreateServices(string clientName,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApiRetry:Delay"] = "00:00:00",
                ["ExternalApiRetry:UseJitter"] = "false"
            }).Build();
        var services = new ServiceCollection();
        services.AddApiClientRetries(configuration);
        services.AddHttpClient(clientName, client => client.BaseAddress = new Uri("https://example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(send));
        return services.BuildServiceProvider();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
