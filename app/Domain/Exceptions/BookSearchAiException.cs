namespace FindBook.Domain.Exceptions;

public enum BookSearchAiFailure { BadResponse, Unavailable, Timeout, NotConfigured }

public sealed class BookSearchAiException(BookSearchAiFailure failure, Exception? innerException = null)
    : Exception($"The book search AI request failed: {failure}.", innerException)
{
    public BookSearchAiFailure Failure { get; } = failure;
}
