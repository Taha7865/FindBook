using System.Net;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;

namespace FindBook.Domain.Clients.OpenLibrary;

public sealed class OpenLibraryApiClient(IHttpClientFactory httpClientFactory, IOpenLibraryApiOptions options) : IOpenLibraryApiClient
{
    private const string Fields = "key,title,author_name,subject,readinglog_count,first_publish_year,cover_i,editions,editions.key,editions.title,editions.publish_date";

    // Search Open Library for books, fetch edition details when requested, and return the fields our app uses.
    public async Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient(OpenLibraryApiOptions.SectionName);
        // Fetch a small candidate pool; result selection belongs to the service.
        var queryParameters = new List<string>();
        // Encode values so characters such as '&' stay inside the value instead of starting another URL parameter.
        if (searchTerms.Title is { } title)
            queryParameters.Add($"title={Uri.EscapeDataString(title)}");
        if (searchTerms.Author is { } author)
            queryParameters.Add($"author={Uri.EscapeDataString(author)}");
        // This means the user requested a year or edition feature, not simply that the book has editions.
        var hasEditionRequest = searchTerms.EditionYear is not null || searchTerms.EditionKeywords.Length > 0;
        // These conditions become the value of Open Library's general search parameter, q.
        var queryParts = new List<string>();
        if (searchTerms.Keywords.Length > 0)
            queryParts.Add(string.Join(' ', searchTerms.Keywords));
        if (searchTerms.EditionYear is { } year)
            queryParts.Add($"publish_year:{year}");
        // Quote each edition phrase and escape embedded quotes/backslashes so they do not break that phrase.
        // This formats Open Library search text; it is not protection against instructions sent to Gemini.
        queryParts.AddRange(searchTerms.EditionKeywords.Select(keyword =>
            "\"" + keyword.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        // Join the search conditions first, then encode the whole q value for the URL.
        if (queryParts.Count > 0)
            queryParameters.Add($"q={Uri.EscapeDataString(string.Join(" AND ", queryParts))}");
        if (queryParameters.Count == 0)
            return [];
        // Open Library's readinglog sort puts higher readinglog_count values first as a popularity signal.
        if (searchTerms.Title is null)
            queryParameters.Add("sort=readinglog");
        // Twenty is our candidate limit, not a reading-log count; final selection returns at most five books.
        var path = $"search.json?{string.Join('&', queryParameters)}&limit=20&fields={Fields}";
        var body = await ReadResponseAsync<OpenLibraryResponse>(httpClient, path, cancellationToken);
        if (body?.Docs is null)
            throw new CatalogException(CatalogFailure.BadResponse);

        var books = new List<CatalogBook>();
        var editionLookups = 0;
        foreach (var item in body.Docs)
        {
            var openLibraryWorkId = ReadId(item?.Key, "works", 'W');
            if (openLibraryWorkId is null || string.IsNullOrWhiteSpace(item?.Title))
                continue;

            // Search names are display metadata. The service separately checks work-author records.
            var authors = (item.Authors ?? [])
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var editions = new List<CatalogEdition>();
            foreach (var edition in item.Editions?.Docs ?? [])
            {
                var editionId = ReadId(edition?.Key, "books", 'M');
                if (editionId is not null && !string.IsNullOrWhiteSpace(edition?.Title))
                {
                    if (!hasEditionRequest)
                    {
                        // Use a date from this edition, not the work's dates, which can describe other editions.
                        var publishDate = edition.PublishDates?.FirstOrDefault(date => !string.IsNullOrWhiteSpace(date))?.Trim();
                        editions.Add(new CatalogEdition(editionId, edition.Title, publishDate));
                    }
                    else if (editionLookups < options.MaxEditionLookups)
                    {
                        editionLookups++;
                        var details = await ReadEditionAsync(httpClient, editionId, openLibraryWorkId,
                            searchTerms.EditionYear, cancellationToken);
                        if (details is not null)
                            editions.Add(details);
                    }
                }
            }

            books.Add(new CatalogBook(openLibraryWorkId, item.Title, authors,
                item.FirstPublishYear, item.CoverId is > 0 ? item.CoverId : null,
                editions.DistinctBy(edition => edition.EditionId).ToArray())
            {
                // Bound catalog text passed to Gemini. Subject lists can be very large.
                Subjects = (item.Subjects ?? []).OfType<string>()
                    .Where(subject => !string.IsNullOrWhiteSpace(subject) && subject.Length <= 200)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
                ReadingLogCount = item.ReadingLogCount is >= 0 ? item.ReadingLogCount : null
            });
        }

        if (body.Docs.Length > 0 && books.Count == 0)
            throw new CatalogException(CatalogFailure.BadResponse);

        return books;
    }

    // Fetch a work's author links. The service uses these links to check authorship before ranking.
    public async Task<CatalogWork?> GetWorkAsync(string workId, CancellationToken cancellationToken)
    {
        if (ReadId(workId, "works", 'W') != workId)
            throw new ArgumentException("Expected a bare Open Library work ID.", nameof(workId));

        using var httpClient = httpClientFactory.CreateClient(OpenLibraryApiOptions.SectionName);
        var work = await ReadResponseAsync<OpenLibraryWorkResponse>(httpClient, $"works/{workId}.json", cancellationToken);
        if (work is null) return null;
        if (ReadId(work.Key, "works", 'W') != workId)
            throw new CatalogException(CatalogFailure.BadResponse);

        var authorIds = (work.Authors ?? []).Select(role => ReadId(role?.Author?.Key, "authors", 'A'))
            .OfType<string>().Distinct().ToArray();
        return new CatalogWork(workId, authorIds);
    }

    // Resolve an author ID to its catalog name and aliases, so the service can compare actual names.
    public async Task<CatalogAuthor?> GetAuthorAsync(string authorId, CancellationToken cancellationToken)
    {
        if (ReadId(authorId, "authors", 'A') != authorId)
            throw new ArgumentException("Expected a bare Open Library author ID.", nameof(authorId));

        using var httpClient = httpClientFactory.CreateClient(OpenLibraryApiOptions.SectionName);
        var author = await ReadResponseAsync<OpenLibraryAuthorResponse>(httpClient, $"authors/{authorId}.json", cancellationToken);
        if (author is null) return null;
        if (ReadId(author.Key, "authors", 'A') != authorId || string.IsNullOrWhiteSpace(author.Name))
            throw new CatalogException(CatalogFailure.BadResponse);

        var alternateNames = (author.AlternateNames ?? []).OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 200)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        return new CatalogAuthor(authorId, author.Name, alternateNames);
    }

