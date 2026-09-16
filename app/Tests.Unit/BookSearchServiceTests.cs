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
    public async Task Unique_exact_query_match_returns_one_winner(string query, string title, string author)
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

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.StartsWith("The query matches this book's title", response.Matches[0].Explanation);
        Assert.Equal(new[] { "extract", "catalog", "select" }, calls);
    }

    [Fact]
    public async Task Unique_exact_title_omitted_by_gemini_is_returned_alone()
    {
        var calls = new List<string>();
        var relatedBooks = Enumerable.Range(1, 5).Select(index => Book($"OL{index}W", $"A companion book {index}")).ToArray();
        var gemini = new StubGemini(calls)
        {
            Selection = new(relatedBooks.Select(book => new SelectedBook(book.OpenLibraryWorkId, "A related book.")).ToArray())
        };
        var catalog = new StubCatalog(calls, [.. relatedBooks, Book("OL6W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Equal("OL6W", Assert.Single(response.Matches).OpenLibraryWorkId);
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

        await Assert.ThrowsAsync<GeminiApiException>(() =>
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
        await Assert.ThrowsAsync<GeminiApiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, []), new BookSearchValidator()).SearchAsync("example", default));
        Assert.Equal(new[] { "extract" }, calls);
    }

    [Fact]
    public async Task Invented_selection_is_rejected_before_building_the_response()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls) { Selection = new([new("OL999W", "Invented selection.")]) };
        await Assert.ThrowsAsync<GeminiApiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, [Book("OL1W", "The Hobbit")]), new BookSearchValidator())
                .SearchAsync("example", default));
    }

    [Fact]
    public async Task Uses_the_injected_validator_and_stops_when_it_rejects_the_input()
    {
        var calls = new List<string>();
        var validator = new RejectingValidator();
        var gemini = new StubGemini(calls);
        await Assert.ThrowsAsync<GeminiApiException>(() =>
            new BookSearchService(gemini, new StubCatalog(calls, []), validator).SearchAsync("example", default));
        Assert.Same(gemini.SearchTerms, validator.ReceivedTerms);
        Assert.Equal(new[] { "extract" }, calls);
    }

    private static CatalogBook Book(string id, string title, params CatalogEdition[] editions)
        => new(id, title, ["Tolkien"], null, null, editions);

    [Fact]
    public async Task Missing_edition_relaxes_edition_filters_before_the_author_fallback()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]),
            Selection = new([new("OL1W", "The book matches; the requested edition is unverified.")])
        };
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit")]) { EmptyFirstSearch = true };

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("tolkien hobbit deluxe 1937", default);

        Assert.Equal(2, catalog.Searches.Count);
        Assert.Equal(1937, catalog.Searches[0].EditionYear);
        Assert.Null(catalog.Searches[1].EditionYear);
        Assert.Empty(catalog.Searches[1].EditionKeywords);
        Assert.Equal("The Hobbit", catalog.Searches[1].Title);
        Assert.Equal("Tolkien", catalog.Searches[1].Author);
        Assert.Equal("tolkien hobbit deluxe 1937", gemini.SelectionQuery);
        Assert.Contains("unverified", Assert.Single(response.Matches).Explanation);
    }

    [Fact]
    public async Task Returns_fetched_edition_features_in_the_public_response()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("The Hobbit", null, [], 1937, ["illustrated"]),
            Selection = new([new("OL1W", "The edition record lists illustrations and a 1937 publication date.")])
        };
        var edition = new CatalogEdition("OL1M", "The Hobbit", "1937")
        {
            EditionName = "Illustrated edition",
            Subtitle = "There and Back Again",
            Contributions = ["An Artist (Illustrator)"],
            Notes = "With illustrations."
        };

        var response = await new BookSearchService(gemini, new StubCatalog(calls, [Book("OL1W", "The Hobbit", edition)]),
            new BookSearchValidator()).SearchAsync("The Hobbit illustrated 1937", default);

        var result = Assert.Single(Assert.Single(response.Matches).Editions);
        Assert.Equal("1937", result.PublishDate);
        Assert.Equal(edition.EditionName, result.EditionName);
        Assert.Equal(edition.Subtitle, result.Subtitle);
        Assert.Equal(edition.Contributions, result.Contributions);
        Assert.Equal(edition.Notes, result.Notes);
    }

    [Fact]
    public async Task Verified_work_author_beats_a_more_popular_contributor_match_selected_by_gemini()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            Selection = new([new("OL1W", "The name appears as an illustrator.")])
        };
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit", new CatalogEdition("OL1M", "The Hobbit", null)
                { Contributions = ["Tolkien (Illustrator)"] }) with { ReadingLogCount = 900 },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = 10 }
        ])
        {
            Works = new() { ["OL1W"] = new("OL1W", ["OL2A"]) },
            Authors = new() { ["OL2A"] = new("OL2A", "Another Writer", []) }
        };

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Contains("work record", response.Matches[0].Explanation);
        Assert.Equal("Tolkien", gemini.SuppliedBooks![1].WorkAuthors[0].Name);
    }

    [Fact]
    public async Task Fetched_author_alias_can_establish_the_exact_author_match()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit")])
        {
            Authors = new() { ["OL1A"] = new("OL1A", "J. R. R. Tolkien", ["Tolkien"]) }
        };

        var response = await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Contains("work record", Assert.Single(response.Matches).Explanation);
    }

    [Fact]
    public async Task Multiple_exact_work_author_matches_remain_multiple_even_with_different_popularity()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = 10 },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = 20 },
            Book("OL3W", "A related book")
        ]);

        var response = await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
    }

    [Fact]
    public async Task Title_only_selects_the_uniquely_most_popular_exact_title()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = 10 },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = 200 },
            Book("OL3W", "A guide to The Hobbit") with { ReadingLogCount = 900 }
        ]);

        var response = await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Contains("reading-list count", response.Matches[0].Explanation);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(null, null)]
    [InlineData(0, null)]
    public async Task Equal_or_unreported_popularity_does_not_establish_a_title_only_winner(int? firstCount, int? secondCount)
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = firstCount },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = secondCount }
        ]);

        var response = await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit", default);

        Assert.Equal(2, response.Matches.Length);
    }

    [Fact]
    public async Task A_missing_count_does_not_veto_the_most_popular_reported_exact_title()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = 10 },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = 200 },
            Book("OL3W", "The Hobbit") with { ReadingLogCount = null }
        ]);

        var response = await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Contains("highest reported", response.Matches[0].Explanation);
    }

    [Fact]
    public async Task Missing_work_author_data_does_not_turn_a_listed_name_into_a_verified_match()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit")])
        {
            Works = new() { ["OL1W"] = null }
        };
        var gemini = new StubGemini(calls);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Empty(response.Matches);
        Assert.Empty(gemini.SuppliedBooks![0].WorkAuthors);
    }

    [Fact]
    public async Task Response_uses_work_authors_instead_of_search_contributors_and_keeps_edition_roles()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("The Hobbit", null, [], null, ["illustrated"]),
            Selection = new([new("OL1W", "The supplied edition includes illustrations.")])
        };
        var catalog = new StubCatalog(calls,
        [
            Book("OL1W", "The Hobbit", new CatalogEdition("OL1M", "The Hobbit", "2000")
                { Contributions = ["An Illustrator (Illustrator)"] })
                with { Authors = ["Tolkien", "An Illustrator"] }
        ])
        {
            Works = new() { ["OL1W"] = new("OL1W", ["OL1A", "OL2A"]) },
            Authors = new()
            {
                ["OL1A"] = new("OL1A", "J. R. R. Tolkien", ["Tolkien"]),
                ["OL2A"] = new("OL2A", "J. R. R. Tolkien", [])
            }
        };

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit illustrated", default);

        var match = Assert.Single(response.Matches);
        Assert.Equal(new[] { "J. R. R. Tolkien" }, match.Authors);
        Assert.Equal(new[] { "An Illustrator (Illustrator)" }, Assert.Single(match.Editions).Contributions);
    }

    [Fact]
    public async Task Response_keeps_all_resolved_work_authors()
    {
        var calls = new List<string>();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new("Good Omens", null, [], null, []),
            Selection = new([new("OL1W", "The title matches.")])
        };
        var catalog = new StubCatalog(calls, [Book("OL1W", "Good Omens") with { Authors = ["A Contributor"] }])
        {
            Works = new() { ["OL1W"] = new("OL1W", ["OL1A", "OL2A"]) },
            Authors = new()
            {
                ["OL1A"] = new("OL1A", "Terry Pratchett", []),
                ["OL2A"] = new("OL2A", "Neil Gaiman", [])
            }
        };

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("Good Omens", default);

        Assert.Equal(new[] { "Terry Pratchett", "Neil Gaiman" }, Assert.Single(response.Matches).Authors);
    }

    [Fact]
    public async Task Optional_verification_failure_keeps_search_results_without_claiming_authorship()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit"), Book("OL2W", "Another title")])
        {
            WorkFailure = CatalogFailure.Unavailable
        };
        var gemini = new StubGemini(calls)
        {
            Selection = new([new("OL1W", "The title and listed name are a possible match.")])
        };

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Single(catalog.WorkRequests);
        Assert.Empty(gemini.SuppliedBooks![0].WorkAuthors);
        Assert.Equal(gemini.Selection.Books[0].Explanation, Assert.Single(response.Matches).Explanation);
        Assert.Equal(new[] { "Tolkien" }, response.Matches[0].Authors);
    }

    [Fact]
    public async Task Author_only_keeps_five_selected_books_and_orders_them_by_popularity()
    {
        var calls = new List<string>();
        var books = Enumerable.Range(1, 5).Select(index => Book($"OL{index}W", index == 1 ? "Tolkien" : $"Book {index}")
            with
        { ReadingLogCount = index * 10 }).ToArray();
        var gemini = new StubGemini(calls)
        {
            SearchTerms = new(null, "Tolkien", [], null, []),
            Selection = new(books.Select(book => new SelectedBook(book.OpenLibraryWorkId, "A book by the requested author.")).ToArray())
        };
        var catalog = new StubCatalog(calls, books);

        var response = await new BookSearchService(gemini, catalog, new BookSearchValidator())
            .SearchAsync("Tolkien", default);

        Assert.Equal(new[] { "OL5W", "OL4W", "OL3W", "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Equal(5, catalog.WorkRequests.Count);
        Assert.Single(catalog.AuthorRequests);
    }

    [Fact]
    public async Task Verification_prioritizes_exact_candidates_and_checks_at_most_five_works()
    {
        var calls = new List<string>();
        var books = Enumerable.Range(1, 6).Select(index => Book($"OL{index}W", index == 6 ? "The Hobbit" : $"Book {index}")).ToArray();
        var catalog = new StubCatalog(calls, books);

        await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Equal(5, catalog.WorkRequests.Count);
        Assert.Equal("OL6W", catalog.WorkRequests[0]);
        Assert.Single(catalog.AuthorRequests);
    }

    [Fact]
    public async Task Verification_caps_distinct_author_lookups_at_ten()
    {
        var calls = new List<string>();
        var catalog = new StubCatalog(calls, [Book("OL1W", "The Hobbit")])
        {
            Works = new() { ["OL1W"] = new("OL1W", Enumerable.Range(1, 15).Select(i => $"OL{i}A").ToArray()) },
            Authors = Enumerable.Range(1, 15).ToDictionary(i => $"OL{i}A", i => (CatalogAuthor?)new CatalogAuthor($"OL{i}A", $"Writer {i}", []))
        };

        await new BookSearchService(new StubGemini(calls), catalog, new BookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Equal(10, catalog.AuthorRequests.Count);
    }

    [Fact]
    public async Task Verification_does_not_swallow_caller_cancellation()
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BookSearchService(new StubGemini(calls), new StubCatalog(calls, [Book("OL1W", "The Hobbit")]), new BookSearchValidator())
                .SearchAsync("The Hobbit", cancellation.Token));
        Assert.DoesNotContain("select", calls);
    }

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
        public List<string> WorkRequests { get; } = [];
        public List<string> AuthorRequests { get; } = [];
        public Dictionary<string, CatalogWork?> Works { get; init; } = [];
        public Dictionary<string, CatalogAuthor?> Authors { get; init; } = [];
        public CatalogFailure? WorkFailure { get; init; }
        public bool EmptyFirstSearch { get; init; }
        public CancellationToken Token { get; private set; }

        private string[] AuthorNames => books.SelectMany(book => book.Authors).Distinct().ToArray();

        public Task<CatalogWork?> GetWorkAsync(string workId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkRequests.Add(workId);
            if (WorkFailure is { } failure) throw new CatalogException(failure);
            if (Works.TryGetValue(workId, out var work)) return Task.FromResult(work);
            var book = books.First(book => book.OpenLibraryWorkId == workId);
            return Task.FromResult<CatalogWork?>(new(workId,
                book.Authors.Select(name => $"OL{Array.IndexOf(AuthorNames, name) + 1}A").ToArray()));
        }

        public Task<CatalogAuthor?> GetAuthorAsync(string authorId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorRequests.Add(authorId);
            if (Authors.TryGetValue(authorId, out var author)) return Task.FromResult(author);
            var index = int.Parse(authorId[2..^1]) - 1;
            return Task.FromResult<CatalogAuthor?>(new(authorId, AuthorNames[index], []));
        }

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
            throw new GeminiApiException(GeminiApiFailureReason.BadResponse);
        }
        public void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary)
            => throw new InvalidOperationException("Must not reach selection.");
    }
}
