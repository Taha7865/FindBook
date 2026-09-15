using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;

namespace FindBook.Domain.Clients.OpenLibrary;

public sealed class OpenLibraryApiClient(IHttpClientFactory httpClientFactory) : IOpenLibraryApiClient
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
            if (searchTerms.Keywords.Length > 0)
                queryParameters.Add($"q={Uri.EscapeDataString(string.Join(' ', searchTerms.Keywords))}");
            if (queryParameters.Count == 0)
                return [];
            if (searchTerms.Title is null)
                queryParameters.Add("sort=readinglog");
            var path = $"search.json?{string.Join('&', queryParameters)}&limit=20&fields={Fields}";
            using var response = await httpClient.GetAsync(path, cancellationToken);

            if (response.StatusCode == HttpStatusCode.RequestTimeout)
                throw new CatalogException(CatalogFailure.Timeout);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new CatalogException(CatalogFailure.Unavailable);

            if (!response.IsSuccessStatusCode)
                throw new CatalogException(CatalogFailure.BadResponse);

            var body = await response.Content.ReadFromJsonAsync<OpenLibraryResponse>(cancellationToken);
            if (body?.Docs is null)
                throw new CatalogException(CatalogFailure.BadResponse);

            var books = new List<CatalogBook>();
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
                        // Search does not supply the edition's publication date here.
                        editions.Add(new CatalogEdition(editionId, edition.Title, null));
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

    private static string? ReadId(string? key, string collection, char suffix)
    {
        if (key is null) return null;
        var prefix = $"/{collection}/";
        var id = key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
        return Regex.IsMatch(id, $"\\AOL[0-9]+{suffix}\\z", RegexOptions.CultureInvariant) ? id : null;
    }
}
