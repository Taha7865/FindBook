using FindBook.Domain.Models;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Validators;

namespace FindBook.Api.Services;

public sealed class BookSearchService(IGeminiApiClient gemini, IOpenLibraryApiClient openLibrary, IBookSearchValidator validator)
{
    public async Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var userQuery = query.Trim();
        var searchTerms = await gemini.ExtractSearchTermsAsync(userQuery, cancellationToken);
        validator.ValidateSearchTerms(searchTerms);
        if (searchTerms.Title is null && searchTerms.Author is null && searchTerms.Keywords.Length == 0)
            return new SearchResponse([]);

        var catalogResults = await openLibrary.SearchAsync(searchTerms, cancellationToken);
        if (catalogResults.Count == 0 && searchTerms.Title is not null && searchTerms.Author is not null)
        {
            // One broader catalog search supplies candidates for the author fallback.
            var authorSearch = searchTerms with { Title = null, Keywords = [] };
            catalogResults = await openLibrary.SearchAsync(authorSearch, cancellationToken);
        }

        var booksFromOpenLibrary = catalogResults.GroupBy(book => book.OpenLibraryWorkId).Select(group =>
        {
            var book = group.First();
            return book with
            {
                Authors = group.SelectMany(item => item.Authors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Editions = group.SelectMany(item => item.Editions).DistinctBy(edition => edition.EditionId).ToArray(),
                Subjects = group.SelectMany(item => item.Subjects).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
                ReadingLogCount = group.Max(item => item.ReadingLogCount)
            };
        }).ToArray();
        if (booksFromOpenLibrary.Length == 0)
            return new SearchResponse([]);

        var selectedBooks = await gemini.SelectBooksAsync(userQuery, booksFromOpenLibrary, cancellationToken);
        validator.ValidateSelection(selectedBooks, booksFromOpenLibrary);

        var booksById = booksFromOpenLibrary.ToDictionary(book => book.OpenLibraryWorkId);
        var matches = selectedBooks.Books.Select(selection =>
        {
            var book = booksById[selection.OpenLibraryWorkId];
            var editions = book.Editions
                .Select(edition => new BookEdition(edition.EditionId, edition.Title,
                    edition.PublishDate, $"https://openlibrary.org/books/{edition.EditionId}"))
                .ToArray();

            return new BookMatch(book.OpenLibraryWorkId, book.Title, book.Authors,
                book.FirstPublishYear, $"https://openlibrary.org/works/{book.OpenLibraryWorkId}",
                book.CoverId is { } coverId ? $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg?default=false" : null,
                editions, selection.Explanation);
        }).ToArray();

        return new SearchResponse(matches);
    }
}
