# FindBook

A book search app built with .NET 8 and Next.js.

One `POST /api/books/search` request runs the search and returns the final results. Gemini extracts up to three sets of search terms, Open Library supplies book records, and a second Gemini call selects books and explains the matches. Search requires a Gemini API key.

## Structure

```text
app/
  Api/         Controllers, search service, appsettings, and DI registration
  Domain/      API clients, typed options, models, prompts, and validation
  Tests.Unit/  Service tests with injected client doubles, plus client and validation tests
  frontend/    Next.js search page and a server route that forwards requests to .NET
```

`BooksController` calls `BookSearchService`. The service receives `IGeminiApiClient`, `IOpenLibraryApiClient`, and `IBookSearchValidator` through constructor injection. Clients live in Domain and use named clients from `IHttpClientFactory`. `Program.cs` binds typed options to appsettings, registers clients as transient, and registers the service and validator as scoped. No AI SDK or agent framework is used.

`GeminiApiException` identifies failures in AI extraction, selection, or output validation. Its `GeminiApiFailureReason` value distinguishes invalid output, unavailable service, timeout, and missing configuration. The controller maps these to HTTP error responses without exposing provider response bodies or credentials.

The frontend uses TypeScript, React state, and plain CSS. Enter or the arrow button submits the text; Shift+Enter adds a line. Results keep the backend's order and show a numbered list when several books are returned. The page also handles loading, cancellation, empty results, API errors, missing covers, and expandable edition details. A single result is not automatically labeled an exact match.

The Next.js server forwards `/api/books/search` to .NET. `API_BASE_URL` sets the backend address at runtime and defaults to `http://127.0.0.1:8080`. This keeps browser requests on one origin; the Gemini key stays in .NET. The forwarding route preserves JSON error statuses, passes cancellation along, and stops waiting after four minutes. It does not retry; .NET owns retries. Cancellation cannot undo a request an external provider has already processed.

## Search flow

1. `ExtractSearchTermsAsync` returns `BookSearchSuggestions`: a `searches` array of up to three `BookSearchTerms` objects. Each has title, author, keywords, and edition clues. The prompt asks for one search for a supplied title or author, and alternative clue combinations for a story description. Explicit author and edition requirements must stay in the suggestions. Gemini makes these language judgments; the validator checks the count and field limits. No searchable terms means no matches.
2. The service runs the suggested searches in order, with a one-second pause before each additional search. Open Library receives separate title, author, and keyword parameters and returns up to 20 records per search. The service combines these records, grouping repeated `openLibraryWorkId` values before selection. The combined pool has at most 60 records before grouping. If the whole pool is empty, existing edition and author fallbacks use the first suggestion, when budget remains. Suggested searches and fallbacks share a maximum of three catalog searches; shared HTTP retries can add attempts. An empty final pool skips Gemini selection.
3. `VerifyWorkAuthorsAsync` checks up to five likely candidates from the combined pool per selection round, prioritizing exact query matches and then the first suggestion's title. It fetches `/works/{workId}.json` and resolves the author links through `/authors/{authorId}.json`. Author records are reused across the request, including a fallback, with at most ten distinct author lookups in total. Resolved names and aliases are supplied to Gemini as `workAuthors`. Missing records remain unverified; a failed optional lookup stops verification for that round but preserves the search results.
4. `SelectBooksAsync` receives the full original query and the combined records. Gemini decides which books are relevant and returns up to five IDs with explanations, which the validator checks. It uses fetched work authors and explicit edition contributions for role evidence. If Gemini accepts none from a title-and-author search, a separate condition triggers the author-only fallback, unless that fallback already ran or the three-search budget is exhausted. Gemini evaluates the new pool against the original query. There is at most one author fallback; another empty selection ends with no matches. An empty fallback pool skips selection entirely.
5. `PrioritizeAcceptedBooks` applies exact-match and popularity preferences only to books Gemini accepted. It cannot restore a rejected book, and it keeps Gemini's explanations. One exact title-plus-work-author match is returned alone; multiple such matches remain up to five candidates. A unique exact title also returns alone. For multiple exact titles, a uniquely highest positive reading-log count wins, even when other counts are unknown. Ties, only zero counts, or entirely unknown counts retain alternatives. Partial or descriptive queries keep Gemini's order. Author-only searches and author fallbacks order accepted books by reading-log count.

Public metadata comes from the fetched records. `authors[]` uses resolved work-author names when available, with duplicate names removed; otherwise it keeps the search names without claiming verified roles. Explicit contributor details remain on their edition.

The two prompts and their examples are in [GeminiPrompts.cs](app/Domain/Clients/Gemini/GeminiPrompts.cs). The selection prompt follows the assessment's title/author hierarchy and asks for one clear match or up to five possibilities. There is no custom fuzzy matcher or numeric confidence score. Prompt quality must be evaluated with live queries; schema compliance alone does not establish accuracy.

The exact-match check normalizes case, accents, punctuation, and spacing. It compares the original query directly with catalog fields, not a title Gemini inferred. Partial names, descriptive queries, and extra edition clues remain with Gemini. Work-author links are the catalog source for primary authorship; a missing link does not prove a contributor role. The helper can narrow or reorder accepted books, but cannot rescue a good book Gemini omitted. Popularity is a selection preference, not proof of identity. No fuzzy scoring framework is used. The fallback can add one catalog search, up to five work lookups, and another Gemini selection call; lookup caps do not guarantee a fast response.

