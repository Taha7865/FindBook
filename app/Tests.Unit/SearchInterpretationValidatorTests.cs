using System.ComponentModel.DataAnnotations;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Tests.Unit;

public sealed class SearchInterpretationValidatorTests
{
    private readonly ISearchInterpretationValidator _validator = new SearchInterpretationValidator();

    [Fact]
    public void Corrected_values_keep_the_original_fragment()
    {
        _validator.Validate("books by J.K. Rolling", new([new(SearchIntent.Author,
            null, new("J. K. Rowling", "J.K. Rolling"), [], null, [])]));
    }

    [Fact]
    public void Rejects_fabricated_source_text()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Title,
            new("Adventures of Huckleberry Finn", "Adventures of Huckleberry Finn"), null, [], null, [])]);

        Assert.Throws<ValidationException>(() => _validator.Validate("mark huckleberry", interpretation));
    }

    [Fact]
    public void Rejects_a_suggested_title_in_an_author_only_search()
    {
        var interpretation = new SearchInterpretation([new(SearchIntent.Author,
            new("Harry Potter", null), new("J. K. Rowling", "J.K. Rolling"), [], null, [])]);

        Assert.Throws<ValidationException>(() => _validator.Validate("J.K. Rolling", interpretation));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Requires_one_or_two_hypotheses(int count)
    {
        var hypothesis = new SearchHypothesis(SearchIntent.Title, new("1984", "1984"), null, [], null, []);

        Assert.Throws<ValidationException>(() => _validator.Validate("1984",
            new(Enumerable.Repeat(hypothesis, count).ToArray())));
    }

    [Fact]
    public void Numeric_titles_do_not_require_a_year()
    {
        _validator.Validate("1984", new([new(SearchIntent.Title,
            new("1984", "1984"), null, [], null, [])]));
    }

    [Theory]
    [InlineData(1937)]
    [InlineData(0)]
    [InlineData(10000)]
    public void Rejects_unsupported_or_invented_years(int year)
    {
        Assert.Throws<ValidationException>(() => _validator.Validate("The Hobbit", new([new(
            SearchIntent.Title, new("The Hobbit", "The Hobbit"), null, [], year, [])])));
    }

    [Fact]
    public void Rejects_incomplete_or_unbounded_ai_values()
    {
        var valid = new SearchHypothesis(SearchIntent.Title, new("The Hobbit", "The Hobbit"), null, [], null, []);
        SearchHypothesis[] invalid =
        [
            valid with { Intent = (SearchIntent)99 },
            valid with { Title = null },
            valid with { Title = new(" ", null) },
            valid with { Title = new(new string('x', 201), null) },
            valid with { Keywords = null! },
            valid with { Keywords = [""] },
            valid with { Keywords = [null!] },
            valid with { Keywords = Enumerable.Repeat("keyword", 9).ToArray() },
            valid with { EditionHints = [new string('x', 101)] },
            valid with { Intent = SearchIntent.Topic, Keywords = [] },
            null!
        ];

        foreach (var hypothesis in invalid)
            Assert.Throws<ValidationException>(() => _validator.Validate("The Hobbit", new([hypothesis])));

        Assert.Throws<ValidationException>(() => _validator.Validate("The Hobbit", null!));
        Assert.Throws<ValidationException>(() => _validator.Validate("The Hobbit", new(null!)));
    }
}
