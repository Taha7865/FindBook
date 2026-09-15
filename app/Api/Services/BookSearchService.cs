using System.Text;
using System.Text.RegularExpressions;
using FindBook.Domain.Models;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Validators;
using FindBook.Domain.Exceptions;

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

        booksFromOpenLibrary = await VerifyWorkAuthorsAsync(userQuery, searchTerms, booksFromOpenLibrary, cancellationToken);
        var selectedBooks = await gemini.SelectBooksAsync(userQuery, booksFromOpenLibrary, cancellationToken);
        validator.ValidateSelection(selectedBooks, booksFromOpenLibrary);
        selectedBooks = SelectFinalMatches(userQuery, searchTerms, selectedBooks, booksFromOpenLibrary);

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

    // Check up to five likely candidates. Reuse author records within this request and cap new author lookups at ten.
    // These limits keep verification small; unchecked or missing records remain unverified.
    private async Task<CatalogBook[]> VerifyWorkAuthorsAsync(string userQuery, BookSearchTerms searchTerms,
        CatalogBook[] books, CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeForExactMatch(userQuery);
        var normalizedTitle = NormalizeForExactMatch(searchTerms.Title ?? "");
        var candidates = books.Select((book, index) => (Book: book, Index: index))
            .OrderByDescending(item => NormalizeForExactMatch(item.Book.Title) == normalizedQuery
                || QueryMatchesTitleAndAuthor(normalizedQuery, item.Book.Title, item.Book.Authors))
            .ThenByDescending(item => NormalizeForExactMatch(item.Book.Title) == normalizedTitle)
            .Take(5);
        var authorsById = new Dictionary<string, CatalogAuthor?>();

        try
        {
            foreach (var candidate in candidates)
            {
                var work = await openLibrary.GetWorkAsync(candidate.Book.OpenLibraryWorkId, cancellationToken);
                if (work is null) continue;
                var workAuthors = new List<CatalogAuthor>();
                foreach (var authorId in work.AuthorIds)
                {
                    if (!authorsById.TryGetValue(authorId, out var author))
                    {
                        if (authorsById.Count >= 10) continue;
                        author = await openLibrary.GetAuthorAsync(authorId, cancellationToken);
                        authorsById[authorId] = author;
                    }
                    if (author is not null) workAuthors.Add(author);
                }
                books[candidate.Index] = candidate.Book with { WorkAuthors = workAuthors.ToArray() };
            }
        }
        catch (CatalogException)
        {
            // Search results are still usable if optional author verification fails. Do not guess missing roles.
            // Stop further detail requests for this search; caller cancellation is not caught here.
        }
        return books;
    }

    // Return a unique strongest match alone. Otherwise keep up to five equally strong candidates.
    private static BookSelection SelectFinalMatches(string userQuery, BookSearchTerms searchTerms, BookSelection selectedBooks,
        IReadOnlyList<CatalogBook> booksFromOpenLibrary)
    {
        // An author request is a list of books, not a request to choose one title.
        if (searchTerms.Title is null && searchTerms.Author is not null)
        {
            var booksById = booksFromOpenLibrary.ToDictionary(book => book.OpenLibraryWorkId);
            return new BookSelection(selectedBooks.Books
                .OrderByDescending(book => booksById[book.OpenLibraryWorkId].ReadingLogCount).Take(5).ToArray());
        }

        var normalizedQuery = NormalizeForExactMatch(userQuery);
        if (normalizedQuery.Length == 0)
            return selectedBooks;

        // Only names resolved through the work's author links can establish this stronger match.
        var titleAndAuthorMatches = booksFromOpenLibrary.Where(book => QueryMatchesTitleAndAuthor(
            normalizedQuery, book.Title, book.WorkAuthors.SelectMany(author => author.AlternateNames.Prepend(author.Name))))
            .OrderByDescending(book => book.ReadingLogCount).ToArray();
        if (titleAndAuthorMatches.Length > 0)
        {
            return new BookSelection(titleAndAuthorMatches.Take(5).Select(book => new SelectedBook(
                book.OpenLibraryWorkId, "The query matches this book's title and an author listed on its Open Library work record.")).ToArray());
        }

        // Compare the whole original query, not a title Gemini inferred from partial words or edition clues.
        var titleMatches = booksFromOpenLibrary.Where(book => normalizedQuery == NormalizeForExactMatch(book.Title))
            .OrderByDescending(book => book.ReadingLogCount).ToArray();
        if (titleMatches.Length == 0) return selectedBooks;
        if (titleMatches.Length == 1)
            return new BookSelection([new(titleMatches[0].OpenLibraryWorkId, "The query matches this book's title.")]);

        // Prefer a uniquely highest reported count. Missing counts stay unknown and do not veto this preference.
        // Zero activity or tied leaders do not distinguish a winner. Popularity is not proof of identity.
        if (titleMatches[0].ReadingLogCount is > 0
            && titleMatches.Skip(1).All(book => book.ReadingLogCount != titleMatches[0].ReadingLogCount))
        {
            return new BookSelection([new(titleMatches[0].OpenLibraryWorkId,
                "The title matches. Among the exact-title results, this work has the highest reported Open Library reading-list count.")]);
        }

        return new BookSelection(titleMatches.Take(5).Select(book => new SelectedBook(book.OpenLibraryWorkId,
            "The title matches, but the available details do not distinguish a single book.")).ToArray());
    }

    // Compare a full title plus a fetched author name or alias, allowing case, accent, and punctuation differences.
    private static bool QueryMatchesTitleAndAuthor(string normalizedQuery, string title, IEnumerable<string> authorNames)
    {
        return authorNames.Any(author => normalizedQuery == NormalizeForExactMatch($"{title} by {author}")
            || normalizedQuery == NormalizeForExactMatch($"{title} {author}"));
    }

    // Normalize text for exact comparisons without inventing missing words or expanding partial names.
    private static string NormalizeForExactMatch(string value)
    {
        var withoutAccents = Regex.Replace(value.Normalize(NormalizationForm.FormD), @"\p{Mn}", "");
        return Regex.Replace(withoutAccents.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    }
}
