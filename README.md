# FindBook

A book search API built with .NET 8. Next.js will be added for the frontend.

One `POST /api/books/search` request runs the search and returns the final results. Gemini extracts search terms, Open Library supplies book records, and a second Gemini call selects books and explains the matches. Search requires a Gemini API key.

## Structure

```text
app/
  Api/         Controllers, search service, appsettings, and DI registration
  Domain/      API clients, typed options, models, prompts, and validation
  Tests.Unit/  Unit tests with simulated API responses
```

`BooksController` calls `BookSearchService`. The service receives `IGeminiApiClient`, `IOpenLibraryApiClient`, and `IBookSearchValidator` through constructor injection. Clients live in Domain and use named clients from `IHttpClientFactory`. `Program.cs` binds typed options to appsettings, registers clients as transient, and registers the service and validator as scoped. No AI SDK or agent framework is used.

`GeminiApiException` identifies failures in AI extraction, selection, or output validation. Its `GeminiApiFailureReason` value distinguishes invalid output, unavailable service, timeout, and missing configuration. The controller maps these to HTTP error responses without exposing provider response bodies or credentials.

## Search flow

1. `ExtractSearchTermsAsync` returns a `BookSearchTerms` object with title, author, keywords, and edition clues. Empty search terms end the request with no matches.
2. Open Library searches separate title, author, and keyword parameters and fetches up to 20 records. If a title-and-author search is empty, one author-only search follows. Duplicate records are grouped by `openLibraryWorkId` before selection. An empty catalog result skips the second Gemini call.
3. `SelectBooksAsync` receives the original query and fetched records. It returns up to five IDs with explanations, which the validator checks.
4. `PrioritizeExactMatches` in `BookSearchService` puts fetched books first when the whole query matches their title, `title by author`, or `title author`. It normalizes case, accents, punctuation, and spacing. The author must match a listed name. Exact matches get a short factual explanation and are included even if Gemini omitted them. Other selections retain Gemini's order; duplicate IDs are removed and the response stays within five books. Response metadata comes from the fetched records.

The two prompts and their examples are in [GeminiPrompts.cs](app/Domain/Clients/Gemini/GeminiPrompts.cs). The selection prompt follows the assessment's title/author hierarchy and asks for one clear match or up to five possibilities. There is no custom fuzzy matcher or numeric confidence score. Prompt quality must be evaluated with live queries; schema compliance alone does not establish accuracy.

The exact-match check compares the original query directly with catalog fields, not Gemini's extracted terms. Partial names, descriptive queries, and extra edition clues remain with Gemini. It applies only to retrieved books and does not verify primary authorship or a specific edition. When several books match exactly, they keep the catalog's order.

`BookSearchValidator` checks field limits, required values, duplicate selections, and membership in the fetched IDs. It does not interpret the query. `Safeguard` places a short instruction in both Gemini system messages and treats user and catalog text as untrusted data. This is a basic precaution, not a guarantee against prompt injection. Output uses Gemini's JSON schema support and is validated again in C#.

Open Library's `readinglog_count` is the popularity signal: it measures reading-list activity, not sales. Searches without a title use `sort=readinglog`. The second prompt prefers higher counts among relevant books, while stronger title/author matches take priority. Supplied subjects help support topic explanations. Missing counts remain unknown.

## Run locally

Use .NET SDK 8.0.425 or a later patch in the 8.0.4xx band. If using the SDK installed during the original setup:

```sh
export DOTNET_ROOT="$HOME/.local/share/findthatbook/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

From the repository root, store your Gemini key in local .NET user secrets, then start the API:

```sh
dotnet restore --locked-mode
dotnet user-secrets set "GeminiApi:ApiKey" "YOUR_API_KEY" --project app/Api
dotnet run --project app/Api
```

The development launch profile loads user secrets. An environment variable named `GeminiApi__ApiKey` can also supply the key. Do not commit credentials.

Open http://localhost:8080/api/health. It returns `{"status":"ok"}` even without a key; this endpoint checks that the API is running.

```sh
curl -X POST http://localhost:8080/api/books/search \
  -H 'Content-Type: application/json' \
  -d '{"query":"J.K. Rolling"}'
