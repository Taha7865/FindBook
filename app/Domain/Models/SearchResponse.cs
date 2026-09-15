namespace FindBook.Domain.Models;

public sealed record SearchResponse(BookMatch[] Matches);

public sealed record BookMatch(
    string OpenLibraryWorkId,
    string Title,
    // TODO: Resolve primary authors and contributor roles from fetched work/edition data.
    string[] Authors,
    int? FirstPublishYear,
    string OpenLibraryUrl,
    string? CoverUrl,
    BookEdition[] Editions,
    string Explanation);

public sealed record BookEdition(
    string EditionId,
    string Title,
    string? PublishDate,
    string OpenLibraryUrl)
{
    public string? Subtitle { get; init; }
    public string? EditionName { get; init; }
    public string[] Contributions { get; init; } = [];
    public string? Notes { get; init; }
}
