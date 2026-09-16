using System.Text.Json.Serialization;

namespace FindBook.Domain.Models;

// Each item uses the same fields as one Open Library search.
public sealed record BookSearchSuggestions([property: JsonRequired] BookSearchTerms[] Searches)
{
    // Alternative queries and the existing fallbacks share this limit.
    public const int MaximumSearches = 3;
}
