using System.Net;
using System.Text;
using System.Text.Json;
using FindBook.Domain.Clients.OpenLibrary;

namespace FindBook.Tests.Unit;

public sealed class OpenLibraryEditionTests
{
    private const string SearchResponse = """
        {"docs":[{"key":"OL1W","title":"The Hobbit","first_publish_year":1937,
        "editions":{"docs":[{"key":"OL2M","title":"The Hobbit"}]}}]}
        """;

    [Fact]
    public async Task Fetches_requested_edition_date_and_features_from_its_own_record()
    {
        var paths = new List<string>();
        var client = CreateClient(path =>
        {
            paths.Add(path);
            return path == "/search.json" ? SearchResponse : """
                {"key":"/books/OL2M","title":"The Hobbit","subtitle":"There and Back Again",
                "publish_date":"September 21, 1937","edition_name":"Illustrated edition",
                "contributions":["A. Artist (Illustrator)"],"notes":{"value":"Includes illustrations."},
                "works":[{"key":"/works/OL1W"}]}
                """;
        });

        var books = await client.SearchAsync(new("The Hobbit", null, [], 1937, ["illustrated"]), default);

        Assert.Equal(new[] { "/search.json", "/books/OL2M.json" }, paths);
        var edition = Assert.Single(Assert.Single(books).Editions);
        Assert.Equal("September 21, 1937", edition.PublishDate);
        Assert.Equal("Illustrated edition", edition.EditionName);
        Assert.Equal("There and Back Again", edition.Subtitle);
        Assert.Equal("A. Artist (Illustrator)", Assert.Single(edition.Contributions));
        Assert.Equal("Includes illustrations.", edition.Notes);
    }

    [Theory]
    [InlineData("2001")]
    [InlineData("2001 [originally published 1937]")]
    [InlineData("1937?")]
    [InlineData("circa 1937")]
    [InlineData("Unknown")]
    [InlineData(null)]
    public async Task First_publication_never_substitutes_for_the_requested_edition_year(string? publishDate)
    {
        var client = CreateClient(path => path == "/search.json" ? SearchResponse : JsonSerializer.Serialize(new
        {
            key = "/books/OL2M",
            title = "The Hobbit",
            publish_date = publishDate,
            works = new[] { new { key = "/works/OL1W" } }
        }));

        var book = Assert.Single(await client.SearchAsync(new("The Hobbit", null, [], 1937, []), default));

        Assert.Equal(1937, book.FirstPublishYear);
        Assert.Empty(book.Editions);
    }

    [Fact]
    public async Task Ignores_an_edition_attached_to_a_different_work()
    {
        var client = CreateClient(path => path == "/search.json" ? SearchResponse
            : """{"key":"/books/OL2M","title":"The Hobbit","publish_date":"1937","works":[{"key":"/works/OL999W"}]}""");

        var book = Assert.Single(await client.SearchAsync(new("The Hobbit", null, [], 1937, []), default));

        Assert.Empty(book.Editions);
    }

    [Fact]
    public async Task Bounds_edition_lookups_and_keeps_unchecked_books_as_possibilities()
    {
        var requests = 0;
        var client = CreateClient(path =>
        {
            requests++;
            return path == "/search.json"
                ? """{"docs":[{"key":"OL1W","title":"One","editions":{"docs":[{"key":"OL1M","title":"One"}]}},{"key":"OL2W","title":"Two","editions":{"docs":[{"key":"OL2M","title":"Two"}]}}]}"""
                : """{"key":"/books/OL1M","title":"One","publish_date":"1937","notes":"Illustrated throughout.","works":[{"key":"/works/OL1W"}]}""";
        }, maxEditionLookups: 1);

        var books = await client.SearchAsync(new(null, "A Writer", [], null, ["illustrated"]), default);

        Assert.Equal(2, requests);
        Assert.Equal(2, books.Count);
        Assert.Equal("Illustrated throughout.", Assert.Single(books[0].Editions).Notes);
        Assert.Empty(books[1].Editions);
    }

    private static OpenLibraryApiClient CreateClient(Func<string, string> response, int maxEditionLookups = 5)
        => new(new StubFactory(response), new OpenLibraryApiOptions { MaxEditionLookups = maxEditionLookups });

    private sealed class StubFactory(Func<string, string> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHandler(response)) { BaseAddress = new Uri("https://openlibrary.org/") };
    }

    private sealed class StubHandler(Func<string, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json") });
    }
}
