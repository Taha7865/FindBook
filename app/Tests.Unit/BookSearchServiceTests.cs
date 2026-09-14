using FindBook.Api.Services;
using FindBook.Domain.Interfaces;
using FindBook.Domain.Models;

namespace FindBook.Tests.Unit;

public sealed class BookSearchServiceTests
{
    [Fact]
    public async Task Groups_editions_and_limits_distinct_works_without_merging_same_titles()
    {
        var books = new List<CatalogBook>
        {
            Book("OL1W", "Same title", new CatalogEdition("OL1M", "First edition", "1937")),
            Book("OL1W", "Same title", new CatalogEdition("OL2M", "Second edition", "1960")),
            Book("OL1W", "Same title", new CatalogEdition("OL2M", "Second edition", "1960"))
        };
        books.AddRange(Enumerable.Range(2, 6).Select(id => Book($"OL{id}W", "Same title")));
        var service = new BookSearchService(new StubCatalog(books));

        var response = await service.SearchAsync("example", default);

        Assert.Equal(new[] { "OL1W", "OL2W", "OL3W", "OL4W", "OL5W" }, response.Matches.Select(book => book.WorkId));
        Assert.Equal(2, response.Matches[0].Editions.Length);
        Assert.Equal("1960", response.Matches[0].Editions[1].PublishDate);
        Assert.Equal("https://openlibrary.org/books/OL2M", response.Matches[0].Editions[1].OpenLibraryUrl);
        Assert.Null(response.Matches[0].CoverUrl);
    }

    [Fact]
    public async Task Trims_outer_whitespace_preserves_query_and_passes_cancellation()
    {
        using var source = new CancellationTokenSource();
        var catalog = new StubCatalog([]);
        var response = await new BookSearchService(catalog).SearchAsync("  García & Tolkien  ", source.Token);

        Assert.Equal("García & Tolkien", catalog.Query);
        Assert.Equal(source.Token, catalog.Token);
        Assert.Empty(response.Matches);
    }

    private static CatalogBook Book(string id, string title, params CatalogEdition[] editions)
        => new(id, title, ["A Writer"], null, null, editions);

    private sealed class StubCatalog(IReadOnlyList<CatalogBook> books) : IBookCatalog
    {
        public string? Query { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<CatalogBook>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Query = query;
            Token = cancellationToken;
            return Task.FromResult(books);
        }
    }
}
