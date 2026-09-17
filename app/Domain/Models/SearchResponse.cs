namespace FindBook.Domain.Models;

public sealed record SearchResponse(BookMatch[] Matches);

public sealed record BookMatch(
    string OpenLibraryWorkId,
    string Title,
    // Work-author names when available; otherwise listed search names with no verified role.
    string[] Authors,
    int? FirstPublishYear,
    string OpenLibraryUrl,
    string? CoverUrl,
    BookEdition[] Editions,
    string Explanation)
{
    // Null means primary authorship was not established, not that the book has no author.
    public string? PrimaryAuthor { get; init; }
}

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
