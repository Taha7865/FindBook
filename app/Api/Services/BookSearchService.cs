using FindBook.Api.Contracts;
using FindBook.Domain.Interfaces;

namespace FindBook.Api.Services;

public sealed class BookSearchService(IBookCatalog catalog)
{
    public async Task<SearchResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var candidates = await catalog.SearchAsync(query.Trim(), cancellationToken);

        // Preserve catalog relevance until the matching rules are implemented.
        var matches = candidates.GroupBy(book => book.WorkId).Take(5).Select(group =>
        {
            var book = group.First();
            var editions = group.SelectMany(item => item.Editions)
                .DistinctBy(edition => edition.EditionId)
                .Select(edition => new BookEdition(edition.EditionId, edition.Title,
                    edition.PublishDate, $"https://openlibrary.org/books/{edition.EditionId}"))
                .ToArray();

            return new BookMatch(book.WorkId, book.Title,
                group.SelectMany(item => item.Authors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                book.FirstPublishYear, $"https://openlibrary.org/works/{book.WorkId}",
                book.CoverId is { } coverId ? $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg?default=false" : null,
                editions, $"Open Library returned \"{book.Title}\" in its relevance-ordered search results.");
        }).ToArray();

        return new SearchResponse(matches);
    }
}