Ordinary searches request `editions.publish_date` and display the first nonblank date supplied for each returned edition. This needs no additional HTTP request and never borrows a date from another edition or the work's publication-date list. Missing dates remain unavailable; catalog date text is preserved.

For edition requests, `OpenLibraryApiClient` uses `publish_year` and quoted edition words in the search, then retrieves up to five matching edition records from `/books/{editionId}.json`. It verifies each record's ID, work link, and requested year against the edition's own publication date. Uncertain dates such as `2001?` do not verify a requested year. The client supplies the fetched subtitle, edition name, contributions, and notes to Gemini and returns them in `editions[]`. Gemini interprets features such as illustrated or deluxe from those fields and must check all requested features against the same edition. These feature judgments still need evaluation. See the [Open Library search documentation](https://openlibrary.org/dev/docs/api/search) and [edition endpoints](https://openlibrary.org/dev/docs/api/books).

`BookSearchValidator` checks the search count, field limits, required values, duplicate selections, and membership in the fetched IDs. It does not interpret the query. `Safeguard` places a short instruction in both Gemini system messages and treats user and catalog text as untrusted data. This is a basic precaution, not a guarantee against prompt injection. Output uses Gemini's JSON schema support and is validated again in C#.

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

Open the frontend at http://localhost:3000. The API is also available at http://localhost:8080. Stop with `Ctrl+C`, then run `docker compose down`. Local development and Docker share ports 3000 and 8080; run one at a time. Compose supplies the key and optional model only to the API at runtime. The key is excluded from Git and the image build contexts.

The root Dockerfile restores locked .NET dependencies, publishes the API and appsettings, and runs as a non-root user in an ASP.NET image. `app/frontend/Dockerfile` uses Node.js 24 and `npm ci`, builds Next.js, then copies its standalone output into a separate runtime image that runs as the `node` user. The frontend build script also copies the static assets into that output. Compose sets `API_BASE_URL=http://api:8080` so the Next.js server reaches the API by its service name. No Gemini key is included in the frontend image. Rebuild after changing code or appsettings.

## Verification and remaining work

```sh
dotnet test --configuration Release
dotnet publish app/Api/Api.csproj --configuration Release
cd app/frontend
npm run typecheck
npm run build
```

`BookSearchServiceTests` runs the real service with test-controlled implementations of `IGeminiApiClient`, `IOpenLibraryApiClient`, and `IBookSearchValidator`. Each case supplies explicit responses and checks business outcomes: combining alternative searches, the shared three-search budget, accepted-only selection, exact-match priority, popularity, grouped editions, displayed authors, and the two author-fallback conditions. Work-author records are supplied separately from search names. Validator and client tests cover their own rules and HTTP boundaries. Shared retry tests use the real factory registration with simulated HTTP responses. They cover recovery, exhausted attempts, permanent errors, POST body replay, Retry-After, and cancellation/timeouts. Simulated responses do not measure Gemini's search accuracy. No CI workflow is configured.

All 152 unit tests pass, including 52 service cases. Formatting and the release publish in Docker also pass. Live checks with alternative searches enabled returned one Chamber of Secrets result, five Rowling books for `J.K. Rolling`, and a Hobbit result with unverified edition features for `tolkien hobbit illustrated deluxe 1937`. Earlier release-published HTTP checks used local upstream fixtures for the complete call sequence, grouped metadata, invalid selections, empty results, missing credentials, and 502/503/504 responses. The published API also recovered from simulated Gemini 503 and Open Library 429 responses; persistent Gemini failures stopped after three attempts and returned HTTP 503.

A description of zombie hunters with trading cards produced two searches but returned a Walking Dead volume instead of the intended Rot & Ruin. A direct catalog check showed that `zombies hunters` included Rot & Ruin when requesting work fields, but excluded it when also requesting edition fields. Alternative queries improve the candidate search, but do not guarantee the intended book is fetched or selected.

A live baseline covered ten queries: the assessment examples plus Rowling, a dragon topic, an alternate Hobbit title, and a precise Chamber of Secrets title. All searches completed, but several results were too broad. The precise title returned multiple works with the same title; other searches included adaptations or collections. Some explanations implied author roles that had not been verified. A separate edition check retrieved the Hobbit's 1937 edition and an illustrated edition with an explicit illustrator subtitle. These are observations from individual runs, not an accuracy guarantee.

Earlier live checks, before restricting the helper to Gemini's accepted books, returned one Rowling work for the full Chamber of Secrets title and five Rowling books for `J.K. Rolling`. A full Hobbit title-and-author query returned multiple works with verified Tolkien links. A release-published HTTP check confirmed the single Chamber result and blank-query validation.

Frontend checks cover the production build, TypeScript, Enter and arrow submission, Shift+Enter, result order, edition details, cancellation, reset, empty and error states, and mobile overflow. The forwarding route was checked against the real backend for 400 and 415 JSON responses. Live browser searches through Next.js, .NET, Gemini, and Open Library returned one Chamber of Secrets result and five ordered Rowling results. Production dependencies reported no known vulnerabilities in the npm audit at the time of this check.

Docker Compose builds and starts the API and frontend. The API health route returns 200, and blank input through the frontend returns 400. A live search through the containers returned one Chamber of Secrets result with the runtime key. A future function-calling version can reuse `OpenLibraryApiClient`; the current implementation uses explicit calls.
