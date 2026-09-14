namespace FindBook.Api.Contracts;

public sealed record SearchResponse(BookMatch[] Matches);

public sealed record BookMatch(
    string WorkId,
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
    string OpenLibraryUrl);
