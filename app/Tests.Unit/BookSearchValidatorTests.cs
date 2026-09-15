using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests.Unit;

public sealed class BookSearchValidatorTests
{
    private readonly IBookSearchValidator _validator = new BookSearchValidator();
    private static readonly CatalogBook[] Books = [new("OL1W", "The Hobbit", ["Tolkien"], 1937, null, [])];

    [Fact]
    public void Accepts_search_terms_and_an_empty_selection_without_deciding_their_meaning()
    {
        _validator.ValidateSearchTerms(new("1984", null, [], null, []));
        _validator.ValidateSearchTerms(new(null, "J. K. Rowling", [], null, []));
        _validator.ValidateSearchTerms(new(null, null, [], null, []));
        _validator.ValidateSelection(new([]), Books);
        _validator.ValidateSelection(new([new("OL1W", "The title matches the query.")]), Books);
    }

    [Fact]
    public void Rejects_missing_or_oversized_fields()
    {
        var valid = new BookSearchTerms("The Hobbit", null, [], null, []);
        BookSearchTerms[] invalid =
        [
            null!, valid with { Title = " " }, valid with { Author = new string('a', 201) },
            valid with { Keywords = null! }, valid with { Keywords = [null!] },
            valid with { Keywords = Enumerable.Repeat("word", 9).ToArray() },
            valid with { EditionKeywords = [new string('x', 101)] }, valid with { EditionYear = 0 }
        ];
        foreach (var terms in invalid)
            Assert.Throws<BookSearchAiException>(() => _validator.ValidateSearchTerms(terms));
    }

    [Theory]
    [InlineData("OL999W")]
    [InlineData("ol1w")]
    [InlineData("https://example.com/OL1W")]
    public void Rejects_ids_that_were_not_in_the_supplied_catalog(string id)
    {
        var error = Assert.Throws<BookSearchAiException>(() =>
            _validator.ValidateSelection(new([new(id, "An explanation.")]), Books));
        Assert.Equal(BookSearchAiFailure.BadResponse, error.Failure);
    }

    [Fact]
    public void Rejects_duplicates_overlong_explanations_and_invalid_lists()
    {
        var book = new SelectedBook("OL1W", "The title matches.");
        BookSelection[] invalid =
        [
            null!, new(null!), new([null!]), new([book, book]),
            new(Enumerable.Repeat(book, 6).ToArray()),
            new([book with { Explanation = " " }]), new([book with { Explanation = new string('x', 501) }])
        ];
        foreach (var selection in invalid)
            Assert.Throws<BookSearchAiException>(() => _validator.ValidateSelection(selection, Books));
    }
}
