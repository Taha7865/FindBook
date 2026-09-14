namespace FindBook.Domain.Exceptions;

public enum CatalogFailure
{
    BadResponse,
    Unavailable,
    Timeout
}

public sealed class CatalogException(CatalogFailure failure, Exception? innerException = null)
    : Exception("The book catalog request failed.", innerException)
{
    public CatalogFailure Failure { get; } = failure;
}
