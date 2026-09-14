using System.Net;
using System.Text;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Exceptions;
using Microsoft.AspNetCore.WebUtilities;

namespace FindBook.Tests.Unit;

public sealed class OpenLibraryApiClientTests
{
    [Fact]
    public async Task Maps_catalog_fields_and_keeps_query_in_one_parameter()
    {
        const string query = "García & title=other #book?";
        using var http = CreateHttp((request, _) =>
        {
            var parameters = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal(query, parameters["q"].ToString());
            Assert.False(parameters.ContainsKey("title"));
            Assert.Equal("/search.json", request.RequestUri.AbsolutePath);
            return Task.FromResult(Json("""
                {"docs":[{"key":"/works/OL1W","title":"Example","author_name":["A Writer"],
                "first_publish_year":1937,"cover_i":123,
                "editions":{"docs":[{"key":"/books/OL2M","title":"Example, illustrated"}]}}]}
                """));
        });

        var books = await new OpenLibraryApiClient(new StubFactory(http)).SearchAsync(query, CancellationToken.None);

        var book = Assert.Single(books);
        Assert.Equal("OL1W", book.WorkId);
        Assert.Equal("A Writer", Assert.Single(book.Authors));
        Assert.Equal(1937, book.FirstPublishYear);
        Assert.Equal(123, book.CoverId);
        Assert.Equal("OL2M", Assert.Single(book.Editions).EditionId);
        Assert.Null(book.Editions[0].PublishDate);
    }

    [Fact]
    public async Task Allows_missing_optional_metadata_and_skips_invalid_records()
    {
        using var http = CreateHttp((_, _) => Task.FromResult(Json("""
            {"docs":[null,{"key":"https://other.example/OL1W","title":"Bad key"},
            {"key":"OL2W\n","title":"Invalid trailing newline"},
            {"key":"OL1W","title":"Example","cover_i":-1}]}
            """)));

        var book = Assert.Single(await new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", default));

        Assert.Empty(book.Authors);
        Assert.Empty(book.Editions);
        Assert.Null(book.FirstPublishYear);
        Assert.Null(book.CoverId);
    }

    [Fact]
    public async Task Empty_docs_is_a_successful_empty_search()
    {
        using var http = CreateHttp((_, _) => Task.FromResult(Json("{\"docs\":[]}")));
        Assert.Empty(await new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("unknown", default));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"docs\":null}")]
    [InlineData("{\"docs\":[{\"title\":\"Missing ID\"}]}")]
    public async Task Invalid_response_is_not_reported_as_no_matches(string body)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(Json(body)));
        var error = await Assert.ThrowsAsync<CatalogException>(() =>
            new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", default));
        Assert.Equal(CatalogFailure.BadResponse, error.Failure);
    }

    [Theory]
    [InlineData(429, CatalogFailure.Unavailable)]
    [InlineData(503, CatalogFailure.Unavailable)]
    [InlineData(500, CatalogFailure.BadResponse)]
    [InlineData(404, CatalogFailure.BadResponse)]
    public async Task Maps_unsuccessful_catalog_status(int status, CatalogFailure failure)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<CatalogException>(() =>
            new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", default));
        Assert.Equal(failure, error.Failure);
    }

    [Fact]
    public async Task Network_failure_becomes_unavailable()
    {
        using var http = CreateHttp((_, _) => throw new HttpRequestException("Connection failed"));
        var error = await Assert.ThrowsAsync<CatalogException>(() =>
            new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", default));
        Assert.Equal(CatalogFailure.Unavailable, error.Failure);
    }

    [Fact]
    public async Task Http_timeout_becomes_catalog_timeout()
    {
        using var http = CreateHttp((_, _) => throw new TaskCanceledException("HTTP timeout"));
        var error = await Assert.ThrowsAsync<CatalogException>(() =>
            new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", default));
        Assert.Equal(CatalogFailure.Timeout, error.Failure);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_catalog_timeout()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var http = CreateHttp((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OpenLibraryApiClient(new StubFactory(http)).SearchAsync("example", source.Token));
    }

    private static HttpClient CreateHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new StubHandler(send)) { BaseAddress = new Uri("https://openlibrary.org/") };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenLibraryApiOptions.SectionName, name);
            return client;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
