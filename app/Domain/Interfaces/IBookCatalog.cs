using FindBook.Domain.Models;

namespace FindBook.Domain.Interfaces;

public interface IBookCatalog
{
    Task<IReadOnlyList<CatalogBook>> SearchAsync(string query, CancellationToken cancellationToken);
}
