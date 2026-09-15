using System.ComponentModel.DataAnnotations;

namespace FindBook.Domain.Clients.Gemini;

public sealed class GeminiApiOptions : HttpClientOptions, IGeminiApiOptions
{
    public const string SectionName = "GeminiApi";

    [Required, RegularExpression(@"gemini-[a-zA-Z0-9.\-]+")]
    public string Model { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    [Range(1, 8192)]
    public int MaxOutputTokens { get; init; }
}
