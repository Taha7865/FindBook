namespace FindBook.Domain.Models;

public sealed record CatalogBook(
    string WorkId,
    string Title,
    string[] Authors,
    int? FirstPublishYear,
    int? CoverId,
    CatalogEdition[] Editions);

public sealed record CatalogEdition(string EditionId, string Title, string? PublishDate);
