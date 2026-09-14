namespace FindBook.Domain.Models;

public sealed record SearchInterpretation(SearchHypothesis[] Hypotheses);

public enum SearchIntent { Title, Author, Topic }

public sealed record SearchHypothesis(
    SearchIntent Intent,
    SearchField? Title,
    SearchField? Author,
    string[] Keywords,
    int? Year,
    string[] EditionHints);

// SourceText is a verbatim fragment of the query, or null for an inferred value.
public sealed record SearchField(string Value, string? SourceText);
