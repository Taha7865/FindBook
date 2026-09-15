# FindBook

A book search app built with .NET 8 and Next.js.

One `POST /api/books/search` request runs the search and returns the final results. Gemini extracts search terms, Open Library supplies book records, and a second Gemini call selects books and explains the matches. Search requires a Gemini API key.

## Structure

```text
app/
  Api/         Controllers, search service, appsettings, and DI registration
  Domain/      API clients, typed options, models, prompts, and validation
  Tests.Unit/  Unit tests with simulated API responses
  frontend/    Next.js search page and a server route that forwards requests to .NET
```

`BooksController` calls `BookSearchService`. The service receives `IGeminiApiClient`, `IOpenLibraryApiClient`, and `IBookSearchValidator` through constructor injection. Clients live in Domain and use named clients from `IHttpClientFactory`. `Program.cs` binds typed options to appsettings, registers clients as transient, and registers the service and validator as scoped. No AI SDK or agent framework is used.

`GeminiApiException` identifies failures in AI extraction, selection, or output validation. Its `GeminiApiFailureReason` value distinguishes invalid output, unavailable service, timeout, and missing configuration. The controller maps these to HTTP error responses without exposing provider response bodies or credentials.

The frontend uses TypeScript, React state, and plain CSS. Enter or the arrow button submits the text; Shift+Enter adds a line. Results keep the backend's order and show a numbered list when several books are returned. The page also handles loading, cancellation, empty results, API errors, missing covers, and expandable edition details. A single result is not automatically labeled an exact match.

The Next.js server forwards `/api/books/search` to .NET. `API_BASE_URL` sets the backend address at runtime and defaults to `http://127.0.0.1:8080`. This keeps browser requests on one origin; the Gemini key stays in .NET. The forwarding route preserves JSON error statuses, passes cancellation along, and stops waiting after four minutes. It does not retry; .NET owns retries. Cancellation cannot undo a request an external provider has already processed.

## Search flow

1. `ExtractSearchTermsAsync` returns a `BookSearchTerms` object with title, author, keywords, and edition clues. Empty search terms end the request with no matches.
2. Open Library searches separate title, author, and keyword parameters and fetches up to 20 records. Requested years and edition words also filter this search. If that search is empty, the service removes the edition filters and tries the same book again. If a title-and-author search still finds nothing, one author-only search follows. C# controls these calls. Duplicate records are grouped by `openLibraryWorkId` before selection. An empty catalog result skips the second Gemini call.
3. `VerifyWorkAuthorsAsync` checks up to five likely candidates, prioritizing exact query matches and then the extracted title. It fetches `/works/{workId}.json` and resolves the author links through `/authors/{authorId}.json`. Author records are reused within the request, with at most ten distinct author lookups. Resolved names and aliases are supplied to Gemini as `workAuthors`. Missing records remain unverified; a failed optional lookup stops further verification but preserves the search results.
4. `SelectBooksAsync` receives the original query and fetched records. It returns up to five IDs with explanations, which the validator checks. It uses fetched work authors and explicit edition contributions for role evidence, not remembered authorship.
5. `SelectFinalMatches` checks the whole original query against the fetched title and work-author names or aliases. One exact title-plus-work-author match is returned alone; multiple such matches remain up to five candidates, ahead of weaker matches. A unique exact title also returns alone. For multiple exact titles, one wins on popularity when it has the uniquely highest reported positive count. Missing counts remain unknown and do not block that preference. Tied leaders, only zero counts, or entirely unknown counts retain up to five exact-title candidates. Partial or descriptive queries keep Gemini's selection. An author-only query keeps up to five selected books, ordered by reading-log count. Public metadata comes from the fetched records; `authors[]` remains the display list from search.

The two prompts and their examples are in [GeminiPrompts.cs](app/Domain/Clients/Gemini/GeminiPrompts.cs). The selection prompt follows the assessment's title/author hierarchy and asks for one clear match or up to five possibilities. There is no custom fuzzy matcher or numeric confidence score. Prompt quality must be evaluated with live queries; schema compliance alone does not establish accuracy.

The exact-match check normalizes case, accents, punctuation, and spacing. It compares the original query directly with catalog fields, not a title Gemini inferred. Partial names, descriptive queries, and extra edition clues remain with Gemini. Work-author links are the catalog source for primary authorship; a missing link does not prove a contributor role. The checks apply to retrieved candidates and available metadata, not every book in Open Library. Popularity is a selection preference, not proof of identity. No fuzzy scoring framework is used.

