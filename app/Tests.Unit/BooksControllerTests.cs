using FindBook.Api.Contracts;
using FindBook.Api.Controllers;
using FindBook.Api.Services;
using FindBook.Domain.Exceptions;
using FindBook.Domain.Interfaces;
using FindBook.Domain.Models;
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
        var controller = new BooksController(new BookSearchService(new FailingCatalog(failure)));

        var response = await controller.Search(new SearchRequest { Query = "example" }, default);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(status, Assert.IsType<ProblemDetails>(result.Value).Status);
    }

    private sealed class FailingCatalog(CatalogFailure failure) : IBookCatalog
    {
        public Task<IReadOnlyList<CatalogBook>> SearchAsync(string query, CancellationToken cancellationToken)
            => throw new CatalogException(failure);
    }
}
