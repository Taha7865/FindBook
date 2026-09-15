using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using FindBook.Domain.Models;

namespace FindBook.Domain.Validators;

public sealed class SearchInterpretationValidator : ISearchInterpretationValidator
{
    public void Validate(string query, SearchInterpretation interpretation)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1000)
            throw new ValidationException("The query must contain 1 to 1,000 characters.");

        if (interpretation?.Hypotheses is not { Length: >= 1 and <= 2 } hypotheses)
            throw new ValidationException("Return one or two search hypotheses.");

        foreach (var hypothesis in hypotheses)
        {
            if (hypothesis is null || !Enum.IsDefined(hypothesis.Intent))
                throw new ValidationException("Each hypothesis must have a supported intent.");

            ValidateField(query, hypothesis.Title);
            ValidateField(query, hypothesis.Author);
            ValidateList(hypothesis.Keywords, 8);
            ValidateList(hypothesis.EditionHints, 5);

            if (hypothesis.Intent == SearchIntent.Title && hypothesis.Title is null)
                throw new ValidationException("A title search must have a title.");

            if (hypothesis.Intent == SearchIntent.Author
                && (hypothesis.Author?.SourceText is null || hypothesis.Title is not null))
                throw new ValidationException("An author-only search needs a supplied author and no title.");

            if (hypothesis.Intent == SearchIntent.Topic && hypothesis.Keywords.Length == 0)
                throw new ValidationException("A topic search must have keywords.");

            if (hypothesis.Year is { } year && (year is < 1 or > 9999
                || !Regex.IsMatch(query, @"(?<!\d)" + year.ToString(CultureInfo.InvariantCulture) + @"(?!\d)")))
                throw new ValidationException("A requested year must appear in the query.");
        }
    }

    private static void ValidateField(string query, SearchField? field)
    {
        if (field is null)
            return;

        if (string.IsNullOrWhiteSpace(field.Value) || field.Value.Length > 200)
            throw new ValidationException("An extracted field must contain 1 to 200 characters.");

        if (field.SourceText is { } source
            && (string.IsNullOrWhiteSpace(source) || !query.Contains(source, StringComparison.Ordinal)))
            throw new ValidationException("Source text must be copied from the query.");
    }

    private static void ValidateList(string[]? values, int maximumCount)
    {
        if (values is null || values.Length > maximumCount
            || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100))
            throw new ValidationException($"Return at most {maximumCount} nonblank entries of up to 100 characters.");
    }
}
