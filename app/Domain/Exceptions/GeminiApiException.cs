namespace FindBook.Domain.Exceptions;

public enum GeminiApiFailureReason { BadResponse, Unavailable, Timeout, NotConfigured }

public sealed class GeminiApiException(GeminiApiFailureReason reason, Exception? innerException = null)
    : Exception($"The Gemini API request failed: {reason}.", innerException)
{
    public GeminiApiFailureReason Reason { get; } = reason;
}
