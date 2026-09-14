# FindBook

A book search application built with .NET 8. Next.js will be added for the frontend.

Current status: API setup with `GET /api/health`. Search, AI integration, and the frontend are not implemented yet.

Search contracts are defined in `app/Api/Contracts`. Queries must contain non-whitespace text and be at most 1,000 characters. Results use `authors[]`; primary-author and contributor-role resolution is deferred with a TODO. Edition publication dates remain separate from the work's first publication year.

`Safeguard` provides a short system instruction and a JSON-encoded user message for the future AI client. It preserves book text and labels it as untrusted data. This is a basic prompt precaution, not reliable injection detection. It is not connected to a model yet; structured response validation will be added with the client.

## Structure

```text
app/
  Api/         Controllers, configuration, and future services and clients
  Domain/      Class library for book models and matching rules
  Tests.Unit/  Unit-test project
```

Api references Domain. Domain has no external dependencies. Services will coordinate searches; separate clients will handle Gemini and Open Library calls. Public API contracts belong in Api. Domain has no implementation code yet.

## Run locally

Install .NET SDK 8.0.425 or a later patch in the 8.0.4xx band. From the repository root:

```sh
dotnet restore --locked-mode
dotnet run --project app/Api
```

Open http://localhost:8080/api/health. The response is `{"status":"ok"}`.

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

The initial build, release publish, and live health response were checked locally. Docker execution has not been verified on this machine.

Run unit tests with `dotnet test --configuration Release`. They cover missing and blank queries, the length boundary, valid book text, and safe JSON encoding of instruction-like input. They do not measure a model's resistance to prompt injection. No CI workflow is configured.

## Planned search behavior

- Group editions of a book. Honor a specific publication year when a matching edition is available.
- Return a single clear title match, or up to five distinct books for broad or author-only queries.
- Combine relevance with popularity. The popularity measure is not selected yet.
- Use Gemini to interpret queries and explain matches using Open Library evidence.
