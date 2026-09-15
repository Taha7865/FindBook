using System.Text;
using System.Text.RegularExpressions;
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
        if (catalogResults.Count == 0 && (searchTerms.EditionYear is not null || searchTerms.EditionKeywords.Length > 0))
        {
            // Keep the requested book as a possibility when its specific edition cannot be found.
            var workSearch = searchTerms with { EditionYear = null, EditionKeywords = [] };
            catalogResults = await openLibrary.SearchAsync(workSearch, cancellationToken);
        }
        if (catalogResults.Count == 0 && searchTerms.Title is not null && searchTerms.Author is not null)
        {
            // One broader catalog search supplies candidates for the author fallback.
            var authorSearch = searchTerms with { Title = null, Keywords = [], EditionYear = null, EditionKeywords = [] };
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
        selectedBooks = PrioritizeExactMatches(userQuery, selectedBooks, booksFromOpenLibrary);

        var booksById = booksFromOpenLibrary.ToDictionary(book => book.OpenLibraryWorkId);
        var matches = selectedBooks.Books.Select(selection =>
        {
            var book = booksById[selection.OpenLibraryWorkId];
            var editions = book.Editions
                .Select(edition => new BookEdition(edition.EditionId, edition.Title,
                    edition.PublishDate, $"https://openlibrary.org/books/{edition.EditionId}")
                {
                    Subtitle = edition.Subtitle,
                    EditionName = edition.EditionName,
                    Contributions = edition.Contributions,
                    Notes = edition.Notes
                })
                .ToArray();

            return new BookMatch(book.OpenLibraryWorkId, book.Title, book.Authors,
                book.FirstPublishYear, $"https://openlibrary.org/works/{book.OpenLibraryWorkId}",
                book.CoverId is { } coverId ? $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg?default=false" : null,
                editions, selection.Explanation);
        }).ToArray();

        return new SearchResponse(matches);
    }

    private static BookSelection PrioritizeExactMatches(string userQuery, BookSelection selectedBooks,
        IReadOnlyList<CatalogBook> booksFromOpenLibrary)
    {
        var normalizedQuery = NormalizeForExactMatch(userQuery);
        if (normalizedQuery.Length == 0)
            return selectedBooks;

        var exactMatches = new List<SelectedBook>();
        foreach (var book in booksFromOpenLibrary)
        {
            // Compare the whole query: an inferred title or extra edition clues must not qualify.
            var matchesTitle = normalizedQuery == NormalizeForExactMatch(book.Title);
            var matchesTitleAndAuthor = book.Authors.Any(author =>
                normalizedQuery == NormalizeForExactMatch($"{book.Title} by {author}")
                || normalizedQuery == NormalizeForExactMatch($"{book.Title} {author}"));
            if (matchesTitle || matchesTitleAndAuthor)
                exactMatches.Add(new SelectedBook(book.OpenLibraryWorkId, matchesTitle
                    ? "The query matches this book's title."
                    // TODO: Verify primary-author roles; authors[] only establishes a listed author.
                    : "The query matches this book's title and a listed author."));
        }

        return new BookSelection(exactMatches.Concat(selectedBooks.Books)
            .DistinctBy(book => book.OpenLibraryWorkId).Take(5).ToArray());
    }

    private static string NormalizeForExactMatch(string value)
    {
        var withoutAccents = Regex.Replace(value.Normalize(NormalizationForm.FormD), @"\p{Mn}", "");
        return Regex.Replace(withoutAccents.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    }
}
