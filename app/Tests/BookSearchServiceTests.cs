using Microsoft.Extensions.Logging.Abstractions;
using FindBook.Api.Services;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests;

public class BookSearchServiceTests
{
    [Fact]
    public async Task Groups_editions_under_one_work_and_returns_fetched_metadata_with_geminis_explanation()
    {
        var firstEdition = new CatalogEdition("OL1M", "The Hobbit", "1937");
        var secondEdition = new CatalogEdition("OL2M", "The Hobbit", "2000")
        {
            Subtitle = "There and Back Again",
            EditionName = "Illustrated edition",
            Contributions = ["An Artist (Illustrator)"],
            Notes = "With illustrations."
        };
        var firstRecord = Book("OL1W", "The Hobbit") with
        {
            Authors = ["Tolkien"],
            FirstPublishYear = 1937,
            CoverId = 42,
            Editions = [firstEdition],
            Subjects = ["Fantasy"]
        };
        var catalog = new StubOpenLibraryApiClient(
            [firstRecord, firstRecord with { Editions = [firstEdition, secondEdition], Subjects = ["Fantasy", "Dragons"] }]);
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, ["illustrated"]),
            new BookSelection([new("OL1W", "The title and illustrated edition fit the request.")]));
        var service = new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance);

        var response = await service.SearchAsync("  tolkien hobbit illustrated  ", default);

        var suppliedBook = Assert.Single(Assert.Single(gemini.SelectionRequests).Books);
        Assert.Equal(new[] { "Fantasy", "Dragons" }, suppliedBook.Subjects);
        Assert.Equal("tolkien hobbit illustrated", gemini.SelectionRequests[0].Query);
        Assert.Equal("tolkien hobbit illustrated", Assert.Single(gemini.ExtractionQueries));
        var match = Assert.Single(response.Matches);
        Assert.Equal("The Hobbit", match.Title);
        Assert.Equal(1937, match.FirstPublishYear);
        Assert.Equal("https://openlibrary.org/works/OL1W", match.OpenLibraryUrl);
        Assert.Equal("https://covers.openlibrary.org/b/id/42-M.jpg?default=false", match.CoverUrl);
        Assert.Equal("The title and illustrated edition fit the request.", match.Explanation);
        Assert.Equal(new[] { "OL1M", "OL2M" }, match.Editions.Select(edition => edition.EditionId));
        Assert.Equal("2000", match.Editions[1].PublishDate);
        Assert.Equal(secondEdition.Subtitle, match.Editions[1].Subtitle);
        Assert.Equal(secondEdition.EditionName, match.Editions[1].EditionName);
        Assert.Equal(secondEdition.Contributions, match.Editions[1].Contributions);
        Assert.Equal(secondEdition.Notes, match.Editions[1].Notes);
    }

    [Fact]
    public async Task A_rejected_same_title_adaptation_is_not_restored_even_when_it_is_more_popular()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, []),
            new BookSelection([new("OL1W", "The supplied subjects identify the novel.")]));
        var catalog = new StubOpenLibraryApiClient(
        [
            Book("OL1W", "The Hobbit") with { Subjects = ["Fiction"], ReadingLogCount = 10 },
            Book("OL2W", "The Hobbit") with { Subjects = ["Motion picture music"], ReadingLogCount = 900 }
        ]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("The Hobbit", default);

        var match = Assert.Single(response.Matches);
        Assert.Equal("OL1W", match.OpenLibraryWorkId);
        Assert.Equal("The supplied subjects identify the novel.", match.Explanation);
        Assert.Single(catalog.Searches);
    }

    [Fact]
    public async Task An_accepted_verified_work_author_match_beats_an_accepted_more_popular_contributor_match()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select("OL1W", "OL2W"));
        var catalog = new StubOpenLibraryApiClient(
        [
            Book("OL1W", "The Hobbit") with
            {
                Authors = ["Another Writer", "Tolkien"], ReadingLogCount = 900,
                Editions = [new("OL1M", "The Hobbit", null) { Contributions = ["Tolkien (Illustrator)"] }]
            },
            Book("OL2W", "The Hobbit") with { Authors = ["Tolkien"], ReadingLogCount = 10 }
        ])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A"]), ["OL2W"] = new("OL2W", ["OL2A"]) },
            Authors = { ["OL1A"] = new("OL1A", "Another Writer", []), ["OL2A"] = new("OL2A", "Tolkien", []) }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Equal("Explanation for OL2W.", response.Matches[0].Explanation);
        Assert.Equal("Tolkien", gemini.SelectionRequests[0].Books[1].WorkAuthors[0].Name);
    }

    [Theory]
    [InlineData(10, 200, new[] { "OL2W" })]
    [InlineData(1, null, new[] { "OL1W" })]
    [InlineData(10, 10, new[] { "OL2W", "OL1W" })]
    [InlineData(null, null, new[] { "OL2W", "OL1W" })]
    [InlineData(0, null, new[] { "OL1W", "OL2W" })]
    public async Task Exact_title_popularity_prefers_a_positive_leader_and_preserves_alternatives_otherwise(
        int? firstCount, int? secondCount, string[] expectedIds)
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, []), Select("OL2W", "OL1W"));
        var catalog = new StubOpenLibraryApiClient(
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = firstCount },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = secondCount }
        ]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("The Hobbit", default);

        Assert.Equal(expectedIds, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.All(response.Matches, book => Assert.Equal($"Explanation for {book.OpenLibraryWorkId}.", book.Explanation));
    }

    [Fact]
    public async Task Gemini_accepting_no_books_triggers_a_separate_author_search_and_selection_of_the_new_pool()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]),
            Select(), Select("OL2W", "OL3W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")],
            [Book("OL2W", "The Silmarillion") with { ReadingLogCount = 10 }, Book("OL3W", "Unfinished Tales") with { ReadingLogCount = 20 }]);
        var validator = new StubBookSearchValidator();

        var response = await new BookSearchService(gemini, catalog, validator, NullLogger<BookSearchService>.Instance).SearchAsync("The Hobbit by Tolkien deluxe 1937", default);

        Assert.Equal(2, catalog.Searches.Count);
        Assert.Null(catalog.Searches[1].Title);
        Assert.Equal("Tolkien", catalog.Searches[1].Author);
        Assert.Null(catalog.Searches[1].EditionYear);
        Assert.Empty(catalog.Searches[1].EditionKeywords);
        Assert.Equal(2, gemini.SelectionRequests.Count);
        Assert.All(gemini.SelectionRequests, request => Assert.Equal("The Hobbit by Tolkien deluxe 1937", request.Query));
        Assert.Equal(new[] { "OL2W", "OL3W" }, gemini.SelectionRequests[1].Books.Select(book => book.OpenLibraryWorkId));
        Assert.Same(gemini.SelectionRequests[1].Books, validator.ValidatedSelections[1].Books);
        Assert.Equal(new[] { "OL3W", "OL2W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
    }

    [Fact]
    public async Task Missing_edition_tries_the_same_book_without_edition_filters_before_broadening_to_the_author()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]), Select("OL1W"));
        var catalog = new StubOpenLibraryApiClient([], [], [Book("OL1W", "The Silmarillion")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("tolkien hobbit deluxe 1937", default);

        Assert.Equal(3, catalog.Searches.Count);
        Assert.Equal(1937, catalog.Searches[0].EditionYear);
        Assert.Equal("The Hobbit", catalog.Searches[1].Title);
        Assert.Null(catalog.Searches[1].EditionYear);
        Assert.Empty(catalog.Searches[1].EditionKeywords);
        Assert.Null(catalog.Searches[2].Title);
        Assert.Equal("Tolkien", catalog.Searches[2].Author);
        Assert.Equal("tolkien hobbit deluxe 1937", Assert.Single(gemini.SelectionRequests).Query);
        Assert.Equal("OL1W", Assert.Single(response.Matches).OpenLibraryWorkId);
    }

    [Fact]
    public async Task Verification_shares_the_ten_author_lookup_limit_and_cached_authors_with_the_fallback()
    {
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")], [Book("OL2W", "Another book")]);
        catalog.Works["OL1W"] = new("OL1W", Enumerable.Range(1, 15).Select(index => $"OL{index}A").ToArray());
        catalog.Works["OL2W"] = new("OL2W", ["OL1A", "OL16A"]);
        for (var index = 1; index <= 16; index++)
            catalog.Authors[$"OL{index}A"] = new($"OL{index}A", $"Writer {index}", []);
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Writer 1", [], null, []), Select(), Select("OL2W"));

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("The Hobbit by Writer 1", default);

        Assert.Equal(10, catalog.AuthorRequests.Count);
        Assert.Equal(new[] { "Writer 1" }, Assert.Single(response.Matches).Authors);
    }

    [Fact]
    public async Task A_failed_author_lookup_is_not_repeated_during_the_author_fallback()
    {
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")],
            [Book("OL2W", "Another book") with { Authors = ["Tolkien"] }])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A"]), ["OL2W"] = new("OL2W", ["OL1A"]) },
            AuthorException = new CatalogException(CatalogFailure.Unavailable)
        };
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select(), Select("OL2W"));

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance)
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Single(catalog.AuthorRequests);
        Assert.Empty(Assert.Single(gemini.SelectionRequests[1].Books).WorkAuthors);
        Assert.Equal(new[] { "Tolkien" }, Assert.Single(response.Matches).Authors);
    }

    [Fact]
    public async Task Caller_cancellation_passes_through_verification_without_selection_or_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")])
        { WorkException = new OperationCanceledException(cancellation.Token) };

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new BookSearchService(gemini, catalog, new StubBookSearchValidator(), NullLogger<BookSearchService>.Instance).SearchAsync("The Hobbit", cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.All(gemini.Tokens.Concat(catalog.Tokens), token => Assert.Equal(cancellation.Token, token));
        Assert.Empty(gemini.SelectionRequests);
        Assert.Single(catalog.Searches);
    }

    private static CatalogBook Book(string id, string title) => new(id, title, [], null, null, []);

    private static BookSelection Select(params string[] ids)
        => new(ids.Select(id => new SelectedBook(id, $"Explanation for {id}.")).ToArray());

    // Search and selection responses are queued. An unplanned extra round fails the test.
    private class StubGeminiApiClient(BookSearchTerms searchTerms, params BookSelection[] selections) : IGeminiApiClient
    {
        private readonly Queue<BookSelection> _selections = new(selections);
        public List<string> ExtractionQueries { get; } = [];
        public List<(string Query, IReadOnlyList<CatalogBook> Books)> SelectionRequests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<BookSearchTerms> ExtractSearchTermsAsync(string userQuery, CancellationToken cancellationToken)
        {
            ExtractionQueries.Add(userQuery);
            Tokens.Add(cancellationToken);
            return Task.FromResult(searchTerms);
        }

        public Task<BookSelection> SelectBooksAsync(string userQuery, IReadOnlyList<CatalogBook> booksFromOpenLibrary,
            CancellationToken cancellationToken)
        {
            SelectionRequests.Add((userQuery, booksFromOpenLibrary));
            Tokens.Add(cancellationToken);
            return Task.FromResult(_selections.Dequeue());
        }
    }

    private class StubOpenLibraryApiClient(params CatalogBook[][] searchResponses) : IOpenLibraryApiClient
    {
        private readonly Queue<CatalogBook[]> _searchResponses = new(searchResponses);
        public List<BookSearchTerms> Searches { get; } = [];
        public List<string> AuthorRequests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Dictionary<string, CatalogWork?> Works { get; } = [];
        public Dictionary<string, CatalogAuthor?> Authors { get; } = [];
        public Exception? WorkException { get; init; }
        public Exception? AuthorException { get; init; }

        public Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken)
        {
            Searches.Add(searchTerms);
            Tokens.Add(cancellationToken);
            return Task.FromResult<IReadOnlyList<CatalogBook>>(_searchResponses.Dequeue());
        }

        public Task<CatalogWork?> GetWorkAsync(string workId, CancellationToken cancellationToken)
        {
            Tokens.Add(cancellationToken);
            if (WorkException is not null) throw WorkException;
            return Task.FromResult(Works.GetValueOrDefault(workId));
        }

        public Task<CatalogAuthor?> GetAuthorAsync(string authorId, CancellationToken cancellationToken)
        {
            AuthorRequests.Add(authorId);
            Tokens.Add(cancellationToken);
            if (AuthorException is not null) throw AuthorException;
            return Task.FromResult(Authors.GetValueOrDefault(authorId));
        }
    }

    // Validation rules have their own tests. Service tests control acceptance through the injected interface.
    private class StubBookSearchValidator : IBookSearchValidator
    {
        public List<(BookSelection Selection, IReadOnlyList<CatalogBook> Books)> ValidatedSelections { get; } = [];

        public void ValidateSearchTerms(BookSearchTerms searchTerms) { }

        public void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary)
        {
            ValidatedSelections.Add((selection, booksFromOpenLibrary));
        }
    }
}
