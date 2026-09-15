using FindBook.Domain.Models;

namespace FindBook.Domain.Validators;

public interface ISearchInterpretationValidator
{
    void Validate(string query, SearchInterpretation interpretation);
}
