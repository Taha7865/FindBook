namespace FindBook.Domain.Clients.Gemini;

public interface IGeminiApiOptions
{
    string BaseUrl { get; }
    TimeSpan TimeoutSettings { get; }
    string Model { get; }
    string ApiKey { get; }
    int MaxOutputTokens { get; }
}
