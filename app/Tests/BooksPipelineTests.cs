using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FindBook.Api.Controllers;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FindBook.Tests;

public sealed class BooksPipelineTests
{
    [Fact]
    public async Task Valid_request_returns_catalog_metadata_and_model_explanation()
    {
        using var app = new SearchApp();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/books/search", new { query = "The Hobbit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var match = Assert.Single((await response.Content.ReadFromJsonAsync<SearchResponse>())!.Matches);
        Assert.Equal("OL1W", match.OpenLibraryWorkId);
        Assert.Equal("The Hobbit", match.Title);
        Assert.Equal(["J. R. R. Tolkien"], match.Authors);
        Assert.Equal(1937, match.FirstPublishYear);
        Assert.Equal("https://openlibrary.org/works/OL1W", match.OpenLibraryUrl);
        Assert.Equal("https://covers.openlibrary.org/b/id/42-M.jpg?default=false", match.CoverUrl);
        Assert.Equal("The title matches.", match.Explanation);
        Assert.Empty(match.Editions);
        Assert.Equal(2, app.GeminiCalls);
        Assert.Equal(3, app.CatalogCalls);
    }

    [Theory]
    [InlineData(1937, true)]
    [InlineData(2000, false)]
    public async Task Topic_publication_year_keeps_matching_works_and_excludes_other_years(int year, bool matches)
    {
        using var app = new SearchApp { FirstPublishYear = year };
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/books/search", new { query = $"a book about a dragon, published in {year}" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var books = (await response.Content.ReadFromJsonAsync<SearchResponse>())!.Matches;
        if (matches)
        {
            var book = Assert.Single(books);
            Assert.Equal("The Hobbit", book.Title);
            Assert.Equal(year, book.FirstPublishYear);
            Assert.Empty(book.Editions);
        }
        else
        {
            Assert.Empty(books);
        }
        Assert.Equal(matches ? 2 : 1, app.GeminiCalls);
        Assert.Equal(matches ? 3 : 1, app.CatalogCalls);
    }

    [Theory]
    [InlineData("{}", "application/json", 400)]
    [InlineData("{\"query\":\"  \"}", "application/json", 400)]
    [InlineData("{\"query\":null}", "application/json", 400)]
    [InlineData("{\"query\":", "application/json", 400)]
    [InlineData("{\"query\":\"The Hobbit\"}", "text/plain", 415)]
    public async Task Invalid_requests_are_rejected_by_the_pipeline_without_upstream_calls(string body, string type, int status)
    {
        using var app = new SearchApp();
        using var client = app.CreateClient();
        using var response = await client.PostAsync("/api/books/search", new StringContent(body, Encoding.UTF8, type));

        await AssertProblem(response, status);
        Assert.Equal(0, app.GeminiCalls);
        Assert.Equal(0, app.CatalogCalls);
    }

    [Theory]
    [InlineData("Gemini", 503, 503, "The AI search service is unavailable. Try again later.")]
    [InlineData("Gemini", 408, 504, "The AI search service took too long to respond. Try again.")]
    [InlineData("Gemini", 401, 503, "The AI search service is not configured.")]
    [InlineData("Gemini", 400, 502, "The AI search service returned an unexpected response.")]
    [InlineData("OpenLibrary", 503, 503, "The book catalog is unavailable. Try again later.")]
    [InlineData("OpenLibrary", 408, 504, "The book catalog took too long to respond. Try again.")]
    [InlineData("OpenLibrary", 400, 502, "The book catalog returned an unexpected response.")]
    public async Task Upstream_failures_return_safe_problem_details(
        string provider, int upstreamStatus, int status, string title)
    {
        using var app = new SearchApp { FailureProvider = provider, FailureStatus = upstreamStatus };
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/books/search", new { query = "The Hobbit" });

        var problem = await AssertProblem(response, status);
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(upstreamStatus is 408 or 503 ? 3 : 1, provider == "Gemini" ? app.GeminiCalls : app.CatalogCalls);
        Assert.DoesNotContain(SearchApp.SensitiveBody, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("not-json-sensitive-model-body")]
    [InlineData("{\"books\":[{\"openLibraryWorkId\":\"OL999W\",\"explanation\":\"not-json-sensitive-model-body\"}]}")]
    public async Task Invalid_model_output_is_an_error_not_an_empty_selection(string selection)
    {
        using var app = new SearchApp { Selection = selection };
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/books/search", new { query = "The Hobbit" });

        await AssertProblem(response, 502);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        return problem;
    }

    // Replace only HTTP transports: routing, binding, validation, clients, retries, and serialization stay real.
    private sealed class SearchApp : WebApplicationFactory<BooksController>
    {
        public const string SensitiveBody = "private-upstream-response";
        public int? FirstPublishYear { get; init; }
        public string? FailureProvider { get; init; }
        public int FailureStatus { get; init; }
        public string Selection { get; init; } = """{"books":[{"openLibraryWorkId":"OL1W","explanation":"The title matches."}]}""";
        public int GeminiCalls { get; private set; }
        public int CatalogCalls { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GeminiApi:ApiKey"] = "test-key-do-not-log",
                ["GeminiApi:BaseUrl"] = "https://gemini.test/",
                ["OpenLibraryApi:BaseUrl"] = "https://catalog.test/",
                ["ExternalApiRetry:Delay"] = "00:00:00",
                ["ExternalApiRetry:UseJitter"] = "false"
            }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(GeminiApiOptions.SectionName)
                    .ConfigurePrimaryHttpMessageHandler(() => new SimulatedHandler(request =>
                    {
                        Assert.Equal("gemini.test", request.RequestUri!.Host);
                        GeminiCalls++;
                        if (FailureProvider == "Gemini") return Json(SensitiveBody, FailureStatus);
                        var terms = FirstPublishYear is { } year
                            ? new BookSearchTerms(null, null, ["dragons"], null, []) { FirstPublishYear = year }
                            : new BookSearchTerms("The Hobbit", "Tolkien", [], null, []);
                        var output = GeminiCalls == 1
                            ? JsonSerializer.Serialize(terms, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                            : Selection;
                        return Json(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } }));
                    }));
                services.AddHttpClient(OpenLibraryApiOptions.SectionName)
                    .ConfigurePrimaryHttpMessageHandler(() => new SimulatedHandler(request =>
                    {
                        Assert.Equal("catalog.test", request.RequestUri!.Host);
                        CatalogCalls++;
                        if (FailureProvider == "OpenLibrary") return Json(SensitiveBody, FailureStatus);
                        switch (request.RequestUri.AbsolutePath)
                        {
                            case "/search.json":
                                if (FirstPublishYear is { } requestedYear)
                                {
                                    var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
                                    Assert.Equal($"dragons AND first_publish_year:{requestedYear}", query["q"]);
                                    Assert.DoesNotContain("editions", query["fields"].ToString());
                                    Assert.False(query.ContainsKey("title"));
                                }
                                return Json("""{"docs":[{"key":"/works/OL1W","title":"The Hobbit","author_name":["Tolkien"],"first_publish_year":1937,"cover_i":42}]}""");
                            case "/works/OL1W.json":
                                return Json("""{"key":"/works/OL1W","authors":[{"author":{"key":"/authors/OL1A"}}]}""");
                            case "/authors/OL1A.json":
                                return Json("""{"key":"/authors/OL1A","name":"J. R. R. Tolkien"}""");
                            default: throw new InvalidOperationException("Unexpected upstream route.");
                        }
                    }));
            });
        }

        private static HttpResponseMessage Json(string body, int status = 200)
            => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class SimulatedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
