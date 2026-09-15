using FindBook.Domain.Models;
using FindBook.Api.Services;
using FindBook.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FindBook.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksController(BookSearchService searchService) : ControllerBase
{
    [HttpPost("search")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromBody] SearchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await searchService.SearchAsync(request.Query, cancellationToken));
        }
        catch (CatalogException exception)
        {
            var (status, title) = exception.Failure switch
            {
                CatalogFailure.Unavailable => (503, "The book catalog is unavailable. Try again later."),
                CatalogFailure.Timeout => (504, "The book catalog took too long to respond. Try again."),
                _ => (502, "The book catalog returned an unexpected response.")
            };

            return Problem(statusCode: status, title: title);
        }
        catch (GeminiApiException exception)
        {
            var (status, title) = exception.Reason switch
            {
                GeminiApiFailureReason.NotConfigured => (503, "The AI search service is not configured."),
                GeminiApiFailureReason.Unavailable => (503, "The AI search service is unavailable. Try again later."),
                GeminiApiFailureReason.Timeout => (504, "The AI search service took too long to respond. Try again."),
                _ => (502, "The AI search service returned an unexpected response.")
            };

            return Problem(statusCode: status, title: title);
        }
    }
}
