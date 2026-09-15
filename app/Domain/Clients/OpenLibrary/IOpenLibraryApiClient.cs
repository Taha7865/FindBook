using FindBook.Domain.Models;

namespace FindBook.Domain.Clients.OpenLibrary;

public interface IOpenLibraryApiClient
{
    Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken);
}
