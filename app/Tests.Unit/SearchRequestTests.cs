using System.ComponentModel.DataAnnotations;
using FindBook.Domain.Models;

namespace FindBook.Tests.Unit;

public sealed class SearchRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Rejects_missing_or_blank_query(string? query)
    {
        Assert.False(IsValid(new SearchRequest { Query = query! }));
    }

    [Fact]
    public void Rejects_omitted_query()
    {
        Assert.False(IsValid(new SearchRequest()));
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Enforces_query_length_limit(int length, bool expected)
    {
        Assert.Equal(expected, IsValid(new SearchRequest { Query = new string('a', length) }));
    }

    [Theory]
    [InlineData("J.K. Rolling")]
    [InlineData("García Márquez — One Hundred Years of Solitude")]
    public void Accepts_book_text_with_punctuation_and_accents(string query)
    {
        Assert.True(IsValid(new SearchRequest { Query = query }));
    }

    private static bool IsValid(SearchRequest request)
    {
        return Validator.TryValidateObject(request, new ValidationContext(request), [], true);
    }
}
