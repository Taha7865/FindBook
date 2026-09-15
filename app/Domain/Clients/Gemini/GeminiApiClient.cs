using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindBook.Domain.Clients.AI;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Models;

namespace FindBook.Domain.Clients.Gemini;

public sealed class GeminiApiClient(IHttpClientFactory httpClientFactory, IGeminiApiOptions options) : IGeminiApiClient
{
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task<BookSearchTerms> ExtractSearchTermsAsync(string userQuery, CancellationToken cancellationToken)
        => GenerateAsync<BookSearchTerms>(GeminiPrompts.ExtractSearchTerms, Safeguard.CreateUserMessage(userQuery),
            GeminiResponseSchemas.SearchTerms, cancellationToken);

    public Task<BookSelection> SelectBooksAsync(string userQuery, IReadOnlyList<CatalogBook> booksFromOpenLibrary,
        CancellationToken cancellationToken)
        => GenerateAsync<BookSelection>(GeminiPrompts.SelectBooks,
            JsonSerializer.Serialize(new { query = userQuery, booksFromOpenLibrary }, OutputJson),
            GeminiResponseSchemas.Selection, cancellationToken);

    private async Task<T> GenerateAsync<T>(string instructions, string userMessage, JsonElement responseSchema,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new BookSearchAiException(BookSearchAiFailure.NotConfigured);

        try
        {
            using var httpClient = httpClientFactory.CreateClient(GeminiApiOptions.SectionName);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"models/{Uri.EscapeDataString(options.Model)}:generateContent");
            // Keep credentials out of URLs and model input.
            request.Headers.Add("x-goog-api-key", options.ApiKey);
            request.Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = Safeguard.SystemInstruction + "\n\n" + instructions } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userMessage } } } },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseJsonSchema = responseSchema,
                    maxOutputTokens = options.MaxOutputTokens
                }
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new BookSearchAiException(BookSearchAiFailure.NotConfigured);
            if (response.StatusCode == HttpStatusCode.RequestTimeout)
                throw new BookSearchAiException(BookSearchAiFailure.Timeout);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new BookSearchAiException(BookSearchAiFailure.Unavailable);
            if (!response.IsSuccessStatusCode)
                throw new BookSearchAiException(BookSearchAiFailure.BadResponse);

            var body = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken);
            if (body?.Candidates is not { Length: 1 } || body.Candidates[0] is not { FinishReason: "STOP" } candidate
                || candidate.Content?.Parts is not { Length: > 0 } parts)
                throw new BookSearchAiException(BookSearchAiFailure.BadResponse);

            var outputText = string.Concat(parts.Where(part => part is not null && !part.Thought).Select(part => part.Text));
            return JsonSerializer.Deserialize<T>(outputText, OutputJson) ?? throw new BookSearchAiException(BookSearchAiFailure.BadResponse);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BookSearchAiException(BookSearchAiFailure.Timeout, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new BookSearchAiException(BookSearchAiFailure.Unavailable, exception);
        }
        catch (JsonException exception)
        {
            throw new BookSearchAiException(BookSearchAiFailure.BadResponse, exception);
        }
    }
}
