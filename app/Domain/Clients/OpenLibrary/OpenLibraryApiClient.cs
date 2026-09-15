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
    private const string Fields = "key,title,author_name,subject,readinglog_count,first_publish_year,cover_i,editions,editions.key,editions.title";

    public async Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient(OpenLibraryApiOptions.SectionName);
            // Fetch a small candidate pool; result selection belongs to the service.
            var queryParameters = new List<string>();
            if (searchTerms.Title is { } title)
                queryParameters.Add($"title={Uri.EscapeDataString(title)}");
            if (searchTerms.Author is { } author)
                queryParameters.Add($"author={Uri.EscapeDataString(author)}");
            var hasEditionRequest = searchTerms.EditionYear is not null || searchTerms.EditionKeywords.Length > 0;
            var queryParts = new List<string>();
            if (searchTerms.Keywords.Length > 0)
                queryParts.Add(string.Join(' ', searchTerms.Keywords));
            if (searchTerms.EditionYear is { } year)
                queryParts.Add($"publish_year:{year}");
            queryParts.AddRange(searchTerms.EditionKeywords.Select(keyword =>
                "\"" + keyword.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
            if (queryParts.Count > 0)
                queryParameters.Add($"q={Uri.EscapeDataString(string.Join(" AND ", queryParts))}");
            if (queryParameters.Count == 0)
                return [];
            if (searchTerms.Title is null)
                queryParameters.Add("sort=readinglog");
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

                // TODO: Resolve primary authors and contributor roles from work/edition records.
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
                            editions.Add(new CatalogEdition(editionId, edition.Title, null));
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

    private static async Task<T?> ReadResponseAsync<T>(HttpClient httpClient, string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestTimeout)
            throw new CatalogException(CatalogFailure.Timeout);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new CatalogException(CatalogFailure.Unavailable);
        if (!response.IsSuccessStatusCode)
            throw new CatalogException(CatalogFailure.BadResponse);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static string? ReadId(string? key, string collection, char suffix)
    {
        if (key is null) return null;
        var prefix = $"/{collection}/";
        var id = key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
        return Regex.IsMatch(id, $"\\AOL[0-9]+{suffix}\\z", RegexOptions.CultureInvariant) ? id : null;
    }
}
