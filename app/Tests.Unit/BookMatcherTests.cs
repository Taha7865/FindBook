using FindBook.Domain.Matching;
using FindBook.Domain.Models;

namespace FindBook.Tests.Unit;

public sealed class BookMatcherTests
{
    [Fact]
    public void Primary_author_exact_match_wins_over_contributor_and_near_matches()
    {
        var result = BookMatcher.Select("The Hobbit by Tolkien", Title("The Hobbit", "Tolkien"),
        [
            Book("OL1W", "The Hobbit: illustrated", "Tolkien"),
            Book("OL2W", "The Hobbit", "Tolkien", AuthorRole.Contributor),
            Book("OL3W", "The Hobbit", "Tolkien", AuthorRole.Primary)
        ]);

        Assert.True(result.HasClearWinner);
        Assert.Equal(new BookRanking("OL3W", MatchTier.ExactTitlePrimaryAuthor), Assert.Single(result.Candidates));
    }

    [Fact]
    public void Contributor_exact_match_ranks_above_near_title_and_author_fallback_without_a_winner()
    {
        var result = BookMatcher.Select("The Hobbit by Tolkien", Title("The Hobbit", "Tolkien"),
        [
            Book("OL1W", "The Silmarillion", "Tolkien"),
            Book("OL2W", "The Hobbit: illustrated", "Tolkien"),
            Book("OL3W", "The Hobbit", "Tolkien", AuthorRole.Contributor)
        ]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(new[] { MatchTier.ExactTitleContributor, MatchTier.TitleAndAuthor, MatchTier.Author },
            result.Candidates.Select(candidate => candidate.Tier));
    }

    [Theory]
    [InlineData("LES MISÉRABLES", "Les Miserables")]
    [InlineData("Harry Potter: and the Chamber of Secrets", "Harry Potter and the Chamber of Secrets")]
    [InlineData("Alice’s Adventures in Wonderland", "Alices Adventures in Wonderland")]
    public void Normalizes_case_accents_and_punctuation_for_a_unique_title(string query, string catalogTitle)
    {
        var result = BookMatcher.Select(query, Title(query),
            [Book("OL1W", "Another title"), Book("OL2W", catalogTitle)]);

        Assert.True(result.HasClearWinner);
        Assert.Equal("OL2W", Assert.Single(result.Candidates).WorkId);
    }

    [Fact]
    public void Distinct_works_with_the_same_exact_title_remain_ambiguous()
    {
        var result = BookMatcher.Select("The Raven", Title("The Raven"),
            [Book("OL1W", "The Raven", "Author One"), Book("OL2W", "The Raven", "Author Two")]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(2, result.Candidates.Length);
    }

    [Fact]
    public void Duplicate_records_of_one_work_do_not_create_ambiguity()
    {
        var result = BookMatcher.Select("The Hobbit", Title("The Hobbit"),
            [Book("OL1W", "The Hobbit"), Book("OL1W", "The Hobbit")]);

        Assert.True(result.HasClearWinner);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Two_plausible_title_hypotheses_do_not_force_one_winner()
    {
        var interpretation = new SearchInterpretation(
            [Title("Emma").Hypotheses[0], Title("Persuasion").Hypotheses[0]]);

        var result = BookMatcher.Select("Emma or Persuasion", interpretation,
            [Book("OL1W", "Emma"), Book("OL2W", "Persuasion")]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(2, result.Candidates.Length);
    }

    [Theory]
    [InlineData("huckleberry")]
    [InlineData(null)]
    public void An_expanded_or_inferred_title_cannot_become_an_exact_match(string? sourceText)
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Title,
            new("Adventures of Huckleberry Finn", sourceText), new("Mark Twain", "mark"), [], null, [])]);

        var result = BookMatcher.Select("mark huckleberry", interpretation,
            [Book("OL1W", "Adventures of Huckleberry Finn", "Mark Twain", AuthorRole.Primary)]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(MatchTier.TitleAndAuthor, Assert.Single(result.Candidates).Tier);
    }

    [Fact]
    public void A_single_search_author_does_not_establish_a_primary_role()
    {
        var result = BookMatcher.Select("The Hobbit by Tolkien", Title("The Hobbit", "Tolkien"),
            [Book("OL1W", "The Hobbit", "Tolkien")]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(MatchTier.TitleAndAuthor, Assert.Single(result.Candidates).Tier);
    }

    [Fact]
    public void An_inferred_author_does_not_resolve_duplicate_exact_titles()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Title,
            new("The Raven", "The Raven"), new("Edgar Allan Poe", null), [], null, [])]);

        var result = BookMatcher.Select("The Raven", interpretation,
        [
            Book("OL1W", "The Raven", "Edgar Allan Poe", AuthorRole.Primary),
            Book("OL2W", "The Raven", "Another Writer", AuthorRole.Primary)
        ]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(2, result.Candidates.Length);
    }

    [Fact]
    public void An_expanded_author_name_does_not_count_as_an_exact_primary_author_match()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Title,
            new("Adventures of Huckleberry Finn", "Adventures of Huckleberry Finn"), new("Mark Twain", "Mark"), [], null, [])]);

        var result = BookMatcher.Select("Adventures of Huckleberry Finn by Mark", interpretation,
            [Book("OL1W", "Adventures of Huckleberry Finn", "Mark Twain", AuthorRole.Primary)]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(MatchTier.TitleAndAuthor, Assert.Single(result.Candidates).Tier);
    }

    [Theory]
    [InlineData("Harry Pott", MatchTier.TitleAndAuthor)]
    [InlineData("Harry Pot", MatchTier.Author)]
    [InlineData("Har", MatchTier.Author)]
    [InlineData("Art", MatchTier.Author)]
    public void Partial_title_words_need_four_letters_to_match_a_prefix(string fragment, MatchTier expected)
    {
        var result = BookMatcher.Select($"{fragment} by Rowling", Title(fragment, "Rowling"),
            [Book("OL1W", "Harry Potter and the Chamber of Secrets", "Rowling")]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(expected, Assert.Single(result.Candidates).Tier);
    }

    [Fact]
    public void Author_only_returns_up_to_five_distinct_books_and_excludes_other_authors()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Author,
            null, new("J. K. Rowling", "J.K. Rolling"), [], null, [])]);
        var books = new[] { Book("OL99W", "Other book", "Other Author") }
            .Concat(Enumerable.Range(1, 7).Select(id => Book($"OL{id}W", $"Book {id}", "J. K. Rowling")))
            .ToArray();

        var result = BookMatcher.Select("J.K. Rolling", interpretation, books);

        Assert.False(result.HasClearWinner);
        Assert.Equal(new[] { "OL1W", "OL2W", "OL3W", "OL4W", "OL5W" },
            result.Candidates.Select(candidate => candidate.WorkId));
    }

    [Fact]
    public void Author_fallback_does_not_fill_remaining_places_with_unrelated_authors()
    {
        var result = BookMatcher.Select("An unknown title by Tolkien", Title("An unknown title", "Tolkien"),
            [Book("OL1W", "Unrelated book", "Other Author"), Book("OL2W", "The Hobbit", "Tolkien")]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(new BookRanking("OL2W", MatchTier.Author), Assert.Single(result.Candidates));
    }

    [Fact]
    public void Author_initials_match_with_or_without_spaces_and_periods()
    {
        var result = BookMatcher.Select("Harry Potter by JK Rowling", Title("Harry Potter", "JK Rowling"),
            [Book("OL1W", "Harry Potter", "J. K. Rowling", AuthorRole.Primary)]);

        Assert.True(result.HasClearWinner);
        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, Assert.Single(result.Candidates).Tier);
    }

    [Fact]
    public void An_exact_title_with_a_different_supplied_author_is_not_a_clear_winner()
    {
        var result = BookMatcher.Select("The Hobbit by Dickens", Title("The Hobbit", "Dickens"),
            [Book("OL1W", "The Hobbit", "Tolkien", AuthorRole.Primary)]);

        Assert.False(result.HasClearWinner);
        Assert.Equal(MatchTier.Related, Assert.Single(result.Candidates).Tier);
    }

    [Fact]
    public void Author_only_with_no_matching_authors_returns_no_candidates()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Author,
            null, new("Dickens", "Dickens"), [], null, [])]);

        var result = BookMatcher.Select("Dickens", interpretation, [Book("OL1W", "The Hobbit", "Tolkien")]);

        Assert.False(result.HasClearWinner);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Topic_search_preserves_catalog_order_and_is_repeatable()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Topic, null, null, ["dragon"], null, [])]);
        var books = new[] { Book("OL3W", "Eragon"), Book("OL1W", "Fourth Wing"), Book("OL2W", "A Game of Thrones") };

        var first = BookMatcher.Select("a book about a dragon", interpretation, books);
        var second = BookMatcher.Select("a book about a dragon", interpretation, books);

        Assert.False(first.HasClearWinner);
        Assert.Equal(new[] { "OL3W", "OL1W", "OL2W" }, first.Candidates.Select(candidate => candidate.WorkId));
        Assert.Equal(first.Candidates, second.Candidates);
    }

    [Theory]
    [InlineData(1937, false)]
    [InlineData(null, true)]
    public void Unverified_edition_constraints_prevent_a_clear_winner(int? year, bool illustrated)
    {
        var interpretation = new SearchInterpretation([Title("The Hobbit").Hypotheses[0] with
        {
            Year = year,
            EditionHints = illustrated ? ["illustrated"] : []
        }]);

        var result = BookMatcher.Select("The Hobbit illustrated 1937", interpretation, [Book("OL1W", "The Hobbit")]);

        Assert.False(result.HasClearWinner);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Empty_catalog_results_remain_empty()
    {
        var result = BookMatcher.Select("The Hobbit", Title("The Hobbit"), []);

        Assert.False(result.HasClearWinner);
        Assert.Empty(result.Candidates);
    }

    private static SearchInterpretation Title(string title, string? author = null) => new([new(
        SearchIntent.Title, new(title, title), author is null ? null : new(author, author), [], null, [])]);

    private static CatalogBook Book(string id, string title, string author = "A Writer", AuthorRole role = AuthorRole.Unknown)
        => new(id, title, [author], null, null, [])
        {
            AuthorCredits = role == AuthorRole.Unknown ? [] : [new(author, role)]
        };
}