```

Queries must contain non-whitespace text and be at most 1,000 characters. Each match includes `openLibraryWorkId`, title, `authors[]`, first publication year, catalog link, optional cover, grouped editions, and an explanation.

## Configuration

Settings live in `app/Api/appsettings.json`. Options classes hold their typed representation, with no setting values in the classes.

```json
"GeminiApi": {
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/",
  "TimeoutSettings": "00:00:30",
  "Model": "gemini-3.5-flash-lite",
  "ApiKey": "",
  "MaxOutputTokens": 4096
}
```

`OpenLibraryApi` contains `BaseUrl` (`https://openlibrary.org/`) and `TimeoutSettings` (`00:00:15`). Each timeout covers one outbound call, including all retry attempts and delays. Gemini has a 30-second budget per call; Open Library has 15 seconds. The complete search can contain several calls. Startup validates URLs, timeout bounds, retry options, and model settings. Missing credentials leave health available and cause search to return 503. Cancellation stops the current call and any retry wait.

`HttpClientRegistration` uses `Microsoft.Extensions.Http.Resilience` with `ConfigureHttpClientDefaults`. Every client created by `IHttpClientFactory` receives the same retry policy. There are no retry loops in individual clients. The `ExternalApiRetry` section sets two retries (three total attempts), exponential delays starting from one second, and jitter to spread simultaneous retries. A valid `Retry-After` header sets the wait instead. The client's timeout can stop the operation before all attempts are used.

The policy retries HTTP 408, 429, 5xx, and network request failures. It does not retry 400/401/403/404 responses, malformed successful responses, or caller cancellation. This includes Gemini's generation POST requests: a retry can consume additional quota and produce different text. Retries cannot fix exhausted daily quotas or missing model access. Future clients that create or change external data should disable retries for unsafe methods or use an idempotency key. See [Microsoft's HTTP resilience guide](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience).

The default is `gemini-3.5-flash-lite`, selected for its low latency and free-tier availability. Model access and limits can change. Set `GeminiApi__Model` to use another compatible Gemini model. See Google's [model details](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite), [pricing](https://ai.google.dev/gemini-api/docs/pricing), and [structured output guide](https://ai.google.dev/gemini-api/docs/structured-output).

| HTTP status | Meaning |
| --- | --- |
| 200 | Search completed; `matches` may be empty. |
| 400 | Missing, malformed, blank, or overlong input. |
| 404 | The requested route does not exist. |
| 415 | The request body is not JSON. |
| 502 | An upstream response was malformed or failed validation. |
| 503 | AI is not configured, or an upstream service is unavailable or rate-limited. |
| 504 | An upstream service timed out. |

## Run with Docker

Install Docker with Compose and start its engine. Create an ignored `.env` file in the repository root:

```dotenv
GEMINI_API_KEY=YOUR_API_KEY
# Optional; this is also the default.
GEMINI_MODEL=gemini-3.5-flash-lite
```

```sh
docker compose up --build
```

The API is available at http://localhost:8080. Stop with `Ctrl+C`, then run `docker compose down`. Local development and Docker share port 8080; run one at a time. Compose supplies the key and optional model at runtime. The key is excluded from Git and the image build context. The Dockerfile restores locked dependencies, publishes the API and appsettings, and runs as a non-root user in an ASP.NET image. Shared retry settings are included in the image; rebuild after changing them.

## Verification and remaining work

```sh
dotnet test --configuration Release
dotnet publish app/Api/Api.csproj --configuration Release
```

Tests cover the call sequence, exact-match priority and its limits, both Gemini request formats, schema and selection validation, catalog mapping, grouping, author fallback, and errors/cancellation. Shared retry tests use the real factory registration with simulated HTTP responses. They cover recovery, exhausted attempts, permanent errors, POST body replay, Retry-After, and cancellation/timeouts. The published API also recovered from simulated Gemini 503 and Open Library 429 responses; persistent Gemini failures stopped after three attempts and returned HTTP 503. Simulated responses do not measure Gemini's search accuracy. No CI workflow is configured.

All 93 unit tests pass. The Gemini integration was also checked with a release publish and HTTP tests using local upstream fixtures. Those HTTP checks covered the complete call sequence, grouped metadata, invalid selections, empty results, missing credentials, and 502/503/504 responses. A live Open Library author search returned 20 works with subjects and reading-list counts. A live end-to-end search for "book about zombie apocalypse" returned HTTP 200 with five candidates. The assessment's full query set still needs live evaluation.

Primary-author and contributor roles remain unresolved; the response uses `authors[]`, and the prompt must not invent roles. Edition publication dates are not fetched yet. The prompt must distinguish a work's first publication year from a specific edition and state when a requested edition cannot be verified. The edition list returned by search is not exhaustive.

Full live evaluation, edition/author-role retrieval, the Next.js frontend, and local Docker execution remain to be completed. Docker is not installed on the development machine, so container startup has not been verified. A future function-calling version can reuse `OpenLibraryApiClient`; the current implementation uses explicit calls.
