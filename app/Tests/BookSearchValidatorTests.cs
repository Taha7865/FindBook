using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests;

public sealed class BookSearchValidatorTests
{
    private readonly IBookSearchValidator _validator = new BookSearchValidator();
    private static readonly CatalogBook[] Books = [new("OL1W", "The Hobbit", ["Tolkien"], 1937, null, [])];

    [Fact]
    public void Rejects_missing_or_oversized_fields()
    {
        var valid = new BookSearchTerms("The Hobbit", null, [], null, []);
        BookSearchTerms[] invalid =
        [
            null!, valid with { Title = " " }, valid with { Author = new string('a', 201) },
            valid with { Keywords = null! }, valid with { Keywords = [null!] },
            valid with { Keywords = Enumerable.Repeat("word", 9).ToArray() },
            valid with { EditionKeywords = [new string('x', 101)] }, valid with { EditionYear = 0 }, valid with { FirstPublishYear = 0 }, valid with { FirstPublishYear = 10000 }
        ];
        foreach (var terms in invalid)
            Assert.Throws<GeminiApiException>(() => _validator.ValidateSearchTerms(terms));
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
            Assert.Throws<GeminiApiException>(() => _validator.ValidateSelection(selection, Books));
    }
}