    // Fetch one edition and keep it only if its ID, parent work, and any requested year match.
    private static async Task<CatalogEdition?> ReadEditionAsync(HttpClient httpClient, string editionId,
        string workId, int? requestedYear, CancellationToken cancellationToken)
    {
        var edition = await ReadResponseAsync<OpenLibraryEditionResponse>(httpClient,
            $"books/{editionId}.json", cancellationToken);
        if (edition is null || ReadId(edition.Key, "books", 'M') != editionId
            || string.IsNullOrWhiteSpace(edition.Title)
            || edition.Works?.Any(work => ReadId(work?.Key, "works", 'W') == workId) != true)
            return null;

        // The search index is a hint. Verify the year against this edition's own record.
        if (requestedYear is { } year && !MatchesPublicationYear(edition.PublishDate, year))
            return null;

        // Open Library can return notes as plain text or as an object whose value contains the text.
        var notes = edition.Notes.ValueKind == JsonValueKind.String ? edition.Notes.GetString()
            : edition.Notes.ValueKind == JsonValueKind.Object
                && edition.Notes.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        return new CatalogEdition(editionId, edition.Title, edition.PublishDate)
        {
            Subtitle = edition.Subtitle,
            EditionName = edition.EditionName,
            Contributions = (edition.Contributions ?? []).OfType<string>()
                .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 200).Take(10).ToArray(),
            Notes = notes is { Length: > 1000 } ? notes[..1000] : notes
        };
    }

    // Check that the edition's date explicitly supports the requested year; uncertain dates do not qualify.
    private static bool MatchesPublicationYear(string? publishDate, int requestedYear)
    {
        var dateText = publishDate?.Trim();
        if (int.TryParse(dateText, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return year == requestedYear;

        // Require an explicit year in a readable date. "2001?" is not a verified date.
        return DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date.Year == requestedYear
            && Regex.IsMatch(dateText!, $@"(?<![0-9]){requestedYear}(?![0-9])");
    }

    // Send a GET request, translate failed HTTP statuses, and read the JSON into the requested response type.
    // The configured HTTP client handles retries before a response reaches this method's status checks.
    private static async Task<T?> ReadResponseAsync<T>(HttpClient httpClient, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            // A missing detail record is unverified. SearchAsync still rejects a missing search response.
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            if (response.StatusCode == HttpStatusCode.RequestTimeout)
                throw new CatalogException(CatalogFailure.Timeout);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new CatalogException(CatalogFailure.Unavailable);
            if (!response.IsSuccessStatusCode)
                throw new CatalogException(CatalogFailure.BadResponse);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogException(CatalogFailure.Timeout, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogException(CatalogFailure.Unavailable, exception);
        }
        catch (JsonException exception)
        {
            throw new CatalogException(CatalogFailure.BadResponse, exception);
        }
    }

    // Extract a work or edition ID from an Open Library key and reject unexpected formats before using the ID.
    // Open Library IDs ending in W identify a work: the overall book, grouping its editions under /works/.
    // IDs ending in M identify an edition: a specific published version of that book, stored under /books/.
    // IDs ending in A identify an author under /authors/.
    // The caller supplies W, M, or A so this method checks for the expected record type.
    // For example, /works/OL27482W becomes OL27482W; an already bare, valid ID is also accepted.
    private static string? ReadId(string? key, string collection, char suffix)
    {
        if (key is null) return null;
        var prefix = $"/{collection}/";
        var id = key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
        return Regex.IsMatch(id, $"\\AOL[0-9]+{suffix}\\z", RegexOptions.CultureInvariant) ? id : null;
    }
}
