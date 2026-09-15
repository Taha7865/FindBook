namespace FindBook.Domain.Exceptions;

public enum AiFailure { BadResponse, Unavailable, Timeout, NotConfigured }

public sealed class AiException(AiFailure failure, Exception? innerException = null)
    : Exception("The AI request failed.", innerException)
{
    public AiFailure Failure { get; } = failure;
}