For edition requests, `OpenLibraryApiClient` uses `publish_year` and quoted edition words in the search, then retrieves up to five matching edition records from `/books/{editionId}.json`. It verifies each record's ID, work link, and requested year against the edition's own publication date. Uncertain dates such as `2001?` do not verify a requested year. The client supplies the fetched subtitle, edition name, contributions, and notes to Gemini and returns them in `editions[]`. Gemini interprets features such as illustrated or deluxe from those fields and must check all requested features against the same edition. These feature judgments still need evaluation. See the [Open Library search documentation](https://openlibrary.org/dev/docs/api/search) and [edition endpoints](https://openlibrary.org/dev/docs/api/books).

`BookSearchValidator` checks field limits, required values, duplicate selections, and membership in the fetched IDs. It does not interpret the query. `Safeguard` places a short instruction in both Gemini system messages and treats user and catalog text as untrusted data. This is a basic precaution, not a guarantee against prompt injection. Output uses Gemini's JSON schema support and is validated again in C#.

Open Library's `readinglog_count` is the popularity signal: it measures reading-list activity, not sales. Searches without a title use `sort=readinglog`, which orders by that count descending. An author-only query such as `J.K. Rolling` is extracted as an author with no title. Open Library supplies up to 20 candidates; Gemini selects up to five relevant distinct works with explanations; the service orders those selections by the fetched counts. Five is a maximum, not a requirement to fill slots with irrelevant books. Missing counts remain unknown and sort after known counts. Stronger verified title/author matches take priority over popularity.

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

In a second terminal, start the frontend using Node.js 22 or later:

```sh
cd app/frontend
npm ci
npm run dev
```

Open http://localhost:3000. If the API uses a different port, start the frontend with `API_BASE_URL=http://127.0.0.1:8081 npm run dev`. Do not put a Gemini key in frontend environment variables.

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

`OpenLibraryApi` contains `BaseUrl` (`https://openlibrary.org/`), `TimeoutSettings` (`00:00:15`), and `MaxEditionLookups` (`5`, allowed range 1–20). Edition lookups run only when edition clues are present; the limit bounds extra calls and the text sent to Gemini. Each timeout covers one outbound call, including all retry attempts and delays. Gemini has a 30-second budget per call; Open Library has 15 seconds. The complete search can contain several calls. Startup validates URLs, timeout bounds, retry options, and model settings. Missing credentials leave health available and cause search to return 503. Cancellation stops the current call and any retry wait.

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
cd app/frontend
npm run typecheck
npm run build
```

Tests cover the call sequence, exact-match priority and its limits, both Gemini request formats, schema and selection validation, catalog mapping, grouping, author fallback, and errors/cancellation. Shared retry tests use the real factory registration with simulated HTTP responses. They cover recovery, exhausted attempts, permanent errors, POST body replay, Retry-After, and cancellation/timeouts. The published API also recovered from simulated Gemini 503 and Open Library 429 responses; persistent Gemini failures stopped after three attempts and returned HTTP 503. Simulated responses do not measure Gemini's search accuracy. No CI workflow is configured.

All 130 unit tests pass. Tests cover edition lookup limits, work links, publication dates, uncertain dates, metadata mapping, and fallback order. Additional author and winner tests cover work-author retrieval, aliases, missing records, bounded lookups, author-record reuse, primary-author priority, popularity ties, and author-only result ordering. The Gemini integration was also checked with a release publish and HTTP tests using local upstream fixtures. Those HTTP checks covered the complete call sequence, grouped metadata, invalid selections, empty results, missing credentials, and 502/503/504 responses.

A live baseline covered ten queries: the assessment examples plus Rowling, a dragon topic, an alternate Hobbit title, and a precise Chamber of Secrets title. All searches completed, but several results were too broad. The precise title returned multiple works with the same title; other searches included adaptations or collections. Some explanations implied author roles that had not been verified. A separate edition check retrieved the Hobbit's 1937 edition and an illustrated edition with an explicit illustrator subtitle. These are observations from individual runs, not an accuracy guarantee.

After adding work-author verification and final selection rules, targeted live checks returned one Rowling work for the full Chamber of Secrets title and five Rowling books for `J.K. Rolling`. A full Hobbit title-and-author query returned multiple works with verified Tolkien links, preserving the rule for multiple equally strong matches. A release-published HTTP check confirmed the single Chamber result and blank-query validation.

Frontend checks cover the production build, TypeScript, Enter and arrow submission, Shift+Enter, result order, edition details, cancellation, reset, empty and error states, and mobile overflow. The forwarding route was checked against the real backend for 400 and 415 JSON responses. Live browser searches through Next.js, .NET, Gemini, and Open Library returned one Chamber of Secrets result and five ordered Rowling results. Production dependencies reported no known vulnerabilities in the npm audit at the time of this check.

Docker is not installed on the development machine, so container startup has not been verified. A future function-calling version can reuse `OpenLibraryApiClient`; the current implementation uses explicit calls.
