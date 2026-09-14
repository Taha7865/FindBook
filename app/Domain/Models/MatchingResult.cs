namespace FindBook.Domain.Models;

// Lower values take priority. These are rule categories, not confidence scores.
public enum MatchTier
{
    ExactTitlePrimaryAuthor,
    ExactTitleContributor,
    ExactTitle,
    TitleAndAuthor,
    Author,
    Related
}

public sealed record BookRanking(string WorkId, MatchTier Tier);

public sealed record MatchingResult(BookRanking[] Candidates, bool HasClearWinner);
