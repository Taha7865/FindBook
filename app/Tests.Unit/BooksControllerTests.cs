using FindBook.Domain.Models;
using FindBook.Api.Controllers;
using FindBook.Api.Services;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Clients.Gemini;
using FindBook.Domain.Clients.OpenLibrary;
using FindBook.Domain.Validators;
using Microsoft.AspNetCore.Mvc;

namespace FindBook.Tests.Unit;

public sealed class BooksControllerTests
{
    [Theory]
    [InlineData(CatalogFailure.BadResponse, 502)]
    [InlineData(CatalogFailure.Unavailable, 503)]
    [InlineData(CatalogFailure.Timeout, 504)]
    public async Task Catalog_failures_return_problem_details(CatalogFailure failure, int status)
    {
        var controller = new BooksController(new BookSearchService(new StubGemini(), new FailingCatalog(failure), new BookSearchValidator()));
        var response = await controller.Search(new SearchRequest { Query = "example" }, default);
        AssertProblem(response, status);
    }

    [Theory]
    [InlineData(GeminiApiFailureReason.BadResponse, 502)]
    [InlineData(GeminiApiFailureReason.Unavailable, 503)]
    [InlineData(GeminiApiFailureReason.NotConfigured, 503)]
    [InlineData(GeminiApiFailureReason.Timeout, 504)]
    public async Task Ai_failures_return_problem_details(GeminiApiFailureReason failure, int status)
    {
        var controller = new BooksController(new BookSearchService(new StubGemini(failure),
            new FailingCatalog(CatalogFailure.BadResponse), new BookSearchValidator()));
        var response = await controller.Search(new SearchRequest { Query = "example" }, default);
        AssertProblem(response, status);
    }

    private static void AssertProblem(ActionResult<SearchResponse> response, int status)
    {
        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(status, Assert.IsType<ProblemDetails>(result.Value).Status);
    }

    private sealed class FailingCatalog(CatalogFailure failure) : IOpenLibraryApiClient
    {
        public Task<IReadOnlyList<CatalogBook>> SearchAsync(BookSearchTerms searchTerms, CancellationToken cancellationToken)
            => throw new CatalogException(failure);
    }

    private sealed class StubGemini(GeminiApiFailureReason? failure = null) : IGeminiApiClient
    {
        public Task<BookSearchTerms> ExtractSearchTermsAsync(string userQuery, CancellationToken cancellationToken)
            => failure is { } error ? throw new GeminiApiException(error) : Task.FromResult(new BookSearchTerms("Example", null, [], null, []));
        public Task<BookSelection> SelectBooksAsync(string userQuery, IReadOnlyList<CatalogBook> booksFromOpenLibrary,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Must not reach selection.");
    }
}
