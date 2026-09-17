using System.Text.Json.Serialization;

namespace FindBook.Domain.Models;

public sealed record BookSearchTerms(
    [property: JsonRequired] string? Title,
    [property: JsonRequired] string? Author,
    [property: JsonRequired] string[] Keywords,
    [property: JsonRequired] int? EditionYear,
    [property: JsonRequired] string[] EditionKeywords)
{
    [JsonRequired]
    public int? FirstPublishYear { get; init; }
}
