using FindBook.Api.Services;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests.Unit;

public sealed class BookSearchServiceTests
{
    [Fact]
    public async Task Runs_extraction_catalog_and_selection_in_order_and_uses_catalog_metadata()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            Selection = new([new("OL2W", "The supplied title matches."), new("OL1W", "Another possible match.")])
        };
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "Same title", new CatalogEdition("OL1M", "First edition", "1937")),
            Book("OL1W", "Same title", new CatalogEdition("OL2M", "Second edition", "1960")),
            Book("OL2W", "Same title")
        ]);
        using var cancellation = new CancellationTokenSource();
        var service = new BookSearchService(gemini, catalog, new BookSearchValidator());

        var response = await service.SearchAsync("  the hobbit  ", cancellation.Token);

        Assert.Equal(new[] { "extract", "catalog", "select" }, calls);
        Assert.Equal("the hobbit", gemini.ExtractionQuery);
        Assert.Equal("the hobbit", gemini.SelectionQuery);
        Assert.Same(gemini.SearchTerms, Assert.Single(catalog.Searches));
        Assert.Equal(cancellation.Token, gemini.Token);
        Assert.Equal(cancellation.Token, catalog.Token);
        Assert.Equal(2, gemini.SuppliedBooks!.Count);
        Assert.Equal(2, gemini.SuppliedBooks[0].Editions.Length);
        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Equal("Same title", response.Matches[0].Title);
        Assert.Equal("The supplied title matches.", response.Matches[0].Explanation);
        Assert.Equal("https://openlibrary.org/works/OL2W", response.Matches[0].OpenLibraryUrl);
        Assert.Equal("https://openlibrary.org/books/OL2M", response.Matches[1].Editions[1].OpenLibraryUrl);
        Assert.Null(response.Matches[0].CoverUrl);
    }

    [Theory]
    [InlineData("  THE HOBBIT!!  ", "The Hobbit", "Tolkien")]
    [InlineData("\"The   Hobbit\"", "The Hobbit", "Tolkien")]
    [InlineData("The Hobbit by Tolkien", "The Hobbit", "Tolkien")]
    [InlineData("The Hobbit (Tolkien)", "The Hobbit", "Tolkien")]
    [InlineData("les miserables", "Les Misérables", "Victor Hugo")]
    public async Task Exact_query_match_moves_above_partial_matches_without_duplicates(string query, string title, string author)
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            Selection = new([new("OL1W", "A related book."), new("OL2W", "A possible match.")])
        };
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "A companion book"),
            Book("OL2W", title) with { Authors = [author] }
        ]);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator()).SearchAsync(query, default);

        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.StartsWith("The query matches this book's title", response.Matches[0].Explanation);
        Assert.Equal("A related book.", response.Matches[1].Explanation);
        Assert.Equal(new[] { "extract", "catalog", "select" }, calls);
    }

    [Fact]
    public async Task Exact_match_omitted_by_gemini_is_included_within_the_five_result_limit()
    {
        var calls = new List<string>();
        var relatedBooks = Enumerable.Range(1, 5).Select(index => Book($"OL{index}W", $"A companion book {index}")).ToArray();
        var gemini = new StubGemini(calls)
        {
            Selection = new(relatedBooks.Select(book => new SelectedBook(book.OpenLibraryWorkId, "A related book.")).ToArray())
        };
        var catalog = new StubCatalog(calls, [.. relatedBooks, Book("OL6W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Equal(new[] { "OL6W", "OL1W", "OL2W", "OL3W", "OL4W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Equal("The query matches this book's title.", response.Matches[0].Explanation);
    }

    [Theory]
    [InlineData("hobbit")]
    [InlineData("a book about a dragon")]
    [InlineData("The Hobbit by Someone Else")]
    [InlineData("The Hobbit 1937 illustrated edition")]
    [InlineData("books like The Hobbit")]
    [InlineData("Tolkien")]
    public async Task Partial_descriptive_author_and_edition_queries_keep_geminis_selection(string query)
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("The Hobbit", "Tolkien", [], null, []),
            Selection = new([new("OL1W", "First possibility."), new("OL2W", "Second possibility.")])
        };
        var catalog = new StubCatalog(calls, [Book("OL1W", "A related book"), Book("OL2W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator()).SearchAsync(query, default);

        Assert.Equal(new[] { "OL1W", "OL2W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Equal("Second possibility.", response.Matches[1].Explanation);
    }

    [Fact]
    public async Task Exact_match_is_returned_even_when_gemini_selects_no_books()
    {
        var calls = new List<string>();
        var response = await new BookSearchService(new StubGemini(calls),
            new StubCatalog(calls, [Book("OL1W", "The Hobbit")]), new BookSearchValidator())
            .SearchAsync("The Hobbit", default);

        Assert.Equal("OL1W", Assert.Single(response.Matches).OpenLibraryWorkId);
    }

    [Fact]
    public async Task Invalid_ai_selection_is_still_rejected_when_an_exact_match_exists()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls) { Selection = new([new("OL999W", "Invented selection.")]) };

        await Assert.ThrowsAsync<BookSearchAiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, [Book("OL1W", "The Hobbit")]), new BookSearchValidator())
                .SearchAsync("The Hobbit", default));
    }

    [Fact]
    public async Task Empty_catalog_skips_the_second_gemini_call()
    {
        var calls = new List<string>();
        var response = await new BookSearchService(new StubGemini(calls), new StubCatalog(calls, []), new BookSearchValidator())
            .SearchAsync("the hobbit", default);

        Assert.Equal(new[] { "extract", "catalog" }, calls);
        Assert.Empty(response.Matches);
    }

    [Fact]
    public async Task No_searchable_terms_returns_empty_without_a_catalog_request()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls) { SearchTerms = new(null, null, [], null, []) };
        var response = await new BookSearchService(gemini, new StubCatalog(calls, []), new BookSearchValidator())
            .SearchAsync("hello", default);

        Assert.Equal(new[] { "extract" }, calls);
        Assert.Empty(response.Matches);
    }

    [Fact]
    public async Task Empty_title_and_author_search_gets_one_broader_author_search()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("Unknown title", "Tolkien", [], null, []),
            Selection = new([new("OL1W", "A book by the requested author.")])
        };
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit")]) { EmptyFirstSearch = true };
        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator()).SearchAsync("unknown tolkien", default);

        Assert.Equal(new[] { "extract", "catalog", "catalog", "select" }, calls);
        Assert.Equal(2, catalog.Searches.Count);
        Assert.Null(catalog.Searches[1].Title);
        Assert.Equal("Tolkien", catalog.Searches[1].Author);
        Assert.Single(response.Matches);
    }

    [Fact]
    public async Task Invalid_search_terms_stop_before_catalog_access()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls) { SearchTerms = new(" ", null, [], null, []) };
        await Assert.ThrowsAsync<BookSearchAiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, []), new BookSearchValidator()).SearchAsync("example", default));
        Assert.Equal(new[] { "extract" }, calls);
    }

    [Fact]
    public async Task Invented_selection_is_rejected_before_building_the_response()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls) { Selection = new([new("OL999W", "Invented selection.")]) };
        await Assert.ThrowsAsync<BookSearchAiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, [Book("OL1W", "The Hobbit")]), new BookSearchValidator())
                .SearchAsync("example", default));
    }

    [Fact]
    public async Task Uses_the_injected_validator_and_stops_when_it_rejects_the_input()
    {
        var calls = new List<string>();
        var validator = new RejectingValidator();
        var gemini = new StubGemini(calls);
        await Assert.ThrowsAsync<BookSearchAiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, []), validator).SearchAsync("example", default));
        Assert.Same(gemini.SearchTerms, validator.ReceivedTerms);
        Assert.Equal(new[] { "extract" }, calls);
    }

    private static CatalogBook Book(string id, string title, params CatalogEdition[] editions)
        => new(id, title, ["Tolkien"], null, null, editions);

    private sealed class StubGemini(List<string> calls) : IGeminiApiClient
    {
        public BookSearchTerms SearchTerms { get; init; } = new("The Hobbit", null, [], null, []);
        public BookSelection Selection { get; init; } = new([]);
        public string? ExtractionQuery { get; private set; }
        public string? SelectionQuery { get; private set; }
        public IReadOnlyList<CatalogBook>? SuppliedBooks { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<BookSearchTerms> ExtractSearchTermsAsync(string userQuery, CancellationToken cancellationToken)
        {
            calls.Add("extract");
            ExtractionQuery = userQuery;
            Token = cancellationToken;
            return Task.FromResult(SearchTerms);
        }

        public Task<BookSelection> SelectBooksAsync(string userQuery, IReadOnlyList<CatalogBook> booksFromOpenLibrary,
            CancellationToken cancellationToken)
        {
            calls.Add("select");
            SelectionQuery = userQuery;
            SuppliedBooks = booksFromOpenLibrary;
            Token = cancellationToken;
            return Task.FromResult(Selection);
        }
    }

    private sealed class StubCatalog(List<string> calls, IReadOnlyList<CatalogBook> books) : IOpenLibraryApiClient
    {
        public List<BookSearchTerms> Searches { get; } = [];
        public bool EmptyFirstSearch { get; init; }
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken)
        {
            calls.Add("catalog");
            Searches.Add(searchTerms);
            Token = cancellationToken;
            return Task.FromResult<IReadOnlyList<CatalogBook>>(EmptyFirstSearch && Searches.Count == 1 ? [] : books);
        }
    }

    private sealed class RejectingValidator : IBookSearchValidator
    {
        public BookSearchTerms? ReceivedTerms { get; private set; }
        public void ValidateSearchTerms(BookSearchTerms searchTerms)
        {
            ReceivedTerms = searchTerms;
            throw new BookSearchAiException(BookSearchAiFailure.BadResponse);
        }
        public void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary)
            => throw new InvalidOperationException("Must not reach selection.");
    }
}
