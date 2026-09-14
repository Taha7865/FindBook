# FindBook

A book search application built with .NET 8. Next.js will be added for the frontend.

Current status: `GET /api/health` and `POST /api/books/search` work. Search calls Open Library directly. AI integration, final matching rules, and the frontend are not implemented yet.

Request and response models are defined in `app/Domain/Models`. Queries must contain non-whitespace text and be at most 1,000 characters. Results use `authors[]`; primary-author and contributor-role resolution is deferred with a TODO. Edition publication dates remain separate from the work's first publication year.

`Safeguard` provides a short system instruction and a JSON-encoded user message for the future AI client. It preserves book text and labels it as untrusted data. This is a basic prompt precaution, not reliable injection detection. It is not connected to a model yet; structured response validation will be added with the client.

## Structure

```text
app/
  Api/         Controllers, services, appsettings, and DI registration
  Domain/      Clients, typed options, models, and matching rules
  Tests.Unit/  Unit-test project
```

Api references Domain. `BooksController` calls `BookSearchService`, which receives `IOpenLibraryApiClient` through its constructor. Domain owns `OpenLibraryApiClient`, its interface and options, all models, and the AI safeguard. It references `Microsoft.Extensions.Http` for `IHttpClientFactory`.

`Program.cs` binds appsettings to `OpenLibraryApiOptions` and exposes it as `IOpenLibraryApiOptions`. Those options configure the named HTTP client. `OpenLibraryApiClient` receives `IHttpClientFactory` and requests that client for each call; the factory manages the underlying connections. The API client is transient and the search service is scoped to the request.

## Run locally

Install .NET SDK 8.0.425 or a later patch in the 8.0.4xx band. From the repository root:

```sh
dotnet restore --locked-mode
dotnet run --project app/Api
```

Open http://localhost:8080/api/health. The response is `{"status":"ok"}`.

Search for a book:

```sh
curl -X POST http://localhost:8080/api/books/search \
  -H 'Content-Type: application/json' \
  -d '{"query":"the hobbit"}'
```

The client requests 20 candidates and the service returns up to five distinct work IDs in Open Library's relevance order. Returned editions are grouped by work; the edition list is not exhaustive. Edition dates are currently `null` because they are not fetched in this search slice. Explanations describe the catalog result and are not AI-generated.

| HTTP status | Meaning |
| --- | --- |
| 200 | Search completed; `matches` may be empty. |
| 400 | Missing, malformed, blank, or overlong input. |
| 404 | The requested route does not exist. |
| 415 | The request body is not JSON. |
| 502 | Open Library returned an unexpected response. |
| 503 | Open Library is unavailable or rate-limited the request. |
| 504 | Open Library did not respond within the timeout. |

Open Library settings live in `app/Api/appsettings.json`:

```json
"OpenLibraryApi": {
  "BaseUrl": "https://openlibrary.org/",
  "TimeoutSettings": "00:00:15"
}
```

`OpenLibraryApiOptions` inherits `BaseUrl` and the `TimeSpan` timeout from `HttpClientOptions`; it contains no setting values. Startup validates the URL and a timeout greater than zero and at most one minute. Environment variables `OpenLibraryApi__BaseUrl` and `OpenLibraryApi__TimeoutSettings` can override appsettings. No automatic retries are made. Client cancellation is passed through to the catalog request.

For the user-local SDK installed during setup, first run:

```sh
export DOTNET_ROOT="$HOME/.local/share/findthatbook/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

## Run with Docker

Install Docker with Compose and start its engine. Then run:

```sh
docker compose up --build
```

The health endpoint uses the same URL. Stop with `Ctrl+C`, then run `docker compose down`. Local development and Docker share port 8080; run one at a time.

The Dockerfile builds with the SDK and runs the compiled API in a smaller ASP.NET image as a non-root user. No API keys are required yet. Future secrets will use environment variables or local secret storage, not committed settings.

## Verification

```sh
dotnet build --configuration Release --no-restore
```

The build, release publish, and a live search for “the hobbit” were checked locally. Full HTTP checks with a local catalog fixture verified validation, grouped results, empty results, and error status/content types. Configuration checks verified the named client's base URL and timeout, and startup rejection of invalid settings. Docker execution has not been verified on this machine.

Run unit tests with `dotnet test --configuration Release`. They use controlled catalog responses to check validation, query encoding, missing metadata, work/edition grouping, the result limit, and error/cancellation handling. Safeguard tests check message encoding, not model resistance to prompt injection. No CI workflow is configured.

## Planned search behavior

- Fetch edition publication dates and honor a specific year when a matching edition is available.
- Return a single clear title match, or up to five distinct books for broad or author-only queries.
- Combine relevance with popularity. The popularity measure is not selected yet.
- Use Gemini to interpret queries and explain matches using Open Library evidence.
