using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;

namespace FindBook.Domain.Validators;

public sealed class BookSearchValidator : IBookSearchValidator
{
    public void ValidateSearchSuggestions(BookSearchSuggestions suggestions)
    {
        if (suggestions?.Searches is null || suggestions.Searches.Length > BookSearchSuggestions.MaximumSearches)
            throw new GeminiApiException(GeminiApiFailureReason.BadResponse);

        foreach (var searchTerms in suggestions.Searches)
        {
            if (searchTerms is null || !IsOptionalText(searchTerms.Title) || !IsOptionalText(searchTerms.Author)
                || !IsTextList(searchTerms.Keywords, 8) || !IsTextList(searchTerms.EditionKeywords, 5)
                || searchTerms.EditionYear is < 1 or > 9999)
                throw new GeminiApiException(GeminiApiFailureReason.BadResponse);
        }
    }

    public void ValidateSelection(BookSelection selection, IReadOnlyList<CatalogBook> booksFromOpenLibrary)
    {
        if (selection?.Books is null || selection.Books.Length > 5)
            throw new GeminiApiException(GeminiApiFailureReason.BadResponse);

        var availableIds = booksFromOpenLibrary.Select(book => book.OpenLibraryWorkId).ToHashSet(StringComparer.Ordinal);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var book in selection.Books)
        {
            if (book is null || string.IsNullOrWhiteSpace(book.OpenLibraryWorkId)
                || !availableIds.Contains(book.OpenLibraryWorkId) || !selectedIds.Add(book.OpenLibraryWorkId)
                || string.IsNullOrWhiteSpace(book.Explanation) || book.Explanation.Length > 500)
                throw new GeminiApiException(GeminiApiFailureReason.BadResponse);
        }
    }

    private static bool IsOptionalText(string? value)
        => value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= 200);

    private static bool IsTextList(string[]? values, int maximumCount)
        => values is not null && values.Length <= maximumCount
            && values.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 100);
}
