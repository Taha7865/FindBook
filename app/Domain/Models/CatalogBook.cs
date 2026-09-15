namespace FindBook.Domain.Models;

public sealed record CatalogBook(
    string OpenLibraryWorkId,
    string Title,
    string[] Authors,
    int? FirstPublishYear,
    int? CoverId,
    CatalogEdition[] Editions)
{
    public string[] Subjects { get; init; } = [];
    public int? ReadingLogCount { get; init; }
}

public sealed record CatalogEdition(string EditionId, string Title, string? PublishDate)
{
    public string? Subtitle { get; init; }
    public string? EditionName { get; init; }
    public string[] Contributions { get; init; } = [];
    public string? Notes { get; init; }
}
