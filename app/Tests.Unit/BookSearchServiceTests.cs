using FindBook.Api.Services;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests.Unit;

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
        var service = new BookSearchService(gemini, catalog, new StubBookSearchValidator());

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

    [Theory]
    [InlineData("  THE HOBBIT!!  ", "The Hobbit")]
    [InlineData("\"The   Hobbit\"", "The Hobbit")]
    [InlineData("les miserables", "Les Misérables")]
    public async Task A_unique_normalized_title_among_accepted_books_returns_one_match(string query, string title)
    {
        var gemini = new StubGeminiApiClient(new(title, null, [], null, []), Select("OL1W", "OL2W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "A companion book"), Book("OL2W", title)]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync(query, default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Equal("Explanation for OL2W.", response.Matches[0].Explanation);
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

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit", default);

        var match = Assert.Single(response.Matches);
        Assert.Equal("OL1W", match.OpenLibraryWorkId);
        Assert.Equal("The supplied subjects identify the novel.", match.Explanation);
        Assert.Single(catalog.Searches);
    }

    [Fact]
    public async Task A_rejected_exact_work_author_match_is_not_added_to_geminis_selection()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select("OL2W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit"), Book("OL2W", "The Hobbit: A Tale")])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A"]) },
            Authors = { ["OL1A"] = new("OL1A", "Tolkien", []) }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
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

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal("OL2W", Assert.Single(response.Matches).OpenLibraryWorkId);
        Assert.Equal("Explanation for OL2W.", response.Matches[0].Explanation);
        Assert.Equal("Tolkien", gemini.SelectionRequests[0].Books[1].WorkAuthors[0].Name);
    }

    [Theory]
    [InlineData("The Hobbit by Tolkien")]
    [InlineData("The Hobbit (Tolkien)")]
    public async Task A_fetched_author_alias_can_establish_an_exact_match_among_accepted_books(string query)
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select("OL2W", "OL1W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit"), Book("OL2W", "A companion book")])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A"]) },
            Authors = { ["OL1A"] = new("OL1A", "J. R. R. Tolkien", ["Tolkien"]) }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync(query, default);

        Assert.Equal("OL1W", Assert.Single(response.Matches).OpenLibraryWorkId);
    }

    [Fact]
    public async Task A_search_author_name_alone_does_not_establish_verified_author_priority()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select("OL2W", "OL1W"));
        var catalog = new StubOpenLibraryApiClient(
            [Book("OL1W", "The Hobbit") with { Authors = ["Tolkien"] }, Book("OL2W", "A related title")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.All(gemini.SelectionRequests[0].Books, book => Assert.Empty(book.WorkAuthors));
    }

    [Fact]
    public async Task Multiple_accepted_exact_work_author_matches_remain_multiple_despite_different_popularity()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select("OL3W", "OL1W", "OL2W"));
        var catalog = new StubOpenLibraryApiClient(
        [
            Book("OL1W", "The Hobbit") with { ReadingLogCount = 10 },
            Book("OL2W", "The Hobbit") with { ReadingLogCount = 20 },
            Book("OL3W", "A companion book")
        ])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A"]), ["OL2W"] = new("OL2W", ["OL1A"]) },
            Authors = { ["OL1A"] = new("OL1A", "Tolkien", []) }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Single(catalog.AuthorRequests);
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

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit", default);

        Assert.Equal(expectedIds, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.All(response.Matches, book => Assert.Equal($"Explanation for {book.OpenLibraryWorkId}.", book.Explanation));
    }

    [Theory]
    [InlineData("hobbit", "The Hobbit", "Tolkien", null)]
    [InlineData("a book about a dragon", null, null, null)]
    [InlineData("The Hobbit 1937 illustrated edition", "The Hobbit", "Tolkien", 1937)]
    [InlineData("books like The Hobbit", "The Hobbit", null, null)]
    public async Task Partial_topic_and_edition_queries_keep_geminis_order_and_explanations(
        string query, string? title, string? author, int? year)
    {
        var terms = new BookSearchTerms(title, author, title is null ? ["dragons"] : [], year, year is not null ? ["illustrated"] : []);
        var gemini = new StubGeminiApiClient(terms, Select("OL2W", "OL1W"));
        var catalog = new StubOpenLibraryApiClient(
            [Book("OL1W", "The Hobbit") with { ReadingLogCount = 900 }, Book("OL2W", "Another possibility")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync(query, default);

        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Equal("Explanation for OL2W.", response.Matches[0].Explanation);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Author_only_returns_accepted_books_by_popularity_without_filling_unused_slots(int selectedCount)
    {
        var books = Enumerable.Range(1, 6).Select(index => Book($"OL{index}W", index == 1 ? "Tolkien" : $"Book {index}")
            with
        { Authors = ["Tolkien"], ReadingLogCount = index * 10 }).ToArray();
        var selectedIds = books.Take(selectedCount).Select(book => book.OpenLibraryWorkId).ToArray();
        var gemini = new StubGeminiApiClient(new(null, "Tolkien", [], null, []), Select(selectedIds));
        var catalog = new StubOpenLibraryApiClient(books);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("Tolkien", default);

        Assert.Equal(selectedIds.Reverse(), response.Matches.Select(book => book.OpenLibraryWorkId));
        Assert.Single(catalog.Searches);
    }

    [Fact]
    public async Task Empty_catalog_triggers_one_author_search_and_orders_its_accepted_books_by_popularity()
    {
        var gemini = new StubGeminiApiClient(new("Unknown title", "Tolkien", ["a clue"], null, []), Select("OL1W", "OL2W"));
        var catalog = new StubOpenLibraryApiClient([],
            [Book("OL1W", "The Hobbit") with { ReadingLogCount = 10 }, Book("OL2W", "The Silmarillion") with { ReadingLogCount = 20 }]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("unknown title tolkien", default);

        Assert.Equal(2, catalog.Searches.Count);
        Assert.Null(catalog.Searches[1].Title);
        Assert.Equal("Tolkien", catalog.Searches[1].Author);
        Assert.Empty(catalog.Searches[1].Keywords);
        Assert.Equal("unknown title tolkien", Assert.Single(gemini.SelectionRequests).Query);
        Assert.Equal(new[] { "OL2W", "OL1W" }, response.Matches.Select(book => book.OpenLibraryWorkId));
    }

    [Fact]
    public async Task Gemini_accepting_no_books_triggers_a_separate_author_search_and_selection_of_the_new_pool()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]),
            Select(), Select("OL2W", "OL3W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")],
            [Book("OL2W", "The Silmarillion") with { ReadingLogCount = 10 }, Book("OL3W", "Unfinished Tales") with { ReadingLogCount = 20 }]);
        var validator = new StubBookSearchValidator();

        var response = await new BookSearchService(gemini, catalog, validator).SearchAsync("The Hobbit by Tolkien deluxe 1937", default);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gemini_rejecting_the_author_fallback_ends_the_search_without_restoring_rejected_books(bool initialCatalogEmpty)
    {
        var selections = initialCatalogEmpty ? new[] { Select() } : new[] { Select(), Select() };
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), selections);
        var catalog = new StubOpenLibraryApiClient(initialCatalogEmpty ? [] : [Book("OL1W", "The Hobbit")],
            [Book("OL1W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.Empty(response.Matches);
        Assert.Equal(2, catalog.Searches.Count);
        Assert.Equal(selections.Length, gemini.SelectionRequests.Count);
    }

    [Theory]
    [InlineData("The Hobbit", "The Hobbit", null)]
    [InlineData("Tolkien", null, "Tolkien")]
    [InlineData("a book about dragons", null, null)]
    public async Task Rejected_title_only_author_only_and_topic_searches_do_not_attempt_an_author_fallback(
        string query, string? title, string? author)
    {
        var terms = new BookSearchTerms(title, author, title is null && author is null ? ["dragons"] : [], null, []);
        var gemini = new StubGeminiApiClient(terms, Select());
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync(query, default);

        Assert.Empty(response.Matches);
        Assert.Single(catalog.Searches);
        Assert.Single(gemini.SelectionRequests);
    }

    [Fact]
    public async Task Empty_author_fallback_skips_another_gemini_selection()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select());
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "A different book")], []);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("hobbit tolkien", default);

        Assert.Empty(response.Matches);
        Assert.Equal(2, catalog.Searches.Count);
        Assert.Single(gemini.SelectionRequests);
    }

    [Fact]
    public async Task Empty_catalog_without_an_author_returns_no_matches_without_asking_gemini_to_select()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, []));
        var catalog = new StubOpenLibraryApiClient(Array.Empty<CatalogBook>());

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Empty(response.Matches);
        Assert.Empty(gemini.SelectionRequests);
    }

    [Fact]
    public async Task A_query_with_no_searchable_terms_does_not_call_open_library()
    {
        var gemini = new StubGeminiApiClient(new(null, null, [], null, []));
        var catalog = new StubOpenLibraryApiClient();

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("hello", default);

        Assert.Empty(response.Matches);
        Assert.Empty(catalog.Searches);
    }

    [Fact]
    public async Task Missing_edition_tries_the_same_book_without_edition_filters_before_broadening_to_the_author()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]), Select("OL1W"));
        var catalog = new StubOpenLibraryApiClient([], [], [Book("OL1W", "The Silmarillion")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
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
    public async Task A_book_found_after_relaxing_edition_filters_keeps_the_requested_edition_caveat()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], 1937, ["deluxe"]),
            new BookSelection([new("OL1W", "The book matches, but the requested edition is unverified.")]));
        var catalog = new StubOpenLibraryApiClient([], [Book("OL1W", "The Hobbit")]);

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("tolkien hobbit deluxe 1937", default);

        Assert.Equal(2, catalog.Searches.Count);
        Assert.Equal("The book matches, but the requested edition is unverified.", Assert.Single(response.Matches).Explanation);
    }

    [Fact]
    public async Task Response_uses_work_authors_instead_of_search_contributors_and_keeps_edition_roles()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, ["illustrated"]), Select("OL1W"));
        var catalog = new StubOpenLibraryApiClient(
        [
            Book("OL1W", "The Hobbit") with
            {
                Authors = ["Tolkien", "An Illustrator"],
                Editions = [new("OL1M", "The Hobbit", "2000") { Contributions = ["An Illustrator (Illustrator)"] }]
            }
        ])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A", "OL2A"]) },
            Authors =
            {
                ["OL1A"] = new("OL1A", "J. R. R. Tolkien", ["Tolkien"]),
                ["OL2A"] = new("OL2A", "J. R. R. Tolkien", [])
            }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit illustrated", default);

        var match = Assert.Single(response.Matches);
        Assert.Equal(new[] { "J. R. R. Tolkien" }, match.Authors);
        Assert.Equal(new[] { "An Illustrator (Illustrator)" }, Assert.Single(match.Editions).Contributions);
    }

    [Fact]
    public async Task Response_keeps_all_resolved_work_authors()
    {
        var gemini = new StubGeminiApiClient(new("Good Omens", null, [], null, []), Select("OL1W"));
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "Good Omens") with { Authors = ["A Contributor"] }])
        {
            Works = { ["OL1W"] = new("OL1W", ["OL1A", "OL2A"]) },
            Authors = { ["OL1A"] = new("OL1A", "Terry Pratchett", []), ["OL2A"] = new("OL2A", "Neil Gaiman", []) }
        };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("Good Omens", default);

        Assert.Equal(new[] { "Terry Pratchett", "Neil Gaiman" }, Assert.Single(response.Matches).Authors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_work_author_details_keep_listed_names_without_changing_geminis_explanation(bool lookupFails)
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []),
            new BookSelection([new("OL1W", "The title and listed name are a possible match.")]));
        var catalog = new StubOpenLibraryApiClient(
            [Book("OL1W", "The Hobbit") with { Authors = ["Tolkien"] }, Book("OL2W", "Another title")])
        { WorkException = lookupFails ? new CatalogException(CatalogFailure.Unavailable) : null };

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
            .SearchAsync("The Hobbit by Tolkien", default);

        Assert.All(gemini.SelectionRequests[0].Books, book => Assert.Empty(book.WorkAuthors));
        var match = Assert.Single(response.Matches);
        Assert.Equal(new[] { "Tolkien" }, match.Authors);
        Assert.Equal("The title and listed name are a possible match.", match.Explanation);
        Assert.Equal(lookupFails ? 1 : 2, catalog.WorkRequests.Count);
    }

    [Fact]
    public async Task Verification_checks_at_most_five_likely_works_and_reuses_a_shared_author_record()
    {
        var books = Enumerable.Range(1, 6).Select(index => Book($"OL{index}W", index == 6 ? "The Hobbit" : $"Book {index}")).ToArray();
        var catalog = new StubOpenLibraryApiClient(books);
        foreach (var book in books)
            catalog.Works[book.OpenLibraryWorkId] = new(book.OpenLibraryWorkId, ["OL1A"]);
        catalog.Authors["OL1A"] = new("OL1A", "Tolkien", []);
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, []), Select("OL6W"));

        await new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("The Hobbit", default);

        Assert.Equal(5, catalog.WorkRequests.Count);
        Assert.Equal("OL6W", catalog.WorkRequests[0]);
        Assert.Single(catalog.AuthorRequests);
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

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
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

        var response = await new BookSearchService(gemini, catalog, new StubBookSearchValidator())
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
            new BookSearchService(gemini, catalog, new StubBookSearchValidator()).SearchAsync("The Hobbit", cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.All(gemini.Tokens.Concat(catalog.Tokens), token => Assert.Equal(cancellation.Token, token));
        Assert.Empty(gemini.SelectionRequests);
        Assert.Single(catalog.Searches);
    }

    [Fact]
    public async Task The_injected_validator_can_stop_the_search_before_catalog_access()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", null, [], null, []));
        var catalog = new StubOpenLibraryApiClient();
        var validator = new StubBookSearchValidator { SearchTermsException = new GeminiApiException(GeminiApiFailureReason.BadResponse) };

        await Assert.ThrowsAsync<GeminiApiException>(() =>
            new BookSearchService(gemini, catalog, validator).SearchAsync("The Hobbit", default));

        Assert.Single(validator.ValidatedTerms);
        Assert.Empty(catalog.Searches);
    }

    [Fact]
    public async Task The_injected_validator_rejecting_a_selection_is_an_error_not_a_reason_to_search_again()
    {
        var gemini = new StubGeminiApiClient(new("The Hobbit", "Tolkien", [], null, []), Select());
        var catalog = new StubOpenLibraryApiClient([Book("OL1W", "The Hobbit")]);
        var validator = new StubBookSearchValidator { SelectionException = new GeminiApiException(GeminiApiFailureReason.BadResponse) };

        await Assert.ThrowsAsync<GeminiApiException>(() =>
            new BookSearchService(gemini, catalog, validator).SearchAsync("The Hobbit", default));

        Assert.Single(validator.ValidatedSelections);
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
        public List<string> WorkRequests { get; } = [];
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
            WorkRequests.Add(workId);
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
        public Exception? SearchTermsException { get; init; }
        public Exception? SelectionException { get; init; }
        public List<BookSearchTerms> ValidatedTerms { get; } = [];
        public List<(BookSelection Selection, IReadOnlyList<CatalogBook> Books)> ValidatedSelections { get; } = [];

        public void ValidateSearchTerms(BookSearchTerms searchTerms)
        {
            ValidatedTerms.Add(searchTerms);
            if (SearchTermsException is not null) throw SearchTermsException;
        }

        public void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary)
        {
            ValidatedSelections.Add((selection, booksFromOpenLibrary));
            if (SelectionException is not null) throw SelectionException;
        }
    }
}
