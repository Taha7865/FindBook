namespace FindBook.Domain.Models;

public sealed record CatalogBook(
    string WorkId,
    string Title,
    string[] Authors,
    int? FirstPublishYear,
    int? CoverId,
    CatalogEdition[] Editions)
{
    // TODO: Populate only from fetched role evidence; search author_name does not establish roles.
    public CatalogAuthorCredit[] AuthorCredits { get; init; } = [];
}

public enum AuthorRole { Unknown, Primary, Contributor }

public sealed record CatalogAuthorCredit(string Name, AuthorRole Role);

public sealed record CatalogEdition(string EditionId, string Title, string? PublishDate);
