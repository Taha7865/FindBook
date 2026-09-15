namespace FindBook.Domain.Models;

internal sealed record GeminiResponse(GeminiCandidate[]? Candidates);
internal sealed record GeminiCandidate(string? FinishReason, GeminiContent? Content);
internal sealed record GeminiContent(GeminiPart[]? Parts);
internal sealed record GeminiPart(string? Text, bool Thought);
