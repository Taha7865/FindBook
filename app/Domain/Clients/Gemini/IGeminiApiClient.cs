using FindBook.Domain.Models;

namespace FindBook.Domain.Clients.Gemini;

public interface IGeminiApiClient
{
    Task<BookSearchTerms> ExtractSearchTermsAsync(string userQuery, CancellationToken cancellationToken);
    Task<BookSelection> SelectBooksAsync(string userQuery, IReadOnlyList<CatalogBook> booksFromOpenLibrary,
        CancellationToken cancellationToken);
}
