using System.Globalization;
using System.Text;
using FindBook.Domain.Models;
using FindBook.Domain.Validators;

namespace FindBook.Domain.Matching;

public static class BookMatcher
{
    public static MatchingResult Select(string query, SearchInterpretation interpretation,
        IReadOnlyList<CatalogBook> candidates)
    {
        SearchInterpretationValidator.Validate(query, interpretation);

        var ranked = candidates.GroupBy(book => book.WorkId)
            .Select(group => new BookRanking(group.Key,
                group.SelectMany(book => interpretation.Hypotheses.Select(hypothesis => Classify(hypothesis, book)))
                    .Min()))
            // OrderBy is stable: ties retain the catalog's supplied relevance order.
            .OrderBy(book => book.Tier)
            .ToArray();

        if (interpretation.Hypotheses.All(hypothesis => hypothesis.Intent == SearchIntent.Author)
            || (ranked.Length > 0 && ranked[0].Tier == MatchTier.Author))
            ranked = ranked.Where(book => book.Tier == MatchTier.Author).ToArray();

        var hasEditionConstraint = interpretation.Hypotheses.Any(hypothesis =>
            hypothesis.Year is not null || hypothesis.EditionHints.Length > 0);
        var clearWinner = !hasEditionConstraint && ranked.Length > 0
            && ranked[0].Tier is MatchTier.ExactTitlePrimaryAuthor or MatchTier.ExactTitle
            && (ranked.Length == 1 || ranked[1].Tier != ranked[0].Tier);

        // Edition constraints cannot establish a winner until edition evidence is fetched.
        return new MatchingResult(ranked.Take(clearWinner ? 1 : 5).ToArray(), clearWinner);
    }

    private static MatchTier Classify(SearchHypothesis hypothesis, CatalogBook book)
    {
        var authorMatches = hypothesis.Author is { } author
            && book.Authors.Concat(book.AuthorCredits.Select(credit => credit.Name))
                .Any(name => SameName(author.Value, name) || IsNearMatch(author.Value, name));

        if (hypothesis.Intent == SearchIntent.Author)
            return authorMatches ? MatchTier.Author : MatchTier.Related;

        if (hypothesis.Title is not { } title)
            return MatchTier.Related;

        var exactTitle = hypothesis.Intent == SearchIntent.Title
            && SameText(title.Value, book.Title) && SameText(title.SourceText, book.Title);
        var suppliedAuthor = hypothesis.Author?.SourceText is not null;

        if (exactTitle && suppliedAuthor)
        {
            var matchingCredits = book.AuthorCredits
                .Where(credit => SameName(hypothesis.Author!.Value, credit.Name)
                    && SameName(hypothesis.Author.SourceText!, credit.Name)).ToArray();
            if (matchingCredits.Any(credit => credit.Role == AuthorRole.Primary))
                return MatchTier.ExactTitlePrimaryAuthor;
            if (matchingCredits.Any(credit => credit.Role == AuthorRole.Contributor))
                return MatchTier.ExactTitleContributor;
        }

        if (exactTitle && !suppliedAuthor)
            return MatchTier.ExactTitle;

        // Inferred titles and unresolved author roles can support a candidate, not an exact winner.
        if (authorMatches && IsNearMatch(title.Value, book.Title))
            return MatchTier.TitleAndAuthor;

        if (authorMatches && suppliedAuthor)
            return MatchTier.Author;

        return MatchTier.Related;
    }

    private static bool SameText(string? left, string right)
    {
        var normalized = Normalize(left);
        return normalized.Length > 0 && normalized == Normalize(right);
    }

    private static bool IsNearMatch(string supplied, string catalog)
    {
        var suppliedWords = Normalize(supplied).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var catalogWords = Normalize(catalog).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return suppliedWords.Length > 0 && suppliedWords.All(word => catalogWords.Any(candidate =>
            candidate == word || (word.Length >= 4 && candidate.StartsWith(word, StringComparison.Ordinal))));
    }

    private static bool SameName(string left, string right)
    {
        // Ignore spacing between initials, for example J.K. Rowling and JK Rowling.
        var normalized = Normalize(left).Replace(" ", "");
        return normalized.Length > 0 && normalized == Normalize(right).Replace(" ", "");
    }

    private static string Normalize(string? value)
    {
        var result = new StringBuilder();
        foreach (var character in (value ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                result.Append(char.ToLowerInvariant(character));
            else if (character is not ('\'' or '’'))
                result.Append(' ');
        }

        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
